using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using GHPC;
using GHPC.Weapons;
using HarmonyLib;
using UnityEngine;

namespace NatoEra
{
    /// <summary>
    /// Sets when a reactive armor brick is allowed to go off.
    ///
    /// [What vanilla does]
    /// LiveRound.penCheck tests `treeArmor.IsEra &amp;&amp; !treeArmor.IsDetonated` twice and
    /// calls Detonate() without consulting the round:
    ///
    ///   A) the "round was stopped" path  - reached only when the plate's line-of-sight RHA
    ///                                      beats CurrentPenRating, i.e. the round did NOT get
    ///                                      through, yet the brick still explodes. A 30 mm
    ///                                      autocannon round pops an ARAT brick exactly like an
    ///                                      APFSDS would.
    ///   B) the "round punched through" path - the brick explodes after being defeated, which
    ///                                      is correct and is left alone.
    ///
    /// [What this patch does]
    /// Path A is reached only when the round could not defeat the plate, so on its own the game
    /// has no way to tell a rifle bullet from a heavy round that simply bounced. This patch adds
    /// the missing test at the top of path A:
    ///
    ///     if (round.Caliber &gt;= CaliberThreshold)      -&gt; detonate
    ///     else if (round.CurrentPenRating &gt; PenThreshold) -&gt; detonate
    ///     else                                          -&gt; leave the brick intact
    ///
    /// A heavy round that hits and fails to penetrate therefore still sets the brick off, while
    /// small-calibre fire is ignored unless it carries enough penetration to matter. This is the
    /// same rule, inserted at the same place and with the same default thresholds, as the Fulda
    /// 1989 expansion uses, so both mods behave identically when an ERA brick is hit.
    ///
    /// [Where it is inserted]
    /// Right at the top of path A, after the `IsEra &amp;&amp; !IsDetonated` test has already
    /// passed, reusing the two existing jump targets that end the path. No new label is created
    /// for the skip path, so any exception handler table stays valid.
    ///
    /// Both thresholds are MelonPreferences entries; see Config.
    /// </summary>
    [HarmonyPatch(typeof(LiveRound), "penCheck")]
    public static class InsensitiveEraPatch
    {
        /// <summary>Only used in log lines, so the two passes (ARAT / BRAT) can be told apart.</summary>
        public static string Tag = "ARAT";

        /// <summary>Default from the Fulda 1989 expansion.</summary>
        public const float DefaultCaliberThreshold = 25f;

        /// <summary>Default from the Fulda 1989 expansion.</summary>
        public const float DefaultPenThreshold = 40f;

        /// <summary>
        /// Rounds at or above this calibre always set a brick off, even if they failed to
        /// penetrate it. Anything below falls through to the penetration test.
        /// </summary>
        public static float CaliberThreshold = DefaultCaliberThreshold;

        /// <summary>
        /// Rounds below <see cref="CaliberThreshold"/> still set a brick off when their
        /// penetration rating exceeds this value.
        /// </summary>
        public static float PenThreshold = DefaultPenThreshold;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            var list = new List<CodeInstruction>(instructions);

            try
            {
                return Build(list, il);
            }
            catch (Exception e)
            {
                MelonLoader.MelonLogger.Error("[NatoERA/" + Tag + "] transpiler exception, the ERA detonation rule was not changed (ERA itself is unaffected): " + e);
                return list;
            }
        }

        private static IEnumerable<CodeInstruction> Build(List<CodeInstruction> list, ILGenerator il)
        {
            var getIsEra = AccessTools.PropertyGetter(typeof(IArmor), "IsEra");
            var getPenRating = AccessTools.PropertyGetter(typeof(LiveRound), "CurrentPenRating");
            var infoField = AccessTools.Field(typeof(LiveRound), "Info");
            var caliberField = AccessTools.Field(typeof(AmmoType), "Caliber");
            var debugField = AccessTools.Field(typeof(LiveRound), "Debug");

            var missing = new List<string>();
            if (getIsEra == null) missing.Add("IArmor.IsEra");
            if (getPenRating == null) missing.Add("LiveRound.CurrentPenRating");
            if (infoField == null) missing.Add("LiveRound.Info");
            if (caliberField == null) missing.Add("AmmoType.Caliber");

            if (missing.Count > 0)
            {
                MelonLoader.MelonLogger.Error("[NatoERA/" + Tag + "] Failed to resolve required members, skipping transpiler. Missing: " + string.Join(", ", missing));
                return list;
            }

            // ------------------------------------------------------------------
            // Path A: the first IsEra test.
            // ------------------------------------------------------------------

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

            // The IsEra test is followed by `brfalse` (not an ERA brick) and `brtrue` on
            // IsDetonated (already spent). Both jump to the same place, which is exactly the
            // instruction to reach when the brick must be left intact. The detonation body
            // starts after that second branch.
            if (isEraIdx + 4 >= list.Count)
            {
                MelonLoader.MelonLogger.Error("[NatoERA/" + Tag + "] Unexpected IL layout after IsEra, skipping transpiler.");
                return list;
            }

            int secondBranchIdx = isEraIdx + 4;
            if (list[secondBranchIdx].opcode.FlowControl != FlowControl.Cond_Branch)
            {
                MelonLoader.MelonLogger.Error("[NatoERA/" + Tag + "] The IsDetonated branch was not found where expected (" + list[secondBranchIdx].opcode + "), skipping transpiler.");
                return list;
            }

            Label leaveLabel;
            if (list[secondBranchIdx].operand is Label lbl)
            {
                leaveLabel = lbl;
            }
            else
            {
                MelonLoader.MelonLogger.Error("[NatoERA/" + Tag + "] The IsDetonated branch has no label operand, skipping transpiler.");
                return list;
            }

            int leaveIdx = IndexOfLabel(list, leaveLabel);
            if (leaveIdx < 0)
            {
                MelonLoader.MelonLogger.Error("[NatoERA/" + Tag + "] The 'leave the brick intact' target could not be resolved, skipping transpiler.");
                return list;
            }

            // Where the detonation body starts: the first Debug field read after the guards.
            // That is the same anchor the Fulda 1989 expansion uses.
            int bodyIdx = -1;
            for (int i = isEraIdx; i < list.Count; i++)
            {
                if (list[i].opcode == OpCodes.Ldsfld && Equals(list[i].operand, debugField))
                {
                    bodyIdx = i;
                    break;
                }
            }

            if (bodyIdx < 0)
            {
                MelonLoader.MelonLogger.Error("[NatoERA/" + Tag + "] The start of the detonation body could not be located, skipping transpiler.");
                return list;
            }

            // ------------------------------------------------------------------
            // Emit the two-stage test in front of the detonation body:
            //
            //     if (Info.Caliber >= CaliberThreshold) goto body
            //     if (CurrentPenRating <= PenThreshold) goto leave
            //     body: <vanilla detonation>
            //     leave: ...
            //
            // The operands are consumed by each comparison, so the stack is balanced and the
            // vanilla body (which pushes its own treeArmor) is unaffected.
            // ------------------------------------------------------------------

            var bodyLabel = il.DefineLabel();

            var gate = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, infoField),
                new CodeInstruction(OpCodes.Ldfld, caliberField),
                new CodeInstruction(OpCodes.Ldc_R4, CaliberThreshold),
                new CodeInstruction(OpCodes.Bge_Un, bodyLabel),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, getPenRating),
                new CodeInstruction(OpCodes.Ldc_R4, PenThreshold),
                new CodeInstruction(OpCodes.Ble_Un, leaveLabel),
            };

            // Carry over whatever labels already pointed at the body - a branch from elsewhere
            // in penCheck must keep landing on the first vanilla instruction, not on our gate.
            var bodyLabels = new List<Label>(list[bodyIdx].labels) { bodyLabel };
            gate[0].labels = bodyLabels;

            list[bodyIdx].labels = new List<Label>();
            list.InsertRange(bodyIdx, gate);

            MelonLoader.MelonLogger.Msg("[NatoERA/" + Tag + "] ERA detonation rule installed at the top of the "
                + "'round was stopped' path: calibre >= " + CaliberThreshold.ToString("0.#")
                + " mm, or penetration > " + PenThreshold.ToString("0.#") + " mm. Body before @"
                + bodyIdx + ", skip to @" + (leaveIdx + gate.Count) + ".");

            return list;
        }

        /// <summary>
        /// Finds the instruction a label is attached to so indices can be compared.
        /// </summary>
        private static int IndexOfLabel(List<CodeInstruction> list, Label label)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var labels = list[i].labels;
                for (int j = 0; j < labels.Count; j++)
                {
                    if (labels[j].Equals(label)) return i;
                }
            }
            return -1;
        }
    }
}
