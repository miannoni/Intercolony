using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Contract lifecycle (DESIGN.md §29, §30, §73).
    ///
    /// <c>Offered</c> is a proposal the player has not answered; everything else is a live
    /// agreement or its ending. Breach is distinct from cancellation because §30 lists breach
    /// conditions separately, and the two should not cost the same.
    /// </summary>
    public enum ContractStatus
    {
        Offered,
        Active,
        Completed,
        Breached,
        Cancelled,

        /// <summary>Proposal lapsed unanswered.</summary>
        Declined,

        /// <summary>
        /// Paused because the counterparty went to war (§88, §113). Not terminal: no deliveries are
        /// due, none count as missed, and the agreement resumes with every remaining cycle intact if
        /// relations recover. See <see cref="HostilityPolicy"/> for why war suspends a relationship
        /// but cancels a transaction.
        /// </summary>
        Suspended
    }

    /// <summary>
    /// A standing supply agreement (DESIGN.md §29, §107).
    ///
    /// §29's design objective is strategic rather than transactional: "A future demand
    /// commitment causes the player to expand capacity." So the point of this entity is that
    /// it is *known in advance* — the player can see that 900 units are due every quadrum for
    /// a year and build a farm around it.
    ///
    /// Deliberately the simple version §30 prescribes: fixed quantity, fixed cadence, fixed
    /// duration, fixed price formula. Category selectors, quantity ranges and negotiated price
    /// rules are all listed in §30 as later additions.
    /// </summary>
    public class RecurringContract : IExposable
    {
        public int id;

        public int settlementId;
        public string settlementName = "";
        public string factionName = "";

        public ThingDef thingDef;
        public ThingDef stuffDef;
        public QualityCategory? minQuality;

        /// <summary>Units due each cycle. Fixed, per §30's "start simple".</summary>
        public int quantityPerCycle;

        /// <summary>Cycle length in ticks. One quadrum by default (§107).</summary>
        public int cadenceTicks = GenDate.TicksPerQuadrum;

        public int totalCycles;
        public int cyclesCompleted;
        public int cyclesFailed;

        /// <summary>Agreed rate, locked for the contract's life — that is what makes it plannable.</summary>
        public float unitPrice;

        /// <summary>Fraction of each cycle's agreed value waived when silver is paid, from 0 to 1.</summary>
        private float discountFraction;

        public float DiscountFraction
        {
            get => discountFraction;
            set => discountFraction = float.IsNaN(value) ? 0f : Mathf.Clamp01(value);
        }

        /// <summary>
        /// How every cycle's order is fulfilled, chosen once when the agreement is accepted.
        /// A standing agreement is a production commitment, so the logistics of it are settled
        /// up front rather than re-decided every cycle.
        /// </summary>
        public FulfillmentMode fulfillment = FulfillmentMode.SellerDelivery;

        public ContractStatus status = ContractStatus.Offered;

        /// <summary>Tick the next delivery window opens.</summary>
        public int nextCycleTick;

        /// <summary>Tick this proposal stops being available.</summary>
        public int offerExpiryTick;

        /// <summary>
        /// When a war suspended the agreement, or 0. Read on resume to move the cycle clock forward
        /// by the length of the outage, so no delivery is lost to the suspension (§88, §113).
        /// </summary>
        public int suspendedTick;

        // --- Renewal (§115, §107) ----------------------------------------------------------

        /// <summary>The settlement has offered another run of the same agreement.</summary>
        public bool renewalOffered;

        /// <summary>When that offer lapses. §115: an offer must not sit unanswered forever.</summary>
        public int renewalExpiryTick;

        /// <summary>Runs of this agreement beyond the first.</summary>
        public int renewals;

        public float DaysUntilRenewalExpires =>
            (renewalExpiryTick - GenTicks.TicksGame) / (float)GenDate.TicksPerDay;

        /// <summary>Id of the sales order for the cycle currently in flight, or 0.</summary>
        public int activeOrderId;

        public string outcomeNote = "";

        /// <summary>
        /// Consecutive-failure tolerance before the agreement collapses (§30 grace period).
        /// One missed quadrum is a bad month; two in a row is not a supplier.
        /// </summary>
        public const int BreachThreshold = 2;

        public int consecutiveFailures;

        public RecurringContract()
        {
        }

        public bool IsOffer => status == ContractStatus.Offered;

        public bool IsActive => status == ContractStatus.Active;

        public int CyclesRemaining => Mathf.Max(0, totalCycles - cyclesCompleted - cyclesFailed);

        public int TotalValue => Mathf.RoundToInt(unitPrice * quantityPerCycle * totalCycles);

        public int CycleValue => Mathf.RoundToInt(unitPrice * quantityPerCycle);

        public float DaysUntilNextCycle => (nextCycleTick - GenTicks.TicksGame) / (float)GenDate.TicksPerDay;

        public float DaysUntilOfferExpires => (offerExpiryTick - GenTicks.TicksGame) / (float)GenDate.TicksPerDay;

        public float CadenceDays => cadenceTicks / (float)GenDate.TicksPerDay;

        public string ItemLabel()
        {
            string label = thingDef?.LabelCap.ToString() ?? "<missing def>";
            System.Collections.Generic.List<string> parts = new System.Collections.Generic.List<string>();
            if (stuffDef != null)
            {
                parts.Add(stuffDef.label);
            }

            if (minQuality.HasValue)
            {
                parts.Add(minQuality.Value.GetLabel() + "+");
            }

            return parts.Count == 0 ? label : $"{label} ({string.Join(", ", parts.ToArray())})";
        }

        /// <summary>Player accepts the proposal. The only path from Offered to Active.</summary>
        public bool TryAccept()
        {
            if (status != ContractStatus.Offered)
            {
                IntercolonyLog.Warning($"Contract {id} is {status}; cannot accept.");
                return false;
            }

            status = ContractStatus.Active;

            // The first delivery is due one full cycle out, so the player has the agreed
            // cadence to produce it rather than being immediately behind.
            nextCycleTick = GenTicks.TicksGame + cadenceTicks;
            return true;
        }

        public bool TryDecline(string reason)
        {
            if (status != ContractStatus.Offered)
            {
                return false;
            }

            // A terminal agreement without an explanation is not a complete state: the Contracts
            // tab cannot tell whether the player answered or the offer simply lapsed.
            if (string.IsNullOrEmpty(reason))
            {
                IntercolonyLog.Warning($"Contract {id} cannot be declined without a reason.");
                return false;
            }

            status = ContractStatus.Declined;
            outcomeNote = reason;
            return true;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", 0);
            Scribe_Values.Look(ref settlementId, "settlementId", -1);
            Scribe_Values.Look(ref settlementName, "settlementName", "");
            Scribe_Values.Look(ref factionName, "factionName", "");
            Scribe_Defs.Look(ref thingDef, "thingDef");
            Scribe_Defs.Look(ref stuffDef, "stuffDef");
            Scribe_Values.Look(ref minQuality, "minQuality");
            Scribe_Values.Look(ref quantityPerCycle, "quantityPerCycle", 0);
            Scribe_Values.Look(ref cadenceTicks, "cadenceTicks", GenDate.TicksPerQuadrum);
            Scribe_Values.Look(ref totalCycles, "totalCycles", 0);
            Scribe_Values.Look(ref cyclesCompleted, "cyclesCompleted", 0);
            Scribe_Values.Look(ref cyclesFailed, "cyclesFailed", 0);
            Scribe_Values.Look(ref unitPrice, "unitPrice", 0f);
            Scribe_Values.Look(ref discountFraction, "discountFraction", 0f);
            DiscountFraction = discountFraction;
            Scribe_Values.Look(
                ref fulfillment, "fulfillment", FulfillmentMode.SellerDelivery);
            Scribe_Values.Look(ref status, "status", ContractStatus.Offered);
            Scribe_Values.Look(ref nextCycleTick, "nextCycleTick", 0);
            Scribe_Values.Look(ref offerExpiryTick, "offerExpiryTick", 0);
            Scribe_Values.Look(ref suspendedTick, "suspendedTick", 0);
            Scribe_Values.Look(ref renewalOffered, "renewalOffered", false);
            Scribe_Values.Look(ref renewalExpiryTick, "renewalExpiryTick", 0);
            Scribe_Values.Look(ref renewals, "renewals", 0);
            Scribe_Values.Look(ref activeOrderId, "activeOrderId", 0);
            Scribe_Values.Look(ref consecutiveFailures, "consecutiveFailures", 0);
            Scribe_Values.Look(ref outcomeNote, "outcomeNote", "");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (settlementName == null) settlementName = "";
                if (factionName == null) factionName = "";
                if (outcomeNote == null) outcomeNote = "";
            }
        }

        public bool IsValidAfterLoad => thingDef != null && quantityPerCycle > 0 && totalCycles > 0;

        public override string ToString()
        {
            return $"#{id} {settlementName}: {quantityPerCycle}x {ItemLabel()} every " +
                   $"{CadenceDays:F0}d, {cyclesCompleted}/{totalCycles} done [{status}]";
        }
    }
}
