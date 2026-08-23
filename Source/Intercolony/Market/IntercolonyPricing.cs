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
        /// The lowest multiplier a -100 brand may apply to a newly computed price. A 25% discount
        /// makes a bad reputation matter while keeping the product sellable; the positive floor is
        /// also a guard against the trap where a sufficiently bad reputation turns payment into a
        /// negative number. This is deliberately named so the late balance pass can retune it
        /// without rewriting the brand calculation.
        /// </summary>
        public const float BrandMinimumMultiplier = 0.75f;

        /// <summary>
        /// The highest multiplier a +100 brand may apply to a newly computed price. A 30% premium
        /// is economically exciting, but the bound keeps a respected product from becoming an
        /// infinite-profit arbitrage route. This is deliberately named so the late balance pass
        /// can retune it without changing the pricing owner or its factor-row contract.
        /// </summary>
        public const float BrandMaximumMultiplier = 1.30f;

        /// <summary>Label for the prospective brand premium or discount in a price breakdown.</summary>
        public const string BrandFactorLabel = "Brand strength";

        // A specification promises only its stated constraints. When a term is unspecified,
        // the seller may fulfil it with the cheapest eligible animal, so the buyer pays only
        // for the value guaranteed by the promise. These are owner-tunable balance values.
        private const float UnspecifiedOrMaleSexFactor = 1f;
        private const float FemaleBreedingValueFactor = 1.20f;
        private const float PregnancyNotRequiredFactor = 1f;
        private const float PregnancyRequiredFactor = 1.40f;

        /// <summary>
        /// Unit price for a lot, plus the factors that produced it.
        ///
        /// <paramref name="distanceTiles"/> may be negative when the player has no home tile,
        /// in which case the distance factor is skipped rather than guessed.
        /// </summary>
        public static float UnitPrice(
            IntercolonyWorldComponent state,
            ThingDef def,
            int quantity,
            SettlementEconomicProfile profile,
            IntercolonyProductCategory category,
            float distanceTiles,
            QualityCategory? minQuality,
            out List<PriceFactor> factors)
        {
            return UnitPrice(state, def, null, quantity, profile, category, distanceTiles, minQuality, out factors);
        }

        /// <summary>
        /// Material-aware overload (DESIGN.md §101 "material-aware valuation"). A plasteel
        /// longsword and a wooden one are not the same product, and
        /// <see cref="ThingDef.BaseMarketValue"/> ignores stuff entirely — it is
        /// <c>GetStatValueAbstract(MarketValue)</c> with no material — so pricing off it
        /// would quote the same silver for both.
        /// </summary>
        public static float UnitPrice(
            IntercolonyWorldComponent state,
            ThingDef def,
            ThingDef stuff,
            int quantity,
            SettlementEconomicProfile profile,
            IntercolonyProductCategory category,
            float distanceTiles,
            QualityCategory? minQuality,
            out List<PriceFactor> factors)
        {
            return UnitPrice(
                state, def, stuff, (AnimalSpec)null, quantity, profile, category,
                distanceTiles, minQuality, out factors);
        }

        /// <summary>
        /// Immediate known-inventory valuation. A live Thing carries RimWorld's material and
        /// quality-aware MarketValue, so a direct sale can price the object the buyer is actually
        /// being offered rather than pretending every item of the same ThingDef is identical.
        /// </summary>
        internal static float UnitPrice(
            IntercolonyWorldComponent state,
            ThingDef def,
            ThingDef stuff,
            Thing actualThing,
            int quantity,
            SettlementEconomicProfile profile,
            IntercolonyProductCategory category,
            float distanceTiles,
            QualityCategory? minQuality,
            out List<PriceFactor> factors)
        {
            return UnitPrice(
                state, def, stuff, null, actualThing, quantity, profile, category,
                distanceTiles, minQuality, out factors);
        }

        /// <summary>
        /// Animal-aware overload. A non-null specification replaces only the base-unit
        /// derivation; all settlement, saturation, distance and difficulty factors remain shared.
        /// Animals never enter material or quality valuation.
        /// </summary>
        public static float UnitPrice(
            IntercolonyWorldComponent state,
            ThingDef def,
            ThingDef stuff,
            AnimalSpec animalSpec,
            int quantity,
            SettlementEconomicProfile profile,
            IntercolonyProductCategory category,
            float distanceTiles,
            QualityCategory? minQuality,
            out List<PriceFactor> factors)
        {
            return UnitPrice(
                state, def, stuff, animalSpec, null, quantity, profile, category,
                distanceTiles, minQuality, out factors);
        }

        private static float UnitPrice(
            IntercolonyWorldComponent state,
            ThingDef def,
            ThingDef stuff,
            AnimalSpec animalSpec,
            Thing actualThing,
            int quantity,
            SettlementEconomicProfile profile,
            IntercolonyProductCategory category,
            float distanceTiles,
            QualityCategory? minQuality,
            out List<PriceFactor> factors)
        {
            factors = new List<PriceFactor>();

            bool isAnimalPrice = animalSpec != null;
            if (isAnimalPrice &&
                !animalSpec.TryValidateFor(def, requireKind: false, out string validationReason))
            {
                IntercolonyLog.Error(
                    $"Animal unit price for race {def?.defName ?? "<null>"} is zero because validation failed: {validationReason}.");
                return 0f;
            }

            float baseValue;
            if (isAnimalPrice)
            {
                // Deliberately start from the species definition, not a pawn. The animal
                // factors below turn that into the specification value without any generation.
                baseValue = def.BaseMarketValue;
                AddAnimalSpecificationFactors(def, animalSpec, factors);
            }
            else
            {
                baseValue = BaseValue(def, stuff, actualThing);
            }

            // Brand is a prospective expectation about a price being computed now. Read the
            // effective view once and add its named row here so UI and order code cannot multiply
            // the same premium separately, and so an accepted order's stored price is untouched.
            float effectiveBrand = EffectiveBrandService.GetEffectiveBrand(state, def);
            if (!Mathf.Approximately(effectiveBrand, ProductBrandRecord.Neutral))
            {
                factors.Add(BrandFactorFor(effectiveBrand));
            }

            // The category supplies the settlement's broad economic character; the good-specific
            // perturbation keeps that character from making every item in the category rank alike.
            List<PriceFactor> demandRows =
                EffectiveEconomyService.ExplainDemand(state, profile, def, category);
            float effectiveDemand = demandRows.Count == 0 ? 0f : 1f;
            foreach (PriceFactor row in demandRows)
            {
                effectiveDemand *= row.multiplier;
            }

            float clampedDemand = Mathf.Clamp(effectiveDemand, 0.4f, 2.0f);

            // The service's rows are the effective demand, not modifiers to apply on top of it.
            // Pricing owns the separate sanity clamp, so when a condition exists its displayed
            // multiplier is reconciled to the price actually charged. If the base row is inside
            // the bound, clamping can only pull the product back toward that base; the adjusted
            // ratio therefore keeps the condition's direction and never labels a shortage as a
            // reduction or a surplus as an increase. A base already outside the bound cannot make
            // that promise, so that genuinely contradictory case stays collapsed to one row.
            if (demandRows.Count == 1)
            {
                factors.Add(new PriceFactor("Local demand", clampedDemand));
            }
            else if (demandRows.Count > 1 && demandRows[0].multiplier > 0f &&
                     demandRows[0].multiplier >= 0.4f && demandRows[0].multiplier <= 2.0f)
            {
                PriceFactor baseRow = demandRows[0];
                PriceFactor conditionRow = demandRows[1];
                factors.Add(baseRow);
                factors.Add(new PriceFactor(
                    conditionRow.label, clampedDemand / baseRow.multiplier));
            }
            else
            {
                factors.Add(new PriceFactor("Local demand", clampedDemand));
            }

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
            if (!isAnimalPrice && CanHaveQuality(def))
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

            factors.Add(SellingEconomyDifficultyFactor());

            float total = baseValue;
            foreach (PriceFactor factor in factors)
            {
                total *= factor.multiplier;
            }

            // Never offer less than a token amount, or the lot reads as insulting.
            return Mathf.Max(0.01f, total);
        }

        /// <summary>
        /// Converts effective brand strength into the bounded price factor used by
        /// <see cref="UnitPrice(IntercolonyWorldComponent, ThingDef, ThingDef, AnimalSpec, int,
        /// SettlementEconomicProfile, IntercolonyProductCategory, float, QualityCategory?, out
        /// List{PriceFactor})"/>. The two sides are interpolated separately so zero remains exactly
        /// x1.00 instead of drifting toward the midpoint of asymmetric bounds.
        /// </summary>
        public static PriceFactor BrandFactorFor(float effectiveBrand)
        {
            if (float.IsNaN(effectiveBrand))
            {
                effectiveBrand = ProductBrandRecord.Neutral;
            }

            float clampedBrand = Mathf.Clamp(
                effectiveBrand, ProductBrandRecord.MinScore, ProductBrandRecord.MaxScore);
            float multiplier = clampedBrand >= ProductBrandRecord.Neutral
                ? Mathf.Lerp(
                    1f, BrandMaximumMultiplier,
                    clampedBrand / ProductBrandRecord.MaxScore)
                : Mathf.Lerp(
                    1f, BrandMinimumMultiplier,
                    clampedBrand / ProductBrandRecord.MinScore);
            return new PriceFactor(BrandFactorLabel, multiplier);
        }

        /// <summary>
        /// Rounds one agreed sale amount. The market surface and the binding order both call this
        /// owner so a counteroffer cannot advertise one total and pay another after acceptance.
        /// Keeping the rounding here avoids repeating the old Find Buyer trap in another dialog.
        /// </summary>
        public static int TotalPayment(float unitPrice, int quantity)
        {
            if (quantity <= 0 || float.IsNaN(unitPrice) || float.IsInfinity(unitPrice))
            {
                return 0;
            }

            return Mathf.RoundToInt(unitPrice * quantity);
        }

        /// <summary>
        /// Re-prices an existing offer for a different lot size.
        ///
        /// A buyer's rate is not flat: §13 saturation means the first units are worth more to
        /// them, so committing to 50 of a 200-unit offer earns a *better* unit price than the
        /// advertised one. Freezing the rate would have shown the player an unchanging
        /// "2.50 each" while the slider moved, which is both wrong and confusing.
        ///
        /// Scaling down is always safe: the total still falls, because quantity drops faster
        /// than the unit rate rises. That is why the confirmation slider only ever reduces.
        /// </summary>
        public static float RepriceForQuantity(
            IntercolonyWorldComponent state,
            MarketOpportunity opportunity,
            SettlementEconomicProfile profile,
            int quantity,
            out List<PriceFactor> factors)
        {
            factors = new List<PriceFactor>();
            if (opportunity?.thingDef == null)
            {
                return 0f;
            }

            // No profile — settlement gone or unreachable — means no basis to re-price, so
            // fall back to the advertised rate rather than inventing one.
            if (profile == null)
            {
                return opportunity.unitPrice;
            }

            IntercolonyProductCategory category =
                IntercolonyProductClassifier.Classify(opportunity.thingDef)
                ?? IntercolonyProductCategory.Commodities;

            float price = UnitPrice(
                state,
                opportunity.thingDef,
                opportunity.stuffDef,
                Mathf.Max(1, quantity),
                profile,
                category,
                opportunity.distanceTiles,
                opportunity.minQuality,
                out factors);

            PriceFactor logistics = LogisticsFactor(opportunity.fulfillment);
            factors.Add(logistics);
            return price * logistics.multiplier;
        }

        /// <summary>
        /// Logistics pricing modifier (DESIGN.md §105, §25.1/§25.2).
        ///
        /// The whole point of offering two modes is that they differ in more than wording.
        /// Seller delivery pays a premium because the player absorbs the caravan time, travel
        /// and risk; buyer pickup pays less because the counterparty does. Applied as a named
        /// factor so the §47 breakdown shows the player exactly what the convenience costs.
        /// </summary>
        public static PriceFactor LogisticsFactor(FulfillmentMode mode)
        {
            switch (mode)
            {
                case FulfillmentMode.BuyerPickup:
                    return new PriceFactor("Buyer collects", 0.85f);
                default:
                    return new PriceFactor("You deliver", 1.12f);
            }
        }

        /// <summary>
        /// The player's global difficulty setting on sales. Added while a price is formed, never
        /// when a stored order is read back, so an agreed amount cannot move underneath an
        /// obligation.
        /// </summary>
        /// <summary>
        /// What a difficulty of 100% actually means. The slider used to sit at 1.0 and the
        /// resulting spread was wide enough to arbitrage — buy from one settlement, sell to
        /// another, profit on the difference. At this baseline the arbitrage still exists but
        /// is worth a silver or two on a major order, which is noise rather than an income.
        /// </summary>
        public const float EconomyDifficultyBaseline = 1.35f;

        /// <summary>
        /// Floor on what a buyer will pay. Without it the selling factor goes negative once the
        /// effective difficulty passes 2.0, which the baseline above brings within reach of the
        /// slider's own maximum — and a negative multiplier would pay the player to lose goods.
        /// </summary>
        private const float MinimumSellingFactor = 0.25f;

        public static float EffectiveEconomyDifficulty =>
            IntercolonyMod.Settings.economyDifficulty * EconomyDifficultyBaseline;

        public static PriceFactor SellingEconomyDifficultyFactor()
        {
            // Higher difficulty must squeeze both sides of the player's economy instead of
            // inflating every price and cancelling itself out against procurement.
            return new PriceFactor(
                "Economy difficulty (selling)",
                Mathf.Max(MinimumSellingFactor, 2f - EffectiveEconomyDifficulty));
        }

        /// <summary>The player's global difficulty setting on supplier quotations.</summary>
        public static PriceFactor BuyingEconomyDifficultyFactor()
        {
            return new PriceFactor(
                "Economy difficulty (buying)", EffectiveEconomyDifficulty);
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

        private static float BaseValue(ThingDef def, ThingDef stuff, Thing actualThing)
        {
            Thing valueThing = actualThing?.GetInnerIfMinified();
            if (valueThing != null && !valueThing.Destroyed && valueThing.def == def)
            {
                float marketValue = valueThing.MarketValue;
                if (marketValue > 0f && !float.IsNaN(marketValue) &&
                    !float.IsInfinity(marketValue))
                {
                    return marketValue;
                }
            }

            return BaseValue(def, stuff);
        }

        /// <summary>
        /// Definition-only animal value: species base times the guaranteed specification
        /// multipliers. A null specification is exactly the existing goods path.
        /// </summary>
        public static float BaseValue(ThingDef def, ThingDef stuff, AnimalSpec animalSpec)
        {
            if (animalSpec == null)
            {
                return BaseValue(def, stuff);
            }

            if (!animalSpec.TryValidateFor(def, requireKind: false, out string validationReason))
            {
                IntercolonyLog.Error(
                    $"Animal base value for race {def?.defName ?? "<null>"} is zero because validation failed: {validationReason}.");
                return 0f;
            }

            float value = def.BaseMarketValue;
            List<PriceFactor> animalFactors = new List<PriceFactor>();
            AddAnimalSpecificationFactors(def, animalSpec, animalFactors);
            foreach (PriceFactor factor in animalFactors)
            {
                value *= factor.multiplier;
            }

            return value;
        }

        private static void AddAnimalSpecificationFactors(
            ThingDef race, AnimalSpec spec, List<PriceFactor> factors)
        {
            float lifeStageFactor = spec.lifeStage != null
                ? spec.lifeStage.marketValueFactor
                : MinimumLifeStageFactor(race);
            string lifeStageLabel = spec.lifeStage != null
                ? $"Life stage ({spec.lifeStage.label})"
                : "Life stage (minimum guaranteed)";
            factors.Add(new PriceFactor(lifeStageLabel, lifeStageFactor));

            if (spec.gender.HasValue)
            {
                float sexFactor = spec.gender.Value == Gender.Female
                    ? FemaleBreedingValueFactor
                    : UnspecifiedOrMaleSexFactor;
                factors.Add(new PriceFactor(
                    $"Sex ({spec.gender.Value.GetLabel(animal: true)})", sexFactor));
            }

            if (spec.pregnant.HasValue)
            {
                factors.Add(new PriceFactor(
                    spec.pregnant.Value ? "Pregnancy required" : "Not pregnant",
                    spec.pregnant.Value ? PregnancyRequiredFactor : PregnancyNotRequiredFactor));
            }

            // minHealthFraction is intentionally an anti-exploit eligibility gate, not price
            // discovery in V1. Gestation progress likewise narrows fulfilment without changing
            // the single pregnancy premium promised by the specification.
        }

        private static float MinimumLifeStageFactor(ThingDef race)
        {
            float minimum = float.MaxValue;
            List<LifeStageAge> stages = race.race.lifeStageAges;
            if (stages != null)
            {
                foreach (LifeStageAge stage in stages)
                {
                    if (stage?.def != null && stage.def.marketValueFactor < minimum)
                    {
                        minimum = stage.def.marketValueFactor;
                    }
                }
            }

            // A malformed content definition with no stages has no factor to read. Preserve
            // the LifeStageDef code default rather than inventing a discount.
            return minimum == float.MaxValue ? 1f : minimum;
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
            return Explain(
                def, stuff, null, (AnimalSpec)null, quantity, unitPrice, factors);
        }

        /// <summary>Breakdown for a known live item, including its RimWorld market value.</summary>
        public static string Explain(
            ThingDef def,
            ThingDef stuff,
            Thing actualThing,
            int quantity,
            float unitPrice,
            List<PriceFactor> factors)
        {
            return Explain(def, stuff, actualThing, null, quantity, unitPrice, factors);
        }

        /// <summary>Breakdown that identifies the animal species base before spec multipliers.</summary>
        public static string Explain(
            ThingDef def,
            ThingDef stuff,
            AnimalSpec animalSpec,
            int quantity,
            float unitPrice,
            List<PriceFactor> factors)
        {
            return Explain(def, stuff, null, animalSpec, quantity, unitPrice, factors);
        }

        private static string Explain(
            ThingDef def,
            ThingDef stuff,
            Thing actualThing,
            AnimalSpec animalSpec,
            int quantity,
            float unitPrice,
            List<PriceFactor> factors)
        {
            StringBuilder sb = new StringBuilder();
            bool isAnimalPrice = animalSpec != null;
            string baseLabel = isAnimalPrice
                ? $"Species base ({def.label})"
                : stuff != null && def.MadeFromStuff
                    ? $"Base value ({stuff.label})"
                    : "Base value";
            float displayedBase = isAnimalPrice
                ? def.BaseMarketValue
                : BaseValue(def, stuff, actualThing);
            sb.AppendLine($"{baseLabel,-25} {displayedBase,10:F2}");
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
