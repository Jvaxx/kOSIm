using System;
using kOS.Safe.UserIO;
using NUnit.Framework;

namespace kOS.Safe.Test.UserIO
{
    /// <summary>
    /// <see cref="TelnetInputPipeline"/>: decoding with the client's terminal type, an
    /// unfinished key sequence released only after the timeout, and interrupts discarding
    /// unfinished sequences.
    /// </summary>
    [TestFixture]
    public class TelnetInputPipelineTest
    {
        private const char ESC = (char)0x1b;
        private static readonly TimeSpan Timeout = TelnetInputPipeline.DefaultSequenceTimeout;

        // TimeSpan.FromMilliseconds rounds to whole milliseconds on .NET Framework.
        private static TimeSpan Ms(double milliseconds) { return TimeSpan.FromTicks((long)(milliseconds * TimeSpan.TicksPerMillisecond)); }

        private static TelnetInputPipeline NewPipeline(string terminalType = "XTERM")
        {
            return new TelnetInputPipeline(new TelnetInputQueue(), terminalType, Timeout);
        }

        [Test]
        public void DefaultTimeoutIsATenthOfASecond()
        {
            Assert.AreEqual(Ms(100), TelnetInputPipeline.DefaultSequenceTimeout);
        }

        [TestCase("INITIAL_UNSET", TerminalInputDialect.Plain)]
        [TestCase("ANSI", TerminalInputDialect.Plain)]
        [TestCase("VT100", TerminalInputDialect.VT100)]
        [TestCase("vt102", TerminalInputDialect.VT100)]
        [TestCase("XTERM", TerminalInputDialect.Xterm)]
        [TestCase("xterm-256color", TerminalInputDialect.Xterm)]
        [TestCase(null, TerminalInputDialect.Plain)]
        public void DecodesWithTheClientsTerminalType(string terminalType, TerminalInputDialect expected)
        {
            Assert.AreEqual(expected, NewPipeline(terminalType).Dialect);
        }

        [Test]
        public void KeySplitAcrossReadsIsOneKey()
        {
            TelnetInputPipeline pipeline = NewPipeline();
            pipeline.AddText("a" + ESC, Ms(0));
            pipeline.AddText("[", Ms(1));
            Assert.AreEqual("a", pipeline.Queue.DequeueAll());
            pipeline.AddText("Ab", Ms(2));
            Assert.AreEqual((char)UnicodeCommand.UPCURSORONE + "b", pipeline.Queue.DequeueAll());
        }

        [Test]
        public void UnfinishedSequenceIsReleasedOnlyAfterTheTimeout()
        {
            TelnetInputPipeline pipeline = NewPipeline();
            pipeline.AddText(ESC.ToString(), Ms(1000));
            Assert.IsFalse(pipeline.FlushIfIdle(Ms(1000)));
            Assert.IsFalse(pipeline.FlushIfIdle(Ms(1099.9)));
            Assert.AreEqual("", pipeline.Queue.DequeueAll());
            Assert.IsTrue(pipeline.FlushIfIdle(Ms(1100)));
            Assert.AreEqual(ESC.ToString(), pipeline.Queue.DequeueAll(), "a lone Escape key arrives");
            Assert.IsFalse(pipeline.FlushIfIdle(Ms(5000)), "nothing left to flush");
        }

        [Test]
        public void TimeoutCountsFromTheLatestText()
        {
            TelnetInputPipeline pipeline = NewPipeline();
            pipeline.AddText(ESC.ToString(), Ms(0));
            pipeline.AddText("[", Ms(90));
            Assert.IsFalse(pipeline.FlushIfIdle(Ms(150)));
            pipeline.AddText("5~", Ms(160));
            Assert.AreEqual(((char)UnicodeCommand.PAGEUPCURSOR).ToString(), pipeline.Queue.DequeueAll());
        }

        [Test]
        public void TimeoutAfterCrMakesALaterLfASecondEnter()
        {
            // A partner arriving that late was a separate key press.
            string enter = ((char)UnicodeCommand.STARTNEXTLINE).ToString();
            TelnetInputPipeline pipeline = NewPipeline("VT100");
            pipeline.AddText("\r", Ms(0));
            pipeline.AddText("\n", Ms(50));
            Assert.AreEqual(enter, pipeline.Queue.DequeueAll());
            pipeline.AddText("\r", Ms(100));
            Assert.IsTrue(pipeline.FlushIfIdle(Ms(200)));
            pipeline.AddText("\n", Ms(300));
            Assert.AreEqual(enter + enter, pipeline.Queue.DequeueAll());
        }

        [Test]
        public void SwitchingEncodingReleasesTheUnfinishedSequenceFirst()
        {
            TelnetInputPipeline pipeline = NewPipeline("XTERM");
            pipeline.AddText(ESC.ToString(), Ms(0));
            pipeline.SetTerminalType("ANSI");
            Assert.AreEqual(TerminalInputDialect.Plain, pipeline.Dialect);
            Assert.AreEqual(ESC.ToString(), pipeline.Queue.DequeueAll());
            pipeline.AddText("[A", Ms(1));
            Assert.AreEqual("[A", pipeline.Queue.DequeueAll(), "the plain dialect has no sequences");
        }

        [Test]
        public void SameEncodingUnderAnotherNameKeepsTheUnfinishedSequence()
        {
            TelnetInputPipeline pipeline = NewPipeline("XTERM");
            pipeline.AddText(ESC + "[", Ms(0));
            pipeline.SetTerminalType("xterm-256color");
            Assert.AreEqual("", pipeline.Queue.DequeueAll());
            pipeline.AddText("D", Ms(1));
            Assert.AreEqual(((char)UnicodeCommand.LEFTCURSORONE).ToString(), pipeline.Queue.DequeueAll());
        }

        [Test]
        public void BreakDiscardsTheUnfinishedSequenceAndEarlierText()
        {
            TelnetInputPipeline pipeline = NewPipeline();
            pipeline.AddText("typed ahead" + ESC + "[", Ms(0));
            pipeline.Break();
            pipeline.AddText("A", Ms(1));
            Assert.AreEqual((char)UnicodeCommand.BREAK + "A", pipeline.Queue.DequeueAll());
            Assert.IsFalse(pipeline.FlushIfIdle(Ms(1000)));
        }

        [Test]
        public void NullQueueIsRejected()
        {
            Assert.Throws<ArgumentNullException>(() => new TelnetInputPipeline(null, "XTERM", Timeout));
        }
    }
}
