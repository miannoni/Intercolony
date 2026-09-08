using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// One persisted bucket of completed production for one exact good and one absolute in-game day.
    ///
    /// Production is kept as one bucket per good per day rather than one record per completed item:
    /// a steady producer could otherwise add thousands of rows to every save. The rolling figure is
    /// derived from these compact buckets when it is read.
    /// </summary>
    public class ProductionBucket : IExposable
    {
        public ThingDef thingDef;
        public int day;
        public int count;

        public void ExposeData()
        {
            Scribe_Defs.Look(ref thingDef, "thingDef");
            Scribe_Values.Look(ref day, "day", 0);
            Scribe_Values.Look(ref count, "count", 0);
        }
    }

    /// <summary>
    /// Records completed goods and reports the recent production rate from the world-owned ledger.
    /// </summary>
    public static class ProductionLedgerService
    {
        /// <summary>
        /// The rolling production window used by the Business view's later report.
        /// </summary>
        public const int WindowDays = 5;

        /// <summary>Records completed units in the current absolute day bucket.</summary>
        public static void Record(
            IntercolonyWorldComponent state,
            ThingDef thingDef,
            int count)
        {
            if (state == null || thingDef == null || count <= 0)
            {
                return;
            }

            List<ProductionBucket> buckets = state.ProductionLedger;
            if (buckets == null)
            {
                return;
            }

            int day = CurrentDay();
            foreach (ProductionBucket bucket in buckets)
            {
                if (bucket != null && bucket.thingDef == thingDef && bucket.day == day)
                {
                    bucket.count += count;
                    return;
                }
            }

            buckets.Add(new ProductionBucket
            {
                thingDef = thingDef,
                day = day,
                count = count
            });
        }

        /// <summary>Reports completed units per day over the rolling window.</summary>
        public static float CompletedPerDay(
            IntercolonyWorldComponent state,
            ThingDef thingDef)
        {
            if (state == null || thingDef == null)
            {
                return 0f;
            }

            List<ProductionBucket> buckets = state.ProductionLedger;
            if (buckets == null || buckets.Count == 0)
            {
                return 0f;
            }

            int currentDay = CurrentDay();
            int firstDay = FirstDay(currentDay);
            long total = 0;
            foreach (ProductionBucket bucket in buckets)
            {
                if (bucket == null || bucket.thingDef != thingDef || bucket.count <= 0 ||
                    bucket.day < firstDay || bucket.day > currentDay)
                {
                    continue;
                }

                total += bucket.count;
            }

            return total / (float)WindowDays;
        }

        /// <summary>Removes invalid buckets and buckets older than the rolling window.</summary>
        public static int Prune(IntercolonyWorldComponent state)
        {
            List<ProductionBucket> buckets = state?.ProductionLedger;
            if (buckets == null || buckets.Count == 0)
            {
                return 0;
            }

            int firstDay = FirstDay(CurrentDay());
            return buckets.RemoveAll(bucket =>
                bucket == null || bucket.thingDef == null || bucket.count <= 0 ||
                bucket.day < firstDay);
        }

        private static int CurrentDay()
        {
            return GenDate.DaysPassedAt(GenTicks.TicksGame);
        }

        private static int FirstDay(int currentDay)
        {
            return currentDay - WindowDays + 1;
        }
    }
}
