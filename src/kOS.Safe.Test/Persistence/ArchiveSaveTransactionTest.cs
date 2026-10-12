using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using kOS.Safe.Exceptions;
using kOS.Safe.Persistence;
using kOS.Safe.Screen;
using kOS.Safe.Utilities;
using NUnit.Framework;

namespace kOS.Safe.Test.Persistence
{
    [TestFixture]
    public class ArchiveSaveTransactionTest
    {
        private string root;
        private string destination;
        private VolumePath path;
        private Archive archive;
        private FaultOperations files;
        private static readonly byte[] Original = Encoding.UTF8.GetBytes("original contents\n");
        private static readonly byte[] Replacement = Encoding.UTF8.GetBytes("replacement contents\nmore text\n");

        [SetUp]
        public void SetUp()
        {
            SafeHouse.Logger = new TestLogger();
            root = Path.Combine(Path.GetTempPath(), "kosim-archive-save-" + Guid.NewGuid().ToString("N"));
            files = new FaultOperations();
            archive = new Archive(root, files);
            path = VolumePath.FromString("/program.ks");
            destination = archive.GetArchivePath(path);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(root, true);
        }

        private void AssertDestination(bool existing)
        {
            if (existing) CollectionAssert.AreEqual(Original, File.ReadAllBytes(destination));
            else Assert.IsFalse(File.Exists(destination));
        }

        private void AssertNoTemporaryFiles()
        {
            Assert.AreEqual(0, Directory.GetFiles(root, ".kosim-save-*.tmp", SearchOption.AllDirectories).Length);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CommitReceivesOnlyCompleteClosedSiblingFile(bool existing)
        {
            if (existing) File.WriteAllBytes(destination, Original);
            files.DuringWrite = () => AssertDestination(existing);
            files.BeforeCommit = (source, target) =>
            {
                AssertDestination(existing);
                Assert.AreEqual(destination, target);
                Assert.AreEqual(Path.GetDirectoryName(destination), Path.GetDirectoryName(source));
                Assert.AreNotEqual(target, source);
                Assert.AreEqual(FileMode.CreateNew, files.OpenMode);
                Assert.IsTrue(files.StreamClosed);
                CollectionAssert.AreEqual(Archive.ConvertToWindowsNewlines(Replacement), File.ReadAllBytes(source));
            };
            VolumeFile result = archive.SaveFile(path, new FileContent(Replacement));
            Assert.IsNotNull(result);
            Assert.AreSame(archive, result.Volume);
            Assert.AreEqual(path, result.Path);
            CollectionAssert.AreEqual(Archive.ConvertToWindowsNewlines(Replacement), File.ReadAllBytes(destination));
            Assert.AreEqual(existing ? 1 : 0, files.ReplaceCalls);
            Assert.AreEqual(existing ? 0 : 1, files.MoveCalls);
            Assert.AreEqual(0, files.Deleted.Count);
            AssertNoTemporaryFiles();
        }

        [TestCase(false, "create")]
        [TestCase(true, "create")]
        [TestCase(false, "write-start")]
        [TestCase(true, "write-start")]
        [TestCase(false, "write-partial")]
        [TestCase(true, "write-partial")]
        [TestCase(false, "flush")]
        [TestCase(true, "flush")]
        [TestCase(false, "close")]
        [TestCase(true, "close")]
        [TestCase(false, "exists")]
        [TestCase(true, "exists")]
        [TestCase(false, "commit")]
        [TestCase(true, "commit")]
        public void FailureBeforeSuccessfulCommitPreservesDestinationAndCleansOwnedTemporary(bool existing, string stage)
        {
            if (existing) File.WriteAllBytes(destination, Original);
            files.FailAt = stage;
            var failure = new IOException("injected " + stage + " failure (including simulated disk full)");
            files.Error = failure;
            Assert.AreSame(failure, Assert.Throws<IOException>(() => archive.SaveFile(path, new FileContent(Replacement))));
            AssertDestination(existing);
            AssertNoTemporaryFiles();
            Assert.AreEqual(stage == "create" ? 0 : 1, files.Deleted.Count);
            if (files.Deleted.Count > 0) Assert.AreEqual(files.Temporary, files.Deleted[0]);
            Assert.AreEqual(stage == "commit" ? 1 : 0, files.MoveCalls + files.ReplaceCalls);
        }

        [TestCase(false, "create")]
        [TestCase(true, "create")]
        [TestCase(false, "commit")]
        [TestCase(true, "commit")]
        public void PermissionErrorsPropagateWithoutDestructiveFallback(bool existing, string stage)
        {
            if (existing) File.WriteAllBytes(destination, Original);
            files.FailAt = stage;
            var failure = new UnauthorizedAccessException("injected permission denial");
            files.Error = failure;
            Assert.AreSame(failure, Assert.Throws<UnauthorizedAccessException>(() => archive.SaveFile(path, new FileContent(Replacement))));
            AssertDestination(existing);
            AssertNoTemporaryFiles();
            Assert.AreEqual(stage == "commit" ? 1 : 0, files.MoveCalls + files.ReplaceCalls);
        }

        [Test]
        public void UnsupportedReplaceDoesNotFallBackToMoveDeleteOrRewrite()
        {
            File.WriteAllBytes(destination, Original);
            files.FailAt = "commit";
            files.Error = new PlatformNotSupportedException("injected unsupported replace");
            Assert.Throws<PlatformNotSupportedException>(() => archive.SaveFile(path, new FileContent(Replacement)));
            CollectionAssert.AreEqual(Original, File.ReadAllBytes(destination));
            Assert.AreEqual(1, files.ReplaceCalls);
            Assert.AreEqual(0, files.MoveCalls);
            CollectionAssert.AreEqual(new[] { files.Temporary }, files.Deleted);
            AssertNoTemporaryFiles();
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void CleanupFailureRetainsBothErrorsAndTemporaryPath(bool existing, bool ioError)
        {
            if (existing) File.WriteAllBytes(destination, Original);
            files.FailAt = "write-partial";
            var writeError = new IOException("injected partial write");
            Exception cleanupError = ioError ? (Exception)new IOException("injected cleanup failure")
                : new UnauthorizedAccessException("injected cleanup denial");
            files.Error = writeError;
            files.CleanupError = cleanupError;
            var reported = Assert.Throws<KOSPersistenceException>(() => archive.SaveFile(path, new FileContent(Replacement)));
            Assert.IsTrue(reported.Message.Contains(files.Temporary));
            Assert.IsTrue(reported.Message.Contains(destination));
            var errors = (AggregateException)reported.InnerException;
            CollectionAssert.AreEqual(new Exception[] { writeError, cleanupError }, errors.InnerExceptions);
            AssertDestination(existing);
            Assert.IsTrue(files.StreamClosed);
            Assert.IsTrue(File.Exists(files.Temporary));
            Assert.AreEqual(1, files.Deleted.Count);
            Assert.AreEqual(0, files.MoveCalls + files.ReplaceCalls);
        }

        [Test]
        public void TemporaryNameCollisionIsNeitherTruncatedNorDeleted()
        {
            File.WriteAllBytes(destination, Original);
            files.Collide = true;
            Assert.Throws<IOException>(() => archive.SaveFile(path, new FileContent(Replacement)));
            Assert.AreEqual("unrelated temporary file", File.ReadAllText(files.Temporary));
            CollectionAssert.AreEqual(Original, File.ReadAllBytes(destination));
            Assert.AreEqual(0, files.Deleted.Count);
            Assert.AreEqual(0, files.MoveCalls + files.ReplaceCalls);
        }

        [Test]
        public void NewDestinationAppearingBeforeMoveIsNotOverwritten()
        {
            files.AfterExists = target => File.WriteAllBytes(target, Original);
            Assert.Throws<IOException>(() => archive.SaveFile(path, new FileContent(Replacement)));
            CollectionAssert.AreEqual(Original, File.ReadAllBytes(destination));
            Assert.AreEqual(1, files.MoveCalls);
            Assert.AreEqual(0, files.ReplaceCalls);
            AssertNoTemporaryFiles();
        }

        [Test]
        public void ExistingDestinationDisappearingBeforeReplaceIsNotRecreated()
        {
            File.WriteAllBytes(destination, Original);
            files.AfterExists = target => File.Delete(target);
            Assert.Throws<FileNotFoundException>(() => archive.SaveFile(path, new FileContent(Replacement)));
            Assert.IsFalse(File.Exists(destination));
            Assert.AreEqual(1, files.ReplaceCalls);
            Assert.AreEqual(0, files.MoveCalls);
            AssertNoTemporaryFiles();
        }

        [Test]
        public void DirectoryAppearingAtNewDestinationPreventsCommit()
        {
            files.AfterExists = target => Directory.CreateDirectory(target);
            Assert.Throws<IOException>(() => archive.SaveFile(path, new FileContent(Replacement)));
            Assert.IsTrue(Directory.Exists(destination));
            AssertNoTemporaryFiles();
        }

        [Test]
        public void DirectoryDestinationIsRejectedBeforeTemporaryCreation()
        {
            Directory.CreateDirectory(destination);
            string child = Path.Combine(destination, "keep.txt");
            File.WriteAllText(child, "keep");
            Assert.Throws<KOSPersistenceException>(() => archive.SaveFile(path, new FileContent(Replacement)));
            Assert.AreEqual("keep", File.ReadAllText(child));
            Assert.IsNull(files.Temporary);
        }

        [Test]
        public void RootDestinationIsRejectedWithoutTemporaryCreation()
        {
            Assert.Throws<KOSPersistenceException>(() => archive.SaveFile(VolumePath.EMPTY, new FileContent(Replacement)));
            Assert.IsNull(files.Temporary);
            Assert.AreEqual(0, Directory.GetFileSystemEntries(root).Length);
        }

        [Test]
        public void ParentFileFailurePreservesParentWithoutTemporaryCreation()
        {
            File.WriteAllBytes(destination, Original);
            var nested = path.Combine("child.ks");
            Assert.Throws<IOException>(() => archive.SaveFile(nested, new FileContent(Replacement)));
            CollectionAssert.AreEqual(Original, File.ReadAllBytes(destination));
            Assert.IsNull(files.Temporary);
        }

        [Test]
        public void NewNestedFileUsesItsOwnDirectoryForTemporary()
        {
            path = VolumePath.FromString("/one/two/nested.ks");
            destination = archive.GetArchivePath(path);
            archive.SaveFile(path, new FileContent(Replacement), false);
            Assert.AreEqual(Path.GetDirectoryName(destination), Path.GetDirectoryName(files.Temporary));
            CollectionAssert.AreEqual(Archive.ConvertToWindowsNewlines(Replacement), File.ReadAllBytes(destination));
            Assert.AreEqual(1, archive.Root.List().Count);
            AssertNoTemporaryFiles();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EmptyContentCreatesOrReplacesWithEmptyFile(bool existing)
        {
            if (existing) File.WriteAllBytes(destination, Original);
            archive.SaveFile(path, new FileContent());
            Assert.AreEqual(0, File.ReadAllBytes(destination).Length);
            AssertNoTemporaryFiles();
        }

        [TestCase("text")]
        [TestCase("ksm")]
        [TestCase("bytes")]
        public void ExistingByteAndTextConversionSemanticsAreRetained(string kind)
        {
            byte[] content;
            if (kind == "text") content = Encoding.UTF8.GetBytes("line\r\nnext\nłąż\n");
            else if (kind == "ksm") content = new byte[] { 0x6b, 0x03, 0x58, 0x45, 0xff, 0x0d, 0x0a };
            else content = new byte[] { 0xff, 0xfe, 0xfd, 0x00, 0x0d, 0x0a };
            byte[] expected = Archive.ConvertToWindowsNewlines(content);
            VolumeFile result = archive.SaveFile(path, new FileContent(content));
            CollectionAssert.AreEqual(expected, File.ReadAllBytes(destination));
            CollectionAssert.AreEqual(Archive.ConvertFromWindowsNewlines(expected), result.ReadAll().Bytes);
            AssertNoTemporaryFiles();
        }

        [Test]
        public void ExtensionlessSaveStillWritesTheExactRequestedDestination()
        {
            path = VolumePath.FromString("/program");
            File.WriteAllText(Path.Combine(root, "program.ks"), "keep script");
            VolumeFile result = archive.SaveFile(path, new FileContent("exact name"));
            Assert.AreEqual(path, result.Path);
            Assert.AreEqual("exact name", File.ReadAllText(Path.Combine(root, "program")));
            Assert.AreEqual("keep script", File.ReadAllText(Path.Combine(root, "program.ks")));
            Assert.AreEqual(2, archive.Root.List().Count);
        }

        [Test]
        public void LongDestinationNameDoesNotLengthenTemporaryBasename()
        {
            path = VolumePath.FromString("/" + new string('a', 240) + ".ks");
            destination = archive.GetArchivePath(path);
            archive.SaveFile(path, new FileContent(Replacement));
            Assert.IsTrue(Path.GetFileName(files.Temporary).Length < 60);
            Assert.IsTrue(File.Exists(destination));
            AssertNoTemporaryFiles();
        }

        [Test]
        public void ConsecutiveSavesLeaveNoTemporaryEntriesAndRefreshOldFileHandles()
        {
            File.WriteAllBytes(destination, Original);
            VolumeFile old = (VolumeFile)archive.Open(path);
            archive.SaveFile(path, new FileContent("first"));
            string temporary = files.Temporary;
            archive.SaveFile(path, new FileContent("second longer"));
            Assert.AreNotEqual(temporary, files.Temporary);
            Assert.AreEqual("second longer", old.ReadAll().String);
            Assert.AreEqual(13, old.Size);
            Assert.AreEqual(1, archive.Root.List().Count);
            AssertNoTemporaryFiles();
        }

        [Test]
        public void UnrelatedOrAbandonedTemporaryFilesAreNotSwept()
        {
            string abandoned = Path.Combine(root, ".kosim-save-abandoned.tmp");
            File.WriteAllText(abandoned, "manual recovery data");
            archive.SaveFile(path, new FileContent(Replacement));
            Assert.AreEqual("manual recovery data", File.ReadAllText(abandoned));
            Assert.AreEqual(1, Directory.GetFiles(root, ".kosim-save-*.tmp").Length);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InterruptedPreparationDoesNotCommit(bool existing)
        {
            if (existing) File.WriteAllBytes(destination, Original);
            var interrupted = new OperationCanceledException("injected preparation interruption, not a process kill");
            files.DuringWrite = () => { throw interrupted; };
            Assert.AreSame(interrupted, Assert.Throws<OperationCanceledException>(() => archive.SaveFile(path, new FileContent(Replacement))));
            AssertDestination(existing);
            Assert.AreEqual(0, files.MoveCalls + files.ReplaceCalls);
            Assert.IsTrue(files.StreamClosed);
            AssertNoTemporaryFiles();
        }

        [Test]
        public void TemporaryStreamIsExclusivelyOpenedUntilPreparationEnds()
        {
            files.DuringWrite = () => Assert.Throws<IOException>(() =>
            {
                using (File.Open(files.Temporary, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) { }
            });
            archive.SaveFile(path, new FileContent(Replacement));
            AssertNoTemporaryFiles();
        }

        [Test]
        public void SimulatedSharingViolationPreservesOriginalAndErrorCode()
        {
            File.WriteAllBytes(destination, Original);
            files.FailAt = "commit";
            var locked = new IOException("injected sharing violation, not a Proton lock test", unchecked((int)0x80070020));
            files.Error = locked;
            Assert.AreSame(locked, Assert.Throws<IOException>(() => archive.SaveFile(path, new FileContent(Replacement))));
            CollectionAssert.AreEqual(Original, File.ReadAllBytes(destination));
            Assert.AreEqual(0, files.MoveCalls);
            AssertNoTemporaryFiles();
        }

        [Test]
        public void FailedSaveCanBeRetriedWithANewTemporaryName()
        {
            File.WriteAllBytes(destination, Original);
            files.FailAt = "commit";
            Assert.Throws<IOException>(() => archive.SaveFile(path, new FileContent(Replacement)));
            string failedTemporary = files.Temporary;
            files.FailAt = null;
            archive.SaveFile(path, new FileContent(Replacement));
            Assert.AreNotEqual(failedTemporary, files.Temporary);
            CollectionAssert.AreEqual(Archive.ConvertToWindowsNewlines(Replacement), File.ReadAllBytes(destination));
            AssertNoTemporaryFiles();
        }

        [Test]
        public void ContentPreparationFailureDoesNotTouchDestination()
        {
            File.WriteAllBytes(destination, Original);
            Assert.Throws<NullReferenceException>(() => archive.SaveFile(path, null));
            CollectionAssert.AreEqual(Original, File.ReadAllBytes(destination));
            Assert.IsNull(files.Temporary);
        }

        [Test]
        public void PostCommitOpenFailureDoesNotPretendTheCommitWasRolledBack()
        {
            File.WriteAllBytes(destination, Original);
            var failing = new OpenFailureArchive(root);
            Assert.Throws<KOSPersistenceException>(() => failing.SaveFile(path, new FileContent(Replacement)));
            CollectionAssert.AreEqual(Archive.ConvertToWindowsNewlines(Replacement), File.ReadAllBytes(destination));
            AssertNoTemporaryFiles();
        }

        [Test]
        public void FailedArchiveWritePreservesEditorDraftAndBothFiles()
        {
            File.WriteAllBytes(destination, Original);
            var other = GlobalPath.FromString("0:/other.ks");
            File.WriteAllText(archive.GetArchivePath(other), "other");
            var document = new EditorDocument();
            var active = GlobalPath.FromVolumePath(path, 0);
            document.RequestOpen(archive, active);
            document.Contents = "edited draft";
            document.RequestOpen(archive, other);
            files.FailAt = "write-partial";
            Assert.Throws<IOException>(() => document.SaveThenOpen());
            CollectionAssert.AreEqual(Original, File.ReadAllBytes(destination));
            Assert.AreEqual("other", File.ReadAllText(archive.GetArchivePath(other)));
            Assert.AreEqual("edited draft", document.Contents);
            Assert.IsTrue(document.IsDirty);
            Assert.AreEqual(active, document.ActivePath);
            Assert.AreEqual(other, document.PendingPath);
            AssertNoTemporaryFiles();
            files.FailAt = null;
            document.SaveThenOpen();
            Assert.AreEqual("edited draft", File.ReadAllText(destination));
            Assert.AreEqual("other", document.Contents);
            Assert.IsFalse(document.IsDirty);
        }

        private sealed class OpenFailureArchive : Archive
        {
            internal OpenFailureArchive(string directory) : base(directory) { }
            public override VolumeItem Open(VolumePath path, bool ksmDefault = false)
            {
                throw new KOSPersistenceException("injected post-commit Open failure");
            }
        }

        // Delegates actual filesystem work to the production adapter; injected faults
        // occur before commit or in a stream around a real exclusively opened file.
        private sealed class FaultOperations : ArchiveFileOperations
        {
            internal string FailAt;
            internal Exception Error = new IOException("injected I/O error");
            internal Exception CleanupError;
            internal string Temporary;
            internal FileMode OpenMode;
            internal bool StreamClosed;
            internal bool Collide;
            internal int ReplaceCalls;
            internal int MoveCalls;
            internal readonly List<string> Deleted = new List<string>();
            internal Action DuringWrite;
            internal Action<string, string> BeforeCommit;
            internal Action<string> AfterExists;

            internal override Stream OpenWrite(string path, FileMode mode)
            {
                Temporary = path;
                OpenMode = mode;
                StreamClosed = false;
                if (FailAt == "create") throw Error;
                if (Collide) File.WriteAllText(path, "unrelated temporary file");
                return new FaultStream(base.OpenWrite(path, mode), this);
            }

            internal override bool Exists(string path)
            {
                if (FailAt == "exists") throw Error;
                bool exists = base.Exists(path);
                if (AfterExists != null) AfterExists(path);
                return exists;
            }

            internal override void Replace(string source, string destination)
            {
                ReplaceCalls++;
                if (BeforeCommit != null) BeforeCommit(source, destination);
                if (FailAt == "commit") throw Error;
                base.Replace(source, destination);
            }

            internal override void Move(string source, string destination)
            {
                MoveCalls++;
                if (BeforeCommit != null) BeforeCommit(source, destination);
                if (FailAt == "commit") throw Error;
                base.Move(source, destination);
            }

            internal override void Delete(string path)
            {
                Deleted.Add(path);
                if (CleanupError != null) throw CleanupError;
                base.Delete(path);
            }
        }

        private sealed class FaultStream : Stream
        {
            private readonly Stream inner;
            private readonly FaultOperations files;
            internal FaultStream(Stream inner, FaultOperations files) { this.inner = inner; this.files = files; }
            public override bool CanRead { get { return inner.CanRead; } }
            public override bool CanSeek { get { return inner.CanSeek; } }
            public override bool CanWrite { get { return inner.CanWrite; } }
            public override long Length { get { return inner.Length; } }
            public override long Position { get { return inner.Position; } set { inner.Position = value; } }
            public override int Read(byte[] buffer, int offset, int count) { return inner.Read(buffer, offset, count); }
            public override long Seek(long offset, SeekOrigin origin) { return inner.Seek(offset, origin); }
            public override void SetLength(long length) { inner.SetLength(length); }
            public override void Flush()
            {
                if (files.FailAt == "flush") throw files.Error;
                inner.Flush();
            }
            public override void Write(byte[] buffer, int offset, int count)
            {
                if (files.FailAt == "write-start") throw files.Error;
                inner.Write(buffer, offset, files.FailAt == "write-partial" ? Math.Min(4, count) : count);
                if (files.DuringWrite != null) files.DuringWrite();
                if (files.FailAt == "write-partial") throw files.Error;
            }
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    inner.Dispose();
                    files.StreamClosed = true;
                    if (files.FailAt == "close") throw files.Error;
                }
                base.Dispose(disposing);
            }
        }
    }
}
