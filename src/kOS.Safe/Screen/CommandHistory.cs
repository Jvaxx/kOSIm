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
    /// <para>A command typed with a leading space runs but is not recorded (decision H4):
    /// only the newest such command keeps its lines, for its own error messages.</para>
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
        private Entry unrecorded; // the newest command typed with a leading space; Text null if none
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

        /// <summary>Changes whenever the recorded commands change (to know when to save them).</summary>
        public int Version { get; private set; }

        /// <summary>The recorded commands, oldest first.</summary>
        public IEnumerable<string> Commands
        {
            get
            {
                foreach (Entry entry in entries)
                    yield return entry.Text;
            }
        }

        /// <summary>Whether a command is shown rather than the draft.</summary>
        public bool IsRecalling { get { return position < entries.Count; } }

        /// <summary>
        /// Record an entered command and go back to an empty draft. An empty command, a
        /// repeat of the newest one and a command typed with a leading space add no entry.
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
            else if (commandText[0] == ' ')
            {
                firstLine = nextLine;
                unrecorded = new Entry { Text = commandText, FirstLine = firstLine };
                nextLine += CountLines(commandText);
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
                ++Version;
            }
            position = entries.Count;
            return firstLine;
        }

        /// <summary>
        /// Put saved commands (oldest first) before the ones recorded so far, within the
        /// bound (the oldest are dropped). Empty commands and ones with a leading space are
        /// skipped. They get new line numbers; the draft and the position are reset.
        /// </summary>
        public void Load(IEnumerable<string> savedCommands)
        {
            if (savedCommands == null) throw new ArgumentNullException("savedCommands");
            var commands = new List<string>();
            foreach (string text in savedCommands)
            {
                if (string.IsNullOrEmpty(text) || text[0] == ' ') continue;
                if (commands.Count > 0 && commands[commands.Count - 1] == text) continue;
                commands.Add(text);
            }
            foreach (Entry entry in entries)
                commands.Add(entry.Text);
            if (commands.Count > maxEntries)
                commands.RemoveRange(0, commands.Count - maxEntries);
            entries.Clear();
            foreach (string text in commands)
            {
                entries.Add(new Entry { Text = text, FirstLine = nextLine });
                nextLine += CountLines(text);
            }
            ++Version;
            ResetNavigation();
        }

        /// <summary>Back to an empty draft at the newest end (a reboot keeps the commands).</summary>
        public void ResetNavigation()
        {
            position = entries.Count;
            draftText = string.Empty;
            draftCursor = 0;
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
            if (unrecorded.Text != null && lineNumber >= unrecorded.FirstLine &&
                lineNumber < unrecorded.FirstLine + CountLines(unrecorded.Text))
                return unrecorded.Text.Split('\n')[lineNumber - unrecorded.FirstLine];
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

        /// <summary>Forget every command and the draft. Line numbers restart at 1.</summary>
        public void Clear()
        {
            entries.Clear();
            unrecorded = new Entry();
            ++Version;
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
