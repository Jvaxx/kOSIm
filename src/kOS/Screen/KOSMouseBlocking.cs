using System;
using kOS.Safe.UserIO;
using kOS.Safe.Utilities;
using UnityEngine;

namespace kOS.Screen
{
    /// <summary>
    /// The game side of INPUT-01's mouse half: one <see cref="MouseBlocking"/> for every kOS
    /// window, applying its decisions to KSP's <c>InputLockManager</c>.
    /// <para>
    /// The lock has its own name, <see cref="LockName"/>, separate from the keyboard lock
    /// <see cref="TermWindow.CONTROL_LOCKOUT"/>, and holds only the current scene's mouse
    /// controls (camera, map clicks, targeting, editor part picking): never keys sent to the
    /// vessel, flight controls or staging. Windows report where they drew with
    /// <see cref="Show"/>; <see cref="KOSMouseBlockingDriver"/> updates the lock every frame.
    /// </para>
    /// <para>
    /// In flight KSP opens part menus and highlights parts without looking at any lock, so each
    /// visible window also gets a Unity UI raycast blocker (INPUT-02, <see cref="KOSRaycastBlockers"/>).
    /// </para>
    /// </summary>
    public static class KOSMouseBlocking
    {
        /// <summary>The <c>InputLockManager</c> lock name used while the mouse is over a kOS window.</summary>
        public const string LockName = "kOSMouseOver";

        /// <summary>
        /// The controls the hover lock holds in a scene: the scene's mouse handling only.
        /// <c>CAMERACONTROLS</c> also covers the camera keys, but only while the mouse is
        /// over a kOS window.
        /// </summary>
        public static ControlTypes SceneMask(GameScenes scene)
        {
            switch (scene)
            {
                case GameScenes.EDITOR:
                    return ControlTypes.EDITOR_SOFT_LOCK | ControlTypes.CAMERACONTROLS;
                case GameScenes.FLIGHT:
                    return ControlTypes.CAMERACONTROLS | ControlTypes.MAP_UI | ControlTypes.TARGETING;
                case GameScenes.SPACECENTER:
                    return ControlTypes.CAMERACONTROLS | ControlTypes.KSC_UI;
                case GameScenes.TRACKSTATION:
                    return ControlTypes.CAMERACONTROLS | ControlTypes.TRACKINGSTATION_UI | ControlTypes.MAP_UI;
                default:
                    return ControlTypes.None;
            }
        }

        private class KspMouseLockSink : IMouseLockSink
        {
            public void Lock()
            {
                ControlTypes mask = SceneMask(HighLogic.LoadedScene);
                if (mask != ControlTypes.None)
                    InputLockManager.SetControlLock(mask, LockName);
            }

            public void Unlock()
            {
                InputLockManager.RemoveControlLock(LockName);
            }
        }

        /// <summary>
        /// KSP's <c>GameEvents</c> read each handler's target object when it is added, so a
        /// static method cannot be added (NullReferenceException): this instance method forwards.
        /// </summary>
        private class GameEventHooks
        {
            public void SceneLoadRequested(GameScenes scene) { OnSceneLoadRequested(scene); }
        }

        private static MouseBlocking instance;
        private static readonly KOSRaycastBlockers blockers = new KOSRaycastBlockers();
        private static GameEventHooks hooks;
        private static bool errorLogged;

        public static MouseBlocking Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new MouseBlocking(new KspMouseLockSink());
                    // GameEvents outlive scenes, so this is added once per game session:
                    hooks = new GameEventHooks();
                    GameEvents.onGameSceneLoadRequested.Add(hooks.SceneLoadRequested);
                }
                return instance;
            }
        }

        /// <summary>
        /// The window drew at <paramref name="rect"/> (GUI coordinates) this frame. Registers
        /// <paramref name="area"/> on first use. Call from OnGUI after the window is drawn, and
        /// only when it is: a window that stops reporting stops blocking a frame later.
        /// </summary>
        public static void Show(ref MouseBlocking.Area area, string name, Rect rect)
        {
            try
            {
                if (area == null)
                    area = Instance.Register(name);
                Instance.Show(area, new ScreenRect(rect.x, rect.y, rect.width, rect.height), Time.frameCount);
            }
            catch (Exception ex)
            {
                LogFirst(ex);
                return;
            }
            // Lock at once rather than at the next frame's update, which may come after the
            // scene has read this frame's mouse.
            Update();
        }

        /// <summary>The window is being destroyed. Safe with null or twice.</summary>
        public static void Unregister(ref MouseBlocking.Area area)
        {
            if (area == null)
                return;
            MouseBlocking.Area old = area;
            area = null;
            if (instance != null)
                instance.Unregister(old);
            try
            {
                blockers.Remove(old);
            }
            catch (Exception ex)
            {
                LogFirst(ex);
            }
        }

        /// <summary>Decide and apply the lock for the current mouse. Cheap; harmless to call more than once a frame.</summary>
        public static void Update()
        {
            try
            {
                // Input.mousePosition has its origin at the bottom left; GUI rectangles at the top left.
                Vector3 mouse = Input.mousePosition;
                bool held = Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2);
                Instance.Update(Time.frameCount, mouse.x, UnityEngine.Screen.height - mouse.y, held);
            }
            catch (Exception ex)
            {
                // Refused lock changes are retried every frame; log only the first failure.
                LogFirst(ex);
            }
            UpdateBlockers();
        }

        private static void UpdateBlockers()
        {
            try
            {
                Instance.UpdateBlockers(Time.frameCount, blockers);
            }
            catch (Exception ex)
            {
                LogFirst(ex);
            }
        }

        private static void OnSceneLoadRequested(GameScenes scene)
        {
            try
            {
                Instance.ReleaseAll();
            }
            catch (Exception ex)
            {
                LogFirst(ex);
            }
            UpdateBlockers(); // nothing is shown any more: every blocker hidden
        }

        private static void LogFirst(Exception ex)
        {
            if (errorLogged)
                return;
            errorLogged = true;
            SafeHouse.Logger.LogException(ex);
        }
    }

    /// <summary>Updates the mouse lock every frame in every scene, whether or not a kOS window draws.</summary>
    [KSPAddon(KSPAddon.Startup.EveryScene, false)]
    public class KOSMouseBlockingDriver : MonoBehaviour
    {
        public void Update()
        {
            KOSMouseBlocking.Update();
        }
    }
}
