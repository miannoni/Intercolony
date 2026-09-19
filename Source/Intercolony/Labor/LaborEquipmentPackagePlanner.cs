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
        /// The zero-based retry stage used for selection. Stage zero is the varied baseline draw;
        /// later stages are progressively more selective.
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
    /// passed the settlement capability gate; this class only uses that promised tier to choose
    /// the item pool and the overlapping package composition.
    /// </summary>
    public static class LaborEquipmentPackagePlanner
    {
        private const int PlannerSeedSalt = 0x504C_4E52;

        private const int MaxApparelItems = 5;
        private const int FinalRetryAttempt = 3;
        private const int MaxAlternativeApparelAnchors = 8;

        // A score still matters, but the floor makes a plausible lower-scoring item common enough
        // to prevent weighted sampling from becoming "always choose the best item".
        private const float MinimumScoreWeight = 0.35f;
        private const float ScoreWeightRange = 0.65f;

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
        /// Builds one plan using precomputed apparel filters and a bounded retry stage.
        /// Retry stage zero is the original varied draw. Positive stages only bias selection toward
        /// stronger, more complete packages; they never change the catalogue or tier authority.
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
            TechLevel itemTechCeiling = GetItemTechCeiling(
                sourceSettlement, promisedTier);
            int seed = SeedFor(
                prospect, sourceSettlement, promisedTier, clause, marketIdentity);

            if (promisedTier == LaborEquipmentLevel.None)
            {
                return new LaborEquipmentPackagePlan(
                    prospect,
                    sourceSettlement,
                    promisedTier,
                    clause,
                    marketIdentity,
                    seed,
                    retryAttempt,
                    itemTechCeiling,
                    weapon: null,
                    EmptyApparel);
            }

            if (retryAttempt == FinalRetryAttempt)
            {
                IReadOnlyList<LaborEquipmentPackagePlan> deterministicPlans =
                    BuildDeterministicPlans(
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
                return deterministicPlans.Count == 0 ? null : deterministicPlans[0];
            }

            // Catalogue reads are deterministic. All random work below is scoped so this
            // planner cannot shift the game's ambient random stream, including on exceptions.
            Rand.PushState(seed);
            try
            {
                IReadOnlyList<LaborEquipmentCatalogueEntry> apparelPool =
                    FilterApparelPool(
                        LaborEquipmentCatalogue.EntriesFor(
                            LaborEquipmentCatalogueRole.Apparel, itemTechCeiling),
                        compatibleApparelDefinitions);

                LaborEquipmentCatalogueEntry weapon = null;
                if (clause != CombatClause.Civilian)
                {
                    IReadOnlyList<LaborEquipmentCatalogueEntry> weaponPool =
                        LaborEquipmentCatalogue.EntriesFor(
                            LaborEquipmentCatalogueRole.PrimaryWeapon, itemTechCeiling);
                    weapon = ChooseWeapon(weaponPool, promisedTier, retryAttempt);
                }

                List<LaborEquipmentCatalogueEntry> apparel = ChooseApparel(
                    apparelPool, promisedTier, retryAttempt, weapon == null, body);
                if (!HasRequiredPackageComposition(
                        promisedTier, clause, weapon, apparel))
                {
                    return null;
                }

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
                    apparelPlans.AsReadOnly());
            }
            finally
            {
                Rand.PopState();
            }
        }

        /// <summary>
        /// Returns the old allocator's deterministic candidate sequence for the final retry.
        /// The allocator owns the Pawn and the authoritative Classify call; this method only
        /// returns data-only candidates in the same order that the old search tried them.
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

            return BuildDeterministicPlans(
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

        private static LaborEquipmentCatalogueEntry ChooseWeapon(
            IReadOnlyList<LaborEquipmentCatalogueEntry> weaponPool,
            LaborEquipmentLevel promisedTier,
            int retryAttempt)
        {
            if (weaponPool == null || weaponPool.Count == 0)
            {
                return null;
            }

            // Standard packages are allowed to be incomplete. Higher combat tiers normally get
            // a weapon, while the later authoritative retry remains free to reject the package.
            if (promisedTier == LaborEquipmentLevel.Standard &&
                retryAttempt == 0 && Rand.Value < 0.15f)
            {
                return null;
            }

            return PickWeightedEntry(
                weaponPool,
                promisedTier,
                null,
                null,
                null,
                retryAttempt,
                false);
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
                    if (selected.Count < MaxApparelItems)
                    {
                        selected.Add(candidate);
                    }

                    continue;
                }

                if (!replaceConflicts || !MoreExcessiveThan(candidate, conflicts))
                {
                    continue;
                }

                for (int conflictIndex = selected.Count - 1;
                     conflictIndex >= 0;
                     conflictIndex--)
                {
                    if (conflicts.Contains(selected[conflictIndex]))
                    {
                        selected.RemoveAt(conflictIndex);
                    }
                }

                if (selected.Count < MaxApparelItems)
                {
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

        private static List<LaborEquipmentCatalogueEntry> ChooseApparel(
            IReadOnlyList<LaborEquipmentCatalogueEntry> apparelPool,
            LaborEquipmentLevel promisedTier,
            int retryAttempt,
            bool weaponless,
            BodyDef body)
        {
            List<LaborEquipmentCatalogueEntry> selected =
                new List<LaborEquipmentCatalogueEntry>();
            if (apparelPool == null || apparelPool.Count == 0)
            {
                return selected;
            }

            int targetCount = ChooseApparelCount(
                apparelPool, promisedTier, retryAttempt, weaponless);
            List<ThingDef> selectedDefinitions = new List<ThingDef>();
            for (int index = 0; index < targetCount; index++)
            {
                LaborEquipmentCatalogueEntry choice = PickWeightedEntry(
                    apparelPool,
                    promisedTier,
                    selected,
                    selectedDefinitions,
                    body,
                    retryAttempt,
                    weaponless);
                if (choice == null)
                {
                    break;
                }

                selected.Add(choice);
                selectedDefinitions.Add(choice.Def);
            }

            return selected;
        }

        private static int ChooseApparelCount(
            IReadOnlyList<LaborEquipmentCatalogueEntry> apparelPool,
            LaborEquipmentLevel promisedTier,
            int retryAttempt,
            bool weaponless)
        {
            int availableDefinitions = CountDefinitions(apparelPool);
            int desired;
            float roll = Rand.Value;

            if (retryAttempt > 0)
            {
                if (weaponless)
                {
                    // A civilian or otherwise weaponless package has no weapon score to carry the
                    // promise. Later stages therefore ask for three-to-five pieces immediately,
                    // then converge on the catalogue's full compatible headroom.
                    desired = retryAttempt == 1
                        ? (roll < 0.05f ? 3 : roll < 0.35f ? 4 : MaxApparelItems)
                        : retryAttempt == 2
                            ? (roll < 0.02f ? 4 : MaxApparelItems)
                            : MaxApparelItems;
                }
                else
                {
                    // Combat packages still retain a little composition variety on the first
                    // escalation, then use the final stages as a completeness safety net.
                    desired = retryAttempt == 1
                        ? (roll < 0.08f ? 2
                            : roll < 0.32f ? 3
                            : roll < 0.80f ? 4
                            : MaxApparelItems)
                        : retryAttempt == 2
                            ? (roll < 0.04f ? 3
                                : roll < 0.22f ? 4
                                : MaxApparelItems)
                            : MaxApparelItems;
                }

                return Math.Min(desired, Math.Min(MaxApparelItems, availableDefinitions));
            }

            // Retry stage zero intentionally remains the original distribution. It is the common
            // path and is what keeps ordinary packages varied instead of uniform.
            switch (promisedTier)
            {
                case LaborEquipmentLevel.Standard:
                    // A Standard worker is deliberately not a full uniform.
                    desired = roll < 0.18f ? 1 : roll < 0.72f ? 2 : 3;
                    break;
                case LaborEquipmentLevel.Professional:
                    desired = roll < 0.10f ? 1
                        : roll < 0.38f ? 2
                        : roll < 0.84f ? 3
                        : 4;
                    break;
                case LaborEquipmentLevel.Elite:
                    desired = roll < 0.08f ? 1
                        : roll < 0.28f ? 2
                        : roll < 0.76f ? 3
                        : roll < 0.95f ? 4
                        : MaxApparelItems;
                    break;
                default:
                    desired = 0;
                    break;
            }

            return Math.Min(desired, Math.Min(MaxApparelItems, availableDefinitions));
        }

        private static LaborEquipmentCatalogueEntry PickWeightedEntry(
            IReadOnlyList<LaborEquipmentCatalogueEntry> pool,
            LaborEquipmentLevel promisedTier,
            List<LaborEquipmentCatalogueEntry> selectedApparel,
            List<ThingDef> selectedDefinitions,
            BodyDef body,
            int retryAttempt,
            bool weaponless)
        {
            float totalWeight = 0f;
            for (int index = 0; index < pool.Count; index++)
            {
                LaborEquipmentCatalogueEntry entry = pool[index];
                if (!CanSelect(
                        entry, promisedTier, selectedApparel, selectedDefinitions, body))
                {
                    continue;
                }

                totalWeight += WeightFor(
                    entry, promisedTier, selectedApparel, retryAttempt, weaponless);
            }

            if (totalWeight <= 0f)
            {
                return null;
            }

            float roll = Rand.Value * totalWeight;
            float cumulative = 0f;
            LaborEquipmentCatalogueEntry lastPositive = null;
            for (int index = 0; index < pool.Count; index++)
            {
                LaborEquipmentCatalogueEntry entry = pool[index];
                if (!CanSelect(
                        entry, promisedTier, selectedApparel, selectedDefinitions, body))
                {
                    continue;
                }

                float weight = WeightFor(
                    entry, promisedTier, selectedApparel, retryAttempt, weaponless);
                if (weight <= 0f)
                {
                    continue;
                }

                lastPositive = entry;
                cumulative += weight;
                if (roll < cumulative)
                {
                    return entry;
                }
            }

            // Keep the choice total safe at the upper floating-point boundary.
            return lastPositive;
        }

        private static bool CanSelect(
            LaborEquipmentCatalogueEntry entry,
            LaborEquipmentLevel promisedTier,
            List<LaborEquipmentCatalogueEntry> selectedApparel,
            List<ThingDef> selectedDefinitions,
            BodyDef body)
        {
            if (entry == null || entry.Def == null ||
                !BandAllowed(promisedTier, entry.Band))
            {
                return false;
            }

            if (selectedDefinitions == null)
            {
                return true;
            }

            if (selectedDefinitions.Contains(entry.Def))
            {
                return false;
            }

            if (entry.Def.apparel == null)
            {
                return false;
            }

            return CanWearTogetherAsSet(entry.Def, selectedApparel, body);
        }

        private static bool HasRequiredPackageComposition(
            LaborEquipmentLevel promisedTier,
            CombatClause clause,
            LaborEquipmentCatalogueEntry weapon,
            List<LaborEquipmentCatalogueEntry> apparel)
        {
            int apparelCount = apparel == null ? 0 : apparel.Count;
            if (clause == CombatClause.Civilian)
            {
                return weapon == null && apparelCount > 0;
            }

            if (promisedTier == LaborEquipmentLevel.Standard)
            {
                // The old allocator allowed a Standard weapon-only package or a Standard
                // apparel-only package, but never an entirely empty package.
                return weapon != null || apparelCount > 0;
            }

            // Professional and Elite were only assembled when both candidate pools contributed
            // an item. Classify remains authoritative for the actual tier floor.
            return weapon != null && apparelCount > 0;
        }

        private static bool CanWearTogetherAsSet(
            ThingDef candidate,
            List<LaborEquipmentCatalogueEntry> selectedApparel,
            BodyDef body)
        {
            if (candidate?.apparel == null || selectedApparel == null)
            {
                return false;
            }

            for (int index = 0; index < selectedApparel.Count; index++)
            {
                LaborEquipmentCatalogueEntry selected = selectedApparel[index];
                if (selected?.Def?.apparel == null)
                {
                    return false;
                }

                if (body != null)
                {
                    // The decompiled RimWorld primitive is pairwise; the old allocator's
                    // CanWearTogetherAsSet helper applied it to every pair in the set.
                    if (!ApparelUtility.CanWearTogether(candidate, selected.Def, body))
                    {
                        return false;
                    }
                }
                else if (!CanShareAbstractSlot(candidate, selected.Def))
                {
                    return false;
                }
            }

            return true;
        }

        private static float WeightFor(
            LaborEquipmentCatalogueEntry entry,
            LaborEquipmentLevel promisedTier,
            List<LaborEquipmentCatalogueEntry> selectedApparel,
            int retryAttempt,
            bool weaponless)
        {
            float bandWeight = BandWeight(
                promisedTier,
                entry.Band,
                selectedApparel);
            if (bandWeight <= 0f)
            {
                return 0f;
            }

            // LaborEquipmentItemScore.Score already includes the generic special-purpose
            // penalty. In particular, disposable/explosive launchers stay in the population but
            // do not receive a second identity-specific exception here.
            float score = Mathf.Clamp01(entry.Score);
            float scoreWeight = MinimumScoreWeight + ScoreWeightRange * score;
            if (retryAttempt <= 0)
            {
                return bandWeight * scoreWeight;
            }

            return bandWeight * scoreWeight *
                   BandEscalationMultiplier(entry.Band, retryAttempt, weaponless) *
                   ScoreEscalationMultiplier(score, retryAttempt, weaponless);
        }

        private static float BandEscalationMultiplier(
            LaborEquipmentItemBand band, int retryAttempt, bool weaponless)
        {
            float boost;
            switch (retryAttempt)
            {
                case 1:
                    boost = 0.25f;
                    break;
                case 2:
                    boost = 0.75f;
                    break;
                default:
                    boost = 2.00f;
                    break;
            }

            if (weaponless)
            {
                boost += retryAttempt == 1 ? 0.40f
                    : retryAttempt == 2 ? 1.00f
                    : 2.25f;
            }

            return 1f + (int)band * boost;
        }

        private static float ScoreEscalationMultiplier(
            float score, int retryAttempt, bool weaponless)
        {
            int exponent;
            float floor;
            switch (retryAttempt)
            {
                case 1:
                    exponent = 2;
                    floor = 0.45f;
                    break;
                case 2:
                    exponent = 4;
                    floor = 0.18f;
                    break;
                default:
                    exponent = 6;
                    floor = 0.04f;
                    break;
            }

            if (weaponless)
            {
                exponent++;
                floor = Math.Max(0.02f, floor - 0.08f);
            }

            float poweredScore = score;
            for (int power = 1; power < exponent; power++)
            {
                poweredScore *= score;
            }

            return floor + (1f - floor) * poweredScore;
        }

        private static int NormalizeRetryAttempt(int retryAttempt)
        {
            return Math.Max(0, Math.Min(3, retryAttempt));
        }

        private static float BandWeight(
            LaborEquipmentLevel promisedTier,
            LaborEquipmentItemBand band,
            List<LaborEquipmentCatalogueEntry> selectedApparel)
        {
            int basicCount = CountBand(selectedApparel, LaborEquipmentItemBand.Basic);
            int professionalCount = CountBand(
                selectedApparel, LaborEquipmentItemBand.Professional);
            int eliteCount = CountBand(selectedApparel, LaborEquipmentItemBand.Elite);

            switch (promisedTier)
            {
                case LaborEquipmentLevel.Standard:
                    if (band == LaborEquipmentItemBand.Basic)
                    {
                        return 1f;
                    }

                    // At most one Professional-ish item is an occasional Standard exception.
                    return band == LaborEquipmentItemBand.Professional && professionalCount == 0
                        ? 0.16f
                        : 0f;

                case LaborEquipmentLevel.Professional:
                    if (band == LaborEquipmentItemBand.Basic)
                    {
                        return 0.48f + (professionalCount > 0 ? 0.22f : 0f);
                    }

                    if (band == LaborEquipmentItemBand.Professional)
                    {
                        return 0.92f + (basicCount > 0 ? 0.24f : 0f);
                    }

                    // At most one Elite-ish item is an occasional Professional exception.
                    return eliteCount == 0 ? 0.10f : 0f;

                case LaborEquipmentLevel.Elite:
                    if (band == LaborEquipmentItemBand.Basic)
                    {
                        // A single weaker mundane piece can be filler, not the package identity.
                        return basicCount == 0 ? 0.05f : 0f;
                    }

                    if (band == LaborEquipmentItemBand.Professional)
                    {
                        return 0.72f + (eliteCount > 0 ? 0.18f : 0f);
                    }

                    return 0.96f + (professionalCount > 0 ? 0.22f : 0f);

                default:
                    return 0f;
            }
        }

        private static bool BandAllowed(
            LaborEquipmentLevel promisedTier, LaborEquipmentItemBand band)
        {
            switch (promisedTier)
            {
                case LaborEquipmentLevel.Standard:
                    return band == LaborEquipmentItemBand.Basic ||
                           band == LaborEquipmentItemBand.Professional;
                case LaborEquipmentLevel.Professional:
                case LaborEquipmentLevel.Elite:
                    return band == LaborEquipmentItemBand.Basic ||
                           band == LaborEquipmentItemBand.Professional ||
                           band == LaborEquipmentItemBand.Elite;
                default:
                    return false;
            }
        }

        private static int CountBand(
            List<LaborEquipmentCatalogueEntry> selected,
            LaborEquipmentItemBand band)
        {
            if (selected == null)
            {
                return 0;
            }

            int count = 0;
            for (int index = 0; index < selected.Count; index++)
            {
                if (selected[index] != null && selected[index].Band == band)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountDefinitions(
            IReadOnlyList<LaborEquipmentCatalogueEntry> entries)
        {
            List<ThingDef> definitions = new List<ThingDef>();
            for (int index = 0; index < entries.Count; index++)
            {
                ThingDef def = entries[index]?.Def;
                if (def != null && !definitions.Contains(def))
                {
                    definitions.Add(def);
                }
            }

            return definitions.Count;
        }

        /// <summary>
        /// Conservative slot compatibility for callers that do not provide a BodyDef. Production
        /// passes the applicant's BodyDef and uses CanWearTogetherAsSet above; this fallback keeps
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
