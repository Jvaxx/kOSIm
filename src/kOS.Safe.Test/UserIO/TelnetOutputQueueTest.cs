using System;
using System.Text;
using System.Threading;
using kOS.Safe.UserIO;
using NUnit.Framework;

namespace kOS.Safe.Test.UserIO
{
    /// <summary>
    /// <see cref="TelnetOutputQueue"/>: output is kept until the backlog would exceed the
    /// capacity; then the unsent backlog is dropped (the caller repaints), a single write is
    /// never split, and a dropped DIE still ends the session.
    /// </summary>
    [TestFixture]
    public class TelnetOutputQueueTest
    {
        private const char DIE = (char)UnicodeCommand.DIE;

        [Test]
        public void OutputWithinCapacityIsKeptInOrder()
        {
            var queue = new TelnetOutputQueue(10);
            Assert.IsFalse(queue.Write("abcd"));
            Assert.IsFalse(queue.Write('e'));
            Assert.IsFalse(queue.Write("fghij")); // exactly full
            Assert.IsFalse(queue.Write(""));
            Assert.AreEqual(10, queue.Count);
            bool die;
            Assert.AreEqual("abcdefghij", queue.TakeAll(out die));
            Assert.IsFalse(die);
            Assert.AreEqual(0, queue.Count);
            Assert.AreEqual("", queue.TakeAll(out die));
            Assert.AreEqual(0, queue.DroppedCount);
            Assert.AreEqual(0, queue.OverflowCount);
        }

        [Test]
        public void WriteBeyondCapacityDropsTheUnsentBacklog()
        {
            var queue = new TelnetOutputQueue(10);
            queue.Write("abcdefgh");
            Assert.IsTrue(queue.Write("XYZ"), "caller must be told to repaint");
            bool die;
            Assert.AreEqual("XYZ", queue.TakeAll(out die));
            Assert.AreEqual(8, queue.DroppedCount);
            Assert.AreEqual(1, queue.OverflowCount);
            Assert.AreEqual(8, queue.HighWaterMark);
        }

        [Test]
        public void SentOutputMakesRoomAgain()
        {
            var queue = new TelnetOutputQueue(10);
            bool die;
            queue.Write("abcdefgh");
            queue.TakeAll(out die);
            Assert.IsFalse(queue.Write("ijklmnop"));
            Assert.AreEqual(0, queue.DroppedCount);
        }

        [Test]
        public void OversizedSingleWriteIsAcceptedWhole()
        {
            var queue = new TelnetOutputQueue(4);
            Assert.IsFalse(queue.Write("a whole repaint"), "nothing was dropped");
            Assert.AreEqual(15, queue.HighWaterMark);
            Assert.IsTrue(queue.Write("x"));
            bool die;
            Assert.AreEqual("x", queue.TakeAll(out die));
            Assert.AreEqual(15, queue.DroppedCount);
        }

        [Test]
        public void DroppedDieStillEndsTheSession()
        {
            var queue = new TelnetOutputQueue(4);
            queue.Write("a" + DIE);
            queue.Write("bcde");
            bool die;
            Assert.AreEqual("bcde", queue.TakeAll(out die));
            Assert.IsTrue(die);
            queue.Write("f");
            Assert.AreEqual("f", queue.TakeAll(out die));
            Assert.IsFalse(die, "reported only once");
        }

        [Test]
        public void DieInSentOutputIsReported()
        {
            var queue = new TelnetOutputQueue();
            queue.Write("bye" + DIE);
            bool die;
            Assert.AreEqual("bye" + DIE, queue.TakeAll(out die));
            Assert.IsTrue(die);
        }

        [Test]
        public void InvalidArgumentsAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TelnetOutputQueue(0));
            Assert.Throws<ArgumentNullException>(() => new TelnetOutputQueue().Write(null));
            Assert.AreEqual(TelnetOutputQueue.DEFAULT_CAPACITY, new TelnetOutputQueue().Capacity);
        }

        [Test]
        public void ConcurrentWritersLoseNothingUnreported()
        {
            // Writes are whole words of one repeated letter, so a split or interleaved write shows.
            var queue = new TelnetOutputQueue(300);
            const int writers = 3, writesEach = 5000;
            long written = 0;
            var threads = new Thread[writers];
            for (int w = 0; w < writers; ++w)
            {
                char letter = (char)('a' + w);
                threads[w] = new Thread(() =>
                {
                    var random = new Random(letter);
                    for (int i = 0; i < writesEach; ++i)
                    {
                        string word = new string(letter, 1 + random.Next(20)) + " ";
                        queue.Write(word);
                        Interlocked.Add(ref written, word.Length);
                    }
                });
                threads[w].Start();
            }
            long taken = 0;
            bool die;
            var all = new StringBuilder();
            Func<bool> anyAlive = () => { foreach (Thread t in threads) if (t.IsAlive) return true; return false; };
            while (anyAlive())
            {
                string chunk = queue.TakeAll(out die);
                taken += chunk.Length;
                all.Append(chunk);
                Thread.Yield();
            }
            foreach (Thread t in threads)
                t.Join();
            string rest = queue.TakeAll(out die);
            taken += rest.Length;
            all.Append(rest);

            Assert.AreEqual(Interlocked.Read(ref written), taken + queue.DroppedCount);
            foreach (string word in all.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                Assert.AreEqual(new string(word[0], word.Length), word, "a write was split or interleaved");
        }
    }
}
