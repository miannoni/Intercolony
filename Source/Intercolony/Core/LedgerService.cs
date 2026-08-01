using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Records cash movements and reports on them (DESIGN.md §75, §117, §45).
    ///
    /// §117's brief is a warning as much as a goal: *"Help the player understand the business
    /// without turning the mod into accounting software."* So this deliberately does not build a
    /// general accounting model. It records seven kinds of movement, buckets them by time, and
    /// subtracts. Anything a player would need a second screen to interpret does not belong here.
    ///
    /// **History starts when the ledger does.** An existing save has no past to reconstruct — the
    /// cumulative totals on orders and contracts cannot say *when* anything happened — and inventing
    /// plausible dates would be fiction presented as a record. The dashboard says so rather than
    /// showing a confident zero.
    /// </summary>
    public static class LedgerService
    {
        /// <summary>
        /// How long individual entries are kept. One in-game year, which is the longest window the
        /// dashboard offers, so nothing is dropped that anything can still ask about.
        /// </summary>
        public const int RetentionDays = GenDate.DaysPerYear;

        /// <summary>
        /// Hard ceiling regardless of age, so a colony trading furiously cannot bloat the save
        /// between prunes. Generous enough that hitting it means genuinely heavy trade.
        /// </summary>
        public const int MaxEntries = 2000;

        // --- Recording ---------------------------------------------------------------------

        /// <summary>
        /// Records a movement. Amount is signed by the caller, because only the caller knows
        /// whether the silver went out or came in.
        /// </summary>
        public static void Record(IntercolonyWorldComponent state, LedgerKind kind, int amount,
            string counterparty, string note)
        {
            if (state == null || amount == 0)
            {
                return;
            }

            state.Ledger.Add(new LedgerEntry(kind, amount, counterparty, note));

            if (state.LedgerStartTick < 0)
            {
                state.LedgerStartTick = GenTicks.TicksGame;
            }
        }

        /// <summary>Convenience for the common case where the mod's own singleton is the state.</summary>
        public static void Record(LedgerKind kind, int amount, string counterparty, string note)
        {
            Record(IntercolonyWorldComponent.Current, kind, amount, counterparty, note);
        }

        /// <summary>
        /// Drops entries past the retention window. Called on the daily refresh — pruning per tick
        /// would be absurd for a list that grows a handful of rows a day (§84).
        /// </summary>
        public static int Prune(IntercolonyWorldComponent state)
        {
            List<LedgerEntry> ledger = state?.Ledger;
            if (ledger == null || ledger.Count == 0)
            {
                return 0;
            }

            int cutoff = GenTicks.TicksGame - RetentionDays * GenDate.TicksPerDay;
            int removed = ledger.RemoveAll(e => e == null || e.tick < cutoff);

            // The age rule is the real one; this is the backstop against a colony that manages to
            // out-trade it inside a single year.
            if (ledger.Count > MaxEntries)
            {
                int excess = ledger.Count - MaxEntries;
                ledger.RemoveRange(0, excess);
                removed += excess;
            }

            return removed;
        }

        // --- Reporting ---------------------------------------------------------------------

        /// <summary>
        /// One period's figures, grouped the way §117's screen shows them.
        /// </summary>
        public class Report
        {
            public int days;
            public readonly Dictionary<LedgerKind, int> byKind = new Dictionary<LedgerKind, int>();
            public int entryCount;

            /// <summary>Whether the ledger covers the whole window, or started partway through it.</summary>
            public bool partial;

            public float daysCovered;

            public int Of(LedgerKind kind)
            {
                return byKind.TryGetValue(kind, out int value) ? value : 0;
            }

            public int Income
            {
                get
                {
                    int total = 0;
                    foreach (KeyValuePair<LedgerKind, int> pair in byKind)
                    {
                        if (pair.Value > 0)
                        {
                            total += pair.Value;
                        }
                    }

                    return total;
                }
            }

            public int Outgoings
            {
                get
                {
                    int total = 0;
                    foreach (KeyValuePair<LedgerKind, int> pair in byKind)
                    {
                        if (pair.Value < 0)
                        {
                            total += pair.Value;
                        }
                    }

                    return total;
                }
            }

            /// <summary>§117's bottom line.</summary>
            public int Net => Income + Outgoings;
        }

        /// <summary>
        /// Adds up the last <paramref name="days"/> days.
        ///
        /// Reports <see cref="Report.partial"/> when the ledger is younger than the window asked
        /// for. That distinction matters more than it looks: a colony three days old showing
        /// "last quadrum: +180" is not reporting a quiet quadrum, it is reporting three days, and a
        /// player comparing that against a target would be comparing against nothing.
        /// </summary>
        public static Report Summarise(IntercolonyWorldComponent state, int days)
        {
            Report report = new Report { days = days };

            List<LedgerEntry> ledger = state?.Ledger;
            if (ledger == null)
            {
                return report;
            }

            int cutoff = GenTicks.TicksGame - days * GenDate.TicksPerDay;

            foreach (LedgerEntry entry in ledger)
            {
                if (entry == null || entry.tick < cutoff)
                {
                    continue;
                }

                report.byKind.TryGetValue(entry.kind, out int running);
                report.byKind[entry.kind] = running + entry.amount;
                report.entryCount++;
            }

            int start = state.LedgerStartTick;
            if (start < 0)
            {
                report.partial = true;
                report.daysCovered = 0f;
            }
            else
            {
                float covered = (GenTicks.TicksGame - Mathf.Max(start, cutoff)) / (float)GenDate.TicksPerDay;
                report.daysCovered = Mathf.Max(0f, covered);
                report.partial = start > cutoff;
            }

            return report;
        }

        /// <summary>The most recent entries, newest first, for the detail list.</summary>
        public static List<LedgerEntry> Recent(IntercolonyWorldComponent state, int count)
        {
            List<LedgerEntry> recent = new List<LedgerEntry>();
            List<LedgerEntry> ledger = state?.Ledger;
            if (ledger == null)
            {
                return recent;
            }

            for (int i = ledger.Count - 1; i >= 0 && recent.Count < count; i--)
            {
                if (ledger[i] != null)
                {
                    recent.Add(ledger[i]);
                }
            }

            return recent;
        }
    }
}
