using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using kOS.Safe.UserIO;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// The terminal's character grid: a list of buffer lines holding the <b>screen</b>,
    /// <see cref="RowCount"/> rows from the screen top (the buffer's last rows), where
    /// all output goes (<c>PRINT</c>, <c>PRINT AT</c> row 0 = the screen's top row, the cursor,
    /// the prompt), and above it the scrollback. (A screen made shorter keeps its bottom, the
    /// cursor's row: VIEW-04.)
    /// <br/>
    /// Screen and view (Phase 2, P2-4; decisions D1 and D2): what is displayed is a <b>view</b>,
    /// <see cref="RowCount"/> rows that PageUp/PageDown (<see cref="ScrollVertical"/>) move into
    /// the scrollback, shared by the GUI and every telnet client. Moving the view never moves
    /// where output goes (before P2-4 it did: VIEW-01). Output and typing at the prompt bring the
    /// view back to the screen. The scrollback keeps at most <see cref="MAX_SCROLLBACK"/> lines;
    /// older ones are dropped (VIEW-02). The screen itself is never trimmed.
    /// </summary>
    public class ScreenBuffer : IScreenBuffer
    {
        private const int DEFAULT_ROWS = 36;
        private const int DEFAULT_COLUMNS = 50;

        // Match the existing script-facing maxima, while allowing small model grids.
        public const int MAX_ROWS = 160;
        public const int MAX_COLUMNS = 255;

        /// <summary>The most lines kept above the screen (decision D2); older ones are dropped.</summary>
        public const int MAX_SCROLLBACK = 2000;

        private int topRow; // the screen's first line in the buffer
        private int viewOffset; // how many rows the view is above the screen (negative: below it)
        private int droppedRows; // scrollback lines dropped so far, so TopRow keeps counting
        private bool newOutputOutOfView;
        private readonly List<IScreenBufferLine> buffer;
        private readonly List<SubBuffer> subBuffers;

        public Queue<char> CharInputQueue { get; private set; }
        
        public int BeepsPending {get; set;}
        
        // These next 6 things really belong in TermWindow, but then they can't be reached from
        // TerminalStruct over in kOS.Safe because TermWindow is just too full of
        // Unity stuff to move it to kOS.Safe, or even provide all its methods
        // in an ITermWindow interface:
        // ---------------------------------------------------------------------------        
        /// <summary>True means the terminal screen should be shown in reversed colors.</summary>
        public bool ReverseScreen {get; set;}
        /// <summary>True means a beep should make the terminal screen flash silently in lieu of an audio beep.</summary>
        public bool VisualBeep {get; set;}
        public int CharacterPixelWidth { get; set; }
        public int CharacterPixelHeight { get; set; }
        public double Brightness { get; set; }

        
        public int ColumnCount { get; private set; }

        /// <summary>
        /// The cursor's row as displayed: in the view, so outside 0..RowCount-1 while the view
        /// is scrolled away from it. On the screen, it is <see cref="ScreenCursorRow"/>.
        /// </summary>
        public virtual int CursorRowShow { get { return ScreenCursorRow + (topRow - ViewTop); } }

        /// <summary>The cursor's row on the screen, as displayed (the prompt's input line included).</summary>
        protected virtual int ScreenCursorRow { get { return CursorRow; } }

        public virtual int CursorColumnShow { get { return CursorColumn; } }

        public int RowCount { get; private set; }

        public int AbsoluteCursorRow
        {
            get { return CursorRow + topRow; }
            set { CursorRow = value - topRow; }
        }

        /// <summary>
        /// The displayed view's first row, counted from the first line ever kept (dropped
        /// scrollback included), so that it changes exactly by how far the display scrolled.
        /// Needed so the terminal knows when it's been scrolled, for its diffing purposes.
        /// </summary>
        public int TopRow { get { return unchecked(droppedRows + ViewTop); } }

        /// <summary>How many rows the view is above the screen: 0 when it shows the screen.</summary>
        public int ViewOffset { get { return topRow - ViewTop; } }

        /// <summary>
        /// Output came while the view was away from the screen, and the view has not been
        /// back since (Phase 3, O2): the terminal shows a "new output" marker.
        /// </summary>
        public bool HasNewOutputOutOfView { get { return newOutputOutOfView; } }

        /// <summary>Lines above the screen (at most <see cref="MAX_SCROLLBACK"/>).</summary>
        public int ScrollbackRows { get { return topRow; } }

        /// <summary>Scrollback lines dropped since this screen was made, to keep at most <see cref="MAX_SCROLLBACK"/>.</summary>
        protected int DroppedRows { get { return droppedRows; } }

        // The view's first line in the buffer, kept within the buffer.
        private int ViewTop
        {
            get
            {
                int maxTop = Math.Max(0, buffer.Count - RowCount);
                return Math.Max(0, Math.Min(topRow - viewOffset, maxTop));
            }
        }

        protected int CursorRow { get; set; }

        protected int CursorColumn { get; set; }

        protected List<ResizeNotifier> Notifyees { get; set; }

        /// <summary>Delegate prototype expected by AddResizeNotifier</summary>
        /// <param name="sb">This screenbuffer telling the callback who it is</param>
        /// <returns>telling this screenbuffer how many vertical rows to scroll as a result of the resize.</returns>
        public delegate int ResizeNotifier(IScreenBuffer sb);

        public ScreenBuffer()
        {
            buffer = new List<IScreenBufferLine>();
            Notifyees = new List<ResizeNotifier>();

            subBuffers = new List<SubBuffer>();
            
            CharInputQueue = new Queue<char>();

            RowCount = DEFAULT_ROWS;
            ColumnCount = DEFAULT_COLUMNS;
            InitializeBuffer();
        }

        public void AddResizeNotifier(ResizeNotifier notifier)
        {
            if (Notifyees.IndexOf(notifier) < 0)
                Notifyees.Add(notifier);
        }

        public void RemoveResizeNotifier(ResizeNotifier notifier)
        {
            Notifyees.Remove(notifier);
        }

        public void RemoveAllResizeNotifiers()
        {
            Notifyees.Clear();
        }

        /// <summary>
        /// Zero leaves that axis unchanged (telnet's unspecified size). Other values
        /// are clamped to [1, MAX_ROWS/MAX_COLUMNS] before allocation or notification.
        /// An unchanged effective size does not rebuild rows or notify observers.
        /// </summary>
        public void SetSize(int rows, int columns)
        {
            rows = rows == 0 ? RowCount : Math.Max(1, Math.Min(rows, MAX_ROWS));
            columns = columns == 0 ? ColumnCount : Math.Max(1, Math.Min(columns, MAX_COLUMNS));
            if (rows == RowCount && columns == ColumnCount) return;

            RowCount = rows;
            ColumnCount = columns;
            ResizeBuffer();

            // A shorter screen keeps its bottom, as a terminal does: it moves down so the
            // cursor's row stays on it, and the rows above go into the scrollback. Before,
            // it kept its top and the cursor moved up onto older output (VIEW-04).
            int belowScreen = ScreenCursorRow - (RowCount - 1);
            if (belowScreen > 0)
            {
                CursorRow -= belowScreen;
                ScrollScreen(belowScreen); // a scrolled view stays where it was
            }
            // Rows left below the screen are dropped; output would scroll over them.
            int pastBottom = buffer.Count - (topRow + RowCount);
            if (pastBottom > 0)
                buffer.RemoveRange(topRow + RowCount, pastBottom);

            // Output must remain writable after shrinking. Do not clamp negative row
            // anchors: TextEditor uses them for input extending above the viewport.
            CursorRow = Math.Min(CursorRow, RowCount - 1);
            CursorColumn = Math.Max(0, Math.Min(CursorColumn, ColumnCount - 1));
            int scrollDiff = Notifyees
                .Where(notifier => notifier != null)
                .Sum(notifier => notifier(this));
            ScrollScreen(scrollDiff);
            viewOffset = ViewOffset; // the same view, as far as the new size allows
        }

        /// <summary>
        /// Move the view (PageUp/PageDown, the GUI's mouse wheel) by <paramref name="deltaRows"/>,
        /// negative up into the scrollback, without moving the screen where output goes (P2-4;
        /// before, this moved the screen too, VIEW-01). The view stays within the buffer.
        /// </summary>
        /// <returns>how many rows the view actually moved</returns>
        public virtual int ScrollVertical(int deltaRows)
        {
            int before = ViewTop;
            long target = Math.Max(0L, Math.Min((long)before + deltaRows, Math.Max(0, buffer.Count - RowCount)));
            viewOffset = topRow - (int)target;
            if (viewOffset <= 0)
                newOutputOutOfView = false; // back at the screen (or below it)
            return ViewTop - before;
        }

        /// <summary>
        /// Bring the view back to the screen (typing at the prompt). Output no longer does
        /// (Phase 3, O2): a view scrolled back stays where it is.
        /// </summary>
        protected void ReturnViewToScreen()
        {
            viewOffset = 0;
            newOutputOutOfView = false;
        }

        private void NoteOutput()
        {
            if (viewOffset != 0)
                newOutputOutOfView = true;
        }

        /// <summary>
        /// Marks the given section of rows in the buffer as dirty and
        /// in need of a diff check.
        /// </summary>
        /// <param name="startRow">Starting with this row number</param>
        /// <param name="numRows">for this many rows, or up to the max row the buffer has if this number is too large</param>
        public void MarkRowsDirty(int startRow, int numRows)
        {
            // Mark fewer rows than asked to if the reqeusted number would have blown past the end of the buffer:
            int numSafeRows = (numRows + startRow <= buffer.Count) ? numRows : buffer.Count - startRow;
            
            for( int i = 0; i < numSafeRows ; ++i)
                buffer[startRow + i].TouchTime();
        }

        public void MoveCursor(int row, int column)
        {
            if (row >= RowCount)
            {
                row = RowCount - 1;
                MoveToNextLine();
            }
            if (row < 0) row = 0;

            if (column >= ColumnCount) column = ColumnCount - 1;
            if (column < 0) column = 0;

            CursorRow = row;
            CursorColumn = column;
        }

        public void MoveToNextLine()
        {
            NoteOutput();
            if ((CursorRow + 1) >= RowCount)
            {
                // scrolling up
                AddNewBufferLines();
                ScrollScreen(1);
            }
            else
            {
                CursorRow++;
            }

            CursorColumn = 0;
        }

        public virtual void PrintAt(string textToPrint, int row, int column)
        {
            MoveCursor(row, column);
            Print(textToPrint, false);
        }

        public void Print(string textToPrint)
        {
            Print(textToPrint, true);
        }

        public void Print(string textToPrint, bool addNewLine)
        {
            NoteOutput();
            List<string> lines = SplitIntoLines(textToPrint);
            foreach (string line in lines)
            {
                PrintLine(line);

                if (CursorColumn > 0 && addNewLine)
                {
                    MoveToNextLine();
                    CursorColumn = 0;
                }
            }
        }

        protected void AddNewBufferLines(int howMany = 1)
        {
            while (howMany-- > 0)
                buffer.Add(new ScreenBufferLine(ColumnCount));
        }

        protected void ResizeBuffer()
        {
            // Grow or shrink the width of the buffer lines to match the new
            // value.  Note that this does not (yet) account for preserving lines and wrapping them.
            for (int row = 0; row < buffer.Count; ++row)
            {
                var newRow = new ScreenBufferLine(ColumnCount);
                newRow.ArrayCopyFrom(buffer[row], 0, 0, Math.Min(buffer[row].Length, ColumnCount));
                buffer[row] = newRow;
            }

            // Add more buffer lines if needed to pad out the rest of the screen:
            while (buffer.Count - topRow < RowCount)
                buffer.Add(new ScreenBufferLine(ColumnCount));
        }

        protected List<string> SplitIntoLines(string textToPrint)
        {
            var lineList = new List<string>();
            int availableColumns = ColumnCount - CursorColumn;

            string[] lines = textToPrint.Trim(new[] { '\r', '\n' }).Split('\n');

            foreach (string line in lines)
            {
                string lineToPrint = line.TrimEnd('\r');
                int startIndex = 0;

                while ((lineToPrint.Length - startIndex) > availableColumns)
                {
                    lineList.Add(lineToPrint.Substring(startIndex, availableColumns));
                    startIndex += availableColumns;
                    availableColumns = ColumnCount;
                }

                lineList.Add(lineToPrint.Substring(startIndex));
                availableColumns = ColumnCount;
            }

            return lineList;
        }

        public void ClearScreen()
        {
            buffer.Clear();
            InitializeBuffer();
        }

        public void AddSubBuffer(SubBuffer subBuffer)
        {
            subBuffers.Add(subBuffer);
            AddResizeNotifier(subBuffer.NotifyOfParentResize);
        }

        public void RemoveSubBuffer(SubBuffer subBuffer)
        {
            subBuffers.Remove(subBuffer);
            RemoveResizeNotifier(subBuffer.NotifyOfParentResize); // registered by AddSubBuffer (P2-3, LIFE-03)
        }

        /// <summary>The displayed rows: the view (the screen unless scrolled), overlays merged.</summary>
        public List<IScreenBufferLine> GetBuffer()
        {
            // base buffer
            int viewTop = ViewTop;
            int extraPadRows = Math.Max(0, (viewTop + RowCount) - buffer.Count); // When screen extends past the buffer bottom., this is needed to prevent GetRange() exception.
            var mergedBuffer = new List<IScreenBufferLine>(buffer.GetRange(viewTop, RowCount - extraPadRows));
            int lastLineWidth = mergedBuffer[mergedBuffer.Count - 1].Length;
            while (extraPadRows > 0)
            {
                mergedBuffer.Add(new ScreenBufferLine(lastLineWidth));
                --extraPadRows;
            }

            // merge sub buffers
            UpdateSubBuffers();
            foreach (SubBuffer subBuffer in subBuffers)
            {
                if (!subBuffer.Enabled) continue;

                // Fixed overlays use view rows; others use absolute buffer rows, so they
                // scroll with the view. Widen before subtracting/adding so extreme positions
                // cannot wrap into view.
                long mergeRow = subBuffer.Fixed ? subBuffer.PositionRow : (long)subBuffer.PositionRow - viewTop;
                int sourceRows = Math.Min(subBuffer.RowCount, subBuffer.Buffer.Count);
                long firstRow = Math.Max(0L, mergeRow);
                long endRow = Math.Min((long)mergedBuffer.Count, mergeRow + sourceRows);

                // Replace only the intersection. Keep the viewport height, row references,
                // and registration-order layering unchanged; never mutate the base buffer.
                for (long row = firstRow; row < endRow; ++row)
                    mergedBuffer[(int)row] = subBuffer.Buffer[(int)(row - mergeRow)];
            }

            return mergedBuffer;
        }

        // This was handy when trying to figure out what was going on.
        public string DebugDump()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("DebugDump ScreenBuffer: RowCount=" + RowCount + ", ColumnCount=" + ColumnCount + ", topRow=" + topRow + ", viewOffset=" + viewOffset + ", buffer.count=" + buffer.Count + "\n");
            for (int i = 0; i < buffer.Count; ++i)
            {
                sb.Append(" line " + i + " = [");
                for (int j = 0; j < buffer[i].Length; ++j)
                {
                    char ch = buffer[i][j];
                    sb.Append((int)ch < 32 ? (" \\" + (int)ch) : (" " + ch));
                }
                sb.Append("]\n");
            }
            foreach (SubBuffer sub in subBuffers)
                sb.Append(sub.DebugDump());
            return sb.ToString();
        }

        protected virtual void UpdateSubBuffers()
        {
            // so subclasses can do something with their subbuffers before they are merged
        }

        private void InitializeBuffer()
        {
            buffer.Clear();
            AddNewBufferLines(RowCount);

            topRow = 0;
            viewOffset = 0;
            newOutputOutOfView = false;
            CursorRow = 0;
            CursorColumn = 0;
        }

        /// <summary>
        /// Move the screen (where output goes) by <paramref name="deltaRows"/> within the buffer,
        /// as output reaching the bottom does, then drop the scrollback beyond
        /// <see cref="MAX_SCROLLBACK"/>. A view scrolled back stays on the same lines (Phase 3,
        /// O2; before, it kept its offset from the screen, and output returned it anyway), as
        /// far as the dropped scrollback allows; a view on the screen moves with it.
        /// </summary>
        /// <returns>how many rows the screen actually moved</returns>
        protected int ScrollScreen(int deltaRows)
        {
            int maxTopRow = buffer.Count - RowCount; // refuse to allow a scroll past the end of the visible buffer.

            // boundary checks
            if (topRow + deltaRows < 0)
                deltaRows = -topRow;
            else if (topRow + deltaRows > maxTopRow)
                deltaRows = (maxTopRow - topRow);

            topRow += deltaRows;
            if (viewOffset > 0)
                viewOffset += deltaRows;

            int excess = topRow - MAX_SCROLLBACK;
            if (excess > 0)
            {
                buffer.RemoveRange(0, excess);
                topRow -= excess;
                droppedRows = unchecked(droppedRows + excess);
            }
            if (viewOffset > topRow)
                viewOffset = topRow; // the view's lines were dropped: it shows the oldest left

            return deltaRows;
        }

        private void MoveColumn(int deltaPosition)
        {
            if (deltaPosition > 0)
            {
                CursorColumn += deltaPosition;
                while (CursorColumn >= ColumnCount)
                {
                    CursorColumn -= ColumnCount;
                    MoveToNextLine();
                }
            }
        }

        private void PrintLine(string textToPrint)
        {
            IScreenBufferLine lineBuffer = buffer[AbsoluteCursorRow];
            textToPrint = StripUnprintables(textToPrint);
            lineBuffer.ArrayCopyFrom(textToPrint.ToCharArray(), 0, CursorColumn);
            MoveColumn(textToPrint.Length);
        }
        
        private string StripUnprintables(string textToPrint)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char ch in textToPrint)
            {
                switch (ch)
                {
                    case (char)0x0007:
                    case (char)UnicodeCommand.BEEP:
                        ++BeepsPending;
                        break;
                    default:
                        if (0x0020 <= ch)
                            sb.Append(ch);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}