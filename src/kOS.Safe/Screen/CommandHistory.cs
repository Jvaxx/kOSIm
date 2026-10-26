using System;
using System.Collections.Generic;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// One interpreter's command history (Phase 3, P3-1): the Up/Down recall with the draft
    /// kept aside, and the source of interpreter lines for error messages. The commands
    /// themselves are in a <see cref="CommandHistoryList"/>, its own or one shared with the
    /// other terminals (<see cref="SharedCommandHistory"/>); the position, the draft and the
    /// newest unrecorded command are this interpreter's.
    /// <para>Bounded at <see cref="MAX_ENTRIES"/> commands; the oldest are dropped.</para>
    /// <para>Every command gets a range of interpreter line numbers, one per line of its
    /// text, counted on from the previous command's and never reused. The compiler is
    /// given the first one, so an error on a command's second line names the command's
    /// own second line (upstream numbered by command, which named the next command).
    /// A dropped command's lines read as <see cref="TRIMMED_LINE"/>.</para>
    /// <para>A command typed with a leading space runs but is not recorded (decision H4):
    /// only the newest such command keeps its lines, for its own error messages.</para>
    /// <para>The position is kept by the recalled command's id, so commands another terminal
    /// adds meanwhile do not move it.</para>
    /// Main thread only (the interpreter's).
    /// </summary>
    public sealed class CommandHistory
    {
        /// <summary>The most commands kept (user decision 2026-10-19).</summary>
        public const int MAX_ENTRIES = 500;

        public const string TRIMMED_LINE = "(command no longer in the history)";

        private const long DRAFT = 0; // the recalled id when the draft is shown; ids start at 1

        private readonly CommandHistoryList list;
        private string unrecordedText; // the newest command typed with a leading space; null if none
        private int unrecordedFirstLine;
        private long recalledId = DRAFT;
        private string draftText = string.Empty;
        private int draftCursor;

        public CommandHistory() : this(MAX_ENTRIES)
        {
        }

        public CommandHistory(int maxEntries) : this(new CommandHistoryList(maxEntries))
        {
        }

        /// <param name="list">the commands, possibly shared with other histories</param>
        public CommandHistory(CommandHistoryList list)
        {
            if (list == null) throw new ArgumentNullException("list");
            this.list = list;
        }

        /// <summary>The recorded commands, possibly shared with other terminals.</summary>
        public CommandHistoryList List { get { return list; } }

        public int Count { get { return list.Count; } }

        /// <summary>Changes whenever the recorded commands change (to know when to save them).</summary>
        public int Version { get { return list.Version; } }

        /// <summary>The recorded commands, oldest first.</summary>
        public IEnumerable<string> Commands { get { return list.Commands; } }

        /// <summary>Whether a command is shown rather than the draft.</summary>
        public bool IsRecalling { get { return recalledId != DRAFT; } }

        /// <summary>
        /// Record an entered command and go back to an empty draft. An empty command, a
        /// repeat of the newest one and a command typed with a leading space add no entry.
        /// </summary>
        /// <returns>the interpreter line number of the command's first line, for the compiler</returns>
        public int Add(string commandText)
        {
            if (commandText == null) commandText = string.Empty;
            ResetNavigation();
            if (commandText.Length > 0 && commandText[0] == ' ')
            {
                unrecordedText = commandText;
                unrecordedFirstLine = list.ReserveLines(commandText);
                return unrecordedFirstLine;
            }
            return list.Add(commandText);
        }

        /// <summary>
        /// Put saved commands (oldest first) before the ones recorded so far
        /// (<see cref="CommandHistoryList.Load"/>); the draft and the position are reset.
        /// </summary>
        public void Load(IEnumerable<string> savedCommands)
        {
            list.Load(savedCommands);
            ResetNavigation();
        }

        /// <summary>Back to an empty draft at the newest end (a reboot keeps the commands).</summary>
        public void ResetNavigation()
        {
            recalledId = DRAFT;
            draftText = string.Empty;
            draftCursor = 0;
        }

        /// <summary>
        /// Move through the history: -1 is older (Up), +1 newer (Down). Leaving the draft
        /// keeps it; moving past the newest command brings it back with its cursor.
        /// Changes made to a recalled command are not kept. When the recalled command is
        /// gone meanwhile (dropped from a full shared history), the move goes from where it was.
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
            if (delta == 0) return false;
            int count = list.Count;
            int target;
            if (!IsRecalling)
            {
                if (delta > 0 || count == 0) return false;
                target = count - 1;
            }
            else
            {
                int index = list.IndexAtOrAfterId(recalledId); // the recalled command, or the next newer one
                if (delta < 0)
                    target = index - 1;
                else
                    target = index < count && list.EntryId(index) == recalledId ? index + 1 : index;
                if (target < 0) return false;
            }

            if (!IsRecalling)
                KeepDraft(currentText, currentCursor);
            if (target < count)
            {
                recalledId = list.EntryId(target);
                text = list.EntryText(target);
                cursor = text.Length;
            }
            else
            {
                recalledId = DRAFT;
                text = draftText;
                cursor = draftCursor;
            }
            return true;
        }

        private void KeepDraft(string currentText, int currentCursor)
        {
            draftText = currentText ?? string.Empty;
            draftCursor = Math.Max(0, Math.Min(currentCursor, draftText.Length));
        }

        /// <summary>The text of a command, by index: 0 is the oldest, <see cref="Count"/> - 1 the newest.</summary>
        public string EntryText(int index)
        {
            return list.EntryText(index);
        }

        /// <summary>The id of a command, by index (<see cref="CommandHistoryList.EntryId"/>).</summary>
        public long EntryId(int index)
        {
            return list.EntryId(index);
        }

        /// <summary>The index of the command with this id; -1 once it is gone.</summary>
        public int IndexOfId(long id)
        {
            return list.IndexOfId(id);
        }

        /// <summary>
        /// The history search (Ctrl+R): the newest command at or before <paramref name="startIndex"/>
        /// that contains <paramref name="query"/>, ignoring case (kerboscript does), skipping
        /// commands equal to <paramref name="skipText"/> (the match already shown).
        /// </summary>
        /// <returns>false when no older command matches</returns>
        public bool TryFindOlder(string query, int startIndex, string skipText, out int index)
        {
            return list.TryFindOlder(query, startIndex, skipText, out index);
        }

        /// <summary>
        /// Show the command at <paramref name="index"/> as if reached with Up/Down (a search
        /// match taken for editing): the draft is kept aside first when it is shown, and
        /// Up/Down move on from this command.
        /// </summary>
        public void Recall(int index, string currentText, int currentCursor)
        {
            if (index < 0 || index >= list.Count) throw new ArgumentOutOfRangeException("index");
            if (!IsRecalling)
                KeepDraft(currentText, currentCursor);
            recalledId = list.EntryId(index);
        }

        /// <summary>The text of one interpreter line, by the numbers <see cref="Add"/> handed out.</summary>
        public string GetLine(int lineNumber)
        {
            if (unrecordedText != null && lineNumber >= unrecordedFirstLine &&
                lineNumber < unrecordedFirstLine + CommandHistoryList.CountLines(unrecordedText))
                return unrecordedText.Split('\n')[lineNumber - unrecordedFirstLine];
            return list.GetLine(lineNumber) ?? TRIMMED_LINE;
        }

        /// <summary>Forget every command (of every history sharing the list) and the draft. Line numbers restart at 1.</summary>
        public void Clear()
        {
            list.Clear();
            unrecordedText = null;
            ResetNavigation();
        }
    }
}
