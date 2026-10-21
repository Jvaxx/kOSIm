using System;
using kOS.Safe.Encapsulation;
using kOS.Safe.Screen;
using kOS.Safe.UserIO;
using NUnit.Framework;

namespace kOS.Safe.Test.Terminal
{
    /// <summary>
    /// Phase 4, step 1: the GUI terminal's layout. Cells and window size are each other's
    /// inverse at every UI scale and cell size, the chrome follows the scale only, and no part
    /// has a negative size or overlaps another.
    /// </summary>
    [TestFixture]
    public class TerminalChromeLayoutTest
    {
        private static readonly float[] scales = { 0.5f, 0.8f, 1f, 1.25f, 1.3f, 1.5f, 2f, 3f };
        // Cell sizes the game gives: CHARHEIGHT 4 to 48, widths from the font's advance.
        private static readonly int[,] cells = { { 2, 4 }, { 3, 6 }, { 7, 12 }, { 8, 14 }, { 12, 20 }, { 26, 48 } };
        private static readonly int[] columnCounts = { 1, 2, 15, 16, 50, 80, 255 };
        private static readonly int[] rowCounts = { 1, 3, 4, 36, 100, 160 };

        private static void ForEachLayout(Action<TerminalChromeLayout> check)
        {
            foreach (float scale in scales)
                for (int c = 0; c < cells.GetLength(0); ++c)
                    check(new TerminalChromeLayout(scale, cells[c, 0], cells[c, 1]));
        }

        private static string Describe(TerminalChromeLayout layout)
        {
            return string.Format("scale {0}, cell {1}x{2}", layout.Scale, layout.CellWidth, layout.CellHeight);
        }

        [Test]
        public void CellsRoundTripThroughTheWindowSize()
        {
            ForEachLayout(layout =>
            {
                foreach (int columns in columnCounts)
                {
                    float width = layout.WindowWidthFor(columns);
                    if (width > layout.MinimumWidth)
                        Assert.AreEqual(columns, layout.ColumnsThatFit(width), Describe(layout) + ", " + columns + " columns");
                    else
                        Assert.GreaterOrEqual(layout.ColumnsThatFit(width), columns, Describe(layout) + ", widened to the minimum");
                }
                foreach (int rows in rowCounts)
                    Assert.AreEqual(rows, layout.RowsThatFit(layout.WindowHeightFor(rows)), Describe(layout) + ", " + rows + " rows");
            });
        }

        [Test]
        public void AWindowSizeGivesTheMostCellsThatFitIt()
        {
            ForEachLayout(layout =>
            {
                for (float width = 0; width < 900; width += 7.5f)
                {
                    int columns = layout.ColumnsThatFit(width);
                    Assert.LessOrEqual(layout.Padding + columns * layout.CellWidth + layout.GutterWidth, Math.Max(width, layout.Padding + layout.GutterWidth),
                        Describe(layout) + ", width " + width);
                    Assert.Greater(layout.Padding + (columns + 1) * layout.CellWidth + layout.GutterWidth, width, Describe(layout) + ", width " + width);
                    Assert.GreaterOrEqual(layout.ColumnsThatFit(layout.WindowWidthFor(columns)), columns, "snapping keeps the cells");
                }
                for (float height = 0; height < 900; height += 7.5f)
                {
                    int rows = layout.RowsThatFit(height);
                    if (rows > 0)
                        Assert.LessOrEqual(layout.WindowHeightFor(rows), height, Describe(layout) + ", height " + height);
                    Assert.Greater(layout.WindowHeightFor(rows + 1), height, Describe(layout) + ", height " + height);
                }
            });
        }

        [Test]
        public void TheChromeFollowsTheScaleAndNotTheCells()
        {
            var one = new TerminalChromeLayout(1f, 7, 12);
            Assert.AreEqual(TerminalChromeLayout.BASE_HEADER_HEIGHT, one.HeaderHeight);
            Assert.AreEqual(TerminalChromeLayout.BASE_STRIP_HEIGHT, one.StripHeight);
            Assert.AreEqual(TerminalChromeLayout.BASE_PADDING, one.Padding);
            Assert.AreEqual(TerminalChromeLayout.BASE_SCROLL_BAR_WIDTH + 2, one.GutterWidth);
            Assert.AreEqual(TerminalChromeLayout.BASE_FONT_SIZE, one.FontSize);
            Assert.AreEqual(1, one.Border);

            var two = new TerminalChromeLayout(2f, 26, 48);
            Assert.AreEqual(2 * TerminalChromeLayout.BASE_HEADER_HEIGHT, two.HeaderHeight);
            Assert.AreEqual(2 * TerminalChromeLayout.BASE_STRIP_HEIGHT, two.StripHeight);
            Assert.AreEqual(2 * TerminalChromeLayout.BASE_PADDING, two.Padding);
            Assert.AreEqual(2 * TerminalChromeLayout.BASE_FONT_SIZE, two.FontSize);
            Assert.AreEqual(2 * TerminalChromeLayout.BASE_MINIMUM_WIDTH, two.MinimumWidth);
            Assert.AreEqual(2, two.Border);

            var bigCells = new TerminalChromeLayout(1.3f, 26, 48);
            var smallCells = new TerminalChromeLayout(1.3f, 3, 6);
            Assert.AreEqual(26, bigCells.HeaderHeight, "20 x 1.3");
            Assert.AreEqual(bigCells.HeaderHeight, smallCells.HeaderHeight);
            Assert.AreEqual(bigCells.StripHeight, smallCells.StripHeight);
            Assert.AreEqual(bigCells.Padding, smallCells.Padding);
            Assert.AreEqual(bigCells.FontSize, smallCells.FontSize);
            Assert.AreEqual(14, bigCells.FontSize, "11 x 1.3, rounded");
        }

        [Test]
        public void TheScaleIsKeptSane()
        {
            Assert.AreEqual(1f, TerminalChromeLayout.ClampScale(float.NaN));
            Assert.AreEqual(1f, TerminalChromeLayout.ClampScale(float.PositiveInfinity));
            Assert.AreEqual(TerminalChromeLayout.MINIMUM_SCALE, TerminalChromeLayout.ClampScale(0f));
            Assert.AreEqual(TerminalChromeLayout.MINIMUM_SCALE, TerminalChromeLayout.ClampScale(-2f));
            Assert.AreEqual(TerminalChromeLayout.MAXIMUM_SCALE, TerminalChromeLayout.ClampScale(10f));
            Assert.AreEqual(1.3f, TerminalChromeLayout.ClampScale(1.3f));
            // The smallest scale still leaves room for descenders below the bottom row:
            Assert.AreEqual(TerminalChromeLayout.MINIMUM_PADDING, new TerminalChromeLayout(0.5f, 7, 12).Padding);
        }

        [Test]
        public void TheMinimumWindowHoldsTheScriptMinimum()
        {
            ForEachLayout(layout =>
            {
                Assert.GreaterOrEqual(layout.ColumnsThatFit(layout.MinimumWindowWidth), TerminalStruct.MINCOLUMNS, Describe(layout));
                Assert.AreEqual(TerminalStruct.MINROWS, layout.RowsThatFit(layout.MinimumWindowHeight), Describe(layout));
                Assert.GreaterOrEqual(layout.MinimumWindowWidth, layout.MinimumWidth, Describe(layout));
            });
        }

        [Test]
        public void NothingIsNegative()
        {
            foreach (int cellWidth in new[] { -3, 0, 7 })
            {
                var layout = new TerminalChromeLayout(1f, cellWidth, cellWidth * 2);
                foreach (float size in new[] { -100f, 0f, 1f, 10f })
                {
                    if (cellWidth <= 0)
                    {
                        Assert.AreEqual(0, layout.ColumnsThatFit(1000), "no cell width");
                        Assert.AreEqual(0, layout.RowsThatFit(1000), "no cell height");
                    }
                    Assert.AreEqual(0, layout.ColumnsThatFit(size), "width " + size);
                    Assert.AreEqual(0, layout.RowsThatFit(size), "height " + size);
                    foreach (ScreenRect rect in new[]
                    {
                        layout.Header(size), layout.CloseButton(size), layout.LargerTextButton(size), layout.SmallerTextButton(size),
                        layout.HeaderButtons(size), layout.Title(size), layout.PresetButton(1, size), layout.PresetButton(3, size),
                        layout.Body(size, size), layout.SelectionArea(size, size), layout.Gutter(size, size),
                        layout.Strip(size, size), layout.Grip(size, size), layout.StripContent(size, size),
                        layout.Field(-5, -5), layout.Field(0, 0)
                    })
                    {
                        Assert.GreaterOrEqual(rect.Width, 0, rect.ToString());
                        Assert.GreaterOrEqual(rect.Height, 0, rect.ToString());
                        Assert.GreaterOrEqual(rect.X, 0, rect.ToString());
                        Assert.GreaterOrEqual(rect.Y, 0, rect.ToString());
                    }
                }
            }
        }

        private static void AssertInside(ScreenRect inner, ScreenRect outer, string what)
        {
            Assert.GreaterOrEqual(inner.X, outer.X, what + ": left " + inner + " in " + outer);
            Assert.GreaterOrEqual(inner.Y, outer.Y, what + ": top " + inner + " in " + outer);
            Assert.LessOrEqual(inner.X + inner.Width, outer.X + outer.Width, what + ": right " + inner + " in " + outer);
            Assert.LessOrEqual(inner.Y + inner.Height, outer.Y + outer.Height, what + ": bottom " + inner + " in " + outer);
        }

        private static void AssertApart(ScreenRect a, ScreenRect b, string what)
        {
            bool apart = a.X + a.Width <= b.X || b.X + b.Width <= a.X || a.Y + a.Height <= b.Y || b.Y + b.Height <= a.Y;
            Assert.IsTrue(apart, what + ": " + a + " and " + b);
        }

        [Test]
        public void ThePartsTileASnappedWindow()
        {
            ForEachLayout(layout =>
            {
                foreach (int columns in columnCounts)
                    foreach (int rows in new[] { 3, 36 })
                    {
                        float width = layout.WindowWidthFor(columns), height = layout.WindowHeightFor(rows);
                        string where = Describe(layout) + ", " + columns + "x" + rows;
                        var window = new ScreenRect(0, 0, width, height);
                        ScreenRect header = layout.Header(width), body = layout.Body(width, height), strip = layout.Strip(width, height);
                        ScreenRect field = layout.Field(columns, rows);

                        Assert.AreEqual(height, header.Height + body.Height + strip.Height, where + ": header, body and strip");
                        Assert.AreEqual(header.Height, body.Y, where);
                        Assert.AreEqual(body.Y + body.Height, strip.Y, where);
                        AssertInside(field, layout.SelectionArea(width, height), where + ": field in the selection area");
                        AssertApart(field, layout.Gutter(width, height), where + ": field and gutter");
                        AssertInside(layout.Gutter(width, height), body, where + ": gutter");
                        Assert.AreEqual(field.Y - body.Y, body.Y + body.Height - (field.Y + field.Height), where + ": even padding top and bottom");
                        Assert.AreEqual(layout.Padding, field.X, where + ": left padding");
                        if (width > layout.MinimumWidth)
                            Assert.AreEqual(width - layout.GutterWidth, field.X + field.Width, where + ": the gutter follows the field");

                        ScreenRect smaller = layout.SmallerTextButton(width), larger = layout.LargerTextButton(width), close = layout.CloseButton(width);
                        foreach (ScreenRect part in new[] { header, strip, smaller, larger, close, layout.Grip(width, height) })
                            AssertInside(part, window, where);
                        foreach (ScreenRect button in new[] { smaller, larger, close })
                        {
                            AssertInside(button, header, where + ": a button in the header");
                            AssertInside(button, layout.HeaderButtons(width), where + ": a button in the header's buttons");
                            AssertApart(layout.Title(width), button, where + ": title and a button");
                        }
                        AssertApart(smaller, larger, where + ": smaller and larger");
                        AssertApart(larger, close, where + ": larger and close");
                        Assert.Less(smaller.X, larger.X, where + ": smaller, then larger");
                        Assert.Less(larger.X, close.X, where + ": larger, then close");
                        Assert.AreEqual(width, close.X + close.Width, where + ": close at the right edge");
                        if (layout.ShowsPresets(width))
                        {
                            // The presets' own checks: TerminalPresetLayoutTest.
                            ScreenRect first = layout.PresetButton(1, width);
                            Assert.AreEqual(first.X, layout.HeaderButtons(width).X, where + ": the buttons start at the first preset");
                            AssertInside(first, header, where + ": a preset in the header");
                            AssertApart(layout.Title(width), first, where + ": title and the presets");
                        }
                        else
                        {
                            Assert.AreEqual(smaller.X, layout.HeaderButtons(width).X, where + ": the buttons start at the smaller one");
                            Assert.AreEqual(smaller.Width + larger.Width + close.Width, layout.HeaderButtons(width).Width, where + ": no gap in the buttons");
                        }
                        Assert.Greater(layout.Title(width).Width, 0, where + ": room for a title");
                        AssertInside(layout.Grip(width, height), strip, where + ": grip");
                        AssertInside(layout.StripContent(width, height), strip, where + ": strip content");
                        AssertApart(layout.StripContent(width, height), layout.Grip(width, height), where + ": strip content and grip");
                        Assert.Greater(layout.StripContent(width, height).Width, 0, where + ": room for the state");
                    }
            });
        }
    }

    /// <summary>
    /// Phase 4, step 3: the header's smaller and larger text buttons change <c>CHARHEIGHT</c> in
    /// steps of 2 within the script's range.
    /// </summary>
    [TestFixture]
    public class CharacterHeightStepTest
    {
        [Test]
        public void TheButtonsStepByTwo()
        {
            Assert.AreEqual(10, TerminalChromeLayout.SmallerCharacterHeight(12));
            Assert.AreEqual(14, TerminalChromeLayout.LargerCharacterHeight(12));
            Assert.AreEqual(11, TerminalChromeLayout.SmallerCharacterHeight(13), "an odd size stays odd");
            Assert.AreEqual(15, TerminalChromeLayout.LargerCharacterHeight(13));
        }

        [Test]
        public void TheScriptRangeIsKept()
        {
            Assert.AreEqual(TerminalStruct.MINCHARPIXELS, TerminalChromeLayout.SmallerCharacterHeight(TerminalStruct.MINCHARPIXELS), "at the minimum");
            Assert.AreEqual(TerminalStruct.MINCHARPIXELS, TerminalChromeLayout.SmallerCharacterHeight(TerminalStruct.MINCHARPIXELS + 1), "one above it");
            Assert.AreEqual(TerminalStruct.MAXCHARPIXELS, TerminalChromeLayout.LargerCharacterHeight(TerminalStruct.MAXCHARPIXELS), "at the maximum");
            Assert.AreEqual(TerminalStruct.MAXCHARPIXELS, TerminalChromeLayout.LargerCharacterHeight(TerminalStruct.MAXCHARPIXELS - 1), "one below it");
            Assert.AreEqual(TerminalStruct.MINCHARPIXELS, TerminalChromeLayout.LargerCharacterHeight(0), "below the range: back in it");
            Assert.AreEqual(TerminalStruct.MAXCHARPIXELS, TerminalChromeLayout.SmallerCharacterHeight(100), "above the range: back in it");
        }
    }

    /// <summary>
    /// Phase 4, step 1: the status strip. Parts never overlap (BAR-01) and give way in a fixed
    /// order: chips by priority, then the state is shortened, then the notice, the size, the
    /// state text and the dot go.
    /// </summary>
    [TestFixture]
    public class StatusStripTest
    {
        private const float CHAR = 7f, GAP = 10f, CHIP_PADDING = 4f, DOT = 12f;

        private static float Measure(string text)
        {
            return text.Length * CHAR;
        }

        private static StatusStrip Strip()
        {
            return new StatusStrip(Measure, GAP, CHIP_PADDING, DOT, "~");
        }

        private static StatusChip Chip(StatusChipKind kind, string text)
        {
            return new StatusChip(kind, text);
        }

        private static StatusChip[] AllChips()
        {
            return new[]
            {
                Chip(StatusChipKind.Delay, "Delay 2.4 s"),
                Chip(StatusChipKind.NoConnection, "No connection"),
                Chip(StatusChipKind.NewOutput, "New output"),
                Chip(StatusChipKind.Telnet, "2 telnet clients")
            };
        }

        private static string Shown(System.Collections.Generic.List<StatusStrip.Placement> placed)
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (StatusStrip.Placement p in placed)
                parts.Add(p.Part + ":" + p.Text);
            return string.Join(", ", parts.ToArray());
        }

        private static void AssertNoOverlap(System.Collections.Generic.List<StatusStrip.Placement> placed, float x, float width, string where)
        {
            foreach (StatusStrip.Placement p in placed)
            {
                Assert.GreaterOrEqual(p.X, x, where + ": " + Shown(placed));
                Assert.LessOrEqual(p.X + p.Width, x + width + 0.001f, where + ": " + Shown(placed));
                Assert.GreaterOrEqual(p.Width, 0, where);
            }
            for (int i = 0; i < placed.Count; ++i)
                for (int j = i + 1; j < placed.Count; ++j)
                {
                    StatusStrip.Placement a = placed[i], b = placed[j];
                    bool apart = a.X + a.Width + GAP <= b.X + 0.001f || b.X + b.Width + GAP <= a.X + 0.001f;
                    Assert.IsTrue(apart, where + ": " + Shown(placed));
                }
        }

        [Test]
        public void EverythingFitsInAWideStrip()
        {
            var placed = Strip().Layout(5, 1000, "Running 0:/launch.ks", AllChips(), "Copied 3 lines", "50x36");
            Assert.AreEqual("State:Running 0:/launch.ks, Chip:Delay 2.4 s, Chip:No connection, Chip:New output, " +
                "Chip:2 telnet clients, Size:50x36, Notice:Copied 3 lines", Shown(placed));
            Assert.AreEqual(5, placed[0].X, "the state at the left");
            Assert.AreEqual(DOT + Measure("Running 0:/launch.ks"), placed[0].Width, "with its dot");
            Assert.AreEqual(5 + placed[0].Width + GAP, placed[1].X, "the chips after it");
            Assert.AreEqual(Measure("Delay 2.4 s") + 2 * CHIP_PADDING, placed[1].Width);
            Assert.AreEqual(1, placed[2].ChipIndex);
            Assert.AreEqual(1005 - Measure("50x36"), placed[5].X, "the size at the right");
            Assert.AreEqual(placed[5].X - GAP - Measure("Copied 3 lines"), placed[6].X, "the notice before the size");
            AssertNoOverlap(placed, 5, 1000, "wide");
        }

        [Test]
        public void TheTelnetCountNoLongerCoversTheState()
        {
            // BAR-01 at 50 columns: the old label was drawn over "Keyclicker" and the state.
            var chips = new[] { Chip(StatusChipKind.Telnet, "1 telnet client") };
            string state = "Running 0:/launch.ks";
            float all = DOT + Measure(state) + GAP + Measure("1 telnet client") + 2 * CHIP_PADDING + GAP + Measure("50x36");
            var fits = Strip().Layout(0, all, state, chips, null, "50x36");
            Assert.AreEqual("State:Running 0:/launch.ks, Chip:1 telnet client, Size:50x36", Shown(fits));
            AssertNoOverlap(fits, 0, all, "exactly enough");

            var narrower = Strip().Layout(0, all - 1, state, chips, null, "50x36");
            Assert.AreEqual("State:Running 0:/launch.ks, Size:50x36", Shown(narrower), "the count goes first");
            AssertNoOverlap(narrower, 0, all - 1, "one pixel short");
        }

        [Test]
        public void ChipsGoLowestPriorityFirst()
        {
            string state = "Ready";
            float stateAndSize = DOT + Measure(state) + GAP + Measure("50x36");
            float[] chipWidths = new float[4];
            StatusChip[] chips = AllChips();
            for (int i = 0; i < chips.Length; ++i)
                chipWidths[i] = Measure(chips[i].Text) + 2 * CHIP_PADDING + GAP;

            float everything = stateAndSize + chipWidths[0] + chipWidths[1] + chipWidths[2] + chipWidths[3];
            Assert.AreEqual(4, Count(Strip().Layout(0, everything, state, chips, null, "50x36"), StatusStrip.Part.Chip));
            var three = Strip().Layout(0, everything - 1, state, chips, null, "50x36");
            Assert.AreEqual("State:Ready, Chip:Delay 2.4 s, Chip:No connection, Chip:New output, Size:50x36", Shown(three));
            var two = Strip().Layout(0, stateAndSize + chipWidths[0] + chipWidths[1] + chipWidths[2] - 1, state, chips, null, "50x36");
            Assert.AreEqual("State:Ready, Chip:Delay 2.4 s, Chip:No connection, Size:50x36", Shown(two));
            var one = Strip().Layout(0, stateAndSize + chipWidths[0] + chipWidths[1] - 1, state, chips, null, "50x36");
            Assert.AreEqual("State:Ready, Chip:No connection, Size:50x36", Shown(one), "losing the connection matters most");
            var none = Strip().Layout(0, stateAndSize + chipWidths[1] - 1, state, chips, null, "50x36");
            Assert.AreEqual("State:Ready, Size:50x36", Shown(none));
        }

        [Test]
        public void ChipsOfEqualPriorityGoFromTheRight()
        {
            var chips = new[] { Chip(StatusChipKind.Telnet, "aaaa"), Chip(StatusChipKind.Telnet, "bbbb") };
            float width = DOT + Measure("Ready") + GAP + Measure("aaaa") + 2 * CHIP_PADDING;
            Assert.AreEqual("State:Ready, Chip:aaaa", Shown(Strip().Layout(0, width, "Ready", chips, null, null)));
        }

        [Test]
        public void ThenTheStateIsShortenedThenTheNoticeTheSizeTheTextAndTheDotGo()
        {
            string state = "Waiting in 0:/a/long/path.ks";
            string notice = "Copied 12 lines", size = "15x3";
            var chips = new[] { Chip(StatusChipKind.NoConnection, "No connection") };
            float noticeAndSize = GAP + Measure(notice) + GAP + Measure(size);

            var full = Strip().Layout(0, DOT + Measure(state) + noticeAndSize, state, chips, notice, size);
            Assert.AreEqual("State:" + state + ", Size:15x3, Notice:" + notice, Shown(full), "chips go before the state is shortened");

            var shortened = Strip().Layout(0, DOT + Measure("Waiting in~") + noticeAndSize, state, chips, notice, size);
            Assert.AreEqual("State:Waiting in~, Size:15x3, Notice:" + notice, Shown(shortened), "a trailing space is not kept before the ellipsis");

            var shortest = Strip().Layout(0, DOT + Measure("W~") + noticeAndSize, state, chips, notice, size);
            Assert.AreEqual("State:W~, Size:15x3, Notice:" + notice, Shown(shortest));

            var noNotice = Strip().Layout(0, DOT + Measure("W~") + noticeAndSize - 1, state, chips, notice, size);
            Assert.AreEqual("State:" + StatusStrip.Shorten(state, DOT + Measure("W~") + noticeAndSize - 1 - DOT - GAP - Measure(size), Measure, "~") +
                ", Size:15x3", Shown(noNotice), "the notice goes, and the state gets its room");
            Assert.AreNotEqual("State:W~, Size:15x3", Shown(noNotice));

            var noSize = Strip().Layout(0, DOT + Measure("W~") + GAP + Measure(size) - 1, state, null, null, size);
            Assert.AreEqual("State:Waitin~", Shown(noSize), "then the size");

            var dotOnly = Strip().Layout(0, DOT + Measure("W~") - 1, state, null, null, size);
            Assert.AreEqual(1, dotOnly.Count);
            Assert.AreEqual("", dotOnly[0].Text, "then the text");
            Assert.AreEqual(DOT, dotOnly[0].Width);

            Assert.AreEqual(0, Strip().Layout(0, DOT - 1, state, null, null, size).Count, "then the dot");
            Assert.AreEqual(0, Strip().Layout(0, -10, state, chips, notice, size).Count, "nothing fits a negative room");
        }

        [Test]
        public void NoStateMeansNoDot()
        {
            var placed = Strip().Layout(0, 200, null, null, null, "50x36");
            Assert.AreEqual("Size:50x36", Shown(placed));
            Assert.AreEqual(200 - Measure("50x36"), placed[0].X);
            Assert.AreEqual("State:", Shown(Strip().Layout(0, 200, "", null, null, null)), "an empty state keeps its dot");
        }

        private static int Count(System.Collections.Generic.List<StatusStrip.Placement> placed, StatusStrip.Part part)
        {
            int n = 0;
            foreach (StatusStrip.Placement p in placed)
                if (p.Part == part) ++n;
            return n;
        }

        [Test]
        public void NarrowingOnlyTakesAway()
        {
            string state = "Running 0:/boot/kosim_boot.ks";
            var previous = Strip().Layout(0, 900, state, AllChips(), "Copied the last error", "255x160");
            Assert.AreEqual(7, previous.Count);
            for (float width = 899; width >= -1; width -= 0.5f)
            {
                var placed = Strip().Layout(0, width, state, AllChips(), "Copied the last error", "255x160");
                string where = "width " + width + ": " + Shown(placed) + " after " + Shown(previous);
                AssertNoOverlap(placed, 0, Math.Max(0, width), where);
                Assert.LessOrEqual(placed.Count, previous.Count, where);
                foreach (StatusStrip.Part part in new[] { StatusStrip.Part.Chip, StatusStrip.Part.Notice, StatusStrip.Part.Size, StatusStrip.Part.State })
                    Assert.LessOrEqual(Count(placed, part), Count(previous, part), where);
                if (Count(placed, StatusStrip.Part.Notice) > 0)
                    Assert.AreEqual(1, Count(placed, StatusStrip.Part.Size), where + ": the notice never hides the size");
                foreach (StatusStrip.Placement p in placed)
                    if (p.Part == StatusStrip.Part.State && p.Text != state)
                        Assert.AreEqual(0, Count(placed, StatusStrip.Part.Chip), where + ": chips go before the state is shortened");
                previous = placed;
            }
            Assert.AreEqual(0, previous.Count);
        }

        [Test]
        public void Shorten()
        {
            Assert.AreEqual("abc", StatusStrip.Shorten("abc", 21, Measure, "~"), "fits");
            Assert.AreEqual("a~", StatusStrip.Shorten("abc", 20, Measure, "~"));
            Assert.AreEqual("", StatusStrip.Shorten("abc", 13, Measure, "~"), "not even one character");
            Assert.AreEqual("", StatusStrip.Shorten("", 0, Measure, "~"));
            Assert.AreEqual("", StatusStrip.Shorten(null, 100, Measure, "~"));
            Assert.AreEqual("ab~", StatusStrip.Shorten("ab cd", 3 * CHAR + 1, Measure, "~"));
            Assert.AreEqual("ab~", StatusStrip.Shorten("ab cd", 4 * CHAR, Measure, "~"), "\"ab \" then the ellipsis: the space is dropped");
            // A surrogate pair is never split:
            string pair = "a\U0001F680bc";
            Assert.AreEqual("a~", StatusStrip.Shorten(pair, 3 * CHAR, Measure, "~"), "not half of the pair");
            Assert.AreEqual("a\U0001F680~", StatusStrip.Shorten(pair, 4 * CHAR, Measure, "~"));
            // The ellipsis of the game's font:
            Assert.AreEqual("Running 0:/lau…", StatusStrip.Shorten("Running 0:/launch.ks", 15 * CHAR, Measure, "…"));
        }
    }
}
