using System;
using System.IO;
using System.Text;
using kOS.Safe.Exceptions;
using kOS.Safe.Persistence;
using kOS.Safe.Utilities;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// A small UTF-8 text file a CPU keeps under the save's folder (the command history, the
    /// terminal's geometry). The path is asked once, on first use; null means no file and
    /// nothing is kept. Saves go through a complete temporary file
    /// (<see cref="ArchiveFileWriter"/>), so a failed save leaves the old file. Read and write
    /// failures are logged once and never thrown. Main thread only.
    /// </summary>
    internal sealed class CpuTextFile
    {
        private readonly string label;
        private readonly Func<string> pathProvider;
        private readonly long maxBytes;
        private readonly ArchiveFileWriter writer;
        private string path;
        private bool pathResolved;
        private bool reportedFailure;

        /// <param name="label">what the file holds, for the log (<c>Command history</c>)</param>
        /// <param name="pathProvider">the file's path, asked once on first use; null for no file</param>
        /// <param name="maxBytes">a larger file is not read</param>
        /// <param name="files">the file operations saves use</param>
        internal CpuTextFile(string label, Func<string> pathProvider, long maxBytes, ArchiveFileOperations files)
        {
            if (pathProvider == null) throw new ArgumentNullException("pathProvider");
            this.label = label;
            this.pathProvider = pathProvider;
            this.maxBytes = maxBytes;
            writer = new ArchiveFileWriter(files);
        }

        /// <summary>The file's path once resolved; null before, or when there is none.</summary>
        internal string Path { get { return path; } }

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
                    Report(label + ": no file for this CPU", e);
                }
            }
            return path;
        }

        /// <summary>The file's text; null when there is no file or it cannot be read.</summary>
        internal string Read()
        {
            string file = ResolvePath();
            if (file == null) return null;
            try
            {
                var info = new FileInfo(file);
                if (!info.Exists) return null;
                if (info.Length > maxBytes)
                {
                    Report(label + ": " + file + " is over " + maxBytes + " bytes; not read", null);
                    return null;
                }
                return File.ReadAllText(file, Encoding.UTF8);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException)
            {
                Report(label + ": cannot read " + file, e);
                return null;
            }
        }

        /// <summary>
        /// Replace the file's text (UTF-8, no byte order mark), creating its folders. True once
        /// the text is saved; false when there is no file or the save failed (logged once; the
        /// old file is kept), so a caller can say so (review N-01).
        /// </summary>
        internal bool Write(string text)
        {
            string file = ResolvePath();
            if (file == null) return false;
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));
                writer.Write(file, new UTF8Encoding(false).GetBytes(text));
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException ||
                e is System.Security.SecurityException || e is KOSPersistenceException)
            {
                Report(label + ": cannot save " + file, e);
                return false;
            }
        }

        private void Report(string message, Exception e)
        {
            if (reportedFailure) return; // once per CPU: a failing disk would otherwise log every save
            reportedFailure = true;
            if (SafeHouse.Logger == null) return;
            SafeHouse.Logger.LogWarning(e == null ? message : message + ": " + e.Message);
        }
    }
}
