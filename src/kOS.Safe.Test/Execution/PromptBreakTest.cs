using kOS.Safe.UserIO;
using NUnit.Framework;
using System.IO;
using System.Text;

namespace kOS.Safe.Test.Execution
{
    /// <summary>
    /// Ctrl-C for a command typed at the prompt (decision 4 of the review NET-03 work).
    /// Upstream, a manual break with only the interpreter context skipped the rest of the
    /// typed line but kept its yields, so <c>WAIT</c> and <c>COMPILE</c> typed at the
    /// prompt ran to the end. A compile thread cannot be stopped and the compiler is not
    /// thread-safe, so a compile still running is cancelled instead: the CPU waits for the
    /// thread and then discards its result (no file, no parts, no error).
    /// </summary>
    [TestFixture]
    public class PromptBreakTest
    {
        private const string Source = "print \"compiled\".\n";
        private const string BrokenSource = "print \"before\".\nprint (.\n";

        private static ScriptHost BootWith(string source)
        {
            var host = new ScriptHost();
            host.WriteArchiveBytes("brk/prog.ks", Encoding.UTF8.GetBytes(source));
            host.Boot();
            return host;
        }

        private static void PressCtrlC(ScriptHost host)
        {
            host.Screen.SpecialKey((char)UnicodeCommand.BREAK);
        }

        /// <summary>Type a command whose compile thread then waits at the gate.</summary>
        private static void TypeHeld(ScriptHost host, string command)
        {
            host.Compiler.Hold();
            host.Type(command + "\n");
            host.Update(); // the interpreter compiles the line
            host.Update(); // the CPU calls the function, which starts the compile thread
            Assert.IsTrue(host.Compiler.WaitUntilHeld(), "the compile thread did not start");
        }

        /// <summary>The prompt must stay busy while the cancelled compile thread runs.</summary>
        private static void AssertBusyFor(ScriptHost host, int updates)
        {
            for (int i = 0; i < updates; ++i)
            {
                host.Update();
                Assert.IsFalse(host.Screen.IsWaitingForCommand(), "the prompt took commands while a compile thread was still running");
            }
        }

        [Test]
        public void CtrlCEndsAWaitTypedAtThePrompt()
        {
            using (var host = BootWith(Source))
            {
                host.Type("wait 1000. print \"never\".\n");
                for (int i = 0; i < 10; ++i)
                    host.Update();
                Assert.IsFalse(host.Screen.IsWaitingForCommand(), "the wait did not start");

                PressCtrlC(host);
                host.RunUntilPrompt(50); // one second of game time, not 1000
                CollectionAssert.DoesNotContain(host.Transcript().Split('\n'), "never", "the rest of the line ran");
                Assert.AreEqual("after", host.Output("print \"after\"."));
                host.AssertNoErrors();
            }
        }

        [Test]
        public void CtrlCAtAnIdlePromptChangesNothing()
        {
            using (var host = BootWith(Source))
            {
                PressCtrlC(host);
                Assert.AreEqual("after", host.Output("print \"after\"."));
                host.AssertNoErrors();
            }
        }

        [Test]
        public void CtrlCCancelsACompileTypedAtThePrompt()
        {
            using (var host = BootWith(Source))
            {
                TypeHeld(host, "compile \"0:/brk/prog.ks\" to \"0:/brk/prog.ksm\".");
                PressCtrlC(host);
                AssertBusyFor(host, 20);

                host.Compiler.Release();
                host.RunUntilPrompt(500);
                Assert.IsFalse(File.Exists(Path.Combine(host.ArchiveRoot, "brk", "prog.ksm")), "a cancelled compile wrote its output");
                Assert.AreEqual("after", host.Output("print \"after\"."));
                Assert.AreEqual(0, host.Compiler.Overlaps, "two compiles ran at once: " + string.Join("\n", host.Compiler.OverlapLog.ToArray()));
                host.AssertNoErrors();
            }
        }

        [Test]
        public void ACancelledCompileReportsNoError()
        {
            using (var host = BootWith(BrokenSource))
            {
                TypeHeld(host, "compile \"0:/brk/prog.ks\" to \"0:/brk/prog.ksm\".");
                PressCtrlC(host);
                host.Compiler.Release();
                host.RunUntilPrompt(500);
                for (int i = 0; i < 50; ++i)
                    host.Update();
                host.AssertNoErrors();
                Assert.AreEqual("after", host.Output("print \"after\"."));
            }
        }

        [Test]
        public void ACompileThatIsNotInterruptedStillWritesItsOutput()
        {
            using (var host = BootWith(Source))
            {
                TypeHeld(host, "compile \"0:/brk/prog.ks\" to \"0:/brk/prog.ksm\".");
                host.Compiler.Release();
                host.RunUntilPrompt(500);
                Assert.IsTrue(File.Exists(Path.Combine(host.ArchiveRoot, "brk", "prog.ksm")));
                Assert.AreEqual("compiled\nProgram ended.", host.Output("runpath(\"0:/brk/prog.ksm\")."));
                host.AssertNoErrors();
            }
        }

        [Test]
        public void CtrlCDuringARunpathCompileWaitsForTheCompileThread()
        {
            // Upstream freed the prompt at once here, so the next typed command was compiled
            // on the game thread while the abandoned compile still used the same compiler.
            using (var host = BootWith(Source))
            {
                TypeHeld(host, "runpath(\"0:/brk/prog.ks\").");
                PressCtrlC(host);
                AssertBusyFor(host, 20);

                host.Compiler.Release();
                host.RunUntilPrompt(500);
                string transcript = host.Transcript();
                StringAssert.Contains("Program aborted.", transcript);
                StringAssert.DoesNotContain("compiled", transcript, "the cancelled program ran");
                Assert.AreEqual("after", host.Output("print \"after\"."));
                Assert.AreEqual(0, host.Compiler.Overlaps, "two compiles ran at once: " + string.Join("\n", host.Compiler.OverlapLog.ToArray()));
                host.AssertNoErrors();
            }
        }

        [Test]
        public void ASecondCtrlCWhileWaitingForTheThreadIsHarmless()
        {
            using (var host = BootWith(Source))
            {
                TypeHeld(host, "compile \"0:/brk/prog.ks\" to \"0:/brk/prog.ksm\".");
                PressCtrlC(host);
                host.Update();
                PressCtrlC(host);
                AssertBusyFor(host, 5);
                host.Compiler.Release();
                host.RunUntilPrompt(500);
                Assert.IsFalse(File.Exists(Path.Combine(host.ArchiveRoot, "brk", "prog.ksm")));
                Assert.AreEqual("after", host.Output("print \"after\"."));
                host.AssertNoErrors();
            }
        }
    }
}
