using System;
using System.Collections.Generic;
using System.Reflection;
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
            int skipped = 0;

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

            void Skip(string name, string reason)
            {
                skipped++;
                sb.AppendLine($"  SKIPPED  {name} — {reason}");
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

            RelationshipResults relationshipResults =
                RunRelationshipMilestoneAssertions(state);
            sb.Append(relationshipResults.Output);
            passed += relationshipResults.passed;
            failed += relationshipResults.failed;
            skipped += relationshipResults.skipped;

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
                Skip("market divergence",
                    $"only {eligible.Count} eligible accessible settlements; " +
                    $"need {MinimumSampleSettlements}");
                sb.AppendLine(SummaryLine(passed, failed, skipped));
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
                Skip("market divergence",
                    $"sampled {trusted.count} trusted and {distrusted.count} distrusted " +
                    $"opportunities; need {MinimumSampledOpportunitiesPerScore} per score");
                sb.AppendLine(SummaryLine(passed, failed, skipped));
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

            sb.AppendLine(SummaryLine(passed, failed, skipped));
            return sb.ToString();
        }

        private sealed class RelationshipResults
        {
            public readonly StringBuilder sb = new StringBuilder();
            public int passed;
            public int failed;
            public int skipped;

            public string Output => sb.ToString();

            public void Check(string name, bool ok, string detail)
            {
                if (ok)
                {
                    passed++;
                }
                else
                {
                    failed++;
                    sb.AppendLine($"  FAIL  {name} - {detail}");
                }
            }

            public void Skip(string name, string reason)
            {
                skipped++;
                sb.AppendLine($"  SKIPPED  {name} - {reason}");
            }
        }

        private static RelationshipResults RunRelationshipMilestoneAssertions(
            IntercolonyWorldComponent state)
        {
            RelationshipResults result = new RelationshipResults();
            result.sb.AppendLine("Stage 5E relationship-tier milestone assertions");

            Settlement relationshipSettlement = null;
            Settlement freshSettlement = null;
            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements != null)
            {
                foreach (Settlement settlement in settlements)
                {
                    if (settlement == null)
                    {
                        continue;
                    }

                    relationshipSettlement ??= settlement;
                    if (state != null && !state.Reputations.ContainsKey(settlement.ID))
                    {
                        freshSettlement ??= settlement;
                    }
                }
            }

            if (state == null || relationshipSettlement == null)
            {
                string reason = state == null
                    ? "no world state"
                    : $"no settlement available; settlements={settlements?.Count ?? 0}";
                result.Skip("R1 in-tier score move records nothing", reason);
                result.Skip("R2 upward crossing records one labelled milestone", reason);
                result.Skip("R3 hysteresis suppresses a boundary graze", reason);
                result.Skip("R4 downward crossing records one labelled milestone", reason);
                result.Skip("R5 migration seed prevents a fabricated milestone", reason);
                result.Skip("R6 first contact records nothing", reason);
                result.Skip("R7 NoteOrderFailed funnels through milestone recording", reason);
                result.Skip("R8 48-to-49 migration seeds each reputation", reason);
                return result;
            }

            Dictionary<int, CommercialReputation> savedReputations =
                new Dictionary<int, CommercialReputation>(state.Reputations);
            List<CommercialEventRecord> savedCommercialTimeline =
                new List<CommercialEventRecord>(state.CommercialTimeline);
            int savedTimelineStartTick = state.CommercialTimelineStartTick;
            int savedSaveVersion = state.SaveVersion;
            FieldInfo saveVersionField = typeof(IntercolonyWorldComponent).GetField(
                "saveVersion", BindingFlags.Instance | BindingFlags.NonPublic);

            try
            {
                // Every case uses a fixture instance rather than mutating an existing record.
                // Restoring the dictionary below therefore restores each original score and
                // lastRecordedTier exactly, while also removing all fixture reputations.
                CommercialReputation r1 = NewRelationshipReputation(
                    relationshipSettlement, 50f);
                ReputationTier r1PreviousTier = r1.Tier;
                float r1ScoreBefore = r1.Score;
                int r1TimelineBefore = state.CommercialTimeline.Count;
                int r1MilestonesBefore = CountRelationshipMilestones(
                    state, relationshipSettlement.ID);
                ReputationService.ApplyAdjustment(state, r1, 1f);
                float r1ScoreAfter = r1.Score;
                int r1TimelineAfter = state.CommercialTimeline.Count;
                int r1MilestonesAfter = CountRelationshipMilestones(
                    state, relationshipSettlement.ID);
                result.Check("R1 in-tier score move records nothing",
                    r1.Tier == r1PreviousTier &&
                    r1MilestonesAfter == r1MilestonesBefore &&
                    r1TimelineAfter == r1TimelineBefore,
                    RelationshipDetail(
                        r1, r1PreviousTier, r1.Tier, r1ScoreBefore, r1ScoreAfter,
                        r1TimelineBefore, r1TimelineAfter,
                        r1MilestonesBefore, r1MilestonesAfter));

                CommercialReputation r2 = NewRelationshipReputation(
                    relationshipSettlement, 59f);
                ReputationTier r2PreviousTier = r2.Tier;
                float r2ScoreBefore = r2.Score;
                int r2TimelineBefore = state.CommercialTimeline.Count;
                int r2MilestonesBefore = CountRelationshipMilestones(
                    state, relationshipSettlement.ID);
                ReputationService.ApplyAdjustment(
                    state, r2, ReputationService.RelationshipMilestoneHysteresis + 2f);
                float r2ScoreAfter = r2.Score;
                int r2TimelineAfter = state.CommercialTimeline.Count;
                int r2MilestonesAfter = CountRelationshipMilestones(
                    state, relationshipSettlement.ID);
                CommercialEventRecord r2Record = LastRelationshipMilestone(
                    state, relationshipSettlement.ID);
                string r2ExpectedDetail =
                    $"{r2.TierLabel(r2PreviousTier)} -> {r2.TierLabel(r2.Tier)}";
                result.Check("R2 upward crossing records one labelled milestone",
                    r2.Tier == ReputationTier.Reliable &&
                    r2ScoreAfter >= 60f + ReputationService.RelationshipMilestoneHysteresis &&
                    r2MilestonesAfter == r2MilestonesBefore + 1 &&
                    r2TimelineAfter == r2TimelineBefore + 1 &&
                    r2Record != null && r2Record.compactDetail == r2ExpectedDetail,
                    RelationshipDetail(
                        r2, r2PreviousTier, r2.Tier, r2ScoreBefore, r2ScoreAfter,
                        r2TimelineBefore, r2TimelineAfter,
                        r2MilestonesBefore, r2MilestonesAfter,
                        $"record={r2Record?.compactDetail ?? "none"}; expected={r2ExpectedDetail}"));

                CommercialReputation r3 = NewRelationshipReputation(
                    relationshipSettlement, 59f);
                ReputationTier r3PreviousTier = r3.Tier;
                float r3ScoreBefore = r3.Score;
                int r3TimelineBefore = state.CommercialTimeline.Count;
                int r3MilestonesBefore = CountRelationshipMilestones(
                    state, relationshipSettlement.ID);
                ReputationService.ApplyAdjustment(state, r3, 1.5f);
                float r3ScoreAfterGraze = r3.Score;
                ReputationTier r3TierAfterGraze = r3.Tier;
                int r3TimelineAfterGraze = state.CommercialTimeline.Count;
                int r3MilestonesAfterGraze = CountRelationshipMilestones(
                    state, relationshipSettlement.ID);
                ReputationService.ApplyAdjustment(state, r3, 2f);
                float r3ScoreAfterClear = r3.Score;
                ReputationTier r3TierAfterClear = r3.Tier;
                int r3TimelineAfterClear = state.CommercialTimeline.Count;
                int r3MilestonesAfterClear = CountRelationshipMilestones(
                    state, relationshipSettlement.ID);
                result.Check("R3 hysteresis suppresses a boundary graze",
                    r3TierAfterGraze == ReputationTier.Reliable &&
                    r3ScoreAfterGraze > 60f &&
                    r3ScoreAfterGraze <
                        60f + ReputationService.RelationshipMilestoneHysteresis &&
                    r3MilestonesAfterGraze == r3MilestonesBefore &&
                    r3TimelineAfterGraze == r3TimelineBefore &&
                    r3TierAfterClear == ReputationTier.Reliable &&
                    r3ScoreAfterClear >=
                        60f + ReputationService.RelationshipMilestoneHysteresis &&
                    r3MilestonesAfterClear == r3MilestonesBefore + 1 &&
                    r3TimelineAfterClear == r3TimelineBefore + 1,
                    $"score {r3ScoreBefore:F2}->{r3ScoreAfterGraze:F2}->{r3ScoreAfterClear:F2}; " +
                    $"tiers {r3.TierLabel(r3PreviousTier)} -> " +
                    $"{r3.TierLabel(r3TierAfterGraze)} -> {r3.TierLabel(r3TierAfterClear)}; " +
                    $"timeline {r3TimelineBefore}->{r3TimelineAfterGraze}->{r3TimelineAfterClear}; " +
                    $"milestones {r3MilestonesBefore}->{r3MilestonesAfterGraze}->" +
                    $"{r3MilestonesAfterClear}; " +
                    $"deadband={ReputationService.RelationshipMilestoneHysteresis:F2}");

                CommercialReputation r4 = NewRelationshipReputation(
                    relationshipSettlement, 75f);
                ReputationTier r4PreviousTier = r4.Tier;
                float r4ScoreBefore = r4.Score;
                int r4TimelineBefore = state.CommercialTimeline.Count;
                int r4MilestonesBefore = CountRelationshipMilestones(
                    state, relationshipSettlement.ID);
                ReputationService.ApplyAdjustment(
                    state, r4, -(ReputationService.RelationshipMilestoneHysteresis + 16f));
                float r4ScoreAfter = r4.Score;
                int r4TimelineAfter = state.CommercialTimeline.Count;
                int r4MilestonesAfter = CountRelationshipMilestones(
                    state, relationshipSettlement.ID);
                CommercialEventRecord r4Record = LastRelationshipMilestone(
                    state, relationshipSettlement.ID);
                string r4ExpectedDetail =
                    $"{r4.TierLabel(r4PreviousTier)} -> {r4.TierLabel(r4.Tier)}";
                result.Check("R4 downward crossing records one labelled milestone",
                    r4.Tier == ReputationTier.Known &&
                    r4ScoreAfter <= 60f - ReputationService.RelationshipMilestoneHysteresis &&
                    r4MilestonesAfter == r4MilestonesBefore + 1 &&
                    r4TimelineAfter == r4TimelineBefore + 1 &&
                    r4Record != null && r4Record.compactDetail == r4ExpectedDetail,
                    RelationshipDetail(
                        r4, r4PreviousTier, r4.Tier, r4ScoreBefore, r4ScoreAfter,
                        r4TimelineBefore, r4TimelineAfter,
                        r4MilestonesBefore, r4MilestonesAfter,
                        $"record={r4Record?.compactDetail ?? "none"}; expected={r4ExpectedDetail}"));

                CommercialReputation r5 = NewRelationshipReputation(
                    relationshipSettlement, 85f);
                ReputationTier r5SeededTier = r5.Tier;
                r5.lastRecordedTier = r5SeededTier;
                float r5SeededScoreBefore = r5.Score;
                int r5SeededTimelineBefore = state.CommercialTimeline.Count;
                int r5SeededMilestonesBefore = CountRelationshipMilestones(
                    state, relationshipSettlement.ID);
                const float r5SmallAdjustment = 1f;
                ReputationService.ApplyAdjustment(state, r5, r5SmallAdjustment);
                float r5SeededScoreAfter = r5.Score;
                int r5SeededTimelineAfter = state.CommercialTimeline.Count;
                int r5SeededMilestonesAfter = CountRelationshipMilestones(
                    state, relationshipSettlement.ID);

                ReputationTier r5UnseededTier = default(ReputationTier);
                r5.lastRecordedTier = r5UnseededTier;
                float r5UnseededScoreBefore = r5.Score;
                int r5UnseededTimelineBefore = state.CommercialTimeline.Count;
                int r5UnseededMilestonesBefore = CountRelationshipMilestones(
                    state, relationshipSettlement.ID);
                ReputationService.ApplyAdjustment(state, r5, r5SmallAdjustment);
                float r5UnseededScoreAfter = r5.Score;
                int r5UnseededTimelineAfter = state.CommercialTimeline.Count;
                int r5UnseededMilestonesAfter = CountRelationshipMilestones(
                    state, relationshipSettlement.ID);
                CommercialEventRecord r5Record = LastRelationshipMilestone(
                    state, relationshipSettlement.ID);
                string r5ExpectedDetail =
                    $"{r5.TierLabel(r5UnseededTier)} -> {r5.TierLabel(r5.Tier)}";
                result.Check("R5 migration seed prevents a fabricated milestone",
                    r5.Tier == r5SeededTier &&
                    r5SeededMilestonesAfter == r5SeededMilestonesBefore &&
                    r5SeededTimelineAfter == r5SeededTimelineBefore &&
                    r5UnseededMilestonesAfter == r5UnseededMilestonesBefore + 1 &&
                    r5UnseededTimelineAfter == r5UnseededTimelineBefore + 1 &&
                    r5Record != null && r5Record.compactDetail == r5ExpectedDetail,
                    $"seeded score {r5SeededScoreBefore:F2}->{r5SeededScoreAfter:F2}; " +
                    $"seeded tiers {r5.TierLabel(r5SeededTier)} -> " +
                    $"{r5.TierLabel(r5SeededTier)}; " +
                    $"timeline {r5SeededTimelineBefore}->{r5SeededTimelineAfter}; " +
                    $"milestones {r5SeededMilestonesBefore}->{r5SeededMilestonesAfter}; " +
                    $"unseeded score {r5UnseededScoreBefore:F2}->{r5UnseededScoreAfter:F2}; " +
                    $"unseeded tiers {r5.TierLabel(r5UnseededTier)} -> " +
                    $"{r5.TierLabel(r5.Tier)}; " +
                    $"timeline {r5UnseededTimelineBefore}->{r5UnseededTimelineAfter}; " +
                    $"milestones {r5UnseededMilestonesBefore}->{r5UnseededMilestonesAfter}; " +
                    $"record={r5Record?.compactDetail ?? "none"}; expected={r5ExpectedDetail}");

                if (freshSettlement == null)
                {
                    result.Skip("R6 first contact records nothing",
                        $"every settlement already has a reputation; settlements=" +
                        $"{settlements?.Count ?? 0}, reputations={state.Reputations.Count}");
                }
                else
                {
                    int r6TimelineBefore = state.CommercialTimeline.Count;
                    int r6MilestonesBefore = CountRelationshipMilestones(
                        state, freshSettlement.ID);
                    CommercialReputation r6 = state.GetOrCreateReputation(freshSettlement);
                    float r6ScoreBefore = r6.Score;
                    ReputationTier r6CurrentTier = r6.Tier;
                    int r6TimelineAfter = state.CommercialTimeline.Count;
                    int r6MilestonesAfter = CountRelationshipMilestones(
                        state, freshSettlement.ID);
                    result.Check("R6 first contact records nothing",
                        r6.lastRecordedTier == r6CurrentTier &&
                        r6MilestonesAfter == r6MilestonesBefore &&
                        r6TimelineAfter == r6TimelineBefore,
                        $"score {r6ScoreBefore:F2}->{r6.Score:F2}; " +
                        $"tiers {r6.TierLabel(r6.lastRecordedTier)} -> " +
                        $"{r6.TierLabel(r6CurrentTier)}; " +
                        $"timeline {r6TimelineBefore}->{r6TimelineAfter}; " +
                        $"milestones {r6MilestonesBefore}->{r6MilestonesAfter}");
                }

                CommercialReputation r7 = NewRelationshipReputation(
                    relationshipSettlement, 61f);
                state.Reputations[relationshipSettlement.ID] = r7;
                ReputationTier r7PreviousTier = r7.Tier;
                float r7ScoreBefore = r7.Score;
                int r7FailedBefore = r7.ordersFailed;
                int r7TimelineBefore = state.CommercialTimeline.Count;
                int r7MilestonesBefore = CountRelationshipMilestones(
                    state, relationshipSettlement.ID);
                SalesOrder r7Order = new SalesOrder
                {
                    settlementId = relationshipSettlement.ID,
                    settlementName = relationshipSettlement.Label ?? "unnamed"
                };
                ReputationService.NoteOrderFailed(state, r7Order);
                float r7ScoreAfter = r7.Score;
                int r7TimelineAfter = state.CommercialTimeline.Count;
                int r7MilestonesAfter = CountRelationshipMilestones(
                    state, relationshipSettlement.ID);
                CommercialEventRecord r7Record = LastRelationshipMilestone(
                    state, relationshipSettlement.ID);
                string r7ExpectedDetail =
                    $"{r7.TierLabel(r7PreviousTier)} -> {r7.TierLabel(r7.Tier)}";
                result.Check("R7 NoteOrderFailed funnels through milestone recording",
                    r7.ordersFailed == r7FailedBefore + 1 &&
                    r7.Tier == ReputationTier.Known &&
                    r7ScoreAfter <= 60f - ReputationService.RelationshipMilestoneHysteresis &&
                    r7MilestonesAfter == r7MilestonesBefore + 1 &&
                    r7TimelineAfter == r7TimelineBefore + 1 &&
                    r7Record != null && r7Record.compactDetail == r7ExpectedDetail,
                    RelationshipDetail(
                        r7, r7PreviousTier, r7.Tier, r7ScoreBefore, r7ScoreAfter,
                        r7TimelineBefore, r7TimelineAfter,
                        r7MilestonesBefore, r7MilestonesAfter,
                        "hook=NoteOrderFailed; weight=-12.00; " +
                        $"record={r7Record?.compactDetail ?? "none"}; expected={r7ExpectedDetail}"));

                // R8 drives the actual schema migration with two tiers. Keeping only these
                // fixtures in the dictionary prevents the migration from changing any real
                // reputation's lastRecordedTier before the outer teardown restores the world.
                if (saveVersionField == null)
                {
                    result.Skip("R8 48-to-49 migration seeds each reputation",
                        "persisted saveVersion field is not accessible");
                }
                else
                {
                    CommercialReputation r8Preferred = NewRelationshipReputation(
                        relationshipSettlement, 85f);
                    int r8UntrustedId = relationshipSettlement.ID;
                    while (state.Reputations.ContainsKey(r8UntrustedId) ||
                           r8UntrustedId == r8Preferred.settlementId)
                    {
                        r8UntrustedId++;
                    }

                    CommercialReputation r8Untrusted = new CommercialReputation(
                        r8UntrustedId, "R8 untrusted fixture", "");
                    r8Untrusted.Adjust(10f - CommercialReputation.StartingScore);
                    r8Untrusted.lastRecordedTier = r8Untrusted.Tier;

                    ReputationTier r8PreferredExpectedTier = r8Preferred.Tier;
                    ReputationTier r8UntrustedExpectedTier = r8Untrusted.Tier;
                    ReputationTier r8StartingTier = new CommercialReputation().Tier;
                    if (r8PreferredExpectedTier == default(ReputationTier) ||
                        r8PreferredExpectedTier == r8StartingTier ||
                        r8PreferredExpectedTier == r8UntrustedExpectedTier)
                    {
                        result.Skip("R8 48-to-49 migration seeds each reputation",
                            "fixture tiers are not distinct and non-default/non-starting: " +
                            $"preferred score {r8Preferred.Score:F2} tier {r8PreferredExpectedTier}; " +
                            $"untrusted score {r8Untrusted.Score:F2} tier {r8UntrustedExpectedTier}");
                    }
                    else
                    {
                        r8Preferred.lastRecordedTier = ReputationTier.Untrusted;
                        r8Untrusted.lastRecordedTier = ReputationTier.Untrusted;

                        state.Reputations.Clear();
                        state.Reputations[r8Preferred.settlementId] = r8Preferred;
                        state.Reputations[r8Untrusted.settlementId] = r8Untrusted;

                        bool saveVersionForced = false;
                        try
                        {
                            saveVersionField.SetValue(state, 48);
                            saveVersionForced = true;
                        }
                        catch (Exception ex)
                        {
                            result.Skip("R8 48-to-49 migration seeds each reputation",
                                $"could not force saveVersion to 48: {ex.Message}");
                        }

                        if (saveVersionForced)
                        {
                            int r8TimelineBefore = state.CommercialTimeline.Count;
                            int r8MilestonesBefore = CountRelationshipMilestones(state);
                            state.MigrateIfNeeded();
                            int r8TimelineAfter = state.CommercialTimeline.Count;
                            int r8MilestonesAfter = CountRelationshipMilestones(state);
                            int r8MilestonesAdded = r8MilestonesAfter - r8MilestonesBefore;

                            result.Check("R8 48-to-49 migration seeds each reputation",
                                state.SaveVersion == IntercolonyWorldComponent.CurrentSaveVersion &&
                                r8Preferred.lastRecordedTier == r8PreferredExpectedTier &&
                                r8Untrusted.lastRecordedTier == r8UntrustedExpectedTier &&
                                r8TimelineAfter == r8TimelineBefore &&
                                r8MilestonesAdded == 0,
                                $"preferred: score {r8Preferred.Score:F2}; " +
                                $"expected tier {r8PreferredExpectedTier}; " +
                                $"actual lastRecordedTier {r8Preferred.lastRecordedTier}; " +
                                $"untrusted: score {r8Untrusted.Score:F2}; " +
                                $"expected tier {r8UntrustedExpectedTier}; " +
                                $"actual lastRecordedTier {r8Untrusted.lastRecordedTier}; " +
                                $"saveVersion {savedSaveVersion}->{state.SaveVersion}; " +
                                $"timeline {r8TimelineBefore}->{r8TimelineAfter}; " +
                                $"RelationshipMilestone count {r8MilestonesBefore}->" +
                                $"{r8MilestonesAfter} (added {r8MilestonesAdded}, expected 0)");
                        }
                    }
                }
            }
            finally
            {
                if (saveVersionField != null)
                {
                    saveVersionField.SetValue(state, savedSaveVersion);
                }

                state.CommercialTimeline.Clear();
                state.CommercialTimeline.AddRange(savedCommercialTimeline);
                state.CommercialTimelineStartTick = savedTimelineStartTick;
                state.Reputations.Clear();
                foreach (KeyValuePair<int, CommercialReputation> entry in savedReputations)
                {
                    state.Reputations[entry.Key] = entry.Value;
                }

                result.sb.AppendLine(
                    $"  (relationship fixtures restored: {state.Reputations.Count} reputation record(s), " +
                    $"{state.CommercialTimeline.Count} timeline record(s))");
            }

            return result;
        }

        private static string SummaryLine(int passed, int failed, int skipped)
        {
            return skipped == 0
                ? $"  {passed} passed, {failed} failed."
                : $"  {passed} passed, {failed} failed, {skipped} skipped.";
        }

        private static CommercialReputation NewRelationshipReputation(
            Settlement settlement, float score)
        {
            CommercialReputation reputation = new CommercialReputation(
                settlement.ID,
                settlement.Label ?? "unnamed",
                settlement.Faction?.Name ?? "");
            reputation.Adjust(score - CommercialReputation.StartingScore);
            reputation.lastRecordedTier = reputation.Tier;
            return reputation;
        }

        private static int CountRelationshipMilestones(
            IntercolonyWorldComponent state, int settlementId)
        {
            int count = 0;
            foreach (CommercialEventRecord record in state.CommercialTimeline)
            {
                if (record != null && record.settlementId == settlementId &&
                    record.type == CommercialEventType.RelationshipMilestone)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountRelationshipMilestones(IntercolonyWorldComponent state)
        {
            int count = 0;
            foreach (CommercialEventRecord record in state.CommercialTimeline)
            {
                if (record != null && record.type == CommercialEventType.RelationshipMilestone)
                {
                    count++;
                }
            }

            return count;
        }

        private static CommercialEventRecord LastRelationshipMilestone(
            IntercolonyWorldComponent state, int settlementId)
        {
            for (int i = state.CommercialTimeline.Count - 1; i >= 0; i--)
            {
                CommercialEventRecord record = state.CommercialTimeline[i];
                if (record != null && record.settlementId == settlementId &&
                    record.type == CommercialEventType.RelationshipMilestone)
                {
                    return record;
                }
            }

            return null;
        }

        private static string RelationshipDetail(
            CommercialReputation reputation,
            ReputationTier previousTier,
            ReputationTier currentTier,
            float scoreBefore,
            float scoreAfter,
            int timelineBefore,
            int timelineAfter,
            int milestonesBefore,
            int milestonesAfter,
            string extra = null)
        {
            return $"score {scoreBefore:F2}->{scoreAfter:F2}; " +
                   $"tiers {reputation.TierLabel(previousTier)} -> " +
                   $"{reputation.TierLabel(currentTier)}; " +
                   $"timeline {timelineBefore}->{timelineAfter}; " +
                   $"milestones {milestonesBefore}->{milestonesAfter}" +
                   (string.IsNullOrEmpty(extra) ? "" : $"; {extra}");
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
