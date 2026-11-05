using System;
using GHPC.State;
using MelonLoader;
using UnityEngine;

// MelonLoader identifies the mod via these two attributes; missing either one causes the mod to be refused at load time.
[assembly: MelonInfo(typeof(NatoEra.NatoEra), "NATO ERA", "1.2.9", "Cyance, WpsxX")]
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
        private const string ModVersion = "1.2.9";
        public const string ModAuthors = "Cyance, WpsxX";

        private static bool _bundlesLoaded;
        private static bool _patchesApplied;

        /// <summary>
        /// Incremented for every combat scene we enter, so a GameReady registration can tell
        /// whether it still belongs to the newest mission.
        ///
        /// GHPC loads a terrain scene and then the mission scene, so OnSceneWasLoaded fires twice
        /// per deployment, and StateController.GameStatePriorityEventHolder.Add does a plain
        /// "GameStateEventTrigger += handler" with no de-duplication (StateController.cs:338-343):
        /// two registrations meant two fitting passes over the same vehicles (the log showed the
        /// same vehicle gaining 4985 kg twice and BRAT reported as 2 of 4 Bradleys).
        ///
        /// A plain "already queued?" boolean fixed the double pass but introduced a deadlock:
        /// any scene that was treated as a combat scene but never reached GameReady (for example
        /// the "leo2_menu" preview scene) left the flag set, and every later mission in that
        /// session was refused - no ERA at all until the player returned to the main menu, which
        /// is exactly the "ERA is missing until I re-enter the mission" report.
        ///
        /// A monotonically increasing token has neither problem: the terrain/mission pair still
        /// registers twice, but only the newest handler survives, and a registration made in a
        /// scene that never reaches GameReady simply expires instead of starving the real one.
        /// </summary>
        private static int _missionToken;

        /// <summary>
        /// The token of the mission that has already started fitting. Guards against the same
        /// mission being fitted twice if GameReady fires more than once for one token.
        /// </summary>
        private static int _fittedToken = -1;

        /// <summary>
        /// A fitting pass is executing right now. Belt and braces for <see cref="_fittedToken"/>:
        /// if two registrations ever slip through, the second pass returns instead of running
        /// side by side with the first.
        /// </summary>
        private static bool _fitting;

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
            // Not a mission: it is the Leopard 2 preview/hangar scene GHPC plays while the
            // real mission is being set up. It never reaches GameReady, so registering a
            // fitting pass here used to leave the old "already queued" flag set and starve
            // every later mission in the session.
            "leo2_menu",
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
                // Back at the menu: the mission that owned the previous registration is gone.
                // Nothing to reset - a stale registration expires by itself as soon as the next
                // combat scene bumps _missionToken. (_fitting is deliberately not reset here: a
                // pass that is still running must stay exclusive until it ends on its own.)
                MelonLogger.Msg("[NatoERA] Scene \"" + sceneName + "\" is a menu/loading scene, skipping.");
                return;
            }

            MelonLogger.Msg("[NatoERA] Entered combat scene \"" + sceneName + "\", preparing to fit ERA.");

            // The cached impact-burst AmmoType references objects from the previous scene
            EraDetonateVisuals.Reset();

            // The AssetBundle only needs to be loaded once - but only a *successful* load counts.
            // The flag used to be set before the call, so a single failure (a bundle locked by
            // another copy of the game, a drive that was not ready yet) switched ERA off for the
            // rest of the session with no way back but a restart. Init() only fills prefab fields
            // and is safe to retry, so a failed attempt now tries again on the next mission.
            if (!_bundlesLoaded)
            {
                try
                {
                    AbramsEra.Init();
                    BradleyEra.Init();
                    _bundlesLoaded = true;
                }
                catch (Exception e)
                {
                    MelonLogger.Error("[NatoERA] Failed to load AssetBundle (will retry next mission): " + e);
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

            // Run the conversion on the GameReady state: by then every vehicle has been instantiated.
            //
            // Register at most once per mission. Terrain scene + mission scene both come through
            // here, and StateController does not de-duplicate handlers: two registrations meant
            // two concurrent fitting passes over the same vehicles.
            //
            // Every combat scene claims a new token. The terrain scene and the mission scene of
            // one deployment both register a handler; both fire on GameReady, but only the one
            // holding the current token is allowed to run, so exactly one pass fits this mission.
            int token = ++_missionToken;
            try
            {
                StateController.RunOrDefer(GameState.GameReady,
                                           new GameStateEventHandler(s => OnGameReady(s, token)),
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

        private static System.Collections.IEnumerator OnGameReady(GameState state, int token)
        {
            // A later combat scene has already taken over. This handler belongs to a scene that
            // is no longer the current mission (typically the terrain scene that preceded it),
            // so it must not fit anything - that is what produced the duplicate ARAT mass.
            if (token != _missionToken)
            {
                yield break;
            }

            if (_fitting)
            {
                MelonLogger.Msg("[NatoERA] A fitting pass is already running, ignoring this GameReady callback.");
                yield break;
            }

            // Same mission, second GameReady: the pass below already fits everything that exists.
            if (_fittedToken == token)
            {
                yield break;
            }

            _fittedToken = token;
            _fitting = true;

            MelonLogger.Msg("[NatoERA] GameReady fired, letting the scene settle before fitting.");

            // The whole pass sits in try/finally. If this enumerator is disposed mid-flight
            // (scene change, StateController teardown) the in-flight flag must not stay stuck,
            // or no later mission would ever be fitted again. yield return inside try/finally
            // is legal; yield inside try/catch is not, which is why the two passes below still
            // go through RunGuarded instead.
            try
            {
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

                // The brick textures are looked up in memory (Resources.FindObjectsOfTypeAll), and
                // the first pass can run before the terrain material that owns them has loaded.
                // Clearing the cached miss makes each mission try again instead of staying
                // flat-colored for the rest of the session.
                EraBuilder.ResetTextureCache();

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
            finally
            {
                _fitting = false;
            }
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