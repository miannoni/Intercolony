using System.Collections.Generic;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Harmony patches. Kept deliberately few: every patch is a compatibility liability
    /// (DESIGN.md §63), so one is added only where RimWorld offers no def-driven or
    /// subclassing hook.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class HarmonyPatches
    {
        static HarmonyPatches()
        {
            Harmony harmony = new Harmony("miannoni.intercolony");
            harmony.PatchAll();
            IntercolonyLog.Verbose("Harmony patches applied.");
        }
    }

    /// <summary>
    /// Adds "Deliver order #N" to the caravan float menu for a settlement.
    ///
    /// A postfix on the vanilla method, because <see cref="Settlement.GetFloatMenuOptions"/>
    /// hard-codes its list of arrival actions and there is no def or registry to extend. The
    /// patch only appends to the returned sequence — it never inspects or removes vanilla
    /// options, so other mods postfixing the same method are unaffected.
    /// </summary>
    [HarmonyPatch(typeof(Settlement), nameof(Settlement.GetFloatMenuOptions))]
    public static class Settlement_GetFloatMenuOptions_Patch
    {
        public static IEnumerable<FloatMenuOption> Postfix(
            IEnumerable<FloatMenuOption> values, Settlement __instance, Caravan caravan)
        {
            foreach (FloatMenuOption option in values)
            {
                yield return option;
            }

            // Guard the whole addition: an exception thrown while building a float menu would
            // break the player's ability to command caravans at all (§86 error recovery).
            List<FloatMenuOption> ours = new List<FloatMenuOption>();
            try
            {
                ours.AddRange(CaravanArrivalAction_DeliverOrder.GetFloatMenuOptions(caravan, __instance));
            }
            catch (System.Exception ex)
            {
                IntercolonyLog.Error("Failed to build delivery float menu options: " + ex);
                yield break;
            }

            foreach (FloatMenuOption option in ours)
            {
                yield return option;
            }
        }
    }

    /// <summary>
    /// Adds "Deliver order #N" gizmos to a caravan parked at a buyer's settlement.
    ///
    /// Needed because <see cref="CaravanArrivalAction_DeliverOrder"/> only fires on arrival:
    /// a caravan that is already on the tile has no arrival left to trigger. Same postfix
    /// discipline as above — append only, never inspect or remove vanilla gizmos.
    /// </summary>
    [HarmonyPatch(typeof(Caravan), nameof(Caravan.GetGizmos))]
    public static class Caravan_GetGizmos_Patch
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Caravan __instance)
        {
            foreach (Gizmo gizmo in values)
            {
                yield return gizmo;
            }

            List<Gizmo> ours = new List<Gizmo>();
            try
            {
                ours.AddRange(CaravanDeliveryGizmos.GetGizmos(__instance));
            }
            catch (System.Exception ex)
            {
                IntercolonyLog.Error("Failed to build delivery gizmos: " + ex);
                yield break;
            }

            foreach (Gizmo gizmo in ours)
            {
                yield return gizmo;
            }
        }
    }
}
