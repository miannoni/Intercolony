using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>Sortable columns on the Supplier Market browse surface.</summary>
    internal enum SupplierMarketColumn
    {
        Item,
        Supplier,
        Quantity,
        UnitPrice,
        TotalPayment,
        Fulfillment,
        LeadTime,
        Reason
    }

    /// <summary>
    /// One supplier-market row. The window consumes the already-decided labels and eligibility
    /// state; it does not reconstruct procurement terms while drawing them.
    /// </summary>
    internal readonly struct SupplierMarketRow
    {
        internal readonly SupplierListing listing;
        internal readonly string itemLabel;
        internal readonly string supplierLabel;
        internal readonly int quantityAvailable;
        internal readonly int selectedQuantity;
        internal readonly float unitPrice;
        internal readonly int totalPayment;
        internal readonly string fulfillmentLabel;
        internal readonly int leadTimeDays;
        internal readonly string reasonLabel;
        internal readonly Settlement settlement;
        internal readonly bool canBuy;
        internal readonly string purchaseFailureReason;

        internal SupplierMarketRow(
            SupplierListing listing,
            string itemLabel,
            string supplierLabel,
            int quantityAvailable,
            int selectedQuantity,
            float unitPrice,
            int totalPayment,
            string fulfillmentLabel,
            int leadTimeDays,
            string reasonLabel,
            Settlement settlement,
            bool canBuy,
            string purchaseFailureReason)
        {
            this.listing = listing;
            this.itemLabel = itemLabel;
            this.supplierLabel = supplierLabel;
            this.quantityAvailable = quantityAvailable;
            this.selectedQuantity = selectedQuantity;
            this.unitPrice = unitPrice;
            this.totalPayment = totalPayment;
            this.fulfillmentLabel = fulfillmentLabel;
            this.leadTimeDays = leadTimeDays;
            this.reasonLabel = reasonLabel;
            this.settlement = settlement;
            this.canBuy = canBuy;
            this.purchaseFailureReason = purchaseFailureReason;
        }
    }

    /// <summary>
    /// Read model for the Supplier Market. Listing selection, labels, sorting and purchase
    /// eligibility live here so the browse rows can be inspected without constructing a Window.
    /// </summary>
    internal static class SupplierMarketUiService
    {
        internal static readonly float[] ColumnWidths =
            { 0.20f, 0.14f, 0.07f, 0.09f, 0.10f, 0.10f, 0.07f, 0.13f, 0.10f };

        internal static readonly string[] ColumnLabels =
            { "Item", "Supplier", "Available", "Unit", "Total", "Move", "Lead", "Reason", "" };

        internal const string NotLookedMessage =
            "You have not looked yet. Supplier offers appear after the first market refresh.";

        internal const string NoReachableOffersMessage =
            "No supplier is currently offering anything you can reach.";

        internal static List<SupplierMarketRow> BuildRows(
            IntercolonyWorldComponent state)
        {
            List<SupplierMarketRow> rows = new List<SupplierMarketRow>();
            if (state?.SupplierListings == null)
            {
                return rows;
            }

            Dictionary<int, Settlement> settlementsById = new Dictionary<int, Settlement>();
            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements != null)
            {
                for (int i = 0; i < settlements.Count; i++)
                {
                    Settlement settlement = settlements[i];
                    if (settlement != null)
                    {
                        settlementsById[settlement.ID] = settlement;
                    }
                }
            }

            Map paymentMap = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
            int availableSilver = PurchaseOrderService.CountColonySilver(paymentMap);
            foreach (SupplierListing listing in state.SupplierListings)
            {
                if (listing == null || !listing.IsAvailable)
                {
                    continue;
                }

                if (!settlementsById.TryGetValue(listing.settlementId, out Settlement settlement) ||
                    !IntercolonyMarketAccess.IsAccessible(settlement))
                {
                    continue;
                }

                rows.Add(BuildRow(
                    state, listing, listing.quantityAvailable, settlement, availableSilver));
            }

            return rows;
        }

        internal static SupplierMarketRow BuildRow(
            IntercolonyWorldComponent state,
            SupplierListing listing,
            int selectedQuantity)
        {
            Settlement settlement = listing == null
                ? null
                : IntercolonyMarketAccess.FindSettlement(listing.settlementId);
            Map paymentMap = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
            int availableSilver = PurchaseOrderService.CountColonySilver(paymentMap);
            return BuildRow(state, listing, selectedQuantity, settlement, availableSilver);
        }

        private static SupplierMarketRow BuildRow(
            IntercolonyWorldComponent state,
            SupplierListing listing,
            int selectedQuantity,
            Settlement settlement,
            int availableSilver)
        {
            int available = Mathf.Max(0, listing?.quantityAvailable ?? 0);
            int chosen = Mathf.Clamp(selectedQuantity, available > 0 ? 1 : 0, available);
            string itemLabel = ItemLabel(listing);
            string supplierLabel = settlement == null
                ? "Unknown supplier"
                : settlement.Label.ToString();
            float unitPrice = listing?.unitPrice ?? 0f;
            int totalPayment = IntercolonyPricing.TotalPayment(unitPrice, chosen);
            string fulfillmentLabel = listing?.fulfillment == FulfillmentMode.BuyerPickup
                ? "Pickup"
                : "Delivery";
            string reasonLabel = SupplyReason(state, listing, settlement);
            bool canBuy = SupplierListingService.CanPurchase(
                state, listing, 1, availableSilver, settlement, out string purchaseFailureReason);

            return new SupplierMarketRow(
                listing,
                itemLabel,
                supplierLabel,
                available,
                chosen,
                unitPrice,
                totalPayment,
                fulfillmentLabel,
                listing?.leadTimeDays ?? 0,
                reasonLabel,
                settlement,
                canBuy,
                purchaseFailureReason);
        }

        internal static List<TermRow> BuildConfirmationRows(
            IntercolonyWorldComponent state,
            SupplierListing listing,
            int quantity)
        {
            SupplierMarketRow row = BuildRow(state, listing, quantity);
            string quantityLabel = $"{quantity} of {row.quantityAvailable}";
            string totalLabel = $"{row.totalPayment:N0} silver";
            string qualityTooltip = listing?.quality.HasValue == true
                ? "The supplier promises this quality."
                : null;
            string materialTooltip = listing?.stuffDef != null
                ? "The supplier promises this material."
                : null;

            List<TermRow> terms = new List<TermRow>
            {
                new TermRow("Item", row.itemLabel),
                new TermRow("Supplier", row.supplierLabel),
                new TermRow("Quantity", quantityLabel),
                new TermRow("Unit price", $"{row.unitPrice:F2} silver"),
                new TermRow("Total", totalLabel,
                    "Calculated by the shared payment calculation from the published rate."),
                new TermRow("Fulfilment", row.fulfillmentLabel),
                new TermRow("Lead time", $"{row.leadTimeDays} days"),
                new TermRow("Reason", row.reasonLabel,
                    "The supplier's current local supply condition."),
            };

            if (listing?.quality.HasValue == true)
            {
                terms.Insert(1, new TermRow(
                    "Quality", listing.quality.Value.GetLabel(), qualityTooltip));
            }

            if (listing?.stuffDef != null)
            {
                int materialIndex = listing.quality.HasValue ? 2 : 1;
                terms.Insert(materialIndex, new TermRow(
                    "Material", listing.stuffDef.LabelCap.ToString(), materialTooltip));
            }

            return terms;
        }

        internal static string EmptyState(IntercolonyWorldComponent state)
        {
            bool hasListingRecord = false;
            if (state?.SupplierListings != null)
            {
                foreach (SupplierListing listing in state.SupplierListings)
                {
                    if (listing != null)
                    {
                        hasListingRecord = true;
                        break;
                    }
                }
            }

            if (state != null && state.RefreshCount <= 0 && !hasListingRecord)
            {
                return NotLookedMessage;
            }

            return NoReachableOffersMessage;
        }

        internal static void SortRows(
            List<SupplierMarketRow> rows,
            SupplierMarketColumn column,
            bool descending)
        {
            if (rows == null)
            {
                return;
            }

            Comparison<SupplierMarketRow> comparison;
            switch (column)
            {
                case SupplierMarketColumn.Item:
                    comparison = (a, b) => string.Compare(
                        a.itemLabel, b.itemLabel, StringComparison.CurrentCultureIgnoreCase);
                    break;
                case SupplierMarketColumn.Supplier:
                    comparison = (a, b) => string.Compare(
                        a.supplierLabel, b.supplierLabel, StringComparison.CurrentCultureIgnoreCase);
                    break;
                case SupplierMarketColumn.Quantity:
                    comparison = (a, b) => a.quantityAvailable.CompareTo(b.quantityAvailable);
                    break;
                case SupplierMarketColumn.UnitPrice:
                    comparison = (a, b) => a.unitPrice.CompareTo(b.unitPrice);
                    break;
                case SupplierMarketColumn.Fulfillment:
                    comparison = (a, b) => string.Compare(
                        a.fulfillmentLabel, b.fulfillmentLabel,
                        StringComparison.CurrentCultureIgnoreCase);
                    break;
                case SupplierMarketColumn.LeadTime:
                    comparison = (a, b) => a.leadTimeDays.CompareTo(b.leadTimeDays);
                    break;
                case SupplierMarketColumn.Reason:
                    comparison = (a, b) => string.Compare(
                        a.reasonLabel, b.reasonLabel, StringComparison.CurrentCultureIgnoreCase);
                    break;
                default:
                    comparison = (a, b) => a.totalPayment.CompareTo(b.totalPayment);
                    break;
            }

            MarketTableSortUtility.Sort(
                rows,
                comparison,
                descending,
                (a, b) => (a.listing?.id ?? 0).CompareTo(b.listing?.id ?? 0));
        }

        internal static bool DefaultDescending(SupplierMarketColumn column)
        {
            return column == SupplierMarketColumn.Quantity ||
                   column == SupplierMarketColumn.UnitPrice ||
                   column == SupplierMarketColumn.TotalPayment;
        }

        internal static string HeaderLabel(
            SupplierMarketColumn column, bool active, bool descending)
        {
            string label = ColumnLabels[(int)column];
            return active ? label + (descending ? " v" : " ^") : label;
        }

        internal static string CellLabel(SupplierMarketRow row, SupplierMarketColumn column)
        {
            switch (column)
            {
                case SupplierMarketColumn.Item: return row.itemLabel;
                case SupplierMarketColumn.Supplier: return row.supplierLabel;
                case SupplierMarketColumn.Quantity: return row.quantityAvailable.ToString();
                case SupplierMarketColumn.UnitPrice: return $"{row.unitPrice:F2}";
                case SupplierMarketColumn.TotalPayment: return row.totalPayment.ToString("N0");
                case SupplierMarketColumn.Fulfillment: return row.fulfillmentLabel;
                case SupplierMarketColumn.LeadTime: return $"{row.leadTimeDays}d";
                case SupplierMarketColumn.Reason: return row.reasonLabel;
                default: return "";
            }
        }

        private static string ItemLabel(SupplierListing listing)
        {
            if (listing?.thingDef == null)
            {
                return "<missing item>";
            }

            List<string> specification = new List<string>();
            if (listing.stuffDef != null)
            {
                specification.Add(listing.stuffDef.LabelCap.ToString());
            }

            if (listing.quality.HasValue)
            {
                specification.Add(listing.quality.Value.GetLabel());
            }

            string label = listing.thingDef.LabelCap.ToString();
            return specification.Count == 0
                ? label
                : $"{label} ({string.Join(", ", specification.ToArray())})";
        }

        private static string SupplyReason(
            IntercolonyWorldComponent state,
            SupplierListing listing,
            Settlement settlement)
        {
            if (state == null || listing == null || settlement == null || listing.thingDef == null)
            {
                return "Supply data unavailable";
            }

            IntercolonyProductCategory? category =
                IntercolonyProductClassifier.Classify(listing.thingDef);
            if (!category.HasValue)
            {
                return "Supply data unavailable";
            }

            SettlementEconomicProfile profile = state.GetProfile(settlement);
            if (profile == null)
            {
                return "Supply data unavailable";
            }

            float condition = EffectiveEconomyService.SupplyCondition(
                state, profile, category.Value, settlement);
            if (condition < SettlementMarketState.Neutral)
            {
                return EffectiveEconomyService.ShortageLabel;
            }

            if (condition > SettlementMarketState.Neutral)
            {
                return EffectiveEconomyService.SurplusLabel;
            }

            return "Stable local supply";
        }

        internal static string BuildTooltip(
            IntercolonyWorldComponent state,
            SupplierMarketRow row)
        {
            SupplierListing listing = row.listing;
            if (listing == null)
            {
                return "The supplier listing is no longer available.";
            }

            string categoryDetail = "";
            if (state != null && row.settlement != null && listing.thingDef != null)
            {
                IntercolonyProductCategory? category =
                    IntercolonyProductClassifier.Classify(listing.thingDef);
                SettlementEconomicProfile profile = state.GetProfile(row.settlement);
                if (category.HasValue && profile != null)
                {
                    List<PriceFactor> factors = EffectiveEconomyService.ExplainSupply(
                        state, profile, category.Value, row.settlement);
                    List<string> details = new List<string>();
                    foreach (PriceFactor factor in factors)
                    {
                        if (factor.label == "Local supply")
                        {
                            continue;
                        }

                        details.Add($"{factor.label}: {factor.multiplier:F2}x");
                    }

                    if (details.Count > 0)
                    {
                        categoryDetail = "\n" + string.Join("\n", details.ToArray());
                    }
                }
            }

            return $"{row.itemLabel} from {row.supplierLabel}\n" +
                   $"{listing.quantityAvailable} available at {listing.unitPrice:F2} silver each\n" +
                   $"{(listing.fulfillment == FulfillmentMode.BuyerPickup ? "Pickup" : "Delivery")}, " +
                   $"lead time {listing.leadTimeDays} days\n" +
                   $"{row.reasonLabel}{categoryDetail}";
        }
    }
}
