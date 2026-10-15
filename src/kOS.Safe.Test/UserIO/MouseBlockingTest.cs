using System;
using System.Collections.Generic;
using kOS.Safe.UserIO;
using NUnit.Framework;

namespace kOS.Safe.Test.UserIO
{
    /// <summary>
    /// INPUT-01 mouse half: when kOS keeps the mouse from the scene under its windows, for the
    /// policy agreed with the user (docs/KOSIm-roadmap.md, "INPUT-01 mouse half"). Each
    /// <see cref="Step"/> is one Unity frame: Update (the policy decides), then OnGUI (the
    /// windows that draw report their rectangles).
    /// </summary>
    [TestFixture]
    public class MouseBlockingTest
    {
        private class RecordingSink : IMouseLockSink
        {
            public readonly List<string> Calls = new List<string>();
            public bool Held;
            public bool FailLock;
            public bool FailUnlock;

            public void Lock()
            {
                if (FailLock)
                    throw new InvalidOperationException("lock refused");
                Calls.Add("lock");
                Held = true;
            }

            public void Unlock()
            {
                if (FailUnlock)
                    throw new InvalidOperationException("unlock refused");
                Calls.Add("unlock");
                Held = false;
            }

            public string Take()
            {
                string result = string.Join(", ", Calls.ToArray());
                Calls.Clear();
                return result;
            }
        }

        private RecordingSink sink;
        private MouseBlocking blocking;
        private MouseBlocking.Area term;
        private MouseBlocking.Area dialog;
        private int frame;

        private static readonly ScreenRect TermRect = new ScreenRect(100, 100, 400, 300);   // x 100..500, y 100..400
        private static readonly ScreenRect DialogRect = new ScreenRect(600, 100, 100, 50); // x 600..700, y 100..150

        private const float InX = 200, InY = 200;      // inside the terminal
        private const float OutX = 50, OutY = 50;      // outside everything
        private const float DialogX = 650, DialogY = 120;

        [SetUp]
        public void SetUp()
        {
            sink = new RecordingSink();
            blocking = new MouseBlocking(sink);
            term = blocking.Register("term");
            dialog = blocking.Register("dialog");
            frame = 1000;
        }

        /// <summary>One frame: update with the mouse state, then the given windows draw.</summary>
        private void Step(float x, float y, bool held, params MouseBlocking.Area[] drawn)
        {
            ++frame;
            blocking.Update(frame, x, y, held);
            foreach (MouseBlocking.Area area in drawn)
                blocking.Show(area, area == dialog ? DialogRect : TermRect, frame);
            Assert.AreEqual(sink.Held, blocking.HoldsLock, "policy and sink disagree");
        }

        private void Hover(float x, float y, params MouseBlocking.Area[] drawn)
        {
            Step(x, y, false, drawn);
        }

        [Test]
        public void NothingShownNeverLocks()
        {
            Hover(InX, InY);
            Hover(InX, InY);
            Step(InX, InY, true);
            Assert.IsFalse(sink.Held);
            Assert.AreEqual("", sink.Take());
        }

        [Test]
        public void HoveringAShownWindowLocksFromTheNextUpdate()
        {
            Hover(InX, InY, term); // drawn now; this update ran before it drew
            Assert.IsFalse(sink.Held);
            Hover(InX, InY, term);
            Assert.IsTrue(sink.Held);
            Assert.AreEqual("lock", sink.Take());
            Hover(InX, InY, term);
            Assert.AreEqual("", sink.Take(), "locked once, not every frame");
        }

        [Test]
        public void MouseOutsideEveryWindowDoesNotLock()
        {
            Hover(OutX, OutY, term, dialog);
            Hover(OutX, OutY, term, dialog);
            Hover(680, 300, term, dialog); // below the dialog
            Assert.IsFalse(sink.Held);
        }

        [Test]
        public void EdgesFollowUnityRectContains()
        {
            Assert.IsTrue(TermRect.Contains(100, 100), "left/top edge");
            Assert.IsTrue(TermRect.Contains(499.9f, 399.9f));
            Assert.IsFalse(TermRect.Contains(500, 200), "right edge");
            Assert.IsFalse(TermRect.Contains(200, 400), "bottom edge");
            Assert.IsFalse(TermRect.Contains(99.9f, 200));
            Assert.IsFalse(new ScreenRect(10, 10, 0, 0).Contains(10, 10), "empty rect");
            Assert.IsFalse(new ScreenRect(10, 10, -5, 5).Contains(8, 12), "negative width");
        }

        [Test]
        public void LeavingReleasesAfterTheDelay()
        {
            Hover(InX, InY, term);
            Hover(InX, InY, term);
            Assert.IsTrue(sink.Held);
            sink.Take();

            Hover(OutX, OutY, term); // last wanted frame was the previous one
            Assert.IsTrue(sink.Held, "released the first frame after leaving");
            Hover(OutX, OutY, term);
            Assert.IsFalse(sink.Held, "kept longer than the delay");
            Assert.AreEqual(2, MouseBlocking.ReleaseDelayFrames, "this test spells out a two-frame delay");
            Assert.AreEqual("unlock", sink.Take());
        }

        [Test]
        public void SeveralUpdatesInOneFrameDoNotShortenTheDelay()
        {
            Hover(InX, InY, term);
            Hover(InX, InY, term);
            ++frame;
            for (int i = 0; i < 5; ++i)
                blocking.Update(frame, OutX, OutY, false);
            Assert.IsTrue(sink.Held);
            blocking.Show(term, TermRect, frame);
            ++frame;
            blocking.Update(frame, OutX, OutY, false);
            Assert.IsFalse(sink.Held);
        }

        [Test]
        public void ReturningWithinTheDelayKeepsTheLockWithoutChurn()
        {
            Hover(InX, InY, term);
            Hover(InX, InY, term);
            sink.Take();
            Hover(OutX, OutY, term);
            Hover(InX, InY, term);
            Hover(InX, InY, term);
            Assert.IsTrue(sink.Held);
            Assert.AreEqual("", sink.Take());
        }

        [Test]
        public void AWindowThatStopsDrawingStopsBlocking()
        {
            // Closed, hidden with the UI, destroyed or failing to draw: it simply is not drawn.
            Hover(InX, InY, term);
            Hover(InX, InY, term);
            Hover(InX, InY);          // the update still sees the previous frame's drawing
            Assert.IsTrue(sink.Held);
            Hover(InX, InY);          // not drawn for a whole frame: not over anything now
            Hover(InX, InY);
            Assert.IsFalse(sink.Held);
            Assert.IsFalse(blocking.IsOver(frame, InX, InY));
        }

        [Test]
        public void ButtonThatClosesAWindowIsReleasedLate()
        {
            // #2759: Accept on the name-tag popup closes it in the OnGUI of the mouse-up frame.
            Hover(DialogX, DialogY, dialog);
            Hover(DialogX, DialogY, dialog);
            Step(DialogX, DialogY, true, dialog);  // press
            Step(DialogX, DialogY, false);         // release: the button acts and the popup is gone
            int releaseFrame = frame;
            Assert.IsTrue(sink.Held, "frame of the mouse-up");
            Hover(DialogX, DialogY);
            Assert.IsTrue(sink.Held, "frame after the mouse-up");
            Hover(DialogX, DialogY);
            Hover(DialogX, DialogY);
            Assert.IsFalse(sink.Held);
            Assert.GreaterOrEqual(frame - releaseFrame, 2, "the scene saw the mouse-up frame and the next one locked");
        }

        [Test]
        public void DragStartedOnAWindowKeepsTheLockOutsideIt()
        {
            Hover(InX, InY, term);
            Hover(InX, InY, term);
            Step(InX, InY, true, term);         // press on the terminal (move or resize)
            for (int i = 0; i < 10; ++i)
                Step(OutX, OutY, true, term);   // dragged far outside
            Assert.IsTrue(sink.Held);
            Hover(OutX, OutY, term);             // released outside
            Assert.IsTrue(sink.Held, "the scene does not see the release of the drag");
            Hover(OutX, OutY, term);
            Assert.IsFalse(sink.Held);
        }

        [Test]
        public void DragStartedOutsideIsNotCutOffOverAWindow()
        {
            Hover(OutX, OutY, term);
            Step(OutX, OutY, true, term);       // camera drag starts on the scene
            for (int i = 0; i < 5; ++i)
                Step(InX, InY, true, term);     // passes over the terminal
            Assert.IsFalse(sink.Held);
            Assert.AreEqual("", sink.Take());

            Hover(InX, InY, term);               // released over it: now just hovering
            Assert.IsTrue(sink.Held);
        }

        [Test]
        public void PressOnTheSceneThenHoverTakesTheLockOnlyAfterRelease()
        {
            Hover(InX, InY, term);
            Hover(InX, InY, term);
            Hover(OutX, OutY, term);
            Hover(OutX, OutY, term);
            Assert.IsFalse(sink.Held);
            Step(OutX, OutY, true, term);
            Step(InX, InY, true, term);
            Assert.IsFalse(sink.Held);
        }

        [Test]
        public void AnyOfTwoWindowsBlocks()
        {
            Hover(DialogX, DialogY, term, dialog);
            Hover(DialogX, DialogY, term, dialog);
            Assert.IsTrue(sink.Held);
            Hover(DialogX, DialogY, term);     // the dialog went away, the terminal stays
            Hover(DialogX, DialogY, term);
            Hover(DialogX, DialogY, term);
            Assert.IsFalse(sink.Held);
            Hover(InX, InY, term);
            Assert.IsTrue(sink.Held);
        }

        [Test]
        public void HideStopsBlockingAtOnce()
        {
            Hover(InX, InY, term);
            Hover(InX, InY, term);
            blocking.Hide(term);
            Assert.IsFalse(blocking.IsOver(frame, InX, InY));
            Hover(InX, InY);
            Assert.IsTrue(sink.Held, "the release delay still applies");
            Hover(InX, InY);
            Assert.IsFalse(sink.Held);
        }

        [Test]
        public void TheLatestRectangleCounts()
        {
            Hover(OutX, OutY);
            blocking.Show(term, new ScreenRect(0, 0, 80, 80), frame); // moved under the mouse
            Hover(OutX, OutY);
            Assert.IsTrue(sink.Held);
        }

        [Test]
        public void ReleaseAllUnlocksAndForgetsTheOldScene()
        {
            Hover(InX, InY, term);
            Step(InX, InY, true, term);
            Assert.IsTrue(sink.Held);
            sink.Take();

            blocking.ReleaseAll();
            Assert.IsFalse(sink.Held);
            Assert.AreEqual("unlock", sink.Take());
            blocking.ReleaseAll();
            Assert.AreEqual("", sink.Take(), "twice is harmless");

            // The old scene's drawing and press do not lock in the new one.
            Step(InX, InY, true);
            Assert.IsFalse(sink.Held);
            Hover(InX, InY, term);
            Hover(InX, InY, term);
            Assert.IsTrue(sink.Held, "new drawing blocks again");
        }

        [Test]
        public void UnregisterForgetsTheWindow()
        {
            Hover(InX, InY, term);
            blocking.Unregister(term);
            blocking.Unregister(term);
            Hover(InX, InY);
            Assert.IsFalse(sink.Held);
            Assert.Throws<InvalidOperationException>(() => blocking.Show(term, TermRect, frame));
            Assert.Throws<InvalidOperationException>(() => blocking.Hide(term));
            Assert.Throws<ArgumentNullException>(() => blocking.Show(null, TermRect, frame));
            blocking.Unregister(null);
        }

        [Test]
        public void RefusedLockIsRetried()
        {
            Hover(InX, InY, term);
            sink.FailLock = true;
            Assert.Throws<InvalidOperationException>(() => Hover(InX, InY, term));
            Assert.IsFalse(blocking.HoldsLock);
            sink.FailLock = false;
            Hover(InX, InY, term);
            Assert.IsTrue(sink.Held);
            Assert.AreEqual("lock", sink.Take());
        }

        [Test]
        public void RefusedUnlockIsRetried()
        {
            Hover(InX, InY, term);
            Hover(InX, InY, term);
            Hover(OutX, OutY, term);
            sink.FailUnlock = true;
            Assert.Throws<InvalidOperationException>(() => Hover(OutX, OutY, term));
            Assert.IsTrue(blocking.HoldsLock);
            Assert.Throws<InvalidOperationException>(() => blocking.ReleaseAll());
            Assert.IsTrue(blocking.HoldsLock);
            sink.FailUnlock = false;
            Hover(OutX, OutY, term);
            Assert.IsFalse(sink.Held);
            Assert.AreEqual("lock, unlock", sink.Take());
        }

        [Test]
        public void ConstructorNeedsASink()
        {
            Assert.Throws<ArgumentNullException>(() => new MouseBlocking(null));
        }
    }
}
