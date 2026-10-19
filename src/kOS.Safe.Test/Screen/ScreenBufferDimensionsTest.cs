using System;
using System.Linq;
using kOS.Safe.Encapsulation;
using kOS.Safe.Screen;
using NUnit.Framework;

namespace kOS.Safe.Test.Terminal
{
    [TestFixture]
    public class ScreenBufferDimensionsTest
    {
        [TestCase(0, 0, 36, 50)]
        [TestCase(0, 20, 36, 20)]
        [TestCase(5, 0, 5, 50)]
        [TestCase(-1, 20, 1, 20)]
        [TestCase(5, -1, 5, 1)]
        [TestCase(int.MinValue, int.MinValue, 1, 1)]
        [TestCase(1, 1, 1, 1)]
        [TestCase(2, 4, 2, 4)] // model still supports smaller grids than script suffix minima
        [TestCase(160, 255, 160, 255)]
        [TestCase(161, 256, 160, 255)]
        [TestCase(65535, 20, 160, 20)] // NAWS height is unsigned 16-bit
        public void RequestedDimensionsAreNormalizedBeforeAllocation(int rows, int columns, int expectedRows, int expectedColumns)
        {
            var screen = new ScreenBuffer();
            screen.SetSize(rows, columns);
            Assert.AreEqual(expectedRows, screen.RowCount);
            Assert.AreEqual(expectedColumns, screen.ColumnCount);
            var buffer = screen.GetBuffer();
            Assert.AreEqual(expectedRows, buffer.Count);
            Assert.IsTrue(buffer.All(line => line.Length == expectedColumns));
            screen.ClearScreen();
            screen.PrintAt("X", 0, 0);
            screen.ScrollVertical(-screen.TopRow); // A one-cell grid scrolls when its sole cell is filled.
            Assert.AreEqual('X', screen.GetBuffer()[0][0]);
        }

        // Added after the cap was implemented: do not allocate int.MaxValue in an unfixed baseline.
        [Test]
        public void MaximumIntegerRequestIsCappedBeforeAllocation()
        {
            var screen = new ScreenBuffer();
            screen.SetSize(int.MaxValue, int.MaxValue);
            Assert.AreEqual(160, screen.RowCount);
            Assert.AreEqual(255, screen.ColumnCount);
            Assert.AreEqual(160, screen.GetBuffer().Count);
        }

        [Test]
        public void ZeroDimensionsPreserveExistingNonDefaultSize()
        {
            var screen = new ScreenBuffer();
            screen.SetSize(5, 20);
            screen.SetSize(0, 0);
            Assert.AreEqual(5, screen.RowCount);
            Assert.AreEqual(20, screen.ColumnCount);
            screen.SetSize(0, 30);
            Assert.AreEqual(5, screen.RowCount);
            Assert.AreEqual(30, screen.ColumnCount);
            screen.SetSize(6, 0);
            Assert.AreEqual(6, screen.RowCount);
            Assert.AreEqual(30, screen.ColumnCount);
        }

        [Test]
        public void UnchangedEffectiveSizePreservesRowsTicksCursorAndHistoryWithoutNotifying()
        {
            var screen = new ScreenBuffer();
            screen.SetSize(3, 8);
            screen.ClearScreen();
            for (int i = 0; i < 5; ++i) screen.Print("row");
            var before = screen.GetBuffer();
            var ticks = before.Select(line => line.LastChangeTick).ToArray();
            int top = screen.TopRow;
            int cursorRow = screen.CursorRowShow;
            int cursorColumn = screen.CursorColumnShow;
            int calls = 0;
            screen.AddResizeNotifier(buffer => { ++calls; return -1; });
            screen.SetSize(3, 8);
            screen.SetSize(0, 0);
            var after = screen.GetBuffer();
            Assert.AreEqual(0, calls);
            Assert.AreEqual(top, screen.TopRow);
            Assert.AreEqual(cursorRow, screen.CursorRowShow);
            Assert.AreEqual(cursorColumn, screen.CursorColumnShow);
            for (int row = 0; row < after.Count; ++row) Assert.AreSame(before[row], after[row]);
            CollectionAssert.AreEqual(ticks, after.Select(line => line.LastChangeTick).ToArray());
        }

        [Test]
        public void RepeatedOverMaximumRequestIsAlsoANoOp()
        {
            var screen = new ScreenBuffer();
            screen.SetSize(160, 255);
            int calls = 0;
            screen.AddResizeNotifier(buffer => { ++calls; return 0; });
            screen.SetSize(161, 256);
            Assert.AreEqual(0, calls);
        }

        [Test]
        public void NotifiersSeeNormalizedDimensionsAndRetainScrollReturnContract()
        {
            var screen = new ScreenBuffer();
            screen.SetSize(3, 8);
            screen.ClearScreen();
            for (int i = 0; i < 5; ++i) screen.Print("row");
            int top = screen.TopRow;
            int calls = 0;
            screen.AddResizeNotifier(buffer =>
            {
                ++calls;
                Assert.AreEqual(1, buffer.RowCount);
                Assert.AreEqual(255, buffer.ColumnCount);
                Assert.AreEqual(1, buffer.GetBuffer().Count);
                return -1;
            });
            screen.SetSize(-1, 256);
            Assert.AreEqual(1, calls);
            // The one-row screen first moves down two rows to keep the cursor's row (VIEW-04),
            // then the notifier's -1 scrolls it up.
            Assert.AreEqual(top + 2 - 1, screen.TopRow);
        }

        [Test]
        public void NarrowingClampsOutputCursorBeforeFurtherPrinting()
        {
            var screen = new ScreenBuffer();
            screen.SetSize(3, 8);
            screen.ClearScreen();
            screen.MoveCursor(2, 7);
            screen.SetSize(2, 5);
            Assert.AreEqual(1, screen.CursorRowShow);
            Assert.AreEqual(4, screen.CursorColumnShow);
            screen.Print("X", false);
            Assert.IsTrue(screen.GetBuffer().Any(line => line.ToArray().Contains('X')));
        }

        [Test]
        public void NormalizedResizePropagatesSafeWidthToOverlay()
        {
            var screen = new ScreenBuffer();
            var overlay = new SubBuffer { Enabled = true, Fixed = true };
            overlay.SetSize(1, 50);
            overlay.Buffer[0][0] = 'X';
            screen.AddSubBuffer(overlay);
            screen.SetSize(-1, -1);
            Assert.AreEqual(1, overlay.ColumnCount);
            Assert.IsTrue(overlay.Buffer.All(line => line.Length == 1));
            Assert.AreEqual('X', screen.GetBuffer()[0][0]);
        }

        [Test]
        public void NoOpResizeDoesNotRebuildInputOverlay()
        {
            var editor = new InspectableEditor();
            foreach (char ch in "command") editor.Type(ch);
            var before = editor.InputOverlay.Buffer.ToArray();
            var ticks = before.Select(line => line.LastChangeTick).ToArray();
            editor.SetSize(editor.RowCount, editor.ColumnCount);
            for (int row = 0; row < before.Length; ++row) Assert.AreSame(before[row], editor.InputOverlay.Buffer[row]);
            CollectionAssert.AreEqual(ticks, editor.InputOverlay.Buffer.Select(line => line.LastChangeTick).ToArray());
        }

        [Test]
        public void ResizingLongInputDoesNotClampItsNegativeAnchorRow()
        {
            var editor = new InspectableEditor();
            editor.SetSize(3, 4);
            editor.ClearScreen();
            foreach (char ch in "abcdefghijklmnopqrstuvwxyz") editor.Type(ch);
            int anchor = editor.AbsoluteCursorRow - editor.TopRow;
            Assert.Less(anchor, 0);
            editor.SetSize(3, 5);
            Assert.AreEqual(anchor, editor.AbsoluteCursorRow - editor.TopRow);
            editor.Type('!');
            Assert.AreEqual(3, editor.GetBuffer().Count);
        }

        [Test]
        public void ScriptSuffixesRetainTheirExistingMinimaAndMaxima()
        {
            var screen = new ScreenBuffer();
            var terminal = new TerminalStruct(new SafeSharedObjects { Screen = screen });
            terminal.SetSuffix("HEIGHT", 0);
            terminal.SetSuffix("WIDTH", -1);
            Assert.AreEqual(3, screen.RowCount);
            Assert.AreEqual(15, screen.ColumnCount);
            terminal.SetSuffix("HEIGHT", 1000);
            terminal.SetSuffix("WIDTH", 1000);
            Assert.AreEqual(160, screen.RowCount);
            Assert.AreEqual(255, screen.ColumnCount);
        }

        [Test]
        public void RepeatingScriptSuffixDimensionsDoesNotNotify()
        {
            var screen = new ScreenBuffer();
            var terminal = new TerminalStruct(new SafeSharedObjects { Screen = screen });
            int calls = 0;
            screen.AddResizeNotifier(buffer => { ++calls; return 0; });
            terminal.SetSuffix("HEIGHT", screen.RowCount);
            terminal.SetSuffix("WIDTH", screen.ColumnCount);
            Assert.AreEqual(0, calls);
        }

        [Test]
        public void SeededResizeSequenceKeepsGridWritableAndNotifiesOnlyOnEffectiveChange()
        {
            var screen = new ScreenBuffer();
            var random = new Random(3104);
            int[] requests = { int.MinValue, -1, 0, 1, 2, 3, 15, 36, 50, 160, 161, 255, 256, 65535, int.MaxValue };
            int calls = 0;
            screen.AddResizeNotifier(buffer => { ++calls; return 0; });
            for (int iteration = 0; iteration < 100; ++iteration)
            {
                int rows = requests[random.Next(requests.Length)];
                int columns = requests[random.Next(requests.Length)];
                int oldRows = screen.RowCount;
                int oldColumns = screen.ColumnCount;
                int expectedRows = rows == 0 ? oldRows : Math.Max(1, Math.Min(160, rows));
                int expectedColumns = columns == 0 ? oldColumns : Math.Max(1, Math.Min(255, columns));
                int oldCalls = calls;
                screen.SetSize(rows, columns);
                Assert.AreEqual(expectedRows, screen.RowCount);
                Assert.AreEqual(expectedColumns, screen.ColumnCount);
                Assert.AreEqual(oldCalls + (oldRows != expectedRows || oldColumns != expectedColumns ? 1 : 0), calls);
                screen.Print("X", false);
                var buffer = screen.GetBuffer();
                Assert.AreEqual(expectedRows, buffer.Count);
                Assert.IsTrue(buffer.All(line => line.Length == expectedColumns));
            }
        }

        private class InspectableEditor : TextEditor
        {
            public SubBuffer InputOverlay { get { return LineSubBuffer; } }
        }

        [TestCase(-1, 4)]
        [TestCase(1, 0)]
        [TestCase(1, -1)]
        public void InvalidSubBufferSizeIsRejectedBeforeMutatingRowsOrTicks(int rows, int columns)
        {
            var overlay = new SubBuffer();
            overlay.SetSize(1, 4);
            overlay.Buffer[0][0] = 'X';
            var before = overlay.Buffer[0];
            ulong tick = before.LastChangeTick;
            Assert.Throws<ArgumentOutOfRangeException>(() => overlay.SetSize(rows, columns));
            Assert.AreEqual(1, overlay.RowCount);
            Assert.AreEqual(4, overlay.ColumnCount);
            Assert.AreSame(before, overlay.Buffer[0]);
            Assert.AreEqual(tick, before.LastChangeTick);
        }

        [Test]
        public void EmptySubBufferRemainsSupported()
        {
            var overlay = new SubBuffer();
            overlay.SetSize(0, 4);
            Assert.AreEqual(0, overlay.RowCount);
            Assert.AreEqual(0, overlay.Buffer.Count);
            overlay.SetSize(1, 4);
            overlay.Wipe();
            Assert.AreEqual(0, overlay.RowCount);
            Assert.AreEqual(4, overlay.ColumnCount);
        }
    }
}
