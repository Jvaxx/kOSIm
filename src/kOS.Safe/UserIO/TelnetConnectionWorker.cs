using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace kOS.Safe.UserIO
{
    public enum TelnetStopReason
    {
        None,
        /// <summary>The client closed its side (a zero-byte read).</summary>
        ClientClosed,
        ReadFailed,
        WriteFailed,
        /// <summary>A handler callback threw; the exception was reported first.</summary>
        WorkerFault,
        /// <summary>kOS decided to end the session (hung client, DIE output, quit, shutdown).</summary>
        ServerRequested
    }

    /// <summary>
    /// Worker-thread callbacks. They run on the connection's own threads and must not
    /// call Unity or KSP; anything game-side has to wait for the owning thread to poll.
    /// </summary>
    public interface ITelnetConnectionHandler
    {
        /// <summary>Reader thread: events parsed from one read, in stream order.</summary>
        void OnInput(List<TelnetInputEvent> events, bool moreDataAvailable);

        /// <summary>Writer thread, called repeatedly. Return true if output was sent.</summary>
        bool OnWriterTick();

        /// <summary>Either worker thread: a callback threw. The session stops afterwards.</summary>
        void OnWorkerFault(Exception exception);
    }

    /// <summary>
    /// Owns one telnet client's socket, reader/writer threads and input parser.
    /// <br/>
    /// Workers never end the session themselves: they call <see cref="RequestStop"/>,
    /// and the owning (game) thread polls <see cref="StopRequested"/> and calls
    /// <see cref="Shutdown"/>. Shutdown is cooperative, idempotent and bounded: it closes
    /// the socket to unblock reads and joins the threads with a timeout instead of
    /// aborting them. Threads are background threads, so a worker that does not exit in
    /// time cannot keep the game process alive.
    /// </summary>
    public sealed class TelnetConnectionWorker
    {
        public const int DEFAULT_WRITE_TIMEOUT_MS = 5000;

        // Writer polling backoff, as in the original server: run without sleeping while
        // there is output, slow down to MAX when idle (ticks also drive keepalive checks).
        private const int WRITER_SLEEP_INC_MS = 20;
        private const int WRITER_SLEEP_MAX_MS = 200;

        private readonly TcpClient client;
        private readonly NetworkStream stream;
        private readonly ITelnetConnectionHandler handler;
        private readonly TelnetInputParser parser = new TelnetInputParser();
        private readonly object writeLock = new object();
        private readonly ManualResetEvent cancelled = new ManualResetEvent(false);
        private readonly Thread readerThread;
        private readonly Thread writerThread;

        private int started;
        private int shutdownStarted;
        private int eventClosed;
        private volatile bool isCancelled;
        private volatile bool writeFailed;
        private int stopReason; // TelnetStopReason, first reason wins

        public TelnetConnectionWorker(TcpClient client, ITelnetConnectionHandler handler, string name,
            int writeTimeoutMilliseconds = DEFAULT_WRITE_TIMEOUT_MS)
        {
            if (client == null) throw new ArgumentNullException("client");
            if (handler == null) throw new ArgumentNullException("handler");
            this.client = client;
            this.handler = handler;
            // A client that stops reading must not block senders (including the game
            // thread) forever once its receive window fills up:
            client.SendTimeout = writeTimeoutMilliseconds;
            stream = client.GetStream();
            readerThread = new Thread(ReadLoop) { IsBackground = true, Name = name + " reader" };
            writerThread = new Thread(WriteLoop) { IsBackground = true, Name = name + " writer" };
        }

        public TelnetStopReason StopReason
        {
            get { return (TelnetStopReason)Thread.VolatileRead(ref stopReason); }
        }

        public bool StopRequested { get { return StopReason != TelnetStopReason.None; } }

        /// <summary>True once a write failed; further sends are skipped.</summary>
        public bool IsWriteDead { get { return writeFailed; } }

        public bool IsShutdown { get { return Thread.VolatileRead(ref shutdownStarted) != 0; } }

        public bool ReaderAlive { get { return readerThread.IsAlive; } }
        public bool WriterAlive { get { return writerThread.IsAlive; } }

        public void Start()
        {
            if (Interlocked.Exchange(ref started, 1) != 0)
                throw new InvalidOperationException("Telnet connection already started.");
            if (IsShutdown)
                return;
            readerThread.Start();
            writerThread.Start();
        }

        /// <summary>
        /// Ask the owning thread to end the session. Thread-safe; the first reason is kept.
        /// </summary>
        public void RequestStop(TelnetStopReason reason)
        {
            if (reason == TelnetStopReason.None) throw new ArgumentOutOfRangeException("reason");
            Interlocked.CompareExchange(ref stopReason, (int)reason, (int)TelnetStopReason.None);
        }

        /// <summary>
        /// Send bytes to the client. Thread-safe; whole buffers are never interleaved.
        /// Returns false if the connection is shutting down or the write failed, in which
        /// case a stop is requested.
        /// </summary>
        public bool Send(byte[] buffer)
        {
            return TrySend(buffer, Timeout.Infinite);
        }

        /// <summary>
        /// Like <see cref="Send"/>, but gives up if another sender holds the connection for
        /// longer than <paramref name="lockTimeoutMilliseconds"/> (e.g. blocked on a
        /// stalled client).
        /// </summary>
        public bool TrySend(byte[] buffer, int lockTimeoutMilliseconds)
        {
            if (buffer == null) throw new ArgumentNullException("buffer");
            if (isCancelled || writeFailed)
                return false;
            if (!Monitor.TryEnter(writeLock, lockTimeoutMilliseconds))
                return false;
            try
            {
                if (isCancelled || writeFailed)
                    return false;
                stream.Write(buffer, 0, buffer.Length);
                return true;
            }
            catch (Exception e)
            {
                if (!IsConnectionException(e))
                    throw;
                writeFailed = true;
                if (!isCancelled)
                    RequestStop(TelnetStopReason.WriteFailed);
                return false;
            }
            finally
            {
                Monitor.Exit(writeLock);
            }
        }

        /// <summary>
        /// End the session: stop workers, close the socket and wait up to
        /// <paramref name="joinTimeout"/> for both threads. Safe to call more than once and
        /// from several threads; a worker calling it does not wait for itself.
        /// Returns true if no worker thread is still running.
        /// </summary>
        public bool Shutdown(TimeSpan joinTimeout)
        {
            if (Interlocked.Exchange(ref shutdownStarted, 1) == 0)
            {
                RequestStop(TelnetStopReason.ServerRequested);
                isCancelled = true;
                cancelled.Set();
                // Shutdown makes a blocked receive return on platforms where Close alone
                // might not; either may throw if the client is already gone.
                try { client.Client.Shutdown(SocketShutdown.Both); }
                catch (Exception e) { if (!IsConnectionException(e)) throw; }
                try { client.Close(); }
                catch (Exception e) { if (!IsConnectionException(e)) throw; }
            }

            DateTime deadline = DateTime.UtcNow + joinTimeout;
            bool readerDone = JoinUntil(readerThread, deadline);
            bool writerDone = JoinUntil(writerThread, deadline);
            // Once neither thread can wait on it any more (P2-3, LIFE-03). A worker calling this
            // is still running, and a thread that did not exit in time keeps it.
            if (!readerThread.IsAlive && !writerThread.IsAlive && Interlocked.Exchange(ref eventClosed, 1) == 0)
                cancelled.Close();
            return readerDone && writerDone;
        }

        /// <summary>True once <see cref="Shutdown"/> has closed the wait handle the writer sleeps on (tests).</summary>
        internal bool IsEventClosed
        {
            get
            {
                try { cancelled.WaitOne(0); return false; }
                catch (ObjectDisposedException) { return true; }
            }
        }

        private static bool JoinUntil(Thread thread, DateTime deadline)
        {
            if (thread == Thread.CurrentThread)
                return true; // exits as soon as the caller returns to its loop
            if ((thread.ThreadState & ThreadState.Unstarted) != 0) // also flagged Background
                return true;
            TimeSpan remaining = deadline - DateTime.UtcNow;
            return thread.Join(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
        }

        private void ReadLoop()
        {
            var buffer = new byte[4096];
            while (!isCancelled)
            {
                int numRead;
                try
                {
                    numRead = stream.Read(buffer, 0, buffer.Length); // blocks while the client is idle
                }
                catch (Exception e)
                {
                    if (!IsConnectionException(e))
                        Fault(e);
                    else if (!isCancelled)
                        RequestStop(TelnetStopReason.ReadFailed);
                    return;
                }

                if (isCancelled)
                    return;

                if (numRead == 0)
                {
                    Deliver(parser.Finish(), false);
                    RequestStop(TelnetStopReason.ClientClosed);
                    return;
                }

                bool moreDataAvailable;
                try
                {
                    moreDataAvailable = stream.DataAvailable;
                }
                catch (Exception e)
                {
                    if (!IsConnectionException(e))
                    {
                        Fault(e);
                        return;
                    }
                    moreDataAvailable = false;
                }
                if (!Deliver(parser.Feed(buffer, 0, numRead), moreDataAvailable))
                    return;
            }
        }

        private bool Deliver(List<TelnetInputEvent> events, bool moreDataAvailable)
        {
            try
            {
                handler.OnInput(events, moreDataAvailable);
                return true;
            }
            catch (ThreadAbortException)
            {
                throw;
            }
            catch (Exception e)
            {
                Fault(e);
                return false;
            }
        }

        private void WriteLoop()
        {
            int sleepTime = WRITER_SLEEP_MAX_MS;
            while (!isCancelled)
            {
                bool sentSomething;
                try
                {
                    sentSomething = handler.OnWriterTick();
                }
                catch (ThreadAbortException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    Fault(e);
                    return;
                }

                if (sentSomething)
                {
                    sleepTime = 0;
                }
                else
                {
                    if (cancelled.WaitOne(sleepTime))
                        return;
                    if (sleepTime < WRITER_SLEEP_MAX_MS)
                        sleepTime += WRITER_SLEEP_INC_MS;
                }
            }
        }

        private void Fault(Exception e)
        {
            RequestStop(TelnetStopReason.WorkerFault);
            try
            {
                handler.OnWorkerFault(e);
            }
            catch (ThreadAbortException)
            {
                throw;
            }
            catch (Exception)
            {
                // Nowhere left to report it, and an exception escaping a worker thread
                // could take the whole game down. The stop request is already recorded.
            }
        }

        private static bool IsConnectionException(Exception e)
        {
            return e is IOException || e is ObjectDisposedException || e is SocketException ||
                   e is InvalidOperationException; // NetworkStream on a socket that is no longer connected
        }
    }
}
