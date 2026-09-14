using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Intercolony
{
    /// <summary>
    /// Enforces Produce-loop worker restrictions at the shared construction eligibility seam.
    /// Vanilla handles a blocking thing before it calls CanConstruct, so this does not prevent a
    /// non-selected pawn from clearing a plant or rubble blocking a Produce cell. Clearing a
    /// blocker is not producing.
    /// </summary>
    [HarmonyPatch(
        typeof(GenConstruct),
        nameof(GenConstruct.CanConstruct),
        new[] { typeof(Thing), typeof(Pawn), typeof(bool), typeof(bool), typeof(JobDef) })]
    public static class ProduceWorkerGatePatch
    {
        public static bool Prefix(Thing t, Pawn p, bool checkSkills, ref bool __result)
        {
            try
            {
                if (t == null || (!(t is Blueprint) && !(t is Frame)))
                {
                    return true;
                }

                Map map = t.Map;
                if (map == null)
                {
                    return true;
                }

                ProduceLoopMapComponent loopComponent = ProduceLoopMapComponent.For(map);
                if (loopComponent == null || loopComponent.Loops.Count == 0)
                {
                    return true;
                }

                ProduceLoopRecord loop = loopComponent.Find(t.Position);
                if (loop == null || loop.thingDef == null || t.def.entityDefToBuild != loop.thingDef)
                {
                    return true;
                }

                if (loop.restrictToSelectedWorkers &&
                    (loop.allowedWorkers == null || !loop.allowedWorkers.Contains(p)))
                {
                    JobFailReason.Is("Only selected workers can work this Produce program.");
                    __result = false;
                    return false;
                }

                if (checkSkills && loop.minConstructionSkill > 0 && p != null)
                {
                    bool belowMinimum = false;
                    if (p.skills != null)
                    {
                        belowMinimum =
                            p.skills.GetSkill(SkillDefOf.Construction).Level < loop.minConstructionSkill;
                    }
                    else if (p.IsColonyMech)
                    {
                        belowMinimum = p.RaceProps.mechFixedSkillLevel < loop.minConstructionSkill;
                    }

                    if (belowMinimum)
                    {
                        JobFailReason.Is(
                            "Produce program requires Construction skill " +
                            loop.minConstructionSkill + " or higher.");
                        __result = false;
                        return false;
                    }
                }

                return true;
            }
            catch (System.Exception ex)
            {
                IntercolonyLog.Error("Failed to enforce Produce worker rules: " + ex);
                return true;
            }
        }
    }
}
