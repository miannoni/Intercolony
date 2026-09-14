using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Lets active Intercolony employees use vanilla apparel policies and apparel optimization
    /// while keeping every other quest lodger restricted.
    ///
    /// These checks need transpilers rather than postfixes. DoCell is void, so a postfix cannot
    /// make its skipped else branch run without reimplementing the dropdown. TryGiveJob's
    /// postfix receives the early null result, with no way to resume the optimizer logic it
    /// skipped. Both approaches would reimplement vanilla instead of only removing its gate.
    /// </summary>
    [HarmonyPatch]
    public static class EmployeeApparelPatch
    {
        /// <summary>
        /// Returns true only for a quest lodger who is not an active Intercolony employee.
        /// </summary>
        internal static bool IsRestrictedQuestLodger(Pawn pawn)
        {
            return pawn != null && pawn.IsQuestLodger() && !EmploymentService.IsEmployee(pawn);
        }

        [HarmonyPatch(
            typeof(PawnColumnWorker_Outfit),
            nameof(PawnColumnWorker_Outfit.DoCell),
            new[] { typeof(Rect), typeof(Pawn), typeof(PawnTable) })]
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> DoCellTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            if (instructions == null)
            {
                return instructions;
            }

            List<CodeInstruction> original = null;
            try
            {
                original = new List<CodeInstruction>(instructions);
                MethodInfo questLodgerMethod = AccessTools.Method(
                    typeof(QuestUtility), nameof(QuestUtility.IsQuestLodger), new[] { typeof(Pawn) });
                MethodInfo helperMethod = AccessTools.Method(
                    typeof(EmployeeApparelPatch), nameof(IsRestrictedQuestLodger), new[] { typeof(Pawn) });

                if (questLodgerMethod == null || helperMethod == null)
                {
                    throw new MissingMethodException(
                        "Could not resolve the QuestUtility.IsQuestLodger or employee apparel helper method.");
                }

                List<int> replacementIndexes = new List<int>();
                for (int i = 0; i < original.Count; i++)
                {
                    if (original[i].operand is MethodInfo calledMethod && calledMethod == questLodgerMethod)
                    {
                        replacementIndexes.Add(i);
                    }
                }

                if (replacementIndexes.Count != 1)
                {
                    IntercolonyLog.Error(
                        "Employee apparel transpiler for PawnColumnWorker_Outfit.DoCell expected " +
                        "exactly one QuestUtility.IsQuestLodger call but found " +
                        replacementIndexes.Count + ". Leaving vanilla unchanged.");
                    // A silent mismatch is indistinguishable from success; this project has been
                    // bitten by exactly that invisible failure shape before.
                    return original;
                }

                int replacementIndex = replacementIndexes[0];
                List<CodeInstruction> patched = new List<CodeInstruction>(original.Count);
                for (int i = 0; i < original.Count; i++)
                {
                    CodeInstruction instruction = new CodeInstruction(original[i]);
                    if (i == replacementIndex)
                    {
                        instruction.operand = helperMethod;
                    }

                    patched.Add(instruction);
                }

                return patched;
            }
            catch (Exception ex)
            {
                IntercolonyLog.Error(
                    "Failed to patch PawnColumnWorker_Outfit.DoCell; leaving vanilla unchanged: " + ex);
                return original ?? instructions;
            }
        }

        [HarmonyPatch(typeof(JobGiver_OptimizeApparel), "TryGiveJob", new[] { typeof(Pawn) })]
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> TryGiveJobTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            if (instructions == null)
            {
                return instructions;
            }

            List<CodeInstruction> original = null;
            try
            {
                original = new List<CodeInstruction>(instructions);
                MethodInfo questLodgerMethod = AccessTools.Method(
                    typeof(QuestUtility), nameof(QuestUtility.IsQuestLodger), new[] { typeof(Pawn) });
                MethodInfo helperMethod = AccessTools.Method(
                    typeof(EmployeeApparelPatch), nameof(IsRestrictedQuestLodger), new[] { typeof(Pawn) });

                if (questLodgerMethod == null || helperMethod == null)
                {
                    throw new MissingMethodException(
                        "Could not resolve the QuestUtility.IsQuestLodger or employee apparel helper method.");
                }

                List<int> replacementIndexes = new List<int>();
                for (int i = 0; i < original.Count; i++)
                {
                    if (original[i].operand is MethodInfo calledMethod && calledMethod == questLodgerMethod)
                    {
                        replacementIndexes.Add(i);
                    }
                }

                if (replacementIndexes.Count != 1)
                {
                    IntercolonyLog.Error(
                        "Employee apparel transpiler for JobGiver_OptimizeApparel.TryGiveJob expected " +
                        "exactly one QuestUtility.IsQuestLodger call but found " +
                        replacementIndexes.Count + ". Leaving vanilla unchanged.");
                    // A silent mismatch is indistinguishable from success; this project has been
                    // bitten by exactly that invisible failure shape before.
                    return original;
                }

                int replacementIndex = replacementIndexes[0];
                List<CodeInstruction> patched = new List<CodeInstruction>(original.Count);
                for (int i = 0; i < original.Count; i++)
                {
                    CodeInstruction instruction = new CodeInstruction(original[i]);
                    if (i == replacementIndex)
                    {
                        instruction.operand = helperMethod;
                    }

                    patched.Add(instruction);
                }

                return patched;
            }
            catch (Exception ex)
            {
                IntercolonyLog.Error(
                    "Failed to patch JobGiver_OptimizeApparel.TryGiveJob; leaving vanilla unchanged: " + ex);
                return original ?? instructions;
            }
        }
    }
}
