using System;
using kOS.Safe.UserIO;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// Where the toolbar panel's parts go (Phase 4, step 4), in window coordinates (origin top
    /// left): a header (the title), a tab bar, the tab's content (scrolled when it does not fit),
    /// on the settings tab a description of the hovered setting, and a footer (emergency
    /// suppress, the telnet line). The width is fixed; the height follows the content, up to
    /// the room the launcher leaves. The chrome follows KSP's UI scale, as the terminal's
    /// (<see cref="TerminalChromeLayout"/>). Every metric is a whole number of pixels.
    /// </summary>
    public sealed class ToolbarPanelLayout
    {
        // At UI scale 1, in pixels:
        public const int BASE_WIDTH = 340;
        public const int BASE_HEADER_HEIGHT = 22;
        public const int BASE_TAB_HEIGHT = 22;
        public const int BASE_PADDING = 6;
        public const int BASE_FONT_SIZE = 11;
        public const int BASE_SMALL_FONT_SIZE = 10;
        public const int BASE_VESSEL_ROW_HEIGHT = 22;
        /// <summary>A CPU: its name on one line, its state on the next.</summary>
        public const int BASE_CPU_ROW_HEIGHT = 34;
        public const int BASE_HEADING_HEIGHT = 22;
        public const int BASE_SETTING_ROW_HEIGHT = 22;
        /// <summary>A line of small text (a note, the telnet line).</summary>
        public const int BASE_NOTE_HEIGHT = 16;
        public const int BASE_BUTTON_WIDTH = 56;
        public const int BASE_SCROLL_BAR_WIDTH = 6;
        public const int BASE_MINIMUM_THUMB = 16;
        /// <summary>The content is never squeezed below this, even when the room is smaller.</summary>
        public const int BASE_MINIMUM_CONTENT_HEIGHT = 60;

        public float Scale { get; private set; }
        public int Width { get; private set; }
        public int HeaderHeight { get; private set; }
        public int TabHeight { get; private set; }
        public int Padding { get; private set; }
        public int FontSize { get; private set; }
        public int SmallFontSize { get; private set; }
        public int VesselRowHeight { get; private set; }
        public int CpuRowHeight { get; private set; }
        public int HeadingHeight { get; private set; }
        public int SettingRowHeight { get; private set; }
        public int NoteHeight { get; private set; }
        public int ButtonWidth { get; private set; }
        public int ScrollBarWidth { get; private set; }
        public int MinimumThumb { get; private set; }
        public int MinimumContentHeight { get; private set; }
        /// <summary>The frame's and the selected tab's line thickness.</summary>
        public int Border { get; private set; }
        /// <summary>Two lines of small text under the settings: the hovered setting's description.</summary>
        public int DescriptionHeight { get; private set; }
        /// <summary>The emergency suppress button and the telnet line, with padding.</summary>
        public int FooterHeight { get; private set; }

        /// <param name="uiScale">KSP's UI scale; clamped as <see cref="TerminalChromeLayout.ClampScale"/></param>
        public ToolbarPanelLayout(float uiScale)
        {
            Scale = TerminalChromeLayout.ClampScale(uiScale);
            Width = Scaled(BASE_WIDTH);
            HeaderHeight = Scaled(BASE_HEADER_HEIGHT);
            TabHeight = Scaled(BASE_TAB_HEIGHT);
            Padding = Scaled(BASE_PADDING);
            FontSize = Scaled(BASE_FONT_SIZE);
            SmallFontSize = Scaled(BASE_SMALL_FONT_SIZE);
            VesselRowHeight = Scaled(BASE_VESSEL_ROW_HEIGHT);
            CpuRowHeight = Scaled(BASE_CPU_ROW_HEIGHT);
            HeadingHeight = Scaled(BASE_HEADING_HEIGHT);
            SettingRowHeight = Scaled(BASE_SETTING_ROW_HEIGHT);
            NoteHeight = Scaled(BASE_NOTE_HEIGHT);
            ButtonWidth = Scaled(BASE_BUTTON_WIDTH);
            ScrollBarWidth = Scaled(BASE_SCROLL_BAR_WIDTH);
            MinimumThumb = Scaled(BASE_MINIMUM_THUMB);
            MinimumContentHeight = Scaled(BASE_MINIMUM_CONTENT_HEIGHT);
            Border = Scaled(1);
            DescriptionHeight = 2 * NoteHeight + Padding;
            FooterHeight = SettingRowHeight + NoteHeight + 2 * Padding;
        }

        private int Scaled(int pixels)
        {
            return Math.Max(1, (int)Math.Round(pixels * Scale, MidpointRounding.AwayFromZero));
        }

        /// <summary>Everything but the content: header, tabs, the padding around the content, the description (settings tab), the footer.</summary>
        public int FixedHeight(bool withDescription)
        {
            return HeaderHeight + TabHeight + 2 * Padding + (withDescription ? DescriptionHeight : 0) + FooterHeight;
        }

        /// <summary>
        /// The window's height for content <paramref name="contentHeight"/> high: all of it when it
        /// fits in <paramref name="room"/>, else the room (the content then scrolls), but never
        /// less than <see cref="MinimumContentHeight"/> of content. No cap when the room is not
        /// positive (not known).
        /// </summary>
        public float WindowHeight(float contentHeight, bool withDescription, float room)
        {
            float fixedHeight = FixedHeight(withDescription);
            float wanted = fixedHeight + Math.Max(MinimumContentHeight, NonNegative(contentHeight));
            if (!(room > 0)) return wanted;
            return Math.Min(wanted, Math.Max(fixedHeight + MinimumContentHeight, (float)Math.Floor(room)));
        }

        public ScreenRect Header()
        {
            return new ScreenRect(0, 0, Width, HeaderHeight);
        }

        /// <summary>Tab <paramref name="index"/> of <paramref name="count"/>, side by side across the window.</summary>
        public ScreenRect Tab(int index, int count)
        {
            if (count <= 0 || index < 0 || index >= count) return new ScreenRect(0, HeaderHeight, 0, 0);
            float left = (float)Math.Round((double)Width * index / count);
            float right = (float)Math.Round((double)Width * (index + 1) / count);
            return new ScreenRect(left, HeaderHeight, right - left, TabHeight);
        }

        /// <summary>The scrolled area between the tabs and the description or footer, inside the padding.</summary>
        public ScreenRect Content(float windowHeight, bool withDescription)
        {
            float top = HeaderHeight + TabHeight + Padding;
            float height = windowHeight - FixedHeight(withDescription);
            return new ScreenRect(Padding, top, NonNegative(Width - 2 * Padding), NonNegative(height));
        }

        /// <summary>Under the content: the hovered setting's description (settings tab only).</summary>
        public ScreenRect Description(float windowHeight)
        {
            return new ScreenRect(Padding, NonNegative(windowHeight - FooterHeight - DescriptionHeight), NonNegative(Width - 2 * Padding), DescriptionHeight);
        }

        public ScreenRect Footer(float windowHeight)
        {
            return new ScreenRect(0, NonNegative(windowHeight - FooterHeight), Width, FooterHeight);
        }

        public ScreenRect SuppressButton(float windowHeight)
        {
            ScreenRect footer = Footer(windowHeight);
            return new ScreenRect(Padding, footer.Y + Padding, NonNegative(Width - 2 * Padding), SettingRowHeight);
        }

        public ScreenRect TelnetLine(float windowHeight)
        {
            ScreenRect button = SuppressButton(windowHeight);
            return new ScreenRect(Padding, button.Y + button.Height, button.Width, NoteHeight);
        }

        /// <summary>The rows' width: the content's, less the scroll bar and a gap when it scrolls.</summary>
        public float RowWidth(bool scrolls)
        {
            return NonNegative(Width - 2 * Padding - (scrolls ? ScrollBarWidth + Padding : 0));
        }

        /// <summary>Where the scroll bar runs, at the content's right edge.</summary>
        public ScreenRect ScrollTrack(ScreenRect content)
        {
            float width = Math.Min(ScrollBarWidth, content.Width);
            return new ScreenRect(content.X + content.Width - width, content.Y, width, content.Height);
        }

        // Scrolling, in pixels from the content's top (PixelScroll):

        public static float ClampScroll(float scroll, float contentHeight, float viewHeight)
        {
            return PixelScroll.Clamp(scroll, contentHeight, viewHeight);
        }

        /// <summary>The thumb in <paramref name="track"/>: as long as the share in view (at least the minimum, at most the track), placed by the scroll.</summary>
        public ScreenRect ScrollThumb(ScreenRect track, float contentHeight, float viewHeight, float scroll)
        {
            return PixelScroll.Thumb(track, contentHeight, viewHeight, scroll, MinimumThumb);
        }

        /// <summary>The scroll for a thumb whose top is at <paramref name="thumbTop"/> (the inverse of <see cref="ScrollThumb"/>).</summary>
        public float ScrollFor(float thumbTop, ScreenRect track, float contentHeight, float viewHeight)
        {
            return PixelScroll.ScrollFor(thumbTop, track, contentHeight, viewHeight, MinimumThumb);
        }

        private static float NonNegative(float value)
        {
            return value > 0 ? value : 0;
        }
    }
}
