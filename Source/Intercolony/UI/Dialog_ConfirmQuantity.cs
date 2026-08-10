using System;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Intercolony
{
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
        private readonly bool chooseFulfillment;

        private int quantity;
        private string buffer;
        private FulfillmentMode fulfillment;

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
                FulfillmentMode.SellerDelivery, minQuantity, quantityLabel)
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
                initialFulfillment, minQuantity, quantityLabel)
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
            FulfillmentMode initialFulfillment,
            int minQuantity,
            string quantityLabel)
        {
            this.title = title;
            this.confirmLabel = confirmLabel;
            this.minQuantity = Mathf.Max(1, minQuantity);
            this.maxQuantity = Mathf.Max(this.minQuantity, maxQuantity);
            this.bodyBuilder = bodyBuilder;
            this.onConfirm = onConfirm;
            this.fulfillmentBodyBuilder = fulfillmentBodyBuilder;
            this.fulfillmentOnConfirm = fulfillmentOnConfirm;
            chooseFulfillment = fulfillmentBodyBuilder != null;
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

        public override Vector2 InitialSize => new Vector2(520f, chooseFulfillment ? 478f : 420f);

        public override void DoWindowContents(Rect inRect)
        {
            float y = 0f;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, inRect.width, 32f), title);
            y += 38f;
            Text.Font = GameFont.Small;

            // Body is rebuilt for the current quantity, so the price the player is agreeing to
            // updates as they move the slider rather than describing the original amount.
            const float QuantityControlsHeight = 122f;
            const float FulfillmentControlsHeight = 58f;
            float controlsHeight = QuantityControlsHeight +
                                   (chooseFulfillment ? FulfillmentControlsHeight : 0f);
            float controlsTop = inRect.height - controlsHeight;

            Rect bodyRect = new Rect(0f, y, inRect.width, controlsTop - y - 8f);
            Widgets.Label(bodyRect, chooseFulfillment
                ? fulfillmentBodyBuilder(quantity, fulfillment)
                : bodyBuilder(quantity));

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
                if (chooseFulfillment)
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
