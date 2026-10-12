using System;
using System.IO;
using System.Linq;
using System.Reflection;
using kOS.Safe.Compilation;
using kOS.Safe.Compilation.KS;
using kOS.Safe.Encapsulation;
using kOS.Safe.Encapsulation.Suffixes;
using kOS.Safe.Execution;
using kOS.Safe.Persistence;
using kOS.Safe.Test.Opcode;
using kOS.Safe.Utilities;
using NUnit.Framework;

namespace kOS.Safe.Test.Persistence
{
    [TestFixture]
    public class RawFileIoTest
    {
        private string root;
        private Archive archive;
        private Harddisk disk;
        private bool previousWindows;
        private static readonly PropertyInfo WindowsProperty = typeof(SafeHouse).GetProperty("IsWindows");

        [SetUp]
        public void SetUp()
        {
            SafeHouse.Logger = new TestLogger();
            previousWindows = SafeHouse.IsWindows;
            root = Path.Combine(Path.GetTempPath(), "kosim-raw-io-" + Guid.NewGuid().ToString("N"));
            archive = new Archive(root);
            disk = new Harddisk(32768);
        }

        [TearDown]
        public void TearDown()
        {
            // Exercise the real managed Windows branch on the host without changing
            // config/Unity or claiming Windows/Proton filesystem runtime validation.
            SetWindows(previousWindows);
            Directory.Delete(root, true);
        }

        private static void SetWindows(bool windows)
        {
            WindowsProperty.GetSetMethod(true).Invoke(null, new object[] { windows });
        }

        private static byte[][] Corpus()
        {
            return new[] {
                new byte[0], new byte[] { 0xff }, new byte[] { 0xff, 0xfe }, new byte[] { 0xff, 0x00, 0xfe },
                FileContent.EncodeString("é"), FileContent.EncodeString("text łąż\n"),
                new byte[] { 65, 66, 67, 68, 0xff, 13, 10 },
                new byte[] { 65, 66, 67, 68, 0xe2, 0x82 },
                Enumerable.Range(0, 256).Select(i => (byte)i).ToArray(),
                new byte[] { 0xef, 0xbb, 0xbf }, new byte[] { 0xef, 0xbb, 0xbf, 65, 13, 10 },
                new byte[] { 0xff, 0xfe, 65, 0, 13, 0, 10, 0 },
                new byte[] { 107, 3, 88, 69, 0xff, 13, 10 }, // KSM magic + opaque payload
                new byte[] { 31, 139, 8, 0, 0xff, 13, 10 }, // gzip header + opaque payload
                FileContent.EncodeString("line\nnext\r\nend\rlast\r\r\n"),
                new byte[] { 65, 66, 67, 68 }.Concat(Enumerable.Range(0, 256).Select(i => (byte)i)).ToArray()
            };
        }

        private byte[] PhysicalBytes(Volume volume, VolumePath path)
        {
            return volume == archive ? File.ReadAllBytes(archive.GetArchivePath(path)) : ((VolumeFile)volume.Open(path)).ReadAll().Bytes;
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void RawSaveReadAndAppendAreExactForCorpus(bool windows, bool useArchive)
        {
            SetWindows(windows);
            Volume volume = useArchive ? (Volume)archive : disk;
            int index = 0;
            foreach (byte[] bytes in Corpus())
            {
                var path = VolumePath.FromString("/case" + index++);
                VolumeFile file = volume.SaveFileRaw(path, new FileContent(bytes));
                CollectionAssert.AreEqual(bytes, PhysicalBytes(volume, path));
                CollectionAssert.AreEqual(bytes, file.ReadAllRaw().Bytes);
                Assert.AreEqual(bytes.Length, file.ReadAllRaw().Size);
                Assert.IsTrue(file.WriteRaw(bytes));
                CollectionAssert.AreEqual(bytes.Concat(bytes).ToArray(), PhysicalBytes(volume, path));
                CollectionAssert.AreEqual(bytes.Concat(bytes).ToArray(), file.ReadAllRaw().Bytes);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EverySingleByteSurvivesRawArchiveRoundTrip(bool windows)
        {
            SetWindows(windows);
            var path = VolumePath.FromString("/one-byte");
            for (int i = 0; i < 256; i++)
            {
                byte[] bytes = { (byte)i };
                VolumeFile file = archive.SaveFileRaw(path, new FileContent(bytes));
                CollectionAssert.AreEqual(bytes, File.ReadAllBytes(archive.GetArchivePath(path)), "byte " + i);
                CollectionAssert.AreEqual(bytes, file.ReadAllRaw().Bytes, "byte " + i);
            }
        }

        [TestCase(false, "archive-disk")]
        [TestCase(true, "archive-disk")]
        [TestCase(false, "disk-archive")]
        [TestCase(true, "disk-archive")]
        [TestCase(false, "disk-disk")]
        [TestCase(true, "disk-disk")]
        [TestCase(false, "archive-archive")]
        [TestCase(true, "archive-archive")]
        public void CopyAndMovePreserveCorpusAcrossBuiltInVolumes(bool windows, string route)
        {
            SetWindows(windows);
            Volume source = route.StartsWith("archive") ? (Volume)archive : disk;
            Volume target = route.EndsWith("archive") ? (Volume)archive : new Harddisk(32768);
            var manager = new VolumeManager();
            manager.Add(source);
            manager.Add(target);
            int sourceId = manager.GetVolumeId(source);
            int targetId = manager.GetVolumeId(target);
            int index = 0;
            foreach (byte[] bytes in Corpus())
            {
                var from = GlobalPath.FromString(sourceId + ":/from" + index);
                var copy = GlobalPath.FromString(targetId + ":/copy" + index);
                var move = GlobalPath.FromString(targetId + ":/move" + index++);
                source.SaveFileRaw(from, new FileContent(bytes));
                target.SaveFileRaw(copy, new FileContent("existing destination"));
                Assert.IsTrue(manager.Copy(from, copy));
                CollectionAssert.AreEqual(bytes, PhysicalBytes(target, copy));
                CollectionAssert.AreEqual(bytes, PhysicalBytes(source, from));
                Assert.IsTrue(manager.Move(from, move));
                CollectionAssert.AreEqual(bytes, PhysicalBytes(target, move));
                Assert.IsNull(source.Open(from));
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void DirectoryCopiesAndMovesUseRawContents(bool windows, bool archiveSource)
        {
            SetWindows(windows);
            Volume source = archiveSource ? (Volume)archive : disk;
            Volume target = archiveSource ? (Volume)disk : archive;
            var manager = new VolumeManager();
            manager.Add(source);
            manager.Add(target);
            byte[] bytes = { 65, 66, 67, 68, 0xff, 13, 10 };
            source.SaveFileRaw(VolumePath.FromString("/tree/nested/binary"), new FileContent(bytes));
            target.CreateDirectory(VolumePath.FromString("/destination"));
            Assert.IsTrue(manager.Copy(GlobalPath.FromString("0:/tree"), GlobalPath.FromString("1:/destination")));
            CollectionAssert.AreEqual(bytes, PhysicalBytes(target, VolumePath.FromString("/destination/tree/nested/binary")));
            Assert.IsTrue(manager.Move(GlobalPath.FromString("0:/tree"), GlobalPath.FromString("1:/moved")));
            CollectionAssert.AreEqual(bytes, PhysicalBytes(target, VolumePath.FromString("/moved/nested/binary")));
            Assert.IsNull(source.Open("/tree"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FileToDirectoryCopyUsesRawBytes(bool windows)
        {
            SetWindows(windows);
            var manager = new VolumeManager();
            manager.Add(disk);
            manager.Add(archive);
            var from = GlobalPath.FromString("0:/binary");
            byte[] bytes = { 0xff };
            disk.SaveFileRaw(from, new FileContent(bytes));
            archive.CreateDirectory(VolumePath.FromString("/destination"));
            Assert.IsTrue(manager.Copy(from, GlobalPath.FromString("1:/destination")));
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(Path.Combine(root, "destination", "binary")));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SaveFromVolumeFileHelperUsesRawContents(bool windows)
        {
            SetWindows(windows);
            var path = VolumePath.FromString("/binary");
            byte[] bytes = { 0xff, 0xfe };
            VolumeFile source = disk.SaveFileRaw(path, new FileContent(bytes));
            archive.SaveFile(source);
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(archive.GetArchivePath(path)));
            disk.SaveFile((VolumeFile)archive.Open(path));
            CollectionAssert.AreEqual(bytes, ((VolumeFile)disk.Open(path)).ReadAllRaw().Bytes);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FullTargetDoesNotDeleteSourceOnFailedMove(bool windows)
        {
            SetWindows(windows);
            var from = GlobalPath.FromString("0:/binary");
            byte[] bytes = { 0xff };
            archive.SaveFileRaw(from, new FileContent(bytes));
            var manager = new VolumeManager();
            manager.Add(archive);
            manager.Add(new Harddisk(0));
            Assert.IsFalse(manager.Move(from, GlobalPath.FromString("1:/binary")));
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(archive.GetArchivePath(from)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CapacityChecksUseActualTransferredBytesNotLegacyTextLength(bool windows)
        {
            SetWindows(windows);
            var from = GlobalPath.FromString("0:/newlines");
            byte[] bytes = { 13, 10 };
            archive.SaveFileRaw(from, new FileContent(bytes));
            Assert.AreEqual(1, ((VolumeFile)archive.Open(from)).ReadAll().Size);
            var manager = new VolumeManager();
            manager.Add(archive);
            manager.Add(new Harddisk(1));
            Assert.IsFalse(manager.Move(from, GlobalPath.FromString("1:/newlines")));
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(archive.GetArchivePath(from)));
            Assert.IsNull(manager.GetVolume(1).Open("/newlines"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FailedRawArchiveCommitPreservesSourceAndOriginalTarget(bool existing)
        {
            SetWindows(true);
            byte[] bytes = { 0xff };
            var from = GlobalPath.FromString("0:/binary");
            var to = GlobalPath.FromString("1:/binary");
            disk.SaveFileRaw(from, new FileContent(bytes));
            string target = archive.GetArchivePath(to);
            if (existing) File.WriteAllText(target, "original");
            var files = new FailCommitOperations(bytes);
            var failing = new Archive(root, files);
            var manager = new VolumeManager();
            manager.Add(disk);
            manager.Add(failing);
            Assert.Throws<IOException>(() => manager.Move(from, to));
            CollectionAssert.AreEqual(bytes, ((VolumeFile)disk.Open(from)).ReadAllRaw().Bytes);
            if (existing) Assert.AreEqual("original", File.ReadAllText(target));
            else Assert.IsFalse(File.Exists(target));
            Assert.AreEqual(0, Directory.GetFiles(root, ".kosim-save-*.tmp").Length);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LegacyReadAllKeepsTextViewWhileReadRawKeepsExactBytes(bool windows)
        {
            SetWindows(windows);
            string physical = Path.Combine(root, "mixed");
            File.WriteAllBytes(physical, FileContent.EncodeString("line\r\nnext\n"));
            VolumeFile file = (VolumeFile)archive.Open("/mixed");
            Assert.AreEqual("line\nnext\n", file.ReadAll().String);
            Assert.AreEqual("line\r\nnext\n", file.ReadAllRaw().String);
            File.WriteAllBytes(physical, new byte[] { 0xff });
            CollectionAssert.AreEqual(new byte[] { 0xef, 0xbf, 0xbd }, file.ReadAll().Bytes);
            CollectionAssert.AreEqual(new byte[] { 0xff }, file.ReadAllRaw().Bytes);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LegacySaveAndAppendKeepPlatformTextBehavior(bool windows)
        {
            SetWindows(windows);
            var path = VolumePath.FromString("/text");
            VolumeFile file = archive.SaveFile(path, new FileContent("line\r\nnext\n"));
            Assert.IsTrue(file.Write("end\r\n"));
            string expected = windows ? "line\r\nnext\r\nend\r\n" : "line\r\nnext\nend\r\n";
            Assert.AreEqual(expected, File.ReadAllText(archive.GetArchivePath(path)));
            Assert.AreEqual("line\nnext\nend\n", file.ReadAll().String);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RawAppendDoesNotAddNewlineOrConvertStringBytes(bool windows)
        {
            SetWindows(windows);
            var path = VolumePath.FromString("/text");
            VolumeFile file = archive.SaveFileRaw(path, new FileContent());
            Assert.IsTrue(file.WriteRaw(FileContent.EncodeString("line\nend\r\n")));
            Assert.AreEqual("line\nend\r\n", File.ReadAllText(archive.GetArchivePath(path)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RawSuffixesExposeBytesAndAppendFileContent(bool useArchive)
        {
            SetWindows(true);
            Volume volume = useArchive ? (Volume)archive : disk;
            var path = VolumePath.FromString("/suffix");
            VolumeFile file = volume.SaveFileRaw(path, new FileContent(new byte[] { 0xff }));
            var raw = (FileContent)file.GetSuffix("READRAW").Value;
            CollectionAssert.AreEqual(new byte[] { 0xff }, raw.Bytes);
            Assert.AreEqual(1, ((ScalarValue)raw.GetSuffix("LENGTH").Value).GetIntValue());
            var binary = (ListValue)raw.GetSuffix("BINARY").Value;
            Assert.AreEqual(ScalarValue.Create(255), binary[0]);
            Assert.IsTrue((BooleanValue)Invoke(file, "WRITERAW", new FileContent(new byte[] { 13, 10, 0xfe })));
            CollectionAssert.AreEqual(new byte[] { 0xff, 13, 10, 0xfe }, PhysicalBytes(volume, path));
        }

        [Test]
        public void RawSuffixDoesNotImplicitlyEncodeStrings()
        {
            VolumeFile file = archive.SaveFileRaw(VolumePath.FromString("/suffix"), new FileContent());
            Assert.Throws<kOS.Safe.Exceptions.KOSCastException>(() => Invoke(file, "WRITERAW", new StringValue("text")));
            Assert.AreEqual(0, file.ReadAllRaw().Size);
        }

        [Test]
        public void FileContentDecodingAndSerializationKeepApprovedLegacyPolicy()
        {
            byte[] bytes = { 0xef, 0xbb, 0xbf, 65, 0xff, 13, 10 };
            var content = new FileContent(bytes);
            Assert.AreEqual("\ufeffA\ufffd\r\n", content.String);
            CollectionAssert.AreEqual(bytes, content.Bytes);
            var restored = new FileContent();
            restored.LoadDump(content.Dump());
            CollectionAssert.AreEqual(bytes, restored.Bytes);
            Assert.AreEqual(content.String, restored.String);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReadingRawReturnsIndependentContent(bool useArchive)
        {
            Volume volume = useArchive ? (Volume)archive : disk;
            var path = VolumePath.FromString("/bytes");
            VolumeFile file = volume.SaveFileRaw(path, new FileContent(new byte[] { 0xff }));
            file.ReadAllRaw().Bytes[0] = 0;
            CollectionAssert.AreEqual(new byte[] { 0xff }, file.ReadAllRaw().Bytes);
        }

        [Test]
        public void OldFileProviderRawDefaultsCallLegacyMethods()
        {
            var file = new LegacyFile(archive, VolumePath.FromString("/legacy"));
            Assert.AreEqual("provider view", file.ReadAllRaw().String);
            Assert.AreEqual(1, file.ReadCalls);
            Assert.IsFalse(file.WriteRaw(new byte[] { 0xff }));
            Assert.AreEqual(1, file.WriteCalls);
            CollectionAssert.AreEqual(new byte[] { 0xff }, file.Written);
        }

        [Test]
        public void OldVolumeProviderRawSaveDefaultPreservesArgumentsAndReturnValue()
        {
            var volume = new LegacyVolume();
            var path = VolumePath.FromString("/legacy");
            VolumeFile result = volume.SaveFileRaw(path, new FileContent(new byte[] { 0xff }), false);
            Assert.IsNotNull(result);
            Assert.AreEqual(1, volume.SaveCalls);
            Assert.IsFalse(volume.VerifyFreeSpace);
            CollectionAssert.AreEqual(new byte[] { 0xff }, result.ReadAllRaw().Bytes);
        }

        [Test]
        public void InheritedArchiveRawMethodDoesNotBypassLegacyProviderPolicy()
        {
            var provider = new PolicyArchive(root);
            var from = GlobalPath.FromString("0:/bytes");
            disk.SaveFileRaw(from, new FileContent(new byte[] { 0xff }));
            var manager = new VolumeManager();
            manager.Add(disk);
            manager.Add(provider);
            Assert.Throws<UnauthorizedAccessException>(() => manager.Copy(from, GlobalPath.FromString("1:/bytes")));
            Assert.AreEqual(1, provider.SaveCalls);
            Assert.IsFalse(File.Exists(Path.Combine(root, "bytes")));
        }

        [Test]
        public void InheritedArchiveFileRawMethodsKeepLegacyProviderHooks()
        {
            var file = new PolicyArchiveFile(archive, new FileInfo(Path.Combine(root, "legacy")), VolumePath.FromString("/legacy"));
            Assert.AreEqual("provider view", file.ReadAllRaw().String);
            Assert.IsFalse(file.WriteRaw(new byte[] { 0xff }));
            Assert.AreEqual(1, file.ReadCalls);
            Assert.AreEqual(1, file.WriteCalls);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ByteExactBootStyleCopyStillCompilesAndCompiledContentRoundTrips(bool windows)
        {
            SetWindows(windows);
            var path = GlobalPath.FromString("1:/boot.ks");
            string source = "local copiedvalue is 1.\r\nset copiedvalue to copiedvalue + 2.\r\n";
            VolumeFile archiveFile = archive.SaveFileRaw(path, new FileContent(source));
            VolumeFile copied = disk.SaveFileRaw(path, archiveFile.ReadAllRaw());
            Assert.AreEqual(source, copied.ReadAll().String);
            var parts = new KSScript().Compile(path, 1, copied.ReadAll().String, "", new CompilerOptions());
            Assert.IsTrue(parts.Count > 0);
            CompiledObject.InitTypeData();
            kOS.Safe.Compilation.Opcode.InitMachineCodeData();
            var packed = new FileContent(parts);
            Assert.AreEqual(FileCategory.KSM, packed.Category);
            VolumeFile compiled = archive.SaveFileRaw(VolumePath.FromString("/boot.ksm"), packed);
            CollectionAssert.AreEqual(packed.Bytes, compiled.ReadAllRaw().Bytes);
            var unpacked = compiled.ReadAllRaw().AsParts(path, "raw-copy-test");
            Assert.AreEqual(parts.Count, unpacked.Count);
            Assert.AreEqual(parts.Sum(part => part.MainCode.Count), unpacked.Sum(part => part.MainCode.Count));
            // Real headless compile/pack/unpack; not CPU execution or KSP boot validation.
        }

        private static object Invoke(VolumeFile file, string name, object argument)
        {
            var suffix = (DelegateSuffixResult)file.GetSuffix(name);
            var cpu = new FakeCpu();
            cpu.PushArgumentStack(null);
            cpu.PushArgumentStack(new KOSArgMarkerType());
            cpu.PushArgumentStack(argument);
            suffix.Invoke(cpu);
            return suffix.Value;
        }

        private sealed class FailCommitOperations : ArchiveFileOperations
        {
            private readonly byte[] expected;
            internal FailCommitOperations(byte[] expected) { this.expected = expected; }
            internal override void Replace(string source, string destination) { Fail(source); }
            internal override void Move(string source, string destination) { Fail(source); }
            private void Fail(string source)
            {
                CollectionAssert.AreEqual(expected, File.ReadAllBytes(source));
                throw new IOException("injected raw commit failure");
            }
        }

        private sealed class LegacyFile : VolumeFile
        {
            internal int ReadCalls;
            internal int WriteCalls;
            internal byte[] Written;
            internal LegacyFile(Volume volume, VolumePath path) : base(volume, path) { }
            public override int Size { get { return 13; } }
            public override FileContent ReadAll() { ReadCalls++; return new FileContent("provider view"); }
            public override bool Write(byte[] content) { WriteCalls++; Written = content; return false; }
            public override void Clear() { }
        }

        private sealed class LegacyVolume : Volume
        {
            private readonly Harddisk inner = new Harddisk(1000);
            internal int SaveCalls;
            internal bool VerifyFreeSpace;
            public override VolumeDirectory Root { get { return inner.Root; } }
            public override void Clear() { inner.Clear(); }
            public override VolumeItem Open(VolumePath path, bool ksmDefault = false) { return inner.Open(path, ksmDefault); }
            public override VolumeDirectory CreateDirectory(VolumePath path) { return inner.CreateDirectory(path); }
            public override VolumeFile CreateFile(VolumePath path) { return inner.CreateFile(path); }
            public override bool Exists(VolumePath path, bool ksmDefault = false) { return inner.Exists(path, ksmDefault); }
            public override bool Delete(VolumePath path, bool ksmDefault = false) { return inner.Delete(path, ksmDefault); }
            public override VolumeFile SaveFile(VolumePath path, FileContent content, bool verifyFreeSpace = true)
            {
                SaveCalls++;
                VerifyFreeSpace = verifyFreeSpace;
                return inner.SaveFile(path, content, verifyFreeSpace);
            }
        }

        private sealed class PolicyArchive : Archive
        {
            internal int SaveCalls;
            internal PolicyArchive(string directory) : base(directory) { }
            public override VolumeFile SaveFile(VolumePath path, FileContent content, bool verifyFreeSpace = true)
            {
                SaveCalls++;
                throw new UnauthorizedAccessException("legacy provider policy denies saves");
            }
        }

        private sealed class PolicyArchiveFile : ArchiveFile
        {
            internal int ReadCalls;
            internal int WriteCalls;
            internal PolicyArchiveFile(Archive archive, FileInfo info, VolumePath path) : base(archive, info, path) { }
            public override FileContent ReadAll() { ReadCalls++; return new FileContent("provider view"); }
            public override bool Write(byte[] bytes) { WriteCalls++; return false; }
        }
    }
}
