using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Records employer conduct and turns it into hiring conditions (DESIGN.md §40, §112).
    ///
    /// §112's acceptance criterion is the whole design brief: *"A bad employer experiences
    /// meaningfully worse hiring conditions."* Meaningfully — so the effects have to be big enough
    /// to feel, and they compound: a bad employer pays more per day, sees fewer workers on offer,
    /// and gets the weaker end of the candidates they do see.
    ///
    /// §39 steps 7 to 9 are the missing tail of Phase 18's escalation, and they land here:
    /// reputation falls, source faction goodwill falls, and future workers become more expensive
    /// or unavailable.
    /// </summary>
    public static class EmployerReputationService
    {
        // --- Event weights -----------------------------------------------------------------
        //
        // §40 splits signals into positive and negative. Negatives are weighted harder than
        // positives on purpose: a reputation as an employer is easier to lose than to build, and
        // a player who lets someone walk out unpaid should not be able to grind it back with a
        // handful of short uneventful contracts.

        private const float ContractCompleted = 3f;
        private const float PayrollPaidClearingArrears = 1.5f;

        private const float PayrollMissed = -6f;
        private const float WorkerWalkedOut = -18f;
        private const float EmployeeDied = -12f;
        private const float EarlyDismissal = -2f;
        private const float DebtSettledLate = 2f;

        /// <summary>Goodwill lost with the worker's own faction when they are badly treated (§39 step 8).</summary>
        private const int GoodwillWalkOut = -8;
        private const int GoodwillDeath = -5;

        public static EmployerReputation For(IntercolonyWorldComponent state)
        {
            return state?.EmployerStanding;
        }

        public static float ScoreFor(IntercolonyWorldComponent state)
        {
            return state?.EmployerStanding?.Score ?? EmployerReputation.StartingScore;
        }

        // --- Events (§40) ------------------------------------------------------------------

        public static void NoteContractCompleted(IntercolonyWorldComponent state, EmploymentContract contract)
        {
            EmployerReputation rep = For(state);
            if (rep == null)
            {
                return;
            }

            rep.contractsCompleted++;

            // A longer contract served out is worth more than a three-day one: §40 lists "safe
            // contract completion" as the positive, and a season of it says more than a weekend.
            float lengthBonus = Mathf.Clamp(contract.termDays / 30f, 0f, 1f);
            rep.Adjust(ContractCompleted * (1f + lengthBonus));
        }

        public static void NotePayrollMissed(IntercolonyWorldComponent state, EmploymentContract contract)
        {
            EmployerReputation rep = For(state);
            if (rep == null)
            {
                return;
            }

            rep.latePayrollIncidents++;
            rep.Adjust(PayrollMissed);
        }

        /// <summary>Arrears cleared while the worker is still employed — §40's "wages paid on time", late.</summary>
        public static void NoteArrearsCleared(IntercolonyWorldComponent state)
        {
            For(state)?.Adjust(PayrollPaidClearingArrears);
        }

        /// <summary>
        /// A worker gave up and left over unpaid wages. The sharpest single hit available, because
        /// it is the one thing on §40's negative list that the player chose to let happen.
        /// </summary>
        public static void NoteWalkOut(IntercolonyWorldComponent state, EmploymentContract contract)
        {
            EmployerReputation rep = For(state);
            if (rep == null)
            {
                return;
            }

            rep.walkOuts++;
            rep.unpaidCompensation += Mathf.Max(0, contract.arrearsSilver);
            rep.Adjust(WorkerWalkedOut);

            AffectGoodwill(contract.employerFaction, GoodwillWalkOut,
                $"{contract.workerName} left {Faction.OfPlayer.Name} unpaid");
        }

        public static void NoteEmployeeDied(IntercolonyWorldComponent state, EmploymentContract contract)
        {
            EmployerReputation rep = For(state);
            if (rep == null)
            {
                return;
            }

            rep.employeeDeaths++;
            rep.Adjust(EmployeeDied);

            AffectGoodwill(contract.employerFaction, GoodwillDeath,
                $"{contract.workerName} died working for {Faction.OfPlayer.Name}");
        }

        public static void NoteEarlyDismissal(IntercolonyWorldComponent state, EmploymentContract contract)
        {
            EmployerReputation rep = For(state);
            if (rep == null)
            {
                return;
            }

            rep.earlyDismissals++;

            // Mild, and scaled by how much of the term was cut short. Ending a contract early is
            // the player's right; doing it constantly is what says something about them.
            float served = contract.termDays <= 0
                ? 1f
                : Mathf.Clamp01(1f - Mathf.Max(0f, contract.DaysRemaining) / contract.termDays);
            rep.Adjust(EarlyDismissal * (1f - served));
        }

        /// <summary>A debt paid off after the worker had already gone. Partial credit, not absolution.</summary>
        public static void NoteDebtSettled(IntercolonyWorldComponent state, LaborDebt debt)
        {
            EmployerReputation rep = For(state);
            if (rep == null || debt == null)
            {
                return;
            }

            // The unpaid-compensation counter comes down, because it no longer is unpaid — but the
            // walk-out count and the score hit that caused it stay. §40 is a record of conduct,
            // and paying late is not the same as paying on time.
            rep.unpaidCompensation = Mathf.Max(0, rep.unpaidCompensation - debt.originalAmount);
            rep.Adjust(DebtSettledLate);
        }

        private static void AffectGoodwill(Faction faction, int delta, string reason)
        {
            if (faction == null || faction.IsPlayer || faction.Hidden || faction.defeated)
            {
                return;
            }

            try
            {
                // No hostility letter: goodwill loss over a labor dispute should not be the thing
                // that silently tips a neutral faction into war without the player being told why
                // in the terms they understand. The employment letter already explains it.
                faction.TryAffectGoodwillWith(Faction.OfPlayer, delta,
                    canSendMessage: true, canSendHostilityLetter: true);

                IntercolonyLog.Verbose($"Goodwill with {faction.Name} {delta:+0;-0}: {reason}.");
            }
            catch (System.Exception ex)
            {
                IntercolonyLog.Warning($"Could not adjust goodwill with {faction?.Name}: {ex.Message}");
            }
        }

        // --- Effects (§112, §39 step 9) ----------------------------------------------------

        /// <summary>
        /// Wage multiplier. A bad employer pays a risk premium; a sought-after one gets a discount
        /// because people want the job. The span is wide (±25%) on purpose — §112 asks for
        /// *meaningfully* worse conditions, and a few percent would not be noticed.
        /// </summary>
        public static float WageFactor(float score)
        {
            return Mathf.Lerp(1.25f, 0.85f, Normalized(score));
        }

        /// <summary>
        /// How much of the worker pool is willing to consider the colony at all (§39 step 9,
        /// "unavailable"). At the bottom only a third of settlements bother.
        /// </summary>
        public static float AvailabilityFactor(float score)
        {
            return Mathf.Lerp(0.35f, 1.15f, Normalized(score));
        }

        /// <summary>
        /// Whether a good employer gets first pick. Above this, candidate generation draws twice
        /// and keeps the better worker; at the bottom it draws twice and keeps the worse. In the
        /// middle it draws once, so the common case costs nothing extra.
        /// </summary>
        public static int CandidateQualityBias(float score)
        {
            if (score >= 70f)
            {
                return 1;
            }

            return score < 30f ? -1 : 0;
        }

        /// <summary>
        /// Whether this settlement will deal with the colony as an employer at all. A settlement
        /// still owed wages will not send another worker — the specific grievance outranks the
        /// general reputation, which is why <see cref="LaborDebt"/> is per settlement.
        /// </summary>
        public static bool WillSupplyLabor(IntercolonyWorldComponent state, int settlementId, out string reason)
        {
            reason = null;

            if (state == null)
            {
                return true;
            }

            foreach (LaborDebt debt in state.LaborDebts)
            {
                if (debt.settlementId == settlementId && !debt.IsSettled)
                {
                    reason = $"still owed {debt.amountOwed} silver for {debt.workerName}";
                    return false;
                }
            }

            return true;
        }

        private static float Normalized(float score)
        {
            return Mathf.Clamp01(score / EmployerReputation.MaxScore);
        }
    }
}
