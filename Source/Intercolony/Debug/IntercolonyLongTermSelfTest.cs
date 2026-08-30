using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// End-to-end check of Phase 22 (DESIGN.md §115, §36.3, §36.4, §43).
    ///
    /// §115 asks for two things. *"Employees can remain for long periods without faction-state drift
    /// or save corruption"* is mostly a claim about play over hundreds of days, which no self-test
    /// can settle — what it can do is prove the arithmetic that governs those long engagements does
    /// not break, which is where the danger actually is.
    ///
    /// The sharpest of those is the one Phase 20 left as a dated warning: a drafted civilian stopped
    /// being the expensive option past a 99-day engagement, and §36.4's open-ended employment has no
    /// term at all. That crossover is now located rather than assumed, at every tenure out to five
    /// in-game years.
    ///
    /// *"Neither employment nor supply agreements end by silently lapsing"* is the second criterion,
    /// and it is checked as a property: every ending path must leave a stated reason behind.
    /// </summary>
    public static class IntercolonyLongTermSelfTest
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

            public void Skip(string label, string detail)
            {
                skipped++;
                sb.AppendLine($"  SKIPPED  {label} — {detail}");
            }
        }

        private sealed class ProcurementContractDiagnosticSnapshot
        {
            public ProcurementContract contract;
            public ProcurementContractStatus status;
            public float unitPrice;
            public int quantityPerCycle;
            public int cyclesCompleted;
            public int cyclesFailed;
            public int totalCycles;
            public int activeOrderId;
            public int nextCycleTick;
            public int nextCycleTickOffset;
            public bool autoReadyWaitNotified;
            public string outcomeNote;
        }

        private sealed class ProcurementDiagnosticSnapshot
        {
            public ProcurementContractDiagnosticSnapshot target;
            public int silverCount;
            public bool canPayForPurchase;
            public string paymentReason;
            public int contractCount;
            public int activeContractCount;
            public readonly List<ProcurementContractDiagnosticSnapshot> contracts =
                new List<ProcurementContractDiagnosticSnapshot>();
        }

        public static string Run(IntercolonyWorldComponent state, Map map)
        {
            Results r = new Results();
            r.sb.AppendLine("Long-term employment self-test (§115, §36.3, §36.4, §43)");

            if (state == null || map == null)
            {
                r.sb.AppendLine("  No world or map. Open a colony first.");
                return Summarize(r);
            }

            EmployerReputation rep = state.EmployerStanding;
            float savedScore = rep?.Score ?? 0f;
            int savedRenewals = rep?.renewals ?? 0;
            int savedSkipped = rep?.noticesSkipped ?? 0;

            try
            {
                CheckShieldNeverCheaperAtAnyTenure(r);
                CheckSeveranceShape(r);
                CheckOpenEndedContract(r);
                CheckNoticeRules(r);
                CheckRenewalGating(r, state);
                CheckAutoRenewal(r, state);
                CheckSupplyAutoReady(r, state, map);
                CheckProcurementWaitForSilver(r, state, map);
                CheckNothingLapsesSilently(r);
            }
            catch (System.Exception ex)
            {
                r.sb.AppendLine($"  EXCEPTION: {ex}");
                r.failed++;
            }
            finally
            {
                if (rep != null)
                {
                    rep.Adjust(savedScore - rep.Score);
                    rep.renewals = savedRenewals;
                    rep.noticesSkipped = savedSkipped;
                }

                r.Info($"restored employer standing to {rep?.ScoreDisplay ?? 0}/100.");
            }

            return Summarize(r);
        }

        // --- The balance Phase 20 dated ----------------------------------------------------

        /// <summary>
        /// §113's acceptance criterion, re-proved for the term lengths this phase makes possible.
        ///
        /// Phase 20 found that the shield economics hold to 99 days and then invert, because
        /// compensation was a fixed number of days' wage while both wage bills grow with the term.
        /// It recorded that as a constraint on any phase raising the cap. §36.4 does not raise the
        /// cap — it removes it — so the fix had to be structural: severance that accrues per day
        /// served, six times faster for a civilian than a security contractor.
        ///
        /// Walked out to five in-game years, well past any engagement a player will actually run.
        /// </summary>
        private static void CheckShieldNeverCheaperAtAnyTenure(Results r)
        {
            const int baseWage = 40;
            int contractorWage = Mathf.RoundToInt(baseWage * CombatClause.Security.WageFactor());

            int worstDay = -1;
            float worstRatio = float.MaxValue;

            for (int days = 5; days <= 300; days += 5)
            {
                EmploymentContract shield = Synthetic(CombatClause.Civilian, baseWage, days);
                shield.clauseBreaches = CombatUseMonitor.BreachesBeforeRefusingWork;
                int shieldCost = baseWage * days + CompensationService.DeathCompensation(shield);

                EmploymentContract contractor = Synthetic(CombatClause.Security, contractorWage, days);
                int contractorCost = contractorWage * days +
                                     CompensationService.DeathCompensation(contractor);

                float ratio = shieldCost / (float)contractorCost;
                if (ratio < worstRatio)
                {
                    worstRatio = ratio;
                    worstDay = days;
                }
            }

            r.Check(worstRatio > 1f,
                "a drafted civilian is dearer than a fighter at every tenure out to 5 years (§113, §115)",
                $"tightest at {worstDay}d, x{worstRatio:0.00}");

            // The specific figure Phase 20 recorded, now expected to be gone entirely.
            EmploymentContract atOldCrossover = Synthetic(CombatClause.Civilian, baseWage, 100);
            atOldCrossover.clauseBreaches = CombatUseMonitor.BreachesBeforeRefusingWork;
            int shieldAt100 = baseWage * 100 + CompensationService.DeathCompensation(atOldCrossover);
            int contractorAt100 = contractorWage * 100 +
                                  CompensationService.DeathCompensation(
                                      Synthetic(CombatClause.Security, contractorWage, 100));

            r.Check(shieldAt100 > contractorAt100,
                "the 99-day crossover Phase 20 recorded no longer exists",
                $"at 100 days: shield {shieldAt100} vs contractor {contractorAt100}");
        }

        private static void CheckSeveranceShape(Results r)
        {
            EmploymentContract fresh = Synthetic(CombatClause.Civilian, 40, 0);
            EmploymentContract veteran = Synthetic(CombatClause.Civilian, 40, 180);

            int freshOwed = CompensationService.DeathCompensation(fresh);
            int veteranOwed = CompensationService.DeathCompensation(veteran);

            r.Check(veteranOwed > freshOwed,
                "a long-serving worker's death costs more than a new arrival's (§43, §115)",
                $"{freshOwed} at day 0 vs {veteranOwed} at day 180");

            // §43's worked example must still hold on day one, or Phase 20's anchor has drifted.
            r.Check(freshOwed == 2400,
                "day-one compensation still reproduces §43's worked example",
                $"{freshOwed} silver");

            r.Check(CompensationService.SeveranceDaysPerDayServed(CombatClause.Civilian) >
                    CompensationService.SeveranceDaysPerDayServed(CombatClause.Security),
                "a civilian accrues severance faster than a security contractor (§42)");

            bool monotonic = true;
            int previous = 0;
            for (int days = 0; days <= 400; days += 10)
            {
                int owed = CompensationService.DeathCompensation(
                    Synthetic(CombatClause.Civilian, 40, days));
                if (owed < previous)
                {
                    monotonic = false;
                }

                previous = owed;
            }

            r.Check(monotonic, "compensation never falls as tenure grows");
        }

        // --- §36.4 open-ended --------------------------------------------------------------

        private static void CheckOpenEndedContract(Results r)
        {
            // Set up the way Arrive leaves an open-ended contract: no term and no expiry tick.
            EmploymentContract open = Synthetic(CombatClause.Civilian, 40, 30);
            open.termDays = 0;
            open.endTick = -1;

            r.Check(open.IsOpenEnded, "a zero term means open-ended (§36.4)");

            // The trap this guards: DaysRemaining used to fall back to termDays, so an open-ended
            // contract would have read as "0 days left" and been ended on the next beat.
            r.Check(open.DaysRemaining > 1000f,
                "an open-ended contract never reads as nearly over",
                $"DaysRemaining {open.DaysRemaining:0}");

            r.Check(open.endTick < 0, "an open-ended contract has no expiry tick");
            r.Check(!open.ServingNotice, "a new open-ended contract is not under notice");

            EmploymentContract fixedTerm = Synthetic(CombatClause.Civilian, 40, 30);
            r.Check(!fixedTerm.IsOpenEnded, "a positive term is not open-ended");
        }

        private static void CheckNoticeRules(Results r)
        {
            EmploymentContract fresh = Synthetic(CombatClause.Civilian, 40, 5);
            fresh.termDays = 0;

            EmploymentContract veteran = Synthetic(CombatClause.Civilian, 40, 180);
            veteran.termDays = 0;

            int freshNotice = RenewalService.NoticeDays(fresh);
            int veteranNotice = RenewalService.NoticeDays(veteran);

            r.Check(veteranNotice > freshNotice,
                "notice grows with service (§36.4)",
                $"{freshNotice}d after 5 days served, {veteranNotice}d after 180");

            r.Check(freshNotice >= 3,
                "even a brand-new open-ended worker is owed some notice",
                $"{freshNotice} days");

            r.Check(RenewalService.PayInLieu(veteran) == veteranNotice * veteran.dailyWage,
                "paying in lieu costs exactly the notice it replaces (§36.4)",
                $"{RenewalService.PayInLieu(veteran)} silver");

            // A fixed-term contract owes no notice — its end date was the notice.
            EmploymentContract fixedTerm = Synthetic(CombatClause.Civilian, 40, 100);
            r.Check(RenewalService.NoticeDays(fixedTerm) == 0,
                "a fixed-term contract owes no notice — the end date was the notice");
        }

        // --- §115 renewal ------------------------------------------------------------------

        /// <summary>
        /// The design claim: renewal is earned rather than bought.
        ///
        /// Each refusal reason is driven separately, because "no offer came" is only meaningful if
        /// the player can tell which of their own choices caused it.
        /// </summary>
        private static void CheckRenewalGating(Results r, IntercolonyWorldComponent state)
        {
            EmploymentContract good = Synthetic(CombatClause.Civilian, 40, 30);
            good.status = EmploymentStatus.Active;

            r.Check(RenewalService.WouldRenew(state, good, out _),
                "a well-treated worker offers to stay (§115, §40)");

            EmploymentContract owed = Synthetic(CombatClause.Civilian, 40, 30);
            owed.arrearsSilver = 200;
            r.Check(!RenewalService.WouldRenew(state, owed, out string owedWhy),
                "a worker still owed wages does not offer to stay",
                Trim(owedWhy));

            EmploymentContract late = Synthetic(CombatClause.Civilian, 40, 30);
            late.missedPayments = 1;
            r.Check(!RenewalService.WouldRenew(state, late, out string lateWhy),
                "a worker paid late does not offer to stay",
                Trim(lateWhy));

            EmploymentContract drafted = Synthetic(CombatClause.Civilian, 40, 30);
            drafted.clauseBreaches = 1;
            r.Check(!RenewalService.WouldRenew(state, drafted, out string draftedWhy),
                "a worker drafted against their clause does not offer to stay (§42)",
                Trim(draftedWhy));

            EmploymentContract brandNew = Synthetic(CombatClause.Civilian, 40, 1);
            r.Check(!RenewalService.WouldRenew(state, brandNew, out _),
                "a worker who barely arrived is not asked to re-sign");

            // Every refusal must carry a reason. §115 forbids silent endings, and a blank string
            // here would produce a letter that says nothing.
            r.Check(!owedWhy.NullOrEmpty() && !lateWhy.NullOrEmpty() && !draftedWhy.NullOrEmpty(),
                "every refusal to renew states a reason (§115)");

            r.Check(RenewalService.RenewalWage(good) > good.dailyWage,
                "a returning worker costs more than they did",
                $"{good.dailyWage} -> {RenewalService.RenewalWage(good)}");

            // Accepting must not touch the pawn — that is what keeps §115's "no faction-state
            // drift" true across a long chain of renewals.
            int endBefore = good.endTick;
            good.renewalOffered = true;
            good.renewalWage = RenewalService.RenewalWage(good);
            bool accepted = RenewalService.Accept(good, out string failReason);

            r.Check(accepted, "a renewal can be accepted", failReason ?? "");
            r.Check(good.status == EmploymentStatus.Active && good.renewals == 1,
                "accepting extends the same employment rather than starting a new one",
                $"status {good.status}, renewals {good.renewals}");
            r.Check(good.endTick > endBefore, "the term is extended");
            r.Check(!good.renewalOffered,
                "the offer is re-armed for next term rather than left standing");
            r.Check(!RenewalService.Accept(good, out _),
                "a renewal cannot be accepted twice");
        }

        private static void CheckAutoRenewal(Results r, IntercolonyWorldComponent state)
        {
            List<Letter> existingLetters = SnapshotLetters();
            List<IArchivable> existingArchivables = SnapshotArchivables();

            try
            {
                EmploymentContract good = Synthetic(CombatClause.Civilian, 40, 30);
                good.autoRenew = true;
                bool wouldRenew = RenewalService.WouldRenew(
                    state, good, out string goodWhy);
                int oldEndTick = good.endTick;
                int oldWage = good.dailyWage;

                RenewalService.Advance(good);
                RenewalService.AdvanceAutoRenew(good);

                r.Check(
                    wouldRenew && good.renewals == 1 && !good.renewalOffered,
                    "auto-renew accepts a live offer",
                    $"eligible={wouldRenew}, renewals={good.renewals}, " +
                    $"offer={good.renewalOffered}, reason={Trim(goodWhy)}");
                r.Check(
                    good.endTick > oldEndTick && good.dailyWage > oldWage,
                    "the renewed term restarts and the wage rises",
                    $"end {oldEndTick}->{good.endTick}, wage {oldWage}->{good.dailyWage}");

                EmploymentContract off = Synthetic(CombatClause.Civilian, 40, 30);
                off.autoRenew = false;
                RenewalService.Advance(off);
                RenewalService.AdvanceAutoRenew(off);
                r.Check(
                    RenewalService.HasLiveOffer(off) && off.renewals == 0,
                    "auto-renew off leaves the offer standing",
                    $"liveOffer={RenewalService.HasLiveOffer(off)}, renewals={off.renewals}");

                EmploymentContract refused = Synthetic(CombatClause.Civilian, 40, 30);
                refused.arrearsSilver = 200;
                refused.autoRenew = true;
                RenewalService.Advance(refused);
                RenewalService.AdvanceAutoRenew(refused);
                r.Check(
                    refused.renewals == 0 && refused.renewalDeclinedByWorker,
                    "auto-renew cannot overrule a worker who refuses",
                    $"renewals={refused.renewals}, declinedByWorker={refused.renewalDeclinedByWorker}");

                EmploymentContract open = Synthetic(CombatClause.Civilian, 40, 30);
                open.termDays = 0;
                open.endTick = -1;
                open.autoRenew = true;
                RenewalService.Advance(open);
                RenewalService.AdvanceAutoRenew(open);
                r.Check(
                    open.renewals == 0 && !open.renewalOffered,
                    "auto-renew does not touch an open-ended contract",
                    $"renewals={open.renewals}, offer={open.renewalOffered}");
            }
            finally
            {
                RemoveGeneratedLetters(existingLetters, existingArchivables);
            }
        }

        private static void CheckSupplyAutoReady(
            Results r, IntercolonyWorldComponent state, Map map)
        {
            const string ReadyAssertion = "auto-ready marks a ready cycle order ready";
            const string AutoReadyOffAssertion = "auto-ready off leaves the order alone";
            const string MissingGoodsAssertion = "auto-ready refuses when the goods are not there";
            const string FailureThrottleAssertion = "the failure letter is sent once, not every pass";
            const string SellerDeliveryAssertion =
                "a seller-delivery cycle order is never auto-readied";

            Map fulfillmentMap = map?.IsPlayerHome == true ? map : Find.AnyPlayerHomeMap;
            ThingDef probeDef = FindAutoReadyProbeDef(state, fulfillmentMap);
            List<RecurringContract> testContracts = new List<RecurringContract>();
            List<SalesOrder> testOrders = new List<SalesOrder>();
            List<Zone_Stockpile> testZones = new List<Zone_Stockpile>();
            List<Thing> testThings = new List<Thing>();
            List<RecurringContract> existingAutoReady = new List<RecurringContract>();
            List<Letter> existingLetters = SnapshotLetters();
            List<IArchivable> existingArchivables = SnapshotArchivables();

            foreach (RecurringContract existing in state.Contracts)
            {
                if (existing != null && existing.autoReadyOrders)
                {
                    existingAutoReady.Add(existing);
                    existing.autoReadyOrders = false;
                }
            }

            try
            {
                if (probeDef == null || fulfillmentMap == null)
                {
                    string missingFixture = fulfillmentMap == null
                        ? "no player-home fulfillment map"
                        : "no isolated valid tradable item definition";
                    r.Skip(ReadyAssertion, missingFixture);
                    r.Skip(AutoReadyOffAssertion, missingFixture);
                    r.Skip(MissingGoodsAssertion, missingFixture);
                    r.Skip(FailureThrottleAssertion, missingFixture);
                    r.Skip(SellerDeliveryAssertion, missingFixture);
                    return;
                }

                string stockFailure = null;
                bool storedStock = TrySpawnStoredStock(
                    fulfillmentMap, probeDef, testZones, testThings, out stockFailure);

                if (!storedStock)
                {
                    r.Skip(
                        ReadyAssertion,
                        stockFailure ?? "no isolated tradable item and player-home map");
                    r.Skip(
                        AutoReadyOffAssertion,
                        "the shared real-stock fixture could not be placed");
                }
                else
                {
                    RecurringContract readyContract = AddAutoReadyFixture(
                        state, fulfillmentMap, probeDef, autoReadyOrders: true,
                        FulfillmentMode.BuyerPickup, -89101, -89102,
                        testContracts, testOrders, out SalesOrder readyOrder);
                    int readied = ContractService.AdvanceAutoReady(state);
                    r.Check(
                        readied == 1 && readyOrder.status == SalesOrderStatus.AwaitingCollection,
                        ReadyAssertion,
                        $"readied={readied}, status={readyOrder.status}");
                    state.Contracts.Remove(readyContract);
                    state.Orders.Remove(readyOrder);

                    RecurringContract offContract = AddAutoReadyFixture(
                        state, fulfillmentMap, probeDef, autoReadyOrders: false,
                        FulfillmentMode.BuyerPickup, -89103, -89104,
                        testContracts, testOrders, out SalesOrder offOrder);
                    int readiedWithAutoReadyOff = ContractService.AdvanceAutoReady(state);
                    r.Check(
                        readiedWithAutoReadyOff == 0 && offOrder.status == SalesOrderStatus.Accepted,
                        AutoReadyOffAssertion,
                        $"readied={readiedWithAutoReadyOff}, status={offOrder.status}");
                    state.Contracts.Remove(offContract);
                    state.Orders.Remove(offOrder);
                }

                DestroyTestThings(testThings);

                RecurringContract absentContract = AddAutoReadyFixture(
                    state, fulfillmentMap, probeDef, autoReadyOrders: true,
                    FulfillmentMode.BuyerPickup, -89105, -89106,
                    testContracts, testOrders, out SalesOrder absentOrder);
                int absentReadied = ContractService.AdvanceAutoReady(state);
                r.Check(
                    absentReadied == 0 && absentOrder.status == SalesOrderStatus.Accepted &&
                    absentOrder.IsOpen && absentOrder.autoReadyFailureNotified,
                    MissingGoodsAssertion,
                    $"readied={absentReadied}, status={absentOrder.status}, " +
                    $"open={absentOrder.IsOpen}, notified={absentOrder.autoReadyFailureNotified}");
                state.Contracts.Remove(absentContract);
                state.Orders.Remove(absentOrder);

                RecurringContract throttledContract = AddAutoReadyFixture(
                    state, fulfillmentMap, probeDef, autoReadyOrders: true,
                    FulfillmentMode.BuyerPickup, -89107, -89108,
                    testContracts, testOrders, out SalesOrder throttledOrder);
                int firstFailurePass = ContractService.AdvanceAutoReady(state);
                bool notifiedAfterFirstPass = throttledOrder.autoReadyFailureNotified;
                int secondFailurePass = ContractService.AdvanceAutoReady(state);
                bool notifiedAfterSecondPass = throttledOrder.autoReadyFailureNotified;
                r.Check(
                    firstFailurePass == 0 && secondFailurePass == 0 &&
                    notifiedAfterFirstPass && notifiedAfterSecondPass,
                    FailureThrottleAssertion,
                    $"passes={firstFailurePass}/{secondFailurePass}, " +
                    $"notified={notifiedAfterFirstPass}/{notifiedAfterSecondPass}");
                state.Contracts.Remove(throttledContract);
                state.Orders.Remove(throttledOrder);

                RecurringContract sellerDeliveryContract = AddAutoReadyFixture(
                    state, fulfillmentMap, probeDef, autoReadyOrders: true,
                    FulfillmentMode.SellerDelivery, -89109, -89110,
                    testContracts, testOrders, out SalesOrder sellerDeliveryOrder);
                int sellerDeliveryReadied = ContractService.AdvanceAutoReady(state);
                r.Check(
                    sellerDeliveryReadied == 0 &&
                    sellerDeliveryOrder.status == SalesOrderStatus.Accepted &&
                    !sellerDeliveryOrder.CanMarkReady,
                    SellerDeliveryAssertion,
                    $"readied={sellerDeliveryReadied}, status={sellerDeliveryOrder.status}, " +
                    $"canMarkReady={sellerDeliveryOrder.CanMarkReady}");
                state.Contracts.Remove(sellerDeliveryContract);
                state.Orders.Remove(sellerDeliveryOrder);
            }
            finally
            {
                foreach (RecurringContract contract in testContracts)
                {
                    state.Contracts.Remove(contract);
                }

                foreach (SalesOrder order in testOrders)
                {
                    state.Orders.Remove(order);
                }

                DestroyTestThings(testThings);
                foreach (Zone_Stockpile zone in testZones)
                {
                    zone?.Delete(playSound: false);
                }

                foreach (RecurringContract existing in existingAutoReady)
                {
                    existing.autoReadyOrders = true;
                }

                RemoveGeneratedLetters(existingLetters, existingArchivables);
            }
        }

        private static void CheckProcurementWaitForSilver(
            Results r, IntercolonyWorldComponent state, Map map)
        {
            const string FundableAssertion = "a fundable procurement cycle creates its order";
            const string WaitAssertion = "an unaffordable cycle waits instead of failing";
            const string ToggleOffAssertion =
                "with the toggle off an unaffordable cycle still fails immediately";
            const string DeadlineAssertion = "waiting ends when the grace window closes";
            const string NoticeAssertion = "the wait notice is sent once, not every refresh";
            const string SilverOnlyAssertion = "only silver is waited for";

            void SkipAll(string reason)
            {
                r.Skip(FundableAssertion, reason);
                r.Skip(WaitAssertion, reason);
                r.Skip(ToggleOffAssertion, reason);
                r.Skip(DeadlineAssertion, reason);
                r.Skip(NoticeAssertion, reason);
                r.Skip(SilverOnlyAssertion, reason);
            }

            Map paymentMap = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
            List<ProcurementContract> savedContracts = state?.ProcurementContracts == null
                ? null
                : new List<ProcurementContract>(state.ProcurementContracts);
            List<PurchaseOrder> savedOrders = state?.PurchaseOrders == null
                ? null
                : new List<PurchaseOrder>(state.PurchaseOrders);
            List<LedgerEntry> savedLedger = state?.Ledger == null
                ? null
                : new List<LedgerEntry>(state.Ledger);
            int savedLedgerStartTick = state?.LedgerStartTick ?? LedgerService.NoHistory;

            System.Reflection.FieldInfo consumptionField = typeof(IntercolonyWorldComponent)
                .GetField(
                    "supplierOfferConsumption",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
            System.Reflection.FieldInfo profileCacheField = typeof(IntercolonyWorldComponent)
                .GetField(
                    "profileCache",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
            System.Reflection.FieldInfo economySeedField = typeof(IntercolonyWorldComponent)
                .GetField(
                    "economySeed",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
            List<SupplierOfferConsumption> liveConsumption = consumptionField?.GetValue(state)
                as List<SupplierOfferConsumption>;
            Dictionary<int, SettlementEconomicProfile> liveProfileCache = profileCacheField?
                .GetValue(state) as Dictionary<int, SettlementEconomicProfile>;
            List<SupplierOfferConsumption> savedConsumption = liveConsumption == null
                ? null
                : new List<SupplierOfferConsumption>(liveConsumption);
            Dictionary<int, SettlementEconomicProfile> savedProfileCache = liveProfileCache == null
                ? null
                : new Dictionary<int, SettlementEconomicProfile>(liveProfileCache);
            int savedEconomySeed = economySeedField == null
                ? 0
                : (int)economySeedField.GetValue(state);
            int savedSilver = paymentMap == null || ThingDefOf.Silver == null
                ? 0
                : PurchaseOrderService.CountColonySilver(paymentMap);
            List<IntVec3> savedSilverCells = new List<IntVec3>();
            if (paymentMap != null && ThingDefOf.Silver != null)
            {
                foreach (Thing silver in paymentMap.listerThings.ThingsOfDef(ThingDefOf.Silver))
                {
                    if (silver != null && !silver.Destroyed && silver.IsInAnyStorage())
                    {
                        savedSilverCells.Add(silver.Position);
                    }
                }
            }

            List<Zone_Stockpile> silverZones = new List<Zone_Stockpile>();
            List<Letter> existingLetters = SnapshotLetters();
            List<IArchivable> existingArchivables = SnapshotArchivables();

            try
            {
                if (paymentMap == null || ThingDefOf.Silver == null)
                {
                    SkipAll("no player-home payment map or silver definition");
                    return;
                }

                if (savedContracts == null || savedOrders == null || savedLedger == null ||
                    liveConsumption == null || liveProfileCache == null ||
                    consumptionField == null || profileCacheField == null ||
                    economySeedField == null)
                {
                    SkipAll("the live fields needed for complete procurement cleanup are inaccessible");
                    return;
                }

                if (savedSilver == int.MaxValue)
                {
                    SkipAll("stored silver count cannot be increased by one for the wait fixture");
                    return;
                }

                int fundableSilverTarget = savedSilver > 0 ? savedSilver : 1;
                if (!TrySetStoredSilver(
                        paymentMap, fundableSilverTarget, savedSilverCells, silverZones,
                        out string silverFailure) ||
                    PurchaseOrderService.CountColonySilver(paymentMap) != fundableSilverTarget)
                {
                    SkipAll(
                        silverFailure ??
                        "the payment map did not reach the stored-silver fixture amount");
                    return;
                }

                Settlement supplier = null;
                ThingDef product = null;
                List<Settlement> settlements = Find.WorldObjects?.Settlements;
                List<ThingDef> tradableDefs = IntercolonyProductClassifier.TradableDefs;
                if (settlements != null && tradableDefs != null)
                {
                    foreach (Settlement candidateSettlement in settlements)
                    {
                        if (candidateSettlement == null ||
                            !IntercolonyMarketAccess.IsAccessible(candidateSettlement))
                        {
                            continue;
                        }

                        SettlementEconomicProfile profile =
                            state.GetProfileForReadOnly(candidateSettlement);
                        if (profile == null)
                        {
                            continue;
                        }

                        foreach (ThingDef candidateDef in tradableDefs)
                        {
                            if (candidateDef == null || candidateDef == ThingDefOf.Silver ||
                                candidateDef.category != ThingCategory.Item ||
                                candidateDef.stackLimit < 1 || candidateDef.MadeFromStuff ||
                                (candidateDef.techLevel != TechLevel.Undefined &&
                                 candidateDef.techLevel > profile.techTier))
                            {
                                continue;
                            }

                            if (!IntercolonyProductClassifier.TryGetTradableCategory(
                                    candidateDef, out IntercolonyProductCategory category))
                            {
                                continue;
                            }

                            float effectiveSupply = EffectiveEconomyService.EffectiveSupply(
                                state, profile, category);
                            if (!RfqService.CanTechnicallySupply(candidateDef, profile) ||
                                effectiveSupply <= 0f ||
                                RfqService.SupplierOfferQuantity(
                                    candidateDef, null, profile, effectiveSupply) < 1)
                            {
                                continue;
                            }

                            supplier = candidateSettlement;
                            product = candidateDef;
                            break;
                        }

                        if (supplier != null)
                        {
                            break;
                        }
                    }
                }

                if (supplier == null || product == null)
                {
                    SkipAll("no accessible supplier has positive current capacity for a tradable item");
                    return;
                }

                state.ProcurementContracts.Clear();
                state.PurchaseOrders.Clear();
                state.Ledger.Clear();
                liveConsumption.Clear();

                ProcurementContract AddFixture(
                    int id, bool autoReadyOrders, int quantity, float unitPrice, int nextCycleTick)
                {
                    ProcurementContract contract = new ProcurementContract
                    {
                        id = id,
                        settlementId = supplier.ID,
                        settlementName = supplier.Label ?? "Procurement self-test supplier",
                        thingDef = product,
                        quantityPerCycle = quantity,
                        unitPrice = unitPrice,
                        cadenceDays = 1,
                        totalCycles = 2,
                        status = ProcurementContractStatus.Active,
                        activeOrderId = ProcurementContract.NoActiveOrderId,
                        nextCycleTick = nextCycleTick,
                        autoReadyOrders = autoReadyOrders
                    };
                    state.AddProcurementContract(contract);
                    return contract;
                }

                int now = GenTicks.TicksGame;
                ProcurementContract fundable = AddFixture(
                    -89201, autoReadyOrders: true, quantity: 1, unitPrice: 1f, nextCycleTick: now);
                int fundableFailuresBefore = fundable.cyclesFailed;
                ProcurementDiagnosticSnapshot fundableBefore = CaptureProcurementDiagnostics(
                    state, paymentMap, fundable);
                ProcurementContractService.AdvanceCycles(state);
                ProcurementDiagnosticSnapshot fundableAfter = CaptureProcurementDiagnostics(
                    state, paymentMap, fundable);
                bool fundableOrderCreated =
                    fundable.activeOrderId != ProcurementContract.NoActiveOrderId;
                bool fundableFailuresUnchanged = fundable.cyclesFailed == fundableFailuresBefore;
                bool fundablePassed = fundableOrderCreated && fundableFailuresUnchanged;
                r.Check(
                    fundablePassed,
                    FundableAssertion,
                    fundablePassed
                        ? $"orderCreated={fundableOrderCreated}, " +
                          $"failuresUnchanged={fundableFailuresUnchanged}"
                        : $"orderCreated={fundableOrderCreated}, " +
                          $"failuresUnchanged={fundableFailuresUnchanged}, " +
                          BuildProcurementDiagnosticDetail(
                              "fundable", fundableBefore, fundableAfter));
                state.ProcurementContracts.Remove(fundable);

                int silverBeforeWait = PurchaseOrderService.CountColonySilver(paymentMap);
                if (silverBeforeWait == int.MaxValue)
                {
                    r.Skip(
                        WaitAssertion,
                        "stored silver count cannot be increased by one for the wait price");
                    r.Skip(
                        ToggleOffAssertion,
                        "stored silver count cannot be increased by one for the wait price");
                    r.Skip(
                        DeadlineAssertion,
                        "stored silver count cannot be increased by one for the wait price");
                    r.Skip(
                        NoticeAssertion,
                        "stored silver count cannot be increased by one for the wait price");
                    r.Skip(
                        SilverOnlyAssertion,
                        "stored silver count cannot be increased by one for the wait price");
                    return;
                }

                int waitingPrice = silverBeforeWait + 1;
                ProcurementContract waiting = AddFixture(
                    -89202, autoReadyOrders: true, quantity: 1,
                    unitPrice: waitingPrice, nextCycleTick: now);
                int waitingFailuresBefore = waiting.cyclesFailed;
                int waitingDueTick = waiting.nextCycleTick;
                ProcurementContractService.AdvanceCycles(state);
                bool waitingFailuresUnchanged = waiting.cyclesFailed == waitingFailuresBefore;
                bool waitingHasNoOrder =
                    waiting.activeOrderId == ProcurementContract.NoActiveOrderId;
                bool waitingDueUnchanged = waiting.nextCycleTick == waitingDueTick;
                r.Check(
                    waitingFailuresUnchanged && waitingHasNoOrder && waitingDueUnchanged,
                    WaitAssertion,
                    $"failuresUnchanged={waitingFailuresUnchanged}, " +
                    $"noOrder={waitingHasNoOrder}, dueUnchanged={waitingDueUnchanged}");
                state.ProcurementContracts.Remove(waiting);

                ProcurementContract toggleOff = AddFixture(
                    -89203, autoReadyOrders: false, quantity: 1,
                    unitPrice: waitingPrice, nextCycleTick: now);
                int toggleOffFailuresBefore = toggleOff.cyclesFailed;
                int toggleOffDueTick = toggleOff.nextCycleTick;
                ProcurementContractService.AdvanceCycles(state);
                bool toggleOffFailed = toggleOff.cyclesFailed > toggleOffFailuresBefore;
                bool toggleOffAdvancedOneCadence = toggleOff.nextCycleTick ==
                    toggleOffDueTick + toggleOff.cadenceDays * GenDate.TicksPerDay;
                r.Check(
                    toggleOffFailed && toggleOffAdvancedOneCadence,
                    ToggleOffAssertion,
                    $"failuresIncreased={toggleOffFailed}, " +
                    $"advancedOneCadence={toggleOffAdvancedOneCadence}");
                state.ProcurementContracts.Remove(toggleOff);

                ProcurementContract deadline = AddFixture(
                    -89204, autoReadyOrders: true, quantity: 1,
                    unitPrice: waitingPrice,
                    nextCycleTick: now - GenDate.TicksPerDay);
                int deadlineFailuresBefore = deadline.cyclesFailed;
                int deadlineDueTick = deadline.nextCycleTick;
                ProcurementContractService.AdvanceCycles(state);
                bool deadlineFailed = deadline.cyclesFailed > deadlineFailuresBefore;
                bool deadlineAdvancedOneCadence = deadline.nextCycleTick ==
                    deadlineDueTick + deadline.cadenceDays * GenDate.TicksPerDay;
                r.Check(
                    deadlineFailed && deadlineAdvancedOneCadence,
                    DeadlineAssertion,
                    $"failuresIncreased={deadlineFailed}, " +
                    $"advancedOneCadence={deadlineAdvancedOneCadence}");
                state.ProcurementContracts.Remove(deadline);

                ProcurementContract notice = AddFixture(
                    -89205, autoReadyOrders: true, quantity: 1,
                    unitPrice: waitingPrice, nextCycleTick: now);
                int noticeFailuresBefore = notice.cyclesFailed;
                int noticeDueTick = notice.nextCycleTick;
                ProcurementContractService.AdvanceCycles(state);
                bool notifiedAfterFirstPass = notice.autoReadyWaitNotified;
                ProcurementContractService.AdvanceCycles(state);
                bool notifiedAfterSecondPass = notice.autoReadyWaitNotified;
                bool noticeStillWaiting = notice.cyclesFailed == noticeFailuresBefore &&
                    notice.activeOrderId == ProcurementContract.NoActiveOrderId &&
                    notice.nextCycleTick == noticeDueTick;
                r.Check(
                    notifiedAfterFirstPass && notifiedAfterSecondPass && noticeStillWaiting,
                    NoticeAssertion,
                    $"notified={notifiedAfterFirstPass}/{notifiedAfterSecondPass}, " +
                    $"stillWaiting={noticeStillWaiting}");
                state.ProcurementContracts.Remove(notice);

                bool invalidSilverReady = PurchaseOrderService.CountColonySilver(paymentMap) > 0;
                string invalidSilverFailure = null;
                if (!invalidSilverReady)
                {
                    invalidSilverReady = TrySetStoredSilver(
                        paymentMap, 1, savedSilverCells, silverZones, out invalidSilverFailure);
                    invalidSilverReady = invalidSilverReady &&
                        PurchaseOrderService.CountColonySilver(paymentMap) > 0;
                }

                if (!invalidSilverReady)
                {
                    r.Skip(
                        SilverOnlyAssertion,
                        invalidSilverFailure ??
                        "the payment map could not reach at least one stored silver");
                }
                else
                {
                    ProcurementContract invalidTerms = AddFixture(
                        -89206, autoReadyOrders: true, quantity: 1,
                        unitPrice: 0f, nextCycleTick: now);
                    int invalidFailuresBefore = invalidTerms.cyclesFailed;
                    int invalidDueTick = invalidTerms.nextCycleTick;
                    ProcurementDiagnosticSnapshot invalidBefore = CaptureProcurementDiagnostics(
                        state, paymentMap, invalidTerms);
                    ProcurementContractService.AdvanceCycles(state);
                    ProcurementDiagnosticSnapshot invalidAfter = CaptureProcurementDiagnostics(
                        state, paymentMap, invalidTerms);
                    bool invalidFailed = invalidTerms.cyclesFailed > invalidFailuresBefore;
                    bool invalidAdvancedOneCadence = invalidTerms.nextCycleTick ==
                        invalidDueTick + invalidTerms.cadenceDays * GenDate.TicksPerDay;
                    bool invalidPassed = invalidFailed && invalidAdvancedOneCadence;
                    r.Check(
                        invalidPassed,
                        SilverOnlyAssertion,
                        invalidPassed
                            ? $"failuresIncreased={invalidFailed}, " +
                              $"advancedOneCadence={invalidAdvancedOneCadence}"
                            : $"failuresIncreased={invalidFailed}, " +
                              $"advancedOneCadence={invalidAdvancedOneCadence}, " +
                              BuildProcurementDiagnosticDetail(
                                  "invalidTerms", invalidBefore, invalidAfter));
                    state.ProcurementContracts.Remove(invalidTerms);
                }
            }
            finally
            {
                if (paymentMap != null && ThingDefOf.Silver != null &&
                    !TrySetStoredSilver(
                        paymentMap, savedSilver, savedSilverCells, silverZones,
                        out string restoreFailure))
                {
                    r.Info($"stored silver restoration failed: {restoreFailure}");
                }

                DeleteTestZones(silverZones);

                if (paymentMap != null && ThingDefOf.Silver != null &&
                    PurchaseOrderService.CountColonySilver(paymentMap) != savedSilver)
                {
                    r.Info("stored silver restoration did not reach the saved count.");
                }

                if (state?.ProcurementContracts != null && savedContracts != null)
                {
                    state.ProcurementContracts.Clear();
                    state.ProcurementContracts.AddRange(savedContracts);
                }

                if (state?.PurchaseOrders != null && savedOrders != null)
                {
                    state.PurchaseOrders.Clear();
                    state.PurchaseOrders.AddRange(savedOrders);
                }

                if (state?.Ledger != null && savedLedger != null)
                {
                    state.Ledger.Clear();
                    state.Ledger.AddRange(savedLedger);
                    state.LedgerStartTick = savedLedgerStartTick;
                }

                if (liveConsumption != null && savedConsumption != null)
                {
                    liveConsumption.Clear();
                    liveConsumption.AddRange(savedConsumption);
                }

                if (liveProfileCache != null && savedProfileCache != null)
                {
                    liveProfileCache.Clear();
                    foreach (KeyValuePair<int, SettlementEconomicProfile> entry in savedProfileCache)
                    {
                        liveProfileCache[entry.Key] = entry.Value;
                    }
                }

                if (economySeedField != null)
                {
                    economySeedField.SetValue(state, savedEconomySeed);
                }

                RemoveGeneratedLetters(existingLetters, existingArchivables);
                r.Info("procurement wait fixtures, orders, letters, and stored silver restored.");
            }
        }

        private static ProcurementDiagnosticSnapshot CaptureProcurementDiagnostics(
            IntercolonyWorldComponent state, Map paymentMap, ProcurementContract target)
        {
            int now = GenTicks.TicksGame;
            bool canPayForPurchase = PurchaseOrderService.CanPayForPurchase(
                paymentMap,
                target.unitPrice,
                target.quantityPerCycle,
                out string paymentReason);
            ProcurementDiagnosticSnapshot snapshot = new ProcurementDiagnosticSnapshot
            {
                target = CaptureProcurementContractSnapshot(target, now),
                silverCount = paymentMap == null
                    ? 0
                    : PurchaseOrderService.CountColonySilver(paymentMap),
                canPayForPurchase = canPayForPurchase,
                paymentReason = paymentReason,
                contractCount = state?.ProcurementContracts?.Count ?? 0
            };

            if (state?.ProcurementContracts != null)
            {
                foreach (ProcurementContract contract in state.ProcurementContracts)
                {
                    if (contract == null)
                    {
                        continue;
                    }

                    if (contract.status == ProcurementContractStatus.Active)
                    {
                        snapshot.activeContractCount++;
                    }

                    snapshot.contracts.Add(CaptureProcurementContractSnapshot(contract, now));
                }
            }

            return snapshot;
        }

        private static ProcurementContractDiagnosticSnapshot CaptureProcurementContractSnapshot(
            ProcurementContract contract, int now)
        {
            return new ProcurementContractDiagnosticSnapshot
            {
                contract = contract,
                status = contract.status,
                unitPrice = contract.unitPrice,
                quantityPerCycle = contract.quantityPerCycle,
                cyclesCompleted = contract.cyclesCompleted,
                cyclesFailed = contract.cyclesFailed,
                totalCycles = contract.totalCycles,
                activeOrderId = contract.activeOrderId,
                nextCycleTick = contract.nextCycleTick,
                nextCycleTickOffset = contract.nextCycleTick - now,
                autoReadyWaitNotified = contract.autoReadyWaitNotified,
                outcomeNote = contract.outcomeNote
            };
        }

        private static string BuildProcurementDiagnosticDetail(
            string targetName,
            ProcurementDiagnosticSnapshot before,
            ProcurementDiagnosticSnapshot after)
        {
            bool otherContractChanged = false;
            int firstOtherChangedId = 0;
            foreach (ProcurementContractDiagnosticSnapshot afterContract in after.contracts)
            {
                if (afterContract.contract == before.target.contract)
                {
                    continue;
                }

                ProcurementContractDiagnosticSnapshot beforeContract =
                    FindProcurementContractSnapshot(before.contracts, afterContract.contract);
                if (beforeContract != null &&
                    (beforeContract.cyclesFailed != afterContract.cyclesFailed ||
                     beforeContract.nextCycleTick != afterContract.nextCycleTick))
                {
                    otherContractChanged = true;
                    firstOtherChangedId = afterContract.contract.id;
                    break;
                }
            }

            string firstOtherChangedIdText = otherContractChanged
                ? firstOtherChangedId.ToString(CultureInfo.InvariantCulture)
                : "none";
            return BuildProcurementDiagnosticPhase("before", targetName, before) + ", " +
                   BuildProcurementDiagnosticPhase("after", targetName, after) + ", " +
                   $"otherContractChanged={otherContractChanged}, " +
                   $"firstOtherChangedId={firstOtherChangedIdText}";
        }

        private static ProcurementContractDiagnosticSnapshot FindProcurementContractSnapshot(
            List<ProcurementContractDiagnosticSnapshot> snapshots,
            ProcurementContract contract)
        {
            foreach (ProcurementContractDiagnosticSnapshot snapshot in snapshots)
            {
                if (snapshot.contract == contract)
                {
                    return snapshot;
                }
            }

            return null;
        }

        private static string BuildProcurementDiagnosticPhase(
            string phase, string targetName, ProcurementDiagnosticSnapshot snapshot)
        {
            ProcurementContractDiagnosticSnapshot target = snapshot.target;
            return
                $"{phase}.{targetName}.status={target.status}, " +
                $"{phase}.{targetName}.unitPrice={FormatDiagnosticFloat(target.unitPrice)}, " +
                $"{phase}.{targetName}.quantityPerCycle={target.quantityPerCycle}, " +
                $"{phase}.{targetName}.cyclesCompleted={target.cyclesCompleted}, " +
                $"{phase}.{targetName}.cyclesFailed={target.cyclesFailed}, " +
                $"{phase}.{targetName}.totalCycles={target.totalCycles}, " +
                $"{phase}.{targetName}.activeOrderId={target.activeOrderId}, " +
                $"{phase}.{targetName}.nextCycleTickOffset={target.nextCycleTickOffset}, " +
                $"{phase}.{targetName}.autoReadyWaitNotified={target.autoReadyWaitNotified}, " +
                $"{phase}.{targetName}.outcomeNote={DiagnosticText(target.outcomeNote)}, " +
                $"{phase}.silverCount={snapshot.silverCount}, " +
                $"{phase}.canPayForPurchase={snapshot.canPayForPurchase}, " +
                $"{phase}.paymentReason={DiagnosticText(snapshot.paymentReason)}, " +
                $"{phase}.procurementContractCount={snapshot.contractCount}, " +
                $"{phase}.activeProcurementContractCount={snapshot.activeContractCount}";
        }

        private static string FormatDiagnosticFloat(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string DiagnosticText(string value)
        {
            return value == null
                ? "<null>"
                : value.Replace("\r", "\\r")
                       .Replace("\n", "\\n")
                       .Replace(",", ";")
                       .Replace("=", ":");
        }

        private static bool TrySetStoredSilver(
            Map map, int target, List<IntVec3> preferredCells,
            List<Zone_Stockpile> testZones, out string failure)
        {
            failure = null;
            if (map == null || ThingDefOf.Silver == null || target < 0 ||
                preferredCells == null || testZones == null)
            {
                failure = "the payment map or stored-silver fixture inputs were unavailable";
                return false;
            }

            int current = PurchaseOrderService.CountColonySilver(map);
            if (current > target)
            {
                int amountToRemove = current - target;
                if (!PurchaseOrderService.TryTakeSilver(map, amountToRemove))
                {
                    failure = "the real silver-removal helper could not reduce stored silver";
                    return false;
                }

                current = PurchaseOrderService.CountColonySilver(map);
            }

            int missing = target - current;
            while (missing > 0)
            {
                IntVec3 storageCell = FindSilverStorageCell(map, preferredCells);
                if (!storageCell.IsValid)
                {
                    storageCell = FindEmptySilverStorageCell(map);
                    if (!storageCell.IsValid)
                    {
                        failure = "no valid storage cell was available for spawned silver";
                        return false;
                    }

                    Zone_Stockpile zone = new Zone_Stockpile(
                        StorageSettingsPreset.DefaultStockpile, map.zoneManager);
                    map.zoneManager.RegisterZone(zone);
                    testZones.Add(zone);
                    zone.AddCell(storageCell);
                }

                int before = PurchaseOrderService.CountColonySilver(map);
                int amount = missing > ThingDefOf.Silver.stackLimit
                    ? ThingDefOf.Silver.stackLimit
                    : missing;
                Thing silver = null;
                try
                {
                    silver = ThingMaker.MakeThing(ThingDefOf.Silver);
                    silver.stackCount = amount;
                    Thing spawned = GenSpawn.Spawn(silver, storageCell, map);
                    if (spawned == null || spawned.Destroyed || !spawned.IsInAnyStorage())
                    {
                        if (silver != null && !silver.Destroyed)
                        {
                            silver.Destroy(DestroyMode.Vanish);
                        }

                        failure =
                            "spawned silver was not genuinely available in colony storage";
                        return false;
                    }
                }
                catch (System.Exception ex)
                {
                    if (silver != null && !silver.Destroyed)
                    {
                        silver.Destroy(DestroyMode.Vanish);
                    }

                    failure = $"could not create stored silver: {ex.Message}";
                    return false;
                }

                int added = PurchaseOrderService.CountColonySilver(map) - before;
                if (added <= 0)
                {
                    failure = "stored silver did not increase after a real spawn";
                    return false;
                }

                missing -= added;
            }

            if (PurchaseOrderService.CountColonySilver(map) != target)
            {
                failure = "the payment map did not reach the requested stored-silver count";
                return false;
            }

            return true;
        }

        private static IntVec3 FindSilverStorageCell(
            Map map, List<IntVec3> preferredCells)
        {
            foreach (IntVec3 cell in preferredCells)
            {
                if (IsUsableSilverStorageCell(map, cell))
                {
                    return cell;
                }
            }

            return IntVec3.Invalid;
        }

        private static IntVec3 FindEmptySilverStorageCell(Map map)
        {
            if (map?.zoneManager == null)
            {
                return IntVec3.Invalid;
            }

            IntVec3 root = DropCellFinder.TradeDropSpot(map);
            foreach (IntVec3 candidate in GenRadial.RadialCellsAround(root, 12f, useCenter: true))
            {
                if (candidate.InBounds(map) && candidate.Standable(map) &&
                    candidate.GetFirstItem(map) == null &&
                    map.zoneManager.ZoneAt(candidate) == null)
                {
                    return candidate;
                }
            }

            return IntVec3.Invalid;
        }

        private static bool IsUsableSilverStorageCell(Map map, IntVec3 cell)
        {
            if (map?.zoneManager == null || !cell.InBounds(map) || !cell.Standable(map))
            {
                return false;
            }

            Thing probe = ThingMaker.MakeThing(ThingDefOf.Silver);
            if (probe == null)
            {
                return false;
            }

            return StoreUtility.IsGoodStoreCell(
                cell, map, probe, null, Faction.OfPlayer);
        }

        private static void DeleteTestZones(List<Zone_Stockpile> testZones)
        {
            if (testZones == null)
            {
                return;
            }

            foreach (Zone_Stockpile zone in testZones)
            {
                if (zone != null)
                {
                    zone.Delete(playSound: false);
                }
            }
        }

        private static ThingDef FindAutoReadyProbeDef(
            IntercolonyWorldComponent state, Map fulfillmentMap)
        {
            if (state == null || fulfillmentMap == null || Find.Maps == null)
            {
                return null;
            }

            foreach (ThingDef candidate in IntercolonyProductClassifier.TradableDefs)
            {
                if (candidate == null || candidate.category != ThingCategory.Item ||
                    candidate.stackLimit < 1 || candidate.MadeFromStuff)
                {
                    continue;
                }

                bool alreadyStocked = false;
                foreach (Map loadedMap in Find.Maps)
                {
                    foreach (KeyValuePair<ThingDef, int> entry in
                             FindBuyerService.ColonyStock(loadedMap))
                    {
                        if (entry.Key == candidate && entry.Value > 0)
                        {
                            alreadyStocked = true;
                            break;
                        }
                    }

                    if (alreadyStocked)
                    {
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
                    return candidate;
                }
            }

            return null;
        }

        private static bool TrySpawnStoredStock(
            Map map, ThingDef def, List<Zone_Stockpile> testZones,
            List<Thing> testThings, out string failure)
        {
            failure = null;
            if (map == null || def == null || map.zoneManager == null)
            {
                failure = "no player-home map, isolated tradable item, or zone manager";
                return false;
            }

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
                failure = "no empty unzoned cell near the trade drop spot";
                return false;
            }

            Zone_Stockpile zone = new Zone_Stockpile(
                StorageSettingsPreset.DefaultStockpile, map.zoneManager);
            map.zoneManager.RegisterZone(zone);
            testZones.Add(zone);
            zone.AddCell(storageCell);

            try
            {
                Thing stack = ThingMaker.MakeThing(def);
                stack.stackCount = 1;
                testThings.Add(stack);
                Thing spawned = GenSpawn.Spawn(stack, storageCell, map);
                if (spawned != null && spawned != stack)
                {
                    testThings.Add(spawned);
                }

                if (spawned == null || spawned.Destroyed ||
                    !OrderValidator.IsAvailableColonyStock(spawned))
                {
                    failure = "the spawned item was not genuinely available in colony storage";
                    return false;
                }

                return true;
            }
            catch (System.Exception ex)
            {
                failure = $"could not create stored test stock: {ex.Message}";
                return false;
            }
        }

        private static RecurringContract AddAutoReadyFixture(
            IntercolonyWorldComponent state, Map fulfillmentMap, ThingDef def,
            bool autoReadyOrders, FulfillmentMode fulfillment, int contractId, int orderId,
            List<RecurringContract> testContracts, List<SalesOrder> testOrders,
            out SalesOrder order)
        {
            RecurringContract contract = new RecurringContract
            {
                id = contractId,
                settlementId = -1,
                settlementName = "Auto-ready self-test buyer",
                factionName = "Auto-ready self-test faction",
                thingDef = def,
                quantityPerCycle = 1,
                cadenceTicks = GenDate.TicksPerDay,
                totalCycles = 1,
                unitPrice = 1f,
                fulfillment = fulfillment,
                status = ContractStatus.Active,
                nextCycleTick = GenTicks.TicksGame + GenDate.TicksPerDay,
                autoReadyOrders = autoReadyOrders
            };

            order = new SalesOrder
            {
                id = orderId,
                contractId = contractId,
                settlementId = -1,
                settlementName = "Auto-ready self-test buyer",
                factionName = "Auto-ready self-test faction",
                line = new OrderLine(def, 1),
                unitPrice = 1f,
                acceptedTick = GenTicks.TicksGame,
                deadlineTick = GenTicks.TicksGame + 10 * GenDate.TicksPerDay,
                fulfillment = fulfillment,
                fulfillmentMap = fulfillmentMap,
                status = SalesOrderStatus.Accepted
            };

            contract.activeOrderId = order.id;
            state.AddContract(contract);
            state.AddOrder(order);
            testContracts.Add(contract);
            testOrders.Add(order);
            return contract;
        }

        private static List<Letter> SnapshotLetters()
        {
            return Find.LetterStack == null
                ? new List<Letter>()
                : new List<Letter>(Find.LetterStack.LettersListForReading);
        }

        private static List<IArchivable> SnapshotArchivables()
        {
            return Find.Archive == null
                ? new List<IArchivable>()
                : new List<IArchivable>(Find.Archive.ArchivablesListForReading);
        }

        private static void RemoveGeneratedLetters(
            List<Letter> existingLetters, List<IArchivable> existingArchivables)
        {
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

        private static void DestroyTestThings(List<Thing> testThings)
        {
            foreach (Thing thing in testThings)
            {
                if (thing != null && !thing.Destroyed)
                {
                    thing.Destroy(DestroyMode.Vanish);
                }
            }
        }

        // --- §115's second acceptance criterion --------------------------------------------

        /// <summary>
        /// *"Neither employment nor supply agreements end by silently lapsing."*
        ///
        /// Checked as a property of the data rather than by reading letters: every terminal state
        /// must carry a note. A blank outcome is exactly what a silent lapse looks like from the
        /// player's side.
        /// </summary>
        private static void CheckNothingLapsesSilently(Results r)
        {
            EmploymentContract contract = Synthetic(CombatClause.Civilian, 40, 30);
            contract.status = EmploymentStatus.Completed;
            contract.outcomeNote = "";

            r.Check(!contract.StatusLine().NullOrEmpty(),
                "a finished employment always reads as something, never blank");

            RecurringContract agreement = new RecurringContract
            {
                id = -880,
                settlementName = "Testholme",
                factionName = "Test Confederacy",
                thingDef = ThingDefOf.Steel,
                quantityPerCycle = 100,
                totalCycles = 4,
                unitPrice = 2f,
                status = ContractStatus.Completed,
                cyclesCompleted = 4,
                renewalOffered = true,
                renewalExpiryTick = GenTicks.TicksGame + 8 * GenDate.TicksPerDay
            };

            r.Check(agreement.DaysUntilRenewalExpires > 0f,
                "a renewal offer has a deadline rather than sitting forever",
                $"{agreement.DaysUntilRenewalExpires:0.#} days");

            ContractService.DeclineRenewal(agreement);
            r.Check(!agreement.renewalOffered && agreement.outcomeNote.Contains("declined"),
                "declining a supply renewal records that it was declined (§115)",
                $"\"{agreement.outcomeNote.Trim()}\"");
        }

        // --- Helpers -----------------------------------------------------------------------

        /// <summary>
        /// A contract with no pawn and no world presence, at a chosen tenure.
        ///
        /// Tenure is faked by backdating <c>arrivedTick</c>, which is the same field the live code
        /// reads — so the arithmetic under test is the arithmetic that runs in play.
        /// </summary>
        private static EmploymentContract Synthetic(CombatClause clause, int dailyWage, int tenureDays)
        {
            return new EmploymentContract
            {
                id = -890,
                settlementName = "Testholme",
                factionName = "Test Confederacy",
                workerName = "Probe",
                workerSkills = "none",
                dailyWage = dailyWage,
                termDays = Mathf.Max(1, tenureDays),
                combatClause = clause,
                wageStructure = WageStructure.Daily,
                hiredTick = GenTicks.TicksGame - tenureDays * GenDate.TicksPerDay,
                arrivalTick = GenTicks.TicksGame - tenureDays * GenDate.TicksPerDay,
                arrivedTick = GenTicks.TicksGame - tenureDays * GenDate.TicksPerDay,
                endTick = GenTicks.TicksGame + GenDate.TicksPerDay,
                status = EmploymentStatus.Active
            };
        }

        private static string Trim(string text)
        {
            if (text.NullOrEmpty())
            {
                return "";
            }

            string flat = text.Replace("\n", " ");
            return flat.Length <= 70 ? flat : flat.Substring(0, 70) + "...";
        }

        private static string Summarize(Results r)
        {
            r.sb.AppendLine();
            r.sb.AppendLine(
                $"  {r.passed} passed, {r.failed} failed" +
                (r.skipped == 0 ? "." : $", {r.skipped} skipped."));
            return r.sb.ToString();
        }
    }
}
