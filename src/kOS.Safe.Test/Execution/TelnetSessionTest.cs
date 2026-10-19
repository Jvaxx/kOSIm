using System;
using System.Linq;
using kOS.Safe.Test.UserIO;
using kOS.Safe.UserIO;
using NUnit.Framework;

namespace kOS.Safe.Test.Execution
{
    /// <summary>
    /// Telnet clients and the terminal session end to end (Phase 2, P2-2): client bytes
    /// through the real parser, queue and router, the real prompt and screen, and the
    /// session's output to each client (<see cref="FixtureTermWindow"/>). Covers resize
    /// authority (D3) with sizes reported by NAWS, detach, repaint and command output.
    /// </summary>
    [TestFixture]
    public class TelnetSessionTest
    {
        private ScriptHost host;
        private FixtureTermWindow window;
        private DateTime now;

        [SetUp]
        public void SetUp()
        {
            host = new ScriptHost();
            host.Boot();
            window = new FixtureTermWindow(host);
            now = DateTime.Now;
            host.BeforeUpdate = () =>
            {
                now += TimeSpan.FromMilliseconds(51); // every update refreshes the clients
                window.Update(now);
            };
        }

        [TearDown]
        public void TearDown()
        {
            host.Dispose();
        }

        /// <summary>IAC SB NAWS width height IAC SE, as one socket read.</summary>
        private static void SendSize(TelnetServerInputModel telnet, int width, int height)
        {
            telnet.Receive(new byte[]
            {
                TelnetInputParser.IAC, TelnetInputParser.SB, TelnetInputParser.OPTION_NAWS,
                (byte)(width >> 8), (byte)width, (byte)(height >> 8), (byte)height,
                TelnetInputParser.IAC, TelnetInputParser.SE
            });
        }

        private static string LastResize(TelnetServerInputModel telnet)
        {
            return RecordingViewer.Describe(telnet.Output).LastOrDefault(line => line.StartsWith("RESIZESCREEN"));
        }

        private string ScreenRow(int row)
        {
            return host.Screen.GetBuffer()[row].ToString().Replace('\0', ' ');
        }

        private void AssertShowsScreen(TelnetServerInputModel telnet)
        {
            Assert.AreEqual(host.Screen.ColumnCount, telnet.Model.Width, "width");
            Assert.AreEqual(host.Screen.RowCount, telnet.Model.Height, "height");
            for (int row = 0; row < host.Screen.RowCount; ++row)
                Assert.AreEqual(ScreenRow(row), telnet.Model.Row(row), "row " + row);
        }

        [Test]
        public void AttachingGivesTheScreenTheClientsSize()
        {
            TelnetServerInputModel telnet = window.AttachTelnet("XTERM");

            Assert.AreEqual(80, host.Screen.ColumnCount);
            Assert.AreEqual(24, host.Screen.RowCount);
            Assert.AreEqual(FixtureTermWindow.Title, telnet.Model.Title);
            AssertShowsScreen(telnet);
        }

        [Test]
        public void TheSizeAClientReportsLastWinsAndTheOtherClientIsAskedToFollow()
        {
            TelnetServerInputModel a = window.AttachTelnet("XTERM");
            TelnetServerInputModel b = window.AttachTelnet("XTERM");
            host.Update();

            SendSize(a, 100, 30);
            host.Update();
            Assert.AreEqual(100, host.Screen.ColumnCount);
            Assert.AreEqual(30, host.Screen.RowCount);
            Assert.AreEqual("RESIZESCREEN 100x30", LastResize(b), "b is asked to take a's size");

            SendSize(b, 70, 20);
            host.Update();
            Assert.AreEqual(70, host.Screen.ColumnCount);
            Assert.AreEqual(20, host.Screen.RowCount);
            Assert.AreEqual("RESIZESCREEN 70x20", LastResize(a), "then a is asked to take b's");

            host.Type("print \"after\".\n");
            host.RunUntilPrompt();
            host.Update();
            AssertShowsScreen(a);
            AssertShowsScreen(b);
        }

        [Test]
        public void ACommandsOutputReachesTheClient()
        {
            TelnetServerInputModel telnet = window.AttachTelnet("XTERM");
            telnet.Send("print 6 * 7.\r");
            host.RunUntilPrompt();
            host.Update();

            AssertShowsScreen(telnet);
            Assert.IsTrue(Enumerable.Range(0, telnet.Model.Height).Any(row => telnet.Model.Row(row).TrimEnd() == "42"),
                "the client shows the result");
        }

        [Test]
        public void CtrlDAtAnEmptyPromptDetachesTheClientFromTheSession()
        {
            TelnetServerInputModel a = window.AttachTelnet("XTERM");
            TelnetServerInputModel b = window.AttachTelnet("XTERM");
            host.Update();

            a.Send("" + (char)4);
            host.Update();

            Assert.IsFalse(a.IsConnectedToProcessor);
            Assert.IsFalse(window.Session.HasViewer(a));
            Assert.AreEqual(1, window.Session.ViewerCount);
            int sent = a.Output.Length;
            b.Send("print 1.\r");
            host.RunUntilPrompt();
            host.Update();
            Assert.AreEqual(sent, a.Output.Length, "a detached client is sent nothing");
            AssertShowsScreen(b);
        }

        [Test]
        public void CtrlLRepaintsOnlyThatClientWithTheTitle()
        {
            TelnetServerInputModel a = window.AttachTelnet("XTERM");
            TelnetServerInputModel b = window.AttachTelnet("XTERM");
            host.Update();
            int sentToB = b.Output.Length;
            int sentToA = a.Output.Length;

            a.Send("" + (char)12); // Ctrl-L
            host.Update();

            Assert.AreEqual(1, window.RepaintRequests[a]);
            var toA = RecordingViewer.Describe(a.Output.Substring(sentToA));
            CollectionAssert.Contains(toA, "CLEARSCREEN");
            Assert.AreEqual("TITLE \"" + FixtureTermWindow.Title + "\"", toA.Last());
            Assert.AreEqual(sentToB, b.Output.Length);
        }
    }
}
