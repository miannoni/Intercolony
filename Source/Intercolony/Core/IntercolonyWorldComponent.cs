using System.Text;
using RimWorld.Planet;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// The single authoritative owner of Intercolony's persistent economic state
    /// (DESIGN.md §71). A <see cref="WorldComponent"/> because the state is world-level,
    /// not map-level: it must survive colony abandonment, multiple player maps, and
    /// caravan travel, and it needs access to the faction/settlement layer.
    ///
    /// RimWorld constructs exactly one of these per world in
    /// <c>World.FillComponents</c> and saves it with the world, so there is no
    /// singleton to manage and no duplicate-owner risk.
    ///
    /// Do not store authoritative state anywhere else — UI, statics, or map components.
    /// </summary>
    public class IntercolonyWorldComponent : WorldComponent
    {
        /// <summary>
        /// Current schema version of Intercolony's persisted state (DESIGN.md §62).
        /// Bump this whenever the saved shape changes, and add a migration step in
        /// <see cref="MigrateIfNeeded"/>.
        /// </summary>
        public const int CurrentSaveVersion = 1;

        /// <summary>Version this state was last written at. 0 means "predates versioning".</summary>
        private int saveVersion = CurrentSaveVersion;

        /// <summary>
        /// Monotonic source of stable entity IDs (DESIGN.md §72). One counter is shared
        /// by every entity kind so an ID is unique across the whole mod; human-readable
        /// aliases (e.g. "SO-42") are a display concern layered on top later.
        /// </summary>
        private int nextId = 1;

        // --- Phase 1 persistence probe. Delete once real state exists (DESIGN.md §94). ---
        public int testCounter;
        public string testString = "";

        public IntercolonyWorldComponent(World world) : base(world)
        {
        }

        public int SaveVersion => saveVersion;

        /// <summary>
        /// The live state owner, or null when no world is loaded. Always null-check:
        /// this is reachable from the main menu and during world generation.
        /// </summary>
        public static IntercolonyWorldComponent Current => Find.World?.GetComponent<IntercolonyWorldComponent>();

        /// <summary>Allocates the next stable entity ID. Survives save/load.</summary>
        public int NextId()
        {
            return nextId++;
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Values.Look(ref saveVersion, "saveVersion", 0);
            Scribe_Values.Look(ref nextId, "nextId", 1);
            Scribe_Values.Look(ref testCounter, "testCounter", 0);
            Scribe_Values.Look(ref testString, "testString", "");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (testString == null)
                {
                    testString = "";
                }

                if (nextId < 1)
                {
                    IntercolonyLog.Warning($"Loaded nextId={nextId}; clamping to 1 to keep IDs positive.");
                    nextId = 1;
                }

                MigrateIfNeeded();
            }
        }

        /// <summary>
        /// Brings loaded state up to <see cref="CurrentSaveVersion"/>. Migrations must
        /// use safe defaults and must never silently drop active obligations (DESIGN.md §62).
        /// </summary>
        private void MigrateIfNeeded()
        {
            if (saveVersion == CurrentSaveVersion)
            {
                return;
            }

            if (saveVersion > CurrentSaveVersion)
            {
                IntercolonyLog.Warning(
                    $"Save was written by a newer Intercolony (schema {saveVersion} > {CurrentSaveVersion}). " +
                    "Loading anyway; unknown fields were dropped.");
                return;
            }

            IntercolonyLog.Message($"Migrating state from schema {saveVersion} to {CurrentSaveVersion}.");

            // No migration steps yet: schema 1 is the first version. When schema 2 lands,
            // migrate 0/1 -> 2 here, one deliberate step per version.
            saveVersion = CurrentSaveVersion;
        }

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            IntercolonyLog.Verbose(
                fromLoad
                    ? $"State loaded (schema {saveVersion}, nextId {nextId})."
                    : $"State initialized fresh (schema {saveVersion}).");
        }

        /// <summary>Human-readable dump of everything persisted, for debug inspection (DESIGN.md §67).</summary>
        public string DebugStateSummary()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Intercolony world state");
            sb.AppendLine($"  saveVersion : {saveVersion} (current {CurrentSaveVersion})");
            sb.AppendLine($"  nextId      : {nextId}");
            sb.AppendLine($"  testCounter : {testCounter}");
            sb.AppendLine($"  testString  : \"{testString}\"");
            return sb.ToString();
        }
    }
}
