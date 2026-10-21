using System;
using System.Collections.Generic;
using kOS.Safe.Screen;
using kOS.Safe.UserIO;
using NUnit.Framework;

namespace kOS.Safe.Test.Terminal
{
    [TestFixture]
    public class TerminalOpacityTest
    {
        [Test]
        public void DefaultsAndRestoringOlderGeometryAreOpaque()
        {
            var opacity = new TerminalOpacity();
            Assert.AreEqual(100, opacity.Percent);
            Assert.AreEqual("100%", opacity.Readout);
            opacity.Percent = 25;
            opacity.Restore(new TerminalGeometry());
            Assert.AreEqual(100, opacity.Percent, "missing opacity restores 100%, not the current opacity");
            opacity.Percent = 0;
            opacity.Restore(null);
            Assert.AreEqual(100, opacity.Percent);
            opacity.Restore(new TerminalGeometry { Opacity = 42 });
            Assert.AreEqual(42, opacity.Percent);
        }

        [Test]
        public void PercentIsClampedAndTheReadoutIsKeptUntilItChanges()
        {
            var opacity = new TerminalOpacity { Percent = -1 };
            Assert.AreEqual(0, opacity.Percent);
            Assert.AreEqual("0%", opacity.Readout);
            string readout = opacity.Readout;
            opacity.Percent = int.MinValue;
            Assert.AreSame(readout, opacity.Readout, "same clamped value");
            opacity.Percent = 75;
            Assert.AreEqual("75%", opacity.Readout);
            readout = opacity.Readout;
            opacity.Percent = 75;
            Assert.AreSame(readout, opacity.Readout);
            opacity.Percent = int.MaxValue;
            Assert.AreEqual(100, opacity.Percent);
            Assert.AreEqual("100%", opacity.Readout);
        }

        [Test]
        public void WindowsDoNotShareOpacityOrChangeThePalette()
        {
            var first = new TerminalOpacity { Percent = 0 };
            var second = new TerminalOpacity { Percent = 75 };
            PaletteColor original = TerminalPalette.Chrome;
            Assert.AreEqual(0f, first.Background(original).A);
            Assert.AreEqual(0.75f, second.Background(original).A);
            Assert.AreEqual(1f, TerminalPalette.Chrome.A);
            Assert.AreEqual(original, TerminalPalette.Chrome);
            first.Percent = 20;
            Assert.AreEqual(75, second.Percent);
        }

        [TestCase(0)]
        [TestCase(37)]
        [TestCase(100)]
        public void BackgroundOpacityPreservesRgbAndMultipliesExistingAlpha(int percent)
        {
            var opacity = new TerminalOpacity { Percent = percent };
            var original = new PaletteColor(12, 34, 56, 0.4f);
            PaletteColor faded = opacity.Background(original);
            Assert.AreEqual(original.R, faded.R);
            Assert.AreEqual(original.G, faded.G);
            Assert.AreEqual(original.B, faded.B);
            Assert.AreEqual(0.4f * percent / 100f, faded.A);
        }

        [TestCase(0)]
        [TestCase(25)]
        [TestCase(100)]
        public void ReverseAndVisualBeepAreComposedBeforeApplyingOpacity(int percent)
        {
            var opacity = new TerminalOpacity { Percent = percent };
            Assert.AreEqual(TerminalPalette.Background.WithAlpha(percent / 100f), opacity.Body(false, 0.7));
            foreach (double brightness in new[] { 0, 0.7, 1 })
            {
                PaletteColor old = TerminalPalette.Text.WithAlpha((float)brightness).Over(TerminalPalette.Background);
                Assert.AreEqual(old.WithAlpha(percent / 100f), opacity.Body(true, brightness), "brightness " + brightness);
                Assert.AreEqual(percent / 100f, opacity.Body(true, brightness).A, "one background, not stacked alphas");
            }
            Assert.AreEqual(opacity.Body(true, 0), opacity.Body(true, -1));
            Assert.AreEqual(opacity.Body(true, 1), opacity.Body(true, 2));
            Assert.AreEqual(opacity.Body(true, 0), opacity.Body(true, double.NaN));
        }

        [Test]
        public void StripAndChipsTileExactlyOnceWithoutStackingTheirBackgrounds()
        {
            var strip = new ScreenRect(10, 30, 280, 18);
            var parts = new List<StatusStrip.Placement>
            {
                new StatusStrip.Placement { Part = StatusStrip.Part.State, X = 16, Width = 40 },
                new StatusStrip.Placement { Part = StatusStrip.Part.Chip, X = 60, Width = 40 },
                new StatusStrip.Placement { Part = StatusStrip.Part.Chip, X = 106, Width = 68 },
                new StatusStrip.Placement { Part = StatusStrip.Part.Size, X = 210, Width = 40 }
            };
            var rectangles = new List<ScreenRect>();
            TerminalOpacity.StripBackground(strip, parts, 3, rectangles);
            Assert.AreEqual(7, rectangles.Count);
            for (float y = strip.Y + 0.5f; y < strip.Y + strip.Height; ++y)
                for (float x = strip.X + 0.5f; x < strip.X + strip.Width; ++x)
                {
                    int painted = 0;
                    foreach (ScreenRect rectangle in rectangles)
                        if (rectangle.Contains(x, y)) ++painted;
                    foreach (StatusStrip.Placement part in parts)
                        if (part.Part == StatusStrip.Part.Chip && new ScreenRect(part.X, strip.Y + 3, part.Width, strip.Height - 6).Contains(x, y))
                            ++painted;
                    Assert.AreEqual(1, painted, "at " + x + "," + y);
                }
            parts.Clear();
            TerminalOpacity.StripBackground(strip, parts, 3, rectangles);
            Assert.AreEqual(1, rectangles.Count, "old rectangles cleared");
            Assert.AreEqual(strip, rectangles[0], "no chips: a single fill");
        }

        [Test]
        public void UnchangedRenderingAndMenuLayoutAllocateNothingAfterWarmup()
        {
            var opacity = new TerminalOpacity { Percent = 75 };
            var menu = new TerminalOpacityMenu();
            menu.Open(10, 20, 1.3f, 1920, 1080);
            var strip = new ScreenRect(0, 100, 400, 18);
            var parts = new List<StatusStrip.Placement> { new StatusStrip.Placement { Part = StatusStrip.Part.Chip, X = 100, Width = 80 } };
            var rectangles = new List<ScreenRect>();
            string readout = null;
            Assert.AreEqual(0, AllocationCounter.BytesAllocated(() =>
            {
                readout = opacity.Readout;
                opacity.Body(true, 0.7);
                opacity.Background(TerminalPalette.Chrome);
                TerminalOpacity.StripBackground(strip, parts, 3, rectangles);
                menu.Refresh(1.3f, 1920, 1080, true);
            }));
            Assert.AreEqual("75%", readout);
        }

        [Test]
        public void StripInsetsAreClampedToItsHeight()
        {
            var strip = new ScreenRect(0, 0, 80, 10);
            var parts = new List<StatusStrip.Placement> { new StatusStrip.Placement { Part = StatusStrip.Part.Chip, X = 10, Width = 30 } };
            var rectangles = new List<ScreenRect>();
            TerminalOpacity.StripBackground(strip, parts, 100, rectangles);
            Assert.AreEqual(5, rectangles[1].Height);
            Assert.AreEqual(5, rectangles[2].Y);
            TerminalOpacity.StripBackground(strip, parts, -1, rectangles);
            Assert.AreEqual(0, rectangles[1].Height);
            Assert.AreEqual(10, rectangles[2].Y);
        }
    }

    [TestFixture]
    public class TerminalOpacityMenuTest
    {
        [TestCase(0.5f)]
        [TestCase(1f)]
        [TestCase(1.3f)]
        [TestCase(2f)]
        [TestCase(3f)]
        public void RightClickOnlyOpensOnUnusedHeaderSpace(float scale)
        {
            var chrome = new TerminalChromeLayout(scale, 8, 16);
            foreach (float width in new[] { chrome.MinimumWindowWidth, 800f * scale })
            {
                float buttons = chrome.HeaderButtons(width).X;
                Assert.IsTrue(TerminalOpacityMenu.CanOpen(chrome, width, 0, 0, 1));
                Assert.IsTrue(TerminalOpacityMenu.CanOpen(chrome, width, buttons - 0.5f, chrome.HeaderHeight - 0.5f, 1));
                Assert.IsFalse(TerminalOpacityMenu.CanOpen(chrome, width, buttons, 0, 1), "buttons include their gap");
                Assert.IsFalse(TerminalOpacityMenu.CanOpen(chrome, width, 10, 0, 0), "left click still drags");
                Assert.IsFalse(TerminalOpacityMenu.CanOpen(chrome, width, 10, 0, 2), "middle click");
                Assert.IsFalse(TerminalOpacityMenu.CanOpen(chrome, width, -1, 0, 1));
                Assert.IsFalse(TerminalOpacityMenu.CanOpen(chrome, width, 1, -1, 1));
                Assert.IsFalse(TerminalOpacityMenu.CanOpen(chrome, width, 1, chrome.HeaderHeight, 1), "body");
                Assert.IsFalse(TerminalOpacityMenu.CanOpen(chrome, width, width, 0, 1));
                Assert.IsFalse(TerminalOpacityMenu.CanOpen(chrome, width, float.NaN, 0, 1));
                foreach (ScreenRect button in new[] { chrome.SmallerTextButton(width), chrome.LargerTextButton(width), chrome.CloseButton(width) })
                    Assert.IsFalse(TerminalOpacityMenu.CanOpen(chrome, width, button.X + button.Width / 2, button.Height / 2, 1));
                if (chrome.ShowsPresets(width))
                    for (int slot = 1; slot <= 3; ++slot)
                    {
                        ScreenRect button = chrome.PresetButton(slot, width);
                        Assert.IsFalse(TerminalOpacityMenu.CanOpen(chrome, width, button.X + 1, button.Height / 2, 1), "preset " + slot);
                    }
            }
        }

        [Test]
        public void ContextClickRequiresAMatchingPressAndRightReleaseInTheHeader()
        {
            var menu = new TerminalOpacityMenu();
            var chrome = new TerminalChromeLayout(1, 8, 16);
            Assert.IsFalse(menu.EndContext(chrome, 400, 10, 10, 1), "no press");
            Assert.IsFalse(menu.BeginContext(chrome, 400, 10, 10, 0), "left drag is not captured");
            Assert.IsTrue(menu.BeginContext(chrome, 400, 10, 10, 1));
            Assert.IsFalse(menu.IsOpen, "wait for release");
            Assert.IsFalse(menu.EndContext(chrome, 400, 10, 10, 0), "left release is unrelated");
            Assert.IsTrue(menu.EndContext(chrome, 400, 10, 10, 1));
            Assert.IsFalse(menu.EndContext(chrome, 400, 10, 10, 1), "only once");
            menu.BeginContext(chrome, 400, 10, 10, 1);
            Assert.IsFalse(menu.EndContext(chrome, 400, chrome.CloseButton(400).X, 10, 1), "released on a button");
            Assert.IsFalse(menu.EndContext(chrome, 400, 10, 10, 1), "a cancelled click is finished");
            menu.BeginContext(chrome, 400, 10, 10, 1);
            Assert.IsFalse(menu.EndContext(chrome, 400, 10, 100, 1), "released over characters");
        }

        [Test]
        public void ClosingAlsoCancelsAPendingContextClick()
        {
            var menu = new TerminalOpacityMenu();
            var chrome = new TerminalChromeLayout(1, 8, 16);
            Assert.IsTrue(menu.BeginContext(chrome, 400, 10, 10, 1));
            menu.Close();
            Assert.IsFalse(menu.EndContext(chrome, 400, 10, 10, 1), "hidden/power/focus loss during a press");
        }

        [TestCase(0.5f)]
        [TestCase(1f)]
        [TestCase(1.3f)]
        [TestCase(2f)]
        [TestCase(3f)]
        public void PopupContentsFitAndItsPositionIsKeptOnScreen(float scale)
        {
            var menu = new TerminalOpacityMenu();
            menu.Open(1919, 1079, scale, 1920, 1080);
            Assert.IsTrue(menu.IsOpen);
            TerminalOpacityMenuLayout layout = menu.Layout;
            Assert.AreEqual(1920 - layout.Width, menu.Bounds.X);
            Assert.AreEqual(1080 - layout.Height, menu.Bounds.Y);
            foreach (ScreenRect part in new[] { layout.Caption, layout.Readout, layout.Slider })
            {
                Assert.GreaterOrEqual(part.X, layout.Padding);
                Assert.GreaterOrEqual(part.Y, layout.Padding);
                Assert.LessOrEqual(part.X + part.Width, layout.Width - layout.Padding);
                Assert.LessOrEqual(part.Y + part.Height, layout.Height - layout.Padding);
                Assert.Greater(part.Width, 0);
                Assert.Greater(part.Height, 0);
            }
            Assert.AreEqual(layout.Caption.X + layout.Caption.Width, layout.Readout.X);
            Assert.AreEqual(layout.Caption.Y + layout.Caption.Height, layout.Slider.Y);
            Assert.GreaterOrEqual(layout.Padding, layout.Border);
            menu.Open(-100, -100, scale, 1920, 1080);
            Assert.AreEqual(0, menu.Bounds.X);
            Assert.AreEqual(0, menu.Bounds.Y);
            menu.Open(50, 60, scale, 1920, 1080);
            Assert.AreEqual(50, menu.Bounds.X);
            Assert.AreEqual(60, menu.Bounds.Y);
        }

        [Test]
        public void RefreshRefitsThePopupAndKeepsTheLayoutWhileScaleIsUnchanged()
        {
            var menu = new TerminalOpacityMenu();
            menu.Open(1800, 1000, 1, 1920, 1080);
            TerminalOpacityMenuLayout layout = menu.Layout;
            menu.Refresh(1, 1024, 768, true);
            Assert.AreSame(layout, menu.Layout);
            Assert.AreEqual(1024 - layout.Width, menu.Bounds.X);
            Assert.AreEqual(768 - layout.Height, menu.Bounds.Y);
            menu.Refresh(2, 1024, 768, true);
            Assert.AreNotSame(layout, menu.Layout);
            Assert.AreEqual(440, menu.Bounds.Width);
            menu.Refresh(1, 80, 40, true);
            Assert.AreEqual(0, menu.Bounds.X, "larger than screen: first controls reachable");
            Assert.AreEqual(0, menu.Bounds.Y);
        }

        [Test]
        public void HiddenUnpoweredUnfocusedAndModalMenusCloseAndDoNotReopen()
        {
            var menu = new TerminalOpacityMenu();
            Assert.IsFalse(menu.IsOpen);
            Assert.IsFalse(menu.Contains(10, 10));
            menu.Refresh(1, 1024, 768, true);
            Assert.IsNull(menu.Layout, "a closed menu does no layout work");
            menu.Open(50, 60, 1, 1024, 768);
            menu.Refresh(1, 1024, 768, false);
            Assert.IsFalse(menu.IsOpen);
            Assert.IsFalse(menu.Contains(60, 70));
            menu.Refresh(1, 1024, 768, true);
            Assert.IsFalse(menu.IsOpen, "recovering availability is not a user open");
            Assert.IsFalse(menu.Escape());
            menu.Close();
            menu.Close();
        }

        [Test]
        public void InsidePressesKeepTheMenuAndAnyOutsidePressDismissesIt()
        {
            var menu = new TerminalOpacityMenu();
            menu.Open(50, 60, 1, 1024, 768);
            menu.PointerDown(50, 60);
            Assert.IsTrue(menu.IsOpen, "left/top edges included");
            menu.PointerDown(269.5f, 119.5f);
            Assert.IsTrue(menu.IsOpen, "inside right/bottom");
            menu.PointerDown(270, 70);
            Assert.IsFalse(menu.IsOpen, "right edge excluded");
            menu.Open(50, 60, 1, 1024, 768);
            menu.PointerDown(60, 120);
            Assert.IsFalse(menu.IsOpen, "bottom edge excluded");
            menu.Open(50, 60, 1, 1024, 768);
            menu.PointerDown(49, 60);
            Assert.IsFalse(menu.IsOpen);
            menu.Open(50, 60, 1, 1024, 768);
            menu.PointerDown(50, 59);
            Assert.IsFalse(menu.IsOpen);
        }

        [Test]
        public void EscapeTakesOnlyOneKeyAndMenusAreIndependent()
        {
            var first = new TerminalOpacityMenu();
            var second = new TerminalOpacityMenu();
            first.Open(50, 60, 1, 1024, 768);
            second.Open(400, 100, 1, 1024, 768);
            Assert.IsTrue(first.Escape());
            Assert.IsFalse(first.Escape());
            Assert.IsTrue(second.IsOpen);
            Assert.IsTrue(second.Contains(400, 100));
        }

        [Test]
        public void LayoutUsesTheSameScaleLimitsAsTheTerminal()
        {
            Assert.AreEqual(1, new TerminalOpacityMenuLayout(float.NaN).Scale);
            Assert.AreEqual(1, new TerminalOpacityMenuLayout(float.PositiveInfinity).Scale);
            Assert.AreEqual(0.5f, new TerminalOpacityMenuLayout(0).Scale);
            Assert.AreEqual(3, new TerminalOpacityMenuLayout(10).Scale);
        }
    }
}
