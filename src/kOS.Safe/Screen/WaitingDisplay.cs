namespace kOS.Safe.Screen
{
    /// <summary>
    /// Whether the terminal state shows <c>Waiting</c> (STATUS-01, found in the 2026-10-20
    /// Proton pass). A program looping on a WAIT of over
    /// <see cref="TerminalStatus.WAIT_SHOWN_AFTER_SECONDS"/> is held only part of each turn,
    /// so the state alternated between <c>Running</c> and <c>Waiting in</c> twice a second
    /// (and sent each change to telnet clients as a title). Hysteresis: once a held wait is
    /// seen, <c>Waiting</c> stays until <see cref="KEPT_FOR_SECONDS"/> pass without one.
    /// A new activity starts afresh: the prompt, another program, or the interpreter after a
    /// program. Kept by the terminal between state refreshes, fed the CPU's game time (the
    /// clock <c>CPU.IsWaitingFor</c> uses, so it stands still while the game is paused).
    /// </summary>
    public sealed class WaitingDisplay
    {
        public const double KEPT_FOR_SECONDS = 1.5;

        private double lastHeldTime = double.NegativeInfinity;
        private string activity;

        /// <param name="now">The CPU's game time in seconds.</param>
        /// <param name="held">Held by a yield for at least <see cref="TerminalStatus.WAIT_SHOWN_AFTER_SECONDS"/>.</param>
        /// <param name="atPrompt">The interpreter waits for a command.</param>
        /// <param name="programPath">The running program's path, or null at the interpreter.</param>
        /// <returns>Whether to show <c>Waiting</c>.</returns>
        public bool Update(double now, bool held, bool atPrompt, string programPath)
        {
            string current = atPrompt ? null : (programPath ?? string.Empty);
            if (current != activity || now < lastHeldTime) // a new activity, or the clock started again
                lastHeldTime = double.NegativeInfinity;
            activity = current;
            if (atPrompt) return false;
            if (held) lastHeldTime = now;
            return now - lastHeldTime < KEPT_FOR_SECONDS;
        }

        public void Reset()
        {
            lastHeldTime = double.NegativeInfinity;
            activity = null;
        }
    }
}
