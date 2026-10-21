using System;
using System.Collections.Generic;
using System.Threading;
using kOS.Safe.Screen;
using UnityEngine;

namespace kOS.Module
{
    /// <summary>
    /// Removes the command history and terminal geometry files of parts no save has any more
    /// (review L-02; <see cref="CpuFilePrune"/>) once a save is loaded from the main menu: not
    /// on a quickload, a revert or a scene change, so a vessel those take away keeps its files
    /// until the save is next loaded from the main menu. In the main menu it listens for the
    /// load; in the first game scene after it, it takes the loaded game's part ids and prunes
    /// on a worker thread, as the saves of a long career can take a while to read.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.EveryScene, false)]
    public class CpuFilePruner : MonoBehaviour
    {
        private static bool loadedFromMainMenu; // game thread only
        private bool listening;

        /// <summary><c>saves/&lt;save&gt;</c> under the game's folder.</summary>
        public static string SaveFolderPath(string saveFolder)
        {
            return System.IO.Path.Combine(KSPUtil.ApplicationRootPath, "saves", saveFolder);
        }

        public void Awake()
        {
            if (HighLogic.LoadedScene == GameScenes.MAINMENU)
            {
                loadedFromMainMenu = false; // a game left for the main menu was pruned when loaded
                GameEvents.onGameStatePostLoad.Add(OnGameStatePostLoad);
                listening = true;
            }
            else if (loadedFromMainMenu && HighLogic.LoadedSceneIsGame)
            {
                loadedFromMainMenu = false;
                StartPrune();
            }
        }

        public void OnDestroy()
        {
            if (listening)
                GameEvents.onGameStatePostLoad.Remove(OnGameStatePostLoad);
        }

        private void OnGameStatePostLoad(ConfigNode game)
        {
            // HighLogic.SaveFolder may be set only after this event: the prune waits for the
            // game's first scene.
            loadedFromMainMenu = true;
        }

        private static void StartPrune()
        {
            try
            {
                Game game = HighLogic.CurrentGame;
                if (game == null || game.flightState == null || game.flightState.protoVessels == null ||
                    string.IsNullOrEmpty(HighLogic.SaveFolder))
                    return; // nothing known to compare with: keep every file

                var loadedIds = new HashSet<uint>();
                foreach (ProtoVessel vessel in game.flightState.protoVessels)
                {
                    if (vessel == null || vessel.protoPartSnapshots == null) continue;
                    foreach (ProtoPartSnapshot part in vessel.protoPartSnapshots)
                        if (part != null) loadedIds.Add(part.persistentId);
                }
                string saveFolder = SaveFolderPath(HighLogic.SaveFolder);
                DateTime started = DateTime.UtcNow;
                var worker = new Thread(() => Prune(saveFolder, loadedIds, started))
                {
                    IsBackground = true,
                    Name = "kOS CPU file prune"
                };
                worker.Start();
            }
            catch (Exception e)
            {
                Debug.LogWarning("kOS: CPU file prune not started: " + e.Message);
            }
        }

        private static void Prune(string saveFolder, HashSet<uint> loadedIds, DateTime started)
        {
            try
            {
                CpuFilePrune.Result result = CpuFilePrune.Run(saveFolder, loadedIds, started);
                // Debug.Log is safe off the game thread.
                if (result.Skipped != null)
                    Debug.Log("kOS: CPU files of parts no save has not removed: " + result.Skipped);
                else if (result.Deleted > 0 || result.TemporaryDeleted > 0)
                    Debug.Log(string.Format("kOS: removed {0} CPU file(s) of parts no save has, and {1} leftover temporary file(s), in {2}",
                        result.Deleted, result.TemporaryDeleted, saveFolder));
            }
            catch (Exception e)
            {
                Debug.LogWarning("kOS: CPU file prune failed in " + saveFolder + ": " + e.Message);
            }
        }
    }
}
