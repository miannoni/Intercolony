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

        // A score still matters, but the floor makes a plausible lower-scoring item common enough
        // to prevent weighted sampling from becoming "always choose the best item".
        private const float MinimumScoreWeight = 0.35f;
        private const float ScoreWeightRange = 0.65f;

        private static readonly IReadOnlyList<LaborEquipmentPackageItemPlan> EmptyApparel =
            new List<LaborEquipmentPackageItemPlan>().AsReadOnly();

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
            if (prospect == null || sourceSettlement == null || !IsPackageTier(promisedTier))
            {
                return null;
            }

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
                    itemTechCeiling,
                    weapon: null,
                    EmptyApparel);
            }

            // Catalogue reads are deterministic. All random work below is scoped so this
            // planner cannot shift the game's ambient random stream, including on exceptions.
            Rand.PushState(seed);
            try
            {
                IReadOnlyList<LaborEquipmentCatalogueEntry> apparelPool =
                    LaborEquipmentCatalogue.EntriesFor(
                        LaborEquipmentCatalogueRole.Apparel, itemTechCeiling);

                LaborEquipmentCatalogueEntry weapon = null;
                if (clause != CombatClause.Civilian)
                {
                    IReadOnlyList<LaborEquipmentCatalogueEntry> weaponPool =
                        LaborEquipmentCatalogue.EntriesFor(
                            LaborEquipmentCatalogueRole.PrimaryWeapon, itemTechCeiling);
                    weapon = ChooseWeapon(weaponPool, promisedTier);
                }

                List<LaborEquipmentCatalogueEntry> apparel = ChooseApparel(
                    apparelPool, promisedTier);
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

        /// <summary>Try-shaped wrapper for callers that prefer an explicit failure result.</summary>
        public static bool TryPlan(
            LaborProspect prospect,
            SettlementEconomicProfile sourceSettlement,
            LaborEquipmentLevel promisedTier,
            CombatClause clause,
            int marketIdentity,
            out LaborEquipmentPackagePlan plan)
        {
            plan = Plan(
                prospect,
                sourceSettlement,
                promisedTier,
                clause,
                marketIdentity);
            return plan != null;
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
            LaborEquipmentLevel promisedTier)
        {
            if (weaponPool == null || weaponPool.Count == 0)
            {
                return null;
            }

            // Standard packages are allowed to be incomplete. Higher combat tiers normally get
            // a weapon, while the later authoritative retry remains free to reject the package.
            if (promisedTier == LaborEquipmentLevel.Standard && Rand.Value < 0.15f)
            {
                return null;
            }

            return PickWeightedEntry(
                weaponPool,
                promisedTier,
                selectedApparel: null,
                selectedDefinitions: null);
        }

        private static List<LaborEquipmentCatalogueEntry> ChooseApparel(
            IReadOnlyList<LaborEquipmentCatalogueEntry> apparelPool,
            LaborEquipmentLevel promisedTier)
        {
            List<LaborEquipmentCatalogueEntry> selected =
                new List<LaborEquipmentCatalogueEntry>();
            if (apparelPool == null || apparelPool.Count == 0)
            {
                return selected;
            }

            int targetCount = ChooseApparelCount(apparelPool, promisedTier);
            List<ThingDef> selectedDefinitions = new List<ThingDef>();
            for (int index = 0; index < targetCount; index++)
            {
                LaborEquipmentCatalogueEntry choice = PickWeightedEntry(
                    apparelPool,
                    promisedTier,
                    selected,
                    selectedDefinitions);
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
            LaborEquipmentLevel promisedTier)
        {
            int availableDefinitions = CountDefinitions(apparelPool);
            int desired;
            float roll = Rand.Value;
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
            List<ThingDef> selectedDefinitions)
        {
            float totalWeight = 0f;
            for (int index = 0; index < pool.Count; index++)
            {
                LaborEquipmentCatalogueEntry entry = pool[index];
                if (!CanSelect(entry, promisedTier, selectedApparel, selectedDefinitions))
                {
                    continue;
                }

                totalWeight += WeightFor(entry, promisedTier, selectedApparel);
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
                if (!CanSelect(entry, promisedTier, selectedApparel, selectedDefinitions))
                {
                    continue;
                }

                float weight = WeightFor(entry, promisedTier, selectedApparel);
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
            List<ThingDef> selectedDefinitions)
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

            for (int index = 0; index < selectedApparel.Count; index++)
            {
                if (!CanShareAbstractSlot(entry.Def, selectedApparel[index].Def))
                {
                    return false;
                }
            }

            return true;
        }

        private static float WeightFor(
            LaborEquipmentCatalogueEntry entry,
            LaborEquipmentLevel promisedTier,
            List<LaborEquipmentCatalogueEntry> selectedApparel)
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
            return bandWeight * scoreWeight;
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
        /// Conservative slot compatibility without a Pawn or BodyDef. Exact pawn-specific
        /// wearability belongs to the later instantiation/retry unit. Different layers are safe;
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
