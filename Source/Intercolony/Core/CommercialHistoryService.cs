using System.Collections.Generic;

namespace Intercolony
{
    /// <summary>
    /// Describes how much of a settlement's commercial relationship the retained data can date.
    /// This is separate from the timeline itself because durable history can outlive its detail.
    /// </summary>
    public enum CommercialHistoryCoverage
    {
        /// <summary>No retained commercial evidence exists for the settlement.</summary>
        None,

        /// <summary>At least one meaningful detailed event remains in the timeline.</summary>
        Timeline,

        /// <summary>
        /// Durable relationship evidence exists without a retained meaningful timeline event;
        /// on an upgraded save this is the history that predates the record spine.
        /// </summary>
        AggregateOnly
    }

    /// <summary>
    /// Long-term commercial facts for one settlement. Support flags are explicit because a
    /// missing aggregate is not the same fact as an aggregate whose value happens to be zero.
    /// </summary>
    public readonly struct CommercialHistorySummary
    {
        /// <summary>Stable settlement ID used to build this snapshot, so callers cannot confuse rows.</summary>
        public readonly int SettlementId;

        /// <summary>
        /// Reputation tier label from the existing settlement reputation record; null means no
        /// reputation record exists and the service must not invent a neutral standing.
        /// </summary>
        public readonly string CommercialStanding;

        /// <summary>Whether <see cref="CommercialStanding"/> is backed by a persisted reputation record.</summary>
        public readonly bool HasCommercialStanding;

        /// <summary>
        /// Earliest known meaningful event tick, or the timeline-start sentinel when no dated
        /// interaction can be supported; callers must check <see cref="HasTradingSince"/> first.
        /// </summary>
        public readonly int TradingSinceTick;

        /// <summary>Whether <see cref="TradingSinceTick"/> contains a displayable known tick.</summary>
        public readonly bool HasTradingSince;

        /// <summary>
        /// True when the date is only the detailed-record spine's start boundary, so UI can say
        /// "history tracked since" instead of claiming that a trade happened on that tick.
        /// </summary>
        public readonly bool TradingSinceIsTimelineStart;

        /// <summary>
        /// Whether this settlement has no evidence, retained detail, or only durable history;
        /// this lets callers distinguish never-traded from history that predates the spine.
        /// </summary>
        public readonly CommercialHistoryCoverage HistoryCoverage;

        /// <summary>
        /// True when durable commercial evidence exists but no meaningful timeline record does,
        /// which is the honest marker for an upgraded relationship whose detail starts later.
        /// </summary>
        public readonly bool HistoryPredatesTimeline;

        /// <summary>
        /// Completed sale count summed from <see cref="CommercialHistoryEntry"/> aggregates, so
        /// pruning detailed sales or timeline records cannot change this number.
        /// </summary>
        public readonly int CompletedSales;

        /// <summary>Whether the durable commercial-history aggregate can support <see cref="CompletedSales"/>.</summary>
        public readonly bool HasCompletedSales;

        /// <summary>
        /// Completed purchase count from the settlement's persisted commercial reputation
        /// counters; it is not reconstructed by counting display events.
        /// </summary>
        public readonly int CompletedPurchases;

        /// <summary>Whether a persisted reputation record supports <see cref="CompletedPurchases"/>.</summary>
        public readonly bool HasCompletedPurchases;

        /// <summary>
        /// Number of live sales or procurement agreements, including suspended obligations but
        /// excluding offers that have not become contracts.
        /// </summary>
        public readonly int ActiveContracts;

        /// <summary>Whether both persisted contract collections were available to count.</summary>
        public readonly bool HasActiveContracts;

        /// <summary>
        /// Sum of the recorded silver actually exchanged in completed sales and purchases. This
        /// comes from the durable aggregate rather than prunable timeline silver.
        /// </summary>
        public readonly int TotalKnownTradeValue;

        /// <summary>
        /// Whether the settlement has a non-zero durable trade-value aggregate to report. The
        /// caller should also inspect <see cref="HistoryCoverage"/> and
        /// <see cref="HistoryPredatesTimeline"/> before wording it as a complete total.
        /// </summary>
        public readonly bool HasTotalKnownTradeValue;

        internal CommercialHistorySummary(
            int settlementId,
            string commercialStanding,
            bool hasCommercialStanding,
            int tradingSinceTick,
            bool hasTradingSince,
            bool tradingSinceIsTimelineStart,
            CommercialHistoryCoverage historyCoverage,
            bool historyPredatesTimeline,
            int completedSales,
            bool hasCompletedSales,
            int completedPurchases,
            bool hasCompletedPurchases,
            int activeContracts,
            bool hasActiveContracts,
            int totalKnownTradeValue,
            bool hasTotalKnownTradeValue)
        {
            SettlementId = settlementId;
            CommercialStanding = commercialStanding;
            HasCommercialStanding = hasCommercialStanding;
            TradingSinceTick = tradingSinceTick;
            HasTradingSince = hasTradingSince;
            TradingSinceIsTimelineStart = tradingSinceIsTimelineStart;
            HistoryCoverage = historyCoverage;
            HistoryPredatesTimeline = historyPredatesTimeline;
            CompletedSales = completedSales;
            HasCompletedSales = hasCompletedSales;
            CompletedPurchases = completedPurchases;
            HasCompletedPurchases = hasCompletedPurchases;
            ActiveContracts = activeContracts;
            HasActiveContracts = hasActiveContracts;
            TotalKnownTradeValue = totalKnownTradeValue;
            HasTotalKnownTradeValue = hasTotalKnownTradeValue;
        }
    }

    /// <summary>
    /// Read-only commercial relationship model. Summary totals and recent narrative detail use
    /// separate sources so timeline pruning can never alter durable obligations or totals.
    /// </summary>
    public static class CommercialHistoryService
    {
        /// <summary>
        /// Builds the durable summary for one settlement without creating a reputation record or
        /// deriving totals from the bounded timeline.
        /// </summary>
        public static CommercialHistorySummary BuildSummary(
            IntercolonyWorldComponent state, int settlementId)
        {
            if (state == null)
            {
                return EmptySummary(settlementId);
            }

            CommercialReputation reputation = state.FindReputation(settlementId);
            int completedSales = 0;
            int totalKnownTradeValue = 0;
            bool hasTradeValueAggregate = false;
            bool hasSaleAggregate = state.CommercialHistory != null;
            bool hasDurableSaleEvidence = false;

            if (state.CommercialHistory != null)
            {
                foreach (CommercialHistoryEntry entry in state.CommercialHistory)
                {
                    if (entry == null || entry.settlementId != settlementId)
                    {
                        continue;
                    }

                    if (entry.completedSaleCount > 0)
                    {
                        completedSales += entry.completedSaleCount;
                        hasDurableSaleEvidence = true;
                    }

                    // A zero value is the exact "none" sentinel, not a displayable trade total.
                    // Never derive this from timeline silver: those records are prunable.
                    if (entry.totalTradeValue != 0)
                    {
                        totalKnownTradeValue += entry.totalTradeValue;
                        hasTradeValueAggregate = true;
                    }
                }
            }

            int completedPurchases = reputation == null
                ? 0
                : Positive(reputation.purchasesCompleted);
            bool hasPurchaseAggregate = reputation != null;

            int activeContracts = 0;
            bool hasActiveContracts = state.Contracts != null &&
                                      state.ProcurementContracts != null;
            if (state.Contracts != null)
            {
                foreach (RecurringContract contract in state.Contracts)
                {
                    if (contract != null && contract.settlementId == settlementId &&
                        (contract.IsActive || contract.status == ContractStatus.Suspended))
                    {
                        activeContracts++;
                    }
                }
            }

            if (state.ProcurementContracts != null)
            {
                foreach (ProcurementContract contract in state.ProcurementContracts)
                {
                    if (contract != null && contract.settlementId == settlementId &&
                        (contract.status == ProcurementContractStatus.Active ||
                         contract.status == ProcurementContractStatus.Suspended))
                    {
                        activeContracts++;
                    }
                }
            }

            bool hasMeaningfulTimeline = false;
            int earliestTimelineTick = CommercialTimelineService.NoHistory;
            int earliestTimelineId = CommercialTimelineService.NoHistory;
            if (state.CommercialTimeline != null)
            {
                foreach (CommercialEventRecord record in state.CommercialTimeline)
                {
                    if (record == null || record.settlementId != settlementId ||
                        !IsMeaningful(record.type))
                    {
                        continue;
                    }

                    if (!hasMeaningfulTimeline || IsEarlier(record, earliestTimelineTick, earliestTimelineId))
                    {
                        earliestTimelineTick = record.tick;
                        earliestTimelineId = record.id;
                        hasMeaningfulTimeline = true;
                    }
                }
            }

            bool hasReputationEvidence = reputation != null;
            bool hasContractEvidence = HasSettlementContractRecord(state, settlementId);
            bool hasPurchaseOrderEvidence = HasSettlementPurchaseOrderRecord(state, settlementId);
            bool hasDurableCommercialEvidence = hasDurableSaleEvidence || hasReputationEvidence;
            bool hasAnyEvidence = hasMeaningfulTimeline || hasDurableCommercialEvidence ||
                                  hasContractEvidence || hasPurchaseOrderEvidence;

            CommercialHistoryCoverage coverage = !hasAnyEvidence
                ? CommercialHistoryCoverage.None
                : hasMeaningfulTimeline
                    ? CommercialHistoryCoverage.Timeline
                    : CommercialHistoryCoverage.AggregateOnly;

            int tradingSinceTick = CommercialTimelineService.NoHistory;
            bool hasTradingSince = false;
            bool tradingSinceIsTimelineStart = false;
            bool historyPredatesTimeline = false;
            if (hasMeaningfulTimeline)
            {
                tradingSinceTick = earliestTimelineTick;
                hasTradingSince = true;
            }
            else if (hasAnyEvidence &&
                     state.CommercialTimelineStartTick != CommercialTimelineService.NoHistory)
            {
                // This is a lower bound, not a fabricated first-trade date. Schema 43 stamped
                // upgraded saves at this boundary because their older events were never recorded.
                tradingSinceTick = state.CommercialTimelineStartTick;
                hasTradingSince = true;
                tradingSinceIsTimelineStart = true;
                historyPredatesTimeline = hasDurableCommercialEvidence;
            }

            return new CommercialHistorySummary(
                settlementId,
                reputation?.TierLabel(),
                reputation != null,
                tradingSinceTick,
                hasTradingSince,
                tradingSinceIsTimelineStart,
                coverage,
                historyPredatesTimeline,
                completedSales,
                hasSaleAggregate,
                completedPurchases,
                hasPurchaseAggregate,
                activeContracts,
                hasActiveContracts,
                totalKnownTradeValue,
                hasTradeValueAggregate);
        }

        /// <summary>
        /// Returns only meaningful events for one settlement, newest first, capped at the count
        /// supplied by the caller. Sorting a copied list keeps the persisted timeline untouched.
        /// </summary>
        public static List<CommercialEventRecord> BuildTimeline(
            IntercolonyWorldComponent state, int settlementId, int maxCount)
        {
            List<CommercialEventRecord> results = new List<CommercialEventRecord>();
            if (state == null || state.CommercialTimeline == null || maxCount <= 0)
            {
                return results;
            }

            foreach (CommercialEventRecord record in state.CommercialTimeline)
            {
                if (record != null && record.settlementId == settlementId &&
                    IsMeaningful(record.type))
                {
                    results.Add(record);
                }
            }

            results.Sort(CompareRecency);
            if (results.Count > maxCount)
            {
                results.RemoveRange(maxCount, results.Count - maxCount);
            }

            return results;
        }

        private static CommercialHistorySummary EmptySummary(int settlementId)
        {
            return new CommercialHistorySummary(
                settlementId,
                null,
                false,
                CommercialTimelineService.NoHistory,
                false,
                false,
                CommercialHistoryCoverage.None,
                false,
                0,
                false,
                0,
                false,
                0,
                false,
                0,
                false);
        }

        private static bool HasSettlementContractRecord(
            IntercolonyWorldComponent state, int settlementId)
        {
            if (state?.Contracts != null)
            {
                foreach (RecurringContract contract in state.Contracts)
                {
                    if (contract != null && contract.settlementId == settlementId)
                    {
                        return true;
                    }
                }
            }

            if (state?.ProcurementContracts != null)
            {
                foreach (ProcurementContract contract in state.ProcurementContracts)
                {
                    if (contract != null && contract.settlementId == settlementId)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool HasSettlementPurchaseOrderRecord(
            IntercolonyWorldComponent state, int settlementId)
        {
            if (state?.PurchaseOrders == null)
            {
                return false;
            }

            foreach (PurchaseOrder order in state.PurchaseOrders)
            {
                if (order != null && order.settlementId == settlementId)
                {
                    return true;
                }
            }

            return false;
        }

        private static int Positive(int value)
        {
            return value > 0 ? value : 0;
        }

        private static bool IsEarlier(
            CommercialEventRecord record, int earliestTick, int earliestId)
        {
            if (record.tick != earliestTick)
            {
                return record.tick < earliestTick;
            }

            return record.id < earliestId;
        }

        private static int CompareRecency(
            CommercialEventRecord left, CommercialEventRecord right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            int byTick = right.tick.CompareTo(left.tick);
            return byTick != 0 ? byTick : right.id.CompareTo(left.id);
        }

        private static bool IsMeaningful(CommercialEventType type)
        {
            // The explicit allow-list keeps a future refresh/noise event out of the player-facing
            // history by default. Tier and brand milestones stay because they are state changes,
            // not the tiny score adjustments that the timeline is meant to omit.
            switch (type)
            {
                case CommercialEventType.SaleCompleted:
                case CommercialEventType.SaleFailed:
                case CommercialEventType.SaleCancelled:
                case CommercialEventType.PurchaseCompleted:
                case CommercialEventType.PurchaseFailed:
                case CommercialEventType.PurchaseCancelled:
                case CommercialEventType.ContractStarted:
                case CommercialEventType.ContractCompleted:
                case CommercialEventType.ContractFailed:
                case CommercialEventType.ContractCancelled:
                case CommercialEventType.BrandMilestone:
                case CommercialEventType.CounterofferAccepted:
                case CommercialEventType.DeadlineExtended:
                case CommercialEventType.QuantityReduced:
                case CommercialEventType.SaleCancelledByAgreement:
                case CommercialEventType.RelationshipMilestone:
                case CommercialEventType.ProcurementCycleCompleted:
                    return true;
                default:
                    return false;
            }
        }
    }
}
