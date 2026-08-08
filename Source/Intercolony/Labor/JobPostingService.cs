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
    /// §114's acceptance criterion is the whole design brief: *"Higher wages and better employer
    /// reputation measurably improve applicant quantity/quality."* Both fall out of one rule rather
    /// than being tuned separately — see <see cref="MatchAll"/>.
    ///
    /// The market limits itself. Every open posting is matched against **one** world labor pool per
    /// refresh, and each worker applies to at most one posting, so ten identical postings are
    /// exactly one posting. That is why nothing here charges a fee or caps how many jobs a player
    /// may advertise: the scarce thing is workers, not advertisements.
    /// </summary>
    public static class JobPostingService
    {
        /// <summary>How long a posting stays up before lapsing, unless the player picks otherwise.</summary>
        public const int DefaultLifespanDays = 30;

        public const int MinLifespanDays = 5;
        public const int MaxLifespanDays = 120;

        /// <summary>
        /// Waiting applicants a posting will hold beyond the positions it is filling. Some slack is
        /// the point — §35.2's own example shows 4 applicants for 3 positions, because choosing is
        /// the interesting part — but an unbounded queue would pin pawns in the world pool forever.
        /// </summary>
        public const int ApplicantSlack = 3;

        /// <summary>
        /// Days an applicant will wait before withdrawing. Long enough that the player need not
        /// watch the tab, short enough that a forgotten posting stops holding people hostage.
        /// </summary>
        public const int ApplicantPatienceDays = 12;

        // --- Creating ----------------------------------------------------------------------

        public static JobPosting TryPost(
            IntercolonyWorldComponent state, SkillDef skill, int minSkillLevel, int positions,
            int termDays, int wageOffered, WageStructure structure, CombatClause clause,
            int lifespanDays, out string failReason)
        {
            failReason = null;

            if (state == null)
            {
                failReason = "No world state.";
                return null;
            }

            if (positions < 1)
            {
                failReason = "A posting needs at least one position.";
                return null;
            }

            if (termDays < 1 || termDays > LaborCandidateService.MaxTermDays)
            {
                failReason = $"Term must be between 1 and {LaborCandidateService.MaxTermDays} days.";
                return null;
            }

            if (wageOffered < 1)
            {
                failReason = "Offer at least 1 silver a day.";
                return null;
            }

            JobPosting posting = new JobPosting
            {
                id = state.NextId(),
                skill = skill,
                minSkillLevel = skill == null ? 0 : Mathf.Clamp(minSkillLevel, 0, 20),
                positions = positions,
                termDays = termDays,
                wageOffered = wageOffered,
                wageStructure = structure,
                combatClause = clause,
                postedTick = GenTicks.TicksGame,
                expiryTick = GenTicks.TicksGame +
                             Mathf.Clamp(lifespanDays, MinLifespanDays, MaxLifespanDays) * GenDate.TicksPerDay,
                status = JobPostingStatus.Open
            };

            state.AddPosting(posting);

            Messages.Message(
                $"Posted: {posting.Headline()}.",
                MessageTypeDefOf.PositiveEvent, historical: false);

            IntercolonyLog.Message($"Posted: {posting}");
            return posting;
        }

        // --- Matching ----------------------------------------------------------------------

        /// <summary>
        /// Exposes every open posting to this cycle's world labor pool.
        ///
        /// **The one rule that makes §114's acceptance criterion true, rather than tuned:**
        /// a worker applies if they meet the skill bar and the offered wage clears what they would
        /// have charged on the open market. Nothing else. From that:
        ///
        /// * a higher offer clears more workers, and because better workers ask more, it clears
        ///   *better* ones — so quantity and quality both rise with wage, from one comparison;
        /// * Phase 19's <c>WageFactor</c> already multiplies every asking price by employer
        ///   reputation, so a bad employer's offer clears fewer people with no separate mechanism.
        ///
        /// Writing a second, purpose-built "attractiveness" formula would have been the obvious
        /// approach and the wrong one: two models of what a worker is worth would drift apart, and
        /// the hiring tab and the posting tab would start quoting different numbers for the same
        /// person. §113 learned that lesson about policy; it applies just as well to pricing.
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

            // Phase one: every worker picks the one posting that suits them best, without regard
            // to whether it already has a queue.
            //
            // Ignoring room here is deliberate and is what makes ten identical postings behave like
            // one. If a full posting pushed workers onto the next identical notice, advertising the
            // same job five times would collect five queues, and the market would stop being the
            // scarce thing. A worker who wanted the job that filled up simply does not apply.
            Dictionary<int, List<Interest>> interested = new Dictionary<int, List<Interest>>();

            foreach (LaborProspect worker in world)
            {
                if (worker == null)
                {
                    continue;
                }

                JobPosting best = null;
                int bestSurplus = int.MinValue;
                int bestAsk = 0;

                foreach (JobPosting posting in open)
                {
                    if (!posting.MeetsRequirement(worker))
                    {
                        continue;
                    }

                    int ask = Ask(state, worker, posting, standing);
                    int surplus = posting.wageOffered - ask;
                    if (surplus < 0)
                    {
                        continue;
                    }

                    // Ties broken by posting id so the outcome is reproducible: the same census
                    // and the same postings must produce the same applicants after a reload.
                    if (surplus > bestSurplus || (surplus == bestSurplus && best != null && posting.id < best.id))
                    {
                        best = posting;
                        bestSurplus = surplus;
                        bestAsk = ask;
                    }
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

                queue.Add(new Interest { worker = worker, ask = bestAsk });
            }

            // Phase two: each posting takes the **best** of the people who want it, not the first
            // few the census happened to list.
            //
            // This is what keeps a generous offer worth making once the queue is full. Below the
            // cap a better wage buys more replies; above it, it buys better ones — because a higher
            // offer clears stronger workers who would not have applied at all, and they now
            // displace the weaker ones rather than arriving behind them. Taking the first N in
            // census order threw that away and made every offer above the cheapest look identical.
            foreach (JobPosting posting in open)
            {
                if (!interested.TryGetValue(posting.id, out List<Interest> queue))
                {
                    continue;
                }

                queue.Sort((a, b) => Desirability(b.worker, posting).CompareTo(
                    Desirability(a.worker, posting)));

                int room = Room(posting);
                int taken = 0;

                for (int i = 0; i < queue.Count && taken < room; i++)
                {
                    if (Apply(posting, queue[i].worker, queue[i].ask))
                    {
                        taken++;
                    }
                }

                if (taken > 0)
                {
                    gained[posting.id] = taken;
                }
            }

            foreach (JobPosting posting in open)
            {
                gained.TryGetValue(posting.id, out int arrived);
                Report(state, posting, arrived, standing);
            }
        }

        /// <summary>One worker's willingness to take one posting, before anyone has been chosen.</summary>
        private struct Interest
        {
            public LaborProspect worker;
            public int ask;
        }

        /// <summary>How many more applicants this posting will hold.</summary>
        private static int Room(JobPosting posting)
        {
            return Mathf.Max(0,
                posting.PositionsRemaining + ApplicantSlack - posting.Applicants.Count);
        }

        /// <summary>
        /// How much an employer would want this worker for this job.
        ///
        /// The advertised skill comes first, because that is what the posting asked for: a
        /// Construction 16 generalist beats a Construction 11 prodigy when the job is building.
        /// Overall ability breaks the tie, so between two equally qualified applicants the more
        /// capable one is the one who turns up.
        /// </summary>
        private static float Desirability(LaborProspect worker, JobPosting posting)
        {
            float required = posting.skill == null ? 0f : worker.LevelOf(posting.skill);
            return required * 100f + worker.pricedSkillValue;
        }

        /// <summary>
        /// Turns a census record into an actual applicant - the only point at which a pawn is built.
        ///
        /// This is what makes a deep market affordable. The census can be hundreds of workers
        /// because none of them exist until one of them applies for something; generating a pawn is
        /// the expensive call, and it happens once per applicant rather than once per worker
        /// considered.
        /// </summary>
        private static bool Apply(JobPosting posting, LaborProspect worker, int ask)
        {
            Pawn pawn = worker.Materialise();
            if (pawn == null)
            {
                return false;
            }

            // Nothing else owns this pawn - it was built for this list. KeepForever rather than
            // Decide for the reason the notes give:
            // WorldPawnGC knows nothing about a job posting and would collect an applicant the
            // player is still deciding about.
            if (!Find.WorldPawns.Contains(pawn))
            {
                Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
            }

            posting.Applicants.Add(new JobApplicant
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
                appliedTick = GenTicks.TicksGame
            });

            return true;
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
                worker.distanceTiles, posting.termDays, standing, posting.combatClause);
        }

        /// <summary>
        /// Tells the player what their advertisement did, and — when it did nothing — why.
        ///
        /// The "why" is the point. A posting that draws nobody is indistinguishable from a broken
        /// feature unless the game says which of the two reasons it was: the wage is below what
        /// anyone with that skill will accept, or nobody with that skill exists to ask.
        /// </summary>
        private static void Report(IntercolonyWorldComponent state, JobPosting posting, int arrived,
            float standing)
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
                    $"{posting.Applicants.Count} waiting in total, for {posting.PositionsRemaining} " +
                    $"open position{(posting.PositionsRemaining == 1 ? "" : "s")}. Review them in the " +
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

            IntercolonyLetters.Send(
                IntercolonyLetterImportance.Chatty,
                "No applicants",
                $"Your posting — {posting.Headline()} — drew no replies.\n\n" +
                ExplainSilence(state, posting, standing),
                LetterDefOf.NeutralEvent);
        }

        /// <summary>
        /// Works out why nobody applied, by asking the pool the same question the matcher did.
        ///
        /// Deliberately measured rather than guessed: it finds the cheapest qualified worker in the
        /// world and reports what they actually wanted. A letter that said "try offering more" when
        /// the real problem was that nobody in the world has Construction 15 would be worse than no
        /// letter at all.
        /// </summary>
        public static string ExplainSilence(IntercolonyWorldComponent state, JobPosting posting,
            float standing)
        {
            List<LaborProspect> world = LaborCandidateService.Census(state);

            int qualified = 0;
            int cheapestAsk = int.MaxValue;

            foreach (LaborProspect worker in world)
            {
                if (worker == null || !posting.MeetsRequirement(worker))
                {
                    continue;
                }

                qualified++;

                int ask = Ask(state, worker, posting, standing);
                if (ask < cheapestAsk)
                {
                    cheapestAsk = ask;
                }
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
                             "it: people charge more to work somewhere with your record.";
            }

            return $"{qualified} worker{(qualified == 1 ? "" : "s")} reachable can do the job, but the " +
                   $"cheapest of them wants {cheapestAsk} silver a day and you offered " +
                   $"{posting.wageOffered}." + reputation;
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

                if (now >= posting.expiryTick)
                {
                    Close(posting, JobPostingStatus.Expired,
                        posting.hired > 0
                            ? $"Expired after filling {posting.hired} of {posting.positions}."
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
            if (posting == null || !posting.IsOpen)
            {
                return;
            }

            posting.status = status;
            posting.outcomeNote = note ?? "";

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
        /// Takes on an applicant at the posted wage.
        ///
        /// The wage comes from the posting, not from the worker — that is the inversion §35.2
        /// describes, and it is why this cannot simply call the candidate hire path.
        /// </summary>
        public static EmploymentContract TryAccept(
            IntercolonyWorldComponent state, JobPosting posting, JobApplicant applicant,
            Map paymentMap, out string failReason)
        {
            failReason = null;

            if (state == null || posting == null || applicant?.pawn == null)
            {
                failReason = "Nothing to accept.";
                return null;
            }

            if (!posting.IsOpen || posting.PositionsRemaining <= 0)
            {
                failReason = "That posting is already filled.";
                return null;
            }

            EmploymentContract contract = EmploymentService.TryHireApplicant(
                state, applicant, posting, paymentMap, out failReason);

            if (contract == null)
            {
                return null;
            }

            posting.Applicants.Remove(applicant);
            posting.hired++;

            if (posting.PositionsRemaining <= 0)
            {
                Close(posting, JobPostingStatus.Filled,
                    $"All {posting.positions} position{(posting.positions == 1 ? "" : "s")} filled.");
            }

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
        /// How many workers in the census would take these terms — the market's response before the
        /// applicant queue truncates it.
        ///
        /// The player never sees this number: they see the queue, and §35.2's screen shows
        /// applicants rather than interest. It exists so the self-test can measure the *market*
        /// rather than the queue length, which saturates at the cap and would hide the very
        /// smoothness the census was built to produce.
        /// </summary>
        public static int CountInterested(
            IntercolonyWorldComponent state, SkillDef skill, int minLevel, int termDays,
            int wageOffered, CombatClause clause)
        {
            if (state == null)
            {
                return 0;
            }

            float standing = EmployerReputationService.ScoreFor(state);
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

                int ask = LaborCandidateService.DailyWageFor(
                    worker.pricedSkillValue, ProfileFor(state, worker.settlementId),
                    worker.distanceTiles, termDays, standing, clause);

                if (wageOffered >= ask)
                {
                    count++;
                }
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
            CombatClause clause, out int low, out int high, out int qualified)
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

                int ask = LaborCandidateService.DailyWageFor(
                    worker.pricedSkillValue, ProfileFor(state, worker.settlementId),
                    worker.distanceTiles, termDays, standing, clause);

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
