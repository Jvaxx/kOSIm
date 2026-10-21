using System;
using System.Collections.Generic;
using System.Globalization;
using kOS.Safe.UserIO;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// One GUI window's background opacity, independent of the screen's text brightness.
    /// No Unity state: colors are composed before drawing so a background is painted once.
    /// </summary>
    public sealed class TerminalOpacity
    {
        public const int DEFAULT_PERCENT = 100;
        private int percent = DEFAULT_PERCENT;
        private string readout;

        public int Percent
        {
            get { return percent; }
            set
            {
                int next = Math.Max(0, Math.Min(100, value));
                if (next == percent) return;
                percent = next;
                readout = null;
            }
        }

        public string Readout
        {
            get { return readout ?? (readout = percent.ToString(CultureInfo.InvariantCulture) + "%"); }
        }

        /// <summary>Missing or rejected opacity (including older presets) restores 100%, not the previous value.</summary>
        public void Restore(TerminalGeometry saved)
        {
            Percent = saved == null ? DEFAULT_PERCENT : saved.Opacity ?? DEFAULT_PERCENT;
        }

        /// <summary>A background's own alpha times this window's opacity. Does not alter the shared palette.</summary>
        public PaletteColor Background(PaletteColor color)
        {
            return color.WithAlpha(color.A * percent / 100f);
        }

        /// <summary>
        /// The strip background excluding the inset chip rectangles. Reuse the caller's list
        /// to avoid per-event allocations. Each chip paints its own background into its hole.
        /// Placements come from StatusStrip, with chips ordered left to right and not overlapping.
        /// </summary>
        public static void StripBackground(ScreenRect strip, IList<StatusStrip.Placement> parts, float inset, List<ScreenRect> rectangles)
        {
            rectangles.Clear();
            inset = Math.Max(0, Math.Min(inset, strip.Height / 2));
            float left = strip.X;
            for (int i = 0; i < parts.Count; ++i)
            {
                StatusStrip.Placement part = parts[i];
                if (part.Part != StatusStrip.Part.Chip) continue;
                rectangles.Add(new ScreenRect(left, strip.Y, Math.Max(0, part.X - left), strip.Height));
                rectangles.Add(new ScreenRect(part.X, strip.Y, part.Width, inset));
                rectangles.Add(new ScreenRect(part.X, strip.Y + strip.Height - inset, part.Width, inset));
                left = part.X + part.Width;
            }
            rectangles.Add(new ScreenRect(left, strip.Y, Math.Max(0, strip.X + strip.Width - left), strip.Height));
        }

        /// <summary>
        /// Reverse screen and visual beep used to overlay the text color, at text brightness,
        /// on an opaque body. Compose those colors first, then apply opacity once; otherwise
        /// the second translucent fill would make the body more opaque than requested.
        /// </summary>
        public PaletteColor Body(bool reversed, double brightness)
        {
            PaletteColor body = TerminalPalette.Background;
            if (reversed)
            {
                float alpha = double.IsNaN(brightness) ? 0 : (float)Math.Max(0, Math.Min(1, brightness));
                body = TerminalPalette.Text.WithAlpha(alpha).Over(body);
            }
            return Background(body);
        }
    }
}
