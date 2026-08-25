using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>Sortable columns on the dedicated Purchase Orders surface.</summary>
    internal enum PurchaseOrdersColumn
    {
        OrderId,
        Supplier,
        Item,
        Quantity,
        TotalPrice,
        Status,
        Fulfillment,
        Timing,
        Action
    }

    /// <summary>
    /// One purchase-order row. The window consumes these labels and action decisions rather than
    /// reconstructing persisted procurement terms while it draws.
    /// </summary>
    internal readonly struct PurchaseOrdersRow
    {
        internal readonly PurchaseOrder order;
        internal readonly int orderId;
        internal readonly int quantity;
        internal readonly int totalPrice;
        internal readonly int timingTick;
        internal readonly bool hasTiming;
        internal readonly bool isLive;
        internal readonly bool canCancel;
        internal readonly string orderIdLabel;
        internal readonly string supplierLabel;
        internal readonly string itemLabel;
        internal readonly string quantityLabel;
        internal readonly string totalPriceLabel;
        internal readonly string statusLabel;
        internal readonly string fulfillmentLabel;
        internal readonly string timingLabel;
        internal readonly string actionLabel;
        internal readonly string tooltip;

        internal PurchaseOrdersRow(
            PurchaseOrder order,
            int orderId,
            int quantity,
            int totalPrice,
            int timingTick,
            bool hasTiming,
            bool isLive,
            bool canCancel,
            string orderIdLabel,
            string supplierLabel,
            string itemLabel,
            string quantityLabel,
            string totalPriceLabel,
            string statusLabel,
            string fulfillmentLabel,
            string timingLabel,
            string actionLabel,
            string tooltip)
        {
            this.order = order;
            this.orderId = orderId;
            this.quantity = quantity;
            this.totalPrice = totalPrice;
            this.timingTick = timingTick;
            this.hasTiming = hasTiming;
            this.isLive = isLive;
            this.canCancel = canCancel;
            this.orderIdLabel = orderIdLabel;
            this.supplierLabel = supplierLabel;
            this.itemLabel = itemLabel;
            this.quantityLabel = quantityLabel;
            this.totalPriceLabel = totalPriceLabel;
            this.statusLabel = statusLabel;
            this.fulfillmentLabel = fulfillmentLabel;
            this.timingLabel = timingLabel;
            this.actionLabel = actionLabel;
            this.tooltip = tooltip;
        }
    }

    /// <summary>
    /// Read model for the Purchase Orders surface. Selection, labels, sorting, derived timing,
    /// and per-row action availability live here; the main-tab window only draws the result.
    /// </summary>
    internal static class PurchaseOrdersUiService
    {
        internal static readonly float[] ColumnWidths =
            { 0.07f, 0.14f, 0.21f, 0.07f, 0.10f, 0.13f, 0.10f, 0.10f, 0.08f };

        internal static readonly string[] ColumnLabels =
            { "Order", "Supplier", "Item", "Qty", "Total", "Status", "Fulfillment", "ETA / pickup deadline", "" };

        internal const string NoOrdersMessage =
            "No purchase orders yet. Accept a quotation in Find seller or buy a Supplier Market listing to place one.";

        internal const string NoLiveOrdersMessage =
            "No purchase orders are currently live. Concluded orders remain below.";

        internal static List<PurchaseOrdersRow> BuildRows(IntercolonyWorldComponent state)
        {
            List<PurchaseOrdersRow> rows = new List<PurchaseOrdersRow>();
            List<PurchaseOrder> orders = SelectPurchaseOrdersForDisplay(state?.PurchaseOrders);
            foreach (PurchaseOrder order in orders)
            {
                rows.Add(BuildRow(order));
            }

            return rows;
        }

        internal static PurchaseOrdersRow BuildRow(PurchaseOrder order)
        {
            int totalPrice = ReportedTotalPrice(order);
            TimingValues(order, out string timingLabel, out int timingTick, out bool hasTiming);
            bool isLive = order != null && order.IsOpen;
            bool canCancel = isLive;
            string itemLabel = order?.ItemLabel() ?? "<missing order>";
            string supplierLabel = SupplierLabel(order);
            string fulfillmentLabel = order?.supplierDelivers == true ? "Delivery" : "Pickup";
            string statusLabel = StatusLabel(order);

            return new PurchaseOrdersRow(
                order,
                order?.id ?? 0,
                order?.quantity ?? 0,
                totalPrice,
                timingTick,
                hasTiming,
                isLive,
                canCancel,
                order == null ? "#?" : $"#{order.id}",
                supplierLabel,
                itemLabel,
                order?.quantity.ToString("N0") ?? "0",
                $"{totalPrice:N0} silver",
                statusLabel,
                fulfillmentLabel,
                timingLabel,
                canCancel ? "Cancel" : "",
                BuildTooltip(order, supplierLabel, itemLabel, statusLabel, timingLabel));
        }

        internal static string EmptyState(List<PurchaseOrdersRow> rows)
        {
            if (rows == null || rows.Count == 0)
            {
                return NoOrdersMessage;
            }

            foreach (PurchaseOrdersRow row in rows)
            {
                if (row.isLive)
                {
                    return null;
                }
            }

            return NoLiveOrdersMessage;
        }

        internal static void SortRows(
            List<PurchaseOrdersRow> rows,
            PurchaseOrdersColumn column,
            bool descending)
        {
            if (rows == null)
            {
                return;
            }

            List<PurchaseOrdersRow> live = new List<PurchaseOrdersRow>();
            List<PurchaseOrdersRow> concluded = new List<PurchaseOrdersRow>();
            foreach (PurchaseOrdersRow row in rows)
            {
                (row.isLive ? live : concluded).Add(row);
            }

            SortGroup(live, column, descending);
            SortGroup(concluded, column, descending);
            rows.Clear();
            rows.AddRange(live);
            rows.AddRange(concluded);
        }

        internal static bool DefaultDescending(PurchaseOrdersColumn column)
        {
            return column == PurchaseOrdersColumn.OrderId ||
                   column == PurchaseOrdersColumn.Quantity ||
                   column == PurchaseOrdersColumn.TotalPrice;
        }

        internal static string HeaderLabel(
            PurchaseOrdersColumn column, bool active, bool descending)
        {
            string label = ColumnLabels[(int)column];
            return active ? label + (descending ? " v" : " ^") : label;
        }

        internal static string CellLabel(PurchaseOrdersRow row, PurchaseOrdersColumn column)
        {
            switch (column)
            {
                case PurchaseOrdersColumn.OrderId: return row.orderIdLabel;
                case PurchaseOrdersColumn.Supplier: return row.supplierLabel;
                case PurchaseOrdersColumn.Item: return row.itemLabel;
                case PurchaseOrdersColumn.Quantity: return row.quantityLabel;
                case PurchaseOrdersColumn.TotalPrice: return row.totalPriceLabel;
                case PurchaseOrdersColumn.Status: return row.statusLabel;
                case PurchaseOrdersColumn.Fulfillment: return row.fulfillmentLabel;
                case PurchaseOrdersColumn.Timing: return row.timingLabel;
                default: return row.actionLabel;
            }
        }

        internal static string OriginLabel(PurchaseOrder order)
        {
            return order != null && order.supplierListingId != PurchaseOrder.NoSupplierListing
                ? "Supplier Market"
                : "RFQ";
        }

        /// <summary>
        /// Keeps the compatibility selection seam used by the existing order diagnostics while
        /// making the live-before-concluded convention explicit for the new surface.
        /// </summary>
        internal static List<PurchaseOrder> SelectPurchaseOrdersForDisplay(
            IEnumerable<PurchaseOrder> orders)
        {
            List<PurchaseOrder> selected = new List<PurchaseOrder>();
            if (orders == null)
            {
                return selected;
            }

            foreach (PurchaseOrder order in orders)
            {
                if (order != null)
                {
                    selected.Add(order);
                }
            }

            MarketTableSortUtility.Sort(
                selected,
                (a, b) => a.IsOpen == b.IsOpen ? 0 : (a.IsOpen ? -1 : 1),
                descending: false,
                (a, b) => b.id.CompareTo(a.id));
            return selected;
        }

        private static void SortGroup(
            List<PurchaseOrdersRow> rows,
            PurchaseOrdersColumn column,
            bool descending)
        {
            Comparison<PurchaseOrdersRow> comparison;
            switch (column)
            {
                case PurchaseOrdersColumn.OrderId:
                    comparison = (a, b) => a.orderId.CompareTo(b.orderId);
                    break;
                case PurchaseOrdersColumn.Supplier:
                    comparison = (a, b) => string.Compare(
                        a.supplierLabel, b.supplierLabel, StringComparison.CurrentCultureIgnoreCase);
                    break;
                case PurchaseOrdersColumn.Item:
                    comparison = (a, b) => string.Compare(
                        a.itemLabel, b.itemLabel, StringComparison.CurrentCultureIgnoreCase);
                    break;
                case PurchaseOrdersColumn.Quantity:
                    comparison = (a, b) => a.quantity.CompareTo(b.quantity);
                    break;
                case PurchaseOrdersColumn.TotalPrice:
                    comparison = (a, b) => a.totalPrice.CompareTo(b.totalPrice);
                    break;
                case PurchaseOrdersColumn.Status:
                    comparison = (a, b) => string.Compare(
                        a.statusLabel, b.statusLabel, StringComparison.CurrentCultureIgnoreCase);
                    break;
                case PurchaseOrdersColumn.Fulfillment:
                    comparison = (a, b) => string.Compare(
                        a.fulfillmentLabel, b.fulfillmentLabel,
                        StringComparison.CurrentCultureIgnoreCase);
                    break;
                case PurchaseOrdersColumn.Timing:
                    comparison = CompareTiming;
                    break;
                default:
                    comparison = (a, b) => string.Compare(
                        a.actionLabel, b.actionLabel, StringComparison.CurrentCultureIgnoreCase);
                    break;
            }

            MarketTableSortUtility.Sort(
                rows,
                comparison,
                descending,
                (a, b) => a.orderId.CompareTo(b.orderId));
        }

        private static int CompareTiming(PurchaseOrdersRow a, PurchaseOrdersRow b)
        {
            if (a.hasTiming != b.hasTiming)
            {
                return a.hasTiming ? -1 : 1;
            }

            if (!a.hasTiming)
            {
                return 0;
            }

            return a.timingTick.CompareTo(b.timingTick);
        }

        private static int ReportedTotalPrice(PurchaseOrder order)
        {
            if (order == null)
            {
                return 0;
            }

            // paidSilver is the amount charged at the order boundary. Falling back to the
            // entity's shared pricing property keeps hand-built legacy records readable without
            // multiplying price in this UI file.
            return order.paidSilver > 0 ? order.paidSilver : order.TotalPrice;
        }

        private static string SupplierLabel(PurchaseOrder order)
        {
            if (order == null)
            {
                return "Unknown supplier\nRFQ";
            }

            string supplier = order.settlementName.NullOrEmpty()
                ? "Unknown supplier"
                : order.settlementName;
            return supplier + "\n" + OriginLabel(order);
        }

        private static string StatusLabel(PurchaseOrder order)
        {
            if (order == null)
            {
                return "Unknown";
            }

            switch (order.status)
            {
                case PurchaseOrderStatus.Confirmed: return "Confirmed";
                case PurchaseOrderStatus.ReadyForPickup: return "Ready for pickup";
                case PurchaseOrderStatus.Completed: return "Completed";
                case PurchaseOrderStatus.Cancelled: return "Cancelled";
                case PurchaseOrderStatus.SupplierDefault: return "Supplier default";
                case PurchaseOrderStatus.LostToWar: return "Lost to war";
                default: return order.status.ToString();
            }
        }

        private static void TimingValues(
            PurchaseOrder order,
            out string label,
            out int timingTick,
            out bool hasTiming)
        {
            timingTick = 0;
            hasTiming = false;
            if (order == null)
            {
                label = "No ETA or pickup deadline";
                return;
            }

            if (order.supplierDelivers)
            {
                if (order.status == PurchaseOrderStatus.Completed)
                {
                    label = "Arrived";
                    return;
                }

                if (!order.IsOpen || order.readyTick <= 0)
                {
                    label = "No arrival date";
                    return;
                }

                hasTiming = true;
                timingTick = order.readyTick;
                label = $"Arrives in {Mathf.Max(0f, order.DaysUntilReady):F1}d";
                return;
            }

            if (order.status == PurchaseOrderStatus.Completed)
            {
                label = "Collected";
                return;
            }

            if (!order.IsOpen || order.pickupExpiryTick <= 0)
            {
                label = "No pickup deadline";
                return;
            }

            hasTiming = true;
            timingTick = order.pickupExpiryTick;
            label = $"Collect by {Mathf.Max(0f, order.DaysUntilPickupExpires):F1}d";
        }

        private static string BuildTooltip(
            PurchaseOrder order,
            string supplierLabel,
            string itemLabel,
            string statusLabel,
            string timingLabel)
        {
            if (order == null)
            {
                return "The purchase order is unavailable.";
            }

            string fulfillment = order.supplierDelivers
                ? "The supplier delivers to your colony."
                : "Send a caravan to collect at the supplier settlement.";
            string outcome = order.outcomeNote.NullOrEmpty()
                ? ""
                : "\n\nOutcome: " + order.outcomeNote;
            return $"{itemLabel} from {supplierLabel}\n" +
                   $"{order.quantity}x, {order.paidSilver:N0} silver charged\n" +
                   $"{statusLabel} — {timingLabel}\n{fulfillment}{outcome}";
        }
    }
}
