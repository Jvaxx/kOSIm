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
    /// Main thread only (the interpreter's).
    /// </summary>
    public abstract class CommandLineEditor : TextEditor
    {
        /// <summary>Ctrl+R, from the GUI and telnet alike.</summary>
        public const char SEARCH_KEY = (char)0x12;

        /// <summary>Escape: leaves the search.</summary>
        public const char CANCEL_KEY = (char)0x1b;

        private const string SEARCH_PROMPT = "(search) '";
        private const string FAILED_SEARCH_PROMPT = "(search failed) '";
        private const string MATCH_SEPARATOR = "': ";

        private readonly CommandHistory history = new CommandHistory();
        private bool searching;
        private readonly StringBuilder query = new StringBuilder();
        private string lastQuery = string.Empty;
        private int matchIndex = -1; // into the history; -1 = no match yet
        private bool searchFailed;

        protected CommandHistory History { get { return history; } }

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
            if (history.TryMove(deltaIndex, LineBuilder.ToString(), LineCursorIndex, out text, out cursor))
                ReplaceLine(text, cursor);
        }

        private void StartSearch()
        {
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
            base.Reset();
        }
    }
}
