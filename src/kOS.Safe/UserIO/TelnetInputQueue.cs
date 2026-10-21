using System;
using System.Collections.Generic;
using System.Text;

namespace kOS.Safe.UserIO
{
    /// <summary>
    /// The keys one telnet client has typed that kOS has not read yet. The connection's
    /// reader thread adds to it and the game thread reads from it; all members are
    /// thread-safe.
    /// <br/>
    /// Typed text is kept in order, up to <see cref="Capacity"/> characters. Control
    /// events do not wait behind it and are coalesced, so they cannot fill the queue:
    /// <list type="bullet">
    /// <item><b>Interrupt</b> (BREAK, from Ctrl-C or the telnet IP command): discards the
    /// text typed before it, as the terminal discards its type-ahead on an interrupt,
    /// and is read before anything else. Text queued after it is kept; but after a telnet
    /// BRK or IP command, <see cref="TelnetInputDispatcher"/> has already dropped the text of
    /// the same read and of the reads that follow until the socket is drained, as upstream
    /// did; only after a Ctrl-C sent as a character (0x03) is the text that follows kept.</item>
    /// <item><b>Resize</b> (RESIZESCREEN width height): only the latest size is kept, read
    /// before text.</item>
    /// <item><b>Repaint request</b> (REQUESTREPAINT, from Ctrl-L or kOS itself):
    /// read before text.</item>
    /// </list>
    /// When the text backlog is full, further text is discarded until the backlog drains
    /// to half its capacity, so input resumes at a clean point rather than mid-word. Each
    /// such episode is reported once at its start and once at its end
    /// (<see cref="TakeOverflowStarted"/>, <see cref="TakeOverflowEnded"/>) so the
    /// client can be told; interrupts always get through.
    /// </summary>
    public sealed class TelnetInputQueue
    {
        /// <summary>Default text backlog: far beyond any paste, small in memory (512 KiB).</summary>
        public const int DEFAULT_CAPACITY = 262144;

        private readonly object sync = new object();
        private readonly Queue<char> text = new Queue<char>();
        private readonly Queue<char> controls = new Queue<char>(); // control events being read out
        private bool resizePending;
        private int resizeWidth;
        private int resizeHeight;
        private bool breakPending;
        private bool repaintPending;

        private bool overflowing;
        private bool overflowStartedUnreported;
        private bool overflowEndedUnreported;
        private long droppedThisEpisode;
        private long droppedUnreported; // by episodes that ended since TakeOverflowEnded

        private long droppedTotal;
        private long discardedByBreak;
        private int overflowCount;
        private int highWaterMark;

        public TelnetInputQueue(int capacity = DEFAULT_CAPACITY)
        {
            if (capacity < 2) throw new ArgumentOutOfRangeException("capacity", "must be at least 2");
            Capacity = capacity;
        }

        /// <summary>Most characters of typed text held at once.</summary>
        public int Capacity { get; private set; }

        /// <summary>Typed text waiting to be read.</summary>
        public int TextCount { get { lock (sync) return text.Count; } }

        /// <summary>Everything waiting to be read, control events included.</summary>
        public int Count { get { lock (sync) return text.Count + ControlCountLocked(); } }

        public bool IsOverflowing { get { lock (sync) return overflowing; } }

        /// <summary>Typed characters discarded because the backlog was full, since creation.</summary>
        public long DroppedCount { get { lock (sync) return droppedTotal; } }

        /// <summary>Typed characters discarded by interrupts (type-ahead), since creation.</summary>
        public long DiscardedByBreakCount { get { lock (sync) return discardedByBreak; } }

        /// <summary>How many times the backlog filled up.</summary>
        public int OverflowCount { get { lock (sync) return overflowCount; } }

        /// <summary>Largest text backlog seen.</summary>
        public int HighWaterMark { get { lock (sync) return highWaterMark; } }

        /// <summary>
        /// Add keys produced by a <see cref="TerminalInputDecoder"/>. BREAK and REQUESTREPAINT
        /// characters in them act as the control events described on the class.
        /// </summary>
        public void EnqueueText(string keys)
        {
            lock (sync)
            {
                foreach (char ch in keys)
                {
                    switch (ch)
                    {
                        case (char)UnicodeCommand.BREAK:
                            BreakLocked();
                            break;
                        case (char)UnicodeCommand.REQUESTREPAINT:
                            repaintPending = true;
                            break;
                        default:
                            AddTextLocked(ch);
                            break;
                    }
                }
            }
        }

        /// <summary>An interrupt from the telnet protocol (IP, BRK).</summary>
        public void EnqueueBreak()
        {
            lock (sync) BreakLocked();
        }

        /// <summary>The client's window size changed; replaces any size not read yet.</summary>
        public void EnqueueResize(int width, int height)
        {
            if (width < 0 || width > char.MaxValue) throw new ArgumentOutOfRangeException("width");
            if (height < 0 || height > char.MaxValue) throw new ArgumentOutOfRangeException("height");
            lock (sync)
            {
                resizePending = true;
                resizeWidth = width;
                resizeHeight = height;
            }
        }

        /// <summary>Ask the reader to repaint the client's screen.</summary>
        public void RequestRepaint()
        {
            lock (sync) repaintPending = true;
        }

        /// <summary>
        /// Read the next key. Control events come first; typed text only when
        /// <paramref name="includeText"/> is true. A resize's three characters are always
        /// read one after another, before anything else.
        /// </summary>
        public bool TryDequeue(bool includeText, out char ch)
        {
            lock (sync)
            {
                if (controls.Count == 0)
                    FillControlsLocked();
                if (controls.Count > 0)
                {
                    ch = controls.Dequeue();
                    return true;
                }
                if (includeText && text.Count > 0)
                {
                    ch = text.Dequeue();
                    if (overflowing && text.Count <= Capacity / 2)
                        EndOverflowLocked();
                    return true;
                }
                ch = '\0';
                return false;
            }
        }

        /// <summary>True if <see cref="TryDequeue"/> would return a key.</summary>
        public bool HasInput(bool includeText)
        {
            lock (sync)
                return ControlCountLocked() > 0 || (includeText && text.Count > 0);
        }

        /// <summary>Read everything waiting, control events first.</summary>
        public string DequeueAll()
        {
            var sb = new StringBuilder();
            char ch;
            lock (sync)
            {
                while (TryDequeue(true, out ch))
                    sb.Append(ch);
            }
            return sb.ToString();
        }

        /// <summary>True once when the backlog has just filled up and text started being discarded.</summary>
        public bool TakeOverflowStarted()
        {
            lock (sync)
            {
                bool result = overflowStartedUnreported;
                overflowStartedUnreported = false;
                return result;
            }
        }

        /// <summary>
        /// True once when an overflow episode has ended, with how many typed characters it
        /// discarded. Episodes that ended since the last call are reported together.
        /// </summary>
        public bool TakeOverflowEnded(out long dropped)
        {
            lock (sync)
            {
                dropped = droppedUnreported;
                droppedUnreported = 0;
                bool result = overflowEndedUnreported;
                overflowEndedUnreported = false;
                return result;
            }
        }

        private void AddTextLocked(char ch)
        {
            if (!overflowing && text.Count >= Capacity)
            {
                overflowing = true;
                overflowStartedUnreported = true;
                droppedThisEpisode = 0;
                ++overflowCount;
            }
            if (overflowing)
            {
                ++droppedThisEpisode;
                ++droppedTotal;
                return;
            }
            text.Enqueue(ch);
            if (text.Count > highWaterMark)
                highWaterMark = text.Count;
        }

        private void BreakLocked()
        {
            discardedByBreak += text.Count;
            text.Clear();
            if (overflowing)
                EndOverflowLocked();
            breakPending = true;
        }

        private void EndOverflowLocked()
        {
            overflowing = false;
            overflowEndedUnreported = true;
            droppedUnreported += droppedThisEpisode;
        }

        private int ControlCountLocked()
        {
            return controls.Count + (resizePending ? 3 : 0) + (breakPending ? 1 : 0) + (repaintPending ? 1 : 0);
        }

        private void FillControlsLocked()
        {
            if (resizePending)
            {
                controls.Enqueue((char)UnicodeCommand.RESIZESCREEN);
                controls.Enqueue((char)resizeWidth);
                controls.Enqueue((char)resizeHeight);
                resizePending = false;
            }
            if (breakPending)
            {
                controls.Enqueue((char)UnicodeCommand.BREAK);
                breakPending = false;
            }
            if (repaintPending)
            {
                controls.Enqueue((char)UnicodeCommand.REQUESTREPAINT);
                repaintPending = false;
            }
        }
    }
}
