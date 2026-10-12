using System.Collections.Generic;
using kOS.Safe.Screen;
using kOS.Safe.UserIO;
using NUnit.Framework;

namespace kOS.Safe.Test.UserIO
{
    [TestFixture]
    public class TelnetProtocolTest
    {
        private const byte IAC = TelnetInputParser.IAC;
        private const byte DO = TelnetInputParser.DO;
        private const byte DONT = TelnetInputParser.DONT;
        private const byte WILL = TelnetInputParser.WILL;
        private const byte WONT = TelnetInputParser.WONT;
        private const byte ECHO = TelnetProtocol.OPTION_ECHO;
        private const byte SGA = TelnetProtocol.OPTION_SUPPRESS_GOAHEAD;
        private const byte NAWS = TelnetInputParser.OPTION_NAWS;
        private const byte TERMTYPE = TelnetInputParser.OPTION_TERMTYPE;

        private static readonly byte[] AskTermType = { IAC, TelnetInputParser.SB, TERMTYPE, 1, IAC, TelnetInputParser.SE };

        private static IEnumerable<TestCaseData> Replies()
        {
            // Matches the upstream server's replies for every option it handled.
            yield return new TestCaseData(DO, ECHO, false, new byte[] { IAC, WILL, ECHO });
            yield return new TestCaseData(DO, ECHO, true, new byte[] { IAC, WONT, ECHO });
            yield return new TestCaseData(DONT, ECHO, false, new byte[] { IAC, WILL, ECHO });
            yield return new TestCaseData(DONT, ECHO, true, new byte[] { IAC, WONT, ECHO });
            yield return new TestCaseData(DO, SGA, false, new byte[] { IAC, WILL, SGA });
            yield return new TestCaseData(DONT, SGA, true, new byte[] { IAC, WONT, SGA });
            yield return new TestCaseData(DO, NAWS, false, new byte[] { IAC, WILL, NAWS });
            yield return new TestCaseData(DONT, NAWS, false, null);
            yield return new TestCaseData(DO, TERMTYPE, false, AskTermType);
            yield return new TestCaseData(WILL, TERMTYPE, false, AskTermType);
            yield return new TestCaseData(WILL, NAWS, false, null);
            yield return new TestCaseData(DO, (byte)99, false, null);
            yield return new TestCaseData(WILL, (byte)99, false, null);
            yield return new TestCaseData(WONT, (byte)99, false, null);
        }

        [TestCaseSource("Replies")]
        public void NegotiationRepliesMatchServerPolicy(byte verb, byte option, bool lineAtATime, byte[] expected)
        {
            string warning;
            byte[] reply = TelnetProtocol.NegotiationReply(verb, option, lineAtATime, out warning);
            Assert.AreEqual(expected, reply);
            Assert.IsNull(warning);
        }

        [TestCase(TERMTYPE, "RFC1091")]
        [TestCase(NAWS, "RFC1073")]
        public void RefusingRequiredOptionsWarnsWithoutReplyOrDisconnect(byte option, string rfc)
        {
            string warning;
            Assert.IsNull(TelnetProtocol.NegotiationReply(WONT, option, false, out warning));
            StringAssert.Contains(rfc, warning);
            StringAssert.DoesNotContain("DIE", warning);
        }

        [TestCase(0, 80, 80)]          // zero means "unchanged" (RFC 1073)
        [TestCase(1, 80, 1)]
        [TestCase(132, 80, 132)]
        [TestCase(ScreenBuffer.MAX_COLUMNS, 80, ScreenBuffer.MAX_COLUMNS)]
        [TestCase(ScreenBuffer.MAX_COLUMNS + 1, 80, ScreenBuffer.MAX_COLUMNS)]
        [TestCase(65535, 80, ScreenBuffer.MAX_COLUMNS)]
        [TestCase(-5, 80, 1)]
        public void DimensionsAreLimitedToTheScreenBuffer(int requested, int current, int expected)
        {
            Assert.AreEqual(expected, TelnetProtocol.NormalizeDimension(requested, current, ScreenBuffer.MAX_COLUMNS));
        }
    }
}
