using System.Collections.Generic;
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

        public int resumeBelow = -1;
        public bool waitingForResume;
        public List<ThingDef> allowedStuff = new List<ThingDef>();
        public bool restrictToSelectedWorkers;
        public List<Pawn> allowedWorkers = new List<Pawn>();
        public int minConstructionSkill;

        // -1 means "never set", not a quantity; 0 is a valid restart threshold.
        // For old records, it derives the current restart threshold of one below target.
        // Callers must display EffectiveResumeBelow; resumeBelow is a sentinel and must never be formatted for display.
        public int EffectiveResumeBelow => resumeBelow >= 0 ? resumeBelow : System.Math.Max(0, targetCount - 1);

        public void ExposeData()
        {
            Scribe_Values.Look(ref cell, "cell");
            Scribe_Values.Look(ref rotation, "rotation");
            Scribe_Defs.Look(ref thingDef, "thingDef");
            Scribe_Defs.Look(ref stuffDef, "stuffDef");
            Scribe_Defs.Look(ref styleDef, "styleDef");
            Scribe_Values.Look(ref paused, "paused", false);
            Scribe_Values.Look(ref targetCount, "targetCount", 0);
            Scribe_Values.Look(ref resumeBelow, "resumeBelow", -1);
            Scribe_Values.Look(ref waitingForResume, "waitingForResume", false);
            Scribe_Collections.Look(ref allowedStuff, "allowedStuff", LookMode.Def);
            Scribe_Values.Look(ref restrictToSelectedWorkers, "restrictToSelectedWorkers", false);
            Scribe_Collections.Look(ref allowedWorkers, "allowedWorkers", LookMode.Reference);
            Scribe_Values.Look(ref minConstructionSkill, "minConstructionSkill", 0);

            if (allowedStuff == null)
            {
                allowedStuff = new List<ThingDef>();
            }

            if (allowedWorkers == null)
            {
                allowedWorkers = new List<Pawn>();
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                allowedStuff.RemoveAll(stuff => stuff == null);
                if (allowedStuff.Count == 0)
                {
                    allowedStuff = stuffDef != null ? new List<ThingDef> { stuffDef } : new List<ThingDef>();
                }

                allowedWorkers.RemoveAll(pawn => pawn == null);
            }
        }
    }
}
