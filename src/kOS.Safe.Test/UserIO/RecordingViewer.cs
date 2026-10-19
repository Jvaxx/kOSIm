using System;
using System.Collections.Generic;
using System.Text;
using kOS.Safe.UserIO;

namespace kOS.Safe.Test.UserIO
{
    /// <summary>
    /// A telnet client as a <see cref="TerminalSession{TViewer}"/> sees it: it records the
    /// output it is sent, and plays it on a <see cref="ViewerScreenModel"/>. Its size is what
    /// the client last reported: when the user resizes it (<see cref="UserResize"/>), or when
    /// its terminal takes a size it was asked to take.
    /// </summary>
    internal sealed class RecordingViewer : ITerminalViewer
    {
        private readonly StringBuilder output = new StringBuilder();
        private int taken; // output already returned by TakeOutput()

        public RecordingViewer(int width = 80, int height = 24)
        {
            ClientWidth = width;
            ClientHeight = height;
            Model = new ViewerScreenModel(width, height);
        }

        public string Name { get; set; }
        public int ClientWidth { get; private set; }
        public int ClientHeight { get; private set; }
        public bool ReverseScreen { get; set; }
        public bool VisualBeep { get; set; }

        /// <summary>The client's display, as the output it was sent draws it.</summary>
        public ViewerScreenModel Model { get; private set; }

        public int DetachCount { get; private set; }

        /// <summary>The user resized the client's window; it reports the new size (NAWS).</summary>
        public void UserResize(int width, int height)
        {
            Model.Resize(width, height);
            ClientWidth = width;
            ClientHeight = height;
        }

        /// <summary>What the client does when told to detach (the telnet server calls the window back).</summary>
        public Action OnDetach;

        public string Output { get { return output.ToString(); } }

        /// <summary>The output sent since the last call.</summary>
        public string TakeOutput()
        {
            string text = output.ToString(taken, output.Length - taken);
            taken = output.Length;
            return text;
        }

        public void Write(char ch)
        {
            Write(ch.ToString());
        }

        public void Write(string str)
        {
            output.Append(str);
            Model.Apply(str);
            if (Model.Width != ClientWidth || Model.Height != ClientHeight)
            {
                // The client took a requested size and reports it (NAWS); the screen already has it.
                ClientWidth = Model.Width;
                ClientHeight = Model.Height;
            }
        }

        public void Detach()
        {
            ++DetachCount;
            if (OnDetach != null)
                OnDetach();
        }

        public override string ToString() { return Name ?? base.ToString(); }

        /// <summary>
        /// Output as one line per item: text runs as <c>text "..."</c>, and kOS commands by
        /// name, with their arguments (<c>RESIZESCREEN 100x30</c>, <c>TELEPORTCURSOR 3,4</c>,
        /// <c>TITLE "..."</c>).
        /// </summary>
        public static List<string> Describe(string output)
        {
            var lines = new List<string>();
            var text = new StringBuilder();
            Action flushText = () =>
            {
                if (text.Length > 0)
                    lines.Add("text \"" + TelnetServerInputModel.EscapeText(text.ToString()) + "\"");
                text.Length = 0;
            };
            for (int i = 0; i < output.Length; ++i)
            {
                char ch = output[i];
                if (ch == (char)UnicodeCommand.RESIZESCREEN && i + 2 < output.Length)
                {
                    flushText();
                    lines.Add("RESIZESCREEN " + (int)output[i + 1] + "x" + (int)output[i + 2]);
                    i += 2;
                }
                else if (ch == (char)UnicodeCommand.TELEPORTCURSOR && i + 2 < output.Length)
                {
                    flushText();
                    lines.Add("TELEPORTCURSOR " + (int)output[i + 1] + "," + (int)output[i + 2]);
                    i += 2;
                }
                else if (ch == (char)UnicodeCommand.TITLEBEGIN)
                {
                    flushText();
                    int end = output.IndexOf((char)UnicodeCommand.TITLEEND, i + 1);
                    if (end < 0) end = output.Length;
                    lines.Add("TITLE \"" + TelnetServerInputModel.EscapeText(output.Substring(i + 1, end - i - 1)) + "\"");
                    i = end;
                }
                else if (Enum.IsDefined(typeof(UnicodeCommand), (int)ch))
                {
                    flushText();
                    lines.Add(Enum.GetName(typeof(UnicodeCommand), (int)ch));
                }
                else
                    text.Append(ch);
            }
            flushText();
            return lines;
        }
    }

    /// <summary>
    /// A minimal terminal that plays <see cref="UnicodeCommand"/> output the way the game's
    /// telnet mappers have a VT100/xterm client do it: text is written at the cursor and
    /// clipped at the right edge (the session never relies on wrapping), STARTNEXTLINE is
    /// CR LF and scrolls at the bottom, CLEARSCREEN clears and homes, the scroll commands
    /// scroll the whole screen, RESIZESCREEN resizes keeping the top-left content (when
    /// <see cref="ObeysResize"/>, as xterm does), and NUL shows as a blank.
    /// </summary>
    internal sealed class ViewerScreenModel
    {
        private char[][] rows;

        public ViewerScreenModel(int width, int height)
        {
            Width = width;
            Height = height;
            rows = NewRows(width, height);
            ObeysResize = true;
        }

        public int Width { get; private set; }
        public int Height { get; private set; }
        public int CursorColumn { get; private set; }
        public int CursorRow { get; private set; }
        public string Title { get; private set; }
        public bool Reverse { get; private set; }
        public bool VisualBeep { get; private set; }
        public int Beeps { get; private set; }
        public bool ObeysResize { get; set; }

        private ExpectNextChar expect = ExpectNextChar.NORMAL;
        private int pendingColumn;
        private int pendingWidth;
        private readonly StringBuilder pendingTitle = new StringBuilder();

        public string Row(int row) { return new string(rows[row]); }

        public void Apply(string output)
        {
            foreach (char ch in output)
                Apply(ch);
        }

        private void Apply(char ch)
        {
            switch (expect)
            {
                case ExpectNextChar.TELEPORTCURSORCOL:
                    pendingColumn = ch;
                    expect = ExpectNextChar.TELEPORTCURSORROW;
                    return;
                case ExpectNextChar.TELEPORTCURSORROW:
                    CursorColumn = Math.Min(pendingColumn, Width);
                    CursorRow = Math.Min((int)ch, Height - 1);
                    expect = ExpectNextChar.NORMAL;
                    return;
                case ExpectNextChar.RESIZEWIDTH:
                    pendingWidth = ch;
                    expect = ExpectNextChar.RESIZEHEIGHT;
                    return;
                case ExpectNextChar.RESIZEHEIGHT:
                    if (ObeysResize)
                        Resize(pendingWidth, ch);
                    expect = ExpectNextChar.NORMAL;
                    return;
                case ExpectNextChar.INTITLE:
                    if (ch == (char)UnicodeCommand.TITLEEND)
                    {
                        Title = pendingTitle.ToString();
                        expect = ExpectNextChar.NORMAL;
                    }
                    else
                        pendingTitle.Append(ch);
                    return;
            }

            switch (ch)
            {
                case (char)UnicodeCommand.TELEPORTCURSOR: expect = ExpectNextChar.TELEPORTCURSORCOL; break;
                case (char)UnicodeCommand.RESIZESCREEN: expect = ExpectNextChar.RESIZEWIDTH; break;
                case (char)UnicodeCommand.TITLEBEGIN:
                    pendingTitle.Length = 0;
                    expect = ExpectNextChar.INTITLE;
                    break;
                case (char)UnicodeCommand.CLEARSCREEN:
                    rows = NewRows(Width, Height);
                    CursorColumn = 0;
                    CursorRow = 0;
                    break;
                case (char)UnicodeCommand.STARTNEXTLINE:
                    CursorColumn = 0;
                    if (CursorRow == Height - 1)
                        ScrollUp();
                    else
                        ++CursorRow;
                    break;
                case (char)UnicodeCommand.SCROLLSCREENUPONE: ScrollUp(); break;
                case (char)UnicodeCommand.SCROLLSCREENDOWNONE: ScrollDown(); break;
                case (char)UnicodeCommand.REVERSESCREENMODE: Reverse = true; break;
                case (char)UnicodeCommand.NORMALSCREENMODE: Reverse = false; break;
                case (char)UnicodeCommand.VISUALBEEPMODE: VisualBeep = true; break;
                case (char)UnicodeCommand.AUDIOBEEPMODE: VisualBeep = false; break;
                case (char)UnicodeCommand.BEEP: ++Beeps; break;
                default:
                    if (Enum.IsDefined(typeof(UnicodeCommand), (int)ch))
                        throw new InvalidOperationException("the session sent an unexpected command " + Enum.GetName(typeof(UnicodeCommand), (int)ch));
                    if (CursorColumn < Width)
                        rows[CursorRow][CursorColumn] = ch == '\0' ? ' ' : ch;
                    CursorColumn = Math.Min(CursorColumn + 1, Width);
                    break;
            }
        }

        public void Resize(int width, int height)
        {
            char[][] newRows = NewRows(width, height);
            for (int row = 0; row < Math.Min(height, Height); ++row)
                Array.Copy(rows[row], newRows[row], Math.Min(width, Width));
            rows = newRows;
            Width = width;
            Height = height;
            CursorColumn = Math.Min(CursorColumn, Width);
            CursorRow = Math.Min(CursorRow, Height - 1);
        }

        private void ScrollUp()
        {
            for (int row = 1; row < Height; ++row)
                rows[row - 1] = rows[row];
            rows[Height - 1] = Blank(Width);
        }

        private void ScrollDown()
        {
            for (int row = Height - 1; row > 0; --row)
                rows[row] = rows[row - 1];
            rows[0] = Blank(Width);
        }

        private static char[][] NewRows(int width, int height)
        {
            var newRows = new char[height][];
            for (int row = 0; row < height; ++row)
                newRows[row] = Blank(width);
            return newRows;
        }

        private static char[] Blank(int width)
        {
            var row = new char[width];
            for (int col = 0; col < width; ++col)
                row[col] = ' ';
            return row;
        }
    }
}
