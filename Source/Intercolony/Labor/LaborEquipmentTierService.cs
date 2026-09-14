using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Answers the two equipment-tier questions without becoming an equipment shop:
    /// what a pawn actually has, and what a settlement can plausibly provide.
    /// </summary>
    public static class LaborEquipmentTierService
    {
        private const float ProfessionalTierScore = 0.52f;
        private const float EliteTierScore = 0.78f;

        // A weapon or a suit without the other half of a combat package can be useful, but it is
        // not a professional or elite combat loadout. The cap keeps Armed and Security honest
        // about the word "package" while still treating any qualifying gear as Standard.
        private const float IncompleteCombatPackageCap = ProfessionalTierScore - 0.01f;

        // These are deliberately broad capability scores rather than item-generation odds. A
        // Standard source is common, Professional is restricted to industrial-era sources with
        // a reasonably strong economic/combat profile, and Elite is both rare and hard-gated.
        private const float StandardCapabilityFloor = -0.30f;
        private const float ProfessionalCapabilityFloor = 0.60f;
        private const float EliteCapabilityFloor = 1.05f;

        /// <summary>
        /// Classifies the pawn's actual loadout under one combat clause.
        ///
        /// The surface is intentionally the same surface priced by
        /// <see cref="EmploymentEquipmentService.Quote(Pawn)"/>: equipped weapons and worn
        /// apparel. Inventory is excluded because it is not part of the equipment bond; counting
        /// it here would promise a tier for gear that the bond does not price.
        /// </summary>
        public static LaborEquipmentLevel Classify(Pawn pawn, CombatClause clause)
        {
            LoadoutSummary summary = ReadQualifyingLoadout(pawn);
            if (summary.QualifyingItemCount <= 0)
            {
                return LaborEquipmentLevel.None;
            }

            if (clause == CombatClause.Civilian)
            {
                // A civilian's tier is work/protection apparel. An equipped weapon is still on
                // the bond surface, but it cannot raise a civilian's equipment tier or imply that
                // the civilian should carry a weapon. With no worn apparel there is no civilian
                // work/protection package to classify.
                return summary.ApparelCount > 0
                    ? TierForScore(summary.ApparelScore)
                    : LaborEquipmentLevel.None;
            }

            if (summary.WeaponCount <= 0 || summary.ApparelCount <= 0)
            {
                return TierForScore(IncompleteCombatPackageCap);
            }

            float weaponWeight = clause == CombatClause.Security ? 0.65f : 0.55f;
            float combatScore = summary.BestWeaponScore * weaponWeight +
                                summary.ApparelScore * (1f - weaponWeight);
            return TierForScore(combatScore);
        }

        /// <summary>
        /// Returns whether a settlement profile can plausibly supply the requested tier for the
        /// clause. This is deterministic: it scores stable profile fields and never rolls.
        /// </summary>
        public static bool CanSupply(
            SettlementEconomicProfile profile,
            LaborEquipmentLevel requested,
            CombatClause clause)
        {
            if (profile == null)
            {
                return false;
            }

            switch (requested)
            {
                case LaborEquipmentLevel.Any:
                case LaborEquipmentLevel.None:
                    return true;
                case LaborEquipmentLevel.Standard:
                    return CapabilityScore(profile, clause) >= StandardCapabilityFloor;
                case LaborEquipmentLevel.Professional:
                    return profile.techTier >= TechLevel.Industrial &&
                           CapabilityScore(profile, clause) >= ProfessionalCapabilityFloor;
                case LaborEquipmentLevel.Elite:
                    // Vanilla generation has no faction-tech ceiling on gear. These hard gates
                    // are therefore the rule that stops a poor or pre-Spacer source from handing
                    // over powered armour merely because a posting asks for Elite.
                    return profile.techTier >= TechLevel.Spacer &&
                           profile.wealthTier >= IntercolonyWealthTier.Comfortable &&
                           CapabilityScore(profile, clause) >= EliteCapabilityFloor;
                default:
                    return false;
            }
        }

        /// <summary>Whether an actual tier satisfies a requested tier.</summary>
        public static bool MeetsOrExceeds(
            LaborEquipmentLevel actual,
            LaborEquipmentLevel requested)
        {
            // Any is a request-side wildcard, not an assertion that an actual loadout is good.
            if (requested == LaborEquipmentLevel.Any)
            {
                return true;
            }

            if (!IsKnownTier(actual) || !IsKnownTier(requested) ||
                actual == LaborEquipmentLevel.Any)
            {
                return false;
            }

            return actual >= requested;
        }

        /// <summary>Player-facing label for the equipment selector and applicant details.</summary>
        public static string Label(LaborEquipmentLevel level)
        {
            switch (level)
            {
                case LaborEquipmentLevel.Any:
                    return "Any equipment";
                case LaborEquipmentLevel.None:
                    return "No equipment";
                case LaborEquipmentLevel.Standard:
                    return "Standard equipment";
                case LaborEquipmentLevel.Professional:
                    return "Professional equipment";
                case LaborEquipmentLevel.Elite:
                    return "Elite equipment";
                default:
                    return "Unknown equipment";
            }
        }

        public static string ShortLabel(LaborEquipmentLevel level)
        {
            switch (level)
            {
                case LaborEquipmentLevel.Any:
                    return "Any";
                case LaborEquipmentLevel.None:
                    return "None";
                case LaborEquipmentLevel.Standard:
                    return "Standard";
                case LaborEquipmentLevel.Professional:
                    return "Professional";
                case LaborEquipmentLevel.Elite:
                    return "Elite";
                default:
                    return "Unknown";
            }
        }

        private static LoadoutSummary ReadQualifyingLoadout(Pawn pawn)
        {
            LoadoutSummary summary = default(LoadoutSummary);
            if (pawn?.equipment != null)
            {
                foreach (ThingWithComps item in pawn.equipment.AllEquipmentListForReading)
                {
                    AddEquippedItem(ref summary, item);
                }
            }

            if (pawn?.apparel != null)
            {
                foreach (Apparel item in pawn.apparel.WornApparel)
                {
                    AddWornApparel(ref summary, item);
                }
            }

            if (summary.ApparelCount > 0 && summary.ApparelWeight > 0f)
            {
                float averageItemScore = summary.ApparelWeightedScore / summary.ApparelWeight;
                float coverage = Mathf.Clamp01(summary.ApparelCoverage);
                float coverageFactor = 0.35f + coverage * 0.65f;
                summary.ApparelScore = Mathf.Clamp01(averageItemScore * coverageFactor);
            }

            return summary;
        }

        private static void AddEquippedItem(ref LoadoutSummary summary, ThingWithComps item)
        {
            if (!IsQualifyingItem(item))
            {
                return;
            }

            summary.QualifyingItemCount++;
            if (!item.def.IsWeapon)
            {
                return;
            }

            summary.WeaponCount++;
            summary.BestWeaponScore = Mathf.Max(
                summary.BestWeaponScore, WeaponItemScore(item));
        }

        private static void AddWornApparel(ref LoadoutSummary summary, Apparel item)
        {
            if (!IsQualifyingItem(item))
            {
                return;
            }

            summary.QualifyingItemCount++;
            summary.ApparelCount++;

            float coverage = item.def.apparel == null
                ? 0f
                : Mathf.Clamp01(item.def.apparel.HumanBodyCoverage);
            float weight = Mathf.Max(0.25f, coverage);
            summary.ApparelCoverage += coverage;
            summary.ApparelWeight += weight;
            summary.ApparelWeightedScore += ApparelItemScore(item) * weight;
        }

        private static bool IsQualifyingItem(Thing item)
        {
            // Keep the same validity boundary as EmploymentEquipmentService.Add. In particular,
            // do not inspect the pawn's inventory or carry tracker here.
            return item != null && item.def != null && item.stackCount > 0;
        }

        private static LaborEquipmentLevel TierForScore(float score)
        {
            if (score >= EliteTierScore)
            {
                return LaborEquipmentLevel.Elite;
            }

            if (score >= ProfessionalTierScore)
            {
                return LaborEquipmentLevel.Professional;
            }

            return LaborEquipmentLevel.Standard;
        }

        private static float WeaponItemScore(ThingWithComps weapon)
        {
            float effectiveness = WeaponEffectivenessScore(weapon);
            return Mathf.Clamp01(
                TechnologyScore(weapon.def.techLevel) * 0.40f +
                QualityScore(weapon) * 0.25f +
                effectiveness * 0.30f +
                MarketValueScore(weapon) * 0.05f);
        }

        private static float ApparelItemScore(Apparel apparel)
        {
            float protection = ApparelProtectionScore(apparel);
            return Mathf.Clamp01(
                TechnologyScore(apparel.def.techLevel) * 0.40f +
                QualityScore(apparel) * 0.25f +
                protection * 0.30f +
                MarketValueScore(apparel) * 0.05f);
        }

        private static float WeaponEffectivenessScore(ThingWithComps weapon)
        {
            if (weapon.def.IsMeleeWeapon)
            {
                // This is the vanilla weapon stat, including the actual item's stuff/quality
                // effects where the stat worker can observe them.
                return Normalize(StatValue(weapon, StatDefOf.MeleeWeapon_AverageDPS, 0f), 2f, 18f);
            }

            if (!weapon.def.IsRangedWeapon)
            {
                return 0f;
            }

            return Normalize(RangedWeaponEffectiveness(weapon), 2f, 18f);
        }

        private static float RangedWeaponEffectiveness(ThingWithComps weapon)
        {
            float accuracy = (
                StatValue(weapon, StatDefOf.AccuracyTouch, 0f) +
                StatValue(weapon, StatDefOf.AccuracyShort, 0f) +
                StatValue(weapon, StatDefOf.AccuracyMedium, 0f) +
                StatValue(weapon, StatDefOf.AccuracyLong, 0f)) / 4f;
            accuracy = Mathf.Clamp01(accuracy);

            float cooldown = Mathf.Max(
                0.1f, StatValue(weapon, StatDefOf.RangedWeapon_Cooldown, 1f));
            float damageMultiplier = Mathf.Max(
                0f, StatValue(weapon, StatDefOf.RangedWeapon_DamageMultiplier, 1f));
            float bestDps = 0f;

            if (weapon.def.Verbs != null)
            {
                foreach (VerbProperties verb in weapon.def.Verbs)
                {
                    if (verb == null || verb.IsMeleeAttack)
                    {
                        continue;
                    }

                    float damage = damageMultiplier;
                    if (verb.defaultProjectile?.projectile != null)
                    {
                        // ProjectileProperties.GetDamageAmount is the vanilla base-damage
                        // calculation; the separate stat applies the real weapon multiplier.
                        damage = verb.defaultProjectile.projectile.GetDamageAmount(null) *
                                 damageMultiplier;
                    }

                    int burstCount = Mathf.Max(1, verb.burstShotCount);
                    float burstSpacing = Mathf.Max(0f, verb.ticksBetweenBurstShots / 60f);
                    float cycleTime = Mathf.Max(
                        0.1f,
                        Mathf.Max(0f, verb.warmupTime) + cooldown +
                        (burstCount - 1) * burstSpacing);
                    bestDps = Mathf.Max(
                        bestDps, damage * burstCount / cycleTime * accuracy);
                }
            }

            return bestDps;
        }

        private static float ApparelProtectionScore(Apparel apparel)
        {
            float sharp = Mathf.Max(
                0f, StatValue(apparel, StatDefOf.ArmorRating_Sharp, 0f));
            float blunt = Mathf.Max(
                0f, StatValue(apparel, StatDefOf.ArmorRating_Blunt, 0f));
            float heat = Mathf.Max(
                0f, StatValue(apparel, StatDefOf.ArmorRating_Heat, 0f));

            // Sharp and blunt are the combat package's primary protection; heat matters, but is
            // deliberately secondary. Coverage is applied again at package level so a high-armor
            // glove or helmet cannot stand in for a whole protective loadout.
            float weightedArmor = sharp * 0.45f + blunt * 0.40f + heat * 0.15f;
            return Mathf.Clamp01(weightedArmor / 0.60f);
        }

        private static float QualityScore(Thing item)
        {
            if (!item.TryGetQuality(out QualityCategory quality))
            {
                quality = QualityCategory.Normal;
            }

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
                    // SettlementProfileGenerator treats Undefined as Industrial; use the same
                    // conservative normalization for a modded item with missing tech metadata.
                    return 0.50f;
            }
        }

        private static float MarketValueScore(Thing item)
        {
            float value = Mathf.Max(0f, StatValue(item, StatDefOf.MarketValue, 0f));
            return Normalize(value, 25f, 2500f);
        }

        private static float StatValue(Thing item, StatDef stat, float fallback)
        {
            float value = item.GetStatValue(stat);
            return IsFinite(value) ? value : fallback;
        }

        private static float Normalize(float value, float low, float high)
        {
            return IsFinite(value) ? Mathf.Clamp01(Mathf.InverseLerp(low, high, value)) : 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static float CapabilityScore(
            SettlementEconomicProfile profile,
            CombatClause clause)
        {
            return TechCapabilityScore(profile.techTier) +
                   WealthCapabilityModifier(profile.wealthTier) +
                   ArchetypeCapabilityModifier(profile.archetype) +
                   ClauseCapabilityModifier(clause);
        }

        private static float TechCapabilityScore(TechLevel tech)
        {
            switch (tech)
            {
                case TechLevel.Animal:
                case TechLevel.Neolithic:
                    return 0.08f;
                case TechLevel.Medieval:
                    return 0.28f;
                case TechLevel.Industrial:
                    return 0.55f;
                case TechLevel.Spacer:
                    return 0.85f;
                case TechLevel.Ultra:
                    return 0.98f;
                case TechLevel.Archotech:
                    return 1.05f;
                default:
                    return 0.08f;
            }
        }

        private static float WealthCapabilityModifier(IntercolonyWealthTier wealth)
        {
            switch (wealth)
            {
                case IntercolonyWealthTier.Destitute:
                    return -0.20f;
                case IntercolonyWealthTier.Modest:
                    return -0.05f;
                case IntercolonyWealthTier.Comfortable:
                    return 0.10f;
                default:
                    return 0.25f;
            }
        }

        private static float ArchetypeCapabilityModifier(IntercolonyArchetype archetype)
        {
            switch (archetype)
            {
                case IntercolonyArchetype.Agricultural:
                    return -0.03f;
                case IntercolonyArchetype.Industrial:
                    return 0.08f;
                case IntercolonyArchetype.Military:
                    return 0.18f;
                case IntercolonyArchetype.Affluent:
                    return 0.15f;
                case IntercolonyArchetype.Frontier:
                    return -0.08f;
                case IntercolonyArchetype.Tribal:
                    return -0.12f;
                case IntercolonyArchetype.TradeHub:
                    return 0.12f;
                default:
                    return 0f;
            }
        }

        private static float ClauseCapabilityModifier(CombatClause clause)
        {
            switch (clause)
            {
                case CombatClause.Armed:
                    return 0.04f;
                case CombatClause.Security:
                    return 0.10f;
                default:
                    return 0f;
            }
        }

        private static bool IsKnownTier(LaborEquipmentLevel level)
        {
            return level >= LaborEquipmentLevel.Any &&
                   level <= LaborEquipmentLevel.Elite;
        }

        private struct LoadoutSummary
        {
            public int QualifyingItemCount;
            public int WeaponCount;
            public int ApparelCount;
            public float BestWeaponScore;
            public float ApparelCoverage;
            public float ApparelWeight;
            public float ApparelWeightedScore;
            public float ApparelScore;
        }
    }
}
