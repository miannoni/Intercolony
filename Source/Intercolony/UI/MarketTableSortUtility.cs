using System;
using System.Collections.Generic;

namespace Intercolony
{
    /// <summary>
    /// Shared clickable-market table ordering. Both market directions use the same descending
    /// toggle and stable tie-break so rows do not jitter when equal values are refreshed.
    /// </summary>
    internal static class MarketTableSortUtility
    {
        internal static void Sort<T>(
            List<T> rows,
            Comparison<T> comparison,
            bool descending,
            Comparison<T> tieBreaker)
        {
            rows.Sort((left, right) =>
            {
                int result = comparison(left, right);
                if (result != 0)
                {
                    return descending ? -result : result;
                }

                return tieBreaker(left, right);
            });
        }
    }
}
