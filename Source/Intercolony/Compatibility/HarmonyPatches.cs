using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
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

    /// <summary>
    /// Lets Intercolony employees be sent on caravans (DESIGN.md §33 q9, §25.1 "caravan labor").
    ///
    /// Vanilla filters quest lodgers out of the caravan dialog:
    /// <c>AllSendablePawns</c> tests <c>(!pawn.IsQuestLodger() || allowLodgers)</c> and
    /// <see cref="Dialog_FormCaravan"/> passes <c>allowLodgers: false</c>. Employees are lodgers
    /// (that is what preserves their <c>kindDef</c> and keeps them out of raid-point maths), so
    /// without this they cannot be loaded — which defeats a large part of the point of hiring
    /// labor, since caravan work is exactly the capacity §31 says silver should buy.
    ///
    /// Rather than reimplement the vanilla predicate — it is long, and other mods may have
    /// changed it — this calls the same method again with <c>allowLodgers: true</c> behind a
    /// re-entry guard and keeps only the pawns that are Intercolony employees. Vanilla's own
    /// rules about downed, mental state, prisoners and lords therefore still apply unchanged,
    /// and no other mod's lodgers are affected.
    /// </summary>
    [HarmonyPatch(typeof(CaravanFormingUtility), nameof(CaravanFormingUtility.AllSendablePawns))]
    public static class CaravanFormingUtility_AllSendablePawns_Patch
    {
        private static bool reentering;

        public static void Postfix(
            List<Pawn> __result, Map map, bool allowEvenIfDowned, bool allowEvenIfInMentalState,
            bool allowEvenIfPrisonerNotSecure, bool allowCapturableDownedPawns, bool allowLodgers,
            int allowLoadAndEnterTransportersLordForGroupID)
        {
            // allowLodgers already includes them; reentering means this is our own inner call.
            if (allowLodgers || reentering || __result == null || map == null)
            {
                return;
            }

            // Cheap bail-out for the overwhelmingly common case of no employees on the payroll.
            if (!EmploymentService.AnyActiveEmployee())
            {
                return;
            }

            reentering = true;
            try
            {
                List<Pawn> withLodgers = CaravanFormingUtility.AllSendablePawns(
                    map, allowEvenIfDowned, allowEvenIfInMentalState, allowEvenIfPrisonerNotSecure,
                    allowCapturableDownedPawns, allowLodgers: true,
                    allowLoadAndEnterTransportersLordForGroupID);

                foreach (Pawn pawn in withLodgers)
                {
                    if (!__result.Contains(pawn) && EmploymentService.IsEmployee(pawn))
                    {
                        __result.Add(pawn);
                    }
                }
            }
            catch (System.Exception ex)
            {
                // A throw here would break caravan forming entirely (§86).
                IntercolonyLog.Error("Failed to add employees to the caravan list: " + ex);
            }
            finally
            {
                reentering = false;
            }
        }
    }

    /// <summary>
    /// Ends a produce loop when vanilla Cancel removes its current blueprint or frame.
    ///
    /// The polling pass cannot distinguish a player cancellation from a blueprint that has not
    /// been placed yet, so this narrow command patch observes the cancellation before vanilla
    /// destroys the thing.
    /// </summary>
    [HarmonyPatch(typeof(Designator_Cancel), nameof(Designator_Cancel.DesignateThing))]
    public static class Designator_Cancel_DesignateThing_Patch
    {
        public static void Prefix(Thing t)
        {
            try
            {
                if (!(t is Blueprint) && !(t is Frame) || t.Map == null)
                {
                    return;
                }

                ProduceLoopMapComponent component = ProduceLoopMapComponent.For(t.Map);
                if (component == null)
                {
                    return;
                }

                ProduceLoopRecord loop = component.Find(t.Position);
                if (loop == null || loop.thingDef == null || t.def.entityDefToBuild != loop.thingDef)
                {
                    return;
                }

                component.Disable(loop.cell);
            }
            catch (System.Exception ex)
            {
                IntercolonyLog.Error("Failed to stop produce loop when cancelling blueprint: " + ex);
            }
        }
    }

    /// <summary>
    /// Feeds actually completed products into the production ledger for F07.
    ///
    /// Nothing in Intercolony could observe an item being completed: the existing production loop
    /// only polls stock, and F07 forbids inferring production from stock (selling twenty chairs is
    /// not negative chair production). This fifth Harmony patch is the only practical hook for
    /// vanilla recipes in this mod because there is no mod-owned completion event or product
    /// component.
    ///
    /// <c>GenRecipe.MakeRecipeProducts</c> is an iterator, so a postfix there would run when its
    /// enumerator is created rather than when its body yields. <see cref="RecordsUtility.Notify_BillDone"/>
    /// is the plainly patchable equivalent: vanilla calls it once after <c>ToList()</c> has
    /// materialized the completed product batch, before storing or dropping it. The postfix only
    /// reads each product's inner definition and stack count, and never changes vanilla's call or
    /// data.
    /// </summary>
    [HarmonyPatch(typeof(RecordsUtility), nameof(RecordsUtility.Notify_BillDone))]
    public static class RecordsUtility_Notify_BillDone_Patch
    {
        public static void Postfix(Pawn billDoer, List<Thing> products)
        {
            if (billDoer == null || products == null || products.Count == 0 ||
                billDoer.Faction == null || billDoer.Faction != Faction.OfPlayer)
            {
                return;
            }

            IntercolonyWorldComponent state = IntercolonyWorldComponent.Current;
            if (state == null)
            {
                return;
            }

            try
            {
                for (int i = 0; i < products.Count; i++)
                {
                    Thing product = products[i];
                    if (product == null)
                    {
                        continue;
                    }

                    Thing inner = product.GetInnerIfMinified();
                    if (inner == null)
                    {
                        continue;
                    }

                    ProductionLedgerService.Record(state, inner.def, inner.stackCount);
                }
            }
            catch (System.Exception ex)
            {
                // A recording failure must not turn a completed vanilla bill into a failed job.
                IntercolonyLog.Error("Failed to record completed production: " + ex);
            }
        }
    }

    /// <summary>
    /// Observes the authoritative moment when a construction frame becomes its finished Thing.
    ///
    /// Vanilla's <c>CompleteConstruction(Pawn)</c> is void and keeps the finished Thing in a
    /// local variable (see <c>Frame.cs:262-370</c>), while destroying the frame before creating
    /// that local. The prefix therefore captures the frame's intended ThingDef and faction before
    /// the original method runs. Ordinary vanilla construction does not call anything in
    /// Intercolony, so this narrow observation is the completion seam available to F07.
    /// </summary>
    [HarmonyPatch(typeof(Frame), nameof(Frame.CompleteConstruction))]
    public static class Frame_CompleteConstruction_Patch
    {
        private sealed class ConstructionObservation
        {
            public ThingDef thingDef;
            public Faction faction;
        }

        private static void Prefix(Frame __instance, out ConstructionObservation __state)
        {
            __state = null;
            if (__instance == null || __instance.def == null)
            {
                return;
            }

            __state = new ConstructionObservation
            {
                thingDef = __instance.def.entityDefToBuild as ThingDef,
                faction = __instance.Faction
            };
        }

        private static void Postfix(ConstructionObservation __state)
        {
            if (__state == null || __state.thingDef == null ||
                __state.faction == null || __state.faction != Faction.OfPlayer)
            {
                return;
            }

            IntercolonyWorldComponent state = IntercolonyWorldComponent.Current;
            if (state == null)
            {
                return;
            }

            try
            {
                // One successful CompleteConstruction call creates one finished Thing; never
                // infer a stack quantity from the frame or from later spawning/minification.
                ProductionLedgerService.Record(state, __state.thingDef, 1);
            }
            catch (System.Exception ex)
            {
                // An observation failure must never break vanilla construction.
                IntercolonyLog.Error("Failed to record completed construction: " + ex);
            }
        }
    }
}
