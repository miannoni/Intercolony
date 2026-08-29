using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Self-tests for the five-day committed cash-flow forecast.
    ///
    /// Every fixture is a world-level value object. Nothing here needs a map, a spawned Thing, or
    /// a pawn, which keeps the suite suitable for the world-only part of the debug registry.
    /// </summary>
    public static class IntercolonyCashFlowSelfTest
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

            public void Info(string line)
            {
                sb.AppendLine($"        {line}");
            }
        }

        public static string Run(IntercolonyWorldComponent state)
        {
            Results r = new Results();
            r.sb.AppendLine("Five-day cash flow forecast self-test");

            if (state == null)
            {
                r.Info("world-backed forecast fixtures skipped because no world state is available.");
                return Summarize(r);
            }

            // Contents, not counts. Each forecast input list is restored exactly so a failed
            // assertion or an exception cannot replace a player's real obligations with probes.
            List<SalesOrder> savedOrders = new List<SalesOrder>(state.Orders);
            List<RecurringContract> savedContracts = new List<RecurringContract>(state.Contracts);
            List<ProcurementContract> savedProcurementContracts =
                new List<ProcurementContract>(state.ProcurementContracts);
            List<PurchaseOrder> savedPurchaseOrders =
                new List<PurchaseOrder>(state.PurchaseOrders);
            List<EmploymentContract> savedEmployments =
                new List<EmploymentContract>(state.Employments);

            try
            {
                state.Orders.Clear();
                state.Contracts.Clear();
                state.ProcurementContracts.Clear();
                state.PurchaseOrders.Clear();
                state.Employments.Clear();
                CashFlowForecast.Invalidate();

                CheckSalesAgreementCycle(r, state);
                CheckProcurementCycle(r, state);
                CheckFixedTermPayroll(r, state);
                CheckOpenEndedPayroll(r, state);
                CheckEmptyReport(r, state);
                CheckNetArithmetic(r, state);
                CheckOpenPurchaseOrder(r, state);
                CheckInvalidation(r, state);
            }
            catch (Exception ex)
            {
                r.sb.AppendLine($"  EXCEPTION: {ex}");
                r.failed++;
            }
            finally
            {
                state.Orders.Clear();
                state.Orders.AddRange(savedOrders);
                state.Contracts.Clear();
                state.Contracts.AddRange(savedContracts);
                state.ProcurementContracts.Clear();
                state.ProcurementContracts.AddRange(savedProcurementContracts);
                state.PurchaseOrders.Clear();
                state.PurchaseOrders.AddRange(savedPurchaseOrders);
                state.Employments.Clear();
                state.Employments.AddRange(savedEmployments);
                CashFlowForecast.Invalidate();
                r.Info("cash-flow fixtures removed and forecast cache invalidated; world obligations restored.");
            }

            return Summarize(r);
        }

        private static void CheckSalesAgreementCycle(
            Results r, IntercolonyWorldComponent state)
        {
            int now = GenTicks.TicksGame;
            RecurringContract contract = new RecurringContract
            {
                id = -8101,
                thingDef = ThingDefOf.WoodLog,
                quantityPerCycle = 7,
                cadenceTicks = GenDate.TicksPerDay,
                totalCycles = 1,
                unitPrice = 123.45f,
                status = ContractStatus.Active,
                nextCycleTick = now
            };
            contract.DiscountFraction = 0.25f;

            try
            {
                state.AddContract(contract);
                CashFlowReport report = CashFlowForecast.Compute(state);
                long paymentTick = (long)contract.nextCycleTick + contract.cadenceTicks;
                int raisingDay = DayIndex(report, contract.nextCycleTick);
                int paymentDay = DayIndex(report, paymentTick);
                int raisingRevenue = RevenueAt(report, raisingDay);
                int paymentRevenue = RevenueAt(report, paymentDay);
                int expected = contract.DiscountedCyclePayment;

                // This must fail if revenue is booked on the raising day or with anything other
                // than the discounted amount the contract itself will pay.
                r.Check(
                    raisingDay >= 0 && paymentDay >= 0 && raisingDay != paymentDay &&
                    raisingRevenue == 0 && paymentRevenue == expected,
                    "a sales cycle is booked on its payment day at the contract amount",
                    $"reportTick={report?.computedTick ?? -1}, raiseTick={contract.nextCycleTick}, " +
                    $"paymentTick={paymentTick}, raiseDay={raisingDay}, paymentDay={paymentDay}, " +
                    $"raiseRevenue={raisingRevenue}, paymentRevenue={paymentRevenue}, " +
                    $"expected={expected}; {DayValues(report)}");
            }
            finally
            {
                state.Contracts.Remove(contract);
            }
        }

        private static void CheckProcurementCycle(
            Results r, IntercolonyWorldComponent state)
        {
            int now = GenTicks.TicksGame;
            ProcurementContract contract = new ProcurementContract
            {
                id = -8102,
                thingDef = ThingDefOf.WoodLog,
                quantityPerCycle = 4,
                unitPrice = 87.65f,
                cadenceDays = 1,
                totalCycles = 1,
                nextCycleTick = now + 2 * GenDate.TicksPerDay,
                status = ProcurementContractStatus.Active,
                activeOrderId = ProcurementContract.NoActiveOrderId
            };

            try
            {
                state.AddProcurementContract(contract);
                CashFlowReport report = CashFlowForecast.Compute(state);
                long lateTick = (long)contract.nextCycleTick +
                                (long)contract.cadenceDays * GenDate.TicksPerDay;
                int dueDay = DayIndex(report, contract.nextCycleTick);
                int lateDay = DayIndex(report, lateTick);
                int dueExpense = ExpenseAt(report, dueDay);
                int lateExpense = ExpenseAt(report, lateDay);
                int expected = contract.paymentPerCycle;

                // This must fail if procurement is delayed like sales revenue or if the forecast
                // charges anything other than the accepted contract payment for the cycle.
                r.Check(
                    dueDay >= 0 && dueExpense == expected &&
                    (lateDay < 0 || lateExpense == 0),
                    "a procurement cycle is expensed on its due day at paymentPerCycle",
                    $"reportTick={report?.computedTick ?? -1}, dueTick={contract.nextCycleTick}, " +
                    $"lateTick={lateTick}, dueDay={dueDay}, lateDay={lateDay}, " +
                    $"dueExpense={dueExpense}, lateExpense={lateExpense}, expected={expected}, " +
                    $"totalExpenses={TotalExpenses(report)}; {DayValues(report)}");
            }
            finally
            {
                state.ProcurementContracts.Remove(contract);
            }
        }

        private static void CheckFixedTermPayroll(
            Results r, IntercolonyWorldComponent state)
        {
            int now = GenTicks.TicksGame;
            int firstPayday = now + GenDate.TicksPerDay;
            int secondPayday = firstPayday + GenDate.TicksPerDay;
            EmploymentContract contract = new EmploymentContract
            {
                status = EmploymentStatus.Active,
                wageStructure = WageStructure.Daily,
                dailyWage = 80,
                nextPaymentTick = firstPayday,
                endTick = firstPayday + GenDate.TicksPerDay / 2,
                termDays = 3,
                arrearsSilver = 13
                // pawn is deliberately left null; this forecast only needs scalar contract fields.
            };

            try
            {
                state.AddEmployment(contract);
                CashFlowReport report = CashFlowForecast.Compute(state);
                int firstDay = DayIndex(report, firstPayday);
                int secondDay = DayIndex(report, secondPayday);
                int firstExpense = ExpenseAt(report, firstDay);
                int secondExpense = ExpenseAt(report, secondDay);
                int expectedFirst = PayrollService.PeriodDue(
                    contract, firstPayday, contract.arrearsSilver);

                // This must fail if payroll is smeared across days, ignores endTick, or projects
                // a payday after the fixed term has ended.
                r.Check(
                    firstDay >= 0 && secondDay >= 0 && firstDay != secondDay &&
                    firstExpense == expectedFirst && secondExpense == 0 &&
                    HasOnlyExpenseAt(report, firstDay, expectedFirst),
                    "a fixed-term employee is paid once and stops after endTick",
                    $"reportTick={report?.computedTick ?? -1}, firstPayday={firstPayday}, " +
                    $"secondPayday={secondPayday}, endTick={contract.endTick}, firstDay={firstDay}, " +
                    $"secondDay={secondDay}, firstExpense={firstExpense}, " +
                    $"secondExpense={secondExpense}, expectedFirst={expectedFirst}, " +
                    $"intervalDays={contract.wageStructure.IntervalDays()}; {DayValues(report)}");
            }
            finally
            {
                state.Employments.Remove(contract);
            }
        }

        private static void CheckOpenEndedPayroll(
            Results r, IntercolonyWorldComponent state)
        {
            int now = GenTicks.TicksGame;
            EmploymentContract contract = new EmploymentContract
            {
                status = EmploymentStatus.Active,
                wageStructure = WageStructure.Daily,
                dailyWage = 40,
                nextPaymentTick = now,
                endTick = -1,
                termDays = 0,
                arrearsSilver = 11
                // pawn is deliberately left null; an open-ended payroll probe must not spawn one.
            };
            int interval = contract.wageStructure.IntervalDays() * GenDate.TicksPerDay;
            int firstExpected = PayrollService.PeriodDue(
                contract, contract.nextPaymentTick, contract.arrearsSilver);
            int laterExpected = PayrollService.PeriodDue(
                contract, contract.nextPaymentTick + interval, 0);

            try
            {
                state.AddEmployment(contract);
                CashFlowReport report = CashFlowForecast.Compute(state);
                bool expectedSchedule = report != null && report.days != null &&
                                        report.days.Count == CashFlowForecast.WindowDays;
                for (int i = 0; expectedSchedule && i < CashFlowForecast.WindowDays; i++)
                {
                    long payday = (long)contract.nextPaymentTick + (long)i * interval;
                    int day = DayIndex(report, payday);
                    int expected = i == 0 ? firstExpected : laterExpected;
                    expectedSchedule = day >= 0 && RevenueAt(report, day) == 0 &&
                                       ExpenseAt(report, day) == expected;
                }

                // This must fail if endTick < 0 is mistaken for an old end date or if an
                // open-ended period is silently pro-rated instead of receiving the full amount.
                r.Check(
                    expectedSchedule,
                    "an open-ended employee is booked through the whole window at full periods",
                    $"reportTick={report?.computedTick ?? -1}, endTick={contract.endTick}, " +
                    $"termDays={contract.termDays}, firstExpected={firstExpected}, " +
                    $"laterExpected={laterExpected}, intervalTicks={interval}; {DayValues(report)}");
            }
            finally
            {
                state.Employments.Remove(contract);
            }
        }

        private static void CheckEmptyReport(
            Results r, IntercolonyWorldComponent state)
        {
            CashFlowReport empty = CashFlowForecast.Compute(state);
            CashFlowReport nullState = CashFlowForecast.Compute(null);
            int emptyDay = FirstEmptyDay(empty);
            int emptyDayRevenue = RevenueAt(empty, emptyDay);
            int emptyDayExpenses = ExpenseAt(empty, emptyDay);

            // This must fail if empty buckets are omitted, populated with nonzero defaults, or if
            // the null-world pure computation returns no report instead of five empty days.
            r.Check(
                DayCount(empty) == CashFlowForecast.WindowDays &&
                emptyDay >= 0 && emptyDayRevenue == 0 && emptyDayExpenses == 0 &&
                DayCount(nullState) == CashFlowForecast.WindowDays && AllDaysZero(nullState),
                "the forecast always has five days and empty days are zero",
                $"expectedDays={CashFlowForecast.WindowDays}, stateDays={DayCount(empty)}, " +
                $"emptyDay={emptyDay}, emptyRevenue={emptyDayRevenue}, " +
                $"emptyExpenses={emptyDayExpenses}, nullDays={DayCount(nullState)}, " +
                $"nullRevenue={TotalRevenue(nullState)}, nullExpenses={TotalExpenses(nullState)}; " +
                $"state={DayValues(empty)}; null={DayValues(nullState)}");
        }

        private static void CheckNetArithmetic(
            Results r, IntercolonyWorldComponent state)
        {
            int now = GenTicks.TicksGame;
            RecurringContract revenueFixture = new RecurringContract
            {
                id = -8105,
                thingDef = ThingDefOf.WoodLog,
                quantityPerCycle = 3,
                cadenceTicks = GenDate.TicksPerDay,
                totalCycles = 1,
                unitPrice = 200f,
                status = ContractStatus.Active,
                nextCycleTick = now
            };
            ProcurementContract expenseFixture = new ProcurementContract
            {
                id = -8106,
                thingDef = ThingDefOf.WoodLog,
                quantityPerCycle = 2,
                unitPrice = 90f,
                cadenceDays = 1,
                totalCycles = 1,
                nextCycleTick = now + GenDate.TicksPerDay,
                status = ProcurementContractStatus.Active,
                activeOrderId = ProcurementContract.NoActiveOrderId
            };

            try
            {
                state.AddContract(revenueFixture);
                state.AddProcurementContract(expenseFixture);
                CashFlowReport report = CashFlowForecast.Compute(state);
                int revenue = TotalRevenue(report);
                int expenses = TotalExpenses(report);

                // This must fail if any day recomputes net with the wrong sign or swaps revenue
                // and expenses; both totals are nonzero so an all-zero table cannot pass.
                r.Check(
                    revenue > 0 && expenses > 0 && EveryNetMatches(report),
                    "every forecast day satisfies Net = revenue - expenses",
                    $"totalRevenue={revenue}, totalExpenses={expenses}, totalNet={TotalNet(report)}, " +
                    $"expectedTotalNet={revenue - expenses}; {DayValues(report)}");
            }
            finally
            {
                state.Contracts.Remove(revenueFixture);
                state.ProcurementContracts.Remove(expenseFixture);
            }
        }

        private static void CheckOpenPurchaseOrder(
            Results r, IntercolonyWorldComponent state)
        {
            int now = GenTicks.TicksGame;
            RecurringContract revenueFixture = new RecurringContract
            {
                id = -8107,
                thingDef = ThingDefOf.WoodLog,
                quantityPerCycle = 3,
                cadenceTicks = GenDate.TicksPerDay,
                totalCycles = 1,
                unitPrice = 150f,
                status = ContractStatus.Active,
                nextCycleTick = now
            };
            ProcurementContract expenseFixture = new ProcurementContract
            {
                id = -8108,
                thingDef = ThingDefOf.WoodLog,
                quantityPerCycle = 2,
                unitPrice = 75f,
                cadenceDays = 1,
                totalCycles = 1,
                nextCycleTick = now + GenDate.TicksPerDay,
                status = ProcurementContractStatus.Active,
                activeOrderId = ProcurementContract.NoActiveOrderId
            };
            PurchaseOrder order = new PurchaseOrder
            {
                id = -8109,
                thingDef = ThingDefOf.WoodLog,
                quantity = 5,
                unitPrice = 61f,
                paidSilver = 305,
                orderedTick = now,
                readyTick = now + 2 * GenDate.TicksPerDay,
                status = PurchaseOrderStatus.Confirmed
            };

            try
            {
                state.AddContract(revenueFixture);
                state.AddProcurementContract(expenseFixture);
                CashFlowReport withoutOrder = CashFlowForecast.Compute(state);
                state.AddPurchaseOrder(order);
                CashFlowReport withOrder = CashFlowForecast.Compute(state);

                // This must fail if an open purchase order is treated as a future expense even
                // though its paidSilver already left at creation and would then be double-counted.
                r.Check(
                    SameAmounts(withoutOrder, withOrder),
                    "an open purchase order does not change the forecast",
                    $"orderPaidSilver={order.paidSilver}, orderedTick={order.orderedTick}, " +
                    $"readyTick={order.readyTick}, without=({TotalRevenue(withoutOrder)} revenue, " +
                    $"{TotalExpenses(withoutOrder)} expenses, {TotalNet(withoutOrder)} net), " +
                    $"with=({TotalRevenue(withOrder)} revenue, {TotalExpenses(withOrder)} expenses, " +
                    $"{TotalNet(withOrder)} net); withoutDays={DayValues(withoutOrder)}; " +
                    $"withDays={DayValues(withOrder)}");
            }
            finally
            {
                state.Contracts.Remove(revenueFixture);
                state.ProcurementContracts.Remove(expenseFixture);
                state.PurchaseOrders.Remove(order);
            }
        }

        private static void CheckInvalidation(
            Results r, IntercolonyWorldComponent state)
        {
            CashFlowForecast.Invalidate();
            CashFlowReport before = CashFlowForecast.Current(state);
            int now = GenTicks.TicksGame;
            SalesOrder order = new SalesOrder
            {
                id = -8110,
                line = new OrderLine(ThingDefOf.WoodLog, 3),
                unitPrice = 77f,
                deadlineTick = now + GenDate.TicksPerDay,
                status = SalesOrderStatus.Accepted
            };
            order.DiscountFraction = 0.20f;

            try
            {
                state.Orders.Add(order);
                CashFlowReport withoutInvalidation = CashFlowForecast.Current(state);
                CashFlowForecast.Invalidate();
                CashFlowReport after = CashFlowForecast.Current(state);
                int expected = order.DiscountedTotalPayment;
                int beforeRevenue = TotalRevenue(before);
                int staleRevenue = TotalRevenue(withoutInvalidation);
                int afterRevenue = TotalRevenue(after);
                int orderDay = DayIndex(after, order.deadlineTick);
                int afterOrderDayRevenue = RevenueAt(after, orderDay);

                // This must fail if Invalidate leaves the old cached report alive after a new
                // obligation is added and Current is asked for again.
                r.Check(
                    staleRevenue == beforeRevenue &&
                    afterRevenue == beforeRevenue + expected &&
                    orderDay >= 0 && afterOrderDayRevenue == expected,
                    "Invalidate makes Current recompute newly added obligations",
                    $"beforeTick={before?.computedTick ?? -1}, staleTick={withoutInvalidation?.computedTick ?? -1}, " +
                    $"afterTick={after?.computedTick ?? -1}, beforeRevenue={beforeRevenue}, " +
                    $"staleRevenue={staleRevenue}, afterRevenue={afterRevenue}, expected={expected}, " +
                    $"orderDay={orderDay}, afterOrderDayRevenue={afterOrderDayRevenue}; " +
                    $"before={DayValues(before)}; after={DayValues(after)}");
            }
            finally
            {
                state.Orders.Remove(order);
                CashFlowForecast.Invalidate();
            }
        }

        private static int DayIndex(CashFlowReport report, long tick)
        {
            if (report?.days == null)
            {
                return -1;
            }

            for (int i = 0; i < report.days.Count; i++)
            {
                CashFlowDay day = report.days[i];
                if (day != null && tick >= day.startTick && tick < day.endTick)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int RevenueAt(CashFlowReport report, int dayIndex)
        {
            return report?.days != null && dayIndex >= 0 && dayIndex < report.days.Count &&
                   report.days[dayIndex] != null
                ? report.days[dayIndex].revenue
                : 0;
        }

        private static int ExpenseAt(CashFlowReport report, int dayIndex)
        {
            return report?.days != null && dayIndex >= 0 && dayIndex < report.days.Count &&
                   report.days[dayIndex] != null
                ? report.days[dayIndex].expenses
                : 0;
        }

        private static bool HasOnlyExpenseAt(
            CashFlowReport report, int targetDay, int expected)
        {
            if (report?.days == null || targetDay < 0 || targetDay >= report.days.Count)
            {
                return false;
            }

            for (int i = 0; i < report.days.Count; i++)
            {
                if (ExpenseAt(report, i) != (i == targetDay ? expected : 0))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool EveryNetMatches(CashFlowReport report)
        {
            if (report?.days == null)
            {
                return false;
            }

            foreach (CashFlowDay day in report.days)
            {
                if (day == null || day.Net != day.revenue - day.expenses)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AllDaysZero(CashFlowReport report)
        {
            if (report?.days == null)
            {
                return false;
            }

            foreach (CashFlowDay day in report.days)
            {
                if (day == null || day.revenue != 0 || day.expenses != 0 || day.Net != 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static int FirstEmptyDay(CashFlowReport report)
        {
            if (report?.days == null)
            {
                return -1;
            }

            for (int i = 0; i < report.days.Count; i++)
            {
                if (RevenueAt(report, i) == 0 && ExpenseAt(report, i) == 0)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool SameAmounts(CashFlowReport left, CashFlowReport right)
        {
            if (left?.days == null || right?.days == null ||
                TotalRevenue(left) != TotalRevenue(right) ||
                TotalExpenses(left) != TotalExpenses(right) ||
                TotalNet(left) != TotalNet(right) ||
                left.days.Count != right.days.Count)
            {
                return false;
            }

            for (int i = 0; i < left.days.Count; i++)
            {
                if (RevenueAt(left, i) != RevenueAt(right, i) ||
                    ExpenseAt(left, i) != ExpenseAt(right, i))
                {
                    return false;
                }
            }

            return true;
        }

        private static int DayCount(CashFlowReport report)
        {
            return report?.days?.Count ?? -1;
        }

        private static int TotalRevenue(CashFlowReport report)
        {
            return report?.TotalRevenue ?? 0;
        }

        private static int TotalExpenses(CashFlowReport report)
        {
            return report?.TotalExpenses ?? 0;
        }

        private static int TotalNet(CashFlowReport report)
        {
            return report?.TotalNet ?? 0;
        }

        private static string DayValues(CashFlowReport report)
        {
            if (report == null)
            {
                return "report=null";
            }

            if (report.days == null)
            {
                return "days=null";
            }

            StringBuilder values = new StringBuilder();
            values.Append("days[");
            for (int i = 0; i < report.days.Count; i++)
            {
                if (i > 0)
                {
                    values.Append(", ");
                }

                CashFlowDay day = report.days[i];
                values.Append(i).Append(":r=").Append(day?.revenue ?? 0)
                    .Append("/e=").Append(day?.expenses ?? 0)
                    .Append("/n=").Append(day?.Net ?? 0);
            }

            values.Append("]");
            return values.ToString();
        }

        private static string Summarize(Results r)
        {
            r.sb.AppendLine();
            r.sb.AppendLine(
                $"  {r.passed} passed, {r.failed} failed, {r.skipped} skipped.");
            return r.sb.ToString();
        }
    }
}
