using System;
using System.Collections.Generic;
using System.Text;
using kOS.Safe.UserIO;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// A snapshot of the frozen screen image as it appears at a moment in time.
    /// </summary>
    public class ScreenSnapShot : IScreenSnapShot
    {
        public List<IScreenBufferLine> Buffer { get; private set;}
        public int TopRow {get; private set;}
        public int CursorColumn {get; private set;}
        public int CursorRow {get; private set;}
        public int RowCount {get; private set;}

        // Tweakable setting:
        // If two diff chunks are side by side and this close or closer, just merge them into one.
        // Its more sane then constantly jumping the cursor around.
        //  (i.e. if old buff is "HELLO, STEVEN, HOW IS THE WEATHER", and the
        //           new buff is "HELLO, STELLA, HAVE SOME OWLS."
        // It would be stupid to drive that change by saying:
        //    Jump cursor to column 10, write "LLA", then jump to column 16, write "AVE", then
        //    jump cursor to column 21, write "OME O", then jump cursor to column 28 and write "LS.".
        // When you could just say:
        //    Jump cursor to column 10, write "LLA, HAVE SOME OWLS.".
        // This setting is how far apart diff sections on the same row have to be before it will
        // perform a cursor jump to get from one to the other, rather than just overwriting the same
        // string as was already there.
        private const int JOIN_DIFF_DIST = 6;

        /// <summary>
        /// Make a screen snapshot of the current state of the screenbuffer.
        /// </summary>
        public ScreenSnapShot(IScreenBuffer fromScreen)
        {
            Buffer = fromScreen.GetBuffer();
            TopRow = fromScreen.TopRow;
            CursorColumn = fromScreen.CursorColumnShow;
            CursorRow = fromScreen.CursorRowShow;
            RowCount = fromScreen.RowCount;
        }
        
        // for me to fill in from scratch when making a deep copy      
        private ScreenSnapShot()
        {
        }

        /// <summary>
        /// A factory that constructs an empty screen buffer of a correct size for the fromScreen
        /// </summary>
        /// <param name="fromScreen">The screen - only used to determine the needed width/height</param>
        /// <returns>An empty snapshot buffer</returns>
        public static ScreenSnapShot EmptyScreen(IScreenBuffer fromScreen)
        {
            ScreenSnapShot newThing = new ScreenSnapShot();
            newThing.TopRow = fromScreen.TopRow;
            newThing.CursorColumn = fromScreen.CursorColumnShow;
            newThing.CursorRow = fromScreen.CursorRowShow;
            newThing.RowCount = fromScreen.RowCount;
            newThing.Buffer = new List<IScreenBufferLine>();
            for (int i = 0; i < newThing.RowCount ; ++i)
                newThing.Buffer.Add(new ScreenBufferLine(fromScreen.ColumnCount));
            return newThing;
        }
        
        /// <summary>
        /// Make a copy of me for later diffing against.
        /// Almost a deep copy.
        /// </summary>
        /// <returns>The new copy</returns>
        public IScreenSnapShot DeepCopy()
        {
            ScreenSnapShot newCopy = new ScreenSnapShot
            {
                TopRow = TopRow,
                CursorColumn = CursorColumn,
                CursorRow = CursorRow,
                RowCount = RowCount,
                Buffer = new List<IScreenBufferLine>(Buffer.Count)
            };

            // This will probably reset the timestamps on the rows, but for our purposes that's actually fine - we call this
            // when we want to get a fully sync'ed copy:
            foreach (IScreenBufferLine line in Buffer)
                newCopy.Buffer.Add(ScreenBufferLine.CopyOf(line)); // one copy of the characters (P-03), not two
            return newCopy;
        }

        /// <summary>
        /// True if the two snapshots have the same rows, row lengths, top row and cursor, so
        /// that one whose cells show the same text as the other (<see cref="DiffFrom"/> found
        /// nothing to send) can stand in for a copy of it as a later diff's base.
        /// </summary>
        public static bool SameLayout(IScreenSnapShot a, IScreenSnapShot b)
        {
            if (a == null || b == null || a.Buffer == null || b.Buffer == null)
                return false;
            if (a.TopRow != b.TopRow || a.RowCount != b.RowCount || a.CursorRow != b.CursorRow ||
                a.CursorColumn != b.CursorColumn || a.Buffer.Count != b.Buffer.Count)
                return false;
            for (int row = 0; row < a.Buffer.Count; ++row)
            {
                IScreenBufferLine lineA = a.Buffer[row];
                IScreenBufferLine lineB = b.Buffer[row];
                if (lineA == null || lineB == null || lineA.Length != lineB.Length)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Get a list of the operations that would make a terminal window look like this snapshot
        /// if you assume that beforehand it looked like the older snapshot you pass in.
        /// </summary>
        /// <param name="older">the older snapshot of a screen to diff from</param>
        /// <returns>the string that if output in order, will give you the desired changes</returns>
        public string DiffFrom(IScreenSnapShot older)
        {
            StringBuilder output = new StringBuilder();
            
            int verticalScroll = TopRow - older.TopRow;
            int trackCursorColumn = older.CursorColumn; // track the movements that will occur as the outputs happen.
            int trackCursorRow = older.CursorRow; // track the movements that will occur as the outputs happen.

            // First, output the command to make the terminal scroll to match:
            if (verticalScroll > 0) // scrolling text up (eyeballs panning down)
                output.Append((char)UnicodeCommand.SCROLLSCREENUPONE, verticalScroll); // A run of scrollup chars
            else if (verticalScroll < 0) // scrolling text down (eyeballs panning up)
                output.Append((char)UnicodeCommand.SCROLLSCREENDOWNONE, -verticalScroll); // A run of scrolldown chars

            // Check each row:
            for (int newRowNum = 0 ; newRowNum < RowCount ; ++newRowNum)
            {
                // Account for the diff due to the scrolling:
                int oldRowNum = newRowNum + verticalScroll;

                IScreenBufferLine newRow = Buffer[newRowNum];
                // A missing old row reads as blanks:
                IScreenBufferLine olderRow = (oldRowNum >= 0 && oldRowNum < older.Buffer.Count) ? older.Buffer[oldRowNum] : null;
                int olderLength = olderRow == null ? 0 : olderRow.Length;
                
                // if new row is an empty dummy, then it is diffed as blanks the old row's length:
                int width = newRow.Length == 0 ? olderLength : newRow.Length;
                // Every row is checked by content (VIEW-05). Upstream skipped a row unless it
                // changed after the older snapshot was copied, but a row can show other text
                // without being edited: the input line's overlay moves down when text is
                // printed above it (its rows keep their change time), an overlay moving away
                // uncovers a base row, the view moves. Comparing costs about what the
                // DeepCopy of every row after each diff already costs.
                // The changes are written as they are found, a chunk at a time (P-04: an
                // unchanged row allocates nothing).
                int chunkStart = -1;
                int chunkEnd = -1;
                
                for (int newCol = 0 ; newCol < width ; ++newCol)
                {
                    // Check if they differ, but in a way that treats ' ' and 0x00 as identical, and treats a shorter
                    // old row as if it has been padded with spaces:
                    if (Cell(newRow, newCol) != Cell(olderRow, newCol))
                    {
                        // Start a new diff chunk if there isn't one yet, or the diff is a long enough distance from the existing one:
                        if (chunkStart < 0 || chunkEnd < newCol - JOIN_DIFF_DIST)
                        {
                            if (chunkStart >= 0)
                                WriteChunk(output, newRow, newRowNum, chunkStart, chunkEnd, ref trackCursorColumn, ref trackCursorRow);
                            chunkStart = newCol;
                        }
                        chunkEnd = newCol; // else stretch the existing diff chunk to here.
                    }
                }
                if (chunkStart >= 0)
                    WriteChunk(output, newRow, newRowNum, chunkStart, chunkEnd, ref trackCursorColumn, ref trackCursorRow);
            }
                    
            // Now set the cursor back to the right spot one more time, unless it's already there:
            int cursorRow = TeleportRow(CursorRow, RowCount);
            if (trackCursorRow != cursorRow || trackCursorColumn != CursorColumn)
            {
                output.Append((char)UnicodeCommand.TELEPORTCURSOR);
                output.Append((char)CursorColumn);
                output.Append((char)cursorRow);
            }
            return output.ToString();
        }

        /// <summary>A cell as a viewer shows it: a null, or a cell past the row's end (or of no row), is a space.</summary>
        private static char Cell(IScreenBufferLine row, int column)
        {
            if (row == null || column >= row.Length)
                return ' ';
            char ch = row[column];
            return ch == '\0' ? ' ' : ch;
        }

        /// <summary>
        /// Write one chunk of changes: the cells from <paramref name="startCol"/> to
        /// <paramref name="endCol"/>, after moving the cursor there unless it already is.
        /// </summary>
        private static void WriteChunk(StringBuilder output, IScreenBufferLine row, int rowNum, int startCol, int endCol,
            ref int trackCursorColumn, ref int trackCursorRow)
        {
            // If the change starts right where the cursor happens to be (i.e. when typing, one char
            // at current cursor position will typically be the only diff from one update to the next),
            // then don't bother moving the cursor first, else move it to the new pos:
            if (trackCursorRow != rowNum || startCol != trackCursorColumn)
            {
                output.Append((char)UnicodeCommand.TELEPORTCURSOR);
                output.Append((char)startCol);
                output.Append((char)rowNum);
            }

            // the bit of the row to print at this location, with nulls made into spaces so the
            // telnet terminal will print correctly.
            for (int col = startCol; col <= endCol; ++col)
                output.Append(Cell(row, col));

            trackCursorColumn = endCol + 1;
            trackCursorRow = rowNum;
        }

        /// <summary>
        /// The row a viewer's cursor is sent to: the cursor's row, kept within the
        /// <paramref name="rowCount"/> displayed rows. The cursor is outside them while the view
        /// is scrolled away from it (P2-4); a telnet terminal always shows a cursor, so it waits
        /// at the nearest edge.
        /// </summary>
        public static int TeleportRow(int cursorRow, int rowCount)
        {
            return Math.Max(0, Math.Min(cursorRow, rowCount - 1));
        }

    }
}
