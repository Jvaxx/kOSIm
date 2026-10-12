using System.Collections.Generic;
using kOS.Safe.UserIO;
using NUnit.Framework;

namespace kOS.Safe.Test.UserIO
{
    [TestFixture]
    public class TelnetInputDispatcherTest
    {
        private class RecordingSink : ITelnetInputSink
        {
            public readonly List<string> Calls = new List<string>();
            public void EnqueueText(string text) { Calls.Add("text:" + text); }
            public void EnqueueBreak() { Calls.Add("break"); }
            public void Negotiate(byte verb, byte option) { Calls.Add("negotiate:" + verb + "," + option); }
            public void ResizeRequested(int width, int height) { Calls.Add("resize:" + width + "x" + height); }
            public void TerminalTypeReceived(string terminalType) { Calls.Add("termtype:" + terminalType); }
            public void ProtocolNotice(TelnetInputEvent notice) { Calls.Add("notice:" + notice.Kind); }
        }

        private static TelnetInputEvent Text(string s) { return new TelnetInputEvent { Kind = TelnetInputEventKind.Text, Text = s }; }
        private static TelnetInputEvent Break() { return new TelnetInputEvent { Kind = TelnetInputEventKind.Break }; }
        private static TelnetInputEvent Resize(int w, int h) { return new TelnetInputEvent { Kind = TelnetInputEventKind.WindowSize, Width = w, Height = h }; }
        private static TelnetInputEvent Negotiation(byte verb, byte option) { return new TelnetInputEvent { Kind = TelnetInputEventKind.Negotiation, Command = verb, Option = option }; }

        [Test]
        public void TextAroundNegotiationsIsPassedOnAsOneString()
        {
            var sink = new RecordingSink();
            new TelnetInputDispatcher().Dispatch(new[] { Text("\x1b"), Negotiation(253, 1), Text("[A"), new TelnetInputEvent { Kind = TelnetInputEventKind.Ignored } }, false, sink);
            CollectionAssert.AreEqual(new[] { "negotiate:253,1", "notice:Ignored", "text:\x1b[A" }, sink.Calls);
        }

        [Test]
        public void TextIsFlushedBeforeResizeAndTerminalTypeChanges()
        {
            var sink = new RecordingSink();
            new TelnetInputDispatcher().Dispatch(new[] { Text("ab"), Resize(100, 40), Text("c"), new TelnetInputEvent { Kind = TelnetInputEventKind.TerminalType, Text = "VT100" }, Text("d") }, false, sink);
            CollectionAssert.AreEqual(new[] { "text:ab", "resize:100x40", "text:c", "termtype:VT100", "text:d" }, sink.Calls);
        }

        [Test]
        public void BreakDiscardsTypeAheadUntilTheSocketIsDrained()
        {
            var dispatcher = new TelnetInputDispatcher();
            var sink = new RecordingSink();

            dispatcher.Dispatch(new[] { Text("typed ahead"), Break(), Text("after") }, true, sink);
            Assert.IsTrue(dispatcher.IsDiscardingText);
            dispatcher.Dispatch(new[] { Text("still buffered"), Resize(90, 30) }, false, sink);
            Assert.IsFalse(dispatcher.IsDiscardingText);
            dispatcher.Dispatch(new[] { Text("new") }, false, sink);

            // Protocol messages inside discarded input are still honored:
            CollectionAssert.AreEqual(new[] { "break", "resize:90x30", "text:new" }, sink.Calls);
        }

        [Test]
        public void BreakWithNothingBufferedOnlyDiscardsItsOwnRead()
        {
            var dispatcher = new TelnetInputDispatcher();
            var sink = new RecordingSink();
            dispatcher.Dispatch(new[] { Text("x"), Break() }, false, sink);
            dispatcher.Dispatch(new[] { Text("y") }, false, sink);
            CollectionAssert.AreEqual(new[] { "break", "text:y" }, sink.Calls);
        }

        [Test]
        public void EmptyBatchesProduceNothing()
        {
            var sink = new RecordingSink();
            new TelnetInputDispatcher().Dispatch(new TelnetInputEvent[0], false, sink);
            CollectionAssert.IsEmpty(sink.Calls);
        }
    }
}
