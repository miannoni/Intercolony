using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Identifies the kind of commercial event recorded in the timeline
    /// (docs/INTERCOLONY_1_0_IMPLEMENTATION_PLAN.md Stage 0.3, read in Stage 7).
    /// </summary>
    /// <remarks>
    /// One value per commercial transition that actually exists in the code today, and no more.
    ///
    /// Failure and cancellation are separate on purpose, in both directions. A cancelled sale is
    /// the player withdrawing, or a war voiding the order — <see cref="HostilityPolicy"/> tells the
    /// player outright that it "does not count against you as a supplier" — while a failed sale is
    /// a missed deadline. A cancelled purchase is the player forfeiting their payment; a failed one
    /// is the supplier defaulting. Collapsing either pair would make the timeline state something
    /// that did not happen, which is worse than recording nothing.
    ///
    /// Scribe writes enums by name, so new values may be appended without a schema change, but an
    /// existing name must never be renamed or reordered out from under a save.
    /// </remarks>
    public enum CommercialEventType
    {
        SaleCompleted,
        SaleFailed,
        SaleCancelled,
        PurchaseCompleted,
        PurchaseFailed,
        PurchaseCancelled,
        ContractStarted,
        ContractCompleted,
        ContractFailed,
        ContractCancelled
    }

    /// <summary>
    /// One entry in the mod's persisted, bounded commercial timeline (the 1.0 program Stage 0.3, docs/INTERCOLONY_1_0_IMPLEMENTATION_PLAN.md Stage 7).
    /// Owned by <see cref="IntercolonyWorldComponent"/>.
    ///
    /// Detailed events give settlements a readable history of deals, contracts, successes
    /// and failures. This complements the compact cumulative aggregates in
    /// <see cref="CommercialHistoryEntry"/>, which are retained indefinitely.
    /// </summary>
    public class CommercialEventRecord : IExposable
    {
        public int id;
        public int tick;
        public int settlementId = -1;

        /// <summary>
        /// Frozen at record time so a historical record still reads correctly if the settlement
        /// is later destroyed or renamed (docs/INTERCOLONY_1_0_IMPLEMENTATION_PLAN.md Stage 7).
        /// Following the <see cref="EmploymentContract.workerName"/> idiom.
        /// </summary>
        public string settlementName = "";

        public CommercialEventType type;

        // Optional references / compact context:
        public int relatedEntityId;
        public ThingDef thingDef;
        public int quantity;
        public int silverAmount;
        public string compactDetail = "";

        public CommercialEventRecord()
        {
        }

        public CommercialEventRecord(
            int id,
            int tick,
            int settlementId,
            CommercialEventType type,
            string settlementName = "",
            int relatedEntityId = 0,
            ThingDef thingDef = null,
            int quantity = 0,
            int silverAmount = 0,
            string compactDetail = null)
        {
            this.id = id;
            this.tick = tick;
            this.settlementId = settlementId;
            this.type = type;
            this.settlementName = settlementName ?? "";
            this.relatedEntityId = relatedEntityId;
            this.thingDef = thingDef;
            this.quantity = quantity;
            this.silverAmount = silverAmount;
            this.compactDetail = compactDetail ?? "";
        }

        public float DaysAgo => (GenTicks.TicksGame - tick) / (float)GenDate.TicksPerDay;

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", 0);
            Scribe_Values.Look(ref tick, "tick", 0);
            Scribe_Values.Look(ref settlementId, "settlementId", -1);
            Scribe_Values.Look(ref settlementName, "settlementName", "");
            Scribe_Values.Look(ref type, "type", CommercialEventType.SaleCompleted);
            Scribe_Values.Look(ref relatedEntityId, "relatedEntityId", 0);
            Scribe_Defs.Look(ref thingDef, "thingDef");
            Scribe_Values.Look(ref quantity, "quantity", 0);
            Scribe_Values.Look(ref silverAmount, "silverAmount", 0);
            Scribe_Values.Look(ref compactDetail, "compactDetail", "");
        }

        public override string ToString()
        {
            string when = $"{Mathf.Max(0f, DaysAgo):0.#}d ago";
            string target = !string.IsNullOrEmpty(settlementName) ? settlementName : $"settlement {settlementId}";
            string item = thingDef != null ? $" {quantity}x {thingDef.label}" : "";
            string silver = silverAmount != 0 ? $" ({silverAmount} silver)" : "";
            string detail = string.IsNullOrEmpty(compactDetail) ? "" : $" — {compactDetail}";
            return $"[#{id} {when}] {type} ({target}){item}{silver}{detail}";
        }
    }
}
