using System;
using System.Collections.Generic;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Declares items Intercolony should not generate demand for (DESIGN.md §64: "Provide
    /// debug/settings tooling to blacklist problematic items").
    ///
    /// A def rather than a hard-coded list so other mods — and the player — can add their own
    /// exclusions by dropping in XML, without touching Intercolony's assembly. Multiple defs
    /// are additive; there is no need to override this one to extend it.
    /// </summary>
    public class IntercolonyTradeBlacklistDef : Def
    {
        /// <summary>Specific defs to exclude. Use sparingly; prefer a rule below.</summary>
        public List<ThingDef> thingDefs;

        /// <summary>
        /// Exclude anything carrying one of these comps. Rules survive mod changes far better
        /// than defName lists: excluding <c>CompHatcher</c> catches every modded fertilized
        /// egg too, without Intercolony knowing the mod exists.
        /// </summary>
        public List<Type> excludeWithComps;

        /// <summary>Exclude anything under one of these categories, including subcategories.</summary>
        public List<ThingCategoryDef> excludeCategories;

        /// <summary>Shown in debug output so an exclusion is never mysterious.</summary>
        public string reason = "blacklisted";
    }
}
