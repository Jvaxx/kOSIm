using System;
using System.Collections.Generic;
using System.Linq;
using kOS.Safe.Screen;
using kOS.Safe.UserIO;
using NUnit.Framework;

namespace kOS.Safe.Test.UserIO
{
    /// <summary>
    /// The terminal's viewer fan-out (Phase 2, P2-2), moved out of <c>TermWindow</c>
    /// unchanged, over a real screen and fake telnet clients: resize authority (decision
    /// D3, last size applied wins), attach and detach, refreshes, full repaints, beeps,
    /// screen modes, the title and the clear. A client's <see cref="ViewerScreenModel"/>
    /// shows what its terminal displays after the output it was sent.
    /// </summary>
    [TestFixture]
    public class TerminalSessionTest
    {
        private const string Title = "Ship CPU: CX-4181 ()";

        private ScreenBuffer screen;
        private TerminalSession<RecordingViewer> session;
        private DateTime now;

        [SetUp]
        public void SetUp()
        {
            screen = new ScreenBuffer();
            screen.SetSize(10, 40);
            screen.ClearScreen();
            now = new DateTime(2026, 10, 19, 12, 0, 0);
            session = new TerminalSession<RecordingViewer>(now) { Screen = screen };
            // As TermWindow.AttachTo registers NotifyOfScreenResize:
            screen.AddResizeNotifier(sb => { session.ScreenResized(); return 0; });
            session.ChangeTitle(Title);
        }

        private bool Refresh()
        {
            now += TimeSpan.FromMilliseconds(51);
            return session.Update(now);
        }

        private RecordingViewer Attach(string name, int width, int height)
        {
            var viewer = new RecordingViewer(width, height) { Name = name };
            session.AttachViewer(viewer);
            return viewer;
        }

        /// <summary>
        /// The user resizes a client, which reports its size (NAWS). As the telnet server
        /// queues it, the router applies the size, then the repaint request that comes with it.
        /// </summary>
        private void ReportSize(RecordingViewer viewer, int width, int height)
        {
            viewer.UserResize(width, height);
            screen.SetSize(height, width);
            session.RepaintViewer(viewer);
        }

        private static List<string> Commands(string output)
        {
            return RecordingViewer.Describe(output).Where(line => !line.StartsWith("text ")).ToList();
        }

        private static string ScreenRow(IScreenSnapShot snapshot, int row, int width)
        {
            string text = row < snapshot.Buffer.Count ? snapshot.Buffer[row].ToString().Replace('\0', ' ') : "";
            return text.Length >= width ? text.Substring(0, width) : text.PadRight(width);
        }

        /// <summary>The client shows exactly the screen as of the last refresh, cursor included.</summary>
        private void AssertShowsScreen(RecordingViewer viewer)
        {
            IScreenSnapShot snapshot = session.Snapshot;
            Assert.AreEqual(screen.ColumnCount, viewer.Model.Width, viewer + " width");
            Assert.AreEqual(screen.RowCount, viewer.Model.Height, viewer + " height");
            for (int row = 0; row < screen.RowCount; ++row)
                Assert.AreEqual(ScreenRow(snapshot, row, screen.ColumnCount), viewer.Model.Row(row), viewer + " row " + row);
            Assert.AreEqual(snapshot.CursorRow, viewer.Model.CursorRow, viewer + " cursor row");
            Assert.AreEqual(snapshot.CursorColumn, viewer.Model.CursorColumn, viewer + " cursor column");
        }

        // ---- Resize authority (D3) ----

        [Test]
        public void AttachingAViewerGivesTheScreenItsSize()
        {
            Refresh();
            RecordingViewer a = Attach("a", 80, 24);

            Assert.AreEqual(24, screen.RowCount);
            Assert.AreEqual(80, screen.ColumnCount);
            List<string> sent = Commands(a.Output);
            CollectionAssert.DoesNotContain(sent, "RESIZESCREEN 80x24", "a viewer is not asked to take its own size");
            Assert.AreEqual("TITLE \"" + Title + "\"", sent[0]);
            Assert.AreEqual("CLEARSCREEN", sent[1], "then a full repaint");
            Assert.AreEqual(1, session.ViewerCount);
        }

        [Test]
        public void ViewersOfAnotherSizeAreAskedToTakeTheNewSize()
        {
            RecordingViewer a = Attach("a", 80, 24);
            Refresh();
            a.TakeOutput();

            RecordingViewer b = Attach("b", 100, 30);

            Assert.AreEqual(30, screen.RowCount);
            Assert.AreEqual(100, screen.ColumnCount);
            List<string> toA = Commands(a.TakeOutput());
            Assert.AreEqual("RESIZESCREEN 100x30", toA[0]);
            Assert.AreEqual("CLEARSCREEN", toA[1], "and repainted");
            CollectionAssert.DoesNotContain(Commands(b.Output), "RESIZESCREEN 100x30");

            screen.Print("after the resize");
            Refresh();
            AssertShowsScreen(a);
            AssertShowsScreen(b);
        }

        [Test]
        public void TheLastSizeAppliedWins()
        {
            RecordingViewer a = Attach("a", 80, 24);
            RecordingViewer b = Attach("b", 100, 30);
            Refresh();

            screen.SetSize(20, 50); // the GUI resize drag
            ReportSize(a, 70, 15);   // a's NAWS
            screen.SetSize(12, 60); // a script's TERMINAL:WIDTH/HEIGHT

            Assert.AreEqual(12, screen.RowCount);
            Assert.AreEqual(60, screen.ColumnCount);
            Assert.AreEqual("RESIZESCREEN 60x12", Commands(a.Output).Last(line => line.StartsWith("RESIZESCREEN")));
            Assert.AreEqual("RESIZESCREEN 60x12", Commands(b.Output).Last(line => line.StartsWith("RESIZESCREEN")));
            screen.Print("last size");
            Refresh();
            AssertShowsScreen(a);
            AssertShowsScreen(b);
        }

        [Test]
        public void AViewerThatReportedTheNewSizeIsNotAskedToTakeIt()
        {
            RecordingViewer a = Attach("a", 80, 24);
            RecordingViewer b = Attach("b", 80, 24);
            Refresh();
            a.TakeOutput();
            b.TakeOutput();

            a.UserResize(90, 20);
            screen.SetSize(20, 90); // (a's repaint request follows separately)

            Assert.AreEqual(new List<string>(), Commands(a.TakeOutput()), "a already has its size");
            Assert.AreEqual("RESIZESCREEN 90x20", Commands(b.TakeOutput())[0]);
        }

        [Test]
        public void ResizingToTheCurrentSizeSendsNothing()
        {
            RecordingViewer a = Attach("a", 80, 24);
            Refresh();
            a.TakeOutput();

            screen.SetSize(24, 80);
            session.ScreenResized(); // e.g. a font change re-sends the notification without a resize

            Assert.AreEqual("", a.TakeOutput());
        }

        [Test]
        public void AViewerThatIgnoresTheRequestIsAskedAgainAtEachResize()
        {
            RecordingViewer a = Attach("a", 80, 24);
            a.Model.ObeysResize = false;
            Refresh();
            a.TakeOutput();

            screen.SetSize(20, 50);
            screen.SetSize(21, 50);

            List<string> toA = Commands(a.TakeOutput());
            Assert.AreEqual(2, toA.Count(line => line.StartsWith("RESIZESCREEN")));
            Assert.AreEqual(80, a.Model.Width, "the client kept its size; the screen is drawn in its top-left corner");
        }

        // ---- Refreshes ----

        [Test]
        public void TheScreenIsRetakenAndSentAtMostEvery50Milliseconds()
        {
            Assert.IsNull(session.Snapshot, "no refresh yet");
            screen.Print("hello");

            Assert.IsFalse(session.Update(now + TimeSpan.FromMilliseconds(50)));
            Assert.IsNull(session.Snapshot);

            Assert.IsTrue(session.Update(now + TimeSpan.FromMilliseconds(51)));
            Assert.AreEqual("hello", ScreenRow(session.Snapshot, 0, 5));

            RecordingViewer a = Attach("a", 40, 10);
            a.TakeOutput();
            screen.Print("again");
            Assert.IsFalse(session.Update(now + TimeSpan.FromMilliseconds(101)));
            Assert.AreEqual("", a.TakeOutput());
            Assert.AreEqual("hello", ScreenRow(session.Snapshot, 0, 5), "the GUI keeps the last snapshot");

            Assert.IsTrue(session.Update(now + TimeSpan.FromMilliseconds(102)));
            StringAssert.Contains("again", a.TakeOutput());
            AssertShowsScreen(a);
        }

        [Test]
        public void ARefreshSendsOnlyTheChanges()
        {
            screen.Print("first line");
            Refresh();
            RecordingViewer a = Attach("a", 40, 10);
            a.TakeOutput();

            screen.Print("second");
            Refresh();

            string sent = a.TakeOutput();
            CollectionAssert.DoesNotContain(Commands(sent), "CLEARSCREEN");
            StringAssert.DoesNotContain("first line", sent);
            StringAssert.Contains("second", sent);
            AssertShowsScreen(a);
        }

        [Test]
        public void ARefreshFollowsScrolling()
        {
            RecordingViewer a = Attach("a", 40, 10);
            Refresh();
            for (int i = 0; i < 25; ++i)
            {
                screen.Print("line " + i);
                if (i % 7 == 0)
                    Refresh();
            }
            Refresh();
            AssertShowsScreen(a);

            screen.ScrollVertical(-5); // PageUp
            Refresh();
            CollectionAssert.Contains(Commands(a.Output), "SCROLLSCREENDOWNONE");
            AssertShowsScreen(a);
        }

        [Test]
        public void BeepsAreSentToEveryViewerWithARefresh()
        {
            RecordingViewer a = Attach("a", 40, 10);
            RecordingViewer b = Attach("b", 40, 10);
            Refresh();

            screen.Print("" + (char)7 + (char)7, false);
            Assert.AreEqual(2, screen.BeepsPending);
            session.RepaintViewer(a); // a full repaint carries no beeps
            Assert.AreEqual(0, a.Model.Beeps);

            Assert.IsTrue(Refresh());
            Assert.AreEqual(2, a.Model.Beeps);
            Assert.AreEqual(2, b.Model.Beeps);
            Assert.AreEqual(2, screen.BeepsPending, "the window hands them to the GUI and clears them");
        }

        [Test]
        public void ScreenModesAreSentWhenTheyChangeAndRestatedByAFullRepaint()
        {
            RecordingViewer a = Attach("a", 40, 10);
            Refresh();
            a.TakeOutput();

            screen.ReverseScreen = true;
            Refresh();
            Refresh();
            Assert.AreEqual(new List<string> { "REVERSESCREENMODE" }, Commands(a.TakeOutput()));
            Assert.IsTrue(a.Model.Reverse);

            screen.VisualBeep = true;
            Refresh();
            Assert.AreEqual(new List<string> { "VISUALBEEPMODE" }, Commands(a.TakeOutput()));

            session.RepaintViewer(a);
            List<string> repaint = Commands(a.TakeOutput());
            CollectionAssert.Contains(repaint, "REVERSESCREENMODE");
            CollectionAssert.Contains(repaint, "VISUALBEEPMODE");
        }

        // ---- Repaint request, title, clear ----

        [Test]
        public void ARepaintRequestRestatesTheSizeTheScreenAndTheTitle()
        {
            RecordingViewer a = Attach("a", 40, 10);
            RecordingViewer b = Attach("b", 40, 10);
            screen.Print("content");
            Refresh();
            a.TakeOutput();
            b.TakeOutput();

            session.RepaintViewer(a);

            List<string> toA = Commands(a.TakeOutput());
            Assert.AreEqual("RESIZESCREEN 40x10", toA[0], "even though a has that size");
            Assert.AreEqual("CLEARSCREEN", toA[1]);
            Assert.AreEqual("TITLE \"" + Title + "\"", toA.Last());
            Assert.AreEqual("", b.TakeOutput(), "only the asking client");
            AssertShowsScreen(a);
        }

        [Test]
        public void TheTitleIsSentWhenItChanges()
        {
            RecordingViewer a = Attach("a", 40, 10);
            a.TakeOutput();

            session.ChangeTitle(Title);
            Assert.AreEqual("", a.TakeOutput());

            session.ChangeTitle("Other CPU");
            Assert.AreEqual(new List<string> { "TITLE \"Other CPU\"" }, Commands(a.TakeOutput()));
            Assert.AreEqual("Other CPU", session.Title);
            Assert.AreEqual("Other CPU", a.Model.Title);
        }

        [Test]
        public void ClearScreenClearsEveryViewerAndTheNextRefreshDiffsFromBlank()
        {
            RecordingViewer a = Attach("a", 40, 10);
            screen.Print("old text");
            Refresh();
            a.TakeOutput();

            session.ClearScreen();
            Assert.AreEqual(new List<string> { "CLEARSCREEN" }, Commands(a.TakeOutput()));
            screen.Print("new");
            Refresh();

            string sent = a.TakeOutput();
            CollectionAssert.DoesNotContain(Commands(sent), "CLEARSCREEN");
            StringAssert.Contains("new", sent);
            AssertShowsScreen(a);
        }

        // ---- Attach and detach ----

        [Test]
        public void AttachingBeforeTheFirstRefreshPaintsTheCurrentScreen()
        {
            screen.Print("already here");
            RecordingViewer a = Attach("a", 40, 10); // TermWindow used to throw: no snapshot yet

            Assert.IsNotNull(session.Snapshot);
            AssertShowsScreen(a);
        }

        [Test]
        public void AttachingTwiceKeepsOneEntryAndRepaints()
        {
            RecordingViewer a = Attach("a", 40, 10);
            Refresh();
            a.TakeOutput();

            session.AttachViewer(a);

            Assert.AreEqual(1, session.ViewerCount);
            CollectionAssert.Contains(Commands(a.TakeOutput()), "CLEARSCREEN");
        }

        [Test]
        public void ADetachedViewerIsToldOnceAndSentNothingMore()
        {
            RecordingViewer a = Attach("a", 40, 10);
            RecordingViewer b = Attach("b", 40, 10);
            bool detaching = false;
            a.OnDetach = () =>
            {
                // As TelnetSingletonServer.DisconnectFromProcessor: calls the window back, once.
                if (detaching) return;
                detaching = true;
                session.DetachViewer(a);
                detaching = false;
            };
            Refresh();

            session.DetachViewer(a);

            Assert.AreEqual(2, a.DetachCount, "the call back reaches the viewer again, which ignores it");
            Assert.IsFalse(session.HasViewer(a));
            Assert.AreEqual(new[] { b }, session.Viewers);
            a.TakeOutput();
            screen.Print("more");
            screen.SetSize(5, 20);
            session.ChangeTitle("changed");
            session.ClearScreen();
            Refresh();
            Assert.AreEqual("", a.TakeOutput());
        }

        [Test]
        public void DetachAllViewersDetachesEachOnce()
        {
            RecordingViewer a = Attach("a", 40, 10);
            RecordingViewer b = Attach("b", 40, 10);

            session.DetachAllViewers();

            Assert.AreEqual(1, a.DetachCount);
            Assert.AreEqual(1, b.DetachCount);
            Assert.AreEqual(0, session.ViewerCount);
        }
    }
}
