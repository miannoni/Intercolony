using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Adds the production-loop toggle to eligible buildings and construction things.
    /// </summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.GetGizmos))]
    public static class Thing_GetGizmos_ProduceLoop_Patch
    {
        // Stable Intercolony key so Produce toggles group across different things.
        private const int ProduceGroupKey = 104729;
        private const int PauseGroupKey = 104730;

        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Thing __instance)
        {
            foreach (Gizmo gizmo in values)
            {
                yield return gizmo;
            }

            IEnumerable<Gizmo> produceGizmos = null;
            try
            {
                produceGizmos = CreateProduceGizmos(__instance);
            }
            catch (System.Exception ex)
            {
                // A gizmo failure must not break the inspect pane for the whole map.
                IntercolonyLog.Error("Failed to build produce gizmo: " + ex);
            }

            if (produceGizmos == null)
            {
                yield break;
            }

            foreach (Gizmo gizmo in produceGizmos)
            {
                yield return gizmo;
            }
        }

        private static IEnumerable<Gizmo> CreateProduceGizmos(Thing thing)
        {
            Rot4 rotation;
            ThingDef thingDef;
            ThingDef stuffDef;
            ThingStyleDef styleDef;
            if (!ProduceSubjectUtility.TryGetProduceSubject(
                    thing,
                    out rotation,
                    out thingDef,
                    out stuffDef,
                    out styleDef))
            {
                return null;
            }

            Map map = thing.Map;
            IntVec3 cell = thing.Position;

            ProduceLoopMapComponent loopComponent = ProduceLoopMapComponent.For(map);
            if (loopComponent == null)
            {
                return null;
            }

            List<Gizmo> gizmos = new List<Gizmo>
            {
                new Command_Toggle
                {
                    defaultLabel = "Produce",
                    defaultDesc = "While on, this object is uninstalled and an identical one is queued in the same place using the same material; the cycle repeats after each replacement. Turning it off ends the production program. Pause temporarily stops new cycles while work already under way finishes.",
                    icon = ContentFinder<Texture2D>.Get("UI/Designators/Uninstall"),
                    groupKeyIgnoreContent = ProduceGroupKey,
                    activateIfAmbiguous = true,
                    isActive = () => loopComponent.IsEnabled(cell),
                    toggleAction = () =>
                    {
                        if (loopComponent.IsEnabled(cell))
                        {
                            loopComponent.Disable(cell);
                        }
                        else
                        {
                            loopComponent.Enable(cell, rotation, thingDef, stuffDef, styleDef);
                        }
                    }
                }
            };

            if (loopComponent.Find(cell) != null)
            {
                gizmos.Add(new Command_Toggle
                {
                    defaultLabel = "Pause production",
                    defaultDesc = "Pauses this production program: work already under way finishes, this object stays installed, and the program continues when resumed.",
                    icon = ContentFinder<Texture2D>.Get("UI/Designators/Uninstall"),
                    groupKeyIgnoreContent = PauseGroupKey,
                    isActive = () =>
                    {
                        ProduceLoopRecord loop = loopComponent.Find(cell);
                        return loop != null && loop.paused;
                    },
                    toggleAction = () =>
                    {
                        ProduceLoopRecord loop = loopComponent.Find(cell);
                        if (loop != null && loop.paused)
                        {
                            loopComponent.Resume(cell);
                        }
                        else
                        {
                            loopComponent.Pause(cell);
                        }
                    }
                });
            }

            return gizmos;
        }
    }

    internal static class ProduceSubjectUtility
    {
        internal static bool TryGetProduceSubject(
            Thing thing,
            out Rot4 rotation,
            out ThingDef thingDef,
            out ThingDef stuffDef,
            out ThingStyleDef styleDef)
        {
            rotation = default(Rot4);
            thingDef = null;
            stuffDef = null;
            styleDef = null;

            if (thing == null || !thing.Spawned || thing.Map == null || thing.Faction != Faction.OfPlayer)
            {
                return false;
            }

            rotation = thing.Rotation;

            // Frame derives from Building, so this case must be checked first.
            if (thing is Frame frame)
            {
                thingDef = frame.def.entityDefToBuild as ThingDef;
                if (thingDef == null || !thingDef.Minifiable)
                {
                    return false;
                }

                stuffDef = frame.Stuff;
                styleDef = frame.StyleDef;
                return true;
            }

            if (thing is Blueprint blueprint)
            {
                // An install blueprint restores an existing minified thing; it does not
                // represent production of a new thing at this cell.
                if (blueprint is Blueprint_Install)
                {
                    return false;
                }

                thingDef = blueprint.def.entityDefToBuild as ThingDef;
                if (thingDef == null || !thingDef.Minifiable)
                {
                    return false;
                }

                Blueprint_Build blueprintBuild = blueprint as Blueprint_Build;
                if (blueprintBuild == null)
                {
                    return false;
                }

                stuffDef = blueprintBuild.stuffToUse;
                styleDef = blueprintBuild.StyleDef;
                return true;
            }

            if (thing is Building building)
            {
                thingDef = building.def;
                if (!thingDef.Minifiable)
                {
                    return false;
                }

                stuffDef = building.Stuff;
                styleDef = building.StyleDef;
                return true;
            }

            return false;
        }
    }
}
