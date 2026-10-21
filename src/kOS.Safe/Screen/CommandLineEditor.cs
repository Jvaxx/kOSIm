using System;
using System.Diagnostics;
using System.Text;
using kOS.Safe.UserIO;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// The prompt's line editing with the command history, shared by the game's
    /// <c>kOS.Screen.Interpreter</c> and the test fixture (Phase 3): Up/Down recall
    /// (P3-1) and the history search (Ctrl+R, decisions H1/H2), for the GUI and telnet alike.
    /// <para>The search shows on the input line, readline-style:
    /// <c>(search) 'query': matching command</c>. Typing extends the query, Backspace
    /// shortens it, Ctrl+R again finds the next older match (with an empty query, the
    /// previous search's). Enter runs the match shown; with none, it only leaves the search.
    /// Esc or BREAK (Ctrl+C) leave it with the line as it was, and break nothing. The cursor
    /// keys, Delete and Tab take the match for editing (then act as usual; Tab types
    /// nothing); PageUp/PageDown scroll as usual. The line being edited is not touched
    /// until a match is taken.</para>
    /// <para>With a <see cref="HistoryStore"/>, the history is read from it on first use
    /// and saved to it when a command changes it (decision H3), at most once every
    /// <see cref="HISTORY_SAVE_INTERVAL"/> (review P-10); a reboot keeps it. A change made
    /// sooner waits for <see cref="SavePendingHistory"/>, which the game's processor calls
    /// every frame and when it goes.</para>
    /// Main thread only (the interpreter's).
    /// </summary>
    public abstract class CommandLineEditor : TextEditor
    {
        /// <summary>Ctrl+R, from the GUI and telnet alike.</summary>
        public const char SEARCH_KEY = (char)0x12;

        /// <summary>Escape: leaves the search.</summary>
        public const char CANCEL_KEY = (char)0x1b;

        /// <summary>
        /// The shortest time between two saves of the history (review P-10): a command typed by
        /// hand is saved at once, a paste or a script feeding the prompt about every 2 s.
        /// </summary>
        public static readonly TimeSpan HISTORY_SAVE_INTERVAL = TimeSpan.FromSeconds(2);

        private static readonly Stopwatch monotonicClock = Stopwatch.StartNew();

        private const string SEARCH_PROMPT = "(search) '";
        private const string FAILED_SEARCH_PROMPT = "(search failed) '";
        private const string MATCH_SEPARATOR = "': ";

        private readonly CommandHistory history = new CommandHistory();
        private bool searching;
        private readonly StringBuilder query = new StringBuilder();
        private string lastQuery = string.Empty;
        private int matchIndex = -1; // into the history; -1 = no match yet
        private bool searchFailed;
        private ICommandHistoryStore historyStore;
        private bool historyLoaded = true; // nothing to load without a store
        private bool historyUnsaved; // a change waits for the next save
        private TimeSpan nextHistorySave = TimeSpan.MinValue;

        /// <summary>The time the saves are spaced by, for the tests; a monotonic clock by default.</summary>
        internal Func<TimeSpan> HistoryClock { get; set; } = () => monotonicClock.Elapsed;

        protected CommandHistory History
        {
            get
            {
                EnsureHistoryLoaded();
                return history;
            }
        }

        private void EnsureHistoryLoaded()
        {
            if (historyLoaded) return;
            historyLoaded = true;
            history.Load(historyStore.Load());
        }

        /// <summary>
        /// Where the history is kept between game sessions; null (the default) keeps it in
        /// memory only. Read on the history's first use, so the store can still find its
        /// place (e.g. a part ID) when set.
        /// </summary>
        public ICommandHistoryStore HistoryStore
        {
            get { return historyStore; }
            set
            {
                SavePendingHistory(true); // to the store it belongs to
                historyStore = value;
                historyLoaded = value == null;
            }
        }

        /// <summary>
        /// Record an entered command (<see cref="CommandHistory.Add"/>) and save the history
        /// when that changed it: at once unless the last save was less than
        /// <see cref="HISTORY_SAVE_INTERVAL"/> ago (then <see cref="SavePendingHistory"/> does).
        /// </summary>
        /// <returns>the interpreter line number of the command's first line, for the compiler</returns>
        protected int RecordCommand(string commandText)
        {
            CommandHistory commands = History;
            int version = commands.Version;
            int firstLine = commands.Add(commandText);
            if (historyStore != null && commands.Version != version)
            {
                historyUnsaved = true;
                SavePendingHistory(false);
            }
            return firstLine;
        }

        /// <summary>
        /// Save the history if a command changed it since the last save (review P-10): with
        /// <paramref name="now"/> at once (the CPU going away), else once
        /// <see cref="HISTORY_SAVE_INTERVAL"/> has passed since the last save. Cheap when nothing
        /// waits: the game's processor calls it every frame.
        /// </summary>
        public void SavePendingHistory(bool now)
        {
            if (!historyUnsaved) return;
            TimeSpan time = HistoryClock();
            if (!now && time < nextHistorySave) return;
            historyUnsaved = false;
            nextHistorySave = time + HISTORY_SAVE_INTERVAL;
            historyStore.Save(history.Commands); // never throws for I/O; a failure is logged once
        }

        /// <summary>Whether the history search is shown on the input line.</summary>
        public bool IsSearchingHistory { get { return searching; } }

        protected override string ShownLine
        {
            get
            {
                if (!searching) return base.ShownLine;
                return (searchFailed ? FAILED_SEARCH_PROMPT : SEARCH_PROMPT) + query + MATCH_SEPARATOR +
                    (matchIndex >= 0 ? history.EntryText(matchIndex) : string.Empty);
            }
        }

        protected override int ShownCursorIndex
        {
            get
            {
                if (!searching) return base.ShownCursorIndex;
                return (searchFailed ? FAILED_SEARCH_PROMPT : SEARCH_PROMPT).Length + query.Length;
            }
        }

        public override void Type(char ch)
        {
            if (searching)
                TypeWhileSearching(ch);
            else
                base.Type(ch);
        }

        public override bool SpecialKey(char key)
        {
            if (searching)
                return SpecialKeyWhileSearching(key);
            return EditKey(key);
        }

        private bool EditKey(char key)
        {
            switch (key)
            {
                case (char)UnicodeCommand.UPCURSORONE:
                    ShowCommandHistoryEntry(-1);
                    return true;
                case (char)UnicodeCommand.DOWNCURSORONE:
                    ShowCommandHistoryEntry(1);
                    return true;
                case SEARCH_KEY:
                    StartSearch();
                    return true;
                default:
                    return base.SpecialKey(key);
            }
        }

        private void ShowCommandHistoryEntry(int deltaIndex)
        {
            string text;
            int cursor;
            if (History.TryMove(deltaIndex, LineBuilder.ToString(), LineCursorIndex, out text, out cursor))
                ReplaceLine(text, cursor);
        }

        private void StartSearch()
        {
            EnsureHistoryLoaded(); // the search reads the history field from here on
            searching = true;
            query.Length = 0;
            matchIndex = -1;
            searchFailed = false;
            RedrawLine();
        }

        private void TypeWhileSearching(char ch)
        {
            switch ((int)ch)
            {
                case 8: // backspace
                    if (query.Length == 0) return;
                    query.Length -= 1;
                    matchIndex = -1;
                    searchFailed = false;
                    if (query.Length > 0)
                        FindOlder(history.Count - 1, null);
                    RedrawLine();
                    break;
                case 13: // enter: run the match shown
                    if (matchIndex >= 0)
                    {
                        TakeMatch();
                        NewLine();
                    }
                    else
                    {
                        EndSearch();
                    }
                    break;
                case '\t':
                    TakeMatch();
                    break;
                case 7:
                case (char)UnicodeCommand.BEEP:
                    base.Type(ch);
                    break;
                default:
                    if (ch < ' ') return;
                    query.Append(ch);
                    if (!searchFailed)
                        FindOlder(matchIndex >= 0 ? matchIndex : history.Count - 1, null);
                    RedrawLine();
                    break;
            }
        }

        private bool SpecialKeyWhileSearching(char key)
        {
            switch (key)
            {
                case SEARCH_KEY:
                    if (query.Length == 0)
                        query.Append(lastQuery);
                    if (query.Length > 0)
                    {
                        if (matchIndex < 0)
                            FindOlder(history.Count - 1, null);
                        else
                            FindOlder(matchIndex - 1, history.EntryText(matchIndex));
                    }
                    RedrawLine();
                    return true;
                case CANCEL_KEY:
                case (char)UnicodeCommand.BREAK:
                    EndSearch();
                    return true;
                case (char)UnicodeCommand.PAGEUPCURSOR:
                case (char)UnicodeCommand.PAGEDOWNCURSOR:
                    return base.SpecialKey(key);
                case (char)UnicodeCommand.LEFTCURSORONE:
                case (char)UnicodeCommand.RIGHTCURSORONE:
                case (char)UnicodeCommand.HOMECURSOR:
                case (char)UnicodeCommand.ENDCURSOR:
                case (char)0x0001: // Ctrl-A, Home
                case (char)0x0005: // Ctrl-E, End
                case (char)UnicodeCommand.UPCURSORONE:
                case (char)UnicodeCommand.DOWNCURSORONE:
                case (char)UnicodeCommand.DELETERIGHT:
                    TakeMatch();
                    EditKey(key);
                    return true;
                default:
                    return false; // other keys do nothing during a search
            }
        }

        /// <summary>Search from <paramref name="startIndex"/> down; on no match, keep the old one and say so.</summary>
        private void FindOlder(int startIndex, string skipText)
        {
            int found;
            if (history.TryFindOlder(query.ToString(), startIndex, skipText, out found))
            {
                matchIndex = found;
                searchFailed = false;
            }
            else
            {
                searchFailed = true;
            }
        }

        /// <summary>Leave the search with the match on the input line, cursor at its end (with none, the line as it was).</summary>
        private void TakeMatch()
        {
            int index = matchIndex;
            EndSearch();
            if (index < 0) return;
            history.Recall(index, LineBuilder.ToString(), LineCursorIndex);
            string text = history.EntryText(index);
            ReplaceLine(text, text.Length);
        }

        private void EndSearch()
        {
            if (query.Length > 0)
                lastQuery = query.ToString();
            searching = false;
            query.Length = 0;
            matchIndex = -1;
            searchFailed = false;
            RedrawLine();
        }

        public override void Reset()
        {
            searching = false;
            query.Length = 0;
            matchIndex = -1;
            searchFailed = false;
            lastQuery = string.Empty;
            if (historyLoaded)
                history.ResetNavigation(); // a reboot keeps the commands (user decision)
            base.Reset();
        }
    }
}
