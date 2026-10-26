using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using kOS.Safe.Screen;
using kOS.Safe.Test.Execution;
using kOS.Safe.UserIO;
using NUnit.Framework;

namespace kOS.Safe.Test.Terminal
{
    /// <summary>
    /// One command history shared by every terminal of a save (user decision 2026-10-26):
    /// <see cref="CommandHistoryList"/> under several <see cref="CommandHistory"/>, each with its
    /// own position, draft and search; <see cref="SharedCommandHistory"/> handing the save's
    /// list to the CPUs; then two prompts through the fixture.
    /// </summary>
    [TestFixture]
    public class SharedHistoryTest
    {
        private const char UP = (char)UnicodeCommand.UPCURSORONE;
        private const char DOWN = (char)UnicodeCommand.DOWNCURSORONE;

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

        private static string Move(CommandHistory history, int delta, string current = "", int cursor = 0)
        {
            string text;
            int newCursor;
            Assert.IsTrue(history.TryMove(delta, current, cursor, out text, out newCursor), "move " + delta);
            return text;
        }

        private static bool CanMove(CommandHistory history, int delta)
        {
            string text;
            int cursor;
            return history.TryMove(delta, "", 0, out text, out cursor);
        }

        // CommandHistory over a shared CommandHistoryList:

        [Test]
        public void ACommandTypedOnOneTerminalIsRecalledOnTheOther()
        {
            var list = new CommandHistoryList();
            var a = new CommandHistory(list);
            var b = new CommandHistory(list);
            a.Add("print 1.");
            b.Add("print 2.");
            Assert.AreSame(list, a.List);
            Assert.AreEqual(2, a.Count);
            Assert.AreEqual("print 2.", Move(a, -1, "draft a", 7));
            Assert.AreEqual("print 1.", Move(a, -1));
            Assert.AreEqual("print 2.", Move(b, -1, "draft b", 7), "each terminal has its own position");
            Assert.AreEqual("print 2.", Move(a, 1));
            Assert.AreEqual("draft a", Move(a, 1), "and its own draft");
            Assert.AreEqual("draft b", Move(b, 1));
        }

        [Test]
        public void ARecalledCommandKeepsItsPlaceWhenAnotherTerminalAddsOne()
        {
            var list = new CommandHistoryList();
            var a = new CommandHistory(list);
            var b = new CommandHistory(list);
            a.Add("a.");
            a.Add("b.");
            Assert.AreEqual("b.", Move(a, -1));
            Assert.IsTrue(a.IsRecalling);
            b.Add("c.");
            Assert.AreEqual("a.", Move(a, -1), "older than b., not b. again");
            Assert.AreEqual("b.", Move(a, 1));
            Assert.AreEqual("c.", Move(a, 1), "the new command is newer");
            Assert.AreEqual("", Move(a, 1), "then the draft");
            Assert.IsFalse(a.IsRecalling);
            Assert.AreEqual("c.", Move(a, -1), "from the draft, the newest");
        }

        [Test]
        public void ARecalledCommandDroppedByAnotherTerminalMovesFromWhereItWas()
        {
            var list = new CommandHistoryList(3);
            var a = new CommandHistory(list);
            var b = new CommandHistory(list);
            a.Add("a.");
            a.Add("b.");
            a.Add("c.");
            Move(a, -1);
            Move(a, -1);
            Assert.AreEqual("a.", Move(a, -1));
            b.Add("d."); // drops a.
            CollectionAssert.AreEqual(new[] { "b.", "c.", "d." }, list.Commands.ToArray());
            Assert.IsFalse(CanMove(a, -1), "nothing older than the dropped command");
            Assert.AreEqual("b.", Move(a, 1), "the next newer one");

            Move(a, 1);
            Move(a, 1);
            b.Add("e.");
            b.Add("f.");
            b.Add("g."); // drops everything a. saw
            Assert.AreEqual("e.", Move(a, 1), "a recalled d., dropped: the next newer is the oldest left");
        }

        [Test]
        public void LineNumbersAreNeverHandedTwiceAcrossTerminals()
        {
            var list = new CommandHistoryList();
            var a = new CommandHistory(list);
            var b = new CommandHistory(list);
            Assert.AreEqual(1, a.Add("if true {\nprint x.\n}"));
            Assert.AreEqual(4, b.Add("print y."));
            Assert.AreEqual(5, a.Add(" print secret."), "an unrecorded command takes numbers from the shared counter");
            Assert.AreEqual(6, b.Add("print z."));
            Assert.AreEqual(6, a.Add("print z."), "a repeat of the newest, from either terminal, is the same entry");
            Assert.AreEqual("print x.", b.GetLine(2), "recorded commands are shared");
            Assert.AreEqual("print y.", a.GetLine(4));
            Assert.AreEqual(" print secret.", a.GetLine(5));
            Assert.AreEqual(CommandHistory.TRIMMED_LINE, b.GetLine(5), "an unrecorded command is its terminal's own");
        }

        [Test]
        public void TheSharedListIsReadOnceAndSavedOnce()
        {
            var store = new MemoryStore { Saved = new List<string> { "saved." } };
            var list = new CommandHistoryList { Store = store };
            var a = new CommandHistory(list);
            var b = new CommandHistory(list);
            Assert.AreEqual(0, store.Loads, "not read before use");
            Assert.AreEqual("saved.", Move(b, -1));
            a.Add("print 1.");
            Assert.AreEqual(1, store.Loads);
            Assert.AreEqual(1, store.Saves);
            CollectionAssert.AreEqual(new[] { "saved.", "print 1." }, store.Saved);
            Assert.AreEqual("print 1.", Move(b, 1), "b was on saved.: the next newer is the new command");
        }

        [Test]
        public void ClearIsSavedLikeACommand()
        {
            var store = new MemoryStore { Saved = new List<string> { "saved." } };
            var list = new CommandHistoryList { Store = store };
            new CommandHistory(list).Clear();
            Assert.AreEqual(1, store.Saves);
            CollectionAssert.IsEmpty(store.Saved);
        }

        // SharedCommandHistory: the save's list, handed to its CPUs.

        private sealed class Stores
        {
            public readonly Dictionary<string, MemoryStore> ByPath = new Dictionary<string, MemoryStore>();
            public int Made;

            public ICommandHistoryStore For(string path)
            {
                ++Made;
                MemoryStore store;
                if (!ByPath.TryGetValue(path, out store))
                    ByPath[path] = store = new MemoryStore();
                return store;
            }
        }

        [Test]
        public void TheCpusOfASaveGetOneListReadOnce()
        {
            var stores = new Stores();
            var share = new SharedCommandHistory(stores.For);
            CommandHistoryList first = share.Acquire("save/kOS/history.txt");
            CommandHistoryList second = share.Acquire("save/kOS/history.txt");
            Assert.AreSame(first, second);
            Assert.AreEqual(2, share.Users);
            Assert.AreEqual(1, stores.Made);
            new CommandHistory(first).Add("print 1.");
            Assert.AreEqual(1, new CommandHistory(second).Count);
            Assert.AreEqual(1, stores.ByPath["save/kOS/history.txt"].Loads);
        }

        [Test]
        public void TheLastCpuGoingSavesAndForgetsTheList()
        {
            var stores = new Stores();
            var share = new SharedCommandHistory(stores.For);
            var clock = TimeSpan.FromSeconds(100);
            CommandHistoryList list = share.Acquire("h.txt");
            share.Acquire("h.txt");
            list.Clock = () => clock;
            var history = new CommandHistory(list);
            history.Add("print 1.");
            history.Add("print 2."); // within the save interval: waits
            MemoryStore store = stores.ByPath["h.txt"];
            Assert.AreEqual(1, store.Saves);

            share.Release(list);
            Assert.AreEqual(2, store.Saves, "a CPU going saves what waits at once");
            CollectionAssert.AreEqual(new[] { "print 1.", "print 2." }, store.Saved);
            Assert.AreSame(list, share.Acquire("h.txt"), "still held by the other CPU");
            share.Release(list);
            share.Release(list);
            Assert.AreEqual(0, share.Users);

            CommandHistoryList next = share.Acquire("h.txt");
            Assert.AreNotSame(list, next, "the next flight reads the file again");
            store.Saved = new List<string> { "edited outside." };
            CollectionAssert.AreEqual(new[] { "edited outside." }, next.Commands.ToArray());
            share.Release(null); // a CPU that never had one
            Assert.AreEqual(1, share.Users);
        }

        [Test]
        public void AnotherSavesFileReplacesTheList()
        {
            var stores = new Stores();
            var share = new SharedCommandHistory(stores.For);
            var clock = TimeSpan.FromSeconds(100);
            CommandHistoryList old = share.Acquire("career/history.txt");
            old.Clock = () => clock;
            var history = new CommandHistory(old);
            history.Add("a.");
            history.Add("b.");

            CommandHistoryList other = share.Acquire("sandbox/history.txt");
            Assert.AreNotSame(old, other);
            CollectionAssert.AreEqual(new[] { "a.", "b." }, stores.ByPath["career/history.txt"].Saved, "saved before it is let go");
            Assert.AreEqual(1, share.Users);

            history.Add("c.");
            share.Release(old); // a CPU of the old save going late
            CollectionAssert.AreEqual(new[] { "a.", "b.", "c." }, stores.ByPath["career/history.txt"].Saved, "to its own file");
            Assert.AreEqual(1, share.Users, "the new list is still held");
            Assert.AreSame(other, share.Acquire("sandbox/history.txt"));
        }

        [Test]
        public void AcquireNeedsAPath()
        {
            var share = new SharedCommandHistory(path => new MemoryStore());
            Assert.Throws<ArgumentNullException>(() => share.Acquire(null));
            Assert.Throws<ArgumentNullException>(() => new SharedCommandHistory(null));
        }

        // Two prompts sharing a list, through the fixture:

        [Test]
        public void TwoPromptsShareTheirCommands()
        {
            var store = new MemoryStore { Saved = new List<string> { "print \"saved\"." } };
            var list = new CommandHistoryList { Store = store };
            using (var a = new ScriptHost(10, 40))
            using (var b = new ScriptHost(10, 40))
            {
                a.Screen.HistoryList = list;
                b.Screen.HistoryList = list;
                a.Execute("print 1.");
                b.Type("print 3");
                b.Screen.SpecialKey(UP);
                Assert.AreEqual("print 1.", b.Screen.InputLine, "typed on the other terminal");
                b.Screen.SpecialKey(UP);
                Assert.AreEqual("print \"saved\".", b.Screen.InputLine);
                b.Screen.SpecialKey(DOWN);
                b.Screen.SpecialKey(DOWN);
                Assert.AreEqual("print 3", b.Screen.InputLine, "its own draft");
                b.Type(".\n");
                b.RunUntilPrompt();
                a.AssertNoErrors();
                b.AssertNoErrors();
                Assert.AreEqual(1, store.Loads);
                b.Screen.SavePendingHistory(true);
                a.Screen.SavePendingHistory(true);
                CollectionAssert.AreEqual(new[] { "print \"saved\".", "print 1.", "print 3." }, store.Saved);
                Assert.AreSame(store, a.Screen.HistoryStore);
            }
        }

        [Test]
        public void AnErrorShowsItsOwnLineWhenTheOtherPromptAddedCommands()
        {
            var list = new CommandHistoryList();
            using (var a = new ScriptHost(10, 40))
            using (var b = new ScriptHost(10, 40))
            {
                a.Screen.HistoryList = list;
                b.Screen.HistoryList = list;
                a.Execute("function f {\nprint nosuchvariable.\n}");
                b.Execute("print 1.");
                b.Execute("if true {\nprint 2.\n}");
                a.Execute("f().");
                Assert.AreEqual(1, a.Errors.Count);
                Assert.AreEqual("print nosuchvariable.", a.InterpreterSourceLines.Last());
                b.AssertNoErrors();
            }
        }

        /// <summary>The prompt's row as the screen shows it, trailing blanks trimmed.</summary>
        private static string CursorRowText(ScriptHost host)
        {
            return host.Screen.GetBuffer()[host.Screen.CursorRowShow].ToString().Replace('\0', ' ').TrimEnd();
        }

        [Test]
        public void ASearchKeepsItsMatchWhenTheOtherPromptAddsCommands()
        {
            var list = new CommandHistoryList(3);
            using (var a = new ScriptHost(10, 50))
            using (var b = new ScriptHost(10, 50))
            {
                a.Screen.HistoryList = list;
                b.Screen.HistoryList = list;
                a.Execute("print \"x\".");
                a.Execute("print \"y\".");
                a.Execute("set s to 1.");
                a.AssertNoErrors();
                a.Screen.SpecialKey(CommandLineEditor.SEARCH_KEY);
                a.Type("print");
                Assert.AreEqual("(search) 'print': print \"y\".", CursorRowText(a));
                b.Execute(" print 9."); // not recorded
                b.Execute("print 9."); // drops print "x". and moves every index
                a.Screen.SpecialKey(CommandLineEditor.SEARCH_KEY);
                Assert.AreEqual("(search failed) 'print': print \"y\".", CursorRowText(a), "the same match; nothing older is left");
                a.Screen.Type('\t');
                Assert.AreEqual("print \"y\".", a.Screen.InputLine, "the match shown is the one taken");
                a.Screen.SpecialKey(UP);
                Assert.AreEqual("print \"y\".", a.Screen.InputLine, "and Up goes on from it: nothing older");
                a.Screen.SpecialKey(DOWN);
                Assert.AreEqual("set s to 1.", a.Screen.InputLine);
            }
        }

        [Test]
        public void ADroppedMatchIsNoMatch()
        {
            var list = new CommandHistoryList(2);
            using (var a = new ScriptHost(10, 50))
            using (var b = new ScriptHost(10, 50))
            {
                a.Screen.HistoryList = list;
                b.Screen.HistoryList = list;
                a.Execute("set s to 1.");
                a.Execute("print 1.");
                a.Screen.SpecialKey(CommandLineEditor.SEARCH_KEY);
                a.Type("set");
                Assert.AreEqual("(search) 'set': set s to 1.", CursorRowText(a));
                b.Execute("print 2."); // drops set s to 1.
                a.Type("\n");
                Assert.IsFalse(a.Screen.IsSearchingHistory, "Enter without a match leaves the search");
                Assert.AreEqual("", a.Screen.InputLine, "and runs nothing");
                a.RunUntilPrompt();
                a.AssertNoErrors();
                CollectionAssert.AreEqual(new[] { "print 1.", "print 2." }, list.Commands.ToArray());
            }
        }

        [Test]
        public void ANewListSavesTheOldAndLeavesASearch()
        {
            var store = new MemoryStore();
            using (var host = new ScriptHost(10, 40))
            {
                host.Screen.HistoryClock = () => TimeSpan.FromSeconds(100);
                host.Screen.HistoryStore = store;
                host.Execute("print 1.");
                host.Execute("print 2."); // waits for the save interval
                host.Screen.SpecialKey(CommandLineEditor.SEARCH_KEY);
                var shared = new CommandHistoryList();
                host.Screen.HistoryList = shared;
                CollectionAssert.AreEqual(new[] { "print 1.", "print 2." }, store.Saved);
                Assert.IsFalse(host.Screen.IsSearchingHistory);
                Assert.AreSame(shared, host.Screen.HistoryList);
                host.Screen.SpecialKey(UP);
                Assert.AreEqual("", host.Screen.InputLine, "the shared list's commands only");
                host.Screen.HistoryList = shared; // the same: nothing changes
                Assert.Throws<ArgumentNullException>(() => host.Screen.HistoryList = null);
            }
        }

        [Test]
        public void TheSavesFileThroughTwoPrompts()
        {
            string directory = Path.Combine(Path.GetTempPath(), "kosim-shared-history-" + Guid.NewGuid().ToString("N"));
            string file = Path.Combine(directory, "kOS", CpuFilePrune.SHARED_HISTORY_FILE);
            try
            {
                var share = new SharedCommandHistory(path => new CommandHistoryFile(() => path));
                CommandHistoryList list = share.Acquire(file);
                using (var a = new ScriptHost(10, 40))
                using (var b = new ScriptHost(10, 40))
                {
                    a.Screen.HistoryList = list;
                    b.Screen.HistoryList = share.Acquire(file);
                    a.Execute("print 1.");
                    b.Execute("print 2.");
                    a.Execute("print 3.");
                    share.Release(b.Screen.HistoryList);
                    share.Release(a.Screen.HistoryList);
                }
                Assert.AreEqual(CommandHistoryFile.HEADER + "\nprint 1.\nprint 2.\nprint 3.\n", File.ReadAllText(file));
                using (var c = new ScriptHost(10, 40))
                {
                    c.Screen.HistoryList = share.Acquire(file);
                    c.Screen.SpecialKey(UP);
                    Assert.AreEqual("print 3.", c.Screen.InputLine, "the next flight reads it back");
                }
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }
    }
}
