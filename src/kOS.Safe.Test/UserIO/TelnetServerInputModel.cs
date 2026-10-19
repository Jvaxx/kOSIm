using System;
using System.Collections.Generic;
using System.Text;
using kOS.Safe.Screen;
using kOS.Safe.UserIO;

namespace kOS.Safe.Test.UserIO
{
    /// <summary>
    /// The input half of the game's <c>kOS.UserIO.TelnetSingletonServer</c> (game assembly,
    /// not loadable headlessly): the real parser and dispatcher feeding the real
    /// <see cref="TelnetInputPipeline"/> and <see cref="TelnetInputQueue"/>. Keep the sink
    /// methods in step with the server's <c>ITelnetInputSink</c> implementation. As an
    /// <see cref="ITerminalViewer"/> it records the output it is sent, unmapped, and plays it
    /// on a <see cref="ViewerScreenModel"/>; the output queue and mapper, negotiation
    /// replies, logging and the welcome menu are left out.
    /// </summary>
    internal sealed class TelnetServerInputModel : ITelnetInputSink, ITerminalViewer
    {
        private readonly StringBuilder output = new StringBuilder();

        private readonly TelnetInputParser parser = new TelnetInputParser();
        private readonly TelnetInputDispatcher dispatcher = new TelnetInputDispatcher();

        public TelnetServerInputModel(int inputCapacity = TelnetInputQueue.DEFAULT_CAPACITY)
        {
            Queue = new TelnetInputQueue(inputCapacity);
            Pipeline = new TelnetInputPipeline(Queue, "INITIAL_UNSET", TelnetInputPipeline.DefaultSequenceTimeout);
            ClientWidth = 80;
            ClientHeight = 24;
            ClientTerminalType = "INITIAL_UNSET";
            IsConnectedToProcessor = true;
            Model = new ViewerScreenModel(ClientWidth, ClientHeight);
        }

        public TelnetInputQueue Queue { get; private set; }
        public TelnetInputPipeline Pipeline { get; private set; }
        public int ClientWidth { get; private set; }
        public int ClientHeight { get; private set; }
        public string ClientTerminalType { get; private set; }
        public bool IsConnectedToProcessor { get; set; }
        public bool ReverseScreen { get; set; }
        public bool VisualBeep { get; set; }

        /// <summary>Everything the terminal window sent this client.</summary>
        public string Output { get { return output.ToString(); } }

        /// <summary>The client's display, as that output draws it.</summary>
        public ViewerScreenModel Model { get; private set; }

        public void Write(char ch)
        {
            Write(ch.ToString());
        }

        public void Write(string str)
        {
            output.Append(str);
            Model.Apply(str);
        }

        /// <summary>The server's <c>DisconnectFromProcessor</c>, without the goodbye output.</summary>
        void ITerminalViewer.Detach()
        {
            IsConnectedToProcessor = false;
        }

        /// <summary>The game-thread clock the pipeline's sequence timeout uses.</summary>
        public TimeSpan Now { get; set; }

        /// <summary>
        /// One socket read. <paramref name="moreDataAvailable"/> is what the socket reports
        /// after it; the break policy depends on it.
        /// </summary>
        public void Receive(byte[] bytes, bool moreDataAvailable = false)
        {
            dispatcher.Dispatch(parser.Feed(bytes, 0, bytes.Length), moreDataAvailable, this);
        }

        /// <summary>Typed text as one read, UTF-8 encoded as a client sends it.</summary>
        public void Send(string text)
        {
            Receive(Encoding.UTF8.GetBytes(text));
        }

        /// <summary>The telnet interrupt command, IAC IP, as its own read.</summary>
        public void SendInterrupt()
        {
            Receive(new byte[] { TelnetInputParser.IAC, TelnetInputParser.IP });
        }

        /// <summary>The connection closed: what the server's reader does at end of stream.</summary>
        public void Finish()
        {
            dispatcher.Dispatch(parser.Finish(), false, this);
        }

        /// <summary>What the server's <c>MainThreadUpdate</c> does with the input each frame.</summary>
        public void MainThreadUpdate()
        {
            Pipeline.FlushIfIdle(Now);
        }

        public bool TryReadChar(bool includeText, out char ch)
        {
            return Queue.TryDequeue(includeText, out ch);
        }

        void ITelnetInputSink.EnqueueText(string text)
        {
            Pipeline.AddText(text, Now);
        }

        void ITelnetInputSink.EnqueueBreak()
        {
            Pipeline.Break();
        }

        void ITelnetInputSink.Negotiate(byte verb, byte option) { }

        void ITelnetInputSink.ResizeRequested(int width, int height)
        {
            int newWidth = TelnetProtocol.NormalizeDimension(width, ClientWidth, ScreenBuffer.MAX_COLUMNS);
            int newHeight = TelnetProtocol.NormalizeDimension(height, ClientHeight, ScreenBuffer.MAX_ROWS);
            if (newWidth == ClientWidth && newHeight == ClientHeight)
                return;
            ClientWidth = newWidth;
            ClientHeight = newHeight;
            Queue.EnqueueResize(newWidth, newHeight);
            if (IsConnectedToProcessor)
                Queue.RequestRepaint();
        }

        void ITelnetInputSink.TerminalTypeReceived(string terminalType)
        {
            if (ClientTerminalType != terminalType)
            {
                Pipeline.SetTerminalType(terminalType);
                ClientTerminalType = terminalType;
            }
        }

        void ITelnetInputSink.ProtocolNotice(TelnetInputEvent notice) { }

        /// <summary>
        /// Keys as one line each: a run of typed characters as <c>text "..."</c>, a resize as
        /// <c>RESIZESCREEN 100x30</c>, other kOS key codes by name.
        /// </summary>
        public static List<string> DescribeKeys(string keys)
        {
            var lines = new List<string>();
            var text = new StringBuilder();
            Action flushText = () =>
            {
                if (text.Length > 0)
                    lines.Add("text \"" + EscapeText(text.ToString()) + "\"");
                text.Length = 0;
            };
            for (int i = 0; i < keys.Length; ++i)
            {
                char ch = keys[i];
                if (ch == (char)UnicodeCommand.RESIZESCREEN && i + 2 < keys.Length)
                {
                    flushText();
                    lines.Add("RESIZESCREEN " + (int)keys[i + 1] + "x" + (int)keys[i + 2]);
                    i += 2;
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

        public static string EscapeText(string text)
        {
            var sb = new StringBuilder();
            foreach (char ch in text)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    default:
                        if (ch < 0x20 || ch == 0x7f)
                            sb.AppendFormat("\\x{0:x2}", (int)ch);
                        else
                            sb.Append(ch);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
