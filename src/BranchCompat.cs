using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace NatoEra
{
    /// <summary>
    /// Compatibility shim for the M1A1 Abrams family of mods.
    ///
    /// [Why this exists]
    /// 20261014 removed parts of the M1 skeleton. Three mods still walk the old tree and
    /// dereference whatever Find() returned without checking it first:
    ///
    ///   M1A1Abrams           1.3.3B2  (ATLAS)                 HandleConversion @199
    ///   M1A1Abrams-AMP       2.7.10   ("ZZ M1A1 Abrams AMP")  HandleConversion @452
    ///   M1A1Abrams-CROWSFIX  1.4.7    (ATLAS, WPSX)           HandleConversion @394
    ///
    /// All three declare the same type and method:
    ///   namespace M1A1Abrams, class M1A1 : Module,
    ///   private static void HandleConversion(Vehicle vic)
    ///
    /// so one shim can serve all of them without touching their sources.
    ///
    /// [What actually breaks in 20261014]
    /// A byte scan of every file under GHPC_Data (506 files) shows:
    ///   present : Turret Scripts, GPS, Optic, gun_recoil, Aux sight (GAS), M1 camo net,
    ///             Gun Breech.001, turret_gun, M1IP_mesh, M1IP TURRET FOLLOW
    ///   gone    : M1A0_mesh, M1A0_turret_armour, M1 TURRET FOLLOW
    ///
    /// So the IPM1_rig path those mods use for M1IP vehicles is still intact. What fails is
    /// the M1 (non-IP) branch: inside "if (cfg_m256)" they rename the vehicle, rewrite the
    /// gun name, Impulse, CodexEntry and sound, spawn a "dummy tube", and only then look up
    /// "M1A0_mesh/M1A0 turret" to swap the recoil bone. That lookup returns null on this
    /// build, and the NullReferenceException tears the whole conversion loop down.
    ///
    /// [Why swallowing the exception is not enough]
    /// By the time it throws, the vehicle has already been renamed to "... SEP" and its
    /// WeaponSystemInfo / CodexEntry already point at the 120mm M256 - with the 105mm model
    /// and the old ammo rack still in place. A catch-and-continue turns that into a visibly
    /// broken half conversion, which is worse than not converting at all.
    ///
    /// [What this does]
    /// Prefix - read only, runs before anything is modified. When the vehicle is provably
    ///          going to fail (missing optic branch, or M1 with M256(M1) enabled but no
    ///          M1A0_mesh), it returns false and the whole method is skipped. The vehicle
    ///          stays exactly vanilla: same name, same gun, same ammo.
    /// Finalizer - catches whatever is left, logs it with the vehicle name, and lets the
    ///          caller continue with the next vehicle instead of aborting the scene.
    ///
    /// Nothing is written to the vehicle by this class, no preference is added, and when no
    /// M1A1Abrams branch is installed the whole thing reports that and stays out of the way.
    /// </summary>
    public static class BranchCompat
    {
        private const string HarmonyId = "com.wpsxx.natoera.branchcompat";
        private const string TargetTypeName = "M1A1Abrams.M1A1";
        private const string TargetMethodName = "HandleConversion";

        /// <summary>Assembly names already patched, so Init() and Refresh() cannot double-patch.</summary>
        private static readonly HashSet<string> _patched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Assemblies whose patch attempt threw. Retried once per session at most, so a
        /// permanently broken target cannot spam one warning per scene load.</summary>
        private static readonly HashSet<string> _failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Log de-duplication, capped so a bad scene cannot flood the log.</summary>
        private static readonly HashSet<string> _logged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private const int MaxLogLines = 64;

        private static HarmonyLib.Harmony _harmony;
        private static MethodInfo _prefixMethod;
        private static MethodInfo _finalizerMethod;

        /// <summary>Name of the vehicle currently inside HandleConversion, used by the finalizer.</summary>
        private static string _currentVehicle = "?";

        private static bool _reportedNone;

        // =====================================================================
        // Entry points
        // =====================================================================

        /// <summary>Called from NatoEra.OnInitializeMelon.</summary>
        public static void Init()
        {
            try
            {
                if (_prefixMethod == null)
                {
                    _prefixMethod = typeof(BranchCompat).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
                    _finalizerMethod = typeof(BranchCompat).GetMethod("Finalizer", BindingFlags.Static | BindingFlags.NonPublic);
                }

                if (_prefixMethod == null || _finalizerMethod == null)
                {
                    MelonLogger.Warning("[NatoERA/Compat] shim methods not found; branch compatibility disabled.");
                    return;
                }

                if (_harmony == null) _harmony = new HarmonyLib.Harmony(HarmonyId);

                PatchAll();
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[NatoERA/Compat] init failed, M1A1Abrams branches are left untouched: " + e.Message);
            }
        }

        /// <summary>
        /// Called from NatoEra.OnSceneWasLoaded. Covers the case where an M1A1Abrams branch is
        /// initialised after this mod, or is loaded lazily by another loader.
        /// </summary>
        public static void Refresh()
        {
            try
            {
                if (_harmony == null || _prefixMethod == null) return;
                PatchAll();
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[NatoERA/Compat] refresh failed: " + e.Message);
            }
        }

        // =====================================================================
        // Patching
        // =====================================================================

        private static void PatchAll()
        {
            int patchedNow = 0;
            bool sawType = false;

            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type target = FindTargetType(asm);
                if (target == null) continue;

                sawType = true;

                string asmName = asm.GetName().Name;
                if (_patched.Contains(asmName) || _failed.Contains(asmName)) continue;

                MethodInfo original = FindHandleConversion(target);
                if (original == null)
                {
                    MelonLogger.Warning("[NatoERA/Compat] " + asmName
                        + ": type M1A1Abrams.M1A1 found but HandleConversion(Vehicle) is missing - skipped.");
                    _patched.Add(asmName);
                    continue;
                }

                try
                {
                    // Named arguments on purpose: the positional 5-parameter overload is
                    // [Obsolete(error)] in Harmony 2.3 (MelonLoader 0.7) while the named form
                    // exists in both 2.2 (0.6.1) and 2.3 (0.7), so this compiles against either.
                    _harmony.Patch(
                        original,
                        prefix: new HarmonyMethod(_prefixMethod),
                        finalizer: new HarmonyMethod(_finalizerMethod));

                    _patched.Add(asmName);
                    patchedNow++;
                    MelonLogger.Msg("[NatoERA/Compat] " + asmName + " (" + DescribeMod(asm)
                        + "): HandleConversion guarded (prefix + finalizer).");
                }
                catch (Exception e)
                {
                    _failed.Add(asmName);
                    MelonLogger.Warning("[NatoERA/Compat] " + asmName + ": patch failed, branch left alone - " + e.Message);
                }
            }

            if (!sawType && !_reportedNone)
            {
                _reportedNone = true;
                MelonLogger.Msg("[NatoERA/Compat] no M1A1Abrams branch loaded yet (re-checked on every scene load).");
            }
            else if (patchedNow > 0)
            {
                MelonLogger.Msg("[NatoERA/Compat] guarded " + patchedNow + " M1A1Abrams branch(es) this pass.");
            }
        }

        private static Type FindTargetType(Assembly asm)
        {
            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types;
            }
            catch (Exception)
            {
                return null;
            }

            if (types == null) return null;

            foreach (Type t in types)
            {
                if (t != null && string.Equals(t.FullName, TargetTypeName, StringComparison.Ordinal))
                    return t;
            }
            return null;
        }

        private static MethodInfo FindHandleConversion(Type target)
        {
            try
            {
                MethodInfo[] methods = target.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                foreach (MethodInfo m in methods)
                {
                    if (!string.Equals(m.Name, TargetMethodName, StringComparison.Ordinal)) continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length == 1) return m;
                }
            }
            catch (Exception)
            {
                // A half-loaded assembly is not a reason to fail the whole shim.
            }
            return null;
        }

        private static string DescribeMod(Assembly asm)
        {
            try
            {
                object[] attrs = asm.GetCustomAttributes(typeof(MelonInfoAttribute), false);
                if (attrs != null && attrs.Length > 0)
                {
                    MelonInfoAttribute info = attrs[0] as MelonInfoAttribute;
                    if (info != null) return info.Name + " " + info.Version;
                }
            }
            catch (Exception)
            {
                // Fall through to the assembly name.
            }
            return "unknown version";
        }

        // =====================================================================
        // Harmony hooks
        // =====================================================================

        /// <summary>
        /// Runs before the branch's own method body. Returns false to skip it entirely.
        /// Read only: nothing on the vehicle is touched here.
        /// </summary>
        private static bool Prefix(object[] __args, MethodBase __originalMethod)
        {
            _currentVehicle = "?";

            try
            {
                Component vic = (__args != null && __args.Length > 0) ? __args[0] as Component : null;
                if (vic == null) return true;

                // The vehicle is a GHPC type, so vic.GetType().Assembly is Assembly-CSharp. The
                // assembly that matters is the one declaring the patched method - the branch - and
                // only __originalMethod knows it. Reading the branch preferences through the
                // vehicle would silently fall back to defaults and defeat the whole check.
                Assembly ownerAsm = (__originalMethod != null && __originalMethod.DeclaringType != null)
                    ? __originalMethod.DeclaringType.Assembly
                    : null;
                if (ownerAsm == null) return true;
                string ownerName = ownerAsm.GetName().Name;

                string friendly = ReadString(vic, "FriendlyName");
                string unique = ReadString(vic, "UniqueName");
                _currentVehicle = (friendly ?? "?") + "/" + (unique ?? "?");

                // 1) The optic branch every one of the three mods dereferences first. If it is
                //    gone the method cannot get past its first statement, so skipping here keeps
                //    the vehicle pristine instead of letting it throw.
                Transform optic = FindPath(vic.transform, "IPM1_rig/HULL/TURRET/Turret Scripts/GPS/Optic");
                if (optic == null)
                {
                    LogOnce(ownerName + "|optic|" + _currentVehicle,
                        "[NatoERA/Compat] skip " + _currentVehicle + " on " + ownerName
                        + ": IPM1_rig/HULL/TURRET/Turret Scripts/GPS/Optic is missing on this build.");
                    return false;
                }

                // 2) The recoil-bone swap. Line 333/657/622 picks the turret mesh by UniqueName and
                //    then dereferences it without a null check, *after* the vehicle has already been
                //    renamed, re-gunned (Name / Impulse / CodexEntry / sound) and given a dummy tube.
                //    Failing there leaves a vehicle that claims to be a 120mm M1A1 while still holding
                //    the 105mm model and the old ammo rack, which is worse than not converting at all.
                //
                //    "M1A0_mesh/M1A0 turret" is gone in 20261014; "M1IP_mesh/M1IP_turret" is still
                //    there. Each branch only walks that block when its own M256 entry is on, so the
                //    check is gated the same way instead of skipping every M1 unconditionally.
                Assembly asm = ownerAsm;
                string asmName = ownerName;
                bool isM1 = string.Equals(unique, "M1", StringComparison.Ordinal);

                // A preset bundle overrides the per-vehicle M256 switch at runtime (line 214/552/486:
                // cfg_m256 = preset.M256Gun). The preset object is a local inside the branch method,
                // so it cannot be read from here - if presets are enabled, assume the switch may be on.
                bool presetPool = ReadBoolPreference(asm, "use_preset_pool", false);

                if (isM1)
                {
                    bool wantsM256 = ReadBoolPreference(asm, "has_m256_m1", false) || presetPool;
                    if (wantsM256 && FindPath(vic.transform, "M1A0_mesh/M1A0 turret") == null)
                    {
                        LogOnce(asmName + "|m1a0|" + _currentVehicle,
                            "[NatoERA/Compat] skip " + _currentVehicle + " on " + asmName
                            + ": M1A0_mesh/M1A0 turret no longer exists on this game build."
                            + (presetPool ? " (preset bundle in use, M256 assumed enabled)" : "")
                            + " Converting would rename and re-gun the vehicle and then throw, so it is"
                            + " left exactly as the game spawned it.");
                        return false;
                    }
                }
                else
                {
                    // Same shape for the M1IP side: if M1IP_mesh ever disappears this catches it
                    // instead of letting the branch mutate the vehicle first.
                    bool wantsM256 = ReadBoolPreference(asm, "has_m256_m1ip", true) || presetPool;
                    if (wantsM256 && FindPath(vic.transform, "M1IP_mesh/M1IP_turret") == null)
                    {
                        LogOnce(asmName + "|m1ip|" + _currentVehicle,
                            "[NatoERA/Compat] skip " + _currentVehicle + " on " + asmName
                            + ": M1IP_mesh/M1IP_turret is missing on this game build; vehicle left untouched.");
                        return false;
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                // A guard must never be the reason a conversion stops.
                MelonLogger.Warning("[NatoERA/Compat] prefix check failed, running the branch unchanged: " + e.Message);
                return true;
            }
        }

        /// <summary>
        /// Catches whatever the branch's own body still throws, so one vehicle cannot abort the
        /// conversion loop for the rest of the scene.
        /// </summary>
        private static Exception Finalizer(Exception __exception)
        {
            if (__exception == null) return null;

            try
            {
                LogOnce("throw|" + _currentVehicle + "|" + __exception.GetType().Name + "|" + __exception.Message,
                    "[NatoERA/Compat] HandleConversion threw on " + _currentVehicle + ": "
                    + __exception.GetType().Name + ": " + __exception.Message
                    + " - that vehicle is only partly converted, re-enter the mission to get a clean state.");
            }
            catch (Exception)
            {
                // Never mask the original failure with a logging failure.
            }

            return null;
        }

        // =====================================================================
        // Reflection helpers (no compile-time dependency on the other mods)
        // =====================================================================

        private static Transform FindPath(Transform root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path)) return null;
            return root.Find(path);
        }

        private static string ReadString(object target, string propertyName)
        {
            try
            {
                PropertyInfo p = target.GetType().GetProperty(propertyName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p == null) return null;
                object v = p.GetValue(target, null);
                return v as string;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Reads a MelonPreferences_Entry&lt;bool&gt; static field from the branch assembly.
        /// Falls back to <paramref name="fallback"/> when the field or property is unavailable,
        /// which is the conservative answer: assume the branch would have run.
        /// </summary>
        private static bool ReadBoolPreference(Assembly asm, string fieldName, bool fallback)
        {
            try
            {
                Type t = FindTargetType(asm);
                if (t == null) return fallback;

                FieldInfo f = t.GetField(fieldName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (f == null) return fallback;

                object entry = f.GetValue(null);
                if (entry == null) return fallback;

                PropertyInfo valueProp = entry.GetType().GetProperty("Value",
                    BindingFlags.Instance | BindingFlags.Public);
                if (valueProp == null) return fallback;

                object v = valueProp.GetValue(entry, null);
                if (v is bool) return (bool)v;
            }
            catch (Exception)
            {
                // Unreadable preference: keep the conservative assumption.
            }
            return fallback;
        }

        private static void LogOnce(string key, string message)
        {
            if (_logged.Count >= MaxLogLines) return;
            if (!_logged.Add(key)) return;
            MelonLogger.Warning(message);
        }
    }
}
