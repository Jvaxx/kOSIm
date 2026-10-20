using System;
using System.Globalization;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// A color as data (sRGB, 0–255 a channel, alpha 0–1), with what the palette's tests need:
    /// drawing it over another color and WCAG 2 contrast ratios.
    /// </summary>
    public struct PaletteColor : IEquatable<PaletteColor>
    {
        public readonly byte R;
        public readonly byte G;
        public readonly byte B;
        public readonly float A;

        public PaletteColor(byte r, byte g, byte b, float a = 1f)
        {
            R = r;
            G = g;
            B = b;
            A = Math.Max(0f, Math.Min(1f, a));
        }

        /// <summary>From <c>#RRGGBB</c>, opaque.</summary>
        public static PaletteColor FromHex(string hex)
        {
            if (hex == null) throw new ArgumentNullException("hex");
            string digits = hex.StartsWith("#") ? hex.Substring(1) : hex;
            if (digits.Length != 6) throw new FormatException("expected #RRGGBB: " + hex);
            int value = int.Parse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return new PaletteColor((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        }

        public PaletteColor WithAlpha(float alpha)
        {
            return new PaletteColor(R, G, B, alpha);
        }

        /// <summary>This color drawn over an opaque <paramref name="below"/> (blended in sRGB, as the GUI draws).</summary>
        public PaletteColor Over(PaletteColor below)
        {
            return new PaletteColor(Blend(R, below.R), Blend(G, below.G), Blend(B, below.B));
        }

        private byte Blend(byte top, byte below)
        {
            return (byte)Math.Round(A * top + (1 - A) * below, MidpointRounding.AwayFromZero);
        }

        /// <summary>WCAG 2's relative luminance of the opaque color.</summary>
        public double RelativeLuminance
        {
            get { return 0.2126 * Linear(R) + 0.7152 * Linear(G) + 0.0722 * Linear(B); }
        }

        private static double Linear(byte channel)
        {
            double c = channel / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        /// <summary>WCAG 2's contrast ratio of two opaque colors, from 1 to 21.</summary>
        public static double ContrastRatio(PaletteColor a, PaletteColor b)
        {
            double la = a.RelativeLuminance, lb = b.RelativeLuminance;
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        public bool Equals(PaletteColor other)
        {
            return R == other.R && G == other.G && B == other.B && A == other.A;
        }

        public override bool Equals(object obj)
        {
            return obj is PaletteColor && Equals((PaletteColor)obj);
        }

        public override int GetHashCode()
        {
            return (R << 16 | G << 8 | B) ^ A.GetHashCode();
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}{3}", R, G, B,
                A < 1f ? string.Format(CultureInfo.InvariantCulture, " a{0:0.##}", A) : "");
        }
    }

    /// <summary>
    /// The GUI terminal's colors (Phase 4, step 3): one modern green look, no themes. Soft green
    /// text on a near-black body; the chrome (header, strip, chips) in dark neutral greys a step
    /// lighter; the accent (focus border, edges, notices) is the text's green. The contrast
    /// each pairing needs is checked by tests (WCAG 2 ratios), also at the default brightness,
    /// which fades the text into the background (<c>TERMINAL:BRIGHTNESS</c> is the text's alpha).
    /// </summary>
    public static class TerminalPalette
    {
        // The cells:
        public static readonly PaletteColor Background = PaletteColor.FromHex("#0F1311");
        public static readonly PaletteColor Text = PaletteColor.FromHex("#7EE08F");
        /// <summary>The text while the CPU has no power.</summary>
        public static readonly PaletteColor UnpoweredText = PaletteColor.FromHex("#ABB3AE");
        /// <summary>The cursor block (the character in it takes the background's color).</summary>
        public static readonly PaletteColor Cursor = Text;
        /// <summary>
        /// Over the selected cells, after the characters: the text's color at this alpha, times
        /// the brightness. Lower than it was (0.35) so that the text under it keeps its contrast.
        /// </summary>
        public const float SELECTION_ALPHA = 0.25f;

        // The chrome:
        /// <summary>Under everything in the window, the fallback screens too.</summary>
        public static readonly PaletteColor WindowBackground = Background;
        /// <summary>The header and the status strip.</summary>
        public static readonly PaletteColor Chrome = PaletteColor.FromHex("#1A1F1C");
        /// <summary>A panel over the window (a popup, the toolbar panel's sections).</summary>
        public static readonly PaletteColor Panel = PaletteColor.FromHex("#151A17");
        public static readonly PaletteColor Chip = PaletteColor.FromHex("#2A322D");
        public static readonly PaletteColor ChromeText = PaletteColor.FromHex("#DCE3DE");
        /// <summary>Secondary chrome text (the size), the grip, a button that cannot be used.</summary>
        public static readonly PaletteColor DimText = PaletteColor.FromHex("#98A29C");
        /// <summary>The frame while the terminal does not have the keyboard.</summary>
        public static readonly PaletteColor Frame = PaletteColor.FromHex("#353D38");
        /// <summary>The focus border, a resize edge, the <c>New output</c> chip, notices.</summary>
        public static readonly PaletteColor Accent = Text;
        /// <summary>The <c>No connection</c> chip.</summary>
        public static readonly PaletteColor Warning = PaletteColor.FromHex("#F2A477");
        /// <summary>Over a control under the pointer.</summary>
        public static readonly PaletteColor Hover = new PaletteColor(255, 255, 255, 0.10f);
        /// <summary>The scroll bar's track, faint over the characters' background; its thumb is <see cref="DimText"/>, <see cref="ChromeText"/> when held.</summary>
        public static readonly PaletteColor ScrollTrack = new PaletteColor(255, 255, 255, 0.08f);

        /// <summary>The dot before the terminal's state in the strip.</summary>
        public static PaletteColor Status(StatusKind kind)
        {
            switch (kind)
            {
                case StatusKind.Ready: return PaletteColor.FromHex("#6FD483");
                case StatusKind.Running: return PaletteColor.FromHex("#6FA8F2");
                case StatusKind.Sending: return PaletteColor.FromHex("#5CCBD6");
                case StatusKind.Waiting:
                case StatusKind.Starting: return PaletteColor.FromHex("#E8C15A");
                case StatusKind.NoPower: return PaletteColor.FromHex("#EE7466");
                default: return PaletteColor.FromHex("#858E89");
            }
        }
    }
}
