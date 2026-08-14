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

        /// <summary>
        /// Failing to protect a worker reflects badly, but less than deliberately putting a
        /// civilian into combat against their agreement (<see cref="CombatMisuse"/>).
        /// </summary>
        private const float EmployeeCaptured = -6f;

        /// <summary>
        /// Using a worker outside their combat clause (§42). Heavier than a missed payroll: a late
        /// wage is a cash-flow problem, and sending a bookkeeper into a firefight is a choice.
        /// Scaled by how many times it has happened, so the first one is a warning and the third is
        /// what the tier says about you.
        /// </summary>
        private const float CombatMisuse = -8f;

        /// <summary>
        /// A worker who left rather than be used as a weapon again. Deliberately as sharp as a
        /// walk-out over unpaid wages: §40's negative list is about what the player chose to let
        /// happen, and both of these are entirely chosen.
        /// </summary>
        private const float BreachWalkOut = -18f;

        /// <summary>Holding a released worker past their safe conduct (§88). See NoteSafePassageDenied.</summary>
        private const float SafePassageDenied = -12f;

        /// <summary>Letting an open-ended worker go without the notice they were owed (§36.4).</summary>
        private const float NoticeSkipped = -9f;

        /// <summary>
        /// A worker liked the colony enough to settle here permanently (§44). The strongest positive
        /// available, and the only one a player cannot manufacture — it takes two quadrums of
        /// spotless treatment to reach.
        /// </summary>
        private const float TransitionSettled = 10f;

        /// <summary>Keeping someone without settling with their people (§44 "pawn defects").</summary>
        private const float Defection = -20f;

        /// <summary>Goodwill lost with the worker's own faction when they are badly treated (§39 step 8).</summary>
        private const int GoodwillWalkOut = -8;
        private const int GoodwillDeath = -5;
        private const int GoodwillCombatMisuse = -4;
        private const int GoodwillSafePassageDenied = -6;
        private const int GoodwillNoticeSkipped = -4;

        /// <summary>Losing a citizen, but properly bought out. A cost, not an insult.</summary>
        private const int GoodwillTransitionSettled = -6;

        /// <summary>
        /// Losing a citizen to what the faction reads as theft (§44). Large enough that a neutral
        /// faction goes hostile — which is the whole point: §116 says conversion must not be cheap
        /// recruitment, and the cheapest route of all is simply not paying.
        /// </summary>
        private const int GoodwillDefection = -80;

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

            // §42's whole purpose is that these deaths are not equal. A security contractor died
            // doing the job they were paid a premium to do; a civilian the player drafted did not.
            // The score reflects the clause and whether the clause was honoured.
            float penalty = EmployeeDied * DeathSeverity(contract);
            rep.Adjust(penalty);

            AffectGoodwill(contract.employerFaction,
                Mathf.RoundToInt(GoodwillDeath * DeathSeverity(contract)),
                $"{contract.workerName} died working for {Faction.OfPlayer.Name}");
        }

        public static void NoteEmployeeCaptured(
            IntercolonyWorldComponent state, EmploymentContract contract)
        {
            if (contract == null)
            {
                return;
            }

            For(state)?.Adjust(EmployeeCaptured);
        }

        /// <summary>
        /// How badly a death reflects on the colony, as a multiplier on the score and goodwill hits.
        ///
        /// Kept as one function so the two consequences cannot drift apart, and expressed as a
        /// multiplier rather than three separate constants so the *ordering* is guaranteed by
        /// construction: a breached civilian death is always worse than an honoured one, which is
        /// always worse than a security contractor's.
        /// </summary>
        private static float DeathSeverity(EmploymentContract contract)
        {
            float severity;
            switch (contract?.combatClause ?? CombatClause.Civilian)
            {
                case CombatClause.Security:
                    severity = 0.4f;
                    break;
                case CombatClause.Armed:
                    severity = 0.7f;
                    break;
                default:
                    severity = 1f;
                    break;
            }

            if (contract != null && contract.clauseBreaches > 0)
            {
                severity *= 1.5f;
            }

            return severity;
        }

        /// <summary>
        /// The player drafted a worker into a fight their contract did not cover (§42).
        ///
        /// The escalation itself lives in <see cref="CombatUseMonitor"/>; this is only the record of
        /// it. Repeats hurt more than the first, because a single mistake in a crisis is not the
        /// same as a habit.
        /// </summary>
        public static void NoteCombatMisuse(IntercolonyWorldComponent state, EmploymentContract contract)
        {
            EmployerReputation rep = For(state);
            if (rep == null || contract == null)
            {
                return;
            }

            rep.combatClauseBreaches++;
            rep.Adjust(CombatMisuse * Mathf.Min(contract.clauseBreaches, 3));

            AffectGoodwill(contract.employerFaction, GoodwillCombatMisuse,
                $"{contract.workerName} was used as a fighter against their contract");
        }

        /// <summary>A worker left rather than be drafted again (§42). Counted as a walk-out, because it is one.</summary>
        public static void NoteBreachWalkOut(IntercolonyWorldComponent state, EmploymentContract contract)
        {
            EmployerReputation rep = For(state);
            if (rep == null || contract == null)
            {
                return;
            }

            rep.walkOuts++;
            rep.Adjust(BreachWalkOut);

            AffectGoodwill(contract.employerFaction, GoodwillWalkOut,
                $"{contract.workerName} refused to keep fighting for {Faction.OfPlayer.Name}");
        }

        /// <summary>
        /// A released worker was still inside the colony when their safe conduct ran out (§88).
        ///
        /// Weighted like a death rather than like a dismissal, and deliberately so. Once the
        /// employment record closes, killing the pawn costs nothing — so if detaining them were free,
        /// walling a released worker in for two days would be the cheapest possible way to be rid of
        /// them, and §88's safe passage would become a loophole rather than a policy. Pricing the
        /// detention itself removes the incentive instead of trying to police the killing.
        /// </summary>
        public static void NoteSafePassageDenied(IntercolonyWorldComponent state, EmploymentContract contract)
        {
            EmployerReputation rep = For(state);
            if (rep == null || contract == null)
            {
                return;
            }

            rep.safePassageDenials++;
            rep.Adjust(SafePassageDenied);

            AffectGoodwill(contract.employerFaction, GoodwillSafePassageDenied,
                $"{contract.workerName} was held in {Faction.OfPlayer.Name} past their release");
        }

        /// <summary>
        /// A worker asked to stay on and was taken up on it (§115, §40's "voluntary renewal").
        ///
        /// §40 lists voluntary renewal as a positive signal and nothing produced one until now.
        /// Weighted like a completed contract, because that is what it is — with the worker's own
        /// judgement of the place attached.
        /// </summary>
        public static void NoteRenewal(IntercolonyWorldComponent state, EmploymentContract contract)
        {
            EmployerReputation rep = For(state);
            if (rep == null || contract == null)
            {
                return;
            }

            rep.renewals++;

            // Compounding slightly with each renewal: a worker on their third term is a stronger
            // statement about the colony than one on their second.
            rep.Adjust(ContractCompleted * (1f + Mathf.Clamp(contract.renewals * 0.25f, 0f, 1f)));
        }

        /// <summary>
        /// An open-ended worker was let go without the notice they were owed (§36.4).
        ///
        /// Between an early dismissal and a walk-out in severity. It is the player's right to do it
        /// — §36.4's rules price the decision rather than block it — but a colony that does it
        /// routinely is one word gets around about.
        /// </summary>
        public static void NoteNoticeSkipped(IntercolonyWorldComponent state, EmploymentContract contract)
        {
            EmployerReputation rep = For(state);
            if (rep == null || contract == null)
            {
                return;
            }

            rep.noticesSkipped++;
            rep.Adjust(NoticeSkipped);

            AffectGoodwill(contract.employerFaction, GoodwillNoticeSkipped,
                $"{contract.workerName} was dismissed without notice");
        }

        /// <summary>A worker settled here for good, bought out cleanly (§44, §116).</summary>
        public static void NoteTransitionSettled(IntercolonyWorldComponent state, EmploymentContract contract)
        {
            EmployerReputation rep = For(state);
            if (rep == null || contract == null)
            {
                return;
            }

            rep.transitions++;
            rep.Adjust(TransitionSettled);

            AffectGoodwill(contract.employerFaction, GoodwillTransitionSettled,
                $"{contract.workerName} left {contract.factionName} to settle in " +
                $"{Faction.OfPlayer.Name}");
        }

        /// <summary>A worker was kept without their people being paid off (§44).</summary>
        public static void NoteDefection(IntercolonyWorldComponent state, EmploymentContract contract)
        {
            EmployerReputation rep = For(state);
            if (rep == null || contract == null)
            {
                return;
            }

            rep.transitions++;
            rep.defections++;
            rep.Adjust(Defection);

            AffectGoodwill(contract.employerFaction, GoodwillDefection,
                $"{contract.workerName} was kept without {contract.factionName} being paid");
        }

        /// <summary>
        /// Compensation the colony could not cover (§43 "debt if unpaid").
        ///
        /// §40 already had an "unpaid compensation" line and until now nothing produced one — the
        /// field was filled with wage arrears for want of anything better. This is what it was for.
        /// The score hit is proportional to the shortfall against the daily wage, so failing to pay
        /// a large settlement hurts more than failing to pay a small one, and a colony that pays in
        /// full takes no hit at all beyond the death itself.
        /// </summary>
        public static void NoteCompensationUnpaid(IntercolonyWorldComponent state,
            EmploymentContract contract, int shortfall)
        {
            EmployerReputation rep = For(state);
            if (rep == null || shortfall <= 0)
            {
                return;
            }

            rep.unpaidCompensation += shortfall;

            int dailyWage = Mathf.Max(1, contract?.dailyWage ?? 1);
            float daysUnpaid = shortfall / (float)dailyWage;
            rep.Adjust(-Mathf.Clamp(daysUnpaid * 0.25f, 1f, 12f));
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

        internal static void AffectGoodwill(Faction faction, int delta, string reason)
        {
            if (faction == null || faction.IsPlayer || faction.Hidden || faction.defeated)
            {
                return;
            }

            // Vanilla's own pre-check, and it earns its place: `TryAffectGoodwillWith` walks
            // `GoodwillSituationManager`, which throws a NullReferenceException outright for any
            // faction whose relation table is empty — `RelationWith` hands back a dummy relation
            // with a null `other`, and `GetSituations(null)` returns null for `GetMaxGoodwill` to
            // dereference. Asking first is cheaper than catching, and it also skips permanent
            // enemies, defeated factions and quest-locked goodwill, none of which we should be
            // nudging over a labor dispute anyway.
            if (!faction.CanChangeGoodwillFor(Faction.OfPlayer, delta))
            {
                IntercolonyLog.Verbose(
                    $"Goodwill with {faction.Name} unchanged ({delta:+0;-0}): it cannot be changed. {reason}.");
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
