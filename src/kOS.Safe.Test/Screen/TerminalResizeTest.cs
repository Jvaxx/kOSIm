using System;
using kOS.Safe.Encapsulation;
using kOS.Safe.Screen;
using kOS.Safe.UserIO;
using NUnit.Framework;

namespace kOS.Safe.Test.Terminal
{
    /// <summary>
    /// Phase 4, step 2: resizing the GUI terminal by its edges, corners and grip, snapped to
    /// whole cells, never smaller than a script can make it nor larger than the screen allows.
    /// </summary>
    [TestFixture]
    public class TerminalResizeTest
    {
        private const ResizeEdges L = ResizeEdges.Left, R = ResizeEdges.Right, T = ResizeEdges.Top, B = ResizeEdges.Bottom;

        private static readonly float[] scales = { 0.5f, 1f, 1.3f, 2f, 3f };

        private static TerminalChromeLayout Layout(float scale = 1f)
        {
            return new TerminalChromeLayout(scale, 8, 14);
        }

        // At scale 1 and 8x14 cells, 50x36 is 414 x 554 (padding 6, gutter 8, header 20, strip 18).
        private static ScreenRect Window(TerminalChromeLayout layout, float x, float y, int columns, int rows)
        {
            return new ScreenRect(x, y, layout.WindowWidthFor(columns), layout.WindowHeightFor(rows));
        }

        // The edges:

        [Test]
        public void TheBandAlongTheFrameGrabsEdgesAndCorners()
        {
            TerminalChromeLayout layout = Layout();
            float w = 414, h = 554;
            Assert.AreEqual(L, TerminalResize.EdgesAt(layout, w, h, 0, 200, false));
            Assert.AreEqual(L, TerminalResize.EdgesAt(layout, w, h, 3, 200, false));
            Assert.AreEqual(ResizeEdges.None, TerminalResize.EdgesAt(layout, w, h, 4, 200, false), "past the band");
            Assert.AreEqual(R, TerminalResize.EdgesAt(layout, w, h, w - 1, 200, false));
            Assert.AreEqual(T, TerminalResize.EdgesAt(layout, w, h, 200, 0, false), "the header's top edge");
            Assert.AreEqual(ResizeEdges.None, TerminalResize.EdgesAt(layout, w, h, 200, 10, false), "the header moves the window");
            Assert.AreEqual(B, TerminalResize.EdgesAt(layout, w, h, 200, h - 2, false));
            Assert.AreEqual(ResizeEdges.None, TerminalResize.EdgesAt(layout, w, h, 200, 300, false), "the characters select");

            Assert.AreEqual(L | T, TerminalResize.EdgesAt(layout, w, h, 1, 1, false));
            Assert.AreEqual(L | T, TerminalResize.EdgesAt(layout, w, h, 1, 11, false), "along the left edge, near the top");
            Assert.AreEqual(L | T, TerminalResize.EdgesAt(layout, w, h, 11, 1, false), "along the top edge, near the left");
            Assert.AreEqual(L, TerminalResize.EdgesAt(layout, w, h, 1, 12, false));
            Assert.AreEqual(L | B, TerminalResize.EdgesAt(layout, w, h, 2, h - 5, false));
            Assert.AreEqual(R | B, TerminalResize.EdgesAt(layout, w, h, w - 5, h - 2, false));
        }

        [Test]
        public void TheGripIsTheBottomRightCornerAndTheButtonsKeepTheirClicks()
        {
            TerminalChromeLayout layout = Layout();
            float w = 414, h = 554;
            ScreenRect grip = layout.Grip(w, h);
            Assert.AreEqual(R | B, TerminalResize.EdgesAt(layout, w, h, grip.X + 1, grip.Y + 1, false), "the whole grip");
            Assert.AreEqual(ResizeEdges.None, TerminalResize.EdgesAt(layout, w, h, w - 1, 0, false), "close button corner");
            foreach (ScreenRect button in new[] { layout.SmallerTextButton(w), layout.LargerTextButton(w), layout.CloseButton(w) })
            {
                Assert.AreEqual(ResizeEdges.None, TerminalResize.EdgesAt(layout, w, h, button.X, 0, false), "a button's top left " + button);
                Assert.AreEqual(ResizeEdges.None, TerminalResize.EdgesAt(layout, w, h, button.X + button.Width / 2, 1, false), "a button's top " + button);
            }
            ScreenRect first = layout.HeaderButtons(w); // from the first preset button (TerminalPresetLayoutTest)
            Assert.AreEqual(T, TerminalResize.EdgesAt(layout, w, h, first.X - 1, 0, false), "the top edge left of the buttons");
            Assert.AreEqual(ResizeEdges.None, TerminalResize.EdgesAt(layout, w, h, w - 1, layout.HeaderHeight - 1, false));
            Assert.AreEqual(R, TerminalResize.EdgesAt(layout, w, h, w - 1, layout.HeaderHeight, false), "the right edge below the header");
        }

        [Test]
        public void TheScrollBarKeepsTheRightEdgeBesideItsTrack()
        {
            TerminalChromeLayout layout = Layout();
            float w = 414, h = 554;
            ScreenRect track = layout.ScrollTrack(w, h);
            Assert.AreEqual(ResizeEdges.None, TerminalResize.EdgesAt(layout, w, h, w - 1, track.Y + 10, true));
            Assert.AreEqual(R, TerminalResize.EdgesAt(layout, w, h, w - 1, track.Y + 10, false), "hidden: the edge resizes");
            Assert.AreEqual(R | B, TerminalResize.EdgesAt(layout, w, h, w - 1, h - 1, true), "the corner still resizes");
        }

        [Test]
        public void OutsideTheWindowGrabsNothing()
        {
            TerminalChromeLayout layout = Layout();
            foreach (float[] point in new[] { new[] { -1f, 100f }, new[] { 414f, 100f }, new[] { 100f, 554f }, new[] { float.NaN, 0f } })
                Assert.AreEqual(ResizeEdges.None, TerminalResize.EdgesAt(layout, 414, 554, point[0], point[1], false));
        }

        [Test]
        public void TheBandsStayInThePaddingAndTheChrome()
        {
            // So the first and last cells can still be selected, and the bands do not cover the field.
            foreach (float scale in scales)
            {
                TerminalChromeLayout layout = Layout(scale);
                Assert.LessOrEqual(layout.ResizeBand, layout.Padding, "left, scale " + scale);
                Assert.LessOrEqual(layout.ResizeBand, layout.GutterWidth, "right");
                Assert.Less(layout.ResizeBand, layout.HeaderHeight, "top: the header can still be dragged");
                Assert.Less(layout.ResizeBand, layout.StripHeight, "bottom");
                Assert.Greater(layout.ResizeCorner, layout.ResizeBand);
                Assert.Less(layout.ResizeCorner, layout.HeaderHeight + layout.Padding, "the corners stay off the field's rows");
            }
        }

        // The drag:

        [Test]
        public void TheRightAndBottomEdgesSnapToTheNearestCell()
        {
            TerminalChromeLayout layout = Layout();
            ScreenRect start = Window(layout, 50, 60, 50, 36);
            ResizeResult result = TerminalResize.Drag(layout, start, 50, 36, R, 3, 0, 1920, 1080);
            Assert.AreEqual(50, result.Columns, "3 px is under half a cell");
            result = TerminalResize.Drag(layout, start, 50, 36, R, 4, 0, 1920, 1080);
            Assert.AreEqual(51, result.Columns, "4 px is half a cell");
            result = TerminalResize.Drag(layout, start, 50, 36, R | B, -80, 70, 1920, 1080);
            Assert.AreEqual(40, result.Columns);
            Assert.AreEqual(41, result.Rows);
            Assert.AreEqual(Window(layout, 50, 60, 40, 41).ToString(), result.Window.ToString(), "position kept, size for the cells");
        }

        [Test]
        public void TheLeftAndTopEdgesKeepTheOppositeEdge()
        {
            TerminalChromeLayout layout = Layout();
            ScreenRect start = Window(layout, 300, 200, 50, 36);
            ResizeResult result = TerminalResize.Drag(layout, start, 50, 36, L | T, -16, 28, 1920, 1080);
            Assert.AreEqual(52, result.Columns);
            Assert.AreEqual(34, result.Rows);
            Assert.AreEqual(start.X + start.Width, result.Window.X + result.Window.Width, "right edge stays");
            Assert.AreEqual(start.Y + start.Height, result.Window.Y + result.Window.Height, "bottom edge stays");
            Assert.AreEqual(layout.WindowWidthFor(52), result.Window.Width);
        }

        [Test]
        public void AnAxisNotDraggedKeepsItsSizeAndCells()
        {
            TerminalChromeLayout layout = Layout();
            ScreenRect start = new ScreenRect(10, 20, 417.5f, 570); // not exactly a cell size (the fault fallback's)
            ResizeResult result = TerminalResize.Drag(layout, start, 50, 36, R, 0, 500, 1920, 1080);
            Assert.AreEqual(36, result.Rows);
            Assert.AreEqual(570, result.Window.Height);
            Assert.AreEqual(20, result.Window.Y);
            result = TerminalResize.Drag(layout, start, 50, 36, B, 500, 0, 1920, 1080);
            Assert.AreEqual(50, result.Columns);
            Assert.AreEqual(417.5f, result.Window.Width);
        }

        [Test]
        public void NeverSmallerThanTheScriptMinimum()
        {
            foreach (float scale in scales)
            {
                TerminalChromeLayout layout = Layout(scale);
                ScreenRect start = Window(layout, 300, 300, 50, 36);
                ResizeResult result = TerminalResize.Drag(layout, start, 50, 36, R | B, -5000, -5000, 1920, 1080);
                Assert.GreaterOrEqual(result.Columns, TerminalStruct.MINCOLUMNS, "scale " + scale);
                Assert.AreEqual(TerminalStruct.MINROWS, result.Rows, "scale " + scale);
                Assert.AreEqual(layout.ColumnsThatFit(result.Window.Width), result.Columns, "the cells that fit the window");
                result = TerminalResize.Drag(layout, start, 50, 36, L | T, 5000, 5000, 1920, 1080);
                Assert.AreEqual(TerminalStruct.MINROWS, result.Rows);
                Assert.AreEqual(start.Y + start.Height, result.Window.Y + result.Window.Height, "bottom edge stays at the minimum too");
            }
        }

        [Test]
        public void AWindowWidenedToTheChromeMinimumReportsTheCellsThatFit()
        {
            var layout = new TerminalChromeLayout(1f, 4, 6); // 15 cells of 4 px are narrower than the header needs
            ScreenRect start = Window(layout, 0, 0, 50, 20);
            ResizeResult result = TerminalResize.Drag(layout, start, 50, 20, R, -1000, 0, 1920, 1080);
            Assert.AreEqual(layout.MinimumWidth, result.Window.Width);
            Assert.AreEqual(layout.ColumnsThatFit(layout.MinimumWidth), result.Columns);
            Assert.Greater(result.Columns, TerminalStruct.MINCOLUMNS);
        }

        [Test]
        public void NotPastTheScreenOrTheMaximum()
        {
            TerminalChromeLayout layout = Layout();
            ScreenRect start = Window(layout, 500, 300, 50, 20);
            ResizeResult result = TerminalResize.Drag(layout, start, 50, 20, R | B, 5000, 5000, 1024, 768);
            Assert.LessOrEqual(result.Window.X + result.Window.Width, 1024);
            Assert.LessOrEqual(result.Window.Y + result.Window.Height, 768);
            Assert.AreEqual(layout.ColumnsThatFit(1024 - 500), result.Columns, "as many as fit");
            result = TerminalResize.Drag(layout, start, 50, 20, L | T, -5000, -5000, 1024, 768);
            Assert.GreaterOrEqual(result.Window.X, 0);
            Assert.GreaterOrEqual(result.Window.Y, 0);

            result = TerminalResize.Drag(layout, Window(layout, 0, 0, 50, 20), 50, 20, R | B, 1e6f, 1e6f, 0, 0);
            Assert.AreEqual(TerminalStruct.MAXCOLUMNS, result.Columns, "no screen known: the script maximum");
            Assert.AreEqual(TerminalStruct.MAXROWS, result.Rows);
        }

        [Test]
        public void AWindowLargerThanTheScreenCanShrinkButNotGrow()
        {
            TerminalChromeLayout layout = Layout();
            ScreenRect start = Window(layout, 0, 0, 150, 60); // 1214 x 890 on a 1024 x 768 screen
            ResizeResult result = TerminalResize.Drag(layout, start, 150, 60, R | B, 2, 2, 1024, 768);
            Assert.AreEqual(150, result.Columns, "kept, not cut to the screen");
            Assert.AreEqual(60, result.Rows);
            result = TerminalResize.Drag(layout, start, 150, 60, R | B, 40, 40, 1024, 768);
            Assert.AreEqual(150, result.Columns, "not grown");
            result = TerminalResize.Drag(layout, start, 150, 60, R | B, -80, -140, 1024, 768);
            Assert.AreEqual(140, result.Columns);
            Assert.AreEqual(50, result.Rows);
        }

        [Test]
        public void EveryDragGivesAWindowItsCellsFit()
        {
            var random = new Random(20261020); // a fixed seed: reproducible
            foreach (float scale in scales)
            {
                foreach (int[] cell in new[] { new[] { 4, 6 }, new[] { 8, 14 }, new[] { 26, 48 } })
                {
                    var layout = new TerminalChromeLayout(scale, cell[0], cell[1]);
                    for (int i = 0; i < 200; ++i)
                    {
                        int columns = random.Next(TerminalStruct.MINCOLUMNS, 120), rows = random.Next(TerminalStruct.MINROWS, 60);
                        // As the game has them: the cells a window of that size holds (more when widened to the minimum width).
                        columns = Math.Min(TerminalStruct.MAXCOLUMNS, layout.ColumnsThatFit(layout.WindowWidthFor(columns)));
                        ScreenRect start = Window(layout, random.Next(0, 800), random.Next(0, 600), columns, rows);
                        var edges = (ResizeEdges)random.Next(1, 16);
                        if ((edges & L) != 0 && (edges & R) != 0) edges &= ~R;
                        if ((edges & T) != 0 && (edges & B) != 0) edges &= ~B;
                        ResizeResult result = TerminalResize.Drag(layout, start, columns, rows, edges,
                            random.Next(-900, 900) + 0.5f, random.Next(-900, 900) + 0.25f, 2560, 1440);
                        string what = string.Format("scale {0}, cell {1}x{2}, {3} from {4}: {5}", scale, cell[0], cell[1], edges, start, result.Window);
                        Assert.AreEqual(result.Columns, Math.Min(TerminalStruct.MAXCOLUMNS, layout.ColumnsThatFit(result.Window.Width)), what);
                        Assert.AreEqual(result.Rows, layout.RowsThatFit(result.Window.Height), what);
                        Assert.GreaterOrEqual(result.Columns, TerminalStruct.MINCOLUMNS, what);
                        Assert.GreaterOrEqual(result.Rows, TerminalStruct.MINROWS, what);
                        if ((edges & L) != 0)
                            Assert.AreEqual(start.X + start.Width, result.Window.X + result.Window.Width, 0.001, what);
                        else
                            Assert.AreEqual(start.X, result.Window.X, what);
                    }
                }
            }
        }
    }

    /// <summary>Phase 4, step 2: the scroll bar's thumb and the view offset it stands for.</summary>
    [TestFixture]
    public class TerminalScrollBarTest
    {
        private static readonly ScreenRect track = new ScreenRect(407, 26, 6, 504);

        [Test]
        public void ShownOnlyWhileScrolledBackOrDragged()
        {
            Assert.IsFalse(TerminalScrollBar.IsShown(0, false));
            Assert.IsTrue(TerminalScrollBar.IsShown(1, false));
            Assert.IsTrue(TerminalScrollBar.IsShown(0, true), "a drag back to the bottom keeps it until released");
        }

        [Test]
        public void TheThumbStandsForTheRowsInView()
        {
            ScreenRect bottom = TerminalScrollBar.Thumb(track, 108, 36, 0, 16);
            Assert.AreEqual(504 * 36 / 144f, bottom.Height, 0.001, "a quarter of the lines are in view");
            Assert.AreEqual(track.Y + track.Height, bottom.Y + bottom.Height, 0.001, "the screen: at the bottom");
            Assert.AreEqual(track.X, bottom.X);
            Assert.AreEqual(track.Width, bottom.Width);
            ScreenRect top = TerminalScrollBar.Thumb(track, 108, 36, 108, 16);
            Assert.AreEqual(track.Y, top.Y, 0.001, "the oldest line: at the top");
            ScreenRect middle = TerminalScrollBar.Thumb(track, 108, 36, 54, 16);
            Assert.AreEqual((top.Y + bottom.Y) / 2, middle.Y, 0.001);
        }

        [Test]
        public void TheThumbHasAMinimumLengthWithinTheTrack()
        {
            Assert.AreEqual(16, TerminalScrollBar.Thumb(track, 2000, 36, 0, 16).Height, 0.001, "a long scrollback");
            var shortTrack = new ScreenRect(0, 0, 6, 10);
            ScreenRect thumb = TerminalScrollBar.Thumb(shortTrack, 2000, 3, 1000, 16);
            Assert.AreEqual(10, thumb.Height, "never longer than the track");
            Assert.AreEqual(0, thumb.Y);
            Assert.AreEqual(1000, TerminalScrollBar.ViewOffsetFor(shortTrack, 2000, 3, 16, 5, 1000), "a thumb that cannot move changes nothing");
        }

        [Test]
        public void TheThumbAndTheViewOffsetAreEachOthersInverse()
        {
            foreach (int scrollback in new[] { 1, 7, 108, 2000 })
                foreach (int rows in new[] { 3, 36, 160 })
                    for (int offset = 0; offset <= scrollback; offset += Math.Max(1, scrollback / 50))
                    {
                        ScreenRect thumb = TerminalScrollBar.Thumb(track, scrollback, rows, offset, 16);
                        Assert.AreEqual(offset, TerminalScrollBar.ViewOffsetFor(track, scrollback, rows, 16, thumb.Y, -1),
                            scrollback + " lines, " + rows + " rows");
                    }
        }

        [Test]
        public void AThumbDraggedPastTheEndsStopsThere()
        {
            Assert.AreEqual(108, TerminalScrollBar.ViewOffsetFor(track, 108, 36, 16, -500, 3));
            Assert.AreEqual(0, TerminalScrollBar.ViewOffsetFor(track, 108, 36, 16, 5000, 3));
            Assert.AreEqual(0, TerminalScrollBar.ViewOffsetFor(track, 0, 36, 16, 26, 3), "no scrollback");
            Assert.AreEqual(3, TerminalScrollBar.ViewOffsetFor(track, 108, 36, 16, float.NaN, 3));
            Assert.AreEqual(0, TerminalScrollBar.Thumb(track, 108, 36, -4, 16).Y + TerminalScrollBar.Thumb(track, 108, 36, -4, 16).Height
                - (track.Y + track.Height), 0.001, "an offset below the screen draws at the bottom");
        }

        [Test]
        public void APressBesideTheThumbSaysWhichSide()
        {
            ScreenRect thumb = TerminalScrollBar.Thumb(track, 108, 36, 54, 16);
            Assert.AreEqual(-1, TerminalScrollBar.SideOfThumb(thumb, thumb.Y - 1));
            Assert.AreEqual(0, TerminalScrollBar.SideOfThumb(thumb, thumb.Y));
            Assert.AreEqual(0, TerminalScrollBar.SideOfThumb(thumb, thumb.Y + thumb.Height - 0.5f));
            Assert.AreEqual(1, TerminalScrollBar.SideOfThumb(thumb, thumb.Y + thumb.Height));
        }

        [Test]
        public void TheTrackIsInTheGutterBesideTheRows()
        {
            foreach (float scale in new[] { 0.5f, 1f, 1.3f, 2f, 3f })
            {
                var layout = new TerminalChromeLayout(scale, 8, 14);
                float width = layout.WindowWidthFor(50), height = layout.WindowHeightFor(36);
                ScreenRect gutter = layout.Gutter(width, height), trackRect = layout.ScrollTrack(width, height);
                ScreenRect field = layout.Field(50, 36);
                string what = "scale " + scale;
                Assert.AreEqual(layout.ScrollBarWidth, trackRect.Width, what);
                Assert.GreaterOrEqual(trackRect.X, gutter.X + 1, what + ": a pixel's gap on the left");
                Assert.LessOrEqual(trackRect.X + trackRect.Width, gutter.X + gutter.Width - 1, what + ": and on the right");
                Assert.GreaterOrEqual(trackRect.X, field.X + field.Width, what + ": right of the characters");
                Assert.AreEqual(field.Y, trackRect.Y, what + ": level with the first row");
                Assert.AreEqual(field.Y + field.Height, trackRect.Y + trackRect.Height, what + ": and the last");
            }
        }
    }
}
