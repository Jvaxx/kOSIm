using System;
using kOS.Safe.UserIO;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// Keeps a window on the game's screen (Phase 4, step 2): wholly after a restore from a
    /// saved geometry, a change of resolution or UI scale and a resize; while it is moved, only
    /// enough of its header to move it back (DRAG-01, <see cref="WindowKeeper"/>).
    /// </summary>
    public static class WindowPlacement
    {
        /// <summary>
        /// The window moved, as little as possible, so that it is wholly on a screen of the
        /// given size. Along an axis where it is larger than the screen it is put at the
        /// screen's edge (left, top), which keeps its header and title on screen; its size is
        /// never changed, so a script's <c>TERMINAL:WIDTH</c> and <c>HEIGHT</c> stay as they were.
        /// A screen size that is not positive (not known yet) leaves the window where it is.
        /// </summary>
        public static ScreenRect KeepOnScreen(ScreenRect window, float screenWidth, float screenHeight)
        {
            return new ScreenRect(Along(window.X, window.Width, screenWidth), Along(window.Y, window.Height, screenHeight),
                window.Width, window.Height);
        }

        /// <summary>At a UI scale of 1, how much of the header's draggable part a window moved partly off screen keeps on it (DRAG-01).</summary>
        public const int BASE_REACHABLE_HEADER = 48;

        /// <summary>
        /// The window moved, as little as possible, so that it can still be moved back by its
        /// header (DRAG-01): its header wholly on screen vertically (not above the top, not
        /// below the bottom), and at least <paramref name="reachable"/> pixels of the header's
        /// draggable part on screen horizontally. Pushed off the left, the header's buttons (from
        /// <paramref name="buttonsLeft"/>, in the window, to its right edge) stay on screen too,
        /// as they do not move the window. Its size is never changed. A screen size that is not
        /// positive (not known yet) leaves the window where it is.
        /// </summary>
        public static ScreenRect KeepReachable(ScreenRect window, float buttonsLeft, float headerHeight, float reachable,
            float screenWidth, float screenHeight)
        {
            float x = window.X, y = window.Y;
            if (screenWidth > 0 && !float.IsInfinity(screenWidth))
            {
                if (float.IsNaN(x) || float.IsInfinity(x)) x = 0;
                float buttons = Math.Max(0, window.Width - Math.Max(0, buttonsLeft));
                float lowest = Math.Min(0, reachable + buttons - window.Width);
                float highest = Math.Max(lowest, screenWidth - reachable);
                x = Math.Max(lowest, Math.Min(x, highest));
            }
            if (screenHeight > 0 && !float.IsInfinity(screenHeight))
            {
                if (float.IsNaN(y) || float.IsInfinity(y)) y = 0;
                y = Math.Max(0, Math.Min(y, screenHeight - headerHeight));
            }
            return new ScreenRect(x, y, window.Width, window.Height);
        }

        /// <summary>
        /// A new window <paramref name="width"/> by <paramref name="height"/> next to
        /// <paramref name="anchor"/> (the editor by its terminal, step 5): below it, else above,
        /// right of it, left of it, the first side with room for the whole window, aligned with
        /// the anchor's left or top edge and kept on screen; with no room on any side, below it
        /// kept on screen (over the anchor).
        /// </summary>
        public static ScreenRect BelowOrBeside(ScreenRect anchor, float width, float height, float screenWidth, float screenHeight)
        {
            return Near(anchor, width, height, screenWidth, screenHeight, Side.Below, Side.Above, Side.Right, Side.Left);
        }

        /// <summary>
        /// As <see cref="BelowOrBeside"/>, trying right of the anchor, left of it, below, then
        /// above (a list picker by the toolbar panel, step 5).
        /// </summary>
        public static ScreenRect Beside(ScreenRect anchor, float width, float height, float screenWidth, float screenHeight)
        {
            return Near(anchor, width, height, screenWidth, screenHeight, Side.Right, Side.Left, Side.Below, Side.Above);
        }

        /// <summary>A window centered over <paramref name="anchor"/> (a confirmation over the editor, the paste box over the terminal), kept on screen.</summary>
        public static ScreenRect CenteredOver(ScreenRect anchor, float width, float height, float screenWidth, float screenHeight)
        {
            var centered = new ScreenRect((float)Math.Round(anchor.X + (anchor.Width - width) / 2f),
                (float)Math.Round(anchor.Y + (anchor.Height - height) / 2f), width, height);
            return KeepOnScreen(centered, screenWidth, screenHeight);
        }

        private enum Side
        {
            Below,
            Above,
            Right,
            Left
        }

        private static ScreenRect Near(ScreenRect anchor, float width, float height, float screenWidth, float screenHeight, params Side[] order)
        {
            bool known = screenWidth > 0 && screenHeight > 0 && !float.IsInfinity(screenWidth) && !float.IsInfinity(screenHeight);
            foreach (Side side in order)
            {
                ScreenRect candidate = On(side, anchor, width, height);
                if (!known || Fits(side, candidate, screenWidth, screenHeight))
                    return KeepOnScreen(candidate, screenWidth, screenHeight);
            }
            return KeepOnScreen(On(order[0], anchor, width, height), screenWidth, screenHeight);
        }

        private static ScreenRect On(Side side, ScreenRect anchor, float width, float height)
        {
            switch (side)
            {
                case Side.Below: return new ScreenRect(anchor.X, anchor.Y + anchor.Height, width, height);
                case Side.Above: return new ScreenRect(anchor.X, anchor.Y - height, width, height);
                case Side.Right: return new ScreenRect(anchor.X + anchor.Width, anchor.Y, width, height);
                default: return new ScreenRect(anchor.X - width, anchor.Y, width, height);
            }
        }

        /// <summary>Whether the window has room on that side: along the side's axis it is wholly on screen (the other axis is moved on).</summary>
        private static bool Fits(Side side, ScreenRect candidate, float screenWidth, float screenHeight)
        {
            bool vertical = side == Side.Below || side == Side.Above;
            float position = vertical ? candidate.Y : candidate.X;
            float size = vertical ? candidate.Height : candidate.Width;
            float other = vertical ? candidate.Width : candidate.Height;
            float screen = vertical ? screenHeight : screenWidth;
            float otherScreen = vertical ? screenWidth : screenHeight;
            return position >= 0 && position + size <= screen && other <= otherScreen;
        }

        private static float Along(float position, float size, float screen)
        {
            if (!(screen > 0) || float.IsInfinity(screen)) return position;
            if (float.IsNaN(position) || float.IsInfinity(position)) return 0;
            if (size >= screen) return 0;
            return Math.Max(0, Math.Min(position, screen - size));
        }
    }
}
