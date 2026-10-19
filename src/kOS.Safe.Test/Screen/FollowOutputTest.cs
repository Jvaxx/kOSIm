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
    /// Phase 3, decisions O1/O2: a view scrolled back stays while output comes, with a
    /// "new output" marker until it is back at the screen; typing brings it back; the GUI's
    /// mouse wheel moves the view (<see cref="WheelScroll"/>).
    /// </summary>
    [TestFixture]
    public class FollowOutputTest
    {
        private static string Row(IScreenBufferLine line)
        {
            return line.ToString().Replace('\0', ' ').TrimEnd();
        }

        private static List<string> View(IScreenBuffer screen)
        {
            return screen.GetBuffer().Select(Row).ToList();
        }

        private static ScreenBuffer ScreenWithLines(int rows, int columns, int lines)
        {
            var screen = new ScreenBuffer();
            screen.SetSize(rows, columns);
            screen.ClearScreen();
            for (int i = 0; i < lines; ++i)
                screen.Print("line " + i);
            return screen;
        }

        [Test]
        public void OutputLeavesAScrolledBackViewWhereItIs()
        {
            ScreenBuffer screen = ScreenWithLines(10, 40, 30);
            screen.ScrollVertical(-5);
            List<string> view = View(screen);
            Assert.IsFalse(screen.HasNewOutputOutOfView);

            for (int i = 0; i < 12; ++i)
                screen.Print("more " + i);

            CollectionAssert.AreEqual(view, View(screen), "the same lines");
            Assert.AreEqual(5 + 12, screen.ViewOffset);
            Assert.IsTrue(screen.HasNewOutputOutOfView);

            screen.ScrollVertical(10);
            Assert.IsTrue(screen.HasNewOutputOutOfView, "not back yet");
            screen.ScrollVertical(10);
            Assert.AreEqual(0, screen.ViewOffset);
            Assert.IsFalse(screen.HasNewOutputOutOfView, "back at the screen");
            Assert.AreEqual("more 11", View(screen)[screen.CursorRowShow - 1]);
        }

        [Test]
        public void AViewOnTheScreenFollowsTheOutput()
        {
            ScreenBuffer screen = ScreenWithLines(10, 40, 30);
            screen.Print("newest");
            Assert.AreEqual(0, screen.ViewOffset);
            Assert.IsFalse(screen.HasNewOutputOutOfView);
            Assert.AreEqual("newest", View(screen)[screen.CursorRowShow - 1]);
        }

        [Test]
        public void ClearScreenEndsTheMarker()
        {
            ScreenBuffer screen = ScreenWithLines(10, 40, 30);
            screen.ScrollVertical(-5);
            screen.Print("x");
            screen.ClearScreen();
            Assert.IsFalse(screen.HasNewOutputOutOfView);
            Assert.AreEqual(0, screen.ViewOffset);
        }

        [Test]
        public void AViewWhoseLinesAreDroppedShowsTheOldestLeft()
        {
            ScreenBuffer screen = ScreenWithLines(10, 40, ScreenBuffer.MAX_SCROLLBACK + 10);
            screen.ScrollVertical(-ScreenBuffer.MAX_SCROLLBACK);
            Assert.AreEqual(ScreenBuffer.MAX_SCROLLBACK, screen.ViewOffset, "at the top of the scrollback");
            for (int i = 0; i < 25; ++i)
                screen.Print("more " + i);
            Assert.AreEqual(ScreenBuffer.MAX_SCROLLBACK, screen.ScrollbackRows);
            Assert.AreEqual(ScreenBuffer.MAX_SCROLLBACK, screen.ViewOffset, "still the oldest line kept");
            Assert.AreEqual("line 26", View(screen)[0], "the dropped lines are gone from the top");
        }

        [Test]
        public void TypingAtThePromptBringsTheViewBack()
        {
            using (var host = new ScriptHost(10, 40))
            {
                host.Boot();
                for (int i = 0; i < 30; ++i)
                    host.Screen.Print("line " + i);
                host.Screen.SpecialKey((char)UnicodeCommand.PAGEUPCURSOR);
                host.Screen.Print("printed by a trigger");
                Assert.AreEqual(10 + 1, host.Screen.ViewOffset, "stays");
                Assert.IsTrue(host.Screen.HasNewOutputOutOfView);

                host.Type("p");
                Assert.AreEqual(0, host.Screen.ViewOffset);
                Assert.IsFalse(host.Screen.HasNewOutputOutOfView);
            }
        }

        [Test]
        public void ARunningProgramDoesNotPullTheViewDown()
        {
            using (var host = new ScriptHost(10, 40))
            {
                File.WriteAllText(Path.Combine(host.ArchiveRoot, "follow.ks"),
                    "from { local i is 0. } until i >= 40 step { set i to i + 1. } do { print \"out \" + i. wait 0. }\n");
                host.Boot();
                for (int i = 0; i < 20; ++i)
                    host.Screen.Print("line " + i);
                host.Type("runpath(\"0:/follow.ks\").\n");
                var timer = System.Diagnostics.Stopwatch.StartNew();
                while (!host.Transcript().Contains("out 3"))
                {
                    Assert.Less(timer.Elapsed.TotalSeconds, 10, host.Transcript());
                    host.Update();
                    System.Threading.Thread.Sleep(1);
                }

                host.Screen.ScrollVertical(-8); // as the GUI's mouse wheel does while it runs
                List<string> view = View(host.Screen);
                for (int i = 0; i < 10; ++i)
                    host.Update();
                CollectionAssert.AreEqual(view, View(host.Screen), "the program's output leaves the view");
                Assert.IsTrue(host.Screen.HasNewOutputOutOfView);

                host.RunUntilPrompt();
                host.AssertNoErrors();
                Assert.IsTrue(host.Screen.HasNewOutputOutOfView, "the program ending does not bring it back");
                host.Screen.ScrollVertical(1000);
                Assert.IsFalse(host.Screen.HasNewOutputOutOfView);
                StringAssert.Contains("out 39", string.Join("\n", View(host.Screen).ToArray()));
            }
        }

        [Test]
        public void TheStatusShowsTheMarker()
        {
            Assert.AreEqual("Ready | New output", new TerminalStatus { AtPrompt = true, NewOutput = true }.Text);
            Assert.AreEqual("Running 0:/a.ks | No connection | New output",
                new TerminalStatus { ProgramPath = "0:/a.ks", Connected = false, NewOutput = true }.Text);
        }

        // The GUI's mouse wheel:

        [Test]
        public void ThreeLinesANotch()
        {
            var wheel = new WheelScroll();
            Assert.AreEqual(3, wheel.Lines(3f), "a notch down");
            Assert.AreEqual(-3, wheel.Lines(-3f), "a notch up");
            Assert.AreEqual(6, wheel.Lines(6f), "two notches in one event");
        }

        [Test]
        public void FineDeltasAddUpAndADirectionChangeStartsAgain()
        {
            var wheel = new WheelScroll();
            Assert.AreEqual(0, wheel.Lines(0.4f));
            Assert.AreEqual(0, wheel.Lines(0.4f));
            Assert.AreEqual(1, wheel.Lines(0.4f), "1.2 lines so far");
            Assert.AreEqual(0, wheel.Lines(-0.5f), "the 0.2 left over is dropped, not taken away");
            Assert.AreEqual(-1, wheel.Lines(-0.5f));
            Assert.AreEqual(0, wheel.Lines(float.NaN));
            Assert.AreEqual(0, wheel.Lines(float.PositiveInfinity));
        }
    }
}
