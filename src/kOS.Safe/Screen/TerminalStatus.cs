using System.Collections.Generic;
using System.Globalization;
using kOS.Safe.Module;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// The CPU state a terminal shows (Phase 3, decision D2): in the GUI terminal's status
    /// strip, and appended to the telnet window title. Nothing is printed on the terminal.
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
    /// Parts are joined with <c> | </c> in <see cref="Text"/> (the telnet title). The GUI's
    /// status strip (Phase 4) shows the same parts separately: <see cref="StateText"/> with a
    /// dot colored by <see cref="Kind"/>, then <see cref="Chips"/>, which also count the
    /// attached telnet clients (not in the title).
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
        /// <summary>Telnet clients attached: a chip in the GUI strip, not part of <see cref="Text"/>.</summary>
        public int TelnetClients;

        /// <summary>The telnet title's state: <see cref="StateText"/> and the chips but the telnet count.</summary>
        public string Text
        {
            get
            {
                var parts = new List<string> { StateText };
                foreach (StatusChip chip in Chips)
                    if (chip.Kind != StatusChipKind.Telnet)
                        parts.Add(chip.Text);
                return string.Join(SEPARATOR, parts.ToArray());
            }
        }

        /// <summary>What the CPU does, which colors the strip's dot.</summary>
        public StatusKind Kind
        {
            get
            {
                if (Mode == ProcessorModes.OFF) return StatusKind.Off;
                if (Mode == ProcessorModes.STARVED) return StatusKind.NoPower;
                if (!Booted) return StatusKind.Starting;
                if (AtPrompt) return Sending ? StatusKind.Sending : StatusKind.Ready;
                return Waiting ? StatusKind.Waiting : StatusKind.Running;
            }
        }

        /// <summary>The first part: the activity, or the delay in place of <c>Ready</c>.</summary>
        public string StateText
        {
            get
            {
                switch (Kind)
                {
                    case StatusKind.Off: return "Off";
                    case StatusKind.NoPower: return "No power";
                    case StatusKind.Starting: return "Starting";
                    case StatusKind.Ready: return "Ready";
                    case StatusKind.Sending: return DelayText;
                }
                if (ProgramPath != null)
                    return (Waiting ? "Waiting in " : "Running ") + ProgramPath;
                return Waiting ? "Waiting in a command" : "Running a command";
            }
        }

        /// <summary>
        /// The parts after <see cref="StateText"/>, in the order shown, each with the
        /// priority the strip keeps it by when narrow (higher kept longer). Off, no power and
        /// starting have only the telnet count.
        /// </summary>
        public List<StatusChip> Chips
        {
            get
            {
                var chips = new List<StatusChip>();
                StatusKind kind = Kind;
                bool running = kind != StatusKind.Off && kind != StatusKind.NoPower && kind != StatusKind.Starting;
                if (running && Sending && kind != StatusKind.Sending)
                    chips.Add(new StatusChip(StatusChipKind.Delay, DelayText));
                if (running && !Connected)
                    chips.Add(new StatusChip(StatusChipKind.NoConnection, "No connection"));
                if (running && NewOutput)
                    chips.Add(new StatusChip(StatusChipKind.NewOutput, "New output"));
                if (TelnetClients > 0)
                    chips.Add(new StatusChip(StatusChipKind.Telnet,
                        TelnetClients + (TelnetClients == 1 ? " telnet client" : " telnet clients")));
                return chips;
            }
        }

        private bool Sending { get { return !double.IsNaN(DelaySecondsLeft); } }

        private string DelayText { get { return "Delay " + FormatSeconds(DelaySecondsLeft) + " s"; } }

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

    /// <summary>What a terminal's CPU does, as <see cref="TerminalStatus.Kind"/> reports it.</summary>
    public enum StatusKind
    {
        Off,
        NoPower,
        Starting,
        Ready,
        /// <summary>At the prompt, a command being sent with a signal delay.</summary>
        Sending,
        Running,
        Waiting
    }

    public enum StatusChipKind
    {
        Delay,
        NoConnection,
        NewOutput,
        Telnet
    }

    /// <summary>One part of the state after the first (<see cref="TerminalStatus.Chips"/>).</summary>
    public sealed class StatusChip
    {
        public readonly StatusChipKind Kind;
        public readonly string Text;

        public StatusChip(StatusChipKind kind, string text)
        {
            Kind = kind;
            Text = text;
        }

        /// <summary>
        /// Higher is kept longer in a narrow strip: losing the connection matters most, the
        /// telnet count least (it is also shown by the clients themselves).
        /// </summary>
        public int Priority
        {
            get
            {
                switch (Kind)
                {
                    case StatusChipKind.NoConnection: return 40;
                    case StatusChipKind.Delay: return 30;
                    case StatusChipKind.NewOutput: return 20;
                    default: return 10;
                }
            }
        }
    }
}
