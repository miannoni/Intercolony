using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Intercolony
{
    public readonly struct TermRow
    {
        public readonly string label;
        public readonly string value;
        public readonly string tooltip;

        public TermRow(string label, string value, string tooltip = null)
        {
            this.label = label;
            this.value = value;
            this.tooltip = tooltip;
        }
    }

    /// <summary>
    /// Confirmation dialog with a quantity slider, used wherever the player commits to a
    /// contract — accepting a market order, buying a quote, selling to a found buyer.
    ///
    /// One dialog rather than three, because the decision is the same shape every time: read
    /// the terms, choose how much, commit. Having the amount live in the tab for one flow and
    /// nowhere for the others made the commitment step inconsistent.
    ///
    /// The slider can only ever reduce the amount, never raise it above what was offered.
    /// Prices are computed for the advertised quantity — saturation in particular (§13) — so
    /// letting the player scale *up* at a rate quoted for a smaller lot would be a way to buy
    /// the good price and then take more of it.
    /// </summary>
    public class Dialog_ConfirmQuantity : Window
    {
        private readonly string title;
        private readonly string confirmLabel;
        private readonly int maxQuantity;
        private readonly int minQuantity;
        private readonly string quantityLabel;
        private readonly Func<int, string> bodyBuilder;
        private readonly Action<int> onConfirm;
        private readonly Func<int, FulfillmentMode, string> fulfillmentBodyBuilder;
        private readonly Action<int, FulfillmentMode> fulfillmentOnConfirm;
        private readonly Func<int, FulfillmentMode, float, string> discountBodyBuilder;
        private readonly Func<int, FulfillmentMode, float, List<TermRow>> discountRowsBuilder;
        private readonly Action<int, FulfillmentMode, float> discountOnConfirm;
        private readonly Func<int, FulfillmentMode, float, string> discountPreviewBuilder;
        private readonly bool chooseFulfillment;
        private readonly bool chooseDiscount;

        private const float WindowWidth = 520f;
        private const float WindowMargin = 18f;
        private const float TitleHeight = 38f;
        private const float BodyControlsGap = 8f;
        private const float QuantityControlsHeight = 122f;
        private const float FulfillmentControlsHeight = 58f;
        private const float DiscountControlsHeight = 58f;
        private const float TermLabelWidth = 150f;
        private const float TermColumnGap = 8f;
        private const float TermRowGap = 5f;
        private const float TooltipAffordanceWidth = 18f;

        private int quantity;
        private string buffer;
        private FulfillmentMode fulfillment;
        private float discountFraction;
        private Vector2 bodyScroll;

        /// <param name="minQuantity">
        /// Floor on the commitment. Hiring needs it: a worker with a five-day minimum term must
        /// not be hirable for two, and the floor belongs in the one dialog every commitment goes
        /// through rather than as a second near-identical dialog.
        /// </param>
        /// <param name="quantityLabel">Caption for the numeric field. "Quantity:" suits goods, "Days:" suits a term.</param>
        public Dialog_ConfirmQuantity(
            string title,
            string confirmLabel,
            int maxQuantity,
            Func<int, string> bodyBuilder,
            Action<int> onConfirm,
            int minQuantity = 1,
            string quantityLabel = "Quantity:")
            : this(title, confirmLabel, maxQuantity, bodyBuilder, onConfirm, null, null,
                null, null, null, null, FulfillmentMode.SellerDelivery, minQuantity, quantityLabel)
        {
        }

        /// <summary>
        /// Find Buyer sale variant whose live terms are presented as measured key/value rows.
        /// Existing string builders remain available for callers not yet converted.
        /// </summary>
        public Dialog_ConfirmQuantity(
            string title,
            string confirmLabel,
            int maxQuantity,
            Func<int, FulfillmentMode, float, List<TermRow>> rowsBuilder,
            Action<int, FulfillmentMode, float> onConfirm,
            Func<int, FulfillmentMode, float, string> discountPreviewBuilder = null,
            FulfillmentMode initialFulfillment = FulfillmentMode.BuyerPickup,
            int minQuantity = 1,
            string quantityLabel = "Quantity:",
            bool allowFulfillmentChoice = true)
            : this(title, confirmLabel, maxQuantity, null, null, null, null,
                null, rowsBuilder, onConfirm, discountPreviewBuilder, initialFulfillment,
                minQuantity, quantityLabel, allowFulfillmentChoice)
        {
        }

        /// <summary>
        /// Find Buyer variant: quantity and logistics are one commitment, so both choices live
        /// on the same confirmation surface and feed the same live terms preview.
        /// </summary>
        public Dialog_ConfirmQuantity(
            string title,
            string confirmLabel,
            int maxQuantity,
            Func<int, FulfillmentMode, string> bodyBuilder,
            Action<int, FulfillmentMode> onConfirm,
            // The buyer coming to you is the ordinary case; forming a caravan to take goods
            // across the map is the exception you opt into. Starting selection only: no
            // persisted default moves, since that would reinterpret existing saves.
            FulfillmentMode initialFulfillment = FulfillmentMode.BuyerPickup,
            int minQuantity = 1,
            string quantityLabel = "Quantity:")
            : this(title, confirmLabel, maxQuantity, null, null, bodyBuilder, onConfirm,
                null, null, null, null, initialFulfillment, minQuantity, quantityLabel)
        {
        }

        /// <summary>
        /// Find Buyer sale variant: the discount joins quantity and logistics in the live
        /// terms preview, while the existing fulfillment-only callers remain unchanged.
        /// </summary>
        public Dialog_ConfirmQuantity(
            string title,
            string confirmLabel,
            int maxQuantity,
            Func<int, FulfillmentMode, float, string> bodyBuilder,
            Action<int, FulfillmentMode, float> onConfirm,
            Func<int, FulfillmentMode, float, string> discountPreviewBuilder = null,
            FulfillmentMode initialFulfillment = FulfillmentMode.BuyerPickup,
            int minQuantity = 1,
            string quantityLabel = "Quantity:",
            bool allowFulfillmentChoice = true)
            : this(title, confirmLabel, maxQuantity, null, null, null, null,
                bodyBuilder, null, onConfirm, discountPreviewBuilder, initialFulfillment,
                minQuantity, quantityLabel, allowFulfillmentChoice)
        {
        }

        private Dialog_ConfirmQuantity(
            string title,
            string confirmLabel,
            int maxQuantity,
            Func<int, string> bodyBuilder,
            Action<int> onConfirm,
            Func<int, FulfillmentMode, string> fulfillmentBodyBuilder,
            Action<int, FulfillmentMode> fulfillmentOnConfirm,
            Func<int, FulfillmentMode, float, string> discountBodyBuilder,
            Func<int, FulfillmentMode, float, List<TermRow>> discountRowsBuilder,
            Action<int, FulfillmentMode, float> discountOnConfirm,
            Func<int, FulfillmentMode, float, string> discountPreviewBuilder,
            FulfillmentMode initialFulfillment,
            int minQuantity,
            string quantityLabel,
            bool allowFulfillmentChoice = true)
        {
            this.title = title;
            this.confirmLabel = confirmLabel;
            this.minQuantity = Mathf.Max(1, minQuantity);
            this.maxQuantity = Mathf.Max(this.minQuantity, maxQuantity);
            this.bodyBuilder = bodyBuilder;
            this.onConfirm = onConfirm;
            this.fulfillmentBodyBuilder = fulfillmentBodyBuilder;
            this.fulfillmentOnConfirm = fulfillmentOnConfirm;
            this.discountBodyBuilder = discountBodyBuilder;
            this.discountRowsBuilder = discountRowsBuilder;
            this.discountOnConfirm = discountOnConfirm;
            this.discountPreviewBuilder = discountPreviewBuilder;
            chooseDiscount = discountBodyBuilder != null || discountRowsBuilder != null;
            chooseFulfillment = allowFulfillmentChoice &&
                                (fulfillmentBodyBuilder != null || chooseDiscount);
            fulfillment = initialFulfillment;
            this.quantityLabel = quantityLabel;

            // Open at the floor, not the ceiling, when there is a real floor: the minimum term is
            // the cheapest commitment, and the player should have to choose to spend more.
            quantity = this.minQuantity > 1 ? this.minQuantity : this.maxQuantity;
            buffer = quantity.ToString();

            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize
        {
            get
            {
                Text.Font = GameFont.Small;
                float bodyWidth = WindowWidth - WindowMargin * 2f;
                float bodyHeight = discountRowsBuilder != null
                    ? MeasureRows(BuildRows(), bodyWidth)
                    : Text.CalcHeight(BuildBody(), bodyWidth);
                float fixedHeight = WindowMargin * 2f + TitleHeight + BodyControlsGap +
                                    ControlsHeight();

                // InitialSize is only consumed when the window opens. Later slider changes can
                // lengthen the live body, so DoWindowContents scrolls any growth beyond this slot.
                float height = Mathf.Min(fixedHeight + bodyHeight, UI.screenHeight * 0.7f);
                return new Vector2(WindowWidth, Mathf.Max(fixedHeight + Text.LineHeight, height));
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            float y = 0f;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, inRect.width, 32f), title);
            y += 38f;
            Text.Font = GameFont.Small;

            // Body is rebuilt for the current quantity, so the price the player is agreeing to
            // updates as they move the slider rather than describing the original amount.
            float controlsHeight = ControlsHeight();
            float controlsTop = inRect.height - controlsHeight;

            Rect bodyRect = new Rect(0f, y, inRect.width, controlsTop - y - BodyControlsGap);
            if (discountRowsBuilder != null)
            {
                List<TermRow> rows = BuildRows();
                float rowsHeight = MeasureRows(rows, bodyRect.width);
                if (rowsHeight <= bodyRect.height)
                {
                    DrawRows(rows, bodyRect.width, bodyRect.y);
                }
                else
                {
                    float viewWidth = bodyRect.width - 16f;
                    float viewHeight = MeasureRows(rows, viewWidth);
                    Rect viewRect = new Rect(0f, 0f, viewWidth, viewHeight);
                    Widgets.BeginScrollView(bodyRect, ref bodyScroll, viewRect);
                    DrawRows(rows, viewWidth, 0f);
                    Widgets.EndScrollView();
                }
            }
            else
            {
                string body = BuildBody();
                float bodyHeight = Text.CalcHeight(body, bodyRect.width);
                if (bodyHeight <= bodyRect.height)
                {
                    Widgets.Label(bodyRect, body);
                }
                else
                {
                    float viewWidth = bodyRect.width - 16f;
                    float viewHeight = Text.CalcHeight(body, viewWidth);
                    Rect viewRect = new Rect(0f, 0f, viewWidth, viewHeight);
                    Widgets.BeginScrollView(bodyRect, ref bodyScroll, viewRect);
                    Widgets.Label(viewRect, body);
                    Widgets.EndScrollView();
                }
            }

            float bottom = controlsTop;

            if (chooseFulfillment)
            {
                Widgets.Label(new Rect(0f, bottom, inRect.width, 22f), "Fulfillment:");

                float choiceWidth = (inRect.width - 8f) / 2f;
                DrawFulfillmentChoice(
                    new Rect(0f, bottom + 24f, choiceWidth, 28f),
                    "You deliver", FulfillmentMode.SellerDelivery, Color.white);
                DrawFulfillmentChoice(
                    new Rect(choiceWidth + 8f, bottom + 24f, choiceWidth, 28f),
                    "Buyer collects", FulfillmentMode.BuyerPickup,
                    new Color(0.6f, 0.85f, 1f));

                bottom += FulfillmentControlsHeight;
            }

            if (chooseDiscount)
            {
                Widgets.Label(new Rect(0f, bottom, 108f, 28f),
                    $"Discount: {discountFraction.ToStringPercent("F0")}");

                discountFraction = Mathf.Clamp01(Widgets.HorizontalSlider(
                    new Rect(110f, bottom + 4f, 190f, 20f),
                    discountFraction, 0f, 1f, middleAlignment: false,
                    label: null, leftAlignedLabel: "0%", rightAlignedLabel: "100%",
                    roundTo: 0.01f));

                string paymentPreview = discountPreviewBuilder?.Invoke(
                    quantity, fulfillment, discountFraction);
                if (!paymentPreview.NullOrEmpty())
                {
                    Widgets.Label(new Rect(314f, bottom, inRect.width - 314f, 48f),
                        paymentPreview);
                }

                bottom += DiscountControlsHeight;
            }

            Widgets.Label(new Rect(0f, bottom, 110f, 28f), quantityLabel);
            Widgets.TextFieldNumeric(
                new Rect(112f, bottom, 90f, 28f), ref quantity, ref buffer, minQuantity, maxQuantity);

            // "All" reads correctly for goods ("all of my stock"); for a term it does not, so a
            // floored commitment says "Max" instead.
            Rect allRect = new Rect(212f, bottom, 60f, 28f);
            if (Widgets.ButtonText(allRect, minQuantity > 1 ? "Max" : "All"))
            {
                SetQuantity(maxQuantity);
            }

            // With a floor, "Min" is the useful shortcut; without one, "Half" is.
            Rect secondRect = new Rect(276f, bottom, 60f, 28f);
            if (minQuantity > 1)
            {
                if (Widgets.ButtonText(secondRect, "Min"))
                {
                    SetQuantity(minQuantity);
                }
            }
            else if (Widgets.ButtonText(secondRect, "Half"))
            {
                SetQuantity(Mathf.Max(1, maxQuantity / 2));
            }

            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            Widgets.Label(new Rect(344f, bottom + 3f, inRect.width - 350f, 24f),
                minQuantity > 1 ? $"{minQuantity} to {maxQuantity}" : $"of {maxQuantity}");
            GUI.color = Color.white;

            bottom += 34f;
            int slid = Mathf.RoundToInt(Widgets.HorizontalSlider(
                new Rect(0f, bottom + 4f, inRect.width, 20f), quantity, minQuantity, maxQuantity));
            if (slid != quantity)
            {
                SetQuantity(slid);
            }

            bottom += 40f;
            Rect confirmRect = new Rect(0f, bottom, 170f, 36f);
            if (Widgets.ButtonText(confirmRect, confirmLabel))
            {
                int confirmedQuantity = Mathf.Clamp(quantity, minQuantity, maxQuantity);
                if (chooseDiscount)
                {
                    discountOnConfirm?.Invoke(
                        confirmedQuantity, fulfillment, Mathf.Clamp01(discountFraction));
                }
                else if (chooseFulfillment)
                {
                    fulfillmentOnConfirm?.Invoke(confirmedQuantity, fulfillment);
                }
                else
                {
                    onConfirm?.Invoke(confirmedQuantity);
                }
                Close();
            }

            Rect cancelRect = new Rect(inRect.width - 130f, bottom, 120f, 36f);
            if (Widgets.ButtonText(cancelRect, "Cancel"))
            {
                Close();
            }
        }

        private void SetQuantity(int value)
        {
            quantity = Mathf.Clamp(value, minQuantity, maxQuantity);
            buffer = quantity.ToString();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
        }

        private float ControlsHeight()
        {
            return QuantityControlsHeight +
                   (chooseFulfillment ? FulfillmentControlsHeight : 0f) +
                   (chooseDiscount ? DiscountControlsHeight : 0f);
        }

        private string BuildBody()
        {
            return chooseDiscount
                ? discountBodyBuilder(quantity, fulfillment, discountFraction)
                : chooseFulfillment
                    ? fulfillmentBodyBuilder(quantity, fulfillment)
                    : bodyBuilder(quantity);
        }

        private List<TermRow> BuildRows()
        {
            return discountRowsBuilder(quantity, fulfillment, discountFraction) ??
                   new List<TermRow>();
        }

        private static float MeasureRows(List<TermRow> rows, float width)
        {
            float height = 0f;
            for (int i = 0; i < rows.Count; i++)
            {
                height += RowHeight(rows[i], width);
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
                float valueWidth = ValueWidth(row, width);
                float rowHeight = RowHeight(row, width);
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

        private static float RowHeight(TermRow row, float width)
        {
            float valueHeight = Text.CalcHeight(row.value ?? "", ValueWidth(row, width));
            float labelHeight = row.label.NullOrEmpty()
                ? 0f
                : Text.CalcHeight(row.label, TermLabelWidth);
            return Mathf.Max(valueHeight, labelHeight);
        }

        private static float ValueWidth(TermRow row, float width)
        {
            float valueWidth = row.label.NullOrEmpty()
                ? width
                : width - TermLabelWidth - TermColumnGap;
            valueWidth -= TooltipAffordanceWidth;
            return Mathf.Max(1f, valueWidth);
        }

        private void DrawFulfillmentChoice(
            Rect rect, string label, FulfillmentMode mode, Color colour)
        {
            bool selected = fulfillment == mode;
            GUI.color = colour;
            if (Widgets.ButtonText(rect, label) && !selected)
            {
                fulfillment = mode;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }

            // ButtonText paints its own background, so the selection must be drawn afterwards.
            if (selected)
            {
                Widgets.DrawHighlightSelected(rect);
            }

            GUI.color = Color.white;
        }
    }
}
