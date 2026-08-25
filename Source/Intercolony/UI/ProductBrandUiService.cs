using System;
using System.Collections.Generic;
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

            internal BrandSummaryRow(
                IntercolonyProductCategory category, string bandName)
            {
                this.category = category;
                this.bandName = bandName;
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
                    }
                }
                else if (record.directScore <= ProductBrandService.QuestionableThreshold)
                {
                    if (!negative.TryGetValue(category.Value, out float existing) ||
                        record.directScore < existing)
                    {
                        negative[category.Value] = record.directScore;
                    }
                }
            }

            foreach (IntercolonyProductCategory category in IntercolonyProductCategoryUtility.All)
            {
                if (positive.TryGetValue(category, out float positiveScore))
                {
                    summary.knownFor.Add(new BrandSummaryRow(
                        category,
                        ProductBrandService.BandNameFor(positiveScore)));
                }

                if (negative.TryGetValue(category, out float negativeScore))
                {
                    summary.weakReputation.Add(new BrandSummaryRow(
                        category,
                        ProductBrandService.BandNameFor(negativeScore)));
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
            string tooltip;

            if (details.mostlyInherited && details.inheritedFrom != null)
            {
                string sourceLabel = DisplayLabel(details.inheritedFrom);
                attribution = $"Mostly inherited from your {sourceLabel} reputation.";
                tooltip = ProductSimilarityService.Explain(details.inheritedFrom, product);
            }
            else if (details.hasDirectRecord && details.inheritedFrom != null)
            {
                attribution = "Direct evidence is doing most of the work.";
                tooltip = ProductSimilarityService.Explain(details.inheritedFrom, product);
            }
            else if (details.hasDirectRecord)
            {
                attribution = "Based on direct delivered-quality evidence.";
                tooltip = "This value is based on delivered-quality evidence for this exact good.";
            }
            else
            {
                attribution = "No delivered-quality evidence yet.";
                tooltip = "No direct or related delivered-quality evidence contributes to this good.";
            }

            return new SpecificGoodDetails(
                details.effectiveBrand,
                StrengthLabel(details.effectiveBrand),
                attribution,
                tooltip);
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
