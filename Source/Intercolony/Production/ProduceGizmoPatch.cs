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

        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Thing __instance)
        {
            foreach (Gizmo gizmo in values)
            {
                yield return gizmo;
            }

            Command_Toggle produceGizmo = null;
            try
            {
                produceGizmo = CreateProduceGizmo(__instance);
            }
            catch (System.Exception ex)
            {
                // A gizmo failure must not break the inspect pane for the whole map.
                IntercolonyLog.Error("Failed to build produce gizmo: " + ex);
            }

            if (produceGizmo != null)
            {
                yield return produceGizmo;
            }
        }

        private static Command_Toggle CreateProduceGizmo(Thing thing)
        {
            if (thing == null || !thing.Spawned || thing.Map == null || thing.Faction != Faction.OfPlayer)
            {
                return null;
            }

            Map map = thing.Map;
            IntVec3 cell = thing.Position;
            Rot4 rotation = thing.Rotation;
            ThingDef thingDef;
            ThingDef stuffDef;
            ThingStyleDef styleDef;

            // Frame derives from Building, so this case must be checked first.
            if (thing is Frame frame)
            {
                thingDef = frame.def.entityDefToBuild as ThingDef;
                if (thingDef == null || !thingDef.Minifiable)
                {
                    return null;
                }

                stuffDef = frame.Stuff;
                styleDef = frame.StyleDef;
            }
            else if (thing is Blueprint blueprint)
            {
                // An install blueprint restores an existing minified thing; it does not
                // represent production of a new thing at this cell.
                if (blueprint is Blueprint_Install)
                {
                    return null;
                }

                thingDef = blueprint.def.entityDefToBuild as ThingDef;
                if (thingDef == null || !thingDef.Minifiable)
                {
                    return null;
                }

                Blueprint_Build blueprintBuild = blueprint as Blueprint_Build;
                if (blueprintBuild == null)
                {
                    return null;
                }

                stuffDef = blueprintBuild.stuffToUse;
                styleDef = blueprintBuild.StyleDef;
            }
            else if (thing is Building building)
            {
                thingDef = building.def;
                if (!thingDef.Minifiable)
                {
                    return null;
                }

                stuffDef = building.Stuff;
                styleDef = building.StyleDef;
            }
            else
            {
                return null;
            }

            ProduceLoopMapComponent loopComponent = ProduceLoopMapComponent.For(map);
            if (loopComponent == null)
            {
                return null;
            }

            return new Command_Toggle
            {
                defaultLabel = "Produce",
                defaultDesc = "While on, this object is uninstalled and an identical one is queued in the same place using the same material; the cycle repeats after each replacement. Turning it off stops the next repetition, while work already under way is allowed to finish.",
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
            };
        }
    }
}
