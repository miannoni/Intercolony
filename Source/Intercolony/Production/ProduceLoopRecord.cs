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

        // Pause retains the program so work already under way can finish without starting another cycle.
        public bool paused;

        // Zero means the original indefinite program mode; a positive value is the stored-stock target.
        public int targetCount;

        public void ExposeData()
        {
            Scribe_Values.Look(ref cell, "cell");
            Scribe_Values.Look(ref rotation, "rotation");
            Scribe_Defs.Look(ref thingDef, "thingDef");
            Scribe_Defs.Look(ref stuffDef, "stuffDef");
            Scribe_Defs.Look(ref styleDef, "styleDef");
            Scribe_Values.Look(ref paused, "paused", false);
            Scribe_Values.Look(ref targetCount, "targetCount", 0);
        }
    }
}
