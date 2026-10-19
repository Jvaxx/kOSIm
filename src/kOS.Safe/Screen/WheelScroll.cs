namespace kOS.Safe.Screen
{
    /// <summary>
    /// The GUI terminal's mouse wheel (Phase 3, O1): <see cref="LINES_PER_NOTCH"/> lines of
    /// the shared view per wheel notch, as PageUp/PageDown move it. Unity reports about
    /// <see cref="DELTA_PER_NOTCH"/> of scroll delta per notch; finer deltas (touchpads) add
    /// up, and a change of direction starts again.
    /// </summary>
    public sealed class WheelScroll
    {
        public const int LINES_PER_NOTCH = 3;
        public const float DELTA_PER_NOTCH = 3f;

        private float carry;

        /// <param name="delta">the event's vertical delta: positive scrolls down (towards newer lines)</param>
        /// <returns>the lines to move the view, positive down</returns>
        public int Lines(float delta)
        {
            if (float.IsNaN(delta) || float.IsInfinity(delta)) return 0;
            if ((carry > 0 && delta < 0) || (carry < 0 && delta > 0))
                carry = 0;
            carry += delta * LINES_PER_NOTCH / DELTA_PER_NOTCH;
            int lines = (int)carry; // toward zero
            carry -= lines;
            return lines;
        }
    }
}
