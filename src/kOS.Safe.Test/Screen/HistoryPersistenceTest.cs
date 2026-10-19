using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using kOS.Safe.Persistence;
using kOS.Safe.Screen;
using kOS.Safe.Test.Execution;
using kOS.Safe.UserIO;
using kOS.Safe.Utilities;
using NUnit.Framework;

namespace kOS.Safe.Test.Terminal
{
    /// <summary>
    /// Phase 3, decisions H3/H4: the history saved per CPU (<see cref="CommandHistoryFile"/>,
    /// read on first use, saved after every recorded command, kept on reboot), and commands
    /// typed with a leading space not recorded.
    /// </summary>
    [TestFixture]
    public class HistoryPersistenceTest
    {
        private const char UP = (char)UnicodeCommand.UPCURSORONE;

        private string directory;
        private ILogger previousLogger;
        private WarningLogger logger;

        private sealed class WarningLogger : ILogger
        {
            public readonly List<string> Warnings = new List<string>();
            public void Log(string text) { }
            public void Log(Exception e) { }
            public void SuperVerbose(string s) { }
            public void LogWarning(string s) { Warnings.Add(s); }
            public void LogWarningAndScreen(string s) { Warnings.Add(s); }
            public void LogException(Exception exception) { }
            public void LogError(string s) { }
        }

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "kosim-history-" + Guid.NewGuid().ToString("N"));
            previousLogger = SafeHouse.Logger;
            logger = new WarningLogger();
            SafeHouse.Logger = logger;
        }

        [TearDown]
        public void TearDown()
        {
            SafeHouse.Logger = previousLogger;
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }

        private string FilePath { get { return Path.Combine(directory, "kOS", "history", "123.txt"); } }

        private sealed class MemoryStore : ICommandHistoryStore
        {
            public List<string> Saved = new List<string>();
            public int Loads;
            public int Saves;

            public IList<string> Load()
            {
                ++Loads;
                return new List<string>(Saved);
            }

            public void Save(IEnumerable<string> commands)
            {
                ++Saves;
                Saved = commands.ToList();
            }
        }

        // CommandHistory:

        [Test]
        public void LeadingSpaceRunsButIsNotRecorded()
        {
            var history = new CommandHistory();
            history.Add("print 1.");
            int version = history.Version;
            int first = history.Add(" print secret.");
            Assert.AreEqual(1, history.Count);
            Assert.AreEqual(version, history.Version, "nothing to save");
            Assert.AreEqual(" print secret.", history.GetLine(first), "its own error messages still show it");
            string text;
            int cursor;
            Assert.IsTrue(history.TryMove(-1, "", 0, out text, out cursor));
            Assert.AreEqual("print 1.", text);

            int next = history.Add("print 2.");
            Assert.AreEqual(first + 1, next, "the unrecorded command used up its line");
            int second = history.Add(" a.\n b.");
            history.Add(" c.");
            Assert.AreEqual(CommandHistory.TRIMMED_LINE, history.GetLine(second), "only the newest unrecorded one is kept");
            Assert.AreEqual(CommandHistory.TRIMMED_LINE, history.GetLine(first));
        }

        [Test]
        public void LoadPutsSavedCommandsFirstWithinTheBound()
        {
            var history = new CommandHistory(4);
            int typed = history.Add("typed.");
            history.Load(new[] { "a.", "", " hidden.", "b.", "b.", "c.", "d." });
            CollectionAssert.AreEqual(new[] { "b.", "c.", "d.", "typed." }, history.Commands.ToArray(),
                "empty, leading-space and repeated commands skipped; oldest dropped");
            Assert.AreEqual(CommandHistory.TRIMMED_LINE, history.GetLine(typed), "loaded commands get new numbers");
            int next = history.Add("e.");
            Assert.AreEqual("e.", history.GetLine(next));
            Assert.AreEqual("d.", history.GetLine(next - 2));
        }

        // CommandHistoryFile:

        [Test]
        public void FormatAndParseRoundTrip()
        {
            var commands = new[] { "print 1.", "if true {\nprint \"a\\nb\".\n}", "set s to \"C:\\\\x\".", "print \"é\tñ\".", "a\rb" };
            string text = CommandHistoryFile.Format(commands);
            StringAssert.StartsWith(CommandHistoryFile.HEADER + "\n", text);
            Assert.AreEqual(1 + commands.Length, text.Count(ch => ch == '\n'), "one line per command");
            CollectionAssert.AreEqual(commands, CommandHistoryFile.Parse(text));
        }

        [Test]
        public void ParseIsTolerant()
        {
            CollectionAssert.AreEqual(new[] { "print 1.", "a\\qb", "x\\", "y" },
                CommandHistoryFile.Parse("print 1.\r\n\r\na\\qb\nx\\\ny"), "no header, CR LF, empty lines, unknown and trailing escapes");
        }

        [Test]
        public void SaveThenLoad()
        {
            var store = new CommandHistoryFile(() => FilePath);
            CollectionAssert.IsEmpty(store.Load(), "no file yet");
            store.Save(new[] { "print 1.", "list.\nprint 2." });
            Assert.IsTrue(File.Exists(FilePath), "the folders are created");
            Assert.AreEqual(FilePath, store.Path);
            CollectionAssert.AreEqual(new[] { "print 1.", "list.\nprint 2." }, new CommandHistoryFile(() => FilePath).Load());
            byte[] bytes = File.ReadAllBytes(FilePath);
            Assert.AreNotEqual(0xEF, bytes[0], "no byte order mark");
            CollectionAssert.IsEmpty(Directory.GetFiles(Path.GetDirectoryName(FilePath), "*.tmp"), "no temporary file left");
        }

        [Test]
        public void PathIsAskedOnceAndNullKeepsNothing()
        {
            int asked = 0;
            var store = new CommandHistoryFile(() => { ++asked; return (string)null; });
            store.Save(new[] { "a." });
            CollectionAssert.IsEmpty(store.Load());
            Assert.AreEqual(1, asked);
        }

        private sealed class FailingReplace : ArchiveFileOperations
        {
            internal override void Replace(string source, string destination) { throw new IOException("replace refused"); }
        }

        [Test]
        public void AFailedSaveKeepsTheOldFileAndDoesNotThrow()
        {
            new CommandHistoryFile(() => FilePath).Save(new[] { "old." });
            var store = new CommandHistoryFile(() => FilePath, new FailingReplace());
            Assert.DoesNotThrow(() => store.Save(new[] { "new." }));
            Assert.DoesNotThrow(() => store.Save(new[] { "newer." }));
            CollectionAssert.AreEqual(new[] { "old." }, new CommandHistoryFile(() => FilePath).Load());
            CollectionAssert.IsEmpty(Directory.GetFiles(Path.GetDirectoryName(FilePath), "*.tmp"));
            Assert.AreEqual(1, logger.Warnings.Count, "logged once, not per command");
            StringAssert.Contains("replace refused", logger.Warnings[0]);
        }

        [Test]
        public void AnOversizedFileIsNotRead()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            using (var stream = new FileStream(FilePath, FileMode.Create))
                stream.SetLength(CommandHistoryFile.MAX_FILE_BYTES + 1);
            CollectionAssert.IsEmpty(new CommandHistoryFile(() => FilePath).Load());
            Assert.AreEqual(1, logger.Warnings.Count);
        }

        // The prompt, through the fixture (the shared CommandLineEditor):

        [Test]
        public void HistoryIsReadOnFirstUseAndSavedAfterEachCommand()
        {
            var store = new MemoryStore { Saved = new List<string> { "print \"saved\"." } };
            using (var host = new ScriptHost(10, 40))
            {
                host.Screen.HistoryStore = store;
                Assert.AreEqual(0, store.Loads, "not read before use");
                host.Screen.SpecialKey(UP);
                Assert.AreEqual("print \"saved\".", host.Screen.InputLine);
                host.Type("\n");
                host.RunUntilPrompt();
                host.AssertNoErrors();
                Assert.AreEqual(1, store.Loads);
                Assert.AreEqual(0, store.Saves, "a repeat of the newest changes nothing");

                host.Execute("print 2.");
                Assert.AreEqual(1, store.Saves);
                CollectionAssert.AreEqual(new[] { "print \"saved\".", "print 2." }, store.Saved);
                host.Execute("");
                host.Execute(" print 3.");
                Assert.AreEqual(1, store.Saves, "empty and leading-space commands are not saved");
                CollectionAssert.AreEqual(new[] { "print \"saved\".", "print 2." }, store.Saved);
            }
        }

        [Test]
        public void ALeadingSpaceCommandRunsAndShowsItsErrorLine()
        {
            using (var host = new ScriptHost(10, 40))
            {
                host.Execute(" print 3.");
                StringAssert.Contains("3", host.Transcript());
                host.Execute(" print nosuchvariable.");
                Assert.AreEqual(1, host.Errors.Count);
                Assert.AreEqual(" print nosuchvariable.", host.InterpreterSourceLines.Last());
                host.Screen.SpecialKey(UP);
                Assert.AreEqual("", host.Screen.InputLine, "nothing to recall");
            }
        }

        [Test]
        public void RebootKeepsTheHistory()
        {
            var store = new MemoryStore();
            using (var host = new ScriptHost(10, 40))
            {
                host.Screen.HistoryStore = store;
                host.Execute("print 1.");
                host.Screen.SpecialKey(UP);
                host.Screen.Reset();
                Assert.AreEqual(1, store.Loads, "not read again");
                host.Screen.SpecialKey(UP);
                Assert.AreEqual("print 1.", host.Screen.InputLine, "navigation starts again from the newest");
            }
        }

        [Test]
        public void SearchFindsSavedCommands()
        {
            var store = new MemoryStore { Saved = new List<string> { "set target to \"mun\"." } };
            using (var host = new ScriptHost(10, 50))
            {
                host.Boot();
                host.Screen.HistoryStore = store;
                host.Screen.SpecialKey(CommandLineEditor.SEARCH_KEY);
                host.Type("mun");
                host.Screen.SpecialKey(CommandLineEditor.CANCEL_KEY);
                host.Screen.SpecialKey(CommandLineEditor.SEARCH_KEY);
                host.Screen.SpecialKey(CommandLineEditor.SEARCH_KEY);
                host.Screen.SpecialKey((char)UnicodeCommand.ENDCURSOR);
                Assert.AreEqual("set target to \"mun\".", host.Screen.InputLine);
            }
        }

        [Test]
        public void TheFileStoreThroughThePrompt()
        {
            using (var host = new ScriptHost(10, 40))
            {
                host.Screen.HistoryStore = new CommandHistoryFile(() => FilePath);
                host.Execute("print 1.");
                host.Execute("if true {\nprint 2.\n}");
            }
            Assert.AreEqual(CommandHistoryFile.HEADER + "\nprint 1.\nif true {\\nprint 2.\\n}\n",
                File.ReadAllText(FilePath, Encoding.UTF8));
            using (var host = new ScriptHost(10, 40))
            {
                host.Screen.HistoryStore = new CommandHistoryFile(() => FilePath);
                host.Screen.SpecialKey(UP);
                Assert.AreEqual("if true {\nprint 2.\n}", host.Screen.InputLine, "a new game session reads it back");
            }
        }
    }
}
