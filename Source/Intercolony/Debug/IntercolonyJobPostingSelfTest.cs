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
    /// §114 asks for one measurable thing: *"Requirements and better employer reputation measurably
    /// improve applicant quantity/quality."* So this drives the **real** matcher against the **real**
    /// world pool at several requirements and at both ends of the reputation range, and measures what
    /// comes back. It does not assert that a formula returns what the formula returns.
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
                sb.AppendLine($"  SKIPPED  {label}  ({detail})");
            }
        }

        /// <summary>One measurement of what a posting drew.</summary>
        private struct Draw
        {
            /// <summary>Workers meeting the requirement — the market's unbounded answer.</summary>
            public int interested;

            /// <summary>Workers actually queued, which the applicant cap truncates.</summary>
            public int applicants;

            public float averageBestSkill;
            public int bestSkill;
        }

        private struct ApplicantValues
        {
            public int settlementId;
            public string settlementName;
            public string factionName;
            public int travelDays;
            public int openMarketAsk;
            public string skillLevels;
        }

        private struct ApplicantDraw
        {
            public int qualified;
            public List<ApplicantValues> values;
            public bool fixtureBuilt;
        }

        private const float WaitingListSpreadMargin = 1f;

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
            int savedEmployments = state.Employments.Count;
            int savedLedger = state.Ledger.Count;
            int savedLedgerStartTick = state.LedgerStartTick;
            int worldPawnsBefore = Find.WorldPawns?.AllPawnsAliveOrDead?.Count ?? 0;

            IntercolonyLaborSelfTestSupport.ResetLedger();

            try
            {
                CheckPoolSplit(r, state);
                CheckRequirementsDriveApplicants(r, state);
                CheckWaitingListIsSpread(r, state);
                CheckMarketReproduces(r, state);
                CheckReputationDrivesApplicants(r, state, rep);
                CheckReputationDrivesCandidateQuality(r, state, rep);
                CheckOnePersonOnePosting(r, state);
                CheckSilenceIsExplained(r, state);
                CheckLifecycle(r, state);
                CheckApplicantOwnAsk(r, state, map);
            }
            catch (System.Exception ex)
            {
                r.sb.AppendLine($"  EXCEPTION: {ex}");
                r.failed++;
            }
            finally
            {
                // A successful applicant hire creates a travelling employment rather than a
                // posting-owned pawn. End and remove any test employment before the outer leak
                // check, even if the hire assertion itself threw during cleanup.
                for (int i = state.Employments.Count - 1; i >= savedEmployments; i--)
                {
                    EmploymentContract contract = state.Employments[i];
                    if (contract?.IsOpen == true)
                    {
                        EmploymentService.End(contract, EmploymentStatus.Failed, "self-test cleanup");
                    }

                    state.Employments.RemoveAt(i);
                }

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

                int returned = IntercolonyLaborSelfTestSupport.RestoreLedger(map);
                if (returned > 0)
                {
                    r.Info($"returned {returned} silver the test had consumed.");
                }

                while (state.Ledger.Count > savedLedger)
                {
                    state.Ledger.RemoveAt(state.Ledger.Count - 1);
                }

                state.LedgerStartTick = savedLedgerStartTick;

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

        // --- §114's acceptance criterion ---------------------------------------------------

        /// <summary>
        /// The requirement claim: a higher skill bar brings fewer but better applicants, while the
        /// saved posted wage does not alter the market's answer.
        ///
        /// Every draw goes through the real posting service. The unbounded interested count reports
        /// the market shape; the queued applicants prove that the matcher applied the same
        /// requirement before its six-person waiting-list cap.
        /// </summary>
        private static void CheckRequirementsDriveApplicants(
            Results r, IntercolonyWorldComponent state)
        {
            SkillDef skill = SkillDefOf.Construction;
            const int term = 20;
            const int probeWage = 9999;
            int[] minimums = { 0, 4, 8, 12, 16, 20 };
            List<Draw> draws = new List<Draw>();

            foreach (int minimum in minimums)
            {
                draws.Add(Measure(state, skill, minimum, term, probeWage));
            }

            StringBuilder shape = new StringBuilder();
            for (int i = 0; i < minimums.Length; i++)
            {
                if (i > 0)
                {
                    shape.Append(", ");
                }

                shape.Append($"{minimums[i]}+ -> {draws[i].interested} interested, " +
                             $"{draws[i].applicants} queued");
            }

            Draw noMinimum = draws[0];
            Draw demanding = draws[draws.Count - 1];
            if (noMinimum.interested == 0)
            {
                r.Skip("minimum skill drives applicant quantity (§114)",
                    $"no-minimum draw was empty: {shape}");
            }
            else
            {
                bool quantityMonotonic = true;
                for (int i = 1; i < draws.Count; i++)
                {
                    if (draws[i].interested > draws[i - 1].interested)
                    {
                        quantityMonotonic = false;
                    }
                }

                bool materiallyFewer = demanding.interested < noMinimum.interested &&
                                       demanding.interested * 2 < noMinimum.interested;
                bool matcherAppliedBar = demanding.applicants < noMinimum.applicants;

                r.Check(quantityMonotonic && materiallyFewer && matcherAppliedBar,
                    "a higher skill minimum reaches no more workers and a demanding minimum reaches materially fewer (§114)",
                    $"{shape}; no minimum queued {noMinimum.applicants}, demanding {demanding.applicants}");
            }

            const int highMinimum = 16;
            Draw unfiltered = draws[0];
            Draw highMinimumDraw = Measure(state, skill, highMinimum, term, probeWage);
            if (unfiltered.applicants == 0 || highMinimumDraw.applicants == 0)
            {
                string emptyDraw = unfiltered.applicants == 0 && highMinimumDraw.applicants == 0
                    ? "both the no-minimum and high-minimum draws were empty"
                    : unfiltered.applicants == 0
                        ? "the no-minimum draw was empty"
                        : "the high-minimum draw was empty";
                r.Skip("a high skill minimum yields better applicants (§114)",
                    $"{emptyDraw}; no minimum {unfiltered.interested} interested, " +
                    $"{unfiltered.applicants} queued; {highMinimum}+ " +
                    $"{highMinimumDraw.interested} interested, {highMinimumDraw.applicants} queued");
            }
            else
            {
                r.Check(highMinimumDraw.averageBestSkill > unfiltered.averageBestSkill,
                    "a high skill minimum yields better applicants (§114)",
                    $"average best skill {unfiltered.averageBestSkill:0.0} at 0+ vs " +
                    $"{highMinimumDraw.averageBestSkill:0.0} at {highMinimum}+");
            }

            const int lowWage = 1;
            const int highWage = 10000;
            int qualified = JobPostingService.CountInterested(
                state, skill, 0, term, lowWage, CombatClause.Civilian);
            if (qualified == 0)
            {
                r.Skip("posted wage does not change interested-worker count (§114)",
                    $"the no-minimum requirement had {qualified} interested workers to compare");
                return;
            }

            Draw lowOffer = Measure(state, skill, 0, term, lowWage);
            Draw highOffer = Measure(state, skill, 0, term, highWage);
            r.Check(lowOffer.interested == highOffer.interested &&
                    lowOffer.applicants > 0 && lowOffer.applicants == highOffer.applicants,
                "posted wage does not change interested-worker count (§114)",
                $"{lowWage}/day -> {lowOffer.interested} interested, {lowOffer.applicants} queued; " +
                $"{highWage}/day -> {highOffer.interested} interested, {highOffer.applicants} queued");
        }

        /// <summary>
        /// F25's queue is intentionally a spread of the qualified pool, not a leaderboard. The
        /// assertion reads the applicants that MatchAll actually materialised and compares their
        /// whole-queue mean with the mean of the same pool's strongest six records.
        /// </summary>
        private static void CheckWaitingListIsSpread(Results r, IntercolonyWorldComponent state)
        {
            const int term = 20;
            const int probeWage = 1;
            int cap = JobPostingService.MaxWaitingApplicants;

            if (Find.WorldPawns == null)
            {
                r.Skip("the waiting list is a spread, not a leaderboard (§35.2)",
                    "Find.WorldPawns was null, so the real applicant path could not run");
                return;
            }

            LaborCandidateService.Clear();
            JobPosting posting = MakePosting(
                state, SkillDefOf.Construction, 0, term, probeWage);
            if (posting == null)
            {
                r.Skip("the waiting list is a spread, not a leaderboard (§35.2)",
                    "the real posting fixture could not be created");
                return;
            }

            try
            {
                List<LaborProspect> census = LaborCandidateService.Census(state);
                List<LaborProspect> qualified = new List<LaborProspect>();
                foreach (LaborProspect prospect in census)
                {
                    if (prospect != null && posting.MeetsRequirement(prospect))
                    {
                        qualified.Add(prospect);
                    }
                }

                JobPostingService.MatchAll(state);

                if (qualified.Count <= cap)
                {
                    r.Skip("the waiting list is a spread, not a leaderboard (§35.2)",
                        $"qualified pool had {qualified.Count} records; more than cap {cap} is needed");
                    return;
                }

                if (posting.Applicants.Count != cap)
                {
                    r.Skip("the waiting list is a spread, not a leaderboard (§35.2)",
                        $"real matcher queued {posting.Applicants.Count} of cap {cap}; " +
                        $"qualified pool had {qualified.Count}");
                    return;
                }

                List<int> qualifiedBestSkills = new List<int>();
                float queuedTotal = 0f;
                bool allApplicantsHavePawns = true;
                foreach (LaborProspect prospect in qualified)
                {
                    qualifiedBestSkills.Add(BestSkillLevel(prospect));
                }

                foreach (JobApplicant applicant in posting.Applicants)
                {
                    if (applicant?.pawn == null)
                    {
                        allApplicantsHavePawns = false;
                        break;
                    }

                    queuedTotal += BestSkillLevel(applicant.pawn);
                }

                if (!allApplicantsHavePawns)
                {
                    r.Skip("the waiting list is a spread, not a leaderboard (§35.2)",
                        $"one of {posting.Applicants.Count} queued applicants had no pawn to measure");
                    return;
                }

                qualifiedBestSkills.Sort((a, b) => b.CompareTo(a));
                float topTotal = 0f;
                for (int i = 0; i < cap; i++)
                {
                    topTotal += qualifiedBestSkills[i];
                }

                float queuedMean = queuedTotal / posting.Applicants.Count;
                float topMean = topTotal / cap;
                float gap = topMean - queuedMean;

                r.Check(gap >= WaitingListSpreadMargin,
                    "the waiting list is a spread, not the strongest workers alive (§35.2, F25)",
                    $"queued mean best skill {queuedMean:0.00}; top {cap} qualified mean " +
                    $"{topMean:0.00}; gap {gap:0.00}; margin {WaitingListSpreadMargin:0.00}; " +
                    $"qualified {qualified.Count}, queued {posting.Applicants.Count}");
            }
            finally
            {
                JobPostingService.Close(posting, JobPostingStatus.Withdrawn, "self-test spread");
                state.Postings.Remove(posting);
            }
        }

        /// <summary>
        /// Rebuilds the same refresh twice and compares the selected applicants' market attributes
        /// in order. This proves the draw is a pure function of EconomySeed and RefreshCount. It
        /// does not prove a Scribe round-trip, and it does not prove pawn identity: pawn
        /// materialisation is outside the seeded stream, so the same inputs regenerate the same
        /// records, not necessarily the same people. The comparison uses attributes rather than
        /// record identity because records sharing all of those market attributes are
        /// interchangeable to the market.
        /// </summary>
        private static void CheckMarketReproduces(Results r, IntercolonyWorldComponent state)
        {
            const int term = 20;
            const int probeWage = 1;
            int cap = JobPostingService.MaxWaitingApplicants;
            const string label = "the same seed and refresh count reproduce the same applicants " +
                                 "in the same order (§35.2, F25)";

            if (Find.WorldPawns == null)
            {
                r.Skip(label, "Find.WorldPawns was null, so the real applicant path could not run");
                return;
            }

            ApplicantDraw first = new ApplicantDraw
            {
                values = new List<ApplicantValues>()
            };

            // Matching generates pawns, and pawn generation draws from the current RNG frame,
            // so the stream is expected to move; "it came back to where it was" is not a true
            // statement about correct code.
            Rand.PushState(0x7F25_2D);
            try
            {
                first = CaptureApplicants(state, SkillDefOf.Construction, 0, term, probeWage);
            }
            finally
            {
                Rand.PopState();
            }

            if (!first.fixtureBuilt)
            {
                r.Skip(label,
                    $"seed {state.EconomySeed}, refresh {state.RefreshCount}; " +
                    "the real posting fixture could not be created");
                return;
            }

            if (first.values == null || first.values.Count == 0)
            {
                r.Skip(label,
                    $"seed {state.EconomySeed}, refresh {state.RefreshCount}; " +
                    $"the first real draw was empty with {first.qualified} qualified records");
                return;
            }

            if (first.qualified <= cap || first.values.Count != cap)
            {
                r.Skip(label,
                    $"seed {state.EconomySeed}, refresh {state.RefreshCount}; first real draw had " +
                    $"{first.values.Count} queued from " +
                    $"{first.qualified} qualified records; need {cap} queued and more than {cap} " +
                    "qualified to compare a full capped market");
                return;
            }

            ApplicantDraw second = CaptureApplicants(
                state, SkillDefOf.Construction, 0, term, probeWage);

            if (!second.fixtureBuilt)
            {
                r.Skip(label,
                    $"seed {state.EconomySeed}, refresh {state.RefreshCount}; " +
                    "the second real posting fixture could not be created");
                return;
            }

            if (second.values == null || second.values.Count == 0)
            {
                r.Skip(label,
                    $"seed {state.EconomySeed}, refresh {state.RefreshCount}; " +
                    $"the second real draw was empty with {second.qualified} qualified records");
                return;
            }

            bool sameSet = SameApplicantSet(first.values, second.values);
            bool sameOrder = SameApplicantOrder(first.values, second.values);

            r.Check(sameSet && sameOrder,
                label,
                $"seed {state.EconomySeed}, refresh {state.RefreshCount}; " +
                $"set {(sameSet ? "same" : "different")}; order {(sameOrder ? "same" : "different")}; " +
                $"first differing position {FirstApplicantDifference(first.values, second.values)}");
        }

        /// <summary>F25's contract rate must carry the quote the applicant brought with them.</summary>
        private static void CheckApplicantOwnAsk(
            Results r, IntercolonyWorldComponent state, Map map)
        {
            const int term = 20;
            const int postedWage = 1;
            const string label = "a hired applicant is paid their own ask (§35.2, F25)";

            if (map == null)
            {
                r.Skip(label, "the current map was null, so colony payment could not be staged");
                return;
            }

            if (Find.WorldPawns == null)
            {
                r.Skip(label, "Find.WorldPawns was null, so a travelling hire could not be cleaned up");
                return;
            }

            int savedSilver = PurchaseOrderService.CountColonySilver(map);
            LaborCandidateService.Clear();
            JobPosting posting = MakePosting(
                state, SkillDefOf.Construction, 0, term, postedWage);
            JobApplicant applicant = null;
            EmploymentContract contract = null;

            try
            {
                if (posting == null)
                {
                    r.Skip(label, "the real posting fixture could not be created");
                    return;
                }

                JobPostingService.MatchAll(state);
                if (posting.Applicants.Count == 0)
                {
                    r.Skip(label, "the real posting produced no applicant to hire");
                    return;
                }

                applicant = posting.Applicants[0];
                if (applicant == null || applicant.pawn == null)
                {
                    r.Skip(label, "the first queued applicant had no pawn to pass through hiring");
                    return;
                }

                int applicantAsk = applicant.openMarketAsk;
                if (applicantAsk == postedWage)
                {
                    r.Skip(label,
                        $"the fixture applicant happened to ask the deliberately low posted wage " +
                        $"of {postedWage}/day");
                    return;
                }

                // This calculation only stages enough silver for the real payment path. The
                // expected contract rate below is read directly from applicant.openMarketAsk.
                int upFront = WageStructureUtility.UpFrontCost(
                    posting.wageStructure, applicantAsk, posting.termDays);
                IntercolonyLaborSelfTestSupport.EnsureSilver(map, upFront);
                int available = PurchaseOrderService.CountColonySilver(map);
                if (available < upFront)
                {
                    r.Skip(label,
                        $"could not stage the up-front cost: {available} silver available, " +
                        $"{upFront} needed for the applicant ask of {applicantAsk}/day");
                    return;
                }

                contract = EmploymentService.TryHireApplicant(
                    state, applicant, posting, map, out string failReason);
                if (contract == null)
                {
                    r.Skip(label,
                        $"the real applicant hire could not be arranged: {failReason ?? "no reason"}");
                    return;
                }

                r.Check(contract.dailyWage == applicantAsk,
                    label,
                    $"posted {postedWage}/day; applicant ask {applicantAsk}/day; " +
                    $"contract daily wage {contract.dailyWage}/day");
            }
            finally
            {
                if (posting != null && applicant != null)
                {
                    posting.Applicants.Remove(applicant);
                }

                if (contract != null)
                {
                    EmploymentService.End(contract, EmploymentStatus.Failed, "self-test hire");
                    state.Employments.Remove(contract);
                }
                else
                {
                    applicant?.Discard();
                }

                if (posting != null)
                {
                    JobPostingService.Close(posting, JobPostingStatus.Withdrawn, "self-test hire");
                    state.Postings.Remove(posting);
                }

                int silverAfter = PurchaseOrderService.CountColonySilver(map);
                if (silverAfter < savedSilver)
                {
                    int returned = IntercolonyLaborSelfTestSupport.EnsureSilver(
                        map, savedSilver);
                    if (returned > 0)
                    {
                        r.Info($"returned {returned} silver to restore the hire fixture.");
                    }
                }
                else if (silverAfter > savedSilver)
                {
                    PurchaseOrderService.TryTakeSilver(map, silverAfter - savedSilver);
                }

                // The amount was restored explicitly above, so do not let this fixture's silver
                // bookkeeping affect the outer labor/payroll self-tests.
                IntercolonyLaborSelfTestSupport.ResetLedger();
            }
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

            // Reach rather than queue length, for the same reason as the response curve: the queue
            // caps out and would report a sought-after employer and an exploitative one as equal.
            r.Check(asGood.interested >= asBad.interested,
                "the same offer reaches at least as many for a good employer as a bad one (§114)",
                $"{asBad.interested} exploitative vs {asGood.interested} sought-after, at {wage}/day");

            r.Check(asGood.interested > asBad.interested,
                "employer reputation measurably changes who will take the job (§114, §112)",
                $"{asBad.interested} vs {asGood.interested}");
        }

        /// <summary>
        /// The quality half of §114: a bad employer sees fewer prospects and worse ones, while a
        /// middle-standing colony keeps the ordinary one-draw generation cost.
        /// </summary>
        private static void CheckReputationDrivesCandidateQuality(
            Results r, IntercolonyWorldComponent state, EmployerReputation rep)
        {
            if (rep == null)
            {
                return;
            }

            rep.Adjust(EmployerReputation.MinScore - rep.Score);
            LaborCandidateService.InvalidateCensus();
            List<LaborProspect> atMinimum = new List<LaborProspect>(
                LaborCandidateService.Census(state));
            int minimumCount = atMinimum.Count;
            float minimumMeanBestSkill = MeanBestProspectSkill(atMinimum);
            int minimumDraws = LaborCandidateService.CensusProspectDraws;

            rep.Adjust(EmployerReputation.MaxScore - rep.Score);
            LaborCandidateService.InvalidateCensus();
            List<LaborProspect> atMaximum = new List<LaborProspect>(
                LaborCandidateService.Census(state));
            int maximumCount = atMaximum.Count;
            float maximumMeanBestSkill = MeanBestProspectSkill(atMaximum);
            int maximumDraws = LaborCandidateService.CensusProspectDraws;

            if (minimumCount == 0 && maximumCount == 0)
            {
                r.Info("reputation quality skipped: the census is empty at both standings.");
                LaborCandidateService.InvalidateCensus();
                return;
            }

            float middle = (EmployerReputation.MinScore + EmployerReputation.MaxScore) / 2;
            rep.Adjust(middle - rep.Score);
            LaborCandidateService.InvalidateCensus();
            List<LaborProspect> atMiddle = new List<LaborProspect>(
                LaborCandidateService.Census(state));
            int middleCount = atMiddle.Count;
            float middleMeanBestSkill = MeanBestProspectSkill(atMiddle);
            int middleDraws = LaborCandidateService.CensusProspectDraws;
            int middleBias = EmployerReputationService.CandidateQualityBias(middle);

            // This fails if GenerateProspectBiased ignores its bias (returning the first draw
            // always), or if the keep-better/keep-worse comparison is inverted.
            r.Check(maximumMeanBestSkill >= minimumMeanBestSkill + 0.5f,
                "a bad employer's census prospects are measurably worse than a good one's (§114)",
                $"mean best skill {minimumMeanBestSkill:0.00} at MinScore vs " +
                $"{maximumMeanBestSkill:0.00} at MaxScore; records {minimumCount} at MinScore vs " +
                $"{maximumCount} at MaxScore");

            // This fails if EnsureCensus drops AvailabilityFactor from perSettlement; the quality
            // half must not replace or weaken the volume half.
            r.Check(maximumCount > minimumCount,
                "a bad employer's job-posting census is smaller than a good one's (§114)",
                $"records {minimumCount} at MinScore vs {maximumCount} at MaxScore");

            if (middleBias == 0)
            {
                // This fails if the biased path draws twice when bias is 0, making an ordinary
                // colony pay an unnecessary generation cost, or draws once when bias is nonzero,
                // making the bias inert.
                r.Check(middleDraws == middleCount && minimumDraws == 2 * minimumCount,
                    "census generation draws once at neutral standing and twice for a bad employer (§35.2)",
                    $"middle: {middleDraws} draws for {middleCount} records " +
                    $"(mean best skill {middleMeanBestSkill:0.00}); " +
                    $"MinScore: {minimumDraws} draws for {minimumCount} records; " +
                    $"MaxScore: {maximumDraws} draws for {maximumCount} records");
            }
            else
            {
                r.Info($"census draw-count check skipped: middle standing {middle:0.##} has " +
                       $"candidate quality bias {middleBias}, not 0.");
            }

            // Leave no live census from the temporary minimum, maximum, or middle standing for
            // the rest of the game; the suite restores the reputation in its outer teardown.
            LaborCandidateService.InvalidateCensus();
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

            r.Check(postingsWithAnyone <= 1,
                "identical postings do not each collect their own queue (§35.2)",
                $"{postingsWithAnyone} of 5 drew anyone");
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
                state, SkillDefOf.Construction, 0, 20, 50, WageStructure.Daily,
                CombatClause.Civilian, out string failReason);

            r.Check(posting != null, "a posting can be created through the real service", failReason ?? "");
            if (posting == null)
            {
                return;
            }

            r.Check(posting.IsOpen,
                "a new posting is open",
                $"status {posting.status}");
            r.Check(posting.NeverExpires && posting.ExpiryLabel == "stays up until filled",
                "a new posting never expires and describes its lifespan without formatting the sentinel",
                $"never expires: {posting.NeverExpires}, label \"{posting.ExpiryLabel}\"");

            r.Check(JobPostingService.TryPost(state, null, 0, 20, 0, WageStructure.Daily,
                        CombatClause.Civilian, out _) == null,
                "a posting offering nothing is refused");

            r.Check(JobPostingService.TryPost(state, null, 0,
                        LaborCandidateService.MaxTermDays + 1, 50, WageStructure.Daily,
                        CombatClause.Civilian, out _) == null,
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

        private static ApplicantDraw CaptureApplicants(
            IntercolonyWorldComponent state, SkillDef skill, int minLevel, int term, int wage)
        {
            ApplicantDraw draw = new ApplicantDraw
            {
                values = new List<ApplicantValues>()
            };

            LaborCandidateService.Clear();
            JobPosting posting = MakePosting(state, skill, minLevel, term, wage);
            if (posting == null)
            {
                return draw;
            }

            draw.fixtureBuilt = true;
            try
            {
                draw.qualified = JobPostingService.CountInterested(
                    state, skill, minLevel, term, wage, CombatClause.Civilian);
                JobPostingService.MatchAll(state);

                foreach (JobApplicant applicant in posting.Applicants)
                {
                    draw.values.Add(CaptureApplicantValues(applicant));
                }
            }
            finally
            {
                JobPostingService.Close(posting, JobPostingStatus.Withdrawn, "self-test reproduction");
                state.Postings.Remove(posting);
            }

            return draw;
        }

        private static ApplicantValues CaptureApplicantValues(JobApplicant applicant)
        {
            ApplicantValues values = new ApplicantValues
            {
                settlementId = applicant?.settlementId ?? -1,
                settlementName = applicant?.settlementName ?? "null",
                factionName = applicant?.factionName ?? "null",
                travelDays = applicant?.travelDays ?? -1,
                openMarketAsk = applicant?.openMarketAsk ?? -1,
                skillLevels = "none"
            };

            if (applicant?.pawn?.skills?.skills == null)
            {
                return values;
            }

            StringBuilder skills = new StringBuilder();
            foreach (SkillRecord skill in applicant.pawn.skills.skills)
            {
                if (skills.Length > 0)
                {
                    skills.Append(',');
                }

                skills.Append(skill.def.index).Append('=')
                      .Append(skill.TotallyDisabled ? -1 : skill.Level);
            }

            values.skillLevels = skills.ToString();
            return values;
        }

        private static bool SameApplicant(
            ApplicantValues first, ApplicantValues second)
        {
            return first.settlementId == second.settlementId &&
                   first.settlementName == second.settlementName &&
                   first.factionName == second.factionName &&
                   first.travelDays == second.travelDays &&
                   first.openMarketAsk == second.openMarketAsk &&
                   first.skillLevels == second.skillLevels;
        }

        private static bool SameApplicantSet(
            List<ApplicantValues> first, List<ApplicantValues> second)
        {
            if (first == null || second == null || first.Count != second.Count)
            {
                return false;
            }

            bool[] matched = new bool[second.Count];
            for (int i = 0; i < first.Count; i++)
            {
                bool found = false;
                for (int j = 0; j < second.Count; j++)
                {
                    if (!matched[j] && SameApplicant(first[i], second[j]))
                    {
                        matched[j] = true;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SameApplicantOrder(
            List<ApplicantValues> first, List<ApplicantValues> second)
        {
            if (first == null || second == null || first.Count != second.Count)
            {
                return false;
            }

            for (int i = 0; i < first.Count; i++)
            {
                if (!SameApplicant(first[i], second[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static string FirstApplicantDifference(
            List<ApplicantValues> first, List<ApplicantValues> second)
        {
            int firstCount = first?.Count ?? 0;
            int secondCount = second?.Count ?? 0;
            int count = firstCount > secondCount ? firstCount : secondCount;

            for (int i = 0; i < count; i++)
            {
                bool hasFirst = i < firstCount;
                bool hasSecond = i < secondCount;
                if (!hasFirst || !hasSecond || !SameApplicant(first[i], second[i]))
                {
                    return $"{i}: first {(hasFirst ? FormatApplicant(first[i]) : "absent")}; " +
                           $"second {(hasSecond ? FormatApplicant(second[i]) : "absent")}";
                }
            }

            return "none";
        }

        private static string FormatApplicant(ApplicantValues values)
        {
            return $"settlement {values.settlementId}/{values.settlementName}; " +
                   $"faction {values.factionName}; travel {values.travelDays}d; " +
                   $"ask {values.openMarketAsk}/day; skills {values.skillLevels}";
        }

        /// <summary>
        /// Posts a job, runs the real matcher against a freshly rebuilt pool, measures the result
        /// and cleans up.
        ///
        /// The pool is cleared first so each measurement sees the same world: without that, the
        /// second posting would be matched against a pool the first had already taken people out of,
        /// and the comparison would measure order rather than the requirement or wage invariant.
        /// </summary>
        private static Draw Measure(
            IntercolonyWorldComponent state, SkillDef skill, int minLevel, int term, int wage)
        {
            LaborCandidateService.Clear();

            JobPosting posting = MakePosting(state, skill, minLevel, term, wage);
            JobPostingService.MatchAll(state);

            Draw draw = new Draw
            {
                applicants = posting.Applicants.Count,
                interested = JobPostingService.CountInterested(
                    state, skill, minLevel, term, wage, CombatClause.Civilian)
            };

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
                state, skill, minLevel, term, wage, WageStructure.Daily,
                CombatClause.Civilian, out _);
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

        private static int BestSkillLevel(LaborProspect prospect)
        {
            int best = 0;
            if (prospect?.skillLevels == null)
            {
                return best;
            }

            foreach (int level in prospect.skillLevels)
            {
                if (level >= 0 && level > best)
                {
                    best = level;
                }
            }

            return best;
        }

        private static float MeanBestProspectSkill(List<LaborProspect> prospects)
        {
            if (prospects == null || prospects.Count == 0)
            {
                return 0f;
            }

            // Keep this calculation independent from production grading, so a broken production
            // helper cannot make its own quality statistic pass this assertion.
            float total = 0f;
            foreach (LaborProspect prospect in prospects)
            {
                int best = 0;
                if (prospect?.skillLevels != null)
                {
                    foreach (int level in prospect.skillLevels)
                    {
                        if (level >= 0 && level > best)
                        {
                            best = level;
                        }
                    }
                }

                total += best;
            }

            return total / prospects.Count;
        }

        private static string Trim(string text)
        {
            string flat = text.Replace("\n", " ").Replace("  ", " ");
            return flat.Length <= 90 ? flat : flat.Substring(0, 90) + "...";
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
