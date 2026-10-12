using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using kOS.Safe.UserIO;
using NUnit.Framework;

namespace kOS.Safe.Test.UserIO
{
    /// <summary>
    /// Real loopback sockets on the host runtime. These cover worker shutdown and I/O
    /// behavior; they do not validate Unity/Mono-on-Proton socket semantics.
    /// </summary>
    [TestFixture]
    public class TelnetConnectionWorkerTest
    {
        private static readonly TimeSpan JoinTimeout = TimeSpan.FromSeconds(5);

        private class RecordingHandler : ITelnetConnectionHandler
        {
            private readonly object sync = new object();
            private readonly StringBuilder text = new StringBuilder();
            private readonly List<TelnetInputEvent> events = new List<TelnetInputEvent>();
            public readonly List<Exception> Faults = new List<Exception>();
            public Func<bool> WriterTick = () => false;
            public Action<List<TelnetInputEvent>> InputHook = _ => { };
            public int WriterTicks;

            public void OnInput(List<TelnetInputEvent> batch, bool moreDataAvailable)
            {
                InputHook(batch);
                lock (sync)
                {
                    events.AddRange(batch);
                    foreach (TelnetInputEvent ev in batch.Where(e => e.Kind == TelnetInputEventKind.Text))
                        text.Append(ev.Text);
                }
            }

            public bool OnWriterTick()
            {
                Interlocked.Increment(ref WriterTicks);
                return WriterTick();
            }

            public void OnWorkerFault(Exception exception)
            {
                lock (sync)
                    Faults.Add(exception);
            }

            public string Text { get { lock (sync) return text.ToString(); } }
            public List<TelnetInputEvent> Events { get { lock (sync) return events.ToList(); } }
        }

        private TcpListener listener;
        private readonly List<IDisposable> cleanup = new List<IDisposable>();
        private readonly List<TelnetConnectionWorker> workers = new List<TelnetConnectionWorker>();

        [SetUp]
        public void SetUp()
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (TelnetConnectionWorker worker in workers)
                worker.Shutdown(JoinTimeout);
            foreach (IDisposable disposable in cleanup)
                disposable.Dispose();
            workers.Clear(); // NUnit reuses the fixture instance between tests
            cleanup.Clear();
            listener.Stop();
        }

        /// <summary>Returns (peer = the "telnet client", server-side socket for the worker).</summary>
        private TcpClient Connect(out TcpClient serverSide)
        {
            var peer = new TcpClient();
            peer.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
            serverSide = listener.AcceptTcpClient();
            cleanup.Add(peer);
            cleanup.Add(serverSide);
            return peer;
        }

        private TelnetConnectionWorker CreateWorker(TcpClient serverSide, ITelnetConnectionHandler handler, int writeTimeoutMs = TelnetConnectionWorker.DEFAULT_WRITE_TIMEOUT_MS)
        {
            var worker = new TelnetConnectionWorker(serverSide, handler, "test telnet", writeTimeoutMs);
            workers.Add(worker);
            return worker;
        }

        private static void WaitFor(Func<bool> condition, string what, int timeoutMs = 5000)
        {
            var watch = Stopwatch.StartNew();
            while (!condition())
            {
                if (watch.ElapsedMilliseconds > timeoutMs)
                    Assert.Fail("Timed out waiting for " + what);
                Thread.Sleep(5);
            }
        }

        private static void Send(TcpClient peer, params byte[] bytes)
        {
            peer.GetStream().Write(bytes, 0, bytes.Length);
            peer.GetStream().Flush();
        }

        [Test, Timeout(20000)]
        public void InputSplitAcrossSocketWritesIsReassembled()
        {
            TcpClient serverSide;
            TcpClient peer = Connect(out serverSide);
            var handler = new RecordingHandler();
            TelnetConnectionWorker worker = CreateWorker(serverSide, handler);
            worker.Start();

            // "é" split mid-character, NAWS split mid-subnegotiation, separate TCP writes:
            Send(peer, 0x61, 0xC3);
            Thread.Sleep(50);
            Send(peer, 0xA9, TelnetInputParser.IAC, TelnetInputParser.SB, TelnetInputParser.OPTION_NAWS, 0);
            Thread.Sleep(50);
            Send(peer, 100, 0, 40, TelnetInputParser.IAC, TelnetInputParser.SE, 0x62);

            WaitFor(() => handler.Text == "aéb", "reassembled text");
            WaitFor(() => handler.Events.Any(e => e.Kind == TelnetInputEventKind.WindowSize), "resize");
            TelnetInputEvent resize = handler.Events.Single(e => e.Kind == TelnetInputEventKind.WindowSize);
            Assert.AreEqual(100, resize.Width);
            Assert.AreEqual(40, resize.Height);
            Assert.IsFalse(worker.StopRequested);
        }

        [Test, Timeout(20000)]
        public void ClientCloseEndsTheReaderInsteadOfSpinning()
        {
            TcpClient serverSide;
            TcpClient peer = Connect(out serverSide);
            var handler = new RecordingHandler();
            TelnetConnectionWorker worker = CreateWorker(serverSide, handler);
            worker.Start();

            Send(peer, 0x78, 0xE2, 0x82); // 'x' then an unfinished character
            WaitFor(() => handler.Text == "x", "first text");
            peer.Client.Shutdown(SocketShutdown.Send);

            WaitFor(() => worker.StopRequested, "stop request");
            Assert.AreEqual(TelnetStopReason.ClientClosed, worker.StopReason);
            WaitFor(() => !worker.ReaderAlive, "reader exit");
            Assert.AreEqual("x�", handler.Text);
            Assert.IsTrue(worker.WriterAlive, "the owning thread decides when to shut down");

            Assert.IsTrue(worker.Shutdown(JoinTimeout));
            Assert.IsFalse(worker.WriterAlive);
        }

        [Test, Timeout(20000)]
        public void ShutdownUnblocksAPendingReadAndClosesTheConnection()
        {
            TcpClient serverSide;
            TcpClient peer = Connect(out serverSide);
            var handler = new RecordingHandler();
            TelnetConnectionWorker worker = CreateWorker(serverSide, handler);
            worker.Start();
            WaitFor(() => handler.WriterTicks > 0, "writer running");
            Thread.Sleep(50); // let the reader block in Read

            var watch = Stopwatch.StartNew();
            Assert.IsTrue(worker.Shutdown(JoinTimeout));
            Assert.Less(watch.ElapsedMilliseconds, 2000, "shutdown should not wait for timeouts");
            Assert.IsFalse(worker.ReaderAlive);
            Assert.IsFalse(worker.WriterAlive);
            Assert.AreEqual(TelnetStopReason.ServerRequested, worker.StopReason);
            CollectionAssert.IsEmpty(handler.Faults);

            peer.ReceiveTimeout = 5000;
            Assert.AreEqual(0, peer.GetStream().Read(new byte[16], 0, 16), "client sees end of stream");
            Assert.IsFalse(worker.Send(new byte[] { 1 }), "no sends after shutdown");
        }

        [Test, Timeout(20000)]
        public void ShutdownIsIdempotentAndSafeFromManyThreadsAtOnce()
        {
            TcpClient serverSide;
            Connect(out serverSide);
            TelnetConnectionWorker worker = CreateWorker(serverSide, new RecordingHandler());
            worker.Start();

            var results = new bool[8];
            var errors = new List<Exception>();
            var start = new ManualResetEvent(false);
            Thread[] threads = Enumerable.Range(0, results.Length).Select(i => new Thread(() =>
            {
                start.WaitOne();
                try { results[i] = worker.Shutdown(JoinTimeout); }
                catch (Exception e) { lock (errors) errors.Add(e); }
            })).ToArray();
            foreach (Thread t in threads) t.Start();
            start.Set();
            foreach (Thread t in threads) t.Join();

            CollectionAssert.IsEmpty(errors);
            CollectionAssert.AreEqual(Enumerable.Repeat(true, results.Length), results);
            Assert.IsTrue(worker.Shutdown(JoinTimeout));
        }

        [Test, Timeout(20000)]
        public void ShutdownFromAWorkerThreadDoesNotDeadlock()
        {
            TcpClient serverSide;
            Connect(out serverSide);
            var handler = new RecordingHandler();
            TelnetConnectionWorker worker = CreateWorker(serverSide, handler);
            bool? resultFromWriter = null;
            handler.WriterTick = () =>
            {
                resultFromWriter = worker.Shutdown(JoinTimeout);
                return false;
            };
            worker.Start();

            WaitFor(() => resultFromWriter.HasValue, "writer-initiated shutdown");
            Assert.IsTrue(resultFromWriter.Value);
            Assert.IsTrue(worker.Shutdown(JoinTimeout));
            Assert.IsFalse(worker.WriterAlive);
        }

        [Test, Timeout(20000)]
        public void ShutdownGivesUpOnAStalledWorkerWithinTheTimeout()
        {
            TcpClient serverSide;
            Connect(out serverSide);
            var handler = new RecordingHandler();
            var release = new ManualResetEvent(false);
            var stalled = new ManualResetEvent(false);
            handler.WriterTick = () =>
            {
                stalled.Set();
                release.WaitOne(); // ignores cancellation, like a stuck callback
                return false;
            };
            TelnetConnectionWorker worker = CreateWorker(serverSide, handler);
            worker.Start();
            Assert.IsTrue(stalled.WaitOne(5000));

            var watch = Stopwatch.StartNew();
            Assert.IsFalse(worker.Shutdown(TimeSpan.FromMilliseconds(200)));
            Assert.Less(watch.ElapsedMilliseconds, 2000);
            Assert.IsTrue(worker.WriterAlive);
            Assert.IsFalse(worker.ReaderAlive, "the reader still exits normally");

            release.Set();
            Assert.IsTrue(worker.Shutdown(JoinTimeout), "a later call can confirm the exit");
        }

        [Test, Timeout(20000)]
        public void ShutdownBeforeStartIsCleanAndPreventsStart()
        {
            TcpClient serverSide;
            Connect(out serverSide);
            TelnetConnectionWorker worker = CreateWorker(serverSide, new RecordingHandler());
            Assert.IsTrue(worker.Shutdown(JoinTimeout));
            worker.Start();
            Assert.IsFalse(worker.ReaderAlive);
            Assert.IsFalse(worker.WriterAlive);
            Assert.Throws<InvalidOperationException>(worker.Start);
        }

        [Test, Timeout(20000)]
        public void WriteFailureMarksTheConnectionDeadAndRequestsStop()
        {
            TcpClient serverSide;
            TcpClient peer = Connect(out serverSide);
            TelnetConnectionWorker worker = CreateWorker(serverSide, new RecordingHandler());
            // Not started: only the send path is exercised.
            peer.LingerState = new LingerOption(true, 0); // close with RST
            peer.Close();

            var watch = Stopwatch.StartNew();
            while (worker.Send(new byte[1024]) && watch.ElapsedMilliseconds < 5000)
                Thread.Sleep(10);

            Assert.IsTrue(worker.IsWriteDead);
            Assert.AreEqual(TelnetStopReason.WriteFailed, worker.StopReason);
            Assert.IsFalse(worker.Send(new byte[] { 1 }));
        }

        [Test, Timeout(60000)]
        public void StalledClientCannotBlockSendersIndefinitely()
        {
            TcpClient serverSide;
            TcpClient peer = Connect(out serverSide);
            peer.ReceiveBufferSize = 4096;
            serverSide.SendBufferSize = 4096;
            TelnetConnectionWorker worker = CreateWorker(serverSide, new RecordingHandler(), writeTimeoutMs: 500);

            // The peer never reads. Fill both socket buffers until a send times out.
            var chunk = new byte[64 * 1024];
            var watch = Stopwatch.StartNew();
            int sends = 0;
            while (worker.Send(chunk))
            {
                Assert.Less(++sends, 10000, "socket buffers never filled");
            }
            Assert.Less(watch.ElapsedMilliseconds, 30000);
            Assert.IsTrue(worker.IsWriteDead);
            Assert.AreEqual(TelnetStopReason.WriteFailed, worker.StopReason);
            Assert.IsTrue(worker.Shutdown(JoinTimeout));
            GC.KeepAlive(peer);
        }

        [Test, Timeout(60000)]
        public void TrySendGivesUpWhileAnotherSenderIsBlocked()
        {
            TcpClient serverSide;
            TcpClient peer = Connect(out serverSide);
            peer.ReceiveBufferSize = 4096;
            serverSide.SendBufferSize = 4096;
            TelnetConnectionWorker worker = CreateWorker(serverSide, new RecordingHandler(), writeTimeoutMs: 20000);

            var blockedSender = new Thread(() =>
            {
                var chunk = new byte[64 * 1024];
                while (worker.Send(chunk)) { }
            }) { IsBackground = true };
            blockedSender.Start();

            // Once the buffers are full the sender blocks inside Send, holding the connection.
            var watch = Stopwatch.StartNew();
            bool gaveUp = false;
            while (watch.ElapsedMilliseconds < 15000)
            {
                var attempt = Stopwatch.StartNew();
                if (!worker.TrySend(new byte[] { 0x41 }, 100))
                {
                    Assert.Less(attempt.ElapsedMilliseconds, 1000);
                    gaveUp = true;
                    break;
                }
            }
            Assert.IsTrue(gaveUp, "TrySend never found the connection busy");
            Assert.IsFalse(worker.IsWriteDead);

            // Shutdown must also release the blocked sender.
            watch.Restart();
            Assert.IsTrue(worker.Shutdown(JoinTimeout));
            Assert.IsTrue(blockedSender.Join(5000), "blocked send was not released by shutdown");
            Assert.Less(watch.ElapsedMilliseconds, 10000);
            GC.KeepAlive(peer);
        }

        [Test, Timeout(20000)]
        public void HandlerExceptionIsReportedAndStopsTheSession()
        {
            TcpClient serverSide;
            TcpClient peer = Connect(out serverSide);
            var handler = new RecordingHandler();
            handler.InputHook = _ => { throw new InvalidCastException("boom"); };
            TelnetConnectionWorker worker = CreateWorker(serverSide, handler);
            worker.Start();

            Send(peer, 0x41);
            WaitFor(() => worker.StopRequested, "stop request");
            Assert.AreEqual(TelnetStopReason.WorkerFault, worker.StopReason);
            WaitFor(() => !worker.ReaderAlive, "reader exit");
            Assert.AreEqual(1, handler.Faults.Count);
            Assert.IsInstanceOf<InvalidCastException>(handler.Faults[0]);
            Assert.IsTrue(worker.Shutdown(JoinTimeout));
        }

        [Test]
        public void FirstStopReasonWins()
        {
            TcpClient serverSide;
            Connect(out serverSide);
            TelnetConnectionWorker worker = CreateWorker(serverSide, new RecordingHandler());
            worker.RequestStop(TelnetStopReason.ClientClosed);
            worker.RequestStop(TelnetStopReason.WriteFailed);
            Assert.AreEqual(TelnetStopReason.ClientClosed, worker.StopReason);
            Assert.Throws<ArgumentOutOfRangeException>(() => worker.RequestStop(TelnetStopReason.None));
        }
    }
}
