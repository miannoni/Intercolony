using System;
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
            string reason = decision == IntercolonyNegotiationDecision.Countered
                ? "The supplier returned a counterproposal; no agreement was formed."
                : "The supplier declined the proposed procurement terms.";

            if (decision == IntercolonyNegotiationDecision.Accepted)
            {
                contract.status = ProcurementContractStatus.Active;
                contract.nextCycleTick = GenTicks.TicksGame +
                    contract.cadenceDays * GenDate.TicksPerDay;
                contract.outcomeNote = "Supplier accepted; first procurement cycle scheduled.";
                ClearPendingAnswer(contract);

                IntercolonyLetters.Send(
                    IntercolonyLetterImportance.Always,
                    "Procurement agreement accepted",
                    $"{contract.settlementName} accepted your standing procurement agreement.\n\n" +
                    $"They will provide {contract.quantityPerCycle}x {contract.thingDef.label} " +
                    $"every {contract.cadenceDays} days for {contract.totalCycles} cycles at " +
                    $"{contract.unitPrice:F2} silver per unit.\n\n" +
                    $"The first cycle is scheduled in {contract.cadenceDays} days. No order or " +
                    "silver has been created yet; each cycle settles separately.",
                    LetterDefOf.PositiveEvent);
                IntercolonyLog.Message($"Supplier accepted procurement proposal {contract.id}.");
                return ProcurementContractAnswer.AppliedAnswer(contract, decision, null);
            }

            contract.status = ProcurementContractStatus.Cancelled;
            contract.outcomeNote = reason;
            ClearPendingAnswer(contract);
            IntercolonyLetters.Send(
                IntercolonyLetterImportance.Important,
                decision == IntercolonyNegotiationDecision.Countered
                    ? "Procurement agreement countered"
                    : "Procurement agreement declined",
                $"{contract.settlementName} did not accept your proposed standing procurement " +
                $"agreement for {contract.thingDef.label}.\n\n{reason}",
                LetterDefOf.NeutralEvent);
            IntercolonyLog.Message($"Supplier declined procurement proposal {contract.id}.");
            return ProcurementContractAnswer.AppliedAnswer(contract, decision, reason);
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
