using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Exercises the production loop against a live map, but with a detached component so existing
    /// player loops are never ticked or replaced by a diagnostic fixture.
    /// </summary>
    public static class IntercolonyProduceSelfTest
    {
        private class Results
        {
            public readonly StringBuilder sb = new StringBuilder();
            public int passed;
            public int failed;

            public void Check(bool condition, string label, string detail = null)
            {
                if (condition)
                {
                    passed++;
                    sb.AppendLine($"  PASS  {label}{(detail == null ? "" : $"  ({detail})")}");
                }
                else
                {
                    failed++;
                    sb.AppendLine($"  FAIL  {label}{(detail == null ? "" : $"  ({detail})")}");
                }
            }

            public void Info(string line)
            {
                sb.AppendLine($"        {line}");
            }

            public void Skip(string label, string reason)
            {
                sb.AppendLine($"SKIPPED {label} — {reason}");
            }
        }

        private sealed class Subject
        {
            public ThingDef thingDef;
            public ThingDef stuffDef;
            public ThingDef nonDefaultStuff;
            public int validStuffCount;
        }

        public static string Run(IntercolonyWorldComponent state, Map map)
        {
            Results r = new Results();
            r.sb.AppendLine("Produce loop self-test");

            if (state == null || map == null)
            {
                r.sb.AppendLine("  No world or map. Open a colony first.");
                return Summarize(r);
            }

            ProduceLoopMapComponent loops = new ProduceLoopMapComponent(map);
            HashSet<IntVec3> reservedCells = new HashSet<IntVec3>();
            List<CellRect> testRects = new List<CellRect>();
            List<Designation> addedDesignations = new List<Designation>();

            try
            {
                Subject subject = FindSubject();
                if (subject == null)
                {
                    SkipSubjectAssertions(r);
                }
                else
                {
                    r.Info($"subject {subject.thingDef.defName} using {subject.stuffDef.defName}");
                    CheckFinishedBuilding(r, map, loops, subject, reservedCells, testRects, addedDesignations);
                    CheckDeconstructGuard(r, map, loops, subject, reservedCells, testRects, addedDesignations);
                    CheckDisablePreservesWork(
                        r, map, loops, subject, reservedCells, testRects, addedDesignations);
                    CheckBlueprintPlacement(
                        r, map, loops, subject, reservedCells, testRects);
                    CheckBlueprintRotation(r, map, loops, subject, reservedCells, testRects);
                }

                CheckNullDefDrop(r, map, loops, subject, reservedCells, testRects);

                if (subject == null)
                {
                    r.Skip(
                        "a record survives a save/load round trip",
                        "no loaded minifiable stuff-built building");
                }
                else
                {
                    CheckRecordRoundTrip(r, map, subject);
                }
            }
            catch (Exception ex)
            {
                r.sb.AppendLine($"  EXCEPTION: {ex}");
                r.failed++;
            }
            finally
            {
                CleanupDesignations(map, addedDesignations, testRects, r);
                CleanupThings(map, testRects, r);
                ClearLoops(loops, r);
            }

            return Summarize(r);
        }

        private static void CheckFinishedBuilding(
            Results r,
            Map map,
            ProduceLoopMapComponent loops,
            Subject subject,
            HashSet<IntVec3> reservedCells,
            List<CellRect> testRects,
            List<Designation> addedDesignations)
        {
            IntVec3 cell;
            if (!TryFindBuildCell(map, loops, subject, Rot4.North, reservedCells, out cell))
            {
                r.Skip("designates a finished building", "no empty valid cell for the subject");
                r.Skip("does not designate twice", "no empty valid cell for the subject");
                return;
            }

            RememberCell(cell, subject.thingDef, Rot4.North, reservedCells, testRects);
            Building building = null;
            Designation uninstall = null;
            CheckSafely(r, "designates a finished building", () =>
            {
                building = SpawnFinishedBuilding(map, subject, cell, Rot4.North);
                if (building == null)
                {
                    return false;
                }

                loops.Enable(cell, Rot4.North, subject.thingDef, subject.stuffDef, null);
                loops.RunPass();
                uninstall = map.designationManager.DesignationOn(
                    building, DesignationDefOf.Uninstall);
                if (uninstall != null)
                {
                    addedDesignations.Add(uninstall);
                }

                return uninstall != null;
            }, building == null ? null : building.ToStringSafe());

            if (uninstall == null)
            {
                r.Skip(
                    "does not designate twice",
                    "the first pass did not create an Uninstall designation");
                return;
            }

            int duplicateDesignationErrors = 0;
            CheckSafely(r, "does not designate twice", () =>
            {
                int duplicateDesignationErrorsBefore = CountDuplicateDesignationErrors();
                loops.RunPass();
                int uninstallCount = CountDesignationsOn(
                    map, building, DesignationDefOf.Uninstall);
                duplicateDesignationErrors =
                    CountDuplicateDesignationErrors() - duplicateDesignationErrorsBefore;
                return uninstallCount == 1 && duplicateDesignationErrors == 0;
            }, $"{CountDesignationsOn(map, building, DesignationDefOf.Uninstall)} designation(s), " +
               $"{duplicateDesignationErrors} duplicate-add error(s)");
        }

        private static void CheckDisablePreservesWork(
            Results r,
            Map map,
            ProduceLoopMapComponent loops,
            Subject subject,
            HashSet<IntVec3> reservedCells,
            List<CellRect> testRects,
            List<Designation> addedDesignations)
        {
            IntVec3 cell;
            if (!TryFindBuildCell(map, loops, subject, Rot4.North, reservedCells, out cell))
            {
                r.Skip("Disable does not cancel work under way", "no empty valid cell for the subject");
                return;
            }

            RememberCell(cell, subject.thingDef, Rot4.North, reservedCells, testRects);
            Building building = null;
            Designation uninstall = null;
            CheckSafely(r, "Disable does not cancel work under way", () =>
            {
                building = SpawnFinishedBuilding(map, subject, cell, Rot4.North);
                if (building == null)
                {
                    return false;
                }

                loops.Enable(cell, Rot4.North, subject.thingDef, subject.stuffDef, null);
                loops.RunPass();
                uninstall = map.designationManager.DesignationOn(
                    building, DesignationDefOf.Uninstall);
                if (uninstall == null)
                {
                    return false;
                }

                addedDesignations.Add(uninstall);
                loops.Disable(cell);
                return map.designationManager.DesignationOn(
                    building, DesignationDefOf.Uninstall) == uninstall;
            }, building == null ? null : building.ToStringSafe());
        }

        private static void CheckDeconstructGuard(
            Results r,
            Map map,
            ProduceLoopMapComponent loops,
            Subject subject,
            HashSet<IntVec3> reservedCells,
            List<CellRect> testRects,
            List<Designation> addedDesignations)
        {
            IntVec3 cell;
            if (!TryFindBuildCell(map, loops, subject, Rot4.North, reservedCells, out cell))
            {
                r.Skip("a Deconstruct designation wins", "no empty valid cell for the subject");
                return;
            }

            RememberCell(cell, subject.thingDef, Rot4.North, reservedCells, testRects);
            Building building = null;
            Designation deconstruct = null;
            CheckSafely(r, "a Deconstruct designation wins", () =>
            {
                building = SpawnFinishedBuilding(map, subject, cell, Rot4.North);
                if (building == null)
                {
                    return false;
                }

                deconstruct = new Designation(building, DesignationDefOf.Deconstruct);
                map.designationManager.AddDesignation(deconstruct);
                addedDesignations.Add(deconstruct);
                loops.Enable(cell, Rot4.North, subject.thingDef, subject.stuffDef, null);
                loops.RunPass();
                return map.designationManager.DesignationOn(
                    building, DesignationDefOf.Uninstall) == null;
            }, building == null ? null : building.ToStringSafe());
        }

        private static void CheckBlueprintPlacement(
            Results r,
            Map map,
            ProduceLoopMapComponent loops,
            Subject subject,
            HashSet<IntVec3> reservedCells,
            List<CellRect> testRects)
        {
            IntVec3 cell;
            if (!TryFindBuildCell(
                    map, loops, subject, Rot4.North, reservedCells, out cell, Rot4.East))
            {
                SkipBlueprintAssertions(r, "no empty valid cell for the subject");
                return;
            }

            RememberCell(cell, subject.thingDef, Rot4.North, reservedCells, testRects);
            Blueprint_Build blueprint = null;
            CheckSafely(r, "re-blueprints an empty cell", () =>
            {
                loops.Enable(cell, Rot4.North, subject.thingDef, subject.stuffDef, null);
                loops.RunPass();
                blueprint = FindBlueprint(map, cell, subject.thingDef);
                return blueprint != null &&
                       blueprint.def.entityDefToBuild == subject.thingDef;
            }, blueprint == null ? null : blueprint.ToStringSafe());

            bool placementSucceeded = blueprint != null &&
                                      blueprint.def.entityDefToBuild == subject.thingDef;
            if (!placementSucceeded)
            {
                r.Skip(
                    "the blueprint carries the recorded material",
                    "the empty-cell placement assertion did not create the expected blueprint");
                r.Skip(
                    "an occupied cell is left alone",
                    "the empty-cell placement assertion did not create the expected blueprint");
                r.Skip(
                    "Disable stops the next repetition",
                    "the empty-cell placement assertion did not create the expected blueprint");
                return;
            }

            if (subject.validStuffCount <= 1 || subject.nonDefaultStuff == null)
            {
                r.Skip(
                    "the blueprint carries the recorded material",
                    "the subject has no valid non-default stuff to distinguish");
            }
            else
            {
                CheckSafely(r, "the blueprint carries the recorded material", () =>
                    blueprint.stuffToUse == subject.stuffDef,
                    $"recorded {subject.stuffDef.defName}");
            }

            ProduceLoopRecord record = loops.Find(cell);
            CheckSafely(r, "an occupied cell is left alone", () =>
            {
                int before = CountBlueprintsAndFramesAt(map, cell);
                if (record == null)
                {
                    return false;
                }

                Rot4 originalRotation = record.rotation;
                try
                {
                    record.rotation = Rot4.East;
                    loops.RunPass();
                    int after = CountBlueprintsAndFramesAt(map, cell);
                    return before == 1 && after == 1;
                }
                finally
                {
                    record.rotation = originalRotation;
                }
            }, $"{CountBlueprintsAndFramesAt(map, cell)} blueprint/frame(s)");

            CheckSafely(r, "Disable stops the next repetition", () =>
            {
                loops.Disable(cell);
                DestroyThingsInRect(map, GenAdj.OccupiedRect(
                    cell, Rot4.North, subject.thingDef.Size));
                loops.RunPass();
                return CountBlueprintsAndFramesAt(map, cell) == 0;
            });
        }

        private static void CheckBlueprintRotation(
            Results r,
            Map map,
            ProduceLoopMapComponent loops,
            Subject subject,
            HashSet<IntVec3> reservedCells,
            List<CellRect> testRects)
        {
            if (!subject.thingDef.rotatable)
            {
                r.Skip("the blueprint carries the recorded rotation", "the subject is not rotatable");
                return;
            }

            Rot4 rotation = subject.thingDef.defaultPlacingRot == Rot4.East
                ? Rot4.South
                : Rot4.East;
            IntVec3 cell;
            if (!TryFindBuildCell(map, loops, subject, rotation, reservedCells, out cell))
            {
                r.Skip("the blueprint carries the recorded rotation", "no empty valid cell for the subject");
                return;
            }

            RememberCell(cell, subject.thingDef, rotation, reservedCells, testRects);
            Blueprint_Build blueprint = null;
            CheckSafely(r, "the blueprint carries the recorded rotation", () =>
            {
                loops.Enable(cell, rotation, subject.thingDef, subject.stuffDef, null);
                loops.RunPass();
                blueprint = FindBlueprint(map, cell, subject.thingDef);
                return blueprint != null && blueprint.Rotation == rotation;
            }, blueprint == null ? null : blueprint.Rotation.ToString());
        }

        private static void CheckNullDefDrop(
            Results r,
            Map map,
            ProduceLoopMapComponent loops,
            Subject subject,
            HashSet<IntVec3> reservedCells,
            List<CellRect> testRects)
        {
            IntVec3 cell;
            if (!TryFindUnusedCell(map, loops, reservedCells, out cell))
            {
                r.Skip("a record with a null def is dropped", "no unused in-bounds cell");
                return;
            }

            RememberCell(cell, null, Rot4.North, reservedCells, testRects);
            CheckSafely(r, "a record with a null def is dropped", () =>
            {
                loops.Enable(
                    cell,
                    Rot4.North,
                    subject?.thingDef,
                    subject?.stuffDef,
                    null);
                ProduceLoopRecord record = loops.Find(cell);
                if (record == null)
                {
                    return false;
                }

                record.thingDef = null;
                loops.RunPass();
                return !loops.IsEnabled(cell);
            });
        }

        private static void CheckRecordRoundTrip(Results r, Map map, Subject subject)
        {
            IntVec3 expectedCell = map.Center;
            Rot4 expectedRotation = Rot4.West;
            ProduceLoopMapComponent saved = new ProduceLoopMapComponent(map);
            saved.Enable(
                expectedCell,
                expectedRotation,
                subject.thingDef,
                subject.stuffDef,
                null);

            ProduceLoopMapComponent loaded = null;
            string failure = null;
            bool recordFound = false;
            IntVec3 loadedCell = default(IntVec3);
            Rot4 loadedRotation = default(Rot4);
            ThingDef loadedThingDef = null;
            ThingDef loadedStuffDef = null;
            string path = Path.Combine(
                Path.GetTempPath(), $"Intercolony-ProduceLoop-{Guid.NewGuid():N}.xml");

            try
            {
                Scribe.saver.InitSaving(path, "intercolonyProduceLoopTest");
                Scribe_Deep.Look(ref saved, "produceLoopMapComponent");
                Scribe.saver.FinalizeSaving();

                Scribe.loader.InitLoading(path);
                Scribe_Deep.Look(ref loaded, "produceLoopMapComponent", map);
                Scribe.loader.FinalizeLoading();

                ProduceLoopRecord record = loaded?.Find(expectedCell);
                recordFound = record != null;
                if (recordFound)
                {
                    loadedCell = record.cell;
                    loadedRotation = record.rotation;
                    loadedThingDef = record.thingDef;
                    loadedStuffDef = record.stuffDef;
                }
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                Scribe.ForceStop();
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                saved?.Disable(expectedCell);
                loaded?.Disable(expectedCell);
            }

            bool ok = failure == null &&
                      recordFound &&
                      loadedCell == expectedCell &&
                      loadedRotation == expectedRotation &&
                      loadedThingDef == subject.thingDef &&
                      loadedStuffDef == subject.stuffDef;
            string detail = failure;
            if (detail == null)
            {
                detail = !recordFound
                    ? "no loaded record"
                    : (loadedCell != expectedCell ||
                       loadedRotation != expectedRotation ||
                       loadedThingDef != subject.thingDef ||
                       loadedStuffDef != subject.stuffDef
                        ? $"cell {loadedCell}, rotation {loadedRotation}, " +
                          $"def {loadedThingDef?.defName ?? "null"}, " +
                          $"stuff {loadedStuffDef?.defName ?? "null"}"
                        : null);
            }
            r.Check(
                ok,
                "a record survives a save/load round trip",
                detail);
        }

        private static Subject FindSubject()
        {
            Subject fallback = null;
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs)
            {
                if (def == null ||
                    !def.Minifiable ||
                    def.category != ThingCategory.Building ||
                    !def.MadeFromStuff ||
                    def.IsFrame ||
                    def.blueprintDef == null ||
                    def.building == null ||
                    def.thingClass == null ||
                    !typeof(Building).IsAssignableFrom(def.thingClass) ||
                    !def.CanHaveFaction)
                {
                    continue;
                }

                List<ThingDef> validStuffs = new List<ThingDef>();
                foreach (ThingDef stuff in GenStuff.AllowedStuffsFor(def))
                {
                    if (stuff != null && !validStuffs.Contains(stuff))
                    {
                        validStuffs.Add(stuff);
                    }
                }

                if (validStuffs.Count == 0)
                {
                    continue;
                }

                ThingDef defaultStuff = GenStuff.DefaultStuffFor(def);
                ThingDef nonDefaultStuff = null;
                for (int i = 0; i < validStuffs.Count; i++)
                {
                    if (validStuffs[i] != defaultStuff)
                    {
                        nonDefaultStuff = validStuffs[i];
                        break;
                    }
                }

                ThingDef selectedStuff = null;
                if (nonDefaultStuff != null &&
                    def.GetStatValueAbstract(StatDefOf.WorkToBuild, nonDefaultStuff) > 0f)
                {
                    selectedStuff = nonDefaultStuff;
                }
                else
                {
                    for (int i = 0; i < validStuffs.Count; i++)
                    {
                        if (def.GetStatValueAbstract(StatDefOf.WorkToBuild, validStuffs[i]) > 0f)
                        {
                            selectedStuff = validStuffs[i];
                            break;
                        }
                    }
                }

                if (selectedStuff == null)
                {
                    continue;
                }

                Subject candidate = new Subject
                {
                    thingDef = def,
                    stuffDef = selectedStuff,
                    nonDefaultStuff = selectedStuff != defaultStuff ? selectedStuff : null,
                    validStuffCount = validStuffs.Count
                };

                if (candidate.nonDefaultStuff != null)
                {
                    return candidate;
                }

                fallback = fallback ?? candidate;
            }

            return fallback;
        }

        private static bool TryFindBuildCell(
            Map map,
            ProduceLoopMapComponent loops,
            Subject subject,
            Rot4 rotation,
            HashSet<IntVec3> reservedCells,
            out IntVec3 cell,
            Rot4? alternateRotation = null)
        {
            foreach (IntVec3 candidate in map.AllCells)
            {
                if (loops.IsEnabled(candidate))
                {
                    continue;
                }

                CellRect occupied = GenAdj.OccupiedRect(
                    candidate, rotation, subject.thingDef.Size);
                if (!occupied.InBounds(map) || Intersects(occupied, reservedCells))
                {
                    continue;
                }

                if (!IsEmpty(map, occupied))
                {
                    continue;
                }

                if (alternateRotation.HasValue)
                {
                    CellRect alternateOccupied = GenAdj.OccupiedRect(
                        candidate, alternateRotation.Value, subject.thingDef.Size);
                    if (!alternateOccupied.InBounds(map) ||
                        Intersects(alternateOccupied, reservedCells) ||
                        !IsEmpty(map, alternateOccupied))
                    {
                        continue;
                    }
                }

                if (!GenConstruct.CanPlaceBlueprintAt(
                        subject.thingDef,
                        candidate,
                        rotation,
                        map,
                        stuffDef: subject.stuffDef).Accepted)
                {
                    continue;
                }

                if (alternateRotation.HasValue &&
                    !GenConstruct.CanPlaceBlueprintAt(
                        subject.thingDef,
                        candidate,
                        alternateRotation.Value,
                        map,
                        stuffDef: subject.stuffDef).Accepted)
                {
                    continue;
                }

                cell = candidate;
                return true;
            }

            cell = IntVec3.Invalid;
            return false;
        }

        private static bool TryFindUnusedCell(
            Map map,
            ProduceLoopMapComponent loops,
            HashSet<IntVec3> reservedCells,
            out IntVec3 cell)
        {
            foreach (IntVec3 candidate in map.AllCells)
            {
                if (loops.IsEnabled(candidate) || reservedCells.Contains(candidate))
                {
                    continue;
                }

                if (IsCellEmpty(map, candidate))
                {
                    cell = candidate;
                    return true;
                }
            }

            cell = IntVec3.Invalid;
            return false;
        }

        private static bool Intersects(CellRect rect, HashSet<IntVec3> reservedCells)
        {
            foreach (IntVec3 cell in rect.Cells)
            {
                if (reservedCells.Contains(cell))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsEmpty(Map map, CellRect rect)
        {
            foreach (IntVec3 cell in rect.Cells)
            {
                if (!IsCellEmpty(map, cell))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsCellEmpty(Map map, IntVec3 cell)
        {
            return map.thingGrid.ThingsListAt(cell).Count == 0 &&
                   map.designationManager.AllDesignationsAt(cell).Count == 0;
        }

        private static Building SpawnFinishedBuilding(
            Map map, Subject subject, IntVec3 cell, Rot4 rotation)
        {
            Thing thing = ThingMaker.MakeThing(subject.thingDef, subject.stuffDef);
            Building building = thing as Building;
            if (building == null)
            {
                thing.Destroy();
                return null;
            }

            building.SetFactionDirect(Faction.OfPlayer);
            Thing spawned = GenSpawn.Spawn(building, cell, map, rotation);
            if (spawned == null)
            {
                if (!building.Destroyed)
                {
                    building.Destroy();
                }

                return null;
            }

            return spawned as Building;
        }

        private static Blueprint_Build FindBlueprint(Map map, IntVec3 cell, ThingDef thingDef)
        {
            List<Thing> things = map.thingGrid.ThingsListAt(cell);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is Blueprint_Build blueprint &&
                    blueprint.def.entityDefToBuild == thingDef)
                {
                    return blueprint;
                }
            }

            return null;
        }

        private static int CountBlueprintsAndFramesAt(Map map, IntVec3 cell)
        {
            int count = 0;
            List<Thing> things = map.thingGrid.ThingsListAt(cell);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is Blueprint || things[i] is Frame)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountDesignationsOn(Map map, Thing thing, DesignationDef def)
        {
            if (thing == null)
            {
                return 0;
            }

            int count = 0;
            List<Designation> designations = map.designationManager.AllDesignationsOn(thing);
            for (int i = 0; i < designations.Count; i++)
            {
                if (designations[i].def == def)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountDuplicateDesignationErrors()
        {
            const string prefix = "Tried to double-add designation on Thing ";
            int count = 0;
            foreach (LogMessage message in Log.Messages)
            {
                if (message != null &&
                    message.type == LogMessageType.Error &&
                    message.text != null &&
                    message.text.StartsWith(prefix, StringComparison.Ordinal))
                {
                    count += message.repeats;
                }
            }

            return count;
        }

        private static void RememberCell(
            IntVec3 cell,
            ThingDef thingDef,
            Rot4 rotation,
            HashSet<IntVec3> reservedCells,
            List<CellRect> testRects)
        {
            CellRect rect = thingDef == null
                ? new CellRect(cell.x, cell.z, 1, 1)
                : GenAdj.OccupiedRect(cell, rotation, thingDef.Size);
            testRects.Add(rect);
            foreach (IntVec3 occupiedCell in rect.Cells)
            {
                reservedCells.Add(occupiedCell);
            }
        }

        private static void CheckSafely(
            Results r,
            string label,
            Func<bool> assertion,
            string detail = null)
        {
            try
            {
                r.Check(assertion(), label, detail);
            }
            catch (Exception ex)
            {
                r.Check(false, label, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void CleanupDesignations(
            Map map,
            List<Designation> addedDesignations,
            List<CellRect> testRects,
            Results r)
        {
            for (int i = 0; i < addedDesignations.Count; i++)
            {
                Designation designation = addedDesignations[i];
                if (designation == null)
                {
                    continue;
                }

                try
                {
                    List<Designation> all = map.designationManager.AllDesignations;
                    for (int j = 0; j < all.Count; j++)
                    {
                        if (all[j] == designation)
                        {
                            map.designationManager.RemoveDesignation(designation);
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    r.sb.AppendLine($"  CLEANUP EXCEPTION: {ex}");
                    r.failed++;
                }
            }

            HashSet<Thing> things = CollectThings(map, testRects);
            foreach (Thing thing in things)
            {
                RemoveAllDesignationsOn(map, thing, DesignationDefOf.Uninstall, r);
                RemoveAllDesignationsOn(map, thing, DesignationDefOf.Deconstruct, r);
            }
        }

        private static void RemoveAllDesignationsOn(
            Map map, Thing thing, DesignationDef def, Results r)
        {
            try
            {
                Designation designation;
                while ((designation = map.designationManager.DesignationOn(thing, def)) != null)
                {
                    map.designationManager.RemoveDesignation(designation);
                }
            }
            catch (Exception ex)
            {
                r.sb.AppendLine($"  CLEANUP EXCEPTION: {ex}");
                r.failed++;
            }
        }

        private static void CleanupThings(Map map, List<CellRect> testRects, Results r)
        {
            HashSet<Thing> things = CollectThings(map, testRects);
            foreach (Thing thing in things)
            {
                try
                {
                    if (!thing.Destroyed)
                    {
                        thing.Destroy(DestroyMode.Vanish);
                    }
                }
                catch (Exception ex)
                {
                    r.sb.AppendLine($"  CLEANUP EXCEPTION: {ex}");
                    r.failed++;
                }
            }
        }

        private static HashSet<Thing> CollectThings(Map map, List<CellRect> rects)
        {
            HashSet<Thing> result = new HashSet<Thing>();
            for (int i = 0; i < rects.Count; i++)
            {
                foreach (IntVec3 cell in rects[i].Cells)
                {
                    List<Thing> things = map.thingGrid.ThingsListAt(cell);
                    for (int j = 0; j < things.Count; j++)
                    {
                        result.Add(things[j]);
                    }
                }
            }

            return result;
        }

        private static void DestroyThingsInRect(Map map, CellRect rect)
        {
            HashSet<Thing> things = new HashSet<Thing>();
            foreach (IntVec3 cell in rect.Cells)
            {
                List<Thing> thingsAtCell = map.thingGrid.ThingsListAt(cell);
                for (int i = 0; i < thingsAtCell.Count; i++)
                {
                    things.Add(thingsAtCell[i]);
                }
            }

            foreach (Thing thing in things)
            {
                if (!thing.Destroyed)
                {
                    thing.Destroy(DestroyMode.Vanish);
                }
            }
        }

        private static void ClearLoops(ProduceLoopMapComponent loops, Results r)
        {
            List<IntVec3> cells = new List<IntVec3>();
            for (int i = 0; i < loops.Loops.Count; i++)
            {
                ProduceLoopRecord record = loops.Loops[i];
                if (record != null)
                {
                    cells.Add(record.cell);
                }
            }

            for (int i = 0; i < cells.Count; i++)
            {
                try
                {
                    loops.Disable(cells[i]);
                }
                catch (Exception ex)
                {
                    r.sb.AppendLine($"  CLEANUP EXCEPTION: {ex}");
                    r.failed++;
                }
            }

            if (loops.Loops.Count != 0)
            {
                r.sb.AppendLine($"  CLEANUP: {loops.Loops.Count} loop record(s) remained.");
                r.failed++;
            }
            else
            {
                r.Info("produce loop records cleared.");
            }
        }

        private static string DescribeRecord(ProduceLoopRecord record)
        {
            return $"cell {record.cell}, rotation {record.rotation}, " +
                   $"def {record.thingDef?.defName ?? "null"}, " +
                   $"stuff {record.stuffDef?.defName ?? "null"}";
        }

        private static void SkipSubjectAssertions(Results r)
        {
            const string reason = "no loaded minifiable stuff-built building";
            r.Skip("designates a finished building", reason);
            r.Skip("does not designate twice", reason);
            r.Skip("a Deconstruct designation wins", reason);
            r.Skip("re-blueprints an empty cell", reason);
            r.Skip("the blueprint carries the recorded material", reason);
            r.Skip("the blueprint carries the recorded rotation", reason);
            r.Skip("an occupied cell is left alone", reason);
            r.Skip("Disable stops the next repetition", reason);
            r.Skip("Disable does not cancel work under way", reason);
        }

        private static void SkipBlueprintAssertions(Results r, string reason)
        {
            r.Skip("re-blueprints an empty cell", reason);
            r.Skip("the blueprint carries the recorded material", reason);
            r.Skip("an occupied cell is left alone", reason);
            r.Skip("Disable stops the next repetition", reason);
        }

        private static string Summarize(Results r)
        {
            r.sb.AppendLine();
            r.sb.AppendLine($"  {r.passed} passed, {r.failed} failed.");
            return r.sb.ToString();
        }
    }
}
