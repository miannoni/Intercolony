using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Turns settlement economic profiles into non-binding demand (DESIGN.md §11, §97).
    ///
    /// Runs on the coarse refresh, never per tick (§59, §84). Rolls are seeded per
    /// (economy seed, settlement, refresh number) inside a pushed Rand state, so generation
    /// is reproducible for debugging and cannot disturb the global random stream (§60).
    /// </summary>
    public static class MarketOpportunityGenerator
    {
        /// <summary>Most opportunities one settlement will have outstanding at once.</summary>
        public const int MaxPerSettlement = 3;

        /// <summary>Chance a given eligible settlement posts anything on a given refresh.</summary>
        private const float PostChance = 0.35f;

        private const int MinDeadlineDays = 6;
        private const int MaxDeadlineDays = 20;

        /// <summary>
        /// Generates opportunities for one settlement. Returns an empty list if the settlement
        /// already has enough outstanding, or if the dice say nothing this cycle.
        /// </summary>
        public static List<MarketOpportunity> GenerateFor(
            Settlement settlement,
            SettlementEconomicProfile profile,
            int economySeed,
            int refreshNumber,
            int existingCount,
            System.Func<int> idAllocator)
        {
            List<MarketOpportunity> created = new List<MarketOpportunity>();
            if (profile == null || existingCount >= MaxPerSettlement)
            {
                return created;
            }

            // A faction at war with the player does not post purchase orders (§51).
            if (!IntercolonyMarketAccess.IsAccessible(settlement))
            {
                return created;
            }

            // Seeded on the refresh number as well as the settlement, so each cycle differs
            // but any given cycle can be reproduced.
            // Salted so opportunity rolls never coincide with the profile rolls, which are
            // seeded from the same economy seed and settlement ID.
            int seed = Gen.HashCombineInt(economySeed, settlement.ID, refreshNumber, 0x0F1E);
            float distance = DistanceToPlayer(settlement);

            Rand.PushState(seed);
            try
            {
                if (Rand.Value > PostChance)
                {
                    return created;
                }

                int wanted = Mathf.Min(Rand.RangeInclusive(1, 2), MaxPerSettlement - existingCount);
                for (int i = 0; i < wanted; i++)
                {
                    MarketOpportunity opportunity = CreateOne(settlement, profile, distance, idAllocator);
                    if (opportunity != null)
                    {
                        created.Add(opportunity);
                    }
                }
            }
            finally
            {
                Rand.PopState();
            }

            return created;
        }

        /// <summary>Must be called inside a pushed Rand state.</summary>
        private static MarketOpportunity CreateOne(
            Settlement settlement,
            SettlementEconomicProfile profile,
            float distance,
            System.Func<int> idAllocator)
        {
            IntercolonyProductCategory category = PickCategory(profile);
            List<ThingDef> candidates = IntercolonyProductClassifier.DefsInCategory(category);
            if (candidates.Count == 0)
            {
                return null;
            }

            ThingDef def = candidates[Rand.Range(0, candidates.Count)];

            ThingDef stuff = PickStuff(def, profile);
            int quantity = PickQuantity(def, stuff, profile);
            QualityCategory? minQuality = PickMinimumQuality(def, profile);

            float unitPrice = IntercolonyPricing.UnitPrice(
                def, stuff, quantity, profile, category, distance, minQuality,
                out List<PriceFactor> factors);

            int deadlineDays = Rand.RangeInclusive(MinDeadlineDays, MaxDeadlineDays);
            int lifespanDays = Rand.RangeInclusive(3, 10);

            return new MarketOpportunity
            {
                id = idAllocator(),
                settlementId = settlement.ID,
                settlementName = settlement.Label ?? "unnamed",
                thingDef = def,
                quantity = quantity,
                unitPrice = unitPrice,
                createdTick = GenTicks.TicksGame,
                expiryTick = GenTicks.TicksGame + lifespanDays * GenDate.TicksPerDay,
                deadlineDays = deadlineDays,
                distanceTiles = distance,
                minQuality = minQuality,
                stuffDef = stuff,
                state = MarketOpportunityState.Available,
                priceExplanation = IntercolonyPricing.Explain(def, quantity, unitPrice, factors)
            };
        }

        /// <summary>
        /// Picks a category weighted by what the settlement actually wants (§9: archetypes
        /// influence probabilities, they are not hard restrictions). Categories Phase 4 cannot
        /// trade yet — furniture, capital equipment, art — are skipped, because they need the
        /// unique-item path from §23.2 / §24.
        /// </summary>
        private static IntercolonyProductCategory PickCategory(SettlementEconomicProfile profile)
        {
            // Phase 8 (§101): furniture, capital equipment and art are now normal market
            // participants. Only minifiable buildings reach this point — the classifier
            // filters the rest — so anything demanded here can physically be delivered.
            IntercolonyProductCategory[] tradable = IntercolonyProductCategoryUtility.All;

            float total = 0f;
            foreach (IntercolonyProductCategory category in tradable)
            {
                total += Mathf.Max(0.01f, profile.DemandFor(category));
            }

            float roll = Rand.Range(0f, total);
            float running = 0f;
            foreach (IntercolonyProductCategory category in tradable)
            {
                running += Mathf.Max(0.01f, profile.DemandFor(category));
                if (roll < running)
                {
                    return category;
                }
            }

            return IntercolonyProductCategory.Commodities;
        }

        /// <summary>
        /// A minimum quality demand, or null when the buyer does not care (§11's quality-order
        /// example, §99 quality constraints).
        ///
        /// Only for goods that can actually carry quality, and weighted by the settlement's
        /// quality preference so an affluent buyer is the one asking for Excellent work.
        /// Deliberately capped below Legendary: a demand nobody can reliably fill is not
        /// interesting, it is just an offer that never gets taken.
        ///
        /// Must be called inside a pushed Rand state.
        /// </summary>
        private static QualityCategory? PickMinimumQuality(ThingDef def, SettlementEconomicProfile profile)
        {
            if (!IntercolonyPricing.CanHaveQuality(def))
            {
                return null;
            }

            // Even a picky buyer often just wants the thing, not a showpiece.
            float demandChance = 0.25f + profile.qualityPreference * 0.5f;
            if (Rand.Value > demandChance)
            {
                return null;
            }

            // Higher preference shifts the floor upward.
            float roll = Rand.Value * profile.qualityPreference;
            if (roll > 0.55f)
            {
                return QualityCategory.Excellent;
            }

            if (roll > 0.32f)
            {
                return QualityCategory.Good;
            }

            return QualityCategory.Normal;
        }

        /// <summary>
        /// A required material, or null for "any" (DESIGN.md §101 material-aware valuation,
        /// §11's "Preferred material: Marble" example).
        ///
        /// Most buyers do not care. When one does, the material is drawn from what the def can
        /// actually be made of, so the demand is always fillable — asking for a plasteel chair
        /// when the def forbids plasteel would be an offer nobody can take.
        ///
        /// Must be called inside a pushed Rand state.
        /// </summary>
        private static ThingDef PickStuff(ThingDef def, SettlementEconomicProfile profile)
        {
            if (def == null || !def.MadeFromStuff)
            {
                return null;
            }

            // Pickier settlements specify a material more often.
            float chance = 0.15f + profile.qualityPreference * 0.35f;
            if (Rand.Value > chance)
            {
                return null;
            }

            List<ThingDef> options = new List<ThingDef>();
            foreach (ThingDef candidate in GenStuff.AllowedStuffsFor(def))
            {
                // Only materials the player could plausibly obtain and the buyer value.
                if (candidate.BaseMarketValue > 0f)
                {
                    options.Add(candidate);
                }
            }

            if (options.Count == 0)
            {
                return null;
            }

            return options[Rand.Range(0, options.Count)];
        }

        /// <summary>
        /// Quantity scaled so the lot is worth a plausible amount of silver rather than a
        /// fixed unit count — 1,200 corn and 1,200 components are wildly different asks (§11).
        /// </summary>
        private static int PickQuantity(ThingDef def, ThingDef stuff, SettlementEconomicProfile profile)
        {
            float targetSilver = Rand.Range(400f, 3000f) * WealthScale(profile.wealthTier);
            float unitValue = Mathf.Max(0.4f, IntercolonyPricing.BaseValue(def, stuff));
            int quantity = Mathf.RoundToInt(targetSilver / unitValue);

            // Crated goods travel one per crate and carry real caravan mass, so a lot that is
            // reasonable in silver can still be impossible to haul. Cap them hard
            // (docs/unique-goods-spike.md: "keep unique-good lot sizes small").
            if (def.category == ThingCategory.Building)
            {
                return Mathf.Clamp(quantity, 1, 8);
            }

            // Single-stack items — weapons, apparel — are bulky too, just less so.
            if (def.stackLimit <= 1)
            {
                return Mathf.Clamp(quantity, 1, 15);
            }

            // Keep lots in a sane band: never a token handful, never an unshippable mountain.
            quantity = Mathf.Clamp(quantity, 5, 5000);

            // Round to something a human would ask for.
            if (quantity > 100)
            {
                quantity = Mathf.RoundToInt(quantity / 25f) * 25;
            }
            else if (quantity > 20)
            {
                quantity = Mathf.RoundToInt(quantity / 5f) * 5;
            }

            return Mathf.Max(1, quantity);
        }

        private static float WealthScale(IntercolonyWealthTier wealth)
        {
            switch (wealth)
            {
                case IntercolonyWealthTier.Destitute: return 0.45f;
                case IntercolonyWealthTier.Modest: return 0.75f;
                case IntercolonyWealthTier.Comfortable: return 1f;
                default: return 1.6f;
            }
        }

        /// <summary>
        /// Approximate tiles between the player's home and this settlement, or -1 when the
        /// player has no home tile yet (§48).
        /// </summary>
        public static float DistanceToPlayer(Settlement settlement)
        {
            Map home = Find.AnyPlayerHomeMap;
            if (home == null || Find.WorldGrid == null)
            {
                return -1f;
            }

            return Find.WorldGrid.ApproxDistanceInTiles(home.Tile, settlement.Tile);
        }
    }
}
