using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Builds a player proposal for a new supply agreement.
    /// </summary>
    public class Dialog_ProposeAgreement : Window
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
        private const float TermsTopOffset = 316f;
        private const float ButtonWidth = 110f;
        private const float ButtonHeight = 34f;
        private const float TermLabelWidth = 150f;
        private const float TermColumnGap = 8f;
        private const float TermRowGap = 5f;
        private const float TooltipAffordanceWidth = 18f;

        // Mirror the selling side's quadrum rhythm; service minimums are permitted bounds, not sensible opening defaults.
        private const int DefaultCadenceDays = 15;
        private const int DefaultTotalDeliveries = 4;
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
        private int quantity = ContractService.MinimumQuantityPerCycle;
        private string quantityBuffer = ContractService.MinimumQuantityPerCycle.ToString();
        private int cadenceDays = DefaultCadenceDays;
        private string cadenceBuffer = DefaultCadenceDays.ToString();
        private int totalDeliveries = DefaultTotalDeliveries;
        private string totalDeliveriesBuffer = DefaultTotalDeliveries.ToString();
        private float selectedUnitPrice;
        private FulfillmentMode fulfillment = DefaultFulfillment;
        private ContractTerms selectedTerms;

        public Dialog_ProposeAgreement(IntercolonyWorldComponent state)
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
                "Propose supply agreement");
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

            Rect settlementRect = new Rect(0f, y, columnWidth, contentBottom - y);
            if (qualifyingSettlements.Count == 0)
            {
                Widgets.Label(settlementRect,
                    "No settlements are eligible for a supply agreement.");
            }
            else
            {
                DrawSettlements(settlementRect);
            }

            Rect itemRect = new Rect(itemX, y, columnWidth, contentBottom - y);
            if (selectedSettlement == null)
            {
                Widgets.Label(itemRect, "Select a settlement to see eligible items.");
            }
            else
            {
                DrawItems(itemRect);
            }

            DrawQuantityAndPrice(new Rect(
                controlsX, y, controlsWidth, contentBottom - y));

            DrawTermsSummary(new Rect(
                controlsX, y + TermsTopOffset, controlsWidth,
                contentBottom - y - TermsTopOffset));

            Rect cancelRect = new Rect(
                inRect.width - ButtonWidth, inRect.height - ButtonHeight,
                ButtonWidth, ButtonHeight);
            Rect proposeRect = new Rect(
                cancelRect.x - ColumnGap - ButtonWidth, cancelRect.y,
                ButtonWidth, ButtonHeight);
            Widgets.Label(new Rect(
                    0f, proposeRect.y + 6f, proposeRect.x - ColumnGap, ButtonHeight),
                "The proposal is sent to the settlement, and they will answer later.");

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

        private void DrawQuantityAndPrice(Rect rect)
        {
            float y = rect.y;
            Widgets.Label(new Rect(rect.x, y, rect.width - 78f, ControlRowHeight),
                "Quantity per delivery:");

            int previousQuantity = quantity;
            Widgets.TextFieldNumeric(
                new Rect(rect.xMax - 72f, y, 72f, ControlRowHeight),
                ref quantity, ref quantityBuffer,
                ContractService.MinimumQuantityPerCycle,
                ContractService.MaximumQuantityPerCycle);

            y += ControlRowHeight + 4f;
            int slidQuantity = Mathf.RoundToInt(Widgets.HorizontalSlider(
                new Rect(rect.x, y, rect.width, SliderHeight),
                quantity,
                ContractService.MinimumQuantityPerCycle,
                ContractService.MaximumQuantityPerCycle));
            if (slidQuantity != quantity)
            {
                quantity = Mathf.Clamp(
                    slidQuantity,
                    ContractService.MinimumQuantityPerCycle,
                    ContractService.MaximumQuantityPerCycle);
                quantityBuffer = quantity.ToString();
            }

            y += SliderHeight + 22f;

            int previousCadence = cadenceDays;
            Widgets.Label(new Rect(rect.x, y, rect.width - 72f, ControlRowHeight),
                "Cadence (days):");
            Widgets.TextFieldNumeric(
                new Rect(rect.xMax - 72f, y, 72f, ControlRowHeight),
                ref cadenceDays, ref cadenceBuffer,
                ProcurementContractService.MinimumCadenceDays,
                ProcurementContractService.MaximumCadenceDays);
            TooltipHandler.TipRegion(
                new Rect(rect.x, y, rect.width, ControlRowHeight),
                $"Days between deliveries. The total term is capped at " +
                $"{ProcurementContractService.MaximumTermDays} days, so changing cadence " +
                "clamps total deliveries to keep the proposal within that limit.");
            y += ControlRowHeight + 4f;

            int previousDeliveries = totalDeliveries;
            Widgets.Label(new Rect(rect.x, y, rect.width - 72f, ControlRowHeight),
                "Total deliveries:");
            Widgets.TextFieldNumeric(
                new Rect(rect.xMax - 72f, y, 72f, ControlRowHeight),
                ref totalDeliveries, ref totalDeliveriesBuffer,
                ProcurementContractService.MinimumTotalCycles,
                ProcurementContractService.MaximumTotalCycles);
            TooltipHandler.TipRegion(
                new Rect(rect.x, y, rect.width, ControlRowHeight),
                $"Number of deliveries. Cadence multiplied by total deliveries may not " +
                $"exceed {ProcurementContractService.MaximumTermDays} days; the UI clamps " +
                "this value when needed.");
            y += ControlRowHeight + 4f;

            if (quantity != previousQuantity ||
                cadenceDays != previousCadence ||
                totalDeliveries != previousDeliveries)
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
                deliveryRect, "You deliver", FulfillmentMode.SellerDelivery);
            DrawFulfillmentChoice(
                pickupRect, "They collect", FulfillmentMode.BuyerPickup);
            TooltipHandler.TipRegion(
                deliveryRect,
                "You deliver each delivery by caravan.");
            TooltipHandler.TipRegion(
                pickupRect,
                "They collect each delivery, so no caravan is needed.");
            y += ControlRowHeight + 4f;

            Widgets.Label(new Rect(rect.x, y, rect.width, ControlRowHeight), "Unit price:");
            // The slider's endpoint labels are drawn just above its rail.
            y += ControlRowHeight + 16f;

            if (selectedSettlement == null || selectedItem == null || selectedTerms == null)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.55f);
                Widgets.Label(new Rect(rect.x, y, rect.width, 48f),
                    "Select a settlement and item to set the rate.");
                GUI.color = Color.white;
                return;
            }

            float slidPrice = Widgets.HorizontalSlider(
                new Rect(rect.x, y, rect.width, SliderHeight),
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
                new Rect(rect.x, y, rect.width, SliderHeight),
                $"Agreed silver per unit. The previewed range is " +
                $"{selectedTerms.minimumUnitPrice:F2} to {selectedTerms.maximumUnitPrice:F2}; " +
                $"the reference rate is {selectedTerms.referenceUnitPrice:F2}.");
            if (!Mathf.Approximately(slidPrice, selectedTerms.unitPrice))
            {
                selectedUnitPrice = slidPrice;
                RefreshTermsForChosenPrice();
            }

            y += SliderHeight + 20f;
            string advice = PriceAdvice();
            float adviceHeight = Mathf.Max(1f, Text.CalcHeight(advice, rect.width));
            Widgets.Label(new Rect(rect.x, y, rect.width, adviceHeight), advice);
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
                Widgets.Label(detailsRect,
                    "Select a settlement and item to preview the terms.");
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
            float daysBetweenDeliveries =
                selectedTerms.cadenceTicks / (float)GenDate.TicksPerDay;
            return new List<TermRow>
            {
                new TermRow("Item", selectedItem.LabelCap.ToString()),
                new TermRow("Settlement", selectedSettlement.Label),
                new TermRow("Quantity per delivery", quantity.ToString("N0"),
                    "Units sold to the settlement in each delivery."),
                new TermRow("Cadence", $"{daysBetweenDeliveries:F0} days",
                    $"Days between deliveries. The full term is capped at " +
                    $"{ProcurementContractService.MaximumTermDays} days."),
                new TermRow("Deliveries", selectedTerms.deliveryCount.ToString("N0"),
                    "Number of deliveries promised by this agreement."),
                new TermRow("Unit price", $"{selectedTerms.unitPrice:F2} silver",
                    "The agreed silver price for one unit."),
                new TermRow("Payment per delivery",
                    $"{selectedTerms.paymentPerDelivery:N0} silver",
                    "The shared payment calculation for one delivery."),
                new TermRow("Total", $"{selectedTerms.totalPayment:N0} silver",
                    "The shared payment calculation across every promised delivery."),
                new TermRow("Fulfilment", FulfillmentLabel(fulfillment),
                    fulfillment == FulfillmentMode.BuyerPickup
                        ? "They collect each delivery, so no caravan is needed."
                        : "You deliver each delivery by caravan.")
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
            ContractProposalResult result = ContractService.ProposeContract(
                state, selectedSettlement, selectedItem, quantity, cadenceDays,
                totalDeliveries, selectedUnitPrice, fulfillment);
            if (!result.Success)
            {
                Messages.Message(
                    result.Reason,
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            MainTabWindow_Intercolony mainTab =
                Find.WindowStack.WindowOfType<MainTabWindow_Intercolony>();
            mainTab?.InvalidateContractProposalSettlementCache();
            Close();
        }

        private string PriceAdvice()
        {
            string rate = $"Rate {selectedTerms.unitPrice:F2}; going rate " +
                          $"{selectedTerms.referenceUnitPrice:F2} silver each.\n";
            if (selectedTerms.unitPrice < selectedTerms.referenceUnitPrice &&
                !Mathf.Approximately(
                    selectedTerms.unitPrice, selectedTerms.referenceUnitPrice))
            {
                return rate + "Generous: improves standing with the buyer's faction.";
            }

            if (selectedTerms.unitPrice > selectedTerms.referenceUnitPrice &&
                !Mathf.Approximately(
                    selectedTerms.unitPrice, selectedTerms.referenceUnitPrice))
            {
                return rate + "Greedy: costs standing with the buyer's faction.";
            }

            return rate + "At the going rate: no standing change.";
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

            ContractTerms bounds = PreviewTerms();
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

        private ContractTerms PreviewTerms(float? agreedUnitPrice = null)
        {
            return ContractService.PreviewContractTerms(
                state, selectedSettlement, selectedItem, quantity, cadenceDays,
                totalDeliveries, agreedUnitPrice, fulfillment);
        }

        private void ClampTermLength()
        {
            cadenceDays = Mathf.Clamp(
                cadenceDays,
                ProcurementContractService.MinimumCadenceDays,
                ProcurementContractService.MaximumCadenceDays);
            totalDeliveries = Mathf.Clamp(
                totalDeliveries,
                ProcurementContractService.MinimumTotalCycles,
                ProcurementContractService.MaximumTotalCycles);

            int maximumDeliveriesForCadence = Mathf.Min(
                ProcurementContractService.MaximumTotalCycles,
                ProcurementContractService.MaximumTermDays / cadenceDays);
            if (totalDeliveries > maximumDeliveriesForCadence)
            {
                totalDeliveries = maximumDeliveriesForCadence;
            }

            cadenceBuffer = cadenceDays.ToString();
            totalDeliveriesBuffer = totalDeliveries.ToString();
        }

        private static string FulfillmentLabel(FulfillmentMode mode)
        {
            return mode == FulfillmentMode.BuyerPickup
                ? "They collect"
                : "You deliver";
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
                List<ThingDef> qualifyingItems = new List<ThingDef>();
                HashSet<ThingDef> seen = new HashSet<ThingDef>();
                foreach (CommercialHistoryEntry entry in state.CommercialHistory)
                {
                    ThingDef thingDef = entry?.thingDef;
                    if (entry == null || entry.settlementId != settlement.ID ||
                        thingDef == null || !seen.Add(thingDef))
                    {
                        continue;
                    }

                    if (ContractService.PreviewContractTerms(
                            state, settlement, thingDef,
                            ContractService.MinimumQuantityPerCycle,
                            DefaultCadenceDays, DefaultTotalDeliveries,
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
    }
}
