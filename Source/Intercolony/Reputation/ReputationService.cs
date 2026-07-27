using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Records commercial events and turns reputation into market effects
    /// (DESIGN.md §27, §28, §70 ReputationService, Phase 13 §106).
    ///
    /// Every effect here is bounded. §28 is explicit: "Avoid runaway positive feedback that
    /// turns high reputation into guaranteed infinite profit." A perfect record should feel
    /// like a better business relationship, not like a cheat code, so the caps are deliberately
    /// modest and the price effect in particular is small.
    /// </summary>
    public static class ReputationService
    {
        // --- Event weights (§27's positive and negative input lists) ---
        private const float CompletedOnTime = 4f;
        private const float CompletedLate = 1f;
        private const float OrderFailed = -12f;
        private const float OrderCancelled = -6f;
        private const float PurchaseCompleted = 2f;
        private const float PurchaseCancelled = -4f;

        /// <summary>
        /// Bonus for a large contract, capped. §27 lists "large contract completion" as a
        /// positive, but scaling without a ceiling would make one enormous order worth more
        /// than years of steady trade.
        /// </summary>
        private static float SizeBonus(int totalPayment)
        {
            return Mathf.Clamp(totalPayment / 2000f, 0f, 3f);
        }

        public static CommercialReputation For(IntercolonyWorldComponent state, Settlement settlement)
        {
            if (state == null || settlement == null)
            {
                return null;
            }

            return state.GetOrCreateReputation(settlement);
        }

        public static CommercialReputation ForSettlement(IntercolonyWorldComponent state, int settlementId)
        {
            return For(state, IntercolonyMarketAccess.FindSettlement(settlementId));
        }

        // --- Event hooks -----------------------------------------------------------------

        public static void NoteOrderCompleted(
            IntercolonyWorldComponent state, SalesOrder order, bool onTime)
        {
            CommercialReputation rep = ForSettlement(state, order?.settlementId ?? -1);
            if (rep == null)
            {
                return;
            }

            if (onTime)
            {
                rep.ordersCompleted++;
                rep.Adjust(CompletedOnTime + SizeBonus(order.TotalPayment));
            }
            else
            {
                // Late but delivered still counts as delivered — barely.
                rep.ordersLate++;
                rep.Adjust(CompletedLate);
            }

            IntercolonyLog.Verbose($"Reputation {rep.settlementName}: {rep.ScoreDisplay} ({rep.TierLabel()})");
        }

        public static void NoteOrderFailed(IntercolonyWorldComponent state, SalesOrder order)
        {
            CommercialReputation rep = ForSettlement(state, order?.settlementId ?? -1);
            if (rep == null)
            {
                return;
            }

            rep.ordersFailed++;
            rep.Adjust(OrderFailed);
            IntercolonyLog.Message(
                $"{rep.settlementName} noted a failed order. Commercial reputation now " +
                $"{rep.ScoreDisplay}/100 ({rep.TierLabel()}).");
        }

        public static void NoteOrderCancelled(IntercolonyWorldComponent state, SalesOrder order)
        {
            CommercialReputation rep = ForSettlement(state, order?.settlementId ?? -1);
            if (rep == null)
            {
                return;
            }

            rep.ordersCancelled++;
            rep.Adjust(OrderCancelled);
        }

        public static void NotePurchaseCompleted(IntercolonyWorldComponent state, PurchaseOrder order)
        {
            CommercialReputation rep = ForSettlement(state, order?.settlementId ?? -1);
            if (rep == null)
            {
                return;
            }

            // §27 lists "prompt payment" as a positive, and payment is taken up front.
            rep.purchasesCompleted++;
            rep.Adjust(PurchaseCompleted);
        }

        public static void NotePurchaseCancelled(IntercolonyWorldComponent state, PurchaseOrder order)
        {
            CommercialReputation rep = ForSettlement(state, order?.settlementId ?? -1);
            if (rep == null)
            {
                return;
            }

            rep.purchaseCancellations++;
            rep.Adjust(PurchaseCancelled);
        }

        // --- Effects (§28) ---------------------------------------------------------------

        /// <summary>
        /// Score for a settlement, or the neutral default when there is no record with it.
        /// </summary>
        public static float ScoreFor(IntercolonyWorldComponent state, Settlement settlement)
        {
            if (state == null || settlement == null)
            {
                return CommercialReputation.StartingScore;
            }

            CommercialReputation rep = state.FindReputation(settlement.ID);
            return rep?.Score ?? CommercialReputation.StartingScore;
        }

        /// <summary>
        /// How much more likely a settlement is to post demand (§28 "more frequent
        /// opportunities"). Bounded to roughly half again at the top, so a good record widens
        /// the pipeline without flooding it.
        /// </summary>
        public static float OpportunityFrequencyFactor(float score)
        {
            return Mathf.Lerp(0.6f, 1.5f, Normalized(score));
        }

        /// <summary>
        /// Larger contracts for trusted partners (§28 "larger orders"). Capped at +40%: a
        /// bigger order is more profit, and stacking that with a better price and higher
        /// frequency is exactly the runaway §28 warns about.
        /// </summary>
        public static float OpportunitySizeFactor(float score)
        {
            return Mathf.Lerp(0.75f, 1.4f, Normalized(score));
        }

        /// <summary>
        /// §28 says "slightly better prices", and means it. The whole span is about 13%,
        /// deliberately the smallest of the three effects — price is the one that compounds
        /// with everything else.
        /// </summary>
        public static PriceFactor PriceFactorFor(float score)
        {
            float multiplier = Mathf.Lerp(0.95f, 1.08f, Normalized(score));
            return new PriceFactor("Trading record", multiplier);
        }

        /// <summary>
        /// Better deadlines for trusted partners (§28). Extra days, not a multiplier, so it
        /// helps a short deadline most — which is where the pressure actually is.
        /// </summary>
        public static int DeadlineBonusDays(float score)
        {
            return Mathf.RoundToInt(Mathf.Lerp(-2f, 4f, Normalized(score)));
        }

        private static float Normalized(float score)
        {
            return Mathf.Clamp01(score / CommercialReputation.MaxScore);
        }
    }
}
