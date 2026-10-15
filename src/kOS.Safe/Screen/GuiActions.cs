using System;
using System.Collections.Generic;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// Tells a script GUI widget whether a change is happening because of the user's GUI
    /// activity (rather than the script setting a value). Widgets use it to pick how their
    /// callbacks are scheduled.
    /// <para>
    /// WIDGET-02: the widget used to set a flag only while it scheduled the action, so an
    /// action delayed by communications ran later with the flag already off. <see cref="Mark"/>
    /// carries the origin with the action instead.
    /// </para>
    /// </summary>
    public sealed class GuiCause
    {
        private int depth;

        public bool IsGuiCaused { get { return depth > 0; } }

        /// <summary>Run the action now as GUI-caused; nesting and exceptions restore the previous state.</summary>
        public void Run(Action action)
        {
            if (action == null)
                throw new ArgumentNullException("action");
            ++depth;
            try
            {
                action();
            }
            finally
            {
                --depth;
            }
        }

        /// <summary>The action, to be run as GUI-caused whenever it is finally delivered.</summary>
        public Action Mark(Action action)
        {
            if (action == null)
                throw new ArgumentNullException("action");
            return () => Run(action);
        }
    }

    /// <summary>
    /// The actions a script GUI window holds back for its communications delay, in delivery
    /// order (time, then arrival). Each is delivered at most once: it leaves the queue before
    /// it runs, so a failing action is not retried every update and an action that queues
    /// another cannot make the wrong one be removed.
    /// </summary>
    public class DelayedGuiActions
    {
        public sealed class Entry
        {
            internal Entry(object owner, string reason, Action action, double time)
            {
                Owner = owner;
                Reason = reason;
                Action = action;
                Time = time;
            }

            public object Owner { get; private set; }
            public string Reason { get; private set; }
            public Action Action { get; private set; }
            public double Time { get; private set; }
        }

        private readonly List<Entry> entries = new List<Entry>();

        public int Count { get { return entries.Count; } }

        /// <summary>The next action to be delivered (for the delay overlay), or null.</summary>
        public Entry Next { get { return entries.Count > 0 ? entries[0] : null; } }

        public void Enqueue(object owner, string reason, Action action, double time)
        {
            if (action == null)
                throw new ArgumentNullException("action");
            var entry = new Entry(owner, reason ?? string.Empty, action, time);
            // Linear insert from the end because almost always it is added at the end.
            int index = entries.Count;
            while (index > 0 && entries[index - 1].Time > time)
                --index;
            entries.Insert(index, entry);
        }

        /// <summary>Drop the undelivered actions of a widget that is going away.</summary>
        public void Remove(object owner)
        {
            entries.RemoveAll(e => ReferenceEquals(e.Owner, owner));
        }

        /// <summary>
        /// Deliver every action due at <paramref name="now"/>, including ones that due actions
        /// queue for now or earlier. An exception from an action propagates after that action
        /// has left the queue; the rest are delivered by the next call.
        /// </summary>
        /// <returns>How many actions were delivered.</returns>
        public int DeliverDue(double now)
        {
            int delivered = 0;
            while (entries.Count > 0 && entries[0].Time <= now)
            {
                Entry next = entries[0];
                entries.RemoveAt(0);
                ++delivered;
                next.Action();
            }
            return delivered;
        }
    }
}
