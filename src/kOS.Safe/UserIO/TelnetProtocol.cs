using System;

namespace kOS.Safe.UserIO
{
    /// <summary>
    /// kOS's replies and limits for client telnet input, kept free of sockets and Unity so
    /// they can be tested headlessly.
    /// </summary>
    public static class TelnetProtocol
    {
        public const byte OPTION_ECHO = 1;                  // RFC 857
        public const byte OPTION_SUPPRESS_GOAHEAD = 3;      // RFC 858
        public const byte TERMTYPE_SEND = 1;                // RFC 1091

        /// <summary>IAC SB TERMINAL-TYPE SEND IAC SE: ask the client for its terminal type.</summary>
        public static byte[] TerminalTypeRequest()
        {
            return new[]
            {
                TelnetInputParser.IAC, TelnetInputParser.SB, TelnetInputParser.OPTION_TERMTYPE,
                TERMTYPE_SEND, TelnetInputParser.IAC, TelnetInputParser.SE
            };
        }

        /// <summary>
        /// The reply to a client's DO/DONT/WILL/WONT, or null when none is sent. Clients
        /// sometimes take the active side of negotiations kOS already started, so kOS
        /// restates its own echo/go-ahead mode instead of obeying the client.
        /// <br/>
        /// A client refusing terminal type or window size information gets a warning.
        /// kOS keeps the connection: the upstream server meant to disconnect but its
        /// disconnect code was never emitted, so clients without these options have
        /// always been allowed to stay.
        /// </summary>
        public static byte[] NegotiationReply(byte verb, byte option, bool lineAtATime, out string clientWarning)
        {
            clientWarning = null;
            switch (verb)
            {
                case TelnetInputParser.DO:
                case TelnetInputParser.DONT:
                    switch (option)
                    {
                        case OPTION_ECHO:
                        case OPTION_SUPPRESS_GOAHEAD:
                            return new[] { TelnetInputParser.IAC, lineAtATime ? TelnetInputParser.WONT : TelnetInputParser.WILL, option };
                        case TelnetInputParser.OPTION_NAWS:
                            return verb == TelnetInputParser.DO
                                ? new[] { TelnetInputParser.IAC, TelnetInputParser.WILL, TelnetInputParser.OPTION_NAWS }
                                : null;
                        case TelnetInputParser.OPTION_TERMTYPE:
                            return verb == TelnetInputParser.DO ? TerminalTypeRequest() : null;
                    }
                    return null;

                case TelnetInputParser.WILL:
                    return option == TelnetInputParser.OPTION_TERMTYPE ? TerminalTypeRequest() : null;

                case TelnetInputParser.WONT:
                    if (option == TelnetInputParser.OPTION_TERMTYPE)
                        clientWarning = "{YOUR TELNET CLIENT WONT IMPLEMENT RFC1091 (Terminal type).  kOS MAY NOT WORK CORRECTLY WITH IT.}";
                    else if (option == TelnetInputParser.OPTION_NAWS)
                        clientWarning = "{YOUR TELNET CLIENT WONT IMPLEMENT RFC1073 (Terminal dimensions).  kOS MAY NOT WORK CORRECTLY WITH IT.}";
                    return null;
            }
            return null;
        }

        /// <summary>
        /// Apply one RFC 1073 window dimension. Zero means "unchanged"; other values are
        /// limited to what the terminal screen buffer supports.
        /// </summary>
        public static int NormalizeDimension(int requested, int current, int max)
        {
            if (requested == 0)
                return current;
            return Math.Max(1, Math.Min(requested, max));
        }
    }
}
