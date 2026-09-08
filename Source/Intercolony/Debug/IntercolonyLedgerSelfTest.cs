using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// End-to-end check of Phase 24 (DESIGN.md §117, §75, §45).
    ///
    /// A ledger's only virtue is being right, and the way a ledger goes wrong is quietly: a movement
    /// recorded twice, one recorded with the wrong sign, or one that never gets recorded at all
    /// because a new payment path forgot to. None of those throw, and all of them produce a report
    /// that looks perfectly plausible.
    ///
    /// So the load-bearing test here is not "does Summarise add up" — it is **does the ledger agree
    /// with the colony's actual silver**. A real payment is put through the real service and the
    /// storage count is measured either side of it.
    /// </summary>
    public static class IntercolonyLedgerSelfTest
    {
        private class Results
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

            public void Skip(string label, string reason)
            {
                skipped++;
                sb.AppendLine($"SKIPPED {label} — {reason}");
            }
        }

        public static string Run(IntercolonyWorldComponent state, Map map)
        {
            Results r = new Results();
            r.sb.AppendLine("Ledger and business report self-test (§117, §75, §45)");

            if (state == null || map == null)
            {
                r.sb.AppendLine("  No world or map. Open a colony first.");
                return Summarize(r);
            }

            int savedCount = state.Ledger.Count;
            int savedStart = state.LedgerStartTick;

            try
            {
                CheckRecording(r, state);
                CheckWindowing(r, state);
                CheckPartialHistoryIsAdmitted(r, state);
                CheckAgreesWithRealSilver(r, state, map);
                CheckContractEstimate(r, state);
                CheckProductionCommitments(r, state);
                CheckPruning(r, state);
            }
            catch (System.Exception ex)
            {
                r.sb.AppendLine($"  EXCEPTION: {ex}");
                r.failed++;
            }
            finally
            {
                while (state.Ledger.Count > savedCount)
                {
                    state.Ledger.RemoveAt(state.Ledger.Count - 1);
                }

                state.LedgerStartTick = savedStart;
                r.Info($"ledger restored to {state.Ledger.Count} entr(ies).");
            }

            return Summarize(r);
        }

        // --- Recording ---------------------------------------------------------------------

        private static void CheckRecording(Results r, IntercolonyWorldComponent state)
        {
            int before = state.Ledger.Count;

            LedgerService.Record(state, LedgerKind.SalePayment, 500, "Testholme", "probe");
            LedgerService.Record(state, LedgerKind.PurchasePayment, -200, "Testholme", "probe");

            r.Check(state.Ledger.Count == before + 2,
                "movements are recorded (§75)", $"{state.Ledger.Count - before} added");

            // A zero movement is not an event. Without this the ledger fills with rows for payments
            // that did not happen — a payroll run that paid nothing, a refund of nothing — and the
            // detail list becomes unreadable.
            LedgerService.Record(state, LedgerKind.WagePayment, 0, "Testholme", "nothing");
            r.Check(state.Ledger.Count == before + 2,
                "a zero movement records nothing");

            LedgerEntry entry = state.Ledger[state.Ledger.Count - 1];
            r.Check(entry.amount == -200 && !entry.IsIncome,
                "outgoings are stored negative and read as outgoings",
                $"{entry.amount}");

            r.Check(state.LedgerStartTick >= 0,
                "the first entry stamps when history began");
        }

        private static void CheckWindowing(Results r, IntercolonyWorldComponent state)
        {
            int start = state.Ledger.Count;

            // Baselines first. Summarise reports the *whole* ledger, so on any colony that has
            // actually traded these assertions were measuring the world's real sales plus the
            // fixtures below. In the first full-suite run that read 13,851 against an expected
            // ceiling of 1,700 — the windowing was working perfectly and the assertion was
            // measuring the wrong thing. Everything below is a delta.
            int quadrumBefore = LedgerService
                .Summarise(state, BusinessReportService.QuadrumDays).Of(LedgerKind.SalePayment);
            int yearBefore = LedgerService
                .Summarise(state, BusinessReportService.YearDays).Of(LedgerKind.SalePayment);

            // Inside the quadrum window.
            state.Ledger.Add(Aged(LedgerKind.SalePayment, 1000, 3));
            // Inside the year window but outside the quadrum.
            state.Ledger.Add(Aged(LedgerKind.SalePayment, 700, 40));
            // Outside both.
            state.Ledger.Add(Aged(LedgerKind.SalePayment, 9999, 400));

            int quadrumAdded = LedgerService
                .Summarise(state, BusinessReportService.QuadrumDays)
                .Of(LedgerKind.SalePayment) - quadrumBefore;
            int yearAdded = LedgerService
                .Summarise(state, BusinessReportService.YearDays)
                .Of(LedgerKind.SalePayment) - yearBefore;

            r.Check(quadrumAdded == 1000,
                "the quadrum window excludes older movements",
                $"{quadrumAdded} of the 11,699 added fell inside the quadrum");

            r.Check(yearAdded == 1700,
                "the year window includes what the quadrum leaves out",
                $"{yearAdded} of the 11,699 added fell inside the year");

            r.Check(yearAdded < 9999,
                "neither window includes movements older than a year",
                "the 400-day entry is excluded");

            // The bottom line has to be the sum of the parts, or §117's report lies about itself.
            LedgerService.Report report =
                LedgerService.Summarise(state, BusinessReportService.YearDays);
            int summed = 0;
            foreach (LedgerKind kind in LedgerEntry.ReportOrder)
            {
                summed += report.Of(kind);
            }

            r.Check(summed == report.Net,
                "the net line equals the sum of the lines above it (§117)",
                $"{summed} vs {report.Net}");

            while (state.Ledger.Count > start)
            {
                state.Ledger.RemoveAt(state.Ledger.Count - 1);
            }
        }

        /// <summary>
        /// A young colony must not be shown a confident quarter.
        ///
        /// This is the one piece of honesty the report cannot get from arithmetic: twelve days of
        /// trading summed under the heading "last quadrum" is not a wrong number, it is a wrong
        /// claim, and a player comparing it against a target would be comparing against nothing.
        /// </summary>
        private static void CheckPartialHistoryIsAdmitted(Results r, IntercolonyWorldComponent state)
        {
            int savedStart = state.LedgerStartTick;

            state.LedgerStartTick = GenTicks.TicksGame - 5 * GenDate.TicksPerDay;
            LedgerService.Report young =
                LedgerService.Summarise(state, BusinessReportService.QuadrumDays);

            r.Check(young.partial,
                "a five-day-old ledger reports a quadrum as partial (§117)",
                $"{young.daysCovered:0} days covered");

            state.LedgerStartTick = GenTicks.TicksGame - 200 * GenDate.TicksPerDay;
            LedgerService.Report mature =
                LedgerService.Summarise(state, BusinessReportService.QuadrumDays);

            r.Check(!mature.partial,
                "an established ledger reports a full period");

            state.LedgerStartTick = savedStart;
        }

        // --- The one that matters ----------------------------------------------------------

        /// <summary>
        /// The ledger must agree with the colony's actual silver.
        ///
        /// Everything else in this file tests arithmetic on numbers the test itself supplied. This
        /// drives a **real** payment through the **real** service and measures storage either side,
        /// because the way a ledger goes wrong is by disagreeing with reality — a sign flipped, a
        /// payment recorded twice, or a new payment path that forgot to record at all. None of those
        /// throw, and all of them leave a report that looks fine.
        /// </summary>
        private static void CheckAgreesWithRealSilver(Results r, IntercolonyWorldComponent state, Map map)
        {
            IntercolonyLaborSelfTestSupport.ResetLedger();
            IntercolonyLaborSelfTestSupport.EnsureSilver(map, 600);

            int silverBefore = PurchaseOrderService.CountColonySilver(map);
            if (silverBefore < 500)
            {
                r.Info("silver agreement skipped: could not stage enough silver.");
                IntercolonyLaborSelfTestSupport.RestoreLedger(map);
                return;
            }

            int ledgerBefore = state.Ledger.Count;

            // A debt settlement is the cleanest real payment to drive: one call, one movement, no
            // pawn, no contract lifecycle to unwind afterwards.
            LaborDebt debt = new LaborDebt
            {
                id = -860,
                settlementId = -1,
                settlementName = "Testholme",
                factionName = "Test Confederacy",
                workerName = "Probe",
                kind = LaborDebtKind.Wages,
                amountOwed = 300,
                originalAmount = 300,
                incurredTick = GenTicks.TicksGame
            };

            bool paid = PayrollService.TrySettleDebt(debt, map, out string failReason);
            r.Check(paid, "a real payment went through", failReason ?? "");

            if (!paid)
            {
                IntercolonyLaborSelfTestSupport.RestoreLedger(map);
                return;
            }

            int silverAfter = PurchaseOrderService.CountColonySilver(map);
            int actuallyLeft = silverBefore - silverAfter;

            int recorded = 0;
            for (int i = ledgerBefore; i < state.Ledger.Count; i++)
            {
                recorded += state.Ledger[i].amount;
            }

            r.Check(state.Ledger.Count == ledgerBefore + 1,
                "one payment produced exactly one ledger entry",
                $"{state.Ledger.Count - ledgerBefore} entr(ies)");

            r.Check(recorded == -actuallyLeft,
                "the ledger agrees with the silver that actually left storage (§75)",
                $"recorded {recorded}, storage fell by {actuallyLeft}");

            r.Check(recorded < 0,
                "money going out is recorded as going out",
                $"{recorded}");

            IntercolonyLaborSelfTestSupport.RestoreLedger(map);
            IntercolonyLaborSelfTestSupport.ResetLedger();
        }

        // --- §45's estimate ----------------------------------------------------------------

        private static void CheckContractEstimate(Results r, IntercolonyWorldComponent state)
        {
            RecurringContract contract = new RecurringContract
            {
                id = -861,
                settlementName = "Testholme",
                factionName = "Test Confederacy",
                thingDef = ThingDefOf.Steel,
                quantityPerCycle = 300,
                cadenceTicks = GenDate.TicksPerQuadrum,
                totalCycles = 8,
                unitPrice = 4f,
                status = ContractStatus.Active
            };

            BusinessReportService.ContractEstimate estimate =
                BusinessReportService.Estimate(state, contract);

            r.Check(estimate.revenue == contract.CycleValue,
                "revenue is the agreed price, not an estimate (§45)",
                $"{estimate.revenue}");

            r.Check(estimate.inputsIfBought < 0 && estimate.transport <= 0 && estimate.payroll <= 0,
                "every cost line is signed as a cost",
                $"inputs {estimate.inputsIfBought}, payroll {estimate.payroll}, " +
                $"transport {estimate.transport}");

            r.Check(estimate.Margin ==
                    estimate.revenue + estimate.inputsIfBought + estimate.payroll + estimate.transport,
                "the margin is the sum of the lines shown (§117)",
                $"{estimate.Margin}");

            // §45's "should I buy inputs or produce them?" only has an answer if buying is priced
            // the way procurement would really price it.
            float expectedUnit = IntercolonyPricing.BaseValue(ThingDefOf.Steel, null) *
                                 RfqService.SupplierMargin;
            int expectedInputs = -Mathf.RoundToInt(expectedUnit * contract.quantityPerCycle);

            r.Check(estimate.inputsIfBought == expectedInputs,
                "inputs are priced with procurement's own supplier margin, not a second number",
                $"{estimate.inputsIfBought} at x{RfqService.SupplierMargin} markup");

            // The transport line is the delivery premium. On a pickup-priced world it would be zero;
            // a recurring agreement always delivers, so it must not be.
            r.Check(estimate.transport < 0,
                "the delivery premium appears as the cost of hauling it (§45)",
                $"{estimate.transport} of {estimate.revenue} revenue");

            r.Check(BusinessReportService.Estimate(state, null) != null,
                "a missing contract estimates to nothing rather than throwing");
        }

        // --- F07 production commitments ----------------------------------------------------

        private static void CheckProductionCommitments(
            Results r, IntercolonyWorldComponent state)
        {
            const string committedAssertion =
                "R1 committed quantity per cycle is reported per cycle-length day";
            const string completedAssertion =
                "R2 completed production is reported for each good from the ledger";
            const string noProductionAssertion =
                "R3 no recorded production is flagged instead of presented as zero";
            const string suspendedAssertion =
                "R4 suspended seller agreements remain counted in Business commitments";

            List<RecurringContract> contracts = state?.Contracts;
            List<ProductionBucket> buckets = state?.ProductionLedger;
            ThingDef committedProduct = ThingDefOf.Steel;
            ThingDef secondProduct = ThingDefOf.WoodLog;
            ThingDef noProductionProduct = ThingDefOf.Plasteel;
            ThingDef suspendedProduct = ThingDefOf.Silver;

            if (contracts == null || buckets == null || committedProduct == null ||
                secondProduct == null || noProductionProduct == null || suspendedProduct == null)
            {
                string reason = contracts == null
                    ? "the world recurring-contract collection is unavailable"
                    : buckets == null
                        ? "the world production ledger is unavailable"
                        : "vanilla Steel, WoodLog, Plasteel, or Silver ThingDef is unavailable";
                r.Skip(committedAssertion, reason);
                r.Skip(completedAssertion, reason);
                r.Skip(noProductionAssertion, reason);
                r.Skip(suspendedAssertion, reason);
                return;
            }

            List<RecurringContract> savedContracts = new List<RecurringContract>(contracts);
            List<ProductionBucket> savedBuckets = new List<ProductionBucket>(buckets);

            const int committedQuantity = 17;
            const int committedCadenceDays = 2;
            // Independent fixture arithmetic: 17 units / 2 days = 8.5 per day.
            const float expectedCommittedPerDay = committedQuantity / (float)committedCadenceDays;

            const int firstRecordedQuantity = 5;
            const int secondRecordedQuantity = 10;
            // Independent fixture arithmetic: 5 / 5 days = 1, and 10 / 5 days = 2.
            const float expectedFirstCompletedPerDay = firstRecordedQuantity / 5f;
            const float expectedSecondCompletedPerDay = secondRecordedQuantity / 5f;

            const int noProductionQuantity = 9;
            const int noProductionCadenceDays = 3;
            const int suspendedQuantity = 8;
            const int suspendedCadenceDays = 4;
            // Independent fixture arithmetic: 8 units / 4 days = 2 per day.
            const float expectedSuspendedPerDay = suspendedQuantity / (float)suspendedCadenceDays;

            List<RecurringContract> fixtures;
            try
            {
                fixtures = new List<RecurringContract>
                {
                    new RecurringContract
                    {
                        id = -862,
                        settlementName = "Testholme",
                        factionName = "Test Confederacy",
                        thingDef = committedProduct,
                        quantityPerCycle = committedQuantity,
                        cadenceTicks = committedCadenceDays * GenDate.TicksPerDay,
                        totalCycles = 7,
                        unitPrice = 3.5f,
                        status = ContractStatus.Active
                    },
                    new RecurringContract
                    {
                        id = -863,
                        settlementName = "Testholme",
                        factionName = "Test Confederacy",
                        thingDef = secondProduct,
                        quantityPerCycle = 6,
                        cadenceTicks = 3 * GenDate.TicksPerDay,
                        totalCycles = 5,
                        unitPrice = 2.25f,
                        status = ContractStatus.Active
                    },
                    new RecurringContract
                    {
                        id = -864,
                        settlementName = "Testholme",
                        factionName = "Test Confederacy",
                        thingDef = noProductionProduct,
                        quantityPerCycle = noProductionQuantity,
                        cadenceTicks = noProductionCadenceDays * GenDate.TicksPerDay,
                        totalCycles = 5,
                        unitPrice = 4.75f,
                        status = ContractStatus.Active
                    },
                    new RecurringContract
                    {
                        id = -865,
                        settlementName = "Testholme",
                        factionName = "Test Confederacy",
                        thingDef = suspendedProduct,
                        quantityPerCycle = suspendedQuantity,
                        cadenceTicks = suspendedCadenceDays * GenDate.TicksPerDay,
                        totalCycles = 5,
                        unitPrice = 2.5f,
                        status = ContractStatus.Suspended
                    }
                };
            }
            catch (System.Exception ex)
            {
                string reason = $"could not build the production commitment fixture: {ex.Message}";
                r.Skip(committedAssertion, reason);
                r.Skip(completedAssertion, reason);
                r.Skip(noProductionAssertion, reason);
                r.Skip(suspendedAssertion, reason);
                return;
            }

            try
            {
                contracts.Clear();
                buckets.Clear();
                for (int i = 0; i < fixtures.Count; i++)
                {
                    state.AddContract(fixtures[i]);
                }

                ProductionLedgerService.Record(state, committedProduct, firstRecordedQuantity);
                ProductionLedgerService.Record(state, secondProduct, secondRecordedQuantity);

                List<BusinessReportService.ProductionCommitment> rows =
                    BusinessReportService.ActiveProductionCommitments(state);
                BusinessReportService.ProductionCommitment committedRow = rows.Find(
                    row => row.thingDef == committedProduct);
                BusinessReportService.ProductionCommitment firstCompletedRow = rows.Find(
                    row => row.thingDef == committedProduct);
                BusinessReportService.ProductionCommitment secondCompletedRow = rows.Find(
                    row => row.thingDef == secondProduct);
                BusinessReportService.ProductionCommitment noProductionRow = rows.Find(
                    row => row.thingDef == noProductionProduct);
                BusinessReportService.ProductionCommitment suspendedRow = rows.Find(
                    row => row.thingDef == suspendedProduct);

                r.Check(
                    committedRow != null &&
                    committedRow.committedPerDay == expectedCommittedPerDay,
                    committedAssertion,
                    $"reported {CommittedValue(committedRow)} per day; expected " +
                    $"{expectedCommittedPerDay:0.###} from {committedQuantity} units / " +
                    $"{committedCadenceDays} days");

                r.Check(
                    firstCompletedRow != null && secondCompletedRow != null &&
                    firstCompletedRow.completedPerDay == expectedFirstCompletedPerDay &&
                    secondCompletedRow.completedPerDay == expectedSecondCompletedPerDay,
                    completedAssertion,
                    $"{committedProduct.defName} {CompletedValue(firstCompletedRow)} per day " +
                    $"vs {expectedFirstCompletedPerDay:0.###}; " +
                    $"{secondProduct.defName} {CompletedValue(secondCompletedRow)} per day vs " +
                    $"{expectedSecondCompletedPerDay:0.###}");

                r.Check(
                    noProductionRow != null && !noProductionRow.hasRecordedProduction,
                    noProductionAssertion,
                    $"flag {(noProductionRow == null ? "<missing row>" :
                        noProductionRow.hasRecordedProduction.ToString())}; completed " +
                    $"{CompletedValue(noProductionRow)} per day");

                r.Check(
                    suspendedRow != null && suspendedRow.committedPerDay == expectedSuspendedPerDay,
                    suspendedAssertion,
                    $"reported {CommittedValue(suspendedRow)} per day; expected " +
                    $"{expectedSuspendedPerDay:0.###} from {suspendedQuantity} units / " +
                    $"{suspendedCadenceDays} days");
            }
            finally
            {
                contracts.Clear();
                contracts.AddRange(savedContracts);
                buckets.Clear();
                buckets.AddRange(savedBuckets);
                r.Info($"production commitment fixture restored {buckets.Count} bucket(s) and " +
                       $"{contracts.Count} agreement(s).");
            }
        }

        private static string CommittedValue(BusinessReportService.ProductionCommitment row)
        {
            return row == null ? "<missing row>" : row.committedPerDay.ToString("0.###");
        }

        private static string CompletedValue(BusinessReportService.ProductionCommitment row)
        {
            return row == null ? "<missing row>" : row.completedPerDay.ToString("0.###");
        }

        // --- Retention ---------------------------------------------------------------------

        private static void CheckPruning(Results r, IntercolonyWorldComponent state)
        {
            int start = state.Ledger.Count;

            state.Ledger.Add(Aged(LedgerKind.SalePayment, 100, 10));
            state.Ledger.Add(Aged(LedgerKind.SalePayment, 100, LedgerService.RetentionDays + 20));
            state.Ledger.Add(Aged(LedgerKind.SalePayment, 100, LedgerService.RetentionDays + 90));

            int removed = LedgerService.Prune(state);

            r.Check(removed == 2,
                "pruning drops entries past the retention window and keeps the rest (§75)",
                $"{removed} removed, retention {LedgerService.RetentionDays} days");

            r.Check(state.Ledger.Count == start + 1,
                "the recent entry survives", $"{state.Ledger.Count - start} left");

            r.Check(LedgerService.RetentionDays >= BusinessReportService.YearDays,
                "retention covers every window the dashboard can ask for",
                $"{LedgerService.RetentionDays}d retained, longest view {BusinessReportService.YearDays}d");

            while (state.Ledger.Count > start)
            {
                state.Ledger.RemoveAt(state.Ledger.Count - 1);
            }
        }

        // --- Helpers -----------------------------------------------------------------------

        private static LedgerEntry Aged(LedgerKind kind, int amount, int daysAgo)
        {
            return new LedgerEntry(kind, amount, "Testholme", "probe")
            {
                tick = GenTicks.TicksGame - daysAgo * GenDate.TicksPerDay
            };
        }

        private static string Summarize(Results r)
        {
            r.sb.AppendLine();
            r.sb.AppendLine($"  {r.passed} passed, {r.failed} failed" +
                            (r.skipped == 0 ? "." : $", {r.skipped} skipped."));
            return r.sb.ToString();
        }
    }
}
