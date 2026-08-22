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
    /// Assertions over sales order logic (DESIGN.md §83.2).
    ///
    /// Covers the parts that are painful to exercise by playing: the state machine's refusal
    /// of illegal transitions (§73), payment arithmetic across partial deliveries, and the
    /// structured validation contract (§18, §74). The caravan hand-over itself is not
    /// simulated here — it needs a real caravan, and §98 requires it be played for real.
    /// </summary>
    public static class IntercolonyOrderSelfTest
    {
        public static string Run(IntercolonyWorldComponent state, Map map)
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

            sb.AppendLine("Sales order self-test");

            // --- Find Buyer UI cache decisions (A5) ---
            float refreshInterval = MainTabWindow_Intercolony.FindBuyerRefreshIntervalSeconds;
            Check("Find Buyer refresh is not due immediately",
                !MainTabWindow_Intercolony.FindBuyerRefreshDue(10f, 10f));
            Check("Find Buyer refresh stays throttled before the interval",
                !MainTabWindow_Intercolony.FindBuyerRefreshDue(
                    10f, 10f + refreshInterval - 0.001f));
            Check("Find Buyer refresh is due at the interval boundary",
                MainTabWindow_Intercolony.FindBuyerRefreshDue(10f, 10f + refreshInterval));

            ThingDef sample = ThingDefOf.Silver;

            // --- State machine (§73): every terminal state is terminal ---
            SalesOrder order = NewOrder(sample, 100, 2.5f);
            Check("new order is open", order.IsOpen);
            Check("new order owes everything", order.RemainingQuantity == 100);
            Check("total payment is quantity x price", order.TotalPayment == 250, order.TotalPayment.ToString());

            Check("cancel succeeds on an open order", SalesOrderService.Cancel(order));
            Check("cancelled order is closed", !order.IsOpen);
            Check("second cancel is refused", !SalesOrderService.Cancel(order));
            Check("cancelled order cannot then fail", !SalesOrderService.Fail(order, "test"));

            SalesOrder failing = NewOrder(sample, 50, 1f);
            Check("fail succeeds on an open order", SalesOrderService.Fail(failing, "test"));
            Check("failed order is closed", !failing.IsOpen);
            Check("second fail is refused", !SalesOrderService.Fail(failing, "test"));
            Check("failed order cannot then cancel", !SalesOrderService.Cancel(failing));

            // --- Deadlines (§17) ---
            SalesOrder overdue = NewOrder(sample, 10, 1f);
            overdue.deadlineTick = GenTicks.TicksGame - 1;
            Check("past deadline reports overdue", overdue.IsOverdue(GenTicks.TicksGame));

            SalesOrder future = NewOrder(sample, 10, 1f);
            future.deadlineTick = GenTicks.TicksGame + GenDate.TicksPerDay;
            Check("future deadline is not overdue", !future.IsOverdue(GenTicks.TicksGame));
            Check("days remaining is about one", Mathf.Abs(future.DaysRemaining - 1f) < 0.05f,
                future.DaysRemaining.ToString("F3"));

            // --- Buyer-pickup estimate and sentinel display (B1/B3) ---
            int nearPickupDays = SalesOrderService.EstimateBuyerPickupTravelDays(7f);
            int distantPickupDays = SalesOrderService.EstimateBuyerPickupTravelDays(140f);
            Check("a near buyer has a short pickup estimate",
                nearPickupDays <= 2, $"{nearPickupDays} days");
            Check("a distant buyer has a longer pickup estimate",
                distantPickupDays > nearPickupDays,
                $"near {nearPickupDays} days vs distant {distantPickupDays} days");

            int unknownPickupDays = SalesOrderService.EstimateBuyerPickupTravelDays(-1f);
            string unknownPickupCell = MainTabWindow_Intercolony.BuyerPickupTimingLabel(-1f);
            Check("the Market formats the shared unknown-distance fallback",
                unknownPickupCell == $"~{unknownPickupDays}d pickup",
                $"\"{unknownPickupCell}\" vs {unknownPickupDays} days");

            SalesOrder unsetArrival = NewOrder(sample, 10, 1f);
            unsetArrival.fulfillment = FulfillmentMode.BuyerPickup;
            unsetArrival.status = SalesOrderStatus.AwaitingCollection;
            unsetArrival.buyerArrivalTick = -1;
            string unsetArrivalDetail = MainTabWindow_Intercolony.OrderStatusEtaText(unsetArrival);
            Check("an unset buyer-arrival sentinel is never formatted as a duration",
                !unsetArrivalDetail.Contains("En route") &&
                !unsetArrivalDetail.Contains("-1") &&
                !unsetArrivalDetail.Contains("d left"),
                unsetArrivalDetail);

            SalesOrder openDeadline = NewOrder(sample, 10, 1f);
            openDeadline.status = SalesOrderStatus.Accepted;
            openDeadline.deadlineTick = GenTicks.TicksGame + GenDate.TicksPerDay;
            string openStatus = MainTabWindow_Intercolony.OrderStatusEtaText(openDeadline);
            SalesOrder closedDeadline = NewOrder(sample, 10, 1f);
            closedDeadline.status = SalesOrderStatus.Failed;
            closedDeadline.deadlineTick = -1;
            closedDeadline.buyerArrivalTick = -1;
            string closedStatus = MainTabWindow_Intercolony.OrderStatusEtaText(closedDeadline);
            Check("open and closed status cells never format a sentinel as a negative duration",
                !openStatus.Contains("-") && !closedStatus.Contains("-") &&
                !openStatus.Contains("-1") && !closedStatus.Contains("-1"),
                $"open \"{openStatus}\", closed \"{closedStatus}\"");

            // --- Payment arithmetic across partial deliveries ---
            SalesOrder partial = NewOrder(sample, 100, 1.37f);
            int firstHalf = partial.PaymentFor(50);
            int secondHalf = partial.PaymentFor(50);
            Check("partial payments never exceed the total",
                firstHalf + secondHalf <= partial.TotalPayment,
                $"{firstHalf} + {secondHalf} vs {partial.TotalPayment}");
            Check("partial payment is floored, never rounded up",
                firstHalf == Mathf.FloorToInt(1.37f * 50), firstHalf.ToString());

            // Regression: an order advertised at 537 silver paid out 536, because the quoted
            // total rounds while instalments floor. A completing delivery must settle the
            // exact advertised total across a range of awkward prices.
            int mismatches = 0;
            string firstMismatch = null;
            for (int q = 1; q <= 40; q++)
            {
                for (int cents = 1; cents < 100; cents += 7)
                {
                    SalesOrder probe = NewOrder(sample, q, q + cents / 100f);
                    int instalments = 0;
                    int deliveredSoFar = 0;

                    // Deliver in thirds, then finish, mimicking real partial hand-overs.
                    int chunk = Mathf.Max(1, q / 3);
                    while (deliveredSoFar < q)
                    {
                        int take = Mathf.Min(chunk, q - deliveredSoFar);
                        deliveredSoFar += take;
                        probe.deliveredQuantity = deliveredSoFar;
                        instalments += probe.RemainingQuantity <= 0
                            ? probe.TotalPayment - instalments
                            : probe.PaymentFor(take);
                    }

                    if (instalments != probe.TotalPayment)
                    {
                        mismatches++;
                        firstMismatch = firstMismatch ??
                            $"q={q} price={q + cents / 100f:F2}: paid {instalments} vs total {probe.TotalPayment}";
                    }
                }
            }

            Check("instalments always settle the advertised total", mismatches == 0,
                $"{mismatches} mismatch(es), first: {firstMismatch}");

            // Remaining quantity must track deliveries and never go negative.
            partial.deliveredQuantity = 40;
            Check("remaining tracks deliveries", partial.RemainingQuantity == 60,
                partial.RemainingQuantity.ToString());
            partial.deliveredQuantity = 250;
            Check("remaining never goes negative", partial.RemainingQuantity == 0,
                partial.RemainingQuantity.ToString());

            // --- B4: opt-in buy-only items remain deliverable after the option is disabled ---
            RunBuyOnlyTradeUnlockChecks(state, map, sb, Check);

            // --- Find Buyer availability: physical stock minus today's commitments ---
            RunAvailabilityChecks(state, map, sb, Check);

            // --- Buyer-pickup orders stay bound to the colony that declared them ready ---
            RunBuyerPickupMapChecks(state, map, sb, Check, Skip);

            // --- Validation contract (§18, §74) ---
            OrderValidationResult nullOrder = OrderValidator.ValidateCaravan(null, null);
            Check("null order fails validation", !nullOrder.Success);
            Check("null order explains itself", nullOrder.failures.Count > 0);

            SalesOrder closed = NewOrder(sample, 10, 1f);
            SalesOrderService.Cancel(closed);
            OrderValidationResult closedResult = OrderValidator.ValidateCaravan(closed, null);
            Check("closed order fails validation", !closedResult.Success);

            SalesOrder open = NewOrder(sample, 10, 1f);
            OrderValidationResult noCaravan = OrderValidator.ValidateCaravan(open, null);
            Check("no caravan means nothing matched", noCaravan.matchedQuantity == 0);
            Check("no caravan reports the full shortfall", noCaravan.missingQuantity == 10,
                noCaravan.missingQuantity.ToString());
            Check("failure summary is non-empty", !string.IsNullOrEmpty(noCaravan.Summary()));
            RunMixedAnimalColonyValidationCheck(map, Check, Skip);

            // --- §99 acceptance: one centralized validation path supports all test cases ---
            // The four cases named in §99, each driven through OrderValidator.Matches with a
            // real spawned Thing rather than asserted in the abstract.
            sb.AppendLine("  §99 test cases:");
            RunCase(sb, Check, Skip, "1,000 Rice",
                ThingDefOf.RawPotatoes, 1000, null, null);
            RunCase(sb, Check, Skip, "200 Cloth",
                ThingDefOf.Cloth, 200, null, null);
            RunCase(sb, Check, Skip, "5 Normal-or-better weapons",
                ThingDefOf.MeleeWeapon_Knife, 5, QualityCategory.Normal, ThingDefOf.Steel);
            RunCase(sb, Check, Skip, "20 Excellent Dining Chairs",
                ThingDefOf.DiningChair, 20, QualityCategory.Excellent, ThingDefOf.WoodLog);

            // --- Matching (§74): def identity is the whole test for unconstrained lines ---
            Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
            Thing steel = ThingMaker.MakeThing(ThingDefOf.Steel);
            SalesOrder silverOrder = NewOrder(ThingDefOf.Silver, 5, 1f);
            Check("matching def matches", OrderValidator.Matches(silverOrder.line, silver));
            Check("different def does not match", !OrderValidator.Matches(silverOrder.line, steel));
            Check("null thing does not match", !OrderValidator.Matches(silverOrder.line, null));
            silver.Destroy(DestroyMode.Vanish);
            steel.Destroy(DestroyMode.Vanish);

            // --- Overdue sweep follows each fulfilment mode's actual obligation (B2) ---
            System.Collections.Generic.List<SalesOrder> sweep = new System.Collections.Generic.List<SalesOrder>();
            SalesOrder lapsedDelivery = NewOrder(sample, 10, 1f);
            lapsedDelivery.fulfillment = FulfillmentMode.SellerDelivery;
            lapsedDelivery.deadlineTick = GenTicks.TicksGame - 1;
            SalesOrder neverReadyPickup = NewOrder(sample, 10, 1f);
            neverReadyPickup.fulfillment = FulfillmentMode.BuyerPickup;
            neverReadyPickup.deadlineTick = GenTicks.TicksGame - 1;
            SalesOrder pickupEnRoute = NewOrder(sample, 10, 1f);
            pickupEnRoute.fulfillment = FulfillmentMode.BuyerPickup;
            pickupEnRoute.status = SalesOrderStatus.AwaitingCollection;
            pickupEnRoute.buyerArrivalTick = GenTicks.TicksGame + GenDate.TicksPerDay;
            pickupEnRoute.deadlineTick = GenTicks.TicksGame - 1;
            SalesOrder healthy = NewOrder(sample, 10, 1f);
            healthy.deadlineTick = GenTicks.TicksGame + GenDate.TicksPerDay * 5;
            SalesOrder alreadyDone = NewOrder(sample, 10, 1f);
            SalesOrderService.Cancel(alreadyDone);
            alreadyDone.deadlineTick = GenTicks.TicksGame - 1;
            sweep.Add(lapsedDelivery);
            sweep.Add(neverReadyPickup);
            sweep.Add(pickupEnRoute);
            sweep.Add(healthy);
            sweep.Add(alreadyDone);

            int failedCount = SalesOrderService.FailOverdue(sweep);
            Check("overdue sweep fails exactly the two unmet obligations",
                failedCount == 2, failedCount.ToString());
            Check("seller delivery still fails after its deadline",
                lapsedDelivery.status == SalesOrderStatus.Failed);
            Check("buyer pickup never marked ready still fails after its deadline",
                neverReadyPickup.status == SalesOrderStatus.Failed);
            Check("buyer travel is spared after the readiness deadline",
                pickupEnRoute.status == SalesOrderStatus.AwaitingCollection);
            Check("healthy order untouched", healthy.IsOpen);
            Check("already-closed order untouched", alreadyDone.status == SalesOrderStatus.Cancelled);

            // --- Retention caps preserve live work and the newest removable history ---
            RunOrderHistoryRetentionChecks(state, Check);

            // --- Accepting consumes the offer, so it cannot be taken twice (§76.1) ---
            int before = state.Opportunities.Count;
            MarketOpportunity offer = null;
            foreach (MarketOpportunity o in state.Opportunities)
            {
                if (o.IsAvailable)
                {
                    offer = o;
                    break;
                }
            }

            if (offer == null)
            {
                Skip("accepting a market opportunity carries its known pickup distance",
                    "no live offer; run Advance refresh first");
            }

            else
            {
                int offerId = offer.id;
                SalesOrder accepted = SalesOrderService.Accept(state, offer);
                Check("accepting produces an order", accepted != null);
                Check("accepting a market opportunity carries its known pickup distance",
                    accepted != null &&
                    accepted.buyerPickupDistanceTiles != SalesOrder.UnknownBuyerPickupDistance &&
                    Mathf.Approximately(accepted.buyerPickupDistanceTiles, offer.distanceTiles),
                    accepted == null
                        ? "acceptance returned null"
                        : $"order={accepted.buyerPickupDistanceTiles}, offer={offer.distanceTiles}");
                if (accepted != null)
                {
                    Check("accepted order is open", accepted.IsOpen);
                    Check("accepted order records the offer", accepted.opportunityId == offerId);
                    float expectedAcceptedPrice = IntercolonyPricing.RepriceForQuantity(
                        state, offer,
                        state.GetProfile(IntercolonyMarketAccess.FindSettlement(offer.settlementId)),
                        offer.quantity,
                        out _);
                    Check("accepted order price matches the current quoted terms",
                        Mathf.Abs(accepted.unitPrice - expectedAcceptedPrice) < 0.001f);

                    float agreedUnitPrice = accepted.unitPrice;
                    int agreedPayment = accepted.TotalPayment;
                    float previousEconomyDifficulty = IntercolonyMod.Settings.economyDifficulty;
                    try
                    {
                        IntercolonyMod.Settings.economyDifficulty = previousEconomyDifficulty ==
                            IntercolonySettings.MinEconomyDifficulty
                                ? IntercolonySettings.MaxEconomyDifficulty
                                : IntercolonySettings.MinEconomyDifficulty;
                        Check("accepted price survives an economy difficulty change",
                            Mathf.Approximately(accepted.unitPrice, agreedUnitPrice) &&
                            accepted.TotalPayment == agreedPayment,
                            $"{accepted.unitPrice:F2} each, {accepted.TotalPayment} total");
                    }
                    finally
                    {
                        IntercolonyMod.Settings.economyDifficulty = previousEconomyDifficulty;
                    }

                    Check("accepted order preserves the condition floor",
                        accepted.line.minHitPointsPercent == offer.minHitPointsPercent,
                        $"{accepted.line.minHitPointsPercent} vs {offer.minHitPointsPercent}");
                    Check("offer was consumed", state.Opportunities.Count == before - 1,
                        $"{state.Opportunities.Count} vs {before - 1}");
                    Check("offer cannot be accepted twice", SalesOrderService.Accept(state, offer) == null);
                    Check("order is findable by id", state.FindOrder(accepted.id) == accepted);

                    // Leave no test residue in the player's save.
                    SalesOrderService.Cancel(accepted);
                    state.Orders.Remove(accepted);
                }
            }

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

        /// <summary>
        /// Exercises one §99 case end to end: build the line, spawn a real Thing that should
        /// satisfy it, and one that should not, and assert the single matcher agrees.
        /// </summary>
        private static void RunCase(
            StringBuilder sb,
            System.Action<string, bool, string> check,
            System.Action<string, string> skip,
            string caseName,
            ThingDef def,
            int quantity,
            QualityCategory? minQuality,
            ThingDef stuff)
        {
            if (def == null)
            {
                skip(caseName, "def not present in this install");
                return;
            }

            OrderLine line = new OrderLine(def, quantity) { minQuality = minQuality };

            ThingDef madeOf = stuff != null && def.MadeFromStuff ? stuff : null;
            Thing good = ThingMaker.MakeThing(def, madeOf);

            if (minQuality.HasValue)
            {
                CompQuality comp = good.TryGetComp<CompQuality>();
                if (comp == null)
                {
                    skip(caseName, $"{def.defName} has no quality comp");
                    good.Destroy(DestroyMode.Vanish);
                    return;
                }

                comp.SetQuality(minQuality.Value, ArtGenerationContext.Outsider);
            }

            check($"{caseName}: a conforming item matches",
                OrderValidator.Matches(line, good, out _), null);

            // A below-threshold item must be rejected with the *specific* reason, not a
            // generic shortfall — that distinction is what §18's worked example is about.
            if (minQuality.HasValue && minQuality.Value > QualityCategory.Awful)
            {
                Thing shoddy = ThingMaker.MakeThing(def, madeOf);
                CompQuality comp = shoddy.TryGetComp<CompQuality>();
                comp?.SetQuality(QualityCategory.Awful, ArtGenerationContext.Outsider);

                bool matched = OrderValidator.Matches(line, shoddy, out MatchFailure failure);
                check($"{caseName}: an Awful item is rejected", !matched, null);
                check($"{caseName}: rejection reason is quality",
                    failure == MatchFailure.BelowMinimumQuality, failure.ToString());
                shoddy.Destroy(DestroyMode.Vanish);
            }

            // A different def must never satisfy the line.
            Thing wrong = ThingMaker.MakeThing(ThingDefOf.Silver);
            check($"{caseName}: a different item is rejected",
                !OrderValidator.Matches(line, wrong, out _), null);
            wrong.Destroy(DestroyMode.Vanish);

            sb.AppendLine($"    {caseName}: {line.ShortLabel()}");
            good.Destroy(DestroyMode.Vanish);
        }

        private static void RunMixedAnimalColonyValidationCheck(
            Map map, Action<string, bool, string> check, Action<string, string> skip)
        {
            const string assertion =
                "enough matching colony animals validate alongside non-matching same-species animals";
            List<Pawn> animals = FindBuyerService.EligibleColonyAnimalCandidates(map);

            for (int i = 0; i < animals.Count; i++)
            {
                Pawn matching = animals[i];
                if (matching.gender != Gender.Female && matching.gender != Gender.Male)
                {
                    continue;
                }

                for (int j = i + 1; j < animals.Count; j++)
                {
                    Pawn rejected = animals[j];
                    if (rejected.def != matching.def || rejected.gender == matching.gender ||
                        (rejected.gender != Gender.Female && rejected.gender != Gender.Male))
                    {
                        continue;
                    }

                    SalesOrder probe = NewOrder(matching.def, 1, 0f);
                    probe.id = -917_402;
                    probe.line.animalSpec = new AnimalSpec { gender = matching.gender };

                    // Each side of the sex constraint needs a free animal so this reaches the
                    // exact matched-plus-rejected validation path rather than a reservation path.
                    SalesOrder oppositeProbe = NewOrder(rejected.def, 1, 0f);
                    oppositeProbe.id = -917_403;
                    oppositeProbe.line.animalSpec = new AnimalSpec { gender = rejected.gender };
                    if (OrderValidator.MatchingColonyAnimals(probe, map, 1).Count == 0 ||
                        OrderValidator.MatchingColonyAnimals(oppositeProbe, map, 1).Count == 0)
                    {
                        continue;
                    }

                    OrderValidationResult validation = OrderValidator.ValidateColony(probe, map);
                    check(assertion,
                        validation.Success && validation.matchedQuantity == 1 &&
                        validation.missingQuantity == 0 && validation.failures.Count == 0,
                        validation.Summary());
                    return;
                }
            }

            skip(assertion,
                "no eligible, uncommitted opposite-sex pair of one species on this map");
        }

        private static SalesOrder NewOrder(ThingDef def, int quantity, float unitPrice)
        {
            return new SalesOrder
            {
                id = 0,
                line = new OrderLine(def, quantity),
                unitPrice = unitPrice,
                acceptedTick = GenTicks.TicksGame,
                deadlineTick = GenTicks.TicksGame + GenDate.TicksPerDay * 10,
                status = SalesOrderStatus.Accepted,
                settlementName = "TestTown"
            };
        }

        private static void RunOrderHistoryRetentionChecks(
            IntercolonyWorldComponent state,
            Action<string, bool, string> check)
        {
            List<SalesOrder> savedSalesOrders = new List<SalesOrder>(state.Orders);
            List<PurchaseOrder> savedPurchaseOrders =
                new List<PurchaseOrder>(state.PurchaseOrders);
            List<PurchaseRequest> savedRequests = new List<PurchaseRequest>(state.Requests);

            const int Excess = 3;
            try
            {
                // Each phase empties all three collections. This makes a mutation of the wrong
                // collection visible instead of letting Prune's summed return value hide the trap.
                ClearOrderHistoryCollections(state);
                for (int i = 0; i < OrderHistoryService.MaxClosedSalesOrders - 1; i++)
                {
                    state.Orders.Add(ClosedSalesHistoryFixture(i));
                }
                List<SalesOrder> underCapSales = new List<SalesOrder>(state.Orders);
                int underCapSalesRemoved = OrderHistoryService.Prune(state);
                check("sales history below its cap is untouched",
                    underCapSalesRemoved == 0 && SameReferences(state.Orders, underCapSales),
                    $"removed {underCapSalesRemoved}, remaining {state.Orders.Count}");

                ClearOrderHistoryCollections(state);
                SalesOrder oldestSale = null;
                SalesOrder newestSale = null;
                for (int i = 0; i < OrderHistoryService.MaxClosedSalesOrders + Excess; i++)
                {
                    SalesOrder fixture = ClosedSalesHistoryFixture(i);
                    oldestSale = oldestSale ?? fixture;
                    newestSale = fixture;
                    state.Orders.Add(fixture);
                }
                OrderHistoryService.Prune(state);
                check("sales history removes exactly the excess",
                    state.Orders.Count == OrderHistoryService.MaxClosedSalesOrders,
                    $"remaining {state.Orders.Count}");
                check("sales history retains the newest records",
                    !state.Orders.Contains(oldestSale) && state.Orders.Contains(newestSale),
                    $"oldest={state.Orders.Contains(oldestSale)}, " +
                    $"newest={state.Orders.Contains(newestSale)}");

                ClearOrderHistoryCollections(state);
                for (int i = 0; i < OrderHistoryService.MaxClosedSalesOrders + Excess; i++)
                {
                    state.Orders.Add(ClosedSalesHistoryFixture(i));
                }
                SalesOrder openSale = ClosedSalesHistoryFixture(int.MaxValue);
                openSale.status = SalesOrderStatus.Accepted;
                state.Orders.Add(openSale);
                OrderHistoryService.Prune(state);
                check("an open sales order is never pruned",
                    state.Orders.Contains(openSale),
                    $"remaining {state.Orders.Count}");

                ClearOrderHistoryCollections(state);
                for (int i = 0; i < OrderHistoryService.MaxClosedPurchaseOrders - 1; i++)
                {
                    state.PurchaseOrders.Add(ClosedPurchaseHistoryFixture(i));
                }
                List<PurchaseOrder> underCapPurchases =
                    new List<PurchaseOrder>(state.PurchaseOrders);
                int underCapPurchasesRemoved = OrderHistoryService.Prune(state);
                check("purchase-order history below its cap is untouched",
                    underCapPurchasesRemoved == 0 &&
                    SameReferences(state.PurchaseOrders, underCapPurchases),
                    $"removed {underCapPurchasesRemoved}, remaining {state.PurchaseOrders.Count}");

                ClearOrderHistoryCollections(state);
                PurchaseOrder oldestPurchase = null;
                PurchaseOrder newestPurchase = null;
                for (int i = 0; i < OrderHistoryService.MaxClosedPurchaseOrders + Excess; i++)
                {
                    PurchaseOrder fixture = ClosedPurchaseHistoryFixture(i);
                    oldestPurchase = oldestPurchase ?? fixture;
                    newestPurchase = fixture;
                    state.PurchaseOrders.Add(fixture);
                }
                OrderHistoryService.Prune(state);
                check("purchase-order history removes exactly the excess",
                    state.PurchaseOrders.Count == OrderHistoryService.MaxClosedPurchaseOrders,
                    $"remaining {state.PurchaseOrders.Count}");
                check("purchase-order history retains the newest records",
                    !state.PurchaseOrders.Contains(oldestPurchase) &&
                    state.PurchaseOrders.Contains(newestPurchase),
                    $"oldest={state.PurchaseOrders.Contains(oldestPurchase)}, " +
                    $"newest={state.PurchaseOrders.Contains(newestPurchase)}");

                ClearOrderHistoryCollections(state);
                for (int i = 0; i < OrderHistoryService.MaxClosedPurchaseOrders + Excess; i++)
                {
                    state.PurchaseOrders.Add(ClosedPurchaseHistoryFixture(i));
                }
                PurchaseOrder openPurchase = ClosedPurchaseHistoryFixture(int.MaxValue);
                openPurchase.status = PurchaseOrderStatus.Confirmed;
                state.PurchaseOrders.Add(openPurchase);
                OrderHistoryService.Prune(state);
                check("an open purchase order is never pruned",
                    state.PurchaseOrders.Contains(openPurchase),
                    $"remaining {state.PurchaseOrders.Count}");

                ClearOrderHistoryCollections(state);
                for (int i = 0; i < OrderHistoryService.MaxConcludedPurchaseRequests - 1; i++)
                {
                    state.Requests.Add(ConcludedRequestHistoryFixture(i));
                }
                List<PurchaseRequest> underCapRequests =
                    new List<PurchaseRequest>(state.Requests);
                int underCapRequestsRemoved = OrderHistoryService.Prune(state);
                check("purchase-request history below its cap is untouched",
                    underCapRequestsRemoved == 0 &&
                    SameReferences(state.Requests, underCapRequests),
                    $"removed {underCapRequestsRemoved}, remaining {state.Requests.Count}");

                ClearOrderHistoryCollections(state);
                PurchaseRequest oldestRequest = null;
                PurchaseRequest newestRequest = null;
                for (int i = 0; i < OrderHistoryService.MaxConcludedPurchaseRequests + Excess; i++)
                {
                    PurchaseRequest fixture = ConcludedRequestHistoryFixture(i);
                    oldestRequest = oldestRequest ?? fixture;
                    newestRequest = fixture;
                    state.Requests.Add(fixture);
                }
                OrderHistoryService.Prune(state);
                check("purchase-request history removes exactly the excess",
                    state.Requests.Count == OrderHistoryService.MaxConcludedPurchaseRequests,
                    $"remaining {state.Requests.Count}");
                check("purchase-request history retains the newest records",
                    !state.Requests.Contains(oldestRequest) && state.Requests.Contains(newestRequest),
                    $"oldest={state.Requests.Contains(oldestRequest)}, " +
                    $"newest={state.Requests.Contains(newestRequest)}");

                ClearOrderHistoryCollections(state);
                for (int i = 0; i < OrderHistoryService.MaxConcludedPurchaseRequests + Excess; i++)
                {
                    state.Requests.Add(ConcludedRequestHistoryFixture(i));
                }
                PurchaseRequest openRequest = ConcludedRequestHistoryFixture(int.MaxValue);
                openRequest.status = PurchaseRequestStatus.Open;
                state.Requests.Add(openRequest);
                OrderHistoryService.Prune(state);
                check("an open purchase request is never pruned",
                    state.Requests.Contains(openRequest),
                    $"remaining {state.Requests.Count}");

                ClearOrderHistoryCollections(state);
                PurchaseRequest referencedRequest = null;
                for (int i = 0; i < OrderHistoryService.MaxConcludedPurchaseRequests + Excess; i++)
                {
                    PurchaseRequest fixture = ConcludedRequestHistoryFixture(i);
                    referencedRequest = referencedRequest ?? fixture;
                    state.Requests.Add(fixture);
                }
                state.PurchaseOrders.Add(new PurchaseOrder
                {
                    id = -3_000_001,
                    requestId = referencedRequest.id,
                    status = PurchaseOrderStatus.Confirmed
                });
                OrderHistoryService.Prune(state);
                check("a purchase-order reference protects its concluded request",
                    state.Requests.Contains(referencedRequest),
                    $"request {referencedRequest.id} present=" +
                    state.Requests.Contains(referencedRequest));
            }
            finally
            {
                // Restore the player's actual records, not merely the old counts. Count-based
                // cleanup can leave synthetic fixtures in place of real save data.
                state.Orders.Clear();
                state.Orders.AddRange(savedSalesOrders);
                state.PurchaseOrders.Clear();
                state.PurchaseOrders.AddRange(savedPurchaseOrders);
                state.Requests.Clear();
                state.Requests.AddRange(savedRequests);
            }
        }

        private static void ClearOrderHistoryCollections(IntercolonyWorldComponent state)
        {
            state.Orders.Clear();
            state.PurchaseOrders.Clear();
            state.Requests.Clear();
        }

        private static bool SameReferences<T>(List<T> actual, List<T> expected)
        {
            if (actual.Count != expected.Count)
            {
                return false;
            }

            for (int i = 0; i < actual.Count; i++)
            {
                if (!ReferenceEquals(actual[i], expected[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static SalesOrder ClosedSalesHistoryFixture(int recency)
        {
            return new SalesOrder
            {
                id = -1_000_000 + recency,
                line = new OrderLine(ThingDefOf.Silver, 1),
                completedTick = recency,
                status = SalesOrderStatus.Cancelled
            };
        }

        private static PurchaseOrder ClosedPurchaseHistoryFixture(int recency)
        {
            return new PurchaseOrder
            {
                id = -2_000_000 + recency,
                orderedTick = recency,
                status = PurchaseOrderStatus.Cancelled
            };
        }

        private static PurchaseRequest ConcludedRequestHistoryFixture(int recency)
        {
            return new PurchaseRequest
            {
                id = -4_000_000 + recency,
                createdTick = recency,
                status = PurchaseRequestStatus.Cancelled
            };
        }

        private static void RunBuyOnlyTradeUnlockChecks(
            IntercolonyWorldComponent state,
            Map map,
            StringBuilder sb,
            Action<string, bool, string> check)
        {
            sb.AppendLine("  Buy-only trade unlock:");

            BuyOnlyTradeCategoryGroup group = null;
            ThingDef def = null;
            foreach (BuyOnlyTradeCategoryGroup candidateGroup in BuyOnlyTradeUnlock.Groups)
            {
                foreach (ThingDef candidate in candidateGroup.Defs)
                {
                    if (candidate.category == ThingCategory.Item && candidate.stackLimit > 0)
                    {
                        group = candidateGroup;
                        def = candidate;
                        break;
                    }
                }

                if (def != null)
                {
                    break;
                }
            }

            check("buy-only discovery found a testable item", def != null,
                "no eligible buy-only item def was discovered");
            if (def == null)
            {
                return;
            }

            HashSet<string> enabledKeys =
                IntercolonyMod.Settings.enabledBuyOnlyTradeCategoryKeys;
            HashSet<string> savedKeys = new HashSet<string>(enabledKeys);
            Tradeability baseline = def.tradeability;
            Map fulfillmentMap = Find.AnyPlayerHomeMap ?? map;
            Zone_Stockpile testZone = null;
            Thing testStock = null;
            SalesOrder createdOrder = null;
            Settlement testBuyer = null;
            bool removedExistingReputation = false;
            bool reputationIsolated = false;
            CommercialReputation existingReputation = null;
            List<LedgerEntry> existingLedger = state == null
                ? null
                : new List<LedgerEntry>(state.Ledger);
            int existingLedgerStartTick = state?.LedgerStartTick ?? LedgerService.NoHistory;
            List<Letter> existingLetters = Find.LetterStack == null
                ? new List<Letter>()
                : new List<Letter>(Find.LetterStack.LettersListForReading);
            List<IArchivable> existingArchivables = Find.Archive == null
                ? new List<IArchivable>()
                : new List<IArchivable>(Find.Archive.ArchivablesListForReading);
            Dictionary<Thing, int> existingSilver = new Dictionary<Thing, int>();
            if (fulfillmentMap != null)
            {
                foreach (Thing silver in
                         fulfillmentMap.listerThings.ThingsOfDef(ThingDefOf.Silver))
                {
                    existingSilver[silver] = silver.stackCount;
                }
            }

            try
            {
                enabledKeys.Remove(group.Key);
                BuyOnlyTradeUnlock.ApplyEnabledCategories(enabledKeys);
                baseline = def.tradeability;

                check("disabled buy-only category is not a trade candidate",
                    !IntercolonyProductClassifier.IsFungibleTradeItem(def) &&
                    !IntercolonyProductClassifier.TradableDefs.Contains(def),
                    $"{def.defName}: {def.tradeability}");

                enabledKeys.Add(group.Key);
                BuyOnlyTradeUnlock.ApplyEnabledCategories(enabledKeys);
                check("enabled buy-only category is a trade candidate",
                    IntercolonyProductClassifier.IsFungibleTradeItem(def) &&
                    IntercolonyProductClassifier.TradableDefs.Contains(def),
                    $"{def.defName}: {def.tradeability}");
                check("enabled buy-only item permits both trade directions",
                    def.tradeability == Tradeability.All &&
                    def.tradeability.PlayerCanSell() && def.tradeability.TraderCanSell(),
                    def.tradeability.ToString());

                if (state == null || fulfillmentMap == null)
                {
                    check("buy-only obligation test has a current map and state", false,
                        "run while viewing a colony map");
                }
                else
                {
                    testBuyer = FirstAccessibleSettlement();
                    check("buy-only obligation test found an accessible buyer", testBuyer != null,
                        "no eligible accessible settlement in this world");
                    if (testBuyer != null)
                    {
                        IntVec3 storageCell = IntVec3.Invalid;
                        IntVec3 root = DropCellFinder.TradeDropSpot(fulfillmentMap);
                        foreach (IntVec3 candidate in
                                 GenRadial.RadialCellsAround(root, 12f, useCenter: true))
                        {
                            if (candidate.InBounds(fulfillmentMap) &&
                                candidate.Standable(fulfillmentMap) &&
                                candidate.GetFirstItem(fulfillmentMap) == null &&
                                fulfillmentMap.zoneManager.ZoneAt(candidate) == null)
                            {
                                storageCell = candidate;
                                break;
                            }
                        }

                        check("buy-only obligation test found a temporary storage cell",
                            storageCell.IsValid,
                            "no empty unzoned cell near the trade drop spot");
                        if (storageCell.IsValid)
                        {
                            testZone = new Zone_Stockpile(
                                StorageSettingsPreset.DefaultStockpile,
                                fulfillmentMap.zoneManager);
                            fulfillmentMap.zoneManager.RegisterZone(testZone);
                            testZone.AddCell(storageCell);

                            testStock = ThingMaker.MakeThing(def);
                            testStock.stackCount = 1;
                            testStock = GenSpawn.Spawn(testStock, storageCell, fulfillmentMap);

                            BuyerOffer offer = new BuyerOffer
                            {
                                settlement = testBuyer,
                                def = def,
                                maxQuantity = 1,
                                quantity = 1,
                                unitPrice = 1f
                            };
                            createdOrder = SalesOrderService.CreateFromOffer(
                                state, fulfillmentMap, offer, 1, 12,
                                FulfillmentMode.BuyerPickup);
                            check("production path creates a buy-only order while enabled",
                                createdOrder != null, null);

                            enabledKeys.Remove(group.Key);
                            BuyOnlyTradeUnlock.ApplyEnabledCategories(enabledKeys);

                            OrderValidationResult afterDisable =
                                OrderValidator.ValidateColony(createdOrder, fulfillmentMap);
                            check("open order validates after its category is disabled",
                                createdOrder != null && afterDisable.Success,
                                afterDisable.Summary());

                            bool markedReady = createdOrder != null &&
                                SalesOrderService.MarkReadyForPickup(createdOrder, fulfillmentMap);
                            check("production path marks the existing order ready after the category is disabled",
                                markedReady && createdOrder.status == SalesOrderStatus.AwaitingCollection,
                                $"ready={markedReady}, status={createdOrder?.status.ToString() ?? "none"}");
                            if (markedReady)
                            {
                                removedExistingReputation = state.Reputations.TryGetValue(
                                    testBuyer.ID, out existingReputation);
                                state.Reputations.Remove(testBuyer.ID);
                                reputationIsolated = true;
                                createdOrder.buyerArrivalTick = GenTicks.TicksGame;
                                SalesOrderService.ProcessBuyerCollections(
                                    new List<SalesOrder> { createdOrder });
                            }

                            check("production collection completes after the category is disabled",
                                createdOrder != null &&
                                createdOrder.status == SalesOrderStatus.Completed &&
                                createdOrder.deliveredQuantity == 1,
                                $"status={createdOrder?.status.ToString() ?? "none"}, " +
                                $"delivered={createdOrder?.deliveredQuantity ?? 0}");
                        }
                    }
                }

                // Prime discovery while Buyable, then imitate a late mod changing the field before
                // Intercolony's first modification. The service must cache this third enum value.
                enabledKeys.Remove(group.Key);
                BuyOnlyTradeUnlock.ApplyEnabledCategories(enabledKeys);
                def.tradeability = Tradeability.Sellable;
                IntercolonyProductClassifier.Invalidate();
                enabledKeys.Add(group.Key);
                BuyOnlyTradeUnlock.ApplyEnabledCategories(enabledKeys);
                enabledKeys.Remove(group.Key);
                BuyOnlyTradeUnlock.ApplyEnabledCategories(enabledKeys);
                check("toggle-off restores the exact pre-modification third value",
                    def.tradeability == Tradeability.Sellable,
                    $"restored {def.tradeability}, expected {Tradeability.Sellable}");
            }
            finally
            {
                if (createdOrder != null && state != null)
                {
                    state.Orders.Remove(createdOrder);
                }

                if (state != null)
                {
                    state.Ledger.Clear();
                    if (existingLedger != null)
                    {
                        state.Ledger.AddRange(existingLedger);
                    }

                    state.LedgerStartTick = existingLedgerStartTick;
                    if (testBuyer != null && reputationIsolated)
                    {
                        state.Reputations.Remove(testBuyer.ID);
                        if (removedExistingReputation)
                        {
                            state.Reputations.Add(testBuyer.ID, existingReputation);
                        }
                    }
                }

                if (testStock != null && !testStock.Destroyed)
                {
                    testStock.Destroy(DestroyMode.Vanish);
                }

                testZone?.Delete(playSound: false);

                if (fulfillmentMap != null)
                {
                    List<Thing> currentSilver =
                        new List<Thing>(
                            fulfillmentMap.listerThings.ThingsOfDef(ThingDefOf.Silver));
                    foreach (Thing silver in currentSilver)
                    {
                        if (existingSilver.TryGetValue(silver, out int originalCount))
                        {
                            if (!silver.Destroyed)
                            {
                                silver.stackCount = originalCount;
                            }
                        }
                        else if (!silver.Destroyed)
                        {
                            silver.Destroy(DestroyMode.Vanish);
                        }
                    }
                }

                if (Find.LetterStack != null)
                {
                    List<Letter> currentLetters =
                        new List<Letter>(Find.LetterStack.LettersListForReading);
                    foreach (Letter letter in currentLetters)
                    {
                        if (!existingLetters.Contains(letter))
                        {
                            Find.LetterStack.RemoveLetter(letter);
                        }
                    }
                }

                if (Find.Archive != null)
                {
                    List<IArchivable> currentArchivables =
                        new List<IArchivable>(Find.Archive.ArchivablesListForReading);
                    foreach (IArchivable archivable in currentArchivables)
                    {
                        if (!existingArchivables.Contains(archivable) && archivable is Letter)
                        {
                            Find.Archive.Remove(archivable);
                        }
                    }
                }

                // First return the service to an unmodified state, then restore the def value and
                // the complete settings set exactly as the test found them.
                enabledKeys.Remove(group.Key);
                BuyOnlyTradeUnlock.ApplyEnabledCategories(enabledKeys);
                def.tradeability = baseline;
                IntercolonyProductClassifier.Invalidate();
                enabledKeys.Clear();
                foreach (string key in savedKeys)
                {
                    enabledKeys.Add(key);
                }

                BuyOnlyTradeUnlock.ApplyEnabledCategories(enabledKeys);
            }
        }

        private static void RunAvailabilityChecks(
            IntercolonyWorldComponent state,
            Map map,
            StringBuilder sb,
            Action<string, bool, string> check)
        {
            sb.AppendLine("  Find Buyer availability:");
            if (state == null || map == null)
            {
                check("availability test has a current map", false, "run while viewing a colony map");
                return;
            }

            Dictionary<ThingDef, int> existingStock = new Dictionary<ThingDef, int>();
            foreach (KeyValuePair<ThingDef, int> entry in FindBuyerService.ColonyStock(map))
            {
                existingStock[entry.Key] = entry.Value;
            }

            ThingDef probeDef = null;
            foreach (ThingDef candidate in IntercolonyProductClassifier.TradableDefs)
            {
                if (candidate.category != ThingCategory.Item || candidate.stackLimit < 10 ||
                    candidate.MadeFromStuff || existingStock.ContainsKey(candidate))
                {
                    continue;
                }

                bool usedByOrder = false;
                foreach (SalesOrder existing in state.Orders)
                {
                    if (existing?.ThingDef == candidate)
                    {
                        usedByOrder = true;
                        break;
                    }
                }

                if (!usedByOrder)
                {
                    probeDef = candidate;
                    break;
                }
            }

            if (probeDef == null)
            {
                check("availability test found an isolated tradeable def", false,
                    "every stackable candidate is already stocked or ordered");
                return;
            }

            Zone_Stockpile testZone = null;
            Thing testStock = null;
            List<SalesOrder> plantedOrders = new List<SalesOrder>();
            List<Letter> existingLetters = Find.LetterStack == null
                ? new List<Letter>()
                : new List<Letter>(Find.LetterStack.LettersListForReading);
            List<IArchivable> existingArchivables = Find.Archive == null
                ? new List<IArchivable>()
                : new List<IArchivable>(Find.Archive.ArchivablesListForReading);
            try
            {
                IntVec3 storageCell = IntVec3.Invalid;
                IntVec3 root = DropCellFinder.TradeDropSpot(map);
                foreach (IntVec3 candidate in GenRadial.RadialCellsAround(root, 12f, useCenter: true))
                {
                    if (candidate.InBounds(map) && candidate.Standable(map) &&
                        candidate.GetFirstItem(map) == null && map.zoneManager.ZoneAt(candidate) == null)
                    {
                        storageCell = candidate;
                        break;
                    }
                }

                if (!storageCell.IsValid)
                {
                    check("availability test found a temporary storage cell", false,
                        "no empty unzoned cell near the trade drop spot");
                    return;
                }

                testZone = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
                map.zoneManager.RegisterZone(testZone);
                testZone.AddCell(storageCell);

                Thing stack = ThingMaker.MakeThing(probeDef);
                stack.stackCount = 10;
                testStock = GenSpawn.Spawn(stack, storageCell, map);

                int BaseAvailable() => FindBuyerService.AvailableQuantity(state, map, probeDef);
                int ListedAvailable() => ListedQuantity(
                    FindBuyerService.AvailableColonyStock(state, map), probeDef);

                SalesOrder Plant(int id, int quantity)
                {
                    SalesOrder planted = NewOrder(probeDef, quantity, 1f);
                    planted.id = id;
                    plantedOrders.Add(planted);
                    state.Orders.Add(planted);
                    return planted;
                }

                void Remove(SalesOrder planted)
                {
                    state.Orders.Remove(planted);
                    plantedOrders.Remove(planted);
                }

                check("10 physical with no orders gives 10 available",
                    BaseAvailable() == 10 && ListedAvailable() == 10,
                    $"single {BaseAvailable()}, listed {ListedAvailable()}");

                ThingDef selectedDef = probeDef;
                int selectedCount = 10;
                int selectedQuantity = 8;
                List<BuyerOffer> cachedOffers = new List<BuyerOffer> { new BuyerOffer() };
                List<KeyValuePair<ThingDef, int>> reducedStock =
                    new List<KeyValuePair<ThingDef, int>>
                    {
                        new KeyValuePair<ThingDef, int>(probeDef, 3)
                    };
                MainTabWindow_Intercolony.ReconcileFindBuyerSelection(
                    reducedStock, ref selectedDef, ref selectedCount,
                    ref selectedQuantity, ref cachedOffers);
                check("Find Buyer refresh reconciles a reduced selected count",
                    selectedDef == probeDef && selectedCount == 3 && selectedQuantity == 3,
                    $"count {selectedCount}, quantity {selectedQuantity}");
                check("Find Buyer refresh invalidates offers when selected count falls",
                    cachedOffers == null, null);

                selectedQuantity = 2;
                cachedOffers = new List<BuyerOffer> { new BuyerOffer() };
                MainTabWindow_Intercolony.ReconcileFindBuyerSelection(
                    new List<KeyValuePair<ThingDef, int>>
                    {
                        new KeyValuePair<ThingDef, int>(probeDef, 6)
                    },
                    ref selectedDef, ref selectedCount, ref selectedQuantity, ref cachedOffers);
                check("Find Buyer refresh preserves a selected quantity within the new count",
                    selectedCount == 6 && selectedQuantity == 2 && cachedOffers == null,
                    $"count {selectedCount}, quantity {selectedQuantity}");

                cachedOffers = new List<BuyerOffer> { new BuyerOffer() };
                MainTabWindow_Intercolony.ReconcileFindBuyerSelection(
                    new List<KeyValuePair<ThingDef, int>>(), ref selectedDef,
                    ref selectedCount, ref selectedQuantity, ref cachedOffers);
                check("Find Buyer refresh clears a vanished selection",
                    selectedDef == null && selectedCount == 0 && selectedQuantity == 0 &&
                    cachedOffers == null,
                    $"def {selectedDef?.defName ?? "null"}, count {selectedCount}, " +
                    $"quantity {selectedQuantity}, offers {(cachedOffers == null ? "null" : "set")}");

                SalesOrder direct = Plant(91001, 8);
                check("direct Find Buyer commitment leaves 2 available",
                    direct.IsDirectFindBuyerSale && BaseAvailable() == 2 && ListedAvailable() == 2,
                    $"direct={direct.IsDirectFindBuyerSale}, available={BaseAvailable()}");
                Remove(direct);

                SalesOrder completed = Plant(91002, 8);
                completed.status = SalesOrderStatus.Completed;
                SalesOrder failed = Plant(91003, 8);
                failed.status = SalesOrderStatus.Failed;
                SalesOrder cancelled = Plant(91004, 8);
                cancelled.status = SalesOrderStatus.Cancelled;
                check("terminal direct orders consume no availability", BaseAvailable() == 10,
                    BaseAvailable().ToString());
                Remove(completed);
                Remove(failed);
                Remove(cancelled);

                SalesOrder partialCommitment = Plant(91005, 8);
                partialCommitment.deliveredQuantity = 3;
                check("partial delivery commits only the remaining quantity",
                    partialCommitment.RemainingQuantity == 5 && BaseAvailable() == 5,
                    $"remaining {partialCommitment.RemainingQuantity}, available {BaseAvailable()}");
                Remove(partialCommitment);

                SalesOrder marketDelivery = Plant(91006, 8);
                marketDelivery.opportunityId = 41006;
                marketDelivery.fulfillment = FulfillmentMode.SellerDelivery;
                check("accepted Market seller-delivery order leaves stock available",
                    !marketDelivery.IsDirectFindBuyerSale && BaseAvailable() == 10,
                    BaseAvailable().ToString());
                Remove(marketDelivery);

                SalesOrder contractCycle = Plant(91007, 8);
                contractCycle.contractId = 51007;
                check("accepted recurring-contract cycle leaves stock available",
                    !contractCycle.IsDirectFindBuyerSale && BaseAvailable() == 10,
                    BaseAvailable().ToString());
                Remove(contractCycle);

                SalesOrder marketPickup = Plant(91008, 8);
                marketPickup.opportunityId = 41008;
                marketPickup.fulfillment = FulfillmentMode.BuyerPickup;
                check("Market buyer-pickup before Mark Ready leaves stock available",
                    marketPickup.status == SalesOrderStatus.Accepted && BaseAvailable() == 10,
                    BaseAvailable().ToString());

                marketPickup.status = SalesOrderStatus.AwaitingCollection;
                check("AwaitingCollection order commits stock", BaseAvailable() == 2,
                    BaseAvailable().ToString());
                Remove(marketPickup);

                SalesOrder shortfall = Plant(91009, 12);
                bool clampedWithoutException = false;
                try
                {
                    clampedWithoutException = BaseAvailable() == 0 && ListedAvailable() == 0;
                }
                catch (Exception exception)
                {
                    sb.AppendLine($"    availability clamp threw {exception.GetType().Name}: {exception.Message}");
                }

                check("commitment above physical stock clamps to zero", clampedWithoutException, null);
                Remove(shortfall);

                SalesOrder excluded = Plant(91010, 8);
                check("excluded order does not consume its own availability",
                    FindBuyerService.AvailableQuantity(state, map, probeDef, excluded.id) == 10,
                    FindBuyerService.AvailableQuantity(state, map, probeDef, excluded.id).ToString());
                Remove(excluded);

                // --- A3: direct Find Buyer creation revalidates at the binding boundary ---
                Settlement buyer = FirstAccessibleSettlement();
                check("commitment-boundary test found an accessible buyer", buyer != null,
                    "no eligible accessible settlement in this world");
                if (buyer != null)
                {
                    SalesOrder existingCommitment = Plant(92001, 8);
                    BuyerOffer staleOffer = new BuyerOffer
                    {
                        settlement = buyer,
                        def = probeDef,
                        maxQuantity = 3,
                        quantity = 3,
                        unitPrice = 1f
                    };

                    List<SalesOrder> ordersBeforeRefusal = new List<SalesOrder>(state.Orders);
                    SalesOrderStatus statusBeforeRefusal = existingCommitment.status;
                    OrderLine lineBeforeRefusal = existingCommitment.line;
                    int deliveredBeforeRefusal = existingCommitment.deliveredQuantity;
                    int paidBeforeRefusal = existingCommitment.paidSilver;
                    string outcomeBeforeRefusal = existingCommitment.outcomeNote;

                    SalesOrder refused = SalesOrderService.CreateFromOffer(
                        state, map, staleOffer, 3, 12, FulfillmentMode.SellerDelivery);
                    check("10 physical and 8 committed refuses a direct sale for 3",
                        refused == null && state.Orders.Count == ordersBeforeRefusal.Count,
                        $"created={refused != null}, orders {state.Orders.Count} vs {ordersBeforeRefusal.Count}");

                    bool sameOrderList = state.Orders.Count == ordersBeforeRefusal.Count;
                    for (int i = 0; sameOrderList && i < ordersBeforeRefusal.Count; i++)
                    {
                        sameOrderList = ReferenceEquals(state.Orders[i], ordersBeforeRefusal[i]);
                    }

                    check("refused direct creation leaves order state completely unchanged",
                        sameOrderList && existingCommitment.status == statusBeforeRefusal &&
                        ReferenceEquals(existingCommitment.line, lineBeforeRefusal) &&
                        existingCommitment.deliveredQuantity == deliveredBeforeRefusal &&
                        existingCommitment.paidSilver == paidBeforeRefusal &&
                        existingCommitment.outcomeNote == outcomeBeforeRefusal,
                        $"same list={sameOrderList}, status={existingCommitment.status}");

                    SalesOrder exactFit = SalesOrderService.CreateFromOffer(
                        state, map, staleOffer, 2, 12, FulfillmentMode.SellerDelivery);
                    if (exactFit != null)
                    {
                        plantedOrders.Add(exactFit);
                    }

                    check("10 physical and 8 committed accepts a direct sale for 2",
                        exactFit != null && exactFit.Quantity == 2 && BaseAvailable() == 0,
                        $"created={exactFit != null}, available={BaseAvailable()}");
                    Remove(existingCommitment);
                    if (exactFit != null)
                    {
                        Remove(exactFit);
                    }
                }

                // --- A4: Mark Ready revalidates def-level commitments after matching stock ---
                SalesOrder competingDirect = Plant(92002, 8);
                SalesOrder blockedPickup = Plant(92003, 8);
                blockedPickup.opportunityId = 42003;
                blockedPickup.fulfillment = FulfillmentMode.BuyerPickup;
                bool blockedReady = SalesOrderService.MarkReadyForPickup(blockedPickup, map);
                check("10 physical and 8 committed elsewhere refuses Mark Ready for 8",
                    !blockedReady && blockedPickup.status == SalesOrderStatus.Accepted &&
                    blockedPickup.buyerArrivalTick < 0,
                    $"ready={blockedReady}, status={blockedPickup.status}, arrival={blockedPickup.buyerArrivalTick}");
                Remove(competingDirect);
                Remove(blockedPickup);

                SalesOrder smallerCommitment = Plant(92010, 4);
                SalesOrder fittingPickup = Plant(92011, 3);
                fittingPickup.opportunityId = 42011;
                fittingPickup.fulfillment = FulfillmentMode.BuyerPickup;
                OrderValidationResult fittingValidation =
                    OrderValidator.ValidateColony(fittingPickup, map);
                bool fittingReady = SalesOrderService.MarkReadyForPickup(fittingPickup, map);
                check("10 physical, 4 committed elsewhere, and 3 required permits Mark Ready",
                    fittingValidation.matchedQuantity == 3 &&
                    fittingValidation.totalPhysicalMatchingQuantity == 10 &&
                    fittingReady && fittingPickup.status == SalesOrderStatus.AwaitingCollection,
                    $"matched={fittingValidation.matchedQuantity}, " +
                    $"physical={fittingValidation.totalPhysicalMatchingQuantity}, " +
                    $"ready={fittingReady}, status={fittingPickup.status}");
                Remove(smallerCommitment);
                Remove(fittingPickup);

                // --- B1/B2: real Mark Ready transition, shared fallback, and deadline boundary ---
                SalesOrder timelyPickup = Plant(92030, 1);
                timelyPickup.opportunityId = 42030;
                timelyPickup.fulfillment = FulfillmentMode.BuyerPickup;
                timelyPickup.settlementId = int.MinValue; // Explicit unknown-distance dispatch.
                timelyPickup.deadlineTick = GenTicks.TicksGame + GenDate.TicksPerDay;
                int dispatchTick = GenTicks.TicksGame;
                int fallbackDays = SalesOrderService.EstimateBuyerPickupTravelDays(-1f);
                bool timelyReady = SalesOrderService.MarkReadyForPickup(timelyPickup, map);
                int dispatchedDays = timelyPickup.buyerArrivalTick < 0
                    ? -1
                    : (timelyPickup.buyerArrivalTick - dispatchTick) / GenDate.TicksPerDay;
                string marketTiming = MainTabWindow_Intercolony.BuyerPickupTimingLabel(-1f);
                int letterDays = dispatchedDays >= 0 ? dispatchedDays : fallbackDays;
                string dispatchLetter = SalesOrderService.BuyerPickupDispatchLetterText(
                    timelyPickup, letterDays);

                check("unknown distance uses the same fallback in Market and dispatch",
                    timelyReady && dispatchedDays == fallbackDays &&
                    marketTiming == $"~{dispatchedDays}d pickup",
                    $"ready={timelyReady}, Market=\"{marketTiming}\", dispatch={dispatchedDays}d");
                check("Market pickup estimate and dispatch letter agree",
                    marketTiming == $"~{dispatchedDays}d pickup" &&
                    dispatchLetter.Contains($"approximately {dispatchedDays} days"),
                    $"Market=\"{marketTiming}\", letter=\"{dispatchLetter}\"");

                // The transition happened while the deadline was future. Simulating the later
                // clock crossing must not make the buyer's journey fail the order.
                timelyPickup.deadlineTick = GenTicks.TicksGame - 1;
                int timelyFailed = SalesOrderService.FailOverdue(
                    new List<SalesOrder> { timelyPickup });
                check("pickup marked ready before the deadline survives buyer travel past it",
                    timelyReady && timelyFailed == 0 &&
                    timelyPickup.status == SalesOrderStatus.AwaitingCollection,
                    $"ready={timelyReady}, failed={timelyFailed}, status={timelyPickup.status}");
                Remove(timelyPickup);

                SalesOrder latePickup = Plant(92031, 1);
                latePickup.opportunityId = 42031;
                latePickup.fulfillment = FulfillmentMode.BuyerPickup;
                latePickup.settlementId = int.MinValue;
                latePickup.deadlineTick = GenTicks.TicksGame - 1;
                bool lateReady = SalesOrderService.MarkReadyForPickup(latePickup, map);
                int lateFailed = SalesOrderService.FailOverdue(
                    new List<SalesOrder> { latePickup });
                check("pickup first marked ready after the deadline cannot escape expiry",
                    !lateReady && lateFailed == 1 &&
                    latePickup.status == SalesOrderStatus.Failed &&
                    latePickup.buyerArrivalTick < 0,
                    $"ready={lateReady}, failed={lateFailed}, status={latePickup.status}, " +
                    $"arrival={latePickup.buyerArrivalTick}");
                Remove(latePickup);

                SalesOrder soleMarketPickup = Plant(92004, 8);
                soleMarketPickup.opportunityId = 42004;
                soleMarketPickup.fulfillment = FulfillmentMode.BuyerPickup;
                bool soleReady = SalesOrderService.MarkReadyForPickup(soleMarketPickup, map);
                check("a sole Market pickup marks ready and consumes 8 availability",
                    soleReady && soleMarketPickup.status == SalesOrderStatus.AwaitingCollection &&
                    BaseAvailable() == 2,
                    $"ready={soleReady}, status={soleMarketPickup.status}, available={BaseAvailable()}");
                Remove(soleMarketPickup);

                BuyerOffer pickupOffer = buyer == null
                    ? null
                    : new BuyerOffer
                    {
                        settlement = buyer,
                        def = probeDef,
                        maxQuantity = 8,
                        quantity = 8,
                        unitPrice = 1f
                    };
                SalesOrder directPickup = SalesOrderService.CreateFromOffer(
                    state, map, pickupOffer, 8, 12, FulfillmentMode.BuyerPickup);
                if (directPickup != null)
                {
                    plantedOrders.Add(directPickup);
                }

                int availabilityIncludingSelf = BaseAvailable();
                bool directReady = directPickup != null &&
                                   SalesOrderService.MarkReadyForPickup(directPickup, map);
                check("direct Find Buyer pickup does not block its own Mark Ready",
                    directPickup != null && directPickup.IsDirectFindBuyerSale &&
                    availabilityIncludingSelf == 2 &&
                    directReady && directPickup.status == SalesOrderStatus.AwaitingCollection &&
                    BaseAvailable() == 2,
                    $"before exclusion={availabilityIncludingSelf}, ready={directReady}, " +
                    $"status={directPickup?.status.ToString() ?? "not created"}, after={BaseAvailable()}");
                if (directPickup != null)
                {
                    Remove(directPickup);
                }

                SalesOrder firstMarketPickup = Plant(92006, 8);
                firstMarketPickup.opportunityId = 42006;
                firstMarketPickup.fulfillment = FulfillmentMode.BuyerPickup;
                firstMarketPickup.settlementId = int.MinValue;
                SalesOrder secondMarketPickup = Plant(92007, 8);
                secondMarketPickup.opportunityId = 42007;
                secondMarketPickup.fulfillment = FulfillmentMode.BuyerPickup;
                secondMarketPickup.settlementId = int.MinValue;

                bool firstReady = SalesOrderService.MarkReadyForPickup(firstMarketPickup, map);
                bool secondReady = SalesOrderService.MarkReadyForPickup(secondMarketPickup, map);
                check("only one of two competing Market pickups can mark ready",
                    firstReady && !secondReady &&
                    firstMarketPickup.status == SalesOrderStatus.AwaitingCollection &&
                    secondMarketPickup.status == SalesOrderStatus.Accepted && BaseAvailable() == 2,
                    $"first={firstReady}/{firstMarketPickup.status}, " +
                    $"second={secondReady}/{secondMarketPickup.status}, available={BaseAvailable()}");

                bool cancelledFirst = SalesOrderService.Cancel(firstMarketPickup);
                bool secondReadyAfterCancel = SalesOrderService.MarkReadyForPickup(secondMarketPickup, map);
                check("cancelling the first pickup frees stock for the second Mark Ready",
                    cancelledFirst && secondReadyAfterCancel &&
                    firstMarketPickup.status == SalesOrderStatus.Cancelled &&
                    secondMarketPickup.status == SalesOrderStatus.AwaitingCollection &&
                    BaseAvailable() == 2,
                    $"cancelled={cancelledFirst}, second ready={secondReadyAfterCancel}, " +
                    $"available={BaseAvailable()}");
                Remove(firstMarketPickup);
                Remove(secondMarketPickup);
            }
            finally
            {
                foreach (SalesOrder planted in plantedOrders)
                {
                    state.Orders.Remove(planted);
                }

                if (testStock != null && !testStock.Destroyed)
                {
                    testStock.Destroy(DestroyMode.Vanish);
                }

                testZone?.Delete(playSound: false);

                if (Find.LetterStack != null)
                {
                    List<Letter> currentLetters =
                        new List<Letter>(Find.LetterStack.LettersListForReading);
                    foreach (Letter letter in currentLetters)
                    {
                        if (!existingLetters.Contains(letter))
                        {
                            Find.LetterStack.RemoveLetter(letter);
                        }
                    }
                }

                if (Find.Archive != null)
                {
                    List<IArchivable> currentArchivables =
                        new List<IArchivable>(Find.Archive.ArchivablesListForReading);
                    foreach (IArchivable archivable in currentArchivables)
                    {
                        if (!existingArchivables.Contains(archivable) && archivable is Letter)
                        {
                            Find.Archive.Remove(archivable);
                        }
                    }
                }
            }
        }

        private static void RunBuyerPickupMapChecks(
            IntercolonyWorldComponent state,
            Map map,
            StringBuilder sb,
            Action<string, bool, string> check,
            Action<string, string> skip)
        {
            sb.AppendLine("  Buyer-pickup colony binding:");
            Map fallbackMap = Find.AnyPlayerHomeMap;
            if (state == null || map == null || fallbackMap == null)
            {
                check("pickup-map test has a colony and fallback map", false,
                    "run while viewing a colony map");
                return;
            }

            ThingDef probeDef = null;
            foreach (ThingDef candidate in IntercolonyProductClassifier.TradableDefs)
            {
                if (candidate.category != ThingCategory.Item || candidate.stackLimit < 3 ||
                    candidate.MadeFromStuff)
                {
                    continue;
                }

                bool alreadyStocked = false;
                foreach (Map loadedMap in Find.Maps)
                {
                    if (ListedQuantity(FindBuyerService.ColonyStock(loadedMap), candidate) > 0)
                    {
                        alreadyStocked = true;
                        break;
                    }
                }

                if (alreadyStocked)
                {
                    continue;
                }

                bool alreadyOrdered = false;
                foreach (SalesOrder existing in state.Orders)
                {
                    if (existing?.ThingDef == candidate)
                    {
                        alreadyOrdered = true;
                        break;
                    }
                }

                if (!alreadyOrdered)
                {
                    probeDef = candidate;
                    break;
                }
            }

            if (probeDef == null)
            {
                check("pickup-map test found an isolated tradeable def", false,
                    "every stackable candidate is already stocked or ordered");
                return;
            }

            List<SalesOrder> testOrders = new List<SalesOrder>();
            List<Zone_Stockpile> testZones = new List<Zone_Stockpile>();
            List<Thing> testStocks = new List<Thing>();
            List<LedgerEntry> existingLedger = new List<LedgerEntry>(state.Ledger);
            int existingLedgerStartTick = state.LedgerStartTick;
            List<Letter> existingLetters = Find.LetterStack == null
                ? new List<Letter>()
                : new List<Letter>(Find.LetterStack.LettersListForReading);
            List<IArchivable> existingArchivables = Find.Archive == null
                ? new List<IArchivable>()
                : new List<IArchivable>(Find.Archive.ArchivablesListForReading);

            bool TrySpawnStoredStock(Map targetMap, int count, out Thing stock)
            {
                stock = null;
                IntVec3 storageCell = IntVec3.Invalid;
                IntVec3 root = DropCellFinder.TradeDropSpot(targetMap);
                foreach (IntVec3 candidate in GenRadial.RadialCellsAround(root, 12f, useCenter: true))
                {
                    if (candidate.InBounds(targetMap) && candidate.Standable(targetMap) &&
                        candidate.GetFirstItem(targetMap) == null &&
                        targetMap.zoneManager.ZoneAt(candidate) == null)
                    {
                        storageCell = candidate;
                        break;
                    }
                }

                if (!storageCell.IsValid)
                {
                    return false;
                }

                Zone_Stockpile zone = new Zone_Stockpile(
                    StorageSettingsPreset.DefaultStockpile, targetMap.zoneManager);
                targetMap.zoneManager.RegisterZone(zone);
                zone.AddCell(storageCell);
                testZones.Add(zone);

                Thing stack = ThingMaker.MakeThing(probeDef);
                stack.stackCount = count;
                stock = GenSpawn.Spawn(stack, storageCell, targetMap);
                testStocks.Add(stock);
                return true;
            }

            SalesOrder PlantPickup(int id, Map initialMap = null)
            {
                SalesOrder order = NewOrder(probeDef, 1, 0f);
                order.id = id;
                order.opportunityId = -id;
                order.settlementId = int.MinValue;
                order.fulfillment = FulfillmentMode.BuyerPickup;
                order.fulfillmentMap = initialMap;
                testOrders.Add(order);
                state.Orders.Add(order);
                return order;
            }

            int StoredCount(Map targetMap) =>
                ListedQuantity(FindBuyerService.ColonyStock(targetMap), probeDef);

            try
            {
                Map absentRecordedMap = new Map();
                SalesOrder refusesAbsentMap = PlantPickup(93004, absentRecordedMap);
                bool absentMapReady = SalesOrderService.MarkReadyForPickup(refusesAbsentMap, map);
                check("Mark Ready refuses an order whose recorded colony is absent",
                    !absentMapReady &&
                    refusesAbsentMap.status == SalesOrderStatus.Accepted &&
                    ReferenceEquals(refusesAbsentMap.fulfillmentMap, absentRecordedMap),
                    $"ready={absentMapReady}, status={refusesAbsentMap.status}, " +
                    $"record unchanged={ReferenceEquals(refusesAbsentMap.fulfillmentMap, absentRecordedMap)}");
                state.Orders.Remove(refusesAbsentMap);
                testOrders.Remove(refusesAbsentMap);

                if (!TrySpawnStoredStock(map, 3, out _))
                {
                    check("pickup-map test found temporary storage on the current colony", false,
                        "no empty unzoned cell near the trade drop spot");
                    skip("Mark Ready adopts and persists the current colony when none was recorded",
                        "no empty unzoned cell near the trade drop spot");
                    return;
                }

                SalesOrder recordsReadyMap = PlantPickup(93001);
                bool startedWithoutRecordedMap = recordsReadyMap.fulfillmentMap == null;
                bool recordedReady = SalesOrderService.MarkReadyForPickup(recordsReadyMap, map);
                check("Mark Ready adopts and persists the current colony when none was recorded",
                    startedWithoutRecordedMap && recordedReady &&
                    ReferenceEquals(recordsReadyMap.fulfillmentMap, map),
                    $"started null={startedWithoutRecordedMap}, ready={recordedReady}, " +
                    $"recorded={recordsReadyMap.fulfillmentMap?.ToString() ?? "null"}");
                state.Orders.Remove(recordsReadyMap);
                testOrders.Remove(recordsReadyMap);

                if (!ReferenceEquals(fallbackMap, map) &&
                    !TrySpawnStoredStock(fallbackMap, 2, out _))
                {
                    check("pickup-map test found temporary storage on the fallback colony", false,
                        "no empty unzoned cell near the trade drop spot");
                    return;
                }

                Map distinctHomeMap = null;
                foreach (Map candidate in Find.Maps)
                {
                    if (candidate.IsPlayerHome && !ReferenceEquals(candidate, fallbackMap))
                    {
                        distinctHomeMap = candidate;
                        break;
                    }
                }

                if (distinctHomeMap == null)
                {
                    skip("recorded-map collection vs AnyPlayerHomeMap",
                        "this test world has only one player home map; " +
                        "human multi-colony test required");
                }
                else
                {
                    if (!ReferenceEquals(distinctHomeMap, map) &&
                        !TrySpawnStoredStock(distinctHomeMap, 1, out _))
                    {
                        skip("recorded-map collection vs AnyPlayerHomeMap",
                            "the second home map has no temporary storage cell; " +
                            "human multi-colony test required");
                    }
                    else
                    {
                        SalesOrder mappedCollection = PlantPickup(93002, fallbackMap);
                        bool mappedReady = SalesOrderService.MarkReadyForPickup(
                            mappedCollection, distinctHomeMap);
                        int recordedBefore = StoredCount(distinctHomeMap);
                        int fallbackBefore = StoredCount(fallbackMap);
                        mappedCollection.buyerArrivalTick = GenTicks.TicksGame;
                        SalesOrderService.ProcessBuyerCollections(
                            new List<SalesOrder> { mappedCollection });

                        check("collection uses the order's recorded colony, not AnyPlayerHomeMap",
                            mappedReady && mappedCollection.status == SalesOrderStatus.Completed &&
                            StoredCount(distinctHomeMap) == recordedBefore - 1 &&
                            StoredCount(fallbackMap) == fallbackBefore,
                            $"ready={mappedReady}, status={mappedCollection.status}, " +
                            $"recorded stock {recordedBefore}->{StoredCount(distinctHomeMap)}, " +
                            $"fallback stock {fallbackBefore}->{StoredCount(fallbackMap)}");
                    }
                }

                SalesOrder oldSaveOrder = PlantPickup(93003);
                oldSaveOrder.status = SalesOrderStatus.AwaitingCollection;
                oldSaveOrder.buyerArrivalTick = GenTicks.TicksGame;
                int fallbackStockBefore = StoredCount(fallbackMap);
                SalesOrderService.ProcessBuyerCollections(new List<SalesOrder> { oldSaveOrder });
                check("an old-save order with no recorded colony completes via the fallback",
                    oldSaveOrder.status == SalesOrderStatus.Completed &&
                    StoredCount(fallbackMap) == fallbackStockBefore - 1,
                    $"status={oldSaveOrder.status}, fallback stock " +
                    $"{fallbackStockBefore}->{StoredCount(fallbackMap)}");
            }
            finally
            {
                foreach (SalesOrder testOrder in testOrders)
                {
                    state.Orders.Remove(testOrder);
                }

                foreach (Thing testStock in testStocks)
                {
                    if (testStock != null && !testStock.Destroyed)
                    {
                        testStock.Destroy(DestroyMode.Vanish);
                    }
                }

                foreach (Zone_Stockpile testZone in testZones)
                {
                    testZone?.Delete(playSound: false);
                }

                state.Ledger.Clear();
                state.Ledger.AddRange(existingLedger);
                state.LedgerStartTick = existingLedgerStartTick;

                if (Find.LetterStack != null)
                {
                    List<Letter> currentLetters =
                        new List<Letter>(Find.LetterStack.LettersListForReading);
                    foreach (Letter letter in currentLetters)
                    {
                        if (!existingLetters.Contains(letter))
                        {
                            Find.LetterStack.RemoveLetter(letter);
                        }
                    }
                }

                if (Find.Archive != null)
                {
                    List<IArchivable> currentArchivables =
                        new List<IArchivable>(Find.Archive.ArchivablesListForReading);
                    foreach (IArchivable archivable in currentArchivables)
                    {
                        if (!existingArchivables.Contains(archivable) && archivable is Letter)
                        {
                            Find.Archive.Remove(archivable);
                        }
                    }
                }
            }
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

        private static int ListedQuantity(
            List<KeyValuePair<ThingDef, int>> stock, ThingDef def)
        {
            foreach (KeyValuePair<ThingDef, int> entry in stock)
            {
                if (entry.Key == def)
                {
                    return entry.Value;
                }
            }

            return 0;
        }
    }
}
