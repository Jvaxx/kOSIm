using System;
using kOS.Safe.UserIO;
using NUnit.Framework;

namespace kOS.Safe.Test.UserIO
{
    [TestFixture]
    public class TelnetMenuTextTest
    {
        private const string Sample = "Choose a CPU to attach to by typing a selection number and pressing return/enter. Or enter [Q] to quit terminal server.";

        /// <summary>The welcome menu's original algorithm, kept as an oracle.</summary>
        private static string UpstreamWordBreak(string msg, int width)
        {
            char[] msgAsArray = msg.ToCharArray();
            int lineStartPos = 0;
            int lastSpacePos = -1;
            for (int i = 0; i < msgAsArray.Length; ++i)
            {
                if (msgAsArray[i] == ' ')
                    lastSpacePos = i;
                if ((i - lineStartPos) >= width)
                {
                    msgAsArray[lastSpacePos] = (char)UnicodeCommand.STARTNEXTLINE;
                    lineStartPos = lastSpacePos + 1;
                }
            }
            return new string(msgAsArray);
        }

        [Test]
        public void WordBreakMatchesUpstreamWheneverUpstreamWorked()
        {
            int compared = 0;
            for (int width = 1; width <= 300; ++width)
            {
                string upstream;
                try { upstream = UpstreamWordBreak(Sample, width); }
                catch (IndexOutOfRangeException) { continue; }
                Assert.AreEqual(upstream, TelnetMenuText.WordBreak(Sample, width), "width " + width);
                ++compared;
            }
            Assert.Greater(compared, 250);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(3)]
        public void WordBreakSurvivesClientsNarrowerThanAWord(int width)
        {
            Assert.Throws<IndexOutOfRangeException>(() => UpstreamWordBreak(Sample, width), "upstream crashed here");
            string broken = TelnetMenuText.WordBreak(Sample, width);
            Assert.AreEqual(Sample.Length, broken.Length);
            Assert.AreEqual(Sample.Replace(" ", ""), broken.Replace(" ", "").Replace(((char)UnicodeCommand.STARTNEXTLINE).ToString(), ""));
        }

        [TestCase("Menu", '=', 20, "========Menu========")]
        [TestCase("Menu", ' ', 20, "        Menu")]
        [TestCase("Menu", '-', 4, "Menu")]
        [TestCase("Menu", '-', 1, "Menu")]   // upstream threw: negative pad length
        [TestCase("Menu", '-', 0, "Menu")]
        public void CenterPaddedNeverNeedsANegativePad(string msg, char pad, int width, string expected)
        {
            Assert.AreEqual(expected, TelnetMenuText.CenterPadded(msg, pad, width));
        }
    }
}
