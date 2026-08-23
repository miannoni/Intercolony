using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Reputation bands (DESIGN.md §27, whose example shows "Tier: Reliable Supplier").
    /// Tiers exist so the player can read their standing at a glance without decoding a number.
    /// </summary>
    public enum ReputationTier
    {
        Untrusted,
        Unproven,
        Known,
        Reliable,
        Preferred
    }

    /// <summary>
    /// A settlement's opinion of the colony as a trading partner (DESIGN.md §27).
    ///
    /// **Separate from faction goodwill by design.** §27 shows both side by side, and they
    /// answer different questions: goodwill is "will they shoot at you", reputation is "will
    /// they rely on you to deliver". A faction can like you politically and still consider
    /// you a flaky supplier, which is the whole point of tracking it.
    ///
    /// Held **per settlement**. §27's illustrative UI is headed by a faction name, but §8 is
    /// the stronger signal: "The primary economic actor should be a settlement, with
    /// faction-level defaults." Two settlements of the same faction can differ in every other
    /// economic respect, so it would be odd for their opinion of you to be shared — and a
    /// specific town remembering that you let *them* down is the more interesting relationship.
    /// </summary>
    public class CommercialReputation : IExposable
    {
        /// <summary>Neutral starting point — unproven rather than distrusted.</summary>
        public const float StartingScore = 50f;

        public const float MinScore = 0f;
        public const float MaxScore = 100f;

        public int settlementId = -1;
        public string settlementName = "";

        /// <summary>Owning faction at last update, for display beside faction goodwill.</summary>
        public string factionName = "";

        private float score = StartingScore;

        /// <summary>
        /// Last tier for which a relationship milestone was recorded. Neutral creation starts in
        /// Known so first contact does not create a history entry.
        /// </summary>
        public ReputationTier lastRecordedTier = ReputationTier.Known;

        // §27's counters, shown in the relationship view.
        public int ordersCompleted;
        public int ordersLate;
        public int ordersFailed;
        public int ordersCancelled;
        public int purchasesCompleted;
        public int purchaseCancellations;

        public CommercialReputation()
        {
        }

        public CommercialReputation(int settlementId, string settlementName, string factionName)
        {
            this.settlementId = settlementId;
            this.settlementName = settlementName;
            this.factionName = factionName;
        }

        public float Score => score;

        public int ScoreDisplay => Mathf.RoundToInt(score);

        public ReputationTier Tier
        {
            get
            {
                if (score < 20f) return ReputationTier.Untrusted;
                if (score < 45f) return ReputationTier.Unproven;
                if (score < 60f) return ReputationTier.Known;
                if (score < 80f) return ReputationTier.Reliable;
                return ReputationTier.Preferred;
            }
        }

        public string TierLabel()
        {
            return TierLabel(Tier);
        }

        /// <summary>Returns the player-facing label for a specified commercial reputation tier.</summary>
        public string TierLabel(ReputationTier tier)
        {
            switch (tier)
            {
                case ReputationTier.Untrusted: return "Untrusted";
                case ReputationTier.Unproven: return "Unproven";
                case ReputationTier.Known: return "Known trader";
                case ReputationTier.Reliable: return "Reliable supplier";
                default: return "Preferred partner";
            }
        }

        /// <summary>Total dealings, used to decide whether a record is worth showing at all.</summary>
        public int TotalDealings =>
            ordersCompleted + ordersLate + ordersFailed + ordersCancelled +
            purchasesCompleted + purchaseCancellations;

        /// <summary>
        /// Applies a change with diminishing returns near the top of the scale.
        ///
        /// §28 warns against "runaway positive feedback that turns high reputation into
        /// guaranteed infinite profit". Gains shrink as the score rises, so the last stretch
        /// to Preferred is slow, while penalties always land at full weight — a reputation
        /// should be harder to keep than to lose.
        /// </summary>
        public void Adjust(float delta)
        {
            if (delta > 0f)
            {
                float headroom = Mathf.Clamp01((MaxScore - score) / 50f);
                delta *= Mathf.Lerp(0.25f, 1f, headroom);
            }

            score = Mathf.Clamp(score + delta, MinScore, MaxScore);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref settlementId, "settlementId", -1);
            Scribe_Values.Look(ref settlementName, "settlementName", "");
            Scribe_Values.Look(ref factionName, "factionName", "");
            Scribe_Values.Look(ref score, "score", StartingScore);
            Scribe_Values.Look(ref lastRecordedTier, "lastRecordedTier", ReputationTier.Known);
            Scribe_Values.Look(ref ordersCompleted, "ordersCompleted", 0);
            Scribe_Values.Look(ref ordersLate, "ordersLate", 0);
            Scribe_Values.Look(ref ordersFailed, "ordersFailed", 0);
            Scribe_Values.Look(ref ordersCancelled, "ordersCancelled", 0);
            Scribe_Values.Look(ref purchasesCompleted, "purchasesCompleted", 0);
            Scribe_Values.Look(ref purchaseCancellations, "purchaseCancellations", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (factionName == null) factionName = "";
                if (settlementName == null) settlementName = "";
            }
        }

        public override string ToString()
        {
            return $"{settlementName} ({factionName}): {ScoreDisplay}/100 ({TierLabel()}) " +
                   $"[{ordersCompleted} done, {ordersLate} late, {ordersFailed} failed, " +
                   $"{ordersCancelled} cancelled, {purchasesCompleted} bought]";
        }
    }
}
