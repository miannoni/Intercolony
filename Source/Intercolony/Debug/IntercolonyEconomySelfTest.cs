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
                CheckReversion(r);
                CheckReversionIsDrivenByElapsedCycles(r);
                CheckShockBounds(r, state);
                CheckReversionSettlesAndPrunes(r, state);
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

        /// <summary>
        /// Mean reversion (the 1.0 program Stage 2B, plan §2.4). Asserts the properties the plan
        /// actually requires — direction, monotonicity, no overshoot, eventual settling — and
        /// deliberately never asserts the coefficient, which the plan calls balance tuning.
        /// </summary>
        private static void CheckReversion(Results r)
        {
            const IntercolonyProductCategory Category = IntercolonyProductCategory.Commodities;
            int index = (int)Category;

            SettlementMarketState high = new SettlementMarketState(9001) { lastAdvancedRefresh = 0 };
            high.demandPressure[index] = 1.40f;

            float previous = high.DemandPressureFor(Category);
            bool monotonic = true;
            bool stayedAbove = true;
            for (int refresh = 1; refresh <= 12; refresh++)
            {
                MarketPressureService.Advance(high, refresh);
                float now = high.DemandPressureFor(Category);
                monotonic &= now < previous;
                stayedAbove &= now > SettlementMarketState.Neutral;
                previous = now;
            }

            r.Check(monotonic, "demand pressure falls on every refresh, never rises",
                $"1.40 -> {previous:F4} over 12 refreshes");
            r.Check(stayedAbove,
                "a shortage decaying toward neutral never crosses to the other side",
                "no overshoot in 12 refreshes");

            // The same claim from below. A shock and a glut must behave alike or the market is
            // quietly biased in one direction.
            SettlementMarketState low = new SettlementMarketState(9002) { lastAdvancedRefresh = 0 };
            low.supplyPressure[index] = 0.70f;

            float previousLow = low.SupplyPressureFor(Category);
            bool risesTowardNeutral = true;
            bool stayedBelow = true;
            for (int refresh = 1; refresh <= 12; refresh++)
            {
                MarketPressureService.Advance(low, refresh);
                float now = low.SupplyPressureFor(Category);
                risesTowardNeutral &= now > previousLow;
                stayedBelow &= now < SettlementMarketState.Neutral;
                previousLow = now;
            }

            r.Check(risesTowardNeutral, "a glut rises back toward neutral on every refresh",
                $"0.70 -> {previousLow:F4}");
            r.Check(stayedBelow, "and never overshoots above neutral");

            r.Check(
                Mathf.Abs(previous - SettlementMarketState.Neutral) <
                Mathf.Abs(1.40f - SettlementMarketState.Neutral) * 0.25f,
                "a shock is substantially spent after a dozen cycles, not still at full strength",
                $"{Mathf.Abs(previous - SettlementMarketState.Neutral):F4} left of 0.40");

            // Untouched categories must not drift. A bug that reverted the whole array from a
            // wrong baseline would be invisible here unless something asserts the quiet ones.
            bool othersUntouched = true;
            foreach (IntercolonyProductCategory category in IntercolonyProductCategoryUtility.All)
            {
                if (category == Category)
                {
                    continue;
                }

                othersUntouched &=
                    Mathf.Approximately(high.DemandPressureFor(category), SettlementMarketState.Neutral) &&
                    Mathf.Approximately(high.SupplyPressureFor(category), SettlementMarketState.Neutral);
            }

            r.Check(othersUntouched, "reverting one category leaves every other exactly neutral");

            SettlementMarketState settled = new SettlementMarketState(9003) { lastAdvancedRefresh = 3 };
            MarketPressureService.Advance(settled, 3);
            r.Check(settled.IsNeutral, "advancing an undisturbed record to the same refresh is a no-op");
        }

        /// <summary>
        /// Reversion must depend on elapsed market cycles, not on how many times something happened
        /// to call it — that is the entire reason <c>lastAdvancedRefresh</c> is persisted. A save
        /// reopened ten cycles later must land where a running game would have put it.
        /// </summary>
        private static void CheckReversionIsDrivenByElapsedCycles(Results r)
        {
            const IntercolonyProductCategory Category = IntercolonyProductCategory.Commodities;
            int index = (int)Category;

            SettlementMarketState stepped = new SettlementMarketState(9101) { lastAdvancedRefresh = 0 };
            stepped.demandPressure[index] = 1.50f;
            for (int refresh = 1; refresh <= 5; refresh++)
            {
                MarketPressureService.Advance(stepped, refresh);
            }

            SettlementMarketState jumped = new SettlementMarketState(9102) { lastAdvancedRefresh = 0 };
            jumped.demandPressure[index] = 1.50f;
            MarketPressureService.Advance(jumped, 5);

            float steppedValue = stepped.DemandPressureFor(Category);
            float jumpedValue = jumped.DemandPressureFor(Category);
            r.Check(Mathf.Abs(steppedValue - jumpedValue) < 0.0001f,
                "five single-cycle advances land where one five-cycle advance lands",
                $"{steppedValue:F5} vs {jumpedValue:F5}");

            r.Check(!MarketPressureService.Advance(jumped, 5),
                "advancing to a refresh already accounted for changes nothing");
            r.Check(Mathf.Approximately(jumped.DemandPressureFor(Category), jumpedValue),
                "and leaves the value untouched");

            r.Check(!MarketPressureService.Advance(jumped, 2),
                "advancing backwards is refused rather than amplifying pressure");
            r.Check(jumped.DemandPressureFor(Category) <= jumpedValue + 0.0001f,
                "a backwards advance never increases pressure");

            // The sentinel is a state, not a small number. Subtracting from it would compute an
            // elapsed span of toRefresh + 1 and erase a fresh shock a cycle early.
            SettlementMarketState fresh = new SettlementMarketState(9103);
            fresh.demandPressure[index] = 1.50f;
            r.Check(fresh.lastAdvancedRefresh == SettlementMarketState.NeverAdvanced,
                "a new record has never been advanced");

            bool moved = MarketPressureService.Advance(fresh, 400);
            r.Check(!moved, "a never-advanced record is stamped rather than reverted");
            r.Check(Mathf.Approximately(fresh.DemandPressureFor(Category), 1.50f),
                "so a fresh shock survives its first advance at full strength",
                fresh.DemandPressureFor(Category).ToString("F4"));
            r.Check(fresh.lastAdvancedRefresh == 400,
                "and it now has a baseline to decay from");

            // A save that sat for a very long time must still land somewhere sane.
            SettlementMarketState ancient = new SettlementMarketState(9104) { lastAdvancedRefresh = 0 };
            ancient.demandPressure[index] = MarketPressureService.MaxPressure;
            MarketPressureService.Advance(ancient, 100_000);
            r.Check(ancient.IsNeutral,
                "a record advanced across an enormous gap settles rather than diverging",
                ancient.DemandPressureFor(Category).ToString("F4"));
        }

        private static void CheckShockBounds(Results r, IntercolonyWorldComponent state)
        {
            const IntercolonyProductCategory Category = IntercolonyProductCategory.ManufacturedGoods;
            state.MarketStates.RemoveAll(s => s != null && s.settlementId == ProbeSettlementId);

            float raised = MarketPressureService.ApplyDemandShock(
                state, ProbeSettlementId, Category, 0.25f);
            r.Check(raised > SettlementMarketState.Neutral,
                "a positive demand shock raises pressure", raised.ToString("F3"));

            SettlementMarketState record = state.MarketStateFor(ProbeSettlementId);
            r.Check(record != null, "shocking an undisturbed settlement creates its record");
            r.Check(record != null && record.lastAdvancedRefresh != SettlementMarketState.NeverAdvanced,
                "and stamps it, so it does not get a free cycle at full strength");

            float clampedHigh = MarketPressureService.ApplyDemandShock(
                state, ProbeSettlementId, Category, 99f);
            r.Check(Mathf.Approximately(clampedHigh, MarketPressureService.MaxPressure),
                "an absurd shock clamps to the ceiling rather than distorting the economy",
                clampedHigh.ToString("F3"));

            float clampedLow = MarketPressureService.ApplySupplyShock(
                state, ProbeSettlementId, Category, -99f);
            r.Check(Mathf.Approximately(clampedLow, MarketPressureService.MinPressure),
                "and to the floor from below", clampedLow.ToString("F3"));

            r.Check(MarketPressureService.MinPressure > 0f,
                "the floor is above zero — zero supply would mean nothing exists, not a glut");
            r.Check(
                Mathf.Approximately(
                    MarketPressureService.MinPressure * MarketPressureService.MaxPressure, 1f),
                "floor and ceiling are exact inverses, so a glut is as strong as the same shortage",
                $"{MarketPressureService.MinPressure:F4} x {MarketPressureService.MaxPressure:F2}");

            state.MarketStates.RemoveAll(s => s != null && s.settlementId == ProbeSettlementId);
            state.RefreshMarketStateIndex();
        }

        /// <summary>
        /// The two halves of the sparse design have to meet: reversion must actually reach the
        /// prune epsilon, or records accumulate forever and the save only grows.
        /// </summary>
        private static void CheckReversionSettlesAndPrunes(Results r, IntercolonyWorldComponent state)
        {
            state.MarketStates.RemoveAll(s => s != null && s.settlementId == ProbeSettlementId);

            SettlementMarketState probe =
                state.MarketStateFor(ProbeSettlementId, createIfMissing: true);
            probe.lastAdvancedRefresh = 0;
            probe.demandPressure[(int)IntercolonyProductCategory.Commodities] =
                MarketPressureService.MaxPressure;

            int refreshesToSettle = 0;
            for (int refresh = 1; refresh <= 200 && !probe.IsNeutral; refresh++)
            {
                MarketPressureService.Advance(probe, refresh);
                refreshesToSettle = refresh;
            }

            r.Check(probe.IsNeutral,
                "the strongest possible shock does eventually settle within the prune epsilon",
                $"{refreshesToSettle} refresh(es)");
            r.Check(refreshesToSettle > 5,
                "but not so fast that a shock is over before the player can trade on it",
                $"{refreshesToSettle} refresh(es)");

            state.PruneNeutralMarketStates();
            r.Check(state.MarketStateFor(ProbeSettlementId) == null,
                "and the settled record is then pruned, keeping the save sparse");
        }

        private static string Summarize(Results r)
        {
            r.sb.AppendLine();
            r.sb.AppendLine($"  {r.passed} passed, {r.failed} failed.");
            return r.sb.ToString();
        }
    }
}
