using System;

namespace Intercolony
{
    /// <summary>
    /// The coarse product taxonomy from DESIGN.md §10. Demand and supply weights are
    /// expressed per category, so this is deliberately the same six buckets the design
    /// already names rather than a new invented set.
    ///
    /// These are weighting buckets, not a def whitelist. Mapping concrete (possibly modded)
    /// ThingDefs into categories is a separate problem for the market phase (§64).
    /// </summary>
    public enum IntercolonyProductCategory
    {
        /// <summary>§10.1 — raw fungible goods: food, textiles, wood, stone, ores.</summary>
        Commodities,

        /// <summary>§10.2 — processed inputs: steel, cloth bolts, chemfuel, components.</summary>
        IntermediateGoods,

        /// <summary>§10.3 — finished products: apparel, weapons, medicine, drugs.</summary>
        ManufacturedGoods,

        /// <summary>§10.4 — furniture and fixtures.</summary>
        Furniture,

        /// <summary>§10.5 — production benches, turrets, heavy equipment.</summary>
        CapitalEquipment,

        /// <summary>§10.6 — art and unique one-off items.</summary>
        ArtAndUnique
    }

    public static class IntercolonyProductCategoryUtility
    {
        /// <summary>
        /// All categories, cached once. Enum.GetValues allocates, so never call it in a
        /// per-frame or per-tick path (DESIGN.md §84).
        /// </summary>
        public static readonly IntercolonyProductCategory[] All =
            (IntercolonyProductCategory[])Enum.GetValues(typeof(IntercolonyProductCategory));

        public static readonly int Count = All.Length;

        /// <summary>Short label for debug and UI display.</summary>
        public static string Label(this IntercolonyProductCategory category)
        {
            switch (category)
            {
                case IntercolonyProductCategory.Commodities: return "commodities";
                case IntercolonyProductCategory.IntermediateGoods: return "intermediate";
                case IntercolonyProductCategory.ManufacturedGoods: return "manufactured";
                case IntercolonyProductCategory.Furniture: return "furniture";
                case IntercolonyProductCategory.CapitalEquipment: return "capital equip";
                case IntercolonyProductCategory.ArtAndUnique: return "art/unique";
                default: return category.ToString();
            }
        }
    }
}
