using System.Linq;
using kOS.Safe.Screen;
using kOS.Safe.Test.UserIO;
using kOS.Safe.UserIO;
using NUnit.Framework;

namespace kOS.Safe.Test.Terminal
{
    /// <summary>
    /// <see cref="ScreenSnapShot.DiffFrom"/> played on a model client: after each diff the
    /// client shows exactly the snapshot (VIEW-05: also rows that moved without being edited).
    /// </summary>
    [TestFixture]
    public class ScreenSnapshotDiffTest
    {
        private ScreenBuffer screen;
        private ViewerScreenModel client;
        private IScreenSnapShot sent; // what the client was last sent, as the session keeps it

        [SetUp]
        public void Setup()
        {
            screen = new ScreenBuffer();
            screen.SetSize(5, 8);
            screen.ClearScreen();
            client = new ViewerScreenModel(8, 5);
            sent = ScreenSnapShot.EmptyScreen(screen);
            Send();
        }

        private void Send()
        {
            var now = new ScreenSnapShot(screen);
            client.Apply(now.DiffFrom(sent));
            sent = now.DeepCopy();
            for (int row = 0; row < screen.RowCount; ++row)
                Assert.AreEqual(now.Buffer[row].ToString().Replace('\0', ' '), client.Row(row), "row " + row);
        }

        private static SubBuffer Overlay(int position, params string[] rows)
        {
            var overlay = new SubBuffer { Enabled = true, PositionRow = position };
            overlay.SetSize(rows.Length, 8);
            for (int row = 0; row < rows.Length; ++row)
                overlay.Buffer[row].ArrayCopyFrom(rows[row].PadRight(8).ToCharArray(), 0, 0);
            return overlay;
        }

        [Test]
        public void AnOverlayMovedDownIsSentAtItsNewRows()
        {
            screen.PrintAt("base0", 0, 0);
            SubBuffer overlay = Overlay(1, "draft-1", "draft-2");
            screen.AddSubBuffer(overlay);
            Send();

            overlay.PositionRow = 2; // moved, not edited: its rows keep their change time
            Send();
            overlay.PositionRow = 3;
            screen.PrintAt("base1", 1, 0);
            Send();
        }

        [Test]
        public void AnOverlayMovedUpOrDisabledUncoversTheRowsUnderIt()
        {
            screen.PrintAt("under2", 2, 0);
            screen.PrintAt("under3", 3, 0);
            SubBuffer overlay = Overlay(2, "over-a", "over-b");
            screen.AddSubBuffer(overlay);
            Send();

            overlay.PositionRow = 1; // row 3 shows the base row again, which did not change
            Send();
            overlay.Enabled = false;
            Send();
        }

        [Test]
        public void TwoRowsSwappedAreBothSent()
        {
            SubBuffer first = Overlay(0, "one");
            SubBuffer second = Overlay(1, "two");
            screen.AddSubBuffer(first);
            screen.AddSubBuffer(second);
            Send();

            first.PositionRow = 1;
            second.PositionRow = 0;
            Send();
            Assert.AreEqual("two", client.Row(0).TrimEnd());
            Assert.AreEqual("one", client.Row(1).TrimEnd());
        }

        [Test]
        public void AnUnchangedScreenSendsNothing()
        {
            screen.PrintAt("same", 0, 0);
            screen.AddSubBuffer(Overlay(2, "fixed"));
            Send();
            var now = new ScreenSnapShot(screen);
            Assert.AreEqual("", now.DiffFrom(sent));
        }

        [Test]
        public void ScrolledOutputIsSentAsAScrollAndTheNewRows()
        {
            for (int i = 0; i < 4; ++i)
                screen.Print("line" + i);
            Send();
            screen.Print("line4");
            screen.Print("line5");
            var now = new ScreenSnapShot(screen);
            string diff = now.DiffFrom(sent);
            Assert.IsTrue(diff.Contains((char)UnicodeCommand.SCROLLSCREENUPONE), "scrolls rather than repaints");
            Assert.IsFalse(diff.Contains("line1"), "rows that only scrolled are not sent again");
            Send();
            Assert.AreEqual(new[] { "line2", "line3", "line4", "line5" },
                Enumerable.Range(0, 4).Select(row => client.Row(row).TrimEnd()).ToArray());
        }
    }
}
