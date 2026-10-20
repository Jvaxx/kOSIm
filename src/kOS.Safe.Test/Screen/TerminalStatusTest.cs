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

        // Phase 4, step 1: the parts for the GUI's status strip.

        private static string[] ChipTexts(TerminalStatus status)
        {
            return status.Chips.ConvertAll(chip => chip.Text).ToArray();
        }

        [Test]
        public void TheStripGetsThePartsSeparately()
        {
            var status = new TerminalStatus { ProgramPath = "0:/a.ks", DelaySecondsLeft = 12.3, Connected = false, NewOutput = true, TelnetClients = 2 };
            Assert.AreEqual(StatusKind.Running, status.Kind);
            Assert.AreEqual("Running 0:/a.ks", status.StateText);
            CollectionAssert.AreEqual(new[] { "Delay 12 s", "No connection", "New output", "2 telnet clients" }, ChipTexts(status));
            Assert.AreEqual("Running 0:/a.ks | Delay 12 s | No connection | New output", status.Text, "the telnet title has no client count");

            var sending = new TerminalStatus { AtPrompt = true, DelaySecondsLeft = 2.44, TelnetClients = 1 };
            Assert.AreEqual(StatusKind.Sending, sending.Kind);
            Assert.AreEqual("Delay 2.4 s", sending.StateText, "in place of Ready, as in the title");
            CollectionAssert.AreEqual(new[] { "1 telnet client" }, ChipTexts(sending), "the delay is not repeated");
            Assert.AreEqual("Delay 2.4 s", sending.Text);
        }

        [Test]
        public void TheKindColorsTheDot()
        {
            Assert.AreEqual(StatusKind.Off, new TerminalStatus { Mode = ProcessorModes.OFF }.Kind);
            Assert.AreEqual(StatusKind.NoPower, new TerminalStatus { Mode = ProcessorModes.STARVED }.Kind);
            Assert.AreEqual(StatusKind.Starting, new TerminalStatus { Booted = false, AtPrompt = true }.Kind);
            Assert.AreEqual(StatusKind.Ready, new TerminalStatus { AtPrompt = true }.Kind);
            Assert.AreEqual(StatusKind.Running, new TerminalStatus().Kind);
            Assert.AreEqual(StatusKind.Waiting, new TerminalStatus { ProgramPath = "0:/a.ks", Waiting = true }.Kind);
            Assert.AreEqual(StatusKind.Waiting, new TerminalStatus { Waiting = true }.Kind);
        }

        [Test]
        public void OffNoPowerAndStartingHaveOnlyTheClientCount()
        {
            foreach (var status in new[]
            {
                new TerminalStatus { Mode = ProcessorModes.OFF, Connected = false, NewOutput = true, DelaySecondsLeft = 1 },
                new TerminalStatus { Mode = ProcessorModes.STARVED, Connected = false, NewOutput = true },
                new TerminalStatus { Booted = false, Connected = false, NewOutput = true }
            })
            {
                Assert.AreEqual(0, status.Chips.Count, status.StateText);
                status.TelnetClients = 3;
                CollectionAssert.AreEqual(new[] { "3 telnet clients" }, ChipTexts(status), status.StateText);
                Assert.AreEqual(status.StateText, status.Text, "as before: the state says it all");
            }
        }

        [Test]
        public void ChipPriorities()
        {
            var status = new TerminalStatus { ProgramPath = "0:/a.ks", DelaySecondsLeft = 1, Connected = false, NewOutput = true, TelnetClients = 1 };
            int[] priorities = status.Chips.ConvertAll(chip => chip.Priority).ToArray();
            CollectionAssert.AreEqual(new[] { StatusChipKind.Delay, StatusChipKind.NoConnection, StatusChipKind.NewOutput, StatusChipKind.Telnet },
                status.Chips.ConvertAll(chip => chip.Kind).ToArray(), "the order shown");
            Assert.Greater(priorities[1], priorities[0], "no connection is kept longest");
            Assert.Greater(priorities[0], priorities[2], "then the delay");
            Assert.Greater(priorities[2], priorities[3], "then new output; the client count goes first");
        }

        // STATUS-01: Waiting kept across the short runs between waits.

        [Test]
        public void WaitingIsKeptBetweenTheWaitsOfALoop()
        {
            var display = new WaitingDisplay();
            // A wait 1 loop: held from half a second into each wait, as IsWaitingFor reports it.
            for (double t = 0; t < 10; t += 0.25)
            {
                bool held = t >= 0.5 && t % 1 >= 0.5;
                Assert.AreEqual(t >= 0.5, display.Update(t, held, false, "0:/loop.ks"), "at " + t);
            }
        }

        [Test]
        public void RunningIsShownAfterARunWithoutAWait()
        {
            var display = new WaitingDisplay();
            Assert.IsTrue(display.Update(1.0, true, false, "0:/a.ks"));
            Assert.IsTrue(display.Update(1.0 + WaitingDisplay.KEPT_FOR_SECONDS - 0.01, false, false, "0:/a.ks"));
            Assert.IsFalse(display.Update(1.0 + WaitingDisplay.KEPT_FOR_SECONDS, false, false, "0:/a.ks"));
        }

        [Test]
        public void ANewActivityStartsAfresh()
        {
            var display = new WaitingDisplay();
            Assert.IsTrue(display.Update(1.0, true, false, "0:/a.ks"));
            Assert.IsFalse(display.Update(1.1, false, true, null), "the prompt");
            Assert.IsFalse(display.Update(1.2, false, false, "0:/a.ks"), "the same program run again");

            Assert.IsTrue(display.Update(2.0, true, false, "0:/a.ks"));
            Assert.IsFalse(display.Update(2.1, false, false, "0:/b.ks"), "another program");
            Assert.IsTrue(display.Update(3.0, true, false, "0:/b.ks"));
            Assert.IsFalse(display.Update(3.1, false, false, null), "the interpreter after a program");

            Assert.IsTrue(display.Update(4.0, true, false, null));
            Assert.IsFalse(display.Update(0.5, false, false, null), "the clock started again");

            Assert.IsTrue(display.Update(5.0, true, false, "0:/a.ks"));
            display.Reset(); // power off
            Assert.IsFalse(display.Update(5.1, false, false, "0:/a.ks"));
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
        public void AWaitLoopStaysWaitingUntilThePrompt()
        {
            using (var host = new ScriptHost())
            {
                // As slow2.ks in the 2026-10-20 pass, where the state alternated twice a second.
                Write(host, "statuswaitloop.ks", "print \"go\".\nlocal n is 0.\nuntil n >= 5 { print \"line \" + n. set n to n + 1. wait 1. }\n");
                host.Boot();
                host.Type("runpath(\"0:/statuswaitloop.ks\").\n");
                UpdateUntil(host, () => host.Transcript().Contains("\ngo"));

                var display = new WaitingDisplay();
                int heldChanges = 0, shownChanges = 0;
                bool held = false, shown = false;
                for (int i = 0; i < 400 && !host.Screen.IsWaitingForCommand(); ++i)
                {
                    host.Update();
                    bool nowHeld = host.Cpu.IsWaitingFor(TerminalStatus.WAIT_SHOWN_AFTER_SECONDS);
                    bool nowShown = display.Update(host.Cpu.SessionTime, nowHeld, host.Screen.IsWaitingForCommand(), host.Cpu.RunningProgramPath);
                    if (nowHeld != held) ++heldChanges;
                    if (nowShown != shown) ++shownChanges;
                    held = nowHeld;
                    shown = nowShown;
                }
                Assert.IsTrue(host.Screen.IsWaitingForCommand(), "the loop ended\n" + host.Transcript());
                host.AssertNoErrors();
                Assert.GreaterOrEqual(heldChanges, 8, "the CPU itself is held only part of each turn");
                Assert.AreEqual(2, shownChanges, "Waiting from the first held wait, never back to Running; off at the prompt (Ready)");
                Assert.IsFalse(shown);
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
