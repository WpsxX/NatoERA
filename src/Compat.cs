using System;
using System.Collections.Generic;
using System.Reflection;
using GHPC;
using GHPC.Equipment;
using GHPC.Thermals;
using GHPC.Utility;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NatoEra
{
    /// <summary>
    /// Compatibility layer for accessing private members of the game internals.
    ///
    /// Background: the old NatoERA was compiled against Assembly-CSharp_publicized.dll,
    /// so writing uniformArmor._isEra = true directly compiled fine. In the new
    /// environment that publicized assembly is not guaranteed to exist (it may also
    /// exist but fail to load because of a different assembly identity), so all access
    /// here goes through Harmony's AccessTools reflection instead.
    ///
    /// Benefit: regardless of whether the original Assembly-CSharp.dll or the publicized
    /// version is loaded at runtime, reflection can always reach the field -- in the
    /// original the field is private (reflection can still access it), in the publicized
    /// version the field is public (reflection can access that too).
    /// </summary>
    public static class Compat
    {
        private static bool _resolved;

        // ---- UniformArmor private fields ----
        private static FieldInfo _fIsEra;
        private static FieldInfo _fIsDetonated;
        private static FieldInfo _fArmorType;
        private static FieldInfo _fCanShatterLongRods;
        private static FieldInfo _fCrushThicknessModifier;
        private static FieldInfo _fName;
        private static FieldInfo _fNormalizesHits;
        private static FieldInfo _fCanRicochet;
        private static FieldInfo _fSurfaceMaterial;
        private static FieldInfo _fNoImpactDecals;

        // ---- Private members of other types ----
        private static FieldInfo _fFlirShader;
        private static FieldInfo _fLateFollowers;

        /// <summary>Resolves all fields on first use. Repeated calls have no side effects.</summary>
        public static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static
                                     | BindingFlags.Public | BindingFlags.NonPublic;

            // UniformArmor
            _fIsEra                 = AccessTools.Field(typeof(UniformArmor), "_isEra");
            _fIsDetonated           = AccessTools.Field(typeof(UniformArmor), "_isDetonated");
            _fArmorType             = AccessTools.Field(typeof(UniformArmor), "_armorType");
            _fCanShatterLongRods    = AccessTools.Field(typeof(UniformArmor), "_canShatterLongRods");
            _fCrushThicknessModifier= AccessTools.Field(typeof(UniformArmor), "_crushThicknessModifier");
            _fName                  = AccessTools.Field(typeof(UniformArmor), "_name");
            _fNormalizesHits        = AccessTools.Field(typeof(UniformArmor), "_normalizesHits");
            _fCanRicochet           = AccessTools.Field(typeof(UniformArmor), "_canRicochet");
            _fSurfaceMaterial       = AccessTools.Field(typeof(UniformArmor), "_surfaceMaterial");
            _fNoImpactDecals        = AccessTools.Field(typeof(UniformArmor), "_noImpactDecals");

            // FLIR's shader cache (the game already did Shader.Find for it, so reusing it is the most reliable)
            _fFlirShader            = AccessTools.Field(typeof(FLIR), "_surfShader");

            // LateFollowTarget's private list
            _fLateFollowers         = AccessTools.Field(typeof(LateFollowTarget), "_lateFollowers");

            LogMissing();
        }

        private static void LogMissing()
        {
            var missing = new List<string>();
            if (_fIsEra == null)                  missing.Add("UniformArmor._isEra");
            if (_fIsDetonated == null)            missing.Add("UniformArmor._isDetonated");
            if (_fArmorType == null)              missing.Add("UniformArmor._armorType");
            if (_fCanShatterLongRods == null)     missing.Add("UniformArmor._canShatterLongRods");
            if (_fCrushThicknessModifier == null) missing.Add("UniformArmor._crushThicknessModifier");
            if (_fFlirShader == null)             missing.Add("FLIR._surfShader");
            if (_fLateFollowers == null)           missing.Add("LateFollowTarget._lateFollowers");

            if (missing.Count > 0)
            {
                MelonLoader.MelonLogger.Warning("[NatoERA/Compat] The following internal members were not found (related features will be degraded): "
                    + string.Join(", ", missing));
            }
        }

        // =====================================================================
        // UniformArmor private field access
        // =====================================================================

        public static void SetIsEra(UniformArmor a, bool v)
        {
            Resolve();
            if (_fIsEra != null) _fIsEra.SetValue(a, v);
        }

        public static void SetArmorType(UniformArmor a, ArmorCodexScriptable v)
        {
            Resolve();
            if (_fArmorType != null) _fArmorType.SetValue(a, v);
        }

        public static void SetCanShatterLongRods(UniformArmor a, bool v)
        {
            Resolve();
            if (_fCanShatterLongRods != null) _fCanShatterLongRods.SetValue(a, v);
        }

        public static void SetCrushThicknessModifier(UniformArmor a, float v)
        {
            Resolve();
            if (_fCrushThicknessModifier != null) _fCrushThicknessModifier.SetValue(a, v);
        }

        public static void SetNormalizesHits(UniformArmor a, bool v)
        {
            Resolve();
            if (_fNormalizesHits != null) _fNormalizesHits.SetValue(a, v);
        }

        public static void SetCanRicochet(UniformArmor a, bool v)
        {
            Resolve();
            if (_fCanRicochet != null) _fCanRicochet.SetValue(a, v);
        }

        public static bool GetIsDetonated(UniformArmor a)
        {
            Resolve();
            if (_fIsDetonated == null) return false;
            return (bool)_fIsDetonated.GetValue(a);
        }

        /// <summary>Resets _isDetonated to false so the same ERA block can be reused (for debugging).</summary>
        public static void ResetIsDetonated(UniformArmor a)
        {
            Resolve();
            if (_fIsDetonated != null) _fIsDetonated.SetValue(a, false);
        }

        // =====================================================================
        // FLIR shader
        // =====================================================================

        private static Shader _flirShader;
        private static bool _flirSearched;

        /// <summary>
        /// Gets the "Standard (FLIR)" shader used by the game's thermal imaging.
        ///
        /// That shader is a build artifact: Shader.Find cannot find it in the Editor,
        /// but it works at runtime. Reading the static field already cached by the FLIR
        /// class via reflection is preferred, to avoid a failed string lookup.
        /// </summary>
        public static Shader FlirShader
        {
            get
            {
                if (_flirSearched) return _flirShader;
                _flirSearched = true;

                // Route 1: the static field the game itself filled via Shader.Find
                try
                {
                    Resolve();
                    if (_fFlirShader != null)
                    {
                        _flirShader = _fFlirShader.GetValue(null) as Shader;
                        if (_flirShader != null)
                        {
                            MelonLoader.MelonLogger.Msg("[NatoERA/Compat] FLIR shader obtained from GHPC.Thermals.FLIR._surfShader");
                            return _flirShader;
                        }
                    }
                }
                catch (Exception e)
                {
                    MelonLoader.MelonLogger.Warning("[NatoERA/Compat] Failed to read FLIR._surfShader via reflection: " + e.Message);
                }

                // Route 2: find by name (same approach as the third-party mod M1A1Abrams-CROWSFIX)
                try
                {
                    _flirShader = Shader.Find("Standard (FLIR)");
                    if (_flirShader != null)
                    {
                        MelonLoader.MelonLogger.Msg("[NatoERA/Compat] FLIR shader obtained from Shader.Find(\"Standard (FLIR)\")");
                        return _flirShader;
                    }
                }
                catch (Exception) { }

                // Route 3: fall back to plain Standard; ERA blocks get no special shading in thermal, but nothing crashes
                try
                {
                    _flirShader = Shader.Find("Standard");
                    MelonLoader.MelonLogger.Warning("[NatoERA/Compat] FLIR shader not found, falling back to Standard (appearance in thermal is suboptimal)");
                }
                catch (Exception) { }

                return _flirShader;
            }
        }

        // =====================================================================
        // LateFollowTarget
        // =====================================================================

        /// <summary>
        /// Gets the LateFollow list on a vehicle.
        /// Newer versions have the public property LateFollowers, prefer it; older versions fall back to the private field.
        /// </summary>
        public static IList<LateFollow> GetLateFollowers(LateFollowTarget t)
        {
            if (t == null) return new List<LateFollow>();

            try
            {
                var prop = AccessTools.Property(typeof(LateFollowTarget), "LateFollowers");
                if (prop != null)
                {
                    var v = prop.GetValue(t, null);
                    if (v is IList<LateFollow> list) return list;
                    if (v != null) return new List<LateFollow>(v as IEnumerable<LateFollow>);
                }
            }
            catch (Exception) { }

            try
            {
                Resolve();
                if (_fLateFollowers != null)
                {
                    var v = _fLateFollowers.GetValue(t);
                    if (v is IList<LateFollow> list) return list;
                }
            }
            catch (Exception e)
            {
                MelonLoader.MelonLogger.Warning("[NatoERA/Compat] Failed to read LateFollowers: " + e.Message);
            }

            return new List<LateFollow>();
        }

        // =====================================================================
        // Convenience helpers
        // =====================================================================

        /// <summary>
        /// Finds a Transform among the children by name (case-insensitive, supports "A/B/C" paths).
        /// </summary>
        public static Transform FindDeep(Transform root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path)) return null;

            var direct = root.Find(path);
            if (direct != null) return direct;

            var last = path;
            int slash = path.LastIndexOf('/');
            if (slash >= 0 && slash < path.Length - 1) last = path.Substring(slash + 1);

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(t.name, last, StringComparison.OrdinalIgnoreCase)) return t;
            }

            return null;
        }

        /// <summary>Gets the component, adding it if absent. Equivalent to Unity's GetComponent ?? AddComponent.</summary>
        public static T AddOrGetComponent<T>(GameObject go) where T : Component
        {
            if (go == null) return null;
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }
    }
}
