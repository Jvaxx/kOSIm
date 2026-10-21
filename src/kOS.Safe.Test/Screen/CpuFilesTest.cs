using System;
using System.Collections.Generic;
using System.IO;
using kOS.Safe.Persistence;
using kOS.Safe.Screen;
using kOS.Safe.Utilities;
using NUnit.Framework;

namespace kOS.Safe.Test.Terminal
{
    /// <summary>
    /// Review N-01: a CPU file's save says whether it saved (the geometry presets will say so
    /// on the terminal).
    /// </summary>
    [TestFixture]
    public class CpuTextFileTest
    {
        private string directory;
        private ILogger previousLogger;

        private sealed class QuietLogger : ILogger
        {
            public int Warnings;
            public void Log(string text) { }
            public void Log(Exception e) { }
            public void SuperVerbose(string s) { }
            public void LogWarning(string s) { ++Warnings; }
            public void LogWarningAndScreen(string s) { ++Warnings; }
            public void LogException(Exception exception) { }
            public void LogError(string s) { }
        }

        private sealed class FailingReplace : ArchiveFileOperations
        {
            internal override void Replace(string source, string destination) { throw new IOException("replace refused"); }
        }

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "kosim-cpufile-" + Guid.NewGuid().ToString("N"));
            previousLogger = SafeHouse.Logger;
            SafeHouse.Logger = new QuietLogger();
        }

        [TearDown]
        public void TearDown()
        {
            SafeHouse.Logger = previousLogger;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        private string FilePath { get { return Path.Combine(directory, "terminal", "42.txt"); } }

        [Test]
        public void WriteSaysWhetherItSaved()
        {
            var file = new CpuTextFile("Test", () => FilePath, 4096, new ArchiveFileOperations());
            Assert.IsTrue(file.Write("first"), "a new file");
            Assert.IsTrue(file.Write("second"), "replacing it");
            Assert.AreEqual("second", File.ReadAllText(FilePath));

            var failing = new CpuTextFile("Test", () => FilePath, 4096, new FailingReplace());
            Assert.IsFalse(failing.Write("third"), "a failed save");
            Assert.IsFalse(failing.Write("fourth"), "every failed save, not only the one logged");
            Assert.AreEqual("second", File.ReadAllText(FilePath), "the old file is kept");
            Assert.AreEqual(1, ((QuietLogger)SafeHouse.Logger).Warnings, "logged once");

            Assert.IsFalse(new CpuTextFile("Test", () => null, 4096, new ArchiveFileOperations()).Write("x"), "no file");
            Assert.IsFalse(new CpuTextFile("Test", () => { throw new InvalidOperationException("no save"); }, 4096,
                new ArchiveFileOperations()).Write("x"), "no path");
        }
    }

    /// <summary>
    /// Review L-02: the per-CPU files of parts that no save has any more are removed when a
    /// save is loaded; a doubt keeps them.
    /// </summary>
    [TestFixture]
    public class CpuFilePruneTest
    {
        private string save;
        private DateTime now;

        // A save as KSP writes it: tabs, nested nodes, persistentId in vessels, parts and modules.
        private static string Save(params uint[] partIds)
        {
            var text = new System.Text.StringBuilder("GAME\n{\n\tversion = 1.12.5\n\tFLIGHTSTATE\n\t{\n\t\tVESSEL\n\t\t{\n\t\t\tpersistentId = 7\n");
            foreach (uint id in partIds)
                text.Append("\t\t\tPART\n\t\t\t{\n\t\t\t\tname = kOSMachine1m\n\t\t\t\tpersistentId = ").Append(id)
                    .Append("\n\t\t\t\tMODULE\n\t\t\t\t{\n\t\t\t\t\tname = kOSProcessor\n\t\t\t\t}\n\t\t\t}\n");
            return text.Append("\t\t}\n\t}\n}\n").ToString();
        }

        private string CpuFile(string folder, string name, DateTime written)
        {
            string path = Path.Combine(save, "kOS", folder, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "kOS test\n");
            File.SetLastWriteTimeUtc(path, written);
            return path;
        }

        private string CpuFile(string folder, string name)
        {
            return CpuFile(folder, name, now.AddHours(-1));
        }

        private void WriteSave(string relativePath, string text)
        {
            string path = Path.Combine(save, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
        }

        [SetUp]
        public void SetUp()
        {
            save = Path.Combine(Path.GetTempPath(), "kosim-prune-" + Guid.NewGuid().ToString("N"), "saves", "career");
            Directory.CreateDirectory(save);
            now = DateTime.UtcNow;
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(save)), true);
        }

        [Test]
        public void FileNamesAreThoseOfTheProcessor()
        {
            uint id;
            Assert.IsTrue(CpuFilePrune.TryParseFileName("80427209.txt", out id));
            Assert.AreEqual(80427209u, id);
            Assert.IsTrue(CpuFilePrune.TryParseFileName("2828206133-1.txt", out id), "a second kOS module on the part");
            Assert.AreEqual(2828206133u, id);
            foreach (string name in new[] { "notes.txt", "80427209.txt.bak", "80427209.TXT", "-1.txt", "80427209-.txt",
                "80427209-a.txt", "0.txt", "99999999999.txt", "+5.txt", " 5.txt", "5 .txt", ".txt", "", null,
                ".kosim-save-0123456789abcdef0123456789abcdef.tmp" })
                Assert.IsFalse(CpuFilePrune.TryParseFileName(name, out id), name ?? "null");
        }

        [Test]
        public void SavedIdsAreReadFromEveryNodeOfAWholeSave()
        {
            var ids = new HashSet<uint>();
            Assert.IsTrue(CpuFilePrune.AddSavedIds(new StringReader(Save(11, 22) +
                "STORED\n{\n  persistentId=33\n persistentIdOther = 44\n persistentId = x\n}\n"), ids));
            CollectionAssert.AreEquivalent(new uint[] { 7, 11, 22, 33 }, ids);

            string whole = Save(55);
            Assert.IsFalse(CpuFilePrune.AddSavedIds(new StringReader(whole.Substring(0, whole.Length - 4)), new HashSet<uint>()),
                "cut short");
            Assert.IsFalse(CpuFilePrune.AddSavedIds(new StringReader(whole + "}\n"), new HashSet<uint>()), "one brace too many");
            Assert.IsFalse(CpuFilePrune.AddSavedIds(new StringReader(whole + "GAME\n{\n"), new HashSet<uint>()), "cut in a node");
            Assert.IsFalse(CpuFilePrune.AddSavedIds(new StringReader(""), new HashSet<uint>()), "empty");
        }

        [Test]
        public void AFileIsRemovedOnlyWhenNoSaveHasItsPart()
        {
            WriteSave("persistent.sfs", Save(1, 2));
            WriteSave("quicksave.sfs", Save(1, 3));
            WriteSave("Backup/persistent (2026_10_20_18_56_51).sfs", Save(4));
            WriteSave("persistent.sfs.before-2cpu", Save()); // not a save: ignored
            string[] kept = {
                CpuFile("history", "1.txt"), CpuFile("terminal", "1.txt"), // in the loaded game
                CpuFile("history", "2.txt"), CpuFile("history", "2-1.txt"), // in persistent.sfs only
                CpuFile("history", "3.txt"), // in the quicksave
                CpuFile("terminal", "4.txt"), // in a backup
                CpuFile("history", "notes.txt"), CpuFile("terminal", "9a.txt"), // not the processor's names
                CpuFile("history", "6.txt", now.AddSeconds(-20)), // changed since the load
            };
            string[] removed = { CpuFile("history", "5.txt"), CpuFile("terminal", "5.txt"), CpuFile("history", "5-1.txt") };

            CpuFilePrune.Result result = CpuFilePrune.Run(save, new HashSet<uint> { 1 }, now);

            Assert.IsNull(result.Skipped);
            Assert.AreEqual(3, result.Deleted);
            foreach (string path in kept) Assert.IsTrue(File.Exists(path), path);
            foreach (string path in removed) Assert.IsFalse(File.Exists(path), path);
        }

        [Test]
        public void ASaveThatCannotBeReadWholeKeepsEveryFile()
        {
            string whole = Save(2);
            WriteSave("persistent.sfs", Save(1));
            WriteSave("named.sfs", whole.Substring(0, whole.Length / 2)); // being written, or damaged
            string gone = CpuFile("history", "5.txt");

            CpuFilePrune.Result result = CpuFilePrune.Run(save, new HashSet<uint> { 1 }, now);

            Assert.AreEqual(0, result.Deleted);
            StringAssert.Contains("named.sfs", result.Skipped);
            Assert.IsTrue(File.Exists(gone));
        }

        [Test]
        public void WhenEveryPartIsLoadedNoSaveIsRead()
        {
            WriteSave("damaged.sfs", "GAME\n{\n"); // would keep every file, if read
            string live = CpuFile("terminal", "1.txt");

            CpuFilePrune.Result result = CpuFilePrune.Run(save, new HashSet<uint> { 1 }, now);

            Assert.IsNull(result.Skipped, "the saves were not read");
            Assert.AreEqual(0, result.Deleted);
            Assert.IsTrue(File.Exists(live));
        }

        [Test]
        public void ACrashsTemporaryFilesGoOnceOld()
        {
            string old = CpuFile("history", ".kosim-save-0123456789abcdef0123456789abcdef.tmp");
            string fresh = CpuFile("terminal", ".kosim-save-fedcba9876543210fedcba9876543210.tmp", now.AddSeconds(-10));

            CpuFilePrune.Result result = CpuFilePrune.Run(save, new HashSet<uint>(), now);

            Assert.AreEqual(1, result.TemporaryDeleted);
            Assert.IsFalse(File.Exists(old));
            Assert.IsTrue(File.Exists(fresh), "a save in progress");
        }

        [Test]
        public void ASaveWithoutCpuFilesIsLeftAlone()
        {
            CpuFilePrune.Result result = CpuFilePrune.Run(save, new HashSet<uint>(), now);
            Assert.AreEqual(0, result.Deleted + result.TemporaryDeleted);
            Assert.IsNull(result.Skipped);
            Assert.IsFalse(Directory.Exists(Path.Combine(save, "kOS")), "nothing created");
        }
    }

    /// <summary>Review L-04: the terminal's install check looks for the files it uses.</summary>
    [TestFixture]
    public class TerminalInstallCheckTest
    {
        [Test]
        public void MissingFilesAreNamed()
        {
            string game = Path.Combine(Path.GetTempPath(), "kosim-install-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(game);
                CollectionAssert.AreEqual(TerminalInstallCheck.FILES, TerminalInstallCheck.Missing(game), "nothing installed");

                string gfx = Path.Combine(game, "GameData", "kOS", "GFX");
                Directory.CreateDirectory(gfx);
                File.WriteAllText(Path.Combine(gfx, "terminal-beep.wav"), "");
                CollectionAssert.AreEqual(new[] { "GameData/kOS/GFX/terminal-click.wav" }, TerminalInstallCheck.Missing(game));

                File.WriteAllText(Path.Combine(gfx, "terminal-click.wav"), "");
                CollectionAssert.IsEmpty(TerminalInstallCheck.Missing(game), "installed");
                CollectionAssert.IsEmpty(TerminalInstallCheck.Missing(game + Path.DirectorySeparatorChar), "a folder ending in a separator");
            }
            finally
            {
                Directory.Delete(game, true);
            }
        }

        [Test]
        public void TheRepositoryHoldsTheFilesChecked()
        {
            // The release is zipped from Resources/ (its GameData folder): the checked files must be in it.
            string repository = Directory.GetCurrentDirectory();
            while (!File.Exists(Path.Combine(repository, "kOS.sln")))
                repository = Directory.GetParent(repository).FullName; // throws past the root: not found
            CollectionAssert.IsEmpty(TerminalInstallCheck.Missing(Path.Combine(repository, "Resources")));
        }
    }

    /// <summary>Review D-03: one part name for the titles, the toolbar panel and the telnet menu.</summary>
    [TestFixture]
    public class CpuNamesTest
    {
        [Test]
        public void ThePartNameIsTheTitlesFirstWord()
        {
            Assert.AreEqual("CX-4181", CpuNames.PartName("CX-4181 Scriptable Control System"));
            Assert.AreEqual("KAL9000", CpuNames.PartName("KAL9000"));
            Assert.AreEqual("KAL9000", CpuNames.PartName("  KAL9000 "));
            Assert.AreEqual("CPU", CpuNames.PartName(null));
            Assert.AreEqual("CPU", CpuNames.PartName(" "));
        }
    }
}
