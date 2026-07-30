using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Standing as an employer (DESIGN.md §40, whose example reads "Tier: Decent Employer").
    /// </summary>
    public enum EmployerTier
    {
        Exploitative,
        Poor,
        Decent,
        Good,
        SoughtAfter
    }

    /// <summary>
    /// How workers across the world regard the colony as a place to work (DESIGN.md §40, §112).
    ///
    /// **Colony-wide, unlike <see cref="CommercialReputation"/>, which is per settlement.** That
    /// asymmetry is deliberate and matches what each thing actually is. A trading record is a
    /// bilateral relationship: Greenmeadow knows whether *it* has been paid. But how a colony
    /// treats the people who work there is not a private matter between two parties — it is a
    /// reputation, and word gets around. §40 illustrates it as a single score for exactly that
    /// reason.
    ///
    /// Per-settlement grievance still exists where it belongs: a settlement the player owes wages
    /// to stops offering workers until the debt is settled, which is carried by
    /// <see cref="LaborDebt"/> rather than by a second score.
    ///
    /// §40 closes with "avoid expensive continuous calculations when event-driven updates are
    /// sufficient", so nothing here is computed on a tick — every field moves when something
    /// happens and is read straight afterwards.
    /// </summary>
    public class EmployerReputation : IExposable
    {
        public const float StartingScore = 50f;
        public const float MinScore = 0f;
        public const float MaxScore = 100f;

        private float score = StartingScore;

        // The four counters §40's example puts on screen, plus the two the escalation produces.
        public int contractsCompleted;
        public int latePayrollIncidents;
        public int employeeDeaths;

        /// <summary>Silver owed and never paid — §40's "unpaid compensation" line.</summary>
        public int unpaidCompensation;

        public int walkOuts;
        public int earlyDismissals;

        /// <summary>Times a worker was drafted into a fight their combat clause did not cover (§42).</summary>
        public int combatClauseBreaches;

        /// <summary>Released workers who were still in the colony when their safe conduct lapsed (§88).</summary>
        public int safePassageDenials;

        public float Score => score;

        public int ScoreDisplay => Mathf.RoundToInt(score);

        public EmployerTier Tier
        {
            get
            {
                if (score < 20f) return EmployerTier.Exploitative;
                if (score < 40f) return EmployerTier.Poor;
                if (score < 65f) return EmployerTier.Decent;
                if (score < 85f) return EmployerTier.Good;
                return EmployerTier.SoughtAfter;
            }
        }

        public string TierLabel()
        {
            switch (Tier)
            {
                case EmployerTier.Exploitative: return "Exploitative employer";
                case EmployerTier.Poor: return "Poor employer";
                case EmployerTier.Decent: return "Decent employer";
                case EmployerTier.Good: return "Good employer";
                default: return "Sought-after employer";
            }
        }

        /// <summary>Total employments that have ended one way or another.</summary>
        public int TotalEmployments => contractsCompleted + walkOuts + earlyDismissals + employeeDeaths;

        public void Adjust(float delta)
        {
            score = Mathf.Clamp(score + delta, MinScore, MaxScore);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref score, "score", StartingScore);
            Scribe_Values.Look(ref contractsCompleted, "contractsCompleted", 0);
            Scribe_Values.Look(ref latePayrollIncidents, "latePayrollIncidents", 0);
            Scribe_Values.Look(ref employeeDeaths, "employeeDeaths", 0);
            Scribe_Values.Look(ref unpaidCompensation, "unpaidCompensation", 0);
            Scribe_Values.Look(ref walkOuts, "walkOuts", 0);
            Scribe_Values.Look(ref earlyDismissals, "earlyDismissals", 0);
            Scribe_Values.Look(ref combatClauseBreaches, "combatClauseBreaches", 0);
            Scribe_Values.Look(ref safePassageDenials, "safePassageDenials", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // A save hand-edited or written by a future version could carry anything.
                score = Mathf.Clamp(score, MinScore, MaxScore);
            }
        }

        /// <summary>§40's screen, near enough verbatim.</summary>
        public string Summary()
        {
            return $"Employer Reputation: {ScoreDisplay} / 100\n" +
                   $"Tier: {TierLabel()}\n\n" +
                   $"Contracts completed: {contractsCompleted}\n" +
                   $"Late payroll incidents: {latePayrollIncidents}\n" +
                   $"Employee deaths: {employeeDeaths}\n" +
                   $"Combat clause breaches: {combatClauseBreaches}\n" +
                   $"Released workers detained: {safePassageDenials}\n" +
                   $"Unpaid compensation: {unpaidCompensation}";
        }

        public override string ToString()
        {
            return $"Employer {ScoreDisplay}/100 ({TierLabel()}) — {contractsCompleted} completed, " +
                   $"{latePayrollIncidents} late payroll, {walkOuts} walk-outs, " +
                   $"{employeeDeaths} deaths, {combatClauseBreaches} clause breaches, " +
                   $"{safePassageDenials} detentions, " +
                   $"{unpaidCompensation} unpaid";
        }
    }
}
