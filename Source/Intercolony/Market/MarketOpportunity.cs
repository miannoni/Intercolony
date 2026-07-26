using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Lifecycle of an opportunity (DESIGN.md §73). Phase 4 has no acceptance step — turning
    /// an opportunity into a binding Sales Order is Phase 5 (§98) — so the only transition is
    /// Available -> Expired.
    /// </summary>
    public enum MarketOpportunityState
    {
        Available,
        Expired,

        /// <summary>
        /// Converted into a binding Sales Order. Terminal: an offer is consumed by acceptance
        /// and can never be taken again (§14 Available -> Accepted, §76.1 exploit resistance).
        /// </summary>
        Accepted
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
            if (state != MarketOpportunityState.Available)
            {
                IntercolonyLog.Warning($"Opportunity {id} is already {state}; refusing to accept again.");
                return false;
            }

            state = MarketOpportunityState.Accepted;
            return true;
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
            Scribe_Values.Look(ref state, "state", MarketOpportunityState.Available);
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
            }
        }

        /// <summary>
        /// Whether this survived loading intact. A null <see cref="thingDef"/> means the def
        /// no longer exists — typically a mod was removed since the save (DESIGN.md §64, §86).
        /// </summary>
        public bool IsValidAfterLoad => thingDef != null && quantity > 0;

        /// <summary>Item plus any constraint, e.g. "plasteel longsword (Excellent+)".</summary>
        public string ItemLabel()
        {
            string label = thingDef?.LabelCap.ToString() ?? "<missing def>";
            return minQuality.HasValue ? $"{label} ({minQuality.Value.GetLabel()}+)" : label;
        }

        public override string ToString()
        {
            return $"#{id} {settlementName} wants {quantity}x {ItemLabel()} " +
                   $"@ {unitPrice:F2} = {TotalPrice} silver [{state}]";
        }
    }
}
