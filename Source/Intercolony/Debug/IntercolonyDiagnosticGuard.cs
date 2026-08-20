using System;
using System.Collections.Generic;

namespace Intercolony
{
    /// <summary>
    /// Restores the world state a diagnostic is allowed to disturb, to exactly what it found.
    ///
    /// **Self-tests drive the real order and contract transitions on purpose**, which is the only
    /// way they prove anything — but those transitions now record commercial events, and from
    /// Stage 2B they will move market pressure too. Without this, failing a synthetic order writes
    /// a permanent row into the player's trading history for a settlement that does not exist, and
    /// several suites do that dozens of times per run.
    ///
    /// It snapshots list **contents**, never lengths. Restoring by count is what made the timeline
    /// self-test destructive in review: pruning removes from the front, so trimming the tail back
    /// to the original length leaves synthetic records standing where the real ones were.
    ///
    /// Deliberately not a "suppress recording" flag on the services. A global bit like that would,
    /// if it ever leaked, silently stop recording real events — and silent history loss is far
    /// worse than a debug list needing cleanup.
    ///
    /// Entity IDs consumed inside the guard are **not** given back. They are opaque and monotonic,
    /// so gaps are harmless, and rewinding the counter could hand out an ID a record already holds.
    /// </summary>
    public sealed class IntercolonyDiagnosticGuard : IDisposable
    {
        private readonly IntercolonyWorldComponent state;
        private readonly List<CommercialEventRecord> savedTimeline;
        private readonly List<SettlementMarketState> savedMarketStates;
        private readonly EmployerReputation savedEmployerStanding;
        private readonly int savedTimelineStartTick;

        public IntercolonyDiagnosticGuard(IntercolonyWorldComponent state)
        {
            this.state = state;
            if (state == null)
            {
                return;
            }

            savedTimeline = new List<CommercialEventRecord>(state.CommercialTimeline);
            savedMarketStates = new List<SettlementMarketState>(state.MarketStates);
            savedEmployerStanding = state.EmployerStanding?.Snapshot();
            savedTimelineStartTick = state.CommercialTimelineStartTick;
        }

        public void Dispose()
        {
            if (state == null)
            {
                return;
            }

            state.CommercialTimeline.Clear();
            state.CommercialTimeline.AddRange(savedTimeline);

            state.MarketStates.Clear();
            state.MarketStates.AddRange(savedMarketStates);
            state.RefreshMarketStateIndex();

            // Employer standing is global to the colony, not per settlement. The payroll suite
            // drives real missed payrolls and a walkout on purpose, which costs -6 and -18, and
            // nothing gave it back — so running that self-test permanently damaged the player's
            // reputation as an employer, and left every later suite reading a value that depended
            // on what had run before it. That is what made the long-term suite's renewal
            // assertion fail in the first full-suite run: renewal needs a standing of 40.
            state.EmployerStanding?.RestoreFrom(savedEmployerStanding);

            // Recording stamps this on the first ever event. A self-test must not be the thing
            // that decides when this colony's trading history began.
            state.CommercialTimelineStartTick = savedTimelineStartTick;
        }
    }
}
