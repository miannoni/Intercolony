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
    ///
    /// Known uncovered route: the Odyssey outfit stand transfers gear directly in
    /// reference/decompiled/RimWorld/JobDriver_UseOutfitStand.cs:81 (DoTransfer).
    /// </summary>
    [HarmonyPatch]
    public static class EmployeeApparelPatch
    {
        private static readonly HashSet<int> contractsBeingAsked = new HashSet<int>();
        private static readonly Dictionary<EmploymentContract, HashSet<Thing>>
            approvedForcedReleases = new Dictionary<EmploymentContract, HashSet<Thing>>();
        private static readonly HashSet<Job> orderedJobReissueBypasses = new HashSet<Job>();
        private static IntercolonyWorldComponent askingWorld;
        private static bool consentPostfixErrorLogged;
        private static bool apparelDropPostfixErrorLogged;
        private static bool orderedJobPrefixErrorLogged;
        private static bool stripDesignatorPrefixErrorLogged;

        private sealed class ForcedReleaseItem
        {
            internal readonly Thing item;
            internal readonly EmploymentEquipmentRecord record;

            internal ForcedReleaseItem(Thing item, EmploymentEquipmentRecord record)
            {
                this.item = item;
                this.record = record;
            }
        }

        /// <summary>
        /// Returns true only for a quest lodger who is not an active Intercolony employee.
        /// </summary>
        internal static bool IsRestrictedQuestLodger(Pawn pawn)
        {
            return pawn != null && pawn.IsQuestLodger() && !EmploymentService.IsEmployee(pawn);
        }

        /// <summary>
        /// Reserves one exact item release for the forced-drop flow. This is deliberately
        /// transient: the caller must approve the item again after a world change.
        /// </summary>
        internal static void ApproveOneForcedRelease(EmploymentContract contract, Thing item)
        {
            ResetTransientConsentState();
            if (contract == null || item == null)
            {
                return;
            }

            if (!approvedForcedReleases.TryGetValue(
                    contract, out HashSet<Thing> approvedItems))
            {
                approvedItems = new HashSet<Thing>();
                approvedForcedReleases.Add(contract, approvedItems);
            }

            approvedItems.Add(item);
        }

        private static bool IsForcedRemovalJob(JobDef jobDef)
        {
            return jobDef == JobDefOf.DropEquipment ||
                jobDef == JobDefOf.RemoveApparel ||
                jobDef == JobDefOf.Equip ||
                jobDef == JobDefOf.Wear ||
                jobDef == JobDefOf.ForceTargetWear ||
                jobDef == JobDefOf.Strip;
        }

        [HarmonyPatch(
            typeof(Pawn_JobTracker),
            nameof(Pawn_JobTracker.TryTakeOrderedJob),
            new[] { typeof(Job), typeof(Nullable<JobTag>), typeof(bool) })]
        [HarmonyPrefix]
        private static bool TryTakeOrderedJobPrefix(
            Pawn_JobTracker __instance,
            Pawn ___pawn,
            Job job,
            JobTag? tag,
            bool requestQueueing)
        {
            // TryTakeOrderedJob is on the hot path for every ordered job. The def check must be
            // the only work for unrelated jobs; vanilla sets job.playerForced after this prefix.
            if (!IsForcedRemovalJob(job?.def))
            {
                return true;
            }

            try
            {
                ResetTransientConsentState();
                if (job == null || orderedJobReissueBypasses.Contains(job))
                {
                    return true;
                }

                if (!TryFindForcedReleaseItems(
                        ___pawn,
                        job,
                        out Pawn employee,
                        out EmploymentContract contract,
                        out List<ForcedReleaseItem> affectedItems))
                {
                    return true;
                }

                if (contractsBeingAsked.Contains(contract.id))
                {
                    return false;
                }

                int bondAtRisk = BondAtRiskFor(contract, affectedItems);
                contractsBeingAsked.Add(contract.id);
                try
                {
                    if (job.def == JobDefOf.Strip)
                    {
                        Find.WindowStack.Add(new Dialog_ForcedApparelReleaseConsent(
                            employee,
                            bondAtRisk,
                            () => ConfirmOrderedForcedRelease(
                                __instance, job, tag, requestQueueing, contract, affectedItems),
                            () => contractsBeingAsked.Remove(contract.id)));
                    }
                    else
                    {
                        Find.WindowStack.Add(new Dialog_ForcedApparelReleaseConsent(
                            employee,
                            BuildAffectedItemLabel(affectedItems),
                            bondAtRisk,
                            () => ConfirmOrderedForcedRelease(
                                __instance, job, tag, requestQueueing, contract, affectedItems),
                            () => contractsBeingAsked.Remove(contract.id)));
                    }

                    return false;
                }
                catch
                {
                    contractsBeingAsked.Remove(contract.id);
                    throw;
                }
            }
            catch (Exception ex)
            {
                // A broken veto must never break the job tracker; failing open costs a bond share,
                // while failing closed can strand the colony's ordered-job flow.
                LogOrderedJobPrefixErrorOnce(ex);
                return true;
            }
        }

        [HarmonyPatch(
            typeof(Designator_Strip),
            nameof(Designator_Strip.DesignateThing),
            new[] { typeof(Thing) })]
        [HarmonyPrefix]
        private static bool DesignateStripThingPrefix(Designator_Strip __instance, Thing t)
        {
            try
            {
                ResetTransientConsentState();
                Pawn employee = t as Pawn;
                EmploymentContract contract = EmploymentService.GetActiveContract(employee);
                if (contract == null)
                {
                    return true;
                }

                List<ForcedReleaseItem> affectedItems = FindRefundableStripItems(employee, contract);
                if (affectedItems.Count == 0)
                {
                    return true;
                }

                if (contractsBeingAsked.Contains(contract.id))
                {
                    return false;
                }

                int bondAtRisk = BondAtRiskFor(contract, affectedItems);
                contractsBeingAsked.Add(contract.id);
                try
                {
                    Find.WindowStack.Add(new Dialog_ForcedApparelReleaseConsent(
                        employee,
                        bondAtRisk,
                        () => ConfirmStripDesignation(
                            __instance, t, contract, affectedItems),
                        () => contractsBeingAsked.Remove(contract.id)));
                    return false;
                }
                catch
                {
                    contractsBeingAsked.Remove(contract.id);
                    throw;
                }
            }
            catch (Exception ex)
            {
                // A broken veto must never prevent vanilla from recording a strip designation.
                LogStripDesignatorPrefixErrorOnce(ex);
                return true;
            }
        }

        private static void ConfirmOrderedForcedRelease(
            Pawn_JobTracker tracker,
            Job job,
            JobTag? tag,
            bool requestQueueing,
            EmploymentContract contract,
            List<ForcedReleaseItem> affectedItems)
        {
            contractsBeingAsked.Remove(contract?.id ?? 0);
            try
            {
                for (int i = 0; i < affectedItems.Count; i++)
                {
                    ApproveOneForcedRelease(contract, affectedItems[i].item);
                }

                // This marker covers only this exact job object and only the synchronous re-entry
                // below. The finally is deliberately adjacent to TryTakeOrderedJob so a throw
                // cannot leave a session-wide bypass behind.
                orderedJobReissueBypasses.Add(job);
                try
                {
                    tracker.TryTakeOrderedJob(job, tag, requestQueueing);
                }
                finally
                {
                    orderedJobReissueBypasses.Remove(job);
                }
            }
            catch (Exception ex)
            {
                LogOrderedJobPrefixErrorOnce(ex);
            }
        }

        private static void ConfirmStripDesignation(
            Designator_Strip designator,
            Thing target,
            EmploymentContract contract,
            List<ForcedReleaseItem> affectedItems)
        {
            contractsBeingAsked.Remove(contract?.id ?? 0);
            try
            {
                for (int i = 0; i < affectedItems.Count; i++)
                {
                    ApproveOneForcedRelease(contract, affectedItems[i].item);
                }

                // This is the vanilla DesignateThing body. The strip route has no job to reissue;
                // add the designation only after all exact approvals have been reserved.
                designator.Map.designationManager.AddDesignation(
                    new Designation(target, DesignationDefOf.Strip));
                StrippableUtility.CheckSendStrippingImpactsGoodwillMessage(target);
            }
            catch (Exception ex)
            {
                LogStripDesignatorPrefixErrorOnce(ex);
            }
        }

        private static bool TryFindForcedReleaseItems(
            Pawn actingPawn,
            Job job,
            out Pawn employee,
            out EmploymentContract contract,
            out List<ForcedReleaseItem> affectedItems)
        {
            employee = actingPawn;
            contract = null;
            affectedItems = new List<ForcedReleaseItem>();
            if (job == null)
            {
                return false;
            }

            // ForceTargetWear and Strip operate on targetA, not on the pawn issuing the job.
            if (job.def == JobDefOf.ForceTargetWear || job.def == JobDefOf.Strip)
            {
                employee = job.targetA.Pawn;
            }

            contract = EmploymentService.GetActiveContract(employee);
            if (contract == null)
            {
                return false;
            }

            if (job.def == JobDefOf.DropEquipment)
            {
                ThingWithComps targetEquipment = job.targetA.Thing as ThingWithComps;
                if (employee?.equipment == null || targetEquipment == null ||
                    !employee.equipment.Contains(targetEquipment))
                {
                    return false;
                }

                AddForcedReleaseItem(affectedItems, contract, targetEquipment);
            }
            else if (job.def == JobDefOf.RemoveApparel)
            {
                Apparel targetApparel = job.targetA.Thing as Apparel;
                if (employee?.apparel == null || targetApparel == null ||
                    !employee.apparel.WornApparel.Contains(targetApparel))
                {
                    return false;
                }

                AddForcedReleaseItem(affectedItems, contract, targetApparel);
            }
            else if (job.def == JobDefOf.Equip)
            {
                ThingWithComps incomingEquipment = job.targetA.Thing as ThingWithComps;
                ThingWithComps currentPrimary = employee?.equipment?.Primary;
                if (incomingEquipment == null || currentPrimary == null ||
                    incomingEquipment == currentPrimary || incomingEquipment.def == null ||
                    incomingEquipment.def.equipmentType != EquipmentType.Primary)
                {
                    return false;
                }

                AddForcedReleaseItem(affectedItems, contract, currentPrimary);
            }
            else if (job.def == JobDefOf.Wear || job.def == JobDefOf.ForceTargetWear)
            {
                Apparel incomingApparel = job.def == JobDefOf.Wear
                    ? job.targetA.Thing as Apparel
                    : job.targetB.Thing as Apparel;
                affectedItems = FindRefundableApparelItemsRemovedByWear(
                    employee, incomingApparel, contract);
            }
            else if (job.def == JobDefOf.Strip)
            {
                affectedItems = FindRefundableStripItems(employee, contract);
            }

            return affectedItems.Count > 0;
        }

        private static void AddForcedReleaseItem(
            List<ForcedReleaseItem> affectedItems,
            EmploymentContract contract,
            Thing item)
        {
            EmploymentEquipmentRecord record = FindRefundableRecord(contract, item);
            if (record != null)
            {
                affectedItems.Add(new ForcedReleaseItem(item, record));
            }
        }

        private static string BuildAffectedItemLabel(List<ForcedReleaseItem> affectedItems)
        {
            if (affectedItems == null || affectedItems.Count == 0)
            {
                return "Unknown gear";
            }

            List<string> labels = new List<string>(affectedItems.Count);
            for (int i = 0; i < affectedItems.Count; i++)
            {
                labels.Add(affectedItems[i].item?.LabelCap.ToString() ?? "Unknown gear");
            }

            return string.Join(", ", labels.ToArray());
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
            List<ForcedReleaseItem> affectedItems = FindRefundableApparelItemsRemovedByWear(
                pawn, newApparel, contract);
            if (affectedItems.Count == 0)
            {
                return null;
            }

            matchingRecord = affectedItems[0].record;
            return affectedItems[0].item as Apparel;
        }

        private static List<ForcedReleaseItem> FindRefundableApparelItemsRemovedByWear(
            Pawn pawn,
            Apparel newApparel,
            EmploymentContract contract)
        {
            List<ForcedReleaseItem> affectedItems = new List<ForcedReleaseItem>();
            if (pawn?.apparel == null || newApparel?.def?.apparel == null ||
                pawn.RaceProps?.body == null || contract == null)
            {
                return affectedItems;
            }

            // JobDriver_Wear and JobDriver_ForceTargetWear scan in reverse order and remove every
            // worn item that cannot be worn with their target. Preserve that order here so every
            // original item removed by the one job receives its own exact approval.
            List<Apparel> wornApparel = pawn.apparel.WornApparel;
            for (int i = wornApparel.Count - 1; i >= 0; i--)
            {
                Apparel candidate = wornApparel[i];
                if (candidate?.def?.apparel == null || ApparelUtility.CanWearTogether(
                        newApparel.def, candidate.def, pawn.RaceProps.body))
                {
                    continue;
                }

                EmploymentEquipmentRecord matchingRecord = FindRefundableRecord(contract, candidate);
                if (matchingRecord != null)
                {
                    affectedItems.Add(new ForcedReleaseItem(candidate, matchingRecord));
                }
            }

            return affectedItems;
        }

        private static List<ForcedReleaseItem> FindRefundableStripItems(
            Pawn pawn,
            EmploymentContract contract)
        {
            List<ForcedReleaseItem> affectedItems = new List<ForcedReleaseItem>();
            if (pawn == null || contract == null)
            {
                return affectedItems;
            }

            if (pawn.equipment != null)
            {
                List<ThingWithComps> equipment = pawn.equipment.AllEquipmentListForReading;
                for (int i = 0; i < equipment.Count; i++)
                {
                    AddForcedReleaseItem(affectedItems, contract, equipment[i]);
                }
            }

            if (pawn.apparel != null)
            {
                bool dropLocked = pawn.Destroyed;
                List<Apparel> wornApparel = pawn.apparel.WornApparel;
                for (int i = 0; i < wornApparel.Count; i++)
                {
                    Apparel apparel = wornApparel[i];
                    if (!dropLocked && pawn.apparel.IsLocked(apparel))
                    {
                        continue;
                    }

                    AddForcedReleaseItem(affectedItems, contract, apparel);
                }
            }

            return affectedItems;
        }

        private static EmploymentEquipmentRecord FindRefundableRecord(
            EmploymentContract contract, Thing item)
        {
            if (contract?.arrivedEquipment == null || item == null || item.Destroyed ||
                item.def == null)
            {
                return null;
            }

            List<EmploymentEquipmentRecord> records = contract.arrivedEquipment;
            for (int i = 0; i < records.Count; i++)
            {
                EmploymentEquipmentRecord record = records[i];
                if (record != null && record.RefundableQuantity > 0 &&
                    EmploymentEquipmentService.Matches(record, item))
                {
                    return record;
                }
            }

            return null;
        }

        private static bool ConsumeForcedReleaseApproval(
            EmploymentContract contract, Thing item)
        {
            if (contract == null || item == null ||
                !approvedForcedReleases.TryGetValue(
                    contract, out HashSet<Thing> approvedItems) ||
                !approvedItems.Remove(item))
            {
                return false;
            }

            if (approvedItems.Count == 0)
            {
                approvedForcedReleases.Remove(contract);
            }

            return true;
        }

        private static int BondAtRiskFor(
            EmploymentContract contract,
            Apparel apparel,
            EmploymentEquipmentRecord record)
        {
            int quantity = Mathf.Min(
                Mathf.Max(1, apparel?.stackCount ?? 1),
                Mathf.Max(1, record.RefundableQuantity));
            return EmploymentEquipmentService.BondAtRiskFor(contract, record, quantity);
        }

        private static int BondAtRiskFor(
            EmploymentContract contract,
            List<ForcedReleaseItem> affectedItems)
        {
            if (contract == null || affectedItems == null || affectedItems.Count == 0)
            {
                return 0;
            }

            List<EmploymentEquipmentRecord> adjustedRecords =
                new List<EmploymentEquipmentRecord>(affectedItems.Count);
            List<int> adjustmentUnits = new List<int>(affectedItems.Count);
            for (int i = 0; i < affectedItems.Count; i++)
            {
                ForcedReleaseItem affectedItem = affectedItems[i];
                EmploymentEquipmentRecord record = affectedItem?.record;
                if (record == null)
                {
                    continue;
                }

                int quantity = Mathf.Min(
                    Mathf.Max(1, affectedItem.item?.stackCount ?? 1),
                    Mathf.Max(1, record.RefundableQuantity));
                adjustedRecords.Add(record);
                adjustmentUnits.Add(quantity);
            }

            return EmploymentEquipmentService.BondAtRiskFor(
                contract, adjustedRecords, adjustmentUnits);
        }

        private static void ResetTransientConsentState()
        {
            IntercolonyWorldComponent currentWorld = IntercolonyWorldComponent.Current;
            if (ReferenceEquals(currentWorld, askingWorld))
            {
                return;
            }

            // These sets are intentionally transient. A newly loaded world must not inherit a
            // suppression entry for a dialog that no longer exists in the window stack or a
            // forced-release approval for an item from the old world.
            contractsBeingAsked.Clear();
            approvedForcedReleases.Clear();
            orderedJobReissueBypasses.Clear();
            askingWorld = currentWorld;
        }

        private static void LogApparelDropPostfixErrorOnce(Exception ex)
        {
            if (apparelDropPostfixErrorLogged)
            {
                return;
            }

            apparelDropPostfixErrorLogged = true;
            IntercolonyLog.Error(
                "Failed to record bought-out employee gear; leaving vanilla removal unchanged: " +
                ex);
        }

        private static void LogOrderedJobPrefixErrorOnce(Exception ex)
        {
            if (orderedJobPrefixErrorLogged)
            {
                return;
            }

            orderedJobPrefixErrorLogged = true;
            IntercolonyLog.Error(
                "Failed to enforce employee gear consent before an ordered job; allowing vanilla: " +
                ex);
        }

        private static void LogStripDesignatorPrefixErrorOnce(Exception ex)
        {
            if (stripDesignatorPrefixErrorLogged)
            {
                return;
            }

            stripDesignatorPrefixErrorLogged = true;
            IntercolonyLog.Error(
                "Failed to enforce employee gear consent before a strip designation; allowing vanilla: " +
                ex);
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

        [HarmonyPatch]
        private static class PawnApparelTrackerTryDropPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    typeof(Pawn_ApparelTracker),
                    nameof(Pawn_ApparelTracker.TryDrop),
                    new[]
                    {
                        typeof(Apparel),
                        typeof(Apparel).MakeByRefType(),
                        typeof(IntVec3),
                        typeof(bool)
                    });
            }

            [HarmonyPostfix]
            private static void Postfix(
                Pawn_ApparelTracker __instance,
                Apparel ap,
                bool __result)
            {
                try
                {
                    if (__result && __instance?.pawn != null && ap != null)
                    {
                        ObserveSuccessfulOriginalDrop(__instance.pawn, ap, allowContractDecision: true);
                    }
                }
                catch (Exception ex)
                {
                    // This observer must never break vanilla apparel removal for the colony.
                    LogApparelDropPostfixErrorOnce(ex);
                }
            }
        }

        [HarmonyPatch]
        private static class PawnEquipmentTrackerTryDropEquipmentPatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    typeof(Pawn_EquipmentTracker),
                    nameof(Pawn_EquipmentTracker.TryDropEquipment),
                    new[]
                    {
                        typeof(ThingWithComps),
                        typeof(ThingWithComps).MakeByRefType(),
                        typeof(IntVec3),
                        typeof(bool)
                    });
            }

            [HarmonyPostfix]
            private static void Postfix(
                Pawn_EquipmentTracker __instance,
                ThingWithComps eq,
                bool __result)
            {
                try
                {
                    if (__result && __instance?.pawn != null && eq != null)
                    {
                        // Equipment has no apparel-consent decision; only an exact forced
                        // approval can make an original weapon non-refundable.
                        ObserveSuccessfulOriginalDrop(
                            __instance.pawn, eq, allowContractDecision: false);
                    }
                }
                catch (Exception ex)
                {
                    // This observer must never break vanilla equipment removal for the colony.
                    LogApparelDropPostfixErrorOnce(ex);
                }
            }
        }

        private static void ObserveSuccessfulOriginalDrop(
            Pawn pawn,
            Thing item,
            bool allowContractDecision)
        {
            ResetTransientConsentState();
            if (pawn == null || item == null)
            {
                return;
            }

            EmploymentContract contract = EmploymentService.GetActiveContract(pawn);
            if (contract == null)
            {
                return;
            }

            // A forced approval is for this exact Thing and is consumed only after the full
            // tracker drop call succeeds. Apparel's standing Allowed decision is intentionally
            // not applied to equipment, whose replacement routes have no standing consent card.
            bool forcedApproval = ConsumeForcedReleaseApproval(contract, item);
            if (!forcedApproval &&
                (!allowContractDecision || contract.apparelBondDecision != ApparelBondDecision.Allowed))
            {
                return;
            }

            EmploymentEquipmentRecord record = FindRefundableRecord(contract, item);
            if (record == null)
            {
                return;
            }

            // Identical gear is indistinguishable in a snapshot with only def, stuff and quality,
            // so the first still-refundable record is the stated convention. This observer is the
            // sole owner of boughtOutQuantity: one successful tracker drop marks one unit.
            if (record.boughtOutQuantity < record.quantity)
            {
                record.boughtOutQuantity++;
            }
        }
    }
}
