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
    /// Self-test for the Stage 4A product-brand record and Stage 4B similarity service:
    /// persistence, bounds, load pruning, neutral initialization and def-driven carryover. It
    /// does not read ProductBrandRecord from the similarity checks because this slice owns only
    /// the similarity function.
    /// </summary>
    public static class IntercolonyBrandSelfTest
    {
        // These are regression sentinels, not aliases for the service's tunable constants. If a
        // future edit collapses every tier to one middling value, the Core examples must turn red
        // instead of moving their goalposts with that edit.
        private const float NarrowFamilyAcceptanceMinimum = 0.93f;
        private const float NarrowFamilyAcceptanceMaximum = 0.97f;
        private const float SameIndustryAcceptanceMinimum = 0.60f;
        private const float SameBroadCategoryAcceptanceMinimum = 0.20f;
        private const float SameBroadCategoryAcceptanceMaximum = 0.50f;
        private const float UnrelatedAcceptanceMinimum = 0.03f;
        private const float UnrelatedAcceptanceMaximum = 0.07f;

        private sealed class Results
        {
            public readonly StringBuilder sb = new StringBuilder();
            public int passed;
            public int failed;
            public int skipped;

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

            public void Skip(string label, string detail)
            {
                skipped++;
                sb.AppendLine($"  SKIP {label}  ({detail})");
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

                RunProductSimilarityChecks(r);
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
            r.sb.AppendLine(
                $"  {r.passed} passed, {r.failed} failed" +
                (r.skipped == 0 ? "." : $", {r.skipped} skipped."));
            return r.sb.ToString();
        }

        private static void RunProductSimilarityChecks(Results r)
        {
            ThingDef revolver = ResolveThingDef("Gun_Revolver");
            ThingDef boltActionRifle = ResolveThingDef("Gun_BoltActionRifle");
            ThingDef anotherRangedFirearm = ResolveThingDef("Gun_Autopistol");
            ThingDef chair = ResolveThingDef("DiningChair");
            ThingDef table = ResolveThingDef("Table1x2c");
            ThingDef apparel = ResolveThingDef("Apparel_Parka");
            ThingDef steel = ResolveThingDef("Steel");
            ThingDef gold = ResolveThingDef("Gold");

            if (RequirePair(
                    r, "revolver to bolt-action rifle is in the narrow-family band",
                    "Gun_Revolver", revolver, "Gun_BoltActionRifle", boltActionRifle))
            {
                float similarity = ProductSimilarityService.GetSimilarity(revolver, boltActionRifle);
                r.Check(
                    similarity >= NarrowFamilyAcceptanceMinimum &&
                    similarity <= NarrowFamilyAcceptanceMaximum &&
                    similarity <= 1.0f,
                    "revolver to bolt-action rifle is VERY HIGH",
                    $"similarity={similarity:0.000}, expected band=[" +
                    $"{NarrowFamilyAcceptanceMinimum:0.00}," +
                    $"{NarrowFamilyAcceptanceMaximum:0.00}]");
            }

            if (RequirePair(
                    r, "revolver to another ranged firearm has a loaded pair",
                    "Gun_Revolver", revolver, "Gun_Autopistol", anotherRangedFirearm))
            {
                float similarity = ProductSimilarityService.GetSimilarity(
                    revolver, anotherRangedFirearm);
                r.Check(
                    similarity >= SameIndustryAcceptanceMinimum &&
                    similarity <= 1.0f,
                    "revolver to another ranged firearm is HIGH",
                    $"similarity={similarity:0.000}, expected >= " +
                    $"{SameIndustryAcceptanceMinimum:0.00}");
            }

            if (RequirePair(
                    r, "chair to table has a loaded furniture pair",
                    "DiningChair", chair, "Table1x2c", table))
            {
                float similarity = ProductSimilarityService.GetSimilarity(chair, table);
                r.Check(
                    similarity >= SameBroadCategoryAcceptanceMinimum &&
                    similarity <= SameBroadCategoryAcceptanceMaximum,
                    "chair to table is MEANINGFULLY RELATED",
                    $"similarity={similarity:0.000}, expected band=[" +
                    $"{SameBroadCategoryAcceptanceMinimum:0.00}," +
                    $"{SameBroadCategoryAcceptanceMaximum:0.00}]");
            }

            if (RequirePair(
                    r, "chair to bolt-action rifle has a loaded unrelated pair",
                    "DiningChair", chair, "Gun_BoltActionRifle", boltActionRifle))
            {
                float similarity = ProductSimilarityService.GetSimilarity(chair, boltActionRifle);
                r.Check(
                    similarity >= UnrelatedAcceptanceMinimum &&
                    similarity <= UnrelatedAcceptanceMaximum,
                    "chair to bolt-action rifle is NEAR THE FLOOR",
                    $"similarity={similarity:0.000}, expected band=[" +
                    $"{UnrelatedAcceptanceMinimum:0.00}," +
                    $"{UnrelatedAcceptanceMaximum:0.00}]");
            }

            if (RequirePair(
                    r, "apparel to furniture has a loaded cross-industry pair",
                    "Apparel_Parka", apparel, "DiningChair", chair))
            {
                float similarity = ProductSimilarityService.GetSimilarity(apparel, chair);
                r.Check(
                    similarity >= UnrelatedAcceptanceMinimum &&
                    similarity <= UnrelatedAcceptanceMaximum,
                    "apparel to furniture is LOW",
                    $"similarity={similarity:0.000}, expected band=[" +
                    $"{UnrelatedAcceptanceMinimum:0.00}," +
                    $"{UnrelatedAcceptanceMaximum:0.00}]");
            }

            if (RequirePair(
                    r, "self-similarity has a loaded revolver def",
                    "Gun_Revolver", revolver, "Gun_Revolver", revolver))
            {
                float similarity = ProductSimilarityService.GetSimilarity(revolver, revolver);
                r.Check(
                    similarity == 1.0f,
                    "self-similarity is exactly 1.0",
                    $"similarity={similarity:0.000}");
            }

            float nullLeft = ProductSimilarityService.GetSimilarity(null, ThingDefOf.Silver);
            float nullRight = ProductSimilarityService.GetSimilarity(ThingDefOf.Silver, null);
            float bothNull = ProductSimilarityService.GetSimilarity(null, null);
            r.Check(
                nullLeft == ProductSimilarityService.UnrelatedFloor &&
                nullRight == ProductSimilarityService.UnrelatedFloor &&
                bothNull == ProductSimilarityService.UnrelatedFloor,
                "a null def on either side returns the unrelated floor",
                $"left={nullLeft:0.000}, right={nullRight:0.000}, both={bothNull:0.000}");

            CheckSymmetry(
                r, "symmetry: revolver and bolt-action rifle",
                "Gun_Revolver", revolver, "Gun_BoltActionRifle", boltActionRifle);
            CheckSymmetry(
                r, "symmetry: chair and table",
                "DiningChair", chair, "Table1x2c", table);
            CheckSymmetry(
                r, "symmetry: apparel and furniture",
                "Apparel_Parka", apparel, "DiningChair", chair);

            CheckBounds(
                r, "bounds: revolver to bolt-action rifle",
                "Gun_Revolver", revolver, "Gun_BoltActionRifle", boltActionRifle);
            CheckBounds(
                r, "bounds: revolver to another ranged firearm",
                "Gun_Revolver", revolver, "Gun_Autopistol", anotherRangedFirearm);
            CheckBounds(
                r, "bounds: chair to table",
                "DiningChair", chair, "Table1x2c", table);
            CheckBounds(
                r, "bounds: chair to bolt-action rifle",
                "DiningChair", chair, "Gun_BoltActionRifle", boltActionRifle);
            CheckBounds(
                r, "bounds: apparel to furniture",
                "Apparel_Parka", apparel, "DiningChair", chair);

            if (RequirePair(
                    r, "debug explanation has a loaded narrow-family pair",
                    "Gun_Revolver", revolver, "Gun_BoltActionRifle", boltActionRifle))
            {
                string explanation = ProductSimilarityService.Explain(revolver, boltActionRifle);
                r.Check(
                    explanation.IndexOf("Evidence:", StringComparison.Ordinal) >= 0 &&
                    explanation.IndexOf("narrow family", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    explanation.IndexOf("Gun_Revolver", StringComparison.Ordinal) >= 0 &&
                    explanation.IndexOf("Gun_BoltActionRifle", StringComparison.Ordinal) >= 0,
                    "debug output names the matched narrow-family evidence",
                    explanation.Replace(Environment.NewLine, " "));
            }

            if (RequirePair(
                    r, "missing-metadata fallback has loaded resource defs",
                    "Steel", steel, "Gold", gold))
            {
                float similarity = ProductSimilarityService.UnrelatedFloor;
                string failure = null;
                try
                {
                    similarity = ProductSimilarityService.GetSimilarity(steel, gold);
                }
                catch (Exception ex)
                {
                    failure = $"{ex.GetType().Name}: {ex.Message}";
                }

                r.Check(
                    failure == null &&
                    similarity >= ProductSimilarityService.UnrelatedFloor &&
                    similarity <= 1.0f,
                    "a def lacking weapon/apparel/building metadata falls back safely",
                    failure ?? $"similarity={similarity:0.000}");
            }
        }

        private static ThingDef ResolveThingDef(string defName)
        {
            return DefDatabase<ThingDef>.GetNamedSilentFail(defName);
        }

        private static bool RequirePair(
            Results r, string label,
            string leftName, ThingDef left,
            string rightName, ThingDef right)
        {
            if (left != null && right != null)
            {
                return true;
            }

            StringBuilder missing = new StringBuilder();
            if (left == null)
            {
                missing.Append(leftName);
            }

            if (right == null)
            {
                if (missing.Length > 0)
                {
                    missing.Append(", ");
                }

                missing.Append(rightName);
            }

            r.Skip(label, $"missing loaded ThingDef(s): {missing}");
            return false;
        }

        private static void CheckSymmetry(
            Results r, string label,
            string leftName, ThingDef left,
            string rightName, ThingDef right)
        {
            if (!RequirePair(r, label, leftName, left, rightName, right))
            {
                return;
            }

            float forward = ProductSimilarityService.GetSimilarity(left, right);
            float reverse = ProductSimilarityService.GetSimilarity(right, left);
            r.Check(
                forward == reverse,
                label,
                $"forward={forward:0.000}, reverse={reverse:0.000}");
        }

        private static void CheckBounds(
            Results r, string label,
            string leftName, ThingDef left,
            string rightName, ThingDef right)
        {
            if (!RequirePair(r, label, leftName, left, rightName, right))
            {
                return;
            }

            float similarity = ProductSimilarityService.GetSimilarity(left, right);
            r.Check(
                similarity >= ProductSimilarityService.UnrelatedFloor &&
                similarity <= 1.0f,
                label,
                $"similarity={similarity:0.000}, bounds=[" +
                $"{ProductSimilarityService.UnrelatedFloor:0.000}," +
                "1.000]");
        }
    }
}
