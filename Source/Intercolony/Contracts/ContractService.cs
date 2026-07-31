using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
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

        /// <summary>Proposes agreements to settlements that trust the colony enough (§28).</summary>
        public static int OfferContracts(IntercolonyWorldComponent state)
        {
            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements == null)
            {
                return 0;
            }

            int created = 0;
            foreach (Settlement settlement in settlements)
            {
                if (!IntercolonyMarketAccess.IsAccessible(settlement))
                {
                    continue;
                }

                if (ReputationService.ScoreFor(state, settlement) < MinimumReputation)
                {
                    continue;
                }

                // One live proposal or agreement per settlement: a standing supply deal is a
                // relationship, not a stack of them.
                if (state.HasContractWith(settlement.ID))
                {
                    continue;
                }

                SettlementEconomicProfile profile = state.GetProfile(settlement);
                if (profile == null)
                {
                    continue;
                }

                RecurringContract contract = TryBuildOffer(state, settlement, profile);
                if (contract != null)
                {
                    state.AddContract(contract);
                    created++;

                    Find.LetterStack.ReceiveLetter(
                        "Supply agreement offered",
                        $"{settlement.Label} proposes a standing agreement:\n\n" +
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
            IntercolonyWorldComponent state, Settlement settlement, SettlementEconomicProfile profile)
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

            return BuildOffer(state, settlement, profile, seed);
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
            Rand.PushState(seed);
            try
            {
                // Contracts are for things a colony can produce repeatedly, so stick to
                // stackable goods; a standing order for one masterwork chair a quadrum is not
                // the strategic commitment §29 is describing.
                List<ThingDef> candidates = new List<ThingDef>();
                foreach (ThingDef def in IntercolonyProductClassifier.TradableDefs)
                {
                    if (def.stackLimit > 1 && def.category == ThingCategory.Item)
                    {
                        candidates.Add(def);
                    }
                }

                if (candidates.Count == 0)
                {
                    return null;
                }

                ThingDef chosen = candidates[Rand.Range(0, candidates.Count)];
                IntercolonyProductCategory category =
                    IntercolonyProductClassifier.Classify(chosen) ?? IntercolonyProductCategory.Commodities;

                float distance = MarketOpportunityGenerator.DistanceToPlayer(settlement);
                int quantity = ContractQuantity(chosen, profile);

                float spot = IntercolonyPricing.UnitPrice(
                    chosen, null, quantity, profile, category, distance, null, out _);

                RecurringContract contract = new RecurringContract
                {
                    id = state.NextId(),
                    settlementId = settlement.ID,
                    settlementName = settlement.Label ?? "unnamed",
                    factionName = settlement.Faction?.Name ?? "",
                    thingDef = chosen,
                    quantityPerCycle = quantity,
                    cadenceTicks = GenDate.TicksPerQuadrum,
                    totalCycles = Rand.RangeInclusive(3, 6),
                    unitPrice = spot * ContractPricePremium,
                    status = ContractStatus.Offered,
                    offerExpiryTick = GenTicks.TicksGame + OfferLifespanDays * GenDate.TicksPerDay
                };

                return contract;
            }
            finally
            {
                Rand.PopState();
            }
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
            quantity = Mathf.Clamp(quantity, 10, 4000);

            // Round to a number a contract would actually name.
            if (quantity > 100)
            {
                quantity = Mathf.RoundToInt(quantity / 50f) * 50;
            }

            return Mathf.Max(10, quantity);
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
                    contract.TryDecline();
                    continue;
                }

                // A renewal offer left unanswered lapses, and says so. §115 forbids silent endings,
                // and an offer that quietly evaporated would be exactly that.
                if (contract.renewalOffered && now >= contract.renewalExpiryTick)
                {
                    contract.renewalOffered = false;
                    contract.renewalExpiryTick = 0;
                    contract.outcomeNote += " Renewal offer lapsed unanswered.";

                    Find.LetterStack.ReceiveLetter(
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

                if (now >= contract.nextCycleTick && contract.CyclesRemaining > 0)
                {
                    RaiseCycleOrder(state, contract);
                }
            }
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

                Find.LetterStack.ReceiveLetter(
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

            Find.LetterStack.ReceiveLetter(
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
                    contract.status = ContractStatus.Completed;
                    contract.outcomeNote =
                        $"All {contract.totalCycles} deliveries met. {contract.TotalValue} silver total.";

                    // §27 lists repeated business as a positive; seeing an agreement through
                    // is the strongest version of that.
                    CommercialReputation rep = ReputationService.ForSettlement(state, contract.settlementId);
                    rep?.Adjust(8f);

                    OfferRenewal(state, contract);
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

                Find.LetterStack.ReceiveLetter(
                    "Supply agreement broken",
                    $"{contract.settlementName} has terminated your supply agreement after " +
                    $"{contract.consecutiveFailures} consecutive missed deliveries.\n\n" +
                    "Their opinion of you as a supplier has suffered considerably.",
                    LetterDefOf.NegativeEvent);
            }
            else
            {
                Find.LetterStack.ReceiveLetter(
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
                fulfillment = FulfillmentMode.SellerDelivery,
                contractId = contract.id
            };

            state.AddOrder(order);
            contract.activeOrderId = order.id;
            contract.nextCycleTick = GenTicks.TicksGame + contract.cadenceTicks;

            Find.LetterStack.ReceiveLetter(
                "Contract delivery due",
                $"Delivery {contract.cyclesCompleted + contract.cyclesFailed + 1} of " +
                $"{contract.totalCycles} for {contract.settlementName}:\n\n" +
                $"{contract.quantityPerCycle}x {contract.ItemLabel()} within " +
                $"{contract.CadenceDays:F0} days, for {contract.CycleValue} silver.",
                LetterDefOf.NeutralEvent);
        }

        public static bool AcceptOffer(IntercolonyWorldComponent state, RecurringContract contract)
        {
            if (contract == null || !contract.TryAccept())
            {
                return false;
            }

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
