using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using kOS.Safe.Screen;
using NUnit.Framework;

namespace kOS.Safe.Test.Terminal
{
    /// <summary>
    /// The screen buffer's line store (review P-09, <see cref="ScreenLines"/>): it behaves as the
    /// <c>List</c> it replaced, and a full scrollback no longer moves every line kept on each
    /// line scrolled. A change of rows alone keeps the lines (<c>ScreenBuffer.ResizeBuffer</c>).
    /// </summary>
    [TestFixture]
    public class ScreenLinesTest
    {
        private static ScreenBufferLine Line(string text)
        {
            var line = new ScreenBufferLine(text.Length);
            line.ArrayCopyFrom(text.ToCharArray(), 0, 0);
            return line;
        }

        private static void AssertSame(List<IScreenBufferLine> expected, ScreenLines actual, string context)
        {
            Assert.AreEqual(expected.Count, actual.Count, context + ": count");
            for (int i = 0; i < expected.Count; ++i)
                Assert.AreSame(expected[i], actual[i], context + ": line " + i);
        }

        /// <summary>Lines the array still refers to, so a dropped line could not be collected.</summary>
        private static int ReferencesHeld(ScreenLines lines)
        {
            var items = (IScreenBufferLine[])typeof(ScreenLines).GetField("items", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(lines);
            return items.Count(line => line != null);
        }

        [Test]
        public void SeededOperationsMatchAList()
        {
            foreach (int seed in new[] { 9101, 9102, 9103, 9104 })
            {
                var random = new Random(seed);
                var expected = new List<IScreenBufferLine>();
                var actual = new ScreenLines();
                int made = 0;
                for (int step = 0; step < 6000; ++step)
                {
                    string context = "seed " + seed + ", step " + step;
                    int kind = random.Next(100);
                    if (kind < 45 || expected.Count == 0)
                    {
                        // Mostly adds, so the store grows past several capacities:
                        int adds = random.Next(1, 6);
                        for (int i = 0; i < adds; ++i)
                        {
                            ScreenBufferLine line = Line("l" + made++);
                            expected.Add(line);
                            actual.Add(line);
                        }
                    }
                    else if (kind < 70)
                    {
                        // From the start, as the scrollback's trim:
                        int removed = random.Next(0, Math.Min(expected.Count, 8) + 1);
                        expected.RemoveRange(0, removed);
                        actual.RemoveRange(0, removed);
                    }
                    else if (kind < 80)
                    {
                        // From the end, as a shorter screen; or the middle:
                        int index = random.Next(expected.Count + 1);
                        int removed = random.Next(0, expected.Count - index + 1);
                        if (random.Next(2) == 0) { index = expected.Count - removed; }
                        expected.RemoveRange(index, removed);
                        actual.RemoveRange(index, removed);
                    }
                    else if (kind < 90)
                    {
                        int index = random.Next(expected.Count);
                        ScreenBufferLine line = Line("s" + made++);
                        expected[index] = line;
                        actual[index] = line;
                    }
                    else if (kind < 99)
                    {
                        int index = random.Next(expected.Count + 1);
                        int length = random.Next(expected.Count - index + 1);
                        CollectionAssert.AreEqual(expected.GetRange(index, length), actual.GetRange(index, length), context + ": range");
                    }
                    else
                    {
                        expected.Clear();
                        actual.Clear();
                    }
                    AssertSame(expected, actual, context);
                    Assert.AreEqual(expected.Count, ReferencesHeld(actual), context + ": references held");
                }
            }
        }

        [Test]
        public void ArgumentsOutOfRangeThrowAsAListDoes()
        {
            var expected = new List<IScreenBufferLine>();
            var actual = new ScreenLines();
            for (int i = 0; i < 5; ++i)
            {
                ScreenBufferLine line = Line("l" + i);
                expected.Add(line);
                actual.Add(line);
            }
            actual.RemoveRange(0, 2); // the first slots are empty now
            expected.RemoveRange(0, 2);

            var calls = new List<Tuple<string, Action<List<IScreenBufferLine>>, Action<ScreenLines>>>
            {
                Tuple.Create<string, Action<List<IScreenBufferLine>>, Action<ScreenLines>>("get -1", l => { var x = l[-1]; }, l => { var x = l[-1]; }),
                Tuple.Create<string, Action<List<IScreenBufferLine>>, Action<ScreenLines>>("get Count", l => { var x = l[3]; }, l => { var x = l[3]; }),
                Tuple.Create<string, Action<List<IScreenBufferLine>>, Action<ScreenLines>>("set Count", l => l[3] = null, l => l[3] = null),
                Tuple.Create<string, Action<List<IScreenBufferLine>>, Action<ScreenLines>>("remove -1", l => l.RemoveRange(-1, 1), l => l.RemoveRange(-1, 1)),
                Tuple.Create<string, Action<List<IScreenBufferLine>>, Action<ScreenLines>>("remove count -1", l => l.RemoveRange(0, -1), l => l.RemoveRange(0, -1)),
                Tuple.Create<string, Action<List<IScreenBufferLine>>, Action<ScreenLines>>("remove past the end", l => l.RemoveRange(2, 2), l => l.RemoveRange(2, 2)),
                Tuple.Create<string, Action<List<IScreenBufferLine>>, Action<ScreenLines>>("range past the end", l => l.GetRange(1, 3), l => l.GetRange(1, 3)),
                Tuple.Create<string, Action<List<IScreenBufferLine>>, Action<ScreenLines>>("range -1", l => l.GetRange(-1, 1), l => l.GetRange(-1, 1)),
            };
            foreach (var call in calls)
            {
                Type listError = Assert.Catch(() => call.Item2(expected), call.Item1).GetType();
                Assert.AreEqual(listError, Assert.Catch(() => call.Item3(actual), call.Item1).GetType(), call.Item1);
            }
            AssertSame(expected, actual, "unchanged by the failed calls");
        }

        [Test]
        public void ScrollingThroughAFullScrollbackMovesEachLineAboutOnce()
        {
            // As a 36-row screen with its 2,000 lines of scrollback: each line scrolled adds one
            // and drops the oldest. List.RemoveRange(0, 1) moved all 2,036 others each time.
            const int kept = ScreenBuffer.MAX_SCROLLBACK + 36;
            const int scrolled = 100000;
            var lines = new ScreenLines();
            for (int i = 0; i < kept; ++i)
                lines.Add(Line("x"));
            long movedWhileFilling = lines.MovedLines;
            for (int i = 0; i < scrolled; ++i)
            {
                lines.Add(Line("y"));
                lines.RemoveRange(0, 1);
            }

            Assert.AreEqual(kept, lines.Count);
            Assert.LessOrEqual(movedWhileFilling, 2 * kept, "growing by doubling");
            Assert.LessOrEqual(lines.MovedLines - movedWhileFilling, scrolled + kept,
                "each copy of the lines kept comes after as many lines dropped (a List: " + (long)scrolled * (kept - 1) + ")");
            Assert.Less(lines.Capacity, 4 * kept, "about twice the lines kept, at most");
            Assert.AreEqual(kept, ReferencesHeld(lines), "dropped lines are not referred to");
        }

        private static ScreenBuffer ScreenWithLines(int rows, int columns, int lines)
        {
            var screen = new ScreenBuffer();
            screen.SetSize(rows, columns);
            screen.ClearScreen();
            for (int i = 0; i < lines; ++i)
                screen.Print("line " + i);
            return screen;
        }

        /// <summary>
        /// The cost of printing into a full scrollback, as a time per line and for one PRINT of
        /// 5,000 lines (to the test output, <c>MEASURE</c> lines; machine-dependent, not asserted).
        /// The content is checked.
        /// </summary>
        [Test]
        public void PrintingIntoAFullScrollbackKeepsTheNewestLines()
        {
            ScreenBuffer screen = ScreenWithLines(36, 50, 0);
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 1900; ++i)
                screen.Print("line " + i);
            double fillingMicroseconds = watch.Elapsed.TotalMilliseconds * 1000 / 1900;
            for (int i = 1900; i < 2500; ++i)
                screen.Print("line " + i);
            watch.Restart();
            for (int i = 2500; i < 22500; ++i)
                screen.Print("line " + i);
            double fullMicroseconds = watch.Elapsed.TotalMilliseconds * 1000 / 20000;
            string many = string.Join("\n", Enumerable.Range(22500, 5000).Select(i => "line " + i).ToArray());
            watch.Restart();
            screen.Print(many);
            double burstMilliseconds = watch.Elapsed.TotalMilliseconds;
            Console.WriteLine("MEASURE printing a line: {0:F2} us while the scrollback fills, {1:F2} us when full; one PRINT of 5,000 lines when full: {2:F2} ms",
                fillingMicroseconds, fullMicroseconds, burstMilliseconds);

            Assert.AreEqual(ScreenBuffer.MAX_SCROLLBACK, screen.ScrollbackRows);
            Assert.AreEqual("line 27499", screen.GetBuffer()[34].ToString().Replace('\0', ' ').TrimEnd(), "the newest line, above the cursor's");
            int oldest = screen.TopRow - screen.ScrollbackRows;
            Assert.AreEqual("line " + (27499 - 34 - ScreenBuffer.MAX_SCROLLBACK),
                screen.GetLineAt(oldest).ToString().Replace('\0', ' ').TrimEnd(), "the oldest line kept");
            Assert.IsNull(screen.GetLineAt(oldest - 1), "dropped");
        }

        [Test]
        public void AChangeOfRowsAloneKeepsTheLinesAndAChangeOfWidthCutsThem()
        {
            ScreenBuffer screen = ScreenWithLines(10, 40, 300);
            int oldest = screen.TopRow - screen.ScrollbackRows;
            IScreenBufferLine[] before = Enumerable.Range(oldest, 300).Select(screen.GetLineAt).ToArray();

            screen.SetSize(14, 40);
            IScreenBufferLine[] taller = Enumerable.Range(oldest, 300).Select(screen.GetLineAt).ToArray();
            for (int i = 0; i < before.Length; ++i)
                Assert.AreSame(before[i], taller[i], "line " + i + " kept");
            long allocated = AllocationCounter.BytesAllocated(() => { screen.SetSize(12, 40); screen.SetSize(14, 40); }, 10);
            Assert.Less(allocated, 10 * 2 * 300 * 40 * sizeof(char), "no line is copied");

            screen.SetSize(14, 4);
            for (int i = 0; i < before.Length; ++i)
            {
                IScreenBufferLine line = screen.GetLineAt(oldest + i);
                Assert.AreNotSame(before[i], line, "line " + i + " remade at the new width");
                Assert.AreEqual(4, line.Length);
                Assert.AreEqual(before[i].ToString().Substring(0, 4), line.ToString(), "line " + i + " cut on the right");
            }
        }
    }
}
