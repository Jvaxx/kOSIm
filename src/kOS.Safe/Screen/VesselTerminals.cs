using System;
using System.Collections.Generic;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// A right click on the toolbar button in flight (user decision 2026-10-26): the active
    /// vessel's terminals all open when any of them is closed, else they all close. The rule
    /// reads the windows, not a remembered state, so it stays right after a terminal is
    /// opened or closed some other way.
    /// <para>Windows still where a CPU without saved geometry opens would all open on top of
    /// each other: each one opened is stepped one header height further down and right,
    /// after the ones already open there. A window with a place of its own keeps it.</para>
    /// Unity-free (the game side is <c>kOS.Screen.KOSToolbarWindow</c>).
    /// </summary>
    public static class VesselTerminals
    {
        /// <summary>Whether the click opens the closed terminals (any is closed) rather than closing them all.</summary>
        /// <param name="open">per terminal of the vessel, whether its window is open</param>
        public static bool OpensAll(IList<bool> open)
        {
            if (open == null) throw new ArgumentNullException("open");
            foreach (bool isOpen in open)
                if (!isOpen) return true;
            return false;
        }

        /// <summary>
        /// Per terminal, how many steps from the default position its window goes when it
        /// opens: -1 to leave it where it is (it is open already, or has a place of its own).
        /// </summary>
        /// <param name="open">per terminal, whether its window is open</param>
        /// <param name="atDefault">per terminal, whether its window is at the default position</param>
        public static int[] CascadeSteps(IList<bool> open, IList<bool> atDefault)
        {
            if (open == null) throw new ArgumentNullException("open");
            if (atDefault == null) throw new ArgumentNullException("atDefault");
            if (open.Count != atDefault.Count) throw new ArgumentException("one value per terminal in each list");

            int next = 0;
            for (int i = 0; i < open.Count; ++i)
                if (open[i] && atDefault[i]) ++next; // already open there: the new ones go after it

            var steps = new int[open.Count];
            for (int i = 0; i < open.Count; ++i)
                steps[i] = !open[i] && atDefault[i] ? next++ : -1;
            return steps;
        }
    }
}
