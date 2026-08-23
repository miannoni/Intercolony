using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Intercolony
{
    /// <summary>
    /// Shows one bounded counteroffer and then its one counterparty response. The dialog owns no
    /// negotiation decision: the service records the response, while this class only edits the
    /// four permitted fields and draws the returned read model.
    /// </summary>
    public sealed class Dialog_Counteroffer : Window
    {
        private const float WindowWidth = 700f;
        private const float WindowMargin = 18f;
        private const float TitleHeight = 34f;
        private const float HeaderHeight = 28f;
        private const float RowGap = 6f;
        private const float ControlHeight = 30f;
        private const float ButtonHeight = 36f;
        private const float ButtonWidth = 172f;
        private const float BottomGap = 10f;
        private const float ColumnGap = 8f;
        private const float LabelColumnWidth = 126f;
        private const float ScrollbarWidth = 16f;
        private const float MaxScreenHeightFraction = 0.72f;

        private readonly IntercolonyWorldComponent state;
        private readonly MarketOpportunity opportunity;
        private readonly bool showingAnswer;
        private readonly CounterofferAnswerView answerView;

        private int quantity;
        private string quantityBuffer;
        private float unitPrice;
        private string unitPriceBuffer;
        private int deadlineDays;
        private string deadlineBuffer;
        private FulfillmentMode fulfillment;
        private Vector2 contentScroll;

        public Dialog_Counteroffer(
            IntercolonyWorldComponent state, MarketOpportunity opportunity)
        {
            this.state = state;
            this.opportunity = opportunity;
            IntercolonyNegotiationTerms original = CounterofferUiService.OriginalTerms(opportunity);
            quantity = original.quantity;
            quantityBuffer = quantity.ToString();
            unitPrice = original.unitPrice;
            unitPriceBuffer = unitPrice.ToString("F2");
            deadlineDays = original.deadlineDays;
            deadlineBuffer = deadlineDays.ToString();
            fulfillment = original.fulfillment;
            showingAnswer = false;
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        private Dialog_Counteroffer(
            IntercolonyWorldComponent state,
            MarketOpportunity opportunity,
            IntercolonyNegotiationResult evaluation,
            SalesOrder acceptedOrder)
            : this(
                state,
                opportunity,
                CounterofferUiService.BuildAnswerView(opportunity, evaluation),
                acceptedOrder)
        {
        }

        internal Dialog_Counteroffer(
            IntercolonyWorldComponent state,
            MarketOpportunity opportunity,
            CounterofferAnswerView answerView,
            SalesOrder acceptedOrder)
        {
            this.state = state;
            this.opportunity = opportunity;
            this.answerView = answerView;
            this.acceptedOrder = acceptedOrder;
            showingAnswer = true;
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        private readonly SalesOrder acceptedOrder;

        public override Vector2 InitialSize
        {
            get
            {
                Text.Font = GameFont.Small;
                float contentWidth = ContentWidth(WindowWidth - WindowMargin * 2f);
                float bodyHeight = MeasuredBodyHeight(contentWidth);
                float fixedHeight = WindowMargin * 2f + TitleHeight + BottomGap + ButtonHeight;
                float desiredHeight = fixedHeight + bodyHeight;
                // A composed term or evaluator answer can wrap. Measuring it before sizing keeps
                // the window honest, while the cap makes a long answer scroll instead of painting
                // over the buttons below it.
                float maximumHeight = Mathf.Max(
                    fixedHeight + Text.LineHeight,
                    UI.screenHeight * MaxScreenHeightFraction);
                return new Vector2(
                    WindowWidth,
                    Mathf.Min(Mathf.Max(fixedHeight + Text.LineHeight, desiredHeight), maximumHeight));
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            float contentX = WindowMargin;
            float contentWidth = Mathf.Max(1f, inRect.width - WindowMargin * 2f);
            Widgets.Label(new Rect(contentX, WindowMargin, contentWidth, TitleHeight),
                showingAnswer ? "Counteroffer answer" : "Counteroffer");
            Text.Font = GameFont.Small;

            float contentTop = WindowMargin + TitleHeight;
            float contentBottom = inRect.height - WindowMargin - ButtonHeight - BottomGap;
            Rect contentRect = new Rect(
                contentX, contentTop, contentWidth, Mathf.Max(1f, contentBottom - contentTop));
            float viewWidth = ContentWidth(contentWidth);
            float contentHeight = MeasuredBodyHeight(viewWidth);
            Rect viewRect = new Rect(0f, 0f, viewWidth, contentHeight);

            // Widgets.Label does not clip or scroll a long string. The measured view rect is the
            // only safe place for changing row text, and the scroll view owns any overflow.
            Widgets.BeginScrollView(contentRect, ref contentScroll, viewRect);
            if (showingAnswer)
            {
                DrawAnswer(viewRect.width);
            }
            else
            {
                DrawProposal(viewRect.width);
            }
            Widgets.EndScrollView();

            DrawButtons(inRect);
        }

        private void DrawProposal(float width)
        {
            IntercolonyNegotiationTerms terms = ProposedTerms();
            List<CounterofferComparisonRow> rows = CounterofferUiService.BuildComparisonRows(
                opportunity, terms, allowEditing: true);
            DrawHeaders(width, "Proposed", 0f);
            DrawRows(width, rows, allowEditing: true, startY: 0f);
        }

        private void DrawAnswer(float width)
        {
            float answerHeight = AnswerRowHeight(width, answerView.answerRow);
            DrawAnswerRow(width, answerView.answerRow, 0f);
            float tableY = answerHeight + RowGap;
            DrawHeaders(width, answerView.comparisonHeader, tableY);
            DrawRows(width, answerView.rows, allowEditing: false, startY: tableY);
        }

        private void DrawHeaders(float width, string rightHeader, float startY)
        {
            float valueWidth = ValueColumnWidth(width);
            float y = startY;
            DrawMeasuredLabel(new Rect(0f, y, LabelColumnWidth, HeaderHeight), "Term");
            float originalX = LabelColumnWidth + ColumnGap;
            DrawMeasuredLabel(new Rect(originalX, y, valueWidth, HeaderHeight), "Original");
            float proposedX = originalX + valueWidth + ColumnGap;
            DrawMeasuredLabel(new Rect(proposedX, y, valueWidth, HeaderHeight), rightHeader);
        }

        private void DrawAnswerRow(
            float width, CounterofferAnswerRow answerRow, float startY)
        {
            float y = startY;
            float rowHeight = AnswerRowHeight(width, answerRow);
            Rect rowRect = new Rect(0f, y, width, rowHeight);
            DrawMeasuredLabel(new Rect(0f, y, LabelColumnWidth, rowHeight), answerRow.label);
            DrawMeasuredLabel(
                new Rect(LabelColumnWidth + ColumnGap, y, width - LabelColumnWidth - ColumnGap, rowHeight),
                answerRow.value);
            if (!answerRow.tooltip.NullOrEmpty())
            {
                TooltipHandler.TipRegion(rowRect, answerRow.tooltip);
                Widgets.DrawHighlightIfMouseover(rowRect);
            }
        }

        private void DrawRows(
            float width,
            List<CounterofferComparisonRow> rows,
            bool allowEditing,
            float startY)
        {
            float valueWidth = ValueColumnWidth(width);
            float originalX = LabelColumnWidth + ColumnGap;
            float proposedX = originalX + valueWidth + ColumnGap;
            float y = startY + HeaderHeight;
            CounterofferEditableTerms editable = CounterofferUiService.EditableTerms(opportunity);

            for (int i = 0; i < rows.Count; i++)
            {
                CounterofferComparisonRow row = rows[i];
                float rowHeight = RowHeight(
                    row.label, row.original, row.proposed, allowEditing && row.IsEditable, width);
                Rect rowRect = new Rect(0f, y, width, rowHeight);

                DrawMeasuredLabel(new Rect(0f, y, LabelColumnWidth, rowHeight), row.label);
                DrawMeasuredLabel(new Rect(originalX, y, valueWidth, rowHeight), row.original);

                if (allowEditing && row.IsEditable)
                {
                    DrawEditor(
                        new Rect(proposedX, y, valueWidth, rowHeight), row.term, editable);
                }
                else
                {
                    DrawMeasuredLabel(new Rect(proposedX, y, valueWidth, rowHeight), row.proposed);
                }

                if (!row.tooltip.NullOrEmpty())
                {
                    TooltipHandler.TipRegion(rowRect, row.tooltip);
                    Widgets.DrawHighlightIfMouseover(rowRect);
                }

                y += rowHeight + RowGap;
            }
        }

        private void DrawEditor(
            Rect rect, CounterofferTerm term, CounterofferEditableTerms editable)
        {
            if (!editable.Contains(term))
            {
                DrawMeasuredLabel(rect, term == CounterofferTerm.Fulfillment
                    ? CounterofferUiService.FormatFulfillment(fulfillment)
                    : "Fixed");
                return;
            }

            switch (term)
            {
                case CounterofferTerm.Price:
                    Widgets.TextFieldNumeric(
                        rect, ref unitPrice, ref unitPriceBuffer, 0.01f, 1000000f);
                    break;
                case CounterofferTerm.Quantity:
                    Widgets.TextFieldNumeric(
                        rect, ref quantity, ref quantityBuffer, 1, Mathf.Max(1, opportunity.quantity));
                    break;
                case CounterofferTerm.Deadline:
                    Widgets.TextFieldNumeric(
                        rect, ref deadlineDays, ref deadlineBuffer, 0, 10000);
                    break;
                case CounterofferTerm.Fulfillment:
                    DrawFulfillmentButtons(rect);
                    break;
            }
        }

        private void DrawFulfillmentButtons(Rect rect)
        {
            float width = (rect.width - ColumnGap) / 2f;
            Rect sellerRect = new Rect(rect.x, rect.y, width, rect.height);
            Rect pickupRect = new Rect(rect.x + width + ColumnGap, rect.y, width, rect.height);
            DrawFulfillmentButton(sellerRect, "You deliver", FulfillmentMode.SellerDelivery);
            DrawFulfillmentButton(pickupRect, "Buyer collects", FulfillmentMode.BuyerPickup);
        }

        private void DrawFulfillmentButton(Rect rect, string label, FulfillmentMode mode)
        {
            bool selected = fulfillment == mode;
            if (Widgets.ButtonText(rect, label) && !selected)
            {
                fulfillment = mode;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }

            if (selected)
            {
                Widgets.DrawHighlightSelected(rect);
            }
        }

        private void DrawButtons(Rect inRect)
        {
            Rect primaryRect = new Rect(
                WindowMargin,
                inRect.height - WindowMargin - ButtonHeight,
                ButtonWidth,
                ButtonHeight);
            Rect secondaryRect = new Rect(
                inRect.width - WindowMargin - ButtonWidth,
                inRect.height - WindowMargin - ButtonHeight,
                ButtonWidth, ButtonHeight);

            if (!showingAnswer)
            {
                if (Widgets.ButtonText(primaryRect, "Send counteroffer"))
                {
                    SubmitCounteroffer();
                }
            }
            else if (answerView.answerRow.decision == IntercolonyNegotiationDecision.Countered)
            {
                if (Widgets.ButtonText(primaryRect, "Accept final counter"))
                {
                    AcceptFinalCounter();
                }
            }
            else if (acceptedOrder != null && Widgets.ButtonText(primaryRect, "View orders"))
            {
                CloseAndShowOrders();
                return;
            }

            string secondaryLabel = showingAnswer &&
                                    answerView.answerRow.decision ==
                                    IntercolonyNegotiationDecision.Countered
                ? "Decline final counter"
                : showingAnswer ? "Close" : "Cancel";
            if (Widgets.ButtonText(secondaryRect, secondaryLabel))
            {
                if (showingAnswer &&
                    answerView.answerRow.decision == IntercolonyNegotiationDecision.Countered)
                {
                    DeclineFinalCounter();
                }
                else
                {
                    Close();
                }
            }
        }

        private void SubmitCounteroffer()
        {
            if (!CounterofferUiService.CounterActionAvailable(opportunity))
            {
                Messages.Message(
                    "This opportunity has already received its one response.",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                Close();
                return;
            }

            IntercolonyNegotiationTerms proposed = ProposedTerms();
            if (!CounterofferUiService.CanEditFulfillmentMode(opportunity))
            {
                proposed.fulfillment = opportunity.fulfillment;
            }

            bool processed = MarketOpportunityNegotiationService.TryCounter(
                state,
                opportunity,
                proposed,
                out IntercolonyNegotiationResult evaluation,
                out SalesOrder order,
                out string failureReason);
            if (!processed)
            {
                Messages.Message(
                    failureReason ?? "The counteroffer could not be sent.",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            Close();
            Find.WindowStack.Add(new Dialog_Counteroffer(
                state, opportunity, evaluation, order));
        }

        private void AcceptFinalCounter()
        {
            SalesOrder order = MarketOpportunityNegotiationService.AcceptFinalCounter(
                state, opportunity);
            if (order == null)
            {
                Messages.Message(
                    "The final counter is no longer available.",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            CloseAndShowOrders();
        }

        private void DeclineFinalCounter()
        {
            if (!MarketOpportunityNegotiationService.TryDecline(state, opportunity))
            {
                Messages.Message(
                    "The final counter is no longer available.",
                    MessageTypeDefOf.RejectInput,
                    historical: false);
                return;
            }

            Close();
        }

        private void CloseAndShowOrders()
        {
            Close();
            Find.WindowStack.WindowOfType<MainTabWindow_Intercolony>()?.ShowOrdersTab();
        }

        private IntercolonyNegotiationTerms ProposedTerms()
        {
            return new IntercolonyNegotiationTerms(
                quantity,
                unitPrice,
                deadlineDays,
                fulfillment);
        }

        private float MeasuredBodyHeight(float width)
        {
            if (showingAnswer)
            {
                return AnswerRowHeight(width, answerView.answerRow) + RowGap +
                       HeaderHeight + MeasuredRows(answerView.rows, width);
            }

            List<CounterofferComparisonRow> rows = CounterofferUiService.BuildComparisonRows(
                opportunity, ProposedTerms(), allowEditing: true);
            return HeaderHeight + MeasuredRows(rows, width);
        }

        private static float AnswerRowHeight(float width, CounterofferAnswerRow answerRow)
        {
            float valueWidth = width - LabelColumnWidth - ColumnGap;
            return Mathf.Max(
                ControlHeight,
                Text.CalcHeight(answerRow.label ?? "", LabelColumnWidth),
                Text.CalcHeight(answerRow.value ?? "", Mathf.Max(1f, valueWidth)));
        }

        private static float MeasuredRows(
            List<CounterofferComparisonRow> rows, float width)
        {
            float height = 0f;
            for (int i = 0; i < rows.Count; i++)
            {
                CounterofferComparisonRow row = rows[i];
                height += RowHeight(row.label, row.original, row.proposed, row.IsEditable, width);
                if (i < rows.Count - 1)
                {
                    height += RowGap;
                }
            }

            return height;
        }

        private static float RowHeight(
            string label,
            string original,
            string proposed,
            bool proposedIsControl,
            float width)
        {
            float valueWidth = ValueColumnWidth(width);
            float height = Mathf.Max(
                Text.CalcHeight(label ?? "", LabelColumnWidth),
                Text.CalcHeight(original ?? "", valueWidth));
            if (!proposedIsControl)
            {
                height = Mathf.Max(height, Text.CalcHeight(proposed ?? "", valueWidth));
            }

            return Mathf.Max(ControlHeight, height);
        }

        private static float ValueColumnWidth(float width)
        {
            return Mathf.Max(
                1f,
                (width - LabelColumnWidth - ColumnGap * 2f) / 2f);
        }

        private static float ContentWidth(float width)
        {
            return Mathf.Max(1f, width - ScrollbarWidth);
        }

        private static void DrawMeasuredLabel(Rect rect, string text)
        {
            float measuredHeight = Text.CalcHeight(text ?? "", rect.width);
            Widgets.Label(
                new Rect(rect.x, rect.y, rect.width, Mathf.Max(rect.height, measuredHeight)),
                text ?? "");
        }
    }
}
