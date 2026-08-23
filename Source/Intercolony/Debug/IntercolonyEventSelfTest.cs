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
    /// Self-test for the persisted event model, definitions, lifecycle, and player messaging
    /// (Stages 3A/3C/3D/3E).
    /// </summary>
    public static class IntercolonyEventSelfTest
    {
        private sealed class Results
        {
            public readonly StringBuilder sb = new StringBuilder();
            public int passed;
            public int failed;
            public int skipped = 0;

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
        }

        public static string Run(IntercolonyWorldComponent state)
        {
            Results r = new Results();
            r.sb.AppendLine(
                "Economic event persistence, lifecycle, and player messaging self-test " +
                "(Stages 3A/3C/3D/3E)");

            if (state == null)
            {
                r.sb.AppendLine("  No world state available. Open or load a game first.");
                return Summarize(r);
            }

            // Contents, not count. Load pruning can remove or replace arbitrary entries, so
            // restoring by length could leave synthetic events in place of the player's real ones —
            // the same Stage 0.3 defect that left synthetic timeline records behind.
            List<EconomicEvent> saved = new List<EconomicEvent>(state.EconomicEvents);
            List<SettlementMarketState> savedMarketStates =
                CloneMarketStates(state.MarketStates);

            try
            {
                List<EconomicEvent> roundTripped = RoundTrip(out string roundTripFailure);
                EconomicEvent sentinel = roundTripped != null && roundTripped.Count == 2
                    ? roundTripped[0]
                    : null;
                EconomicEvent realZero = roundTripped != null && roundTripped.Count == 2
                    ? roundTripped[1]
                    : null;

                r.Check(
                    roundTripFailure == null &&
                    sentinel != null &&
                    sentinel.id == 731 &&
                    sentinel.type == EconomicEventType.AnimalDisease &&
                    sentinel.startTick == 1200 &&
                    sentinel.endTick == 1800 &&
                    sentinel.anchorSettlementId == EconomicEvent.NoSettlement &&
                    sentinel.radiusTiles == EconomicEvent.NoRadius &&
                    sentinel.factionLoadId == EconomicEvent.NoFaction &&
                    ArraysEqual(sentinel.demandModifier, new[] { 1.25f, 1.5f, 1.75f, 2f, 2.25f, 2.5f }) &&
                    ArraysEqual(sentinel.supplyScarcityModifier, new[] { 0.9f, 0.8f, 0.7f, 0.6f, 0.5f, 0.4f }),
                    "a Scribe round trip preserves every economic-event field",
                    roundTripFailure);

                float[] shortSaved = EconomicEvent.FromSaved(new List<float> { 1.4f });
                float[] missingSaved = EconomicEvent.FromSaved(null);
                r.Check(
                    shortSaved.Length == IntercolonyProductCategoryUtility.Count &&
                    Mathf.Approximately(shortSaved[0], 1.4f) &&
                    AllNeutralFrom(shortSaved, 1) &&
                    AllNeutralFrom(missingSaved, 0),
                    "short and missing modifier lists load padded with neutral 1.0");

                EconomicEvent boundary = new EconomicEvent { startTick = 400, endTick = 500 };
                r.Check(
                    boundary.IsActiveAt(boundary.startTick) && !boundary.IsActiveAt(boundary.endTick),
                    "event activity is half-open at its end tick");

                r.Check(
                    sentinel != null && realZero != null &&
                    sentinel.anchorSettlementId == EconomicEvent.NoSettlement &&
                    sentinel.radiusTiles == EconomicEvent.NoRadius &&
                    sentinel.factionLoadId == EconomicEvent.NoFaction &&
                    realZero.anchorSettlementId == 0 &&
                    realZero.anchorSettlementId != EconomicEvent.NoSettlement,
                    "sentinels survive and settlement id zero remains real");

                int now = GenTicks.TicksGame;
                EconomicEvent ended = new EconomicEvent { id = 901, endTick = now };
                EconomicEvent live = new EconomicEvent
                {
                    id = 902,
                    startTick = now,
                    endTick = now + 1
                };
                state.EconomicEvents.Clear();
                state.EconomicEvents.Add(null);
                state.EconomicEvents.Add(ended);
                state.EconomicEvents.Add(live);
                IntercolonyWorldComponent.PruneLoadedEconomicEvents(state.EconomicEvents, now);
                r.Check(
                    state.EconomicEvents.Count == 1 && state.EconomicEvents[0] == live,
                    "load pruning drops null and ended events but keeps a live event");

                CheckDefinitions(r, state);
                CheckLifecycle(r, state);
                CheckPlayerMessaging(r, state);
                CheckAcceptedObligationTerms(r, state);
            }
            catch (Exception ex)
            {
                r.sb.AppendLine($"  EXCEPTION: {ex}");
                r.failed++;
            }
            finally
            {
                state.EconomicEvents.Clear();
                state.EconomicEvents.AddRange(saved);
                state.MarketStates.Clear();
                state.MarketStates.AddRange(savedMarketStates);
                state.RefreshMarketStateIndex();
                r.sb.AppendLine($"        economic events restored to {state.EconomicEvents.Count}.");
                r.sb.AppendLine($"        market states restored to {state.MarketStates.Count}.");
            }

            return Summarize(r);
        }

        private static void CheckLifecycle(Results r, IntercolonyWorldComponent state)
        {
            List<Settlement> accessible = new List<Settlement>();
            List<Settlement> worldSettlements = Find.WorldObjects?.Settlements;
            if (worldSettlements != null)
            {
                for (int i = 0; i < worldSettlements.Count; i++)
                {
                    if (IntercolonyMarketAccess.IsAccessible(worldSettlements[i]))
                    {
                        accessible.Add(worldSettlements[i]);
                    }
                }
            }

            accessible.Sort((left, right) => left.ID.CompareTo(right.ID));
            CheckGeneratedLifecycleWiring(r, state, GenTicks.TicksGame);
            if (accessible.Count < 2)
            {
                r.skipped += 7;
                r.sb.AppendLine(
                    "  SKIP  lifecycle assertions require two accessible settlements");
                return;
            }

            Settlement anchor = accessible[0];
            Settlement outside = accessible[1];
            int now = GenTicks.TicksGame;
            state.EconomicEvents.Clear();
            state.MarketStates.Clear();
            state.RefreshMarketStateIndex();

            EconomicEvent expired = EventFor(anchor, now - 2, now);
            EconomicEvent live = EventFor(anchor, now, now + 1);
            state.EconomicEvents.Add(expired);
            state.EconomicEvents.Add(live);
            EconomicEventService.AdvanceLifecycle(state, now, allowGeneration: false);
            r.Check(
                state.EconomicEvents.Count == 1 && state.EconomicEvents[0] == live,
                "lifecycle removes an event past its window and keeps a live event");

            state.EconomicEvents.Clear();
            state.MarketStates.Clear();
            state.RefreshMarketStateIndex();
            EconomicEvent scoped = EventFor(anchor, now, now + 1);
            state.EconomicEvents.Add(scoped);
            EconomicEventService.ApplyStartShock(state, scoped);
            float insidePressure = state.MarketStateFor(anchor.ID)?.DemandPressureFor(
                IntercolonyProductCategory.ManufacturedGoods) ?? SettlementMarketState.Neutral;
            float outsidePressure = state.MarketStateFor(outside.ID)?.DemandPressureFor(
                IntercolonyProductCategory.ManufacturedGoods) ?? SettlementMarketState.Neutral;
            r.Check(
                insidePressure > SettlementMarketState.Neutral &&
                Mathf.Approximately(outsidePressure, SettlementMarketState.Neutral),
                "start shock reaches an in-scope settlement and leaves an out-of-scope one neutral");

            EconomicEventService.AdvanceLifecycle(state, scoped.endTick, allowGeneration: false);
            float tail = state.MarketStateFor(anchor.ID)?.DemandPressureFor(
                IntercolonyProductCategory.ManufacturedGoods) ?? SettlementMarketState.Neutral;
            r.Check(
                state.EconomicEvents.Count == 0 && tail > SettlementMarketState.Neutral,
                "pressure tail remains after lifecycle removes the ended event");

            float linkedBefore = state.MarketStateFor(anchor.ID).DemandPressureFor(
                IntercolonyProductCategory.IntermediateGoods);
            MarketPressureService.PropagateEconomicChains(state);
            float linkedAfter = state.MarketStateFor(anchor.ID).DemandPressureFor(
                IntercolonyProductCategory.IntermediateGoods);
            r.Check(
                linkedAfter > linkedBefore,
                "real chain propagation carries a category linked from the start shock");

            state.EconomicEvents.Clear();
            state.MarketStates.Clear();
            state.RefreshMarketStateIndex();
            for (int i = 0; i < EconomicEventService.MaxConcurrentEvents + 4; i++)
            {
                EconomicEventService.TryGenerate(state, now, forceStart: true);
            }
            r.Check(
                state.EconomicEvents.Count == EconomicEventService.MaxConcurrentEvents,
                "forced generation never exceeds the concurrent-event cap");

            EconomicEventService.GenerationDecision first =
                EconomicEventService.DecideGeneration(state, accessible);
            EconomicEventService.GenerationDecision second =
                EconomicEventService.DecideGeneration(state, accessible);
            r.Check(
                first.roll == second.roll && first.Starts == second.Starts &&
                first.type == second.type && first.anchor.ID == second.anchor.ID,
                "economy seed and refresh count reproduce the same generation decision");

            int busiestFaction = EconomicEvent.NoFaction;
            int busiestCount = 0;
            for (int i = 0; i < accessible.Count; i++)
            {
                int factionId = accessible[i].Faction.loadID;
                int count = accessible.FindAll(s => s.Faction.loadID == factionId).Count;
                if (count > busiestCount)
                {
                    busiestFaction = factionId;
                    busiestCount = count;
                }
            }

            EconomicEvent factionWide = new EconomicEvent
            {
                startTick = now,
                endTick = now + 1,
                factionLoadId = busiestFaction
            };
            factionWide.demandModifier[(int)IntercolonyProductCategory.ManufacturedGoods] = 1.25f;
            state.MarketStates.Clear();
            state.RefreshMarketStateIndex();
            int shocked = EconomicEventService.ApplyStartShock(state, factionWide);
            // A faction at or below the cap cannot exercise the capped path, so this world
            // does not provide enough settlements to test the per-event settlement cap.
            if (busiestCount <= EconomicEventService.MaxShockedSettlementsPerEvent)
            {
                r.skipped++;
                r.sb.AppendLine(
                    $"  SKIP  faction-wide start shock cap was not tested: busiest faction has {busiestCount} settlements, cap is {EconomicEventService.MaxShockedSettlementsPerEvent}");
            }
            else
            {
                r.Check(
                    shocked == EconomicEventService.MaxShockedSettlementsPerEvent,
                    "faction-wide start shock obeys the per-event settlement work cap",
                    $"{shocked} of {busiestCount} in-scope settlements shocked");
            }
        }

        private static void CheckPlayerMessaging(Results r, IntercolonyWorldComponent state)
        {
            List<Settlement> eligible = EligibleSettlements();
            if (eligible.Count < 2)
            {
                r.skipped += 4;
                r.sb.AppendLine(
                    "  SKIP  player-messaging assertions require two eligible settlements");
                return;
            }

            List<EconomicEvent> savedEvents = new List<EconomicEvent>(state.EconomicEvents);
            Dictionary<int, CommercialReputation> savedReputations =
                new Dictionary<int, CommercialReputation>(state.Reputations);
            List<CommercialHistoryEntry> savedCommercialHistory =
                new List<CommercialHistoryEntry>(state.CommercialHistory);

            try
            {
                Settlement traded = eligible[0];
                Settlement untraded = eligible[1];
                int now = GenTicks.TicksGame;
                state.EconomicEvents.Clear();
                state.Reputations.Clear();
                state.CommercialHistory.Clear();
                state.Reputations[traded.ID] = new CommercialReputation(
                    traded.ID, traded.Label ?? "traded settlement", traded.Faction?.Name ?? "");

                EconomicEvent tradedEvent = EventFor(
                    traded, now, now + 3 * GenDate.TicksPerDay);
                EconomicEvent untradedEvent = EventFor(
                    untraded, now, now + 3 * GenDate.TicksPerDay);
                IntercolonyLetterImportance tradedImportance =
                    EconomicEventService.ImportanceForStartLetter(state, tradedEvent);
                IntercolonyLetterImportance untradedImportance =
                    EconomicEventService.ImportanceForStartLetter(state, untradedEvent);
                r.Check(
                    tradedImportance == IntercolonyLetterImportance.Important &&
                    untradedImportance == IntercolonyLetterImportance.Chatty,
                    "event severity is Important for a traded settlement and Chatty otherwise",
                    $"traded={tradedImportance}, untraded={untradedImportance}");

                bool noAlways = true;
                EconomicEventType[] types = EconomicEventDefinitions.DefinedTypes;
                for (int i = 0; i < types.Length; i++)
                {
                    EconomicEvent probe = EventFor(
                        untraded, now, now + GenDate.TicksPerDay);
                    probe.type = types[i];
                    if (EconomicEventService.ImportanceForStartLetter(state, probe) ==
                        IntercolonyLetterImportance.Always)
                    {
                        noAlways = false;
                        break;
                    }
                }

                r.Check(
                    noAlways,
                    "no economic event start letter ever uses Always severity");

                EconomicEvent durationEvent = new EconomicEvent
                {
                    startTick = now,
                    endTick = now + 3 * GenDate.TicksPerDay
                };
                string startLabel = EconomicEventService.RemainingDurationLabel(
                    durationEvent, now);
                string nearEndLabel = EconomicEventService.RemainingDurationLabel(
                    durationEvent, durationEvent.endTick - 1);
                string afterEndLabel = EconomicEventService.RemainingDurationLabel(
                    durationEvent, durationEvent.endTick + GenDate.TicksPerDay);
                r.Check(
                    startLabel == "3 days left" &&
                    nearEndLabel == "1 day left" &&
                    afterEndLabel == "0 days left",
                    "remaining event duration is correct at start, near end, and never negative",
                    $"start={startLabel}, near end={nearEndLabel}, after end={afterEndLabel}");

                state.EconomicEvents.Add(tradedEvent);
                List<WITab_Economy.DisplayRow> inScopeRows = WITab_Economy.BuildRows(
                    state, traded);
                List<WITab_Economy.DisplayRow> outOfScopeRows = WITab_Economy.BuildRows(
                    state, untraded);
                string expectedEventRow = $"{tradedEvent.type.Label()}, " +
                                          EconomicEventService.RemainingDurationLabel(
                                              tradedEvent, now);
                bool namedInScope = ContainsValue(inScopeRows, expectedEventRow);
                bool namedOutOfScope = ContainsValue(outOfScopeRows, expectedEventRow);
                r.Check(
                    namedInScope && !namedOutOfScope,
                    "economy rows name an active in-scope event and omit it out of scope",
                    $"in-scope={namedInScope}, out-of-scope={namedOutOfScope}");
            }
            finally
            {
                // Contents, not count. The messaging fixtures replace the player's live events,
                // and restoring only the length could preserve a synthetic event at one index.
                state.EconomicEvents.Clear();
                state.EconomicEvents.AddRange(savedEvents);
                state.Reputations.Clear();
                foreach (KeyValuePair<int, CommercialReputation> entry in savedReputations)
                {
                    state.Reputations.Add(entry.Key, entry.Value);
                }

                state.CommercialHistory.Clear();
                state.CommercialHistory.AddRange(savedCommercialHistory);
            }
        }

        private static void CheckAcceptedObligationTerms(
            Results r, IntercolonyWorldComponent state)
        {
            const int AssertionCount = 7;
            List<Settlement> eligible = EligibleSettlements();
            if (eligible.Count == 0)
            {
                r.skipped += AssertionCount;
                r.sb.AppendLine(
                    "  SKIP  accepted-obligation assertions require an eligible settlement");
                return;
            }

            Settlement settlement = eligible[0];
            SettlementEconomicProfile profile = state.GetProfile(settlement);
            List<ThingDef> commodityDefs = new List<ThingDef>();
            foreach (ThingDef candidate in IntercolonyProductClassifier.TradableDefs)
            {
                if (candidate != null && candidate.category == ThingCategory.Item &&
                    candidate.stackLimit >= 10 && !candidate.MadeFromStuff &&
                    IntercolonyProductClassifier.Classify(candidate) ==
                    IntercolonyProductCategory.Commodities)
                {
                    commodityDefs.Add(candidate);
                }
            }

            commodityDefs.Sort((left, right) => string.Compare(
                left.defName, right.defName, StringComparison.OrdinalIgnoreCase));
            ThingDef good = commodityDefs.Count == 0 ? null : commodityDefs[0];
            if (profile == null || good == null)
            {
                r.skipped += AssertionCount;
                r.sb.AppendLine(
                    profile == null
                        ? "  SKIP  accepted-obligation assertions require a profile for the eligible settlement"
                        : "  SKIP  accepted-obligation assertions require a tradable stackable commodity good");
                return;
            }

            // Contents, not count. This test replaces every obligation and market collection with
            // synthetic records; restoring only lengths could discard the player's real orders or
            // leave a synthetic event or pressure record behind.
            List<EconomicEvent> savedEvents = new List<EconomicEvent>(state.EconomicEvents);
            List<SalesOrder> savedOrders = new List<SalesOrder>(state.Orders);
            List<PurchaseOrder> savedPurchaseOrders =
                new List<PurchaseOrder>(state.PurchaseOrders);
            List<SettlementMarketState> savedMarketStates =
                CloneMarketStates(state.MarketStates);

            try
            {
                const int quantity = 10;
                int now = GenTicks.TicksGame;
                int deadline = now + 10 * GenDate.TicksPerDay;
                IntercolonyProductCategory category =
                    IntercolonyProductCategory.Commodities;

                state.EconomicEvents.Clear();
                state.MarketStates.Clear();
                state.RefreshMarketStateIndex();

                float struckPrice = IntercolonyPricing.UnitPrice(
                    state, good, quantity, profile, category, -1f, null, out _);
                float struckDemandMultiplier = EconomicEventService.DemandMultiplier(
                    state, settlement, category);
                float struckDemandBasis = Mathf.Clamp(
                    EffectiveEconomyService.EffectiveDemand(state, profile, good, category),
                    0.4f, 2.0f);
                SalesOrder sale = new SalesOrder
                {
                    id = -3_700_001,
                    settlementId = settlement.ID,
                    settlementName = settlement.Label ?? "unnamed",
                    factionName = settlement.Faction?.Name ?? "",
                    line = new OrderLine(good, quantity),
                    unitPrice = struckPrice,
                    acceptedTick = now,
                    deadlineTick = deadline,
                    status = SalesOrderStatus.Accepted
                };
                PurchaseOrder purchase = new PurchaseOrder
                {
                    id = -3_700_002,
                    settlementId = settlement.ID,
                    settlementName = settlement.Label ?? "unnamed",
                    factionName = settlement.Faction?.Name ?? "",
                    thingDef = good,
                    quantity = quantity,
                    unitPrice = struckPrice,
                    supplierDelivers = false,
                    orderedTick = now,
                    readyTick = now + GenDate.TicksPerDay,
                    pickupExpiryTick = deadline,
                    status = PurchaseOrderStatus.Confirmed
                };
                state.Orders.Add(sale);
                state.PurchaseOrders.Add(purchase);

                float savedSalePrice = sale.unitPrice;
                int savedSaleQuantity = sale.Quantity;
                int savedSaleDeadline = sale.deadlineTick;
                float savedPurchasePrice = purchase.unitPrice;
                int savedPurchaseQuantity = purchase.quantity;
                int savedPurchaseDeadline = purchase.pickupExpiryTick;

                // Use the same production boundary as the force action. Hand-building an event
                // and appending it would let this test pass with an inert record, skipping the
                // Build, registration, start shock, and active-pricing path this test must cover.
                EconomicEvent started = EconomicEventService.StartEvent(
                    state,
                    EconomicEventType.Drought,
                    settlement,
                    now,
                    out int shockedSettlements);
                bool eventAffectedSettlement = started != null &&
                    state.EconomicEvents.Contains(started) &&
                    started.IsActiveAt(now) &&
                    EconomicEventService.IsInScope(started, settlement) &&
                    shockedSettlements > 0;

                // The price is captured before the event because it is the rate at which both
                // obligations were struck. Re-reading current conditions for the expected value
                // would erase the binding-term boundary this test is meant to guard.
                float currentPrice = IntercolonyPricing.UnitPrice(
                    state, good, quantity, profile, category, -1f, null, out _);
                float currentDemandMultiplier = EconomicEventService.DemandMultiplier(
                    state, settlement, category);
                float currentDemandBasis = Mathf.Clamp(
                    EffectiveEconomyService.EffectiveDemand(state, profile, good, category),
                    0.4f, 2.0f);

                r.Check(
                    eventAffectedSettlement && Mathf.Approximately(
                        sale.unitPrice, savedSalePrice),
                    "a drought leaves an accepted sales order's stored unit price unchanged",
                    $"stored {savedSalePrice:F4}, after {sale.unitPrice:F4}");
                r.Check(
                    eventAffectedSettlement && sale.Quantity == savedSaleQuantity,
                    "a drought leaves an accepted sales order's quantity unchanged",
                    $"stored {savedSaleQuantity}, after {sale.Quantity}");
                r.Check(
                    eventAffectedSettlement && sale.deadlineTick == savedSaleDeadline,
                    "a drought leaves an accepted sales order's deadline unchanged",
                    $"stored {savedSaleDeadline}, after {sale.deadlineTick}");
                r.Check(
                    eventAffectedSettlement && Mathf.Approximately(
                        purchase.unitPrice, savedPurchasePrice),
                    "a drought leaves an accepted purchase order's stored unit price unchanged",
                    $"stored {savedPurchasePrice:F4}, after {purchase.unitPrice:F4}");
                r.Check(
                    eventAffectedSettlement && purchase.quantity == savedPurchaseQuantity,
                    "a drought leaves an accepted purchase order's quantity unchanged",
                    $"stored {savedPurchaseQuantity}, after {purchase.quantity}");
                r.Check(
                    eventAffectedSettlement && purchase.pickupExpiryTick == savedPurchaseDeadline,
                    "a drought leaves an accepted purchase order's deadline unchanged",
                    $"stored {savedPurchaseDeadline}, after {purchase.pickupExpiryTick}");

                // This complement is essential: if the event system were deleted, all six frozen
                // term checks would still pass. Establish that the event moved the clamped demand
                // basis actually consumed by pricing before comparing the newly computed price.
                // A non-neutral event multiplier alone is insufficient because the pricing clamp
                // can leave a high-baseline good's quoted price unchanged.
                bool droughtMovedPriceBasis = eventAffectedSettlement &&
                    Mathf.Abs(currentDemandBasis - struckDemandBasis) > 0.0001f;
                if (eventAffectedSettlement && !droughtMovedPriceBasis)
                {
                    r.skipped++;
                    r.sb.AppendLine(
                        "  SKIPPED  a drought changes a newly computed price without repricing " +
                        "the accepted deal  " +
                        $"(sampled {category} price basis did not move " +
                        $"{struckDemandBasis:F4}->{currentDemandBasis:F4}; demand multiplier " +
                        $"{struckDemandMultiplier:F4}->{currentDemandMultiplier:F4})");
                }
                else
                {
                    r.Check(
                        eventAffectedSettlement && droughtMovedPriceBasis &&
                        Mathf.Abs(currentPrice - struckPrice) > 0.0001f,
                        "a drought changes a newly computed price without repricing the accepted deal",
                        $"before {struckPrice:F4}, current {currentPrice:F4}; " +
                        $"price basis {struckDemandBasis:F4}->{currentDemandBasis:F4}");
                }
            }
            finally
            {
                // Restore the player's actual records, not merely the old counts. A count-based
                // cleanup can leave synthetic obligations or pressure in place of save data.
                state.EconomicEvents.Clear();
                state.EconomicEvents.AddRange(savedEvents);
                state.Orders.Clear();
                state.Orders.AddRange(savedOrders);
                state.PurchaseOrders.Clear();
                state.PurchaseOrders.AddRange(savedPurchaseOrders);
                state.MarketStates.Clear();
                state.MarketStates.AddRange(savedMarketStates);
                state.RefreshMarketStateIndex();
            }
        }

        private static List<Settlement> EligibleSettlements()
        {
            List<Settlement> eligible = new List<Settlement>();
            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements != null)
            {
                for (int i = 0; i < settlements.Count; i++)
                {
                    if (SettlementProfileGenerator.IsEligible(settlements[i]))
                    {
                        eligible.Add(settlements[i]);
                    }
                }
            }

            eligible.Sort((left, right) => left.ID.CompareTo(right.ID));
            return eligible;
        }

        private static bool ContainsValue(
            List<WITab_Economy.DisplayRow> rows, string expectedValue)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].value == expectedValue)
                {
                    return true;
                }
            }

            return false;
        }

        private static void CheckGeneratedLifecycleWiring(
            Results r, IntercolonyWorldComponent state, int now)
        {
            List<EconomicEvent> savedEvents = new List<EconomicEvent>(state.EconomicEvents);
            List<SettlementMarketState> savedMarketStates =
                CloneMarketStates(state.MarketStates);

            try
            {
                state.EconomicEvents.Clear();
                state.MarketStates.Clear();
                state.RefreshMarketStateIndex();

                EconomicEventService.GenerationDecision decision =
                    EconomicEventService.DecideGeneration(state);
                if (decision.anchor == null)
                {
                    r.skipped += 2;
                    r.sb.AppendLine(
                        "  SKIP  forced-generation lifecycle assertions require an eligible accessible anchor settlement");
                    return;
                }

                EconomicEvent started =
                    EconomicEventService.TryGenerate(state, now, forceStart: true);
                Settlement pressuredSettlement = FirstEligibleSettlementInScope(started);
                bool pressureAfterStart = pressuredSettlement != null &&
                    HasNonNeutralPressure(state, pressuredSettlement.ID);
                r.Check(
                    started != null && state.EconomicEvents.Contains(started) && pressureAfterStart,
                    "forced generation applies pressure through the real event-start path");

                EconomicEventService.AdvanceLifecycle(
                    state, started?.endTick ?? now, allowGeneration: false);
                bool pressureAfterEnd = pressuredSettlement != null &&
                    HasNonNeutralPressure(state, pressuredSettlement.ID);
                r.Check(
                    started != null && !state.EconomicEvents.Contains(started) && pressureAfterEnd,
                    "pressure tail remains after a generated event ends through the lifecycle");
            }
            finally
            {
                state.EconomicEvents.Clear();
                state.EconomicEvents.AddRange(savedEvents);
                state.MarketStates.Clear();
                state.MarketStates.AddRange(savedMarketStates);
                state.RefreshMarketStateIndex();
            }
        }

        private static Settlement FirstEligibleSettlementInScope(EconomicEvent economicEvent)
        {
            if (economicEvent == null)
            {
                return null;
            }

            List<Settlement> eligible = new List<Settlement>();
            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements != null)
            {
                for (int i = 0; i < settlements.Count; i++)
                {
                    if (SettlementProfileGenerator.IsEligible(settlements[i]))
                    {
                        eligible.Add(settlements[i]);
                    }
                }
            }

            eligible.Sort((left, right) => left.ID.CompareTo(right.ID));
            return eligible.Find(candidate =>
                EconomicEventService.IsInScope(economicEvent, candidate));
        }

        private static bool HasNonNeutralPressure(
            IntercolonyWorldComponent state, int settlementId)
        {
            for (int categoryIndex = 0;
                categoryIndex < IntercolonyProductCategoryUtility.Count;
                categoryIndex++)
            {
                IntercolonyProductCategory category =
                    (IntercolonyProductCategory)categoryIndex;
                float demand = EffectiveEconomyService.CurrentDemandPressure(
                    state, settlementId, category);
                float supply = EffectiveEconomyService.CurrentSupplyPressure(
                    state, settlementId, category);
                if (!Mathf.Approximately(demand, SettlementMarketState.Neutral) ||
                    !Mathf.Approximately(supply, SettlementMarketState.Neutral))
                {
                    return true;
                }
            }

            return false;
        }

        private static EconomicEvent EventFor(Settlement anchor, int startTick, int endTick)
        {
            EconomicEvent economicEvent = new EconomicEvent
            {
                startTick = startTick,
                endTick = endTick,
                anchorSettlementId = anchor.ID,
                radiusTiles = EconomicEventDefinitions.SingleSettlementRadiusTiles
            };
            economicEvent.demandModifier[(int)IntercolonyProductCategory.ManufacturedGoods] = 1.25f;
            return economicEvent;
        }

        private static List<SettlementMarketState> CloneMarketStates(
            List<SettlementMarketState> source)
        {
            List<SettlementMarketState> copy = new List<SettlementMarketState>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                SettlementMarketState original = source[i];
                if (original == null)
                {
                    copy.Add(null);
                    continue;
                }

                copy.Add(new SettlementMarketState(original.settlementId)
                {
                    demandPressure = (float[])original.demandPressure.Clone(),
                    supplyPressure = (float[])original.supplyPressure.Clone(),
                    lastAdvancedRefresh = original.lastAdvancedRefresh
                });
            }

            return copy;
        }

        private static void CheckDefinitions(Results r, IntercolonyWorldComponent state)
        {
            EconomicEventType[] types = EconomicEventDefinitions.DefinedTypes;
            for (int i = 0; i < types.Length; i++)
            {
                EconomicEventType type = types[i];
                EconomicEventDefinitions.Definition definition = EconomicEventDefinitions.Get(type);
                r.Check(
                    AnyNonNeutral(definition.demandModifier) ||
                    AnyNonNeutral(definition.supplyScarcityModifier),
                    $"{type} definition has at least one non-neutral modifier");

                CheckNoChainPair(
                    r,
                    type,
                    "demand",
                    definition.demandModifier,
                    MarketPressureService.DemandLinks);
                CheckNoChainPair(
                    r,
                    type,
                    "supply",
                    definition.supplyScarcityModifier,
                    MarketPressureService.SupplyLinks);
            }

            EconomicEventDefinitions.Definition drought =
                EconomicEventDefinitions.Get(EconomicEventType.Drought);
            r.Check(
                drought.supplyScarcityModifier[(int)IntercolonyProductCategory.Commodities] >
                EconomicEvent.Neutral,
                "drought raises commodity scarcity rather than encoding supply down as a glut");

            // War scope needs a real faction load ID. A fabricated faction would make this assertion
            // prove the fixture rather than the shipped factory, the same self-test trap previously
            // seen when contract tests hand-built the object whose invariants they claimed to test.
            Settlement anchor = null;
            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements != null)
            {
                anchor = settlements.Find(candidate => candidate?.Faction != null);
            }

            if (anchor == null)
            {
                r.skipped++;
                r.sb.AppendLine("  SKIP  factory assertions require a settlement with a faction");
                return;
            }

            const int startTick = 12_345_678;
            EconomicEvent droughtEvent = EconomicEventDefinitions.Build(
                state, EconomicEventType.Drought, anchor, startTick);
            EconomicEvent warEvent = EconomicEventDefinitions.Build(
                state, EconomicEventType.WarMobilization, anchor, startTick);
            EconomicEvent constructionEvent = EconomicEventDefinitions.Build(
                state, EconomicEventType.ConstructionBoom, anchor, startTick);
            EconomicEvent epidemicEvent = EconomicEventDefinitions.Build(
                state, EconomicEventType.Epidemic, anchor, startTick);

            r.Check(
                droughtEvent.anchorSettlementId == anchor.ID &&
                droughtEvent.radiusTiles > 0f &&
                droughtEvent.factionLoadId == EconomicEvent.NoFaction,
                "drought is radial and leaves faction scope at its sentinel");
            r.Check(
                warEvent.anchorSettlementId == anchor.ID &&
                warEvent.radiusTiles == EconomicEvent.NoRadius &&
                warEvent.factionLoadId == anchor.Faction.loadID,
                "war mobilization retains its anchor while leaving radial scope at its sentinel");
            r.Check(
                constructionEvent.anchorSettlementId == anchor.ID &&
                constructionEvent.radiusTiles == 0f &&
                constructionEvent.factionLoadId == EconomicEvent.NoFaction,
                "construction boom is single-settlement and leaves faction scope at its sentinel");
            r.Check(
                epidemicEvent.anchorSettlementId == anchor.ID &&
                epidemicEvent.radiusTiles == 0f &&
                epidemicEvent.factionLoadId == EconomicEvent.NoFaction,
                "epidemic is single-settlement and leaves faction scope at its sentinel");

            EconomicEvent[] first =
                { droughtEvent, warEvent, constructionEvent, epidemicEvent };
            for (int i = 0; i < types.Length; i++)
            {
                EconomicEvent again = EconomicEventDefinitions.Build(
                    state, types[i], anchor, startTick);
                r.Check(
                    first[i].endTick == again.endTick &&
                    ArraysEqual(first[i].demandModifier, again.demandModifier) &&
                    ArraysEqual(first[i].supplyScarcityModifier, again.supplyScarcityModifier),
                    $"{types[i]} factory duration and modifiers are deterministic");
                r.Check(
                    first[i].endTick - first[i].startTick > 0 &&
                    first[i].endTick > first[i].startTick,
                    $"{types[i]} duration is positive and ends after it starts");
            }
        }

        private static void CheckNoChainPair(
            Results r,
            EconomicEventType type,
            string tableLabel,
            float[] modifiers,
            MarketPressureService.EconomicChainLink[] links)
        {
            bool clean = true;
            string detail = null;
            for (int i = 0; i < links.Length; i++)
            {
                MarketPressureService.EconomicChainLink link = links[i];
                if (!Mathf.Approximately(modifiers[(int)link.source], EconomicEvent.Neutral) &&
                    !Mathf.Approximately(modifiers[(int)link.target], EconomicEvent.Neutral))
                {
                    clean = false;
                    detail = $"{link.source} -> {link.target}";
                    break;
                }
            }

            r.Check(
                clean,
                $"{type} does not double-count a {tableLabel}-chain link",
                detail);
        }

        private static bool AnyNonNeutral(float[] values)
        {
            if (values == null)
            {
                return false;
            }

            for (int i = 0; i < values.Length; i++)
            {
                if (!Mathf.Approximately(values[i], EconomicEvent.Neutral))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<EconomicEvent> RoundTrip(out string failure)
        {
            EconomicEvent sentinel = new EconomicEvent
            {
                id = 731,
                type = EconomicEventType.AnimalDisease,
                startTick = 1200,
                endTick = 1800,
                anchorSettlementId = EconomicEvent.NoSettlement,
                radiusTiles = EconomicEvent.NoRadius,
                factionLoadId = EconomicEvent.NoFaction,
                demandModifier = new[] { 1.25f, 1.5f, 1.75f, 2f, 2.25f, 2.5f },
                supplyScarcityModifier = new[] { 0.9f, 0.8f, 0.7f, 0.6f, 0.5f, 0.4f }
            };
            EconomicEvent realZero = new EconomicEvent
            {
                id = 732,
                anchorSettlementId = 0,
                radiusTiles = 0f,
                factionLoadId = 0,
                startTick = 1800,
                endTick = 2400
            };
            List<EconomicEvent> savedList = new List<EconomicEvent> { sentinel, realZero };
            List<EconomicEvent> loadedList = null;
            failure = null;
            string path = Path.Combine(
                Path.GetTempPath(), $"Intercolony-EconomicEvent-{Guid.NewGuid():N}.xml");

            try
            {
                Scribe.saver.InitSaving(path, "intercolonyEconomicEventTest");
                Scribe_Collections.Look(ref savedList, "economicEvents", LookMode.Deep);
                Scribe.saver.FinalizeSaving();

                Scribe.loader.InitLoading(path);
                Scribe_Collections.Look(ref loadedList, "economicEvents", LookMode.Deep);
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

            return loadedList;
        }

        private static bool ArraysEqual(float[] actual, float[] expected)
        {
            if (actual == null || actual.Length != expected.Length)
            {
                return false;
            }

            for (int i = 0; i < actual.Length; i++)
            {
                if (!Mathf.Approximately(actual[i], expected[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AllNeutralFrom(float[] values, int startIndex)
        {
            if (values == null || values.Length != IntercolonyProductCategoryUtility.Count)
            {
                return false;
            }

            for (int i = startIndex; i < values.Length; i++)
            {
                if (!Mathf.Approximately(values[i], EconomicEvent.Neutral))
                {
                    return false;
                }
            }

            return true;
        }

        private static string Summarize(Results r)
        {
            r.sb.AppendLine();
            r.sb.AppendLine($"  {r.passed} passed, {r.failed} failed, {r.skipped} skipped.");
            return r.sb.ToString();
        }
    }
}
