using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using kOS.Safe.UserIO;
using NUnit.Framework;

namespace kOS.Safe.Test.UserIO
{
    /// <summary>
    /// <see cref="TelnetInputQueue"/>: typed text in order and bounded, control events
    /// (interrupt, resize, repaint) never behind typed text, overflow reported once per
    /// episode.
    /// </summary>
    [TestFixture]
    public class TelnetInputQueueTest
    {
        private const char BREAK = (char)UnicodeCommand.BREAK;
        private const char REPAINT = (char)UnicodeCommand.REQUESTREPAINT;
        private const char RESIZE = (char)UnicodeCommand.RESIZESCREEN;

        private static string ReadAll(TelnetInputQueue queue, bool includeText = true)
        {
            var sb = new StringBuilder();
            char ch;
            while (queue.TryDequeue(includeText, out ch))
                sb.Append(ch);
            return sb.ToString();
        }

        private static string Read(TelnetInputQueue queue, int count)
        {
            var sb = new StringBuilder();
            char ch;
            for (int i = 0; i < count && queue.TryDequeue(true, out ch); ++i)
                sb.Append(ch);
            return sb.ToString();
        }

        [Test]
        public void TextIsReadInOrder()
        {
            var queue = new TelnetInputQueue();
            queue.EnqueueText("print ");
            queue.EnqueueText("1.");
            Assert.AreEqual(8, queue.TextCount);
            Assert.AreEqual("print 1.", ReadAll(queue));
            char ch;
            Assert.IsFalse(queue.TryDequeue(true, out ch));
            Assert.AreEqual('\0', ch);
        }

        [Test]
        public void ReaderThatCannotTakeTextStillGetsControls()
        {
            var queue = new TelnetInputQueue();
            queue.EnqueueText("abc");
            Assert.IsFalse(queue.HasInput(false));
            Assert.AreEqual("", ReadAll(queue, false));
            queue.RequestRepaint();
            Assert.IsTrue(queue.HasInput(false));
            Assert.AreEqual(REPAINT.ToString(), ReadAll(queue, false));
            Assert.AreEqual("abc", ReadAll(queue));
        }

        [Test]
        public void RepaintIsReadBeforeEarlierTextAndCoalesces()
        {
            var queue = new TelnetInputQueue();
            queue.EnqueueText("ab" + REPAINT + "c" + REPAINT);
            queue.RequestRepaint();
            Assert.AreEqual(REPAINT + "abc", ReadAll(queue));
        }

        [Test]
        public void BreakDiscardsOnlyTheTextTypedBeforeIt()
        {
            var queue = new TelnetInputQueue();
            queue.EnqueueText("wiped");
            queue.EnqueueBreak();
            queue.EnqueueText("kept");
            Assert.AreEqual(BREAK + "kept", ReadAll(queue));
            Assert.AreEqual(5, queue.DiscardedByBreakCount);
            Assert.AreEqual(0, queue.DroppedCount);
        }

        [Test]
        public void TypedCtrlCActsLikeAProtocolBreak()
        {
            // The decoder turns Ctrl-C into BREAK inside typed text.
            var queue = new TelnetInputQueue();
            queue.EnqueueText("wiped" + BREAK + "kept");
            Assert.AreEqual(BREAK + "kept", ReadAll(queue));
            Assert.AreEqual(5, queue.DiscardedByBreakCount);
        }

        [Test]
        public void BreaksBeforeTheFirstIsReadCoalesce()
        {
            var queue = new TelnetInputQueue();
            queue.EnqueueText("a");
            queue.EnqueueBreak();
            queue.EnqueueText("b");
            queue.EnqueueBreak();
            queue.EnqueueText("c");
            Assert.AreEqual(BREAK + "c", ReadAll(queue));
            Assert.AreEqual(2, queue.DiscardedByBreakCount);
        }

        [Test]
        public void ResizeKeepsOnlyTheLatestSizeAndComesBeforeText()
        {
            var queue = new TelnetInputQueue();
            queue.EnqueueText("x");
            queue.EnqueueResize(80, 24);
            queue.EnqueueResize(100, 30);
            Assert.AreEqual(4, queue.Count);
            Assert.AreEqual(new string(new[] { RESIZE, (char)100, (char)30, 'x' }), ReadAll(queue));
        }

        [Test]
        public void ResizeTripleIsNeverSplitByLaterControls()
        {
            var queue = new TelnetInputQueue();
            queue.EnqueueResize(80, 24);
            Assert.AreEqual(RESIZE.ToString(), Read(queue, 1));
            // Arriving while the triple is half read: they wait until it is complete.
            queue.EnqueueBreak();
            queue.RequestRepaint();
            queue.EnqueueResize(120, 40);
            Assert.AreEqual(new string(new[] { (char)80, (char)24 }), Read(queue, 2));
            Assert.AreEqual(new string(new[] { RESIZE, (char)120, (char)40, BREAK, REPAINT }), ReadAll(queue));
        }

        [Test]
        public void ResizeOutOfCharRangeIsRejected()
        {
            var queue = new TelnetInputQueue();
            Assert.Throws<ArgumentOutOfRangeException>(() => queue.EnqueueResize(-1, 24));
            Assert.Throws<ArgumentOutOfRangeException>(() => queue.EnqueueResize(80, char.MaxValue + 1));
            Assert.AreEqual(0, queue.Count);
        }

        [Test]
        public void CapacityBelowTwoIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TelnetInputQueue(1));
            Assert.AreEqual(TelnetInputQueue.DEFAULT_CAPACITY, new TelnetInputQueue().Capacity);
        }

        [Test]
        public void FullBacklogDiscardsFurtherTextAndReportsOnce()
        {
            var queue = new TelnetInputQueue(10);
            queue.EnqueueText("0123456789");
            Assert.IsFalse(queue.IsOverflowing, "exactly full is not an overflow");
            Assert.IsFalse(queue.TakeOverflowStarted());

            queue.EnqueueText("abc");
            Assert.IsTrue(queue.IsOverflowing);
            Assert.AreEqual(10, queue.TextCount);
            Assert.AreEqual(3, queue.DroppedCount);
            Assert.AreEqual(1, queue.OverflowCount);
            Assert.IsTrue(queue.TakeOverflowStarted());
            Assert.IsFalse(queue.TakeOverflowStarted(), "reported twice");
            long dropped;
            Assert.IsFalse(queue.TakeOverflowEnded(out dropped));
        }

        [Test]
        public void OverflowEndsOnlyWhenDrainedToHalf()
        {
            var queue = new TelnetInputQueue(10);
            queue.EnqueueText("0123456789abc");
            long dropped;

            // Hysteresis: room for one more character is not enough to resume.
            Assert.AreEqual("0", Read(queue, 1));
            queue.EnqueueText("d");
            Assert.IsTrue(queue.IsOverflowing);
            Assert.AreEqual(9, queue.TextCount);
            Assert.AreEqual("123", Read(queue, 3));
            Assert.IsTrue(queue.IsOverflowing, "ended above half");
            Assert.IsFalse(queue.TakeOverflowEnded(out dropped));

            Assert.AreEqual("4", Read(queue, 1)); // 5 left, half of 10
            Assert.IsFalse(queue.IsOverflowing);
            Assert.IsTrue(queue.TakeOverflowEnded(out dropped));
            Assert.AreEqual(4, dropped);
            Assert.IsFalse(queue.TakeOverflowEnded(out dropped), "reported twice");

            queue.EnqueueText("e");
            Assert.AreEqual("56789e", ReadAll(queue));
            Assert.AreEqual(4, queue.DroppedCount);
        }

        [Test]
        public void BreakEndsAnOverflowAndStillGetsThrough()
        {
            var queue = new TelnetInputQueue(4);
            queue.EnqueueText("abcdefg");
            Assert.IsTrue(queue.IsOverflowing);
            queue.EnqueueBreak();
            Assert.IsFalse(queue.IsOverflowing);
            long dropped;
            Assert.IsTrue(queue.TakeOverflowEnded(out dropped));
            Assert.AreEqual(3, dropped);
            queue.EnqueueText("h");
            Assert.AreEqual(BREAK + "h", ReadAll(queue));
            Assert.AreEqual(4, queue.DiscardedByBreakCount);
        }

        [Test]
        public void ControlsGetThroughAFullBacklog()
        {
            var queue = new TelnetInputQueue(4);
            queue.EnqueueText("abcdefg");
            queue.EnqueueResize(50, 20);
            queue.RequestRepaint();
            Assert.AreEqual(new string(new[] { RESIZE, (char)50, (char)20, REPAINT }), ReadAll(queue, false));
            Assert.AreEqual("abcd", ReadAll(queue));
        }

        [Test]
        public void EpisodesNotYetReportedAreReportedTogether()
        {
            // The reader thread can end and start episodes before the game thread reports.
            var queue = new TelnetInputQueue(4);
            queue.EnqueueText("abcdef"); // 2 dropped
            queue.EnqueueBreak();
            queue.EnqueueText("ghijklm"); // 3 dropped
            queue.EnqueueBreak();
            long dropped;
            Assert.IsTrue(queue.TakeOverflowEnded(out dropped));
            Assert.AreEqual(5, dropped);
            Assert.AreEqual(2, queue.OverflowCount);
            Assert.IsTrue(queue.TakeOverflowStarted());
            Assert.IsFalse(queue.TakeOverflowStarted());
            Assert.IsFalse(queue.TakeOverflowEnded(out dropped));
        }

        [Test]
        public void StatisticsAndCounts()
        {
            var queue = new TelnetInputQueue(8);
            queue.EnqueueText("abcdef");
            queue.EnqueueResize(10, 10);
            queue.RequestRepaint();
            queue.EnqueueBreak();
            Assert.AreEqual(0, queue.TextCount);
            Assert.AreEqual(5, queue.Count, "a resize counts 3, break and repaint 1 each");
            Assert.AreEqual(6, queue.HighWaterMark);
            queue.EnqueueText("xy");
            Assert.AreEqual(new string(new[] { RESIZE, (char)10, (char)10, BREAK, REPAINT }) + "xy", queue.DequeueAll());
            Assert.AreEqual(0, queue.Count);
            Assert.AreEqual(6, queue.HighWaterMark);
            Assert.AreEqual(6, queue.DiscardedByBreakCount);
        }

        [Test]
        public void ConcurrentProducerAndConsumerKeepOrderAndAccountForEveryCharacter()
        {
            // Text chars are numbered in order (all distinct, none a UnicodeCommand); a resize
            // carries height = width + 1 so a split or mixed triple shows.
            const int total = 50000;
            const int firstChar = 0x0100;
            var queue = new TelnetInputQueue(512);
            int breaksSent = 0;
            Exception producerError = null;
            var producer = new Thread(() =>
            {
                try
                {
                    var random = new Random(2026);
                    int next = 0;
                    while (next < total)
                    {
                        int length = Math.Min(total - next, 1 + random.Next(64));
                        var chunk = new StringBuilder(length);
                        for (int i = 0; i < length; ++i)
                            chunk.Append((char)(firstChar + next++));
                        queue.EnqueueText(chunk.ToString());
                        int roll = random.Next(100);
                        if (roll < 5)
                        {
                            int width = 1 + random.Next(250);
                            queue.EnqueueResize(width, width + 1);
                        }
                        else if (roll == 5)
                        {
                            queue.EnqueueBreak();
                            ++breaksSent;
                        }
                        if (roll < 20)
                            Thread.Yield();
                    }
                }
                catch (Exception e) { producerError = e; }
            });
            producer.Start();

            int textRead = 0, breaksRead = 0, resizesRead = 0, lastText = -1;
            char ch;
            Func<bool> read = () =>
            {
                if (!queue.TryDequeue(true, out ch))
                    return false;
                if (ch == RESIZE)
                {
                    char width, height;
                    Assert.IsTrue(queue.TryDequeue(false, out width), "resize triple cut short");
                    Assert.IsTrue(queue.TryDequeue(true, out height), "resize triple cut short");
                    Assert.AreEqual(width + 1, (int)height, "resize triple mixed with other input");
                    ++resizesRead;
                }
                else if (ch == BREAK)
                    ++breaksRead;
                else
                {
                    Assert.Greater((int)ch - firstChar, lastText, "text out of order or repeated");
                    lastText = ch - firstChar;
                    ++textRead;
                }
                return true;
            };
            var consumerRandom = new Random(17);
            while (producer.IsAlive)
            {
                for (int i = consumerRandom.Next(200); i > 0 && read(); --i) { }
                Thread.Yield();
            }
            producer.Join();
            while (read()) { }

            Assert.IsNull(producerError);
            Assert.AreEqual(0, queue.Count);
            Assert.AreEqual(total, textRead + queue.DroppedCount + queue.DiscardedByBreakCount, "characters unaccounted for");
            Assert.LessOrEqual(breaksRead, breaksSent);
            Assert.AreEqual(breaksSent > 0, breaksRead > 0);
            Assert.Greater(resizesRead, 0);
            Assert.LessOrEqual(queue.HighWaterMark, queue.Capacity);
        }
    }
}
