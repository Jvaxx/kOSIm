using System.IO;
using System.Text;
using kOS.Safe.Persistence;
using NUnit.Framework;

namespace kOS.Safe.Test.Execution
{
    /// <summary>
    /// <c>MOVEPATH</c> copies, then deletes the source. When the copy's destination is the source
    /// itself (the same path, its own directory, a volume alias, a default extension, or another
    /// case on a case-insensitive volume) upstream copied the file onto itself and then deleted
    /// it, returning True. The scripts run on the real prompt and CPU; the archive is volume 0, a
    /// disk named "data" volume 1.
    /// </summary>
    [TestFixture]
    public class MoveOntoItselfTest
    {
        private ScriptHost host;
        private Harddisk disk;

        [SetUp]
        public void SetUp()
        {
            host = new ScriptHost();
            disk = new Harddisk(10000);
            disk.Name = "data";
            disk.CreateFile(VolumePath.FromString("/x.txt")).WriteLn("disk x");
            disk.CreateDirectory(VolumePath.FromString("/d"));
            disk.CreateFile(VolumePath.FromString("/d/inner.txt")).WriteLn("disk inner");
            host.Volumes.Add(disk);
            host.WriteArchiveBytes("mv/a.txt", Encoding.UTF8.GetBytes("archive a\n"));
            host.WriteArchiveBytes("mv/p.ks", Encoding.UTF8.GetBytes("print 1.\n"));
            host.WriteArchiveBytes("mv/s/inner.txt", Encoding.UTF8.GetBytes("archive inner\n"));
            host.Boot();
        }

        [TearDown]
        public void TearDown()
        {
            host.Dispose();
        }

        private string ArchiveText(string relativePath)
        {
            string file = Path.Combine(host.ArchiveRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(file) ? File.ReadAllText(file) : null;
        }

        private string DiskText(string path)
        {
            var file = disk.Open(VolumePath.FromString(path)) as VolumeFile;
            return file == null ? null : file.ReadAll().String;
        }

        /// <summary>The command fails with the "onto itself" error.</summary>
        private void AssertRefused(string command)
        {
            host.Execute(command);
            Assert.AreEqual(1, host.Errors.Count, "errors for: " + command);
            StringAssert.Contains("onto itself", host.Errors[0].Message);
            host.Errors.Clear();
        }

        private void AssertArchiveIntact()
        {
            Assert.AreEqual("archive a\n", ArchiveText("mv/a.txt"));
            Assert.AreEqual("print 1.\n", ArchiveText("mv/p.ks"));
            Assert.AreEqual("archive inner\n", ArchiveText("mv/s/inner.txt"));
        }

        private void AssertDiskIntact()
        {
            Assert.AreEqual("disk x\n", DiskText("/x.txt"));
            Assert.AreEqual("disk inner\n", DiskText("/d/inner.txt"));
        }

        [Test]
        public void FileOntoItself()
        {
            AssertRefused("movepath(\"0:/mv/a.txt\", \"0:/mv/a.txt\").");
            AssertRefused("movepath(\"1:/x.txt\", \"1:/x.txt\").");
            AssertArchiveIntact();
            AssertDiskIntact();
        }

        [Test]
        public void FileIntoItsOwnDirectory()
        {
            AssertRefused("movepath(\"0:/mv/a.txt\", \"0:/mv\").");
            AssertRefused("movepath(\"0:/mv/a.txt\", \"0:/mv/\").");
            AssertRefused("movepath(\"1:/d/inner.txt\", \"1:/d\").");
            AssertRefused("movepath(\"1:/x.txt\", \"1:/\").");
            AssertArchiveIntact();
            AssertDiskIntact();
        }

        [Test]
        public void FileOntoItselfThroughAnAliasOrAnObject()
        {
            AssertRefused("movepath(\"archive:/mv/a.txt\", \"0:/mv/a.txt\").");
            AssertRefused("movepath(\"data:/x.txt\", \"1:/x.txt\").");
            AssertRefused("movepath(open(\"0:/mv/a.txt\"), open(\"0:/mv\")).");
            AssertRefused("movepath(open(\"1:/x.txt\"), path(\"1:\"):volume).");
            AssertRefused("cd(\"0:/mv\"). movepath(\"a.txt\", \"a.txt\").");
            AssertArchiveIntact();
            AssertDiskIntact();
        }

        [Test]
        public void FileFoundByItsDefaultExtension()
        {
            // "p" opens p.ks; moving it onto p.ks moved it onto itself.
            AssertRefused("movepath(\"0:/mv/p\", \"0:/mv/p.ks\").");
            AssertRefused("movepath(\"0:/mv/p\", \"0:/mv\").");
            AssertArchiveIntact();
        }

        [Test]
        public void OtherCaseOnTheCaseInsensitiveDisk()
        {
            // A disk ignores case: X.TXT is x.txt.
            AssertRefused("movepath(\"1:/x.txt\", \"1:/X.TXT\").");
            AssertRefused("movepath(\"1:/d/inner.txt\", \"1:/D\").");
            AssertDiskIntact();
        }

        [Test]
        public void DirectoryOntoItselfOrItsParent()
        {
            AssertRefused("movepath(\"0:/mv/s\", \"0:/mv\").");
            AssertRefused("movepath(\"1:/d\", \"1:/\").");
            AssertRefused("movepath(open(\"1:/d\"), path(\"1:\"):volume).");
            AssertArchiveIntact();
            AssertDiskIntact();
        }

        [Test]
        public void DirectoryIntoItsOwnSubdirectoryInAnotherCase()
        {
            // A disk matches a nested path's directories exactly, so this fails before copying
            // (upstream: "does not contain path"); what matters is that nothing is lost.
            host.Execute("movepath(\"1:/d\", \"1:/D/sub\").");
            Assert.AreEqual(1, host.Errors.Count);
            AssertDiskIntact();
            host.Errors.Clear();

            // "D" is d itself, so d would go to d/d (upstream: copied into itself, then deleted):
            host.Execute("movepath(\"1:/d\", \"1:/D\").");
            Assert.AreEqual(1, host.Errors.Count);
            StringAssert.Contains("subdirectory of itself", host.Errors[0].Message);
            AssertDiskIntact();
            host.Errors.Clear();

            // A directory moved into its own parent and the subdirectory name it already has
            // is caught by the "onto itself" check, not the subdirectory one:
            AssertRefused("movepath(\"1:/d\", \"1:/d/..\").");
            AssertDiskIntact();
        }

        [Test]
        public void OrdinaryMovesStillWork()
        {
            host.Execute("print movepath(\"0:/mv/a.txt\", \"0:/mv/b.txt\").");
            host.Execute("print movepath(\"0:/mv/b.txt\", \"0:/mv/s\").");
            host.Execute("print movepath(\"1:/d/inner.txt\", \"1:/\").");
            host.Execute("print movepath(\"1:/x.txt\", \"1:/d\").");
            host.Execute("print movepath(\"0:/mv/p\", \"0:/mv/q.ks\").");
            host.Execute("print movepath(\"1:/d\", \"0:/mv\").");
            host.AssertNoErrors();
            Assert.IsNull(ArchiveText("mv/a.txt"));
            Assert.IsNull(ArchiveText("mv/b.txt"));
            Assert.AreEqual("archive a\n", ArchiveText("mv/s/b.txt"));
            Assert.IsNull(ArchiveText("mv/p.ks"));
            Assert.AreEqual("print 1.\n", ArchiveText("mv/q.ks"));
            Assert.AreEqual("disk inner\n", DiskText("/inner.txt"));
            Assert.AreEqual("disk x\n", ArchiveText("mv/d/x.txt"));
            Assert.IsNull(disk.Open(VolumePath.FromString("/d")));
        }

        [Test]
        public void ArchiveNamesDifferingInCaseAreDifferentFilesHere()
        {
            // The headless suite runs on a case-sensitive file system: A.txt is another file,
            // so moving onto it, or renaming to it, is an ordinary move. (Under Proton the
            // archive ignores case like a disk; not checked here.)
            Assume.That(!File.Exists(Path.Combine(host.ArchiveRoot, "mv", "A.TXT")), "case-insensitive file system");
            host.Execute("print movepath(\"0:/mv/a.txt\", \"0:/mv/A.txt\").");
            host.AssertNoErrors();
            Assert.IsNull(ArchiveText("mv/a.txt"));
            Assert.AreEqual("archive a\n", ArchiveText("mv/A.txt"));

            host.WriteArchiveBytes("mv/b.txt", Encoding.UTF8.GetBytes("b\n"));
            host.WriteArchiveBytes("mv/B.txt", Encoding.UTF8.GetBytes("B\n"));
            host.Execute("print movepath(\"0:/mv/b.txt\", \"0:/mv/B.txt\").");
            host.AssertNoErrors();
            Assert.IsNull(ArchiveText("mv/b.txt"));
            Assert.AreEqual("b\n", ArchiveText("mv/B.txt"));
        }

        [Test]
        public void CopyOntoItselfIsUnchanged()
        {
            // COPYPATH onto itself rewrites the same bytes, as upstream; nothing is lost.
            host.Execute("print copypath(\"0:/mv/a.txt\", \"0:/mv/a.txt\").");
            host.Execute("print copypath(\"1:/x.txt\", \"1:/\").");
            host.AssertNoErrors();
            AssertArchiveIntact();
            AssertDiskIntact();
        }
    }
}
