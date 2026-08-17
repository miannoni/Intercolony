using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>RFQ lifecycle (DESIGN.md §19, §73). Terminal states are terminal.</summary>
    public enum PurchaseRequestStatus
    {
        /// <summary>Sent out; suppliers have answered and the quotes stand until expiry.</summary>
        Open,

        /// <summary>Lapsed without the player acting.</summary>
        Expired,

        /// <summary>Withdrawn by the player.</summary>
        Cancelled,

        /// <summary>
        /// Filled: the player has ordered the entire requested quantity. This
        /// was reported as <see cref="Cancelled"/> until 2026-08-10, which told the player they
        /// had abandoned the very requests they had successfully acted on.
        /// </summary>
        Ordered
    }

    /// <summary>Logistics terms stated by the player when an RFQ is raised.</summary>
    public enum ProcurementFulfillmentPreference
    {
        /// <summary>Each supplier chooses whether to deliver or offer collection.</summary>
        Either,

        /// <summary>Only supplier-delivery quotations are requested.</summary>
        SupplierDelivers,

        /// <summary>Only quotations for collection by the player are requested.</summary>
        PlayerPickup
    }

    /// <summary>
    /// One supplier's answer to a request (DESIGN.md §7.5, §19).
    ///
    /// A quote is a *response*, not an obligation: nothing is committed until the player
    /// accepts one, which is Phase 11 (§104). Quantities can fall short of what was asked —
    /// §20 explicitly lists partial quotations as an outcome.
    /// </summary>
    public class Quotation : IExposable
    {
        public int id;
        public int settlementId;
        public string settlementName = "";
        public string factionName = "";

        /// <summary>Units this supplier can actually provide. May be less than requested.</summary>
        public int quantityOffered;

        public float unitPrice;

        /// <summary>Days before the goods are ready or arrive, depending on the mode.</summary>
        public int leadTimeDays;

        /// <summary>True when the supplier delivers (§25.4); false when the player collects (§25.3).</summary>
        public bool supplierDelivers;

        /// <summary>
        /// Quality the supplier is offering, or null for goods that cannot carry one.
        /// §20 lists differing quality as an RFQ outcome, and fixing it at quote time is what
        /// lets the player compare "cheap and shoddy" against "dear and good" — and what makes
        /// §104's "preserve expected properties" checkable, since the promise is on record.
        /// </summary>
        public QualityCategory? offeredQuality;

        /// <summary>Material the supplier would provide, or null when the def has no stuff.</summary>
        public ThingDef offeredStuff;

        public float distanceTiles = -1f;

        /// <summary>Price factor breakdown, for the §47 tooltip.</summary>
        public string priceExplanation = "";

        /// <summary>Animal promise offered by this supplier, or null for goods.</summary>
        public AnimalSpec animalSpec;

        public Quotation()
        {
        }

        public int TotalPrice => Mathf.RoundToInt(unitPrice * quantityOffered);

        public string FulfillmentLabel => supplierDelivers ? "delivered" : "pickup";

        public bool IsAnimalOrder => animalSpec != null;

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", 0);
            Scribe_Values.Look(ref settlementId, "settlementId", -1);
            Scribe_Values.Look(ref settlementName, "settlementName", "");
            Scribe_Values.Look(ref factionName, "factionName", "");
            Scribe_Values.Look(ref quantityOffered, "quantityOffered", 0);
            Scribe_Values.Look(ref unitPrice, "unitPrice", 0f);
            Scribe_Values.Look(ref leadTimeDays, "leadTimeDays", 0);
            Scribe_Values.Look(ref supplierDelivers, "supplierDelivers", false);
            Scribe_Values.Look(ref offeredQuality, "offeredQuality");
            Scribe_Defs.Look(ref offeredStuff, "offeredStuff");
            Scribe_Values.Look(ref distanceTiles, "distanceTiles", -1f);
            Scribe_Values.Look(ref priceExplanation, "priceExplanation", "");
            Scribe_Deep.Look(ref animalSpec, "animalSpec");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (settlementName == null) settlementName = "";
                if (factionName == null) factionName = "";
                if (priceExplanation == null) priceExplanation = "";
            }
        }

        public bool TryValidateForRequest(
            ThingDef requestRace, bool requestIsAnimal, out string reason)
        {
            if (!requestIsAnimal)
            {
                if (IsAnimalOrder)
                {
                    reason = "animal quotation is attached to a goods request";
                    return false;
                }

                reason = null;
                return true;
            }

            if (!IsAnimalOrder)
            {
                reason = "missing animal specification and pawn kind";
                return false;
            }

            return animalSpec.TryValidateFor(requestRace, requireKind: true, out reason);
        }

        public override string ToString()
        {
            return $"{settlementName}: {quantityOffered}x @ {unitPrice:F2} = {TotalPrice} " +
                   $"({FulfillmentLabel}, {leadTimeDays}d)";
        }
    }

    /// <summary>
    /// A stated need put to known counterparties (DESIGN.md §7.4, §19).
    ///
    /// "The purchase side is deliberately not a store catalog. The player states a need."
    /// Quotes are generated once, when the request is made, and then stand until expiry —
    /// re-rolling them on demand would let a player reroll for a better price, which §76.1
    /// warns against.
    /// </summary>
    public class PurchaseRequest : IExposable
    {
        public int id;

        public ThingDef thingDef;

        /// <summary>Required material, or null for any.</summary>
        public ThingDef stuffDef;

        /// <summary>
        /// Lowest workmanship the player will accept, or null to take whatever is offered.
        /// A settlement that cannot work to that standard does not quote at all, so the floor
        /// narrows the field rather than silently costing more.
        /// </summary>
        public QualityCategory? minQuality;

        public int quantityRequested;

        /// <summary>Units already committed through accepted quotations.</summary>
        public int quantityOrdered;

        /// <summary>How soon the player wants it. Suppliers who cannot meet it still quote (§19).</summary>
        public int desiredDays;

        public int createdTick;
        public int expiryTick;

        public PurchaseRequestStatus status = PurchaseRequestStatus.Open;

        public ProcurementFulfillmentPreference fulfillmentPreference =
            ProcurementFulfillmentPreference.Either;

        public List<Quotation> quotes = new List<Quotation>();

        /// <summary>Set when nobody answered, so the UI can explain rather than show an empty list.</summary>
        public string noResponseReason = "";

        /// <summary>Requested animal constraints, or null for goods.</summary>
        public AnimalSpec animalSpec;

        public PurchaseRequest()
        {
        }

        public bool IsOpen => status == PurchaseRequestStatus.Open;

        public int QuantityOutstanding => Mathf.Max(0, quantityRequested - quantityOrdered);

        public bool IsAnimalOrder => animalSpec != null;

        public int TicksRemaining => expiryTick - GenTicks.TicksGame;

        public float DaysRemaining => TicksRemaining / (float)GenDate.TicksPerDay;

        public bool HasExpired(int nowTick) => nowTick >= expiryTick;

        public bool AnyQuotes => quotes.Count > 0;

        /// <summary>Best total-value quote that covers the full request, or null if none does.</summary>
        public Quotation BestCompleteQuote
        {
            get
            {
                Quotation best = null;
                foreach (Quotation quote in quotes)
                {
                    if (quote.quantityOffered < QuantityOutstanding)
                    {
                        continue;
                    }

                    if (best == null || quote.TotalPrice < best.TotalPrice)
                    {
                        best = quote;
                    }
                }

                return best;
            }
        }

        /// <summary>The only legal transition to Expired (§73).</summary>
        public bool TryExpire()
        {
            if (status != PurchaseRequestStatus.Open)
            {
                IntercolonyLog.Warning($"Request {id} is already {status}; refusing to expire again.");
                return false;
            }

            status = PurchaseRequestStatus.Expired;
            return true;
        }

        public bool TryCancel()
        {
            if (status != PurchaseRequestStatus.Open)
            {
                return false;
            }

            status = PurchaseRequestStatus.Cancelled;
            return true;
        }

        public string ItemLabel()
        {
            string label = thingDef?.LabelCap.ToString() ?? "<missing def>";
            return stuffDef != null ? $"{label} ({stuffDef.label})" : label;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", 0);
            Scribe_Defs.Look(ref thingDef, "thingDef");
            Scribe_Defs.Look(ref stuffDef, "stuffDef");
            Scribe_Values.Look(ref minQuality, "minQuality");
            Scribe_Values.Look(ref quantityRequested, "quantityRequested", 0);
            Scribe_Values.Look(ref quantityOrdered, "quantityOrdered", 0);
            Scribe_Values.Look(ref desiredDays, "desiredDays", 0);
            Scribe_Values.Look(ref createdTick, "createdTick", 0);
            Scribe_Values.Look(ref expiryTick, "expiryTick", 0);
            Scribe_Values.Look(ref status, "status", PurchaseRequestStatus.Open);
            Scribe_Values.Look(ref fulfillmentPreference, "fulfillmentPreference",
                ProcurementFulfillmentPreference.Either);
            Scribe_Values.Look(ref noResponseReason, "noResponseReason", "");
            Scribe_Deep.Look(ref animalSpec, "animalSpec");
            Scribe_Collections.Look(ref quotes, "quotes", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (noResponseReason == null)
                {
                    noResponseReason = "";
                }

                // A missing or IsNull list node loads as null, not empty.
                if (quotes == null)
                {
                    quotes = new List<Quotation>();
                }
                else
                {
                    quotes.RemoveAll(q => q == null);
                }
            }
        }

        /// <summary>A missing def means the mod supplying the item was removed (§64, §86).</summary>
        public bool IsValidAfterLoad => TryValidateAfterLoad(out _);

        public bool TryValidateAfterLoad(out string reason)
        {
            if (thingDef == null)
            {
                reason = IsAnimalOrder ? "missing race definition" : "missing item definition";
                return false;
            }

            if (quantityRequested <= 0)
            {
                reason = "non-positive quantity";
                return false;
            }

            if (IsAnimalOrder && !animalSpec.TryValidateFor(thingDef, requireKind: true, out reason))
            {
                return false;
            }

            reason = null;
            return true;
        }

        public override string ToString()
        {
            return $"#{id} {quantityRequested}x {ItemLabel()} [{status}] " +
                   $"{quotes.Count} quote(s)";
        }
    }
}
