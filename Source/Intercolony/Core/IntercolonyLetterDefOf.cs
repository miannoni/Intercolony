using RimWorld;
using Verse;

namespace Intercolony
{
    [DefOf]
    public static class IntercolonyLetterDefOf
    {
        public static LetterDef Intercolony_EmergencyEmployeeArrival;

        static IntercolonyLetterDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(IntercolonyLetterDefOf));
        }
    }
}
