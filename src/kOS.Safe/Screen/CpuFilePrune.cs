using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using kOS.Safe.Persistence;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// Removes the per-CPU files (terminal geometry: <c>saves/&lt;save&gt;/kOS/terminal</c>, named
    /// <c>&lt;persistentId&gt;[-&lt;n&gt;].txt</c>) of parts that no save of the game has any more: a
    /// vessel destroyed or recovered, a part removed (review L-02). Run when a save is loaded
    /// from the main menu (game side: <c>kOS.Module.CpuFilePruner</c>), on a worker thread.
    /// <para>
    /// The per-CPU command histories of earlier versions (<c>kOS/history/&lt;persistentId&gt;[-&lt;n&gt;].txt</c>)
    /// are replaced by the save's shared <see cref="SHARED_HISTORY_FILE"/> and deleted whatever
    /// the saves hold (user decision 2026-10-26); the folder goes once empty. Other names and
    /// links are kept, and nothing is done through a linked folder.
    /// </para>
    /// <para>
    /// A file is kept while its part is in the loaded game or in any save of its folder: every
    /// <c>.sfs</c> file in it and in its <c>Backup</c> folder (a quicksave or a named save may be
    /// loaded later), as any <c>persistentId = N</c> line in them. Nothing is deleted when a
    /// save cannot be read whole, so a doubt keeps every file. A file changed in the last
    /// <see cref="ArchiveTempFiles.LEFTOVER_AGE"/> is kept too (a part made since the load), and
    /// so is any other name. A save's temporary file left by a crash in these folders, or beside the
    /// shared history, is removed as in the archive (<see cref="ArchiveTempFiles"/>).
    /// </para>
    /// Uses only System.IO; safe on any thread.
    /// </summary>
    public static class CpuFilePrune
    {
        /// <summary>The folder under <c>saves/&lt;save&gt;/kOS</c> that holds per-CPU files.</summary>
        public const string GEOMETRY_FOLDER = "terminal";

        /// <summary>The folder under <c>saves/&lt;save&gt;/kOS</c> of the per-CPU command histories of earlier versions.</summary>
        public const string LEGACY_HISTORY_FOLDER = "history";

        /// <summary>The command history every CPU of the save shares, under <c>saves/&lt;save&gt;/kOS</c>.</summary>
        public const string SHARED_HISTORY_FILE = "history.txt";

        private static readonly string[] CPU_FOLDERS = { GEOMETRY_FOLDER };

        /// <summary>What a run did, for the log.</summary>
        public sealed class Result
        {
            /// <summary>Per-CPU files deleted.</summary>
            public int Deleted { get; internal set; }
            /// <summary>Leftover temporary files deleted.</summary>
            public int TemporaryDeleted { get; internal set; }
            /// <summary>Per-CPU command histories of earlier versions deleted.</summary>
            public int LegacyHistoryDeleted { get; internal set; }
            /// <summary>Why nothing was pruned (a save that could not be read), else null.</summary>
            public string Skipped { get; internal set; }
        }

        /// <summary>
        /// Prune the per-CPU files of one save folder.
        /// </summary>
        /// <param name="saveFolder">the save's folder, <c>saves/&lt;save&gt;</c></param>
        /// <param name="loadedIds">the <c>persistentId</c> of every part of the loaded game</param>
        /// <param name="utcNow">now; files changed since <see cref="ArchiveTempFiles.LEFTOVER_AGE"/> before it are kept</param>
        public static Result Run(string saveFolder, ICollection<uint> loadedIds, DateTime utcNow)
        {
            if (saveFolder == null) throw new ArgumentNullException("saveFolder");
            if (loadedIds == null) throw new ArgumentNullException("loadedIds");
            var result = new Result();
            string kosFolder = Path.Combine(saveFolder, "kOS");
            if (Directory.Exists(kosFolder))
            {
                foreach (string file in Directory.GetFiles(kosFolder)) // beside the shared history
                    if (ArchiveTempFiles.TryDeleteLeftover(file, utcNow))
                        result.TemporaryDeleted++;
            }
            DeleteLegacyHistory(Path.Combine(kosFolder, LEGACY_HISTORY_FOLDER), utcNow, result);

            var candidates = new List<KeyValuePair<string, uint>>();
            foreach (string folder in CPU_FOLDERS)
            {
                string path = Path.Combine(saveFolder, "kOS", folder);
                if (!Directory.Exists(path)) continue;
                foreach (string file in Directory.GetFiles(path))
                {
                    if (ArchiveTempFiles.TryDeleteLeftover(file, utcNow))
                    {
                        result.TemporaryDeleted++;
                        continue;
                    }
                    uint id;
                    if (TryParseFileName(Path.GetFileName(file), out id) && !loadedIds.Contains(id))
                        candidates.Add(new KeyValuePair<string, uint>(file, id));
                }
            }
            if (candidates.Count == 0) return result; // the usual case: no save needs reading

            var savedIds = new HashSet<uint>();
            foreach (string save in SaveFiles(saveFolder))
            {
                string problem = AddSavedIds(save, savedIds);
                if (problem != null)
                {
                    result.Skipped = problem;
                    return result;
                }
            }

            foreach (KeyValuePair<string, uint> candidate in candidates)
            {
                if (savedIds.Contains(candidate.Value)) continue;
                try
                {
                    var file = new FileInfo(candidate.Key);
                    if (!file.Exists || file.LastWriteTimeUtc > utcNow - ArchiveTempFiles.LEFTOVER_AGE ||
                        LinkSafeDelete.IsLink(file))
                        continue;
                    file.Delete();
                    result.Deleted++;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException)
                {
                    // kept; the next load tries again
                }
            }
            return result;
        }

        /// <summary>
        /// Delete the per-CPU command histories of earlier versions, and the folder once empty.
        /// Failures keep the file; the next load tries again.
        /// </summary>
        private static void DeleteLegacyHistory(string folder, DateTime utcNow, Result result)
        {
            try
            {
                var directory = new DirectoryInfo(folder);
                if (!directory.Exists || LinkSafeDelete.IsLink(directory)) return;
                foreach (FileInfo file in directory.GetFiles())
                {
                    if (ArchiveTempFiles.TryDeleteLeftover(file.FullName, utcNow))
                    {
                        result.TemporaryDeleted++;
                        continue;
                    }
                    uint id;
                    if (!TryParseFileName(file.Name, out id) || LinkSafeDelete.IsLink(file)) continue;
                    try
                    {
                        file.Delete();
                        result.LegacyHistoryDeleted++;
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException)
                    {
                        // kept; the next load tries again
                    }
                }
                if (directory.GetFileSystemInfos().Length == 0)
                    directory.Delete();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException)
            {
                // kept; the next load tries again
            }
        }

        /// <summary>
        /// The part's <c>persistentId</c> from a per-CPU file's name (<c>80427209.txt</c>,
        /// <c>80427209-1.txt</c>), as <c>kOSProcessor</c> names them; false for any other name.
        /// </summary>
        public static bool TryParseFileName(string fileName, out uint id)
        {
            id = 0;
            if (fileName == null || !fileName.EndsWith(".txt", StringComparison.Ordinal)) return false;
            string stem = fileName.Substring(0, fileName.Length - 4);
            int dash = stem.IndexOf('-');
            string idText = dash < 0 ? stem : stem.Substring(0, dash);
            if (dash >= 0 && !AllDigits(stem.Substring(dash + 1))) return false;
            return AllDigits(idText) && uint.TryParse(idText, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id != 0;
        }

        private static bool AllDigits(string text)
        {
            if (text.Length == 0) return false;
            foreach (char c in text)
                if (c < '0' || c > '9') return false;
            return true;
        }

        /// <summary>The saves of a folder: its <c>.sfs</c> files and those of its <c>Backup</c> folder.</summary>
        private static IEnumerable<string> SaveFiles(string saveFolder)
        {
            foreach (string folder in new[] { saveFolder, Path.Combine(saveFolder, "Backup") })
            {
                if (!Directory.Exists(folder)) continue;
                // GetFiles("*.sfs") would also match a longer extension starting with sfs.
                foreach (string file in Directory.GetFiles(folder))
                    if (string.Equals(Path.GetExtension(file), ".sfs", StringComparison.OrdinalIgnoreCase))
                        yield return file;
            }
        }

        /// <summary>
        /// Add the ids of a save's <c>persistentId = N</c> lines. Null once read, else why it
        /// cannot be used: it cannot be read, or it is not whole (its braces do not balance or
        /// it does not end with one, as a save being written would not).
        /// </summary>
        public static string AddSavedIds(string saveFile, ISet<uint> ids)
        {
            try
            {
                using (var reader = new StreamReader(saveFile))
                {
                    return AddSavedIds(reader, ids) ? null : saveFile + " is not a whole save";
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException)
            {
                return "cannot read " + saveFile + ": " + e.Message;
            }
        }

        /// <summary>
        /// Add the ids of a save's <c>persistentId = N</c> lines (any node: parts, vessels, parts
        /// stored in an inventory); false when the text is not a whole save (see
        /// <see cref="AddSavedIds(string, ISet{uint})"/>). The ids are added either way.
        /// </summary>
        public static bool AddSavedIds(TextReader reader, ISet<uint> ids)
        {
            int depth = 0;
            bool broken = false, endsWithClose = false;
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                foreach (char c in line)
                {
                    if (c == '{') ++depth;
                    else if (c == '}' && --depth < 0) broken = true;
                }
                string trimmed = line.Trim();
                if (trimmed.Length > 0) endsWithClose = trimmed[trimmed.Length - 1] == '}';

                if (!trimmed.StartsWith("persistentId", StringComparison.Ordinal)) continue;
                string rest = trimmed.Substring("persistentId".Length).TrimStart();
                if (rest.Length == 0 || rest[0] != '=') continue;
                uint id;
                if (uint.TryParse(rest.Substring(1).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out id))
                    ids.Add(id);
            }
            return !broken && depth == 0 && endsWithClose;
        }
    }
}
