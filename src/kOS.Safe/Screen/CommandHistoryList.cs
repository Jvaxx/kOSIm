using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// The recorded commands behind one or more <see cref="CommandHistory"/>: in the game every
    /// CPU of a save shares one (<see cref="SharedCommandHistory"/>, user decision 2026-10-26), so a
    /// command typed on one terminal is recalled on all of them; each terminal keeps its own
    /// Up/Down position, draft and search.
    /// <para>Bounded at its maximum; the oldest are dropped. Every command gets a range of
    /// interpreter line numbers, one per line of its text, from one counter: never reused, so
    /// the interpreters sharing the list never hand the compiler the same number. Every entry
    /// also gets an id, never reused even by <see cref="Load"/> or <see cref="Clear"/>, by which a
    /// terminal keeps its place while others add commands.</para>
    /// <para>With a <see cref="Store"/>, the commands are read from it on first use and saved
    /// to it when a command changes them (decision H3), at most once every
    /// <see cref="SAVE_INTERVAL"/> (review P-10). A change made sooner waits for
    /// <see cref="SavePending"/>, which the game's processors call every frame and when they go.</para>
    /// Main thread only (the interpreters').
    /// </summary>
    public sealed class CommandHistoryList
    {
        /// <summary>
        /// The shortest time between two saves (review P-10): a command typed by hand is saved
        /// at once, a paste or a script feeding the prompt about every 2 s.
        /// </summary>
        public static readonly TimeSpan SAVE_INTERVAL = TimeSpan.FromSeconds(2);

        private static readonly Stopwatch monotonicClock = Stopwatch.StartNew();

        private struct Entry
        {
            public string Text;
            public int FirstLine;
            public long Id;
        }

        private readonly int maxEntries;
        private readonly List<Entry> entries = new List<Entry>();
        private int nextLine = 1;
        private long nextId = 1;
        private ICommandHistoryStore store;
        private bool loaded = true; // nothing to load without a store
        private bool unsaved; // a change waits for the next save
        private TimeSpan nextSave = TimeSpan.MinValue;

        public CommandHistoryList() : this(CommandHistory.MAX_ENTRIES)
        {
        }

        public CommandHistoryList(int maxEntries)
        {
            if (maxEntries < 1) throw new ArgumentOutOfRangeException("maxEntries");
            this.maxEntries = maxEntries;
        }

        /// <summary>The time the saves are spaced by, for the tests; a monotonic clock by default.</summary>
        internal Func<TimeSpan> Clock { get; set; } = () => monotonicClock.Elapsed;

        /// <summary>
        /// Where the commands are kept between game sessions; null (the default) keeps them in
        /// memory only. Read on first use, so the store can still find its place when set. A
        /// change still waiting is saved to the old store first.
        /// </summary>
        public ICommandHistoryStore Store
        {
            get { return store; }
            set
            {
                SavePending(true); // to the store it belongs to
                store = value;
                loaded = value == null;
            }
        }

        public int Count
        {
            get
            {
                EnsureLoaded();
                return entries.Count;
            }
        }

        /// <summary>Changes whenever the recorded commands change (to know when to save them).</summary>
        public int Version { get; private set; }

        /// <summary>The recorded commands, oldest first.</summary>
        public IEnumerable<string> Commands
        {
            get
            {
                EnsureLoaded();
                foreach (Entry entry in entries)
                    yield return entry.Text;
            }
        }

        private void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            Load(store.Load());
        }

        /// <summary>
        /// Record an entered command (not one with a leading space: <see cref="CommandHistory"/>
        /// keeps those) and save when that changed the commands. An empty command and a repeat
        /// of the newest one add no entry.
        /// </summary>
        /// <returns>the interpreter line number of the command's first line, for the compiler</returns>
        internal int Add(string commandText)
        {
            EnsureLoaded();
            if (commandText.Length == 0)
                return nextLine; // compiles to nothing; a line no command owns
            if (entries.Count > 0 && entries[entries.Count - 1].Text == commandText)
                return entries[entries.Count - 1].FirstLine;

            int firstLine = ReserveLines(commandText);
            entries.Add(new Entry { Text = commandText, FirstLine = firstLine, Id = nextId++ });
            if (entries.Count > maxEntries)
                entries.RemoveRange(0, entries.Count - maxEntries);
            Changed();
            return firstLine;
        }

        /// <summary>Line numbers for a command that is not recorded (a leading space): the first of them.</summary>
        internal int ReserveLines(string commandText)
        {
            int firstLine = nextLine;
            nextLine += CountLines(commandText);
            return firstLine;
        }

        /// <summary>
        /// Put saved commands (oldest first) before the ones recorded so far, within the
        /// bound (the oldest are dropped). Empty commands and ones with a leading space are
        /// skipped. They all get new line numbers and ids.
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
                entries.Add(new Entry { Text = text, FirstLine = ReserveLines(text), Id = nextId++ });
            ++Version;
        }

        /// <summary>
        /// Save the commands if one changed them since the last save (review P-10): with
        /// <paramref name="now"/> at once (a CPU going away), else once <see cref="SAVE_INTERVAL"/>
        /// has passed since the last save. Cheap when nothing waits: the game's processors call
        /// it every frame.
        /// </summary>
        public void SavePending(bool now)
        {
            if (!unsaved) return;
            TimeSpan time = Clock();
            if (!now && time < nextSave) return;
            unsaved = false;
            nextSave = time + SAVE_INTERVAL;
            store.Save(Commands); // never throws for I/O; a failure is logged once
        }

        /// <summary>The text of a command, by index: 0 is the oldest, <see cref="Count"/> - 1 the newest.</summary>
        public string EntryText(int index)
        {
            EnsureLoaded();
            return entries[index].Text;
        }

        /// <summary>The id of a command, by index; ids grow from the oldest to the newest.</summary>
        public long EntryId(int index)
        {
            EnsureLoaded();
            return entries[index].Id;
        }

        /// <summary>The index of the first command whose id is at least <paramref name="id"/>; <see cref="Count"/> if none.</summary>
        public int IndexAtOrAfterId(long id)
        {
            EnsureLoaded();
            int low = 0, high = entries.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (entries[middle].Id < id)
                    low = middle + 1;
                else
                    high = middle;
            }
            return low;
        }

        /// <summary>The index of the command with this id; -1 once it is gone (dropped, or the list reloaded).</summary>
        public int IndexOfId(long id)
        {
            int index = IndexAtOrAfterId(id);
            return index < entries.Count && entries[index].Id == id ? index : -1;
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
            EnsureLoaded();
            for (index = Math.Min(startIndex, entries.Count - 1); index >= 0; --index)
            {
                string text = entries[index].Text;
                if (text != skipText && text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>The text of one interpreter line of a recorded command; null when no recorded command has it.</summary>
        internal string GetLine(int lineNumber)
        {
            EnsureLoaded();
            for (int i = entries.Count - 1; i >= 0; --i)
            {
                Entry entry = entries[i];
                if (lineNumber >= entry.FirstLine)
                {
                    string[] lines = entry.Text.Split('\n');
                    int offset = lineNumber - entry.FirstLine;
                    return offset < lines.Length ? lines[offset] : null;
                }
            }
            return null;
        }

        /// <summary>Forget every command (saved as for <see cref="Add"/>). Line numbers restart at 1 (ids do not).</summary>
        public void Clear()
        {
            EnsureLoaded();
            entries.Clear();
            nextLine = 1;
            Changed();
        }

        /// <summary>The commands changed: save them, at once unless the last save was less than <see cref="SAVE_INTERVAL"/> ago.</summary>
        private void Changed()
        {
            ++Version;
            if (store == null) return;
            unsaved = true;
            SavePending(false);
        }

        internal static int CountLines(string text)
        {
            int lines = 1;
            foreach (char ch in text)
                if (ch == '\n') ++lines;
            return lines;
        }
    }
}
