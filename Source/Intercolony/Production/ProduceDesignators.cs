using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    public abstract class Designator_ProduceBase : Designator_Cells
    {
        protected Designator_ProduceBase(string label, string description)
        {
            defaultLabel = label;
            defaultDesc = description;
            icon = ContentFinder<Texture2D>.Get("UI/Designators/Uninstall");
            soundDragSustain = SoundDefOf.Designate_DragStandard;
            soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
            useMouseIcon = true;
        }

        public override DrawStyleCategoryDef DrawStyleCategory => DrawStyleCategoryDefOf.FilledRectangle;

        protected ProduceLoopMapComponent LoopComponentAt(IntVec3 cell)
        {
            Map map = base.Map;
            if (map == null || !cell.InBounds(map))
            {
                return null;
            }

            return ProduceLoopMapComponent.For(map);
        }

        protected bool TryGetProduceSubjectAt(
            IntVec3 cell,
            out Rot4 rotation,
            out ThingDef thingDef,
            out ThingDef stuffDef,
            out ThingStyleDef styleDef)
        {
            rotation = default(Rot4);
            thingDef = null;
            stuffDef = null;
            styleDef = null;

            Map map = base.Map;
            if (map == null || !cell.InBounds(map))
            {
                return false;
            }

            foreach (Thing thing in map.thingGrid.ThingsAt(cell).OrderByDescending(t => t.def.altitudeLayer))
            {
                if (ProduceSubjectUtility.TryGetProduceSubject(
                        thing,
                        out rotation,
                        out thingDef,
                        out stuffDef,
                        out styleDef))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public class Designator_ProduceResume : Designator_ProduceBase
    {
        public Designator_ProduceResume()
            : base(
                "Produce / resume",
                "Enables a production program at each eligible object, or resumes a paused program.")
        {
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            ProduceLoopMapComponent loopComponent = LoopComponentAt(c);
            if (loopComponent == null)
            {
                return false;
            }

            ProduceLoopRecord loop = loopComponent.Find(c);
            if (loop != null)
            {
                return loop.paused;
            }

            return TryGetProduceSubjectAt(
                c,
                out Rot4 rotation,
                out ThingDef thingDef,
                out ThingDef stuffDef,
                out ThingStyleDef styleDef);
        }

        public override void DesignateSingleCell(IntVec3 loc)
        {
            ProduceLoopMapComponent loopComponent = LoopComponentAt(loc);
            if (loopComponent == null)
            {
                return;
            }

            ProduceLoopRecord loop = loopComponent.Find(loc);
            if (loop != null)
            {
                if (loop.paused)
                {
                    loopComponent.Resume(loc);
                }

                return;
            }

            if (TryGetProduceSubjectAt(
                    loc,
                    out Rot4 rotation,
                    out ThingDef thingDef,
                    out ThingDef stuffDef,
                    out ThingStyleDef styleDef))
            {
                loopComponent.Enable(loc, rotation, thingDef, stuffDef, styleDef);
            }
        }
    }

    public class Designator_ProducePause : Designator_ProduceBase
    {
        public Designator_ProducePause()
            : base(
                "Pause production",
                "Pauses a production program: work already under way finishes, the object stays installed, and the program continues when resumed.")
        {
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            ProduceLoopMapComponent loopComponent = LoopComponentAt(c);
            if (loopComponent == null)
            {
                return false;
            }

            ProduceLoopRecord loop = loopComponent.Find(c);
            return loop != null && !loop.paused;
        }

        public override void DesignateSingleCell(IntVec3 loc)
        {
            ProduceLoopMapComponent loopComponent = LoopComponentAt(loc);
            if (loopComponent == null)
            {
                return;
            }

            ProduceLoopRecord loop = loopComponent.Find(loc);
            if (loop != null && !loop.paused)
            {
                loopComponent.Pause(loc);
            }
        }
    }

    public class Designator_ProduceStop : Designator_ProduceBase
    {
        public Designator_ProduceStop()
            : base(
                "Stop production",
                "Stops a production program. Work already under way finishes, but no further cycles begin.")
        {
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            ProduceLoopMapComponent loopComponent = LoopComponentAt(c);
            return loopComponent != null && loopComponent.Find(c) != null;
        }

        public override void DesignateSingleCell(IntVec3 loc)
        {
            ProduceLoopMapComponent loopComponent = LoopComponentAt(loc);
            if (loopComponent == null)
            {
                return;
            }

            if (loopComponent.Find(loc) != null)
            {
                loopComponent.Disable(loc);
            }
        }
    }
}
