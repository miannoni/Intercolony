using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Self-test for the Stage 5A evaluator and Stage 5B counteroffer state machine. The evaluator
    /// assertions are deliberately behavioural, while the Stage 5B assertions cover agreed terms,
    /// the finite round boundary, refusal retention, persistence, the counteroffer surface, and
    /// the invariant that negotiation never edits a binding order.
    /// </summary>
    public static class IntercolonyNegotiationSelfTest
    {
        private const float OriginalPrice = 100f;
        private const int OriginalQuantity = 100;
        private const int OriginalDeadlineDays = 14;
        private const float TrustedScore = 100f;
        private const float UntrustedScore = 0f;
        private const float NeutralScore = CommercialReputation.StartingScore;
        private const float ModestIncrease = 1.05f;
        private const float ExtremeIncrease = 2.00f;
        private const float AggressiveIncrease = 1.20f;
        private const float UrgentDemandMultiplier = 1.30f;
        private const float GlutDemandMultiplier = 0.70f;
        private const float SyntheticBrandScore = 100f;
        private const float SyntheticBrandEvidence = 100f;
        private const float CounterpartyFriendlyPrice = 0.75f;
        private const float ExplanationTolerance = 0.001f;

        private sealed class Results
        {
            public readonly StringBuilder sb = new StringBuilder();
            public int passed;
            public int failed;
            public int skipped;

            public void Check(bool condition, string label, string detail = null)
            {
                if (condition)
                {
                    passed++;
                    sb.AppendLine($"  PASS  {label}{(detail == null ? "" : $"  ({detail})")}");
                }
                else
                {
                    failed++;
                    sb.AppendLine($"  FAIL  {label}{(detail == null ? "" : $"  ({detail})")}");
                }
            }

            public void Skip(string label, string detail)
            {
                skipped++;
                sb.AppendLine($"  SKIP {label}  ({detail})");
            }
        }

        public static string Run(IntercolonyWorldComponent state)
        {
            Results r = new Results();
            r.sb.AppendLine("Negotiation evaluator/state-machine self-test (Stage 5A/5B Parts One and Two)");

            if (state == null)
            {
                r.sb.AppendLine("  No world state available. Open or load a game first.");
                return Summarize(r);
            }

            ThingDef product = ThingDefOf.Silver;
            IntercolonyProductCategory category =
                IntercolonyProductClassifier.Classify(product) ??
                IntercolonyProductCategory.Commodities;
            SettlementEconomicProfile profile = SelectProfile(
                state, product, category, out string profileSelectionFailure);
            if (profile == null || state.Reputations == null)
            {
                r.Skip(
                    "eleven evaluator/state-machine assertions",
                    profile == null
                        ? profileSelectionFailure
                        : "the loaded world has no reputation dictionary");
                return Summarize(r);
            }

            // Contents, not count. Each fixture replaces the reputation object at one key, and
            // restoring only the old dictionary count could leave a synthetic high/low record
            // attached to the player's real settlement after a future test changes its shape.
            Dictionary<int, CommercialReputation> savedReputations =
                new Dictionary<int, CommercialReputation>(state.Reputations);

            try
            {
                IntercolonyNegotiationProposal modestSale = Proposal(
                    state, profile, product, category,
                    IntercolonyNegotiationDirection.Sale,
                    new IntercolonyNegotiationTerms(
                        OriginalQuantity, OriginalPrice * ModestIncrease,
                        OriginalDeadlineDays, FulfillmentMode.SellerDelivery));

                SetReputation(state, profile, TrustedScore);
                IntercolonyNegotiationResult trusted =
                    IntercolonyNegotiationEvaluator.Evaluate(modestSale);

                SetReputation(state, profile, UntrustedScore);
                IntercolonyNegotiationResult untrusted =
                    IntercolonyNegotiationEvaluator.Evaluate(modestSale);

                r.Check(
                    trusted.AcceptanceScore > untrusted.AcceptanceScore ||
                    (trusted.Decision == IntercolonyNegotiationDecision.Accepted &&
                     untrusted.Decision != IntercolonyNegotiationDecision.Accepted),
                    "G2a higher commercial trust improves a moderate counteroffer",
                    $"high reputation={TrustedScore:F1}: {trusted.Decision}/{trusted.AcceptanceScore:F3}; " +
                    $"low reputation={UntrustedScore:F1}: {untrusted.Decision}/{untrusted.AcceptanceScore:F3}; " +
                    $"price multiple={ModestIncrease:F2}x");

                SetReputation(state, profile, TrustedScore);
                IntercolonyNegotiationProposal extremeSale = Proposal(
                    state, profile, product, category,
                    IntercolonyNegotiationDirection.Sale,
                    new IntercolonyNegotiationTerms(
                        OriginalQuantity, OriginalPrice * ExtremeIncrease,
                        OriginalDeadlineDays, FulfillmentMode.SellerDelivery));
                IntercolonyNegotiationResult extreme =
                    IntercolonyNegotiationEvaluator.Evaluate(extremeSale);

                r.Check(
                    extreme.Decision != IntercolonyNegotiationDecision.Accepted,
                    "G2b an absurd counteroffer is not accepted at maximum reputation",
                    $"absurd multiple={ExtremeIncrease:F2}x; reputation={TrustedScore:F1}; " +
                    $"score={extreme.AcceptanceScore:F3}; decision={extreme.Decision}; " +
                    $"reason={extreme.RefusalReason ?? "none"}");

                SetReputation(state, profile, NeutralScore);
                IntercolonyNegotiationProposal friendlySale = Proposal(
                    state, profile, product, category,
                    IntercolonyNegotiationDirection.Sale,
                    new IntercolonyNegotiationTerms(
                        OriginalQuantity, OriginalPrice * CounterpartyFriendlyPrice,
                        OriginalDeadlineDays, FulfillmentMode.SellerDelivery));
                IntercolonyNegotiationResult friendly =
                    IntercolonyNegotiationEvaluator.Evaluate(friendlySale);

                r.Check(
                    friendly.Decision == IntercolonyNegotiationDecision.Accepted,
                    "a request that improves the counterparty's terms is accepted",
                    $"{friendly.Decision}/{friendly.AcceptanceScore:F3}");

                SetReputation(state, profile, NeutralScore);
                IntercolonyNegotiationResult first =
                    IntercolonyNegotiationEvaluator.Evaluate(modestSale);
                IntercolonyNegotiationResult second =
                    IntercolonyNegotiationEvaluator.Evaluate(modestSale);
                r.Check(
                    SameResult(first, second) &&
                    IntercolonyNegotiationEvaluator.Explain(first) ==
                    IntercolonyNegotiationEvaluator.Explain(second),
                    "identical inputs produce a deterministic result",
                    $"{first.Decision}/{first.AcceptanceScore:F3}");

                SetReputation(state, profile, TrustedScore);
                string explanation = IntercolonyNegotiationEvaluator.Explain(trusted);
                float contributionTotal = 0f;
                foreach (IntercolonyNegotiationFactor factor in trusted.Factors)
                {
                    contributionTotal += factor.contribution;
                }

                bool hasPriceFactor = TryFindFactor(
                    trusted, "Price change", out float priceContribution);
                bool hasReputationFactor = TryFindFactor(
                    trusted, "Commercial reputation", out float reputationContribution);
                bool explanationNamesDrivers =
                    explanation.Contains("Price change") &&
                    explanation.Contains("Commercial reputation") &&
                    explanation.Contains(trusted.Decision.ToString());
                bool contributionMatches =
                    Mathf.Abs(contributionTotal - trusted.AcceptanceScore) <
                    ExplanationTolerance &&
                    hasPriceFactor && hasReputationFactor &&
                    priceContribution < 0f && reputationContribution > 0f;
                string changedExplanation =
                    IntercolonyNegotiationEvaluator.Explain(untrusted);

                r.Check(
                    explanationNamesDrivers &&
                    contributionMatches &&
                    explanation != changedExplanation,
                    "the explanation names dynamic drivers with consistent contributions",
                    $"sum={contributionTotal:F3}, score={trusted.AcceptanceScore:F3}");

                SetReputation(state, profile, NeutralScore);
                IntercolonyNegotiationProposal purchaseProposal = Proposal(
                    state, profile, product, category,
                    IntercolonyNegotiationDirection.Purchase,
                    new IntercolonyNegotiationTerms(
                        OriginalQuantity, OriginalPrice * ModestIncrease,
                        OriginalDeadlineDays, FulfillmentMode.SellerDelivery));
                IntercolonyNegotiationResult saleDirection =
                    IntercolonyNegotiationEvaluator.Evaluate(modestSale);
                IntercolonyNegotiationResult purchaseDirection =
                    IntercolonyNegotiationEvaluator.Evaluate(purchaseProposal);
                bool salePriceFactor = TryFindFactor(
                    saleDirection, "Price change", out float salePriceContribution);
                bool purchasePriceFactor = TryFindFactor(
                    purchaseDirection, "Price change", out float purchasePriceContribution);

                r.Check(
                    salePriceFactor && purchasePriceFactor &&
                    salePriceContribution < 0f && purchasePriceContribution > 0f,
                    "selling and buying directions read the same price change from opposite sides",
                    $"sale={salePriceContribution:F3}, purchase={purchasePriceContribution:F3}");

                ThingDef stage5FGateProduct = SelectStateMachineProduct();
                IntercolonyProductCategory? stage5FGateCategory =
                    IntercolonyProductClassifier.Classify(stage5FGateProduct);
                RunStage5FGateAssertions(
                    state, profile, stage5FGateProduct, stage5FGateCategory, r);

                ThingDef stateMachineProduct = SelectStateMachineProduct();
                IntercolonyProductCategory? stateMachineCategory =
                    IntercolonyProductClassifier.Classify(stateMachineProduct);
                if (!stateMachineCategory.HasValue)
                {
                    r.Skip(
                        "nine Stage 5B state-machine/UI assertions and nine Stage 5C renegotiation assertions",
                        "the loaded world has no definition-driven fungible market product");
                }
                else
                {
                    RunStateMachineAssertions(
                        state, profile, stateMachineProduct, stateMachineCategory.Value, r);

                    RunPostAcceptanceRenegotiationAssertions(
                        state, profile, stateMachineProduct, r);
                }
            }
            catch (System.Exception ex)
            {
                r.failed++;
                r.sb.AppendLine($"  EXCEPTION: {ex}");
            }
            finally
            {
                state.Reputations.Clear();
                foreach (KeyValuePair<int, CommercialReputation> entry in savedReputations)
                {
                    state.Reputations[entry.Key] = entry.Value;
                }

                r.sb.AppendLine(
                    $"        commercial reputation contents restored to " +
                    $"{state.Reputations.Count} record(s).");
            }

            return Summarize(r);
        }

        private static ThingDef SelectStateMachineProduct()
        {
            foreach (ThingDef candidate in IntercolonyProductClassifier.TradableDefs)
            {
                if (candidate != null)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static void RunStage5FGateAssertions(
            IntercolonyWorldComponent state,
            SettlementEconomicProfile profile,
            ThingDef product,
            IntercolonyProductCategory? category,
            Results r)
        {
            if (product == null || !category.HasValue)
            {
                string reason = product == null
                    ? "the definition-driven tradable-product fixture is unavailable"
                    : "the selected tradable product has no product category";
                r.Skip("G3 relevant brand leverage", reason);
                r.Skip("G4 unrelated brand leverage", reason);
                r.Skip("G5 market/event urgency", reason);
                return;
            }

            RunStage5FBrandAssertions(state, profile, product, category.Value, r);
            RunStage5FUrgencyAssertion(state, profile, product, category.Value, r);
            RunStage5FPartTwoAssertions(state, profile, product, category.Value, r);
        }

        private static void RunStage5FPartTwoAssertions(
            IntercolonyWorldComponent state,
            SettlementEconomicProfile profile,
            ThingDef product,
            IntercolonyProductCategory category,
            Results r)
        {
            List<MarketOpportunity> savedOpportunities =
                new List<MarketOpportunity>(state.Opportunities);
            List<SalesOrder> savedOrders = new List<SalesOrder>(state.Orders);
            Dictionary<int, CommercialReputation> savedReputations =
                new Dictionary<int, CommercialReputation>(state.Reputations);
            List<CommercialEventRecord> savedCommercialTimeline =
                new List<CommercialEventRecord>(state.CommercialTimeline);
            int savedTimelineStartTick = state.CommercialTimelineStartTick;

            try
            {
                SetReputation(state, profile, UntrustedScore);

                // Search once for the evaluator's real final-counter band. The returned package
                // is only an input to the fixture; G11 compares the read model to the package
                // consumed by the acceptance path, as observed through the resulting order.
                MarketOpportunity searchOpportunity = TestOpportunity(profile, product, 5101);
                searchOpportunity.fulfillment = FulfillmentMode.BuyerPickup;
                IntercolonyNegotiationTerms counterProposal = FindCounteredTerms(
                    state, profile, product, category, searchOpportunity,
                    out IntercolonyNegotiationResult searchEvaluation,
                    out int searchProposalsTried);
                if (counterProposal == null)
                {
                    string reason =
                        $"no Countered result after {searchProposalsTried} evaluator proposal(s); " +
                        $"last={searchEvaluation?.Decision.ToString() ?? "none"}; " +
                        $"state={searchOpportunity.NegotiationState}";
                    r.Skip("G11a persisted final-counter terms reach the binding order", reason);
                    r.Skip("G11b displayed total payment equals the binding order charge", reason);
                    r.Skip("G12a every non-initial negotiation state refuses another counter", reason);
                    r.Skip("G12b one negotiation records at most one final counter", reason);
                    return;
                }

                MarketOpportunity finalCounterOpportunity =
                    TestOpportunity(profile, product, 5102);
                finalCounterOpportunity.fulfillment = FulfillmentMode.BuyerPickup;
                state.Opportunities.Add(finalCounterOpportunity);
                bool counterProcessed = MarketOpportunityNegotiationService.TryCounter(
                    state, finalCounterOpportunity, counterProposal.Clone(),
                    out IntercolonyNegotiationResult counterEvaluation,
                    out SalesOrder counterOrder,
                    out string counterFailure);
                bool hasStoredFinalCounter = finalCounterOpportunity.TryGetFinalCounterTerms(
                    out IntercolonyNegotiationTerms storedFinalCounter);
                CounterofferAnswerView persistedFinalCounterView =
                    CounterofferUiService.BuildPersistedFinalCounterView(
                        finalCounterOpportunity, storedFinalCounter);
                MarketOpportunityNegotiationState finalCounterStateBeforeAccept =
                    finalCounterOpportunity.NegotiationState;
                SalesOrder acceptedFinalCounterOrder =
                    MarketOpportunityNegotiationService.AcceptFinalCounter(
                        state, finalCounterOpportunity);

                int orderDeadlineDays = acceptedFinalCounterOrder == null
                    ? -1
                    : (acceptedFinalCounterOrder.deadlineTick -
                       acceptedFinalCounterOrder.acceptedTick) / GenDate.TicksPerDay;
                bool orderDeadlineIsWholeDays = acceptedFinalCounterOrder != null &&
                    acceptedFinalCounterOrder.deadlineTick - acceptedFinalCounterOrder.acceptedTick ==
                    orderDeadlineDays * GenDate.TicksPerDay;
                CounterofferComparisonRow priceRow = default(CounterofferComparisonRow);
                CounterofferComparisonRow quantityRow = default(CounterofferComparisonRow);
                CounterofferComparisonRow deadlineRow = default(CounterofferComparisonRow);
                CounterofferComparisonRow fulfillmentRow = default(CounterofferComparisonRow);
                bool rowsFound =
                    TryGetComparisonRow(
                        persistedFinalCounterView?.rows, CounterofferTerm.Price, out priceRow) &&
                    TryGetComparisonRow(
                        persistedFinalCounterView?.rows, CounterofferTerm.Quantity, out quantityRow) &&
                    TryGetComparisonRow(
                        persistedFinalCounterView?.rows, CounterofferTerm.Deadline, out deadlineRow) &&
                    TryGetComparisonRow(
                        persistedFinalCounterView?.rows, CounterofferTerm.Fulfillment,
                        out fulfillmentRow);
                bool viewTermsMatchAcceptedOrder =
                    hasStoredFinalCounter && counterProcessed &&
                    counterEvaluation?.Decision == IntercolonyNegotiationDecision.Countered &&
                    counterOrder == null && acceptedFinalCounterOrder != null &&
                    persistedFinalCounterView?.comparisonTerms != null &&
                    persistedFinalCounterView.comparisonTerms.quantity ==
                        acceptedFinalCounterOrder.Quantity &&
                    Mathf.Approximately(
                        persistedFinalCounterView.comparisonTerms.unitPrice,
                        acceptedFinalCounterOrder.unitPrice) &&
                    persistedFinalCounterView.comparisonTerms.deadlineDays == orderDeadlineDays &&
                    orderDeadlineIsWholeDays &&
                    persistedFinalCounterView.comparisonTerms.fulfillment ==
                        acceptedFinalCounterOrder.fulfillment;
                bool comparisonRowsMatchAcceptedOrder =
                    rowsFound && acceptedFinalCounterOrder != null &&
                    priceRow.proposed == $"{acceptedFinalCounterOrder.unitPrice:F2} silver/unit" &&
                    quantityRow.proposed == $"{acceptedFinalCounterOrder.Quantity} units" &&
                    deadlineRow.proposed == $"{orderDeadlineDays} days" &&
                    fulfillmentRow.proposed ==
                        (acceptedFinalCounterOrder.fulfillment == FulfillmentMode.BuyerPickup
                            ? "Buyer collects"
                            : "You deliver");
                string orderPriceDisplay = acceptedFinalCounterOrder == null
                    ? "none"
                    : $"{acceptedFinalCounterOrder.unitPrice:F2} silver/unit";
                string orderQuantityDisplay = acceptedFinalCounterOrder == null
                    ? "none"
                    : $"{acceptedFinalCounterOrder.Quantity} units";
                string orderDeadlineDisplay = acceptedFinalCounterOrder == null
                    ? "none"
                    : $"{orderDeadlineDays} days";
                string orderFulfillmentDisplay = acceptedFinalCounterOrder == null
                    ? "none"
                    : (acceptedFinalCounterOrder.fulfillment == FulfillmentMode.BuyerPickup
                        ? "Buyer collects"
                        : "You deliver");
                r.Check(
                    viewTermsMatchAcceptedOrder && comparisonRowsMatchAcceptedOrder,
                    "G11a the persisted final-counter view and rows equal accepted order terms",
                    $"stateBefore={finalCounterStateBeforeAccept}; " +
                    $"stateAfter={finalCounterOpportunity.NegotiationState}; " +
                    $"counterProcessed={counterProcessed}; decision={counterEvaluation?.Decision}; " +
                    $"counterFailure={counterFailure ?? "none"}; " +
                    $"viewTerms={DescribeTerms(persistedFinalCounterView?.comparisonTerms)}; " +
                    $"order={DescribeOrder(acceptedFinalCounterOrder)}; " +
                    $"viewQuantity={persistedFinalCounterView?.comparisonTerms?.quantity.ToString() ?? "none"} " +
                    $"vs orderQuantity={acceptedFinalCounterOrder?.Quantity.ToString() ?? "none"}; " +
                    $"viewUnitPrice={persistedFinalCounterView?.comparisonTerms?.unitPrice.ToString("F3") ?? "none"} " +
                    $"vs orderUnitPrice={acceptedFinalCounterOrder?.unitPrice.ToString("F3") ?? "none"}; " +
                    $"viewDeadlineDays={persistedFinalCounterView?.comparisonTerms?.deadlineDays.ToString() ?? "none"} " +
                    $"vs orderDeadlineDays={orderDeadlineDays}; " +
                    $"viewFulfillment={persistedFinalCounterView?.comparisonTerms?.fulfillment.ToString() ?? "none"} " +
                    $"vs orderFulfillment={acceptedFinalCounterOrder?.fulfillment.ToString() ?? "none"}; " +
                    $"rowsFound={rowsFound}; priceRow={priceRow.proposed ?? "none"} " +
                    $"vs orderPriceDisplay={orderPriceDisplay}; " +
                    $"quantityRow={quantityRow.proposed ?? "none"} " +
                    $"vs orderQuantityDisplay={orderQuantityDisplay}; " +
                    $"deadlineRow={deadlineRow.proposed ?? "none"} " +
                    $"vs orderDeadlineDisplay={orderDeadlineDisplay}; " +
                    $"fulfillmentRow={fulfillmentRow.proposed ?? "none"} " +
                    $"vs orderFulfillmentDisplay={orderFulfillmentDisplay}");

                CounterofferComparisonRow totalPaymentRow = default(CounterofferComparisonRow);
                bool totalPaymentRowFound = TryGetComparisonRow(
                    persistedFinalCounterView?.rows, CounterofferTerm.None,
                    out totalPaymentRow);
                bool displayedTotalMatchesOrder =
                    totalPaymentRowFound && acceptedFinalCounterOrder != null &&
                    totalPaymentRow.proposed ==
                        $"{acceptedFinalCounterOrder.TotalPayment:N0} silver";
                string orderTotalDisplay = acceptedFinalCounterOrder == null
                    ? "none"
                    : $"{acceptedFinalCounterOrder.TotalPayment:N0} silver";
                r.Check(
                    displayedTotalMatchesOrder,
                    "G11b the displayed total payment equals the binding order charge",
                    $"stateBefore={finalCounterStateBeforeAccept}; " +
                    $"stateAfter={finalCounterOpportunity.NegotiationState}; " +
                    $"displayedTotal={totalPaymentRow.proposed ?? "none"}; " +
                    $"orderTotalPayment={acceptedFinalCounterOrder?.TotalPayment.ToString() ?? "none"}; " +
                    $"orderTotalDisplay={orderTotalDisplay}; " +
                    $"rowFound={totalPaymentRowFound}; counterFailure={counterFailure ?? "none"}; " +
                    $"order={DescribeOrder(acceptedFinalCounterOrder)}");

                bool statesFixtureAvailable = true;
                List<string> stateDetails = new List<string>();
                int stateFixtureId = 5200;
                foreach (MarketOpportunityNegotiationState requestedState in
                         Enum.GetValues(typeof(MarketOpportunityNegotiationState)))
                {
                    MarketOpportunity stateOpportunity =
                        TestOpportunity(profile, product, stateFixtureId++);
                    stateOpportunity.fulfillment = FulfillmentMode.BuyerPickup;
                    state.Opportunities.Add(stateOpportunity);
                    MarketOpportunityNegotiationState stateBefore =
                        stateOpportunity.NegotiationState;
                    bool statePass;
                    bool responseProcessed = false;
                    string responseFailure = null;
                    IntercolonyNegotiationResult responseEvaluation = null;
                    if (requestedState == MarketOpportunityNegotiationState.None)
                    {
                        responseProcessed = MarketOpportunityNegotiationService.TryCounter(
                            state, stateOpportunity, counterProposal.Clone(),
                            out responseEvaluation, out SalesOrder responseOrder,
                            out responseFailure);
                        statePass = stateBefore == MarketOpportunityNegotiationState.None &&
                            responseProcessed && string.IsNullOrWhiteSpace(responseFailure);
                        stateDetails.Add(
                            $"state={requestedState}; before={stateBefore}; processed={responseProcessed}; " +
                            $"decision={responseEvaluation?.Decision}; failure={responseFailure ?? "none"}");
                    }
                    else if (requestedState ==
                             MarketOpportunityNegotiationState.CounterpartyCountered)
                    {
                        responseProcessed = MarketOpportunityNegotiationService.TryCounter(
                            state, stateOpportunity, counterProposal.Clone(),
                            out responseEvaluation, out SalesOrder responseOrder,
                            out responseFailure);
                        bool furtherProcessed = MarketOpportunityNegotiationService.TryCounter(
                            state, stateOpportunity, counterProposal.Clone(),
                            out IntercolonyNegotiationResult furtherEvaluation,
                            out SalesOrder furtherOrder, out string furtherFailure);
                        statePass = responseProcessed &&
                            responseEvaluation?.Decision ==
                                IntercolonyNegotiationDecision.Countered &&
                            stateOpportunity.NegotiationState == requestedState &&
                            !furtherProcessed && furtherEvaluation == null &&
                            furtherOrder == null && !string.IsNullOrWhiteSpace(furtherFailure);
                        stateDetails.Add(
                            $"state={requestedState}; before={stateBefore}; after={stateOpportunity.NegotiationState}; " +
                            $"processed={responseProcessed}; decision={responseEvaluation?.Decision}; " +
                            $"failure={responseFailure ?? "none"}; furtherProcessed={furtherProcessed}; " +
                            $"furtherFailure={furtherFailure ?? "none"}");
                    }
                    else if (requestedState == MarketOpportunityNegotiationState.CounterpartyRefused)
                    {
                        IntercolonyNegotiationTerms refusalProposal =
                            new IntercolonyNegotiationTerms(
                                OriginalQuantity, OriginalPrice * ExtremeIncrease,
                                OriginalDeadlineDays, FulfillmentMode.BuyerPickup);
                        responseProcessed = MarketOpportunityNegotiationService.TryCounter(
                            state, stateOpportunity, refusalProposal,
                            out responseEvaluation, out SalesOrder responseOrder,
                            out responseFailure);
                        bool furtherProcessed = MarketOpportunityNegotiationService.TryCounter(
                            state, stateOpportunity, counterProposal.Clone(),
                            out IntercolonyNegotiationResult furtherEvaluation,
                            out SalesOrder furtherOrder, out string furtherFailure);
                        statePass = responseProcessed &&
                            responseEvaluation?.Decision ==
                                IntercolonyNegotiationDecision.Refused &&
                            stateOpportunity.NegotiationState == requestedState &&
                            !furtherProcessed && furtherEvaluation == null &&
                            furtherOrder == null && !string.IsNullOrWhiteSpace(furtherFailure);
                        stateDetails.Add(
                            $"state={requestedState}; before={stateBefore}; after={stateOpportunity.NegotiationState}; " +
                            $"processed={responseProcessed}; decision={responseEvaluation?.Decision}; " +
                            $"failure={responseFailure ?? "none"}; furtherProcessed={furtherProcessed}; " +
                            $"furtherFailure={furtherFailure ?? "none"}");
                    }
                    else
                    {
                        statesFixtureAvailable = false;
                        statePass = false;
                        stateDetails.Add(
                            $"state={requestedState}; before={stateBefore}; " +
                            "no fixture transition exists for this enum value");
                    }

                    if (!statePass)
                    {
                        statesFixtureAvailable = false;
                    }
                }

                r.Check(
                    statesFixtureAvailable,
                    "G12a every non-initial negotiation state refuses another counter",
                    $"states enumerated from {typeof(MarketOpportunityNegotiationState).Name}: " +
                    string.Join(" | ", stateDetails));

                MarketOpportunity oneCounterOpportunity =
                    TestOpportunity(profile, product, 5301);
                oneCounterOpportunity.fulfillment = FulfillmentMode.BuyerPickup;
                state.Opportunities.Add(oneCounterOpportunity);
                bool oneCounterProcessed = MarketOpportunityNegotiationService.TryCounter(
                    state, oneCounterOpportunity, counterProposal.Clone(),
                    out IntercolonyNegotiationResult oneCounterEvaluation,
                    out SalesOrder oneCounterOrder,
                    out string oneCounterFailure);
                bool hadStoredCounter = oneCounterOpportunity.TryGetFinalCounterTerms(
                    out IntercolonyNegotiationTerms firstStoredCounter);
                IntercolonyNegotiationTerms attemptedReplacement =
                    new IntercolonyNegotiationTerms(
                        1, 777f, 3, FulfillmentMode.SellerDelivery);
                bool secondRecordAccepted = oneCounterOpportunity.TryRecordFinalCounter(
                    attemptedReplacement);
                bool storedCounterUnchanged = oneCounterOpportunity.TryGetFinalCounterTerms(
                        out IntercolonyNegotiationTerms afterSecondRecord) &&
                    SameTerms(firstStoredCounter, afterSecondRecord);
                bool secondCounterProcessed = MarketOpportunityNegotiationService.TryCounter(
                    state, oneCounterOpportunity, counterProposal.Clone(),
                    out IntercolonyNegotiationResult secondCounterEvaluation,
                    out SalesOrder secondCounterOrder,
                    out string secondCounterFailure);
                int exchangeEvaluatorInvocations = oneCounterEvaluation == null ? 0 : 1;
                if (secondCounterEvaluation != null)
                {
                    exchangeEvaluatorInvocations++;
                }
                bool evaluatorInvocationBounded =
                    exchangeEvaluatorInvocations <= 1 && secondCounterEvaluation == null;
                r.Check(
                    oneCounterProcessed &&
                    oneCounterEvaluation?.Decision == IntercolonyNegotiationDecision.Countered &&
                    oneCounterOrder == null && hadStoredCounter && !secondRecordAccepted &&
                    storedCounterUnchanged && !secondCounterProcessed &&
                    secondCounterEvaluation == null && secondCounterOrder == null &&
                    !string.IsNullOrWhiteSpace(secondCounterFailure) &&
                    evaluatorInvocationBounded,
                    "G12b one negotiation admits at most one final counter",
                    $"state={oneCounterOpportunity.NegotiationState}; firstProcessed={oneCounterProcessed}; " +
                    $"firstDecision={oneCounterEvaluation?.Decision}; firstFailure={oneCounterFailure ?? "none"}; " +
                    $"firstStored={DescribeTerms(firstStoredCounter)}; " +
                    $"replacement={DescribeTerms(attemptedReplacement)}; " +
                    $"secondRecordAccepted={secondRecordAccepted}; " +
                    $"afterSecondRecord={DescribeTerms(afterSecondRecord)}; " +
                    $"storedUnchanged={storedCounterUnchanged}; secondProcessed={secondCounterProcessed}; " +
                    $"secondEvaluation={secondCounterEvaluation?.Decision.ToString() ?? "none"}; " +
                    $"secondOrder={DescribeOrder(secondCounterOrder)}; " +
                    $"secondFailure={secondCounterFailure ?? "none"}; " +
                    $"exchangeEvaluatorInvocations={exchangeEvaluatorInvocations}; " +
                    $"fixtureSearchEvaluatorInvocations={searchProposalsTried}");
            }
            finally
            {
                state.Opportunities.Clear();
                state.Opportunities.AddRange(savedOpportunities);
                state.Orders.Clear();
                state.Orders.AddRange(savedOrders);
                state.CommercialTimeline.Clear();
                state.CommercialTimeline.AddRange(savedCommercialTimeline);
                state.CommercialTimelineStartTick = savedTimelineStartTick;
                state.Reputations.Clear();
                foreach (KeyValuePair<int, CommercialReputation> entry in savedReputations)
                {
                    state.Reputations[entry.Key] = entry.Value;
                }
            }
        }

        private static void RunStage5FBrandAssertions(
            IntercolonyWorldComponent state,
            SettlementEconomicProfile profile,
            ThingDef product,
            IntercolonyProductCategory category,
            Results r)
        {
            if (state.ProductBrandRecords == null)
            {
                r.Skip("G3 relevant brand leverage", "the world has no product-brand record list");
                r.Skip("G4 unrelated brand leverage", "the world has no product-brand record list");
                return;
            }

            IntercolonyProductCategory unrelatedCategory;
            ThingDef unrelatedProduct = FindDifferentCategoryProduct(
                product, category, out unrelatedCategory);
            if (unrelatedProduct == null)
            {
                string reason =
                    $"no tradable product in a different category from {category.Label()}";
                r.Skip("G3 relevant brand leverage", reason);
                r.Skip("G4 unrelated brand leverage", reason);
                return;
            }

            Dictionary<int, CommercialReputation> savedReputations =
                new Dictionary<int, CommercialReputation>(state.Reputations);
            List<ProductBrandRecord> savedBrandRecords =
                new List<ProductBrandRecord>(state.ProductBrandRecords);

            try
            {
                SetReputation(state, profile, NeutralScore);
                IntercolonyNegotiationProposal aggressiveSale = Proposal(
                    state, profile, product, category,
                    IntercolonyNegotiationDirection.Sale,
                    new IntercolonyNegotiationTerms(
                        OriginalQuantity, OriginalPrice * AggressiveIncrease,
                        OriginalDeadlineDays, FulfillmentMode.SellerDelivery));

                state.ProductBrandRecords.Clear();
                IntercolonyNegotiationResult neutral =
                    IntercolonyNegotiationEvaluator.Evaluate(aggressiveSale);

                state.ProductBrandRecords.Add(new ProductBrandRecord(
                    product, SyntheticBrandScore, SyntheticBrandEvidence, OriginalQuantity));
                IntercolonyNegotiationResult relevant =
                    IntercolonyNegotiationEvaluator.Evaluate(aggressiveSale);

                r.Check(
                    relevant.AcceptanceScore > neutral.AcceptanceScore,
                    "G3 strong brand in the sold-goods category improves price leverage",
                    $"multiple={AggressiveIncrease:F2}x; relevant category={category.Label()}, " +
                    $"neutral={neutral.Decision}/{neutral.AcceptanceScore:F3}; " +
                    $"strong relevant={relevant.Decision}/{relevant.AcceptanceScore:F3}; " +
                    $"brand input={product.defName} at {SyntheticBrandScore:F1}/100");

                state.ProductBrandRecords.Clear();
                state.ProductBrandRecords.Add(new ProductBrandRecord(
                    unrelatedProduct, SyntheticBrandScore, SyntheticBrandEvidence,
                    OriginalQuantity));
                IntercolonyNegotiationResult unrelated =
                    IntercolonyNegotiationEvaluator.Evaluate(aggressiveSale);

                float relevantImprovement =
                    relevant.AcceptanceScore - neutral.AcceptanceScore;
                float unrelatedImprovement =
                    unrelated.AcceptanceScore - neutral.AcceptanceScore;
                r.Check(
                    unrelatedImprovement < relevantImprovement,
                    "G4 a strong unrelated-category brand gives almost no comparable leverage",
                    $"multiple={AggressiveIncrease:F2}x; relevant delta={relevantImprovement:F3} " +
                    $"({relevant.Decision}/{relevant.AcceptanceScore:F3} vs " +
                    $"neutral {neutral.Decision}/{neutral.AcceptanceScore:F3}); " +
                    $"unrelated delta={unrelatedImprovement:F3} " +
                    $"({unrelated.Decision}/{unrelated.AcceptanceScore:F3} vs " +
                    $"neutral {neutral.Decision}/{neutral.AcceptanceScore:F3}); " +
                    $"brand moved from {product.defName}/{category.Label()} to " +
                    $"{unrelatedProduct.defName}/{unrelatedCategory.Label()}");
            }
            finally
            {
                state.ProductBrandRecords.Clear();
                state.ProductBrandRecords.AddRange(savedBrandRecords);
                state.Reputations.Clear();
                foreach (KeyValuePair<int, CommercialReputation> entry in savedReputations)
                {
                    state.Reputations[entry.Key] = entry.Value;
                }
            }
        }

        private static void RunStage5FUrgencyAssertion(
            IntercolonyWorldComponent state,
            SettlementEconomicProfile profile,
            ThingDef product,
            IntercolonyProductCategory category,
            Results r)
        {
            if (state.EconomicEvents == null)
            {
                r.Skip("G5 market/event urgency", "the world has no economic-event list");
                return;
            }

            Settlement settlement = IntercolonyMarketAccess.FindSettlement(profile.settlementId);
            if (settlement == null)
            {
                r.Skip(
                    "G5 market/event urgency",
                    $"counterparty settlement {profile.settlementId} cannot be resolved for event scope");
                return;
            }

            Dictionary<int, CommercialReputation> savedReputations =
                new Dictionary<int, CommercialReputation>(state.Reputations);
            List<ProductBrandRecord> savedBrandRecords =
                state.ProductBrandRecords == null
                    ? null
                    : new List<ProductBrandRecord>(state.ProductBrandRecords);
            List<EconomicEvent> savedEvents = new List<EconomicEvent>(state.EconomicEvents);

            try
            {
                SetReputation(state, profile, NeutralScore);
                if (state.ProductBrandRecords != null)
                {
                    state.ProductBrandRecords.Clear();
                }

                IntercolonyNegotiationProposal aggressiveSale = Proposal(
                    state, profile, product, category,
                    IntercolonyNegotiationDirection.Sale,
                    new IntercolonyNegotiationTerms(
                        OriginalQuantity, OriginalPrice * AggressiveIncrease,
                        OriginalDeadlineDays, FulfillmentMode.SellerDelivery));
                int fixtureTick = GenTicks.TicksGame;

                state.EconomicEvents.Clear();
                state.EconomicEvents.Add(Stage5FMarketEvent(
                    category, UrgentDemandMultiplier, fixtureTick));
                IntercolonyNegotiationResult urgent =
                    IntercolonyNegotiationEvaluator.Evaluate(aggressiveSale);
                float urgentEventMultiplier = EconomicEventService.DemandMultiplier(
                    state, settlement, category);

                state.EconomicEvents.Clear();
                state.EconomicEvents.Add(Stage5FMarketEvent(
                    category, GlutDemandMultiplier, fixtureTick));
                IntercolonyNegotiationResult glut =
                    IntercolonyNegotiationEvaluator.Evaluate(aggressiveSale);
                float glutEventMultiplier = EconomicEventService.DemandMultiplier(
                    state, settlement, category);

                r.Check(
                    urgent.AcceptanceScore > glut.AcceptanceScore,
                    "G5 higher market/event urgency improves willingness for an above-market sale",
                    $"multiple={AggressiveIncrease:F2}x; shortage event x{urgentEventMultiplier:F2}: " +
                    $"{urgent.Decision}/{urgent.AcceptanceScore:F3}; glut event x{glutEventMultiplier:F2}: " +
                    $"{glut.Decision}/{glut.AcceptanceScore:F3}; category={category.Label()}");
            }
            finally
            {
                state.EconomicEvents.Clear();
                state.EconomicEvents.AddRange(savedEvents);
                if (state.ProductBrandRecords != null)
                {
                    state.ProductBrandRecords.Clear();
                    if (savedBrandRecords != null)
                    {
                        state.ProductBrandRecords.AddRange(savedBrandRecords);
                    }
                }

                state.Reputations.Clear();
                foreach (KeyValuePair<int, CommercialReputation> entry in savedReputations)
                {
                    state.Reputations[entry.Key] = entry.Value;
                }
            }
        }

        private static ThingDef FindDifferentCategoryProduct(
            ThingDef target,
            IntercolonyProductCategory targetCategory,
            out IntercolonyProductCategory differentCategory)
        {
            foreach (ThingDef candidate in IntercolonyProductClassifier.TradableDefs)
            {
                if (candidate == null || ReferenceEquals(candidate, target))
                {
                    continue;
                }

                IntercolonyProductCategory? candidateCategory =
                    IntercolonyProductClassifier.Classify(candidate);
                if (candidateCategory.HasValue && candidateCategory.Value != targetCategory)
                {
                    differentCategory = candidateCategory.Value;
                    return candidate;
                }
            }

            differentCategory = default(IntercolonyProductCategory);
            return null;
        }

        private static EconomicEvent Stage5FMarketEvent(
            IntercolonyProductCategory category,
            float demandMultiplier,
            int fixtureTick)
        {
            EconomicEvent economicEvent = new EconomicEvent
            {
                id = 905001,
                type = EconomicEventType.Drought,
                startTick = fixtureTick - 1,
                endTick = fixtureTick + 1000,
                anchorSettlementId = EconomicEvent.NoSettlement,
                radiusTiles = EconomicEvent.NoRadius,
                factionLoadId = EconomicEvent.NoFaction
            };
            economicEvent.demandModifier[(int)category] = demandMultiplier;
            return economicEvent;
        }

        private static void RunStateMachineAssertions(
            IntercolonyWorldComponent state,
            SettlementEconomicProfile profile,
            ThingDef product,
            IntercolonyProductCategory category,
            Results r)
        {
            // Contents, not counts. These assertions add opportunities and orders, and replace
            // one reputation record with synthetic trust levels. Restoring only old counts could
            // leave a new order, opportunity or reputation object attached to the player's world
            // if a later assertion takes a different branch.
            List<MarketOpportunity> savedOpportunities =
                new List<MarketOpportunity>(state.Opportunities);
            List<SalesOrder> savedOrders = new List<SalesOrder>(state.Orders);
            Dictionary<int, CommercialReputation> savedReputations =
                new Dictionary<int, CommercialReputation>(state.Reputations);
            // Contents, not count. Timeline pruning removes from the front, so trimming the tail
            // after a fixture would destroy real history and leave synthetic records behind.
            List<CommercialEventRecord> savedCommercialTimeline =
                new List<CommercialEventRecord>(state.CommercialTimeline);
            int savedTimelineStartTick = state.CommercialTimelineStartTick;

            try
            {
                SetReputation(state, profile, NeutralScore);
                MarketOpportunity acceptedOpportunity = TestOpportunity(profile, product, 1);
                state.Opportunities.Add(acceptedOpportunity);
                IntercolonyNegotiationTerms agreedTerms = new IntercolonyNegotiationTerms(
                    OriginalQuantity,
                    OriginalPrice * CounterpartyFriendlyPrice,
                    OriginalDeadlineDays,
                    FulfillmentMode.SellerDelivery);
                bool acceptedCounterProcessed = MarketOpportunityNegotiationService.TryCounter(
                    state, acceptedOpportunity, agreedTerms,
                    out IntercolonyNegotiationResult acceptedEvaluation,
                    out SalesOrder acceptedOrder,
                    out string acceptedFailure);

                bool agreedTermsBound = acceptedOrder != null &&
                    acceptedOrder.Quantity == agreedTerms.quantity &&
                    Mathf.Approximately(acceptedOrder.unitPrice, agreedTerms.unitPrice) &&
                    acceptedOrder.fulfillment == agreedTerms.fulfillment &&
                    acceptedOrder.deadlineTick ==
                    acceptedOrder.acceptedTick + agreedTerms.deadlineDays * GenDate.TicksPerDay &&
                    Mathf.Approximately(acceptedOpportunity.unitPrice, OriginalPrice) &&
                    acceptedOpportunity.quantity == OriginalQuantity &&
                    acceptedOpportunity.deadlineDays == OriginalDeadlineDays;
                r.Check(
                    acceptedCounterProcessed &&
                    acceptedEvaluation?.Decision == IntercolonyNegotiationDecision.Accepted &&
                    agreedTermsBound,
                    "an accepted counter creates an order from the agreed terms",
                    $"decision={acceptedEvaluation?.Decision}, " +
                    $"order={DescribeOrder(acceptedOrder)}, failure={acceptedFailure}");

                CommercialEventRecord acceptedCounterRecord = acceptedOrder == null
                    ? null
                    : FindRecordFor(
                        state, acceptedOrder.id, CommercialEventType.CounterofferAccepted);
                int acceptedCounterRecordCount = acceptedOrder == null
                    ? 0
                    : CountRecordsFor(
                        state, acceptedOrder.id, CommercialEventType.CounterofferAccepted);
                r.Check(
                    acceptedCounterProcessed &&
                    acceptedEvaluation?.Decision == IntercolonyNegotiationDecision.Accepted &&
                    acceptedOrder != null && acceptedCounterRecord != null &&
                    acceptedCounterRecordCount == 1,
                    "T1 an accepted counteroffer records exactly one CounterofferAccepted",
                    $"orderId={acceptedOrder?.id.ToString() ?? "none"}; " +
                    $"{DescribeTimelineRecords(state, acceptedOrder?.id ?? 0)}; " +
                    $"detail='{acceptedCounterRecord?.compactDetail ?? "none"}'");

                SetReputation(state, profile, UntrustedScore);
                MarketOpportunity boundedOpportunity = TestOpportunity(profile, product, 2);
                state.Opportunities.Add(boundedOpportunity);
                IntercolonyNegotiationTerms firstCounter = FindCounteredTerms(
                    state, profile, product, category, boundedOpportunity, out _);
                bool counterActionInitiallyOffered =
                    CounterofferUiService.CounterActionAvailable(boundedOpportunity);
                if (firstCounter == null)
                {
                    // The hard evaluator boundary supplies a deterministic terminal response if
                    // this particular loaded world's context has no ordinary counter band.
                    firstCounter = new IntercolonyNegotiationTerms(
                        OriginalQuantity,
                        OriginalPrice * ExtremeIncrease,
                        OriginalDeadlineDays,
                        FulfillmentMode.SellerDelivery);
                }

                bool firstCounterProcessed = MarketOpportunityNegotiationService.TryCounter(
                    state, boundedOpportunity, firstCounter,
                    out IntercolonyNegotiationResult firstEvaluation,
                    out SalesOrder firstOrder,
                    out string firstFailure);
                bool furtherCounterProcessed = MarketOpportunityNegotiationService.TryCounter(
                    state, boundedOpportunity, agreedTerms,
                    out IntercolonyNegotiationResult furtherEvaluation,
                    out SalesOrder furtherOrder,
                    out string furtherFailure);
                bool terminalResponse = firstEvaluation != null &&
                    (firstEvaluation.Decision == IntercolonyNegotiationDecision.Countered ||
                     firstEvaluation.Decision == IntercolonyNegotiationDecision.Refused) &&
                    firstOrder == null && !furtherCounterProcessed &&
                    furtherEvaluation == null && furtherOrder == null &&
                    boundedOpportunity.NegotiationState !=
                    MarketOpportunityNegotiationState.None;
                r.Check(
                    firstCounterProcessed && terminalResponse,
                    "the counterparty response closes the player counter edge",
                    $"first={firstEvaluation?.Decision}, state={boundedOpportunity.NegotiationState}, " +
                    $"failure={firstFailure ?? furtherFailure}, secondFailure={furtherFailure}");

                r.Check(
                    counterActionInitiallyOffered &&
                    !CounterofferUiService.CounterActionAvailable(boundedOpportunity),
                    "the Market counter action is offered once and then disappears",
                    $"initial={counterActionInitiallyOffered}, " +
                    $"after={CounterofferUiService.CounterActionAvailable(boundedOpportunity)}, " +
                    $"state={boundedOpportunity.NegotiationState}");

                MarketOpportunity supportedModeOpportunity = TestOpportunity(profile, product, 6);
                MarketOpportunity unsupportedModeOpportunity = TestOpportunity(profile, product, 7);
                unsupportedModeOpportunity.thingDef = null;
                CounterofferEditableTerms supportedEditable =
                    CounterofferUiService.EditableTerms(supportedModeOpportunity);
                CounterofferEditableTerms unsupportedEditable =
                    CounterofferUiService.EditableTerms(unsupportedModeOpportunity);
                IntercolonyNegotiationTerms unsupportedProposal =
                    CounterofferUiService.OriginalTerms(unsupportedModeOpportunity);
                unsupportedProposal.fulfillment = FulfillmentMode.BuyerPickup;
                List<CounterofferComparisonRow> unsupportedRows =
                    CounterofferUiService.BuildComparisonRows(
                        unsupportedModeOpportunity, unsupportedProposal, allowEditing: true);
                bool unsupportedModeWasNormalized = false;
                foreach (CounterofferComparisonRow row in unsupportedRows)
                {
                    if (row.term == CounterofferTerm.Fulfillment)
                    {
                        unsupportedModeWasNormalized =
                            row.proposed == row.original && !row.IsEditable;
                        break;
                    }
                }
                r.Check(
                    supportedModeOpportunity.SupportsBothFulfillmentModes &&
                    supportedEditable.fulfillment && !unsupportedEditable.fulfillment &&
                    unsupportedModeWasNormalized,
                    "the fulfilment editor follows the opportunity's two-mode capability",
                    $"supported={supportedModeOpportunity.SupportsBothFulfillmentModes}/" +
                    $"{supportedEditable.fulfillment}, unsupported={unsupportedEditable.fulfillment}");

                SetReputation(state, profile, NeutralScore);
                MarketOpportunity refusedOpportunity = TestOpportunity(profile, product, 3);
                state.Opportunities.Add(refusedOpportunity);
                int orderCountBeforeRefusal = state.Orders.Count;
                IntercolonyNegotiationTerms extremeTerms = new IntercolonyNegotiationTerms(
                    OriginalQuantity,
                    OriginalPrice * ExtremeIncrease,
                    OriginalDeadlineDays,
                    FulfillmentMode.SellerDelivery);
                bool refusalProcessed = MarketOpportunityNegotiationService.TryCounter(
                    state, refusedOpportunity, extremeTerms,
                    out IntercolonyNegotiationResult refusalEvaluation,
                    out SalesOrder refusalOrder,
                    out string refusalFailure);
                bool refusalRetainsOffer = refusalProcessed &&
                    refusalEvaluation?.Decision == IntercolonyNegotiationDecision.Refused &&
                    refusalOrder == null && refusedOpportunity.IsAvailable &&
                    refusedOpportunity.NegotiationState ==
                    MarketOpportunityNegotiationState.CounterpartyRefused &&
                    state.Orders.Count == orderCountBeforeRefusal &&
                    Mathf.Approximately(refusedOpportunity.unitPrice, OriginalPrice) &&
                    refusedOpportunity.quantity == OriginalQuantity &&
                    refusedOpportunity.deadlineDays == OriginalDeadlineDays;
                r.Check(
                    refusalRetainsOffer,
                    "a refused negotiation retains the original opportunity without an order",
                    $"state={refusedOpportunity.NegotiationState}, available={refusedOpportunity.IsAvailable}, " +
                    $"orders={state.Orders.Count - orderCountBeforeRefusal}, failure={refusalFailure}");

                CounterofferAnswerView acceptedAnswer =
                    CounterofferUiService.BuildAnswerView(acceptedOpportunity, acceptedEvaluation);
                CounterofferAnswerView refusedAnswer =
                    CounterofferUiService.BuildAnswerView(refusedOpportunity, refusalEvaluation);
                CounterofferAnswerView finalCounterAnswer = firstEvaluation == null
                    ? null
                    : CounterofferUiService.BuildAnswerView(boundedOpportunity, firstEvaluation);
                bool answerRowsMatchDecisions = acceptedAnswer.answerRow.decision ==
                                                   acceptedEvaluation?.Decision &&
                                               acceptedAnswer.answerRow.value == "Accepted" &&
                                               acceptedAnswer.answerRow.tooltip.Contains("Accepted") &&
                                               SameTerms(
                                                   acceptedEvaluation?.ProposedTerms,
                                                   acceptedAnswer.comparisonTerms) &&
                                               refusedAnswer.answerRow.decision ==
                                                   refusalEvaluation?.Decision &&
                                               refusedAnswer.answerRow.value == "Refused" &&
                                               refusedAnswer.answerRow.tooltip.Contains("Refused") &&
                                               SameTerms(
                                                   refusalEvaluation?.ProposedTerms,
                                                   refusedAnswer.comparisonTerms);
                bool finalCounterAnswerMatches = firstEvaluation != null &&
                    firstEvaluation.Decision == IntercolonyNegotiationDecision.Countered &&
                    firstEvaluation.FinalCounterTerms != null &&
                    finalCounterAnswer.answerRow.value == "Final counter" &&
                    finalCounterAnswer.answerRow.tooltip.Contains("Countered") &&
                    SameTerms(
                        firstEvaluation.FinalCounterTerms,
                        finalCounterAnswer.comparisonTerms);
                if (firstEvaluation?.Decision == IntercolonyNegotiationDecision.Countered)
                {
                    r.Check(
                        answerRowsMatchDecisions && finalCounterAnswerMatches,
                        "the answer row and displayed terms follow the evaluator's actual response",
                        $"accepted={acceptedAnswer.answerRow.value}, refused={refusedAnswer.answerRow.value}, " +
                        $"first={firstEvaluation.Decision}, " +
                        $"finalTerms={DescribeTerms(firstEvaluation.FinalCounterTerms)}");
                }
                else
                {
                    r.Skip(
                        "the answer row and displayed terms follow the evaluator's actual response",
                        $"this loaded world produced {firstEvaluation?.Decision.ToString() ?? "no response"} " +
                        "instead of a final counter for the bounded answer fixture");
                }

                SetReputation(state, profile, UntrustedScore);
                MarketOpportunity persistedOpportunity = TestOpportunity(profile, product, 4);
                // Use a non-default fulfillment mode so this round-trip also detects a missing
                // finalCounterFulfillment Scribe field rather than comparing equal defaults.
                persistedOpportunity.fulfillment = FulfillmentMode.BuyerPickup;
                state.Opportunities.Add(persistedOpportunity);
                IntercolonyNegotiationResult persistedSearchResult;
                IntercolonyNegotiationTerms persistedProposedTerms = FindCounteredTerms(
                    state, profile, product, category, persistedOpportunity,
                    out persistedSearchResult);
                if (persistedProposedTerms == null)
                {
                    r.Skip(
                        "a pending final counter survives a Scribe round trip",
                        $"the evaluator returned " +
                        $"{persistedSearchResult?.Decision.ToString() ?? "no result"} " +
                        "for every bounded proposal in this world; no pending counter existed");
                }
                else
                {
                    IntercolonyNegotiationResult persistedEvaluation = null;
                    SalesOrder persistedOrder = null;
                    string persistedFailure = null;
                    bool persistedCounterProcessed =
                        MarketOpportunityNegotiationService.TryCounter(
                            state, persistedOpportunity, persistedProposedTerms,
                            out persistedEvaluation,
                            out persistedOrder,
                            out persistedFailure);
                    bool processedNonCounter = persistedCounterProcessed &&
                        persistedEvaluation != null &&
                        persistedEvaluation.Decision != IntercolonyNegotiationDecision.Countered;
                    if (processedNonCounter)
                    {
                        r.Skip(
                            "a pending final counter survives a Scribe round trip",
                            $"the evaluator returned {persistedEvaluation.Decision} " +
                            $"instead of Countered; counterProcessed={persistedCounterProcessed}, " +
                            $"orderCreated={persistedOrder != null}, " +
                            $"processingFailure={persistedFailure ?? "none"}");
                    }
                    else
                    {
                        // A candidate was already found by evaluating these exact inputs. An
                        // unprocessed or unknown result stays a failure, not a world skip.
                        List<MarketOpportunity> loadedOpportunities = RoundTripOpportunities(
                            new List<MarketOpportunity> { persistedOpportunity },
                            out string roundTripFailure);
                        MarketOpportunity loadedOpportunity = loadedOpportunities != null &&
                            loadedOpportunities.Count == 1 ? loadedOpportunities[0] : null;
                        IntercolonyNegotiationTerms loadedCounter = null;
                        bool loadedCounterMatches = loadedOpportunity != null &&
                            loadedOpportunity.TryGetFinalCounterTerms(
                                out loadedCounter) &&
                            SameTerms(persistedEvaluation?.FinalCounterTerms, loadedCounter);
                        r.Check(
                            persistedCounterProcessed &&
                            persistedEvaluation?.Decision ==
                            IntercolonyNegotiationDecision.Countered &&
                            persistedOrder == null && loadedCounterMatches &&
                            loadedOpportunity.NegotiationState ==
                            MarketOpportunityNegotiationState.CounterpartyCountered,
                            "a pending final counter survives a Scribe round trip",
                            $"counterProcessed={persistedCounterProcessed}, " +
                            $"decision={persistedEvaluation?.Decision.ToString() ?? "none"}, " +
                            $"orderCreated={persistedOrder != null}, " +
                            $"loadedTermsMatch={loadedCounterMatches}, " +
                            $"state={loadedOpportunity?.NegotiationState}, " +
                            $"expectedFinal={DescribeTerms(persistedEvaluation?.FinalCounterTerms)}, " +
                            $"loadedFinal={DescribeTerms(loadedCounter)}, " +
                            $"roundTripFailure={roundTripFailure ?? "none"}, " +
                            $"processingFailure={persistedFailure ?? "none"}");
                    }
                }

                SetReputation(state, profile, UntrustedScore);
                SalesOrder existingOrder = new SalesOrder
                {
                    id = state.PeekNextId() + 5000,
                    opportunityId = 0,
                    settlementId = profile.settlementId,
                    settlementName = profile.settlementName,
                    line = new OrderLine(product, 7),
                    unitPrice = 321f,
                    acceptedTick = GenTicks.TicksGame,
                    deadlineTick = GenTicks.TicksGame + 17 * GenDate.TicksPerDay,
                    status = SalesOrderStatus.Accepted,
                    fulfillment = FulfillmentMode.SellerDelivery
                };
                state.Orders.Add(existingOrder);
                int existingQuantity = existingOrder.Quantity;
                float existingPrice = existingOrder.unitPrice;
                int existingDeadline = existingOrder.deadlineTick;
                MarketOpportunity bindingBoundaryOpportunity =
                    TestOpportunity(profile, product, 5);
                state.Opportunities.Add(bindingBoundaryOpportunity);
                IntercolonyNegotiationTerms bindingCounter = FindCounteredTerms(
                    state, profile, product, category, bindingBoundaryOpportunity, out _) ??
                    extremeTerms;
                bool bindingCounterProcessed = MarketOpportunityNegotiationService.TryCounter(
                    state, bindingBoundaryOpportunity, bindingCounter,
                    out IntercolonyNegotiationResult bindingEvaluation,
                    out SalesOrder bindingOrder,
                    out string bindingFailure);
                bool existingTermsAfterResponse = bindingCounterProcessed &&
                    existingOrder.status == SalesOrderStatus.Accepted &&
                    existingOrder.Quantity == existingQuantity &&
                    Mathf.Approximately(existingOrder.unitPrice, existingPrice) &&
                    existingOrder.deadlineTick == existingDeadline;
                SalesOrder finalCounterOrder = bindingEvaluation?.Decision ==
                    IntercolonyNegotiationDecision.Countered
                    ? MarketOpportunityNegotiationService.AcceptFinalCounter(
                        state, bindingBoundaryOpportunity)
                    : null;
                bool existingTermsUnchanged = existingTermsAfterResponse &&
                    existingOrder.status == SalesOrderStatus.Accepted &&
                    existingOrder.Quantity == existingQuantity &&
                    Mathf.Approximately(existingOrder.unitPrice, existingPrice) &&
                    existingOrder.deadlineTick == existingDeadline;
                r.Check(
                    existingTermsUnchanged,
                    "negotiation never changes an existing accepted order's terms",
                    $"existing={DescribeOrder(existingOrder)}, " +
                    $"response={bindingEvaluation?.Decision}, " +
                    $"newOrder={DescribeOrder(bindingOrder ?? finalCounterOrder)}, " +
                    $"failure={bindingFailure}");

                SalesOrder acceptedFinalCounterOrder = null;
                if (firstEvaluation?.Decision == IntercolonyNegotiationDecision.Countered)
                {
                    acceptedFinalCounterOrder = MarketOpportunityNegotiationService.AcceptFinalCounter(
                        state, boundedOpportunity);
                }

                if (firstEvaluation?.Decision == IntercolonyNegotiationDecision.Countered)
                {
                    bool finalCounterTermsBound = acceptedFinalCounterOrder != null &&
                        SameOrderTerms(acceptedFinalCounterOrder, firstEvaluation.FinalCounterTerms);
                    r.Check(
                        finalCounterTermsBound,
                        "accepting the final counter creates an order with its agreed terms",
                        $"decision={firstEvaluation.Decision}, " +
                        $"order={DescribeOrder(acceptedFinalCounterOrder)}, " +
                        $"terms={DescribeTerms(firstEvaluation.FinalCounterTerms)}");
                }
                else
                {
                    r.Skip(
                        "accepting the final counter creates an order with its agreed terms",
                        "the bounded answer fixture did not produce a final counter to accept");
                }

                if (firstEvaluation?.Decision == IntercolonyNegotiationDecision.Countered)
                {
                    CommercialEventRecord finalCounterRecord = acceptedFinalCounterOrder == null
                        ? null
                        : FindRecordFor(
                            state,
                            acceptedFinalCounterOrder.id,
                            CommercialEventType.CounterofferAccepted);
                    int finalCounterRecordCount = acceptedFinalCounterOrder == null
                        ? 0
                        : CountRecordsFor(
                            state,
                            acceptedFinalCounterOrder.id,
                            CommercialEventType.CounterofferAccepted);
                    r.Check(
                        acceptedFinalCounterOrder != null && finalCounterRecord != null &&
                        finalCounterRecordCount == 1 && acceptedCounterRecord != null &&
                        finalCounterRecord.compactDetail != acceptedCounterRecord.compactDetail,
                        "T2 accepting a persisted final counter records its distinct CounterofferAccepted",
                        $"T1 detail='{acceptedCounterRecord?.compactDetail ?? "none"}'; " +
                        $"T2 orderId={acceptedFinalCounterOrder?.id.ToString() ?? "none"}; " +
                        $"{DescribeTimelineRecords(state, acceptedFinalCounterOrder?.id ?? 0)}; " +
                        $"T2 detail='{finalCounterRecord?.compactDetail ?? "none"}'");
                }
                else
                {
                    r.Skip(
                        "T2 accepting a persisted final counter records its distinct CounterofferAccepted",
                        "the bounded answer fixture did not produce a final counter to accept");
                }

                int timelineBeforeNoWritePaths = state.CommercialTimeline.Count;
                SetReputation(state, profile, UntrustedScore);
                MarketOpportunity t6RefusedOpportunity = TestOpportunity(profile, product, 8);
                state.Opportunities.Add(t6RefusedOpportunity);
                bool t6RefusalProcessed = MarketOpportunityNegotiationService.TryCounter(
                    state,
                    t6RefusedOpportunity,
                    extremeTerms,
                    out IntercolonyNegotiationResult t6RefusalEvaluation,
                    out SalesOrder t6RefusalOrder,
                    out string t6RefusalFailure);

                MarketOpportunity t6DeclinedOpportunity = TestOpportunity(profile, product, 9);
                state.Opportunities.Add(t6DeclinedOpportunity);
                IntercolonyNegotiationTerms t6CounterTerms = FindCounteredTerms(
                    state,
                    profile,
                    product,
                    category,
                    t6DeclinedOpportunity,
                    out IntercolonyNegotiationResult t6SearchEvaluation,
                    out int t6CounterProposalsTried);
                bool t6CounterProcessed = false;
                IntercolonyNegotiationResult t6CounterEvaluation = null;
                SalesOrder t6CounterOrder = null;
                string t6CounterFailure = null;
                bool t6DeclineProcessed = false;
                if (t6CounterTerms != null)
                {
                    t6CounterProcessed = MarketOpportunityNegotiationService.TryCounter(
                        state,
                        t6DeclinedOpportunity,
                        t6CounterTerms,
                        out t6CounterEvaluation,
                        out t6CounterOrder,
                        out t6CounterFailure);
                    if (t6CounterEvaluation?.Decision ==
                        IntercolonyNegotiationDecision.Countered)
                    {
                        t6DeclineProcessed = MarketOpportunityNegotiationService.TryDecline(
                            state, t6DeclinedOpportunity);
                    }
                }

                int timelineAfterNoWritePaths = state.CommercialTimeline.Count;
                if (t6CounterTerms == null)
                {
                    r.Skip(
                        "T6 refusal and declined final counter write nothing",
                        $"no Countered result after {t6CounterProposalsTried} proposal(s); " +
                        $"refusal={t6RefusalEvaluation?.Decision.ToString() ?? "none"}; " +
                        $"{DescribeTimelineTotals(state)}");
                }
                else
                {
                    r.Check(
                        t6RefusalProcessed &&
                        t6RefusalEvaluation?.Decision == IntercolonyNegotiationDecision.Refused &&
                        t6RefusalOrder == null &&
                        t6CounterProcessed &&
                        t6CounterEvaluation?.Decision ==
                            IntercolonyNegotiationDecision.Countered &&
                        t6CounterOrder == null && t6DeclineProcessed &&
                        timelineBeforeNoWritePaths == timelineAfterNoWritePaths,
                        "T6 a refused or declined negotiation writes nothing",
                        $"timeline before={timelineBeforeNoWritePaths} after={timelineAfterNoWritePaths}; " +
                        $"refused={t6RefusalEvaluation?.Decision.ToString() ?? "none"}; " +
                        $"declined={t6CounterEvaluation?.Decision.ToString() ?? "none"}; " +
                        $"counterTried={t6CounterProposalsTried}; " +
                        $"refusalFailure={t6RefusalFailure ?? "none"}; " +
                        $"counterFailure={t6CounterFailure ?? "none"}; " +
                        $"{DescribeTimelineTotals(state)}");
                }
            }
            catch (Exception ex)
            {
                r.failed++;
                r.sb.AppendLine($"  EXCEPTION in Stage 5B assertions: {ex}");
            }
            finally
            {
                state.Opportunities.Clear();
                state.Opportunities.AddRange(savedOpportunities);
                state.Orders.Clear();
                state.Orders.AddRange(savedOrders);
                state.CommercialTimeline.Clear();
                state.CommercialTimeline.AddRange(savedCommercialTimeline);
                state.CommercialTimelineStartTick = savedTimelineStartTick;
                state.Reputations.Clear();
                foreach (KeyValuePair<int, CommercialReputation> entry in savedReputations)
                {
                    state.Reputations[entry.Key] = entry.Value;
                }

                r.sb.AppendLine(
                    $"        Stage 5B world contents restored: " +
                    $"{state.Opportunities.Count} opportunit{(state.Opportunities.Count == 1 ? "y" : "ies")}, " +
                    $"{state.Orders.Count} sales order(s), {state.Reputations.Count} reputation record(s), " +
                    $"{state.CommercialTimeline.Count} timeline record(s).");
            }
        }

        private static void RunPostAcceptanceRenegotiationAssertions(
            IntercolonyWorldComponent state,
            SettlementEconomicProfile profile,
            ThingDef product,
            Results r)
        {
            List<SalesOrder> savedOrders = new List<SalesOrder>(state.Orders);
            Dictionary<int, CommercialReputation> savedReputations =
                new Dictionary<int, CommercialReputation>(state.Reputations);
            // Contents, not count. Prune removes oldest records from the front, so tail trimming
            // would discard real history and retain synthetic negotiation records.
            List<CommercialEventRecord> savedCommercialTimeline =
                new List<CommercialEventRecord>(state.CommercialTimeline);
            int savedTimelineStartTick = state.CommercialTimelineStartTick;

            try
            {
                SetReputation(state, profile, TrustedScore);

                SalesOrder deadlineOrder = RenegotiationOrder(
                    state, profile, product, 101, 100, 100f, 14,
                    FulfillmentMode.SellerDelivery);
                state.Orders.Add(deadlineOrder);
                int deadlineBefore = deadlineOrder.deadlineTick;
                int quantityBefore = deadlineOrder.Quantity;
                float priceBefore = deadlineOrder.unitPrice;
                SalesOrderStatus statusBefore = deadlineOrder.status;
                FulfillmentMode fulfillmentBefore = deadlineOrder.fulfillment;
                const int extensionDays = 3;
                bool deadlineProcessed = PostAcceptanceRenegotiationService.TryRequest(
                    state, deadlineOrder,
                    RenegotiationRequest.DeadlineExtension(extensionDays),
                    out IntercolonyNegotiationResult deadlineEvaluation,
                    out string deadlineFailure);
                int expectedDeadline = deadlineBefore + extensionDays * GenDate.TicksPerDay;
                r.Check(
                    deadlineProcessed &&
                    deadlineEvaluation?.Decision == IntercolonyNegotiationDecision.Accepted &&
                    deadlineOrder.deadlineTick == expectedDeadline &&
                    deadlineOrder.Quantity == quantityBefore &&
                    Mathf.Approximately(deadlineOrder.unitPrice, priceBefore) &&
                    deadlineOrder.status == statusBefore &&
                    deadlineOrder.fulfillment == fulfillmentBefore,
                    "A1 deadline extension moves exactly the deadline",
                    $"deadlineTick expected={expectedDeadline} actual={deadlineOrder.deadlineTick}; " +
                    $"quantity expected={quantityBefore} actual={deadlineOrder.Quantity}; " +
                    $"unitPrice expected={priceBefore:F3} actual={deadlineOrder.unitPrice:F3}; " +
                    $"status expected={statusBefore} actual={deadlineOrder.status}; " +
                    $"fulfillment expected={fulfillmentBefore} actual={deadlineOrder.fulfillment}; " +
                    $"decision={deadlineEvaluation?.Decision}, failure={deadlineFailure ?? "none"}");

                SalesOrder quantityOrder = RenegotiationOrder(
                    state, profile, product, 102, 100, 137.5f, 14,
                    FulfillmentMode.SellerDelivery);
                state.Orders.Add(quantityOrder);
                const int requestedQuantity = 60;
                float quantityPriceBefore = quantityOrder.unitPrice;
                int expectedTotal = IntercolonyPricing.TotalPayment(
                    quantityPriceBefore, requestedQuantity);
                bool quantityProcessed = PostAcceptanceRenegotiationService.TryRequest(
                    state, quantityOrder,
                    RenegotiationRequest.QuantityReduction(requestedQuantity),
                    out IntercolonyNegotiationResult quantityEvaluation,
                    out string quantityFailure);
                r.Check(
                    quantityProcessed &&
                    quantityEvaluation?.Decision == IntercolonyNegotiationDecision.Accepted &&
                    quantityOrder.Quantity == requestedQuantity &&
                    Mathf.Approximately(quantityOrder.unitPrice, quantityPriceBefore) &&
                    quantityOrder.TotalPayment == expectedTotal,
                    "A2 quantity reduction lowers the bound quantity and payment",
                    $"quantity expected={requestedQuantity} actual={quantityOrder.Quantity}; " +
                    $"unitPrice expected={quantityPriceBefore:F3} actual={quantityOrder.unitPrice:F3}; " +
                    $"TotalPayment expected={expectedTotal} actual={quantityOrder.TotalPayment}; " +
                    $"decision={quantityEvaluation?.Decision}, failure={quantityFailure ?? "none"}");

                SalesOrder cancellationOrder = null;
                CommercialReputation cancellationReputation = null;
                IntercolonyNegotiationResult cancellationEvaluation = null;
                string cancellationFailure = null;
                bool cancellationProcessed = false;
                bool cancellationHadReputation = false;
                CommercialReputation cancellationSavedReputation = null;
                float cancellationReputationBefore = CommercialReputation.StartingScore;
                int cancellationFailedDealsBefore = 0;
                int cancellationFailedRecordsBefore = 0;
                int candidatesTried = 0;
                int acceptedCandidates = 0;
                IntercolonyNegotiationResult lastCancellationEvaluation = null;
                string lastCancellationFailure = null;

                List<Settlement> settlements = Find.WorldObjects?.Settlements;
                if (settlements != null)
                {
                    foreach (Settlement settlement in settlements)
                    {
                        // Keep this search aligned with SelectProfile: eligibility is the
                        // structural gate, while IsAccessible also applies the shared hostility
                        // policy before a settlement can be used as a counterparty.
                        if (settlement == null || !SettlementProfileGenerator.IsEligible(settlement) ||
                            !IntercolonyMarketAccess.IsAccessible(settlement))
                        {
                            continue;
                        }

                        SettlementEconomicProfile candidateProfile = state.GetProfile(settlement);
                        if (candidateProfile == null)
                        {
                            continue;
                        }

                        candidatesTried++;
                        bool candidateHadReputation = state.Reputations.TryGetValue(
                            candidateProfile.settlementId,
                            out CommercialReputation candidateSavedReputation);
                        SalesOrder candidateOrder = null;
                        bool candidateAccepted = false;
                        try
                        {
                            SetReputation(state, candidateProfile, TrustedScore);
                            candidateOrder = RenegotiationOrder(
                                state, candidateProfile, product, 103 + candidatesTried,
                                100, 100f, 14, FulfillmentMode.SellerDelivery,
                                SalesOrderStatus.Accepted);
                            state.Orders.Add(candidateOrder);
                            CommercialReputation candidateReputation = state.FindReputation(
                                candidateProfile.settlementId);
                            float candidateReputationBefore = candidateReputation?.Score ??
                                CommercialReputation.StartingScore;
                            int candidateFailedDealsBefore = candidateReputation?.ordersFailed ?? 0;
                            int candidateFailedRecordsBefore = CountSaleFailedRecords(
                                state, candidateOrder.id);
                            bool candidateProcessed =
                                PostAcceptanceRenegotiationService.TryRequest(
                                    state, candidateOrder,
                                    RenegotiationRequest.MutualCancellation(),
                                    out IntercolonyNegotiationResult candidateEvaluation,
                                    out string candidateFailure);
                            lastCancellationEvaluation = candidateEvaluation;
                            lastCancellationFailure = candidateFailure;
                            candidateAccepted = candidateEvaluation?.Decision ==
                                IntercolonyNegotiationDecision.Accepted;
                            if (candidateAccepted)
                            {
                                cancellationOrder = candidateOrder;
                                cancellationReputation = state.FindReputation(
                                    candidateProfile.settlementId);
                                cancellationEvaluation = candidateEvaluation;
                                cancellationFailure = candidateFailure;
                                cancellationProcessed = candidateProcessed;
                                cancellationHadReputation = candidateHadReputation;
                                cancellationSavedReputation = candidateSavedReputation;
                                cancellationReputationBefore = candidateReputationBefore;
                                cancellationFailedDealsBefore = candidateFailedDealsBefore;
                                cancellationFailedRecordsBefore = candidateFailedRecordsBefore;
                            }
                        }
                        finally
                        {
                            if (!candidateAccepted)
                            {
                                if (candidateOrder != null)
                                {
                                    state.Orders.Remove(candidateOrder);
                                }

                                if (candidateHadReputation)
                                {
                                    state.Reputations[candidateProfile.settlementId] =
                                        candidateSavedReputation;
                                }
                                else
                                {
                                    state.Reputations.Remove(candidateProfile.settlementId);
                                }
                            }
                        }

                        if (candidateAccepted)
                        {
                            acceptedCandidates++;
                            break;
                        }
                    }
                }

                string cancellationExplanation =
                    IntercolonyNegotiationEvaluator.Explain(lastCancellationEvaluation);
                string cancellationSearchDetail =
                    $"candidates tried={candidatesTried}; accepted={acceptedCandidates}; " +
                    $"last failure={lastCancellationFailure ?? "none"}; " +
                    $"last evaluation:\n{cancellationExplanation}";
                if (cancellationOrder == null)
                {
                    r.Skip(
                        "A3 mutual cancellation ends the order without a breach",
                        $"no accepting counterparty after {candidatesTried} candidate(s); " +
                        cancellationSearchDetail);
                }
                else
                {
                    float reputationBefore = cancellationReputationBefore;
                    int failedDealsBefore = cancellationFailedDealsBefore;
                    int failedRecordsBefore = cancellationFailedRecordsBefore;
                    float reputationAfter = cancellationReputation?.Score ??
                        CommercialReputation.StartingScore;
                    int failedDealsAfter = cancellationReputation?.ordersFailed ?? 0;
                    int failedRecordsAfter = CountSaleFailedRecords(
                        state, cancellationOrder.id);
                    r.Check(
                        cancellationProcessed &&
                        cancellationEvaluation?.Decision == IntercolonyNegotiationDecision.Accepted &&
                        cancellationOrder.status == SalesOrderStatus.Cancelled &&
                        Mathf.Approximately(reputationAfter, reputationBefore) &&
                        failedDealsAfter == failedDealsBefore &&
                        failedRecordsAfter == failedRecordsBefore,
                        "A3 mutual cancellation ends the order without a breach",
                        $"status expected={SalesOrderStatus.Cancelled} actual={cancellationOrder.status}; " +
                        $"reputation before={reputationBefore:F3} after={reputationAfter:F3}; " +
                        $"ordersFailed before={failedDealsBefore} after={failedDealsAfter}; " +
                        $"SaleFailedRecords before={failedRecordsBefore} after={failedRecordsAfter}; " +
                        $"decision={cancellationEvaluation?.Decision}, failure={cancellationFailure ?? "none"}; " +
                        $"last evaluation:\n{cancellationExplanation}");

                    int agreementCancellationRecordCount = CountRecordsFor(
                        state,
                        cancellationOrder.id,
                        CommercialEventType.SaleCancelledByAgreement);
                    int playerCancellationRecordCount = CountRecordsFor(
                        state, cancellationOrder.id, CommercialEventType.SaleCancelled);
                    r.Check(
                        cancellationProcessed &&
                        cancellationEvaluation?.Decision ==
                            IntercolonyNegotiationDecision.Accepted &&
                        cancellationOrder.status == SalesOrderStatus.Cancelled &&
                        agreementCancellationRecordCount == 1 &&
                        playerCancellationRecordCount == 0,
                        "T5 mutual cancellation records SaleCancelledByAgreement, never SaleCancelled",
                        $"orderId={cancellationOrder.id}; " +
                        $"{DescribeTimelineRecords(state, cancellationOrder.id)}; " +
                        $"decision={cancellationEvaluation?.Decision}; " +
                        $"failure={cancellationFailure ?? "none"}");
                }

                if (cancellationOrder == null)
                {
                    r.Skip(
                        "T5 mutual cancellation records SaleCancelledByAgreement, never SaleCancelled",
                        $"no accepting counterparty after {candidatesTried} candidate(s); " +
                        $"SaleCancelledByAgreement=0, SaleCancelled=0; " +
                        cancellationSearchDetail);
                }

                r.Check(
                    cancellationOrder != null && acceptedCandidates > 0 &&
                    cancellationEvaluation?.Decision == IntercolonyNegotiationDecision.Accepted,
                    "A3b a mutual cancellation is reachable at all",
                    cancellationSearchDetail);

                if (cancellationOrder != null)
                {
                    state.Orders.Remove(cancellationOrder);
                    if (cancellationHadReputation)
                    {
                        state.Reputations[cancellationOrder.settlementId] =
                            cancellationSavedReputation;
                    }
                    else
                    {
                        state.Reputations.Remove(cancellationOrder.settlementId);
                    }
                }

                SetReputation(state, profile, UntrustedScore);
                SalesOrder refusedOrder = RenegotiationOrder(
                    state, profile, product, 104, 100, 100f, 14,
                    FulfillmentMode.BuyerPickup);
                state.Orders.Add(refusedOrder);
                int refusedDeadlineBefore = refusedOrder.deadlineTick;
                int refusedQuantityBefore = refusedOrder.Quantity;
                float refusedPriceBefore = refusedOrder.unitPrice;
                SalesOrderStatus refusedStatusBefore = refusedOrder.status;
                FulfillmentMode refusedFulfillmentBefore = refusedOrder.fulfillment;
                int refusedDeliveredBefore = refusedOrder.deliveredQuantity;
                bool refusedProcessed = PostAcceptanceRenegotiationService.TryRequest(
                    state, refusedOrder,
                    RenegotiationRequest.QuantityReduction(1),
                    out IntercolonyNegotiationResult refusedEvaluation,
                    out string refusedFailure);
                r.Check(
                    refusedProcessed &&
                    refusedEvaluation?.Decision == IntercolonyNegotiationDecision.Refused &&
                    refusedOrder.deadlineTick == refusedDeadlineBefore &&
                    refusedOrder.Quantity == refusedQuantityBefore &&
                    Mathf.Approximately(refusedOrder.unitPrice, refusedPriceBefore) &&
                    refusedOrder.status == refusedStatusBefore &&
                    refusedOrder.fulfillment == refusedFulfillmentBefore &&
                    refusedOrder.deliveredQuantity == refusedDeliveredBefore,
                    "A4 a refused request leaves the order unchanged",
                    $"deadlineTick before={refusedDeadlineBefore} after={refusedOrder.deadlineTick}; " +
                    $"quantity before={refusedQuantityBefore} after={refusedOrder.Quantity}; " +
                    $"unitPrice before={refusedPriceBefore:F3} after={refusedOrder.unitPrice:F3}; " +
                    $"status before={refusedStatusBefore} after={refusedOrder.status}; " +
                    $"fulfillment before={refusedFulfillmentBefore} after={refusedOrder.fulfillment}; " +
                    $"deliveredQuantity before={refusedDeliveredBefore} after={refusedOrder.deliveredQuantity}; " +
                    $"decision={refusedEvaluation?.Decision}, failure={refusedFailure ?? "none"}");

                bool refusedDeadlineProcessed = PostAcceptanceRenegotiationService.TryRequest(
                    state,
                    refusedOrder,
                    RenegotiationRequest.DeadlineExtension(
                        PostAcceptanceRenegotiationService.MaxExtensionDays),
                    out IntercolonyNegotiationResult refusedDeadlineEvaluation,
                    out string refusedDeadlineFailure);
                int acceptedDeadlineRecordCount = CountRecordsFor(
                    state, deadlineOrder.id, CommercialEventType.DeadlineExtended);
                int refusedDeadlineRecordCount = CountRecordsFor(
                    state, refusedOrder.id, CommercialEventType.DeadlineExtended);
                r.Check(
                    deadlineProcessed &&
                    deadlineEvaluation?.Decision == IntercolonyNegotiationDecision.Accepted &&
                    acceptedDeadlineRecordCount == 1 &&
                    refusedDeadlineProcessed &&
                    refusedDeadlineEvaluation?.Decision ==
                        IntercolonyNegotiationDecision.Refused &&
                    refusedDeadlineRecordCount == 0,
                    "T3 an accepted deadline extension records once and a refused one records zero",
                    $"accepted orderId={deadlineOrder.id}; " +
                    $"{DescribeTimelineRecords(state, deadlineOrder.id)}; " +
                    $"refused orderId={refusedOrder.id}; " +
                    $"{DescribeTimelineRecords(state, refusedOrder.id)}; " +
                    $"acceptedDecision={deadlineEvaluation?.Decision}; " +
                    $"refusedDecision={refusedDeadlineEvaluation?.Decision}; " +
                    $"acceptedFailure={deadlineFailure ?? "none"}; " +
                    $"refusedFailure={refusedDeadlineFailure ?? "none"}");

                int acceptedQuantityRecordCount = CountRecordsFor(
                    state, quantityOrder.id, CommercialEventType.QuantityReduced);
                int refusedQuantityRecordCount = CountRecordsFor(
                    state, refusedOrder.id, CommercialEventType.QuantityReduced);
                r.Check(
                    quantityProcessed &&
                    quantityEvaluation?.Decision == IntercolonyNegotiationDecision.Accepted &&
                    acceptedQuantityRecordCount == 1 &&
                    refusedProcessed &&
                    refusedEvaluation?.Decision == IntercolonyNegotiationDecision.Refused &&
                    refusedQuantityRecordCount == 0,
                    "T4 an accepted quantity reduction records once and a refusal records zero",
                    $"accepted orderId={quantityOrder.id}; " +
                    $"{DescribeTimelineRecords(state, quantityOrder.id)}; " +
                    $"refused orderId={refusedOrder.id}; " +
                    $"{DescribeTimelineRecords(state, refusedOrder.id)}; " +
                    $"acceptedDecision={quantityEvaluation?.Decision}; " +
                    $"refusedDecision={refusedEvaluation?.Decision}; " +
                    $"acceptedFailure={quantityFailure ?? "none"}; " +
                    $"refusedFailure={refusedFailure ?? "none"}");

                RunPostAcceptanceRenegotiationAssertionsPartTwo(
                    state, profile, product, r);
            }
            catch (Exception ex)
            {
                r.failed++;
                r.sb.AppendLine($"  EXCEPTION in Stage 5C assertions: {ex}");
            }
            finally
            {
                state.Orders.Clear();
                state.Orders.AddRange(savedOrders);
                state.CommercialTimeline.Clear();
                state.CommercialTimeline.AddRange(savedCommercialTimeline);
                state.CommercialTimelineStartTick = savedTimelineStartTick;
                state.Reputations.Clear();
                foreach (KeyValuePair<int, CommercialReputation> entry in savedReputations)
                {
                    state.Reputations[entry.Key] = entry.Value;
                }

                r.sb.AppendLine(
                    $"        Stage 5C world contents restored: " +
                    $"{state.Orders.Count} sales order(s), " +
                    $"{state.Reputations.Count} reputation record(s), " +
                    $"{state.CommercialTimeline.Count} timeline record(s).");
            }
        }

        private static void RunPostAcceptanceRenegotiationAssertionsPartTwo(
            IntercolonyWorldComponent state,
            SettlementEconomicProfile profile,
            ThingDef product,
            Results r)
        {
            SetReputation(state, profile, UntrustedScore);
            SalesOrder oneAttemptOrder = RenegotiationOrder(
                state, profile, product, 105, 100, 100f, 14,
                FulfillmentMode.SellerDelivery);
            state.Orders.Add(oneAttemptOrder);
            bool firstAttempt = PostAcceptanceRenegotiationService.TryRequest(
                state, oneAttemptOrder,
                RenegotiationRequest.QuantityReduction(1),
                out IntercolonyNegotiationResult firstAttemptEvaluation,
                out string firstAttemptFailure);
            bool secondAttempt = PostAcceptanceRenegotiationService.TryRequest(
                state, oneAttemptOrder,
                RenegotiationRequest.QuantityReduction(1),
                out IntercolonyNegotiationResult secondAttemptEvaluation,
                out string secondAttemptFailure);
            bool sameKindAvailableAfter = oneAttemptOrder.CanRequest(
                RenegotiationRequestKind.QuantityReduction);
            bool differentKindAvailableAfter = oneAttemptOrder.CanRequest(
                RenegotiationRequestKind.DeadlineExtension);
            r.Check(
                firstAttempt &&
                firstAttemptEvaluation?.Decision == IntercolonyNegotiationDecision.Refused &&
                !secondAttempt && !sameKindAvailableAfter && differentKindAvailableAfter,
                "A5 one attempt per kind and refusal cannot be re-rolled",
                $"firstTry={firstAttempt}, firstDecision={firstAttemptEvaluation?.Decision}; " +
                $"secondTry={secondAttempt}, secondDecision={secondAttemptEvaluation?.Decision}; " +
                $"sameKindCanRequest={sameKindAvailableAfter}; " +
                $"differentKindCanRequest={differentKindAvailableAfter}; " +
                $"firstFailure={firstAttemptFailure ?? "none"}, " +
                $"secondFailure={secondAttemptFailure ?? "none"}");

            SalesOrder invalidOrder = RenegotiationOrder(
                state, profile, product, 106, 100, 100f, 14,
                FulfillmentMode.SellerDelivery);
            state.Orders.Add(invalidOrder);
            bool invalidCanRequestBefore = invalidOrder.CanRequest(
                RenegotiationRequestKind.DeadlineExtension);
            bool invalidProcessed = PostAcceptanceRenegotiationService.TryRequest(
                state, invalidOrder,
                RenegotiationRequest.DeadlineExtension(0),
                out IntercolonyNegotiationResult invalidEvaluation,
                out string invalidFailure);
            bool invalidCanRequestAfter = invalidOrder.CanRequest(
                RenegotiationRequestKind.DeadlineExtension);
            r.Check(
                invalidCanRequestBefore && !invalidProcessed && invalidCanRequestAfter,
                "A6 a structurally invalid request does not consume the attempt",
                $"TryRequest={invalidProcessed}; " +
                $"CanRequest before={invalidCanRequestBefore} after={invalidCanRequestAfter}; " +
                $"evaluation={invalidEvaluation?.Decision.ToString() ?? "none"}; " +
                $"failure={invalidFailure ?? "none"}");

            SalesOrder awaitingCollectionOrder = RenegotiationOrder(
                state, profile, product, 107, 100, 100f, 14,
                FulfillmentMode.BuyerPickup, SalesOrderStatus.AwaitingCollection);
            SalesOrder completedOrder = RenegotiationOrder(
                state, profile, product, 108, 100, 100f, 14,
                FulfillmentMode.SellerDelivery, SalesOrderStatus.Completed);
            state.Orders.Add(awaitingCollectionOrder);
            state.Orders.Add(completedOrder);
            bool awaitingTry = PostAcceptanceRenegotiationService.TryRequest(
                state, awaitingCollectionOrder,
                RenegotiationRequest.DeadlineExtension(1),
                out IntercolonyNegotiationResult awaitingEvaluation,
                out string awaitingFailure);
            bool completedTry = PostAcceptanceRenegotiationService.TryRequest(
                state, completedOrder,
                RenegotiationRequest.DeadlineExtension(1),
                out IntercolonyNegotiationResult completedEvaluation,
                out string completedFailure);
            const string expectedStatusGateFailure =
                "only an accepted sales order can be renegotiated";
            r.Check(
                !awaitingTry && awaitingFailure == expectedStatusGateFailure &&
                !completedTry && completedFailure == expectedStatusGateFailure,
                "A7 only an Accepted order is renegotiable",
                $"AwaitingCollection TryRequest={awaitingTry}, evaluation=" +
                $"{awaitingEvaluation?.Decision.ToString() ?? "none"}, " +
                $"failure expected={expectedStatusGateFailure}, " +
                $"actual={awaitingFailure ?? "none"}; " +
                $"Completed TryRequest={completedTry}, evaluation=" +
                $"{completedEvaluation?.Decision.ToString() ?? "none"}, " +
                $"failure expected={expectedStatusGateFailure}, " +
                $"actual={completedFailure ?? "none"}");

            SetReputation(state, profile, TrustedScore);
            SalesOrder persistenceOrder = RenegotiationOrder(
                state, profile, product, 109, 100, 100f, 14,
                FulfillmentMode.SellerDelivery);
            state.Orders.Add(persistenceOrder);
            const int persistedExtensionDays = 2;
            const int persistedQuantity = 70;
            bool persistedExtension = PostAcceptanceRenegotiationService.TryRequest(
                state, persistenceOrder,
                RenegotiationRequest.DeadlineExtension(persistedExtensionDays),
                out IntercolonyNegotiationResult persistedExtensionEvaluation,
                out string persistedExtensionFailure);
            bool persistedReduction = PostAcceptanceRenegotiationService.TryRequest(
                state, persistenceOrder,
                RenegotiationRequest.QuantityReduction(persistedQuantity),
                out IntercolonyNegotiationResult persistedReductionEvaluation,
                out string persistedReductionFailure);
            int persistedExpectedDeadline = persistenceOrder.acceptedTick +
                (14 + persistedExtensionDays) * GenDate.TicksPerDay;
            List<SalesOrder> loadedOrders = RoundTripOrders(
                new List<SalesOrder> { persistenceOrder }, out string ordersRoundTripFailure);
            SalesOrder loadedOrder = loadedOrders != null && loadedOrders.Count == 1
                ? loadedOrders[0]
                : null;
            bool loadedDeadlineAttempted = loadedOrder != null &&
                !loadedOrder.CanRequest(RenegotiationRequestKind.DeadlineExtension);
            bool loadedQuantityAttempted = loadedOrder != null &&
                !loadedOrder.CanRequest(RenegotiationRequestKind.QuantityReduction);
            r.Check(
                persistedExtension &&
                persistedExtensionEvaluation?.Decision ==
                IntercolonyNegotiationDecision.Accepted &&
                persistedReduction &&
                persistedReductionEvaluation?.Decision ==
                IntercolonyNegotiationDecision.Accepted &&
                loadedOrder != null &&
                loadedOrder.deadlineTick == persistedExpectedDeadline &&
                loadedOrder.Quantity == persistedQuantity &&
                loadedDeadlineAttempted && loadedQuantityAttempted,
                "A8 save/load preserves an explicitly renegotiated obligation",
                $"deadlineTick expected={persistedExpectedDeadline} loaded=" +
                $"{loadedOrder?.deadlineTick.ToString() ?? "none"}; " +
                $"quantity expected={persistedQuantity} loaded=" +
                $"{loadedOrder?.Quantity.ToString() ?? "none"}; " +
                $"deadlineExtensionAttempted expected=True loaded={loadedDeadlineAttempted}; " +
                $"quantityReductionAttempted expected=True loaded={loadedQuantityAttempted}; " +
                $"extensionDecision={persistedExtensionEvaluation?.Decision}, " +
                $"reductionDecision={persistedReductionEvaluation?.Decision}; " +
                $"extensionFailure={persistedExtensionFailure ?? "none"}, " +
                $"reductionFailure={persistedReductionFailure ?? "none"}, " +
                $"roundTripFailure={ordersRoundTripFailure ?? "none"}");
        }

        private static SalesOrder RenegotiationOrder(
            IntercolonyWorldComponent state,
            SettlementEconomicProfile profile,
            ThingDef product,
            int idSuffix,
            int quantity,
            float unitPrice,
            int deadlineDays,
            FulfillmentMode fulfillment,
            SalesOrderStatus status = SalesOrderStatus.Accepted)
        {
            int acceptedTick = GenTicks.TicksGame;
            return new SalesOrder
            {
                id = state.PeekNextId() + 930000 + idSuffix,
                opportunityId = 0,
                settlementId = profile.settlementId,
                settlementName = profile.settlementName,
                factionName = profile.factionName,
                line = new OrderLine(product, quantity),
                unitPrice = unitPrice,
                acceptedTick = acceptedTick,
                deadlineTick = acceptedTick + deadlineDays * GenDate.TicksPerDay,
                status = status,
                fulfillment = fulfillment
            };
        }

        private static int CountSaleFailedRecords(
            IntercolonyWorldComponent state, int orderId)
        {
            int count = 0;
            foreach (CommercialEventRecord record in state.CommercialTimeline)
            {
                if (record != null && record.relatedEntityId == orderId &&
                    record.type == CommercialEventType.SaleFailed)
                {
                    count++;
                }
            }

            return count;
        }

        private static CommercialEventRecord FindRecordFor(
            IntercolonyWorldComponent state, int relatedEntityId, CommercialEventType type)
        {
            foreach (CommercialEventRecord record in state.CommercialTimeline)
            {
                if (record != null && record.relatedEntityId == relatedEntityId &&
                    record.type == type)
                {
                    return record;
                }
            }

            return null;
        }

        private static int CountRecordsFor(
            IntercolonyWorldComponent state, int relatedEntityId, CommercialEventType type)
        {
            int count = 0;
            foreach (CommercialEventRecord record in state.CommercialTimeline)
            {
                if (record != null && record.relatedEntityId == relatedEntityId &&
                    record.type == type)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountRecordsOfType(
            IntercolonyWorldComponent state, CommercialEventType type)
        {
            int count = 0;
            foreach (CommercialEventRecord record in state.CommercialTimeline)
            {
                if (record != null && record.type == type)
                {
                    count++;
                }
            }

            return count;
        }

        private static string DescribeTimelineRecords(
            IntercolonyWorldComponent state, int relatedEntityId)
        {
            return
                $"CounterofferAccepted={CountRecordsFor(
                    state, relatedEntityId, CommercialEventType.CounterofferAccepted)}; " +
                $"DeadlineExtended={CountRecordsFor(
                    state, relatedEntityId, CommercialEventType.DeadlineExtended)}; " +
                $"QuantityReduced={CountRecordsFor(
                    state, relatedEntityId, CommercialEventType.QuantityReduced)}; " +
                $"SaleCancelledByAgreement={CountRecordsFor(
                    state, relatedEntityId, CommercialEventType.SaleCancelledByAgreement)}; " +
                $"SaleCancelled={CountRecordsFor(
                    state, relatedEntityId, CommercialEventType.SaleCancelled)}";
        }

        private static string DescribeTimelineTotals(IntercolonyWorldComponent state)
        {
            return
                $"CounterofferAccepted={CountRecordsOfType(
                    state, CommercialEventType.CounterofferAccepted)}; " +
                $"DeadlineExtended={CountRecordsOfType(
                    state, CommercialEventType.DeadlineExtended)}; " +
                $"QuantityReduced={CountRecordsOfType(
                    state, CommercialEventType.QuantityReduced)}; " +
                $"SaleCancelledByAgreement={CountRecordsOfType(
                    state, CommercialEventType.SaleCancelledByAgreement)}; " +
                $"SaleCancelled={CountRecordsOfType(
                    state, CommercialEventType.SaleCancelled)}";
        }

        private static MarketOpportunity TestOpportunity(
            SettlementEconomicProfile profile,
            ThingDef product,
            int idSuffix)
        {
            return new MarketOpportunity
            {
                id = 900000 + idSuffix,
                settlementId = profile.settlementId,
                settlementName = profile.settlementName,
                thingDef = product,
                quantity = OriginalQuantity,
                unitPrice = OriginalPrice,
                fulfillment = FulfillmentMode.SellerDelivery,
                createdTick = GenTicks.TicksGame,
                expiryTick = GenTicks.TicksGame + 100 * GenDate.TicksPerDay,
                deadlineDays = OriginalDeadlineDays,
                distanceTiles = 10f,
                state = MarketOpportunityState.Available,
                priceExplanation = "Stage 5B self-test fixture"
            };
        }

        private static IntercolonyNegotiationTerms FindCounteredTerms(
            IntercolonyWorldComponent state,
            SettlementEconomicProfile profile,
            ThingDef product,
            IntercolonyProductCategory category,
            MarketOpportunity opportunity,
            out IntercolonyNegotiationResult lastEvaluation)
        {
            return FindCounteredTerms(
                state, profile, product, category, opportunity,
                out lastEvaluation, out _);
        }

        private static IntercolonyNegotiationTerms FindCounteredTerms(
            IntercolonyWorldComponent state,
            SettlementEconomicProfile profile,
            ThingDef product,
            IntercolonyProductCategory category,
            MarketOpportunity opportunity,
            out IntercolonyNegotiationResult lastEvaluation,
            out int proposalsTried)
        {
            lastEvaluation = null;
            proposalsTried = 0;
            // Search the evaluator's actual result band rather than duplicating its score
            // thresholds. The broad bounded grid keeps this fixture useful across settlement
            // identities, while the fallback callers still have a hard refusal case.
            float[] priceMultipliers =
                { 0.50f, 0.60f, 0.70f, 0.80f, 0.90f, 0.95f, 1.00f, 1.05f, 1.10f,
                  1.15f, 1.20f, 1.25f, 1.30f, 1.40f, 1.50f, 1.60f, 1.70f };
            int[] quantityOptions = { OriginalQuantity, 98, 95, 90, 85 };
            int[] deadlineOptions = { 0, 2, 4, 6, 8, 10, 12, 14, 18, 22, 24, 28 };
            foreach (float priceMultiplier in priceMultipliers)
            {
                foreach (int quantity in quantityOptions)
                {
                    foreach (int deadline in deadlineOptions)
                    {
                        proposalsTried++;
                        IntercolonyNegotiationTerms proposed = new IntercolonyNegotiationTerms(
                            quantity,
                            OriginalPrice * priceMultiplier,
                            deadline,
                            opportunity.fulfillment);
                        IntercolonyNegotiationResult result =
                            IntercolonyNegotiationEvaluator.Evaluate(new IntercolonyNegotiationProposal
                            {
                                state = state,
                                profile = profile,
                                thingDef = product,
                                category = category,
                                direction = IntercolonyNegotiationDirection.Sale,
                                originalTerms = new IntercolonyNegotiationTerms(
                                    opportunity.quantity,
                                    opportunity.unitPrice,
                                    opportunity.deadlineDays,
                                    opportunity.fulfillment),
                                proposedTerms = proposed,
                                fulfillmentModeChangeAllowed = opportunity.SupportsBothFulfillmentModes
                            });
                        lastEvaluation = result;
                        if (result.Decision == IntercolonyNegotiationDecision.Countered)
                        {
                            return proposed;
                        }
                    }
                }
            }

            return null;
        }

        private static string DescribeTerms(IntercolonyNegotiationTerms terms)
        {
            return terms == null ? "none" : terms.ToString();
        }

        private static string DescribeOrder(SalesOrder order)
        {
            return order == null
                ? "none"
                : $"{order.Quantity}x/{order.unitPrice:F2}/{order.deadlineTick}";
        }

        private static List<MarketOpportunity> RoundTripOpportunities(
            List<MarketOpportunity> savedList, out string failure)
        {
            List<MarketOpportunity> loadedList = null;
            failure = null;
            string tempPath = Path.Combine(
                Path.GetTempPath(), $"intercolony-negotiation-{Guid.NewGuid():N}.xml");

            try
            {
                Scribe.saver.InitSaving(tempPath, "intercolonyNegotiationTest");
                Scribe_Collections.Look(ref savedList, "opportunities", LookMode.Deep);
                Scribe.saver.FinalizeSaving();

                Scribe.loader.InitLoading(tempPath);
                Scribe_Collections.Look(ref loadedList, "opportunities", LookMode.Deep);
                Scribe.loader.FinalizeLoading();
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                Scribe.ForceStop();
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }

            return loadedList;
        }

        private static List<SalesOrder> RoundTripOrders(
            List<SalesOrder> savedList, out string failure)
        {
            List<SalesOrder> loadedList = null;
            failure = null;
            string tempPath = Path.Combine(
                Path.GetTempPath(), $"intercolony-negotiation-orders-{Guid.NewGuid():N}.xml");

            try
            {
                Scribe.saver.InitSaving(tempPath, "intercolonyNegotiationTest");
                Scribe_Collections.Look(ref savedList, "orders", LookMode.Deep);
                Scribe.saver.FinalizeSaving();

                Scribe.loader.InitLoading(tempPath);
                Scribe_Collections.Look(ref loadedList, "orders", LookMode.Deep);
                Scribe.loader.FinalizeLoading();
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                Scribe.ForceStop();
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }

            return loadedList;
        }

        private static IntercolonyNegotiationProposal Proposal(
            IntercolonyWorldComponent state,
            SettlementEconomicProfile profile,
            ThingDef product,
            IntercolonyProductCategory category,
            IntercolonyNegotiationDirection direction,
            IntercolonyNegotiationTerms proposed)
        {
            return new IntercolonyNegotiationProposal
            {
                state = state,
                profile = profile,
                thingDef = product,
                category = category,
                direction = direction,
                originalTerms = new IntercolonyNegotiationTerms(
                    OriginalQuantity, OriginalPrice, OriginalDeadlineDays,
                    FulfillmentMode.SellerDelivery),
                proposedTerms = proposed
            };
        }

        private static void SetReputation(
            IntercolonyWorldComponent state,
            SettlementEconomicProfile profile,
            float score)
        {
            CommercialReputation record = new CommercialReputation(
                profile.settlementId, profile.settlementName, profile.factionName);
            record.Adjust(score - CommercialReputation.StartingScore);
            state.Reputations[profile.settlementId] = record;
        }

        private static SettlementEconomicProfile SelectProfile(
            IntercolonyWorldComponent state,
            ThingDef product,
            IntercolonyProductCategory category,
            out string failureReason)
        {
            failureReason =
                "no eligible, accessible and non-hostile settlement with an economic profile";
            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements == null || settlements.Count == 0)
            {
                failureReason =
                    "the loaded world has no settlements; needed an eligible, accessible and " +
                    "non-hostile settlement";
                return null;
            }

            SettlementEconomicProfile selected = null;
            float bestDemand = float.MinValue;
            bool foundEligible = false;
            bool foundAccessible = false;
            string lastAccessFailure = null;
            foreach (Settlement settlement in settlements)
            {
                // Mirrors the existing FirstAccessibleSettlement helpers: eligibility is the
                // structural economic-participant gate, while IsAccessible is the production
                // access gate and therefore uses HostilityPolicy.IsAtWar for hostility.
                if (settlement == null || !SettlementProfileGenerator.IsEligible(settlement))
                {
                    continue;
                }

                foundEligible = true;
                if (!IntercolonyMarketAccess.IsAccessible(settlement, out string accessFailure))
                {
                    lastAccessFailure = accessFailure;
                    continue;
                }

                foundAccessible = true;
                SettlementEconomicProfile candidate = state.GetProfile(settlement);
                if (candidate == null)
                {
                    continue;
                }

                float demand = candidate.BaseDemandFor(product, category);
                if (selected == null ||
                    demand > bestDemand ||
                    (Mathf.Approximately(demand, bestDemand) &&
                     candidate.wealthTier > selected.wealthTier))
                {
                    bestDemand = demand;
                    selected = candidate;
                }
            }

            if (selected != null)
            {
                failureReason = null;
            }
            else if (!foundEligible)
            {
                failureReason =
                    "the loaded world has no eligible economic participant; needed an eligible, " +
                    "accessible and non-hostile settlement";
            }
            else if (!foundAccessible)
            {
                failureReason =
                    "no eligible settlement is accessible and non-hostile to the player" +
                    (lastAccessFailure == null
                        ? "."
                        : $"; last access rejection: {lastAccessFailure}.");
            }
            else
            {
                failureReason =
                    "eligible, accessible and non-hostile settlement(s) had no economic profile";
            }

            return selected;
        }

        private static bool TryFindFactor(
            IntercolonyNegotiationResult result, string label, out float contribution)
        {
            foreach (IntercolonyNegotiationFactor factor in result.Factors)
            {
                if (factor.label == label)
                {
                    contribution = factor.contribution;
                    return true;
                }
            }

            contribution = 0f;
            return false;
        }

        // World-dependent inputs can make the untrusted party counter rather than refuse, so
        // compare the player-visible outcome ordering instead of requiring an exact enum equality.
        private static int DecisionFavourability(IntercolonyNegotiationDecision decision)
        {
            switch (decision)
            {
                case IntercolonyNegotiationDecision.Refused:
                    return 0;
                case IntercolonyNegotiationDecision.Countered:
                    return 1;
                case IntercolonyNegotiationDecision.Accepted:
                    return 2;
                default:
                    return -1;
            }
        }

        private static bool TryGetComparisonRow(
            List<CounterofferComparisonRow> rows,
            CounterofferTerm term,
            out CounterofferComparisonRow row)
        {
            if (rows != null)
            {
                foreach (CounterofferComparisonRow candidate in rows)
                {
                    if (candidate.term == term)
                    {
                        row = candidate;
                        return true;
                    }
                }
            }

            row = default(CounterofferComparisonRow);
            return false;
        }

        private static bool SameResult(
            IntercolonyNegotiationResult first, IntercolonyNegotiationResult second)
        {
            return first.Decision == second.Decision &&
                   first.Direction == second.Direction &&
                   Mathf.Approximately(first.AcceptanceScore, second.AcceptanceScore) &&
                   first.RefusalReason == second.RefusalReason &&
                   SameTerms(first.FinalCounterTerms, second.FinalCounterTerms);
        }

        private static bool SameTerms(
            IntercolonyNegotiationTerms first, IntercolonyNegotiationTerms second)
        {
            if (first == null || second == null)
            {
                return first == second;
            }

            return first.quantity == second.quantity &&
                   Mathf.Approximately(first.unitPrice, second.unitPrice) &&
                   first.deadlineDays == second.deadlineDays &&
                   first.fulfillment == second.fulfillment;
        }

        private static bool SameOrderTerms(
            SalesOrder order, IntercolonyNegotiationTerms terms)
        {
            return order != null && terms != null &&
                   order.Quantity == terms.quantity &&
                   Mathf.Approximately(order.unitPrice, terms.unitPrice) &&
                   order.deadlineTick ==
                   order.acceptedTick + terms.deadlineDays * GenDate.TicksPerDay &&
                   order.fulfillment == terms.fulfillment;
        }

        private static string Summarize(Results r)
        {
            r.sb.AppendLine();
            r.sb.AppendLine(
                $"  {r.passed} passed, {r.failed} failed, {r.skipped} skipped.");
            return r.sb.ToString();
        }
    }
}
