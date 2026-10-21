using System.Collections.Generic;
using System.IO;

namespace kOS.Safe.Screen
{
    /// <summary>
    /// Whether kOS is installed where the terminal looks for its files (review L-04): the
    /// sounds it plays, which are read from <c>GameData/kOS/GFX</c> under the game's folder, so
    /// a kOS folder put anywhere else in GameData misses them. The terminal draws without any
    /// file since Phase 4; it still says so, as the sign of a broken install. Before, it loaded
    /// seven textures it no longer draws, and kept them, to tell.
    /// </summary>
    public static class TerminalInstallCheck
    {
        /// <summary>The files checked, relative to the game's folder, with <c>/</c>.</summary>
        public static readonly string[] FILES =
        {
            "GameData/kOS/GFX/terminal-beep.wav",
            "GameData/kOS/GFX/terminal-click.wav"
        };

        /// <summary>Those of <see cref="FILES"/> missing under the game's folder.</summary>
        public static IList<string> Missing(string gameFolder)
        {
            var missing = new List<string>();
            foreach (string file in FILES)
            {
                if (!File.Exists(Path.Combine(gameFolder, file.Replace('/', Path.DirectorySeparatorChar))))
                    missing.Add(file);
            }
            return missing;
        }
    }
}
