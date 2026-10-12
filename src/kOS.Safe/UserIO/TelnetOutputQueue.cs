using System;
using System.Text;

namespace kOS.Safe.UserIO
{
    /// <summary>
    /// Output waiting to be sent to one telnet client. kOS writes to it (mostly from the
    /// game thread) and the connection's writer thread takes it; all members are
    /// thread-safe.
    /// <br/>
    /// Almost all telnet output is screen drawing, which kOS can redraw at any time. So
    /// when a slow or stalled client lets the backlog reach <see cref="Capacity"/>, the
    /// unsent backlog is dropped and <see cref="Write"/> returns true: the caller must
    /// then ask for a full repaint, which replaces everything that was dropped. A single
    /// write is never split or refused, so a multi-character terminal command (cursor
    /// move, title, resize) is always sent whole; the backlog can therefore exceed the
    /// capacity by at most one write. A <see cref="UnicodeCommand.DIE"/> in dropped output
    /// is still reported by <see cref="TakeAll"/>.
    /// </summary>
    public sealed class TelnetOutputQueue
    {
        /// <summary>
        /// Default backlog, several full repaints of the largest screen (255x160), so normal
        /// bursts never drop anything.
        /// </summary>
        public const int DEFAULT_CAPACITY = 262144;

        private readonly object sync = new object();
        private readonly StringBuilder pending = new StringBuilder();
        private bool diePending;
        private long droppedTotal;
        private int overflowCount;
        private int highWaterMark;

        public TelnetOutputQueue(int capacity = DEFAULT_CAPACITY)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException("capacity", "must be at least 1");
            Capacity = capacity;
        }

        public int Capacity { get; private set; }

        public int Count { get { lock (sync) return pending.Length; } }

        /// <summary>Characters dropped because the backlog was full, since creation.</summary>
        public long DroppedCount { get { lock (sync) return droppedTotal; } }

        /// <summary>How many times the backlog was dropped.</summary>
        public int OverflowCount { get { lock (sync) return overflowCount; } }

        /// <summary>Largest backlog seen.</summary>
        public int HighWaterMark { get { lock (sync) return highWaterMark; } }

        /// <summary>
        /// Queue output. Returns true if the unsent backlog had to be dropped to stay within
        /// <see cref="Capacity"/>; the client then needs a full repaint.
        /// </summary>
        public bool Write(string text)
        {
            if (text == null) throw new ArgumentNullException("text");
            if (text.Length == 0)
                return false;
            lock (sync)
            {
                bool dropped = false;
                if (pending.Length > 0 && pending.Length + text.Length > Capacity)
                {
                    for (int i = 0; i < pending.Length && !diePending; ++i)
                        diePending = pending[i] == (char)UnicodeCommand.DIE;
                    droppedTotal += pending.Length;
                    ++overflowCount;
                    pending.Length = 0;
                    dropped = true;
                }
                pending.Append(text);
                if (pending.Length > highWaterMark)
                    highWaterMark = pending.Length;
                return dropped;
            }
        }

        public bool Write(char ch)
        {
            return Write(ch.ToString());
        }

        /// <summary>
        /// Take everything queued. <paramref name="containsDie"/> is true if it, or output
        /// dropped since the last call, contained <see cref="UnicodeCommand.DIE"/>.
        /// </summary>
        public string TakeAll(out bool containsDie)
        {
            lock (sync)
            {
                string content = pending.ToString();
                pending.Length = 0;
                containsDie = diePending || content.IndexOf((char)UnicodeCommand.DIE) >= 0;
                diePending = false;
                return content;
            }
        }
    }
}
