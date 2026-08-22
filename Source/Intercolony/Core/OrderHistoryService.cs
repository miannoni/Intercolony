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

        /// <summary>
        /// Keeps concluded request detail at the same depth as the other two order histories.
        /// Three different caps would each need a separate justification and would otherwise
        /// give the player inconsistent history depth for no reason. The Orders page shows 25
        /// rows at a time, so 100 entries already provide four screens of history. This cap is
        /// for bounded save growth, not durable history depth; commercial totals live separately
        /// on the world component, so pruning this detail loses no aggregate used to judge the player.
        /// </summary>
        public const int MaxConcludedPurchaseRequests = 100;

        /// <summary>Prunes closed order detail beyond the three retention caps.</summary>
        public static int Prune(IntercolonyWorldComponent state)
        {
            if (state == null)
            {
                return 0;
            }

            return PruneSalesOrders(state) +
                   PrunePurchaseOrders(state) +
                   PruneConcludedRequests(state);
        }

        public static int CountClearableSalesOrderHistory(IntercolonyWorldComponent state)
        {
            if (state?.Orders == null)
            {
                return 0;
            }

            HashSet<int> contractOrderIds = LiveContractOrderIds(state.Contracts);
            int count = 0;
            foreach (SalesOrder order in state.Orders)
            {
                if (MayRemoveSalesOrder(order, state.LastRefreshTick, contractOrderIds))
                {
                    count++;
                }
            }

            return count;
        }

        public static int ClearSalesOrderHistory(IntercolonyWorldComponent state)
        {
            if (state?.Orders == null)
            {
                return 0;
            }

            HashSet<int> contractOrderIds = LiveContractOrderIds(state.Contracts);
            return state.Orders.RemoveAll(order =>
                MayRemoveSalesOrder(order, state.LastRefreshTick, contractOrderIds));
        }

        public static int CountClearablePurchaseOrderHistory(IntercolonyWorldComponent state)
        {
            if (state?.PurchaseOrders == null)
            {
                return 0;
            }

            int count = 0;
            foreach (PurchaseOrder order in state.PurchaseOrders)
            {
                if (MayRemovePurchaseOrder(order))
                {
                    count++;
                }
            }

            return count;
        }

        public static int ClearPurchaseOrderHistory(IntercolonyWorldComponent state)
        {
            return state?.PurchaseOrders?.RemoveAll(MayRemovePurchaseOrder) ?? 0;
        }

        public static int CountClearablePurchaseRequestHistory(IntercolonyWorldComponent state)
        {
            if (state?.Requests == null)
            {
                return 0;
            }

            HashSet<int> purchaseOrderRequestIds = PurchaseOrderRequestIds(state.PurchaseOrders);
            int count = 0;
            foreach (PurchaseRequest request in state.Requests)
            {
                if (MayRemovePurchaseRequest(request, purchaseOrderRequestIds))
                {
                    count++;
                }
            }

            return count;
        }

        public static int ClearPurchaseRequestHistory(IntercolonyWorldComponent state)
        {
            if (state?.Requests == null)
            {
                return 0;
            }

            HashSet<int> purchaseOrderRequestIds = PurchaseOrderRequestIds(state.PurchaseOrders);
            return state.Requests.RemoveAll(request =>
                MayRemovePurchaseRequest(request, purchaseOrderRequestIds));
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
                !retained.Contains(order) &&
                MayRemoveSalesOrder(order, state.LastRefreshTick, contractOrderIds));
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
                !retained.Contains(order) && MayRemovePurchaseOrder(order));
        }

        private static int PruneConcludedRequests(IntercolonyWorldComponent state)
        {
            List<PurchaseRequest> requests = state.Requests;
            if (requests == null || requests.Count == 0)
            {
                return 0;
            }

            HashSet<int> purchaseOrderRequestIds = PurchaseOrderRequestIds(state.PurchaseOrders);
            List<PurchaseRequest> concluded = new List<PurchaseRequest>();
            foreach (PurchaseRequest request in requests)
            {
                if (MayRemovePurchaseRequest(request, purchaseOrderRequestIds))
                {
                    concluded.Add(request);
                }
            }

            if (concluded.Count <= MaxConcludedPurchaseRequests)
            {
                return 0;
            }

            concluded.Sort(CompareRequestRecency);
            HashSet<PurchaseRequest> retained = new HashSet<PurchaseRequest>();
            for (int i = 0; i < MaxConcludedPurchaseRequests; i++)
            {
                retained.Add(concluded[i]);
            }

            return requests.RemoveAll(request =>
                !retained.Contains(request) &&
                MayRemovePurchaseRequest(request, purchaseOrderRequestIds));
        }

        private static bool MayRemoveSalesOrder(
            SalesOrder order, int lastRefreshTick, HashSet<int> contractOrderIds)
        {
            return order != null &&
                   !order.IsOpen &&
                   !contractOrderIds.Contains(order.id) &&
                   !CompletedInCurrentRefresh(order, lastRefreshTick);
        }

        private static bool MayRemovePurchaseOrder(PurchaseOrder order)
        {
            return order != null && !order.IsOpen;
        }

        private static bool MayRemovePurchaseRequest(
            PurchaseRequest request, HashSet<int> purchaseOrderRequestIds)
        {
            return request != null &&
                   !request.IsOpen &&
                   !purchaseOrderRequestIds.Contains(request.id);
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

        /// <summary>
        /// Purchase requests have no concluded tick, so creation order is a proxy for recency.
        /// This can drop an old request that concluded recently before a newly created request,
        /// and that imprecision is accepted rather than unnoticed: persisting a concluded-tick
        /// field and bumping the save schema for a pruning nicety would be disproportionate.
        /// </summary>
        private static int CompareRequestRecency(PurchaseRequest left, PurchaseRequest right)
        {
            int byTick = right.createdTick.CompareTo(left.createdTick);
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

        private static HashSet<int> PurchaseOrderRequestIds(List<PurchaseOrder> orders)
        {
            HashSet<int> requestIds = new HashSet<int>();
            if (orders == null)
            {
                return requestIds;
            }

            foreach (PurchaseOrder order in orders)
            {
                if (order != null && order.requestId != 0)
                {
                    requestIds.Add(order.requestId);
                }
            }

            return requestIds;
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
