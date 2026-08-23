using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>One labelled value in the expanded settlement history summary.</summary>
    internal readonly struct CommercialHistorySummaryRow
    {
        internal readonly string label;
        internal readonly string value;
        internal readonly string tooltip;

        internal CommercialHistorySummaryRow(string label, string value, string tooltip)
        {
            this.label = label;
            this.value = value;
            this.tooltip = tooltip;
        }
    }

    /// <summary>One already-formatted event in the expanded settlement history timeline.</summary>
    internal readonly struct CommercialHistoryTimelineRow
    {
        internal readonly string label;
        internal readonly string tooltip;

        internal CommercialHistoryTimelineRow(string label, string tooltip)
        {
            this.label = label;
            this.tooltip = tooltip;
        }
    }

    /// <summary>
    /// Complete row model for the Relations surface. The window receives labels and display
    /// decisions here; it does not reconstruct commercial history while drawing.
    /// </summary>
    internal readonly struct CommercialHistoryRelationRow
    {
        internal readonly int settlementId;
        internal readonly string settlementLabel;
        internal readonly string factionLabel;
        internal readonly string factionAndGoodwillLabel;
        internal readonly string goodwillLabel;
        internal readonly int score;
        internal readonly ReputationTier tier;
        internal readonly bool hasReputation;
        internal readonly string scoreLabel;
        internal readonly string statsLabel;
        internal readonly string rowTooltip;
        internal readonly List<CommercialHistorySummaryRow> summaryRows;
        internal readonly List<CommercialHistoryTimelineRow> timelineRows;
        internal readonly string emptyTimelineLabel;

        internal CommercialHistoryRelationRow(
            int settlementId,
            string settlementLabel,
            string factionLabel,
            string factionAndGoodwillLabel,
            string goodwillLabel,
            int score,
            ReputationTier tier,
            bool hasReputation,
            string scoreLabel,
            string statsLabel,
            string rowTooltip,
            List<CommercialHistorySummaryRow> summaryRows,
            List<CommercialHistoryTimelineRow> timelineRows,
            string emptyTimelineLabel)
        {
            this.settlementId = settlementId;
            this.settlementLabel = settlementLabel;
            this.factionLabel = factionLabel;
            this.factionAndGoodwillLabel = factionAndGoodwillLabel;
            this.goodwillLabel = goodwillLabel;
            this.score = score;
            this.tier = tier;
            this.hasReputation = hasReputation;
            this.scoreLabel = scoreLabel;
            this.statsLabel = statsLabel;
            this.rowTooltip = rowTooltip;
            this.summaryRows = summaryRows;
            this.timelineRows = timelineRows;
            this.emptyTimelineLabel = emptyTimelineLabel;
        }
    }

    /// <summary>
    /// Read model for the settlement-history detail embedded in Relations. It asks the durable
    /// summary and bounded timeline services for data, then owns every player-facing label.
    /// </summary>
    internal static class CommercialHistoryUiService
    {
        /// <summary>
        /// A detail view requests a screenful. The page scrolls the expanded rows, while the
        /// bounded request prevents one settlement from expanding into the full 1,000-record cap.
        /// </summary>
        internal const int TimelineRowLimit = 12;

        internal static List<CommercialHistoryRelationRow> BuildRows(
            IntercolonyWorldComponent state)
        {
            List<CommercialHistoryRelationRow> rows =
                new List<CommercialHistoryRelationRow>();
            if (state == null)
            {
                return rows;
            }

            HashSet<int> settlementIds = new HashSet<int>();
            if (state.Reputations != null)
            {
                foreach (KeyValuePair<int, CommercialReputation> entry in state.Reputations)
                {
                    if (entry.Value != null)
                    {
                        settlementIds.Add(entry.Key);
                    }
                }
            }

            if (state.CommercialHistory != null)
            {
                foreach (CommercialHistoryEntry entry in state.CommercialHistory)
                {
                    if (entry != null)
                    {
                        settlementIds.Add(entry.settlementId);
                    }
                }
            }

            if (state.CommercialTimeline != null)
            {
                foreach (CommercialEventRecord record in state.CommercialTimeline)
                {
                    if (record != null)
                    {
                        settlementIds.Add(record.settlementId);
                    }
                }
            }

            if (state.Contracts != null)
            {
                foreach (RecurringContract contract in state.Contracts)
                {
                    if (contract != null)
                    {
                        settlementIds.Add(contract.settlementId);
                    }
                }
            }

            if (state.ProcurementContracts != null)
            {
                foreach (ProcurementContract contract in state.ProcurementContracts)
                {
                    if (contract != null)
                    {
                        settlementIds.Add(contract.settlementId);
                    }
                }
            }

            if (state.PurchaseOrders != null)
            {
                foreach (PurchaseOrder order in state.PurchaseOrders)
                {
                    if (order != null)
                    {
                        settlementIds.Add(order.settlementId);
                    }
                }
            }

            foreach (int settlementId in settlementIds)
            {
                CommercialHistoryRelationRow row = BuildRow(state, settlementId);
                CommercialHistorySummary summary =
                    CommercialHistoryService.BuildSummary(state, settlementId);
                if (row.hasReputation ||
                    summary.HistoryCoverage != CommercialHistoryCoverage.None)
                {
                    rows.Add(row);
                }
            }

            rows.Sort((left, right) =>
            {
                if (left.hasReputation != right.hasReputation)
                {
                    return left.hasReputation ? -1 : 1;
                }

                int byScore = right.score.CompareTo(left.score);
                return byScore != 0
                    ? byScore
                    : string.Compare(
                        left.settlementLabel,
                        right.settlementLabel,
                        StringComparison.CurrentCultureIgnoreCase);
            });
            return rows;
        }

        internal static CommercialHistoryRelationRow BuildRow(
            IntercolonyWorldComponent state, int settlementId)
        {
            CommercialReputation reputation = state?.FindReputation(settlementId);
            Settlement settlement = IntercolonyMarketAccess.FindSettlement(settlementId);
            CommercialHistorySummary summary =
                CommercialHistoryService.BuildSummary(state, settlementId);

            string settlementLabel = settlement?.Label.ToString();
            if (string.IsNullOrEmpty(settlementLabel))
            {
                settlementLabel = reputation?.settlementName;
            }

            if (string.IsNullOrEmpty(settlementLabel))
            {
                settlementLabel = HistoricalSettlementName(state, settlementId);
            }

            if (string.IsNullOrEmpty(settlementLabel))
            {
                settlementLabel = $"Settlement {settlementId}";
            }

            string factionLabel = settlement?.Faction?.Name;
            if (string.IsNullOrEmpty(factionLabel))
            {
                factionLabel = reputation?.factionName;
            }

            bool hasFaction = !string.IsNullOrEmpty(factionLabel);
            string goodwillLabel = settlement?.Faction != null
                ? $"goodwill {settlement.Faction.PlayerGoodwill:+#;-#;0}"
                : hasFaction ? factionLabel : "(gone)";
            string factionAndGoodwillLabel = string.IsNullOrEmpty(factionLabel)
                ? goodwillLabel
                : $"{factionLabel}  {goodwillLabel}";
            int score = reputation?.ScoreDisplay ?? 0;
            ReputationTier tier = reputation?.Tier ?? ReputationTier.Known;
            bool hasReputation = reputation != null;
            string scoreLabel = hasReputation
                ? $"{score}/100  {reputation.TierLabel()}"
                : "No reputation record";
            string statsLabel = hasReputation
                ? $"{reputation.ordersCompleted} completed   {reputation.ordersLate} late   " +
                  $"{reputation.ordersFailed} failed   {reputation.ordersCancelled} cancelled   " +
                  $"{reputation.purchasesCompleted} purchases"
                : "No reputation counters retained";

            List<CommercialHistorySummaryRow> summaryRows = BuildSummaryRows(summary);
            List<CommercialEventRecord> events = CommercialHistoryService.BuildTimeline(
                state, settlementId, TimelineRowLimit);
            List<CommercialHistoryTimelineRow> timelineRows =
                new List<CommercialHistoryTimelineRow>();
            foreach (CommercialEventRecord record in events)
            {
                timelineRows.Add(BuildTimelineRow(record));
            }

            string emptyTimelineLabel = EmptyTimelineLabel(summary);
            string rowTooltip = BuildRowTooltip(reputation, settlementId);
            return new CommercialHistoryRelationRow(
                settlementId,
                settlementLabel,
                hasFaction ? factionLabel : "",
                factionAndGoodwillLabel,
                goodwillLabel,
                score,
                tier,
                hasReputation,
                scoreLabel,
                statsLabel,
                rowTooltip,
                summaryRows,
                timelineRows,
                emptyTimelineLabel);
        }

        internal static List<CommercialHistorySummaryRow> BuildSummaryRows(
            CommercialHistorySummary summary)
        {
            List<CommercialHistorySummaryRow> rows =
                new List<CommercialHistorySummaryRow>();
            rows.Add(new CommercialHistorySummaryRow(
                "Commercial standing",
                summary.HasCommercialStanding
                    ? summary.CommercialStanding
                    : "No reputation recorded",
                summary.HasCommercialStanding
                    ? "The settlement's persisted commercial reputation tier."
                    : "No settlement reputation record exists, so no standing is inferred."));
            rows.Add(new CommercialHistorySummaryRow(
                "Trading since",
                TradingSinceLabel(summary),
                TradingSinceTooltip(summary)));
            rows.Add(new CommercialHistorySummaryRow(
                "Completed sales",
                summary.HasCompletedSales
                    ? summary.CompletedSales.ToString("N0")
                    : "No retained sales total",
                summary.HasCompletedSales
                    ? "Completed sales from durable commercial aggregates."
                    : "The retained data cannot support a sales total for this settlement."));
            rows.Add(new CommercialHistorySummaryRow(
                "Completed purchases",
                summary.HasCompletedPurchases
                    ? summary.CompletedPurchases.ToString("N0")
                    : "No reputation purchase total",
                summary.HasCompletedPurchases
                    ? "Completed purchases from the settlement's persisted reputation record."
                    : "No persisted reputation record supports a purchase count."));
            rows.Add(new CommercialHistorySummaryRow(
                "Active contracts",
                summary.HasActiveContracts
                    ? summary.ActiveContracts.ToString("N0")
                    : "Contract data unavailable",
                summary.HasActiveContracts
                    ? "Live sales and procurement agreements, including suspended obligations."
                    : "Both persisted contract collections were not available to count."));
            rows.Add(new CommercialHistorySummaryRow(
                "Total known trade value",
                summary.HasTotalKnownTradeValue
                    ? $"{summary.TotalKnownTradeValue:N0} silver"
                    : "No retained trade-value total",
                summary.HasTotalKnownTradeValue
                    ? "Known silver recorded by durable commercial aggregates; it may be incomplete."
                    : "The retained data cannot support a trade-value total for this settlement."));
            return rows;
        }

        private static CommercialHistoryTimelineRow BuildTimelineRow(
            CommercialEventRecord record)
        {
            string description = EventDescription(record);
            string date = FormatTick(record?.tick ?? CommercialTimelineService.NoHistory);
            string label = string.IsNullOrEmpty(date)
                ? description
                : $"{date} — {description}";
            string tooltip = description +
                             "\nRetained detailed history is shown here; durable totals are " +
                             "kept separately above.";
            return new CommercialHistoryTimelineRow(label, tooltip);
        }

        private static string TradingSinceLabel(CommercialHistorySummary summary)
        {
            if (!summary.HasTradingSince)
            {
                return summary.HistoryCoverage == CommercialHistoryCoverage.None
                    ? "No commercial history recorded"
                    : "Earlier relationship; date unavailable";
            }

            string date = FormatTick(summary.TradingSinceTick);
            if (string.IsNullOrEmpty(date))
            {
                return summary.HistoryPredatesTimeline
                    ? "Earlier relationship; detailed date unavailable"
                    : "Detailed date unavailable";
            }

            if (summary.TradingSinceIsTimelineStart)
            {
                return $"Detailed history tracked since {date}";
            }

            return summary.HistoryCoverage == CommercialHistoryCoverage.Timeline
                ? $"Earliest retained event: {date}"
                : $"Detailed history tracked since {date}";
        }

        private static string TradingSinceTooltip(CommercialHistorySummary summary)
        {
            if (summary.HistoryCoverage == CommercialHistoryCoverage.None)
            {
                return "No retained commercial evidence exists for this settlement.";
            }

            if (summary.TradingSinceIsTimelineStart)
            {
                return summary.HistoryPredatesTimeline
                    ? "This is the start of reliable detailed records, not a claim that the first trade happened on this date. Durable relationship evidence predates the record spine."
                    : "This is the start boundary of reliable detailed records, not a claim that a trade happened on this date.";
            }

            if (summary.HistoryCoverage == CommercialHistoryCoverage.AggregateOnly)
            {
                return "The relationship is supported by durable evidence, but no dated meaningful event remains in the retained timeline.";
            }

            return "This is the earliest retained meaningful event, not necessarily the first trade ever.";
        }

        private static string EmptyTimelineLabel(CommercialHistorySummary summary)
        {
            if (summary.HistoryCoverage == CommercialHistoryCoverage.None)
            {
                return "No commercial events recorded for this settlement.";
            }

            return summary.HistoryPredatesTimeline
                ? "Earlier commercial activity exists, but its detailed events predate the retained timeline."
                : "No detailed commercial events are retained for this settlement.";
        }

        private static string EventDescription(CommercialEventRecord record)
        {
            if (record == null)
            {
                return "Commercial event unavailable";
            }

            string title;
            switch (record.type)
            {
                case CommercialEventType.SaleCompleted: title = "Sale completed"; break;
                case CommercialEventType.SaleFailed: title = "Sale failed"; break;
                case CommercialEventType.SaleCancelled: title = "Sale cancelled"; break;
                case CommercialEventType.PurchaseCompleted: title = "Purchase completed"; break;
                case CommercialEventType.PurchaseFailed: title = "Purchase failed"; break;
                case CommercialEventType.PurchaseCancelled: title = "Purchase cancelled"; break;
                case CommercialEventType.ContractStarted: title = "Sales agreement started"; break;
                case CommercialEventType.ContractCompleted: title = "Sales agreement completed"; break;
                case CommercialEventType.ContractFailed: title = "Sales agreement failed"; break;
                case CommercialEventType.ContractCancelled: title = "Sales agreement cancelled"; break;
                case CommercialEventType.BrandMilestone: title = "Brand milestone"; break;
                case CommercialEventType.CounterofferAccepted: title = "Counteroffer accepted"; break;
                case CommercialEventType.DeadlineExtended: title = "Deadline extended"; break;
                case CommercialEventType.QuantityReduced: title = "Quantity reduced"; break;
                case CommercialEventType.SaleCancelledByAgreement: title = "Sale cancelled by agreement"; break;
                case CommercialEventType.RelationshipMilestone: title = "Relationship milestone"; break;
                case CommercialEventType.ProcurementCycleCompleted: title = "Procurement cycle completed"; break;
                default: title = "Commercial event"; break;
            }

            List<string> context = new List<string>();
            if (record.quantity != 0)
            {
                string item = record.thingDef == null
                    ? "goods"
                    : record.thingDef.LabelCap.ToString();
                context.Add($"{Mathf.Abs(record.quantity):N0}x {item}");
            }

            if (record.silverAmount != 0)
            {
                context.Add($"{record.silverAmount:N0} silver");
            }

            string result = context.Count == 0
                ? title
                : title + ": " + string.Join(", ", context.ToArray());
            if (!string.IsNullOrEmpty(record.compactDetail))
            {
                result += " — " + record.compactDetail;
            }

            return result;
        }

        private static string BuildRowTooltip(CommercialReputation reputation, int settlementId)
        {
            if (reputation == null)
            {
                return "This settlement has retained commercial evidence, but no persisted reputation record. Expand the row for the supported history.";
            }

            string economy = SettlementEconomyDisplay.SettlementEconomicSummary(settlementId);
            return $"{reputation.factionName}\n" +
                   $"Commercial reputation: {reputation.ScoreDisplay}/100 ({reputation.TierLabel()})\n\n" +
                   (string.IsNullOrEmpty(economy) ? "" : economy + "\n") +
                   "A better record means larger orders, more frequent offers, slightly better " +
                   "prices and more generous deadlines.\n\n" +
                   "This is separate from faction goodwill, and it is held by this settlement " +
                   "rather than its faction: another town of the same faction forms its own view.";
        }

        private static string HistoricalSettlementName(
            IntercolonyWorldComponent state, int settlementId)
        {
            if (state?.CommercialTimeline != null)
            {
                foreach (CommercialEventRecord record in state.CommercialTimeline)
                {
                    if (record != null && record.settlementId == settlementId &&
                        !string.IsNullOrEmpty(record.settlementName))
                    {
                        return record.settlementName;
                    }
                }
            }

            if (state?.Contracts != null)
            {
                foreach (RecurringContract contract in state.Contracts)
                {
                    if (contract != null && contract.settlementId == settlementId &&
                        !string.IsNullOrEmpty(contract.settlementName))
                    {
                        return contract.settlementName;
                    }
                }
            }

            if (state?.ProcurementContracts != null)
            {
                foreach (ProcurementContract contract in state.ProcurementContracts)
                {
                    if (contract != null && contract.settlementId == settlementId &&
                        !string.IsNullOrEmpty(contract.settlementName))
                    {
                        return contract.settlementName;
                    }
                }
            }

            if (state?.PurchaseOrders != null)
            {
                foreach (PurchaseOrder order in state.PurchaseOrders)
                {
                    if (order != null && order.settlementId == settlementId &&
                        !string.IsNullOrEmpty(order.settlementName))
                    {
                        return order.settlementName;
                    }
                }
            }

            return "";
        }

        private static string FormatTick(int tick)
        {
            if (tick == CommercialTimelineService.NoHistory || tick < 0)
            {
                return "";
            }

            return GenDate.DateShortStringAt(
                GenDate.TickGameToAbs(tick), Vector2.zero);
        }
    }
}
