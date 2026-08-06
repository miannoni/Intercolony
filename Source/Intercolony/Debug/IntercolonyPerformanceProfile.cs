using System;
using System.Diagnostics;
using System.Text;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Live-game timings for Phase 25. This deliberately invokes production entry points against
    /// the loaded world: a synthetic benchmark would answer a different question than the one the
    /// player actually feels.
    /// </summary>
    public static class IntercolonyPerformanceProfile
    {
        private const double MinimumRepeatedMilliseconds = 100d;
        private const int MaximumRepetitions = 1000000;

        public static string Run(IntercolonyWorldComponent state, Map map)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Intercolony performance profile (live game, Stopwatch)");
            sb.AppendLine($"Context: tick {GenTicks.TicksGame}, " +
                          $"{Find.WorldObjects?.Settlements?.Count ?? 0} settlement(s), " +
                          $"{state.ActiveOpportunityCount} active opportunity(ies), " +
                          $"refresh #{state.RefreshCount}.");
            sb.AppendLine($"Repeated read/cache samples run until at least " +
                          $"{MinimumRepeatedMilliseconds:F0} ms total or " +
                          $"{MaximumRepetitions:N0} runs. Cache resets are outside timed regions.");
            sb.AppendLine();

            using (IntercolonyLog.SuppressVerbose())
            {
                int refreshBefore = state.RefreshCount;
                int activeBefore = state.ActiveOpportunityCount;
                IntercolonyWorldComponent.RefreshPerformanceSample refresh =
                    state.RunRefreshForPerformanceProfile();
                sb.AppendLine("Daily refresh path (state-changing production path, one run)");
                sb.AppendLine($"  Full scheduled-refresh body : {refresh.totalMilliseconds,10:F3} ms " +
                              $"(1 run; refresh #{refreshBefore} -> #{state.RefreshCount})");
                sb.AppendLine($"  Market generation phase     : " +
                              $"{refresh.opportunityGenerationMilliseconds,10:F3} ms " +
                              $"(1 run; {refresh.opportunitiesCreated} created; " +
                              $"{activeBefore} -> {state.ActiveOpportunityCount} active)");
                sb.AppendLine();

                int productCount = 0;
                Measurement productCold = MeasureRepeated(
                    IntercolonyProductClassifier.InvalidateForPerformanceProfile,
                    () => productCount = IntercolonyProductClassifier.TradableDefs.Count);
                Measurement productWarm = MeasureRepeated(
                    null,
                    () => productCount = IntercolonyProductClassifier.TradableDefs.Count);
                AppendMeasurement(sb, "Product classification cold", productCold,
                    $"{productCount} tradable fungible def(s)");
                AppendMeasurement(sb, "Product classification warm", productWarm,
                    $"{productCount} cached def(s)");
                sb.AppendLine();

                int profileCount = 0;
                Measurement profilesCold = MeasureRepeated(
                    state.InvalidateProfileCacheForPerformanceProfile,
                    () => profileCount = state.AllProfiles().Count);
                Measurement profilesWarm = MeasureRepeated(
                    null,
                    () => profileCount = state.AllProfiles().Count);
                AppendMeasurement(sb, "Settlement profiles cold", profilesCold,
                    $"{profileCount} eligible settlement(s)");
                AppendMeasurement(sb, "Settlement profiles warm", profilesWarm,
                    $"{profileCount} cached profile lookup(s)");
                sb.AppendLine();

                int censusCount = 0;
                Measurement censusCold = MeasureRepeated(
                    LaborCandidateService.InvalidateCensusForPerformanceProfile,
                    () => censusCount = LaborCandidateService.Census(state).Count);
                Measurement censusWarm = MeasureRepeated(
                    null,
                    () => censusCount = LaborCandidateService.Census(state).Count);
                AppendMeasurement(sb, "Labor census cold", censusCold,
                    $"{censusCount} worker record(s)");
                AppendMeasurement(sb, "Labor census warm", censusWarm,
                    $"{censusCount} cached worker record(s)");
                sb.AppendLine();

                SalesOrder openOrder = FirstOpenOrder(state);
                if (map == null)
                {
                    sb.AppendLine("Order validation             : SKIPPED (no current colony map)");
                }
                else if (openOrder == null)
                {
                    sb.AppendLine("Order validation             : SKIPPED (no open sales order)");
                }
                else
                {
                    OrderValidationResult validation = null;
                    Measurement orderValidation = MeasureRepeated(
                        null,
                        () => validation = OrderValidator.ValidateColony(openOrder, map));
                    AppendMeasurement(sb, "Order validation in storage", orderValidation,
                        $"order #{openOrder.id}; {map.listerThings.AllThings.Count} map thing(s); " +
                        $"{validation?.matchedQuantity ?? 0} matched unit(s)");
                }
            }

            sb.AppendLine();
            sb.AppendLine("Notes:");
            sb.AppendLine("  The refresh sample advances the loaded world once through the same DoRefresh body " +
                          "called by the 60,000-tick schedule.");
            sb.AppendLine("  Cold means the relevant derived cache was cleared before every timed run; warm means " +
                          "the cache was already populated.");
            sb.AppendLine("  Verbose dev logging is suppressed during timed samples so log rendering is not " +
                          "mistaken for hot-path work.");
            sb.AppendLine("  These are elapsed-time measurements only. No GC or allocation-byte claim is made " +
                          "without a profiler.");
            return sb.ToString();
        }

        private static SalesOrder FirstOpenOrder(IntercolonyWorldComponent state)
        {
            foreach (SalesOrder order in state.Orders)
            {
                if (order.IsOpen)
                {
                    return order;
                }
            }

            return null;
        }

        private static Measurement MeasureRepeated(Action prepare, Action action)
        {
            return prepare == null
                ? MeasureBatched(action)
                : MeasureWithReset(prepare, action);
        }

        private static Measurement MeasureWithReset(Action prepare, Action action)
        {
            Measurement result = new Measurement();
            Stopwatch timer = new Stopwatch();

            do
            {
                prepare?.Invoke();
                timer.Restart();
                action();
                timer.Stop();

                double elapsed = timer.Elapsed.TotalMilliseconds;
                if (result.runs == 0)
                {
                    result.firstMilliseconds = elapsed;
                }

                result.totalMilliseconds += elapsed;
                result.runs++;
            }
            while (result.totalMilliseconds < MinimumRepeatedMilliseconds &&
                   result.runs < MaximumRepetitions);

            return result;
        }

        /// <summary>
        /// Cached getters can be shorter than one Stopwatch interval. Time them as a batch so the
        /// reported average is based on useful elapsed time rather than start/stop overhead.
        /// </summary>
        private static Measurement MeasureBatched(Action action)
        {
            Measurement result = new Measurement();
            Stopwatch timer = Stopwatch.StartNew();
            action();
            timer.Stop();
            result.firstMilliseconds = timer.Elapsed.TotalMilliseconds;

            int repetitions = 1;
            do
            {
                timer.Restart();
                for (int i = 0; i < repetitions; i++)
                {
                    action();
                }

                timer.Stop();
                result.runs = repetitions;
                result.totalMilliseconds = timer.Elapsed.TotalMilliseconds;

                if (result.totalMilliseconds >= MinimumRepeatedMilliseconds ||
                    repetitions == MaximumRepetitions)
                {
                    break;
                }

                repetitions = Math.Min(MaximumRepetitions, repetitions * 10);
            }
            while (true);

            return result;
        }

        private static void AppendMeasurement(
            StringBuilder sb, string label, Measurement measurement, string sample)
        {
            sb.AppendLine($"{label,-31}: {measurement.AverageMilliseconds,10:F3} ms avg " +
                          $"({measurement.runs:N0} run(s), {measurement.totalMilliseconds:F3} ms " +
                          $"timed total; first {measurement.firstMilliseconds:F3} ms; {sample})");
        }

        private sealed class Measurement
        {
            public int runs;
            public double totalMilliseconds;
            public double firstMilliseconds;

            public double AverageMilliseconds => runs > 0 ? totalMilliseconds / runs : 0d;
        }
    }
}
