using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Death and injury compensation (DESIGN.md §43, §113).
    ///
    /// §43's purpose is one sentence — *"Employee death should create consequences"* — and its
    /// warning is the other: *"Balance carefully so ordinary RimWorld danger does not make
    /// employees unusable."* Those pull against each other, and the combat clause is what resolves
    /// them. A worker who signed up to fight is cheap to bury; a bookkeeper you drafted is not.
    /// So the deterrent lands on misuse rather than on employment itself.
    ///
    /// §43 lists five possible effects. Four are implemented here or next door: compensation owed,
    /// debt if unpaid (<see cref="LaborDebt"/>), employer reputation loss and faction goodwill loss
    /// (<see cref="EmployerReputationService"/>). The fifth — a reduced applicant pool — already
    /// falls out of Phase 19, because reputation is what sizes the pool.
    /// </summary>
    public static class CompensationService
    {
        /// <summary>Ceiling on the breach surcharge, so a long abusive contract cannot run away.</summary>
        public const float MaxBreachMultiplier = 4f;

        /// <summary>
        /// What breaching the combat clause does to the bill — and the single number that makes §42
        /// work at all.
        ///
        /// **It compounds rather than doubling once, and that is a correction, not a flourish.** A
        /// flat 2x is enough on a short contract but inverts on a long one: at a 90-day term, wages
        /// for a security contractor (2.5x rate) overtake a doubled civilian death payout, so the
        /// cheapest way to field a fighter becomes drafting a bookkeeper. Compounding keeps the
        /// civilian more expensive at every term length, which is what §113's acceptance criterion
        /// actually asks for.
        /// </summary>
        public static float BreachMultiplier(EmploymentContract contract)
        {
            int breaches = Mathf.Max(0, contract?.clauseBreaches ?? 0);
            return breaches == 0 ? 1f : Mathf.Min(1f + breaches, MaxBreachMultiplier);
        }

        /// <summary>Silver owed if this worker dies now (§43).</summary>
        public static int DeathCompensation(EmploymentContract contract)
        {
            if (contract == null)
            {
                return 0;
            }

            float owed = contract.dailyWage * contract.combatClause.DeathCompensationDays();
            return Mathf.Max(0, Mathf.RoundToInt(owed * BreachMultiplier(contract)));
        }

        /// <summary>
        /// Silver owed for permanent injuries taken during the employment. Capped at the death
        /// figure: however many parts a worker loses, being maimed cannot cost more than being
        /// killed, or the player would be better off finishing them.
        /// </summary>
        public static int InjuryCompensation(EmploymentContract contract, int newPermanentInjuries)
        {
            if (contract == null || newPermanentInjuries <= 0)
            {
                return 0;
            }

            float perInjury = contract.dailyWage * contract.combatClause.InjuryCompensationDays();
            float owed = perInjury * newPermanentInjuries * BreachMultiplier(contract);

            return Mathf.Clamp(Mathf.RoundToInt(owed), 0, DeathCompensation(contract));
        }

        /// <summary>
        /// Counts the permanent injuries and missing parts a pawn is carrying.
        ///
        /// Both are counted because both are what §43 means by injury: a lost hand and a permanent
        /// spine scar are equally not going to heal. Snapshotted on arrival so the colony pays only
        /// for the harm it did — a worker who showed up already missing a leg does not get a payout
        /// for it at the end of the term.
        /// </summary>
        public static int CountPermanentInjuries(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null)
            {
                return 0;
            }

            int count = 0;
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];
                if (hediff is Hediff_MissingPart || hediff.IsPermanent())
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Settles a death. Called the moment the death is noticed, before the record is closed,
        /// because the compensation is computed from fields the closing clears.
        /// </summary>
        public static void ClaimOnDeath(IntercolonyWorldComponent state, EmploymentContract contract)
        {
            int owed = DeathCompensation(contract);
            if (owed <= 0)
            {
                return;
            }

            Settle(state, contract, owed, "death", ExplainDeath(contract, owed));
        }

        /// <summary>
        /// Settles permanent injuries when employment ends for any reason other than death — the
        /// death payout already covers everything that happened to them.
        /// </summary>
        public static void ClaimOnEnd(IntercolonyWorldComponent state, EmploymentContract contract)
        {
            if (contract?.pawn == null || contract.pawn.Dead)
            {
                return;
            }

            // A worker who never arrived has no snapshot to compare against, so every injury they
            // were generated with would read as one the colony inflicted. endTick is set exactly
            // once, on arrival, which makes it the honest test for "did they ever work here".
            if (contract.endTick < 0)
            {
                return;
            }

            int now = CountPermanentInjuries(contract.pawn);
            int gained = now - contract.permanentInjuriesOnArrival;
            int owed = InjuryCompensation(contract, gained);
            if (owed <= 0)
            {
                return;
            }

            Settle(state, contract, owed, "injury", ExplainInjury(contract, gained, owed));
        }

        /// <summary>
        /// Pays what the colony can and books the rest as a debt (§43 "debt if unpaid").
        ///
        /// Deliberately the same shape as a missed payroll: partial payment, a recorded shortfall,
        /// and a reputation consequence sized to the shortfall rather than to the bill. §38's rule
        /// applies here too — a colony that cannot pay does not get to have the obligation vanish.
        /// </summary>
        private static void Settle(IntercolonyWorldComponent state, EmploymentContract contract,
            int owed, string kindWord, string explanation)
        {
            Map map = contract.destinationMap ?? contract.pawn?.MapHeld ?? Find.AnyPlayerHomeMap;

            int available = PurchaseOrderService.CountColonySilver(map);
            int paid = Mathf.Min(owed, available);

            if (paid > 0 && PurchaseOrderService.TryTakeSilver(map, paid))
            {
                contract.compensationPaid += paid;
            }
            else
            {
                paid = 0;
            }

            int shortfall = owed - paid;

            if (shortfall > 0)
            {
                RecordDebt(state, contract, shortfall);
                EmployerReputationService.NoteCompensationUnpaid(state, contract, shortfall);
            }

            string money = shortfall > 0
                ? $"{owed} silver is owed. {paid} was paid from storage; {shortfall} could not be " +
                  "covered and is now a debt to " + contract.settlementName + "."
                : $"{owed} silver has been paid to {contract.settlementName}.";

            Find.LetterStack.ReceiveLetter(
                shortfall > 0 ? "Compensation unpaid" : "Compensation paid",
                explanation + "\n\n" + money,
                shortfall > 0 ? LetterDefOf.NegativeEvent : LetterDefOf.NeutralEvent);

            IntercolonyLog.Message(
                $"Compensation ({kindWord}) for {contract.workerName}: {owed} owed, {paid} paid, " +
                $"{shortfall} outstanding.");
        }

        /// <summary>§43's letter, near enough verbatim — it prints the figure the way §43 shows it.</summary>
        private static string ExplainDeath(EmploymentContract contract, int owed)
        {
            string clause =
                $"They were hired as a {contract.combatClause.Label()} " +
                $"({contract.combatClause.DeathCompensationDays()} days' wage on death).";

            string breach = contract.clauseBreaches > 0
                ? $"\n\nYou drafted them into combat {contract.clauseBreaches} time(s) against the terms " +
                  $"of that clause. The settlement is {BreachMultiplier(contract):0.#}x what it would " +
                  $"otherwise have been: {owed} silver."
                : "";

            return $"{contract.workerName} died while employed by your colony.\n\n" + clause + breach;
        }

        private static string ExplainInjury(EmploymentContract contract, int injuries, int owed)
        {
            string breach = contract.clauseBreaches > 0
                ? $" The figure is {BreachMultiplier(contract):0.#}x what it would otherwise have been, " +
                  "because they were drafted into combat against the terms of their clause."
                : "";

            return $"{contract.workerName} is going home with {injuries} permanent " +
                   (injuries == 1 ? "injury" : "injuries") +
                   $" they did not arrive with.\n\n{contract.settlementName} expects compensation " +
                   $"for a {contract.combatClause.Label()} who was hurt in your service." + breach;
        }

        private static void RecordDebt(IntercolonyWorldComponent state, EmploymentContract contract,
            int shortfall)
        {
            if (state?.LaborDebts == null || shortfall <= 0)
            {
                return;
            }

            state.LaborDebts.Add(new LaborDebt
            {
                id = state.NextId(),
                settlementId = contract.settlementId,
                settlementName = contract.settlementName,
                factionName = contract.factionName,
                workerName = contract.workerName,
                kind = LaborDebtKind.Compensation,
                amountOwed = shortfall,
                originalAmount = shortfall,
                incurredTick = GenTicks.TicksGame
            });
        }
    }
}
