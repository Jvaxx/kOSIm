using System.IO;
using kOS.Safe.Module;
using kOS.Safe.Screen;
using kOS.Safe.Test.Execution;
using NUnit.Framework;

namespace kOS.Safe.Test.Terminal
{
    /// <summary>
    /// Phase 3, decision D2: the CPU state shown in the GUI bar and the telnet title.
    /// The text (<see cref="TerminalStatus"/>), then what the real CPU reports for it:
    /// the running program and a held WAIT.
    /// </summary>
    [TestFixture]
    public class TerminalStatusTest
    {
        [Test]
        public void PowerAndBootSayItAll()
        {
            Assert.AreEqual("Off", new TerminalStatus { Mode = ProcessorModes.OFF, AtPrompt = true, Connected = false }.Text);
            Assert.AreEqual("No power", new TerminalStatus { Mode = ProcessorModes.STARVED, ProgramPath = "0:/a.ks" }.Text);
            Assert.AreEqual("Starting", new TerminalStatus { Booted = false }.Text);
        }

        [Test]
        public void WhatRuns()
        {
            Assert.AreEqual("Ready", new TerminalStatus { AtPrompt = true }.Text);
            Assert.AreEqual("Running 0:/launch.ks", new TerminalStatus { ProgramPath = "0:/launch.ks" }.Text);
            Assert.AreEqual("Waiting in 0:/launch.ks", new TerminalStatus { ProgramPath = "0:/launch.ks", Waiting = true }.Text);
            Assert.AreEqual("Running a command", new TerminalStatus().Text);
            Assert.AreEqual("Waiting in a command", new TerminalStatus { Waiting = true }.Text);
        }

        [Test]
        public void Communications()
        {
            Assert.AreEqual("Delay 2.4 s", new TerminalStatus { AtPrompt = true, DelaySecondsLeft = 2.44 }.Text, "in place of Ready");
            Assert.AreEqual("Running 0:/a.ks | Delay 12 s", new TerminalStatus { ProgramPath = "0:/a.ks", DelaySecondsLeft = 12.3 }.Text);
            Assert.AreEqual("Ready | No connection", new TerminalStatus { AtPrompt = true, Connected = false }.Text);
            Assert.AreEqual("0.0", TerminalStatus.FormatSeconds(-1));
            Assert.AreEqual("9.9", TerminalStatus.FormatSeconds(9.94));
            Assert.AreEqual("100", TerminalStatus.FormatSeconds(99.5));
        }

        [Test]
        public void TitleWithStatus()
        {
            Assert.AreEqual("ship CPU: CX-4181 () - Ready", TerminalStatus.TitleWithStatus("ship CPU: CX-4181 ()", "Ready"));
            Assert.AreEqual("ship", TerminalStatus.TitleWithStatus("ship", ""));
        }

        // The real CPU:

        private static void Write(ScriptHost host, string name, string content)
        {
            File.WriteAllText(Path.Combine(host.ArchiveRoot, name), content);
        }

        private static void Updates(ScriptHost host, double seconds)
        {
            for (double t = 0; t < seconds; t += ScriptHost.UpdateSeconds)
                host.Update();
        }

        /// <summary>Update until the condition holds; compiles run on a background thread in wall-clock time.</summary>
        private static void UpdateUntil(ScriptHost host, System.Func<bool> condition)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (!condition())
            {
                Assert.Less(timer.Elapsed.TotalSeconds, 10, "condition not reached\n" + host.Transcript());
                host.Update();
                System.Threading.Thread.Sleep(1);
            }
        }

        [Test]
        public void TheCpuNamesTheMainProgramAndAHeldWait()
        {
            using (var host = new ScriptHost())
            {
                Write(host, "statusmain.ks", "runpath(\"0:/statuslib.ks\").\nwait 2.\nprint \"done\".\n");
                Write(host, "statuslib.ks", "wait 0.1.\n");
                host.Boot();
                Assert.IsNull(host.Cpu.RunningProgramPath, "the interpreter only");
                Assert.IsFalse(host.Cpu.IsWaitingFor(TerminalStatus.WAIT_SHOWN_AFTER_SECONDS));

                host.Type("runpath(\"0:/statusmain.ks\").\n");
                Updates(host, 0.6);
                Assert.AreEqual("0:/statusmain.ks", host.Cpu.RunningProgramPath, "the first program, not the library it ran");
                Assert.IsFalse(host.Screen.IsWaitingForCommand());
                Updates(host, 0.6);
                Assert.IsTrue(host.Cpu.IsWaitingFor(TerminalStatus.WAIT_SHOWN_AFTER_SECONDS), "held by wait 2 for over half a second");
                Assert.IsFalse(host.Cpu.IsWaitingFor(10));

                host.RunUntilPrompt();
                host.AssertNoErrors();
                Assert.IsNull(host.Cpu.RunningProgramPath, "back at the interpreter");
                Assert.IsFalse(host.Cpu.IsWaitingFor(TerminalStatus.WAIT_SHOWN_AFTER_SECONDS));
            }
        }

        [Test]
        public void AShortWaitLoopIsNotShownAsWaiting()
        {
            using (var host = new ScriptHost())
            {
                Write(host, "statusloop.ks", "print \"go\".\nlocal n is 0.\nuntil n > 100 { set n to n + 1. wait 0. }\n");
                host.Boot();
                host.Type("runpath(\"0:/statusloop.ks\").\n");
                UpdateUntil(host, () => host.Transcript().Contains("\ngo")); // compiled (a compile held long is Waiting too)
                for (int i = 0; i < 60; ++i)
                {
                    host.Update();
                    Assert.IsFalse(host.Cpu.IsWaitingFor(TerminalStatus.WAIT_SHOWN_AFTER_SECONDS), "update " + i);
                }
            }
        }

        [Test]
        public void AWaitAtTheInterpreterHasNoProgram()
        {
            using (var host = new ScriptHost())
            {
                host.Boot();
                host.Type("wait 1.\n");
                Updates(host, 0.7);
                Assert.IsNull(host.Cpu.RunningProgramPath);
                Assert.IsFalse(host.Screen.IsWaitingForCommand());
                Assert.IsTrue(host.Cpu.IsWaitingFor(TerminalStatus.WAIT_SHOWN_AFTER_SECONDS));
                host.RunUntilPrompt();
            }
        }

        [Test]
        public void TheBootFileIsTheProgram()
        {
            using (var host = new ScriptHost())
            {
                Write(host, "statusboot.ks", "wait 1.\n");
                host.Boot("/statusboot.ks"); // a volume path, as the processor gives
                UpdateUntil(host, () => host.Cpu.RunningProgramPath != null);
                Assert.AreEqual("0:/statusboot.ks", host.Cpu.RunningProgramPath);
                host.RunUntilPrompt();
            }
        }
    }
}
