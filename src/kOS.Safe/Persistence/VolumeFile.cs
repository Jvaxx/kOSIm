using kOS.Safe.Encapsulation.Suffixes;
using kOS.Safe.Exceptions;
using System.Linq;
using kOS.Safe.Encapsulation;

namespace kOS.Safe.Persistence
{
    [kOS.Safe.Utilities.KOSNomenclature("VolumeFile")]
    public abstract class VolumeFile : VolumeItem
    {
        protected VolumeFile(Volume volume, VolumePath path) : base(volume, path)
        {
            InitializeSuffixes();
        }

        public abstract FileContent ReadAll();

        /// <summary>
        /// Read without text/newline conversion. Built-in volumes preserve bytes.
        /// Legacy third-party implementations fall back to ReadAll until overridden.
        /// </summary>
        public virtual FileContent ReadAllRaw() { return ReadAll(); }

        public abstract bool Write(byte[] content);

        /// <summary>
        /// Append without text/newline conversion. The legacy fallback preserves existing
        /// provider behavior, not a byte-exact guarantee for third-party implementations.
        /// </summary>
        public virtual bool WriteRaw(byte[] content) { return Write(content); }

        public bool Write(string content)
        {
            return Write(FileContent.EncodeString(content));
        }

        public bool WriteLn(string content)
        {
            return Write(content + FileContent.NewLine);
        }

        public abstract void Clear();

        public override string ToString()
        {
            return Name;
        }

        private void InitializeSuffixes()
        {
            AddSuffix("READALL", new Suffix<FileContent>(ReadAll));
            AddSuffix("READRAW", new Suffix<FileContent>(ReadAllRaw));
            AddSuffix("WRITERAW", new OneArgsSuffix<BooleanValue, Structure>(content => WriteRawObject(content)));
            AddSuffix("WRITE", new OneArgsSuffix<BooleanValue, Structure>(str => WriteObject(str)));
            AddSuffix("WRITELN", new OneArgsSuffix<BooleanValue, StringValue>(str => new BooleanValue(WriteLn(str))));
            AddSuffix("CLEAR", new NoArgsVoidSuffix(Clear));
        }

        private bool WriteRawObject(Structure content)
        {
            var fileContent = content as FileContent;
            if (fileContent == null)
            {
                if (content == null) throw new KOSCastException("null", "FileContent");
                throw new KOSCastException(content.GetType(), typeof(FileContent));
            }
            return WriteRaw(fileContent.Bytes);
        }

        private bool WriteObject(Structure content)
        {
            if (content is StringValue)
            {
                return Write(content.ToString());
            }

            var stringValue = content as FileContent;
            if (stringValue != null)
            {
                FileContent fileContent = stringValue;
                return Write(fileContent.Bytes);
            }

            throw new KOSException("Only instances of string and FileContent can be written");
        }
    }
}