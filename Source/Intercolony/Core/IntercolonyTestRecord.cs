using Verse;

namespace Intercolony
{
    /// <summary>
    /// Authoritative lifecycle for <see cref="IntercolonyTestRecord"/> (DESIGN.md §73).
    /// Deliberately shaped like the real order lifecycle so the transition discipline
    /// carries over: Pending -> Active -> Closed, with no path back.
    /// </summary>
    public enum IntercolonyTestRecordState
    {
        Pending,
        Active,
        Closed
    }

    /// <summary>
    /// A throwaway persisted entity, created by the Phase 2 debug framework (DESIGN.md §95).
    ///
    /// Its real job is to de-risk the persistence pattern every later system depends on:
    /// a list of <see cref="IExposable"/> children scribed with <c>LookMode.Deep</c>, holding
    /// IDs from the world component's generator and a state-machine enum. That round trip is
    /// the most common place RimWorld save/load goes wrong, so it is worth proving on a
    /// disposable type before sales orders and employment contracts rely on it.
    ///
    /// Delete this type once a real persisted entity exists (Phase 3+).
    /// </summary>
    public class IntercolonyTestRecord : IExposable
    {
        public int id;
        public string label = "";
        public int createdTick;
        public IntercolonyTestRecordState state = IntercolonyTestRecordState.Pending;

        /// <summary>Required by Scribe: deep-loaded children are constructed parameterlessly.</summary>
        public IntercolonyTestRecord()
        {
        }

        public IntercolonyTestRecord(int id, string label, int createdTick)
        {
            this.id = id;
            this.label = label;
            this.createdTick = createdTick;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", 0);
            Scribe_Values.Look(ref label, "label", "");
            Scribe_Values.Look(ref createdTick, "createdTick", 0);
            Scribe_Values.Look(ref state, "state", IntercolonyTestRecordState.Pending);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && label == null)
            {
                label = "";
            }
        }

        /// <summary>
        /// The only legal way to change state. Returns false and logs rather than
        /// silently permitting an impossible transition (DESIGN.md §73).
        /// </summary>
        public bool TryAdvance()
        {
            switch (state)
            {
                case IntercolonyTestRecordState.Pending:
                    state = IntercolonyTestRecordState.Active;
                    return true;
                case IntercolonyTestRecordState.Active:
                    state = IntercolonyTestRecordState.Closed;
                    return true;
                default:
                    IntercolonyLog.Warning($"Record {id} is already {state}; refusing to advance further.");
                    return false;
            }
        }

        public override string ToString()
        {
            return $"#{id} {label} [{state}] created@{createdTick}";
        }
    }
}
