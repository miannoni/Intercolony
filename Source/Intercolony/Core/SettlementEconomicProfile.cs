using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Broad economic character of a settlement (DESIGN.md §9). Archetypes influence
    /// probabilities; they are never hard restrictions. An industrial settlement is
    /// *more likely* to supply components, not forbidden from demanding them.
    /// </summary>
    public enum IntercolonyArchetype
    {
        Agricultural,
        Industrial,
        Military,
        Affluent,
        Frontier,
        Tribal,
        TradeHub,
        Mixed
    }

    /// <summary>Rough purchasing power (DESIGN.md §9 wealthTier).</summary>
    public enum IntercolonyWealthTier
    {
        Destitute,
        Modest,
        Comfortable,
        Wealthy
    }

    /// <summary>
    /// A settlement's stable economic identity (DESIGN.md §9).
    ///
    /// Deliberately NOT <see cref="Verse.IExposable"/>. Profiles are regenerated
    /// deterministically from the world's economy seed plus the settlement's stable ID,
    /// which §96 explicitly permits ("persistence or deterministic regeneration"). That
    /// choice buys several acceptance criteria outright:
    /// destroyed settlements need no orphan cleanup, modded factions put nothing in the
    /// save file, save/load is stable because the same seed yields the same profile, and
    /// the profile shape can change without a schema migration.
    ///
    /// Anything that genuinely accumulates over time — reputation, demand saturation,
    /// order history — must live in persisted state instead, not here.
    /// </summary>
    public class SettlementEconomicProfile
    {
        /// <summary>
        /// How far one good's standing demand may sit from its category's, either way.
        ///
        /// This replaced a rolling per-cycle roll of 0.55–1.45 seeded on the refresh count, which
        /// made a settlement's appetite for a specific good drift every market cycle for no reason
        /// the player could ever learn. The 1.0 program moves that kind of movement into explicit
        /// market pressure (Stage 2); what stays here is *identity* — this settlement simply likes
        /// steel a little more than components, and will still like it more next quadrum.
        ///
        /// **The band has to straddle <see cref="FindBuyerService.InterestThreshold"/> (0.9).**
        /// Category weights cluster around 1.0, so with a tighter spread every good in a wanted
        /// category clears the threshold and "No current interest" becomes dead code again — the
        /// exact flattening that threshold was introduced to stop. At 0.15 the bottom of the band
        /// reaches 0.85, so a settlement can be keen on a category and still uninterested in a
        /// particular good in it.
        /// </summary>
        public const float ExactGoodAffinitySpread = 0.15f;

        /// <summary>
        /// Stable <c>WorldObject.ID</c> of the settlement this describes. A value of <c>-1</c>
        /// marks a synthetic profile for a generic estimate; current market pressure is looked up
        /// by this ID, so defaulting to zero would silently borrow a real settlement's conditions.
        /// </summary>
        public int settlementId = -1;

        /// <summary>
        /// <c>Faction.loadID</c> at generation time. Cached profiles are invalidated when a
        /// settlement changes hands, since tech tier is inherited from the faction (§8).
        /// </summary>
        public int factionLoadId;

        public string settlementName = "";
        public string factionName = "";

        /// <summary>Baseline tech tier, inherited from the faction (DESIGN.md §8, §50).</summary>
        public TechLevel techTier;

        public IntercolonyWealthTier wealthTier;
        public IntercolonyArchetype archetype;

        /// <summary>Relative appetite to buy, per category. Higher means more likely to demand.</summary>
        public float[] demandWeights = new float[IntercolonyProductCategoryUtility.Count];

        /// <summary>Relative ability to sell, per category. Higher means more likely to supply.</summary>
        public float[] supplyWeights = new float[IntercolonyProductCategoryUtility.Count];

        /// <summary>0 = indifferent to quality, 1 = strongly prefers high quality (§9 qualityPreference).</summary>
        public float qualityPreference;

        /// <summary>Placeholder multiplier on available workers. Labor is Phase 8+ (§9, §96).</summary>
        public float laborSupplyModifier = 1f;

        /// <summary>How much prices and opportunities swing between refreshes (§9 volatility).</summary>
        public float volatility;

        /// <summary>The seed this profile was derived from. Printed for reproducible debugging (§60).</summary>
        public int seed;

        /// <summary>
        /// What this settlement normally wants from a category. Stable identity: no market
        /// condition, event or refresh count reaches it. Stage 2 layers current pressure on top
        /// through the effective-economy API rather than by changing this.
        /// </summary>
        public float BaseDemandFor(IntercolonyProductCategory category)
        {
            return demandWeights[(int)category];
        }

        /// <summary>
        /// What this settlement normally wants of one specific good: its category appetite tilted
        /// by a standing preference for that good.
        ///
        /// Deliberately independent of the refresh count. A caller asking this question is asking
        /// what the settlement *is*, and gets the same answer every cycle until Stage 2's pressure
        /// is applied over it.
        /// </summary>
        public float BaseDemandFor(ThingDef def, IntercolonyProductCategory category)
        {
            float categoryDemand = BaseDemandFor(category);
            if (def == null)
            {
                return categoryDemand;
            }

            return Mathf.Max(0.02f, categoryDemand * ExactGoodAffinityFor(def));
        }

        /// <summary>
        /// This settlement's standing preference for one good, around 1.0 and bounded by
        /// <see cref="ExactGoodAffinitySpread"/>.
        ///
        /// Deterministic in the profile seed and the def alone. Two settlements disagree about
        /// steel; one settlement does not disagree with itself from quadrum to quadrum.
        /// </summary>
        public float ExactGoodAffinityFor(ThingDef def)
        {
            if (def == null)
            {
                return 1f;
            }

            // Salted apart from every other roll drawn off this seed, so affinity cannot line up
            // with the profile's own generation rolls or with opportunity selection.
            Rand.PushState(Gen.HashCombineInt(seed, def.shortHash, 0x4146_4659, 0));
            try
            {
                return 1f + Rand.Range(-ExactGoodAffinitySpread, ExactGoodAffinitySpread);
            }
            finally
            {
                Rand.PopState();
            }
        }

        /// <summary>
        /// What this settlement can normally supply from a category. Stable identity, same as
        /// <see cref="BaseDemandFor(IntercolonyProductCategory)"/>.
        /// </summary>
        public float BaseSupplyFor(IntercolonyProductCategory category)
        {
            return supplyWeights[(int)category];
        }

        /// <summary>The category this settlement is most inclined to sell. Its specialization, loosely.</summary>
        public IntercolonyProductCategory StrongestSupply
        {
            get
            {
                IntercolonyProductCategory best = IntercolonyProductCategory.Commodities;
                float bestWeight = float.MinValue;
                foreach (IntercolonyProductCategory category in IntercolonyProductCategoryUtility.All)
                {
                    float weight = BaseSupplyFor(category);
                    if (weight > bestWeight)
                    {
                        bestWeight = weight;
                        best = category;
                    }
                }

                return best;
            }
        }

        public string DebugSummary()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"{settlementName} ({factionName})");
            sb.AppendLine($"  id {settlementId}  seed {seed}");
            sb.AppendLine($"  {archetype} / {wealthTier} / {techTier}");
            sb.AppendLine($"  quality pref {qualityPreference:F2}  labor x{laborSupplyModifier:F2}  volatility {volatility:F2}");
            sb.AppendLine(
                $"  exact-good affinity band {1f - ExactGoodAffinitySpread:F2}-" +
                $"{1f + ExactGoodAffinitySpread:F2}, standing (does not move between refreshes)");
            sb.AppendLine("  category         demand  supply   (baseline identity, no market pressure)");
            foreach (IntercolonyProductCategory category in IntercolonyProductCategoryUtility.All)
            {
                sb.AppendLine($"    {category.Label(),-14} {BaseDemandFor(category),6:F2}  {BaseSupplyFor(category),6:F2}");
            }

            return sb.ToString();
        }

        public override string ToString()
        {
            return $"{settlementName} [{archetype}/{wealthTier}/{techTier}] sells {StrongestSupply.Label()}";
        }
    }
}
