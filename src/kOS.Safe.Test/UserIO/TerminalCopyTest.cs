using System.Collections.Generic;
using System.Linq;
using kOS.Safe.Exceptions;
using kOS.Safe.Screen;
using kOS.Safe.Test.Execution;
using kOS.Safe.UserIO;
using NUnit.Framework;

namespace kOS.Safe.Test.UserIO
{
    /// <summary>
    /// Phase 3, P3-4: what the GUI terminal copies, on the real screen and prompt. The keys
    /// (Ctrl+Shift+C, Ctrl+Shift+E), the clipboard and the notice are Unity code, checked in game.
    /// </summary>
    [TestFixture]
    public class TerminalCopyTest
    {
        private static List<string> ViewRows(IScreenBuffer screen)
        {
            return screen.GetBuffer().Select(l => l.ToString().Replace('\0', ' ').TrimEnd(' ')).ToList();
        }

        [Test]
        public void TheRowsInViewAreCopiedTrimmed()
        {
            using (var host = new ScriptHost(6, 20))
            {
                host.Boot();
                host.Screen.Print("first   ");
                host.Screen.PrintAt("x", 0, 10); // cells 8 and 9 were never written (NUL)
                host.Screen.Print("  indented");
                host.Screen.Print(" "); // a blank row ("" prints no row)
                host.Screen.Print("end");
                Assert.AreEqual("first     x\n  indented\n\nend", TerminalCopy.VisibleText(host.Screen));
                Assert.AreEqual(4, TerminalCopy.LineCount(TerminalCopy.VisibleText(host.Screen)));
            }
        }

        [Test]
        public void ABlankScreenCopiesNothing()
        {
            using (var host = new ScriptHost(6, 20))
            {
                host.Boot();
                Assert.AreEqual("", TerminalCopy.VisibleText(host.Screen));
                Assert.AreEqual(0, TerminalCopy.LineCount(""));
                Assert.AreEqual(0, TerminalCopy.LineCount(null));
            }
        }

        [Test]
        public void AScrolledBackViewIsWhatIsCopied()
        {
            using (var host = new ScriptHost(10, 40))
            {
                host.Boot();
                for (int i = 0; i < 30; ++i)
                    host.Screen.Print("line " + i);
                host.Screen.SpecialKey((char)UnicodeCommand.PAGEUPCURSOR);
                Assert.AreEqual(10, host.Screen.ViewOffset);
                string copied = TerminalCopy.VisibleText(host.Screen);
                Assert.AreEqual(string.Join("\n", Enumerable.Range(11, 10).Select(i => "line " + i).ToArray()), copied);
                Assert.AreEqual(10, host.Screen.ViewOffset, "copying leaves the view where it is");
                Assert.AreEqual(string.Join("\n", ViewRows(host.Screen).ToArray()), copied, "as drawn");
            }
        }

        [Test]
        public void TheLineBeingTypedIsCopiedAsShown()
        {
            using (var host = new ScriptHost(6, 20))
            {
                host.Boot();
                host.Execute("print \"out\".");
                host.Type("print \"a long line being typed\"");
                Assert.AreEqual("print \"out\".\nout\nprint \"a long line b\neing typed\"", TerminalCopy.VisibleText(host.Screen));
                Assert.AreEqual("print \"a long line being typed\"", host.Screen.InputLine, "copying changes nothing");
            }
        }

        [Test]
        public void AFixedOverlayIsCopiedWhereItIsDrawn()
        {
            using (var host = new ScriptHost(4, 10))
            {
                host.Boot();
                host.Screen.Print("under");
                var overlay = new SubBuffer { Fixed = true, Enabled = true, PositionRow = 3 };
                overlay.SetSize(1, 10);
                overlay.Buffer[0].ArrayCopyFrom("[overlay]".ToCharArray(), 0, 0);
                host.Screen.AddSubBuffer(overlay);
                Assert.AreEqual("under\n\n\n[overlay]", TerminalCopy.VisibleText(host.Screen));
            }
        }

        /// <summary>
        /// COPY-01, found in game: a syntax error's verbose description marks the bad token
        /// with chars 26 and 27, drawn as nothing; the last error copy left them in (^Z^[ in an
        /// editor). The screen does not keep them, so a copy of the rows has none either.
        /// </summary>
        [Test]
        public void ControlCharactersOnTheScreenAreNotCopied()
        {
            using (var host = new ScriptHost(12, 50))
            {
                host.Boot();
                host.Execute("print 1 2.");
                Assert.AreEqual(1, host.Errors.Count);
                string verbose = ((KOSException)host.Errors[0]).VerboseMessage;
                string marked = verbose.Split('\n').Last(l => l.StartsWith("print 1"));
                // both marks before the token (TinyPG gives it no length), as seen in game
                Assert.AreEqual("print 1 [26][27]2.", string.Concat(marked.Select(ch => ch < ' ' ? "[" + (int)ch + "]" : ch.ToString()).ToArray()),
                    "upstream marks the token");
                host.Screen.Print("clear");
                host.Screen.Print(verbose);
                string copied = TerminalCopy.VisibleText(host.Screen);
                StringAssert.Contains("print 1 2.", copied);
                Assert.IsFalse(copied.Any(ch => ch < ' ' && ch != '\n' && ch != '\t'), copied);
            }
        }

        [Test]
        public void ControlCharactersInAnErrorAreNotCopied()
        {
            var recorder = new PrintedErrorRecorder();
            recorder.Begin();
            recorder.Printed("Error Column: 9\nprint 1 \u001a2\u001b.\u0007\r");
            recorder.Printed("At interpreter\tline 1");
            recorder.End();
            Assert.AreEqual("Error Column: 9\nprint 1 2.\nAt interpreter\tline 1", recorder.Text);
            Assert.AreEqual(null, TerminalCopy.WithoutControlCharacters(null));
            Assert.AreEqual("", TerminalCopy.WithoutControlCharacters(""));
        }

        // ---- The last error, as printed ----

        [Test]
        public void AnErrorIsKeptAsPrinted()
        {
            var recorder = new PrintedErrorRecorder();
            Assert.IsNull(recorder.Text);
            recorder.Begin();
            recorder.Printed("Undefined Variable Name 'x'.");
            recorder.Printed("At interpreter, line 3\r\nprint x.\n      ^\n");
            recorder.End();
            Assert.AreEqual("Undefined Variable Name 'x'.\nAt interpreter, line 3\nprint x.\n      ^", recorder.Text);
        }

        [Test]
        public void TextPrintedOutsideAnErrorIsNotKept()
        {
            var recorder = new PrintedErrorRecorder();
            recorder.Printed("Program ended.");
            recorder.End();
            Assert.IsNull(recorder.Text);

            recorder.Begin();
            recorder.Printed("first error");
            recorder.End();
            recorder.Printed("a warning afterwards");
            Assert.AreEqual("first error", recorder.Text);
        }

        [Test]
        public void TheNewestErrorReplacesTheLastButAnEmptyOneDoesNot()
        {
            var recorder = new PrintedErrorRecorder();
            recorder.Begin();
            recorder.Printed("old");
            recorder.End();
            recorder.Begin();
            recorder.Printed("");
            recorder.Printed(null);
            recorder.End();
            Assert.AreEqual("old", recorder.Text);
            recorder.Begin();
            recorder.Printed("new");
            recorder.End();
            Assert.AreEqual("new", recorder.Text);
        }
    }
}
