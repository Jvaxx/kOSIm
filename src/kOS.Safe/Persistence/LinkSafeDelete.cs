using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace kOS.Safe.Persistence
{
    /// <summary>
    /// Archive symlink policy (IO-04): links the player made in the archive are followed to
    /// read, write and list, but a delete never enters one. Deleting a link removes the link;
    /// deleting a directory removes the links inside it, not what they point to. A link is a
    /// file-system entry with <see cref="FileAttributes.ReparsePoint"/> (Windows symlinks and
    /// junctions; Unix symlinks on Mono, and directory symlinks under Wine), or, under Wine, a
    /// file whose Unix path is not where it is listed (Wine shows a file symlink as its target).
    /// <para>
    /// Under Wine (Proton) a link cannot be deleted safely (found 2026-10-19): deleting a file
    /// link deletes its <em>target</em>, also when the link is opened as a reparse point, and a
    /// directory link cannot be removed. There, <see cref="LinksCanBeDeleted"/> is false and
    /// callers refuse before deleting anything (<see cref="FirstLinkIn"/>).
    /// </para>
    /// </summary>
    public static class LinkSafeDelete
    {
        // Tests set this to simulate Wine (false) on the host; null means detect.
        internal static bool? linksCanBeDeletedOverride;

        private static readonly Lazy<bool> runningUnderWine = new Lazy<bool>(DetectWine);

        /// <summary>False under Wine, where deleting a link would delete its target.</summary>
        public static bool LinksCanBeDeleted
        {
            get { return linksCanBeDeletedOverride ?? !runningUnderWine.Value; }
        }

        /// <summary>Delete a file, or only the link if it is one.</summary>
        public static void DeleteFile(string path)
        {
            var file = new FileInfo(path);
            if (IsLink(file))
                DeleteLink(file);
            else
                file.Delete();
        }

        /// <summary>Delete a directory and everything in it, or only the link if it is one.</summary>
        public static void DeleteDirectory(string path)
        {
            var directory = new DirectoryInfo(path);
            if (IsLink(directory))
            {
                DeleteLink(directory);
                return;
            }
            DeleteContents(directory);
            directory.Delete(false); // empty by now
        }

        /// <summary>Delete everything in a directory, keeping the directory.</summary>
        public static void DeleteContents(string path)
        {
            DeleteContents(new DirectoryInfo(path));
        }

        private static void DeleteContents(DirectoryInfo directory)
        {
            foreach (FileSystemInfo entry in directory.GetFileSystemInfos())
            {
                if (entry is DirectoryInfo)
                    DeleteDirectory(entry.FullName);
                else if (IsLink(entry))
                    DeleteLink(entry);
                else
                    entry.Delete();
            }
        }

        public static bool IsLink(FileSystemInfo entry)
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                return true;
            // Wine marks a directory link as a reparse point but shows a file link as the file
            // it points to, to every Win32 query tried (2026-10-19); only the Unix path Wine
            // maps it to gives it away.
            return runningUnderWine.Value && ResolvesElsewhere(entry.FullName);
        }

        /// <summary>
        /// The path itself if it is a link, else the first link inside it (never entering a
        /// link), else null.
        /// </summary>
        public static string FirstLinkIn(string path)
        {
            FileSystemInfo entry = Directory.Exists(path) ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path);
            if (IsLink(entry))
                return entry.FullName;
            var directory = entry as DirectoryInfo;
            if (directory == null)
                return null;
            foreach (FileSystemInfo child in directory.GetFileSystemInfos())
            {
                string found = FirstLinkIn(child.FullName);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static void DeleteLink(FileSystemInfo link)
        {
            // Callers check first; this keeps a missed check from deleting a link's target.
            if (!LinksCanBeDeleted)
                throw new IOException("Can't delete the link " + link.FullName + " under Wine");
            link.Delete(); // removes the link itself, file or directory (Windows and Unix Mono)
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandleW(string moduleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string procName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr wine_get_unix_file_name(string dosPath); // Wine only

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetProcessHeap();

        [DllImport("kernel32.dll")]
        private static extern bool HeapFree(IntPtr heap, uint flags, IntPtr memory);

        /// <summary>
        /// Under Wine: whether the entry's Unix path, which Wine resolves through links, is not
        /// its parent's Unix path plus its own name. True also when either cannot be mapped
        /// (refusing is safe). The archive folder being a link is fine: the parent resolves too.
        /// </summary>
        private static bool ResolvesElsewhere(string path)
        {
            path = path.TrimEnd(Path.DirectorySeparatorChar);
            string parent = Path.GetDirectoryName(path);
            if (parent == null)
                return false;
            string name = Path.GetFileName(path);
            string unixPath = UnixPath(path), unixParent = UnixPath(parent);
            if (unixPath == null || unixParent == null)
                return true;
            int slash = unixPath.LastIndexOf('/');
            if (slash < 0 || unixPath.Substring(0, slash).TrimEnd('/') != unixParent.TrimEnd('/'))
                return true;
            string unixName = unixPath.Substring(slash + 1);
            if (unixName == name)
                return false;
            if (!string.Equals(unixName, name, StringComparison.OrdinalIgnoreCase))
                return true;
            // Differs only in case: either the path's own spelling (Wine matches names ignoring
            // case), or a link to a sibling whose name differs only in case.
            foreach (string entry in Directory.GetFileSystemEntries(parent))
            {
                if (Path.GetFileName(entry) == name)
                    return true;
            }
            return false;
        }

        private static string UnixPath(string path)
        {
            try
            {
                IntPtr unixPath = wine_get_unix_file_name(path);
                if (unixPath == IntPtr.Zero)
                    return null;
                try
                {
                    return PtrToUtf8(unixPath);
                }
                finally
                {
                    HeapFree(GetProcessHeap(), 0, unixPath);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string PtrToUtf8(IntPtr text)
        {
            int length = 0;
            while (Marshal.ReadByte(text, length) != 0)
                length++;
            var bytes = new byte[length];
            Marshal.Copy(text, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }

        private static bool DetectWine()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                return false;
            try
            {
                // Wine's ntdll exports wine_get_version; Windows' does not.
                IntPtr ntdll = GetModuleHandleW("ntdll.dll");
                return ntdll == IntPtr.Zero || GetProcAddress(ntdll, "wine_get_version") != IntPtr.Zero;
            }
            catch (Exception)
            {
                return true; // unknown: keep links (refusing a delete loses nothing)
            }
        }
    }
}
