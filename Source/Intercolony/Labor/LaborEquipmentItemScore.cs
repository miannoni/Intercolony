using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    public enum LaborEquipmentItemBand
    {
        Basic,
        Professional,
        Elite
    }

    public readonly struct LaborEquipmentItemScoreResult
    {
        public LaborEquipmentItemScoreResult(
            float baseScore,
            float score,
            float specialPurposePenalty,
            LaborEquipmentItemBand band)
        {
            BaseScore = baseScore;
            Score = score;
            SpecialPurposePenalty = specialPurposePenalty;
            Band = band;
        }

        public float BaseScore { get; }

        public float Score { get; }

        public float SpecialPurposePenalty { get; }

        public LaborEquipmentItemBand Band { get; }
    }

    /// <summary>
    /// Scores one weapon or apparel definition without creating a Thing or consulting world
    /// state. The score is a selection input; LaborEquipmentTierService.Classify remains the
    /// authority for the tier of a generated loadout.
    /// </summary>
    public static class LaborEquipmentItemScore
    {
        // These are item-band thresholds, deliberately below the package thresholds in
        // LaborEquipmentTierService. Individual items are overlapping inputs rather than rigid
        // recipes; the later package check decides whether a loadout actually keeps its promise.
        private const float ProfessionalBandThreshold = 0.36f;
        private const float EliteBandThreshold = 0.46f;

        private const float TechnologyWeight = 0.32f;
        private const float QualityWeight = 0.34f;
        private const float EffectivenessWeight = 0.29f;
        private const float MarketValueWeight = 0.05f;

        private const float SustainedWeaponWeight = 0.70f;
        private const float BurstWeaponWeight = 0.20f;
        private const float SingleShotWeaponWeight = 0.10f;

        private const float ApparelArmorWeight = 0.80f;
        private const float ApparelCoverageWeight = 0.20f;

        private const float SingleUsePenalty = 0.45f;
        private const float ExplosivePenalty = 0.08f;
        private const float SpecializedPenalty = 0.10f;
        private const float ResourceBehaviorPenalty = 0.05f;
        private const float SpecialPurposePenaltyCap = 0.65f;

        /// <summary>
        /// Returns a deterministic item score for the supplied definition, stuff, and quality.
        /// Quality is an explicit input because abstract stat accessors do not represent the
        /// quality of a future Thing instance.
        /// </summary>
        public static LaborEquipmentItemScoreResult Evaluate(
            ThingDef def,
            ThingDef stuff,
            QualityCategory quality)
        {
            if (def == null || (!def.IsWeapon && !def.IsApparel))
            {
                return new LaborEquipmentItemScoreResult(
                    0f,
                    0f,
                    0f,
                    LaborEquipmentItemBand.Basic);
            }

            float baseScore = ItemScore(def, stuff, quality);
            float specialPurposePenalty = def.IsWeapon
                ? SpecialPurposePenaltyFor(def)
                : 0f;
            float score = Mathf.Clamp01(baseScore - specialPurposePenalty);
            return new LaborEquipmentItemScoreResult(
                baseScore,
                score,
                specialPurposePenalty,
                BandFor(score));
        }

        private static float ItemScore(
            ThingDef def,
            ThingDef stuff,
            QualityCategory quality)
        {
            float technology = TechnologyScore(EffectiveTechLevel(def, stuff));
            float qualityScore = QualityScore(quality);
            float effectiveness = def.IsWeapon
                ? WeaponEffectivenessScore(def, stuff)
                : ApparelEffectivenessScore(def, stuff);
            float marketValue = MarketValueScore(def, stuff);

            return Mathf.Clamp01(
                technology * TechnologyWeight +
                qualityScore * QualityWeight +
                effectiveness * EffectivenessWeight +
                marketValue * MarketValueWeight);
        }

        private static float WeaponEffectivenessScore(ThingDef def, ThingDef stuff)
        {
            if (def.IsMeleeWeapon)
            {
                return Normalize(
                    AbstractStatValue(def, StatDefOf.MeleeWeapon_AverageDPS, stuff, 0f),
                    2f,
                    18f);
            }

            if (!def.IsRangedWeapon)
            {
                return 0f;
            }

            return RangedWeaponEffectivenessScore(def, stuff);
        }

        private static float RangedWeaponEffectivenessScore(ThingDef def, ThingDef stuff)
        {
            float accuracy = (
                AbstractStatValue(def, StatDefOf.AccuracyTouch, stuff, 0f) +
                AbstractStatValue(def, StatDefOf.AccuracyShort, stuff, 0f) +
                AbstractStatValue(def, StatDefOf.AccuracyMedium, stuff, 0f) +
                AbstractStatValue(def, StatDefOf.AccuracyLong, stuff, 0f)) / 4f;
            accuracy = Mathf.Clamp01(accuracy);

            float cooldown = Mathf.Max(
                0.1f,
                AbstractStatValue(def, StatDefOf.RangedWeapon_Cooldown, stuff, 1f));
            float damageMultiplier = Mathf.Max(
                0f,
                AbstractStatValue(def, StatDefOf.RangedWeapon_DamageMultiplier, stuff, 1f));
            float bestDps = 0f;
            float bestBurstImpact = 0f;
            float bestSingleShotImpact = 0f;

            foreach (VerbProperties verb in def.Verbs)
            {
                if (verb == null || verb.IsMeleeAttack)
                {
                    continue;
                }

                float damage = damageMultiplier;
                if (verb.defaultProjectile?.projectile != null)
                {
                    // Passing null for the weapon asks ProjectileProperties for base damage;
                    // the weapon's abstract damage multiplier is applied exactly once here.
                    damage = verb.defaultProjectile.projectile.GetDamageAmount(null) *
                             damageMultiplier;
                }

                if (!IsFinite(damage) || damage < 0f)
                {
                    damage = 0f;
                }

                int burstCount = Math.Max(1, verb.burstShotCount);
                float burstSpacing = Mathf.Max(0f, verb.ticksBetweenBurstShots / 60f);
                float cycleTime = Mathf.Max(
                    0.1f,
                    Mathf.Max(0f, verb.warmupTime) + cooldown +
                    (burstCount - 1) * burstSpacing);
                float dps = damage * burstCount / cycleTime * accuracy;
                float burstImpact = damage * burstCount * accuracy;
                float singleShotImpact = damage * accuracy;
                bestDps = Mathf.Max(bestDps, dps);
                bestBurstImpact = Mathf.Max(bestBurstImpact, burstImpact);
                bestSingleShotImpact = Mathf.Max(bestSingleShotImpact, singleShotImpact);
            }

            float sustainedScore = Normalize(bestDps, 2f, 18f);
            // Burst impact keeps automatic weapons useful even when their sustained rate is
            // moderated by warmup/cooldown. The single-shot term recognizes a precise heavy
            // projectile without making an ordinary one-shot shotgun a professional weapon.
            float burstScore = Normalize(bestBurstImpact, 5f, 35f);
            float singleShotScore = Normalize(bestSingleShotImpact, 12f, 30f);
            return Mathf.Clamp01(
                sustainedScore * SustainedWeaponWeight +
                burstScore * BurstWeaponWeight +
                singleShotScore * SingleShotWeaponWeight);
        }

        private static float ApparelEffectivenessScore(ThingDef def, ThingDef stuff)
        {
            float sharp = Mathf.Max(
                0f,
                AbstractStatValue(def, StatDefOf.ArmorRating_Sharp, stuff, 0f));
            float blunt = Mathf.Max(
                0f,
                AbstractStatValue(def, StatDefOf.ArmorRating_Blunt, stuff, 0f));
            float heat = Mathf.Max(
                0f,
                AbstractStatValue(def, StatDefOf.ArmorRating_Heat, stuff, 0f));
            float weightedArmor = sharp * 0.45f + blunt * 0.40f + heat * 0.15f;
            float armorScore = Normalize(weightedArmor, 0f, 1f);
            float coverage = def.apparel == null
                ? 0f
                : Mathf.Clamp01(def.apparel.HumanBodyCoverage);

            return Mathf.Clamp01(
                armorScore * ApparelArmorWeight +
                coverage * ApparelCoverageWeight);
        }

        private static float SpecialPurposePenaltyFor(ThingDef def)
        {
            if (def == null || !def.IsWeapon)
            {
                return 0f;
            }

            // These are vanilla semantic tags, not item identity checks; modded definitions can
            // opt into the same behavior without adding a name to this classifier.
            bool singleUse = HasTag(def.weaponTags, "GunSingleUse") ||
                             HasTag(def.thingSetMakerTags, "SingleUseWeapon") ||
                             HasCompProperties<CompProperties_UseEffectDestroySelf>(def);
            bool explosive = HasCompProperties<CompProperties_Explosive>(def);
            bool specialized = false;
            bool resourceBehavior =
                HasCompProperties<CompProperties_EquippableAbilityReloadable>(def) ||
                HasCompProperties<CompProperties_Rechargeable>(def) ||
                HasCompProperties<CompProperties_Refuelable>(def);

            foreach (VerbProperties verb in def.Verbs)
            {
                if (verb == null)
                {
                    continue;
                }

                Type verbClass = verb.verbClass;
                if (verbClass != null &&
                    (typeof(Verb_ShootOneUse).IsAssignableFrom(verbClass) ||
                     typeof(Verb_LaunchProjectileStaticOneUse).IsAssignableFrom(verbClass)))
                {
                    singleUse = true;
                }

                explosive |= CausesExplosion(verb);
                specialized |= verb.onlyManualCast ||
                               verb.ai_IsBuildingDestroyer ||
                               verb.ai_IsDoorDestroyer ||
                               verb.ai_ProjectileLaunchingIgnoresMeleeThreats ||
                               verb.ai_AvoidFriendlyFireRadius > 0f;
                resourceBehavior |= verb.consumeFuelPerShot > 0f ||
                                    verb.consumeFuelPerBurst > 0f;
            }

            float penalty = 0f;
            if (singleUse)
            {
                penalty += SingleUsePenalty;
            }

            if (explosive)
            {
                penalty += ExplosivePenalty;
            }

            if (specialized)
            {
                penalty += SpecializedPenalty;
            }

            if (resourceBehavior)
            {
                penalty += ResourceBehaviorPenalty;
            }

            return Mathf.Clamp01(Mathf.Min(SpecialPurposePenaltyCap, penalty));
        }

        private static bool CausesExplosion(VerbProperties verb)
        {
            try
            {
                return verb.CausesExplosion;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool HasTag(List<string> tags, string tag)
        {
            if (tags == null)
            {
                return false;
            }

            for (int index = 0; index < tags.Count; index++)
            {
                if (String.Equals(tags[index], tag, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasCompProperties<T>(ThingDef def) where T : CompProperties
        {
            return def != null && def.comps != null && def.GetCompProperties<T>() != null;
        }

        private static TechLevel EffectiveTechLevel(ThingDef def, ThingDef stuff)
        {
            TechLevel tech = def == null ? TechLevel.Undefined : def.techLevel;
            if (stuff != null && (int)stuff.techLevel > (int)tech)
            {
                tech = stuff.techLevel;
            }

            return tech;
        }

        private static float QualityScore(QualityCategory quality)
        {
            return Mathf.InverseLerp(
                (float)QualityCategory.Awful,
                (float)QualityCategory.Legendary,
                (float)quality);
        }

        private static float TechnologyScore(TechLevel tech)
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

        private static float MarketValueScore(ThingDef def, ThingDef stuff)
        {
            float value = Mathf.Max(
                0f,
                AbstractStatValue(def, StatDefOf.MarketValue, stuff, 0f));
            return Normalize(value, 25f, 2500f);
        }

        private static float AbstractStatValue(
            ThingDef def,
            StatDef stat,
            ThingDef stuff,
            float fallback)
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

        private static float Normalize(float value, float low, float high)
        {
            return IsFinite(value)
                ? Mathf.Clamp01(Mathf.InverseLerp(low, high, value))
                : 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static LaborEquipmentItemBand BandFor(float score)
        {
            if (score >= EliteBandThreshold)
            {
                return LaborEquipmentItemBand.Elite;
            }

            if (score >= ProfessionalBandThreshold)
            {
                return LaborEquipmentItemBand.Professional;
            }

            return LaborEquipmentItemBand.Basic;
        }
    }
}
