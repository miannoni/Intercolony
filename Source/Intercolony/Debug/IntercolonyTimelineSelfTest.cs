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
                CheckStage7AcceptanceSurface(r, state);
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
            List<CommercialHistoryEntry> savedHistory =
                new List<CommercialHistoryEntry>(state.CommercialHistory);
            Dictionary<int, CommercialReputation> savedReputations =
                new Dictionary<int, CommercialReputation>(state.Reputations);
            List<ProductBrandRecord> savedBrandRecords =
                new List<ProductBrandRecord>(state.ProductBrandRecords);
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

            try
            {
                int bound = CommercialTimelineService.MaxTimelineRecords;
                r.Check(bound > 0, "W1 retention bound is positive", $"bound={bound}");

                // W1: use varied types, settlements, ticks and IDs. Exact retained identity is
                // checked against this fixture's written records, not against a constant-derived
                // detail string, so middle/type pruning cannot pass by accident.
                ResetRetentionFixtures(state);
                List<CommercialEventRecord> w1Written = new List<CommercialEventRecord>();
                CommercialEventType[] w1Types =
                    (CommercialEventType[])Enum.GetValues(typeof(CommercialEventType));
                int w1Total = bound + 37;
                for (int i = 0; i < w1Total; i++)
                {
                    CommercialEventRecord record = new CommercialEventRecord(
                        id: 7310000 + i,
                        tick: 73100000 + i,
                        settlementId: 731100 + (i % 7),
                        type: w1Types[i % w1Types.Length],
                        settlementName: $"W1 settlement {i % 7}",
                        compactDetail: $"W1 written {i}");
                    state.CommercialTimeline.Add(record);
                    w1Written.Add(record);
                }

                int w1PrePruneCount = state.CommercialTimeline.Count;
                int w1Removed = CommercialTimelineService.Prune(state);
                int w1RetainedOldestId = state.CommercialTimeline.Count == 0 ||
                                         state.CommercialTimeline[0] == null
                    ? -1
                    : state.CommercialTimeline[0].id;
                int w1RetainedNewestId = state.CommercialTimeline.Count == 0 ||
                                         state.CommercialTimeline[state.CommercialTimeline.Count - 1] == null
                    ? -1
                    : state.CommercialTimeline[state.CommercialTimeline.Count - 1].id;
                int w1ExpectedFirstId = w1Written[w1Written.Count - bound].id;
                int w1ExpectedLastId = w1Written[w1Written.Count - 1].id;
                HashSet<int> w1ExpectedIds = new HashSet<int>();
                for (int i = w1Written.Count - bound; i < w1Written.Count; i++)
                {
                    w1ExpectedIds.Add(w1Written[i].id);
                }

                HashSet<int> w1ActualIds = new HashSet<int>();
                foreach (CommercialEventRecord record in state.CommercialTimeline)
                {
                    if (record != null)
                    {
                        w1ActualIds.Add(record.id);
                    }
                }

                int w1MissingId = -1;
                foreach (int expectedId in w1ExpectedIds)
                {
                    if (!w1ActualIds.Contains(expectedId))
                    {
                        w1MissingId = expectedId;
                        break;
                    }
                }

                int w1UnexpectedId = -1;
                foreach (int actualId in w1ActualIds)
                {
                    if (!w1ExpectedIds.Contains(actualId))
                    {
                        w1UnexpectedId = actualId;
                        break;
                    }
                }

                r.Check(
                    w1PrePruneCount > bound && state.CommercialTimeline.Count == bound,
                    "W1 pruning enforces the bound",
                    $"before={w1PrePruneCount}; removed={w1Removed}; after=" +
                    $"{state.CommercialTimeline.Count}; bound={bound}; " +
                    $"retainedOldestId={w1RetainedOldestId}; retainedNewestId={w1RetainedNewestId}");
                r.Check(
                    w1ActualIds.Count == w1ExpectedIds.Count && w1MissingId < 0 &&
                    w1UnexpectedId < 0 &&
                    !state.CommercialTimeline.Exists(e => e != null && e.id == w1Written[0].id) &&
                    state.CommercialTimeline.Exists(e => e != null && e.id == w1Written[w1Written.Count - 1].id),
                    "W1 pruning retains exactly the newest fixture records",
                    $"writtenOldestId={w1Written[0].id}; writtenNewestId=" +
                    $"{w1Written[w1Written.Count - 1].id}; expectedFirstRetainedId={w1ExpectedFirstId}; " +
                    $"expectedLastRetainedId={w1ExpectedLastId}; retainedOldestId={w1RetainedOldestId}; " +
                    $"retainedNewestId={w1RetainedNewestId}; missingId={w1MissingId}; " +
                    $"unexpectedId={w1UnexpectedId}; retainedCount={state.CommercialTimeline.Count}; " +
                    $"bound={bound}");

                // W2: the precondition is an exactly bounded, known-order fixture. The second
                // prune must remove zero records and preserve object identity and order.
                ResetRetentionFixtures(state);
                List<CommercialEventRecord> w2Written = new List<CommercialEventRecord>();
                for (int i = 0; i < bound; i++)
                {
                    CommercialEventRecord record = new CommercialEventRecord(
                        id: 7320000 + i,
                        tick: 73200000 + i,
                        settlementId: 732100,
                        type: CommercialEventType.SaleCompleted,
                        compactDetail: $"W2 written {i}");
                    state.CommercialTimeline.Add(record);
                    w2Written.Add(record);
                }

                List<CommercialEventRecord> w2Before =
                    new List<CommercialEventRecord>(state.CommercialTimeline);
                r.Check(
                    w2Before.Count == bound && ReferenceEquals(w2Before[0], w2Written[0]) &&
                    ReferenceEquals(w2Before[w2Before.Count - 1], w2Written[w2Written.Count - 1]),
                    "W2 idempotence fixture is already bounded and anchored",
                    $"count={w2Before.Count}; bound={bound}; firstId={w2Before[0].id}; " +
                    $"lastId={w2Before[w2Before.Count - 1].id}");
                int w2Removed = CommercialTimelineService.Prune(state);
                bool w2Same = state.CommercialTimeline.Count == w2Before.Count;
                for (int i = 0; i < w2Before.Count && w2Same; i++)
                {
                    w2Same = ReferenceEquals(state.CommercialTimeline[i], w2Before[i]);
                }

                r.Check(
                    w2Before.Count == bound && w2Removed == 0 && w2Same,
                    "W2 pruning is idempotent",
                    $"before={w2Before.Count}; after={state.CommercialTimeline.Count}; " +
                    $"bound={bound}; removed={w2Removed}; beforeFirstId={w2Before[0].id}; " +
                    $"afterFirstId={state.CommercialTimeline[0].id}; beforeLastId=" +
                    $"{w2Before[w2Before.Count - 1].id}; afterLastId=" +
                    $"{state.CommercialTimeline[state.CommercialTimeline.Count - 1].id}");

                // W3: all values below are established independently of the detailed timeline.
                // The old target detail intentionally disagrees with the durable aggregates and is
                // pruned, so a read model that replays the timeline goes red.
                ResetRetentionFixtures(state);
                ThingDef w3Def = ThingDefOf.Steel ?? ThingDefOf.Silver;
                if (w3Def == null)
                {
                    r.Skip(
                        "W3 pruning preserves authoritative commercial state",
                        "ThingDefOf.Steel and ThingDefOf.Silver are unavailable for the brand/order fixture");
                }
                else
                {
                    const int w3SettlementId = 733100;
                    const int w3ExpectedCompletedSales = 7;
                    const int w3ExpectedQuantity = 987;
                    const int w3ExpectedTradeValue = 321;
                    CommercialHistoryEntry w3Entry = new CommercialHistoryEntry
                    {
                        settlementId = w3SettlementId,
                        thingDef = w3Def,
                        completedSaleCount = w3ExpectedCompletedSales,
                        totalQuantitySupplied = w3ExpectedQuantity,
                        totalTradeValue = w3ExpectedTradeValue
                    };
                    state.CommercialHistory.Add(w3Entry);

                    CommercialReputation w3Reputation = new CommercialReputation(
                        w3SettlementId, "W3 Testholme", "W3 faction");
                    w3Reputation.purchasesCompleted = 3;
                    state.Reputations[w3SettlementId] = w3Reputation;

                    ProductBrandRecord w3Brand = new ProductBrandRecord(
                        w3Def, directScore: 37f, evidenceWeight: 1000f, unitsDelivered: 123);
                    state.ProductBrandRecords.Add(w3Brand);

                    state.Contracts.Add(new RecurringContract
                    {
                        id = 733110,
                        settlementId = w3SettlementId,
                        settlementName = "W3 Testholme",
                        thingDef = w3Def,
                        quantityPerCycle = 10,
                        totalCycles = 2,
                        status = ContractStatus.Active
                    });
                    state.ProcurementContracts.Add(new ProcurementContract
                    {
                        id = 733111,
                        settlementId = w3SettlementId,
                        settlementName = "W3 Testholme",
                        thingDef = w3Def,
                        quantityPerCycle = 10,
                        totalCycles = 2,
                        status = ProcurementContractStatus.Active
                    });
                    state.Orders.Add(MakeHistorySale(
                        733120, w3SettlementId, w3Def, 10, 11, 1.1f,
                        SalesOrderStatus.Accepted));
                    state.PurchaseOrders.Add(MakeHistoryPurchase(
                        733121, w3SettlementId, w3Def, 10, 13, 1.3f,
                        PurchaseOrderStatus.Confirmed));

                    state.CommercialTimelineStartTick = 73300000;
                    int w3TargetDetailId = 733130;
                    state.CommercialTimeline.Add(new CommercialEventRecord(
                        w3TargetDetailId, 73300001, w3SettlementId,
                        CommercialEventType.SaleCompleted, "W3 Testholme",
                        silverAmount: 9999, quantity: 999, compactDetail: "W3 old detail"));
                    int w3Total = bound + 41;
                    for (int i = 0; i < w3Total; i++)
                    {
                        state.CommercialTimeline.Add(new CommercialEventRecord(
                            7332000 + i, 73301000 + i, 733200 + (i % 3),
                            CommercialEventType.PurchaseCompleted,
                            compactDetail: $"W3 bulk {i}"));
                    }

                    CommercialHistorySummary w3BeforeSummary =
                        CommercialHistoryService.BuildSummary(state, w3SettlementId);
                    float w3BeforeReputation = w3Reputation.Score;
                    float w3BeforeBrand = EffectiveBrandService.GetEffectiveBrand(state, w3Def);
                    int w3BeforeActiveContracts = CountActiveContracts(state);
                    int w3BeforeOpenOrders = CountOpenOrders(state);
                    int w3BeforeRawSales = w3Entry.completedSaleCount;
                    int w3BeforeRawQuantity = w3Entry.totalQuantitySupplied;
                    int w3BeforeRawTradeValue = w3Entry.totalTradeValue;

                    r.Check(
                        w3BeforeSummary.CompletedSales == w3ExpectedCompletedSales &&
                        w3BeforeSummary.TotalKnownTradeValue == w3ExpectedTradeValue &&
                        w3BeforeReputation == CommercialReputation.StartingScore &&
                        Mathf.Approximately(w3BeforeBrand, w3Brand.directScore) &&
                        w3BeforeActiveContracts == 2 && w3BeforeOpenOrders == 2 &&
                        w3BeforeRawSales == w3ExpectedCompletedSales &&
                        w3BeforeRawQuantity == w3ExpectedQuantity &&
                        w3BeforeRawTradeValue == w3ExpectedTradeValue,
                        "W3 authoritative fixture is anchored to known values",
                        $"sales={w3BeforeSummary.CompletedSales}/{w3ExpectedCompletedSales}; " +
                        $"tradeValue={w3BeforeSummary.TotalKnownTradeValue}/{w3ExpectedTradeValue}; " +
                        $"reputation={w3BeforeReputation}/{CommercialReputation.StartingScore}; " +
                        $"brand={w3BeforeBrand:0.###}/{w3Brand.directScore:0.###}; " +
                        $"activeContracts={w3BeforeActiveContracts}/2; openOrders={w3BeforeOpenOrders}/2; " +
                        $"rawQuantity={w3BeforeRawQuantity}/{w3ExpectedQuantity}");

                    CommercialTimelineService.Prune(state);
                    CommercialHistorySummary w3AfterSummary =
                        CommercialHistoryService.BuildSummary(state, w3SettlementId);
                    ProductBrandRecord w3AfterBrandRecord = state.ProductBrandRecords.Find(
                        record => record != null && record.thingDef == w3Def);
                    CommercialHistoryEntry w3AfterEntry = state.CommercialHistory.Find(
                        entry => entry != null && entry.settlementId == w3SettlementId &&
                                 entry.thingDef == w3Def);
                    float w3AfterReputation = w3Reputation.Score;
                    float w3AfterBrand = EffectiveBrandService.GetEffectiveBrand(state, w3Def);
                    int w3AfterActiveContracts = CountActiveContracts(state);
                    int w3AfterOpenOrders = CountOpenOrders(state);
                    int w3AfterRawSales = w3AfterEntry?.completedSaleCount ?? -1;
                    int w3AfterRawQuantity = w3AfterEntry?.totalQuantitySupplied ?? -1;
                    int w3AfterRawTradeValue = w3AfterEntry?.totalTradeValue ?? -1;

                    r.Check(
                        w3AfterSummary.CompletedSales == w3BeforeSummary.CompletedSales &&
                        w3BeforeSummary.CompletedSales == w3ExpectedCompletedSales,
                        "W3 completed-sales aggregate is unchanged by pruning",
                        $"before={w3BeforeSummary.CompletedSales}; after={w3AfterSummary.CompletedSales}; " +
                        $"expected={w3ExpectedCompletedSales}");
                    r.Check(
                        w3AfterSummary.TotalKnownTradeValue == w3BeforeSummary.TotalKnownTradeValue &&
                        w3BeforeSummary.TotalKnownTradeValue == w3ExpectedTradeValue,
                        "W3 trade-value aggregate is unchanged by pruning",
                        $"before={w3BeforeSummary.TotalKnownTradeValue}; " +
                        $"after={w3AfterSummary.TotalKnownTradeValue}; expected={w3ExpectedTradeValue}");
                    r.Check(
                        w3AfterRawSales == w3BeforeRawSales &&
                        w3BeforeRawSales == w3ExpectedCompletedSales,
                        "W3 raw completed-sale aggregate is unchanged by pruning",
                        $"before={w3BeforeRawSales}; after={w3AfterRawSales}; " +
                        $"expected={w3ExpectedCompletedSales}");
                    r.Check(
                        w3AfterRawQuantity == w3BeforeRawQuantity &&
                        w3BeforeRawQuantity == w3ExpectedQuantity,
                        "W3 raw supplied-quantity aggregate is unchanged by pruning",
                        $"before={w3BeforeRawQuantity}; after={w3AfterRawQuantity}; " +
                        $"expected={w3ExpectedQuantity}");
                    r.Check(
                        w3AfterRawTradeValue == w3BeforeRawTradeValue &&
                        w3BeforeRawTradeValue == w3ExpectedTradeValue,
                        "W3 raw trade-value aggregate is unchanged by pruning",
                        $"before={w3BeforeRawTradeValue}; after={w3AfterRawTradeValue}; " +
                        $"expected={w3ExpectedTradeValue}");
                    r.Check(
                        Mathf.Approximately(w3AfterReputation, w3BeforeReputation) &&
                        Mathf.Approximately(w3BeforeReputation, CommercialReputation.StartingScore),
                        "W3 reputation score is unchanged by pruning",
                        $"before={w3BeforeReputation:0.###}; after={w3AfterReputation:0.###}; " +
                        $"expected={CommercialReputation.StartingScore:0.###}");
                    r.Check(
                        w3AfterBrandRecord != null &&
                        Mathf.Approximately(w3AfterBrand, w3BeforeBrand) &&
                        Mathf.Approximately(w3BeforeBrand, w3Brand.directScore),
                        "W3 brand score is unchanged by pruning",
                        $"before={w3BeforeBrand:0.###}; after={w3AfterBrand:0.###}; " +
                        $"expected={w3Brand.directScore:0.###}; recordPresent={w3AfterBrandRecord != null}");
                    r.Check(
                        w3AfterActiveContracts == w3BeforeActiveContracts &&
                        w3BeforeActiveContracts == 2,
                        "W3 active-contract count is unchanged by pruning",
                        $"before={w3BeforeActiveContracts}; after={w3AfterActiveContracts}; expected=2");
                    r.Check(
                        w3AfterOpenOrders == w3BeforeOpenOrders && w3BeforeOpenOrders == 2,
                        "W3 open-order count is unchanged by pruning",
                        $"before={w3BeforeOpenOrders}; after={w3AfterOpenOrders}; expected=2");
                    r.Check(
                        !state.CommercialTimeline.Exists(e => e != null && e.id == w3TargetDetailId),
                        "W3 pruning removes the contradictory target detail",
                        $"targetDetailId={w3TargetDetailId}; retainedCount={state.CommercialTimeline.Count}; " +
                        $"bound={bound}");
                }

                // W4: make the public contract-eligibility answer true from durable history, then
                // remove two matching detail events. A timeline-backed eligibility regression goes
                // false after the prune even though the aggregate remains intact.
                ResetRetentionFixtures(state);
                Settlement w4Settlement = FindContractFixtureSettlement(state, out string w4SettlementReason);
                ThingDef w4Def = FindContractFixtureThingDef(out string w4ThingReason);
                if (w4Settlement == null || w4Def == null)
                {
                    r.Skip(
                        "W4 contract eligibility survives pruning",
                        $"settlement={w4SettlementReason}; product={w4ThingReason}");
                }
                else
                {
                    CommercialReputation w4Reputation = new CommercialReputation(
                        w4Settlement.ID, w4Settlement.Label, w4Settlement.Faction?.Name ?? "W4 faction");
                    w4Reputation.Adjust(20f);
                    state.Reputations[w4Settlement.ID] = w4Reputation;
                    state.CommercialHistory.Add(new CommercialHistoryEntry
                    {
                        settlementId = w4Settlement.ID,
                        thingDef = w4Def,
                        completedSaleCount = ContractService.MinimumCompletedOrdersForAgreement,
                        totalQuantitySupplied = ContractService.MinimumCompletedOrdersForAgreement * 10,
                        totalTradeValue = 222
                    });

                    int w4FirstDetailId = 7341000;
                    state.CommercialTimelineStartTick = 73400000;
                    for (int i = 0; i < ContractService.MinimumCompletedOrdersForAgreement; i++)
                    {
                        state.CommercialTimeline.Add(new CommercialEventRecord(
                            w4FirstDetailId + i, 73400001 + i, w4Settlement.ID,
                            CommercialEventType.SaleCompleted, w4Settlement.Label,
                            thingDef: w4Def, quantity: 10, silverAmount: 111,
                            compactDetail: $"W4 old eligibility detail {i}"));
                    }

                    ContractTerms w4BeforeTerms = ContractService.PreviewContractTerms(
                        state, w4Settlement, w4Def, ContractService.MinimumQuantityPerCycle);
                    bool w4EligibleBefore = w4BeforeTerms != null;
                    r.Check(
                        w4EligibleBefore && w4Reputation.Score >= ContractService.MinimumReputation &&
                        state.CommercialHistory[0].completedSaleCount >=
                        ContractService.MinimumCompletedOrdersForAgreement,
                        "W4 eligibility fixture is known-good before pruning",
                        $"eligibleBefore={w4EligibleBefore}; reputation={w4Reputation.Score:0.###}; " +
                        $"requiredReputation={ContractService.MinimumReputation:0.###}; " +
                        $"aggregateSales={state.CommercialHistory[0].completedSaleCount}; " +
                        $"requiredSales={ContractService.MinimumCompletedOrdersForAgreement}; " +
                        $"settlementId={w4Settlement.ID}; product={w4Def.defName}");

                    if (w4EligibleBefore)
                    {
                        int w4Total = bound + 29;
                        for (int i = 0; i < w4Total; i++)
                        {
                            state.CommercialTimeline.Add(new CommercialEventRecord(
                                7342000 + i, 73401000 + i, 734200,
                                CommercialEventType.PurchaseCompleted,
                                compactDetail: $"W4 bulk {i}"));
                        }

                        CommercialTimelineService.Prune(state);
                        ContractTerms w4AfterTerms = ContractService.PreviewContractTerms(
                            state, w4Settlement, w4Def, ContractService.MinimumQuantityPerCycle);
                        bool w4EligibleAfter = w4AfterTerms != null;
                        int w4RemainingDetails = 0;
                        foreach (CommercialEventRecord record in state.CommercialTimeline)
                        {
                            if (record != null && record.settlementId == w4Settlement.ID)
                            {
                                w4RemainingDetails++;
                            }
                        }

                        r.Check(
                            w4EligibleAfter == w4EligibleBefore && w4EligibleAfter &&
                            w4RemainingDetails == 0,
                            "W4 contract eligibility survives hard pruning",
                            $"eligibleBefore={w4EligibleBefore}; eligibleAfter={w4EligibleAfter}; " +
                            $"remainingTargetDetails={w4RemainingDetails}; " +
                            $"firstDetailId={w4FirstDetailId}; bound={bound}; " +
                            $"retainedCount={state.CommercialTimeline.Count}");
                    }
                }

                // W5: Record() is the actual append boundary. Sample RecordCount after every
                // append so a refresh-only implementation cannot hide an oversized save window.
                ResetRetentionFixtures(state);
                int w5Total = bound + 23;
                int w5MaximumObserved = 0;
                int w5FirstOverflowIndex = -1;
                for (int i = 0; i < w5Total; i++)
                {
                    CommercialTimelineService.Record(
                        state, CommercialEventType.SaleCompleted, 735100, "W5 Testholme",
                        relatedEntityId: 7350000 + i, compactDetail: $"W5 append {i}");
                    int countAtAppend = CommercialTimelineService.RecordCount(state);
                    if (countAtAppend > w5MaximumObserved)
                    {
                        w5MaximumObserved = countAtAppend;
                    }

                    if (countAtAppend > bound && w5FirstOverflowIndex < 0)
                    {
                        w5FirstOverflowIndex = i;
                    }
                }

                r.Check(
                    w5Total > bound && w5FirstOverflowIndex < 0 &&
                    w5MaximumObserved <= bound,
                    "W5 prune-on-append never exposes an oversized timeline",
                    $"appended={w5Total}; bound={bound}; maxObserved={w5MaximumObserved}; " +
                    $"firstOverflowIndex={w5FirstOverflowIndex}; finalCount=" +
                    $"{CommercialTimelineService.RecordCount(state)}");

                // W6: durable history remains after the target's only meaningful detail is
                // pruned. The spine tick is a lower-bound boundary, not a fabricated first trade.
                ResetRetentionFixtures(state);
                const int w6Boundary = 73600000;
                const int w6SettlementId = 736100;
                const int w6TargetDetailId = 7361000;
                state.CommercialTimelineStartTick = w6Boundary;
                state.CommercialHistory.Add(new CommercialHistoryEntry
                {
                    settlementId = w6SettlementId,
                    completedSaleCount = 1,
                    totalTradeValue = 88
                });
                state.CommercialTimeline.Add(new CommercialEventRecord(
                    w6TargetDetailId, w6Boundary + 1, w6SettlementId,
                    CommercialEventType.SaleCompleted, "W6 Testholme",
                    compactDetail: "W6 only retained detail"));
                int w6Total = bound + 31;
                for (int i = 0; i < w6Total; i++)
                {
                    state.CommercialTimeline.Add(new CommercialEventRecord(
                        7362000 + i, w6Boundary + 1000 + i, 736200,
                        CommercialEventType.ContractCompleted,
                        compactDetail: $"W6 bulk {i}"));
                }

                CommercialTimelineService.Prune(state);
                CommercialHistorySummary w6Summary =
                    CommercialHistoryService.BuildSummary(state, w6SettlementId);
                bool w6TargetDetailGone = !state.CommercialTimeline.Exists(
                    record => record != null && record.id == w6TargetDetailId);
                r.Check(
                    w6TargetDetailGone &&
                    w6Summary.HistoryCoverage == CommercialHistoryCoverage.AggregateOnly &&
                    w6Summary.HistoryPredatesTimeline && w6Summary.HasTradingSince &&
                    w6Summary.TradingSinceTick == w6Boundary &&
                    w6Summary.TradingSinceIsTimelineStart,
                    "W6 read model marks pruned detail as an aggregate-only boundary",
                    $"targetDetailId={w6TargetDetailId}; detailGone={w6TargetDetailGone}; " +
                    $"coverage={w6Summary.HistoryCoverage}; predates={w6Summary.HistoryPredatesTimeline}; " +
                    $"since={w6Summary.TradingSinceTick}; boundary={w6Boundary}; " +
                    $"hasSince={w6Summary.HasTradingSince}; sinceIsStart=" +
                    $"{w6Summary.TradingSinceIsTimelineStart}; retainedCount=" +
                    $"{state.CommercialTimeline.Count}; bound={bound}");

                // W7: compare the profiling accessor with the actual list both before and after
                // an explicit prune. The pre-prune list is intentionally oversized.
                ResetRetentionFixtures(state);
                int w7Total = bound + 11;
                for (int i = 0; i < w7Total; i++)
                {
                    state.CommercialTimeline.Add(new CommercialEventRecord(
                        7372000 + i, 73700000 + i, 737200,
                        CommercialEventType.SaleCompleted,
                        compactDetail: $"W7 written {i}"));
                }

                int w7ActualBefore = state.CommercialTimeline.Count;
                int w7ReportedBefore = CommercialTimelineService.RecordCount(state);
                r.Check(
                    w7ReportedBefore == w7ActualBefore,
                    "W7 RecordCount reports the real pre-pruning count",
                    $"reportedBefore={w7ReportedBefore}; actualBefore={w7ActualBefore}; bound={bound}");
                CommercialTimelineService.Prune(state);
                int w7ActualAfter = state.CommercialTimeline.Count;
                int w7ReportedAfter = CommercialTimelineService.RecordCount(state);
                r.Check(
                    w7ReportedAfter == w7ActualAfter,
                    "W7 RecordCount reports the real post-pruning count",
                    $"reportedAfter={w7ReportedAfter}; actualAfter={w7ActualAfter}; " +
                    $"bound={bound}");
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

                state.ProductBrandRecords.Clear();
                state.ProductBrandRecords.AddRange(savedBrandRecords);
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
                r.Info(
                    $"retention fixtures restored: history={state.CommercialHistory.Count}; " +
                    $"reputations={state.Reputations.Count}; brands={state.ProductBrandRecords.Count}; " +
                    $"timeline={state.CommercialTimeline.Count}; sales={state.Orders.Count}; " +
                    $"purchases={state.PurchaseOrders.Count}; contracts=" +
                    $"{state.Contracts.Count + state.ProcurementContracts.Count}.");
            }
        }

        private static void ResetRetentionFixtures(IntercolonyWorldComponent state)
        {
            state.CommercialHistory.Clear();
            state.Reputations.Clear();
            state.ProductBrandRecords.Clear();
            state.CommercialTimeline.Clear();
            state.Orders.Clear();
            state.PurchaseOrders.Clear();
            state.Contracts.Clear();
            state.ProcurementContracts.Clear();
            state.CommercialTimelineStartTick = CommercialTimelineService.NoHistory;
        }

        private static int CountActiveContracts(IntercolonyWorldComponent state)
        {
            int count = 0;
            foreach (RecurringContract contract in state.Contracts)
            {
                if (contract != null &&
                    (contract.IsActive || contract.status == ContractStatus.Suspended))
                {
                    count++;
                }
            }

            foreach (ProcurementContract contract in state.ProcurementContracts)
            {
                if (contract != null &&
                    (contract.status == ProcurementContractStatus.Active ||
                     contract.status == ProcurementContractStatus.Suspended))
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountOpenOrders(IntercolonyWorldComponent state)
        {
            int count = 0;
            foreach (SalesOrder order in state.Orders)
            {
                if (order != null && order.IsOpen)
                {
                    count++;
                }
            }

            foreach (PurchaseOrder order in state.PurchaseOrders)
            {
                if (order != null && order.IsOpen)
                {
                    count++;
                }
            }

            return count;
        }

        private static Settlement FindContractFixtureSettlement(
            IntercolonyWorldComponent state, out string reason)
        {
            if (Find.WorldObjects?.Settlements == null)
            {
                reason = "world settlements are unavailable";
                return null;
            }

            foreach (Settlement settlement in Find.WorldObjects.Settlements)
            {
                if (settlement == null || !SettlementProfileGenerator.IsEligible(settlement))
                {
                    continue;
                }

                if (!IntercolonyMarketAccess.IsAccessible(settlement, out _))
                {
                    continue;
                }

                if (state.GetProfile(settlement) != null)
                {
                    reason = null;
                    return settlement;
                }
            }

            reason = "no eligible, accessible settlement with an economic profile";
            return null;
        }

        private static ThingDef FindContractFixtureThingDef(out string reason)
        {
            List<ThingDef> candidates = IntercolonyProductClassifier.TradableDefs;
            if (candidates != null)
            {
                foreach (ThingDef def in candidates)
                {
                    if (def != null && def.stackLimit > 1 && def.category == ThingCategory.Item &&
                        IntercolonyProductClassifier.IsFungibleTradeItem(def))
                    {
                        reason = null;
                        return def;
                    }
                }
            }

            reason = "no registered fungible stackable trade item";
            return null;
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
            const int relationsSummarySettlementId = 710801;
            const int relationsNeverSettlementId = 710802;
            const int relationsAggregateSettlementId = 710803;
            const int relationsRetainedSettlementId = 710804;
            const int relationsSpineSettlementId = 710805;
            const int relationsUnsupportedSettlementId = 710806;
            const int relationsTimelineSettlementId = 710807;
            const int relationsFilterSettlementId = 710808;
            const int relationsMutationSettlementId = 710809;

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

                // --- Stage 7C Relations read model -----------------------------------------
                // Y1: durable aggregates deliberately disagree with the retained detail. The
                // row must present the summary service's six labelled values, not recalculate
                // them from orders or timeline rows.
                ResetHistoryFixtures(state);
                const int y1ExpectedSales = 5;
                const int y1ExpectedPurchases = 7;
                const int y1ExpectedActiveContracts = 2;
                const int y1ExpectedTradeValue = 600;
                const int y1FirstMeaningfulTick = 7108010;
                state.CommercialHistory.Add(new CommercialHistoryEntry
                {
                    settlementId = relationsSummarySettlementId,
                    thingDef = fixtureDef,
                    completedSaleCount = 3,
                    totalTradeValue = 214
                });
                state.CommercialHistory.Add(new CommercialHistoryEntry
                {
                    settlementId = relationsSummarySettlementId,
                    thingDef = fixtureDef,
                    completedSaleCount = 2,
                    totalTradeValue = 386
                });
                CommercialReputation y1Reputation = new CommercialReputation(
                    relationsSummarySettlementId, "Y1 Summary Testholme", "Y1 faction");
                y1Reputation.purchasesCompleted = y1ExpectedPurchases;
                state.Reputations[relationsSummarySettlementId] = y1Reputation;
                state.Contracts.Add(new RecurringContract
                {
                    id = 710811,
                    settlementId = relationsSummarySettlementId,
                    settlementName = "Y1 Summary Testholme",
                    quantityPerCycle = 1,
                    totalCycles = 1,
                    unitPrice = 1f,
                    status = ContractStatus.Active
                });
                state.ProcurementContracts.Add(new ProcurementContract
                {
                    id = 710812,
                    settlementId = relationsSummarySettlementId,
                    settlementName = "Y1 Summary Testholme",
                    quantityPerCycle = 1,
                    totalCycles = 1,
                    unitPrice = 1f,
                    cadenceDays = 1,
                    status = ProcurementContractStatus.Suspended
                });
                state.CommercialTimelineStartTick = 7108000;
                state.CommercialTimeline.Add(new CommercialEventRecord(
                    710813, y1FirstMeaningfulTick, relationsSummarySettlementId,
                    CommercialEventType.SaleCompleted, "Y1 Summary Testholme",
                    compactDetail: "Y1 detail sale"));
                state.CommercialTimeline.Add(new CommercialEventRecord(
                    710814, 7108020, relationsSummarySettlementId,
                    CommercialEventType.PurchaseCompleted, "Y1 Summary Testholme",
                    compactDetail: "Y1 detail purchase"));

                CommercialHistorySummary y1Summary =
                    CommercialHistoryService.BuildSummary(state, relationsSummarySettlementId);
                CommercialHistoryRelationRow y1Row =
                    CommercialHistoryUiService.BuildRow(state, relationsSummarySettlementId);
                string y1ExpectedStanding = y1Summary.CommercialStanding;
                string y1ExpectedTradingSince = "Earliest retained event: " +
                    GenDate.DateShortStringAt(
                        GenDate.TickGameToAbs(y1FirstMeaningfulTick), Vector2.zero);
                string y1ExpectedSalesText = y1Summary.CompletedSales.ToString("N0");
                string y1ExpectedPurchasesText = y1Summary.CompletedPurchases.ToString("N0");
                string y1ExpectedContractsText = y1Summary.ActiveContracts.ToString("N0");
                string y1ExpectedTradeValueText =
                    $"{y1Summary.TotalKnownTradeValue:N0} silver";
                string y1PresentedStanding = SummaryRowValue(
                    y1Row.summaryRows, "Commercial standing");
                string y1PresentedTradingSince = SummaryRowValue(
                    y1Row.summaryRows, "Trading since");
                string y1PresentedSales = SummaryRowValue(
                    y1Row.summaryRows, "Completed sales");
                string y1PresentedPurchases = SummaryRowValue(
                    y1Row.summaryRows, "Completed purchases");
                string y1PresentedContracts = SummaryRowValue(
                    y1Row.summaryRows, "Active contracts");
                string y1PresentedTradeValue = SummaryRowValue(
                    y1Row.summaryRows, "Total known trade value");
                bool y1SummaryFixtureKnown =
                    y1Summary.HasCommercialStanding &&
                    y1Summary.HasTradingSince &&
                    y1Summary.TradingSinceTick == y1FirstMeaningfulTick &&
                    y1Summary.HistoryCoverage == CommercialHistoryCoverage.Timeline &&
                    y1Summary.CompletedSales == y1ExpectedSales &&
                    y1Summary.CompletedPurchases == y1ExpectedPurchases &&
                    y1Summary.ActiveContracts == y1ExpectedActiveContracts &&
                    y1Summary.TotalKnownTradeValue == y1ExpectedTradeValue;

                r.Check(
                    y1SummaryFixtureKnown && y1PresentedStanding == y1ExpectedStanding,
                    "Y1 Commercial standing row presents the summary value",
                    $"presented=\"{y1PresentedStanding}\"; summary=\"{y1ExpectedStanding}\"; " +
                    $"summary={DescribeSummary(y1Summary)}");
                r.Check(
                    y1SummaryFixtureKnown && y1PresentedTradingSince == y1ExpectedTradingSince,
                    "Y1 Trading since row presents the summary value",
                    $"presented=\"{y1PresentedTradingSince}\"; " +
                    $"summary=\"{y1ExpectedTradingSince}\"; summary={DescribeSummary(y1Summary)}");
                r.Check(
                    y1SummaryFixtureKnown && y1PresentedSales == y1ExpectedSalesText,
                    "Y1 Completed sales row presents the summary value",
                    $"presented=\"{y1PresentedSales}\"; summary=\"{y1ExpectedSalesText}\"; " +
                    $"summary={DescribeSummary(y1Summary)}");
                r.Check(
                    y1SummaryFixtureKnown && y1PresentedPurchases == y1ExpectedPurchasesText,
                    "Y1 Completed purchases row presents the summary value",
                    $"presented=\"{y1PresentedPurchases}\"; " +
                    $"summary=\"{y1ExpectedPurchasesText}\"; summary={DescribeSummary(y1Summary)}");
                r.Check(
                    y1SummaryFixtureKnown && y1PresentedContracts == y1ExpectedContractsText,
                    "Y1 Active contracts row presents the summary value",
                    $"presented=\"{y1PresentedContracts}\"; " +
                    $"summary=\"{y1ExpectedContractsText}\"; summary={DescribeSummary(y1Summary)}");
                r.Check(
                    y1SummaryFixtureKnown && y1PresentedTradeValue == y1ExpectedTradeValueText,
                    "Y1 Total known trade value row presents the summary value",
                    $"presented=\"{y1PresentedTradeValue}\"; " +
                    $"summary=\"{y1ExpectedTradeValueText}\"; summary={DescribeSummary(y1Summary)}");

                // Y2: these are three different coverage answers, not three spellings of an
                // empty timeline. Pairwise inequality is intentional: collapsing any two cases
                // makes this assertion red even if the collapsed wording is otherwise plausible.
                ResetHistoryFixtures(state);
                const int y2SpineBoundary = 7108200;
                const int y2RetainedTick = 7108210;
                state.CommercialTimelineStartTick = y2SpineBoundary;
                state.CommercialHistory.Add(new CommercialHistoryEntry
                {
                    settlementId = relationsAggregateSettlementId,
                    thingDef = fixtureDef,
                    completedSaleCount = 1,
                    totalTradeValue = 17
                });
                state.CommercialTimeline.Add(new CommercialEventRecord(
                    7108231, y2RetainedTick, relationsRetainedSettlementId,
                    CommercialEventType.SaleCompleted, "Y2 retained Testholme",
                    compactDetail: "Y2 retained detail"));
                CommercialHistoryRelationRow y2NeverRow =
                    CommercialHistoryUiService.BuildRow(state, relationsNeverSettlementId);
                CommercialHistoryRelationRow y2AggregateRow =
                    CommercialHistoryUiService.BuildRow(state, relationsAggregateSettlementId);
                CommercialHistoryRelationRow y2RetainedRow =
                    CommercialHistoryUiService.BuildRow(state, relationsRetainedSettlementId);
                string y2NeverSince = SummaryRowValue(
                    y2NeverRow.summaryRows, "Trading since");
                string y2AggregateSince = SummaryRowValue(
                    y2AggregateRow.summaryRows, "Trading since");
                string y2RetainedSince = SummaryRowValue(
                    y2RetainedRow.summaryRows, "Trading since");
                string y2AggregateExpected = "Detailed history tracked since " +
                    GenDate.DateShortStringAt(
                        GenDate.TickGameToAbs(y2SpineBoundary), Vector2.zero);
                string y2RetainedExpected = "Earliest retained event: " +
                    GenDate.DateShortStringAt(
                        GenDate.TickGameToAbs(y2RetainedTick), Vector2.zero);
                r.Check(
                    y2NeverSince == "No commercial history recorded" &&
                    y2AggregateSince == y2AggregateExpected &&
                    y2RetainedSince == y2RetainedExpected &&
                    y2NeverSince != y2AggregateSince &&
                    y2NeverSince != y2RetainedSince &&
                    y2AggregateSince != y2RetainedSince &&
                    !ContainsIgnoreCase(y2NeverSince, "since"),
                    "Y2 coverage cases have distinct truthful Relations wording",
                    $"never=\"{y2NeverSince}\"; aggregate-only=\"{y2AggregateSince}\"; " +
                    $"retained=\"{y2RetainedSince}\"");

                // Y3: a durable relationship with no retained detail gets the spine lower bound.
                // The label and tooltip must both preserve that distinction from a first trade.
                ResetHistoryFixtures(state);
                const int y3SpineBoundary = 7108300;
                state.CommercialTimelineStartTick = y3SpineBoundary;
                state.CommercialHistory.Add(new CommercialHistoryEntry
                {
                    settlementId = relationsSpineSettlementId,
                    thingDef = fixtureDef,
                    completedSaleCount = 1,
                    totalTradeValue = 29
                });
                CommercialHistoryRelationRow y3Row =
                    CommercialHistoryUiService.BuildRow(state, relationsSpineSettlementId);
                string y3Since = SummaryRowValue(y3Row.summaryRows, "Trading since");
                string y3SinceTooltip = SummaryRowTooltip(y3Row.summaryRows, "Trading since");
                r.Check(
                    ContainsIgnoreCase(y3Since, "Detailed history tracked since") &&
                    !ContainsIgnoreCase(y3Since, "first trade") &&
                    ContainsIgnoreCase(y3SinceTooltip, "not a claim") &&
                    ContainsIgnoreCase(y3SinceTooltip, "record spine"),
                    "Y3 spine boundary is not presented as a first trade",
                    $"presented=\"{y3Since}\"; tooltip=\"{y3SinceTooltip}\"; " +
                    $"boundary={y3SpineBoundary}");

                // Y4: search the actual presented strings for the independent sentinel digits.
                // Equality with an expected empty-state sentence would not catch a stray
                // sentinel in a tooltip or one of the other displayed fields.
                const string y4NoHistoryDigits = "-1";
                const string y4FloatMaxDigits =
                    "34028230000000000000000000000000000000";
                List<string> y4Presented = new List<string>();
                y4Presented.AddRange(PresentedRelationStrings(y2NeverRow));
                y4Presented.AddRange(PresentedRelationStrings(y2AggregateRow));
                y4Presented.AddRange(PresentedRelationStrings(y2RetainedRow));
                bool y4NoSentinel = true;
                foreach (string presented in y4Presented)
                {
                    if (presented != null &&
                        (presented.Contains(y4NoHistoryDigits) ||
                         presented.Contains(y4FloatMaxDigits)))
                    {
                        y4NoSentinel = false;
                        break;
                    }
                }

                r.Check(
                    y4NoSentinel,
                    "Y4 Relations history presentation never formats a none sentinel",
                    $"searched NoHistory digits=\"{y4NoHistoryDigits}\"; " +
                    $"searched float.MaxValue F0 digits=\"{y4FloatMaxDigits}\"; " +
                    $"presented={DescribeStrings(y4Presented)}");

                // Y5: zero is not evidence for a retained trade-value total. The fixture has a
                // durable sales count but its value aggregate is the explicit unknown sentinel.
                ResetHistoryFixtures(state);
                state.CommercialHistory.Add(new CommercialHistoryEntry
                {
                    settlementId = relationsUnsupportedSettlementId,
                    thingDef = fixtureDef,
                    completedSaleCount = 2,
                    totalTradeValue = 0
                });
                CommercialHistorySummary y5Summary =
                    CommercialHistoryService.BuildSummary(state, relationsUnsupportedSettlementId);
                CommercialHistoryRelationRow y5Row =
                    CommercialHistoryUiService.BuildRow(state, relationsUnsupportedSettlementId);
                string y5TradeValue = SummaryRowValue(
                    y5Row.summaryRows, "Total known trade value");
                r.Check(
                    !y5Summary.HasTotalKnownTradeValue &&
                    y5TradeValue == "No retained trade-value total" &&
                    y5TradeValue != "0",
                    "Y5 unsupported trade value is presented honestly, not as zero",
                    $"supported={y5Summary.HasTotalKnownTradeValue}; " +
                    $"presented=\"{y5TradeValue}\"; expected=\"No retained trade-value total\"; " +
                    $"summary={DescribeSummary(y5Summary)}");

                // Y6: exercise the actual Relations row with a thousand retained records. The
                // expected twelve is an independent screenful contract, not the implementation
                // constant, so changing the UI to request everything makes this red.
                ResetHistoryFixtures(state);
                const int y6RecordCount = 1000;
                const int y6RequestedRows = 12;
                const int y6FirstId = 7108400;
                for (int i = 0; i < y6RecordCount; i++)
                {
                    int id = y6FirstId + i;
                    state.CommercialTimeline.Add(new CommercialEventRecord(
                        id, 7108400 + i, relationsTimelineSettlementId,
                        CommercialEventType.SaleCompleted, "Y6 Timeline Testholme",
                        compactDetail: $"Y6-event-{id}"));
                }

                CommercialHistoryRelationRow y6Row =
                    CommercialHistoryUiService.BuildRow(state, relationsTimelineSettlementId);
                List<string> y6DisplayedLabels = RelationTimelineLabels(y6Row.timelineRows);
                List<string> y6ExpectedMarkers = new List<string>();
                for (int i = y6RecordCount - 1;
                     i >= y6RecordCount - y6RequestedRows;
                     i--)
                {
                    y6ExpectedMarkers.Add($"Y6-event-{y6FirstId + i}");
                }
                bool y6NewestFirst = y6DisplayedLabels.Count == y6ExpectedMarkers.Count;
                for (int i = 0; i < y6ExpectedMarkers.Count && y6NewestFirst; i++)
                {
                    y6NewestFirst = ContainsIgnoreCase(
                        y6DisplayedLabels[i], y6ExpectedMarkers[i]);
                }
                r.Check(
                    y6Row.timelineRows.Count <= y6RequestedRows &&
                    y6Row.timelineRows.Count == y6RequestedRows,
                    "Y6 Relations timeline is bounded to the requested count",
                    $"requested max={y6RequestedRows}; returned count={y6Row.timelineRows.Count}; " +
                    $"ordered labels={DescribeStrings(y6DisplayedLabels)}");
                r.Check(
                    y6NewestFirst,
                    "Y6 Relations timeline is ordered newest first",
                    $"expected markers={DescribeStrings(y6ExpectedMarkers)}; " +
                    $"ordered labels={DescribeStrings(y6DisplayedLabels)}");

                // Y7: use a small mixed fixture so dropping the settlement filter cannot hide
                // behind the twelve-row cap.
                ResetHistoryFixtures(state);
                const int y7OldTargetId = 7108501;
                const int y7NewTargetId = 7108502;
                const int y7OtherId = 7108503;
                state.CommercialTimeline.Add(new CommercialEventRecord(
                    y7OldTargetId, 7108501, relationsFilterSettlementId,
                    CommercialEventType.SaleCompleted, "Y7 target Testholme",
                    compactDetail: $"Y7-target-{y7OldTargetId}"));
                state.CommercialTimeline.Add(new CommercialEventRecord(
                    y7NewTargetId, 7108503, relationsFilterSettlementId,
                    CommercialEventType.PurchaseCompleted, "Y7 target Testholme",
                    compactDetail: $"Y7-target-{y7NewTargetId}"));
                state.CommercialTimeline.Add(new CommercialEventRecord(
                    y7OtherId, 7108504, relationsNeverSettlementId,
                    CommercialEventType.SaleCompleted, "Y7 other Testholme",
                    compactDetail: $"Y7-other-{y7OtherId}"));
                CommercialHistoryRelationRow y7Row =
                    CommercialHistoryUiService.BuildRow(state, relationsFilterSettlementId);
                List<string> y7DisplayedLabels = RelationTimelineLabels(y7Row.timelineRows);
                List<string> y7ExpectedMarkers = new List<string>
                {
                    $"Y7-target-{y7OldTargetId}",
                    $"Y7-target-{y7NewTargetId}"
                };
                bool y7OnlyTarget = y7DisplayedLabels.Count == y7ExpectedMarkers.Count;
                foreach (string expectedMarker in y7ExpectedMarkers)
                {
                    bool found = false;
                    foreach (string label in y7DisplayedLabels)
                    {
                        if (ContainsIgnoreCase(label, expectedMarker))
                        {
                            found = true;
                            break;
                        }
                    }

                    y7OnlyTarget = y7OnlyTarget && found;
                }

                foreach (string label in y7DisplayedLabels)
                {
                    y7OnlyTarget = y7OnlyTarget &&
                        ContainsIgnoreCase(label, "Y7-target-") &&
                        !ContainsIgnoreCase(label, "Y7-other-");
                }
                r.Check(
                    y7OnlyTarget,
                    "Y7 Relations timeline contains only that settlement's events",
                    $"requested settlement={relationsFilterSettlementId}; " +
                    $"expected markers={DescribeStrings(y7ExpectedMarkers)}; " +
                    $"labels={DescribeStrings(y7DisplayedLabels)}");

                // Y8: call both the direct row builder and the collection builder against an ID
                // absent from every persisted source, then verify that presentation created no
                // reputation, aggregate, timeline, order, or contract record.
                ResetHistoryFixtures(state);
                int y8ReputationsBefore = state.Reputations.Count;
                int y8HistoryBefore = state.CommercialHistory.Count;
                int y8TimelineBefore = state.CommercialTimeline.Count;
                int y8SalesBefore = state.Orders.Count;
                int y8PurchasesBefore = state.PurchaseOrders.Count;
                int y8ContractsBefore = state.Contracts.Count;
                int y8ProcurementBefore = state.ProcurementContracts.Count;
                List<CommercialHistoryRelationRow> y8Rows =
                    CommercialHistoryUiService.BuildRows(state);
                CommercialHistoryRelationRow y8DirectRow =
                    CommercialHistoryUiService.BuildRow(state, relationsMutationSettlementId);
                bool y8HasHistory = state.CommercialHistory.Exists(
                    entry => entry != null && entry.settlementId == relationsMutationSettlementId);
                bool y8HasTimeline = state.CommercialTimeline.Exists(
                    record => record != null && record.settlementId == relationsMutationSettlementId);
                r.Check(
                    y8Rows.Count == 0 && y8DirectRow.summaryRows.Count == 6 &&
                    state.Reputations.Count == y8ReputationsBefore &&
                    state.FindReputation(relationsMutationSettlementId) == null &&
                    state.CommercialHistory.Count == y8HistoryBefore && !y8HasHistory &&
                    state.CommercialTimeline.Count == y8TimelineBefore && !y8HasTimeline &&
                    state.Orders.Count == y8SalesBefore &&
                    state.PurchaseOrders.Count == y8PurchasesBefore &&
                    state.Contracts.Count == y8ContractsBefore &&
                    state.ProcurementContracts.Count == y8ProcurementBefore,
                    "Y8 Relations read model mutates no persisted history",
                    $"rows={y8Rows.Count}; reputations {y8ReputationsBefore}->{state.Reputations.Count}; " +
                    $"history {y8HistoryBefore}->{state.CommercialHistory.Count}; " +
                    $"timeline {y8TimelineBefore}->{state.CommercialTimeline.Count}; " +
                    $"sales {y8SalesBefore}->{state.Orders.Count}; " +
                    $"purchases {y8PurchasesBefore}->{state.PurchaseOrders.Count}; " +
                    $"contracts {y8ContractsBefore}->{state.Contracts.Count}; " +
                    $"procurement {y8ProcurementBefore}->{state.ProcurementContracts.Count}");
            }
            catch (Exception ex)
            {
                r.Check(false, "Stage 7A/7C commercial-history fixtures completed", ex.ToString());
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

        // --- Stage 7D acceptance surface -------------------------------------------------

        /// <summary>
        /// Covers only the Stage 7 gate holes that U/W/Y do not prove. Sale and purchase rows
        /// are seeded through their real completion transitions; the remaining event kinds are
        /// compact read-model fixtures because their write sites already have dedicated transition
        /// tests. The assertion is still against the presented Relations row, not raw records.
        /// </summary>
        private static void CheckStage7AcceptanceSurface(
            Results r, IntercolonyWorldComponent state)
        {
            const int settlementId = 710901;
            const int saleId = 710902;
            const int purchaseId = 710903;

            List<CommercialHistoryEntry> savedHistory =
                new List<CommercialHistoryEntry>(state.CommercialHistory);
            Dictionary<int, CommercialReputation> savedReputations =
                new Dictionary<int, CommercialReputation>(state.Reputations);
            List<ProductBrandRecord> savedBrands =
                new List<ProductBrandRecord>(state.ProductBrandRecords);
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

            try
            {
                ResetHistoryFixtures(state);
                state.ProductBrandRecords.Clear();

                ThingDef fixtureDef = IntercolonyProductClassifier.TradableDefs.Count > 0
                    ? IntercolonyProductClassifier.TradableDefs[0]
                    : ThingDefOf.Steel ?? ThingDefOf.Silver;
                if (fixtureDef == null)
                {
                    const string reason =
                        "no ThingDef is available for the commercial-history fixture";
                    r.Skip("G1 presented sale and purchase rows", reason);
                    r.Skip("G2 presented procurement contract rows", reason);
                    r.Skip("G3 presented negotiation rows", reason);
                    r.Skip("G4 presented brand context without a brand control", reason);
                    r.Skip("G8 distinguished standing from brand", reason);
                    r.Skip("G9 one settlement-history surface covered all five categories", reason);
                    return;
                }

                CommercialReputation reputation = new CommercialReputation(
                    settlementId, "Stage 7D Testholme", "Stage 7D faction");
                state.Reputations[settlementId] = reputation;
                state.ProductBrandRecords.Add(new ProductBrandRecord(
                    fixtureDef,
                    ProductBrandService.EstablishedThreshold + 10f,
                    evidenceWeight: 4f,
                    unitsDelivered: 4));

                // Real production paths: these two completion boundaries must write the rows
                // that G1 presents. PurchaseOrderService resolves its owner through Current,
                // which is the same world component passed to this self-test in live use.
                SalesOrder completedSale = MakeHistorySale(
                    saleId, settlementId, fixtureDef, 4, 37, 9.99f,
                    SalesOrderStatus.Accepted);
                completedSale.acceptedTick = GenTicks.TicksGame;
                completedSale.deadlineTick = GenTicks.TicksGame + GenDate.TicksPerDay;
                completedSale.deliveredQuantity = completedSale.Quantity;
                state.Orders.Add(completedSale);
                SalesOrderService.Complete(
                    state, completedSale, GenTicks.TicksGame, "Stage 7D sale fixture");

                PurchaseOrder completedPurchase = MakeHistoryPurchase(
                    purchaseId, settlementId, fixtureDef, 3, 29, 2.5f,
                    PurchaseOrderStatus.Confirmed);
                state.PurchaseOrders.Add(completedPurchase);
                PurchaseOrderService.Complete(completedPurchase, "Stage 7D purchase fixture");

                // Direct records are used for the hard-to-reach read combinations. Their
                // production writers are covered by the contract, RFQ, negotiation and brand
                // self-tests; unique detail markers make a missing presented category explicit.
                CommercialTimelineService.Record(
                    state, CommercialEventType.ContractStarted, settlementId,
                    "Stage 7D Testholme", 710904, fixtureDef, 40, 120,
                    "procurement contract started");
                CommercialTimelineService.Record(
                    state, CommercialEventType.ProcurementCycleCompleted, settlementId,
                    "Stage 7D Testholme", 710905, fixtureDef, 40, 120,
                    "procurement cycle completed");
                CommercialTimelineService.Record(
                    state, CommercialEventType.ContractCancelled, settlementId,
                    "Stage 7D Testholme", 710906, fixtureDef, 40, 0,
                    "procurement contract ended/cancelled");
                CommercialTimelineService.Record(
                    state, CommercialEventType.CounterofferAccepted, settlementId,
                    "Stage 7D Testholme", 710907, fixtureDef, 5, 80,
                    "accepted counteroffer");
                CommercialTimelineService.Record(
                    state, CommercialEventType.DeadlineExtended, settlementId,
                    "Stage 7D Testholme", 710908, fixtureDef, 5, 0,
                    "deadline extension accepted");
                CommercialTimelineService.Record(
                    state, CommercialEventType.QuantityReduced, settlementId,
                    "Stage 7D Testholme", 710909, fixtureDef, 3, 0,
                    "quantity reduction accepted");
                CommercialTimelineService.Record(
                    state, CommercialEventType.BrandMilestone, settlementId,
                    "Stage 7D Testholme", 710910, fixtureDef, 4, 0,
                    "product brand reached Established");

                CommercialHistoryRelationRow row =
                    CommercialHistoryUiService.BuildRow(state, settlementId);
                List<string> labels = RelationTimelineLabels(row.timelineRows);

                bool g1Sale = HasTimelineLabel(labels, "Sale completed");
                bool g1Purchase = HasTimelineLabel(labels, "Purchase completed");
                r.Check(
                    g1Sale && g1Purchase,
                    "G1 presented timeline contains distinguishable completed sale and purchase rows",
                    $"missing categories={MissingCategories(
                        !g1Sale ? "completed sale" : null,
                        !g1Purchase ? "completed purchase" : null)}; " +
                    $"labels={DescribeStrings(labels)}");

                bool g2Started = HasTimelineLabel(labels, "Sales agreement started") &&
                                 HasTimelineLabel(labels, "procurement contract started");
                bool g2Cycle = HasTimelineLabel(labels, "Procurement cycle completed") &&
                               HasTimelineLabel(labels, "procurement cycle completed");
                bool g2Ended = HasTimelineLabel(labels, "Sales agreement cancelled") &&
                               HasTimelineLabel(labels, "procurement contract ended/cancelled");
                r.Check(
                    g2Started && g2Cycle && g2Ended,
                    "G2 presented timeline contains procurement start, cycle completion and contract end",
                    $"missing categories={MissingCategories(
                        !g2Started ? "procurement contract started" : null,
                        !g2Cycle ? "procurement cycle completed" : null,
                        !g2Ended ? "procurement contract ended" : null)}; " +
                    $"labels={DescribeStrings(labels)}");

                bool g3Counter = HasTimelineLabel(labels, "Counteroffer accepted") &&
                                 HasTimelineLabel(labels, "accepted counteroffer");
                bool g3Deadline = HasTimelineLabel(labels, "Deadline extended") &&
                                  HasTimelineLabel(labels, "deadline extension accepted");
                bool g3Quantity = HasTimelineLabel(labels, "Quantity reduced") &&
                                  HasTimelineLabel(labels, "quantity reduction accepted");
                r.Check(
                    g3Counter && g3Deadline && g3Quantity,
                    "G3 presented timeline contains accepted counteroffer, deadline extension and quantity reduction",
                    $"missing categories={MissingCategories(
                        !g3Counter ? "accepted counteroffer" : null,
                        !g3Deadline ? "deadline extension" : null,
                        !g3Quantity ? "quantity reduction" : null)}; " +
                    $"labels={DescribeStrings(labels)}");

                List<string> presented = PresentedRelationStrings(row);
                List<string> brandControls = new List<string>();
                string[] brandControlFragments =
                {
                    "manage brand", "brand management", "set brand", "adjust brand", "edit brand"
                };
                foreach (string value in presented)
                {
                    foreach (string fragment in brandControlFragments)
                    {
                        if (ContainsIgnoreCase(value, fragment))
                        {
                            brandControls.Add(value);
                            break;
                        }
                    }
                }

                bool g4Milestone = HasTimelineLabel(labels, "Brand milestone") &&
                                   HasTimelineLabel(labels, "product brand reached Established");
                bool g4NoManagementControl = brandControls.Count == 0;
                r.Check(
                    g4Milestone && g4NoManagementControl,
                    "G4 presented timeline shows brand milestones as context without a brand-management control",
                    $"missing categories={MissingCategories(
                        !g4Milestone ? "brand milestone" : null,
                        !g4NoManagementControl ? "no brand-management control" : null)}; " +
                    $"controls={DescribeStrings(brandControls)}; presented={DescribeStrings(presented)}");

                string standingBefore = SummaryRowValue(row.summaryRows, "Commercial standing");
                string brandLabel = FindTimelineLabel(labels, "Brand milestone");
                ProductBrandUiService.BrandSummary brandBefore =
                    ProductBrandUiService.BuildSummary(state);
                float brandScoreBefore = EffectiveBrandService.GetEffectiveBrand(state, fixtureDef);
                reputation.Adjust(25f);
                CommercialHistoryRelationRow afterReputationChange =
                    CommercialHistoryUiService.BuildRow(state, settlementId);
                string standingAfter = SummaryRowValue(
                    afterReputationChange.summaryRows, "Commercial standing");
                ProductBrandUiService.BrandSummary brandAfter =
                    ProductBrandUiService.BuildSummary(state);
                float brandScoreAfter = EffectiveBrandService.GetEffectiveBrand(state, fixtureDef);
                bool g8StandingMoved = standingBefore != standingAfter;
                bool g8BrandStayed = Mathf.Approximately(brandScoreBefore, brandScoreAfter) &&
                                     brandBefore.knownFor.Count == brandAfter.knownFor.Count &&
                                     brandBefore.weakReputation.Count == brandAfter.weakReputation.Count;
                bool g8DifferentFacts = standingBefore != brandLabel &&
                                        standingAfter != brandLabel &&
                                        standingBefore != standingAfter;
                r.Check(
                    standingBefore == reputation.TierLabel(ReputationTier.Known) &&
                    standingAfter == reputation.TierLabel(ReputationTier.Reliable) &&
                    g8StandingMoved && g8BrandStayed && g8DifferentFacts,
                    "G8 commercial reliability and product brand are different labelled facts",
                    $"missing categories={MissingCategories(
                        standingBefore != reputation.TierLabel(ReputationTier.Known) ? "initial reputation tier" : null,
                        standingAfter != reputation.TierLabel(ReputationTier.Reliable) ? "changed reputation tier" : null,
                        !g8BrandStayed ? "stable global product brand" : null,
                        !g8DifferentFacts ? "distinct standing/brand wording" : null)}; " +
                    $"standing={standingBefore}->{standingAfter}; brandRow={brandLabel}; " +
                    $"brandScore={brandScoreBefore:0.##}->{brandScoreAfter:0.##}");

                bool g9Sales = g1Sale && g1Purchase;
                bool g9Contracts = g2Started && g2Cycle && g2Ended;
                bool g9Negotiation = g3Counter && g3Deadline && g3Quantity;
                bool g9Brand = g4Milestone;
                r.Check(
                    g9Sales && g9Contracts && g9Negotiation && g9Brand,
                    "G9 one settlement-history detail row covers sales, purchases, contracts, negotiation and brand context",
                    $"missing categories={MissingCategories(
                        !g1Sale ? "sales" : null,
                        !g1Purchase ? "purchases" : null,
                        !g9Contracts ? "contract events" : null,
                        !g9Negotiation ? "negotiation events" : null,
                        !g9Brand ? "brand context" : null)}; " +
                    $"settlement={settlementId}; labels={DescribeStrings(labels)}");
            }
            catch (Exception ex)
            {
                r.Check(false, "Stage 7D acceptance-surface fixtures completed", ex.ToString());
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

                state.ProductBrandRecords.Clear();
                state.ProductBrandRecords.AddRange(savedBrands);
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
                r.Info(
                    $"Stage 7D acceptance fixtures restored: history={state.CommercialHistory.Count}; " +
                    $"reputations={state.Reputations.Count}; brands={state.ProductBrandRecords.Count}; " +
                    $"timeline={state.CommercialTimeline.Count}; sales={state.Orders.Count}; " +
                    $"purchases={state.PurchaseOrders.Count}; contracts={state.Contracts.Count +
                    state.ProcurementContracts.Count}.");
            }
        }

        private static bool HasTimelineLabel(List<string> labels, string fragment)
        {
            return FindTimelineLabel(labels, fragment) != null;
        }

        private static string FindTimelineLabel(List<string> labels, string fragment)
        {
            if (labels != null)
            {
                foreach (string label in labels)
                {
                    if (ContainsIgnoreCase(label, fragment))
                    {
                        return label;
                    }
                }
            }

            return null;
        }

        private static string MissingCategories(params string[] categories)
        {
            List<string> missing = new List<string>();
            foreach (string category in categories)
            {
                if (!string.IsNullOrEmpty(category))
                {
                    missing.Add(category);
                }
            }

            return missing.Count == 0 ? "none" : string.Join(", ", missing.ToArray());
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

        private static string SummaryRowValue(
            List<CommercialHistorySummaryRow> rows, string label)
        {
            if (rows != null)
            {
                foreach (CommercialHistorySummaryRow row in rows)
                {
                    if (row.label == label)
                    {
                        return row.value;
                    }
                }
            }

            return "<missing>";
        }

        private static string SummaryRowTooltip(
            List<CommercialHistorySummaryRow> rows, string label)
        {
            if (rows != null)
            {
                foreach (CommercialHistorySummaryRow row in rows)
                {
                    if (row.label == label)
                    {
                        return row.tooltip;
                    }
                }
            }

            return "<missing>";
        }

        private static List<string> PresentedRelationStrings(
            CommercialHistoryRelationRow row)
        {
            List<string> result = new List<string>
            {
                row.settlementLabel,
                row.factionLabel,
                row.factionAndGoodwillLabel,
                row.goodwillLabel,
                row.scoreLabel,
                row.statsLabel,
                row.rowTooltip,
                row.emptyTimelineLabel
            };

            if (row.summaryRows != null)
            {
                foreach (CommercialHistorySummaryRow summary in row.summaryRows)
                {
                    result.Add(summary.label);
                    result.Add(summary.value);
                    result.Add(summary.tooltip);
                }
            }

            if (row.timelineRows != null)
            {
                foreach (CommercialHistoryTimelineRow timeline in row.timelineRows)
                {
                    result.Add(timeline.label);
                    result.Add(timeline.tooltip);
                }
            }

            return result;
        }

        private static List<string> RelationTimelineLabels(
            List<CommercialHistoryTimelineRow> rows)
        {
            List<string> result = new List<string>();
            if (rows == null)
            {
                return result;
            }

            foreach (CommercialHistoryTimelineRow row in rows)
            {
                result.Add(row.label ?? "");
            }

            return result;
        }

        private static string DescribeTimelineLabels(
            List<CommercialHistoryTimelineRow> rows)
        {
            List<string> labels = new List<string>();
            if (rows != null)
            {
                foreach (CommercialHistoryTimelineRow row in rows)
                {
                    labels.Add(row.label ?? "<null>");
                }
            }

            return "[" + string.Join(" | ", labels.ToArray()) + "]";
        }

        private static string DescribeStrings(List<string> values)
        {
            List<string> text = new List<string>();
            if (values != null)
            {
                foreach (string value in values)
                {
                    text.Add(value == null ? "<null>" : $"\"{value.Replace("\n", "\\n")}\"");
                }
            }

            return "[" + string.Join(" | ", text.ToArray()) + "]";
        }

        private static bool ContainsIgnoreCase(string value, string fragment)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
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
