using System;
using System.Collections.Generic;
using System.Text;
using kOS.Safe.Screen;
using kOS.Safe.UserIO;
using NUnit.Framework;

namespace kOS.Safe.Test.Terminal
{
    /// <summary>
    /// The snapshot's per-refresh costs (review P-03, P-04): <see cref="ScreenSnapShot.DiffFrom"/>
    /// sends exactly what the previous version sent (a copy of it is kept below as the
    /// reference) without allocating for unchanged rows, <see cref="ScreenSnapShot.DeepCopy"/>
    /// copies each row once, and <see cref="ScreenSnapShot.SameLayout"/> tells when a viewer's
    /// copy can be kept.
    /// </summary>
    [TestFixture]
    public class ScreenSnapshotCostTest
    {
        private static readonly char[] cells = { '\0', ' ', 'a', 'b', 'c' };

        /// <summary>A screen of the size, scrolled by <paramref name="scrolledLines"/>, the cursor where asked.</summary>
        private static ScreenBuffer Screen(int rows, int columns, int scrolledLines, int cursorRow, int cursorColumn)
        {
            var screen = new ScreenBuffer();
            screen.SetSize(rows, columns);
            screen.ClearScreen();
            for (int i = 0; i < scrolledLines; ++i)
                screen.Print("x");
            screen.MoveCursor(cursorRow, cursorColumn);
            return screen;
        }

        private static ScreenBufferLine Line(string text)
        {
            var line = new ScreenBufferLine(text.Length);
            line.ArrayCopyFrom(text.ToCharArray(), 0, 0);
            return line;
        }

        private static string RandomText(Random random, int length)
        {
            var text = new char[length];
            for (int i = 0; i < length; ++i)
                text[i] = cells[random.Next(cells.Length)];
            return new string(text);
        }

        /// <summary>
        /// A snapshot of a random screen whose rows are then replaced by random ones: other
        /// lengths (empty rows included, as a screen pads them), nulls and spaces, and for an
        /// older snapshot fewer or more rows than shown.
        /// </summary>
        private static ScreenSnapShot RandomSnapshot(Random random, int rows, int columns, bool older)
        {
            ScreenBuffer screen = Screen(rows, columns, random.Next(2 * rows + 2), random.Next(rows), random.Next(columns)); // scrolled by up to two screens
            var snapshot = new ScreenSnapShot(screen);
            List<IScreenBufferLine> buffer = snapshot.Buffer;
            for (int row = 0; row < buffer.Count; ++row)
            {
                int pick = random.Next(10);
                int length = pick == 0 ? 0 : pick == 1 ? random.Next(columns + 4) : columns;
                buffer[row] = Line(RandomText(random, length));
            }
            if (older && random.Next(3) == 0)
            {
                if (random.Next(2) == 0 && buffer.Count > 1)
                    buffer.RemoveRange(buffer.Count / 2, buffer.Count - buffer.Count / 2);
                else
                    buffer.Add(Line(RandomText(random, columns)));
            }
            return snapshot;
        }

        /// <summary>A newer screen as an edit of <paramref name="older"/>: a few cells changed, at any distance apart.</summary>
        private static ScreenSnapShot EditedSnapshot(Random random, ScreenSnapShot older, int columns)
        {
            var newer = (ScreenSnapShot)older.DeepCopy();
            int edits = random.Next(6);
            for (int i = 0; i < edits; ++i)
            {
                IScreenBufferLine line = newer.Buffer[random.Next(newer.RowCount)];
                if (line.Length > 0)
                    line[random.Next(line.Length)] = cells[random.Next(cells.Length)];
            }
            return newer;
        }

        [Test]
        public void TheDiffIsTheSameAsBefore()
        {
            var random = new Random(20261020);
            for (int round = 0; round < 3000; ++round)
            {
                int rows = 1 + random.Next(8);
                int columns = 1 + random.Next(24);
                ScreenSnapShot older = RandomSnapshot(random, rows, columns, true);
                ScreenSnapShot newer = random.Next(2) == 0 && older.Buffer.Count >= older.RowCount && older.RowCount == rows
                    ? EditedSnapshot(random, older, columns)
                    : RandomSnapshot(random, rows, columns, false);
                Assert.AreEqual(ReferenceDiff(newer, older), newer.DiffFrom(older), "round " + round);
            }
        }

        [Test]
        public void ChangesTheJoinDistanceApartAreSentAsOneChunkAndFartherAsTwo()
        {
            ScreenBuffer screen = Screen(2, 30, 0, 0, 0);
            var older = new ScreenSnapShot(screen).DeepCopy();
            foreach (int gap in new[] { 5, 6, 7, 8 })
            {
                var newer = (ScreenSnapShot)older.DeepCopy();
                newer.Buffer[1][2] = 'a';
                newer.Buffer[1][2 + gap] = 'b';
                Assert.AreEqual(ReferenceDiff(newer, older), newer.DiffFrom(older), "gap " + gap);
            }
        }

        [Test]
        public void AnUnchangedScreenIsDiffedWithoutAllocatingForItsRows()
        {
            long small = DiffAllocation(5, 20);
            long large = DiffAllocation(100, 200);
            Assert.AreEqual(small, large, "allocation grew with the screen's size");
            Assert.Less(large, 256, "bytes allocated per diff of an unchanged screen");
        }

        private static long DiffAllocation(int rows, int columns)
        {
            ScreenBuffer screen = Screen(rows, columns, 0, 0, 0);
            for (int row = 0; row < rows; ++row)
                screen.PrintAt(new string('z', columns / 2), row, 0);
            var newer = new ScreenSnapShot(screen);
            IScreenSnapShot older = newer.DeepCopy();
            Assert.AreEqual("", newer.DiffFrom(older));
            return AllocationCounter.BytesAllocated(() => newer.DiffFrom(older), 10) / 10;
        }

        [Test]
        public void ACopyKeepsTheTextOfTheMomentInItsOwnLines()
        {
            ScreenBuffer screen = Screen(3, 10, 0, 0, 0);
            screen.PrintAt("before", 1, 0);
            var snapshot = new ScreenSnapShot(screen);
            IScreenSnapShot copy = snapshot.DeepCopy();
            screen.PrintAt("after!", 1, 0);

            Assert.AreEqual("after!", snapshot.Buffer[1].ToString().Substring(0, 6), "a snapshot holds the screen's own lines");
            Assert.AreEqual("before", copy.Buffer[1].ToString().Substring(0, 6));
            Assert.AreEqual(snapshot.RowCount, copy.RowCount);
            Assert.AreEqual(snapshot.TopRow, copy.TopRow);
            Assert.AreEqual(snapshot.CursorRow, copy.CursorRow);
            Assert.AreEqual(snapshot.CursorColumn, copy.CursorColumn);
            for (int row = 0; row < copy.Buffer.Count; ++row)
                Assert.AreNotSame(snapshot.Buffer[row], copy.Buffer[row]);
        }

        [Test]
        public void ACopyCopiesEachRowOnce()
        {
            const int rows = 50, columns = 255;
            var snapshot = new ScreenSnapShot(Screen(rows, columns, 0, 0, 0));
            long bytes = AllocationCounter.BytesAllocated(() => snapshot.DeepCopy(), 10) / 10;
            long oneCopyOfTheText = rows * columns * sizeof(char);
            // One copy: the text, plus a line object, an array header and a list slot a row.
            // The previous code copied the text twice.
            Assert.Less(bytes, oneCopyOfTheText * 3 / 2, "bytes per copy");
        }

        [Test]
        public void SameLayoutComparesRowsLengthsTopRowAndCursor()
        {
            ScreenBuffer screen = Screen(4, 10, 0, 1, 2);
            var snapshot = new ScreenSnapShot(screen);
            Assert.IsTrue(ScreenSnapShot.SameLayout(snapshot, snapshot.DeepCopy()));
            Assert.IsFalse(ScreenSnapShot.SameLayout(snapshot, null));

            var shorterRow = (ScreenSnapShot)snapshot.DeepCopy();
            shorterRow.Buffer[2] = Line("abc");
            Assert.IsFalse(ScreenSnapShot.SameLayout(snapshot, shorterRow), "a row of another length");

            var fewerRows = (ScreenSnapShot)snapshot.DeepCopy();
            fewerRows.Buffer.RemoveAt(3);
            Assert.IsFalse(ScreenSnapShot.SameLayout(snapshot, fewerRows), "fewer rows");

            screen.MoveCursor(2, 2);
            Assert.IsFalse(ScreenSnapShot.SameLayout(snapshot, new ScreenSnapShot(screen)), "the cursor's row");
            screen.MoveCursor(1, 3);
            Assert.IsFalse(ScreenSnapShot.SameLayout(snapshot, new ScreenSnapShot(screen)), "the cursor's column");
            screen.MoveCursor(1, 2);
            Assert.IsTrue(ScreenSnapShot.SameLayout(snapshot, new ScreenSnapShot(screen)));
            screen.Print("\n\n\n\n");
            Assert.IsFalse(ScreenSnapShot.SameLayout(snapshot, new ScreenSnapShot(screen)), "scrolled");
        }

        /// <summary>
        /// <see cref="ScreenSnapShot.DiffFrom"/> as it was before review P-04 (commit
        /// <c>5a0de50b</c>), less its commented-out code: the reference for the output.
        /// </summary>
        private static string ReferenceDiff(IScreenSnapShot newer, IScreenSnapShot older)
        {
            const int JOIN_DIFF_DIST = 6;
            StringBuilder output = new StringBuilder();

            int verticalScroll = newer.TopRow - older.TopRow;
            int trackCursorColumn = older.CursorColumn;
            int trackCursorRow = older.CursorRow;

            if (verticalScroll > 0)
                output.Append(new String((char)UnicodeCommand.SCROLLSCREENUPONE, verticalScroll));
            else if (verticalScroll < 0)
                output.Append(new String((char)UnicodeCommand.SCROLLSCREENDOWNONE, -verticalScroll));

            for (int newRowNum = 0; newRowNum < newer.RowCount; ++newRowNum)
            {
                int oldRowNum = newRowNum + verticalScroll;

                IScreenBufferLine newRow = newer.Buffer[newRowNum];
                bool oldRowExists = (oldRowNum >= 0 && oldRowNum < older.Buffer.Count);
                IScreenBufferLine olderRow = oldRowExists ? older.Buffer[oldRowNum] : new ScreenBufferLine(0);

                if (newRow.Length == 0)
                    newRow = new ScreenBufferLine(olderRow.Length);
                var diffs = new List<int[]>(); // start, end

                for (int newCol = 0; newCol < newRow.Length; ++newCol)
                {
                    bool oldRowTooShort = newCol >= olderRow.Length;
                    char newChar = (newRow[newCol] == '\0' ? ' ' : newRow[newCol]);
                    char oldChar = (oldRowTooShort ? ' ' : (olderRow[newCol] == '\0' ? ' ' : olderRow[newCol]));
                    if (newChar != oldChar)
                    {
                        if (diffs.Count == 0 || diffs[diffs.Count - 1][1] < newCol - JOIN_DIFF_DIST)
                            diffs.Add(new[] { newCol, newCol });
                        else
                            diffs[diffs.Count - 1][1] = newCol;
                    }
                }

                string newRowText = newRow.ToString();

                foreach (int[] diff in diffs)
                {
                    string moveString;
                    if (trackCursorRow == newRowNum && diff[0] == trackCursorColumn)
                        moveString = "";
                    else
                        moveString = String.Format("{0}{1}{2}",
                                                   (char)UnicodeCommand.TELEPORTCURSOR,
                                                   (char)diff[0],
                                                   (char)newRowNum);

                    string content = newRowText.Substring(diff[0], diff[1] - diff[0] + 1).Replace('\0', ' ');

                    output.Append(String.Format("{0}{1}", moveString, content));

                    trackCursorColumn = diff[1] + 1;
                    trackCursorRow = newRowNum;
                }
            }

            int cursorRow = ScreenSnapShot.TeleportRow(newer.CursorRow, newer.RowCount);
            if (trackCursorRow != cursorRow || trackCursorColumn != newer.CursorColumn)
            {
                output.Append(String.Format("{0}{1}{2}",
                                            (char)UnicodeCommand.TELEPORTCURSOR,
                                            (char)newer.CursorColumn,
                                            (char)cursorRow));
            }
            return output.ToString();
        }
    }
}
