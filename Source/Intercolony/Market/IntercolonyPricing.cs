using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>One named multiplier in a price calculation, for the §47 breakdown.</summary>
    public struct PriceFactor
    {
        public string label;
        public float multiplier;

        public PriceFactor(string label, float multiplier)
        {
            this.label = label;
            this.multiplier = multiplier;
        }
    }

    /// <summary>
    /// The single place transaction prices are computed (DESIGN.md §46: "Centralize price
    /// logic. Do not scatter pricing formulas across UI and transaction state code.").
    ///
    /// Starts from RimWorld's own <c>BaseMarketValue</c> rather than inventing a value model,
    /// then layers economic context on top, as §46 prescribes.
    /// </summary>
    public static class IntercolonyPricing
    {
        /// <summary>
        /// Quantity at which saturation has fully bitten. Beyond roughly this many units of
        /// one good, a single settlement stops paying a premium (§13).
        /// </summary>
        private const float SaturationSpan = 2000f;

        /// <summary>Premium a settlement pays on its very first units, before saturation.</summary>
        private const float SaturationBest = 1.22f;

        /// <summary>Multiplier once demand is thoroughly saturated.</summary>
        private const float SaturationWorst = 0.96f;

        /// <summary>
        /// Unit price for a lot, plus the factors that produced it.
        ///
        /// <paramref name="distanceTiles"/> may be negative when the player has no home tile,
        /// in which case the distance factor is skipped rather than guessed.
        /// </summary>
        public static float UnitPrice(
            ThingDef def,
            int quantity,
            SettlementEconomicProfile profile,
            IntercolonyProductCategory category,
            float distanceTiles,
            QualityCategory? minQuality,
            out List<PriceFactor> factors)
        {
            return UnitPrice(def, null, quantity, profile, category, distanceTiles, minQuality, out factors);
        }

        /// <summary>
        /// Material-aware overload (DESIGN.md §101 "material-aware valuation"). A plasteel
        /// longsword and a wooden one are not the same product, and
        /// <see cref="ThingDef.BaseMarketValue"/> ignores stuff entirely — it is
        /// <c>GetStatValueAbstract(MarketValue)</c> with no material — so pricing off it
        /// would quote the same silver for both.
        /// </summary>
        public static float UnitPrice(
            ThingDef def,
            ThingDef stuff,
            int quantity,
            SettlementEconomicProfile profile,
            IntercolonyProductCategory category,
            float distanceTiles,
            QualityCategory? minQuality,
            out List<PriceFactor> factors)
        {
            factors = new List<PriceFactor>();

            float baseValue = BaseValue(def, stuff);

            // Local appetite for this category. Weights sit around 1.0, so this is already a
            // multiplier; clamped so an extreme roll cannot produce a silly price.
            float demand = Mathf.Clamp(profile.DemandFor(category), 0.4f, 2.0f);
            factors.Add(new PriceFactor("Local demand", demand));

            float wealth = WealthFactor(profile.wealthTier);
            factors.Add(new PriceFactor("Buyer wealth", wealth));

            float saturation = SaturationFactor(quantity);
            factors.Add(new PriceFactor("Lot size", saturation));

            if (distanceTiles >= 0f)
            {
                float distance = DistanceFactor(distanceTiles);
                factors.Add(new PriceFactor("Distance", distance));
            }

            // Only goods that can actually carry a quality rating. Applying a buyer's
            // craftsmanship preference to chemfuel or raw meat is meaningless and shows up in
            // the §47 breakdown as an unexplainable line the player cannot act on.
            if (CanHaveQuality(def))
            {
                float quality = QualityPremium(profile);
                if (!Mathf.Approximately(quality, 1f))
                {
                    factors.Add(new PriceFactor("Quality expectations", quality));
                }

                // A quality floor is a real constraint on the seller — it narrows what can be
                // delivered and may mean discarding usable stock — so the buyer pays for it.
                if (minQuality.HasValue)
                {
                    factors.Add(new PriceFactor(
                        $"Requires {minQuality.Value.GetLabel()}+", MinQualityPremium(minQuality.Value)));
                }
            }

            float total = baseValue;
            foreach (PriceFactor factor in factors)
            {
                total *= factor.multiplier;
            }

            // Never offer less than a token amount, or the lot reads as insulting.
            return Mathf.Max(0.01f, total);
        }

        /// <summary>
        /// Marginal demand decay (DESIGN.md §13). Prevents one nearby settlement from becoming
        /// an infinite premium sink: the more you ship in a single lot, the worse the unit price.
        /// Continuous rather than tiered, so there is no cliff to game.
        /// </summary>
        public static float SaturationFactor(int quantity)
        {
            float t = Mathf.Clamp01(quantity / SaturationSpan);
            return Mathf.Lerp(SaturationBest, SaturationWorst, t);
        }

        private static float WealthFactor(IntercolonyWealthTier wealth)
        {
            switch (wealth)
            {
                case IntercolonyWealthTier.Destitute: return 0.85f;
                case IntercolonyWealthTier.Modest: return 0.95f;
                case IntercolonyWealthTier.Comfortable: return 1.05f;
                default: return 1.2f;
            }
        }

        /// <summary>
        /// Distant buyers pay a little more, because the player is absorbing the haul
        /// (DESIGN.md §48: distance should create regional economics, and far settlements must
        /// not become useless). Capped so the far side of the planet is not a money printer.
        /// </summary>
        private static float DistanceFactor(float tiles)
        {
            return 1f + Mathf.Min(tiles, 120f) * 0.0015f;
        }

        /// <summary>
        /// Whether the def can carry a quality rating at all. Def-driven via
        /// <see cref="CompQuality"/> rather than a category guess, so modded quality-bearing
        /// items are handled without Intercolony knowing about them (§63).
        /// </summary>
        public static bool CanHaveQuality(ThingDef def)
        {
            return def != null && def.HasComp(typeof(CompQuality));
        }

        /// <summary>
        /// Market value including material, falling back to the stuffless base when the def
        /// is not made from stuff or no material was specified.
        /// </summary>
        public static float BaseValue(ThingDef def, ThingDef stuff)
        {
            if (def == null)
            {
                return 0f;
            }

            if (stuff != null && def.MadeFromStuff)
            {
                return def.GetStatValueAbstract(StatDefOf.MarketValue, stuff);
            }

            return def.BaseMarketValue;
        }

        /// <summary>
        /// Settlements that care about craftsmanship pay a premium on goods where
        /// craftsmanship is a real property. Only applied when <see cref="CanHaveQuality"/>.
        /// </summary>
        private static float QualityPremium(SettlementEconomicProfile profile)
        {
            return 1f + (profile.qualityPreference - 0.5f) * 0.1f;
        }

        /// <summary>
        /// What a buyer pays extra for insisting on a quality floor. Scales steeply, because
        /// each step up is markedly rarer to produce.
        /// </summary>
        private static float MinQualityPremium(QualityCategory minQuality)
        {
            switch (minQuality)
            {
                case QualityCategory.Awful:
                case QualityCategory.Poor:
                    return 1f;
                case QualityCategory.Normal:
                    return 1.1f;
                case QualityCategory.Good:
                    return 1.35f;
                case QualityCategory.Excellent:
                    return 1.8f;
                case QualityCategory.Masterwork:
                    return 2.6f;
                default:
                    return 4f;
            }
        }

        /// <summary>
        /// Renders the §47 breakdown: base value, each named factor as a percentage, and the
        /// resulting offer. Prices should not feel arbitrary.
        /// </summary>
        public static string Explain(ThingDef def, int quantity, float unitPrice, List<PriceFactor> factors)
        {
            return Explain(def, null, quantity, unitPrice, factors);
        }

        /// <summary>
        /// Material-aware breakdown. The base line must show the value actually used, or the
        /// factors below it will not reconstruct the quoted price and the whole explanation
        /// stops being trustworthy (§47).
        /// </summary>
        public static string Explain(
            ThingDef def, ThingDef stuff, int quantity, float unitPrice, List<PriceFactor> factors)
        {
            StringBuilder sb = new StringBuilder();
            string baseLabel = stuff != null && def.MadeFromStuff
                ? $"Base value ({stuff.label})"
                : "Base value";
            sb.AppendLine($"{baseLabel,-25} {BaseValue(def, stuff),10:F2}");
            foreach (PriceFactor factor in factors)
            {
                float percent = (factor.multiplier - 1f) * 100f;
                sb.AppendLine($"{factor.label,-25} {percent,+9:F1}%");
            }

            sb.AppendLine("-----------------------------------");
            sb.AppendLine($"Unit price                {unitPrice,10:F2}");
            sb.AppendLine($"x {quantity} units          {Mathf.RoundToInt(unitPrice * quantity),10}");
            return sb.ToString();
        }
    }
}
