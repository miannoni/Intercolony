using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Answers what quality the market currently expects from the colony for one product.
    ///
    /// This is a derived read model. It never creates, mutates or prunes a
    /// <see cref="ProductBrandRecord"/> because inherited reputation must not turn a tooltip read
    /// into a new persisted fact (docs/INTERCOLONY_1_0_IMPLEMENTATION_PLAN.md §§4.2 and 4.5).
    /// </summary>
    public static class EffectiveBrandService
    {
        /// <summary>
        /// Evidence-weight units at which direct confidence reaches 63.2% (1 - e^-1). Three such
        /// scales reach about 95%, so one delivery is not proof while a hundredth contributes very
        /// little. An exponential is used because residual uncertainty composes multiplicatively:
        /// splitting the same evidence across deliveries gives the same confidence as one batch,
        /// instead of a concave per-delivery delta creating a threshold or a split-delivery exploit.
        /// </summary>
        public const float DirectEvidenceConfidenceScale = 10f;

        /// <summary>
        /// Returns the effective product brand, or neutral when the state or product is unknown.
        /// The sparse list is scanned directly so this hot read does not allocate a working list,
        /// closure or LINQ iterator.
        /// </summary>
        public static float GetEffectiveBrand(
            IntercolonyWorldComponent state, ThingDef targetProduct)
        {
            return GetEffectiveBrand(state?.ProductBrandRecords, targetProduct);
        }

        private static float GetEffectiveBrand(
            List<ProductBrandRecord> records, ThingDef targetProduct)
        {
            if (records == null || targetProduct == null)
            {
                return ProductBrandRecord.Neutral;
            }

            ProductBrandRecord directRecord = null;
            float inheritedBrand = ProductBrandRecord.Neutral;
            float strongestInheritedMagnitude = 0f;

            for (int i = 0; i < records.Count; i++)
            {
                ProductBrandRecord record = records[i];
                if (record == null || record.thingDef == null)
                {
                    continue;
                }

                if (ReferenceEquals(record.thingDef, targetProduct))
                {
                    directRecord = record;
                    continue;
                }

                float similarity = ProductSimilarityService.GetSimilarity(
                    record.thingDef, targetProduct);
                float inheritedCandidate = Mathf.Clamp(
                    record.directScore * similarity,
                    ProductBrandRecord.MinScore,
                    ProductBrandRecord.MaxScore);
                float inheritedMagnitude = Mathf.Abs(inheritedCandidate);

                // Keep one strongest signed signal rather than adding every related score. This is
                // the trap that would turn +70 revolvers, +65 rifles and +60 pistols into +195 on a
                // fourth firearm. Comparing magnitude also lets a strongly negative inherited
                // signal survive instead of treating only positive reputation as real evidence.
                if (inheritedMagnitude > strongestInheritedMagnitude)
                {
                    strongestInheritedMagnitude = inheritedMagnitude;
                    inheritedBrand = inheritedCandidate;
                }
            }

            if (directRecord == null)
            {
                return inheritedBrand;
            }

            float confidence = DirectEvidenceConfidence(directRecord.evidenceWeight);

            // Lerp is signed, so negative direct evidence receives exactly the same authority as
            // positive evidence. As confidence approaches one, the direct score takes control;
            // therefore a terrible product cannot remain hidden behind a positive related brand.
            return Mathf.Clamp(
                Mathf.Lerp(inheritedBrand, directRecord.directScore, confidence),
                ProductBrandRecord.MinScore,
                ProductBrandRecord.MaxScore);
        }

        private static float DirectEvidenceConfidence(float evidenceWeight)
        {
            if (evidenceWeight <= 0f || float.IsNaN(evidenceWeight))
            {
                return 0f;
            }

            return Mathf.Clamp01(
                1f - Mathf.Exp(-evidenceWeight / DirectEvidenceConfidenceScale));
        }
    }
}
