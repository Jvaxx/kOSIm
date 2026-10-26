using System;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// The command history the CPUs of one save share (user decision 2026-10-26, replacing the
    /// per-CPU files): every CPU of the save asks for its history file and gets the same
    /// <see cref="CommandHistoryList"/>, read once. When the last of them lets go, the list is
    /// saved and forgotten, so the next flight scene reads the file again (deleting it between
    /// flights clears the history, as with the old per-CPU files).
    /// <para>Another save's file replaces the list; CPUs still holding the old one keep it,
    /// saved to its own file.</para>
    /// Main thread only.
    /// </summary>
    public sealed class SharedCommandHistory
    {
        /// <summary>The game's: <c>saves/&lt;save&gt;/kOS/history.txt</c> (<c>kOS.Module.kOSProcessor</c>).</summary>
        public static readonly SharedCommandHistory Saves = new SharedCommandHistory(path => new CommandHistoryFile(() => path));

        private readonly Func<string, ICommandHistoryStore> storeFor;
        private string path;
        private CommandHistoryList list;
        private int users;

        /// <param name="storeFor">the store of a history file, by its path</param>
        public SharedCommandHistory(Func<string, ICommandHistoryStore> storeFor)
        {
            if (storeFor == null) throw new ArgumentNullException("storeFor");
            this.storeFor = storeFor;
        }

        /// <summary>The CPUs holding the current list, for the tests.</summary>
        internal int Users { get { return users; } }

        /// <summary>
        /// The list of the history file at <paramref name="filePath"/>, shared with every CPU
        /// that asked for the same file; give it back with <see cref="Release"/>.
        /// </summary>
        public CommandHistoryList Acquire(string filePath)
        {
            if (filePath == null) throw new ArgumentNullException("filePath");
            if (list != null && !string.Equals(path, filePath, StringComparison.Ordinal))
            {
                list.SavePending(true);
                list = null;
            }
            if (list == null)
            {
                path = filePath;
                list = new CommandHistoryList { Store = storeFor(filePath) };
                users = 0;
            }
            ++users;
            return list;
        }

        /// <summary>
        /// A CPU going: save what waits at once; forget the list when no CPU holds it any more.
        /// Ignores null (a CPU that never acquired one).
        /// </summary>
        public void Release(CommandHistoryList released)
        {
            if (released == null) return;
            released.SavePending(true);
            if (released != list) return; // replaced by another save's list
            if (--users > 0) return;
            list = null;
            path = null;
            users = 0;
        }
    }
}
