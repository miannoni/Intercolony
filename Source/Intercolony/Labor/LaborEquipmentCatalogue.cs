using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// The stable role of a definition in a labor equipment package. Apparel eligibility for a
    /// particular pawn is deliberately not part of this enum: that remains a per-pawn filter.
    /// </summary>
    internal enum LaborEquipmentCatalogueRole
    {
        Apparel,
        PrimaryWeapon
    }

    /// <summary>One non-melee ranged verb's deterministic inputs to weapon scoring.</summary>
    internal readonly struct LaborEquipmentCatalogueRangedVerbInputs
    {
        internal LaborEquipmentCatalogueRangedVerbInputs(
            int sourceIndex,
            bool hasProjectile,
            float baseDamage,
            int burstShotCount,
            float burstSpacing,
            float warmupTime)
        {
            SourceIndex = sourceIndex;
            HasProjectile = hasProjectile;
            BaseDamage = baseDamage;
            BurstShotCount = burstShotCount;
            BurstSpacing = burstSpacing;
            WarmupTime = warmupTime;
        }

        /// <summary>Original index in the def's verb list; it is a stable tie-break key.</summary>
        public int SourceIndex { get; }

        public bool HasProjectile { get; }

        /// <summary>Projectile base damage, or 1 when the scorer uses damage multiplier directly.</summary>
        public float BaseDamage { get; }

        public int BurstShotCount { get; }

        /// <summary>Ticks-between-shots converted to seconds and clamped at zero.</summary>
        public float BurstSpacing { get; }

        /// <summary>Warmup time clamped at zero.</summary>
        public float WarmupTime { get; }
    }

    /// <summary>
    /// The raw, deterministic stat inputs that the item scorer reads for one def/stuff pair.
    /// The derived score and band are kept separately on the entry and always come from
    /// LaborEquipmentItemScore.Evaluate.
    /// </summary>
    internal readonly struct LaborEquipmentCatalogueStatInputs
    {
        internal LaborEquipmentCatalogueStatInputs(
            float marketValue,
            float armorSharp,
            float armorBlunt,
            float armorHeat,
            float coverage,
            float meleeAverageDps,
            float accuracyTouch,
            float accuracyShort,
            float accuracyMedium,
            float accuracyLong,
            float rangedWeaponCooldown,
            float rangedWeaponDamageMultiplier,
            IReadOnlyList<LaborEquipmentCatalogueRangedVerbInputs> rangedVerbs)
        {
            MarketValue = marketValue;
            ArmorSharp = armorSharp;
            ArmorBlunt = armorBlunt;
            ArmorHeat = armorHeat;
            Coverage = coverage;
            MeleeAverageDps = meleeAverageDps;
            AccuracyTouch = accuracyTouch;
            AccuracyShort = accuracyShort;
            AccuracyMedium = accuracyMedium;
            AccuracyLong = accuracyLong;
            RangedWeaponCooldown = rangedWeaponCooldown;
            RangedWeaponDamageMultiplier = rangedWeaponDamageMultiplier;
            RangedVerbs = rangedVerbs;
        }

        public float MarketValue { get; }

        public float ArmorSharp { get; }

        public float ArmorBlunt { get; }

        public float ArmorHeat { get; }

        public float Coverage { get; }

        public float MeleeAverageDps { get; }

        public float AccuracyTouch { get; }

        public float AccuracyShort { get; }

        public float AccuracyMedium { get; }

        public float AccuracyLong { get; }

        public float RangedWeaponCooldown { get; }

        public float RangedWeaponDamageMultiplier { get; }

        public IReadOnlyList<LaborEquipmentCatalogueRangedVerbInputs> RangedVerbs { get; }
    }

    /// <summary>
    /// One stable def/stuff/quality candidate. This contains no Thing, Pawn, world state, or
    /// random state and is therefore safe for a later seeded package assembler to sample.
    /// </summary>
    internal sealed class LaborEquipmentCatalogueEntry
    {
        internal LaborEquipmentCatalogueEntry(
            ThingDef def,
            ThingDef stuffDef,
            QualityCategory quality,
            bool hasQuality,
            LaborEquipmentCatalogueRole role,
            LaborEquipmentCatalogueStatInputs statInputs,
            LaborEquipmentItemScoreResult scoreResult)
        {
            Def = def;
            StuffDef = stuffDef;
            Quality = quality;
            HasQuality = hasQuality;
            Role = role;
            DefinitionTechLevel = def == null ? TechLevel.Undefined : def.techLevel;
            StuffTechLevel = stuffDef == null ? TechLevel.Undefined : stuffDef.techLevel;
            EffectiveTechLevel = EffectiveTechLevelFor(def, stuffDef);
            CandidateTechLevel = CandidateTechLevelFor(def);
            StatInputs = statInputs;
            ScoreResult = scoreResult;
        }

        public ThingDef Def { get; }

        public ThingDef StuffDef { get; }

        public QualityCategory Quality { get; }

        public bool HasQuality { get; }

        public LaborEquipmentCatalogueRole Role { get; }

        public TechLevel DefinitionTechLevel { get; }

        public TechLevel StuffTechLevel { get; }

        /// <summary>Max(def tech, stuff tech), matching the scorer's effective tech input.</summary>
        public TechLevel EffectiveTechLevel { get; }

        /// <summary>Def tech with the allocator's Undefined-as-Industrial selection fallback.</summary>
        public TechLevel CandidateTechLevel { get; }

        public LaborEquipmentCatalogueStatInputs StatInputs { get; }

        public float MarketValue => StatInputs.MarketValue;

        /// <summary>The scorer's base stat strength before its special-purpose penalty.</summary>
        public float BaseStatStrength => ScoreResult.BaseScore;

        public float BaseScore => ScoreResult.BaseScore;

        public float Score => ScoreResult.Score;

        public float SpecialPurposePenalty => ScoreResult.SpecialPurposePenalty;

        public LaborEquipmentItemBand Band => ScoreResult.Band;

        public LaborEquipmentItemScoreResult ScoreResult { get; }

        private static TechLevel EffectiveTechLevelFor(ThingDef def, ThingDef stuffDef)
        {
            TechLevel tech = def == null ? TechLevel.Undefined : def.techLevel;
            if (stuffDef != null && (int)stuffDef.techLevel > (int)tech)
            {
                tech = stuffDef.techLevel;
            }

            return tech;
        }

        private static TechLevel CandidateTechLevelFor(ThingDef def)
        {
            return def != null && def.techLevel != TechLevel.Undefined
                ? def.techLevel
                : TechLevel.Industrial;
        }
    }

    /// <summary>
    /// One static candidate definition and its complete loaded-def stuff/quality family.
    /// Pawn-specific apparel wearability is intentionally not cached here.
    /// </summary>
    internal sealed class LaborEquipmentCatalogueDefinition
    {
        internal LaborEquipmentCatalogueDefinition(
            ThingDef def,
            LaborEquipmentCatalogueRole role,
            bool hasQuality,
            IReadOnlyList<ThingDef> stuffDefs,
            IReadOnlyList<LaborEquipmentCatalogueEntry> entries)
        {
            Def = def;
            Role = role;
            HasQuality = hasQuality;
            MadeFromStuff = def != null && def.MadeFromStuff;
            DefinitionTechLevel = def == null ? TechLevel.Undefined : def.techLevel;
            CandidateTechLevel = def == null || def.techLevel == TechLevel.Undefined
                ? TechLevel.Industrial
                : def.techLevel;
            StuffDefs = stuffDefs;
            Entries = entries;
        }

        public ThingDef Def { get; }

        public LaborEquipmentCatalogueRole Role { get; }

        public bool HasQuality { get; }

        public bool MadeFromStuff { get; }

        public TechLevel DefinitionTechLevel { get; }

        public TechLevel CandidateTechLevel { get; }

        /// <summary>
        /// Sorted stuff family. A null element is the one non-stuff choice for a non-stuffable
        /// definition, matching the allocator's current choice construction.
        /// </summary>
        public IReadOnlyList<ThingDef> StuffDefs { get; }

        /// <summary>All unique quality combinations for this definition, in stable order.</summary>
        public IReadOnlyList<LaborEquipmentCatalogueEntry> Entries { get; }
    }

    /// <summary>
    /// Transient, lazily-built catalogue of the loaded equipment universe.
    ///
    /// The cache contains only references to loaded defs and derived selection facts. It is not a
    /// Def, GameComponent, or save object and has no Scribe surface. Call Invalidate or Rebuild
    /// after a def universe changes; the next read after Invalidate rebuilds lazily.
    /// </summary>
    internal static class LaborEquipmentCatalogue
    {
        private static readonly object CacheLock = new object();

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

        private static readonly IReadOnlyList<LaborEquipmentCatalogueEntry> EmptyEntries =
            new List<LaborEquipmentCatalogueEntry>().AsReadOnly();

        private static readonly IReadOnlyList<LaborEquipmentCatalogueRangedVerbInputs>
            EmptyRangedVerbs = new List<LaborEquipmentCatalogueRangedVerbInputs>().AsReadOnly();

        private static volatile CatalogueCache cache;

        /// <summary>All statically eligible definitions, ordered by role then ordinal defName.</summary>
        internal static IReadOnlyList<LaborEquipmentCatalogueDefinition> Definitions =>
            GetCache().Definitions;

        /// <summary>
        /// All unique def/stuff/quality entries across the concrete tech ceilings, ordered by
        /// role, defName, stuff defName, then quality enum value.
        /// </summary>
        internal static IReadOnlyList<LaborEquipmentCatalogueEntry> Entries =>
            GetCache().Entries;

        /// <summary>
        /// Returns the exact static candidate population available to the allocator for an item
        /// tech ceiling. The later assembler must still apply pawn-specific apparel filters.
        /// </summary>
        internal static IReadOnlyList<LaborEquipmentCatalogueEntry> EntriesFor(
            LaborEquipmentCatalogueRole role, TechLevel itemTechCeiling)
        {
            return GetCache().EntriesFor(role, itemTechCeiling);
        }

        /// <summary>
        /// Drops the transient cache. This does not touch defs or save state.
        /// </summary>
        internal static void Invalidate()
        {
            lock (CacheLock)
            {
                cache = null;
            }
        }

        /// <summary>Immediately rebuilds the transient catalogue from the current loaded defs.</summary>
        internal static void Rebuild()
        {
            lock (CacheLock)
            {
                cache = Build();
            }
        }

        private static CatalogueCache GetCache()
        {
            CatalogueCache current = cache;
            if (current != null)
            {
                return current;
            }

            lock (CacheLock)
            {
                if (cache == null)
                {
                    cache = Build();
                }

                return cache;
            }
        }

        private static CatalogueCache Build()
        {
            List<DefinitionBuildState> definitionStates =
                new List<DefinitionBuildState>();
            List<LaborEquipmentCatalogueEntry> allEntries =
                new List<LaborEquipmentCatalogueEntry>();
            Dictionary<string, LaborEquipmentCatalogueEntry> entriesByKey =
                new Dictionary<string, LaborEquipmentCatalogueEntry>(StringComparer.Ordinal);

            int roleCount = Enum.GetValues(typeof(LaborEquipmentCatalogueRole)).Length;
            int techLevelCount = (int)TechLevel.Archotech + 1;
            List<LaborEquipmentCatalogueEntry>[][] entriesByRoleAndTech =
                new List<LaborEquipmentCatalogueEntry>[roleCount][];
            for (int roleIndex = 0; roleIndex < roleCount; roleIndex++)
            {
                entriesByRoleAndTech[roleIndex] =
                    new List<LaborEquipmentCatalogueEntry>[techLevelCount];
                for (int techIndex = 0; techIndex < techLevelCount; techIndex++)
                {
                    entriesByRoleAndTech[roleIndex][techIndex] =
                        new List<LaborEquipmentCatalogueEntry>();
                }
            }

            List<ThingDef> allDefs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int defIndex = 0; defIndex < allDefs.Count; defIndex++)
            {
                ThingDef def = allDefs[defIndex];
                if (!TryGetRole(def, out LaborEquipmentCatalogueRole role))
                {
                    continue;
                }

                bool hasQuality = def.HasComp(typeof(CompQuality));
                DefinitionBuildState definitionState = new DefinitionBuildState(
                    def, role, hasQuality);

                for (int techIndex = (int)TechLevel.Animal;
                     techIndex <= (int)TechLevel.Archotech;
                     techIndex++)
                {
                    TechLevel itemTechCeiling = (TechLevel)techIndex;
                    if (!IsWithinSourceTech(def, itemTechCeiling))
                    {
                        continue;
                    }

                    List<ThingDef> stuffChoices =
                        BuildStuffChoices(def, itemTechCeiling);
                    for (int stuffIndex = 0; stuffIndex < stuffChoices.Count; stuffIndex++)
                    {
                        ThingDef stuffDef = stuffChoices[stuffIndex];
                        if (!definitionState.StuffDefs.Contains(stuffDef))
                        {
                            definitionState.StuffDefs.Add(stuffDef);
                        }

                        int qualityCount = hasQuality ? QualityOrder.Length : 1;
                        for (int qualityIndex = 0; qualityIndex < qualityCount; qualityIndex++)
                        {
                            QualityCategory quality = hasQuality
                                ? QualityOrder[qualityIndex]
                                : QualityCategory.Normal;
                            string entryKey = EntryKey(def, stuffDef, quality);
                            if (!entriesByKey.TryGetValue(
                                    entryKey, out LaborEquipmentCatalogueEntry entry))
                            {
                                LaborEquipmentCatalogueStatInputs statInputs =
                                    BuildStatInputs(def, stuffDef);
                                LaborEquipmentItemScoreResult scoreResult =
                                    LaborEquipmentItemScore.Evaluate(def, stuffDef, quality);
                                entry = new LaborEquipmentCatalogueEntry(
                                    def,
                                    stuffDef,
                                    quality,
                                    hasQuality,
                                    role,
                                    statInputs,
                                    scoreResult);
                                entriesByKey.Add(entryKey, entry);
                                allEntries.Add(entry);
                            }

                            if (!definitionState.Entries.Contains(entry))
                            {
                                definitionState.Entries.Add(entry);
                            }

                            List<LaborEquipmentCatalogueEntry> techEntries =
                                entriesByRoleAndTech[(int)role][techIndex];
                            if (!techEntries.Contains(entry))
                            {
                                techEntries.Add(entry);
                            }
                        }
                    }
                }

                definitionStates.Add(definitionState);
            }

            List<LaborEquipmentCatalogueDefinition> definitions =
                new List<LaborEquipmentCatalogueDefinition>(definitionStates.Count);
            for (int stateIndex = 0; stateIndex < definitionStates.Count; stateIndex++)
            {
                DefinitionBuildState state = definitionStates[stateIndex];
                state.StuffDefs.Sort(CompareThingDefs);
                state.Entries.Sort(CompareEntries);
                definitions.Add(new LaborEquipmentCatalogueDefinition(
                    state.Def,
                    state.Role,
                    state.HasQuality,
                    state.StuffDefs.AsReadOnly(),
                    state.Entries.AsReadOnly()));
            }

            definitions.Sort(CompareDefinitions);
            allEntries.Sort(CompareEntries);

            IReadOnlyList<LaborEquipmentCatalogueEntry>[][] readOnlyEntriesByRoleAndTech =
                new IReadOnlyList<LaborEquipmentCatalogueEntry>[roleCount][];
            for (int roleIndex = 0; roleIndex < roleCount; roleIndex++)
            {
                readOnlyEntriesByRoleAndTech[roleIndex] =
                    new IReadOnlyList<LaborEquipmentCatalogueEntry>[techLevelCount];
                for (int techIndex = 0; techIndex < techLevelCount; techIndex++)
                {
                    List<LaborEquipmentCatalogueEntry> techEntries =
                        entriesByRoleAndTech[roleIndex][techIndex];
                    techEntries.Sort(CompareEntries);
                    readOnlyEntriesByRoleAndTech[roleIndex][techIndex] =
                        techEntries.AsReadOnly();
                }
            }

            return new CatalogueCache(
                definitions.AsReadOnly(),
                allEntries.AsReadOnly(),
                readOnlyEntriesByRoleAndTech);
        }

        private static bool TryGetRole(
            ThingDef def, out LaborEquipmentCatalogueRole role)
        {
            role = LaborEquipmentCatalogueRole.Apparel;
            if (def == null || def.category != ThingCategory.Item ||
                def.generateAllowChance <= 0f || def.thingClass == null)
            {
                return false;
            }

            if (def.IsApparel && typeof(Apparel).IsAssignableFrom(def.thingClass) &&
                def.apparel.layers != null && def.apparel.layers.Count > 0 &&
                def.apparel.bodyPartGroups != null && def.apparel.bodyPartGroups.Count > 0)
            {
                role = LaborEquipmentCatalogueRole.Apparel;
                return true;
            }

            if (def.IsWeapon && def.equipmentType == EquipmentType.Primary &&
                typeof(ThingWithComps).IsAssignableFrom(def.thingClass))
            {
                role = LaborEquipmentCatalogueRole.PrimaryWeapon;
                return true;
            }

            return false;
        }

        private static List<ThingDef> BuildStuffChoices(
            ThingDef def, TechLevel itemTechCeiling)
        {
            List<ThingDef> choices = new List<ThingDef>();
            if (!def.MadeFromStuff)
            {
                choices.Add(null);
                return choices;
            }

            foreach (ThingDef stuff in GenStuff.AllowedStuffsFor(
                         def, itemTechCeiling, checkAllowedInStuffGeneration: true))
            {
                if (stuff == null || !stuff.IsStuff ||
                    !IsWithinSourceStuffTech(stuff, itemTechCeiling) ||
                    choices.Contains(stuff))
                {
                    continue;
                }

                choices.Add(stuff);
            }

            choices.Sort(CompareThingDefs);
            return choices;
        }

        private static bool IsWithinSourceTech(ThingDef def, TechLevel itemTechCeiling)
        {
            if (def == null || itemTechCeiling == TechLevel.Undefined)
            {
                return false;
            }

            TechLevel itemTech = def.techLevel == TechLevel.Undefined
                ? TechLevel.Industrial
                : def.techLevel;
            return (int)itemTech <= (int)itemTechCeiling;
        }

        private static bool IsWithinSourceStuffTech(
            ThingDef stuff, TechLevel itemTechCeiling)
        {
            if (stuff == null || itemTechCeiling == TechLevel.Undefined)
            {
                return false;
            }

            return stuff.techLevel == TechLevel.Undefined ||
                   (int)stuff.techLevel <= (int)itemTechCeiling;
        }

        private static LaborEquipmentCatalogueStatInputs BuildStatInputs(
            ThingDef def, ThingDef stuffDef)
        {
            bool isApparel = def != null && def.IsApparel;
            bool isMeleeWeapon = def != null && def.IsMeleeWeapon;
            bool isRangedWeapon = def != null && def.IsRangedWeapon;

            float armorSharp = isApparel
                ? Math.Max(0f, AbstractStatValue(
                    def, StatDefOf.ArmorRating_Sharp, stuffDef, 0f))
                : 0f;
            float armorBlunt = isApparel
                ? Math.Max(0f, AbstractStatValue(
                    def, StatDefOf.ArmorRating_Blunt, stuffDef, 0f))
                : 0f;
            float armorHeat = isApparel
                ? Math.Max(0f, AbstractStatValue(
                    def, StatDefOf.ArmorRating_Heat, stuffDef, 0f))
                : 0f;
            float coverage = isApparel && def.apparel != null
                ? Mathf.Clamp01(def.apparel.HumanBodyCoverage)
                : 0f;

            float meleeAverageDps = isMeleeWeapon
                ? AbstractStatValue(
                    def, StatDefOf.MeleeWeapon_AverageDPS, stuffDef, 0f)
                : 0f;
            float accuracyTouch = isRangedWeapon
                ? AbstractStatValue(def, StatDefOf.AccuracyTouch, stuffDef, 0f)
                : 0f;
            float accuracyShort = isRangedWeapon
                ? AbstractStatValue(def, StatDefOf.AccuracyShort, stuffDef, 0f)
                : 0f;
            float accuracyMedium = isRangedWeapon
                ? AbstractStatValue(def, StatDefOf.AccuracyMedium, stuffDef, 0f)
                : 0f;
            float accuracyLong = isRangedWeapon
                ? AbstractStatValue(def, StatDefOf.AccuracyLong, stuffDef, 0f)
                : 0f;
            float rangedWeaponCooldown = isRangedWeapon
                ? Math.Max(0.1f, AbstractStatValue(
                    def, StatDefOf.RangedWeapon_Cooldown, stuffDef, 1f))
                : 0f;
            float rangedWeaponDamageMultiplier = isRangedWeapon
                ? Math.Max(0f, AbstractStatValue(
                    def, StatDefOf.RangedWeapon_DamageMultiplier, stuffDef, 1f))
                : 0f;
            IReadOnlyList<LaborEquipmentCatalogueRangedVerbInputs> rangedVerbs =
                BuildRangedVerbInputs(def, isRangedWeapon);

            return new LaborEquipmentCatalogueStatInputs(
                AbstractStatValue(def, StatDefOf.MarketValue, stuffDef, 0f),
                armorSharp,
                armorBlunt,
                armorHeat,
                coverage,
                meleeAverageDps,
                accuracyTouch,
                accuracyShort,
                accuracyMedium,
                accuracyLong,
                rangedWeaponCooldown,
                rangedWeaponDamageMultiplier,
                rangedVerbs);
        }

        private static IReadOnlyList<LaborEquipmentCatalogueRangedVerbInputs>
            BuildRangedVerbInputs(ThingDef def, bool isRangedWeapon)
        {
            if (!isRangedWeapon || def == null)
            {
                return EmptyRangedVerbs;
            }

            List<LaborEquipmentCatalogueRangedVerbInputs> rangedVerbs =
                new List<LaborEquipmentCatalogueRangedVerbInputs>();
            List<VerbProperties> verbs = def.Verbs;
            for (int index = 0; index < verbs.Count; index++)
            {
                VerbProperties verb = verbs[index];
                if (verb == null || verb.IsMeleeAttack)
                {
                    continue;
                }

                bool hasProjectile = verb.defaultProjectile?.projectile != null;
                float baseDamage = hasProjectile
                    ? verb.defaultProjectile.projectile.GetDamageAmount(null)
                    : 1f;
                if (!IsFinite(baseDamage) || baseDamage < 0f)
                {
                    baseDamage = 0f;
                }

                rangedVerbs.Add(new LaborEquipmentCatalogueRangedVerbInputs(
                    index,
                    hasProjectile,
                    baseDamage,
                    Math.Max(1, verb.burstShotCount),
                    Mathf.Max(0f, verb.ticksBetweenBurstShots / 60f),
                    Mathf.Max(0f, verb.warmupTime)));
            }

            rangedVerbs.Sort(CompareRangedVerbInputs);
            return rangedVerbs.AsReadOnly();
        }

        private static float AbstractStatValue(
            ThingDef def, StatDef stat, ThingDef stuff, float fallback)
        {
            if (def == null || stat == null)
            {
                return fallback;
            }

            try
            {
                float value = def.GetStatValueAbstract(stat, stuff);
                return IsFinite(value) ? value : fallback;
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        private static string EntryKey(
            ThingDef def, ThingDef stuffDef, QualityCategory quality)
        {
            return NameOf(def) + "\u001f" + NameOf(stuffDef) + "\u001f" +
                   ((int)quality).ToString();
        }

        private static int CompareDefinitions(
            LaborEquipmentCatalogueDefinition left,
            LaborEquipmentCatalogueDefinition right)
        {
            int comparison = left.Role.CompareTo(right.Role);
            if (comparison != 0)
            {
                return comparison;
            }

            return StringComparer.Ordinal.Compare(NameOf(left.Def), NameOf(right.Def));
        }

        private static int CompareEntries(
            LaborEquipmentCatalogueEntry left,
            LaborEquipmentCatalogueEntry right)
        {
            int comparison = left.Role.CompareTo(right.Role);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = StringComparer.Ordinal.Compare(NameOf(left.Def), NameOf(right.Def));
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = StringComparer.Ordinal.Compare(
                NameOf(left.StuffDef), NameOf(right.StuffDef));
            if (comparison != 0)
            {
                return comparison;
            }

            return left.Quality.CompareTo(right.Quality);
        }

        private static int CompareThingDefs(ThingDef left, ThingDef right)
        {
            return StringComparer.Ordinal.Compare(NameOf(left), NameOf(right));
        }

        private static int CompareRangedVerbInputs(
            LaborEquipmentCatalogueRangedVerbInputs left,
            LaborEquipmentCatalogueRangedVerbInputs right)
        {
            return left.SourceIndex.CompareTo(right.SourceIndex);
        }

        private static string NameOf(ThingDef def)
        {
            return def?.defName ?? String.Empty;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private sealed class DefinitionBuildState
        {
            internal DefinitionBuildState(
                ThingDef def, LaborEquipmentCatalogueRole role, bool hasQuality)
            {
                Def = def;
                Role = role;
                HasQuality = hasQuality;
                StuffDefs = new List<ThingDef>();
                Entries = new List<LaborEquipmentCatalogueEntry>();
            }

            internal ThingDef Def { get; }

            internal LaborEquipmentCatalogueRole Role { get; }

            internal bool HasQuality { get; }

            internal List<ThingDef> StuffDefs { get; }

            internal List<LaborEquipmentCatalogueEntry> Entries { get; }
        }

        private sealed class CatalogueCache
        {
            private readonly IReadOnlyList<LaborEquipmentCatalogueEntry>[][] entriesByRoleAndTech;

            internal CatalogueCache(
                IReadOnlyList<LaborEquipmentCatalogueDefinition> definitions,
                IReadOnlyList<LaborEquipmentCatalogueEntry> entries,
                IReadOnlyList<LaborEquipmentCatalogueEntry>[][] entriesByRoleAndTech)
            {
                Definitions = definitions;
                Entries = entries;
                this.entriesByRoleAndTech = entriesByRoleAndTech;
            }

            internal IReadOnlyList<LaborEquipmentCatalogueDefinition> Definitions { get; }

            internal IReadOnlyList<LaborEquipmentCatalogueEntry> Entries { get; }

            internal IReadOnlyList<LaborEquipmentCatalogueEntry> EntriesFor(
                LaborEquipmentCatalogueRole role, TechLevel itemTechCeiling)
            {
                int roleIndex = (int)role;
                int techIndex = (int)itemTechCeiling;
                if (roleIndex < 0 || roleIndex >= entriesByRoleAndTech.Length ||
                    techIndex <= (int)TechLevel.Undefined ||
                    techIndex >= entriesByRoleAndTech[roleIndex].Length)
                {
                    return EmptyEntries;
                }

                return entriesByRoleAndTech[roleIndex][techIndex];
            }
        }
    }
}
