using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using kOS.Safe.Exceptions;
using kOS.Safe.Persistence;
using kOS.Safe.Utilities;

namespace kOS.Safe.Screen
{
    /// <summary>Where a <see cref="CommandLineEditor"/> keeps its history between game sessions.</summary>
    public interface ICommandHistoryStore
    {
        /// <summary>The saved commands, oldest first; empty when there are none or they cannot be read.</summary>
        IList<string> Load();

        /// <summary>Replace the saved commands. Must not throw for I/O failures.</summary>
        void Save(IEnumerable<string> commands);
    }

    /// <summary>
    /// A CPU's command history as a text file (Phase 3, decision H3): UTF-8, a header line,
    /// then one command per line with <c>\</c>, line feed and carriage return escaped as
    /// <c>\\</c>, <c>\n</c> and <c>\r</c>. Saves go through a complete temporary file
    /// (<see cref="ArchiveFileWriter"/>), so a failed save leaves the old file. Read and
    /// write failures are logged once and never reach the prompt. Clearing a history means
    /// deleting its file (user decision). Main thread only.
    /// </summary>
    public sealed class CommandHistoryFile : ICommandHistoryStore
    {
        public const string HEADER = "# kOS command history: one command per line, \\n \\r \\\\ escaped";

        /// <summary>A larger file is not read (500 pasted commands of 64 KiB would be 32 MiB).</summary>
        public const long MAX_FILE_BYTES = 16L * 1024 * 1024;

        private readonly Func<string> pathProvider;
        private readonly ArchiveFileWriter writer;
        private string path;
        private bool pathResolved;
        private bool reportedFailure;

        /// <param name="pathProvider">the file's path, asked once on first use; null for no file (nothing is kept)</param>
        public CommandHistoryFile(Func<string> pathProvider) : this(pathProvider, new ArchiveFileOperations())
        {
        }

        internal CommandHistoryFile(Func<string> pathProvider, ArchiveFileOperations files)
        {
            if (pathProvider == null) throw new ArgumentNullException("pathProvider");
            this.pathProvider = pathProvider;
            writer = new ArchiveFileWriter(files);
        }

        /// <summary>The file's path once resolved; null before, or when there is none.</summary>
        public string Path { get { return path; } }

        private string ResolvePath()
        {
            if (!pathResolved)
            {
                pathResolved = true;
                try
                {
                    path = pathProvider();
                }
                catch (Exception e)
                {
                    Report("Command history: no file for this CPU", e);
                }
            }
            return path;
        }

        public IList<string> Load()
        {
            string file = ResolvePath();
            if (file == null) return new List<string>();
            try
            {
                var info = new FileInfo(file);
                if (!info.Exists) return new List<string>();
                if (info.Length > MAX_FILE_BYTES)
                {
                    Report("Command history: " + file + " is over " + MAX_FILE_BYTES + " bytes; not read", null);
                    return new List<string>();
                }
                return Parse(File.ReadAllText(file, Encoding.UTF8));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException)
            {
                Report("Command history: cannot read " + file, e);
                return new List<string>();
            }
        }

        public void Save(IEnumerable<string> commands)
        {
            string file = ResolvePath();
            if (file == null) return;
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));
                writer.Write(file, new UTF8Encoding(false).GetBytes(Format(commands)));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException ||
                e is System.Security.SecurityException || e is KOSPersistenceException)
            {
                Report("Command history: cannot save " + file, e);
            }
        }

        private void Report(string message, Exception e)
        {
            if (reportedFailure) return; // once per CPU: a failing disk would otherwise log every command
            reportedFailure = true;
            if (SafeHouse.Logger == null) return;
            SafeHouse.Logger.LogWarning(e == null ? message : message + ": " + e.Message);
        }

        public static string Format(IEnumerable<string> commands)
        {
            if (commands == null) throw new ArgumentNullException("commands");
            var text = new StringBuilder(HEADER).Append('\n');
            foreach (string command in commands)
            {
                foreach (char ch in command)
                {
                    switch (ch)
                    {
                        case '\\': text.Append("\\\\"); break;
                        case '\n': text.Append("\\n"); break;
                        case '\r': text.Append("\\r"); break;
                        default: text.Append(ch); break;
                    }
                }
                text.Append('\n');
            }
            return text.ToString();
        }

        /// <summary>
        /// The commands in a saved file. Tolerant: CR LF line ends, a missing header, an
        /// unknown escape (kept as typed) and empty lines (skipped) are accepted.
        /// </summary>
        public static List<string> Parse(string text)
        {
            if (text == null) throw new ArgumentNullException("text");
            var commands = new List<string>();
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; ++i)
            {
                string line = lines[i];
                if (line.EndsWith("\r")) line = line.Substring(0, line.Length - 1);
                if (i == 0 && line.StartsWith("# kOS command history")) continue;
                if (line.Length == 0) continue;
                commands.Add(Unescape(line));
            }
            return commands;
        }

        private static string Unescape(string line)
        {
            if (line.IndexOf('\\') < 0) return line;
            var text = new StringBuilder(line.Length);
            for (int i = 0; i < line.Length; ++i)
            {
                char ch = line[i];
                if (ch == '\\' && i + 1 < line.Length)
                {
                    char next = line[i + 1];
                    if (next == '\\' || next == 'n' || next == 'r')
                    {
                        text.Append(next == 'n' ? '\n' : next == 'r' ? '\r' : '\\');
                        ++i;
                        continue;
                    }
                }
                text.Append(ch);
            }
            return text.ToString();
        }
    }
}
