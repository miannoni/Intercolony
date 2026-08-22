using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Self-test for the inert Stage 4A/4B product-brand record: persistence, bounds, load
    /// pruning and neutral initialization. It does not assert what brand means because no slice
    /// computes brand yet.
    /// </summary>
    public static class IntercolonyBrandSelfTest
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
            r.sb.AppendLine("Product brand record self-test (Stage 4A/4B)");

            if (state == null)
            {
                r.sb.AppendLine("  No world state available. Open or load a game first.");
                return Summarize(r);
            }

            // Contents, not count. The load-pruning assertion replaces the list with synthetic
            // records; restoring only its old length could leave those records in the player's
            // world state if a future test removes or inserts a different number of entries.
            List<ProductBrandRecord> saved = new List<ProductBrandRecord>(state.ProductBrandRecords);

            try
            {
                ThingDef def = ThingDefOf.Silver;
                ProductBrandRecord original = new ProductBrandRecord(
                    def, directScore: 37.25f, evidenceWeight: 18.5f, unitsDelivered: 240);
                List<ProductBrandRecord> loaded = RoundTrip(
                    new List<ProductBrandRecord> { original }, out string roundTripFailure,
                    "Intercolony-ProductBrandRecord");
                ProductBrandRecord copy = loaded != null && loaded.Count == 1 ? loaded[0] : null;

                r.Check(
                    roundTripFailure == null &&
                    copy != null &&
                    copy.thingDef == def &&
                    Mathf.Approximately(copy.directScore, 37.25f) &&
                    Mathf.Approximately(copy.evidenceWeight, 18.5f) &&
                    copy.unitsDelivered == 240,
                    "a Scribe round trip preserves every product-brand field",
                    roundTripFailure);

                ProductBrandRecord high = new ProductBrandRecord();
                ProductBrandRecord low = new ProductBrandRecord();
                high.directScore = ProductBrandRecord.MaxScore + 50f;
                low.directScore = ProductBrandRecord.MinScore - 50f;
                r.Check(
                    Mathf.Approximately(high.directScore, ProductBrandRecord.MaxScore) &&
                    Mathf.Approximately(low.directScore, ProductBrandRecord.MinScore),
                    "directScore clamps at both brand bounds",
                    $"high={high.directScore:0.##}, low={low.directScore:0.##}");

                List<ProductBrandRecord> withUnresolved = RoundTrip(
                    new List<ProductBrandRecord>
                    {
                        new ProductBrandRecord(def, directScore: 12f, evidenceWeight: 4f, unitsDelivered: 8),
                        new ProductBrandRecord(null, directScore: -22f, evidenceWeight: 9f, unitsDelivered: 3)
                    }, out string unresolvedFailure, "Intercolony-ProductBrandRecords");
                state.ProductBrandRecords.Clear();
                if (withUnresolved != null)
                {
                    state.ProductBrandRecords.AddRange(withUnresolved);
                }

                int dropped = IntercolonyWorldComponent.PruneLoadedProductBrandRecords(
                    state.ProductBrandRecords);
                r.Check(
                    unresolvedFailure == null &&
                    dropped == 1 &&
                    state.ProductBrandRecords.Count == 1 &&
                    state.ProductBrandRecords[0].thingDef == def,
                    "an unresolved ThingDef record is dropped while a valid neighbour survives",
                    unresolvedFailure ?? $"dropped={dropped}, retained={state.ProductBrandRecords.Count}");

                ProductBrandRecord fresh = new ProductBrandRecord();
                r.Check(
                    Mathf.Approximately(ProductBrandRecord.Neutral, 0f) &&
                    Mathf.Approximately(fresh.directScore, ProductBrandRecord.Neutral) &&
                    Mathf.Approximately(fresh.directScore, 0f) &&
                    Mathf.Approximately(fresh.evidenceWeight, 0f) &&
                    fresh.unitsDelivered == 0,
                    "neutral is exactly zero and a fresh record has zero evidence");
            }
            catch (Exception ex)
            {
                r.sb.AppendLine($"  EXCEPTION: {ex}");
                r.failed++;
            }
            finally
            {
                state.ProductBrandRecords.Clear();
                state.ProductBrandRecords.AddRange(saved);
                r.sb.AppendLine(
                    $"        product brand records restored to {state.ProductBrandRecords.Count}.");
            }

            return Summarize(r);
        }

        private static List<ProductBrandRecord> RoundTrip(
            List<ProductBrandRecord> savedList, out string failure, string rootName)
        {
            List<ProductBrandRecord> loadedList = null;
            failure = null;
            string tempPath = Path.Combine(
                Path.GetTempPath(), $"{rootName}-{Guid.NewGuid():N}.xml");

            try
            {
                Scribe.saver.InitSaving(tempPath, "intercolonyProductBrandTest");
                Scribe_Collections.Look(ref savedList, "productBrandRecords", LookMode.Deep);
                Scribe.saver.FinalizeSaving();

                Scribe.loader.InitLoading(tempPath);
                Scribe_Collections.Look(ref loadedList, "productBrandRecords", LookMode.Deep);
                Scribe.loader.FinalizeLoading();
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                Scribe.ForceStop();
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }

            return loadedList;
        }

        private static string Summarize(Results r)
        {
            r.sb.AppendLine();
            r.sb.AppendLine($"  {r.passed} passed, {r.failed} failed.");
            return r.sb.ToString();
        }
    }
}
