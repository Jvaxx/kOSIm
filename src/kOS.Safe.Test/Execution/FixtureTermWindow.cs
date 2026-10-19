using System;
using System.Collections.Generic;
using System.Linq;
using kOS.Safe.Screen;
using kOS.Safe.Test.UserIO;
using kOS.Safe.UserIO;

namespace kOS.Safe.Test.Execution
{
    /// <summary>
    /// The game's <c>kOS.Screen.TermWindow</c> (Unity, not loadable headlessly) as an input
    /// host: the real <see cref="TerminalInputRouter{TClient}"/> for telnet clients modeled
    /// by <see cref="TelnetServerInputModel"/>, over the real prompt of a
    /// <see cref="ScriptHost"/>. Only the GUI, sounds and telnet output are left out: a
    /// repaint request and a window close are counted. Keep the host members in step with
    /// <c>TermWindow</c>'s <c>ITerminalInputHost</c> implementation.
    /// <br/>
    /// The limits default to the game's and can be changed to measure their effect.
    /// </summary>
    internal sealed class FixtureTermWindow : ITerminalInputHost<TelnetServerInputModel>
    {
        private readonly FixtureInterpreter screen; // the game's shared.Screen and shared.Interpreter
        private readonly List<TelnetServerInputModel> telnets = new List<TelnetServerInputModel>();
        private readonly TerminalInputRouter<TelnetServerInputModel> router;

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
            router = new TerminalInputRouter<TelnetServerInputModel>(this);
        }

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
            telnets.Add(telnet);
            RepaintRequests[telnet] = 0;
            return telnet;
        }

        public bool IsAttached(TelnetServerInputModel telnet) { return telnets.Contains(telnet); }

        /// <summary>Everything not yet given to the prompt: telnet backlogs and the type-ahead.</summary>
        public int PendingInput { get { return TypeAhead.Count + telnets.Sum(t => t.Queue.Count); } }

        /// <summary>The terminal window's per-frame input work, telnet server's included.</summary>
        public void Update()
        {
            foreach (TelnetServerInputModel telnet in telnets.ToArray())
                telnet.MainThreadUpdate();
            LastTelnetKeys = router.ProcessClientInput(telnets.Where(telnet => telnet.IsConnectedToProcessor).ToArray());
            LastPromptKeys = router.ProcessUnconsumedInput();
            MaxTelnetKeysPerUpdate = Math.Max(MaxTelnetKeysPerUpdate, LastTelnetKeys);
            MaxPromptKeysPerUpdate = Math.Max(MaxPromptKeysPerUpdate, LastPromptKeys);
        }

        public bool ProcessOneInputChar(char ch, TelnetServerInputModel whichTelnet, bool allowQueue, bool forceQueue)
        {
            return router.ProcessOneInputChar(ch, whichTelnet, allowQueue, forceQueue);
        }

        IScreenBuffer ITerminalInputHost<TelnetServerInputModel>.Screen { get { return screen; } }

        IInterpreter ITerminalInputHost<TelnetServerInputModel>.Interpreter { get { return screen; } }

        bool ITerminalInputHost<TelnetServerInputModel>.HasBooted { get { return Booted; } }

        bool ITerminalInputHost<TelnetServerInputModel>.IsClientAttached(TelnetServerInputModel client)
        {
            return client.IsConnectedToProcessor && telnets.Contains(client);
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
            client.IsConnectedToProcessor = false;
            telnets.Remove(client);
        }

        void ITerminalInputHost<TelnetServerInputModel>.RepaintClient(TelnetServerInputModel client)
        {
            ++RepaintRequests[client];
        }

        void ITerminalInputHost<TelnetServerInputModel>.KeyClick() { ++KeyClicks; }
    }
}
