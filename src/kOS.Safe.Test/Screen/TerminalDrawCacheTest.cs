using System;
using System.Collections.Generic;
using kOS.Safe.Module;
using kOS.Safe.Screen;
using NUnit.Framework;

namespace kOS.Safe.Test.Terminal
{
    /// <summary>
    /// The GUI terminal's rows as drawn (review P-08, <see cref="TerminalRowTexts"/>): each row
    /// shows what <c>line.ToString().Replace('\0', ' ')</c> showed, and an unchanged row keeps
    /// its string, so drawing an unchanged screen allocates nothing.
    /// </summary>
    [TestFixture]
    public class TerminalRowTextsTest
    {
        private static ScreenBufferLine Line(string text)
        {
            var line = new ScreenBufferLine(text.Length);
            line.ArrayCopyFrom(text.ToCharArray(), 0, 0);
            return line;
        }

        private static string Shown(IScreenBufferLine line)
        {
            return line.ToString().Replace('\0', ' '); // what the window drew before P-08
        }

        /// <summary>A line of another kind, whose change tick never moves: only its characters tell a change.</summary>
        private sealed class FixedTickLine : IScreenBufferLine
        {
            private readonly char[] characters;

            public FixedTickLine(string text) { characters = text.ToCharArray(); }

            public ulong LastChangeTick { get { return 7; } }
            public char[] ToArray() { return (char[])characters.Clone(); }
            public char this[int i] { get { return characters[i]; } set { characters[i] = value; } }
            public int Length { get { return characters.Length; } }
            public void ArrayCopyFrom(IScreenBufferLine source, int sourceStart, int destinationStart, int length = -1) { throw new NotSupportedException(); }
            public void ArrayCopyFrom(char[] source, int sourceStart, int destinationStart, int length = -1) { throw new NotSupportedException(); }
            public void TouchTime() { }
        }

        [Test]
        public void NullCellsShowAsSpaces()
        {
            var rows = new TerminalRowTexts();
            ScreenBufferLine line = Line("a\0b\0");
            Assert.AreEqual("a b ", rows.Get(0, line));
            Assert.AreEqual("    ", rows.Get(1, new ScreenBufferLine(4)), "a new line is all null cells");
            Assert.AreEqual("", rows.Get(2, new ScreenBufferLine(0)));
        }

        [Test]
        public void AnUnchangedRowKeepsItsString()
        {
            var rows = new TerminalRowTexts();
            ScreenBufferLine line = Line("hello\0");
            string first = rows.Get(0, line);
            Assert.AreSame(first, rows.Get(0, line), "same line, same tick");
            Assert.AreEqual(1, rows.TextsMade);

            line.TouchTime(); // a new tick, the same characters
            Assert.AreSame(first, rows.Get(0, line));
            Assert.AreSame(first, rows.Get(0, Line("hello ")), "another line showing the same (a null cell is a space)");
            Assert.AreEqual(1, rows.TextsMade);
        }

        [Test]
        public void AChangedRowGetsANewString()
        {
            var rows = new TerminalRowTexts();
            ScreenBufferLine line = Line("hello");
            rows.Get(0, line);

            line[4] = '!';
            Assert.AreEqual("hell!", rows.Get(0, line), "the indexer");
            line.ArrayCopyFrom("HE".ToCharArray(), 0, 0);
            Assert.AreEqual("HEll!", rows.Get(0, line), "a copy into the line");
            line[0] = '\0';
            Assert.AreEqual(" Ell!", rows.Get(0, line));
            Assert.AreEqual("hell", rows.Get(0, Line("hell")), "another length");
            Assert.AreEqual("hello", rows.Get(0, Line("hello")), "another line");
        }

        [Test]
        public void AnotherKindOfLineIsComparedByItsCharacters()
        {
            var rows = new TerminalRowTexts();
            var line = new FixedTickLine("abc");
            string first = rows.Get(0, line);
            Assert.AreEqual("abc", first);
            Assert.AreSame(first, rows.Get(0, line));

            line[1] = '\0'; // the tick stays 7
            Assert.AreEqual("a c", rows.Get(0, line));
        }

        [Test]
        public void RowsAreKeptApart()
        {
            var rows = new TerminalRowTexts();
            ScreenBufferLine top = Line("top"), bottom = Line("bottom");
            Assert.AreEqual("top", rows.Get(0, top));
            Assert.AreEqual("bottom", rows.Get(5, bottom), "a row past the ones kept so far");
            Assert.AreEqual("top", rows.Get(0, top));
            Assert.AreEqual("bottom", rows.Get(0, bottom), "a line moved to another row (a scroll)");
            Assert.AreEqual("top", rows.Get(5, top));
        }

        [Test]
        public void KeepAndClearForgetRows()
        {
            var rows = new TerminalRowTexts();
            for (int row = 0; row < 4; ++row)
                rows.Get(row, Line("row" + row));
            rows.Keep(2);
            long made = rows.TextsMade;
            rows.Get(1, Line("row1"));
            Assert.AreEqual(made, rows.TextsMade, "a row kept");
            rows.Get(3, Line("row3"));
            Assert.AreEqual(made + 1, rows.TextsMade, "a row forgotten");

            rows.Clear();
            rows.Get(1, Line("row1"));
            Assert.AreEqual(made + 2, rows.TextsMade, "every row forgotten");
            rows.Keep(10); // more than were ever kept: nothing to do
            rows.Keep(-1);
            Assert.Throws<ArgumentOutOfRangeException>(() => rows.Get(-1, Line("x")));
            Assert.Throws<ArgumentNullException>(() => rows.Get(0, null));
        }

        /// <summary>
        /// The window's loop over a real screen, seeded: prints that scroll, PRINT AT, clears,
        /// resizes, a scrolled-back view and a fixed overlay, each followed by several draws
        /// (IMGUI events) of the snapshot it would draw. Every row always shows what the old
        /// code showed, and the draws after the first of a change make no string.
        /// </summary>
        [Test]
        public void ARealScreenShowsAsBefore()
        {
            var random = new Random(20261021);
            var screen = new ScreenBuffer();
            screen.SetSize(12, 30);
            var overlay = new SubBuffer { Fixed = true, PositionRow = 2, Enabled = true };
            overlay.SetSize(2, 10);
            overlay.Buffer[0].ArrayCopyFrom("OVERLAY".ToCharArray(), 0, 0);
            screen.AddSubBuffer(overlay);
            var rows = new TerminalRowTexts();
            int draws = 0;

            for (int step = 0; step < 2000; ++step)
            {
                switch (random.Next(9))
                {
                    case 0:
                    case 1:
                    case 2:
                        screen.Print(new string((char)('a' + random.Next(26)), random.Next(1, 50)));
                        break;
                    case 3:
                        screen.PrintAt(random.Next(2) == 0 ? "\0x\0" : "AT", random.Next(screen.RowCount), random.Next(screen.ColumnCount));
                        break;
                    case 4:
                        if (random.Next(10) == 0) screen.ClearScreen();
                        break;
                    case 5:
                        if (random.Next(10) == 0) screen.SetSize(random.Next(3, 20), random.Next(15, 60));
                        break;
                    case 6:
                        screen.ScrollVertical(random.Next(-6, 7));
                        break;
                    case 7:
                        overlay.Enabled = !overlay.Enabled;
                        break;
                    case 8:
                        break; // nothing changed
                }

                var snapshot = new ScreenSnapShot(screen);
                List<IScreenBufferLine> buffer = snapshot.Buffer;
                int rowsToPaint = Math.Min(screen.RowCount, Math.Min(snapshot.RowCount, buffer.Count));
                for (int ev = 0; ev < 3; ++ev)
                {
                    long before = rows.TextsMade;
                    for (int row = 0; row < rowsToPaint; ++row)
                        Assert.AreEqual(Shown(buffer[row]), rows.Get(row, buffer[row]), "step " + step + ", event " + ev + ", row " + row);
                    rows.Keep(rowsToPaint);
                    if (ev > 0)
                        Assert.AreEqual(before, rows.TextsMade, "step " + step + ": the same snapshot drawn again makes no string");
                    ++draws;
                }
            }
            Assert.Greater(draws, 0);
        }

        /// <summary>
        /// Drawing an unchanged screen allocates nothing (before: two strings a row each event),
        /// and one changed row makes one string. Bytes per draw before and after are written to
        /// the test output (<c>MEASURE</c> lines; not asserted beyond the zero).
        /// </summary>
        [Test]
        public void AnUnchangedScreenAllocatesNothing()
        {
            foreach (int[] size in new[] { new[] { 36, 50 }, new[] { 160, 255 } })
            {
                var screen = new ScreenBuffer();
                screen.SetSize(size[0], size[1]);
                for (int i = 0; i < size[0]; ++i)
                    screen.Print("line " + i + new string('x', size[1] / 2));
                List<IScreenBufferLine> buffer = new ScreenSnapShot(screen).Buffer;
                var rows = new TerminalRowTexts();
                Action draw = () =>
                {
                    for (int row = 0; row < buffer.Count; ++row)
                        rows.Get(row, buffer[row]);
                    rows.Keep(buffer.Count);
                };
                Action oldDraw = () =>
                {
                    for (int row = 0; row < buffer.Count; ++row)
                        Shown(buffer[row]);
                };

                Assert.AreEqual(0, AllocationCounter.BytesAllocated(draw), size[0] + "x" + size[1]);
                long oldBytes = AllocationCounter.BytesAllocated(oldDraw) / 100;
                Console.WriteLine("MEASURE drawing an unchanged {0}x{1} screen: {2} bytes per event before, 0 after", size[1], size[0], oldBytes);

                long made = rows.TextsMade;
                screen.PrintAt("changed", 3, 0);
                draw();
                draw();
                Assert.AreEqual(made + 1, rows.TextsMade, "one changed row, one string");
            }
        }
    }

    /// <summary>
    /// The status strip as last laid out (review P-07, <see cref="StatusStripCache"/>): the same
    /// layout as a fresh <see cref="StatusStrip"/>, made again only when something it shows
    /// changed, so an unchanged strip allocates nothing.
    /// </summary>
    [TestFixture]
    public class StatusStripCacheTest
    {
        private const float CHAR = 7f, GAP = 10f, CHIP_PADDING = 4f, DOT = 12f;

        private int measured;
        private Func<string, float> measure;
        private readonly object style = new object();

        [SetUp]
        public void SetUp()
        {
            measured = 0;
            measure = text => { ++measured; return text.Length * CHAR; };
        }

        private static TerminalStatus Running(string path, bool newOutput = false, int clients = 0)
        {
            return new TerminalStatus { AtPrompt = false, ProgramPath = path, NewOutput = newOutput, TelnetClients = clients };
        }

        private List<StatusStrip.Placement> Fresh(float x, float width, TerminalStatus status, string notice, int columns, int rows)
        {
            return new StatusStrip(measure, GAP, CHIP_PADDING, DOT, "~").Layout(x, width,
                status == null ? null : status.StateText, status == null ? null : status.Chips, notice, columns + "x" + rows);
        }

        private List<StatusStrip.Placement> Cached(StatusStripCache cache, float x, float width, TerminalStatus status, string notice, int columns, int rows)
        {
            return cache.Layout(style, measure, GAP, CHIP_PADDING, DOT, "~", x, width, status, notice, columns, rows);
        }

        private static string Text(List<StatusStrip.Placement> placed)
        {
            var parts = new List<string>();
            foreach (StatusStrip.Placement p in placed)
                parts.Add(p.Part + ":" + p.ChipIndex + ":" + p.Text + "@" + p.X + "+" + p.Width);
            return string.Join(", ", parts.ToArray());
        }

        [Test]
        public void TheLayoutIsAFreshStripsOne()
        {
            var cache = new StatusStripCache();
            var cases = new[]
            {
                new { Width = 600f, Status = Running("0:/launch.ks", true, 2), Notice = (string)null },
                new { Width = 600f, Status = Running("0:/launch.ks", true, 2), Notice = "Copied 3 lines" },
                new { Width = 120f, Status = Running("0:/a/very/long/path/to/launch.ks", true, 1), Notice = "Copied" },
                new { Width = 60f, Status = new TerminalStatus { AtPrompt = true }, Notice = (string)null },
                new { Width = 600f, Status = (TerminalStatus)null, Notice = (string)null },
                new { Width = 600f, Status = new TerminalStatus { Mode = ProcessorModes.OFF, TelnetClients = 1 }, Notice = (string)null },
                new { Width = 5f, Status = new TerminalStatus { AtPrompt = true, Connected = false }, Notice = "x" },
            };
            foreach (var c in cases)
            {
                string expected = Text(Fresh(3, c.Width, c.Status, c.Notice, 50, 36));
                Assert.AreEqual(expected, Text(Cached(cache, 3, c.Width, c.Status, c.Notice, 50, 36)), expected);
                Assert.AreEqual(c.Status == null ? null : c.Status.StateText, cache.StateText);
                Assert.AreEqual("50x36", cache.Size);
                if (c.Status != null)
                    Assert.AreEqual(c.Status.Chips.Count, cache.Chips.Count);
                else
                    Assert.IsNull(cache.Chips);
            }
        }

        [Test]
        public void AnUnchangedStripIsNotLaidOutAgain()
        {
            var cache = new StatusStripCache();
            TerminalStatus status = Running("0:/launch.ks", true, 2);
            List<StatusStrip.Placement> first = Cached(cache, 3, 400, status, "Copied", 50, 36);
            string size = cache.Size;
            List<StatusChip> chips = cache.Chips;
            int measures = measured;

            Assert.AreSame(first, Cached(cache, 3, 400, status, "Copied", 50, 36), "the same status");
            Assert.AreSame(first, Cached(cache, 3, 400, Running("0:/launch.ks", true, 2), "Copied", 50, 36), "a new status showing the same (the next refresh)");
            Assert.AreSame(first, Cached(cache, 3, 400, Running("0:/launch.ks", true, 2), string.Copy("Copied"), 50, 36), "an equal notice");
            Assert.AreSame(size, cache.Size, "the size text is kept");
            Assert.AreSame(chips, cache.Chips, "the chips are kept: the placements' chip indexes count in them");
            Assert.AreEqual(1, cache.LayoutsMade);
            Assert.AreEqual(measures, measured, "nothing measured again");
        }

        [Test]
        public void EachChangeLaysItOutAgain()
        {
            var cache = new StatusStripCache();
            TerminalStatus status = Running("0:/launch.ks", true, 2);
            Cached(cache, 3, 400, status, null, 50, 36);
            int layouts = cache.LayoutsMade;

            Action<string, Func<List<StatusStrip.Placement>>, Func<List<StatusStrip.Placement>>> changes = (what, cached, fresh) =>
            {
                Assert.AreEqual(Text(fresh()), Text(cached()), what);
                Assert.AreEqual(++layouts, cache.LayoutsMade, what + ": laid out again");
                cached();
                Assert.AreEqual(layouts, cache.LayoutsMade, what + ": then kept");
            };

            changes("the width", () => Cached(cache, 3, 300, status, null, 50, 36), () => Fresh(3, 300, status, null, 50, 36));
            changes("the place", () => Cached(cache, 4, 300, status, null, 50, 36), () => Fresh(4, 300, status, null, 50, 36));
            changes("a notice", () => Cached(cache, 4, 300, status, "Copied", 50, 36), () => Fresh(4, 300, status, "Copied", 50, 36));
            changes("another notice", () => Cached(cache, 4, 300, status, "Copied 2 lines", 50, 36), () => Fresh(4, 300, status, "Copied 2 lines", 50, 36));
            changes("the notice gone", () => Cached(cache, 4, 300, status, null, 50, 36), () => Fresh(4, 300, status, null, 50, 36));
            changes("the columns", () => Cached(cache, 4, 300, status, null, 51, 36), () => Fresh(4, 300, status, null, 51, 36));
            Assert.AreEqual("51x36", cache.Size);
            changes("the rows", () => Cached(cache, 4, 300, status, null, 51, 37), () => Fresh(4, 300, status, null, 51, 37));
            Assert.AreEqual("51x37", cache.Size);

            TerminalStatus other = Running("0:/land.ks", true, 2);
            changes("the state", () => Cached(cache, 4, 300, other, null, 51, 37), () => Fresh(4, 300, other, null, 51, 37));
            TerminalStatus fewer = Running("0:/land.ks", false, 2);
            changes("a chip gone", () => Cached(cache, 4, 300, fewer, null, 51, 37), () => Fresh(4, 300, fewer, null, 51, 37));
            TerminalStatus oneClient = Running("0:/land.ks", false, 1);
            changes("a chip's text", () => Cached(cache, 4, 300, oneClient, null, 51, 37), () => Fresh(4, 300, oneClient, null, 51, 37));
            Assert.AreEqual("1 telnet client", cache.Chips[0].Text);
            TerminalStatus lost = new TerminalStatus { ProgramPath = "0:/land.ks", Connected = false, TelnetClients = 1 };
            changes("a chip of another kind", () => Cached(cache, 4, 300, lost, null, 51, 37), () => Fresh(4, 300, lost, null, 51, 37));
            changes("no status", () => Cached(cache, 4, 300, null, null, 51, 37), () => Fresh(4, 300, null, null, 51, 37));
            Assert.IsNull(cache.StateText);
            Assert.IsNull(cache.Chips);
            changes("a status again", () => Cached(cache, 4, 300, lost, null, 51, 37), () => Fresh(4, 300, lost, null, 51, 37));
        }

        [Test]
        public void OtherMeasuresLayItOutAgain()
        {
            var cache = new StatusStripCache();
            TerminalStatus status = Running("0:/launch.ks", true, 2);
            Func<string, float> wide = text => text.Length * 2 * CHAR;
            cache.Layout(style, measure, GAP, CHIP_PADDING, DOT, "~", 3, 400, status, null, 50, 36);
            int layouts = cache.LayoutsMade;

            var placed = cache.Layout(new object(), wide, GAP, CHIP_PADDING, DOT, "~", 3, 400, status, null, 50, 36);
            Assert.AreEqual(++layouts, cache.LayoutsMade, "another style (another font or size)");
            Assert.AreEqual(Text(new StatusStrip(wide, GAP, CHIP_PADDING, DOT, "~").Layout(3, 400, status.StateText, status.Chips, null, "50x36")), Text(placed),
                "measured by the new measurer");
            object key = new object();
            cache.Layout(key, measure, GAP, CHIP_PADDING, DOT, "~", 3, 400, status, null, 50, 36);
            ++layouts;
            cache.Layout(key, measure, GAP + 1, CHIP_PADDING, DOT, "~", 3, 400, status, null, 50, 36);
            Assert.AreEqual(++layouts, cache.LayoutsMade, "the gap");
            cache.Layout(key, measure, GAP + 1, CHIP_PADDING + 1, DOT, "~", 3, 400, status, null, 50, 36);
            Assert.AreEqual(++layouts, cache.LayoutsMade, "the chip padding");
            cache.Layout(key, measure, GAP + 1, CHIP_PADDING + 1, DOT + 1, "~", 3, 400, status, null, 50, 36);
            Assert.AreEqual(++layouts, cache.LayoutsMade, "the dot");
            cache.Layout(key, measure, GAP + 1, CHIP_PADDING + 1, DOT + 1, "...", 3, 400, status, null, 50, 36);
            Assert.AreEqual(++layouts, cache.LayoutsMade, "the ellipsis (found once the font is checked)");
            cache.Layout(key, measure, GAP + 1, CHIP_PADDING + 1, DOT + 1, "...", 3, 400, status, null, 50, 36);
            Assert.AreEqual(layouts, cache.LayoutsMade, "then kept");
        }

        [Test]
        public void AnUnchangedStripAllocatesNothing()
        {
            var cache = new StatusStripCache();
            TerminalStatus status = Running("0:/launch.ks", true, 2);
            string notice = "Copied";
            Assert.AreEqual(0, AllocationCounter.BytesAllocated(() => cache.Layout(style, measure, GAP, CHIP_PADDING, DOT, "~", 3, 400, status, notice, 50, 36)));
        }
    }

    /// <summary>The header title as fitted (review P-07, <see cref="ShortenedText"/>): as <see cref="StatusStrip.Shorten"/>, fitted again only on a change.</summary>
    [TestFixture]
    public class ShortenedTextTest
    {
        private static float Measure(string text)
        {
            return text.Length * 7f;
        }

        [Test]
        public void ItIsFittedAgainOnlyOnAChange()
        {
            var title = new ShortenedText();
            object style = new object();
            Func<string, float> measure = Measure;
            const string name = "CX-4181 Scriptable Control System (cpu top)";

            string shown = title.Get(name, 100, style, measure, "~");
            Assert.AreEqual(StatusStrip.Shorten(name, 100, measure, "~"), shown);
            Assert.AreSame(shown, title.Get(name, 100, style, measure, "~"));
            Assert.AreSame(shown, title.Get(string.Copy(name), 100, style, measure, "~"), "an equal text");
            Assert.AreEqual(1, title.Fitted);
            Assert.AreEqual(0, AllocationCounter.BytesAllocated(() => title.Get(name, 100, style, measure, "~")));

            Assert.AreEqual(StatusStrip.Shorten(name, 150, measure, "~"), title.Get(name, 150, style, measure, "~"), "the room");
            Assert.AreEqual(StatusStrip.Shorten("other", 150, measure, "~"), title.Get("other", 150, style, measure, "~"), "the text");
            Func<string, float> wide = text => text.Length * 30f;
            Assert.AreEqual(StatusStrip.Shorten("other", 150, wide, "~"), title.Get("other", 150, new object(), wide, "~"), "the style");
            Assert.AreEqual(StatusStrip.Shorten("other", 150, wide, "..."), title.Get("other", 150, style, wide, "..."), "the ellipsis");
            Assert.AreEqual(5, title.Fitted);
            Assert.AreEqual("", title.Get(null, 150, style, wide, "..."), "no title");
            Assert.AreEqual("", title.Get(null, 150, style, wide, "..."));
        }
    }
}
