using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using kOS.Safe.UserIO;
using NUnit.Framework;

namespace kOS.Safe.Test.UserIO
{
    /// <summary>
    /// <see cref="InputBudget"/> and the terminal window's per-update input loops in
    /// <see cref="TerminalInputPump"/>.
    /// </summary>
    [TestFixture]
    public class TerminalInputPumpTest
    {
        private const char REPAINT = (char)UnicodeCommand.REQUESTREPAINT;
        private const char RESIZE = (char)UnicodeCommand.RESIZESCREEN;

        private static InputBudget Budget(int chars) { return new InputBudget(chars, TimeSpan.FromHours(1)); }

        private static TerminalInputPump.KeySource Source(TelnetInputQueue queue) { return queue.TryDequeue; }

        [Test]
        public void BudgetLimitsCharacters()
        {
            var budget = Budget(3);
            Assert.IsTrue(budget.TryTake());
            Assert.IsTrue(budget.TryTake());
            Assert.IsTrue(budget.HasRemaining);
            Assert.IsTrue(budget.TryTake());
            Assert.IsFalse(budget.HasRemaining);
            Assert.IsFalse(budget.TryTake());
            Assert.AreEqual(3, budget.Taken);
        }

        [Test]
        public void BudgetLimitsTimeButAlwaysAllowsTheFirstCharacter()
        {
            TimeSpan now = TimeSpan.FromMilliseconds(10); // already past the limit
            var budget = new InputBudget(100, TimeSpan.FromMilliseconds(4), () => now);
            Assert.IsTrue(budget.TryTake(), "the first key always gets through");
            Assert.IsFalse(budget.TryTake());

            now = TimeSpan.Zero;
            budget = new InputBudget(100, TimeSpan.FromMilliseconds(4), () => now);
            Assert.IsTrue(budget.TryTake());
            now = TimeSpan.FromTicks(39000); // 3.9 ms
            Assert.IsTrue(budget.TryTake());
            now = TimeSpan.FromMilliseconds(4);
            Assert.IsFalse(budget.TryTake());
            Assert.AreEqual(2, budget.Taken);
        }

        [Test]
        public void BudgetNeedsAtLeastOneCharacter()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Budget(0));
            Assert.IsTrue(new InputBudget(1, TimeSpan.FromMilliseconds(4)).TryTake(), "real stopwatch");
        }

        private sealed class Terminal
        {
            public readonly Queue<char> TypeAhead = new Queue<char>();
            public readonly List<string> Processed = new List<string>();

            public void Process(int client, char ch)
            {
                Processed.Add(client + ":" + ch);
                if (ch != REPAINT && ch != RESIZE && ch >= ' ')
                    TypeAhead.Enqueue(ch);
            }

            public string From(int client)
            {
                return string.Concat(Processed.Where(p => p.StartsWith(client + ":")).Select(p => p.Substring(2)).ToArray());
            }
        }

        private static int Drain(IList<TelnetInputQueue> queues, ref int nextStart, Terminal terminal, int budgetChars = 4,
            int typeAheadLimit = 1000)
        {
            return TerminalInputPump.DrainTelnets(queues.Select(Source).ToList(), ref nextStart,
                () => terminal.TypeAhead.Count, typeAheadLimit, () => Budget(budgetChars), terminal.Process);
        }

        private static TelnetInputQueue QueueWith(string text)
        {
            var queue = new TelnetInputQueue();
            queue.EnqueueText(text);
            return queue;
        }

        [Test]
        public void EachClientGetsItsOwnBudgetPerUpdate()
        {
            var queues = new[] { QueueWith("abcdefgh"), QueueWith("ABCDEFGH") };
            var terminal = new Terminal();
            int next = 0;
            Assert.AreEqual(8, Drain(queues, ref next, terminal));
            Assert.AreEqual("abcd", terminal.From(0));
            Assert.AreEqual("ABCD", terminal.From(1));
            Assert.AreEqual(4, queues[0].TextCount, "the rest waits, in order, for later updates");
            Drain(queues, ref next, terminal);
            Assert.AreEqual("abcdefgh", terminal.From(0));
            Assert.AreEqual(0, Drain(queues, ref next, terminal));
        }

        [Test]
        public void ClientsTakeTurnsAtGoingFirst()
        {
            var queues = new[] { QueueWith("aaaa"), QueueWith("bbbb"), QueueWith("cccc") };
            var terminal = new Terminal();
            int next = 0;
            var firsts = new StringBuilder();
            for (int update = 0; update < 4; ++update)
            {
                terminal.Processed.Clear();
                Drain(queues, ref next, terminal, 1);
                firsts.Append(terminal.Processed[0][0]);
                Assert.AreEqual(3, terminal.Processed.Count, "one key each per update");
            }
            Assert.AreEqual("0120", firsts.ToString());
        }

        [Test]
        public void ANoisyClientCannotStarveAQuietOne()
        {
            var queues = new[] { QueueWith(new string('x', 100000)), QueueWith("q") };
            var terminal = new Terminal();
            int next = 0;
            Drain(queues, ref next, terminal, 4096, int.MaxValue);
            Assert.AreEqual("q", terminal.From(1));
            Assert.AreEqual(4096, terminal.From(0).Length);
        }

        [Test]
        public void OutOfRangeStartIndexIsWrapped()
        {
            var queues = new[] { QueueWith("a"), QueueWith("b") };
            var terminal = new Terminal();
            int next = -3;
            Drain(queues, ref next, terminal);
            Assert.AreEqual("1:b", terminal.Processed[0]);
            Assert.AreEqual(0, next);
            next = 7;
            Assert.AreEqual(0, Drain(new TelnetInputQueue[0], ref next, terminal));
            Assert.AreEqual(7, next);
        }

        [Test]
        public void FullTypeAheadHoldsTextButNotControls()
        {
            var queue = QueueWith("abcdef");
            var terminal = new Terminal();
            int next = 0;
            Drain(new[] { queue }, ref next, terminal, 100, 3);
            Assert.AreEqual("abc", terminal.From(0));
            Assert.AreEqual(3, queue.TextCount);

            queue.EnqueueResize(90, 30);
            queue.RequestRepaint();
            terminal.Processed.Clear();
            Drain(new[] { queue }, ref next, terminal, 100, 3);
            Assert.AreEqual(new string(new[] { RESIZE, (char)90, (char)30, REPAINT }), terminal.From(0));
            Assert.AreEqual(3, queue.TextCount, "text still held");

            terminal.TypeAhead.Clear(); // the prompt consumed it
            Drain(new[] { queue }, ref next, terminal, 100, 3);
            Assert.AreEqual(new string(new[] { RESIZE, (char)90, (char)30, REPAINT }) + "def", terminal.From(0));
        }

        [Test]
        public void ControlsAreProcessedAfterTheBudgetIsSpent()
        {
            var queue = QueueWith("abcdef");
            var terminal = new Terminal();
            int next = 0;
            Drain(new[] { queue }, ref next, terminal, 2);
            queue.RequestRepaint();
            // Arrives "during" the update: the loop keeps asking for controls after the budget.
            var sources = new List<TerminalInputPump.KeySource>
            {
                (bool includeText, out char ch) =>
                {
                    bool got = queue.TryDequeue(includeText, out ch);
                    if (got && ch == 'c') queue.RequestRepaint();
                    return got;
                }
            };
            terminal.Processed.Clear();
            // A control read while text is allowed uses budget too (negligible at real limits).
            TerminalInputPump.DrainTelnets(sources, ref next, () => 0, 1000, () => Budget(2), terminal.Process);
            Assert.AreEqual(REPAINT + "c" + REPAINT, terminal.From(0));
        }

        private sealed class Prompt
        {
            public bool Waiting = true;
            public readonly StringBuilder Typed = new StringBuilder();
            public Func<char, bool> OnKey;

            public bool Process(char key)
            {
                if (OnKey != null && !OnKey(key))
                    return false;
                Typed.Append(key);
                if (key == '\r')
                    Waiting = false; // a command started running
                return true;
            }
        }

        [Test]
        public void FeedPromptStopsWhenACommandStarts()
        {
            var typeAhead = new Queue<char>("one\rtwo\r");
            var prompt = new Prompt();
            Assert.AreEqual(4, TerminalInputPump.FeedPrompt(typeAhead, () => prompt.Waiting, prompt.Process, Budget(100)));
            Assert.AreEqual("one\r", prompt.Typed.ToString());
            Assert.AreEqual("two\r", new string(typeAhead.ToArray()));
            Assert.AreEqual(0, TerminalInputPump.FeedPrompt(typeAhead, () => prompt.Waiting, prompt.Process, Budget(100)));
        }

        [Test]
        public void FeedPromptKeepsAKeyThatWasNotAccepted()
        {
            var typeAhead = new Queue<char>("abc");
            var prompt = new Prompt { OnKey = key => key != 'b' };
            Assert.AreEqual(1, TerminalInputPump.FeedPrompt(typeAhead, () => prompt.Waiting, prompt.Process, Budget(100)));
            Assert.AreEqual("bc", new string(typeAhead.ToArray()));
        }

        [Test]
        public void FeedPromptStaysWithinItsBudget()
        {
            var typeAhead = new Queue<char>(new string('x', 5000));
            var prompt = new Prompt();
            Assert.AreEqual(2048, TerminalInputPump.FeedPrompt(typeAhead, () => prompt.Waiting, prompt.Process,
                Budget(TerminalInputPump.PROMPT_CHARS_PER_UPDATE)));
            Assert.AreEqual(5000 - 2048, typeAhead.Count);
        }

        [Test]
        public void FeedPromptCopesWithAKeyThatWipesTheTypeAhead()
        {
            var typeAhead = new Queue<char>("a!bc");
            var prompt = new Prompt();
            prompt.OnKey = key =>
            {
                if (key == '!') typeAhead.Clear();
                return true;
            };
            Assert.AreEqual(2, TerminalInputPump.FeedPrompt(typeAhead, () => prompt.Waiting, prompt.Process, Budget(100)));
            Assert.AreEqual("a!", prompt.Typed.ToString());
            Assert.AreEqual(0, typeAhead.Count);
        }

        [Test]
        public void DefaultLimits()
        {
            Assert.AreEqual(4096, TerminalInputPump.TELNET_CHARS_PER_UPDATE);
            Assert.AreEqual(4096, TerminalInputPump.TYPE_AHEAD_LIMIT);
            Assert.AreEqual(2048, TerminalInputPump.PROMPT_CHARS_PER_UPDATE);
            Assert.AreEqual(TimeSpan.FromMilliseconds(4), TerminalInputPump.TimePerUpdate);
        }
    }
}
