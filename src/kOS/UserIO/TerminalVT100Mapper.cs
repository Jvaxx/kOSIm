using kOS.Safe.UserIO;
using System.Collections.Generic;
using System.Text;
using System;

namespace kOS.UserIO
{
    /// <summary>
    /// Subclass of TerminalUnicodeMapper designed to handle the specifics of
    /// the vt100 terminal control codes.
    /// </summary>
    public class TerminalVT100Mapper : TerminalUnicodeMapper
    {
        protected const char ESCAPE_CHARACTER = (char)0x1b;
        protected const char BELL_CHAR = (char)0x07;
        protected const char DELETE_CHARACTER = (char)0x7f;
        private readonly string csi = string.Format("{0}{1}", ESCAPE_CHARACTER, '[');


        public TerminalVT100Mapper(string typeString)
            : base(typeString)
        {
            TerminalTypeID = TerminalType.XTERM;
            AllowNativeUnicodeCommands = false;
        }

        private ExpectNextChar outputExpected = ExpectNextChar.NORMAL;
        private int pendingCol;

        ///<summary>
        /// ESC followed by '[' is called the CSI (Control Sequence Initiator) and it's how most VT100 codes start.
        ///</summary>
        protected string CSI
        {
            get { return csi; }
        }

        /// <summary>
        /// Map the Unicode chars (and the fake control codes we made) on output to what the terminal
        /// wants to see.
        /// Subclasses of this should perform their own manipulations, then fall-through
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
                    case ExpectNextChar.TELEPORTCURSORCOL:
                        pendingCol = t;
                        outputExpected = ExpectNextChar.TELEPORTCURSORROW;
                        break;

                    case ExpectNextChar.TELEPORTCURSORROW:
                        int row = t;
                        // VT100 counts rows and cols starting at 1, not 0, thus the +1's below:
                        sb.AppendFormat("{0}{1};{2}H", csi, (row + 1), (pendingCol + 1));
                        outputExpected = ExpectNextChar.NORMAL;
                        break;

                    default:
                        switch (t)
                        {
                            case (char)UnicodeCommand.TELEPORTCURSOR:
                                outputExpected = ExpectNextChar.TELEPORTCURSORCOL;
                                break;

                            case (char)UnicodeCommand.CLEARSCREEN:
                                sb.AppendFormat("{0}2J{0}H", csi);
                                break;

                            case (char)UnicodeCommand.SCROLLSCREENUPONE:
                                sb.AppendFormat("{0}S", csi);
                                break;

                            case (char)UnicodeCommand.SCROLLSCREENDOWNONE:
                                sb.AppendFormat("{0}T", csi);
                                break;

                            case (char)UnicodeCommand.HOMECURSOR:
                                sb.AppendFormat("{0}H", csi);
                                break;

                            case (char)UnicodeCommand.UPCURSORONE:
                                sb.AppendFormat("{0}A", csi);
                                break;

                            case (char)UnicodeCommand.DOWNCURSORONE:
                                sb.AppendFormat("{0}B", csi);
                                break;

                            case (char)UnicodeCommand.RIGHTCURSORONE:
                                sb.AppendFormat("{0}C", csi);
                                break;

                            case (char)UnicodeCommand.LEFTCURSORONE:
                                sb.AppendFormat("{0}D", csi);
                                break;

                            case (char)UnicodeCommand.DELETELEFT:
                                sb.AppendFormat("{0}K", csi);
                                break;

                            case (char)UnicodeCommand.DELETERIGHT:
                                sb.AppendFormat("{0}1K", csi);
                                break;

                            case (char)UnicodeCommand.BEEP:
                                sb.Append(BELL_CHAR);
                                break;
                                
                            case (char)UnicodeCommand.REVERSESCREENMODE:
                                sb.AppendFormat("{0}?5h", csi);
                                break;
                                
                            case (char)UnicodeCommand.NORMALSCREENMODE:
                                sb.AppendFormat("{0}?5l", csi);
                                break;
                                
                            case (char)UnicodeCommand.VISUALBEEPMODE:
                                // sadly, have to consume and ignore - no vt100 code for this, so it's not supported.
                                break;
                                
                            case (char)UnicodeCommand.AUDIOBEEPMODE:
                                // sadly, have to consume and ignore - no vt100 code for this. so it's not supported.
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
            get { return TerminalInputDialect.VT100; }
        }

        /// <summary>
        /// VT100 input (ESC [ sequences, DEL as backspace, BEL), converted by the base
        /// class's <see cref="TerminalInputDecoder"/>. Kept as an override so code compiled
        /// against earlier versions still binds.
        /// </summary>
        /// <returns>input mapped into our internal pretend Unicode terminal's codes</returns>
        public override string InputConvert(char[] inChars)
        {
            return base.InputConvert(inChars);
        }

        /// <summary>
        /// No longer used by kOS, which decodes input with <see cref="TerminalInputDecoder"/>;
        /// kept unchanged for subclasses compiled against earlier versions.
        /// <br/>
        /// Return the Unicode (which might be a UnicodeCommand enum value) from the Vt100 CSI sequence
        /// passed in.
        /// </summary>
        /// <param name="inChars">input containing what could be a CSI initiated sequence</param>
        /// <param name="offset">how far into inChars is the first char after the CSI code (ESC[ or CSI).</param>
        /// <param name="numConsumed">how many characters (starting at offset) got consumed and turned into something else.</param>
        /// <returns>The UnicdeCommand equivalent.  NOTE that if numConsumed is zero, this value shouldn't be used as nothing was actually done.</returns>
        protected char ConvertVT100InputCSI(char[] inChars, int offset, out int numConsumed)
        {
            // The keys are VT100Keys', shared with TerminalInputDecoder (review D-03); what this
            // returns and consumes is unchanged.
            char returnChar = '\0'; // default until changed.
            numConsumed = 0; // default if all the clauses below get skipped.
            if (offset < inChars.Length)
            {
                char key = VT100Keys.ForFinal(inChars[offset]);
                if (key != '\0')
                {
                    returnChar = key;
                    numConsumed = 1;
                } // else nothing: leave the chars to be read normally.
            }
            // The following are technically VT220 codes, not VT100, but I couldn't be bothered making a separate
            // mapper just for them.  (i.e. the proper way would be to make a VT220Mapper that inherits from this VT100Mapper,
            // and implement these only there):
            // These codes look like ESC [ _num_ ~.  For example: PgUp is ESC [ 5 ~
            if (offset + 1 < inChars.Length && inChars[offset + 1] == '~')
            {
                char key = VT100Keys.ForTilde(inChars[offset]);
                if (key != '\0')
                {
                    returnChar = key;
                    numConsumed = 2;
                }
                else
                    numConsumed = 0; // not recognized: leave the chars to be read normally.
            }

            // There's a lot more keyboard controls that could be added to here to really get everything the keyboard can do.

            return returnChar;
        }
    }
}