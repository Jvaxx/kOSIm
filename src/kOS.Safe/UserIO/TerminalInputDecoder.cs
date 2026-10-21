using System.Text;

namespace kOS.Safe.UserIO
{
    /// <summary>Which terminal's key encoding a <see cref="TerminalInputDecoder"/> understands.</summary>
    public enum TerminalInputDialect
    {
        /// <summary>Unknown terminal: control characters only, no escape sequences.</summary>
        Plain,
        /// <summary>VT100/VT102: <c>ESC [</c> sequences, DEL as backspace, BEL.</summary>
        VT100,
        /// <summary>xterm: VT100 plus the 8-bit CSI character U+009B.</summary>
        Xterm
    }

    /// <summary>
    /// Converts what a telnet client types into kOS's internal key characters
    /// (<see cref="UnicodeCommand"/>), keeping state between calls so a key sequence
    /// (<c>ESC [ A</c>) or a line ending (<c>CR LF</c>) split across socket reads is still
    /// read as one key. This is the input half of the terminal mappers in
    /// <c>kOS.UserIO</c>; their per-read conversion lost such keys.
    /// <br/>
    /// Mappings are the upstream mappers' (see <see cref="Decode"/>), with two intentional
    /// differences:
    /// <list type="bullet">
    /// <item>Each CR or LF is one Enter, and only the other one of the pair directly after
    /// it (or a NUL after CR) is absorbed. Upstream also dropped a CR right after
    /// <c>CR LF</c>, so a blank line or a second quick Enter was lost.</item>
    /// <item>Keys arriving in pieces are joined, as described above. An incomplete sequence
    /// is held until <see cref="Flush"/>, which the owner calls when no more input arrived
    /// within a short timeout (a lone ESC key press then still arrives).</item>
    /// </list>
    /// Not thread-safe; the owner serializes calls.
    /// </summary>
    public sealed class TerminalInputDecoder
    {
        /// <summary>
        /// Longest escape sequence held while waiting for its end. Real key sequences are
        /// a few characters; anything longer is passed on as typed.
        /// </summary>
        public const int MaxSequenceLength = 32;

        private const char ESC = (char)0x1b;
        private const char C1_CSI = (char)0x9b;
        private const char BEL = (char)0x07;
        private const char DEL = (char)0x7f;
        private const char NO_PARTNER = '\0';

        private enum SequenceState { None, Escape, Csi }

        private readonly StringBuilder sequence = new StringBuilder(); // raw chars of the pending sequence, introducer included
        private SequenceState state = SequenceState.None;
        private char newlinePartner = NO_PARTNER; // the char that would complete the CR LF / LF CR pair just started

        public TerminalInputDecoder(TerminalInputDialect dialect)
        {
            Dialect = dialect;
        }

        public TerminalInputDialect Dialect { get; private set; }

        /// <summary>True while an escape sequence has started but not ended.</summary>
        public bool HasPendingSequence { get { return state != SequenceState.None; } }

        /// <summary>
        /// True while anything depends on the next input: an unfinished escape sequence,
        /// or a CR/LF whose partner may still follow.
        /// </summary>
        public bool HasPendingState { get { return HasPendingSequence || newlinePartner != NO_PARTNER; } }

        /// <summary>
        /// Convert typed text, returning the keys that are complete so far.
        /// <list type="bullet">
        /// <item>All dialects: BS → DELETELEFT, CR or LF → STARTNEXTLINE, Ctrl-L →
        /// REQUESTREPAINT (Ctrl-R is passed on: the history search), Ctrl-C → BREAK, NUL dropped; DEL → DELETERIGHT (Plain only).</item>
        /// <item>VT100 and xterm: DEL → DELETELEFT, BEL → BEEP, and <c>ESC [</c> + A/B/C/D/H/F
        /// or 1~/3~/4~/5~/6~ → cursor keys, Home, Delete, End, PageUp, PageDown. For any
        /// other <c>ESC [</c> sequence the ESC is dropped and the rest passed on as typed;
        /// an ESC not followed by <c>[</c> is passed on (both as upstream did).</item>
        /// <item>xterm: U+009B works like <c>ESC [</c>, and is likewise dropped when the
        /// sequence is not recognized.</item>
        /// </list>
        /// </summary>
        public string Decode(string text)
        {
            var output = new StringBuilder(text.Length);
            foreach (char ch in text)
                Feed(ch, output);
            return output.ToString();
        }

        /// <summary>
        /// Give up waiting: pass an unfinished sequence on as typed, and treat a following
        /// CR/LF as a new Enter. Returns the released keys (often empty).
        /// </summary>
        public string Flush()
        {
            var output = new StringBuilder();
            if (state != SequenceState.None)
                ReleaseSequence(output);
            newlinePartner = NO_PARTNER;
            return output.ToString();
        }

        /// <summary>Discard any unfinished sequence, e.g. after an interrupt.</summary>
        public void Reset()
        {
            state = SequenceState.None;
            sequence.Length = 0;
            newlinePartner = NO_PARTNER;
        }

        private void Feed(char ch, StringBuilder output)
        {
            switch (state)
            {
                case SequenceState.Escape:
                    if (ch == '[')
                    {
                        sequence.Append(ch);
                        state = SequenceState.Csi;
                        return;
                    }
                    ReleaseSequence(output);
                    break; // and handle ch normally below

                case SequenceState.Csi:
                    if (ch >= (char)0x20 && ch <= (char)0x3f) // parameter and intermediate bytes (ECMA-48)
                    {
                        if (sequence.Length < MaxSequenceLength)
                        {
                            sequence.Append(ch);
                            return;
                        }
                        ReleaseSequence(output);
                        break;
                    }
                    if (ch >= (char)0x40 && ch <= (char)0x7e) // final byte
                    {
                        sequence.Append(ch);
                        CompleteSequence(output);
                        return;
                    }
                    ReleaseSequence(output); // not a sequence after all
                    break;
            }
            Start(ch, output);
        }

        private void Start(char ch, StringBuilder output)
        {
            if (Dialect != TerminalInputDialect.Plain && ch == ESC)
            {
                BeginSequence(ch, SequenceState.Escape);
                return;
            }
            if (Dialect == TerminalInputDialect.Xterm && ch == C1_CSI)
            {
                BeginSequence(ch, SequenceState.Csi);
                return;
            }
            if (Dialect != TerminalInputDialect.Plain)
            {
                if (ch == DEL)
                    ch = (char)UnicodeCommand.DELETELEFT; // VT100 sends DEL for the backspace key
                else if (ch == BEL)
                    ch = (char)UnicodeCommand.BEEP;
            }
            Basic(ch, output);
        }

        private void BeginSequence(char introducer, SequenceState newState)
        {
            sequence.Length = 0;
            sequence.Append(introducer);
            state = newState;
        }

        /// <summary>The control-character mappings every dialect shares.</summary>
        private void Basic(char ch, StringBuilder output)
        {
            if (newlinePartner != NO_PARTNER)
            {
                char partner = newlinePartner;
                newlinePartner = NO_PARTNER;
                if (ch == partner || (ch == '\0' && partner == '\n'))
                    return; // second half of CR LF, LF CR or CR NUL
            }

            switch (ch)
            {
                case (char)0x08: // Ctrl-H, the ASCII backspace
                    output.Append((char)UnicodeCommand.DELETELEFT);
                    break;
                case DEL: // only reaches here in the Plain dialect
                    output.Append((char)UnicodeCommand.DELETERIGHT);
                    break;
                case '\r':
                    output.Append((char)UnicodeCommand.STARTNEXTLINE);
                    newlinePartner = '\n';
                    break;
                case '\n':
                    output.Append((char)UnicodeCommand.STARTNEXTLINE);
                    newlinePartner = '\r';
                    break;
                case (char)0x0c: // Ctrl-L (upstream also took Ctrl-R; it is the history search since Phase 3)
                    output.Append((char)UnicodeCommand.REQUESTREPAINT);
                    break;
                case (char)0x03: // Ctrl-C, same as a telnet interrupt
                    output.Append((char)UnicodeCommand.BREAK);
                    break;
                case '\0':
                    break;
                default:
                    output.Append(ch);
                    break;
            }
        }

        private void CompleteSequence(StringBuilder output)
        {
            char key;
            if (TryRecognize(out key))
            {
                state = SequenceState.None;
                sequence.Length = 0;
                Basic(key, output);
            }
            else
            {
                ReleaseSequence(output);
            }
        }

        private bool TryRecognize(out char key)
        {
            key = '\0';
            int bodyStart = sequence[0] == C1_CSI ? 1 : 2; // after U+009B or ESC [
            int bodyLength = sequence.Length - bodyStart - 1;
            char final = sequence[sequence.Length - 1];

            if (bodyLength == 0)
                key = VT100Keys.ForFinal(final);
            else if (bodyLength == 1 && final == '~') // VT220 editing keys, ESC [ n ~
                key = VT100Keys.ForTilde(sequence[bodyStart]);
            return key != '\0';
        }

        /// <summary>
        /// Pass the pending sequence on as typed characters: a lone ESC as is, but without
        /// the introducer (ESC or U+009B) of an unrecognized control sequence, as upstream did.
        /// </summary>
        private void ReleaseSequence(StringBuilder output)
        {
            string raw = sequence.ToString();
            bool dropIntroducer = state == SequenceState.Csi;
            state = SequenceState.None;
            sequence.Length = 0;
            // The rest can't start another sequence (only ESC or U+009B begin one, and only
            // first), so it is plain text:
            for (int i = dropIntroducer ? 1 : 0; i < raw.Length; ++i)
                Basic(raw[i], output);
        }
    }
}
