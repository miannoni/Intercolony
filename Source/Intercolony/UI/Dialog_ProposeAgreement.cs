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
        private const float TermsTopOffset = 224f;
        private const float ButtonWidth = 110f;
        private const float ButtonHeight = 34f;

        private readonly IntercolonyWorldComponent state;
        private readonly List<Settlement> qualifyingSettlements;
        private readonly Dictionary<int, List<ThingDef>> qualifyingItemsBySettlement;

        private Vector2 settlementScroll;
        private Vector2 itemScroll;
        private Settlement selectedSettlement;
        private List<ThingDef> qualifyingItems = new List<ThingDef>();
        private ThingDef selectedItem;
        private int quantity = ContractService.MinimumQuantityPerCycle;
        private string quantityBuffer = ContractService.MinimumQuantityPerCycle.ToString();
        private float selectedUnitPrice;
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
                "Quantity and price");
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

            if (quantity != previousQuantity)
            {
                RefreshTerms(resetPrice: false);
            }

            y += SliderHeight + 22f;
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
            if (!Mathf.Approximately(slidPrice, selectedTerms.unitPrice))
            {
                selectedUnitPrice = slidPrice;
                RefreshTermsForChosenPrice();
            }

            y += SliderHeight + 20f;
            Widgets.Label(new Rect(rect.x, y, rect.width, 52f), PriceAdvice());
        }

        private void DrawTermsSummary(Rect rect)
        {
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, SectionLabelHeight),
                "Agreement terms");

            Rect detailsRect = new Rect(
                rect.x, rect.y + SectionLabelHeight, rect.width,
                rect.height - SectionLabelHeight);
            if (selectedTerms == null)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.55f);
                Widgets.Label(detailsRect,
                    "Select a settlement and item to preview the terms.");
                GUI.color = Color.white;
                return;
            }

            float daysBetweenDeliveries =
                selectedTerms.cadenceTicks / (float)GenDate.TicksPerDay;
            Widgets.Label(detailsRect,
                $"Payment per delivery: {selectedTerms.paymentPerDelivery:N0} silver\n" +
                $"Delivery interval: {daysBetweenDeliveries:F0} days\n" +
                $"Deliveries: {selectedTerms.deliveryCount}\n" +
                $"Total: {selectedTerms.totalPayment:N0} silver");
        }

        private void TryPropose()
        {
            ContractProposalResult result = ContractService.ProposeContract(
                state, selectedSettlement, selectedItem, quantity, selectedUnitPrice);
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

            ContractTerms bounds = ContractService.PreviewContractTerms(
                state, selectedSettlement, selectedItem, quantity);
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
            selectedTerms = ContractService.PreviewContractTerms(
                state, selectedSettlement, selectedItem, quantity, selectedUnitPrice);
            if (selectedTerms != null)
            {
                selectedUnitPrice = selectedTerms.unitPrice;
            }
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
                            ContractService.MinimumQuantityPerCycle) != null)
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
