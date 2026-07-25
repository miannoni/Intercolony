using System.Collections.Generic;
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
        public const int CurrentSaveVersion = 3;

        /// <summary>
        /// How often the scheduled refresh fires, in ticks. One in-game day (60,000 ticks).
        /// Coarse by design: never regenerate the economy per tick (DESIGN.md §59, §84).
        /// </summary>
        public const int RefreshIntervalTicks = 60000;

        /// <summary>Version this state was last written at. 0 means "predates versioning".</summary>
        private int saveVersion = CurrentSaveVersion;

        /// <summary>
        /// Monotonic source of stable entity IDs (DESIGN.md §72). One counter is shared
        /// by every entity kind so an ID is unique across the whole mod; human-readable
        /// aliases (e.g. "SO-42") are a display concern layered on top later.
        /// </summary>
        private int nextId = 1;

        /// <summary>
        /// Per-world seed for all economic generation (DESIGN.md §60 "persistent seeds").
        /// Every settlement profile is derived from this plus the settlement's stable ID, so
        /// this single int is the only thing about the economy that needs saving. 0 means
        /// "not yet assigned"; <see cref="EconomySeed"/> assigns it lazily.
        /// </summary>
        private int economySeed;

        /// <summary>
        /// Derived profiles, keyed by settlement ID. NOT persisted and not authoritative:
        /// regenerating from <see cref="economySeed"/> reproduces it exactly (§96). Purely a
        /// cache so profile lookups are not recomputing rolls every frame (§84).
        /// </summary>
        private readonly Dictionary<int, SettlementEconomicProfile> profileCache =
            new Dictionary<int, SettlementEconomicProfile>();

        /// <summary>Tick of the last refresh, or -1 if none has run yet.</summary>
        private int lastRefreshTick = -1;

        /// <summary>How many refreshes have run in this world's lifetime, scheduled or forced.</summary>
        private int refreshCount;

        // --- Phase 1/2 persistence probes. Delete once real state exists (DESIGN.md §94, §95). ---
        public int testCounter;
        public string testString = "";
        private List<IntercolonyTestRecord> testRecords = new List<IntercolonyTestRecord>();

        public IntercolonyWorldComponent(World world) : base(world)
        {
        }

        public int SaveVersion => saveVersion;

        public int LastRefreshTick => lastRefreshTick;

        public int RefreshCount => refreshCount;

        /// <summary>Read-only view; mutate through <see cref="CreateTestRecord"/> / <see cref="ClearTestState"/>.</summary>
        public List<IntercolonyTestRecord> TestRecords => testRecords;

        /// <summary>Ticks until the next scheduled refresh.</summary>
        public int TicksUntilNextRefresh => RefreshIntervalTicks - (GenTicks.TicksGame % RefreshIntervalTicks);

        /// <summary>
        /// Arbitrary salt so the economy seed does not equal the world seed itself, keeping
        /// Intercolony's rolls independent of anything else keyed off the world seed.
        /// </summary>
        private const int EconomySeedSalt = 0x1C7EC0;

        /// <summary>
        /// The world's economy seed, assigned on first access and then persisted.
        ///
        /// Derived from the world's own seed rather than drawn from <see cref="Rand"/>. Drawing
        /// would perturb RimWorld's global random state (DESIGN.md §60) at an arbitrary moment,
        /// and would make the entire economy depend on *when* the first profile happened to be
        /// requested. Deriving means the same world always produces the same economy, which is
        /// what makes profile regeneration reproducible for debugging.
        /// </summary>
        public int EconomySeed
        {
            get
            {
                if (economySeed == 0)
                {
                    economySeed = Gen.HashCombineInt(world?.info?.Seed ?? 0, EconomySeedSalt);

                    // 0 is the "unassigned" sentinel, so a hash that lands on it must move.
                    if (economySeed == 0)
                    {
                        economySeed = EconomySeedSalt;
                    }

                    IntercolonyLog.Message($"Derived economy seed {economySeed} from the world seed.");
                }

                return economySeed;
            }
        }

        /// <summary>
        /// Profile for a settlement, generated on demand and cached (DESIGN.md §9, §96).
        /// Returns null for settlements that are not economic participants, so callers must
        /// null-check — that is also how a destroyed settlement stops having an economy (§87).
        /// </summary>
        public SettlementEconomicProfile GetProfile(Settlement settlement)
        {
            if (!SettlementProfileGenerator.IsEligible(settlement))
            {
                return null;
            }

            if (profileCache.TryGetValue(settlement.ID, out SettlementEconomicProfile cached))
            {
                // Tech tier is inherited from the faction (§8), so a settlement changing hands
                // must invalidate its profile rather than keep its old owner's economy.
                if (cached.factionLoadId == (settlement.Faction?.loadID ?? -1))
                {
                    return cached;
                }

                IntercolonyLog.Verbose(
                    $"Settlement {settlement.ID} changed faction; regenerating profile.");
            }

            SettlementEconomicProfile profile = SettlementProfileGenerator.Generate(EconomySeed, settlement);
            profileCache[settlement.ID] = profile;
            return profile;
        }

        /// <summary>Every eligible settlement's profile, in world-object order.</summary>
        public List<SettlementEconomicProfile> AllProfiles()
        {
            List<SettlementEconomicProfile> profiles = new List<SettlementEconomicProfile>();
            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements == null)
            {
                return profiles;
            }

            foreach (Settlement settlement in settlements)
            {
                SettlementEconomicProfile profile = GetProfile(settlement);
                if (profile != null)
                {
                    profiles.Add(profile);
                }
            }

            return profiles;
        }

        /// <summary>Whether a profile is currently cached for this settlement ID. Debug inspection only.</summary>
        public bool HasCachedProfile(int settlementId)
        {
            return profileCache.ContainsKey(settlementId);
        }

        /// <summary>Forces a cache prune without waiting for a refresh. Debug inspection only.</summary>
        public void PruneProfileCacheNow()
        {
            PruneProfileCache();
        }

        /// <summary>
        /// Drops cached profiles so the next lookup regenerates them. Since generation is
        /// deterministic, this is a no-op in behaviour — which is exactly what makes it a
        /// useful test that regeneration really is deterministic (§96).
        /// </summary>
        public void ClearProfileCache()
        {
            int count = profileCache.Count;
            profileCache.Clear();
            IntercolonyLog.Message($"Profile cache cleared ({count} entr{(count == 1 ? "y" : "ies")}).");
        }

        /// <summary>
        /// Replaces the economy seed with a random one, regenerating every profile. Debug-only:
        /// this rewrites the character of every settlement in the world. Unlike the derived
        /// default this does draw from <see cref="Rand"/>, which is acceptable precisely because
        /// it is a manual dev action rather than something that happens during normal play.
        /// </summary>
        public void RerollEconomySeed()
        {
            profileCache.Clear();

            economySeed = 0;
            while (economySeed == 0)
            {
                economySeed = Rand.Int;
            }

            IntercolonyLog.Message($"Economy seed rerolled to {economySeed}; all profiles regenerated.");
        }

        /// <summary>
        /// Drops cache entries for settlements that no longer exist (DESIGN.md §87). Cheap and
        /// only runs on the coarse refresh, never per tick.
        /// </summary>
        private void PruneProfileCache()
        {
            if (profileCache.Count == 0)
            {
                return;
            }

            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements == null)
            {
                return;
            }

            HashSet<int> liveIds = new HashSet<int>();
            foreach (Settlement settlement in settlements)
            {
                liveIds.Add(settlement.ID);
            }

            List<int> stale = null;
            foreach (KeyValuePair<int, SettlementEconomicProfile> entry in profileCache)
            {
                if (!liveIds.Contains(entry.Key))
                {
                    stale = stale ?? new List<int>();
                    stale.Add(entry.Key);
                }
            }

            if (stale == null)
            {
                return;
            }

            foreach (int id in stale)
            {
                profileCache.Remove(id);
            }

            IntercolonyLog.Verbose($"Pruned {stale.Count} profile(s) for settlements that no longer exist.");
        }

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
            Scribe_Values.Look(ref economySeed, "economySeed", 0);
            Scribe_Values.Look(ref lastRefreshTick, "lastRefreshTick", -1);
            Scribe_Values.Look(ref refreshCount, "refreshCount", 0);
            Scribe_Values.Look(ref testCounter, "testCounter", 0);
            Scribe_Values.Look(ref testString, "testString", "");
            Scribe_Collections.Look(ref testRecords, "testRecords", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (testString == null)
                {
                    testString = "";
                }

                // A missing or IsNull list node loads as null, not as an empty list
                // (Scribe_Collections.Look, LoadingVars branch). Every consumer below
                // assumes non-null, so restore the invariant here rather than at each use.
                if (testRecords == null)
                {
                    testRecords = new List<IntercolonyTestRecord>();
                }
                else
                {
                    // Deep-loaded children are never expected to be null, but a corrupt or
                    // hand-edited save can produce them. Drop them loudly instead of
                    // throwing NREs from unrelated code later.
                    int removed = testRecords.RemoveAll(r => r == null);
                    if (removed > 0)
                    {
                        IntercolonyLog.Warning($"Dropped {removed} null test record(s) while loading.");
                    }
                }

                if (nextId < 1)
                {
                    IntercolonyLog.Warning($"Loaded nextId={nextId}; clamping to 1 to keep IDs positive.");
                    nextId = 1;
                }

                MigrateIfNeeded();
                ValidateIds();
            }
        }

        /// <summary>
        /// Fires the scheduled refresh on a coarse interval. World components tick every
        /// game tick, so this must stay a cheap modulo test and nothing else (DESIGN.md §84).
        /// </summary>
        public override void WorldComponentTick()
        {
            if (GenTicks.IsTickInterval(RefreshIntervalTicks))
            {
                DoRefresh("scheduled");
            }
        }

        /// <summary>
        /// Runs the refresh immediately (DESIGN.md §95). Note this does not shift the
        /// schedule: the next scheduled refresh still lands on the next multiple of
        /// <see cref="RefreshIntervalTicks"/>. The schedule is intentionally derived from
        /// absolute tick rather than from <see cref="lastRefreshTick"/>, so it cannot drift
        /// and can be staggered per settlement later (§59). <see cref="lastRefreshTick"/> is
        /// informational only.
        /// </summary>
        public void ForceRefreshNow()
        {
            DoRefresh("forced");
        }

        private void DoRefresh(string cause)
        {
            lastRefreshTick = GenTicks.TicksGame;
            refreshCount++;
            PruneProfileCache();

            // Nothing to regenerate yet: opportunity generation arrives with settlement
            // economic profiles (DESIGN.md §96+). This proves the cadence and gives that
            // work a hook to attach to. Seeded per-refresh RNG (§60) is still outstanding.
            IntercolonyLog.Verbose($"Refresh #{refreshCount} ({cause}) at tick {lastRefreshTick}.");
        }

        /// <summary>Creates a persisted test record with a freshly allocated ID (DESIGN.md §95).</summary>
        public IntercolonyTestRecord CreateTestRecord(string label = null)
        {
            IntercolonyTestRecord record = new IntercolonyTestRecord(
                NextId(),
                label ?? "test",
                GenTicks.TicksGame);
            testRecords.Add(record);
            return record;
        }

        /// <summary>
        /// Resets every Phase 1/2 probe field to its default (DESIGN.md §95 "clear test state").
        /// Deliberately does NOT rewind <see cref="nextId"/>: IDs must never be reissued, or
        /// a stale reference could silently resolve to a different entity.
        /// </summary>
        public void ClearTestState()
        {
            int cleared = testRecords.Count;
            testRecords.Clear();
            testCounter = 0;
            testString = "";
            lastRefreshTick = -1;
            refreshCount = 0;
            IntercolonyLog.Message($"Test state cleared ({cleared} record(s) removed). nextId left at {nextId}.");
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

            // One deliberate step per version. Each step upgrades in place and falls through
            // to the next, so a schema-0 save walks the whole chain.
            if (saveVersion < 2)
            {
                // 1 -> 2 added lastRefreshTick, refreshCount, and testRecords. All three are
                // additive with safe defaults already applied by ExposeData, so there is
                // nothing to move; the refresh clock simply starts as "never run".
                if (lastRefreshTick > GenTicks.TicksGame)
                {
                    IntercolonyLog.Warning(
                        $"lastRefreshTick {lastRefreshTick} is in the future (now {GenTicks.TicksGame}); resetting to never.");
                    lastRefreshTick = -1;
                }

                IntercolonyLog.Message("  schema 1 -> 2: refresh clock and test record list initialized.");
            }

            if (saveVersion < 3)
            {
                // 2 -> 3 added economySeed. Leaving it 0 is the correct default: EconomySeed
                // assigns one lazily on first use. A save upgraded from schema 2 therefore
                // gets a fresh economy, which is acceptable because no economic state existed
                // to preserve at schema 2.
                IntercolonyLog.Message("  schema 2 -> 3: economy seed will be assigned on first use.");
            }

            saveVersion = CurrentSaveVersion;
        }

        /// <summary>
        /// Guards the invariant that every allocated ID is below <see cref="nextId"/>
        /// (DESIGN.md §67 "validate IDs/references"). A save whose records outrank the
        /// counter would hand out duplicate IDs, which is the kind of corruption that
        /// surfaces much later as two orders sharing an identity.
        /// </summary>
        private void ValidateIds()
        {
            int highest = 0;
            foreach (IntercolonyTestRecord record in testRecords)
            {
                if (record.id > highest)
                {
                    highest = record.id;
                }
            }

            if (highest >= nextId)
            {
                IntercolonyLog.Warning(
                    $"Highest persisted ID {highest} is >= nextId {nextId}; advancing nextId to {highest + 1} to avoid duplicates.");
                nextId = highest + 1;
            }
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
            sb.AppendLine($"  saveVersion  : {saveVersion} (current {CurrentSaveVersion})");
            sb.AppendLine($"  nextId       : {nextId}");
            sb.AppendLine($"  economySeed  : {(economySeed == 0 ? "unassigned" : economySeed.ToString())}");
            sb.AppendLine($"  profiles     : {profileCache.Count} cached");
            sb.AppendLine($"  tick now     : {GenTicks.TicksGame}");
            sb.AppendLine($"  lastRefresh  : {LastRefreshTickDescription}");
            sb.AppendLine($"  refreshCount : {refreshCount}");
            sb.AppendLine($"  nextRefresh  : in {TicksUntilNextRefresh} ticks");
            sb.AppendLine($"  testCounter  : {testCounter}");
            sb.AppendLine($"  testString   : \"{testString}\"");
            sb.AppendLine($"  testRecords  : {testRecords.Count}");
            foreach (IntercolonyTestRecord record in testRecords)
            {
                sb.AppendLine($"    {record}");
            }

            return sb.ToString();
        }

        /// <summary>"never" or the tick, for display.</summary>
        public string LastRefreshTickDescription =>
            lastRefreshTick < 0 ? "never" : lastRefreshTick.ToString();
    }
}
