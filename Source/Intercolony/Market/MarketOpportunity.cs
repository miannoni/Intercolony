using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Lifecycle of an opportunity (DESIGN.md §73). An offer is live while Available, and its
    /// authoritative terminal transitions are expiry, acceptance, or player decline.
    /// </summary>
    public enum MarketOpportunityState
    {
        Available,
        Expired,

        /// <summary>
        /// Converted into a binding Sales Order. Terminal: an offer is consumed by acceptance
        /// and can never be taken again (§14 Available -> Accepted, §76.1 exploit resistance).
        /// </summary>
        Accepted,

        /// <summary>
        /// Declined by the player. Terminal: a declined offer is removed from the live market
        /// rather than being left as an apparently actionable stale row.
        /// </summary>
        Declined
    }

    /// <summary>
    /// The finite pre-acceptance negotiation states for an opportunity. There is deliberately no
    /// state that permits another player counter after a counterparty response: the absence of that
    /// state is the construction that prevents an infinite back-and-forth loop.
    /// </summary>
    public enum MarketOpportunityNegotiationState
    {
        None,

        /// <summary>One final counter is waiting for the player's accept or decline.</summary>
        CounterpartyCountered,

        /// <summary>
        /// The counterparty refused the one player counter. The original offer remains available,
        /// but this opportunity cannot be countered again.
        /// </summary>
        CounterpartyRefused
    }

    /// <summary>
    /// A temporary, non-binding indication that a counterparty wants to buy something
    /// (DESIGN.md §7.2). Persisted, because §61 lists active market opportunities as state
    /// that must survive save/load.
    ///
    /// Carries enough information to become a Sales Order later (§11), and a pre-computed
    /// price explanation so the UI never has to re-derive pricing (§46, §47).
    /// </summary>
    public class MarketOpportunity : IExposable
    {
        public int id;

        /// <summary>Stable <c>WorldObject.ID</c> of the buying settlement.</summary>
        public int settlementId;

        /// <summary>
        /// Cached buyer name. The settlement can be destroyed while the opportunity is still
        /// listed (§87), and the UI should still be able to say who wanted the goods.
        /// </summary>
        public string settlementName = "";

        public ThingDef thingDef;
        public int quantity;
        public float unitPrice;

        /// <summary>
        /// Minimum quality demanded, or null if the buyer does not care (§11, §99).
        /// Carried on the opportunity so the market table can show it before acceptance —
        /// a player must be able to see they are committing to Excellent work.
        /// </summary>
        public QualityCategory? minQuality;

        /// <summary>Required material, or null for any (§101 material-aware valuation).</summary>
        public ThingDef stuffDef;

        /// <summary>Minimum condition as a fraction of max hit points, or zero for any.</summary>
        public float minHitPointsPercent;

        /// <summary>
        /// Who moves the goods (§25). Advertised before acceptance because it changes both the
        /// price and what the player has to do — it is half the decision.
        /// </summary>
        public FulfillmentMode fulfillment = FulfillmentMode.SellerDelivery;

        public int createdTick;
        public int expiryTick;

        /// <summary>Days the buyer would allow for delivery once this became an order (§17).</summary>
        public int deadlineDays;

        /// <summary>
        /// Approximate world tiles from the player's home at generation time, or -1 if unknown
        /// (§48). Stored rather than recomputed so the market table can sort and filter on it
        /// without a pathfinding call per row per frame (§84).
        /// </summary>
        public float distanceTiles = -1f;

        public MarketOpportunityState state = MarketOpportunityState.Available;

        /// <summary>
        /// The only persisted negotiation state needed after the evaluator answers. The player's
        /// proposed terms do not need to survive separately: a final counter is either stored below
        /// or the opportunity has a terminal response, while the original terms remain above.
        /// </summary>
        private MarketOpportunityNegotiationState negotiationState =
            MarketOpportunityNegotiationState.None;

        /// <summary>
        /// The counterparty's one final counter, present only while negotiationState is
        /// CounterpartyCountered. These are separate persisted values because the evaluator terms
        /// model is intentionally an ephemeral input model, not a saved entity.
        /// </summary>
        private int finalCounterQuantity;
        private float finalCounterUnitPrice;
        private int finalCounterDeadlineDays;
        private FulfillmentMode finalCounterFulfillment = FulfillmentMode.SellerDelivery;

        /// <summary>Human-readable price factor breakdown, built once at creation (§47).</summary>
        public string priceExplanation = "";

        /// <summary>Required by Scribe for deep-loaded children.</summary>
        public MarketOpportunity()
        {
        }

        public int TotalPrice => Mathf.RoundToInt(unitPrice * quantity);

        public int TicksRemaining => expiryTick - GenTicks.TicksGame;

        public float DaysRemaining => TicksRemaining / (float)GenDate.TicksPerDay;

        public bool IsAvailable => state == MarketOpportunityState.Available;

        public MarketOpportunityNegotiationState NegotiationState => negotiationState;

        public bool HasPendingCounterpartyCounter =>
            IsAvailable && negotiationState == MarketOpportunityNegotiationState.CounterpartyCountered;

        /// <summary>
        /// The original offer can be countered only before the counterparty has answered. This
        /// finite gate is the state-machine boundary, rather than a convention callers must obey.
        /// </summary>
        public bool CanSubmitCounter =>
            IsAvailable && negotiationState == MarketOpportunityNegotiationState.None;

        /// <summary>
        /// A refused counter leaves the advertised terms intact, so the player may still accept
        /// those original terms but cannot reopen negotiation on the same listing.
        /// </summary>
        public bool CanAcceptOriginalTerms =>
            IsAvailable &&
            (negotiationState == MarketOpportunityNegotiationState.None ||
             negotiationState == MarketOpportunityNegotiationState.CounterpartyRefused);

        /// <summary>
        /// Current sale opportunities are fungible goods with both existing sale-side logistics
        /// paths. Deriving this from the same eligibility gate avoids persisting a duplicate mode
        /// capability flag, while an unsupported future opportunity naturally cannot change mode.
        /// </summary>
        public bool SupportsBothFulfillmentModes =>
            thingDef != null && IntercolonyProductClassifier.IsFungibleTradeItem(thingDef);

        public bool HasConditionConstraint => minHitPointsPercent != 0f;

        public bool HasExpired(int nowTick)
        {
            return nowTick >= expiryTick;
        }

        /// <summary>
        /// Consumes the offer. Returns false if it has already been taken or has lapsed.
        ///
        /// The guard lives on the opportunity itself rather than on the list that holds it,
        /// because removal from a list cannot stop a caller that already has a reference —
        /// a UI row captured earlier in the frame, or a second click on a confirmation
        /// dialog. Two orders off one offer is a duplication exploit (§76.1).
        /// </summary>
        public bool TryAccept()
        {
            if (!CanAcceptOriginalTerms)
            {
                IntercolonyLog.Warning(
                    $"Opportunity {id} is {state}/{negotiationState}; refusing to accept original terms.");
                return false;
            }

            state = MarketOpportunityState.Accepted;
            ClearNegotiation();
            return true;
        }

        /// <summary>
        /// Claims the opportunity for terms returned by the evaluator. It is separate from
        /// TryAccept so a caller cannot use an ordinary accept to bypass a pending final counter.
        /// </summary>
        internal bool TryAcceptNegotiated(
            IntercolonyNegotiationTerms agreedTerms, bool acceptingFinalCounter)
        {
            if (!IsAvailable)
            {
                IntercolonyLog.Warning(
                    $"Opportunity {id} is already {state}; refusing negotiated acceptance.");
                return false;
            }

            if (acceptingFinalCounter)
            {
                if (!HasPendingCounterpartyCounter || !MatchesFinalCounter(agreedTerms))
                {
                    IntercolonyLog.Warning(
                        $"Opportunity {id} has no matching final counter to accept.");
                    return false;
                }
            }
            else if (!CanSubmitCounter)
            {
                IntercolonyLog.Warning(
                    $"Opportunity {id} is {negotiationState}; refusing negotiated acceptance.");
                return false;
            }

            state = MarketOpportunityState.Accepted;
            ClearNegotiation();
            return true;
        }

        /// <summary>Declines any still-available branch, including a pending final counter.</summary>
        public bool TryDecline()
        {
            if (!IsAvailable)
            {
                IntercolonyLog.Warning($"Opportunity {id} is {state}; refusing to decline again.");
                return false;
            }

            state = MarketOpportunityState.Declined;
            ClearNegotiation();
            return true;
        }

        /// <summary>
        /// Stores the evaluator's single final counter. The state transition has no outgoing
        /// player-counter edge, so calling the counter action again cannot create another round.
        /// </summary>
        internal bool TryRecordFinalCounter(IntercolonyNegotiationTerms terms)
        {
            if (!CanSubmitCounter || terms == null)
            {
                return false;
            }

            finalCounterQuantity = terms.quantity;
            finalCounterUnitPrice = terms.unitPrice;
            finalCounterDeadlineDays = terms.deadlineDays;
            finalCounterFulfillment = terms.fulfillment;
            negotiationState = MarketOpportunityNegotiationState.CounterpartyCountered;
            return true;
        }

        /// <summary>Records a refusal while retaining the untouched original offer.</summary>
        internal bool TryRecordCounterpartyRefusal()
        {
            if (!CanSubmitCounter)
            {
                return false;
            }

            negotiationState = MarketOpportunityNegotiationState.CounterpartyRefused;
            return true;
        }

        public bool TryGetFinalCounterTerms(out IntercolonyNegotiationTerms terms)
        {
            if (!HasPendingCounterpartyCounter)
            {
                terms = null;
                return false;
            }

            terms = new IntercolonyNegotiationTerms(
                finalCounterQuantity,
                finalCounterUnitPrice,
                finalCounterDeadlineDays,
                finalCounterFulfillment);
            return true;
        }

        internal bool MatchesFinalCounter(IntercolonyNegotiationTerms terms)
        {
            return terms != null &&
                   HasPendingCounterpartyCounter &&
                   terms.quantity == finalCounterQuantity &&
                   Mathf.Approximately(terms.unitPrice, finalCounterUnitPrice) &&
                   terms.deadlineDays == finalCounterDeadlineDays &&
                   terms.fulfillment == finalCounterFulfillment;
        }

        private void ClearNegotiation()
        {
            negotiationState = MarketOpportunityNegotiationState.None;
            finalCounterQuantity = 0;
            finalCounterUnitPrice = 0f;
            finalCounterDeadlineDays = 0;
            finalCounterFulfillment = FulfillmentMode.SellerDelivery;
        }

        /// <summary>
        /// The only legal transition to Expired. Returns false and logs rather than permitting
        /// an impossible state change (DESIGN.md §73).
        /// </summary>
        public bool TryExpire()
        {
            if (state != MarketOpportunityState.Available)
            {
                IntercolonyLog.Warning($"Opportunity {id} is already {state}; refusing to expire again.");
                return false;
            }

            state = MarketOpportunityState.Expired;
            return true;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", 0);
            Scribe_Values.Look(ref settlementId, "settlementId", -1);
            Scribe_Values.Look(ref settlementName, "settlementName", "");
            Scribe_Defs.Look(ref thingDef, "thingDef");
            Scribe_Values.Look(ref quantity, "quantity", 0);
            Scribe_Values.Look(ref unitPrice, "unitPrice", 0f);
            Scribe_Values.Look(ref createdTick, "createdTick", 0);
            Scribe_Values.Look(ref expiryTick, "expiryTick", 0);
            Scribe_Values.Look(ref deadlineDays, "deadlineDays", 0);
            Scribe_Values.Look(ref distanceTiles, "distanceTiles", -1f);
            Scribe_Values.Look(ref minQuality, "minQuality");
            Scribe_Defs.Look(ref stuffDef, "stuffDef");
            Scribe_Values.Look(ref minHitPointsPercent, "minHitPointsPercent", 0f);
            Scribe_Values.Look(ref fulfillment, "fulfillment", FulfillmentMode.SellerDelivery);
            Scribe_Values.Look(ref state, "state", MarketOpportunityState.Available);
            Scribe_Values.Look(
                ref negotiationState,
                "negotiationState",
                MarketOpportunityNegotiationState.None);
            Scribe_Values.Look(ref finalCounterQuantity, "finalCounterQuantity", 0);
            Scribe_Values.Look(ref finalCounterUnitPrice, "finalCounterUnitPrice", 0f);
            Scribe_Values.Look(ref finalCounterDeadlineDays, "finalCounterDeadlineDays", 0);
            Scribe_Values.Look(
                ref finalCounterFulfillment,
                "finalCounterFulfillment",
                FulfillmentMode.SellerDelivery);
            Scribe_Values.Look(ref priceExplanation, "priceExplanation", "");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (settlementName == null)
                {
                    settlementName = "";
                }

                if (priceExplanation == null)
                {
                    priceExplanation = "";
                }

                if (negotiationState == MarketOpportunityNegotiationState.CounterpartyCountered &&
                    (finalCounterQuantity <= 0 ||
                     finalCounterUnitPrice <= 0f ||
                     float.IsNaN(finalCounterUnitPrice) ||
                     float.IsInfinity(finalCounterUnitPrice) ||
                     finalCounterDeadlineDays < 0))
                {
                    // A partial or corrupt final counter cannot be safely reconstructed. Returning
                    // to the untouched original offer is safer than presenting terms the player
                    // never actually received, and does not fabricate a new negotiation.
                    IntercolonyLog.Warning(
                        $"Opportunity {id} had invalid persisted final counter terms; " +
                        "clearing its negotiation state.");
                    ClearNegotiation();
                }
            }
        }

        /// <summary>
        /// Whether this survived loading intact. A null <see cref="thingDef"/> means the def
        /// no longer exists — typically a mod was removed since the save (DESIGN.md §64, §86).
        /// </summary>
        public bool IsValidAfterLoad => thingDef != null && quantity > 0;

        /// <summary>Item plus any constraint, e.g. "Longsword (plasteel, excellent+)".</summary>
        public string ItemLabel()
        {
            string label = thingDef?.LabelCap.ToString() ?? "<missing def>";
            if (stuffDef == null && !minQuality.HasValue && !HasConditionConstraint)
            {
                return label;
            }

            List<string> parts = new List<string>();
            if (stuffDef != null)
            {
                parts.Add(stuffDef.label);
            }

            if (minQuality.HasValue)
            {
                parts.Add(minQuality.Value.GetLabel() + "+");
            }

            if (HasConditionConstraint)
            {
                parts.Add($"{Mathf.RoundToInt(minHitPointsPercent * 100f)}%+ cond");
            }

            return $"{label} ({string.Join(", ", parts.ToArray())})";
        }

        /// <summary>
        /// Whether this is a crated good — furniture, art, equipment. Used by the UI to warn
        /// about caravan mass, since each one travels as its own crate.
        /// </summary>
        public bool IsCratedGood => thingDef != null && thingDef.category == ThingCategory.Building;

        public override string ToString()
        {
            return $"#{id} {settlementName} wants {quantity}x {ItemLabel()} " +
                   $"@ {unitPrice:F2} = {TotalPrice} silver [{state}]";
        }
    }
}
