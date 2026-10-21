using System;
using System.Collections.Generic;
using GHPC;
using GHPC.Utility;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NatoEra
{
    /// <summary>
    /// Keeps ERA off vehicles another mod has rebuilt.
    ///
    /// [Why this exists]
    /// The M8-LOSAT mod (M8LOSATM2.dll) rewrites a stock M2 Bradley into an M8 AGS-like
    /// tank hunter. It does not know this mod exists - a string scan of its assembly finds
    /// no reference to "NatoERA", "ARAT", "BRAT" or any of our configuration keys - but its
    /// own setup expects to be the only thing touching that hull, and it breaks in several
    /// ways once our BRAT bricks are already on it:
    ///
    ///   1. M8PartitionArmor picks its template with
    ///        armor != null &amp;&amp; !armor.IsEra &amp;&amp; armor.ArmorType != null
    ///                     &amp;&amp; armor.GetComponent&lt;AarVisual&gt;() != null &amp;&amp; ...AarMaterial != null
    ///      "!IsEra" is the only thing keeping ERA bricks out, and our bricks live on
    ///      LAYER_INDEX_RAYCAST_ONLY with the same Unit, so they are candidates. If the scan
    ///      reaches a brick first and its MeshRenderer has no material, M8 throws
    ///      "Owned native partition armor/X-ray template unavailable." and rolls the whole
    ///      conversion back.
    ///   2. M8FittedArmorRig.Select() throws
    ///      "Ambiguous owned fitted armor: Hull ..." / "Turret ..." whenever two owned armor
    ///      components share a token prefix - which is exactly what our array re-parented
    ///      under the hull or turret looks like.
    ///   3. Both rigs (plus M8InteriorRig) capture RaycastColliderGroup.childColliders
    ///      snapshots and rewrite them on rollback, so colliders belonging to a mod it does
    ///      not know about get silently dropped.
    ///
    /// [What this does]
    /// Once a vehicle turns out to be M8-converted, every ERA component this mod fitted to it
    /// is removed again, and the vehicle is blacklisted so later passes skip it. Nothing of
    /// M8's is touched and no ammunition or armor value of ours is changed.
    ///
    /// [When it runs]
    /// M8 converts a candidate 1.5 s into the scene and the conversion itself is synchronous,
    /// so by the time this runs the markers are there. Two independent markers are accepted
    /// (see <see cref="IsM8Converted"/>), both anchored on things only M8 creates.
    /// </summary>
    public static class M8Guard
    {
        /// <summary>
        /// The one word every M8 marker contains. M8VehicleName writes "M8 LOSAT" into
        /// Unit._friendlyName, M8LosatModelFactory creates "M8_LOSAT_Runtime_Model", and the
        /// mod's own prefix is "[M8 LOSAT Fire Control] ", so a single substring test covers
        /// all of them. No stock GHPC vehicle and no other mod in this install uses it.
        /// </summary>
        private const string M8Marker = "LOSAT";

        /// <summary>Vehicles already cleared, keyed by instance id so a destroyed hull cannot leak the entry.</summary>
        private static readonly HashSet<int> _cleared = new HashSet<int>();

        /// <summary>
        /// True when this unit has been rebuilt into an M8 LOSAT.
        ///
        /// Two places to look, because M8 gets there in more than one step:
        ///   1. Unit._friendlyName - renamed to "M8 LOSAT" part way through the conversion.
        ///      Read through reflection because M8VehicleName writes the backing field of the
        ///      instance (the shared template keeps the old name).
        ///   2. The vehicle's own hierarchy - M8 parents "M8_LOSAT_Runtime_Model" under it
        ///      before the rename, so this one catches a conversion that is still in progress
        ///      or one that renamed the unit to something else afterwards.
        /// Either hit is conclusive: both strings come from M8 alone.
        /// </summary>
        public static bool IsM8Converted(Unit unit)
        {
            if (unit == null) return false;

            try
            {
                string name = ReadFriendlyName(unit);
                if (!string.IsNullOrEmpty(name) && name.IndexOf(M8Marker, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // A reflection miss must not hide the hierarchy test.
            }

            try
            {
                foreach (var t in unit.GetComponentsInChildren<Transform>(true))
                {
                    if (t == null) continue;
                    if (t.name.IndexOf(M8Marker, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }
            catch (Exception) { }

            return false;
        }

        /// <summary>
        /// Clears this mod's ERA off <paramref name="unit"/> when it is M8-converted.
        /// Safe to call before any fitting happened: nothing is found and only the
        /// bookkeeping entry is recorded.
        /// </summary>
        /// <returns>Number of ERA bricks removed (0 when the unit is not an M8).</returns>
        public static int ClearIfM8Converted(Unit unit)
        {
            if (unit == null) return 0;
            if (!IsM8Converted(unit)) return 0;

            int id = unit.GetInstanceID();
            if (_cleared.Contains(id)) return 0;

            int removed = 0;
            try
            {
                removed = RemoveOwnedEra(unit);
                _cleared.Add(id);
            }
            catch (Exception e)
            {
                // Not recording the id means the next pass retries instead of giving up silently.
                MelonLogger.Error("[NatoERA/M8Guard] Cleaning ERA off an M8-converted vehicle failed: " + e);
            }

            MelonLogger.Warning("[NatoERA/M8Guard] " + Describe(unit)
                + " is an M8-LOSAT conversion; skipped it and removed " + removed
                + " ERA brick(s) so the M8 setup is not disturbed.");
            return removed;
        }

        /// <summary>
        /// Unity compares destroyed objects against null, so a stale instance id can be dropped
        /// as soon as the entries are looked at again.
        /// </summary>
        public static void Prune()
        {
            if (_cleared.Count == 0) return;
            _cleared.RemoveWhere(IsDeadUnitId);
        }

        // =====================================================================
        // Removal
        // =====================================================================

        /// <summary>
        /// Removes every component of this mod from the unit.
        ///
        /// The easy half is the ownership: bricks live under "&lt;prefab&gt;(Clone)" roots that
        /// we created, and the marker components (HasARAT / HasBRAT / AlreadyConvertedNERA)
        /// sit on the unit or on a follow node. The harder half is M8 yourself-proofing: ERA
        /// arrays are also re-parented onto "BRADLEY HULL FOLLOW"/HULL or a turret armor node,
        /// so the search is widened to every UniformArmor whose Unit is this unit and that
        /// still carries our components.
        ///
        /// [Why DestroyImmediate and not Destroy]
        /// M8 captures its RaycastColliderGroup snapshots during the same frame. A deferred
        /// Destroy would leave our colliders in the group while M8 takes its snapshot, and they
        /// would come back on rollback. DestroyImmediate unregisters the brick before that.
        /// </summary>
        private static int RemoveOwnedEra(Unit unit)
        {
            int removed = 0;
            var roots = new List<GameObject>();

            // 1. Instantiated array roots that hang off the unit's own hierarchy.
            foreach (var t in unit.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                if (!IsEraArrayRoot(t.gameObject)) continue;
                if (roots.Contains(t.gameObject)) continue;
                roots.Add(t.gameObject);
            }

            // 2. Array roots that were re-parented away from the unit (follow nodes).
            try
            {
                foreach (var armor in Resources.FindObjectsOfTypeAll<UniformArmor>())
                {
                    if (armor == null || armor.gameObject == null) continue;
                    if (!armor.gameObject.scene.IsValid()) continue;
                    if (!ReferenceEquals(armor.Unit, unit)) continue;
                    if (!IsOurBrick(armor)) continue;

                    var root = FindArrayRoot(armor.transform);
                    if (root == null) continue;
                    if (roots.Contains(root.gameObject)) continue;
                    roots.Add(root.gameObject);
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[NatoERA/M8Guard] Scanning armour for ERA bricks failed: " + e.Message);
            }

            // 3. Drop the bricks, then the now empty array roots, then the markers.
            foreach (var root in roots)
            {
                if (root == null) continue;
                try
                {
                    removed += CountBricks(root.transform);
                    Object.DestroyImmediate(root);
                }
                catch (Exception e)
                {
                    MelonLogger.Warning("[NatoERA/M8Guard] Could not remove ERA array " + root.name + ": " + e.Message);
                }
            }

            RemoveMarkers(unit);
            return removed;
        }

        private static void RemoveMarkers(Unit unit)
        {
            var targets = new List<GameObject> { unit.gameObject };

            // The turret fallback puts its markers on the anchor node, not on the Unit.
            foreach (var t in unit.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                if (t.GetComponent<Util.AlreadyConvertedNERA>() != null) targets.Add(t.gameObject);
            }

            foreach (var go in targets)
            {
                if (go == null) continue;
                DestroyComponent<Util.AlreadyConvertedNERA>(go);
                DestroyComponent<Util.HasARAT>(go);
                DestroyComponent<Util.HasBRAT>(go);
            }
        }

        private static void DestroyComponent<T>(GameObject go) where T : Component
        {
            try
            {
                var c = go.GetComponent<T>();
                if (c != null) Object.DestroyImmediate(c);
            }
            catch (Exception) { }
        }

        // =====================================================================
        // Ownership tests
        // =====================================================================

        /// <summary>An array root is one of the "&lt;prefab&gt;(Clone)" objects Instantiate produced.</summary>
        private static bool IsEraArrayRoot(GameObject go)
        {
            if (go == null) return false;
            if (!go.name.EndsWith("(Clone)", StringComparison.Ordinal)) return false;
            return go.GetComponentInChildren<UniformArmor>(true) != null;
        }

        /// <summary>
        /// True for a brick this mod configured. Detection effect, ERA flag and codex are all
        /// details of the workflow; the ERA flag plus one of our markers is conclusive.
        /// </summary>
        private static bool IsOurBrick(UniformArmor armor)
        {
            if (armor == null) return false;
            if (!armor.IsEra) return false;
            return armor.GetComponent<Util.HasARAT>() != null
                || armor.GetComponent<Util.HasBRAT>() != null
                || armor.GetComponent<Util.AlreadyConvertedNERA>() != null
                || armor.name.StartsWith("ARAT-1", StringComparison.Ordinal)
                || armor.name.StartsWith("BRAT-M3", StringComparison.Ordinal)
                || armor.name.StartsWith("BRAT-M5", StringComparison.Ordinal);
        }

        /// <summary>
        /// Walks up from a brick to the array root that was instantiated from the bundle, so a
        /// whole array is removed at once instead of brick by brick.
        /// </summary>
        private static Transform FindArrayRoot(Transform brick)
        {
            if (brick == null) return null;

            Transform best = null;
            var t = brick;
            int guard = 0;
            while (t != null && guard++ < 64)
            {
                if (t.name.EndsWith("(Clone)", StringComparison.Ordinal)) best = t;
                t = t.parent;
            }
            return best ?? brick;
        }

        private static int CountBricks(Transform root)
        {
            if (root == null) return 0;
            return root.GetComponentsInChildren<UniformArmor>(true).Length;
        }

        // =====================================================================
        // Unit name / bookkeeping helpers
        // =====================================================================

        private static System.Reflection.FieldInfo _friendlyNameField;
        private static bool _friendlyNameResolved;

        /// <summary>
        /// Reads Unit._friendlyName directly. The public FriendlyName property is an auto
        /// property the game does not change at runtime, but M8VehicleName writes the backing
        /// field of the instance, so the field is the value that actually matters.
        /// </summary>
        private static string ReadFriendlyName(Unit unit)
        {
            if (!_friendlyNameResolved)
            {
                _friendlyNameResolved = true;
                _friendlyNameField = HarmonyLib.AccessTools.Field(typeof(Unit), "_friendlyName");
                if (_friendlyNameField == null)
                {
                    MelonLogger.Warning("[NatoERA/M8Guard] Unit._friendlyName not found, falling back to the public property.");
                }
            }

            if (_friendlyNameField != null)
            {
                var v = _friendlyNameField.GetValue(unit) as string;
                if (!string.IsNullOrEmpty(v)) return v;
            }

            return unit.FriendlyName;
        }

        private static bool IsDeadUnitId(int id)
        {
            var all = Object.FindObjectsOfType<Unit>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].GetInstanceID() == id) return false;
            }
            return true;
        }

        private static string Describe(Unit unit)
        {
            try
            {
                string un = unit.UniqueName;
                string fn = ReadFriendlyName(unit);
                return (string.IsNullOrEmpty(un) ? unit.name : un) + " (\"" + fn + "\")";
            }
            catch (Exception)
            {
                return unit.name;
            }
        }

        /// <summary>
        /// Diagnostic dump: every piece of our ERA still attached to a unit, used by the
        /// post-fit verification so the log shows whether anything slipped through.
        /// </summary>
        public static int CountOwnedEra(Unit unit)
        {
            if (unit == null) return 0;
            int n = 0;
            try
            {
                foreach (var armor in Resources.FindObjectsOfTypeAll<UniformArmor>())
                {
                    if (armor == null || armor.gameObject == null) continue;
                    if (!armor.gameObject.scene.IsValid()) continue;
                    if (!ReferenceEquals(armor.Unit, unit)) continue;
                    if (!IsOurBrick(armor)) continue;
                    n++;
                }
            }
            catch (Exception) { }
            return n;
        }
    }
}
