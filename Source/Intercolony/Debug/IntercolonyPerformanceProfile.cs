using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Stage 8C performance assertions. The benchmark state is detached from the loaded world:
    /// production entry points are exercised, but a profile run cannot add opportunities,
    /// listings, pressure, events or history to the player's save.
    /// </summary>
    public static class IntercolonyPerformanceProfile
    {
        private const int WarmupRuns = 2;
        private const int TimedSamples = 7;
        private const int FastCallsPerSample = 128;
        private const int HistoryRecordCount = 1000;

        /// <summary>
        /// The existing real-save profile measured a full refresh in single-digit milliseconds;
        /// 100 ms leaves substantial headroom for a much larger modded world and is a guard, not
        /// a target. It is intentionally independent of the measured value.
        /// </summary>
        private const double CoarseRefreshBudgetMilliseconds = 100d;

        /// <summary>
        /// Chain propagation copies bounded category arrays and has no world-geometry work;
        /// 20 ms leaves headroom over the expected low-single-digit millisecond result at the
        /// populated settlement fixture used here.
        /// </summary>
        private const double PressurePropagationBudgetMilliseconds = 20d;

        /// <summary>
        /// Regional diffusion performs bounded graph construction and world-grid distance
        /// sorting; 100 ms is deliberately generous enough to catch an accidental unbounded
        /// scan without turning this assertion into a machine-specific micro-target.
        /// </summary>
        private const double RegionalDiffusionBudgetMilliseconds = 100d;

        /// <summary>
        /// Applying one event start shock visits the eligible settlement set once; 50 ms leaves
        /// headroom for a large world while remaining small enough to catch repeated full-world
        /// work accidentally added to each category.
        /// </summary>
        private const double EventStartShockBudgetMilliseconds = 50d;

        /// <summary>
        /// Active event modifiers are read from visible-row pricing paths, so this budget is much
        /// tighter than the one-shot start shock budget. Two milliseconds leaves headroom for the
        /// bounded active-event list without accepting a per-row full-world walk.
        /// </summary>
        private const double EventActiveModifierBudgetMilliseconds = 2d;

        /// <summary>
        /// Supplier refresh builds bounded offers for every eligible settlement. 250 ms allows
        /// for a heavily modded definition set and is a regression ceiling rather than a desired
        /// refresh cost.
        /// </summary>
        private const double SupplierMarketBudgetMilliseconds = 250d;

        /// <summary>
        /// Effective brand lookup scans the sparse direct-record list and uses warmed derived
        /// similarity metadata; 2 ms per lookup leaves headroom without masking an accidental
        /// full definition-universe rebuild.
        /// </summary>
        private const double BrandEffectiveScoreBudgetMilliseconds = 2d;

        /// <summary>
        /// Relations asks for twelve presented timeline rows, even when the retained timeline is
        /// full. 25 ms leaves headroom for the required populated 1,000-record fixture while
        /// guarding against a much more expensive render-time walk or formatting regression.
        /// </summary>
        private const double HistoryRenderingBudgetMilliseconds = 25d;

        public static string Run(IntercolonyWorldComponent state, Map map)
        {
            Results result = new Results();
            result.sb.AppendLine("Stage 8C performance profile (Stopwatch median assertions)");
            result.sb.AppendLine(
                $"Warm-up runs={WarmupRuns}; timed samples={TimedSamples}; " +
                $"fast-call batch={FastCallsPerSample}; history fixture={HistoryRecordCount} records.");

            if (state == null)
            {
                result.sb.AppendLine("  No world state available. Open or load a game first.");
                return result.Summarize();
            }

            IntercolonyWorldComponent refreshState = null;
            IntercolonyWorldComponent propagationState = null;
            IntercolonyWorldComponent diffusionState = null;
            IntercolonyWorldComponent eventState = null;
            IntercolonyWorldComponent supplierState = null;
            IntercolonyWorldComponent brandState = null;
            IntercolonyWorldComponent historyState = null;
            MarketFixture propagationFixture = null;
            MarketFixture diffusionFixture = null;
            MarketFixture eventFixtureState = null;
            List<CommercialEventRecord> savedHistory = null;

            try
            {
                using (IntercolonyLog.SuppressVerbose())
                {
                    if (map?.IsPlayerHome != true)
                    {
                        result.sb.AppendLine(
                            "  FindBuyerService.ColonyStock(map)  unavailable " +
                            "(the supplied map is not a player colony).");
                        result.sb.AppendLine(
                            "  FindBuyerService.AvailableColonyStock(state, map)  unavailable " +
                            "(the supplied map is not a player colony).");
                        result.sb.AppendLine(
                            "  FindBuyerService.AvailableColonyAnimals(state, map)  unavailable " +
                            "(the supplied map is not a player colony).");
                    }
                    else
                    {
                        int allThingsCount = map.listerThings.AllThings.Count;
                        int storedThingsCount = StoredThingsCount(map);

                        List<KeyValuePair<ThingDef, int>> stock = null;
                        Timing colonyStock = Measure(
                            null,
                            () => stock = FindBuyerService.ColonyStock(map),
                            1);
                        result.Check(
                            stock != null,
                            "FindBuyerService.ColonyStock(map)",
                            TimingDetail(colonyStock,
                                $"all things={allThingsCount}; " +
                                $"stored things={storedThingsCount}; " +
                                $"distinct defs returned={stock?.Count ?? 0}"));

                        List<KeyValuePair<ThingDef, int>> availableStockResult = null;
                        Timing availableColonyStock = Measure(
                            null,
                            () => availableStockResult =
                                FindBuyerService.AvailableColonyStock(state, map),
                            1);
                        result.Check(
                            availableStockResult != null,
                            "FindBuyerService.AvailableColonyStock(state, map)",
                            TimingDetail(availableColonyStock,
                                $"all things={allThingsCount}; " +
                                $"stored things={storedThingsCount}; " +
                                $"distinct defs returned={stock?.Count ?? 0}; " +
                                $"available defs returned={availableStockResult?.Count ?? 0}"));

                        List<AnimalStockGroup> availableAnimalsResult = null;
                        Timing availableColonyAnimals = Measure(
                            null,
                            () => availableAnimalsResult =
                                FindBuyerService.AvailableColonyAnimals(state, map),
                            1);
                        result.Check(
                            availableAnimalsResult != null,
                            "FindBuyerService.AvailableColonyAnimals(state, map)",
                            TimingDetail(availableColonyAnimals,
                                $"all things={allThingsCount}; " +
                                $"stored things={storedThingsCount}; " +
                                $"distinct defs returned={stock?.Count ?? 0}; " +
                                $"animal groups returned={availableAnimalsResult?.Count ?? 0}"));
                    }

                    // The detached state still points at the loaded World, so settlement count,
                    // world-grid geometry and definition availability are real. Its persisted
                    // collections start empty and are disposable benchmark fixtures.
                    refreshState = NewDetachedState();
                    Timing refresh = Measure(
                        () => ResetDetachedState(refreshState),
                        () => refreshState.RunRefreshForPerformanceProfile(),
                        1);
                    result.Check(
                        refresh.MedianMilliseconds <= CoarseRefreshBudgetMilliseconds,
                        "coarse economy refresh",
                        TimingDetail(refresh, CoarseRefreshBudgetMilliseconds,
                            $"settlements={WorldSettlementCount()}"));

                    propagationState = NewDetachedState();
                    propagationFixture = BuildMarketFixture(propagationState);
                    Timing propagation = Measure(
                        propagationFixture.Restore,
                        () => MarketPressureService.PropagateEconomicChains(propagationState),
                        1);
                    result.Check(
                        propagation.MedianMilliseconds <= PressurePropagationBudgetMilliseconds,
                        "pressure propagation",
                        TimingDetail(propagation, PressurePropagationBudgetMilliseconds,
                            $"market records={propagationFixture.RecordCount}"));

                    diffusionState = NewDetachedState();
                    diffusionFixture = BuildMarketFixture(diffusionState);
                    Timing diffusion = Measure(
                        diffusionFixture.Restore,
                        () => MarketPressureService.DiffuseRegionalPressure(diffusionState),
                        1);
                    result.Check(
                        diffusion.MedianMilliseconds <= RegionalDiffusionBudgetMilliseconds,
                        "regional diffusion",
                        TimingDetail(diffusion, RegionalDiffusionBudgetMilliseconds,
                            $"market records={diffusionFixture.RecordCount}; " +
                            $"world settlements={WorldSettlementCount()}"));

                    eventState = NewDetachedState();
                    eventFixtureState = BuildMarketFixture(eventState);
                    EconomicEvent eventFixture = BuildEventFixture();
                    eventState.EconomicEvents.Add(eventFixture);
                    Settlement eventSettlement = FirstEligibleSettlement();
                    Timing eventStartShock = Measure(
                        eventFixtureState.Restore,
                        () => EconomicEventService.ApplyStartShock(eventState, eventFixture),
                        1);
                    float eventMultiplier = 0f;
                    Timing eventActiveModifier = Measure(
                        null,
                        () => eventMultiplier = EconomicEventService.DemandMultiplier(
                            eventState,
                            eventSettlement,
                            IntercolonyProductCategory.Commodities),
                        FastCallsPerSample);
                    result.Check(
                        eventStartShock.MedianMilliseconds <= EventStartShockBudgetMilliseconds &&
                        eventActiveModifier.MedianMilliseconds <= EventActiveModifierBudgetMilliseconds,
                        "event application",
                        $"start shock median={eventStartShock.MedianMilliseconds:F3} ms/call; " +
                        $"start-shock budget={EventStartShockBudgetMilliseconds:F3} ms/call; " +
                        $"active modifier median={eventActiveModifier.MedianMilliseconds:F3} ms/call; " +
                        $"active-modifier budget={EventActiveModifierBudgetMilliseconds:F3} ms/call; " +
                        $"samples={TimedSamples} x start={eventStartShock.CallsPerSample}, " +
                        $"active={eventActiveModifier.CallsPerSample} calls; " +
                        $"eligible settlements={EligibleSettlementCount()}; " +
                        $"settlement={eventSettlement?.ID.ToString() ?? "<none>"}; " +
                        $"last multiplier={eventMultiplier:F3}");

                    supplierState = NewDetachedState();
                    int supplierListingsCreated = 0;
                    Timing supplierMarket = Measure(
                        () => supplierState.SupplierListings.Clear(),
                        () => supplierListingsCreated =
                            SupplierListingService.Refresh(supplierState),
                        1);
                    result.Check(
                        supplierMarket.MedianMilliseconds <= SupplierMarketBudgetMilliseconds,
                        "Supplier Market generation",
                        TimingDetail(supplierMarket, SupplierMarketBudgetMilliseconds,
                            $"last batch created={supplierListingsCreated}; " +
                            $"world settlements={WorldSettlementCount()}"));

                    brandState = NewDetachedState();
                    List<ThingDef> brandDefs = BrandFixtureDefs();
                    ThingDef brandTarget = brandDefs.Count > 0
                        ? brandDefs[0]
                        : ThingDefOf.Silver;
                    for (int i = 0; i < brandDefs.Count; i++)
                    {
                        brandState.ProductBrandRecords.Add(new ProductBrandRecord(
                            brandDefs[i],
                            directScore: (i % 2 == 0 ? 55f : -35f),
                            evidenceWeight: 20f,
                            unitsDelivered: 100));
                    }

                    float brandValue = 0f;
                    Timing brand = Measure(
                        null,
                        () => brandValue = EffectiveBrandService.GetEffectiveBrand(
                            brandState, brandTarget),
                        FastCallsPerSample);
                    result.Check(
                        brand.MedianMilliseconds <= BrandEffectiveScoreBudgetMilliseconds,
                        "Brand effective-score lookup",
                        TimingDetail(brand, BrandEffectiveScoreBudgetMilliseconds,
                            $"records={brandState.ProductBrandRecords.Count}; " +
                            $"target={brandTarget?.defName ?? "<none>"}; " +
                            $"last value={brandValue:F2}; similarity metadata warmed"));

                    historyState = NewDetachedState();
                    savedHistory = new List<CommercialEventRecord>(historyState.CommercialTimeline);
                    const int historySettlementId = 8_300_001;
                    for (int i = 0; i < HistoryRecordCount; i++)
                    {
                        historyState.CommercialTimeline.Add(new CommercialEventRecord(
                            8_300_000 + i,
                            8_300_000 + i,
                            historySettlementId,
                            CommercialEventType.SaleCompleted,
                            "Stage 8C history fixture",
                            compactDetail: $"history-{i}"));
                    }

                    List<CommercialHistoryRelationRow> historyRows = null;
                    Timing history = Measure(
                        null,
                        () => historyRows = CommercialHistoryUiService.BuildRows(historyState),
                        1);
                    int presentedRows = historyRows != null && historyRows.Count > 0
                        ? historyRows[0].timelineRows.Count
                        : 0;
                    result.Check(
                        history.MedianMilliseconds <= HistoryRenderingBudgetMilliseconds &&
                        historyRows != null && historyRows.Count == 1 &&
                        presentedRows == CommercialHistoryUiService.TimelineRowLimit,
                        "history rendering with a populated timeline",
                        TimingDetail(history, HistoryRenderingBudgetMilliseconds,
                            $"retained={historyState.CommercialTimeline.Count}; " +
                            $"relation rows={historyRows?.Count ?? 0}; presented timeline rows={presentedRows}; " +
                            $"requested={CommercialHistoryUiService.TimelineRowLimit}"));
                }
            }
            catch (Exception ex)
            {
                result.Check(false, "Stage 8C benchmark execution", ex.ToString());
            }
            finally
            {
                // Every benchmark state is detached, but explicit cleanup is intentional: it
                // makes the fixture boundary auditable and keeps the 1,000-record test from
                // becoming a retained diagnostic object if a future benchmark gains a cache.
                propagationFixture?.Restore();
                diffusionFixture?.Restore();
                eventFixtureState?.Restore();
                ResetDetachedState(refreshState);
                ResetDetachedState(propagationState);
                ResetDetachedState(diffusionState);
                ResetDetachedState(eventState);
                ResetDetachedState(supplierState);
                ResetDetachedState(brandState);

                if (historyState != null)
                {
                    historyState.CommercialTimeline.Clear();
                    if (savedHistory != null)
                    {
                        historyState.CommercialTimeline.AddRange(savedHistory);
                    }
                }

                result.sb.AppendLine(
                    "        detached benchmark state and the populated timeline fixture restored in finally.");
            }

            AppendCallSiteAudit(result);
            result.sb.AppendLine(
                "        Product similarity cache: derived private static Dictionary<ThingDef, ProductProfile>; " +
                "it is warmed by lookup, invalidatable, and not authoritative product state.");
            result.sb.AppendLine(
                "        Timing method: two warm-up calls, seven timed samples, median per-call value; " +
                "fast reads use 128 calls per sample. Stopwatch time includes the production call only; " +
                "fixture reset and cache warm-up are outside timed regions.");
            return result.Summarize();
        }

        private static IntercolonyWorldComponent NewDetachedState()
        {
            return new IntercolonyWorldComponent(Find.World);
        }

        private static void ResetDetachedState(IntercolonyWorldComponent state)
        {
            if (state == null)
            {
                return;
            }

            state.Opportunities.Clear();
            state.EconomicEvents.Clear();
            state.MarketStates.Clear();
            state.ProductBrandRecords.Clear();
            state.Orders.Clear();
            state.Requests.Clear();
            state.SupplierListings.Clear();
            state.Contracts.Clear();
            state.ProcurementContracts.Clear();
            state.PurchaseOrders.Clear();
            state.Employments.Clear();
            state.Postings.Clear();
            state.CommercialHistory.Clear();
            state.CommercialTimeline.Clear();
            state.Ledger.Clear();
            state.LaborDebts.Clear();
            state.Reputations.Clear();
            state.RefreshMarketStateIndex();
        }

        private static MarketFixture BuildMarketFixture(IntercolonyWorldComponent state)
        {
            List<SettlementMarketState> records = new List<SettlementMarketState>();
            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements != null)
            {
                for (int i = 0; i < settlements.Count; i++)
                {
                    Settlement settlement = settlements[i];
                    if (!SettlementProfileGenerator.IsEligible(settlement))
                    {
                        continue;
                    }

                    SettlementMarketState record = new SettlementMarketState(settlement.ID);
                    record.demandPressure[0] = 1.15f;
                    record.supplyPressure[1] = 0.85f;
                    record.lastAdvancedRefresh = 0;
                    records.Add(record);
                    state.MarketStates.Add(record);
                }
            }

            // Propagation remains measurable on a world with no eligible settlement. Diffusion
            // correctly reports an empty graph in that case because it requires real world nodes.
            if (records.Count == 0)
            {
                SettlementMarketState fallback = new SettlementMarketState(8_300_101);
                fallback.demandPressure[0] = 1.15f;
                fallback.supplyPressure[1] = 0.85f;
                records.Add(fallback);
                state.MarketStates.Add(fallback);
            }

            state.RefreshMarketStateIndex();
            return new MarketFixture(state, records);
        }

        private static EconomicEvent BuildEventFixture()
        {
            float[] demand = NeutralModifiers();
            float[] scarcity = NeutralModifiers();
            demand[(int)IntercolonyProductCategory.Commodities] = 1.25f;
            scarcity[(int)IntercolonyProductCategory.Commodities] = 1.30f;
            return new EconomicEvent
            {
                type = EconomicEventType.Drought,
                startTick = GenTicks.TicksGame,
                endTick = GenTicks.TicksGame + 30 * GenDate.TicksPerDay,
                anchorSettlementId = EconomicEvent.NoSettlement,
                radiusTiles = EconomicEvent.NoRadius,
                factionLoadId = EconomicEvent.NoFaction,
                demandModifier = demand,
                supplyScarcityModifier = scarcity
            };
        }

        private static float[] NeutralModifiers()
        {
            float[] result = new float[IntercolonyProductCategoryUtility.Count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = EconomicEvent.Neutral;
            }

            return result;
        }

        private static List<ThingDef> BrandFixtureDefs()
        {
            List<ThingDef> result = new List<ThingDef>();
            List<ThingDef> tradable = IntercolonyProductClassifier.TradableDefs;
            if (tradable != null)
            {
                for (int i = 0; i < tradable.Count && result.Count < 64; i++)
                {
                    ThingDef def = tradable[i];
                    if (def != null && !result.Contains(def))
                    {
                        result.Add(def);
                    }
                }
            }

            return result;
        }

        private static Timing Measure(Action reset, Action operation, int callsPerSample)
        {
            for (int i = 0; i < WarmupRuns; i++)
            {
                reset?.Invoke();
                operation();
            }

            double[] samples = new double[TimedSamples];
            for (int sample = 0; sample < TimedSamples; sample++)
            {
                reset?.Invoke();
                Stopwatch timer = Stopwatch.StartNew();
                for (int call = 0; call < callsPerSample; call++)
                {
                    operation();
                }

                timer.Stop();
                samples[sample] = timer.Elapsed.TotalMilliseconds / callsPerSample;
            }

            Array.Sort(samples);
            return new Timing(samples[TimedSamples / 2], TimedSamples, callsPerSample);
        }

        private static string TimingDetail(
            Timing timing, double budgetMilliseconds, string workload)
        {
            return $"median={timing.MedianMilliseconds:F3} ms/call; " +
                   $"budget={budgetMilliseconds:F3} ms/call; " +
                   $"samples={timing.SampleCount} x {timing.CallsPerSample} calls; {workload}";
        }

        private static string TimingDetail(Timing timing, string workload)
        {
            return $"median={timing.MedianMilliseconds:F3} ms/call; " +
                   $"samples={timing.SampleCount} x {timing.CallsPerSample} calls; {workload}";
        }

        private static int StoredThingsCount(Map map)
        {
            if (map?.haulDestinationManager == null)
            {
                return 0;
            }

            List<SlotGroup> groups = map.haulDestinationManager.AllGroupsListForReading;
            int count = 0;
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i] != null)
                {
                    count += groups[i].HeldThingsCount;
                }
            }

            return count;
        }

        private static int WorldSettlementCount()
        {
            return Find.WorldObjects?.Settlements?.Count ?? 0;
        }

        private static int EligibleSettlementCount()
        {
            int count = 0;
            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements == null)
            {
                return count;
            }

            for (int i = 0; i < settlements.Count; i++)
            {
                if (SettlementProfileGenerator.IsEligible(settlements[i]))
                {
                    count++;
                }
            }

            return count;
        }

        private static Settlement FirstEligibleSettlement()
        {
            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements == null)
            {
                return null;
            }

            for (int i = 0; i < settlements.Count; i++)
            {
                if (SettlementProfileGenerator.IsEligible(settlements[i]))
                {
                    return settlements[i];
                }
            }

            return null;
        }

        private static void AppendCallSiteAudit(Results result)
        {
            result.sb.AppendLine("Call-site audit (source evidence; not inferred from timing):");
            result.Info(
                "coarse economy refresh: per-tick WorldComponentTick modulo guard at " +
                "Source/Intercolony/Core/IntercolonyWorldComponent.cs:1569; DoRefresh body starts " +
                "at :1672. ForceRefreshNow at :1644 is manual, not a game tick path.");
            result.Info(
                "pressure propagation: coarse schedule only in DoRefresh at " +
                "Source/Intercolony/Core/IntercolonyWorldComponent.cs:1689; no per-frame caller found.");
            result.Info(
                "regional diffusion: coarse schedule only in DoRefresh at " +
                "Source/Intercolony/Core/IntercolonyWorldComponent.cs:1690; no per-frame caller found.");
            result.Info(
                "event application: start shock is reached by event start at " +
                "Source/Intercolony/Economy/EconomicEventService.cs:138, from the refresh lifecycle " +
                "at Source/Intercolony/Core/IntercolonyWorldComponent.cs:1683; active " +
                "DemandMultiplier is reached through EffectiveEconomyService.cs:185 and :215 " +
                "for visible-row pricing. Debug action " +
                "Source/Intercolony/Debug/IntercolonyDebugActions.cs:214 is manual.");
            result.Info(
                "Supplier Market generation: SupplierListingService.Refresh is called from the " +
                "coarse DoRefresh at Source/Intercolony/Core/IntercolonyWorldComponent.cs:1706; " +
                "its per-settlement GenerateFor loop is " +
                "Source/Intercolony/Procurement/SupplierListingService.cs:287.");
            result.Info(
                "Brand effective-score lookup: mixed. Market generation reaches pricing at " +
                "Source/Intercolony/Market/MarketOpportunityGenerator.cs:138; Find Buyers is an " +
                "event-driven/cache-miss query at " +
                "Source/Intercolony/UI/MainTabWindow_Intercolony.cs:1763; selected buyer-tab " +
                "brand details call GetEffectiveBrandDetails each visible frame at " +
                "Source/Intercolony/UI/MainTabWindow_Intercolony.cs:1752, so this is a per-frame " +
                "UI read while that surface is open.");
            result.Info(
                "history rendering: per-frame while Relations is visible; " +
                "Source/Intercolony/UI/MainTabWindow_Intercolony.cs:3378 calls " +
                "CommercialHistoryUiService.BuildRows. " +
                "The measured fixture retains 1,000 records and presents 12.");
            result.Info(
                "Find Buyer stock paths: the tab refreshes at the real-time interval declared at " +
                "Source/Intercolony/UI/MainTabWindow_Intercolony.cs:128; " +
                "AvailableColonyAnimals is called at :1598 and AvailableColonyStock at :1603. " +
                "Both enter FindBuyerService.ColonyStock through " +
                "Source/Intercolony/Market/FindBuyerService.cs:684.");
        }

        private sealed class MarketFixture
        {
            private readonly IntercolonyWorldComponent state;
            private readonly List<SettlementMarketState> records;
            private readonly Dictionary<SettlementMarketState, float[]> demand;
            private readonly Dictionary<SettlementMarketState, float[]> supply;
            private readonly Dictionary<SettlementMarketState, int> refreshes;

            public MarketFixture(
                IntercolonyWorldComponent state, List<SettlementMarketState> records)
            {
                this.state = state;
                this.records = records;
                demand = new Dictionary<SettlementMarketState, float[]>();
                supply = new Dictionary<SettlementMarketState, float[]>();
                refreshes = new Dictionary<SettlementMarketState, int>();
                foreach (SettlementMarketState record in records)
                {
                    demand[record] = (float[])record.demandPressure.Clone();
                    supply[record] = (float[])record.supplyPressure.Clone();
                    refreshes[record] = record.lastAdvancedRefresh;
                }
            }

            public int RecordCount => records.Count;

            public void Restore()
            {
                state.MarketStates.Clear();
                state.MarketStates.AddRange(records);
                foreach (SettlementMarketState record in records)
                {
                    Array.Copy(demand[record], record.demandPressure, demand[record].Length);
                    Array.Copy(supply[record], record.supplyPressure, supply[record].Length);
                    record.lastAdvancedRefresh = refreshes[record];
                }

                state.RefreshMarketStateIndex();
            }
        }

        private sealed class Timing
        {
            public readonly double MedianMilliseconds;
            public readonly int SampleCount;
            public readonly int CallsPerSample;

            public Timing(double medianMilliseconds, int sampleCount, int callsPerSample)
            {
                MedianMilliseconds = medianMilliseconds;
                SampleCount = sampleCount;
                CallsPerSample = callsPerSample;
            }
        }

        private sealed class Results
        {
            public readonly StringBuilder sb = new StringBuilder();
            public int passed;
            public int failed;

            public void Check(bool condition, string label, string detail)
            {
                if (condition)
                {
                    passed++;
                    sb.AppendLine($"  PASS  {label}  ({detail})");
                }
                else
                {
                    failed++;
                    sb.AppendLine($"  FAIL  {label}  ({detail})");
                }
            }

            public void Info(string line)
            {
                sb.AppendLine("        " + line);
            }

            public string Summarize()
            {
                sb.AppendLine($"  {passed} passed, {failed} failed, 0 skipped.");
                return sb.ToString();
            }
        }
    }
}
