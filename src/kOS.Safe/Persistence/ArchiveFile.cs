using System;
using System.IO;

namespace kOS.Safe.Persistence
{
    [kOS.Safe.Utilities.KOSNomenclature("VolumeFile", KOSToCSharp = false)]
    public class ArchiveFile : VolumeFile
    {
        private readonly FileInfo fileInfo;

        public override int Size { get { fileInfo.Refresh(); return (int)fileInfo.Length; } }

        public ArchiveFile(Archive archive, FileInfo fileInfo, VolumePath path)
            : base(archive, path)
        {
            this.fileInfo = fileInfo;
        }

        public override FileContent ReadAll()
        {
            byte[] bytes = File.ReadAllBytes(fileInfo.FullName);

            bytes = Archive.ConvertFromWindowsNewlines(bytes);

            return new FileContent(bytes);
        }

        public override FileContent ReadAllRaw()
        {
            // Preserve existing derived-provider hooks until they explicitly opt in.
            if (GetType() != typeof(ArchiveFile)) return base.ReadAllRaw();
            return new FileContent(File.ReadAllBytes(fileInfo.FullName));
        }

        public override bool Write(byte[] content)
        {
            return Append(content, true);
        }

        public override bool WriteRaw(byte[] content)
        {
            if (GetType() != typeof(ArchiveFile)) return base.WriteRaw(content);
            return Append(content, false);
        }

        private bool Append(byte[] content, bool convertText)
        {
            if (!fileInfo.Exists)
            {
                throw new KOSFileException("File does not exist: " + fileInfo.Name);
            }

            byte[] bytes = convertText ? Archive.ConvertToWindowsNewlines(content) : content;
            using (FileStream stream = fileInfo.Open(FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
            }

            return true;
        }

        public override void Clear()
        {
            File.WriteAllText(fileInfo.FullName, string.Empty);
        }
    }
}
