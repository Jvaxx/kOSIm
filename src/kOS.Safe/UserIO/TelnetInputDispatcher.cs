using System.Collections.Generic;
using System.Text;

namespace kOS.Safe.UserIO
{
    /// <summary>
    /// Receives parsed telnet input on the connection's reader thread. Implementations
    /// must not call Unity or KSP.
    /// </summary>
    public interface ITelnetInputSink
    {
        void EnqueueText(string text);
        void EnqueueBreak();
        void Negotiate(byte verb, byte option);
        void ResizeRequested(int width, int height);
        void TerminalTypeReceived(string terminalType);
        void ProtocolNotice(TelnetInputEvent notice);
    }

    /// <summary>
    /// Applies kOS's ordering and interrupt policy to parsed telnet input:
    /// <list type="bullet">
    /// <item>Adjacent text is passed on as one string so terminal mappers see whole
    /// key sequences where possible. Text is flushed before a resize, so the input
    /// queue keeps stream order, and before a terminal type change, so each part is
    /// converted by the mapper that was current when it arrived.</item>
    /// <item>A break discards the text that arrived in the same read and any text
    /// received until the socket has no more buffered input, as the upstream server
    /// did. Protocol messages in discarded input are still processed, so parser
    /// state and window size stay correct.</item>
    /// </list>
    /// </summary>
    public sealed class TelnetInputDispatcher
    {
        private readonly StringBuilder pendingText = new StringBuilder();

        /// <summary>True while text is being discarded after a break.</summary>
        public bool IsDiscardingText { get; private set; }

        /// <param name="events">events from one socket read, in stream order</param>
        /// <param name="moreDataAvailable">whether the socket already holds more input</param>
        public void Dispatch(IList<TelnetInputEvent> events, bool moreDataAvailable, ITelnetInputSink sink)
        {
            foreach (TelnetInputEvent ev in events)
            {
                if (ev.Kind == TelnetInputEventKind.Break)
                    IsDiscardingText = true;
            }

            foreach (TelnetInputEvent ev in events)
            {
                switch (ev.Kind)
                {
                    case TelnetInputEventKind.Text:
                        if (!IsDiscardingText)
                            pendingText.Append(ev.Text);
                        break;
                    case TelnetInputEventKind.Break:
                        sink.EnqueueBreak();
                        break;
                    case TelnetInputEventKind.Negotiation:
                        sink.Negotiate(ev.Command, ev.Option);
                        break;
                    case TelnetInputEventKind.WindowSize:
                        FlushText(sink);
                        sink.ResizeRequested(ev.Width, ev.Height);
                        break;
                    case TelnetInputEventKind.TerminalType:
                        FlushText(sink); // earlier text belongs to the old terminal mapper
                        sink.TerminalTypeReceived(ev.Text);
                        break;
                    default:
                        sink.ProtocolNotice(ev);
                        break;
                }
            }
            FlushText(sink);

            if (!moreDataAvailable)
                IsDiscardingText = false;
        }

        private void FlushText(ITelnetInputSink sink)
        {
            if (pendingText.Length == 0)
                return;
            string text = pendingText.ToString();
            pendingText.Length = 0;
            sink.EnqueueText(text);
        }
    }
}
