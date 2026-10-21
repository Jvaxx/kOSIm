using System;
using System.Collections.Generic;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// The lines of a <see cref="ScreenBuffer"/>, scrollback and screen, in order (review P-09):
    /// a list whose first lines can be dropped without moving the others. With a full
    /// scrollback, every line scrolled drops the oldest; <c>List.RemoveRange(0, 1)</c> moved
    /// all the lines kept (about 2,000) each time. Here the dropped slots are left empty at the
    /// array's start and reclaimed by one copy when the array is full and they are at least as
    /// many as the lines kept: a copy of N lines comes after at least N lines dropped. The
    /// array never shrinks; it stays under four times the most lines held (about twice while
    /// a full scrollback scrolls).
    /// </summary>
    internal sealed class ScreenLines
    {
        private const int INITIAL_CAPACITY = 64;

        private IScreenBufferLine[] items = new IScreenBufferLine[INITIAL_CAPACITY];
        private int start; // the slot of line 0
        private int count;

        public int Count { get { return count; } }

        /// <summary>The array's size, for the tests.</summary>
        internal int Capacity { get { return items.Length; } }

        /// <summary>How many lines were moved within the array or into a new one so far, for the tests.</summary>
        internal long MovedLines { get; private set; }

        public IScreenBufferLine this[int index]
        {
            get
            {
                CheckIndex(index);
                return items[start + index];
            }
            set
            {
                CheckIndex(index);
                items[start + index] = value;
            }
        }

        public void Add(IScreenBufferLine line)
        {
            if (start + count == items.Length)
                MakeRoom();
            items[start + count] = line;
            ++count;
        }

        /// <summary>Remove <paramref name="removed"/> lines from <paramref name="index"/> on, as <c>List.RemoveRange</c>.</summary>
        public void RemoveRange(int index, int removed)
        {
            CheckRange(index, removed);
            if (removed == 0) return;
            if (index == 0)
            {
                Array.Clear(items, start, removed); // no references kept to dropped lines
                start += removed;
            }
            else
            {
                int after = count - index - removed;
                Array.Copy(items, start + index + removed, items, start + index, after);
                MovedLines += after;
                Array.Clear(items, start + count - removed, removed);
            }
            count -= removed;
            if (count == 0)
                start = 0;
        }

        /// <summary>A new list of <paramref name="length"/> lines from <paramref name="index"/> on, as <c>List.GetRange</c>.</summary>
        public List<IScreenBufferLine> GetRange(int index, int length)
        {
            CheckRange(index, length);
            var range = new List<IScreenBufferLine>(length);
            for (int i = 0; i < length; ++i)
                range.Add(items[start + index + i]);
            return range;
        }

        public void Clear()
        {
            Array.Clear(items, start, count);
            start = 0;
            count = 0;
        }

        // The array is full up to its end: move the lines to its start when the empty slots
        // there are at least as many (so this copy is paid for by as many drops), else grow.
        private void MakeRoom()
        {
            if (start > 0 && start >= count)
            {
                Array.Copy(items, start, items, 0, count);
                Array.Clear(items, count, start);
                MovedLines += count;
            }
            else
            {
                var larger = new IScreenBufferLine[items.Length * 2];
                Array.Copy(items, start, larger, 0, count);
                items = larger;
                MovedLines += count;
            }
            start = 0;
        }

        private void CheckIndex(int index)
        {
            if (index < 0 || index >= count)
                throw new ArgumentOutOfRangeException("index", index, "Index was out of range. Must be non-negative and less than the size of the collection.");
        }

        private void CheckRange(int index, int length)
        {
            if (index < 0) throw new ArgumentOutOfRangeException("index", index, "Non-negative number required.");
            if (length < 0) throw new ArgumentOutOfRangeException("count", length, "Non-negative number required.");
            if (count - index < length) throw new ArgumentException("Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
        }
    }
}
