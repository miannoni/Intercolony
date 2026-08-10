using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Intercolony
{
    /// <summary>One player-facing category of otherwise eligible buy-only defs.</summary>
    public sealed class BuyOnlyTradeCategoryGroup
    {
        private string itemLabelsTooltip;

        internal BuyOnlyTradeCategoryGroup(string key, string label)
        {
            Key = key;
            Label = label;
        }

        public string Key { get; }
        public string Label { get; }
        public List<ThingDef> Defs { get; } = new List<ThingDef>();

        public string ItemLabelsTooltip
        {
            get
            {
                if (itemLabelsTooltip != null)
                {
                    return itemLabelsTooltip;
                }

                List<string> labels = new List<string>();
                foreach (ThingDef def in Defs)
                {
                    labels.Add(def.LabelCap.NullOrEmpty() ? def.defName : def.LabelCap.ToString());
                }

                labels.Sort(StringComparer.OrdinalIgnoreCase);
                itemLabelsTooltip = "Affected items:\n" + string.Join("\n", labels);
                return itemLabelsTooltip;
            }
        }
    }

    /// <summary>
    /// Applies the opt-in global tradeability override after all defs have loaded. Discovery is
    /// intentionally captured once: it describes the buy-only defs produced by the completed def
    /// load, while the per-def original cache is populated only at the moment a def is changed.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class BuyOnlyTradeUnlock
    {
        // ThingDef.FirstThingCategory can legitimately be null. This stable non-def key keeps
        // such content reachable without pretending it belongs to an unrelated vanilla category.
        internal const string UncategorizedCategoryKey = "__IntercolonyUncategorized";

        private static readonly List<BuyOnlyTradeCategoryGroup> groups = DiscoverGroups();
        private static readonly Dictionary<ThingDef, Tradeability> originalTradeability =
            new Dictionary<ThingDef, Tradeability>();

        static BuyOnlyTradeUnlock()
        {
            ApplyEnabledCategories(IntercolonyMod.Settings.enabledBuyOnlyTradeCategoryKeys);
        }

        public static IReadOnlyList<BuyOnlyTradeCategoryGroup> Groups => groups;

        /// <summary>
        /// Makes enabled groups bidirectional and restores the exact value observed at first
        /// modification for groups that have been turned off.
        /// </summary>
        public static void ApplyEnabledCategories(ISet<string> enabledCategoryKeys)
        {
            bool changed = false;
            foreach (BuyOnlyTradeCategoryGroup group in groups)
            {
                bool enabled = enabledCategoryKeys != null && enabledCategoryKeys.Contains(group.Key);
                foreach (ThingDef def in group.Defs)
                {
                    if (enabled)
                    {
                        if (!originalTradeability.ContainsKey(def))
                        {
                            originalTradeability.Add(def, def.tradeability);
                        }

                        if (def.tradeability != Tradeability.All)
                        {
                            def.tradeability = Tradeability.All;
                            changed = true;
                        }
                    }
                    else if (originalTradeability.TryGetValue(def, out Tradeability original))
                    {
                        if (def.tradeability != original)
                        {
                            def.tradeability = original;
                            changed = true;
                        }

                        originalTradeability.Remove(def);
                    }
                }
            }

            if (changed)
            {
                IntercolonyProductClassifier.Invalidate();
            }
        }

        private static List<BuyOnlyTradeCategoryGroup> DiscoverGroups()
        {
            Dictionary<string, BuyOnlyTradeCategoryGroup> byKey =
                new Dictionary<string, BuyOnlyTradeCategoryGroup>(StringComparer.Ordinal);

            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.tradeability != Tradeability.Buyable ||
                    !IntercolonyProductClassifier.WouldTradeIgnoringTradeability(def))
                {
                    continue;
                }

                ThingCategoryDef category = def.FirstThingCategory;
                string key = category?.defName ?? UncategorizedCategoryKey;
                if (!byKey.TryGetValue(key, out BuyOnlyTradeCategoryGroup group))
                {
                    string label = category == null
                        ? "Uncategorized items"
                        : category.LabelCap.NullOrEmpty()
                            ? category.defName
                            : category.LabelCap.ToString();
                    group = new BuyOnlyTradeCategoryGroup(key, label);
                    byKey.Add(key, group);
                }

                group.Defs.Add(def);
            }

            List<BuyOnlyTradeCategoryGroup> result = new List<BuyOnlyTradeCategoryGroup>(byKey.Values);
            result.Sort((left, right) =>
                StringComparer.OrdinalIgnoreCase.Compare(left.Label, right.Label));
            return result;
        }
    }
}
