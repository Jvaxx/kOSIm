using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// The fonts the GUI terminal tries, in order (Phase 4, step 3c): the first the OS has is
    /// used. A font the user picked comes first; then upstream's fallbacks in upstream's order,
    /// with <c>Lucida Console</c> before the Courier entries. Under Proton the game sees only
    /// the prefix's fonts, which have none of Consolas, Monaco or Liberation Mono, so the
    /// terminal gets Lucida Console instead of Courier New Bold; native Windows, macOS and Linux
    /// find what they found before (step 3a probe, <c>docs/testing-and-build.md</c>).
    /// </summary>
    public static class TerminalFontChoice
    {
        /// <summary><c>TerminalFontName</c>'s default in <c>config.xml</c>: no font picked.</summary>
        public const string NOT_CHOSEN_YET = "_not_chosen_yet_";
        /// <summary>What the first run puts in <c>TerminalFontName</c> (<c>Bootstrapper.CheckForUnpickedFont</c>): no font picked.</summary>
        public const string CHOOSE_FONT = "Choose Font";

        public const string LUCIDA_CONSOLE = "Lucida Console";

        /// <summary>Tried after the user's pick.</summary>
        public static readonly ReadOnlyCollection<string> Fallbacks = new ReadOnlyCollection<string>(new[]
        {
            "Consolas Bold",        // typical Windows good programming font
            "Consolas",
            "Monaco Bold",          // typical Mac good programming font
            "Monaco",
            "Liberation Mono Bold", // typical Linux good programming font
            "Liberation Mono",
            LUCIDA_CONSOLE,         // in Windows and in a Proton prefix; before the Courier ones (step 3c)
            "Courier New Bold",     // The Courier ones are ugly fallbacks just in case.
            "Courier Bold",
            "Courier New",
            "Courier",
            "Arial"                 // very bad, proportional, but guaranteed to exist in Unity no matter what.
        });

        /// <summary>Whether <paramref name="fontName"/> (<c>TerminalFontName</c>) names a font the user picked.</summary>
        public static bool IsPicked(string fontName)
        {
            return !string.IsNullOrEmpty(fontName) && fontName.Trim().Length > 0 &&
                fontName != NOT_CHOSEN_YET && fontName != CHOOSE_FONT;
        }

        /// <summary>The names to try, in order, for the configured <paramref name="pickedFontName"/>.</summary>
        public static string[] NamesToTry(string pickedFontName)
        {
            var names = new List<string>(Fallbacks.Count + 1);
            if (IsPicked(pickedFontName))
                names.Add(pickedFontName);
            names.AddRange(Fallbacks);
            return names.ToArray();
        }
    }
}
