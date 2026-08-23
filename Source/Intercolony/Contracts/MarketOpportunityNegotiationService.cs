using RimWorld;
using RimWorld.Planet;
using UnityEngine;

namespace Intercolony
{
    /// <summary>
    /// Owns the finite Stage 5B negotiation transitions for a market opportunity. The evaluator
    /// answers one proposed package; this service records that answer and exposes only the one
    /// legal player response to a final counter. It never evaluates a counterparty counter again,
    /// which makes an unbounded exchange impossible by construction (§5.3).
    /// </summary>
    public static class MarketOpportunityNegotiationService
    {
        /// <summary>
        /// Sends one player counter to the existing evaluator. A true return means the state
        /// machine processed the response; the evaluator's decision says whether that response
        /// created an order, stored one final counter, or retained the original offer after a
        /// refusal. A false return means the action was not legal in the current finite state.
        /// </summary>
        public static bool TryCounter(
            IntercolonyWorldComponent state,
            MarketOpportunity opportunity,
            IntercolonyNegotiationTerms proposedTerms,
            out IntercolonyNegotiationResult evaluation,
            out SalesOrder order,
            out string failureReason)
        {
            evaluation = null;
            order = null;
            failureReason = null;

            if (state == null || opportunity == null)
            {
                failureReason = "world state and opportunity are required";
                return false;
            }

            // There is intentionally no transition out of CounterpartyCountered or
            // CounterpartyRefused back into this method's evaluator call. The opportunity's
            // finite state is the guardrail, not a UI convention that a later dialog might forget.
            if (!opportunity.CanSubmitCounter)
            {
                failureReason =
                    $"opportunity {opportunity.id} is already in " +
                    $"{opportunity.NegotiationState} and cannot be countered again";
                return false;
            }

            if (!TryBuildProposal(
                    state, opportunity, proposedTerms, out IntercolonyNegotiationProposal proposal,
                    out failureReason))
            {
                return false;
            }

            evaluation = IntercolonyNegotiationEvaluator.Evaluate(proposal);
            switch (evaluation.Decision)
            {
                case IntercolonyNegotiationDecision.Accepted:
                    // The agreed proposal is consumed through the same order boundary as an
                    // ordinary accept. The opportunity fields are never overwritten with the
                    // proposed values, so a binding order is built from a separate term package.
                    order = SalesOrderService.AcceptNegotiatedTerms(
                        state, opportunity, proposedTerms, acceptingFinalCounter: false);
                    if (order == null)
                    {
                        failureReason =
                            "the accepted counter could not pass the ordinary order acceptance boundary";
                        return false;
                    }

                    return true;

                case IntercolonyNegotiationDecision.Countered:
                    if (!evaluation.HasFinalCounter)
                    {
                        failureReason =
                            "the evaluator returned Countered without its required final counter";
                        return false;
                    }

                    if (!ValidateTerms(
                            opportunity, evaluation.FinalCounterTerms, out failureReason) ||
                        !opportunity.TryRecordFinalCounter(evaluation.FinalCounterTerms))
                    {
                        failureReason = failureReason ??
                            "the evaluator's final counter could not enter the opportunity state machine";
                        return false;
                    }

                    return true;

                case IntercolonyNegotiationDecision.Refused:
                    if (!opportunity.TryRecordCounterpartyRefusal())
                    {
                        failureReason =
                            "the refusal could not enter the opportunity state machine";
                        return false;
                    }

                    // Refusal is deliberately non-destructive: the advertised original terms
                    // remain available for a normal accept, but the refusal state closes the
                    // counter edge so repeated bargaining cannot become an exploit.
                    return true;

                default:
                    failureReason = "the evaluator returned an unknown negotiation decision";
                    return false;
            }
        }

        /// <summary>
        /// Accepts the one final counter stored on the opportunity. There is no overload that
        /// accepts arbitrary new terms here: the only accepted package is the exact persisted
        /// final counter, and SalesOrderService performs the normal exactly-once consumption.
        /// </summary>
        public static SalesOrder AcceptFinalCounter(
            IntercolonyWorldComponent state, MarketOpportunity opportunity)
        {
            if (state == null || opportunity == null ||
                !opportunity.TryGetFinalCounterTerms(out IntercolonyNegotiationTerms finalCounter))
            {
                return null;
            }

            return SalesOrderService.AcceptNegotiatedTerms(
                state, opportunity, finalCounter, acceptingFinalCounter: true);
        }

        /// <summary>
        /// Declines the original offer or the pending final counter and removes the non-binding
        /// opportunity. No order is created, and the terminal opportunity state prevents a stale
        /// reference from accepting it later.
        /// </summary>
        public static bool TryDecline(
            IntercolonyWorldComponent state, MarketOpportunity opportunity)
        {
            if (state == null || opportunity == null || !opportunity.TryDecline())
            {
                return false;
            }

            state.RemoveOpportunity(opportunity);
            return true;
        }

        private static bool TryBuildProposal(
            IntercolonyWorldComponent state,
            MarketOpportunity opportunity,
            IntercolonyNegotiationTerms proposedTerms,
            out IntercolonyNegotiationProposal proposal,
            out string failureReason)
        {
            proposal = null;
            failureReason = null;

            if (!ValidateTerms(opportunity, proposedTerms, out failureReason))
            {
                return false;
            }

            Settlement settlement = IntercolonyMarketAccess.FindSettlement(opportunity.settlementId);
            if (settlement == null)
            {
                failureReason = "the buyer no longer exists";
                return false;
            }

            if (!IntercolonyMarketAccess.IsAccessible(settlement, out failureReason))
            {
                return false;
            }

            SettlementEconomicProfile profile = state.GetProfile(settlement);
            if (profile == null)
            {
                failureReason = "the buyer has no economic profile";
                return false;
            }

            IntercolonyProductCategory? category =
                IntercolonyProductClassifier.Classify(opportunity.thingDef);
            if (!category.HasValue)
            {
                failureReason = "the opportunity's product has no market category";
                return false;
            }

            proposal = new IntercolonyNegotiationProposal
            {
                state = state,
                profile = profile,
                thingDef = opportunity.thingDef,
                category = category.Value,
                direction = IntercolonyNegotiationDirection.Sale,
                originalTerms = new IntercolonyNegotiationTerms(
                    opportunity.quantity,
                    opportunity.unitPrice,
                    opportunity.deadlineDays,
                    opportunity.fulfillment),
                proposedTerms = proposedTerms.Clone(),
                fulfillmentModeChangeAllowed = opportunity.SupportsBothFulfillmentModes
            };

            return true;
        }

        private static bool ValidateTerms(
            MarketOpportunity opportunity,
            IntercolonyNegotiationTerms terms,
            out string failureReason)
        {
            failureReason = null;
            if (opportunity == null || terms == null)
            {
                failureReason = "an opportunity and proposed terms are required";
                return false;
            }

            if (terms.quantity < 1 || terms.quantity > opportunity.quantity)
            {
                failureReason =
                    $"quantity must stay between 1 and the advertised {opportunity.quantity} units";
                return false;
            }

            if (terms.unitPrice <= 0f || float.IsNaN(terms.unitPrice) ||
                float.IsInfinity(terms.unitPrice))
            {
                failureReason = "unit price must be a finite positive amount";
                return false;
            }

            if (terms.deadlineDays < 0)
            {
                failureReason = "deadline cannot be negative";
                return false;
            }

            if (terms.fulfillment != FulfillmentMode.SellerDelivery &&
                terms.fulfillment != FulfillmentMode.BuyerPickup)
            {
                failureReason = "fulfillment mode is not a supported sale mode";
                return false;
            }

            if (terms.fulfillment != opportunity.fulfillment &&
                !opportunity.SupportsBothFulfillmentModes)
            {
                failureReason = "this opportunity does not support a fulfillment-mode change";
                return false;
            }

            return true;
        }
    }
}
