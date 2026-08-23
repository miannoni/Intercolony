using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Self-test for the Stage 5A negotiation evaluator. The six assertions are deliberately
    /// behavioural: reputation must change willingness, hard boundaries must survive reputation,
    /// counterparty-favouring terms must be accepted, identical reads must be deterministic,
    /// explanations must contain real contributions, and Sale/Purchase must invert perspective.
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
            r.sb.AppendLine("Negotiation evaluator self-test (Stage 5A)");

            if (state == null)
            {
                r.sb.AppendLine("  No world state available. Open or load a game first.");
                return Summarize(r);
            }

            ThingDef product = ThingDefOf.Silver;
            IntercolonyProductCategory category =
                IntercolonyProductClassifier.Classify(product) ??
                IntercolonyProductCategory.Commodities;
            SettlementEconomicProfile profile = SelectProfile(state, product, category);
            if (profile == null || state.Reputations == null)
            {
                r.Skip(
                    "six evaluator assertions",
                    "the loaded world has no eligible settlement profile or reputation dictionary");
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
            IntercolonyProductCategory category)
        {
            List<SettlementEconomicProfile> profiles = state.AllProfiles();
            SettlementEconomicProfile selected = null;
            float bestDemand = float.MinValue;
            foreach (SettlementEconomicProfile candidate in profiles)
            {
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

        private static string Summarize(Results r)
        {
            r.sb.AppendLine();
            r.sb.AppendLine(
                $"  {r.passed} passed, {r.failed} failed, {r.skipped} skipped.");
            return r.sb.ToString();
        }
    }
}
