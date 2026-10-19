using System.Linq;
using kOS.Safe.Test.Execution;
using kOS.Safe.UserIO;
using NUnit.Framework;

namespace kOS.Safe.Test.UserIO
{
    /// <summary>
    /// Phase 3, P3-2 (TERM-01): clipboard text normalized by <see cref="TerminalPaste"/>, then
    /// queued by the real router over the real prompt.
    /// </summary>
    [TestFixture]
    public class TerminalPasteTest
    {
        [Test]
        public void EachLineBreakIsOneEnter()
        {
            TerminalPaste paste = TerminalPaste.Prepare("a\r\nb\rc\nd\r\n\r\n");
            Assert.AreEqual("a\rb\rc\rd\r\r", paste.Keys);
            Assert.AreEqual(5, paste.EnterCount);
            Assert.IsTrue(paste.RunsCommands);
        }

        [Test]
        public void OneLineRunsNothing()
        {
            TerminalPaste paste = TerminalPaste.Prepare("print 1.");
            Assert.AreEqual("print 1.", paste.Keys);
            Assert.IsFalse(paste.RunsCommands);
        }

        [Test]
        public void ControlCharactersAndKeyCodesAreDropped()
        {
            string keyCodes = new string(new[] { (char)UnicodeCommand.BREAK, (char)UnicodeCommand.RESIZESCREEN, (char)UnicodeCommand.REQUESTREPAINT, '', '�' });
            TerminalPaste paste = TerminalPaste.Prepare("a\x03\x04\x18\x1b[A\x7f\x85\u0008b" + keyCodes + "\tc");
            Assert.AreEqual("a[Ab\tc", paste.Keys);
            Assert.AreEqual(7 + keyCodes.Length, paste.DroppedCount);
        }

        [Test]
        public void TextOutsideAsciiIsKept()
        {
            const string text = "print \"é Я ñ 日本 🚀\".";
            Assert.AreEqual(text, TerminalPaste.Prepare(text).Keys);
        }

        [Test]
        public void ATooLargePasteIsRefusedWhole()
        {
            Assert.AreEqual(65536, TerminalPaste.MAX_CHARS);
            TerminalPaste largest = TerminalPaste.Prepare(new string('a', TerminalPaste.MAX_CHARS));
            Assert.IsFalse(largest.IsTooLarge);
            Assert.AreEqual(TerminalPaste.MAX_CHARS, largest.Keys.Length);

            TerminalPaste tooLarge = TerminalPaste.Prepare(new string('a', TerminalPaste.MAX_CHARS + 1));
            Assert.IsTrue(tooLarge.IsTooLarge);
            Assert.IsTrue(tooLarge.IsEmpty, "never truncated");
            Assert.IsTrue(TerminalPaste.Prepare(null).IsEmpty);
        }

        // Through the router, over the real prompt:

        private ScriptHost host;
        private FixtureTermWindow window;

        private void StartTerminal()
        {
            host = new ScriptHost(24, 60);
            host.Boot();
            window = new FixtureTermWindow(host);
            host.BeforeUpdate = window.Update;
        }

        [TearDown]
        public void TearDown()
        {
            if (host != null)
                host.Dispose();
            host = null;
        }

        private void RunPaste()
        {
            host.RunUntil(() => window.TypeAhead.Count == 0 && host.Screen.IsWaitingForCommand());
            host.RunUntilPrompt();
        }

        [Test]
        public void AMultilinePasteRunsEachLineOnceInOrder()
        {
            StartTerminal();
            Assert.IsTrue(window.QueuePaste("print \"a\".\r\nwait 0.1. print \"b\".\nprint \"c\".\nprint \"d"));
            Assert.AreEqual(1, window.KeyClicks, "one click per paste");
            RunPaste();

            string[] lines = host.Transcript().Split('\n');
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, lines.Where(l => l.Length == 1).ToArray());
            Assert.AreEqual("print \"d", host.Screen.InputLine, "the last line, without a line break, waits at the prompt");
            host.AssertNoErrors();
        }

        [Test]
        public void APasteWaitsForABusyPrompt()
        {
            StartTerminal();
            host.Type("wait 0.2.\n");
            host.Update();
            Assert.IsFalse(host.Screen.IsWaitingForCommand());
            window.QueuePaste("print \"after\".\n");
            Assert.AreEqual(15, window.TypeAhead.Count);
            RunPaste();
            StringAssert.Contains("after", host.Transcript());
        }

        [Test]
        public void ARunningScriptReadsThePaste()
        {
            StartTerminal();
            host.Type("set s to \"\". until s:length = 4 { set s to s + terminal:input:getchar(). } print \"got [\" + s:replace(char(13), \"<CR>\") + \"]\".\n");
            host.RunUntilBlocked();
            window.QueuePaste("ab\ncd");
            RunPaste();
            StringAssert.Contains("got [ab<CR>c]", host.Transcript().Replace("\n", ""));
            Assert.AreEqual("d", host.Screen.InputLine, "the rest goes to the prompt afterwards");
        }

        [Test]
        public void BreakDropsWhatThePasteHasNotRun()
        {
            StartTerminal();
            window.QueuePaste("wait 1000.\nprint \"never\".\n");
            host.RunUntil(() => window.TypeAhead.Count == 15 && !host.Screen.IsWaitingForCommand()); // the wait runs; its next line is queued
            Assert.IsTrue(window.ProcessOneInputChar((char)UnicodeCommand.BREAK, null, true, true));
            Assert.AreEqual(0, window.TypeAhead.Count);
            host.RunUntilPrompt();
            StringAssert.DoesNotContain("never", host.Transcript());
        }

        [Test]
        public void PastedControlCharactersDoNotBreakOrDetach()
        {
            StartTerminal();
            host.Type("wait 1000.\n");
            host.Update();
            host.Update();
            window.QueuePaste("\x03\x04x");
            for (int i = 0; i < 5; ++i)
                host.Update();
            Assert.IsFalse(host.Screen.IsWaitingForCommand(), "the wait still runs");
            Assert.AreEqual(1, window.TypeAhead.Count);
            Assert.AreEqual(0, window.CloseRequests);
        }

        [Test]
        public void AKeyTypedDuringAPasteComesAfterIt()
        {
            StartTerminal();
            window.QueuePaste("print 1");
            Assert.IsTrue(window.ProcessOneInputChar('2', null, true, false), "a GUI key");
            Assert.IsTrue(window.ProcessOneInputChar((char)UnicodeCommand.LEFTCURSORONE, null, true, false));
            Assert.IsTrue(window.ProcessOneInputChar('3', null, true, false));
            window.Update();
            Assert.AreEqual("print 132", host.Screen.InputLine);
        }

        [Test]
        public void ALongPasteKeepsThePromptBudget()
        {
            StartTerminal();
            string paste = string.Concat(Enumerable.Range(0, 400).Select(i => "set x" + i + " to " + i + ".\n").ToArray());
            window.QueuePaste(paste);
            RunPaste();
            Assert.LessOrEqual(window.MaxPromptKeysPerUpdate, TerminalInputPump.PROMPT_CHARS_PER_UPDATE);
            host.Execute("print x0 + x399.");
            StringAssert.Contains("399", host.Transcript());
            host.AssertNoErrors();
        }

        [Test]
        public void ARefusedPasteQueuesNothing()
        {
            StartTerminal();
            Assert.IsFalse(window.QueuePaste(new string('a', TerminalPaste.MAX_CHARS + 1)));
            Assert.IsFalse(window.QueuePaste("\x03"), "nothing left");
            Assert.AreEqual(0, window.TypeAhead.Count);
            Assert.AreEqual(0, window.KeyClicks);
        }
    }
}
