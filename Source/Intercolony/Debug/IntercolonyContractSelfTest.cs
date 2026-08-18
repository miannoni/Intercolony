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
    /// Assertions over recurring contracts (DESIGN.md §83.2, §107).
    ///
    /// §107's acceptance criterion is "a multi-cycle contract survives save/load and affects
    /// production planning". The save/load half needs a real reload, so it is split into
    /// plant/verify probes like the Phase 7 spike. This covers the rest: the cycle machinery
    /// actually cycling, breach after repeated misses, and the reputation gate.
    ///
    /// Test contracts are removed afterwards so running this leaves no residue.
    /// </summary>
    public static class IntercolonyContractSelfTest
    {
        private const string ProbeTag = "ContractProbe";

        public static string Run(IntercolonyWorldComponent state)
        {
            StringBuilder sb = new StringBuilder();
            int passed = 0;
            int failed = 0;
            List<string> skippedAssertions = new List<string>();

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
                skippedAssertions.Add(name);
                sb.AppendLine($"  SKIPPED  {name} — {reason}");
            }

            string Summarize()
            {
                if (skippedAssertions.Count == 0)
                {
                    sb.AppendLine($"  {passed} passed, {failed} failed, 0 skipped.");
                }
                else
                {
                    sb.AppendLine($"  {passed} passed, {failed} failed, " +
                                  $"{skippedAssertions.Count} SKIPPED — not a clean run.");
                    sb.AppendLine("  Skipped assertions:");
                    foreach (string name in skippedAssertions)
                    {
                        sb.AppendLine($"  SKIPPED  {name}");
                    }
                }

                return sb.ToString();
            }

            sb.AppendLine("Recurring contract self-test");

            List<RecurringContract> created = new List<RecurringContract>();
            List<SalesOrder> createdOrders = new List<SalesOrder>();

            RecurringContract PlantLivenessContract(
                ContractStatus status, bool renewalOffered = false, int renewalExpiryTick = 0)
            {
                RecurringContract contract = new RecurringContract
                {
                    settlementId = UnusedContractSettlementId(state),
                    status = status,
                    renewalOffered = renewalOffered,
                    renewalExpiryTick = renewalExpiryTick
                };

                state.AddContract(contract);
                created.Add(contract);
                return contract;
            }

            Settlement subject = FirstAccessibleSettlement();
            if (subject == null || IntercolonyProductClassifier.TradableDefs.Count == 0)
            {
                Skip("contract self-test prerequisites",
                    "no accessible settlement or tradable defs");
                return Summarize();
            }

            List<RecurringContract> savedContracts = new List<RecurringContract>(state.Contracts);
            List<SalesOrder> savedStateOrders = new List<SalesOrder>(state.Orders);
            List<CommercialHistoryEntry> savedCommercialHistory =
                new List<CommercialHistoryEntry>(state.CommercialHistory);
            bool hadSubjectReputation = state.Reputations.TryGetValue(
                subject.ID, out CommercialReputation savedSubjectReputation);
            state.Contracts.Clear();
            state.Orders.Clear();
            state.CommercialHistory.Clear();
            state.Reputations.Remove(subject.ID);
            try
            {
            // --- State machine (§73) ---
            RecurringContract probe = MakeContract(state, subject, 3);
            created.Add(probe);

            Check("a new contract is an offer", probe.IsOffer);
            Check("an offer is not yet active", !probe.IsActive);
            Check("accepting an offer succeeds", probe.TryAccept());
            Check("accepted contract is active", probe.IsActive);
            Check("a second accept is refused", !probe.TryAccept());
            Check("an active contract cannot be declined",
                !probe.TryDecline("Declined by the self-test."));
            Check("first delivery is a full cadence away",
                probe.nextCycleTick >= GenTicks.TicksGame + probe.cadenceTicks - 10,
                $"{probe.DaysUntilNextCycle:F1}d");

            RecurringContract declined = MakeContract(state, subject, 3);
            created.Add(declined);
            Check("declining an offer records why",
                declined.TryDecline("Declined by the self-test.") &&
                declined.outcomeNote == "Declined by the self-test.");

            // --- Relationship liveness: one standing agreement per settlement ---
            RecurringContract liveOffer = PlantLivenessContract(ContractStatus.Offered);
            Check("an offered contract is a live relationship",
                state.HasContractWith(liveOffer.settlementId));

            RecurringContract liveActive = PlantLivenessContract(ContractStatus.Active);
            Check("an active contract is a live relationship",
                state.HasContractWith(liveActive.settlementId));

            RecurringContract liveSuspended = PlantLivenessContract(ContractStatus.Suspended);
            Check("a suspended contract is still a live relationship",
                state.HasContractWith(liveSuspended.settlementId));

            RecurringContract liveRenewal = PlantLivenessContract(
                ContractStatus.Completed, renewalOffered: true,
                renewalExpiryTick: GenTicks.TicksGame + GenDate.TicksPerDay);
            Check("a pending renewal is still a live relationship",
                state.HasContractWith(liveRenewal.settlementId));

            RecurringContract ended = PlantLivenessContract(ContractStatus.Completed);
            Check("a completed contract without a renewal is no longer live",
                !state.HasContractWith(ended.settlementId));

            RecurringContract lapsedRenewal = PlantLivenessContract(
                ContractStatus.Completed, renewalOffered: true,
                renewalExpiryTick: GenTicks.TicksGame);
            Check("a lapsed renewal no longer blocks a new relationship",
                !state.HasContractWith(lapsedRenewal.settlementId));

            // --- Cycle machinery: does a multi-cycle contract actually cycle? ---
            RecurringContract runner = MakeContract(state, subject, 3);
            created.Add(runner);
            runner.TryAccept();

            int cyclesSeen = 0;
            int guard = 0;
            while (runner.IsActive && guard++ < 20)
            {
                // Force the cycle due, then let the service raise its order.
                runner.nextCycleTick = GenTicks.TicksGame;
                ContractService.AdvanceContracts(state);

                SalesOrder order = state.FindOrder(runner.activeOrderId);
                if (order == null)
                {
                    break;
                }

                createdOrders.Add(order);
                cyclesSeen++;

                // Complete it the way a real delivery would.
                order.deliveredQuantity = order.Quantity;
                order.paidSilver = order.TotalPayment;
                order.status = SalesOrderStatus.Completed;

                ContractService.AdvanceContracts(state);
            }

            Check("a multi-cycle contract raises an order per cycle", cyclesSeen == runner.totalCycles,
                $"{cyclesSeen} cycles for a {runner.totalCycles}-cycle contract");
            Check("fulfilling every cycle completes the contract",
                runner.status == ContractStatus.Completed, runner.status.ToString());
            Check("completed contract counted every delivery",
                runner.cyclesCompleted == runner.totalCycles,
                $"{runner.cyclesCompleted}/{runner.totalCycles}");
            sb.AppendLine($"  ({cyclesSeen} cycles run, contract ended {runner.status})");

            // --- Breach after consecutive misses (§30 grace period) ---
            RecurringContract breaker = MakeContract(state, subject, 5);
            created.Add(breaker);
            breaker.TryAccept();

            int misses = 0;
            guard = 0;
            while (breaker.IsActive && guard++ < 20)
            {
                breaker.nextCycleTick = GenTicks.TicksGame;
                ContractService.AdvanceContracts(state);

                SalesOrder order = state.FindOrder(breaker.activeOrderId);
                if (order == null)
                {
                    break;
                }

                createdOrders.Add(order);
                order.status = SalesOrderStatus.Failed;
                misses++;

                ContractService.AdvanceContracts(state);
            }

            Check("repeated misses breach the contract",
                breaker.status == ContractStatus.Breached, breaker.status.ToString());
            Check("breach happens at the threshold, not later",
                misses == RecurringContract.BreachThreshold,
                $"broke after {misses} misses, threshold is {RecurringContract.BreachThreshold}");

            // A single miss must NOT end an agreement, or the grace period is meaningless.
            RecurringContract stumbler = MakeContract(state, subject, 5);
            created.Add(stumbler);
            stumbler.TryAccept();
            stumbler.nextCycleTick = GenTicks.TicksGame;
            ContractService.AdvanceContracts(state);
            SalesOrder stumbleOrder = state.FindOrder(stumbler.activeOrderId);
            if (stumbleOrder != null)
            {
                createdOrders.Add(stumbleOrder);
                stumbleOrder.status = SalesOrderStatus.Failed;
                ContractService.AdvanceContracts(state);

                Check("one missed delivery does not end the agreement", stumbler.IsActive,
                    stumbler.status.ToString());

                // And a success afterwards must clear the strike.
                stumbler.nextCycleTick = GenTicks.TicksGame;
                ContractService.AdvanceContracts(state);
                SalesOrder recovery = state.FindOrder(stumbler.activeOrderId);
                if (recovery != null)
                {
                    createdOrders.Add(recovery);
                    recovery.deliveredQuantity = recovery.Quantity;
                    recovery.status = SalesOrderStatus.Completed;
                    ContractService.AdvanceContracts(state);
                    Check("a delivery clears the strike", stumbler.consecutiveFailures == 0,
                        stumbler.consecutiveFailures.ToString());
                }
            }

            // --- Standing agreements come from exact, repeated supply history ---
            // Isolate this block from detailed orders. Every assertion calls the public
            // production BuildOffer path, and its completed sale fixtures exist only in the
            // durable aggregate. If eligibility regresses to scanning state.Orders these fail.
            SettlementEconomicProfile profile = state.GetProfile(subject);
            Check("contract test settlement has an economic profile", profile != null);
            List<SalesOrder> savedOrders = new List<SalesOrder>(state.Orders);
            ThingDef temporarilyBlacklistedDef = null;
            state.Orders.Clear();
            try
            {
                ThingDef meat = IntercolonyProductClassifier.TradableDefs.Find(
                    def => def.IsMeat && def.stackLimit > 1 && def.category == ThingCategory.Item);
                ThingDef rice = DefDatabase<ThingDef>.GetNamedSilentFail("RawRice");
                ThingDef cloth = ThingDefOf.Cloth;

                bool fixturesValid = meat != null && rice != null && cloth != null &&
                                     IntercolonyProductClassifier.IsFungibleTradeItem(rice) &&
                                     rice.stackLimit > 1 && rice.category == ThingCategory.Item;
                Check("supply-history fixtures are valid recurring goods", fixturesValid);

                if (fixturesValid && profile != null)
                {
                    List<SalesOrder> historyOrders = new List<SalesOrder>();

                    SalesOrder PlantHistoryOrder(
                        int settlementId, ThingDef def, SalesOrderStatus status,
                        int contractId = 0)
                    {
                        SalesOrder order = new SalesOrder
                        {
                            id = int.MinValue + historyOrders.Count,
                            settlementId = settlementId,
                            settlementName = subject.Label ?? "unnamed",
                            contractId = contractId,
                            line = new OrderLine(def, 100),
                            deliveredQuantity = status == SalesOrderStatus.Completed ? 100 : 0,
                            status = status
                        };

                        state.RecordCompletedSale(order);
                        historyOrders.Add(order);
                        return order;
                    }

                    void ClearHistory()
                    {
                        state.CommercialHistory.Clear();
                        historyOrders.Clear();
                    }

                    // Four completed meat sales from recurring cycles make only that exact good
                    // eligible; contractId does not make a genuine delivery count for less.
                    for (int i = 0; i < 4; i++)
                    {
                        PlantHistoryOrder(subject.ID, meat, SalesOrderStatus.Completed,
                            contractId: 7001);
                    }

                    RecurringContract fourMeat =
                        ContractService.BuildOffer(state, subject, profile, 100);
                    Check("four completed meat orders make meat eligible",
                        fourMeat != null && fourMeat.thingDef == meat);

                    bool onlySuppliedMeatOffered = true;
                    for (int seed = 0; seed < 40; seed++)
                    {
                        RecurringContract offer =
                            ContractService.BuildOffer(state, subject, profile, seed);
                        if (offer == null || offer.thingDef != meat || offer.thingDef == cloth)
                        {
                            onlySuppliedMeatOffered = false;
                            break;
                        }
                    }

                    Check("an unsupplied clothing good is never offered",
                        onlySuppliedMeatOffered);

                    // A different settlement's history cannot make anything eligible here.
                    ClearHistory();
                    int otherSettlementId = subject.ID == int.MaxValue
                        ? int.MinValue
                        : subject.ID + 1;
                    PlantHistoryOrder(otherSettlementId, meat, SalesOrderStatus.Completed);
                    PlantHistoryOrder(otherSettlementId, meat, SalesOrderStatus.Completed);
                    Check("one settlement's history does not leak into another",
                        ContractService.BuildOffer(state, subject, profile, 101) == null);

                    ClearHistory();
                    PlantHistoryOrder(subject.ID, meat, SalesOrderStatus.Completed);
                    PlantHistoryOrder(subject.ID, meat, SalesOrderStatus.Failed);
                    Check("a failed order does not increase completed history",
                        ContractService.BuildOffer(state, subject, profile, 102) == null);

                    ClearHistory();
                    PlantHistoryOrder(subject.ID, meat, SalesOrderStatus.Completed);
                    PlantHistoryOrder(subject.ID, meat, SalesOrderStatus.Cancelled);
                    Check("a cancelled order does not increase completed history",
                        ContractService.BuildOffer(state, subject, profile, 103) == null);

                    ClearHistory();
                    PlantHistoryOrder(subject.ID, meat, SalesOrderStatus.Completed);
                    Check("one completed order is below the history threshold",
                        ContractService.BuildOffer(state, subject, profile, 104) == null);
                    PlantHistoryOrder(subject.ID, meat, SalesOrderStatus.Completed);
                    RecurringContract exactlyTwo =
                        ContractService.BuildOffer(state, subject, profile, 104);
                    Check("two completed aggregate sales meet the history threshold",
                        exactlyTwo != null && exactlyTwo.thingDef == meat);

                    ClearHistory();
                    for (int i = 0; i < 4; i++)
                    {
                        PlantHistoryOrder(subject.ID, meat, SalesOrderStatus.Completed);
                    }

                    for (int i = 0; i < 2; i++)
                    {
                        PlantHistoryOrder(subject.ID, rice, SalesOrderStatus.Completed);
                    }

                    int meatOffers = 0;
                    int riceOffers = 0;
                    for (int seed = 200; seed < 320; seed++)
                    {
                        RecurringContract offer =
                            ContractService.BuildOffer(state, subject, profile, seed);
                        if (offer?.thingDef == meat) meatOffers++;
                        if (offer?.thingDef == rice) riceOffers++;
                    }

                    Check("repeat history weights candidate choice",
                        meatOffers > riceOffers && meatOffers + riceOffers == 120,
                        $"meat {meatOffers}, rice {riceOffers}");

                    List<string> firstSequence = new List<string>();
                    for (int seed = 400; seed < 440; seed++)
                    {
                        firstSequence.Add(
                            ContractService.BuildOffer(state, subject, profile, seed)?.thingDef?.defName);
                    }

                    historyOrders.Reverse();
                    state.CommercialHistory.Reverse();

                    bool stableSequence = true;
                    for (int seed = 400; seed < 440; seed++)
                    {
                        string actual = ContractService.BuildOffer(
                            state, subject, profile, seed)?.thingDef?.defName;
                        if (actual != firstSequence[seed - 400])
                        {
                            stableSequence = false;
                            break;
                        }
                    }

                    Check("candidate order is stable before seeded selection", stableSequence);

                    ClearHistory();
                    PlantHistoryOrder(subject.ID, null, SalesOrderStatus.Completed);
                    PlantHistoryOrder(subject.ID, null, SalesOrderStatus.Completed);
                    PlantHistoryOrder(subject.ID, meat, SalesOrderStatus.Completed);
                    PlantHistoryOrder(subject.ID, meat, SalesOrderStatus.Completed);
                    temporarilyBlacklistedDef = meat;
                    IntercolonyTradeBlacklist.AddRuntimeExclusion(
                        meat, "contract history self-test");
                    Check("missing and now-blacklisted defs are filtered out",
                        ContractService.BuildOffer(state, subject, profile, 105) == null);
                    IntercolonyTradeBlacklist.RemoveRuntimeExclusion(meat);
                    temporarilyBlacklistedDef = null;

                    ClearHistory();
                    Check("no qualifying history produces no offer",
                        ContractService.BuildOffer(state, subject, profile, 106) == null);

                    // Exercise the same deep-list Scribe path used by the world component,
                    // including the ThingDef reference rather than copying fields in memory.
                    List<CommercialHistoryEntry> savedHistory =
                        new List<CommercialHistoryEntry>
                        {
                            new CommercialHistoryEntry
                            {
                                settlementId = subject.ID,
                                thingDef = meat,
                                completedSaleCount = 2,
                                totalQuantitySupplied = 275
                            }
                        };
                    List<CommercialHistoryEntry> loadedHistory = null;
                    string historyRoundTripFailure = null;
                    string historyPath = Path.Combine(
                        Path.GetTempPath(), $"Intercolony-CommercialHistory-{Guid.NewGuid():N}.xml");
                    try
                    {
                        Scribe.saver.InitSaving(historyPath, "intercolonyCommercialHistoryTest");
                        Scribe_Collections.Look(
                            ref savedHistory, "commercialHistory", LookMode.Deep);
                        Scribe.saver.FinalizeSaving();

                        Scribe.loader.InitLoading(historyPath);
                        Scribe_Collections.Look(
                            ref loadedHistory, "commercialHistory", LookMode.Deep);
                        Scribe.loader.FinalizeLoading();
                    }
                    catch (Exception exception)
                    {
                        historyRoundTripFailure =
                            $"{exception.GetType().Name}: {exception.Message}";
                    }
                    finally
                    {
                        Scribe.ForceStop();
                        if (File.Exists(historyPath))
                        {
                            File.Delete(historyPath);
                        }
                    }

                    Check("commercial history survives a Scribe save/load round trip",
                        historyRoundTripFailure == null &&
                        loadedHistory?.Count == 1 &&
                        loadedHistory[0].settlementId == subject.ID &&
                        loadedHistory[0].thingDef == meat &&
                        loadedHistory[0].completedSaleCount == 2 &&
                        loadedHistory[0].totalQuantitySupplied == 275,
                        historyRoundTripFailure);

                    // --- Contract terms beat spot (§29: otherwise there is no reason to commit) ---
                    // Plant the real prerequisite rather than letting a null offer weaken this
                    // coverage into a skip.
                    for (int i = 0;
                         i < ContractService.MinimumCompletedOrdersForAgreement;
                         i++)
                    {
                        PlantHistoryOrder(subject.ID, meat, SalesOrderStatus.Completed);
                    }

                    RecurringContract real =
                        ContractService.BuildOffer(state, subject, profile, 12345);
                    Check("the price fixture builds a real history-gated offer",
                        real != null && real.thingDef == meat);
                    if (real != null)
                    {
                        IntercolonyProductCategory category =
                            IntercolonyProductClassifier.Classify(real.thingDef)
                            ?? IntercolonyProductCategory.Commodities;
                        float spot = IntercolonyPricing.UnitPrice(
                            real.thingDef, null, real.quantityPerCycle, profile, category,
                            MarketOpportunityGenerator.DistanceToPlayer(subject), null, out _);

                        Check("a contract pays more per unit than spot", real.unitPrice > spot,
                            $"contract {real.unitPrice:F2} vs spot {spot:F2} for {real.thingDef.label}");
                        Check("contract lots are worth committing to", real.CycleValue >= 500,
                            $"{real.CycleValue} silver per delivery");
                        sb.AppendLine($"  (real offer: {real.quantityPerCycle}x {real.thingDef.label} " +
                                      $"@ {real.unitPrice:F2} vs spot {spot:F2}, " +
                                      $"{real.CycleValue} silver per cycle)");
                    }
                }
            }
            finally
            {
                if (temporarilyBlacklistedDef != null)
                {
                    IntercolonyTradeBlacklist.RemoveRuntimeExclusion(temporarilyBlacklistedDef);
                }

                state.Orders.Clear();
                state.Orders.AddRange(savedOrders);
            }

            // --- Reputation gate (§28 "access to recurring contracts") ---
            Check("contracts require a real trading record",
                ContractService.MinimumReputation > CommercialReputation.StartingScore,
                $"threshold {ContractService.MinimumReputation} vs neutral {CommercialReputation.StartingScore}");
            }
            finally
            {
                // AdvanceContracts is global, while completion and breach adjust reputation.
                // Restore the exact pre-test objects so none of those production effects escape.
                state.Reputations.Remove(subject.ID);
                if (hadSubjectReputation)
                {
                    state.Reputations.Add(subject.ID, savedSubjectReputation);
                }

                state.Contracts.Clear();
                state.Contracts.AddRange(savedContracts);
                state.Orders.Clear();
                state.Orders.AddRange(savedStateOrders);
                state.CommercialHistory.Clear();
                state.CommercialHistory.AddRange(savedCommercialHistory);
            }

            return Summarize();
        }

        /// <summary>Leaves a live multi-cycle contract in the save for the reload check (§107).</summary>
        public static string PlantSaveLoadProbe(IntercolonyWorldComponent state)
        {
            Settlement subject = FirstAccessibleSettlement();
            if (subject == null)
            {
                return "No accessible settlement.";
            }

            RecurringContract contract = MakeContract(state, subject, 4);
            contract.settlementName = ProbeTag + " " + contract.settlementName;
            contract.TryAccept();
            contract.cyclesCompleted = 1;

            return $"Planted contract #{contract.id}: {contract.quantityPerCycle}x " +
                   $"{contract.ItemLabel()} x{contract.totalCycles}, 1 already delivered.\n" +
                   "Save, quit to menu, reload, then run \"Verify contract probe\".";
        }

        public static string VerifySaveLoadProbe(IntercolonyWorldComponent state)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Contract save/load verification");

            RecurringContract found = null;
            foreach (RecurringContract contract in state.Contracts)
            {
                if (contract.settlementName != null && contract.settlementName.StartsWith(ProbeTag))
                {
                    found = contract;
                    break;
                }
            }

            if (found == null)
            {
                sb.AppendLine("  FAIL: the planted contract did not survive the reload.");
                return sb.ToString();
            }

            sb.AppendLine($"  found: {found}");
            bool pass = found.IsActive &&
                        found.cyclesCompleted == 1 &&
                        found.totalCycles == 4 &&
                        found.quantityPerCycle > 0 &&
                        found.thingDef != null &&
                        found.unitPrice > 0f;

            sb.AppendLine($"  status {found.status}, {found.cyclesCompleted}/{found.totalCycles} " +
                          $"delivered, {found.quantityPerCycle}x {found.ItemLabel()} @ {found.unitPrice:F2}");
            sb.AppendLine(pass
                ? "  PASS: a multi-cycle contract survived save/load with its terms and progress."
                : "  FAIL: terms or progress were lost.");
            return sb.ToString();
        }

        private static RecurringContract MakeContract(
            IntercolonyWorldComponent state, Settlement settlement, int cycles)
        {
            ThingDef def = ThingDefOf.Steel != null &&
                           IntercolonyProductClassifier.IsFungibleTradeItem(ThingDefOf.Steel)
                ? ThingDefOf.Steel
                : IntercolonyProductClassifier.TradableDefs[0];

            RecurringContract contract = new RecurringContract
            {
                id = state.NextId(),
                settlementId = settlement.ID,
                settlementName = settlement.Label ?? "unnamed",
                factionName = settlement.Faction?.Name ?? "",
                thingDef = def,
                quantityPerCycle = 100,
                cadenceTicks = GenDate.TicksPerQuadrum,
                totalCycles = cycles,
                unitPrice = Mathf.Max(0.5f, IntercolonyPricing.BaseValue(def, null) * 1.15f),
                status = ContractStatus.Offered,
                offerExpiryTick = GenTicks.TicksGame + GenDate.TicksPerDay * 8
            };

            state.AddContract(contract);
            return contract;
        }

        private static Settlement FirstAccessibleSettlement()
        {
            foreach (Settlement settlement in Find.WorldObjects.Settlements)
            {
                if (SettlementProfileGenerator.IsEligible(settlement) &&
                    IntercolonyMarketAccess.IsAccessible(settlement))
                {
                    return settlement;
                }
            }

            return null;
        }

        private static int UnusedContractSettlementId(IntercolonyWorldComponent state)
        {
            int candidate = int.MinValue;
            while (true)
            {
                bool used = false;
                foreach (RecurringContract contract in state.Contracts)
                {
                    if (contract.settlementId == candidate)
                    {
                        used = true;
                        break;
                    }
                }

                if (!used)
                {
                    return candidate;
                }

                candidate++;
            }
        }
    }
}
