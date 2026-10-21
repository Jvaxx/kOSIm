using System;
using kOS.Safe.UserIO;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// The opacity-only header context menu: placement and dismissal, with no Unity objects.
    /// Its opaque popup can extend beyond its terminal, but is kept on the game's screen.
    /// </summary>
    public sealed class TerminalOpacityMenu
    {
        private float anchorX, anchorY;
        private bool contextPressed;
        public bool IsOpen { get; private set; }
        public ScreenRect Bounds { get; private set; }
        public TerminalOpacityMenuLayout Layout { get; private set; }

        /// <summary>Only a right press on unused header space, never a preset/size/close button.</summary>
        public static bool CanOpen(TerminalChromeLayout chrome, float width, float x, float y, int button)
        {
            return button == 1 && chrome.Header(width).Contains(x, y) && !chrome.HeaderButtons(width).Contains(x, y);
        }

        /// <summary>A valid press is captured until the same button is released; left drags are untouched.</summary>
        public bool BeginContext(TerminalChromeLayout chrome, float width, float x, float y, int button)
        {
            if (!CanOpen(chrome, width, x, y, button)) return false;
            Close();
            contextPressed = true;
            return true;
        }

        /// <summary>A release over a button or outside the header cancels; an unrelated button's release does not complete the click.</summary>
        public bool EndContext(TerminalChromeLayout chrome, float width, float x, float y, int button)
        {
            if (button != 1 || !contextPressed) return false;
            contextPressed = false;
            return CanOpen(chrome, width, x, y, button);
        }

        public void Open(float x, float y, float scale, float screenWidth, float screenHeight)
        {
            anchorX = x;
            anchorY = y;
            IsOpen = true;
            Refresh(scale, screenWidth, screenHeight, true);
        }

        /// <summary>
        /// Refit after a resolution/UI-scale change. Not available when hidden, unpowered,
        /// unfocused, under a modal paste confirmation, or displaying a render fault.
        /// </summary>
        public void Refresh(float scale, float screenWidth, float screenHeight, bool available)
        {
            if (!available) Close();
            if (!IsOpen) return;
            scale = TerminalChromeLayout.ClampScale(scale);
            if (Layout == null || Layout.Scale != scale)
                Layout = new TerminalOpacityMenuLayout(scale);
            Bounds = WindowPlacement.KeepOnScreen(new ScreenRect(anchorX, anchorY, Layout.Width, Layout.Height), screenWidth, screenHeight);
        }

        public bool Contains(float x, float y)
        {
            return IsOpen && Bounds.Contains(x, y);
        }

        /// <summary>Dismiss on any outside press; the caller leaves that press available to its normal recipient.</summary>
        public void PointerDown(float x, float y)
        {
            if (!Contains(x, y)) Close();
        }

        /// <summary>True when Escape was taken by the menu, rather than the terminal or the game.</summary>
        public bool Escape()
        {
            bool taken = IsOpen;
            Close();
            return taken;
        }

        public void Close()
        {
            IsOpen = false;
            contextPressed = false;
        }
    }

    /// <summary>Local popup coordinates. Its dimensions follow KSP's UI scale, not character height.</summary>
    public sealed class TerminalOpacityMenuLayout
    {
        public float Scale { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Padding { get; private set; }
        public int FontSize { get; private set; }
        public int Border { get; private set; }
        public ScreenRect Caption { get; private set; }
        public ScreenRect Readout { get; private set; }
        public ScreenRect Slider { get; private set; }

        public TerminalOpacityMenuLayout(float scale)
        {
            Scale = TerminalChromeLayout.ClampScale(scale);
            Width = Scaled(220);
            Padding = Scaled(6);
            FontSize = Scaled(11);
            Border = Scaled(1);
            int row = Scaled(24);
            Height = 2 * Padding + 2 * row;
            int readoutWidth = Scaled(44);
            Caption = new ScreenRect(Padding, Padding, Width - 2 * Padding - readoutWidth, row);
            Readout = new ScreenRect(Width - Padding - readoutWidth, Padding, readoutWidth, row);
            Slider = new ScreenRect(Padding, Padding + row, Width - 2 * Padding, row);
        }

        private int Scaled(int value)
        {
            return Math.Max(1, (int)Math.Round(value * Scale, MidpointRounding.AwayFromZero));
        }
    }
}
