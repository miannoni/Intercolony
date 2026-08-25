using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Builds a player proposal for a new procurement agreement.
    /// </summary>
    public class Dialog_ProposeProcurementAgreement : Window
    {
        private const float TitleHeight = 30f;
        private const float TitleBottomPadding = 8f;
        private const float SectionLabelHeight = 24f;
        private const float ColumnGap = 12f;
        private const float ContentBottomPadding = 10f;
        private const float ScrollbarWidth = 16f;
        private const float RowHeight = 26f;
        private const float RowHorizontalPadding = 4f;
        private const float RowVerticalPadding = 2f;
        private const float ControlRowHeight = 28f;
        private const float SliderHeight = 20f;
        private const float TermsTopOffset = 224f;
        private const float ButtonWidth = 110f;
        private const float ButtonHeight = 34f;
        private const float TermLabelWidth = 150f;
        private const float TermColumnGap = 8f;
        private const float TermRowGap = 5f;
        private const float TooltipAffordanceWidth = 18f;

        // Mirror the selling side's quadrum rhythm; service minimums are permitted bounds, not sensible opening defaults.
        private const int DefaultCadenceDays = 15;
        private const int DefaultTotalCycles = 4;
        private const FulfillmentMode DefaultFulfillment = FulfillmentMode.SellerDelivery;

        private readonly IntercolonyWorldComponent state;
        private readonly List<Settlement> qualifyingSettlements;
        private readonly Dictionary<int, List<ThingDef>> qualifyingItemsBySettlement;

        private Vector2 settlementScroll;
        private Vector2 itemScroll;
        private Vector2 termsScroll;
        private Settlement selectedSettlement;
        private List<ThingDef> qualifyingItems = new List<ThingDef>();
        private ThingDef selectedItem;
        private int quantity = ProcurementContractService.MinimumQuantityPerCycle;
        private string quantityBuffer = ProcurementContractService.MinimumQuantityPerCycle.ToString();
        private int cadenceDays = DefaultCadenceDays;
        private string cadenceBuffer = DefaultCadenceDays.ToString();
        private int totalCycles = DefaultTotalCycles;
        private string totalCyclesBuffer = DefaultTotalCycles.ToString();
        private float selectedUnitPrice;
        private FulfillmentMode fulfillment = DefaultFulfillment;
        private ProcurementContractTerms selectedTerms;

        public Dialog_ProposeProcurementAgreement(IntercolonyWorldComponent state)
        {
            this.state = state;
            qualifyingItemsBySettlement = new Dictionary<int, List<ThingDef>>();
            qualifyingSettlements = FindQualifyingSettlements(
                state, qualifyingItemsBySettlement);
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize => new Vector2(820f, 620f);

        public override void DoWindowContents(Rect inRect)
        {
            float y = 0f;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, inRect.width, TitleHeight),
                "Propose procurement agreement");
            y += TitleHeight + TitleBottomPadding;
            Text.Font = GameFont.Small;

            float contentBottom = inRect.height - ButtonHeight - ContentBottomPadding;
            float columnWidth = Mathf.Floor((inRect.width - ColumnGap * 2f) / 3f);
            float itemX = columnWidth + ColumnGap;
            float controlsX = itemX + columnWidth + ColumnGap;
            float controlsWidth = inRect.width - controlsX;
            Widgets.Label(new Rect(0f, y, columnWidth, SectionLabelHeight), "Settlement");
            Widgets.Label(new Rect(itemX, y, columnWidth, SectionLabelHeight), "Item");
            Widgets.Label(new Rect(controlsX, y, controlsWidth, SectionLabelHeight),
                "Controls");
            y += SectionLabelHeight;

            Rect settlementRect = new Rect(
                0f, y, columnWidth, Mathf.Max(0f, contentBottom - y));
            if (qualifyingSettlements.Count == 0)
            {
                Widgets.Label(settlementRect, "No eligible settlements.");
            }
            else
            {
                DrawSettlements(settlementRect);
            }

            Rect itemRect = new Rect(
                itemX, y, columnWidth, Mathf.Max(0f, contentBottom - y));
            if (selectedSettlement == null)
            {
                Widgets.Label(itemRect, "Select a settlement.");
            }
            else if (qualifyingItems.Count == 0)
            {
                Widgets.Label(itemRect, "No eligible items.");
            }
            else
            {
                DrawItems(itemRect);
            }

            DrawControls(new Rect(
                controlsX, y, controlsWidth, Mathf.Max(0f, contentBottom - y)));

            DrawTermsSummary(new Rect(
                controlsX, y + TermsTopOffset, controlsWidth,
                Mathf.Max(0f, contentBottom - y - TermsTopOffset)));

            Rect cancelRect = new Rect(
                inRect.width - ButtonWidth, inRect.height - ButtonHeight,
                ButtonWidth, ButtonHeight);
            Rect proposeRect = new Rect(
                cancelRect.x - ColumnGap - ButtonWidth, cancelRect.y,
                ButtonWidth, ButtonHeight);

            bool canPropose = selectedSettlement != null &&
                              selectedItem != null &&
                              selectedTerms != null &&
                              selectedTerms.IsUnitPriceInRange(selectedUnitPrice);
            if (Widgets.ButtonText(proposeRect, "Propose", active: canPropose))
            {
                TryPropose();
            }

            if (Widgets.ButtonText(cancelRect, "Cancel"))
            {
                Close();
            }
        }

        private void DrawSettlements(Rect rect)
        {
            Rect viewRect = new Rect(
                0f, 0f, rect.width - ScrollbarWidth,
                Mathf.Max(rect.height, qualifyingSettlements.Count * RowHeight));

            Widgets.BeginScrollView(rect, ref settlementScroll, viewRect);
            float rowY = 0f;
            foreach (Settlement settlement in qualifyingSettlements)
            {
                Rect row = new Rect(0f, rowY, viewRect.width, RowHeight);
                if (selectedSettlement == settlement)
                {
                    Widgets.DrawHighlightSelected(row);
                }

                Widgets.DrawHighlightIfMouseover(row);
                Rect labelRect = new Rect(
                    row.x + RowHorizontalPadding,
                    row.y + RowVerticalPadding,
                    row.width - RowHorizontalPadding * 2f,
                    RowHeight - RowVerticalPadding * 2f);
                string label = settlement.Label ?? "";
                Widgets.LabelEllipses(labelRect, label);
                if (Text.CalcSize(label).x > labelRect.width)
                {
                    TooltipHandler.TipRegion(labelRect, label);
                }

                if (Widgets.ButtonInvisible(row))
                {
                    SelectSettlement(settlement);
                }

                rowY += RowHeight;
            }
            Widgets.EndScrollView();
        }

        private void DrawItems(Rect rect)
        {
            Rect viewRect = new Rect(
                0f, 0f, rect.width - ScrollbarWidth,
                Mathf.Max(rect.height, qualifyingItems.Count * RowHeight));

            Widgets.BeginScrollView(rect, ref itemScroll, viewRect);
            float rowY = 0f;
            foreach (ThingDef thingDef in qualifyingItems)
            {
                Rect row = new Rect(0f, rowY, viewRect.width, RowHeight);
                if (selectedItem == thingDef)
                {
                    Widgets.DrawHighlightSelected(row);
                }

                Widgets.DrawHighlightIfMouseover(row);
                Rect labelRect = new Rect(
                    row.x + RowHorizontalPadding,
                    row.y + RowVerticalPadding,
                    row.width - RowHorizontalPadding * 2f,
                    RowHeight - RowVerticalPadding * 2f);
                string label = thingDef.LabelCap.ToString();
                Widgets.LabelEllipses(labelRect, label);
                if (Text.CalcSize(label).x > labelRect.width)
                {
                    TooltipHandler.TipRegion(labelRect, label);
                }

                if (Widgets.ButtonInvisible(row))
                {
                    SelectItem(thingDef);
                }

                rowY += RowHeight;
            }
            Widgets.EndScrollView();
        }

        private void DrawControls(Rect rect)
        {
            float y = rect.y;
            float fieldWidth = 72f;

            int previousQuantity = quantity;
            Widgets.Label(new Rect(rect.x, y, rect.width - fieldWidth, ControlRowHeight),
                "Quantity per cycle:");
            Widgets.TextFieldNumeric(
                new Rect(rect.xMax - fieldWidth, y, fieldWidth, ControlRowHeight),
                ref quantity, ref quantityBuffer,
                ProcurementContractService.MinimumQuantityPerCycle,
                ProcurementContractService.MaximumQuantityPerCycle);
            TooltipHandler.TipRegion(
                new Rect(rect.x, y, rect.width, ControlRowHeight),
                $"Units requested from the supplier in each cycle. Range: " +
                $"{ProcurementContractService.MinimumQuantityPerCycle} to " +
                $"{ProcurementContractService.MaximumQuantityPerCycle}.");
            y += ControlRowHeight + 4f;

            int previousCadence = cadenceDays;
            Widgets.Label(new Rect(rect.x, y, rect.width - fieldWidth, ControlRowHeight),
                "Cadence (days):");
            Widgets.TextFieldNumeric(
                new Rect(rect.xMax - fieldWidth, y, fieldWidth, ControlRowHeight),
                ref cadenceDays, ref cadenceBuffer,
                ProcurementContractService.MinimumCadenceDays,
                ProcurementContractService.MaximumCadenceDays);
            TooltipHandler.TipRegion(
                new Rect(rect.x, y, rect.width, ControlRowHeight),
                $"Days between procurement cycles. The total term is capped at " +
                $"{ProcurementContractService.MaximumTermDays} days, so changing cadence " +
                "clamps total cycles to keep the proposal within that limit.");
            y += ControlRowHeight + 4f;

            int previousCycles = totalCycles;
            Widgets.Label(new Rect(rect.x, y, rect.width - fieldWidth, ControlRowHeight),
                "Total cycles:");
            Widgets.TextFieldNumeric(
                new Rect(rect.xMax - fieldWidth, y, fieldWidth, ControlRowHeight),
                ref totalCycles, ref totalCyclesBuffer,
                ProcurementContractService.MinimumTotalCycles,
                ProcurementContractService.MaximumTotalCycles);
            TooltipHandler.TipRegion(
                new Rect(rect.x, y, rect.width, ControlRowHeight),
                $"Number of procurement cycles. Cadence multiplied by total cycles may not " +
                $"exceed {ProcurementContractService.MaximumTermDays} days; the UI clamps " +
                "this value when needed.");
            y += ControlRowHeight + 4f;

            if (quantity != previousQuantity ||
                cadenceDays != previousCadence ||
                totalCycles != previousCycles)
            {
                ClampTermLength();
                RefreshTerms(resetPrice: false);
            }

            Widgets.Label(new Rect(rect.x, y, rect.width, ControlRowHeight), "Fulfilment:");
            y += ControlRowHeight - 4f;
            float choiceWidth = (rect.width - ColumnGap) / 2f;
            Rect deliveryRect = new Rect(rect.x, y, choiceWidth, ControlRowHeight);
            Rect pickupRect = new Rect(
                rect.x + choiceWidth + ColumnGap, y, choiceWidth, ControlRowHeight);
            DrawFulfillmentChoice(
                deliveryRect, "Supplier delivers", FulfillmentMode.SellerDelivery);
            DrawFulfillmentChoice(
                pickupRect, "Buyer pickup", FulfillmentMode.BuyerPickup);
            TooltipHandler.TipRegion(
                deliveryRect,
                "The supplier sends each cycle's goods to the colony.");
            TooltipHandler.TipRegion(
                pickupRect,
                "The colony collects each cycle's goods from the supplier.");
            y += ControlRowHeight + 4f;

            Widgets.Label(new Rect(rect.x, y, rect.width, ControlRowHeight), "Unit price:");
            y += ControlRowHeight + 16f;

            if (selectedTerms == null)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.55f);
                Widgets.Label(new Rect(rect.x, y, rect.width, SliderHeight),
                    "Select an item to set the rate.");
                GUI.color = Color.white;
                return;
            }

            Rect priceRect = new Rect(rect.x, y, rect.width, SliderHeight);
            float slidPrice = Widgets.HorizontalSlider(
                priceRect,
                selectedTerms.unitPrice,
                selectedTerms.minimumUnitPrice,
                selectedTerms.maximumUnitPrice,
                middleAlignment: false,
                label: null,
                leftAlignedLabel: selectedTerms.minimumUnitPrice.ToString("F2"),
                rightAlignedLabel: selectedTerms.maximumUnitPrice.ToString("F2"),
                roundTo: 0.01f);
            slidPrice = Mathf.Clamp(
                slidPrice, selectedTerms.minimumUnitPrice, selectedTerms.maximumUnitPrice);
            TooltipHandler.TipRegion(
                priceRect,
                $"Agreed silver per unit. The previewed range is " +
                $"{selectedTerms.minimumUnitPrice:F2} to {selectedTerms.maximumUnitPrice:F2}; " +
                $"the reference rate is {selectedTerms.referenceUnitPrice:F2}.");
            if (!Mathf.Approximately(slidPrice, selectedTerms.unitPrice))
            {
                selectedUnitPrice = slidPrice;
                RefreshTermsForChosenPrice();
            }
        }

        private void DrawFulfillmentChoice(Rect rect, string label, FulfillmentMode mode)
        {
            bool selected = fulfillment == mode;
            if (Widgets.ButtonText(rect, label, active: true) && !selected)
            {
                fulfillment = mode;
                RefreshTerms(resetPrice: false);
            }

            if (selected)
            {
                Widgets.DrawHighlightSelected(rect);
            }
        }

        private void DrawTermsSummary(Rect rect)
        {
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, SectionLabelHeight),
                "Agreement terms");

            Rect detailsRect = new Rect(
                rect.x, rect.y + SectionLabelHeight, rect.width,
                Mathf.Max(0f, rect.height - SectionLabelHeight));
            if (selectedTerms == null)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.55f);
                Widgets.Label(detailsRect, "Select a settlement and item.");
                GUI.color = Color.white;
                return;
            }

            List<TermRow> rows = BuildTermsRows();
            float rowsHeight = MeasureRows(rows, detailsRect.width);
            if (rowsHeight <= detailsRect.height)
            {
                DrawRows(rows, detailsRect.width, detailsRect.y);
                return;
            }

            float viewWidth = Mathf.Max(1f, detailsRect.width - ScrollbarWidth);
            float viewHeight = MeasureRows(rows, viewWidth);
            Rect viewRect = new Rect(0f, 0f, viewWidth, viewHeight);
            Widgets.BeginScrollView(detailsRect, ref termsScroll, viewRect);
            DrawRows(rows, viewWidth, 0f);
            Widgets.EndScrollView();
        }

        private List<TermRow> BuildTermsRows()
        {
            return new List<TermRow>
            {
                new TermRow("Item", selectedItem.LabelCap.ToString()),
                new TermRow("Supplier", selectedSettlement.Label),
                new TermRow("Quantity per cycle", quantity.ToString("N0")),
                new TermRow("Cadence", $"{cadenceDays:N0} days"),
                new TermRow("Cycles", selectedTerms.totalCycles.ToString("N0")),
                new TermRow("Unit price", $"{selectedTerms.unitPrice:F2} silver",
                    "The agreed silver price for one unit."),
                new TermRow("Payment per cycle", $"{selectedTerms.paymentPerCycle:N0} silver",
                    "The shared payment calculation for one procurement cycle."),
                new TermRow("Total", $"{selectedTerms.totalPayment:N0} silver",
                    "The shared payment calculation across every scheduled cycle."),
                new TermRow("Fulfilment", FulfillmentLabel(fulfillment))
            };
        }

        private static float MeasureRows(List<TermRow> rows, float width)
        {
            float height = 0f;
            for (int i = 0; i < rows.Count; i++)
            {
                height += RowHeightForTerm(rows[i], width);
                if (i < rows.Count - 1)
                {
                    height += TermRowGap;
                }
            }
            return height;
        }

        private static void DrawRows(List<TermRow> rows, float width, float startY)
        {
            float y = startY;
            for (int i = 0; i < rows.Count; i++)
            {
                TermRow row = rows[i];
                float valueWidth = ValueWidthForTerm(row, width);
                float rowHeight = RowHeightForTerm(row, width);
                Rect rowRect = new Rect(0f, y, width, rowHeight);

                float valueX = 0f;
                if (!row.label.NullOrEmpty())
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.65f);
                    Widgets.Label(new Rect(0f, y, TermLabelWidth, rowHeight), row.label);
                    GUI.color = Color.white;
                    valueX = TermLabelWidth + TermColumnGap;
                }
                Widgets.Label(new Rect(valueX, y, valueWidth, rowHeight), row.value ?? "");

                if (!row.tooltip.NullOrEmpty())
                {
                    TooltipHandler.TipRegion(rowRect, row.tooltip);
                    Widgets.DrawHighlightIfMouseover(rowRect);
                    GUI.color = new Color(0.6f, 0.85f, 1f, 0.65f);
                    Text.Anchor = TextAnchor.UpperCenter;
                    Widgets.Label(new Rect(width - TooltipAffordanceWidth, y,
                        TooltipAffordanceWidth, rowHeight), "?");
                    Text.Anchor = TextAnchor.UpperLeft;
                    GUI.color = Color.white;
                }

                y += rowHeight + TermRowGap;
            }
        }

        private static float RowHeightForTerm(TermRow row, float width)
        {
            float valueHeight = Text.CalcHeight(
                row.value ?? "", ValueWidthForTerm(row, width));
            float labelHeight = row.label.NullOrEmpty()
                ? 0f
                : Text.CalcHeight(row.label, TermLabelWidth);
            return Mathf.Max(valueHeight, labelHeight);
        }

        private static float ValueWidthForTerm(TermRow row, float width)
        {
            float valueWidth = row.label.NullOrEmpty()
                ? width
                : width - TermLabelWidth - TermColumnGap;
            valueWidth -= TooltipAffordanceWidth;
            return Mathf.Max(1f, valueWidth);
        }

        private void TryPropose()
        {
            ProcurementContractProposalResult result =
                ProcurementContractService.ProposeContract(
                    state, selectedSettlement, selectedItem, quantity, cadenceDays,
                    totalCycles, selectedUnitPrice, fulfillment);
            if (!result.Success)
            {
                Messages.Message(
                    result.Reason,
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            Close();
        }

        private void SelectSettlement(Settlement settlement)
        {
            if (selectedSettlement == settlement)
            {
                return;
            }

            selectedSettlement = settlement;
            selectedItem = null;
            selectedTerms = null;
            selectedUnitPrice = 0f;
            itemScroll = Vector2.zero;
            termsScroll = Vector2.zero;
            if (!qualifyingItemsBySettlement.TryGetValue(
                    settlement.ID, out qualifyingItems))
            {
                qualifyingItems = new List<ThingDef>();
            }
        }

        private void SelectItem(ThingDef thingDef)
        {
            if (selectedItem == thingDef)
            {
                return;
            }

            selectedItem = thingDef;
            termsScroll = Vector2.zero;
            RefreshTerms(resetPrice: true);
        }

        private void RefreshTerms(bool resetPrice)
        {
            if (selectedSettlement == null || selectedItem == null)
            {
                selectedTerms = null;
                selectedUnitPrice = 0f;
                return;
            }

            ProcurementContractTerms bounds = PreviewTerms();
            if (bounds == null)
            {
                selectedTerms = null;
                selectedUnitPrice = 0f;
                return;
            }

            selectedUnitPrice = resetPrice
                ? bounds.referenceUnitPrice
                : Mathf.Clamp(
                    selectedUnitPrice, bounds.minimumUnitPrice, bounds.maximumUnitPrice);
            RefreshTermsForChosenPrice();
        }

        private void RefreshTermsForChosenPrice()
        {
            selectedTerms = PreviewTerms(selectedUnitPrice);
            if (selectedTerms != null)
            {
                selectedUnitPrice = selectedTerms.unitPrice;
            }
        }

        private ProcurementContractTerms PreviewTerms(float? agreedUnitPrice = null)
        {
            return ProcurementContractService.PreviewContractTerms(
                state, selectedSettlement, selectedItem, null, null, quantity, cadenceDays,
                totalCycles, agreedUnitPrice, fulfillment);
        }

        private void ClampTermLength()
        {
            cadenceDays = Mathf.Clamp(
                cadenceDays,
                ProcurementContractService.MinimumCadenceDays,
                ProcurementContractService.MaximumCadenceDays);
            totalCycles = Mathf.Clamp(
                totalCycles,
                ProcurementContractService.MinimumTotalCycles,
                ProcurementContractService.MaximumTotalCycles);

            int maximumCyclesForCadence = Mathf.Min(
                ProcurementContractService.MaximumTotalCycles,
                ProcurementContractService.MaximumTermDays / cadenceDays);
            if (totalCycles > maximumCyclesForCadence)
            {
                totalCycles = maximumCyclesForCadence;
            }

            cadenceBuffer = cadenceDays.ToString();
            totalCyclesBuffer = totalCycles.ToString();
        }

        private static string FulfillmentLabel(FulfillmentMode mode)
        {
            return mode == FulfillmentMode.BuyerPickup
                ? "Buyer pickup"
                : "Supplier delivery";
        }

        private static List<Settlement> FindQualifyingSettlements(
            IntercolonyWorldComponent state,
            Dictionary<int, List<ThingDef>> qualifyingItemsBySettlement)
        {
            List<Settlement> result = new List<Settlement>();
            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (state == null || settlements == null)
            {
                return result;
            }

            foreach (Settlement settlement in settlements)
            {
                if (!IntercolonyMarketAccess.IsAccessible(settlement, out _))
                {
                    continue;
                }

                List<ThingDef> qualifyingItems = new List<ThingDef>();
                foreach (ThingDef thingDef in CandidateThingDefs(state, settlement.ID))
                {
                    if (ProcurementContractService.PreviewContractTerms(
                            state, settlement, thingDef, null, null,
                            ProcurementContractService.MinimumQuantityPerCycle,
                            DefaultCadenceDays, DefaultTotalCycles,
                            agreedUnitPrice: null,
                            fulfillment: DefaultFulfillment) != null)
                    {
                        qualifyingItems.Add(thingDef);
                    }
                }

                if (qualifyingItems.Count == 0)
                {
                    continue;
                }

                qualifyingItems.Sort((a, b) => string.Compare(
                    a.LabelCap.ToString(), b.LabelCap.ToString(),
                    StringComparison.CurrentCultureIgnoreCase));
                qualifyingItemsBySettlement.Add(settlement.ID, qualifyingItems);
                result.Add(settlement);
            }

            result.Sort((a, b) => string.Compare(
                a.Label, b.Label, StringComparison.CurrentCultureIgnoreCase));
            return result;
        }

        private static HashSet<ThingDef> CandidateThingDefs(
            IntercolonyWorldComponent state, int settlementId)
        {
            HashSet<ThingDef> result = new HashSet<ThingDef>();
            if (state.CommercialHistory != null)
            {
                foreach (CommercialHistoryEntry entry in state.CommercialHistory)
                {
                    if (entry != null && entry.settlementId == settlementId &&
                        entry.thingDef != null)
                    {
                        result.Add(entry.thingDef);
                    }
                }
            }

            if (state.SupplierListings != null)
            {
                foreach (SupplierListing listing in state.SupplierListings)
                {
                    if (listing != null && listing.settlementId == settlementId &&
                        listing.thingDef != null)
                    {
                        result.Add(listing.thingDef);
                    }
                }
            }

            return result;
        }
    }
}
