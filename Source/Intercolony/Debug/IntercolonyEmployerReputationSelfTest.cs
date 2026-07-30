using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// End-to-end check of Phase 19's acceptance criterion (DESIGN.md §112, §40).
    ///
    /// §112 asks one thing: *"A bad employer experiences meaningfully worse hiring conditions."*
    /// So this test does not merely check that a score moves — it drives real conduct through the
    /// real services and then measures the hiring conditions that result, which is the only thing
    /// the criterion is actually about.
    ///
    /// The score is saved and restored around the run: a dev check must not permanently brand the
    /// player an exploitative employer.
    /// </summary>
    public static class IntercolonyEmployerReputationSelfTest
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
            r.sb.AppendLine("Employer reputation self-test (DESIGN.md §112, §40)");

            if (state == null || map == null)
            {
                r.sb.AppendLine("  No world or map. Open a colony first.");
                return Summarize(r);
            }

            EmployerReputation rep = state.EmployerStanding;
            r.Check(rep != null, "the colony has an employer record");
            if (rep == null)
            {
                return Summarize(r);
            }

            // Snapshot, because the test deliberately wrecks the score.
            float savedScore = rep.Score;
            int savedCompleted = rep.contractsCompleted;
            int savedLate = rep.latePayrollIncidents;
            int savedDeaths = rep.employeeDeaths;
            int savedUnpaid = rep.unpaidCompensation;
            int savedWalkOuts = rep.walkOuts;
            int savedDismissals = rep.earlyDismissals;
            int savedDebts = state.LaborDebts.Count;

            IntercolonyLaborSelfTestSupport.ResetLedger();

            try
            {
                CheckEffectCurves(r);
                CheckConductMovesTheScore(r, state, rep);
                CheckHiringConditions(r, state, map, rep);
                CheckGrievanceGate(r, state);
            }
            catch (System.Exception ex)
            {
                r.sb.AppendLine($"  EXCEPTION: {ex}");
                r.failed++;
            }
            finally
            {
                // Restore everything the test touched. Reputation is persistent state, and a
                // self-test that leaves the colony branded would be a bug of its own.
                rep.Adjust(savedScore - rep.Score);
                rep.contractsCompleted = savedCompleted;
                rep.latePayrollIncidents = savedLate;
                rep.employeeDeaths = savedDeaths;
                rep.unpaidCompensation = savedUnpaid;
                rep.walkOuts = savedWalkOuts;
                rep.earlyDismissals = savedDismissals;

                while (state.LaborDebts.Count > savedDebts)
                {
                    state.LaborDebts.RemoveAt(state.LaborDebts.Count - 1);
                }

                int returned = IntercolonyLaborSelfTestSupport.RestoreLedger(map);
                if (returned > 0)
                {
                    r.Info($"returned {returned} silver the test had consumed.");
                }

                LaborCandidateService.Clear();
                r.Info($"restored employer standing to {rep.ScoreDisplay}/100 and removed test debts.");
            }

            return Summarize(r);
        }

        /// <summary>The effect curves must be monotonic and must actually differ at the extremes.</summary>
        private static void CheckEffectCurves(Results r)
        {
            float worstWage = EmployerReputationService.WageFactor(EmployerReputation.MinScore);
            float bestWage = EmployerReputationService.WageFactor(EmployerReputation.MaxScore);
            float worstAvail = EmployerReputationService.AvailabilityFactor(EmployerReputation.MinScore);
            float bestAvail = EmployerReputationService.AvailabilityFactor(EmployerReputation.MaxScore);

            r.Check(worstWage > bestWage, "a bad employer pays more per worker than a good one",
                $"x{worstWage:0.00} vs x{bestWage:0.00}");
            r.Check(worstWage - bestWage >= 0.2f,
                "the wage penalty is meaningful, not cosmetic (§112)",
                $"{(worstWage - bestWage) * 100f:0}% spread");
            r.Check(worstAvail < bestAvail, "a bad employer sees less labor on offer",
                $"x{worstAvail:0.00} vs x{bestAvail:0.00}");

            bool wageMonotonic = true;
            bool availMonotonic = true;
            for (float score = 1f; score <= EmployerReputation.MaxScore; score += 1f)
            {
                if (EmployerReputationService.WageFactor(score) >
                    EmployerReputationService.WageFactor(score - 1f) + 0.0001f)
                {
                    wageMonotonic = false;
                }

                if (EmployerReputationService.AvailabilityFactor(score) <
                    EmployerReputationService.AvailabilityFactor(score - 1f) - 0.0001f)
                {
                    availMonotonic = false;
                }
            }

            r.Check(wageMonotonic, "wages never rise as reputation improves", "checked every point 0-100");
            r.Check(availMonotonic, "availability never falls as reputation improves", "checked every point 0-100");

            r.Check(EmployerReputationService.CandidateQualityBias(95f) > 0,
                "a sought-after employer gets first pick");
            r.Check(EmployerReputationService.CandidateQualityBias(10f) < 0,
                "an exploitative employer gets the leftovers");
            r.Check(EmployerReputationService.CandidateQualityBias(50f) == 0,
                "a neutral employer gets neither, so the common case costs no extra generation");

            // Tier boundaries must cover the range with no gap.
            EmployerReputation probe = new EmployerReputation();
            HashSet<EmployerTier> seen = new HashSet<EmployerTier>();
            for (int score = 0; score <= 100; score++)
            {
                probe.Adjust(score - probe.Score);
                seen.Add(probe.Tier);
            }

            r.Check(seen.Count == 5, "every tier is reachable", $"{seen.Count}/5 tiers seen");
        }

        /// <summary>§40's signals, applied through the real service rather than by setting fields.</summary>
        private static void CheckConductMovesTheScore(
            Results r, IntercolonyWorldComponent state, EmployerReputation rep)
        {
            EmploymentContract sample = new EmploymentContract
            {
                id = -1,
                settlementId = -999,
                settlementName = "SelfTest",
                factionName = "SelfTest",
                workerName = "Probe",
                dailyWage = 20,
                termDays = 30,
                arrearsSilver = 100
            };

            rep.Adjust(50f - rep.Score);

            float before = rep.Score;
            EmployerReputationService.NoteContractCompleted(state, sample);
            r.Check(rep.Score > before, "completing a contract raises standing (§40 positive signal)",
                $"{before:0.#} -> {rep.Score:0.#}");
            r.Check(rep.contractsCompleted > 0, "completions are counted for §40's screen");

            before = rep.Score;
            EmployerReputationService.NotePayrollMissed(state, sample);
            r.Check(rep.Score < before, "missing payroll lowers standing (§39 step 7)",
                $"{before:0.#} -> {rep.Score:0.#}");
            r.Check(rep.latePayrollIncidents > 0, "late payroll is counted");

            before = rep.Score;
            float completionGain = 0f;
            {
                float mark = rep.Score;
                EmployerReputationService.NoteContractCompleted(state, sample);
                completionGain = rep.Score - mark;
                rep.Adjust(-completionGain);
            }

            EmployerReputationService.NoteWalkOut(state, sample);
            float walkOutLoss = before - rep.Score;
            r.Check(walkOutLoss > 0f, "a worker walking out lowers standing",
                $"{before:0.#} -> {rep.Score:0.#}");
            r.Check(walkOutLoss > completionGain,
                "a walk-out costs more than a completed contract earns — reputation is easier to lose",
                $"-{walkOutLoss:0.#} vs +{completionGain:0.#}");
            r.Check(rep.unpaidCompensation >= sample.arrearsSilver,
                "unpaid wages show as unpaid compensation (§40)",
                $"{rep.unpaidCompensation} silver");

            before = rep.Score;
            EmployerReputationService.NoteEmployeeDied(state, sample);
            r.Check(rep.Score < before, "an employee dying lowers standing (§112 death effects)",
                $"{before:0.#} -> {rep.Score:0.#}");
            r.Check(rep.employeeDeaths > 0, "deaths are counted");

            // Settling a debt reduces the outstanding figure but does not undo the walk-out.
            int walkOutsBefore = rep.walkOuts;
            int unpaidBefore = rep.unpaidCompensation;
            LaborDebt debt = new LaborDebt
            {
                id = -2, settlementId = -999, settlementName = "SelfTest",
                workerName = "Probe", amountOwed = 100, originalAmount = 100
            };

            before = rep.Score;
            EmployerReputationService.NoteDebtSettled(state, debt);
            r.Check(rep.Score > before, "paying a debt late earns some credit back",
                $"{before:0.#} -> {rep.Score:0.#}");
            r.Check(rep.unpaidCompensation < unpaidBefore,
                "settling reduces unpaid compensation", $"{unpaidBefore} -> {rep.unpaidCompensation}");
            r.Check(rep.walkOuts == walkOutsBefore,
                "settling does not erase the walk-out — §40 is a record of conduct");

            r.Check(rep.Summary().Contains("Employer Reputation:") &&
                    rep.Summary().Contains("Late payroll incidents:") &&
                    rep.Summary().Contains("Unpaid compensation:"),
                "the summary matches §40's illustrated screen");

            // Score must stay in range no matter how much conduct is piled on.
            for (int i = 0; i < 40; i++)
            {
                EmployerReputationService.NoteWalkOut(state, sample);
            }

            r.Check(rep.Score >= EmployerReputation.MinScore, "score cannot go below zero",
                rep.Score.ToString("0.##"));

            for (int i = 0; i < 200; i++)
            {
                EmployerReputationService.NoteContractCompleted(state, sample);
            }

            r.Check(rep.Score <= EmployerReputation.MaxScore, "score cannot exceed 100",
                rep.Score.ToString("0.##"));
        }

        /// <summary>
        /// The criterion itself: the same world, priced as a good employer and as a bad one.
        /// </summary>
        private static void CheckHiringConditions(
            Results r, IntercolonyWorldComponent state, Map map, EmployerReputation rep)
        {
            // The load-bearing assertion, and it has to be same-pawn. Comparing pool averages
            // across two reputations compares two *different* sets of workers: a bad employer sees
            // fewer, weaker candidates, and a weak candidate is individually cheap even at a
            // premium. That comparison can come out either way on the luck of the draw, so it is
            // reported below rather than asserted. What must always hold is that the *same* worker
            // costs a bad employer more.
            List<LaborCandidate> probePool = LaborCandidateService.Refresh(state, force: true);
            if (probePool.Count > 0)
            {
                LaborCandidate probe = probePool[0];
                SettlementEconomicProfile probeProfile =
                    state.GetProfile(IntercolonyMarketAccess.FindSettlement(probe.settlementId));

                int atWorst = LaborCandidateService.DailyWage(
                    probe.pawn, probeProfile, probe.distanceTiles, probe.minTermDays,
                    EmployerReputation.MinScore);
                int atBest = LaborCandidateService.DailyWage(
                    probe.pawn, probeProfile, probe.distanceTiles, probe.minTermDays,
                    EmployerReputation.MaxScore);
                int atNeutral = LaborCandidateService.DailyWage(
                    probe.pawn, probeProfile, probe.distanceTiles, probe.minTermDays,
                    EmployerReputation.StartingScore);

                r.Check(atWorst > atNeutral && atNeutral > atBest,
                    "the same worker costs a bad employer more and a good one less",
                    $"{atWorst}/day at 0, {atNeutral}/day at 50, {atBest}/day at 100");
            }
            else
            {
                r.Check(false, "a candidate was available to price at both extremes");
            }

            rep.Adjust(EmployerReputation.MaxScore - rep.Score);
            List<LaborCandidate> goodPool = LaborCandidateService.Refresh(state, force: true);
            int goodCount = goodPool.Count;
            int goodCheapest = goodCount > 0 ? goodPool[0].dailyWage : 0;
            int goodTotal = 0;
            foreach (LaborCandidate c in goodPool)
            {
                goodTotal += c.dailyWage;
            }

            float goodAverage = goodCount > 0 ? goodTotal / (float)goodCount : 0f;
            float goodSkill = AverageTopSkill(goodPool);

            rep.Adjust(EmployerReputation.MinScore - rep.Score);
            List<LaborCandidate> badPool = LaborCandidateService.Refresh(state, force: true);
            int badCount = badPool.Count;
            int badCheapest = badCount > 0 ? badPool[0].dailyWage : 0;
            int badTotal = 0;
            foreach (LaborCandidate c in badPool)
            {
                badTotal += c.dailyWage;
            }

            float badAverage = badCount > 0 ? badTotal / (float)badCount : 0f;
            float badSkill = AverageTopSkill(badPool);

            r.Info($"as a sought-after employer: {goodCount} workers, cheapest {goodCheapest}/day, " +
                   $"average {goodAverage:0.#}/day, average best skill {goodSkill:0.#}.");
            r.Info($"as an exploitative employer: {badCount} workers, cheapest {badCheapest}/day, " +
                   $"average {badAverage:0.#}/day, average best skill {badSkill:0.#}.");
            r.Info("cheapest can be lower for a bad employer: the workers on offer are weaker, and a " +
                   "weak worker is cheap even at a premium. Compare the skill figures.");

            r.Check(goodCount > 0 && badCount > 0,
                "both extremes still offer somebody — a bad employer is squeezed, not locked out",
                $"{goodCount} vs {badCount}");
            r.Check(badCount < goodCount,
                "a bad employer sees fewer workers on offer (§39 step 9)",
                $"{badCount} vs {goodCount}");
            // Not asserted: see the note at the top of this method. Both pools are different sets
            // of workers, so which one has the higher average is not an invariant.
            r.Info(badAverage > goodAverage
                ? $"this draw: bad employer's average is higher ({badAverage:0.#} vs {goodAverage:0.#}/day)."
                : $"this draw: bad employer's average is lower ({badAverage:0.#} vs {goodAverage:0.#}/day) " +
                  "because its pool is weaker — not a failure.");

            r.Check(badSkill <= goodSkill,
                "a bad employer's pool is no stronger than a good employer's",
                $"average best skill {badSkill:0.#} vs {goodSkill:0.#}");
        }

        private static float AverageTopSkill(List<LaborCandidate> pool)
        {
            if (pool.Count == 0)
            {
                return 0f;
            }

            int total = 0;
            foreach (LaborCandidate candidate in pool)
            {
                int best = 0;
                if (candidate.pawn?.skills != null)
                {
                    foreach (SkillRecord skill in candidate.pawn.skills.skills)
                    {
                        if (!skill.TotallyDisabled && skill.Level > best)
                        {
                            best = skill.Level;
                        }
                    }
                }

                total += best;
            }

            return total / (float)pool.Count;
        }

        /// <summary>
        /// The per-settlement half of §39 step 9: a settlement still owed wages sends nobody,
        /// regardless of the general score.
        /// </summary>
        private static void CheckGrievanceGate(Results r, IntercolonyWorldComponent state)
        {
            r.Check(EmployerReputationService.WillSupplyLabor(state, -12345, out _),
                "a settlement with no grievance will supply labor");

            state.LaborDebts.Add(new LaborDebt
            {
                id = -3,
                settlementId = -12345,
                settlementName = "GrievanceTest",
                workerName = "Probe",
                amountOwed = 250,
                originalAmount = 250,
                incurredTick = GenTicks.TicksGame
            });

            bool willSupply = EmployerReputationService.WillSupplyLabor(state, -12345, out string reason);
            r.Check(!willSupply, "a settlement still owed wages refuses to send anyone", reason);
            r.Check(!reason.NullOrEmpty(), "and says why", reason);

            state.LaborDebts[state.LaborDebts.Count - 1].amountOwed = 0;
            r.Check(EmployerReputationService.WillSupplyLabor(state, -12345, out _),
                "settling the debt reopens that settlement as a source of labor");
        }

        private static string Summarize(Results r)
        {
            r.sb.AppendLine();
            r.sb.AppendLine($"  {r.passed} passed, {r.failed} failed.");
            return r.sb.ToString();
        }
    }
}
