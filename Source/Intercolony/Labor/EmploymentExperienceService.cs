using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Records one normalized mood observation per day for workers who are present and working.
    /// This service records experience only; it does not evaluate the result or change goodwill.
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
    }
}
