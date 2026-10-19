using System.Collections.Generic;
using kOS.Safe.Screen;

namespace kOS.Safe.UserIO
{
    /// <summary>
    /// Text the GUI terminal copies to the clipboard (Phase 3, P3-4): the rows in view.
    /// The last error is kept by <see cref="PrintedErrorRecorder"/>.
    /// </summary>
    public static class TerminalCopy
    {
        /// <summary>
        /// The rows the terminal shows now, as drawn: the view (scrolled back or not), with the
        /// line being typed and the overlays. Blank cells are spaces; trailing spaces and
        /// trailing blank rows are left out. Rows are joined with LF. Empty when the view is blank.
        /// </summary>
        public static string VisibleText(IScreenBuffer screen)
        {
            List<IScreenBufferLine> view = screen.GetBuffer();
            var rows = new List<string>(view.Count);
            foreach (IScreenBufferLine line in view)
                rows.Add(line == null ? string.Empty : line.ToString().Replace('\0', ' ').TrimEnd(' '));
            while (rows.Count > 0 && rows[rows.Count - 1].Length == 0)
                rows.RemoveAt(rows.Count - 1);
            return string.Join("\n", rows.ToArray());
        }

        /// <summary>How many lines a copied text holds (0 for none).</summary>
        public static int LineCount(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int count = 1;
            foreach (char ch in text)
                if (ch == '\n') ++count;
            return count;
        }
    }
}
