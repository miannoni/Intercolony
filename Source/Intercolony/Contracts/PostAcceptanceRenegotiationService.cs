using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>One of the bounded concessions available on an accepted sales order.</summary>
    public enum RenegotiationRequestKind
    {
        DeadlineExtension,
        QuantityReduction,
        MutualCancellation
    }

    /// <summary>
    /// Immutable input for one post-acceptance request. Factory methods keep terms that do not
    /// belong to a request kind at their neutral values.
    /// </summary>
    public sealed class RenegotiationRequest
    {
        /// <summary>The concession being requested.</summary>
        public readonly RenegotiationRequestKind kind;

        /// <summary>Additional days for a <see cref="RenegotiationRequestKind.DeadlineExtension"/>.</summary>
        public readonly int extensionDays;

        /// <summary>New bound quantity for a <see cref="RenegotiationRequestKind.QuantityReduction"/>.</summary>
        public readonly int newQuantity;

        private RenegotiationRequest(
            RenegotiationRequestKind kind, int extensionDays, int newQuantity)
        {
            this.kind = kind;
            this.extensionDays = extensionDays;
            this.newQuantity = newQuantity;
        }

        /// <summary>Creates a request to move the current deadline later.</summary>
        public static RenegotiationRequest DeadlineExtension(int extensionDays)
        {
            return new RenegotiationRequest(
                RenegotiationRequestKind.DeadlineExtension, extensionDays, 0);
        }

        /// <summary>Creates a request to bind a smaller quantity.</summary>
        public static RenegotiationRequest QuantityReduction(int newQuantity)
        {
            return new RenegotiationRequest(
                RenegotiationRequestKind.QuantityReduction, 0, newQuantity);
        }

        /// <summary>Creates a request to end the order by mutual agreement.</summary>
        public static RenegotiationRequest MutualCancellation()
        {
            return new RenegotiationRequest(
                RenegotiationRequestKind.MutualCancellation, 0, 0);
        }
    }

    /// <summary>
    /// Owns the finite post-acceptance renegotiation transition for a binding sales order. Each
    /// request kind reaches the common negotiation evaluator at most once per order.
    /// </summary>
    public static class PostAcceptanceRenegotiationService
    {
        /// <summary>Largest single deadline extension the player may request.</summary>
        public const int MaxExtensionDays = 15;

        /// <summary>
        /// Evaluates one valid request against the order's current bound terms and applies it only
        /// when accepted. A false return means no counterparty answer was reached.
        /// </summary>
        public static bool TryRequest(
            IntercolonyWorldComponent state,
            SalesOrder order,
            RenegotiationRequest request,
            out IntercolonyNegotiationResult evaluation,
            out string failureReason)
        {
            evaluation = null;
            failureReason = null;

            if (state == null || order == null || request == null)
            {
                failureReason = "world state, order and request are required";
                return false;
            }

            if (order.status != SalesOrderStatus.Accepted)
            {
                failureReason = "only an accepted sales order can be renegotiated";
                return false;
            }

            if (!order.CanRequest(request.kind))
            {
                failureReason = "this renegotiation request kind was already attempted on the order";
                return false;
            }

            if (!TryBuildProposal(
                    state, order, request,
                    out IntercolonyNegotiationProposal proposal,
                    out failureReason))
            {
                return false;
            }

            evaluation = IntercolonyNegotiationEvaluator.Evaluate(proposal);
            order.MarkRenegotiationAttempted(request.kind);

            if (evaluation.Decision != IntercolonyNegotiationDecision.Accepted)
            {
                return true;
            }

            switch (request.kind)
            {
                case RenegotiationRequestKind.DeadlineExtension:
                    order.deadlineTick += request.extensionDays * GenDate.TicksPerDay;
                    return true;

                case RenegotiationRequestKind.QuantityReduction:
                    order.line.quantity = request.newQuantity;
                    return true;

                case RenegotiationRequestKind.MutualCancellation:
                    if (SalesOrderService.CancelByMutualAgreement(state, order))
                    {
                        return true;
                    }

                    failureReason = "the accepted mutual cancellation could not close the order";
                    return false;

                default:
                    failureReason = "the renegotiation request kind is not supported";
                    return false;
            }
        }

        private static bool TryBuildProposal(
            IntercolonyWorldComponent state,
            SalesOrder order,
            RenegotiationRequest request,
            out IntercolonyNegotiationProposal proposal,
            out string failureReason)
        {
            proposal = null;
            failureReason = null;

            if (!ValidateRequest(order, request, out int currentDeadlineDays, out failureReason))
            {
                return false;
            }

            Settlement settlement = IntercolonyMarketAccess.FindSettlement(order.settlementId);
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
                IntercolonyProductClassifier.Classify(order.ThingDef);
            if (!category.HasValue)
            {
                failureReason = "the order's product has no market category";
                return false;
            }

            IntercolonyNegotiationTerms originalTerms = new IntercolonyNegotiationTerms(
                order.Quantity,
                order.unitPrice,
                currentDeadlineDays,
                order.fulfillment);
            IntercolonyNegotiationTerms proposedTerms = originalTerms.Clone();
            bool cancellationRequested = false;

            switch (request.kind)
            {
                case RenegotiationRequestKind.DeadlineExtension:
                    proposedTerms.deadlineDays += request.extensionDays;
                    break;

                case RenegotiationRequestKind.QuantityReduction:
                    proposedTerms.quantity = request.newQuantity;
                    break;

                case RenegotiationRequestKind.MutualCancellation:
                    cancellationRequested = true;
                    break;

                default:
                    failureReason = "the renegotiation request kind is not supported";
                    return false;
            }

            proposal = new IntercolonyNegotiationProposal
            {
                state = state,
                profile = profile,
                thingDef = order.ThingDef,
                category = category.Value,
                direction = IntercolonyNegotiationDirection.Sale,
                originalTerms = originalTerms,
                proposedTerms = proposedTerms,
                counterAllowed = false,
                cancellationRequested = cancellationRequested
            };

            return true;
        }

        private static bool ValidateRequest(
            SalesOrder order,
            RenegotiationRequest request,
            out int currentDeadlineDays,
            out string failureReason)
        {
            currentDeadlineDays = 0;
            failureReason = null;

            if (order.line == null || order.ThingDef == null || order.Quantity <= 0)
            {
                failureReason = "the order has no valid bound product or quantity";
                return false;
            }

            if (order.unitPrice < 0.01f || float.IsNaN(order.unitPrice) ||
                float.IsInfinity(order.unitPrice))
            {
                failureReason = "the order has no valid bound unit price";
                return false;
            }

            if (order.fulfillment != FulfillmentMode.SellerDelivery &&
                order.fulfillment != FulfillmentMode.BuyerPickup)
            {
                failureReason = "the order has an unsupported fulfillment mode";
                return false;
            }

            currentDeadlineDays =
                (order.deadlineTick - order.acceptedTick) / GenDate.TicksPerDay;
            if (currentDeadlineDays < 0)
            {
                failureReason = "the order has an invalid deadline";
                return false;
            }

            switch (request.kind)
            {
                case RenegotiationRequestKind.DeadlineExtension:
                    if (request.extensionDays < 1 || request.extensionDays > MaxExtensionDays)
                    {
                        failureReason =
                            $"deadline extension must be between 1 and {MaxExtensionDays} days";
                        return false;
                    }

                    if (order.IsOverdue(GenTicks.TicksGame))
                    {
                        failureReason = "an overdue order cannot request a deadline extension";
                        return false;
                    }

                    return true;

                case RenegotiationRequestKind.QuantityReduction:
                    int minimumQuantity = Mathf.Max(1, order.deliveredQuantity);
                    if (request.newQuantity < minimumQuantity ||
                        request.newQuantity >= order.Quantity)
                    {
                        failureReason =
                            $"new quantity must be at least {minimumQuantity} and below the current {order.Quantity}";
                        return false;
                    }

                    return true;

                case RenegotiationRequestKind.MutualCancellation:
                    return true;

                default:
                    failureReason = "the renegotiation request kind is not supported";
                    return false;
            }
        }
    }
}
