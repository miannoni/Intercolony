using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Records and maintains detailed commercial timeline events
    /// (docs/INTERCOLONY_1_0_IMPLEMENTATION_PLAN.md Stage 0.3, read in Stage 7).
    ///
    /// Detailed events give settlements a readable history of deals, contracts, successes
    /// and failures. Persisted and bounded by <see cref="MaxTimelineRecords"/> on the
    /// <see cref="IntercolonyWorldComponent"/>.
    /// </summary>
    public static class CommercialTimelineService
    {
        /// <summary>
        /// Global capacity cap for retained timeline records (the 1.0 program Stage 0.3).
        /// One thousand is the initial safe cap: it preserves a substantial narrative while
        /// keeping worst-case serialized save growth finite and measurable before profiling can
        /// justify a retune. When exceeded, the oldest records are pruned first.
        /// </summary>
        public const int MaxTimelineRecords = 1000;

        /// <summary>
        /// Value of <see cref="IntercolonyWorldComponent.CommercialTimelineStartTick"/> meaning "nothing has
        /// been recorded yet", compared exactly rather than by sign.
        /// </summary>
        public const int NoHistory = -1;

        /// <summary>
        /// Returns the number of detailed records currently retained for save profiling. This is
        /// a read-only measurement; it does not prune the timeline or inspect authoritative state.
        /// </summary>
        public static int RecordCount(IntercolonyWorldComponent state)
        {
            return state?.CommercialTimeline?.Count ?? 0;
        }

        /// <summary>
        /// Records a commercial event in the world component's timeline.
        /// </summary>
        public static CommercialEventRecord Record(
            IntercolonyWorldComponent state,
            CommercialEventType type,
            int settlementId,
            string settlementName = "",
            int relatedEntityId = 0,
            ThingDef thingDef = null,
            int quantity = 0,
            int silverAmount = 0,
            string compactDetail = null)
        {
            if (state == null)
            {
                return null;
            }

            CommercialEventRecord record = new CommercialEventRecord(
                state.NextId(),
                GenTicks.TicksGame,
                settlementId,
                type,
                settlementName,
                relatedEntityId,
                thingDef,
                quantity,
                silverAmount,
                compactDetail);

            if (state.CommercialTimelineStartTick == NoHistory)
            {
                state.CommercialTimelineStartTick = GenTicks.TicksGame;
            }

            state.CommercialTimeline.Add(record);
            // Enforce the save-size bound at the write boundary as well as during refresh. A save
            // taken between refreshes must not be able to capture an oversized display history.
            Prune(state);
            return record;
        }

        /// <summary>
        /// Convenience overload recording against the current world state singleton.
        /// </summary>
        public static CommercialEventRecord Record(
            CommercialEventType type,
            int settlementId,
            string settlementName = "",
            int relatedEntityId = 0,
            ThingDef thingDef = null,
            int quantity = 0,
            int silverAmount = 0,
            string compactDetail = null)
        {
            return Record(
                IntercolonyWorldComponent.Current,
                type,
                settlementId,
                settlementName,
                relatedEntityId,
                thingDef,
                quantity,
                silverAmount,
                compactDetail);
        }

        /// <summary>
        /// Drops timeline entries beyond <see cref="MaxTimelineRecords"/>, keeping the most recent records.
        /// Oldest timeline records are pruned first. Active obligations and cumulative aggregates are untouched.
        /// </summary>
        public static int Prune(IntercolonyWorldComponent state)
        {
            List<CommercialEventRecord> timeline = state?.CommercialTimeline;
            if (timeline == null || timeline.Count == 0)
            {
                return 0;
            }

            int removed = timeline.RemoveAll(e => e == null);

            if (timeline.Count > MaxTimelineRecords)
            {
                // Deliberately sort newest-first by tick then id (precedent: OrderHistoryService)
                // rather than trusting insertion order, retain the newest MaxTimelineRecords,
                // and restore chronological append order.
                timeline.Sort(CompareRecency);
                int excess = timeline.Count - MaxTimelineRecords;
                timeline.RemoveRange(MaxTimelineRecords, excess);
                timeline.Reverse();
                removed += excess;
            }

            return removed;
        }

        private static int CompareRecency(CommercialEventRecord left, CommercialEventRecord right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return 1;
            if (right == null) return -1;
            int byTick = right.tick.CompareTo(left.tick);
            return byTick != 0 ? byTick : right.id.CompareTo(left.id);
        }

        /// <summary>
        /// Returns timeline records involving the specified settlement, newest first.
        /// If maxCount is greater than 0, caps the returned count.
        /// </summary>
        public static List<CommercialEventRecord> ForSettlement(
            IntercolonyWorldComponent state, int settlementId, int maxCount = 0)
        {
            List<CommercialEventRecord> results = new List<CommercialEventRecord>();
            List<CommercialEventRecord> timeline = state?.CommercialTimeline;
            if (timeline == null)
            {
                return results;
            }

            for (int i = timeline.Count - 1; i >= 0; i--)
            {
                CommercialEventRecord record = timeline[i];
                if (record != null && record.settlementId == settlementId)
                {
                    results.Add(record);
                    if (maxCount > 0 && results.Count >= maxCount)
                    {
                        break;
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Returns the most recent timeline records globally, newest first.
        /// </summary>
        public static List<CommercialEventRecord> Recent(
            IntercolonyWorldComponent state, int count)
        {
            List<CommercialEventRecord> results = new List<CommercialEventRecord>();
            List<CommercialEventRecord> timeline = state?.CommercialTimeline;
            if (timeline == null || count <= 0)
            {
                return results;
            }

            for (int i = timeline.Count - 1; i >= 0 && results.Count < count; i--)
            {
                if (timeline[i] != null)
                {
                    results.Add(timeline[i]);
                }
            }

            return results;
        }
    }
}
