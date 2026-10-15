using System;
using System.Collections.Generic;

namespace kOS.Safe.UserIO
{
    /// <summary>
    /// Receives the mouse-lock changes decided by <see cref="MouseBlocking"/>. The game
    /// implements it with KSP's <c>InputLockManager</c> (one lock name, scene mouse controls
    /// only); tests record the calls.
    /// </summary>
    public interface IMouseLockSink
    {
        void Lock();
        void Unlock();
    }

    /// <summary>A rectangle in GUI screen coordinates (origin top left), like Unity's <c>Rect</c>.</summary>
    public struct ScreenRect
    {
        public readonly float X, Y, Width, Height;

        public ScreenRect(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        /// <summary>As Unity's <c>Rect.Contains</c>: left and top edges inside, right and bottom outside.</summary>
        public bool Contains(float x, float y)
        {
            return x >= X && x < X + Width && y >= Y && y < Y + Height;
        }

        public override string ToString()
        {
            return string.Format("({0}, {1}, {2}x{3})", X, Y, Width, Height);
        }
    }

    /// <summary>
    /// Keeps mouse clicks, the wheel and drags over kOS windows from reaching the scene under
    /// them (INPUT-01 mouse half, #2659/#2759; policy agreed with the user 2026-10-15, see
    /// docs/KOSIm-roadmap.md):
    /// <list type="bullet">
    /// <item>Every kOS window reports its rectangle each time it draws. It counts as visible
    /// only if it drew in this frame or the previous one, so a window that stops drawing for
    /// any reason (closed, hidden with the UI, destroyed, a drawing fault) stops blocking by
    /// itself.</item>
    /// <item>The lock is wanted while the mouse is over a visible window. While a button is
    /// held, the press decides: one that started over a kOS window keeps the lock wherever the
    /// mouse goes (dragging or resizing a window); one that started elsewhere never takes it
    /// (a camera drag crossing a window is not cut off).</item>
    /// <item>The lock is released only <see cref="ReleaseDelayFrames"/> frames after it was
    /// last wanted, so the scene does not see the release of a click that closed a window.</item>
    /// <item><see cref="ReleaseAll"/> (scene change) releases at once.</item>
    /// </list>
    /// Keys and flight controls are not this class's business (see <see cref="InputOwnership"/>).
    /// No Unity or KSP state; not thread-safe: use it from the game thread only.
    /// </summary>
    public class MouseBlocking
    {
        /// <summary>Frames the lock outlives the last frame it was wanted.</summary>
        public const int ReleaseDelayFrames = 2;

        private const int Never = int.MinValue;

        /// <summary>One window, or one part of a window (a popup), that blocks the mouse.</summary>
        public sealed class Area
        {
            internal Area(string name)
            {
                Name = name;
                ShownFrame = Never;
            }

            /// <summary>For logs.</summary>
            public string Name { get; private set; }

            public ScreenRect Rect { get; internal set; }

            /// <summary>The last frame it was drawn in, or <see cref="int.MinValue"/>.</summary>
            public int ShownFrame { get; internal set; }

            internal bool IsRegistered { get; set; }
        }

        private readonly IMouseLockSink sink;
        private readonly List<Area> areas = new List<Area>();
        private bool buttonWasHeld;
        private bool pressIsOurs;
        private int lastWantedFrame = Never;

        public MouseBlocking(IMouseLockSink sink)
        {
            if (sink == null)
                throw new ArgumentNullException("sink");
            this.sink = sink;
        }

        /// <summary>The sink holds the lock.</summary>
        public bool HoldsLock { get; private set; }

        public Area Register(string name)
        {
            var area = new Area(name ?? string.Empty) { IsRegistered = true };
            areas.Add(area);
            return area;
        }

        /// <summary>Forget a window that is being destroyed. Safe to call twice. The lock follows on the next update.</summary>
        public void Unregister(Area area)
        {
            if (area == null || !area.IsRegistered)
                return;
            area.IsRegistered = false;
            areas.Remove(area);
        }

        /// <summary>The window drew with this rectangle in <paramref name="frame"/>.</summary>
        public void Show(Area area, ScreenRect rect, int frame)
        {
            CheckRegistered(area);
            area.Rect = rect;
            area.ShownFrame = frame;
        }

        /// <summary>The window stopped drawing (closed, hidden); it no longer blocks from now on.</summary>
        public void Hide(Area area)
        {
            CheckRegistered(area);
            area.ShownFrame = Never;
        }

        /// <summary>True when the mouse is over a window visible in <paramref name="frame"/>.</summary>
        public bool IsOver(int frame, float mouseX, float mouseY)
        {
            foreach (Area a in areas)
                if (IsVisible(a, frame) && a.Rect.Contains(mouseX, mouseY))
                    return true;
            return false;
        }

        /// <summary>
        /// Decide the lock for the current mouse state and apply it. Call every frame (more often is
        /// harmless): the release delay counts frames, not calls. A refused change is retried by the
        /// next call; the sink's exception is rethrown after the state is kept consistent.
        /// </summary>
        /// <param name="anyButtonHeld">Any mouse button is down this frame.</param>
        public void Update(int frame, float mouseX, float mouseY, bool anyButtonHeld)
        {
            bool over = IsOver(frame, mouseX, mouseY);
            if (anyButtonHeld && !buttonWasHeld)
                pressIsOurs = over;
            buttonWasHeld = anyButtonHeld;

            bool wanted = anyButtonHeld ? pressIsOurs : over;
            if (wanted)
                lastWantedFrame = frame;

            bool keep = lastWantedFrame != Never && frame - lastWantedFrame < ReleaseDelayFrames;
            if (keep && !HoldsLock)
            {
                sink.Lock();
                HoldsLock = true;
            }
            else if (!keep && HoldsLock)
            {
                sink.Unlock();
                HoldsLock = false;
            }
        }

        /// <summary>
        /// Scene change or shutdown: release now and forget what was shown and pressed, so nothing
        /// from the old scene keeps blocking. Safe to call twice.
        /// </summary>
        public void ReleaseAll()
        {
            foreach (Area a in areas)
                a.ShownFrame = Never;
            buttonWasHeld = false;
            pressIsOurs = false;
            lastWantedFrame = Never;
            if (HoldsLock)
            {
                sink.Unlock();
                HoldsLock = false;
            }
        }

        private static bool IsVisible(Area area, int frame)
        {
            // Drawn this frame or the previous one: OnGUI runs after Update, so an update sees the
            // previous frame's drawing. Never-shown areas are skipped before subtracting.
            return area.ShownFrame != Never && area.ShownFrame <= frame && frame - area.ShownFrame <= 1;
        }

        private void CheckRegistered(Area area)
        {
            if (area == null)
                throw new ArgumentNullException("area");
            if (!area.IsRegistered)
                throw new InvalidOperationException("Mouse area '" + area.Name + "' is not registered.");
        }
    }
}
