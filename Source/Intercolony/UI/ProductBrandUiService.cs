using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Decides the small amount of product-brand content the UI is allowed to show. Keeping this
    /// read model separate from drawing makes the summary grouping, band naming and attribution
    /// testable without pretending that RimWorld's immediate-mode widgets are a unit-test surface.
    /// </summary>
    internal static class ProductBrandUiService
    {
        internal const string NoBrandEvidenceMessage =
            "No brand reputation yet — no goods have been delivered.";

        internal const string NoBrandMilestoneMessage =
            "No product has reached a brand milestone yet.";

        internal sealed class BrandSummary
        {
            internal readonly List<BrandSummaryRow> knownFor =
                new List<BrandSummaryRow>();
            internal readonly List<BrandSummaryRow> weakReputation =
                new List<BrandSummaryRow>();
            internal readonly string emptyState;

            internal BrandSummary(bool hasAnyRecord)
            {
                emptyState = hasAnyRecord
                    ? NoBrandMilestoneMessage
                    : NoBrandEvidenceMessage;
            }

            internal bool IsEmpty => knownFor.Count == 0 && weakReputation.Count == 0;
        }

        internal readonly struct BrandSummaryRow
        {
            internal readonly IntercolonyProductCategory category;
            internal readonly string bandName;
            internal readonly string tooltip;

            internal BrandSummaryRow(
                IntercolonyProductCategory category, string bandName)
                : this(category, bandName, null)
            {
            }

            internal BrandSummaryRow(
                IntercolonyProductCategory category,
                string bandName,
                string tooltip)
            {
                this.category = category;
                this.bandName = bandName;
                this.tooltip = tooltip;
            }
        }

        internal readonly struct SpecificGoodDetails
        {
            internal readonly float effectiveBrand;
            internal readonly string strengthLabel;
            internal readonly string attribution;
            internal readonly string tooltip;

            internal SpecificGoodDetails(
                float effectiveBrand,
                string strengthLabel,
                string attribution,
                string tooltip)
            {
                this.effectiveBrand = effectiveBrand;
                this.strengthLabel = strengthLabel;
                this.attribution = attribution;
                this.tooltip = tooltip;
            }
        }

        /// <summary>
        /// Groups only sparse direct records into the six player-recognisable categories. A category
        /// keeps its strongest positive and strongest negative evidence independently so a mixed
        /// workshop history does not hide a serious weakness behind a good product in the same
        /// bucket.
        /// </summary>
        internal static BrandSummary BuildSummary(IntercolonyWorldComponent state)
        {
            List<ProductBrandRecord> records = state?.ProductBrandRecords;
            BrandSummary summary = new BrandSummary(records != null && records.Count > 0);
            if (records == null || records.Count == 0)
            {
                return summary;
            }

            Dictionary<IntercolonyProductCategory, float> positive =
                new Dictionary<IntercolonyProductCategory, float>();
            Dictionary<IntercolonyProductCategory, float> negative =
                new Dictionary<IntercolonyProductCategory, float>();
            Dictionary<IntercolonyProductCategory, ThingDef> positiveProducts =
                new Dictionary<IntercolonyProductCategory, ThingDef>();
            Dictionary<IntercolonyProductCategory, ThingDef> negativeProducts =
                new Dictionary<IntercolonyProductCategory, ThingDef>();

            for (int i = 0; i < records.Count; i++)
            {
                ProductBrandRecord record = records[i];
                if (record == null || record.thingDef == null)
                {
                    continue;
                }

                IntercolonyProductCategory? category;
                try
                {
                    category = IntercolonyProductClassifier.Classify(record.thingDef);
                }
                catch (Exception)
                {
                    // A malformed modded def should make one row unavailable, not make the
                    // Business tab itself fail to draw for every other product.
                    continue;
                }

                if (!category.HasValue)
                {
                    continue;
                }

                string bandName = ProductBrandService.BandNameFor(record.directScore);
                if (bandName == null)
                {
                    continue;
                }

                if (record.directScore >= ProductBrandService.EstablishedThreshold)
                {
                    if (!positive.TryGetValue(category.Value, out float existing) ||
                        record.directScore > existing)
                    {
                        positive[category.Value] = record.directScore;
                        positiveProducts[category.Value] = record.thingDef;
                    }
                }
                else if (record.directScore <= ProductBrandService.QuestionableThreshold)
                {
                    if (!negative.TryGetValue(category.Value, out float existing) ||
                        record.directScore < existing)
                    {
                        negative[category.Value] = record.directScore;
                        negativeProducts[category.Value] = record.thingDef;
                    }
                }
            }

            foreach (IntercolonyProductCategory category in IntercolonyProductCategoryUtility.All)
            {
                if (positive.TryGetValue(category, out float positiveScore))
                {
                    ThingDef product = positiveProducts[category];
                    summary.knownFor.Add(new BrandSummaryRow(
                        category,
                        ProductBrandService.BandNameFor(positiveScore),
                        BuildBrandTooltip(state, product)));
                }

                if (negative.TryGetValue(category, out float negativeScore))
                {
                    ThingDef product = negativeProducts[category];
                    summary.weakReputation.Add(new BrandSummaryRow(
                        category,
                        ProductBrandService.BandNameFor(negativeScore),
                        BuildBrandTooltip(state, product)));
                }
            }

            return summary;
        }

        /// <summary>
        /// Builds the labelled values for a selected or sold good. The numeric value comes from
        /// EffectiveBrandService, while the second row describes which part of that same result
        /// carries more weight; it never reimplements the effective-brand equation.
        /// </summary>
        internal static SpecificGoodDetails BuildSpecificGoodDetails(
            IntercolonyWorldComponent state, ThingDef product)
        {
            EffectiveBrandService.EffectiveBrandDetails details =
                EffectiveBrandService.GetEffectiveBrandDetails(state, product);
            string attribution;

            if (details.mostlyInherited && details.inheritedFrom != null)
            {
                string sourceLabel = DisplayLabel(details.inheritedFrom);
                attribution = $"Mostly inherited from your {sourceLabel} reputation.";
            }
            else if (details.hasDirectRecord && details.inheritedFrom != null)
            {
                attribution = "Direct evidence is doing most of the work.";
            }
            else if (details.hasDirectRecord)
            {
                attribution = "Based on direct delivered-quality evidence.";
            }
            else
            {
                attribution = "No delivered-quality evidence yet.";
            }

            return new SpecificGoodDetails(
                details.effectiveBrand,
                StrengthLabel(details.effectiveBrand),
                attribution,
                BuildBrandTooltip(state, product));
        }

        /// <summary>
        /// One player-facing explanation for every product-brand band. The current multiplier is
        /// deliberately read from IntercolonyPricing so this text and the amount charged use the
        /// same calculation rather than two copies of the brand interpolation.
        /// </summary>
        internal static string BuildBrandTooltip(
            IntercolonyWorldComponent state, ThingDef product)
        {
            EffectiveBrandService.EffectiveBrandDetails details =
                EffectiveBrandService.GetEffectiveBrandDetails(state, product);
            PriceFactor currentPrice = IntercolonyPricing.BrandFactorFor(details.effectiveBrand);
            PriceFactor bestPrice = IntercolonyPricing.BrandFactorFor(ProductBrandRecord.MaxScore);
            PriceFactor worstPrice = IntercolonyPricing.BrandFactorFor(ProductBrandRecord.MinScore);

            int currentPosition = BrandLadderIndex(
                ProductBrandService.BandNameFor(details.effectiveBrand));
            string productLabel = DisplayLabel(product);
            string productFamily = ProductFamilyLabel(product);
            StringBuilder tooltip = new StringBuilder();

            tooltip.AppendLine(CurrentBandPositionLine(currentPosition));
            tooltip.AppendLine();
            for (int i = 0; i < BrandLadder.Length; i++)
            {
                if (i > 0)
                {
                    tooltip.Append(" · ");
                }

                if (i == currentPosition)
                {
                    tooltip.Append('[').Append(BrandLadder[i]).Append(']');
                }
                else
                {
                    tooltip.Append(BrandLadder[i]);
                }
            }

            tooltip.AppendLine();
            tooltip.AppendLine();
            tooltip.AppendLine(
                $"Buyers pay {MultiplierLabel(currentPrice.multiplier)} for your " +
                $"{productLabel} because of this standing.");
            tooltip.AppendLine(
                $"The best record reaches {MultiplierLabel(bestPrice.multiplier)}; the worst " +
                $"falls to {MultiplierLabel(worstPrice.multiplier)}.");
            tooltip.AppendLine();
            tooltip.AppendLine(
                $"It moves with the quality of what you actually deliver in {productFamily}.");
            tooltip.Append(
                "It affects price only - it does not change how often settlements ask for this " +
                "product.");

            AppendInheritanceLine(tooltip, details, productLabel);
            return tooltip.ToString();
        }

        private static readonly string[] BrandLadder =
        {
            ProductBrandService.NotoriousBandLabel,
            ProductBrandService.PoorReputationBandLabel,
            ProductBrandService.QuestionableBandLabel,
            "no reputation",
            ProductBrandService.EstablishedBandLabel,
            ProductBrandService.RespectedBandLabel,
            ProductBrandService.RenownedBandLabel
        };

        private static int BrandLadderIndex(string bandName)
        {
            if (string.IsNullOrEmpty(bandName))
            {
                return 3;
            }

            for (int i = 0; i < BrandLadder.Length; i++)
            {
                if (BrandLadder[i] == bandName)
                {
                    return i;
                }
            }

            return 3;
        }

        private static string CurrentBandPositionLine(int position)
        {
            if (position == 3)
            {
                return "No reputation yet.";
            }

            string bandName = BrandLadder[position];
            if (position > 3)
            {
                int positiveLevel = position - 3;
                return $"{bandName} - the {Ordinal(positiveLevel)} of three positive levels.";
            }

            int negativeLevel = 3 - position;
            return $"{bandName} - the {Ordinal(negativeLevel)} of three negative levels.";
        }

        private static string Ordinal(int value)
        {
            switch (value)
            {
                case 1: return "first";
                case 2: return "second";
                case 3: return "third";
                default: return value.ToString();
            }
        }

        private static string MultiplierLabel(float multiplier)
        {
            return $"{multiplier:F2}x";
        }

        private static string ProductFamilyLabel(ThingDef product)
        {
            if (product == null)
            {
                return "relevant";
            }

            try
            {
                IntercolonyProductCategory? category =
                    IntercolonyProductClassifier.Classify(product);
                return category.HasValue ? category.Value.Label() : "relevant";
            }
            catch (Exception)
            {
                return "relevant";
            }
        }

        private static void AppendInheritanceLine(
            StringBuilder tooltip,
            EffectiveBrandService.EffectiveBrandDetails details,
            string productLabel)
        {
            if (details.inheritedFrom == null || !details.mostlyInherited)
            {
                return;
            }

            string sourceLabel = DisplayLabel(details.inheritedFrom);
            tooltip.AppendLine();
            if (!details.hasDirectRecord)
            {
                tooltip.Append(
                    $"This standing is inherited from your {sourceLabel} reputation.");
                return;
            }

            tooltip.Append(
                $"This standing is mostly inherited from your {sourceLabel} reputation; " +
                $"your direct {productLabel} record also contributes.");
        }

        private static string StrengthLabel(float effectiveBrand)
        {
            int rounded = Mathf.RoundToInt(effectiveBrand);
            return rounded > 0 ? $"+{rounded}" : rounded.ToString();
        }

        private static string DisplayLabel(ThingDef def)
        {
            if (def == null)
            {
                return "related goods";
            }

            string label = def.LabelCap.ToString();
            return string.IsNullOrEmpty(label) ? def.defName : label;
        }
    }
}
