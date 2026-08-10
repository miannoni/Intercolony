using System.Collections.Generic;
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
        Cancelled,

        /// <summary>
        /// Buyer-pickup only: the player has declared the goods ready and the buyer's caravan
        /// is on its way (§25.2). Distinct from Accepted because the player can no longer
        /// quietly consume the stock — someone is coming for it.
        /// </summary>
        AwaitingCollection
    }

    /// <summary>
    /// Who moves the goods (DESIGN.md §25). The two modes are a genuine trade-off rather
    /// than flavour: seller delivery pays more but costs caravan time, travel and risk;
    /// buyer pickup pays less but costs nothing but patience.
    /// </summary>
    public enum FulfillmentMode
    {
        /// <summary>§25.1 — the player hauls the goods to the buyer.</summary>
        SellerDelivery,

        /// <summary>§25.2 — the buyer sends a caravan once the player declares the goods ready.</summary>
        BuyerPickup
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

        /// <summary>
        /// Recurring contract this delivery belongs to, or 0 for a one-off. Lets the Orders
        /// tab show that a deadline is a contract obligation rather than a spot sale.
        /// </summary>
        public int contractId;

        public int settlementId;
        public string settlementName = "";
        public string factionName = "";

        /// <summary>The player colony this order is fulfilled from.</summary>
        public Map fulfillmentMap;

        /// <summary>
        /// The specific animals the player set aside when marking an animal order ready for
        /// buyer collection, or null for every other kind of order.
        ///
        /// Animals are individuals, so unlike goods a head count is not enough: the buyer
        /// collects these animals, not any matching ones. References rather than deep saves,
        /// because the map already owns these pawns (see docs/LABOR_TECHNICAL_NOTES.md on
        /// ownership). A reference that fails to resolve becomes null and is treated as an
        /// animal that is no longer there, which is exactly what it means.
        /// </summary>
        public List<Pawn> designatedAnimals;

        /// <summary>
        /// What is being sold, including any quality, material or condition constraints
        /// (§15 lineItems). Phase 6 carries exactly one line; §15's multi-line model is a
        /// later addition, and a list of one would be abstraction ahead of a second use case.
        /// </summary>
        public OrderLine line = new OrderLine();

        /// <summary>Agreed unit price, locked at acceptance so later market drift cannot change the deal.</summary>
        public float unitPrice;

        public int acceptedTick;
        public int deadlineTick;

        public SalesOrderStatus status = SalesOrderStatus.Accepted;

        /// <summary>How the goods move (§25). Fixed at acceptance; the price already reflects it.</summary>
        public FulfillmentMode fulfillment = FulfillmentMode.SellerDelivery;

        /// <summary>Buyer-pickup only: tick the buyer's caravan arrives. -1 until goods are declared ready.</summary>
        public int buyerArrivalTick = -1;

        /// <summary>How much has actually been handed over. Partial deliveries accumulate.</summary>
        public int deliveredQuantity;

        /// <summary>Silver actually paid out so far.</summary>
        public int paidSilver;

        /// <summary>Set when the order ends, for the orders list and any later dispute handling.</summary>
        public string outcomeNote = "";

        public SalesOrder()
        {
        }

        /// <summary>Convenience passthroughs so call sites read naturally.</summary>
        public ThingDef ThingDef => line?.thingDef;

        public int Quantity => line?.quantity ?? 0;

        public bool IsAnimalOrder => line?.IsAnimalOrder == true;

        public int TotalPayment => Mathf.RoundToInt(unitPrice * Quantity);

        public int RemainingQuantity => Mathf.Max(0, Quantity - deliveredQuantity);

        public bool IsOpen => status == SalesOrderStatus.Accepted ||
                              status == SalesOrderStatus.AwaitingCollection;

        /// <summary>
        /// Whether this order was created by Find Buyer rather than from a Market listing or
        /// a recurring contract. Provenance is derived from the two existing source IDs so old
        /// saves and new orders agree without introducing another persisted source of truth.
        /// </summary>
        public bool IsDirectFindBuyerSale => opportunityId == 0 && contractId == 0;

        /// <summary>Buyer pickup where the player has not yet declared the goods ready.</summary>
        public bool CanMarkReady => status == SalesOrderStatus.Accepted &&
                                    fulfillment == FulfillmentMode.BuyerPickup;

        public bool BuyerEnRoute => status == SalesOrderStatus.AwaitingCollection;

        public float DaysUntilBuyerArrives =>
            buyerArrivalTick < 0 ? -1f : (buyerArrivalTick - GenTicks.TicksGame) / (float)GenDate.TicksPerDay;

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
            Scribe_Values.Look(ref contractId, "contractId", 0);
            Scribe_Values.Look(ref settlementId, "settlementId", -1);
            Scribe_Values.Look(ref settlementName, "settlementName", "");
            Scribe_Values.Look(ref factionName, "factionName", "");
            Scribe_References.Look(ref fulfillmentMap, "fulfillmentMap");
            Scribe_Collections.Look(
                ref designatedAnimals, "designatedAnimals", LookMode.Reference);
            Scribe_Deep.Look(ref line, "line");
            Scribe_Values.Look(ref unitPrice, "unitPrice", 0f);

            // Schema 6 stored the item and quantity directly on the order. Read the legacy
            // nodes so an order accepted before Phase 6 is not silently emptied — §62 forbids
            // dropping active obligations, and an order whose line vanished would be exactly
            // that, a promise the player can no longer fulfil.
            ThingDef legacyDef = null;
            int legacyQuantity = 0;
            Scribe_Defs.Look(ref legacyDef, "thingDef");
            Scribe_Values.Look(ref legacyQuantity, "quantity", 0);
            Scribe_Values.Look(ref acceptedTick, "acceptedTick", 0);
            Scribe_Values.Look(ref deadlineTick, "deadlineTick", 0);
            Scribe_Values.Look(ref status, "status", SalesOrderStatus.Accepted);
            Scribe_Values.Look(ref fulfillment, "fulfillment", FulfillmentMode.SellerDelivery);
            Scribe_Values.Look(ref buyerArrivalTick, "buyerArrivalTick", -1);
            Scribe_Values.Look(ref deliveredQuantity, "deliveredQuantity", 0);
            Scribe_Values.Look(ref paidSilver, "paidSilver", 0);
            Scribe_Values.Look(ref outcomeNote, "outcomeNote", "");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (settlementName == null) settlementName = "";
                if (factionName == null) factionName = "";
                if (outcomeNote == null) outcomeNote = "";

                if (line == null || line.thingDef == null)
                {
                    if (legacyDef != null && legacyQuantity > 0)
                    {
                        line = new OrderLine(legacyDef, legacyQuantity);
                        IntercolonyLog.Message(
                            $"Order {id}: migrated schema-6 item fields into an order line.");
                    }
                    else if (line == null)
                    {
                        line = new OrderLine();
                    }
                }
            }
        }

        /// <summary>A missing def means the mod supplying the item was removed (§64, §86).</summary>
        public bool IsValidAfterLoad => TryValidateAfterLoad(out _);

        public bool TryValidateAfterLoad(out string reason)
        {
            if (line?.thingDef == null)
            {
                reason = IsAnimalOrder ? "missing race definition" : "missing item definition";
                return false;
            }

            if (line.quantity <= 0)
            {
                reason = "non-positive quantity";
                return false;
            }

            if (IsAnimalOrder && !line.animalSpec.TryValidateFor(
                    line.thingDef, requireKind: false, out reason))
            {
                return false;
            }

            reason = null;
            return true;
        }

        public override string ToString()
        {
            return $"#{id} {settlementName}: {deliveredQuantity}/{Quantity} " +
                   $"{line?.ShortLabel() ?? "<missing def>"} [{status}]";
        }
    }
}
