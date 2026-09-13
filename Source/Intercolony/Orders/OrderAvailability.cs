using System;

namespace Intercolony
{
    /// <summary>
    /// A read-only snapshot of an order's currently available and required quantity.
    /// F12 requires a programmed recurring caravan to wait instead of leaving with a partial
    /// order, so the caller needs the counts it can report (for example, "5 of 10 available"),
    /// not only the yes/no answer used by the existing readiness decision.
    /// </summary>
    public readonly struct OrderAvailability
    {
        /// <summary>Matching units currently free after other open-order commitments.</summary>
        public readonly int AvailableQuantity;

        /// <summary>Units still required by the order.</summary>
        public readonly int RequiredQuantity;

        /// <summary>
        /// Whether this snapshot represents a measurable order and has enough units to fulfil it.
        /// Callers must check <see cref="IsApplicable"/> before interpreting this value.
        /// </summary>
        public bool CanBeFullySatisfied => IsApplicable && AvailableQuantity >= RequiredQuantity;

        /// <summary>
        /// Whether the quantities describe a real order/map snapshot. False means the quantities
        /// are intentionally not applicable and must not be read as a shortage.
        /// </summary>
        public readonly bool IsApplicable;

        public OrderAvailability(int availableQuantity, int requiredQuantity)
            : this(availableQuantity, requiredQuantity, true)
        {
        }

        private OrderAvailability(int availableQuantity, int requiredQuantity, bool isApplicable)
        {
            AvailableQuantity = Math.Max(0, availableQuantity);
            RequiredQuantity = Math.Max(0, requiredQuantity);
            IsApplicable = isApplicable;
        }

        /// <summary>Explicit sentinel for an order/map pair whose availability cannot be measured.</summary>
        public static OrderAvailability NotApplicable => new OrderAvailability(0, 0, false);
    }
}
