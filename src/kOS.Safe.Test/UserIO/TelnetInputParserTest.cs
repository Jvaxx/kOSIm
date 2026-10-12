using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using kOS.Safe.UserIO;
using NUnit.Framework;

namespace kOS.Safe.Test.UserIO
{
    [TestFixture]
    public class TelnetInputParserTest
    {
        private const byte IAC = TelnetInputParser.IAC;
        private const byte SB = TelnetInputParser.SB;
        private const byte SE = TelnetInputParser.SE;
        private const byte DO = TelnetInputParser.DO;
        private const byte WILL = TelnetInputParser.WILL;
        private const byte IP = TelnetInputParser.IP;
        private const byte NAWS = TelnetInputParser.OPTION_NAWS;
        private const byte TERMTYPE = TelnetInputParser.OPTION_TERMTYPE;

        private static byte[] Bytes(params object[] parts)
        {
            var result = new List<byte>();
            foreach (object part in parts)
            {
                var s = part as string;
                if (s != null)
                    result.AddRange(Encoding.UTF8.GetBytes(s));
                else if (part is byte[])
                    result.AddRange((byte[])part);
                else
                    result.Add(Convert.ToByte(part));
            }
            return result.ToArray();
        }

        /// <summary>Events with adjacent text merged, so chunking differences in text don't matter.</summary>
        private static List<string> Normalize(IEnumerable<TelnetInputEvent> events)
        {
            var result = new List<string>();
            var text = new StringBuilder();
            foreach (TelnetInputEvent ev in events)
            {
                if (ev.Kind == TelnetInputEventKind.Text)
                {
                    text.Append(ev.Text);
                    continue;
                }
                if (text.Length > 0)
                {
                    result.Add("Text(" + text + ")");
                    text.Length = 0;
                }
                result.Add(ev.Kind == TelnetInputEventKind.Malformed || ev.Kind == TelnetInputEventKind.Ignored
                    ? ev.Kind + ":" + ev.Command + ":" + ev.Option
                    : ev.ToString());
            }
            if (text.Length > 0)
                result.Add("Text(" + text + ")");
            return result;
        }

        private static List<string> ParseWhole(byte[] data, bool finish = true)
        {
            var parser = new TelnetInputParser();
            var events = parser.Feed(data, 0, data.Length);
            if (finish)
                events.AddRange(parser.Finish());
            return Normalize(events);
        }

        private static List<string> ParseInChunks(byte[] data, IEnumerable<int> cuts, bool finish = true)
        {
            var parser = new TelnetInputParser();
            var events = new List<TelnetInputEvent>();
            int start = 0;
            foreach (int cut in cuts.Concat(new[] { data.Length }))
            {
                // Feed through an oversized buffer with stale bytes after the valid range:
                var buffer = Enumerable.Repeat((byte)IAC, data.Length + 8).ToArray();
                Array.Copy(data, start, buffer, 3, cut - start);
                events.AddRange(parser.Feed(buffer, 3, cut - start));
                start = cut;
            }
            if (finish)
                events.AddRange(parser.Finish());
            return Normalize(events);
        }

        // A stream mixing every construct the server understands.
        private static readonly byte[] Composite = Bytes(
            "ab", IAC, WILL, TERMTYPE, "é€", IAC, DO, (byte)3, "😀x",
            IAC, SB, NAWS, 1, IAC, IAC, 0, 40, IAC, SE,
            IAC, SB, TERMTYPE, 0, "XTERM", IAC, SE,
            IAC, TelnetInputParser.NOP, "y", IAC, IAC, "z", IAC, IP, "\r\n");

        private static readonly string[] CompositeExpected =
        {
            "Text(ab)", "Negotiation(251,24)", "Text(é€)", "Negotiation(253,3)", "Text(😀x)",
            "WindowSize(511x40)", "TerminalType(XTERM)", "Ignored:241:0", "Text(y�z)", "Break()",
            "Text(\r\n)"
        };

        [Test]
        public void ParsesEveryConstructInOneRead()
        {
            CollectionAssert.AreEqual(CompositeExpected, ParseWhole(Composite));
        }

        [Test]
        public void EverySingleSplitPointGivesTheSameEvents()
        {
            for (int cut = 0; cut <= Composite.Length; ++cut)
                CollectionAssert.AreEqual(CompositeExpected, ParseInChunks(Composite, new[] { cut }), "cut at " + cut);
        }

        [Test]
        public void EveryPairOfSplitPointsGivesTheSameEvents()
        {
            for (int first = 0; first <= Composite.Length; ++first)
                for (int second = first; second <= Composite.Length; ++second)
                    CollectionAssert.AreEqual(CompositeExpected, ParseInChunks(Composite, new[] { first, second }),
                        "cuts at " + first + "," + second);
        }

        [Test]
        public void ByteAtATimeGivesTheSameEvents()
        {
            CollectionAssert.AreEqual(CompositeExpected, ParseInChunks(Composite, Enumerable.Range(1, Composite.Length - 1)));
        }

        [TestCase(1)]
        [TestCase(7)]
        [TestCase(1234)]
        [TestCase(98765)]
        public void SeededRandomStreamsParseIdenticallyWhateverTheFragmentation(int seed)
        {
            var random = new Random(seed);
            byte[] interesting = { IAC, IAC, IAC, SB, SE, DO, WILL, IP, NAWS, TERMTYPE, 0, 0xC3, 0xA9, 0xE2, 0x82, 0xF0, (byte)'a', (byte)'\r' };
            for (int round = 0; round < 200; ++round)
            {
                var data = new byte[random.Next(0, 80)];
                for (int i = 0; i < data.Length; ++i)
                    data[i] = random.Next(3) == 0 ? (byte)random.Next(256) : interesting[random.Next(interesting.Length)];
                int[] cuts = Enumerable.Range(0, random.Next(0, 6)).Select(_ => random.Next(data.Length + 1)).OrderBy(c => c).ToArray();

                List<string> whole = ParseWhole(data);
                CollectionAssert.AreEqual(whole, ParseInChunks(data, cuts),
                    "seed " + seed + " round " + round + " data " + BitConverter.ToString(data) + " cuts " + string.Join(",", cuts));
            }
        }

        [TestCase(WILL, TERMTYPE)] // the upstream scraper leaked 0x18 (Ctrl-X)
        [TestCase(DO, (byte)3)]     // ... and 0x03 (Ctrl-C) for suppress-go-ahead
        [TestCase(DO, (byte)1)]
        [TestCase(DO, NAWS)]
        [TestCase(DO, (byte)99)]    // unknown options must not swallow the next data byte
        public void NegotiationOptionBytesNeverLeakIntoText(byte verb, byte option)
        {
            CollectionAssert.AreEqual(
                new[] { "Negotiation(" + verb + "," + option + ")", "Text(ab)" },
                ParseWhole(Bytes(IAC, verb, option, "ab")));
        }

        [Test]
        public void EscapedIacIsDataAndDoesNotSwallowTheNextByte()
        {
            // 0xFF alone is not valid UTF-8, so it decodes to U+FFFD (as before), but 'a' must survive.
            CollectionAssert.AreEqual(new[] { "Text(�a)" }, ParseWhole(Bytes(IAC, IAC, "a")));
        }

        [Test]
        public void MultipleCommandsInOneReadAreAllReported()
        {
            CollectionAssert.AreEqual(
                new[] { "Negotiation(251,24)", "Negotiation(253,3)", "Negotiation(253,1)", "WindowSize(80x24)", "Text(k)" },
                ParseWhole(Bytes(IAC, WILL, TERMTYPE, IAC, DO, 3, IAC, DO, 1, IAC, SB, NAWS, 0, 80, 0, 24, IAC, SE, "k")));
        }

        [TestCase(0, 0)]
        [TestCase(0, 40)]
        [TestCase(65535, 65535)] // both bytes 255, each sent as IAC IAC
        public void WindowSizeIsReportedAsSent(int width, int height)
        {
            var payload = new List<byte>();
            foreach (int value in new[] { width >> 8, width & 0xFF, height >> 8, height & 0xFF })
            {
                payload.Add((byte)value);
                if (value == IAC)
                    payload.Add(IAC);
            }
            CollectionAssert.AreEqual(new[] { "WindowSize(" + width + "x" + height + ")" },
                ParseWhole(Bytes(IAC, SB, NAWS, payload.ToArray(), IAC, SE)));
        }

        [TestCase(3)]
        [TestCase(5)]
        [TestCase(0)]
        public void WindowSizeWithWrongLengthIsMalformed(int length)
        {
            byte[] payload = Enumerable.Repeat((byte)7, length).ToArray();
            CollectionAssert.AreEqual(new[] { "Malformed:0:0", "Text(q)" },
                ParseWhole(Bytes(IAC, SB, NAWS, payload, IAC, SE, "q")));
        }

        [Test]
        public void TerminalTypeMustBeAnIsReply()
        {
            CollectionAssert.AreEqual(new[] { "Malformed:0:0" }, ParseWhole(Bytes(IAC, SB, TERMTYPE, 1, IAC, SE)));
            CollectionAssert.AreEqual(new[] { "TerminalType()" }, ParseWhole(Bytes(IAC, SB, TERMTYPE, 0, IAC, SE)));
        }

        [Test]
        public void UnknownSubnegotiationIsIgnored()
        {
            CollectionAssert.AreEqual(new[] { "Ignored:250:42", "Text(t)" }, ParseWhole(Bytes(IAC, SB, 42, 1, 2, 3, IAC, SE, "t")));
        }

        [Test]
        public void OversizedSubnegotiationIsDiscardedAndParsingRecovers()
        {
            var name = new string('V', TelnetInputParser.MAX_SUBNEGOTIATION_BYTES + 100);
            CollectionAssert.AreEqual(new[] { "Malformed:0:0", "Text(ok)" },
                ParseWhole(Bytes(IAC, SB, TERMTYPE, 0, name, IAC, SE, "ok")));
        }

        [Test]
        public void SubnegotiationInterruptedByCommandIsDroppedAndCommandHonored()
        {
            CollectionAssert.AreEqual(new[] { "Malformed:0:0", "Break()", "Text(r)" },
                ParseWhole(Bytes(IAC, SB, NAWS, 0, 80, IAC, IP, "r")));
        }

        [TestCase(SE)]
        [TestCase((byte)'x')]
        [TestCase((byte)0)]
        public void InvalidCommandBytesAreMalformedAndTextContinues(byte command)
        {
            CollectionAssert.AreEqual(new[] { "Text(a)", "Malformed:" + command + ":0", "Text(b)" },
                ParseWhole(Bytes("a", IAC, command, "b")));
        }

        [Test]
        public void IncompleteSequencesStayPendingBetweenReads()
        {
            var parser = new TelnetInputParser();
            Assert.IsFalse(parser.HasPendingInput);

            foreach (byte[] partial in new[] { Bytes(IAC), Bytes(IAC, DO), Bytes(IAC, SB), Bytes(IAC, SB, NAWS, 0), Bytes(IAC, SB, NAWS, 0, 80, 0, 24, IAC), Bytes(0xE2, 0x82) })
            {
                parser = new TelnetInputParser();
                CollectionAssert.IsEmpty(parser.Feed(partial, 0, partial.Length), BitConverter.ToString(partial));
                Assert.IsTrue(parser.HasPendingInput, BitConverter.ToString(partial));
            }
        }

        [Test]
        public void EndOfStreamReportsTruncatedProtocolAndResets()
        {
            var parser = new TelnetInputParser();
            byte[] partial = Bytes("x", IAC, SB, NAWS, 0, 80);
            CollectionAssert.AreEqual(new[] { "Text(x)" }, Normalize(parser.Feed(partial, 0, partial.Length)));
            CollectionAssert.AreEqual(new[] { "Malformed:0:0" }, Normalize(parser.Finish()));
            Assert.IsFalse(parser.HasPendingInput);
            byte[] after = Bytes("y");
            CollectionAssert.AreEqual(new[] { "Text(y)" }, Normalize(parser.Feed(after, 0, after.Length)));
        }

        [Test]
        public void EndOfStreamInsideCharacterYieldsReplacementCharacter()
        {
            var parser = new TelnetInputParser();
            byte[] partial = Bytes("a", 0xF0, 0x9F, 0x98);
            CollectionAssert.AreEqual(new[] { "Text(a)" }, Normalize(parser.Feed(partial, 0, partial.Length)));
            CollectionAssert.AreEqual(new[] { "Text(�)" }, Normalize(parser.Finish()));
            Assert.IsFalse(parser.HasPendingInput);
        }

        [Test]
        public void CharacterSplitAroundCommandStillDecodes()
        {
            // Not something a sane client sends, but the decoder state must survive commands.
            CollectionAssert.AreEqual(new[] { "Negotiation(253,1)", "Text(é)" },
                ParseWhole(Bytes(0xC3, IAC, DO, 1, 0xA9)));
        }

        [Test]
        public void EmptyFeedIsHarmless()
        {
            var parser = new TelnetInputParser();
            CollectionAssert.IsEmpty(parser.Feed(new byte[0], 0, 0));
            CollectionAssert.IsEmpty(parser.Finish());
        }

        [Test]
        public void FeedValidatesItsRange()
        {
            var parser = new TelnetInputParser();
            Assert.Throws<ArgumentNullException>(() => parser.Feed(null, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => parser.Feed(new byte[4], -1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => parser.Feed(new byte[4], 2, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => parser.Feed(new byte[4], 0, -1));
        }
    }
}
