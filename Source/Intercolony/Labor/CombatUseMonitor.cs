using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Notices when an employee is used as a weapon (DESIGN.md §42, §113).
    ///
    /// §113 asks for "combat-use tracking where technically feasible". It is feasible without a
    /// single Harmony patch, because vanilla already records the thing we need:
    /// <c>Pawn_MindState.lastAttackTargetTick</c> is stamped by <c>Verb.TryCastNextBurstShot</c> on
    /// every verb a pawn casts, melee or ranged, and it is saved. Sampling it against
    /// <c>Pawn.Drafted</c> answers exactly the question §42 poses — *did the player point this
    /// worker at something* — with no hook into combat, damage or the storyteller.
    ///
    /// **Drafted is the whole test, and that is the design.** §42 says self-defense is acceptable
    /// and aggressive use is not, and drafting is precisely the line between them: an undrafted
    /// worker who shoots back is defending themselves, while a drafted one is being aimed. It also
    /// means the rule is legible without a tutorial — do not draft your civilians.
    /// </summary>
    public static class CombatUseMonitor
    {
        /// <summary>
        /// How often the sampler runs. One second of real time at normal speed, which is short
        /// enough that <c>Drafted</c> at sample time is <c>Drafted</c> at cast time, and coarse
        /// enough to cost nothing (§84) — the loop is over active contracts, usually one or two.
        /// </summary>
        public const int SampleIntervalTicks = 60;

        /// <summary>
        /// How recent an attack must be to count. Without this, a worker who fought off an attacker
        /// undrafted and was then drafted a few seconds later would be charged with the earlier
        /// shots — the stale tick would still be sitting in <c>lastAttackTargetTick</c>.
        /// </summary>
        private const int AttackRecencyTicks = SampleIntervalTicks * 2;

        /// <summary>
        /// One in-game hour. A firefight fires dozens of verbs; the clause was breached once. This
        /// is what turns a burst of shots into a single incident the player can be told about.
        /// </summary>
        private const int IncidentCooldownTicks = 2500;

        /// <summary>Breaches before the worker downs tools. Mirrors §39's payroll escalation.</summary>
        public const int BreachesBeforeRefusingWork = 2;

        /// <summary>Breaches before the worker gives up and goes home.</summary>
        public const int BreachesBeforeQuitting = 3;

        /// <summary>
        /// Called on a short beat from the world component. Walks active employments and records
        /// any fight that has started since the last look.
        /// </summary>
        public static void Sample(List<EmploymentContract> contracts, IntercolonyWorldComponent state)
        {
            if (contracts == null)
            {
                return;
            }

            int now = GenTicks.TicksGame;

            // Downwards: an escalation can end a contract, which removes nothing from the list but
            // may in future, and the cost of being safe here is zero.
            for (int i = contracts.Count - 1; i >= 0; i--)
            {
                EmploymentContract contract = contracts[i];
                if (contract.status != EmploymentStatus.Active)
                {
                    continue;
                }

                Pawn worker = contract.pawn;
                if (worker == null || !worker.Spawned || worker.Dead || worker.mindState == null)
                {
                    continue;
                }

                int attackTick = worker.mindState.lastAttackTargetTick;

                // Nothing new, or a stale tick left over from before this sample window.
                if (attackTick <= contract.countedAttackTick || now - attackTick > AttackRecencyTicks)
                {
                    continue;
                }

                contract.countedAttackTick = attackTick;

                if (!worker.Drafted)
                {
                    // Self-defense, hunting, a mental break — none of it is the player aiming them.
                    continue;
                }

                NoteDraftedAttack(contract, state, worker);
            }
        }

        /// <summary>
        /// Records that a drafted employee attacked something, and escalates if that was outside
        /// their clause.
        ///
        /// Public so the self-test drives the **real** escalation rather than a copy of it. Phase 19
        /// learned that lesson the expensive way: a test that builds its own object and asserts its
        /// own arithmetic proves only that the test is self-consistent.
        /// </summary>
        /// <returns>True if this opened a new incident; false if it fell inside the cooldown.</returns>
        public static bool NoteDraftedAttack(EmploymentContract contract,
            IntercolonyWorldComponent state, Pawn worker)
        {
            int now = GenTicks.TicksGame;

            // Still inside the same skirmish we already counted.
            if (now - contract.lastIncidentTick < IncidentCooldownTicks)
            {
                return false;
            }

            contract.lastIncidentTick = now;
            contract.combatIncidents++;

            if (contract.CombatUsePermittedNow)
            {
                // Within the terms. Recorded anyway — §113 wants combat use tracked, and for a
                // security contractor this line is a record of service, not of misconduct.
                IntercolonyLog.Verbose(
                    $"{contract.workerName} ({contract.combatClause.Label()}) fought within terms " +
                    $"— incident {contract.combatIncidents}.");
                return true;
            }

            Breach(contract, state, worker);
            return true;
        }

        /// <summary>
        /// One breach of the combat clause, and the escalation that follows it.
        ///
        /// Deliberately the same three-step shape as §39's arrears escalation — warn, down tools,
        /// leave — so it reads as a mechanic the player already understands rather than a new one.
        /// The difference is that this one cannot be bought off: there is nothing to pay, and the
        /// only way back is to stop doing it, which is the point.
        /// </summary>
        private static void Breach(EmploymentContract contract, IntercolonyWorldComponent state, Pawn worker)
        {
            contract.clauseBreaches++;
            EmployerReputationService.NoteCombatMisuse(state, contract);

            string terms = contract.combatClause == CombatClause.Armed
                ? $"{contract.workerName} agreed to defend this colony, not to fight away from it."
                : $"{contract.workerName} was hired as a civilian and did not agree to fight at all.";

            string exposure = $"They have now been drafted into combat {contract.clauseBreaches} time(s) " +
                              "against the terms of their contract.";

            if (contract.clauseBreaches >= BreachesBeforeQuitting)
            {
                // No debt: the wages are not in dispute, the work is. Whatever has been paid stays
                // paid, and the walk-out itself is the cost.
                EmployerReputationService.NoteBreachWalkOut(state, contract);

                EmploymentService.End(contract, EmploymentStatus.Quit,
                    $"{contract.workerName} refused to keep being used as a fighter and went home");
                return;
            }

            if (contract.clauseBreaches >= BreachesBeforeRefusingWork)
            {
                contract.HoldWork(WorkRefusalReason.CombatMisuse);

                IntercolonyLetters.Send(
                    IntercolonyLetterImportance.Always,
                    "Employee refuses to work",
                    terms + "\n\n" + exposure + "\n\n" +
                    "They have downed tools and will not pick them up again this term. There is " +
                    "nothing to pay — the only thing that would have helped was not drafting them.\n\n" +
                    $"Draft them once more and they will leave. If they die now, compensation is " +
                    $"{CompensationService.DeathCompensation(contract)} silver.",
                    LetterDefOf.ThreatSmall, worker);
                return;
            }

            IntercolonyLetters.Send(
                IntercolonyLetterImportance.Always,
                "Combat clause breached",
                terms + "\n\n" + exposure + "\n\n" +
                "Word of this reaches other settlements, and their opinion of you as an employer " +
                "has suffered.\n\n" +
                $"Compensation if they die has risen to " +
                $"{CompensationService.DeathCompensation(contract)} silver, and it rises again with " +
                "every further breach. Draft them again and they will stop working; a third time " +
                "and they will leave.",
                LetterDefOf.NegativeEvent, worker);
        }
    }
}
