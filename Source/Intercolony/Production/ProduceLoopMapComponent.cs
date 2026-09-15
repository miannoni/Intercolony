using System.Collections.Generic;
using System.Linq;
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
        private List<ProduceControlPreset> presets = new List<ProduceControlPreset>();
        private int nextPresetId = 1;
        private int presetsRevision;

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

        public IReadOnlyList<ProduceControlPreset> Presets
        {
            get { return presets; }
        }

        public int PresetsRevision
        {
            get { return presetsRevision; }
        }

        public ProduceControlPreset FindPreset(int id)
        {
            if (presets == null)
            {
                return null;
            }

            for (int i = 0; i < presets.Count; i++)
            {
                if (presets[i] != null && presets[i].id == id)
                {
                    return presets[i];
                }
            }

            return null;
        }

        public ProduceControlPreset FindPresetByName(string name)
        {
            if (presets == null || name == null)
            {
                return null;
            }

            string trimmedName = name.Trim();
            if (trimmedName.Length == 0)
            {
                return null;
            }

            for (int i = 0; i < presets.Count; i++)
            {
                ProduceControlPreset preset = presets[i];
                if (preset != null &&
                    string.Equals(
                        preset.name == null ? string.Empty : preset.name.Trim(),
                        trimmedName,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    return preset;
                }
            }

            return null;
        }

        public ProduceControlPreset CreatePresetFromLoop(
            string name,
            ProduceLoopRecord loop,
            out string reason)
        {
            reason = null;
            string trimmedName = NormalizePresetName(name);
            if (trimmedName.Length == 0)
            {
                reason = "Preset name cannot be empty.";
                return null;
            }

            if (loop == null)
            {
                reason = "A production loop is required.";
                return null;
            }

            if (FindPresetByName(trimmedName) != null)
            {
                reason = "A preset with that name already exists.";
                return null;
            }

            ProduceControlPreset preset = new ProduceControlPreset
            {
                id = nextPresetId++,
                name = trimmedName,
                targetCount = loop.targetCount,
                resumeBelow = loop.resumeBelow,
                restrictToSelectedWorkers = loop.restrictToSelectedWorkers,
                allowedWorkers = CopyAllowedWorkers(loop.allowedWorkers),
                minConstructionSkill = loop.minConstructionSkill,
                allowedStuff = CopyAllowedStuff(loop.allowedStuff)
            };
            presets.Add(preset);
            presetsRevision++;
            return preset;
        }

        public bool TryRenamePreset(int id, string newName, out string reason)
        {
            reason = null;
            string trimmedName = NormalizePresetName(newName);
            if (trimmedName.Length == 0)
            {
                reason = "Preset name cannot be empty.";
                return false;
            }

            ProduceControlPreset preset = FindPreset(id);
            if (preset == null)
            {
                reason = "Preset was not found.";
                return false;
            }

            ProduceControlPreset duplicate = FindPresetByName(trimmedName);
            if (duplicate != null && duplicate.id != id)
            {
                reason = "A preset with that name already exists.";
                return false;
            }

            if (duplicate == preset)
            {
                if (!string.Equals(preset.name, trimmedName, System.StringComparison.Ordinal))
                {
                    preset.name = trimmedName;
                    presetsRevision++;
                }

                return true;
            }

            if (!string.Equals(preset.name, trimmedName, System.StringComparison.Ordinal))
            {
                preset.name = trimmedName;
                presetsRevision++;
            }

            return true;
        }

        public bool TryResolveProduceTargetCell(IntVec3 cell, out IntVec3 targetCell)
        {
            Rot4 rotation;
            ThingDef thingDef;
            ThingDef stuffDef;
            ThingStyleDef styleDef;
            return TryResolveProduceTargetCell(
                cell,
                out targetCell,
                out rotation,
                out thingDef,
                out stuffDef,
                out styleDef);
        }

        private bool TryResolveProduceTargetCell(
            IntVec3 cell,
            out IntVec3 targetCell,
            out Rot4 rotation,
            out ThingDef thingDef,
            out ThingDef stuffDef,
            out ThingStyleDef styleDef)
        {
            targetCell = IntVec3.Invalid;
            rotation = default(Rot4);
            thingDef = null;
            stuffDef = null;
            styleDef = null;

            if (!cell.InBounds(map))
            {
                return false;
            }

            foreach (Thing thing in map.thingGrid.ThingsAt(cell).OrderByDescending(t => t.def.altitudeLayer))
            {
                if (!ProduceSubjectUtility.TryGetProduceSubject(
                        thing,
                        out rotation,
                        out thingDef,
                        out stuffDef,
                        out styleDef))
                {
                    continue;
                }

                // The gizmo uses thing.Position as this object's loop key (ProduceGizmoPatch.cs:66), not the dragged cell.
                targetCell = thing.Position;
                return true;
            }

            return false;
        }

        public bool TryApplyPreset(IntVec3 cell, ProduceControlPreset preset, out string reason)
        {
            return TryApplyPreset(cell, preset, out _, out reason);
        }

        public bool TryApplyPreset(
            IntVec3 cell,
            ProduceControlPreset preset,
            out IntVec3 targetCell,
            out string reason)
        {
            targetCell = IntVec3.Invalid;
            reason = null;
            if (!cell.InBounds(map))
            {
                reason = "cell is outside the map";
                return false;
            }

            if (preset == null)
            {
                reason = "preset is required";
                return false;
            }

            Rot4 rotation;
            ThingDef thingDef;
            ThingDef stuffDef;
            ThingStyleDef styleDef;
            if (!TryResolveProduceTargetCell(
                    cell,
                    out targetCell,
                    out rotation,
                    out thingDef,
                    out stuffDef,
                    out styleDef))
            {
                reason = "no production object here";
                return false;
            }

            List<ThingDef> compatibleStuff = new List<ThingDef>();
            bool applyAllowedStuff = false;
            if (!thingDef.MadeFromStuff)
            {
                // Non-stuffable products have no material constraint to carry into the program.
                applyAllowedStuff = true;
            }
            else if (preset.allowedStuff != null && preset.allowedStuff.Count > 0)
            {
                for (int i = 0; i < preset.allowedStuff.Count; i++)
                {
                    ThingDef stuff = preset.allowedStuff[i];
                    if (stuff != null &&
                        stuff.stuffProps != null &&
                        stuff.stuffProps.CanMake(thingDef) &&
                        !compatibleStuff.Contains(stuff))
                    {
                        compatibleStuff.Add(stuff);
                    }
                }

                if (compatibleStuff.Count == 0)
                {
                    reason = "no compatible allowed material";
                    return false;
                }

                applyAllowedStuff = true;
            }

            ProduceLoopRecord loop = Find(targetCell);
            if (loop == null)
            {
                Enable(targetCell, rotation, thingDef, stuffDef, styleDef);
                loop = Find(targetCell);
            }

            SetTargetCount(targetCell, preset.targetCount);
            SetResumeBelow(targetCell, preset.resumeBelow);
            SetWorkerRestriction(targetCell, preset.restrictToSelectedWorkers);
            SetAllowedWorkers(targetCell, preset.allowedWorkers);
            SetMinConstructionSkill(targetCell, preset.minConstructionSkill);
            if (applyAllowedStuff)
            {
                SetAllowedStuff(targetCell, compatibleStuff);
            }

            // SetTargetCount and SetResumeBelow intentionally clear this latch; recompute it from this target's stock after all settings are applied.
            if (loop.targetCount <= 0)
            {
                loop.waitingForResume = false;
            }
            else
            {
                loop.waitingForResume = CountStoredThings(loop) >= loop.targetCount;
            }

            return true;
        }

        public void ApplyPresetSettings(int id, ProduceControlPreset settings)
        {
            ProduceControlPreset preset = FindPreset(id);
            if (preset == null || settings == null)
            {
                return;
            }

            preset.targetCount = settings.targetCount;
            preset.resumeBelow = settings.resumeBelow;
            preset.restrictToSelectedWorkers = settings.restrictToSelectedWorkers;
            preset.allowedWorkers = CopyAllowedWorkers(settings.allowedWorkers);
            preset.minConstructionSkill = settings.minConstructionSkill;
            preset.allowedStuff = CopyAllowedStuff(settings.allowedStuff);
            presetsRevision++;
        }

        public bool RemovePreset(int id)
        {
            ProduceControlPreset preset = FindPreset(id);
            if (preset == null)
            {
                return false;
            }

            presets.Remove(preset);
            presetsRevision++;
            return true;
        }

        private static string NormalizePresetName(string name)
        {
            return name == null ? string.Empty : name.Trim();
        }

        private static List<Pawn> CopyAllowedWorkers(List<Pawn> workers)
        {
            List<Pawn> copiedWorkers = workers == null
                ? new List<Pawn>()
                : new List<Pawn>(workers);
            copiedWorkers.RemoveAll(worker => worker == null);
            return copiedWorkers;
        }

        private static List<ThingDef> CopyAllowedStuff(List<ThingDef> stuffs)
        {
            List<ThingDef> copiedStuffs = stuffs == null
                ? new List<ThingDef>()
                : new List<ThingDef>(stuffs);
            copiedStuffs.RemoveAll(stuff => stuff == null);
            return copiedStuffs;
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

        public void SetWorkerRestriction(IntVec3 cell, bool restrict)
        {
            ProduceLoopRecord loop = Find(cell);
            if (loop == null)
            {
                return;
            }

            loop.restrictToSelectedWorkers = restrict;
        }

        public void SetAllowedWorkers(IntVec3 cell, List<Pawn> workers)
        {
            ProduceLoopRecord loop = Find(cell);
            if (loop == null)
            {
                return;
            }

            loop.allowedWorkers = workers == null ? new List<Pawn>() : new List<Pawn>(workers);
            loop.allowedWorkers.RemoveAll(worker => worker == null);
        }

        public void SetMinConstructionSkill(IntVec3 cell, int level)
        {
            ProduceLoopRecord loop = Find(cell);
            if (loop == null)
            {
                return;
            }

            loop.minConstructionSkill = System.Math.Max(0, System.Math.Min(20, level));
        }

        public void SetAllowedStuff(IntVec3 cell, List<ThingDef> stuffs)
        {
            ProduceLoopRecord loop = Find(cell);
            if (loop == null)
            {
                return;
            }

            List<ThingDef> copiedStuffs = stuffs == null
                ? new List<ThingDef>()
                : new List<ThingDef>(stuffs);
            copiedStuffs.RemoveAll(stuff => stuff == null);
            if (loop.thingDef != null && loop.thingDef.MadeFromStuff && copiedStuffs.Count == 0)
            {
                // An empty set makes ResolveStuffForNextCycle return null and silently stops a
                // stuffable program forever, so keep the existing set instead.
                return;
            }

            loop.allowedStuff = copiedStuffs;
        }

        public IReadOnlyList<ProduceLoopRecord> Loops
        {
            get { return loops; }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref loops, "loops", LookMode.Deep);
            Scribe_Collections.Look(ref presets, "presets", LookMode.Deep);
            Scribe_Values.Look(ref nextPresetId, "nextPresetId", 1);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && loops == null)
            {
                loops = new List<ProduceLoopRecord>();
            }

            if (Scribe.mode == LoadSaveMode.PostLoadInit && presets == null)
            {
                presets = new List<ProduceControlPreset>();
            }
        }
    }
}
