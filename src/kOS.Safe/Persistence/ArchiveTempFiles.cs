using System;
using System.Collections.Generic;
using System.IO;

namespace kOS.Safe.Persistence
{
    /// <summary>
    /// The temporary files of atomic saves (<see cref="ArchiveFileWriter"/>, IO-03):
    /// <c>.kosim-save-&lt;32 hex digits&gt;.tmp</c> beside the file saved. A save removes its own,
    /// committed or failed; one is left only when the game stops in the middle of a save (a crash).
    /// Such a leftover is deleted when its folder is next listed, once it is
    /// <see cref="LEFTOVER_AGE"/> old (review L-03): a save takes milliseconds, so a file that old
    /// is no save in progress, also not one of another game sharing the folder through a link.
    /// </summary>
    internal static class ArchiveTempFiles
    {
        private const string PREFIX = ".kosim-save-";
        private const string SUFFIX = ".tmp";
        private const int HEX_DIGITS = 32; // Guid "N"

        /// <summary>How old a temporary file must be (last written) before a listing deletes it.</summary>
        internal static readonly TimeSpan LEFTOVER_AGE = TimeSpan.FromMinutes(1);

        /// <summary>A new temporary file's name.</summary>
        internal static string NewName()
        {
            return PREFIX + Guid.NewGuid().ToString("N") + SUFFIX;
        }

        /// <summary>Whether a file name is exactly one <see cref="NewName"/> makes.</summary>
        internal static bool IsTemporaryName(string fileName)
        {
            if (fileName == null || fileName.Length != PREFIX.Length + HEX_DIGITS + SUFFIX.Length ||
                !fileName.StartsWith(PREFIX, StringComparison.Ordinal) || !fileName.EndsWith(SUFFIX, StringComparison.Ordinal))
                return false;
            for (int i = PREFIX.Length; i < PREFIX.Length + HEX_DIGITS; ++i)
            {
                char c = fileName[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// The files of a folder listing without the leftovers this deleted. A leftover is a file
        /// with a temporary name, last written at least <see cref="LEFTOVER_AGE"/> before
        /// <paramref name="utcNow"/>, that is not a link. One that cannot be deleted is kept, as
        /// any other file, and the listing goes on.
        /// </summary>
        internal static string[] Sweep(string[] files, DateTime utcNow)
        {
            List<string> kept = null;
            for (int i = 0; i < files.Length; ++i)
            {
                if (!TryDeleteLeftover(files[i], utcNow))
                {
                    if (kept != null) kept.Add(files[i]);
                    continue;
                }
                if (kept == null)
                {
                    kept = new List<string>(files.Length);
                    for (int j = 0; j < i; ++j) kept.Add(files[j]);
                }
            }
            return kept == null ? files : kept.ToArray();
        }

        /// <summary>Delete the file if it is a leftover (<see cref="Sweep"/>); true if it was deleted.</summary>
        internal static bool TryDeleteLeftover(string path, DateTime utcNow)
        {
            if (!IsTemporaryName(Path.GetFileName(path)))
                return false;
            try
            {
                var file = new FileInfo(path);
                if (!file.Exists || file.LastWriteTimeUtc > utcNow - LEFTOVER_AGE || LinkSafeDelete.IsLink(file))
                    return false;
                file.Delete();
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException)
            {
                return false;
            }
        }
    }
}
