using System;
using System.Collections.Generic;
using kOS.Safe.Encapsulation;
using NUnit.Framework;

namespace kOS.Safe.Test.Collections
{
    using Structure = kOS.Safe.Encapsulation.Structure; // the test project also has a Structure namespace

    /// <summary>
    /// WIDGET-01: popup-menu selection by value. Upstream <c>PopupMenu.Choose()</c> used the
    /// public <c>Index</c> (whose setter schedules the change callback) as its loop counter.
    /// </summary>
    [TestFixture]
    public class OptionSearchTest
    {
        private static string Shown(Structure s)
        {
            return s.ToString();
        }

        private static ListValue Options(params Structure[] items)
        {
            var list = new ListValue();
            foreach (Structure s in items)
                list.Add(s);
            return list;
        }

        /// <summary>The upstream loop, driving a setter that records every change it would notify.</summary>
        private static List<int> UpstreamChooseNotifications(IList<Structure> list, Structure v, int startIndex)
        {
            var notified = new List<int>();
            int index = startIndex;
            Action<int> set = value => { if (value != index) notified.Add(value); index = value; };
            for (set(0); index < list.Count; set(index + 1))
                if (list[index] == v)
                    return notified;
            string vs = Shown(v);
            for (set(0); index < list.Count; set(index + 1))
                if (Shown(list[index]) == vs)
                    return notified;
            set(-1);
            return notified;
        }

        [Test]
        public void UpstreamNotifiedEveryOptionItPassed()
        {
            ListValue list = Options(new StringValue("a"), new StringValue("b"), new StringValue("c"));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, UpstreamChooseNotifications(list, list[2], 1),
                "choosing the last option from the second");
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 0, 1, 2, 3, -1 },
                UpstreamChooseNotifications(list, new StringValue("zz"), 0), "a missing value");
        }

        [Test]
        public void FirstMiddleAndLastAreFoundByIdentity()
        {
            ListValue list = Options(new StringValue("a"), new StringValue("b"), new StringValue("c"));
            for (int i = 0; i < list.Count; ++i)
                Assert.AreEqual(i, OptionSearch.IndexOf(list, list[i], Shown));
        }

        [Test]
        public void TheSameObjectWinsOverAnEarlierEqualLabel()
        {
            var second = new StringValue("same");
            ListValue list = Options(new StringValue("same"), second);
            Assert.AreEqual(1, OptionSearch.IndexOf(list, second, Shown));
        }

        [Test]
        public void AnotherObjectWithTheSameTextFindsTheFirstSuchOption()
        {
            ListValue list = Options(new StringValue("a"), new StringValue("dup"), new StringValue("dup"));
            Assert.AreEqual(1, OptionSearch.IndexOf(list, new StringValue("dup"), Shown));
            Assert.AreEqual(1, OptionSearch.IndexOf(Options(new ScalarIntValue(1), new ScalarIntValue(42)), new ScalarIntValue(42), Shown));
        }

        [Test]
        public void TextComparisonIsCaseSensitive()
        {
            Assert.AreEqual(-1, OptionSearch.IndexOf(Options(new StringValue("Apple")), new StringValue("apple"), Shown));
        }

        [Test]
        public void TheDisplayFunctionDecidesTheText()
        {
            ListValue list = Options(new StringValue("alpha"), new StringValue("beta"));
            Func<Structure, string> firstLetter = s => s.ToString().Substring(0, 1);
            Assert.AreEqual(1, OptionSearch.IndexOf(list, new StringValue("bravo"), firstLetter));
        }

        [Test]
        public void AMissingValueOrAnEmptyMenuGivesMinusOne()
        {
            Assert.AreEqual(-1, OptionSearch.IndexOf(Options(new StringValue("a")), new StringValue("b"), Shown));
            Assert.AreEqual(-1, OptionSearch.IndexOf(new ListValue(), new StringValue("a"), Shown));
            Assert.AreEqual(-1, OptionSearch.IndexOf(Options(new StringValue("a")), null, Shown));
        }

        [Test]
        public void NullArgumentsAreRejected()
        {
            Assert.Throws<ArgumentNullException>(() => OptionSearch.IndexOf(null, new StringValue("a"), Shown));
            Assert.Throws<ArgumentNullException>(() => OptionSearch.IndexOf(new ListValue(), new StringValue("a"), null));
        }
    }
}
