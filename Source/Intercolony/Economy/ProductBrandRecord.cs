using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Direct, colony-wide product-brand evidence for one exact ThingDef (the 1.0 program
    /// Stage 4A/4B, docs/INTERCOLONY_1_0_IMPLEMENTATION_PLAN.md §§4.1-4.2).
    ///
    /// This is only the persisted record. It deliberately does not decide what quality means,
    /// derive reputation from similar products, or affect pricing; those are later slices. Keeping
    /// the record passive makes a save/load defect visible here rather than later as a balance bug.
    /// </summary>
    public class ProductBrandRecord : IExposable
    {
        /// <summary>
        /// The worst direct expectation the scale can represent. The floor belongs here so a
        /// future writer cannot make brand grow outside the player-facing -100..100 contract.
        /// </summary>
        public const float MinScore = -100f;

        /// <summary>
        /// No direct evidence yet. Zero is neutral because an unrecorded product is unknown, not
        /// evidence that the colony makes either poor or exceptional goods.
        /// </summary>
        public const float Neutral = 0f;

        /// <summary>
        /// The best direct expectation the scale can represent. The ceiling keeps a strong record
        /// exciting without allowing later accumulation logic to create an unbounded score.
        /// </summary>
        public const float MaxScore = 100f;

        /// <summary>Exact product whose delivered history this record describes.</summary>
        public ThingDef thingDef;

        private float directScoreValue = Neutral;

        /// <summary>
        /// Direct reputation for <see cref="thingDef"/>. The setter is the model's guardrail, so
        /// future score writers cannot bypass the -100..100 scale by forgetting a caller-side cap.
        /// </summary>
        public float directScore
        {
            get => directScoreValue;
            set => directScoreValue = ClampScore(value);
        }

        /// <summary>How much delivered exposure backs <see cref="directScore"/>.</summary>
        public float evidenceWeight;

        /// <summary>Total units actually delivered for this exact product.</summary>
        public int unitsDelivered;

        public ProductBrandRecord()
        {
        }

        public ProductBrandRecord(ThingDef thingDef)
        {
            this.thingDef = thingDef;
        }

        public ProductBrandRecord(
            ThingDef thingDef, float directScore, float evidenceWeight, int unitsDelivered)
        {
            this.thingDef = thingDef;
            this.directScore = directScore;
            this.evidenceWeight = evidenceWeight;
            this.unitsDelivered = unitsDelivered;
        }

        private static float ClampScore(float value)
        {
            // Mathf.Clamp intentionally does not turn NaN into a bound because comparisons with
            // NaN are false. A corrupt or hand-edited save therefore falls back to unknown rather
            // than smuggling a non-number through every later brand calculation.
            if (float.IsNaN(value))
            {
                return Neutral;
            }

            return Mathf.Clamp(value, MinScore, MaxScore);
        }

        public void ExposeData()
        {
            Scribe_Defs.Look(ref thingDef, "thingDef");
            Scribe_Values.Look(ref directScoreValue, "directScore", Neutral);
            Scribe_Values.Look(ref evidenceWeight, "evidenceWeight", 0f);
            Scribe_Values.Look(ref unitsDelivered, "unitsDelivered", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Scribe loads the backing field directly, so repeat the model guard after a save
                // boundary. This catches a malformed or future-version value before any consumer
                // can mistake it for a legitimate score.
                directScoreValue = ClampScore(directScoreValue);
            }
        }
    }
}
