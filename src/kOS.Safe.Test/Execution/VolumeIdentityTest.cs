using System.Text;
using kOS.Safe.Exceptions;
using kOS.Safe.Persistence;
using kOS.Safe.Utilities;
using NUnit.Framework;

namespace kOS.Safe.Test.Execution
{
    /// <summary>
    /// IO-05: <c>COPYPATH</c>/<c>MOVEPATH</c> given a volume or a file/directory object turned
    /// it into "name:/path" and looked the name up again, so with two volumes of the same name
    /// the second one's object acted on the first. The scripts run on the real prompt and CPU;
    /// the archive is volume 0, two disks named "data" are volumes 1 and 2.
    /// </summary>
    [TestFixture]
    public class VolumeIdentityTest
    {
        private ScriptHost host;
        private Harddisk first, second;

        [SetUp]
        public void SetUp()
        {
            host = new ScriptHost();
            first = NewDisk("data", "first");
            second = NewDisk("data", "second");
            host.Volumes.Add(first);
            host.Volumes.Add(second);
            host.WriteArchiveBytes("io05/src.txt", Encoding.UTF8.GetBytes("archive\n"));
            host.Boot();
        }

        [TearDown]
        public void TearDown()
        {
            host.Dispose();
        }

        private static Harddisk NewDisk(string name, string text)
        {
            var disk = new Harddisk(10000);
            disk.Name = name;
            disk.CreateFile(VolumePath.FromString("/x.txt")).WriteLn(text);
            disk.CreateDirectory(VolumePath.FromString("/d"));
            disk.CreateFile(VolumePath.FromString("/d/inner.txt")).WriteLn(text + " inner");
            return disk;
        }

        private static string Text(Volume volume, string path)
        {
            var file = volume.Open(VolumePath.FromString(path)) as VolumeFile;
            return file == null ? null : file.ReadAll().String;
        }

        private string ArchiveText(string relativePath)
        {
            return Encoding.UTF8.GetString(host.ReadArchiveBytes(relativePath));
        }

        private void Run(string command)
        {
            host.Execute(command);
            host.AssertNoErrors();
        }

        [Test]
        public void FileObjectOfTheSecondVolumeIsCopied()
        {
            Run("copypath(open(\"2:/x.txt\"), \"0:/io05/out.txt\").");
            Assert.AreEqual("second\n", ArchiveText("io05/out.txt"));
        }

        [Test]
        public void DirectoryObjectOfTheSecondVolumeIsCopied()
        {
            Run("copypath(open(\"2:/d\"), \"0:/io05\").");
            Assert.AreEqual("second inner\n", ArchiveText("io05/d/inner.txt"));
        }

        [Test]
        public void VolumeObjectAsDestinationIsTheSecondVolume()
        {
            Run("copypath(\"0:/io05/src.txt\", path(\"2:\"):volume).");
            Assert.AreEqual("archive\n", Text(second, "/src.txt"));
            Assert.IsNull(Text(first, "/src.txt"));
        }

        [Test]
        public void VolumeObjectAsSourceIsTheSecondVolume()
        {
            Run("copypath(path(\"2:\"):volume, \"0:/io05/whole\").");
            Assert.AreEqual("second\n", ArchiveText("io05/whole/x.txt"));
            Assert.AreEqual("second inner\n", ArchiveText("io05/whole/d/inner.txt"));
        }

        [Test]
        public void FileObjectAsDestinationOverwritesThatFile()
        {
            Run("copypath(\"0:/io05/src.txt\", open(\"2:/x.txt\")).");
            Assert.AreEqual("archive\n", Text(second, "/x.txt"));
            Assert.AreEqual("first\n", Text(first, "/x.txt"));
        }

        [Test]
        public void DirectoryObjectAsDestinationReceivesTheFile()
        {
            Run("copypath(\"0:/io05/src.txt\", open(\"2:/d\")).");
            Assert.AreEqual("archive\n", Text(second, "/d/src.txt"));
            Assert.IsNull(Text(first, "/d/src.txt"));
        }

        [Test]
        public void SameNamedVolumesCopyDirectoryIntoTheOtherOnesSubdirectory()
        {
            // Not a copy into itself: the two "data:/d" are different disks.
            Run("copypath(open(\"2:/d\"), open(\"1:/d\")).");
            Assert.AreEqual("second inner\n", Text(first, "/d/d/inner.txt"));
            Assert.IsNull(second.Open(VolumePath.FromString("/d/d")));
        }

        [Test]
        public void FileObjectOfTheSecondVolumeIsMoved()
        {
            Run("movepath(open(\"2:/x.txt\"), \"0:/io05/moved.txt\").");
            Assert.AreEqual("second\n", ArchiveText("io05/moved.txt"));
            Assert.IsNull(Text(second, "/x.txt"), "the moved file is still on its disk");
            Assert.AreEqual("first\n", Text(first, "/x.txt"), "the other disk's file was moved");
        }

        [Test]
        public void MoveBetweenSameNamedVolumesUsesBothDisks()
        {
            Run("movepath(open(\"2:/x.txt\"), open(\"1:/d\")).");
            Assert.AreEqual("second\n", Text(first, "/d/x.txt"));
            Assert.IsNull(Text(second, "/x.txt"));
            Assert.AreEqual("first\n", Text(first, "/x.txt"));
        }

        [Test]
        public void StringPathsStillResolveByName()
        {
            // Unchanged: a name finds the first volume of that name; a number is exact.
            Run("copypath(\"data:/x.txt\", \"0:/io05/byname.txt\").");
            Run("copypath(\"2:/x.txt\", \"0:/io05/bynumber.txt\").");
            Assert.AreEqual("first\n", ArchiveText("io05/byname.txt"));
            Assert.AreEqual("second\n", ArchiveText("io05/bynumber.txt"));
        }

        [Test]
        public void VolumeNotInTheManagerFailsAsDestination()
        {
            Run("set gone to path(\"2:\"):volume.");
            host.Volumes.Remove(2);

            host.Execute("copypath(\"0:/io05/src.txt\", gone).");
            Assert.AreEqual(1, host.Errors.Count);
            Assert.IsInstanceOf<KOSPersistenceException>(host.Errors[0]);
            StringAssert.Contains("Volume not found", host.Errors[0].Message);
            Assert.IsNull(Text(first, "/src.txt"), "the same-named disk was written");
            Assert.IsNull(Text(second, "/src.txt"));
        }

        [Test]
        public void FileOnAVolumeNotInTheManagerIsNotMoved()
        {
            Run("set gone to open(\"2:/x.txt\").");
            host.Volumes.Remove(2);

            host.Execute("movepath(gone, \"0:/io05/moved.txt\").");
            Assert.AreEqual(1, host.Errors.Count);
            StringAssert.Contains("Volume not found", host.Errors[0].Message);
            Assert.AreEqual("second\n", Text(second, "/x.txt"));
            Assert.AreEqual("first\n", Text(first, "/x.txt"));
            Assert.IsFalse(System.IO.File.Exists(System.IO.Path.Combine(host.ArchiveRoot, "io05", "moved.txt")));
        }

        [Test]
        public void VolumeObjectSurvivesARenumbering()
        {
            // Docking/undocking re-adds a disk under a new number; the object still names it.
            Run("set disk to path(\"2:\"):volume.");
            host.Volumes.Remove(2);
            host.Volumes.Add(second);
            Assert.AreEqual(3, host.Volumes.GetVolumeId(second));

            Run("copypath(\"0:/io05/src.txt\", disk).");
            Assert.AreEqual("archive\n", Text(second, "/src.txt"));
            Assert.IsNull(Text(first, "/src.txt"));
        }

        [Test]
        public void DirectoryCannotBeCopiedIntoItselfThroughAnObject()
        {
            host.Execute("copypath(open(\"2:/d\"), \"2:/d/sub\").");
            Assert.AreEqual(1, host.Errors.Count);
            StringAssert.Contains("subdirectory of itself", host.Errors[0].Message);
        }
        [Test]
        public void DeletePathOfAFileObjectDeletesOnItsDisk()
        {
            Run("deletepath(open(\"2:/x.txt\")).");
            Assert.IsNull(Text(second, "/x.txt"));
            Assert.AreEqual("first\n", Text(first, "/x.txt"));
        }

        [Test]
        public void DeletePathOnADetachedDiskDeletesNothing()
        {
            Run("set gone to open(\"2:/x.txt\").");
            host.Volumes.Remove(2);

            host.Execute("deletepath(gone).");
            Assert.AreEqual(1, host.Errors.Count);
            StringAssert.Contains("Volume not found", host.Errors[0].Message);
            Assert.AreEqual("first\n", Text(first, "/x.txt"));
            Assert.AreEqual("second\n", Text(second, "/x.txt"));
        }

        [Test]
        public void OpenAndExistsOfAVolumeObjectUseThatVolume()
        {
            second.CreateFile(VolumePath.FromString("/only2.txt")).WriteLn("2");
            Assert.AreEqual("True", host.Output("print open(path(\"2:\"):volume):list:haskey(\"only2.txt\")."));
            Assert.AreEqual("True", host.Output("print exists(open(\"2:/only2.txt\"))."));
            host.AssertNoErrors();
        }

        [Test]
        public void JsonThroughAFileObjectUsesItsDisk()
        {
            Run("writejson(list(2), open(\"2:/x.txt\")).");
            StringAssert.Contains("2", Text(second, "/x.txt"));
            Assert.AreEqual("first\n", Text(first, "/x.txt"));
            Assert.AreEqual("2", host.Output("print readjson(open(\"2:/x.txt\"))[0]."));
            host.AssertNoErrors();
        }

        [Test]
        public void CdIntoADirectoryObjectOfTheSecondVolume()
        {
            Run("cd(open(\"2:/d\")).");
            Assert.AreEqual("second inner", host.Output("print open(\"inner.txt\"):readall:string:trim."));
            host.AssertNoErrors();
        }

        [Test]
        public void RelativePathsUseTheCurrentVolumeAfterSwitch()
        {
            Run("switch to 2.");
            Assert.AreEqual("second", host.Output("print open(\"x.txt\"):readall:string:trim."));
            Assert.AreEqual("second", host.Output("print open(\"/x.txt\"):readall:string:trim."));
            Run("copypath(\"x.txt\", \"0:/io05/rel.txt\").");
            Assert.AreEqual("second\n", ArchiveText("io05/rel.txt"));
            host.AssertNoErrors();
        }

        [Test]
        public void RunPathOfAFileObjectRunsItsDisksScript()
        {
            first.CreateFile(VolumePath.FromString("/p.ks")).WriteLn("print \"from first\".");
            second.CreateFile(VolumePath.FromString("/p.ks")).WriteLn("print \"from second\".");
            Assert.AreEqual("from second\nProgram ended.", host.Output("runpath(open(\"2:/p.ks\"))."));
            host.AssertNoErrors();
        }
    }

    /// <summary>
    /// IO-05 at the volume manager: an object's volume keeps the manager's range check
    /// (the archive out of reach in the game).
    /// </summary>
    [TestFixture]
    public class VolumeEndpointRangeTest
    {
        private class ReachManager : VolumeManager
        {
            public Volume OutOfReach;

            public override bool CheckRange(Volume volume)
            {
                return volume != OutOfReach;
            }
        }

        [SetUp]
        public void SetUp()
        {
            SafeHouse.Logger = new TestLogger();
        }

        [Test]
        public void VolumeOutOfRangeFails()
        {
            var manager = new ReachManager();
            var near = new Harddisk(1000);
            var far = new Harddisk(1000);
            manager.Add(near);
            manager.Add(far);
            manager.OutOfReach = far;

            Assert.Throws<KOSVolumeOutOfRangeException>(() => manager.GetVolumeFromObject(far, manager.GlobalPathFromObject(far)));
            Assert.AreSame(near, manager.GetVolumeFromObject(near, manager.GlobalPathFromObject(near)));
        }

        [Test]
        public void StringPathResolvesThroughTheManager()
        {
            var manager = new VolumeManager();
            var disk = new Harddisk(1000);
            manager.Add(disk);
            GlobalPath path = manager.GlobalPathFromObject("0:/x");
            Assert.AreSame(disk, manager.GetVolumeFromObject("0:/x", path));
        }
    }
}
