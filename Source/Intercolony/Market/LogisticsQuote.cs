using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>The two transport methods currently offered by supplier logistics.</summary>
    public enum LogisticsTransportMethod
    {
        SupplierDelivery,
        ColonyPickup
    }

    /// <summary>
    /// F21 needs one answer for cost, time and method so those terms can be shown together and
    /// later made to vary. Today those three logistics consequences come from five unrelated
    /// formulas.
    /// </summary>
    public readonly struct LogisticsQuote
    {
        /// <summary>Approximate world-tile distance from the player's home map.</summary>
        public readonly float DistanceTiles;

        /// <summary>How the quoted goods move to or from the colony.</summary>
        public readonly LogisticsTransportMethod TransportMethod;

        /// <summary>Days until the goods are ready or arrive, according to the method.</summary>
        public readonly int LeadTimeDays;

        /// <summary>Combined distance and transport multiplier for the quoted price.</summary>
        public readonly float PriceMultiplier;

        /// <summary>Distance component, kept separate so the existing price explanation is unchanged.</summary>
        internal float DistancePriceMultiplier => DistancePriceMultiplierFor(DistanceTiles);

        /// <summary>Transport component, kept separate so the existing price explanation is unchanged.</summary>
        internal float TransportPriceMultiplier =>
            TransportPriceMultiplierFor(TransportMethod);

        private LogisticsQuote(
            float distanceTiles,
            LogisticsTransportMethod transportMethod,
            int leadTimeDays,
            float priceMultiplier)
        {
            DistanceTiles = distanceTiles;
            TransportMethod = transportMethod;
            LeadTimeDays = leadTimeDays;
            PriceMultiplier = priceMultiplier;
        }

        /// <summary>
        /// Produces the logistics terms using the existing distance, method and supply inputs.
        /// The pickup jitter remains here so the owner preserves the old lead-time roll.
        /// </summary>
        public static LogisticsQuote Create(
            float distanceTiles,
            LogisticsTransportMethod transportMethod,
            float supply)
        {
            float distanceMultiplier = DistancePriceMultiplierFor(distanceTiles);
            float transportMultiplier = TransportPriceMultiplierFor(transportMethod);
            int prep = Mathf.RoundToInt(Mathf.Lerp(5f, 1f, Mathf.Clamp01(supply / 2f)));
            int leadTime;

            if (transportMethod == LogisticsTransportMethod.ColonyPickup)
            {
                leadTime = Mathf.Max(1, prep + Rand.RangeInclusive(0, 2));
            }
            else
            {
                int travel = distanceTiles < 0f
                    ? 3
                    : Mathf.RoundToInt(distanceTiles / 12f);
                leadTime = Mathf.Max(1, prep + travel);
            }

            return new LogisticsQuote(
                distanceTiles,
                transportMethod,
                leadTime,
                distanceMultiplier * transportMultiplier);
        }

        /// <summary>Maps the existing delivery decision to the logistics vocabulary.</summary>
        public static LogisticsTransportMethod MethodFor(bool supplierDelivers)
        {
            return supplierDelivers
                ? LogisticsTransportMethod.SupplierDelivery
                : LogisticsTransportMethod.ColonyPickup;
        }

        /// <summary>Existing RFQ distance multiplier, including its unknown-distance behavior.</summary>
        public static float DistancePriceMultiplierFor(float distanceTiles)
        {
            return distanceTiles >= 0f
                ? 1f + Mathf.Min(distanceTiles, 150f) * 0.0012f
                : 1f;
        }

        /// <summary>Existing supplier-delivery price multiplier for a chosen method.</summary>
        public static float TransportPriceMultiplierFor(
            LogisticsTransportMethod transportMethod)
        {
            return transportMethod == LogisticsTransportMethod.SupplierDelivery ? 1.12f : 1f;
        }
    }
}
