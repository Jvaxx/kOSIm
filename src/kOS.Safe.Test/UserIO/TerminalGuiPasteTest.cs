using System;
using System.Linq;
using kOS.Safe.Test.Execution;
using kOS.Safe.UserIO;
using NUnit.Framework;

namespace kOS.Safe.Test.UserIO
{
    /// <summary>
    /// Phase 3, P3-3 (TERM-01): the GUI terminal's paste, <see cref="TerminalGuiPaste{TClient}"/>,
    /// through the real router over the real prompt. The keys that call it (Ctrl+V,
    /// Shift+Insert) and the confirmation window are Unity code, checked in game.
    /// </summary>
    [TestFixture]
    public class TerminalGuiPasteTest
    {
        private ScriptHost host;
        private FixtureTermWindow window;

        private TerminalGuiPaste<TelnetServerInputModel> Paste { get { return window.GuiPaste; } }

        [SetUp]
        public void StartTerminal()
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

        private void Updates(int count)
        {
            for (int i = 0; i < count; ++i)
                host.Update();
        }

        [Test]
        public void OneLineIsTypedAtOnceAndRunsNothing()
        {
            Assert.AreEqual(GuiPasteResult.Queued, Paste.Paste("print \"one\"."));
            Assert.IsFalse(Paste.IsAwaitingConfirmation);
            Assert.AreEqual(1, window.KeyClicks);
            Updates(3);
            Assert.AreEqual("print \"one\".", host.Screen.InputLine);
            StringAssert.DoesNotContain("one", host.Transcript(), "nothing ran");
        }

        [Test]
        public void ALineBreakWaitsForConfirmationThenRunsEachLine()
        {
            Assert.AreEqual(GuiPasteResult.AwaitingConfirmation, Paste.Paste("print \"a\".\r\nprint \"b\".\n"));
            Assert.IsTrue(Paste.IsAwaitingConfirmation);
            Updates(5);
            Assert.AreEqual(0, window.TypeAhead.Count, "nothing is queued before confirming");
            Assert.AreEqual("", host.Screen.InputLine);
            Assert.AreEqual(0, window.KeyClicks);

            Assert.IsTrue(Paste.Confirm());
            Assert.IsFalse(Paste.IsAwaitingConfirmation);
            Assert.AreEqual(1, window.KeyClicks);
            RunPaste();
            CollectionAssert.AreEqual(new[] { "a", "b" }, host.Transcript().Split('\n').Where(l => l.Length == 1).ToArray());
            host.AssertNoErrors();
        }

        [Test]
        public void ALineBreakAloneAsksToo()
        {
            Assert.AreEqual(GuiPasteResult.AwaitingConfirmation, Paste.Paste("print \"x\".\n"));
            Assert.AreEqual("Run 1 line", Paste.ConfirmLabel);
        }

        [Test]
        public void CancelTypesNothing()
        {
            host.Type("draft");
            Paste.Paste("print \"never\".\nprint \"never\".\n");
            Paste.Cancel();
            Assert.IsFalse(Paste.IsAwaitingConfirmation);
            Assert.IsFalse(Paste.Confirm(), "nothing left to confirm");
            Updates(5);
            Assert.AreEqual(0, window.TypeAhead.Count);
            Assert.AreEqual(0, window.KeyClicks);
            Assert.AreEqual("draft", host.Screen.InputLine, "the line being typed is kept");
            StringAssert.DoesNotContain("never", host.Transcript());
        }

        [Test]
        public void APasteWhileOneWaitsDoesNothing()
        {
            Paste.Paste("print \"first\".\n");
            int reads = 0;
            Assert.AreEqual(GuiPasteResult.Nothing, Paste.Paste(() => { ++reads; return "print \"second\".\n"; }));
            Assert.AreEqual(0, reads, "the clipboard is not read");
            Assert.AreEqual(GuiPasteResult.Nothing, Paste.Paste("x"));
            Paste.Confirm();
            RunPaste();
            StringAssert.Contains("first", host.Transcript());
            StringAssert.DoesNotContain("second", host.Transcript());
            Assert.AreEqual("", host.Screen.InputLine);
        }

        [Test]
        public void TheConfirmedPasteWaitsForABusyPromptAndKeepsTheOrder()
        {
            host.Type("wait 0.2.\n");
            host.Update();
            Assert.IsFalse(host.Screen.IsWaitingForCommand());
            window.ProcessOneInputChar('p', null, true, true); // typed ahead before the paste
            Paste.Paste("rint \"x\".\nprint \"y\".\n");
            Paste.Confirm();
            RunPaste();
            CollectionAssert.AreEqual(new[] { "x", "y" }, host.Transcript().Split('\n').Where(l => l.Length == 1).ToArray());
        }

        [Test]
        public void ATooLargePasteIsRefusedWithAMessageAboveTheLineBeingTyped()
        {
            host.Type("print \"draft\".");
            Assert.AreEqual(GuiPasteResult.Refused, Paste.Paste(new string('a', TerminalPaste.MAX_CHARS + 1)));
            Assert.IsFalse(Paste.IsAwaitingConfirmation);
            Assert.AreEqual(0, window.TypeAhead.Count);
            Assert.AreEqual(0, window.KeyClicks);
            Assert.AreEqual("print \"draft\".", host.Screen.InputLine);

            string[] rows = host.Snapshot().Split('\n');
            Assert.AreEqual("|Paste refused: 65,537 characters, over the limit of 65,536. |", rows[0]);
            Assert.AreEqual("|Nothing was typed." + new string(' ', 60 - 18) + "|", rows[1]);
            Assert.AreEqual("|print \"draft\"." + new string(' ', 60 - 14) + "|", rows[2], "the line being typed, below the message");
            Assert.AreEqual("cursor 2,14", rows[rows.Length - 1]);

            host.Type("\n");
            host.RunUntilPrompt();
            CollectionAssert.Contains(host.Transcript().Split('\n'), "draft", "it still runs");
        }

        [Test]
        public void TheMessageKeepsALongLineBeingTyped()
        {
            string draft = "print \"" + new string('d', 70) + "\"";
            host.Type(draft);
            Paste.Paste(new string('a', TerminalPaste.MAX_CHARS + 1));
            string[] rows = host.Snapshot().Split('\n');
            Assert.AreEqual("|" + draft.Substring(0, 60) + "|", rows[2]);
            Assert.AreEqual("|" + draft.Substring(60).PadRight(60) + "|", rows[3]);
            Assert.AreEqual(draft, host.Screen.InputLine);
        }

        [Test]
        public void TheLimitItselfIsAccepted()
        {
            string largest = new string('a', TerminalPaste.MAX_CHARS - 2) + "\r\n";
            Assert.AreEqual(GuiPasteResult.AwaitingConfirmation, Paste.Paste(largest));
            Assert.IsTrue(Paste.Confirm());
            Assert.AreEqual(TerminalPaste.MAX_CHARS - 1, window.TypeAhead.Count);
        }

        [Test]
        public void AClipboardThatCannotBeReadIsReported()
        {
            Assert.AreEqual(GuiPasteResult.Refused, Paste.Paste(() => { throw new InvalidOperationException("no clipboard"); }));
            StringAssert.StartsWith("Paste failed: the clipboard could not be read (InvalidOperationException).", host.Transcript().Replace("\n", ""));
            Assert.AreEqual(0, window.TypeAhead.Count);
            Assert.Throws<OutOfMemoryException>(() => Paste.Paste(() => { throw new OutOfMemoryException(); }), "fatal errors are not caught");
        }

        [Test]
        public void NothingToPasteDoesNothing()
        {
            Assert.AreEqual(GuiPasteResult.Nothing, Paste.Paste((string)null));
            Assert.AreEqual(GuiPasteResult.Nothing, Paste.Paste(""));
            Assert.AreEqual(GuiPasteResult.Nothing, Paste.Paste("\x03\x04"));
            Assert.AreEqual(GuiPasteResult.Nothing, Paste.Paste(() => null));
            Assert.AreEqual(0, window.KeyClicks);
            Assert.AreEqual("", host.Transcript(), "no message");
        }

        [Test]
        public void PastedControlCharactersCannotConfirmOrBreak()
        {
            host.Type("wait 1000.\n");
            Updates(2);
            Paste.Paste("\x03" + (char)UnicodeCommand.BREAK + "x");
            Updates(5);
            Assert.IsFalse(host.Screen.IsWaitingForCommand(), "the wait still runs");
            Assert.AreEqual(1, window.TypeAhead.Count);
        }

        [Test]
        public void TheQuestionDescribesThePaste()
        {
            Paste.Paste("a\nb\nc");
            Assert.AreEqual("Run 2 lines", Paste.ConfirmLabel);
            Assert.AreEqual("This paste runs 2 lines as commands.\nIts last line is typed without running.", Paste.Question);
            Assert.AreEqual("a\nb\nc", Paste.Preview);
            Paste.Cancel();
            Assert.AreEqual("", Paste.Question);
            Assert.AreEqual("", Paste.Preview);
            Assert.AreEqual("", Paste.ConfirmLabel);

            Paste.Paste("x\x07\ty\r\n");
            Assert.AreEqual("This paste runs 1 line as commands.\n1 control character left out.", Paste.Question);
            Assert.AreEqual("x y", Paste.Preview, "a tab shows as a space");
        }

        [Test]
        public void ThePreviewShowsTheFirstLinesShortened()
        {
            string longLine = new string('L', 100);
            string text = longLine + "\n" + string.Concat(Enumerable.Range(1, 2000).Select(i => "print " + i + ".\n").ToArray());
            Paste.Paste(text);
            Assert.AreEqual("Run 2,001 lines", Paste.ConfirmLabel);
            string[] preview = Paste.Preview.Split('\n');
            Assert.AreEqual(TerminalGuiPaste<TelnetServerInputModel>.PREVIEW_LINES + 1, preview.Length);
            Assert.AreEqual(new string('L', 57) + "...", preview[0]);
            Assert.AreEqual("print 5.", preview[5]);
            Assert.AreEqual("(and 1,995 more lines)", preview[6]);
        }
    }
}
