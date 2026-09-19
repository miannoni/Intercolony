using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// One planned item. This is deliberately only def/stuff/quality data; it is not a Thing.
    /// </summary>
    public sealed class LaborEquipmentPackageItemPlan
    {
        internal LaborEquipmentPackageItemPlan(LaborEquipmentCatalogueEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            Def = entry.Def;
            StuffDef = entry.StuffDef;
            Quality = entry.Quality;
            HasQuality = entry.HasQuality;
            BaseScore = entry.BaseScore;
            Score = entry.Score;
            SpecialPurposePenalty = entry.SpecialPurposePenalty;
            Band = entry.Band;
        }

        public ThingDef Def { get; }

        public ThingDef StuffDef { get; }

        public QualityCategory Quality { get; }

        public bool HasQuality { get; }

        public float BaseScore { get; }

        /// <summary>The score after the catalogue's special-purpose penalty.</summary>
        public float Score { get; }

        public float SpecialPurposePenalty { get; }

        public LaborEquipmentItemBand Band { get; }

        // These aliases keep the plan readable to the later instantiation unit without exposing
        // the catalogue's internal entry type.
        public ThingDef ThingDef => Def;

        public ThingDef Stuff => StuffDef;
    }

    /// <summary>
    /// The data-only result of <see cref="LaborEquipmentPackagePlanner.Plan"/>.
    ///
    /// The planner owns no pawn and creates no Things. The next equipment unit turns these
    /// definitions into real items and performs the authoritative classification/retry loop.
    /// </summary>
    public sealed class LaborEquipmentPackagePlan
    {
        internal LaborEquipmentPackagePlan(
            LaborProspect prospect,
            SettlementEconomicProfile sourceSettlement,
            LaborEquipmentLevel promisedTier,
            CombatClause clause,
            int marketIdentity,
            int seed,
            int retryAttempt,
            TechLevel itemTechCeiling,
            LaborEquipmentPackageItemPlan weapon,
            IReadOnlyList<LaborEquipmentPackageItemPlan> apparel)
        {
            Prospect = prospect;
            SourceSettlementId = prospect == null ? -1 : prospect.settlementId;
            SourceMarketSeed = sourceSettlement == null ? 0 : sourceSettlement.seed;
            MarketIdentity = marketIdentity;
            PromisedTier = promisedTier;
            Clause = clause;
            Seed = seed;
            RetryAttempt = retryAttempt;
            ItemTechCeiling = itemTechCeiling;
            Weapon = weapon;
            Apparel = apparel ?? new List<LaborEquipmentPackageItemPlan>().AsReadOnly();
        }

        /// <summary>The lightweight prospect this stable plan was derived from.</summary>
        public LaborProspect Prospect { get; }

        public int SourceSettlementId { get; }

        /// <summary>The stable seed stored on the source settlement's economic profile.</summary>
        public int SourceMarketSeed { get; }

        /// <summary>The caller's stable posting/market identity, such as a JobPosting id.</summary>
        public int MarketIdentity { get; }

        public LaborEquipmentLevel PromisedTier { get; }

        public CombatClause Clause { get; }

        /// <summary>The complete deterministic seed used by this plan.</summary>
        public int Seed { get; }

        /// <summary>
        /// The bounded retry metadata retained for callers that already expose this plan field.
        /// Candidate selection itself now always uses the seeded deterministic candidate space.
        /// </summary>
        public int RetryAttempt { get; }

        public TechLevel ItemTechCeiling { get; }

        /// <summary>Null for civilians or for an intentionally incomplete package.</summary>
        public LaborEquipmentPackageItemPlan Weapon { get; }

        public LaborEquipmentPackageItemPlan WeaponChoice => Weapon;

        public bool HasWeapon => Weapon != null;

        /// <summary>Chosen apparel definitions, possibly incomplete by design.</summary>
        public IReadOnlyList<LaborEquipmentPackageItemPlan> Apparel { get; }

        public IReadOnlyList<LaborEquipmentPackageItemPlan> ApparelChoices => Apparel;
    }

    /// <summary>
    /// Chooses a varied equipment package from the cached, def-driven catalogue.
    ///
    /// This is intentionally a planning boundary. It never creates Things, touches a Pawn, or
    /// calls LaborEquipmentTierService.Classify. A caller must provide a tier that has already
    /// passed the settlement capability gate; this class only uses that promised tier to build the
    /// complete candidate space. The allocator owns the seeded candidate walk and the real
    /// Classify acceptance test.
    /// </summary>
    public static class LaborEquipmentPackagePlanner
    {
        private const int PlannerSeedSalt = 0x504C_4E52;
        private const int CandidateOrderSeedSalt = 0x4F52_4452;

        private const int MaxApparelItems = 5;
        private const int MaxAlternativeApparelAnchors = 8;


        private static readonly IReadOnlyList<LaborEquipmentPackageItemPlan> EmptyApparel =
            new List<LaborEquipmentPackageItemPlan>().AsReadOnly();

        private static readonly IReadOnlyList<LaborEquipmentPackagePlan> EmptyPlans =
            new List<LaborEquipmentPackagePlan>().AsReadOnly();

        private static readonly IReadOnlyList<LaborEquipmentCatalogueEntry>
            EmptyCatalogueEntries = new List<LaborEquipmentCatalogueEntry>().AsReadOnly();

        private static readonly QualityCategory[] QualityOrder =
        {
            QualityCategory.Awful,
            QualityCategory.Poor,
            QualityCategory.Normal,
            QualityCategory.Good,
            QualityCategory.Excellent,
            QualityCategory.Masterwork,
            QualityCategory.Legendary
        };

        /// <summary>
        /// Builds one deterministic package plan for an already-promised tier.
        ///
        /// <paramref name="marketIdentity"/> is a stable market-side identity supplied by the
        /// caller (for example, a persisted posting id). It is intentionally not a tick, redraw
        /// count, DateTime value, or object hash.
        /// </summary>
        public static LaborEquipmentPackagePlan Plan(
            LaborProspect prospect,
            SettlementEconomicProfile sourceSettlement,
            LaborEquipmentLevel promisedTier,
            CombatClause clause,
            int marketIdentity)
        {
            return Plan(
                prospect,
                sourceSettlement,
                promisedTier,
                clause,
                marketIdentity,
                compatibleApparelDefinitions: null,
                retryAttempt: 0);
        }

        /// <summary>
        /// Builds one plan using a precomputed, Pawn-owned apparel compatibility filter. The
        /// planner only applies the supplied definition set to its catalogue pool; it never
        /// receives or inspects a Pawn.
        /// </summary>
        public static LaborEquipmentPackagePlan Plan(
            LaborProspect prospect,
            SettlementEconomicProfile sourceSettlement,
            LaborEquipmentLevel promisedTier,
            CombatClause clause,
            int marketIdentity,
            ISet<ThingDef> compatibleApparelDefinitions)
        {
            return Plan(
                prospect,
                sourceSettlement,
                promisedTier,
                clause,
                marketIdentity,
                compatibleApparelDefinitions,
                retryAttempt: 0);
        }

        /// <summary>Builds a plan at an explicit retry stage without a BodyDef filter.</summary>
        public static LaborEquipmentPackagePlan Plan(
            LaborProspect prospect,
            SettlementEconomicProfile sourceSettlement,
            LaborEquipmentLevel promisedTier,
            CombatClause clause,
            int marketIdentity,
            ISet<ThingDef> compatibleApparelDefinitions,
            int retryAttempt)
        {
            return Plan(
                prospect,
                sourceSettlement,
                promisedTier,
                clause,
                marketIdentity,
                compatibleApparelDefinitions,
                body: null,
                retryAttempt: retryAttempt);
        }

        /// <summary>
        /// Builds one plan using the first candidate in the seeded deterministic candidate order.
        /// Production fulfilment uses PlanDeterministicCandidates so it can instantiate and
        /// classify each candidate until one passes.
        /// </summary>
        public static LaborEquipmentPackagePlan Plan(
            LaborProspect prospect,
            SettlementEconomicProfile sourceSettlement,
            LaborEquipmentLevel promisedTier,
            CombatClause clause,
            int marketIdentity,
            ISet<ThingDef> compatibleApparelDefinitions,
            BodyDef body,
            int retryAttempt)
        {
            if (prospect == null || sourceSettlement == null || !IsPackageTier(promisedTier))
            {
                return null;
            }

            retryAttempt = NormalizeRetryAttempt(retryAttempt);
            IReadOnlyList<LaborEquipmentPackagePlan> candidates =
                PlanDeterministicCandidates(
                    prospect,
                    sourceSettlement,
                    promisedTier,
                    clause,
                    marketIdentity,
                    compatibleApparelDefinitions,
                    body,
                    retryAttempt);
            return candidates == null || candidates.Count == 0 ? null : candidates[0];
        }

        /// <summary>
        /// Returns the complete data-only candidate space in a stable, applicant-seeded order.
        /// The allocator owns the Pawn and the authoritative Classify call; this method only
        /// returns candidates for the allocator to instantiate and test in order.
        /// </summary>
        internal static IReadOnlyList<LaborEquipmentPackagePlan> PlanDeterministicCandidates(
            LaborProspect prospect,
            SettlementEconomicProfile sourceSettlement,
            LaborEquipmentLevel promisedTier,
            CombatClause clause,
            int marketIdentity,
            ISet<ThingDef> compatibleApparelDefinitions,
            BodyDef body,
            int retryAttempt)
        {
            if (prospect == null || sourceSettlement == null || !IsPackageTier(promisedTier))
            {
                return EmptyPlans;
            }

            retryAttempt = NormalizeRetryAttempt(retryAttempt);
            TechLevel itemTechCeiling = GetItemTechCeiling(
                sourceSettlement, promisedTier);
            int seed = SeedFor(
                prospect, sourceSettlement, promisedTier, clause, marketIdentity);
            if (promisedTier == LaborEquipmentLevel.None)
            {
                return new List<LaborEquipmentPackagePlan>
                {
                    new LaborEquipmentPackagePlan(
                        prospect,
                        sourceSettlement,
                        promisedTier,
                        clause,
                        marketIdentity,
                        seed,
                        retryAttempt,
                        itemTechCeiling,
                        weapon: null,
                        EmptyApparel)
                }.AsReadOnly();
            }

            IReadOnlyList<LaborEquipmentPackagePlan> plans = BuildDeterministicPlans(
                prospect,
                sourceSettlement,
                promisedTier,
                clause,
                marketIdentity,
                compatibleApparelDefinitions,
                body,
                retryAttempt,
                itemTechCeiling,
                seed);
            return RandomizeCandidateOrder(plans, seed);
        }

        private static IReadOnlyList<LaborEquipmentPackagePlan> RandomizeCandidateOrder(
            IReadOnlyList<LaborEquipmentPackagePlan> candidates,
            int seed)
        {
            if (candidates == null || candidates.Count < 2)
            {
                return candidates;
            }

            List<LaborEquipmentPackagePlan> ordered =
                new List<LaborEquipmentPackagePlan>(candidates);
            int orderSeed = Gen.HashCombineInt(seed, CandidateOrderSeedSalt);
            for (int index = ordered.Count - 1; index > 0; index--)
            {
                int swapSeed = Gen.HashCombineInt(orderSeed, index);
                int swapIndex = Rand.RangeSeeded(0, index + 1, swapSeed);
                LaborEquipmentPackagePlan swap = ordered[index];
                ordered[index] = ordered[swapIndex];
                ordered[swapIndex] = swap;
            }

            return ordered.AsReadOnly();
        }

        private static IReadOnlyList<LaborEquipmentPackagePlan> BuildDeterministicPlans(
            LaborProspect prospect,
            SettlementEconomicProfile sourceSettlement,
            LaborEquipmentLevel promisedTier,
            CombatClause clause,
            int marketIdentity,
            ISet<ThingDef> compatibleApparelDefinitions,
            BodyDef body,
            int retryAttempt,
            TechLevel itemTechCeiling,
            int seed)
        {
            IReadOnlyList<LaborEquipmentCatalogueEntry> apparelPool =
                FilterApparelPool(
                    LaborEquipmentCatalogue.EntriesFor(
                        LaborEquipmentCatalogueRole.Apparel, itemTechCeiling),
                    compatibleApparelDefinitions);
            IReadOnlyList<LaborEquipmentCatalogueEntry> weaponPool =
                clause == CombatClause.Civilian
                    ? EmptyCatalogueEntries
                    : LaborEquipmentCatalogue.EntriesFor(
                        LaborEquipmentCatalogueRole.PrimaryWeapon, itemTechCeiling);

            List<LaborEquipmentPackagePlan> plans =
                new List<LaborEquipmentPackagePlan>();
            for (int qualityIndex = 0;
                 qualityIndex < QualityOrder.Length;
                 qualityIndex++)
            {
                QualityCategory quality = QualityOrder[qualityIndex];
                List<LaborEquipmentCatalogueEntry> apparelChoices =
                    BuildQualityChoices(apparelPool, quality);
                List<List<LaborEquipmentCatalogueEntry>> apparelPlans =
                    BuildApparelPlans(apparelChoices, body);

                if (clause == CombatClause.Civilian)
                {
                    for (int planIndex = 0;
                         planIndex < apparelPlans.Count;
                         planIndex++)
                    {
                        plans.Add(CreatePlan(
                            prospect,
                            sourceSettlement,
                            promisedTier,
                            clause,
                            marketIdentity,
                            seed,
                            retryAttempt,
                            itemTechCeiling,
                            weapon: null,
                            apparelPlans[planIndex]));
                    }

                    continue;
                }

                List<LaborEquipmentCatalogueEntry> weaponChoices =
                    BuildQualityChoices(weaponPool, quality);

                // Match the old allocator's Standard path: an apparel-only package gets tried
                // before the weapon x apparel walk, because Classify accepts either incomplete
                // shape only at Standard.
                if (promisedTier == LaborEquipmentLevel.Standard)
                {
                    for (int planIndex = 0;
                         planIndex < apparelPlans.Count;
                         planIndex++)
                    {
                        plans.Add(CreatePlan(
                            prospect,
                            sourceSettlement,
                            promisedTier,
                            clause,
                            marketIdentity,
                            seed,
                            retryAttempt,
                            itemTechCeiling,
                            weapon: null,
                            apparelPlans[planIndex]));
                    }
                }

                if (weaponChoices.Count == 0)
                {
                    continue;
                }

                if (promisedTier == LaborEquipmentLevel.Standard)
                {
                    // This is the old allocator's weapon-only Standard candidate. It is not
                    // valid for Professional or Elite, whose package composition remains strict.
                    apparelPlans.Insert(0, new List<LaborEquipmentCatalogueEntry>());
                }

                if (apparelPlans.Count == 0)
                {
                    continue;
                }

                // Preserve the old deterministic combination walk. A strong weapon is not
                // enough by itself: each weapon is paired with every conflict-aware apparel plan
                // in the old plan order before the next weapon is considered.
                for (int weaponIndex = 0;
                     weaponIndex < weaponChoices.Count;
                     weaponIndex++)
                {
                    for (int planIndex = 0;
                         planIndex < apparelPlans.Count;
                         planIndex++)
                    {
                        plans.Add(CreatePlan(
                            prospect,
                            sourceSettlement,
                            promisedTier,
                            clause,
                            marketIdentity,
                            seed,
                            retryAttempt,
                            itemTechCeiling,
                            weaponChoices[weaponIndex],
                            apparelPlans[planIndex]));
                    }
                }
            }

            return plans.AsReadOnly();
        }

        private static LaborEquipmentPackagePlan CreatePlan(
            LaborProspect prospect,
            SettlementEconomicProfile sourceSettlement,
            LaborEquipmentLevel promisedTier,
            CombatClause clause,
            int marketIdentity,
            int seed,
            int retryAttempt,
            TechLevel itemTechCeiling,
            LaborEquipmentCatalogueEntry weapon,
            IReadOnlyList<LaborEquipmentCatalogueEntry> apparel)
        {
            List<LaborEquipmentPackageItemPlan> apparelPlans =
                new List<LaborEquipmentPackageItemPlan>(apparel.Count);
            for (int index = 0; index < apparel.Count; index++)
            {
                apparelPlans.Add(new LaborEquipmentPackageItemPlan(apparel[index]));
            }

            return new LaborEquipmentPackagePlan(
                prospect,
                sourceSettlement,
                promisedTier,
                clause,
                marketIdentity,
                seed,
                retryAttempt,
                itemTechCeiling,
                weapon == null ? null : new LaborEquipmentPackageItemPlan(weapon),
                apparelPlans.Count == 0
                    ? EmptyApparel
                    : apparelPlans.AsReadOnly());
        }

        /// <summary>
        /// Convenience overload using the source profile's stable market seed as the market-side
        /// identity. Callers that have a persisted posting/order identity should use the overload
        /// with an explicit <paramref name="marketIdentity"/>.
        /// </summary>
        public static LaborEquipmentPackagePlan Plan(
            LaborProspect prospect,
            SettlementEconomicProfile sourceSettlement,
            LaborEquipmentLevel promisedTier,
            CombatClause clause)
        {
            return Plan(
                prospect,
                sourceSettlement,
                promisedTier,
                clause,
                sourceSettlement == null ? 0 : sourceSettlement.seed);
        }

        /// <summary>Builds a plan at an explicit bounded retry stage without a Pawn filter.</summary>
        public static LaborEquipmentPackagePlan Plan(
            LaborProspect prospect,
            SettlementEconomicProfile sourceSettlement,
            LaborEquipmentLevel promisedTier,
            CombatClause clause,
            int marketIdentity,
            int retryAttempt)
        {
            return Plan(
                prospect,
                sourceSettlement,
                promisedTier,
                clause,
                marketIdentity,
                compatibleApparelDefinitions: null,
                retryAttempt: retryAttempt);
        }

        /// <summary>Try-shaped wrapper for callers that prefer an explicit failure result.</summary>
        public static bool TryPlan(
            LaborProspect prospect,
            SettlementEconomicProfile sourceSettlement,
            LaborEquipmentLevel promisedTier,
            CombatClause clause,
            int marketIdentity,
            out LaborEquipmentPackagePlan plan)
        {
            return TryPlan(
                prospect,
                sourceSettlement,
                promisedTier,
                clause,
                marketIdentity,
                null,
                out plan);
        }

        /// <summary>Try-shaped wrapper using a precomputed apparel definition filter.</summary>
        public static bool TryPlan(
            LaborProspect prospect,
            SettlementEconomicProfile sourceSettlement,
            LaborEquipmentLevel promisedTier,
            CombatClause clause,
            int marketIdentity,
            ISet<ThingDef> compatibleApparelDefinitions,
            out LaborEquipmentPackagePlan plan)
        {
            return TryPlan(
                prospect,
                sourceSettlement,
                promisedTier,
                clause,
                marketIdentity,
                compatibleApparelDefinitions,
                body: null,
                retryAttempt: 0,
                out plan);
        }

        /// <summary>Try-shaped wrapper using a precomputed filter and retry stage.</summary>
        public static bool TryPlan(
            LaborProspect prospect,
            SettlementEconomicProfile sourceSettlement,
            LaborEquipmentLevel promisedTier,
            CombatClause clause,
            int marketIdentity,
            ISet<ThingDef> compatibleApparelDefinitions,
            int retryAttempt,
            out LaborEquipmentPackagePlan plan)
        {
            return TryPlan(
                prospect,
                sourceSettlement,
                promisedTier,
                clause,
                marketIdentity,
                compatibleApparelDefinitions,
                body: null,
                retryAttempt: retryAttempt,
                out plan);
        }

        /// <summary>Try-shaped wrapper using both precomputed apparel filters.</summary>
        public static bool TryPlan(
            LaborProspect prospect,
            SettlementEconomicProfile sourceSettlement,
            LaborEquipmentLevel promisedTier,
            CombatClause clause,
            int marketIdentity,
            ISet<ThingDef> compatibleApparelDefinitions,
            BodyDef body,
            int retryAttempt,
            out LaborEquipmentPackagePlan plan)
        {
            plan = Plan(
                prospect,
                sourceSettlement,
                promisedTier,
                clause,
                marketIdentity,
                compatibleApparelDefinitions,
                body,
                retryAttempt);
            return plan != null;
        }

        /// <summary>Try-shaped wrapper using an explicit retry stage without a Pawn filter.</summary>
        public static bool TryPlan(
            LaborProspect prospect,
            SettlementEconomicProfile sourceSettlement,
            LaborEquipmentLevel promisedTier,
            CombatClause clause,
            int marketIdentity,
            int retryAttempt,
            out LaborEquipmentPackagePlan plan)
        {
            return TryPlan(
                prospect,
                sourceSettlement,
                promisedTier,
                clause,
                marketIdentity,
                null,
                retryAttempt,
                out plan);
        }

        /// <summary>
        /// Stable prospect identity used by the planner seed. It hashes only the census record's
        /// stable skills/passions and its derived price, never a Pawn reference or frame state.
        /// </summary>
        public static int StableProspectIdentity(LaborProspect prospect)
        {
            if (prospect == null)
            {
                return 0;
            }

            int identity = Gen.HashCombineInt(PlannerSeedSalt, 0x50524F53);
            int[] skillLevels = prospect.skillLevels;
            Passion[] passions = prospect.passions;
            int skillCount = skillLevels == null ? 0 : skillLevels.Length;
            identity = Gen.HashCombineInt(identity, skillCount);
            for (int index = 0; index < skillCount; index++)
            {
                identity = Gen.HashCombineInt(identity, index);
                identity = Gen.HashCombineInt(identity, skillLevels[index]);
                int passion = passions != null && index < passions.Length
                    ? (int)passions[index]
                    : (int)Passion.None;
                identity = Gen.HashCombineInt(identity, passion);
            }

            // This is derived from the same immutable census facts but adds useful separation for
            // records whose skill arrays happen to tie on a short fixture.
            identity = Gen.HashCombineInt(
                identity,
                Mathf.RoundToInt(prospect.pricedSkillValue * 1000f));
            return identity;
        }

        private static int SeedFor(
            LaborProspect prospect,
            SettlementEconomicProfile sourceSettlement,
            LaborEquipmentLevel promisedTier,
            CombatClause clause,
            int marketIdentity)
        {
            // Exact seed construction:
            // H(H(H(H(H(H(stableProspectIdentity, sourceSettlementId), sourceMarketSeed),
            // marketIdentity), promisedTier), clause), plannerSalt)
            int seed = StableProspectIdentity(prospect);
            seed = Gen.HashCombineInt(seed, prospect.settlementId);
            seed = Gen.HashCombineInt(seed, sourceSettlement.seed);
            seed = Gen.HashCombineInt(seed, marketIdentity);
            seed = Gen.HashCombineInt(seed, (int)promisedTier);
            seed = Gen.HashCombineInt(seed, (int)clause);
            return Gen.HashCombineInt(seed, PlannerSeedSalt);
        }

        private static bool IsPackageTier(LaborEquipmentLevel tier)
        {
            return tier == LaborEquipmentLevel.None ||
                   tier == LaborEquipmentLevel.Standard ||
                   tier == LaborEquipmentLevel.Professional ||
                   tier == LaborEquipmentLevel.Elite;
        }

        private static TechLevel GetItemTechCeiling(
            SettlementEconomicProfile sourceSettlement,
            LaborEquipmentLevel promisedTier)
        {
            TechLevel sourceTech = sourceSettlement.techTier == TechLevel.Undefined
                ? TechLevel.Industrial
                : sourceSettlement.techTier;
            TechLevel promisedTechCeiling;
            switch (promisedTier)
            {
                case LaborEquipmentLevel.Standard:
                    promisedTechCeiling = sourceTech;
                    break;
                case LaborEquipmentLevel.Professional:
                    promisedTechCeiling = TechLevel.Industrial;
                    break;
                case LaborEquipmentLevel.Elite:
                    // Elite is intentionally not Spacer-gated. CanSupply decides whether the
                    // source may promise it; this is only the item-pool ceiling after that gate.
                    promisedTechCeiling = TechLevel.Spacer;
                    break;
                default:
                    promisedTechCeiling = sourceTech;
                    break;
            }

            return (int)sourceTech >= (int)promisedTechCeiling
                ? sourceTech
                : promisedTechCeiling;
        }

        private static IReadOnlyList<LaborEquipmentCatalogueEntry> FilterApparelPool(
            IReadOnlyList<LaborEquipmentCatalogueEntry> cataloguePool,
            ISet<ThingDef> compatibleApparelDefinitions)
        {
            if (compatibleApparelDefinitions == null)
            {
                return cataloguePool;
            }

            List<LaborEquipmentCatalogueEntry> filtered =
                new List<LaborEquipmentCatalogueEntry>();
            for (int index = 0; index < cataloguePool.Count; index++)
            {
                LaborEquipmentCatalogueEntry entry = cataloguePool[index];
                if (entry?.Def != null && compatibleApparelDefinitions.Contains(entry.Def))
                {
                    filtered.Add(entry);
                }
            }

            return filtered.AsReadOnly();
        }

        private static List<LaborEquipmentCatalogueEntry> BuildQualityChoices(
            IReadOnlyList<LaborEquipmentCatalogueEntry> pool,
            QualityCategory quality)
        {
            List<LaborEquipmentCatalogueEntry> choices =
                new List<LaborEquipmentCatalogueEntry>();
            if (pool == null)
            {
                return choices;
            }

            for (int index = 0; index < pool.Count; index++)
            {
                LaborEquipmentCatalogueEntry entry = pool[index];
                if (entry == null)
                {
                    continue;
                }

                if ((entry.HasQuality && entry.Quality != quality) ||
                    (!entry.HasQuality && quality != QualityCategory.Normal))
                {
                    continue;
                }

                choices.Add(entry);
            }

            choices.Sort(CompareChoices);
            return choices;
        }

        private static List<List<LaborEquipmentCatalogueEntry>> BuildApparelPlans(
            List<LaborEquipmentCatalogueEntry> choices,
            BodyDef body)
        {
            List<List<LaborEquipmentCatalogueEntry>> plans =
                new List<List<LaborEquipmentCatalogueEntry>>();
            HashSet<string> planKeys = new HashSet<string>(StringComparer.Ordinal);

            AddApparelPlan(
                plans,
                planKeys,
                BuildGreedyApparelPlan(
                    choices, forward: true, replaceConflicts: false, anchorIndex: -1,
                    body: body));
            AddApparelPlan(
                plans,
                planKeys,
                BuildGreedyApparelPlan(
                    choices, forward: true, replaceConflicts: true, anchorIndex: -1,
                    body: body));
            AddApparelPlan(
                plans,
                planKeys,
                BuildGreedyApparelPlan(
                    choices, forward: false, replaceConflicts: false, anchorIndex: -1,
                    body: body));

            List<int> anchorIndices = new List<int>();
            int halfAnchorCount = MaxAlternativeApparelAnchors / 2;
            for (int index = 0;
                 index < halfAnchorCount && index < choices.Count;
                 index++)
            {
                if (!anchorIndices.Contains(index))
                {
                    anchorIndices.Add(index);
                }
            }

            int highAnchorStart = Math.Max(
                halfAnchorCount, choices.Count - halfAnchorCount);
            for (int index = highAnchorStart; index < choices.Count; index++)
            {
                if (!anchorIndices.Contains(index))
                {
                    anchorIndices.Add(index);
                }
            }

            for (int anchorIndex = 0;
                 anchorIndex < anchorIndices.Count;
                 anchorIndex++)
            {
                int selectedAnchor = anchorIndices[anchorIndex];
                AddApparelPlan(
                    plans,
                    planKeys,
                    BuildGreedyApparelPlan(
                        choices, forward: true, replaceConflicts: false,
                        anchorIndex: selectedAnchor, body: body));
                AddApparelPlan(
                    plans,
                    planKeys,
                    BuildGreedyApparelPlan(
                        choices, forward: true, replaceConflicts: true,
                        anchorIndex: selectedAnchor, body: body));
                AddApparelPlan(
                    plans,
                    planKeys,
                    BuildGreedyApparelPlan(
                        choices, forward: false, replaceConflicts: false,
                        anchorIndex: selectedAnchor, body: body));
                AddApparelPlan(
                    plans,
                    planKeys,
                    BuildGreedyApparelPlan(
                        choices, forward: false, replaceConflicts: true,
                        anchorIndex: selectedAnchor, body: body));
            }

            plans.Sort(CompareApparelPlans);
            return plans;
        }

        private static List<LaborEquipmentCatalogueEntry> BuildGreedyApparelPlan(
            List<LaborEquipmentCatalogueEntry> choices,
            bool forward,
            bool replaceConflicts,
            int anchorIndex,
            BodyDef body)
        {
            List<LaborEquipmentCatalogueEntry> selected =
                new List<LaborEquipmentCatalogueEntry>();
            if (anchorIndex >= 0 && anchorIndex < choices.Count)
            {
                selected.Add(choices[anchorIndex]);
            }

            int step = forward ? 1 : -1;
            int index = forward ? 0 : choices.Count - 1;
            while (index >= 0 && index < choices.Count)
            {
                LaborEquipmentCatalogueEntry candidate = choices[index];
                index += step;
                if (anchorIndex >= 0 && anchorIndex < choices.Count &&
                    candidate == choices[anchorIndex])
                {
                    continue;
                }

                // The old walk normally rejected a duplicate through the pairwise slot check.
                // Keep the restored explicit definition-level rule even when a modded apparel
                // definition exposes an unusual layer/body-part combination.
                if (candidate?.Def == null || ContainsDefinition(selected, candidate.Def))
                {
                    continue;
                }

                List<LaborEquipmentCatalogueEntry> conflicts =
                    FindConflicts(selected, candidate, body);
                if (conflicts.Count == 0)
                {
                    if (selected.Count < MaxApparelItems &&
                        !WouldLowerSaturatedApparelScore(candidate, selected))
                    {
                        selected.Add(candidate);
                    }

                    continue;
                }

                if (!replaceConflicts || !MoreExcessiveThan(candidate, conflicts))
                {
                    continue;
                }

                List<LaborEquipmentCatalogueEntry> remaining =
                    new List<LaborEquipmentCatalogueEntry>(selected);
                for (int conflictIndex = remaining.Count - 1;
                     conflictIndex >= 0;
                     conflictIndex--)
                {
                    if (conflicts.Contains(remaining[conflictIndex]))
                    {
                        remaining.RemoveAt(conflictIndex);
                    }
                }

                if (remaining.Count < MaxApparelItems &&
                    !WouldLowerSaturatedApparelScore(candidate, remaining))
                {
                    selected.Clear();
                    selected.AddRange(remaining);
                    selected.Add(candidate);
                }
            }

            return selected;
        }

        private static List<LaborEquipmentCatalogueEntry> FindConflicts(
            List<LaborEquipmentCatalogueEntry> selected,
            LaborEquipmentCatalogueEntry candidate,
            BodyDef body)
        {
            List<LaborEquipmentCatalogueEntry> conflicts =
                new List<LaborEquipmentCatalogueEntry>();
            for (int index = 0; index < selected.Count; index++)
            {
                if (!CanWearTogether(
                        candidate?.Def, selected[index]?.Def, body))
                {
                    conflicts.Add(selected[index]);
                }
            }

            return conflicts;
        }

        private static bool MoreExcessiveThan(
            LaborEquipmentCatalogueEntry candidate,
            List<LaborEquipmentCatalogueEntry> conflicts)
        {
            for (int index = 0; index < conflicts.Count; index++)
            {
                if (CompareChoices(candidate, conflicts[index]) <= 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static void AddApparelPlan(
            List<List<LaborEquipmentCatalogueEntry>> plans,
            HashSet<string> planKeys,
            List<LaborEquipmentCatalogueEntry> plan)
        {
            if (plan == null || plan.Count == 0)
            {
                return;
            }

            string planKey = PlanKeyForEntries(plan);
            if (planKeys.Add(planKey))
            {
                plans.Add(plan);
            }
        }

        private static int CompareApparelPlans(
            List<LaborEquipmentCatalogueEntry> left,
            List<LaborEquipmentCatalogueEntry> right)
        {
            int comparison = CompareFloat(
                ApparelPlanScore(left), ApparelPlanScore(right));
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.Count.CompareTo(right.Count);
            if (comparison != 0)
            {
                return comparison;
            }

            float leftValue = 0f;
            for (int index = 0; index < left.Count; index++)
            {
                leftValue += CandidateValue(left[index]);
            }

            float rightValue = 0f;
            for (int index = 0; index < right.Count; index++)
            {
                rightValue += CandidateValue(right[index]);
            }

            comparison = CompareFloat(leftValue, rightValue);
            if (comparison != 0)
            {
                return comparison;
            }

            return StringComparer.Ordinal.Compare(
                PlanKeyForEntries(left), PlanKeyForEntries(right));
        }

        private static string PlanKeyForEntries(
            List<LaborEquipmentCatalogueEntry> choices)
        {
            List<string> keys = new List<string>();
            for (int index = 0; index < choices.Count; index++)
            {
                keys.Add(ChoiceKey(choices[index]));
            }

            keys.Sort(StringComparer.Ordinal);
            return String.Join("|", keys.ToArray());
        }

        private static float ApparelPlanScore(
            List<LaborEquipmentCatalogueEntry> plan)
        {
            if (plan == null || plan.Count == 0)
            {
                return 0f;
            }

            float totalCoverage = 0f;
            float totalWeight = 0f;
            float weightedScore = 0f;
            for (int index = 0; index < plan.Count; index++)
            {
                LaborEquipmentCatalogueEntry choice = plan[index];
                float coverage = ApparelCoverage(choice);
                float weight = Math.Max(0.25f, coverage);
                totalCoverage += coverage;
                totalWeight += weight;
                weightedScore += ApparelChoiceScore(choice) * weight;
            }

            if (totalWeight <= 0f)
            {
                return 0f;
            }

            float average = weightedScore / totalWeight;
            float coverageFactor = 0.35f + Mathf.Clamp01(totalCoverage) * 0.65f;
            return Mathf.Clamp01(average * coverageFactor);
        }

        private static float ApparelChoiceScore(
            LaborEquipmentCatalogueEntry choice)
        {
            if (choice?.Def == null)
            {
                return 0f;
            }

            float technology = SelectionTechnologyScore(choice.CandidateTechLevel);
            float quality = SelectionQualityScore(choice);
            float protection = ApparelProtectionScore(choice);
            float marketValue = Mathf.Clamp01(Mathf.InverseLerp(
                25f, 2500f, choice.MarketValue));
            return Mathf.Clamp01(
                technology * 0.40f + quality * 0.25f + protection * 0.30f +
                marketValue * 0.05f);
        }

        private static float ApparelCoverage(
            LaborEquipmentCatalogueEntry choice)
        {
            return choice?.Def?.apparel == null
                ? 0f
                : Mathf.Clamp01(choice.StatInputs.Coverage);
        }

        private static bool WouldLowerSaturatedApparelScore(
            LaborEquipmentCatalogueEntry candidate,
            List<LaborEquipmentCatalogueEntry> selected)
        {
            if (candidate == null || selected == null || selected.Count == 0)
            {
                return false;
            }

            float totalCoverage = 0f;
            float totalWeight = 0f;
            float weightedScore = 0f;
            for (int index = 0; index < selected.Count; index++)
            {
                LaborEquipmentCatalogueEntry choice = selected[index];
                float coverage = ApparelCoverage(choice);
                float weight = Math.Max(0.25f, coverage);
                totalCoverage += coverage;
                totalWeight += weight;
                weightedScore += ApparelChoiceScore(choice) * weight;
            }

            if (totalCoverage < 1f || totalWeight <= 0f)
            {
                return false;
            }

            float currentAverage = weightedScore / totalWeight;
            return ApparelChoiceScore(candidate) < currentAverage;
        }

        private static float ApparelProtectionScore(
            LaborEquipmentCatalogueEntry choice)
        {
            if (choice?.Def == null)
            {
                return 0f;
            }

            float sharp = Math.Max(0f, choice.StatInputs.ArmorSharp);
            float blunt = Math.Max(0f, choice.StatInputs.ArmorBlunt);
            float heat = Math.Max(0f, choice.StatInputs.ArmorHeat);
            float weightedArmor = sharp * 0.45f + blunt * 0.40f + heat * 0.15f;
            return Mathf.Clamp01(weightedArmor / 0.60f);
        }

        private static float SelectionQualityScore(
            LaborEquipmentCatalogueEntry choice)
        {
            QualityCategory quality = choice != null && choice.HasQuality
                ? choice.Quality
                : QualityCategory.Normal;
            return Mathf.InverseLerp(
                (float)QualityCategory.Awful,
                (float)QualityCategory.Legendary,
                (float)quality);
        }

        private static float SelectionTechnologyScore(TechLevel tech)
        {
            switch (tech)
            {
                case TechLevel.Animal:
                    return 0.05f;
                case TechLevel.Neolithic:
                    return 0.10f;
                case TechLevel.Medieval:
                    return 0.25f;
                case TechLevel.Industrial:
                    return 0.50f;
                case TechLevel.Spacer:
                    return 0.72f;
                case TechLevel.Ultra:
                    return 0.90f;
                case TechLevel.Archotech:
                    return 1f;
                default:
                    return 0.50f;
            }
        }

        private static int CompareChoices(
            LaborEquipmentCatalogueEntry left,
            LaborEquipmentCatalogueEntry right)
        {
            if (left == null)
            {
                return right == null ? 0 : -1;
            }

            if (right == null)
            {
                return 1;
            }

            int comparison = CompareTech(left, right);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.Quality.CompareTo(right.Quality);
            if (comparison != 0)
            {
                return comparison;
            }

            if (left.Def != null && left.Def.IsApparel &&
                right.Def != null && right.Def.IsApparel)
            {
                comparison = CompareFloat(
                    ApparelChoiceScore(left), ApparelChoiceScore(right));
                if (comparison != 0)
                {
                    return comparison;
                }

                comparison = CompareFloat(
                    ApparelCoverage(left), ApparelCoverage(right));
                if (comparison != 0)
                {
                    return comparison;
                }
            }

            comparison = CompareFloat(
                CandidateValue(left), CandidateValue(right));
            if (comparison != 0)
            {
                return comparison;
            }

            return StringComparer.Ordinal.Compare(ChoiceKey(left), ChoiceKey(right));
        }

        private static int CompareTech(
            LaborEquipmentCatalogueEntry left,
            LaborEquipmentCatalogueEntry right)
        {
            int leftTech = Math.Max(
                (int)left.CandidateTechLevel, (int)left.StuffTechLevel);
            int rightTech = Math.Max(
                (int)right.CandidateTechLevel, (int)right.StuffTechLevel);
            return leftTech.CompareTo(rightTech);
        }

        private static float CandidateValue(
            LaborEquipmentCatalogueEntry entry)
        {
            float value = entry?.Def == null ? 0f : entry.Def.BaseMarketValue;
            if (entry?.StuffDef != null)
            {
                value += entry.StuffDef.BaseMarketValue;
            }

            return IsFinite(value) ? value : 0f;
        }

        private static int CompareFloat(float left, float right)
        {
            float safeLeft = IsFinite(left) ? left : 0f;
            float safeRight = IsFinite(right) ? right : 0f;
            return safeLeft.CompareTo(safeRight);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static string ChoiceKey(LaborEquipmentCatalogueEntry choice)
        {
            return (choice?.Def?.defName ?? "") + ":" +
                   (choice?.StuffDef?.defName ?? "") + ":" +
                   ((int)(choice == null
                       ? QualityCategory.Normal
                       : choice.Quality)).ToString();
        }

        private static bool ContainsDefinition(
            List<LaborEquipmentCatalogueEntry> selected,
            ThingDef definition)
        {
            for (int index = 0; index < selected.Count; index++)
            {
                if (selected[index]?.Def == definition)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CanWearTogether(
            ThingDef left,
            ThingDef right,
            BodyDef body)
        {
            if (left?.apparel == null || right?.apparel == null)
            {
                return false;
            }

            return body != null
                ? ApparelUtility.CanWearTogether(left, right, body)
                : CanShareAbstractSlot(left, right);
        }

        private static int NormalizeRetryAttempt(int retryAttempt)
        {
            return Math.Max(0, Math.Min(3, retryAttempt));
        }

        /// <summary>
        /// Conservative slot compatibility for callers that do not provide a BodyDef. Production
        /// passes the applicant's BodyDef and uses the same pairwise check above; this fallback keeps
        /// the public Pawn-free planner safe for data-only callers. Different layers are safe;
        /// same-layer apparel is kept apart when it names a shared body-part group.
        /// </summary>
        private static bool CanShareAbstractSlot(ThingDef left, ThingDef right)
        {
            if (left?.apparel == null || right?.apparel == null)
            {
                return false;
            }

            bool sharedLayer = false;
            for (int leftIndex = 0; leftIndex < left.apparel.layers.Count; leftIndex++)
            {
                for (int rightIndex = 0; rightIndex < right.apparel.layers.Count; rightIndex++)
                {
                    if (left.apparel.layers[leftIndex] == right.apparel.layers[rightIndex])
                    {
                        sharedLayer = true;
                        break;
                    }
                }

                if (sharedLayer)
                {
                    break;
                }
            }

            if (!sharedLayer)
            {
                return true;
            }

            for (int leftIndex = 0; leftIndex < left.apparel.bodyPartGroups.Count; leftIndex++)
            {
                if (right.apparel.bodyPartGroups.Contains(left.apparel.bodyPartGroups[leftIndex]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
