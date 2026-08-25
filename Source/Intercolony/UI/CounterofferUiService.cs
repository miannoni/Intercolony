using System.Collections.Generic;

namespace Intercolony
{
    internal enum CounterofferTerm
    {
        None,
        Price,
        Quantity,
        Deadline,
        Fulfillment
    }

    internal readonly struct CounterofferEditableTerms
    {
        public readonly bool price;
        public readonly bool quantity;
        public readonly bool deadline;
        public readonly bool fulfillment;

        public CounterofferEditableTerms(
            bool price, bool quantity, bool deadline, bool fulfillment)
        {
            this.price = price;
            this.quantity = quantity;
            this.deadline = deadline;
            this.fulfillment = fulfillment;
        }

        public bool Contains(CounterofferTerm term)
        {
            switch (term)
            {
                case CounterofferTerm.Price:
                    return price;
                case CounterofferTerm.Quantity:
                    return quantity;
                case CounterofferTerm.Deadline:
                    return deadline;
                case CounterofferTerm.Fulfillment:
                    return fulfillment;
                default:
                    return false;
            }
        }
    }

    internal readonly struct CounterofferComparisonRow
    {
        public readonly CounterofferTerm term;
        public readonly string label;
        public readonly string original;
        public readonly string proposed;
        public readonly string tooltip;
        public readonly bool editable;

        public CounterofferComparisonRow(
            CounterofferTerm term,
            string label,
            string original,
            string proposed,
            string tooltip,
            bool editable)
        {
            this.term = term;
            this.label = label;
            this.original = original;
            this.proposed = proposed;
            this.tooltip = tooltip;
            this.editable = editable;
        }

        public bool IsEditable => editable;
    }

    internal readonly struct CounterofferAnswerRow
    {
        public readonly IntercolonyNegotiationDecision decision;
        public readonly string label;
        public readonly string value;
        public readonly string tooltip;

        public CounterofferAnswerRow(
            IntercolonyNegotiationDecision decision,
            string label,
            string value,
            string tooltip)
        {
            this.decision = decision;
            this.label = label;
            this.value = value;
            this.tooltip = tooltip;
        }
    }

    internal sealed class CounterofferAnswerView
    {
        public readonly CounterofferAnswerRow answerRow;
        public readonly IntercolonyNegotiationTerms comparisonTerms;
        public readonly string comparisonHeader;
        public readonly List<CounterofferComparisonRow> rows;

        public CounterofferAnswerView(
            CounterofferAnswerRow answerRow,
            IntercolonyNegotiationTerms comparisonTerms,
            string comparisonHeader,
            List<CounterofferComparisonRow> rows)
        {
            this.answerRow = answerRow;
            this.comparisonTerms = comparisonTerms;
            this.comparisonHeader = comparisonHeader;
            this.rows = rows;
        }
    }

    /// <summary>
    /// Read model for the counteroffer surface. It decides which controls and rows exist, while
    /// the dialog only draws them; this keeps a visual rearrangement from quietly inventing a new
    /// negotiation rule that the service would reject.
    /// </summary>
    internal static class CounterofferUiService
    {
        internal static bool CounterActionAvailable(MarketOpportunity opportunity)
        {
            return opportunity != null && opportunity.CanSubmitCounter;
        }

        internal static bool CanEditFulfillmentMode(MarketOpportunity opportunity)
        {
            return opportunity != null && opportunity.SupportsBothFulfillmentModes;
        }

        internal static CounterofferEditableTerms EditableTerms(MarketOpportunity opportunity)
        {
            return new CounterofferEditableTerms(
                price: opportunity != null && opportunity.CanSubmitCounter,
                quantity: opportunity != null && opportunity.CanSubmitCounter,
                deadline: opportunity != null && opportunity.CanSubmitCounter,
                fulfillment: CanEditFulfillmentMode(opportunity) &&
                             opportunity.CanSubmitCounter);
        }

        internal static List<CounterofferComparisonRow> BuildComparisonRows(
            MarketOpportunity opportunity,
            IntercolonyNegotiationTerms proposedTerms,
            bool allowEditing)
        {
            List<CounterofferComparisonRow> rows = new List<CounterofferComparisonRow>();
            if (opportunity == null)
            {
                return rows;
            }

            IntercolonyNegotiationTerms original = OriginalTerms(opportunity);
            IntercolonyNegotiationTerms proposed = proposedTerms?.Clone() ?? original.Clone();
            if (!CanEditFulfillmentMode(opportunity))
            {
                // Keep an unsupported mode out of the proposed read model as well as out of the
                // editor. Otherwise a stale caller could paint a term the service must reject.
                proposed.fulfillment = original.fulfillment;
            }
            CounterofferEditableTerms editable = allowEditing
                ? EditableTerms(opportunity)
                : new CounterofferEditableTerms(false, false, false, false);

            rows.Add(new CounterofferComparisonRow(
                CounterofferTerm.Price,
                "Price",
                FormatPrice(original.unitPrice),
                FormatPrice(proposed.unitPrice),
                "The proposed unit price is the amount the buyer would pay for each accepted unit.",
                editable.price));
            rows.Add(new CounterofferComparisonRow(
                CounterofferTerm.Quantity,
                "Quantity",
                FormatQuantity(original.quantity),
                FormatQuantity(proposed.quantity),
                "The proposed quantity is the exact number of units bound if the counteroffer is accepted.",
                editable.quantity));
            rows.Add(new CounterofferComparisonRow(
                CounterofferTerm.Deadline,
                "Deadline",
                FormatDeadline(original.deadlineDays),
                FormatDeadline(proposed.deadlineDays),
                "The deadline starts when the terms become a binding order.",
                editable.deadline));
            rows.Add(new CounterofferComparisonRow(
                CounterofferTerm.Fulfillment,
                "Fulfilment",
                FormatFulfillment(original.fulfillment),
                FormatFulfillment(proposed.fulfillment),
                editable.fulfillment
                    ? "The selected mode decides who moves the goods."
                    : "This opportunity supports only its advertised fulfilment mode.",
                editable.fulfillment));
            rows.Add(new CounterofferComparisonRow(
                CounterofferTerm.None,
                "Total payment",
                FormatPayment(original.unitPrice, original.quantity),
                FormatPayment(proposed.unitPrice, proposed.quantity),
                "The total is calculated by the shared pricing owner from the unit price and quantity.",
                editable: false));
            return rows;
        }

        internal static CounterofferAnswerView BuildAnswerView(
            MarketOpportunity opportunity,
            IntercolonyNegotiationResult evaluation)
        {
            IntercolonyNegotiationDecision decision = evaluation == null
                ? IntercolonyNegotiationDecision.Refused
                : evaluation.Decision;
            IntercolonyNegotiationTerms comparisonTerms = evaluation == null
                ? OriginalTerms(opportunity)
                : decision == IntercolonyNegotiationDecision.Countered
                    ? evaluation.FinalCounterTerms
                    : evaluation.ProposedTerms;

            if (comparisonTerms == null)
            {
                comparisonTerms = OriginalTerms(opportunity);
            }

            string value;
            string header;
            switch (decision)
            {
                case IntercolonyNegotiationDecision.Accepted:
                    value = "Accepted";
                    header = "Proposed";
                    break;
                case IntercolonyNegotiationDecision.Countered:
                    value = "Final counter";
                    header = "Counterparty";
                    break;
                default:
                    value = "Refused";
                    header = "Proposed";
                    break;
            }

            CounterofferAnswerRow answerRow = new CounterofferAnswerRow(
                decision,
                "Answer",
                value,
                IntercolonyNegotiationEvaluator.Explain(evaluation));
            return new CounterofferAnswerView(
                answerRow,
                comparisonTerms,
                header,
                BuildComparisonRows(opportunity, comparisonTerms, allowEditing: false));
        }

        internal static CounterofferAnswerView BuildPersistedFinalCounterView(
            MarketOpportunity opportunity,
            IntercolonyNegotiationTerms finalCounterTerms)
        {
            CounterofferAnswerRow answerRow = new CounterofferAnswerRow(
                IntercolonyNegotiationDecision.Countered,
                "Answer",
                "Final counter",
                "The evaluator's one final counter is persisted on this opportunity; no further counter is available.");
            IntercolonyNegotiationTerms terms = finalCounterTerms ?? OriginalTerms(opportunity);
            return new CounterofferAnswerView(
                answerRow,
                terms,
                "Counterparty",
                BuildComparisonRows(opportunity, terms, allowEditing: false));
        }

        internal static IntercolonyNegotiationTerms OriginalTerms(MarketOpportunity opportunity)
        {
            return opportunity == null
                ? new IntercolonyNegotiationTerms()
                : new IntercolonyNegotiationTerms(
                    opportunity.quantity,
                    opportunity.unitPrice,
                    opportunity.deadlineDays,
                    opportunity.fulfillment);
        }

        internal static string FormatPrice(float unitPrice)
        {
            return $"{unitPrice:F2} silver/unit";
        }

        internal static string FormatQuantity(int quantity)
        {
            return $"{quantity} units";
        }

        internal static string FormatDeadline(int deadlineDays)
        {
            return $"{deadlineDays} days";
        }

        internal static string FormatFulfillment(FulfillmentMode mode)
        {
            return mode == FulfillmentMode.BuyerPickup ? "Buyer collects" : "You deliver";
        }

        internal static string FormatPayment(float unitPrice, int quantity)
        {
            return $"{IntercolonyPricing.TotalPayment(unitPrice, quantity):N0} silver";
        }
    }
}
