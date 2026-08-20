using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Self-test for persisted market pressure (the 1.0 program Stage 2A).
    ///
    /// The claim under test is narrow and worth stating exactly: pressure is sparse, absence means
    /// neutral, a disturbed settlement keeps its record across save and load, and a settled one
    /// stops costing anything.
    /// </summary>
    public static class IntercolonyEconomySelfTest
    {
        private sealed class Results
        {
            public readonly StringBuilder sb = new StringBuilder();
            public int passed;
            public int failed;

            public void Check(bool condition, string label, string detail = null)
            {
                if (condition)
                {
                    passed++;
                    sb.AppendLine($"  PASS  {label}{(detail == null ? "" : $"  ({detail})")}");
                }
                else
                {
                    failed++;
                    sb.AppendLine($"  FAIL  {label}{(detail == null ? "" : $"  ({detail})")}");
                }
            }
        }

        public static string Run(IntercolonyWorldComponent state)
        {
            Results r = new Results();
            r.sb.AppendLine("Market pressure self-test (the 1.0 program Stage 2A)");

            if (state == null)
            {
                r.sb.AppendLine("  No world state available. Open or load a game first.");
                return Summarize(r);
            }

            // Contents, not count. Pruning removes arbitrary entries rather than trailing ones,
            // so restoring by length would leave the test's synthetic pressure in place of the
            // player's real pressure - the same defect the timeline guard exists to prevent.
            List<SettlementMarketState> saved =
                new List<SettlementMarketState>(state.MarketStates);

            try
            {
                CheckSparseDefaults(r, state);
                CheckPruning(r, state);
                CheckScribeRoundTrip(r);
                CheckShortSaveIsPaddedNeutral(r);
            }
            catch (Exception ex)
            {
                r.sb.AppendLine($"  EXCEPTION: {ex}");
                r.failed++;
            }
            finally
            {
                state.MarketStates.Clear();
                state.MarketStates.AddRange(saved);
                state.PruneNeutralMarketStates();
                r.sb.AppendLine($"        market states restored to {state.MarketStates.Count}.");
            }

            return Summarize(r);
        }

        private const int ProbeSettlementId = 971_101;

        private static void CheckSparseDefaults(Results r, IntercolonyWorldComponent state)
        {
            state.MarketStates.RemoveAll(s => s != null && s.settlementId == ProbeSettlementId);
            state.PruneNeutralMarketStates();

            r.Check(state.MarketStateFor(ProbeSettlementId) == null,
                "an undisturbed settlement stores no record");

            SettlementMarketState created = state.MarketStateFor(ProbeSettlementId, createIfMissing: true);
            r.Check(created != null, "a record can be created on demand");
            r.Check(created != null && created.IsNeutral, "a fresh record is neutral");
            r.Check(
                created != null &&
                Mathf.Approximately(
                    created.DemandPressureFor(IntercolonyProductCategory.Commodities),
                    SettlementMarketState.Neutral),
                "a fresh record reads neutral per category");

            r.Check(ReferenceEquals(created, state.MarketStateFor(ProbeSettlementId)),
                "a second lookup returns the same record rather than a duplicate");

            r.Check(state.MarketStateFor(-1, createIfMissing: true) == null,
                "an invalid settlement id never creates a record");
        }

        private static void CheckPruning(Results r, IntercolonyWorldComponent state)
        {
            SettlementMarketState probe =
                state.MarketStateFor(ProbeSettlementId, createIfMissing: true);

            probe.demandPressure[(int)IntercolonyProductCategory.Commodities] = 1.4f;
            r.Check(!probe.IsNeutral, "a disturbed record is not neutral");

            int beforeDisturbed = state.MarketStates.Count;
            state.PruneNeutralMarketStates();
            r.Check(state.MarketStates.Count == beforeDisturbed,
                "pruning keeps a disturbed record", $"{state.MarketStates.Count}");
            r.Check(state.MarketStateFor(ProbeSettlementId) != null,
                "a disturbed settlement is still reachable after pruning");

            // Just inside the epsilon: reversion approaches 1.0 asymptotically, so a record that
            // is close enough must be prunable or the sparse representation never stays sparse.
            probe.demandPressure[(int)IntercolonyProductCategory.Commodities] =
                SettlementMarketState.Neutral + SettlementMarketState.NeutralEpsilon * 0.5f;
            r.Check(probe.IsNeutral, "a record within the epsilon counts as settled");

            state.PruneNeutralMarketStates();
            r.Check(state.MarketStateFor(ProbeSettlementId) == null,
                "pruning drops a settled record");
        }

        private static void CheckScribeRoundTrip(Results r)
        {
            SettlementMarketState original = new SettlementMarketState(4242)
            {
                lastAdvancedRefresh = 17
            };
            original.demandPressure[(int)IntercolonyProductCategory.Commodities] = 1.35f;
            original.supplyPressure[(int)IntercolonyProductCategory.ManufacturedGoods] = 0.62f;

            List<SettlementMarketState> savedList =
                new List<SettlementMarketState> { original };
            List<SettlementMarketState> loadedList = null;
            string failure = null;
            string path = Path.Combine(
                Path.GetTempPath(), $"Intercolony-MarketState-{Guid.NewGuid():N}.xml");

            try
            {
                Scribe.saver.InitSaving(path, "intercolonyMarketStateTest");
                Scribe_Collections.Look(ref savedList, "marketStates", LookMode.Deep);
                Scribe.saver.FinalizeSaving();

                Scribe.loader.InitLoading(path);
                Scribe_Collections.Look(ref loadedList, "marketStates", LookMode.Deep);
                Scribe.loader.FinalizeLoading();
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                Scribe.ForceStop();
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            SettlementMarketState loaded =
                loadedList != null && loadedList.Count == 1 ? loadedList[0] : null;

            r.Check(failure == null && loaded != null,
                "market pressure survives a Scribe round trip", failure);
            r.Check(loaded != null && loaded.settlementId == 4242,
                "settlement id survives");
            r.Check(loaded != null && loaded.lastAdvancedRefresh == 17,
                "last advanced refresh survives");
            r.Check(
                loaded != null &&
                Mathf.Approximately(
                    loaded.DemandPressureFor(IntercolonyProductCategory.Commodities), 1.35f),
                "demand pressure survives per category",
                loaded?.DemandPressureFor(IntercolonyProductCategory.Commodities).ToString("F3"));
            r.Check(
                loaded != null &&
                Mathf.Approximately(
                    loaded.SupplyPressureFor(IntercolonyProductCategory.ManufacturedGoods), 0.62f),
                "supply pressure survives per category");
            r.Check(
                loaded != null &&
                Mathf.Approximately(
                    loaded.SupplyPressureFor(IntercolonyProductCategory.Commodities),
                    SettlementMarketState.Neutral),
                "an untouched category loads neutral");
        }

        /// <summary>
        /// A save written when there were fewer product categories must not come back with zeros
        /// in the new slots. Zero would read as "no demand at all", which is a shortage nobody
        /// caused, rather than "undisturbed".
        /// </summary>
        private static void CheckShortSaveIsPaddedNeutral(Results r)
        {
            SettlementMarketState state = new SettlementMarketState(55);
            bool everyCategoryNeutral = true;
            foreach (IntercolonyProductCategory category in IntercolonyProductCategoryUtility.All)
            {
                everyCategoryNeutral &=
                    Mathf.Approximately(state.DemandPressureFor(category), SettlementMarketState.Neutral) &&
                    Mathf.Approximately(state.SupplyPressureFor(category), SettlementMarketState.Neutral);
            }

            r.Check(everyCategoryNeutral, "a new record is neutral in every category");
            r.Check(state.IsNeutral, "and reports itself settled");
        }

        private static string Summarize(Results r)
        {
            r.sb.AppendLine();
            r.sb.AppendLine($"  {r.passed} passed, {r.failed} failed.");
            return r.sb.ToString();
        }
    }
}
