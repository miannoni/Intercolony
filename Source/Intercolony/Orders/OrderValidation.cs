using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Why a particular Thing failed to satisfy a line. Kept as a reason code rather than a
    /// bare string so the summary can aggregate ("3 below Excellent") instead of repeating
    /// the same sentence once per item (DESIGN.md §18).
    /// </summary>
    public enum MatchFailure
    {
        WrongDef,
        BelowMinimumQuality,
        WrongStuff,
        TooDamaged
    }

    /// <summary>
    /// Result of checking a caravan's cargo against an order (DESIGN.md §74: "Return
    /// structured results, not only booleans", and §18: "Return structured validation
    /// failures for UI").
    ///
    /// Structured because the player needs to know *why* a delivery fell short — §18's
    /// worked example is "18 / 20 chairs delivered, 2 chairs below Excellent quality" — and
    /// because a bare bool would force the UI to re-derive the reason and risk disagreeing
    /// with the authoritative check.
    /// </summary>
    public class OrderValidationResult
    {
        public bool Success => missingQuantity <= 0 && failures.Count == 0;

        /// <summary>Units present that satisfy the line.</summary>
        public int matchedQuantity;

        /// <summary>Units still required after counting what is present.</summary>
        public int missingQuantity;

        public readonly List<string> failures = new List<string>();

        /// <summary>
        /// Units that were the right item but failed a constraint, by reason. This is what
        /// turns "you are short 2" into "2 are below Excellent", which is the difference
        /// between an actionable message and a confusing one.
        /// </summary>
        public readonly Dictionary<MatchFailure, int> rejected = new Dictionary<MatchFailure, int>();

        private bool hasConditionRejection;
        private float lowestRejectedCondition;
        private float highestRejectedCondition;
        private float requiredCondition;

        public void NoteRejected(MatchFailure reason, int count)
        {
            rejected.TryGetValue(reason, out int existing);
            rejected[reason] = existing + count;
        }

        public void NoteConditionRejected(int count, float offeredCondition, float required)
        {
            NoteRejected(MatchFailure.TooDamaged, count);

            if (!hasConditionRejection)
            {
                hasConditionRejection = true;
                lowestRejectedCondition = offeredCondition;
                highestRejectedCondition = offeredCondition;
            }
            else
            {
                lowestRejectedCondition = Mathf.Min(lowestRejectedCondition, offeredCondition);
                highestRejectedCondition = Mathf.Max(highestRejectedCondition, offeredCondition);
            }

            requiredCondition = required;
        }

        public string Summary()
        {
            if (Success)
            {
                return $"{matchedQuantity} units ready to hand over.";
            }

            StringBuilder sb = new StringBuilder();
            sb.Append($"{matchedQuantity} of {matchedQuantity + missingQuantity} units available");

            foreach (KeyValuePair<MatchFailure, int> entry in rejected)
            {
                sb.Append("\n- ").Append(DescribeRejection(entry.Key, entry.Value));
            }

            foreach (string failure in failures)
            {
                sb.Append("\n- ").Append(failure);
            }

            return sb.ToString();
        }

        private string DescribeRejection(MatchFailure reason, int count)
        {
            switch (reason)
            {
                case MatchFailure.BelowMinimumQuality:
                    return $"{count} carried below the required quality";
                case MatchFailure.WrongStuff:
                    return $"{count} carried in the wrong material";
                case MatchFailure.TooDamaged:
                    if (!hasConditionRejection)
                    {
                        return $"{count} offered below the required condition";
                    }

                    int lowest = Mathf.RoundToInt(lowestRejectedCondition * 100f);
                    int highest = Mathf.RoundToInt(highestRejectedCondition * 100f);
                    string offered = lowest == highest ? $"{lowest}%" : $"{lowest}–{highest}%";
                    return $"{count} offered below the condition floor " +
                           $"({offered} offered; {Mathf.RoundToInt(requiredCondition * 100f)}% required)";
                default:
                    return $"{count} rejected";
            }
        }
    }

    /// <summary>
    /// The single path that answers "does this Thing satisfy this order line"
    /// (DESIGN.md §74, and §99's acceptance criterion: "One centralized validation path
    /// supports all test cases").
    ///
    /// Every §99 case runs through <see cref="Matches"/>: 1,000 Rice and 200 Cloth exercise
    /// the unconstrained path, 20 Excellent Dining Chairs the quality path, 5 Normal-or-better
    /// weapons the quality path on a different category. Stuff and condition constraints ride
    /// the same code.
    /// </summary>
    public static class OrderValidator
    {
        /// <summary>
        /// Counts how much of the order a caravan can satisfy right now. Does not mutate
        /// anything — callers decide whether to act on the result.
        /// </summary>
        public static OrderValidationResult ValidateCaravan(SalesOrder order, Caravan caravan)
        {
            OrderValidationResult result = new OrderValidationResult();

            if (order == null)
            {
                result.failures.Add("Order no longer exists.");
                return result;
            }

            if (order.line?.thingDef == null)
            {
                result.failures.Add("The requested item no longer exists in this game's content.");
                result.missingQuantity = order.RemainingQuantity;
                return result;
            }

            if (!order.IsOpen)
            {
                result.failures.Add($"Order is {order.status}, not open.");
                result.missingQuantity = order.RemainingQuantity;
                return result;
            }

            if (caravan == null)
            {
                result.failures.Add("No caravan.");
                result.missingQuantity = order.RemainingQuantity;
                return result;
            }

            int required = order.RemainingQuantity;
            int found = 0;

            List<Thing> items = CaravanInventoryUtility.AllInventoryItems(caravan);
            for (int i = 0; i < items.Count; i++)
            {
                Thing thing = items[i];
                if (Matches(order.line, thing, out MatchFailure failure))
                {
                    found += CountableUnits(thing);
                }
                else if (failure != MatchFailure.WrongDef)
                {
                    // Right item, failed a constraint — worth telling the player about.
                    NoteRejected(result, order.line, thing, failure);
                }
            }

            result.matchedQuantity = Mathf.Min(found, required);
            result.missingQuantity = Mathf.Max(0, required - found);

            if (result.missingQuantity > 0 && result.rejected.Count == 0)
            {
                result.failures.Add(
                    $"{result.missingQuantity} more {order.line.thingDef.label} needed " +
                    $"({found} carried, {required} required).");
            }

            return result;
        }

        /// <summary>
        /// Checks colony storage for a buyer-pickup order with the same rejection detail as a
        /// caravan delivery. Declaring goods ready is still a delivery decision to the player.
        /// </summary>
        public static OrderValidationResult ValidateColony(SalesOrder order, Map map)
        {
            OrderValidationResult result = new OrderValidationResult();

            if (order == null)
            {
                result.failures.Add("Order no longer exists.");
                return result;
            }

            if (order.line?.thingDef == null)
            {
                result.failures.Add("The requested item no longer exists in this game's content.");
                result.missingQuantity = order.RemainingQuantity;
                return result;
            }

            if (!order.IsOpen)
            {
                result.failures.Add($"Order is {order.status}, not open.");
                result.missingQuantity = order.RemainingQuantity;
                return result;
            }

            if (map == null)
            {
                result.failures.Add("No colony map.");
                result.missingQuantity = order.RemainingQuantity;
                return result;
            }

            int required = order.RemainingQuantity;
            int found = 0;
            foreach (Thing thing in ColonyCandidates(map, order.line.thingDef))
            {
                if (!thing.IsInAnyStorage())
                {
                    continue;
                }

                if (Matches(order.line, thing, out MatchFailure failure))
                {
                    found += CountableUnits(thing);
                }
                else if (failure != MatchFailure.WrongDef)
                {
                    NoteRejected(result, order.line, thing, failure);
                }
            }

            result.matchedQuantity = Mathf.Min(found, required);
            result.missingQuantity = Mathf.Max(0, required - found);
            if (result.missingQuantity > 0 && result.rejected.Count == 0)
            {
                result.failures.Add(
                    $"{result.missingQuantity} more {order.line.thingDef.label} needed " +
                    $"({found} stored, {required} required).");
            }

            return result;
        }

        private static void NoteRejected(
            OrderValidationResult result, OrderLine line, Thing thing, MatchFailure failure)
        {
            int count = CountableUnits(thing);
            if (failure == MatchFailure.TooDamaged)
            {
                Thing inner = thing.GetInnerIfMinified();
                float condition = inner.MaxHitPoints > 0
                    ? inner.HitPoints / (float)inner.MaxHitPoints
                    : 1f;
                result.NoteConditionRejected(count, condition, line.minHitPointsPercent);
            }
            else
            {
                result.NoteRejected(failure, count);
            }
        }

        /// <summary>
        /// Takes units out of colony storage, returning how many were actually removed.
        /// </summary>
        public static int TakeFromColony(SalesOrder order, Map map, int wanted)
        {
            if (order?.line?.thingDef == null || map == null || wanted <= 0)
            {
                return 0;
            }

            // Snapshot first: destroying things mutates the lister mid-iteration.
            List<Thing> matching = new List<Thing>();
            foreach (Thing thing in ColonyCandidates(map, order.line.thingDef))
            {
                if (thing.IsInAnyStorage() && Matches(order.line, thing, out _))
                {
                    matching.Add(thing);
                }
            }

            int remaining = wanted;
            foreach (Thing thing in matching)
            {
                if (remaining <= 0)
                {
                    break;
                }

                if (thing is MinifiedThing)
                {
                    thing.Destroy(DestroyMode.Vanish);
                    remaining -= 1;
                    continue;
                }

                int take = Mathf.Min(remaining, thing.stackCount);
                thing.SplitOff(take).Destroy(DestroyMode.Vanish);
                remaining -= take;
            }

            return wanted - remaining;
        }

        private static IEnumerable<Thing> ColonyCandidates(Map map, ThingDef requestedDef)
        {
            // The requested def's index cannot contain minified furniture because the wrapper
            // has a MinifiedThing def. Include that narrow group so installed and packed forms
            // still answer the same order without scanning every thing on the map.
            if (!ThingRequestGroup.MinifiedThing.Includes(requestedDef))
            {
                foreach (Thing thing in map.listerThings.ThingsOfDef(requestedDef))
                {
                    yield return thing;
                }
            }

            foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.MinifiedThing))
            {
                yield return thing;
            }
        }

        /// <summary>
        /// How many order units a carried Thing represents. A minified chair is one chair, not
        /// however many the wrapper claims to stack to.
        /// </summary>
        public static int CountableUnits(Thing thing)
        {
            if (thing == null)
            {
                return 0;
            }

            return thing is MinifiedThing ? 1 : thing.stackCount;
        }

        /// <summary>
        /// Whether a single Thing satisfies the line, and if not, why. The one place this
        /// question is answered, so delivery, UI, and pricing can never disagree (§74).
        /// </summary>
        public static bool Matches(OrderLine line, Thing thing, out MatchFailure failure)
        {
            failure = MatchFailure.WrongDef;

            if (line?.thingDef == null || thing == null)
            {
                return false;
            }

            // Furniture and equipment travel minified: the Thing in a caravan's inventory is a
            // MinifiedThing whose own def is "MinifiedThing", with the chair inside it. Without
            // unwrapping, §99's "20 Excellent Dining Chairs" could never match anything a
            // caravan is physically able to carry.
            thing = thing.GetInnerIfMinified();

            if (thing.def != line.thingDef)
            {
                return false;
            }

            if (line.HasQualityConstraint)
            {
                // An item that cannot carry quality can never satisfy a quality constraint.
                // Treating "no quality" as acceptable would silently let a player deliver
                // unqualified goods against an Excellent order.
                if (!thing.TryGetQuality(out QualityCategory quality) ||
                    quality < line.minQuality.Value)
                {
                    failure = MatchFailure.BelowMinimumQuality;
                    return false;
                }
            }

            if (line.HasStuffConstraint && thing.Stuff != line.allowedStuff)
            {
                failure = MatchFailure.WrongStuff;
                return false;
            }

            if (line.HasConditionConstraint && thing.def.useHitPoints)
            {
                float condition = thing.MaxHitPoints > 0
                    ? thing.HitPoints / (float)thing.MaxHitPoints
                    : 1f;
                if (condition < line.minHitPointsPercent)
                {
                    failure = MatchFailure.TooDamaged;
                    return false;
                }
            }

            return true;
        }

        /// <summary>Convenience overload for callers that do not care why.</summary>
        public static bool Matches(OrderLine line, Thing thing)
        {
            return Matches(line, thing, out _);
        }
    }
}
