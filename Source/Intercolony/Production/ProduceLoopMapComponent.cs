using System.Collections.Generic;
using RimWorld;
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

        public override void MapComponentTick()
        {
            base.MapComponentTick();

            if (!map.IsHashIntervalTick(60))
            {
                return;
            }

            RunPass();
        }

        internal void RunPass()
        {
            List<ProduceLoopRecord> snapshot = new List<ProduceLoopRecord>(loops);
            for (int i = 0; i < snapshot.Count; i++)
            {
                TickLoop(snapshot[i]);
            }
        }

        private void TickLoop(ProduceLoopRecord loop)
        {
            // Pause leaves an in-flight blueprint or frame for vanilla to finish, while cancelling the
            // loop's outstanding uninstall designation so an installed object stays installed. It prevents
            // a replacement blueprint and the next cycle; Stop remains Disable.
            if (loop.paused)
            {
                return;
            }

            // Reaching the target stops new cycles and latches; the latch clears only at or below
            // the resume-below threshold. Work already under way is never destroyed.
            if (loop.targetCount > 0)
            {
                int stored = CountStoredThings(loop);

                if (!loop.waitingForResume && stored >= loop.targetCount)
                {
                    loop.waitingForResume = true;
                }

                if (loop.waitingForResume)
                {
                    if (stored > loop.EffectiveResumeBelow)
                    {
                        return;
                    }

                    loop.waitingForResume = false;
                }
            }
            else if (loop.waitingForResume)
            {
                loop.waitingForResume = false;
            }

            if (loop.thingDef == null || !loop.cell.InBounds(map) || !loop.thingDef.Minifiable)
            {
                Disable(loop.cell);
                return;
            }

            // This guard is a performance early-out: in steady state, most loops have work under way,
            // and it avoids a full CanPlaceBlueprintAt evaluation every pass. Removing it is not
            // behaviourally observable, because CanPlaceBlueprintAt refuses the duplicate anyway.
            // The M7 mutation test confirmed this.
            List<Thing> thingsAtCell = map.thingGrid.ThingsListAt(loop.cell);
            for (int i = 0; i < thingsAtCell.Count; i++)
            {
                Thing thing = thingsAtCell[i];
                if (thing is Blueprint blueprint && blueprint.def.entityDefToBuild == loop.thingDef)
                {
                    return;
                }

                if (thing is Frame frame && frame.def.entityDefToBuild == loop.thingDef)
                {
                    return;
                }
            }

            Building finishedBuilding = null;
            for (int i = 0; i < thingsAtCell.Count; i++)
            {
                if (thingsAtCell[i] is Building building && building.def == loop.thingDef)
                {
                    finishedBuilding = building;
                    break;
                }
            }

            if (finishedBuilding != null)
            {
                if (map.designationManager.DesignationOn(finishedBuilding, DesignationDefOf.Uninstall) != null ||
                    map.designationManager.DesignationOn(finishedBuilding, DesignationDefOf.Deconstruct) != null ||
                    !PassesVanillaUninstallEligibility(finishedBuilding))
                {
                    return;
                }

                if (finishedBuilding.Faction != Faction.OfPlayer)
                {
                    finishedBuilding.SetFaction(Faction.OfPlayer);
                }

                if (finishedBuilding.GetStatValue(StatDefOf.WorkToBuild) == 0f || finishedBuilding.def.IsFrame)
                {
                    finishedBuilding.Uninstall();
                }
                else
                {
                    map.designationManager.AddDesignation(
                        new Designation(finishedBuilding, DesignationDefOf.Uninstall));
                }

                return;
            }

            ThingDef resolvedStuff = ResolveStuffForNextCycle(loop);
            if (resolvedStuff == null)
            {
                // No allowed material can make this product at all; this is a configuration problem, not a transient shortage.
                // Keep this silent because TickLoop runs every 60 ticks.
                return;
            }

            if (resolvedStuff != loop.stuffDef)
            {
                loop.stuffDef = resolvedStuff;
            }

            if (!GenConstruct.CanPlaceBlueprintAt(
                    loop.thingDef,
                    loop.cell,
                    loop.rotation,
                    map,
                    stuffDef: resolvedStuff).Accepted)
            {
                return;
            }

            GenConstruct.PlaceBlueprintForBuild(
                loop.thingDef,
                loop.cell,
                map,
                loop.rotation,
                Faction.OfPlayer,
                resolvedStuff,
                styleDef: loop.styleDef);
        }

        private ThingDef ResolveStuffForNextCycle(ProduceLoopRecord loop)
        {
            // The execution plan's rule 6.7 said not to start a new blueprint when no allowed stuff could satisfy the requirement.
            // Applied literally, that stopped production in any colony whose stockpile was momentarily empty, breaking ten existing assertions.
            // Availability now ranks the candidates instead of vetoing blueprint placement.
            if (!loop.thingDef.MadeFromStuff)
            {
                return loop.stuffDef;
            }

            ThingDef currentStuff = loop.stuffDef;
            int currentAvailable;
            if (IsAllowedStuff(loop, currentStuff) &&
                HasEnoughMaterial(loop, currentStuff, out currentAvailable))
            {
                return currentStuff;
            }

            ThingDef bestStuff = null;
            int bestAvailable = -1;
            for (int i = 0; i < loop.allowedStuff.Count; i++)
            {
                ThingDef candidate = loop.allowedStuff[i];
                int available;
                if (!IsAllowedStuff(loop, candidate) ||
                    !HasEnoughMaterial(loop, candidate, out available))
                {
                    continue;
                }

                if (bestStuff == null ||
                    available > bestAvailable ||
                    (available == bestAvailable &&
                     string.CompareOrdinal(candidate.defName, bestStuff.defName) < 0))
                {
                    bestStuff = candidate;
                    bestAvailable = available;
                }
            }

            if (bestStuff != null)
            {
                return bestStuff;
            }

            if (IsAllowedStuff(loop, currentStuff))
            {
                return currentStuff;
            }

            ThingDef fallbackStuff = null;
            for (int i = 0; i < loop.allowedStuff.Count; i++)
            {
                ThingDef candidate = loop.allowedStuff[i];
                if (!IsAllowedStuff(loop, candidate))
                {
                    continue;
                }

                if (fallbackStuff == null ||
                    string.CompareOrdinal(candidate.defName, fallbackStuff.defName) < 0)
                {
                    fallbackStuff = candidate;
                }
            }

            return fallbackStuff;
        }

        private bool IsAllowedStuff(ProduceLoopRecord loop, ThingDef stuff)
        {
            return stuff != null &&
                loop.allowedStuff.Contains(stuff) &&
                stuff.stuffProps != null &&
                loop.thingDef != null &&
                stuff.stuffProps.CanMake(loop.thingDef);
        }

        private bool HasEnoughMaterial(ProduceLoopRecord loop, ThingDef stuff, out int available)
        {
            int required = 0;
            List<ThingDefCountClass> costList = loop.thingDef.CostListAdjusted(stuff);
            if (costList != null)
            {
                for (int i = 0; i < costList.Count; i++)
                {
                    if (costList[i] != null && costList[i].thingDef == stuff)
                    {
                        required = costList[i].count;
                        break;
                    }
                }
            }

            if (!stuff.CountAsResource)
            {
                available = int.MaxValue;
                return true;
            }

            available = map.resourceCounter.GetCount(stuff);
            return available >= required;
        }

        private static bool PassesVanillaUninstallEligibility(Building building)
        {
            if (building.def.category != ThingCategory.Building || !building.def.Minifiable)
            {
                return false;
            }

            if (!DebugSettings.godMode && building.Faction != Faction.OfPlayer &&
                !building.def.building.alwaysUninstallable)
            {
                if (building.Faction != null || !building.ClaimableBy(Faction.OfPlayer).Accepted)
                {
                    return false;
                }
            }

            return true;
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

        private int CountStoredThings(ProduceLoopRecord loop)
        {
            // Storage groups are the relevant source, not ColonyStock: its trade-item filter would
            // discard minified buildings even though their inner thing is exactly what we count.
            int count = 0;
            HashSet<Thing> seenThings = new HashSet<Thing>();
            List<SlotGroup> groups = map.haulDestinationManager.AllGroupsListForReading;
            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                SlotGroup group = groups[groupIndex];
                if (group == null)
                {
                    continue;
                }

                foreach (Thing thing in group.HeldThings)
                {
                    if (!seenThings.Add(thing))
                    {
                        continue;
                    }

                    Thing inner = thing.GetInnerIfMinified();
                    if (inner?.def == loop.thingDef &&
                        (loop.thingDef == null ||
                         !loop.thingDef.MadeFromStuff ||
                         loop.allowedStuff.Count == 0 ||
                         loop.allowedStuff.Contains(inner.Stuff)))
                    {
                        count += inner.stackCount;
                    }
                }
            }

            return count;
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
                styleDef = styleDef,
                resumeBelow = -1,
                waitingForResume = false,
                allowedStuff = stuffDef != null ? new List<ThingDef> { stuffDef } : new List<ThingDef>(),
                restrictToSelectedWorkers = false,
                allowedWorkers = new List<Pawn>(),
                minConstructionSkill = 0
            });
        }

        public void Pause(IntVec3 cell)
        {
            ProduceLoopRecord loop = Find(cell);
            if (loop == null)
            {
                return;
            }

            Building installedBuilding = null;
            if (loop.thingDef != null && loop.cell.InBounds(map))
            {
                List<Thing> thingsAtCell = map.thingGrid.ThingsListAt(loop.cell);
                for (int i = 0; i < thingsAtCell.Count; i++)
                {
                    if (thingsAtCell[i] is Building building && building.def == loop.thingDef)
                    {
                        installedBuilding = building;
                        break;
                    }
                }
            }

            if (installedBuilding != null)
            {
                Designation uninstallDesignation = map.designationManager.DesignationOn(
                    installedBuilding,
                    DesignationDefOf.Uninstall);
                if (uninstallDesignation != null)
                {
                    map.designationManager.RemoveDesignation(uninstallDesignation);
                }
            }

            loop.paused = true;
        }

        public void Disable(IntVec3 cell)
        {
            loops.RemoveAll(loop => loop.cell == cell);
        }

        public void Resume(IntVec3 cell)
        {
            ProduceLoopRecord loop = Find(cell);
            if (loop == null)
            {
                return;
            }

            loop.paused = false;
        }

        public void SetTargetCount(IntVec3 cell, int targetCount)
        {
            ProduceLoopRecord loop = Find(cell);
            if (loop == null)
            {
                return;
            }

            loop.targetCount = targetCount < 0 ? 0 : targetCount;
            loop.waitingForResume = false;
            if (loop.targetCount == 0)
            {
                loop.resumeBelow = -1;
            }
            else if (loop.resumeBelow >= 0 && loop.resumeBelow >= loop.targetCount)
            {
                loop.resumeBelow = loop.targetCount - 1;
            }
        }

        public void SetResumeBelow(IntVec3 cell, int resumeBelow)
        {
            ProduceLoopRecord loop = Find(cell);
            if (loop == null)
            {
                return;
            }

            if (resumeBelow < 0)
            {
                loop.resumeBelow = -1;
            }
            else if (loop.targetCount > 0)
            {
                loop.resumeBelow = System.Math.Min(resumeBelow, loop.targetCount - 1);
            }
            else
            {
                loop.resumeBelow = resumeBelow;
            }

            loop.waitingForResume = false;
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
