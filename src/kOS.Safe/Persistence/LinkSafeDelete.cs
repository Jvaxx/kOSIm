using System.IO;

namespace kOS.Safe.Persistence
{
    /// <summary>
    /// Archive symlink policy (IO-04): links the player made in the archive are followed to
    /// read, write and list, but a delete never enters one. Deleting a link removes the link;
    /// deleting a directory removes the links inside it, not what they point to. A link is a
    /// file-system entry with <see cref="FileAttributes.ReparsePoint"/> (Windows symlinks and
    /// junctions; Unix symlinks on Mono; how Wine reports a Unix symlink is not verified).
    /// </summary>
    public static class LinkSafeDelete
    {
        /// <summary>Delete a directory and everything in it, or only the link if it is one.</summary>
        public static void DeleteDirectory(string path)
        {
            var directory = new DirectoryInfo(path);
            if (!IsLink(directory))
                DeleteContents(directory);
            // Not recursive: a link is removed as itself, a real directory is empty by now.
            directory.Delete(false);
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
                else
                    entry.Delete(); // a file, or a link to one: removes the link itself
            }
        }

        public static bool IsLink(FileSystemInfo entry)
        {
            return (entry.Attributes & FileAttributes.ReparsePoint) != 0;
        }
    }
}
