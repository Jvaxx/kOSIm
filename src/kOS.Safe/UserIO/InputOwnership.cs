using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace kOS.Safe.UserIO
{
    /// <summary>
    /// Receives the control-lock changes decided by <see cref="InputOwnership"/>. The game
    /// implements it with KSP's <c>InputLockManager</c>, choosing each window's lock name
    /// and control types; tests record the calls.
    /// </summary>
    public interface IInputLockSink
    {
        void Lock(InputOwnership.Window window);
        void Unlock(InputOwnership.Window window);
    }

    /// <summary>
    /// Decides which kOS window owns the keyboard and which windows hold a control lock
    /// (INPUT-01, policy agreed with the user 2026-10-15; see docs/KOSIm-roadmap.md):
    /// <list type="bullet">
    /// <item>At most one window has the keyboard. Only a window that wants focus, is open and
    /// powered, while input is not suspended, gets it.</item>
    /// <item>Only a user's open or click gives focus. An open by an action group or a script
    /// shows the window without focus.</item>
    /// <item>Suspension (UI hidden with "obey hide UI", flight-results dialog) releases the
    /// keyboard and locks; the window that wanted focus gets it back afterwards.</item>
    /// <item>Power loss drops focus for good, and an unpowered window cannot take focus.</item>
    /// <item>Close, removal and <see cref="ReleaseAll"/> always release.</item>
    /// </list>
    /// Every change is applied to the sink at once, unlocks before locks, so two windows
    /// sharing one lock name (the terminals' <c>kOSTerminal</c>) hand it over cleanly. A
    /// refused change is reported and retried by the next call.
    /// This class holds no Unity or KSP state and is not thread-safe: use it from the game
    /// thread only.
    /// </summary>
    public class InputOwnership
    {
        /// <summary>One window taking part in keyboard ownership.</summary>
        public sealed class Window
        {
            internal Window(string name, bool takesLock)
            {
                Name = name;
                TakesLock = takesLock;
                IsPowered = true;
            }

            /// <summary>For logs and the game's lock mapping.</summary>
            public string Name { get; private set; }

            /// <summary>False for windows that rely on Unity's own field focus (script GUI windows, #2568).</summary>
            public bool TakesLock { get; private set; }

            public bool IsOpen { get; internal set; }
            public bool IsPowered { get; internal set; }

            /// <summary>The user gave it focus and has not taken it away; it may be suspended.</summary>
            public bool WantsFocus { get; internal set; }

            /// <summary>The sink holds this window's lock.</summary>
            public bool HoldsLock { get; internal set; }

            internal bool IsRegistered { get; set; }
        }

        private readonly IInputLockSink sink;
        private readonly List<Window> windows = new List<Window>();
        private bool suspended;

        public InputOwnership(IInputLockSink sink)
        {
            if (sink == null)
                throw new ArgumentNullException("sink");
            this.sink = sink;
        }

        public bool IsSuspended { get { return suspended; } }

        /// <summary>The window that receives keys, or null.</summary>
        public Window KeyboardOwner
        {
            get
            {
                if (suspended)
                    return null;
                foreach (Window w in windows)
                    if (w.WantsFocus && w.IsOpen && w.IsPowered)
                        return w;
                return null;
            }
        }

        public bool HasKeyboard(Window window)
        {
            return window != null && KeyboardOwner == window;
        }

        /// <summary>Add a closed, powered window without focus.</summary>
        public Window Register(string name, bool takesLock)
        {
            var window = new Window(name ?? string.Empty, takesLock) { IsRegistered = true };
            windows.Add(window);
            return window;
        }

        /// <summary>
        /// Remove a window that is being destroyed, releasing its lock. Safe to call twice.
        /// The window is removed even if the sink throws.
        /// </summary>
        public void Unregister(Window window)
        {
            if (window == null || !window.IsRegistered)
                return;
            window.IsOpen = false;
            window.WantsFocus = false;
            try
            {
                Apply();
            }
            finally
            {
                window.IsRegistered = false;
                windows.Remove(window);
            }
        }

        /// <param name="takeFocus">True only when the user opened it (part menu, toolbar);
        /// false for action groups and scripts.</param>
        public void Open(Window window, bool takeFocus)
        {
            CheckRegistered(window);
            window.IsOpen = true;
            if (takeFocus)
                GiveFocusTo(window);
            Apply();
        }

        public void Close(Window window)
        {
            CheckRegistered(window);
            window.IsOpen = false;
            window.WantsFocus = false;
            Apply();
        }

        /// <summary>
        /// A user's click: on <paramref name="window"/>, or outside every kOS window when it is
        /// null. A click on a closed or unpowered window counts as outside.
        /// </summary>
        public void Click(Window window)
        {
            if (window != null)
                CheckRegistered(window);
            if (window != null && window.IsOpen && window.IsPowered)
                GiveFocusTo(window);
            else
                GiveFocusTo(null);
            Apply();
        }

        /// <summary>Power loss drops focus for good; power return gives nothing back.</summary>
        public void SetPowered(Window window, bool powered)
        {
            CheckRegistered(window);
            window.IsPowered = powered;
            if (!powered)
                window.WantsFocus = false;
            Apply();
        }

        /// <summary>
        /// True while the UI is hidden (and kOS obeys it) or the flight-results dialog shows.
        /// Cheap to call every frame with the current value.
        /// </summary>
        public void SetSuspended(bool value)
        {
            if (suspended == value)
                return;
            suspended = value;
            Apply();
        }

        /// <summary>Scene change or shutdown: nobody keeps focus or a lock. Safe to call twice.</summary>
        public void ReleaseAll()
        {
            foreach (Window w in windows)
                w.WantsFocus = false;
            Apply();
        }

        /// <summary>Retry lock changes the sink refused earlier. A no-op when nothing is pending.</summary>
        public void Apply()
        {
            Window owner = KeyboardOwner;
            Exception firstError = null;
            // Unlock first: terminals share one lock name, so locking the new owner before
            // unlocking the old one would let the unlock remove the new lock.
            foreach (Window w in windows.ToArray())
            {
                if (!w.HoldsLock || ShouldLock(w, owner))
                    continue;
                try
                {
                    sink.Unlock(w);
                    w.HoldsLock = false;
                }
                catch (Exception ex)
                {
                    if (firstError == null)
                        firstError = ex;
                }
            }
            // A failed unlock is retried later; locking now and unlocking after would remove a
            // shared lock from its new owner, so locks wait until the unlocks succeed.
            foreach (Window w in firstError != null ? new Window[0] : windows.ToArray())
            {
                if (w.HoldsLock || !ShouldLock(w, owner))
                    continue;
                try
                {
                    sink.Lock(w);
                    w.HoldsLock = true;
                }
                catch (Exception ex)
                {
                    if (firstError == null)
                        firstError = ex;
                }
            }
            if (firstError != null)
                ExceptionDispatchInfo.Capture(firstError).Throw();
        }

        private static bool ShouldLock(Window window, Window owner)
        {
            return window == owner && window.TakesLock;
        }

        private void GiveFocusTo(Window window)
        {
            foreach (Window w in windows)
                w.WantsFocus = w == window;
        }

        private void CheckRegistered(Window window)
        {
            if (window == null)
                throw new ArgumentNullException("window");
            if (!window.IsRegistered)
                throw new InvalidOperationException("Input window '" + window.Name + "' is not registered.");
        }
    }
}
