using System;
using System.IO;
using kOS.Safe.Exceptions;
using kOS.Safe.Persistence;
using kOS.Safe.Utilities;
using NUnit.Framework;

namespace kOS.Safe.Test.Persistence
{
    /// <summary>
    /// IO-04: archive containment by directory boundary, not text prefix. Real directories:
    /// "Script" (the archive) beside "Script-private" (outside it).
    /// </summary>
    [TestFixture]
    public class DirectoryContainmentTest
    {
        private string baseDir, root, sibling;
        private static readonly string Sep = Path.DirectorySeparatorChar.ToString();

        [SetUp]
        public void SetUp()
        {
            SafeHouse.Logger = new TestLogger();
            baseDir = Path.Combine(Path.GetTempPath(), "kosim-io04-" + Guid.NewGuid().ToString("N"));
            root = Path.Combine(baseDir, "Script");
            sibling = Path.Combine(baseDir, "Script-private");
            Directory.CreateDirectory(Path.Combine(root, "sub"));
            Directory.CreateDirectory(sibling);
            File.WriteAllText(Path.Combine(sibling, "secret.png"), "secret");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(baseDir))
                Directory.Delete(baseDir, true);
        }

        private string[] Roots()
        {
            return new[] { root, root + Sep, root + Sep + Sep };
        }

        [Test]
        public void TheRootIsWithinButNotBelow()
        {
            foreach (string r in Roots())
            {
                foreach (string p in Roots())
                {
                    Assert.IsTrue(DirectoryContainment.IsWithin(r, p), r + " / " + p);
                    Assert.IsFalse(DirectoryContainment.IsBelow(r, p), r + " / " + p);
                }
            }
        }

        [Test]
        public void DescendantsAreBelow()
        {
            foreach (string r in Roots())
            {
                Assert.IsTrue(DirectoryContainment.IsBelow(r, Path.Combine(root, "sub")));
                Assert.IsTrue(DirectoryContainment.IsBelow(r, Path.Combine(root, "sub", "x.png")));
                Assert.IsTrue(DirectoryContainment.IsBelow(r, Path.Combine(root, "sub") + Sep));
            }
        }

        [Test]
        public void PrefixSharingSiblingIsOutside()
        {
            foreach (string r in Roots())
            {
                Assert.IsFalse(DirectoryContainment.IsWithin(r, sibling), r);
                Assert.IsFalse(DirectoryContainment.IsWithin(r, Path.Combine(sibling, "secret.png")), r);
                Assert.IsFalse(DirectoryContainment.IsWithin(r, root + "x"), r);
                Assert.IsFalse(DirectoryContainment.IsWithin(r, root + ".png"), r);
            }
        }

        [Test]
        public void ParentReferencesAreNormalized()
        {
            foreach (string r in Roots())
            {
                Assert.Throws<KOSInvalidPathException>(() => DirectoryContainment.Combine(r, Path.Combine("..", "Script-private", "secret.png")));
                Assert.Throws<KOSInvalidPathException>(() => DirectoryContainment.Combine(r, Path.Combine("sub", "..", "..", "Script-private")));
                Assert.Throws<KOSInvalidPathException>(() => DirectoryContainment.Combine(r, ".."));
                Assert.AreEqual(Path.Combine(root, "x.png"), DirectoryContainment.Combine(r, Path.Combine("sub", "..", "x.png")));
                Assert.AreEqual(root, DirectoryContainment.Combine(r, "."));
            }
        }

        [Test]
        public void RootedInputIsOutside()
        {
            string otherRoot = Path.GetPathRoot(root);
            Assert.Throws<KOSInvalidPathException>(() => DirectoryContainment.Combine(root, otherRoot));
            Assert.Throws<KOSInvalidPathException>(() => DirectoryContainment.Combine(root, Path.Combine(sibling, "secret.png")));
            // A rooted path naming a file inside is still inside.
            Assert.AreEqual(Path.Combine(root, "sub"), DirectoryContainment.Combine(root, Path.Combine(root, "sub")));
        }

        [Test]
        public void AlternateSeparatorsAreNormalized()
        {
            string alt = Path.AltDirectorySeparatorChar.ToString();
            Assert.Throws<KOSInvalidPathException>(() => DirectoryContainment.Combine(root, ".." + alt + "Script-private" + alt + "secret.png"));
            Assert.IsTrue(DirectoryContainment.IsBelow(root + alt, root + alt + "sub"));

            // A backslash separates only on Windows; elsewhere it is part of a file name.
            bool backslashSeparates = Path.DirectorySeparatorChar == '\\' || Path.AltDirectorySeparatorChar == '\\';
            string backslashed = "sub\\..\\..\\Script-private";
            Assert.AreEqual(!backslashSeparates, DirectoryContainment.IsWithin(root, Path.Combine(root, backslashed)));
        }

        [Test]
        public void CaseDifferencesAreOutside()
        {
            // Ordinal on every platform: on a case-insensitive file system this refuses a
            // path that is really inside, never accepts one that is outside.
            string upper = Path.Combine(baseDir, "SCRIPT", "x.png");
            Assert.IsFalse(DirectoryContainment.IsWithin(root, upper));
        }

        [Test]
        public void FileSystemRootContainsEverything()
        {
            string fsRoot = Path.GetPathRoot(root);
            Assert.IsTrue(DirectoryContainment.IsWithin(fsRoot, fsRoot));
            Assert.IsFalse(DirectoryContainment.IsBelow(fsRoot, fsRoot));
            Assert.IsTrue(DirectoryContainment.IsBelow(fsRoot, root));
        }

        [Test]
        public void NullArgumentsThrow()
        {
            Assert.Throws<ArgumentNullException>(() => DirectoryContainment.IsWithin(null, root));
            Assert.Throws<ArgumentNullException>(() => DirectoryContainment.IsWithin(root, null));
        }

        [Test]
        public void TextureCandidatesArePngThenAsGiven()
        {
            CollectionAssert.AreEqual(
                new[] { Path.Combine(root, "sub", "img") + ".png", Path.Combine(root, "sub", "img") },
                ArchiveTextureFiles.Candidates(root, Path.Combine("sub", "img")));
            // Missing files are the caller's business (an empty texture), not an error here.
            Assert.AreEqual(2, ArchiveTextureFiles.Candidates(root + Sep, "missing").Length);
        }

        [Test]
        public void TextureOfTheArchiveFolderItselfIsRefused()
        {
            // Otherwise "." + ".png" would read "Script.png" next to the archive.
            File.WriteAllText(root + ".png", "beside");
            foreach (string r in Roots())
            {
                Assert.Throws<KOSInvalidPathException>(() => ArchiveTextureFiles.Candidates(r, "."));
                Assert.Throws<KOSInvalidPathException>(() => ArchiveTextureFiles.Candidates(r, Path.Combine("sub", "..")));
                Assert.Throws<KOSInvalidPathException>(() => ArchiveTextureFiles.Candidates(r, ""));
            }
        }

        [Test]
        public void TextureOutsideTheArchiveIsRefused()
        {
            foreach (string r in Roots())
            {
                Assert.Throws<KOSInvalidPathException>(() => ArchiveTextureFiles.Candidates(r, Path.Combine("..", "Script-private", "secret")));
                Assert.Throws<KOSInvalidPathException>(() => ArchiveTextureFiles.Candidates(r, Path.Combine(sibling, "secret")));
            }
        }

        [Test]
        public void ArchivePathsStayInsideWithEitherRootForm()
        {
            foreach (string r in new[] { root, root + Sep })
            {
                var archive = new Archive(r);
                Assert.AreEqual(root, archive.GetArchivePath(VolumePath.EMPTY).TrimEnd(Path.DirectorySeparatorChar));
                Assert.AreEqual(Path.Combine(root, "sub", "x.ks"), archive.GetArchivePath(VolumePath.FromString("/sub/x.ks")));
                Assert.Throws<KOSInvalidPathException>(() => archive.GetArchivePath(VolumePath.FromString("/..")));
            }
        }
    }
}
