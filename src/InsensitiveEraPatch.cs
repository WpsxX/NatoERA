using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using GHPC;
using GHPC.Weapons;
using HarmonyLib;
using UnityEngine;

namespace NatoEra
{
    /// <summary>
    /// "Insensitive ERA" patch -- stops small-caliber rounds from unconditionally detonating reactive armor.
    ///
    /// Vanilla LiveRound.penCheck logic is: Detonate() whenever armor.IsEra,
    /// regardless of whether the incoming round is a 125mm HEAT shell or a 7.62mm machine gun bullet. That is not realistic --
    /// modern ERA has a minimum detonation threshold for both kinetic and chemical energy rounds.
    ///
    /// This patch inserts a gate into the ERA check inside penCheck:
    ///   caliber >= CaliberThreshold                                 -> detonate as usual (large-caliber rounds always trigger)
    ///   caliber <  CaliberThreshold and penetration <= PenThreshold -> skip detonation (small-caliber / low-penetration rounds do not fire it)
    ///   caliber <  CaliberThreshold and penetration >  PenThreshold -> fall through to the original detonation logic
    ///
    /// The two thresholds default to the values hard-coded by the original mod and can be overridden through
    /// the "NatoEra Caliber Threshold (mm)" / "NatoEra Penetration Threshold (mm)" preferences.
    ///
    /// About the insertion point: both the old NatoERA and FuldaExpansion1989 use `index of IArmor.get_IsEra + 5`
    /// as the insertion point. Fulda works on the new game build in testing, which means this offset still holds in the current IL layout.
    /// Rather than hardcoding +5, this linearly searches forward for the second `Ldsfld LiveRound.Debug`,
    /// which is semantically identical but does not fail silently when the compiler makes minor adjustments.
    /// </summary>
    [HarmonyPatch(typeof(LiveRound), "penCheck")]
    public static class InsensitiveEraPatch
    {
        /// <summary>Only ammo with penetration above this value (mm RHA) detonates ERA. Defaults to the original mod's fixed value.</summary>
        public static float PenThreshold = 10f;

        /// <summary>Ammo with caliber above this value (mm) unconditionally detonates ERA. Defaults to the original mod's fixed value.</summary>
        public static float CaliberThreshold = 20f;

        /// <summary>
        /// Label used to tell different instances apart; it only appears in logs.
        /// Each vehicle type can register its own patch instance (ARAT / BRAT have different thresholds).
        /// </summary>
        public static string Tag = "ARAT";

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            var list = new List<CodeInstruction>(instructions);

            try
            {
                return Build(list, il);
            }
            catch (Exception e)
            {
                MelonLoader.MelonLogger.Error("[NatoERA/" + Tag + "] transpiler exception, the ERA sensitivity feature was skipped (ERA itself is unaffected): " + e);
                return list;
            }
        }

        private static IEnumerable<CodeInstruction> Build(List<CodeInstruction> list, ILGenerator il)
        {
            // ---- Idempotency guard: avoid double injection when one method is patched by several patch instances ----
            // Detect whether our own injected Ldc_R4 CaliberThreshold instruction is already present.
            if (list.Any(i => i.opcode == OpCodes.Ldc_R4 && i.operand is float f && Mathf.Approximately(f, CaliberThreshold)))
            {
                MelonLoader.MelonLogger.Warning("[NatoERA/" + Tag + "] penCheck was already injected (caliber threshold " + CaliberThreshold + "), skipping duplicate patch.");
                return list;
            }

            // ---- Resolve the members to reference ----
            var getIsEra = AccessTools.PropertyGetter(typeof(IArmor), "IsEra");
            var getPenRating = AccessTools.PropertyGetter(typeof(LiveRound), "CurrentPenRating");
            var debugField = AccessTools.Field(typeof(LiveRound), "Debug");
            var infoField = AccessTools.Field(typeof(LiveRound), "Info");
            var caliberField = AccessTools.Field(typeof(AmmoType), "Caliber");

            var missing = new List<string>();
            if (getIsEra == null) missing.Add("IArmor.IsEra");
            if (getPenRating == null) missing.Add("LiveRound.CurrentPenRating");
            if (debugField == null) missing.Add("LiveRound.Debug");
            if (infoField == null) missing.Add("LiveRound.Info");
            if (caliberField == null) missing.Add("AmmoType.Caliber");

            if (missing.Count > 0)
            {
                MelonLoader.MelonLogger.Error("[NatoERA/" + Tag + "] Failed to resolve required members, skipping transpiler. Missing: " + string.Join(", ", missing));
                return list;
            }

            // ---- Locate the IArmor.get_IsEra call ----
            int isEraIdx = -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].opcode == OpCodes.Callvirt && Equals(list[i].operand, getIsEra))
                {
                    isEraIdx = i;
                    break;
                }
            }

            if (isEraIdx < 0)
            {
                MelonLoader.MelonLogger.Error("[NatoERA/" + Tag + "] IsEra call not found in penCheck, skipping transpiler.");
                return list;
            }

            // ---- Search for two LiveRound.Debug anchors after IsEra ----
            // After the first Debug comes the "detonate" branch (exec); after the second Debug comes the exit that skips detonation (endof).
            int execIdx = -1;
            int endofIdx = -1;
            int debugSeen = 0;

            for (int i = isEraIdx; i < list.Count; i++)
            {
                if (list[i].opcode != OpCodes.Ldsfld || !Equals(list[i].operand, debugField)) continue;

                debugSeen++;
                if (debugSeen == 1) execIdx = i;
                else if (debugSeen == 2) { endofIdx = i; break; }
            }

            if (execIdx < 0 || endofIdx < 0)
            {
                MelonLoader.MelonLogger.Error("[NatoERA/" + Tag + "] Could not find the LiveRound.Debug anchors (found "
                    + debugSeen + "), skipping transpiler. penCheck instruction count=" + list.Count);
                return list;
            }

            var labelExec = il.DefineLabel();
            var labelEnd = il.DefineLabel();

            list[execIdx].labels.Add(labelExec);
            list[endofIdx].labels.Add(labelEnd);

            // ---- Inject gate ----
            var injected = new List<CodeInstruction>
            {
                // if (this.Info.Caliber >= CaliberThreshold) goto exec;
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, infoField),
                new CodeInstruction(OpCodes.Ldfld, caliberField),
                new CodeInstruction(OpCodes.Ldc_R4, CaliberThreshold),
                new CodeInstruction(OpCodes.Bge_S, labelExec),

                // if (this.CurrentPenRating <= PenThreshold) goto endof;
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, getPenRating),
                new CodeInstruction(OpCodes.Ldc_R4, PenThreshold),
                new CodeInstruction(OpCodes.Ble_Un_S, labelEnd),
            };

            // Inserted after the IsEra check: the original IL has a few instructions such as brfalse after Callvirt IsEra,
            // so inserting before the exec anchor is equivalent to the "...confirmed it is ERA" point.
            list.InsertRange(execIdx, injected);

            MelonLoader.MelonLogger.Msg("[NatoERA/" + Tag + "] penCheck transpiler succeeded: IsEra@" + isEraIdx
                + " exec@" + execIdx + " endof@" + endofIdx
                + " | caliber threshold=" + CaliberThreshold + "mm penetration threshold=" + PenThreshold + "mm");
            return list;
        }
    }
}
