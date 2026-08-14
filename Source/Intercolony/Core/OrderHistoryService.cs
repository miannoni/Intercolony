using System.Collections.Generic;

namespace Intercolony
{
    /// <summary>
    /// Bounds detailed order history without touching commitments or records still needed by
    /// another live system. Durable commercial totals live separately on the world component.
    /// </summary>
    public static class OrderHistoryService
    {
        public const int MaxClosedSalesOrders = 100;
        public const int MaxClosedPurchaseOrders = 100;

        /// <summary>Prunes closed order detail beyond the two retention caps.</summary>
        public static int Prune(IntercolonyWorldComponent state)
        {
            if (state == null)
            {
                return 0;
            }

            return PruneSalesOrders(state) + PrunePurchaseOrders(state);
        }

        private static int PruneSalesOrders(IntercolonyWorldComponent state)
        {
            List<SalesOrder> orders = state.Orders;
            if (orders == null || orders.Count == 0)
            {
                return 0;
            }

            List<SalesOrder> closed = new List<SalesOrder>();
            foreach (SalesOrder order in orders)
            {
                if (order != null && !order.IsOpen)
                {
                    closed.Add(order);
                }
            }

            if (closed.Count <= MaxClosedSalesOrders)
            {
                return 0;
            }

            closed.Sort(CompareSalesRecency);
            HashSet<SalesOrder> retained = new HashSet<SalesOrder>();
            for (int i = 0; i < MaxClosedSalesOrders; i++)
            {
                retained.Add(closed[i]);
            }

            HashSet<int> contractOrderIds = LiveContractOrderIds(state.Contracts);
            return orders.RemoveAll(order =>
                order != null &&
                !order.IsOpen &&
                !retained.Contains(order) &&
                !contractOrderIds.Contains(order.id) &&
                !CompletedInCurrentRefresh(order, state.LastRefreshTick));
        }

        private static int PrunePurchaseOrders(IntercolonyWorldComponent state)
        {
            List<PurchaseOrder> orders = state.PurchaseOrders;
            if (orders == null || orders.Count == 0)
            {
                return 0;
            }

            List<PurchaseOrder> closed = new List<PurchaseOrder>();
            foreach (PurchaseOrder order in orders)
            {
                if (order != null && !order.IsOpen)
                {
                    closed.Add(order);
                }
            }

            if (closed.Count <= MaxClosedPurchaseOrders)
            {
                return 0;
            }

            closed.Sort(ComparePurchaseRecency);
            HashSet<PurchaseOrder> retained = new HashSet<PurchaseOrder>();
            for (int i = 0; i < MaxClosedPurchaseOrders; i++)
            {
                retained.Add(closed[i]);
            }

            return orders.RemoveAll(order =>
                order != null && !order.IsOpen && !retained.Contains(order));
        }

        /// <summary>Newest completion first; no recorded completion is always oldest.</summary>
        private static int CompareSalesRecency(SalesOrder left, SalesOrder right)
        {
            int leftTick = left.completedTick == SalesOrder.NeverCompletedTick
                ? int.MinValue
                : left.completedTick;
            int rightTick = right.completedTick == SalesOrder.NeverCompletedTick
                ? int.MinValue
                : right.completedTick;

            int byTick = rightTick.CompareTo(leftTick);
            return byTick != 0 ? byTick : right.id.CompareTo(left.id);
        }

        /// <summary>Purchase orders have no close tick, so creation time is their chronology.</summary>
        private static int ComparePurchaseRecency(PurchaseOrder left, PurchaseOrder right)
        {
            int byTick = right.orderedTick.CompareTo(left.orderedTick);
            return byTick != 0 ? byTick : right.id.CompareTo(left.id);
        }

        private static HashSet<int> LiveContractOrderIds(List<RecurringContract> contracts)
        {
            HashSet<int> orderIds = new HashSet<int>();
            if (contracts == null)
            {
                return orderIds;
            }

            foreach (RecurringContract contract in contracts)
            {
                if (contract != null && contract.activeOrderId != 0 && !IsConcluded(contract))
                {
                    orderIds.Add(contract.activeOrderId);
                }
            }

            return orderIds;
        }

        private static bool IsConcluded(RecurringContract contract)
        {
            return contract.status == ContractStatus.Completed ||
                   contract.status == ContractStatus.Breached ||
                   contract.status == ContractStatus.Cancelled ||
                   contract.status == ContractStatus.Declined;
        }

        private static bool CompletedInCurrentRefresh(SalesOrder order, int lastRefreshTick)
        {
            return order.status == SalesOrderStatus.Completed &&
                   order.completedTick != SalesOrder.NeverCompletedTick &&
                   order.completedTick >= lastRefreshTick;
        }
    }
}
