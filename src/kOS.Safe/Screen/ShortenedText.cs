using System;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// One text as <see cref="StatusStrip.Shorten"/> fits it, kept while the text, the room
    /// and the measures stay the same (review P-07: the terminal's header title, drawn on
    /// every IMGUI event; a title that does not fit was shortened by a search making a
    /// string per step, each time). No Unity state.
    /// </summary>
    public sealed class ShortenedText
    {
        private string text;
        private float room;
        private object measureKey;
        private string ellipsis;
        private string shown;

        /// <summary>How many times the text was fitted so far, for the tests.</summary>
        internal int Fitted { get; private set; }

        /// <param name="text">the text</param>
        /// <param name="room">the width it must fit</param>
        /// <param name="measureKey">
        /// what <paramref name="measure"/> measures with (the style, in the game): another key
        /// means other widths. The measurer is used only during this call.
        /// </param>
        /// <param name="measure">a text's width in pixels</param>
        /// <param name="ellipsis">ends a shortened text</param>
        /// <returns>what <see cref="StatusStrip.Shorten"/> gives for them</returns>
        public string Get(string text, float room, object measureKey, Func<string, float> measure, string ellipsis)
        {
            if (shown == null || text != this.text || room != this.room || !ReferenceEquals(measureKey, this.measureKey) ||
                ellipsis != this.ellipsis)
            {
                shown = StatusStrip.Shorten(text, room, measure, ellipsis);
                this.text = text;
                this.room = room;
                this.measureKey = measureKey;
                this.ellipsis = ellipsis;
                ++Fitted;
            }
            return shown;
        }
    }
}
