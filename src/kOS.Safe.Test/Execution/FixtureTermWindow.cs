using System;
using System.Collections.Generic;
using System.Linq;
using kOS.Safe.Screen;
using kOS.Safe.Test.UserIO;
using kOS.Safe.UserIO;

namespace kOS.Safe.Test.Execution
{
    /// <summary>
    /// The input routing of the game's <c>kOS.Screen.TermWindow</c> (Unity, not loadable
    /// headlessly) for telnet clients modeled by <see cref="TelnetServerInputModel"/>:
    /// <c>ProcessTelnetInput</c>, <c>ProcessUnconsumedInput</c>, <c>ProcessOneInputChar</c>,
    /// <c>Type</c>, <c>SpecialKey</c> and <c>WipeAllTypeAhead</c>, over the real prompt of a
    /// <see cref="ScriptHost"/>. Keep it in step with <c>src/kOS/Screen/TermWindow.cs</c>;
    /// only the GUI, sounds and telnet output are left out (a repaint request is counted).
    /// <br/>
    /// The limits default to the game's and can be changed to measure their effect.
    /// </summary>
    internal sealed class FixtureTermWindow
    {
        private enum ExpectNextChar { NORMAL, RESIZEWIDTH, RESIZEHEIGHT }

        private readonly FixtureInterpreter screen; // the game's shared.Screen and shared.Interpreter
        private readonly List<TelnetServerInputModel> telnets = new List<TelnetServerInputModel>();
        private int nextTelnetToDrain;
        private ExpectNextChar inputExpected = ExpectNextChar.NORMAL;
        private int pendingWidth;

        public int TelnetCharsPerUpdate = TerminalInputPump.TELNET_CHARS_PER_UPDATE;
        public int TypeAheadLimit = TerminalInputPump.TYPE_AHEAD_LIMIT;
        public int PromptCharsPerUpdate = TerminalInputPump.PROMPT_CHARS_PER_UPDATE;
        public TimeSpan TimePerUpdate = TerminalInputPump.TimePerUpdate;

        public readonly Dictionary<TelnetServerInputModel, int> RepaintRequests = new Dictionary<TelnetServerInputModel, int>();
        public int LastTelnetKeys { get; private set; }
        public int LastPromptKeys { get; private set; }
        public int MaxTelnetKeysPerUpdate { get; private set; }
        public int MaxPromptKeysPerUpdate { get; private set; }

        public FixtureTermWindow(ScriptHost host)
        {
            screen = host.Screen;
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
            LastTelnetKeys = ProcessTelnetInput();
            LastPromptKeys = ProcessUnconsumedInput();
            MaxTelnetKeysPerUpdate = Math.Max(MaxTelnetKeysPerUpdate, LastTelnetKeys);
            MaxPromptKeysPerUpdate = Math.Max(MaxPromptKeysPerUpdate, LastPromptKeys);
        }

        private int ProcessTelnetInput()
        {
            TelnetServerInputModel[] tempTelnetList = telnets.Where(telnet => telnet.IsConnectedToProcessor).ToArray();
            var sources = new TerminalInputPump.KeySource[tempTelnetList.Length];
            for (int i = 0; i < tempTelnetList.Length; ++i)
            {
                TelnetServerInputModel telnet = tempTelnetList[i];
                sources[i] = (bool includeText, out char ch) =>
                {
                    if (!telnet.IsConnectedToProcessor || !telnets.Contains(telnet))
                    {
                        ch = '\0';
                        return false;
                    }
                    return telnet.TryReadChar(includeText, out ch);
                };
            }
            return TerminalInputPump.DrainTelnets(sources, ref nextTelnetToDrain,
                () => screen.CharInputQueue.Count, TypeAheadLimit,
                () => new InputBudget(TelnetCharsPerUpdate, TimePerUpdate),
                (index, ch) => ProcessOneInputChar(ch, tempTelnetList[index], true, true));
        }

        private int ProcessUnconsumedInput()
        {
            if (!screen.IsWaitingForCommand())
                return 0;
            return TerminalInputPump.FeedPrompt(screen.CharInputQueue, () => screen.IsWaitingForCommand(),
                key => ProcessOneInputChar(key, null, false, false),
                new InputBudget(PromptCharsPerUpdate, TimePerUpdate));
        }

        public bool ProcessOneInputChar(char ch, TelnetServerInputModel whichTelnet, bool allowQueue, bool forceQueue)
        {
            switch (inputExpected)
            {
                case ExpectNextChar.RESIZEWIDTH:
                    pendingWidth = ch;
                    inputExpected = ExpectNextChar.RESIZEHEIGHT;
                    return true;
                case ExpectNextChar.RESIZEHEIGHT:
                    int height = ch;
                    screen.SetSize(height, pendingWidth);
                    inputExpected = ExpectNextChar.NORMAL;
                    return true;
                default:
                    break;
            }

            if (!IsSpecial(ch))
                return Type(ch, allowQueue, forceQueue);

            switch (ch)
            {
                case (char)UnicodeCommand.DELETELEFT:
                case (char)8:
                    Type((char)8, allowQueue, forceQueue);
                    break;
                case (char)UnicodeCommand.STARTNEXTLINE:
                case '\r':
                    Type('\r', allowQueue, forceQueue);
                    break;
                case '\t':
                    Type('\t', allowQueue, forceQueue);
                    break;
                case (char)UnicodeCommand.RESIZESCREEN:
                    inputExpected = ExpectNextChar.RESIZEWIDTH;
                    break;
                case (char)0x0004: // Ctrl-D
                case (char)0x0018: // Ctrl-X
                    if (screen.IsAtStartOfCommand() && whichTelnet != null)
                    {
                        whichTelnet.IsConnectedToProcessor = false; // DetachTelnet
                        telnets.Remove(whichTelnet);
                    }
                    break;
                case (char)UnicodeCommand.REQUESTREPAINT:
                    if (whichTelnet != null)
                        ++RepaintRequests[whichTelnet];
                    break;
                default:
                    return SpecialKey(ch, allowQueue, forceQueue);
            }
            return true;
        }

        private static bool IsSpecial(char c)
        {
            if (c < 0x0020 || c > 0xE000)
                return true;
            return Enum.IsDefined(typeof(UnicodeCommand), (int)c);
        }

        private bool Type(char ch, bool doQueuing, bool forceQueue)
        {
            if (!forceQueue && screen.IsWaitingForCommand())
            {
                screen.Type(ch);
                return true;
            }
            if (doQueuing)
            {
                screen.CharInputQueue.Enqueue(ch);
                return true;
            }
            return false;
        }

        private bool SpecialKey(char key, bool doQueuing, bool forceQueue)
        {
            bool rudeQueueSkipping = key == (char)UnicodeCommand.BREAK;
            if (rudeQueueSkipping)
            {
                forceQueue = false;
                WipeAllTypeAhead();
            }
            if (!forceQueue && (screen.IsWaitingForCommand() || rudeQueueSkipping))
            {
                screen.SpecialKey(key);
                return true;
            }
            if (doQueuing)
            {
                screen.CharInputQueue.Enqueue(key);
                return true;
            }
            return false;
        }

        private void WipeAllTypeAhead()
        {
            screen.CharInputQueue.Clear();
            inputExpected = ExpectNextChar.NORMAL;
        }
    }
}
