using System;
using kOS.Safe.Encapsulation;
using kOS.Safe.UserIO;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// Where the GUI terminal's parts go (Phase 4, step 1), in window coordinates (origin top
    /// left, as Unity's GUI): a header on top (title, settings and close buttons), the
    /// character field below it with even padding, a gutter on its right kept for the scroll
    /// bar (step 2), and the status strip at the bottom with the resize grip at its right end.
    /// <para>
    /// The chrome follows KSP's UI scale; the field follows the cell size (<c>CHARHEIGHT</c>).
    /// Drawing, selection, the wheel and resizing all use this one layout, and the window size
    /// for a number of cells and the cells that fit a window size are each other's inverse.
    /// </para>
    /// Every chrome metric is a whole number of pixels, so the field lands on pixel boundaries.
    /// </summary>
    public sealed class TerminalChromeLayout
    {
        // The chrome at UI scale 1, in pixels:
        public const int BASE_HEADER_HEIGHT = 20;
        public const int BASE_STRIP_HEIGHT = 18;
        public const int BASE_PADDING = 6;
        public const int BASE_SCROLL_BAR_WIDTH = 6;
        public const int BASE_FONT_SIZE = 11;
        /// <summary>What the header (title, two buttons) and the strip (state, size, grip) need at least.</summary>
        public const int BASE_MINIMUM_WIDTH = 150;

        /// <summary>The bottom row's descenders and underscores are drawn up to this far below the field.</summary>
        public const int MINIMUM_PADDING = 4;

        public const float MINIMUM_SCALE = 0.5f;
        public const float MAXIMUM_SCALE = 3f;

        public float Scale { get; private set; }
        public int CellWidth { get; private set; }
        public int CellHeight { get; private set; }

        public int HeaderHeight { get; private set; }
        public int StripHeight { get; private set; }
        /// <summary>Between the field and the window's left edge, the header and the strip.</summary>
        public int Padding { get; private set; }
        public int ScrollBarWidth { get; private set; }
        /// <summary>Right of the field: the scroll bar with a pixel either side, and at least the padding.</summary>
        public int GutterWidth { get; private set; }
        /// <summary>The focus border's thickness.</summary>
        public int Border { get; private set; }
        /// <summary>The header buttons are squares this wide; the grip is a square as high as the strip.</summary>
        public int ButtonWidth { get; private set; }
        public int FontSize { get; private set; }
        public int MinimumWidth { get; private set; }

        /// <param name="uiScale">KSP's UI scale; clamped to <see cref="MINIMUM_SCALE"/>..<see cref="MAXIMUM_SCALE"/>, 1 when not a number</param>
        /// <param name="cellWidth">a character cell's width in pixels (none fit when not positive)</param>
        /// <param name="cellHeight">a character cell's height in pixels (none fit when not positive)</param>
        public TerminalChromeLayout(float uiScale, int cellWidth, int cellHeight)
        {
            Scale = ClampScale(uiScale);
            CellWidth = Math.Max(0, cellWidth);
            CellHeight = Math.Max(0, cellHeight);
            HeaderHeight = Scaled(BASE_HEADER_HEIGHT);
            StripHeight = Scaled(BASE_STRIP_HEIGHT);
            Padding = Math.Max(MINIMUM_PADDING, Scaled(BASE_PADDING));
            ScrollBarWidth = Scaled(BASE_SCROLL_BAR_WIDTH);
            GutterWidth = Math.Max(Padding, ScrollBarWidth + 2);
            Border = Scaled(1);
            ButtonWidth = HeaderHeight;
            FontSize = Scaled(BASE_FONT_SIZE);
            MinimumWidth = Scaled(BASE_MINIMUM_WIDTH);
        }

        public static float ClampScale(float uiScale)
        {
            if (float.IsNaN(uiScale) || float.IsInfinity(uiScale)) return 1f;
            return Math.Max(MINIMUM_SCALE, Math.Min(MAXIMUM_SCALE, uiScale));
        }

        private int Scaled(int pixels)
        {
            return Math.Max(1, (int)Math.Round(pixels * Scale, MidpointRounding.AwayFromZero));
        }

        // Cells and window size:

        public int ColumnsThatFit(float windowWidth)
        {
            if (CellWidth <= 0) return 0;
            return Math.Max(0, (int)Math.Floor((windowWidth - Padding - GutterWidth) / CellWidth));
        }

        public int RowsThatFit(float windowHeight)
        {
            if (CellHeight <= 0) return 0;
            return Math.Max(0, (int)Math.Floor((windowHeight - HeaderHeight - StripHeight - 2 * Padding) / CellHeight));
        }

        /// <summary>The window's width for <paramref name="columns"/>, at least <see cref="MinimumWidth"/>.</summary>
        public float WindowWidthFor(int columns)
        {
            return Math.Max(MinimumWidth, Padding + Math.Max(0, columns) * CellWidth + GutterWidth);
        }

        public float WindowHeightFor(int rows)
        {
            return HeaderHeight + 2 * Padding + Math.Max(0, rows) * CellHeight + StripHeight;
        }

        /// <summary>The smallest window a resize drag gives: the script minimum of cells (<c>TERMINAL:WIDTH</c>).</summary>
        public float MinimumWindowWidth { get { return WindowWidthFor(TerminalStruct.MINCOLUMNS); } }

        /// <summary>The smallest window a resize drag gives: the script minimum of cells (<c>TERMINAL:HEIGHT</c>).</summary>
        public float MinimumWindowHeight { get { return WindowHeightFor(TerminalStruct.MINROWS); } }

        // The parts, for a window of the given size:

        public ScreenRect Header(float windowWidth)
        {
            return new ScreenRect(0, 0, NonNegative(windowWidth), HeaderHeight);
        }

        public ScreenRect CloseButton(float windowWidth)
        {
            return new ScreenRect(Math.Max(0, windowWidth - ButtonWidth), 0, ButtonWidth, HeaderHeight);
        }

        public ScreenRect SettingsButton(float windowWidth)
        {
            return new ScreenRect(Math.Max(0, windowWidth - 2 * ButtonWidth), 0, ButtonWidth, HeaderHeight);
        }

        /// <summary>The title's room: from the padding to the settings button.</summary>
        public ScreenRect Title(float windowWidth)
        {
            return new ScreenRect(Padding, 0, NonNegative(SettingsButton(windowWidth).X - 2 * Padding), HeaderHeight);
        }

        /// <summary>Between the header and the strip: the field, its padding and the gutter.</summary>
        public ScreenRect Body(float windowWidth, float windowHeight)
        {
            return new ScreenRect(0, HeaderHeight, NonNegative(windowWidth), NonNegative(windowHeight - HeaderHeight - StripHeight));
        }

        /// <summary>The characters of a screen of <paramref name="columns"/> by <paramref name="rows"/>.</summary>
        public ScreenRect Field(int columns, int rows)
        {
            return new ScreenRect(Padding, HeaderHeight + Padding, Math.Max(0, columns) * CellWidth, Math.Max(0, rows) * CellHeight);
        }

        /// <summary>Where a press starts a selection: the body without the gutter.</summary>
        public ScreenRect SelectionArea(float windowWidth, float windowHeight)
        {
            ScreenRect body = Body(windowWidth, windowHeight);
            return new ScreenRect(body.X, body.Y, NonNegative(body.Width - GutterWidth), body.Height);
        }

        /// <summary>The right edge of the body, kept for the scroll bar.</summary>
        public ScreenRect Gutter(float windowWidth, float windowHeight)
        {
            ScreenRect body = Body(windowWidth, windowHeight);
            return new ScreenRect(Math.Max(0, body.Width - GutterWidth), body.Y, Math.Min(GutterWidth, body.Width), body.Height);
        }

        public ScreenRect Strip(float windowWidth, float windowHeight)
        {
            return new ScreenRect(0, Math.Max(0, windowHeight - StripHeight), NonNegative(windowWidth), StripHeight);
        }

        public ScreenRect Grip(float windowWidth, float windowHeight)
        {
            return new ScreenRect(Math.Max(0, windowWidth - StripHeight), Math.Max(0, windowHeight - StripHeight), StripHeight, StripHeight);
        }

        /// <summary>The strip's room for the state, chips and size: from the padding to the grip.</summary>
        public ScreenRect StripContent(float windowWidth, float windowHeight)
        {
            ScreenRect strip = Strip(windowWidth, windowHeight);
            return new ScreenRect(Padding, strip.Y, NonNegative(Grip(windowWidth, windowHeight).X - 2 * Padding), StripHeight);
        }

        private static float NonNegative(float value)
        {
            return value > 0 ? value : 0;
        }
    }
}
