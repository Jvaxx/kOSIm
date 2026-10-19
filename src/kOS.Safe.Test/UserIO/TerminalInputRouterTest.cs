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
    }
}
