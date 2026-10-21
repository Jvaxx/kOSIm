using kOS.Safe.Encapsulation;
using kOS.Safe.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using kOS.Safe.Exceptions;

namespace kOS.Safe.Persistence
{
    [kOS.Safe.Utilities.KOSNomenclature("Archive")]
    public class Archive : Volume
    {
        public const string ArchiveName = "Archive";
        public ArchiveDirectory RootArchiveDirectory { get; private set; }

        private static string ArchiveFolder { get; set; }
        private readonly ArchiveFileWriter fileWriter;

        public override VolumeDirectory Root {
            get {
                return RootArchiveDirectory;
            }
        }

        public Archive(string archiveFolder) : this(archiveFolder, new ArchiveFileOperations())
        {
        }

        internal Archive(string archiveFolder, ArchiveFileOperations fileOperations)
        {
            fileWriter = new ArchiveFileWriter(fileOperations);
            ArchiveFolder = Path.GetFullPath(archiveFolder).TrimEnd(VolumePath.PathSeparator);
            CreateArchiveDirectory();
            Renameable = false;
            InitializeName(ArchiveName);

            RootArchiveDirectory = new ArchiveDirectory(this, VolumePath.EMPTY);
        }

        private void CreateArchiveDirectory()
        {
            Directory.CreateDirectory(ArchiveFolder);
        }

        public string GetArchivePath(VolumePath path)
        {
            if (path.PointsOutside)
            {
                throw new KOSInvalidPathException("Path refers to parent directory", path.ToString());
            }

            string mergedPath = ArchiveFolder;

            foreach (string segment in path.Segments)
            {
                mergedPath = Path.Combine(mergedPath, segment);
            }

            string fullPath = Path.GetFullPath(mergedPath);

            if (!DirectoryContainment.IsWithin(ArchiveFolder, fullPath))
            {
                throw new KOSInvalidPathException("Path refers to parent directory", path.ToString());
            }

            return fullPath;
        }

        public override void Clear()
        {
            // Keep the folder (it may be a link to the player's scripts); never delete through
            // a link inside it (IO-04).
            if (Directory.Exists(ArchiveFolder))
            {
                LinkSafeDelete.DeleteContents(ArchiveFolder);
            }

            Directory.CreateDirectory(ArchiveFolder);
        }

        /// <summary>
        /// Whether two existing paths differing only in case name the same entry: true on a
        /// case-insensitive file system (Windows, so under Proton), false on a case-sensitive one,
        /// where an existing path always matches the entries' names exactly. False when either
        /// does not exist (on a case-sensitive file system that is a different name).
        /// </summary>
        internal bool NameSameEntryIgnoringCase(VolumePath a, VolumePath b)
        {
            string pathA = GetArchivePath(a), pathB = GetArchivePath(b);
            if (!(File.Exists(pathA) || Directory.Exists(pathA)) || !(File.Exists(pathB) || Directory.Exists(pathB)))
                return false;
            return !(MatchesEntryNamesExactly(a) && MatchesEntryNamesExactly(b));
        }

        private bool MatchesEntryNamesExactly(VolumePath path)
        {
            string directory = ArchiveFolder;
            foreach (string segment in path.Segments)
            {
                bool found = false;
                foreach (string entry in Directory.GetFileSystemEntries(directory))
                {
                    if (string.Equals(Path.GetFileName(entry), segment, StringComparison.Ordinal))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                    return false;
                directory = Path.Combine(directory, segment);
            }
            return true;
        }

        public override VolumeItem Open(VolumePath path, bool ksmDefault = false)
        {
            try
            {
                var fileSystemInfo = Search(path, ksmDefault);

                if (fileSystemInfo == null) {
                    return null;
                }
                else if (fileSystemInfo is FileInfo)
                {
                    VolumePath filePath = VolumePath.FromString(fileSystemInfo.FullName.Substring(ArchiveFolder.Length).Replace(Path.DirectorySeparatorChar, VolumePath.PathSeparator));
                    return new ArchiveFile(this, fileSystemInfo as FileInfo, filePath);
                }
                else {
                    // we can use 'path' here, default extensions are not added to directories
                    return new ArchiveDirectory(this, path);
                }
            }
            catch (Exception e)
            {
                throw new KOSPersistenceException("Could not open path: " + path, e);
            }
        }

        public override VolumeDirectory CreateDirectory(VolumePath path)
        {
            string archivePath = GetArchivePath(path);

            if (Directory.Exists(archivePath))
            {
                throw new KOSPersistenceException("Already exists: " + path);
            }

            try
            {
                Directory.CreateDirectory(archivePath);
            }
            catch (IOException)
            {
                throw new KOSPersistenceException("Could not create directory: " + path);
            }

            return new ArchiveDirectory(this, path);
        }

        public override VolumeFile CreateFile(VolumePath path)
        {
            if (path.Depth == 0)
            {
                throw new KOSPersistenceException("Can't create a file over root directory");
            }

            string archivePath = GetArchivePath(path);

            if (File.Exists(archivePath))
            {
                throw new KOSPersistenceException("Already exists: " + path);
            }

            try
            {
                Directory.CreateDirectory(GetArchivePath(path.GetParent()));
            }
            catch (IOException)
            {
                throw new KOSPersistenceException("Parent directory for path does not exist: " + path.ToString());
            }

            try
            {
                File.Create(archivePath).Dispose();
            }
            catch (UnauthorizedAccessException)
            {
                throw new KOSPersistenceException("Could not create file: " + path);
            }

            return Open(path) as VolumeFile;
        }

        public override bool Exists(VolumePath path, bool ksmDefault = false)
        {
            return Search(path, ksmDefault) != null;
        }

        public override bool Delete(VolumePath path, bool ksmDefault = false)
        {
            if (path.Depth == 0)
            {
                throw new KOSPersistenceException("Can't delete root directory");
            }

            var fileSystemInfo = Search(path, ksmDefault);

            if (fileSystemInfo == null)
            {
                return false;
            }

            if (!LinkSafeDelete.LinksCanBeDeleted)
            {
                // Under Wine one walk both refuses a link and deletes (review P-11).
                if (LinkSafeDelete.DeleteUnlessLinked(fileSystemInfo.FullName) != null)
                    throw LinkRefused(path);
            }
            else if (fileSystemInfo is FileInfo)
            {
                LinkSafeDelete.DeleteFile(fileSystemInfo.FullName); // a link, not its target (IO-04)
            }
            else
            {
                LinkSafeDelete.DeleteDirectory(fileSystemInfo.FullName); // never through a link (IO-04)
            }

            return true;
        }

        /// <summary>
        /// Under Wine (Proton) a link cannot be deleted without deleting its target (IO-04):
        /// refuse, before anything is deleted (or copied, for a move), to delete an entry that
        /// is or contains a link.
        /// </summary>
        internal void CheckDeletable(VolumePath path, bool ksmDefault = false)
        {
            var fileSystemInfo = Search(path, ksmDefault);
            if (fileSystemInfo != null)
                CheckDeletable(path, fileSystemInfo);
        }

        private static void CheckDeletable(VolumePath path, FileSystemInfo fileSystemInfo)
        {
            if (!LinkSafeDelete.LinksCanBeDeleted && LinkSafeDelete.FirstLinkIn(fileSystemInfo.FullName) != null)
            {
                throw LinkRefused(path);
            }
        }

        private static KOSPersistenceException LinkRefused(VolumePath path)
        {
            return new KOSPersistenceException("Can't delete " + path + ": it is or contains a link, and under " +
                "Wine (Proton) deleting a link would delete what it points to. Remove the link outside the game.");
        }

        public override VolumeFile SaveFile(VolumePath path, FileContent content, bool verifyFreeSpace = true)
        {
            return SaveFileContents(path, content, true);
        }

        public override VolumeFile SaveFileRaw(VolumePath path, FileContent content, bool verifyFreeSpace = true)
        {
            // Existing derived providers may enforce policy in SaveFile. Do not bypass
            // their legacy override merely because they inherit the new raw method.
            // Such providers must explicitly override SaveFileRaw to opt into raw I/O.
            if (GetType() != typeof(Archive)) return base.SaveFileRaw(path, content, verifyFreeSpace);
            return SaveFileContents(path, content, false);
        }

        private VolumeFile SaveFileContents(VolumePath path, FileContent content, bool convertText)
        {
            Directory.CreateDirectory(ArchiveFolder);

            string archivePath = GetArchivePath(path);
            if (Directory.Exists(archivePath))
            {
                throw new KOSPersistenceException("Can't save file over a directory: " + path);
            }

            string parentPath = Directory.GetParent(archivePath).FullName;

            if (!Directory.Exists(parentPath))
            {
                Directory.CreateDirectory(parentPath);
            }

            byte[] fileBody = convertText ? ConvertToWindowsNewlines(content.Bytes) : content.Bytes;

            fileWriter.Write(archivePath, fileBody);

            return Open(path) as VolumeFile;
        }

        public static byte[] ConvertToWindowsNewlines(byte[] bytes)
        {
            FileCategory category = PersistenceUtilities.IdentifyCategory(bytes);

            if (SafeHouse.IsWindows && !PersistenceUtilities.IsBinary(category))
            {
                string asString = FileContent.DecodeString(bytes);
                // Legacy text path/classification and UTF-8 fallback are retained.
                // Normalize existing CRLF first so repeated saves do not add CRs.
                // Lone CRs and already doubled CRs are not guessed away.
                asString = asString.Replace("\r\n", "\n").Replace("\n", "\r\n");
                return FileContent.EncodeString(asString);
            }

            return bytes;
        }

        public static byte[] ConvertFromWindowsNewlines(byte[] bytes)
        {
            FileCategory category = PersistenceUtilities.IdentifyCategory(bytes);

            if (!PersistenceUtilities.IsBinary(category))
            {
                string asString = FileContent.DecodeString(bytes);
                // Only evil windows gets evil windows line breaks, and only if this is some sort of ASCII:
                asString = asString.Replace("\r\n", "\n");
                return FileContent.EncodeString(asString);
            }

            return bytes;
        }

        public override float RequiredPower()
        {
            const int MULTIPLIER = 5;
            const float POWER_REQUIRED = BASE_POWER * MULTIPLIER;

            return POWER_REQUIRED;
        }

        /// <summary>
        /// Get the file from the OS.
        /// </summary>
        /// <param name="name">filename to look for</param>
        /// <param name="ksmDefault">if true, it prefers to use the KSM filename over the KS.  The default is to prefer KS.</param>
        /// <returns>the full fileinfo of the filename if found</returns>
        private FileSystemInfo Search(VolumePath volumePath, bool ksmDefault)
        {
            var path = GetArchivePath(volumePath);

            if (Directory.Exists(path))
            {
                return new DirectoryInfo(path);
            }

            if (File.Exists(path))
            {
                return new FileInfo(path);
            }

            var kerboscriptFile = new FileInfo(PersistenceUtilities.CookedFilename(path, KERBOSCRIPT_EXTENSION, true));
            var kosMlFile = new FileInfo(PersistenceUtilities.CookedFilename(path, KOS_MACHINELANGUAGE_EXTENSION, true));

            if (kerboscriptFile.Exists && kosMlFile.Exists)
            {
                return ksmDefault ? kosMlFile : kerboscriptFile;
            }
            if (kerboscriptFile.Exists)
            {
                return kerboscriptFile;
            }
            if (kosMlFile.Exists)
            {
                return kosMlFile;
            }
            return null;
        }
    }
}