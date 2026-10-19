using System.Linq;
using kOS.Safe.Screen;
using kOS.Safe.Test.Execution;
using kOS.Safe.UserIO;
using NUnit.Framework;

namespace kOS.Safe.Test.Terminal
{
    /// <summary>
    /// Phase 3, P3-1: command history (TERM-05 draft, bounded history, interpreter line
    /// numbers for error messages). Unit tests on <see cref="CommandHistory"/>, then the
    /// prompt keys on the real <see cref="TextEditor"/> through <see cref="FixtureInterpreter"/>.
    /// </summary>
    [TestFixture]
    public class CommandHistoryTest
    {
        private static string Move(CommandHistory history, int delta, string current = "", int cursor = 0)
        {
            string text;
            int newCursor;
            Assert.IsTrue(history.TryMove(delta, current, cursor, out text, out newCursor), "move " + delta);
            return text + "|" + newCursor;
        }

        private static bool CanMove(CommandHistory history, int delta)
        {
            string text;
            int cursor;
            return history.TryMove(delta, "draft", 1, out text, out cursor);
        }

        [Test]
        public void EmptyHistoryDoesNothing()
        {
            var history = new CommandHistory();
            Assert.IsFalse(CanMove(history, -1));
            Assert.IsFalse(CanMove(history, 1));
            Assert.IsFalse(history.IsRecalling);
        }

        [Test]
        public void DraftComesBackWithItsCursor()
        {
            var history = new CommandHistory();
            history.Add("print 1.");
            history.Add("print 2.");

            Assert.AreEqual("print 2.|8", Move(history, -1, "lis", 2));
            Assert.AreEqual("print 1.|8", Move(history, -1, "print 2.", 8));
            Assert.AreEqual("print 2.|8", Move(history, 1, "print 1.", 8));
            Assert.AreEqual("lis|2", Move(history, 1, "print 2.", 8));
            Assert.IsFalse(history.IsRecalling);
        }

        [Test]
        public void OldestAndNewestAreLimits()
        {
            var history = new CommandHistory();
            history.Add("a.");
            Assert.IsFalse(CanMove(history, 1), "nothing newer than the draft");
            Move(history, -1);
            Assert.IsFalse(CanMove(history, -1), "nothing older than the oldest");
            Assert.IsTrue(history.IsRecalling, "a refused move stays put");
        }

        [Test]
        public void EditsToARecalledCommandAreNotKept()
        {
            var history = new CommandHistory();
            history.Add("a.");
            history.Add("b.");
            Move(history, -1, "draft", 5);
            Move(history, -1, "b. changed", 10);
            Assert.AreEqual("b.|2", Move(history, 1, "a. changed", 10));
            Assert.AreEqual("draft|5", Move(history, 1, "b.", 2), "only the draft is kept");
        }

        [Test]
        public void EnterStartsAFreshDraft()
        {
            var history = new CommandHistory();
            history.Add("a.");
            Move(history, -1, "draft", 5);
            history.Add("a."); // ran the recalled command
            Assert.IsFalse(history.IsRecalling);
            Assert.AreEqual("a.|2", Move(history, -1, "", 0));
            Assert.AreEqual("|0", Move(history, 1, "a.", 2), "the old draft is gone");
        }

        [Test]
        public void EmptyAndRepeatedCommandsAddNoEntry()
        {
            var history = new CommandHistory();
            history.Add("a.");
            history.Add("a.");
            history.Add(string.Empty); // Enter on an empty prompt, or Ctrl-C
            Assert.AreEqual(1, history.Count);
        }

        [Test]
        public void HistoryIsBoundedAndKeepsLineNumbers()
        {
            Assert.AreEqual(500, CommandHistory.MAX_ENTRIES);
            var history = new CommandHistory(3);
            int first = history.Add("one.");
            history.Add("two.");
            history.Add("three.");
            int fourth = history.Add("four.");
            Assert.AreEqual(3, history.Count);
            Assert.AreEqual(CommandHistory.TRIMMED_LINE, history.GetLine(first), "a dropped command");
            Assert.AreEqual("four.", history.GetLine(fourth), "numbers do not shift");
            Assert.AreEqual("two.", history.GetLine(fourth - 2));

            Move(history, -1);
            Move(history, -1);
            Assert.AreEqual("two.|4", Move(history, -1));
            Assert.IsFalse(CanMove(history, -1));
        }

        [Test]
        public void EachLineOfACommandHasItsOwnNumber()
        {
            var history = new CommandHistory();
            Assert.AreEqual(1, history.Add("if true {\nprint x.\n}"));
            Assert.AreEqual(4, history.Add("print y."));
            Assert.AreEqual("print x.", history.GetLine(2));
            Assert.AreEqual("}", history.GetLine(3));
            Assert.AreEqual("print y.", history.GetLine(4));
            Assert.AreEqual(5, history.Add("if true {\nprint x.\n}"), "a repeat of an older command is a new entry");
            Assert.AreEqual(CommandHistory.TRIMMED_LINE, history.GetLine(0));
        }

        [Test]
        public void ClearRestartsTheNumbers()
        {
            var history = new CommandHistory();
            history.Add("a.");
            history.Add("b.");
            history.Clear();
            Assert.AreEqual(0, history.Count);
            Assert.AreEqual(1, history.Add("c."));
        }

        // The prompt, through the fixture copy of the game's Interpreter:

        [Test]
        public void PromptRestoresTheDraftWithoutRunningAnything()
        {
            using (var host = new ScriptHost(10, 40))
            {
                host.Execute("print 1.");
                host.Type("print 4");
                host.Screen.SpecialKey((char)UnicodeCommand.LEFTCURSORONE);
                host.Screen.SpecialKey((char)UnicodeCommand.LEFTCURSORONE);
                string before = host.Transcript();

                host.Screen.SpecialKey((char)UnicodeCommand.UPCURSORONE);
                Assert.AreEqual("print 1.", host.Screen.InputLine);
                Assert.AreEqual(8, host.Screen.InputCursor);
                host.Screen.SpecialKey((char)UnicodeCommand.DOWNCURSORONE);
                Assert.AreEqual("print 4", host.Screen.InputLine);
                Assert.AreEqual(5, host.Screen.InputCursor);
                host.Screen.SpecialKey((char)UnicodeCommand.DOWNCURSORONE);
                Assert.AreEqual("print 4", host.Screen.InputLine, "Down at the draft does nothing");

                host.RunUntilPrompt();
                Assert.AreEqual(before, host.Transcript(), "nothing ran");
                Assert.IsEmpty(host.Errors);
            }
        }

        [Test]
        public void PromptRecallsAMultilineCommand()
        {
            using (var host = new ScriptHost(10, 40))
            {
                host.Execute("if true {\nprint \"m\".\n}");
                host.Screen.SpecialKey((char)UnicodeCommand.UPCURSORONE);
                Assert.AreEqual("if true {\nprint \"m\".\n}", host.Screen.InputLine);
            }
        }

        /// <summary>
        /// Upstream numbered interpreter lines by command, so line 2 of the newest command
        /// was looked up past the end of the history (the game showed "Cannot Show kOS Error
        /// Location"), and line 2 of an older command showed the next command.
        /// </summary>
        [Test]
        public void ErrorOnTheSecondLineOfACommandShowsThatLine()
        {
            using (var host = new ScriptHost(10, 40))
            {
                host.Execute("function f {\nprint nosuchvariable.\n}");
                host.Execute("print 1.");
                host.Execute("f().");
                Assert.AreEqual(1, host.Errors.Count, string.Join("\n", host.Errors.Select(e => e.Message).ToArray()));
                Assert.AreEqual("print nosuchvariable.", LastSourceLine(host));

                host.Execute("if true {\nprint nosuchvariable.\n}");
                Assert.AreEqual(2, host.Errors.Count);
                Assert.AreEqual("print nosuchvariable.", LastSourceLine(host));
            }
        }

        private static string LastSourceLine(ScriptHost host)
        {
            return host.InterpreterSourceLines[host.InterpreterSourceLines.Count - 1];
        }
    }
}
