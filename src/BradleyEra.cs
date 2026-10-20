using System;
using System.Collections.Generic;
using GHPC;
using GHPC.Utility;
using MelonLoader;
using UnityEngine;

namespace NatoEra
{
    /// <summary>
    /// M2 Bradley BRAT (Bradley Reactive Armor Tile) integration layer.
    ///
    /// Differences from ARAT:
    ///   - Hull only, no turret
    ///   - The hull armor plates are not in tag "Penetrable"; they live on the "HULL" node under the LateFollow hierarchy
    ///   - Two brick types: BRAT-M5 (thick) and BRAT-M3 (thin), with different protection values
    ///
    /// New hierarchy (verified against the prefab): Vehicle -> (LateFollow)"BRADLEY HULL FOLLOW" -> "HULL"
    /// </summary>
    public static class BradleyEra
    {
        private const string BundleHull = "m2_hull_brat";
        private const string AssetHull  = "M2 Hull ERA Array.prefab";

        /// <summary>
        /// FriendlyName of the M2 Bradley (verified_new version).
        ///
        /// Note: the game has two Bradley variants with different FriendlyNames --
        ///   M2 Bradley.prefab                        _friendlyName: M2 Bradley       _uniqueName: M2BRADLEY
        ///   M2 Bradley(AP heavy belt temp) Variant   _friendlyName: M2 Bradley(alt)  _uniqueName: M2BRADLEY(ALT)
        /// The stock campaigns GT01_Deep_Strike / GT03_CrossScreen / UMC_flank use the (alt) variant.
        /// The old mod compared for exact equality, which misses (alt); prefix matching is required here.
        /// </summary>
        private const string BradleyFriendlyName = "M2 Bradley";

        /// <summary>Name of the hull LateFollow node (verified_new version, unchanged).</summary>
        private const string HullFollowName = "BRADLEY HULL FOLLOW";

        /// <summary>Name of the hull root node under LateFollow.</summary>
        private const string HullNodeName = "HULL";

        /// <summary>
        /// Name of the armor rig node that owns the real hull "HULL" on this model.
        /// "BRADLEY HULL FOLLOW" only has a "Driver" child nowadays, the hull itself moved here.
        /// </summary>
        private const string ArmourRigName = "bradley_armour_rigged";

        /// <summary>Offset of the prefab instance relative to HULL (values measured on the old version).</summary>
        private static readonly Vector3 HullLocalPosition = new Vector3(0f, 0.269f, 0.112f);

        // =====================================================================
        // Hand tuned brick offsets for the addon plated hull (Blender session, 2026-10-21)
        //
        // IMPORTANT: these offsets apply ONLY to a Bradley that actually carries the
        // M2Extended addon armour package. A bare M2 Bradley must keep the placement the
        // original mod gave it, so ApplyAddonBrickOffsets is called from HandleM2Extended
        // and nowhere else.
        //
        // With the addon kit fitted the hull is 25.4 mm wider on each side, and the stock
        // prefab then leaves the BRAT bricks visibly detached from the vehicle. The values
        // below were dialled in by hand against the addon plated vehicle and confirmed
        // visually in game.
        //
        // They are expressed as a shift of each brick's localPosition rather than a shift
        // of the array root, because:
        //   1. the two sides move in opposite directions, so a single root offset cannot
        //      express it;
        //   2. the sub-arrays are separate child nodes with their own local frames, and
        //      the mirrors built by Blender are not symmetric (see the -1.6973 vs 1.6980
        //      asymmetry that ships in the prefab).
        // =====================================================================

        /// <summary>
        /// How far each hull side brick is pulled towards the hull, in metres.
        /// Applied to every brick whose localPosition.x is clearly off centre
        /// (|x| &gt; <see cref="SideBrickCentreDeadZone"/>), pushing it inwards along X.
        /// </summary>
        private const float SideBrickInset = 0.15f;

        /// <summary>
        /// Bricks closer to the centre line than this are left untouched by
        /// <see cref="SideBrickInset"/>. They are the inner columns of the glacis arrays,
        /// which sit on the front plate and must not be pulled inwards.
        /// </summary>
        private const float SideBrickCentreDeadZone = 0.5f;

        /// <summary>
        /// The sub-arrays whose bricks are mounted on the hull sides, i.e. the only ones
        /// that receive <see cref="SideBrickInset"/>. The glacis arrays
        /// ("Front Upper Array", "Front Headlight Array", "Front Lower Array") are
        /// deliberately absent, as is "Hull Upper Array" (handled per brick through
        /// <see cref="RearCornerBricks"/>).
        /// </summary>
        private static readonly string[] SideBrickArrays =
        {
            "Side Lower Array",
            "Side Lower Extended Array",
            "Side Extended Array",
        };

        /// <summary>Sub-array that holds the rear corner bricks.</summary>
        private const string RearCornerArrayName = "Hull Upper Array";

        /// <summary>
        /// The five BRAT-M3 bricks at the left rear corner of "Hull Upper Array"
        /// needed a small three axis nudge on top of the side inset, applied in the
        /// frame of "Hull Upper Array" (which is an identity transform, so the
        /// values are directly comparable with the recorded Blender data).
        /// </summary>
        private static readonly Vector3 RearCornerBrickOffset = new Vector3(0.07669f, -0.09353f, 0.01212f);

        /// <summary>Names of the bricks that receive <see cref="RearCornerBrickOffset"/>.</summary>
        private static readonly string[] RearCornerBricks =
        {
            "BRAT-M3 (85)", "BRAT-M3 (86)", "BRAT-M3 (87)", "BRAT-M3 (88)", "BRAT-M3 (89)",
        };

        private static ArmorCodexScriptable _codexM3;
        private static GHPC.Equipment.ArmorType _armorM3;
        private static ArmorCodexScriptable _codexM5;
        private static GHPC.Equipment.ArmorType _armorM5;

        private static GameObject _hullPrefab;

        public static void Init()
        {
            _hullPrefab = EraBuilder.LoadEraPrefab(BundleHull, AssetHull);

            if (_hullPrefab == null)
            {
                MelonLogger.Error("[NatoERA/BRAT] m2_hull_brat failed to load; M2 Bradley ERA will be unavailable.");
            }
        }

        /// <summary>
        /// Number of Bradley hulls handled between two yields of the fitting coroutine.
        ///
        /// Fitting one BRAT array instantiates a 55 node prefab and then walks it a few
        /// times (trim, M2Extended overlay, brick offsets). Spreading the vehicles over
        /// frames keeps the game state machine responsive while the scene is still spawning.
        /// </summary>
        private const int VehiclesPerFrame = 1;

        /// <summary>
        /// Coroutine version of <see cref="Convert"/>. Same work, spread over several frames.
        /// </summary>
        public static System.Collections.IEnumerator ConvertCoroutine(GHPC.State.GameState state)
        {
            if (_hullPrefab == null)
            {
                MelonLogger.Warning("[NatoERA/BRAT] prefab not ready, skipping this conversion pass.");
                yield break;
            }

            // Per-vehicle master switch (original showERAm2)
            if (!Config.ShowEraM2)
            {
                MelonLogger.Msg("[NatoERA/BRAT] \"M2 ERA\" is false, skipping BRAT.");
                yield break;
            }

            int count = 0;
            int sinceYield = 0;

            foreach (var unit in UnityEngine.Object.FindObjectsOfType<Unit>())
            {
                if (unit == null) continue;
                if (!IsBradley(unit)) continue;

                if (unit.GetComponent<Util.AlreadyConvertedNERA>() != null) continue;

                var hull = FindHull(unit.transform);
                if (hull == null)
                {
                    MelonLogger.Warning("[NatoERA/BRAT] " + unit.name + " : cannot find " + HullFollowName + "/" + HullNodeName);
                    continue;
                }

                if (hull.Find(_hullPrefab.name + "(Clone)") != null)
                {
                    unit.gameObject.AddComponent<Util.AlreadyConvertedNERA>();
                    continue;
                }

                if (Attach(hull))
                {
                    unit.gameObject.AddComponent<Util.AlreadyConvertedNERA>();
                    unit.gameObject.AddComponent<Util.HasBRAT>();
                    ApplyMass(unit);
                    count++;
                }

                if (++sinceYield >= VehiclesPerFrame)
                {
                    sinceYield = 0;
                    yield return null;
                }
            }

            MelonLogger.Msg("[NatoERA/BRAT] Scan result: fitted BRAT to " + count + " M2 Bradley vehicles.");
        }

        /// <summary>
        /// Blocking version of <see cref="ConvertCoroutine"/>, kept for callers that cannot yield.
        /// </summary>
        public static void Convert(GHPC.State.GameState state)
        {
            var e = ConvertCoroutine(state);
            while (e.MoveNext())
            {
                // Drain the coroutine as fast as possible: a yield just means "next vehicle".
            }
        }

        // =====================================================================
        // Attach
        // =====================================================================

        private static bool Attach(Transform hull)
        {
            GameObject instance;
            try
            {
                instance = UnityEngine.Object.Instantiate(_hullPrefab, hull);
            }
            catch (Exception e)
            {
                MelonLogger.Error("[NatoERA/BRAT] Instantiation failed: " + e.Message);
                return false;
            }

            instance.name = _hullPrefab.name + "(Clone)";
            instance.transform.localPosition = HullLocalPosition;
            instance.transform.localEulerAngles = Vector3.zero;
            instance.transform.localScale = Vector3.one;
            instance.SetActive(true);

            // The two brick types are configured separately, with different protection values
            var specM5 = new EraBuilder.EraSpec
            {
                NameFilter  = "BRAT-M5",
                DisplayName = "BRAT-M5",
                HeatRha     = Config.BratM5HeatRha,
                SabotRha    = Config.BratM5SabotRha,
                Color       = Config.BratColor,
            };

            var specM3 = new EraBuilder.EraSpec
            {
                NameFilter  = "BRAT-M3",
                DisplayName = "BRAT-M3",
                HeatRha     = Config.BratM3HeatRha,
                SabotRha    = Config.BratM3SabotRha,
                Color       = Config.BratColor,
            };

            // Watch the order: M5 first, then M3.
            // "BRAT-M5" does not contain "BRAT-M3" and "BRAT-M3" does not contain "BRAT-M5", so the two do not interfere,
            // but the bundle also has a "BRAT-M3 " with a trailing space, which still matches via Contains.
            int n5 = EraBuilder.Setup(instance, specM5, ref _codexM5, ref _armorM5);
            int n3 = EraBuilder.Setup(instance, specM3, ref _codexM3, ref _armorM3);

            TrimSubArrays(instance);
            HandleM2Extended(instance, hull);

            // Same as the original author: weigh the sub-arrays as a whole (always 0 when Feather BRAT is on)
            _lastMassKg = ComputeBradleyMass();

            return (n5 + n3) > 0;
        }

        // =====================================================================
        // Identification
        // =====================================================================

        /// <summary>
        /// Tests whether a unit is a Bradley (including the (alt) variant).
        /// Prefix matching rather than equality is required to cover "M2 Bradley(alt)".
        /// </summary>
        private static bool IsBradley(Unit unit)
        {
            if (unit == null) return false;

            string fn = unit.FriendlyName;
            if (!string.IsNullOrEmpty(fn) &&
                fn.StartsWith(BradleyFriendlyName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Fallback: also accept UniqueName ("M2BRADLEY" / "M2BRADLEY(ALT)")
            string un = unit.UniqueName;
            if (!string.IsNullOrEmpty(un) &&
                un.StartsWith("M2BRADLEY", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        /// <summary>Total ERA mass (kg) fitted by the last Attach.</summary>
        private static float _lastMassKg;

        // =====================================================================
        // M2Extended compatibility
        // =====================================================================

        /// <summary>
        /// Handles the spatial relationship between BRAT and the M2Extended addon armor.
        ///
        /// [Why this is needed]
        /// The M2Extended "Addon Armour Package" fits the M2A2 with a full set of 25.4mm steel addon armor plates,
        /// occupying exactly the hull side/front positions used by BRAT. Stacking both in place would put two
        /// armor sets in the same area, physically intersecting and double-counting the penetration calculation.
        ///
        /// [What this does]
        /// The real BRAT is mounted outside the M2A2 addon armor, so the correct approach is to shift the BRAT
        /// side bricks laterally outwards along the hull so that the order hull -> 25.4mm addon steel plate ->
        /// BRAT ERA holds. On top of that the bricks are pulled back towards the hull by
        /// <see cref="ApplyAddonBrickOffsets"/> so the kit hugs the wider addon plated hull instead of floating
        /// 0.166 m off it.
        ///
        /// The outward displacement is not hardcoded: it measures the world bounds of the addon armor parts
        /// fitted by M2Extended itself, takes how far they extend past the inner face of the bricks on the
        /// same side of the array, and adds a clearance value.
        ///
        /// When M2Extended is absent this method does nothing at all, so a plain M2 Bradley keeps exactly the
        /// placement the original mod gave it -- the hand tuned offsets recorded in the Blender session are
        /// only meaningful for the addon plated vehicle and MUST NOT be applied to a bare M2.
        /// </summary>
        private static void HandleM2Extended(GameObject arrayRoot, Transform hull)
        {
            if (!Config.EnableM2ExtendedOverlay)
            {
                MelonLogger.Msg("[NatoERA/BRAT] M2Extended overlay disabled (NatoEra M2Extended Overlay = false), BRAT fitted as-is.");
                return;
            }

            var addon = FindM2ExtendedAddonArmor(hull);
            if (addon.Count == 0)
            {
                MelonLogger.Msg("[NatoERA/BRAT] No M2Extended addon armor detected, BRAT keeps the stock placement.");
                return;
            }

            MelonLogger.Msg("[NatoERA/BRAT] Detected " + addon.Count + " M2Extended addon armor parts.");
            OverlayOutsideAddonArmor(arrayRoot, addon);

            // Only an addon plated hull is wide enough to need the hand tuned brick offsets.
            ApplyAddonBrickOffsets(arrayRoot);
        }

        /// <summary>
        /// Overlay: shift the BRAT hull side arrays laterally outwards along the hull so they sit outside the M2Extended addon armor.
        ///
        /// [Why the displacement cannot be hardcoded]
        /// M2Extended builds the kit with Object.Instantiate and then calls SetParent(..., true)
        /// (worldPositionStays = true) on each of the kit's child nodes in turn; the kit root's localEulerAngles
        /// are (0,90,90). The final world-space orientation of each item depends on the orientation of the
        /// LateFollow node it was attached to at that moment and cannot be fully reproduced offline. So no axis
        /// is assumed here; everything is measured at runtime from the "world bounds".
        ///
        /// [Algorithm]
        ///   1. Find the combined bounds of the addon armor parts in the local space of the "BRAT array root"
        ///   2. Find the combined bounds of the BRAT side bricks
        ///   3. Walk the three axes and find the one where "BRAT span is small, addon armor span is large" -- the hull lateral
        ///      (the BRAT side bricks only cover the +/-1.8 m hull width, while the addon armor plates span the whole length/height)
        ///   4. On the lateral axis, compute how far each side needs to move out
        ///   5. Apply it to the whole array root so all sub-arrays move out together
        /// </summary>
        private static void OverlayOutsideAddonArmor(GameObject arrayRoot, List<UniformArmor> addon)
        {
            float gap = Config.M2ExtendedOverlayGap;
            var space = arrayRoot.transform;

            // ---- 1. Addon armor bounds (in the array root space) ----
            Bounds addonBounds;
            if (!TryGetCombinedBounds(space, ToTransforms(addon), out addonBounds))
            {
                MelonLogger.Warning("[NatoERA/BRAT] Cannot measure the addon armor bounds, overlay skipped (BRAT stays in place).");
                return;
            }

            // ---- 2. BRAT side bricks: left and right sides must be measured separately ----
            // Merging them into one bounds would treat the "outer edge of the left bricks" as the "inner edge of the right bricks",
            // giving an absurd displacement (measured: it pushed BRAT out to +/-3.3 m).
            Bounds bratRight = new Bounds();
            Bounds bratLeft  = new Bounds();
            var bricksRight = CollectSideBricks(space, +1);
            var bricksLeft  = CollectSideBricks(space, -1);

            if (bricksRight.Count == 0 && bricksLeft.Count == 0)
            {
                MelonLogger.Msg("[NatoERA/BRAT] No hull side BRAT bricks (probably disabled by the preferences), no overlay needed.");
                return;
            }

            bool hasRight = bricksRight.Count > 0 && TryGetCombinedBounds(space, bricksRight, out bratRight);
            bool hasLeft  = bricksLeft.Count  > 0 && TryGetCombinedBounds(space, bricksLeft,  out bratLeft);

            if (!hasRight && !hasLeft)
            {
                MelonLogger.Warning("[NatoERA/BRAT] Cannot measure the BRAT side bounds, overlay skipped (BRAT stays in place).");
                return;
            }

            // ---- 3. Determine which axis is the hull lateral axis (the narrower single side is more reliable) ----
            Bounds refBrat = hasRight ? bratRight : bratLeft;
            int lateralAxis = PickLateralAxis(refBrat);
            MelonLogger.Msg("[NatoERA/BRAT] Hull lateral axis = " + AxisName(lateralAxis)
                + " (single-side brick size=(" + refBrat.size.x.ToString("F2") + ", "
                + refBrat.size.y.ToString("F2") + ", " + refBrat.size.z.ToString("F2") + "))");

            // ---- 4. Compute the displacement of each side separately ----
            float shiftPos = hasRight ? ComputeSideShift(lateralAxis, +1, addonBounds, bratRight, gap) : 0f;
            float shiftNeg = hasLeft  ? ComputeSideShift(lateralAxis, -1, addonBounds, bratLeft,  gap) : 0f;

            float shift;
            if (shiftPos > 0f && shiftNeg > 0f) shift = Mathf.Min(shiftPos, shiftNeg);
            else if (shiftPos > 0f) shift = shiftPos;
            else if (shiftNeg > 0f) shift = shiftNeg;
            else
            {
                MelonLogger.Msg("[NatoERA/BRAT] The addon armor and the BRAT side bricks need no outward shift (BRAT is already outermost or does not overlap).");
                return;
            }

            // ---- Safety valve ----
            // A normal addon armor plate is only a few tens of millimetres thick and a BRAT brick is about 0.23 m,
            // so a sane outward shift never exceeds 0.5 m. Anything larger means the measurement premises
            // (orientation / reference frame) are wrong; better not to shift at all than to widen the vehicle to 6 m.
            const float MaxSaneShift = 0.5f;
            if (shift > MaxSaneShift)
            {
                MelonLogger.Warning(string.Format(
                    "[NatoERA/BRAT] Computed outward shift {0:F3} m exceeds the sane limit {1:F1} m; treating it as a measurement error,"
                    + " giving up the overlay (BRAT stays in place).",
                    shift, MaxSaneShift));
                return;
            }

            var p = space.localPosition;
            if (lateralAxis == 0) p.x += shift;
            else if (lateralAxis == 1) p.y += shift;
            else p.z += shift;
            space.localPosition = p;

            MelonLogger.Msg("[NatoERA/BRAT] Overlay done: BRAT moved out along the " + AxisName(lateralAxis) + " axis by "
                + shift.ToString("F3") + " m (gap " + gap.ToString("F3") + " m), "
                + "armor order hull -> M2A2 addon steel plate -> BRAT ERA.");
        }

        /// <summary>
        /// Collects the hull side BRAT bricks, split into left and right.
        /// <paramref name="sign"/> = +1 takes the X&gt;0 half, -1 takes the X&lt;0 half.
        /// The position of a single brick can be judged from the transform origin of its MeshFilter (the bricks are small).
        /// </summary>
        private static List<Transform> CollectSideBricks(Transform space, int sign)
        {
            var result = new List<Transform>();
            foreach (var name in new[] { "Side Lower Array", "Side Lower Extended Array", "Side Extended Array" })
            {
                var t = space.Find(name);
                if (t == null) continue;
                foreach (var mf in t.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf == null || mf.sharedMesh == null) continue;
                    // Use the rendered centre rather than the transform origin: the brick meshes are not
                    // necessarily centred on their transform, and transform origins of mirrored nodes lie.
                    var r = mf.GetComponent<Renderer>();
                    var world = (r != null) ? r.bounds.center : mf.transform.position;
                    var local = space.InverseTransformPoint(world);
                    if ((sign > 0 && local.x > 0f) || (sign < 0 && local.x < 0f)) result.Add(mf.transform);
                }
            }
            return result;
        }

        /// <summary>
        /// Determines which axis is the hull lateral axis.
        ///
        /// Basis: the size of the BRAT side bricks on one side (left or right only) is
        ///   lateral span  ~= 0.23 m (brick thickness, the thinnest)
        ///   vertical span ~= 1.17 m (two layers of bricks)
        ///   length span   ~= 5.49 m (laid out along the hull length, the longest)
        /// so the lateral axis = the one with the smallest span.
        ///
        /// Note: the decision uses "single-side bricks" rather than "left+right merged"; merged, the lateral span becomes
        /// 3.63 m (the vehicle width), is no longer the smallest, and the criterion no longer holds.
        /// </summary>
        private static int PickLateralAxis(Bounds singleSideBricks)
        {
            float sx = singleSideBricks.size.x, sy = singleSideBricks.size.y, sz = singleSideBricks.size.z;
            if (sx <= sy && sx <= sz) return 0;
            if (sy <= sx && sy <= sz) return 1;
            return 2;
        }

        private static string AxisName(int axis)
        {
            switch (axis)
            {
                case 0: return "X";
                case 1: return "Y";
                default: return "Z";
            }
        }

        /// <summary>Gets a component by axis.</summary>
        private static float Comp(Vector3 v, int axis)
        {
            return axis == 0 ? v.x : (axis == 1 ? v.y : v.z);
        }

        /// <summary>
        /// Computes how many metres one side (+1 = positive / -1 = negative) must move out along the given axis. Returns 0 when no shift is needed.
        ///
        /// The <paramref name="brat"/> passed in is the bounds of the **single side** bricks (excluding the other side),
        /// so bratInner is the inner edge of that side and can be compared directly.
        /// </summary>
        private static float ComputeSideShift(int axis, int sign, Bounds addon, Bounds brat, float gap)
        {
            // The other two axes must overlap, otherwise there is no addon armor on this side at all
            for (int a = 0; a < 3; a++)
            {
                if (a == axis) continue;
                float bMin = Comp(brat.min, a), bMax = Comp(brat.max, a);
                float aMin = Comp(addon.min, a), aMax = Comp(addon.max, a);
                if (!(bMin < aMax && bMax > aMin)) return 0f;
            }

            float addonOuter;   // the outward-facing edge of the addon armor
            float bratInner;    // the edge of the BRAT on the hull side
            float bratOuter;    // the outward-facing edge of the BRAT

            if (sign > 0)
            {
                addonOuter = Comp(addon.max, axis);
                bratInner  = Comp(brat.min, axis);
                bratOuter  = Comp(brat.max, axis);
                if (addonOuter <= 0f) return 0f;
            }
            else
            {
                addonOuter = Comp(addon.min, axis);
                bratInner  = Comp(brat.max, axis);
                bratOuter  = Comp(brat.min, axis);
                if (addonOuter >= 0f) return 0f;
            }

            // The outer edge of the addon armor does not pass the inner edge of the BRAT -> BRAT is already outermost, no action
            bool addonInsideBrat = sign > 0 ? (addonOuter <= bratInner) : (addonOuter >= bratInner);
            if (addonInsideBrat)
            {
                MelonLogger.Msg(string.Format(
                    "[NatoERA/BRAT] {0} side: addon armor outer edge {1:F3} m does not pass the BRAT inner edge {2:F3} m, BRAT is already outside, no shift needed.",
                    sign > 0 ? "positive" : "negative", addonOuter, bratInner));
                return 0f;
            }

            // The addon armor sticks out past the BRAT -> push the whole BRAT outside the addon armor outer edge
            float need = Mathf.Abs(addonOuter - bratInner) + gap;
            MelonLogger.Msg(string.Format(
                "[NatoERA/BRAT] {0} side: addon armor outer edge {1:F3} m, BRAT inner edge {2:F3} m / outer edge {3:F3} m, shift out {4:F3} m.",
                sign > 0 ? "positive" : "negative", addonOuter, bratInner, bratOuter, need));
            return need;
        }

        /// <summary>
        /// Computes the combined bounds of a set of Transforms' meshes in the <paramref name="space"/> coordinate system.
        ///
        /// Renderer.bounds (the world space axis aligned box of the rendered geometry) is used on purpose.
        /// The M2Extended addon plates carry the kind of transform the stock Bradley armor plates use:
        /// a 1/100 scale mesh with localScale around (+-100, +-100, +-100), often with negative (mirrored) values,
        /// for example "middle side" has localScale (-100, -100, -100) and a mesh bounds of 0.03 x 0.01 x 0.00.
        /// Going through MeshFilter.sharedMesh.bounds and TransformPoint loses that information: measured in the
        /// BRAT array space, the mesh path reports X[-1.51, 1.51] for the whole addon set, while the rendered
        /// geometry really spans X[-4.29, 4.29]. That 2.8x error is exactly what made the overlay conclude
        /// "BRAT is already outermost" and skip the shift. Renderer.bounds always matches what is on screen.
        /// </summary>
        private static bool TryGetCombinedBounds(Transform space, List<Transform> items, out Bounds result)
        {
            result = new Bounds(Vector3.zero, Vector3.zero);
            bool any = false;
            Vector3 min = Vector3.zero, max = Vector3.zero;

            foreach (var t in items)
            {
                if (t == null) continue;

                // Prefer the renderer bounds of this node and, failing that, of its children.
                Bounds b;
                var r = t.GetComponent<Renderer>();
                if (r != null)
                {
                    b = r.bounds;
                }
                else
                {
                    var children = t.GetComponentsInChildren<Renderer>(true);
                    if (children == null || children.Length == 0) continue;
                    b = children[0].bounds;
                    for (int c = 1; c < children.Length; c++)
                    {
                        if (children[c] != null) b.Encapsulate(children[c].bounds);
                    }
                }

                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + new Vector3(
                        (i & 1) == 0 ? -b.extents.x : b.extents.x,
                        (i & 2) == 0 ? -b.extents.y : b.extents.y,
                        (i & 4) == 0 ? -b.extents.z : b.extents.z);

                    // world point -> local space of space
                    var p = space.InverseTransformPoint(corner);

                    if (!any) { min = max = p; any = true; }
                    else { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
                }
            }

            if (!any) return false;
            result = new Bounds((min + max) * 0.5f, max - min);
            return true;
        }

        private static List<Transform> ToTransforms(List<UniformArmor> list)
        {
            var r = new List<Transform>(list.Count);
            foreach (var ua in list) if (ua != null) r.Add(ua.transform);
            return r;
        }

        /// <summary>
        /// Finds the addon armor parts fitted by M2Extended.
        ///
        /// Only the armor parts named by M2Extended itself ("addon upper glacis plate" etc.) are accepted,
        /// which excludes the stock Bradley armor plates (those are named "UnknownMaterialAndThickness ..."
        /// and the like), and also excludes this mod's own BRAT/ARAT.
        /// </summary>
        private static List<UniformArmor> FindM2ExtendedAddonArmor(Transform hull)
        {
            var result = new List<UniformArmor>();
            if (hull == null) return result;

            var armorMarkers = new[]
            {
                "addon upper glacis plate",
                "addon lower glacis plate",
                "addon side plate",
                "addon turret collar",
                "addon hull side plate",
                "addon turret plate",
                "addon mantlet plate",
                "mounting bracket",
            };

            try
            {
                // The addon armor parts are SetParent'ed under their own LateFollow roots (not under HULL),
                // so search the whole vehicle rather than just the HULL subtree.
                var vehicleRoot = hull;
                while (vehicleRoot.parent != null) vehicleRoot = vehicleRoot.parent;

                foreach (var armor in vehicleRoot.GetComponentsInChildren<UniformArmor>(true))
                {
                    if (armor == null) continue;
                    string n = armor.Name;
                    if (string.IsNullOrEmpty(n)) continue;

                    foreach (var marker in armorMarkers)
                    {
                        if (n.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            result.Add(armor);
                            break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[NatoERA/BRAT] Error while detecting the M2Extended addon armor (treating it as not installed): " + e.Message);
            }

            return result;
        }

        /// <summary>
        /// Deletes the sub-arrays that are not needed according to the preferences.
        /// Sub-array names in the new bundle (verified): Side Lower Array / Side Lower Extended Array /
        /// Side Extended Array / Front Upper Array / Front Headlight Array /
        /// Front Lower Array / Hull Upper Array
        ///
        /// One-to-one mapping to the switches of the original M2Bradley.cs:77-100:
        ///   M2 Hull Side Lower ERA            -> Side Lower Array
        ///   M2 Extended Hull Side Lower ERA   -> Side Lower Extended Array
        ///   M2 Extended Hull Side ERA         -> Side Extended Array
        ///   M2 Hull Lower Front ERA           -> Front Lower Array
        ///   M2 Hull Upper ERA                 -> Hull Upper Array
        /// </summary>
        private static void TrimSubArrays(GameObject arrayRoot)
        {
            var toRemove = new List<GameObject>();

            foreach (var t in arrayRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t.gameObject == arrayRoot) continue;
                string n = t.name;

                if (!Config.ShowEraM2_HullSideLower         && n == "Side Lower Array")          { toRemove.Add(t.gameObject); continue; }
                if (!Config.ShowEraM2_HullSideLowerExtended && n == "Side Lower Extended Array") { toRemove.Add(t.gameObject); continue; }
                if (!Config.ShowEraM2_HullSideExtended      && n == "Side Extended Array")       { toRemove.Add(t.gameObject); continue; }
                if (!Config.ShowEraM2_HullLowerFront        && n == "Front Lower Array")         { toRemove.Add(t.gameObject); continue; }
                if (!Config.ShowEraM2_HullUpper             && n == "Hull Upper Array")          { toRemove.Add(t.gameObject); continue; }
            }

            foreach (var go in toRemove)
            {
                if (go != null) UnityEngine.Object.Destroy(go);
            }

            if (toRemove.Count > 0)
            {
                MelonLogger.Msg("[NatoERA/BRAT] Removed " + toRemove.Count + " sub-arrays according to the preferences.");
            }
        }

        /// <summary>
        /// Applies the hand tuned brick offsets for the addon plated hull.
        ///
        /// Caller: <see cref="HandleM2Extended"/>, and only after the M2Extended addon
        /// armour has been detected on this vehicle. A bare M2 Bradley never reaches
        /// this method, so it keeps the stock placement of the original mod.
        ///
        /// Two independent adjustments:
        ///
        /// 1. Hull side bricks (<see cref="SideBrickInset"/>)
        ///    The bricks mounted on the hull sides are pulled
        ///    <c>SideBrickInset</c> metres towards the hull so they hug the wider addon
        ///    plated hull instead of floating off it. See
        ///    <see cref="SideBrickArrays"/> for exactly which bricks this covers.
        ///
        /// 2. Rear corner bricks (<see cref="RearCornerBrickOffset"/>)
        ///    Five BRAT-M3 bricks at the left rear corner of "Hull Upper Array" needed a
        ///    small three axis nudge to close a gap along the hull shoulder.
        ///
        /// Both adjustments are applied to brick <c>localPosition</c> values, so they are
        /// stable no matter which rotation the hull node ends up with at runtime.
        /// </summary>
        private static void ApplyAddonBrickOffsets(GameObject arrayRoot)
        {
            int inset = 0;

            // ---- 1. Pull the hull side bricks inwards ----
            foreach (var t in EnumerateSideBricks(arrayRoot))
            {
                var p = t.localPosition;
                if (p.x > SideBrickCentreDeadZone)
                {
                    p.x -= SideBrickInset;
                    t.localPosition = p;
                    inset++;
                }
                else if (p.x < -SideBrickCentreDeadZone)
                {
                    p.x += SideBrickInset;
                    t.localPosition = p;
                    inset++;
                }
            }

            // ---- 2. Rear corner bricks ----
            // These receive the FULL three axis offset, so they are excluded from
            // EnumerateSideBricks: applying the side inset to them as well would move
            // them twice along X (measured: it landed 0.15 m off).
            int corner = 0;
            var hullUpper = arrayRoot.transform.Find(RearCornerArrayName);
            if (hullUpper != null)
            {
                foreach (var name in RearCornerBricks)
                {
                    var t = hullUpper.Find(name);
                    if (t == null) continue;
                    t.localPosition = t.localPosition + RearCornerBrickOffset;
                    corner++;
                }
            }

            if (inset > 0 || corner > 0)
            {
                MelonLogger.Msg("[NatoERA/BRAT] Addon plated hull: tuned brick offsets applied -- "
                    + inset + " side bricks pulled in by " + SideBrickInset.ToString("F2") + " m, "
                    + corner + " rear corner bricks nudged by (" + RearCornerBrickOffset.x.ToString("F3")
                    + ", " + RearCornerBrickOffset.y.ToString("F3") + ", " + RearCornerBrickOffset.z.ToString("F3") + ") m.");
            }
            else
            {
                MelonLogger.Warning("[NatoERA/BRAT] No brick matched the tuned offsets; the array keeps the stock placement.");
            }
        }

        /// <summary>
        /// Enumerates the bricks that belong to the hull <em>sides</em>.
        ///
        /// Only the three side sub-arrays are walked, plus the bricks parented straight
        /// to the array root. The glacis arrays ("Front Upper Array",
        /// "Front Headlight Array", "Front Lower Array") are deliberately skipped: their
        /// bricks also sit off the centre line (x up to 1.293) but they are mounted on the
        /// front plate, not the sides, and pulling them in would break the glacis
        /// alignment the prefab ships with.
        ///
        /// "Hull Upper Array" is skipped as a whole; the handful of bricks in it that
        /// needed moving are handled by <see cref="RearCornerBricks"/> instead.
        /// </summary>
        private static IEnumerable<Transform> EnumerateSideBricks(GameObject arrayRoot)
        {
            var root = arrayRoot.transform;

            // Bricks parented straight to the array root.
            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child == null) continue;
                if (Array.IndexOf(SideBrickArrays, child.name) >= 0) continue; // handled below
                yield return child;
            }

            // Bricks inside the three side sub-arrays.
            foreach (var arrayName in SideBrickArrays)
            {
                var array = root.Find(arrayName);
                if (array == null) continue;
                for (int i = 0; i < array.childCount; i++)
                {
                    var brick = array.GetChild(i);
                    if (brick != null) yield return brick;
                }
            }
        }

        /// <summary>
        /// BRAT fitted mass (kg). Takes Config.TotalMassM2 directly, i.e. the result of accumulating the table
        /// of the original M2Bradley.cs:103-108 against the current switches (all 0 when Feather BRAT is on).
        /// </summary>
        private static float ComputeBradleyMass()
        {
            return Config.TotalMassM2;
        }

        // =====================================================================
        // Utilities
        // =====================================================================

        /// <summary>
        /// <summary>
        /// Finds the hull node.
        ///
        /// Route 1: find by name through the LateFollowTarget's LateFollow list (the public property is available in the new version)
        /// Route 2: the stock "bradley_armour_rigged/HULL" node, which is where every stock hull armor plate lives
        /// Route 3: find by name directly in the child hierarchy (fallback)
        ///
        /// Why route 2 exists: on the current model "BRADLEY HULL FOLLOW" has a single child named
        /// "Driver" and NO "HULL", so the original author's Find("HULL") returns null and the array would
        /// be instantiated without a parent, i.e. left standing in the scene at world origin instead of
        /// riding on the vehicle. A blind deep search is not good enough either, because the prefab
        /// contains three different nodes called "HULL" (under "bradley_armour_rigged",
        /// "M2BRADLEY_rig" and "bradley_int_rigged") and which one gets hit depends on traversal order.
        /// Verified offline against M2 Bradley.prefab: "bradley_armour_rigged/HULL" is the one that
        /// carries the hull armor plates ("Hull Sides Hard Steel 0.25", "Hull Front Alu 5083", ...).
        /// </summary>
        private static Transform FindHull(Transform vehicleRoot)
        {
            if (vehicleRoot == null) return null;

            // Route 1
            try
            {
                var lft = vehicleRoot.GetComponentInChildren<LateFollowTarget>(true);
                if (lft != null)
                {
                    foreach (var lf in Compat.GetLateFollowers(lft))
                    {
                        if (lf == null) continue;
                        if (!string.Equals(lf.name, HullFollowName, StringComparison.OrdinalIgnoreCase)) continue;

                        var child = lf.transform.Find(HullNodeName);
                        if (child != null) return child;

                        var deep = Compat.FindDeep(lf.transform, HullNodeName);
                        if (deep != null) return deep;
                    }
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[NatoERA/BRAT] Failed to find HULL through LateFollow: " + e.Message);
            }

            // Route 2: the armor rig node, the only "HULL" that actually carries armor plates
            var armourRig = Compat.FindDeep(vehicleRoot, ArmourRigName);
            if (armourRig != null)
            {
                var hullDirect = armourRig.Find(HullNodeName);
                if (hullDirect != null)
                {
                    MelonLogger.Msg("[NatoERA/BRAT] Using " + ArmourRigName + "/" + HullNodeName
                        + " as the hull node (" + HullFollowName + "/" + HullNodeName + " does not exist on this model).");
                    return hullDirect;
                }
            }

            // Route 3
            var fallback = Compat.FindDeep(vehicleRoot, HullNodeName);
            if (fallback != null)
            {
                MelonLogger.Msg("[NatoERA/BRAT] Found " + HullNodeName + " directly through the hierarchy (" + fallback.GetType().Name + ")");
            }

            return fallback;
        }

        private static void ApplyMass(Unit unit)
        {
            try
            {
                var rb = unit.GetComponent<Rigidbody>();
                if (rb == null)
                {
                    MelonLogger.Warning("[NatoERA/BRAT] " + unit.name + " has no Rigidbody, skipping mass compensation.");
                    return;
                }

                // Accumulated from the original author's weight table (all 0 when Feather BRAT is on)
                float extra = _lastMassKg > 0f ? _lastMassKg : ComputeBradleyMass();
                if (extra <= 0f) return;

                rb.mass += extra;
                MelonLogger.Msg("[NatoERA/BRAT] " + unit.name + " added " + extra.ToString("F0")
                    + " kg -> total mass " + rb.mass.ToString("F0") + " kg");
            }
            catch (Exception e)
            {
                MelonLogger.Error("[NatoERA/BRAT] Failed to set mass: " + e.Message);
            }
        }
    }
}
