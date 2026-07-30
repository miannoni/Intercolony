using RimWorld;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// What the colony owes a settlement for. Both kinds behave identically as money — the
    /// distinction exists because the player is told what the bill is for, and "unpaid wages" and
    /// "compensation for a death you caused" are not the same accusation.
    /// </summary>
    public enum LaborDebtKind
    {
        /// <summary>§39 step 6 — wages earned and never handed over.</summary>
        Wages,

        /// <summary>§43 — death or injury compensation the colony could not cover.</summary>
        Compensation
    }

    /// <summary>
    /// Wages owed to a settlement whose worker has already gone home (DESIGN.md §39 step 6:
    /// "outstanding debt remains").
    ///
    /// Separate from <see cref="EmploymentContract"/> on purpose: the contract is over, the pawn
    /// is gone and its references are cleared, but the obligation is not. Phase 19 (§112) reads
    /// these to work out employer reputation, and §39 steps 8 and 9 make future labor more
    /// expensive or unavailable because of them — so the record has to outlive the employment.
    /// </summary>
    public class LaborDebt : IExposable
    {
        public int id;

        public int settlementId;
        public string settlementName = "";
        public string factionName = "";

        /// <summary>Who the wages were for. Kept for the player's benefit, not for lookup.</summary>
        public string workerName = "";

        public LaborDebtKind kind = LaborDebtKind.Wages;

        public int amountOwed;

        /// <summary>Total ever owed on this record, so paying it down partially still shows the history.</summary>
        public int originalAmount;

        public int incurredTick;

        /// <summary>How many pay periods were missed before the worker gave up.</summary>
        public int missedPayments;

        public bool IsSettled => amountOwed <= 0;

        public float DaysOutstanding => (GenTicks.TicksGame - incurredTick) / (float)GenDate.TicksPerDay;

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", 0);
            Scribe_Values.Look(ref settlementId, "settlementId", -1);
            Scribe_Values.Look(ref settlementName, "settlementName", "");
            Scribe_Values.Look(ref factionName, "factionName", "");
            Scribe_Values.Look(ref workerName, "workerName", "");

            // Every debt written before Phase 20 was wages, which is the default — so an old
            // save reads correctly without a migration step.
            Scribe_Values.Look(ref kind, "kind", LaborDebtKind.Wages);
            Scribe_Values.Look(ref amountOwed, "amountOwed", 0);
            Scribe_Values.Look(ref originalAmount, "originalAmount", 0);
            Scribe_Values.Look(ref incurredTick, "incurredTick", 0);
            Scribe_Values.Look(ref missedPayments, "missedPayments", 0);
        }

        public bool IsValidAfterLoad => amountOwed >= 0 && originalAmount > 0;

        /// <summary>What this is a bill for, in the player's words.</summary>
        public string KindLabel()
        {
            return kind == LaborDebtKind.Compensation ? "compensation" : "unpaid wages";
        }

        public override string ToString()
        {
            return $"Debt #{id} {amountOwed}/{originalAmount} silver to {settlementName} " +
                   $"for {workerName} ({KindLabel()}), {DaysOutstanding:F0}d outstanding";
        }
    }
}
