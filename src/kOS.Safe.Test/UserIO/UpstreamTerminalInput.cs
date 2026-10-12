using System.Collections.Generic;
using kOS.Safe.UserIO;

namespace kOS.Safe.Test.UserIO
{
    /// <summary>
    /// The upstream mappers' input conversion (<c>TerminalUnicodeMapper</c>,
    /// <c>TerminalVT100Mapper</c>, <c>TerminalXtermMapper</c> before the decoder), copied
    /// unchanged apart from being static, as an oracle. Each call converted one socket read
    /// on its own.
    /// </summary>
    internal static class UpstreamTerminalInput
    {
        private const char ESCAPE_CHARACTER = (char)0x1b;
        private const char BELL_CHAR = (char)0x07;
        private const char DELETE_CHARACTER = (char)0x7f;

        public static string Convert(TerminalInputDialect dialect, string text)
        {
            char[] chars = text.ToCharArray();
            switch (dialect)
            {
                case TerminalInputDialect.Xterm: return XtermInputConvert(chars);
                case TerminalInputDialect.VT100: return VT100InputConvert(chars);
                default: return BaseInputConvert(chars);
            }
        }

        private static string BaseInputConvert(char[] inChars)
        {
            List<char> outChars = new List<char>();
            char prevCh = '\0'; // dummy for first pass
            foreach (char ch in inChars)
            {
                switch (ch)
                {
                    case (char)0x08: outChars.Add((char)UnicodeCommand.DELETELEFT); break;
                    case (char)0x7f: outChars.Add((char)UnicodeCommand.DELETERIGHT); break;
                    case '\r':
                        if (prevCh != '\n')
                            outChars.Add((char)UnicodeCommand.STARTNEXTLINE);
                        break;
                    case '\n':
                        if (prevCh != '\r')
                            outChars.Add((char)UnicodeCommand.STARTNEXTLINE);
                        break;
                    case (char)0x0c:
                    case (char)0x12:
                        outChars.Add((char)UnicodeCommand.REQUESTREPAINT); break;
                    case (char)0x03: outChars.Add((char)UnicodeCommand.BREAK); break;
                    case (char)0x00: break;
                    default: outChars.Add(ch); break;
                }
                prevCh = ch;
            }
            return new string(outChars.ToArray());
        }

        private static string VT100InputConvert(char[] inChars)
        {
            List<char> outChars = new List<char>();
            for (int index = 0; index < inChars.Length; ++index)
            {
                switch (inChars[index])
                {
                    case ESCAPE_CHARACTER:
                        if (index + 1 < inChars.Length && inChars[index + 1] == '[')
                        {
                            int numConsumed;
                            char ch = ConvertVT100InputCSI(inChars, index + 2, out numConsumed);
                            if (numConsumed > 0)
                            {
                                outChars.Add(ch);
                                index += (1 + numConsumed);
                            }
                            // Not recognized: the ESC is dropped, '[' and the rest are typed.
                        }
                        else
                            outChars.Add(inChars[index]);
                        break;
                    case DELETE_CHARACTER: outChars.Add((char)UnicodeCommand.DELETELEFT); break;
                    case BELL_CHAR: outChars.Add((char)UnicodeCommand.BEEP); break;
                    default: outChars.Add(inChars[index]); break;
                }
            }
            return BaseInputConvert(outChars.ToArray());
        }

        private static string XtermInputConvert(char[] inChars)
        {
            List<char> outChars = new List<char>();
            for (int index = 0; index < inChars.Length; ++index)
            {
                switch (inChars[index])
                {
                    case (char)0x9b:
                        int numConsumed;
                        char ch = ConvertVT100InputCSI(inChars, index + 1, out numConsumed);
                        if (numConsumed > 0)
                        {
                            outChars.Add(ch);
                            index += numConsumed;
                        }
                        break;
                    default: outChars.Add(inChars[index]); break;
                }
            }
            return VT100InputConvert(outChars.ToArray());
        }

        private static char ConvertVT100InputCSI(char[] inChars, int offset, out int numConsumed)
        {
            char returnChar = '\0';
            numConsumed = 0;
            if (offset < inChars.Length)
            {
                switch (inChars[offset])
                {
                    case 'A': returnChar = (char)UnicodeCommand.UPCURSORONE; numConsumed = 1; break;
                    case 'B': returnChar = (char)UnicodeCommand.DOWNCURSORONE; numConsumed = 1; break;
                    case 'C': returnChar = (char)UnicodeCommand.RIGHTCURSORONE; numConsumed = 1; break;
                    case 'D': returnChar = (char)UnicodeCommand.LEFTCURSORONE; numConsumed = 1; break;
                    case 'H': returnChar = (char)UnicodeCommand.HOMECURSOR; numConsumed = 1; break;
                    case 'F': returnChar = (char)UnicodeCommand.ENDCURSOR; numConsumed = 1; break;
                    default: numConsumed = 0; break;
                }
            }
            if (offset + 1 < inChars.Length && inChars[offset + 1] == '~')
            {
                switch (inChars[offset])
                {
                    case '1': returnChar = (char)UnicodeCommand.HOMECURSOR; numConsumed = 2; break;
                    case '3': returnChar = (char)UnicodeCommand.DELETERIGHT; numConsumed = 2; break;
                    case '4': returnChar = (char)UnicodeCommand.ENDCURSOR; numConsumed = 2; break;
                    case '5': returnChar = (char)UnicodeCommand.PAGEUPCURSOR; numConsumed = 2; break;
                    case '6': returnChar = (char)UnicodeCommand.PAGEDOWNCURSOR; numConsumed = 2; break;
                    default: numConsumed = 0; break;
                }
            }
            return returnChar;
        }
    }
}
