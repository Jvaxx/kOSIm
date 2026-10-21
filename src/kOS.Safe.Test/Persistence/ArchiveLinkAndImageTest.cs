using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using kOS.Safe.Exceptions;
using kOS.Safe.Persistence;
using kOS.Safe.Utilities;
using NUnit.Framework;

namespace kOS.Safe.Test.Persistence
{
    /// <summary>Real symbolic links for archive tests (the headless suite runs on Linux/Mono).</summary>
    internal static class SymbolicLinks
    {
        [DllImport("libc", SetLastError = true)]
        private static extern int symlink(string target, string linkPath);

        public static void Create(string target, string linkPath)
        {
            if (symlink(target, linkPath) != 0)
                throw new IOException("symlink failed (" + Marshal.GetLastWin32Error() + "): " + linkPath + " -> " + target);
        }

        public static bool IsLink(string path)
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
    }

    /// <summary>
    /// IO-04 symlink policy (user decision 2026-10-15): links in the archive are followed to
    /// read, write and list; a delete never enters one. A whole-file save onto a file link
    /// replaces the link (the atomic save, IO-03); appends go through it. Real archive, real
    /// links to an "outside" directory beside it.
    /// </summary>
    [TestFixture]
    public class ArchiveLinkTest
    {
        private string baseDir, root, outside;
        private Archive archive;

        [SetUp]
        public void SetUp()
        {
            SafeHouse.Logger = new TestLogger();
            baseDir = Path.Combine(Path.GetTempPath(), "kosim-io04-" + Guid.NewGuid().ToString("N"));
            root = Path.Combine(baseDir, "Script");
            outside = Path.Combine(baseDir, "repo");
            Directory.CreateDirectory(Path.Combine(root, "dir"));
            Directory.CreateDirectory(Path.Combine(outside, "deep"));
            File.WriteAllText(Path.Combine(outside, "t.ks"), "outside\n");
            File.WriteAllText(Path.Combine(outside, "deep", "d.ks"), "deep\n");
            archive = new Archive(root);
        }

        [TearDown]
        public void TearDown()
        {
            LinkSafeDelete.linksCanBeDeletedOverride = null;
            LinkSafeDelete.unixPathOverride = null;
            // Links first, so the cleanup itself never depends on the code under test.
            foreach (string path in Directory.GetFileSystemEntries(baseDir, "*", SearchOption.AllDirectories)
                .Where(p => SymbolicLinks.IsLink(p)).OrderByDescending(p => p.Length))
            {
                File.Delete(path);
            }
            Directory.Delete(baseDir, true);
        }

        private void AssertOutsideIntact()
        {
            Assert.AreEqual("outside\n", File.ReadAllText(Path.Combine(outside, "t.ks")));
            Assert.AreEqual("deep\n", File.ReadAllText(Path.Combine(outside, "deep", "d.ks")));
        }

        [Test]
        public void LinkedDirectoryIsFollowedToReadListAndWrite()
        {
            SymbolicLinks.Create(outside, Path.Combine(root, "linked"));

            var directory = archive.Open(VolumePath.FromString("/linked")) as VolumeDirectory;
            Assert.IsNotNull(directory);
            Assert.IsTrue(directory.List().ContainsKey("t.ks"));
            Assert.IsTrue(directory.List().ContainsKey("deep"));
            Assert.AreEqual("outside\n", (archive.Open(VolumePath.FromString("/linked/t.ks")) as VolumeFile).ReadAll().String);

            archive.SaveFile(VolumePath.FromString("/linked/new.ks"), new FileContent("written\n"));
            Assert.AreEqual("written\n", File.ReadAllText(Path.Combine(outside, "new.ks")));
        }

        [Test]
        public void DeletingALinkedDirectoryRemovesOnlyTheLink()
        {
            string link = Path.Combine(root, "linked");
            SymbolicLinks.Create(outside, link);

            Assert.IsTrue(archive.Delete(VolumePath.FromString("/linked")));
            Assert.IsFalse(Directory.Exists(link) || File.Exists(link));
            AssertOutsideIntact();
        }

        [Test]
        public void DeletingADirectoryKeepsTheTargetsOfLinksInside()
        {
            SymbolicLinks.Create(outside, Path.Combine(root, "dir", "dirlink"));
            SymbolicLinks.Create(Path.Combine(outside, "t.ks"), Path.Combine(root, "dir", "filelink.ks"));
            SymbolicLinks.Create(Path.Combine(outside, "missing"), Path.Combine(root, "dir", "dangling"));
            File.WriteAllText(Path.Combine(root, "dir", "own.ks"), "own\n");

            Assert.IsTrue(archive.Delete(VolumePath.FromString("/dir")));
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "dir")));
            AssertOutsideIntact();
        }

        [Test]
        public void DeletingAFileLinkRemovesOnlyTheLink()
        {
            SymbolicLinks.Create(Path.Combine(outside, "t.ks"), Path.Combine(root, "filelink.ks"));

            Assert.IsTrue(archive.Delete(VolumePath.FromString("/filelink.ks")));
            Assert.IsFalse(File.Exists(Path.Combine(root, "filelink.ks")));
            AssertOutsideIntact();
        }

        // Under Wine (Proton) deleting a file link deleted its target and a directory link could
        // not be removed (2026-10-19): there, anything that is or contains a link is refused
        // before anything is deleted or copied. Simulated here through the override.

        [Test]
        public void UnderWineAFileLinkIsRefusedAndKept()
        {
            LinkSafeDelete.linksCanBeDeletedOverride = false;
            string link = Path.Combine(root, "filelink.ks");
            SymbolicLinks.Create(Path.Combine(outside, "t.ks"), link);

            var error = Assert.Throws<KOSPersistenceException>(() => archive.Delete(VolumePath.FromString("/filelink.ks")));
            StringAssert.Contains("is or contains a link", error.Message);
            Assert.IsTrue(SymbolicLinks.IsLink(link));
            AssertOutsideIntact();
        }

        [Test]
        public void UnderWineADirectoryWithALinkInsideIsRefusedWhole()
        {
            LinkSafeDelete.linksCanBeDeletedOverride = false;
            File.WriteAllText(Path.Combine(root, "dir", "a-own.ks"), "own\n");
            Directory.CreateDirectory(Path.Combine(root, "dir", "sub"));
            SymbolicLinks.Create(outside, Path.Combine(root, "dir", "sub", "dirlink"));
            File.WriteAllText(Path.Combine(root, "dir", "z-own.ks"), "own\n");

            Assert.Throws<KOSPersistenceException>(() => archive.Delete(VolumePath.FromString("/dir")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "dir", "a-own.ks")), "nothing deleted before the refusal");
            Assert.IsTrue(File.Exists(Path.Combine(root, "dir", "z-own.ks")));
            Assert.IsTrue(SymbolicLinks.IsLink(Path.Combine(root, "dir", "sub", "dirlink")));
            AssertOutsideIntact();
        }

        [Test]
        public void UnderWineMovingALinkIsRefusedBeforeCopying()
        {
            LinkSafeDelete.linksCanBeDeletedOverride = false;
            SymbolicLinks.Create(Path.Combine(outside, "t.ks"), Path.Combine(root, "filelink.ks"));
            var manager = new VolumeManager();
            manager.Add(archive);

            Assert.Throws<KOSPersistenceException>(() =>
                manager.Move(GlobalPath.FromString("0:/filelink.ks"), GlobalPath.FromString("0:/moved.ks")));
            Assert.IsFalse(File.Exists(Path.Combine(root, "moved.ks")), "nothing copied");
            Assert.IsTrue(SymbolicLinks.IsLink(Path.Combine(root, "filelink.ks")));
            AssertOutsideIntact();
        }

        [Test]
        public void UnderWineEntriesWithoutLinksAreStillDeletedAndMoved()
        {
            LinkSafeDelete.linksCanBeDeletedOverride = false;
            File.WriteAllText(Path.Combine(root, "dir", "own.ks"), "own\n");
            File.WriteAllText(Path.Combine(root, "plain.ks"), "plain\n");
            var manager = new VolumeManager();
            manager.Add(archive);

            Assert.IsTrue(manager.Move(GlobalPath.FromString("0:/plain.ks"), GlobalPath.FromString("0:/dir/plain.ks")));
            Assert.IsTrue(archive.Delete(VolumePath.FromString("/dir")));
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "dir")));
        }

        [Test]
        public void MovingADirectoryCopiesThroughALinkAndKeepsItsTarget()
        {
            SymbolicLinks.Create(outside, Path.Combine(root, "dir", "dirlink"));
            var manager = new VolumeManager();
            manager.Add(archive);

            Assert.IsTrue(manager.Move(GlobalPath.FromString("0:/dir"), GlobalPath.FromString("0:/moved")));
            Assert.AreEqual("outside\n", File.ReadAllText(Path.Combine(root, "moved", "dirlink", "t.ks")));
            Assert.IsFalse(SymbolicLinks.IsLink(Path.Combine(root, "moved", "dirlink")), "the copy is a real directory");
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "dir")));
            AssertOutsideIntact();
        }

        [Test]
        public void ClearKeepsLinkTargetsAndTheFolder()
        {
            SymbolicLinks.Create(outside, Path.Combine(root, "dir", "dirlink"));
            SymbolicLinks.Create(outside, Path.Combine(root, "toplink"));
            File.WriteAllText(Path.Combine(root, "own.ks"), "own\n");

            archive.Clear();
            Assert.IsTrue(Directory.Exists(root));
            Assert.IsEmpty(Directory.GetFileSystemEntries(root));
            AssertOutsideIntact();
        }

        [Test]
        public void ClearOfALinkedArchiveKeepsTheLink()
        {
            // The whole archive folder linked to the player's scripts.
            string linkedRoot = Path.Combine(baseDir, "LinkedScript");
            SymbolicLinks.Create(outside, linkedRoot);
            var linkedArchive = new Archive(linkedRoot);
            string other = Path.Combine(baseDir, "other");
            Directory.CreateDirectory(other);
            File.WriteAllText(Path.Combine(other, "o.ks"), "other\n");
            SymbolicLinks.Create(other, Path.Combine(outside, "innerlink"));

            linkedArchive.Clear();
            Assert.IsTrue(SymbolicLinks.IsLink(linkedRoot));
            Assert.IsEmpty(Directory.GetFileSystemEntries(outside), "the archive's own entries are cleared");
            Assert.AreEqual("other\n", File.ReadAllText(Path.Combine(other, "o.ks")), "a link inside was entered");
        }

        [Test]
        public void SavingOverAFileLinkReplacesTheLinkOnHostMono()
        {
            // Recorded behavior, not a policy choice: the atomic save (IO-03) replaces the
            // link with a regular file and leaves its target unchanged (Linux Mono; Proton
            // not checked). Following a file link would need its target, which net48 cannot read.
            string link = Path.Combine(root, "filelink.ks");
            SymbolicLinks.Create(Path.Combine(outside, "t.ks"), link);

            archive.SaveFile(VolumePath.FromString("/filelink.ks"), new FileContent("saved\n"));
            Assert.IsFalse(SymbolicLinks.IsLink(link));
            Assert.AreEqual("saved\n", File.ReadAllText(link));
            AssertOutsideIntact();
        }

        [Test]
        public void AppendingToAFileLinkWritesItsTarget()
        {
            // Recorded behavior (Linux Mono; Proton not checked): an append (WRITE, WRITELN,
            // LOG, WRITERAW) and CLEAR open the file in place, so they go through a file link
            // to its target and keep the link; only a whole-file save replaces it (above).
            string target = Path.Combine(outside, "t.ks");
            string link = Path.Combine(root, "filelink.ks");
            SymbolicLinks.Create(target, link);
            var file = (VolumeFile)archive.Open(VolumePath.FromString("/filelink.ks"));

            Assert.IsTrue(file.WriteLn("appended"));
            Assert.IsTrue(file.WriteRaw(Encoding.UTF8.GetBytes("raw\n")));
            Assert.IsTrue(SymbolicLinks.IsLink(link));
            Assert.AreEqual("outside\nappended\nraw\n", File.ReadAllText(target));

            file.Clear();
            Assert.IsTrue(SymbolicLinks.IsLink(link));
            Assert.AreEqual("", File.ReadAllText(target));
        }

        // Review P-11: under Wine each entry's check maps its path to Unix (a P/Invoke), and a
        // delete checked the tree, then walked it again checking each entry. Simulated here: the
        // override stands in for Wine's mapping, the identity except for the "links" given.

        private int unixPathCalls;

        private void SimulateWine(Dictionary<string, string> fileLinks)
        {
            LinkSafeDelete.linksCanBeDeletedOverride = false;
            unixPathCalls = 0; // one fixture object runs every test
            LinkSafeDelete.unixPathOverride = path =>
            {
                ++unixPathCalls;
                string target;
                return fileLinks != null && fileLinks.TryGetValue(path, out target) ? target : path;
            };
        }

        private string MakeTree()
        {
            string dir = Path.Combine(root, "dir");
            File.WriteAllText(Path.Combine(dir, "a.ks"), "a\n");
            File.WriteAllText(Path.Combine(dir, "b.ks"), "b\n");
            Directory.CreateDirectory(Path.Combine(dir, "sub", "deeper"));
            File.WriteAllText(Path.Combine(dir, "sub", "c.ks"), "c\n");
            File.WriteAllText(Path.Combine(dir, "sub", "deeper", "d.ks"), "d\n");
            return dir; // 7 entries: dir, a, b, sub, c, deeper, d
        }

        [Test]
        public void UnderWineADeleteWalksTheTreeOnceAndMapsEachPathOnce()
        {
            string dir = MakeTree();
            SimulateWine(null);

            Assert.IsTrue(archive.Delete(VolumePath.FromString("/dir")));
            Assert.IsFalse(Directory.Exists(dir));
            Assert.AreEqual(7 + 1, unixPathCalls, "each entry, and the parent of the first (was 4 per entry: 28)");
        }

        [Test]
        public void UnderWineAMoveChecksBeforeCopyingAndDeletesInOneMoreWalk()
        {
            string dir = MakeTree();
            SimulateWine(null);
            var manager = new VolumeManager();
            manager.Add(archive);

            Assert.IsTrue(manager.Move(GlobalPath.FromString("0:/dir"), GlobalPath.FromString("0:/moved")));
            Assert.IsFalse(Directory.Exists(dir));
            Assert.AreEqual("d\n", File.ReadAllText(Path.Combine(root, "moved", "sub", "deeper", "d.ks")));
            Assert.AreEqual(2 * (7 + 1), unixPathCalls, "the check before the copy, then the delete (was 6 per entry: 42)");
        }

        [Test]
        public void UnderWineAFileLinkShownAsItsTargetIsFoundAndKept()
        {
            // Wine shows a file link as the file it points to; only its Unix path differs.
            string dir = MakeTree();
            string link = Path.Combine(dir, "sub", "c.ks");
            SimulateWine(new Dictionary<string, string> { { link, "/elsewhere/c.ks" } });

            var error = Assert.Throws<KOSPersistenceException>(() => archive.Delete(VolumePath.FromString("/dir")));
            StringAssert.Contains("is or contains a link", error.Message);
            foreach (string kept in new[] { "a.ks", "b.ks", Path.Combine("sub", "c.ks"), Path.Combine("sub", "deeper", "d.ks") })
                Assert.IsTrue(File.Exists(Path.Combine(dir, kept)), "nothing deleted: " + kept);
            Assert.AreEqual(link, LinkSafeDelete.FirstLinkIn(dir));
            Assert.Throws<KOSPersistenceException>(() => archive.Delete(VolumePath.FromString("/dir/sub/c.ks")), "the link alone");
            Assert.IsTrue(File.Exists(link));
        }

        [Test]
        public void UnderWineAPathThatCannotBeMappedIsRefused()
        {
            string dir = MakeTree();
            SimulateWine(new Dictionary<string, string> { { Path.Combine(dir, "sub"), null } });

            Assert.Throws<KOSPersistenceException>(() => archive.Delete(VolumePath.FromString("/dir")));
            Assert.IsTrue(File.Exists(Path.Combine(dir, "sub", "deeper", "d.ks")));
            Assert.IsTrue(File.Exists(Path.Combine(dir, "a.ks")));
        }

        [Test]
        public void UnderWineADanglingLinkInsideIsRefused()
        {
            string dir = MakeTree();
            SymbolicLinks.Create(Path.Combine(outside, "missing"), Path.Combine(dir, "sub", "dangling"));
            SimulateWine(null);

            Assert.Throws<KOSPersistenceException>(() => archive.Delete(VolumePath.FromString("/dir")));
            Assert.IsTrue(File.Exists(Path.Combine(dir, "a.ks")), "nothing deleted");
            Assert.IsTrue(SymbolicLinks.IsLink(Path.Combine(dir, "sub", "dangling")));
        }
    }

    /// <summary>IO-04 image limits for script GUI images: 16 MiB files, 4096 pixels a side.</summary>
    [TestFixture]
    public class ArchiveImageLimitTest
    {
        private string dir;

        [SetUp]
        public void SetUp()
        {
            dir = Path.Combine(Path.GetTempPath(), "kosim-io04-img-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(dir, true);
        }

        private string Write(string name, byte[] bytes)
        {
            string path = Path.Combine(dir, name);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        private static byte[] Png(uint width, uint height)
        {
            var bytes = new byte[] {
                0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
                0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R',
                0, 0, 0, 0, 0, 0, 0, 0,
                8, 6, 0, 0, 0, 0, 0, 0, 0 };
            for (int i = 0; i < 4; ++i)
            {
                bytes[16 + i] = (byte)(width >> (24 - 8 * i));
                bytes[20 + i] = (byte)(height >> (24 - 8 * i));
            }
            return bytes;
        }

        /// <summary>SOI, APP0, a DHT whose bytes would read as a huge size, fill bytes, then the frame header.</summary>
        private static byte[] Jpeg(byte frameMarker, int width, int height)
        {
            return new byte[] {
                0xFF, 0xD8,
                0xFF, 0xE0, 0, 7, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0,
                0xFF, 0xC4, 0, 8, 0, 0xFF, 0xFF, 0xFF, 0xFF, 0,
                0xFF, 0xFF, frameMarker, 0, 11, 8, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 1, 1, 0x11, 0,
                0xFF, 0xDA, 0, 2,
                0xFF, 0xD9 };
        }

        private static void AssertRefused(string path, string fragment)
        {
            var e = Assert.Throws<KOSException>(() => ArchiveTextureFiles.ReadImage(path, "img/x.png"));
            StringAssert.Contains(fragment, e.Message);
            StringAssert.Contains("img/x.png", e.Message);
        }

        [Test]
        public void PngUpToTheLimitIsRead()
        {
            byte[] png = Png(4096, 4096);
            CollectionAssert.AreEqual(png, ArchiveTextureFiles.ReadImage(Write("a.png", png), "a.png"));
        }

        [Test]
        public void PngOverTheLimitIsRefused()
        {
            AssertRefused(Write("w.png", Png(4097, 1)), "4097x1 pixels, the limit is 4096x4096");
            AssertRefused(Write("h.png", Png(1, 4097)), "1x4097");
            AssertRefused(Write("u.png", Png(0xFFFFFFFF, 1)), "Image too large");
        }

        [Test]
        public void JpegFrameHeaderIsFoundPastOtherSegments()
        {
            int width, height;
            Assert.IsTrue(ArchiveTextureFiles.TryReadSize(Jpeg(0xC0, 640, 480), out width, out height));
            Assert.AreEqual(640, width);
            Assert.AreEqual(480, height);

            Assert.IsNotNull(ArchiveTextureFiles.ReadImage(Write("ok.jpg", Jpeg(0xC0, 4096, 4096)), "ok.jpg"));
            AssertRefused(Write("base.jpg", Jpeg(0xC0, 4097, 10)), "4097x10");
            AssertRefused(Write("progressive.jpg", Jpeg(0xC2, 10, 40000)), "10x40000");
        }

        [Test]
        public void JpegWithoutAFrameHeaderIsLeftToTheLoader()
        {
            byte[] noFrame = { 0xFF, 0xD8, 0xFF, 0xDA, 0, 2, 0xFF, 0xD9 };
            int width, height;
            Assert.IsFalse(ArchiveTextureFiles.TryReadSize(noFrame, out width, out height));
            Assert.IsFalse(ArchiveTextureFiles.TryReadSize(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0 }, out width, out height));
            Assert.IsFalse(ArchiveTextureFiles.TryReadSize(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0 }, out width, out height));
        }

        [Test]
        public void OtherContentIsReturnedUnchanged()
        {
            byte[] text = Encoding.ASCII.GetBytes("not an image");
            CollectionAssert.AreEqual(text, ArchiveTextureFiles.ReadImage(Write("t.png", text), "t.png"));
            byte[] truncated = Png(9999, 9999).Take(20).ToArray();
            CollectionAssert.AreEqual(truncated, ArchiveTextureFiles.ReadImage(Write("short.png", truncated), "short.png"));
            Assert.IsEmpty(ArchiveTextureFiles.ReadImage(Write("empty.png", new byte[0]), "empty.png"));
        }

        [Test]
        public void FileOverSixteenMebibytesIsRefusedBeforeReading()
        {
            string atLimit = Path.Combine(dir, "limit.png");
            string over = Path.Combine(dir, "over.png");
            using (var stream = File.Create(atLimit)) stream.SetLength(ArchiveTextureFiles.MaxFileBytes);
            using (var stream = File.Create(over)) stream.SetLength(ArchiveTextureFiles.MaxFileBytes + 1);

            Assert.AreEqual(ArchiveTextureFiles.MaxFileBytes, ArchiveTextureFiles.ReadImage(atLimit, "limit.png").Length);
            AssertRefused(over, "is 16777217 bytes, the limit is 16777216 bytes (16 MiB)");
        }
    }
}
