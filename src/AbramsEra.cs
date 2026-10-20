using System;
using System.Collections.Generic;
using System.Linq;
using GHPC;
using GHPC.State;
using GHPC.Utility;
using GHPC.Vehicle;
using MelonLoader;
using UnityEngine;

namespace NatoEra
{
    /// <summary>
    /// North American tank ARAT (Abrams Reactive Armor Tile) integration layer.
    ///
    /// Covers two vehicles:
    ///   - M1IP Abrams: turret uses abrams_m1ip_turret_arat, hull uses abrams_hull_arat
    ///   - M1 Abrams  : turret uses abrams_m1_turret_arat, hull uses abrams_hull_arat
    ///
    /// The armor plates are found in reverse via the tag "Penetrable": the game tags every surface
    /// that can be penetrated with it, and we then pick out the M1/M1IP turret and hull armor
    /// by name and by the parent unit name.
    /// </summary>
    public static class AbramsEra
    {
        // ---- bundle and asset names (case-insensitive, kept from the old version) ----
        private const string BundleHull     = "abrams_hull_arat";
        private const string BundleTurretM1 = "abrams_m1_turret_arat";
        private const string BundleTurretIp = "abrams_m1ip_turret_arat";

        private const string AssetHull     = "Hull ERA Array.prefab";
        private const string AssetTurretM1 = "Turret ERA Array.prefab";
        private const string AssetTurretIp = "Turret ERA Array M1IP.prefab";

        // ---- prefab instance placement ----
        // The rotation is the same for the hull and the turret (verbatim from the original
        // author's M1Abrams.cs), but the offset is NOT: the original source only had one value
        // because it attached both arrays at local (0, 0.9817, -0.69), measured against the
        // vanilla M1IP skeleton. The new build moved the turret armor under a different node,
        // so the turret array needs its own offset.
        //
        // Values below were measured live through DSHBridge on the new build and confirmed
        // visually; they align the array bounding box with the native armor geometry:
        //   hull   : HULLARMOR local, hull side plates        -> (0, 0.9817, -0.69)
        //   turret : M1A0_turret_armour local, outer plates   -> (-0.0093, 0.9633, -0.9713)
        // Both arrays use the author's (0, 270, 0) yaw.
        private static readonly Vector3 HullArrayLocalPosition   = new Vector3(0f, 0.9817f, -0.69f);
        private static readonly Vector3 TurretArrayLocalPosition = new Vector3(-0.0093f, 0.9633f, -0.9713f);
        private static readonly Vector3 ArrayLocalEuler          = new Vector3(0f, 270f, 0f);

        // ---- shared codex ----
        private static ArmorCodexScriptable _codex;
        private static GHPC.Equipment.ArmorType _armorType;

        // ---- cache of instantiated prefabs ----
        private static GameObject _hullPrefab;
        private static GameObject _turretPrefabM1;
        private static GameObject _turretPrefabIp;

        /// <summary>Names of the hull armor plates on M1 series vehicles that receive ARAT.</summary>
        private static readonly string[] HullArmorNames = { "HULLARMOR" };

        /// <summary>
        /// Names of the turret armor plates on the M1 series (M1IP and M1 use different names).
        ///
        /// The new build renamed the M1IP node from "Turret_Armor" to "Turret_Armor (partial)".
        /// Both spellings are accepted because either may be present depending on the model, but
        /// note that on the shared "IPM1_rig" skeleton both of them are empty containers: the
        /// armor plates themselves hang deeper under "AARTURRET" and carry VariableArmor, not
        /// UniformArmor. The array still belongs here - it is the node that rides along with the
        /// turret and its offset was measured against it - so no component check is made.
        /// </summary>
        private static readonly string[] TurretArmorIp = { "Turret_Armor", "Turret_Armor (partial)" };
        private static readonly string[] TurretArmorM1 = { "M1A0_turret_armour" };

        // =====================================================================
        // Init: only loads the bundles, does not touch the scene
        // =====================================================================

        public static void Init()
        {
            _hullPrefab     = EraBuilder.LoadEraPrefab(BundleHull,     AssetHull);
            _turretPrefabM1 = EraBuilder.LoadEraPrefab(BundleTurretM1, AssetTurretM1);
            _turretPrefabIp = EraBuilder.LoadEraPrefab(BundleTurretIp, AssetTurretIp);

            if (_hullPrefab == null || _turretPrefabM1 == null || _turretPrefabIp == null)
            {
                MelonLogger.Error("[NatoERA/ARAT] One or more AssetBundles failed to load; M1/M1IP ERA will be unavailable.");
            }
        }

        // =====================================================================
        // Convert: scans the whole scene after GameReady
        // =====================================================================

        /// <summary>
        /// Number of armor plates handled between two yields of the fitting coroutine.
        ///
        /// Fitting an array instantiates a whole prefab (a 152 node hull array, a 73 node
        /// turret array) and walks it several times. Doing every plate of every vehicle in
        /// one frame stalls the game state machine, which in turn starves the routines that
        /// spawn the rest of the scene. Handing the frame back every few plates keeps the
        /// main thread responsive without changing what gets fitted.
        /// </summary>
        private const int PlatesPerFrame = 8;

        /// <summary>
        /// Coroutine version of <see cref="Convert"/>. Same work, spread over several frames.
        ///
        /// Everything that has to be read from the scene (the penetrable tag scan) happens
        /// before the first yield, so the plate list is a stable snapshot. The per-vehicle
        /// fallback and the mass bookkeeping run after the last yield, once every plate has
        /// been handled.
        /// </summary>
        public static System.Collections.IEnumerator ConvertCoroutine(GameState state)
        {
            if (_hullPrefab == null || _turretPrefabM1 == null || _turretPrefabIp == null)
            {
                MelonLogger.Warning("[NatoERA/ARAT] prefabs not ready, skipping this conversion pass.");
                yield break;
            }

            // Two master switches: correspond to the original showERAm1 / showERAm1ip
            if (!Config.ShowEraM1 && !Config.ShowEraM1Ip)
            {
                MelonLogger.Msg("[NatoERA/ARAT] Both \"M1 ERA\" and \"M1IP ERA\" are false, skipping ARAT.");
                yield break;
            }

            GameObject[] penetrables;
            try
            {
                penetrables = GameObject.FindGameObjectsWithTag(EraBuilder.EraTag);
            }
            catch (Exception e)
            {
                MelonLogger.Error("[NatoERA/ARAT] Failed to find Penetrable objects: " + e.Message);
                yield break;
            }

            int hullCount = 0;
            int turretCount = 0;
            int sinceYield = 0;

            // Clear the bookkeeping from the previous pass (a second scan in the same scene must not double the mass)
            _pendingMass.Clear();
            _unitCache.Clear();

            foreach (var armorGo in penetrables)
            {
                if (armorGo == null) continue;

                // Already fitted, skip it
                if (armorGo.GetComponent<Util.AlreadyConvertedNERA>() != null) continue;

                var unit = GetParentUnit(armorGo.transform);
                if (unit == null) continue;

                string tag = unit.UniqueName ?? string.Empty;
                bool isM1Ip = tag.IndexOf("M1IP", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isM1   = !isM1Ip && tag.IndexOf("M1", StringComparison.OrdinalIgnoreCase) >= 0;

                if (!isM1Ip && !isM1) continue;

                // Per-vehicle master switch (original: if (showERAm1ip.Value) { ... } / if (showERAm1.Value) { ... })
                if (isM1Ip && !Config.ShowEraM1Ip) continue;
                if (isM1 && !Config.ShowEraM1) continue;

                if (NameMatches(armorGo.name, HullArmorNames))
                {
                    if (AttachAndRegister(armorGo, unit, _hullPrefab, HullArrayLocalPosition, ArrayLocalEuler,
                                          isM1Ip, isTurret: false))
                    {
                        hullCount++;
                    }

                    // Give the frame back after this batch of plates
                    if (++sinceYield >= PlatesPerFrame)
                    {
                        sinceYield = 0;
                        yield return null;
                    }
                    continue;
                }

                // Turret plates.
                //
                // The original author keyed the choice of turret prefab off UniqueName
                // ("M1IP" -> M1IP array, "M1" -> M1 array). That no longer works, because a
                // vehicle whose UniqueName is "M1" can carry the shared "IPM1_rig" skeleton
                // (AMP / CROWSFIX / M1A1 mods), and that skeleton only has the "M1A0_turret_armour"
                // node. Matching the array to the skeleton that is actually present keeps the
                // two in sync; the preference switches still decide whether anything happens.
                if (NameMatches(armorGo.name, TurretArmorIp))
                {
                    // Original: the M1IP turret array is gated by showERAm1ip_Turret.
                    bool on = isM1Ip ? Config.ShowEraM1Ip_Turret : Config.ShowEraM1_Turret;
                    if (!on) continue;

                    if (AttachAndRegister(armorGo, unit, _turretPrefabIp, TurretArrayLocalPosition, ArrayLocalEuler,
                                          isM1Ip: true, isTurret: true))
                    {
                        turretCount++;
                    }
                }
                else if (NameMatches(armorGo.name, TurretArmorM1))
                {
                    if (!Config.ShowEraM1_Turret) continue;

                    if (AttachAndRegister(armorGo, unit, _turretPrefabM1, TurretArrayLocalPosition, ArrayLocalEuler,
                                          isM1Ip: false, isTurret: true))
                    {
                        turretCount++;
                    }
                }

                if (++sinceYield >= PlatesPerFrame)
                {
                    sinceYield = 0;
                    yield return null;
                }
            }

            // =============================================================
            // Turret arrays for IPM1_rig based hulls (AMP / CROWSFIX / M1A1 mods)
            // =============================================================
            // Those mods rebuild the M1 around the "IPM1_rig" skeleton and strip every
            // UniformArmor plate off the turret: the whole vehicle keeps a single
            // "HULLARMOR" node. The name based scan above therefore never finds a turret
            // position on such a vehicle. The fallback below attaches the turret array to
            // the turret follow node itself, which is what the visual turret mesh hangs off.
            turretCount += AttachTurretToRigFollowNode();

            // Once the arrays are fitted, add the ballast back so the vehicle physics does not get lighter
            foreach (var kv in _unitCache)
            {
                var unit = kv.Value;
                if (unit == null) continue;

                bool isM1Ip = IsM1IpUnit(unit);
                _pendingMass[unit.GetInstanceID()] = ComputeVehicleMass(isM1Ip);
                ApplyMass(unit, isM1Ip);
            }
            MelonLogger.Msg("[NatoERA/ARAT] Scan result: " + hullCount + " hull positions, " + turretCount
                + " turret positions, on " + _pendingMass.Count + " vehicles.");
        }

        /// <summary>
        /// Blocking version of <see cref="ConvertCoroutine"/>, kept for callers that cannot
        /// yield (there are none at the moment, but it keeps the public surface unchanged).
        /// </summary>
        public static void Convert(GameState state)
        {
            var e = ConvertCoroutine(state);
            while (e.MoveNext())
            {
                // Drain the coroutine as fast as possible: a yield just means "next batch".
            }
        }

        // =====================================================================
        // Turret arrays for IPM1_rig based hulls
        // =====================================================================

        /// <summary>
        /// Fallback for vehicles whose turret carries no UniformArmor plate at all
        /// (the AMP / CROWSFIX / M1A1 mods rebuild the Abrams around "IPM1_rig").
        ///
        /// The original author relied on "Turret_Armor" / "M1A0_turret_armour" existing as
        /// armor plates on the vanilla M1 / M1IP. On the IPM1_rig skeleton those nodes are gone,
        /// but the turret follow node still exists and is exactly what the visual turret mesh
        /// and the gun are parented to, so the ERA array rides along with the turret.
        /// </summary>
        /// <returns>Number of turret arrays attached by this fallback</returns>
        private static int AttachTurretToRigFollowNode()
        {
            int attached = 0;

            // Iterate a snapshot: Convert() may add components while we walk the scene
            var units = new List<Unit>(_unitCache.Values);
            foreach (var unit in units)
            {
                if (unit == null) continue;

                bool isM1Ip = IsM1IpUnit(unit);
                if (isM1Ip && !Config.ShowEraM1Ip) continue;
                if (!isM1Ip && !Config.ShowEraM1) continue;

                // The turret array is its own preference (original: showERAm1ip_Turret / showERAm1_Turret)
                if (isM1Ip && !Config.ShowEraM1Ip_Turret) continue;
                if (!isM1Ip && !Config.ShowEraM1_Turret) continue;

                var prefab = isM1Ip ? _turretPrefabIp : _turretPrefabM1;
                if (prefab == null) continue;

                var follow = FindTurretFollow(unit.transform);
                if (follow == null)
                {
                    // Vanilla M1/M1IP keep a named armor plate; the name based scan already handled it.
                    continue;
                }

                // Skip when the name based scan already fitted this vehicle (vanilla path).
                if (HasTurretArray(unit.transform)) continue;

                // The armor plates live on a separate follow root, not under the Unit, so the
                // name based scan above cannot reach them when they are only empty containers.
                if (HasTurretArrayOnSkeleton(unit)) continue;

                // Prefer the real turret armor node: the placement offset was measured against
                // it, and it rides along with the turret just like the old armor plate did.
                // Fall back to the bare follow node for models without one.
                var anchor = FindTurretArmorNode(unit);
                if (anchor == null) anchor = FindTurretFollow(unit.transform);
                if (anchor == null)
                {
                    // Vanilla M1/M1IP keep a named armor plate; the name based scan already handled it.
                    continue;
                }

                if (!AddArray(anchor, prefab, TurretArrayLocalPosition, ArrayLocalEuler,
                              "ARAT-1", isM1Ip, isTurret: true, out _))
                {
                    continue;
                }

                // Remember on the follow node that we fitted it, so a second scan does not duplicate it.
                var marker = anchor;
                if (marker.GetComponent<Util.AlreadyConvertedNERA>() == null)
                {
                    marker.gameObject.AddComponent<Util.AlreadyConvertedNERA>();
                }
                if (marker.GetComponent<Util.HasARAT>() == null)
                {
                    marker.gameObject.AddComponent<Util.HasARAT>();
                }

                attached++;
                MelonLogger.Msg("[NatoERA/ARAT] " + unit.UniqueName
                    + ": turret array attached to " + anchor.name
                    + " (" + prefab.name + ").");
            }

            return attached;
        }

        /// <summary>
        /// Locates the turret follow node of an IPM1_rig based vehicle.
        /// Tries the known skeleton path first, then falls back to a name match.
        /// </summary>
        private static Transform FindTurretFollow(Transform vehicle)
        {
            if (vehicle == null) return null;

            Transform t = vehicle.Find("IPM1_rig/HULL/TURRET");
            if (t != null) return t;

            foreach (var c in vehicle.GetComponentsInChildren<Transform>(true))
            {
                if (c == null) continue;
                if (string.Equals(c.name, "TURRET", StringComparison.OrdinalIgnoreCase)
                    && c.parent != null
                    && string.Equals(c.parent.name, "HULL", StringComparison.OrdinalIgnoreCase))
                {
                    return c;
                }
            }
            return null;
        }

        /// <summary>True when a turret ERA array is already present anywhere under the vehicle.</summary>
        private static bool HasTurretArray(Transform vehicle)
        {
            if (vehicle == null) return false;
            foreach (var c in vehicle.GetComponentsInChildren<Transform>(true))
            {
                if (c == null) continue;
                if (string.Equals(c.name, "Turret ERA Array(Clone)", StringComparison.Ordinal)) return true;
                if (string.Equals(c.name, "Turret ERA Array M1IP(Clone)", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>
        /// True when any known turret armor node of this vehicle already carries the turret array.
        ///
        /// On the new build the turret plates hang off "M1IP TURRET FOLLOW", which is a follow
        /// root of its own and is not a child of the Unit, so walking the Unit subtree misses it.
        /// </summary>
        private static bool HasTurretArrayOnSkeleton(Unit unit)
        {
            if (unit == null) return false;

            var plate = FindTurretArmorNode(unit);
            if (plate == null) return false;

            return plate.Find("Turret ERA Array(Clone)") != null
                || plate.Find("Turret ERA Array M1IP(Clone)") != null;
        }

        /// <summary>
        /// Locates the turret armor node of a vehicle, following the follow skeleton when needed.
        /// Returns null for vehicles that have no known turret armor node at all.
        /// </summary>
        private static Transform FindTurretArmorNode(Unit unit)
        {
            if (unit == null) return null;

            // 1. Straight down the Unit hierarchy (vanilla models keep the plates under the Unit).
            var hit = FindChildByName(unit.transform, TurretArmorIp) ?? FindChildByName(unit.transform, TurretArmorM1);
            if (hit != null) return hit;

            // 2. The armor skeleton is a follow root: "M1IP TURRET FOLLOW" (M1IP and every
            //    IPM1_rig based mod) or "M1 TURRET FOLLOW" (vanilla M1).
            string[] followRoots = { "M1IP TURRET FOLLOW", "M1 TURRET FOLLOW", "TURRET FOLLOW" };
            foreach (var root in followRoots)
            {
                var follow = FindSceneTransform(root);
                if (follow == null) continue;

                hit = FindChildByName(follow, TurretArmorIp) ?? FindChildByName(follow, TurretArmorM1);
                if (hit != null) return hit;
            }

            return null;
        }

        /// <summary>First descendant whose name equals one of the candidates (case insensitive).</summary>
        private static Transform FindChildByName(Transform root, string[] names)
        {
            if (root == null || names == null || names.Length == 0) return null;

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                foreach (var n in names)
                {
                    if (string.Equals(t.name, n, StringComparison.OrdinalIgnoreCase)) return t;
                }
            }
            return null;
        }

        /// <summary>
        /// Finds a transform by name in the loaded scene, including inactive objects.
        /// The follow roots are not necessarily parented under the Unit, so a plain Find fails.
        /// </summary>
        private static Transform FindSceneTransform(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            foreach (var t in UnityEngine.Object.FindObjectsOfType<Transform>(true))
            {
                if (t == null) continue;
                if (string.Equals(t.name, name, StringComparison.Ordinal)) return t;
            }
            return null;
        }

        // =====================================================================
        // Attach the ERA array to an armor plate
        // =====================================================================

        /// <summary>
        /// Attaches one ERA array; on success it marks the armor plate and accumulates the mass for that vehicle.
        /// </summary>
        private static bool AttachAndRegister(GameObject armorGo, Unit unit, GameObject prefab,
                                              Vector3 localPos, Vector3 localEuler,
                                              bool isM1Ip, bool isTurret)
        {
            bool ok = AddArray(armorGo.transform, prefab, localPos, localEuler, "ARAT-1", isM1Ip, isTurret, out float massKg);
            if (!ok) return false;

            armorGo.AddComponent<Util.AlreadyConvertedNERA>();
            armorGo.AddComponent<Util.HasARAT>();

            RegisterUnit(unit, massKg);
            return true;
        }

        private static bool AddArray(Transform armorTransform, GameObject prefab,
                                     Vector3 localPos, Vector3 localEuler,
                                     string nameFilter, bool isM1Ip, bool isTurret,
                                     out float massKg)
        {
            massKg = 0f;
            if (armorTransform == null || prefab == null) return false;

            // A child with the same name already exists, so it has already been fitted
            if (armorTransform.Find(prefab.name + "(Clone)") != null) return false;

            GameObject instance;
            try
            {
                instance = UnityEngine.Object.Instantiate(prefab, armorTransform);
            }
            catch (Exception e)
            {
                MelonLogger.Error("[NatoERA/ARAT] Failed to instantiate ERA array on " + armorTransform.name + ": " + e.Message);
                return false;
            }

            instance.name = prefab.name + "(Clone)";
            instance.transform.localPosition = localPos;
            instance.transform.localEulerAngles = localEuler;
            instance.transform.localScale = Vector3.one;
            instance.SetActive(true);

            var spec = new EraBuilder.EraSpec
            {
                NameFilter  = nameFilter,
                DisplayName = "ARAT-1",
                HeatRha     = Config.AratHeatRha,
                SabotRha    = Config.AratSabotRha,
                Color       = Config.AratColor,
            };

            int n = EraBuilder.Setup(instance, spec, ref _codex, ref _armorType);

            // Trim the sub-arrays that are not wanted according to the preferences
            TrimSubArrays(instance, isM1Ip, isTurret);

            // Mass is settled once per vehicle (see ComputeVehicleMass); it is no longer summed per array here
            massKg = 0f;

            return n > 0;
        }

        /// <summary>
        /// Computes the fitted mass (kg) for the vehicle using the original author's weight table.
        ///
        /// The original author (M1Abrams.cs:181-217 / 300-340) used a "one settlement per vehicle" scheme:
        ///   the hull base mass is always added; every other item sits inside its own visibility sub-switch.
        /// This method folds the same rules into Config.TotalMassM1Ip / TotalMassM1 and adds the
        /// total once per vehicle, avoiding the error introduced by fragmented accumulation.
        /// </summary>
        private static float ComputeVehicleMass(bool isM1Ip)
        {
            return isM1Ip ? Config.TotalMassM1Ip : Config.TotalMassM1;
        }

        /// <summary>
        /// Deletes the ERA sub-arrays that are not needed according to the preferences.
        ///
        /// Sub-array names (verified):
        ///   Hull  : Plus Array / Lower Front Array / Upper Front Array
        ///   Turret: Side Plus Array / Cheek Array / Roof Array
        ///
        /// Mapping to the original switches:
        ///   Extended Hull ERA     -> Plus Array        (default false, the old version deletes it)
        ///   Hull Lower Front ERA  -> Lower Front Array
        ///   Hull Upper Front ERA  -> Upper Front Array
        ///   Extended Turret ERA   -> Side Plus Array
        ///   Turret Cheek ERA      -> Cheek Array
        ///   Turret Roof ERA       -> Roof Array
        /// </summary>
        private static void TrimSubArrays(GameObject arrayRoot, bool isM1Ip, bool isTurret)
        {
            bool keepPlus, keepLower, keepUpper, keepSidePlus, keepCheek, keepRoof;

            if (!isTurret)
            {
                keepPlus  = isM1Ip ? Config.ShowEraM1Ip_HullExtended   : Config.ShowEraM1_HullExtended;
                keepLower = isM1Ip ? Config.ShowEraM1Ip_HullLowerFront : Config.ShowEraM1_HullLowerFront;
                keepUpper = isM1Ip ? Config.ShowEraM1Ip_HullUpperFront : Config.ShowEraM1_HullUpperFront;
                keepSidePlus = keepCheek = keepRoof = true;  // the hull array has none of these
            }
            else
            {
                keepSidePlus = isM1Ip ? Config.ShowEraM1Ip_TurretExtended : Config.ShowEraM1_TurretExtended;
                keepCheek    = isM1Ip ? Config.ShowEraM1Ip_TurretCheek    : Config.ShowEraM1_TurretCheek;
                keepRoof     = isM1Ip ? Config.ShowEraM1Ip_TurretRoof     : Config.ShowEraM1_TurretRoof;
                keepPlus = keepLower = keepUpper = true;     // the turret array has none of these
            }

            var toRemove = new List<GameObject>();

            foreach (var t in arrayRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t.gameObject == arrayRoot) continue;
                string n = t.name;

                if (!keepPlus     && n == "Plus Array")        { toRemove.Add(t.gameObject); continue; }
                if (!keepLower    && n == "Lower Front Array") { toRemove.Add(t.gameObject); continue; }
                if (!keepUpper    && n == "Upper Front Array") { toRemove.Add(t.gameObject); continue; }
                if (!keepSidePlus && n == "Side Plus Array")   { toRemove.Add(t.gameObject); continue; }
                if (!keepCheek    && n == "Cheek Array")       { toRemove.Add(t.gameObject); continue; }
                if (!keepRoof     && n == "Roof Array")        { toRemove.Add(t.gameObject); continue; }
            }

            foreach (var go in toRemove)
            {
                if (go != null) UnityEngine.Object.Destroy(go);
            }

            if (toRemove.Count > 0)
            {
                MelonLogger.Msg("[NatoERA/ARAT] " + arrayRoot.name + ": removed "
                    + toRemove.Count + " sub-arrays according to the preferences.");
            }
        }

        // =====================================================================
        // Mass compensation
        // =====================================================================

        /// <summary>
        /// Fitted mass (kg) accumulated for each vehicle during this scan, keyed by the Unit instanceId.
        /// The value comes from ComputeArrayMass(), i.e. the original author's weight table.
        /// </summary>
        private static readonly Dictionary<int, float> _pendingMass = new Dictionary<int, float>();

        /// <summary>
        /// True when the vehicle should be treated as an M1IP.
        ///
        /// UniqueName alone is not enough on the new build. The M1A1Abrams and M1A1Abrams-CROWSFIX
        /// mods rewrite an M1 spawn into the M1IP model (M1toM1IP.cs patches UnitSpawner.SpawnUnit,
        /// calls LoadVanillaVehicle("M1") and passes uniqueName = "M1IP"), and the reverse also
        /// happens: a vehicle keeps the "M1" name while carrying the M1IP armor skeleton. The old
        /// port read the M1 switch set in those cases and silently dropped every optional array,
        /// so the skeleton that is actually present takes precedence over the name.
        ///
        /// The armor skeleton is NOT a child of the Unit: "M1IP TURRET FOLLOW" is a LateFollow
        /// root of its own (parent == null) that follows the Unit's turret, so both the name test
        /// and HasChildNamed() on unit.transform would miss it. Resolve it through LateFollow.
        /// </summary>
        private static bool IsM1IpUnit(Unit unit)
        {
            if (unit == null) return false;

            // 1. The name is still the cheapest and most reliable signal when it is explicit.
            string tag = unit.UniqueName ?? string.Empty;
            if (tag.IndexOf("M1IP", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            // 2. Fall through to the model. Only the M1IP skeleton carries these nodes; a vanilla
            //    M1 has "M1A0_turret_armour" and no "Turret_Armor (partial)".
            if (FindTurretArmorNode(unit) != null) return true;
            if (FindTurretFollowOfVehicle(unit) != null) return true;

            return false;
        }

        /// <summary>
        /// Resolves the turret follow root that belongs to this vehicle, by walking LateFollow
        /// targets. Returns null when no follow root follows this unit's turret.
        /// </summary>
        private static Transform FindTurretFollowOfVehicle(Unit unit)
        {
            if (unit == null) return null;

            // "M1IP TURRET FOLLOW" follows the turret node, so start from there.
            var turret = unit.transform.Find("IPM1_rig/HULL/TURRET");
            if (turret == null) turret = FindChildNamed(unit.transform, "TURRET");
            if (turret == null) return null;

            foreach (var t in UnityEngine.Object.FindObjectsOfType<Transform>(true))
            {
                if (t == null || t.parent != null) continue;     // follow roots are scene roots
                if (t.name.IndexOf("TURRET FOLLOW", StringComparison.OrdinalIgnoreCase) < 0) continue;

                var lf = t.GetComponent<GHPC.Utility.LateFollow>();
                if (lf == null) continue;

                Transform target = null;
                try { target = lf.FollowTarget; } catch { }
                if (target == turret) return t;
            }
            return null;
        }

        /// <summary>First descendant whose name matches (case insensitive), or null.</summary>
        private static Transform FindChildNamed(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name)) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                if (string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase)) return t;
            }
            return null;
        }

        private static void ApplyMass(Unit unit, bool isM1Ip)
        {
            try
            {
                if (unit == null) return;

                float extra;
                if (!_pendingMass.TryGetValue(unit.GetInstanceID(), out extra) || extra <= 0f) return;

                var rb = unit.GetComponent<Rigidbody>();
                if (rb == null)
                {
                    MelonLogger.Warning("[NatoERA/ARAT] " + unit.UniqueName + " has no Rigidbody, skipping mass compensation.");
                    return;
                }

                rb.mass += extra;
                MelonLogger.Msg("[NatoERA/ARAT] " + unit.UniqueName + (isM1Ip ? " (M1IP)" : " (M1)")
                    + " added " + extra.ToString("F0")
                    + " kg -> total mass " + rb.mass.ToString("F0") + " kg");
            }
            catch (Exception e)
            {
                MelonLogger.Error("[NatoERA/ARAT] Failed to set mass: " + e.Message);
            }
        }

        // =====================================================================
        // Utilities
        // =====================================================================

        private static bool NameMatches(string actual, string[] candidates)
        {
            if (string.IsNullOrEmpty(actual)) return false;
            foreach (var c in candidates)
            {
                if (string.Equals(actual, c, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static Unit GetParentUnit(Transform t)
        {
            try
            {
                return CodeUtils.GetParentUnitFollowAware(t);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static readonly Dictionary<int, Unit> _unitCache = new Dictionary<int, Unit>();

        /// <summary>Records a vehicle and accumulates the ERA mass fitted to it during this scan.</summary>
        private static void RegisterUnit(Unit unit, float massKg)
        {
            if (unit == null) return;

            int id = unit.GetInstanceID();
            _unitCache[id] = unit;

            float prev;
            if (_pendingMass.TryGetValue(id, out prev))
            {
                _pendingMass[id] = prev + massKg;
            }
            else
            {
                _pendingMass[id] = massKg;
            }
        }

        private static Unit FindUnitByInstanceId(int instanceId)
        {
            Unit u;
            if (_unitCache.TryGetValue(instanceId, out u)) return u;
            return null;
        }
    }
}