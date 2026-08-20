using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Records what the 0.9.3 market actually does, before the 1.0 program changes it
    /// (docs/INTERCOLONY_1_0_IMPLEMENTATION_PLAN.md Stage 0.2).
    ///
    /// **This is not here to preserve 0.9.3's balance.** It is the evidence that answers a question
    /// nobody can answer from memory once Stage 2 lands: did the new economy stop producing offers,
    /// collapse every settlement onto one category, hand out unlimited supply, inflate prices, or
    /// quietly stop letting archetype matter? Those failures all look like "the market feels off" in
    /// play and like nothing at all in a self-test.
    ///
    /// It measures the real production owners — <see cref="MarketOpportunityGenerator.GenerateFor"/>,
    /// <see cref="RfqService.GenerateResponses"/> and <see cref="IntercolonyPricing.BaseValue"/> —
    /// rather than reimplementing them, so a change to any of those shows up here instead of being
    /// hidden by a parallel copy of the arithmetic.
    ///
    /// Nothing it does is committed to world state. Opportunities are generated into a local list
    /// and discarded; the RFQ probe quotes a request that is never added. The one lasting effect is
    /// that entity IDs are consumed, which is harmless because they are opaque and monotonic.
    /// </summary>
    public static class IntercolonyMarketBaseline
    {
        /// <summary>
        /// Synthetic market cycles sampled. Large enough that a 35% posting chance per settlement
        /// per cycle averages out, small enough to run instantly.
        /// </summary>
        public const int DefaultRefreshSamples = 20;

        /// <summary>Exact goods probed per category on the procurement side.</summary>
        private const int ProbesPerCategory = 2;

        public static string Run(
            IntercolonyWorldComponent state, int refreshSamples = DefaultRefreshSamples)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== Intercolony 0.9.3 market baseline ===");

            if (state == null)
            {
                sb.AppendLine("No world state. Load a game first.");
                return sb.ToString();
            }

            List<SettlementSample> settlements = CollectSettlements(state);
            if (settlements.Count == 0)
            {
                sb.AppendLine("No accessible settlements with profiles. Nothing to measure.");
                return sb.ToString();
            }

            sb.AppendLine($"economy seed {state.EconomySeed}   refresh count {state.RefreshCount}   " +
                          $"accessible settlements {settlements.Count}   cycles sampled {refreshSamples}");
            sb.AppendLine();

            AppendSettlements(sb, settlements);

            List<MarketOpportunity> sample = SampleOpportunities(state, settlements, refreshSamples);
            AppendOpportunityTotals(sb, sample, settlements.Count, refreshSamples);
            AppendByArchetype(sb, state, sample, settlements, refreshSamples);
            AppendCategories(sb, sample);
            AppendTopGoods(sb, sample);
            AppendLotsAndPrices(sb, sample);
            AppendDeterminism(sb, state, settlements, refreshSamples, sample);
            AppendProcurement(sb, state, settlements.Count, sample);

            return sb.ToString();
        }

        // --- Settlements -------------------------------------------------------------------

        private sealed class SettlementSample
        {
            public Settlement settlement;
            public SettlementEconomicProfile profile;
            public float distance;
        }

        private static List<SettlementSample> CollectSettlements(IntercolonyWorldComponent state)
        {
            List<SettlementSample> result = new List<SettlementSample>();
            List<Settlement> all = Find.WorldObjects?.Settlements;
            if (all == null)
            {
                return result;
            }

            foreach (Settlement settlement in all)
            {
                if (!IntercolonyMarketAccess.IsAccessible(settlement))
                {
                    continue;
                }

                SettlementEconomicProfile profile = state.GetProfile(settlement);
                if (profile == null)
                {
                    continue;
                }

                result.Add(new SettlementSample
                {
                    settlement = settlement,
                    profile = profile,
                    distance = MarketOpportunityGenerator.DistanceToPlayer(settlement)
                });
            }

            // Stable order, so two runs of the report line up row for row.
            result.Sort((a, b) => a.settlement.ID.CompareTo(b.settlement.ID));
            return result;
        }

        private static void AppendSettlements(StringBuilder sb, List<SettlementSample> settlements)
        {
            sb.AppendLine("-- settlements --");
            sb.AppendLine("  id     archetype     wealth       tech          dist   sells");
            foreach (SettlementSample s in settlements)
            {
                sb.AppendLine(
                    $"  {s.settlement.ID,-6} {s.profile.archetype,-13} {s.profile.wealthTier,-12} " +
                    $"{s.profile.techTier,-13} {s.distance,5:F0}  {s.profile.StrongestSupply.Label()}");
            }

            sb.AppendLine();
        }

        // --- Opportunity generation --------------------------------------------------------

        /// <summary>
        /// Generates against synthetic cycle numbers past the world's real one, so sampling cannot
        /// reproduce — and therefore cannot be confused with — the offers currently on the market.
        ///
        /// Every cycle is sampled with <c>existingCount: 0</c>. That measures the generator's own
        /// output rate rather than the steady state, because the live cap interacts with expiry and
        /// with what the player accepts; simulating that here would mean reimplementing
        /// <c>DoRefresh</c> and measuring the copy. The cap is reported separately.
        /// </summary>
        private static List<MarketOpportunity> SampleOpportunities(
            IntercolonyWorldComponent state, List<SettlementSample> settlements, int refreshSamples)
        {
            List<MarketOpportunity> created = new List<MarketOpportunity>();
            int syntheticId = 1;
            int firstCycle = state.RefreshCount + 1;

            for (int cycle = firstCycle; cycle < firstCycle + refreshSamples; cycle++)
            {
                foreach (SettlementSample s in settlements)
                {
                    created.AddRange(MarketOpportunityGenerator.GenerateFor(
                        s.settlement,
                        s.profile,
                        state.EconomySeed,
                        cycle,
                        existingCount: 0,
                        idAllocator: () => syntheticId++));
                }
            }

            return created;
        }

        private static void AppendOpportunityTotals(
            StringBuilder sb, List<MarketOpportunity> sample, int settlementCount, int refreshSamples)
        {
            sb.AppendLine("-- offer generation (appetite, not what the player sees) --");
            float perCycle = sample.Count / (float)refreshSamples;
            float perSettlementCycle = sample.Count / (float)(refreshSamples * settlementCount);
            int ceiling = IntercolonyWorldComponent.MaxLiveOpportunities;
            sb.AppendLine($"  offers generated       {sample.Count}");
            sb.AppendLine($"  per cycle              {perCycle:F2}");
            sb.AppendLine($"  per settlement/cycle   {perSettlementCycle:F3}");
            sb.AppendLine($"  per-settlement cap     {MarketOpportunityGenerator.MaxPerSettlement} " +
                          "outstanding (not applied above)");
            sb.AppendLine($"  GLOBAL LIVE CEILING    {ceiling}   <- what actually reaches the market");
            sb.AppendLine();
            sb.AppendLine($"  The figures above are what the generator *wants* to post. The market is");
            sb.AppendLine($"  ceiling-bound, not generator-bound: GenerateOpportunities stops at");
            sb.AppendLine($"  {ceiling} live offers however many settlements ask. On this world the");
            sb.AppendLine($"  generator's appetite is ~{perCycle:F0}/cycle against a ceiling of {ceiling}, so");
            sb.AppendLine($"  roughly {(perCycle <= 0f ? 0f : 100f - Mathf.Min(100f, ceiling / perCycle * 100f)):F0}% of what it offers is never listed.");
            sb.AppendLine();
            sb.AppendLine("  Appetite is still the right thing to measure for Stage 2: the ceiling");
            sb.AppendLine("  would hide a generator that had stopped working until the moment it fell");
            sb.AppendLine("  below the cap. But do not read these as market size.");
            sb.AppendLine();
        }

        private static void AppendByArchetype(
            StringBuilder sb,
            IntercolonyWorldComponent state,
            List<MarketOpportunity> sample,
            List<SettlementSample> settlements,
            int refreshSamples)
        {
            Dictionary<IntercolonyArchetype, int> settlementsBy =
                new Dictionary<IntercolonyArchetype, int>();
            Dictionary<int, IntercolonyArchetype> archetypeById =
                new Dictionary<int, IntercolonyArchetype>();

            foreach (SettlementSample s in settlements)
            {
                IntercolonyArchetype archetype = s.profile.archetype;
                settlementsBy.TryGetValue(archetype, out int count);
                settlementsBy[archetype] = count + 1;
                archetypeById[s.settlement.ID] = archetype;
            }

            Dictionary<IntercolonyArchetype, int> offersBy =
                new Dictionary<IntercolonyArchetype, int>();
            foreach (MarketOpportunity opportunity in sample)
            {
                if (!archetypeById.TryGetValue(opportunity.settlementId, out IntercolonyArchetype a))
                {
                    continue;
                }

                offersBy.TryGetValue(a, out int count);
                offersBy[a] = count + 1;
            }

            sb.AppendLine("-- by archetype --");
            sb.AppendLine("  archetype      settlements   offers   per settlement/cycle");
            foreach (IntercolonyArchetype archetype in
                     (IntercolonyArchetype[])Enum.GetValues(typeof(IntercolonyArchetype)))
            {
                if (!settlementsBy.TryGetValue(archetype, out int settlementCount))
                {
                    continue;
                }

                offersBy.TryGetValue(archetype, out int offers);
                float rate = offers / (float)(refreshSamples * settlementCount);
                sb.AppendLine($"  {archetype,-14} {settlementCount,11}   {offers,6}   {rate,20:F3}");
            }

            sb.AppendLine();
        }

        private static void AppendCategories(StringBuilder sb, List<MarketOpportunity> sample)
        {
            Dictionary<IntercolonyProductCategory, int> counts =
                new Dictionary<IntercolonyProductCategory, int>();
            int classified = 0;
            foreach (MarketOpportunity opportunity in sample)
            {
                IntercolonyProductCategory? category =
                    IntercolonyProductClassifier.Classify(opportunity.thingDef);
                if (!category.HasValue)
                {
                    continue;
                }

                classified++;
                counts.TryGetValue(category.Value, out int count);
                counts[category.Value] = count + 1;
            }

            sb.AppendLine("-- demand by category --");
            foreach (IntercolonyProductCategory category in IntercolonyProductCategoryUtility.All)
            {
                counts.TryGetValue(category, out int count);
                float share = classified == 0 ? 0f : count / (float)classified * 100f;
                sb.AppendLine($"  {category.Label(),-16} {count,5}   {share,5:F1}%");
            }

            sb.AppendLine();
        }

        private static void AppendTopGoods(StringBuilder sb, List<MarketOpportunity> sample)
        {
            Dictionary<string, int> byGood = new Dictionary<string, int>();
            foreach (MarketOpportunity opportunity in sample)
            {
                string label = opportunity.thingDef?.defName ?? "(none)";
                byGood.TryGetValue(label, out int count);
                byGood[label] = count + 1;
            }

            List<KeyValuePair<string, int>> ordered =
                new List<KeyValuePair<string, int>>(byGood);
            ordered.Sort((a, b) =>
            {
                int byCount = b.Value.CompareTo(a.Value);
                return byCount != 0 ? byCount : string.CompareOrdinal(a.Key, b.Key);
            });

            sb.AppendLine($"-- exact-good turnover -- ({byGood.Count} distinct goods)");
            int shown = Mathf.Min(15, ordered.Count);
            for (int i = 0; i < shown; i++)
            {
                sb.AppendLine($"  {ordered[i].Key,-28} {ordered[i].Value,4}");
            }

            sb.AppendLine();
        }

        private static void AppendLotsAndPrices(StringBuilder sb, List<MarketOpportunity> sample)
        {
            if (sample.Count == 0)
            {
                sb.AppendLine("-- lots and prices --");
                sb.AppendLine("  no offers sampled");
                sb.AppendLine();
                return;
            }

            int minLot = int.MaxValue;
            int maxLot = 0;
            long lotTotal = 0;

            float minFactor = float.MaxValue;
            float maxFactor = 0f;
            double factorTotal = 0;
            int priced = 0;

            foreach (MarketOpportunity opportunity in sample)
            {
                minLot = Mathf.Min(minLot, opportunity.quantity);
                maxLot = Mathf.Max(maxLot, opportunity.quantity);
                lotTotal += opportunity.quantity;

                // The factor is the offered price over the item's own base value, taken from the
                // pricing owner rather than recomputed, so this tracks whatever pricing does next.
                float baseValue = IntercolonyPricing.BaseValue(
                    opportunity.thingDef, opportunity.stuffDef);
                if (baseValue <= 0f)
                {
                    continue;
                }

                float factor = opportunity.unitPrice / baseValue;
                minFactor = Mathf.Min(minFactor, factor);
                maxFactor = Mathf.Max(maxFactor, factor);
                factorTotal += factor;
                priced++;
            }

            sb.AppendLine("-- lots and prices --");
            sb.AppendLine($"  lot size          min {minLot}   mean {lotTotal / (double)sample.Count:F1}   max {maxLot}");
            if (priced > 0)
            {
                sb.AppendLine($"  unit price factor min {minFactor:F2}   mean {factorTotal / priced:F2}   " +
                              $"max {maxFactor:F2}   (offered price / base value, n={priced})");
            }
            else
            {
                sb.AppendLine("  unit price factor  no offer had a positive base value");
            }

            int withQuality = 0;
            int withStuff = 0;
            int withCondition = 0;
            int pickup = 0;
            foreach (MarketOpportunity opportunity in sample)
            {
                if (opportunity.minQuality.HasValue) withQuality++;
                if (opportunity.stuffDef != null) withStuff++;
                if (opportunity.HasConditionConstraint) withCondition++;
                if (opportunity.fulfillment == FulfillmentMode.BuyerPickup) pickup++;
            }

            sb.AppendLine($"  constrained       quality {Share(withQuality, sample.Count)}   " +
                          $"material {Share(withStuff, sample.Count)}   " +
                          $"condition {Share(withCondition, sample.Count)}");
            sb.AppendLine($"  buyer pickup      {Share(pickup, sample.Count)}");
            sb.AppendLine();
        }

        /// <summary>
        /// Proves the sample is reproducible before anyone relies on it as a baseline. A figure
        /// that moves between two runs of the same seed is not evidence of anything.
        /// </summary>
        private static void AppendDeterminism(
            StringBuilder sb,
            IntercolonyWorldComponent state,
            List<SettlementSample> settlements,
            int refreshSamples,
            List<MarketOpportunity> first)
        {
            List<MarketOpportunity> second = SampleOpportunities(state, settlements, refreshSamples);
            bool identical = first.Count == second.Count;
            if (identical)
            {
                for (int i = 0; i < first.Count; i++)
                {
                    if (first[i].settlementId != second[i].settlementId ||
                        first[i].thingDef != second[i].thingDef ||
                        first[i].quantity != second[i].quantity ||
                        !Mathf.Approximately(first[i].unitPrice, second[i].unitPrice))
                    {
                        identical = false;
                        break;
                    }
                }
            }

            sb.AppendLine("-- determinism --");
            sb.AppendLine(identical
                ? "  PASS  a second sample of the same cycles is identical"
                : "  FAIL  resampling the same cycles produced different offers");
            sb.AppendLine();
        }

        // --- Procurement -------------------------------------------------------------------

        /// <summary>
        /// Quotes a throwaway request per probe good against the real supplier logic.
        ///
        /// Only the current market window can be measured: quote seeding reads
        /// <c>state.RefreshCount</c>, and advancing that would mean running real refreshes on the
        /// player's world. So this is a snapshot of one window rather than an average over several,
        /// and the report says so rather than implying more.
        /// </summary>
        private static void AppendProcurement(
            StringBuilder sb,
            IntercolonyWorldComponent state,
            int settlementCount,
            List<MarketOpportunity> sample)
        {
            List<ThingDef> probes = PickProbeGoods(sample);

            sb.AppendLine($"-- procurement, refresh window {state.RefreshCount} only --");
            sb.AppendLine("  probes are the most-demanded goods per category in the sample above");
            if (probes.Count == 0)
            {
                sb.AppendLine("  no classifiable demanded goods found");
                return;
            }

            sb.AppendLine("  good                         quotes  full  part  offered   mean price");

            int totalProbes = 0;
            int answered = 0;
            int fullQuotes = 0;
            int partialQuotes = 0;

            foreach (ThingDef def in probes)
            {
                const int requested = 50;
                PurchaseRequest probe = new PurchaseRequest
                {
                    thingDef = def,
                    quantityRequested = requested,
                    desiredDays = 10,
                    fulfillmentPreference = ProcurementFulfillmentPreference.Either,
                    minQuality = null,
                    stuffDef = null
                };

                RfqService.GenerateResponses(state, probe);

                int full = 0;
                int partial = 0;
                int offeredTotal = 0;
                double priceTotal = 0;
                foreach (Quotation quote in probe.quotes)
                {
                    if (quote.quantityOffered >= requested) full++;
                    else partial++;
                    offeredTotal += quote.quantityOffered;
                    priceTotal += quote.unitPrice;
                }

                totalProbes++;
                if (probe.quotes.Count > 0) answered++;
                fullQuotes += full;
                partialQuotes += partial;

                string meanPrice = probe.quotes.Count == 0
                    ? "-"
                    : (priceTotal / probe.quotes.Count).ToString("F2");
                sb.AppendLine(
                    $"  {def.defName,-28} {probe.quotes.Count,6}  {full,4}  {partial,4}  " +
                    $"{offeredTotal,7}   {meanPrice,10}");
            }

            sb.AppendLine();
            sb.AppendLine($"  goods probed           {totalProbes} (asking {50} units each)");
            sb.AppendLine($"  answered by anyone     {Share(answered, totalProbes)}");
            sb.AppendLine($"  full vs partial quotes {fullQuotes} full, {partialQuotes} partial");
            sb.AppendLine($"  suppliers considered   {settlementCount} accessible settlements");
        }

        /// <summary>
        /// Picks the most-demanded goods per category out of the sampled offers.
        ///
        /// The first version took the alphabetically first classifiable def per category, which
        /// filled the basket with `AncientAPC`, `AncientBandNode` and `AncientCryptosleepCasket` —
        /// ruins scenery nobody trades. Measuring supply for goods no demand ever asks about tells
        /// us nothing about whether procurement still works.
        ///
        /// Ranking by observed demand keeps both halves of the report describing one economy, stays
        /// deterministic because the sample is, and needs no hardcoded vanilla list. Ties break on
        /// defName so two runs probe the same goods.
        /// </summary>
        private static List<ThingDef> PickProbeGoods(List<MarketOpportunity> sample)
        {
            Dictionary<IntercolonyProductCategory, Dictionary<ThingDef, int>> demand =
                new Dictionary<IntercolonyProductCategory, Dictionary<ThingDef, int>>();

            foreach (MarketOpportunity opportunity in sample)
            {
                if (opportunity.thingDef == null)
                {
                    continue;
                }

                IntercolonyProductCategory? category =
                    IntercolonyProductClassifier.Classify(opportunity.thingDef);
                if (!category.HasValue)
                {
                    continue;
                }

                if (!demand.TryGetValue(category.Value, out Dictionary<ThingDef, int> counts))
                {
                    counts = new Dictionary<ThingDef, int>();
                    demand[category.Value] = counts;
                }

                counts.TryGetValue(opportunity.thingDef, out int seen);
                counts[opportunity.thingDef] = seen + 1;
            }

            List<ThingDef> probes = new List<ThingDef>();
            foreach (IntercolonyProductCategory category in IntercolonyProductCategoryUtility.All)
            {
                if (!demand.TryGetValue(category, out Dictionary<ThingDef, int> counts))
                {
                    continue;
                }

                List<KeyValuePair<ThingDef, int>> ranked =
                    new List<KeyValuePair<ThingDef, int>>(counts);
                ranked.Sort((a, b) =>
                {
                    int byCount = b.Value.CompareTo(a.Value);
                    return byCount != 0
                        ? byCount
                        : string.CompareOrdinal(a.Key.defName, b.Key.defName);
                });

                int take = Mathf.Min(ProbesPerCategory, ranked.Count);
                for (int i = 0; i < take; i++)
                {
                    probes.Add(ranked[i].Key);
                }
            }

            return probes;
        }

        private static string Share(int count, int total)
        {
            float percent = total == 0 ? 0f : count / (float)total * 100f;
            return $"{count}/{total} ({percent:F0}%)";
        }
    }
}
