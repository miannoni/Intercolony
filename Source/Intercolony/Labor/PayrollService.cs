using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Recurring payroll and wage arrears (DESIGN.md §38, §39, §111).
    ///
    /// §38's requirement is one sentence and it drives everything here: *"Lack of money should
    /// not simply block time. It should create arrears."* So a period that cannot be met does not
    /// pause the contract or refuse the hire — it pays what it can, records the shortfall, and
    /// starts an escalation the player can see coming and can still stop.
    ///
    /// §39's closing line sets the tone for how harsh that escalation is: *"The first missed
    /// payroll should not instantly destroy the colony. Failure should be playable."*
    /// </summary>
    public static class PayrollService
    {
        /// <summary>Missed periods before the worker downs tools (§39 step 4).</summary>
        public const int MissesBeforeRefusingWork = 2;

        /// <summary>Missed periods before the worker walks out (§39 step 5).</summary>
        public const int MissesBeforeQuitting = 3;

        /// <summary>
        /// Runs the pay periods that have come due. Called on the world component's hourly beat,
        /// which is far finer than the shortest pay period (one day).
        /// </summary>
        public static void Advance(List<EmploymentContract> contracts, List<LaborDebt> debts,
            IntercolonyWorldComponent state)
        {
            if (contracts == null)
            {
                return;
            }

            int now = GenTicks.TicksGame;

            for (int i = contracts.Count - 1; i >= 0; i--)
            {
                EmploymentContract contract = contracts[i];

                if (contract.status != EmploymentStatus.Active ||
                    !contract.wageStructure.IsPeriodic() ||
                    contract.nextPaymentTick < 0 ||
                    now < contract.nextPaymentTick)
                {
                    continue;
                }

                PayPeriod(contract, debts, state);
            }
        }

        /// <summary>
        /// Settles one pay period. Pays what the colony has, records the rest as arrears, and
        /// escalates if this is not the first miss.
        /// </summary>
        private static void PayPeriod(EmploymentContract contract, List<LaborDebt> debts,
            IntercolonyWorldComponent state)
        {
            Map map = contract.destinationMap ?? contract.pawn?.MapHeld ?? Find.AnyPlayerHomeMap;

            // Wages for the period, plus anything still owed from previous periods: a worker who
            // was short-changed last quadrum is owed that too, not just this quadrum's wage.
            int due = contract.PeriodPayment + contract.arrearsSilver;

            // A term shorter than the pay period, or a final partial period, is paid pro rata
            // rather than rounded up to a whole period the worker did not serve.
            int daysLeftInTerm = Mathf.CeilToInt(Mathf.Max(0f, contract.DaysRemaining));
            if (daysLeftInTerm < contract.wageStructure.IntervalDays() && daysLeftInTerm >= 0)
            {
                int interval = contract.wageStructure.IntervalDays();
                int served = Mathf.Clamp(interval - daysLeftInTerm, 0, interval);
                due = contract.dailyWage * served + contract.arrearsSilver;
            }

            int available = PurchaseOrderService.CountColonySilver(map);
            int paid = Mathf.Min(due, available);

            if (paid > 0)
            {
                PurchaseOrderService.TryTakeSilver(map, paid);
                contract.paidSilver += paid;

                LedgerService.Record(LedgerKind.WagePayment, -paid, contract.settlementName,
                    $"{contract.workerName}, {contract.wageStructure.Label()} wages");
            }

            int shortfall = due - paid;
            contract.arrearsSilver = shortfall;

            // Schedule the next period before any early return, so a contract cannot get stuck
            // re-running the same overdue period every hour.
            AdvancePaymentClock(contract);

            if (shortfall <= 0)
            {
                OnPeriodPaid(contract, paid);
                return;
            }

            contract.missedPayments++;
            EmployerReputationService.NotePayrollMissed(state, contract);
            OnPeriodMissed(contract, paid, shortfall, available, debts, state);
        }

        private static void OnPeriodPaid(EmploymentContract contract, int paid)
        {
            bool refusingOverWages = contract.refusingWork &&
                                     contract.refusalReason == WorkRefusalReason.UnpaidWages;

            if (contract.missedPayments == 0 && !refusingOverWages)
            {
                // Routine payday. No letter — a message per quadrum per worker would be noise. A
                // worker refusing over §42 combat misuse counts as routine here on purpose: paying
                // them does not settle that grievance and must not claim to.
                return;
            }

            contract.missedPayments = 0;
            contract.ResumeWork(WorkRefusalReason.UnpaidWages);
            EmployerReputationService.NoteArrearsCleared(IntercolonyWorldComponent.Current);

            IntercolonyLetters.Send(
                IntercolonyLetterImportance.Important,
                "Wages settled",
                $"{contract.workerName} has been paid {paid} silver, clearing what was owed.\n\n" +
                (refusingOverWages
                    ? "They are back at work, on the priorities they had before they stopped."
                    : "They are working normally again."),
                LetterDefOf.PositiveEvent, contract.pawn);
        }

        private static void OnPeriodMissed(EmploymentContract contract, int paid, int shortfall,
            int available, List<LaborDebt> debts, IntercolonyWorldComponent state)
        {
            string money = $"Paid {paid} of {paid + shortfall} silver — " +
                           $"{shortfall} short, with {available} in storage at the time.";

            if (contract.missedPayments >= MissesBeforeQuitting)
            {
                // §39 steps 5 to 8: the worker leaves, the debt does not, the colony's name as an
                // employer takes the sharpest hit available, and the worker's own faction hears.
                RecordDebt(contract, debts, state);
                EmployerReputationService.NoteWalkOut(state, contract);

                EmploymentService.End(contract, EmploymentStatus.Quit,
                    $"{contract.workerName} walked out over {contract.arrearsSilver} silver in unpaid wages");
                return;
            }

            if (contract.missedPayments >= MissesBeforeRefusingWork)
            {
                contract.HoldWork(WorkRefusalReason.UnpaidWages);

                IntercolonyLetters.Send(
                    IntercolonyLetterImportance.Always,
                    "Employee has stopped working",
                    $"{contract.workerName} has not been paid {contract.missedPayments} times running and " +
                    "has downed tools.\n\n" + money + "\n\n" +
                    $"They are owed {contract.arrearsSilver} silver. Pay it from the Labor tab and they " +
                    "will go back to the priorities they had.\n\n" +
                    $"If a {OrdinalMiss(MissesBeforeQuitting)} payment is missed they will leave, and the " +
                    "debt will stay on your record.",
                    LetterDefOf.ThreatSmall, contract.pawn);
                return;
            }

            // §39 step 2: a warning, before anything actually breaks.
            IntercolonyLetters.Send(
                IntercolonyLetterImportance.Always,
                "Payroll missed",
                $"{contract.workerName}'s wages could not be paid in full.\n\n" + money + "\n\n" +
                $"They are owed {contract.arrearsSilver} silver, payable from the Labor tab. " +
                "Miss another and they will stop working; a third and they will leave.",
                LetterDefOf.NegativeEvent, contract.pawn);
        }

        private static string OrdinalMiss(int count)
        {
            switch (count)
            {
                case 2: return "second";
                case 3: return "third";
                case 4: return "fourth";
                default: return count + "th";
            }
        }

        /// <summary>
        /// Moves the pay clock forward one period, or stops it once the term is over.
        ///
        /// Advances by whole periods from the *scheduled* time rather than from now, so a payment
        /// processed an hour late does not drift the whole schedule an hour later each time.
        /// </summary>
        private static void AdvancePaymentClock(EmploymentContract contract)
        {
            int interval = contract.wageStructure.IntervalDays() * GenDate.TicksPerDay;
            if (interval <= 0)
            {
                contract.nextPaymentTick = -1;
                return;
            }

            int next = contract.nextPaymentTick + interval;
            while (next <= GenTicks.TicksGame)
            {
                next += interval;
            }

            // Past the end of the term there is nothing left to earn; the final settlement
            // happens when employment ends.
            contract.nextPaymentTick = contract.endTick >= 0 && next > contract.endTick ? -1 : next;
        }

        /// <summary>Starts the pay clock. Called when the worker arrives and begins earning.</summary>
        public static void BeginPayroll(EmploymentContract contract)
        {
            if (!contract.wageStructure.IsPeriodic())
            {
                contract.nextPaymentTick = -1;
                return;
            }

            int interval = contract.wageStructure.IntervalDays() * GenDate.TicksPerDay;
            contract.nextPaymentTick = GenTicks.TicksGame + interval;
        }

        /// <summary>
        /// Final settlement when employment ends for any reason.
        ///
        /// Pays for days actually worked since the last payday, so a worker dismissed mid-period
        /// is not stiffed and one who leaves early is not overpaid. Anything the colony cannot
        /// cover becomes a debt (§39 step 6).
        /// </summary>
        public static void SettleOnEnd(EmploymentContract contract, EmploymentStatus status,
            List<LaborDebt> debts, IntercolonyWorldComponent state)
        {
            if (!contract.wageStructure.IsPeriodic())
            {
                // Prepaid is already settled by definition. §37's risk cuts both ways: the
                // player does not get silver back, and the worker keeps what was paid.
                return;
            }

            // Quitting already recorded its debt on the way out.
            if (status == EmploymentStatus.Quit)
            {
                return;
            }

            int owed = contract.arrearsSilver + EarnedSinceLastPayday(contract);
            if (owed <= 0)
            {
                return;
            }

            Map map = contract.destinationMap ?? contract.pawn?.MapHeld ?? Find.AnyPlayerHomeMap;
            int available = PurchaseOrderService.CountColonySilver(map);
            int paid = Mathf.Min(owed, available);

            if (paid > 0)
            {
                PurchaseOrderService.TryTakeSilver(map, paid);
                contract.paidSilver += paid;

                LedgerService.Record(LedgerKind.WagePayment, -paid, contract.settlementName,
                    $"{contract.workerName}, final settlement");
            }

            contract.arrearsSilver = owed - paid;

            if (contract.arrearsSilver > 0)
            {
                RecordDebt(contract, debts, state);
            }
        }

        /// <summary>Wages earned since the last payday but not yet due.</summary>
        private static int EarnedSinceLastPayday(EmploymentContract contract)
        {
            int interval = contract.wageStructure.IntervalDays() * GenDate.TicksPerDay;
            if (interval <= 0)
            {
                return 0;
            }

            // nextPaymentTick is one interval after the last payday, so working backwards from it
            // gives when the current period began. A cleared clock (term over) means the period
            // began one interval before the term ended.
            int periodStart = contract.nextPaymentTick >= 0
                ? contract.nextPaymentTick - interval
                : Mathf.Max(contract.arrivalTick, contract.endTick - interval);

            int workedTicks = Mathf.Max(0, GenTicks.TicksGame - periodStart);
            int workedDays = workedTicks / GenDate.TicksPerDay;

            return Mathf.Clamp(workedDays, 0, contract.wageStructure.IntervalDays()) * contract.dailyWage;
        }

        private static void RecordDebt(EmploymentContract contract, List<LaborDebt> debts,
            IntercolonyWorldComponent state)
        {
            if (contract.arrearsSilver <= 0 || debts == null || state == null)
            {
                return;
            }

            debts.Add(new LaborDebt
            {
                id = state.NextId(),
                settlementId = contract.settlementId,
                settlementName = contract.settlementName,
                factionName = contract.factionName,
                workerName = contract.workerName,
                amountOwed = contract.arrearsSilver,
                originalAmount = contract.arrearsSilver,
                incurredTick = GenTicks.TicksGame,
                missedPayments = contract.missedPayments
            });

            IntercolonyLog.Message(
                $"Labor debt recorded: {contract.arrearsSilver} silver to {contract.settlementName} " +
                $"for {contract.workerName}.");
        }

        /// <summary>
        /// Pays down what is owed to a working employee, from colony silver. Clears the mood
        /// penalty and puts them back to work if that settles the account.
        /// </summary>
        public static bool TryPayArrears(EmploymentContract contract, Map map, out string failReason)
        {
            failReason = null;

            if (contract == null || contract.arrearsSilver <= 0)
            {
                failReason = "Nothing owed.";
                return false;
            }

            map = map ?? contract.destinationMap ?? Find.AnyPlayerHomeMap;
            int available = PurchaseOrderService.CountColonySilver(map);
            if (available < contract.arrearsSilver)
            {
                failReason = $"Not enough silver in storage: {available} of {contract.arrearsSilver} needed.";
                return false;
            }

            if (!PurchaseOrderService.TryTakeSilver(map, contract.arrearsSilver))
            {
                failReason = "Could not collect the silver.";
                return false;
            }

            int settled = contract.arrearsSilver;
            contract.paidSilver += settled;

            LedgerService.Record(LedgerKind.WagePayment, -settled, contract.settlementName,
                $"{contract.workerName}, arrears cleared");
            contract.arrearsSilver = 0;
            contract.missedPayments = 0;
            contract.ResumeWork(WorkRefusalReason.UnpaidWages);
            EmployerReputationService.NoteArrearsCleared(IntercolonyWorldComponent.Current);

            Messages.Message(
                contract.refusingWork
                    ? $"Paid {contract.workerName} the {settled} silver owed. They are still refusing " +
                      "work — that is not about the money."
                    : $"Paid {contract.workerName} the {settled} silver owed. They are back at work.",
                MessageTypeDefOf.PositiveEvent, historical: false);

            return true;
        }

        /// <summary>
        /// Pays off a debt left behind by a worker who has already gone. §39 keeps the obligation
        /// after the employment ends, so the player needs a way to make it good.
        /// </summary>
        public static bool TrySettleDebt(LaborDebt debt, Map map, out string failReason)
        {
            failReason = null;

            if (debt == null || debt.IsSettled)
            {
                failReason = "Nothing owed.";
                return false;
            }

            map = map ?? Find.AnyPlayerHomeMap;
            int available = PurchaseOrderService.CountColonySilver(map);
            if (available < debt.amountOwed)
            {
                failReason = $"Not enough silver in storage: {available} of {debt.amountOwed} needed.";
                return false;
            }

            if (!PurchaseOrderService.TryTakeSilver(map, debt.amountOwed))
            {
                failReason = "Could not collect the silver.";
                return false;
            }

            int settled = debt.amountOwed;
            debt.amountOwed = 0;

            LedgerService.Record(LedgerKind.DebtSettlement, -settled, debt.settlementName,
                $"{debt.workerName}, {debt.KindLabel()}");
            EmployerReputationService.NoteDebtSettled(IntercolonyWorldComponent.Current, debt);

            IntercolonyLetters.Send(
                IntercolonyLetterImportance.Important,
                "Debt settled",
                $"{settled} silver has been sent to {debt.settlementName} to cover " +
                $"{debt.KindLabel()} for {debt.workerName}.\n\n" +
                "Paying late is better than not paying, but it is on the record either way.",
                LetterDefOf.NeutralEvent);

            IntercolonyLog.Message($"Labor debt settled: {debt}");
            return true;
        }

        /// <summary>Total unpaid wages, live arrears plus debts left by departed workers.</summary>
        public static int TotalOwed(IntercolonyWorldComponent state)
        {
            if (state == null)
            {
                return 0;
            }

            int total = 0;
            foreach (EmploymentContract contract in state.Employments)
            {
                if (contract.status == EmploymentStatus.Active)
                {
                    total += contract.arrearsSilver;
                }
            }

            foreach (LaborDebt debt in state.LaborDebts)
            {
                total += debt.amountOwed;
            }

            return total;
        }
    }
}
