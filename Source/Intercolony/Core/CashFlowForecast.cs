using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// A five-day estimate of committed cash movement for the Business tab.
    ///
    /// This report counts obligations the colony has already accepted: open sales orders, the
    /// future cycles of active sales and procurement agreements, and scheduled payroll. It does
    /// not count spot sales, unaccepted opportunities, or anything speculative. Every estimate
    /// comes from the payment rule that will actually move the silver when the obligation resolves.
    /// </summary>
    public class CashFlowDay
    {
        public int dayIndex;
        public int startTick;
        public int endTick;
        public int revenue;
        public int expenses;

        public int Net => revenue - expenses;
    }

    public class CashFlowReport
    {
        public List<CashFlowDay> days;
        public int computedTick;

        public int TotalRevenue { get; }
        public int TotalExpenses { get; }
        public int TotalNet { get; }

        internal CashFlowReport(List<CashFlowDay> days, int computedTick)
        {
            this.days = days;
            this.computedTick = computedTick;

            int revenue = 0;
            int expenses = 0;
            int net = 0;
            foreach (CashFlowDay day in days)
            {
                revenue += day.revenue;
                expenses += day.expenses;
                net += day.Net;
            }

            TotalRevenue = revenue;
            TotalExpenses = expenses;
            TotalNet = net;
        }
    }

    public static class CashFlowForecast
    {
        public const int WindowDays = 5;

        private static CashFlowReport cachedReport;
        private static IntercolonyWorldComponent cachedOwner;

        /// <summary>
        /// Computes the rolling report directly from the world's current obligations. This method
        /// never reads or writes the cache, so callers that need a fresh answer can use it without
        /// depending on the UI refresh interval.
        /// </summary>
        public static CashFlowReport Compute(IntercolonyWorldComponent state)
        {
            int now = GenTicks.TicksGame;
            List<CashFlowDay> days = CreateDays(now);

            if (state != null)
            {
                AddOpenSalesOrders(state, days, now);
                AddUnraisedSalesAgreementCycles(state, days, now);
                AddProcurementCycles(state, days, now);
                AddPayroll(state, days, now);
            }

            return new CashFlowReport(days, now);
        }

        /// <summary>
        /// Returns the report for this world, refreshing it at most one in-game hour old. The owner
        /// check is deliberate: static cache state can outlive a RimWorld game, and a report from a
        /// previous world must never be shown for the new one.
        /// </summary>
        public static CashFlowReport Current(IntercolonyWorldComponent state)
        {
            int now = GenTicks.TicksGame;
            bool stale = cachedReport == null ||
                         !ReferenceEquals(cachedOwner, state) ||
                         (long)now - cachedReport.computedTick >= GenDate.TicksPerHour;
            if (stale)
            {
                cachedReport = Compute(state);
                cachedOwner = state;
            }

            return cachedReport;
        }

        /// <summary>Discards the cached report after an obligation changes outside the time window.</summary>
        public static void Invalidate()
        {
            cachedReport = null;
            cachedOwner = null;
        }

        private static List<CashFlowDay> CreateDays(int now)
        {
            List<CashFlowDay> days = new List<CashFlowDay>(WindowDays);
            for (int i = 0; i < WindowDays; i++)
            {
                int start = now + i * GenDate.TicksPerDay;
                days.Add(new CashFlowDay
                {
                    dayIndex = i,
                    startTick = start,
                    endTick = start + GenDate.TicksPerDay
                });
            }

            return days;
        }

        private static void AddOpenSalesOrders(
            IntercolonyWorldComponent state, List<CashFlowDay> days, int now)
        {
            if (state.Orders == null)
            {
                return;
            }

            foreach (SalesOrder order in state.Orders)
            {
                if (order == null || !order.IsOpen)
                {
                    continue;
                }

                // SalesOrderService credits the player's silver from DiscountedTotalPayment at
                // the completion boundary. The forecast uses that same property rather than the
                // undiscounted total or the partial-delivery helper.
                AddRevenue(days, now, order.deadlineTick, order.DiscountedTotalPayment);
            }
        }

        private static void AddUnraisedSalesAgreementCycles(
            IntercolonyWorldComponent state, List<CashFlowDay> days, int now)
        {
            if (state.Contracts == null)
            {
                return;
            }

            long windowEnd = WindowEnd(now);
            foreach (RecurringContract contract in state.Contracts)
            {
                if (contract == null || !contract.IsActive || contract.cadenceTicks <= 0)
                {
                    continue;
                }

                int cyclesRemaining = contract.CyclesRemaining;
                // CyclesRemaining includes a cycle whose order is already in flight. The open
                // order was booked above, while nextCycleTick has moved on to the next raise, so
                // that one raised cycle must not become a second future booking here.
                if (contract.activeOrderId != 0)
                {
                    cyclesRemaining--;
                }

                if (cyclesRemaining <= 0)
                {
                    continue;
                }

                long raiseTick = contract.nextCycleTick;
                for (int cycle = 0; cycle < cyclesRemaining; cycle++)
                {
                    long paymentTick = raiseTick + contract.cadenceTicks;
                    if (paymentTick >= windowEnd)
                    {
                        break;
                    }

                    // Raising an order commits the work, but no silver moves on the raising day.
                    // Its order is paid at the deadline one cadence later, so this apparent
                    // one-cadence offset is intentional rather than an off-by-one error.
                    AddRevenue(days, now, paymentTick, contract.DiscountedCyclePayment);
                    raiseTick += contract.cadenceTicks;
                }
            }
        }

        private static void AddProcurementCycles(
            IntercolonyWorldComponent state, List<CashFlowDay> days, int now)
        {
            if (state.ProcurementContracts == null)
            {
                return;
            }

            long windowEnd = WindowEnd(now);
            foreach (ProcurementContract contract in state.ProcurementContracts)
            {
                // This is the same active-only gate used by ProcurementContractService.AdvanceCycles.
                // Suspended agreements raise no cycle, so they cannot create a forecast expense.
                if (contract == null || contract.status != ProcurementContractStatus.Active ||
                    contract.cadenceDays <= 0)
                {
                    continue;
                }

                int cyclesRemaining = contract.totalCycles -
                                      contract.cyclesCompleted - contract.cyclesFailed;
                // The paid purchase order for an in-flight cycle has already taken its silver and
                // is deliberately excluded below, so do not forecast that same cycle again from
                // the contract's next scheduled tick.
                if (contract.activeOrderId != ProcurementContract.NoActiveOrderId)
                {
                    cyclesRemaining--;
                }

                if (cyclesRemaining <= 0)
                {
                    continue;
                }

                long interval = (long)contract.cadenceDays * GenDate.TicksPerDay;
                int paymentPerCycle = contract.paymentPerCycle;
                long cycleTick = contract.nextCycleTick;
                for (int cycle = 0; cycle < cyclesRemaining; cycle++)
                {
                    if (cycleTick >= windowEnd)
                    {
                        break;
                    }

                    // The active contract stores the accepted unit price and quantity, so this
                    // uses the same shared payment calculation PurchaseOrderService uses when it
                    // creates the paid order at this tick.
                    AddExpense(days, now, cycleTick, paymentPerCycle);
                    cycleTick += interval;
                }
            }
        }

        private static void AddPayroll(
            IntercolonyWorldComponent state, List<CashFlowDay> days, int now)
        {
            if (state.Employments == null)
            {
                return;
            }

            long windowEnd = WindowEnd(now);
            foreach (EmploymentContract contract in state.Employments)
            {
                if (contract == null || contract.status != EmploymentStatus.Active ||
                    !contract.wageStructure.IsPeriodic() || contract.nextPaymentTick < 0)
                {
                    continue;
                }

                int intervalDays = contract.wageStructure.IntervalDays();
                long interval = (long)intervalDays * GenDate.TicksPerDay;
                if (interval <= 0)
                {
                    continue;
                }

                long payday = contract.nextPaymentTick;
                int arrears = contract.arrearsSilver;
                while (payday < windowEnd)
                {
                    // A payday on the end tick is still due; only a later payday falls outside
                    // the signed term. Open-ended contracts use endTick < 0 as a sentinel and
                    // therefore never enter this comparison as a quantity.
                    if (contract.endTick >= 0 && payday > contract.endTick)
                    {
                        break;
                    }

                    int bucket = BucketForTick(now, payday);
                    if (bucket >= 0)
                    {
                        // Payroll is a payday obligation, not a daily average. Arrears belong to
                        // the first projected payday only because that payday settles them; later
                        // projected paydays must pass zero or the debt would be charged twice.
                        days[bucket].expenses += PayrollService.PeriodDue(
                            contract, (int)payday, arrears);
                    }

                    arrears = 0;
                    payday += interval;
                }
            }
        }

        // Purchase orders contribute nothing here. PurchaseOrderService takes the whole price
        // with TryTakeSilver at creation, so an open purchase order's silver has already left and
        // booking it again would double-count money the player has already spent.

        private static long WindowEnd(int now)
        {
            return (long)now + (long)WindowDays * GenDate.TicksPerDay;
        }

        private static int BucketForTick(int now, long tick)
        {
            long offset = tick - now;
            long windowTicks = (long)WindowDays * GenDate.TicksPerDay;
            if (offset < 0 || offset >= windowTicks)
            {
                return -1;
            }

            return (int)(offset / GenDate.TicksPerDay);
        }

        private static void AddRevenue(
            List<CashFlowDay> days, int now, long tick, int amount)
        {
            int bucket = BucketForTick(now, tick);
            if (bucket >= 0)
            {
                days[bucket].revenue += amount;
            }
        }

        private static void AddExpense(
            List<CashFlowDay> days, int now, long tick, int amount)
        {
            int bucket = BucketForTick(now, tick);
            if (bucket >= 0)
            {
                days[bucket].expenses += amount;
            }
        }
    }
}
