using System;

namespace kOS.Safe.UserIO
{
    /// <summary>
    /// Recognizes the terminal type strings that telnet clients send (RFC 1091).
    /// The string comes from the client, so any value, including empty or null,
    /// must be safe.
    /// </summary>
    public static class TerminalTypeNames
    {
        /// <summary>True for "xterm" and its variants, like "xterm-256color".</summary>
        public static bool IsXterm(string typeString)
        {
            return HasPrefix(typeString, "xterm");
        }

        /// <summary>True for "vt100" or "vt102" (any "vt10" prefix).</summary>
        public static bool IsVT100(string typeString)
        {
            return HasPrefix(typeString, "vt10");
        }

        private static bool HasPrefix(string typeString, string prefix)
        {
            return typeString != null && typeString.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
    }
}
