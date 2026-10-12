using System;

namespace kOS.Safe.UserIO
{
    /// <summary>
    /// Text layout for the telnet welcome menu. The width comes from the client, so any
    /// width (including 1) must be safe.
    /// </summary>
    public static class TelnetMenuText
    {
        /// <summary>
        /// For writing out a message to the screen, trying to center it according to
        /// what the telnet client claimed its current width is.
        /// </summary>
        /// <param name="msg">message to center</param>
        /// <param name="padChar">character to pad with. ' ' for space, '=' or '-' to draw a line across.</param>
        /// <param name="width">client width</param>
        /// <returns>the padded version of the string</returns>
        public static string CenterPadded(string msg, char padChar, int width)
        {
            int halfPadWidth = Math.Max(0, (width - msg.Length)/2); // the client may be narrower than the message
            string padString = new String(padChar, halfPadWidth);

            return padString + msg + (padChar == ' ' ? "" : padString);
        }

        /// <summary>
        /// Break up a potentially long line so it only breaks on spaces and not
        /// in the middle of a word. A word longer than the width is left unbroken.
        /// </summary>
        /// <param name="msg">string to attempt to print.</param>
        /// <param name="width">client width</param>
        /// <returns>new string that has the line breaks added as UnicodeCommand.STARTNEXTLINE</returns>
        public static string WordBreak(string msg, int width)
        {
            char[] msgAsArray = msg.ToCharArray();

            int lineStartPos = 0;
            int lastSpacePos = -1;
            for (int i = 0 ; i < msgAsArray.Length ; ++i)
            {
                if (msgAsArray[i] == ' ')
                    lastSpacePos = i;
                // Without a space on the current line there is nowhere to break (previously this
                // indexed position -1 when the first word was wider than the client):
                if ( (i - lineStartPos) >= width && lastSpacePos >= lineStartPos)
                {
                    msgAsArray[lastSpacePos] = (char)UnicodeCommand.STARTNEXTLINE;
                    lineStartPos = lastSpacePos+1;
                }
            }
            return new string(msgAsArray);
        }
    }
}
