using System;
using System.Linq;
using kOS.Safe.Screen;
using kOS.Safe.Test.Execution;
using kOS.Safe.Test.UserIO;
using kOS.Safe.UserIO;
using NUnit.Framework;

namespace kOS.Safe.Test.Terminal
{
    /// <summary>
    /// Phase 3, history search (decisions H1/H2): Ctrl+R on the prompt line, readline-style.
    /// Unit tests on <see cref="CommandHistory"/>, then the keys on the real
    /// <see cref="CommandLineEditor"/> through <see cref="FixtureInterpreter"/>, then telnet
    /// end to end (<see cref="FixtureTermWindow"/>).
    /// </summary>
    [TestFixture]
    public class HistorySearchTest
    {
        private const char CTRL_R = CommandLineEditor.SEARCH_KEY;
        private const char ESC = CommandLineEditor.CANCEL_KEY;
        private const char BREAK = (char)UnicodeCommand.BREAK;
        private const char LEFT = (char)UnicodeCommand.LEFTCURSORONE;
        private const char UP = (char)UnicodeCommand.UPCURSORONE;
        private const char DOWN = (char)UnicodeCommand.DOWNCURSORONE;

        private static CommandHistory HistoryOf(params string[] commands)
        {
            var history = new CommandHistory();
            foreach (string command in commands)
                history.Add(command);
            return history;
        }

        private static int Find(CommandHistory history, string query, int start, string skip = null)
        {
            int index;
            return history.TryFindOlder(query, start, skip, out index) ? index : -1;
        }

        [Test]
        public void FindsTheNewestMatchIgnoringCase()
        {
            CommandHistory history = HistoryOf("print ship:name.", "list files.", "PRINT 2.", "stage.");
            Assert.AreEqual(2, Find(history, "print", 3));
            Assert.AreEqual(0, Find(history, "print", 1), "from an older start");
            Assert.AreEqual(0, Find(history, "Ship:", 99), "a start past the newest is the newest");
            Assert.AreEqual(-1, Find(history, "nothing", 3));
            Assert.AreEqual(-1, Find(history, "print", -1));
        }

        [Test]
        public void SkipsTheMatchAlreadyShown()
        {
            CommandHistory history = HistoryOf("print 1.", "stage.", "print 1.", "print 2.");
            Assert.AreEqual(2, Find(history, "", 3, "print 2."), "an empty query matches anything else");
            Assert.AreEqual(0, Find(history, "print", 1, "print 2.")); // start below index 2
            Assert.AreEqual(-1, Find(history, "print 1", 2, "print 1."), "an equal older command is skipped");
        }

        [Test]
        public void RecallKeepsTheDraftForDown()
        {
            CommandHistory history = HistoryOf("a.", "b.", "c.");
            history.Recall(0, "draft", 3);
            Assert.IsTrue(history.IsRecalling);
            string text;
            int cursor;
            Assert.IsTrue(history.TryMove(1, "a.", 2, out text, out cursor));
            Assert.AreEqual("b.", text);
            Assert.IsTrue(history.TryMove(1, "b.", 2, out text, out cursor));
            Assert.IsTrue(history.TryMove(1, "c.", 2, out text, out cursor));
            Assert.AreEqual("draft", text);
            Assert.AreEqual(3, cursor);
        }

        // The prompt, through the fixture (the shared CommandLineEditor):

        /// <summary>The rows the screen shows (input line included), trailing blanks trimmed.</summary>
        private static string[] Shown(ScriptHost host)
        {
            return host.Screen.GetBuffer().Select(row => row.ToString().Replace('\0', ' ').TrimEnd()).ToArray();
        }

        private static string CursorRowText(ScriptHost host)
        {
            return Shown(host)[host.Screen.CursorRowShow];
        }

        private static ScriptHost HostWithHistory()
        {
            var host = new ScriptHost(12, 50);
            host.Execute("set a to 1.");
            host.Execute("print \"first\".");
            host.Execute("set b to 2.");
            host.Execute("print \"second\".");
            host.Screen.ClearScreen();
            return host;
        }

        private static void Keys(ScriptHost host, string keys)
        {
            foreach (char key in keys)
            {
                if (key >= ' ' || key == '\r' || key == '\b' || key == '\t')
                    host.Screen.Type(key);
                else
                    host.Screen.SpecialKey(key);
            }
        }

        [Test]
        public void SearchShowsTheNewestMatchOnThePromptLine()
        {
            using (ScriptHost host = HostWithHistory())
            {
                host.Type("dra");
                host.Screen.SpecialKey(CTRL_R);
                Assert.IsTrue(host.Screen.IsSearchingHistory);
                Assert.AreEqual("(search) '':", CursorRowText(host));

                host.Type("print");
                Assert.AreEqual("(search) 'print': print \"second\".", CursorRowText(host));
                Assert.AreEqual("(search) 'print".Length, host.Screen.CursorColumnShow, "the cursor ends the query");
                Assert.AreEqual("dra", host.Screen.InputLine, "the line being edited is untouched");

                host.Screen.SpecialKey(CTRL_R);
                Assert.AreEqual("(search) 'print': print \"first\".", CursorRowText(host), "Ctrl+R again: the next older");
                host.Screen.SpecialKey(CTRL_R);
                Assert.AreEqual("(search failed) 'print': print \"first\".", CursorRowText(host), "none older");
            }
        }

        [Test]
        public void EnterRunsTheMatchAndRecordsIt()
        {
            using (ScriptHost host = HostWithHistory())
            {
                Keys(host, CTRL_R + "FIRST");
                host.Type("\n");
                host.RunUntilPrompt();
                host.AssertNoErrors();
                Assert.IsFalse(host.Screen.IsSearchingHistory);
                Assert.AreEqual("print \"first\".\nfirst", host.Transcript());
                Assert.AreEqual("", host.Screen.InputLine);

                host.Screen.SpecialKey(UP);
                Assert.AreEqual("print \"first\".", host.Screen.InputLine, "the newest command now");
            }
        }

        /// <summary>With BREAK, also nothing of the prompt's BREAK (which empties and ends the line).</summary>
        [TestCase(ESC, TestName = "LeavingTheSearchKeepsTheLine(Esc)")]
        [TestCase(BREAK, TestName = "LeavingTheSearchKeepsTheLine(BREAK)")]
        public void LeavingTheSearchKeepsTheLine(char key)
        {
            using (ScriptHost host = HostWithHistory())
            {
                host.Type("print 4");
                host.Screen.SpecialKey(LEFT);
                Keys(host, CTRL_R + "set");
                host.Screen.SpecialKey(key);

                Assert.IsFalse(host.Screen.IsSearchingHistory);
                Assert.AreEqual("print 4", host.Screen.InputLine);
                Assert.AreEqual(6, host.Screen.InputCursor);
                Assert.AreEqual("print 4", CursorRowText(host));
                host.RunUntilPrompt();
                Assert.AreEqual("", host.Transcript(), "nothing ran");
            }
        }

        [Test]
        public void CursorKeysTakeTheMatchForEditing()
        {
            using (ScriptHost host = HostWithHistory())
            {
                host.Type("draft");
                Keys(host, CTRL_R + "set a");
                host.Screen.SpecialKey(LEFT);
                Assert.IsFalse(host.Screen.IsSearchingHistory);
                Assert.AreEqual("set a to 1.", host.Screen.InputLine);
                Assert.AreEqual(10, host.Screen.InputCursor, "taken with the cursor at its end, then Left");
                Assert.AreEqual("set a to 1.", CursorRowText(host));

                host.Screen.SpecialKey(DOWN);
                Assert.AreEqual("print \"first\".", host.Screen.InputLine, "Down moves on from the match");
                host.Screen.SpecialKey(DOWN);
                host.Screen.SpecialKey(DOWN);
                host.Screen.SpecialKey(DOWN);
                Assert.AreEqual("draft", host.Screen.InputLine, "past the newest: the draft from before the search");
                host.RunUntilPrompt();
                Assert.AreEqual("", host.Transcript(), "nothing ran");
            }
        }

        [Test]
        public void TabTakesTheMatchWithoutTyping()
        {
            using (ScriptHost host = HostWithHistory())
            {
                Keys(host, CTRL_R + "set b\t");
                Assert.IsFalse(host.Screen.IsSearchingHistory);
                Assert.AreEqual("set b to 2.", host.Screen.InputLine);
            }
        }

        [Test]
        public void EnterWithoutAMatchOnlyLeavesTheSearch()
        {
            using (ScriptHost host = HostWithHistory())
            {
                host.Type("draft");
                Keys(host, CTRL_R + "zzz");
                Assert.AreEqual("(search failed) 'zzz':", CursorRowText(host));
                host.Type("\n");
                Assert.IsFalse(host.Screen.IsSearchingHistory);
                Assert.AreEqual("draft", host.Screen.InputLine);
                host.RunUntilPrompt();
                Assert.AreEqual("", host.Transcript(), "nothing ran");
            }
        }

        [Test]
        public void BackspaceSearchesAgainFromTheNewest()
        {
            using (ScriptHost host = HostWithHistory())
            {
                Keys(host, CTRL_R + "set ax");
                Assert.AreEqual("(search failed) 'set ax': set a to 1.", CursorRowText(host));
                host.Type("\b\b");
                Assert.AreEqual("(search) 'set ': set b to 2.", CursorRowText(host));
                host.Type("\b\b\b\b");
                Assert.AreEqual("(search) '':", CursorRowText(host));
                host.Type("\b");
                Assert.IsTrue(host.Screen.IsSearchingHistory, "Backspace on an empty query stays");
            }
        }

        [Test]
        public void CtrlRWithAnEmptyQueryUsesThePreviousOne()
        {
            using (ScriptHost host = HostWithHistory())
            {
                Keys(host, CTRL_R + "first" + ESC);
                Keys(host, CTRL_R.ToString() + CTRL_R);
                Assert.AreEqual("(search) 'first': print \"first\".", CursorRowText(host));
            }
        }

        [Test]
        public void AMultilineMatchShowsOnSeveralRows()
        {
            using (var host = new ScriptHost(12, 50))
            {
                host.Execute("if true {\nprint \"m\".\n}");
                host.Screen.ClearScreen();
                Keys(host, CTRL_R + "if");
                string[] rows = Shown(host);
                int row = host.Screen.CursorRowShow;
                Assert.AreEqual("(search) 'if': if true {", rows[row]);
                Assert.AreEqual("print \"m\".", rows[row + 1]);
                Assert.AreEqual("}", rows[row + 2]);
                host.Screen.SpecialKey(ESC);
                Assert.AreEqual("", Shown(host)[row + 1], "the rows are cleared on leaving");
            }
        }

        [Test]
        public void ResetEndsTheSearch()
        {
            using (ScriptHost host = HostWithHistory())
            {
                Keys(host, CTRL_R + "x");
                host.Screen.Reset();
                Assert.IsFalse(host.Screen.IsSearchingHistory);
            }
        }

        // Telnet end to end:

        private sealed class TelnetSession : IDisposable
        {
            public readonly ScriptHost Host = new ScriptHost();
            public readonly FixtureTermWindow Window;
            public readonly TelnetServerInputModel Telnet;
            private DateTime now = DateTime.Now;
            private TimeSpan clock;

            public TelnetSession()
            {
                Host.Boot();
                Window = new FixtureTermWindow(Host);
                Host.BeforeUpdate = () =>
                {
                    now += TimeSpan.FromMilliseconds(51); // every update refreshes the clients
                    clock += TimeSpan.FromMilliseconds(51); // two updates pass the sequence timeout: a lone ESC is a key
                    Telnet.Now = clock;
                    Window.Update(now);
                };
                Telnet = Window.AttachTelnet("XTERM");
            }

            public void Run(int updates = 5)
            {
                for (int i = 0; i < updates; ++i)
                    Host.Update();
                Host.RunUntilPrompt();
            }

            public string CursorRow()
            {
                return Telnet.Model.Row(Telnet.Model.CursorRow).TrimEnd();
            }

            public void Dispose() { Host.Dispose(); }
        }

        [Test]
        public void TelnetCtrlRSearchesAndNoLongerRepaints()
        {
            using (var session = new TelnetSession())
            {
                session.Telnet.Send("print \"first\".\r");
                session.Telnet.Send("print \"second\".\r");
                session.Run();
                int repaints = session.Window.RepaintRequests.ContainsKey(session.Telnet) ? session.Window.RepaintRequests[session.Telnet] : 0;

                session.Telnet.Send("\x12" + "fir");
                session.Run();
                Assert.IsTrue(session.Host.Screen.IsSearchingHistory);
                Assert.AreEqual("(search) 'fir': print \"first\".", session.CursorRow(), "what the client shows");
                Assert.AreEqual(repaints, session.Window.RepaintRequests.ContainsKey(session.Telnet) ? session.Window.RepaintRequests[session.Telnet] : 0,
                    "Ctrl-R is no repaint");

                session.Telnet.Send("\x04"); // Ctrl-D: no detach during a search
                session.Run();
                Assert.IsTrue(session.Window.IsAttached(session.Telnet));

                session.Telnet.Send("\r");
                session.Run();
                session.Host.AssertNoErrors();
                Assert.IsFalse(session.Host.Screen.IsSearchingHistory);
                StringAssert.EndsWith("print \"first\".\nfirst", session.Host.Transcript());
            }
        }

        [Test]
        public void TelnetEscapeLeavesTheSearch()
        {
            using (var session = new TelnetSession())
            {
                session.Telnet.Send("print 1.\r");
                session.Run();
                session.Telnet.Send("dr\x12" + "pr");
                session.Run();
                session.Telnet.Send("\x1b");
                session.Run();
                Assert.IsFalse(session.Host.Screen.IsSearchingHistory);
                Assert.AreEqual("dr", session.Host.Screen.InputLine);
            }
        }

        [Test]
        public void DecoderPassesCtrlROnAndKeepsCtrlL()
        {
            Assert.AreEqual(((char)UnicodeCommand.REQUESTREPAINT).ToString(), new TerminalInputDecoder(TerminalInputDialect.Xterm).Decode("\x0c"));
            foreach (TerminalInputDialect dialect in new[] { TerminalInputDialect.Plain, TerminalInputDialect.VT100, TerminalInputDialect.Xterm })
                Assert.AreEqual("\x12", new TerminalInputDecoder(dialect).Decode("\x12"), dialect.ToString());
        }
    }
}
