using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Intercolony
{
    /// <summary>
    /// Lets active Intercolony employees use vanilla apparel policies and apparel optimization
    /// while keeping every other quest lodger restricted.
    ///
    /// The two vanilla gates need transpilers rather than postfixes. DoCell is void, so a postfix
    /// cannot make its skipped else branch run without reimplementing the dropdown. The consent
    /// postfix below is different: it sees the job vanilla already decided to return and can
    /// suppress that job without reimplementing the optimizer.
    /// </summary>
    [HarmonyPatch]
    public static class EmployeeApparelPatch
    {
        private static readonly HashSet<int> contractsBeingAsked = new HashSet<int>();
        private static IntercolonyWorldComponent askingWorld;
        private static bool consentPostfixErrorLogged;

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

        [HarmonyPatch(typeof(JobGiver_OptimizeApparel), "TryGiveJob", new[] { typeof(Pawn) })]
        [HarmonyPostfix]
        private static void TryGiveJobPostfix(Pawn pawn, ref Job __result)
        {
            Job originalResult = __result;
            try
            {
                ResetTransientConsentState();

                if (!EmploymentService.IsEmployee(pawn) || __result == null)
                {
                    return;
                }

                EmploymentContract contract = EmploymentService.GetActiveContract(pawn);
                if (contract == null)
                {
                    return;
                }

                Apparel removedApparel = FindRefundableApparelRemovedByJob(
                    pawn, __result, contract, out EmploymentEquipmentRecord record);
                if (removedApparel == null || record == null)
                {
                    return;
                }

                if (contract.apparelBondDecision == ApparelBondDecision.Allowed)
                {
                    return;
                }

                // Denial is intentionally a silent hot-path decision. The optimizer will ask
                // again later, but no card, message, or log entry is produced here.
                if (contract.apparelBondDecision == ApparelBondDecision.Denied)
                {
                    __result = null;
                    return;
                }

                if (contract.apparelBondDecision != ApparelBondDecision.Pending)
                {
                    return;
                }

                // A pending card suppresses every matching pass while it is open, including a
                // pass that would identify a different original apparel item on the same worker.
                if (contractsBeingAsked.Contains(contract.id))
                {
                    __result = null;
                    return;
                }

                int bondAtRisk = BondAtRiskFor(contract, removedApparel, record);
                contractsBeingAsked.Add(contract.id);
                try
                {
                    Find.WindowStack.Add(new Dialog_ApparelBondConsent(
                        contract,
                        pawn,
                        removedApparel,
                        bondAtRisk,
                        () => contractsBeingAsked.Remove(contract.id)));
                    __result = null;
                }
                catch
                {
                    contractsBeingAsked.Remove(contract.id);
                    throw;
                }
            }
            catch (Exception ex)
            {
                // A job-giver postfix must never break apparel optimization for the colony.
                __result = originalResult;
                LogConsentPostfixErrorOnce(ex);
            }
        }

        private static Apparel FindRefundableApparelRemovedByJob(
            Pawn pawn,
            Job job,
            EmploymentContract contract,
            out EmploymentEquipmentRecord matchingRecord)
        {
            matchingRecord = null;
            if (pawn?.apparel == null || job == null || contract == null)
            {
                return null;
            }

            if (job.def == JobDefOf.RemoveApparel)
            {
                Apparel target = job.targetA.Thing as Apparel;
                if (target == null || !pawn.apparel.WornApparel.Contains(target))
                {
                    return null;
                }

                matchingRecord = FindRefundableRecord(contract, target);
                return matchingRecord == null ? null : target;
            }

            if (job.def != JobDefOf.Wear)
            {
                return null;
            }

            Apparel newApparel = job.targetA.Thing as Apparel;
            if (newApparel?.def?.apparel == null || pawn.RaceProps?.body == null)
            {
                return null;
            }

            // JobDriver_Wear scans in reverse order and removes every worn item that cannot be
            // worn with its target. Keep the same order and find the first such original item.
            List<Apparel> wornApparel = pawn.apparel.WornApparel;
            for (int i = wornApparel.Count - 1; i >= 0; i--)
            {
                Apparel candidate = wornApparel[i];
                if (candidate?.def?.apparel == null || ApparelUtility.CanWearTogether(
                        newApparel.def, candidate.def, pawn.RaceProps.body))
                {
                    continue;
                }

                matchingRecord = FindRefundableRecord(contract, candidate);
                if (matchingRecord != null)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static EmploymentEquipmentRecord FindRefundableRecord(
            EmploymentContract contract, Apparel apparel)
        {
            if (contract?.arrivedEquipment == null || apparel == null || apparel.Destroyed ||
                apparel.def == null)
            {
                return null;
            }

            List<EmploymentEquipmentRecord> records = contract.arrivedEquipment;
            for (int i = 0; i < records.Count; i++)
            {
                EmploymentEquipmentRecord record = records[i];
                if (record != null && record.RefundableQuantity > 0 && Matches(record, apparel))
                {
                    return record;
                }
            }

            return null;
        }

        // EmploymentEquipmentService.Matches is private, so keep this predicate identical to the
        // settlement matcher while the allowed file scope for this unit remains narrow.
        private static bool Matches(EmploymentEquipmentRecord record, Apparel apparel)
        {
            if (record == null || apparel == null || apparel.Destroyed || apparel.def == null ||
                apparel.def != record.thingDef || apparel.Stuff != record.stuffDef)
            {
                return false;
            }

            QualityCategory? quality = null;
            if (apparel.TryGetQuality(out QualityCategory observedQuality))
            {
                quality = observedQuality;
            }

            return quality == record.quality;
        }

        private static int BondAtRiskFor(
            EmploymentContract contract,
            Apparel apparel,
            EmploymentEquipmentRecord record)
        {
            int quantity = Mathf.Min(
                Mathf.Max(1, apparel?.stackCount ?? 1),
                Mathf.Max(1, record.RefundableQuantity));
            EmploymentEquipmentRecord itemRecord = new EmploymentEquipmentRecord
            {
                thingDef = record.thingDef,
                stuffDef = record.stuffDef,
                quality = record.quality,
                unitValue = record.unitValue,
                quantity = quantity
            };

            // BondFor is the same replacement-value-plus-premium path used by the hire quote.
            // A one-item record makes the displayed amount the share attached to this apparel.
            int itemBond = EmploymentEquipmentService.BondFor(
                new List<EmploymentEquipmentRecord> { itemRecord });
            return Mathf.Clamp(itemBond, 0, Mathf.Max(0, contract?.equipmentBond ?? 0));
        }

        private static void ResetTransientConsentState()
        {
            IntercolonyWorldComponent currentWorld = IntercolonyWorldComponent.Current;
            if (ReferenceEquals(currentWorld, askingWorld))
            {
                return;
            }

            // This set is intentionally transient. A newly loaded world must not inherit a
            // suppression entry for a dialog that no longer exists in the window stack.
            contractsBeingAsked.Clear();
            askingWorld = currentWorld;
        }

        private static void LogConsentPostfixErrorOnce(Exception ex)
        {
            if (consentPostfixErrorLogged)
            {
                return;
            }

            consentPostfixErrorLogged = true;
            IntercolonyLog.Error(
                "Failed to enforce employee apparel consent; leaving vanilla job unchanged: " + ex);
        }
    }
}
