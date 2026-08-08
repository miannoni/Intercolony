using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// End-to-end check of Phase 20's two acceptance criteria (DESIGN.md §113, §42, §43, §88).
    ///
    /// §113 asks for two things, and this test is organised around them rather than around the code:
    ///
    /// 1. *"Using civilian workers aggressively in combat has meaningful cost."* — proven as
    ///    arithmetic on the real pricing and compensation services, at several term lengths, because
    ///    the interesting failure is an inversion that only shows up on long contracts.
    /// 2. *"A source faction turning hostile mid-contract produces a stated, understandable outcome
    ///    for both the employee and any booked trade obligations — never a silently voided
    ///    obligation."* — proven by driving the real <see cref="HostilityPolicy"/> transitions and
    ///    asserting that nothing ends up refunded, breached or blank when it should not.
    ///
    /// Everything it touches is either synthetic or restored. It never declares a real war.
    /// </summary>
    public static class IntercolonyCombatClauseSelfTest
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
            r.sb.AppendLine("Combat clause, compensation and war-policy self-test (§113, §42, §43, §88)");

            if (state == null || map == null)
            {
                r.sb.AppendLine("  No world or map. Open a colony first.");
                return Summarize(r);
            }

            EmployerReputation rep = state.EmployerStanding;
            float savedScore = rep?.Score ?? 0f;
            int savedBreaches = rep?.combatClauseBreaches ?? 0;
            int savedWalkOuts = rep?.walkOuts ?? 0;
            int savedUnpaid = rep?.unpaidCompensation ?? 0;
            int savedDeaths = rep?.employeeDeaths ?? 0;
            int savedDenials = rep?.safePassageDenials ?? 0;
            int savedDebts = state.LaborDebts.Count;

            try
            {
                CheckClausePermissions(r);
                CheckClausePricing(r, state, map);
                CheckCompensationScale(r);
                CheckShieldIsNeverCheaper(r);
                CheckBreachEscalation(r, state);
                CheckWarAgreesWithMarketAccess(r);
                CheckWarOnSalesOrder(r);
                CheckWarOnPurchaseOrder(r);
                CheckWarSuspendsAgreement(r, state, map);
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
                    rep.combatClauseBreaches = savedBreaches;
                    rep.walkOuts = savedWalkOuts;
                    rep.unpaidCompensation = savedUnpaid;
                    rep.employeeDeaths = savedDeaths;
                    rep.safePassageDenials = savedDenials;
                }

                while (state.LaborDebts.Count > savedDebts)
                {
                    state.LaborDebts.RemoveAt(state.LaborDebts.Count - 1);
                }

                r.Info($"restored employer standing to {rep?.ScoreDisplay ?? 0}/100 and removed test debts.");
            }

            return Summarize(r);
        }

        // --- §42: what each clause permits ------------------------------------------------

        /// <summary>
        /// The permission matrix, stated as a table so a future edit that collapses two clauses into
        /// one behaviour fails here rather than in play.
        /// </summary>
        private static void CheckClausePermissions(Results r)
        {
            r.Check(!CombatClause.Civilian.PermitsCombat(true) &&
                    !CombatClause.Civilian.PermitsCombat(false),
                "a civilian may not be drafted into a fight anywhere (§42)");

            r.Check(CombatClause.Armed.PermitsCombat(true) &&
                    !CombatClause.Armed.PermitsCombat(false),
                "an armed employee may defend the colony but not campaign away from it (§42)");

            r.Check(CombatClause.Security.PermitsCombat(true) &&
                    CombatClause.Security.PermitsCombat(false),
                "a security contractor may fight anywhere (§42)");

            // The distinction between Armed and Security only exists because of the map test. If
            // both ever answered the same way for both maps, one of them would be dead weight.
            r.Check(CombatClause.Armed.PermitsCombat(false) != CombatClause.Security.PermitsCombat(false),
                "armed and security are genuinely different clauses, not two names for one");
        }

        // --- §42: pricing ------------------------------------------------------------------

        /// <summary>
        /// Prices one real candidate under all three clauses through the real wage formula.
        ///
        /// Uses the live candidate pool rather than a hand-built pawn, for the reason Phase 19
        /// recorded: a test that constructs its own inputs proves its own arithmetic and nothing
        /// about the code the game runs.
        /// </summary>
        private static void CheckClausePricing(Results r, IntercolonyWorldComponent state, Map map)
        {
            List<LaborCandidate> pool = new List<LaborCandidate>(LaborCandidateService.Refresh(state));
            if (pool.Count == 0)
            {
                r.Info("clause pricing skipped: no workers on offer this cycle.");
                return;
            }

            LaborCandidate probe = pool[0];
            Settlement settlement = IntercolonyMarketAccess.FindSettlement(probe.settlementId);
            SettlementEconomicProfile profile = settlement == null ? null : state.GetProfile(settlement);
            float standing = EmployerReputationService.ScoreFor(state);

            int civilian = LaborCandidateService.DailyWage(
                probe.pawn, profile, probe.distanceTiles, probe.minTermDays, standing,
                CombatClause.Civilian);
            int armed = LaborCandidateService.DailyWage(
                probe.pawn, profile, probe.distanceTiles, probe.minTermDays, standing,
                CombatClause.Armed);
            int security = LaborCandidateService.DailyWage(
                probe.pawn, profile, probe.distanceTiles, probe.minTermDays, standing,
                CombatClause.Security);

            r.Check(civilian < armed && armed < security,
                "the same worker costs strictly more the more you may ask of them (§42)",
                $"{civilian} / {armed} / {security} silver per day");

            // §42 says "higher" and "much higher". A few percent would satisfy the ordering above
            // while failing the design, so the gap is asserted as well as the direction.
            r.Check(security >= civilian * 2,
                "a security contractor is much more expensive, not marginally (§42)",
                $"x{security / (float)Mathf.Max(1, civilian):0.00}");

            r.Check(probe.dailyWage == civilian,
                "the listed rate in the hiring table is the civilian rate",
                $"listed {probe.dailyWage}, civilian {civilian}");
        }

        // --- §43: compensation -------------------------------------------------------------

        private static void CheckCompensationScale(Results r)
        {
            // Same wage across all three, to isolate the clause's effect on the payout from its
            // effect on the wage.
            EmploymentContract civilian = Synthetic(CombatClause.Civilian, 40, 20);
            EmploymentContract armed = Synthetic(CombatClause.Armed, 40, 20);
            EmploymentContract security = Synthetic(CombatClause.Security, 40, 20);

            int cDeath = CompensationService.DeathCompensation(civilian);
            int aDeath = CompensationService.DeathCompensation(armed);
            int sDeath = CompensationService.DeathCompensation(security);

            r.Check(cDeath > aDeath && aDeath > sDeath,
                "at the same wage, a civilian death costs most and a contractor's least (§43)",
                $"{cDeath} / {aDeath} / {sDeath} silver");

            // §43 prints 2,400 silver as its example figure. A mid-range worker earns about 40 a
            // day, which is what the 60-day civilian figure was chosen to reproduce. If someone
            // retunes the constants, this is the line that notices the example stopped matching.
            r.Check(cDeath == 2400,
                "a civilian death at 40 silver/day reproduces §43's worked example",
                $"{cDeath} silver");

            r.Check(CompensationService.CaptureCompensation(civilian) == cDeath &&
                    CompensationService.CaptureCompensation(armed) == aDeath &&
                    CompensationService.CaptureCompensation(security) == sDeath,
                "capture uses the death amount for every combat clause, without reusing its label");

            // Injury must always be cheaper than death, or the player is better off finishing them.
            foreach (CombatClause clause in CombatClauseUtility.All)
            {
                EmploymentContract contract = Synthetic(clause, 40, 20);
                int death = CompensationService.DeathCompensation(contract);
                int maimed = CompensationService.InjuryCompensation(contract, 20);

                r.Check(maimed <= death && maimed > 0,
                    $"maiming a {clause.Label()} costs something, but never more than killing them (§43)",
                    $"{maimed} vs {death} silver");
            }

            r.Check(CompensationService.InjuryCompensation(Synthetic(CombatClause.Civilian, 40, 20), 0) == 0,
                "a worker who goes home unharmed is owed no injury compensation");

            // The surcharge must rise with every breach and then stop, not run away.
            EmploymentContract escalating = Synthetic(CombatClause.Civilian, 40, 20);
            int previous = CompensationService.DeathCompensation(escalating);
            bool monotonic = true;
            for (int breaches = 1; breaches <= 8; breaches++)
            {
                escalating.clauseBreaches = breaches;
                int now = CompensationService.DeathCompensation(escalating);
                if (now < previous)
                {
                    monotonic = false;
                }

                previous = now;
            }

            r.Check(monotonic, "compensation never falls as breaches accumulate");
            r.Check(previous == 2400 * (int)CompensationService.MaxBreachMultiplier,
                "the breach surcharge is capped rather than unbounded",
                $"{previous} silver at 8 breaches");
        }

        /// <summary>
        /// §113's first acceptance criterion: *"Using civilian workers aggressively in combat has
        /// meaningful cost."*
        ///
        /// **The first version of this test asserted something false, and finding that out was worth
        /// more than the test passing.** It claimed the money comparison holds at every term length.
        /// It does not, and no choice of constants can make it: the shield costs
        /// <c>w*T + C_civilian</c> and the contractor <c>2.5*w*T + C_security</c>, so the shield wins
        /// whenever <c>1.5*w*T</c> exceeds the difference in payouts. Compensation is a fixed number
        /// of days' wage while both wage bills grow with the term, so a long enough contract always
        /// favours the shield. Raising the surcharge moves the crossover; it cannot remove it.
        ///
        /// So the criterion is defended in two parts, which is how the design actually works:
        ///
        /// * **Money, over the terms the game can produce.** The crossover sits above
        ///   <see cref="LaborCandidateService.MaxTermDays"/>, so within every contract a player can
        ///   sign, the shield is strictly dearer. The crossover is located here rather than assumed,
        ///   so raising the cap — which Phase 22 (§115, long fixed-term and open-ended contracts)
        ///   will want to do — fails this test instead of quietly making the exploit correct.
        /// * **Availability, above it.** A civilian cannot be drafted more than
        ///   <see cref="CombatUseMonitor.BreachesBeforeQuitting"/> times before leaving, so a long
        ///   campaign is not purchasable from a civilian at any price. That, not the payout, is what
        ///   holds at term lengths where the arithmetic runs out.
        /// </summary>
        private static void CheckShieldIsNeverCheaper(Results r)
        {
            const int baseWage = 40;
            int contractorWage = Mathf.RoundToInt(baseWage * CombatClause.Security.WageFactor());

            // Cost of getting a worker killed after using them as a fighter for the whole term.
            int ShieldCost(int term)
            {
                EmploymentContract shield = Synthetic(CombatClause.Civilian, baseWage, term);
                shield.clauseBreaches = CombatUseMonitor.BreachesBeforeRefusingWork;
                return baseWage * term + CompensationService.DeathCompensation(shield);
            }

            int ContractorCost(int term)
            {
                EmploymentContract contractor = Synthetic(CombatClause.Security, contractorWage, term);
                return contractorWage * term + CompensationService.DeathCompensation(contractor);
            }

            // --- Part one: money, across every term the game can actually offer ---
            int worstTerm = -1;
            float worstRatio = float.MaxValue;
            bool everCheaper = false;

            for (int term = 2; term <= LaborCandidateService.MaxTermDays; term++)
            {
                int shieldCost = ShieldCost(term);
                int contractorCost = ContractorCost(term);

                float ratio = shieldCost / (float)contractorCost;
                if (ratio < worstRatio)
                {
                    worstRatio = ratio;
                    worstTerm = term;
                }

                if (shieldCost <= contractorCost)
                {
                    everCheaper = true;
                }
            }

            r.Check(!everCheaper,
                "within every term the game can offer, a civilian shield is dearer than a fighter (§113)",
                $"tightest at {worstTerm}d, x{worstRatio:0.00}; cap is " +
                $"{LaborCandidateService.MaxTermDays}d");

            // Locate the crossover rather than assume it. A shrinking margin is the early warning
            // that the balance is drifting towards the cap.
            int crossover = -1;
            for (int term = 2; term <= 2000; term++)
            {
                if (ShieldCost(term) <= ContractorCost(term))
                {
                    crossover = term;
                    break;
                }
            }

            r.Check(crossover < 0 || crossover > LaborCandidateService.MaxTermDays,
                "the term length where the shield becomes cheaper is beyond the hiring cap",
                crossover < 0
                    ? "no crossover below 2000 days"
                    : $"crossover at {crossover}d vs cap {LaborCandidateService.MaxTermDays}d");

            r.Info(crossover < 0
                ? "money alone deters the shield at every term length tested."
                : $"PHASE 22 NOTE (§115): raising MaxTermDays past {crossover - 1} makes a drafted " +
                  "civilian the cheaper fighter on pure cost. Above that only the walk-out deters it.");

            // --- Part two: availability, which is what holds past the crossover ---
            r.Check(CombatUseMonitor.BreachesBeforeQuitting > 0,
                "a civilian can only be drafted a bounded number of times before leaving (§42)",
                $"{CombatUseMonitor.BreachesBeforeQuitting} fights, then they go home");
            r.Check(CombatUseMonitor.BreachesBeforeRefusingWork < CombatUseMonitor.BreachesBeforeQuitting,
                "they stop working before they leave, so the escalation has a warning stage (§39's shape)");

            // --- And breaching must always cost more than honouring, same worker, same term ---
            EmploymentContract wellUsed = Synthetic(CombatClause.Civilian, baseWage, 60);
            int honestCivilian = baseWage * 60 + CompensationService.DeathCompensation(wellUsed);
            int abusedCivilian = ShieldCost(60);

            r.Check(honestCivilian < abusedCivilian,
                "honouring the clause is cheaper than breaching it, same worker, same term",
                $"{honestCivilian} vs {abusedCivilian} silver");
        }

        // --- §42: the escalation -----------------------------------------------------------

        /// <summary>
        /// Drives the real escalation in <see cref="CombatUseMonitor"/> — warn, down tools, walk out
        /// — on a synthetic contract with no pawn.
        ///
        /// No pawn is deliberate: it proves the escalation does not depend on one, which is what
        /// makes it safe to run against a contract whose worker died in the same tick. The work-hold
        /// step is the one part that needs a pawn, and it is checked separately below by asserting
        /// the flag rather than the priorities.
        /// </summary>
        private static void CheckBreachEscalation(Results r, IntercolonyWorldComponent state)
        {
            EmploymentContract contract = Synthetic(CombatClause.Civilian, 40, 30);
            contract.status = EmploymentStatus.Active;
            contract.endTick = GenTicks.TicksGame + 30 * GenDate.TicksPerDay;

            r.Check(!contract.CombatUsePermittedNow,
                "a civilian off any player map is out of terms (§42)");

            // Breach 1: a warning and a reputation hit, worker stays.
            contract.lastIncidentTick = -99999;
            CombatUseMonitor.NoteDraftedAttack(contract, state, null);
            r.Check(contract.clauseBreaches == 1 && contract.status == EmploymentStatus.Active &&
                    !contract.refusingWork,
                "the first breach warns and nothing else",
                $"breaches {contract.clauseBreaches}, status {contract.status}");

            // The cooldown is what turns a firefight into one incident rather than fifty.
            int before = contract.clauseBreaches;
            bool counted = CombatUseMonitor.NoteDraftedAttack(contract, state, null);
            r.Check(!counted && contract.clauseBreaches == before,
                "a second shot in the same skirmish is the same incident, not a second breach");

            // Breach 2: downs tools, and paying wages must not undo it.
            contract.lastIncidentTick = -99999;
            CombatUseMonitor.NoteDraftedAttack(contract, state, null);
            r.Check(contract.clauseBreaches == 2 && contract.refusalReason == WorkRefusalReason.CombatMisuse,
                "the second breach makes them refuse work over combat, not wages",
                $"reason {contract.refusalReason}");

            contract.ResumeWork(WorkRefusalReason.UnpaidWages);
            r.Check(contract.refusalReason == WorkRefusalReason.CombatMisuse,
                "settling wages cannot buy off a combat-clause refusal (§42)");

            contract.ResumeWork(WorkRefusalReason.CombatMisuse);
            r.Check(contract.refusalReason == WorkRefusalReason.None,
                "the refusal does clear when the matching reason is given");

            // Breach 3: they leave.
            contract.lastIncidentTick = -99999;
            CombatUseMonitor.NoteDraftedAttack(contract, state, null);
            r.Check(contract.status == EmploymentStatus.Quit,
                "the third breach ends the employment (§42, §113)",
                $"status {contract.status}, note \"{contract.outcomeNote}\"");
            r.Check(!contract.outcomeNote.NullOrEmpty(),
                "the walk-out states a reason rather than ending blank");

            // A security contractor drafted the same number of times breaches nothing.
            EmploymentContract soldier = Synthetic(CombatClause.Security, 100, 30);
            soldier.status = EmploymentStatus.Active;
            for (int i = 0; i < 3; i++)
            {
                soldier.lastIncidentTick = -99999;
                CombatUseMonitor.NoteDraftedAttack(soldier, state, null);
            }

            r.Check(soldier.clauseBreaches == 0 && soldier.combatIncidents == 3 &&
                    soldier.status == EmploymentStatus.Active,
                "a security contractor's fights are recorded but never a breach (§113)",
                $"{soldier.combatIncidents} incidents, {soldier.clauseBreaches} breaches");
        }

        // --- §88: the war policy -----------------------------------------------------------

        /// <summary>
        /// The contradiction guard §113 warns about, plus the malformed-faction case that produced
        /// it in practice.
        ///
        /// <see cref="IntercolonyMarketAccess.IsAccessible"/> now calls
        /// <see cref="HostilityPolicy.IsAtWar"/> rather than repeating its tests, so the first
        /// assertion is structural rather than empirical — and it is kept anyway, because it is the
        /// assertion that fails the day someone reintroduces the second copy.
        ///
        /// The second part is the one with teeth. `Faction.RelationWith` does not fail quietly when
        /// a relation is missing: it writes a red `Log.Error` and returns a dummy whose `other` is
        /// null, which is enough to make `GoodwillSituationManager` throw further down. A world
        /// containing such a faction turned every hostility check in the mod into an error in the
        /// player's log, and the hourly sweep asks about every settlement. So the test names them.
        /// </summary>
        private static void CheckWarAgreesWithMarketAccess(Results r)
        {
            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements == null)
            {
                r.Info("access agreement skipped: no world objects.");
                return;
            }

            int checkedCount = 0;
            int contradictions = 0;
            int atWar = 0;

            foreach (Settlement settlement in settlements)
            {
                if (settlement.Faction == null || settlement.Faction.IsPlayer)
                {
                    continue;
                }

                checkedCount++;
                if (!HostilityPolicy.IsAtWar(settlement.Faction))
                {
                    continue;
                }

                atWar++;

                // One-directional on purpose: a settlement can be inaccessible for reasons that
                // have nothing to do with war (§51 eligibility, no faction). What must never happen
                // is a faction at war that the market still treats as open for business.
                if (IntercolonyMarketAccess.IsAccessible(settlement))
                {
                    contradictions++;
                }
            }

            r.Check(contradictions == 0,
                "no faction is at war and still open for business (§113's contradiction guard)",
                $"{checkedCount} settlements, {atWar} at war, {contradictions} contradictions");

            // --- Factions with no relation to the player at all ---
            List<Faction> factions = Find.FactionManager?.AllFactionsListForReading;
            if (factions == null)
            {
                return;
            }

            List<string> malformed = new List<string>();
            bool threw = false;

            foreach (Faction faction in factions)
            {
                if (faction == null || faction.IsPlayer)
                {
                    continue;
                }

                if (faction.RelationWith(Faction.OfPlayer, allowNull: true) != null)
                {
                    continue;
                }

                malformed.Add(faction.Name ?? faction.def?.defName ?? "<unnamed>");

                try
                {
                    // Must answer, quietly, rather than throw or write to the error log.
                    if (HostilityPolicy.IsAtWar(faction))
                    {
                        threw = true;
                    }
                }
                catch (System.Exception)
                {
                    threw = true;
                }
            }

            r.Check(!threw,
                "a faction with no player relation is answered quietly, not with an error (§88)",
                malformed.Count == 0
                    ? "no malformed factions in this world"
                    : $"{malformed.Count}: {string.Join(", ", malformed.ToArray())}");

            if (malformed.Count > 0)
            {
                r.Info($"NOTE: {string.Join(", ", malformed.ToArray())} carr(y/ies) no relation to " +
                       "the player. This was once assumed to be a world-generation artefact and was " +
                       "not — it was a faction object leaking from a previous game. Check " +
                       "LaborCandidateService's pool owner before blaming world data.");
            }

            // The leak that produced those malformed factions: employment records must only ever
            // reference factions this world knows about. A dead-world faction saves a reference
            // nothing can resolve, and the pawn beside it carries another world's thing IDs.
            int foreign = 0;
            foreach (EmploymentContract contract in IntercolonyWorldComponent.Current?.Employments
                                                    ?? new List<EmploymentContract>())
            {
                if (contract.employerFaction != null &&
                    !factions.Contains(contract.employerFaction))
                {
                    foreign++;
                    r.Info($"  employment #{contract.id} ({contract.workerName}) references " +
                           $"{contract.employerFaction.Name}, which is not in this world.");
                }
            }

            r.Check(foreign == 0,
                "every employment references a faction that exists in this world",
                foreign == 0 ? "all employer factions resolve" : $"{foreign} foreign reference(s)");
        }

        private static void CheckWarOnSalesOrder(Results r)
        {
            SalesOrder order = new SalesOrder
            {
                id = -901,
                settlementName = "Testholme",
                factionName = "Test Confederacy",
                line = new OrderLine(ThingDefOf.Steel, 300),
                unitPrice = 2f,
                acceptedTick = GenTicks.TicksGame,
                deadlineTick = GenTicks.TicksGame + 10 * GenDate.TicksPerDay,
                status = SalesOrderStatus.Accepted
            };

            bool applied = HostilityPolicy.ApplyToSalesOrder(order, sendLetter: false);

            r.Check(applied && order.status == SalesOrderStatus.Cancelled,
                "a war cancels an undelivered sales order (§88)",
                $"status {order.status}");
            r.Check(!order.outcomeNote.NullOrEmpty() && order.outcomeNote.Contains("war"),
                "the cancellation names the war rather than ending blank (§113)",
                $"\"{order.outcomeNote}\"");
            r.Check(order.status != SalesOrderStatus.Failed,
                "a war is not recorded as the player failing to deliver");
            r.Check(order.paidSilver == 0,
                "nothing was paid on a sales order, so nothing is lost");

            // Idempotence matters: the sweep runs hourly for as long as the war lasts.
            r.Check(!HostilityPolicy.ApplyToSalesOrder(order, sendLetter: false),
                "the hourly sweep does not re-cancel an already-cancelled order");
        }

        private static void CheckWarOnPurchaseOrder(Results r)
        {
            PurchaseOrder order = new PurchaseOrder
            {
                id = -902,
                settlementName = "Testholme",
                factionName = "Test Confederacy",
                thingDef = ThingDefOf.Steel,
                quantity = 300,
                unitPrice = 3.8f,
                paidSilver = 1140,
                orderedTick = GenTicks.TicksGame,
                readyTick = GenTicks.TicksGame + 5 * GenDate.TicksPerDay,
                status = PurchaseOrderStatus.Confirmed
            };

            bool applied = HostilityPolicy.ApplyToPurchaseOrder(order, sendLetter: false);

            r.Check(applied && order.status == PurchaseOrderStatus.LostToWar,
                "a war voids a prepaid purchase order (§88)",
                $"status {order.status}");

            // The load-bearing assertion. SupplierDefault means "refunded" everywhere else in the
            // mod, so if these two statuses were ever merged the player would silently get their
            // money back — the opposite of the chosen policy, and unnoticeable in play.
            r.Check(order.status != PurchaseOrderStatus.SupplierDefault,
                "a war is not a supplier default, because a default refunds and this does not");
            r.Check(order.paidSilver == 1140,
                "the prepayment is recorded as lost, not zeroed out of the record",
                $"{order.paidSilver} silver");
            r.Check(order.outcomeNote.Contains("1140"),
                "the outcome names the exact silver that was not recovered (§113)",
                $"\"{order.outcomeNote}\"");

            r.Check(!HostilityPolicy.ApplyToPurchaseOrder(order, sendLetter: false),
                "the hourly sweep does not re-void an already-voided order");
        }

        /// <summary>
        /// Suspension and resumption, driven through the real policy on a really-built contract.
        ///
        /// <see cref="ContractService.BuildOffer"/> is used rather than a hand-built object — it was
        /// made public for exactly this reason — and the contract is never added to world state, so
        /// nothing the player owns is touched.
        /// </summary>
        private static void CheckWarSuspendsAgreement(Results r, IntercolonyWorldComponent state, Map map)
        {
            RecurringContract contract = BuildProbeContract(state);
            if (contract == null)
            {
                r.Info("suspension check skipped: no settlement could produce an offer.");
                return;
            }

            contract.status = ContractStatus.Active;
            contract.cyclesCompleted = 3;
            contract.nextCycleTick = GenTicks.TicksGame + 5 * GenDate.TicksPerDay;

            int cyclesOwedBefore = contract.CyclesRemaining;
            int nextCycleBefore = contract.nextCycleTick;
            int failuresBefore = contract.cyclesFailed;

            bool suspended = HostilityPolicy.Suspend(state, contract, sendLetter: false);

            r.Check(suspended && contract.status == ContractStatus.Suspended,
                "a war suspends an active supply agreement rather than ending it (§88)",
                $"status {contract.status}");
            r.Check(contract.status != ContractStatus.Breached,
                "suspension is not a breach — the war was not the player's failure");
            r.Check(contract.cyclesFailed == failuresBefore,
                "no delivery is counted as missed while suspended (§113)",
                $"{contract.cyclesFailed} failures");
            r.Check(contract.CyclesRemaining == cyclesOwedBefore,
                "every remaining delivery survives the suspension",
                $"{contract.CyclesRemaining} of {contract.totalCycles}");
            r.Check(!contract.outcomeNote.NullOrEmpty(),
                "the suspension states why rather than going quiet");

            // Rewind the suspension start so resuming has a measurable outage to compensate for.
            const int outageDays = 12;
            contract.suspendedTick = GenTicks.TicksGame - outageDays * GenDate.TicksPerDay;

            bool resumed = HostilityPolicy.Resume(state, contract, sendLetter: false);

            r.Check(resumed && contract.IsActive,
                "peace resumes the agreement (§88)",
                $"status {contract.status}");

            int expected = nextCycleBefore + outageDays * GenDate.TicksPerDay;
            r.Check(contract.nextCycleTick == expected,
                "the cycle clock is pushed forward by exactly the length of the outage",
                $"{(contract.nextCycleTick - nextCycleBefore) / (float)GenDate.TicksPerDay:0.#} days");
            r.Check(contract.CyclesRemaining == cyclesOwedBefore,
                "resumption restores the same number of deliveries that were owed",
                $"{contract.CyclesRemaining}");
            r.Check(contract.suspendedTick == 0,
                "the suspension marker is cleared, so a second war measures its own outage");

            r.Check(!HostilityPolicy.Resume(state, contract, sendLetter: false),
                "an already-active agreement cannot be resumed twice");
            r.Check(!HostilityPolicy.Suspend(state, null, sendLetter: false) &&
                    !HostilityPolicy.Resume(state, null, sendLetter: false),
                "the policy tolerates a null contract without throwing");
        }

        // --- Helpers -----------------------------------------------------------------------

        /// <summary>
        /// A contract with no pawn and no world presence, for pricing and escalation arithmetic.
        /// Never added to state, so nothing here can touch a real employment.
        /// </summary>
        private static EmploymentContract Synthetic(CombatClause clause, int dailyWage, int termDays)
        {
            return new EmploymentContract
            {
                id = -900,
                settlementName = "Testholme",
                factionName = "Test Confederacy",
                workerName = "Probe",
                workerSkills = "none",
                dailyWage = dailyWage,
                termDays = termDays,
                combatClause = clause,
                wageStructure = WageStructure.Daily,
                hiredTick = GenTicks.TicksGame,
                arrivalTick = GenTicks.TicksGame,
                status = EmploymentStatus.Travelling
            };
        }

        private static RecurringContract BuildProbeContract(IntercolonyWorldComponent state)
        {
            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements == null)
            {
                return null;
            }

            foreach (Settlement settlement in settlements)
            {
                if (!IntercolonyMarketAccess.IsAccessible(settlement))
                {
                    continue;
                }

                SettlementEconomicProfile profile = state.GetProfile(settlement);
                if (profile == null)
                {
                    continue;
                }

                RecurringContract contract = ContractService.BuildOffer(
                    state, settlement, profile, Gen.HashCombineInt(settlement.ID, 0x50_4832));
                if (contract != null)
                {
                    return contract;
                }
            }

            return null;
        }

        private static string Summarize(Results r)
        {
            r.sb.AppendLine();
            r.sb.AppendLine($"  {r.passed} passed, {r.failed} failed.");
            return r.sb.ToString();
        }
    }
}
