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
    /// Self-test for the persisted, bounded commercial timeline spine (the 1.0 program Stage 0.3, docs/INTERCOLONY_1_0_IMPLEMENTATION_PLAN.md Stage 7).
    ///
    /// Verifies recording across all event types, monotonic IDs, querying by settlement
    /// and recency, retention pruning (global cap of 1,000 records, oldest dropped first),
    /// and Scribe XML serialization/deserialization round trip.
    /// </summary>
    public static class IntercolonyTimelineSelfTest
    {
        private class Results
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

            public void Info(string line)
            {
                sb.AppendLine($"        {line}");
            }
        }

        public static string Run(IntercolonyWorldComponent state)
        {
            Results r = new Results();
            r.sb.AppendLine("Commercial timeline spine self-test (the 1.0 program Stage 0.3)");

            if (state == null)
            {
                r.sb.AppendLine("  No world state available. Open or load a game first.");
                return Summarize(r);
            }

            // Snapshot actual list contents before running self-test.
            // CheckRetentionAndPruning calls Prune(), which drops from the front (oldest records).
            // Restoring by tail truncation would leave synthetic test records in place and destroy
            // real commercial history.
            List<CommercialEventRecord> savedRecords = new List<CommercialEventRecord>(state.CommercialTimeline);
            int savedStartTick = state.CommercialTimelineStartTick;

            try
            {
                CheckRecording(r, state);
                CheckWriteSites(r, state);
                CheckQuerying(r, state);
                CheckRetentionAndPruning(r, state);
                CheckScribeRoundTrip(r);
            }
            catch (Exception ex)
            {
                r.sb.AppendLine($"  EXCEPTION: {ex}");
                r.failed++;
            }
            finally
            {
                state.CommercialTimeline.Clear();
                state.CommercialTimeline.AddRange(savedRecords);
                state.CommercialTimelineStartTick = savedStartTick;

                r.Info($"commercial timeline restored to {state.CommercialTimeline.Count} record(s).");
            }

            return Summarize(r);
        }

        // --- Recording ---------------------------------------------------------------------

        private static void CheckRecording(Results r, IntercolonyWorldComponent state)
        {
            int before = state.CommercialTimeline.Count;

            ThingDef steel = ThingDefOf.Steel ?? ThingDefOf.Silver;

            CommercialEventRecord sale = CommercialTimelineService.Record(
                state,
                CommercialEventType.SaleCompleted,
                settlementId: 42,
                settlementName: "Testholme",
                relatedEntityId: 101,
                thingDef: steel,
                quantity: 75,
                silverAmount: 150,
                compactDetail: "Delivery to Testholme");

            r.Check(sale != null, "record created successfully");
            r.Check(state.CommercialTimeline.Count == before + 1, "record appended to world component timeline");
            r.Check(sale.id > 0, "record assigned positive monotonic ID", $"id={sale?.id}");
            r.Check(sale.tick == GenTicks.TicksGame, "record stamped with current tick", $"tick={sale?.tick}");
            r.Check(sale.settlementId == 42, "settlement ID preserved", $"settlementId={sale?.settlementId}");
            r.Check(sale.settlementName == "Testholme", "settlement name preserved", $"settlementName={sale?.settlementName}");
            r.Check(sale.type == CommercialEventType.SaleCompleted, "event type is SaleCompleted", $"type={sale?.type}");
            r.Check(sale.relatedEntityId == 101, "related entity ID preserved", $"relatedEntityId={sale?.relatedEntityId}");
            r.Check(sale.thingDef == steel, "thingDef preserved", $"thingDef={sale?.thingDef?.defName}");
            r.Check(sale.quantity == 75, "quantity preserved", $"quantity={sale?.quantity}");
            r.Check(sale.silverAmount == 150, "silver amount preserved as int", $"silverAmount={sale?.silverAmount}");
            r.Check(sale.compactDetail == "Delivery to Testholme", "compact detail preserved", $"compactDetail={sale?.compactDetail}");
            r.Check(sale.DaysAgo >= 0f, "DaysAgo is non-negative", $"DaysAgo={sale?.DaysAgo}");
            r.Check(state.CommercialTimelineStartTick != CommercialTimelineService.NoHistory,
                "recording stamps commercialTimelineStartTick on world state",
                $"startTick={state.CommercialTimelineStartTick}");

            string str = sale.ToString();
            r.Check(!string.IsNullOrEmpty(str) && str.Contains("SaleCompleted") && str.Contains("Testholme"),
                "ToString produces informative summary preferring settlement name", str);

            // Null thingDef case: verify a record with no thingDef records cleanly and ToString does not throw
            CommercialEventRecord nullDefRecord = CommercialTimelineService.Record(
                state,
                CommercialEventType.ContractStarted,
                settlementId: 42,
                settlementName: "Testholme",
                relatedEntityId: 202,
                thingDef: null,
                quantity: 0,
                silverAmount: 500,
                compactDetail: "Standing agreement without specific def");

            r.Check(nullDefRecord != null && nullDefRecord.thingDef == null,
                "record with null thingDef created successfully");
            string nullDefStr = nullDefRecord?.ToString();
            r.Check(!string.IsNullOrEmpty(nullDefStr) && !nullDefStr.Contains("x ") && nullDefStr.Contains("ContractStarted"),
                "ToString handles null thingDef without throwing", nullDefStr);

            // Read from the enum rather than a hand-written list, so a type added later cannot
            // leave this assertion quietly testing an outdated set.
            CommercialEventType[] allTypes =
                (CommercialEventType[])Enum.GetValues(typeof(CommercialEventType));

            int typeCount = 0;
            foreach (CommercialEventType eventType in allTypes)
            {
                CommercialEventRecord rec = CommercialTimelineService.Record(
                    state, eventType, settlementId: 50, settlementName: "Settlement 50", compactDetail: eventType.ToString());
                if (rec != null && rec.type == eventType)
                {
                    typeCount++;
                }
            }

            r.Check(typeCount == allTypes.Length, "every CommercialEventType variant records cleanly", $"{typeCount}/{allTypes.Length}");
        }

        // --- Write sites -------------------------------------------------------------------

        /// <summary>
        /// Drives the real order transitions rather than calling <see cref="CommercialTimelineService"/>
        /// directly, because the claim under test is that the production paths record at all — a test
        /// that recorded its own events would pass with every write site deleted.
        ///
        /// The settlement IDs are deliberately fictitious. <c>ReputationService.ForSettlement</c>
        /// resolves through <c>IntercolonyMarketAccess.FindSettlement</c>, which returns null for an
        /// ID no settlement owns, so the reputation hooks on these paths no-op and leave no records
        /// behind. <c>IntercolonyOrderSelfTest</c> relies on the same property.
        /// </summary>
        private static void CheckWriteSites(Results r, IntercolonyWorldComponent state)
        {
            const int settlementId = 9101;
            ThingDef def = ThingDefOf.Silver;

            SalesOrder failing = NewSalesOrder(state, settlementId, def);
            r.Check(SalesOrderService.Fail(failing, "test failure"), "Fail transition succeeded");
            r.Check(FindRecordFor(state, failing.id, CommercialEventType.SaleFailed) != null,
                "SalesOrderService.Fail writes a SaleFailed record");

            SalesOrder cancelling = NewSalesOrder(state, settlementId, def);
            r.Check(SalesOrderService.Cancel(cancelling), "Cancel transition succeeded");
            CommercialEventRecord cancelRecord =
                FindRecordFor(state, cancelling.id, CommercialEventType.SaleCancelled);
            r.Check(cancelRecord != null, "SalesOrderService.Cancel writes a SaleCancelled record");
            r.Check(cancelRecord != null && cancelRecord.settlementName == "Testholme",
                "the write site freezes the settlement name", cancelRecord?.settlementName);

            // A war voids an order without blaming the player, so it must not land as SaleFailed.
            SalesOrder atWar = NewSalesOrder(state, settlementId, def);
            r.Check(HostilityPolicy.ApplyToSalesOrder(atWar, sendLetter: false),
                "war cancellation transition succeeded");
            r.Check(FindRecordFor(state, atWar.id, CommercialEventType.SaleCancelled) != null,
                "a war-voided order records as cancelled, not failed");
            r.Check(FindRecordFor(state, atWar.id, CommercialEventType.SaleFailed) == null,
                "a war-voided order records no failure against the player");

            PurchaseOrder purchase = NewPurchaseOrder(state, settlementId, def);
            r.Check(PurchaseOrderService.Cancel(purchase), "purchase cancel transition succeeded");
            r.Check(FindRecordFor(state, purchase.id, CommercialEventType.PurchaseCancelled) != null,
                "PurchaseOrderService.Cancel writes a PurchaseCancelled record");

            // The supplier failing to deliver is not the player withdrawing.
            PurchaseOrder lost = NewPurchaseOrder(state, settlementId, def);
            r.Check(HostilityPolicy.ApplyToPurchaseOrder(lost, sendLetter: false),
                "purchase lost-to-war transition succeeded");
            r.Check(FindRecordFor(state, lost.id, CommercialEventType.PurchaseFailed) != null,
                "an order lost to war records as a purchase failure");
            r.Check(FindRecordFor(state, lost.id, CommercialEventType.PurchaseCancelled) == null,
                "an order lost to war is not recorded as a player cancellation");
        }

        private static SalesOrder NewSalesOrder(
            IntercolonyWorldComponent state, int settlementId, ThingDef def)
        {
            return new SalesOrder
            {
                id = state.NextId(),
                settlementId = settlementId,
                settlementName = "Testholme",
                factionName = "Test faction",
                line = new OrderLine(def, 10),
                status = SalesOrderStatus.Accepted
            };
        }

        private static PurchaseOrder NewPurchaseOrder(
            IntercolonyWorldComponent state, int settlementId, ThingDef def)
        {
            return new PurchaseOrder
            {
                id = state.NextId(),
                settlementId = settlementId,
                settlementName = "Testholme",
                factionName = "Test faction",
                thingDef = def,
                quantity = 10,
                status = PurchaseOrderStatus.Confirmed
            };
        }

        private static CommercialEventRecord FindRecordFor(
            IntercolonyWorldComponent state, int relatedEntityId, CommercialEventType type)
        {
            return state.CommercialTimeline.Find(
                e => e != null && e.relatedEntityId == relatedEntityId && e.type == type);
        }

        // --- Querying ----------------------------------------------------------------------

        private static void CheckQuerying(Results r, IntercolonyWorldComponent state)
        {
            CommercialTimelineService.Record(state, CommercialEventType.SaleCompleted, settlementId: 9001, settlementName: "Alpha", compactDetail: "first 9001");
            CommercialTimelineService.Record(state, CommercialEventType.PurchaseCompleted, settlementId: 9002, settlementName: "Beta", compactDetail: "only 9002");
            CommercialTimelineService.Record(state, CommercialEventType.ContractStarted, settlementId: 9001, settlementName: "Alpha", compactDetail: "second 9001");

            List<CommercialEventRecord> for9001 = CommercialTimelineService.ForSettlement(state, 9001);
            r.Check(for9001.Count == 2, "ForSettlement filters exclusively to matching settlement", $"count={for9001.Count}");
            r.Check(for9001.Count == 2 && for9001[0].compactDetail == "second 9001" && for9001[1].compactDetail == "first 9001",
                "ForSettlement returns records newest first");

            List<CommercialEventRecord> capped = CommercialTimelineService.ForSettlement(state, 9001, maxCount: 1);
            r.Check(capped.Count == 1 && capped[0].compactDetail == "second 9001",
                "ForSettlement respects maxCount cap");

            List<CommercialEventRecord> forMissing = CommercialTimelineService.ForSettlement(state, 99999);
            r.Check(forMissing.Count == 0, "ForSettlement for unknown settlement returns empty list");

            List<CommercialEventRecord> recent = CommercialTimelineService.Recent(state, 3);
            r.Check(recent.Count == 3, "Recent returns requested count");
            r.Check(recent[0].compactDetail == "second 9001" && recent[1].compactDetail == "only 9002",
                "Recent returns global records newest first");

            // Query helpers tolerate null thingDef
            CommercialEventRecord nullDef = CommercialTimelineService.Record(
                state, CommercialEventType.ContractCompleted, settlementId: 9003, settlementName: "Gamma", thingDef: null);
            List<CommercialEventRecord> for9003 = CommercialTimelineService.ForSettlement(state, 9003);
            r.Check(for9003.Count == 1 && for9003[0].thingDef == null,
                "ForSettlement query helper returns null-thingDef record without throwing");
            List<CommercialEventRecord> recentWithNull = CommercialTimelineService.Recent(state, 1);
            r.Check(recentWithNull.Count == 1 && recentWithNull[0].thingDef == null,
                "Recent query helper returns null-thingDef record without throwing");
        }

        // --- Retention and Pruning ---------------------------------------------------------

        private static void CheckRetentionAndPruning(Results r, IntercolonyWorldComponent state)
        {
            r.Check(CommercialTimelineService.MaxTimelineRecords == 1000,
                "retention cap is 1,000 records (the 1.0 program Stage 0.3)",
                $"MaxTimelineRecords={CommercialTimelineService.MaxTimelineRecords}");

            int initialCount = state.CommercialTimeline.Count;

            // Explicitly insert a known oldest marker at the beginning to test pruning by identity
            CommercialEventRecord knownOldest = new CommercialEventRecord(
                id: 99999,
                tick: GenTicks.TicksGame - 100000,
                settlementId: 7777,
                type: CommercialEventType.SaleCompleted,
                settlementName: "Oldest Settlement",
                compactDetail: "known-oldest-record");
            state.CommercialTimeline.Insert(0, knownOldest);
            int oldestId = knownOldest.id;

            // Fill timeline to exceed MaxTimelineRecords by 50
            int totalToCreate = (CommercialTimelineService.MaxTimelineRecords + 50) - state.CommercialTimeline.Count;
            if (totalToCreate > 0)
            {
                for (int i = 0; i < totalToCreate; i++)
                {
                    state.CommercialTimeline.Add(new CommercialEventRecord(
                        id: 100000 + i,
                        tick: GenTicks.TicksGame + i,
                        settlementId: 8000,
                        type: CommercialEventType.SaleCompleted,
                        settlementName: "Bulk Settlement",
                        compactDetail: $"bulk-{i}"));
                }
            }

            int prePruneCount = state.CommercialTimeline.Count;
            r.Check(prePruneCount >= CommercialTimelineService.MaxTimelineRecords + 50,
                "staged oversized timeline for pruning", $"count={prePruneCount}");

            int removed = CommercialTimelineService.Prune(state);
            r.Check(removed == prePruneCount - CommercialTimelineService.MaxTimelineRecords,
                "pruning drops exactly the excess records",
                $"removed={removed}");

            r.Check(state.CommercialTimeline.Count == CommercialTimelineService.MaxTimelineRecords,
                "pruning bounds timeline to MaxTimelineRecords",
                $"retained={state.CommercialTimeline.Count}");

            // Verify known-oldest record was dropped by identity (Item 7)
            bool oldestStillPresent = state.CommercialTimeline.Exists(e => e != null && e.id == oldestId);
            r.Check(!oldestStillPresent, "pruning drops known oldest record by identity", $"oldestId={oldestId}");

            // Verify oldest were dropped, newest were retained
            CommercialEventRecord newest = state.CommercialTimeline[state.CommercialTimeline.Count - 1];
            r.Check(newest.compactDetail == $"bulk-{totalToCreate - 1}",
                "pruning preserved newest records and dropped oldest",
                $"newest detail='{newest.compactDetail}'");
        }

        // --- Scribe Round Trip -------------------------------------------------------------

        private static void CheckScribeRoundTrip(Results r)
        {
            ThingDef def = ThingDefOf.Silver;
            List<CommercialEventRecord> savedList = new List<CommercialEventRecord>
            {
                new CommercialEventRecord(
                    id: 777,
                    tick: 123456,
                    settlementId: 88,
                    type: CommercialEventType.ContractCompleted,
                    settlementName: "Silverhold",
                    relatedEntityId: 999,
                    thingDef: def,
                    quantity: 500,
                    silverAmount: 2500,
                    compactDetail: "Standing agreement completed"),
                new CommercialEventRecord(
                    id: 778,
                    tick: 123500,
                    settlementId: 89,
                    type: CommercialEventType.ContractStarted,
                    settlementName: "NullDefOutpost",
                    relatedEntityId: 1000,
                    thingDef: null,
                    quantity: 0,
                    silverAmount: 100,
                    compactDetail: "Record with null thingDef")
            };

            List<CommercialEventRecord> loadedList = null;
            string roundTripFailure = null;
            string tempPath = Path.Combine(
                Path.GetTempPath(), $"Intercolony-TimelineRecord-{Guid.NewGuid():N}.xml");

            try
            {
                Scribe.saver.InitSaving(tempPath, "intercolonyTimelineTest");
                Scribe_Collections.Look(ref savedList, "commercialTimeline", LookMode.Deep);
                Scribe.saver.FinalizeSaving();

                Scribe.loader.InitLoading(tempPath);
                Scribe_Collections.Look(ref loadedList, "commercialTimeline", LookMode.Deep);
                Scribe.loader.FinalizeLoading();
            }
            catch (Exception ex)
            {
                roundTripFailure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                Scribe.ForceStop();
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }

            bool ok = roundTripFailure == null &&
                      loadedList != null &&
                      loadedList.Count == 2 &&
                      loadedList[0].id == 777 &&
                      loadedList[0].tick == 123456 &&
                      loadedList[0].settlementId == 88 &&
                      loadedList[0].settlementName == "Silverhold" &&
                      loadedList[0].type == CommercialEventType.ContractCompleted &&
                      loadedList[0].relatedEntityId == 999 &&
                      loadedList[0].thingDef == def &&
                      loadedList[0].quantity == 500 &&
                      loadedList[0].silverAmount == 2500 &&
                      loadedList[0].compactDetail == "Standing agreement completed" &&
                      loadedList[1].id == 778 &&
                      loadedList[1].settlementName == "NullDefOutpost" &&
                      loadedList[1].thingDef == null &&
                      loadedList[1].silverAmount == 100 &&
                      loadedList[1].compactDetail == "Record with null thingDef";

            r.Check(ok, "commercial timeline survives a Scribe save/load round trip",
                roundTripFailure ?? (loadedList != null && loadedList.Count > 0 ? loadedList[0].ToString() : "null"));

            // Verify that PostLoadInit validation retains records with null thingDef (Item 6)
            if (loadedList != null)
            {
                int nullEntries = loadedList.RemoveAll(e => e == null);
                r.Check(nullEntries == 0 && loadedList.Count == 2 && loadedList.Exists(e => e.thingDef == null),
                    "load validation preserves records with null thingDef");
            }
        }

        private static string Summarize(Results r)
        {
            r.sb.AppendLine();
            r.sb.AppendLine($"  {r.passed} passed, {r.failed} failed.");
            return r.sb.ToString();
        }
    }
}
