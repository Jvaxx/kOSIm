using System;

namespace kOS.Safe.UserIO
{
    /// <summary>
    /// Turns one telnet client's typed text into keys in its <see cref="TelnetInputQueue"/>,
    /// decoding with the terminal type the client reported. Called from the connection's
    /// reader thread (text, interrupts, terminal type) and the game thread
    /// (<see cref="FlushIfIdle"/>); all members are thread-safe.
    /// <br/>
    /// Times are any monotonic clock (the server uses a <c>Stopwatch</c>); passing them in
    /// keeps the timeout testable.
    /// </summary>
    public sealed class TelnetInputPipeline
    {
        /// <summary>
        /// How long an unfinished key sequence (a lone ESC, or ESC [) waits for the rest
        /// before it is passed on as typed. The parts of one key normally arrive within
        /// milliseconds; a person pressing Escape will not notice a tenth of a second.
        /// </summary>
        public static readonly TimeSpan DefaultSequenceTimeout = TimeSpan.FromMilliseconds(100);

        private readonly object sync = new object();
        private readonly TimeSpan sequenceTimeout;
        private TerminalInputDecoder decoder;
        private TimeSpan lastTextTime;

        public TelnetInputPipeline(TelnetInputQueue queue, string terminalType, TimeSpan sequenceTimeout)
        {
            if (queue == null) throw new ArgumentNullException("queue");
            Queue = queue;
            this.sequenceTimeout = sequenceTimeout;
            decoder = new TerminalInputDecoder(TerminalTypeNames.InputDialectFor(terminalType));
        }

        public TelnetInputQueue Queue { get; private set; }

        public TerminalInputDialect Dialect { get { lock (sync) return decoder.Dialect; } }

        /// <summary>
        /// The client reported its terminal type. If that changes the key encoding, a key
        /// sequence still unfinished in the old encoding is passed on as typed first.
        /// </summary>
        public void SetTerminalType(string terminalType)
        {
            TerminalInputDialect dialect = TerminalTypeNames.InputDialectFor(terminalType);
            lock (sync)
            {
                if (dialect == decoder.Dialect)
                    return;
                Queue.EnqueueText(decoder.Flush());
                decoder = new TerminalInputDecoder(dialect);
            }
        }

        public void AddText(string text, TimeSpan now)
        {
            lock (sync)
            {
                Queue.EnqueueText(decoder.Decode(text));
                lastTextTime = now;
            }
        }

        /// <summary>A telnet interrupt: an unfinished key sequence is type-ahead and is discarded too.</summary>
        public void Break()
        {
            lock (sync)
            {
                decoder.Reset();
                Queue.EnqueueBreak();
            }
        }

        /// <summary>
        /// Called regularly: once no text has arrived for the sequence timeout, pass on an
        /// unfinished key sequence as typed. Returns true if it flushed.
        /// </summary>
        public bool FlushIfIdle(TimeSpan now)
        {
            lock (sync)
            {
                if (!decoder.HasPendingState || now - lastTextTime < sequenceTimeout)
                    return false;
                Queue.EnqueueText(decoder.Flush());
                return true;
            }
        }
    }
}
