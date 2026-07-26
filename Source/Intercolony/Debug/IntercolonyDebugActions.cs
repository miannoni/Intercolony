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

                    int needed = order.RemainingQuantity;
                    IntVec3 cell = DropCellFinder.TradeDropSpot(map);
                    while (needed > 0)
                    {
                        int stack = Mathf.Min(needed, order.ThingDef.stackLimit);
                        Thing thing = ThingMaker.MakeThing(order.ThingDef);
                        thing.stackCount = stack;
                        GenPlace.TryPlaceThing(thing, cell, map, ThingPlaceMode.Near);
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
