using kOS.Safe.Utilities;
using UnityEngine;

namespace kOS.Utilities
{
    /// <summary>
    /// The target of a vessel as scripts read it (TARGET, HASTARGET).
    /// <para>KSP 1.12.5 takes the target of a vessel away when it becomes the active one
    /// (<c>FlightGlobals.setActiveVessel</c> → <c>Vessel.MakeActive</c> →
    /// <c>Vessel.ResumeTarget</c> → <c>SetVesselTarget(null)</c>) and gives it back from a
    /// coroutine some frames later. Until then the target to restore is in
    /// <c>Vessel.pTI</c>, which KSP sets to null once it has restored it. Reading the live
    /// target alone, a script with <c>LOCK STEERING TO TARGET:POSITION</c> died with
    /// "No TARGET is selected" on the vessel just switched to.</para>
    /// See <see cref="TargetResume"/> for how long the saved target is believed.
    /// </summary>
    public static class VesselTargeting
    {
        /// <summary>
        /// KSP's <c>GameEvents</c> read each handler's target object when it is added, so a
        /// static method cannot be added (NullReferenceException): this instance method forwards.
        /// </summary>
        private class GameEventHooks
        {
            public void VesselChange(Vessel vessel) { activatedFrame = Time.frameCount; }

            /// <summary>
            /// Docking and grappling (<c>ModuleDockingNode.DockToVessel</c>,
            /// <c>ModuleGrappleNode.Grapple</c>) call <c>Vessel.MakeActive</c> on the active
            /// vessel in the frame of <c>Part.Couple</c>, with no onVesselChange.
            /// </summary>
            public void PartCouple(GameEvents.FromToAction<Part, Part> action) { activatedFrame = Time.frameCount; }
        }

        private static GameEventHooks hooks;

        /// <summary>The frame the active vessel became the active one.</summary>
        private static int activatedFrame = TargetResume.NoFrame;

        /// <summary>
        /// Start noting when the active vessel changes. Call before the first
        /// <see cref="GetTarget"/>; safe to call again.
        /// </summary>
        public static void Hook()
        {
            if (hooks != null)
                return;
            // GameEvents outlive scenes, so this is added once per game session:
            hooks = new GameEventHooks();
            GameEvents.onVesselChange.Add(hooks.VesselChange);
            GameEvents.onPartCouple.Add(hooks.PartCouple);
            // The vessel that is active now may have become so just before this listened:
            activatedFrame = Time.frameCount;
        }

        /// <summary>
        /// The target of <paramref name="vessel"/>, or null: what KSP has, else what KSP is
        /// about to restore after a vessel switch.
        /// </summary>
        public static ITargetable GetTarget(Vessel vessel)
        {
            if (vessel != FlightGlobals.ActiveVessel)
            {
                // Left before KSP restored its target, a vessel keeps none, with the one it
                // had still saved: KSP only clears that when it restores.
                if (vessel.targetObject == null && vessel.pTI != null)
                    return vessel.pTI.FindTarget();
                return vessel.targetObject;
            }

            ITargetable target = FlightGlobals.fetch.VesselTarget;
            if (target == null && vessel.pTI != null && TargetResume.IsPending(activatedFrame, Time.frameCount))
                target = vessel.pTI.FindTarget();
            return target;
        }

        /// <summary>
        /// Give <paramref name="vessel"/> now the target <see cref="GetTarget"/> reports while
        /// KSP has none yet, for what needs KSP itself to have it (SASMODE and NAVMODE
        /// "TARGET"): a script that saw HASTARGET may set those.
        /// </summary>
        public static void RestorePending(Vessel vessel)
        {
            bool active = vessel == FlightGlobals.ActiveVessel;
            if ((active ? FlightGlobals.fetch.VesselTarget : vessel.targetObject) != null)
                return;
            ITargetable pending = GetTarget(vessel);
            // Not itself, which it can be after docking to its target:
            if (pending == null || pending.GetVessel() == vessel)
                return;
            if (active)
                VesselUtils.SetTarget(pending, vessel);
            else
                vessel.targetObject = pending;
        }
    }
}
