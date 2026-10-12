using System;
using System.Reflection;
using kOS.Safe.Persistence;
using kOS.Safe.Utilities;
using NUnit.Framework;

namespace kOS.Safe.Test.Persistence
{
    [TestFixture]
    public class LegacyNewlineTest
    {
        private bool previousWindows;
        private static readonly PropertyInfo WindowsProperty = typeof(SafeHouse).GetProperty("IsWindows");

        [SetUp]
        public void SetUp()
        {
            previousWindows = SafeHouse.IsWindows;
            SetWindows(true);
        }

        [TearDown]
        public void TearDown() { SetWindows(previousWindows); }

        private static void SetWindows(bool windows)
        {
            WindowsProperty.GetSetMethod(true).Invoke(null, new object[] { windows });
        }

        [TestCase("", "")]
        [TestCase("plain text", "plain text")]
        [TestCase("\n", "\r\n")]
        [TestCase("\r\n", "\r\n")]
        [TestCase("line\nnext\n", "line\r\nnext\r\n")]
        [TestCase("line\r\nnext\r\n", "line\r\nnext\r\n")]
        [TestCase("line\r\nnext\nlast", "line\r\nnext\r\nlast")]
        [TestCase("\n\n", "\r\n\r\n")]
        [TestCase("\r\n\r\n", "\r\n\r\n")]
        [TestCase("line\rnext", "line\rnext")]
        [TestCase("line\r\r\n", "line\r\r\n")]
        [TestCase("ASCII prefix: é\n", "ASCII prefix: é\r\n")]
        public void WindowsTextConversionIsIdempotentWithoutGuessingAwayLoneCarriageReturns(string input, string expected)
        {
            byte[] bytes = FileContent.EncodeString(input);
            for (int i = 0; i < 5; i++)
            {
                bytes = Archive.ConvertToWindowsNewlines(bytes);
                Assert.AreEqual(expected, FileContent.DecodeString(bytes));
            }
        }

        [Test]
        public void ConversionIsDisabledOffTheWindowsBranch()
        {
            SetWindows(false);
            byte[] bytes = { 65, 66, 67, 68, 0xff, 13, 10 };
            Assert.AreSame(bytes, Archive.ConvertToWindowsNewlines(bytes));
        }

        [Test]
        public void LegacyReadStillNormalizesCrLfOnly()
        {
            byte[] bytes = FileContent.EncodeString("line\r\nnext\nlast\rend");
            Assert.AreEqual("line\nnext\nlast\rend", FileContent.DecodeString(Archive.ConvertFromWindowsNewlines(bytes)));
        }

        [Test]
        public void LegacyMalformedUtf8ReplacementPolicyIsNotChanged()
        {
            byte[] bytes = { 0xff };
            CollectionAssert.AreEqual(new byte[] { 0xef, 0xbf, 0xbd }, Archive.ConvertToWindowsNewlines(bytes));
            CollectionAssert.AreEqual(new byte[] { 0xef, 0xbf, 0xbd }, Archive.ConvertFromWindowsNewlines(bytes));
        }

        [Test]
        public void LegacyBinaryClassificationStillBypassesTextConversion()
        {
            byte[][] samples = {
                new byte[] { 107, 3, 88, 69, 0xff, 13, 10 },
                new byte[] { 31, 139, 8, 0, 0xff, 13, 10 },
                new byte[] { 0xef, 0xbb, 0xbf, 65, 13, 10 },
                FileContent.EncodeString("éé\n")
            };
            foreach (byte[] bytes in samples)
            {
                Assert.AreSame(bytes, Archive.ConvertToWindowsNewlines(bytes));
                Assert.AreSame(bytes, Archive.ConvertFromWindowsNewlines(bytes));
            }
        }
    }
}
