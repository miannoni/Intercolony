using System;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Derives a <see cref="SettlementEconomicProfile"/> deterministically from the world's
    /// economy seed and a settlement's stable ID (DESIGN.md §9, §60, §96).
    ///
    /// Pure with respect to game state apart from reading the settlement and its faction:
    /// the same seed and settlement always produce the same profile, which is what makes
    /// regeneration a valid alternative to persistence.
    /// </summary>
    public static class SettlementProfileGenerator
    {
        /// <summary>
        /// Whether a settlement takes part in the Intercolony economy at all.
        ///
        /// DESIGN.md §51 asks for "the simplest intuitive rule" for market access, and
        /// deliberately warns against overcomplicating communications before commerce works.
        /// This checks only *structurally stable* traits, so a settlement's profile does not
        /// wink in and out of existence as goodwill drifts. Relationship, comms, and
        /// discovery gating belong to the market layer that consumes profiles.
        /// </summary>
        public static bool IsEligible(Settlement settlement)
        {
            if (settlement == null || !settlement.Spawned)
            {
                return false;
            }

            Faction faction = settlement.Faction;
            if (faction == null)
            {
                return false;
            }

            // The player's own settlements are not counterparties.
            if (faction.IsPlayer)
            {
                return false;
            }

            // Hidden factions have no diplomatic surface to trade against, and temporary
            // factions are transient bookkeeping rather than real polities.
            if (faction.Hidden || faction.temporary)
            {
                return false;
            }

            // Permanent enemies will never trade, so giving them an economy is wasted work.
            if (faction.def.permanentEnemy)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Builds the profile. Uses <see cref="Rand.PushState(int)"/> / <see cref="Rand.PopState"/>
        /// so the roll cannot perturb RimWorld's global random state (DESIGN.md §60).
        /// </summary>
        public static SettlementEconomicProfile Generate(int economySeed, Settlement settlement)
        {
            Faction faction = settlement.Faction;
            return GenerateFrom(
                economySeed,
                settlement.ID,
                faction?.loadID ?? -1,
                settlement.Label,
                faction?.Name,
                faction?.def?.techLevel ?? TechLevel.Undefined);
        }

        /// <summary>
        /// The generation core, taking plain values rather than a <see cref="Settlement"/>.
        ///
        /// Split out so the rolls can be exercised against inputs that a vanilla world never
        /// produces — unset tech levels, missing names, extreme IDs — without needing a
        /// faction-adding mod installed. That is the only practical way to test §96's
        /// "modded factions do not crash" criterion locally. See IntercolonyProfileSelfTest.
        /// </summary>
        public static SettlementEconomicProfile GenerateFrom(
            int economySeed,
            int settlementId,
            int factionLoadId,
            string settlementName,
            string factionName,
            TechLevel rawTech)
        {
            int seed = Gen.HashCombineInt(economySeed, settlementId);

            SettlementEconomicProfile profile = new SettlementEconomicProfile
            {
                settlementId = settlementId,
                factionLoadId = factionLoadId,
                settlementName = string.IsNullOrEmpty(settlementName) ? "unnamed" : settlementName,
                factionName = string.IsNullOrEmpty(factionName) ? "no faction" : factionName,
                techTier = NormalizeTech(rawTech),
                seed = seed
            };

            Rand.PushState(seed);
            try
            {
                profile.archetype = RollArchetype(profile.techTier);
                profile.wealthTier = RollWealth(profile.archetype, profile.techTier);
                profile.volatility = Rand.Range(0.05f, 0.35f);
                profile.qualityPreference = RollQualityPreference(profile.archetype, profile.wealthTier);
                profile.laborSupplyModifier = RollLaborModifier(profile.archetype, profile.wealthTier);
                FillWeights(profile);
            }
            finally
            {
                Rand.PopState();
            }

            return profile;
        }

        /// <summary>
        /// Resolves <see cref="TechLevel.Undefined"/> to Industrial.
        ///
        /// Modded factions routinely leave <c>techLevel</c> unset (DESIGN.md §63, §64), and
        /// Undefined is the zero value of the enum — so every "is this pre-industrial?" test
        /// would silently classify such a faction as neolithic and shove it toward the Tribal
        /// archetype. Normalizing once here keeps every downstream tech comparison consistent.
        /// </summary>
        private static TechLevel NormalizeTech(TechLevel tech)
        {
            return tech == TechLevel.Undefined ? TechLevel.Industrial : tech;
        }

        private static readonly IntercolonyArchetype[] AllArchetypes =
            (IntercolonyArchetype[])Enum.GetValues(typeof(IntercolonyArchetype));

        private static readonly IntercolonyWealthTier[] AllWealthTiers =
            (IntercolonyWealthTier[])Enum.GetValues(typeof(IntercolonyWealthTier));

        private static IntercolonyArchetype RollArchetype(TechLevel tech)
        {
            // Tech level gates plausibility rather than forbidding outcomes (§9, §50).
            bool preIndustrial = tech <= TechLevel.Medieval;

            // Weighted so no archetype is impossible at any tech level, but the obviously
            // wrong ones are rare. Tribal only reads as tribal below industrial; a spacer
            // "tribal" settlement would just be confusing.
            float[] weights = new float[AllArchetypes.Length];
            weights[(int)IntercolonyArchetype.Agricultural] = preIndustrial ? 2.5f : 1.5f;
            weights[(int)IntercolonyArchetype.Industrial] = preIndustrial ? 0.4f : 2.0f;
            weights[(int)IntercolonyArchetype.Military] = 1.2f;
            weights[(int)IntercolonyArchetype.Affluent] = preIndustrial ? 0.5f : 1.2f;
            weights[(int)IntercolonyArchetype.Frontier] = 1.5f;
            weights[(int)IntercolonyArchetype.Tribal] = preIndustrial ? 2.0f : 0.1f;
            weights[(int)IntercolonyArchetype.TradeHub] = preIndustrial ? 0.6f : 1.3f;
            weights[(int)IntercolonyArchetype.Mixed] = 1.5f;

            return AllArchetypes[PickIndexByWeight(weights)];
        }

        /// <summary>
        /// Weighted index pick. Rand.ElementByWeight only goes up to six option/weight pairs,
        /// and there are more archetypes than that. Must be called inside a pushed Rand state.
        /// </summary>
        private static int PickIndexByWeight(float[] weights)
        {
            float total = 0f;
            for (int i = 0; i < weights.Length; i++)
            {
                total += Mathf.Max(0f, weights[i]);
            }

            if (total <= 0f)
            {
                IntercolonyLog.Warning("PickIndexByWeight got no positive weights; defaulting to index 0.");
                return 0;
            }

            float roll = Rand.Range(0f, total);
            float running = 0f;
            for (int i = 0; i < weights.Length; i++)
            {
                running += Mathf.Max(0f, weights[i]);
                if (roll < running)
                {
                    return i;
                }
            }

            // Floating-point drift can land past the final boundary.
            return weights.Length - 1;
        }

        private static IntercolonyWealthTier RollWealth(IntercolonyArchetype archetype, TechLevel tech)
        {
            float rich = 1f;
            float poor = 1f;

            switch (archetype)
            {
                case IntercolonyArchetype.Affluent:
                    rich = 4f;
                    poor = 0.3f;
                    break;
                case IntercolonyArchetype.TradeHub:
                    rich = 2.5f;
                    poor = 0.5f;
                    break;
                case IntercolonyArchetype.Frontier:
                case IntercolonyArchetype.Tribal:
                    rich = 0.3f;
                    poor = 3f;
                    break;
                case IntercolonyArchetype.Industrial:
                    rich = 1.5f;
                    break;
            }

            // Higher tech skews wealthier in absolute silver terms.
            if (tech >= TechLevel.Spacer)
            {
                rich *= 1.8f;
                poor *= 0.6f;
            }
            else if (tech <= TechLevel.Neolithic)
            {
                rich *= 0.4f;
                poor *= 1.8f;
            }

            float[] weights = new float[AllWealthTiers.Length];
            weights[(int)IntercolonyWealthTier.Destitute] = poor;
            weights[(int)IntercolonyWealthTier.Modest] = 2f;
            weights[(int)IntercolonyWealthTier.Comfortable] = 2f;
            weights[(int)IntercolonyWealthTier.Wealthy] = rich;

            return AllWealthTiers[PickIndexByWeight(weights)];
        }

        private static float RollQualityPreference(IntercolonyArchetype archetype, IntercolonyWealthTier wealth)
        {
            float baseValue;
            switch (archetype)
            {
                case IntercolonyArchetype.Affluent:
                    baseValue = 0.75f;
                    break;
                case IntercolonyArchetype.Military:
                    baseValue = 0.6f;
                    break;
                case IntercolonyArchetype.Frontier:
                case IntercolonyArchetype.Tribal:
                    baseValue = 0.25f;
                    break;
                default:
                    baseValue = 0.45f;
                    break;
            }

            // Poorer settlements care about price before craftsmanship.
            baseValue += (int)wealth * 0.05f - 0.075f;
            return Mathf.Clamp01(baseValue + Rand.Range(-0.12f, 0.12f));
        }

        private static float RollLaborModifier(IntercolonyArchetype archetype, IntercolonyWealthTier wealth)
        {
            // Placeholder until the labor market exists (§96 "labor tendency placeholder").
            // Rough intuition: poor and agrarian places have spare hands, rich ones do not.
            float baseValue;
            switch (archetype)
            {
                case IntercolonyArchetype.Agricultural:
                case IntercolonyArchetype.Frontier:
                    baseValue = 1.3f;
                    break;
                case IntercolonyArchetype.Affluent:
                    baseValue = 0.7f;
                    break;
                case IntercolonyArchetype.Military:
                    baseValue = 0.85f;
                    break;
                default:
                    baseValue = 1f;
                    break;
            }

            if (wealth <= IntercolonyWealthTier.Modest)
            {
                baseValue += 0.2f;
            }

            return Mathf.Max(0.1f, baseValue + Rand.Range(-0.15f, 0.15f));
        }

        /// <summary>
        /// Fills demand and supply weights. Archetype sets the shape, tech level constrains
        /// what can plausibly be *produced* (§50: a tribal settlement should not routinely
        /// supply fabrication benches), and volatility adds per-settlement jitter so two
        /// settlements of the same archetype still differ.
        /// </summary>
        private static void FillWeights(SettlementEconomicProfile profile)
        {
            foreach (IntercolonyProductCategory category in IntercolonyProductCategoryUtility.All)
            {
                int i = (int)category;
                profile.demandWeights[i] = Jitter(BaseDemand(profile.archetype, category) * WealthDemandFactor(profile.wealthTier, category), profile.volatility);
                profile.supplyWeights[i] = Jitter(BaseSupply(profile.archetype, category) * TechSupplyFactor(profile.techTier, category), profile.volatility);
            }
        }

        private static float Jitter(float value, float volatility)
        {
            // Weights are relative, so a floor rather than zero: nothing is truly impossible,
            // it is just improbable (§9 "influence probabilities, not hard restrictions").
            return Mathf.Max(0.02f, value * Rand.Range(1f - volatility, 1f + volatility));
        }

        private static float BaseDemand(IntercolonyArchetype archetype, IntercolonyProductCategory category)
        {
            switch (archetype)
            {
                case IntercolonyArchetype.Agricultural:
                    // Grows food, needs tools and machinery.
                    return Pick(category, 0.4f, 1.2f, 1.0f, 0.8f, 1.3f, 0.4f);
                case IntercolonyArchetype.Industrial:
                    // Eats raw inputs, sells finished goods.
                    return Pick(category, 1.5f, 1.3f, 0.6f, 0.5f, 0.9f, 0.4f);
                case IntercolonyArchetype.Military:
                    return Pick(category, 1.0f, 1.0f, 1.6f, 0.4f, 1.4f, 0.2f);
                case IntercolonyArchetype.Affluent:
                    return Pick(category, 0.8f, 0.6f, 1.2f, 1.6f, 0.9f, 2.0f);
                case IntercolonyArchetype.Frontier:
                    // Needs everything, can afford little.
                    return Pick(category, 1.2f, 1.4f, 1.3f, 0.9f, 1.1f, 0.2f);
                case IntercolonyArchetype.Tribal:
                    return Pick(category, 0.9f, 0.7f, 1.2f, 0.5f, 0.3f, 0.4f);
                case IntercolonyArchetype.TradeHub:
                    // Buys broadly to resell.
                    return Pick(category, 1.2f, 1.2f, 1.2f, 1.1f, 1.1f, 1.1f);
                default:
                    return 1f;
            }
        }

        private static float BaseSupply(IntercolonyArchetype archetype, IntercolonyProductCategory category)
        {
            switch (archetype)
            {
                case IntercolonyArchetype.Agricultural:
                    return Pick(category, 2.0f, 0.6f, 0.4f, 0.3f, 0.1f, 0.2f);
                case IntercolonyArchetype.Industrial:
                    return Pick(category, 0.6f, 2.0f, 1.6f, 1.2f, 1.4f, 0.3f);
                case IntercolonyArchetype.Military:
                    return Pick(category, 0.4f, 0.6f, 1.5f, 0.3f, 1.0f, 0.2f);
                case IntercolonyArchetype.Affluent:
                    return Pick(category, 0.3f, 0.5f, 0.9f, 1.2f, 0.8f, 1.8f);
                case IntercolonyArchetype.Frontier:
                    return Pick(category, 1.3f, 0.4f, 0.3f, 0.2f, 0.1f, 0.3f);
                case IntercolonyArchetype.Tribal:
                    return Pick(category, 1.6f, 0.3f, 0.4f, 0.4f, 0.05f, 0.9f);
                case IntercolonyArchetype.TradeHub:
                    return Pick(category, 1.1f, 1.1f, 1.1f, 1.0f, 1.0f, 1.0f);
                default:
                    return 1f;
            }
        }

        /// <summary>
        /// Tech gate on production (DESIGN.md §50). Applied to supply far more harshly than
        /// demand: a neolithic settlement plainly cannot manufacture a fabrication bench,
        /// whereas wanting advanced goods it cannot make is perfectly plausible.
        /// </summary>
        private static float TechSupplyFactor(TechLevel tech, IntercolonyProductCategory category)
        {
            if (category != IntercolonyProductCategory.CapitalEquipment &&
                category != IntercolonyProductCategory.ManufacturedGoods &&
                category != IntercolonyProductCategory.IntermediateGoods)
            {
                return 1f;
            }

            switch (tech)
            {
                case TechLevel.Animal:
                case TechLevel.Neolithic:
                    return category == IntercolonyProductCategory.CapitalEquipment ? 0.02f : 0.25f;
                case TechLevel.Medieval:
                    return category == IntercolonyProductCategory.CapitalEquipment ? 0.2f : 0.6f;
                case TechLevel.Industrial:
                    return 1f;
                case TechLevel.Spacer:
                case TechLevel.Ultra:
                case TechLevel.Archotech:
                    return 1.3f;
                default:
                    // Unreachable: NormalizeTech resolves Undefined before this point.
                    return 1f;
            }
        }

        private static float WealthDemandFactor(IntercolonyWealthTier wealth, IntercolonyProductCategory category)
        {
            // Discretionary categories scale with purchasing power; necessities barely move.
            bool discretionary = category == IntercolonyProductCategory.ArtAndUnique ||
                                 category == IntercolonyProductCategory.Furniture ||
                                 category == IntercolonyProductCategory.CapitalEquipment;
            if (!discretionary)
            {
                return 1f;
            }

            switch (wealth)
            {
                case IntercolonyWealthTier.Destitute: return 0.25f;
                case IntercolonyWealthTier.Modest: return 0.6f;
                case IntercolonyWealthTier.Comfortable: return 1f;
                default: return 1.6f;
            }
        }

        /// <summary>Positional lookup, one value per category in enum order. Keeps the tables above readable.</summary>
        private static float Pick(
            IntercolonyProductCategory category,
            float commodities,
            float intermediate,
            float manufactured,
            float furniture,
            float capital,
            float art)
        {
            switch (category)
            {
                case IntercolonyProductCategory.Commodities: return commodities;
                case IntercolonyProductCategory.IntermediateGoods: return intermediate;
                case IntercolonyProductCategory.ManufacturedGoods: return manufactured;
                case IntercolonyProductCategory.Furniture: return furniture;
                case IntercolonyProductCategory.CapitalEquipment: return capital;
                default: return art;
            }
        }
    }
}
