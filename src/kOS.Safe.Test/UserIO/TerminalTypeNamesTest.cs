using System;
using kOS.Safe.UserIO;
using NUnit.Framework;

namespace kOS.Safe.Test.UserIO
{
    /// <summary>
    /// NET-03: TerminalUnicodeMapper.GuessTypeId() used Substring(0,5)/Substring(0,4), so a
    /// terminal type shorter than 5 characters threw on the telnet reader thread and
    /// ended the session. The game mapper now delegates to TerminalTypeNames.
    /// </summary>
    [TestFixture]
    public class TerminalTypeNamesTest
    {
        /// <summary>The original prefix checks, kept as an oracle (0 = unknown, 1 = xterm, 2 = vt100).</summary>
        private static int UpstreamGuess(string typeString)
        {
            if (typeString.Substring(0, 5).Equals("xterm", StringComparison.CurrentCultureIgnoreCase))
                return 1;
            if (typeString.Substring(0, 4).Equals("vt10", StringComparison.CurrentCultureIgnoreCase))
                return 2;
            return 0;
        }

        private static int Guess(string typeString)
        {
            if (TerminalTypeNames.IsXterm(typeString))
                return 1;
            if (TerminalTypeNames.IsVT100(typeString))
                return 2;
            return 0;
        }

        [TestCase("ANSI")] // Microsoft Telnet's first answer
        [TestCase("VT52")]
        [TestCase("VTNT")]
        [TestCase("dumb")]
        [TestCase("vt1")]
        [TestCase("x")]
        [TestCase("xter")]
        [TestCase("")]
        [TestCase(null)]
        public void ShortOrMissingTypesAreUnknownInsteadOfThrowing(string typeString)
        {
            Assert.AreEqual(0, Guess(typeString));
        }

        [Test]
        public void BareVt10PrefixIsVT100()
        {
            // Upstream threw here; the prefix it checked for is now accepted on its own.
            Assert.AreEqual(2, Guess("VT10"));
        }

        [Test]
        public void UpstreamOracleThrowsForShortTypes()
        {
            // Pins the defect, so the oracle comparison below stays meaningful.
            Assert.Throws<ArgumentOutOfRangeException>(() => UpstreamGuess("ANSI"));
        }

        [TestCase("XTERM", 1)]
        [TestCase("xterm", 1)]
        [TestCase("xterm-256color", 1)]
        [TestCase("VT100", 2)]
        [TestCase("vt102", 2)]
        [TestCase("VT100-AM", 2)]
        [TestCase("VT220", 0)]
        [TestCase("vt10x", 2)]
        [TestCase("ANSI-BBS", 0)]
        [TestCase("INITIAL_UNSET", 0)] // TelnetSingletonServer's placeholder
        [TestCase("screen-256color", 0)]
        public void KnownTypesMatchUpstream(string typeString, int expected)
        {
            Assert.AreEqual(expected, UpstreamGuess(typeString));
            Assert.AreEqual(expected, Guess(typeString));
        }

        [Test]
        public void AgreesWithUpstreamWheneverUpstreamWorked()
        {
            var random = new Random(3103);
            const string alphabet = "xtermXTERMvV10-2 ansi";
            int compared = 0;
            for (int n = 0; n < 2000; ++n)
            {
                var chars = new char[random.Next(0, 10)];
                for (int i = 0; i < chars.Length; ++i)
                    chars[i] = alphabet[random.Next(alphabet.Length)];
                string typeString = random.Next(4) == 0 ? "xterm".Substring(0, random.Next(6)) + new string(chars) : new string(chars);

                int upstream;
                try { upstream = UpstreamGuess(typeString); }
                catch (ArgumentOutOfRangeException)
                {
                    // Only a bare 4-character "vt10" is recognized among the strings upstream rejected.
                    int expected = string.Equals(typeString, "vt10", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
                    Assert.AreEqual(expected, Guess(typeString), "'" + typeString + "'");
                    continue;
                }
                Assert.AreEqual(upstream, Guess(typeString), "'" + typeString + "'");
                ++compared;
            }
            Assert.Greater(compared, 500);
        }
    }
}
