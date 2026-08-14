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
        /// Number of per-good demand rolls averaged together. This is a tuning knob: a short
        /// window damps cycle-to-cycle variance without making local demand feel static.
        /// </summary>
        private const int DemandSmoothingWindowCycles = 3;

        /// <summary>Stable <c>WorldObject.ID</c> of the settlement this describes.</summary>
        public int settlementId;

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

        public float DemandFor(IntercolonyProductCategory category)
        {
            return demandWeights[(int)category];
        }

        /// <summary>
        /// Appetite for one good in the current market cycle, layered over the settlement's
        /// category identity. The broad weight still leads; a smoothed, bounded modifier
        /// represents local shortages and preferences that drift between refreshes.
        /// </summary>
        public float DemandFor(ThingDef def, IntercolonyProductCategory category)
        {
            float categoryDemand = DemandFor(category);
            if (def == null)
            {
                return categoryDemand;
            }

            int currentCycle = Mathf.Max(0, IntercolonyWorldComponent.Current?.RefreshCount ?? 0);
            int firstCycle = Mathf.Max(0, currentCycle - DemandSmoothingWindowCycles + 1);
            float multiplierSum = 0f;
            int cycleCount = 0;
            for (int cycle = firstCycle; cycle <= currentCycle; cycle++)
            {
                // As with market opportunities, the refresh number participates directly in
                // the seed. Older rolls are recomputed rather than stored, preserving the save
                // schema while making every (world, settlement, good, cycle) result repeatable.
                int demandSeed = Gen.HashCombineInt(seed, def.shortHash, cycle, 0x4445_4D44);
                Rand.PushState(demandSeed);
                try
                {
                    multiplierSum += Rand.Range(0.55f, 1.45f);
                    cycleCount++;
                }
                finally
                {
                    Rand.PopState();
                }
            }

            return Mathf.Max(0.02f, categoryDemand * (multiplierSum / cycleCount));
        }

        public float SupplyFor(IntercolonyProductCategory category)
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
                    float weight = SupplyFor(category);
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
            sb.AppendLine("  category         demand  supply");
            foreach (IntercolonyProductCategory category in IntercolonyProductCategoryUtility.All)
            {
                sb.AppendLine($"    {category.Label(),-14} {DemandFor(category),6:F2}  {SupplyFor(category),6:F2}");
            }

            return sb.ToString();
        }

        public override string ToString()
        {
            return $"{settlementName} [{archetype}/{wealthTier}/{techTier}] sells {StrongestSupply.Label()}";
        }
    }
}
