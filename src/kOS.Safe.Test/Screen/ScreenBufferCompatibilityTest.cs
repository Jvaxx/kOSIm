using System.Linq;
using kOS.Safe.Screen;
using NUnit.Framework;

namespace kOS.Safe.Test.Terminal
{
    /// <summary>Characterize supported character-grid behavior before terminal refactoring.</summary>
    [TestFixture]
    public class ScreenBufferCompatibilityTest
    {
        private ScreenBuffer screen;

        [SetUp]
        public void Setup()
        {
            screen = new ScreenBuffer();
            screen.SetSize(3, 8);
            screen.ClearScreen(); // Resizing alone retains the original rows as history.
        }

        private string Row(int index)
        {
            return screen.GetBuffer()[index].ToString().Replace('\0', ' ');
        }

        [Test]
        public void DefaultScreenHasExpectedGridAndCursor()
        {
            var defaultScreen = new ScreenBuffer();
            Assert.AreEqual(36, defaultScreen.RowCount);
            Assert.AreEqual(50, defaultScreen.ColumnCount);
            Assert.AreEqual(36, defaultScreen.GetBuffer().Count);
            Assert.IsTrue(defaultScreen.GetBuffer().All(line => line.Length == 50));
            Assert.AreEqual(0, defaultScreen.TopRow);
            Assert.AreEqual(0, defaultScreen.CursorRowShow);
            Assert.AreEqual(0, defaultScreen.CursorColumnShow);
        }

        [Test]
        public void PrintAtUsesRowAndColumnWithoutAddingNewline()
        {
            screen.PrintAt("HUD", 1, 2);
            Assert.AreEqual("  HUD   ", Row(1));
            Assert.AreEqual("        ", Row(0));
            Assert.AreEqual(1, screen.CursorRowShow);
            Assert.AreEqual(5, screen.CursorColumnShow);
        }

        [Test]
        public void PrintAtOverwritesOnlyAddressedCells()
        {
            screen.PrintAt("abcdef", 1, 0);
            screen.PrintAt("X", 1, 2);
            Assert.AreEqual("abXdef  ", Row(1));
        }

        [Test]
        public void PrintAdvancesToNextLine()
        {
            screen.Print("first");
            screen.Print("second");
            Assert.AreEqual("first   ", Row(0));
            Assert.AreEqual("second  ", Row(1));
            Assert.AreEqual(2, screen.CursorRowShow);
            Assert.AreEqual(0, screen.CursorColumnShow);
        }

        [Test]
        public void LongOutputWrapsAtColumnBoundary()
        {
            screen.SetSize(3, 4);
            screen.Print("abcdef", false);
            Assert.AreEqual("abcd", Row(0));
            Assert.AreEqual("ef  ", Row(1));
            Assert.AreEqual(1, screen.CursorRowShow);
            Assert.AreEqual(2, screen.CursorColumnShow);
        }

        [Test]
        public void ExactWidthPrintDoesNotAddAnExtraBlankLine()
        {
            screen.Print("abcdefgh");
            Assert.AreEqual("abcdefgh", Row(0));
            Assert.AreEqual(1, screen.CursorRowShow);
            Assert.AreEqual(0, screen.CursorColumnShow);
        }

        [Test]
        public void OverflowRetainsRowsAndScrollClampsToAvailableHistory()
        {
            screen.Print("first");
            screen.Print("second");
            screen.Print("third");
            Assert.AreEqual(1, screen.TopRow);
            Assert.AreEqual("second  ", Row(0));
            Assert.AreEqual("third   ", Row(1));
            Assert.AreEqual(-1, screen.ScrollVertical(-10));
            Assert.AreEqual("first   ", Row(0));
            Assert.AreEqual(0, screen.ScrollVertical(-10));
            Assert.AreEqual(1, screen.ScrollVertical(10));
            Assert.AreEqual(0, screen.ScrollVertical(10));
        }

        [Test]
        public void ClearScreenResetsContentHistoryAndCursor()
        {
            screen.Print("first");
            screen.Print("second");
            screen.Print("third");
            screen.ClearScreen();
            Assert.AreEqual(0, screen.TopRow);
            Assert.AreEqual(0, screen.CursorRowShow);
            Assert.AreEqual(0, screen.CursorColumnShow);
            Assert.AreEqual(0, screen.ScrollVertical(-10));
            Assert.IsTrue(screen.GetBuffer().All(line => line.ToArray().All(cell => cell == '\0')));
        }

        [Test]
        public void GrowingGridPreservesAddressedContent()
        {
            screen.PrintAt("HUD", 1, 2);
            screen.SetSize(4, 10);
            Assert.AreEqual(4, screen.GetBuffer().Count);
            Assert.IsTrue(screen.GetBuffer().All(line => line.Length == 10));
            Assert.AreEqual("  HUD     ", Row(1));
        }

        [Test]
        public void ResizeNotifierIsRegisteredOnceAndCanBeRemoved()
        {
            int calls = 0;
            ScreenBuffer.ResizeNotifier notifier = buffer =>
            {
                calls++;
                Assert.AreSame(screen, buffer);
                Assert.AreEqual(4, buffer.RowCount);
                Assert.AreEqual(10, buffer.ColumnCount);
                return 0;
            };
            screen.AddResizeNotifier(notifier);
            screen.AddResizeNotifier(notifier);
            screen.SetSize(4, 10);
            Assert.AreEqual(1, calls);
            screen.RemoveResizeNotifier(notifier);
            screen.SetSize(3, 8);
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void FixedOverlayDoesNotMutateUnderlyingGrid()
        {
            screen.PrintAt("base", 1, 0);
            var overlay = new SubBuffer { Fixed = true, Enabled = true, PositionRow = 1 };
            overlay.SetSize(1, 8);
            overlay.Buffer[0].ArrayCopyFrom("overlay".ToCharArray(), 0, 0);
            screen.AddSubBuffer(overlay);
            Assert.AreEqual("overlay ", Row(1));
            overlay.Enabled = false;
            Assert.AreEqual("base    ", Row(1));
            overlay.Enabled = true;
            screen.RemoveSubBuffer(overlay);
            Assert.AreEqual("base    ", Row(1));
        }

        [Test]
        public void SnapshotDeepCopyPreservesContentAndCursorAfterLaterWrites()
        {
            screen.PrintAt("A", 1, 2);
            var copy = new ScreenSnapShot(screen).DeepCopy();
            screen.PrintAt("B", 1, 2);
            Assert.AreEqual('A', copy.Buffer[1][2]);
            Assert.AreEqual('B', screen.GetBuffer()[1][2]);
            Assert.AreEqual(1, copy.CursorRow);
            Assert.AreEqual(3, copy.CursorColumn);
            Assert.AreEqual(3, copy.RowCount);
        }

        [Test]
        public void EmptySnapshotKeepsGridAndCursorMetadata()
        {
            screen.PrintAt("HUD", 1, 2);
            var empty = ScreenSnapShot.EmptyScreen(screen);
            Assert.AreEqual(3, empty.Buffer.Count);
            Assert.IsTrue(empty.Buffer.All(line => line.Length == 8));
            Assert.IsTrue(empty.Buffer.All(line => line.ToArray().All(cell => cell == '\0')));
            Assert.AreEqual(1, empty.CursorRow);
            Assert.AreEqual(5, empty.CursorColumn);
        }
    }
}
