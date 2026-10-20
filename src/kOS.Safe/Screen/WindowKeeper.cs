using kOS.Safe.UserIO;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// Which rule keeps a movable window (the terminal, the editor) on the screen at each draw
    /// (DRAG-01). The user may move it partly off screen by its header
    /// (<see cref="WindowPlacement.KeepReachable"/>); it is brought wholly on screen
    /// (<see cref="WindowPlacement.KeepOnScreen"/>) when it is opened or restored, when the
    /// resolution or the UI scale changes, and when its size changes other than by the user's
    /// edge drag (the character size, a script's <c>TERMINAL:WIDTH</c>, a telnet client). An
    /// edge drag already stops the dragged edge at the screen's edge
    /// (<see cref="TerminalResize"/>, <see cref="WindowResize"/>), and moving the window under
    /// the mouse would lose the edge it holds.
    /// </summary>
    public sealed class WindowKeeper
    {
        private bool fitWholly = true;
        private float lastScreenWidth = float.NaN;
        private float lastScreenHeight = float.NaN;
        private float lastScale = float.NaN;
        private float lastWidth = float.NaN;
        private float lastHeight = float.NaN;

        /// <summary>The next <see cref="Keep"/> brings the window wholly on screen: it was opened or restored.</summary>
        public void FitWhollyNext()
        {
            fitWholly = true;
        }

        /// <summary>The user's edge drag gave the window this size: not a reason to move it.</summary>
        public void SizedByUser(ScreenRect window)
        {
            lastWidth = window.Width;
            lastHeight = window.Height;
        }

        /// <summary>
        /// The window kept on a screen of the given size at the given UI scale; the other
        /// arguments are those of <see cref="WindowPlacement.KeepReachable"/>.
        /// </summary>
        public ScreenRect Keep(ScreenRect window, float buttonsLeft, float headerHeight, float reachable,
            float screenWidth, float screenHeight, float uiScale)
        {
            // Comparisons with the first NaNs are true:
            bool wholly = fitWholly || screenWidth != lastScreenWidth || screenHeight != lastScreenHeight || uiScale != lastScale ||
                window.Width != lastWidth || window.Height != lastHeight;
            ScreenRect kept = wholly
                ? WindowPlacement.KeepOnScreen(window, screenWidth, screenHeight)
                : WindowPlacement.KeepReachable(window, buttonsLeft, headerHeight, reachable, screenWidth, screenHeight);
            fitWholly = false;
            lastScreenWidth = screenWidth;
            lastScreenHeight = screenHeight;
            lastScale = uiScale;
            lastWidth = kept.Width;
            lastHeight = kept.Height;
            return kept;
        }
    }
}
