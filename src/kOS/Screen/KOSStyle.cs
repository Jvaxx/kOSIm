using System;
using System.Collections.Generic;
using kOS.Safe.Screen;
using UnityEngine;

namespace kOS.Screen
{
    /// <summary>
    /// The look shared by kOS windows (Phase 4): the palette, the chrome's font and text
    /// styles, and flat controls drawn without KSP's window skin. The terminal uses it first
    /// (step 1); the other windows adopt it later. Metrics and the UI scale come from
    /// <see cref="TerminalChromeLayout"/>.
    /// <para>
    /// Icons are drawn in code at the size they are shown (no new binary assets; a symbol
    /// glyph may be missing from KSP's font under Proton). Generated textures are kept for
    /// the whole game, like <see cref="Utilities.Utils.GetTextureWithErrorMsg"/>'s, and made
    /// again if Unity destroyed them. Game thread only.
    /// </para>
    /// </summary>
    public static class KOSStyle
    {
        // The palette (classic green; other themes are step 3):
        public static readonly Color WindowBackground = new Color(0.09f, 0.10f, 0.10f, 1f);
        public static readonly Color ChromeBackground = new Color(0.14f, 0.15f, 0.15f, 1f);
        public static readonly Color PanelBackground = new Color(0.12f, 0.13f, 0.13f, 1f);
        public static readonly Color ChipBackground = new Color(0.22f, 0.24f, 0.24f, 1f);
        public static readonly Color ChromeText = new Color(0.84f, 0.88f, 0.85f, 1f);
        public static readonly Color DimText = new Color(0.58f, 0.63f, 0.60f, 1f);
        public static readonly Color FrameColor = new Color(0.30f, 0.33f, 0.32f, 1f);
        /// <summary>The terminal's green: the focus border, a toggle that is on, the slider.</summary>
        public static readonly Color Accent = new Color(0.5f, 1.0f, 0.5f, 1f);
        public static readonly Color Warning = new Color(1.0f, 0.62f, 0.45f, 1f);
        public static readonly Color Hover = new Color(1f, 1f, 1f, 0.10f);

        /// <summary>The dot before the terminal's state.</summary>
        public static Color StatusColor(StatusKind kind)
        {
            switch (kind)
            {
                case StatusKind.Ready: return new Color(0.45f, 0.88f, 0.45f, 1f);
                case StatusKind.Running: return new Color(0.42f, 0.72f, 1.0f, 1f);
                case StatusKind.Sending: return new Color(0.42f, 0.88f, 0.92f, 1f);
                case StatusKind.Waiting:
                case StatusKind.Starting: return new Color(0.96f, 0.80f, 0.32f, 1f);
                case StatusKind.NoPower: return new Color(0.95f, 0.40f, 0.35f, 1f);
                default: return new Color(0.45f, 0.47f, 0.46f, 1f);
            }
        }

        /// <summary>KSP's UI scale (Settings, Graphics), which the chrome follows.</summary>
        public static float UiScale
        {
            get { return GameSettings.UI_SCALE; }
        }

        public enum Icon
        {
            Settings,
            Close,
            Grip,
            Dot
        }

        // The chrome's font: KSP's UI font, the terminal cells keep their own.

        private static bool fontChecked;
        private static string ellipsis = "...";
        private static string separator = " - ";

        private static Font ChromeFont
        {
            get { return HighLogic.Skin != null && HighLogic.Skin.label != null ? HighLogic.Skin.label.font : null; }
        }

        /// <summary>Ends a shortened text: "…" if the chrome font has it, else "...".</summary>
        public static string Ellipsis
        {
            get { CheckFont(); return ellipsis; }
        }

        /// <summary>Between the parts of the terminal's title: " · " if the chrome font has it, else " - ".</summary>
        public static string Separator
        {
            get { CheckFont(); return separator; }
        }

        private static void CheckFont()
        {
            if (fontChecked) return;
            Font font = ChromeFont;
            if (font == null) return; // the skin is not loaded yet: ask again later
            fontChecked = true;
            bool hasEllipsis = HasGlyph(font, '…'), hasDot = HasGlyph(font, '·');
            if (hasEllipsis) ellipsis = "…";
            if (hasDot) separator = " · ";
            Debug.Log(string.Format("kOS: chrome font {0}: ellipsis {1}, middle dot {2}", font.name,
                hasEllipsis ? "yes" : "no (using ...)", hasDot ? "yes" : "no (using -)"));
        }

        private static bool HasGlyph(Font font, char c)
        {
            try
            {
                return font.HasCharacter(c);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static readonly Dictionary<int, GUIStyle> labels = new Dictionary<int, GUIStyle>();
        private static readonly Dictionary<int, GUIStyle> titles = new Dictionary<int, GUIStyle>();
        private static readonly Dictionary<int, GUIStyle> centered = new Dictionary<int, GUIStyle>();
        private static readonly GUIContent measured = new GUIContent();

        /// <summary>One line of chrome text, vertically centered in its rectangle.</summary>
        public static GUIStyle Label(int fontSize)
        {
            return TextStyle(labels, fontSize, TextAnchor.MiddleLeft, FontStyle.Normal);
        }

        public static GUIStyle TitleLabel(int fontSize)
        {
            return TextStyle(titles, fontSize, TextAnchor.MiddleLeft, FontStyle.Bold);
        }

        public static GUIStyle CenteredLabel(int fontSize)
        {
            return TextStyle(centered, fontSize, TextAnchor.MiddleCenter, FontStyle.Normal);
        }

        private static GUIStyle TextStyle(Dictionary<int, GUIStyle> cache, int fontSize, TextAnchor alignment, FontStyle fontStyle)
        {
            GUIStyle style;
            if (cache.TryGetValue(fontSize, out style) && style.font == ChromeFont)
                return style;
            style = new GUIStyle
            {
                font = ChromeFont,
                fontSize = fontSize,
                fontStyle = fontStyle,
                alignment = alignment,
                wordWrap = false,
                richText = false, // vessel names and paths are shown as they are
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
            style.normal.textColor = ChromeText;
            cache[fontSize] = style;
            return style;
        }

        /// <summary>The width of <paramref name="text"/> in <paramref name="style"/>.</summary>
        public static float Measure(GUIStyle style, string text)
        {
            measured.text = text;
            return style.CalcSize(measured).x;
        }

        /// <summary>Draws <paramref name="text"/> in <paramref name="color"/>, the style's own color left as it was.</summary>
        public static void Text(Rect where, string text, GUIStyle style, Color color)
        {
            Color previous = style.normal.textColor;
            style.normal.textColor = color;
            try
            {
                GUI.Label(where, text, style);
            }
            finally
            {
                style.normal.textColor = previous;
            }
        }

        // Drawing:

        public static void Fill(Rect where, Color color)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            Color previous = GUI.color;
            GUI.color = color;
            try
            {
                GUI.DrawTexture(where, Texture2D.whiteTexture, ScaleMode.StretchToFill, true);
            }
            finally
            {
                GUI.color = previous;
            }
        }

        /// <summary>A border <paramref name="thickness"/> pixels wide just inside <paramref name="where"/>.</summary>
        public static void Frame(Rect where, int thickness, Color color)
        {
            float t = Math.Min(thickness, Math.Min(where.width, where.height) / 2f);
            if (t <= 0) return;
            Fill(new Rect(where.x, where.y, where.width, t), color);
            Fill(new Rect(where.x, where.yMax - t, where.width, t), color);
            Fill(new Rect(where.x, where.y + t, t, where.height - 2 * t), color);
            Fill(new Rect(where.xMax - t, where.y + t, t, where.height - 2 * t), color);
        }

        /// <summary>An icon <paramref name="size"/> pixels square, centered in <paramref name="where"/>.</summary>
        public static void DrawIcon(Rect where, Icon icon, int size, Color color)
        {
            if (size <= 0 || Event.current == null || Event.current.type != EventType.Repaint) return;
            Texture2D texture = IconTexture(icon, size);
            var square = new Rect(Mathf.Round(where.x + (where.width - size) / 2f), Mathf.Round(where.y + (where.height - size) / 2f), size, size);
            Color previous = GUI.color;
            GUI.color = color;
            try
            {
                GUI.DrawTexture(square, texture, ScaleMode.StretchToFill, true);
            }
            finally
            {
                GUI.color = previous;
            }
        }

        private static bool IsHovered(Rect where)
        {
            return Event.current != null && where.Contains(Event.current.mousePosition);
        }

        /// <summary>A flat square button showing <paramref name="icon"/>; <paramref name="active"/> keeps it lit.</summary>
        public static bool IconButton(Rect where, Icon icon, int iconSize, bool active)
        {
            if (active || IsHovered(where))
                Fill(where, Hover);
            DrawIcon(where, icon, iconSize, active ? Accent : ChromeText);
            return GUI.Button(where, GUIContent.none, GUIStyle.none);
        }

        /// <summary>A flat text button.</summary>
        public static bool Button(Rect where, string text, int fontSize)
        {
            Fill(where, ChipBackground);
            if (IsHovered(where))
                Fill(where, Hover);
            Text(where, text, CenteredLabel(fontSize), ChromeText);
            return GUI.Button(where, GUIContent.none, GUIStyle.none);
        }

        /// <summary>A check box and its label, the whole row clickable.</summary>
        /// <returns>the new value</returns>
        public static bool Toggle(Rect where, bool value, string text, int fontSize, int border)
        {
            if (IsHovered(where))
                Fill(where, Hover);
            float box = Mathf.Round(fontSize * 0.9f);
            var boxRect = new Rect(where.x + 2, Mathf.Round(where.y + (where.height - box) / 2f), box, box);
            Frame(boxRect, border, value ? Accent : FrameColor);
            if (value)
            {
                float inset = Math.Max(2, Mathf.Round(box / 4f));
                Fill(new Rect(boxRect.x + inset, boxRect.y + inset, box - 2 * inset, box - 2 * inset), Accent);
            }
            Text(new Rect(boxRect.xMax + box / 2f, where.y, Math.Max(0, where.xMax - boxRect.xMax - box / 2f), where.height),
                text, Label(fontSize), ChromeText);
            return GUI.Toggle(where, value, GUIContent.none, GUIStyle.none);
        }

        private static GUIStyle sliderStyle;
        private static GUIStyle thumbStyle;
        private static Texture2D thumbTexture;

        /// <summary>A horizontal slider: a thin track, filled up to the thumb.</summary>
        public static float Slider(Rect where, float value, float min, float max)
        {
            float thumbWidth = Math.Max(6, Mathf.Round(where.height * 0.45f));
            if (sliderStyle == null || thumbTexture == null)
            {
                thumbTexture = SolidTexture(Accent);
                sliderStyle = new GUIStyle { padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0) };
                thumbStyle = new GUIStyle { padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0) };
                thumbStyle.normal.background = thumbTexture;
                thumbStyle.hover.background = thumbTexture;
                thumbStyle.active.background = thumbTexture;
            }
            thumbStyle.fixedWidth = thumbWidth;
            float track = Math.Max(2, Mathf.Round(where.height / 6f));
            float y = Mathf.Round(where.y + (where.height - track) / 2f);
            float fraction = max > min ? Mathf.Clamp01((value - min) / (max - min)) : 0f;
            Fill(new Rect(where.x, y, where.width, track), FrameColor);
            Fill(new Rect(where.x, y, thumbWidth / 2f + fraction * (where.width - thumbWidth), track), DimText);
            return GUI.HorizontalSlider(where, value, min, max, sliderStyle, thumbStyle);
        }

        // Generated textures:

        private static readonly Dictionary<int, Texture2D> icons = new Dictionary<int, Texture2D>();

        private static Texture2D IconTexture(Icon icon, int size)
        {
            int key = (int)icon * 4096 + Math.Min(size, 4095);
            Texture2D texture;
            if (icons.TryGetValue(key, out texture) && texture != null) // Unity's ==: not destroyed
                return texture;
            texture = MakeIcon(size, Coverage(icon, size));
            icons[key] = texture;
            return texture;
        }

        private static Texture2D SolidTexture(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixel(0, 0, color);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>
        /// White, with the icon's coverage of each pixel as alpha (4x4 samples a pixel), so it
        /// is tinted by <c>GUI.color</c> and smooth at any size.
        /// </summary>
        private static Texture2D MakeIcon(int size, Func<float, float, bool> inside)
        {
            const int samples = 4;
            var pixels = new Color32[size * size];
            for (int py = 0; py < size; ++py)
                for (int px = 0; px < size; ++px)
                {
                    int hits = 0;
                    for (int sy = 0; sy < samples; ++sy)
                        for (int sx = 0; sx < samples; ++sx)
                        {
                            float u = (px + (sx + 0.5f) / samples) / size;
                            float v = (py + (sy + 0.5f) / samples) / size; // from the top
                            if (inside(u, v)) ++hits;
                        }
                    // Texture rows start at the bottom:
                    pixels[(size - 1 - py) * size + px] = new Color32(255, 255, 255, (byte)(255 * hits / (samples * samples)));
                }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>Whether a point of the icon's unit square (from the top left) is drawn.</summary>
        private static Func<float, float, bool> Coverage(Icon icon, int size)
        {
            // Strokes at least about 1.3 pixels wide, whatever the size:
            float half = Math.Max(0.065f, 0.65f / size);
            switch (icon)
            {
                case Icon.Settings:
                    return (u, v) =>
                    {
                        float dx = u - 0.5f, dy = v - 0.5f;
                        float r = (float)Math.Sqrt(dx * dx + dy * dy);
                        double angle = Math.Atan2(dy, dx);
                        float outer = Math.Cos(8 * angle) > 0.1 ? 0.48f : 0.36f; // eight teeth
                        return r <= outer && r >= 0.15f;
                    };
                case Icon.Close:
                    return (u, v) =>
                    {
                        float dx = u - 0.5f, dy = v - 0.5f;
                        if (Math.Abs(dx) > 0.32f || Math.Abs(dy) > 0.32f) return false;
                        return Math.Abs(dx - dy) / 1.4142f <= half || Math.Abs(dx + dy) / 1.4142f <= half;
                    };
                case Icon.Grip:
                    return (u, v) =>
                    {
                        if (u > 0.85f || v > 0.85f) return false;
                        float d = u + v;
                        foreach (float line in new[] { 1.0f, 1.3f, 1.6f })
                            if (Math.Abs(d - line) / 1.4142f <= half) return true;
                        return false;
                    };
                default: // Dot
                    return (u, v) =>
                    {
                        float dx = u - 0.5f, dy = v - 0.5f;
                        return dx * dx + dy * dy <= 0.25f;
                    };
            }
        }
    }
}
