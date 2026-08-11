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

        /// <summary>
        /// What paying by the day costs on top of the daily rate. Paying as you go is
        /// optionality: the player may end the arrangement any morning and owe nothing further.
        /// The worker carries that risk — they travelled for a job that might last a day — so
        /// they charge for it. Per-quadrum is the baseline because a quadrum's notice is a real
        /// commitment, and prepaid is cheaper still because the player takes all the risk.
        /// </summary>
        public const float DailyPremium = 0.35f;

        /// <summary>
        /// Days of wage due at hire for a pay-as-you-go arrangement, covering the journey
        /// regardless of how long the worker ends up staying. Daily costs more than per-quadrum
        /// because it is the arrangement most likely to end almost immediately.
        /// </summary>
        public const int DailySigningFeeDays = 5;
        public const int QuadrumSigningFeeDays = 2;

        /// <summary>The per-day rate actually charged under this structure.</summary>
        public static int EffectiveDailyWage(WageStructure structure, int dailyWage)
        {
            return structure == WageStructure.Daily
                ? Mathf.Max(1, Mathf.RoundToInt(dailyWage * (1f + DailyPremium)))
                : dailyWage;
        }

        /// <summary>Days of wage taken as a signing fee, or zero when there is none.</summary>
        public static int SigningFeeDays(WageStructure structure)
        {
            switch (structure)
            {
                case WageStructure.Daily:
                    return DailySigningFeeDays;
                case WageStructure.Quadrum:
                    return QuadrumSigningFeeDays;
                default:
                    // Prepaid hands over the whole term at hire; a fee on top would be charging
                    // twice for the same commitment.
                    return 0;
            }
        }

        /// <summary>The one-off fee itself, priced off the structure's own daily rate.</summary>
        public static int SigningFee(WageStructure structure, int dailyWage)
        {
            int days = SigningFeeDays(structure);
            return days <= 0
                ? 0
                : Mathf.Max(1, EffectiveDailyWage(structure, dailyWage) * days);
        }

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
            return structure == WageStructure.Prepaid
                ? TotalCost(structure, dailyWage, termDays)
                : SigningFee(structure, dailyWage);
        }

        /// <summary>
        /// Total cost across the whole term, including anything paid at hire. Prepaid is
        /// cheapest, per-quadrum sits in the middle, and paying by the day is dearest — the
        /// player is buying the freedom to stop at any point and that has a price.
        /// </summary>
        public static int TotalCost(WageStructure structure, int dailyWage, int termDays)
        {
            if (structure == WageStructure.Prepaid)
            {
                int gross = dailyWage * termDays;
                return Mathf.Max(1, Mathf.RoundToInt(gross * (1f - PrepaidDiscount)));
            }

            return EffectiveDailyWage(structure, dailyWage) * termDays +
                   SigningFee(structure, dailyWage);
        }

        /// <summary>Amount due for one full pay period.</summary>
        public static int PeriodCost(WageStructure structure, int dailyWage)
        {
            return EffectiveDailyWage(structure, dailyWage) * structure.IntervalDays();
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
                           $"The cheapest way to hire — but if they die " +
                           "or you change your mind, the silver is spent.";
                case WageStructure.Daily:
                    return $"{SigningFee(structure, dailyWage)} silver to sign, then " +
                           $"{EffectiveDailyWage(structure, dailyWage)} at the end of each day — " +
                           $"{total} over the full term. The dearest way to hire, because you " +
                           "can stop any morning and owe nothing more.";
                default:
                    int period = PeriodCost(structure, dailyWage);
                    return $"{SigningFee(structure, dailyWage)} silver to sign, then {period} " +
                           $"every {GenDate.DaysPerQuadrum} days — {total} over the full term. " +
                           "A short term pays the remainder pro rata at the end.";
            }
        }
    }
}
