using kOS.Safe.Utilities;
using NUnit.Framework;

namespace kOS.Safe.Test.Utilities
{
    /// <summary>
    /// After a vessel switch KSP takes the new active vessel's target away and gives it back
    /// some frames later; a script locked on TARGET died in between. kOS reports the target
    /// KSP is about to restore, for as long as KSP can still restore it.
    /// </summary>
    [TestFixture]
    public class TargetResumeTest
    {
        [Test]
        public void TheSavedTargetIsBelievedFromTheSwitchUntilKspHasHadItsFrames()
        {
            Assert.IsTrue(TargetResume.IsPending(1000, 1000), "the frame of the switch itself");
            Assert.IsTrue(TargetResume.IsPending(1000, 1001), "the usual case: restored a frame later");
            Assert.IsTrue(TargetResume.IsPending(1000, 1121), "KSP's last frame: one, then 120 waits");
            Assert.IsTrue(TargetResume.IsPending(1000, 1000 + TargetResume.MaxFrames));
            Assert.IsFalse(TargetResume.IsPending(1000, 1001 + TargetResume.MaxFrames),
                "KSP did not restore it: report what KSP has");
        }

        [Test]
        public void KspGetsAllItsFrames()
        {
            Assert.GreaterOrEqual(TargetResume.MaxFrames, 121);
        }

        [Test]
        public void AVesselNotSeenBecomingActiveHasNothingPending()
        {
            Assert.IsFalse(TargetResume.IsPending(TargetResume.NoFrame, 0));
            Assert.IsFalse(TargetResume.IsPending(TargetResume.NoFrame, 50));
            Assert.IsFalse(TargetResume.IsPending(int.MinValue, int.MaxValue), "no overflow into a small difference");
        }

        [Test]
        public void AFrameBeforeTheSwitchIsNotPending()
        {
            Assert.IsFalse(TargetResume.IsPending(1000, 999));
            Assert.IsTrue(TargetResume.IsPending(0, 0), "frame zero is a frame");
        }
    }
}
