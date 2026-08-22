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
    /// Self-test for the Stage 4A product-brand record, Stage 4B similarity service, Stage 4C
    /// quality capture and completed-sale brand update, Stage 4D effective-brand read model, and
    /// Stage 4E Parts One and Two: persistence, bounds, load pruning, neutral initialization,
    /// actual batch quality, gradual volume-weighted brand movement, def-driven carryover, direct
    /// evidence confidence, the bounded prospective price factor, and bounded Find Buyer interest.
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

        // A single settlement can sit on either side of the gate by chance. These checks compare
        // the full Find Buyer sample, and skip honestly when the loaded world cannot provide a
        // large enough accessible sample to make an aggregate threshold result meaningful.
        private const int MinimumBrandInterestSampleSettlements = 12;

        // Keep this as an independent regression sentinel rather than an alias for the service's
        // tunable shift. If the production shift is mutated to zero, the assertions must still
        // run when the sampled neutral demand has genuine 0.10-point crossing headroom.
        private const float ExpectedBrandInterestShiftDistance = 0.10f;

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
            r.sb.AppendLine("Product brand record self-test (Stage 4A/4B/4C/4D/4E Part One)");

            if (state == null)
            {
                r.sb.AppendLine("  No world state available. Open or load a game first.");
                return Summarize(r);
            }

            // Contents, not count. The load-pruning assertion replaces the list with synthetic
            // records; restoring only its old length could leave those records in the player's
            // world state if a future test removes or inserts a different number of entries.
            List<ProductBrandRecord> saved = SnapshotBrandRecords(state.ProductBrandRecords);

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

                RunDeliveredQualityChecks(r);
                RunDeliveredBrandUpdateChecks(state, r);
                RunProductSimilarityChecks(r);
                RunEffectiveBrandChecks(state, r);
                RunBrandPricingChecks(state, r);
                RunBrandInterestChecks(state, r);
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

        private static void RunDeliveredBrandUpdateChecks(
            IntercolonyWorldComponent state, Results r)
        {
            ThingDef product = ThingDefOf.DiningChair;
            List<ProductBrandRecord> saved = SnapshotBrandRecords(state.ProductBrandRecords);
            List<CommercialHistoryEntry> savedCommercialHistory =
                SnapshotCommercialHistory(state.CommercialHistory);
            List<CommercialEventRecord> savedCommercialTimeline =
                new List<CommercialEventRecord>(state.CommercialTimeline);
            int savedTimelineStartTick = state.CommercialTimelineStartTick;

            try
            {
                state.ProductBrandRecords.Clear();
                ProductBrandRecord smallMasterwork =
                    ProductBrandService.ApplyDeliveredQuality(
                        state, product,
                        new DeliveredQualityResult(
                            DeliveredQualityCapture.MasterworkQualityTarget, 1));
                r.Check(
                    smallMasterwork != null &&
                    smallMasterwork.directScore > ProductBrandRecord.Neutral &&
                    smallMasterwork.directScore <
                        DeliveredQualityCapture.MasterworkQualityTarget * 0.25f,
                    "one small Masterwork delivery moves brand up without jumping to its target",
                    $"score={smallMasterwork?.directScore ?? float.NaN:0.###}, " +
                    $"target={DeliveredQualityCapture.MasterworkQualityTarget:0.##}");

                const int splitTotalUnits = 100;
                const int splitDeliveryUnits = 10;
                const float startingScore = 37f;
                state.ProductBrandRecords.Clear();
                state.ProductBrandRecords.Add(new ProductBrandRecord(
                    product, startingScore, evidenceWeight: 0f, unitsDelivered: 0));
                ProductBrandRecord oneDelivery =
                    ProductBrandService.ApplyDeliveredQuality(
                        state, product,
                        new DeliveredQualityResult(
                            DeliveredQualityCapture.ExcellentQualityTarget, splitTotalUnits));
                float oneDeliveryScore = oneDelivery.directScore;

                state.ProductBrandRecords.Clear();
                state.ProductBrandRecords.Add(new ProductBrandRecord(
                    product, startingScore, evidenceWeight: 0f, unitsDelivered: 0));
                for (int i = 0; i < splitTotalUnits / splitDeliveryUnits; i++)
                {
                    ProductBrandService.ApplyDeliveredQuality(
                        state, product,
                        new DeliveredQualityResult(
                            DeliveredQualityCapture.ExcellentQualityTarget,
                            splitDeliveryUnits));
                }

                ProductBrandRecord splitDeliveries = FindBrandRecord(
                    state.ProductBrandRecords, product);
                r.Check(
                    splitDeliveries != null &&
                    Mathf.Abs(oneDeliveryScore - splitDeliveries.directScore) < 0.001f &&
                    splitDeliveries.unitsDelivered == splitTotalUnits,
                    "one delivery and ten equal-quality split deliveries reach the same final score",
                    $"one={oneDeliveryScore:0.######}, split={splitDeliveries?.directScore ?? float.NaN:0.######}, " +
                    $"epsilon=0.001, units={splitDeliveries?.unitsDelivered ?? -1}");

                state.ProductBrandRecords.Clear();
                ProductBrandRecord positive = null;
                float firstPositiveScore = float.NaN;
                for (int i = 0; i < 20; i++)
                {
                    positive = ProductBrandService.ApplyDeliveredQuality(
                        state, product,
                        new DeliveredQualityResult(
                            DeliveredQualityCapture.MasterworkQualityTarget, 10));
                    if (i == 0)
                    {
                        firstPositiveScore = positive.directScore;
                    }
                }

                r.Check(
                    positive != null &&
                    firstPositiveScore > ProductBrandRecord.Neutral &&
                    positive.directScore > firstPositiveScore &&
                    positive.directScore >
                        DeliveredQualityCapture.MasterworkQualityTarget * 0.90f &&
                    positive.directScore <= ProductBrandRecord.MaxScore &&
                    Mathf.Approximately(positive.evidenceWeight, 200f) &&
                    positive.unitsDelivered == 200,
                    "repeated Masterwork deliveries build positive brand toward, not beyond, the bound",
                    $"first={firstPositiveScore:0.###}, final={positive?.directScore ?? float.NaN:0.###}, " +
                    $"evidence={positive?.evidenceWeight ?? float.NaN:0.###}, " +
                    $"units={positive?.unitsDelivered ?? -1}");

                state.ProductBrandRecords.Clear();
                ProductBrandRecord negative = null;
                float firstNegativeScore = float.NaN;
                for (int i = 0; i < 20; i++)
                {
                    negative = ProductBrandService.ApplyDeliveredQuality(
                        state, product,
                        new DeliveredQualityResult(
                            DeliveredQualityCapture.AwfulQualityTarget, 10));
                    if (i == 0)
                    {
                        firstNegativeScore = negative.directScore;
                    }
                }

                r.Check(
                    negative != null &&
                    firstNegativeScore < ProductBrandRecord.Neutral &&
                    negative.directScore < firstNegativeScore &&
                    negative.directScore <
                        DeliveredQualityCapture.AwfulQualityTarget * 0.90f &&
                    negative.directScore >= ProductBrandRecord.MinScore &&
                    Mathf.Approximately(negative.evidenceWeight, 200f) &&
                    negative.unitsDelivered == 200,
                    "repeated Awful deliveries build negative brand symmetrically within the bound",
                    $"first={firstNegativeScore:0.###}, final={negative?.directScore ?? float.NaN:0.###}, " +
                    $"evidence={negative?.evidenceWeight ?? float.NaN:0.###}, " +
                    $"units={negative?.unitsDelivered ?? -1}");

                state.ProductBrandRecords.Clear();
                ProductBrandRecord diluted = new ProductBrandRecord(
                    product, directScore: 85f, evidenceWeight: 100f, unitsDelivered: 100);
                state.ProductBrandRecords.Add(diluted);
                ProductBrandService.ApplyDeliveredQuality(
                    state, product,
                    new DeliveredQualityResult(
                        DeliveredQualityCapture.NormalQualityTarget, 200));
                r.Check(
                    diluted.directScore >= ProductBrandRecord.Neutral &&
                    diluted.directScore < 10f &&
                    diluted.directScore < 85f &&
                    diluted.evidenceWeight == 300f &&
                    diluted.unitsDelivered == 300,
                    "a large Normal flood dilutes an established positive brand toward neutral",
                    $"before=85, after={diluted.directScore:0.###}, " +
                    $"evidence={diluted.evidenceWeight:0.###}, units={diluted.unitsDelivered}");

                state.ProductBrandRecords.Clear();
                ProductBrandRecord untouched = new ProductBrandRecord(
                    product, directScore: 42f, evidenceWeight: 17.5f, unitsDelivered: 99);
                state.ProductBrandRecords.Add(untouched);
                ProductBrandRecord noEvidenceResult = ProductBrandService.ApplyDeliveredQuality(
                    state, product, DeliveredQualityResult.NoEvidence);
                bool existingWasUntouched =
                    noEvidenceResult == null &&
                    state.ProductBrandRecords.Count == 1 &&
                    Mathf.Approximately(untouched.directScore, 42f) &&
                    Mathf.Approximately(untouched.evidenceWeight, 17.5f) &&
                    untouched.unitsDelivered == 99;
                state.ProductBrandRecords.Clear();
                ProductBrandService.ApplyDeliveredQuality(
                    state, product, DeliveredQualityResult.NoEvidence);
                r.Check(
                    existingWasUntouched && state.ProductBrandRecords.Count == 0,
                    "a delivery with no quality-carrying units changes neither evidence nor brand",
                    $"existingUntouched={existingWasUntouched}, " +
                    $"newRecordCount={state.ProductBrandRecords.Count}");

                state.ProductBrandRecords.Clear();
                SalesOrder constructed = new SalesOrder
                {
                    line = new OrderLine(product, 1),
                    status = SalesOrderStatus.Accepted,
                    deadlineTick = int.MaxValue,
                    deliveredQuantity = 1
                };
                bool untouchedBeforeCompletion =
                    state.ProductBrandRecords.Count == 0 &&
                    !constructed.ActualDeliveredQuality.HasQualityEvidence;
                SalesOrderService.Complete(
                    state,
                    constructed,
                    completedTick: 0,
                    outcomeNote: "brand update self-test",
                    actualDeliveredQuality: new DeliveredQualityResult(
                        DeliveredQualityCapture.GoodQualityTarget, 1));
                ProductBrandRecord completedRecord = FindBrandRecord(
                    state.ProductBrandRecords, product);
                float completedScore = completedRecord?.directScore ?? float.NaN;
                SalesOrderService.Complete(
                    state,
                    constructed,
                    completedTick: 1,
                    outcomeNote: "must not apply twice",
                    actualDeliveredQuality: new DeliveredQualityResult(
                        DeliveredQualityCapture.AwfulQualityTarget, 100));
                r.Check(
                    untouchedBeforeCompletion &&
                    constructed.status == SalesOrderStatus.Completed &&
                    completedRecord != null &&
                    completedScore > ProductBrandRecord.Neutral &&
                    Mathf.Approximately(completedRecord.directScore, completedScore) &&
                    completedRecord.unitsDelivered == 1,
                    "brand moves only when SalesOrderService reaches its real completion boundary",
                    $"beforeRecords={(untouchedBeforeCompletion ? 0 : state.ProductBrandRecords.Count)}, " +
                    $"status={constructed.status}, score={completedRecord?.directScore ?? float.NaN:0.###}, " +
                    $"units={completedRecord?.unitsDelivered ?? -1}");
            }
            finally
            {
                // The real completion boundary also records ordinary sale history. Restore those
                // incidental fixtures so this focused brand suite cannot write a synthetic trade
                // into the player's world while it is proving the brand transition.
                state.CommercialHistory.Clear();
                state.CommercialHistory.AddRange(savedCommercialHistory);
                state.CommercialTimeline.Clear();
                state.CommercialTimeline.AddRange(savedCommercialTimeline);
                state.CommercialTimelineStartTick = savedTimelineStartTick;
                state.ProductBrandRecords.Clear();
                state.ProductBrandRecords.AddRange(saved);
            }
        }

        private static ProductBrandRecord FindBrandRecord(
            List<ProductBrandRecord> records, ThingDef product)
        {
            if (records == null || product == null)
            {
                return null;
            }

            for (int i = 0; i < records.Count; i++)
            {
                ProductBrandRecord record = records[i];
                if (record != null && ReferenceEquals(record.thingDef, product))
                {
                    return record;
                }
            }

            return null;
        }

        private static List<CommercialHistoryEntry> SnapshotCommercialHistory(
            List<CommercialHistoryEntry> entries)
        {
            List<CommercialHistoryEntry> snapshot =
                new List<CommercialHistoryEntry>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                CommercialHistoryEntry entry = entries[i];
                snapshot.Add(entry == null
                    ? null
                    : new CommercialHistoryEntry
                    {
                        settlementId = entry.settlementId,
                        thingDef = entry.thingDef,
                        completedSaleCount = entry.completedSaleCount,
                        totalQuantitySupplied = entry.totalQuantitySupplied
                    });
            }

            return snapshot;
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

        private static void RunDeliveredQualityChecks(Results r)
        {
            List<Thing> cleanup = new List<Thing>();

            try
            {
                Thing awful = MakeQualityThing(QualityCategory.Awful);
                Thing normal = MakeQualityThing(QualityCategory.Normal);
                Thing masterwork = MakeQualityThing(QualityCategory.Masterwork);
                cleanup.Add(awful);
                cleanup.Add(normal);
                cleanup.Add(masterwork);

                DeliveredQualityResult awfulResult = DeliveredQualityCapture.FromThings(
                    new[] { awful });
                DeliveredQualityResult normalResult = DeliveredQualityCapture.FromThings(
                    new[] { normal });
                DeliveredQualityResult masterworkResult = DeliveredQualityCapture.FromThings(
                    new[] { masterwork });
                r.Check(
                    Mathf.Approximately(
                        awfulResult.QualityTarget, DeliveredQualityCapture.AwfulQualityTarget) &&
                    Mathf.Approximately(
                        normalResult.QualityTarget, DeliveredQualityCapture.NormalQualityTarget) &&
                    Mathf.Approximately(
                        masterworkResult.QualityTarget,
                        DeliveredQualityCapture.MasterworkQualityTarget) &&
                    awfulResult.QualityTarget < 0f &&
                    Mathf.Approximately(normalResult.QualityTarget, 0f) &&
                    masterworkResult.QualityTarget > 50f &&
                    awfulResult.QualityEvidenceUnits == 1 &&
                    normalResult.QualityEvidenceUnits == 1 &&
                    masterworkResult.QualityEvidenceUnits == 1,
                    "quality targets preserve negative, neutral and strongly positive tiers",
                    $"awful={awfulResult.QualityTarget:0.##}, " +
                    $"normal={normalResult.QualityTarget:0.##}, " +
                    $"masterwork={masterworkResult.QualityTarget:0.##}");

                DeliveredQualityBatch mixedBatch = DeliveredQualityCapture.BeginBatch();
                mixedBatch.Add(normal, 20);
                mixedBatch.Add(masterwork, 1);
                DeliveredQualityResult mixedResult = mixedBatch.Result;
                float expectedMixedTarget = (
                    20f * DeliveredQualityCapture.NormalQualityTarget +
                    DeliveredQualityCapture.MasterworkQualityTarget) / 21f;
                r.Check(
                    mixedResult.QualityEvidenceUnits == 21 &&
                    Mathf.Abs(mixedResult.QualityTarget - expectedMixedTarget) < 0.001f &&
                    !Mathf.Approximately(
                        mixedResult.QualityTarget,
                        (DeliveredQualityCapture.NormalQualityTarget +
                         DeliveredQualityCapture.MasterworkQualityTarget) / 2f),
                    "a mixed batch is weighted by delivered unit count",
                    $"captured={mixedResult.QualityTarget:0.###}, " +
                    $"expected={expectedMixedTarget:0.###}");

                Thing steel = ThingMaker.MakeThing(ThingDefOf.Steel);
                cleanup.Add(steel);
                steel.stackCount = 12;
                DeliveredQualityResult noQualityResult = DeliveredQualityCapture.FromThings(
                    new[] { steel });
                r.Check(
                    noQualityResult.QualityEvidenceUnits == 0 &&
                    Mathf.Approximately(noQualityResult.QualityTarget, 0f) &&
                    !noQualityResult.HasQualityEvidence,
                    "goods without a quality component contribute no quality evidence",
                    $"target={noQualityResult.QualityTarget:0.##}, " +
                    $"evidenceUnits={noQualityResult.QualityEvidenceUnits}");

                Thing deliveredGood = MakeQualityThing(QualityCategory.Good);
                cleanup.Add(deliveredGood);
                OrderLine requested = new OrderLine(ThingDefOf.DiningChair, 1)
                {
                    minQuality = QualityCategory.Masterwork
                };
                DeliveredQualityResult captured = DeliveredQualityCapture.FromThings(
                    new[] { deliveredGood });
                SalesOrder completed = new SalesOrder
                {
                    line = requested,
                    status = SalesOrderStatus.Accepted,
                    deadlineTick = int.MaxValue,
                    deliveredQuantity = 1
                };
                SalesOrderService.Complete(
                    null, completed, 0, "quality capture self-test", captured);
                r.Check(
                    captured.QualityEvidenceUnits == 1 &&
                    Mathf.Approximately(
                        captured.QualityTarget, DeliveredQualityCapture.GoodQualityTarget) &&
                    Mathf.Approximately(
                        completed.ActualDeliveredQuality.QualityTarget,
                        captured.QualityTarget) &&
                    completed.ActualDeliveredQuality.QualityEvidenceUnits ==
                        captured.QualityEvidenceUnits &&
                    !Mathf.Approximately(
                        captured.QualityTarget,
                        DeliveredQualityCapture.QualityTargetFor(requested.minQuality.Value)),
                    "completion captures actual delivered quality instead of the requested minimum",
                    $"requested={requested.minQuality.Value}, " +
                    $"delivered={captured.QualityTarget:0.##}");
            }
            finally
            {
                foreach (Thing thing in cleanup)
                {
                    thing?.Destroy(DestroyMode.Vanish);
                }
            }
        }

        private static Thing MakeQualityThing(QualityCategory quality)
        {
            Thing thing = ThingMaker.MakeThing(ThingDefOf.DiningChair, ThingDefOf.WoodLog);
            thing.TryGetComp<CompQuality>()?.SetQuality(
                quality, ArtGenerationContext.Outsider);
            return thing;
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

        private static void RunEffectiveBrandChecks(
            IntercolonyWorldComponent state, Results r)
        {
            ThingDef target = ResolveThingDef("Gun_BoltActionRifle");
            ThingDef revolver = ResolveThingDef("Gun_Revolver");
            ThingDef autopistol = ResolveThingDef("Gun_Autopistol");
            ThingDef assaultRifle = ResolveThingDef("Gun_AssaultRifle");
            ThingDef chair = ResolveThingDef("DiningChair");

            if (target == null || revolver == null || autopistol == null ||
                assaultRifle == null || chair == null)
            {
                StringBuilder missing = new StringBuilder();
                AppendMissing(missing, "Gun_BoltActionRifle", target);
                AppendMissing(missing, "Gun_Revolver", revolver);
                AppendMissing(missing, "Gun_Autopistol", autopistol);
                AppendMissing(missing, "Gun_AssaultRifle", assaultRifle);
                AppendMissing(missing, "DiningChair", chair);
                r.Skip("effective brand checks have their required Core ThingDefs",
                    $"missing loaded ThingDef(s): {missing}");
                return;
            }

            // This method replaces the sparse list with synthetic fixtures. Snapshot the actual
            // objects, not only the count: restoring by length can leave test records persisted if
            // a future assertion adds or removes a different number of entries.
            List<ProductBrandRecord> saved = new List<ProductBrandRecord>(state.ProductBrandRecords);

            try
            {
                state.ProductBrandRecords.Clear();
                state.ProductBrandRecords.Add(new ProductBrandRecord(
                    revolver, directScore: 70f, evidenceWeight: 20f, unitsDelivered: 20));
                state.ProductBrandRecords.Add(new ProductBrandRecord(
                    autopistol, directScore: 65f, evidenceWeight: 20f, unitsDelivered: 20));
                state.ProductBrandRecords.Add(new ProductBrandRecord(
                    assaultRifle, directScore: 60f, evidenceWeight: 20f, unitsDelivered: 20));

                float revolverSignal = 70f * ProductSimilarityService.GetSimilarity(revolver, target);
                float autopistolSignal = 65f * ProductSimilarityService.GetSimilarity(autopistol, target);
                float assaultRifleSignal =
                    60f * ProductSimilarityService.GetSimilarity(assaultRifle, target);
                float strongestSignal = Mathf.Max(
                    Mathf.Abs(revolverSignal),
                    Mathf.Max(Mathf.Abs(autopistolSignal), Mathf.Abs(assaultRifleSignal)));
                float inherited = EffectiveBrandService.GetEffectiveBrand(state, target);
                r.Check(
                    Mathf.Abs(inherited - strongestSignal) < 0.01f &&
                    inherited <= ProductBrandRecord.MaxScore,
                    "inherited brands choose one strongest related signal instead of stacking",
                    $"effective={inherited:0.###}, strongest={strongestSignal:0.###}, " +
                    $"bound={ProductBrandRecord.MaxScore:0.##}");

                state.ProductBrandRecords.Clear();
                state.ProductBrandRecords.Add(new ProductBrandRecord(
                    chair, directScore: 100f, evidenceWeight: 20f, unitsDelivered: 20));
                float floorSimilarity = ProductSimilarityService.GetSimilarity(chair, target);
                float nearFloorInherited = EffectiveBrandService.GetEffectiveBrand(state, target);
                r.Check(
                    floorSimilarity <= ProductSimilarityService.UnrelatedFloor + 0.001f &&
                    Mathf.Abs(nearFloorInherited - 100f * floorSimilarity) < 0.01f &&
                    nearFloorInherited < 10f,
                    "an unrelated product contributes only its similarity-scaled floor",
                    $"similarity={floorSimilarity:0.###}, effective={nearFloorInherited:0.###}");

                state.ProductBrandRecords.Clear();
                state.ProductBrandRecords.Add(new ProductBrandRecord(
                    revolver, directScore: 90f, evidenceWeight: 20f, unitsDelivered: 20));
                float inheritedForDirectTest = EffectiveBrandService.GetEffectiveBrand(state, target);
                ProductBrandRecord direct = new ProductBrandRecord(
                    target, directScore: -70f, evidenceWeight: 1f, unitsDelivered: 1);
                state.ProductBrandRecords.Add(direct);
                float lowEvidence = EffectiveBrandService.GetEffectiveBrand(state, target);
                direct.evidenceWeight = 100f;
                direct.unitsDelivered = 100;
                float highEvidence = EffectiveBrandService.GetEffectiveBrand(state, target);
                r.Check(
                    Mathf.Abs(lowEvidence - inheritedForDirectTest) <
                        Mathf.Abs(lowEvidence - direct.directScore) &&
                    Mathf.Abs(highEvidence - direct.directScore) <
                        Mathf.Abs(highEvidence - inheritedForDirectTest) &&
                    highEvidence < lowEvidence,
                    "direct evidence pulls effective brand from inherited toward the direct score",
                    $"inherited={inheritedForDirectTest:0.###}, low={lowEvidence:0.###}, " +
                    $"high={highEvidence:0.###}, direct={direct.directScore:0.##}");

                state.ProductBrandRecords.Clear();
                state.ProductBrandRecords.Add(new ProductBrandRecord(
                    revolver, directScore: 90f, evidenceWeight: 20f, unitsDelivered: 20));
                state.ProductBrandRecords.Add(new ProductBrandRecord(
                    target, directScore: -90f, evidenceWeight: 20f, unitsDelivered: 20));
                float badProduct = EffectiveBrandService.GetEffectiveBrand(state, target);
                r.Check(
                    badProduct < -50f,
                    "meaningful negative direct evidence cannot be masked by a positive related brand",
                    $"effective={badProduct:0.###}");

                List<ProductBrandRecord> beforeReads =
                    SnapshotBrandRecords(state.ProductBrandRecords);
                for (int i = 0; i < 100; i++)
                {
                    EffectiveBrandService.GetEffectiveBrand(state, target);
                    EffectiveBrandService.GetEffectiveBrand(state, chair);
                }

                r.Check(
                    SameBrandRecordContents(beforeReads, state.ProductBrandRecords),
                    "effective-brand reads leave sparse brand records unchanged",
                    $"records={state.ProductBrandRecords.Count}, reads=200");

                state.ProductBrandRecords.Clear();
                float neutral = EffectiveBrandService.GetEffectiveBrand(state, target);
                r.Check(
                    Mathf.Approximately(neutral, ProductBrandRecord.Neutral),
                    "a product with no brand records is neutral",
                    $"effective={neutral:0.###}");
            }
            finally
            {
                state.ProductBrandRecords.Clear();
                state.ProductBrandRecords.AddRange(saved);
            }
        }

        private static void RunBrandPricingChecks(
            IntercolonyWorldComponent state, Results r)
        {
            ThingDef product = ThingDefOf.DiningChair;
            if (product == null)
            {
                r.Skip("brand pricing checks have their required Core ThingDef",
                    "missing loaded ThingDef: DiningChair");
                return;
            }

            List<ProductBrandRecord> saved = SnapshotBrandRecords(state.ProductBrandRecords);
            const IntercolonyProductCategory category = IntercolonyProductCategory.Furniture;
            SettlementEconomicProfile profile = new SettlementEconomicProfile
            {
                settlementId = -1,
                wealthTier = IntercolonyWealthTier.Comfortable,
                qualityPreference = 0.5f,
                seed = 4_500_001
            };
            profile.demandWeights[(int)category] = 1f;

            try
            {
                state.ProductBrandRecords.Clear();
                float neutralPrice = IntercolonyPricing.UnitPrice(
                    state, product, quantity: 10, profile, category, distanceTiles: 25f,
                    minQuality: null, out List<PriceFactor> neutralFactors);

                state.ProductBrandRecords.Add(new ProductBrandRecord(
                    product, ProductBrandRecord.MaxScore, evidenceWeight: 1000f,
                    unitsDelivered: 1000));
                float positivePrice = IntercolonyPricing.UnitPrice(
                    state, product, quantity: 10, profile, category, distanceTiles: 25f,
                    minQuality: null, out List<PriceFactor> positiveFactors);

                state.ProductBrandRecords.Clear();
                state.ProductBrandRecords.Add(new ProductBrandRecord(
                    product, ProductBrandRecord.MinScore, evidenceWeight: 1000f,
                    unitsDelivered: 1000));
                float negativePrice = IntercolonyPricing.UnitPrice(
                    state, product, quantity: 10, profile, category, distanceTiles: 25f,
                    minQuality: null, out List<PriceFactor> negativeFactors);

                bool positiveRowFound = TryFindPriceFactor(
                    positiveFactors, IntercolonyPricing.BrandFactorLabel,
                    out PriceFactor positiveBrand);
                bool negativeRowFound = TryFindPriceFactor(
                    negativeFactors, IntercolonyPricing.BrandFactorLabel,
                    out PriceFactor negativeBrand);

                r.Check(
                    positivePrice > neutralPrice && negativePrice < neutralPrice,
                    "strong brand changes a newly computed unit price in the expected direction",
                    $"neutral={neutralPrice:0.####}, positive={positivePrice:0.####}, " +
                    $"negative={negativePrice:0.####}");

                float reconstructed = IntercolonyPricing.BaseValue(product, null);
                foreach (PriceFactor factor in positiveFactors)
                {
                    reconstructed *= factor.multiplier;
                }

                r.Check(
                    reconstructed == positivePrice,
                    "base value and active factor rows multiply exactly to the returned unit price",
                    $"reconstructed={reconstructed:R}, returned={positivePrice:R}");

                state.ProductBrandRecords.Clear();
                state.ProductBrandRecords.Add(new ProductBrandRecord(
                    product, ProductBrandRecord.Neutral, evidenceWeight: 1000f,
                    unitsDelivered: 1000));
                IntercolonyPricing.UnitPrice(
                    state, product, quantity: 10, profile, category, distanceTiles: 25f,
                    minQuality: null, out List<PriceFactor> neutralAgainFactors);
                r.Check(
                    CountPriceFactorRows(neutralAgainFactors, IntercolonyPricing.BrandFactorLabel) == 0,
                    "a neutral brand contributes no brand row",
                    $"brandRows={CountPriceFactorRows(
                        neutralAgainFactors, IntercolonyPricing.BrandFactorLabel)}");

                r.Check(
                    positiveRowFound && negativeRowFound &&
                    positiveBrand.multiplier >= IntercolonyPricing.BrandMinimumMultiplier &&
                    positiveBrand.multiplier <= IntercolonyPricing.BrandMaximumMultiplier &&
                    negativeBrand.multiplier >= IntercolonyPricing.BrandMinimumMultiplier &&
                    negativeBrand.multiplier <= IntercolonyPricing.BrandMaximumMultiplier,
                    "the plus and minus 100 brand multipliers stay within their named bounds",
                    $"positive={positiveBrand.multiplier:0.###}, " +
                    $"negative={negativeBrand.multiplier:0.###}, bounds=[" +
                    $"{IntercolonyPricing.BrandMinimumMultiplier:0.###}," +
                    $"{IntercolonyPricing.BrandMaximumMultiplier:0.###}]");

                r.Check(
                    negativePrice > 0f,
                    "a -100 brand leaves the product sellable at a positive price",
                    $"negativePrice={negativePrice:0.####}");
            }
            finally
            {
                // Pricing fixtures replace the sparse list with synthetic direct evidence. Restore
                // its contents, not only its count, so this test cannot overwrite the player's
                // actual brand records when a later assertion changes the fixture shape.
                state.ProductBrandRecords.Clear();
                state.ProductBrandRecords.AddRange(saved);
            }
        }

        private static void RunBrandInterestChecks(
            IntercolonyWorldComponent state, Results r)
        {
            ThingDef product = ThingDefOf.Steel;
            if (product == null || !IntercolonyProductClassifier.Classify(product).HasValue)
            {
                r.Skip("brand interest checks have their required tradable Core ThingDef",
                    "missing or unclassified ThingDef: Steel");
                return;
            }

            // Each run replaces the sparse list with one strong direct record. Snapshot the
            // records themselves, not only the count, because a fixture that changes shape must
            // never leave synthetic brand evidence in the player's world after the assertion.
            List<ProductBrandRecord> savedBrands = SnapshotBrandRecords(state.ProductBrandRecords);
            Dictionary<int, CommercialReputation> savedReputations =
                new Dictionary<int, CommercialReputation>(state.Reputations);
            Dictionary<int, float> savedReputationScores =
                SnapshotReputationScores(state.Reputations);

            try
            {
                state.ProductBrandRecords.Clear();
                List<BuyerOffer> neutralOffers = FindBuyerService.FindBuyers(
                    state, product, null, quantity: 10, includeUninterested: true);
                int sampledSettlements = neutralOffers.Count;
                if (sampledSettlements < MinimumBrandInterestSampleSettlements)
                {
                    r.Skip(
                        "brand interest checks have enough accessible Find Buyer settlements",
                        $"FindBuyers returned {sampledSettlements}; need " +
                        $"{MinimumBrandInterestSampleSettlements} to compare an aggregate sample");
                    return;
                }

                int neutralInterested = CountInterestedOffers(neutralOffers);
                IntercolonyProductCategory category =
                    IntercolonyProductClassifier.Classify(product).Value;
                int renownedCrossingHeadroom = CountBrandInterestCrossings(
                    state, neutralOffers, product, category, upward: true);
                int notoriousCrossingHeadroom = CountBrandInterestCrossings(
                    state, neutralOffers, product, category, upward: false);

                state.ProductBrandRecords.Add(new ProductBrandRecord(
                    product, ProductBrandRecord.MaxScore, evidenceWeight: 1000f,
                    unitsDelivered: 1000));
                List<BuyerOffer> renownedOffers = FindBuyerService.FindBuyers(
                    state, product, null, quantity: 10, includeUninterested: true);
                int renownedInterested = CountInterestedOffers(renownedOffers);

                state.ProductBrandRecords.Clear();
                state.ProductBrandRecords.Add(new ProductBrandRecord(
                    product, ProductBrandRecord.MinScore, evidenceWeight: 1000f,
                    unitsDelivered: 1000));
                List<BuyerOffer> notoriousOffers = FindBuyerService.FindBuyers(
                    state, product, null, quantity: 10, includeUninterested: true);
                int notoriousInterested = CountInterestedOffers(notoriousOffers);

                // The comparison must use the production entry point above. Calling a brand
                // helper directly would prove only that the helper works, not that Find Buyer
                // actually applies its result at the interest threshold.
                if (renownedCrossingHeadroom == 0)
                {
                    r.Skip(
                        "a +100 brand increases interested settlements through Find Buyer",
                        $"sampled={sampledSettlements}, threshold={FindBuyerService.InterestThreshold:0.###}, " +
                        $"shiftDistance={ExpectedBrandInterestShiftDistance:0.###}; " +
                        "upward crossing headroom=0");
                }
                else
                {
                    r.Check(
                        renownedOffers.Count == sampledSettlements &&
                        renownedInterested > neutralInterested,
                        "a +100 brand increases interested settlements through Find Buyer",
                        $"neutral={neutralInterested}, renowned={renownedInterested}, " +
                        $"sampled={sampledSettlements}");
                }

                if (notoriousCrossingHeadroom == 0)
                {
                    r.Skip(
                        "a -100 brand decreases interested settlements through Find Buyer",
                        $"sampled={sampledSettlements}, threshold={FindBuyerService.InterestThreshold:0.###}, " +
                        $"shiftDistance={ExpectedBrandInterestShiftDistance:0.###}; " +
                        "downward crossing headroom=0");
                }
                else
                {
                    r.Check(
                        notoriousOffers.Count == sampledSettlements &&
                        notoriousInterested < neutralInterested,
                        "a -100 brand decreases interested settlements through Find Buyer",
                        $"neutral={neutralInterested}, notorious={notoriousInterested}, " +
                        $"sampled={sampledSettlements}");
                }

                r.Check(
                    notoriousOffers.Count == sampledSettlements && notoriousInterested > 0,
                    "a -100 brand still leaves some settlements interested",
                    $"notorious={notoriousInterested} of {sampledSettlements}");

                r.Check(
                    renownedOffers.Count == sampledSettlements &&
                    renownedInterested < renownedOffers.Count,
                    "a +100 brand does not make every settlement interested",
                    $"renowned={renownedInterested} of {renownedOffers.Count}");

                r.Check(
                    ReputationScoresUnchanged(savedReputationScores, state.Reputations),
                    "brand-adjusted Find Buyer interest leaves CommercialReputation scores unchanged",
                    $"tracked={savedReputationScores.Count}, current={state.Reputations.Count}");
            }
            finally
            {
                // Restore the complete sparse brand-list contents, not only its old length, so
                // this threshold experiment cannot overwrite a player's real product history.
                state.ProductBrandRecords.Clear();
                state.ProductBrandRecords.AddRange(savedBrands);

                // Find Buyer is intended to be read-only with respect to reputation. Restore the
                // dictionary contents as a defensive cleanup if a future interest hook creates a
                // record while this self-test is running.
                state.Reputations.Clear();
                foreach (KeyValuePair<int, CommercialReputation> entry in savedReputations)
                {
                    state.Reputations.Add(entry.Key, entry.Value);
                }
            }
        }

        private static bool TryFindPriceFactor(
            List<PriceFactor> factors, string label, out PriceFactor result)
        {
            if (factors != null)
            {
                for (int i = 0; i < factors.Count; i++)
                {
                    if (factors[i].label == label)
                    {
                        result = factors[i];
                        return true;
                    }
                }
            }

            result = default(PriceFactor);
            return false;
        }

        private static int CountPriceFactorRows(List<PriceFactor> factors, string label)
        {
            int count = 0;
            if (factors == null)
            {
                return count;
            }

            for (int i = 0; i < factors.Count; i++)
            {
                if (factors[i].label == label)
                {
                    count++;
                }
            }

            return count;
        }

        private static void AppendMissing(StringBuilder missing, string name, ThingDef def)
        {
            if (def == null)
            {
                if (missing.Length > 0)
                {
                    missing.Append(", ");
                }

                missing.Append(name);
            }
        }

        private static bool SameBrandRecordContents(
            List<ProductBrandRecord> expected, List<ProductBrandRecord> actual)
        {
            if (expected == null || actual == null || expected.Count != actual.Count)
            {
                return false;
            }

            for (int i = 0; i < expected.Count; i++)
            {
                ProductBrandRecord expectedRecord = expected[i];
                ProductBrandRecord actualRecord = actual[i];
                if (expectedRecord == null || actualRecord == null)
                {
                    if (expectedRecord != actualRecord)
                    {
                        return false;
                    }

                    continue;
                }

                if (!ReferenceEquals(expectedRecord.thingDef, actualRecord.thingDef) ||
                    expectedRecord.directScore != actualRecord.directScore ||
                    expectedRecord.evidenceWeight != actualRecord.evidenceWeight ||
                    expectedRecord.unitsDelivered != actualRecord.unitsDelivered)
                {
                    return false;
                }
            }

            return true;
        }

        private static int CountBrandInterestCrossings(
            IntercolonyWorldComponent state,
            List<BuyerOffer> offers,
            ThingDef product,
            IntercolonyProductCategory category,
            bool upward)
        {
            int crossings = 0;
            float threshold = FindBuyerService.InterestThreshold;
            foreach (BuyerOffer offer in offers)
            {
                if (offer == null || offer.profile == null)
                {
                    continue;
                }

                // Read the same unshifted interest input that Find Buyer reads. The directional
                // bands below only identify a possible threshold crossing; they do not recreate
                // the production gate or infer interest from BuyerOffer.Interested.
                float demand = EffectiveEconomyService.EffectiveDemand(
                    state, offer.profile, product, category);
                bool canCross = upward
                    ? demand >= threshold - ExpectedBrandInterestShiftDistance && demand < threshold
                    : demand >= threshold &&
                      demand < threshold + ExpectedBrandInterestShiftDistance;
                if (canCross)
                {
                    crossings++;
                }
            }

            return crossings;
        }

        private static int CountInterestedOffers(List<BuyerOffer> offers)
        {
            int interested = 0;
            if (offers == null)
            {
                return interested;
            }

            foreach (BuyerOffer offer in offers)
            {
                if (offer != null && offer.Interested)
                {
                    interested++;
                }
            }

            return interested;
        }

        private static Dictionary<int, float> SnapshotReputationScores(
            Dictionary<int, CommercialReputation> reputations)
        {
            Dictionary<int, float> snapshot = new Dictionary<int, float>();
            if (reputations == null)
            {
                return snapshot;
            }

            foreach (KeyValuePair<int, CommercialReputation> entry in reputations)
            {
                if (entry.Value != null)
                {
                    snapshot[entry.Key] = entry.Value.Score;
                }
            }

            return snapshot;
        }

        private static bool ReputationScoresUnchanged(
            Dictionary<int, float> expected,
            Dictionary<int, CommercialReputation> actual)
        {
            if (expected == null || actual == null || expected.Count != actual.Count)
            {
                return false;
            }

            foreach (KeyValuePair<int, float> entry in expected)
            {
                if (!actual.TryGetValue(entry.Key, out CommercialReputation reputation) ||
                    reputation == null ||
                    !Mathf.Approximately(reputation.Score, entry.Value))
                {
                    return false;
                }
            }

            return true;
        }

        private static List<ProductBrandRecord> SnapshotBrandRecords(
            List<ProductBrandRecord> records)
        {
            List<ProductBrandRecord> snapshot = new List<ProductBrandRecord>(records.Count);
            for (int i = 0; i < records.Count; i++)
            {
                ProductBrandRecord record = records[i];
                snapshot.Add(record == null
                    ? null
                    : new ProductBrandRecord(
                        record.thingDef,
                        record.directScore,
                        record.evidenceWeight,
                        record.unitsDelivered));
            }

            return snapshot;
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
