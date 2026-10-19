using System;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// A selection of character cells in the GUI terminal (Phase 3, O3), as text editors
    /// select: from one cell boundary to another in reading order, so the rows between are
    /// selected whole. Rows are counted as <see cref="ScreenBuffer.TopRow"/> counts them
    /// (dropped scrollback included), so a selection stays on its text while output scrolls
    /// it or the view moves. A click without a drag selects nothing. Main thread only.
    /// </summary>
    public sealed class TerminalSelection
    {
        private bool active;
        private int anchorRow, anchorColumn, endRow, endColumn;

        /// <summary>The mouse button is down and moves the selection's end.</summary>
        public bool IsDragging { get; private set; }

        public bool IsEmpty { get { return !active || (anchorRow == endRow && anchorColumn == endColumn); } }

        /// <summary>The first selected row (reading order).</summary>
        public int StartRow { get { return AnchorFirst ? anchorRow : endRow; } }

        /// <summary>The last selected row (reading order).</summary>
        public int EndRow { get { return AnchorFirst ? endRow : anchorRow; } }

        private int StartColumn { get { return AnchorFirst ? anchorColumn : endColumn; } }
        private int EndColumn { get { return AnchorFirst ? endColumn : anchorColumn; } }

        private bool AnchorFirst
        {
            get { return anchorRow < endRow || (anchorRow == endRow && anchorColumn <= endColumn); }
        }

        /// <param name="row">the row, as <see cref="ScreenBuffer.TopRow"/> counts</param>
        /// <param name="column">the cell boundary: 0 before the first cell, the column count after the last</param>
        public void Begin(int row, int column)
        {
            active = true;
            anchorRow = endRow = row;
            anchorColumn = endColumn = Math.Max(0, column);
            IsDragging = true;
        }

        public void DragTo(int row, int column)
        {
            if (!IsDragging) return;
            endRow = row;
            endColumn = Math.Max(0, column);
        }

        public void EndDrag()
        {
            IsDragging = false;
        }

        public void Clear()
        {
            active = false;
            IsDragging = false;
        }

        /// <summary>
        /// The selected cells of one row: <paramref name="from"/> to <paramref name="to"/>
        /// (exclusive), within <paramref name="columnCount"/>.
        /// </summary>
        /// <returns>false when no cell of the row is selected</returns>
        public bool TryGetColumns(int row, int columnCount, out int from, out int to)
        {
            from = to = 0;
            if (IsEmpty || row < StartRow || row > EndRow) return false;
            from = row == StartRow ? Math.Min(StartColumn, columnCount) : 0;
            to = row == EndRow ? Math.Min(EndColumn, columnCount) : columnCount;
            return to > from;
        }
    }
}
