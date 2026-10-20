using System;
using System.Collections.Generic;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// Lays out the GUI terminal's one-line status strip (Phase 4, step 1): the state on the
    /// left after its dot, then the chips (<see cref="TerminalStatus.Chips"/>); on the right
    /// the size, with a copy notice before it while one shows. Nothing overlaps (BAR-01: the
    /// telnet label covered the state). When the room is short, in this order:
    /// <list type="number">
    /// <item>chips are dropped, lowest priority first (the rightmost of equals first);</item>
    /// <item>the state text is shortened, ending with an ellipsis;</item>
    /// <item>the notice is dropped, then the size;</item>
    /// <item>the state text goes, then the dot.</item>
    /// </list>
    /// Text widths come from a measurer (the font's, in the game). No Unity state.
    /// </summary>
    public sealed class StatusStrip
    {
        public enum Part
        {
            /// <summary>The dot and the state text (<see cref="Placement.Text"/> may be shortened or empty).</summary>
            State,
            Chip,
            Notice,
            Size
        }

        /// <summary>A part that fits, at <see cref="X"/> (as the content rectangle's X counts) and <see cref="Width"/> wide.</summary>
        public struct Placement
        {
            public Part Part;
            /// <summary>For a chip, its index in the chips given.</summary>
            public int ChipIndex;
            public string Text;
            public float X;
            public float Width;
        }

        private readonly Func<string, float> measure;
        private readonly float gap;
        private readonly float chipPadding;
        private readonly float dotWidth;
        private readonly string ellipsis;

        /// <param name="measure">a text's width in pixels</param>
        /// <param name="gap">between two parts</param>
        /// <param name="chipPadding">left and right of a chip's text, inside its background</param>
        /// <param name="dotWidth">the state's dot with the space after it</param>
        /// <param name="ellipsis">ends a shortened text ("…", or "..." for a font without it)</param>
        public StatusStrip(Func<string, float> measure, float gap, float chipPadding, float dotWidth, string ellipsis)
        {
            this.measure = measure;
            this.gap = gap;
            this.chipPadding = chipPadding;
            this.dotWidth = dotWidth;
            this.ellipsis = ellipsis;
        }

        /// <param name="x">the content rectangle's left edge</param>
        /// <param name="width">its width</param>
        /// <param name="state">the state text, or null for no state (no dot either)</param>
        /// <param name="chips">in the order shown</param>
        /// <param name="notice">a copy notice, or null</param>
        /// <param name="size">the size text, or null</param>
        public List<Placement> Layout(float x, float width, string state, IList<StatusChip> chips, string notice, string size)
        {
            var keptChips = new List<int>();
            if (chips != null)
                for (int i = 0; i < chips.Count; ++i)
                    keptChips.Add(i);
            bool keepNotice = !string.IsNullOrEmpty(notice);
            bool keepSize = !string.IsNullOrEmpty(size);
            bool keepDot = state != null;
            string stateText = state ?? string.Empty;

            while (true)
            {
                // Everything kept but the state text, gaps included:
                int parts = keptChips.Count + (keepNotice ? 1 : 0) + (keepSize ? 1 : 0) + (keepDot ? 1 : 0);
                float fixedWidth = parts > 1 ? gap * (parts - 1) : 0;
                foreach (int i in keptChips)
                    fixedWidth += ChipWidth(chips[i].Text);
                if (keepNotice) fixedWidth += measure(notice);
                if (keepSize) fixedWidth += measure(size);
                if (keepDot) fixedWidth += dotWidth;
                float textRoom = width - fixedWidth;

                if (stateText.Length == 0 ? textRoom >= 0 : measure(stateText) <= textRoom)
                    break;
                if (stateText.Length > 0 && keptChips.Count == 0)
                {
                    string shortened = Shorten(stateText, textRoom, measure, ellipsis);
                    if (shortened.Length > 0)
                    {
                        stateText = shortened;
                        break;
                    }
                }

                // Give up the next part, as the summary orders them:
                if (keptChips.Count > 0)
                    keptChips.Remove(LowestPriority(chips, keptChips));
                else if (keepNotice)
                    keepNotice = false;
                else if (keepSize)
                    keepSize = false;
                else if (stateText.Length > 0)
                    stateText = string.Empty;
                else if (keepDot)
                    keepDot = false;
                else
                    break; // nothing left
            }

            var placed = new List<Placement>();
            float left = x;
            if (keepDot)
            {
                float w = dotWidth + (stateText.Length > 0 ? measure(stateText) : 0);
                placed.Add(new Placement { Part = Part.State, ChipIndex = -1, Text = stateText, X = left, Width = w });
                left += w + gap;
            }
            foreach (int i in keptChips)
            {
                float w = ChipWidth(chips[i].Text);
                placed.Add(new Placement { Part = Part.Chip, ChipIndex = i, Text = chips[i].Text, X = left, Width = w });
                left += w + gap;
            }
            float right = x + width;
            if (keepSize)
            {
                float w = measure(size);
                right -= w;
                placed.Add(new Placement { Part = Part.Size, ChipIndex = -1, Text = size, X = right, Width = w });
                right -= gap;
            }
            if (keepNotice)
            {
                float w = measure(notice);
                right -= w;
                placed.Add(new Placement { Part = Part.Notice, ChipIndex = -1, Text = notice, X = right, Width = w });
            }
            return placed;
        }

        private float ChipWidth(string text)
        {
            return measure(text) + 2 * chipPadding;
        }

        private static int LowestPriority(IList<StatusChip> chips, List<int> kept)
        {
            int lowest = kept[kept.Count - 1];
            for (int k = kept.Count - 2; k >= 0; --k)
                if (chips[kept[k]].Priority < chips[lowest].Priority)
                    lowest = kept[k];
            return lowest;
        }

        /// <summary>
        /// <paramref name="text"/> if it fits <paramref name="room"/>, else its longest start
        /// that fits with <paramref name="ellipsis"/> after it (trailing spaces dropped, a
        /// surrogate pair kept whole), else the empty string.
        /// </summary>
        public static string Shorten(string text, float room, Func<string, float> measure, string ellipsis)
        {
            if (string.IsNullOrEmpty(text) || measure(text) <= room) return text ?? string.Empty;
            // The longest start that fits: widths grow with the length, so search for it.
            int fits = 0, low = 1, high = text.Length - 1;
            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                if (measure(Start(text, middle) + ellipsis) <= room)
                {
                    fits = middle;
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }
            if (fits == 0) return string.Empty;
            string start = Start(text, fits);
            return start.Length == 0 ? string.Empty : start + ellipsis;
        }

        private static string Start(string text, int length)
        {
            if (length > 0 && char.IsHighSurrogate(text[length - 1]))
                --length;
            return text.Substring(0, length).TrimEnd(' ');
        }
    }
}
