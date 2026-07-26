using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>One settlement's appetite for a specific good (DESIGN.md §12).</summary>
    public class BuyerOffer
    {
        public Settlement settlement;
        public SettlementEconomicProfile profile;

        /// <summary>What the offer is for. Carried so creating the sale needs no extra context.</summary>
        public ThingDef def;

        public ThingDef stuff;

        /// <summary>Most units this settlement would take before saturating (§13).</summary>
        public int maxQuantity;

        /// <summary>Unit price for the quantity actually being offered.</summary>
        public float unitPrice;

        /// <summary>Units this offer covers — the lesser of what is held and what is wanted.</summary>
        public int quantity;

        public float distanceTiles;

        /// <summary>Set when the settlement will not buy this at all, with the reason shown to the player.</summary>
        public string noInterestReason;

        public bool Interested => noInterestReason == null && maxQuantity > 0;

        public int TotalPrice => Mathf.RoundToInt(unitPrice * quantity);

        /// <summary>Price factor breakdown, for the §47 tooltip.</summary>
        public List<PriceFactor> factors = new List<PriceFactor>();
    }

    /// <summary>
    /// "I already have a huge surplus. Who wants it?" (DESIGN.md §12, Phase 9 §102).
    ///
    /// Deliberately does **not** search posted opportunities. A surplus rarely matches
    /// whatever a settlement happened to advertise, and §12's worked example shows demand
    /// bands and a "No current interest" row — that is latent appetite derived from
    /// settlement profiles, not a listing lookup. Searching listings would answer a much less
    /// useful question and would usually return nothing.
    /// </summary>
    public static class FindBuyerService
    {
        /// <summary>
        /// Below this category demand weight a settlement is simply not in the market for the
        /// good, and is reported as uninterested rather than quoted a derisory price.
        /// </summary>
        private const float InterestThreshold = 0.55f;

        /// <summary>
        /// Who would buy <paramref name="quantity"/> of this good, best offer first.
        /// Uninterested settlements are included but sort last: §12 shows them explicitly, and
        /// "nobody near you wants this" is a useful answer.
        /// </summary>
        public static List<BuyerOffer> FindBuyers(
            IntercolonyWorldComponent state,
            ThingDef def,
            ThingDef stuff,
            int quantity,
            bool includeUninterested = true)
        {
            List<BuyerOffer> offers = new List<BuyerOffer>();
            if (state == null || def == null || quantity <= 0)
            {
                return offers;
            }

            IntercolonyProductCategory? category = IntercolonyProductClassifier.Classify(def);
            if (!category.HasValue)
            {
                return offers;
            }

            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements == null)
            {
                return offers;
            }

            foreach (Settlement settlement in settlements)
            {
                if (!IntercolonyMarketAccess.IsAccessible(settlement, out string reason))
                {
                    continue;
                }

                SettlementEconomicProfile profile = state.GetProfile(settlement);
                if (profile == null)
                {
                    continue;
                }

                BuyerOffer offer = Evaluate(settlement, profile, def, stuff, category.Value, quantity);
                if (offer.Interested || includeUninterested)
                {
                    offers.Add(offer);
                }
            }

            offers.Sort(CompareOffers);
            return offers;
        }

        private static int CompareOffers(BuyerOffer a, BuyerOffer b)
        {
            // Interested first, then by what the player would actually receive.
            if (a.Interested != b.Interested)
            {
                return a.Interested ? -1 : 1;
            }

            int byTotal = b.TotalPrice.CompareTo(a.TotalPrice);
            if (byTotal != 0)
            {
                return byTotal;
            }

            // Equal money: prefer the shorter haul.
            float da = a.distanceTiles < 0f ? float.MaxValue : a.distanceTiles;
            float db = b.distanceTiles < 0f ? float.MaxValue : b.distanceTiles;
            return da.CompareTo(db);
        }

        private static BuyerOffer Evaluate(
            Settlement settlement,
            SettlementEconomicProfile profile,
            ThingDef def,
            ThingDef stuff,
            IntercolonyProductCategory category,
            int wantedQuantity)
        {
            BuyerOffer offer = new BuyerOffer
            {
                settlement = settlement,
                profile = profile,
                def = def,
                stuff = stuff,
                distanceTiles = MarketOpportunityGenerator.DistanceToPlayer(settlement)
            };

            float demand = profile.DemandFor(category);
            if (demand < InterestThreshold)
            {
                offer.noInterestReason = "no current interest";
                return offer;
            }

            offer.maxQuantity = MaxAppetite(def, stuff, profile, demand);
            if (offer.maxQuantity <= 0)
            {
                offer.noInterestReason = "cannot afford a worthwhile lot";
                return offer;
            }

            offer.quantity = Mathf.Min(wantedQuantity, offer.maxQuantity);
            offer.unitPrice = IntercolonyPricing.UnitPrice(
                def, stuff, offer.quantity, profile, category, offer.distanceTiles,
                null, out List<PriceFactor> factors);
            offer.factors = factors;

            return offer;
        }

        /// <summary>
        /// How much of this good a settlement would absorb, expressed in units rather than
        /// silver so the player can compare it against a stockpile (§12's "Demand: up to
        /// 2,000"). Bounded by wealth and appetite, then clamped by how the good travels —
        /// the same crated-goods reasoning as generation (docs/unique-goods-spike.md).
        /// </summary>
        private static int MaxAppetite(
            ThingDef def, ThingDef stuff, SettlementEconomicProfile profile, float demand)
        {
            float budget = WealthBudget(profile.wealthTier) * demand;
            float unitValue = Mathf.Max(0.4f, IntercolonyPricing.BaseValue(def, stuff));
            int units = Mathf.RoundToInt(budget / unitValue);

            if (def.category == ThingCategory.Building)
            {
                return Mathf.Clamp(units, 0, 8);
            }

            if (def.stackLimit <= 1)
            {
                return Mathf.Clamp(units, 0, 15);
            }

            return Mathf.Clamp(units, 0, 5000);
        }

        private static float WealthBudget(IntercolonyWealthTier wealth)
        {
            switch (wealth)
            {
                case IntercolonyWealthTier.Destitute: return 900f;
                case IntercolonyWealthTier.Modest: return 2200f;
                case IntercolonyWealthTier.Comfortable: return 4500f;
                default: return 9000f;
            }
        }

        /// <summary>
        /// Colony stock worth offering, as def -> count. Counts only what is in storage:
        /// loose items scattered across the map are not a surplus the player is choosing to
        /// sell, and including them would make the list unusable.
        /// </summary>
        public static List<KeyValuePair<ThingDef, int>> ColonyStock(Map map)
        {
            List<KeyValuePair<ThingDef, int>> result = new List<KeyValuePair<ThingDef, int>>();
            if (map == null)
            {
                return result;
            }

            Dictionary<ThingDef, int> counts = new Dictionary<ThingDef, int>();
            foreach (Thing thing in map.listerThings.AllThings)
            {
                Thing inner = thing.GetInnerIfMinified();
                if (inner?.def == null || !IntercolonyProductClassifier.IsFungibleTradeItem(inner.def))
                {
                    continue;
                }

                if (!thing.IsInAnyStorage())
                {
                    continue;
                }

                int units = OrderValidator.CountableUnits(thing);
                counts.TryGetValue(inner.def, out int existing);
                counts[inner.def] = existing + units;
            }

            foreach (KeyValuePair<ThingDef, int> entry in counts)
            {
                result.Add(entry);
            }

            result.Sort((a, b) => b.Value.CompareTo(a.Value));
            return result;
        }
    }
}
