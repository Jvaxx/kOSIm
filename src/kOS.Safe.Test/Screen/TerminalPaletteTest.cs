using System;
using kOS.Safe.Screen;
using NUnit.Framework;

namespace kOS.Safe.Test.Terminal
{
    /// <summary>
    /// Phase 4, step 3: the terminal's colors are readable. WCAG 2 contrast ratios: 7:1 for the
    /// main text, 4.5:1 for any other text (also at the default brightness, which fades the
    /// text into the background), 3:1 for marks that are not text (state dots, the focus
    /// border, the scroll bar's thumb).
    /// </summary>
    [TestFixture]
    public class TerminalPaletteTest
    {
        /// <summary>The default <c>TerminalBrightness</c> (config.xml) of a new terminal.</summary>
        private const float DEFAULT_BRIGHTNESS = 0.7f;
        private static readonly float[] brightnesses = { 1f, DEFAULT_BRIGHTNESS };

        private static double Contrast(PaletteColor a, PaletteColor b)
        {
            return PaletteColor.ContrastRatio(a, b);
        }

        /// <summary>A cell color as the terminal draws it: its alpha is the brightness.</summary>
        private static PaletteColor AtBrightness(PaletteColor color, float brightness)
        {
            return color.WithAlpha(brightness).Over(TerminalPalette.Background);
        }

        private static void AssertAtLeast(double minimum, double ratio, string what)
        {
            Assert.GreaterOrEqual(ratio, minimum, string.Format("{0}: {1:0.00}:1", what, ratio));
        }

        // PaletteColor:

        [Test]
        public void ContrastRatiosMatchWcag()
        {
            PaletteColor black = PaletteColor.FromHex("#000000"), white = PaletteColor.FromHex("#FFFFFF");
            Assert.AreEqual(21.0, Contrast(black, white), 1e-9);
            Assert.AreEqual(21.0, Contrast(white, black), 1e-9, "either order");
            Assert.AreEqual(1.0, Contrast(TerminalPalette.Text, TerminalPalette.Text), 1e-9);
            // WCAG's own example: #777777 on white is just under 4.5:1.
            Assert.AreEqual(4.48, Contrast(PaletteColor.FromHex("#777777"), white), 0.01);
        }

        [Test]
        public void HexAndBlending()
        {
            PaletteColor color = PaletteColor.FromHex("#7EE08F");
            Assert.AreEqual(new PaletteColor(0x7E, 0xE0, 0x8F), color);
            Assert.AreEqual(color, PaletteColor.FromHex("7ee08f"), "without # and in lower case");
            Assert.AreEqual(1f, color.A);
            Assert.Throws<FormatException>(() => PaletteColor.FromHex("#7EE08"));
            Assert.Throws<ArgumentNullException>(() => PaletteColor.FromHex(null));

            PaletteColor black = PaletteColor.FromHex("#000000"), white = PaletteColor.FromHex("#FFFFFF");
            Assert.AreEqual(white, white.Over(black), "opaque covers");
            Assert.AreEqual(black, white.WithAlpha(0f).Over(black), "transparent shows what is below");
            Assert.AreEqual(new PaletteColor(128, 128, 128), white.WithAlpha(0.5f).Over(black));
            Assert.AreEqual(1f, white.WithAlpha(2f).A, "alpha clamped");
        }

        // The cells:

        [Test]
        public void TheTextIsReadable()
        {
            AssertAtLeast(7, Contrast(TerminalPalette.Text, TerminalPalette.Background), "text");
            AssertAtLeast(4.5, Contrast(AtBrightness(TerminalPalette.Text, DEFAULT_BRIGHTNESS), TerminalPalette.Background),
                "text at the default brightness");
            foreach (float brightness in brightnesses)
                AssertAtLeast(4.5, Contrast(AtBrightness(TerminalPalette.UnpoweredText, brightness), TerminalPalette.Background),
                    "unpowered text at brightness " + brightness);
        }

        [Test]
        public void TheCursorAndAReversedScreenAreReadable()
        {
            foreach (float brightness in brightnesses)
            {
                // The cursor's character, and a reversed screen's text, take the background's color on the cursor's color.
                PaletteColor block = AtBrightness(TerminalPalette.Cursor, brightness);
                AssertAtLeast(4.5, Contrast(TerminalPalette.Background, block), "the cursor's character at brightness " + brightness);
            }
        }

        [Test]
        public void TheTextUnderTheSelectionIsReadable()
        {
            foreach (float brightness in brightnesses)
            {
                PaletteColor selection = TerminalPalette.Text.WithAlpha(TerminalPalette.SELECTION_ALPHA * brightness);
                PaletteColor glyph = selection.Over(AtBrightness(TerminalPalette.Text, brightness));
                PaletteColor behind = selection.Over(TerminalPalette.Background);
                AssertAtLeast(4.5, Contrast(glyph, behind), "selected text at brightness " + brightness);
                Assert.Greater(Contrast(behind, TerminalPalette.Background), 1.4, "the selection shows at brightness " + brightness);
            }
        }

        // The chrome:

        [Test]
        public void TheChromeTextIsReadable()
        {
            AssertAtLeast(7, Contrast(TerminalPalette.ChromeText, TerminalPalette.Chrome), "chrome text");
            AssertAtLeast(4.5, Contrast(TerminalPalette.DimText, TerminalPalette.Chrome), "dim chrome text");
            AssertAtLeast(7, Contrast(TerminalPalette.ChromeText, TerminalPalette.Hover.Over(TerminalPalette.Chrome)), "a hovered button's icon");
            foreach (PaletteColor chipText in new[] { TerminalPalette.ChromeText, TerminalPalette.Accent, TerminalPalette.Warning })
            {
                AssertAtLeast(4.5, Contrast(chipText, TerminalPalette.Chip), "chip text " + chipText);
                AssertAtLeast(4.5, Contrast(chipText, TerminalPalette.Hover.Over(TerminalPalette.Chip)), "hovered chip text " + chipText);
            }
            AssertAtLeast(4.5, Contrast(TerminalPalette.Accent, TerminalPalette.Chrome), "a notice in the strip");
            AssertAtLeast(7, Contrast(TerminalPalette.ChromeText, TerminalPalette.Panel), "panel text");
            // The toolbar panel (step 4): notes, a selected tab and a hovered row on the panel.
            AssertAtLeast(4.5, Contrast(TerminalPalette.DimText, TerminalPalette.Panel), "dim panel text");
            AssertAtLeast(4.5, Contrast(TerminalPalette.Accent, TerminalPalette.Panel), "accent text on the panel");
            AssertAtLeast(7, Contrast(TerminalPalette.ChromeText, TerminalPalette.Hover.Over(TerminalPalette.Panel)), "a hovered row's text");
            AssertAtLeast(4.5, Contrast(TerminalPalette.DimText, TerminalPalette.Hover.Over(TerminalPalette.Panel)), "a hovered row's state");
        }

        [Test]
        public void TheFocusBorderStandsOut()
        {
            AssertAtLeast(3, Contrast(TerminalPalette.Accent, TerminalPalette.Chrome), "accent on the chrome");
            AssertAtLeast(3, Contrast(TerminalPalette.Accent, TerminalPalette.Frame), "focused against unfocused frame");
            AssertAtLeast(3, Contrast(TerminalPalette.Accent, TerminalPalette.Background), "accent on the body (edges)");
        }

        [Test]
        public void EveryStateDotShowsOnTheStrip()
        {
            foreach (StatusKind kind in Enum.GetValues(typeof(StatusKind)))
                AssertAtLeast(3, Contrast(TerminalPalette.Status(kind), TerminalPalette.Chrome), "state " + kind);
        }

        [Test]
        public void TheStatesThatNeedAttentionDifferFromReady()
        {
            PaletteColor ready = TerminalPalette.Status(StatusKind.Ready);
            foreach (StatusKind kind in new[] { StatusKind.Running, StatusKind.Waiting, StatusKind.NoPower, StatusKind.Off })
                Assert.AreNotEqual(ready, TerminalPalette.Status(kind), kind.ToString());
        }

        [Test]
        public void TheScrollBarsThumbShowsOnItsTrack()
        {
            PaletteColor track = TerminalPalette.ScrollTrack.Over(TerminalPalette.Background);
            AssertAtLeast(3, Contrast(TerminalPalette.DimText, track), "thumb");
            AssertAtLeast(3, Contrast(TerminalPalette.ChromeText, track), "held thumb");
        }

        [Test]
        public void TheChromeIsAStepLighterThanTheBody()
        {
            Assert.Greater(TerminalPalette.Chrome.RelativeLuminance, TerminalPalette.Background.RelativeLuminance);
            Assert.Greater(TerminalPalette.Chip.RelativeLuminance, TerminalPalette.Chrome.RelativeLuminance);
            Assert.Less(Contrast(TerminalPalette.Chrome, TerminalPalette.Background), 1.5, "a step, not a contrast");
        }
    }
}
