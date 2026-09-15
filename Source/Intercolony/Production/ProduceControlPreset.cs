using System.Collections.Generic;
using Verse;

namespace Intercolony
{
    public class ProduceControlPreset : IExposable
    {
        public int id;
        public string name;
        public int targetCount;
        public int resumeBelow = -1;
        public bool restrictToSelectedWorkers;
        public List<Pawn> allowedWorkers = new List<Pawn>();
        public int minConstructionSkill;
        public List<ThingDef> allowedStuff = new List<ThingDef>();

        public int EffectiveResumeBelow => resumeBelow >= 0 ? resumeBelow : System.Math.Max(0, targetCount - 1);

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", 0);
            Scribe_Values.Look(ref name, "name");
            Scribe_Values.Look(ref targetCount, "targetCount", 0);
            Scribe_Values.Look(ref resumeBelow, "resumeBelow", -1);
            Scribe_Values.Look(ref restrictToSelectedWorkers, "restrictToSelectedWorkers", false);
            Scribe_Collections.Look(ref allowedWorkers, "allowedWorkers", LookMode.Reference);
            Scribe_Values.Look(ref minConstructionSkill, "minConstructionSkill", 0);
            Scribe_Collections.Look(ref allowedStuff, "allowedStuff", LookMode.Def);

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
                allowedWorkers.RemoveAll(pawn => pawn == null);
            }
        }
    }
}
