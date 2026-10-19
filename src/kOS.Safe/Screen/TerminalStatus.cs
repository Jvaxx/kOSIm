using System.Collections.Generic;
using System.Globalization;
using kOS.Safe.Module;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// The CPU state a terminal shows (Phase 3, decision D2): in the GUI terminal's bottom
    /// bar, and appended to the telnet window title. Nothing is printed on the terminal.
    /// <list type="bullet">
    /// <item><c>Off</c>, <c>No power</c>, <c>Starting</c> (not booted yet) say it all.</item>
    /// <item>Otherwise what runs: <c>Ready</c> (the prompt waits for a command),
    /// <c>Running 0:/launch.ks</c> or <c>Waiting in 0:/launch.ks</c> (held by a WAIT or
    /// another yield, such as a long compile, for at least <see cref="WAIT_SHOWN_AFTER_SECONDS"/>, so a
    /// <c>WAIT 0</c> loop does not flicker), <c>Running a command</c> or
    /// <c>Waiting in a command</c> at the interpreter;</item>
    /// <item>then the communications: <c>Delay 2.4 s</c> while a command is sent with a
    /// signal delay (in place of <c>Ready</c>), <c>No connection</c> without a connection
    /// to control;</item>
    /// <item>last, <c>New output</c> while output came with the view scrolled back (O2).</item>
    /// </list>
    /// Parts are joined with <c> | </c>.
    /// </summary>
    public sealed class TerminalStatus
    {
        public const double WAIT_SHOWN_AFTER_SECONDS = 0.5;
        public const string SEPARATOR = " | ";

        public ProcessorModes Mode = ProcessorModes.READY;
        public bool Booted = true;
        /// <summary>The interpreter waits for a command.</summary>
        public bool AtPrompt;
        /// <summary>The running program's path, or null at the interpreter.</summary>
        public string ProgramPath;
        /// <summary>Held by a yield for at least <see cref="WAIT_SHOWN_AFTER_SECONDS"/>.</summary>
        public bool Waiting;
        /// <summary>Seconds left to send a command with a signal delay; NaN when none is being sent.</summary>
        public double DelaySecondsLeft = double.NaN;
        public bool Connected = true;
        /// <summary>Output came while the view was scrolled away from the screen (O2).</summary>
        public bool NewOutput;

        public string Text
        {
            get
            {
                if (Mode == ProcessorModes.OFF) return "Off";
                if (Mode == ProcessorModes.STARVED) return "No power";
                if (!Booted) return "Starting";

                var parts = new List<string>();
                bool sending = !double.IsNaN(DelaySecondsLeft);
                if (!(AtPrompt && sending))
                    parts.Add(Activity());
                if (sending)
                    parts.Add("Delay " + FormatSeconds(DelaySecondsLeft) + " s");
                if (!Connected)
                    parts.Add("No connection");
                if (NewOutput)
                    parts.Add("New output");
                return string.Join(SEPARATOR, parts.ToArray());
            }
        }

        private string Activity()
        {
            if (AtPrompt) return "Ready";
            if (ProgramPath != null)
                return (Waiting ? "Waiting in " : "Running ") + ProgramPath;
            return Waiting ? "Waiting in a command" : "Running a command";
        }

        /// <summary>One decimal under 10 s, whole seconds above.</summary>
        public static string FormatSeconds(double seconds)
        {
            if (seconds < 0) seconds = 0;
            return seconds < 10
                ? seconds.ToString("0.0", CultureInfo.InvariantCulture)
                : seconds.ToString("0", CultureInfo.InvariantCulture);
        }

        /// <summary>The telnet title: the terminal's name, then the state when there is one.</summary>
        public static string TitleWithStatus(string title, string status)
        {
            return string.IsNullOrEmpty(status) ? title : title + " - " + status;
        }
    }
}
