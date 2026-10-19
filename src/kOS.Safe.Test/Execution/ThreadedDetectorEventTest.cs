using System;
using System.Threading;
using kOS.Safe.Execution;
using NSubstitute;
using NUnit.Framework;

namespace kOS.Safe.Test.Execution
{
    /// <summary>
    /// P2-3, LIFE-03: a threaded yield closes its end-of-work event once the main thread has
    /// seen the child thread end, on every path (finished, failed, cancelled).
    /// </summary>
    [TestFixture]
    public class ThreadedDetectorEventTest
    {
        private sealed class Work : YiedFinishedThreadedDetector
        {
            public readonly ManualResetEvent Release = new ManualResetEvent(false);
            public bool Fail;
            public int Finished;
            public int Cancelled;

            public override void ThreadInitialize(SafeSharedObjects shared) { }

            public override void ThreadExecute()
            {
                Release.WaitOne(5000);
                if (Fail)
                    throw new InvalidOperationException("work failed");
            }

            public override void ThreadFinish() { ++Finished; }

            public override void ThreadCancelled() { ++Cancelled; }
        }

        private SafeSharedObjects shared;

        [SetUp]
        public void SetUp()
        {
            shared = new SafeSharedObjects { Cpu = Substitute.For<ICpu>() };
        }

        private static void WaitUntilFinished(Work work)
        {
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!work.IsFinished())
            {
                if (DateTime.UtcNow > deadline)
                    Assert.Fail("the work did not finish");
                Thread.Sleep(1);
            }
        }

        [Test, Timeout(10000)]
        public void FinishedWorkClosesItsEvent()
        {
            var work = new Work();
            work.Begin(shared);
            Assert.IsTrue(work.HoldsThreadEvent);
            Assert.IsFalse(work.IsFinished());

            work.Release.Set();
            WaitUntilFinished(work);

            Assert.AreEqual(1, work.Finished);
            Assert.IsFalse(work.HoldsThreadEvent);
            Assert.IsTrue(work.IsFinished(), "asking again is fine");
        }

        [Test, Timeout(10000)]
        public void FailedWorkClosesItsEvent()
        {
            var work = new Work { Fail = true };
            work.Begin(shared);
            work.Release.Set();

            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            InvalidOperationException error = null;
            while (error == null)
            {
                try { work.IsFinished(); }
                catch (InvalidOperationException e) { error = e; }
                if (DateTime.UtcNow > deadline)
                    Assert.Fail("the work did not fail");
                Thread.Sleep(1);
            }

            Assert.AreEqual("work failed", error.Message);
            Assert.IsFalse(work.HoldsThreadEvent);
            shared.Cpu.Received(1).BreakExecution(false);
        }

        [Test, Timeout(10000)]
        public void CancelledWorkClosesItsEventOnceItEnds()
        {
            var work = new Work();
            work.Begin(shared);

            Assert.IsTrue(work.Cancel(), "still running");
            Assert.IsTrue(work.HoldsThreadEvent, "the thread may still set it");

            work.Release.Set();
            WaitUntilFinished(work);

            Assert.AreEqual(0, work.Finished);
            Assert.AreEqual(1, work.Cancelled);
            Assert.IsFalse(work.HoldsThreadEvent);
        }
    }
}
