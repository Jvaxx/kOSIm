using System;
using System.Collections.Generic;
using kOS.Safe.Screen;

namespace kOS.Safe.UserIO
{
    /// <summary>
    /// A viewer that is sent the terminal's screen as <see cref="UnicodeCommand"/> output:
    /// a telnet client (<c>kOS.UserIO.TelnetSingletonServer</c>), or a test fake. The GUI
    /// window draws the session's <see cref="TerminalSession{TViewer}.Snapshot"/> itself and
    /// owns the session; it is not a viewer. Main thread only, apart from the client size,
    /// which a telnet reader thread may update at any time.
    /// </summary>
    public interface ITerminalViewer
    {
        /// <summary>The viewer's own width in columns, as it last reported it.</summary>
        int ClientWidth { get; }

        /// <summary>The viewer's own height in rows, as it last reported it.</summary>
        int ClientHeight { get; }

        /// <summary>The reverse-screen mode last sent to this viewer.</summary>
        bool ReverseScreen { get; set; }

        /// <summary>The visual-beep mode last sent to this viewer.</summary>
        bool VisualBeep { get; set; }

        void Write(char ch);

        void Write(string str);

        /// <summary>
        /// Leave the session (a telnet client goes back to its welcome menu). The viewer may
        /// call <see cref="TerminalSession{TViewer}.DetachViewer"/> back, for itself.
        /// </summary>
        void Detach();
    }

    /// <summary>
    /// The terminal's viewers and what they are sent (Phase 2, P2-2; moved unchanged out of
    /// <c>TermWindow</c>): the attached telnet clients, the screen snapshot shared by them and
    /// the GUI, diff and full repaints, resize requests, the title and the clear. Main thread
    /// only.
    /// <br/>
    /// Resize authority (decision D3): there is one screen size, and the last size applied
    /// wins, whoever asked for it (the GUI, a script, a telnet client attaching or reporting
    /// its size). The owner calls <see cref="ScreenResized"/> from the screen's resize
    /// notifier, and every viewer of another size is then asked to take the screen's size.
    /// <br/>
    /// Lifecycle (P2-3): the window disposes the session when it is destroyed, with its
    /// processor. Every viewer is then told to leave (a telnet client goes back to its welcome
    /// menu, with a detach message), and a viewer attaching afterwards is told to leave at once.
    /// </summary>
    /// <typeparam name="TViewer">the window's telnet client type</typeparam>
    public sealed class TerminalSession<TViewer> : IDisposable where TViewer : class, ITerminalViewer
    {
        /// <summary>How often the snapshot is retaken and the viewers are sent the changes.</summary>
        public static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(50); // = 1/20th second.

        private readonly List<TViewer> viewers = new List<TViewer>();
        // What each viewer was last sent; null until its first full repaint.
        private readonly Dictionary<TViewer, IScreenSnapShot> previousScreens = new Dictionary<TViewer, IScreenSnapShot>();
        private DateTime lastSnapshot;
        private DateTime lastRefresh;

        /// <param name="now">the time the first refresh is counted from</param>
        public TerminalSession(DateTime now)
        {
            lastSnapshot = now;
            lastRefresh = now;
        }

        /// <summary>The processor's screen; set when the window attaches to it.</summary>
        public IScreenBuffer Screen { get; set; }

        /// <summary>
        /// The screen as last taken by <see cref="Update"/>, for the GUI and every viewer.
        /// Null until the first refresh.
        /// </summary>
        public IScreenSnapShot Snapshot { get; private set; }

        /// <summary>The terminal's title, as sent to the viewers.</summary>
        public string Title { get; private set; }

        public int ViewerCount { get { return viewers.Count; } }

        /// <summary>True once <see cref="Dispose"/> has run: no viewer can attach any more.</summary>
        public bool IsDisposed { get; private set; }

        /// <summary>A copy of the attached viewers, safe to use while viewers detach.</summary>
        public TViewer[] Viewers { get { return viewers.ToArray(); } }

        public bool HasViewer(TViewer viewer)
        {
            return viewers.Contains(viewer);
        }

        /// <summary>
        /// Once per update: retake the snapshot and send the viewers the changes, at most
        /// once per <see cref="RefreshInterval"/> (no faster than the fastest typist or script
        /// might change the screen). Taking the snapshot is not trivial, so it is taken once
        /// for the GUI and all the viewers to share until the next refresh.
        /// </summary>
        /// <returns>true if the viewers were sent the changes (with the screen's pending beeps)
        /// in this update</returns>
        public bool Update(DateTime now)
        {
            if (IsDisposed)
                return false;
            if (now > lastSnapshot + RefreshInterval)
            {
                Snapshot = new ScreenSnapShot(Screen);
                lastSnapshot = now;
            }

            if (now > lastRefresh + RefreshInterval)
            {
                lastRefresh = now;
                foreach (TViewer viewer in Viewers)
                {
                    Repaint(viewer, false); // try the incremental differ update.
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Attach a viewer (a telnet client picking this processor): the screen takes the
        /// viewer's size, then the viewer is sent the title and a full repaint. A disposed
        /// session tells the viewer to leave instead, and leaves the screen alone.
        /// </summary>
        public void AttachViewer(TViewer viewer)
        {
            if (IsDisposed)
            {
                viewer.Detach();
                return;
            }
            Screen.SetSize(viewer.ClientHeight, viewer.ClientWidth); // Reset the screen to match the telnet window.
            if (!viewers.Contains(viewer))
                viewers.Add(viewer);
            previousScreens[viewer] = null;
            SendTitle(viewer);
            Repaint(viewer, true); // Need to start with a full paint of the terminal.
        }

        /// <summary>
        /// Tell the viewer to leave (<see cref="ITerminalViewer.Detach"/>) and forget it.
        /// </summary>
        public void DetachViewer(TViewer viewer)
        {
            viewer.Detach();
            viewers.Remove(viewer);
            previousScreens.Remove(viewer);
        }

        public void DetachAllViewers()
        {
            // Detaching removes from the list, so go through a copy:
            foreach (TViewer viewer in Viewers)
            {
                DetachViewer(viewer);
            }
        }

        /// <summary>
        /// End the session: tell every viewer to leave. Safe to call more than once.
        /// </summary>
        public void Dispose()
        {
            if (IsDisposed)
                return;
            IsDisposed = true;
            DetachAllViewers();
        }

        /// <summary>
        /// The screen changed size (from its resize notifier): ask every viewer of another
        /// size to take the new size, and repaint it.
        /// </summary>
        public void ScreenResized()
        {
            foreach (TViewer viewer in Viewers)
            {
                ResizeAndRepaint(viewer, Screen.ColumnCount, Screen.RowCount, false);
            }
        }

        /// <summary>
        /// The viewer asked for a full redraw: restate its size, the whole screen and the title
        /// (the title may have been in output a slow client never got).
        /// </summary>
        public void RepaintViewer(TViewer viewer)
        {
            ResizeAndRepaint(viewer, Screen.ColumnCount, Screen.RowCount, true);
            SendTitle(viewer);
        }

        public void ChangeTitle(string newTitle)
        {
            if (Title != newTitle) // For once, a direct simple reference-equals is really what we want.  Immutable strings should make this work quickly.
            {
                Title = newTitle;
                foreach (TViewer viewer in Viewers)
                    SendTitle(viewer);
            }
        }

        /// <summary>Clear the screen and every viewer's display.</summary>
        public void ClearScreen()
        {
            Screen.ClearScreen();
            foreach (TViewer viewer in Viewers)
            {
                viewer.Write((char)UnicodeCommand.CLEARSCREEN);
                previousScreens[viewer] = ScreenSnapShot.EmptyScreen(Screen);
            }
        }

        private void SendTitle(TViewer viewer)
        {
            // Make the telnet client learn about the new title:
            string changeTitleCmd = String.Format("{0}{1}{2}",
                                                  (char)UnicodeCommand.TITLEBEGIN,
                                                  Title,
                                                  (char)UnicodeCommand.TITLEEND);
            viewer.Write(changeTitleCmd);
        }

        /// <summary>
        /// Tell the viewer to resize itself
        /// </summary>
        /// <param name="viewer">which viewer to send to</param>
        /// <param name="width">new width</param>
        /// <param name="height">new height</param>
        /// <param name="unconditional">if true, then send the resize message no matter what.  If false,
        /// then only send it if we calculate that the size changed.</param>
        private void ResizeAndRepaint(TViewer viewer, int width, int height, bool unconditional)
        {
            // Don't bother telling it to resize if its already the same size - this should stop resize spew looping:
            if (unconditional || viewer.ClientWidth != width || viewer.ClientHeight != height)
            {
                string resizeCmd = new string( new [] {(char)UnicodeCommand.RESIZESCREEN, (char)width, (char)height} );
                viewer.Write(resizeCmd);
                Repaint(viewer, true);
            }
        }

        /// <summary>
        /// Do the repaint of the viewer.
        /// </summary>
        /// <param name="viewer">which viewer to repaint</param>
        /// <param name="fullSync">if true, then ignore the diffing algorithm and just redraw everything.</param>
        private void Repaint(TViewer viewer, bool fullSync)
        {
            if (fullSync || previousScreens[viewer] == null)
            {
                RepaintFull(viewer);
                return;
            }

            // If the state of the screen reverse, or the visual bell flags, has changed since last time,
            // spit out the characters to change the state:
            if (viewer.ReverseScreen != Screen.ReverseScreen)
            {
                viewer.Write(Screen.ReverseScreen ? ((char)UnicodeCommand.REVERSESCREENMODE) : ((char)UnicodeCommand.NORMALSCREENMODE));
                viewer.ReverseScreen = Screen.ReverseScreen;
            }
            if (viewer.VisualBeep != Screen.VisualBeep)
            {
                viewer.Write(Screen.VisualBeep ? ((char)UnicodeCommand.VISUALBEEPMODE) : ((char)UnicodeCommand.AUDIOBEEPMODE));
                viewer.VisualBeep = Screen.VisualBeep;
            }
            string updateText = Snapshot.DiffFrom(previousScreens[viewer]);
            viewer.Write(updateText);

            previousScreens[viewer] = Snapshot.DeepCopy();
            for (int i = 0 ; i < Screen.BeepsPending ; ++i)
                viewer.Write((char)UnicodeCommand.BEEP); // The terminal's UnicodeMapper will convert this to ascii 0x07 if the right terminal type.
        }

        /// <summary>
        /// Cover the case where the whole screen needs to be repainted from scratch.
        /// <br/>
        /// It paints the screen as it is now, not the last refresh's <see cref="Snapshot"/>
        /// (VIEW-03): full repaints come between refreshes (attach, resize, repaint request),
        /// often right after a resize, which the old snapshot predates: its rows have the old
        /// width (a narrower client would wrap them) and are not the screen's new rows. The
        /// viewer's diff baseline is a copy made now, whose rows count as newer than every
        /// change so far, so what the painted snapshot lacked would never be sent.
        /// </summary>
        /// <param name="viewer">which viewer to paint to.</param>
        private void RepaintFull(TViewer viewer)
        {
            IScreenSnapShot current = new ScreenSnapShot(Screen);
            List<IScreenBufferLine> buffer = current.Buffer; // just to keep the name shorter below:

            // Sometimes the buffer is shorter than the terminal height if the resize JUST happened in the last Update():
            int rowsToPaint = Math.Min(Screen.RowCount, buffer.Count);

            viewer.Write((char)UnicodeCommand.CLEARSCREEN);
            // Restate the screen modes too: a full repaint also replaces output that a slow
            // client never received (see TelnetOutputQueue).
            viewer.Write(Screen.ReverseScreen ? (char)UnicodeCommand.REVERSESCREENMODE : (char)UnicodeCommand.NORMALSCREENMODE);
            viewer.ReverseScreen = Screen.ReverseScreen;
            viewer.Write(Screen.VisualBeep ? (char)UnicodeCommand.VISUALBEEPMODE : (char)UnicodeCommand.AUDIOBEEPMODE);
            viewer.VisualBeep = Screen.VisualBeep;
            for (int row = 0 ; row < rowsToPaint ; ++row)
            {
                IScreenBufferLine lineBuffer = buffer[row];
                int columnOfLastContent = -1;
                for (int col = 0 ; col < lineBuffer.Length ; ++col)
                {
                    char ch = lineBuffer[col];
                    switch (ch)
                    {
                        case (char)0x0000: // The buffer pads null chars into the 'dead' space of the screen past the last printed char.
                            break;
                        case (char)0x0009: // tab chars - really shouldn't be in the buffer.
                            break;
                        default:
                            columnOfLastContent = col;
                            break;
                    }
                }
                if (columnOfLastContent >= 0) // skip for empty lines
                {
                    string line = lineBuffer.ToString().Substring(0, columnOfLastContent+1);
                    viewer.Write(line);
                }
                if (row < rowsToPaint-1) //don't write the eoln for the lastmost line.
                    viewer.Write((char)UnicodeCommand.STARTNEXTLINE);
            }

            // ensure cursor locatiom (in case it's not at the bottom):
            viewer.Write(String.Format("{0}{1}{2}",
                                       (char)UnicodeCommand.TELEPORTCURSOR,
                                       // The next two are cast to char because, for example, a value of 76 should be
                                       // encoded as (char)76 (which is 'L'), rather than as '7' followed by '6':
                                       (char)current.CursorColumn,
                                       (char)current.CursorRow));

            previousScreens[viewer] = current.DeepCopy();
        }
    }
}
