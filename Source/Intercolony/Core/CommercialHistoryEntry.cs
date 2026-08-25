using RimWorld;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Compact retained proof that the colony traded one exact good with one settlement.
    /// Detailed orders remain separate; this record keeps only the cumulative figures
    /// that still matter after those details are eventually pruned.
    /// </summary>
    public class CommercialHistoryEntry : IExposable
    {
        public int settlementId = -1;
        public ThingDef thingDef;
        public int completedSaleCount;
        public int totalQuantitySupplied;

        /// <summary>
        /// Silver actually exchanged in completed sales and purchases for this settlement/item.
        /// It is retained here so pruning detailed order and timeline records cannot erase the
        /// long-term trade-value floor.
        /// </summary>
        public int totalTradeValue;

        public void ExposeData()
        {
            Scribe_Values.Look(ref settlementId, "settlementId", -1);
            Scribe_Defs.Look(ref thingDef, "thingDef");
            Scribe_Values.Look(ref completedSaleCount, "completedSaleCount", 0);
            Scribe_Values.Look(ref totalQuantitySupplied, "totalQuantitySupplied", 0);
            Scribe_Values.Look(ref totalTradeValue, "totalTradeValue", 0);
        }
    }
}
