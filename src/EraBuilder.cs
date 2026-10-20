using System;
using System.Collections.Generic;
using System.Linq;
using GHPC;
using GHPC.Effects;
using GHPC.Equipment;
using GHPC.Thermals;
using MelonLoader;
using UnityEngine;

namespace NatoEra
{
    /// <summary>
    /// Shared build helper for ERA (explosive reactive armor) arrays.
    ///
    /// ARAT (M1/M1IP) and BRAT (M2 Bradley) share almost the same assembly flow;
    /// the only differences are: bundle name, brick name prefix, protection values and color preference.
    /// The old code copied this logic once per class; here it is consolidated into one place.
    ///
    /// Key points (from decompiling and checking the live FuldaExpansion1989 build):
    ///   - ERA bricks must be on a layer that BOTH LiveRoundLayerMask accepts and the camera renders,
    ///     and must carry tag = "Penetrable", otherwise the ERA does nothing or stays invisible.
    ///     The intersection is { Default, Water, Terrain }; see the EraLayer property for the measurements.
    ///   - The only detonation effect confirmed to work is the GameObject named "HEAT Impact" in the assets.
    ///     The "Autocannon HE Armor Impact" the old version looked for no longer exists in the new build.
    /// </summary>
    public static class EraBuilder
    {
        /// <summary>ERA brick parameters</summary>
        public class EraSpec
        {
            /// <summary>Keyword that brick GameObject names must contain, e.g. "ARAT-1" / "BRAT-M5"</summary>
            public string NameFilter;

            /// <summary>Name passed to UniformArmor.SetName</summary>
            public string DisplayName;

            /// <summary>CE (chemical energy / HEAT) protection, maps to PrimaryHeatRha</summary>
            public float HeatRha;

            /// <summary>KE (kinetic energy / AP) protection, maps to PrimarySabotRha</summary>
            public float SabotRha;

            /// <summary>Brick color</summary>
            public Color Color = new Color(0.22f, 0.23f, 0.18f);
        }

        /// <summary>
        /// Layer index the ERA hit volumes use: "Raycast Only" (8).
        ///
        /// [Why layer 8 and not layer 0]
        /// An earlier build put the bricks on Default(0) so the camera would render them, which fixed the
        /// invisible-ERA bug but broke the physics: the layer-0 collision matrix intersects Main Body(14) and
        /// Terrain(18), so 148 solid bricks embedded inside the hull started fighting the vehicle's own
        /// colliders and launched the tank into the air as soon as the player drove off.
        ///
        /// Layer 8 collides with nothing but layer 6, so bricks on it never push the tank. It is still
        /// visible to projectiles: LiveRoundLayerMask = 262545 contains bit 8.
        ///
        /// There is a second, nastier reason the bricks must NOT rely on disabled colliders: every projectile
        /// raycast goes through GHPC.PhysicsHelpers.RaycastColliderUtils.Raycast, whose RaycastImpl calls
        /// DetectAndEnableColliderTree - i.e. the game re-enables every collider under a group bounding box
        /// as soon as a round flies past. Any "disable the collider" workaround is therefore undone the first
        /// time anything is shot at, which is exactly why the tank only took off while the player was driving.
        /// The fix has to be the layer, not the enabled flag.
        ///
        /// Rendering is handled separately: the brick's MeshRenderer is kept on the default layer by
        /// MoveVisualsToRenderedLayer so the camera still draws it.
        /// </summary>
        public static int EraLayer
        {
            get
            {
                if (_eraLayer >= 0) return _eraLayer;

                _eraLayer = HitVolumeLayer;

                var cam = UnityEngine.Camera.main;
                int cameraMask = cam != null ? cam.cullingMask : 0;
                MelonLogger.Msg("[NatoERA/EraBuilder] ERA hit volumes on layer " + _eraLayer
                    + " (" + UnityEngine.LayerMask.LayerToName(_eraLayer) + "), visuals on layer "
                    + VisualLayer + " (" + UnityEngine.LayerMask.LayerToName(VisualLayer) + ")"
                    + "; main camera culling mask=" + cameraMask);
                return _eraLayer;
            }
        }

        private static int _eraLayer = -1;

        /// <summary>"Raycast Only" (8): projectiles see it, physics ignores it.</summary>
        public const int HitVolumeLayer = 8;

        /// <summary>Default (0): every camera renders it, and no collider of ours ever lives here.</summary>
        public const int VisualLayer = 0;

        /// <summary>Tag for ERA bricks, which routes projectiles into the armor penetration check.</summary>
        public const string EraTag = "Penetrable";

        // =====================================================================
        // Main entry point
        // =====================================================================

        /// <summary>
        /// Attaches reactive armor to all matching bricks under an ERA array (the root of a prefab instantiated from a bundle).
        /// </summary>
        /// <param name="arrayRoot">ERA array root node</param>
        /// <param name="spec">Brick parameters</param>
        /// <param name="codex">Shared ArmorCodexScriptable, created on first call</param>
        /// <param name="armorType">Shared ArmorType, created on first call</param>
        /// <returns>Number of bricks actually configured</returns>
        public static int Setup(GameObject arrayRoot, EraSpec spec,
                                ref ArmorCodexScriptable codex,
                                ref GHPC.Equipment.ArmorType armorType)
        {
            if (arrayRoot == null)
            {
                MelonLogger.Error("[NatoERA/EraBuilder] arrayRoot is null, skipping.");
                return 0;
            }

            if (spec == null || string.IsNullOrEmpty(spec.NameFilter))
            {
                MelonLogger.Error("[NatoERA/EraBuilder] spec or NameFilter is empty, skipping.");
                return 0;
            }

            EnsureCodex(spec, ref codex, ref armorType);

            var all = arrayRoot.GetComponentsInChildren<Transform>(true);
            int applied = 0;

            foreach (var t in all)
            {
                if (t == null) continue;
                var go = t.gameObject;
                if (!go.name.Contains(spec.NameFilter)) continue;

                try
                {
                    ConfigureBrick(go, spec, codex);
                    applied++;
                }
                catch (Exception e)
                {
                    MelonLogger.Error("[NatoERA/EraBuilder] Failed to configure brick " + go.name + ": " + e);
                }
            }

            // The hit volumes stay on "Raycast Only"; the meshes that render move to Default.
            // See EraLayer for why this split is mandatory and why blanket-relayering to the camera
            // layer (the previous behaviour) made the tanks take off the moment anything was fired.
            int hitNodes = 0;
            int visualNodes = 0;
            ApplyLayers(arrayRoot, ref hitNodes, ref visualNodes);

            MelonLogger.Msg("[NatoERA/EraBuilder] " + spec.DisplayName + " complete: "
                + applied + " bricks configured (" + all.Length + " nodes in the array), "
                + hitNodes + " hit-volume nodes on layer " + HitVolumeLayer
                + ", " + visualNodes + " visual nodes on layer " + VisualLayer);
            return applied;
        }

        /// <summary>
        /// Splits the array into hit volumes (layer 8) and visuals (layer 0).
        ///
        /// Every node that carries, or parents, a brick collider stays on layer 8 so the physics engine
        /// ignores it. Every node that only carries a MeshRenderer moves to layer 0 so the camera draws it.
        /// The brick itself is special: its collider must stay on layer 8 while its renderer needs to be
        /// visible, so the renderer is detached into a child quad on layer 0 (see MakeBrickVisual).
        /// </summary>
        private static void ApplyLayers(GameObject arrayRoot, ref int hitNodes, ref int visualNodes)
        {
            var transforms = arrayRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                var t = transforms[i];
                if (t == null) continue;
                var go = t.gameObject;

                // Anything that has (or holds) a collider has to stay invisible to physics.
                bool needsHitVolume = go.GetComponent<Collider>() != null
                                      || go.GetComponentInChildren<Collider>(true) != null;

                if (needsHitVolume)
                {
                    if (go.layer != HitVolumeLayer) { go.layer = HitVolumeLayer; hitNodes++; }
                }
                else
                {
                    if (go.layer != VisualLayer) { go.layer = VisualLayer; visualNodes++; }
                }
            }
        }

        /// <summary>
        /// Counts the matching bricks under an ERA array (modifies nothing).
        /// Used to estimate mass from the bricks actually kept.
        /// </summary>
        public static int CountBricks(GameObject arrayRoot, string nameFilter)
        {
            if (arrayRoot == null || string.IsNullOrEmpty(nameFilter)) return 0;
            int n = 0;
            foreach (var t in arrayRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t.gameObject.name.Contains(nameFilter)) n++;
            }
            return n;
        }

        /// <summary>
        /// Returns the fixed mass for the number of sub-arrays actually kept.
        ///
        /// [Why we no longer estimate from brick count]
        /// The original author assigned a fixed weight per sub-array (see the README table),
        /// e.g. M1IP hull side 1887 kg, turret 737 kg, turret roof 501 kg, ...
        /// Those values were weighed per sub-array as a whole, not as "one brick x brick count",
        /// so estimating from brick count yields completely different numbers. To keep the same feel as the old version,
        /// the original table values are used everywhere here, and the caller accumulates them according to its switches.
        /// </summary>
        [Obsolete("Use the fixed weight tables in AbramsEra/BradleyEra instead (consistent with the original author)")]
        public static float EstimateMass(GameObject arrayRoot)
        {
            return 0f;
        }

        /// <summary>
        /// Creates (or reuses) the ArmorCodex shared by the whole mod.
        /// </summary>
        private static void EnsureCodex(EraSpec spec,
                                        ref ArmorCodexScriptable codex,
                                        ref GHPC.Equipment.ArmorType armorType)
        {
            if (codex != null) return;

            ArmorType at;
            try
            {
                at = new GHPC.Equipment.ArmorType();
            }
            catch (Exception e)
            {
                MelonLogger.Error("[NatoERA/EraBuilder] Could not create ArmorType: " + e.Message);
                return;
            }

            // Parameters consistent with Fulda / the old NatoERA
            at.RhaeMultiplierKe = 1f;
            at.RhaeMultiplierCe = 1f;
            at.CanRicochet = false;          // an ERA surface does not ricochet AP rounds
            at.CrushThicknessModifier = 1f;
            at.NormalizesHits = true;        // ERA dampens the normalization of long-rod penetrators
            at.CanShatterLongRods = false;
            at.ThicknessSource = GHPC.Equipment.ArmorType.RhaSource.Multipliers;
            at.Name = spec.DisplayName + " Armor";

            var so = ScriptableObject.CreateInstance<ArmorCodexScriptable>();
            so.name = spec.DisplayName + " Armor Codex";
            so.hideFlags = HideFlags.DontUnloadUnusedAsset;
            so.ArmorType = at;

            codex = so;
            armorType = at;

            MelonLogger.Msg("[NatoERA/EraBuilder] Created ArmorCodex: " + so.name);
        }

        // =====================================================================
        // Single brick
        // =====================================================================

        private static void ConfigureBrick(GameObject go, EraSpec spec, ArmorCodexScriptable codex)
        {
            // ---- Colliders ----
            // Every brick in the bundle already ships with a BoxCollider + MeshCollider.
            // The old code called AddComponent<BoxCollider>() unconditionally, stacking a second collider and causing duplicate hits and physics glitches.
            if (go.GetComponent<Collider>() == null)
            {
                go.AddComponent<BoxCollider>();
            }

            // Keep exactly one collider: the BoxCollider. It is the only one needed - the projectile
            // ray test wants a convex, cheap shape - and the bundle's non-convex MeshCollider is dropped
            // because Unity silently refuses isTrigger on it (the property stays false, so it keeps acting
            // as a solid body). Do not rely on enabled=false anywhere: RaycastColliderUtils.RaycastImpl
            // calls DetectAndEnableColliderTree before every raycast, which re-enables colliders under a
            // group bounding box. Safety comes from the layer instead, see EraLayer.
            foreach (var col in go.GetComponents<Collider>())
            {
                if (col == null) continue;

                var meshCol = col as MeshCollider;
                if (meshCol != null)
                {
                    meshCol.isTrigger = true;
                    meshCol.enabled = false;
                    continue;
                }

                col.isTrigger = true;
                col.enabled = true;
            }

            // ---- Layer and tag: whether projectiles can hit this depends entirely on these two lines ----
            go.layer = HitVolumeLayer;
            try
            {
                go.tag = EraTag;
            }
            catch (Exception)
            {
                // Throws if the tag is not registered; leaves the rest of the configuration intact
            }

            // ---- Reactive armor component ----
            var armor = Compat.AddOrGetComponent<UniformArmor>(go);
            if (armor == null)
            {
                MelonLogger.Error("[NatoERA/EraBuilder] Could not add UniformArmor: " + go.name);
                return;
            }

            armor.SetName(spec.DisplayName);

            armor.PrimaryHeatRha = spec.HeatRha;
            armor.PrimarySabotRha = spec.SabotRha;
            armor.SecondaryHeatRha = 0f;
            armor.SecondarySabotRha = 0f;
            armor.AngleMatters = true;

            // ---- Visual ----
            // The brick GameObject now lives on the hit-volume layer, where no camera draws it.
            // Its mesh is moved into a plain child on the rendered layer so it stays visible, and that
            // child is what Detonate() hides (see the UndetonatedObjects assignment below).
            var visual = MakeBrickVisual(go, spec);

            // Hide the visual on detonation. Detonate() disables the Renderer of every object
            // in UndetonatedObjects, so filling in the visual object is enough.
            armor.UndetonatedObjects = new[] { visual };
            armor.DetonatedObjects = new GameObject[0];

            // Private fields (the new build ships no publicized assembly, so these go through reflection)
            Compat.SetIsEra(armor, true);
            Compat.SetCanShatterLongRods(armor, false);
            Compat.SetCrushThicknessModifier(armor, 1f);
            Compat.SetNormalizesHits(armor, true);
            Compat.SetArmorType(armor, codex);

            // ---- Detonation effect ----
            if (armor.DetonateEffect == null)
            {
                armor.DetonateEffect = DetonateEffect;
            }

            // ---- Heat source: gives the bricks a thermal signature in thermal sights ----
            // HeatSource.OnEnable sets Heat to its serialized default of 0.8f, matching the old behavior.
            // It goes on the visual object, next to the renderer it is supposed to warm up.
            if (visual.GetComponent<HeatSource>() == null)
            {
                visual.AddComponent<HeatSource>();
            }
        }

        /// <summary>Name of the generated child that carries a brick's rendered mesh.</summary>
        public const string VisualChildName = "ERAVisual";

        /// <summary>
        /// Moves a brick's rendered mesh onto a child GameObject on the rendered layer and returns that child.
        ///
        /// The brick itself has to stay on the hit-volume layer for physics, which would make it invisible,
        /// so the MeshFilter/MeshRenderer pair is re-parented into a child that sits on VisualLayer with an
        /// identity local transform. Any material treatment is applied to the moved renderer afterwards.
        /// </summary>
        private static GameObject MakeBrickVisual(GameObject go, EraSpec spec)
        {
            // Idempotent: if the visual was already split out on a previous pass, reuse it.
            var existing = go.transform.Find(VisualChildName);
            if (existing != null)
            {
                ApplyMaterial(existing.gameObject, spec);
                return existing.gameObject;
            }

            var srcFilter = go.GetComponent<MeshFilter>();
            var srcRenderer = go.GetComponent<MeshRenderer>();
            if (srcFilter == null && srcRenderer == null)
            {
                // No mesh at all (should not happen); fall back to the brick itself so the caller
                // still has something to hide on detonation.
                return go;
            }

            var visual = new GameObject(VisualChildName);
            visual.layer = VisualLayer;
            visual.transform.SetParent(go.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;

            if (srcFilter != null)
            {
                var dst = visual.AddComponent<MeshFilter>();
                dst.sharedMesh = srcFilter.sharedMesh;
                UnityEngine.Object.Destroy(srcFilter);
            }

            if (srcRenderer != null)
            {
                var dst = visual.AddComponent<MeshRenderer>();
                dst.sharedMaterials = srcRenderer.sharedMaterials;
                dst.shadowCastingMode = srcRenderer.shadowCastingMode;
                dst.receiveShadows = srcRenderer.receiveShadows;
                dst.lightProbeUsage = srcRenderer.lightProbeUsage;
                dst.reflectionProbeUsage = srcRenderer.reflectionProbeUsage;
                dst.motionVectorGenerationMode = srcRenderer.motionVectorGenerationMode;
                UnityEngine.Object.Destroy(srcRenderer);
            }

            ApplyMaterial(visual, spec);
            return visual;
        }

        private static void ApplyMaterial(GameObject go, EraSpec spec)
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null) return;

            var shader = Compat.FlirShader;
            if (shader == null) return;

            Material mat;
            if (mr.sharedMaterial != null)
            {
                // Copy before editing so the material shared by other objects in the same bundle is not polluted
                mat = new Material(mr.sharedMaterial);
            }
            else
            {
                mat = new Material(shader);
            }

            mat.shader = shader;
            mat.color = spec.Color;

            var tex = FallbackTexture;
            if (tex != null)
            {
                mat.mainTexture = tex;
                mat.mainTextureScale = new Vector2(0.07f, 0.07f);
                mat.mainTextureOffset = Vector2.zero;

                // The original ARAT.cs:113 explicitly enabled the normal map keyword.
                // If the new build's material has _BumpMap, wire it up as well, otherwise the bricks look perfectly flat.
                var bump = FallbackNormalMap;
                if (bump != null && mat.HasProperty("_BumpMap"))
                {
                    mat.EnableKeyword("_NORMALMAP");
                    mat.SetTexture("_BumpMap", bump);
                }
            }

            mr.sharedMaterial = mat;

            // The bundle ships the sub-array bricks with a plain "Standard" material and a white
            // base color, while the 64 default bricks already use the FLIR shader. Without this
            // log the difference is invisible until you look at the vehicle in-game (they render
            // as white blocks), so report anything that still does not match.
            if (mr.sharedMaterial == null || mr.sharedMaterial.shader != shader)
            {
                MelonLogger.Warning("[NatoERA/EraBuilder] " + go.name
                    + ": material did not take the FLIR shader ("
                    + (mr.sharedMaterial != null && mr.sharedMaterial.shader != null
                        ? mr.sharedMaterial.shader.name : "null") + ").");
            }
        }

        // =====================================================================
        // Detonation effect
        // =====================================================================

        private static GameObject _detonateEffect;
        private static bool _detonateLookupDone;

        /// <summary>
        /// Looks up the ERA detonation effect.
        ///
        /// Confirmed: the "HEAT Impact" asset exists in the new game build
        /// (that is exactly how the live FuldaExpansion1989 build fetches it).
        /// The "Autocannon HE Armor Impact" used by the old NatoERA no longer exists.
        /// </summary>
        public static GameObject DetonateEffect
        {
            get
            {
                if (_detonateLookupDone) return _detonateEffect;
                _detonateLookupDone = true;

                // Route 1: the "HEAT Impact" asset from the game resources (preferred, visually correct)
                try
                {
                    foreach (var o in Resources.FindObjectsOfTypeAll<GameObject>())
                    {
                        if (o != null && o.name == "HEAT Impact")
                        {
                            _detonateEffect = o;
                            MelonLogger.Msg("[NatoERA/EraBuilder] Detonation effect: HEAT Impact");
                            return _detonateEffect;
                        }
                    }
                }
                catch (Exception e)
                {
                    MelonLogger.Warning("[NatoERA/EraBuilder] Looking up HEAT Impact failed: " + e.Message);
                }

                // Route 2: the public prefab fields on ParticleEffectsManager
                try
                {
                    var pem = ParticleEffectsManager.Instance;
                    if (pem != null)
                    {
                        _detonateEffect = pem.RicochetEffectPrefab
                                       ?? pem.MediumRicochetEffectPrefab
                                       ?? pem.LargeRicochetEffectPrefab;
                        if (_detonateEffect != null)
                        {
                            MelonLogger.Msg("[NatoERA/EraBuilder] Detonation effect: ParticleEffectsManager." + _detonateEffect.name);
                            return _detonateEffect;
                        }
                    }
                }
                catch (Exception e)
                {
                    MelonLogger.Warning("[NatoERA/EraBuilder] Fetching a ParticleEffectsManager prefab failed: " + e.Message);
                }

                MelonLogger.Warning("[NatoERA/EraBuilder] No detonation effect found, leaving DetonateEffect empty (does not affect detonation logic or brick hiding).");
                return null;
            }
        }

        // =====================================================================
        // Textures
        // =====================================================================

        private static Texture _fallbackTex;
        private static bool _fallbackTexSearched;

        /// <summary>
        /// Base texture for the bricks.
        /// The old version looked up GHPC_ConcretePanels_Diffuse in Resources; that asset is no longer in Resources in the new build.
        /// Returns null when not found -- the material then stays flat-colored, which is perfectly acceptable visually.
        /// </summary>
        private static Texture FallbackTexture
        {
            get
            {
                if (_fallbackTexSearched) return _fallbackTex;
                _fallbackTexSearched = true;

                try
                {
                    foreach (var t in Resources.FindObjectsOfTypeAll<Texture>())
                    {
                        if (t != null && t.name == "GHPC_ConcretePanels_Diffuse")
                        {
                            _fallbackTex = t;
                            MelonLogger.Msg("[NatoERA/EraBuilder] Found brick base texture GHPC_ConcretePanels_Diffuse");
                            return _fallbackTex;
                        }
                    }
                }
                catch (Exception) { }

                MelonLogger.Msg("[NatoERA/EraBuilder] GHPC_ConcretePanels_Diffuse not found, bricks use a flat-color material.");
                return null;
            }
        }

        private static Texture _fallbackNormalMap;
        private static bool _fallbackNormalMapSearched;

        /// <summary>
        /// Normal map for the bricks (the concrete_tex_normal used at ARAT.cs:114 in the original).
        /// Also looked up by name in Resources; skipped when not found.
        /// </summary>
        private static Texture FallbackNormalMap
        {
            get
            {
                if (_fallbackNormalMapSearched) return _fallbackNormalMap;
                _fallbackNormalMapSearched = true;

                try
                {
                    foreach (var t in Resources.FindObjectsOfTypeAll<Texture>())
                    {
                        if (t != null && t.name == "GHPC_ConcretePanels_Normal")
                        {
                            _fallbackNormalMap = t;
                            MelonLogger.Msg("[NatoERA/EraBuilder] Found brick normal map GHPC_ConcretePanels_Normal");
                            return _fallbackNormalMap;
                        }
                    }
                }
                catch (Exception) { }

                MelonLogger.Msg("[NatoERA/EraBuilder] GHPC_ConcretePanels_Normal not found, bricks use no normal map.");
                return null;
            }
        }

        // =====================================================================
        // Bundle loading
        // =====================================================================

        /// <summary>
        /// Loads a prefab from an AssetBundle located in the Mods/NatoEraAssets directory.
        /// </summary>
        public static GameObject LoadEraPrefab(string bundleFileName, string assetName)
        {
            try
            {
                var dir = System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.ModsDirectory, "NatoEraAssets");
                var path = System.IO.Path.Combine(dir, bundleFileName);

                if (!System.IO.File.Exists(path))
                {
                    MelonLogger.Error("[NatoERA/EraBuilder] AssetBundle does not exist: " + path);
                    return null;
                }

                var bundle = AssetBundle.LoadFromFile(path);
                if (bundle == null)
                {
                    MelonLogger.Error("[NatoERA/EraBuilder] Failed to load AssetBundle: " + path);
                    return null;
                }

                var prefab = bundle.LoadAsset<GameObject>(assetName);
                if (prefab == null)
                {
                    MelonLogger.Error("[NatoERA/EraBuilder] Asset \"" + assetName + "\" not found in bundle: " + path);
                    MelonLogger.Msg("[NatoERA/EraBuilder] Available assets in this bundle: " + string.Join(", ", bundle.GetAllAssetNames()));
                    return null;
                }

                prefab.hideFlags = HideFlags.DontUnloadUnusedAsset;
                MelonLogger.Msg("[NatoERA/EraBuilder] Loaded " + bundleFileName + " → " + assetName);
                return prefab;
            }
            catch (Exception e)
            {
                MelonLogger.Error("[NatoERA/EraBuilder] Exception while loading bundle (" + bundleFileName + "): " + e);
                return null;
            }
        }
    }
}

