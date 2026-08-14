using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>Why a player-proposed recurring contract was refused.</summary>
    public enum ContractProposalFailure
    {
        None,
        InvalidState,
        InaccessibleSettlement,
        ReputationTooLow,
        ExistingContract,
        MissingEconomicProfile,
        InvalidItem,
        InsufficientTradeHistory,
        QuantityOutOfRange
    }

    /// <summary>The settlement's answer to a player-proposed recurring contract.</summary>
    public sealed class ContractProposalResult
    {
        private ContractProposalResult(
            RecurringContract contract, ContractProposalFailure failure, string reason)
        {
            Contract = contract;
            Failure = failure;
            Reason = reason;
        }

        public bool Success => Contract != null && Failure == ContractProposalFailure.None;

        public RecurringContract Contract { get; }

        public ContractProposalFailure Failure { get; }

        public string Reason { get; }

        internal static ContractProposalResult Accepted(RecurringContract contract)
        {
            return new ContractProposalResult(contract, ContractProposalFailure.None, null);
        }

        internal static ContractProposalResult Refused(
            ContractProposalFailure failure, string reason)
        {
            return new ContractProposalResult(null, failure, reason);
        }
    }

    /// <summary>
    /// Offers, runs and ends recurring contracts (DESIGN.md §29, §30, §107).
    ///
    /// Owns every <see cref="RecurringContract.status"/> transition (§73).
    ///
    /// Contracts are gated on commercial reputation, which is §28's "access to recurring
    /// contracts" made concrete: a settlement will not stake a year of its supply on someone
    /// with no record. That also gives Phase 13 somewhere to lead.
    /// </summary>
    public static class ContractService
    {
        /// <summary>Minimum reputation before a settlement will propose a standing agreement.</summary>
        public const float MinimumReputation = 62f;

        /// <summary>Completed sales of one exact good needed before a settlement trusts a repeat supply.</summary>
        public const int MinimumCompletedOrdersForAgreement = 2;

        /// <summary>How long a proposal stays on the table.</summary>
        private const int OfferLifespanDays = 8;

        /// <summary>Chance per refresh that a qualifying settlement proposes one.</summary>
        private const float OfferChance = 0.12f;

        /// <summary>
        /// A contract pays a premium over spot: the buyer is buying certainty, and the player
        /// is giving up the freedom to sell elsewhere. Without this there is no reason to take
        /// one, and §29's "commitment causes the player to expand capacity" never happens.
        /// </summary>
        private const float ContractPricePremium = 1.15f;

        /// <summary>
        /// Existing offer sizing clamps every recurring delivery to this range.
        /// Player-proposed terms use the same bounds rather than introducing another capacity rule.
        /// </summary>
        public const int MinimumQuantityPerCycle = 10;
        public const int MaximumQuantityPerCycle = 4000;

        /// <summary>Proposes agreements to settlements that trust the colony enough (§28).</summary>
        public static int OfferContracts(IntercolonyWorldComponent state)
        {
            if (!state.ReceiveContractProposals)
            {
                return 0;
            }

            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements == null)
            {
                return 0;
            }

            Dictionary<int, Dictionary<ThingDef, int>> completedOrders =
                BuildCompletedOrderCounts(state);

            int created = 0;
            foreach (Settlement settlement in settlements)
            {
                if (!TryGetEligibleCounterparty(
                        state, settlement, out SettlementEconomicProfile profile, out _, out _))
                {
                    continue;
                }

                completedOrders.TryGetValue(
                    settlement.ID, out Dictionary<ThingDef, int> settlementHistory);
                RecurringContract contract = TryBuildOffer(
                    state, settlement, profile, settlementHistory);
                if (contract != null)
                {
                    state.AddContract(contract);
                    created++;

                    int completedCount = settlementHistory[contract.thingDef];
                    string frequency = completedCount == 2
                        ? "twice"
                        : $"{completedCount} times";

                    IntercolonyLetters.Send(
                        IntercolonyLetterImportance.Always,
                        "Supply agreement offered",
                        $"{settlement.Label} has bought {contract.thingDef.label} from you " +
                        $"{frequency} and now wants a standing supply agreement.\n\n" +
                        $"{contract.quantityPerCycle}x {contract.ItemLabel()} every " +
                        $"{contract.CadenceDays:F0} days, for {contract.totalCycles} deliveries.\n" +
                        $"{contract.CycleValue} silver per delivery, {contract.TotalValue} in total.\n\n" +
                        "Review it in the Intercolony Contracts tab. A standing agreement is worth " +
                        "more per unit than spot sales, but missing deliveries breaks it.",
                        LetterDefOf.PositiveEvent);
                }
            }

            return created;
        }

        private static RecurringContract TryBuildOffer(
            IntercolonyWorldComponent state, Settlement settlement, SettlementEconomicProfile profile,
            Dictionary<ThingDef, int> completedOrders)
        {
            int seed = Gen.HashCombineInt(state.EconomySeed, settlement.ID, state.RefreshCount, 0x0C0A);

            Rand.PushState(seed);
            try
            {
                if (Rand.Value > OfferChance)
                {
                    return null;
                }
            }
            finally
            {
                Rand.PopState();
            }

            return BuildOffer(state, settlement, profile, seed, completedOrders);
        }

        /// <summary>
        /// Builds an offer unconditionally, skipping the chance roll.
        ///
        /// Public so tests and debug tooling exercise the **real** pricing and sizing rules.
        /// A self-test that constructs its own contract only proves its own arithmetic — that
        /// mistake produced a false failure here, asserting a property that only this method
        /// guarantees against an object it never made.
        /// </summary>
        public static RecurringContract BuildOffer(
            IntercolonyWorldComponent state, Settlement settlement, SettlementEconomicProfile profile,
            int seed)
        {
            Dictionary<int, Dictionary<ThingDef, int>> completedOrders =
                BuildCompletedOrderCounts(state);
            completedOrders.TryGetValue(
                settlement.ID, out Dictionary<ThingDef, int> settlementHistory);
            return BuildOffer(state, settlement, profile, seed, settlementHistory);
        }

        private static RecurringContract BuildOffer(
            IntercolonyWorldComponent state, Settlement settlement, SettlementEconomicProfile profile,
            int seed, Dictionary<ThingDef, int> completedOrders)
        {
            Rand.PushState(seed);
            try
            {
                // Contracts are for things a colony can produce repeatedly, so stick to
                // stackable goods; a standing order for one masterwork chair a quadrum is not
                // the strategic commitment §29 is describing.
                List<KeyValuePair<ThingDef, int>> candidates =
                    new List<KeyValuePair<ThingDef, int>>();
                if (completedOrders != null)
                {
                    foreach (KeyValuePair<ThingDef, int> entry in completedOrders)
                    {
                        ThingDef def = entry.Key;
                        if (entry.Value < MinimumCompletedOrdersForAgreement ||
                            !TryGetEligibleItemCategory(
                                def, out IntercolonyProductCategory candidateCategory))
                        {
                            continue;
                        }

                        if (!state.ReceiveContractProposalsFor(candidateCategory))
                        {
                            continue;
                        }

                        candidates.Add(entry);
                    }
                }

                if (candidates.Count == 0)
                {
                    return null;
                }

                candidates.Sort((left, right) =>
                    string.CompareOrdinal(left.Key.defName, right.Key.defName));

                int totalWeight = 0;
                foreach (KeyValuePair<ThingDef, int> candidate in candidates)
                {
                    totalWeight += candidate.Value;
                }

                int choice = Rand.Range(0, totalWeight);
                ThingDef chosen = candidates[candidates.Count - 1].Key;
                foreach (KeyValuePair<ThingDef, int> candidate in candidates)
                {
                    if (choice < candidate.Value)
                    {
                        chosen = candidate.Key;
                        break;
                    }

                    choice -= candidate.Value;
                }

                IntercolonyProductCategory category =
                    IntercolonyProductClassifier.Classify(chosen).Value;

                int quantity = ContractQuantity(chosen, profile);
                return BuildContract(state, settlement, profile, chosen, category, quantity);
            }
            finally
            {
                Rand.PopState();
            }
        }

        /// <summary>
        /// Proposes the supplied fixed terms to a settlement. Passing every existing commercial
        /// gate is the settlement's acceptance; this release has no negotiation or counteroffer
        /// state in which a player-originated proposal could remain pending.
        /// </summary>
        public static ContractProposalResult ProposeContract(
            IntercolonyWorldComponent state,
            Settlement settlement,
            ThingDef thingDef,
            int quantityPerCycle)
        {
            if (state == null)
            {
                return ContractProposalResult.Refused(
                    ContractProposalFailure.InvalidState, "No Intercolony world state is available.");
            }

            if (!TryGetEligibleCounterparty(
                    state,
                    settlement,
                    out SettlementEconomicProfile profile,
                    out ContractProposalFailure counterpartyFailure,
                    out string counterpartyReason))
            {
                return ContractProposalResult.Refused(
                    counterpartyFailure, counterpartyReason);
            }

            if (!TryGetEligibleItemCategory(
                    thingDef, out IntercolonyProductCategory category, out string itemReason))
            {
                return ContractProposalResult.Refused(
                    ContractProposalFailure.InvalidItem, itemReason);
            }

            Dictionary<int, Dictionary<ThingDef, int>> completedOrders =
                BuildCompletedOrderCounts(state);
            completedOrders.TryGetValue(
                settlement.ID, out Dictionary<ThingDef, int> settlementHistory);
            int completedSales = 0;
            settlementHistory?.TryGetValue(thingDef, out completedSales);
            if (completedSales < MinimumCompletedOrdersForAgreement)
            {
                return ContractProposalResult.Refused(
                    ContractProposalFailure.InsufficientTradeHistory,
                    $"Only {completedSales} completed sale(s) of {thingDef.label} to that settlement; " +
                    $"{MinimumCompletedOrdersForAgreement} are required.");
            }

            if (quantityPerCycle < MinimumQuantityPerCycle ||
                quantityPerCycle > MaximumQuantityPerCycle)
            {
                return ContractProposalResult.Refused(
                    ContractProposalFailure.QuantityOutOfRange,
                    $"Quantity per cycle must be between {MinimumQuantityPerCycle} and " +
                    $"{MaximumQuantityPerCycle}.");
            }

            int seed = Gen.HashCombineInt(
                state.EconomySeed, settlement.ID, thingDef.shortHash, quantityPerCycle);
            RecurringContract contract;
            Rand.PushState(seed);
            try
            {
                contract = BuildContract(
                    state, settlement, profile, thingDef, category, quantityPerCycle);
            }
            finally
            {
                Rand.PopState();
            }

            // Construction starts at Offered so the established transition sets the first due
            // date. Eligibility above is the settlement's yes, so no pending offer is stored.
            if (!contract.TryAccept())
            {
                return ContractProposalResult.Refused(
                    ContractProposalFailure.InvalidState,
                    "The accepted contract could not enter its active lifecycle.");
            }

            state.AddContract(contract);
            IntercolonyLog.Message(
                $"Player-proposed contract {contract.id} accepted: {contract.quantityPerCycle}x " +
                $"{contract.thingDef.label} every {contract.CadenceDays:F0}d x{contract.totalCycles} " +
                $"for {contract.settlementName}.");
            return ContractProposalResult.Accepted(contract);
        }

        private static bool TryGetEligibleCounterparty(
            IntercolonyWorldComponent state,
            Settlement settlement,
            out SettlementEconomicProfile profile,
            out ContractProposalFailure failure,
            out string reason)
        {
            profile = null;
            if (!IntercolonyMarketAccess.IsAccessible(settlement, out string accessReason))
            {
                failure = ContractProposalFailure.InaccessibleSettlement;
                reason = "The settlement is inaccessible: " + accessReason + ".";
                return false;
            }

            float reputation = ReputationService.ScoreFor(state, settlement);
            if (reputation < MinimumReputation)
            {
                failure = ContractProposalFailure.ReputationTooLow;
                reason =
                    $"Commercial reputation is {reputation:F0}; {MinimumReputation:F0} is required.";
                return false;
            }

            // One live proposal or agreement per settlement: a standing supply deal is a
            // relationship, not a stack of them.
            if (state.HasContractWith(settlement.ID))
            {
                failure = ContractProposalFailure.ExistingContract;
                reason = "That settlement already has a live contract or pending renewal.";
                return false;
            }

            profile = state.GetProfile(settlement);
            if (profile == null)
            {
                failure = ContractProposalFailure.MissingEconomicProfile;
                reason = "The settlement has no economic profile.";
                return false;
            }

            failure = ContractProposalFailure.None;
            reason = null;
            return true;
        }

        private static bool TryGetEligibleItemCategory(
            ThingDef def, out IntercolonyProductCategory category)
        {
            return TryGetEligibleItemCategory(def, out category, out _);
        }

        private static bool TryGetEligibleItemCategory(
            ThingDef def, out IntercolonyProductCategory category, out string reason)
        {
            category = default(IntercolonyProductCategory);
            if (def == null || DefDatabase<ThingDef>.GetNamedSilentFail(def.defName) != def)
            {
                reason = "The selected item is not registered in the active ThingDef database.";
                return false;
            }

            string exclusion = IntercolonyTradeBlacklist.ExclusionReason(def);
            if (exclusion != null)
            {
                reason = $"{def.label} is excluded from Intercolony trade: {exclusion}.";
                return false;
            }

            if (!IntercolonyProductClassifier.IsFungibleTradeItem(def))
            {
                reason = $"{def.label} is not a fungible Intercolony trade item.";
                return false;
            }

            if (def.stackLimit <= 1)
            {
                reason = $"{def.label} is not stackable.";
                return false;
            }

            if (def.category != ThingCategory.Item)
            {
                reason = $"{def.label} is not a physical item.";
                return false;
            }

            IntercolonyProductCategory? classified = IntercolonyProductClassifier.Classify(def);
            if (!classified.HasValue)
            {
                reason = $"{def.label} has no Intercolony product category.";
                return false;
            }

            category = classified.Value;
            reason = null;
            return true;
        }

        private static RecurringContract BuildContract(
            IntercolonyWorldComponent state,
            Settlement settlement,
            SettlementEconomicProfile profile,
            ThingDef thingDef,
            IntercolonyProductCategory category,
            int quantityPerCycle)
        {
            float distance = MarketOpportunityGenerator.DistanceToPlayer(settlement);
            float spot = IntercolonyPricing.UnitPrice(
                thingDef, null, quantityPerCycle, profile, category, distance, null, out _);

            return new RecurringContract
            {
                id = state.NextId(),
                settlementId = settlement.ID,
                settlementName = settlement.Label ?? "unnamed",
                factionName = settlement.Faction?.Name ?? "",
                thingDef = thingDef,
                quantityPerCycle = quantityPerCycle,
                cadenceTicks = GenDate.TicksPerQuadrum,
                totalCycles = Rand.RangeInclusive(3, 6),
                unitPrice = spot * ContractPricePremium,
                status = ContractStatus.Offered,
                offerExpiryTick = GenTicks.TicksGame + OfferLifespanDays * GenDate.TicksPerDay
            };
        }

        /// <summary>
        /// Projects the durable proof of supply into the lookup shape used during one refresh.
        /// </summary>
        private static Dictionary<int, Dictionary<ThingDef, int>> BuildCompletedOrderCounts(
            IntercolonyWorldComponent state)
        {
            Dictionary<int, Dictionary<ThingDef, int>> result =
                new Dictionary<int, Dictionary<ThingDef, int>>();

            foreach (CommercialHistoryEntry entry in state.CommercialHistory)
            {
                ThingDef def = entry?.thingDef;
                if (entry == null || entry.completedSaleCount <= 0 || def == null)
                {
                    continue;
                }

                if (!result.TryGetValue(
                        entry.settlementId, out Dictionary<ThingDef, int> settlementHistory))
                {
                    settlementHistory = new Dictionary<ThingDef, int>();
                    result.Add(entry.settlementId, settlementHistory);
                }

                settlementHistory.TryGetValue(def, out int count);
                settlementHistory[def] = count + entry.completedSaleCount;
            }

            return result;
        }

        /// <summary>
        /// Per-cycle quantity. Deliberately larger than a typical spot order — the point of a
        /// contract is that it is worth restructuring production around (§29).
        /// Must be called inside a pushed Rand state.
        /// </summary>
        private static int ContractQuantity(ThingDef def, SettlementEconomicProfile profile)
        {
            float targetSilver = Rand.Range(1500f, 5000f) *
                                 (profile.wealthTier >= IntercolonyWealthTier.Comfortable ? 1.4f : 0.8f);
            float unitValue = Mathf.Max(0.4f, IntercolonyPricing.BaseValue(def, null));
            int quantity = Mathf.RoundToInt(targetSilver / unitValue);
            quantity = Mathf.Clamp(quantity, MinimumQuantityPerCycle, MaximumQuantityPerCycle);

            // Round to a number a contract would actually name.
            if (quantity > 100)
            {
                quantity = Mathf.RoundToInt(quantity / 50f) * 50;
            }

            return Mathf.Max(MinimumQuantityPerCycle, quantity);
        }

        /// <summary>
        /// Drives live contracts: raises each cycle's order when due, and reacts to how the
        /// previous one ended. Called from the coarse refresh.
        /// </summary>
        public static void AdvanceContracts(IntercolonyWorldComponent state)
        {
            int now = GenTicks.TicksGame;

            foreach (RecurringContract contract in state.Contracts)
            {
                if (contract.IsOffer && now >= contract.offerExpiryTick)
                {
                    contract.TryDecline("Offer lapsed unanswered.");
                    continue;
                }

                // A renewal offer left unanswered lapses, and says so. §115 forbids silent endings,
                // and an offer that quietly evaporated would be exactly that.
                if (contract.renewalOffered && now >= contract.renewalExpiryTick)
                {
                    contract.renewalOffered = false;
                    contract.renewalExpiryTick = 0;
                    contract.outcomeNote += " Renewal offer lapsed unanswered.";

                    IntercolonyLetters.Send(
                        IntercolonyLetterImportance.Important,
                        "Renewal offer lapsed",
                        $"{contract.settlementName}'s offer to renew your supply agreement has expired " +
                        "unanswered.\n\nThey have made other arrangements.",
                        LetterDefOf.NeutralEvent);
                    continue;
                }

                if (!contract.IsActive)
                {
                    continue;
                }

                // Resolve the cycle in flight before starting another.
                if (contract.activeOrderId != 0)
                {
                    SalesOrder order = state.FindOrder(contract.activeOrderId);
                    if (order == null || order.IsOpen)
                    {
                        continue;
                    }

                    ResolveCycle(state, contract, order);
                    contract.activeOrderId = 0;

                    if (!contract.IsActive)
                    {
                        continue;
                    }
                }

                if (contract.CyclesRemaining <= 0)
                {
                    // Nothing left to deliver and no order in flight. Normal play cannot reach this
                    // — the last cycle's order resolves and completes it — but anything that credits
                    // cycles without an order would otherwise strand the agreement Active forever.
                    Complete(state, contract);
                    continue;
                }

                if (now >= contract.nextCycleTick)
                {
                    RaiseCycleOrder(state, contract);
                }
            }
        }

        /// <summary>
        /// Closes an agreement that has run its course, and asks whether it carries on.
        ///
        /// **The only way a contract becomes Completed**, deliberately. It used to be inline in
        /// <see cref="ResolveCycle"/>, which meant it could only ever be reached by an order
        /// finishing — so a debug helper that credited the remaining cycles directly left the
        /// contract Active with nothing left to deliver and no way out. One entry point, so
        /// crediting cycles and completing cannot come apart again.
        /// </summary>
        public static void Complete(IntercolonyWorldComponent state, RecurringContract contract)
        {
            if (contract == null || contract.status == ContractStatus.Completed)
            {
                return;
            }

            contract.status = ContractStatus.Completed;
            contract.activeOrderId = 0;
            contract.outcomeNote =
                $"All {contract.totalCycles} deliveries met. {contract.TotalValue} silver total.";

            // §27 lists repeated business as a positive; seeing an agreement through
            // is the strongest version of that.
            CommercialReputation rep = ReputationService.ForSettlement(state, contract.settlementId);
            rep?.Adjust(8f);

            OfferRenewal(state, contract);
        }

        /// <summary>
        /// A completed agreement either gets offered again or is closed with a reason (§115, §107).
        ///
        /// §115's acceptance criterion says an agreement that runs its course *"either renews or is
        /// declined for a stated reason. Neither employment nor supply agreements end by silently
        /// lapsing."* Before this, a completed agreement simply stopped — §107 listed renewal and
        /// Phase 14 did not build it.
        ///
        /// Deliberately the same shape as the employment renewal in <see cref="RenewalService"/>:
        /// the *counterparty* offers, and whether they offer depends on the player's record with
        /// them. The two use different reputations — commercial here, employer there — because a
        /// settlement's opinion of you as a supplier is per settlement (§8) while your name as an
        /// employer is not, but the mechanism and the wording are one.
        /// </summary>
        private static void OfferRenewal(IntercolonyWorldComponent state, RecurringContract contract)
        {
            CommercialReputation standing = ReputationService.ForSettlement(state, contract.settlementId);
            float score = standing?.Score ?? CommercialReputation.StartingScore;

            // Reliability rather than the score alone: a run with missed deliveries in it is a
            // reason not to re-sign even if the relationship survived them.
            bool clean = contract.cyclesFailed == 0;
            bool trusted = score >= MinimumReputation;

            if (!clean || !trusted)
            {
                contract.outcomeNote += !clean
                    ? $" Not renewed: {contract.cyclesFailed} missed deliver" +
                      (contract.cyclesFailed == 1 ? "y." : "ies.")
                    : " Not renewed: they do not trust the arrangement enough to repeat it.";

                IntercolonyLetters.Send(
                    IntercolonyLetterImportance.Important,
                    "Supply agreement fulfilled",
                    $"You completed every remaining delivery of your agreement with " +
                    $"{contract.settlementName}.\n\n" + contract.outcomeNote + "\n\n" +
                    (clean
                        ? "Build your standing with them and they may propose another."
                        : "A run without a missed delivery is what gets one renewed."),
                    LetterDefOf.NeutralEvent);
                return;
            }

            contract.renewalOffered = true;
            contract.renewalExpiryTick = GenTicks.TicksGame + OfferLifespanDays * GenDate.TicksPerDay;

            IntercolonyLetters.Send(
                IntercolonyLetterImportance.Always,
                "Supply agreement — renewal offered",
                $"You completed every delivery of your agreement with {contract.settlementName}, " +
                $"{contract.cyclesCompleted} of {contract.totalCycles}, without missing one.\n\n" +
                $"They would sign again on the same terms: {contract.quantityPerCycle}x " +
                $"{contract.ItemLabel()} every {contract.CadenceDays:F0} days, " +
                $"{contract.totalCycles} more times at {contract.unitPrice:F2} each.\n\n" +
                $"Answer in the Contracts tab within {OfferLifespanDays} days.",
                LetterDefOf.PositiveEvent);

            IntercolonyLog.Message($"Renewal offered on contract {contract.id} by {contract.settlementName}.");
        }

        /// <summary>Takes up a renewal offer: the same agreement, its counters reset for a fresh run.</summary>
        public static bool AcceptRenewal(IntercolonyWorldComponent state, RecurringContract contract)
        {
            if (contract == null || !contract.renewalOffered ||
                contract.status != ContractStatus.Completed)
            {
                return false;
            }

            contract.status = ContractStatus.Active;
            contract.outcomeNote = "";
            contract.cyclesCompleted = 0;
            contract.cyclesFailed = 0;
            contract.consecutiveFailures = 0;
            contract.activeOrderId = 0;
            contract.nextCycleTick = GenTicks.TicksGame;
            contract.renewalOffered = false;
            contract.renewalExpiryTick = 0;
            contract.renewals++;

            CommercialReputation rep = ReputationService.ForSettlement(state, contract.settlementId);
            rep?.Adjust(4f);

            Messages.Message(
                $"Renewed the supply agreement with {contract.settlementName}: " +
                $"{contract.quantityPerCycle}x {contract.ItemLabel()} every " +
                $"{contract.CadenceDays:F0} days, {contract.totalCycles} more times.",
                MessageTypeDefOf.PositiveEvent, historical: false);

            IntercolonyLog.Message($"Contract {contract.id} renewed (run {contract.renewals + 1}).");
            return true;
        }

        /// <summary>
        /// Turns a renewal down. §115 calls this voluntary non-renewal, and it is not a breach —
        /// the agreement was completed. Declining costs nothing but the relationship's momentum.
        /// </summary>
        public static void DeclineRenewal(RecurringContract contract)
        {
            if (contract == null || !contract.renewalOffered)
            {
                return;
            }

            contract.renewalOffered = false;
            contract.renewalExpiryTick = 0;
            contract.outcomeNote += " Renewal declined.";

            Messages.Message(
                $"Declined to renew with {contract.settlementName}.",
                MessageTypeDefOf.NeutralEvent, historical: false);
        }

        private static void ResolveCycle(
            IntercolonyWorldComponent state, RecurringContract contract, SalesOrder order)
        {
            if (order.status == SalesOrderStatus.Completed)
            {
                contract.cyclesCompleted++;
                contract.consecutiveFailures = 0;

                if (contract.CyclesRemaining <= 0)
                {
                    Complete(state, contract);
                }

                return;
            }

            // Anything other than completion is a missed delivery.
            contract.cyclesFailed++;
            contract.consecutiveFailures++;

            if (contract.consecutiveFailures >= RecurringContract.BreachThreshold)
            {
                contract.status = ContractStatus.Breached;
                contract.outcomeNote =
                    $"Breached after {contract.consecutiveFailures} consecutive missed deliveries.";

                CommercialReputation rep = ReputationService.ForSettlement(state, contract.settlementId);
                rep?.Adjust(-20f);

                IntercolonyLetters.Send(
                    IntercolonyLetterImportance.Always,
                    "Supply agreement broken",
                    $"{contract.settlementName} has terminated your supply agreement after " +
                    $"{contract.consecutiveFailures} consecutive missed deliveries.\n\n" +
                    "Their opinion of you as a supplier has suffered considerably.",
                    LetterDefOf.NegativeEvent);
            }
            else
            {
                IntercolonyLetters.Send(
                    IntercolonyLetterImportance.Always,
                    "Delivery missed",
                    $"You missed a delivery to {contract.settlementName}. One more in a row and " +
                    "the agreement ends.",
                    LetterDefOf.NegativeEvent);
            }
        }

        /// <summary>Creates the sales order for this cycle.</summary>
        private static void RaiseCycleOrder(IntercolonyWorldComponent state, RecurringContract contract)
        {
            Settlement settlement = IntercolonyMarketAccess.FindSettlement(contract.settlementId);
            if (settlement == null || !IntercolonyMarketAccess.IsAccessible(settlement))
            {
                // The counterparty is gone or hostile. Ending it here is kinder than letting
                // the player keep failing deliveries to someone who cannot receive them (§88).
                contract.status = ContractStatus.Cancelled;
                contract.outcomeNote = "The counterparty is no longer reachable.";
                return;
            }

            SalesOrder order = new SalesOrder
            {
                id = state.NextId(),
                settlementId = contract.settlementId,
                settlementName = contract.settlementName,
                factionName = contract.factionName,
                line = new OrderLine(contract.thingDef, contract.quantityPerCycle)
                {
                    minQuality = contract.minQuality,
                    allowedStuff = contract.stuffDef
                },
                unitPrice = contract.unitPrice,
                acceptedTick = GenTicks.TicksGame,

                // The whole cycle is the delivery window — that is what makes the commitment
                // plannable rather than a recurring emergency.
                deadlineTick = GenTicks.TicksGame + contract.cadenceTicks,
                status = SalesOrderStatus.Accepted,
                fulfillment = contract.fulfillment,
                contractId = contract.id
            };

            state.AddOrder(order);
            contract.activeOrderId = order.id;
            contract.nextCycleTick = GenTicks.TicksGame + contract.cadenceTicks;

            IntercolonyLetters.Send(
                IntercolonyLetterImportance.Always,
                "Contract delivery due",
                $"Delivery {contract.cyclesCompleted + contract.cyclesFailed + 1} of " +
                $"{contract.totalCycles} for {contract.settlementName}:\n\n" +
                $"{contract.quantityPerCycle}x {contract.ItemLabel()} within " +
                $"{contract.CadenceDays:F0} days, for {contract.CycleValue} silver.",
                LetterDefOf.NeutralEvent);
        }

        /// <summary>Best premium a negotiator can talk the buyer up to, at Social 20.</summary>
        public const float MaxNegotiationPremium = 0.15f;

        /// <summary>The narrowest and widest the player may resize an offered agreement.</summary>
        public const float QuantityAdjustment = 0.1f;

        public static int MinAcceptableQuantity(RecurringContract contract)
        {
            return Mathf.Max(
                1,
                Mathf.RoundToInt((contract?.quantityPerCycle ?? 0) * (1f - QuantityAdjustment)));
        }

        public static int MaxAcceptableQuantity(RecurringContract contract)
        {
            return Mathf.Max(
                MinAcceptableQuantity(contract),
                Mathf.RoundToInt((contract?.quantityPerCycle ?? 0) * (1f + QuantityAdjustment)));
        }

        /// <summary>
        /// What the colony's best talker can add to the offered rate. Mirrors the release-fee
        /// negotiation in <see cref="TransitionService"/>: the same skill, read the same way,
        /// so a good negotiator is worth having for the same reason in both places.
        /// </summary>
        public static float NegotiatedUnitPrice(RecurringContract contract, Pawn negotiator)
        {
            float offered = contract?.unitPrice ?? 0f;
            SkillRecord social = negotiator?.skills?.GetSkill(SkillDefOf.Social);
            if (social == null || social.TotallyDisabled)
            {
                return offered;
            }

            float premium = Mathf.Lerp(
                0f, MaxNegotiationPremium, Mathf.Clamp01(social.Level / 20f));
            return offered * (1f + premium);
        }

        public static bool AcceptOffer(IntercolonyWorldComponent state, RecurringContract contract)
        {
            return AcceptOffer(
                state, contract, contract?.quantityPerCycle ?? 0,
                contract?.fulfillment ?? FulfillmentMode.SellerDelivery, null);
        }

        /// <summary>
        /// Accepts on terms the player adjusted: a slightly larger or smaller commitment, who
        /// moves the goods, and whatever a negotiator argued the rate up to.
        /// </summary>
        public static bool AcceptOffer(
            IntercolonyWorldComponent state,
            RecurringContract contract,
            int quantityPerCycle,
            FulfillmentMode fulfillment,
            Pawn negotiator)
        {
            if (contract == null)
            {
                return false;
            }

            // Settle the terms before the transition, so a refused acceptance cannot leave a
            // still-offered agreement carrying terms the player only proposed.
            int agreedQuantity = Mathf.Clamp(
                quantityPerCycle,
                MinAcceptableQuantity(contract),
                MaxAcceptableQuantity(contract));
            float agreedPrice = NegotiatedUnitPrice(contract, negotiator);

            if (!contract.TryAccept())
            {
                return false;
            }

            contract.quantityPerCycle = agreedQuantity;
            contract.fulfillment = fulfillment;
            contract.unitPrice = agreedPrice;

            IntercolonyLog.Message(
                $"Contract {contract.id} accepted: {contract.quantityPerCycle}x " +
                $"{contract.thingDef.label} every {contract.CadenceDays:F0}d x{contract.totalCycles} " +
                $"for {contract.settlementName}.");
            Messages.Message(
                $"Supply agreement with {contract.settlementName} begins. First delivery due in " +
                $"{contract.CadenceDays:F0} days.",
                MessageTypeDefOf.PositiveEvent, historical: false);
            return true;
        }

        /// <summary>
        /// Player withdraws from a live agreement. Costs reputation — less than a breach, but
        /// walking away from a commitment is not free.
        /// </summary>
        public static bool CancelContract(IntercolonyWorldComponent state, RecurringContract contract)
        {
            bool suspended = contract != null && contract.status == ContractStatus.Suspended;

            if (contract == null || (!contract.IsActive && !suspended))
            {
                return false;
            }

            contract.status = ContractStatus.Cancelled;
            contract.outcomeNote = suspended
                ? "Withdrawn by the player while suspended by war."
                : "Withdrawn by the player.";

            // No reputation penalty for walking away from an agreement a war had already frozen:
            // §88's suspension exists because the interruption was not the player's doing, and
            // charging them for ending it would take that back.
            if (!suspended)
            {
                CommercialReputation rep = ReputationService.ForSettlement(state, contract.settlementId);
                rep?.Adjust(-10f);
            }

            IntercolonyLog.Message($"Contract {contract.id} cancelled by the player.");
            return true;
        }
    }
}
