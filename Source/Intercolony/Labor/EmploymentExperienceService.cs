using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Intercolony
{
    internal sealed class EmploymentGoodwillEvaluation
    {
        internal EmploymentGoodwillEvaluation(
            int goodwillDelta, Faction originFaction, float averageMood, string outcomeNote)
        {
            GoodwillDelta = goodwillDelta;
            OriginFaction = originFaction;
            AverageMood = averageMood;
            OutcomeNote = outcomeNote;
        }

        internal int GoodwillDelta { get; }
        internal Faction OriginFaction { get; }
        internal float AverageMood { get; }
        internal string OutcomeNote { get; }
    }

    /// <summary>
    /// Records one normalized mood observation per day for workers who are present and working.
    /// It resolves the accumulated experience once, when EmploymentService closes a contract.
    /// </summary>
    public static class EmploymentExperienceService
    {
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
        /// Evaluates the accumulated employment experience at the end of an employment without
        /// changing the contract or faction. The caller supplies the terminal status because the
        /// contract has already been closed by the time this runs. <paramref name="noticeSkipped"/>
        /// is transient context: a skipped notice is a dismissed employment, but its goodwill
        /// consequence was already recorded by the notice service and must not receive an F09
        /// result as well.
        /// </summary>
        internal static EmploymentGoodwillEvaluation EvaluateGoodwill(
            EmploymentContract contract, EmploymentStatus status, bool noticeSkipped = false)
        {
            if (contract == null)
            {
                return NoResult(null);
            }

            if (status != EmploymentStatus.Completed && status != EmploymentStatus.Dismissed)
            {
                return NoResult(
                    $"F09: no goodwill result for {status}; this is not an ordinary completion or dismissal.");
            }

            if (noticeSkipped)
            {
                return NoResult(
                    "F09: no goodwill result; the skipped notice was already priced.");
            }

            // A breach has already moved the origin faction's goodwill, even if a later ordinary
            // end path closes the contract as Completed or Dismissed rather than Quit.
            if (contract.clauseBreaches > 0)
            {
                return NoResult(
                    "F09: no goodwill result; combat misuse was already priced.");
            }

            // Read the balance knobs when resolving, not while sampling, so setting changes
            // affect future resolutions without changing experience already recorded.
            IntercolonySettings settings = IntercolonyMod.Settings;
            int minimumSamplesForGoodwill = settings.minimumEmploymentDaysForGoodwill;
            float positiveMoodThreshold = settings.positiveExperienceThreshold;
            float negativeMoodThreshold = settings.negativeExperienceThreshold;
            int goodwillImpact = settings.employmentGoodwillImpact;

            if (contract.moodSampleCount <= 0)
            {
                // Zero is NEVER SAMPLED, not a quantity and not an average of zero.
                return NoResult(
                    "F09: no goodwill result; no mood samples were recorded.");
            }

            if (contract.moodSampleCount < minimumSamplesForGoodwill)
            {
                return NoResult(
                    $"F09: no goodwill result; only {contract.moodSampleCount} mood samples were " +
                    $"recorded, and {minimumSamplesForGoodwill} are required.");
            }

            float averageMood = contract.moodSampleTotal / contract.moodSampleCount;
            int goodwillDelta = averageMood >= positiveMoodThreshold
                ? goodwillImpact
                : averageMood <= negativeMoodThreshold
                    ? -goodwillImpact
                    : 0;

            if (goodwillDelta == 0)
            {
                return NoResult(
                    $"F09: average mood {averageMood:0.00}; no goodwill change.");
            }

            Faction originFaction = contract.employerFaction;
            if (originFaction == null)
            {
                return NoResult(
                    $"F09: average mood {averageMood:0.00} qualified for " +
                    $"{goodwillDelta:+0;-0} goodwill, but the origin faction is unavailable; " +
                    "no goodwill change.");
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
                return NoResult(
                    $"F09: average mood {averageMood:0.00} qualified for " +
                    $"{goodwillDelta:+0;-0} goodwill, but {reason}; no goodwill change.");
            }

            // HostilityPolicy.IsAtWar is the mod's one definition of a hostile relationship. A
            // positive employment result must never improve a faction that is already hostile.
            if (goodwillDelta > 0 && HostilityPolicy.IsAtWar(originFaction))
            {
                return NoResult(
                    $"F09: average mood {averageMood:0.00} qualified for " +
                    $"{goodwillDelta:+0;-0} goodwill, but the origin faction is hostile; " +
                    "no goodwill change.");
            }

            return new EmploymentGoodwillEvaluation(
                goodwillDelta,
                originFaction,
                averageMood,
                $"F09: average mood {averageMood:0.00}; goodwill result " +
                $"{goodwillDelta:+0;-0} for {originFaction.Name}.");
        }

        /// <summary>Applies a fresh evaluation and records its outcome on the contract.</summary>
        public static void ResolveGoodwill(EmploymentContract contract, EmploymentStatus status,
            bool noticeSkipped = false)
        {
            ResolveGoodwill(contract, EvaluateGoodwill(contract, status, noticeSkipped));
        }

        internal static void ResolveGoodwill(
            EmploymentContract contract, EmploymentGoodwillEvaluation evaluation)
        {
            if (contract == null || evaluation == null)
            {
                return;
            }

            if (evaluation.GoodwillDelta != 0)
            {
                EmployerReputationService.AffectGoodwill(
                    evaluation.OriginFaction,
                    evaluation.GoodwillDelta,
                    $"{contract.workerName} had an average employment mood of " +
                    $"{evaluation.AverageMood:0.00}");
            }

            AppendOutcomeNote(contract, evaluation.OutcomeNote);
        }

        private static EmploymentGoodwillEvaluation NoResult(string outcomeNote)
        {
            return new EmploymentGoodwillEvaluation(0, null, 0f, outcomeNote);
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
