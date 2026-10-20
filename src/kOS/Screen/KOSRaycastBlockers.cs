using System;
using System.Collections.Generic;
using kOS.Safe.UserIO;
using UnityEngine;
using UnityEngine.UI;

namespace kOS.Screen
{
    /// <summary>
    /// The game side of INPUT-02: a transparent Unity UI image over each visible kOS window, on
    /// one screen-space overlay canvas kept for the whole game. With it,
    /// <c>EventSystem.IsPointerOverGameObject()</c> is true over kOS windows, which is all that
    /// KSP's part clicks (<c>UIPartActionController.HandleMouseClick</c>) and part hover
    /// (<c>Part.UpdateMouseOver</c>) look at: no <c>InputLockManager</c> lock stops them. The
    /// canvas sorts above KSP's own (camera-space) canvases, so a stock UI element under a kOS
    /// window does not take its clicks either. kOS's IMGUI windows get their events from
    /// IMGUI, not from these images. No new dependency (Phase 1: no ClickThroughBlocker).
    /// Game thread only.
    /// </summary>
    public sealed class KOSRaycastBlockers : IRaycastBlockers
    {
        /// <summary>Above KSP's canvases: overlay canvases are tested in this order, before camera-space ones.</summary>
        public const int SortingOrder = 30000;

        private sealed class Blocker
        {
            public GameObject Object;
            public RectTransform Transform;
            public ScreenRect Rect;
            public bool Active;
        }

        private readonly Dictionary<MouseBlocking.Area, Blocker> blockers = new Dictionary<MouseBlocking.Area, Blocker>();
        private GameObject canvasObject;

        public void Show(MouseBlocking.Area area, ScreenRect rect)
        {
            Blocker blocker = Find(area);
            if (blocker == null)
            {
                blocker = Make(area);
                blockers[area] = blocker;
            }
            if (!Same(blocker.Rect, rect))
            {
                blocker.Rect = rect;
                // The canvas has no scaler: one unit is one pixel, its origin at the top left here.
                blocker.Transform.anchoredPosition = new Vector2(rect.X, -rect.Y);
                blocker.Transform.sizeDelta = new Vector2(Math.Max(0, rect.Width), Math.Max(0, rect.Height));
            }
            if (!blocker.Active)
            {
                blocker.Object.SetActive(true);
                blocker.Active = true;
            }
        }

        public void Hide(MouseBlocking.Area area)
        {
            Blocker blocker = Find(area);
            if (blocker == null || !blocker.Active) return;
            blocker.Object.SetActive(false);
            blocker.Active = false;
        }

        public void Remove(MouseBlocking.Area area)
        {
            Blocker blocker;
            if (area == null || !blockers.TryGetValue(area, out blocker)) return;
            blockers.Remove(area);
            if (blocker.Object != null) // Unity's ==: not destroyed
                UnityEngine.Object.Destroy(blocker.Object);
        }

        /// <summary>The area's blocker, made again if Unity destroyed it (or its canvas).</summary>
        private Blocker Find(MouseBlocking.Area area)
        {
            Blocker blocker;
            if (!blockers.TryGetValue(area, out blocker)) return null;
            if (blocker.Object != null && canvasObject != null) return blocker;
            blockers.Remove(area);
            return null;
        }

        private Blocker Make(MouseBlocking.Area area)
        {
            var image = new GameObject("kOSMouseBlocker " + area.Name, typeof(RectTransform));
            var transform = (RectTransform)image.transform;
            transform.SetParent(CanvasTransform(), false);
            transform.anchorMin = transform.anchorMax = transform.pivot = new Vector2(0, 1);
            Image graphic = image.AddComponent<Image>();
            graphic.color = Color.clear; // drawn as nothing, still hit by the raycaster
            graphic.raycastTarget = true;
            image.SetActive(false);
            return new Blocker { Object = image, Transform = transform, Rect = new ScreenRect(float.NaN, 0, 0, 0), Active = false };
        }

        private Transform CanvasTransform()
        {
            if (canvasObject == null)
            {
                canvasObject = new GameObject("kOSMouseBlockers", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
                UnityEngine.Object.DontDestroyOnLoad(canvasObject);
                var canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = SortingOrder;
            }
            return canvasObject.transform;
        }

        private static bool Same(ScreenRect a, ScreenRect b)
        {
            return a.X == b.X && a.Y == b.Y && a.Width == b.Width && a.Height == b.Height;
        }
    }
}
