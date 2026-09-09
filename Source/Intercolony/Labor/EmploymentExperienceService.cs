using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Records one normalized mood observation per day for workers who are present and working.
    /// It resolves the accumulated experience once, when EmploymentService closes a contract.
    /// </summary>
    public static class EmploymentExperienceService
    {
        // These are starting values for balance, not fixed by the plan.
        private const int MinimumSamplesForGoodwill = 10;
        private const float PositiveMoodThreshold = 0.75f;
        private const float NegativeMoodThreshold = 0.35f;
        private const int GoodwillDelta = 3;

        public static void Sample(List<EmploymentContract> contracts)
        {
            if (contracts == null)
            {
                return;
            }

            for (int i = contracts.Count - 1; i >= 0; i--)
            {
                EmploymentContract contract = contracts[i];
                if (contract == null || contract.status != EmploymentStatus.Active ||
                    contract.refusingWork)
                {
                    continue;
                }

                Pawn worker = contract.pawn;
                if (worker == null || !worker.Spawned || worker.Dead || worker.Downed ||
                    worker.needs == null || worker.needs.mood == null)
                {
                    continue;
                }

                Need_Mood mood = worker.needs.mood;
                contract.moodSampleTotal += mood.CurLevelPercentage;
                contract.moodSampleCount++;
            }
        }

        /// <summary>
        /// Resolves the accumulated employment experience at the end of an employment. The caller
        /// supplies the terminal status because the contract has already been closed by the time
        /// this runs. <paramref name="noticeSkipped"/> is transient context: a skipped notice is a
        /// dismissed employment, but its goodwill consequence was already recorded by the notice
        /// service and must not receive an F09 result as well.
        /// </summary>
        public static void ResolveGoodwill(EmploymentContract contract, EmploymentStatus status,
            bool noticeSkipped = false)
        {
            if (contract == null)
            {
                return;
            }

            if (status != EmploymentStatus.Completed && status != EmploymentStatus.Dismissed)
            {
                AppendOutcomeNote(contract,
                    $"F09: no goodwill result for {status}; this is not an ordinary completion or dismissal.");
                return;
            }

            if (noticeSkipped)
            {
                AppendOutcomeNote(contract,
                    "F09: no goodwill result; the skipped notice was already priced.");
                return;
            }

            // A breach has already moved the origin faction's goodwill, even if a later ordinary
            // end path closes the contract as Completed or Dismissed rather than Quit.
            if (contract.clauseBreaches > 0)
            {
                AppendOutcomeNote(contract,
                    "F09: no goodwill result; combat misuse was already priced.");
                return;
            }

            if (contract.moodSampleCount <= 0)
            {
                // Zero is NEVER SAMPLED, not a quantity and not an average of zero.
                AppendOutcomeNote(contract,
                    "F09: no goodwill result; no mood samples were recorded.");
                return;
            }

            if (contract.moodSampleCount < MinimumSamplesForGoodwill)
            {
                AppendOutcomeNote(contract,
                    $"F09: no goodwill result; only {contract.moodSampleCount} mood samples were " +
                    $"recorded, and {MinimumSamplesForGoodwill} are required.");
                return;
            }

            float averageMood = contract.moodSampleTotal / contract.moodSampleCount;
            int goodwillDelta = averageMood >= PositiveMoodThreshold
                ? GoodwillDelta
                : averageMood <= NegativeMoodThreshold
                    ? -GoodwillDelta
                    : 0;

            if (goodwillDelta == 0)
            {
                AppendOutcomeNote(contract,
                    $"F09: average mood {averageMood:0.00}; no goodwill change.");
                return;
            }

            Faction originFaction = contract.employerFaction;
            if (originFaction == null)
            {
                AppendOutcomeNote(contract,
                    $"F09: average mood {averageMood:0.00} qualified for " +
                    $"{goodwillDelta:+0;-0} goodwill, but the origin faction is unavailable; " +
                    "no goodwill change.");
                return;
            }

            // Match the existing writer's unavailable-faction guard here so the outcome note says
            // what happened without resolving a replacement faction from the pawn or factionName.
            if (originFaction.IsPlayer || originFaction.Hidden || originFaction.defeated)
            {
                string reason = originFaction.IsPlayer
                    ? "the origin faction is the player faction"
                    : originFaction.Hidden
                        ? "the origin faction is hidden"
                        : "the origin faction is defeated";
                AppendOutcomeNote(contract,
                    $"F09: average mood {averageMood:0.00} qualified for " +
                    $"{goodwillDelta:+0;-0} goodwill, but {reason}; no goodwill change.");
                return;
            }

            // HostilityPolicy.IsAtWar is the mod's one definition of a hostile relationship. A
            // positive employment result must never improve a faction that is already hostile.
            if (goodwillDelta > 0 && HostilityPolicy.IsAtWar(originFaction))
            {
                AppendOutcomeNote(contract,
                    $"F09: average mood {averageMood:0.00} qualified for " +
                    $"{goodwillDelta:+0;-0} goodwill, but the origin faction is hostile; " +
                    "no goodwill change.");
                return;
            }

            EmployerReputationService.AffectGoodwill(
                originFaction,
                goodwillDelta,
                $"{contract.workerName} had an average employment mood of {averageMood:0.00}");

            AppendOutcomeNote(contract,
                $"F09: average mood {averageMood:0.00}; goodwill result " +
                $"{goodwillDelta:+0;-0} for {originFaction.Name}.");
        }

        private static void AppendOutcomeNote(EmploymentContract contract, string note)
        {
            if (contract == null || string.IsNullOrEmpty(note))
            {
                return;
            }

            contract.outcomeNote = string.IsNullOrEmpty(contract.outcomeNote)
                ? note
                : contract.outcomeNote + " " + note;
        }
    }
}
