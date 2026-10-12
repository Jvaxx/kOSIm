using System;
using System.Collections.Generic;
using System.Reflection;
using kOS.Safe.Screen;
using NUnit.Framework;

namespace kOS.Safe.Test.Terminal
{
    [TestFixture]
    public class RenderFaultRecoveryTest
    {
        private RenderFaultRecovery recovery;
        private List<Exception> reports;
        private List<int> suppressed;
        private readonly Exception fault = new InvalidOperationException("forced draw failure");

        [SetUp]
        public void Setup()
        {
            recovery = new RenderFaultRecovery();
            reports = new List<Exception>();
            suppressed = new List<int>();
        }

        private void Report(Exception exception, int count)
        {
            reports.Add(exception);
            suppressed.Add(count);
        }

        private bool Fail(double now)
        {
            return recovery.TryRender(now, () => { throw fault; }, exception => true, Report);
        }

        [Test]
        public void HealthyViewRendersEveryPass()
        {
            int draws = 0;
            for (int pass = 0; pass < 100; ++pass)
                Assert.IsTrue(recovery.TryRender(0, () => ++draws, exception => true, Report));
            Assert.AreEqual(100, draws);
            Assert.IsFalse(recovery.IsFaulted);
            Assert.AreEqual(0, reports.Count);
        }

        [Test]
        public void FailureReturnsFallbackSignalAndReportsOriginalException()
        {
            Assert.IsFalse(Fail(0));
            Assert.IsTrue(recovery.IsFaulted);
            Assert.AreSame(fault, reports[0]);
            Assert.AreEqual(0, suppressed[0]);
        }

        [Test]
        public void PersistentFailureDoesNotRunEveryRepaint()
        {
            int draws = 0;
            Action draw = () => { ++draws; throw fault; };
            for (int pass = 0; pass < 200; ++pass)
                Assert.IsFalse(recovery.TryRender(pass / 100.0, draw, exception => true, Report));
            Assert.AreEqual(1, draws);
            Assert.AreEqual(1, reports.Count);
            Assert.IsFalse(recovery.TryRender(2, draw, exception => true, Report));
            Assert.AreEqual(2, draws);
            Assert.AreEqual(1, reports.Count);
        }

        [Test]
        public void PeriodicReportIncludesOnlySuppressedFailedAttempts()
        {
            for (int now = 0; now <= 10; now += 2) Assert.IsFalse(Fail(now));
            Assert.AreEqual(2, reports.Count);
            CollectionAssert.AreEqual(new[] { 0, 4 }, suppressed);
            Assert.AreEqual(0, recovery.SuppressedFailures);
        }

        [Test]
        public void ChangingErrorsAndExplicitRetriesShareOneReportBudget()
        {
            Assert.IsFalse(Fail(0));
            for (int i = 0; i < 100; ++i)
            {
                recovery.RequestRetry();
                Exception different = new ArgumentException("different fault " + i);
                Assert.IsFalse(recovery.TryRender(1, () => { throw different; }, exception => true, Report));
            }
            Assert.AreEqual(1, reports.Count);
            Assert.AreEqual(100, recovery.SuppressedFailures);
        }

        [Test]
        public void SuccessClearsFallbackButDoesNotResetReportBudget()
        {
            Assert.IsFalse(Fail(0));
            Assert.IsTrue(recovery.TryRender(2, () => { }, exception => true, Report));
            Assert.IsFalse(recovery.IsFaulted);
            Assert.IsFalse(Fail(2.1));
            Assert.AreEqual(1, reports.Count);
            Assert.AreEqual(1, recovery.SuppressedFailures);
        }

        [Test]
        public void ExplicitRetryCanRecoverBeforeCooldownExpires()
        {
            Assert.IsFalse(Fail(0));
            recovery.RequestRetry();
            Assert.IsTrue(recovery.TryRender(0.1, () => { }, exception => true, Report));
            Assert.IsFalse(recovery.IsFaulted);
            Assert.AreEqual(1, reports.Count);
        }

        [Test]
        public void ExplicitRetryFailureRearmsCooldown()
        {
            Assert.IsFalse(Fail(0));
            recovery.RequestRetry();
            Assert.IsFalse(Fail(0.1));
            bool drew = false;
            Assert.IsFalse(recovery.TryRender(0.2, () => drew = true, exception => true, Report));
            Assert.IsFalse(drew);
            Assert.AreEqual(1, reports.Count);
        }

        private class SimulatedGuiExitException : Exception { }

        [TestCase(false)]
        [TestCase(true)]
        public void GuiExitLikeControlFlowIsRethrownWithoutFaultState(bool wrapped)
        {
            Exception exit = new SimulatedGuiExitException();
            Exception thrown = wrapped ? new TargetInvocationException(exit) : exit;
            Exception caught = null;
            try
            {
                recovery.TryRender(0, () => { throw thrown; },
                    exception => !(exception.GetBaseException() is SimulatedGuiExitException), Report);
            }
            catch (Exception exception) { caught = exception; }
            Assert.AreSame(thrown, caught);
            Assert.IsFalse(recovery.IsFaulted);
            Assert.AreEqual(0, reports.Count);
        }

        [TestCase(typeof(OutOfMemoryException))]
        [TestCase(typeof(StackOverflowException))]
        [TestCase(typeof(AccessViolationException))]
        [TestCase(typeof(AppDomainUnloadedException))]
        public void FatalErrorsAreNeverRecoveredEvenIfCallerAllowsThem(Type exceptionType)
        {
            var fatal = (Exception)Activator.CreateInstance(exceptionType);
            // Catch base Exception manually because NUnit 2 Assert.Throws requires exact types.
            Exception caught = null;
            try { recovery.TryRender(0, () => { throw fatal; }, exception => true, Report); }
            catch (Exception exception) { caught = exception; }
            Assert.AreSame(fatal, caught);
            Assert.IsFalse(recovery.IsFaulted);
            Assert.AreEqual(0, reports.Count);
        }

        [Test]
        public void WrappedFatalErrorIsAlsoRethrown()
        {
            var wrapper = new TargetInvocationException(new OutOfMemoryException());
            Assert.Throws<TargetInvocationException>(() => recovery.TryRender(0,
                () => { throw wrapper; }, exception => true, Report));
            Assert.AreEqual(0, reports.Count);
        }

        [Test]
        public void FatalErrorsInAnyAggregateBranchAreRethrown()
        {
            var aggregate = new AggregateException(fault, new OutOfMemoryException());
            Assert.Throws<AggregateException>(() => recovery.TryRender(0,
                () => { throw aggregate; }, exception => true, Report));
            Assert.AreEqual(0, reports.Count);
        }

        [Test]
        public void PreparationAndWindowDrawingShareOneCooldownAndReportBudget()
        {
            int preparations = 0;
            int draws = 0;
            int fallbacks = 0;
            for (int pass = 0; pass < 5; ++pass)
            {
                recovery.TryRender(pass, () => ++preparations, exception => true, Report);
                if (recovery.IsFaulted || !recovery.TryRender(pass,
                    () => { ++draws; throw fault; }, exception => true, Report))
                    ++fallbacks;
            }
            Assert.AreEqual(3, preparations);
            Assert.AreEqual(3, draws);
            Assert.AreEqual(5, fallbacks);
            Assert.AreEqual(1, reports.Count);
        }

        [Test]
        public void FailedPreparationDoesNotAttemptWindowDrawing()
        {
            int draws = 0;
            recovery.TryRender(0, () => { throw fault; }, exception => true, Report);
            bool fallback = recovery.IsFaulted || !recovery.TryRender(0,
                () => ++draws, exception => true, Report);
            Assert.IsTrue(fallback);
            Assert.AreEqual(0, draws);
            Assert.AreEqual(1, reports.Count);
        }

        [Test]
        public void ReportingFailurePropagatesWithoutRecursiveLoggingAndKeepsBudget()
        {
            int logs = 0;
            var reportFault = new ApplicationException("logger failure");
            Action<Exception, int> report = (exception, count) => { ++logs; throw reportFault; };
            Assert.AreSame(reportFault, Assert.Throws<ApplicationException>(() => recovery.TryRender(0,
                () => { throw fault; }, exception => true, report)));
            Assert.IsTrue(recovery.IsFaulted);
            Assert.IsFalse(recovery.TryRender(1, () => { throw fault; }, exception => true, report));
            recovery.RequestRetry();
            Assert.IsFalse(recovery.TryRender(1, () => { throw fault; }, exception => true, report));
            Assert.AreEqual(1, logs);
        }

        [Test]
        public void RecoveryStateIsIndependentPerView()
        {
            Assert.IsFalse(Fail(0));
            var other = new RenderFaultRecovery();
            Assert.IsTrue(other.TryRender(0, () => { }, exception => true, Report));
            Assert.IsFalse(other.IsFaulted);
            Assert.IsTrue(recovery.IsFaulted);
        }

        private class FaultyLine : ScreenBufferLine
        {
            public bool ThrowOnRead = true;
            public FaultyLine() : base(4) { this[0] = 'X'; }
            public override string ToString()
            {
                if (ThrowOnRead) throw new InvalidOperationException("broken snapshot row");
                return base.ToString();
            }
        }

        [Test]
        public void ForcedSnapshotReadFaultUnwindsCleanupAndAllowsContinuationWithoutClearingModel()
        {
            var screen = new ScreenBuffer();
            screen.SetSize(3, 4);
            screen.ClearScreen();
            var snapshot = new ScreenSnapShot(screen);
            var line = new FaultyLine();
            snapshot.Buffer[0] = line;
            int groupDepth = 0;
            string color = "original";
            int fallbackPasses = 0;
            Action draw = () =>
            {
                ++groupDepth;
                try
                {
                    color = "drawing";
                    snapshot.Buffer[0].ToString();
                }
                finally { color = "original"; --groupDepth; }
            };
            for (int pass = 0; pass < 2; ++pass)
                if (!recovery.TryRender(pass, draw, exception => true, Report))
                {
                    // The real UI also keeps fallback controls outside the recovered drawing action.
                    Assert.AreEqual(0, groupDepth);
                    Assert.AreEqual("original", color);
                    ++fallbackPasses;
                }
            Assert.AreEqual(2, fallbackPasses);
            Assert.AreSame(line, snapshot.Buffer[0]);
            Assert.AreEqual('X', line[0]);
            line.ThrowOnRead = false;
            Assert.IsTrue(recovery.TryRender(2, draw, exception => true, Report));
            Assert.IsFalse(recovery.IsFaulted);
        }

        [TestCase(0, 10)]
        [TestCase(-1, 10)]
        [TestCase(double.NaN, 10)]
        [TestCase(double.PositiveInfinity, 10)]
        [TestCase(2, 0)]
        [TestCase(2, -1)]
        [TestCase(2, double.NaN)]
        [TestCase(2, double.PositiveInfinity)]
        public void InvalidIntervalsAreRejected(double retrySeconds, double reportSeconds)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RenderFaultRecovery(retrySeconds, reportSeconds));
        }

        [TestCase(-1)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void InvalidClockValuesAreRejected(double now)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => recovery.TryRender(now, () => { }, exception => true, Report));
        }

        [Test]
        public void CustomIntervalsAreHonored()
        {
            recovery = new RenderFaultRecovery(0.5, 1);
            Assert.IsFalse(Fail(0));
            Assert.IsFalse(Fail(0.5));
            Assert.IsFalse(Fail(1));
            Assert.AreEqual(2, reports.Count);
            CollectionAssert.AreEqual(new[] { 0, 1 }, suppressed);
        }
    }
}
