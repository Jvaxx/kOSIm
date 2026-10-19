using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using kOS.Safe.UserIO;

namespace kOS.Safe.Screen
{
    public class ScreenBuffer : IScreenBuffer
    {
        private const int DEFAULT_ROWS = 36;
        private const int DEFAULT_COLUMNS = 50;

        // Match the existing script-facing maxima, while allowing small model grids.
        public const int MAX_ROWS = 160;
        public const int MAX_COLUMNS = 255;

        private int topRow;
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

        public virtual int CursorRowShow { get { return CursorRow; } }

        public virtual int CursorColumnShow { get { return CursorColumn; } }

        public int RowCount { get; private set; }

        public int AbsoluteCursorRow
        {
            get { return CursorRow + topRow; }
            set { CursorRow = value - topRow; }
        }

        // Needed so the terminal knows when it's been scrolled, for its diffing purposes.
        public int TopRow { get { return topRow; } }

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

            // Output must remain writable after shrinking. Do not clamp negative row
            // anchors: TextEditor uses them for input extending above the viewport.
            CursorRow = Math.Min(CursorRow, RowCount - 1);
            CursorColumn = Math.Max(0, Math.Min(CursorColumn, ColumnCount - 1));
            int scrollDiff = Notifyees
                .Where(notifier => notifier != null)
                .Sum(notifier => notifier(this));
            ScrollVertical(scrollDiff);
        }

        public virtual int ScrollVertical(int deltaRows)
        {
            return ScrollVerticalInternal(deltaRows);
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
            if ((CursorRow + 1) >= RowCount)
            {
                // scrolling up
                AddNewBufferLines();
                ScrollVerticalInternal();
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

        public List<IScreenBufferLine> GetBuffer()
        {
            // base buffer
            int extraPadRows = Math.Max(0, (topRow + RowCount) - buffer.Count); // When screen extends past the buffer bottom., this is needed to prevent GetRange() exception.
            var mergedBuffer = new List<IScreenBufferLine>(buffer.GetRange(topRow, RowCount - extraPadRows));
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

                // Fixed overlays use viewport rows; others use absolute scrollback rows.
                // Widen before subtracting/adding so extreme positions cannot wrap into view.
                long mergeRow = subBuffer.Fixed ? subBuffer.PositionRow : (long)subBuffer.PositionRow - topRow;
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
            sb.Append("DebugDump ScreenBuffer: RowCount=" + RowCount + ", ColumnCount=" + ColumnCount + ", topRow=" + topRow + ", buffer.count=" + buffer.Count + "\n");
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
            CursorRow = 0;
            CursorColumn = 0;
        }

        private int ScrollVerticalInternal(int deltaRows = 1)
        {
            int maxTopRow = buffer.Count - RowCount; // refuse to allow a scroll past the end of the visible buffer.

            // boundary checks
            if (topRow + deltaRows < 0)
                deltaRows = -topRow;
            else if (topRow + deltaRows > maxTopRow)
                deltaRows = (maxTopRow - topRow);

            topRow += deltaRows;

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