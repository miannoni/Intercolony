using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Adds the receiving-location toggle to player-owned storage buildings.
    /// </summary>
    [HarmonyPatch(typeof(Building_Storage), nameof(Building_Storage.GetGizmos))]
    public static class Building_Storage_GetGizmos_Receiving_Patch
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Building_Storage __instance)
        {
            foreach (Gizmo gizmo in values)
            {
                yield return gizmo;
            }

            IEnumerable<Gizmo> receivingGizmos = null;
            try
            {
                receivingGizmos = CreateReceivingGizmos(__instance);
            }
            catch (System.Exception ex)
            {
                // A gizmo failure must not break the inspect pane for the whole map.
                IntercolonyLog.Error("Failed to build receiving gizmo: " + ex);
            }

            if (receivingGizmos == null)
            {
                yield break;
            }

            foreach (Gizmo gizmo in receivingGizmos)
            {
                yield return gizmo;
            }
        }

        private static IEnumerable<Gizmo> CreateReceivingGizmos(Building_Storage building)
        {
            if (building == null || !building.Spawned || building.Map == null ||
                building.Faction != Faction.OfPlayer)
            {
                return null;
            }

            ReceivingLocationMapComponent receivingComponent =
                ReceivingLocationMapComponent.For(building.Map);
            if (receivingComponent == null)
            {
                return null;
            }

            return new List<Gizmo>
            {
                ReceivingGizmoUtility.CreateToggle(
                    () => receivingComponent.IsReceiving(building),
                    () => receivingComponent.SetReceiving(
                        building,
                        !receivingComponent.IsReceiving(building)))
            };
        }
    }

    /// <summary>
    /// Adds the receiving-location toggle to stockpile zones on player home maps.
    /// </summary>
    [HarmonyPatch(typeof(Zone_Stockpile), nameof(Zone_Stockpile.GetGizmos))]
    public static class Zone_Stockpile_GetGizmos_Receiving_Patch
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Zone_Stockpile __instance)
        {
            foreach (Gizmo gizmo in values)
            {
                yield return gizmo;
            }

            IEnumerable<Gizmo> receivingGizmos = null;
            try
            {
                receivingGizmos = CreateReceivingGizmos(__instance);
            }
            catch (System.Exception ex)
            {
                // A gizmo failure must not break the inspect pane for the whole map.
                IntercolonyLog.Error("Failed to build receiving gizmo: " + ex);
            }

            if (receivingGizmos == null)
            {
                yield break;
            }

            foreach (Gizmo gizmo in receivingGizmos)
            {
                yield return gizmo;
            }
        }

        private static IEnumerable<Gizmo> CreateReceivingGizmos(Zone_Stockpile zone)
        {
            if (zone == null || zone.zoneManager == null || zone.Map == null || !zone.Map.IsPlayerHome)
            {
                return null;
            }

            ReceivingLocationMapComponent receivingComponent =
                ReceivingLocationMapComponent.For(zone.Map);
            if (receivingComponent == null)
            {
                return null;
            }

            return new List<Gizmo>
            {
                ReceivingGizmoUtility.CreateToggle(
                    () => receivingComponent.IsReceiving(zone),
                    () => receivingComponent.SetReceiving(
                        zone,
                        !receivingComponent.IsReceiving(zone)))
            };
        }
    }

    internal static class ReceivingGizmoUtility
    {
        // Stable Intercolony key so receiving toggles group across storage buildings and zones.
        internal const int ReceivingGroupKey = 104732;

        internal static Command_Toggle CreateToggle(
            System.Func<bool> isActive,
            System.Action toggleAction)
        {
            return new Command_Toggle
            {
                defaultLabel = "Receive deliveries",
                defaultDesc = "Deliveries arriving for this colony will be put here when this storage accepts them and has room. The storage's own filters still decide what it accepts.",
                icon = ContentFinder<Texture2D>.Get("UI/Designators/Uninstall"),
                groupKeyIgnoreContent = ReceivingGroupKey,
                activateIfAmbiguous = true,
                isActive = isActive,
                toggleAction = toggleAction
            };
        }
    }
}
