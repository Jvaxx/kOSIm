using System;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// When the editor's view follows its caret (EDIT-03, Phase 4 step 5). Upstream scrolled to
    /// the caret on every repaint, so the wheel and the scroll bar could not look away from it.
    /// Now the view moves to the caret only in a frame where the caret or the text changed
    /// (typing, arrows, a click, a page key, a new file); a wheel or scroll-bar move is kept
    /// until then, and a resize only clamps the scroll. Pixels from the text's top.
    /// </summary>
    public sealed class EditorScrollFollow
    {
        private int caret = -1;
        private string text;

        /// <summary>Forgets the caret and the text: the next <see cref="Follow"/> brings the caret into view (a file was loaded).</summary>
        public void Reset()
        {
            caret = -1;
            text = null;
        }

        /// <summary>
        /// The scroll for this frame, from <paramref name="scroll"/> (where the view is, the
        /// user's wheel or scroll bar included).
        /// </summary>
        /// <param name="caretIndex">the caret's index in <paramref name="currentText"/></param>
        /// <param name="caretTop">the top of the caret's line, from the text's top</param>
        /// <param name="lineHeight">the caret's line height</param>
        /// <param name="contentHeight">the whole text's height</param>
        /// <param name="viewHeight">the view's height</param>
        public float Follow(float scroll, int caretIndex, string currentText, float caretTop, float lineHeight,
            float contentHeight, float viewHeight)
        {
            bool moved = caretIndex != caret || !string.Equals(currentText, text, StringComparison.Ordinal);
            caret = caretIndex;
            text = currentText;
            if (moved)
                return PixelScroll.Reveal(scroll, caretTop, Math.Max(0, lineHeight), contentHeight, viewHeight);
            return PixelScroll.Clamp(scroll, contentHeight, viewHeight);
        }
    }

    /// <summary>A place in a text: its line and column, both from 1 (the editor's strip, Phase 4 step 5).</summary>
    public struct TextPosition
    {
        public readonly int Line;
        public readonly int Column;

        public TextPosition(int line, int column)
        {
            Line = line;
            Column = column;
        }

        /// <summary>
        /// The position of <paramref name="index"/> in <paramref name="text"/> (clamped to the
        /// text): lines end at <c>\n</c>, and a column counts characters (a tab is one).
        /// </summary>
        public static TextPosition Of(string text, int index)
        {
            if (text == null) text = string.Empty;
            index = Math.Max(0, Math.Min(text.Length, index));
            int line = 1, lineStart = 0;
            for (int i = 0; i < index; ++i)
                if (text[i] == '\n')
                {
                    ++line;
                    lineStart = i + 1;
                }
            return new TextPosition(line, index - lineStart + 1);
        }

        /// <summary>As the editor's strip shows it: <c>Ln 12, Col 4</c>.</summary>
        public override string ToString()
        {
            return "Ln " + Line + ", Col " + Column;
        }
    }
}
