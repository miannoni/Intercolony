using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// The equipment observation used by a hire preview and its matching transaction.
    ///
    /// It is deliberately transient: the contract stores the snapshot, while this object lets a
    /// UI pass the exact observation it displayed into the hire path.
    /// </summary>
    public sealed class EmploymentEquipmentQuote
    {
        public readonly Pawn sourcePawn;
        public readonly List<EmploymentEquipmentRecord> equipment;
        public readonly int bond;

        internal EmploymentEquipmentQuote(
            Pawn sourcePawn, List<EmploymentEquipmentRecord> equipment, int bond)
        {
            this.sourcePawn = sourcePawn;
            this.equipment = equipment;
            this.bond = bond;
        }
    }

    /// <summary>
    /// The complete amount due at hire, built once by the preview and carried into the matching
    /// transaction. The wage and bond remain separate so the UI can disclose both components.
    /// </summary>
    public sealed class EmploymentHireCostQuote
    {
        public readonly EmploymentEquipmentQuote equipment;
        public readonly int upfrontWages;
        public readonly long totalDue;

        internal EmploymentHireCostQuote(
            EmploymentEquipmentQuote equipment, int upfrontWages)
        {
            this.equipment = equipment;
            this.upfrontWages = upfrontWages;
            totalDue = EmploymentEquipmentService.TotalHireCost(upfrontWages, equipment.bond);
        }
    }

    /// <summary>
    /// One stack of weapons or apparel observed from an employee at hire.
    ///
    /// This is deliberately a value snapshot rather than a reference to the pawn's Thing. The
    /// pawn reference is cleared when employment ends, while settlement needs the exact supplied
    /// definition, material, workmanship and count to decide whether the borrowed capital came
    /// back. Body parts, implants and the pawn's unrelated personal inventory are not part of
    /// this record.
    /// </summary>
    public class EmploymentEquipmentRecord : IExposable
    {
        public ThingDef thingDef;
        public ThingDef stuffDef;
        public QualityCategory? quality;
        public int quantity;

        public void ExposeData()
        {
            Scribe_Defs.Look(ref thingDef, "thingDef");
            Scribe_Defs.Look(ref stuffDef, "stuffDef");
            Scribe_Values.Look(ref quality, "quality");
            Scribe_Values.Look(ref quantity, "quantity", 0);
        }
    }

    /// <summary>
    /// The one-time settlement of an employment equipment bond. It is transient: the contract
    /// keeps the original bond and equipment snapshot, while this result explains the movement
    /// that was made at the ending boundary.
    /// </summary>
    public sealed class EmploymentEquipmentSettlement
    {
        public readonly int bond;
        public readonly int matchedBond;
        public readonly int returnedSilver;
        public readonly int retainedSilver;
        public readonly int undeliveredSilver;
        public readonly int returnedQuantity;
        public readonly int retainedQuantity;
        public readonly bool usedDestinationFallback;
        public readonly bool usedStorageFallback;
        public readonly bool destinationUnavailable;
        public readonly string message;

        internal EmploymentEquipmentSettlement(
            int bond, int matchedBond, int returnedSilver, int retainedSilver,
            int undeliveredSilver, int returnedQuantity, int retainedQuantity,
            bool usedDestinationFallback, bool usedStorageFallback,
            bool destinationUnavailable, string message)
        {
            this.bond = bond;
            this.matchedBond = matchedBond;
            this.returnedSilver = returnedSilver;
            this.retainedSilver = retainedSilver;
            this.undeliveredSilver = undeliveredSilver;
            this.returnedQuantity = returnedQuantity;
            this.retainedQuantity = retainedQuantity;
            this.usedDestinationFallback = usedDestinationFallback;
            this.usedStorageFallback = usedStorageFallback;
            this.destinationUnavailable = destinationUnavailable;
            this.message = message;
        }
    }

    /// <summary>
    /// Captures the employee's actual gear at hire and calculates its refundable bond.
    ///
    /// The same saved snapshot is used at the ending boundary to match the weapons and apparel the
    /// worker still carries. Matching is by definition, stuff and quality rather than Thing
    /// identity, and condition is deliberately ignored: normal wear returns the full share.
    /// </summary>
    public static class EmploymentEquipmentService
    {
        /// <summary>
        /// F23 calls approximately 10% a starting figure for balancing, not a fixed rule.
        /// </summary>
        public const float EquipmentBondPremium = 0.10f;

        /// <summary>
        /// Captures the pawn's actual equipment and worn-apparel stacks into save-safe values.
        /// Nothing in Intercolony changes a travelling worker's gear, so this hire-time observation
        /// is also the gear that will arrive; arrival must not read or charge it again. The pawn's
        /// personal inventory and body modifications are deliberately not charged as equipment.
        /// </summary>
        public static EmploymentEquipmentQuote Quote(Pawn worker)
        {
            List<EmploymentEquipmentRecord> equipment =
                CaptureAtHire(worker);
            return new EmploymentEquipmentQuote(worker, equipment, BondFor(equipment));
        }

        private static List<EmploymentEquipmentRecord> CaptureAtHire(Pawn worker)
        {
            List<EmploymentEquipmentRecord> equipment =
                new List<EmploymentEquipmentRecord>();

            if (worker?.equipment != null)
            {
                foreach (ThingWithComps item in worker.equipment.AllEquipmentListForReading)
                {
                    Add(equipment, item);
                }
            }

            if (worker?.apparel != null)
            {
                foreach (Apparel item in worker.apparel.WornApparel)
                {
                    Add(equipment, item);
                }
            }

            return equipment;
        }

        /// <summary>
        /// Replacement value for the captured equipment on the deterministic F19 basis. A live
        /// supplier quote is intentionally not involved: the same contract must not acquire a
        /// different bond because a settlement roll changed between draws.
        /// </summary>
        public static float ReplacementValue(List<EmploymentEquipmentRecord> equipment)
        {
            float total = 0f;
            if (equipment == null)
            {
                return total;
            }

            foreach (EmploymentEquipmentRecord item in equipment)
            {
                if (item == null || item.thingDef == null || item.quantity <= 0)
                {
                    continue;
                }

                float baseValue = IntercolonyPricing.BaseValue(item.thingDef, item.stuffDef);
                if (baseValue <= 0f || float.IsNaN(baseValue) || float.IsInfinity(baseValue))
                {
                    continue;
                }

                total += baseValue * item.quantity;
            }

            return total;
        }

        /// <summary>Rounds the replacement value plus the named refundable-bond premium once.</summary>
        public static int BondFor(List<EmploymentEquipmentRecord> equipment)
        {
            float replacementValue = ReplacementValue(equipment);
            if (replacementValue <= 0f || float.IsNaN(replacementValue) ||
                float.IsInfinity(replacementValue))
            {
                return 0;
            }

            return Mathf.Max(0, Mathf.RoundToInt(
                replacementValue * (1f + EquipmentBondPremium)));
        }

        /// <summary>
        /// The amount due at hire. Wages and the refundable bond remain separate inputs and are
        /// only combined at the final payment boundary.
        /// </summary>
        public static long TotalHireCost(int upfrontWages, int equipmentBond)
        {
            return (long)Mathf.Max(0, upfrontWages) + Mathf.Max(0, equipmentBond);
        }

        /// <summary>
        /// Creates the single amount-due calculation used by the preview and its matching hire.
        /// </summary>
        public static EmploymentHireCostQuote QuoteHireCost(
            int upfrontWages, EmploymentEquipmentQuote equipmentQuote)
        {
            return equipmentQuote == null
                ? null
                : new EmploymentHireCostQuote(equipmentQuote, upfrontWages);
        }

        /// <summary>
        /// Removes the exact amount in the hire quote atomically. The ledger records only the wage
        /// component; the bond is a refundable liability rather than wage or purchase income.
        /// </summary>
        public static bool TryTakeHireCost(
            Map map, EmploymentHireCostQuote hireCostQuote, out string failureReason)
        {
            failureReason = null;
            if (map == null)
            {
                failureReason = "No colony to collect the hire cost from.";
                return false;
            }

            if (hireCostQuote == null)
            {
                failureReason = "No hire cost was quoted.";
                return false;
            }

            long total = hireCostQuote.totalDue;
            if (total > int.MaxValue)
            {
                failureReason = "The up-front hire cost is too large to collect.";
                return false;
            }

            if (total <= 0 || PurchaseOrderService.TryTakeSilver(map, (int)total))
            {
                return true;
            }

            failureReason = "Could not collect the up-front hire cost.";
            return false;
        }

        /// <summary>Player-facing value for the bond row; zero is never shown as a fake deposit.</summary>
        public static string BondLabel(int bond)
        {
            return bond > 0
                ? $"{bond:N0} silver refundable deposit"
                : "Nothing bondable \u2014 no equipment bond charged";
        }

        /// <summary>
        /// Settles the equipment deposit exactly once, before quest teardown can drop the worker's
        /// gear. Only recorded weapons and worn apparel are bondable, but a recorded Thing may be
        /// equipped, worn, in inventory or in the vanilla carry tracker when it is returned.
        /// Body modifications and hit points are outside this bond.
        /// </summary>
        public static EmploymentEquipmentSettlement SettleBond(EmploymentContract contract)
        {
            if (contract == null || contract.equipmentBond <= 0 || contract.equipmentBondSettled)
            {
                return null;
            }

            int bond = Mathf.Max(0, contract.equipmentBond);
            List<EmploymentEquipmentRecord> records = contract.arrivedEquipment ??
                new List<EmploymentEquipmentRecord>();
            List<CarriedEquipmentStack> carried = CaptureCarriedEquipment(contract.pawn);
            List<int> matchedQuantities = new List<int>(records.Count);

            float totalReplacementValue = 0f;
            float matchedReplacementValue = 0f;
            int totalQuantity = 0;
            int matchedQuantity = 0;

            for (int i = 0; i < records.Count; i++)
            {
                EmploymentEquipmentRecord record = records[i];
                int quantity = Mathf.Max(0, record?.quantity ?? 0);
                int matched = MatchQuantity(record, carried);
                matchedQuantities.Add(matched);

                float unitValue = ReplacementValuePerUnit(record);
                if (unitValue > 0f && !float.IsNaN(unitValue) && !float.IsInfinity(unitValue))
                {
                    totalReplacementValue += unitValue * quantity;
                    matchedReplacementValue += unitValue * matched;
                }

                totalQuantity += quantity;
                matchedQuantity += matched;
            }

            int matchedBond = BondShare(
                bond, totalReplacementValue, matchedReplacementValue, totalQuantity, matchedQuantity);
            int retainedSilver = bond - matchedBond;

            Map destination = ResolveSettlementMap(contract, out bool usedDestinationFallback);
            bool usedStorageFallback = false;
            int returnedSilver = 0;
            if (matchedBond > 0 && destination != null)
            {
                try
                {
                    returnedSilver = PurchaseOrderService.ReturnSilverToColony(
                        destination, matchedBond, out usedStorageFallback);
                }
                catch (Exception ex)
                {
                    IntercolonyLog.Warning(
                        $"Employment #{contract.id}: equipment-bond refund could not be placed: {ex}");
                }
            }

            returnedSilver = Mathf.Clamp(returnedSilver, 0, matchedBond);
            int undeliveredSilver = matchedBond - returnedSilver;
            int retainedQuantity = Mathf.Max(0, totalQuantity - matchedQuantity);
            string message = BuildSettlementMessage(
                returnedSilver, retainedSilver, undeliveredSilver,
                records, matchedQuantities, destination, usedDestinationFallback,
                usedStorageFallback);

            contract.equipmentBondSettled = true;

            if (returnedSilver > 0)
            {
                LedgerService.Record(
                    LedgerKind.Refund, returnedSilver, contract.settlementName,
                    $"{contract.workerName}, equipment bond returned");
            }

            MessageTypeDef messageType = retainedSilver > 0 || undeliveredSilver > 0
                ? MessageTypeDefOf.NeutralEvent
                : MessageTypeDefOf.PositiveEvent;
            Messages.Message(message, messageType, historical: false);
            IntercolonyLog.Message($"Equipment bond settled for employment #{contract.id}: {message}");

            return new EmploymentEquipmentSettlement(
                bond, matchedBond, returnedSilver, retainedSilver, undeliveredSilver,
                matchedQuantity, retainedQuantity, usedDestinationFallback,
                usedStorageFallback, destination == null, message);
        }

        private sealed class CarriedEquipmentStack
        {
            public readonly Thing thing;
            public int remaining;

            public CarriedEquipmentStack(Thing thing)
            {
                this.thing = thing;
                remaining = Mathf.Max(0, thing?.stackCount ?? 0);
            }
        }

        private static List<CarriedEquipmentStack> CaptureCarriedEquipment(Pawn worker)
        {
            List<CarriedEquipmentStack> carried = new List<CarriedEquipmentStack>();
            if (worker?.inventory?.innerContainer != null)
            {
                foreach (Thing item in worker.inventory.innerContainer)
                {
                    AddCarried(carried, item);
                }
            }

            if (worker?.carryTracker?.CarriedThing != null)
            {
                AddCarried(carried, worker.carryTracker.CarriedThing);
            }

            if (worker?.equipment != null)
            {
                foreach (ThingWithComps item in worker.equipment.AllEquipmentListForReading)
                {
                    AddCarried(carried, item);
                }
            }

            if (worker?.apparel != null)
            {
                foreach (Apparel item in worker.apparel.WornApparel)
                {
                    AddCarried(carried, item);
                }
            }

            return carried;
        }

        private static void AddCarried(List<CarriedEquipmentStack> carried, Thing item)
        {
            if (item == null || item.Destroyed || item.def == null || item.stackCount <= 0)
            {
                return;
            }

            carried.Add(new CarriedEquipmentStack(item));
        }

        private static int MatchQuantity(
            EmploymentEquipmentRecord record, List<CarriedEquipmentStack> carried)
        {
            if (record == null || record.quantity <= 0)
            {
                return 0;
            }

            int needed = record.quantity;
            int matched = 0;
            for (int i = 0; i < carried.Count && needed > 0; i++)
            {
                CarriedEquipmentStack candidate = carried[i];
                if (candidate.remaining <= 0 || !Matches(record, candidate.thing))
                {
                    continue;
                }

                int take = Mathf.Min(needed, candidate.remaining);
                candidate.remaining -= take;
                needed -= take;
                matched += take;
            }

            return matched;
        }

        private static bool Matches(EmploymentEquipmentRecord record, Thing item)
        {
            if (record == null || item == null || item.Destroyed || item.def == null ||
                item.def != record.thingDef || item.Stuff != record.stuffDef)
            {
                return false;
            }

            QualityCategory? quality = null;
            if (item.TryGetQuality(out QualityCategory observedQuality))
            {
                quality = observedQuality;
            }

            return quality == record.quality;
        }

        private static int BondShare(
            int bond, float totalReplacementValue, float matchedReplacementValue,
            int totalQuantity, int matchedQuantity)
        {
            if (bond <= 0 || matchedQuantity <= 0)
            {
                return 0;
            }

            if (matchedQuantity >= totalQuantity)
            {
                return bond;
            }

            if (totalReplacementValue > 0f && !float.IsNaN(totalReplacementValue) &&
                !float.IsInfinity(totalReplacementValue) && matchedReplacementValue > 0f)
            {
                if (Mathf.Approximately(matchedReplacementValue, totalReplacementValue))
                {
                    return bond;
                }

                // Apply the share to the already-rounded bond, so the 10% premium follows the
                // returned item's proportion instead of being lost or charged a second time.
                return Mathf.Clamp(
                    Mathf.RoundToInt(bond * matchedReplacementValue / totalReplacementValue),
                    0, bond);
            }

            // A hand-authored or damaged old record can have no usable replacement value. It is
            // still safer to conserve the charged bond by item count than to make the whole amount
            // disappear or invent a condition-based value.
            return totalQuantity > 0
                ? Mathf.Clamp(Mathf.RoundToInt(bond * matchedQuantity / (float)totalQuantity), 0, bond)
                : 0;
        }

        private static float ReplacementValuePerUnit(EmploymentEquipmentRecord item)
        {
            if (item == null || item.thingDef == null || item.quantity <= 0)
            {
                return 0f;
            }

            float baseValue = IntercolonyPricing.BaseValue(item.thingDef, item.stuffDef);
            return baseValue > 0f && !float.IsNaN(baseValue) && !float.IsInfinity(baseValue)
                ? baseValue
                : 0f;
        }

        private static Map ResolveSettlementMap(
            EmploymentContract contract, out bool usedDestinationFallback)
        {
            usedDestinationFallback = false;
            if (contract?.destinationMap != null &&
                Find.Maps?.Contains(contract.destinationMap) == true)
            {
                return contract.destinationMap;
            }

            Map fallback = Find.AnyPlayerHomeMap;
            usedDestinationFallback = fallback != null;
            return fallback;
        }

        private static string BuildSettlementMessage(
            int returnedSilver, int retainedSilver, int undeliveredSilver,
            List<EmploymentEquipmentRecord> records,
            List<int> matchedQuantities, Map destination, bool usedDestinationFallback,
            bool usedStorageFallback)
        {
            List<string> matched = new List<string>();
            List<string> retained = new List<string>();
            for (int i = 0; i < records.Count; i++)
            {
                EmploymentEquipmentRecord record = records[i];
                int matchedQuantity = matchedQuantities[i];
                int retainedQuantity = Mathf.Max(0, record?.quantity ?? 0) - matchedQuantity;
                string matchedLabel = Describe(record, matchedQuantity);
                if (!string.IsNullOrEmpty(matchedLabel))
                {
                    matched.Add(matchedLabel);
                }

                string retainedLabel = Describe(record, retainedQuantity);
                if (!string.IsNullOrEmpty(retainedLabel))
                {
                    retained.Add(retainedLabel);
                }
            }

            string matchedText = EquipmentList(matched);
            string retainedText = EquipmentList(retained);
            string message;

            if (returnedSilver > 0 && retainedSilver <= 0 && undeliveredSilver <= 0)
            {
                message =
                    $"Equipment bond settled: {returnedSilver:N0} silver returned for the " +
                    $"matching {matchedText}.";
            }
            else if (returnedSilver > 0)
            {
                message =
                    $"Equipment bond settled: {returnedSilver:N0} silver returned for the " +
                    $"matching {matchedText}.";
                if (retainedSilver > 0)
                {
                    message +=
                        $" {retainedSilver:N0} silver retained for equipment kept: {retainedText}.";
                }
            }
            else if (retainedSilver > 0)
            {
                message =
                    $"Equipment bond settled: no silver returned. {retainedSilver:N0} silver " +
                    $"retained for equipment kept: {retainedText}.";
            }
            else
            {
                message =
                    $"Equipment bond settled: no silver was returned for the matching {matchedText}.";
            }

            if (undeliveredSilver > 0)
            {
                if (destination == null)
                {
                    message +=
                        $" The refundable {undeliveredSilver:N0} silver could not be returned " +
                        "because no player colony map remains; the destination colony is gone or " +
                        "abandoned, so there is no colony storage to receive it.";
                }
                else
                {
                    message +=
                        $" The remaining {undeliveredSilver:N0} silver could not be placed because " +
                        "the colony's storage and trade spot rejected it.";
                }
            }

            if (usedDestinationFallback && destination != null &&
                (returnedSilver > 0 || undeliveredSilver > 0))
            {
                string destinationLabel = destination.Parent?.Label ?? "another player colony";
                message +=
                    $" The original destination map was unavailable, so the refund went to " +
                    $"{destinationLabel}.";
            }

            if (usedStorageFallback && returnedSilver > 0)
            {
                message +=
                    " The refund was returned but could not be stored, so it was left at the " +
                    "trade spot.";
            }

            return message;
        }

        private static string Describe(EmploymentEquipmentRecord record, int quantity)
        {
            if (record == null || quantity <= 0)
            {
                return null;
            }

            string label = record.thingDef?.label;
            if (string.IsNullOrEmpty(label))
            {
                label = "recorded equipment";
            }

            if (record.stuffDef != null && !string.IsNullOrEmpty(record.stuffDef.label))
            {
                label = $"{record.stuffDef.label} {label}";
            }

            if (record.quality.HasValue)
            {
                label += $" ({record.quality.Value.GetLabel()})";
            }

            return $"{quantity:N0}x {label}";
        }

        private static string EquipmentList(List<string> items)
        {
            return items.Count > 0
                ? string.Join(", ", items.ToArray())
                : "no recorded equipment";
        }

        public const string BondTooltip =
            "This is a deposit against the equipment recorded at hire, not wages. " +
            "It comes back when that equipment is returned; retaining it settles the deposit " +
            "against the gear.";

        private static void Add(List<EmploymentEquipmentRecord> records, Thing item)
        {
            if (item == null || item.def == null || item.stackCount <= 0)
            {
                return;
            }

            QualityCategory? quality = null;
            if (item.TryGetQuality(out QualityCategory observedQuality))
            {
                quality = observedQuality;
            }

            records.Add(new EmploymentEquipmentRecord
            {
                thingDef = item.def,
                stuffDef = item.Stuff,
                quality = quality,
                quantity = item.stackCount
            });
        }
    }
}
