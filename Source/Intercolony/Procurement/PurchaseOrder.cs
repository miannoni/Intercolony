using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Purchase order lifecycle (DESIGN.md §21). That section sketches eight states and says
    /// "the first implementation may use fewer states", so this uses the fewest that still
    /// model the real thing:
    ///
    /// <c>Confirmed</c> — paid for, supplier is producing or gathering it.
    /// <c>ReadyForPickup</c> — goods are waiting at the supplier; the player must fetch them.
    /// <c>Delivered</c> is not a stored state: goods either arrive at the colony (and the
    /// order completes immediately) or they sit at the supplier until collected. As with
    /// sales orders, a caravan in motion *is* the in-transit state and needs no flag.
    /// </summary>
    public enum PurchaseOrderStatus
    {
        Confirmed,
        ReadyForPickup,
        Completed,
        Cancelled,

        /// <summary>The supplier failed to deliver. Payment is refunded (§21 SupplierDefault).</summary>
        SupplierDefault,

        /// <summary>
        /// The supplier's faction went to war before the goods arrived (§88, §113). Distinct from
        /// <see cref="SupplierDefault"/> precisely because the payment is *not* refunded — the
        /// silver is with an enemy now. Keeping them apart is what stops the orders list implying
        /// the player got their money back.
        /// </summary>
        LostToWar
    }

    /// <summary>
    /// A paid-for commitment from a supplier (DESIGN.md §7.6, §21, §104).
    ///
    /// Records exactly what was promised — def, material, quality, count — so §104's
    /// "preserve expected properties" is verifiable rather than a matter of trust: the goods
    /// that arrive are checked against the terms stored here.
    /// </summary>
    public class PurchaseOrder : IExposable
    {
        public int id;

        /// <summary>The request and quote this came from, for traceability.</summary>
        public int requestId;
        public int quotationId;

        public int settlementId;
        public string settlementName = "";
        public string factionName = "";

        public ThingDef thingDef;
        public ThingDef stuffDef;
        public QualityCategory? quality;
        public int quantity;

        public float unitPrice;
        public int paidSilver;

        /// <summary>True when the supplier brings it; false when the player collects (§25.3/§25.4).</summary>
        public bool supplierDelivers;

        public int orderedTick;

        /// <summary>Tick at which the goods are ready or arrive.</summary>
        public int readyTick;

        public PurchaseOrderStatus status = PurchaseOrderStatus.Confirmed;

        /// <summary>
        /// Deadline for collecting a pickup order. Goods do not wait forever; §21's
        /// SupplierDefault covers the supplier reselling them.
        /// </summary>
        public int pickupExpiryTick;

        public string outcomeNote = "";

        public PurchaseOrder()
        {
        }

        public int TotalPrice => Mathf.RoundToInt(unitPrice * quantity);

        public bool IsOpen => status == PurchaseOrderStatus.Confirmed ||
                              status == PurchaseOrderStatus.ReadyForPickup;

        public bool AwaitingCollection => status == PurchaseOrderStatus.ReadyForPickup;

        public float DaysUntilReady => (readyTick - GenTicks.TicksGame) / (float)GenDate.TicksPerDay;

        public float DaysUntilPickupExpires =>
            (pickupExpiryTick - GenTicks.TicksGame) / (float)GenDate.TicksPerDay;

        /// <summary>Item description including everything that was promised.</summary>
        public string ItemLabel()
        {
            string label = thingDef?.LabelCap.ToString() ?? "<missing def>";
            System.Collections.Generic.List<string> parts = new System.Collections.Generic.List<string>();
            if (stuffDef != null)
            {
                parts.Add(stuffDef.label);
            }

            if (quality.HasValue)
            {
                parts.Add(quality.Value.GetLabel());
            }

            return parts.Count == 0 ? label : $"{label} ({string.Join(", ", parts.ToArray())})";
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", 0);
            Scribe_Values.Look(ref requestId, "requestId", 0);
            Scribe_Values.Look(ref quotationId, "quotationId", 0);
            Scribe_Values.Look(ref settlementId, "settlementId", -1);
            Scribe_Values.Look(ref settlementName, "settlementName", "");
            Scribe_Values.Look(ref factionName, "factionName", "");
            Scribe_Defs.Look(ref thingDef, "thingDef");
            Scribe_Defs.Look(ref stuffDef, "stuffDef");
            Scribe_Values.Look(ref quality, "quality");
            Scribe_Values.Look(ref quantity, "quantity", 0);
            Scribe_Values.Look(ref unitPrice, "unitPrice", 0f);
            Scribe_Values.Look(ref paidSilver, "paidSilver", 0);
            Scribe_Values.Look(ref supplierDelivers, "supplierDelivers", false);
            Scribe_Values.Look(ref orderedTick, "orderedTick", 0);
            Scribe_Values.Look(ref readyTick, "readyTick", 0);
            Scribe_Values.Look(ref pickupExpiryTick, "pickupExpiryTick", 0);
            Scribe_Values.Look(ref status, "status", PurchaseOrderStatus.Confirmed);
            Scribe_Values.Look(ref outcomeNote, "outcomeNote", "");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (settlementName == null) settlementName = "";
                if (factionName == null) factionName = "";
                if (outcomeNote == null) outcomeNote = "";
            }
        }

        public bool IsValidAfterLoad => thingDef != null && quantity > 0;

        public override string ToString()
        {
            return $"#{id} {quantity}x {ItemLabel()} from {settlementName} " +
                   $"[{status}] {(supplierDelivers ? "delivered" : "pickup")}";
        }
    }
}
