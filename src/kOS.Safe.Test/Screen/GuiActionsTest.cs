using System;
using System.Collections.Generic;
using kOS.Safe.Screen;
using NUnit.Framework;

namespace kOS.Safe.Test.Terminal
{
    /// <summary>
    /// WIDGET-02: a user's GUI action delayed by communications must still be seen as
    /// GUI-caused when it is finally delivered, exactly once.
    /// </summary>
    [TestFixture]
    public class GuiActionsTest
    {
        private GuiCause cause;
        private DelayedGuiActions queue;
        private List<string> log;

        [SetUp]
        public void SetUp()
        {
            cause = new GuiCause();
            queue = new DelayedGuiActions();
            log = new List<string>();
        }

        /// <summary>What a widget's change does: record whether it counts as GUI-caused.</summary>
        private Action Change(string name)
        {
            return () => log.Add(name + (cause.IsGuiCaused ? ":gui" : ":script"));
        }

        /// <summary>The widget's Communicate(): run now, or hold for the delay.</summary>
        private void Communicate(object widget, Action change, double delay, double now)
        {
            Action marked = cause.Mark(change);
            if (delay > 0)
                queue.Enqueue(widget, "w", marked, now + delay);
            else
                marked();
        }

        [Test]
        public void UpstreamLostTheOriginOfADelayedAction()
        {
            // The upstream flag was set only while Communicate() ran.
            bool flag = false;
            Action change = () => log.Add(flag ? "gui" : "script");
            flag = true;
            queue.Enqueue("w", "w", change, 5);
            flag = false;
            queue.DeliverDue(5);
            CollectionAssert.AreEqual(new[] { "script" }, log);
        }

        [Test]
        public void AUserActionIsGuiCausedWithAndWithoutDelay()
        {
            Communicate("w", Change("now"), 0, 0);
            Communicate("w", Change("later"), 2, 0);
            Assert.IsFalse(cause.IsGuiCaused, "the origin leaked outside the action");
            queue.DeliverDue(2);
            CollectionAssert.AreEqual(new[] { "now:gui", "later:gui" }, log);
        }

        [Test]
        public void AScriptChangeIsNotGuiCaused()
        {
            Change("script")();
            CollectionAssert.AreEqual(new[] { "script:script" }, log);
        }

        [Test]
        public void NestingAndExceptionsRestoreTheOrigin()
        {
            cause.Run(() =>
            {
                cause.Run(() => { });
                Assert.IsTrue(cause.IsGuiCaused, "an inner action ended the outer one's origin");
            });
            Assert.Throws<InvalidOperationException>(() => cause.Run(() => { throw new InvalidOperationException(); }));
            Assert.IsFalse(cause.IsGuiCaused, "a failing action left later script changes marked as GUI-caused");
        }

        [Test]
        public void ActionsAreDeliveredInTimeThenArrivalOrderOnlyWhenDue()
        {
            queue.Enqueue("a", "a", () => log.Add("a3"), 3);
            queue.Enqueue("b", "b", () => log.Add("b1"), 1);
            queue.Enqueue("c", "c", () => log.Add("c3"), 3);
            queue.Enqueue("d", "d", () => log.Add("d2"), 2);
            Assert.AreEqual("b", queue.Next.Owner);

            Assert.AreEqual(0, queue.DeliverDue(0.5));
            Assert.AreEqual(2, queue.DeliverDue(2));
            Assert.AreEqual(2, queue.DeliverDue(10));
            CollectionAssert.AreEqual(new[] { "b1", "d2", "a3", "c3" }, log);
            Assert.IsNull(queue.Next);
        }

        [Test]
        public void EachActionIsDeliveredOnce()
        {
            queue.Enqueue("w", "w", () => log.Add("x"), 1);
            queue.DeliverDue(1);
            queue.DeliverDue(2);
            CollectionAssert.AreEqual(new[] { "x" }, log);
        }

        [Test]
        public void AWidgetDisposedBeforeDeliveryGetsNothing()
        {
            var gone = new object();
            var kept = new object();
            queue.Enqueue(gone, "g", () => log.Add("gone"), 1);
            queue.Enqueue(kept, "k", () => log.Add("kept"), 1);
            queue.Enqueue(gone, "g", () => log.Add("gone2"), 2);
            queue.Remove(gone);
            queue.DeliverDue(5);
            CollectionAssert.AreEqual(new[] { "kept" }, log);
        }

        [Test]
        public void AFailingActionIsNotRetriedAndTheRestFollow()
        {
            // Upstream ran the action before removing it, so a throwing action stayed first
            // and was retried every update, blocking everything behind it.
            queue.Enqueue("a", "a", () => { log.Add("fail"); throw new InvalidOperationException(); }, 1);
            queue.Enqueue("b", "b", () => log.Add("b"), 1);
            Assert.Throws<InvalidOperationException>(() => queue.DeliverDue(1));
            Assert.AreEqual(1, queue.DeliverDue(1));
            CollectionAssert.AreEqual(new[] { "fail", "b" }, log);
        }

        [Test]
        public void AnActionQueuedDuringDeliveryDoesNotDisplaceAnother()
        {
            // Upstream removed index 0 after running the action; an earlier-timed action queued
            // by it would have been removed undelivered instead.
            queue.Enqueue("a", "a", () => { log.Add("a"); queue.Enqueue("n", "n", () => log.Add("n"), 0.5); }, 1);
            queue.Enqueue("b", "b", () => log.Add("b"), 1);
            queue.DeliverDue(1);
            CollectionAssert.AreEqual(new[] { "a", "n", "b" }, log);
            Assert.AreEqual(0, queue.Count);
        }

        [Test]
        public void ADelayedActionQueuedByADeliveredOneWaits()
        {
            queue.Enqueue("a", "a", () => { log.Add("a"); queue.Enqueue("n", "n", () => log.Add("n"), 3); }, 1);
            queue.DeliverDue(1);
            CollectionAssert.AreEqual(new[] { "a" }, log);
            Assert.AreEqual(3, queue.Next.Time);
            queue.DeliverDue(3);
            CollectionAssert.AreEqual(new[] { "a", "n" }, log);
        }

        [Test]
        public void NullArgumentsAreRejected()
        {
            Assert.Throws<ArgumentNullException>(() => cause.Run(null));
            Assert.Throws<ArgumentNullException>(() => cause.Mark(null));
            Assert.Throws<ArgumentNullException>(() => queue.Enqueue("w", "w", null, 1));
            queue.Enqueue("w", null, () => { }, 1);
            Assert.AreEqual("", queue.Next.Reason);
        }
    }
}
