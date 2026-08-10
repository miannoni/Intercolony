using RimWorld;
using Verse;

namespace Intercolony
{
    /// <summary>Side-effect-free animal eligibility and specification matching.</summary>
    public static class AnimalTradeUtility
    {
        public static bool IsEligibleForSale(Pawn pawn)
        {
            return TryValidateSaleEligibility(pawn, out _);
        }

        /// <summary>Returns the first failed gate, in safety-first order.</summary>
        public static bool TryValidateSaleEligibility(Pawn pawn, out string reason)
        {
            if (pawn == null)
            {
                reason = "missing pawn";
                return false;
            }

            RaceProperties race = pawn.def?.race;

            // Highest-severity boundary first: no humanlike may ever enter animal trade.
            if (race != null && race.Humanlike)
            {
                reason = "humanlike";
                return false;
            }

            if (race == null || !race.Animal)
            {
                reason = "not an animal";
                return false;
            }

            if (pawn.Destroyed || pawn.Dead)
            {
                reason = "dead or destroyed";
                return false;
            }

            if (pawn.Faction != Faction.OfPlayer)
            {
                reason = "not in the player faction";
                return false;
            }

            if (pawn.HomeFaction != Faction.OfPlayer)
            {
                reason = "home faction is not the player";
                return false;
            }

            if (pawn.HostFaction != null)
            {
                reason = "has a host faction";
                return false;
            }

            if (pawn.Downed)
            {
                reason = "downed";
                return false;
            }

            if (pawn.InMentalState)
            {
                reason = "in a mental state";
                return false;
            }

            if (pawn.IsPrisoner)
            {
                reason = "prisoner";
                return false;
            }

            if (pawn.IsSlave)
            {
                reason = "slave";
                return false;
            }

            if (pawn.IsColonist)
            {
                reason = "colonist";
                return false;
            }

            if (pawn.IsQuestLodger())
            {
                reason = "quest lodger";
                return false;
            }

            if (pawn.IsQuestHelper())
            {
                reason = "quest helper";
                return false;
            }

            if (EmploymentService.IsEmployee(pawn))
            {
                reason = "active Intercolony employee";
                return false;
            }

            reason = null;
            return true;
        }

        public static bool Matches(Pawn pawn, ThingDef race, AnimalSpec spec)
        {
            if (pawn == null || spec == null || !spec.IsValidFor(race))
            {
                return false;
            }

            if (pawn.def != race)
            {
                return false;
            }

            if (spec.gender.HasValue && pawn.gender != spec.gender.Value)
            {
                return false;
            }

            if (spec.lifeStage != null && pawn.ageTracker?.CurLifeStage != spec.lifeStage)
            {
                return false;
            }

            if (spec.pregnant.HasValue)
            {
                bool isPregnant = pawn.health?.hediffSet?.HasHediff(HediffDefOf.Pregnant) == true;
                if (isPregnant != spec.pregnant.Value)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
