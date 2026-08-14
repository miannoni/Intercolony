using RimWorld;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Compact retained proof that the colony supplied one exact good to one settlement.
    /// Detailed orders remain separate; this record keeps only the two cumulative figures
    /// that still matter after those details are eventually pruned.
    /// </summary>
    public class CommercialHistoryEntry : IExposable
    {
        public int settlementId = -1;
        public ThingDef thingDef;
        public int completedSaleCount;
        public int totalQuantitySupplied;

        public void ExposeData()
        {
            Scribe_Values.Look(ref settlementId, "settlementId", -1);
            Scribe_Defs.Look(ref thingDef, "thingDef");
            Scribe_Values.Look(ref completedSaleCount, "completedSaleCount", 0);
            Scribe_Values.Look(ref totalQuantitySupplied, "totalQuantitySupplied", 0);
        }
    }
}
