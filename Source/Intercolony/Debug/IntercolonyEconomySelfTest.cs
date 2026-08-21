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
    /// Self-test for persisted market pressure (the 1.0 program Stage 2A).
    ///
    /// The claim under test is narrow and worth stating exactly: pressure is sparse, absence means
    /// neutral, a disturbed settlement keeps its record across save and load, and a settled one
    /// stops costing anything.
    /// </summary>
    public static class IntercolonyEconomySelfTest
    {
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
                sb.AppendLine($"  SKIPPED  {label}  ({detail})");
            }
        }

        public static string Run(IntercolonyWorldComponent state)
        {
            Results r = new Results();
            r.sb.AppendLine("Market pressure and effective economy self-test (the 1.0 program Stage 2A/2B/2.2)");

            if (state == null)
            {
                r.sb.AppendLine("  No world state available. Open or load a game first.");
                return Summarize(r);
            }

            // Contents, not count. Pruning removes arbitrary entries rather than trailing ones,
            // so restoring by length would leave the test's synthetic pressure in place of the
            // player's real pressure - the same defect the timeline guard exists to prevent.
            List<SettlementMarketState> saved =
                new List<SettlementMarketState>(state.MarketStates);

            try
            {
                CheckSparseDefaults(r, state);
                CheckPruning(r, state);
                CheckScribeRoundTrip(r);
                CheckShortSaveIsPaddedNeutral(r);
                CheckReversion(r);
                CheckReversionIsDrivenByElapsedCycles(r);
                CheckShockBounds(r, state);
                CheckCompletedTradeNudges(r, state);
                CheckTradeNudgeFormula(r, state);
                CheckEconomicChainStability(r);
                CheckEconomicChainDirection(r, state);
                CheckEconomicChainSupplyDirectionAndOneHop(r, state);
                CheckEconomicChainConvergence(r, state);
                CheckEconomicChainBounds(r, state);
                CheckRegionalDiffusion(r, state);
                CheckReversionSettlesAndPrunes(r, state);
                CheckEffectiveEconomyIsBaselineWhenUndisturbed(r, state);
                CheckEffectiveEconomyReadsAreFree(r, state);
                CheckEffectiveDemandFollowsPressure(r, state);
                CheckEffectiveSupplyInvertsScarcity(r, state);
                CheckEffectiveEconomyBounds(r, state);
                CheckEffectiveEconomyExplanations(r, state);
                CheckPricingExplainsEffectiveDemand(r, state);
                CheckSyntheticProfilesIgnoreMarketPressure(r, state);
            }
            catch (Exception ex)
            {
                r.sb.AppendLine($"  EXCEPTION: {ex}");
                r.failed++;
            }
            finally
            {
                state.MarketStates.Clear();
                state.MarketStates.AddRange(saved);
                state.PruneNeutralMarketStates();
                state.RefreshMarketStateIndex();
                r.sb.AppendLine($"        market states restored to {state.MarketStates.Count}.");
            }

            return Summarize(r);
        }

        private const int ProbeSettlementId = 971_101;

        private static void CheckCompletedTradeNudges(Results r, IntercolonyWorldComponent state)
        {
            ClearProbe(state);
            ThingDef def = ThingDefOf.WoodLog;
            IntercolonyProductCategory? category = IntercolonyProductClassifier.Classify(def);
            using (new IntercolonyDiagnosticGuard(state))
            {
                SalesOrder sale = new SalesOrder
                {
                    id = -971101,
                    settlementId = ProbeSettlementId,
                    settlementName = "economy probe",
                    line = new OrderLine(def, 1),
                    status = SalesOrderStatus.Accepted,
                    deliveredQuantity = 1,
                    paidSilver = 1000
                };
                SalesOrderService.Complete(state, sale, GenTicks.TicksGame, "probe");
                SettlementMarketState afterSale = state.MarketStateFor(ProbeSettlementId);
                bool otherCategoriesNeutral = true;
                if (afterSale != null && category.HasValue)
                {
                    for (int i = 0; i < afterSale.demandPressure.Length; i++)
                    {
                        if (i != (int)category.Value &&
                            !Mathf.Approximately(
                                afterSale.demandPressure[i], SettlementMarketState.Neutral))
                        {
                            otherCategoriesNeutral = false;
                        }
                    }
                }

                r.Check(
                    category.HasValue && afterSale != null &&
                    afterSale.DemandPressureFor(category.Value) < SettlementMarketState.Neutral &&
                    otherCategoriesNeutral,
                    "a completed sale lowers demand pressure only for its category",
                    category.HasValue && afterSale != null
                        ? afterSale.DemandPressureFor(category.Value).ToString("F6")
                        : "completion did not create pressure");

                ClearProbe(state);
                PurchaseOrder purchase = new PurchaseOrder
                {
                    id = -971102,
                    settlementId = ProbeSettlementId,
                    settlementName = "economy probe",
                    thingDef = def,
                    quantity = 1,
                    paidSilver = 1000,
                    status = PurchaseOrderStatus.Confirmed
                };
                PurchaseOrderService.Complete(purchase, "probe");
                SettlementMarketState afterPurchase = state.MarketStateFor(ProbeSettlementId);
                r.Check(
                    category.HasValue && afterPurchase != null &&
                    afterPurchase.SupplyPressureFor(category.Value) > SettlementMarketState.Neutral,
                    "a completed purchase raises supply pressure toward scarce",
                    category.HasValue && afterPurchase != null
                        ? afterPurchase.SupplyPressureFor(category.Value).ToString("F6")
                        : "completion did not create pressure");

                ClearProbe(state);
                SalesOrder unresolved = new SalesOrder
                {
                    id = -971103,
                    settlementId = ProbeSettlementId,
                    settlementName = "economy probe",
                    line = new OrderLine(new ThingDef(), 1),
                    status = SalesOrderStatus.Accepted,
                    deliveredQuantity = 1,
                    paidSilver = 1000
                };
                SalesOrderService.Complete(state, unresolved, GenTicks.TicksGame, "probe");
                r.Check(state.MarketStateFor(ProbeSettlementId) == null,
                    "an unresolved category does not change pressure");
            }

            state.CommercialHistory.RemoveAll(h => h != null && h.settlementId == ProbeSettlementId);
            state.Reputations.Remove(ProbeSettlementId);
            ClearProbe(state);
        }

        private static void CheckTradeNudgeFormula(Results r, IntercolonyWorldComponent state)
        {
            const IntercolonyProductCategory Category = IntercolonyProductCategory.Commodities;
            const float Value = 10_000f;

            ClearProbe(state);
            float single = MarketPressureService.NudgeDemandDown(
                state, ProbeSettlementId, Category, Value);
            ClearProbe(state);
            float split = SettlementMarketState.Neutral;
            for (int i = 0; i < 10; i++)
            {
                split = MarketPressureService.NudgeDemandDown(
                    state, ProbeSettlementId, Category, Value / 10f);
            }
            r.Check(Mathf.Abs(single - split) < 0.00001f,
                "one trade and ten equal splits compose to the same pressure",
                $"single {single:F6}, split {split:F6}");

            ClearProbe(state);
            float tiny = MarketPressureService.NudgeDemandDown(
                state, ProbeSettlementId, Category, 1f);
            r.Check(Mathf.Abs(tiny - SettlementMarketState.Neutral) < 0.0001f,
                "a tiny trade moves pressure only negligibly",
                tiny.ToString("F6"));

            ClearProbe(state);
            float enormous = MarketPressureService.NudgeSupplyUp(
                state, ProbeSettlementId, Category, float.MaxValue);
            r.Check(enormous <= MarketPressureService.MaxPressure &&
                    enormous >= MarketPressureService.MinPressure,
                "an enormous trade cannot cross the pressure bound",
                enormous.ToString("F6"));
            ClearProbe(state);
        }

        private static void CheckEconomicChainStability(Results r)
        {
            // Today's two graphs are acyclic, so they cannot self-amplify. This row-sum guard is
            // for a future link that closes a cycle: even then coupling must remain weaker than
            // mean reversion, or pressure can pin the economy at its bounds and look like balance.
            float[] demandIncoming = new float[IntercolonyProductCategoryUtility.Count];
            float[] supplyIncoming = new float[IntercolonyProductCategoryUtility.Count];
            foreach (MarketPressureService.EconomicChainLink link in
                     MarketPressureService.DemandLinks)
            {
                demandIncoming[(int)link.target] += link.coefficient;
            }

            foreach (MarketPressureService.EconomicChainLink link in
                     MarketPressureService.SupplyLinks)
            {
                supplyIncoming[(int)link.target] += link.coefficient;
            }

            float maximumIncoming = 0f;
            for (int i = 0; i < demandIncoming.Length; i++)
            {
                maximumIncoming = Mathf.Max(
                    maximumIncoming, Mathf.Max(demandIncoming[i], supplyIncoming[i]));
            }

            float stabilityBound = (1f / MarketPressureService.ReversionRetention) - 1f;
            r.Check(maximumIncoming < stabilityBound,
                "economic-chain maximum incoming coefficient stays below the reversion stability bound",
                $"maximum {maximumIncoming:F5}, bound {stabilityBound:F5}");
        }

        private static void CheckEconomicChainDirection(
            Results r,
            IntercolonyWorldComponent state)
        {
            ClearProbe(state);
            try
            {
                MarketPressureService.ApplyDemandShock(
                    state, ProbeSettlementId, IntercolonyProductCategory.ManufacturedGoods, 0.40f);
                MarketPressureService.PropagateEconomicChains(state);
                SettlementMarketState record = state.MarketStateFor(ProbeSettlementId);

                r.Check(record != null &&
                        record.DemandPressureFor(IntercolonyProductCategory.IntermediateGoods) >
                        SettlementMarketState.Neutral,
                    "manufactured-goods demand raises intermediate-goods demand on propagation",
                    record == null
                        ? "no record"
                        : record.DemandPressureFor(IntercolonyProductCategory.IntermediateGoods)
                            .ToString("F5"));

                bool unlinkedUntouched = record != null;
                IntercolonyProductCategory[] unlinked =
                {
                    IntercolonyProductCategory.ManufacturedGoods,
                    IntercolonyProductCategory.Furniture,
                    IntercolonyProductCategory.CapitalEquipment,
                    IntercolonyProductCategory.ArtAndUnique
                };
                foreach (IntercolonyProductCategory category in unlinked)
                {
                    if (record != null)
                    {
                        unlinkedUntouched &= Mathf.Approximately(
                            record.DemandPressureFor(category),
                            category == IntercolonyProductCategory.ManufacturedGoods
                                ? 1.40f
                                : SettlementMarketState.Neutral);
                    }
                }

                r.Check(unlinkedUntouched,
                    "manufactured-goods demand leaves categories without its outgoing link untouched");
            }
            finally
            {
                ClearProbe(state);
            }
        }

        private static void CheckEconomicChainSupplyDirectionAndOneHop(
            Results r,
            IntercolonyWorldComponent state)
        {
            ClearProbe(state);
            try
            {
                MarketPressureService.ApplySupplyShock(
                    state, ProbeSettlementId, IntercolonyProductCategory.Commodities, 0.40f);
                MarketPressureService.PropagateEconomicChains(state);
                SettlementMarketState record = state.MarketStateFor(ProbeSettlementId);
                float intermediateAfterFirst = record.SupplyPressureFor(
                    IntercolonyProductCategory.IntermediateGoods);
                float furnitureAfterFirst = record.SupplyPressureFor(
                    IntercolonyProductCategory.Furniture);

                r.Check(intermediateAfterFirst > SettlementMarketState.Neutral,
                    "commodity scarcity raises intermediate-goods scarcity",
                    intermediateAfterFirst.ToString("F5"));
                r.Check(Mathf.Approximately(
                        furnitureAfterFirst, SettlementMarketState.Neutral),
                    "commodity scarcity does not reach furniture in the same propagation",
                    furnitureAfterFirst.ToString("F5"));

                MarketPressureService.PropagateEconomicChains(state);
                float furnitureAfterSecond = record.SupplyPressureFor(
                    IntercolonyProductCategory.Furniture);
                r.Check(furnitureAfterSecond > SettlementMarketState.Neutral,
                    "commodity scarcity reaches furniture after a second propagation",
                    furnitureAfterSecond.ToString("F5"));
            }
            finally
            {
                ClearProbe(state);
            }
        }

        private static void CheckEconomicChainConvergence(
            Results r,
            IntercolonyWorldComponent state)
        {
            ClearProbe(state);
            try
            {
                MarketPressureService.ApplySupplyShock(
                    state, ProbeSettlementId, IntercolonyProductCategory.Commodities, 0.40f);
                SettlementMarketState record = state.MarketStateFor(ProbeSettlementId);
                record.lastAdvancedRefresh = 0;
                for (int refresh = 1; refresh <= 200; refresh++)
                {
                    MarketPressureService.Advance(record, refresh);
                    MarketPressureService.PropagateEconomicChains(state);
                }

                bool allNeutral = true;
                foreach (IntercolonyProductCategory category in
                         IntercolonyProductCategoryUtility.All)
                {
                    allNeutral &= Mathf.Abs(record.DemandPressureFor(category) -
                                      SettlementMarketState.Neutral) <=
                                  SettlementMarketState.NeutralEpsilon;
                    allNeutral &= Mathf.Abs(record.SupplyPressureFor(category) -
                                      SettlementMarketState.Neutral) <=
                                  SettlementMarketState.NeutralEpsilon;
                }

                r.Check(allNeutral,
                    "a single shock and its propagated chain converge back within neutral epsilon");
            }
            finally
            {
                ClearProbe(state);
            }
        }

        private static void CheckEconomicChainBounds(
            Results r,
            IntercolonyWorldComponent state)
        {
            ClearProbe(state);
            try
            {
                MarketPressureService.ApplyDemandShock(
                    state, ProbeSettlementId, IntercolonyProductCategory.ManufacturedGoods,
                    MarketPressureService.MaxPressure);
                MarketPressureService.ApplyDemandShock(
                    state, ProbeSettlementId, IntercolonyProductCategory.IntermediateGoods,
                    MarketPressureService.MaxPressure);
                MarketPressureService.PropagateEconomicChains(state);
                float bounded = state.MarketStateFor(ProbeSettlementId).DemandPressureFor(
                    IntercolonyProductCategory.IntermediateGoods);
                r.Check(bounded <= MarketPressureService.MaxPressure &&
                        bounded >= MarketPressureService.MinPressure,
                    "chain propagation into an extreme category stays inside pressure bounds",
                    bounded.ToString("F5"));
            }
            finally
            {
                ClearProbe(state);
            }
        }

        private static void CheckRegionalDiffusion(
            Results r,
            IntercolonyWorldComponent state)
        {
            float stabilityProduct = MarketPressureService.DiffusionCoefficient *
                                     MarketPressureService.MaxNeighbours;
            r.Check(stabilityProduct <= 0.5f,
                "regional diffusion stays within the monotone stability bound",
                $"coefficient {MarketPressureService.DiffusionCoefficient:F5} * " +
                $"neighbours {MarketPressureService.MaxNeighbours} = {stabilityProduct:F5}");

            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (!TryFindDiffusionPair(settlements, out Settlement source, out Settlement near))
            {
                const string Reason = "world has no two settlements within half the diffusion radius";
                r.Skip("regional diffusion conserves the two-settlement pressure sum", Reason);
                r.Skip("a sub-epsilon transfer creates no neutral-neighbour record", Reason);
                r.Skip("regional pressure converges back within neutral epsilon", Reason);
                r.Skip("diffusion into an extreme settlement stays inside pressure bounds", Reason);
                r.Skip("near pressure spreads while pressure beyond the radius does not", Reason);
                return;
            }

            List<Settlement> pair = new List<Settlement> { source, near };
            const IntercolonyProductCategory Category =
                IntercolonyProductCategory.Commodities;

            ClearDiffusionProbes(state, pair);
            try
            {
                MarketPressureService.ApplyDemandShock(state, source.ID, Category, 0.40f);
                MarketPressureService.ApplyDemandShock(state, near.ID, Category, -0.20f);
                float before = state.MarketStateFor(source.ID).DemandPressureFor(Category) +
                               state.MarketStateFor(near.ID).DemandPressureFor(Category) -
                               2f * SettlementMarketState.Neutral;
                MarketPressureService.DiffuseRegionalPressure(state, pair);
                float after = state.MarketStateFor(source.ID).DemandPressureFor(Category) +
                              state.MarketStateFor(near.ID).DemandPressureFor(Category) -
                              2f * SettlementMarketState.Neutral;
                r.Check(Mathf.Abs(after - before) < 0.00001f,
                    "regional diffusion conserves the two-settlement pressure sum",
                    $"before {before:F6}, after {after:F6}");
            }
            finally
            {
                ClearDiffusionProbes(state, pair);
            }

            try
            {
                float distance = Find.WorldGrid.ApproxDistanceInTiles(source.Tile, near.Tile);
                float weight = 1f - distance / MarketPressureService.MaxDiffusionRadius;
                float tinyShock = SettlementMarketState.NeutralEpsilon /
                                  (MarketPressureService.DiffusionCoefficient * weight) * 0.5f;
                MarketPressureService.ApplyDemandShock(state, source.ID, Category, tinyShock);
                MarketPressureService.DiffuseRegionalPressure(state, pair);
                r.Check(state.MarketStateFor(near.ID) == null,
                    "a sub-epsilon transfer creates no neutral-neighbour record",
                    $"distance {distance:F2}, transfer " +
                    $"{tinyShock * MarketPressureService.DiffusionCoefficient * weight:F6}");
            }
            finally
            {
                ClearDiffusionProbes(state, pair);
            }

            try
            {
                MarketPressureService.ApplySupplyShock(state, source.ID, Category, 0.40f);
                MarketPressureService.DiffuseRegionalPressure(state, pair);
                foreach (Settlement settlement in pair)
                {
                    state.MarketStateFor(settlement.ID).lastAdvancedRefresh = 0;
                }
                for (int refresh = 1; refresh <= 200; refresh++)
                {
                    List<SettlementMarketState> records =
                        new List<SettlementMarketState>(state.MarketStates);
                    foreach (SettlementMarketState record in records)
                    {
                        MarketPressureService.Advance(record, refresh);
                    }
                    MarketPressureService.PropagateEconomicChains(state);
                    MarketPressureService.DiffuseRegionalPressure(state, pair);
                }

                bool neutral = true;
                foreach (Settlement settlement in pair)
                {
                    SettlementMarketState record = state.MarketStateFor(settlement.ID);
                    foreach (IntercolonyProductCategory category in
                             IntercolonyProductCategoryUtility.All)
                    {
                        neutral &= Mathf.Abs(record.DemandPressureFor(category) -
                                             SettlementMarketState.Neutral) <=
                                   SettlementMarketState.NeutralEpsilon;
                        neutral &= Mathf.Abs(record.SupplyPressureFor(category) -
                                             SettlementMarketState.Neutral) <=
                                   SettlementMarketState.NeutralEpsilon;
                    }
                }
                r.Check(neutral,
                    "regional pressure converges back within neutral epsilon");
            }
            finally
            {
                ClearDiffusionProbes(state, pair);
            }

            try
            {
                SettlementMarketState sourceRecord =
                    state.MarketStateFor(source.ID, createIfMissing: true);
                SettlementMarketState nearRecord =
                    state.MarketStateFor(near.ID, createIfMissing: true);
                sourceRecord.demandPressure[(int)Category] =
                    MarketPressureService.MaxPressure + 1f;
                nearRecord.demandPressure[(int)Category] = MarketPressureService.MaxPressure;
                MarketPressureService.DiffuseRegionalPressure(state, pair);
                float bounded = nearRecord.DemandPressureFor(Category);
                r.Check(bounded <= MarketPressureService.MaxPressure &&
                        bounded >= MarketPressureService.MinPressure,
                    "diffusion into an extreme settlement stays inside pressure bounds",
                    bounded.ToString("F5"));
            }
            finally
            {
                ClearDiffusionProbes(state, pair);
            }

            if (!TryFindDistantSettlement(settlements, source, out Settlement distant))
            {
                r.Skip("near pressure spreads while pressure beyond the radius does not",
                    "no settlement lies beyond the diffusion radius from the selected source");
                return;
            }

            List<Settlement> geometry = new List<Settlement> { source, near, distant };
            ClearDiffusionProbes(state, geometry);
            try
            {
                MarketPressureService.ApplyDemandShock(state, source.ID, Category, 0.40f);
                MarketPressureService.DiffuseRegionalPressure(state, geometry);
                SettlementMarketState nearRecord = state.MarketStateFor(near.ID);
                SettlementMarketState distantRecord = state.MarketStateFor(distant.ID);
                r.Check(nearRecord != null &&
                        nearRecord.DemandPressureFor(Category) >
                        SettlementMarketState.Neutral && distantRecord == null,
                    "near pressure spreads while pressure beyond the radius does not",
                    $"near {Find.WorldGrid.ApproxDistanceInTiles(source.Tile, near.Tile):F2}, " +
                    $"distant {Find.WorldGrid.ApproxDistanceInTiles(source.Tile, distant.Tile):F2}");
            }
            finally
            {
                ClearDiffusionProbes(state, geometry);
            }
        }

        private static bool TryFindDiffusionPair(
            List<Settlement> settlements,
            out Settlement source,
            out Settlement near)
        {
            source = null;
            near = null;
            if (settlements == null || Find.WorldGrid == null)
            {
                return false;
            }

            List<Settlement> ordered = settlements.FindAll(s => s != null);
            ordered.Sort((a, b) => a.ID.CompareTo(b.ID));
            for (int i = 0; i < ordered.Count; i++)
            {
                for (int j = i + 1; j < ordered.Count; j++)
                {
                    float distance = Find.WorldGrid.ApproxDistanceInTiles(
                        ordered[i].Tile, ordered[j].Tile);
                    if (distance <= MarketPressureService.MaxDiffusionRadius * 0.5f)
                    {
                        source = ordered[i];
                        near = ordered[j];
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool TryFindDistantSettlement(
            List<Settlement> settlements,
            Settlement source,
            out Settlement distant)
        {
            distant = null;
            if (settlements == null || source == null || Find.WorldGrid == null)
            {
                return false;
            }

            foreach (Settlement candidate in settlements)
            {
                if (candidate != null && candidate.ID != source.ID &&
                    Find.WorldGrid.ApproxDistanceInTiles(source.Tile, candidate.Tile) >
                    MarketPressureService.MaxDiffusionRadius)
                {
                    if (distant == null || candidate.ID < distant.ID)
                    {
                        distant = candidate;
                    }
                }
            }
            return distant != null;
        }

        private static void ClearDiffusionProbes(
            IntercolonyWorldComponent state,
            List<Settlement> settlements)
        {
            HashSet<int> ids = new HashSet<int>();
            foreach (Settlement settlement in settlements)
            {
                if (settlement != null)
                {
                    ids.Add(settlement.ID);
                }
            }
            state.MarketStates.RemoveAll(s => s != null && ids.Contains(s.settlementId));
            state.RefreshMarketStateIndex();
        }

        private static void CheckSparseDefaults(Results r, IntercolonyWorldComponent state)
        {
            state.MarketStates.RemoveAll(s => s != null && s.settlementId == ProbeSettlementId);
            state.PruneNeutralMarketStates();

            r.Check(state.MarketStateFor(ProbeSettlementId) == null,
                "an undisturbed settlement stores no record");

            SettlementMarketState created = state.MarketStateFor(ProbeSettlementId, createIfMissing: true);
            r.Check(created != null, "a record can be created on demand");
            r.Check(created != null && created.IsNeutral, "a fresh record is neutral");
            r.Check(
                created != null &&
                Mathf.Approximately(
                    created.DemandPressureFor(IntercolonyProductCategory.Commodities),
                    SettlementMarketState.Neutral),
                "a fresh record reads neutral per category");

            r.Check(ReferenceEquals(created, state.MarketStateFor(ProbeSettlementId)),
                "a second lookup returns the same record rather than a duplicate");

            r.Check(state.MarketStateFor(-1, createIfMissing: true) == null,
                "an invalid settlement id never creates a record");
        }

        private static void CheckPruning(Results r, IntercolonyWorldComponent state)
        {
            SettlementMarketState probe =
                state.MarketStateFor(ProbeSettlementId, createIfMissing: true);

            probe.demandPressure[(int)IntercolonyProductCategory.Commodities] = 1.4f;
            r.Check(!probe.IsNeutral, "a disturbed record is not neutral");

            int beforeDisturbed = state.MarketStates.Count;
            state.PruneNeutralMarketStates();
            r.Check(state.MarketStates.Count == beforeDisturbed,
                "pruning keeps a disturbed record", $"{state.MarketStates.Count}");
            r.Check(state.MarketStateFor(ProbeSettlementId) != null,
                "a disturbed settlement is still reachable after pruning");

            // Just inside the epsilon: reversion approaches 1.0 asymptotically, so a record that
            // is close enough must be prunable or the sparse representation never stays sparse.
            probe.demandPressure[(int)IntercolonyProductCategory.Commodities] =
                SettlementMarketState.Neutral + SettlementMarketState.NeutralEpsilon * 0.5f;
            r.Check(probe.IsNeutral, "a record within the epsilon counts as settled");

            state.PruneNeutralMarketStates();
            r.Check(state.MarketStateFor(ProbeSettlementId) == null,
                "pruning drops a settled record");
        }

        private static void CheckScribeRoundTrip(Results r)
        {
            SettlementMarketState original = new SettlementMarketState(4242)
            {
                lastAdvancedRefresh = 17
            };
            original.demandPressure[(int)IntercolonyProductCategory.Commodities] = 1.35f;
            original.supplyPressure[(int)IntercolonyProductCategory.ManufacturedGoods] = 0.62f;

            List<SettlementMarketState> savedList =
                new List<SettlementMarketState> { original };
            List<SettlementMarketState> loadedList = null;
            string failure = null;
            string path = Path.Combine(
                Path.GetTempPath(), $"Intercolony-MarketState-{Guid.NewGuid():N}.xml");

            try
            {
                Scribe.saver.InitSaving(path, "intercolonyMarketStateTest");
                Scribe_Collections.Look(ref savedList, "marketStates", LookMode.Deep);
                Scribe.saver.FinalizeSaving();

                Scribe.loader.InitLoading(path);
                Scribe_Collections.Look(ref loadedList, "marketStates", LookMode.Deep);
                Scribe.loader.FinalizeLoading();
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                Scribe.ForceStop();
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            SettlementMarketState loaded =
                loadedList != null && loadedList.Count == 1 ? loadedList[0] : null;

            r.Check(failure == null && loaded != null,
                "market pressure survives a Scribe round trip", failure);
            r.Check(loaded != null && loaded.settlementId == 4242,
                "settlement id survives");
            r.Check(loaded != null && loaded.lastAdvancedRefresh == 17,
                "last advanced refresh survives");
            r.Check(
                loaded != null &&
                Mathf.Approximately(
                    loaded.DemandPressureFor(IntercolonyProductCategory.Commodities), 1.35f),
                "demand pressure survives per category",
                loaded?.DemandPressureFor(IntercolonyProductCategory.Commodities).ToString("F3"));
            r.Check(
                loaded != null &&
                Mathf.Approximately(
                    loaded.SupplyPressureFor(IntercolonyProductCategory.ManufacturedGoods), 0.62f),
                "supply pressure survives per category");
            r.Check(
                loaded != null &&
                Mathf.Approximately(
                    loaded.SupplyPressureFor(IntercolonyProductCategory.Commodities),
                    SettlementMarketState.Neutral),
                "an untouched category loads neutral");
        }

        /// <summary>
        /// A save written when there were fewer product categories must not come back with zeros
        /// in the new slots. Zero would read as "no demand at all", which is a shortage nobody
        /// caused, rather than "undisturbed".
        /// </summary>
        private static void CheckShortSaveIsPaddedNeutral(Results r)
        {
            SettlementMarketState state = new SettlementMarketState(55);
            bool everyCategoryNeutral = true;
            foreach (IntercolonyProductCategory category in IntercolonyProductCategoryUtility.All)
            {
                everyCategoryNeutral &=
                    Mathf.Approximately(state.DemandPressureFor(category), SettlementMarketState.Neutral) &&
                    Mathf.Approximately(state.SupplyPressureFor(category), SettlementMarketState.Neutral);
            }

            r.Check(everyCategoryNeutral, "a new record is neutral in every category");
            r.Check(state.IsNeutral, "and reports itself settled");
        }

        /// <summary>
        /// Mean reversion (the 1.0 program Stage 2B, plan §2.4). Asserts the properties the plan
        /// actually requires — direction, monotonicity, no overshoot, eventual settling — and
        /// deliberately never asserts the coefficient, which the plan calls balance tuning.
        /// </summary>
        private static void CheckReversion(Results r)
        {
            const IntercolonyProductCategory Category = IntercolonyProductCategory.Commodities;
            int index = (int)Category;

            SettlementMarketState high = new SettlementMarketState(9001) { lastAdvancedRefresh = 0 };
            high.demandPressure[index] = 1.40f;

            float previous = high.DemandPressureFor(Category);
            bool monotonic = true;
            bool stayedAbove = true;
            for (int refresh = 1; refresh <= 12; refresh++)
            {
                MarketPressureService.Advance(high, refresh);
                float now = high.DemandPressureFor(Category);
                monotonic &= now < previous;
                stayedAbove &= now > SettlementMarketState.Neutral;
                previous = now;
            }

            r.Check(monotonic, "demand pressure falls on every refresh, never rises",
                $"1.40 -> {previous:F4} over 12 refreshes");
            r.Check(stayedAbove,
                "a shortage decaying toward neutral never crosses to the other side",
                "no overshoot in 12 refreshes");

            // The same claim from below. A shock and a glut must behave alike or the market is
            // quietly biased in one direction.
            SettlementMarketState low = new SettlementMarketState(9002) { lastAdvancedRefresh = 0 };
            low.supplyPressure[index] = 0.70f;

            float previousLow = low.SupplyPressureFor(Category);
            bool risesTowardNeutral = true;
            bool stayedBelow = true;
            for (int refresh = 1; refresh <= 12; refresh++)
            {
                MarketPressureService.Advance(low, refresh);
                float now = low.SupplyPressureFor(Category);
                risesTowardNeutral &= now > previousLow;
                stayedBelow &= now < SettlementMarketState.Neutral;
                previousLow = now;
            }

            r.Check(risesTowardNeutral, "a glut rises back toward neutral on every refresh",
                $"0.70 -> {previousLow:F4}");
            r.Check(stayedBelow, "and never overshoots above neutral");

            r.Check(
                Mathf.Abs(previous - SettlementMarketState.Neutral) <
                Mathf.Abs(1.40f - SettlementMarketState.Neutral) * 0.25f,
                "a shock is substantially spent after a dozen cycles, not still at full strength",
                $"{Mathf.Abs(previous - SettlementMarketState.Neutral):F4} left of 0.40");

            // Untouched categories must not drift. A bug that reverted the whole array from a
            // wrong baseline would be invisible here unless something asserts the quiet ones.
            bool othersUntouched = true;
            foreach (IntercolonyProductCategory category in IntercolonyProductCategoryUtility.All)
            {
                if (category == Category)
                {
                    continue;
                }

                othersUntouched &=
                    Mathf.Approximately(high.DemandPressureFor(category), SettlementMarketState.Neutral) &&
                    Mathf.Approximately(high.SupplyPressureFor(category), SettlementMarketState.Neutral);
            }

            r.Check(othersUntouched, "reverting one category leaves every other exactly neutral");

            SettlementMarketState settled = new SettlementMarketState(9003) { lastAdvancedRefresh = 3 };
            MarketPressureService.Advance(settled, 3);
            r.Check(settled.IsNeutral, "advancing an undisturbed record to the same refresh is a no-op");
        }

        /// <summary>
        /// Reversion must depend on elapsed market cycles, not on how many times something happened
        /// to call it — that is the entire reason <c>lastAdvancedRefresh</c> is persisted. A save
        /// reopened ten cycles later must land where a running game would have put it.
        /// </summary>
        private static void CheckReversionIsDrivenByElapsedCycles(Results r)
        {
            const IntercolonyProductCategory Category = IntercolonyProductCategory.Commodities;
            int index = (int)Category;

            SettlementMarketState stepped = new SettlementMarketState(9101) { lastAdvancedRefresh = 0 };
            stepped.demandPressure[index] = 1.50f;
            for (int refresh = 1; refresh <= 5; refresh++)
            {
                MarketPressureService.Advance(stepped, refresh);
            }

            SettlementMarketState jumped = new SettlementMarketState(9102) { lastAdvancedRefresh = 0 };
            jumped.demandPressure[index] = 1.50f;
            MarketPressureService.Advance(jumped, 5);

            float steppedValue = stepped.DemandPressureFor(Category);
            float jumpedValue = jumped.DemandPressureFor(Category);
            r.Check(Mathf.Abs(steppedValue - jumpedValue) < 0.0001f,
                "five single-cycle advances land where one five-cycle advance lands",
                $"{steppedValue:F5} vs {jumpedValue:F5}");

            r.Check(!MarketPressureService.Advance(jumped, 5),
                "advancing to a refresh already accounted for changes nothing");
            r.Check(Mathf.Approximately(jumped.DemandPressureFor(Category), jumpedValue),
                "and leaves the value untouched");

            r.Check(!MarketPressureService.Advance(jumped, 2),
                "advancing backwards is refused rather than amplifying pressure");
            r.Check(jumped.DemandPressureFor(Category) <= jumpedValue + 0.0001f,
                "a backwards advance never increases pressure");

            // The sentinel is a state, not a small number. Subtracting from it would compute an
            // elapsed span of toRefresh + 1 and erase a fresh shock a cycle early.
            SettlementMarketState fresh = new SettlementMarketState(9103);
            fresh.demandPressure[index] = 1.50f;
            r.Check(fresh.lastAdvancedRefresh == SettlementMarketState.NeverAdvanced,
                "a new record has never been advanced");

            bool moved = MarketPressureService.Advance(fresh, 400);
            r.Check(!moved, "a never-advanced record is stamped rather than reverted");
            r.Check(Mathf.Approximately(fresh.DemandPressureFor(Category), 1.50f),
                "so a fresh shock survives its first advance at full strength",
                fresh.DemandPressureFor(Category).ToString("F4"));
            r.Check(fresh.lastAdvancedRefresh == 400,
                "and it now has a baseline to decay from");

            // A save that sat for a very long time must still land somewhere sane.
            SettlementMarketState ancient = new SettlementMarketState(9104) { lastAdvancedRefresh = 0 };
            ancient.demandPressure[index] = MarketPressureService.MaxPressure;
            MarketPressureService.Advance(ancient, 100_000);
            r.Check(ancient.IsNeutral,
                "a record advanced across an enormous gap settles rather than diverging",
                ancient.DemandPressureFor(Category).ToString("F4"));
        }

        private static void CheckShockBounds(Results r, IntercolonyWorldComponent state)
        {
            const IntercolonyProductCategory Category = IntercolonyProductCategory.ManufacturedGoods;
            state.MarketStates.RemoveAll(s => s != null && s.settlementId == ProbeSettlementId);

            float raised = MarketPressureService.ApplyDemandShock(
                state, ProbeSettlementId, Category, 0.25f);
            r.Check(raised > SettlementMarketState.Neutral,
                "a positive demand shock raises pressure", raised.ToString("F3"));

            SettlementMarketState record = state.MarketStateFor(ProbeSettlementId);
            r.Check(record != null, "shocking an undisturbed settlement creates its record");
            r.Check(record != null && record.lastAdvancedRefresh != SettlementMarketState.NeverAdvanced,
                "and stamps it, so it does not get a free cycle at full strength");

            float clampedHigh = MarketPressureService.ApplyDemandShock(
                state, ProbeSettlementId, Category, 99f);
            r.Check(Mathf.Approximately(clampedHigh, MarketPressureService.MaxPressure),
                "an absurd shock clamps to the ceiling rather than distorting the economy",
                clampedHigh.ToString("F3"));

            float clampedLow = MarketPressureService.ApplySupplyShock(
                state, ProbeSettlementId, Category, -99f);
            r.Check(Mathf.Approximately(clampedLow, MarketPressureService.MinPressure),
                "and to the floor from below", clampedLow.ToString("F3"));

            r.Check(MarketPressureService.MinPressure > 0f,
                "the floor is above zero — zero supply would mean nothing exists, not a glut");
            r.Check(
                Mathf.Approximately(
                    MarketPressureService.MinPressure * MarketPressureService.MaxPressure, 1f),
                "floor and ceiling are exact inverses, so a glut is as strong as the same shortage",
                $"{MarketPressureService.MinPressure:F4} x {MarketPressureService.MaxPressure:F2}");

            state.MarketStates.RemoveAll(s => s != null && s.settlementId == ProbeSettlementId);
            state.RefreshMarketStateIndex();
        }

        /// <summary>
        /// The two halves of the sparse design have to meet: reversion must actually reach the
        /// prune epsilon, or records accumulate forever and the save only grows.
        /// </summary>
        private static void CheckReversionSettlesAndPrunes(Results r, IntercolonyWorldComponent state)
        {
            state.MarketStates.RemoveAll(s => s != null && s.settlementId == ProbeSettlementId);

            SettlementMarketState probe =
                state.MarketStateFor(ProbeSettlementId, createIfMissing: true);
            probe.lastAdvancedRefresh = 0;
            probe.demandPressure[(int)IntercolonyProductCategory.Commodities] =
                MarketPressureService.MaxPressure;

            int refreshesToSettle = 0;
            for (int refresh = 1; refresh <= 200 && !probe.IsNeutral; refresh++)
            {
                MarketPressureService.Advance(probe, refresh);
                refreshesToSettle = refresh;
            }

            r.Check(probe.IsNeutral,
                "the strongest possible shock does eventually settle within the prune epsilon",
                $"{refreshesToSettle} refresh(es)");
            r.Check(refreshesToSettle > 5,
                "but not so fast that a shock is over before the player can trade on it",
                $"{refreshesToSettle} refresh(es)");

            state.PruneNeutralMarketStates();
            r.Check(state.MarketStateFor(ProbeSettlementId) == null,
                "and the settled record is then pruned, keeping the save sparse");
        }

        /// <summary>
        /// The profile used by the effective-economy checks. Its settlement ID is
        /// <see cref="ProbeSettlementId"/> so pressure applied to that ID reaches it — the service
        /// takes the ID off the profile rather than as a second argument precisely so the two
        /// cannot be mismatched by a caller.
        /// </summary>
        private static SettlementEconomicProfile EffectiveProbeProfile()
        {
            return SettlementProfileGenerator.GenerateFrom(
                71_009, ProbeSettlementId, 1, "EffectiveProbe", "Probes", TechLevel.Industrial);
        }

        private static void ClearProbe(IntercolonyWorldComponent state)
        {
            state.MarketStates.RemoveAll(s => s != null && s.settlementId == ProbeSettlementId);
            state.RefreshMarketStateIndex();
        }

        /// <summary>
        /// The base case, and the one that has to hold before any of the rest means anything: with
        /// nothing happening, the effective economy is the settlement's identity, unchanged.
        /// Absence of a record means neutral, so an undisturbed world behaves exactly as it did
        /// before Stage 2 existed.
        /// </summary>
        private static void CheckEffectiveEconomyIsBaselineWhenUndisturbed(
            Results r, IntercolonyWorldComponent state)
        {
            ClearProbe(state);
            SettlementEconomicProfile profile = EffectiveProbeProfile();
            const IntercolonyProductCategory Category = IntercolonyProductCategory.IntermediateGoods;

            r.Check(
                Mathf.Approximately(
                    EffectiveEconomyService.CurrentDemandPressure(state, ProbeSettlementId, Category),
                    SettlementMarketState.Neutral),
                "an undisturbed settlement reads neutral demand pressure");
            r.Check(
                Mathf.Approximately(
                    EffectiveEconomyService.CurrentSupplyPressure(state, ProbeSettlementId, Category),
                    SettlementMarketState.Neutral),
                "and neutral supply pressure");

            float baselineDemand = profile.BaseDemandFor(Category);
            float baselineSupply = profile.BaseSupplyFor(Category);
            r.Check(
                Mathf.Approximately(
                    EffectiveEconomyService.EffectiveDemand(state, profile, Category), baselineDemand),
                "effective demand equals baseline demand when nothing is happening",
                baselineDemand.ToString("F3"));
            r.Check(
                Mathf.Approximately(
                    EffectiveEconomyService.EffectiveSupply(state, profile, Category), baselineSupply),
                "effective supply equals baseline supply when nothing is happening",
                baselineSupply.ToString("F3"));

            ThingDef def = ThingDefOf.Steel;
            r.Check(def != null, "the probe good resolved");
            r.Check(
                def != null &&
                Mathf.Approximately(
                    EffectiveEconomyService.EffectiveDemand(state, profile, def, Category),
                    profile.BaseDemandFor(def, Category)),
                "and the same holds for one specific good");

            r.Check(
                Mathf.Approximately(EffectiveEconomyService.EffectiveDemand(null, profile, Category),
                    baselineDemand),
                "a null world state reads as an undisturbed one rather than throwing");
            r.Check(
                Mathf.Approximately(
                    EffectiveEconomyService.EffectiveDemand(state, null, Category), 0f),
                "a settlement with no profile is not an economic participant and demands nothing");
            r.Check(
                Mathf.Approximately(
                    EffectiveEconomyService.EffectiveSupply(state, null, Category), 0f),
                "and supplies nothing");
        }

        /// <summary>
        /// Reading the effective economy must be free of consequence.
        ///
        /// Two distinct failures are guarded here. A read that created records would put one
        /// neutral entry per settlement into the save on the first UI hover, undoing the whole
        /// point of the sparse representation. A read that advanced reversion would make a
        /// shortage decay faster the more often the player looked at it.
        /// </summary>
        private static void CheckEffectiveEconomyReadsAreFree(
            Results r, IntercolonyWorldComponent state)
        {
            ClearProbe(state);
            SettlementEconomicProfile profile = EffectiveProbeProfile();
            const IntercolonyProductCategory Category = IntercolonyProductCategory.Commodities;

            int before = state.MarketStates.Count;
            for (int i = 0; i < 5; i++)
            {
                EffectiveEconomyService.EffectiveDemand(state, profile, ThingDefOf.Steel, Category);
                EffectiveEconomyService.EffectiveSupply(state, profile, Category);
                EffectiveEconomyService.ExplainDemand(state, profile, ThingDefOf.Steel, Category);
            }

            r.Check(state.MarketStates.Count == before,
                "reading an undisturbed settlement creates no record",
                $"{before} -> {state.MarketStates.Count}");
            r.Check(state.MarketStateFor(ProbeSettlementId) == null,
                "and it is still absent from the index");

            MarketPressureService.ApplyDemandShock(state, ProbeSettlementId, Category, 0.30f);
            SettlementMarketState record = state.MarketStateFor(ProbeSettlementId);
            int stamped = record.lastAdvancedRefresh;
            float shocked = record.DemandPressureFor(Category);

            float first = EffectiveEconomyService.EffectiveDemand(state, profile, Category);
            for (int i = 0; i < 10; i++)
            {
                EffectiveEconomyService.EffectiveDemand(state, profile, Category);
            }

            r.Check(Mathf.Approximately(record.DemandPressureFor(Category), shocked),
                "ten reads do not decay a shock",
                $"{shocked:F4} -> {record.DemandPressureFor(Category):F4}");
            r.Check(record.lastAdvancedRefresh == stamped,
                "and do not re-stamp the record, so reversion stays driven by market cycles");
            r.Check(
                Mathf.Approximately(
                    EffectiveEconomyService.EffectiveDemand(state, profile, Category), first),
                "and the eleventh read returns what the first did");

            ClearProbe(state);
        }

        /// <summary>
        /// The claim the whole stage rests on: pressure reaches the number a market system will
        /// actually consume, in the right direction, in that category and no other.
        /// </summary>
        private static void CheckEffectiveDemandFollowsPressure(
            Results r, IntercolonyWorldComponent state)
        {
            ClearProbe(state);
            SettlementEconomicProfile profile = EffectiveProbeProfile();
            const IntercolonyProductCategory Shocked = IntercolonyProductCategory.ManufacturedGoods;
            const IntercolonyProductCategory Quiet = IntercolonyProductCategory.Furniture;

            float baselineShocked = profile.BaseDemandFor(Shocked);
            float baselineQuiet = profile.BaseDemandFor(Quiet);

            MarketPressureService.ApplyDemandShock(state, ProbeSettlementId, Shocked, 0.30f);
            float raised = EffectiveEconomyService.EffectiveDemand(state, profile, Shocked);

            r.Check(raised > baselineShocked,
                "a demand shock raises effective demand above the settlement's baseline",
                $"{baselineShocked:F3} -> {raised:F3}");
            r.Check(
                Mathf.Approximately(
                    EffectiveEconomyService.EffectiveDemand(state, profile, Quiet), baselineQuiet),
                "and leaves every other category exactly at its baseline");

            // The composed answer must be the baseline times the pressure and nothing else. A
            // service that quietly rescaled would still pass a directional check.
            float pressure = EffectiveEconomyService.CurrentDemandPressure(
                state, ProbeSettlementId, Shocked);
            r.Check(Mathf.Abs(raised - baselineShocked * pressure) < 0.0001f,
                "effective demand is exactly baseline x pressure",
                $"{raised:F5} vs {baselineShocked * pressure:F5}");

            // Pressure is a category-wide circumstance, not a change of taste. Two goods in the
            // same category must move by the same proportion, or a shortage would silently
            // reorder which goods the settlement prefers - the identity Stage 1 made stable.
            ThingDef a = ThingDefOf.Steel;
            ThingDef b = ThingDefOf.WoodLog;
            r.Check(a != null && b != null, "both probe goods resolved");
            if (a != null && b != null)
            {
                float ratioA = EffectiveEconomyService.EffectiveDemand(state, profile, a, Shocked) /
                               profile.BaseDemandFor(a, Shocked);
                float ratioB = EffectiveEconomyService.EffectiveDemand(state, profile, b, Shocked) /
                               profile.BaseDemandFor(b, Shocked);
                r.Check(Mathf.Abs(ratioA - ratioB) < 0.0001f,
                    "a shortage moves every good in the category by the same proportion",
                    $"{ratioA:F5} vs {ratioB:F5}");
            }

            MarketPressureService.ApplyDemandShock(state, ProbeSettlementId, Shocked, -0.60f);
            float lowered = EffectiveEconomyService.EffectiveDemand(state, profile, Shocked);
            r.Check(lowered < baselineShocked,
                "a settlement that has bought its fill demands less than its baseline",
                $"{lowered:F3}");

            ClearProbe(state);
        }

        /// <summary>
        /// The inversion, which is most of the reason a single owner exists.
        ///
        /// Supply pressure counts upward toward *scarce*; a supply weight counts upward toward
        /// *able to sell*. Multiplying them — the natural mistake, and one each consumer would
        /// make independently — turns every shortage into a glut and inverts procurement without
        /// anything looking wrong at the call site.
        /// </summary>
        private static void CheckEffectiveSupplyInvertsScarcity(
            Results r, IntercolonyWorldComponent state)
        {
            ClearProbe(state);
            SettlementEconomicProfile profile = EffectiveProbeProfile();
            const IntercolonyProductCategory Category = IntercolonyProductCategory.IntermediateGoods;
            float baseline = profile.BaseSupplyFor(Category);

            MarketPressureService.ApplySupplyShock(state, ProbeSettlementId, Category, 0.35f);
            float scarce = EffectiveEconomyService.EffectiveSupply(state, profile, Category);
            r.Check(scarce < baseline,
                "a settlement under supply pressure supplies LESS, not more",
                $"baseline {baseline:F3} -> {scarce:F3}");

            float scarcity = EffectiveEconomyService.CurrentSupplyPressure(
                state, ProbeSettlementId, Category);
            r.Check(scarcity > SettlementMarketState.Neutral,
                "the stored pressure is above neutral, so the sign really was inverted on read",
                scarcity.ToString("F3"));
            r.Check(Mathf.Abs(scarce - baseline / scarcity) < 0.0001f,
                "effective supply is exactly baseline / scarcity",
                $"{scarce:F5} vs {baseline / scarcity:F5}");

            ClearProbe(state);
            MarketPressureService.ApplySupplyShock(state, ProbeSettlementId, Category, -0.25f);
            float glut = EffectiveEconomyService.EffectiveSupply(state, profile, Category);
            r.Check(glut > baseline,
                "and a settlement with a surplus supplies more",
                $"{glut:F3}");

            ClearProbe(state);
        }

        /// <summary>
        /// The bound exists so that Stage 3's event modifier cannot stack on top of pressure into
        /// a price swing the plan rules out. Today it is headroom rather than a limit, and that
        /// relationship is what is asserted — a bound tighter than pressure's own would clip
        /// ordinary market movement and look like a balance problem.
        /// </summary>
        private static void CheckEffectiveEconomyBounds(Results r, IntercolonyWorldComponent state)
        {
            r.Check(
                Mathf.Approximately(
                    EffectiveEconomyService.MinCondition * EffectiveEconomyService.MaxCondition, 1f),
                "the condition floor and ceiling are exact inverses",
                $"{EffectiveEconomyService.MinCondition:F4} x {EffectiveEconomyService.MaxCondition:F2}");
            r.Check(EffectiveEconomyService.MaxCondition > MarketPressureService.MaxPressure,
                "the bound leaves pressure alone rather than clipping ordinary market movement",
                $"{MarketPressureService.MaxPressure:F2} < {EffectiveEconomyService.MaxCondition:F2}");
            r.Check(EffectiveEconomyService.MinCondition < MarketPressureService.MinPressure,
                "and the same from below");
            r.Check(EffectiveEconomyService.MinCondition > 0f,
                "the floor is above zero - a zero condition would erase the settlement, not depress it");

            r.Check(
                Mathf.Approximately(
                    EffectiveEconomyService.Bound(99f), EffectiveEconomyService.MaxCondition),
                "an absurd condition clamps to the ceiling");
            r.Check(
                Mathf.Approximately(
                    EffectiveEconomyService.Bound(-99f), EffectiveEconomyService.MinCondition),
                "and to the floor from below");
            r.Check(
                Mathf.Approximately(
                    EffectiveEconomyService.Bound(SettlementMarketState.Neutral),
                    SettlementMarketState.Neutral),
                "and neutral passes through untouched");

            // The strongest shock the pressure layer can produce must still arrive unclipped, or
            // the two clamps are fighting each other.
            ClearProbe(state);
            SettlementEconomicProfile profile = EffectiveProbeProfile();
            const IntercolonyProductCategory Category = IntercolonyProductCategory.Commodities;
            MarketPressureService.ApplyDemandShock(state, ProbeSettlementId, Category, 99f);

            float condition = EffectiveEconomyService.DemandCondition(state, profile, Category);
            r.Check(Mathf.Approximately(condition, MarketPressureService.MaxPressure),
                "the most extreme pressure reaches the effective layer unclipped",
                condition.ToString("F3"));

            ClearProbe(state);
        }

        /// <summary>
        /// Explanations must multiply out to the number they explain. §2.10 forbids double
        /// counting, and the way that defect arrives is a caller multiplying an effective value
        /// that already contains pressure by a factor list that contains it again — which looks
        /// correct at both sites.
        /// </summary>
        private static void CheckEffectiveEconomyExplanations(
            Results r, IntercolonyWorldComponent state)
        {
            ClearProbe(state);
            SettlementEconomicProfile profile = EffectiveProbeProfile();
            const IntercolonyProductCategory Category = IntercolonyProductCategory.ManufacturedGoods;
            ThingDef def = ThingDefOf.Steel;

            List<PriceFactor> quiet =
                EffectiveEconomyService.ExplainDemand(state, profile, def, Category);
            r.Check(quiet.Count == 1,
                "an undisturbed settlement explains its demand with one line, not a x1.00 row",
                $"{quiet.Count} line(s)");
            r.Check(Mathf.Abs(Product(quiet) -
                    EffectiveEconomyService.EffectiveDemand(state, profile, def, Category)) < 0.0001f,
                "and that line multiplies out to the effective demand");

            MarketPressureService.ApplyDemandShock(state, ProbeSettlementId, Category, 0.30f);
            List<PriceFactor> shocked =
                EffectiveEconomyService.ExplainDemand(state, profile, def, Category);
            r.Check(shocked.Count == 2, "a shortage adds exactly one named line",
                $"{shocked.Count} line(s)");
            r.Check(shocked.Count == 2 && shocked[1].label == EffectiveEconomyService.ShortageLabel,
                "labelled as a shortage when the settlement wants more than usual",
                shocked.Count == 2 ? shocked[1].label : "<missing>");
            r.Check(Mathf.Abs(Product(shocked) -
                    EffectiveEconomyService.EffectiveDemand(state, profile, def, Category)) < 0.0001f,
                "and the factors multiply to exactly the effective demand, never to more",
                $"{Product(shocked):F5}");

            MarketPressureService.ApplyDemandShock(state, ProbeSettlementId, Category, -0.60f);
            List<PriceFactor> surplus =
                EffectiveEconomyService.ExplainDemand(state, profile, def, Category);
            r.Check(surplus.Count == 2 && surplus[1].label == EffectiveEconomyService.SurplusLabel,
                "a settlement that wants less than usual is labelled a surplus",
                surplus.Count == 2 ? surplus[1].label : "<missing>");

            ClearProbe(state);

            // Supply reads from the settlement's own stock, so scarcity is labelled a shortage on
            // this side too even though it makes the multiplier fall rather than rise.
            MarketPressureService.ApplySupplyShock(state, ProbeSettlementId, Category, 0.35f);
            List<PriceFactor> supply =
                EffectiveEconomyService.ExplainSupply(state, profile, Category);
            r.Check(supply.Count == 2 && supply[1].label == EffectiveEconomyService.ShortageLabel,
                "a scarce supplier is labelled a shortage, not a surplus, despite the multiplier falling",
                supply.Count == 2 ? $"{supply[1].label} x{supply[1].multiplier:F3}" : "<missing>");
            r.Check(Mathf.Abs(Product(supply) -
                    EffectiveEconomyService.EffectiveSupply(state, profile, Category)) < 0.0001f,
                "and supply factors multiply to exactly the effective supply");

            r.Check(EffectiveEconomyService.ExplainDemand(state, null, def, Category).Count == 0,
                "a settlement with no profile explains nothing rather than throwing");

            ClearProbe(state);
        }

        /// <summary>
        /// The player sees pricing's factors rather than the effective-economy service directly,
        /// so the integration boundary is the claim that matters. These checks deliberately enter
        /// through <see cref="IntercolonyPricing.UnitPrice(IntercolonyWorldComponent, ThingDef, int, SettlementEconomicProfile, IntercolonyProductCategory, float, QualityCategory?, out List{PriceFactor})"/>
        /// and verify that its rows both name current conditions and still reconstruct the exact
        /// price. Calling the explanation service alone would stay green if pricing fused the rows
        /// again or applied pressure a second time.
        /// </summary>
        private static void CheckPricingExplainsEffectiveDemand(
            Results r, IntercolonyWorldComponent state)
        {
            ClearProbe(state);
            try
            {
                SettlementEconomicProfile profile = EffectiveProbeProfile();
                ThingDef def = ThingDefOf.Steel;
                IntercolonyProductCategory category =
                    IntercolonyProductClassifier.Classify(def).Value;

                // This assertion is about pricing's clamp reconciliation, so each fixture puts
                // the category baseline on the intended side of that clamp deterministically.
                // Depending on the generated profile or on which category Steel maps to would
                // turn the boundary cases back into accidents. The generated settlementId stays
                // untouched because pressure reaches the probe through that sentinel.
                profile.demandWeights[(int)category] = 1f;
                float quietPrice = IntercolonyPricing.UnitPrice(
                    state, def, 1, profile, category, -1f, null,
                    out List<PriceFactor> quietFactors);
                List<PriceFactor> quietDemandRows = new List<PriceFactor>();
                foreach (PriceFactor factor in quietFactors)
                {
                    if (factor.label == "Local demand" ||
                        factor.label == EffectiveEconomyService.ShortageLabel ||
                        factor.label == EffectiveEconomyService.SurplusLabel)
                    {
                        quietDemandRows.Add(factor);
                    }
                }

                r.Check(quietDemandRows.Count == 1 &&
                        quietDemandRows[0].label == "Local demand",
                    "undisturbed pricing has one Local demand row and no current-condition row",
                    $"{quietDemandRows.Count} demand row(s)");
                float quietReconstructed = IntercolonyPricing.BaseValue(def, null) *
                                           Product(quietFactors);
                r.Check(Mathf.Abs(quietReconstructed - quietPrice) <=
                        Mathf.Abs(quietPrice) * 0.0001f,
                    "undisturbed pricing factors reconstruct the returned unit price",
                    $"{quietReconstructed:F5} vs {quietPrice:F5}");
                float quietEffective = Mathf.Clamp(EffectiveEconomyService.EffectiveDemand(
                    state, profile, def, category), 0.4f, 2.0f);
                r.Check(Mathf.Abs(Product(quietDemandRows) - quietEffective) < 0.0001f,
                    "undisturbed pricing's demand rows reconstruct clamped effective demand",
                    $"{Product(quietDemandRows):F5} vs {quietEffective:F5}");

                ClearProbe(state);
                profile.demandWeights[(int)category] = 1f;
                MarketPressureService.ApplyDemandShock(
                    state, ProbeSettlementId, category, 0.30f);
                float shortagePrice = IntercolonyPricing.UnitPrice(
                    state, def, 1, profile, category, -1f, null,
                    out List<PriceFactor> shortageFactors);
                List<PriceFactor> shortageDemandRows = new List<PriceFactor>();
                foreach (PriceFactor factor in shortageFactors)
                {
                    if (factor.label == "Local demand" ||
                        factor.label == EffectiveEconomyService.ShortageLabel ||
                        factor.label == EffectiveEconomyService.SurplusLabel)
                    {
                        shortageDemandRows.Add(factor);
                    }
                }

                r.Check(shortageDemandRows.Count == 2 &&
                        shortageDemandRows[1].label == EffectiveEconomyService.ShortageLabel,
                    "pricing splits a demand shock into a named current-shortage row",
                    shortageDemandRows.Count == 2 ? shortageDemandRows[1].label : "<missing>");
                float shortageReconstructed = IntercolonyPricing.BaseValue(def, null) *
                                              Product(shortageFactors);
                r.Check(Mathf.Abs(shortageReconstructed - shortagePrice) <=
                        Mathf.Abs(shortagePrice) * 0.0001f,
                    "shocked pricing factors reconstruct the returned unit price without double counting",
                    $"{shortageReconstructed:F5} vs {shortagePrice:F5}");
                float shortageEffective = Mathf.Clamp(EffectiveEconomyService.EffectiveDemand(
                    state, profile, def, category), 0.4f, 2.0f);
                r.Check(Mathf.Abs(Product(shortageDemandRows) - shortageEffective) < 0.0001f,
                    "shocked pricing's demand rows reconstruct clamped effective demand",
                    $"{Product(shortageDemandRows):F5} vs {shortageEffective:F5}");
                float shortageCondition = EffectiveEconomyService.DemandCondition(
                    state, profile, category);
                r.Check(shortageDemandRows.Count == 2 &&
                        Mathf.Abs(shortageDemandRows[1].multiplier - shortageCondition) < 0.0001f,
                    "an unclamped shortage row retains the true demand condition",
                    shortageDemandRows.Count == 2
                        ? $"{shortageDemandRows[1].multiplier:F5} vs {shortageCondition:F5}"
                        : "<missing>");

                ClearProbe(state);
                profile.demandWeights[(int)category] = 1f;
                MarketPressureService.ApplyDemandShock(
                    state, ProbeSettlementId, category, -0.30f);
                float surplusPrice = IntercolonyPricing.UnitPrice(
                    state, def, 1, profile, category, -1f, null,
                    out List<PriceFactor> surplusFactors);
                List<PriceFactor> surplusDemandRows = new List<PriceFactor>();
                foreach (PriceFactor factor in surplusFactors)
                {
                    if (factor.label == "Local demand" ||
                        factor.label == EffectiveEconomyService.ShortageLabel ||
                        factor.label == EffectiveEconomyService.SurplusLabel)
                    {
                        surplusDemandRows.Add(factor);
                    }
                }

                r.Check(surplusDemandRows.Count == 2 &&
                        surplusDemandRows[1].label == EffectiveEconomyService.SurplusLabel,
                    "pricing labels an unclamped demand glut as a surplus, not a shortage",
                    surplusDemandRows.Count == 2 ? surplusDemandRows[1].label : "<missing>");
                float surplusReconstructed = IntercolonyPricing.BaseValue(def, null) *
                                             Product(surplusFactors);
                r.Check(Mathf.Abs(surplusReconstructed - surplusPrice) <=
                        Mathf.Abs(surplusPrice) * 0.0001f,
                    "unclamped surplus factors reconstruct the returned unit price",
                    $"{surplusReconstructed:F5} vs {surplusPrice:F5}");

                ClearProbe(state);
                profile.demandWeights[(int)category] = 0.5f;
                MarketPressureService.ApplyDemandShock(
                    state, ProbeSettlementId, category, -99f);
                float floorRawEffective = EffectiveEconomyService.EffectiveDemand(
                    state, profile, def, category);
                float floorPrice = IntercolonyPricing.UnitPrice(
                    state, def, 1, profile, category, -1f, null,
                    out List<PriceFactor> floorFactors);
                List<PriceFactor> floorDemandRows = new List<PriceFactor>();
                foreach (PriceFactor factor in floorFactors)
                {
                    if (factor.label == "Local demand" ||
                        factor.label == EffectiveEconomyService.ShortageLabel ||
                        factor.label == EffectiveEconomyService.SurplusLabel)
                    {
                        floorDemandRows.Add(factor);
                    }
                }

                r.Check(floorRawEffective < 0.4f,
                    "the floor-clamp fixture places raw effective demand below 0.4",
                    floorRawEffective.ToString("F5"));
                r.Check(floorDemandRows.Count == 2 &&
                        floorDemandRows[1].label == EffectiveEconomyService.SurplusLabel,
                    "a binding floor preserves two demand rows and the surplus label",
                    floorDemandRows.Count == 2 ? floorDemandRows[1].label : "<missing>");
                r.Check(Mathf.Abs(Product(floorDemandRows) - 0.4f) < 0.0001f,
                    "floor-clamped demand rows reconstruct 0.4",
                    Product(floorDemandRows).ToString("F5"));
                float floorReconstructed = IntercolonyPricing.BaseValue(def, null) *
                                           Product(floorFactors);
                r.Check(Mathf.Abs(floorReconstructed - floorPrice) <=
                        Mathf.Abs(floorPrice) * 0.0001f,
                    "floor-clamped factors reconstruct the returned unit price",
                    $"{floorReconstructed:F5} vs {floorPrice:F5}");

                ClearProbe(state);
                // 1.7 is deliberate rather than a rounder high weight. Even at the affinity
                // ceiling its base row is 1.7 * 1.15 = 1.955, still inside pricing's split range;
                // at the affinity floor under maximum pressure it is 1.7 * 0.85 * 1.60 = 2.312,
                // so the price ceiling binds across the entire affinity band rather than by hash.
                profile.demandWeights[(int)category] = 1.7f;
                MarketPressureService.ApplyDemandShock(
                    state, ProbeSettlementId, category, 99f);
                float ceilingPrice = IntercolonyPricing.UnitPrice(
                    state, def, 1, profile, category, -1f, null,
                    out List<PriceFactor> ceilingFactors);
                List<PriceFactor> ceilingDemandRows = new List<PriceFactor>();
                foreach (PriceFactor factor in ceilingFactors)
                {
                    if (factor.label == "Local demand" ||
                        factor.label == EffectiveEconomyService.ShortageLabel ||
                        factor.label == EffectiveEconomyService.SurplusLabel)
                    {
                        ceilingDemandRows.Add(factor);
                    }
                }

                r.Check(ceilingDemandRows.Count == 2 &&
                        ceilingDemandRows[1].label == EffectiveEconomyService.ShortageLabel,
                    "a binding ceiling preserves two demand rows and the shortage label",
                    ceilingDemandRows.Count == 2 ? ceilingDemandRows[1].label : "<missing>");
                r.Check(Mathf.Abs(Product(ceilingDemandRows) - 2.0f) < 0.0001f,
                    "ceiling-clamped demand rows reconstruct 2.0",
                    Product(ceilingDemandRows).ToString("F5"));
                float ceilingReconstructed = IntercolonyPricing.BaseValue(def, null) *
                                             Product(ceilingFactors);
                r.Check(Mathf.Abs(ceilingReconstructed - ceilingPrice) <=
                        Mathf.Abs(ceilingPrice) * 0.0001f,
                    "ceiling-clamped factors reconstruct the returned unit price",
                    $"{ceilingReconstructed:F5} vs {ceilingPrice:F5}");

                ClearProbe(state);
                profile.demandWeights[(int)category] = 2.5f;
                MarketPressureService.ApplyDemandShock(
                    state, ProbeSettlementId, category, 0.30f);
                float outsideBasePrice = IntercolonyPricing.UnitPrice(
                    state, def, 1, profile, category, -1f, null,
                    out List<PriceFactor> outsideBaseFactors);
                List<PriceFactor> outsideBaseDemandRows = new List<PriceFactor>();
                foreach (PriceFactor factor in outsideBaseFactors)
                {
                    if (factor.label == "Local demand" ||
                        factor.label == EffectiveEconomyService.ShortageLabel ||
                        factor.label == EffectiveEconomyService.SurplusLabel)
                    {
                        outsideBaseDemandRows.Add(factor);
                    }
                }

                r.Check(outsideBaseDemandRows.Count == 1 &&
                        outsideBaseDemandRows[0].label == "Local demand",
                    "a base demand outside the clamp collapses a conditioned price to one truthful row",
                    $"{outsideBaseDemandRows.Count} demand row(s)");
                r.Check(Mathf.Abs(Product(outsideBaseDemandRows) - 2.0f) < 0.0001f,
                    "the collapsed outside-base demand row reconstructs 2.0",
                    Product(outsideBaseDemandRows).ToString("F5"));
                float outsideBaseReconstructed = IntercolonyPricing.BaseValue(def, null) *
                                                 Product(outsideBaseFactors);
                r.Check(Mathf.Abs(outsideBaseReconstructed - outsideBasePrice) <=
                        Mathf.Abs(outsideBasePrice) * 0.0001f,
                    "outside-base factors reconstruct the returned unit price",
                    $"{outsideBaseReconstructed:F5} vs {outsideBasePrice:F5}");
            }
            finally
            {
                ClearProbe(state);
            }
        }

        /// <summary>
        /// Generic estimates have no settlement whose current conditions they can inherit. Their
        /// sentinel must therefore remain neutral even while a real settlement is disturbed.
        /// </summary>
        private static void CheckSyntheticProfilesIgnoreMarketPressure(
            Results r, IntercolonyWorldComponent state)
        {
            ClearProbe(state);
            SettlementEconomicProfile synthetic = new SettlementEconomicProfile();
            const IntercolonyProductCategory Category = IntercolonyProductCategory.Commodities;
            // A zero baseline stays zero under any pressure, so it cannot expose the defect.
            synthetic.demandWeights[(int)Category] = 1.20f;

            r.Check(synthetic.settlementId == -1,
                "a newly constructed profile is synthetic rather than settlement zero",
                synthetic.settlementId.ToString());

            float baseline = synthetic.BaseDemandFor(Category);
            MarketPressureService.ApplyDemandShock(
                state, ProbeSettlementId, Category, 0.30f);

            float pressure = EffectiveEconomyService.CurrentDemandPressure(
                state, synthetic.settlementId, Category);
            r.Check(Mathf.Approximately(pressure, SettlementMarketState.Neutral),
                "a synthetic profile reads neutral demand pressure despite another settlement's shock",
                pressure.ToString("F3"));

            float effective = EffectiveEconomyService.EffectiveDemand(state, synthetic, Category);
            r.Check(effective == baseline,
                "a synthetic profile's effective demand equals its baseline exactly",
                $"{baseline:F5} vs {effective:F5}");

            SettlementEconomicProfile shocked = new SettlementEconomicProfile();
            shocked.demandWeights[(int)Category] = synthetic.demandWeights[(int)Category];
            shocked.settlementId = ProbeSettlementId;
            float shockedBaseline = shocked.BaseDemandFor(Category);
            float shockedEffective = EffectiveEconomyService.EffectiveDemand(state, shocked, Category);
            r.Check(shockedEffective > shockedBaseline,
                "a profile naming the shocked settlement reads demand above its baseline",
                $"{shockedBaseline:F5} vs {shockedEffective:F5}");

            ClearProbe(state);
        }

        private static float Product(List<PriceFactor> factors)
        {
            float total = 1f;
            foreach (PriceFactor factor in factors)
            {
                total *= factor.multiplier;
            }

            return total;
        }

        private static string Summarize(Results r)
        {
            r.sb.AppendLine();
            r.sb.AppendLine($"  {r.passed} passed, {r.failed} failed, {r.skipped} skipped.");
            return r.sb.ToString();
        }
    }
}
