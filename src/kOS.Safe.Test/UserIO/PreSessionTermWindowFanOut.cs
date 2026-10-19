using System;
using System.Collections.Generic;
using kOS.Safe.Screen;
using kOS.Safe.UserIO;

namespace kOS.Safe.Test.UserIO
{
    /// <summary>
    /// <c>TermWindow</c>'s telnet fan-out as it was before <see cref="TerminalSession{TViewer}"/>
    /// (commit <c>f4b575ae</c>), as an oracle: the telnet list, <c>GetNewestBuffer</c>,
    /// <c>TelnetOutputUpdate</c>, <c>NotifyOfScreenResize</c> (its telnet half),
    /// <c>ResizeAndRepaintTelnet</c>, <c>ChangeTitle</c>, <c>SendTitleToTelnet</c>,
    /// <c>RepaintTelnet</c>, <c>RepaintTelnetFull</c>, <c>ClearScreen</c>, the attach and
    /// detach methods, the router's repaint request, and <c>TelnetSingletonServer.ConnectToProcessor</c>'s
    /// order of calls. Copied unchanged apart from <c>DateTime.Now</c> becoming a parameter,
    /// <c>shared.Screen</c> a field and the telnet server a <see cref="RecordingViewer"/>.
    /// </summary>
    internal sealed class PreSessionTermWindowFanOut
    {
        private readonly IScreenBuffer screen;
        private readonly List<RecordingViewer> telnets = new List<RecordingViewer>();
        private readonly Dictionary<RecordingViewer, IScreenSnapShot> prevTelnetScreens = new Dictionary<RecordingViewer, IScreenSnapShot>();
        private IScreenSnapShot mostRecentScreen;
        private DateTime lastBufferGet;
        private DateTime lastTelnetIncrementalRepaint;

        public string TitleText { get; private set; }

        public PreSessionTermWindowFanOut(IScreenBuffer screen, DateTime now)
        {
            this.screen = screen;
            lastBufferGet = now;
            lastTelnetIncrementalRepaint = now;
            screen.AddResizeNotifier(NotifyOfScreenResize);
        }

        public IScreenSnapShot MostRecentScreen { get { return mostRecentScreen; } }
        public int NumTelnets() { return telnets.Count; }

        /// <summary>TermWindow.Update's part: returns telnetsGotRepainted.</summary>
        public bool Update(DateTime now)
        {
            GetNewestBuffer(now);
            return TelnetOutputUpdate(now);
        }

        /// <summary>TelnetSingletonServer.ConnectToProcessor.</summary>
        public void ConnectToProcessor(RecordingViewer telnet)
        {
            screen.SetSize(telnet.ClientHeight, telnet.ClientWidth);
            AttachTelnet(telnet);
            SendTitleToTelnet(telnet);
            RepaintTelnet(telnet, true);
        }

        /// <summary>TermWindow's ITerminalInputHost.RepaintClient.</summary>
        public void RepaintClient(RecordingViewer client)
        {
            ResizeAndRepaintTelnet(client, screen.ColumnCount, screen.RowCount, true);
            SendTitleToTelnet(client);
        }

        private bool TelnetOutputUpdate(DateTime newTime)
        {
            bool telnetsGotRepainted = false;
            if (newTime > lastTelnetIncrementalRepaint + System.TimeSpan.FromMilliseconds(50)) // = 1/20th second.
            {
                lastTelnetIncrementalRepaint = newTime;
                foreach (RecordingViewer telnet in telnets)
                {
                    RepaintTelnet(telnet, false); // try the incremental differ update.
                }
                telnetsGotRepainted = true;
            }
            return telnetsGotRepainted;
        }

        private void GetNewestBuffer(DateTime newTime)
        {
            if (newTime > lastBufferGet + System.TimeSpan.FromMilliseconds(50)) // = 1/20th second.
            {
                mostRecentScreen = new ScreenSnapShot(screen);
                lastBufferGet = newTime;
            }
        }

        public void AttachTelnet(RecordingViewer server)
        {
            if (!telnets.Contains(server)) telnets.Add(server); // AddUnique
            prevTelnetScreens[server] = null;
        }

        public void DetachTelnet(RecordingViewer server)
        {
            server.Detach(); // server.DisconnectFromProcessor()
            telnets.Remove(server);
            prevTelnetScreens.Remove(server);
        }

        public void DetachAllTelnets()
        {
            RecordingViewer[] listSnapshot = telnets.ToArray();
            foreach (RecordingViewer telnet in listSnapshot)
            {
                DetachTelnet(telnet);
            }
        }

        public int NotifyOfScreenResize(IScreenBuffer sb)
        {
            foreach (RecordingViewer telnet in telnets)
            {
                ResizeAndRepaintTelnet(telnet, sb.ColumnCount, sb.RowCount, false);
            }
            return 0;
        }

        private void ResizeAndRepaintTelnet(RecordingViewer telnet, int width, int height, bool unconditional)
        {
            if (unconditional || telnet.ClientWidth != width || telnet.ClientHeight != height)
            {
                string resizeCmd = new string( new [] {(char)UnicodeCommand.RESIZESCREEN, (char)width, (char)height} );
                telnet.Write(resizeCmd);
                RepaintTelnet(telnet, true);
            }
        }

        public void ChangeTitle(string newTitle)
        {
            if (TitleText != newTitle)
            {
                TitleText = newTitle;
                foreach (RecordingViewer telnet in telnets)
                    SendTitleToTelnet(telnet);
            }
        }

        private void SendTitleToTelnet(RecordingViewer telnet)
        {
            string changeTitleCmd = String.Format("{0}{1}{2}",
                                                  (char)UnicodeCommand.TITLEBEGIN,
                                                  TitleText,
                                                  (char)UnicodeCommand.TITLEEND);
            telnet.Write(changeTitleCmd);
        }

        private void RepaintTelnet(RecordingViewer telnet, bool fullSync)
        {
            if (fullSync || prevTelnetScreens[telnet] == null)
            {
                RepaintTelnetFull(telnet);
                return;
            }

            if (telnet.ReverseScreen != screen.ReverseScreen)
            {
                telnet.Write(screen.ReverseScreen ? ((char)UnicodeCommand.REVERSESCREENMODE) : ((char)UnicodeCommand.NORMALSCREENMODE));
                telnet.ReverseScreen = screen.ReverseScreen;
            }
            if (telnet.VisualBeep != screen.VisualBeep)
            {
                telnet.Write(screen.VisualBeep ? ((char)UnicodeCommand.VISUALBEEPMODE) : ((char)UnicodeCommand.AUDIOBEEPMODE));
                telnet.VisualBeep = screen.VisualBeep;
            }
            string updateText = mostRecentScreen.DiffFrom(prevTelnetScreens[telnet]);
            telnet.Write(updateText);

            prevTelnetScreens[telnet] = mostRecentScreen.DeepCopy();
            for (int i = 0 ; i < screen.BeepsPending ; ++i)
                telnet.Write((char)UnicodeCommand.BEEP);
        }

        private void RepaintTelnetFull(RecordingViewer telnet)
        {
            List<IScreenBufferLine> buffer = mostRecentScreen.Buffer;

            int rowsToPaint = Math.Min(screen.RowCount, buffer.Count);

            telnet.Write((char)UnicodeCommand.CLEARSCREEN);
            telnet.Write(screen.ReverseScreen ? (char)UnicodeCommand.REVERSESCREENMODE : (char)UnicodeCommand.NORMALSCREENMODE);
            telnet.ReverseScreen = screen.ReverseScreen;
            telnet.Write(screen.VisualBeep ? (char)UnicodeCommand.VISUALBEEPMODE : (char)UnicodeCommand.AUDIOBEEPMODE);
            telnet.VisualBeep = screen.VisualBeep;
            for (int row = 0 ; row < rowsToPaint ; ++row)
            {
                IScreenBufferLine lineBuffer = buffer[row];
                int columnOfLastContent = -1;
                for (int col = 0 ; col < lineBuffer.Length ; ++col)
                {
                    char ch = lineBuffer[col];
                    switch (ch)
                    {
                        case (char)0x0000:
                            break;
                        case (char)0x0009:
                            break;
                        default:
                            columnOfLastContent = col;
                            break;
                    }
                }
                if (columnOfLastContent >= 0)
                {
                    string line = lineBuffer.ToString().Substring(0, columnOfLastContent+1);
                    telnet.Write(line);
                }
                if (row < rowsToPaint-1)
                    telnet.Write((char)UnicodeCommand.STARTNEXTLINE);
            }

            telnet.Write(String.Format("{0}{1}{2}",
                                       (char)UnicodeCommand.TELEPORTCURSOR,
                                       (char)mostRecentScreen.CursorColumn,
                                       (char)mostRecentScreen.CursorRow));

            prevTelnetScreens[telnet] = mostRecentScreen.DeepCopy();
        }

        public void ClearScreen()
        {
            screen.ClearScreen();
            foreach (RecordingViewer telnet in telnets)
            {
                telnet.Write((char)UnicodeCommand.CLEARSCREEN);
                prevTelnetScreens[telnet] = ScreenSnapShot.EmptyScreen(screen);
            }
        }
    }
}
