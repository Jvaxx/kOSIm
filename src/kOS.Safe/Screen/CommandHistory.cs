using System;
using System.Collections.Generic;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// The interpreter's command history (Phase 3, P3-1): the Up/Down recall with the
    /// draft kept aside, and the source of interpreter lines for error messages.
    /// <para>Bounded at <see cref="MAX_ENTRIES"/> commands; the oldest are dropped.</para>
    /// <para>Every command gets a range of interpreter line numbers, one per line of its
    /// text, counted on from the previous command's and never reused. The compiler is
    /// given the first one, so an error on a command's second line names the command's
    /// own second line (upstream numbered by command, which named the next command).
    /// A dropped command's lines read as <see cref="TRIMMED_LINE"/>.</para>
    /// Main thread only (the interpreter's).
    /// </summary>
    public sealed class CommandHistory
    {
        /// <summary>The most commands kept (user decision 2026-10-19).</summary>
        public const int MAX_ENTRIES = 500;

        public const string TRIMMED_LINE = "(command no longer in the history)";

        private struct Entry
        {
            public string Text;
            public int FirstLine;
        }

        private readonly int maxEntries;
        private readonly List<Entry> entries = new List<Entry>();
        private int nextLine = 1;
        private int position; // index into entries of the recalled command; entries.Count = the draft
        private string draftText = string.Empty;
        private int draftCursor;

        public CommandHistory() : this(MAX_ENTRIES)
        {
        }

        public CommandHistory(int maxEntries)
        {
            if (maxEntries < 1) throw new ArgumentOutOfRangeException("maxEntries");
            this.maxEntries = maxEntries;
        }

        public int Count { get { return entries.Count; } }

        /// <summary>Whether a command is shown rather than the draft.</summary>
        public bool IsRecalling { get { return position < entries.Count; } }

        /// <summary>
        /// Record an entered command and go back to an empty draft. An empty command and a
        /// repeat of the newest one add no entry.
        /// </summary>
        /// <returns>the interpreter line number of the command's first line, for the compiler</returns>
        public int Add(string commandText)
        {
            if (commandText == null) commandText = string.Empty;
            draftText = string.Empty;
            draftCursor = 0;
            int firstLine;
            if (commandText.Length == 0)
            {
                firstLine = nextLine; // compiles to nothing; a line no command owns
            }
            else if (entries.Count > 0 && entries[entries.Count - 1].Text == commandText)
            {
                firstLine = entries[entries.Count - 1].FirstLine;
            }
            else
            {
                firstLine = nextLine;
                entries.Add(new Entry { Text = commandText, FirstLine = firstLine });
                nextLine += CountLines(commandText);
                if (entries.Count > maxEntries)
                    entries.RemoveRange(0, entries.Count - maxEntries);
            }
            position = entries.Count;
            return firstLine;
        }

        /// <summary>
        /// Move through the history: -1 is older (Up), +1 newer (Down). Leaving the draft
        /// keeps it; moving past the newest command brings it back with its cursor.
        /// Changes made to a recalled command are not kept.
        /// </summary>
        /// <param name="delta">-1 or +1</param>
        /// <param name="currentText">the line being edited</param>
        /// <param name="currentCursor">its cursor index</param>
        /// <param name="text">the text to show</param>
        /// <param name="cursor">the cursor index to show</param>
        /// <returns>false when there is nothing that way (nothing changes)</returns>
        public bool TryMove(int delta, string currentText, int currentCursor, out string text, out int cursor)
        {
            text = null;
            cursor = 0;
            int newPosition = position + Math.Sign(delta);
            if (delta == 0 || newPosition < 0 || newPosition > entries.Count)
                return false;
            if (!IsRecalling)
            {
                draftText = currentText ?? string.Empty;
                draftCursor = Math.Max(0, Math.Min(currentCursor, draftText.Length));
            }
            position = newPosition;
            if (IsRecalling)
            {
                text = entries[position].Text;
                cursor = text.Length;
            }
            else
            {
                text = draftText;
                cursor = draftCursor;
            }
            return true;
        }

        /// <summary>The text of a command, by index: 0 is the oldest, <see cref="Count"/> - 1 the newest.</summary>
        public string EntryText(int index)
        {
            return entries[index].Text;
        }

        /// <summary>
        /// The history search (Ctrl+R): the newest command at or before <paramref name="startIndex"/>
        /// that contains <paramref name="query"/>, ignoring case (kerboscript does), skipping
        /// commands equal to <paramref name="skipText"/> (the match already shown).
        /// </summary>
        /// <returns>false when no older command matches</returns>
        public bool TryFindOlder(string query, int startIndex, string skipText, out int index)
        {
            if (query == null) throw new ArgumentNullException("query");
            for (index = Math.Min(startIndex, entries.Count - 1); index >= 0; --index)
            {
                string text = entries[index].Text;
                if (text != skipText && text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Show the command at <paramref name="index"/> as if reached with Up/Down (a search
        /// match taken for editing): the draft is kept aside first when it is shown, and
        /// Up/Down move on from this command.
        /// </summary>
        public void Recall(int index, string currentText, int currentCursor)
        {
            if (index < 0 || index >= entries.Count) throw new ArgumentOutOfRangeException("index");
            if (!IsRecalling)
            {
                draftText = currentText ?? string.Empty;
                draftCursor = Math.Max(0, Math.Min(currentCursor, draftText.Length));
            }
            position = index;
        }

        /// <summary>The text of one interpreter line, by the numbers <see cref="Add"/> handed out.</summary>
        public string GetLine(int lineNumber)
        {
            for (int i = entries.Count - 1; i >= 0; --i)
            {
                Entry entry = entries[i];
                if (lineNumber >= entry.FirstLine)
                {
                    string[] lines = entry.Text.Split('\n');
                    int offset = lineNumber - entry.FirstLine;
                    return offset < lines.Length ? lines[offset] : TRIMMED_LINE;
                }
            }
            return TRIMMED_LINE;
        }

        /// <summary>Forget every command and the draft (a reboot). Line numbers restart at 1.</summary>
        public void Clear()
        {
            entries.Clear();
            nextLine = 1;
            position = 0;
            draftText = string.Empty;
            draftCursor = 0;
        }

        private static int CountLines(string text)
        {
            int lines = 1;
            foreach (char ch in text)
                if (ch == '\n') ++lines;
            return lines;
        }
    }
}
