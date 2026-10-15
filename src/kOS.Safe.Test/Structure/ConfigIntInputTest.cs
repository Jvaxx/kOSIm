using System;
using kOS.Safe.Encapsulation.Suffixes;
using NUnit.Framework;

namespace kOS.Safe.Test.Structure
{
    /// <summary>
    /// PANEL-02: the settings panel limited every integer field to 4 characters, so a valid
    /// telnet port such as 54100 could not be typed. The limit now follows the key's range.
    /// </summary>
    [TestFixture]
    public class ConfigIntInputTest
    {
        private const int UpstreamFieldLength = 4;

        // The same ranges as the integer keys in kOS's Config:
        private static ConfigKey Port() { return new ConfigKey("TelnetPort", "TPORT", "port", 5410, 1024, 65535, typeof(int)); }
        private static ConfigKey Width() { return new ConfigKey("TerminalDefaultWidth", "DEFAULTWIDTH", "width", 50, 15, 255, typeof(int)); }
        private static ConfigKey Height() { return new ConfigKey("TerminalDefaultHeight", "DEFAULTHEIGHT", "height", 36, 3, 160, typeof(int)); }
        private static ConfigKey FontSize() { return new ConfigKey("TerminalFontDefaultSize", "DEFAULTFONTSIZE", "font", 12, 4, 48, typeof(int)); }

        /// <summary>What the panel does on Return: parse, then let the key clamp it.</summary>
        private static int Commit(ConfigKey key, string typed)
        {
            int value;
            Assert.IsTrue(ConfigIntInput.TryParse(typed, out value), "'" + typed + "' was rejected");
            key.Value = value;
            return (int)key.Value;
        }

        /// <summary>Unity's TextField drops characters past its limit.</summary>
        private static string TypeInto(ConfigKey key, string typed)
        {
            int max = ConfigIntInput.MaxLength(key);
            return typed.Length > max ? typed.Substring(0, max) : typed;
        }

        [TestCase("1024", 1024)]
        [TestCase("5410", 5410)]
        [TestCase("54100", 54100)]
        [TestCase("65535", 65535)]
        public void EveryValidPortCanBeTyped(string typed, int expected)
        {
            ConfigKey port = Port();
            Assert.AreEqual(expected, Commit(port, TypeInto(port, typed)));
        }

        [Test]
        public void TheOldLimitCouldNotHoldAFiveDigitPort()
        {
            Assert.Greater("54100".Length, UpstreamFieldLength);
            Assert.AreEqual(5, ConfigIntInput.MaxLength(Port()));
        }

        [Test]
        public void TheLimitFollowsEachKeysRange()
        {
            Assert.AreEqual(3, ConfigIntInput.MaxLength(Width()));
            Assert.AreEqual(3, ConfigIntInput.MaxLength(Height()));
            Assert.AreEqual(2, ConfigIntInput.MaxLength(FontSize()));
            Assert.AreEqual(3, ConfigIntInput.MaxLength(new ConfigKey("k", "K", "n", 0, -50, 10, typeof(int))), "the minus sign counts");
            Assert.AreEqual(1, ConfigIntInput.MaxLength(new ConfigKey("k", "K", "n", "x", "n/a", "n/a", typeof(string))));
        }

        [TestCase("65536", 65535)]
        [TestCase("99999", 65535)]
        [TestCase("80", 1024)]
        [TestCase("-5", 1024)]
        public void OutOfRangeValuesAreClampedOnCommit(string typed, int expected)
        {
            Assert.AreEqual(expected, Commit(Port(), typed));
        }

        [Test]
        public void EmptyTextReadsAsZeroAndCommitsAsTheMinimum()
        {
            // Unchanged behavior: clearing the field while typing must not be an error.
            int value;
            Assert.IsTrue(ConfigIntInput.TryParse("", out value));
            Assert.AreEqual(0, value);
            Assert.IsTrue(ConfigIntInput.TryParse("   ", out value));
            Assert.AreEqual(0, value);
            Assert.IsTrue(ConfigIntInput.TryParse(null, out value));
            Assert.AreEqual(1024, Commit(Port(), ""));
        }

        [TestCase(" 5410 ", 5410)]
        [TestCase("+5410", 5410)]
        [TestCase("054100", 54100)]
        public void SpacesAndSignsParseAsBefore(string typed, int expected)
        {
            int value;
            Assert.IsTrue(ConfigIntInput.TryParse(typed, out value));
            Assert.AreEqual(expected, value);
        }

        [TestCase("54a")]
        [TestCase("5 410")]
        [TestCase("5410.0")]
        [TestCase("1,024")]
        [TestCase("-")]
        [TestCase("99999999999")]
        public void InvalidTextIsRejected(string typed)
        {
            int value;
            Assert.IsFalse(ConfigIntInput.TryParse(typed, out value));
        }

        [Test]
        public void ANullKeyIsRejected()
        {
            Assert.Throws<ArgumentNullException>(() => ConfigIntInput.MaxLength(null));
        }
    }
}
