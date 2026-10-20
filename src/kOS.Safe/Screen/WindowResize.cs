using System;
using kOS.Safe.UserIO;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// Resizing a window that is not made of cells (the editor, Phase 4 step 5) by an edge, a
    /// corner or its grip, as the terminal is (<see cref="TerminalResize"/>) but to the pixel.
    /// Window coordinates have their origin at the window's top left.
    /// </summary>
    public static class WindowResize
    {
        /// <summary>
        /// The edges a press at (<paramref name="x"/>, <paramref name="y"/>) grabs: the grip is the
        /// bottom-right corner; within <see cref="FlatWindowLayout.ResizeBand"/> of the frame an
        /// edge, and a corner within <see cref="FlatWindowLayout.ResizeCorner"/> of one. A press
        /// on <paramref name="keep"/> (the header's buttons) is theirs.
        /// </summary>
        public static ResizeEdges EdgesAt(FlatWindowLayout layout, float width, float height, float x, float y, ScreenRect keep)
        {
            if (layout == null) throw new ArgumentNullException("layout");
            if (!(x >= 0 && y >= 0 && x < width && y < height)) return ResizeEdges.None;
            if (layout.Grip(width, height).Contains(x, y)) return ResizeEdges.Right | ResizeEdges.Bottom;
            if (keep.Contains(x, y)) return ResizeEdges.None;

            int band = layout.ResizeBand;
            bool left = x < band, right = x >= width - band, top = y < band, bottom = y >= height - band;
            if (!(left || right || top || bottom)) return ResizeEdges.None;

            int corner = layout.ResizeCorner;
            if ((left || right) && !(top || bottom))
            {
                top = y < corner;
                bottom = y >= height - corner;
            }
            else if ((top || bottom) && !(left || right))
            {
                left = x < corner;
                right = x >= width - corner;
            }
            return (left ? ResizeEdges.Left : 0) | (right ? ResizeEdges.Right : 0) |
                (top ? ResizeEdges.Top : 0) | (bottom ? ResizeEdges.Bottom : 0);
        }

        /// <summary>
        /// The window after its <paramref name="edges"/> moved by (<paramref name="dx"/>,
        /// <paramref name="dy"/>) from <paramref name="start"/>: at least the minimum, and no
        /// larger than reaches the screen's edge from the edge that stays (a window already
        /// larger keeps its size). A left or top edge moves the window so that the opposite edge
        /// stays put. Whole pixels.
        /// </summary>
        /// <param name="screenWidth">the game's screen; not positive when not known (no limit)</param>
        public static ScreenRect Drag(ScreenRect start, ResizeEdges edges, float dx, float dy,
            float minimumWidth, float minimumHeight, float screenWidth, float screenHeight)
        {
            if (float.IsNaN(dx) || float.IsInfinity(dx)) dx = 0;
            if (float.IsNaN(dy) || float.IsInfinity(dy)) dy = 0;

            float x = start.X, width = start.Width;
            if ((edges & (ResizeEdges.Left | ResizeEdges.Right)) != 0)
            {
                bool fromLeft = (edges & ResizeEdges.Left) != 0;
                float room = screenWidth > 0 ? (fromLeft ? start.X + start.Width : screenWidth - start.X) : float.MaxValue;
                width = Size(fromLeft ? start.Width - dx : start.Width + dx, minimumWidth, room, start.Width);
                if (fromLeft)
                    x = start.X + start.Width - width;
            }

            float y = start.Y, height = start.Height;
            if ((edges & (ResizeEdges.Top | ResizeEdges.Bottom)) != 0)
            {
                bool fromTop = (edges & ResizeEdges.Top) != 0;
                float room = screenHeight > 0 ? (fromTop ? start.Y + start.Height : screenHeight - start.Y) : float.MaxValue;
                height = Size(fromTop ? start.Height - dy : start.Height + dy, minimumHeight, room, start.Height);
                if (fromTop)
                    y = start.Y + start.Height - height;
            }
            return new ScreenRect(x, y, width, height);
        }

        private static float Size(float wanted, float minimum, float room, float current)
        {
            float upper = Math.Max(minimum, Math.Max(room, current));
            return (float)Math.Round(Math.Max(minimum, Math.Min(upper, wanted)), MidpointRounding.AwayFromZero);
        }
    }
}
