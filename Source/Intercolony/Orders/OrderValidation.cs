using System.Collections.Generic;
using System.Text;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Result of checking a caravan's cargo against an order (DESIGN.md §74: "Return
    /// structured results, not only booleans", and §18: "Return structured validation
    /// failures for UI").
    ///
    /// Structured because the player needs to know *why* a delivery fell short — §18's
    /// worked example is "18 / 20 chairs delivered" — and because a bare bool would force
    /// the UI to re-derive the reason and risk disagreeing with the authoritative check.
    /// </summary>
    public class OrderValidationResult
    {
        public bool Success => missingQuantity <= 0 && failures.Count == 0;

        /// <summary>Units present in the caravan that satisfy the order line.</summary>
        public int matchedQuantity;

        /// <summary>Units still required after counting what is present.</summary>
        public int missingQuantity;

        public readonly List<string> failures = new List<string>();

        public string Summary()
        {
            StringBuilder sb = new StringBuilder();
            if (Success)
            {
                sb.Append($"{matchedQuantity} units ready to hand over.");
                return sb.ToString();
            }

            sb.Append($"{matchedQuantity} of {matchedQuantity + missingQuantity} units available");
            foreach (string failure in failures)
            {
                sb.Append("\n- ").Append(failure);
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Centralized matching logic (DESIGN.md §74: "Centralize matching logic").
    ///
    /// Phase 5 handles only the fungible case (§23.1): does this Thing have the right
    /// ThingDef, and is there enough of it. Quality, stuff, and hit-point constraints are
    /// Phase 6 (§99) and belong here too when they arrive, rather than being scattered into
    /// the delivery or UI code.
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

            if (order.thingDef == null)
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
            int found = CountMatching(order, caravan);

            result.matchedQuantity = Mathf.Min(found, required);
            result.missingQuantity = Mathf.Max(0, required - found);

            if (result.missingQuantity > 0)
            {
                result.failures.Add(
                    $"{result.missingQuantity} more {order.thingDef.label} needed " +
                    $"({found} carried, {required} required).");
            }

            return result;
        }

        /// <summary>Total units of the ordered def carried by the caravan.</summary>
        public static int CountMatching(SalesOrder order, Caravan caravan)
        {
            if (order?.thingDef == null || caravan == null)
            {
                return 0;
            }

            int total = 0;
            List<Thing> items = CaravanInventoryUtility.AllInventoryItems(caravan);
            for (int i = 0; i < items.Count; i++)
            {
                if (Matches(order, items[i]))
                {
                    total += items[i].stackCount;
                }
            }

            return total;
        }

        /// <summary>
        /// Whether a single Thing satisfies the order line. The one place this question is
        /// answered, so delivery and UI can never disagree about it (§74).
        /// </summary>
        public static bool Matches(SalesOrder order, Thing thing)
        {
            if (order?.thingDef == null || thing == null)
            {
                return false;
            }

            // Phase 5 is fungible-only: def identity is the whole test. Phase 6 (§99) adds
            // quality, stuff, and condition constraints here.
            return thing.def == order.thingDef;
        }
    }
}
