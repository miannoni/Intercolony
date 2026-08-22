using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Assertions over commercial reputation (DESIGN.md §83.2, §106).
    ///
    /// §106's acceptance criterion is unusual: "Two colonies with different trade histories
    /// receive observably different future opportunities." That is a claim about *divergence*,
    /// not about a number being right, so the test builds two contrasting histories and
    /// compares what the market then offers — rather than only checking the arithmetic.
    ///
    /// §28's counter-requirement is checked too: the effects must be bounded, or a good record
    /// becomes "guaranteed infinite profit".
    /// </summary>
    public static class IntercolonyReputationSelfTest
    {
        private const int SampleRefreshes = 120;
        private const int MinimumSampleSettlements = 12;
        // More than the 240-opportunity maximum from one 120-refresh settlement.
        private const int MinimumSampledOpportunitiesPerScore = 360;

        public static string Run(IntercolonyWorldComponent state)
        {
            StringBuilder sb = new StringBuilder();
            int passed = 0;
            int failed = 0;

            void Check(string name, bool ok, string detail = null)
            {
                if (ok)
                {
                    passed++;
                }
                else
                {
                    failed++;
                    sb.AppendLine($"  FAIL  {name}{(detail == null ? "" : " — " + detail)}");
                }
            }

            sb.AppendLine("Commercial reputation self-test");

            // --- Scale and tiers ---
            CommercialReputation probe = new CommercialReputation(-1, "Probe", "ProbeFaction");
            Check("starts neutral", Mathf.Abs(probe.Score - CommercialReputation.StartingScore) < 0.01f,
                probe.Score.ToString("F1"));
            Check("neutral is not a flattering tier", probe.Tier == ReputationTier.Known,
                probe.Tier.ToString());

            probe.Adjust(-1000f);
            Check("score floors at 0", Mathf.Approximately(probe.Score, 0f), probe.Score.ToString("F2"));
            Check("floor is Untrusted", probe.Tier == ReputationTier.Untrusted);

            probe.Adjust(10000f);
            Check("score caps at 100", Mathf.Approximately(probe.Score, 100f), probe.Score.ToString("F2"));
            Check("cap is Preferred", probe.Tier == ReputationTier.Preferred);

            // §28: gains must slow near the top, or reputation runs away.
            CommercialReputation low = new CommercialReputation(-2, "Low", "F");
            low.Adjust(-30f);
            float lowBefore = low.Score;
            low.Adjust(5f);
            float lowGain = low.Score - lowBefore;

            CommercialReputation high = new CommercialReputation(-3, "High", "F");
            high.Adjust(40f);
            float highBefore = high.Score;
            high.Adjust(5f);
            float highGain = high.Score - highBefore;

            Check("gains diminish as reputation rises", lowGain > highGain,
                $"low gained {lowGain:F2}, high gained {highGain:F2}");

            // Penalties must not diminish — easier to lose than to keep.
            CommercialReputation penalty = new CommercialReputation(-4, "Penalty", "F");
            penalty.Adjust(45f);
            float before = penalty.Score;
            penalty.Adjust(-10f);
            Check("penalties land at full weight",
                Mathf.Abs((before - penalty.Score) - 10f) < 0.01f,
                $"lost {before - penalty.Score:F2}");

            // --- §28 effect bounds ---
            float bestPrice = ReputationService.PriceFactorFor(100f).multiplier;
            float worstPrice = ReputationService.PriceFactorFor(0f).multiplier;
            Check("a perfect record improves prices", bestPrice > worstPrice,
                $"{bestPrice:F3} vs {worstPrice:F3}");
            Check("price effect stays slight", bestPrice <= 1.15f,
                $"best price multiplier {bestPrice:F3} — §28 says slightly better prices");

            float bestSize = ReputationService.OpportunitySizeFactor(100f);
            float bestFreq = ReputationService.OpportunityFrequencyFactor(100f);
            Check("order size is bounded", bestSize <= 1.5f, bestSize.ToString("F2"));
            Check("frequency is bounded", bestFreq <= 1.75f, bestFreq.ToString("F2"));

            // Combined effect is the one that actually matters for runaway.
            float combined = bestPrice * bestSize * bestFreq;
            Check("combined best-case advantage stays sane", combined < 3f,
                $"x{combined:F2} vs a neutral partner — §28 warns against runaway feedback");
            sb.AppendLine($"  (best-case combined advantage: x{combined:F2})");

            // --- §106: two histories, observably different offers ---
            List<Settlement> eligible = new List<Settlement>();
            foreach (Settlement settlement in Find.WorldObjects.Settlements)
            {
                if (SettlementProfileGenerator.IsEligible(settlement) &&
                    IntercolonyMarketAccess.IsAccessible(settlement))
                {
                    eligible.Add(settlement);
                }
            }

            if (eligible.Count < MinimumSampleSettlements)
            {
                sb.AppendLine($"  (only {eligible.Count} eligible accessible settlements; " +
                              $"need {MinimumSampleSettlements}; divergence check skipped)");
                sb.AppendLine($"  {passed} passed, {failed} failed.");
                return sb.ToString();
            }

            List<ReputationSnapshot> savedReputations = new List<ReputationSnapshot>();
            foreach (Settlement settlement in eligible)
            {
                CommercialReputation record = state.GetOrCreateReputation(settlement);
                savedReputations.Add(new ReputationSnapshot
                {
                    record = record,
                    originalScore = record.Score
                });
            }

            MarketSample trusted = new MarketSample();
            MarketSample distrusted = new MarketSample();
            try
            {
                // Same settlements, same seeds, only the trade history differs — so any
                // difference in output is attributable to reputation and not one settlement's
                // random draw.
                trusted = SampleMarket(state, eligible, 100f);
                distrusted = SampleMarket(state, eligible, 5f);
            }
            finally
            {
                // Restore every touched settlement to its captured value, even if sampling
                // throws part-way through.
                foreach (ReputationSnapshot saved in savedReputations)
                {
                    saved.record.Adjust(saved.originalScore - saved.record.Score);
                }
            }

            sb.AppendLine($"  (sampled {eligible.Count} settlements across {SampleRefreshes} refreshes per score)");

            sb.AppendLine($"  (trusted: {trusted.count} offers, {trusted.totalQuantity} total units, " +
                          $"avg {trusted.AverageQuantity:F0} units, {trusted.totalDeadline} total deadline-days, " +
                          $"avg {trusted.AverageDeadline:F1}d deadline)");
            sb.AppendLine($"  (distrusted: {distrusted.count} offers, {distrusted.totalQuantity} total units, " +
                          $"avg {distrusted.AverageQuantity:F0} units, {distrusted.totalDeadline} total deadline-days, " +
                          $"avg {distrusted.AverageDeadline:F1}d deadline)");

            if (trusted.count < MinimumSampledOpportunitiesPerScore ||
                distrusted.count < MinimumSampledOpportunitiesPerScore)
            {
                sb.AppendLine($"  (sampled {trusted.count} trusted and {distrusted.count} distrusted " +
                              $"opportunities; need {MinimumSampledOpportunitiesPerScore} per score; " +
                              "divergence check skipped)");
                sb.AppendLine($"  {passed} passed, {failed} failed.");
                return sb.ToString();
            }

            Check("a trusted partner posts at least as often", trusted.count >= distrusted.count,
                $"{trusted.count} vs {distrusted.count}");
            Check("trade histories produce observably different offers",
                trusted.count != distrusted.count ||
                Mathf.Abs(trusted.AverageQuantity - distrusted.AverageQuantity) > 0.5f ||
                Mathf.Abs(trusted.AverageDeadline - distrusted.AverageDeadline) > 0.5f,
                "the two histories produced indistinguishable markets");

            Check("a trusted partner commits to larger lots",
                trusted.AverageQuantity > distrusted.AverageQuantity,
                $"{eligible.Count} settlements; trusted {trusted.totalQuantity} units across " +
                $"{trusted.count} opportunities (avg {trusted.AverageQuantity:F1}) vs distrusted " +
                $"{distrusted.totalQuantity} units across {distrusted.count} opportunities " +
                $"(avg {distrusted.AverageQuantity:F1})");
            Check("a trusted partner allows more time",
                trusted.AverageDeadline >= distrusted.AverageDeadline,
                $"{trusted.AverageDeadline:F1}d vs {distrusted.AverageDeadline:F1}d");

            sb.AppendLine($"  {passed} passed, {failed} failed.");
            return sb.ToString();
        }

        private struct MarketSample
        {
            public int count;
            public long totalQuantity;
            public long totalDeadline;

            public float AverageQuantity => count == 0 ? 0f : totalQuantity / (float)count;
            public float AverageDeadline => count == 0 ? 0f : totalDeadline / (float)count;
        }

        private struct ReputationSnapshot
        {
            public CommercialReputation record;
            public float originalScore;
        }

        /// <summary>
        /// Generates many refresh cycles across the sampled settlements at a fixed reputation
        /// and summarises the result. Uses the same settlements and seed sequence for both
        /// scores, so reputation is the only variable.
        /// </summary>
        private static MarketSample SampleMarket(
            IntercolonyWorldComponent state,
            List<Settlement> settlements,
            float score)
        {
            foreach (Settlement settlement in settlements)
            {
                CommercialReputation record = state.GetOrCreateReputation(settlement);
                record.Adjust(score - record.Score);
            }

            MarketSample sample = new MarketSample();
            int idCounter = 900000;
            foreach (Settlement settlement in settlements)
            {
                SettlementEconomicProfile profile = state.GetProfile(settlement);
                for (int refresh = 0; refresh < SampleRefreshes; refresh++)
                {
                    List<MarketOpportunity> batch = MarketOpportunityGenerator.GenerateFor(
                        settlement, profile, state.EconomySeed, refresh, 0, () => idCounter++);

                    foreach (MarketOpportunity opportunity in batch)
                    {
                        sample.count++;
                        sample.totalQuantity += opportunity.quantity;
                        sample.totalDeadline += opportunity.deadlineDays;
                    }
                }
            }

            return sample;
        }
    }
}
