using kOS.Safe.Screen;
using kOS.Safe.UserIO;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace kOS.Safe.Test.UserIO
{
    /// <summary>
    /// Client byte streams in <c>UserIO/Recordings</c> replayed through the real
    /// <see cref="TelnetInputParser"/> and <see cref="TelnetInputDispatcher"/>, checked
    /// against the recorded trace of what reaches the server (<c>.trace</c>), including
    /// kOS's negotiation replies and clamped window sizes.
    /// <br/>
    /// One recording is a live capture; the PuTTY and Windows ones are reconstructed
    /// from those clients' documented behavior, as their headers say.
    /// <br/>
    /// Fragmented variants split the bytes inside each recorded read, with more data
    /// reported available until the read's end, so the break policy (discard the rest of
    /// the read and anything already buffered) applies as it did to the original read.
    /// They must produce the same trace once adjacent text is joined.
    /// <br/>
    /// Re-record traces with <c>KOSIM_RECORD_FIXTURES=1</c> and review the diff.
    /// </summary>
    [TestFixture]
    public class TelnetRecordingTest
    {
        private static readonly string[] Recordings = { "inetutils-xterm", "putty-xterm", "windows-telnet-ansi" };

        [TestCaseSource("Recordings")]
        public void RecordedReadsMatchRecordedTrace(string name)
        {
            List<string> trace = Replay(LoadReads(name));
            string file = Path.Combine(RecordingsDir(), name + ".trace");
            string actual = string.Join("\n", trace.ToArray()) + "\n";
            if (Environment.GetEnvironmentVariable("KOSIM_RECORD_FIXTURES") == "1")
            {
                File.WriteAllText(file, actual);
                Assert.Fail("Recorded " + name + "; rerun without KOSIM_RECORD_FIXTURES to check.");
            }
            Assert.AreEqual(File.ReadAllText(file), actual);
        }

        [TestCaseSource("Recordings")]
        public void EverySplitInsideEachReadGivesTheSameTrace(string name)
        {
            List<byte[]> reads = LoadReads(name);
            List<string> expected = JoinText(Replay(reads));
            for (int r = 0; r < reads.Count; ++r)
            {
                for (int split = 1; split < reads[r].Length; ++split)
                {
                    var chunks = reads.Select(read => new List<byte[]> { read }).ToList();
                    chunks[r] = new List<byte[]> { reads[r].Take(split).ToArray(), reads[r].Skip(split).ToArray() };
                    Assert.AreEqual(expected, JoinText(Replay(chunks)), "read " + r + " split at " + split);
                }
            }
        }

        [TestCaseSource("Recordings")]
        public void ByteAtATimeGivesTheSameTrace(string name)
        {
            List<byte[]> reads = LoadReads(name);
            var chunks = reads.Select(read => read.Select(b => new[] { b }).ToList()).ToList();
            Assert.AreEqual(JoinText(Replay(reads)), JoinText(Replay(chunks)));
        }

        [TestCaseSource("Recordings")]
        public void SeededRandomChunksGiveTheSameTrace(string name)
        {
            List<byte[]> reads = LoadReads(name);
            List<string> expected = JoinText(Replay(reads));
            foreach (int seed in new[] { 3, 17, 2026 })
            {
                var random = new Random(seed);
                for (int iteration = 0; iteration < 50; ++iteration)
                {
                    var chunks = reads.Select(read => RandomChunks(read, random)).ToList();
                    Assert.AreEqual(expected, JoinText(Replay(chunks)), "seed " + seed + ", iteration " + iteration);
                }
            }
        }

        [Test]
        public void InetutilsCaptureNegotiatesCharacterModeAndReportsItsTerminal()
        {
            List<string> trace = Replay(LoadReads("inetutils-xterm"));
            CollectionAssert.Contains(trace, "negotiate DO ECHO -> WILL ECHO");
            CollectionAssert.Contains(trace, "negotiate DO SGA -> WILL SGA");
            CollectionAssert.Contains(trace, "resize 80x24");
            CollectionAssert.Contains(trace, "termtype \"XTERM\"");
            CollectionAssert.Contains(trace, "resize 100x30");
        }

        private static List<byte[]> RandomChunks(byte[] read, Random random)
        {
            var chunks = new List<byte[]>();
            int offset = 0;
            while (offset < read.Length)
            {
                int length = Math.Min(read.Length - offset, 1 + random.Next(4));
                chunks.Add(read.Skip(offset).Take(length).ToArray());
                offset += length;
            }
            return chunks;
        }

        private static List<string> Replay(List<byte[]> reads)
        {
            return Replay(reads.Select(read => new List<byte[]> { read }).ToList());
        }

        /// <summary>Each inner list is one recorded read, received in pieces.</summary>
        private static List<string> Replay(List<List<byte[]>> reads)
        {
            var parser = new TelnetInputParser();
            var dispatcher = new TelnetInputDispatcher();
            var sink = new TraceSink();
            foreach (List<byte[]> pieces in reads)
            {
                for (int i = 0; i < pieces.Count; ++i)
                {
                    bool moreDataAvailable = i < pieces.Count - 1;
                    dispatcher.Dispatch(parser.Feed(pieces[i], 0, pieces[i].Length), moreDataAvailable, sink);
                }
            }
            dispatcher.Dispatch(parser.Finish(), false, sink);
            return sink.Trace;
        }

        /// <summary>Join adjacent text lines; escaping is per character, so escaped text concatenates.</summary>
        private static List<string> JoinText(List<string> trace)
        {
            var joined = new List<string>();
            foreach (string line in trace)
            {
                if (IsText(line) && joined.Count > 0 && IsText(joined[joined.Count - 1]))
                    joined[joined.Count - 1] = "text \"" + TextBody(joined[joined.Count - 1]) + TextBody(line) + "\"";
                else
                    joined.Add(line);
            }
            return joined;
        }

        private static bool IsText(string line) { return line.StartsWith("text \""); }
        private static string TextBody(string line) { return line.Substring(6, line.Length - 7); }

        private static string RecordingsDir()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            while (!Directory.Exists(Path.Combine(dir, "src", "kOS.Safe.Test", "UserIO", "Recordings")))
                dir = Directory.GetParent(dir).FullName;
            return Path.Combine(dir, "src", "kOS.Safe.Test", "UserIO", "Recordings");
        }

        /// <summary><c>read:</c> lines hold one socket read in hex; <c>#</c> starts a comment.</summary>
        private static List<byte[]> LoadReads(string name)
        {
            var reads = new List<byte[]>();
            bool sawEof = false;
            foreach (string rawLine in File.ReadAllLines(Path.Combine(RecordingsDir(), name + ".telnet")))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                    continue;
                Assert.IsFalse(sawEof, name + ": data after eof");
                if (line == "eof")
                    sawEof = true;
                else if (line.StartsWith("read:"))
                    reads.Add(line.Substring(5).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(hex => byte.Parse(hex, NumberStyles.HexNumber)).ToArray());
                else
                    Assert.Fail(name + ": unexpected line " + line);
            }
            Assert.IsTrue(sawEof, name + ": recording must end with eof");
            return reads;
        }

        private sealed class TraceSink : ITelnetInputSink
        {
            public readonly List<string> Trace = new List<string>();

            public void EnqueueText(string text) { Trace.Add("text \"" + Escape(text) + "\""); }
            public void EnqueueBreak() { Trace.Add("break"); }

            public void Negotiate(byte verb, byte option)
            {
                string warning;
                byte[] reply = TelnetProtocol.NegotiationReply(verb, option, false, out warning);
                string line = "negotiate " + Name(verb) + " " + OptionName(option) + " -> " +
                    (reply == null ? "no reply" : Describe(reply));
                if (warning != null)
                    line += ", warning " + warning;
                Trace.Add(line);
            }

            public void ResizeRequested(int width, int height)
            {
                int w = TelnetProtocol.NormalizeDimension(width, 0, ScreenBuffer.MAX_COLUMNS);
                int h = TelnetProtocol.NormalizeDimension(height, 0, ScreenBuffer.MAX_ROWS);
                Trace.Add("resize " + width + "x" + height + (w != width || h != height ? " (applied as " + w + "x" + h + ")" : ""));
            }

            public void TerminalTypeReceived(string terminalType) { Trace.Add("termtype \"" + Escape(terminalType) + "\""); }
            public void ProtocolNotice(TelnetInputEvent notice) { Trace.Add("notice " + notice.Kind + ": " + notice.Text); }

            private static string Describe(byte[] reply)
            {
                if (reply.Length == 3)
                    return Name(reply[1]) + " " + OptionName(reply[2]);
                if (reply.Length == 6 && reply[1] == TelnetInputParser.SB && reply[3] == TelnetProtocol.TERMTYPE_SEND)
                    return "SB TERMTYPE SEND";
                return BitConverter.ToString(reply);
            }

            private static string Name(byte verb)
            {
                switch (verb)
                {
                    case TelnetInputParser.WILL: return "WILL";
                    case TelnetInputParser.WONT: return "WONT";
                    case TelnetInputParser.DO: return "DO";
                    case TelnetInputParser.DONT: return "DONT";
                    default: return verb.ToString();
                }
            }

            private static string OptionName(byte option)
            {
                switch (option)
                {
                    case 1: return "ECHO";
                    case 3: return "SGA";
                    case 24: return "TERMTYPE";
                    case 31: return "NAWS";
                    case 32: return "TSPEED";
                    case 39: return "NEW-ENVIRON";
                    default: return "option " + option;
                }
            }

            private static string Escape(string text)
            {
                var sb = new StringBuilder();
                foreach (char ch in text)
                {
                    switch (ch)
                    {
                        case '\r': sb.Append("\\r"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        default:
                            if (ch < 0x20 || ch == 0x7f)
                                sb.AppendFormat("\\x{0:x2}", (int)ch);
                            else
                                sb.Append(ch);
                            break;
                    }
                }
                return sb.ToString();
            }
        }
    }
}
