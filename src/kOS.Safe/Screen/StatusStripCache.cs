using System;
using System.Collections.Generic;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// The GUI terminal's status strip as last laid out, kept while nothing it shows changes
    /// (review P-07). The window draws the strip on every IMGUI event (several a frame); it
    /// made a <see cref="StatusStrip"/> with its measurer, the state text, the chips and the
    /// size text, and laid them out, each time. Here:
    /// <list type="bullet">
    /// <item>the state text and the chips are read once per status: a changed status is a
    /// new <see cref="TerminalStatus"/> (as the window makes one each refresh), and one
    /// showing the same as the last keeps the last's chips;</item>
    /// <item>the size text is made when the size changes;</item>
    /// <item>the layout is made again only when the strip's measures (the measurer's key,
    /// the gaps, the dot, the ellipsis), its place, the state, the chips, the notice or the
    /// size changed.</item>
    /// </list>
    /// The placements returned, <see cref="Chips"/> and the texts are the cache's own: read
    /// them, do not change them. No Unity state.
    /// </summary>
    public sealed class StatusStripCache
    {
        private StatusStrip strip;
        private object measureKey;
        private float gap, chipPadding, dotWidth;
        private string ellipsis;

        private bool statusRead;
        private TerminalStatus status;
        private string stateText;
        private List<StatusChip> chips;

        private int columns = -1, rows = -1;
        private string size;

        private float x, width;
        private string notice;

        private List<StatusStrip.Placement> placed;

        /// <summary>The state text laid out, or null for no state.</summary>
        public string StateText { get { return stateText; } }

        /// <summary>The chips laid out (a placement's <see cref="StatusStrip.Placement.ChipIndex"/> counts in them), or null.</summary>
        public List<StatusChip> Chips { get { return chips; } }

        /// <summary>The size text laid out, <c>columns</c>x<c>rows</c>.</summary>
        public string Size { get { return size; } }

        /// <summary>How many layouts were made so far, for the tests.</summary>
        internal int LayoutsMade { get; private set; }

        /// <param name="measureKey">
        /// what <paramref name="measure"/> measures with (the style, in the game): another key
        /// means other widths. The measurer is used only while laying out, during this call.
        /// </param>
        /// <param name="measure">a text's width in pixels</param>
        /// <param name="gap">as <see cref="StatusStrip"/>'s</param>
        /// <param name="chipPadding">as <see cref="StatusStrip"/>'s</param>
        /// <param name="dotWidth">as <see cref="StatusStrip"/>'s</param>
        /// <param name="ellipsis">as <see cref="StatusStrip"/>'s</param>
        /// <param name="x">as <see cref="StatusStrip.Layout"/>'s</param>
        /// <param name="width">as <see cref="StatusStrip.Layout"/>'s</param>
        /// <param name="status">the CPU state, or null for none (no dot either)</param>
        /// <param name="notice">a copy notice, or null</param>
        /// <param name="columns">the screen's columns</param>
        /// <param name="rows">the screen's rows</param>
        /// <returns>what <see cref="StatusStrip.Layout"/> gives for them</returns>
        public List<StatusStrip.Placement> Layout(object measureKey, Func<string, float> measure, float gap, float chipPadding,
            float dotWidth, string ellipsis, float x, float width, TerminalStatus status, string notice, int columns, int rows)
        {
            bool changed = placed == null;
            if (strip == null || !ReferenceEquals(measureKey, this.measureKey) || gap != this.gap ||
                chipPadding != this.chipPadding || dotWidth != this.dotWidth || ellipsis != this.ellipsis)
            {
                strip = new StatusStrip(measure, gap, chipPadding, dotWidth, ellipsis);
                this.measureKey = measureKey;
                this.gap = gap;
                this.chipPadding = chipPadding;
                this.dotWidth = dotWidth;
                this.ellipsis = ellipsis;
                changed = true;
            }

            if (!statusRead || !ReferenceEquals(status, this.status))
            {
                statusRead = true;
                this.status = status;
                string newState = status == null ? null : status.StateText;
                if (newState != stateText)
                {
                    stateText = newState;
                    changed = true;
                }
                List<StatusChip> newChips = status == null ? null : status.Chips;
                if (!SameChips(newChips, chips))
                {
                    chips = newChips;
                    changed = true;
                }
            }

            if (columns != this.columns || rows != this.rows || size == null)
            {
                this.columns = columns;
                this.rows = rows;
                size = columns + "x" + rows;
                changed = true;
            }

            if (x != this.x || width != this.width || notice != this.notice)
            {
                this.x = x;
                this.width = width;
                this.notice = notice;
                changed = true;
            }

            if (changed)
            {
                placed = strip.Layout(x, width, stateText, chips, notice, size);
                ++LayoutsMade;
            }
            return placed;
        }

        private static bool SameChips(List<StatusChip> a, List<StatusChip> b)
        {
            if (a == null || b == null) return a == b;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; ++i)
                if (a[i].Kind != b[i].Kind || a[i].Text != b[i].Text)
                    return false;
            return true;
        }
    }
}
