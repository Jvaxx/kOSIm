using kOS.Safe.Screen;
using NUnit.Framework;

namespace kOS.Safe.Test.Terminal
{
    [TestFixture]
    public class ScreenCursorRenderingTest
    {
        private class CursorScreen : ScreenBuffer
        {
            public void SetDisplayCursor(int row, int column)
            {
                CursorRow = row;
                CursorColumn = column;
            }
        }

        private static CursorScreen ScreenWithCursor(int row, int column)
        {
            var screen = new CursorScreen();
            screen.SetSize(3, 4);
            screen.ClearScreen();
            screen.PrintAt("ABCD", 1, 0);
            screen.SetDisplayCursor(row, column);
            return screen;
        }

        [TestCase(-1, 0)]
        [TestCase(0, -1)]
        [TestCase(-1, -1)]
        [TestCase(3, 0)]
        [TestCase(0, 4)]
        [TestCase(int.MinValue, int.MaxValue)]
        [TestCase(int.MaxValue, int.MinValue)]
        public void OutOfBoundsSnapshotCursorIsHiddenWithoutIndexing(int row, int column)
        {
            var snapshot = new ScreenSnapShot(ScreenWithCursor(row, column));
            char character;
            Assert.IsFalse(snapshot.TryGetCursorCharacter(3, 4, out character));
            Assert.AreEqual('\0', character);
        }

        [TestCase(0, 4)]
        [TestCase(3, 0)]
        [TestCase(-1, 4)]
        [TestCase(3, -1)]
        [TestCase(1, 4)] // cursor row clipped by shrinking viewport
        [TestCase(3, 1)] // cursor column clipped by shrinking viewport
        public void CursorOutsideCurrentViewportIsHidden(int visibleRows, int visibleColumns)
        {
            var snapshot = new ScreenSnapShot(ScreenWithCursor(1, 2));
            char character;
            Assert.IsFalse(snapshot.TryGetCursorCharacter(visibleRows, visibleColumns, out character));
        }

        [Test]
        public void VisibleSnapshotCursorReadsItsOwnCharacter()
        {
            var screen = ScreenWithCursor(1, 2);
            var snapshot = new ScreenSnapShot(screen);
            char character;
            Assert.IsTrue(snapshot.TryGetCursorCharacter(3, 4, out character));
            Assert.AreEqual('C', character);
            Assert.AreEqual(1, snapshot.CursorRow);
            Assert.AreEqual(2, snapshot.CursorColumn);
        }

        [Test]
        public void SnapshotCursorDoesNotFollowLaterLiveCursorMovementOrResize()
        {
            var screen = ScreenWithCursor(1, 2);
            IScreenSnapShot snapshot = new ScreenSnapShot(screen).DeepCopy();
            screen.SetDisplayCursor(-1, -1);
            char character;
            Assert.IsTrue(snapshot.TryGetCursorCharacter(3, 4, out character));
            Assert.AreEqual('C', character);
            screen.SetSize(1, 1);
            Assert.IsFalse(snapshot.TryGetCursorCharacter(screen.RowCount, screen.ColumnCount, out character));
            Assert.IsTrue(snapshot.TryGetCursorCharacter(3, 4, out character));
            Assert.AreEqual('C', character);
        }

        [Test]
        public void NullSnapshotIsHidden()
        {
            IScreenSnapShot snapshot = null;
            char character;
            Assert.IsFalse(snapshot.TryGetCursorCharacter(3, 4, out character));
        }

        [Test]
        public void EmptySnapshotRowsAreHidden()
        {
            var snapshot = new ScreenSnapShot(ScreenWithCursor(1, 2));
            snapshot.Buffer.Clear();
            char character;
            Assert.IsFalse(snapshot.TryGetCursorCharacter(3, 4, out character));
        }

        [Test]
        public void ShortenedSnapshotRowsAreHidden()
        {
            var snapshot = new ScreenSnapShot(ScreenWithCursor(2, 1));
            snapshot.Buffer.RemoveAt(2);
            char character;
            Assert.IsFalse(snapshot.TryGetCursorCharacter(3, 4, out character));
        }

        [Test]
        public void ShortOrZeroWidthCursorRowIsHidden()
        {
            var snapshot = new ScreenSnapShot(ScreenWithCursor(1, 2));
            snapshot.Buffer[1] = new ScreenBufferLine(2);
            char character;
            Assert.IsFalse(snapshot.TryGetCursorCharacter(3, 4, out character));
            snapshot.Buffer[1] = new ScreenBufferLine(0);
            Assert.IsFalse(snapshot.TryGetCursorCharacter(3, 4, out character));
        }

        [Test]
        public void NullCursorRowIsHidden()
        {
            var snapshot = new ScreenSnapShot(ScreenWithCursor(1, 2));
            snapshot.Buffer[1] = null;
            char character;
            Assert.IsFalse(snapshot.TryGetCursorCharacter(3, 4, out character));
        }

        [Test]
        public void SnapshotHeightAlsoBoundsCursorWhenBufferHasExtraRows()
        {
            var screen = ScreenWithCursor(3, 0);
            var snapshot = new ScreenSnapShot(screen);
            snapshot.Buffer.Add(new ScreenBufferLine(4));
            char character;
            Assert.IsFalse(snapshot.TryGetCursorCharacter(4, 4, out character));
        }

        [Test]
        public void NullCellIsDisplayedAsSpaceWithoutChangingSnapshot()
        {
            var snapshot = new ScreenSnapShot(ScreenWithCursor(0, 0));
            char character;
            Assert.IsTrue(snapshot.TryGetCursorCharacter(3, 4, out character));
            Assert.AreEqual(' ', character);
            Assert.AreEqual('\0', snapshot.Buffer[0][0]);
        }

        [Test]
        public void RealScrolledInputWithNegativeCursorIsHidden()
        {
            var editor = new TextEditor();
            editor.SetSize(3, 4);
            editor.ClearScreen();
            foreach (char ch in "abcdefghijklmnopqrstuvwxyz") editor.Type(ch);
            editor.SpecialKey((char)kOS.Safe.UserIO.UnicodeCommand.HOMECURSOR);
            Assert.Less(editor.CursorRowShow, 0);
            var snapshot = new ScreenSnapShot(editor);
            char character;
            Assert.IsFalse(snapshot.TryGetCursorCharacter(3, 4, out character));
        }
    }
}
