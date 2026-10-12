using System.Collections.Generic;
using System.Text;
using kOS.Safe.UserIO;

namespace kOS.UserIO
{
    /// <summary>
    /// Subclass of TerminalUnicodeMapper designed to handle the specifics of
    /// the xterm terminal control codes.
    /// <br/>
    /// Note that because the XTERM program was designed explicitly to attempt
    /// to emulate the already popular and existent VT100 hardware terminal, this
    /// class can mostly be just a subclass of the VT100 mapper that lets it
    /// do most of the heavy work.  It's only a separate class to support the
    /// few places where XTERM is more capable than VT100.
    /// </summary>
    public class TerminalXtermMapper : TerminalVT100Mapper
    {
        public TerminalXtermMapper(string typeString) : base(typeString)
        {
            TerminalTypeID = TerminalType.XTERM;
            AllowNativeUnicodeCommands = false;
        }

        private ExpectNextChar outputExpected = ExpectNextChar.NORMAL;
        private int pendingWidth;
        private StringBuilder pendingTitle;

        /// <summary>
        /// Map the Unicode chars (and the fake control codes we made) into what the
        /// terminal wants to see.
        /// Subclasses of this should perform their own manipulations, then fallthrough
        /// to this base class implementation at the bottom, to allow chains of
        /// subclasses to all operate on the data.
        /// </summary>
        /// <param name="str">Unicode char</param>
        /// <returns>raw byte stream to send to the terminal</returns>
        public override char[] OutputConvert(string str)
        {
            StringBuilder sb = new StringBuilder();

            foreach (char t in str)
            {
                switch (outputExpected)
                {
                    case ExpectNextChar.RESIZEWIDTH:
                        pendingWidth = t;
                        outputExpected = ExpectNextChar.RESIZEHEIGHT;
                        break;
                    case ExpectNextChar.RESIZEHEIGHT:
                        int height = t;
                        sb.AppendFormat("{0}8;{1};{2}t", CSI, height, pendingWidth);
                        outputExpected = ExpectNextChar.NORMAL;
                        break;
                    case ExpectNextChar.INTITLE:
                        if (t == (char)UnicodeCommand.TITLEEND)
                        {

                            sb.AppendFormat("{0}]2;{1}{2}", ESCAPE_CHARACTER , pendingTitle, BELL_CHAR);
                            pendingTitle = new StringBuilder();
                            outputExpected = ExpectNextChar.NORMAL;
                        }
                        else
                            pendingTitle.Append(t);
                        break;
                    default:
                        switch (t)
                        {
                            case (char)UnicodeCommand.RESIZESCREEN:
                                outputExpected = ExpectNextChar.RESIZEWIDTH;
                                break;
                            case (char)UnicodeCommand.TITLEBEGIN:
                                outputExpected = ExpectNextChar.INTITLE;
                                pendingTitle = new StringBuilder();
                                break;
                            case (char)UnicodeCommand.CLEARSCREEN:
                                sb.AppendFormat("{0}?47h", ESCAPE_CHARACTER);                   // <-- Tells xterm to use fixed-buffer mode, not saving in scrollback.
                                sb.AppendFormat("{0}2J{0}H", CSI);  // <-- The normal clear screen char from vt100.
                                break;
                            default: 
                                sb.Append(t); // default passhtrough
                                break;
                        }
                        break;
                }
            }
            return base.OutputConvert(sb.ToString());
        }
        
        protected override TerminalInputDialect InputDialect
        {
            get { return TerminalInputDialect.Xterm; }
        }

        /// <summary>
        /// xterm input (VT100 plus the 8-bit CSI character 0x9b), converted by the base
        /// class's <see cref="TerminalInputDecoder"/>. Kept as an override so code compiled
        /// against earlier versions still binds.
        /// </summary>
        /// <returns>input mapped into our internal pretend Unicode terminal's codes</returns>
        public override string InputConvert(char[] inChars)
        {
            return base.InputConvert(inChars);
        }
    }
}
