using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// End-to-end check of Phase 18's acceptance criteria (DESIGN.md §111, §37, §38, §39).
    ///
    /// The whole point of §39 is that running out of silver produces an escalation rather than a
    /// crash or a silent deletion, so this test *deliberately starves the colony* and walks the
    /// escalation from warning to walk-out, asserting each stage and the debt left behind.
    ///
    /// Drives the real services throughout. The recurring lesson from Phase 4 and Phase 16 is
    /// that a test built against a convenient stand-in passes without proving anything, and that
    /// a skipped branch is worse than a failing one because it looks like a pass.
    /// </summary>
    public static class IntercolonyPayrollSelfTest
    {
        private class Results
        {
            public readonly StringBuilder sb = new StringBuilder();
            public int passed;
            public int failed;

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

        public static string Run(IntercolonyWorldComponent state, Map map)
        {
            Results r = new Results();
            r.sb.AppendLine("Payroll and arrears self-test (DESIGN.md §111, §37-§39)");

            if (state == null || map == null)
            {
                r.sb.AppendLine("  No world or map. Open a colony first.");
                return Summarize(r);
            }

            IntercolonyLaborSelfTestSupport.ResetLedger();

            try
            {
                CheckWageStructureMaths(r);
                CheckEscalation(r, state, map);
            }
            catch (System.Exception ex)
            {
                r.sb.AppendLine($"  EXCEPTION: {ex}");
                r.failed++;
            }
            finally
            {
                // The test starves the colony on purpose. Give back whatever it consumed net, so
                // running a dev check does not cost the player their treasury.
                int returned = IntercolonyLaborSelfTestSupport.RestoreLedger(map);
                if (returned > 0)
                {
                    r.Info($"returned {returned} silver the test had consumed.");
                }

                LaborCandidateService.Clear();
            }

            return Summarize(r);
        }

        /// <summary>§37's invariants, which must hold for every term length rather than one.</summary>
        private static void CheckWageStructureMaths(Results r)
        {
            int prepaidCheaper = 0;
            int upFrontOnlyPrepaid = 0;
            int periodicMatchesGross = 0;
            int samples = 0;

            for (int wage = 5; wage <= 80; wage += 5)
            {
                for (int term = 1; term <= 60; term++)
                {
                    samples++;
                    int gross = wage * term;

                    int prepaid = WageStructureUtility.TotalCost(WageStructure.Prepaid, wage, term);
                    int daily = WageStructureUtility.TotalCost(WageStructure.Daily, wage, term);
                    int quadrum = WageStructureUtility.TotalCost(WageStructure.Quadrum, wage, term);

                    if (prepaid < gross || gross <= 1)
                    {
                        prepaidCheaper++;
                    }

                    // Prepaid cheapest, per-quadrum in the middle, daily dearest. Paying as you
                    // go buys the freedom to stop, and that is what the player pays for.
                    if (prepaid < quadrum && quadrum < daily)
                    {
                        periodicMatchesGross++;
                    }

                    if (WageStructureUtility.UpFrontCost(WageStructure.Prepaid, wage, term) == prepaid &&
                        WageStructureUtility.UpFrontCost(WageStructure.Daily, wage, term) ==
                            WageStructureUtility.SigningFee(WageStructure.Daily, wage) &&
                        WageStructureUtility.UpFrontCost(WageStructure.Quadrum, wage, term) ==
                            WageStructureUtility.SigningFee(WageStructure.Quadrum, wage) &&
                        WageStructureUtility.SigningFee(WageStructure.Daily, wage) >
                            WageStructureUtility.SigningFee(WageStructure.Quadrum, wage))
                    {
                        upFrontOnlyPrepaid++;
                    }
                }
            }

            r.Check(prepaidCheaper == samples,
                "prepaying is always cheaper in total (§37 'discounted total cost')",
                $"{prepaidCheaper}/{samples} wage/term combinations");
            r.Check(periodicMatchesGross == samples,
                "prepaid is cheapest, per-quadrum next, paying by the day dearest",
                $"{periodicMatchesGross}/{samples}");
            r.Check(upFrontOnlyPrepaid == samples,
                "pay-as-you-go takes a signing fee at hire, larger for daily than per-quadrum",
                $"{upFrontOnlyPrepaid}/{samples}");
            r.Check(WageStructure.Quadrum.IntervalDays() == GenDate.DaysPerQuadrum,
                "a quadrum pay period is a real quadrum",
                $"{WageStructure.Quadrum.IntervalDays()} days");
            r.Check(WageStructure.Daily.IntervalDays() == 1, "a daily pay period is one day");
            r.Check(!WageStructure.Prepaid.IsPeriodic() && WageStructure.Daily.IsPeriodic(),
                "prepaid is not on a schedule and daily is");
        }

        /// <summary>
        /// §39's escalation, driven for real: hire on a daily wage, pay one period properly, then
        /// strip the colony of silver and miss periods until the worker walks out.
        /// </summary>
        private static void CheckEscalation(Results r, IntercolonyWorldComponent state, Map map)
        {
            List<LaborCandidate> pool = LaborCandidateService.Refresh(state, force: true);
            r.Check(pool.Count > 0, "candidate pool is not empty", $"{pool.Count} workers offered");
            if (pool.Count == 0)
            {
                return;
            }

            LaborCandidate candidate = pool[0];
            int term = Mathf.Max(candidate.minTermDays, 20);

            // The pool's quoted daily wage is not quite the wage TryHire will use: hiring prices
            // the chosen term again with the settlement, distance, reputation and combat clause.
            // Keep that money calculation in EmploymentService rather than duplicating it here,
            // and fund four times the quote's up-front cost as a deliberately generous margin.
            // Over-funding is harmless because this test strips every silver stack before it starts
            // missing payroll; the extra balance therefore cannot soften the escalation under test.
            int quotedUpFront = WageStructureUtility.UpFrontCost(
                WageStructure.Daily, candidate.dailyWage, term);
            IntercolonyLaborSelfTestSupport.EnsureSilver(map, quotedUpFront * 4);

            // Daily wage, so one pay period is one day and the escalation can be driven without
            // simulating a quadrum.
            EmploymentContract contract = EmploymentService.TryHire(
                state, candidate, term, map, out string failReason, WageStructure.Daily,
                CombatClause.Civilian);

            r.Check(contract != null, "hired on a daily wage", failReason ?? "");
            if (contract == null)
            {
                return;
            }

            // A periodic hire takes the signing fee up front and nothing else — not the term.
            // This asserted `paidSilver == 0`, which stopped being true when daily and per-quadrum
            // hires gained a five-day signing fee: WageStructure.UpFrontCost returns SigningFee
            // for every non-prepaid structure, and 0.9.2 shipped a fix specifically to *disclose*
            // that fee, so the charge is deliberate and the assertion was stale. The distinction
            // still worth guarding is that a periodic hire is not charged for the whole term.
            // Multiply the days out here rather than calling SigningFee, because SigningFee takes
            // the *base* wage and applies the daily premium itself (WageStructure.cs:82), while
            // contract.dailyWage has already had that premium applied (EmploymentService.cs:126,
            // 168). Passing one into the other charges the premium twice: on a base of 60 that is
            // 60 -> 81 -> 109, so the test demanded 545 where the hire correctly took 405.
            //
            // This assertion had never actually executed. The hire above it always failed for want
            // of silver, and the method returns early when it does, so the arithmetic was written
            // when the signing fee was introduced and then never run until the funding fix landed.
            int expectedSigningFee =
                contract.dailyWage * WageStructureUtility.SigningFeeDays(WageStructure.Daily);
            r.Check(contract.paidSilver == expectedSigningFee,
                "a periodic hire pays the signing fee up front and no more (§37)",
                $"{contract.paidSilver} silver, expected {expectedSigningFee}");
            r.Check(
                contract.paidSilver <
                WageStructureUtility.TotalCost(WageStructure.Daily, contract.dailyWage, term),
                "and is not charged for the whole term");
            r.Check(contract.nextPaymentTick < 0,
                "the pay clock does not start until the worker arrives");

            contract.arrivalTick = GenTicks.TicksGame;
            EmploymentService.Advance(state.Employments);

            r.Check(contract.status == EmploymentStatus.Active, "worker arrived",
                contract.status.ToString());
            if (contract.status != EmploymentStatus.Active)
            {
                return;
            }

            r.Check(contract.nextPaymentTick > GenTicks.TicksGame,
                "the pay clock starts on arrival, not at hire",
                $"first payday in {contract.DaysUntilPayment:0.##}d");

            // --- A period that CAN be paid ---
            int wage = contract.PeriodPayment;
            IntercolonyLaborSelfTestSupport.EnsureSilver(map, wage);

            int before = PurchaseOrderService.CountColonySilver(map);
            contract.nextPaymentTick = GenTicks.TicksGame;
            PayrollService.Advance(state.Employments, state.LaborDebts, state);

            int after = PurchaseOrderService.CountColonySilver(map);
            r.Check(before - after == wage, "a met pay period takes exactly the period's wage",
                $"{before} -> {after}, period is {wage}");
            r.Check(contract.arrearsSilver == 0 && contract.missedPayments == 0,
                "a met pay period leaves no arrears");
            r.Check(contract.nextPaymentTick > GenTicks.TicksGame,
                "the clock advances to the next period",
                $"next in {contract.DaysUntilPayment:0.##}d");

            // --- Starve the colony and miss period one: warning only ---
            IntercolonyLaborSelfTestSupport.StripSilver(map);
            r.Info($"colony silver stripped to {PurchaseOrderService.CountColonySilver(map)}.");

            contract.nextPaymentTick = GenTicks.TicksGame;
            PayrollService.Advance(state.Employments, state.LaborDebts, state);

            r.Check(contract.status == EmploymentStatus.Active,
                "a first missed payroll does not end employment (§39 'failure should be playable')",
                contract.status.ToString());
            r.Check(contract.arrearsSilver > 0, "the shortfall becomes arrears, not a blocked period",
                $"{contract.arrearsSilver} silver owed");
            r.Check(contract.missedPayments == 1, "one miss recorded",
                contract.missedPayments.ToString());
            r.Check(!contract.refusingWork, "the worker still works after one miss");

            int arrearsAfterOne = contract.arrearsSilver;

            // --- Miss period two: worker downs tools ---
            contract.nextPaymentTick = GenTicks.TicksGame;
            PayrollService.Advance(state.Employments, state.LaborDebts, state);

            r.Check(contract.status == EmploymentStatus.Active,
                "a second miss still does not end employment");
            r.Check(contract.refusingWork, "the worker downs tools on the second miss (§39 step 4)");
            r.Check(contract.arrearsSilver > arrearsAfterOne,
                "arrears accumulate across periods",
                $"{arrearsAfterOne} -> {contract.arrearsSilver}");

            if (contract.pawn?.workSettings != null && contract.pawn.workSettings.EverWork)
            {
                int active = 0;
                foreach (WorkTypeDef work in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                {
                    if (contract.pawn.workSettings.GetPriority(work) > 0)
                    {
                        active++;
                    }
                }

                r.Check(active == 0, "every work priority is zeroed while refusing",
                    $"{active} still enabled");
            }

            // The mood penalty is a situational thought, so it is true or false right now rather
            // than something that had to be granted. Assert the worker actually reads as unpaid.
            ThoughtDef unpaid = DefDatabase<ThoughtDef>.GetNamedSilentFail("Intercolony_UnpaidWages");
            r.Check(unpaid != null, "the unpaid-wages thought def loaded");
            if (unpaid?.Worker != null && contract.pawn != null)
            {
                ThoughtState moodState = unpaid.Worker.CurrentState(contract.pawn);
                r.Check(moodState.Active, "the worker is unhappy about the unpaid wages (§39 step 3)",
                    moodState.Active ? $"stage {moodState.StageIndex}" : "inactive");
            }

            // --- Recovering: pay it off, work resumes ---
            int owed = contract.arrearsSilver;
            IntercolonyLaborSelfTestSupport.EnsureSilver(map, owed);
            bool settled = PayrollService.TryPayArrears(contract, map, out string payFail);

            r.Check(settled, "arrears can be paid off", payFail ?? $"{owed} silver");
            r.Check(contract.arrearsSilver == 0, "paying up clears the arrears");
            r.Check(!contract.refusingWork, "paying up puts the worker back to work");
            r.Check(contract.missedPayments == 0, "paying up resets the miss counter");

            if (contract.pawn?.workSettings != null && contract.pawn.workSettings.EverWork)
            {
                int active = 0;
                foreach (WorkTypeDef work in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                {
                    if (contract.pawn.workSettings.GetPriority(work) > 0)
                    {
                        active++;
                    }
                }

                r.Check(active > 0, "the priorities they had are restored, not left blank",
                    $"{active} work types re-enabled");
            }

            if (unpaid?.Worker != null && contract.pawn != null)
            {
                r.Check(!unpaid.Worker.CurrentState(contract.pawn).Active,
                    "the mood penalty lifts the moment the debt is settled");
            }

            // --- All the way to a walk-out ---
            int debtsBefore = state.LaborDebts.Count;
            IntercolonyLaborSelfTestSupport.StripSilver(map);

            for (int i = 0; i < PayrollService.MissesBeforeQuitting && contract.IsOpen; i++)
            {
                contract.nextPaymentTick = GenTicks.TicksGame;
                PayrollService.Advance(state.Employments, state.LaborDebts, state);
            }

            r.Check(contract.status == EmploymentStatus.Quit,
                $"the worker walks out after {PayrollService.MissesBeforeQuitting} misses (§39 step 5)",
                contract.status.ToString());
            r.Check(state.LaborDebts.Count == debtsBefore + 1,
                "a debt record outlives the employment (§39 step 6)",
                $"{debtsBefore} -> {state.LaborDebts.Count}");
            r.Check(contract.pawn == null,
                "the closed record still holds no live references");

            if (state.LaborDebts.Count > debtsBefore)
            {
                LaborDebt debt = state.LaborDebts[state.LaborDebts.Count - 1];
                r.Check(debt.amountOwed > 0 && debt.originalAmount == debt.amountOwed,
                    "the debt records what is owed", $"{debt.amountOwed} silver");
                r.Check(debt.settlementId == contract.settlementId,
                    "the debt is against the settlement that supplied the worker",
                    debt.settlementName);
                r.Check(PayrollService.TotalOwed(state) >= debt.amountOwed,
                    "total owed includes debts from departed workers");

                // And it can be made good.
                IntercolonyLaborSelfTestSupport.EnsureSilver(map, debt.amountOwed);
                int owedNow = debt.amountOwed;
                bool paidOff = PayrollService.TrySettleDebt(debt, map, out string debtFail);
                r.Check(paidOff, "a debt can be settled after the fact", debtFail ?? $"{owedNow} silver");
                r.Check(debt.IsSettled && debt.originalAmount == owedNow,
                    "settling clears the balance but keeps the history",
                    $"owed {debt.amountOwed}, originally {debt.originalAmount}");
            }

            r.sb.AppendLine();
            r.sb.AppendLine("  Not covered here — check by hand:");
            r.sb.AppendLine("    * save while an employee is in arrears, reload, and confirm the arrears,");
            r.sb.AppendLine("      miss count and refusing-work state all survived (§61, §82);");
            r.sb.AppendLine("    * that a quadrum-paid worker really is paid every 15 days in normal play.");
        }

        private static string Summarize(Results r)
        {
            r.sb.AppendLine();
            r.sb.AppendLine($"  {r.passed} passed, {r.failed} failed.");
            return r.sb.ToString();
        }
    }
}
