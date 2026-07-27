using System.Collections.Generic;
using System.Text;
using RimWorld;
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
        public const int CurrentSaveVersion = 12;

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

        /// <summary>
        /// Live market opportunities (DESIGN.md §7.2). Persisted, because §61 lists active
        /// opportunities as state that must survive save/load. This replaces the Phase 1/2
        /// test-record probe: the deep-list round trip it existed to de-risk now carries a
        /// real entity.
        /// </summary>
        private List<MarketOpportunity> opportunities = new List<MarketOpportunity>();

        /// <summary>
        /// Player's maximum acceptable haul, in world tiles, or <see cref="NoDistanceLimit"/>
        /// for no limit (DESIGN.md §53 filters, §66 "maximum market distance").
        ///
        /// Persisted because it is a per-save player preference: a young colony that cannot
        /// cross the planet should not have to re-set it after every reload.
        /// </summary>
        private float maxMarketDistance = NoDistanceLimit;

        /// <summary>Sentinel meaning "show everything regardless of distance".</summary>
        public const float NoDistanceLimit = 9999f;

        /// <summary>
        /// Binding sales orders (DESIGN.md §7.3, §61). Unlike opportunities these are
        /// obligations, so completed and failed ones are retained rather than removed —
        /// the player needs to see what happened, and §62 warns against silently dropping
        /// active obligations.
        /// </summary>
        private List<SalesOrder> orders = new List<SalesOrder>();

        public List<SalesOrder> Orders => orders;

        /// <summary>
        /// Purchase requests and their quotations (DESIGN.md §61 lists both as persistent).
        /// Retained after expiry so the player can see what was asked and what came back.
        /// </summary>
        private List<PurchaseRequest> requests = new List<PurchaseRequest>();

        public List<PurchaseRequest> Requests => requests;

        public void AddRequest(PurchaseRequest request)
        {
            if (request != null)
            {
                requests.Add(request);
            }
        }

        /// <summary>
        /// Commercial reputation per settlement (DESIGN.md §27, §61), keyed by the settlement's
        /// stable <c>WorldObject.ID</c>. §8 makes the settlement the primary economic actor, so
        /// a specific town's opinion of you is its own — two settlements of one faction can
        /// rate you quite differently.
        /// </summary>
        private Dictionary<int, CommercialReputation> reputations =
            new Dictionary<int, CommercialReputation>();

        public Dictionary<int, CommercialReputation> Reputations => reputations;

        public CommercialReputation FindReputation(int settlementId)
        {
            return reputations.TryGetValue(settlementId, out CommercialReputation rep) ? rep : null;
        }

        /// <summary>
        /// Reputation record for a settlement, created at neutral on first dealing. Records
        /// are only created when something actually happens, so the list stays a history of
        /// real trade rather than a roster of every settlement on the planet.
        /// </summary>
        public CommercialReputation GetOrCreateReputation(Settlement settlement)
        {
            if (settlement == null)
            {
                return null;
            }

            string factionName = settlement.Faction?.Name ?? "";

            if (reputations.TryGetValue(settlement.ID, out CommercialReputation existing))
            {
                // Both can change — settlements are renameable and can change hands.
                existing.settlementName = settlement.Label ?? existing.settlementName;
                existing.factionName = factionName;
                return existing;
            }

            CommercialReputation created = new CommercialReputation(
                settlement.ID, settlement.Label ?? "unnamed", factionName);
            reputations[settlement.ID] = created;
            return created;
        }

        /// <summary>Paid purchase orders awaiting delivery or collection (§7.6, §61).</summary>
        private List<PurchaseOrder> purchaseOrders = new List<PurchaseOrder>();

        public List<PurchaseOrder> PurchaseOrders => purchaseOrders;

        public void AddPurchaseOrder(PurchaseOrder order)
        {
            if (order != null)
            {
                purchaseOrders.Add(order);
            }
        }

        public PurchaseOrder FindPurchaseOrder(int orderId)
        {
            foreach (PurchaseOrder order in purchaseOrders)
            {
                if (order.id == orderId)
                {
                    return order;
                }
            }

            return null;
        }

        public int OpenPurchaseCount
        {
            get
            {
                int count = 0;
                foreach (PurchaseOrder order in purchaseOrders)
                {
                    if (order.IsOpen)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public int OpenRequestCount
        {
            get
            {
                int count = 0;
                foreach (PurchaseRequest request in requests)
                {
                    if (request.IsOpen)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public SalesOrder FindOrder(int orderId)
        {
            foreach (SalesOrder order in orders)
            {
                if (order.id == orderId)
                {
                    return order;
                }
            }

            return null;
        }

        public void AddOrder(SalesOrder order)
        {
            if (order != null)
            {
                orders.Add(order);
            }
        }

        public void RemoveOpportunity(MarketOpportunity opportunity)
        {
            opportunities.Remove(opportunity);
        }

        /// <summary>Open orders, i.e. still owed.</summary>
        public int OpenOrderCount
        {
            get
            {
                int count = 0;
                foreach (SalesOrder order in orders)
                {
                    if (order.IsOpen)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public float MaxMarketDistance
        {
            get => maxMarketDistance;
            set => maxMarketDistance = value;
        }

        public IntercolonyWorldComponent(World world) : base(world)
        {
        }

        public int SaveVersion => saveVersion;

        public int LastRefreshTick => lastRefreshTick;

        public int RefreshCount => refreshCount;

        /// <summary>Read-only view; mutate through the generation and expiry paths below.</summary>
        public List<MarketOpportunity> Opportunities => opportunities;

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
            Scribe_Values.Look(ref maxMarketDistance, "maxMarketDistance", NoDistanceLimit);
            Scribe_Collections.Look(ref opportunities, "opportunities", LookMode.Deep);
            Scribe_Collections.Look(ref orders, "orders", LookMode.Deep);
            Scribe_Collections.Look(ref requests, "requests", LookMode.Deep);
            Scribe_Collections.Look(ref purchaseOrders, "purchaseOrders", LookMode.Deep);
            Scribe_Collections.Look(ref reputations, "settlementReputations", LookMode.Value, LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // A missing or IsNull list node loads as null, not as an empty list
                // (Scribe_Collections.Look, LoadingVars branch). Every consumer below
                // assumes non-null, so restore the invariant here rather than at each use.
                if (opportunities == null)
                {
                    opportunities = new List<MarketOpportunity>();
                }
                else
                {
                    // A null child means a corrupt save; an opportunity whose ThingDef no
                    // longer resolves means a mod was removed since the save. Drop both
                    // loudly rather than letting them surface as NREs elsewhere (§64, §86).
                    int nulls = opportunities.RemoveAll(o => o == null);
                    int broken = opportunities.RemoveAll(o => !o.IsValidAfterLoad);
                    if (nulls > 0 || broken > 0)
                    {
                        IntercolonyLog.Warning(
                            $"Dropped {nulls} null and {broken} unresolvable opportunit(ies) while loading. " +
                            "Unresolvable usually means a mod that supplied the item was removed.");
                    }
                }

                if (orders == null)
                {
                    orders = new List<SalesOrder>();
                }
                else
                {
                    int nullOrders = orders.RemoveAll(o => o == null);
                    int brokenOrders = orders.RemoveAll(o => !o.IsValidAfterLoad);
                    if (nullOrders > 0 || brokenOrders > 0)
                    {
                        // §62 is explicit that migration must not silently drop active
                        // obligations, so this is an error rather than a quiet warning:
                        // a dropped order is a broken promise the player cannot see.
                        IntercolonyLog.Error(
                            $"Dropped {nullOrders} null and {brokenOrders} unresolvable order(s) while loading. " +
                            "This usually means a mod supplying an ordered item was removed.");
                    }
                }

                if (requests == null)
                {
                    requests = new List<PurchaseRequest>();
                }
                else
                {
                    int nullRequests = requests.RemoveAll(r => r == null);
                    int brokenRequests = requests.RemoveAll(r => !r.IsValidAfterLoad);
                    if (nullRequests > 0 || brokenRequests > 0)
                    {
                        IntercolonyLog.Warning(
                            $"Dropped {nullRequests} null and {brokenRequests} unresolvable request(s) " +
                            "while loading. Unresolvable usually means a mod supplying the item was removed.");
                    }
                }

                if (purchaseOrders == null)
                {
                    purchaseOrders = new List<PurchaseOrder>();
                }
                else
                {
                    int nullPurchases = purchaseOrders.RemoveAll(o => o == null);
                    int brokenPurchases = purchaseOrders.RemoveAll(o => !o.IsValidAfterLoad);
                    if (nullPurchases > 0 || brokenPurchases > 0)
                    {
                        // A purchase is silver already spent, so losing one is worse than
                        // losing a listing (§62).
                        IntercolonyLog.Error(
                            $"Dropped {nullPurchases} null and {brokenPurchases} unresolvable purchase order(s) " +
                            "while loading. Any silver paid for them is gone.");
                    }
                }

                if (reputations == null)
                {
                    reputations = new Dictionary<int, CommercialReputation>();
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

            // Deadlines are checked hourly rather than on the daily refresh. §17 is explicit
            // that an order must not silently fail; noticing up to a day late would make the
            // failure message arrive long after the moment it describes. Still coarse enough
            // to be free (§84).
            if (GenTicks.IsTickInterval(DeadlineCheckIntervalTicks))
            {
                if (orders.Count > 0)
                {
                    SalesOrderService.FailOverdue(orders);
                }

                // Purchases become ready on their own schedule; checking hourly means the
                // "ready to collect" letter lands near the moment it describes (§17).
                if (purchaseOrders.Count > 0)
                {
                    PurchaseOrderService.AdvanceOrders(purchaseOrders);
                }

                // Buyers arriving to collect (§25.2). Hourly so the letter lands near the
                // moment it describes.
                if (orders.Count > 0)
                {
                    SalesOrderService.ProcessBuyerCollections(orders);
                }
            }
        }

        /// <summary>One in-game hour.</summary>
        private const int DeadlineCheckIntervalTicks = 2500;

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

            int expired = ExpireStaleOpportunities();
            int withdrawn = DropInaccessibleOpportunities();
            int created = GenerateOpportunities();
            RfqService.ExpireStale(requests);
            PurchaseOrderService.AdvanceOrders(purchaseOrders);

            IntercolonyLog.Verbose(
                $"Refresh #{refreshCount} ({cause}) at tick {lastRefreshTick}: " +
                $"{expired} expired, {withdrawn} withdrawn, {created} created, " +
                $"{ActiveOpportunityCount} active.");
        }

        /// <summary>Opportunities still available and not past their expiry tick.</summary>
        public int ActiveOpportunityCount
        {
            get
            {
                int count = 0;
                foreach (MarketOpportunity opportunity in opportunities)
                {
                    if (opportunity.IsAvailable)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>
        /// Marks lapsed opportunities expired and drops them (DESIGN.md §97 "opportunities
        /// expire"). Removal rather than retention keeps the saved list bounded; a history of
        /// missed opportunities is a §75 transaction-log concern, not this list's job.
        /// </summary>
        public int ExpireStaleOpportunities()
        {
            int now = GenTicks.TicksGame;
            int expired = 0;

            for (int i = opportunities.Count - 1; i >= 0; i--)
            {
                MarketOpportunity opportunity = opportunities[i];
                if (opportunity.IsAvailable && opportunity.HasExpired(now))
                {
                    opportunity.TryExpire();
                    expired++;
                }

                if (!opportunity.IsAvailable)
                {
                    opportunities.RemoveAt(i);
                }
            }

            return expired;
        }

        /// <summary>
        /// Removes listings whose buyer has become unreachable — turned hostile, or ceased to
        /// exist (DESIGN.md §51, §87). Opportunities are non-binding, so withdrawing them
        /// costs the player nothing; binding contracts caught by a war are §88's problem.
        /// </summary>
        public int DropInaccessibleOpportunities()
        {
            int dropped = 0;
            for (int i = opportunities.Count - 1; i >= 0; i--)
            {
                MarketOpportunity opportunity = opportunities[i];

                // Also drop offers for goods that are no longer tradable at all. A save made
                // before an item was excluded — silver, or anything newly blacklisted — would
                // otherwise keep advertising it forever, since nothing else revisits the
                // eligibility of an already-generated listing.
                bool stillSellable = IntercolonyProductClassifier.IsFungibleTradeItem(opportunity.thingDef);

                if (!stillSellable || !IntercolonyMarketAccess.IsStillValid(opportunity))
                {
                    opportunities.RemoveAt(i);
                    dropped++;
                }
            }

            return dropped;
        }

        /// <summary>
        /// Generates new demand across eligible settlements (DESIGN.md §11, §97).
        /// Called from the coarse refresh only.
        /// </summary>
        public int GenerateOpportunities()
        {
            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements == null)
            {
                return 0;
            }

            // Count existing per settlement once, rather than rescanning inside the loop.
            Dictionary<int, int> perSettlement = new Dictionary<int, int>();
            foreach (MarketOpportunity opportunity in opportunities)
            {
                perSettlement.TryGetValue(opportunity.settlementId, out int n);
                perSettlement[opportunity.settlementId] = n + 1;
            }

            int slots = MaxLiveOpportunities - ActiveOpportunityCount;
            if (slots <= 0)
            {
                return 0;
            }

            // Visit settlements in a seeded shuffle. Iterating world-object order would let
            // the same handful of settlements claim every slot on every refresh, so distant
            // or late-indexed settlements would never post anything (§48: far settlements
            // must not become useless). Seeded on the refresh number so the choice is still
            // reproducible for debugging (§60).
            List<Settlement> candidates = new List<Settlement>();
            foreach (Settlement settlement in settlements)
            {
                if (GetProfile(settlement) != null)
                {
                    candidates.Add(settlement);
                }
            }

            ShuffleSeeded(candidates, Gen.HashCombineInt(EconomySeed, refreshCount, 0x5A1F, 0));

            int created = 0;
            foreach (Settlement settlement in candidates)
            {
                if (slots <= 0)
                {
                    break;
                }

                SettlementEconomicProfile profile = GetProfile(settlement);
                perSettlement.TryGetValue(settlement.ID, out int existing);
                List<MarketOpportunity> fresh = MarketOpportunityGenerator.GenerateFor(
                    settlement, profile, EconomySeed, refreshCount, existing, NextId);

                foreach (MarketOpportunity opportunity in fresh)
                {
                    if (slots <= 0)
                    {
                        break;
                    }

                    opportunities.Add(opportunity);
                    created++;
                    slots--;
                }
            }

            return created;
        }

        /// <summary>
        /// Ceiling on live offers, regardless of world size (DESIGN.md §5.2 "No infinite
        /// global catalog").
        ///
        /// The per-settlement cap alone is not enough: total demand scaled with the number of
        /// settlements, which is invisible on a small test map and produced 695 live offers on
        /// a full-size world. A flat ceiling keeps the market readable and keeps the refresh
        /// cheap. The exact number is a first-pass guess; balance is §78.
        /// </summary>
        public const int MaxLiveOpportunities = 60;

        /// <summary>
        /// Fisher-Yates using a local seeded RNG, so shuffling cannot disturb the global
        /// random stream (§60).
        /// </summary>
        private static void ShuffleSeeded<T>(List<T> list, int seed)
        {
            Rand.PushState(seed);
            try
            {
                for (int i = list.Count - 1; i > 0; i--)
                {
                    int j = Rand.Range(0, i + 1);
                    T tmp = list[i];
                    list[i] = list[j];
                    list[j] = tmp;
                }
            }
            finally
            {
                Rand.PopState();
            }
        }

        /// <summary>Removes every opportunity. Debug only.</summary>
        public void ClearOpportunities()
        {
            int count = opportunities.Count;
            opportunities.Clear();
            IntercolonyLog.Message($"Cleared {count} opportunit{(count == 1 ? "y" : "ies")}.");
        }

        /// <summary>Forces every live opportunity to lapse immediately. Debug only.</summary>
        public int ExpireAllOpportunitiesNow()
        {
            foreach (MarketOpportunity opportunity in opportunities)
            {
                opportunity.expiryTick = GenTicks.TicksGame;
            }

            return ExpireStaleOpportunities();
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

            if (saveVersion < 4)
            {
                // 3 -> 4 retired the Phase 1/2 test probes (testCounter, testString,
                // testRecords) now that a real persisted entity exists, and added the
                // opportunity list. The retired nodes are simply no longer read; Scribe
                // ignores unknown XML, so old saves load cleanly and the dead data is
                // dropped the next time the game is saved.
                //
                // Nothing of value is lost: the probes were scaffolding by construction.
                // nextId is deliberately NOT rewound, so IDs once issued to test records are
                // never reissued to opportunities.
                IntercolonyLog.Message(
                    "  schema 3 -> 4: test probes retired, market opportunity list added.");
            }

            if (saveVersion < 5)
            {
                // 4 -> 5 added the distance filter and per-opportunity distance. Existing
                // opportunities keep distanceTiles = -1 ("unknown"), which the filter treats
                // as always-visible so a migrated save never hides listings the player could
                // previously see.
                IntercolonyLog.Message(
                    "  schema 4 -> 5: distance filter added; existing opportunities have unknown distance.");
            }

            if (saveVersion < 6)
            {
                // 5 -> 6 added sales orders. Purely additive: a save from schema 5 simply has
                // no orders yet, which is the correct state for a colony that never accepted one.
                IntercolonyLog.Message("  schema 5 -> 6: sales orders added.");
            }

            if (saveVersion < 7)
            {
                // 6 -> 7 moved an order's item and quantity into an OrderLine that can also
                // carry quality, material and condition constraints. SalesOrder.ExposeData
                // still reads the schema-6 nodes and rebuilds a line from them, so an order
                // accepted before this change keeps its terms rather than becoming an empty
                // promise — §62 forbids silently dropping active obligations.
                IntercolonyLog.Message(
                    "  schema 6 -> 7: order items migrated into constraint-capable order lines.");
            }

            if (saveVersion < 8)
            {
                // 7 -> 8 added purchase requests and quotations. Purely additive: a save from
                // schema 7 simply has none, which is correct for a colony that never asked.
                IntercolonyLog.Message("  schema 7 -> 8: purchase requests added.");
            }

            if (saveVersion < 9)
            {
                // 8 -> 9 added purchase orders. Purely additive.
                IntercolonyLog.Message("  schema 8 -> 9: purchase orders added.");
            }

            if (saveVersion < 10)
            {
                // 9 -> 10 added fulfilment modes. Existing sales orders default to
                // SellerDelivery, which is what they were implicitly, so nothing changes for
                // an order already in flight.
                IntercolonyLog.Message("  schema 9 -> 10: fulfilment modes added; existing orders are seller-delivery.");
            }

            if (saveVersion < 11)
            {
                // 10 -> 11 added commercial reputation. A save with trade history behind it
                // starts every faction at neutral: reconstructing a record from orders that
                // were completed before reputation existed would be inventing a past.
                IntercolonyLog.Message("  schema 10 -> 11: commercial reputation added, all parties neutral.");
            }

            if (saveVersion < 12)
            {
                // 11 -> 12 re-keyed reputation from faction to settlement (§8: the settlement
                // is the primary economic actor). Schema-11 records are read under a different
                // node name and so are simply not loaded — a faction record cannot be split
                // across its settlements without inventing history that never happened.
                IntercolonyLog.Message(
                    "  schema 11 -> 12: reputation is now per settlement; any faction-level records were discarded.");
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
            foreach (MarketOpportunity opportunity in opportunities)
            {
                if (opportunity.id > highest)
                {
                    highest = opportunity.id;
                }
            }

            foreach (SalesOrder order in orders)
            {
                if (order.id > highest)
                {
                    highest = order.id;
                }
            }

            foreach (PurchaseOrder purchase in purchaseOrders)
            {
                if (purchase.id > highest)
                {
                    highest = purchase.id;
                }
            }

            foreach (PurchaseRequest request in requests)
            {
                if (request.id > highest)
                {
                    highest = request.id;
                }

                foreach (Quotation quote in request.quotes)
                {
                    if (quote.id > highest)
                    {
                        highest = quote.id;
                    }
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
            sb.AppendLine($"  opportunities: {opportunities.Count} ({ActiveOpportunityCount} available)");
            foreach (MarketOpportunity opportunity in opportunities)
            {
                sb.AppendLine($"    {opportunity}  {opportunity.DaysRemaining:F1}d left");
            }

            sb.AppendLine($"  orders       : {orders.Count} ({OpenOrderCount} open)");
            foreach (SalesOrder order in orders)
            {
                sb.AppendLine($"    {order}  {(order.IsOpen ? $"{order.DaysRemaining:F1}d left" : order.outcomeNote)}");
            }

            return sb.ToString();
        }

        /// <summary>"never" or the tick, for display.</summary>
        public string LastRefreshTickDescription =>
            lastRefreshTick < 0 ? "never" : lastRefreshTick.ToString();
    }
}
