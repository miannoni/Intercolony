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
    /// F25 makes the posting an RFQ: a worker applies when they meet the requirement, and the
    /// application carries the worker's own asking wage. Reputation still controls the census's
    /// availability and quality, while the requirement controls who can answer — see <see
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
        /// Days an applicant will wait before withdrawing. Long enough that the player need not
        /// watch the tab, short enough that a forgotten posting stops holding people hostage.
        /// </summary>
        public const int ApplicantPatienceDays = 12;

        // Separates applicant-queue shuffles from the labor-census random stream.
        private const int ApplicantShuffleSalt = 0x4C41_5445;

        // --- Creating ----------------------------------------------------------------------

        public static JobPosting TryPost(
            IntercolonyWorldComponent state, SkillDef skill, int minSkillLevel,
            int termDays, WageStructure structure, CombatClause clause,
            out string failReason)
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
                postedTick = GenTicks.TicksGame,
                expiryTick = -1,
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
                int bestAsk = 0;

                foreach (JobPosting posting in open)
                {
                    if (!posting.MeetsRequirement(worker))
                    {
                        continue;
                    }

                    int ask = Ask(state, worker, posting, standing);

                    // A prospect chooses the posting that pays them the most. Ask includes the
                    // posting's term and combat clause, so this is a real choice: the same prospect
                    // can ask more for Armed or Security work than for Civilian work. Break equal
                    // asks by lower posting id so a seeded census produces the same market after a
                    // save reload.
                    if (best == null || ask > bestAsk ||
                        (ask == bestAsk && posting.id < best.id))
                    {
                        best = posting;
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
                    if (!interested.TryGetValue(posting.id, out List<Interest> queue))
                    {
                        continue;
                    }

                    int room = Room(posting);
                    if (room <= 0)
                    {
                        continue;
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
            }
            finally
            {
                Rand.PopState();
            }

            foreach (JobPosting posting in open)
            {
                gained.TryGetValue(posting.id, out int arrived);
                Report(state, posting, arrived, standing);
            }
        }

        /// <summary>One qualified worker's chosen posting and the ask they quoted for it.</summary>
        private struct Interest
        {
            public LaborProspect worker;
            public int ask;
        }

        /// <summary>How many more applicants this posting will hold.</summary>
        private static int Room(JobPosting posting)
        {
            return Mathf.Max(0, MaxWaitingApplicants - posting.Applicants.Count);
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
        /// feature unless the game says whether nobody qualified or whether the qualified pool did
        /// not answer this refresh. The posted wage is intentionally not one of those reasons.
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

            IntercolonyLetters.Send(
                IntercolonyLetterImportance.Chatty,
                "No applicants",
                $"Your posting — {posting.Headline()} — drew no replies.\n\n" +
                ExplainSilence(state, posting, standing),
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
        /// Takes on an applicant at the applicant's own market ask.
        ///
        /// EmploymentService resolves the contract rate from the applicant's saved quote. The
        /// posted wage remains saved posting data in this slice, but it neither chose the applicant
        /// nor sets the hire rate.
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

            if (!posting.IsOpen)
            {
                failReason = "That posting is not open.";
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
            CombatClause clause, out int low, out int high, out int qualified)
        {
            return GoingRate(
                state, skill, minLevel, termDays, clause, WageStructure.Quadrum,
                out low, out high, out qualified);
        }

        /// <summary>
        /// The same band for a specific wage structure. Paying by the day carries a premium, so
        /// a daily posting genuinely costs more than a per-quadrum one for the same worker. The
        /// band is market information for the player, not an application threshold: applicants
        /// still arrive based on the requirement and quote their own effective rate.
        /// </summary>
        public static bool GoingRate(
            IntercolonyWorldComponent state, SkillDef skill, int minLevel, int termDays,
            CombatClause clause, WageStructure structure,
            out int low, out int high, out int qualified)
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
                        worker.distanceTiles, termDays, standing, clause));

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
