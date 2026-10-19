using System.Text;

namespace kOS.Safe.UserIO
{
    /// <summary>
    /// Clipboard text made safe to type at the terminal (Phase 3, P3-2, TERM-01): CR LF,
    /// CR and LF each become one Enter (<c>'\r'</c>); tab is kept; every other control
    /// character, and every character from <see cref="UnicodeCommand.BREAK"/> (U+E000) up,
    /// which the terminal reads as a key code, is dropped. So pasted text can never break,
    /// detach, resize or repaint. A text over <see cref="MAX_CHARS"/> is refused whole,
    /// never cut. Queue it with <see cref="TerminalInputRouter{TClient}.QueuePaste"/>.
    /// </summary>
    public sealed class TerminalPaste
    {
        /// <summary>The longest clipboard text accepted, in UTF-16 characters (user decision 2026-10-19).</summary>
        public const int MAX_CHARS = 65536;

        /// <summary>The keys to type; empty when refused or nothing was left.</summary>
        public string Keys { get; private set; }

        /// <summary>How many Enters the keys hold: the commands the paste would run.</summary>
        public int EnterCount { get; private set; }

        /// <summary>How many characters were dropped as control characters or key codes.</summary>
        public int DroppedCount { get; private set; }

        /// <summary>The clipboard text's length, before normalizing.</summary>
        public int SourceLength { get; private set; }

        /// <summary>Over <see cref="MAX_CHARS"/>: nothing is typed.</summary>
        public bool IsTooLarge { get { return SourceLength > MAX_CHARS; } }

        public bool IsEmpty { get { return Keys.Length == 0; } }

        /// <summary>Whether the paste would run commands, so it should be confirmed first.</summary>
        public bool RunsCommands { get { return EnterCount > 0; } }

        private TerminalPaste()
        {
        }

        public static TerminalPaste Prepare(string text)
        {
            var paste = new TerminalPaste { Keys = string.Empty, SourceLength = text == null ? 0 : text.Length };
            if (text == null || paste.IsTooLarge)
                return paste;

            var keys = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; ++i)
            {
                char ch = text[i];
                if (ch == '\r' || ch == '\n')
                {
                    if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                        ++i; // CR LF is one line break
                    keys.Append('\r');
                    ++paste.EnterCount;
                }
                else if (ch == '\t' || IsTypeable(ch))
                {
                    keys.Append(ch);
                }
                else
                {
                    ++paste.DroppedCount;
                }
            }
            paste.Keys = keys.ToString();
            return paste;
        }

        // As TerminalInputRouter.IsSpecial (below U+0020, or U+E000 and up, is a key code), plus DEL and the C1 controls.
        private static bool IsTypeable(char ch)
        {
            return ch >= 0x20 && ch < (char)UnicodeCommand.BREAK && !(ch >= 0x7F && ch < 0xA0);
        }
    }
}
