using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>Why a player-proposed procurement agreement was refused before being sent.</summary>
    public enum ProcurementContractProposalFailure
    {
        None,
        InvalidState,
        InaccessibleSettlement,
        MissingEconomicProfile,
        InvalidItem,
        SupplierCannotSupply,
        ExistingContract,
        QuantityOutOfRange,
        CadenceOutOfRange,
        TotalCyclesOutOfRange,
        TermTooLong,
        InvalidFulfillment,
        UnitPriceOutOfRange
    }

    /// <summary>Result of validating and sending one procurement proposal.</summary>
    public sealed class ProcurementContractProposalResult
    {
        private ProcurementContractProposalResult(
            ProcurementContract contract,
            ProcurementContractProposalFailure failure,
            string reason,
            IntercolonyNegotiationResult evaluation)
        {
            Contract = contract;
            Failure = failure;
            Reason = reason;
            Evaluation = evaluation;
        }

        /// <summary>The persisted proposal, or null when validation refused it.</summary>
        public ProcurementContract Contract { get; }

        /// <summary>The validation failure, or None when the proposal was sent.</summary>
        public ProcurementContractProposalFailure Failure { get; }

        /// <summary>Player-facing explanation for a refusal.</summary>
        public string Reason { get; }

        /// <summary>The one Stage 5 evaluation captured when the proposal was sent.</summary>
        public IntercolonyNegotiationResult Evaluation { get; }

        /// <summary>Whether a durable pending proposal was created.</summary>
        public bool Success => Contract != null && Failure == ProcurementContractProposalFailure.None;

        internal static ProcurementContractProposalResult Sent(
            ProcurementContract contract, IntercolonyNegotiationResult evaluation)
        {
            return new ProcurementContractProposalResult(
                contract, ProcurementContractProposalFailure.None, null, evaluation);
        }

        internal static ProcurementContractProposalResult Refused(
            ProcurementContractProposalFailure failure, string reason)
        {
            return new ProcurementContractProposalResult(null, failure, reason, null);
        }
    }

    /// <summary>The delayed supplier answer applied to one procurement proposal.</summary>
    public sealed class ProcurementContractAnswer
    {
        private ProcurementContractAnswer(
            ProcurementContract contract,
            IntercolonyNegotiationDecision decision,
            string reason,
            bool applied)
        {
            Contract = contract;
            Decision = decision;
            Reason = reason;
            Applied = applied;
        }

        /// <summary>The answered procurement record.</summary>
        public ProcurementContract Contract { get; }

        /// <summary>The decision captured by the Stage 5 evaluator.</summary>
        public IntercolonyNegotiationDecision Decision { get; }

        /// <summary>Explanation attached to a refusal or counter.</summary>
        public string Reason { get; }

        /// <summary>Whether this call performed the one legal answer transition.</summary>
        public bool Applied { get; }

        internal static ProcurementContractAnswer AppliedAnswer(
            ProcurementContract contract,
            IntercolonyNegotiationDecision decision,
            string reason)
        {
            return new ProcurementContractAnswer(contract, decision, reason, true);
        }

        internal static ProcurementContractAnswer NotApplied(
            ProcurementContract contract, string reason)
        {
            return new ProcurementContractAnswer(
                contract, IntercolonyNegotiationDecision.Refused, reason, false);
        }
    }

    /// <summary>
    /// Owns proposal, delayed answer, and withdrawal of standing procurement proposals (§6.6).
    /// Cycle orders, renewal, payment, and supplier default remain with later procurement stages.
    /// </summary>
    public static class ProcurementContractService
    {
        /// <summary>Procurement reuses the sell-side quantity bounds.</summary>
        public const int MinimumQuantityPerCycle = ContractService.MinimumQuantityPerCycle;

        /// <summary>Procurement reuses the sell-side quantity bounds.</summary>
        public const int MaximumQuantityPerCycle = ContractService.MaximumQuantityPerCycle;

        /// <summary>A cadence must name at least one in-game day.</summary>
        public const int MinimumCadenceDays = 1;

        /// <summary>A single standing interval cannot exceed one in-game year.</summary>
        public const int MaximumCadenceDays = 365;

        /// <summary>At least one cycle must be promised.</summary>
        public const int MinimumTotalCycles = 1;

        /// <summary>A proposal may schedule at most one year of daily cycles.</summary>
        public const int MaximumTotalCycles = 365;

        /// <summary>The example agreement and every other proposal are capped at one year.</summary>
        public const int MaximumTermDays = 365;

        private const float MinimumUnitPrice = 0.01f;
        private const float MaximumUnitPriceMultiplier = 2f;
        private const int DecisionSeedSalt = 0x6F21;

        /// <summary>
        /// Sends a standing purchase proposal using the supplier's reference price when the
        /// player does not specify a rate.
        /// </summary>
        public static ProcurementContractProposalResult ProposeContract(
            IntercolonyWorldComponent state,
            Settlement settlement,
            ThingDef thingDef,
            int quantityPerCycle,
            int cadenceDays,
            int totalCycles,
            float? agreedUnitPrice = null,
            FulfillmentMode fulfillment = FulfillmentMode.SellerDelivery)
        {
            return ProposeContract(
                state, settlement, thingDef, null, null, quantityPerCycle, cadenceDays,
                totalCycles, agreedUnitPrice, fulfillment);
        }

        /// <summary>
        /// Sends a fully specified standing purchase proposal. Validation completes before the
        /// Stage 5 evaluator is called, so a refused proposal creates no durable record.
        /// </summary>
        public static ProcurementContractProposalResult ProposeContract(
            IntercolonyWorldComponent state,
            Settlement settlement,
            ThingDef thingDef,
            ThingDef stuffDef,
            QualityCategory? quality,
            int quantityPerCycle,
            int cadenceDays,
            int totalCycles,
            float? agreedUnitPrice = null,
            FulfillmentMode fulfillment = FulfillmentMode.SellerDelivery)
        {
            if (state == null)
            {
                return ProcurementContractProposalResult.Refused(
                    ProcurementContractProposalFailure.InvalidState,
                    "No Intercolony world state is available.");
            }

            string accessReason = null;
            bool accessible = settlement != null && IntercolonyMarketAccess.IsAccessible(
                settlement, out accessReason);
            if (!accessible)
            {
                return ProcurementContractProposalResult.Refused(
                    ProcurementContractProposalFailure.InaccessibleSettlement,
                    settlement == null
                        ? "A supplier settlement is required."
                        : "The supplier settlement is inaccessible: " + accessReason + ".");
            }

            SettlementEconomicProfile profile = state.GetProfile(settlement);
            if (profile == null)
            {
                return ProcurementContractProposalResult.Refused(
                    ProcurementContractProposalFailure.MissingEconomicProfile,
                    "The supplier settlement has no economic profile.");
            }

            if (!TryGetCategory(thingDef, out IntercolonyProductCategory category, out string itemReason))
            {
                return ProcurementContractProposalResult.Refused(
                    ProcurementContractProposalFailure.InvalidItem, itemReason);
            }

            if (quantityPerCycle < MinimumQuantityPerCycle ||
                quantityPerCycle > MaximumQuantityPerCycle)
            {
                return ProcurementContractProposalResult.Refused(
                    ProcurementContractProposalFailure.QuantityOutOfRange,
                    $"Quantity per cycle must be between {MinimumQuantityPerCycle} and " +
                    $"{MaximumQuantityPerCycle}.");
            }

            if (cadenceDays < MinimumCadenceDays || cadenceDays > MaximumCadenceDays)
            {
                return ProcurementContractProposalResult.Refused(
                    ProcurementContractProposalFailure.CadenceOutOfRange,
                    $"Cadence must be between {MinimumCadenceDays} and {MaximumCadenceDays} days.");
            }

            if (totalCycles < MinimumTotalCycles || totalCycles > MaximumTotalCycles)
            {
                return ProcurementContractProposalResult.Refused(
                    ProcurementContractProposalFailure.TotalCyclesOutOfRange,
                    $"Total cycles must be between {MinimumTotalCycles} and {MaximumTotalCycles}.");
            }

            if ((long)cadenceDays * totalCycles > MaximumTermDays)
            {
                return ProcurementContractProposalResult.Refused(
                    ProcurementContractProposalFailure.TermTooLong,
                    $"Cadence multiplied by total cycles must not exceed {MaximumTermDays} days.");
            }

            if (fulfillment != FulfillmentMode.SellerDelivery &&
                fulfillment != FulfillmentMode.BuyerPickup)
            {
                return ProcurementContractProposalResult.Refused(
                    ProcurementContractProposalFailure.InvalidFulfillment,
                    "Fulfillment must be supplier delivery or buyer pickup.");
            }

            int seed = Gen.HashCombineInt(
                state.EconomySeed, settlement.ID, thingDef.shortHash, quantityPerCycle);
            seed = Gen.HashCombineInt(seed, cadenceDays, totalCycles, DecisionSeedSalt);
            Rand.PushState(seed);
            float referenceUnitPrice;
            try
            {
                if (!RfqService.CanTechnicallySupply(thingDef, profile))
                {
                    return ProcurementContractProposalResult.Refused(
                        ProcurementContractProposalFailure.SupplierCannotSupply,
                        $"{settlement.Label} cannot technically supply {thingDef.label}.");
                }

                float supply = EffectiveEconomyService.EffectiveSupply(
                    state, profile, category);
                float distance = MarketOpportunityGenerator.DistanceToPlayer(settlement);
                referenceUnitPrice = IntercolonyPricing.SupplierUnitPrice(
                    state,
                    thingDef,
                    stuffDef,
                    quality,
                    profile,
                    category,
                    supply,
                    distance,
                    fulfillment == FulfillmentMode.SellerDelivery,
                    quantityPerCycle,
                    out _);
            }
            finally
            {
                Rand.PopState();
            }

            float unitPrice = agreedUnitPrice ?? referenceUnitPrice;
            if (unitPrice < MinimumUnitPrice ||
                float.IsNaN(unitPrice) ||
                float.IsInfinity(unitPrice) ||
                unitPrice > referenceUnitPrice * MaximumUnitPriceMultiplier)
            {
                return ProcurementContractProposalResult.Refused(
                    ProcurementContractProposalFailure.UnitPriceOutOfRange,
                    $"Unit price must be at least {MinimumUnitPrice:F2} and no more than " +
                    $"{referenceUnitPrice * MaximumUnitPriceMultiplier:F2} " +
                    $"(twice the current supplier rate of {referenceUnitPrice:F2}).");
            }

            if (state.HasContractWith(settlement.ID, thingDef))
            {
                ProcurementContract existing = state.FindProcurementContractWith(
                    settlement.ID, thingDef);
                string existingDescription = existing == null
                    ? "an existing standing relationship"
                    : $"procurement proposal #{existing.id} ({existing.status})";
                return ProcurementContractProposalResult.Refused(
                    ProcurementContractProposalFailure.ExistingContract,
                    $"A standing agreement already exists for {settlement.Label} and " +
                    $"{thingDef.label}: {existingDescription}.");
            }

            IntercolonyNegotiationProposal proposal = new IntercolonyNegotiationProposal
            {
                state = state,
                profile = profile,
                thingDef = thingDef,
                category = category,
                direction = IntercolonyNegotiationDirection.Purchase,
                originalTerms = new IntercolonyNegotiationTerms(
                    quantityPerCycle, referenceUnitPrice, cadenceDays, fulfillment),
                proposedTerms = new IntercolonyNegotiationTerms(
                    quantityPerCycle, unitPrice, cadenceDays, fulfillment),
                fulfillmentModeChangeAllowed = true,
                counterAllowed = true
            };
            IntercolonyNegotiationResult evaluation =
                IntercolonyNegotiationEvaluator.Evaluate(proposal);

            ProcurementContract contract = new ProcurementContract
            {
                id = state.NextId(),
                settlementId = settlement.ID,
                settlementName = settlement.Label ?? "unnamed",
                thingDef = thingDef,
                stuffDef = stuffDef,
                quality = quality,
                quantityPerCycle = quantityPerCycle,
                unitPrice = unitPrice,
                cadenceDays = cadenceDays,
                totalCycles = totalCycles,
                fulfillment = fulfillment,
                status = ProcurementContractStatus.Offered,
                createdTick = GenTicks.TicksGame,
                offerExpiryTick = ProcurementContract.NoExpiryTick,
                decisionDueTick = GenTicks.TicksGame +
                    ContractService.ProposalDecisionDelayTicks(DelayAppeal(evaluation)),
                proposalAppeal = DelayAppeal(evaluation),
                proposalDecision = (int)evaluation.Decision
            };

            // The evaluator answers once at proposal time. A counter's exact package is copied
            // into durable fields now, before the delayed answer transition, so resolution never
            // needs to ask the evaluator for a different answer.
            if (evaluation.Decision == IntercolonyNegotiationDecision.Countered &&
                (!evaluation.HasFinalCounter ||
                 !contract.TryRecordFinalCounterTerms(
                     evaluation.FinalCounterTerms, totalCycles)))
            {
                return ProcurementContractProposalResult.Refused(
                    ProcurementContractProposalFailure.InvalidState,
                    "The supplier returned an invalid final counter package.");
            }

            state.AddProcurementContract(contract);
            IntercolonyLog.Message(
                $"Procurement proposal {contract.id} sent to {contract.settlementName}: " +
                $"{quantityPerCycle}x {thingDef.label} every {cadenceDays}d x{totalCycles}; " +
                "awaiting supplier answer.");
            return ProcurementContractProposalResult.Sent(contract, evaluation);
        }

        /// <summary>
        /// Answers one due proposal. The stored evaluator decision is applied exactly once;
        /// acceptance only schedules the first cycle and never creates an order or takes silver.
        /// </summary>
        public static ProcurementContractAnswer AnswerProposal(
            IntercolonyWorldComponent state, ProcurementContract contract)
        {
            if (state == null || contract == null)
            {
                return ProcurementContractAnswer.NotApplied(
                    contract, "World state and procurement proposal are required.");
            }

            if (!contract.IsPendingProposal)
            {
                return ProcurementContractAnswer.NotApplied(
                    contract,
                    $"Procurement proposal #{contract.id} has already been answered or is not pending.");
            }

            IntercolonyNegotiationDecision decision =
                (IntercolonyNegotiationDecision)contract.proposalDecision;

            if (decision == IntercolonyNegotiationDecision.Accepted)
            {
                ProcurementContractCounterTerms acceptedTerms = new ProcurementContractCounterTerms(
                    contract.quantityPerCycle,
                    contract.unitPrice,
                    contract.cadenceDays,
                    contract.totalCycles,
                    contract.fulfillment);
                return ApplyAcceptedTerms(
                    state, contract, acceptedTerms, acceptingFinalCounter: false);
            }

            if (decision == IntercolonyNegotiationDecision.Countered)
            {
                if (!contract.TryRecordCounterpartyCounter() ||
                    !contract.TryGetFinalCounterTerms(
                        out ProcurementContractCounterTerms counterTerms))
                {
                    return ProcurementContractAnswer.NotApplied(
                        contract, "The supplier's final counter terms were not persisted.");
                }

                const string reason =
                    "The supplier returned one final counterproposal; accept or decline it. " +
                    "No further counter is available.";
                contract.outcomeNote = reason;
                ClearPendingAnswer(contract);
                IntercolonyLetters.Send(
                    IntercolonyLetterImportance.Important,
                    "Procurement agreement countered",
                    $"{contract.settlementName} returned a final counterproposal for your " +
                    $"standing procurement agreement for {contract.thingDef.label}.\n\n" +
                    $"They offer {counterTerms.quantityPerCycle}x {contract.thingDef.label} " +
                    $"every {counterTerms.cadenceDays} days for {counterTerms.totalCycles} cycles " +
                    $"at {counterTerms.unitPrice:F2} silver per unit " +
                    $"({counterTerms.paymentPerCycle} silver per cycle).\n\n" +
                    "You may accept these exact terms or decline them; no second counter is allowed.",
                    LetterDefOf.NeutralEvent);
                IntercolonyLog.Message(
                    $"Supplier returned final counter for procurement proposal {contract.id}.");
                return ProcurementContractAnswer.AppliedAnswer(contract, decision, reason);
            }

            if (decision != IntercolonyNegotiationDecision.Refused ||
                !contract.TryRecordCounterpartyRefusal())
            {
                return ProcurementContractAnswer.NotApplied(
                    contract, "The stored supplier decision could not enter the contract state.");
            }

            const string refusalReason = "The supplier declined the proposed procurement terms.";
            contract.outcomeNote = refusalReason;
            ClearPendingAnswer(contract);
            IntercolonyLetters.Send(
                IntercolonyLetterImportance.Important,
                "Procurement agreement declined",
                $"{contract.settlementName} declined your proposed standing procurement " +
                $"agreement for {contract.thingDef.label}.\n\n{refusalReason}",
                LetterDefOf.NeutralEvent);
            IntercolonyLog.Message($"Supplier declined procurement proposal {contract.id}.");
            return ProcurementContractAnswer.AppliedAnswer(contract, decision, refusalReason);
        }

        /// <summary>
        /// Accepts the exact final counter already persisted on the contract. It enters the same
        /// activation path as an ordinary accepted proposal and never evaluates a second time.
        /// </summary>
        public static ProcurementContractAnswer AcceptFinalCounter(
            IntercolonyWorldComponent state, ProcurementContract contract)
        {
            if (state == null || contract == null ||
                !contract.TryGetFinalCounterTerms(
                    out ProcurementContractCounterTerms finalCounterTerms))
            {
                return ProcurementContractAnswer.NotApplied(
                    contract, "No pending final procurement counter is available.");
            }

            return ApplyAcceptedTerms(
                state, contract, finalCounterTerms, acceptingFinalCounter: true);
        }

        /// <summary>Boolean convenience wrapper for callers that only need the transition result.</summary>
        public static bool TryAcceptFinalCounter(
            IntercolonyWorldComponent state, ProcurementContract contract)
        {
            return AcceptFinalCounter(state, contract).Applied;
        }

        /// <summary>
        /// Declines the pending final counter once. The original proposal terms remain untouched,
        /// no payment or cycle is created, and the cancelled record cannot be reopened.
        /// </summary>
        public static bool TryDeclineFinalCounter(
            IntercolonyWorldComponent state, ProcurementContract contract)
        {
            if (state == null || contract == null || !contract.TryDeclineFinalCounter())
            {
                return false;
            }

            contract.outcomeNote =
                "The player declined the supplier's final counter; no agreement was formed.";
            ClearPendingAnswer(contract);
            IntercolonyLetters.Send(
                IntercolonyLetterImportance.Important,
                "Procurement counter declined",
                $"You declined {contract.settlementName}'s final counter for " +
                $"{contract.thingDef.label}. No agreement was formed and no silver was paid.",
                LetterDefOf.NeutralEvent);
            IntercolonyLog.Message(
                $"Player declined final counter for procurement proposal {contract.id}.");
            return true;
        }

        /// <summary>Stage 5-shaped alias for declining a pending procurement final counter.</summary>
        public static bool TryDecline(
            IntercolonyWorldComponent state, ProcurementContract contract)
        {
            return TryDeclineFinalCounter(state, contract);
        }

        private static ProcurementContractAnswer ApplyAcceptedTerms(
            IntercolonyWorldComponent state,
            ProcurementContract contract,
            ProcurementContractCounterTerms acceptedTerms,
            bool acceptingFinalCounter)
        {
            if (state == null || contract == null ||
                !contract.TryActivateAcceptedTerms(acceptedTerms, acceptingFinalCounter))
            {
                return ProcurementContractAnswer.NotApplied(
                    contract,
                    acceptingFinalCounter
                        ? "The final procurement counter is no longer available."
                        : "The procurement proposal is no longer pending.");
            }

            contract.outcomeNote = acceptingFinalCounter
                ? "Player accepted the supplier's final counter; first procurement cycle scheduled."
                : "Supplier accepted; first procurement cycle scheduled.";
            ClearPendingAnswer(contract);
            IntercolonyLetters.Send(
                IntercolonyLetterImportance.Always,
                acceptingFinalCounter
                    ? "Procurement counter accepted"
                    : "Procurement agreement accepted",
                $"{contract.settlementName} accepted the standing procurement agreement for " +
                $"{contract.thingDef.label}.\n\n" +
                $"They will provide {contract.quantityPerCycle}x {contract.thingDef.label} " +
                $"every {contract.cadenceDays} days for {contract.totalCycles} cycles at " +
                $"{contract.unitPrice:F2} silver per unit " +
                $"({IntercolonyPricing.TotalPayment(contract.unitPrice, contract.quantityPerCycle)} " +
                "silver per cycle).\n\n" +
                $"The first cycle is scheduled in {contract.cadenceDays} days. No order or " +
                "silver has been created yet; each cycle settles separately.",
                LetterDefOf.PositiveEvent);
            IntercolonyLog.Message(
                acceptingFinalCounter
                    ? $"Player accepted final counter for procurement proposal {contract.id}."
                    : $"Supplier accepted procurement proposal {contract.id}.");
            return ProcurementContractAnswer.AppliedAnswer(
                contract,
                acceptingFinalCounter
                    ? IntercolonyNegotiationDecision.Countered
                    : IntercolonyNegotiationDecision.Accepted,
                null);
        }

        /// <summary>Answers every due procurement proposal on the same coarse tick used by sales.</summary>
        public static int AdvanceProposals(IntercolonyWorldComponent state)
        {
            if (state == null)
            {
                return 0;
            }

            int answered = 0;
            int now = GenTicks.TicksGame;
            foreach (ProcurementContract contract in state.ProcurementContracts)
            {
                if (contract != null && contract.IsPendingProposal &&
                    now >= contract.decisionDueTick &&
                    AnswerProposal(state, contract).Applied)
                {
                    answered++;
                }
            }

            return answered;
        }

        /// <summary>
        /// Advances active procurement agreements through one scheduled cycle. The purchase order
        /// remains the source of truth for fulfilment; this method only owns the agreement's cycle
        /// counters and cadence so a paid order cannot be replaced while it is still open.
        /// </summary>
        public static int AdvanceCycles(IntercolonyWorldComponent state)
        {
            if (state == null)
            {
                return 0;
            }

            int now = GenTicks.TicksGame;
            int resolved = 0;
            foreach (ProcurementContract contract in state.ProcurementContracts)
            {
                if (contract == null || contract.status != ProcurementContractStatus.Active)
                {
                    continue;
                }

                if (CycleCountReached(contract))
                {
                    Complete(state, contract);
                    continue;
                }

                // Resolve the in-flight order before considering the next scheduled cycle. An
                // open order deliberately holds the agreement at its due tick; the next refresh
                // will retry after the shared purchase-order service closes it.
                if (contract.activeOrderId != ProcurementContract.NoActiveOrderId)
                {
                    PurchaseOrder order = state.FindPurchaseOrder(contract.activeOrderId);
                    if (order == null || order.IsOpen)
                    {
                        continue;
                    }

                    contract.activeOrderId = ProcurementContract.NoActiveOrderId;
                    if (order.status == PurchaseOrderStatus.Completed)
                    {
                        contract.cyclesCompleted++;
                    }
                    else
                    {
                        // Supplier-default and war-loss transitions remain owned by their existing
                        // purchase-order services. Once either has concluded the corresponding
                        // agreement cycle is no longer live, so count it without adding a second
                        // supplier-default policy here.
                        contract.cyclesFailed++;

                        if (order.status == PurchaseOrderStatus.SupplierDefault)
                        {
                            string reason = order.outcomeNote.NullOrEmpty()
                                ? "The supplier did not fulfil the paid order."
                                : order.outcomeNote;
                            contract.outcomeNote =
                                $"Cycle {contract.cyclesCompleted + contract.cyclesFailed} of " +
                                $"{contract.totalCycles} failed: supplier default. {reason}";
                            IntercolonyLetters.Send(
                                IntercolonyLetterImportance.Always,
                                "Procurement cycle failed",
                                $"Cycle {contract.cyclesCompleted + contract.cyclesFailed} of " +
                                $"{contract.totalCycles} for {contract.settlementName} failed.\n\n" +
                                $"The supplier defaulted: {reason}\n\n" +
                                "Any payment was handled by the existing purchase-order refund " +
                                "rule. The agreement remains active and its next cycle is still " +
                                "scheduled.",
                                LetterDefOf.NegativeEvent);
                        }
                    }

                    resolved++;
                    if (CycleCountReached(contract))
                    {
                        Complete(state, contract);
                        continue;
                    }
                }

                if (now < contract.nextCycleTick)
                {
                    continue;
                }

                int cycleNumber = contract.cyclesCompleted + contract.cyclesFailed + 1;
                if (TryCreateCycleOrder(state, contract, out string failureReason))
                {
                    // Keep the order ID tied to the cycle that was actually paid for. The next
                    // due tick is derived from the scheduled tick, not from a late refresh.
                    contract.nextCycleTick += contract.cadenceDays * GenDate.TicksPerDay;
                    resolved++;
                    continue;
                }

                contract.cyclesFailed++;
                contract.outcomeNote =
                    $"Cycle {cycleNumber} of {contract.totalCycles} failed: " +
                    (failureReason ?? "The purchase order could not be created.");
                contract.nextCycleTick += contract.cadenceDays * GenDate.TicksPerDay;
                IntercolonyLetters.Send(
                    IntercolonyLetterImportance.Always,
                    "Procurement cycle failed",
                    $"Cycle {cycleNumber} of {contract.totalCycles} for {contract.settlementName} " +
                    $"could not be fulfilled.\n\n{failureReason ?? "The purchase order could not be created."}\n\n" +
                    "This cycle is counted as failed; the agreement remains active and its next " +
                    "cycle is still scheduled.",
                    LetterDefOf.NegativeEvent);
                IntercolonyLog.Message(
                    $"Procurement contract {contract.id} cycle {cycleNumber} failed: " +
                    (failureReason ?? "purchase order could not be created."));
                resolved++;

                if (CycleCountReached(contract))
                {
                    Complete(state, contract);
                }
            }

            return resolved;
        }

        private static bool TryCreateCycleOrder(
            IntercolonyWorldComponent state,
            ProcurementContract contract,
            out string failureReason)
        {
            Settlement settlement = IntercolonyMarketAccess.FindSettlement(contract.settlementId);
            Map paymentMap = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
            bool created = PurchaseOrderService.TryCreatePaidOrder(
                state,
                paymentMap,
                refreshWindow: ProcurementContract.NoExpiryTick,
                requestId: 0,
                quotationId: 0,
                supplierListingId: PurchaseOrder.NoSupplierListing,
                settlementId: contract.settlementId,
                settlementName: settlement?.Label ?? contract.settlementName,
                factionName: settlement?.Faction?.Name ?? "",
                thingDef: contract.thingDef,
                stuffDef: contract.stuffDef,
                quality: contract.quality,
                quantity: contract.quantityPerCycle,
                animalSpec: null,
                unitPrice: contract.unitPrice,
                supplierDelivers: contract.fulfillment == FulfillmentMode.SellerDelivery,
                leadTimeDays: 0,
                order: out PurchaseOrder order,
                failureReason: out failureReason);

            if (!created)
            {
                return false;
            }

            if (TryGetCurrentSupplyFailure(state, contract, out string supplyFailure))
            {
                // The cycle has already been paid for. Reuse the purchase-order default path so
                // refund placement, accounting, and the SupplierDefault status remain governed
                // by one rule rather than a second contract-specific refund implementation.
                PurchaseOrderService.Refund(order, supplyFailure);
                if (order.IsOpen)
                {
                    // A refund that cannot currently be placed remains an open purchase order;
                    // let its existing lifecycle retry instead of counting a live order as failed.
                    contract.activeOrderId = order.id;
                    failureReason = null;
                    return true;
                }

                failureReason = supplyFailure;
                contract.activeOrderId = ProcurementContract.NoActiveOrderId;
                return false;
            }

            contract.activeOrderId = order.id;
            return true;
        }

        /// <summary>
        /// Checks the current product capacity at the cycle boundary. This deliberately reuses
        /// the effective-economy read model and the RFQ/listing capacity conversion: events and
        /// market pressure can make a previously accepted promise impossible without adding a
        /// supplier personality or an independent random failure roll.
        /// </summary>
        private static bool TryGetCurrentSupplyFailure(
            IntercolonyWorldComponent state,
            ProcurementContract contract,
            out string failureReason)
        {
            failureReason = null;
            Settlement settlement = IntercolonyMarketAccess.FindSettlement(contract.settlementId);
            if (settlement == null)
            {
                failureReason =
                    "Supplier default: the supplier settlement no longer exists to fulfil this cycle.";
                return true;
            }

            SettlementEconomicProfile profile = state.GetProfile(settlement);
            if (profile == null)
            {
                failureReason =
                    "Supplier default: the supplier has no current economic capacity for this cycle.";
                return true;
            }

            if (!TryGetCategory(contract.thingDef, out IntercolonyProductCategory category,
                    out string categoryReason))
            {
                failureReason = "Supplier default: " + categoryReason;
                return true;
            }

            if (!RfqService.CanTechnicallySupply(contract.thingDef, profile))
            {
                failureReason =
                    $"Supplier default: {settlement.Label} can no longer technically supply " +
                    $"{contract.ItemLabel()}.";
                return true;
            }

            float effectiveSupply = EffectiveEconomyService.EffectiveSupply(
                state, profile, category);
            int available = RfqService.SupplierOfferQuantity(
                contract.thingDef, contract.stuffDef, profile, effectiveSupply);
            if (available >= contract.quantityPerCycle)
            {
                return false;
            }

            List<string> rows = new List<string>
            {
                $"Promised: {contract.quantityPerCycle}x {contract.ItemLabel()}",
                $"Current effective supply: {effectiveSupply:F2}",
                $"Current capacity: {available}x {contract.ItemLabel()}"
            };
            foreach (PriceFactor factor in EffectiveEconomyService.ExplainSupply(
                         state, profile, category))
            {
                if (factor.label != "Local supply")
                {
                    rows.Add($"{factor.label}: {factor.multiplier:F2}x");
                }
            }

            failureReason = "Supplier default: current supply conditions cannot cover this cycle.\n" +
                            string.Join("\n", rows.ToArray());
            return true;
        }

        private static bool CycleCountReached(ProcurementContract contract)
        {
            return contract.cyclesCompleted + contract.cyclesFailed >= contract.totalCycles;
        }

        private static void Complete(
            IntercolonyWorldComponent state, ProcurementContract contract)
        {
            if (contract == null || contract.status == ProcurementContractStatus.Completed)
            {
                return;
            }

            contract.status = ProcurementContractStatus.Completed;
            contract.activeOrderId = ProcurementContract.NoActiveOrderId;
            contract.outcomeNote =
                $"All {contract.totalCycles} procurement cycles resolved: " +
                $"{contract.cyclesCompleted} completed and {contract.cyclesFailed} failed.";

            IntercolonyLetters.Send(
                IntercolonyLetterImportance.Always,
                "Procurement agreement completed",
                $"Your procurement agreement with {contract.settlementName} is complete.\n\n" +
                contract.outcomeNote,
                LetterDefOf.PositiveEvent);
            IntercolonyLog.Message(
                $"Procurement contract {contract.id} completed: {contract.cyclesCompleted} " +
                $"cycles completed, {contract.cyclesFailed} failed.");
        }

        /// <summary>Withdraws a still-pending proposal without creating an agreement.</summary>
        public static bool CancelProposal(
            IntercolonyWorldComponent state, ProcurementContract contract)
        {
            if (state == null || contract == null || !contract.IsPendingProposal)
            {
                return false;
            }

            contract.status = ProcurementContractStatus.Cancelled;
            contract.outcomeNote = "The player withdrew the supplier proposal.";
            ClearPendingAnswer(contract);
            IntercolonyLog.Message($"Player cancelled procurement proposal {contract.id}.");
            return true;
        }

        private static float DelayAppeal(IntercolonyNegotiationResult evaluation)
        {
            if (evaluation == null)
            {
                return 0f;
            }

            return evaluation.Decision == IntercolonyNegotiationDecision.Accepted
                ? 1f
                : evaluation.Decision == IntercolonyNegotiationDecision.Refused ? 0f : 0.5f;
        }

        private static void ClearPendingAnswer(ProcurementContract contract)
        {
            contract.decisionDueTick = ProcurementContract.NoDecisionDueTick;
            contract.proposalAppeal = ProcurementContract.NoProposalAppeal;
        }

        private static bool TryGetCategory(
            ThingDef thingDef, out IntercolonyProductCategory category, out string reason)
        {
            category = default(IntercolonyProductCategory);
            if (thingDef == null || string.IsNullOrEmpty(thingDef.defName) ||
                DefDatabase<ThingDef>.GetNamedSilentFail(thingDef.defName) != thingDef)
            {
                reason = "The selected item is not registered in the active ThingDef database.";
                return false;
            }

            if (!IntercolonyProductClassifier.TryGetTradableCategory(
                    thingDef, out category))
            {
                string exclusion = IntercolonyTradeBlacklist.ExclusionReason(thingDef);
                reason = exclusion != null
                    ? $"{thingDef.label} is excluded from Intercolony trade: {exclusion}."
                    : $"{thingDef.label} has no Intercolony product category.";
                return false;
            }

            reason = null;
            return true;
        }
    }
}
