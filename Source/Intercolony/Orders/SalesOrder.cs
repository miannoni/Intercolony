using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Sales order lifecycle (DESIGN.md §14). That section sketches a longer chain
    /// (Preparing, Ready, InTransit, Delivered) and explicitly says the initial
    /// implementation does not need every state — what matters is that transitions are
    /// explicit and authoritative.
    ///
    /// Phase 5 uses the minimum that still models the loop honestly. "In transit" is not a
    /// stored state because the caravan itself *is* that state: the goods are physically on
    /// the map, owned by pawns, visible to the player. Inventing a parallel status field
    /// would be a second source of truth that could disagree with the world.
    /// </summary>
    public enum SalesOrderStatus
    {
        Accepted,
        Completed,
        Failed,
        Cancelled
    }

    /// <summary>
    /// A binding commitment to deliver goods to a counterparty (DESIGN.md §7.3, §15).
    ///
    /// Phase 5 carries a single line item — one ThingDef and a quantity — because that is the
    /// fungible case (§23.1) and matching it needs no quality, stuff, or hit-point rules.
    /// Generalized matching is Phase 6 (§99), which is why the fields the general case needs
    /// are deliberately absent rather than present-and-unused.
    /// </summary>
    public class SalesOrder : IExposable
    {
        public int id;

        /// <summary>The opportunity this came from, for traceability.</summary>
        public int opportunityId;

        public int settlementId;
        public string settlementName = "";
        public string factionName = "";

        public ThingDef thingDef;
        public int quantity;

        /// <summary>Agreed unit price, locked at acceptance so later market drift cannot change the deal.</summary>
        public float unitPrice;

        public int acceptedTick;
        public int deadlineTick;

        public SalesOrderStatus status = SalesOrderStatus.Accepted;

        /// <summary>How much has actually been handed over. Partial deliveries accumulate.</summary>
        public int deliveredQuantity;

        /// <summary>Silver actually paid out so far.</summary>
        public int paidSilver;

        /// <summary>Set when the order ends, for the orders list and any later dispute handling.</summary>
        public string outcomeNote = "";

        public SalesOrder()
        {
        }

        public int TotalPayment => Mathf.RoundToInt(unitPrice * quantity);

        public int RemainingQuantity => Mathf.Max(0, quantity - deliveredQuantity);

        public bool IsOpen => status == SalesOrderStatus.Accepted;

        public int TicksRemaining => deadlineTick - GenTicks.TicksGame;

        public float DaysRemaining => TicksRemaining / (float)GenDate.TicksPerDay;

        public bool IsOverdue(int nowTick) => nowTick >= deadlineTick;

        /// <summary>
        /// Payment for a partial hand-over, rounded down so the colony is never overpaid by
        /// rounding across several deliveries.
        /// </summary>
        public int PaymentFor(int units)
        {
            return Mathf.FloorToInt(unitPrice * units);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", 0);
            Scribe_Values.Look(ref opportunityId, "opportunityId", 0);
            Scribe_Values.Look(ref settlementId, "settlementId", -1);
            Scribe_Values.Look(ref settlementName, "settlementName", "");
            Scribe_Values.Look(ref factionName, "factionName", "");
            Scribe_Defs.Look(ref thingDef, "thingDef");
            Scribe_Values.Look(ref quantity, "quantity", 0);
            Scribe_Values.Look(ref unitPrice, "unitPrice", 0f);
            Scribe_Values.Look(ref acceptedTick, "acceptedTick", 0);
            Scribe_Values.Look(ref deadlineTick, "deadlineTick", 0);
            Scribe_Values.Look(ref status, "status", SalesOrderStatus.Accepted);
            Scribe_Values.Look(ref deliveredQuantity, "deliveredQuantity", 0);
            Scribe_Values.Look(ref paidSilver, "paidSilver", 0);
            Scribe_Values.Look(ref outcomeNote, "outcomeNote", "");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (settlementName == null) settlementName = "";
                if (factionName == null) factionName = "";
                if (outcomeNote == null) outcomeNote = "";
            }
        }

        /// <summary>A missing def means the mod supplying the item was removed (§64, §86).</summary>
        public bool IsValidAfterLoad => thingDef != null && quantity > 0;

        public override string ToString()
        {
            return $"#{id} {settlementName}: {deliveredQuantity}/{quantity} " +
                   $"{thingDef?.label ?? "<missing def>"} [{status}]";
        }
    }
}
