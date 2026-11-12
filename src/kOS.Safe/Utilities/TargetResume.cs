namespace kOS.Safe.Utilities
{
    /// <summary>
    /// How long kOS keeps reporting the target KSP is about to give back to a vessel.
    /// <para>When a vessel becomes the active one, KSP (<c>Vessel.ResumeTarget</c>, 1.12.5)
    /// clears its target and restores it from a coroutine: one frame later when the vessel
    /// has been loaded for a while, and after waiting up to 120 more frames when it has
    /// not. A script reading TARGET in between saw no target and died on "No TARGET is
    /// selected". During that time the target KSP will restore is still known.</para>
    /// <para>KSP does not always restore it (the target went away, or has no orbit line to
    /// restore to), and then keeps the saved target for good. So the saved target is
    /// believed for this many frames only: after them kOS reports what KSP has.</para>
    /// Unity-free (the game side is <c>kOS.Utilities.VesselTargeting</c>).
    /// </summary>
    public static class TargetResume
    {
        /// <summary>The frame of a vessel that was not seen becoming active.</summary>
        public const int NoFrame = -1;

        /// <summary>
        /// KSP restores at the latest 121 frames after the switch (one frame, then 120
        /// waits); a few more cost nothing.
        /// </summary>
        public const int MaxFrames = 125;

        /// <summary>Whether KSP may still be about to restore the target of the active vessel.</summary>
        /// <param name="activatedFrame">frame the vessel became active, or <see cref="NoFrame"/></param>
        /// <param name="currentFrame">frame now</param>
        public static bool IsPending(int activatedFrame, int currentFrame)
        {
            if (activatedFrame < 0)
                return false;
            long elapsed = (long)currentFrame - activatedFrame;
            return elapsed >= 0 && elapsed <= MaxFrames;
        }
    }
}
