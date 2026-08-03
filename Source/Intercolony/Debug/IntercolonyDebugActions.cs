using System;
using System.Collections.Generic;
using System.Text;
using LudeonTK;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Dev-mode actions for inspecting and forcing world state (DESIGN.md §67, §95).
    /// Found under Debug actions → category "Intercolony" (default key `/`).
    /// </summary>
    public static class IntercolonyDebugActions
    {
        private const string Category = "Intercolony";

        [DebugAction(Category, "Open debug window", allowedGameStates = AllowedGameStates.Playing, displayPriority = 100)]
        private static void OpenDebugWindow()
        {
            IntercolonyDebugWindow.Toggle();
        }

        [DebugAction(Category, "Dump state", allowedGameStates = AllowedGameStates.Playing, displayPriority = 90)]
        private static void DumpState()
        {
            WithState(state => IntercolonyLog.Message(state.DebugStateSummary()));
        }

        [DebugAction(Category, "Dump settlement profiles", allowedGameStates = AllowedGameStates.Playing, displayPriority = 80)]
        private static void DumpSettlementProfiles()
        {
            WithState(state =>
            {
                List<SettlementEconomicProfile> profiles = state.AllProfiles();
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Settlement economic profiles ({profiles.Count} eligible, economy seed {state.EconomySeed})");
                foreach (SettlementEconomicProfile profile in profiles)
                {
                    sb.AppendLine();
                    sb.Append(profile.DebugSummary());
                }

                IntercolonyLog.Message(sb.ToString());
            });
        }

        /// <summary>
        /// Click a settlement on the world map to print its profile. A ToolWorld action is
        /// only offered while the world map is rendered, and must read the hovered tile itself
        /// (see <c>DebugActionNode.cs:286</c> — the tool is handed a plain Action).
        /// </summary>
        [DebugAction(Category, "Inspect settlement profile", actionType = DebugActionType.ToolWorld,
            allowedGameStates = AllowedGameStates.PlayingOnWorld, displayPriority = 70)]
        private static void InspectSettlementProfile()
        {
            WithState(state =>
            {
                PlanetTile tile = GenWorld.MouseTile();
                Settlement settlement = Find.WorldObjects.SettlementAt(tile);
                if (settlement == null)
                {
                    IntercolonyLog.Message($"No settlement at tile {tile}.");
                    return;
                }

                SettlementEconomicProfile profile = state.GetProfile(settlement);
                if (profile == null)
                {
                    IntercolonyLog.Message(
                        $"{settlement.Label} is not an economic participant " +
                        $"(faction: {settlement.Faction?.Name ?? "none"}).");
                    return;
                }

                IntercolonyLog.Message(profile.DebugSummary());
            });
        }

        [DebugAction(Category, "Run profile self-test", allowedGameStates = AllowedGameStates.Playing, displayPriority = 60)]
        private static void RunProfileSelfTest()
        {
            IntercolonyLog.Message(IntercolonyProfileSelfTest.Run());
        }

        [DebugAction(Category, "Run market self-test", allowedGameStates = AllowedGameStates.Playing, displayPriority = 59)]
        private static void RunMarketSelfTest()
        {
            WithState(state => IntercolonyLog.Message(IntercolonyMarketSelfTest.Run(state)));
        }

        /// <summary>
        /// Destroys a settlement to prove §87 handling. Genuinely destructive — intended for a
        /// throwaway <c>-quicktest</c> world, not a save you care about.
        /// </summary>
        [DebugAction(Category, "Test settlement removal (DESTRUCTIVE)", allowedGameStates = AllowedGameStates.Playing, displayPriority = 50)]
        private static void TestSettlementRemoval()
        {
            WithState(state =>
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Settlement removal test (DESIGN.md §87)");

                Settlement victim = null;
                foreach (Settlement settlement in Find.WorldObjects.Settlements)
                {
                    if (SettlementProfileGenerator.IsEligible(settlement))
                    {
                        victim = settlement;
                        break;
                    }
                }

                if (victim == null)
                {
                    IntercolonyLog.Warning("No eligible settlement to remove.");
                    return;
                }

                int id = victim.ID;
                int before = state.AllProfiles().Count;
                bool profileBefore = state.GetProfile(victim) != null;
                bool cachedBefore = state.HasCachedProfile(id);
                sb.AppendLine($"  victim: {victim.Label} (id {id})");
                sb.AppendLine($"  before: {before} eligible, profile={profileBefore}, cached={cachedBefore}");

                Find.WorldObjects.Remove(victim);

                bool eligibleAfter = SettlementProfileGenerator.IsEligible(victim);
                bool profileAfter = state.GetProfile(victim) != null;
                int after = state.AllProfiles().Count;
                sb.AppendLine($"  after removal: {after} eligible, IsEligible={eligibleAfter}, profile={profileAfter}");

                state.PruneProfileCacheNow();
                bool cachedAfterPrune = state.HasCachedProfile(id);
                sb.AppendLine($"  after prune: cached={cachedAfterPrune}");

                bool pass = profileBefore && !eligibleAfter && !profileAfter && after == before - 1 && !cachedAfterPrune;
                sb.AppendLine(pass
                    ? "  PASS: removal handled gracefully, no orphan profile left."
                    : "  FAIL: see values above.");

                IntercolonyLog.Message(sb.ToString());
            });
        }

        [DebugAction(Category, "Clear profile cache", allowedGameStates = AllowedGameStates.Playing)]
        private static void ClearProfileCache()
        {
            WithState(state => state.ClearProfileCache());
        }

        [DebugAction(Category, "Reroll economy seed", allowedGameStates = AllowedGameStates.Playing)]
        private static void RerollEconomySeed()
        {
            WithState(state =>
            {
                state.RerollEconomySeed();
                Report($"Economy seed is now {state.EconomySeed}.");
            });
        }

        [DebugAction(Category, "Dump opportunities", allowedGameStates = AllowedGameStates.Playing, displayPriority = 85)]
        private static void DumpOpportunities()
        {
            WithState(state =>
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Market opportunities ({state.Opportunities.Count} listed, " +
                              $"{state.ActiveOpportunityCount} available, refresh #{state.RefreshCount})");
                foreach (MarketOpportunity opportunity in state.Opportunities)
                {
                    sb.AppendLine();
                    sb.AppendLine($"  {opportunity}");
                    sb.AppendLine($"    expires in {opportunity.DaysRemaining:F1}d, " +
                                  $"delivery deadline {opportunity.deadlineDays}d");
                    foreach (string line in opportunity.priceExplanation.Split('\n'))
                    {
                        if (!string.IsNullOrEmpty(line.Trim()))
                        {
                            sb.AppendLine("    " + line.TrimEnd());
                        }
                    }
                }

                IntercolonyLog.Message(sb.ToString());
            });
        }

        [DebugAction(Category, "Dump orders", allowedGameStates = AllowedGameStates.Playing, displayPriority = 84)]
        private static void DumpOrders()
        {
            WithState(state =>
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Sales orders ({state.Orders.Count} total, {state.OpenOrderCount} open)");
                foreach (SalesOrder order in state.Orders)
                {
                    sb.AppendLine($"  {order}");
                    sb.AppendLine($"    accepted@{order.acceptedTick} deadline@{order.deadlineTick} " +
                                  $"({order.DaysRemaining:F1}d left), paid {order.paidSilver}/{order.TotalPayment}");
                    if (!string.IsNullOrEmpty(order.outcomeNote))
                    {
                        sb.AppendLine($"    {order.outcomeNote}");
                    }
                }

                IntercolonyLog.Message(sb.ToString());
            });
        }

        /// <summary>
        /// Accepts an offer without clicking through the market tab. Sets up a test; it does
        /// not bypass delivery, which still has to happen physically.
        /// </summary>
        [DebugAction(Category, "Accept first offer", allowedGameStates = AllowedGameStates.Playing, displayPriority = 83)]
        private static void AcceptFirstOffer()
        {
            WithState(state =>
            {
                foreach (MarketOpportunity opportunity in new List<MarketOpportunity>(state.Opportunities))
                {
                    if (!opportunity.IsAvailable)
                    {
                        continue;
                    }

                    SalesOrder order = SalesOrderService.Accept(state, opportunity);
                    if (order != null)
                    {
                        Report($"Accepted order #{order.id}.");
                        return;
                    }
                }

                IntercolonyLog.Warning("No acceptable offer found.");
            });
        }

        /// <summary>
        /// Drops the goods an open order needs at the colony, so the delivery half of the loop
        /// can be exercised without first farming them. The caravan trip and hand-over are
        /// still entirely real.
        /// </summary>
        [DebugAction(Category, "Spawn goods for open orders", allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 82)]
        private static void SpawnGoodsForOpenOrders()
        {
            WithState(state =>
            {
                Map map = Find.CurrentMap;
                if (map == null)
                {
                    IntercolonyLog.Warning("No current map.");
                    return;
                }

                int spawned = 0;
                foreach (SalesOrder order in state.Orders)
                {
                    if (!order.IsOpen || order.ThingDef == null)
                    {
                        continue;
                    }

                    ThingDef def = order.ThingDef;

                    // Goods must actually satisfy the line, or the helper produces stock that
                    // cannot be delivered and the order system looks broken when it is not.
                    // Passing no stuff for a MadeFromStuff def also makes RimWorld log a red
                    // "madeFromStuff but stuff=null" error and pick a material for us.
                    ThingDef stuff = null;
                    if (def.MadeFromStuff)
                    {
                        stuff = order.line?.allowedStuff ?? GenStuff.DefaultStuffFor(def);
                    }

                    int needed = order.RemainingQuantity;
                    IntVec3 cell = DropCellFinder.TradeDropSpot(map);
                    while (needed > 0)
                    {
                        int stack = Mathf.Min(needed, Mathf.Max(1, def.stackLimit));
                        Thing thing = ThingMaker.MakeThing(def, stuff);
                        thing.stackCount = stack;

                        // Meet the quality floor, otherwise the delivery is correctly refused.
                        if (order.line?.minQuality != null)
                        {
                            thing.TryGetComp<CompQuality>()?
                                .SetQuality(order.line.minQuality.Value, ArtGenerationContext.Outsider);
                        }

                        // Crated goods have to arrive haulable. Spawning a building directly
                        // would install it, forcing an uninstall before it could be caravanned.
                        Thing toPlace = def.Minifiable ? thing.TryMakeMinified() : thing;

                        GenPlace.TryPlaceThing(toPlace, cell, map, ThingPlaceMode.Near);
                        needed -= stack;
                        spawned += stack;
                    }
                }

                Report(spawned > 0
                    ? $"Spawned {spawned} units at the trade drop spot."
                    : "No open orders needing goods.");
            });
        }

        /// <summary>
        /// Creates one order in each lifecycle state so §98's save/load matrix can be checked
        /// in a single save-and-reload rather than five separate play sessions.
        /// </summary>
        [DebugAction(Category, "Create order state matrix", allowedGameStates = AllowedGameStates.Playing, displayPriority = 81)]
        private static void CreateOrderStateMatrix()
        {
            WithState(state =>
            {
                ThingDef def = ThingDefOf.Silver;

                SalesOrder MakeOrder(string label)
                {
                    SalesOrder order = new SalesOrder
                    {
                        id = state.NextId(),
                        settlementName = "MatrixTest " + label,
                        factionName = "MatrixFaction",
                        line = new OrderLine(def, 100),
                        unitPrice = 1.5f,
                        acceptedTick = GenTicks.TicksGame,
                        deadlineTick = GenTicks.TicksGame + GenDate.TicksPerDay * 10,
                        status = SalesOrderStatus.Accepted
                    };

                    state.AddOrder(order);
                    return order;
                }

                // Open, untouched.
                MakeOrder("open");

                // Open with a partial delivery recorded, to prove progress survives too.
                SalesOrder partial = MakeOrder("partial");
                partial.deliveredQuantity = 40;
                partial.paidSilver = partial.PaymentFor(40);

                // Completion is only reachable through a real delivery (SalesOrderService
                // .Complete is private by design), so the matrix sets the terminal state
                // directly. That is fine here: this checks persistence of the state, and a
                // genuine completion was already exercised in play.
                SalesOrder completed = MakeOrder("completed");
                completed.deliveredQuantity = completed.Quantity;
                completed.paidSilver = completed.TotalPayment;
                completed.status = SalesOrderStatus.Completed;
                completed.outcomeNote = $"Delivered {completed.deliveredQuantity} units for {completed.paidSilver} silver.";

                SalesOrder failed = MakeOrder("failed");
                SalesOrderService.Fail(failed, "Matrix test failure.");

                SalesOrder cancelled = MakeOrder("cancelled");
                SalesOrderService.Cancel(cancelled);

                Report("Created 5 matrix orders. Dump orders, save, quit to menu, reload, dump again.");
            });
        }

        /// <summary>
        /// §33's mandatory labor spike. Generates a foreign pawn, transfers it to the player
        /// faction, probes what works, restores it, and destroys the probe.
        /// </summary>
        [DebugAction(Category, "Run labor control spike", allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 51)]
        private static void RunLaborSpike()
        {
            IntercolonyLog.Message(IntercolonyLaborSpike.Run(Find.CurrentMap));
        }

        [DebugAction(Category, "Run contract self-test", allowedGameStates = AllowedGameStates.Playing, displayPriority = 58)]
        private static void RunContractSelfTest()
        {
            WithState(state => IntercolonyLog.Message(IntercolonyContractSelfTest.Run(state)));
        }

        [DebugAction(Category, "Plant contract probe", allowedGameStates = AllowedGameStates.Playing, displayPriority = 54)]
        private static void PlantContractProbe()
        {
            WithState(state => IntercolonyLog.Message(IntercolonyContractSelfTest.PlantSaveLoadProbe(state)));
        }

        [DebugAction(Category, "Verify contract probe", allowedGameStates = AllowedGameStates.Playing, displayPriority = 53)]
        private static void VerifyContractProbe()
        {
            WithState(state => IntercolonyLog.Message(IntercolonyContractSelfTest.VerifySaveLoadProbe(state)));
        }

        /// <summary>Forces a settlement to propose an agreement, bypassing the reputation gate.</summary>
        [DebugAction(Category, "Offer contract (force)", allowedGameStates = AllowedGameStates.Playing, displayPriority = 52)]
        private static void ForceOfferContract()
        {
            WithState(state =>
            {
                foreach (Settlement settlement in Find.WorldObjects.Settlements)
                {
                    if (!IntercolonyMarketAccess.IsAccessible(settlement) ||
                        state.HasContractWith(settlement.ID))
                    {
                        continue;
                    }

                    CommercialReputation rep = state.GetOrCreateReputation(settlement);
                    rep.Adjust(ContractService.MinimumReputation + 5f - rep.Score);

                    // "Force" should force. Going through OfferContracts left it to a 12%
                    // roll on a fixed seed, so the action could never succeed for that
                    // settlement no matter how many times it was clicked.
                    SettlementEconomicProfile profile = state.GetProfile(settlement);
                    RecurringContract offer = ContractService.BuildOffer(
                        state, settlement, profile, Rand.Int);

                    if (offer != null)
                    {
                        state.AddContract(offer);
                        Report($"{settlement.Label} offered an agreement: " +
                               $"{offer.quantityPerCycle}x {offer.thingDef.label} x{offer.totalCycles}.");
                    }
                    else
                    {
                        IntercolonyLog.Warning("Could not build an offer.");
                    }

                    return;
                }

                IntercolonyLog.Warning("No eligible settlement without an existing agreement.");
            });
        }

        [DebugAction(Category, "Dump contracts", allowedGameStates = AllowedGameStates.Playing, displayPriority = 81)]
        private static void DumpContracts()
        {
            WithState(state =>
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Recurring contracts ({state.Contracts.Count})");
                foreach (RecurringContract contract in state.Contracts)
                {
                    sb.AppendLine($"  {contract}");
                    if (!string.IsNullOrEmpty(contract.outcomeNote))
                    {
                        sb.AppendLine($"    {contract.outcomeNote}");
                    }
                }

                IntercolonyLog.Message(sb.ToString());
            });
        }

        [DebugAction(Category, "Run reputation self-test", allowedGameStates = AllowedGameStates.Playing, displayPriority = 57)]
        private static void RunReputationSelfTest()
        {
            WithState(state => IntercolonyLog.Message(IntercolonyReputationSelfTest.Run(state)));
        }

        [DebugAction(Category, "Dump reputations", allowedGameStates = AllowedGameStates.Playing, displayPriority = 82)]
        private static void DumpReputations()
        {
            WithState(state =>
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Commercial reputations ({state.Reputations.Count})");
                foreach (KeyValuePair<int, CommercialReputation> entry in state.Reputations)
                {
                    sb.AppendLine($"  {entry.Value}");
                }

                IntercolonyLog.Message(sb.ToString());
            });
        }

        [DebugAction(Category, "Run RFQ self-test", allowedGameStates = AllowedGameStates.Playing, displayPriority = 56)]
        private static void RunRfqSelfTest()
        {
            WithState(state => IntercolonyLog.Message(IntercolonyRfqSelfTest.Run(state)));
        }

        [DebugAction(Category, "Dump requests", allowedGameStates = AllowedGameStates.Playing, displayPriority = 83)]
        private static void DumpRequests()
        {
            WithState(state =>
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Purchase requests ({state.Requests.Count} total, {state.OpenRequestCount} open)");
                foreach (PurchaseRequest request in state.Requests)
                {
                    sb.AppendLine($"  {request}");
                    if (!request.AnyQuotes)
                    {
                        sb.AppendLine($"    no quotes: {request.noResponseReason}");
                    }

                    foreach (Quotation quote in request.quotes)
                    {
                        sb.AppendLine($"    {quote}");
                    }
                }

                IntercolonyLog.Message(sb.ToString());
            });
        }

        [DebugAction(Category, "Run unique goods spike", allowedGameStates = AllowedGameStates.Playing, displayPriority = 56)]
        private static void RunUniqueGoodsSpike()
        {
            IntercolonyLog.Message(IntercolonyUniqueGoodsSpike.Run());
        }

        [DebugAction(Category, "Plant unique goods probes", allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 56)]
        private static void PlantUniqueGoodsProbes()
        {
            IntercolonyLog.Message(IntercolonyUniqueGoodsSpike.PlantSaveLoadProbes(Find.CurrentMap));
        }

        [DebugAction(Category, "Verify unique goods probes", allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 55)]
        private static void VerifyUniqueGoodsProbes()
        {
            IntercolonyLog.Message(IntercolonyUniqueGoodsSpike.VerifySaveLoadProbes(Find.CurrentMap));
        }

        [DebugAction(Category, "Run order self-test", allowedGameStates = AllowedGameStates.Playing, displayPriority = 58)]
        private static void RunOrderSelfTest()
        {
            WithState(state => IntercolonyLog.Message(IntercolonyOrderSelfTest.Run(state)));
        }

        [DebugAction(Category, "Dump product classification", allowedGameStates = AllowedGameStates.Playing, displayPriority = 45)]
        private static void DumpProductClassification()
        {
            IntercolonyLog.Message(IntercolonyProductClassifier.DebugHistogram());
        }

        [DebugAction(Category, "Dump trade blacklist", allowedGameStates = AllowedGameStates.Playing, displayPriority = 44)]
        private static void DumpTradeBlacklist()
        {
            IntercolonyLog.Message(IntercolonyTradeBlacklist.DebugSummary());
        }

        [DebugAction(Category, "Run employer reputation self-test", allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 61)]
        private static void RunEmployerReputationSelfTest()
        {
            WithState(state => IntercolonyLog.Message(
                IntercolonyEmployerReputationSelfTest.Run(state, Find.CurrentMap)));
        }

        /// <summary>
        /// Backdates an active employment past §116's tenure bar so the attachment offer fires now.
        ///
        /// Exists because the thing worth testing about §44 is what happens to the *pawn* — whether a
        /// quest lodger really becomes a colonist in place, or walks off the map like every other
        /// employment ending does — and that was gated behind thirty in-game days of waiting. The
        /// wait is the design; it is not the part that can be wrong.
        ///
        /// Backdating `arrivedTick` rather than setting a flag, so every downstream reading is the
        /// real one: severance, notice length and the eligibility gates all price this worker as
        /// genuinely long-serving, because as far as the contract is concerned they are.
        /// </summary>
        /// <summary>
        /// Winds an employment forward to the brink of its term so §115's renewal question fires now.
        ///
        /// **Added because four of the six remaining play-tests were blocked on the same thing:**
        /// nothing could fast-forward a contract, so renewal, supply renewal and the long-run checks
        /// all required sitting through real in-game weeks. A feature that can only be tested by
        /// waiting is a feature that does not get tested.
        ///
        /// Moves the clock rather than setting a flag, so everything downstream reads the real
        /// thing: the worker has genuinely served nearly the whole term, payroll has genuinely run,
        /// and <see cref="RenewalService.WouldRenew"/> weighs the record it actually finds.
        /// </summary>
        [DebugAction(Category, "Force renewal offer", allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 37)]
        private static void ForceRenewalOffer()
        {
            WithState(state =>
            {
                EmploymentContract target = null;
                foreach (EmploymentContract contract in state.Employments)
                {
                    if (contract.status == EmploymentStatus.Active && contract.pawn != null &&
                        !contract.renewalOffered && !contract.ServingNotice)
                    {
                        target = contract;
                        break;
                    }
                }

                if (target == null)
                {
                    Report("No employee awaiting a renewal decision. Hire one on a *fixed* term and " +
                           "use \"Arrive employees now\".");
                    return;
                }

                if (target.IsOpenEnded)
                {
                    Report($"{target.workerName} is on an open-ended contract — there is no term to " +
                           "renew. Hire someone on a fixed term instead.");
                    return;
                }

                // Far enough back that the term is nearly served, and past the three-day floor
                // WouldRenew applies to workers who have barely arrived.
                target.arrivedTick = GenTicks.TicksGame -
                                     (target.termDays - RenewalService.OfferLeadDays + 1) * GenDate.TicksPerDay;
                target.endTick = GenTicks.TicksGame +
                                 (RenewalService.OfferLeadDays - 1) * GenDate.TicksPerDay;

                RenewalService.Advance(target);

                if (RenewalService.HasLiveOffer(target))
                {
                    Report($"{target.workerName} has asked to stay on at {target.renewalWage}/day " +
                           $"(was {target.dailyWage}). Answer on their row in Labor -> Employees.");
                    return;
                }

                RenewalService.WouldRenew(state, target, out string refusal);
                Report($"{target.workerName} was asked and will not re-sign: {refusal} " +
                       "(that is the other half of the test — the letter should say the same).");
            });
        }

        /// <summary>
        /// Completes a recurring supply agreement so §115's renewal offer fires on it.
        ///
        /// Marks every remaining cycle delivered rather than faking the offer, so the settlement
        /// weighs a genuinely clean run — and a contract with a missed delivery in its history still
        /// correctly gets no offer, which is the half of the behaviour worth checking.
        /// </summary>
        /// <summary>
        /// Accepts the first standing-agreement proposal on the table.
        ///
        /// Distinct from "Accept first offer", which takes a *market opportunity* and produces a
        /// one-off sales order. The two were easy to confuse — a play-test followed the wrong one
        /// and got "No acceptable offer found" from an action that was working perfectly on a
        /// different kind of offer entirely.
        /// </summary>
        [DebugAction(Category, "Accept first contract offer", allowedGameStates = AllowedGameStates.Playing, displayPriority = 51)]
        private static void AcceptFirstContractOffer()
        {
            WithState(state =>
            {
                foreach (RecurringContract contract in state.Contracts)
                {
                    if (!contract.IsOffer)
                    {
                        continue;
                    }

                    if (ContractService.AcceptOffer(state, contract))
                    {
                        Report($"Accepted the agreement with {contract.settlementName}: " +
                               $"{contract.quantityPerCycle}x {contract.ItemLabel()} every " +
                               $"{contract.CadenceDays:F0} days, {contract.totalCycles} times.");
                        return;
                    }
                }

                Report("No standing-agreement proposal on the table. Use \"Offer contract (force)\" " +
                       "first. (Note: \"Accept first offer\" is a different thing — it takes a market " +
                       "opportunity, not an agreement.)");
            });
        }

        [DebugAction(Category, "Force supply agreement to complete", allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 36)]
        private static void ForceContractCompletion()
        {
            WithState(state =>
            {
                RecurringContract target = null;
                foreach (RecurringContract contract in state.Contracts)
                {
                    if (contract.IsActive)
                    {
                        target = contract;
                        break;
                    }
                }

                if (target == null)
                {
                    Report("No active supply agreement. Use \"Offer contract (force)\" then " +
                           "\"Accept first offer\" to get one, then run this.");
                    return;
                }

                int remaining = target.CyclesRemaining;
                target.cyclesCompleted += remaining;
                target.consecutiveFailures = 0;

                // Withdraw any order still in flight, or it would resolve as a miss against an
                // agreement that has just been credited as fully delivered.
                if (target.activeOrderId != 0)
                {
                    SalesOrder inFlight = state.FindOrder(target.activeOrderId);
                    if (inFlight != null && inFlight.IsOpen)
                    {
                        inFlight.status = SalesOrderStatus.Cancelled;
                        inFlight.outcomeNote = "Withdrawn by a debug action.";
                    }

                    target.activeOrderId = 0;
                }

                // The real completion path, not a hand-rolled imitation of it. Crediting the cycles
                // and calling AdvanceContracts was not enough: AdvanceContracts only completes a
                // contract when an *order* resolves, so the agreement stayed Active with nothing
                // left to deliver and no renewal was ever offered.
                ContractService.Complete(state, target);

                Report(target.renewalOffered
                    ? $"{target.settlementName} completed ({remaining} cycle(s) credited) and has " +
                      "offered to renew. Answer in the Contracts tab."
                    : $"{target.settlementName} completed ({remaining} cycle(s) credited). " +
                      $"No renewal offered — {target.outcomeNote.Trim()}");
            });
        }

        [DebugAction(Category, "Force attachment offer", allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 39)]
        private static void ForceAttachmentOffer()
        {
            WithState(state =>
            {
                EmploymentContract target = null;
                foreach (EmploymentContract contract in state.Employments)
                {
                    if (contract.status == EmploymentStatus.Active && contract.pawn != null &&
                        !contract.transitionResolved)
                    {
                        target = contract;
                        break;
                    }
                }

                if (target == null)
                {
                    Report("No active employee to attach. Hire one and use \"Arrive employees now\".");
                    return;
                }

                target.arrivedTick = GenTicks.TicksGame -
                                     (TransitionService.RequiredTenureDays + 5) * GenDate.TicksPerDay;

                // Clear the conduct gates too, so the offer is not blocked by a payroll that
                // happened to slip while the test was being set up.
                target.arrearsSilver = 0;
                target.missedPayments = 0;
                target.clauseBreaches = 0;
                target.transitionOffered = false;
                target.transitionOfferedTick = -1;

                TransitionService.Advance(state, target);

                // The offer is worthless without the means to take it up, and the fee is large by
                // design (§116) — 180 days of the worker's wage. Granted here so the play-test can
                // reach the part that matters, which is what happens to the pawn.
                GrantSilver(Find.CurrentMap,
                    Mathf.CeilToInt(TransitionService.ReleaseFee(state, target) * 1.2f),
                    "the release fee");

                if (!TransitionService.HasLiveOffer(target))
                {
                    TransitionService.MeetsTerms(state, target, out string blocker);
                    Report($"{target.workerName} still will not settle: {blocker}");
                    return;
                }

                Report($"{target.workerName} now has {target.TenureDays:0} days' tenure and has asked " +
                       $"to stay. Release fee {TransitionService.ReleaseFee(state, target)} silver. " +
                       "Answer on their row in Labor -> Employees.");
            });
        }

        /// <summary>
        /// Checks that a converted employee actually became a colonist, rather than looking like one.
        ///
        /// The failure this exists to catch is specific and quiet: if the pawn is not removed from
        /// the quest's <c>QuestPart_Leave</c> before the quest ends, they are handed a
        /// <c>LordJob_ExitMapBest</c> and walk off the map — exactly what every other employment
        /// ending is meant to do. Watching them for a minute would show it; so would this, instantly
        /// and without doubt.
        ///
        /// The contract's pawn reference is cleared on conversion (it must be — a closed record
        /// holding a live pawn is how saves grow dangling references), so the worker is found again
        /// by the name the record froze at hire.
        /// </summary>
        [DebugAction(Category, "Verify converted employees", allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 38)]
        private static void VerifyConvertedEmployees()
        {
            WithState(state =>
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Converted employees (§44, §116)");

                int converted = 0;
                int sound = 0;

                foreach (EmploymentContract contract in state.Employments)
                {
                    if (contract.status != EmploymentStatus.Converted)
                    {
                        continue;
                    }

                    converted++;

                    Pawn found = null;
                    foreach (Map map in Find.Maps)
                    {
                        foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                        {
                            if (pawn.LabelShortCap == contract.workerName)
                            {
                                found = pawn;
                                break;
                            }
                        }

                        if (found != null)
                        {
                            break;
                        }
                    }

                    sb.AppendLine();
                    sb.AppendLine($"  {contract.workerName} — {contract.outcomeNote}");

                    if (found == null)
                    {
                        sb.AppendLine("    FAIL: not on any map. They left — the conversion did not " +
                                      "take them off the quest's departure list.");
                        continue;
                    }

                    bool playerFaction = found.Faction == Faction.OfPlayer;
                    bool lodger = found.IsQuestLodger();
                    bool stillEmployee = EmploymentService.IsEmployee(found);
                    bool colonist = found.IsColonist;

                    sb.AppendLine($"    spawned      : yes, on {found.Map?.Parent?.Label ?? "a map"}");
                    sb.AppendLine($"    faction      : {found.Faction?.Name ?? "none"}" +
                                  (playerFaction ? "" : "   <-- FAIL, should be yours"));
                    sb.AppendLine($"    quest lodger : {lodger}" +
                                  (lodger ? "   <-- FAIL, should be false" : ""));
                    sb.AppendLine($"    IsColonist   : {colonist}" +
                                  (colonist ? "" : "   <-- FAIL"));
                    sb.AppendLine($"    still an employee: {stillEmployee}" +
                                  (stillEmployee ? "   <-- FAIL" : ""));
                    sb.AppendLine($"    kindDef      : {found.kindDef?.defName}");
                    sb.AppendLine($"    drafter      : {(found.drafter != null ? "present" : "MISSING")}");

                    bool ok = playerFaction && !lodger && !stillEmployee && colonist;
                    if (ok)
                    {
                        sound++;
                    }

                    sb.AppendLine(ok
                        ? "    PASS: a real colonist, in place."
                        : "    FAIL: see the marked lines above.");
                }

                if (converted == 0)
                {
                    sb.AppendLine("  None yet. Use \"Force attachment offer\", then Keep them.");
                }
                else
                {
                    sb.AppendLine();
                    sb.AppendLine($"  {sound} of {converted} converted correctly.");
                }

                IntercolonyLog.Message(sb.ToString());
            });
        }

        [DebugAction(Category, "Run ledger self-test", allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 66)]
        private static void RunLedgerSelfTest()
        {
            WithState(state => IntercolonyLog.Message(
                IntercolonyLedgerSelfTest.Run(state, Find.CurrentMap)));
        }

        [DebugAction(Category, "Run transition self-test", allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 65)]
        private static void RunTransitionSelfTest()
        {
            WithState(state => IntercolonyLog.Message(
                IntercolonyTransitionSelfTest.Run(state, Find.CurrentMap)));
        }

        [DebugAction(Category, "Run long-term employment self-test", allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 64)]
        private static void RunLongTermSelfTest()
        {
            WithState(state => IntercolonyLog.Message(
                IntercolonyLongTermSelfTest.Run(state, Find.CurrentMap)));
        }

        [DebugAction(Category, "Run job posting self-test", allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 63)]
        private static void RunJobPostingSelfTest()
        {
            WithState(state => IntercolonyLog.Message(
                IntercolonyJobPostingSelfTest.Run(state, Find.CurrentMap)));
        }

        [DebugAction(Category, "Run combat clause self-test", allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 62)]
        private static void RunCombatClauseSelfTest()
        {
            WithState(state => IntercolonyLog.Message(
                IntercolonyCombatClauseSelfTest.Run(state, Find.CurrentMap)));
        }

        /// <summary>
        /// Declares war on an employee's own faction, so §88's safe passage can be watched rather
        /// than only asserted.
        ///
        /// This exists because safe passage is the one part of Phase 20 a self-test cannot prove:
        /// it is about what a spawned pawn does over the next two in-game days, whether the turrets
        /// hold their fire, and whether they actually reach the map edge. None of that is arithmetic.
        ///
        /// It changes real faction relations, which is why it says so and asks first.
        /// </summary>
        [DebugAction(Category, "Force war with an employee's faction", allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 40)]
        private static void ForceWarWithEmployer()
        {
            WithState(state =>
            {
                EmploymentContract target = null;
                foreach (EmploymentContract contract in state.Employments)
                {
                    if (contract.IsOpen && contract.employerFaction != null &&
                        !HostilityPolicy.IsAtWar(contract.employerFaction))
                    {
                        target = contract;
                        break;
                    }
                }

                if (target == null)
                {
                    Report("No employee whose faction is not already at war. Hire someone first.");
                    return;
                }

                Faction faction = target.employerFaction;

                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    $"Declare war between {faction.Name} and your colony?\n\n" +
                    $"{target.workerName} is employed from {target.settlementName}, and §88's policy " +
                    "will release them under safe passage within the hour.\n\n" +
                    "This permanently changes real faction relations in this save. Use a throwaway " +
                    "colony.",
                    () => DeclareWar(state, faction, target),
                    destructive: true));
            });
        }

        /// <summary>
        /// Sets the relation to hostile directly instead of pushing goodwill down until vanilla
        /// notices.
        ///
        /// The first version did the natural thing —
        /// <c>faction.TryAffectGoodwillWith(player, faction.GoodwillToMakeHostile(player), ...)</c>
        /// — and it threw a NullReferenceException inside RimWorld. The cause is worth recording
        /// because nothing about it is Intercolony's:
        ///
        /// <c>Faction.RelationWith</c> returns a **dummy `FactionRelation` whose `other` is null**
        /// when no relation exists, rather than failing. `GoodwillToMakeHostile` walks
        /// `GoodwillWith` → `GetMaxGoodwill` → `GetSituations` → `Recalculate` →
        /// `CheckHostilityChanged` → `Notify_GoodwillSituationsChanged` → `CheckKindThresholds`,
        /// which calls `faction.GoodwillWith(relation.other)` — null for a dummy — and
        /// `GoodwillSituationManager.GetSituations(null)` returns null, so `GetMaxGoodwill`
        /// dereferences it. Any faction in the world with an empty relation table detonates that
        /// whole path. This save has one ("The Breigua Treaty" reports a null relation with every
        /// faction including the player), which is why the debug menu found it first.
        ///
        /// <c>Faction.SetRelation</c> avoids all of it: it rebuilds the entry on both sides rather
        /// than reading it, so it works on a faction whose table is empty and never touches the
        /// goodwill situation cache. Both sides get their goodwill set explicitly, because
        /// `SetRelation` copies only `kind` to the mirror — leaving the mirror at the default +100,
        /// which `CheckKindThresholds` would quietly flip back to neutral within a thousand ticks.
        /// </summary>
        private static void DeclareWar(
            IntercolonyWorldComponent state, Faction faction, EmploymentContract target)
        {
            try
            {
                if (!faction.CanChangeGoodwillFor(Faction.OfPlayer, -200))
                {
                    Report($"{faction.Name} cannot have its goodwill changed " +
                           "(permanent enemy, defeated, no goodwill, or locked by a quest). " +
                           "Hire from a different faction.");
                    return;
                }

                const int Hostile = -100;

                faction.SetRelation(new FactionRelation(Faction.OfPlayer, FactionRelationKind.Hostile)
                {
                    baseGoodwill = Hostile
                });

                FactionRelation mirror = Faction.OfPlayer.RelationWith(faction, allowNull: true);
                if (mirror != null)
                {
                    mirror.kind = FactionRelationKind.Hostile;
                    mirror.baseGoodwill = Hostile;
                }

                // Immediately, rather than waiting up to an hour for the beat, so the outcome is
                // observable while the player is still looking at it.
                HostilityPolicy.Sweep(state);

                Report($"{faction.Name} is now at war (goodwill {faction.PlayerGoodwill}, " +
                       $"hostile {faction.HostileTo(Faction.OfPlayer)}). " +
                       $"{target.workerName}: {target.status} — {target.StatusLine()}");
            }
            catch (System.Exception ex)
            {
                // This runs inside Dialog_MessageBox's paint callback, where an escaping exception
                // becomes "Exception filling window" and repeats every frame until the dialog is
                // closed. Catching it keeps a dev tool's failure to one line in the log.
                Report($"Could not declare war on {faction?.Name}: {ex.Message}");
                IntercolonyLog.Warning($"Force-war debug action threw: {ex}");
            }
        }

        [DebugAction(Category, "Dump employer standing", allowedGameStates = AllowedGameStates.Playing, displayPriority = 86)]
        private static void DumpEmployerStanding()
        {
            WithState(state =>
            {
                EmployerReputation rep = state.EmployerStanding;
                float score = rep.Score;
                IntercolonyLog.Message(
                    rep.Summary() + "\n" +
                    $"  wage factor        : x{EmployerReputationService.WageFactor(score):0.00}\n" +
                    $"  availability factor: x{EmployerReputationService.AvailabilityFactor(score):0.00}\n" +
                    $"  quality bias       : {EmployerReputationService.CandidateQualityBias(score)}");
            });
        }

        [DebugAction(Category, "Run payroll self-test", allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 60)]
        private static void RunPayrollSelfTest()
        {
            WithState(state => IntercolonyLog.Message(
                IntercolonyPayrollSelfTest.Run(state, Find.CurrentMap)));
        }

        [DebugAction(Category, "Force payroll now", allowedGameStates = AllowedGameStates.Playing, displayPriority = 50)]
        private static void ForcePayrollNow()
        {
            WithState(state =>
            {
                int moved = 0;
                foreach (EmploymentContract contract in state.Employments)
                {
                    if (contract.status == EmploymentStatus.Active && contract.nextPaymentTick >= 0)
                    {
                        contract.nextPaymentTick = GenTicks.TicksGame;
                        moved++;
                    }
                }

                PayrollService.Advance(state.Employments, state.LaborDebts, state);
                Report(moved > 0 ? $"Forced {moved} pay period(s)." : "No employee on a pay schedule.");
            });
        }

        [DebugAction(Category, "Dump labor debts", allowedGameStates = AllowedGameStates.Playing, displayPriority = 85)]
        private static void DumpLaborDebts()
        {
            WithState(state =>
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Labor debts ({state.LaborDebts.Count}, {state.UnsettledDebtCount} unsettled, " +
                              $"{PayrollService.TotalOwed(state)} silver owed in total)");
                foreach (LaborDebt debt in state.LaborDebts)
                {
                    sb.AppendLine($"  {debt}");
                }

                IntercolonyLog.Message(sb.ToString());
            });
        }

        [DebugAction(Category, "Run labor self-test", allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 59)]
        private static void RunLaborSelfTest()
        {
            WithState(state => IntercolonyLog.Message(
                IntercolonyLaborSelfTest.Run(state, Find.CurrentMap)));
        }

        [DebugAction(Category, "List available workers", allowedGameStates = AllowedGameStates.Playing, displayPriority = 46)]
        private static void ListAvailableWorkers()
        {
            WithState(state =>
            {
                List<LaborCandidate> pool = LaborCandidateService.Refresh(state);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Available workers ({pool.Count})");
                foreach (LaborCandidate candidate in pool)
                {
                    sb.AppendLine($"  {candidate.Name,-18} {candidate.SkillSummary(),-38} " +
                                  $"{candidate.dailyWage,4}/day  min {candidate.minTermDays,2}d  " +
                                  $"{candidate.travelDays,2}d away  {candidate.settlementName} ({candidate.factionName})");
                }

                IntercolonyLog.Message(sb.ToString());
            });
        }

        /// <summary>
        /// Hires the cheapest listed worker for their minimum term. Phase 17 (§110) replaces
        /// this with a real hiring window; until then this is the only way in.
        /// </summary>
        /// <summary>
        /// Tops the colony up so a debug action can actually do the thing it says it does.
        ///
        /// Added after a play-test ran aground on it. \"Hire cheapest worker\" reported *Not enough
        /// silver in storage: 0 of 234 needed* on a fresh world, which is correct behaviour and a
        /// useless dev tool — and the obvious workaround does not work either, because
        /// <see cref="PurchaseOrderService.CountColonySilver"/> counts only silver where
        /// <c>IsInAnyStorage()</c> is true. Spawning stacks on open ground leaves the readout at
        /// zero, which reads exactly like a broken mod.
        ///
        /// <see cref="IntercolonyLaborSelfTestSupport.AddSilver"/> already solved this for the
        /// self-tests, stockpile and all, so it is reused rather than reinvented. The ledger is
        /// reset afterwards because a debug grant is a deliberate gift, not a test loan — leaving it
        /// negative would let a later self-test's RestoreLedger take the silver back out.
        /// </summary>
        private static void GrantSilver(Map map, int needed, string purpose)
        {
            if (map == null || needed <= 0)
            {
                return;
            }

            int added = IntercolonyLaborSelfTestSupport.EnsureSilver(map, needed);
            IntercolonyLaborSelfTestSupport.ResetLedger();

            if (added > 0)
            {
                Report($"Granted {added} silver for {purpose} (needed {needed}).");
            }
        }

        [DebugAction(Category, "Hire cheapest worker", allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 47)]
        private static void HireCheapestWorker()
        {
            WithState(state =>
            {
                List<LaborCandidate> pool = LaborCandidateService.Refresh(state);
                if (pool.Count == 0)
                {
                    Report("No workers available.");
                    return;
                }

                LaborCandidate candidate = pool[0];
                Map map = Find.CurrentMap;

                // Prepaid takes the whole term at once, so the grant has to cover it. Computed from
                // the same helper TryHire will use rather than guessed at, and padded a little
                // because the hire re-prices for the chosen term and can land slightly above the
                // listed rate.
                int upFront = WageStructureUtility.TotalCost(
                    WageStructure.Prepaid, candidate.dailyWage, candidate.minTermDays);
                GrantSilver(map, Mathf.CeilToInt(upFront * 1.5f), "the hire");

                EmploymentContract contract = EmploymentService.TryHire(
                    state, candidate, candidate.minTermDays, map, out string failReason,
                    WageStructure.Prepaid, CombatClause.Civilian);

                // TryHire already logs and messages on success; only the failure needs reporting.
                if (contract == null)
                {
                    Report($"Could not hire: {failReason}");
                }
            });
        }

        [DebugAction(Category, "Arrive employees now", allowedGameStates = AllowedGameStates.PlayingOnMap, displayPriority = 48)]
        private static void ArriveEmployeesNow()
        {
            WithState(state =>
            {
                int moved = 0;
                foreach (EmploymentContract contract in state.Employments)
                {
                    if (contract.status == EmploymentStatus.Travelling)
                    {
                        contract.arrivalTick = GenTicks.TicksGame;
                        moved++;
                    }
                }

                EmploymentService.Advance(state.Employments);
                Report(moved > 0 ? $"Pulled {moved} arrival(s) forward." : "No employees travelling.");
            });
        }

        [DebugAction(Category, "Expire employment now", allowedGameStates = AllowedGameStates.Playing, displayPriority = 49)]
        private static void ExpireEmploymentNow()
        {
            WithState(state =>
            {
                int moved = 0;
                foreach (EmploymentContract contract in state.Employments)
                {
                    if (contract.status == EmploymentStatus.Active)
                    {
                        contract.endTick = GenTicks.TicksGame;
                        moved++;
                    }
                }

                EmploymentService.Advance(state.Employments);
                Report(moved > 0 ? $"Expired {moved} contract(s)." : "No active employees.");
            });
        }

        [DebugAction(Category, "Dump employments", allowedGameStates = AllowedGameStates.Playing, displayPriority = 84)]
        private static void DumpEmployments()
        {
            WithState(state =>
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Employments ({state.Employments.Count}, {state.ActiveEmployeeCount} open)");
                foreach (EmploymentContract contract in state.Employments)
                {
                    sb.AppendLine($"  {contract}");
                    sb.AppendLine($"      {contract.StatusLine()}");
                    sb.AppendLine($"      skills: {contract.workerSkills}");
                    sb.AppendLine($"      pawn on map: {contract.pawn?.LabelShort ?? "none"}, " +
                                  $"faction {contract.pawn?.Faction?.Name ?? "-"}, " +
                                  $"lodger {(contract.pawn != null && contract.pawn.IsQuestLodger())}, " +
                                  $"kind {contract.pawn?.kindDef?.defName ?? "-"} " +
                                  $"(hired as {contract.originalKind?.defName ?? "-"})");
                }

                IntercolonyLog.Message(sb.ToString());
            });
        }

        [DebugAction(Category, "Advance refresh", allowedGameStates = AllowedGameStates.Playing, displayPriority = 95)]
        private static void AdvanceRefresh()
        {
            WithState(state =>
            {
                state.ForceRefreshNow();
                Report($"Refresh #{state.RefreshCount}: {state.ActiveOpportunityCount} opportunities available.");
            });
        }

        [DebugAction(Category, "Expire all opportunities", allowedGameStates = AllowedGameStates.Playing)]
        private static void ExpireAllOpportunities()
        {
            WithState(state => Report($"Expired {state.ExpireAllOpportunitiesNow()} opportunit(ies)."));
        }

        [DebugAction(Category, "Clear opportunities", allowedGameStates = AllowedGameStates.Playing)]
        private static void ClearOpportunities()
        {
            WithState(state => state.ClearOpportunities());
        }

        [DebugAction(Category, "Allocate ID", allowedGameStates = AllowedGameStates.Playing)]
        private static void AllocateId()
        {
            WithState(state => Report($"Allocated ID {state.NextId()}"));
        }

        /// <summary>
        /// Runs <paramref name="action"/> against the live state owner, or warns if there
        /// isn't one. Debug actions are reachable in states where no world exists.
        /// </summary>
        private static void WithState(Action<IntercolonyWorldComponent> action)
        {
            IntercolonyWorldComponent state = IntercolonyWorldComponent.Current;
            if (state == null)
            {
                IntercolonyLog.Warning("No world loaded; state owner unavailable.");
                return;
            }

            action(state);
        }

        private static void Report(string text)
        {
            IntercolonyLog.Message(text);
            Messages.Message("[Intercolony] " + text, MessageTypeDefOf.SilentInput, historical: false);
        }
    }
}
