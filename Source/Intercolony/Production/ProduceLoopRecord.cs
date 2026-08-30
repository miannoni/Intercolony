using Verse;

namespace Intercolony
{
    public class ProduceLoopRecord : IExposable
    {
        public IntVec3 cell;
        public Rot4 rotation;
        public ThingDef thingDef;
        public ThingDef stuffDef;
        public ThingStyleDef styleDef;

        public void ExposeData()
        {
            Scribe_Values.Look(ref cell, "cell");
            Scribe_Values.Look(ref rotation, "rotation");
            Scribe_Defs.Look(ref thingDef, "thingDef");
            Scribe_Defs.Look(ref stuffDef, "stuffDef");
            Scribe_Defs.Look(ref styleDef, "styleDef");
        }
    }
}
