using System.Collections.Generic;
using kOS.Safe.Screen;
using kOS.Safe.Test.Execution;
using kOS.Safe.UserIO;
using NUnit.Framework;

namespace kOS.Safe.Test.UserIO
{
    /// <summary>
    /// The terminal window's input routing, moved out of <c>TermWindow</c> unchanged
    /// (Phase 2, P2-1), over the real prompt: what the telnet prompt and paste tests do not
    /// already cover (immediate GUI typing, the queue flags, key clicks, the resize combo,
    /// Ctrl-D/Ctrl-X, repaint requests and the boot guard).
    /// </summary>
    [TestFixture]
    public class TerminalInputRouterTest
    {
        private ScriptHost host;
        private FixtureTermWindow window;

        [SetUp]
        public void SetUp()
        {
            host = new ScriptHost();
            host.Boot();
            window = new FixtureTermWindow(host);
            host.BeforeUpdate = window.Update;
        }

        [TearDown]
        public void TearDown()
        {
            host.Dispose();
        }

        private void MakeBusy()
        {
            host.Type("wait 1000.\n");
            host.Update();
            host.Update();
            Assert.IsFalse(host.Screen.IsWaitingForCommand(), "the prompt should be busy");
        }

        [Test]
        public void AKeyNotForcedIntoTheQueueIsTypedAtAWaitingPromptAtOnce()
        {
            Assert.IsTrue(window.ProcessOneInputChar('x', null, true, false));
            Assert.AreEqual(0, window.TypeAhead.Count);
            Assert.IsFalse(host.Screen.IsAtStartOfCommand(), "the key should be on the input line");
        }

        [Test]
        public void AForcedKeyWaitsInTheQueueForTheNextUpdate()
        {
            Assert.IsTrue(window.ProcessOneInputChar('x', null, true, true));
            Assert.AreEqual(1, window.TypeAhead.Count);
            Assert.IsTrue(host.Screen.IsAtStartOfCommand());

            window.Update();
            Assert.AreEqual(0, window.TypeAhead.Count);
            Assert.IsFalse(host.Screen.IsAtStartOfCommand());
        }

        [Test]
        public void ABusyPromptDropsKeysThatMayNotBeQueued()
        {
            MakeBusy();
            Assert.IsFalse(window.ProcessOneInputChar('x', null, false, false));
            Assert.IsFalse(window.ProcessOneInputChar((char)UnicodeCommand.LEFTCURSORONE, null, false, false));
            Assert.AreEqual(0, window.TypeAhead.Count);
            Assert.AreEqual(0, window.KeyClicks);

            Assert.IsTrue(window.ProcessOneInputChar('x', null, true, false));
            Assert.AreEqual(1, window.TypeAhead.Count, "a busy prompt queues a key that may be queued");
        }

        [Test]
        public void OnlyKeysThatMayBeQueuedClick()
        {
            window.ProcessOneInputChar('x', null, true, true);
            window.ProcessOneInputChar((char)UnicodeCommand.LEFTCURSORONE, null, true, true);
            Assert.AreEqual(2, window.KeyClicks);

            window.Update(); // the prompt is fed with queuing off: no second click
            Assert.AreEqual(0, window.TypeAhead.Count);
            Assert.AreEqual(2, window.KeyClicks);
        }

        [Test]
        public void TheResizeComboSetsWidthThenHeight()
        {
            Assert.IsTrue(window.ProcessOneInputChar((char)UnicodeCommand.RESIZESCREEN, null, true, true));
            Assert.IsTrue(window.ProcessOneInputChar((char)60, null, true, true));
            Assert.IsTrue(window.ProcessOneInputChar((char)20, null, true, true));
            Assert.AreEqual(60, host.Screen.ColumnCount);
            Assert.AreEqual(20, host.Screen.RowCount);
            Assert.AreEqual(0, window.TypeAhead.Count, "the combo's characters are not input");

            window.ProcessOneInputChar('x', null, true, true);
            Assert.AreEqual(1, window.TypeAhead.Count, "input is normal after the combo");
        }

        [Test]
        public void CtrlDAtTheStartOfACommandClosesTheWindowOrDetachesTheClient()
        {
            TelnetServerInputModel telnet = window.AttachTelnet("XTERM");

            window.ProcessOneInputChar((char)0x0004, null, true, true);
            Assert.AreEqual(1, window.CloseRequests);

            window.ProcessOneInputChar((char)0x0018, telnet, true, true);
            Assert.IsFalse(window.IsAttached(telnet));
            Assert.IsFalse(telnet.IsConnectedToProcessor);
            Assert.AreEqual(0, window.TypeAhead.Count, "Ctrl-D/Ctrl-X are not input");
        }

        [Test]
        public void CtrlDWithTypedTextDoesNothing()
        {
            TelnetServerInputModel telnet = window.AttachTelnet("XTERM");
            host.Type("pri");

            window.ProcessOneInputChar((char)0x0004, null, true, true);
            window.ProcessOneInputChar((char)0x0004, telnet, true, true);
            Assert.AreEqual(0, window.CloseRequests);
            Assert.IsTrue(window.IsAttached(telnet));
            Assert.AreEqual(0, window.TypeAhead.Count);
            Assert.IsFalse(host.Screen.IsAtStartOfCommand(), "the typed text stays");
        }

        [Test]
        public void ARepaintRequestReachesOnlyTheClientThatSentIt()
        {
            TelnetServerInputModel first = window.AttachTelnet("XTERM");
            TelnetServerInputModel second = window.AttachTelnet("XTERM");

            Assert.IsTrue(window.ProcessOneInputChar((char)UnicodeCommand.REQUESTREPAINT, first, true, true));
            Assert.IsTrue(window.ProcessOneInputChar((char)UnicodeCommand.REQUESTREPAINT, null, true, true));
            Assert.AreEqual(1, window.RepaintRequests[first]);
            Assert.AreEqual(0, window.RepaintRequests[second]);
            Assert.AreEqual(0, window.TypeAhead.Count);
        }

        [Test]
        public void TheQueueWaitsUntilTheProcessorHasBooted()
        {
            window.Booted = false;
            window.ProcessOneInputChar('x', null, true, true);
            window.Update();
            Assert.AreEqual(1, window.TypeAhead.Count, "#2925: no prompt input before the first processor update");

            window.Booted = true;
            window.Update();
            Assert.AreEqual(0, window.TypeAhead.Count);
            Assert.IsFalse(host.Screen.IsAtStartOfCommand());
        }

        [Test]
        public void BreakSkipsAndClearsTheQueue()
        {
            MakeBusy();
            window.ProcessOneInputChar('x', null, true, true);
            window.ProcessOneInputChar('y', null, true, true);

            Assert.IsTrue(window.ProcessOneInputChar((char)UnicodeCommand.BREAK, null, true, true));
            Assert.AreEqual(0, window.TypeAhead.Count, "BREAK wipes the type-ahead");
            host.RunUntilPrompt(50);
            Assert.IsTrue(host.Screen.IsAtStartOfCommand(), "the wait was broken and nothing was typed");
        }

        // ---- Idle updates (review P-01, P-02) and a missing interpreter (R-05), on a bare host ----

        /// <summary>A prompt that always waits, counting how often it is asked.</summary>
        private sealed class CountingInterpreter : ScreenBuffer, IInterpreter
        {
            public int WaitingChecks;
            public readonly List<char> Typed = new List<char>();
            public void Type(char ch) { Typed.Add(ch); }
            public bool SpecialKey(char key) { return true; }
            public string GetCommandHistoryAbsolute(int absoluteIndex) { return string.Empty; }
            public void SetInputLock(bool isLocked) { }
            public bool IsAtStartOfCommand() { return true; }
            public bool IsWaitingForCommand() { ++WaitingChecks; return true; }
            public void Reset() { }
        }

        private sealed class BareHost : ITerminalInputHost<object>
        {
            public IScreenBuffer Screen { get; set; }
            public IInterpreter Interpreter { get; set; }
            public bool HasBooted { get { return true; } }
            public int CloseRequests;
            public int DetachRequests;
            public bool IsClientAttached(object client) { return true; }
            public bool TryReadClientChar(object client, bool includeText, out char ch) { ch = '\0'; return false; }
            public void CloseWindow() { ++CloseRequests; }
            public void DetachClient(object client) { ++DetachRequests; }
            public void RepaintClient(object client) { }
            public void KeyClick() { }
        }

        [Test]
        public void AnIdlePromptIsNotAskedAnythingAndNothingIsAllocated()
        {
            var interpreter = new CountingInterpreter();
            var router = new TerminalInputRouter<object>(new BareHost { Screen = interpreter, Interpreter = interpreter });

            Assert.AreEqual(0, router.ProcessUnconsumedInput());
            Assert.AreEqual(0, interpreter.WaitingChecks, "an empty type-ahead needs no look at the prompt");
            Assert.AreEqual(0, AllocationCounter.BytesAllocated(() => router.ProcessUnconsumedInput()),
                "bytes allocated by 100 updates of an idle prompt");

            interpreter.CharInputQueue.Enqueue('x');
            interpreter.CharInputQueue.Enqueue('y');
            Assert.AreEqual(2, router.ProcessUnconsumedInput(), "queued keys are still typed");
            CollectionAssert.AreEqual(new[] { 'x', 'y' }, interpreter.Typed);
            Assert.AreEqual(0, interpreter.CharInputQueue.Count);
        }

        [Test]
        public void NoClientsIsNothingToReadAndNothingIsAllocated()
        {
            var interpreter = new CountingInterpreter();
            var router = new TerminalInputRouter<object>(new BareHost { Screen = interpreter, Interpreter = interpreter });
            var none = new object[0];

            Assert.AreEqual(0, router.ProcessClientInput(none));
            Assert.AreEqual(0, AllocationCounter.BytesAllocated(() => router.ProcessClientInput(none)),
                "bytes allocated by 100 updates without telnet clients");
        }

        [Test]
        public void CtrlDOrCtrlXWithoutAnInterpreterDoesNothing()
        {
            var bare = new BareHost { Screen = new ScreenBuffer() };
            var router = new TerminalInputRouter<object>(bare);

            Assert.IsTrue(router.ProcessOneInputChar((char)0x0004, null, true, true));
            Assert.IsTrue(router.ProcessOneInputChar((char)0x0018, new object(), true, true));
            Assert.AreEqual(0, bare.CloseRequests);
            Assert.AreEqual(0, bare.DetachRequests);
        }
    }
}
