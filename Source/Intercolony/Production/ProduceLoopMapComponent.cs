using System.Collections.Generic;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// MapComponent state is saved in the map's save block and is independent of the
    /// world-level IntercolonyWorldComponent schema and migration ladder.
    /// </summary>
    public class ProduceLoopMapComponent : MapComponent
    {
        private List<ProduceLoopRecord> loops = new List<ProduceLoopRecord>();

        public ProduceLoopMapComponent(Map map) : base(map)
        {
        }

        public static ProduceLoopMapComponent For(Map map)
        {
            return map?.GetComponent<ProduceLoopMapComponent>();
        }

        public bool IsEnabled(IntVec3 cell)
        {
            return Find(cell) != null;
        }

        public ProduceLoopRecord Find(IntVec3 cell)
        {
            for (int i = 0; i < loops.Count; i++)
            {
                if (loops[i].cell == cell)
                {
                    return loops[i];
                }
            }

            return null;
        }

        public void Enable(
            IntVec3 cell,
            Rot4 rotation,
            ThingDef thingDef,
            ThingDef stuffDef,
            ThingStyleDef styleDef)
        {
            loops.RemoveAll(loop => loop.cell == cell);
            loops.Add(new ProduceLoopRecord
            {
                cell = cell,
                rotation = rotation,
                thingDef = thingDef,
                stuffDef = stuffDef,
                styleDef = styleDef
            });
        }

        public void Disable(IntVec3 cell)
        {
            loops.RemoveAll(loop => loop.cell == cell);
        }

        public IReadOnlyList<ProduceLoopRecord> Loops
        {
            get { return loops; }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref loops, "loops", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && loops == null)
            {
                loops = new List<ProduceLoopRecord>();
            }
        }
    }
}
