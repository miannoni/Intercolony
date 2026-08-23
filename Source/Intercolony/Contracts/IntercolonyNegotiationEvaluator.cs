using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// The two commercial directions that Stage 5 and Stage 6 need. Keeping this narrow is
    /// deliberate: a procurement proposal is the mirror of a sale, not evidence that the mod
    /// needs a generic negotiation framework for every future system.
    /// </summary>
    public enum IntercolonyNegotiationDirection
    {
        Sale,
        Purchase
    }

    /// <summary>What the counterparty decided about one proposed change to its terms.</summary>
    public enum IntercolonyNegotiationDecision
    {
        Accepted,
        Countered,
        Refused
    }

    /// <summary>
    /// Terms in a negotiation proposal. This is an ephemeral input model, not persisted state:
    /// Stage 5A only answers a question and does not create an obligation or a counteroffer action.
    /// </summary>
    public sealed class IntercolonyNegotiationTerms
    {
        public int quantity;
        public float unitPrice;
        public int deadlineDays;
        public FulfillmentMode fulfillment;

        public IntercolonyNegotiationTerms()
        {
        }

        public IntercolonyNegotiationTerms(
            int quantity, float unitPrice, int deadlineDays, FulfillmentMode fulfillment)
        {
            this.quantity = quantity;
            this.unitPrice = unitPrice;
            this.deadlineDays = deadlineDays;
            this.fulfillment = fulfillment;
        }

        public IntercolonyNegotiationTerms Clone()
        {
            return new IntercolonyNegotiationTerms(
                quantity, unitPrice, deadlineDays, fulfillment);
        }

        public override string ToString()
        {
            return $"{quantity} units at {unitPrice:F2}/unit, {deadlineDays} deadline-days, " +
                   $"{fulfillment}";
        }
    }

    /// <summary>
    /// Read-only context for one evaluation. The caller supplies the already authoritative
    /// settlement profile and world state; the evaluator does not create reputation, market or
    /// brand records merely because somebody asks what a counterparty might do.
    /// </summary>
    public sealed class IntercolonyNegotiationProposal
    {
        public IntercolonyWorldComponent state;
        public SettlementEconomicProfile profile;
        public ThingDef thingDef;
        public IntercolonyProductCategory category;
        public IntercolonyNegotiationDirection direction = IntercolonyNegotiationDirection.Sale;
        public IntercolonyNegotiationTerms originalTerms;
        public IntercolonyNegotiationTerms proposedTerms;

        /// <summary>
        /// Some opportunities support only their advertised logistics mode. The evaluator treats
        /// an unsupported mode change as invalid instead of pretending a later UI can fulfil it.
        /// </summary>
        public bool fulfillmentModeChangeAllowed = true;
    }

    /// <summary>One signed contribution to the acceptance score, for debug output.</summary>
    public struct IntercolonyNegotiationFactor
    {
        public string label;
        public float contribution;
        public string detail;

        public IntercolonyNegotiationFactor(string label, float contribution, string detail)
        {
            this.label = label;
            this.contribution = contribution;
            this.detail = detail;
        }
    }

    /// <summary>
    /// The complete answer from <see cref="IntercolonyNegotiationEvaluator"/>. A countered
    /// result contains one <see cref="FinalCounterTerms"/> value. It is intentionally named as a
    /// final counter rather than exposing a method that evaluates another counter, so the model
    /// cannot imply an unbounded back-and-forth loop.
    /// </summary>
    public sealed class IntercolonyNegotiationResult
    {
        public IntercolonyNegotiationDirection Direction;
        public IntercolonyNegotiationDecision Decision;
        public float AcceptanceScore;
        public string RefusalReason;
        public IntercolonyNegotiationTerms OriginalTerms;
        public IntercolonyNegotiationTerms ProposedTerms;
        public IntercolonyNegotiationTerms FinalCounterTerms;
        public readonly List<IntercolonyNegotiationFactor> Factors =
            new List<IntercolonyNegotiationFactor>();

        public bool HasFinalCounter =>
            Decision == IntercolonyNegotiationDecision.Countered && FinalCounterTerms != null;
    }

    /// <summary>
    /// Central Stage 5A negotiation read model.
    ///
    /// The service is deliberately a pure decision layer. It reads the existing effective
    /// economy, profile, reputation and brand owners, but it does not alter any of them and no
    /// UI or order service calls it yet. This keeps one future counteroffer action from growing
    /// its own private version of "how trusted is this settlement?".
    /// </summary>
    public static class IntercolonyNegotiationEvaluator
    {
        // --- Outcome thresholds ----------------------------------------------------------

        /// <summary>
        /// Small requests begin slightly open rather than being refused by a neutral partner.
        /// Naming the starting point keeps a late calibration pass from hiding it in the formula.
        /// </summary>
        private const float BaseWillingness = 0.10f;

        /// <summary>
        /// A score at or above this point is a clean acceptance; below it the counterparty still
        /// needs to protect one of its terms.
        /// </summary>
        private const float AcceptedScoreThreshold = 0.10f;

        /// <summary>
        /// This is the lowest score that can produce one final counter. Below it the proposed
        /// package is too far from a workable deal even after the counterparty moves halfway back.
        /// </summary>
        private const float CounteredScoreThreshold = -0.45f;

        /// <summary>
        /// A hard refusal sits below the ordinary score bands. Extreme requests use this floor so
        /// reputation cannot become a universal permission to demand anything.
        /// </summary>
        private const float HardRefusalScore = -1.25f;

        /// <summary>
        /// The counterparty moves halfway toward its original terms. One named fraction gives the
        /// later calibration pass a single knob without creating a negotiation loop.
        /// </summary>
        private const float CounterMoveTowardOriginalFraction = 0.50f;

        // --- Input normalization --------------------------------------------------------

        /// <summary>Zero quantity cannot describe a commercial lot.</summary>
        private const int MinimumQuantity = 1;

        /// <summary>A non-positive price cannot be a usable binding term.</summary>
        private const float MinimumUnitPrice = 0.01f;

        /// <summary>Same-day fulfilment is valid; negative time is not.</summary>
        private const int MinimumDeadlineDays = 0;

        /// <summary>
        /// Profiles should have positive weights, but this floor keeps a malformed or future
        /// synthetic profile from dividing by zero and turning a debug read into NaN.
        /// </summary>
        private const float MinimumBaselineForRatio = 0.02f;

        /// <summary>Neutral ratio for an unchanged market, identity or signed term.</summary>
        private const float NeutralRatio = 1f;

        /// <summary>Lowest normalized signal; all bounded inputs use the same readable range.</summary>
        private const float MinimumSignal = -1f;

        /// <summary>Highest normalized signal; caps keep one input from becoming an override.</summary>
        private const float MaximumSignal = 1f;

        /// <summary>
        /// A quarter-price change is the full ordinary price signal. Larger changes remain visible
        /// through the hard refusal gate rather than making the score unbounded.
        /// </summary>
        private const float PriceChangeSignalSpan = 0.25f;

        /// <summary>
        /// A fifty-percent quantity change is the full ordinary quantity signal, leaving smaller
        /// reductions negotiable while making a near-zero delivery visibly severe.
        /// </summary>
        private const float QuantityChangeSignalSpan = 0.50f;

        /// <summary>
        /// Fourteen days is a meaningful ordinary deadline movement without making a one-day
        /// extension disappear in a long contract.
        /// </summary>
        private const float DeadlineChangeSignalSpanDays = 14f;

        /// <summary>
        /// The effective market ratio can move by half before its score contribution saturates.
        /// The underlying economy remains authoritative; this is only the negotiation readout.
        /// </summary>
        private const float MarketConditionSignalSpan = 0.50f;

        /// <summary>
        /// Category appetite or supply one point away from neutral is the full identity signal.
        /// </summary>
        private const float IdentitySignalSpan = 1f;

        /// <summary>
        /// A 0.30 event multiplier deviation is enough to represent a meaningful urgent shock.
        /// </summary>
        private const float EventUrgencySignalSpan = 0.30f;

        /// <summary>
        /// Reputation is centered on its existing neutral score and reaches a full signal at the
        /// existing 0/100 endpoints; no second relationship meter is introduced here.
        /// </summary>
        private const float ReputationSignalHalfRange = 50f;

        /// <summary>Brand already owns a bounded -100..100 scale, so this only normalizes it.</summary>
        private const float BrandSignalRange = 100f;

        /// <summary>Destitute settlements have the least room to absorb an adverse term.</summary>
        private const float DestituteWealthSignal = -1f;

        /// <summary>Modest settlements have slightly less room than the neutral midpoint.</summary>
        private const float ModestWealthSignal = -0.35f;

        /// <summary>Comfortable settlements have slightly more room than the neutral midpoint.</summary>
        private const float ComfortableWealthSignal = 0.35f;

        /// <summary>Wealthy settlements have the most room, but remain bounded at one signal unit.</summary>
        private const float WealthyWealthSignal = 1f;

        // --- Tunable contribution weights ------------------------------------------------

        /// <summary>Price is the most visible concession, so it carries the largest term weight.</summary>
        private const float PriceChangeWeight = 0.90f;

        /// <summary>Quantity changes affect fulfilment burden, but are less absolute than price.</summary>
        private const float QuantityChangeWeight = 0.70f;

        /// <summary>Deadlines matter, while the existing deadline remains a concrete anchor.</summary>
        private const float DeadlineChangeWeight = 0.45f;

        /// <summary>Logistics changes matter, but only one of the two existing modes is involved.</summary>
        private const float FulfillmentChangeWeight = 0.35f;

        /// <summary>Current demand or supply should help explain willingness without dominating it.</summary>
        private const float MarketConditionWeight = 0.35f;

        /// <summary>Stable category identity is meaningful context, not an absolute restriction.</summary>
        private const float IdentityWeight = 0.20f;

        /// <summary>Wealth gives a counterparty room to absorb terms, not a second price formula.</summary>
        private const float WealthWeight = 0.18f;

        /// <summary>
        /// Reputation changes willingness to trust a changed promise. This is intentionally a
        /// behavioural contribution, not another price multiplier.
        /// </summary>
        private const float ReputationWeight = 0.60f;

        /// <summary>Relevant product brand gives a seller leverage on a sale only.</summary>
        private const float BrandWeight = 0.25f;

        /// <summary>
        /// An urgent event lets a counterparty compromise on a player-favouring request. It is
        /// applied to concession tolerance, not added as another market price multiplier.
        /// </summary>
        private const float EventUrgencyWeight = 0.25f;

        /// <summary>
        /// Trusted partners can reduce the quantity they are willing to accept on a sale without
        /// being treated like strangers. This is the new relationship behaviour §5.2 asks for.
        /// </summary>
        private const float ReputationQuantityToleranceWeight = 0.30f;

        /// <summary>Trusted partners also receive bounded tolerance for a deadline extension.</summary>
        private const float ReputationDeadlineToleranceWeight = 0.20f;

        /// <summary>
        /// Positive normalized term burden at this weighted amount represents a full event-based
        /// compromise request. It prevents an event from multiplying every individual term.
        /// </summary>
        private const float EventCompromiseBurdenSpan = 1f;

        // --- Hard refusal gates ----------------------------------------------------------

        /// <summary>
        /// Asking for more than 75% extra price is outside one limited counter round, regardless
        /// of reputation. A counterparty can be trusted and still have a finite market boundary.
        /// </summary>
        private const float PriceHardRefusalBurden = 0.75f;

        /// <summary>Requesting or removing more than 75% of the lot is not a modest counter.</summary>
        private const float QuantityHardRefusalBurden = 0.75f;

        /// <summary>
        /// A two-month adverse deadline movement is too large for a one-round counter and avoids
        /// letting reputation turn an expired-looking promise into an automatic acceptance.
        /// </summary>
        private const float DeadlineHardRefusalBurdenDays = 60f;

        /// <summary>
        /// Several individually moderate concessions can still be collectively unreasonable.
        /// This cap catches that package without inventing a separate personality system.
        /// </summary>
        private const float CombinedHardRefusalBurden = 1.25f;

        /// <summary>Modes are represented as one bounded burden unit when they change.</summary>
        private const float FulfillmentModeBurden = 1f;

        /// <summary>
        /// Evaluates the proposed terms from the counterparty's perspective.
        /// </summary>
        public static IntercolonyNegotiationResult Evaluate(
            IntercolonyNegotiationProposal proposal)
        {
            if (!TryValidate(proposal, out string validationFailure))
            {
                return InvalidResult(proposal, validationFailure);
            }

            IntercolonyNegotiationResult result = new IntercolonyNegotiationResult
            {
                Direction = proposal.direction,
                OriginalTerms = proposal.originalTerms.Clone(),
                ProposedTerms = proposal.proposedTerms.Clone()
            };

            SettlementEconomicProfile profile = proposal.profile;
            IntercolonyNegotiationTerms original = proposal.originalTerms;
            IntercolonyNegotiationTerms proposed = proposal.proposedTerms;

            float score = BaseWillingness;
            result.Factors.Add(new IntercolonyNegotiationFactor(
                "Counterparty baseline", BaseWillingness,
                "A workable proposal starts open to one bounded change."));

            float priceBurden = proposal.direction == IntercolonyNegotiationDirection.Sale
                ? proposed.unitPrice / original.unitPrice - NeutralRatio
                : NeutralRatio - proposed.unitPrice / original.unitPrice;
            float priceSignal = SignedSignal(priceBurden, PriceChangeSignalSpan);
            float priceContribution = -priceSignal * PriceChangeWeight;
            score += priceContribution;
            result.Factors.Add(new IntercolonyNegotiationFactor(
                "Price change", priceContribution,
                $"counterparty burden {priceBurden:+0.0%;-0.0%;0.0%}; " +
                $"{proposal.direction} perspective"));

            float quantityDelta = proposed.quantity / (float)original.quantity - NeutralRatio;
            float quantityBurden = proposal.direction == IntercolonyNegotiationDirection.Sale
                ? -quantityDelta
                : quantityDelta;
            float quantitySignal = SignedSignal(quantityBurden, QuantityChangeSignalSpan);
            float quantityContribution = -quantitySignal * QuantityChangeWeight;
            score += quantityContribution;
            result.Factors.Add(new IntercolonyNegotiationFactor(
                "Quantity change", quantityContribution,
                $"counterparty burden {quantityBurden:+0.0%;-0.0%;0.0%}; " +
                $"{original.quantity} -> {proposed.quantity} units"));

            float deadlineDelta = proposed.deadlineDays - original.deadlineDays;
            float deadlineBurden = proposal.direction == IntercolonyNegotiationDirection.Sale
                ? deadlineDelta
                : -deadlineDelta;
            float deadlineSignal = SignedSignal(
                deadlineBurden, DeadlineChangeSignalSpanDays);
            float deadlineContribution = -deadlineSignal * DeadlineChangeWeight;
            score += deadlineContribution;
            result.Factors.Add(new IntercolonyNegotiationFactor(
                "Deadline change", deadlineContribution,
                $"counterparty burden {deadlineBurden:+0.0;-0.0;0.0} day(s); " +
                $"{original.deadlineDays} -> {proposed.deadlineDays}"));

            float originalFulfillmentBurden = FulfillmentBurden(
                proposal.direction, original.fulfillment);
            float proposedFulfillmentBurden = FulfillmentBurden(
                proposal.direction, proposed.fulfillment);
            float fulfillmentBurden = proposedFulfillmentBurden - originalFulfillmentBurden;
            float fulfillmentSignal = Mathf.Clamp(
                fulfillmentBurden, MinimumSignal, MaximumSignal);
            float fulfillmentContribution = -fulfillmentSignal * FulfillmentChangeWeight;
            score += fulfillmentContribution;
            result.Factors.Add(new IntercolonyNegotiationFactor(
                "Fulfillment mode", fulfillmentContribution,
                $"counterparty burden {fulfillmentBurden:+0.0;-0.0;0.0}; " +
                $"{original.fulfillment} -> {proposed.fulfillment}"));

            float baseline = proposal.direction == IntercolonyNegotiationDirection.Sale
                ? profile.BaseDemandFor(proposal.thingDef, proposal.category)
                : profile.BaseSupplyFor(proposal.category);
            float effective = proposal.direction == IntercolonyNegotiationDirection.Sale
                ? EffectiveEconomyService.EffectiveDemand(
                    proposal.state, profile, proposal.thingDef, proposal.category)
                : EffectiveEconomyService.EffectiveSupply(
                    proposal.state, profile, proposal.category);
            float marketRatio = effective / Mathf.Max(MinimumBaselineForRatio, baseline);
            float marketSignal = SignedSignal(
                marketRatio - NeutralRatio, MarketConditionSignalSpan);
            float marketContribution = marketSignal * MarketConditionWeight;
            score += marketContribution;
            result.Factors.Add(new IntercolonyNegotiationFactor(
                "Current market", marketContribution,
                $"{proposal.direction} {effective:F2} effective vs {baseline:F2} baseline; " +
                $"{profile.archetype} profile"));

            float identityBaseline = proposal.direction == IntercolonyNegotiationDirection.Sale
                ? profile.BaseDemandFor(proposal.category)
                : profile.BaseSupplyFor(proposal.category);
            float identitySignal = SignedSignal(
                identityBaseline - NeutralRatio, IdentitySignalSpan);
            float identityContribution = identitySignal * IdentityWeight;
            score += identityContribution;
            result.Factors.Add(new IntercolonyNegotiationFactor(
                "Settlement identity", identityContribution,
                $"{profile.archetype}; category baseline {identityBaseline:F2}"));

            float wealthSignal = WealthSignal(profile.wealthTier);
            float wealthContribution = wealthSignal * WealthWeight;
            score += wealthContribution;
            result.Factors.Add(new IntercolonyNegotiationFactor(
                "Settlement wealth", wealthContribution,
                $"{profile.wealthTier}; room to absorb a changed term"));

            CommercialReputation reputation = proposal.state.FindReputation(profile.settlementId);
            float reputationScore = reputation?.Score ?? CommercialReputation.StartingScore;
            float reputationSignal = SignedSignal(
                reputationScore - CommercialReputation.StartingScore,
                ReputationSignalHalfRange);

            // ReputationService.PriceFactorFor already affects transaction prices. This factor
            // changes willingness to accept a changed promise instead, so this evaluator never
            // multiplies the proposed price by reputation a second time. That is the trap §5.2
            // warns about: double-counting the same relationship benefit as both price and trust.
            float reputationContribution = reputationSignal * ReputationWeight;
            score += reputationContribution;
            result.Factors.Add(new IntercolonyNegotiationFactor(
                "Commercial reputation", reputationContribution,
                $"{reputationScore:F1}/100 ({reputation?.TierLabel() ?? "Known trader by default"})"));

            if (proposal.direction == IntercolonyNegotiationDirection.Sale)
            {
                float brandScore = EffectiveBrandService.GetEffectiveBrand(
                    proposal.state, proposal.thingDef);
                float brandSignal = SignedSignal(brandScore, BrandSignalRange);
                float brandContribution = brandSignal * BrandWeight;
                score += brandContribution;
                result.Factors.Add(new IntercolonyNegotiationFactor(
                    "Brand strength", brandContribution,
                    $"{brandScore:+0.0;-0.0;0.0}/100 for the offered product"));
            }

            // The targeted terms below are the qualitative reputation behaviour from §5.2. A
            // trusted counterparty can tolerate a bounded quantity reduction or extension; this
            // is deliberately not another price benefit and therefore does not duplicate the
            // existing ReputationService price factor.
            float positiveReputationSignal = Mathf.Max(0f, reputationSignal);
            float quantityToleranceContribution =
                Mathf.Max(0f, quantitySignal) * positiveReputationSignal *
                ReputationQuantityToleranceWeight;
            if (!Mathf.Approximately(quantityToleranceContribution, 0f))
            {
                score += quantityToleranceContribution;
                result.Factors.Add(new IntercolonyNegotiationFactor(
                    "Reputation: quantity tolerance", quantityToleranceContribution,
                    "trusted partners can ask for a bounded quantity concession"));
            }

            float deadlineToleranceContribution =
                Mathf.Max(0f, deadlineSignal) * positiveReputationSignal *
                ReputationDeadlineToleranceWeight;
            if (!Mathf.Approximately(deadlineToleranceContribution, 0f))
            {
                score += deadlineToleranceContribution;
                result.Factors.Add(new IntercolonyNegotiationFactor(
                    "Reputation: deadline tolerance", deadlineToleranceContribution,
                    "trusted partners can ask for a bounded extension or acceleration"));
            }

            Settlement settlement = profile.settlementId == EconomicEvent.NoSettlement
                ? null
                : IntercolonyMarketAccess.FindSettlement(profile.settlementId);
            float eventMultiplier = proposal.direction == IntercolonyNegotiationDirection.Sale
                ? EconomicEventService.DemandMultiplier(
                    proposal.state, settlement, proposal.category)
                : EconomicEventService.SupplyScarcityMultiplier(
                    proposal.state, settlement, proposal.category);
            float eventSignal = proposal.direction == IntercolonyNegotiationDirection.Sale
                ? SignedSignal(eventMultiplier - NeutralRatio, EventUrgencySignalSpan)
                : SignedSignal(NeutralRatio - eventMultiplier, EventUrgencySignalSpan);

            // Only a positive counterparty burden can be a request for urgent compromise. The
            // effective demand/supply factor above already includes the event in current market
            // conditions; using urgency only to change concession tolerance avoids counting the
            // same event as a second independent price multiplier.
            float weightedPositiveBurden =
                Mathf.Max(0f, priceSignal) * PriceChangeWeight +
                Mathf.Max(0f, quantitySignal) * QuantityChangeWeight +
                Mathf.Max(0f, deadlineSignal) * DeadlineChangeWeight +
                Mathf.Max(0f, fulfillmentSignal) * FulfillmentChangeWeight;
            float compromiseSignal = Mathf.Clamp01(
                weightedPositiveBurden / EventCompromiseBurdenSpan);
            float eventContribution = eventSignal * EventUrgencyWeight * compromiseSignal;
            if (!Mathf.Approximately(eventContribution, 0f) ||
                !Mathf.Approximately(eventSignal, 0f))
            {
                score += eventContribution;
                result.Factors.Add(new IntercolonyNegotiationFactor(
                    "Event urgency", eventContribution,
                    $"event multiplier x{eventMultiplier:F2}; " +
                    "applies only to a bounded concession request"));
            }

            List<string> hardRefusals = HardRefusalReasons(
                priceBurden, quantityBurden, deadlineBurden, weightedPositiveBurden,
                proposal.fulfillmentModeChangeAllowed, original, proposed);
            if (hardRefusals.Count > 0)
            {
                float beforeHardRefusal = score;
                score = Mathf.Min(score, HardRefusalScore);
                float hardRefusalContribution = score - beforeHardRefusal;
                result.Factors.Add(new IntercolonyNegotiationFactor(
                    "Hard refusal rule", hardRefusalContribution,
                    string.Join("; ", hardRefusals.ToArray())));
                result.RefusalReason = string.Join("; ", hardRefusals.ToArray());
            }

            result.AcceptanceScore = score;
            if (score >= AcceptedScoreThreshold && hardRefusals.Count == 0)
            {
                result.Decision = IntercolonyNegotiationDecision.Accepted;
            }
            else if (score >= CounteredScoreThreshold && hardRefusals.Count == 0)
            {
                result.Decision = IntercolonyNegotiationDecision.Countered;
                result.FinalCounterTerms = BuildFinalCounter(original, proposed);
            }
            else
            {
                result.Decision = IntercolonyNegotiationDecision.Refused;
                if (result.RefusalReason == null)
                {
                    result.RefusalReason =
                        "The combined terms remain below the counterparty's minimum willingness.";
                }
            }

            return result;
        }

        /// <summary>
        /// Renders the signed score contributions. Each row is the value already included in the
        /// result, rather than a second calculation, so the dump can expose the exact reasons a
        /// decision moved toward a final counter or refusal.
        /// </summary>
        public static string Explain(IntercolonyNegotiationResult result)
        {
            if (result == null)
            {
                return "Negotiation evaluation: no result.";
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(
                $"Negotiation evaluation: {result.Decision} ({result.Direction}), " +
                $"score {result.AcceptanceScore:+0.000;-0.000;0.000}.");
            if (!string.IsNullOrEmpty(result.RefusalReason))
            {
                sb.AppendLine($"Reason: {result.RefusalReason}");
            }

            float contributionTotal = 0f;
            foreach (IntercolonyNegotiationFactor factor in result.Factors)
            {
                contributionTotal += factor.contribution;
                sb.AppendLine(
                    $"  {factor.label,-34} {FormatSigned(factor.contribution)}  " +
                    factor.detail);
            }

            sb.AppendLine(
                $"  contribution total{new string(' ', 17)} " +
                $"{FormatSigned(contributionTotal)}");
            if (result.HasFinalCounter)
            {
                sb.AppendLine($"Final counter: {result.FinalCounterTerms}");
            }

            return sb.ToString();
        }

        private static bool TryValidate(
            IntercolonyNegotiationProposal proposal, out string failure)
        {
            if (proposal == null)
            {
                failure = "no negotiation proposal was supplied";
                return false;
            }

            if (proposal.state == null || proposal.profile == null)
            {
                failure = "world state and settlement profile are required";
                return false;
            }

            if ((int)proposal.category < 0 ||
                (int)proposal.category >= IntercolonyProductCategoryUtility.Count)
            {
                failure = "the product category is outside the market taxonomy";
                return false;
            }

            if (proposal.originalTerms == null || proposal.proposedTerms == null)
            {
                failure = "both original and proposed terms are required";
                return false;
            }

            if (!ValidTerms(proposal.originalTerms) || !ValidTerms(proposal.proposedTerms))
            {
                failure = "quantity, price, deadline or fulfilment mode is invalid";
                return false;
            }

            if (!proposal.fulfillmentModeChangeAllowed &&
                proposal.originalTerms.fulfillment != proposal.proposedTerms.fulfillment)
            {
                failure = "this opportunity does not support a fulfillment-mode change";
                return false;
            }

            failure = null;
            return true;
        }

        private static bool ValidTerms(IntercolonyNegotiationTerms terms)
        {
            return terms.quantity >= MinimumQuantity &&
                   terms.unitPrice >= MinimumUnitPrice &&
                   IsFinite(terms.unitPrice) &&
                   terms.deadlineDays >= MinimumDeadlineDays &&
                   ValidFulfillmentMode(terms.fulfillment);
        }

        private static bool ValidFulfillmentMode(FulfillmentMode mode)
        {
            return mode == FulfillmentMode.SellerDelivery ||
                   mode == FulfillmentMode.BuyerPickup;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static IntercolonyNegotiationResult InvalidResult(
            IntercolonyNegotiationProposal proposal, string reason)
        {
            IntercolonyNegotiationResult result = new IntercolonyNegotiationResult
            {
                Direction = proposal?.direction ?? IntercolonyNegotiationDirection.Sale,
                Decision = IntercolonyNegotiationDecision.Refused,
                AcceptanceScore = HardRefusalScore,
                RefusalReason = reason,
                OriginalTerms = proposal?.originalTerms?.Clone(),
                ProposedTerms = proposal?.proposedTerms?.Clone()
            };
            result.Factors.Add(new IntercolonyNegotiationFactor(
                "Validation", HardRefusalScore, reason));
            return result;
        }

        private static float SignedSignal(float value, float span)
        {
            if (!IsFinite(value) || span <= 0f)
            {
                return 0f;
            }

            return Mathf.Clamp(value / span, MinimumSignal, MaximumSignal);
        }

        private static float WealthSignal(IntercolonyWealthTier wealth)
        {
            switch (wealth)
            {
                case IntercolonyWealthTier.Destitute:
                    return DestituteWealthSignal;
                case IntercolonyWealthTier.Modest:
                    return ModestWealthSignal;
                case IntercolonyWealthTier.Comfortable:
                    return ComfortableWealthSignal;
                case IntercolonyWealthTier.Wealthy:
                    return WealthyWealthSignal;
                default:
                    return 0f;
            }
        }

        private static float FulfillmentBurden(
            IntercolonyNegotiationDirection direction, FulfillmentMode mode)
        {
            if (direction == IntercolonyNegotiationDirection.Sale)
            {
                return mode == FulfillmentMode.BuyerPickup ? FulfillmentModeBurden : 0f;
            }

            return mode == FulfillmentMode.SellerDelivery ? FulfillmentModeBurden : 0f;
        }

        private static List<string> HardRefusalReasons(
            float priceBurden,
            float quantityBurden,
            float deadlineBurden,
            float weightedPositiveBurden,
            bool fulfillmentModeChangeAllowed,
            IntercolonyNegotiationTerms original,
            IntercolonyNegotiationTerms proposed)
        {
            List<string> reasons = new List<string>();
            if (priceBurden > PriceHardRefusalBurden)
            {
                reasons.Add("price change exceeds the one-round negotiation boundary");
            }

            if (quantityBurden > QuantityHardRefusalBurden)
            {
                reasons.Add("quantity change exceeds the one-round negotiation boundary");
            }

            if (deadlineBurden > DeadlineHardRefusalBurdenDays)
            {
                reasons.Add("deadline change exceeds the one-round negotiation boundary");
            }

            if (weightedPositiveBurden > CombinedHardRefusalBurden)
            {
                reasons.Add("the combined counterparty burden is too extreme");
            }

            if (!fulfillmentModeChangeAllowed && original.fulfillment != proposed.fulfillment)
            {
                reasons.Add("fulfillment mode is fixed for this opportunity");
            }

            return reasons;
        }

        private static string FormatSigned(float value)
        {
            return value.ToString("+0.000;-0.000;0.000");
        }

        private static IntercolonyNegotiationTerms BuildFinalCounter(
            IntercolonyNegotiationTerms original, IntercolonyNegotiationTerms proposed)
        {
            // This is a bounded read-model answer, not a recursive call. The later action may
            // present this one final counter, but Stage 5.3 explicitly stops after this response.
            return new IntercolonyNegotiationTerms
            {
                quantity = Mathf.Max(
                    MinimumQuantity,
                    Mathf.RoundToInt(Mathf.Lerp(
                        proposed.quantity, original.quantity,
                        CounterMoveTowardOriginalFraction))),
                unitPrice = Mathf.Max(
                    MinimumUnitPrice,
                    Mathf.Lerp(
                        proposed.unitPrice, original.unitPrice,
                        CounterMoveTowardOriginalFraction)),
                deadlineDays = Mathf.Max(
                    MinimumDeadlineDays,
                    Mathf.RoundToInt(Mathf.Lerp(
                        proposed.deadlineDays, original.deadlineDays,
                        CounterMoveTowardOriginalFraction))),
                fulfillment = proposed.fulfillment == original.fulfillment
                    ? proposed.fulfillment
                    : original.fulfillment
            };
        }
    }
}
