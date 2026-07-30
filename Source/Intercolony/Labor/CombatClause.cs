using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// What an employee has agreed to be pointed at (DESIGN.md §42).
    ///
    /// §42 states the problem in one line: *"Without constraints, hired workers become cheap meat
    /// shields."* The clause is the constraint, and it works by pricing rather than by prohibition
    /// — nothing here stops the player drafting a civilian. It makes doing so cost more than hiring
    /// someone who signed up for it.
    /// </summary>
    public enum CombatClause
    {
        /// <summary>§42 Civilian — not expected to fight. Self-defense is acceptable; being drafted is not.</summary>
        Civilian,

        /// <summary>§42 Armed Employee — may defend the colony. Higher wage. Not for expeditions.</summary>
        Armed,

        /// <summary>§42 Security Contractor — an explicit combat worker. Much higher wage, no restrictions.</summary>
        Security
    }

    public static class CombatClauseUtility
    {
        /// <summary>
        /// Every clause in draw order, cheapest first. A field rather than
        /// <c>Enum.GetValues</c> so the UI order is a decision rather than a consequence of
        /// declaration order.
        /// </summary>
        public static readonly CombatClause[] All =
        {
            CombatClause.Civilian, CombatClause.Armed, CombatClause.Security
        };

        /// <summary>
        /// Wage multiplier. §42 says only "higher wage" and "much higher wage", so the numbers are
        /// ours: half again for a worker who will hold a line on your own map, and two and a half
        /// times for one who will go anywhere. The gaps are wide because the whole mechanism fails
        /// if drafting a civilian is cheaper than hiring a soldier, which is exactly the failure
        /// §42 exists to prevent.
        /// </summary>
        public static float WageFactor(this CombatClause clause)
        {
            switch (clause)
            {
                case CombatClause.Armed:
                    return 1.5f;
                case CombatClause.Security:
                    return 2.5f;
                default:
                    return 1f;
            }
        }

        /// <summary>
        /// Days of wage owed if the employee dies (§43).
        ///
        /// Anchored to §43's own worked example — it shows 2,400 silver for a death, and a mid-range
        /// worker earns around 40 a day, so a civilian death is 60 days' wage. A security
        /// contractor's death is a fraction of that: they were hired to take the risk, and their
        /// wage already carried the premium for it.
        /// </summary>
        public static int DeathCompensationDays(this CombatClause clause)
        {
            switch (clause)
            {
                case CombatClause.Armed:
                    return 30;
                case CombatClause.Security:
                    return 12;
                default:
                    return 60;
            }
        }

        /// <summary>
        /// Days of wage owed per permanent injury taken during employment (§43 "injury and death
        /// compensation"). A quarter of the death figure, so a maimed civilian is expensive but not
        /// a colony-ending event — §43 warns explicitly against balancing this so hard that
        /// "ordinary RimWorld danger makes employees unusable".
        /// </summary>
        public static int InjuryCompensationDays(this CombatClause clause)
        {
            return Mathf.Max(1, clause.DeathCompensationDays() / 4);
        }

        /// <summary>
        /// Whether drafting this worker into a fight is within the terms.
        ///
        /// The map test is what separates Armed from Security: §42 gives an armed employee
        /// "colony defense", which is a place as much as an activity. Marching them to someone
        /// else's settlement is a different job, and it is the one Security is for.
        /// </summary>
        public static bool PermitsCombat(this CombatClause clause, bool onPlayerHomeMap)
        {
            switch (clause)
            {
                case CombatClause.Security:
                    return true;
                case CombatClause.Armed:
                    return onPlayerHomeMap;
                default:
                    return false;
            }
        }

        public static string Label(this CombatClause clause)
        {
            switch (clause)
            {
                case CombatClause.Armed:
                    return "armed employee";
                case CombatClause.Security:
                    return "security contractor";
                default:
                    return "civilian";
            }
        }

        public static string LabelCap(this CombatClause clause)
        {
            switch (clause)
            {
                case CombatClause.Armed:
                    return "Armed employee";
                case CombatClause.Security:
                    return "Security contractor";
                default:
                    return "Civilian";
            }
        }

        /// <summary>
        /// What the clause permits, for the hiring dialog. Written to be read before committing,
        /// the same standard §111 set for wage structures.
        /// </summary>
        public static string Explain(this CombatClause clause)
        {
            switch (clause)
            {
                case CombatClause.Armed:
                    return "Will fight to defend this colony. Drafting them here is within the terms; " +
                           "taking them off the map to attack someone is not.";
                case CombatClause.Security:
                    return "Hired to fight, anywhere, with no restrictions. Compensation if they die " +
                           "is a fraction of a civilian's — the risk is already in the wage.";
                default:
                    return "Will not be drafted. They will defend themselves if attacked, but drafting " +
                           "them breaches the contract, and a civilian death costs the most to settle.";
            }
        }

        /// <summary>
        /// A single line naming the clause, its cost and what a death would cost, for lists.
        /// </summary>
        public static string Summary(this CombatClause clause, int dailyWage)
        {
            return $"{clause.LabelCap()} — {dailyWage} silver/day, " +
                   $"{dailyWage * clause.DeathCompensationDays()} silver if they die";
        }

        /// <summary>
        /// Whether this pawn is standing somewhere that counts as defending the colony. A caravan
        /// or a hostile settlement map is not; the player's own settlement is.
        /// </summary>
        public static bool IsOnPlayerHomeMap(Pawn pawn)
        {
            Map map = pawn?.Map;
            return map != null && map.IsPlayerHome;
        }
    }
}
