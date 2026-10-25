using System;
using GHPC.State;
using MelonLoader;
using UnityEngine;

// MelonLoader identifies the mod via these two attributes; missing either one causes the mod to be refused at load time.
[assembly: MelonInfo(typeof(NatoEra.NatoEra), "NATO ERA", "1.2.6", "Cyance, WpsxX")]
[assembly: MelonGame("Radian Simulations LLC", "GHPC")]

namespace NatoEra
{
    /// <summary>
    /// Mod entry point.
    ///
    /// Lifecycle:
    ///   1. OnInitializeMelon -- register preferences, resolve reflected fields, register transpilers
    ///   2. OnSceneWasLoaded -- detect whether this is a combat scene; if so, load the AssetBundle
    ///      and trigger the vehicle conversion on the GameReady state
    /// </summary>
    public class NatoEra : MelonMod
    {
        private const string ModName = "NATO ERA";
        private const string ModVersion = "1.2.6";
        public const string ModAuthors = "Cyance, WpsxX";

        private static bool _bundlesLoaded;
        private static bool _patchesApplied;

        /// <summary>
        /// Menu-type scenes. Any of these names means we are still on the main menu / loading screen, so the ERA kit is not needed.
        /// Reuses the legacy name table.
        /// </summary>
        private static readonly string[] MenuScenes =
        {
            "MainMenu2_Scene",
            "MainMenu2-1_Scene",
            "LOADER_MENU",
            "LOADER_INITIAL",
            "t64_menu",
        };

        public override void OnInitializeMelon()
        {
            MelonLogger.Msg("[NatoERA] " + ModName + " v" + ModVersion + " initializing...");

            // 1. Preferences (also pushes the values into the static Config fields)
            Config.Init();

            // 2. Resolve the game's internal fields via reflection (done up front so any problem shows up in the log early)
            Compat.Resolve();

            MelonLogger.Msg("[NatoERA] Init complete. ERA will be fitted to M1/M1IP/M2 Bradley automatically once GameReady fires.");
        }

        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            // Anything cached from the scene that just went away must not be reused
            EraDetonateVisuals.Reset();
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            if (IsMenuScene(sceneName))
            {
                MelonLogger.Msg("[NatoERA] Scene \"" + sceneName + "\" is a menu/loading scene, skipping.");
                return;
            }

            MelonLogger.Msg("[NatoERA] Entered combat scene \"" + sceneName + "\", preparing to fit ERA.");

            // The cached impact-burst AmmoType references objects from the previous scene
            EraDetonateVisuals.Reset();

            // The AssetBundle only needs to be loaded once
            if (!_bundlesLoaded)
            {
                _bundlesLoaded = true;
                try
                {
                    AbramsEra.Init();
                    BradleyEra.Init();
                }
                catch (Exception e)
                {
                    MelonLogger.Error("[NatoERA] Failed to load AssetBundle: " + e);
                }
            }

            // The transpilers only need to take effect once (Harmony patches are global)
            if (!_patchesApplied)
            {
                _patchesApplied = true;
                try
                {
                    Config.ApplyToPatches();
                    MelonLogger.Msg("[NatoERA] ERA detonation rule active: a brick only detonates when the round penetrates it.");
                }
                catch (Exception e)
                {
                    MelonLogger.Error("[NatoERA] Failed to apply sensitivity patch parameters: " + e.Message);
                }
            }

            // Run the conversion on the GameReady state: by then every vehicle has been instantiated
            try
            {
                StateController.RunOrDefer(GameState.GameReady, new GameStateEventHandler(OnGameReady),
                                           GameStatePriority.Medium);
            }
            catch (Exception e)
            {
                MelonLogger.Error("[NatoERA] Failed to register GameReady callback: " + e);
            }
        }

        /// <summary>
        /// Frames to wait after GameReady before fitting anything.
        ///
        /// StateController starts every handler of one priority at the same time and then
        /// waits for the whole group before moving on to the next priority, so whatever we
        /// do here competes with the other Medium handlers on the main thread. Handing the
        /// frame back a few times first lets those handlers spawn their vehicles, which is
        /// what the scene is still busy doing when we arrive.
        /// </summary>
        private const int SettleFrames = 30;

        private static System.Collections.IEnumerator OnGameReady(GameState state)
        {
            MelonLogger.Msg("[NatoERA] GameReady fired, letting the scene settle before fitting.");

            // Give the other GameReady handlers (and the spawners that run alongside them)
            // a window to finish creating vehicles, so the scan below sees the final set.
            for (int i = 0; i < SettleFrames; i++)
            {
                yield return null;
            }

            int unitsBefore = CountUnits();
            MelonLogger.Msg("[NatoERA] Fitting starts with " + unitsBefore + " unit(s) in the scene.");

            // Vehicles another mod has rebuilt are off limits. The M8-LOSAT mod converts one
            // stock M2 Bradley every 1.5 s and does that work synchronously, so a vehicle it
            // has already taken over is detectable here - and if this mod got there first the
            // conversion would fail instead. Strip our ERA off those and remember them, so the
            // two passes below skip them outright.
            SweepForeignVehicles();

            // yield cannot sit inside try/catch, so each pass is wrapped the only way the
            // language allows: hand the enumerator to a guarded drain that returns it.
            var arat = AbramsEra.ConvertCoroutine(state);
            while (RunGuarded(arat, "M1/M1IP fitting"))
            {
                yield return null;
            }

            var brat = BradleyEra.ConvertCoroutine(state);
            while (RunGuarded(brat, "M2 Bradley fitting"))
            {
                yield return null;
            }

            int unitsAfter = CountUnits();
            MelonLogger.Msg("[NatoERA] ERA fitting pass finished (" + unitsAfter + " unit(s) in the scene).");
        }

        /// <summary>
        /// Runs <see cref="M8Guard"/> over every unit in the scene once.
        ///
        /// This is the only place that needs to know about a specific other mod; the two
        /// fitting passes ask the guard themselves too, so a vehicle converted between this
        /// sweep and the pass is still caught.
        /// </summary>
        private static void SweepForeignVehicles()
        {
            try
            {
                M8Guard.Prune();

                int skipped = 0;
                foreach (var unit in UnityEngine.Object.FindObjectsOfType<GHPC.Unit>())
                {
                    if (unit == null) continue;
                    if (M8Guard.ClearIfM8Converted(unit) > 0) skipped++;
                }

                if (skipped > 0)
                {
                    MelonLogger.Warning("[NatoERA] " + skipped + " vehicle(s) belong to another mod and were left unarmoured.");
                }
            }
            catch (Exception e)
            {
                MelonLogger.Error("[NatoERA] Foreign-vehicle sweep failed: " + e);
            }
        }

        /// <summary>
        /// Advances one step of <paramref name="enumerator"/>, logging instead of throwing.
        ///
        /// A coroutine body cannot yield from inside a try/catch, so the exception guard has
        /// to live in a helper like this one. Returns true while there is more work to do.
        /// </summary>
        private static bool RunGuarded(System.Collections.IEnumerator enumerator, string label)
        {
            try
            {
                return enumerator.MoveNext();
            }
            catch (Exception e)
            {
                MelonLogger.Error("[NatoERA] " + label + " failed: " + e);
                return false;
            }
        }

        /// <summary>
        /// Number of Unit components currently alive. Used only for logging, so it is cheap
        /// and never throws.
        /// </summary>
        private static int CountUnits()
        {
            try
            {
                return UnityEngine.Object.FindObjectsOfType<GHPC.Unit>().Length;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        private static bool IsMenuScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return false;

            foreach (var m in MenuScenes)
            {
                if (string.Equals(sceneName, m, StringComparison.OrdinalIgnoreCase)) return true;
                if (sceneName.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }
    }
}
