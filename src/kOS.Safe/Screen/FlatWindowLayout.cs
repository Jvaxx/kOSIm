using System;
using System.Collections.Generic;
using kOS.Safe.UserIO;

namespace kOS.Safe.Screen
{
    /// <summary>What runs along the bottom of a <see cref="FlatWindowLayout"/> window.</summary>
    public enum FlatWindowBottom
    {
        None,
        /// <summary>A row of buttons, right-aligned (a confirmation, the paste box, the name tag).</summary>
        Buttons,
        /// <summary>A one-line strip as the terminal's (the editor).</summary>
        Strip
    }

    /// <summary>
    /// Where the parts of kOS's smaller flat windows go (Phase 4, step 5: the editor, its
    /// confirmation, the paste box, the name tag, the list pickers), in window coordinates
    /// (origin top left): a header as the terminal's (title, buttons on the right), the body
    /// inside the padding, and the bottom (<see cref="FlatWindowBottom"/>). Metrics follow
    /// KSP's UI scale with the terminal's (<see cref="TerminalChromeLayout"/>); each is a
    /// whole number of pixels.
    /// </summary>
    public sealed class FlatWindowLayout
    {
        // At UI scale 1, in pixels (the header, strip, padding and fonts are the terminal's):
        public const int BASE_ROW_HEIGHT = 22;
        public const int BASE_SMALL_FONT_SIZE = 10;
        public const int BASE_GAP = 4;
        public const int BASE_MINIMUM_BUTTON_WIDTH = 56;
        /// <summary>A confirmation's, the paste box's and the name tag's width.</summary>
        public const int BASE_DIALOG_WIDTH = 340;
        public const int BASE_NAME_TAG_WIDTH = 240;
        public const int BASE_EDITOR_MINIMUM_WIDTH = 260;
        public const int BASE_EDITOR_MINIMUM_HEIGHT = 140;

        public float Scale { get; private set; }
        public int HeaderHeight { get; private set; }
        public int StripHeight { get; private set; }
        public int Padding { get; private set; }
        public int FontSize { get; private set; }
        public int SmallFontSize { get; private set; }
        /// <summary>A button's, a text field's or a list row's height.</summary>
        public int RowHeight { get; private set; }
        /// <summary>Between buttons side by side.</summary>
        public int Gap { get; private set; }
        public int MinimumButtonWidth { get; private set; }
        /// <summary>The header's square buttons (close) are this wide.</summary>
        public int IconButtonWidth { get; private set; }
        public int Border { get; private set; }
        public int ScrollBarWidth { get; private set; }
        public int MinimumThumb { get; private set; }
        public int ResizeBand { get; private set; }
        public int ResizeCorner { get; private set; }
        public int DialogWidth { get; private set; }
        public int NameTagWidth { get; private set; }
        public int EditorMinimumWidth { get; private set; }
        public int EditorMinimumHeight { get; private set; }

        /// <param name="uiScale">KSP's UI scale; clamped as <see cref="TerminalChromeLayout.ClampScale"/></param>
        public FlatWindowLayout(float uiScale)
        {
            Scale = TerminalChromeLayout.ClampScale(uiScale);
            HeaderHeight = Scaled(TerminalChromeLayout.BASE_HEADER_HEIGHT);
            StripHeight = Scaled(TerminalChromeLayout.BASE_STRIP_HEIGHT);
            Padding = Math.Max(TerminalChromeLayout.MINIMUM_PADDING, Scaled(TerminalChromeLayout.BASE_PADDING));
            FontSize = Scaled(TerminalChromeLayout.BASE_FONT_SIZE);
            SmallFontSize = Scaled(BASE_SMALL_FONT_SIZE);
            RowHeight = Scaled(BASE_ROW_HEIGHT);
            Gap = Scaled(BASE_GAP);
            MinimumButtonWidth = Scaled(BASE_MINIMUM_BUTTON_WIDTH);
            IconButtonWidth = HeaderHeight;
            Border = Scaled(1);
            ScrollBarWidth = Scaled(TerminalChromeLayout.BASE_SCROLL_BAR_WIDTH);
            MinimumThumb = Scaled(TerminalChromeLayout.BASE_MINIMUM_THUMB);
            ResizeBand = Scaled(TerminalChromeLayout.BASE_RESIZE_BAND);
            ResizeCorner = Scaled(TerminalChromeLayout.BASE_RESIZE_CORNER);
            DialogWidth = Scaled(BASE_DIALOG_WIDTH);
            NameTagWidth = Scaled(BASE_NAME_TAG_WIDTH);
            EditorMinimumWidth = Scaled(BASE_EDITOR_MINIMUM_WIDTH);
            EditorMinimumHeight = Scaled(BASE_EDITOR_MINIMUM_HEIGHT);
        }

        private int Scaled(int pixels)
        {
            return Math.Max(1, (int)Math.Round(pixels * Scale, MidpointRounding.AwayFromZero));
        }

        public float BottomHeight(FlatWindowBottom bottom)
        {
            switch (bottom)
            {
                case FlatWindowBottom.Buttons: return RowHeight + Padding;
                case FlatWindowBottom.Strip: return StripHeight;
                default: return 0;
            }
        }

        /// <summary>The window's height for a body <paramref name="bodyHeight"/> high inside its padding.</summary>
        public float WindowHeight(float bodyHeight, FlatWindowBottom bottom)
        {
            return HeaderHeight + 2 * Padding + NonNegative(bodyHeight) + BottomHeight(bottom);
        }

        /// <summary>A button wide enough for a label <paramref name="textWidth"/> wide, at least <see cref="MinimumButtonWidth"/>.</summary>
        public float ButtonWidth(float textWidth)
        {
            return Math.Max(MinimumButtonWidth, (float)Math.Ceiling(NonNegative(textWidth) + 4 * Padding));
        }

        public ScreenRect Header(float windowWidth)
        {
            return new ScreenRect(0, 0, NonNegative(windowWidth), HeaderHeight);
        }

        /// <summary>The square button at the header's right end.</summary>
        public ScreenRect CloseButton(float windowWidth)
        {
            float width = Math.Min(IconButtonWidth, NonNegative(windowWidth));
            return new ScreenRect(NonNegative(windowWidth) - width, 0, width, HeaderHeight);
        }

        /// <summary>
        /// Text buttons in the header, left of the close button, in the given order, each
        /// <paramref name="widths"/> wide (squeezed alike when they do not fit the right half).
        /// </summary>
        public List<ScreenRect> HeaderButtons(float windowWidth, IList<float> widths)
        {
            ScreenRect close = CloseButton(windowWidth);
            float inset = Math.Max(1, (float)Math.Round(HeaderHeight * 0.15));
            var room = new ScreenRect(0, inset, close.X, HeaderHeight - 2 * inset);
            return RightAligned(room, widths, Gap, close.X / 2f);
        }

        /// <summary>The title's room: from the padding to the first of <paramref name="buttonsLeft"/> (the left edge of the header's buttons).</summary>
        public ScreenRect Title(float buttonsLeft)
        {
            return new ScreenRect(Padding, 0, NonNegative(buttonsLeft - 2 * Padding), HeaderHeight);
        }

        /// <summary>Between the header and the bottom, inside the padding.</summary>
        public ScreenRect Body(float windowWidth, float windowHeight, FlatWindowBottom bottom)
        {
            float top = HeaderHeight + Padding;
            return new ScreenRect(Padding, top, NonNegative(windowWidth - 2 * Padding),
                NonNegative(windowHeight - top - Padding - BottomHeight(bottom)));
        }

        public ScreenRect Strip(float windowWidth, float windowHeight)
        {
            return new ScreenRect(0, NonNegative(windowHeight - StripHeight), NonNegative(windowWidth), StripHeight);
        }

        /// <summary>The bottom-right corner of the strip, where the resize grip is drawn.</summary>
        public ScreenRect Grip(float windowWidth, float windowHeight)
        {
            ScreenRect strip = Strip(windowWidth, windowHeight);
            float width = Math.Min(StripHeight, strip.Width);
            return new ScreenRect(strip.X + strip.Width - width, strip.Y, width, strip.Height);
        }

        /// <summary>The bottom row's buttons, right-aligned inside the padding, in the given order.</summary>
        public List<ScreenRect> Buttons(float windowWidth, float windowHeight, IList<float> widths)
        {
            var row = new ScreenRect(Padding, NonNegative(windowHeight - Padding - RowHeight), NonNegative(windowWidth - 2 * Padding), RowHeight);
            return RightAligned(row, widths, Gap, row.Width);
        }

        /// <summary>Where a scroll bar runs, at <paramref name="view"/>'s right edge.</summary>
        public ScreenRect ScrollTrack(ScreenRect view)
        {
            float width = Math.Min(ScrollBarWidth, NonNegative(view.Width));
            return new ScreenRect(view.X + view.Width - width, view.Y, width, NonNegative(view.Height));
        }

        public ScreenRect ScrollThumb(ScreenRect track, float contentHeight, float viewHeight, float scroll)
        {
            return PixelScroll.Thumb(track, contentHeight, viewHeight, scroll, MinimumThumb);
        }

        public float ScrollFor(float thumbTop, ScreenRect track, float contentHeight, float viewHeight)
        {
            return PixelScroll.ScrollFor(thumbTop, track, contentHeight, viewHeight, MinimumThumb);
        }

        /// <summary>
        /// Rectangles <paramref name="widths"/> wide, left to right in their order, ending at
        /// <paramref name="room"/>'s right edge with <paramref name="gap"/> between them; scaled
        /// down alike when together they are wider than <paramref name="most"/>.
        /// </summary>
        private static List<ScreenRect> RightAligned(ScreenRect room, IList<float> widths, float gap, float most)
        {
            var rects = new List<ScreenRect>();
            if (widths == null || widths.Count == 0) return rects;
            float total = 0;
            foreach (float width in widths)
                total += NonNegative(width);
            float gaps = gap * (widths.Count - 1);
            float available = NonNegative(Math.Min(most, room.Width) - gaps);
            float factor = total > available && total > 0 ? available / total : 1f;
            float x = room.X + room.Width;
            var placed = new ScreenRect[widths.Count];
            for (int i = widths.Count - 1; i >= 0; --i)
            {
                float width = (float)Math.Floor(NonNegative(widths[i]) * factor);
                x -= width;
                placed[i] = new ScreenRect(x, room.Y, width, room.Height);
                x -= gap;
            }
            rects.AddRange(placed);
            return rects;
        }

        private static float NonNegative(float value)
        {
            return value > 0 ? value : 0;
        }
    }
}
