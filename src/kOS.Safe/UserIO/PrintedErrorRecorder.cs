using System.Text;

namespace kOS.Safe.UserIO
{
    /// <summary>
    /// Keeps the last error a CPU printed on its terminal, as printed (Phase 3, P3-4): the
    /// logger calls <see cref="Begin"/>, then <see cref="Printed"/> for each text it shows on
    /// the screen for that error (the message, then its source location), then
    /// <see cref="End"/>. Text printed outside Begin/End is not an error and is ignored.
    /// The GUI terminal copies <see cref="Text"/> with Ctrl+Shift+E.
    /// </summary>
    public sealed class PrintedErrorRecorder
    {
        private StringBuilder current;

        /// <summary>The last error, as printed; null until one was.</summary>
        public string Text { get; private set; }

        public void Begin()
        {
            current = new StringBuilder();
        }

        public void Printed(string text)
        {
            if (current == null || string.IsNullOrEmpty(text)) return;
            if (current.Length > 0 && current[current.Length - 1] != '\n')
                current.Append('\n'); // each print starts a new line on the screen
            current.Append(text.Replace("\r\n", "\n"));
        }

        /// <summary>
        /// The error is complete. One that printed nothing keeps the previous one. Control
        /// characters are left out, as in a copy of the screen (COPY-01).
        /// </summary>
        public void End()
        {
            if (current == null) return;
            string text = TerminalCopy.WithoutControlCharacters(current.ToString()).TrimEnd('\n');
            current = null;
            if (text.Length > 0)
                Text = text;
        }
    }
}
