using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// How a worker is paid (DESIGN.md §37).
    ///
    /// §37 gives prepaid a "discounted total cost" and names its risk plainly: the employee may
    /// die, or business conditions may change, and the money is already gone. That trade-off is
    /// the whole point of offering a choice — a cheaper total against carrying the risk yourself.
    /// </summary>
    public enum WageStructure
    {
        /// <summary>Whole term paid at hire, at a discount. Phase 16's only option.</summary>
        Prepaid,

        /// <summary>Paid at the end of each day worked. §37: "best for short-term flexibility".</summary>
        Daily,

        /// <summary>Paid at the end of each quadrum worked. §37: "likely default for longer employment".</summary>
        Quadrum
    }

    public static class WageStructureUtility
    {
        /// <summary>
        /// What prepaying saves. §37 lists a discount as prepaid's benefit without naming a
        /// number; 10% is enough to be a real choice at the margins without making periodic
        /// payment obviously wrong.
        /// </summary>
        public const float PrepaidDiscount = 0.10f;

        public static int IntervalDays(this WageStructure structure)
        {
            switch (structure)
            {
                case WageStructure.Daily:
                    return 1;
                case WageStructure.Quadrum:
                    return GenDate.DaysPerQuadrum;
                default:
                    return 0;
            }
        }

        public static bool IsPeriodic(this WageStructure structure)
        {
            return structure != WageStructure.Prepaid;
        }

        public static string Label(this WageStructure structure)
        {
            switch (structure)
            {
                case WageStructure.Daily:
                    return "daily";
                case WageStructure.Quadrum:
                    return "per quadrum";
                default:
                    return "prepaid";
            }
        }

        /// <summary>What the player hands over at the moment of hiring.</summary>
        public static int UpFrontCost(WageStructure structure, int dailyWage, int termDays)
        {
            return structure == WageStructure.Prepaid ? TotalCost(structure, dailyWage, termDays) : 0;
        }

        /// <summary>
        /// Total cost across the whole term. Prepaid is cheaper; periodic structures cost the
        /// full rate because the player keeps their silver until the work is done.
        /// </summary>
        public static int TotalCost(WageStructure structure, int dailyWage, int termDays)
        {
            int gross = dailyWage * termDays;
            return structure == WageStructure.Prepaid
                ? Mathf.Max(1, Mathf.RoundToInt(gross * (1f - PrepaidDiscount)))
                : gross;
        }

        /// <summary>Amount due for one full pay period.</summary>
        public static int PeriodCost(WageStructure structure, int dailyWage)
        {
            return dailyWage * structure.IntervalDays();
        }

        /// <summary>
        /// A one-line explanation of the structure, for the hiring dialog. Written so the
        /// trade-off is legible before committing rather than discovered afterwards (§111).
        /// </summary>
        public static string Explain(WageStructure structure, int dailyWage, int termDays)
        {
            int total = TotalCost(structure, dailyWage, termDays);
            int gross = dailyWage * termDays;

            switch (structure)
            {
                case WageStructure.Prepaid:
                    return $"{total} silver now, all of it. " +
                           $"Saves {gross - total} against paying as they work — but if they die " +
                           "or you change your mind, the silver is spent.";
                case WageStructure.Daily:
                    return $"{dailyWage} silver at the end of each day, {total} over the full term. " +
                           "Nothing owed if the arrangement ends early.";
                default:
                    int period = PeriodCost(structure, dailyWage);
                    return $"{period} silver every {GenDate.DaysPerQuadrum} days, {total} over the " +
                           "full term. A short term pays the remainder pro rata at the end.";
            }
        }
    }
}
