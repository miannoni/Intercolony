using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Maps a <see cref="ThingDef"/> onto one of the §10 product categories.
    ///
    /// Driven entirely by def properties and category ancestry — never a hard-coded list of
    /// vanilla defNames (DESIGN.md §63 "prefer definition-driven behavior", §64 "if an item
    /// behaves like a normal tradable physical Thing, Intercolony should attempt to support
    /// it"). A modded steel-equivalent lands in IntermediateGoods without Intercolony knowing
    /// the mod exists.
    /// </summary>
    public static class IntercolonyProductClassifier
    {
        /// <summary>
        /// Classification is pure and defs never change at runtime, so results are cached.
        /// Rebuilt on demand; there is no def reload during a session.
        /// </summary>
        private static readonly Dictionary<ThingDef, IntercolonyProductCategory?> cache =
            new Dictionary<ThingDef, IntercolonyProductCategory?>();

        private static List<ThingDef> tradableCache;

        /// <summary>
        /// Minimum unit value worth generating demand for. Below this, quantities become
        /// absurd (a settlement asking for 40,000 units of something worth 0.1 silver).
        /// </summary>
        private const float MinMarketValue = 0.4f;

        /// <summary>
        /// The category this def belongs to, or null if Intercolony should not trade it.
        /// </summary>
        public static IntercolonyProductCategory? Classify(ThingDef def)
        {
            if (def == null)
            {
                return null;
            }

            if (cache.TryGetValue(def, out IntercolonyProductCategory? cached))
            {
                return cached;
            }

            IntercolonyProductCategory? result = ClassifyUncached(def);
            cache[def] = result;
            return result;
        }

        private static IntercolonyProductCategory? ClassifyUncached(ThingDef def)
        {
            if (!IsTradableGood(def))
            {
                return null;
            }

            // Order matters: the first matching rule wins, so the most specific tests come
            // first. Art is checked before furniture because sculptures are also buildings.
            if (HasCategory(def, ThingCategoryDefOf.BuildingsArt))
            {
                return IntercolonyProductCategory.ArtAndUnique;
            }

            if (def.IsWeapon || def.IsApparel || def.IsMedicine || def.IsDrug)
            {
                return IntercolonyProductCategory.ManufacturedGoods;
            }

            // Raw inputs straight off the land.
            if (HasCategory(def, ThingCategoryDefOf.Foods) ||
                HasCategory(def, ThingCategoryDefOf.PlantFoodRaw) ||
                HasCategory(def, ThingCategoryDefOf.PlantMatter) ||
                HasCategory(def, ThingCategoryDefOf.MeatRaw) ||
                HasCategory(def, ThingCategoryDefOf.Fish) ||
                HasCategory(def, ThingCategoryDefOf.Leathers) ||
                HasCategory(def, ThingCategoryDefOf.Wools) ||
                HasCategory(def, ThingCategoryDefOf.StoneChunks) ||
                HasCategory(def, ThingCategoryDefOf.Chunks))
            {
                return IntercolonyProductCategory.Commodities;
            }

            // Processed inputs: steel, cloth, chemfuel, components, stone blocks.
            if (HasCategory(def, ThingCategoryDefOf.Manufactured) ||
                HasCategory(def, ThingCategoryDefOf.Textiles) ||
                HasCategory(def, ThingCategoryDefOf.StoneBlocks) ||
                HasCategory(def, ThingCategoryDefOf.ResourcesRaw))
            {
                return IntercolonyProductCategory.IntermediateGoods;
            }

            if (def.category == ThingCategory.Building)
            {
                // A bench or turret is capital equipment; a chair is furniture. Work benches
                // are distinguished by having work-giving properties rather than by name.
                bool isProductive = def.hasInteractionCell || def.building?.isMealSource == true;
                return isProductive
                    ? IntercolonyProductCategory.CapitalEquipment
                    : IntercolonyProductCategory.Furniture;
            }

            // A tradable item that matched no rule. Intermediate is the least surprising
            // bucket for an unknown modded resource, and §64 prefers inclusion over exclusion.
            return IntercolonyProductCategory.IntermediateGoods;
        }

        /// <summary>
        /// Whether Intercolony will generate demand for this def at all.
        ///
        /// Phase 4 deliberately restricts itself to stackable items — genuinely fungible lots
        /// (§23.1). Furniture, art, and capital equipment need the unique-item snapshot path
        /// (§23.2, §24) which does not exist yet, so they classify but are not traded.
        /// </summary>
        public static bool IsTradableGood(ThingDef def)
        {
            // Player- and mod-declared exclusions win over everything else (§64).
            if (IntercolonyTradeBlacklist.IsBlacklisted(def))
            {
                return false;
            }

            return PassesIntrinsicRules(def);
        }

        /// <summary>
        /// The intrinsic tradability rules, ignoring the blacklist. Separated so the blacklist
        /// can report which of its exclusions actually changed an outcome without recursing.
        /// </summary>
        private static bool PassesIntrinsicRules(ThingDef def)
        {
            if (def == null || def.BaseMarketValue < MinMarketValue)
            {
                return false;
            }

            // Never trade the payment currency. Every Intercolony transaction settles in
            // silver, so a silver-for-silver sale is a direct money printer: buy low, get paid
            // more of the same commodity, repeat. That is exactly the "guaranteed arbitrage"
            // §76.6 warns about, and there is no legitimate version of it without a lending
            // system that Intercolony deliberately does not have.
            //
            // This is a structural invariant, not a taste call, so it is enforced here rather
            // than in the §64 blacklist — a blacklist entry can be removed by another mod's
            // XML, and removing this one would re-open the exploit.
            if (def == ThingDefOf.Silver)
            {
                return false;
            }

            if (!def.tradeability.PlayerCanSell())
            {
                return false;
            }

            // Corpses and living things are not commodities here.
            if (def.IsCorpse || def.category == ThingCategory.Pawn)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Whether this def would be a trade candidate if it were not blacklisted. Used only
        /// by blacklist debug output, so an exclusion list is not padded with defs that were
        /// never eligible anyway.
        /// </summary>
        public static bool WouldTradeIgnoringBlacklist(ThingDef def)
        {
            return PassesIntrinsicRules(def) &&
                   (def.category == ThingCategory.Item ||
                    (def.category == ThingCategory.Building && def.Minifiable));
        }

        /// <summary>Drops cached classifications, e.g. after a blacklist change.</summary>
        public static void Invalidate()
        {
            cache.Clear();
            tradableCache = null;
        }

        /// <summary>
        /// Defs demand can be generated for.
        ///
        /// Items qualify outright. Buildings qualify **only when minifiable**, per the Phase 7
        /// spike (`docs/unique-goods-spike.md`): a non-minifiable building cannot be crated,
        /// so a caravan physically cannot carry it. That exclusion is permanent, not a
        /// temporary gap — no future phase can deliver a wall.
        /// </summary>
        public static bool IsFungibleTradeItem(ThingDef def)
        {
            if (!IsTradableGood(def))
            {
                return false;
            }

            if (def.category == ThingCategory.Item)
            {
                return true;
            }

            return def.category == ThingCategory.Building && def.Minifiable;
        }

        /// <summary>
        /// Whether wear is a meaningful term for generated demand. Many bulk goods technically
        /// have hit points, but asking for "healthy" meat or steel would only add noise.
        /// </summary>
        public static bool CanHaveConditionFloor(ThingDef def)
        {
            if (def == null || !def.useHitPoints || !IsFungibleTradeItem(def))
            {
                return false;
            }

            if (def.IsWeapon || def.IsApparel)
            {
                return true;
            }

            IntercolonyProductCategory? category = Classify(def);
            return category == IntercolonyProductCategory.Furniture ||
                   category == IntercolonyProductCategory.CapitalEquipment ||
                   category == IntercolonyProductCategory.ArtAndUnique;
        }

        /// <summary>
        /// All defs eligible for opportunity generation, computed once per session.
        /// Enumerating DefDatabase is not something to do per refresh (§84).
        /// </summary>
        public static List<ThingDef> TradableDefs
        {
            get
            {
                if (tradableCache != null)
                {
                    return tradableCache;
                }

                tradableCache = new List<ThingDef>();
                foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
                {
                    if (IsFungibleTradeItem(def) && Classify(def).HasValue)
                    {
                        tradableCache.Add(def);
                    }
                }

                IntercolonyLog.Verbose($"Classified {tradableCache.Count} tradable fungible defs.");
                return tradableCache;
            }
        }

        /// <summary>Tradable defs in one category. Allocates; call on refresh, not per frame.</summary>
        public static List<ThingDef> DefsInCategory(IntercolonyProductCategory category)
        {
            List<ThingDef> result = new List<ThingDef>();
            foreach (ThingDef def in TradableDefs)
            {
                if (Classify(def) == category)
                {
                    result.Add(def);
                }
            }

            return result;
        }

        private static bool HasCategory(ThingDef def, ThingCategoryDef category)
        {
            if (def.thingCategories == null || category == null)
            {
                return false;
            }

            foreach (ThingCategoryDef own in def.thingCategories)
            {
                // Walk parents so a subcategory (e.g. MeatRaw under Foods) still matches.
                for (ThingCategoryDef c = own; c != null; c = c.parent)
                {
                    if (c == category)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Debug histogram of how the current def database classified (§67).</summary>
        public static string DebugHistogram()
        {
            Dictionary<IntercolonyProductCategory, int> counts =
                new Dictionary<IntercolonyProductCategory, int>();
            foreach (ThingDef def in TradableDefs)
            {
                IntercolonyProductCategory? c = Classify(def);
                if (c.HasValue)
                {
                    counts.TryGetValue(c.Value, out int n);
                    counts[c.Value] = n + 1;
                }
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine($"Tradable fungible defs: {TradableDefs.Count}");
            foreach (IntercolonyProductCategory category in IntercolonyProductCategoryUtility.All)
            {
                counts.TryGetValue(category, out int n);
                sb.AppendLine($"  {category.Label(),-14} {n}");
                if (n == 0)
                {
                    continue;
                }

                // A few examples, so a miscategorisation is obvious at a glance.
                int shown = 0;
                foreach (ThingDef def in TradableDefs)
                {
                    if (Classify(def) == category && shown < 6)
                    {
                        sb.AppendLine($"      {def.defName} ({def.BaseMarketValue:F1})");
                        shown++;
                    }
                }
            }

            return sb.ToString();
        }
    }
}
