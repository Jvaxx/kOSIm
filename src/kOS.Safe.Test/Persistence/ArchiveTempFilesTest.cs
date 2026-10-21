using System;
using System.IO;
using kOS.Safe.Persistence;
using kOS.Safe.Utilities;
using NUnit.Framework;

namespace kOS.Safe.Test.Persistence
{
    /// <summary>
    /// Review L-03: a temporary file an atomic save (IO-03) left in the archive when the game
    /// stopped in the middle of it is deleted when its folder is next listed, once a minute old.
    /// </summary>
    [TestFixture]
    public class ArchiveTempFilesTest
    {
        private string baseDir, root;
        private Archive archive;

        private const string LEFTOVER = ".kosim-save-0123456789abcdef0123456789abcdef.tmp";

        [SetUp]
        public void SetUp()
        {
            SafeHouse.Logger = new TestLogger();
            baseDir = Path.Combine(Path.GetTempPath(), "kosim-l03-" + Guid.NewGuid().ToString("N"));
            root = Path.Combine(baseDir, "Script");
            Directory.CreateDirectory(Path.Combine(root, "dir"));
            archive = new Archive(root);
        }

        [TearDown]
        public void TearDown()
        {
            string link = Path.Combine(root, LEFTOVER);
            if (File.Exists(link) && SymbolicLinks.IsLink(link)) File.Delete(link);
            Directory.Delete(baseDir, true);
        }

        private string Write(string relativePath, TimeSpan age)
        {
            string path = Path.Combine(root, relativePath);
            File.WriteAllText(path, "partial save");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);
            return path;
        }

        [Test]
        public void OnlyTheSavesOwnNamesAreTemporary()
        {
            Assert.IsTrue(ArchiveTempFiles.IsTemporaryName(ArchiveTempFiles.NewName()));
            Assert.IsTrue(ArchiveTempFiles.IsTemporaryName(LEFTOVER));
            Assert.AreNotEqual(ArchiveTempFiles.NewName(), ArchiveTempFiles.NewName());
            foreach (string name in new[] {
                ".kosim-save-0123456789ABCDEF0123456789abcdef.tmp", // upper case: not Guid "N"
                ".kosim-save-0123456789abcdef0123456789abcde.tmp", // 31 digits
                ".kosim-save-0123456789abcdef0123456789abcdef0.tmp",
                ".kosim-save-0123456789abcdeg0123456789abcdef.tmp",
                "kosim-save-0123456789abcdef0123456789abcdef.tmp",
                ".kosim-save-0123456789abcdef0123456789abcdef.tmp.ks",
                ".kosim-save-0123456789abcdef0123456789abcdef.TMP", "", null })
                Assert.IsFalse(ArchiveTempFiles.IsTemporaryName(name), name ?? "null");
        }

        [Test]
        public void ListingDeletesOldLeftoversOnly()
        {
            string old = Write(LEFTOVER, TimeSpan.FromMinutes(2));
            string oldInDir = Write(Path.Combine("dir", ".kosim-save-fedcba9876543210fedcba9876543210.tmp"), TimeSpan.FromDays(3));
            string fresh = Write(".kosim-save-00000000000000000000000000000000.tmp", TimeSpan.FromSeconds(10));
            string lookalike = Write(".kosim-save-0123456789ABCDEF0123456789abcdef.tmp", TimeSpan.FromDays(3));
            string script = Write("launch.ks", TimeSpan.FromDays(3));

            var listing = archive.Root.List();
            Assert.IsFalse(File.Exists(old), "an old leftover");
            Assert.IsFalse(listing.ContainsKey(LEFTOVER));
            Assert.IsTrue(File.Exists(oldInDir), "only the folder listed is swept");
            Assert.IsTrue(File.Exists(fresh), "possibly a save in progress");
            Assert.IsTrue(File.Exists(lookalike), "not a name kOS makes");
            Assert.IsTrue(File.Exists(script));
            Assert.IsTrue(listing.ContainsKey("launch.ks"));
            Assert.IsTrue(listing.ContainsKey("dir"));

            ((VolumeDirectory)archive.Open(VolumePath.FromString("/dir"))).List();
            Assert.IsFalse(File.Exists(oldInDir), "swept when its own folder is listed");
        }

        [Test]
        public void ALinkWithATemporaryNameIsKept()
        {
            string target = Path.Combine(baseDir, "target.ks");
            File.WriteAllText(target, "kept\n");
            File.SetLastWriteTimeUtc(target, DateTime.UtcNow - TimeSpan.FromDays(3));
            string link = Path.Combine(root, LEFTOVER);
            SymbolicLinks.Create(target, link);

            // A day from now, whatever time the link itself has: only its being a link keeps it.
            Assert.IsFalse(ArchiveTempFiles.TryDeleteLeftover(link, DateTime.UtcNow + TimeSpan.FromDays(1)));
            archive.Root.List();
            Assert.IsTrue(SymbolicLinks.IsLink(link));
            Assert.AreEqual("kept\n", File.ReadAllText(target));
        }

        [Test]
        public void ASaveStillLeavesNoTemporaryFile()
        {
            archive.SaveFile(VolumePath.FromString("/saved.ks"), new FileContent("print 1.\n"));
            archive.SaveFile(VolumePath.FromString("/saved.ks"), new FileContent("print 2.\n"));
            foreach (string file in Directory.GetFiles(root))
                Assert.IsFalse(ArchiveTempFiles.IsTemporaryName(Path.GetFileName(file)), file);
        }
    }
}
