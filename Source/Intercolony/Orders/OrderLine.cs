using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// What an order actually asks for (DESIGN.md §15 OrderLine, §23.1 generic lot
    /// descriptors).
    ///
    /// One selector type covers every §99 test case — 1,000 Rice, 20 Excellent Dining Chairs,
    /// 5 Normal-or-better weapons, 200 Cloth — so there is exactly one place that answers
    /// "does this Thing satisfy this line", which is precisely what §99's acceptance criterion
    /// asks for and what §74 means by centralizing matching logic.
    ///
    /// Constraints are opt-in: a null or zero constraint means "don't care", so a plain
    /// commodity line carries no quality or material baggage.
    /// </summary>
    public class OrderLine : IExposable
    {
        /// <summary>The requested item. Required.</summary>
        public ThingDef thingDef;

        public int quantity;

        /// <summary>Minimum acceptable quality, or null when quality is irrelevant.</summary>
        public QualityCategory? minQuality;

        /// <summary>Required material, or null when any material is acceptable.</summary>
        public ThingDef allowedStuff;

        /// <summary>
        /// Minimum condition as a fraction of max hit points, 0 meaning "don't care".
        /// Buyers will not accept a nearly-broken chair.
        /// </summary>
        public float minHitPointsPercent;

        public OrderLine()
        {
        }

        public OrderLine(ThingDef thingDef, int quantity)
        {
            this.thingDef = thingDef;
            this.quantity = quantity;
        }

        public bool HasQualityConstraint => minQuality.HasValue;

        public bool HasStuffConstraint => allowedStuff != null;

        public bool HasConditionConstraint => minHitPointsPercent > 0f;

        public bool HasAnyConstraint => HasQualityConstraint || HasStuffConstraint || HasConditionConstraint;

        public void ExposeData()
        {
            Scribe_Defs.Look(ref thingDef, "thingDef");
            Scribe_Values.Look(ref quantity, "quantity", 0);
            Scribe_Values.Look(ref minQuality, "minQuality");
            Scribe_Defs.Look(ref allowedStuff, "allowedStuff");
            Scribe_Values.Look(ref minHitPointsPercent, "minHitPointsPercent", 0f);
        }

        /// <summary>Short label for tables: "Dining chair (Excellent+)".</summary>
        public string ShortLabel()
        {
            string label = thingDef?.LabelCap.ToString() ?? "<missing>";
            if (!HasAnyConstraint)
            {
                return label;
            }

            StringBuilder sb = new StringBuilder(label);
            sb.Append(" (");
            bool first = true;

            if (HasQualityConstraint)
            {
                sb.Append(minQuality.Value.GetLabel()).Append("+");
                first = false;
            }

            if (HasStuffConstraint)
            {
                if (!first) sb.Append(", ");
                sb.Append(allowedStuff.LabelCap);
                first = false;
            }

            if (HasConditionConstraint)
            {
                if (!first) sb.Append(", ");
                sb.Append($"{Mathf.RoundToInt(minHitPointsPercent * 100f)}%+ cond");
            }

            sb.Append(")");
            return sb.ToString();
        }

        /// <summary>Full description for tooltips and order detail.</summary>
        public string Describe()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"{quantity}x {thingDef?.LabelCap.ToString() ?? "<missing>"}");
            if (HasQualityConstraint)
            {
                sb.AppendLine($"  Minimum quality: {minQuality.Value.GetLabel()}");
            }

            if (HasStuffConstraint)
            {
                sb.AppendLine($"  Material: {allowedStuff.LabelCap}");
            }

            if (HasConditionConstraint)
            {
                sb.AppendLine($"  Minimum condition: {Mathf.RoundToInt(minHitPointsPercent * 100f)}%");
            }

            return sb.ToString();
        }
    }
}
