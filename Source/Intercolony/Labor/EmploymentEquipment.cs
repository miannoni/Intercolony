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
    /// pawn reference is cleared when employment ends, while the next F23 slice needs the exact
    /// supplied definition, material, workmanship and count to decide whether the borrowed capital
    /// came back. Body parts, implants and inventory are not part of this record.
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
    /// Captures the employee's actual gear at hire and calculates its refundable bond.
    ///
    /// F23's return/refund and retention settlement are intentionally not implemented here. This
    /// first slice only records what will arrive and takes the deposit that makes it borrowed
    /// capital.
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
        /// is also the gear that will arrive; arrival must not read or charge it again.
        /// Inventory and body modifications are deliberately not inspected.
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
