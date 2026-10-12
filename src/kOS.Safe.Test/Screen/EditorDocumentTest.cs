using System;
using System.IO;
using kOS.Safe.Exceptions;
using kOS.Safe.Persistence;
using kOS.Safe.Screen;
using kOS.Safe.Utilities;
using NUnit.Framework;

namespace kOS.Safe.Test
{
    [TestFixture]
    public class EditorDocumentTest
    {
        private EditorDocument document;
        private Harddisk a;
        private Harddisk b;
        private GlobalPath pathA;
        private GlobalPath pathB;
        private string archiveDirectory;

        [SetUp]
        public void SetUp()
        {
            SafeHouse.Logger = new TestLogger();
            a = new Harddisk(5000);
            b = new Harddisk(5000);
            pathA = GlobalPath.FromString("1:/a.ks");
            pathB = GlobalPath.FromString("2:/b.ks");
            Write(a, pathA, "100");
            Write(b, pathB, "BBB");
            document = new EditorDocument();
            Assert.IsTrue(document.RequestOpen(a, pathA));
        }

        [TearDown]
        public void TearDown()
        {
            if (archiveDirectory != null) Directory.Delete(archiveDirectory, true);
            archiveDirectory = null;
        }

        private static void Write(Volume volume, VolumePath path, string text)
        {
            Assert.IsNotNull(volume.SaveFile(path, new FileContent(text)));
        }

        private static string Read(Volume volume, VolumePath path)
        {
            return ((VolumeFile)volume.Open(path)).ReadAll().String;
        }

        private void AssertActiveA(bool dirty)
        {
            Assert.AreSame(a, document.ActiveVolume);
            Assert.AreEqual(pathA, document.ActivePath);
            Assert.AreEqual(dirty ? "200" : "100", document.Contents);
            Assert.AreEqual(dirty, document.IsDirty);
        }

        [TestCase("200")]
        [TestCase("1000")]
        [TestCase("10")]
        [TestCase("")]
        [TestCase("1\n0")]
        public void ActualChangesAreDirty(string text)
        {
            document.Contents = text;
            Assert.IsTrue(document.IsDirty);
            Assert.IsFalse(document.RequestOpen(b, pathB));
        }

        [Test]
        public void UnchangedTextIsClean()
        {
            document.Contents = "100";
            Assert.IsFalse(document.IsDirty);
        }

        [Test]
        public void UndoBackToSavedTextIsClean()
        {
            document.Contents = "200";
            document.Contents = "100";
            Assert.IsFalse(document.IsDirty);
            Assert.IsTrue(document.RequestOpen(b, pathB));
        }

        [Test]
        public void SuccessfulSaveMovesTheDirtyBaseline()
        {
            document.Contents = "200";
            document.Save();
            Assert.AreEqual("200", Read(a, pathA));
            Assert.IsFalse(document.IsDirty);
            document.Contents = "100";
            Assert.IsTrue(document.IsDirty);
            document.Contents = "200";
            Assert.IsFalse(document.IsDirty);
        }

        [Test]
        public void CleanSwitchLoadsImmediatelyWithoutWritingA()
        {
            Assert.IsTrue(document.RequestOpen(b, pathB));
            Assert.AreSame(b, document.ActiveVolume);
            Assert.AreEqual(pathB, document.ActivePath);
            Assert.AreEqual("BBB", document.Contents);
            Assert.IsFalse(document.IsDirty);
            Assert.IsFalse(document.HasPendingOpen);
            Assert.AreEqual("100", Read(a, pathA));
        }

        [TestCase(false, false, "save")]
        [TestCase(false, false, "discard")]
        [TestCase(false, false, "cancel")]
        [TestCase(true, false, "save")]
        [TestCase(true, false, "discard")]
        [TestCase(true, false, "cancel")]
        [TestCase(false, true, "save")]
        [TestCase(false, true, "discard")]
        [TestCase(false, true, "cancel")]
        public void CrossVolumeTransactionsKeepDestinationsSeparate(bool archiveA, bool archiveB, string choice)
        {
            archiveDirectory = Path.Combine(Path.GetTempPath(), "kosim-editor-" + Guid.NewGuid());
            Volume source = archiveA ? (Volume)new Archive(Path.Combine(archiveDirectory, "a")) : a;
            Volume target = archiveB ? (Volume)new Archive(Path.Combine(archiveDirectory, "b")) : b;
            Directory.CreateDirectory(archiveDirectory);
            Write(source, pathA, "100");
            Write(target, pathB, "BBB");
            document = new EditorDocument();
            document.RequestOpen(source, pathA);
            document.Contents = "200";
            Assert.IsFalse(document.RequestOpen(target, pathB));
            Assert.AreSame(source, document.ActiveVolume);
            Assert.AreSame(target, document.PendingVolume);
            Assert.AreEqual(pathA, document.ActivePath);
            Assert.AreEqual(pathB, document.PendingPath);
            Assert.AreEqual("200", document.Contents);

            if (choice == "save") document.SaveThenOpen();
            else if (choice == "discard") document.LoadPending();
            else document.CancelOpen();

            Assert.AreEqual(choice == "save" ? "200" : "100", Read(source, pathA));
            Assert.AreEqual("BBB", Read(target, pathB));
            Assert.IsNull(source.Open(pathB));
            Assert.IsNull(target.Open(pathA));
            Assert.IsFalse(document.HasPendingOpen);
            Assert.AreSame(choice == "cancel" ? source : target, document.ActiveVolume);
            Assert.AreEqual(choice == "cancel" ? pathA : pathB, document.ActivePath);
            Assert.AreEqual(choice == "cancel" ? "200" : "BBB", document.Contents);
            Assert.AreEqual(choice == "cancel", document.IsDirty);
            if (choice == "cancel")
            {
                document.Save();
                Assert.AreEqual("200", Read(source, pathA));
                Assert.AreEqual("BBB", Read(target, pathB));
            }
        }

        [TestCase(false, "save")]
        [TestCase(false, "discard")]
        [TestCase(false, "cancel")]
        [TestCase(true, "save")]
        [TestCase(true, "discard")]
        [TestCase(true, "cancel")]
        public void SameVolumeTransactionsDoNotOverwriteB(bool archive, string choice)
        {
            // Archive's root is static upstream: test one real archive, not two independent roots.
            Volume source = a;
            if (archive)
            {
                archiveDirectory = Path.Combine(Path.GetTempPath(), "kosim-editor-" + Guid.NewGuid());
                source = new Archive(archiveDirectory);
                Write(source, pathA, "100");
                document = new EditorDocument();
                document.RequestOpen(source, pathA);
            }
            Write(source, pathB, "BBB");
            document.Contents = "200";
            document.RequestOpen(source, pathB);
            if (choice == "save") document.SaveThenOpen();
            else if (choice == "discard") document.LoadPending();
            else document.CancelOpen();
            Assert.AreEqual(choice == "save" ? "200" : "100", Read(source, pathA));
            Assert.AreEqual("BBB", Read(source, pathB));
            Assert.AreEqual(choice == "cancel" ? pathA : pathB, document.ActivePath);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void MissingDestinationIsAnEmptyCleanNewDocument(bool save)
        {
            b.Delete(pathB);
            document.Contents = "200";
            document.RequestOpen(b, pathB);
            if (save) document.SaveThenOpen();
            else document.LoadPending();
            Assert.AreEqual(save ? "200" : "100", Read(a, pathA));
            Assert.AreEqual("", document.Contents);
            Assert.AreEqual(pathB, document.ActivePath);
            Assert.IsTrue(document.IsNewFile);
            Assert.IsFalse(document.IsDirty);
            Assert.IsNull(b.Open(pathB));
            document.Contents = "NEW";
            document.Save();
            Assert.AreEqual("NEW", Read(b, pathB));
            Assert.IsFalse(document.IsNewFile);
        }

        [Test]
        public void FullDiskSaveFailureKeepsDirtyDocumentAndRequestForRetry()
        {
            a = new Harddisk(6);
            Write(a, pathA, "100");
            Write(a, VolumePath.FromString("/filler.ks"), "xxx");
            document = new EditorDocument();
            document.RequestOpen(a, pathA);
            document.Contents = "2000";
            document.RequestOpen(b, pathB);
            Assert.Throws<KOSPersistenceException>(() => document.SaveThenOpen());
            Assert.AreEqual("100", Read(a, pathA));
            Assert.AreEqual("BBB", Read(b, pathB));
            Assert.AreEqual("2000", document.Contents);
            Assert.IsTrue(document.IsDirty);
            Assert.AreSame(a, document.ActiveVolume);
            Assert.AreEqual(pathA, document.ActivePath);
            Assert.AreEqual(pathB, document.PendingPath);
            a.Delete("/filler.ks"); // recover capacity without resolving/cancelling the request
            document.SaveThenOpen();
            Assert.AreEqual("2000", Read(a, pathA));
            Assert.AreEqual("BBB", document.Contents);
            Assert.IsFalse(document.HasPendingOpen);
        }

        [Test]
        public void ThrownSaveFailureCanRetryWithoutLosingThePendingDestination()
        {
            var failing = new FaultVolume(a);
            document = new EditorDocument();
            document.RequestOpen(failing, pathA);
            document.Contents = "200";
            document.RequestOpen(b, pathB);
            failing.SaveError = new IOException("injected save failure");
            Assert.Throws<IOException>(() => document.SaveThenOpen());
            Assert.AreEqual("200", document.Contents);
            Assert.IsTrue(document.IsDirty);
            Assert.AreSame(failing, document.ActiveVolume);
            Assert.AreEqual(pathA, document.ActivePath);
            Assert.AreEqual(pathB, document.PendingPath);
            Assert.AreEqual("100", Read(a, pathA));
            failing.SaveError = null;
            document.SaveThenOpen();
            Assert.AreEqual("200", Read(a, pathA));
            Assert.AreEqual("BBB", document.Contents);
            Assert.IsFalse(document.HasPendingOpen);
        }

        [Test]
        public void FailedSaveCanBeDiscardedToOpenB()
        {
            var failing = new FaultVolume(a) { SaveError = new UnauthorizedAccessException("denied") };
            document = new EditorDocument();
            document.RequestOpen(failing, pathA);
            document.Contents = "200";
            document.RequestOpen(b, pathB);
            Assert.Throws<UnauthorizedAccessException>(() => document.SaveThenOpen());
            document.LoadPending();
            Assert.AreEqual("100", Read(a, pathA));
            Assert.AreEqual("BBB", document.Contents);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DirectoryLoadFailurePreservesActiveState(bool saveFirst)
        {
            b.Delete(pathB);
            b.CreateDirectory(pathB);
            document.Contents = "200";
            document.RequestOpen(b, pathB);
            if (saveFirst) Assert.Throws<KOSPersistenceException>(() => document.SaveThenOpen());
            else Assert.Throws<KOSPersistenceException>(() => document.LoadPending());
            Assert.AreSame(a, document.ActiveVolume);
            Assert.AreEqual(pathA, document.ActivePath);
            Assert.AreEqual("200", document.Contents);
            Assert.AreEqual(!saveFirst, document.IsDirty);
            Assert.AreEqual(saveFirst ? "200" : "100", Read(a, pathA));
            Assert.AreEqual(pathB, document.PendingPath);
            document.CancelOpen();
            document.Save();
            Assert.AreEqual("200", Read(a, pathA));
        }

        [Test]
        public void FailedFileReadDoesNotReplaceActiveDocument()
        {
            var failing = new FaultVolume(b) { ReadError = new IOException("injected read failure") };
            document.Contents = "200";
            document.RequestOpen(failing, pathB);
            Assert.Throws<IOException>(() => document.LoadPending());
            AssertActiveA(true);
            Assert.AreSame(failing, document.PendingVolume);
            failing.ReadError = null;
            document.LoadPending();
            Assert.AreEqual("BBB", document.Contents);
        }

        [Test]
        public void CleanLoadFailureIsRecoverableWithoutLosingActiveDocument()
        {
            var failing = new FaultVolume(b) { OpenError = new IOException("unavailable") };
            Assert.Throws<IOException>(() => document.RequestOpen(failing, pathB));
            AssertActiveA(false);
            Assert.AreEqual(pathB, document.PendingPath);
            failing.OpenError = null;
            document.LoadPending();
            Assert.AreEqual("BBB", document.Contents);
        }

        [Test]
        public void ReloadDiscardReadsActiveDestination()
        {
            document.Contents = "200";
            Write(a, pathA, "300");
            Assert.IsFalse(document.RequestOpen(document.ActiveVolume, document.ActivePath));
            document.LoadPending();
            Assert.AreEqual("300", document.Contents);
            Assert.AreEqual(pathA, document.ActivePath);
            Assert.IsFalse(document.IsDirty);
        }

        [Test]
        public void ReloadCancelKeepsDraftAndSaveDestination()
        {
            document.Contents = "200";
            document.RequestOpen(document.ActiveVolume, document.ActivePath);
            document.CancelOpen();
            AssertActiveA(true);
            document.Save();
            Assert.AreEqual("200", Read(a, pathA));
        }

        [Test]
        public void AnotherRequestCannotReplaceUnresolvedTarget()
        {
            document.Contents = "200";
            document.RequestOpen(b, pathB);
            Assert.Throws<InvalidOperationException>(() => document.RequestOpen(a, pathA));
            AssertActiveA(true);
            Assert.AreSame(b, document.PendingVolume);
            Assert.AreEqual(pathB, document.PendingPath);
        }

        [Test]
        public void PendingTransactionPreventsEditingUntilCancelled()
        {
            document.Contents = "200";
            document.RequestOpen(b, pathB);
            Assert.Throws<InvalidOperationException>(() => document.Contents = "300");
            AssertActiveA(true);
            document.CancelOpen();
            document.Contents = "300";
            Assert.AreEqual("300", document.Contents);
        }

        [Test]
        public void ExplicitDiscardRestoresLastSuccessfulBaseline()
        {
            document.Contents = "200";
            document.Save();
            document.Contents = "300";
            document.RequestOpen(b, pathB);
            document.DiscardChanges();
            Assert.AreEqual("200", document.Contents);
            Assert.IsFalse(document.IsDirty);
            Assert.IsFalse(document.HasPendingOpen);
            Assert.AreEqual(pathA, document.ActivePath);
        }

        [Test]
        public void PathsCannotBeMutatedByCallersDuringTransaction()
        {
            pathA.Segments.Clear();
            document.ActivePath.Segments.Clear();
            Assert.AreEqual("1:/a.ks", document.ActivePath.ToString());
            document.Contents = "200";
            document.RequestOpen(b, pathB);
            pathB.Segments.Clear();
            document.PendingPath.Segments.Clear();
            Assert.AreEqual("2:/b.ks", document.PendingPath.ToString());
            document.SaveThenOpen();
            Assert.AreEqual("200", Read(a, VolumePath.FromString("/a.ks")));
            Assert.AreEqual("BBB", document.Contents);
        }

        [Test]
        public void ExtensionResolutionUsesActualOpenedFilePath()
        {
            var requested = GlobalPath.FromString("1:/a");
            document.RequestOpen(a, requested);
            Assert.AreEqual("1:/a.ks", document.ActivePath.ToString());
            document.Contents = "200";
            document.Save();
            Assert.AreEqual("200", Read(a, pathA));
            Assert.AreEqual(1, a.Root.List().Count);
        }

        [Test]
        public void CancelIsIdempotentAndDoesNotClearDirtyState()
        {
            document.Contents = "200";
            document.CancelOpen();
            document.CancelOpen();
            AssertActiveA(true);
        }

        [Test]
        public void FailedSaveCancelPreservesTheOriginalSaveDestination()
        {
            var failing = new FaultVolume(a) { SaveError = new IOException("unavailable") };
            document = new EditorDocument();
            document.RequestOpen(failing, pathA);
            document.Contents = "200";
            document.RequestOpen(b, pathB);
            Assert.Throws<IOException>(() => document.SaveThenOpen());
            document.CancelOpen();
            Assert.AreEqual(pathA, document.ActivePath);
            Assert.AreEqual("200", document.Contents);
            Assert.IsTrue(document.IsDirty);
            failing.SaveError = null;
            document.Save();
            Assert.AreEqual("200", Read(a, pathA));
            Assert.AreEqual("BBB", Read(b, pathB));
            Assert.IsNull(a.Open(pathB));
        }

        [Test]
        public void InitialOpenFailureCanRetryWithoutAnActiveDocument()
        {
            var failing = new FaultVolume(a) { OpenError = new IOException("unavailable") };
            document = new EditorDocument();
            Assert.Throws<IOException>(() => document.RequestOpen(failing, pathA));
            Assert.IsNull(document.ActiveVolume);
            Assert.IsNull(document.ActivePath);
            Assert.AreEqual("", document.Contents);
            Assert.IsFalse(document.IsDirty);
            Assert.AreEqual(pathA, document.PendingPath);
            failing.OpenError = null;
            document.LoadPending();
            Assert.AreEqual("100", document.Contents);
            Assert.AreEqual(pathA, document.ActivePath);
            Assert.IsFalse(document.HasPendingOpen);
        }

        [Test]
        public void InitiallyMissingFileIsNotCreatedUntilSaved()
        {
            document = new EditorDocument();
            var missing = GlobalPath.FromString("2:/new.ks");
            Assert.IsTrue(document.RequestOpen(b, missing));
            Assert.AreSame(b, document.ActiveVolume);
            Assert.AreEqual(missing, document.ActivePath);
            Assert.AreEqual("", document.Contents);
            Assert.IsFalse(document.IsDirty);
            Assert.IsTrue(document.IsNewFile);
            Assert.IsNull(b.Open(missing));
            document.Save();
            Assert.AreEqual("", Read(b, missing));
            Assert.IsFalse(document.IsNewFile);
        }

        [Test]
        public void FailedNewFileSaveKeepsNewDirtyDraft()
        {
            var full = new Harddisk(0);
            var missing = GlobalPath.FromString("3:/new.ks");
            document = new EditorDocument();
            document.RequestOpen(full, missing);
            document.Contents = "200";
            Assert.Throws<KOSPersistenceException>(() => document.Save());
            Assert.AreEqual("200", document.Contents);
            Assert.IsTrue(document.IsDirty);
            Assert.IsTrue(document.IsNewFile);
            Assert.AreEqual(missing, document.ActivePath);
            Assert.IsNull(full.Open(missing));
            document.DiscardChanges();
            Assert.AreEqual("", document.Contents);
            Assert.IsFalse(document.IsDirty);
        }

        [Test]
        public void SaveWithoutActiveDocumentIsRejected()
        {
            Assert.Throws<InvalidOperationException>(() => new EditorDocument().Save());
        }

        [Test]
        public void ResolveWithoutRequestDoesNotWriteOrChangeDocument()
        {
            document.Contents = "200";
            Assert.Throws<InvalidOperationException>(() => document.SaveThenOpen());
            Assert.Throws<InvalidOperationException>(() => document.LoadPending());
            AssertActiveA(true);
            Assert.AreEqual("100", Read(a, pathA));
        }

        [Test]
        public void InvalidRequestsDoNotChangeActiveOrPendingState()
        {
            Assert.Throws<ArgumentNullException>(() => document.RequestOpen(null, pathB));
            Assert.Throws<ArgumentNullException>(() => document.RequestOpen(b, null));
            AssertActiveA(false);
            Assert.IsFalse(document.HasPendingOpen);
        }

        // Real volume contents and behavior, with faults only at explicit I/O boundaries.
        private sealed class FaultVolume : Volume
        {
            private readonly Volume inner;
            public Exception SaveError;
            public Exception OpenError;
            public Exception ReadError;
            public FaultVolume(Volume inner) { this.inner = inner; }
            public override VolumeDirectory Root { get { return inner.Root; } }
            public override void Clear() { inner.Clear(); }
            public override VolumeItem Open(VolumePath path, bool ksmDefault = false)
            {
                if (OpenError != null) throw OpenError;
                VolumeItem item = inner.Open(path, ksmDefault);
                return ReadError != null && item is VolumeFile ? new FaultFile((VolumeFile)item, ReadError) : item;
            }
            public override VolumeDirectory CreateDirectory(VolumePath path) { return inner.CreateDirectory(path); }
            public override VolumeFile CreateFile(VolumePath path) { return inner.CreateFile(path); }
            public override bool Exists(VolumePath path, bool ksmDefault = false) { return inner.Exists(path, ksmDefault); }
            public override bool Delete(VolumePath path, bool ksmDefault = false) { return inner.Delete(path, ksmDefault); }
            public override VolumeFile SaveFile(VolumePath path, FileContent content, bool verifyFreeSpace = true)
            {
                if (SaveError != null) throw SaveError;
                return inner.SaveFile(path, content, verifyFreeSpace);
            }
        }

        private sealed class FaultFile : VolumeFile
        {
            private readonly Exception error;
            public FaultFile(VolumeFile file, Exception error) : base(file.Volume, file.Path) { this.error = error; }
            public override int Size { get { return 0; } }
            public override FileContent ReadAll() { throw error; }
            public override bool Write(byte[] content) { throw new NotSupportedException(); }
            public override void Clear() { throw new NotSupportedException(); }
        }
    }
}
