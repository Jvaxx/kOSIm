using System;
using kOS.Safe.Encapsulation;
using kOS.Safe.UserIO;

namespace kOS.Safe.Screen
{
    /// <summary>The edges of a window a resize drag moves.</summary>
    [Flags]
    public enum ResizeEdges
    {
        None = 0,
        Left = 1,
        Right = 2,
        Top = 4,
        Bottom = 8
    }

    /// <summary>The window a resize drag gives, and the cells that fit it.</summary>
    public struct ResizeResult
    {
        public readonly ScreenRect Window;
        public readonly int Columns;
        public readonly int Rows;

        public ResizeResult(ScreenRect window, int columns, int rows)
        {
            Window = window;
            Columns = columns;
            Rows = rows;
        }
    }

    /// <summary>
    /// Resizing the GUI terminal by an edge, a corner or the grip (Phase 4, step 2), snapped to
    /// whole cells, in window coordinates of a <see cref="TerminalChromeLayout"/>.
    /// </summary>
    public static class TerminalResize
    {
        /// <summary>
        /// The edges a press at (<paramref name="x"/>, <paramref name="y"/>) grabs: the grip is the
        /// bottom-right corner; within <see cref="TerminalChromeLayout.ResizeBand"/> of the frame an
        /// edge, and a corner within <see cref="TerminalChromeLayout.ResizeCorner"/> of one. The
        /// header buttons keep their clicks (so the top-right corner does not resize), and while
        /// the scroll bar shows, the right edge beside its track is the scroll bar's.
        /// </summary>
        public static ResizeEdges EdgesAt(TerminalChromeLayout layout, float width, float height, float x, float y, bool scrollBarShown)
        {
            if (layout == null) throw new ArgumentNullException("layout");
            if (!(x >= 0 && y >= 0 && x < width && y < height)) return ResizeEdges.None;
            if (layout.Grip(width, height).Contains(x, y)) return ResizeEdges.Right | ResizeEdges.Bottom;
            if (layout.HeaderButtons(width).Contains(x, y)) return ResizeEdges.None;

            int band = layout.ResizeBand;
            bool left = x < band, right = x >= width - band, top = y < band, bottom = y >= height - band;
            if (right && !top && !bottom && scrollBarShown)
            {
                ScreenRect track = layout.ScrollTrack(width, height);
                if (y >= track.Y && y < track.Y + track.Height)
                    right = false;
            }
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
        /// <paramref name="dy"/>) from <paramref name="start"/>: the nearest whole number of cells,
        /// between the script minimum (15 × 3) and maximum, and no more than fit between the
        /// edge that stays and the screen's edge (a window already larger keeps its size, Q2).
        /// A left or top edge moves the window so that the opposite edge stays put. An axis
        /// that is not dragged keeps its size and cells.
        /// </summary>
        /// <param name="screenWidth">the game's screen; not positive when not known (no limit)</param>
        public static ResizeResult Drag(TerminalChromeLayout layout, ScreenRect start, int startColumns, int startRows,
            ResizeEdges edges, float dx, float dy, float screenWidth, float screenHeight)
        {
            if (layout == null) throw new ArgumentNullException("layout");
            if (float.IsNaN(dx) || float.IsInfinity(dx)) dx = 0;
            if (float.IsNaN(dy) || float.IsInfinity(dy)) dy = 0;

            float x = start.X, width = start.Width;
            int columns = startColumns;
            if ((edges & (ResizeEdges.Left | ResizeEdges.Right)) != 0 && layout.CellWidth > 0)
            {
                bool fromLeft = (edges & ResizeEdges.Left) != 0;
                float wanted = fromLeft ? start.Width - dx : start.Width + dx;
                int fitRoom = screenWidth > 0
                    ? layout.ColumnsThatFit(fromLeft ? start.X + start.Width : screenWidth - start.X)
                    : TerminalStruct.MAXCOLUMNS;
                columns = NearestCells(wanted - layout.Padding - layout.GutterWidth, layout.CellWidth,
                    TerminalStruct.MINCOLUMNS, TerminalStruct.MAXCOLUMNS, fitRoom, startColumns);
                width = layout.WindowWidthFor(columns);
                // A window widened to the chrome's minimum width holds more cells than asked:
                columns = Math.Min(TerminalStruct.MAXCOLUMNS, Math.Max(columns, layout.ColumnsThatFit(width)));
                if (fromLeft)
                    x = start.X + start.Width - width;
            }

            float y = start.Y, height = start.Height;
            int rows = startRows;
            if ((edges & (ResizeEdges.Top | ResizeEdges.Bottom)) != 0 && layout.CellHeight > 0)
            {
                bool fromTop = (edges & ResizeEdges.Top) != 0;
                float wanted = fromTop ? start.Height - dy : start.Height + dy;
                int fitRoom = screenHeight > 0
                    ? layout.RowsThatFit(fromTop ? start.Y + start.Height : screenHeight - start.Y)
                    : TerminalStruct.MAXROWS;
                rows = NearestCells(wanted - layout.HeaderHeight - layout.StripHeight - 2 * layout.Padding, layout.CellHeight,
                    TerminalStruct.MINROWS, TerminalStruct.MAXROWS, fitRoom, startRows);
                height = layout.WindowHeightFor(rows);
                if (fromTop)
                    y = start.Y + start.Height - height;
            }
            return new ResizeResult(new ScreenRect(x, y, width, height), columns, rows);
        }

        private static int NearestCells(float pixels, int cell, int minimum, int maximum, int fitRoom, int current)
        {
            int upper = Math.Max(minimum, Math.Min(maximum, Math.Max(fitRoom, current)));
            double cells = Math.Round(pixels / (double)cell, MidpointRounding.AwayFromZero);
            if (cells < minimum) return minimum;
            if (cells > upper) return upper;
            return (int)cells;
        }
    }
}
