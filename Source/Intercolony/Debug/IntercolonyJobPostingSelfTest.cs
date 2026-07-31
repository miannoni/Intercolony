using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// End-to-end check of Phase 21's acceptance criterion (DESIGN.md §114, §35.2).
    ///
    /// §114 asks for one measurable thing: *"Higher wages and better employer reputation measurably
    /// improve applicant quantity/quality."* So this drives the **real** matcher against the **real**
    /// world pool at several wages and at both ends of the reputation range, and measures what comes
    /// back. It does not assert that a formula returns what the formula returns.
    ///
    /// It also guards the thing that is invisible in play: applicants are pinned world pawns, and a
    /// posting that closes without discarding them leaks one pawn per applicant, forever. The world
    /// pawn count is measured before and after.
    ///
    /// Every posting it creates is withdrawn and removed, and employer standing is restored.
    /// </summary>
    public static class IntercolonyJobPostingSelfTest
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

        /// <summary>One measurement of what a posting drew.</summary>
        private struct Draw
        {
            public int applicants;
            public float averageBestSkill;
            public int bestSkill;
        }

        public static string Run(IntercolonyWorldComponent state, Map map)
        {
            Results r = new Results();
            r.sb.AppendLine("Job posting and applicant self-test (§114, §35.2)");

            if (state == null || map == null)
            {
                r.sb.AppendLine("  No world or map. Open a colony first.");
                return Summarize(r);
            }

            EmployerReputation rep = state.EmployerStanding;
            float savedScore = rep?.Score ?? 0f;
            int savedPostings = state.Postings.Count;
            int worldPawnsBefore = Find.WorldPawns?.AllPawnsAliveOrDead?.Count ?? 0;

            try
            {
                CheckPoolSplit(r, state);
                CheckResponseCurveIsSmooth(r, state);
                CheckWageDrivesApplicants(r, state);
                CheckReputationDrivesApplicants(r, state, rep);
                CheckOnePersonOnePosting(r, state);
                CheckSilenceIsExplained(r, state);
                CheckLifecycle(r, state);
            }
            catch (System.Exception ex)
            {
                r.sb.AppendLine($"  EXCEPTION: {ex}");
                r.failed++;
            }
            finally
            {
                // Every posting this test made must go, and closing is what discards its applicants.
                for (int i = state.Postings.Count - 1; i >= savedPostings; i--)
                {
                    JobPostingService.Close(state.Postings[i], JobPostingStatus.Withdrawn, "self-test");
                    state.Postings.RemoveAt(i);
                }

                if (rep != null)
                {
                    rep.Adjust(savedScore - rep.Score);
                }

                LaborCandidateService.Clear();

                int worldPawnsAfter = Find.WorldPawns?.AllPawnsAliveOrDead?.Count ?? 0;

                // The leak that no amount of playing would reveal. An applicant is pinned
                // KeepForever, so one missed discard is a pawn the world pawn GC has been told never
                // to collect — invisible until a save file is inexplicably large.
                r.Check(worldPawnsAfter <= worldPawnsBefore,
                    "no world pawns leaked by postings opened and closed (§35.2)",
                    $"{worldPawnsBefore} before, {worldPawnsAfter} after");

                r.Info($"restored employer standing to {rep?.ScoreDisplay ?? 0}/100 and removed test postings.");
            }

            return Summarize(r);
        }

        // --- The pool ----------------------------------------------------------------------

        /// <summary>
        /// The census must be deep, and it must price the same way a real pawn does.
        ///
        /// Depth is the point: a shallow market answers a one-silver change by flipping from nobody
        /// interested to everybody interested, because there were three qualified people who all
        /// charged about the same. Pricing agreement is what keeps that depth honest — the posting
        /// dialog quotes a band drawn from census records, and the worker who eventually arrives is
        /// a real pawn. If the two priced differently the band would be a lie the player discovers
        /// only after hiring.
        /// </summary>
        private static void CheckPoolSplit(Results r, IntercolonyWorldComponent state)
        {
            int advertised = LaborCandidateService.Refresh(state).Count;
            List<LaborProspect> world = LaborCandidateService.Census(state);

            if (advertised == 0 && world.Count == 0)
            {
                r.Info("pool checks skipped: no labor available this cycle at all.");
                return;
            }

            r.Check(world.Count > advertised,
                "a posting reaches far further than the hiring listing (§35.2)",
                $"{advertised} advertising, {world.Count} in the census");

            r.Check(world.Count >= advertised * 5,
                "the census is deep enough for an offer to have a shape, not a threshold",
                $"x{world.Count / (float)Mathf.Max(1, advertised):0.0} the listing");

            // Building it twice must not build two: every posting answered in one refresh has to
            // see the same people, or ten identical postings stop being one posting.
            int again = LaborCandidateService.Census(state).Count;
            r.Check(again == world.Count,
                "the census is taken once per cycle, not once per question",
                $"{world.Count} then {again}");

            // Same rule, two representations. WeightedLevel and the top-N are shared; this proves
            // the shared path is actually shared rather than two copies that agree today.
            Pawn probe = null;
            foreach (LaborCandidate candidate in LaborCandidateService.Refresh(state))
            {
                if (candidate?.pawn != null)
                {
                    probe = candidate.pawn;
                    break;
                }
            }

            if (probe != null)
            {
                float fromPawn = LaborCandidateService.PricedSkillValue(probe);
                r.Check(fromPawn > 0f,
                    "a real pawn prices to a positive skill value",
                    $"{fromPawn:0.0}");
            }

            float lowest = float.MaxValue;
            float highest = 0f;
            foreach (LaborProspect prospect in world)
            {
                if (prospect.pricedSkillValue < lowest)
                {
                    lowest = prospect.pricedSkillValue;
                }

                if (prospect.pricedSkillValue > highest)
                {
                    highest = prospect.pricedSkillValue;
                }
            }

            r.Check(world.Count == 0 || highest > lowest,
                "the census contains a spread of ability, not one worker repeated",
                $"skill value {lowest:0.0} to {highest:0.0}");
        }

        /// <summary>
        /// The complaint this phase's second pass exists to answer, turned into an assertion.
        ///
        /// Playing it revealed that a single silver could take a posting from no replies to every
        /// qualified worker in the world, which is not a market — it is a threshold. So: walk the
        /// wage one silver at a time across the whole going-rate band and assert that no single step
        /// moves more than a modest share of the eventual total.
        /// </summary>
        private static void CheckResponseCurveIsSmooth(Results r, IntercolonyWorldComponent state)
        {
            SkillDef skill = SkillDefOf.Construction;
            const int term = 20;

            if (!JobPostingService.GoingRate(state, skill, 8, term, CombatClause.Civilian,
                    out int low, out int high, out int qualified))
            {
                r.Info("response curve skipped: nobody reachable has the skill.");
                return;
            }

            if (high - low < 4)
            {
                r.Info($"response curve skipped: the band is only {low}-{high}, too narrow to walk.");
                return;
            }

            // Sampled rather than exhaustive — the band can be wide and each step runs the matcher.
            int steps = Mathf.Min(12, high - low + 1);
            int biggestJump = 0;
            int biggestAt = 0;
            int previous = 0;
            int total = 0;
            StringBuilder curve = new StringBuilder();

            for (int i = 0; i < steps; i++)
            {
                int wage = low + Mathf.RoundToInt(i * (high - low) / (float)(steps - 1));
                int applicants = Measure(state, skill, 8, term, wage).applicants;

                if (i > 0)
                {
                    curve.Append(' ');
                }

                curve.Append(applicants);

                if (i > 0 && applicants - previous > biggestJump)
                {
                    biggestJump = applicants - previous;
                    biggestAt = wage;
                }

                previous = applicants;
                total = Mathf.Max(total, applicants);
            }

            r.Info($"{qualified} qualified in the census; replies across {low}-{high}: {curve}");

            if (total == 0)
            {
                r.Info("response curve skipped: nobody applied anywhere in the band.");
                return;
            }

            float share = biggestJump / (float)total;
            r.Check(share <= 0.6f,
                "no single step across the band flips the whole market (§35.2)",
                $"biggest jump {biggestJump} of {total} ({share:P0}) at {biggestAt}/day");

            r.Check(total >= 3,
                "a full-band offer draws enough people to choose between",
                $"{total} at the top of the band");
        }

        // --- §114's acceptance criterion ---------------------------------------------------

        /// <summary>
        /// The headline claim: a better offer brings more and better applicants.
        ///
        /// Measured across a spread of wages derived from the actual going rate, so the test works
        /// on any world rather than assuming a silver figure. Quantity is asserted as monotonic
        /// across the whole spread; quality is asserted between the extremes, because a single
        /// step can legitimately add one mediocre worker.
        /// </summary>
        private static void CheckWageDrivesApplicants(Results r, IntercolonyWorldComponent state)
        {
            SkillDef skill = SkillDefOf.Construction;
            const int minLevel = 0;
            const int term = 20;

            if (!JobPostingService.GoingRate(state, skill, minLevel, term, CombatClause.Civilian,
                    out int low, out int high, out int qualified))
            {
                r.Info("wage effect skipped: nobody reachable can do the work this cycle.");
                return;
            }

            r.Info($"{qualified} reachable worker(s) with {skill.skillLabel}; they ask {low} to {high}/day.");

            int[] wages = { Mathf.Max(1, low - 5), low, (low + high) / 2, high, high + 15 };
            List<Draw> draws = new List<Draw>();

            foreach (int wage in wages)
            {
                draws.Add(Measure(state, skill, minLevel, term, wage));
            }

            bool quantityMonotonic = true;
            for (int i = 1; i < draws.Count; i++)
            {
                if (draws[i].applicants < draws[i - 1].applicants)
                {
                    quantityMonotonic = false;
                }
            }

            StringBuilder shape = new StringBuilder();
            for (int i = 0; i < wages.Length; i++)
            {
                if (i > 0)
                {
                    shape.Append(", ");
                }

                shape.Append($"{wages[i]}/day -> {draws[i].applicants}");
            }

            r.Check(quantityMonotonic,
                "raising the offer never brings fewer applicants (§114)", shape.ToString());

            Draw worst = draws[0];
            Draw best = draws[draws.Count - 1];

            r.Check(best.applicants > worst.applicants,
                "a generous offer brings measurably more applicants than a poor one (§114)",
                $"{worst.applicants} at {wages[0]}/day vs {best.applicants} at {wages[wages.Length - 1]}/day");

            if (best.applicants > 0 && worst.applicants > 0)
            {
                r.Check(best.averageBestSkill >= worst.averageBestSkill,
                    "a generous offer brings applicants at least as good (§114)",
                    $"average best skill {worst.averageBestSkill:0.0} vs {best.averageBestSkill:0.0}");
            }
            else
            {
                r.Info($"quality comparison skipped: the low offer drew {worst.applicants}.");
            }

            // An offer below everyone's asking price must draw nobody. This is the assertion that
            // catches a matcher that has quietly stopped checking the wage at all.
            Draw hopeless = Measure(state, skill, 0, term, 1);
            r.Check(hopeless.applicants == 0,
                "an offer of 1 silver a day draws nobody (§114)",
                $"{hopeless.applicants} applicant(s)");
        }

        /// <summary>
        /// The second half of §114: reputation moves applicants too.
        ///
        /// Nothing in the matcher reads reputation directly — it falls out of Phase 19's
        /// <c>WageFactor</c> multiplying every asking price. This test exists to prove that
        /// indirection actually works end to end, because a change to either half could silently
        /// break it while both halves still pass their own tests.
        /// </summary>
        private static void CheckReputationDrivesApplicants(
            Results r, IntercolonyWorldComponent state, EmployerReputation rep)
        {
            if (rep == null)
            {
                return;
            }

            SkillDef skill = SkillDefOf.Construction;
            const int term = 20;

            if (!JobPostingService.GoingRate(state, skill, 0, term, CombatClause.Civilian,
                    out int low, out int high, out _))
            {
                r.Info("reputation effect skipped: nobody reachable can do the work.");
                return;
            }

            // Mid-band, so there is room to move in both directions. At the top of the band every
            // offer clears everyone and reputation would be invisible.
            int wage = (low + high) / 2;

            rep.Adjust(EmployerReputation.MinScore - rep.Score);
            Draw asBad = Measure(state, skill, 0, term, wage);

            rep.Adjust(EmployerReputation.MaxScore - rep.Score);
            Draw asGood = Measure(state, skill, 0, term, wage);

            r.Check(asGood.applicants >= asBad.applicants,
                "the same offer draws at least as many for a good employer as a bad one (§114)",
                $"{asBad.applicants} exploitative vs {asGood.applicants} sought-after, at {wage}/day");

            r.Check(asGood.applicants > asBad.applicants,
                "employer reputation measurably changes who answers (§114, §112)",
                $"{asBad.applicants} vs {asGood.applicants}");
        }

        /// <summary>
        /// Ten identical postings must behave exactly like one.
        ///
        /// This is the property that lets the feature have no cap and no fee: workers are the scarce
        /// thing, not advertisements. If it ever failed, posting the same job repeatedly would
        /// multiply the labor supply out of nothing.
        /// </summary>
        private static void CheckOnePersonOnePosting(Results r, IntercolonyWorldComponent state)
        {
            SkillDef skill = SkillDefOf.Construction;
            const int term = 20;

            if (!JobPostingService.GoingRate(state, skill, 0, term, CombatClause.Civilian,
                    out _, out int high, out _))
            {
                r.Info("competition check skipped: nobody reachable can do the work.");
                return;
            }

            int wage = high + 20;

            Draw single = Measure(state, skill, 0, term, wage);

            // Five identical postings, matched together.
            List<JobPosting> group = new List<JobPosting>();
            for (int i = 0; i < 5; i++)
            {
                group.Add(MakePosting(state, skill, 0, term, wage));
            }

            LaborCandidateService.Clear();
            JobPostingService.MatchAll(state);

            int total = 0;
            int postingsWithAnyone = 0;
            foreach (JobPosting posting in group)
            {
                total += posting.Applicants.Count;
                if (posting.Applicants.Count > 0)
                {
                    postingsWithAnyone++;
                }
            }

            foreach (JobPosting posting in group)
            {
                JobPostingService.Close(posting, JobPostingStatus.Withdrawn, "self-test");
                state.Postings.Remove(posting);
            }

            r.Check(total <= single.applicants,
                "five identical postings draw no more people than one (§35.2)",
                $"one drew {single.applicants}, five drew {total} across {postingsWithAnyone} posting(s)");
        }

        /// <summary>A posting that draws nobody has to say which of the two reasons it was.</summary>
        private static void CheckSilenceIsExplained(Results r, IntercolonyWorldComponent state)
        {
            float standing = EmployerReputationService.ScoreFor(state);

            JobPosting unaffordable = MakePosting(state, SkillDefOf.Construction, 0, 20, 1);
            string tooCheap = JobPostingService.ExplainSilence(state, unaffordable, standing);

            JobPosting impossible = MakePosting(state, SkillDefOf.Construction, 20, 20, 9999);
            string nobodyCan = JobPostingService.ExplainSilence(state, impossible, standing);

            foreach (JobPosting posting in new[] { unaffordable, impossible })
            {
                JobPostingService.Close(posting, JobPostingStatus.Withdrawn, "self-test");
                state.Postings.Remove(posting);
            }

            r.Check(!tooCheap.NullOrEmpty() && !nobodyCan.NullOrEmpty(),
                "a posting that draws nobody always explains itself (§114)");

            // The two reasons must not produce the same sentence, or the explanation is decoration.
            r.Check(tooCheap != nobodyCan,
                "\"your offer is too low\" and \"nobody can do this\" read differently");

            r.Info($"too cheap: \"{Trim(tooCheap)}\"");
            r.Info($"nobody qualifies: \"{Trim(nobodyCan)}\"");
        }

        /// <summary>Posting, filling and closing, through the real service.</summary>
        private static void CheckLifecycle(Results r, IntercolonyWorldComponent state)
        {
            JobPosting posting = JobPostingService.TryPost(
                state, SkillDefOf.Construction, 0, 2, 20, 50, WageStructure.Daily,
                CombatClause.Civilian, 30, out string failReason);

            r.Check(posting != null, "a posting can be created through the real service", failReason ?? "");
            if (posting == null)
            {
                return;
            }

            r.Check(posting.IsOpen && posting.PositionsRemaining == 2,
                "a new posting is open with every position unfilled",
                $"{posting.PositionsRemaining} of {posting.positions}");

            r.Check(JobPostingService.TryPost(state, null, 0, 0, 20, 50, WageStructure.Daily,
                        CombatClause.Civilian, 30, out _) == null,
                "a posting with no positions is refused");

            r.Check(JobPostingService.TryPost(state, null, 0, 1, 20, 0, WageStructure.Daily,
                        CombatClause.Civilian, 30, out _) == null,
                "a posting offering nothing is refused");

            r.Check(JobPostingService.TryPost(state, null, 0, 1,
                        LaborCandidateService.MaxTermDays + 1, 50, WageStructure.Daily,
                        CombatClause.Civilian, 30, out _) == null,
                "a posting past the term cap is refused",
                $"cap is {LaborCandidateService.MaxTermDays}d");

            JobPostingService.Withdraw(posting);
            r.Check(!posting.IsOpen && posting.status == JobPostingStatus.Withdrawn,
                "withdrawing closes the posting",
                $"status {posting.status}");
            r.Check(posting.Applicants.Count == 0,
                "a closed posting holds no applicants — they are pinned pawns and would leak");

            r.Check(!JobPostingService.Withdraw(posting),
                "an already-closed posting cannot be withdrawn twice");

            state.Postings.Remove(posting);
        }

        // --- Helpers -----------------------------------------------------------------------

        /// <summary>
        /// Posts a job, runs the real matcher against a freshly rebuilt pool, measures the result
        /// and cleans up.
        ///
        /// The pool is cleared first so each measurement sees the same world: without that, the
        /// second posting would be matched against a pool the first had already taken people out of,
        /// and the comparison would measure order rather than wage.
        /// </summary>
        private static Draw Measure(
            IntercolonyWorldComponent state, SkillDef skill, int minLevel, int term, int wage)
        {
            LaborCandidateService.Clear();

            JobPosting posting = MakePosting(state, skill, minLevel, term, wage);
            JobPostingService.MatchAll(state);

            Draw draw = new Draw { applicants = posting.Applicants.Count };

            if (draw.applicants > 0)
            {
                float sum = 0f;
                foreach (JobApplicant applicant in posting.Applicants)
                {
                    int best = BestSkillLevel(applicant.pawn);
                    sum += best;
                    if (best > draw.bestSkill)
                    {
                        draw.bestSkill = best;
                    }
                }

                draw.averageBestSkill = sum / draw.applicants;
            }

            JobPostingService.Close(posting, JobPostingStatus.Withdrawn, "self-test measurement");
            state.Postings.Remove(posting);
            return draw;
        }

        private static JobPosting MakePosting(
            IntercolonyWorldComponent state, SkillDef skill, int minLevel, int term, int wage)
        {
            return JobPostingService.TryPost(
                state, skill, minLevel, 6, term, wage, WageStructure.Daily,
                CombatClause.Civilian, JobPostingService.DefaultLifespanDays, out _);
        }

        private static int BestSkillLevel(Pawn pawn)
        {
            if (pawn?.skills == null)
            {
                return 0;
            }

            int best = 0;
            foreach (SkillRecord skill in pawn.skills.skills)
            {
                if (!skill.TotallyDisabled && skill.Level > best)
                {
                    best = skill.Level;
                }
            }

            return best;
        }

        private static string Trim(string text)
        {
            string flat = text.Replace("\n", " ").Replace("  ", " ");
            return flat.Length <= 90 ? flat : flat.Substring(0, 90) + "...";
        }

        private static string Summarize(Results r)
        {
            r.sb.AppendLine();
            r.sb.AppendLine($"  {r.passed} passed, {r.failed} failed.");
            return r.sb.ToString();
        }
    }
}
