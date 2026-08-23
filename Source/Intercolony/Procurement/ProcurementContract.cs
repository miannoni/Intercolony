using RimWorld;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Lifecycle of a standing procurement agreement proposed by the player.
    ///
    /// This is intentionally separate from <see cref="ContractStatus"/>: that enum belongs to
    /// the persisted sales-contract lifecycle, whose proposal and fulfilment direction are
    /// different from procurement.
    /// </summary>
    public enum ProcurementContractStatus
    {
        /// <summary>A proposal awaiting the supplier's answer.</summary>
        Offered,

        /// <summary>The supplier accepted and future cycles are live.</summary>
        Active,

        /// <summary>All scheduled procurement cycles were resolved.</summary>
        Completed,

        /// <summary>The agreement ended before all scheduled cycles were resolved.</summary>
        Cancelled,

        /// <summary>The supplier failed to fulfil a cycle.</summary>
        SupplierDefault
    }

    /// <summary>
    /// A standing procurement agreement proposed by the player to a supplier.
    ///
    /// This dedicated record keeps the persisted sales <see cref="RecurringContract"/> stable;
    /// a procurement cycle will later create a normal <see cref="PurchaseOrder"/> from these
    /// frozen terms.
    /// </summary>
    public class ProcurementContract : IExposable
    {
        /// <summary>Stable ID allocated by <see cref="IntercolonyWorldComponent.NextId"/>.</summary>
        public int id;

        /// <summary>Stable world-object ID of the supplier settlement.</summary>
        public int settlementId;

        /// <summary>Settlement name captured when the proposal was made.</summary>
        public string settlementName = "";

        /// <summary>Product the supplier promises to provide each cycle.</summary>
        public ThingDef thingDef;

        /// <summary>Material required for the promised product, when applicable.</summary>
        public ThingDef stuffDef;

        /// <summary>Quality promised for each cycle, or null when quality is not specified.</summary>
        public QualityCategory? quality;

        /// <summary>Units the supplier promises to provide in each cycle.</summary>
        public int quantityPerCycle;

        /// <summary>Silver paid per unit for every cycle of this agreement.</summary>
        public float unitPrice;

        /// <summary>Number of in-game days between procurement cycles.</summary>
        public int cadenceDays;

        /// <summary>Total number of procurement cycles in this agreement.</summary>
        public int totalCycles;

        /// <summary>How each cycle's goods reach the player.</summary>
        public FulfillmentMode fulfillment = FulfillmentMode.SellerDelivery;

        /// <summary>Number of cycles fulfilled successfully.</summary>
        public int cyclesCompleted;

        /// <summary>Number of cycles that ended in supplier default.</summary>
        public int cyclesFailed;

        /// <summary>Absolute tick when the next procurement cycle is due.</summary>
        public int nextCycleTick;

        /// <summary>
        /// Sentinel meaning that no procurement order is currently in flight.
        /// </summary>
        public const int NoActiveOrderId = -1;

        /// <summary>
        /// ID of the <see cref="PurchaseOrder"/> for the cycle currently in flight, or
        /// <see cref="NoActiveOrderId"/> when no cycle has an active order. This is a purchase
        /// order ID, never a sales-order ID; keeping that distinction explicit prevents the two
        /// contract kinds from acquiring an ambiguous shared reference later.
        /// </summary>
        public int activeOrderId = NoActiveOrderId;

        /// <summary>Current procurement-agreement lifecycle state.</summary>
        public ProcurementContractStatus status = ProcurementContractStatus.Offered;

        /// <summary>Absolute tick when this procurement agreement was created.</summary>
        public int createdTick;

        /// <summary>Sentinel for an offer that never expires; otherwise the absolute expiry tick.</summary>
        public const int NoExpiryTick = -1;

        /// <summary>Absolute tick when the initial offer expires, or <see cref="NoExpiryTick"/>.</summary>
        public int offerExpiryTick = NoExpiryTick;

        /// <summary>Whether the supplier has offered another run of the agreement.</summary>
        public bool renewalOffered;

        /// <summary>Absolute tick when the renewal offer expires.</summary>
        public int renewalExpiryTick;

        /// <summary>Number of completed renewal runs beyond the first agreement.</summary>
        public int renewals;

        /// <summary>Writes the durable procurement terms, progress, and lifecycle state.</summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", 0);
            Scribe_Values.Look(ref settlementId, "settlementId", -1);
            Scribe_Values.Look(ref settlementName, "settlementName", "");
            Scribe_Defs.Look(ref thingDef, "thingDef");
            Scribe_Defs.Look(ref stuffDef, "stuffDef");
            Scribe_Values.Look(ref quality, "quality");
            Scribe_Values.Look(ref quantityPerCycle, "quantityPerCycle", 0);
            Scribe_Values.Look(ref unitPrice, "unitPrice", 0f);
            Scribe_Values.Look(ref cadenceDays, "cadenceDays", 0);
            Scribe_Values.Look(ref totalCycles, "totalCycles", 0);
            Scribe_Values.Look(ref fulfillment, "fulfillment", FulfillmentMode.SellerDelivery);
            Scribe_Values.Look(ref cyclesCompleted, "cyclesCompleted", 0);
            Scribe_Values.Look(ref cyclesFailed, "cyclesFailed", 0);
            Scribe_Values.Look(ref nextCycleTick, "nextCycleTick", 0);
            Scribe_Values.Look(ref activeOrderId, "activeOrderId", NoActiveOrderId);
            Scribe_Values.Look(ref status, "status", ProcurementContractStatus.Offered);
            Scribe_Values.Look(ref createdTick, "createdTick", 0);
            Scribe_Values.Look(ref offerExpiryTick, "offerExpiryTick", NoExpiryTick);
            Scribe_Values.Look(ref renewalOffered, "renewalOffered", false);
            Scribe_Values.Look(ref renewalExpiryTick, "renewalExpiryTick", 0);
            Scribe_Values.Look(ref renewals, "renewals", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && settlementName == null)
            {
                settlementName = "";
            }
        }

        /// <summary>
        /// Whether this record still names a usable product and a non-empty standing term after
        /// loading. A missing definition normally means its supplying mod was removed.
        /// </summary>
        public bool IsValidAfterLoad => thingDef != null && quantityPerCycle > 0 && totalCycles > 0;
    }
}
