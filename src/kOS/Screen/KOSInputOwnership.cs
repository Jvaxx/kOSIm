using System;
using kOS.Safe.UserIO;
using kOS.Safe.Utilities;
using KSP.UI.Dialogs;

namespace kOS.Screen
{
    /// <summary>
    /// The game side of INPUT-01: one <see cref="InputOwnership"/> shared by every kOS window
    /// (all vessels and CPUs share one keyboard), applying its decisions to KSP's
    /// <c>InputLockManager</c>.
    /// <para>
    /// Every window that takes a lock uses <c>ControlTypes.All</c> under the name
    /// <see cref="TermWindow.CONTROL_LOCKOUT"/>, as the focused terminal always did: only one
    /// window owns the keyboard, and kOS's targeting workaround (<c>VesselUtils</c>) and other
    /// mods look that name up.
    /// </para>
    /// </summary>
    public static class KOSInputOwnership
    {
        private class KspLockSink : IInputLockSink
        {
            public void Lock(InputOwnership.Window window)
            {
                InputLockManager.SetControlLock(ControlTypes.All, TermWindow.CONTROL_LOCKOUT);
            }

            public void Unlock(InputOwnership.Window window)
            {
                InputLockManager.RemoveControlLock(TermWindow.CONTROL_LOCKOUT);
            }
        }

        /// <summary>
        /// KSP's <c>GameEvents</c> read each handler's target object when it is added, so a
        /// static method cannot be added (NullReferenceException): these instance methods forward.
        /// </summary>
        private class GameEventHooks
        {
            public void HideUI() { OnHideUI(); }
            public void ShowUI() { OnShowUI(); }
            public void SceneLoadRequested(GameScenes scene) { OnSceneLoadRequested(scene); }
        }

        private static InputOwnership instance;
        private static GameEventHooks hooks;
        private static bool uiHidden;
        private static int scriptedDepth;
        private static bool updateErrorLogged;

        public static InputOwnership Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new InputOwnership(new KspLockSink());
                    // GameEvents outlive scenes, so these are added once per game session:
                    hooks = new GameEventHooks();
                    GameEvents.onHideUI.Add(hooks.HideUI);
                    GameEvents.onShowUI.Add(hooks.ShowUI);
                    GameEvents.onGameSceneLoadRequested.Add(hooks.SceneLoadRequested);
                }
                return instance;
            }
        }

        /// <summary>
        /// True while kOS itself invokes a part event or action for a script
        /// (<c>DOEVENT</c>, <c>DOACTION</c>): a terminal opened that way does not take focus.
        /// </summary>
        public static bool IsScriptedInvocation { get { return scriptedDepth > 0; } }

        public static void RunScripted(Action action)
        {
            ++scriptedDepth;
            try
            {
                action();
            }
            finally
            {
                --scriptedDepth;
            }
        }

        /// <summary>
        /// Suspend input while the UI is hidden (when kOS obeys it) or the flight-results
        /// dialog shows, and retry refused lock changes. Cheap; call it every frame and before
        /// reading keys.
        /// </summary>
        public static void Update()
        {
            try
            {
                kOS.Safe.Encapsulation.IConfig cfg = SafeHouse.Config;
                bool hidden = uiHidden && (cfg == null || cfg.ObeyHideUI);
                Instance.SetSuspended(hidden || FlightResultsDialog.isDisplaying);
                Instance.Apply();
            }
            catch (Exception ex)
            {
                // Refused lock changes are retried every frame; log only the first failure.
                if (!updateErrorLogged)
                {
                    updateErrorLogged = true;
                    SafeHouse.Logger.LogException(ex);
                }
            }
        }

        private static void OnHideUI()
        {
            uiHidden = true;
            Update();
        }

        private static void OnShowUI()
        {
            uiHidden = false;
            Update();
        }

        private static void OnSceneLoadRequested(GameScenes scene)
        {
            Instance.ReleaseAll();
        }
    }
}
