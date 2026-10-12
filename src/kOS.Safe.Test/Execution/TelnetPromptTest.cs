using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using kOS.Safe.Test.UserIO;
using kOS.Safe.UserIO;
using NUnit.Framework;

namespace kOS.Safe.Test.Execution
{
    /// <summary>
    /// Telnet input end to end on the real prompt: socket reads through the parser,
    /// decoder and bounded queue (<see cref="TelnetServerInputModel"/>), the terminal
    /// window's per-update input loops (<see cref="FixtureTermWindow"/>), and the real
    /// interpreter and CPU (<see cref="ScriptHost"/>).
    /// </summary>
    [TestFixture]
    public class TelnetPromptTest
    {
        private sealed class Session : IDisposable
        {
            public readonly ScriptHost Host = new ScriptHost();
            public readonly FixtureTermWindow Window;
            public readonly TelnetServerInputModel Telnet;

            public Session(string terminalType = "XTERM")
            {
                Host.Boot();
                Window = new FixtureTermWindow(Host);
                Host.BeforeUpdate = Window.Update;
                Telnet = Window.AttachTelnet(terminalType);
            }

            /// <summary>Run until every typed command has run and the prompt is idle.</summary>
            public void RunUntilInputDone(int maxRounds = 20000)
            {
                for (int round = 0; round < maxRounds; ++round)
                {
                    Host.RunUntilPrompt();
                    if (Window.PendingInput == 0 && Host.Screen.IsAtStartOfCommand())
                        return;
                }
                throw new Exception("Input was not used up; " + Window.PendingInput + " keys left\n" + Host.Transcript());
            }

            /// <summary>Send text in socket reads of at most <paramref name="readSize"/> bytes (ASCII).</summary>
            public void SendInReads(string text, int readSize)
            {
                for (int offset = 0; offset < text.Length; offset += readSize)
                    Telnet.Send(text.Substring(offset, Math.Min(readSize, text.Length - offset)));
            }

            public void Dispose() { Host.Dispose(); }
        }

        /// <summary>Split after each CR, so the LF of every CR LF arrives in the next read.</summary>
        private static List<string> SplitAfterEachCr(string text)
        {
            var reads = new List<string>();
            int start = 0;
            for (int i = 0; i < text.Length; ++i)
            {
                if (text[i] == '\r')
                {
                    reads.Add(text.Substring(start, i + 1 - start));
                    start = i + 1;
                }
            }
            if (start < text.Length)
                reads.Add(text.Substring(start));
            return reads;
        }

        private const string MultiLinePaste = "print 1.\r\nprint 2.\r\n\r\nprint 3.\r\n";

        [TestCase(false, TestName = "SplitMultiLinePasteRunsEveryLineOnce(all reads before the first update)")]
        [TestCase(true, TestName = "SplitMultiLinePasteRunsEveryLineOnce(one read per update)")]
        public void SplitMultiLinePasteRunsEveryLineOnce(bool updateBetweenReads)
        {
            using (var session = new Session())
            {
                foreach (string read in SplitAfterEachCr(MultiLinePaste))
                {
                    session.Telnet.Send(read);
                    if (updateBetweenReads)
                        session.Host.Update();
                }
                session.RunUntilInputDone();
                session.Host.AssertNoErrors();
                // An empty command adds no row, so the transcript cannot count Enters; the
                // next test pins the keys.
                Assert.AreEqual("print 1.\n1\nprint 2.\n2\nprint 3.\n3", session.Host.Transcript());
            }
        }

        [Test]
        public void UpstreamPerReadConversionTypedExtraEntersForTheSamePaste()
        {
            // What the old mappers produced for these reads: each LF starting a read was a
            // second Enter. The decoder gives one Enter per line ending.
            string enter = ((char)UnicodeCommand.STARTNEXTLINE).ToString();
            string upstream = string.Concat(SplitAfterEachCr(MultiLinePaste)
                .Select(read => UpstreamTerminalInput.Convert(TerminalInputDialect.Xterm, read)).ToArray());
            var decoder = new TerminalInputDecoder(TerminalInputDialect.Xterm);
            string decoded = string.Concat(SplitAfterEachCr(MultiLinePaste).Select(read => decoder.Decode(read)).ToArray());
            Assert.AreEqual(4, decoded.Split(new[] { enter }, StringSplitOptions.None).Length - 1);
            Assert.AreEqual(7, upstream.Split(new[] { enter }, StringSplitOptions.None).Length - 1);
        }

        [Test]
        public void InterruptDuringABacklogAbortsAtOnceAndDiscardsTheBacklog()
        {
            // A program: Ctrl-C does not end a WAIT typed at the prompt itself (upstream CPU
            // behavior, BreakExecution with only the interpreter context; recorded separately).
            using (var session = new Session())
            {
                session.Host.WriteArchiveBytes("telnet/wait.ks", Encoding.UTF8.GetBytes("wait 1000.\nprint \"waited\".\n"));
                session.Telnet.Send("runpath(\"0:/telnet/wait.ks\").\r\n");
                session.Host.RunUntil(() => !session.Host.Screen.IsWaitingForCommand() && session.Window.PendingInput == 0, 100);

                const string line = "print \"x\".\r\n";
                string paste = string.Concat(Enumerable.Repeat(line, 3000).ToArray());
                session.SendInReads(paste, 1024);
                for (int i = 0; i < 5; ++i)
                    session.Host.Update();

                // The type-ahead filled to its limit; the rest waits in the telnet queue.
                Assert.IsFalse(session.Host.Screen.IsWaitingForCommand(), "the wait ended early");
                Assert.AreEqual(TerminalInputPump.TYPE_AHEAD_LIMIT, session.Window.TypeAhead.Count);
                int keys = paste.Length - 3000; // each CR LF is one Enter key
                int backlog = keys - TerminalInputPump.TYPE_AHEAD_LIMIT;
                Assert.AreEqual(backlog, session.Telnet.Queue.TextCount);

                session.Telnet.SendInterrupt();
                session.Host.Update();
                Assert.IsTrue(session.Host.Screen.IsWaitingForCommand(), "the interrupt took more than one update");
                Assert.AreEqual(0, session.Window.PendingInput, "the interrupt did not discard the backlog at once");
                Assert.AreEqual(backlog, session.Telnet.Queue.DiscardedByBreakCount);

                for (int i = 0; i < 50; ++i)
                    session.Host.Update();
                session.Telnet.Send("print \"after\".\r\n");
                session.RunUntilInputDone();
                string[] rows = session.Host.Transcript().Split('\n');
                CollectionAssert.DoesNotContain(rows, "x", "discarded type-ahead ran");
                CollectionAssert.DoesNotContain(rows, "waited");
                CollectionAssert.Contains(rows, "Program aborted.");
                Assert.AreEqual("after", rows.Last());
                session.Host.AssertNoErrors();
            }
        }

        [Test]
        public void ResizeIsAppliedBeforeAWaitingBacklog()
        {
            using (var session = new Session())
            {
                session.Telnet.Send("wait 1000.\r\n");
                session.Host.RunUntil(() => !session.Host.Screen.IsWaitingForCommand() && session.Window.PendingInput == 0, 100);
                session.SendInReads(string.Concat(Enumerable.Repeat("print 1.\r\n", 2000).ToArray()), 512);
                session.Host.Update();
                Assert.Greater(session.Telnet.Queue.TextCount, 0);

                // IAC SB NAWS 0 100 0 30 IAC SE
                session.Telnet.Receive(new byte[] { 255, 250, 31, 0, 100, 0, 30, 255, 240 });
                session.Host.Update();
                Assert.AreEqual(100, session.Host.Screen.ColumnCount);
                Assert.AreEqual(30, session.Host.Screen.RowCount);
                Assert.AreEqual(1, session.Window.RepaintRequests[session.Telnet]);
            }
        }

        [Test]
        public void LongPastedLineIsTypedWithinThePerUpdateLimits()
        {
            using (var session = new Session())
            {
                string command = "print \"" + new string('a', 10000) + "\":length.";
                session.SendInReads(command + "\r\n", 1460);
                int updatesBefore = 0;
                session.Host.BeforeUpdate = () => { ++updatesBefore; session.Window.Update(); };
                session.RunUntilInputDone();
                session.Host.AssertNoErrors();
                Assert.AreEqual("10000", session.Host.Transcript().Split('\n').Last());
                Assert.LessOrEqual(session.Window.MaxTelnetKeysPerUpdate, TerminalInputPump.TELNET_CHARS_PER_UPDATE);
                Assert.LessOrEqual(session.Window.MaxPromptKeysPerUpdate, TerminalInputPump.PROMPT_CHARS_PER_UPDATE);
                Assert.GreaterOrEqual(updatesBefore, (command.Length + 2) / TerminalInputPump.PROMPT_CHARS_PER_UPDATE);
            }
        }
    }
}
