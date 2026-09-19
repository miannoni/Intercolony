using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Posting jobs and collecting applicants (DESIGN.md §35.2, §114).
    ///
    /// F25 makes the posting an RFQ: a worker applies when they meet the requirement (and, for an
    /// emergency posting, have a valid emergency route), and the application carries the worker's
    /// own asking wage. Reputation still controls the census's availability and quality, while the requirement controls who can answer — see <see
    /// cref="MatchAll"/>.
    ///
    /// The market limits itself. Every open posting is matched against **one** world labor pool per
    /// refresh, and each worker applies to at most one posting, so ten identical postings are
    /// exactly one posting. That is why nothing here charges a fee or caps how many jobs a player
    /// may advertise: the scarce thing is workers, not advertisements.
    /// </summary>
    public static class JobPostingService
    {
        /// <summary>
        /// A posting no longer names a number of positions, so the waiting pool needs its own
        /// ceiling or it would grow without limit. Applicants past this simply do not apply;
        /// the player hires from those waiting and more arrive as the market turns over.
        /// </summary>
        public const int MaxWaitingApplicants = 6;

        /// <summary>
        /// One expensive applicant attempt per world tick keeps equipment fulfilment from stacking
        /// into the click-sized multi-applicant stall measured on the old path.
        /// </summary>
        public const int EmergencyMaterialisationsPerTick = 1;

        /// <summary>
        /// Days an applicant will wait before withdrawing. Long enough that the player need not
        /// watch the tab, short enough that a forgotten posting stops holding people hostage.
        /// </summary>
        public const int ApplicantPatienceDays = 12;

        // Separates applicant-queue shuffles from the labor-census random stream.
        private const int ApplicantShuffleSalt = 0x4C41_5445;

        private const string NoApplicantsLetterTitle = "No applicants";
        private const string NoApplicantsLetterIntro =
            "Your posting — {0} — drew no replies.\n\n";
        private const string NoSkillQualifiedExplanation =
            "Nobody reachable met the {0} skill requirement.";
        private const string NoEquipmentTierExplanation =
            "Nobody with {0} answered.";
        private const string NoEmergencyReachExplanation =
            "Nobody who met the skill and equipment requirements can reach the colony by emergency dispatch.";
        private const string EquipmentFulfilmentFailureExplanation =
            "Unexpectedly, capable workers could not be equipped to the requested tier.";

        // A posting may encounter several prospects, but a fulfilment defect should be visible
        // once per posting rather than once for every prospect that exposes the same defect.
        private static readonly HashSet<int> EquipmentFulfilmentWarnings =
            new HashSet<int>();

        // --- Creating ----------------------------------------------------------------------

        /// <summary>Creates and stores an open job posting.</summary>
        /// <param name="state">The world state that owns the posting.</param>
        /// <param name="skill">The required skill, or null for any work.</param>
        /// <param name="minSkillLevel">The minimum skill level when a skill is required.</param>
        /// <param name="termDays">The requested employment term in days.</param>
        /// <param name="structure">The wage payment structure.</param>
        /// <param name="clause">The combat clause attached to the employment.</param>
        /// <param name="failReason">The reason creation failed, or null on success.</param>
        /// <param name="requestedEquipmentLevel">
        /// The equipment tier the posting requests. Any is the legacy default for existing callers.
        /// </param>
        /// <param name="emergencyDispatch">
        /// Whether to match the posting immediately against prospects with an emergency route.
        /// </param>
        public static JobPosting TryPost(
            IntercolonyWorldComponent state, SkillDef skill, int minSkillLevel,
            int termDays, WageStructure structure, CombatClause clause,
            out string failReason,
            LaborEquipmentLevel requestedEquipmentLevel = LaborEquipmentLevel.Any,
            bool emergencyDispatch = false)
        {
            using (PostingTimings.BeginTryPost(emergencyDispatch))
            {
                failReason = null;

                if (state == null)
                {
                    failReason = "No world state.";
                    return null;
                }

                if (termDays < 1 || termDays > LaborCandidateService.MaxTermDays)
                {
                    failReason = $"Term must be between 1 and {LaborCandidateService.MaxTermDays} days.";
                    return null;
                }

                JobPosting posting = new JobPosting
                {
                    id = state.NextId(),
                    skill = skill,
                    minSkillLevel = skill == null ? 0 : Mathf.Clamp(minSkillLevel, 0, 20),
                    termDays = termDays,
                    wageStructure = structure,
                    combatClause = clause,
                    requestedEquipmentLevel = requestedEquipmentLevel,
                    emergencyDispatch = emergencyDispatch,
                    postedTick = GenTicks.TicksGame,
                    expiryTick = -1,
                    status = JobPostingStatus.Open
                };

                state.AddPosting(posting);

                if (emergencyDispatch)
                {
                    MatchImmediately(state, posting);
                }

                using (PostingTimings.Phase(PostingTimingPhase.FinalApplicantPublication))
                {
                    Messages.Message(
                        $"Posted: {posting.Headline()}.",
                        MessageTypeDefOf.PositiveEvent, historical: false);

                    IntercolonyLog.Message($"Posted: {posting}");
                    return posting;
                }
            }
        }

        // --- Matching ----------------------------------------------------------------------

        /// <summary>
        /// Gives a newly-created emergency posting the current market's first look immediately.
        ///
        /// The census is deliberately reused rather than rebuilt: posting an emergency job must
        /// not manufacture a second population just because it was created between refreshes.
        /// With one posting there is no cross-posting choice to resolve, but the same prospect
        /// predicate, queue room and shuffle are still used. The selected census records are queued;
        /// the Apply path runs later from the world component tick.
        /// </summary>
        internal static void MatchImmediately(
            IntercolonyWorldComponent state, JobPosting posting)
        {
            if (state == null || posting == null || !posting.IsOpen)
            {
                return;
            }

            float standing;
            List<LaborProspect> world;
            List<Interest> interested;
            MatchAttempt attempt = new MatchAttempt();

            using (PostingTimings.Phase(PostingTimingPhase.LightweightCensusFiltering))
            {
                standing = EmployerReputationService.ScoreFor(state);
                world = LaborCandidateService.Census(state);
                interested = new List<Interest>();

                foreach (LaborProspect worker in world)
                {
                    if (worker == null)
                    {
                        continue;
                    }

                    ProspectDecision decision = EvaluatePosting(state, posting, worker, standing);
                    if (!decision.SkillQualified)
                    {
                        continue;
                    }

                    attempt.skillQualified++;
                    if (!decision.Eligible)
                    {
                        RecordRejection(ref attempt, decision.rejection);
                        continue;
                    }

                    interested.Add(new Interest
                    {
                        worker = worker,
                        ask = decision.ask,
                        censusRefreshCount = worker.censusRefreshCount,
                        censusIndex = worker.censusIndex
                    });
                }
            }

            Rand.PushState(Gen.HashCombineInt(state.EconomySeed, state.RefreshCount) ^ ApplicantShuffleSalt);
            try
            {
                using (PostingTimings.Phase(PostingTimingPhase.CandidateApplicationSelection))
                {
                    QueueInterested(state, posting, interested, standing, ref attempt);
                }
            }
            finally
            {
                Rand.PopState();
            }
        }

        /// <summary>
        /// Exposes every open posting to this cycle's world labor pool.
        ///
        /// **The F25 rule is deliberately simple:** a worker applies if and only if they meet the
        /// posting's requirement. The posted wage is retained as saved posting data, but it is not
        /// a clearing threshold. Each applicant carries the asking wage calculated for that worker,
        /// posting term, employer standing, and combat clause, so the player discovers the price by
        /// reviewing the replies.
        ///
        /// The census still supplies the market shape: employer reputation affects availability and
        /// candidate quality, and settlement labor supply remains part of each worker's ask. A
        /// demanding requirement therefore yields fewer replies because fewer prospects qualify,
        /// without adding a second scarcity or attractiveness formula.
        ///
        /// Called from the market refresh, which is also when the pool itself changes — so "the
        /// world had a look at your advertisement" and "the world moved on" are the same beat.
        /// </summary>
        public static void MatchAll(IntercolonyWorldComponent state)
        {
            if (state == null)
            {
                return;
            }

            List<JobPosting> open = new List<JobPosting>();
            foreach (JobPosting posting in state.Postings)
            {
                if (posting.IsOpen)
                {
                    open.Add(posting);
                }
            }

            if (open.Count == 0)
            {
                // Nothing is advertised, so the latent pool is never built. A player who does not
                // use postings pays nothing for them.
                return;
            }

            float standing = EmployerReputationService.ScoreFor(state);
            List<LaborProspect> world = LaborCandidateService.Census(state);
            Dictionary<int, int> gained = new Dictionary<int, int>();
            Dictionary<int, MatchAttempt> attempts = new Dictionary<int, MatchAttempt>();

            foreach (JobPosting posting in open)
            {
                attempts[posting.id] = new MatchAttempt();
            }

            // Phase one: every worker picks the one posting that suits them best, without regard
            // to whether it already has a queue.
            //
            // Ignoring room here is deliberate and is what makes ten identical postings behave like
            // one. If a full posting pushed workers onto the next identical notice, advertising the
            // same job five times would collect five queues, and the market would stop being the
            // scarce thing. A worker who wanted the job that filled up simply does not apply.
            Dictionary<int, List<Interest>> interested = new Dictionary<int, List<Interest>>();

            for (int workerIndex = 0; workerIndex < world.Count; workerIndex++)
            {
                LaborProspect worker = world[workerIndex];
                if (worker == null)
                {
                    continue;
                }

                JobPosting best = null;
                int bestAsk = 0;
                JobPosting bestSkillMatch = null;
                int bestSkillAsk = 0;
                ProspectRejection bestSkillRejection = ProspectRejection.None;

                foreach (JobPosting posting in open)
                {
                    ProspectDecision decision = EvaluatePosting(state, posting, worker, standing);
                    if (!decision.SkillQualified)
                    {
                        continue;
                    }

                    // Keep one diagnostic owner for each prospect, just as matching keeps one
                    // queue owner. If every skill-qualified choice misses its equipment or
                    // emergency gate, the best skill-only choice still identifies the gate that
                    // stopped the prospect.
                    if (bestSkillMatch == null || decision.ask > bestSkillAsk ||
                        (decision.ask == bestSkillAsk && posting.id < bestSkillMatch.id))
                    {
                        bestSkillMatch = posting;
                        bestSkillAsk = decision.ask;
                        bestSkillRejection = decision.rejection;
                    }

                    if (!decision.Eligible)
                    {
                        continue;
                    }

                    // A prospect chooses the posting that pays them the most. Ask includes the
                    // posting's term and combat clause, so this is a real choice: the same prospect
                    // can ask more for Armed or Security work than for Civilian work. Break equal
                    // asks by lower posting id so a seeded census produces the same market after a
                    // save reload.
                    if (best == null || decision.ask > bestAsk ||
                        (decision.ask == bestAsk && posting.id < best.id))
                    {
                        best = posting;
                        bestAsk = decision.ask;
                    }
                }

                if (best != null)
                {
                    MatchAttempt attempt = attempts[best.id];
                    attempt.skillQualified++;
                    attempts[best.id] = attempt;
                }
                else if (bestSkillMatch != null)
                {
                    MatchAttempt attempt = attempts[bestSkillMatch.id];
                    attempt.skillQualified++;
                    RecordRejection(ref attempt, bestSkillRejection);
                    attempts[bestSkillMatch.id] = attempt;
                }

                if (best == null)
                {
                    continue;
                }

                if (!interested.TryGetValue(best.id, out List<Interest> queue))
                {
                    queue = new List<Interest>();
                    interested[best.id] = queue;
                }

                queue.Add(new Interest
                {
                    worker = worker,
                    ask = bestAsk,
                    censusRefreshCount = worker.censusRefreshCount,
                    censusIndex = worker.censusIndex
                });
            }

            // Phase two: each posting takes a deterministic spread from the qualified pool, not
            // the strongest few workers.
            //
            // Once F25 stopped using the posted wage as a filter, best-N ranking would hand the
            // player the strongest N workers every refresh — and therefore the highest asks —
            // turning the waiting list into a leaderboard. Shuffle each qualified queue with the
            // same seeded RNG inputs as the census and take up to its existing room. The cap stays
            // unchanged, while the existing ask formula supplies the quality/price correlation
            // without another attractiveness rule.
            Rand.PushState(Gen.HashCombineInt(state.EconomySeed, state.RefreshCount) ^ ApplicantShuffleSalt);
            try
            {
                foreach (JobPosting posting in open)
                {
                    if (posting.HasPendingMaterialisation)
                    {
                        continue;
                    }

                    if (posting.NeedsEmergencyMatchRebuild &&
                        posting.HasUntrackedApplicantIdentity)
                    {
                        // Do not let a forced refresh run before the first world tick duplicate
                        // applicants from a pre-queue save whose provenance is unavailable.
                        posting.MarkEmergencyMatchInitialised();
                        continue;
                    }

                    if (posting.emergencyDispatch && posting.HasUntrackedApplicantIdentity)
                    {
                        // A legacy emergency applicant has no census identity that can be matched
                        // against this refresh. Keep the posting fenced while it remains visible;
                        // once the player hires or rejects it, matching can safely resume.
                        continue;
                    }

                    MatchAttempt attempt = attempts[posting.id];
                    if (posting.emergencyDispatch)
                    {
                        interested.TryGetValue(posting.id, out List<Interest> queue);
                        QueueInterested(state, posting, queue, standing, ref attempt);
                        attempts[posting.id] = attempt;
                        continue;
                    }

                    if (!interested.TryGetValue(posting.id, out List<Interest> ordinaryQueue))
                    {
                        continue;
                    }

                    int taken = ApplyInterested(state, posting, ordinaryQueue, ref attempt);
                    attempts[posting.id] = attempt;

                    if (taken > 0)
                    {
                        gained[posting.id] = taken;
                    }
                }
            }
            finally
            {
                Rand.PopState();
            }

            foreach (JobPosting posting in open)
            {
                if (posting.emergencyDispatch)
                {
                    // QueueInterested reports an empty emergency match immediately, and a
                    // non-empty one reports after the tick-bound materialisation queue drains.
                    continue;
                }

                gained.TryGetValue(posting.id, out int arrived);
                MatchAttempt attempt = attempts[posting.id];
                Report(state, posting, arrived, standing, attempt);
            }
        }

        /// <summary>One qualified worker's chosen posting and the ask they quoted for it.</summary>
        private struct Interest
        {
            public LaborProspect worker;
            public int ask;
            public int censusRefreshCount;
            public int censusIndex;
        }

        private struct MatchAttempt
        {
            public int skillQualified;
            public int tierRejected;
            public int emergencyReachRejected;
            public int sourceRejected;
            public int fulfilmentRejected;
            public int accepted;
        }

        private struct ProspectDecision
        {
            public int ask;
            public ProspectRejection rejection;

            public bool SkillQualified => rejection != ProspectRejection.Skill;

            public bool Eligible => rejection == ProspectRejection.None;
        }

        private enum ProspectRejection
        {
            None,
            Skill,
            Tier,
            EmergencyReach
        }

        private enum ApplyResult
        {
            Rejected,
            SourceRejected,
            FulfilmentRejected,
            Accepted
        }

        /// <summary>How many more applicants this posting will hold.</summary>
        private static int Room(JobPosting posting)
        {
            return Mathf.Max(0, MaxWaitingApplicants - posting.Applicants.Count);
        }

        /// <summary>
        /// Applies the shared phase-one gates to one census record. Keeping the emergency route
        /// after skill and promised equipment preserves the existing diagnostic priority and keeps
        /// the legacy Any path out of the new route check.
        /// </summary>
        private static ProspectDecision EvaluatePosting(
            IntercolonyWorldComponent state, JobPosting posting, LaborProspect worker,
            float standing)
        {
            if (!posting.MeetsRequirement(worker))
            {
                return new ProspectDecision
                {
                    rejection = ProspectRejection.Skill
                };
            }

            int ask = Ask(state, worker, posting, standing);

            // Reject a prospect whose census promise is below the request before the expensive
            // Apply/Materialise path. None is intentionally a wildcard here: Apply strips
            // bondable equipment after the pawn is built.
            if (posting.requestedEquipmentLevel != LaborEquipmentLevel.Any &&
                posting.requestedEquipmentLevel != LaborEquipmentLevel.None &&
                !LaborEquipmentTierService.MeetsOrExceeds(
                    worker.equipmentTier, posting.requestedEquipmentLevel))
            {
                return new ProspectDecision
                {
                    ask = ask,
                    rejection = ProspectRejection.Tier
                };
            }

            if (posting.emergencyDispatch &&
                !LaborCandidateService.CanReachEmergency(state, worker))
            {
                return new ProspectDecision
                {
                    ask = ask,
                    rejection = ProspectRejection.EmergencyReach
                };
            }

            return new ProspectDecision
            {
                ask = ask,
                rejection = ProspectRejection.None
            };
        }

        private static void RecordRejection(
            ref MatchAttempt attempt, ProspectRejection rejection)
        {
            switch (rejection)
            {
                case ProspectRejection.Tier:
                    attempt.tierRejected++;
                    break;
                case ProspectRejection.EmergencyReach:
                    attempt.emergencyReachRejected++;
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// Runs the existing capped shuffle/apply phase for one posting. The same helper serves
        /// refresh matching and immediate emergency matching so the queue cap and materialisation
        /// rules cannot drift between the two entry points.
        /// </summary>
        private static int ApplyInterested(
            IntercolonyWorldComponent state, JobPosting posting, List<Interest> queue,
            ref MatchAttempt attempt)
        {
            int room = Room(posting);
            if (room <= 0)
            {
                return 0;
            }

            for (int i = queue.Count - 1; i > 0; i--)
            {
                int j = Rand.RangeInclusive(0, i);
                Interest swap = queue[i];
                queue[i] = queue[j];
                queue[j] = swap;
            }

            int taken = 0;
            for (int i = 0; i < queue.Count && taken < room; i++)
            {
                ApplyResult result = Apply(
                    state, posting, queue[i].worker, queue[i].ask,
                    queue[i].censusRefreshCount, queue[i].censusIndex);
                switch (result)
                {
                    case ApplyResult.Accepted:
                        attempt.accepted++;
                        taken++;
                        break;
                    case ApplyResult.SourceRejected:
                        attempt.sourceRejected++;
                        break;
                    case ApplyResult.FulfilmentRejected:
                        attempt.fulfilmentRejected++;
                        break;
                    default:
                        break;
                }
            }

            return taken;
        }

        /// <summary>
        /// Freezes the cheap matching result for an emergency posting without building a pawn.
        /// The queue keeps every selected prospect, not only the visible room, because a failed
        /// equipment attempt must not reduce the number of valid applicants compared with the old
        /// synchronous walk.
        /// </summary>
        private static bool QueueInterested(
            IntercolonyWorldComponent state, JobPosting posting, List<Interest> queue,
            float standing, ref MatchAttempt attempt)
        {
            if (state == null || posting == null || !posting.IsOpen)
            {
                return false;
            }

            int room = Room(posting);
            if (room <= 0)
            {
                posting.MarkEmergencyMatchInitialised();
                ReportImmediately(state, posting, 0, standing, attempt);
                return false;
            }

            if (queue == null)
            {
                posting.MarkEmergencyMatchInitialised();
                ReportImmediately(state, posting, 0, standing, attempt);
                return false;
            }

            for (int i = queue.Count - 1; i > 0; i--)
            {
                int j = Rand.RangeInclusive(0, i);
                Interest swap = queue[i];
                queue[i] = queue[j];
                queue[j] = swap;
            }

            PendingJobPostingMatch pending = new PendingJobPostingMatch
            {
                standing = standing,
                skillQualified = attempt.skillQualified,
                tierRejected = attempt.tierRejected,
                emergencyReachRejected = attempt.emergencyReachRejected,
                sourceRejected = attempt.sourceRejected,
                fulfilmentRejected = attempt.fulfilmentRejected,
                accepted = attempt.accepted
            };

            for (int i = 0; i < queue.Count; i++)
            {
                Interest interest = queue[i];
                if (interest.worker == null ||
                    AlreadyApplied(posting, interest.censusRefreshCount, interest.censusIndex))
                {
                    continue;
                }

                pending.candidates.Add(new PendingJobPostingCandidate
                {
                    worker = interest.worker,
                    ask = interest.ask,
                    censusRefreshCount = interest.censusRefreshCount,
                    censusIndex = interest.censusIndex
                });
            }

            if (!pending.HasRemaining)
            {
                posting.MarkEmergencyMatchInitialised();
                ReportImmediately(state, posting, 0, standing, attempt);
                return false;
            }

            posting.BeginPendingMaterialisation(pending);
            return true;
        }

        private static bool AlreadyApplied(
            JobPosting posting, int censusRefreshCount, int censusIndex)
        {
            if (posting == null || censusRefreshCount < 0 || censusIndex < 0)
            {
                return false;
            }

            foreach (JobApplicant applicant in posting.Applicants)
            {
                if (applicant != null &&
                    applicant.sourceCensusRefreshCount == censusRefreshCount &&
                    applicant.sourceCensusIndex == censusIndex)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Advances at most one emergency prospect globally per world tick. This is called from the
        /// existing WorldComponentTick owner, so all pawn and Thing work remains on RimWorld's main
        /// thread and no second scheduler or background execution is introduced.
        /// </summary>
        public static void AdvanceMaterialisations(IntercolonyWorldComponent state)
        {
            if (state == null || state.Postings == null)
            {
                return;
            }

            int materialisationsThisTick = 0;
            foreach (JobPosting posting in state.Postings)
            {
                if (posting == null)
                {
                    continue;
                }

                if (!posting.IsOpen)
                {
                    if (posting.PendingMaterialisation != null || posting.Applicants.Count > 0)
                    {
                        posting.ClearPendingMaterialisation();
                        posting.DiscardApplicants();
                    }

                    continue;
                }

                if (posting.NeedsEmergencyMatchRebuild)
                {
                    // A legacy applicant has no source identity. It predates the reconstructable
                    // queue and is treated as a completed search rather than risk duplicating it.
                    if (posting.HasUntrackedApplicantIdentity)
                    {
                        posting.MarkEmergencyMatchInitialised();
                    }
                    else
                    {
                        MatchImmediately(state, posting);
                    }
                }

                if (!posting.HasPendingMaterialisation)
                {
                    continue;
                }

                AdvanceOneMaterialisation(state, posting);
                materialisationsThisTick++;
                if (materialisationsThisTick >= EmergencyMaterialisationsPerTick)
                {
                    return;
                }
            }
        }

        private static void AdvanceOneMaterialisation(
            IntercolonyWorldComponent state, JobPosting posting)
        {
            PendingJobPostingMatch pending = posting.PendingMaterialisation;
            if (pending == null)
            {
                return;
            }

            if (Room(posting) <= 0 || !pending.HasRemaining)
            {
                CompletePendingMatch(state, posting, pending);
                return;
            }

            PendingJobPostingCandidate candidate = pending.TakeNext();
            if (candidate == null || candidate.worker == null)
            {
                if (!pending.HasRemaining)
                {
                    CompletePendingMatch(state, posting, pending);
                }

                return;
            }

            ApplyResult result = Apply(
                state, posting, candidate.worker, candidate.ask,
                candidate.censusRefreshCount, candidate.censusIndex);
            switch (result)
            {
                case ApplyResult.Accepted:
                    pending.accepted++;
                    break;
                case ApplyResult.SourceRejected:
                    pending.sourceRejected++;
                    break;
                case ApplyResult.FulfilmentRejected:
                    pending.fulfilmentRejected++;
                    break;
            }

            if (!posting.IsOpen)
            {
                posting.ClearPendingMaterialisation();
                posting.DiscardApplicants();
                return;
            }

            if (Room(posting) <= 0 || !pending.HasRemaining)
            {
                CompletePendingMatch(state, posting, pending);
            }
        }

        private static void CompletePendingMatch(
            IntercolonyWorldComponent state, JobPosting posting, PendingJobPostingMatch pending)
        {
            MatchAttempt attempt = new MatchAttempt
            {
                skillQualified = pending.skillQualified,
                tierRejected = pending.tierRejected,
                emergencyReachRejected = pending.emergencyReachRejected,
                sourceRejected = pending.sourceRejected,
                fulfilmentRejected = pending.fulfilmentRejected,
                accepted = pending.accepted
            };
            int arrived = pending.accepted;
            posting.CompletePendingMaterialisation();
            if (posting.IsOpen)
            {
                ReportImmediately(state, posting, arrived, pending.standing, attempt);
            }
        }

        private static void ReportImmediately(
            IntercolonyWorldComponent state, JobPosting posting, int arrived,
            float standing, MatchAttempt attempt)
        {
            using (PostingTimings.Phase(PostingTimingPhase.FinalApplicantPublication))
            {
                Report(state, posting, arrived, standing, attempt);
            }
        }

        /// <summary>
        /// Turns a census record into an actual applicant - the only point at which a pawn is built.
        ///
        /// This is what makes a deep market affordable. The census can be hundreds of workers
        /// because none of them exist until one of them applies for something; generating a pawn is
        /// the expensive call, and it happens once for a legacy applicant or demanding posting
        /// rather than once per worker considered.
        /// </summary>
        private static ApplyResult Apply(
            IntercolonyWorldComponent state, JobPosting posting, LaborProspect worker, int ask,
            int censusRefreshCount, int censusIndex)
        {
            EmergencyArrivalQuote emergencyArrivalQuote = default(EmergencyArrivalQuote);
            if (posting.emergencyDispatch)
            {
                // Matching already applied the emergency gate, but the quote is frozen at the
                // publication boundary as well. A route that disappeared between selection and
                // materialisation must not become an emergency applicant with no emergency quote.
                emergencyArrivalQuote = LaborCandidateService.QuoteEmergencyArrival(state, worker);
                if (!emergencyArrivalQuote.available)
                {
                    return ApplyResult.SourceRejected;
                }
            }

            // Any is the legacy equipment path: one generation, no equipment capability gate, no
            // classification and no retry. Existing non-emergency postings load as Any and must
            // behave exactly as they did before equipment requests existed.
            if (posting.requestedEquipmentLevel == LaborEquipmentLevel.Any)
            {
                Pawn pawn;
                using (PostingTimings.Phase(PostingTimingPhase.PawnMaterialisation))
                {
                    pawn = worker.Materialise();
                }
                if (pawn == null)
                {
                    return ApplyResult.Rejected;
                }

                if (!posting.MeetsRequirement(pawn))
                {
                    WarnSkillRequirementFailure(posting, pawn);
                    DiscardRejectedPawn(pawn);
                    return ApplyResult.FulfilmentRejected;
                }

                using (PostingTimings.Phase(PostingTimingPhase.FinalApplicantPublication))
                {
                    AddApplicant(
                        posting, worker, pawn, ask, censusRefreshCount, censusIndex,
                        emergencyArrivalQuote);
                }
                return ApplyResult.Accepted;
            }

            SettlementEconomicProfile profile = ProfileFor(state, worker.settlementId);
            if (!LaborEquipmentTierService.CanSupply(
                    profile,
                    posting.requestedEquipmentLevel,
                    posting.combatClause))
            {
                // This is the cheap source-settlement filter. Do it before Materialise: full pawn
                // generation is expensive, and an incapable source must not spend it.
                return ApplyResult.SourceRejected;
            }

            Pawn applicantPawn;
            using (PostingTimings.Phase(PostingTimingPhase.PawnMaterialisation))
            {
                applicantPawn = worker.Materialise();
            }
            if (applicantPawn == null)
            {
                return ApplyResult.Rejected;
            }

            if (!posting.MeetsRequirement(applicantPawn))
            {
                WarnSkillRequirementFailure(posting, applicantPawn);
                DiscardRejectedPawn(applicantPawn);
                return ApplyResult.FulfilmentRejected;
            }

            if (posting.requestedEquipmentLevel == LaborEquipmentLevel.None)
            {
                StripBondableEquipment(applicantPawn);
            }
            else
            {
                LaborEquipmentLevel natural = LaborEquipmentTierService.Classify(
                    applicantPawn, posting.combatClause);
                if (!LaborEquipmentTierService.MeetsOrExceeds(
                        natural, posting.requestedEquipmentLevel))
                {
                    string fulfilmentFailure;
                    bool fulfilled;
                    using (PostingTimings.Phase(PostingTimingPhase.EquipmentFulfilment))
                    {
                        fulfilled = LaborEquipmentAllocator.TryFulfil(
                            applicantPawn, worker, posting.id, worker.equipmentTier,
                            posting.combatClause, profile, out fulfilmentFailure);
                    }

                    if (!fulfilled)
                    {
                        WarnEquipmentFulfilmentFailure(
                            posting, worker, fulfilmentFailure ?? "unknown allocator failure");
                        DiscardRejectedPawn(applicantPawn);
                        return ApplyResult.FulfilmentRejected;
                    }
                }
            }

            // The allocator is deliberately not an acceptance shortcut. The actual pawn's final
            // loadout is classified once here, after every mutation, and this invariant decides
            // whether the applicant is allowed into the waiting list.
            LaborEquipmentLevel actual = LaborEquipmentTierService.Classify(
                applicantPawn, posting.combatClause);
            if (!LaborEquipmentTierService.MeetsOrExceeds(
                    actual, posting.requestedEquipmentLevel))
            {
                WarnEquipmentFulfilmentFailure(
                    posting, worker,
                    $"actual loadout classified as {actual} after fulfilment.");
                DiscardRejectedPawn(applicantPawn);
                return ApplyResult.FulfilmentRejected;
            }

            using (PostingTimings.Phase(PostingTimingPhase.FinalApplicantPublication))
            {
                AddApplicant(
                    posting, worker, applicantPawn, ask, censusRefreshCount, censusIndex,
                    emergencyArrivalQuote);
            }
            return ApplyResult.Accepted;
        }

        private static void WarnSkillRequirementFailure(JobPosting posting, Pawn pawn)
        {
            string skillName = posting?.skill?.skillLabel ?? posting?.skill?.defName ?? "unknown";
            string reason = "materialised pawn has no skills";
            if (posting?.skill != null && pawn?.skills != null)
            {
                SkillRecord record = pawn.skills.GetSkill(posting.skill);
                reason = record == null
                    ? "materialised pawn has no matching skill record"
                    : record.TotallyDisabled
                        ? "materialised pawn's skill is TotallyDisabled"
                        : $"materialised pawn level {record.Level} is below {posting.minSkillLevel}";
            }

            IntercolonyLog.Warning(
                $"Skill requirement failed for posting {posting?.id.ToString() ?? "unknown"}: " +
                $"skill {skillName} >= {posting?.minSkillLevel.ToString() ?? "unknown"}; {reason}.");
        }

        private static void WarnEquipmentFulfilmentFailure(
            JobPosting posting, LaborProspect worker, string reason)
        {
            if (posting != null && !EquipmentFulfilmentWarnings.Add(posting.id))
            {
                return;
            }

            IntercolonyLog.Warning(
                $"Equipment fulfilment failed for posting {posting?.id.ToString() ?? "unknown"}: " +
                $"requested {posting?.requestedEquipmentLevel.ToString() ?? "unknown"}, " +
                $"promised {worker?.equipmentTier.ToString() ?? "unknown"}, " +
                $"clause {posting?.combatClause.ToString() ?? "unknown"}; {reason}");
        }

        private static void StripBondableEquipment(Pawn pawn)
        {
            if (pawn?.equipment != null)
            {
                List<ThingWithComps> equipment = pawn.equipment.AllEquipmentListForReading;
                for (int i = equipment.Count - 1; i >= 0; i--)
                {
                    ThingWithComps item = equipment[i];
                    if (item != null && item.def != null && item.def.IsWeapon)
                    {
                        // DestroyEquipment removes the item through the equipment tracker before
                        // destroying it; never mutate AllEquipmentListForReading directly.
                        pawn.equipment.DestroyEquipment(item);
                    }
                }
            }

            if (pawn?.apparel != null)
            {
                List<Apparel> wornApparel = pawn.apparel.WornApparel;
                for (int i = wornApparel.Count - 1; i >= 0; i--)
                {
                    Apparel item = wornApparel[i];
                    pawn.apparel.Remove(item);
                    item.Destroy(DestroyMode.Vanish);
                }
            }
        }

        private static void AddApplicant(
            JobPosting posting, LaborProspect worker, Pawn pawn, int ask,
            int censusRefreshCount, int censusIndex, EmergencyArrivalQuote emergencyArrivalQuote)
        {
            // Nothing else owns this pawn - it was built for this list. KeepForever rather than
            // Decide for the reason the notes give:
            // WorldPawnGC knows nothing about a job posting and would collect an applicant the
            // player is still deciding about.
            if (!Find.WorldPawns.Contains(pawn))
            {
                Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
            }

            JobApplicant applicant = new JobApplicant
            {
                pawn = pawn,
                settlementId = worker.settlementId,
                settlementName = worker.settlementName,
                factionName = worker.factionName,
                faction = worker.faction,
                distanceTiles = worker.distanceTiles,
                travelDays = worker.travelDays,
                requiredSkillLevel = posting.SkillLevelOf(pawn),
                openMarketAsk = ask,
                appliedTick = GenTicks.TicksGame,
                sourceCensusRefreshCount = censusRefreshCount,
                sourceCensusIndex = censusIndex
            };

            if (posting.emergencyDispatch)
            {
                LaborCandidateService.FreezeEmergencyArrivalQuote(
                    applicant, emergencyArrivalQuote);
            }

            posting.Applicants.Add(applicant);
        }

        private static void DiscardRejectedPawn(Pawn pawn)
        {
            if (Find.WorldPawns.Contains(pawn))
            {
                Find.WorldPawns.RemoveAndDiscardPawnViaGC(pawn);
                return;
            }

            // Destroy() on an uncontained pawn passes it back to WorldPawns, which leaks it. Do
            // not use RemoveAndDiscardPawnViaGC here either: its RemovePawn step logs an error
            // for a pawn that was never contained. PassToWorld marks this uncontained rejection
            // as Discard, so vanilla removes and disposes it without either defect.
            Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.Discard);
        }

        /// <summary>
        /// What this worker charges for this particular job.
        ///
        /// Priced for the posting's own term and clause rather than any advertised minimum - a
        /// 60-day civilian job and a 5-day security job are different work, and the same person
        /// charges differently for them.
        /// </summary>
        private static int Ask(IntercolonyWorldComponent state, LaborProspect worker,
            JobPosting posting, float standing)
        {
            return LaborCandidateService.DailyWageFor(
                worker.pricedSkillValue, ProfileFor(state, worker.settlementId),
                worker.distanceTiles, posting.termDays, standing, posting.combatClause,
                posting.emergencyDispatch);
        }

        /// <summary>
        /// Tells the player what their advertisement did, and — when it did nothing — why.
        ///
        /// The "why" is the point. A posting that draws nobody is indistinguishable from a broken
        /// feature unless the game says whether skill, equipment, fulfilment, or emergency reach
        /// stopped the reply.
        /// The posted wage is intentionally not one of those reasons.
        /// </summary>
        private static void Report(IntercolonyWorldComponent state, JobPosting posting, int arrived,
            float standing, MatchAttempt attempt)
        {
            if (arrived > 0)
            {
                posting.emptyCycles = 0;
                posting.noAnswerNotified = false;

                IntercolonyLetters.Send(
                    IntercolonyLetterImportance.Always,
                    arrived == 1 ? "1 applicant" : $"{arrived} applicants",
                    $"Your posting — {posting.Headline()} — drew " +
                    (arrived == 1 ? "an applicant" : $"{arrived} applicants") + ".\n\n" +
                    $"{posting.Applicants.Count} waiting in total. Review them in the " +
                    "Labor tab under Posts.\n\n" +
                    $"They will wait about {ApplicantPatienceDays} days.",
                    LetterDefOf.PositiveEvent);
                return;
            }

            posting.emptyCycles++;

            // Once, not every cycle. A standing order that quietly finds nobody for a season should
            // say so the first time and then leave the player alone.
            if (posting.noAnswerNotified || posting.Applicants.Count > 0)
            {
                return;
            }

            posting.noAnswerNotified = true;

            string explanation;
            if (posting.requestedEquipmentLevel == LaborEquipmentLevel.Any &&
                !posting.emergencyDispatch)
            {
                // Any has no equipment gate; preserve its legacy explanation byte for byte.
                explanation = ExplainSilence(state, posting, standing);
            }
            else if (attempt.fulfilmentRejected > 0)
            {
                explanation = EquipmentFulfilmentFailureExplanation;
            }
            else if (attempt.tierRejected > 0 || attempt.sourceRejected > 0)
            {
                explanation = string.Format(
                    NoEquipmentTierExplanation,
                    LaborEquipmentTierService.Label(posting.requestedEquipmentLevel));
            }
            else if (posting.skill != null && attempt.skillQualified == 0)
            {
                explanation = string.Format(
                    NoSkillQualifiedExplanation, posting.SkillLabel);
            }
            else if (attempt.emergencyReachRejected > 0)
            {
                explanation = NoEmergencyReachExplanation;
            }
            else
            {
                explanation = ExplainSilence(state, posting, standing);
            }

            IntercolonyLetters.Send(
                IntercolonyLetterImportance.Chatty,
                NoApplicantsLetterTitle,
                string.Format(NoApplicantsLetterIntro, posting.Headline()) + explanation,
                LetterDefOf.NeutralEvent);
        }

        /// <summary>
        /// Works out why nobody applied, by asking the pool the same requirement question the
        /// matcher did.
        ///
        /// Deliberately measured rather than guessed: it counts qualified prospects in the current
        /// world pool. A letter that said "try offering more" would contradict F25, because the
        /// posted wage no longer decides who applies.
        /// </summary>
        public static string ExplainSilence(IntercolonyWorldComponent state, JobPosting posting,
            float standing)
        {
            List<LaborProspect> world = LaborCandidateService.Census(state);

            int qualified = 0;

            foreach (LaborProspect worker in world)
            {
                if (worker == null || !posting.MeetsRequirement(worker))
                {
                    continue;
                }

                qualified++;
            }

            if (qualified == 0)
            {
                return $"Nobody reachable has {posting.SkillLabel}. Lower the requirement, or wait — " +
                       "who is looking for work changes with the market.";
            }

            string reputation = "";
            EmployerReputation rep = state?.EmployerStanding;
            if (rep != null && rep.Score < EmployerReputation.StartingScore)
            {
                reputation = $"\n\nYour standing as an employer ({rep.TierLabel().ToLower()}) is part of " +
                             "the market reach: a poor record brings a smaller, weaker pool.";
            }

            return $"{qualified} worker{(qualified == 1 ? "" : "s")} reachable can do the job, but the " +
                   "qualified pool did not answer this refresh. They may have chosen another open " +
                   "posting, or the next market refresh may draw a different pool." + reputation;
        }

        // --- Lifecycle ---------------------------------------------------------------------

        /// <summary>
        /// Ages out applicants who have waited too long, and closes postings that have lapsed.
        /// Runs on the hourly beat so an expiry lands near the moment it describes (§17).
        /// </summary>
        public static void Advance(IntercolonyWorldComponent state)
        {
            if (state == null)
            {
                return;
            }

            int now = GenTicks.TicksGame;
            int patience = ApplicantPatienceDays * GenDate.TicksPerDay;

            foreach (JobPosting posting in state.Postings)
            {
                if (!posting.IsOpen)
                {
                    continue;
                }

                int withdrew = 0;
                for (int i = posting.Applicants.Count - 1; i >= 0; i--)
                {
                    JobApplicant applicant = posting.Applicants[i];
                    if (applicant == null || applicant.pawn == null ||
                        now - applicant.appliedTick >= patience)
                    {
                        applicant?.Discard();
                        posting.Applicants.RemoveAt(i);
                        withdrew++;
                    }
                }

                if (withdrew > 0)
                {
                    IntercolonyLog.Verbose(
                        $"Posting {posting.id}: {withdrew} applicant(s) withdrew after waiting.");
                }

                if (!posting.NeverExpires && now >= posting.expiryTick)
                {
                    Close(posting, JobPostingStatus.Expired,
                        posting.hired > 0
                            ? $"Expired after filling {posting.hired}."
                            : "Expired without filling any position.");

                    IntercolonyLetters.Send(
                        IntercolonyLetterImportance.Important,
                        "Job posting expired",
                        $"Your posting — {posting.Headline()} — has come down.\n\n" +
                        posting.outcomeNote,
                        LetterDefOf.NeutralEvent);
                }
            }
        }

        /// <summary>Closes a posting and releases anyone still waiting on it.</summary>
        public static void Close(JobPosting posting, JobPostingStatus status, string note)
        {
            if (posting == null)
            {
                return;
            }

            if (!posting.IsOpen)
            {
                // A Filled/Expired posting can still be closed by a lifecycle caller after its
                // status changed. Drop any deferred census work and any pawns that were created
                // before that transition, even though there is no second status transition to log.
                posting.ClearPendingMaterialisation();
                posting.DiscardApplicants();
                return;
            }

            posting.status = status;
            posting.outcomeNote = note ?? "";

            posting.ClearPendingMaterialisation();

            // Applicants are pinned world pawns; a closed posting that kept them would leak one
            // pawn per unhired applicant, forever, invisibly.
            posting.DiscardApplicants();

            IntercolonyLog.Message($"Posting closed: {posting} — {note}");
        }

        public static bool Withdraw(JobPosting posting)
        {
            if (posting == null || !posting.IsOpen)
            {
                return false;
            }

            Close(posting, JobPostingStatus.Withdrawn, "Withdrawn by the player.");
            return true;
        }

        // --- Hiring ------------------------------------------------------------------------

        /// <summary>
        /// Takes on an applicant at the applicant's own market ask.
        ///
        /// EmploymentService resolves the contract rate from the applicant's saved quote. The
        /// posted wage remains saved posting data in this slice, but it neither chose the applicant
        /// nor sets the hire rate.
        /// </summary>
        public static EmploymentContract TryAccept(
            IntercolonyWorldComponent state, JobPosting posting, JobApplicant applicant,
            Map paymentMap, out string failReason,
            EmploymentHireCostQuote quotedHireCost = null)
        {
            failReason = null;

            if (state == null || posting == null || applicant?.pawn == null)
            {
                failReason = "Nothing to accept.";
                return null;
            }

            if (!posting.IsOpen)
            {
                failReason = "That posting is not open.";
                return null;
            }

            EmploymentContract contract = EmploymentService.TryHireApplicant(
                state, applicant, posting, paymentMap, out failReason, quotedHireCost);

            if (contract == null)
            {
                return null;
            }

            posting.Applicants.Remove(applicant);
            posting.hired++;

            return contract;
        }

        /// <summary>Turns an applicant away. They go home; the posting stays open.</summary>
        public static void Reject(JobPosting posting, JobApplicant applicant)
        {
            if (posting == null || applicant == null)
            {
                return;
            }

            posting.Applicants.Remove(applicant);
            applicant.Discard();
        }

        // --- Shared ------------------------------------------------------------------------

        private static SettlementEconomicProfile ProfileFor(IntercolonyWorldComponent state, int settlementId)
        {
            Settlement settlement = IntercolonyMarketAccess.FindSettlement(settlementId);
            return settlement == null ? null : state.GetProfile(settlement);
        }

        /// <summary>
        /// How many workers in the census meet this requirement — the market's qualified supply
        /// before the applicant queue truncates it.
        ///
        /// The player never sees this number: they see the queue, and §35.2's screen shows
        /// applicants rather than interest. It exists so the self-test can measure the *market*
        /// rather than the queue length, which saturates at the cap. The term, wage, and clause
        /// parameters remain in this compatibility-shaped diagnostic entry point, but F25
        /// intentionally does not use the posted wage to count applicants.
        /// </summary>
        public static int CountInterested(
            IntercolonyWorldComponent state, SkillDef skill, int minLevel, int termDays,
            int wageOffered, CombatClause clause)
        {
            if (state == null)
            {
                return 0;
            }

            int count = 0;

            foreach (LaborProspect worker in LaborCandidateService.Census(state))
            {
                if (worker == null)
                {
                    continue;
                }

                if (skill != null && (!worker.CanDo(skill) || worker.LevelOf(skill) < minLevel))
                {
                    continue;
                }

                count++;
            }

            return count;
        }

        /// <summary>
        /// What workers matching a posting's requirement currently ask, for the posting dialog.
        ///
        /// Returned as a band rather than a number because it genuinely is one — the same
        /// requirement is met by a Construction 10 labourer from next door and a Construction 18
        /// master from across the planet, and they do not cost the same. Showing a single figure
        /// would be a more confident lie.
        /// </summary>
        public static bool GoingRate(
            IntercolonyWorldComponent state, SkillDef skill, int minLevel, int termDays,
            CombatClause clause, out int low, out int high, out int qualified,
            bool emergencyDispatch = false)
        {
            return GoingRate(
                state, skill, minLevel, termDays, clause, WageStructure.Quadrum,
                out low, out high, out qualified, emergencyDispatch);
        }

        /// <summary>
        /// The same band for a specific wage structure. Paying by the day carries a premium, so
        /// a daily posting genuinely costs more than a per-quadrum one for the same worker. The
        /// band is market information for the player, not an application threshold: applicants
        /// still arrive based on the requirement and quote their own effective rate. Emergency
        /// previews use the same urgency multiplier as the applicant ask.
        /// </summary>
        public static bool GoingRate(
            IntercolonyWorldComponent state, SkillDef skill, int minLevel, int termDays,
            CombatClause clause, WageStructure structure,
            out int low, out int high, out int qualified,
            bool emergencyDispatch = false)
        {
            low = 0;
            high = 0;
            qualified = 0;

            if (state == null)
            {
                return false;
            }

            float standing = EmployerReputationService.ScoreFor(state);
            List<LaborProspect> world = LaborCandidateService.Census(state);

            int min = int.MaxValue;
            int max = 0;

            foreach (LaborProspect worker in world)
            {
                if (worker == null)
                {
                    continue;
                }

                if (skill != null && (!worker.CanDo(skill) || worker.LevelOf(skill) < minLevel))
                {
                    continue;
                }

                qualified++;

                int ask = WageStructureUtility.EffectiveDailyWage(
                    structure,
                    LaborCandidateService.DailyWageFor(
                        worker.pricedSkillValue, ProfileFor(state, worker.settlementId),
                        worker.distanceTiles, termDays, standing, clause, emergencyDispatch));

                if (ask < min)
                {
                    min = ask;
                }

                if (ask > max)
                {
                    max = ask;
                }
            }

            if (qualified == 0)
            {
                return false;
            }

            low = min;
            high = max;
            return true;
        }
    }
}
