using System;
using System.Collections.Generic;
using System.Linq;
using kOS.Safe.Screen;
using NUnit.Framework;

namespace kOS.Safe.Test.Terminal
{
    [TestFixture]
    public class ScreenBufferOverlayTest
    {
        private ScreenBuffer screen;

        [SetUp]
        public void Setup()
        {
            screen = new ScreenBuffer();
            screen.SetSize(3, 4);
            screen.ClearScreen();
            for (int row = 0; row < 3; ++row)
                screen.PrintAt(new string((char)('a' + row), 3), row, 0);
        }

        private static SubBuffer Overlay(int position, int rows, int columns = 4, bool fixedPosition = true)
        {
            var overlay = new SubBuffer { Enabled = true, Fixed = fixedPosition, PositionRow = position };
            overlay.SetSize(rows, columns);
            for (int row = 0; row < rows; ++row)
                overlay.Buffer[row].ArrayCopyFrom(new string((char)('A' + row), columns).ToCharArray(), 0, 0);
            return overlay;
        }

        // Independent cell-by-cell oracle: no range insertion/removal or intersection arithmetic.
        private static void ApplyOverlay(string[] expected, SubBuffer overlay, int topRow)
        {
            if (!overlay.Enabled) return;
            for (int row = 0; row < expected.Length; ++row)
            {
                long sourceRow = (long)row + (overlay.Fixed ? 0 : topRow) - overlay.PositionRow;
                if (sourceRow >= 0 && sourceRow < overlay.RowCount && sourceRow < overlay.Buffer.Count)
                    expected[row] = overlay.Buffer[(int)sourceRow].ToString();
            }
        }

        private static void AssertRows(ScreenBuffer buffer, string[] expected)
        {
            var actual = buffer.GetBuffer();
            Assert.AreEqual(buffer.RowCount, actual.Count, "Composition must not grow or shrink the viewport");
            CollectionAssert.AreEqual(expected, actual.Select(line => line.ToString()).ToArray());
        }

        [TestCase(-5, 2)] // completely above
        [TestCase(-2, 2)] // touches top but does not intersect
        [TestCase(-1, 2)] // partly above
        [TestCase(-2, 7)] // spans both boundaries
        [TestCase(0, 1)]
        [TestCase(0, 3)]
        [TestCase(1, 1)]
        [TestCase(1, 4)] // partly below
        [TestCase(2, 1)]
        [TestCase(3, 2)] // touches bottom but does not intersect
        [TestCase(5, 2)] // completely below
        [TestCase(int.MinValue, 2)]
        [TestCase(int.MaxValue, 2)]
        [TestCase(int.MaxValue - 1, 3)] // position + height would overflow int
        public void FixedOverlayCopiesOnlyVisibleSourceRows(int position, int rows)
        {
            var expected = screen.GetBuffer().Select(line => line.ToString()).ToArray();
            var overlay = Overlay(position, rows);
            ApplyOverlay(expected, overlay, screen.TopRow);
            screen.AddSubBuffer(overlay);
            AssertRows(screen, expected);
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        [TestCase(int.MinValue)] // position - topRow would overflow int
        [TestCase(int.MaxValue)]
        public void AbsoluteOverlayUsesScrollbackCoordinates(int position)
        {
            screen.Print("tail");
            Assert.Greater(screen.TopRow, 0);
            var overlay = Overlay(position, 3, fixedPosition: false);
            var expected = screen.GetBuffer().Select(line => line.ToString()).ToArray();
            ApplyOverlay(expected, overlay, screen.TopRow);
            screen.AddSubBuffer(overlay);
            AssertRows(screen, expected);

            screen.ScrollVertical(-screen.TopRow);
            overlay.Enabled = false;
            expected = screen.GetBuffer().Select(line => line.ToString()).ToArray();
            overlay.Enabled = true;
            ApplyOverlay(expected, overlay, screen.TopRow);
            AssertRows(screen, expected);
        }

        [Test]
        public void DisabledAndEmptyOverlaysLeaveBaseRowsUnchanged()
        {
            var expected = screen.GetBuffer().Select(line => line.ToString()).ToArray();
            var disabled = Overlay(-1, 5);
            disabled.Enabled = false;
            screen.AddSubBuffer(disabled);
            screen.AddSubBuffer(Overlay(1, 0));
            var wiped = Overlay(-1, 4);
            wiped.Wipe();
            screen.AddSubBuffer(wiped);
            AssertRows(screen, expected);
        }

        [Test]
        public void ShortenedPublicOverlayBufferUsesOnlyAvailableRows()
        {
            var expected = screen.GetBuffer().Select(line => line.ToString()).ToArray();
            var overlay = Overlay(-1, 4);
            overlay.Buffer.RemoveRange(2, 2);
            ApplyOverlay(expected, overlay, screen.TopRow);
            screen.AddSubBuffer(overlay);
            AssertRows(screen, expected);
            overlay.Buffer.Clear();
            AssertRows(screen, new[] { "aaa\0", "bbb\0", "ccc\0" });
        }

        [Test]
        public void LaterOverlaysWinWithoutMutatingBaseOrEarlierLayers()
        {
            var baseline = screen.GetBuffer().Select(line => line.ToString()).ToArray();
            var first = Overlay(-1, 5);
            var second = Overlay(1, 3);
            var firstRows = first.Buffer.Select(line => line.ToString()).ToArray();
            var expected = (string[])baseline.Clone();
            ApplyOverlay(expected, first, 0);
            ApplyOverlay(expected, second, 0);
            screen.AddSubBuffer(first);
            screen.AddSubBuffer(second);
            AssertRows(screen, expected);
            AssertRows(screen, expected); // repeat reads do not modify any layer
            var composed = screen.GetBuffer();
            Assert.AreSame(first.Buffer[1], composed[0]);
            Assert.AreSame(second.Buffer[0], composed[1]);
            CollectionAssert.AreEqual(firstRows, first.Buffer.Select(line => line.ToString()).ToArray());
            first.Enabled = second.Enabled = false;
            AssertRows(screen, baseline);
        }

        [Test]
        public void SeededOverlayCompositionsMatchReferenceAcrossResizeAndScroll()
        {
            var random = new Random(3100);
            for (int iteration = 0; iteration < 200; ++iteration)
            {
                var buffer = new ScreenBuffer();
                buffer.SetSize(random.Next(1, 9), random.Next(1, 9));
                buffer.ClearScreen();
                for (int row = 0; row < 15; ++row) buffer.Print("x");
                buffer.ScrollVertical(-random.Next(0, 16));
                var overlays = new List<SubBuffer>();
                for (int layer = 0; layer < 4; ++layer)
                {
                    var overlay = Overlay(random.Next(-12, 25), random.Next(0, 15),
                        buffer.ColumnCount, random.Next(2) == 0);
                    overlay.Enabled = random.Next(4) != 0;
                    overlays.Add(overlay);
                    buffer.AddSubBuffer(overlay);
                }
                buffer.SetSize(random.Next(1, 9), random.Next(1, 9));
                var expected = new string[buffer.RowCount];
                var enabled = overlays.Select(overlay => overlay.Enabled).ToArray();
                foreach (var overlay in overlays) overlay.Enabled = false;
                var baseRows = buffer.GetBuffer();
                for (int row = 0; row < expected.Length; ++row) expected[row] = baseRows[row].ToString();
                for (int layer = 0; layer < overlays.Count; ++layer)
                {
                    overlays[layer].Enabled = enabled[layer];
                    ApplyOverlay(expected, overlays[layer], buffer.TopRow);
                }
                AssertRows(buffer, expected);
            }
        }

        private class InspectableTextEditor : TextEditor
        {
            public SubBuffer InputOverlay { get { return LineSubBuffer; } }
        }

        [Test]
        public void LongInputRemainsClippedAfterNarrowingAndWidening()
        {
            var editor = new InspectableTextEditor();
            editor.SetSize(3, 4);
            editor.ClearScreen();
            foreach (char ch in "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789") editor.Type(ch);
            foreach (int columns in new[] { 4, 3, 6 })
            {
                editor.SetSize(3, columns);
                editor.Type('!'); // rebuild wrapped input after the parent resize
                var actual = editor.GetBuffer(); // updates the input overlay position before merging
                long mergeRow = (long)editor.InputOverlay.PositionRow - editor.TopRow;
                Assert.Less(mergeRow, 0, "Long command must exercise top clipping");
                Assert.AreEqual(3, actual.Count);
                for (int row = 0; row < actual.Count; ++row)
                {
                    Assert.AreEqual(columns, actual[row].Length);
                    Assert.AreEqual(editor.InputOverlay.Buffer[(int)(row - mergeRow)].ToString(), actual[row].ToString());
                }
            }
        }

        [Test]
        public void ARemovedOverlayNoLongerFollowsTheScreenSize()
        {
            // P2-3, LIFE-03: RemoveSubBuffer also unregisters the resize notifier AddSubBuffer registered.
            SubBuffer kept = Overlay(0, 1);
            SubBuffer removed = Overlay(1, 1);
            screen.AddSubBuffer(kept);
            screen.AddSubBuffer(removed);

            screen.RemoveSubBuffer(removed);
            screen.SetSize(3, 7);

            Assert.AreEqual(7, kept.ColumnCount);
            Assert.AreEqual(4, removed.ColumnCount);
        }
    }
}
