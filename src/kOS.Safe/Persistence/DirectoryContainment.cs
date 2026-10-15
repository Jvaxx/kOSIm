using System;
using System.IO;
using kOS.Safe.Exceptions;

namespace kOS.Safe.Persistence
{
    /// <summary>
    /// Whether a file-system path is inside a directory, by directory boundary rather than
    /// text prefix: "Script-private" is not inside "Script" (IO-04). Lexical only: paths are
    /// normalized with <see cref="Path.GetFullPath(string)"/>, links are not resolved.
    /// Comparison is ordinal (case-sensitive) on every platform, so on a case-insensitive
    /// file system a differently cased path to the same place is refused, never the reverse.
    /// </summary>
    public static class DirectoryContainment
    {
        /// <summary>True if <paramref name="path"/> is <paramref name="root"/> or below it.</summary>
        public static bool IsWithin(string root, string path)
        {
            return Compare(root, path) >= 0;
        }

        /// <summary>True if <paramref name="path"/> is below <paramref name="root"/>, not the root itself.</summary>
        public static bool IsBelow(string root, string path)
        {
            return Compare(root, path) > 0;
        }

        /// <summary>
        /// The full path of <paramref name="relativePath"/> under <paramref name="root"/>.
        /// Throws if it leaves the root (through "..", or by being rooted itself).
        /// </summary>
        public static string Combine(string root, string relativePath)
        {
            string fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!IsWithin(root, fullPath))
                throw new KOSInvalidPathException("Path refers to parent directory", relativePath);
            return fullPath;
        }

        /// <summary>-1 outside, 0 the root itself, 1 below it.</summary>
        private static int Compare(string root, string path)
        {
            if (root == null) throw new ArgumentNullException("root");
            if (path == null) throw new ArgumentNullException("path");

            string fullRoot = TrimSeparators(Path.GetFullPath(root));
            string fullPath = TrimSeparators(Path.GetFullPath(path));

            if (string.Equals(fullPath, fullRoot, StringComparison.Ordinal))
                return 0;
            if (!fullPath.StartsWith(fullRoot, StringComparison.Ordinal))
                return -1;
            // A file-system root ("/", "C:\") keeps its separator; any other root needs one next.
            if (IsSeparator(fullRoot[fullRoot.Length - 1]))
                return 1;
            return IsSeparator(fullPath[fullRoot.Length]) ? 1 : -1;
        }

        private static string TrimSeparators(string fullPath)
        {
            string fileSystemRoot = Path.GetPathRoot(fullPath) ?? "";
            int end = fullPath.Length;
            while (end > fileSystemRoot.Length && IsSeparator(fullPath[end - 1]))
                --end;
            return fullPath.Substring(0, end);
        }

        private static bool IsSeparator(char c)
        {
            return c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;
        }
    }
}
