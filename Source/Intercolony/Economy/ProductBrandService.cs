using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Consumes actual delivered-quality evidence at the completed-sale boundary. This is the
    /// write side of product brand; EffectiveBrandService remains a read-only derived view.
    /// </summary>
    public static class ProductBrandService
    {
        /// <summary>
        /// Quality-bearing units over which a sale moves about 63.2% of the remaining distance to
        /// its batch target. This is conservative balance tuning: a small crafted sale should be
        /// visible without becoming instant proof, while a large shipment can still teach the
        /// market clearly. Retune in play; the self-test asserts direction, composition and bounds.
        /// </summary>
        public const float DeliveredVolumeScale = 20f;

        /// <summary>
        /// Applies one completed sale's actual quality result to the exact product record.
        /// </summary>
        public static ProductBrandRecord ApplyDeliveredQuality(
            IntercolonyWorldComponent state,
            ThingDef product,
            DeliveredQualityResult deliveredQuality)
        {
            if (state == null || product == null || !deliveredQuality.HasQualityEvidence)
            {
                // Bulk goods and any other handoff without a quality component say nothing about
                // craftsmanship. In particular, do not create a sparse record for that absence.
                return null;
            }

            ProductBrandRecord record = FindRecord(state, product);
            if (record == null)
            {
                // The sparse list records the first real delivered evidence, including neutral
                // Normal evidence, rather than pre-populating every possible ThingDef.
                record = new ProductBrandRecord(product);
                state.ProductBrandRecords.Add(record);
            }

            int deliveredUnits = deliveredQuality.QualityEvidenceUnits;
            float residual = Mathf.Exp(-deliveredUnits / DeliveredVolumeScale);

            // This is the same composition property documented by MarketPressureService:
            // exp(-a/K) * exp(-b/K) equals exp(-(a+b)/K). The obvious concave per-delivery delta
            // is wrong here because it is subadditive (f(a) + f(b) > f(a+b)), so splitting one
            // shipment into many orders would move the brand farther than one shipment.
            record.directScore = deliveredQuality.QualityTarget +
                (record.directScore - deliveredQuality.QualityTarget) * residual;
            record.evidenceWeight += deliveredUnits;
            record.unitsDelivered += deliveredUnits;
            return record;
        }

        private static ProductBrandRecord FindRecord(
            IntercolonyWorldComponent state, ThingDef product)
        {
            for (int i = 0; i < state.ProductBrandRecords.Count; i++)
            {
                ProductBrandRecord record = state.ProductBrandRecords[i];
                if (record != null && ReferenceEquals(record.thingDef, product))
                {
                    return record;
                }
            }

            return null;
        }
    }
}
