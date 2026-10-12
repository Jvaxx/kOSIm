using System;
using System.Collections.Generic;
using System.Text;

namespace kOS.Safe.UserIO
{
    public enum TelnetInputEventKind
    {
        /// <summary>Decoded client text, with all telnet protocol bytes removed.</summary>
        Text,
        /// <summary>IAC BRK or IAC IP: the client asked to interrupt.</summary>
        Break,
        /// <summary>IAC DO/DONT/WILL/WONT option.</summary>
        Negotiation,
        /// <summary>RFC 1073 NAWS subnegotiation with both 16-bit fields.</summary>
        WindowSize,
        /// <summary>RFC 1091 TERMINAL-TYPE IS subnegotiation.</summary>
        TerminalType,
        /// <summary>Recognized but unused protocol input (NOP, AYT, other options...).</summary>
        Ignored,
        /// <summary>Malformed, truncated or oversized protocol input that was discarded.</summary>
        Malformed
    }

    /// <summary>
    /// One parsed item. Only the fields that belong to <see cref="Kind"/> are meaningful.
    /// </summary>
    public struct TelnetInputEvent
    {
        public TelnetInputEventKind Kind;
        public string Text;        // Text, TerminalType, and the description of Ignored/Malformed
        public byte Command;       // Negotiation verb (DO/DONT/WILL/WONT) or the command/option ignored
        public byte Option;        // Negotiation and subnegotiation option
        public int Width;          // WindowSize, as sent (0 means "unchanged" per RFC 1073)
        public int Height;

        public override string ToString()
        {
            switch (Kind)
            {
                case TelnetInputEventKind.Text: return "Text(" + Text + ")";
                case TelnetInputEventKind.Negotiation: return "Negotiation(" + Command + "," + Option + ")";
                case TelnetInputEventKind.WindowSize: return "WindowSize(" + Width + "x" + Height + ")";
                case TelnetInputEventKind.TerminalType: return "TerminalType(" + Text + ")";
                default: return Kind + "(" + Text + ")";
            }
        }
    }

    /// <summary>
    /// Incremental RFC 854 input parser for one telnet connection. Protocol state and the
    /// UTF-8 decoder persist between <see cref="Feed"/> calls, so commands, subnegotiations
    /// and multi-byte characters may be split across socket reads at any byte.
    /// <br/>
    /// It only parses: replies, terminal state and logging are the caller's job. Not
    /// thread-safe; feed it from a single reader thread.
    /// </summary>
    public sealed class TelnetInputParser
    {
        public const byte SE = 240;
        public const byte NOP = 241;
        public const byte BRK = 243;
        public const byte IP = 244;
        public const byte SB = 250;
        public const byte WILL = 251;
        public const byte WONT = 252;
        public const byte DO = 253;
        public const byte DONT = 254;
        public const byte IAC = 255;

        public const byte OPTION_TERMTYPE = 24;  // RFC 1091
        public const byte OPTION_NAWS = 31;      // RFC 1073
        public const byte TERMTYPE_IS = 0;

        /// <summary>
        /// Subnegotiation payload limit after IAC unescaping. RFC 1091 caps terminal type
        /// names at 40 characters; NAWS needs 4 bytes. Longer payloads are discarded.
        /// </summary>
        public const int MAX_SUBNEGOTIATION_BYTES = 1024;

        private enum State { Data, Iac, Option, SubOption, SubData, SubIac }

        private readonly Decoder decoder = new UTF8Encoding(false, false).GetDecoder();
        private readonly List<byte> textBytes = new List<byte>();
        private readonly List<byte> subData = new List<byte>();
        private State state = State.Data;
        private byte verb;
        private byte subOption;
        private bool subOverflow;

        /// <summary>True when bytes of an unfinished protocol sequence or character are held.</summary>
        public bool HasPendingInput
        {
            get { return state != State.Data || textBytes.Count > 0 || DecoderHasPendingBytes(); }
        }

        /// <summary>
        /// Parse the next chunk of bytes from the client and return the completed events in
        /// stream order. Incomplete trailing sequences stay pending for the next call.
        /// </summary>
        public List<TelnetInputEvent> Feed(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException("buffer");
            if (offset < 0 || count < 0 || offset > buffer.Length - count) throw new ArgumentOutOfRangeException("count");

            var events = new List<TelnetInputEvent>();
            for (int i = offset; i < offset + count; ++i)
                Consume(buffer[i], events);
            FlushText(events, false);
            return events;
        }

        /// <summary>
        /// The client closed its side. Report and reset any unfinished sequence; an
        /// incomplete trailing UTF-8 character decodes to U+FFFD.
        /// </summary>
        public List<TelnetInputEvent> Finish()
        {
            var events = new List<TelnetInputEvent>();
            FlushText(events, true);
            if (state != State.Data)
                events.Add(Malformed("connection ended inside a telnet " + state + " sequence"));
            ResetProtocolState();
            return events;
        }

        private void Consume(byte b, List<TelnetInputEvent> events)
        {
            switch (state)
            {
                case State.Data:
                    if (b == IAC)
                        state = State.Iac;
                    else
                        textBytes.Add(b);
                    break;

                case State.Iac:
                    ConsumeCommand(b, events);
                    break;

                case State.Option:
                    FlushText(events, false);
                    events.Add(new TelnetInputEvent { Kind = TelnetInputEventKind.Negotiation, Command = verb, Option = b });
                    state = State.Data;
                    break;

                case State.SubOption:
                    subOption = b;
                    subData.Clear();
                    subOverflow = false;
                    state = State.SubData;
                    break;

                case State.SubData:
                    if (b == IAC)
                        state = State.SubIac;
                    else
                        AddSubData(b);
                    break;

                case State.SubIac:
                    if (b == IAC)
                    {
                        AddSubData(IAC); // escaped 255 inside the payload
                        state = State.SubData;
                    }
                    else if (b == SE)
                    {
                        state = State.Data;
                        FlushText(events, false);
                        events.Add(CompleteSubnegotiation());
                    }
                    else
                    {
                        // RFC 854 forbids other commands here. Drop the unterminated
                        // subnegotiation and treat this byte as the command it introduces.
                        FlushText(events, false);
                        events.Add(Malformed("subnegotiation of option " + subOption + " interrupted by command " + b));
                        ConsumeCommand(b, events);
                    }
                    break;
            }
        }

        private void ConsumeCommand(byte command, List<TelnetInputEvent> events)
        {
            state = State.Data;
            switch (command)
            {
                case IAC:
                    textBytes.Add(IAC); // escaped data byte 255
                    break;
                case DO:
                case DONT:
                case WILL:
                case WONT:
                    verb = command;
                    state = State.Option;
                    break;
                case SB:
                    state = State.SubOption;
                    break;
                case BRK:
                case IP:
                    FlushText(events, false);
                    events.Add(new TelnetInputEvent { Kind = TelnetInputEventKind.Break, Command = command });
                    break;
                default:
                    FlushText(events, false);
                    events.Add(new TelnetInputEvent
                    {
                        Kind = command <= SE ? TelnetInputEventKind.Malformed : TelnetInputEventKind.Ignored,
                        Command = command,
                        Text = "command " + command
                    });
                    break;
            }
        }

        private void AddSubData(byte b)
        {
            if (subData.Count < MAX_SUBNEGOTIATION_BYTES)
                subData.Add(b);
            else
                subOverflow = true;
        }

        private TelnetInputEvent CompleteSubnegotiation()
        {
            if (subOverflow)
                return Malformed("subnegotiation of option " + subOption + " exceeded " + MAX_SUBNEGOTIATION_BYTES + " bytes");

            switch (subOption)
            {
                case OPTION_NAWS:
                    if (subData.Count != 4)
                        return Malformed("window size subnegotiation had " + subData.Count + " bytes instead of 4");
                    return new TelnetInputEvent
                    {
                        Kind = TelnetInputEventKind.WindowSize,
                        Option = subOption,
                        Width = (subData[0] << 8) | subData[1],
                        Height = (subData[2] << 8) | subData[3]
                    };
                case OPTION_TERMTYPE:
                    if (subData.Count < 1 || subData[0] != TERMTYPE_IS)
                        return Malformed("terminal type subnegotiation was not an IS reply");
                    return new TelnetInputEvent
                    {
                        Kind = TelnetInputEventKind.TerminalType,
                        Option = subOption,
                        Text = Encoding.UTF8.GetString(subData.ToArray(), 1, subData.Count - 1)
                    };
                default:
                    return new TelnetInputEvent
                    {
                        Kind = TelnetInputEventKind.Ignored,
                        Command = SB,
                        Option = subOption,
                        Text = "subnegotiation of option " + subOption
                    };
            }
        }

        private void FlushText(List<TelnetInputEvent> events, bool endOfStream)
        {
            if (textBytes.Count == 0 && !endOfStream)
                return;

            byte[] bytes = textBytes.ToArray();
            textBytes.Clear();
            // The decoder keeps the bytes of an incomplete character for the next call:
            var chars = new char[decoder.GetCharCount(bytes, 0, bytes.Length, endOfStream)];
            decoder.GetChars(bytes, 0, bytes.Length, chars, 0, endOfStream);
            if (chars.Length > 0)
                events.Add(new TelnetInputEvent { Kind = TelnetInputEventKind.Text, Text = new string(chars) });
        }

        private bool DecoderHasPendingBytes()
        {
            // GetCharCount does not change decoder state; a flushing count of no new
            // bytes is nonzero only when an incomplete character is being held.
            return decoder.GetCharCount(new byte[0], 0, 0, true) > 0;
        }

        private void ResetProtocolState()
        {
            state = State.Data;
            subData.Clear();
            subOverflow = false;
        }

        private static TelnetInputEvent Malformed(string description)
        {
            return new TelnetInputEvent { Kind = TelnetInputEventKind.Malformed, Text = description };
        }
    }
}
