using System;
using System.Text;
using kOS.Safe.UserIO;
using NUnit.Framework;

namespace kOS.Safe.Test.UserIO
{
    /// <summary>
    /// <see cref="TerminalInputDecoder"/>: the upstream mappers' key mappings, but kept
    /// across reads, so keys split between socket reads still decode as one key.
    /// </summary>
    [TestFixture]
    public class TerminalInputDecoderTest
    {
        private const char ESC = (char)0x1b;
        private const char CSI8 = (char)0x9b;

        private static readonly TerminalInputDialect[] Dialects =
            { TerminalInputDialect.Plain, TerminalInputDialect.VT100, TerminalInputDialect.Xterm };

        private static string DecodeWhole(TerminalInputDialect dialect, string text)
        {
            var decoder = new TerminalInputDecoder(dialect);
            return decoder.Decode(text) + decoder.Flush();
        }

        private static string Key(UnicodeCommand command) { return ((char)command).ToString(); }

        [TestCase("\x1b[A", UnicodeCommand.UPCURSORONE)]
        [TestCase("\x1b[B", UnicodeCommand.DOWNCURSORONE)]
        [TestCase("\x1b[C", UnicodeCommand.RIGHTCURSORONE)]
        [TestCase("\x1b[D", UnicodeCommand.LEFTCURSORONE)]
        [TestCase("\x1b[H", UnicodeCommand.HOMECURSOR)]
        [TestCase("\x1b[F", UnicodeCommand.ENDCURSOR)]
        [TestCase("\x1b[1~", UnicodeCommand.HOMECURSOR)]
        [TestCase("\x1b[3~", UnicodeCommand.DELETERIGHT)]
        [TestCase("\x1b[4~", UnicodeCommand.ENDCURSOR)]
        [TestCase("\x1b[5~", UnicodeCommand.PAGEUPCURSOR)]
        [TestCase("\x1b[6~", UnicodeCommand.PAGEDOWNCURSOR)]
        [TestCase("\x9b" + "A", UnicodeCommand.UPCURSORONE)]
        public void XtermKeySplitAtEveryPointIsOneKey(string sequence, UnicodeCommand expected)
        {
            for (int split = 0; split <= sequence.Length; ++split)
            {
                var decoder = new TerminalInputDecoder(TerminalInputDialect.Xterm);
                string output = decoder.Decode(sequence.Substring(0, split)) + decoder.Decode(sequence.Substring(split));
                Assert.AreEqual(Key(expected), output, "split at " + split);
                Assert.IsFalse(decoder.HasPendingSequence);
            }
        }

        [TestCase("\r\n")]
        [TestCase("\n\r")]
        [TestCase("\r\0")]
        public void LineEndingSplitAcrossReadsIsOneEnter(string ending)
        {
            var decoder = new TerminalInputDecoder(TerminalInputDialect.VT100);
            string output = decoder.Decode(ending.Substring(0, 1)) + decoder.Decode(ending.Substring(1));
            Assert.AreEqual(Key(UnicodeCommand.STARTNEXTLINE), output);
        }

        [Test]
        public void EveryEnterInConsecutiveLineEndingsCounts()
        {
            // Upstream lost the second Enter (a blank line in a paste).
            string nl = Key(UnicodeCommand.STARTNEXTLINE);
            Assert.AreEqual(nl, UpstreamTerminalInput.Convert(TerminalInputDialect.VT100, "\r\n\r\n"));
            Assert.AreEqual(nl + nl, DecodeWhole(TerminalInputDialect.VT100, "\r\n\r\n"));
            Assert.AreEqual(nl + nl, DecodeWhole(TerminalInputDialect.VT100, "\r\0\r\0"));
        }

        [Test]
        public void LoneEscapeWaitsForFlush()
        {
            var decoder = new TerminalInputDecoder(TerminalInputDialect.VT100);
            Assert.AreEqual("", decoder.Decode("\x1b"));
            Assert.IsTrue(decoder.HasPendingSequence);
            Assert.AreEqual("\x1b", decoder.Flush());
            Assert.IsFalse(decoder.HasPendingState);
        }

        [Test]
        public void PlainDialectPassesEscapeAtOnce()
        {
            var decoder = new TerminalInputDecoder(TerminalInputDialect.Plain);
            Assert.AreEqual("\x1b[A", decoder.Decode("\x1b[A"));
            Assert.IsFalse(decoder.HasPendingSequence);
        }

        [Test]
        public void ResetDiscardsAnUnfinishedSequence()
        {
            var decoder = new TerminalInputDecoder(TerminalInputDialect.Xterm);
            decoder.Decode("\x1b[");
            decoder.Reset();
            Assert.AreEqual("x", decoder.Decode("x"));
        }

        [Test]
        public void OverlongSequenceIsPassedOnAsTyped()
        {
            string body = new string('1', TerminalInputDecoder.MaxSequenceLength * 2);
            string output = DecodeWhole(TerminalInputDialect.VT100, "\x1b[" + body + "A");
            Assert.AreEqual("[" + body + "A", output); // ESC dropped, as for any unrecognized ESC [ sequence
        }

        /// <summary>
        /// The decoder's documented differences from upstream: consecutive line endings
        /// (see above), keys followed by '~' (upstream read "ESC [ A ~" as typed text), and
        /// ESC directly before U+009B (upstream's xterm pass removed the U+009B first, so the
        /// ESC then met whatever followed it). None of these is a real key.
        /// </summary>
        private static bool HasDocumentedDifference(string text)
        {
            if (text.Contains(ESC.ToString() + CSI8))
                return true;
            string raw = text.Replace(CSI8.ToString(), "");
            if (raw.Contains("\r\n\r") || raw.Contains("\n\r\n"))
                return true;
            foreach (char key in "ABCDHF")
            {
                if (text.Contains("[" + key + "~") || text.Contains(CSI8 + key.ToString() + "~"))
                    return true;
            }
            return false;
        }

        [Test]
        public void SingleReadMatchesUpstreamMapper()
        {
            const string alphabet = "\x1b\x1b[[[ABCDHF~~135692;Oxé\r\n\r\n\0\x08\x7f\x07\x03\x0c\x9b";
            int compared = 0;
            foreach (TerminalInputDialect dialect in Dialects)
            {
                var random = new Random(3105);
                for (int n = 0; n < 4000; ++n)
                {
                    string text = RandomText(random, alphabet, 12);
                    if (HasDocumentedDifference(text))
                        continue;
                    Assert.AreEqual(Escape(UpstreamTerminalInput.Convert(dialect, text)), Escape(DecodeWhole(dialect, text)),
                        dialect + " '" + Escape(text) + "'");
                    ++compared;
                }
            }
            Assert.Greater(compared, 6000);
        }

        [Test]
        public void ChunkingDoesNotChangeTheResult()
        {
            const string alphabet = "\x1b\x1b[[[ABCDHF~~135692;Oxé\r\n\r\n\0\x08\x7f\x07\x03\x0c\x9b";
            foreach (TerminalInputDialect dialect in Dialects)
            {
                var random = new Random(3106);
                for (int n = 0; n < 2000; ++n)
                {
                    string text = RandomText(random, alphabet, 24);
                    string whole = DecodeWhole(dialect, text);
                    var decoder = new TerminalInputDecoder(dialect);
                    var chunked = new StringBuilder();
                    int offset = 0;
                    while (offset < text.Length)
                    {
                        int length = Math.Min(text.Length - offset, 1 + random.Next(4));
                        chunked.Append(decoder.Decode(text.Substring(offset, length)));
                        offset += length;
                    }
                    chunked.Append(decoder.Flush());
                    Assert.AreEqual(Escape(whole), Escape(chunked.ToString()), dialect + " '" + Escape(text) + "'");
                }
            }
        }

        private static string RandomText(Random random, string alphabet, int maxLength)
        {
            var chars = new char[random.Next(maxLength + 1)];
            for (int i = 0; i < chars.Length; ++i)
                chars[i] = alphabet[random.Next(alphabet.Length)];
            return new string(chars);
        }

        private static string Escape(string text)
        {
            var sb = new StringBuilder();
            foreach (char ch in text)
                sb.Append(ch < 0x20 || ch >= 0x7f ? string.Format("\\u{0:X4}", (int)ch) : ch.ToString());
            return sb.ToString();
        }
    }

    /// <summary>Review D-03: one table of VT100/VT220 keys for the decoder and the old mapper.</summary>
    [TestFixture]
    public class VT100KeysTest
    {
        [Test]
        public void TheTableHoldsTheUpstreamKeysAndNothingElse()
        {
            int finals = 0, tildes = 0;
            for (int c = 0; c <= char.MaxValue; ++c)
            {
                if (VT100Keys.ForFinal((char)c) != '\0') ++finals;
                if (VT100Keys.ForTilde((char)c) != '\0') ++tildes;
            }
            Assert.AreEqual(6, finals);
            Assert.AreEqual(5, tildes);
            Assert.AreEqual((char)UnicodeCommand.UPCURSORONE, VT100Keys.ForFinal('A'));
            Assert.AreEqual((char)UnicodeCommand.DOWNCURSORONE, VT100Keys.ForFinal('B'));
            Assert.AreEqual((char)UnicodeCommand.RIGHTCURSORONE, VT100Keys.ForFinal('C'));
            Assert.AreEqual((char)UnicodeCommand.LEFTCURSORONE, VT100Keys.ForFinal('D'));
            Assert.AreEqual((char)UnicodeCommand.HOMECURSOR, VT100Keys.ForFinal('H'));
            Assert.AreEqual((char)UnicodeCommand.ENDCURSOR, VT100Keys.ForFinal('F'));
            Assert.AreEqual((char)UnicodeCommand.HOMECURSOR, VT100Keys.ForTilde('1'));
            Assert.AreEqual((char)UnicodeCommand.DELETERIGHT, VT100Keys.ForTilde('3'));
            Assert.AreEqual((char)UnicodeCommand.ENDCURSOR, VT100Keys.ForTilde('4'));
            Assert.AreEqual((char)UnicodeCommand.PAGEUPCURSOR, VT100Keys.ForTilde('5'));
            Assert.AreEqual((char)UnicodeCommand.PAGEDOWNCURSOR, VT100Keys.ForTilde('6'));
        }

        [Test]
        public void TheDecoderUsesTheTable()
        {
            foreach (TerminalInputDialect dialect in new[] { TerminalInputDialect.VT100, TerminalInputDialect.Xterm })
            {
                for (char c = (char)0x40; c <= (char)0x7E; ++c)
                {
                    var decoder = new TerminalInputDecoder(dialect);
                    string keys = decoder.Decode("\u001b[" + c) + decoder.Flush();
                    char key = VT100Keys.ForFinal(c);
                    if (key != '\0')
                        Assert.AreEqual(key.ToString(), keys, dialect + " ESC [ " + c);
                    else
                        Assert.AreEqual("[" + c, keys, dialect + " ESC [ " + c + " is typed as text");
                }
                for (char n = '0'; n <= '9'; ++n)
                {
                    var decoder = new TerminalInputDecoder(dialect);
                    string keys = decoder.Decode("\u001b[" + n + "~") + decoder.Flush();
                    char key = VT100Keys.ForTilde(n);
                    Assert.AreEqual(key != '\0' ? key.ToString() : "[" + n + "~", keys, dialect + " ESC [ " + n + " ~");
                }
            }
        }
    }
}
