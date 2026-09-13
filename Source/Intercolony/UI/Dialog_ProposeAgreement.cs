using System;
using System.Collections.Generic;
using System.Text;
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
        private const FulfillmentMode DefaultFulfillment = FulfillmentMode.BuyerPickup;

        private readonly IntercolonyWorldComponent state;
        private readonly List<Settlement> qualifyingSettlements;
        private readonly Dictionary<int, List<ThingDef>> qualifyingItemsBySettlement;
        private readonly Dictionary<ThingDef, List<Settlement>> qualifyingSettlementsByItem;
        private readonly List<ThingDef> qualifyingItems;

        private Vector2 settlementScroll;
        private Vector2 itemScroll;
        private Vector2 termsScroll;
        private Settlement selectedSettlement;
        private readonly List<Settlement> selectedItemSettlements =
            new List<Settlement>();
        private readonly Dictionary<int, ContractTerms> selectedItemSettlementTerms =
            new Dictionary<int, ContractTerms>();
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
        private IntercolonyNegotiationAcceptancePreview selectedAcceptancePreview;

        public Dialog_ProposeAgreement(IntercolonyWorldComponent state)
        {
            this.state = state;
            qualifyingItemsBySettlement = new Dictionary<int, List<ThingDef>>();
            qualifyingSettlements = FindQualifyingSettlements(
                state, qualifyingItemsBySettlement);
            qualifyingSettlementsByItem = InvertQualifyingItemsBySettlement(
                qualifyingSettlements, qualifyingItemsBySettlement);
            qualifyingItems = FindQualifyingItems(qualifyingSettlementsByItem);
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
            Widgets.Label(new Rect(0f, y, columnWidth, SectionLabelHeight), "Item");
            Widgets.Label(new Rect(itemX, y, columnWidth, SectionLabelHeight), "Settlement");
            Widgets.Label(new Rect(controlsX, y, controlsWidth, SectionLabelHeight),
                "Controls");
            y += SectionLabelHeight;

            Rect itemRect = new Rect(0f, y, columnWidth, contentBottom - y);
            if (qualifyingItems.Count == 0)
            {
                Widgets.Label(itemRect,
                    "No items are eligible for a supply agreement.");
            }
            else
            {
                DrawItems(itemRect);
            }

            Rect settlementRect = new Rect(itemX, y, columnWidth, contentBottom - y);
            if (selectedItem == null)
            {
                Widgets.Label(settlementRect,
                    "Select an item to see eligible settlements.");
            }
            else
            {
                DrawSettlements(settlementRect);
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
                Mathf.Max(rect.height, selectedItemSettlements.Count * RowHeight));

            Widgets.BeginScrollView(rect, ref settlementScroll, viewRect);
            float rowY = 0f;
            foreach (Settlement settlement in selectedItemSettlements)
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
                ContractTerms preview = selectedItemSettlementTerms[settlement.ID];
                string settlementLabel = settlement.Label ?? "";
                string label = $"{settlementLabel}: " +
                               $"{preview.referenceUnitPrice:F2} silver/unit";
                float labelHeight = Text.CalcHeight(label, labelRect.width);
                Widgets.LabelEllipses(labelRect, label);
                if (labelHeight > labelRect.height ||
                    Text.CalcSize(label).x > labelRect.width)
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
                "You take the goods to the buyer each delivery, so your caravan is needed.");
            TooltipHandler.TipRegion(
                pickupRect,
                "The buyer collects the goods each delivery, so no caravan of yours is needed.");
            y += ControlRowHeight + 4f;

            if (selectedSettlement == null || selectedItem == null || selectedTerms == null)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.55f);
                Widgets.Label(new Rect(rect.x, y, rect.width, 48f),
                    "Select a settlement and item to set the rate.");
                GUI.color = Color.white;
                return;
            }

            string priceLabel = $"Unit price: {selectedTerms.unitPrice:F2} silver";
            float priceLabelHeight = Text.CalcHeight(priceLabel, rect.width);
            float priceControlHeight = Mathf.Max(SliderHeight, priceLabelHeight + 10f);
            float slidPrice = Widgets.HorizontalSlider(
                new Rect(rect.x, y, rect.width, priceControlHeight),
                selectedTerms.unitPrice,
                selectedTerms.minimumUnitPrice,
                selectedTerms.maximumUnitPrice,
                middleAlignment: false,
                label: priceLabel,
                leftAlignedLabel: selectedTerms.minimumUnitPrice.ToString("F2"),
                rightAlignedLabel: selectedTerms.maximumUnitPrice.ToString("F2"),
                roundTo: 0.01f);
            slidPrice = Mathf.Clamp(
                slidPrice, selectedTerms.minimumUnitPrice, selectedTerms.maximumUnitPrice);
            TooltipHandler.TipRegion(
                new Rect(rect.x, y, rect.width, priceControlHeight),
                $"Agreed silver per unit. The previewed range is " +
                $"{selectedTerms.minimumUnitPrice:F2} to {selectedTerms.maximumUnitPrice:F2}; " +
                $"the reference rate is {selectedTerms.referenceUnitPrice:F2}.");
            if (!Mathf.Approximately(slidPrice, selectedTerms.unitPrice))
            {
                selectedUnitPrice = slidPrice;
                RefreshTermsForChosenPrice();
            }

            y += priceControlHeight + 20f;
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
                DrawRows(rows, detailsRect.width, detailsRect.x, detailsRect.y);
                return;
            }

            float viewWidth = Mathf.Max(1f, detailsRect.width - ScrollbarWidth);
            float viewHeight = MeasureRows(rows, viewWidth);
            Rect viewRect = new Rect(0f, 0f, viewWidth, viewHeight);
            Widgets.BeginScrollView(detailsRect, ref termsScroll, viewRect);
            DrawRows(rows, viewWidth, 0f, 0f);
            Widgets.EndScrollView();
        }

        private List<TermRow> BuildTermsRows()
        {
            List<TermRow> rows = new List<TermRow>
            {
                new TermRow("Payment per delivery",
                    $"{selectedTerms.paymentPerDelivery:N0} silver",
                    "The shared payment calculation for one delivery."),
                new TermRow("Total", $"{selectedTerms.totalPayment:N0} silver",
                    "The shared payment calculation across every promised delivery.")
            };

            EffectiveBrandService.EffectiveBrandDetails brandDetails =
                EffectiveBrandService.GetEffectiveBrandDetails(state, selectedItem);
            if (brandDetails.hasDirectRecord || brandDetails.inheritedFrom != null)
            {
                rows.Add(new TermRow(
                    "Brand",
                    BrandStrengthLabel(brandDetails.effectiveBrand),
                    BrandTooltip(brandDetails, selectedItem)));
            }

            if (selectedAcceptancePreview != null)
            {
                rows.Add(new TermRow(
                    "Acceptance",
                    AcceptanceLabel(selectedAcceptancePreview),
                    AcceptanceTooltip(selectedAcceptancePreview)));
            }

            return rows;
        }

        private static string BrandStrengthLabel(float effectiveBrand)
        {
            int rounded = Mathf.RoundToInt(effectiveBrand);
            return rounded > 0 ? $"+{rounded}" : rounded.ToString();
        }

        private static string BrandTooltip(
            EffectiveBrandService.EffectiveBrandDetails details, ThingDef product)
        {
            if (details.hasDirectRecord && details.inheritedFrom == null)
            {
                return "Based on your colony's own delivered-quality record for this product.";
            }

            if (!details.hasDirectRecord)
            {
                string sourceLabel = details.inheritedFrom.LabelCap.ToString();
                return $"Inherited from your colony's delivered-quality record for related " +
                       $"{sourceLabel} goods. Product similarity connects that standing to " +
                       $"{product.LabelCap}.";
            }

            string inheritedLabel = details.inheritedFrom.LabelCap.ToString();
            return details.mostlyInherited
                ? $"Mostly inherited from your colony's delivered-quality record for related " +
                  $"{inheritedLabel} goods; your colony also has its own record for this " +
                  $"product."
                : $"Based mainly on your colony's own delivered-quality record for this " +
                  $"product; related {inheritedLabel} evidence also contributes.";
        }

        private static string AcceptanceLabel(
            IntercolonyNegotiationAcceptancePreview preview)
        {
            string band = IntercolonyNegotiationEvaluator.AcceptanceBandLabel(preview.Band);
            if (!IntercolonyMod.Settings.showProposalAppealPercentage)
            {
                return band;
            }

            return $"{band} ({preview.ProposalAppeal.ToStringPercent("F0")})";
        }

        private static string AcceptanceTooltip(
            IntercolonyNegotiationAcceptancePreview preview)
        {
            StringBuilder tooltip = new StringBuilder(
                "The settlement's answer is rolled against a chance derived from this appeal.\n\n" +
                "What drives this estimate:");
            if (preview.Factors == null || preview.Factors.Count == 0)
            {
                tooltip.Append("\n- No named factors are available.");
                return tooltip.ToString();
            }

            foreach (IntercolonyNegotiationFactor factor in preview.Factors)
            {
                if (factor.label.NullOrEmpty())
                {
                    continue;
                }

                tooltip.Append("\n- ").Append(factor.label);
                if (!factor.detail.NullOrEmpty())
                {
                    tooltip.Append(": ").Append(factor.detail);
                }
            }

            return tooltip.ToString();
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

        private static void DrawRows(
            List<TermRow> rows, float width, float startX, float startY)
        {
            float y = startY;
            for (int i = 0; i < rows.Count; i++)
            {
                TermRow row = rows[i];
                float valueWidth = ValueWidthForTerm(row, width);
                float rowHeight = RowHeightForTerm(row, width);
                Rect rowRect = new Rect(startX, y, width, rowHeight);

                float valueX = startX;
                if (!row.label.NullOrEmpty())
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.65f);
                    Widgets.Label(new Rect(startX, y, TermLabelWidth, rowHeight), row.label);
                    GUI.color = Color.white;
                    valueX = startX + TermLabelWidth + TermColumnGap;
                }
                Widgets.Label(new Rect(valueX, y, valueWidth, rowHeight), row.value ?? "");

                if (!row.tooltip.NullOrEmpty())
                {
                    TooltipHandler.TipRegion(rowRect, row.tooltip);
                    Widgets.DrawHighlightIfMouseover(rowRect);
                    GUI.color = new Color(0.6f, 0.85f, 1f, 0.65f);
                    Text.Anchor = TextAnchor.UpperCenter;
                    Widgets.Label(new Rect(startX + width - TooltipAffordanceWidth, y,
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
            ApplyCachedTermsForSelectedSettlement();
        }

        private void SelectItem(ThingDef thingDef)
        {
            if (selectedItem == thingDef)
            {
                return;
            }

            selectedItem = thingDef;
            selectedSettlement = null;
            ClearSelectedTerms();
            settlementScroll = Vector2.zero;
            RefreshSettlementPreviews();
        }

        private void RefreshTerms(bool resetPrice)
        {
            RefreshSettlementPreviews();
            if (selectedSettlement == null || selectedItem == null)
            {
                ClearSelectedTerms();
                return;
            }

            if (!selectedItemSettlementTerms.TryGetValue(
                    selectedSettlement.ID, out ContractTerms bounds))
            {
                ClearSelectedTerms();
                return;
            }

            selectedUnitPrice = resetPrice
                ? bounds.referenceUnitPrice
                : Mathf.Clamp(
                    selectedUnitPrice, bounds.minimumUnitPrice, bounds.maximumUnitPrice);
            RefreshTermsForChosenPrice();
        }

        private void RefreshSettlementPreviews()
        {
            selectedItemSettlements.Clear();
            selectedItemSettlementTerms.Clear();
            if (selectedItem == null ||
                !qualifyingSettlementsByItem.TryGetValue(
                    selectedItem, out List<Settlement> settlements))
            {
                return;
            }

            foreach (Settlement settlement in settlements)
            {
                ContractTerms preview = ContractService.PreviewContractTerms(
                    state, settlement, selectedItem, quantity, cadenceDays,
                    totalDeliveries, agreedUnitPrice: null, fulfillment: fulfillment);
                if (preview == null)
                {
                    continue;
                }

                selectedItemSettlementTerms.Add(settlement.ID, preview);
                selectedItemSettlements.Add(settlement);
            }

            selectedItemSettlements.Sort((a, b) =>
            {
                ContractTerms aTerms = selectedItemSettlementTerms[a.ID];
                ContractTerms bTerms = selectedItemSettlementTerms[b.ID];
                int rateComparison = bTerms.referenceUnitPrice.CompareTo(
                    aTerms.referenceUnitPrice);
                return rateComparison != 0
                    ? rateComparison
                    : string.Compare(
                        a.Label, b.Label, StringComparison.CurrentCultureIgnoreCase);
            });
        }

        private void ApplyCachedTermsForSelectedSettlement()
        {
            if (selectedSettlement == null || selectedItem == null ||
                !selectedItemSettlementTerms.TryGetValue(
                    selectedSettlement.ID, out selectedTerms))
            {
                ClearSelectedTerms();
                return;
            }

            selectedUnitPrice = selectedTerms.referenceUnitPrice;
            selectedAcceptancePreview = ContractService.PreviewAcceptance(
                state, selectedSettlement, selectedItem, quantity, cadenceDays,
                totalDeliveries, selectedUnitPrice, fulfillment);
        }

        private void ClearSelectedTerms()
        {
            selectedTerms = null;
            selectedAcceptancePreview = null;
            selectedUnitPrice = 0f;
        }

        private void RefreshTermsForChosenPrice()
        {
            if (selectedSettlement == null || selectedItem == null)
            {
                ClearSelectedTerms();
                return;
            }

            selectedTerms = PreviewTerms(selectedUnitPrice);
            if (selectedTerms == null)
            {
                selectedAcceptancePreview = null;
                return;
            }

            selectedUnitPrice = selectedTerms.unitPrice;
            selectedAcceptancePreview = ContractService.PreviewAcceptance(
                state, selectedSettlement, selectedItem, quantity, cadenceDays,
                totalDeliveries, selectedTerms.unitPrice, fulfillment);
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

        private static Dictionary<ThingDef, List<Settlement>>
            InvertQualifyingItemsBySettlement(
                List<Settlement> settlements,
                Dictionary<int, List<ThingDef>> qualifyingItemsBySettlement)
        {
            Dictionary<ThingDef, List<Settlement>> result =
                new Dictionary<ThingDef, List<Settlement>>();
            foreach (Settlement settlement in settlements)
            {
                if (!qualifyingItemsBySettlement.TryGetValue(
                        settlement.ID, out List<ThingDef> items))
                {
                    continue;
                }

                foreach (ThingDef thingDef in items)
                {
                    if (thingDef == null)
                    {
                        continue;
                    }

                    if (!result.TryGetValue(
                            thingDef, out List<Settlement> itemSettlements))
                    {
                        itemSettlements = new List<Settlement>();
                        result.Add(thingDef, itemSettlements);
                    }

                    itemSettlements.Add(settlement);
                }
            }

            return result;
        }

        private static List<ThingDef> FindQualifyingItems(
            Dictionary<ThingDef, List<Settlement>> qualifyingSettlementsByItem)
        {
            List<ThingDef> result = new List<ThingDef>(
                qualifyingSettlementsByItem.Keys);
            result.Sort((a, b) => string.Compare(
                a.LabelCap.ToString(), b.LabelCap.ToString(),
                StringComparison.CurrentCultureIgnoreCase));
            return result;
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
