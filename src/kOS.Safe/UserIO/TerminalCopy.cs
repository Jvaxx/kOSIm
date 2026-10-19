using System.Collections.Generic;
using System.Text;
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

        /// <summary>
        /// The selected text (Phase 3, O3): each row's selected cells, blanks as spaces and
        /// trailing spaces left out, rows joined with LF; dropped rows are skipped. Empty for
        /// an empty selection.
        /// </summary>
        public static string SelectionText(ScreenBuffer screen, TerminalSelection selection)
        {
            if (selection.IsEmpty) return string.Empty;
            var rows = new List<string>();
            for (int row = selection.StartRow; row <= selection.EndRow; ++row)
            {
                IScreenBufferLine line = screen.GetLineAt(row);
                if (line == null) continue;
                int from, to;
                if (!selection.TryGetColumns(row, line.Length, out from, out to)) continue;
                rows.Add(line.ToString().Substring(from, to - from).Replace('\0', ' ').TrimEnd(' '));
            }
            return WithoutControlCharacters(string.Join("\n", rows.ToArray()));
        }

        /// <summary>
        /// The text without C0 control characters other than tab and LF (COPY-01). A syntax
        /// error's verbose description marks the bad token with chars 26 and 27
        /// (<c>KOSParseException</c>); the screen does not keep them, but the printed error text
        /// did, and an editor would show them as ^Z^[.
        /// </summary>
        public static string WithoutControlCharacters(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var kept = new StringBuilder(text.Length);
            foreach (char ch in text)
                if (ch >= ' ' || ch == '\t' || ch == '\n')
                    kept.Append(ch);
            return kept.ToString();
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
