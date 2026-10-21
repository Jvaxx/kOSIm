namespace kOS.Safe.UserIO
{
    /// <summary>
    /// The keys of VT100 and VT220 control sequences a telnet client sends (cursor keys, Home,
    /// End, Delete, PageUp, PageDown), as kOS key codes. The one table for
    /// <see cref="TerminalInputDecoder"/> and the game's <c>TerminalVT100Mapper</c>, kept for
    /// mods, so they cannot drift apart (review D-03).
    /// </summary>
    public static class VT100Keys
    {
        /// <summary>The key of <c>ESC [ final</c> (or CSI): A, B, C, D, H, F; '\0' for none.</summary>
        public static char ForFinal(char final)
        {
            switch (final)
            {
                case 'A': return (char)UnicodeCommand.UPCURSORONE;
                case 'B': return (char)UnicodeCommand.DOWNCURSORONE;
                case 'C': return (char)UnicodeCommand.RIGHTCURSORONE;
                case 'D': return (char)UnicodeCommand.LEFTCURSORONE;
                case 'H': return (char)UnicodeCommand.HOMECURSOR;
                case 'F': return (char)UnicodeCommand.ENDCURSOR;
                default: return '\0';
            }
        }

        /// <summary>The key of the VT220 editing key <c>ESC [ n ~</c>: n is 1, 3, 4, 5 or 6; '\0' for none.</summary>
        public static char ForTilde(char n)
        {
            switch (n)
            {
                case '1': return (char)UnicodeCommand.HOMECURSOR;
                case '3': return (char)UnicodeCommand.DELETERIGHT;
                case '4': return (char)UnicodeCommand.ENDCURSOR;
                case '5': return (char)UnicodeCommand.PAGEUPCURSOR;
                case '6': return (char)UnicodeCommand.PAGEDOWNCURSOR;
                default: return '\0';
            }
        }
    }
}
