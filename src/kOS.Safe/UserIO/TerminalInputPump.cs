using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace kOS.Safe.UserIO
{
    /// <summary>
    /// How much input one game update may process: at most a number of characters, and
    /// after the first character, no longer than a time limit. Limits a large paste or a
    /// noisy client to a slice of each frame; the rest waits, in order, for later frames.
    /// </summary>
    public sealed class InputBudget
    {
        private readonly int maxChars;
        private readonly TimeSpan maxTime;
        private readonly Func<TimeSpan> elapsed;
        private int taken;

        public InputBudget(int maxChars, TimeSpan maxTime) : this(maxChars, maxTime, null) { }

        /// <param name="elapsed">time since the budget started; a new stopwatch if null</param>
        public InputBudget(int maxChars, TimeSpan maxTime, Func<TimeSpan> elapsed)
        {
            if (maxChars < 1) throw new ArgumentOutOfRangeException("maxChars");
            this.maxChars = maxChars;
            this.maxTime = maxTime;
            if (elapsed == null)
            {
                Stopwatch watch = Stopwatch.StartNew();
                elapsed = () => watch.Elapsed;
            }
            this.elapsed = elapsed;
        }

        public int Taken { get { return taken; } }

        /// <summary>True if another character may be processed. Always true for the first.</summary>
        public bool HasRemaining
        {
            get { return taken == 0 || (taken < maxChars && elapsed() < maxTime); }
        }

        /// <summary>Use one character of the budget, if any is left.</summary>
        public bool TryTake()
        {
            if (!HasRemaining)
                return false;
            ++taken;
            return true;
        }
    }

    /// <summary>
    /// The per-frame input loops of the terminal window, kept here so they can be tested
    /// without Unity.
    /// </summary>
    public static class TerminalInputPump
    {
        /// <summary>Characters one telnet client may hand to its terminal per update.</summary>
        public const int TELNET_CHARS_PER_UPDATE = 4096;

        /// <summary>
        /// Telnet text is moved into the terminal's type-ahead only while that holds fewer
        /// than this many characters; the rest stays in the (bounded) telnet queue. Control
        /// events are never held back.
        /// </summary>
        public const int TYPE_AHEAD_LIMIT = 4096;

        /// <summary>Type-ahead characters fed to the prompt per update (a paste of one long line).</summary>
        public const int PROMPT_CHARS_PER_UPDATE = 2048;

        /// <summary>Time limit for each of the loops above, per update.</summary>
        public static readonly TimeSpan TimePerUpdate = TimeSpan.FromMilliseconds(4);

        /// <summary>Reads the next key from a telnet client; text only if <paramref name="includeText"/>.</summary>
        public delegate bool KeySource(bool includeText, out char ch);

        /// <summary>
        /// Move telnet input into the terminal, one client after another starting at
        /// <paramref name="nextStart"/>, which advances each call so clients take turns at
        /// filling the shared type-ahead. Each client gets its own budget. Control events
        /// (interrupts, resizes, repaints) are always processed.
        /// </summary>
        /// <param name="process">called with the client's index and the key</param>
        /// <returns>how many keys were processed</returns>
        public static int DrainTelnets(IList<KeySource> sources, ref int nextStart, Func<int> typeAheadCount,
            int typeAheadLimit, Func<InputBudget> newBudget, Action<int, char> process)
        {
            int count = sources.Count;
            if (count == 0)
                return 0;
            int start = ((nextStart % count) + count) % count;
            nextStart = (start + 1) % count;

            int processed = 0;
            for (int i = 0; i < count; ++i)
            {
                int index = (start + i) % count;
                InputBudget budget = newBudget();
                while (true)
                {
                    bool textAllowed = budget.HasRemaining && typeAheadCount() < typeAheadLimit;
                    char ch;
                    if (!sources[index](textAllowed, out ch))
                        break;
                    if (textAllowed)
                        budget.TryTake();
                    process(index, ch);
                    ++processed;
                }
            }
            return processed;
        }

        /// <summary>
        /// Feed queued type-ahead to the interpreter while it waits for a command, as the
        /// terminal window does every update, within <paramref name="budget"/>. A key the
        /// interpreter does not accept stays queued.
        /// </summary>
        /// <returns>how many keys were consumed</returns>
        public static int FeedPrompt(Queue<char> typeAhead, Func<bool> isWaitingForCommand, Func<char, bool> process, InputBudget budget)
        {
            int consumed = 0;
            while (typeAhead.Count > 0 && isWaitingForCommand() && budget.TryTake())
            {
                char key = typeAhead.Peek();
                if (!process(key))
                    break; // do not consume more until it accepts input again
                if (typeAhead.Count > 0) // an interrupt may have wiped the type-ahead
                    typeAhead.Dequeue();
                ++consumed;
            }
            return consumed;
        }
    }
}
