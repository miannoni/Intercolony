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
                    trusted.AcceptanceScore > untrusted.AcceptanceScore &&
                    DecisionFavourability(trusted.Decision) >=
                    DecisionFavourability(untrusted.Decision),
                    "a trusted counterparty accepts a modest price increase more readily",
                    $"trusted={trusted.Decision}/{trusted.AcceptanceScore:F3}, " +
                    $"untrusted={untrusted.Decision}/{untrusted.AcceptanceScore:F3}");

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
                    extreme.Decision == IntercolonyNegotiationDecision.Refused &&
                    extreme.RefusalReason != null &&
                    extreme.RefusalReason.Contains("one-round negotiation boundary"),
                    "an extreme price demand is refused even at perfect reputation",
                    $"{extreme.Decision}: {extreme.RefusalReason}");

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

                ThingDef stateMachineProduct = SelectStateMachineProduct();
                IntercolonyProductCategory? stateMachineCategory =
                    IntercolonyProductClassifier.Classify(stateMachineProduct);
                if (!stateMachineCategory.HasValue)
                {
                    r.Skip(
                        "nine Stage 5B state-machine/UI assertions",
                        "the loaded world has no definition-driven fungible market product");
                }
                else
                {
                    RunStateMachineAssertions(
                        state, profile, stateMachineProduct, stateMachineCategory.Value, r);
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
                state.Reputations.Clear();
                foreach (KeyValuePair<int, CommercialReputation> entry in savedReputations)
                {
                    state.Reputations[entry.Key] = entry.Value;
                }

                r.sb.AppendLine(
                    $"        Stage 5B world contents restored: " +
                    $"{state.Opportunities.Count} opportunit{(state.Opportunities.Count == 1 ? "y" : "ies")}, " +
                    $"{state.Orders.Count} sales order(s), {state.Reputations.Count} reputation record(s).");
            }
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
            lastEvaluation = null;
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
