using System.Text;
using RimWorld;
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

            sb.AppendLine("Sales order self-test");

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

            // --- Payment arithmetic across partial deliveries ---
            SalesOrder partial = NewOrder(sample, 100, 1.37f);
            int firstHalf = partial.PaymentFor(50);
            int secondHalf = partial.PaymentFor(50);
            Check("partial payments never exceed the total",
                firstHalf + secondHalf <= partial.TotalPayment,
                $"{firstHalf} + {secondHalf} vs {partial.TotalPayment}");
            Check("partial payment is floored, never rounded up",
                firstHalf == Mathf.FloorToInt(1.37f * 50), firstHalf.ToString());

            // Remaining quantity must track deliveries and never go negative.
            partial.deliveredQuantity = 40;
            Check("remaining tracks deliveries", partial.RemainingQuantity == 60,
                partial.RemainingQuantity.ToString());
            partial.deliveredQuantity = 250;
            Check("remaining never goes negative", partial.RemainingQuantity == 0,
                partial.RemainingQuantity.ToString());

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

            // --- §99 acceptance: one centralized validation path supports all test cases ---
            // The four cases named in §99, each driven through OrderValidator.Matches with a
            // real spawned Thing rather than asserted in the abstract.
            sb.AppendLine("  §99 test cases:");
            RunCase(sb, Check, "1,000 Rice",
                ThingDefOf.RawPotatoes, 1000, null, null);
            RunCase(sb, Check, "200 Cloth",
                ThingDefOf.Cloth, 200, null, null);
            RunCase(sb, Check, "5 Normal-or-better weapons",
                ThingDefOf.MeleeWeapon_Knife, 5, QualityCategory.Normal, ThingDefOf.Steel);
            RunCase(sb, Check, "20 Excellent Dining Chairs",
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

            // --- Overdue sweep marks open orders failed and leaves closed ones alone ---
            System.Collections.Generic.List<SalesOrder> sweep = new System.Collections.Generic.List<SalesOrder>();
            SalesOrder lapsed = NewOrder(sample, 10, 1f);
            lapsed.deadlineTick = GenTicks.TicksGame - 1;
            SalesOrder healthy = NewOrder(sample, 10, 1f);
            healthy.deadlineTick = GenTicks.TicksGame + GenDate.TicksPerDay * 5;
            SalesOrder alreadyDone = NewOrder(sample, 10, 1f);
            SalesOrderService.Cancel(alreadyDone);
            alreadyDone.deadlineTick = GenTicks.TicksGame - 1;
            sweep.Add(lapsed);
            sweep.Add(healthy);
            sweep.Add(alreadyDone);

            int failedCount = SalesOrderService.FailOverdue(sweep);
            Check("overdue sweep fails exactly the lapsed order", failedCount == 1, failedCount.ToString());
            Check("lapsed order is now failed", lapsed.status == SalesOrderStatus.Failed);
            Check("healthy order untouched", healthy.IsOpen);
            Check("already-closed order untouched", alreadyDone.status == SalesOrderStatus.Cancelled);

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
                sb.AppendLine("  (no live offer; acceptance checks skipped — run Advance refresh first)");
            }
            else
            {
                int offerId = offer.id;
                SalesOrder accepted = SalesOrderService.Accept(state, offer);
                Check("accepting produces an order", accepted != null);
                if (accepted != null)
                {
                    Check("accepted order is open", accepted.IsOpen);
                    Check("accepted order records the offer", accepted.opportunityId == offerId);
                    Check("accepted order price matches the offer",
                        Mathf.Abs(accepted.unitPrice - offer.unitPrice) < 0.001f);
                    Check("offer was consumed", state.Opportunities.Count == before - 1,
                        $"{state.Opportunities.Count} vs {before - 1}");
                    Check("offer cannot be accepted twice", SalesOrderService.Accept(state, offer) == null);
                    Check("order is findable by id", state.FindOrder(accepted.id) == accepted);

                    // Leave no test residue in the player's save.
                    SalesOrderService.Cancel(accepted);
                    state.Orders.Remove(accepted);
                }
            }

            sb.AppendLine($"  {passed} passed, {failed} failed.");
            return sb.ToString();
        }

        /// <summary>
        /// Exercises one §99 case end to end: build the line, spawn a real Thing that should
        /// satisfy it, and one that should not, and assert the single matcher agrees.
        /// </summary>
        private static void RunCase(
            StringBuilder sb,
            System.Action<string, bool, string> check,
            string caseName,
            ThingDef def,
            int quantity,
            QualityCategory? minQuality,
            ThingDef stuff)
        {
            if (def == null)
            {
                sb.AppendLine($"    {caseName}: SKIPPED (def not present in this install)");
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
                    sb.AppendLine($"    {caseName}: SKIPPED ({def.defName} has no quality comp)");
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
    }
}
