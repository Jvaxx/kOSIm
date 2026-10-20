using System;
using kOS.Safe.UserIO;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// The GUI terminal's scroll bar (Phase 4, step 2), in the gutter the
    /// <see cref="TerminalChromeLayout"/> keeps for it. It shows only while the view is
    /// scrolled back (or its thumb is being dragged). The track stands for the scrollback and
    /// the screen; the thumb for the rows in view, at the bottom when the view shows the
    /// screen. Moving the thumb moves the shared view, as the wheel and PageUp/PageDown do.
    /// </summary>
    public static class TerminalScrollBar
    {
        public static bool IsShown(int viewOffset, bool dragging)
        {
            return viewOffset > 0 || dragging;
        }

        /// <summary>The thumb for a view <paramref name="viewOffset"/> rows above the screen.</summary>
        /// <param name="scrollbackRows">lines above the screen</param>
        /// <param name="rows">the screen's rows</param>
        /// <param name="minimumLength">the thumb's least length, unless the track is shorter</param>
        public static ScreenRect Thumb(ScreenRect track, int scrollbackRows, int rows, int viewOffset, float minimumLength)
        {
            int scrollback = Math.Max(0, scrollbackRows);
            float length = ThumbLength(track, scrollback, rows, minimumLength);
            float range = track.Height - length;
            int offset = Math.Max(0, Math.Min(viewOffset, scrollback));
            float fraction = scrollback == 0 ? 1f : (float)(scrollback - offset) / scrollback;
            return new ScreenRect(track.X, track.Y + range * fraction, track.Width, length);
        }

        /// <summary>
        /// The view offset that puts the thumb's top nearest <paramref name="thumbTop"/>, within
        /// the scrollback; <paramref name="currentOffset"/> when the thumb fills the track and
        /// cannot move.
        /// </summary>
        public static int ViewOffsetFor(ScreenRect track, int scrollbackRows, int rows, float minimumLength,
            float thumbTop, int currentOffset)
        {
            int scrollback = Math.Max(0, scrollbackRows);
            float range = track.Height - ThumbLength(track, scrollback, rows, minimumLength);
            if (scrollback == 0) return 0;
            if (!(range > 0) || float.IsNaN(thumbTop)) return currentOffset;
            double fraction = Math.Max(0.0, Math.Min(1.0, (thumbTop - track.Y) / (double)range));
            int firstLine = (int)Math.Round(fraction * scrollback, MidpointRounding.AwayFromZero);
            return scrollback - firstLine;
        }

        /// <summary>Where a press at height <paramref name="y"/> is: -1 above the thumb, 1 below it, 0 on it.</summary>
        public static int SideOfThumb(ScreenRect thumb, float y)
        {
            if (y < thumb.Y) return -1;
            if (y >= thumb.Y + thumb.Height) return 1;
            return 0;
        }

        private static float ThumbLength(ScreenRect track, int scrollback, int rows, float minimumLength)
        {
            if (!(track.Height > 0)) return 0;
            int shown = Math.Max(1, rows);
            float proportional = track.Height * shown / (float)(scrollback + shown);
            return Math.Min(track.Height, Math.Max(minimumLength, proportional));
        }
    }
}
