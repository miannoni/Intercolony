using RimWorld;
using UnityEngine;
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

        /// <summary>
        /// The supplier returned its one final counter and the player must accept or decline it.
        /// This state has no player-counter edge, which makes the exchange finite by construction.
        /// </summary>
        CounterpartyCountered,

        /// <summary>
        /// The supplier refused the proposal. This is terminal commercial history, not an active
        /// agreement that can be reopened as a new negotiation.
        /// </summary>
        CounterpartyRefused,

        /// <summary>The supplier accepted and future cycles are live.</summary>
        Active,

        /// <summary>All scheduled procurement cycles were resolved.</summary>
        Completed,

        /// <summary>The agreement ended before all scheduled cycles were resolved.</summary>
        Cancelled,

        /// <summary>The supplier failed to fulfil a cycle.</summary>
        SupplierDefault,

        /// <summary>
        /// The agreement is paused because the supplier's faction went to war. It is not
        /// terminal and resumes if relations recover.
        /// </summary>
        Suspended
    }

    /// <summary>
    /// The supplier's persisted final counter for a procurement proposal. It remains a separate
    /// read model so the player can inspect the exact package before the binding contract fields
    /// are replaced on acceptance.
    /// </summary>
    public sealed class ProcurementContractCounterTerms
    {
        internal ProcurementContractCounterTerms(
            int quantityPerCycle,
            float unitPrice,
            int cadenceDays,
            int totalCycles,
            FulfillmentMode fulfillment)
        {
            this.quantityPerCycle = quantityPerCycle;
            this.unitPrice = unitPrice;
            this.cadenceDays = cadenceDays;
            this.totalCycles = totalCycles;
            this.fulfillment = fulfillment;
        }

        /// <summary>
        /// Quantity bound to each cycle; exposing the persisted value prevents a later UI from
        /// displaying the original proposal while acceptance binds the counter.
        /// </summary>
        public readonly int quantityPerCycle;

        /// <summary>
        /// Unit price bound to each cycle; the accepted contract copies this value without
        /// repricing the supplier's already answered proposal.
        /// </summary>
        public readonly float unitPrice;

        /// <summary>
        /// Days between cycles in the final counter, retained because cadence changes the
        /// agreement's schedule independently of quantity and price.
        /// </summary>
        public readonly int cadenceDays;

        /// <summary>
        /// Number of cycles in the final counter, retained so the player sees the complete term
        /// before accepting rather than an incomplete one-cycle quotation.
        /// </summary>
        public readonly int totalCycles;

        /// <summary>
        /// Fulfillment mode in the final counter, retained because it changes who moves each
        /// cycle's goods and is part of the binding package.
        /// </summary>
        public readonly FulfillmentMode fulfillment;

        /// <summary>
        /// Silver charged for one cycle using the shared pricing owner, keeping any later display
        /// aligned with the purchase-order payment calculation.
        /// </summary>
        public int paymentPerCycle => IntercolonyPricing.TotalPayment(unitPrice, quantityPerCycle);
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
        /// Absolute tick when war suspended this agreement, or 0. The suspension clock is
        /// persisted so resumption can move the next cycle by the exact outage duration.
        /// </summary>
        public int suspendedTick;

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

        /// <summary>
        /// Sentinel meaning this player proposal has not been scheduled for a supplier answer.
        /// This mirrors the sales agreement's persisted decision marker so save/load cannot turn
        /// a pending procurement proposal into an instant answer.
        /// </summary>
        public const int NoDecisionDueTick = -1;

        /// <summary>Absolute tick when the supplier's delayed answer is due.</summary>
        public int decisionDueTick = NoDecisionDueTick;

        /// <summary>
        /// Sentinel meaning no Stage 5 evaluation was stored for this proposal. The score is
        /// persisted because the supplier evaluates once when the proposal is sent.
        /// </summary>
        public const float NoProposalAppeal = -1f;

        /// <summary>Stage 5 acceptance score normalized to the evaluator's 0..1 answer delay range.</summary>
        public float proposalAppeal = NoProposalAppeal;

        /// <summary>Sentinel meaning no supplier decision has been captured yet.</summary>
        public const int NoProposalDecision = -1;

        /// <summary>
        /// The supplier decision captured when the proposal is evaluated. It survives resolution
        /// as commercial history so the contract records whether the supplier accepted, refused,
        /// or countered; <see cref="NoProposalDecision"/> means the proposal was not yet evaluated.
        /// </summary>
        public int proposalDecision = NoProposalDecision;

        /// <summary>
        /// Persisted quantity in the supplier's one final counter. These fields are deliberately
        /// stored separately from the proposed contract fields so a pending answer cannot silently
        /// overwrite the player's original package before acceptance.
        /// </summary>
        private int finalCounterQuantityPerCycle;

        /// <summary>Persisted unit price in the supplier's one final counter.</summary>
        private float finalCounterUnitPrice;

        /// <summary>Persisted cycle cadence in days in the supplier's one final counter.</summary>
        private int finalCounterCadenceDays;

        /// <summary>Persisted total cycle count in the supplier's one final counter.</summary>
        private int finalCounterTotalCycles;

        /// <summary>Persisted fulfillment mode in the supplier's one final counter.</summary>
        private FulfillmentMode finalCounterFulfillment = FulfillmentMode.SellerDelivery;

        /// <summary>Whether the supplier has offered another run of the agreement.</summary>
        public bool renewalOffered;

        /// <summary>Absolute tick when the renewal offer expires.</summary>
        public int renewalExpiryTick;

        /// <summary>Number of completed renewal runs beyond the first agreement.</summary>
        public int renewals;

        /// <summary>
        /// When set, a cycle whose payment cannot be met waits and retries until its deadline
        /// instead of being counted as a failed cycle immediately.
        /// </summary>
        public bool autoReadyOrders;

        /// <summary>
        /// Deliberately unsaved so the insufficient-silver reminder resets on load.
        /// </summary>
        public bool autoReadyWaitNotified;

        /// <summary>Why a terminal proposal did not become an active agreement.</summary>
        public string outcomeNote = "";

        /// <summary>
        /// Whether this record is waiting for the one supplier answer allowed by the proposal
        /// state machine. Keeping this derived from status prevents a second answer after load.
        /// </summary>
        public bool IsPendingProposal =>
            status == ProcurementContractStatus.Offered &&
            decisionDueTick != NoDecisionDueTick &&
            proposalAppeal != NoProposalAppeal;

        /// <summary>
        /// Whether the supplier's final counter is awaiting the player. This status gate is the
        /// only entry to counter acceptance or decline, so no second negotiation round can occur.
        /// </summary>
        public bool HasPendingCounterpartyCounter =>
            status == ProcurementContractStatus.CounterpartyCountered &&
            HasPersistedFinalCounterTerms();

        /// <summary>
        /// Whether the player may accept the persisted counter package exactly once.
        /// </summary>
        public bool CanAcceptFinalCounter => HasPendingCounterpartyCounter;

        /// <summary>
        /// Whether the player may terminally decline the persisted counter without forming or
        /// reopening an agreement.
        /// </summary>
        public bool CanDeclineFinalCounter => HasPendingCounterpartyCounter;

        /// <summary>
        /// Silver charged for one cycle by <see cref="PurchaseOrderService"/> when it creates the
        /// paid order. Every display of this figure must use the same calculation rather than a
        /// second copy; the counter-terms record above carries the same property for the terms
        /// the player is still negotiating.
        /// </summary>
        public int paymentPerCycle => IntercolonyPricing.TotalPayment(unitPrice, quantityPerCycle);

        /// <summary>
        /// Returns the exact persisted counter terms for a later read model and acceptance path.
        /// Reconstructing this package from the original proposal would make the displayed and
        /// charged terms diverge.
        /// </summary>
        public bool TryGetFinalCounterTerms(out ProcurementContractCounterTerms terms)
        {
            if (!HasPendingCounterpartyCounter)
            {
                terms = null;
                return false;
            }

            terms = new ProcurementContractCounterTerms(
                finalCounterQuantityPerCycle,
                finalCounterUnitPrice,
                finalCounterCadenceDays,
                finalCounterTotalCycles,
                finalCounterFulfillment);
            return true;
        }

        /// <summary>
        /// Stores the evaluator's one final counter before the delayed supplier answer is
        /// delivered. Total cycles are not an evaluator term, so the proposal's already validated
        /// cycle count is carried forward unchanged.
        /// </summary>
        internal bool TryRecordFinalCounterTerms(
            IntercolonyNegotiationTerms terms, int totalCycles)
        {
            if (status != ProcurementContractStatus.Offered ||
                proposalDecision != (int)IntercolonyNegotiationDecision.Countered ||
                HasPersistedFinalCounterTerms() ||
                terms == null ||
                terms.quantity < ProcurementContractService.MinimumQuantityPerCycle ||
                terms.quantity > ProcurementContractService.MaximumQuantityPerCycle ||
                terms.unitPrice <= 0f || float.IsNaN(terms.unitPrice) ||
                float.IsInfinity(terms.unitPrice) ||
                terms.deadlineDays < ProcurementContractService.MinimumCadenceDays ||
                terms.deadlineDays > ProcurementContractService.MaximumCadenceDays ||
                totalCycles < ProcurementContractService.MinimumTotalCycles ||
                totalCycles > ProcurementContractService.MaximumTotalCycles ||
                (terms.fulfillment != FulfillmentMode.SellerDelivery &&
                 terms.fulfillment != FulfillmentMode.BuyerPickup))
            {
                return false;
            }

            finalCounterQuantityPerCycle = terms.quantity;
            finalCounterUnitPrice = terms.unitPrice;
            finalCounterCadenceDays = terms.deadlineDays;
            finalCounterTotalCycles = totalCycles;
            finalCounterFulfillment = terms.fulfillment;
            return true;
        }

        /// <summary>Moves the answered counter branch into its one player-response state.</summary>
        internal bool TryRecordCounterpartyCounter()
        {
            if (status != ProcurementContractStatus.Offered ||
                proposalDecision != (int)IntercolonyNegotiationDecision.Countered ||
                !HasPersistedFinalCounterTerms())
            {
                return false;
            }

            status = ProcurementContractStatus.CounterpartyCountered;
            return true;
        }

        /// <summary>Records a supplier refusal exactly once without forming an agreement.</summary>
        internal bool TryRecordCounterpartyRefusal()
        {
            if (status != ProcurementContractStatus.Offered ||
                proposalDecision != (int)IntercolonyNegotiationDecision.Refused)
            {
                return false;
            }

            status = ProcurementContractStatus.CounterpartyRefused;
            return true;
        }

        /// <summary>
        /// Applies accepted terms through the shared contract activation boundary. The final-counter
        /// branch accepts only the exact package loaded from the persisted counter fields.
        /// </summary>
        internal bool TryActivateAcceptedTerms(
            ProcurementContractCounterTerms acceptedTerms, bool acceptingFinalCounter)
        {
            if (acceptedTerms == null)
            {
                return false;
            }

            if (acceptingFinalCounter)
            {
                if (!CanAcceptFinalCounter || !MatchesFinalCounter(acceptedTerms))
                {
                    return false;
                }
            }
            else if (!IsPendingProposal ||
                     proposalDecision != (int)IntercolonyNegotiationDecision.Accepted)
            {
                return false;
            }

            quantityPerCycle = acceptedTerms.quantityPerCycle;
            unitPrice = acceptedTerms.unitPrice;
            cadenceDays = acceptedTerms.cadenceDays;
            totalCycles = acceptedTerms.totalCycles;
            fulfillment = acceptedTerms.fulfillment;
            status = ProcurementContractStatus.Active;
            nextCycleTick = GenTicks.TicksGame + cadenceDays * GenDate.TicksPerDay;
            ClearFinalCounterTerms();
            return true;
        }

        /// <summary>Closes a pending final counter when the player declines it.</summary>
        internal bool TryDeclineFinalCounter()
        {
            if (!CanDeclineFinalCounter)
            {
                return false;
            }

            status = ProcurementContractStatus.Cancelled;
            ClearFinalCounterTerms();
            return true;
        }

        private bool MatchesFinalCounter(ProcurementContractCounterTerms terms)
        {
            return terms != null &&
                   HasPendingCounterpartyCounter &&
                   terms.quantityPerCycle == finalCounterQuantityPerCycle &&
                   Mathf.Approximately(terms.unitPrice, finalCounterUnitPrice) &&
                   terms.cadenceDays == finalCounterCadenceDays &&
                   terms.totalCycles == finalCounterTotalCycles &&
                   terms.fulfillment == finalCounterFulfillment;
        }

        private bool HasPersistedFinalCounterTerms()
        {
            return finalCounterQuantityPerCycle > 0 &&
                   finalCounterUnitPrice > 0f &&
                   !float.IsNaN(finalCounterUnitPrice) &&
                   !float.IsInfinity(finalCounterUnitPrice) &&
                   finalCounterCadenceDays > 0 &&
                   finalCounterTotalCycles > 0;
        }

        private void ClearFinalCounterTerms()
        {
            finalCounterQuantityPerCycle = 0;
            finalCounterUnitPrice = 0f;
            finalCounterCadenceDays = 0;
            finalCounterTotalCycles = 0;
            finalCounterFulfillment = FulfillmentMode.SellerDelivery;
        }

        /// <summary>
        /// Describes the frozen procurement item for letters and diagnostics, including the
        /// material and quality that are part of the supplier's promise.
        /// </summary>
        public string ItemLabel()
        {
            string label = thingDef?.LabelCap.ToString() ?? "<missing def>";
            System.Collections.Generic.List<string> parts =
                new System.Collections.Generic.List<string>();
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
            Scribe_Values.Look(ref suspendedTick, "suspendedTick", 0);
            Scribe_Values.Look(ref activeOrderId, "activeOrderId", NoActiveOrderId);
            Scribe_Values.Look(ref status, "status", ProcurementContractStatus.Offered);
            Scribe_Values.Look(ref createdTick, "createdTick", 0);
            Scribe_Values.Look(ref offerExpiryTick, "offerExpiryTick", NoExpiryTick);
            Scribe_Values.Look(ref decisionDueTick, "decisionDueTick", NoDecisionDueTick);
            Scribe_Values.Look(ref proposalAppeal, "proposalAppeal", NoProposalAppeal);
            Scribe_Values.Look(ref proposalDecision, "proposalDecision", NoProposalDecision);
            Scribe_Values.Look(ref finalCounterQuantityPerCycle, "finalCounterQuantityPerCycle", 0);
            Scribe_Values.Look(ref finalCounterUnitPrice, "finalCounterUnitPrice", 0f);
            Scribe_Values.Look(ref finalCounterCadenceDays, "finalCounterCadenceDays", 0);
            Scribe_Values.Look(ref finalCounterTotalCycles, "finalCounterTotalCycles", 0);
            Scribe_Values.Look(
                ref finalCounterFulfillment,
                "finalCounterFulfillment",
                FulfillmentMode.SellerDelivery);
            Scribe_Values.Look(ref renewalOffered, "renewalOffered", false);
            Scribe_Values.Look(ref renewalExpiryTick, "renewalExpiryTick", 0);
            Scribe_Values.Look(ref renewals, "renewals", 0);
            Scribe_Values.Look(ref autoReadyOrders, "autoReadyOrders", false);
            Scribe_Values.Look(ref outcomeNote, "outcomeNote", "");

            if (Scribe.mode == LoadSaveMode.PostLoadInit && settlementName == null)
            {
                settlementName = "";
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit && outcomeNote == null)
            {
                outcomeNote = "";
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit &&
                status == ProcurementContractStatus.CounterpartyCountered &&
                !HasPersistedFinalCounterTerms())
            {
                // A counter state without all four persisted terms cannot safely be shown or
                // accepted. It is terminal rather than an invitation to re-evaluate the proposal.
                IntercolonyLog.Warning(
                    $"Procurement contract {id} had incomplete final counter terms; cancelling it.");
                status = ProcurementContractStatus.Cancelled;
                outcomeNote = "The persisted supplier counter was incomplete and could not be accepted.";
            }
        }

        /// <summary>
        /// Whether this record still names a usable product and a non-empty standing term after
        /// loading. A missing definition normally means its supplying mod was removed.
        /// </summary>
        public bool IsValidAfterLoad => thingDef != null && quantityPerCycle > 0 && totalCycles > 0;
    }
}
