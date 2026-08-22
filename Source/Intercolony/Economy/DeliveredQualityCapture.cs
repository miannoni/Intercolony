using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// The quality evidence captured from the real Things in one handoff. A zero target with
    /// zero evidence units means "nothing here could report quality", not a Normal-quality sale.
    ///
    /// This remains a value rather than a brand record because this slice only preserves the
    /// evidence at the fulfillment boundary. The next slice decides how direct brand evidence
    /// should consume it.
    /// </summary>
    public readonly struct DeliveredQualityResult
    {
        public readonly float QualityTarget;
        public readonly int QualityEvidenceUnits;

        public bool HasQualityEvidence => QualityEvidenceUnits > 0;

        public DeliveredQualityResult(float qualityTarget, int qualityEvidenceUnits)
        {
            QualityTarget = qualityEvidenceUnits > 0 ? qualityTarget : 0f;
            QualityEvidenceUnits = Math.Max(0, qualityEvidenceUnits);
        }

        public static DeliveredQualityResult NoEvidence =>
            new DeliveredQualityResult(0f, 0);
    }

    /// <summary>
    /// Computes quality evidence from the Things that are actually handed over. It deliberately
    /// has no minimum-quality or advertised-quality input: those describe the request, while
    /// this service must describe only what survived into the handoff.
    /// </summary>
    public static class DeliveredQualityCapture
    {
        /// <summary>
        /// Awful is the bottom of the brand scale, so it supplies the strongest negative target
        /// without allowing a result to fall below the record's -100 floor.
        /// </summary>
        public const float AwfulQualityTarget = -100f;

        /// <summary>
        /// Poor is negative evidence, but it is intentionally less severe than Awful so one
        /// below-par batch does not erase every distinction between poor and disastrous work.
        /// </summary>
        public const float PoorQualityTarget = -60f;

        /// <summary>
        /// Normal is approximately neutral because ordinary acceptable work should not move an
        /// unknown product's reputation merely by existing.
        /// </summary>
        public const float NormalQualityTarget = 0f;

        /// <summary>
        /// Good is meaningfully positive rather than a tiny step above neutral, rewarding a
        /// colony that consistently delivers work better than ordinary.
        /// </summary>
        public const float GoodQualityTarget = 35f;

        /// <summary>
        /// Excellent is clear positive craftsmanship evidence, while remaining below the two
        /// exceptional tiers so the upper end of the scale retains room to matter.
        /// </summary>
        public const float ExcellentQualityTarget = 65f;

        /// <summary>
        /// Masterwork is strongly positive evidence and therefore sits close to the exceptional
        /// ceiling rather than being averaged into an unremarkable middle value.
        /// </summary>
        public const float MasterworkQualityTarget = 90f;

        /// <summary>
        /// Legendary is the strongest named quality and uses the maximum positive target that
        /// the bounded brand scale can represent.
        /// </summary>
        public const float LegendaryQualityTarget = 100f;

        /// <summary>Starts a batch accumulator for the actual Things in a handoff.</summary>
        public static DeliveredQualityBatch BeginBatch()
        {
            return new DeliveredQualityBatch();
        }

        /// <summary>
        /// Computes a unit-weighted result from the supplied Things. Each Thing contributes its
        /// actual stack count, so a caller must pass the split handoff piece when only part of a
        /// source stack was delivered.
        /// </summary>
        public static DeliveredQualityResult FromThings(IEnumerable<Thing> things)
        {
            DeliveredQualityBatch batch = BeginBatch();
            if (things != null)
            {
                foreach (Thing thing in things)
                {
                    batch.Add(thing);
                }
            }

            return batch.Result;
        }

        /// <summary>
        /// Resolves the plan's named quality mapping. A future enum value must fail loudly here
        /// rather than silently being scored as Normal evidence and hiding a tuning/API change.
        /// The seven current values and the CompQuality.Quality member were checked against the
        /// RimWorld 1.6 reference/decompiled sources before using this switch.
        /// </summary>
        public static float QualityTargetFor(QualityCategory quality)
        {
            switch (quality)
            {
                case QualityCategory.Awful:
                    return AwfulQualityTarget;
                case QualityCategory.Poor:
                    return PoorQualityTarget;
                case QualityCategory.Normal:
                    return NormalQualityTarget;
                case QualityCategory.Good:
                    return GoodQualityTarget;
                case QualityCategory.Excellent:
                    return ExcellentQualityTarget;
                case QualityCategory.Masterwork:
                    return MasterworkQualityTarget;
                case QualityCategory.Legendary:
                    return LegendaryQualityTarget;
                default:
                    throw new ArgumentOutOfRangeException(nameof(quality), quality,
                        "RimWorld returned an unknown QualityCategory.");
            }
        }
    }

    /// <summary>
    /// Accumulates actual quality-bearing units without averaging distinct Thing objects as if
    /// each object represented one unit. Fulfillment paths add their split piece before they
    /// destroy it, which preserves the evidence after the source inventory has changed.
    /// </summary>
    public sealed class DeliveredQualityBatch
    {
        private float weightedTargetTotal;
        private int qualityEvidenceUnits;

        public int QualityEvidenceUnits => qualityEvidenceUnits;

        /// <summary>Gets the current unit-weighted result, or no evidence when nothing qualifies.</summary>
        public DeliveredQualityResult Result
        {
            get
            {
                if (qualityEvidenceUnits <= 0)
                {
                    return DeliveredQualityResult.NoEvidence;
                }

                return new DeliveredQualityResult(
                    weightedTargetTotal / qualityEvidenceUnits,
                    qualityEvidenceUnits);
            }
        }

        /// <summary>
        /// Adds all countable units in one actual Thing. QualityUtility.TryGetQuality is used
        /// instead of reading its out value blindly: RimWorld deliberately writes Normal to the
        /// out parameter when no CompQuality exists, and that value is not craftsmanship evidence.
        /// </summary>
        public void Add(Thing thing)
        {
            Add(thing, CountableUnits(thing));
        }

        /// <summary>
        /// Adds an explicit number of units represented by an actual Thing. Fulfillment uses the
        /// implicit stack count overload; the explicit form keeps tests and future handoff code
        /// honest when a caller has already measured a bounded unit count.
        /// </summary>
        public void Add(Thing thing, int units)
        {
            if (thing == null || units <= 0 ||
                !thing.TryGetQuality(out QualityCategory quality))
            {
                // Raw goods and other Things without CompQuality contribute no evidence in
                // either direction. Treating QualityUtility's fallback out value (Normal) as a
                // score would turn "cannot report quality" into a misleading neutral opinion.
                return;
            }

            float target = DeliveredQualityCapture.QualityTargetFor(quality);
            weightedTargetTotal += target * units;
            qualityEvidenceUnits += units;
        }

        private static int CountableUnits(Thing thing)
        {
            if (thing == null)
            {
                return 0;
            }

            // QualityUtility.TryGetQuality handles a MinifiedThing by looking through its inner
            // Thing. A minified building is still one delivered unit, not its wrapper count.
            return thing is MinifiedThing ? 1 : thing.stackCount;
        }
    }
}
