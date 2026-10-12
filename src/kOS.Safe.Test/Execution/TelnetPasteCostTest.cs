using System;
using System.Diagnostics;
using System.Linq;
using kOS.Safe.Test.UserIO;
using kOS.Safe.UserIO;
using NUnit.Framework;

namespace kOS.Safe.Test.Execution
{
    /// <summary>
    /// A headless proxy for the frame cost of a large telnet paste: wall time per update
    /// of the terminal window's input work and the CPU update, with the game's per-update
    /// limits and with them removed (which approximates upstream, which drained every
    /// telnet queue and fed the prompt without limit). Only the deterministic bounds are
    /// asserted; the times go to the test output (<c>MEASURE</c> lines) and depend on the
    /// machine. Real frame time needs the game.
    /// </summary>
    [TestFixture]
    public class TelnetPasteCostTest
    {
        private sealed class Result
        {
            public int Updates;
            public double TotalMs;
            public double MaxInputMs;
            public double MaxUpdateMs;
            public int MaxTelnetKeys;
            public int MaxPromptKeys;
            public string LastRow;

            public override string ToString()
            {
                return string.Format("{0} updates, total {1:F1} ms, worst update {2:F1} ms (input work {3:F1} ms), " +
                    "most keys per update: {4} from telnet, {5} to the prompt",
                    Updates, TotalMs, MaxUpdateMs, MaxInputMs, MaxTelnetKeys, MaxPromptKeys);
            }
        }

        private static Result Measure(string paste, bool budgets)
        {
            using (var host = new ScriptHost())
            {
                host.Boot();
                var window = new FixtureTermWindow(host);
                if (!budgets)
                {
                    window.TelnetCharsPerUpdate = int.MaxValue;
                    window.TypeAheadLimit = int.MaxValue;
                    window.PromptCharsPerUpdate = int.MaxValue;
                    window.TimePerUpdate = TimeSpan.MaxValue;
                }
                var result = new Result();
                var inputWatch = new Stopwatch();
                host.BeforeUpdate = () =>
                {
                    inputWatch.Restart();
                    window.Update();
                    result.MaxInputMs = Math.Max(result.MaxInputMs, inputWatch.Elapsed.TotalMilliseconds);
                };
                TelnetServerInputModel telnet = window.AttachTelnet("XTERM");
                for (int offset = 0; offset < paste.Length; offset += 1460) // one TCP segment per read
                    telnet.Send(paste.Substring(offset, Math.Min(1460, paste.Length - offset)));

                var updateWatch = new Stopwatch();
                var total = Stopwatch.StartNew();
                for (int i = 0; i < 100000 && (window.PendingInput > 0 || !host.Screen.IsWaitingForCommand()); ++i)
                {
                    updateWatch.Restart();
                    host.Update();
                    result.MaxUpdateMs = Math.Max(result.MaxUpdateMs, updateWatch.Elapsed.TotalMilliseconds);
                    ++result.Updates;
                }
                result.TotalMs = total.Elapsed.TotalMilliseconds;
                result.MaxTelnetKeys = window.MaxTelnetKeysPerUpdate;
                result.MaxPromptKeys = window.MaxPromptKeysPerUpdate;
                host.AssertNoErrors();
                result.LastRow = host.Transcript().Split('\n').Last();
                return result;
            }
        }

        [Test]
        public void LongLinePasteSpreadsPromptWorkOverUpdates()
        {
            const int length = 6000;
            string paste = "print \"" + new string('a', length) + "\":length.\r\n";
            Result unbounded = Measure(paste, false);
            Result bounded = Measure(paste, true);
            Console.WriteLine("MEASURE long line, {0} chars, no limits: {1}", paste.Length, unbounded);
            Console.WriteLine("MEASURE long line, {0} chars, game limits: {1}", paste.Length, bounded);

            Assert.AreEqual(length.ToString(), unbounded.LastRow);
            Assert.AreEqual(length.ToString(), bounded.LastRow);
            Assert.AreEqual(paste.Length - 1, unbounded.MaxPromptKeys, "without limits the whole line is typed in one update");
            Assert.LessOrEqual(bounded.MaxPromptKeys, TerminalInputPump.PROMPT_CHARS_PER_UPDATE);
            Assert.LessOrEqual(bounded.MaxTelnetKeys, TerminalInputPump.TELNET_CHARS_PER_UPDATE);
        }

        /// <summary>
        /// Each line runs in its own update, so this takes about 1.5 ms per line; the suite
        /// uses 500. <c>KOSIM_PASTE_LINES=3000</c> reproduces the recorded measurement.
        /// </summary>
        [Test]
        public void ManyLinePasteKeepsTheTypeAheadShort()
        {
            int lines;
            if (!int.TryParse(Environment.GetEnvironmentVariable("KOSIM_PASTE_LINES"), out lines) || lines < 400)
                lines = 500;
            string paste = string.Concat(Enumerable.Range(0, lines).Select(i => "print " + i + ".\r\n").ToArray());
            Result unbounded = Measure(paste, false);
            Result bounded = Measure(paste, true);
            Console.WriteLine("MEASURE {0} lines, {1} chars, no limits: {2}", lines, paste.Length, unbounded);
            Console.WriteLine("MEASURE {0} lines, {1} chars, game limits: {2}", lines, paste.Length, bounded);

            Assert.AreEqual((lines - 1).ToString(), unbounded.LastRow);
            Assert.AreEqual((lines - 1).ToString(), bounded.LastRow);
            Assert.Greater(paste.Length - lines, TerminalInputPump.TELNET_CHARS_PER_UPDATE, "the paste must exceed one update's limit");
            Assert.AreEqual(paste.Length - lines, unbounded.MaxTelnetKeys, "without limits the whole paste moves in one update");
            Assert.LessOrEqual(bounded.MaxTelnetKeys, TerminalInputPump.TELNET_CHARS_PER_UPDATE);
        }
    }
}
