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
    /// evidence confidence, the bounded prospective price factor, bounded Find Buyer interest,
    /// and Stage 4F Parts One and Two: commercial brand milestones and the compact brand UI,
    /// plus the Stage 4 acceptance gate's known-inventory valuation and binding-payment checks.
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
            r.sb.AppendLine("Product brand record self-test (Stage 4A/4B/4C/4D/4E/4F Part One)");

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
                RunBrandMilestoneTimelineChecks(state, r);
                RunProductSimilarityChecks(r);
                RunEffectiveBrandChecks(state, r);
                RunBrandUiChecks(state, r);
                RunBrandPricingChecks(state, r);
                RunBrandInterestChecks(state, r);
                RunKnownInventoryPricingChecks(state, r);
                RunBindingQualityPaymentChecks(state, r);
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
                SnapshotCommercialTimeline(state.CommercialTimeline);
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

        private static void RunBrandMilestoneTimelineChecks(
            IntercolonyWorldComponent state, Results r)
        {
            ThingDef product = ThingDefOf.DiningChair;
            if (product == null)
            {
                r.Skip(
                    "brand milestone timeline checks have their required product ThingDef",
                    "missing loaded ThingDef: DiningChair");
                return;
            }

            // These checks deliberately drive SalesOrderService.Complete. Snapshot the complete
            // contents, not only the counts: every real completion also writes ordinary sale
            // history, and a fixture must never leave synthetic brand or timeline records in the
            // player's world when a later assertion changes the number of entries.
            List<ProductBrandRecord> savedBrands = SnapshotBrandRecords(state.ProductBrandRecords);
            List<CommercialHistoryEntry> savedCommercialHistory =
                SnapshotCommercialHistory(state.CommercialHistory);
            List<CommercialEventRecord> savedCommercialTimeline =
                SnapshotCommercialTimeline(state.CommercialTimeline);
            int savedTimelineStartTick = state.CommercialTimelineStartTick;

            try
            {
                ResetBrandMilestoneFixture(
                    state, product,
                    ProductBrandService.EstablishedThreshold -
                    ProductBrandService.BrandMilestoneHysteresis - 0.5f);
                CompleteBrandMilestoneTestSale(
                    state, product, 9101,
                    DeliveredQualityCapture.MasterworkQualityTarget, 1);
                List<CommercialEventRecord> upwardEvents =
                    FindBrandMilestoneEvents(state.CommercialTimeline);
                bool upwardNamesBand = upwardEvents.Count == 1 &&
                    upwardEvents[0].thingDef == product &&
                    upwardEvents[0].compactDetail.Contains(
                        ProductBrandService.EstablishedBandLabel) &&
                    upwardEvents[0].compactDetail.Contains("Reached");
                r.Check(
                    upwardEvents.Count == 1 && upwardNamesBand,
                    "crossing upward writes exactly one Established brand milestone event",
                    $"events={upwardEvents.Count}, detail={(upwardEvents.Count == 1
                        ? upwardEvents[0].compactDetail : "<none>")}");

                ResetBrandMilestoneFixture(
                    state, product, ProductBrandService.EstablishedThreshold + 2f);
                for (int i = 0; i < 6; i++)
                {
                    CompleteBrandMilestoneTestSale(
                        state, product, 9200 + i,
                        ProductBrandService.EstablishedThreshold + 5f, 1);
                }

                ProductBrandRecord withinBand = FindBrandRecord(
                    state.ProductBrandRecords, product);
                List<CommercialEventRecord> withinBandEvents =
                    FindBrandMilestoneEvents(state.CommercialTimeline);
                r.Check(
                    withinBandEvents.Count == 0 &&
                    withinBand != null &&
                    withinBand.evidenceWeight == 6f &&
                    withinBand.directScore > ProductBrandService.EstablishedThreshold &&
                    withinBand.directScore < ProductBrandService.RespectedThreshold,
                    "small movements that stay inside a brand band write no milestone event",
                    $"events={withinBandEvents.Count}, score={withinBand?.directScore ?? float.NaN:0.###}, " +
                    $"evidence={withinBand?.evidenceWeight ?? float.NaN:0.###}");

                ResetBrandMilestoneFixture(
                    state, product,
                    ProductBrandService.EstablishedThreshold +
                    ProductBrandService.BrandMilestoneHysteresis + 0.5f);
                CompleteBrandMilestoneTestSale(
                    state, product, 9301,
                    DeliveredQualityCapture.AwfulQualityTarget, 1);
                List<CommercialEventRecord> downwardEvents =
                    FindBrandMilestoneEvents(state.CommercialTimeline);
                bool downwardNamesBand = downwardEvents.Count == 1 &&
                    downwardEvents[0].thingDef == product &&
                    downwardEvents[0].compactDetail.Contains(
                        ProductBrandService.EstablishedBandLabel) &&
                    downwardEvents[0].compactDetail.Contains("Lost");
                r.Check(
                    downwardEvents.Count == 1 && downwardNamesBand,
                    "crossing downward writes exactly one symmetric Established milestone event",
                    $"events={downwardEvents.Count}, detail={(downwardEvents.Count == 1
                        ? downwardEvents[0].compactDetail : "<none>")}");

                ResetBrandMilestoneFixture(
                    state, product, ProductBrandService.EstablishedThreshold - 0.1f);
                const int oscillationSales = 6;
                for (int i = 0; i < oscillationSales; i++)
                {
                    float target = i % 2 == 0
                        ? ProductBrandService.EstablishedThreshold + 4f
                        : ProductBrandService.EstablishedThreshold - 4f;
                    CompleteBrandMilestoneTestSale(
                        state, product, 9400 + i, target, 1);
                }

                ProductBrandRecord oscillating = FindBrandRecord(
                    state.ProductBrandRecords, product);
                List<CommercialEventRecord> oscillationEvents =
                    FindBrandMilestoneEvents(state.CommercialTimeline);
                r.Check(
                    oscillationEvents.Count == 0 &&
                    oscillating != null &&
                    oscillating.directScore > ProductBrandService.EstablishedThreshold -
                        ProductBrandService.BrandMilestoneHysteresis &&
                    oscillating.directScore < ProductBrandService.EstablishedThreshold +
                        ProductBrandService.BrandMilestoneHysteresis,
                    "fractional oscillation around a threshold does not emit one event per sale",
                    $"sales={oscillationSales}, events={oscillationEvents.Count}, " +
                    $"finalScore={oscillating?.directScore ?? float.NaN:0.###}, " +
                    $"deadband={ProductBrandService.BrandMilestoneHysteresis:0.###}");

                ResetBrandMilestoneFixture(
                    state, product,
                    ProductBrandService.EstablishedThreshold -
                    ProductBrandService.BrandMilestoneHysteresis - 0.5f);
                ProductBrandRecord constructed = FindBrandRecord(
                    state.ProductBrandRecords, product);
                bool noEventFromConstruction =
                    constructed != null && FindBrandMilestoneEvents(state.CommercialTimeline).Count == 0;

                ProductBrandService.ApplyDeliveredQuality(
                    state, product,
                    new DeliveredQualityResult(
                        DeliveredQualityCapture.MasterworkQualityTarget, 1));
                bool noEventFromDirectScoreFixture =
                    FindBrandMilestoneEvents(state.CommercialTimeline).Count == 0;

                ResetBrandMilestoneFixture(
                    state, product,
                    ProductBrandService.EstablishedThreshold -
                    ProductBrandService.BrandMilestoneHysteresis - 0.5f);
                SalesOrder completed = CompleteBrandMilestoneTestSale(
                    state, product, 9501,
                    DeliveredQualityCapture.MasterworkQualityTarget, 1);
                List<CommercialEventRecord> completionEvents =
                    FindBrandMilestoneEvents(state.CommercialTimeline);
                r.Check(
                    noEventFromConstruction &&
                    noEventFromDirectScoreFixture &&
                    completed.status == SalesOrderStatus.Completed &&
                    completionEvents.Count == 1,
                    "brand milestone history is written only by real sale completion",
                    $"construction={noEventFromConstruction}, directUpdate={noEventFromDirectScoreFixture}, " +
                    $"status={completed.status}, events={completionEvents.Count}");
            }
            finally
            {
                // Restore the complete contents of both mutable histories. Restoring by count can
                // leave synthetic records in the player's save when a test's fixture shape differs
                // from the original list, which is exactly the defect this cleanup guards.
                state.ProductBrandRecords.Clear();
                state.ProductBrandRecords.AddRange(savedBrands);
                state.CommercialHistory.Clear();
                state.CommercialHistory.AddRange(savedCommercialHistory);
                state.CommercialTimeline.Clear();
                state.CommercialTimeline.AddRange(savedCommercialTimeline);
                state.CommercialTimelineStartTick = savedTimelineStartTick;
            }
        }

        private static void ResetBrandMilestoneFixture(
            IntercolonyWorldComponent state, ThingDef product, float directScore)
        {
            state.ProductBrandRecords.Clear();
            state.ProductBrandRecords.Add(new ProductBrandRecord(
                product, directScore, evidenceWeight: 0f, unitsDelivered: 0));
            state.CommercialTimeline.Clear();
            state.CommercialTimelineStartTick = CommercialTimelineService.NoHistory;
        }

        private static SalesOrder CompleteBrandMilestoneTestSale(
            IntercolonyWorldComponent state,
            ThingDef product,
            int orderId,
            float qualityTarget,
            int qualityUnits)
        {
            SalesOrder order = new SalesOrder
            {
                id = orderId,
                settlementId = -1,
                settlementName = "",
                line = new OrderLine(product, qualityUnits),
                status = SalesOrderStatus.Accepted,
                deadlineTick = int.MaxValue,
                deliveredQuantity = qualityUnits,
                paidSilver = 0
            };

            SalesOrderService.Complete(
                state,
                order,
                completedTick: 0,
                outcomeNote: "brand milestone self-test",
                actualDeliveredQuality: new DeliveredQualityResult(
                    qualityTarget, qualityUnits));
            return order;
        }

        private static List<CommercialEventRecord> FindBrandMilestoneEvents(
            List<CommercialEventRecord> timeline)
        {
            List<CommercialEventRecord> events = new List<CommercialEventRecord>();
            if (timeline == null)
            {
                return events;
            }

            for (int i = 0; i < timeline.Count; i++)
            {
                CommercialEventRecord record = timeline[i];
                if (record != null && record.type == CommercialEventType.BrandMilestone)
                {
                    events.Add(record);
                }
            }

            return events;
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

        private static List<CommercialEventRecord> SnapshotCommercialTimeline(
            List<CommercialEventRecord> records)
        {
            List<CommercialEventRecord> snapshot =
                new List<CommercialEventRecord>(records.Count);
            for (int i = 0; i < records.Count; i++)
            {
                CommercialEventRecord record = records[i];
                snapshot.Add(record == null
                    ? null
                    : new CommercialEventRecord(
                        record.id,
                        record.tick,
                        record.settlementId,
                        record.type,
                        record.settlementName,
                        record.relatedEntityId,
                        record.thingDef,
                        record.quantity,
                        record.silverAmount,
                        record.compactDetail));
            }

            return snapshot;
        }

        private static List<SettlementMarketState> SnapshotMarketStates(
            List<SettlementMarketState> states)
        {
            List<SettlementMarketState> snapshot =
                new List<SettlementMarketState>(states.Count);
            for (int i = 0; i < states.Count; i++)
            {
                SettlementMarketState state = states[i];
                if (state == null)
                {
                    snapshot.Add(null);
                    continue;
                }

                snapshot.Add(new SettlementMarketState
                {
                    settlementId = state.settlementId,
                    demandPressure = state.demandPressure == null
                        ? null
                        : (float[])state.demandPressure.Clone(),
                    supplyPressure = state.supplyPressure == null
                        ? null
                        : (float[])state.supplyPressure.Clone(),
                    lastAdvancedRefresh = state.lastAdvancedRefresh
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

        /// <summary>
        /// Verifies the production read model that feeds both the Business summary and the
        /// selected-good rows. These assertions inspect returned UI data; they do not reconstruct
        /// the grouping or effective-brand calculation in the test itself.
        /// </summary>
        private static void RunBrandUiChecks(
            IntercolonyWorldComponent state, Results r)
        {
            ThingDef positiveProduct = ResolveThingDef("Gun_Revolver");
            ThingDef negativeProduct = ResolveThingDef("DiningChair");
            ThingDef targetProduct = ResolveThingDef("Gun_BoltActionRifle");

            if (positiveProduct == null || negativeProduct == null || targetProduct == null)
            {
                StringBuilder missing = new StringBuilder();
                AppendMissing(missing, "Gun_Revolver", positiveProduct);
                AppendMissing(missing, "DiningChair", negativeProduct);
                AppendMissing(missing, "Gun_BoltActionRifle", targetProduct);
                r.Skip(
                    "brand UI checks have their required Core ThingDefs",
                    $"missing loaded ThingDef(s): {missing}");
                return;
            }

            List<ProductBrandRecord> saved = SnapshotBrandRecords(state.ProductBrandRecords);
            try
            {
                state.ProductBrandRecords.Clear();
                state.ProductBrandRecords.Add(new ProductBrandRecord(
                    positiveProduct,
                    // Fixed sentinels keep this assertion meaningful if production thresholds
                    // move: the UI must still recognise the documented +50 boundary as Respected.
                    directScore: 50f,
                    evidenceWeight: 20f,
                    unitsDelivered: 20));
                state.ProductBrandRecords.Add(new ProductBrandRecord(
                    negativeProduct,
                    directScore: -25f,
                    evidenceWeight: 20f,
                    unitsDelivered: 20));

                ProductBrandUiService.BrandSummary summary =
                    ProductBrandUiService.BuildSummary(state);
                r.Check(
                    HasSummaryRow(
                        summary.knownFor,
                        IntercolonyProductCategory.ManufacturedGoods,
                        "Respected") &&
                    HasSummaryRow(
                        summary.weakReputation,
                        IntercolonyProductCategory.Furniture,
                        "Questionable"),
                    "brand UI groups exact milestone boundaries with their positive and weak bands",
                    $"known={summary.knownFor.Count}, weak={summary.weakReputation.Count}");

                ThingDef inheritedSource = positiveProduct;
                state.ProductBrandRecords.Clear();
                state.ProductBrandRecords.Add(new ProductBrandRecord(
                    inheritedSource, directScore: 80f, evidenceWeight: 1f, unitsDelivered: 1));
                ProductBrandUiService.SpecificGoodDetails inheritedDetails =
                    ProductBrandUiService.BuildSpecificGoodDetails(state, targetProduct);
                float expectedEffective =
                    EffectiveBrandService.GetEffectiveBrand(state, targetProduct);
                int expectedRounded = Mathf.RoundToInt(expectedEffective);
                string expectedLabel = expectedRounded > 0
                    ? $"+{expectedRounded}"
                    : expectedRounded.ToString();
                r.Check(
                    Mathf.Abs(inheritedDetails.effectiveBrand - expectedEffective) < 0.001f &&
                    inheritedDetails.strengthLabel == expectedLabel,
                    "specific-good brand UI reports EffectiveBrandService's value",
                    $"display={inheritedDetails.strengthLabel}, effective={expectedEffective:0.###}");

                state.ProductBrandRecords.Add(new ProductBrandRecord(
                    targetProduct, directScore: -70f, evidenceWeight: 100f, unitsDelivered: 100));
                ProductBrandUiService.SpecificGoodDetails directDetails =
                    ProductBrandUiService.BuildSpecificGoodDetails(state, targetProduct);
                r.Check(
                    inheritedDetails.attribution.IndexOf(
                        "inherited", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    inheritedDetails.attribution.IndexOf(
                        inheritedSource.LabelCap.ToString(), StringComparison.OrdinalIgnoreCase) >= 0 &&
                    directDetails.attribution.IndexOf(
                        "inherited", StringComparison.OrdinalIgnoreCase) < 0 &&
                    directDetails.attribution.IndexOf(
                        "direct", StringComparison.OrdinalIgnoreCase) >= 0,
                    "brand UI attribution changes from inherited to direct evidence",
                    $"inherited='{inheritedDetails.attribution}', direct='{directDetails.attribution}'");

                state.ProductBrandRecords.Clear();
                ProductBrandUiService.BrandSummary empty =
                    ProductBrandUiService.BuildSummary(state);
                r.Check(
                    empty.IsEmpty &&
                    empty.knownFor.Count == 0 &&
                    empty.weakReputation.Count == 0 &&
                    empty.emptyState == ProductBrandUiService.NoBrandEvidenceMessage,
                    "brand UI returns the plain empty state when no brand records exist",
                    empty.emptyState);
            }
            finally
            {
                state.ProductBrandRecords.Clear();
                state.ProductBrandRecords.AddRange(saved);
            }
        }

        private static bool HasSummaryRow(
            List<ProductBrandUiService.BrandSummaryRow> rows,
            IntercolonyProductCategory category,
            string bandName)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                ProductBrandUiService.BrandSummaryRow row = rows[i];
                if (row.category == category && row.bandName == bandName)
                {
                    return true;
                }
            }

            return false;
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

        private static void RunKnownInventoryPricingChecks(
            IntercolonyWorldComponent state, Results r)
        {
            ThingDef product = ThingDefOf.DiningChair;
            if (product == null)
            {
                r.Skip(
                    "known-inventory direct-sale pricing has the Core DiningChair ThingDef",
                    "DiningChair is not loaded");
                return;
            }

            if (!IntercolonyPricing.CanHaveQuality(product))
            {
                r.Skip(
                    "known-inventory direct-sale pricing has a quality-capable Core ThingDef",
                    "DiningChair is loaded but has no CompQuality");
                return;
            }

            ThingDef stuff = ThingDefOf.WoodLog;
            if (stuff == null || !product.MadeFromStuff)
            {
                r.Skip(
                    "known-inventory direct-sale pricing has the same Core stuff on both Things",
                    stuff == null
                        ? "WoodLog is not loaded"
                        : "DiningChair does not accept stuff");
                return;
            }

            List<ProductBrandRecord> savedBrands = SnapshotBrandRecords(state.ProductBrandRecords);
            List<Thing> temporaryThings = new List<Thing>();

            try
            {
                // Keep prospective brand neutral so the comparison isolates the actual live
                // Thing value. A regression here would let brand evidence obscure the quality
                // signal instead of failing the direct-sale assertion cleanly.
                state.ProductBrandRecords.Clear();

                Thing masterwork = ThingMaker.MakeThing(product, stuff);
                Thing awful = ThingMaker.MakeThing(product, stuff);
                temporaryThings.Add(masterwork);
                temporaryThings.Add(awful);

                CompQuality masterworkComp = masterwork?.TryGetComp<CompQuality>();
                CompQuality awfulComp = awful?.TryGetComp<CompQuality>();
                if (masterworkComp == null || awfulComp == null)
                {
                    r.Skip(
                        "known-inventory direct-sale pricing has two quality-bearing DiningChairs",
                        "DiningChair did not instantiate CompQuality on both Things");
                    return;
                }

                masterworkComp.SetQuality(
                    QualityCategory.Masterwork, ArtGenerationContext.Outsider);
                awfulComp.SetQuality(QualityCategory.Awful, ArtGenerationContext.Outsider);
                QualityCategory masterworkQuality = default(QualityCategory);
                QualityCategory awfulQuality = default(QualityCategory);
                bool qualitiesApplied =
                    masterwork.TryGetQuality(out masterworkQuality) &&
                    awful.TryGetQuality(out awfulQuality);
                if (!qualitiesApplied ||
                    masterworkQuality != QualityCategory.Masterwork ||
                    awfulQuality != QualityCategory.Awful)
                {
                    r.Skip(
                        "known-inventory direct-sale pricing can distinguish DiningChair quality",
                        $"applied qualities were {masterworkQuality}/{awfulQuality}");
                    return;
                }

                SettlementEconomicProfile profile = new SettlementEconomicProfile
                {
                    seed = 0,
                    wealthTier = IntercolonyWealthTier.Modest,
                    qualityPreference = 0.5f
                };
                foreach (IntercolonyProductCategory categoryValue in
                         IntercolonyProductCategoryUtility.All)
                {
                    profile.demandWeights[(int)categoryValue] = 1f;
                }

                IntercolonyProductCategory category =
                    IntercolonyProductClassifier.Classify(product)
                    ?? IntercolonyProductCategory.Commodities;
                // This is the same internal known-Thing entry point used by Find Buyer direct-sale
                // repricing. It keeps buyer interest out of this valuation-only criterion.
                float masterworkPrice = IntercolonyPricing.UnitPrice(
                    state, product, stuff, masterwork, 1, profile, category, -1f, null, out _);
                float awfulPrice = IntercolonyPricing.UnitPrice(
                    state, product, stuff, awful, 1, profile, category, -1f, null, out _);

                // This fails if known-inventory pricing falls back to ThingDef/BaseMarketValue
                // and ignores the live CompQuality carried by the actual Thing.
                r.Check(
                    masterworkPrice > awfulPrice,
                    "a direct sale values known Masterwork inventory above equivalent Awful inventory",
                    $"product={product.defName}, masterwork={masterworkPrice:0.####}, " +
                    $"awful={awfulPrice:0.####}, " +
                    $"liveValues={masterwork.MarketValue:0.####}/{awful.MarketValue:0.####}, " +
                    $"stuff={stuff.defName}");
            }
            finally
            {
                foreach (Thing thing in temporaryThings)
                {
                    if (thing != null && !thing.Destroyed)
                    {
                        thing.Destroy(DestroyMode.Vanish);
                    }
                }

                state.ProductBrandRecords.Clear();
                state.ProductBrandRecords.AddRange(savedBrands);
            }
        }

        private static void RunBindingQualityPaymentChecks(
            IntercolonyWorldComponent state, Results r)
        {
            ThingDef product = ThingDefOf.DiningChair;
            if (product == null || !IntercolonyPricing.CanHaveQuality(product))
            {
                r.Skip(
                    "binding quality-payment checks have their quality-capable product ThingDef",
                    "missing or non-quality ThingDef: DiningChair");
                return;
            }

            // Complete() also records sale history, timeline events and (for a real settlement)
            // economic/reputation effects. Use the invalid settlement sentinel so this focused
            // fixture exercises only its intended product-brand side effect, while still
            // snapshotting every mutable world collection that the completion boundary owns.
            List<ProductBrandRecord> savedBrands = SnapshotBrandRecords(state.ProductBrandRecords);
            List<CommercialHistoryEntry> savedCommercialHistory =
                SnapshotCommercialHistory(state.CommercialHistory);
            List<CommercialEventRecord> savedCommercialTimeline =
                SnapshotCommercialTimeline(state.CommercialTimeline);
            int savedTimelineStartTick = state.CommercialTimelineStartTick;
            List<SettlementMarketState> savedMarketStates =
                SnapshotMarketStates(state.MarketStates);
            Dictionary<int, CommercialReputation> savedReputations =
                new Dictionary<int, CommercialReputation>(state.Reputations);

            try
            {
                state.ProductBrandRecords.Clear();
                state.ProductBrandRecords.Add(new ProductBrandRecord(
                    product, directScore: 0f, evidenceWeight: 100f, unitsDelivered: 100));
                SalesOrder above = MakeQualityPaymentOrder(
                    product, -4_801, QualityCategory.Awful, unitPrice: 37.25f,
                    discountFraction: 0.15f);
                float aboveUnitPrice = above.unitPrice;
                float aboveReferencePrice = above.referenceUnitPrice;
                float aboveDiscount = above.DiscountFraction;
                int aboveTotalPayment = above.TotalPayment;
                int aboveDiscountedPayment = above.DiscountedTotalPayment;
                float aboveBrandAtAcceptance =
                    FindBrandRecord(state.ProductBrandRecords, product).directScore;

                // Move brand after acceptance, before the target order completes. The target's
                // requested Awful quality is deliberately far below the delivered Masterwork.
                MakeAndCompleteQualityPaymentMover(
                    state, product, -4_802, DeliveredQualityCapture.AwfulQualityTarget);
                float aboveBrandBeforeCompletion =
                    FindBrandRecord(state.ProductBrandRecords, product).directScore;
                SalesOrderService.Complete(
                    state, above, completedTick: 0,
                    outcomeNote: "binding quality-payment self-test: above",
                    actualDeliveredQuality: new DeliveredQualityResult(
                        DeliveredQualityCapture.MasterworkQualityTarget, 1));
                float aboveBrandAfterCompletion =
                    FindBrandRecord(state.ProductBrandRecords, product)?.directScore ?? float.NaN;

                // This fails if the completion boundary recomputes payment from delivered
                // quality or from the brand that changed after acceptance.
                r.Check(
                    above.status == SalesOrderStatus.Completed &&
                    Mathf.Approximately(above.unitPrice, aboveUnitPrice) &&
                    Mathf.Approximately(above.referenceUnitPrice, aboveReferencePrice) &&
                    Mathf.Approximately(above.DiscountFraction, aboveDiscount) &&
                    above.TotalPayment == aboveTotalPayment &&
                    above.DiscountedTotalPayment == aboveDiscountedPayment,
                    "a binding order keeps its stored payment when delivered quality is far above requested",
                    $"unit={above.unitPrice:0.####}/{aboveUnitPrice:0.####}, " +
                    $"payment={above.DiscountedTotalPayment}/{aboveDiscountedPayment}");

                bool aboveBrandMoved =
                    !Mathf.Approximately(aboveBrandBeforeCompletion, aboveBrandAtAcceptance) &&
                    aboveBrandAfterCompletion > aboveBrandBeforeCompletion;
                // This fails if payment is frozen by also dropping the delivered-quality brand
                // update, which would make the payment check vacuous.
                r.Check(
                    aboveBrandMoved,
                    "the same above-requested completion moves future brand after brand already moved",
                    $"brand={aboveBrandAtAcceptance:0.###}->" +
                    $"{aboveBrandBeforeCompletion:0.###}->" +
                    $"{aboveBrandAfterCompletion:0.###}");

                state.ProductBrandRecords.Clear();
                state.ProductBrandRecords.Add(new ProductBrandRecord(
                    product, directScore: 0f, evidenceWeight: 100f, unitsDelivered: 100));
                SalesOrder below = MakeQualityPaymentOrder(
                    product, -4_803, QualityCategory.Masterwork, unitPrice: 61.75f,
                    discountFraction: 0.20f);
                float belowUnitPrice = below.unitPrice;
                float belowReferencePrice = below.referenceUnitPrice;
                float belowDiscount = below.DiscountFraction;
                int belowTotalPayment = below.TotalPayment;
                int belowDiscountedPayment = below.DiscountedTotalPayment;
                float belowBrandAtAcceptance =
                    FindBrandRecord(state.ProductBrandRecords, product).directScore;

                // The accepted Masterwork order now receives an Awful item, the opposite
                // delivered-quality extreme. Its payment must remain just as fixed.
                MakeAndCompleteQualityPaymentMover(
                    state, product, -4_804, DeliveredQualityCapture.MasterworkQualityTarget);
                float belowBrandBeforeCompletion =
                    FindBrandRecord(state.ProductBrandRecords, product).directScore;
                SalesOrderService.Complete(
                    state, below, completedTick: 1,
                    outcomeNote: "binding quality-payment self-test: below",
                    actualDeliveredQuality: new DeliveredQualityResult(
                        DeliveredQualityCapture.AwfulQualityTarget, 1));
                float belowBrandAfterCompletion =
                    FindBrandRecord(state.ProductBrandRecords, product)?.directScore ?? float.NaN;

                // This fails if the opposite quality extreme can rewrite the accepted amount,
                // including the stored reference price or discount terms.
                r.Check(
                    below.status == SalesOrderStatus.Completed &&
                    Mathf.Approximately(below.unitPrice, belowUnitPrice) &&
                    Mathf.Approximately(below.referenceUnitPrice, belowReferencePrice) &&
                    Mathf.Approximately(below.DiscountFraction, belowDiscount) &&
                    below.TotalPayment == belowTotalPayment &&
                    below.DiscountedTotalPayment == belowDiscountedPayment,
                    "a binding order keeps its stored payment when delivered quality is far below requested",
                    $"unit={below.unitPrice:0.####}/{belowUnitPrice:0.####}, " +
                    $"payment={below.DiscountedTotalPayment}/{belowDiscountedPayment}");

                bool belowBrandMoved =
                    !Mathf.Approximately(belowBrandBeforeCompletion, belowBrandAtAcceptance) &&
                    belowBrandAfterCompletion < belowBrandBeforeCompletion;
                // This fails if poor delivered quality no longer changes future brand while the
                // binding payment remains fixed.
                r.Check(
                    belowBrandMoved,
                    "the same below-requested completion moves future brand after brand already moved",
                    $"brand={belowBrandAtAcceptance:0.###}->" +
                    $"{belowBrandBeforeCompletion:0.###}->" +
                    $"{belowBrandAfterCompletion:0.###}");
            }
            finally
            {
                state.ProductBrandRecords.Clear();
                state.ProductBrandRecords.AddRange(savedBrands);
                state.CommercialHistory.Clear();
                state.CommercialHistory.AddRange(savedCommercialHistory);
                state.CommercialTimeline.Clear();
                state.CommercialTimeline.AddRange(savedCommercialTimeline);
                state.CommercialTimelineStartTick = savedTimelineStartTick;
                state.MarketStates.Clear();
                state.MarketStates.AddRange(savedMarketStates);
                state.RefreshMarketStateIndex();
                state.Reputations.Clear();
                foreach (KeyValuePair<int, CommercialReputation> entry in savedReputations)
                {
                    state.Reputations.Add(entry.Key, entry.Value);
                }
            }
        }

        private static SalesOrder MakeQualityPaymentOrder(
            ThingDef product,
            int id,
            QualityCategory requestedQuality,
            float unitPrice,
            float discountFraction)
        {
            return new SalesOrder
            {
                id = id,
                settlementId = -1,
                settlementName = "",
                line = new OrderLine(product, 1) { minQuality = requestedQuality },
                unitPrice = unitPrice,
                referenceUnitPrice = unitPrice,
                DiscountFraction = discountFraction,
                status = SalesOrderStatus.Accepted,
                deadlineTick = int.MaxValue,
                deliveredQuantity = 1,
                paidSilver = 0
            };
        }

        private static void MakeAndCompleteQualityPaymentMover(
            IntercolonyWorldComponent state,
            ThingDef product,
            int id,
            float qualityTarget)
        {
            SalesOrder mover = MakeQualityPaymentOrder(
                product, id, QualityCategory.Awful, unitPrice: 1f, discountFraction: 0f);
            SalesOrderService.Complete(
                state, mover, completedTick: 0,
                outcomeNote: "binding quality-payment self-test: brand mover",
                actualDeliveredQuality: new DeliveredQualityResult(qualityTarget, 1));
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
