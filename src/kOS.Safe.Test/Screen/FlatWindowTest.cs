using System.Collections.Generic;
using kOS.Safe.Screen;
using kOS.Safe.UserIO;
using NUnit.Framework;

namespace kOS.Safe.Test.Terminal
{
    /// <summary>Phase 4, step 5: the smaller flat windows' layout.</summary>
    [TestFixture]
    public class FlatWindowLayoutTest
    {
        private static readonly float[] scales = { 0.5f, 0.8f, 1f, 1.3f, 1.5f, 2f, 3f };

        internal static bool Overlap(ScreenRect a, ScreenRect b)
        {
            return a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
        }

        internal static void AssertWithin(ScreenRect inner, ScreenRect outer, string what)
        {
            Assert.GreaterOrEqual(inner.X, outer.X - 1e-3, what);
            Assert.GreaterOrEqual(inner.Y, outer.Y - 1e-3, what);
            Assert.LessOrEqual(inner.X + inner.Width, outer.X + outer.Width + 1e-3, what);
            Assert.LessOrEqual(inner.Y + inner.Height, outer.Y + outer.Height + 1e-3, what);
        }

        [Test]
        public void ThePartsTileTheWindow()
        {
            foreach (float scale in scales)
                foreach (FlatWindowBottom bottom in new[] { FlatWindowBottom.None, FlatWindowBottom.Buttons, FlatWindowBottom.Strip })
                    foreach (float bodyHeight in new[] { 0f, 40f, 300f })
                    {
                        var layout = new FlatWindowLayout(scale);
                        string what = string.Format("scale {0}, {1}, body {2}", scale, bottom, bodyHeight);
                        float width = layout.DialogWidth, height = layout.WindowHeight(bodyHeight, bottom);
                        var window = new ScreenRect(0, 0, width, height);
                        ScreenRect header = layout.Header(width), body = layout.Body(width, height, bottom);
                        AssertWithin(header, window, what);
                        AssertWithin(body, window, what);
                        Assert.AreEqual(bodyHeight, body.Height, 1e-3, what);
                        Assert.IsFalse(Overlap(header, body), what);
                        AssertWithin(layout.CloseButton(width), header, what);
                        if (bottom == FlatWindowBottom.Strip)
                        {
                            ScreenRect strip = layout.Strip(width, height);
                            AssertWithin(strip, window, what);
                            Assert.IsFalse(Overlap(strip, body), what);
                            Assert.AreEqual(height, strip.Y + strip.Height, 1e-3, what);
                            AssertWithin(layout.Grip(width, height), strip, what);
                        }
                        if (bottom == FlatWindowBottom.Buttons)
                            foreach (ScreenRect button in layout.Buttons(width, height, new[] { 80f, 60f }))
                            {
                                AssertWithin(button, window, what);
                                Assert.IsFalse(Overlap(button, body), what);
                                Assert.AreEqual(layout.RowHeight, button.Height, what);
                            }
                    }
        }

        [Test]
        public void MetricsFollowTheTerminalsChromeAndTheScale()
        {
            var one = new FlatWindowLayout(1f);
            var terminal = new TerminalChromeLayout(1f, 8, 16);
            Assert.AreEqual(terminal.HeaderHeight, one.HeaderHeight);
            Assert.AreEqual(terminal.StripHeight, one.StripHeight);
            Assert.AreEqual(terminal.Padding, one.Padding);
            Assert.AreEqual(terminal.FontSize, one.FontSize);
            Assert.AreEqual(FlatWindowLayout.BASE_DIALOG_WIDTH, one.DialogWidth);
            var large = new FlatWindowLayout(2f);
            Assert.AreEqual(2 * one.RowHeight, large.RowHeight);
            Assert.AreEqual(2 * one.EditorMinimumWidth, large.EditorMinimumWidth);
            Assert.AreEqual(new FlatWindowLayout(3f).HeaderHeight, new FlatWindowLayout(9f).HeaderHeight, "clamped");
            Assert.AreEqual(one.HeaderHeight, new FlatWindowLayout(float.NaN).HeaderHeight);
        }

        [Test]
        public void ButtonsAreRightAlignedInTheirOrder()
        {
            var layout = new FlatWindowLayout(1f);
            float width = 400, height = layout.WindowHeight(50, FlatWindowBottom.Buttons);
            List<ScreenRect> buttons = layout.Buttons(width, height, new[] { 100f, 70f, 60f });
            Assert.AreEqual(3, buttons.Count);
            Assert.AreEqual(width - layout.Padding, buttons[2].X + buttons[2].Width, 1e-3, "the last ends at the padding");
            Assert.AreEqual(100, buttons[0].Width, 1e-3);
            for (int i = 1; i < buttons.Count; ++i)
                Assert.AreEqual(buttons[i - 1].X + buttons[i - 1].Width + layout.Gap, buttons[i].X, 1e-3, "in order, a gap apart");
        }

        [Test]
        public void ButtonsThatDoNotFitShrinkAlike()
        {
            var layout = new FlatWindowLayout(1f);
            float width = 200, height = layout.WindowHeight(50, FlatWindowBottom.Buttons);
            List<ScreenRect> buttons = layout.Buttons(width, height, new[] { 200f, 100f });
            Assert.GreaterOrEqual(buttons[0].X, layout.Padding - 1e-3);
            Assert.AreEqual(2.0, buttons[0].Width / buttons[1].Width, 0.05);
            Assert.IsFalse(Overlap(buttons[0], buttons[1]));
            Assert.AreEqual(0, layout.Buttons(width, height, new float[0]).Count);
        }

        [Test]
        public void HeaderButtonsSitLeftOfCloseAndLeaveTheTitleHalf()
        {
            foreach (float scale in scales)
            {
                var layout = new FlatWindowLayout(scale);
                foreach (float width in new[] { 120f, 300f, 800f })
                {
                    string what = "scale " + scale + ", width " + width;
                    ScreenRect close = layout.CloseButton(width);
                    Assert.AreEqual(width, close.X + close.Width, 1e-3, what);
                    List<ScreenRect> buttons = layout.HeaderButtons(width, new[] { 60f * scale, 50f * scale });
                    ScreenRect header = layout.Header(width);
                    foreach (ScreenRect button in buttons)
                    {
                        AssertWithin(button, header, what);
                        Assert.IsFalse(Overlap(button, close), what);
                    }
                    Assert.GreaterOrEqual(buttons[0].X, close.X / 2f - 1e-3, what);
                    ScreenRect title = layout.Title(buttons[0].X);
                    Assert.LessOrEqual(title.X + title.Width, buttons[0].X, what);
                    Assert.GreaterOrEqual(title.Width, 0, what);
                }
            }
        }

        [Test]
        public void ButtonWidthFitsTheLabel()
        {
            var layout = new FlatWindowLayout(1f);
            Assert.AreEqual(layout.MinimumButtonWidth, layout.ButtonWidth(5));
            Assert.Greater(layout.ButtonWidth(120), 120);
            Assert.AreEqual(layout.MinimumButtonWidth, layout.ButtonWidth(-10));
        }

        [Test]
        public void TheScrollTrackRunsAlongTheViewsRightEdge()
        {
            var layout = new FlatWindowLayout(1.3f);
            var view = new ScreenRect(10, 30, 200, 100);
            ScreenRect track = layout.ScrollTrack(view);
            Assert.AreEqual(view.X + view.Width, track.X + track.Width, 1e-3);
            Assert.AreEqual(layout.ScrollBarWidth, track.Width);
            Assert.AreEqual(view.Height, track.Height);
            ScreenRect thumb = layout.ScrollThumb(track, 1000, 100, 900);
            Assert.AreEqual(track.Y + track.Height, thumb.Y + thumb.Height, 1e-3, "at the end");
            Assert.GreaterOrEqual(thumb.Height, layout.MinimumThumb);
            Assert.AreEqual(900, layout.ScrollFor(thumb.Y, track, 1000, 100), 1e-3);
        }
    }

    [TestFixture]
    public class WindowPlacementNearTest
    {
        private const float ScreenWidth = 1024, ScreenHeight = 768;

        private static void AssertOnScreen(ScreenRect rect, string what)
        {
            FlatWindowLayoutTest.AssertWithin(rect, new ScreenRect(0, 0, ScreenWidth, ScreenHeight), what);
        }

        [Test]
        public void TheEditorGoesBelowItsTerminalWhenThereIsRoom()
        {
            var terminal = new ScreenRect(50, 60, 420, 300);
            ScreenRect editor = WindowPlacement.BelowOrBeside(terminal, 420, 300, ScreenWidth, ScreenHeight);
            Assert.AreEqual(50, editor.X);
            Assert.AreEqual(360, editor.Y);
            Assert.AreEqual(420, editor.Width);
            Assert.AreEqual(300, editor.Height);
        }

        [Test]
        public void ThenAboveThenRightThenLeft()
        {
            // The dev instance's 50x36 terminal: no room below or above, room on the right.
            var terminal = new ScreenRect(50, 60, 420, 482);
            ScreenRect right = WindowPlacement.BelowOrBeside(terminal, 420, 482, ScreenWidth, ScreenHeight);
            Assert.AreEqual(470, right.X);
            Assert.AreEqual(60, right.Y);

            var low = new ScreenRect(50, 500, 420, 200);
            ScreenRect above = WindowPlacement.BelowOrBeside(low, 420, 300, ScreenWidth, ScreenHeight);
            Assert.AreEqual(200, above.Y);
            Assert.AreEqual(50, above.X);

            var rightSide = new ScreenRect(560, 60, 420, 482);
            ScreenRect left = WindowPlacement.BelowOrBeside(rightSide, 420, 482, ScreenWidth, ScreenHeight);
            Assert.AreEqual(140, left.X);
            Assert.AreEqual(60, left.Y);
        }

        [Test]
        public void WithNoRoomAnywhereItGoesOverTheAnchorOnScreen()
        {
            var terminal = new ScreenRect(200, 100, 700, 600);
            ScreenRect editor = WindowPlacement.BelowOrBeside(terminal, 600, 400, ScreenWidth, ScreenHeight);
            AssertOnScreen(editor, "over");
            Assert.AreEqual(200, editor.X, "below, moved up onto the screen");
            Assert.AreEqual(ScreenHeight - 400, editor.Y);
        }

        [Test]
        public void TheOtherAxisIsMovedOnScreen()
        {
            var terminal = new ScreenRect(800, 60, 200, 300);
            ScreenRect editor = WindowPlacement.BelowOrBeside(terminal, 420, 300, ScreenWidth, ScreenHeight);
            Assert.AreEqual(360, editor.Y, "still below");
            Assert.AreEqual(ScreenWidth - 420, editor.X, "moved left onto the screen");
        }

        [Test]
        public void APickerGoesBesideThePanel()
        {
            // The toolbar panel at the launcher, bottom right:
            var panel = new ScreenRect(640, 300, 340, 420);
            ScreenRect picker = WindowPlacement.Beside(panel, 300, 380, ScreenWidth, ScreenHeight);
            Assert.AreEqual(340, picker.X, "left of it: no room on the right");
            Assert.AreEqual(300, picker.Y);
            AssertOnScreen(picker, "left");

            var leftPanel = new ScreenRect(0, 40, 340, 420);
            Assert.AreEqual(340, WindowPlacement.Beside(leftPanel, 300, 380, ScreenWidth, ScreenHeight).X, "right of it");
            Assert.AreEqual(340, WindowPlacement.Beside(new ScreenRect(0, 600, 340, 160), 300, 380, ScreenWidth, ScreenHeight).X);
            Assert.AreEqual(ScreenHeight - 380, WindowPlacement.Beside(new ScreenRect(0, 600, 340, 160), 300, 380, ScreenWidth, ScreenHeight).Y,
                "moved up onto the screen");
        }

        [Test]
        public void ADialogIsCenteredOverItsWindowOnScreen()
        {
            var editor = new ScreenRect(100, 100, 400, 300);
            ScreenRect dialog = WindowPlacement.CenteredOver(editor, 200, 100, ScreenWidth, ScreenHeight);
            Assert.AreEqual(200, dialog.X);
            Assert.AreEqual(200, dialog.Y);
            ScreenRect edge = WindowPlacement.CenteredOver(new ScreenRect(-100, 700, 100, 100), 300, 120, ScreenWidth, ScreenHeight);
            AssertOnScreen(edge, "kept on screen");
        }

        [Test]
        public void AnUnknownScreenLeavesTheFirstSide()
        {
            var terminal = new ScreenRect(50, 60, 420, 300);
            ScreenRect editor = WindowPlacement.BelowOrBeside(terminal, 420, 300, 0, 0);
            Assert.AreEqual(360, editor.Y);
            Assert.AreEqual(470, WindowPlacement.Beside(terminal, 100, 100, 0, 0).X);
        }

        [Test]
        public void AWindowLargerThanTheScreenKeepsItsHeaderOnScreen()
        {
            ScreenRect huge = WindowPlacement.BelowOrBeside(new ScreenRect(50, 60, 300, 200), 2000, 1000, ScreenWidth, ScreenHeight);
            Assert.AreEqual(0, huge.X);
            Assert.AreEqual(0, huge.Y);
            Assert.AreEqual(2000, huge.Width, "its size kept");
        }
    }

    [TestFixture]
    public class WindowResizeTest
    {
        private readonly FlatWindowLayout layout = new FlatWindowLayout(1f);
        private const float Width = 400, Height = 300;
        private static readonly ScreenRect noButtons = new ScreenRect(0, 0, 0, 0);

        [Test]
        public void EdgesAndCornersAreGrabbed()
        {
            int band = layout.ResizeBand;
            Assert.AreEqual(ResizeEdges.Left, WindowResize.EdgesAt(layout, Width, Height, 1, 150, noButtons));
            Assert.AreEqual(ResizeEdges.Right, WindowResize.EdgesAt(layout, Width, Height, Width - 1, 150, noButtons));
            Assert.AreEqual(ResizeEdges.Top, WindowResize.EdgesAt(layout, Width, Height, 200, 1, noButtons));
            Assert.AreEqual(ResizeEdges.Bottom, WindowResize.EdgesAt(layout, Width, Height, 200, Height - 1, noButtons));
            Assert.AreEqual(ResizeEdges.Left | ResizeEdges.Top, WindowResize.EdgesAt(layout, Width, Height, 1, layout.ResizeCorner - 1, noButtons));
            Assert.AreEqual(ResizeEdges.None, WindowResize.EdgesAt(layout, Width, Height, band + 1, 150, noButtons));
            Assert.AreEqual(ResizeEdges.None, WindowResize.EdgesAt(layout, Width, Height, -1, 150, noButtons));
        }

        [Test]
        public void TheGripIsTheBottomRightCorner()
        {
            ScreenRect grip = layout.Grip(Width, Height);
            Assert.AreEqual(ResizeEdges.Right | ResizeEdges.Bottom,
                WindowResize.EdgesAt(layout, Width, Height, grip.X + 1, grip.Y + 1, noButtons));
        }

        [Test]
        public void TheHeaderButtonsKeepTheirPresses()
        {
            ScreenRect close = layout.CloseButton(Width);
            Assert.AreEqual(ResizeEdges.None, WindowResize.EdgesAt(layout, Width, Height, Width - 1, 1, close));
            Assert.AreEqual(ResizeEdges.Right | ResizeEdges.Top, WindowResize.EdgesAt(layout, Width, Height, Width - 1, 1, noButtons));
        }

        [Test]
        public void DraggingTheRightAndBottomEdgesGrows()
        {
            var start = new ScreenRect(100, 100, Width, Height);
            ScreenRect grown = WindowResize.Drag(start, ResizeEdges.Right | ResizeEdges.Bottom, 50.4f, 30.6f, 200, 100, 1024, 768);
            Assert.AreEqual(100, grown.X);
            Assert.AreEqual(100, grown.Y);
            Assert.AreEqual(450, grown.Width);
            Assert.AreEqual(331, grown.Height);
        }

        [Test]
        public void TheLeftAndTopEdgesKeepTheOppositeEdge()
        {
            var start = new ScreenRect(100, 100, Width, Height);
            ScreenRect moved = WindowResize.Drag(start, ResizeEdges.Left | ResizeEdges.Top, -40, 20, 200, 100, 1024, 768);
            Assert.AreEqual(start.X + start.Width, moved.X + moved.Width, 1e-3);
            Assert.AreEqual(start.Y + start.Height, moved.Y + moved.Height, 1e-3);
            Assert.AreEqual(440, moved.Width);
            Assert.AreEqual(280, moved.Height);
        }

        [Test]
        public void SizesStayBetweenTheMinimumAndTheScreen()
        {
            var start = new ScreenRect(100, 100, Width, Height);
            ScreenRect small = WindowResize.Drag(start, ResizeEdges.Right | ResizeEdges.Bottom, -1000, -1000, 200, 100, 1024, 768);
            Assert.AreEqual(200, small.Width);
            Assert.AreEqual(100, small.Height);
            ScreenRect large = WindowResize.Drag(start, ResizeEdges.Right | ResizeEdges.Bottom, 5000, 5000, 200, 100, 1024, 768);
            Assert.AreEqual(1024 - 100, large.Width);
            Assert.AreEqual(768 - 100, large.Height);
            ScreenRect left = WindowResize.Drag(start, ResizeEdges.Left, -5000, 0, 200, 100, 1024, 768);
            Assert.AreEqual(0, left.X, "to the screen's left edge");
            ScreenRect unknown = WindowResize.Drag(start, ResizeEdges.Right, 5000, 0, 200, 100, 0, 0);
            Assert.AreEqual(Width + 5000, unknown.Width, "no limit when the screen is not known");
        }

        [Test]
        public void AnAxisNotDraggedIsKept()
        {
            var start = new ScreenRect(100, 100, Width, Height);
            ScreenRect wider = WindowResize.Drag(start, ResizeEdges.Right, 20, 500, 200, 100, 1024, 768);
            Assert.AreEqual(Height, wider.Height);
            Assert.AreEqual(100, wider.Y);
            ScreenRect bad = WindowResize.Drag(start, ResizeEdges.Right, float.NaN, 0, 200, 100, 1024, 768);
            Assert.AreEqual(Width, bad.Width);
        }
    }

    /// <summary>EDIT-03: the editor's view follows the caret only when it moves or the text changes.</summary>
    [TestFixture]
    public class EditorScrollFollowTest
    {
        private const float Line = 16, View = 160, Content = 1600;

        [Test]
        public void ANewFileShowsTheCaret()
        {
            var follow = new EditorScrollFollow();
            Assert.AreEqual(0, follow.Follow(0, 0, "a\nb", 0, Line, Content, View));
            follow.Reset();
            Assert.AreEqual(400 + Line - View, follow.Follow(0, 5, "x", 400, Line, Content, View), 1e-3);
        }

        [Test]
        public void TheWheelAndScrollBarAreNotPulledBack()
        {
            var follow = new EditorScrollFollow();
            string text = "some text";
            follow.Follow(0, 3, text, 0, Line, Content, View);
            Assert.AreEqual(800, follow.Follow(800, 3, text, 0, Line, Content, View), "looking far from the caret");
            Assert.AreEqual(900, follow.Follow(900, 3, text, 0, Line, Content, View), "and further");
        }

        [Test]
        public void TypingOrMovingTheCaretBringsItBack()
        {
            var follow = new EditorScrollFollow();
            follow.Follow(0, 3, "abc", 48, Line, Content, View);
            follow.Follow(800, 3, "abc", 48, Line, Content, View);
            Assert.AreEqual(48, follow.Follow(800, 4, "abc", 48, Line, Content, View), "an arrow key");
            follow.Follow(800, 4, "abc", 48, Line, Content, View);
            Assert.AreEqual(48, follow.Follow(800, 4, "abcd", 48, Line, Content, View), "a replaced character, the caret in place");
        }

        [Test]
        public void TheViewMovesAsLittleAsPossible()
        {
            var follow = new EditorScrollFollow();
            follow.Follow(100, 1, "t", 120, Line, Content, View);
            Assert.AreEqual(100, follow.Follow(100, 2, "t", 150, Line, Content, View), "already in view");
            Assert.AreEqual(300 + Line - View, follow.Follow(100, 3, "t", 300, Line, Content, View), 1e-3, "below: at the bottom");
            Assert.AreEqual(20, follow.Follow(400, 4, "t", 20, Line, Content, View), "above: at the top");
        }

        [Test]
        public void AResizeClampsWithoutResetting()
        {
            var follow = new EditorScrollFollow();
            follow.Follow(0, 1, "t", 0, Line, Content, View);
            Assert.AreEqual(700, follow.Follow(700, 1, "t", 0, Line, Content, 300), "taller view, scroll kept");
            Assert.AreEqual(Content - 900, follow.Follow(1500, 1, "t", 0, Line, Content, 900), "clamped to the end");
            Assert.AreEqual(0, follow.Follow(50, 1, "t", 0, Line, 100, 900), "all of it fits");
        }
    }

    [TestFixture]
    public class TextPositionTest
    {
        [Test]
        public void LinesAndColumnsCountFromOne()
        {
            const string text = "print 1.\n\tif x {\n}\n";
            Assert.AreEqual("Ln 1, Col 1", TextPosition.Of(text, 0).ToString());
            Assert.AreEqual("Ln 1, Col 9", TextPosition.Of(text, 8).ToString(), "before the newline");
            Assert.AreEqual("Ln 2, Col 1", TextPosition.Of(text, 9).ToString());
            Assert.AreEqual("Ln 2, Col 3", TextPosition.Of(text, 11).ToString(), "a tab is one column");
            Assert.AreEqual("Ln 4, Col 1", TextPosition.Of(text, text.Length).ToString(), "after the last newline");
        }

        [Test]
        public void OutOfRangeIndexesAreClamped()
        {
            Assert.AreEqual("Ln 1, Col 1", TextPosition.Of("abc", -5).ToString());
            Assert.AreEqual("Ln 1, Col 4", TextPosition.Of("abc", 99).ToString());
            Assert.AreEqual("Ln 1, Col 1", TextPosition.Of(null, 3).ToString());
        }

        [Test]
        public void RevealShowsATallSpansTop()
        {
            Assert.AreEqual(500, PixelScroll.Reveal(0, 500, 400, 2000, 100));
            Assert.AreEqual(0, PixelScroll.Reveal(300, -10, 16, 2000, 100), "clamped");
        }
    }
}
