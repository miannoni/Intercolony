using System;
using System.Collections.Generic;

namespace Intercolony
{
    /// <summary>
    /// Restores the commercial timeline to exactly what it held before a self-test ran.
    ///
    /// **Self-tests drive the real order and contract transitions on purpose**, which is the only
    /// way they prove anything — but since Stage 0.3b those transitions record commercial events,
    /// so a test that fails a synthetic order now writes a permanent row into the player's trading
    /// history for a settlement that does not exist. Four existing suites do this dozens of times
    /// per run.
    ///
    /// The guard snapshots the list's **contents**, not its length. Restoring by count is what made
    /// the timeline self-test destructive in review: pruning removes from the front, so trimming the
    /// tail back to the original count leaves synthetic records where the real ones were.
    ///
    /// Deliberately not a suppression flag on the recording service. A global "stop recording" bit
    /// that leaked would silently stop recording real events, and silent history loss is far worse
    /// than a debug list needing cleanup.
    /// </summary>
    public sealed class IntercolonyTimelineGuard : IDisposable
    {
        private readonly IntercolonyWorldComponent state;
        private readonly List<CommercialEventRecord> saved;
        private readonly int savedStartTick;

        public IntercolonyTimelineGuard(IntercolonyWorldComponent state)
        {
            this.state = state;
            if (state == null)
            {
                return;
            }

            saved = new List<CommercialEventRecord>(state.CommercialTimeline);
            savedStartTick = state.CommercialTimelineStartTick;
        }

        /// <summary>Records written since construction, for a test that wants to assert on them.</summary>
        public int RecordedSinceStart =>
            state == null ? 0 : state.CommercialTimeline.Count - saved.Count;

        public void Dispose()
        {
            if (state == null)
            {
                return;
            }

            state.CommercialTimeline.Clear();
            state.CommercialTimeline.AddRange(saved);

            // Recording sets the start tick on the first ever event. A self-test must not be the
            // thing that decides when this colony's trading history began.
            state.CommercialTimelineStartTick = savedStartTick;
        }
    }
}
