using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using RimWorld;
using RimWorld.Planet;
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

            public void Info(string line)
            {
                sb.AppendLine($"        {line}");
            }

            public void Skip(string label, string detail)
            {
                skipped++;
                sb.AppendLine($"  SKIPPED  {label} — {detail}");
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
                CheckContractWriteSites(r, state);
                CheckQuerying(r, state);
                CheckCommercialHistoryReadModel(r, state);
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

        /// <summary>
        /// Drives every contract transition that owns a timeline write. In particular, the three
        /// starts remain separate because covering only the easiest caller would let player-made
        /// proposals or renewals silently stop recording while incoming offers still passed.
        /// </summary>
        private static void CheckContractWriteSites(Results r, IntercolonyWorldComponent state)
        {
            Settlement subject = FirstAccessibleSettlement();
            if (subject == null || IntercolonyProductClassifier.TradableDefs.Count == 0)
            {
                const string reason = "no accessible live settlement or tradable contract item";
                SkipContractWrite(r, "incoming-offer transition", CommercialEventType.ContractStarted, reason);
                SkipContractWrite(r, "player-proposal transition", CommercialEventType.ContractStarted, reason);
                SkipContractWrite(r, "renewal transition", CommercialEventType.ContractStarted, reason);
                SkipContractWrite(r, "completion transition", CommercialEventType.ContractCompleted, reason);
                SkipContractWrite(r, "breach transition", CommercialEventType.ContractFailed, reason);
                SkipContractWrite(r, "unreachable-counterparty transition", CommercialEventType.ContractCancelled, reason);
                SkipContractWrite(r, "player-withdrawal transition", CommercialEventType.ContractCancelled, reason);
                return;
            }

            // Snapshot the exact contents rather than restoring by count. Contract transitions can
            // remove or replace objects, so trimming a tail could leave synthetic contracts behind
            // while silently discarding the player's real agreements.
            List<RecurringContract> savedContracts = new List<RecurringContract>(state.Contracts);
            List<CommercialHistoryEntry> savedCommercialHistory =
                new List<CommercialHistoryEntry>(state.CommercialHistory);
            bool hadSubjectReputation = state.Reputations.TryGetValue(
                subject.ID, out CommercialReputation savedSubjectReputation);
            state.Contracts.Clear();
            state.CommercialHistory.Clear();
            state.Reputations.Remove(subject.ID);

            try
            {
                RecurringContract incoming = MakeContract(state, subject, 3);
                bool incomingAccepted = ContractService.AcceptOffer(state, incoming);
                CheckContractWrite(r, state, incoming, CommercialEventType.ContractStarted,
                    incomingAccepted && incoming.status == ContractStatus.Active,
                    "AcceptOffer activates an incoming contract");

                // Proposal decisions are seeded production behavior rather than a test override.
                // Fresh IDs give the real resolver independent decisions; a deleted write still
                // cannot be hidden because only an actually accepted contract is examined below.
                RecurringContract acceptedProposal = null;
                for (int attempt = 0; attempt < 64 && acceptedProposal == null; attempt++)
                {
                    RecurringContract proposal = MakeContract(state, subject, 3);
                    proposal.decisionDueTick = GenTicks.TicksGame;
                    proposal.proposalAppeal = 1f;
                    ContractService.ResolvePlayerProposal(state, proposal);
                    if (proposal.status == ContractStatus.Active)
                    {
                        acceptedProposal = proposal;
                    }
                }

                CheckContractWrite(r, state, acceptedProposal, CommercialEventType.ContractStarted,
                    acceptedProposal != null && acceptedProposal.status == ContractStatus.Active,
                    "ResolvePlayerProposal activates an accepted player proposal");

                RecurringContract renewal = MakeContract(state, subject, 3);
                renewal.status = ContractStatus.Completed;
                renewal.renewalOffered = true;
                renewal.renewalExpiryTick = GenTicks.TicksGame + GenDate.TicksPerDay;
                bool renewalAccepted = ContractService.AcceptRenewal(state, renewal);
                CheckContractWrite(r, state, renewal, CommercialEventType.ContractStarted,
                    renewalAccepted && renewal.status == ContractStatus.Active,
                    "AcceptRenewal starts a fresh contract run");

                RecurringContract completed = MakeContract(state, subject, 3);
                completed.TryAccept();
                ContractService.Complete(state, completed);
                CheckContractWrite(r, state, completed, CommercialEventType.ContractCompleted,
                    completed.status == ContractStatus.Completed,
                    "Complete ends the contract as completed");

                RecurringContract breached = MakeContract(state, subject, 3);
                breached.TryAccept();
                breached.consecutiveFailures = RecurringContract.BreachThreshold - 1;
                SalesOrder missed = NewSalesOrder(state, subject.ID, breached.thingDef);
                missed.status = SalesOrderStatus.Failed;
                ContractService.ResolveCycle(state, breached, missed);
                CheckContractWrite(r, state, breached, CommercialEventType.ContractFailed,
                    breached.status == ContractStatus.Breached,
                    "ResolveCycle breaches the contract at the failure threshold");

                RecurringContract unreachable = MakeContract(state, subject, 3);
                unreachable.TryAccept();
                // World-object IDs are positive. A negative ID exercises the real missing-counterparty
                // branch without fabricating a Settlement merely to make the self-test convenient.
                unreachable.settlementId = -unreachable.id;
                ContractService.RaiseCycleOrder(state, unreachable);
                CheckContractWrite(r, state, unreachable, CommercialEventType.ContractCancelled,
                    unreachable.status == ContractStatus.Cancelled,
                    "RaiseCycleOrder cancels an unreachable contract");

                RecurringContract withdrawn = MakeContract(state, subject, 3);
                withdrawn.TryAccept();
                bool cancelled = ContractService.CancelContract(state, withdrawn);
                CheckContractWrite(r, state, withdrawn, CommercialEventType.ContractCancelled,
                    cancelled && withdrawn.status == ContractStatus.Cancelled,
                    "CancelContract withdraws an active contract");
            }
            finally
            {
                // Reputation and history are restored with the contract objects because completion,
                // breach, and withdrawal deliberately mutate all three production stores.
                state.Reputations.Remove(subject.ID);
                if (hadSubjectReputation)
                {
                    state.Reputations.Add(subject.ID, savedSubjectReputation);
                }

                state.Contracts.Clear();
                state.Contracts.AddRange(savedContracts);
                state.CommercialHistory.Clear();
                state.CommercialHistory.AddRange(savedCommercialHistory);
            }
        }

        private static void CheckContractWrite(
            Results r,
            IntercolonyWorldComponent state,
            RecurringContract contract,
            CommercialEventType type,
            bool transitioned,
            string transitionLabel)
        {
            r.Check(transitioned, transitionLabel, contract?.status.ToString() ?? "no accepted contract");
            r.Check(contract != null && FindRecordFor(state, contract.id, type) != null,
                $"{transitionLabel} writes {type}");
            int count = contract == null ? 0 : CountRecordsFor(state, contract.id, type);
            r.Check(count == 1, $"{transitionLabel} writes exactly one {type}", $"count={count}");
        }

        private static void SkipContractWrite(
            Results r, string transitionLabel, CommercialEventType type, string reason)
        {
            // Report all three missing proofs. Counting one skipped transition would hide that its
            // status, existence, and duplicate protections were all unavailable on this world.
            r.Skip($"{transitionLabel} reaches its terminal status", reason);
            r.Skip($"{transitionLabel} writes {type}", reason);
            r.Skip($"{transitionLabel} writes exactly one {type}", reason);
        }

        /// <summary>
        /// Deliberately mirrors <c>IntercolonyContractSelfTest.MakeContract</c>. Each self-test owns
        /// its fixtures, so changing one suite cannot accidentally weaken another suite's setup.
        /// </summary>
        private static RecurringContract MakeContract(
            IntercolonyWorldComponent state, Settlement settlement, int cycles)
        {
            ThingDef def = ThingDefOf.Steel != null &&
                           IntercolonyProductClassifier.IsFungibleTradeItem(ThingDefOf.Steel)
                ? ThingDefOf.Steel
                : IntercolonyProductClassifier.TradableDefs[0];

            RecurringContract contract = new RecurringContract
            {
                id = state.NextId(),
                settlementId = settlement.ID,
                settlementName = settlement.Label ?? "unnamed",
                factionName = settlement.Faction?.Name ?? "",
                thingDef = def,
                quantityPerCycle = 100,
                cadenceTicks = GenDate.TicksPerQuadrum,
                totalCycles = cycles,
                unitPrice = Mathf.Max(0.5f, IntercolonyPricing.BaseValue(def, null) * 1.15f),
                status = ContractStatus.Offered,
                offerExpiryTick = GenTicks.TicksGame + GenDate.TicksPerDay * 8
            };

            state.AddContract(contract);
            return contract;
        }

        private static Settlement FirstAccessibleSettlement()
        {
            foreach (Settlement settlement in Find.WorldObjects.Settlements)
            {
                if (SettlementProfileGenerator.IsEligible(settlement) &&
                    IntercolonyMarketAccess.IsAccessible(settlement))
                {
                    return settlement;
                }
            }

            return null;
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

        private static int CountRecordsFor(
            IntercolonyWorldComponent state, int relatedEntityId, CommercialEventType type)
        {
            return state.CommercialTimeline.FindAll(
                e => e != null && e.relatedEntityId == relatedEntityId && e.type == type).Count;
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

        // --- Stage 7A commercial-history read model ----------------------------------------

        /// <summary>
        /// Exercises the aggregate/timeline split through the public read model. These fixtures
        /// deliberately use recorded payments that disagree with unit-price arithmetic: a test
        /// that recomputes a price, counts timeline rows, or creates state while reading must fail
        /// without needing an existing save to contain any particular settlement.
        /// </summary>
        private static void CheckCommercialHistoryReadModel(
            Results r, IntercolonyWorldComponent state)
        {
            const int summarySettlementId = 710701;
            const int oldHistorySettlementId = 710702;
            const int spineOnlySettlementId = 710703;
            const int timelineSettlementId = 710704;
            const int quietSettlementId = 710705;

            List<CommercialHistoryEntry> savedHistory =
                new List<CommercialHistoryEntry>(state.CommercialHistory);
            Dictionary<int, CommercialReputation> savedReputations =
                new Dictionary<int, CommercialReputation>(state.Reputations);
            List<CommercialEventRecord> savedTimeline =
                new List<CommercialEventRecord>(state.CommercialTimeline);
            List<SalesOrder> savedSalesOrders = new List<SalesOrder>(state.Orders);
            List<PurchaseOrder> savedPurchaseOrders =
                new List<PurchaseOrder>(state.PurchaseOrders);
            List<RecurringContract> savedContracts =
                new List<RecurringContract>(state.Contracts);
            List<ProcurementContract> savedProcurementContracts =
                new List<ProcurementContract>(state.ProcurementContracts);
            int savedTimelineStartTick = state.CommercialTimelineStartTick;
            FieldInfo saveVersionField = typeof(IntercolonyWorldComponent).GetField(
                "saveVersion", BindingFlags.Instance | BindingFlags.NonPublic);
            int savedSaveVersion = saveVersionField == null
                ? -1
                : (int)saveVersionField.GetValue(state);
            Map silverMap = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
            Dictionary<Thing, int> savedSilver = SnapshotHistorySilver(silverMap);

            ThingDef fixtureDef = ThingDefOf.Steel ?? ThingDefOf.Silver;

            try
            {
                if (fixtureDef == null)
                {
                    const string reason = "ThingDefOf.Steel and ThingDefOf.Silver are unavailable";
                    r.Skip("U1 summary survives timeline pruning", reason);
                    r.Skip("U2 recorded sale and purchase silver accumulate", reason);
                    r.Skip("U3 failed, cancelled and refunded value stays unchanged", reason);
                }
                else
                {
                    ResetHistoryFixtures(state);

                    // U1: all three durable count/value sources are populated before the detail
                    // timeline is removed. Timeline silver is intentionally unrelated to 37+83.
                    SalesOrder summarySale = MakeHistorySale(
                        710711, summarySettlementId, fixtureDef, 4, 37, 9.99f,
                        SalesOrderStatus.Completed);
                    PurchaseOrder summaryPurchase = MakeHistoryPurchase(
                        710712, summarySettlementId, fixtureDef, 30, 83, 2.5f,
                        PurchaseOrderStatus.Completed);
                    state.Orders.Add(summarySale);
                    state.PurchaseOrders.Add(summaryPurchase);
                    state.RecordCompletedSale(summarySale);
                    state.RecordCompletedPurchase(summaryPurchase);

                    // Anchor the pruning check to the facts this fixture creates: one completed
                    // sale, one completed purchase, and the two recorded payments (37 + 83).
                    const int u1ExpectedCompletedSales = 1;
                    const int u1ExpectedCompletedPurchases = 1;
                    const int u1ExpectedTradeValue = 37 + 83;

                    CommercialReputation summaryReputation = new CommercialReputation(
                        summarySettlementId, "Summary Testholme", "Summary faction");
                    summaryReputation.purchasesCompleted = 1;
                    state.Reputations[summarySettlementId] = summaryReputation;
                    state.Contracts.Add(new RecurringContract
                    {
                        id = 710713,
                        settlementId = summarySettlementId,
                        settlementName = "Summary Testholme",
                        thingDef = fixtureDef,
                        quantityPerCycle = 1,
                        totalCycles = 1,
                        unitPrice = 1f,
                        status = ContractStatus.Active
                    });
                    state.ProcurementContracts.Add(new ProcurementContract
                    {
                        id = 710714,
                        settlementId = summarySettlementId,
                        settlementName = "Summary Testholme",
                        thingDef = fixtureDef,
                        quantityPerCycle = 1,
                        totalCycles = 1,
                        unitPrice = 1f,
                        cadenceDays = 1,
                        status = ProcurementContractStatus.Active
                    });
                    state.CommercialTimelineStartTick = 7107000;
                    state.CommercialTimeline.Add(new CommercialEventRecord(
                        710715, 7107010, summarySettlementId, CommercialEventType.SaleCompleted,
                        "Summary Testholme", summarySale.id, fixtureDef, 4, 5, "detail sale"));
                    state.CommercialTimeline.Add(new CommercialEventRecord(
                        710716, 7107020, summarySettlementId, CommercialEventType.PurchaseCompleted,
                        "Summary Testholme", summaryPurchase.id, fixtureDef, 30, 7, "detail purchase"));

                    CommercialHistorySummary u1Before =
                        CommercialHistoryService.BuildSummary(state, summarySettlementId);
                    state.CommercialTimeline.Clear();
                    CommercialHistorySummary u1After =
                        CommercialHistoryService.BuildSummary(state, summarySettlementId);
                    r.Check(
                        u1Before.CompletedSales == u1ExpectedCompletedSales &&
                        u1Before.CompletedPurchases == u1ExpectedCompletedPurchases &&
                        u1Before.TotalKnownTradeValue == u1ExpectedTradeValue &&
                        u1Before.CompletedSales == u1After.CompletedSales &&
                        u1Before.CompletedPurchases == u1After.CompletedPurchases &&
                        u1Before.ActiveContracts == u1After.ActiveContracts &&
                        u1Before.TotalKnownTradeValue == u1After.TotalKnownTradeValue,
                        "U1 summary counts and trade value survive timeline pruning",
                        $"before={DescribeSummary(u1Before)}; after={DescribeSummary(u1After)}");

                    // U2: the fixture's agreed-price totals are 40 and 75, while the recorded
                    // payments are 37 and 83. Each delta must use the latter in its direction.
                    ResetHistoryFixtures(state);
                    SalesOrder u2Sale = MakeHistorySale(
                        710721, summarySettlementId, fixtureDef, 4, 37, 9.99f,
                        SalesOrderStatus.Completed);
                    PurchaseOrder u2Purchase = MakeHistoryPurchase(
                        710722, summarySettlementId, fixtureDef, 30, 83, 2.5f,
                        PurchaseOrderStatus.Completed);
                    state.Orders.Add(u2Sale);
                    state.PurchaseOrders.Add(u2Purchase);
                    int u2Before = CommercialHistoryService.BuildSummary(
                        state, summarySettlementId).TotalKnownTradeValue;
                    state.RecordCompletedSale(u2Sale);
                    int u2AfterSale = CommercialHistoryService.BuildSummary(
                        state, summarySettlementId).TotalKnownTradeValue;
                    state.RecordCompletedPurchase(u2Purchase);
                    int u2AfterPurchase = CommercialHistoryService.BuildSummary(
                        state, summarySettlementId).TotalKnownTradeValue;
                    r.Check(
                        u2AfterSale - u2Before == 37 &&
                        u2AfterPurchase - u2AfterSale == 83,
                        "U2 total trade value uses the recorded sale and purchase silver",
                        $"sale increment expected=37 actual={u2AfterSale - u2Before}; " +
                        $"purchase increment expected=83 actual={u2AfterPurchase - u2AfterSale}; " +
                        $"totals {u2Before}->{u2AfterSale}->{u2AfterPurchase}");

                    // U3: terminal non-completions carry payment-shaped data but never enter the
                    // completed aggregate. The refunded purchase is represented by the terminal
                    // SupplierDefault status, which is the status the refund path writes.
                    ResetHistoryFixtures(state);
                    SalesOrder u3BaselineSale = MakeHistorySale(
                        710731, summarySettlementId, fixtureDef, 4, 37, 9.99f,
                        SalesOrderStatus.Completed);
                    PurchaseOrder u3BaselinePurchase = MakeHistoryPurchase(
                        710732, summarySettlementId, fixtureDef, 30, 83, 2.5f,
                        PurchaseOrderStatus.Completed);
                    state.RecordCompletedSale(u3BaselineSale);
                    state.RecordCompletedPurchase(u3BaselinePurchase);
                    int u3Baseline = CommercialHistoryService.BuildSummary(
                        state, summarySettlementId).TotalKnownTradeValue;

                    SalesOrder failedSale = MakeHistorySale(
                        710733, summarySettlementId, fixtureDef, 8, 111, 12.5f,
                        SalesOrderStatus.Failed);
                    state.RecordCompletedSale(failedSale);
                    int u3AfterFailedSale = CommercialHistoryService.BuildSummary(
                        state, summarySettlementId).TotalKnownTradeValue;
                    r.Check(
                        u3AfterFailedSale == u3Baseline,
                        "U3 failed sale adds no trade value",
                        $"baseline={u3Baseline}; after failed sale={u3AfterFailedSale}; " +
                        $"failed payment={failedSale.paidSilver}; status={failedSale.status}");

                    SalesOrder cancelledOrder = MakeHistorySale(
                        710734, summarySettlementId, fixtureDef, 9, 127, 14.5f,
                        SalesOrderStatus.Cancelled);
                    state.RecordCompletedSale(cancelledOrder);
                    int u3AfterCancelledOrder = CommercialHistoryService.BuildSummary(
                        state, summarySettlementId).TotalKnownTradeValue;
                    r.Check(
                        u3AfterCancelledOrder == u3AfterFailedSale,
                        "U3 cancelled order adds no trade value",
                        $"before cancelled={u3AfterFailedSale}; after={u3AfterCancelledOrder}; " +
                        $"cancelled payment={cancelledOrder.paidSilver}; status={cancelledOrder.status}");

                    PurchaseOrder refundedPurchase = MakeHistoryPurchase(
                        710735, summarySettlementId, fixtureDef, 11, 139, 3.25f,
                        PurchaseOrderStatus.SupplierDefault);
                    refundedPurchase.outcomeNote = "Refunded by supplier default.";
                    state.RecordCompletedPurchase(refundedPurchase);
                    int u3AfterRefundedPurchase = CommercialHistoryService.BuildSummary(
                        state, summarySettlementId).TotalKnownTradeValue;
                    r.Check(
                        u3AfterRefundedPurchase == u3AfterCancelledOrder,
                        "U3 refunded purchase adds no trade value",
                        $"before refund={u3AfterCancelledOrder}; after={u3AfterRefundedPurchase}; " +
                        $"refunded payment={refundedPurchase.paidSilver}; " +
                        $"status={refundedPurchase.status}; note={refundedPurchase.outcomeNote}");
                }

                // U4: a missing record and durable history without a retained event are different
                // coverage answers. The no-history answer is the explicit bool, not a date value.
                ResetHistoryFixtures(state);
                state.CommercialTimelineStartTick = 7107400;
                state.CommercialHistory.Add(new CommercialHistoryEntry
                {
                    settlementId = oldHistorySettlementId,
                    thingDef = fixtureDef,
                    completedSaleCount = 1,
                    totalTradeValue = 73
                });
                CommercialHistorySummary neverTraded =
                    CommercialHistoryService.BuildSummary(state, quietSettlementId);
                CommercialHistorySummary predatesSpine =
                    CommercialHistoryService.BuildSummary(state, oldHistorySettlementId);
                r.Check(
                    neverTraded.HistoryCoverage != predatesSpine.HistoryCoverage &&
                    neverTraded.HistoryCoverage == CommercialHistoryCoverage.None &&
                    predatesSpine.HistoryCoverage == CommercialHistoryCoverage.AggregateOnly &&
                    !neverTraded.HasTradingSince,
                    "U4 never-traded and pre-spine history have distinguishable coverage",
                    $"never coverage={neverTraded.HistoryCoverage}; pre-spine coverage=" +
                    $"{predatesSpine.HistoryCoverage}; never since=" +
                    $"{neverTraded.TradingSinceTick}/{neverTraded.HasTradingSince}; " +
                    $"pre-spine since={predatesSpine.TradingSinceTick}/" +
                    $"{predatesSpine.HasTradingSince}");

                // U5: without a retained event, the only honest date is the spine boundary. It is
                // explicitly flagged as a lower bound so the UI cannot call it a first trade.
                ResetHistoryFixtures(state);
                const int spineBoundary = 7107500;
                state.CommercialTimelineStartTick = spineBoundary;
                state.CommercialHistory.Add(new CommercialHistoryEntry
                {
                    settlementId = spineOnlySettlementId,
                    thingDef = fixtureDef,
                    completedSaleCount = 1,
                    totalTradeValue = 91
                });
                CommercialHistorySummary spineSummary =
                    CommercialHistoryService.BuildSummary(state, spineOnlySettlementId);
                r.Check(
                    spineSummary.HasTradingSince &&
                    spineSummary.TradingSinceTick == spineBoundary &&
                    spineSummary.TradingSinceIsTimelineStart &&
                    spineSummary.HistoryPredatesTimeline,
                    "U5 trading-since reports the timeline spine boundary",
                    $"summary={DescribeSummary(spineSummary)}; boundary={spineBoundary}");

                // U6: include three retained target events, one unsupported future/noise value,
                // and one meaningful event for a different settlement.
                ResetHistoryFixtures(state);
                CommercialEventType unsupportedType = (CommercialEventType)999;
                state.CommercialTimeline.Add(new CommercialEventRecord(
                    710761, 100, timelineSettlementId, CommercialEventType.SaleCompleted,
                    "Timeline Testholme", compactDetail: "old target"));
                state.CommercialTimeline.Add(new CommercialEventRecord(
                    710762, 200, timelineSettlementId, CommercialEventType.ContractStarted,
                    "Timeline Testholme", compactDetail: "middle target"));
                state.CommercialTimeline.Add(new CommercialEventRecord(
                    710763, 300, timelineSettlementId, CommercialEventType.PurchaseCompleted,
                    "Timeline Testholme", compactDetail: "new target"));
                state.CommercialTimeline.Add(new CommercialEventRecord(
                    710764, 400, timelineSettlementId, unsupportedType,
                    "Timeline Testholme", compactDetail: "noise target"));
                state.CommercialTimeline.Add(new CommercialEventRecord(
                    710765, 500, quietSettlementId, CommercialEventType.SaleCompleted,
                    "Other Testholme", compactDetail: "other settlement"));

                List<CommercialEventRecord> u6All =
                    CommercialHistoryService.BuildTimeline(state, timelineSettlementId, 20);
                List<CommercialEventRecord> u6Capped =
                    CommercialHistoryService.BuildTimeline(state, timelineSettlementId, 2);
                r.Check(
                    u6All.Count == 3 && u6All.TrueForAll(record => record.type != unsupportedType),
                    "U6 timeline keeps only meaningful event types",
                    $"returned ids={DescribeIds(u6All)}; types={DescribeTypes(u6All)}");
                r.Check(
                    u6All.TrueForAll(record => record.settlementId == timelineSettlementId),
                    "U6 timeline excludes another settlement's events",
                    $"requested settlement={timelineSettlementId}; returned ids={DescribeIds(u6All)}");
                r.Check(
                    u6All.Count == 3 && u6All[0].id == 710763 &&
                    u6All[1].id == 710762 && u6All[2].id == 710761,
                    "U6 timeline is newest first",
                    $"ordered ids={DescribeIds(u6All)}");
                r.Check(
                    u6Capped.Count <= 2,
                    "U6 timeline respects the requested bound",
                    $"requested max=2; returned count={u6Capped.Count}; " +
                    $"ordered ids={DescribeIds(u6Capped)}");

                // U7: use an ID absent from every persisted collection before both read calls.
                ResetHistoryFixtures(state);
                int u7ReputationsBefore = state.Reputations.Count;
                int u7HistoryBefore = state.CommercialHistory.Count;
                int u7TimelineBefore = state.CommercialTimeline.Count;
                CommercialHistorySummary u7Summary =
                    CommercialHistoryService.BuildSummary(state, quietSettlementId);
                List<CommercialEventRecord> u7Timeline =
                    CommercialHistoryService.BuildTimeline(state, quietSettlementId, 5);
                bool u7HasHistory = state.CommercialHistory.Exists(
                    entry => entry != null && entry.settlementId == quietSettlementId);
                bool u7HasTimeline = state.CommercialTimeline.Exists(
                    record => record != null && record.settlementId == quietSettlementId);
                r.Check(
                    state.Reputations.Count == u7ReputationsBefore &&
                    state.FindReputation(quietSettlementId) == null &&
                    state.CommercialHistory.Count == u7HistoryBefore && !u7HasHistory &&
                    state.CommercialTimeline.Count == u7TimelineBefore && !u7HasTimeline &&
                    u7Timeline.Count == 0,
                    "U7 summary and timeline reads mutate no persisted history",
                    $"summary={DescribeSummary(u7Summary)}; reputations " +
                    $"{u7ReputationsBefore}->{state.Reputations.Count}; history " +
                    $"{u7HistoryBefore}->{state.CommercialHistory.Count}; timeline " +
                    $"{u7TimelineBefore}->{state.CommercialTimeline.Count}; " +
                    $"returned ids={DescribeIds(u7Timeline)}");

                // U8: a 55-schema save already contains entries, but its old silver totals cannot
                // be reconstructed. The migration may advance the version, never the totals.
                ResetHistoryFixtures(state);
                state.CommercialHistory.Add(new CommercialHistoryEntry
                {
                    settlementId = oldHistorySettlementId,
                    thingDef = fixtureDef,
                    completedSaleCount = 2,
                    totalTradeValue = 137
                });
                state.CommercialHistory.Add(new CommercialHistoryEntry
                {
                    settlementId = spineOnlySettlementId,
                    thingDef = fixtureDef,
                    completedSaleCount = 3,
                    totalTradeValue = 263
                });
                List<int> u8BeforeTotals = new List<int>();
                foreach (CommercialHistoryEntry entry in state.CommercialHistory)
                {
                    u8BeforeTotals.Add(entry.totalTradeValue);
                }

                if (saveVersionField == null)
                {
                    r.Skip("U8 55-to-current migration preserves trade value", "private saveVersion field is unavailable");
                }
                else
                {
                    saveVersionField.SetValue(state, 55);
                    state.MigrateIfNeeded();
                    List<int> u8AfterTotals = new List<int>();
                    foreach (CommercialHistoryEntry entry in state.CommercialHistory)
                    {
                        u8AfterTotals.Add(entry.totalTradeValue);
                    }

                    bool u8Unchanged = u8BeforeTotals.Count == u8AfterTotals.Count;
                    for (int i = 0; i < u8BeforeTotals.Count && u8Unchanged; i++)
                    {
                        u8Unchanged = u8BeforeTotals[i] == u8AfterTotals[i];
                    }

                    r.Check(
                        u8Unchanged && state.SaveVersion == IntercolonyWorldComponent.CurrentSaveVersion,
                        "U8 55-to-current migration adds no trade value",
                        $"totals before={DescribeInts(u8BeforeTotals)}; after=" +
                        $"{DescribeInts(u8AfterTotals)}; saveVersion=55->{state.SaveVersion}; " +
                        $"current={IntercolonyWorldComponent.CurrentSaveVersion}");
                }
            }
            catch (Exception ex)
            {
                r.Check(false, "Stage 7A commercial-history fixtures completed", ex.ToString());
            }
            finally
            {
                state.CommercialHistory.Clear();
                state.CommercialHistory.AddRange(savedHistory);
                state.Reputations.Clear();
                foreach (KeyValuePair<int, CommercialReputation> saved in savedReputations)
                {
                    state.Reputations[saved.Key] = saved.Value;
                }

                state.CommercialTimeline.Clear();
                state.CommercialTimeline.AddRange(savedTimeline);
                state.Orders.Clear();
                state.Orders.AddRange(savedSalesOrders);
                state.PurchaseOrders.Clear();
                state.PurchaseOrders.AddRange(savedPurchaseOrders);
                state.Contracts.Clear();
                state.Contracts.AddRange(savedContracts);
                state.ProcurementContracts.Clear();
                state.ProcurementContracts.AddRange(savedProcurementContracts);
                state.CommercialTimelineStartTick = savedTimelineStartTick;
                if (saveVersionField != null && savedSaveVersion >= 0)
                {
                    saveVersionField.SetValue(state, savedSaveVersion);
                }

                RestoreHistorySilver(silverMap, savedSilver);
                r.Info(
                    $"commercial history fixtures restored: history={state.CommercialHistory.Count}; " +
                    $"reputations={state.Reputations.Count}; timeline={state.CommercialTimeline.Count}; " +
                    $"sales={state.Orders.Count}; purchases={state.PurchaseOrders.Count}; " +
                    $"contracts={state.Contracts.Count + state.ProcurementContracts.Count}; " +
                    $"saveVersion={state.SaveVersion}.");
            }
        }

        private static void ResetHistoryFixtures(IntercolonyWorldComponent state)
        {
            state.CommercialHistory.Clear();
            state.Reputations.Clear();
            state.CommercialTimeline.Clear();
            state.Orders.Clear();
            state.PurchaseOrders.Clear();
            state.Contracts.Clear();
            state.ProcurementContracts.Clear();
            state.CommercialTimelineStartTick = CommercialTimelineService.NoHistory;
        }

        private static SalesOrder MakeHistorySale(
            int id,
            int settlementId,
            ThingDef thingDef,
            int quantity,
            int paidSilver,
            float unitPrice,
            SalesOrderStatus status)
        {
            return new SalesOrder
            {
                id = id,
                settlementId = settlementId,
                settlementName = "History Testholme",
                factionName = "History faction",
                line = new OrderLine(thingDef, quantity),
                unitPrice = unitPrice,
                acceptedTick = 1,
                deadlineTick = 100000,
                deliveredQuantity = status == SalesOrderStatus.Completed ? quantity : 0,
                paidSilver = paidSilver,
                status = status
            };
        }

        private static PurchaseOrder MakeHistoryPurchase(
            int id,
            int settlementId,
            ThingDef thingDef,
            int quantity,
            int paidSilver,
            float unitPrice,
            PurchaseOrderStatus status)
        {
            return new PurchaseOrder
            {
                id = id,
                settlementId = settlementId,
                settlementName = "History Testholme",
                factionName = "History faction",
                thingDef = thingDef,
                quantity = quantity,
                unitPrice = unitPrice,
                paidSilver = paidSilver,
                orderedTick = 1,
                readyTick = 100000,
                pickupExpiryTick = 100000,
                status = status
            };
        }

        private static string DescribeSummary(CommercialHistorySummary summary)
        {
            return $"id={summary.SettlementId}; standing={summary.CommercialStanding ?? "<none>"}; " +
                   $"hasStanding={summary.HasCommercialStanding}; since={summary.TradingSinceTick}; " +
                   $"hasSince={summary.HasTradingSince}; sinceIsStart={summary.TradingSinceIsTimelineStart}; " +
                   $"coverage={summary.HistoryCoverage}; predates={summary.HistoryPredatesTimeline}; " +
                   $"sales={summary.CompletedSales}/{summary.HasCompletedSales}; " +
                   $"purchases={summary.CompletedPurchases}/{summary.HasCompletedPurchases}; " +
                   $"active={summary.ActiveContracts}/{summary.HasActiveContracts}; " +
                   $"value={summary.TotalKnownTradeValue}/{summary.HasTotalKnownTradeValue}";
        }

        private static string DescribeIds(List<CommercialEventRecord> records)
        {
            List<string> ids = new List<string>();
            foreach (CommercialEventRecord record in records)
            {
                ids.Add(record == null ? "null" : record.id.ToString());
            }

            return "[" + string.Join(",", ids.ToArray()) + "]";
        }

        private static string DescribeTypes(List<CommercialEventRecord> records)
        {
            List<string> types = new List<string>();
            foreach (CommercialEventRecord record in records)
            {
                types.Add(record == null ? "null" : record.type.ToString());
            }

            return "[" + string.Join(",", types.ToArray()) + "]";
        }

        private static string DescribeInts(List<int> values)
        {
            List<string> text = new List<string>();
            foreach (int value in values)
            {
                text.Add(value.ToString());
            }

            return "[" + string.Join(",", text.ToArray()) + "]";
        }

        private static Dictionary<Thing, int> SnapshotHistorySilver(Map map)
        {
            Dictionary<Thing, int> result = new Dictionary<Thing, int>();
            if (map == null || ThingDefOf.Silver == null)
            {
                return result;
            }

            foreach (Thing thing in map.listerThings.ThingsOfDef(ThingDefOf.Silver))
            {
                if (thing != null && !thing.Destroyed)
                {
                    result[thing] = thing.stackCount;
                }
            }

            return result;
        }

        private static void RestoreHistorySilver(
            Map map, Dictionary<Thing, int> savedSilver)
        {
            if (map == null || savedSilver == null || ThingDefOf.Silver == null)
            {
                return;
            }

            List<Thing> current = new List<Thing>(
                map.listerThings.ThingsOfDef(ThingDefOf.Silver));
            foreach (Thing thing in current)
            {
                if (thing == null || thing.Destroyed)
                {
                    continue;
                }

                if (savedSilver.TryGetValue(thing, out int savedCount))
                {
                    thing.stackCount = savedCount;
                }
                else
                {
                    thing.Destroy(DestroyMode.Vanish);
                }
            }

            foreach (KeyValuePair<Thing, int> saved in savedSilver)
            {
                if (saved.Key != null && !saved.Key.Destroyed)
                {
                    saved.Key.stackCount = saved.Value;
                }
            }
        }

        private static string Summarize(Results r)
        {
            r.sb.AppendLine();
            r.sb.AppendLine(r.skipped == 0
                ? $"  {r.passed} passed, {r.failed} failed, 0 skipped."
                : $"  {r.passed} passed, {r.failed} failed, {r.skipped} SKIPPED — not a clean run.");
            return r.sb.ToString();
        }
    }
}
