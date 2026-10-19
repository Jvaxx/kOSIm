using System;
using System.Collections.Generic;
using System.Globalization;
using kOS.Safe.Screen;

namespace kOS.Safe.UserIO
{
    /// <summary>What a GUI paste did (<see cref="TerminalGuiPaste{TClient}.Paste"/>).</summary>
    public enum GuiPasteResult
    {
        /// <summary>Nothing to type: an empty clipboard, only control characters, or a paste already waiting.</summary>
        Nothing,
        /// <summary>One line, queued at once.</summary>
        Queued,
        /// <summary>Lines that would run: waiting for <see cref="TerminalGuiPaste{TClient}.Confirm"/> or <see cref="TerminalGuiPaste{TClient}.Cancel"/>.</summary>
        AwaitingConfirmation,
        /// <summary>Not typed; the terminal shows why.</summary>
        Refused
    }

    /// <summary>
    /// The GUI terminal's paste (Phase 3, P3-3, TERM-01): clipboard text is prepared by
    /// <see cref="TerminalPaste"/>; one line is queued at once; text holding a line break,
    /// which would run commands, waits for the user to confirm it; a text over
    /// <see cref="TerminalPaste.MAX_CHARS"/>, or a clipboard that cannot be read, is refused
    /// with a message on the terminal. Queued keys go through
    /// <see cref="TerminalInputRouter{TClient}.QueuePaste"/>. Telnet clients paste through
    /// their own terminal and do not use this. Main thread only.
    /// </summary>
    /// <typeparam name="TClient">the window's telnet client type</typeparam>
    public sealed class TerminalGuiPaste<TClient> where TClient : class
    {
        /// <summary>Lines shown in the confirmation, and their width.</summary>
        public const int PREVIEW_LINES = 6;
        public const int PREVIEW_COLUMNS = 60;

        private readonly TerminalInputRouter<TClient> router;
        private readonly Func<IScreenBuffer> screen;

        public TerminalGuiPaste(TerminalInputRouter<TClient> router, Func<IScreenBuffer> screen)
        {
            if (router == null) throw new ArgumentNullException("router");
            if (screen == null) throw new ArgumentNullException("screen");
            this.router = router;
            this.screen = screen;
        }

        /// <summary>The paste waiting for confirmation; null when none is.</summary>
        public TerminalPaste Pending { get; private set; }

        public bool IsAwaitingConfirmation { get { return Pending != null; } }

        /// <summary>
        /// Paste the clipboard. While a paste waits for confirmation, another one does nothing
        /// (the clipboard is not read). A failing <paramref name="readClipboard"/> is reported
        /// on the terminal, except for exceptions that must not be caught.
        /// </summary>
        public GuiPasteResult Paste(Func<string> readClipboard)
        {
            if (readClipboard == null) throw new ArgumentNullException("readClipboard");
            if (IsAwaitingConfirmation)
                return GuiPasteResult.Nothing;
            string text;
            try
            {
                text = readClipboard();
            }
            catch (Exception e) when (!IsFatal(e))
            {
                Notice("Paste failed: the clipboard could not be read (" + e.GetType().Name + ").");
                return GuiPasteResult.Refused;
            }
            return Paste(text);
        }

        /// <summary>Paste <paramref name="text"/> as clipboard text.</summary>
        public GuiPasteResult Paste(string text)
        {
            if (IsAwaitingConfirmation)
                return GuiPasteResult.Nothing;
            TerminalPaste paste = TerminalPaste.Prepare(text);
            if (paste.IsTooLarge)
            {
                Notice(RefusalMessage(paste));
                return GuiPasteResult.Refused;
            }
            if (paste.IsEmpty)
                return GuiPasteResult.Nothing;
            if (paste.RunsCommands)
            {
                Pending = paste;
                return GuiPasteResult.AwaitingConfirmation;
            }
            return router.QueuePaste(paste) ? GuiPasteResult.Queued : GuiPasteResult.Nothing;
        }

        /// <summary>Queue the waiting paste.</summary>
        /// <returns>whether anything was queued</returns>
        public bool Confirm()
        {
            TerminalPaste paste = Pending;
            Pending = null;
            return paste != null && router.QueuePaste(paste);
        }

        /// <summary>Drop the waiting paste, if any. Nothing is typed.</summary>
        public void Cancel()
        {
            Pending = null;
        }

        /// <summary>The terminal's message for a paste over the limit.</summary>
        public static string RefusalMessage(TerminalPaste paste)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "Paste refused: {0:N0} characters, over the limit of {1:N0}. Nothing was typed.",
                paste.SourceLength, TerminalPaste.MAX_CHARS);
        }

        /// <summary>The lines of a paste as the user sees them; the last one may have no Enter.</summary>
        public static List<string> Lines(TerminalPaste paste)
        {
            var lines = new List<string>(paste.Keys.Split('\r'));
            if (lines[lines.Count - 1].Length == 0)
                lines.RemoveAt(lines.Count - 1); // the text ends with a line break
            return lines;
        }

        /// <summary>The confirmation's question for the waiting paste.</summary>
        public string Question
        {
            get
            {
                if (Pending == null) return string.Empty;
                int lineCount = Lines(Pending).Count;
                int runs = Pending.EnterCount;
                string question = string.Format(CultureInfo.InvariantCulture,
                    "This paste runs {0} as commands.", Count(runs, "line"));
                if (lineCount > runs)
                    question += "\nIts last line is typed without running.";
                if (Pending.DroppedCount > 0)
                    question += string.Format(CultureInfo.InvariantCulture,
                        "\n{0} left out.", Count(Pending.DroppedCount, "control character"));
                return question;
            }
        }

        /// <summary>The first lines of the waiting paste, shortened, then how many more there are.</summary>
        public string Preview
        {
            get
            {
                if (Pending == null) return string.Empty;
                List<string> lines = Lines(Pending);
                var preview = new List<string>();
                for (int i = 0; i < lines.Count && i < PREVIEW_LINES; ++i)
                {
                    string line = lines[i].Replace('\t', ' ');
                    preview.Add(line.Length > PREVIEW_COLUMNS ? line.Substring(0, PREVIEW_COLUMNS - 3) + "..." : line);
                }
                if (lines.Count > PREVIEW_LINES)
                    preview.Add(string.Format(CultureInfo.InvariantCulture, "(and {0})", Count(lines.Count - PREVIEW_LINES, "more line")));
                return string.Join("\n", preview.ToArray());
            }
        }

        /// <summary>The confirming button's label.</summary>
        public string ConfirmLabel
        {
            get { return Pending == null ? string.Empty : "Run " + Count(Pending.EnterCount, "line"); }
        }

        private void Notice(string message)
        {
            IScreenBuffer target = screen();
            if (target != null)
                target.Print(message);
        }

        private static string Count(int count, string noun)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:N0} {1}{2}", count, noun, count == 1 ? "" : "s");
        }

        private static bool IsFatal(Exception e)
        {
            return e is OutOfMemoryException || e is StackOverflowException || e is AccessViolationException ||
                e is System.Threading.ThreadAbortException;
        }
    }
}
