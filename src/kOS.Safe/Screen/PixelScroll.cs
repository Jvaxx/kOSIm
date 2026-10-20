using System;
using kOS.Safe.UserIO;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// A view scrolled by pixels over taller content, and the thin scroll bar beside it (Phase 4:
    /// the toolbar panel, the editor, the list pickers). Scrolls are pixels from the content's
    /// top; the thumb is as long as the share in view, at least a minimum, at most the track.
    /// </summary>
    public static class PixelScroll
    {
        public static float Maximum(float contentHeight, float viewHeight)
        {
            return NonNegative(contentHeight - viewHeight);
        }

        public static float Clamp(float scroll, float contentHeight, float viewHeight)
        {
            if (float.IsNaN(scroll) || float.IsInfinity(scroll)) return 0;
            return Math.Max(0, Math.Min(Maximum(contentHeight, viewHeight), scroll));
        }

        /// <summary>The thumb in <paramref name="track"/>, placed by the scroll.</summary>
        public static ScreenRect Thumb(ScreenRect track, float contentHeight, float viewHeight, float scroll, float minimumThumb)
        {
            float length = ThumbLength(track, contentHeight, viewHeight, minimumThumb);
            float range = track.Height - length;
            float maximum = Maximum(contentHeight, viewHeight);
            float fraction = maximum > 0 ? Clamp(scroll, contentHeight, viewHeight) / maximum : 0;
            return new ScreenRect(track.X, track.Y + (float)Math.Round(range * fraction), track.Width, length);
        }

        /// <summary>The scroll for a thumb whose top is at <paramref name="thumbTop"/> (the inverse of <see cref="Thumb"/>).</summary>
        public static float ScrollFor(float thumbTop, ScreenRect track, float contentHeight, float viewHeight, float minimumThumb)
        {
            float range = track.Height - ThumbLength(track, contentHeight, viewHeight, minimumThumb);
            if (!(range > 0)) return 0;
            float fraction = Math.Max(0, Math.Min(1, (thumbTop - track.Y) / range));
            return Clamp(fraction * Maximum(contentHeight, viewHeight), contentHeight, viewHeight);
        }

        private static float ThumbLength(ScreenRect track, float contentHeight, float viewHeight, float minimumThumb)
        {
            if (!(contentHeight > 0) || contentHeight <= viewHeight) return track.Height;
            float length = (float)Math.Round(track.Height * viewHeight / contentHeight);
            return Math.Min(track.Height, Math.Max(minimumThumb, length));
        }

        /// <summary>
        /// The scroll that shows the span from <paramref name="top"/>, <paramref name="height"/>
        /// high, moved as little as possible (a span taller than the view shows its top).
        /// </summary>
        public static float Reveal(float scroll, float top, float height, float contentHeight, float viewHeight)
        {
            if (top + height > scroll + viewHeight)
                scroll = top + height - viewHeight;
            if (top < scroll)
                scroll = top;
            return Clamp(scroll, contentHeight, viewHeight);
        }

        private static float NonNegative(float value)
        {
            return value > 0 ? value : 0;
        }
    }
}
