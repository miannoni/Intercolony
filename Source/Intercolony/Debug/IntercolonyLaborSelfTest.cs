using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace Intercolony
{
    /// <summary>
    /// End-to-end check of Phase 16's acceptance criteria (DESIGN.md §109).
    ///
    /// This drives the **real** hire path — <see cref="EmploymentService.TryHire"/>,
    /// <see cref="EmploymentService.Advance"/>, <see cref="EmploymentService.End"/> — rather
    /// than a convenient stand-in. Phase 4 taught that a test built against a private copy of
    /// the logic passes vacuously, and Phase 14 that it can also fail spuriously.
    ///
    /// Save/load survival is the one criterion no self-test can reach: it needs a real
    /// save-and-reload cycle. It is checked by hand, and the manual steps are printed here so
    /// they are not forgotten.
    /// </summary>
    public static class IntercolonyLaborSelfTest
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
            r.sb.AppendLine("Labor self-test (DESIGN.md §109)");

            if (state == null || map == null)
            {
                r.sb.AppendLine("  No world or map. Open a colony first.");
                return r.sb.ToString();
            }

            try
            {
                // --- Candidate pool ---
                List<LaborCandidate> pool = LaborCandidateService.Refresh(state);
                r.Check(pool.Count > 0, "candidate pool is not empty", $"{pool.Count} workers offered");

                if (pool.Count == 0)
                {
                    r.sb.AppendLine("  Cannot continue without a candidate.");
                    return Summarize(r);
                }

                CheckPricing(r, state, pool);

                // --- Hire ---
                LaborCandidate candidate = pool[0];
                int term = candidate.minTermDays;
                int expectedTotal = candidate.dailyWage * term;

                // Budget for the second hire too, or the early-dismissal check silently skips —
                // which is how the KeepForever unpin path went unexercised on the first run.
                int budget = expectedTotal;
                if (pool.Count > 1)
                {
                    budget += pool[1].dailyWage * pool[1].minTermDays;
                }

                int added = IntercolonyLaborSelfTestSupport.EnsureSilver(map, budget);
                if (added > 0)
                {
                    r.Info($"added {added} silver to storage so the hire path could run.");
                }

                int silverBefore = PurchaseOrderService.CountColonySilver(map);
                if (silverBefore < expectedTotal)
                {
                    r.Check(false, "colony can afford the cheapest worker",
                        $"{silverBefore} silver in storage, {expectedTotal} needed");
                    return Summarize(r);
                }

                Faction employer = candidate.faction;
                PawnKindDef originalKind = candidate.pawn.kindDef;
                string workerName = candidate.Name;

                // Captured before the hire, because hiring releases the pawn from the candidate
                // and anything read from it afterwards is a fallback string, not the truth.
                string expectedSkills = candidate.SkillSummary();

                EmploymentContract contract = EmploymentService.TryHire(
                    state, candidate, term, map, out string failReason,
                    WageStructure.Prepaid, CombatClause.Civilian);

                r.Check(contract != null, "hire succeeded", failReason ?? $"{workerName}, {term} days");
                if (contract == null)
                {
                    return Summarize(r);
                }

                r.Check(contract.workerSkills == expectedSkills,
                    "the record froze the worker's real skills",
                    $"expected \"{expectedSkills}\", got \"{contract.workerSkills}\"");

                int silverAfter = PurchaseOrderService.CountColonySilver(map);
                r.Check(silverBefore - silverAfter == contract.paidSilver,
                    "wages were deducted exactly once",
                    $"{silverBefore} -> {silverAfter}, contract says {contract.paidSilver}");
                // Not dailyWage x termDays: from Phase 18 a prepaid hire carries §37's discount,
                // so the gross rate is the wrong expectation and TotalCommitment is the right one.
                r.Check(contract.paidSilver == contract.TotalCommitment,
                    "the prepaid total matches the quoted commitment",
                    $"{contract.dailyWage}/day x {contract.termDays}d = {contract.TotalCommitment} " +
                    $"({contract.dailyWage * contract.termDays} before the prepay discount), " +
                    $"paid {contract.paidSilver}");
                r.Check(contract.TotalCommitment < contract.dailyWage * contract.termDays,
                    "prepaying costs less than the gross rate (§37)");
                r.Check(contract.status == EmploymentStatus.Travelling,
                    "contract starts as travelling", contract.status.ToString());
                r.Check(contract.pawn != null && !contract.pawn.Spawned,
                    "worker is not on the map yet");
                r.Check(contract.pawn != null && Find.WorldPawns.Contains(contract.pawn),
                    "worker is parked in the world pawn pool so nothing collects them");

                // --- Arrival ---
                contract.arrivalTick = GenTicks.TicksGame;
                EmploymentService.Advance(state.Employments);

                Pawn worker = contract.pawn;
                r.Check(contract.status == EmploymentStatus.Active,
                    "contract went active on arrival", contract.status.ToString());
                r.Check(worker != null && worker.Spawned, "worker is spawned on the map");

                if (worker == null || !worker.Spawned)
                {
                    return Summarize(r);
                }

                r.Check(worker.Faction == Faction.OfPlayer, "worker is in the player faction");
                r.Check(worker.IsFreeColonist, "worker is a free colonist (this is what makes them usable)");
                r.Check(worker.IsQuestLodger(), "worker is a quest lodger");
                r.Check(worker.kindDef == originalKind, "kindDef survived the transfer",
                    $"{originalKind?.defName} -> {worker.kindDef?.defName}");
                r.Check(worker.HomeFaction == employer, "home faction is still the employer",
                    $"{worker.HomeFaction?.Name ?? "none"} vs {employer?.Name ?? "none"}");
                r.Check(worker.workSettings != null, "work priorities are assignable");
                r.Check(worker.drafter != null, "worker is draftable");
                r.Check(contract.endTick > GenTicks.TicksGame,
                    "term clock starts at arrival, not at hire",
                    $"{(contract.endTick - GenTicks.TicksGame) / (float)GenDate.TicksPerDay:0.#}d remaining");

                // Informational: the storyteller exclusion the lodger route buys. Threat points
                // move with wealth too, so this is reported rather than asserted — the binding
                // evidence is the lodger flag above, which DefaultThreatPointsNow tests directly.
                r.Info($"threat points now: {StorytellerUtility.DefaultThreatPointsNow(map):0}");

                // --- Caravan eligibility (§33 q9) ---
                // Asserted against the list the caravan dialog actually builds, not against
                // IsFreeColonist. The Phase 15 spike checked IsFreeColonist and reported "caravan
                // eligible: yes" while vanilla was in fact filtering employees out for being
                // quest lodgers, and it took playtesting to notice.
                r.Check(Dialog_FormCaravan.AllSendablePawns(map, reform: false).Contains(worker),
                    "an employee can be loaded onto a caravan (§33 q9)");

                // --- Term expiring while the worker is away from any map ---
                // Simulated by despawning: that is exactly the state a pawn is in while inside a
                // caravan, and it is the condition Advance tests.
                IntVec3 restoreCell = worker.Position;
                worker.DeSpawn();
                contract.endTick = GenTicks.TicksGame;
                EmploymentService.Advance(state.Employments);

                r.Check(contract.status == EmploymentStatus.Active,
                    "an expired term is held, not ended, while the worker is off-map",
                    contract.status.ToString());
                r.Check(contract.termLapsedNotified, "the player is told the term lapsed while away");

                GenSpawn.Spawn(worker, restoreCell, map);

                // --- Expiry and departure ---
                contract.endTick = GenTicks.TicksGame;
                EmploymentService.Advance(state.Employments);

                r.Check(contract.status == EmploymentStatus.Completed,
                    "contract completed when the term ran out", contract.status.ToString());
                r.Check(worker.Faction == employer, "faction restored to the employer",
                    $"{worker.Faction?.Name ?? "none"}");
                r.Check(!worker.IsColonist, "worker is no longer a colonist");
                r.Check(worker.kindDef == originalKind, "kindDef intact after departure",
                    $"{originalKind?.defName} -> {worker.kindDef?.defName}");
                r.Check(worker.GetLord() != null, "worker is walking off the map");
                r.Check(contract.pawn == null && contract.quest == null,
                    "closed record holds no live references (nothing to dangle on load)");

                // --- Dismissal before arrival ---
                CheckEarlyDismissal(r, state, map);

                r.sb.AppendLine();
                r.sb.AppendLine("  Not covered here — check by hand:");
                r.sb.AppendLine("    * save mid-employment, quit to menu, reload, confirm the worker is still");
                r.sb.AppendLine("      employed, still a lodger, and the term clock did not reset (§61, §82);");
                r.sb.AppendLine("    * that the worker actually hauls, cooks and sleeps over several days.");
            }
            catch (System.Exception ex)
            {
                r.sb.AppendLine($"  EXCEPTION: {ex}");
                r.failed++;
            }
            finally
            {
                LaborCandidateService.Clear();
            }

            return Summarize(r);
        }

        /// <summary>Wage rules that must hold regardless of which worker was rolled.</summary>
        private static void CheckPricing(Results r, IntercolonyWorldComponent state, List<LaborCandidate> pool)
        {
            int positive = 0;
            int longerIsCheaperPerDay = 0;
            int sampled = 0;

            foreach (LaborCandidate candidate in pool)
            {
                if (candidate.dailyWage > 0)
                {
                    positive++;
                }

                SettlementEconomicProfile profile =
                    state.GetProfile(IntercolonyMarketAccess.FindSettlement(candidate.settlementId));

                float standing = EmployerReputationService.ScoreFor(state);
                int shortTerm = LaborCandidateService.DailyWage(
                    candidate.pawn, profile, candidate.distanceTiles, 3, standing,
                    CombatClause.Civilian);
                int longTerm = LaborCandidateService.DailyWage(
                    candidate.pawn, profile, candidate.distanceTiles, 30, standing,
                    CombatClause.Civilian);
                sampled++;
                if (longTerm <= shortTerm)
                {
                    longerIsCheaperPerDay++;
                }
            }

            r.Check(positive == pool.Count, "every quoted wage is positive",
                $"{positive}/{pool.Count}");
            r.Check(longerIsCheaperPerDay == sampled,
                "a longer term never costs more per day (§36.1)",
                $"{longerIsCheaperPerDay}/{sampled} sampled");
            r.Check(pool.TrueForAll(c => c.minTermDays > 0), "every candidate has a minimum term");
            r.Check(pool.TrueForAll(c => c.travelDays > 0), "every candidate has a travel time");
            r.Check(pool.TrueForAll(c => c.pawn != null && c.pawn.RaceProps.Humanlike),
                "every candidate is a humanlike pawn");
        }

        /// <summary>
        /// A hire cancelled before the worker arrives must not leave a pinned pawn behind.
        /// TryHire pins them as KeepForever, which the world pawn GC obeys forever if nothing
        /// unpins them.
        /// </summary>
        private static void CheckEarlyDismissal(Results r, IntercolonyWorldComponent state, Map map)
        {
            List<LaborCandidate> pool = LaborCandidateService.Refresh(state);
            if (pool.Count == 0)
            {
                r.Info("early-dismissal check skipped: no second candidate available.");
                return;
            }

            LaborCandidate candidate = pool[0];
            int total = candidate.dailyWage * candidate.minTermDays;
            if (PurchaseOrderService.CountColonySilver(map) < total)
            {
                r.Info($"early-dismissal check skipped: needs {total} silver.");
                return;
            }

            EmploymentContract contract = EmploymentService.TryHire(
                state, candidate, candidate.minTermDays, map, out string failReason,
                WageStructure.Prepaid, CombatClause.Civilian);
            if (contract == null)
            {
                r.Check(false, "second hire for the dismissal check succeeded", failReason);
                return;
            }

            Pawn worker = contract.pawn;
            EmploymentService.End(contract, EmploymentStatus.Dismissed, "dismissed by self-test");

            r.Check(contract.status == EmploymentStatus.Dismissed,
                "a travelling worker can be dismissed before arrival");
            r.Check(worker != null && !Find.WorldPawns.Contains(worker),
                "a dismissed traveller is unpinned from the world pawn pool");
            r.Check(contract.pawn == null, "dismissed record holds no pawn reference");
        }

        private static string Summarize(Results r)
        {
            r.sb.AppendLine();
            r.sb.AppendLine($"  {r.passed} passed, {r.failed} failed.");
            return r.sb.ToString();
        }
    }
}
