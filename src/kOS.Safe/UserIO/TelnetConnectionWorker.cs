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
        /// <summary>kOS decided to end the session (hung or stalled client, DIE output, quit, shutdown).</summary>
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
    /// Once the workers run, only the writer thread writes to the socket, because a write
    /// blocks while the client is not reading (up to the write timeout). Every other thread,
    /// the game thread above all, queues bytes with <see cref="Post"/> and can wait a bounded
    /// time for them with <see cref="Flush"/> (R-01).
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

        /// <summary>
        /// Most bytes <see cref="Post"/> keeps waiting for the writer. A client that leaves more
        /// unsent has stopped reading, and its session is ended.
        /// </summary>
        public const int MAX_POSTED_BYTES = 65536;

        // How often a waiting Flush checks that the writer is still there to signal it.
        private const int FLUSH_POLL_MS = 20;

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
        private readonly AutoResetEvent wake = new AutoResetEvent(false); // something was posted or a flush is waiting
        private readonly WaitHandle[] writerWaits; // cancelled first: WaitAny reports the lowest index set
        private readonly Thread readerThread;
        private readonly Thread writerThread;

        private readonly object postSync = new object();
        private readonly Queue<byte[]> posted = new Queue<byte[]>(); // guarded by postSync
        private int postedBytes; // guarded by postSync

        // Flush requests, numbered, and the latest one the writer has completed. Guarded by flushSync.
        private readonly object flushSync = new object();
        private long flushRequested;
        private long flushCompleted;

        private int started;
        private volatile bool workersStarted;
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
            writerWaits = new WaitHandle[] { cancelled, wake };
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
            workersStarted = true;
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
        /// Write bytes to the socket now, for the writer thread's own output (from
        /// <see cref="ITelnetConnectionHandler.OnWriterTick"/>). It blocks while the client is
        /// not reading, up to the write timeout, so once the workers run any other thread must
        /// use <see cref="Post"/>: calling this from one throws. Whole buffers are never
        /// interleaved. Returns false if the connection is shutting down or the write failed,
        /// in which case a stop is requested.
        /// </summary>
        public bool Send(byte[] buffer)
        {
            if (buffer == null) throw new ArgumentNullException("buffer");
            if (workersStarted && Thread.CurrentThread != writerThread)
                throw new InvalidOperationException("Only the telnet writer thread writes to the socket once the connection runs; use Post.");
            if (isCancelled || writeFailed)
                return false;
            lock (writeLock)
            {
                if (isCancelled || writeFailed)
                    return false;
                try
                {
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
            }
        }

        /// <summary>
        /// Queue bytes for the writer thread, which sends them in order, before the handler's
        /// next output. Thread-safe, and never waits for the client, so the game thread can
        /// use it. Returns false if the connection is shutting down or the write failed, or if
        /// the client already left <see cref="MAX_POSTED_BYTES"/> unsent: it is then treated
        /// as stalled and a stop is requested.
        /// </summary>
        public bool Post(byte[] buffer)
        {
            if (buffer == null) throw new ArgumentNullException("buffer");
            if (isCancelled || writeFailed)
                return false;
            lock (postSync)
            {
                if (buffer.Length > MAX_POSTED_BYTES - postedBytes)
                {
                    RequestStop(TelnetStopReason.ServerRequested);
                    return false;
                }
                posted.Enqueue((byte[])buffer.Clone()); // the caller may reuse its array
                postedBytes += buffer.Length;
            }
            Wake();
            return true;
        }

        /// <summary>
        /// Wait until the writer thread has written everything queued before this call: the
        /// posted bytes and the handler's own output (the handler returns false from
        /// <see cref="ITelnetConnectionHandler.OnWriterTick"/> only when it had nothing to
        /// send). Waits at most <paramref name="timeout"/>, and not at all when the connection
        /// is shutting down, a write failed, the writer is not running, or it is the caller.
        /// Returns true if everything was written.
        /// </summary>
        public bool Flush(TimeSpan timeout)
        {
            if (Thread.CurrentThread == writerThread)
                return false; // it would wait for itself
            long request;
            lock (flushSync)
                request = ++flushRequested;
            Wake();

            DateTime deadline = DateTime.UtcNow + timeout;
            lock (flushSync)
            {
                while (flushCompleted < request)
                {
                    if (isCancelled || writeFailed || !writerThread.IsAlive)
                        return false;
                    double remaining = (deadline - DateTime.UtcNow).TotalMilliseconds;
                    if (remaining <= 0)
                        return false;
                    Monitor.Wait(flushSync, (int)Math.Ceiling(Math.Min(remaining, FLUSH_POLL_MS)));
                }
                return true;
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
            {
                cancelled.Close();
                wake.Close();
            }
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
                // Read first: whatever was queued before this request is written below.
                long flushRequest;
                lock (flushSync)
                    flushRequest = flushRequested;

                bool sentSomething;
                try
                {
                    WritePosted();
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
                    CompleteFlush(flushRequest);
                    if (WaitHandle.WaitAny(writerWaits, sleepTime) == 0)
                        return;
                    if (sleepTime < WRITER_SLEEP_MAX_MS)
                        sleepTime += WRITER_SLEEP_INC_MS;
                }
            }
        }

        /// <summary>Writer thread: sends what <see cref="Post"/> queued, in order.</summary>
        private void WritePosted()
        {
            while (true)
            {
                byte[] buffer;
                lock (postSync)
                {
                    if (posted.Count == 0)
                        return;
                    buffer = posted.Dequeue();
                    postedBytes -= buffer.Length;
                }
                if (!Send(buffer))
                {
                    lock (postSync)
                    {
                        posted.Clear();
                        postedBytes = 0;
                    }
                    return;
                }
            }
        }

        private void CompleteFlush(long request)
        {
            lock (flushSync)
            {
                if (request <= flushCompleted)
                    return;
                flushCompleted = request;
                Monitor.PulseAll(flushSync);
            }
        }

        private void Wake()
        {
            try { wake.Set(); }
            catch (ObjectDisposedException) { } // closed once both workers are gone; nothing left to wake
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
