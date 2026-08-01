using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// What a cash movement was for (DESIGN.md §75, whose list this is).
    ///
    /// Deliberately about the *business reason* rather than the code path. Two different services
    /// both hand silver to a settlement, and lumping them as "outgoing" would make the dashboard
    /// unable to answer §45's questions — "should I hire or train" needs payroll separated from
    /// purchases, not a net figure.
    /// </summary>
    public enum LedgerKind
    {
        /// <summary>Silver in from a buyer collecting or a delivery completing (§25).</summary>
        SalePayment,

        /// <summary>Silver out to a supplier (§21). Paid at order time, not on arrival.</summary>
        PurchasePayment,

        /// <summary>Wages, prepaid or periodic (§37, §38).</summary>
        WagePayment,

        /// <summary>Death or injury settlement (§43).</summary>
        Compensation,

        /// <summary>A release fee to keep an employee permanently (§44).</summary>
        ReleaseFee,

        /// <summary>Silver back from a supplier who defaulted (§21).</summary>
        Refund,

        /// <summary>A debt settled after the fact — wages or compensation paid late (§39 step 6).</summary>
        DebtSettlement
    }

    /// <summary>
    /// One movement of silver (DESIGN.md §75).
    ///
    /// **The mod has always known these numbers and never remembered when they happened.** Every
    /// figure the dashboard needs already existed as a cumulative total on an entity —
    /// <c>SalesOrder.paidSilver</c>, <c>EmploymentContract.compensationPaid</c> — which answers "how
    /// much in total" and cannot answer "how much last quadrum". §117's whole screen is the second
    /// question, so the ledger exists to make it askable.
    ///
    /// Kept small on purpose: seven fields, no references. A reference to a settlement or a contract
    /// would be a second source of truth that can dangle on load, and would tie the length of the
    /// log to the lifetime of everything it mentions. Names are frozen strings for the same reason
    /// a completed contract freezes its worker's name.
    /// </summary>
    public class LedgerEntry : IExposable
    {
        public int tick;

        public LedgerKind kind;

        /// <summary>Positive is silver in, negative is silver out. Signed at the point of record.</summary>
        public int amount;

        /// <summary>Who it was with, frozen. Empty when there is no counterparty.</summary>
        public string counterparty = "";

        /// <summary>What it was for, in the player's terms — "700 rice", "Tomas Vega, 12 days".</summary>
        public string note = "";

        public LedgerEntry()
        {
        }

        public LedgerEntry(LedgerKind kind, int amount, string counterparty, string note)
        {
            tick = GenTicks.TicksGame;
            this.kind = kind;
            this.amount = amount;
            this.counterparty = counterparty ?? "";
            this.note = note ?? "";
        }

        public float DaysAgo => (GenTicks.TicksGame - tick) / (float)GenDate.TicksPerDay;

        public bool IsIncome => amount > 0;

        /// <summary>The label §117's report uses for this row's group.</summary>
        public static string Label(LedgerKind kind)
        {
            switch (kind)
            {
                case LedgerKind.SalePayment:
                    return "Sales revenue";
                case LedgerKind.PurchasePayment:
                    return "Purchases";
                case LedgerKind.WagePayment:
                    return "Payroll";
                case LedgerKind.Compensation:
                    return "Compensation";
                case LedgerKind.ReleaseFee:
                    return "Release fees";
                case LedgerKind.Refund:
                    return "Refunds";
                default:
                    return "Debts settled";
            }
        }

        /// <summary>
        /// Report order, so the dashboard reads the same way every time regardless of what
        /// happened to occur this quadrum. Follows §117's mock-up: revenue first, then the costs in
        /// the order it lists them.
        /// </summary>
        public static readonly LedgerKind[] ReportOrder =
        {
            LedgerKind.SalePayment,
            LedgerKind.Refund,
            LedgerKind.PurchasePayment,
            LedgerKind.WagePayment,
            LedgerKind.Compensation,
            LedgerKind.ReleaseFee,
            LedgerKind.DebtSettlement
        };

        public void ExposeData()
        {
            Scribe_Values.Look(ref tick, "tick", 0);
            Scribe_Values.Look(ref kind, "kind", LedgerKind.SalePayment);
            Scribe_Values.Look(ref amount, "amount", 0);
            Scribe_Values.Look(ref counterparty, "counterparty", "");
            Scribe_Values.Look(ref note, "note", "");
        }

        public override string ToString()
        {
            string when = $"{Mathf.Max(0f, DaysAgo):0.#}d ago";
            string who = counterparty.NullOrEmpty() ? "" : $" {counterparty}";
            return $"[{when}] {Label(kind)}{who} {amount:+#;-#;0}" +
                   (note.NullOrEmpty() ? "" : $" — {note}");
        }
    }
}
