using System;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// The GUI terminal's rows as the strings it draws, null cells shown as spaces (review
    /// P-08). The window draws every row on every IMGUI event (several a frame); it made two
    /// strings a row each time (<c>line.ToString().Replace('\0', ' ')</c>), the largest steady
    /// allocation of an open terminal. Here each row keeps its last string, and a row gets a
    /// new one only when what it shows changed:
    /// <list type="bullet">
    /// <item>the same <see cref="ScreenBufferLine"/> with the same
    /// <see cref="ScreenBufferLine.LastChangeTick"/> is unchanged: its characters change only
    /// through members that take a new tick (lines are changed on the main thread only, as
    /// the window draws them, so the ticks only grow);</item>
    /// <item>else (another line, a new tick, or another <see cref="IScreenBufferLine"/>,
    /// whose tick is not trusted) the characters are compared with the string kept, so a
    /// line replaced by one showing the same text, as a snapshot's padding rows are, keeps
    /// it too.</item>
    /// </list>
    /// No Unity state.
    /// </summary>
    public sealed class TerminalRowTexts
    {
        private struct Row
        {
            public IScreenBufferLine Line;
            public ulong Tick;
            public string Text;
        }

        private Row[] rows = new Row[0];
        private char[] scratch = new char[0];
        private int used; // rows past this one hold nothing

        /// <summary>How many strings were made so far, for the tests.</summary>
        internal long TextsMade { get; private set; }

        /// <summary>What <paramref name="line"/> shows as row <paramref name="row"/>: its characters, a null one as a space.</summary>
        public string Get(int row, IScreenBufferLine line)
        {
            if (row < 0) throw new ArgumentOutOfRangeException("row");
            if (line == null) throw new ArgumentNullException("line");
            if (row >= rows.Length)
                Array.Resize(ref rows, Math.Max(row + 1, rows.Length * 2));
            if (row >= used)
                used = row + 1;

            Row kept = rows[row];
            ulong tick = line.LastChangeTick;
            if (kept.Text != null)
            {
                if (ReferenceEquals(kept.Line, line) && kept.Tick == tick && line.GetType() == typeof(ScreenBufferLine))
                    return kept.Text;
                if (Shows(kept.Text, line))
                {
                    rows[row] = new Row { Line = line, Tick = tick, Text = kept.Text };
                    return kept.Text;
                }
            }
            string text = TextOf(line);
            rows[row] = new Row { Line = line, Tick = tick, Text = text };
            return text;
        }

        /// <summary>Forgets the rows from <paramref name="rowCount"/> on, so the lines and strings of rows no longer drawn can go.</summary>
        public void Keep(int rowCount)
        {
            if (rowCount < 0) rowCount = 0;
            for (int row = rowCount; row < used; ++row)
                rows[row] = new Row();
            if (rowCount < used)
                used = rowCount;
        }

        /// <summary>Forgets every row (the window closed).</summary>
        public void Clear()
        {
            Keep(0);
        }

        private static bool Shows(string text, IScreenBufferLine line)
        {
            int length = line.Length;
            if (text.Length != length) return false;
            for (int i = 0; i < length; ++i)
            {
                char c = line[i];
                if (c == '\0') c = ' ';
                if (text[i] != c) return false;
            }
            return true;
        }

        private string TextOf(IScreenBufferLine line)
        {
            int length = line.Length;
            if (scratch.Length < length)
                scratch = new char[Math.Max(length, scratch.Length * 2)];
            for (int i = 0; i < length; ++i)
            {
                char c = line[i];
                scratch[i] = c == '\0' ? ' ' : c; // null cells must advance like spaces, matching PRINT AT
            }
            ++TextsMade;
            return new string(scratch, 0, length);
        }
    }
}
