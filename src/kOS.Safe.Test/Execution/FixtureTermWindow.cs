using System;
using System.Collections.Generic;
using System.Linq;
using kOS.Safe.Screen;
using kOS.Safe.Test.UserIO;
using kOS.Safe.UserIO;

namespace kOS.Safe.Test.Execution
{
    /// <summary>
    /// The game's <c>kOS.Screen.TermWindow</c> (Unity, not loadable headlessly) without its
    /// GUI: the real <see cref="TerminalInputRouter{TClient}"/> and
    /// <see cref="TerminalSession{TViewer}"/> for telnet clients modeled by
    /// <see cref="TelnetServerInputModel"/>, over the real prompt and screen of a
    /// <see cref="ScriptHost"/>. The GUI and sounds are left out: a window close is counted,
    /// and so is a repaint request (which is also carried out). Keep the host members, the
    /// session calls and <see cref="Update"/> in step with <c>TermWindow</c>.
    /// <br/>
    /// The limits default to the game's and can be changed to measure their effect.
    /// </summary>
    internal sealed class FixtureTermWindow : ITerminalInputHost<TelnetServerInputModel>
    {
        private readonly FixtureInterpreter screen; // the game's shared.Screen and shared.Interpreter
        private readonly TerminalSession<TelnetServerInputModel> session;
        private readonly TerminalInputRouter<TelnetServerInputModel> router;

        public const string Title = "fixture CPU";

        public readonly Dictionary<TelnetServerInputModel, int> RepaintRequests = new Dictionary<TelnetServerInputModel, int>();
        public int CloseRequests { get; private set; }
        public int KeyClicks { get; private set; }
        /// <summary>The processor's <c>HasBooted</c>.</summary>
        public bool Booted = true;
        public int LastTelnetKeys { get; private set; }
        public int LastPromptKeys { get; private set; }
        public int MaxTelnetKeysPerUpdate { get; private set; }
        public int MaxPromptKeysPerUpdate { get; private set; }

        public FixtureTermWindow(ScriptHost host)
        {
            screen = host.Screen;
            session = new TerminalSession<TelnetServerInputModel>(DateTime.Now) { Screen = screen };
            router = new TerminalInputRouter<TelnetServerInputModel>(this);
            GuiPaste = new TerminalGuiPaste<TelnetServerInputModel>(router, () => screen);
            // As TermWindow.AttachTo:
            resizeNotifier = sb => { session.ScreenResized(); return 0; };
            screen.AddResizeNotifier(resizeNotifier);
            session.ChangeTitle(Title);
        }

        private readonly ScreenBuffer.ResizeNotifier resizeNotifier;

        /// <summary>
        /// As <c>TermWindow.OnDestroy</c> (with its processor): the session is disposed, which
        /// detaches every client, and the screen forgets the window's resize notifier.
        /// </summary>
        public void Destroy()
        {
            session.Dispose();
            screen.RemoveResizeNotifier(resizeNotifier);
        }

        public TerminalSession<TelnetServerInputModel> Session { get { return session; } }

        public int TelnetCharsPerUpdate
        {
            get { return router.ClientCharsPerUpdate; }
            set { router.ClientCharsPerUpdate = value; }
        }

        public int TypeAheadLimit
        {
            get { return router.TypeAheadLimit; }
            set { router.TypeAheadLimit = value; }
        }

        public int PromptCharsPerUpdate
        {
            get { return router.PromptCharsPerUpdate; }
            set { router.PromptCharsPerUpdate = value; }
        }

        public TimeSpan TimePerUpdate
        {
            get { return router.TimePerUpdate; }
            set { router.TimePerUpdate = value; }
        }

        public Queue<char> TypeAhead { get { return screen.CharInputQueue; } }

        public TelnetServerInputModel AttachTelnet(string terminalType)
        {
            var telnet = new TelnetServerInputModel();
            ((ITelnetInputSink)telnet).TerminalTypeReceived(terminalType);
            RepaintRequests[telnet] = 0;
            session.AttachViewer(telnet); // TelnetSingletonServer.ConnectToProcessor
            return telnet;
        }

        public bool IsAttached(TelnetServerInputModel telnet) { return session.HasViewer(telnet); }

        /// <summary>Everything not yet given to the prompt: telnet backlogs and the type-ahead.</summary>
        public int PendingInput { get { return TypeAhead.Count + session.Viewers.Sum(t => t.Queue.Count); } }

        /// <summary>
        /// The terminal window's per-frame work apart from the GUI (the telnet server's input
        /// work included): the session's refresh, then the input.
        /// </summary>
        public void Update()
        {
            Update(DateTime.Now);
        }

        /// <returns>whether the clients were sent the screen's changes</returns>
        public bool Update(DateTime now)
        {
            foreach (TelnetServerInputModel telnet in session.Viewers)
                telnet.MainThreadUpdate();
            bool refreshed = session.Update(now);
            LastTelnetKeys = router.ProcessClientInput(session.Viewers.Where(telnet => telnet.IsConnectedToProcessor).ToArray());
            LastPromptKeys = router.ProcessUnconsumedInput();
            MaxTelnetKeysPerUpdate = Math.Max(MaxTelnetKeysPerUpdate, LastTelnetKeys);
            MaxPromptKeysPerUpdate = Math.Max(MaxPromptKeysPerUpdate, LastPromptKeys);
            if (refreshed)
                screen.BeepsPending = 0; // TermWindow hands them to the GUI
            return refreshed;
        }

        public bool ProcessOneInputChar(char ch, TelnetServerInputModel whichTelnet, bool allowQueue, bool forceQueue)
        {
            return router.ProcessOneInputChar(ch, whichTelnet, allowQueue, forceQueue);
        }

        /// <summary>The GUI paste, as <c>TermWindow</c>'s (Ctrl+V, Shift+Insert and the confirmation call it).</summary>
        public TerminalGuiPaste<TelnetServerInputModel> GuiPaste { get; private set; }

        /// <summary>Queue clipboard text as a confirmed GUI paste does (the router entry <see cref="GuiPaste"/> uses).</summary>
        public bool QueuePaste(string text)
        {
            return router.QueuePaste(TerminalPaste.Prepare(text));
        }

        IScreenBuffer ITerminalInputHost<TelnetServerInputModel>.Screen { get { return screen; } }

        IInterpreter ITerminalInputHost<TelnetServerInputModel>.Interpreter { get { return screen; } }

        bool ITerminalInputHost<TelnetServerInputModel>.HasBooted { get { return Booted; } }

        bool ITerminalInputHost<TelnetServerInputModel>.IsClientAttached(TelnetServerInputModel client)
        {
            return client.IsConnectedToProcessor && session.HasViewer(client);
        }

        bool ITerminalInputHost<TelnetServerInputModel>.TryReadClientChar(TelnetServerInputModel client, bool includeText, out char ch)
        {
            return client.TryReadChar(includeText, out ch);
        }

        void ITerminalInputHost<TelnetServerInputModel>.CloseWindow()
        {
            ++CloseRequests;
        }

        void ITerminalInputHost<TelnetServerInputModel>.DetachClient(TelnetServerInputModel client)
        {
            session.DetachViewer(client);
        }

        void ITerminalInputHost<TelnetServerInputModel>.RepaintClient(TelnetServerInputModel client)
        {
            ++RepaintRequests[client];
            session.RepaintViewer(client);
        }

        void ITerminalInputHost<TelnetServerInputModel>.KeyClick() { ++KeyClicks; }
    }
}
