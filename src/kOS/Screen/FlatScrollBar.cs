using kOS.Safe.Screen;
using kOS.Safe.UserIO;
using UnityEngine;

namespace kOS.Screen
{
    /// <summary>
    /// The thin scroll bar of kOS's flat windows (Phase 4: the toolbar panel, the editor, the
    /// list pickers): drag the thumb; a press beside it moves a page. Positions from
    /// <see cref="PixelScroll"/>. One per scrolled view: it remembers a drag in progress.
    /// </summary>
    public sealed class FlatScrollBar
    {
        private bool dragging;
        private float grabOffset;

        public bool IsDragging { get { return dragging; } }

        /// <summary>Lets go of a drag (the view changed under it).</summary>
        public void Release()
        {
            dragging = false;
        }

        /// <summary>Handles this event and draws the bar in <paramref name="track"/>.</summary>
        /// <returns>the scroll after this event</returns>
        public float Draw(ScreenRect track, float scroll, float contentHeight, float viewHeight, float minimumThumb)
        {
            var trackRect = new Rect(track.X, track.Y, track.Width, track.Height);
            ScreenRect thumb = PixelScroll.Thumb(track, contentHeight, viewHeight, scroll, minimumThumb);
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && trackRect.Contains(e.mousePosition))
            {
                if (thumb.Contains(e.mousePosition.x, e.mousePosition.y))
                {
                    dragging = true;
                    grabOffset = e.mousePosition.y - thumb.Y;
                }
                else
                {
                    float page = e.mousePosition.y < thumb.Y ? -viewHeight : viewHeight;
                    scroll = PixelScroll.Clamp(scroll + page, contentHeight, viewHeight);
                }
                e.Use();
            }
            else if (dragging && !Input.GetMouseButton(0))
            {
                dragging = false; // released, also outside the window
            }
            else if (dragging && e.type == EventType.MouseDrag)
            {
                scroll = PixelScroll.ScrollFor(e.mousePosition.y - grabOffset, track, contentHeight, viewHeight, minimumThumb);
                e.Use();
            }
            KOSStyle.Fill(trackRect, KOSStyle.ScrollTrack);
            ScreenRect shown = PixelScroll.Thumb(track, contentHeight, viewHeight, scroll, minimumThumb);
            KOSStyle.Fill(new Rect(shown.X, shown.Y, shown.Width, shown.Height), dragging ? KOSStyle.ChromeText : KOSStyle.DimText);
            return scroll;
        }
    }
}
