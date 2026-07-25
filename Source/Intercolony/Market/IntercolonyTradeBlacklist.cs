using System;
using System.Collections.Generic;
using System.Text;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Resolves whether a def is excluded from trade, combining every
    /// <see cref="IntercolonyTradeBlacklistDef"/> in the def database with any runtime
    /// toggles made through debug tooling (DESIGN.md §64, §67).
    ///
    /// Results are cached per def; call <see cref="Invalidate"/> after changing a runtime
    /// toggle so the classifier picks the change up.
    /// </summary>
    public static class IntercolonyTradeBlacklist
    {
        /// <summary>defName -> reason, for exclusions toggled at runtime rather than in XML.</summary>
        private static readonly Dictionary<string, string> runtimeExclusions =
            new Dictionary<string, string>();

        private static Dictionary<ThingDef, string> cache;

        /// <summary>The reason this def is excluded, or null if it is tradable.</summary>
        public static string ExclusionReason(ThingDef def)
        {
            if (def == null)
            {
                return "null def";
            }

            if (cache == null)
            {
                Rebuild();
            }

            return cache.TryGetValue(def, out string reason) ? reason : null;
        }

        public static bool IsBlacklisted(ThingDef def)
        {
            return ExclusionReason(def) != null;
        }

        /// <summary>Drops the cache. Also clears the classifier's, since it depends on this.</summary>
        public static void Invalidate()
        {
            cache = null;
            IntercolonyProductClassifier.Invalidate();
        }

        /// <summary>Excludes a def for this session. Debug tooling; not persisted.</summary>
        public static void AddRuntimeExclusion(ThingDef def, string reason = "excluded at runtime")
        {
            if (def == null)
            {
                return;
            }

            runtimeExclusions[def.defName] = reason;
            Invalidate();
            IntercolonyLog.Message($"Blacklisted {def.defName} ({reason}).");
        }

        public static void RemoveRuntimeExclusion(ThingDef def)
        {
            if (def != null && runtimeExclusions.Remove(def.defName))
            {
                Invalidate();
                IntercolonyLog.Message($"Un-blacklisted {def.defName}.");
            }
        }

        private static void Rebuild()
        {
            cache = new Dictionary<ThingDef, string>();

            List<IntercolonyTradeBlacklistDef> rules =
                DefDatabase<IntercolonyTradeBlacklistDef>.AllDefsListForReading;

            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                string reason = ResolveReason(def, rules);
                if (reason != null)
                {
                    cache[def] = reason;
                }
            }

            IntercolonyLog.Verbose(
                $"Trade blacklist rebuilt: {rules.Count} rule def(s), {cache.Count} def(s) excluded.");
        }

        private static string ResolveReason(ThingDef def, List<IntercolonyTradeBlacklistDef> rules)
        {
            if (runtimeExclusions.TryGetValue(def.defName, out string runtime))
            {
                return runtime;
            }

            foreach (IntercolonyTradeBlacklistDef rule in rules)
            {
                if (rule.thingDefs != null && rule.thingDefs.Contains(def))
                {
                    return rule.reason;
                }

                if (rule.excludeWithComps != null)
                {
                    foreach (Type comp in rule.excludeWithComps)
                    {
                        if (comp != null && def.HasComp(comp))
                        {
                            return rule.reason;
                        }
                    }
                }

                if (rule.excludeCategories != null && def.thingCategories != null)
                {
                    foreach (ThingCategoryDef excluded in rule.excludeCategories)
                    {
                        foreach (ThingCategoryDef own in def.thingCategories)
                        {
                            for (ThingCategoryDef c = own; c != null; c = c.parent)
                            {
                                if (c == excluded)
                                {
                                    return rule.reason;
                                }
                            }
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>Debug listing of what is excluded and why (DESIGN.md §67).</summary>
        public static string DebugSummary()
        {
            if (cache == null)
            {
                Rebuild();
            }

            StringBuilder sb = new StringBuilder();
            List<IntercolonyTradeBlacklistDef> rules =
                DefDatabase<IntercolonyTradeBlacklistDef>.AllDefsListForReading;

            sb.AppendLine($"Trade blacklist: {rules.Count} rule def(s), {cache.Count} def(s) excluded");
            foreach (IntercolonyTradeBlacklistDef rule in rules)
            {
                sb.AppendLine($"  rule {rule.defName}: {rule.reason}");
                if (rule.excludeWithComps != null)
                {
                    foreach (Type comp in rule.excludeWithComps)
                    {
                        sb.AppendLine($"    comp: {comp?.Name ?? "<unresolved>"}");
                    }
                }

                if (rule.thingDefs != null)
                {
                    foreach (ThingDef def in rule.thingDefs)
                    {
                        sb.AppendLine($"    def: {def?.defName ?? "<unresolved>"}");
                    }
                }

                if (rule.excludeCategories != null)
                {
                    foreach (ThingCategoryDef category in rule.excludeCategories)
                    {
                        sb.AppendLine($"    category: {category?.defName ?? "<unresolved>"}");
                    }
                }
            }

            // Only list excluded defs that would otherwise have been traded, so the output
            // is not swamped by the thousands of defs that were never candidates anyway.
            sb.AppendLine("  excluded defs that would otherwise be traded:");
            int shown = 0;
            foreach (KeyValuePair<ThingDef, string> entry in cache)
            {
                if (IntercolonyProductClassifier.WouldTradeIgnoringBlacklist(entry.Key))
                {
                    sb.AppendLine($"    {entry.Key.defName} — {entry.Value}");
                    shown++;
                }
            }

            if (shown == 0)
            {
                sb.AppendLine("    (none)");
            }

            return sb.ToString();
        }
    }
}
