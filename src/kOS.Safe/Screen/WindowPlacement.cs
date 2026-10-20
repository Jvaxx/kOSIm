using System;
using kOS.Safe.UserIO;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// Keeps a window on the game's screen (Phase 4, step 2): after a restore from a saved
    /// geometry, a change of resolution or UI scale, a resize, and while it is moved.
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

        private static float Along(float position, float size, float screen)
        {
            if (!(screen > 0) || float.IsInfinity(screen)) return position;
            if (float.IsNaN(position) || float.IsInfinity(position)) return 0;
            if (size >= screen) return 0;
            return Math.Max(0, Math.Min(position, screen - size));
        }
    }
}
