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
        /// The first positive milestone is deliberately at +25: it is far enough from neutral
        /// that one ordinary delivery cannot call an unknown product Established by accident.
        /// </summary>
        public const float EstablishedThreshold = 25f;

        /// <summary>
        /// Respected is +50 so a product must accumulate a second, clearly stronger body of
        /// evidence before it leaves the first positive band.
        /// </summary>
        public const float RespectedThreshold = 50f;

        /// <summary>
        /// Renowned is +75, reserving the top quarter of the bounded score for a product with a
        /// durable exceptional record rather than a merely good run.
        /// </summary>
        public const float RenownedThreshold = 75f;

        /// <summary>
        /// The first negative milestone is -25: below that point the colony has more than a
        /// neutral amount of poor evidence against this exact product.
        /// </summary>
        public const float QuestionableThreshold = -25f;

        /// <summary>
        /// Poor reputation is -50, mirroring the distance of Respected from neutral on the
        /// positive side and keeping the milestone scale symmetric.
        /// </summary>
        public const float PoorReputationThreshold = -50f;

        /// <summary>
        /// Notorious is -75, reserving the bottom quarter of the bounded score for a product
        /// whose negative delivery history is genuinely severe.
        /// </summary>
        public const float NotoriousThreshold = -75f;

        /// <summary>
        /// A crossing must clear one point on both sides of a boundary. This deadband filters the
        /// fractional score noise produced by small deliveries without persisting another band
        /// field into saves.
        /// </summary>
        public const float BrandMilestoneHysteresis = 1f;

        internal const string EstablishedBandLabel = "Established";
        internal const string RespectedBandLabel = "Respected";
        internal const string RenownedBandLabel = "Renowned";
        internal const string QuestionableBandLabel = "Questionable";
        internal const string PoorReputationBandLabel = "Poor reputation";
        internal const string NotoriousBandLabel = "Notorious";

        /// <summary>
        /// Returns the player-facing band for a bounded direct score. The neutral middle is not a
        /// band: showing a row for it would turn the sparse reputation summary into a list of
        /// products that have not yet earned a reputation worth naming.
        /// </summary>
        internal static string BandNameFor(float directScore)
        {
            if (float.IsNaN(directScore))
            {
                return null;
            }

            if (directScore >= RenownedThreshold)
            {
                return RenownedBandLabel;
            }

            if (directScore >= RespectedThreshold)
            {
                return RespectedBandLabel;
            }

            if (directScore >= EstablishedThreshold)
            {
                return EstablishedBandLabel;
            }

            if (directScore <= NotoriousThreshold)
            {
                return NotoriousBandLabel;
            }

            if (directScore <= PoorReputationThreshold)
            {
                return PoorReputationBandLabel;
            }

            if (directScore <= QuestionableThreshold)
            {
                return QuestionableBandLabel;
            }

            return null;
        }

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
            string ignoredCrossedBand;
            bool ignoredCrossedUpward;
            return ApplyDeliveredQuality(
                state, product, deliveredQuality,
                out ignoredCrossedBand, out ignoredCrossedUpward);
        }

        /// <summary>
        /// Applies delivered-quality evidence and reports a transient milestone crossing to the
        /// exactly-once completion boundary. The caller owns the timeline write so it can attach
        /// the real sale's settlement and order; this method never writes history by itself.
        /// </summary>
        internal static ProductBrandRecord ApplyDeliveredQuality(
            IntercolonyWorldComponent state,
            ThingDef product,
            DeliveredQualityResult deliveredQuality,
            out string crossedBand,
            out bool crossedUpward)
        {
            crossedBand = null;
            crossedUpward = false;

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

            float previousScore = record.directScore;
            int deliveredUnits = deliveredQuality.QualityEvidenceUnits;
            float residual = Mathf.Exp(-deliveredUnits / DeliveredVolumeScale);

            // This is the same composition property documented by MarketPressureService:
            // exp(-a/K) * exp(-b/K) equals exp(-(a+b)/K). The obvious concave per-delivery delta
            // is wrong here because it is subadditive (f(a) + f(b) > f(a+b)), so splitting one
            // shipment into many orders would move the brand farther than one shipment.
            record.directScore = deliveredQuality.QualityTarget +
                (record.directScore - deliveredQuality.QualityTarget) * residual;

            TryGetCrossedBand(
                previousScore, record.directScore,
                out crossedBand, out crossedUpward);

            record.evidenceWeight += deliveredUnits;
            record.unitsDelivered += deliveredUnits;
            return record;
        }

        private static void TryGetCrossedBand(
            float previousScore, float updatedScore,
            out string crossedBand, out bool crossedUpward)
        {
            crossedBand = null;
            crossedUpward = false;

            // The record does not persist its current band. Instead, a boundary is accepted only
            // when the pre-update score is at least one deadband-width on the old side and the
            // post-update score is at least one deadband-width on the new side. A brand hovering
            // around a threshold therefore never satisfies alternating directions on successive
            // fractional deliveries, which prevents one milestone from spamming the timeline.
            if (updatedScore > previousScore)
            {
                if (CrossedUpward(previousScore, updatedScore, NotoriousThreshold))
                {
                    crossedBand = NotoriousBandLabel;
                    crossedUpward = true;
                    return;
                }

                if (CrossedUpward(previousScore, updatedScore, PoorReputationThreshold))
                {
                    crossedBand = PoorReputationBandLabel;
                    crossedUpward = true;
                    return;
                }

                if (CrossedUpward(previousScore, updatedScore, QuestionableThreshold))
                {
                    crossedBand = QuestionableBandLabel;
                    crossedUpward = true;
                    return;
                }

                if (CrossedUpward(previousScore, updatedScore, EstablishedThreshold))
                {
                    crossedBand = EstablishedBandLabel;
                    crossedUpward = true;
                    return;
                }

                if (CrossedUpward(previousScore, updatedScore, RespectedThreshold))
                {
                    crossedBand = RespectedBandLabel;
                    crossedUpward = true;
                    return;
                }

                if (CrossedUpward(previousScore, updatedScore, RenownedThreshold))
                {
                    crossedBand = RenownedBandLabel;
                    crossedUpward = true;
                    return;
                }
            }
            else if (updatedScore < previousScore)
            {
                if (CrossedDownward(previousScore, updatedScore, RenownedThreshold))
                {
                    crossedBand = RenownedBandLabel;
                    return;
                }

                if (CrossedDownward(previousScore, updatedScore, RespectedThreshold))
                {
                    crossedBand = RespectedBandLabel;
                    return;
                }

                if (CrossedDownward(previousScore, updatedScore, EstablishedThreshold))
                {
                    crossedBand = EstablishedBandLabel;
                    return;
                }

                if (CrossedDownward(previousScore, updatedScore, QuestionableThreshold))
                {
                    crossedBand = QuestionableBandLabel;
                    return;
                }

                if (CrossedDownward(previousScore, updatedScore, PoorReputationThreshold))
                {
                    crossedBand = PoorReputationBandLabel;
                    return;
                }

                if (CrossedDownward(previousScore, updatedScore, NotoriousThreshold))
                {
                    crossedBand = NotoriousBandLabel;
                }
            }
        }

        private static bool CrossedUpward(
            float previousScore, float updatedScore, float threshold)
        {
            return previousScore <= threshold - BrandMilestoneHysteresis &&
                   updatedScore >= threshold + BrandMilestoneHysteresis;
        }

        private static bool CrossedDownward(
            float previousScore, float updatedScore, float threshold)
        {
            return previousScore >= threshold + BrandMilestoneHysteresis &&
                   updatedScore <= threshold - BrandMilestoneHysteresis;
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
