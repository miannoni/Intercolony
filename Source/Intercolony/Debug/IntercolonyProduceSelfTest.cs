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
                    CheckPauseBehavior(
                        r, map, loops, subject, reservedCells, testRects);
                    CheckDesignatorCancel(r, map, subject, reservedCells, testRects);
                    CheckProduceDesignators(r, map, subject, reservedCells, testRects);
                    CheckBlueprintRotation(r, map, loops, subject, reservedCells, testRects);
                }

                CheckNullDefDrop(r, map, loops, subject, reservedCells, testRects);

                if (subject == null)
                {
                    r.Skip(
                        "a record survives a save/load round trip",
                        "no loaded minifiable stuff-built building");
                    r.Skip(
                        "a paused loop reloads paused, and an old record reloads running",
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
                    "a cell with work under way gains no second blueprint",
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
            CheckSafely(r, "a cell with work under way gains no second blueprint", () =>
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

        private static void CheckPauseBehavior(
            Results r,
            Map map,
            ProduceLoopMapComponent loops,
            Subject subject,
            HashSet<IntVec3> reservedCells,
            List<CellRect> testRects)
        {
            const string pausedPlacementLabel = "a paused loop places no blueprint";
            const string pauseStopLabel = "a paused loop keeps its record, unlike Stop";
            const string resumeLabel = "resuming places a blueprint again";
            const string inFlightLabel = "pausing leaves work already under way alone";

            IntVec3 pausedCell;
            if (!TryFindBuildCell(
                    map, loops, subject, Rot4.North, reservedCells, out pausedCell))
            {
                const string reason = "no empty valid cell for the pause fixture";
                r.Skip(pausedPlacementLabel, reason);
                r.Skip(pauseStopLabel, reason);
                r.Skip(resumeLabel, reason);
                r.Skip(inFlightLabel, reason);
                return;
            }

            RememberCell(
                pausedCell,
                subject.thingDef,
                Rot4.North,
                reservedCells,
                testRects);

            bool pauseScenarioReady = false;
            bool pausedBlueprintPresent = false;
            bool pausedRecordSurvived = false;
            bool resumedBlueprintPresent = false;
            bool resumedRecordSurvived = false;
            try
            {
                CheckSafely(
                    r,
                    pausedPlacementLabel,
                    () =>
                    {
                        loops.Enable(
                            pausedCell,
                            Rot4.North,
                            subject.thingDef,
                            subject.stuffDef,
                            null);
                        loops.Pause(pausedCell);
                        loops.RunPass();
                        pauseScenarioReady = true;
                        pausedBlueprintPresent =
                            FindBlueprint(map, pausedCell, subject.thingDef) != null;
                        pausedRecordSurvived = loops.Find(pausedCell) != null;
                        return !pausedBlueprintPresent && pausedRecordSurvived;
                    },
                    () => $"cell {pausedCell}; blueprint present " +
                    $"{(pausedBlueprintPresent ? "yes" : "no")}; record survived " +
                    $"{(pausedRecordSurvived ? "yes" : "no")}");

                IntVec3 stopCell;
                if (!TryFindBuildCell(
                        map, loops, subject, Rot4.North, reservedCells, out stopCell))
                {
                    r.Skip(
                        pauseStopLabel,
                        "no equivalent empty valid cell for the Stop comparison");
                }
                else
                {
                    RememberCell(
                        stopCell,
                        subject.thingDef,
                        Rot4.North,
                        reservedCells,
                        testRects);
                    try
                    {
                        bool stopBlueprintPresent = false;
                        bool stopRecordSurvived = false;
                        CheckSafely(
                            r,
                            pauseStopLabel,
                            () =>
                            {
                                if (!pauseScenarioReady)
                                {
                                    return false;
                                }

                                loops.Enable(
                                    stopCell,
                                    Rot4.North,
                                    subject.thingDef,
                                    subject.stuffDef,
                                    null);
                                loops.Disable(stopCell);
                                pausedBlueprintPresent =
                                    FindBlueprint(map, pausedCell, subject.thingDef) != null;
                                pausedRecordSurvived = loops.Find(pausedCell) != null;
                                stopBlueprintPresent =
                                    FindBlueprint(map, stopCell, subject.thingDef) != null;
                                stopRecordSurvived = loops.Find(stopCell) != null;
                                return pausedRecordSurvived && !stopRecordSurvived;
                            },
                            () => $"cell {pausedCell}; blueprint present " +
                            $"{(pausedBlueprintPresent ? "yes" : "no")}; record survived " +
                            $"{(pausedRecordSurvived ? "yes" : "no")}; " +
                            $"Stop cell {stopCell}; blueprint present " +
                            $"{(stopBlueprintPresent ? "yes" : "no")}; record survived " +
                            $"{(stopRecordSurvived ? "yes" : "no")}");
                    }
                    finally
                    {
                        try
                        {
                            loops.Disable(stopCell);
                        }
                        finally
                        {
                            DestroyThingsInRect(
                                map,
                                GenAdj.OccupiedRect(
                                    stopCell,
                                    Rot4.North,
                                    subject.thingDef.Size));
                        }
                    }
                }

                CheckSafely(
                    r,
                    resumeLabel,
                    () =>
                    {
                        if (!pauseScenarioReady)
                        {
                            return false;
                        }

                        loops.Resume(pausedCell);
                        loops.RunPass();
                        resumedBlueprintPresent =
                            FindBlueprint(map, pausedCell, subject.thingDef) != null;
                        resumedRecordSurvived = loops.Find(pausedCell) != null;
                        return !pausedBlueprintPresent &&
                               pausedRecordSurvived &&
                               resumedBlueprintPresent &&
                               resumedRecordSurvived;
                    },
                    () => $"cell {pausedCell}; blueprint present while paused " +
                    $"{(pausedBlueprintPresent ? "yes" : "no")}; blueprint present after resume " +
                    $"{(resumedBlueprintPresent ? "yes" : "no")}; record survived while paused " +
                    $"{(pausedRecordSurvived ? "yes" : "no")}; record survived after resume " +
                    $"{(resumedRecordSurvived ? "yes" : "no")}");

                IntVec3 inFlightCell;
                if (!TryFindBuildCell(
                        map, loops, subject, Rot4.North, reservedCells, out inFlightCell))
                {
                    r.Skip(inFlightLabel, "no empty valid cell for the in-flight blueprint fixture");
                }
                else
                {
                    RememberCell(
                        inFlightCell,
                        subject.thingDef,
                        Rot4.North,
                        reservedCells,
                        testRects);
                    try
                    {
                        bool blueprintBeforePausePresent = false;
                        bool blueprintAfterPausePresent = false;
                        bool inFlightRecordSurvived = false;
                        CheckSafely(
                            r,
                            inFlightLabel,
                            () =>
                            {
                                loops.Enable(
                                    inFlightCell,
                                    Rot4.North,
                                    subject.thingDef,
                                    subject.stuffDef,
                                    null);
                                loops.RunPass();
                                blueprintBeforePausePresent =
                                    FindBlueprint(map, inFlightCell, subject.thingDef) != null;
                                if (!blueprintBeforePausePresent)
                                {
                                    return false;
                                }

                                loops.Pause(inFlightCell);
                                loops.RunPass();
                                blueprintAfterPausePresent =
                                    FindBlueprint(map, inFlightCell, subject.thingDef) != null;
                                inFlightRecordSurvived = loops.Find(inFlightCell) != null;
                                return blueprintAfterPausePresent && inFlightRecordSurvived;
                            },
                            () => $"cell {inFlightCell}; blueprint present before pause " +
                            $"{(blueprintBeforePausePresent ? "yes" : "no")}; blueprint present after pause " +
                            $"{(blueprintAfterPausePresent ? "yes" : "no")}; record survived " +
                            $"{(inFlightRecordSurvived ? "yes" : "no")}");
                    }
                    finally
                    {
                        try
                        {
                            loops.Disable(inFlightCell);
                        }
                        finally
                        {
                            DestroyThingsInRect(
                                map,
                                GenAdj.OccupiedRect(
                                    inFlightCell,
                                    Rot4.North,
                                    subject.thingDef.Size));
                        }
                    }
                }
            }
            finally
            {
                try
                {
                    loops.Disable(pausedCell);
                }
                finally
                {
                    DestroyThingsInRect(
                        map,
                        GenAdj.OccupiedRect(
                            pausedCell,
                            Rot4.North,
                            subject.thingDef.Size));
                }
            }
        }

        private static void CheckDesignatorCancel(
            Results r,
            Map map,
            Subject subject,
            HashSet<IntVec3> reservedCells,
            List<CellRect> testRects)
        {
            const string matchingLabel = "vanilla Cancel ends the loop for that cell";
            const string elsewhereLabel = "cancelling elsewhere leaves the loop alone";

            if (Find.CurrentMap != map)
            {
                const string reason =
                    "Designator_Cancel.Map uses Find.CurrentMap, which is not the self-test map";
                r.Skip(matchingLabel, reason);
                r.Skip(elsewhereLabel, reason);
                return;
            }

            // The ordinary self-test fixture is detached. The cancellation prefix resolves the
            // map-owned component, so this check must use that component to exercise the patch.
            ProduceLoopMapComponent loops = ProduceLoopMapComponent.For(map);
            if (loops == null)
            {
                const string reason = "the current map has no ProduceLoopMapComponent";
                r.Skip(matchingLabel, reason);
                r.Skip(elsewhereLabel, reason);
                return;
            }

            bool matchingRecordSurvived = false;
            bool matchingBlueprintAfterPass = false;
            bool elsewhereRecordSurvived = false;
            bool elsewhereBlueprintAfterPass = false;
            IntVec3 matchingCell = IntVec3.Invalid;
            IntVec3 loopCell = IntVec3.Invalid;
            IntVec3 elsewhereCell = IntVec3.Invalid;
            bool matchingCellRemembered = false;
            bool loopCellRemembered = false;
            bool elsewhereCellRemembered = false;

            try
            {
                Designator_Cancel cancel = new Designator_Cancel();

                if (!TryFindBuildCell(
                        map, loops, subject, Rot4.North, reservedCells, out matchingCell))
                {
                    const string reason = "no empty valid cell for the matching-cancel scenario";
                    r.Skip(matchingLabel, reason);
                    r.Skip(elsewhereLabel, reason);
                    return;
                }

                RememberCell(
                    matchingCell,
                    subject.thingDef,
                    Rot4.North,
                    reservedCells,
                    testRects);
                matchingCellRemembered = true;
                try
                {
                    loops.Enable(
                        matchingCell,
                        Rot4.North,
                        subject.thingDef,
                        subject.stuffDef,
                        null);
                    loops.RunPass();
                    Blueprint_Build blueprint = FindBlueprint(
                        map, matchingCell, subject.thingDef);
                    if (blueprint == null)
                    {
                        const string reason =
                            "the matching-cancel scenario did not create the expected blueprint";
                        r.Skip(matchingLabel, reason);
                        r.Skip(elsewhereLabel, reason);
                        return;
                    }

                    cancel.DesignateThing(blueprint);
                    loops.RunPass();
                    matchingRecordSurvived = loops.Find(matchingCell) != null;
                    matchingBlueprintAfterPass =
                        FindBlueprint(map, matchingCell, subject.thingDef) != null;
                }
                finally
                {
                    if (matchingCellRemembered)
                    {
                        try
                        {
                            loops.Disable(matchingCell);
                        }
                        finally
                        {
                            DestroyThingsInRect(map, GenAdj.OccupiedRect(
                                matchingCell, Rot4.North, subject.thingDef.Size));
                        }
                    }
                }

                if (!TryFindBuildCell(
                        map, loops, subject, Rot4.North, reservedCells, out loopCell))
                {
                    const string reason = "no empty valid cell for the loop-preservation scenario";
                    r.Skip(matchingLabel, reason);
                    r.Skip(elsewhereLabel, reason);
                    return;
                }

                RememberCell(
                    loopCell,
                    subject.thingDef,
                    Rot4.North,
                    reservedCells,
                    testRects);
                loopCellRemembered = true;
                try
                {
                    loops.Enable(
                        loopCell,
                        Rot4.North,
                        subject.thingDef,
                        subject.stuffDef,
                        null);
                    loops.RunPass();
                    if (FindBlueprint(map, loopCell, subject.thingDef) == null)
                    {
                        const string reason =
                            "the loop-preservation scenario did not create the loop blueprint";
                        r.Skip(matchingLabel, reason);
                        r.Skip(elsewhereLabel, reason);
                        return;
                    }

                    if (!TryFindBuildCell(
                            map, loops, subject, Rot4.North, reservedCells, out elsewhereCell))
                    {
                        const string reason =
                            "no different empty valid cell for the cancelled blueprint";
                        r.Skip(matchingLabel, reason);
                        r.Skip(elsewhereLabel, reason);
                        return;
                    }

                    RememberCell(
                        elsewhereCell,
                        subject.thingDef,
                        Rot4.North,
                        reservedCells,
                        testRects);
                    elsewhereCellRemembered = true;
                    GenConstruct.PlaceBlueprintForBuild(
                        subject.thingDef,
                        elsewhereCell,
                        map,
                        Rot4.North,
                        Faction.OfPlayer,
                        subject.stuffDef);
                    Blueprint_Build elsewhereBlueprint = FindBlueprint(
                        map, elsewhereCell, subject.thingDef);
                    if (elsewhereBlueprint == null)
                    {
                        const string reason =
                            "the separate cancelled cell did not create the expected blueprint";
                        r.Skip(matchingLabel, reason);
                        r.Skip(elsewhereLabel, reason);
                        return;
                    }

                    cancel.DesignateThing(elsewhereBlueprint);
                    loops.RunPass();
                    elsewhereRecordSurvived = loops.Find(loopCell) != null;
                    elsewhereBlueprintAfterPass =
                        FindBlueprint(map, loopCell, subject.thingDef) != null;
                }
                finally
                {
                    if (loopCellRemembered)
                    {
                        try
                        {
                            loops.Disable(loopCell);
                        }
                        finally
                        {
                            try
                            {
                                DestroyThingsInRect(map, GenAdj.OccupiedRect(
                                    loopCell, Rot4.North, subject.thingDef.Size));
                            }
                            finally
                            {
                                if (elsewhereCellRemembered)
                                {
                                    DestroyThingsInRect(map, GenAdj.OccupiedRect(
                                        elsewhereCell, Rot4.North, subject.thingDef.Size));
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                string reason =
                    $"Designator_Cancel could not be constructed or driven: " +
                    $"{ex.GetType().Name}: {ex.Message}";
                r.Skip(matchingLabel, reason);
                r.Skip(elsewhereLabel, reason);
                return;
            }

            CheckSafely(
                r,
                matchingLabel,
                () => !matchingRecordSurvived && !matchingBlueprintAfterPass,
                $"loop cell {matchingCell}, cancelled cell {matchingCell}, " +
                $"record survived {(matchingRecordSurvived ? "yes" : "no")}, " +
                $"blueprint present after pass " +
                $"{(matchingBlueprintAfterPass ? "yes" : "no")}");
            CheckSafely(
                r,
                elsewhereLabel,
                () => elsewhereRecordSurvived && elsewhereBlueprintAfterPass,
                $"loop cell {loopCell}, cancelled cell {elsewhereCell}, " +
                $"record survived {(elsewhereRecordSurvived ? "yes" : "no")}, " +
                $"blueprint present after pass " +
                $"{(elsewhereBlueprintAfterPass ? "yes" : "no")}");
        }

        private static void CheckProduceDesignators(
            Results r,
            Map map,
            Subject subject,
            HashSet<IntVec3> reservedCells,
            List<CellRect> testRects)
        {
            const string resumeEnableLabel =
                "the area resume command starts a program on an eligible cell";
            const string resumePausedLabel = "the area resume command resumes a paused program";
            const string pauseLabel = "the area pause command pauses a running program";
            const string stopLabel = "the area stop command deletes the record";
            const string refusalLabel = "an area command refuses a cell it cannot act on";
            const string dragLabel = "a drag applies the command to every eligible cell it covers";

            if (Find.CurrentMap != map)
            {
                const string reason =
                    "Produce designators.Map uses Find.CurrentMap, which is not the self-test map";
                r.Skip(resumeEnableLabel, reason);
                r.Skip(resumePausedLabel, reason);
                r.Skip(pauseLabel, reason);
                r.Skip(stopLabel, reason);
                r.Skip(refusalLabel, reason);
                r.Skip(dragLabel, reason);
                return;
            }

            ProduceLoopMapComponent designatorLoops = ProduceLoopMapComponent.For(map);
            if (designatorLoops == null)
            {
                const string reason = "the current map has no ProduceLoopMapComponent";
                r.Skip(resumeEnableLabel, reason);
                r.Skip(resumePausedLabel, reason);
                r.Skip(pauseLabel, reason);
                r.Skip(stopLabel, reason);
                r.Skip(refusalLabel, reason);
                r.Skip(dragLabel, reason);
                return;
            }

            List<IntVec3> fixtureCells = new List<IntVec3>();
            IntVec3 startCell = IntVec3.Invalid;
            IntVec3 pausedResumeCell = IntVec3.Invalid;
            IntVec3 pauseCell = IntVec3.Invalid;
            IntVec3 stopCell = IntVec3.Invalid;
            IntVec3 refusalPauseCell = IntVec3.Invalid;
            IntVec3 refusalResumeCell = IntVec3.Invalid;
            IntVec3 dragFirstCell = IntVec3.Invalid;
            IntVec3 dragSecondCell = IntVec3.Invalid;
            IntVec3 dragIneligibleCell = IntVec3.Invalid;

            bool startRecordBefore = false;
            bool startPausedBefore = false;
            bool startCanDesignate = false;
            bool startRecordAfter = false;
            bool startPausedAfter = false;
            bool startThingDefAfter = false;
            bool startStuffDefAfter = false;

            bool pausedResumeRecordBefore = false;
            bool pausedResumePausedBefore = false;
            bool pausedResumeCanDesignate = false;
            bool pausedResumeRecordAfter = false;
            bool pausedResumePausedAfter = false;

            bool pauseRecordBefore = false;
            bool pausePausedBefore = false;
            bool pauseCanDesignate = false;
            bool pauseRecordAfter = false;
            bool pausePausedAfter = false;

            bool stopRecordBefore = false;
            bool stopPausedBefore = false;
            bool stopCanDesignate = false;
            bool stopRecordAfter = false;
            bool stopPausedAfter = false;

            bool refusalPauseRecordBefore = false;
            bool refusalPausePausedBefore = false;
            bool refusalPauseCanDesignate = false;
            bool refusalPauseRecordAfter = false;
            bool refusalPausePausedAfter = false;
            bool refusalResumeRecordBefore = false;
            bool refusalResumePausedBefore = false;
            bool refusalResumeCanDesignate = false;
            bool refusalResumeRecordAfter = false;
            bool refusalResumePausedAfter = false;

            bool dragFirstRecordBefore = false;
            bool dragFirstPausedBefore = false;
            bool dragFirstCanDesignate = false;
            bool dragFirstRecordAfter = false;
            bool dragFirstPausedAfter = false;
            bool dragSecondRecordBefore = false;
            bool dragSecondPausedBefore = false;
            bool dragSecondCanDesignate = false;
            bool dragSecondRecordAfter = false;
            bool dragSecondPausedAfter = false;
            ProduceLoopRecord dragIneligibleRecordBefore = null;
            bool dragIneligibleRecordBeforeExists = false;
            bool dragIneligiblePausedBefore = false;
            bool dragIneligibleCanDesignate = false;
            ProduceLoopRecord dragIneligibleRecordAfter = null;
            bool dragIneligibleRecordAfterExists = false;
            bool dragIneligiblePausedAfter = false;

            try
            {
                Designator_ProduceResume resumeDesignator = new Designator_ProduceResume();
                Designator_ProducePause pauseDesignator = new Designator_ProducePause();
                Designator_ProduceStop stopDesignator = new Designator_ProduceStop();

                if (!TryFindBuildCell(
                        map,
                        designatorLoops,
                        subject,
                        Rot4.North,
                        reservedCells,
                        out startCell))
                {
                    const string reason = "not enough empty valid cells for the area-designator fixtures";
                    r.Skip(resumeEnableLabel, reason);
                    r.Skip(resumePausedLabel, reason);
                    r.Skip(pauseLabel, reason);
                    r.Skip(stopLabel, reason);
                    r.Skip(refusalLabel, reason);
                    r.Skip(dragLabel, reason);
                    return;
                }

                fixtureCells.Add(startCell);
                RememberCell(
                    startCell,
                    subject.thingDef,
                    Rot4.North,
                    reservedCells,
                    testRects);
                GenConstruct.PlaceBlueprintForBuild(
                    subject.thingDef,
                    startCell,
                    map,
                    Rot4.North,
                    Faction.OfPlayer,
                    subject.stuffDef);
                if (FindBlueprint(map, startCell, subject.thingDef) == null)
                {
                    const string reason =
                        "the eligible-cell fixture did not create the expected blueprint";
                    r.Skip(resumeEnableLabel, reason);
                    r.Skip(resumePausedLabel, reason);
                    r.Skip(pauseLabel, reason);
                    r.Skip(stopLabel, reason);
                    r.Skip(refusalLabel, reason);
                    r.Skip(dragLabel, reason);
                    return;
                }

                if (!TryFindBuildCell(
                        map,
                        designatorLoops,
                        subject,
                        Rot4.North,
                        reservedCells,
                        out pausedResumeCell))
                {
                    const string reason = "not enough empty valid cells for the area-designator fixtures";
                    r.Skip(resumeEnableLabel, reason);
                    r.Skip(resumePausedLabel, reason);
                    r.Skip(pauseLabel, reason);
                    r.Skip(stopLabel, reason);
                    r.Skip(refusalLabel, reason);
                    r.Skip(dragLabel, reason);
                    return;
                }

                fixtureCells.Add(pausedResumeCell);
                RememberCell(
                    pausedResumeCell,
                    subject.thingDef,
                    Rot4.North,
                    reservedCells,
                    testRects);

                if (!TryFindBuildCell(
                        map,
                        designatorLoops,
                        subject,
                        Rot4.North,
                        reservedCells,
                        out pauseCell))
                {
                    const string reason = "not enough empty valid cells for the area-designator fixtures";
                    r.Skip(resumeEnableLabel, reason);
                    r.Skip(resumePausedLabel, reason);
                    r.Skip(pauseLabel, reason);
                    r.Skip(stopLabel, reason);
                    r.Skip(refusalLabel, reason);
                    r.Skip(dragLabel, reason);
                    return;
                }

                fixtureCells.Add(pauseCell);
                RememberCell(
                    pauseCell,
                    subject.thingDef,
                    Rot4.North,
                    reservedCells,
                    testRects);

                if (!TryFindBuildCell(
                        map,
                        designatorLoops,
                        subject,
                        Rot4.North,
                        reservedCells,
                        out stopCell))
                {
                    const string reason = "not enough empty valid cells for the area-designator fixtures";
                    r.Skip(resumeEnableLabel, reason);
                    r.Skip(resumePausedLabel, reason);
                    r.Skip(pauseLabel, reason);
                    r.Skip(stopLabel, reason);
                    r.Skip(refusalLabel, reason);
                    r.Skip(dragLabel, reason);
                    return;
                }

                fixtureCells.Add(stopCell);
                RememberCell(
                    stopCell,
                    subject.thingDef,
                    Rot4.North,
                    reservedCells,
                    testRects);

                if (!TryFindBuildCell(
                        map,
                        designatorLoops,
                        subject,
                        Rot4.North,
                        reservedCells,
                        out refusalPauseCell))
                {
                    const string reason = "not enough empty valid cells for the area-designator fixtures";
                    r.Skip(resumeEnableLabel, reason);
                    r.Skip(resumePausedLabel, reason);
                    r.Skip(pauseLabel, reason);
                    r.Skip(stopLabel, reason);
                    r.Skip(refusalLabel, reason);
                    r.Skip(dragLabel, reason);
                    return;
                }

                fixtureCells.Add(refusalPauseCell);
                RememberCell(
                    refusalPauseCell,
                    subject.thingDef,
                    Rot4.North,
                    reservedCells,
                    testRects);

                if (!TryFindBuildCell(
                        map,
                        designatorLoops,
                        subject,
                        Rot4.North,
                        reservedCells,
                        out refusalResumeCell))
                {
                    const string reason = "not enough empty valid cells for the area-designator fixtures";
                    r.Skip(resumeEnableLabel, reason);
                    r.Skip(resumePausedLabel, reason);
                    r.Skip(pauseLabel, reason);
                    r.Skip(stopLabel, reason);
                    r.Skip(refusalLabel, reason);
                    r.Skip(dragLabel, reason);
                    return;
                }

                fixtureCells.Add(refusalResumeCell);
                RememberCell(
                    refusalResumeCell,
                    subject.thingDef,
                    Rot4.North,
                    reservedCells,
                    testRects);

                if (!TryFindBuildCell(
                        map,
                        designatorLoops,
                        subject,
                        Rot4.North,
                        reservedCells,
                        out dragFirstCell))
                {
                    const string reason = "not enough empty valid cells for the area-designator fixtures";
                    r.Skip(resumeEnableLabel, reason);
                    r.Skip(resumePausedLabel, reason);
                    r.Skip(pauseLabel, reason);
                    r.Skip(stopLabel, reason);
                    r.Skip(refusalLabel, reason);
                    r.Skip(dragLabel, reason);
                    return;
                }

                fixtureCells.Add(dragFirstCell);
                RememberCell(
                    dragFirstCell,
                    subject.thingDef,
                    Rot4.North,
                    reservedCells,
                    testRects);

                if (!TryFindBuildCell(
                        map,
                        designatorLoops,
                        subject,
                        Rot4.North,
                        reservedCells,
                        out dragSecondCell))
                {
                    const string reason = "not enough empty valid cells for the area-designator fixtures";
                    r.Skip(resumeEnableLabel, reason);
                    r.Skip(resumePausedLabel, reason);
                    r.Skip(pauseLabel, reason);
                    r.Skip(stopLabel, reason);
                    r.Skip(refusalLabel, reason);
                    r.Skip(dragLabel, reason);
                    return;
                }

                fixtureCells.Add(dragSecondCell);
                RememberCell(
                    dragSecondCell,
                    subject.thingDef,
                    Rot4.North,
                    reservedCells,
                    testRects);

                if (!TryFindBuildCell(
                        map,
                        designatorLoops,
                        subject,
                        Rot4.North,
                        reservedCells,
                        out dragIneligibleCell))
                {
                    const string reason = "not enough empty valid cells for the area-designator fixtures";
                    r.Skip(resumeEnableLabel, reason);
                    r.Skip(resumePausedLabel, reason);
                    r.Skip(pauseLabel, reason);
                    r.Skip(stopLabel, reason);
                    r.Skip(refusalLabel, reason);
                    r.Skip(dragLabel, reason);
                    return;
                }

                fixtureCells.Add(dragIneligibleCell);
                RememberCell(
                    dragIneligibleCell,
                    subject.thingDef,
                    Rot4.North,
                    reservedCells,
                    testRects);

                designatorLoops.Enable(
                    pausedResumeCell,
                    Rot4.North,
                    subject.thingDef,
                    subject.stuffDef,
                    null);
                designatorLoops.Pause(pausedResumeCell);
                designatorLoops.Enable(
                    pauseCell,
                    Rot4.North,
                    subject.thingDef,
                    subject.stuffDef,
                    null);
                designatorLoops.Enable(
                    stopCell,
                    Rot4.North,
                    subject.thingDef,
                    subject.stuffDef,
                    null);
                designatorLoops.Enable(
                    refusalResumeCell,
                    Rot4.North,
                    subject.thingDef,
                    subject.stuffDef,
                    null);
                designatorLoops.Enable(
                    dragFirstCell,
                    Rot4.North,
                    subject.thingDef,
                    subject.stuffDef,
                    null);
                designatorLoops.Enable(
                    dragSecondCell,
                    Rot4.North,
                    subject.thingDef,
                    subject.stuffDef,
                    null);
                designatorLoops.Enable(
                    dragIneligibleCell,
                    Rot4.North,
                    subject.thingDef,
                    subject.stuffDef,
                    null);
                designatorLoops.Pause(dragIneligibleCell);

                CheckSafely(
                    r,
                    resumeEnableLabel,
                    () =>
                    {
                        ProduceLoopRecord before = designatorLoops.Find(startCell);
                        startRecordBefore = before != null;
                        startPausedBefore = before != null && before.paused;
                        startCanDesignate = resumeDesignator.CanDesignateCell(startCell).Accepted;
                        resumeDesignator.DesignateSingleCell(startCell);
                        ProduceLoopRecord after = designatorLoops.Find(startCell);
                        startRecordAfter = after != null;
                        startPausedAfter = after != null && after.paused;
                        startThingDefAfter = after != null && after.thingDef == subject.thingDef;
                        startStuffDefAfter = after != null && after.stuffDef == subject.stuffDef;
                        return !startRecordBefore &&
                               startCanDesignate &&
                               startRecordAfter &&
                               !startPausedAfter &&
                               startThingDefAfter &&
                               startStuffDefAfter;
                    },
                    () => $"cell {startCell}; record before {(startRecordBefore ? "yes" : "no")}, " +
                    $"paused before {(startRecordBefore ? (startPausedBefore ? "yes" : "no") : "n/a")}; " +
                    $"CanDesignateCell {(startCanDesignate ? "accepted" : "rejected")}; " +
                    $"record after {(startRecordAfter ? "yes" : "no")}, " +
                    $"paused after {(startRecordAfter ? (startPausedAfter ? "yes" : "no") : "n/a")}; " +
                    $"def recorded {(startThingDefAfter ? "yes" : "no")}, " +
                    $"stuff recorded {(startStuffDefAfter ? "yes" : "no")}");

                CheckSafely(
                    r,
                    resumePausedLabel,
                    () =>
                    {
                        ProduceLoopRecord before = designatorLoops.Find(pausedResumeCell);
                        pausedResumeRecordBefore = before != null;
                        pausedResumePausedBefore = before != null && before.paused;
                        pausedResumeCanDesignate =
                            resumeDesignator.CanDesignateCell(pausedResumeCell).Accepted;
                        resumeDesignator.DesignateSingleCell(pausedResumeCell);
                        ProduceLoopRecord after = designatorLoops.Find(pausedResumeCell);
                        pausedResumeRecordAfter = after != null;
                        pausedResumePausedAfter = after != null && after.paused;
                        return pausedResumeRecordBefore &&
                               pausedResumePausedBefore &&
                               pausedResumeCanDesignate &&
                               pausedResumeRecordAfter &&
                               !pausedResumePausedAfter;
                    },
                    () => $"cell {pausedResumeCell}; record before " +
                    $"{(pausedResumeRecordBefore ? "yes" : "no")}, paused before " +
                    $"{(pausedResumeRecordBefore ? (pausedResumePausedBefore ? "yes" : "no") : "n/a")}; " +
                    $"CanDesignateCell {(pausedResumeCanDesignate ? "accepted" : "rejected")}; " +
                    $"record after {(pausedResumeRecordAfter ? "yes" : "no")}, paused after " +
                    $"{(pausedResumeRecordAfter ? (pausedResumePausedAfter ? "yes" : "no") : "n/a")}");

                CheckSafely(
                    r,
                    pauseLabel,
                    () =>
                    {
                        ProduceLoopRecord before = designatorLoops.Find(pauseCell);
                        pauseRecordBefore = before != null;
                        pausePausedBefore = before != null && before.paused;
                        pauseCanDesignate = pauseDesignator.CanDesignateCell(pauseCell).Accepted;
                        pauseDesignator.DesignateSingleCell(pauseCell);
                        ProduceLoopRecord after = designatorLoops.Find(pauseCell);
                        pauseRecordAfter = after != null;
                        pausePausedAfter = after != null && after.paused;
                        return pauseRecordBefore &&
                               !pausePausedBefore &&
                               pauseCanDesignate &&
                               pauseRecordAfter &&
                               pausePausedAfter;
                    },
                    () => $"cell {pauseCell}; record before {(pauseRecordBefore ? "yes" : "no")}, " +
                    $"paused before {(pauseRecordBefore ? (pausePausedBefore ? "yes" : "no") : "n/a")}; " +
                    $"CanDesignateCell {(pauseCanDesignate ? "accepted" : "rejected")}; " +
                    $"record after {(pauseRecordAfter ? "yes" : "no")}, paused after " +
                    $"{(pauseRecordAfter ? (pausePausedAfter ? "yes" : "no") : "n/a")}");

                CheckSafely(
                    r,
                    stopLabel,
                    () =>
                    {
                        ProduceLoopRecord before = designatorLoops.Find(stopCell);
                        stopRecordBefore = before != null;
                        stopPausedBefore = before != null && before.paused;
                        stopCanDesignate = stopDesignator.CanDesignateCell(stopCell).Accepted;
                        stopDesignator.DesignateSingleCell(stopCell);
                        ProduceLoopRecord after = designatorLoops.Find(stopCell);
                        stopRecordAfter = after != null;
                        stopPausedAfter = after != null && after.paused;
                        return stopRecordBefore &&
                               !stopPausedBefore &&
                               stopCanDesignate &&
                               !stopRecordAfter;
                    },
                    () => $"cell {stopCell}; record before {(stopRecordBefore ? "yes" : "no")}, " +
                    $"paused before {(stopRecordBefore ? (stopPausedBefore ? "yes" : "no") : "n/a")}; " +
                    $"CanDesignateCell {(stopCanDesignate ? "accepted" : "rejected")}; " +
                    $"record after {(stopRecordAfter ? "yes" : "no")}, paused after " +
                    $"{(stopRecordAfter ? (stopPausedAfter ? "yes" : "no") : "n/a")}");

                CheckSafely(
                    r,
                    refusalLabel,
                    () =>
                    {
                        ProduceLoopRecord pauseBefore = designatorLoops.Find(refusalPauseCell);
                        refusalPauseRecordBefore = pauseBefore != null;
                        refusalPausePausedBefore = pauseBefore != null && pauseBefore.paused;
                        refusalPauseCanDesignate =
                            pauseDesignator.CanDesignateCell(refusalPauseCell).Accepted;
                        ProduceLoopRecord resumeBefore = designatorLoops.Find(refusalResumeCell);
                        refusalResumeRecordBefore = resumeBefore != null;
                        refusalResumePausedBefore = resumeBefore != null && resumeBefore.paused;
                        refusalResumeCanDesignate =
                            resumeDesignator.CanDesignateCell(refusalResumeCell).Accepted;
                        ProduceLoopRecord pauseAfter = designatorLoops.Find(refusalPauseCell);
                        refusalPauseRecordAfter = pauseAfter != null;
                        refusalPausePausedAfter = pauseAfter != null && pauseAfter.paused;
                        ProduceLoopRecord resumeAfter = designatorLoops.Find(refusalResumeCell);
                        refusalResumeRecordAfter = resumeAfter != null;
                        refusalResumePausedAfter = resumeAfter != null && resumeAfter.paused;
                        return !refusalPauseRecordBefore &&
                               !refusalPauseCanDesignate &&
                               refusalResumeRecordBefore &&
                               !refusalResumePausedBefore &&
                               !refusalResumeCanDesignate &&
                               !refusalPauseRecordAfter &&
                               refusalResumeRecordAfter &&
                               !refusalResumePausedAfter;
                    },
                    () => $"Pause cell {refusalPauseCell}; record before " +
                    $"{(refusalPauseRecordBefore ? "yes" : "no")}, paused before " +
                    $"{(refusalPauseRecordBefore ? (refusalPausePausedBefore ? "yes" : "no") : "n/a")}; " +
                    $"CanDesignateCell {(refusalPauseCanDesignate ? "accepted" : "rejected")}, " +
                    $"record after {(refusalPauseRecordAfter ? "yes" : "no")}, paused after " +
                    $"{(refusalPauseRecordAfter ? (refusalPausePausedAfter ? "yes" : "no") : "n/a")}; " +
                    $"Resume cell {refusalResumeCell}; record before " +
                    $"{(refusalResumeRecordBefore ? "yes" : "no")}, paused before " +
                    $"{(refusalResumeRecordBefore ? (refusalResumePausedBefore ? "yes" : "no") : "n/a")}; " +
                    $"CanDesignateCell {(refusalResumeCanDesignate ? "accepted" : "rejected")}, " +
                    $"record after {(refusalResumeRecordAfter ? "yes" : "no")}, paused after " +
                    $"{(refusalResumeRecordAfter ? (refusalResumePausedAfter ? "yes" : "no") : "n/a")}");

                CheckSafely(
                    r,
                    dragLabel,
                    () =>
                    {
                        ProduceLoopRecord firstBefore = designatorLoops.Find(dragFirstCell);
                        dragFirstRecordBefore = firstBefore != null;
                        dragFirstPausedBefore = firstBefore != null && firstBefore.paused;
                        dragFirstCanDesignate = pauseDesignator.CanDesignateCell(dragFirstCell).Accepted;
                        ProduceLoopRecord secondBefore = designatorLoops.Find(dragSecondCell);
                        dragSecondRecordBefore = secondBefore != null;
                        dragSecondPausedBefore = secondBefore != null && secondBefore.paused;
                        dragSecondCanDesignate =
                            pauseDesignator.CanDesignateCell(dragSecondCell).Accepted;
                        dragIneligibleRecordBefore = designatorLoops.Find(dragIneligibleCell);
                        dragIneligibleRecordBeforeExists = dragIneligibleRecordBefore != null;
                        dragIneligiblePausedBefore =
                            dragIneligibleRecordBefore != null && dragIneligibleRecordBefore.paused;
                        dragIneligibleCanDesignate =
                            pauseDesignator.CanDesignateCell(dragIneligibleCell).Accepted;

                        pauseDesignator.DesignateMultiCell(new List<IntVec3>
                        {
                            dragFirstCell,
                            dragSecondCell,
                            dragIneligibleCell
                        });

                        ProduceLoopRecord firstAfter = designatorLoops.Find(dragFirstCell);
                        dragFirstRecordAfter = firstAfter != null;
                        dragFirstPausedAfter = firstAfter != null && firstAfter.paused;
                        ProduceLoopRecord secondAfter = designatorLoops.Find(dragSecondCell);
                        dragSecondRecordAfter = secondAfter != null;
                        dragSecondPausedAfter = secondAfter != null && secondAfter.paused;
                        dragIneligibleRecordAfter = designatorLoops.Find(dragIneligibleCell);
                        dragIneligibleRecordAfterExists = dragIneligibleRecordAfter != null;
                        dragIneligiblePausedAfter =
                            dragIneligibleRecordAfter != null && dragIneligibleRecordAfter.paused;

                        return dragFirstRecordBefore &&
                               !dragFirstPausedBefore &&
                               dragFirstCanDesignate &&
                               dragFirstRecordAfter &&
                               dragFirstPausedAfter &&
                               dragSecondRecordBefore &&
                               !dragSecondPausedBefore &&
                               dragSecondCanDesignate &&
                               dragSecondRecordAfter &&
                               dragSecondPausedAfter &&
                               dragIneligibleRecordBeforeExists &&
                               dragIneligiblePausedBefore &&
                               !dragIneligibleCanDesignate &&
                               dragIneligibleRecordAfterExists &&
                               dragIneligibleRecordAfter == dragIneligibleRecordBefore &&
                               dragIneligiblePausedAfter == dragIneligiblePausedBefore;
                    },
                    () => $"Pause drag; eligible cell {dragFirstCell}; record before " +
                    $"{(dragFirstRecordBefore ? "yes" : "no")}, paused before " +
                    $"{(dragFirstRecordBefore ? (dragFirstPausedBefore ? "yes" : "no") : "n/a")}; " +
                    $"CanDesignateCell {(dragFirstCanDesignate ? "accepted" : "rejected")}; " +
                    $"record after {(dragFirstRecordAfter ? "yes" : "no")}, paused after " +
                    $"{(dragFirstRecordAfter ? (dragFirstPausedAfter ? "yes" : "no") : "n/a")}; " +
                    $"eligible cell {dragSecondCell}; record before " +
                    $"{(dragSecondRecordBefore ? "yes" : "no")}, paused before " +
                    $"{(dragSecondRecordBefore ? (dragSecondPausedBefore ? "yes" : "no") : "n/a")}; " +
                    $"CanDesignateCell {(dragSecondCanDesignate ? "accepted" : "rejected")}; " +
                    $"record after {(dragSecondRecordAfter ? "yes" : "no")}, paused after " +
                    $"{(dragSecondRecordAfter ? (dragSecondPausedAfter ? "yes" : "no") : "n/a")}; " +
                    $"ineligible cell {dragIneligibleCell}; record before " +
                    $"{(dragIneligibleRecordBeforeExists ? "yes" : "no")}, paused before " +
                    $"{(dragIneligibleRecordBeforeExists ? (dragIneligiblePausedBefore ? "yes" : "no") : "n/a")}; " +
                    $"CanDesignateCell {(dragIneligibleCanDesignate ? "accepted" : "rejected")}; " +
                    $"record after {(dragIneligibleRecordAfterExists ? "yes" : "no")}, paused after " +
                    $"{(dragIneligibleRecordAfterExists ? (dragIneligiblePausedAfter ? "yes" : "no") : "n/a")}");
            }
            catch (Exception ex)
            {
                string reason =
                    $"Produce area designators could not be constructed or driven: " +
                    $"{ex.GetType().Name}: {ex.Message}";
                r.Skip(resumeEnableLabel, reason);
                r.Skip(resumePausedLabel, reason);
                r.Skip(pauseLabel, reason);
                r.Skip(stopLabel, reason);
                r.Skip(refusalLabel, reason);
                r.Skip(dragLabel, reason);
            }
            finally
            {
                for (int i = fixtureCells.Count - 1; i >= 0; i--)
                {
                    IntVec3 cell = fixtureCells[i];
                    try
                    {
                        designatorLoops.Disable(cell);
                    }
                    catch (Exception ex)
                    {
                        r.sb.AppendLine($"  CLEANUP EXCEPTION: {ex}");
                        r.failed++;
                    }

                    try
                    {
                        DestroyThingsInRect(
                            map,
                            GenAdj.OccupiedRect(cell, Rot4.North, subject.thingDef.Size));
                    }
                    catch (Exception ex)
                    {
                        r.sb.AppendLine($"  CLEANUP EXCEPTION: {ex}");
                        r.failed++;
                    }
                }
            }
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
            ProduceLoopMapComponent pausedSaved = new ProduceLoopMapComponent(map);
            pausedSaved.Enable(
                expectedCell,
                expectedRotation,
                subject.thingDef,
                subject.stuffDef,
                null);
            pausedSaved.Pause(expectedCell);

            ProduceLoopMapComponent loaded = null;
            ProduceLoopMapComponent pausedLoaded = null;
            string failure = null;
            string pausedFailure = null;
            bool recordFound = false;
            bool pausedRecordFound = false;
            bool pausedLoadedPaused = false;
            bool pausedXmlHasPausedNode = false;
            bool oldXmlHasPausedNode = false;
            bool loadedPaused = false;
            IntVec3 loadedCell = default(IntVec3);
            Rot4 loadedRotation = default(Rot4);
            ThingDef loadedThingDef = null;
            ThingDef loadedStuffDef = null;
            string path = Path.Combine(
                Path.GetTempPath(), $"Intercolony-ProduceLoop-{Guid.NewGuid():N}.xml");

            try
            {
                Scribe.saver.InitSaving(path, "intercolonyProduceLoopPausedTest");
                Scribe_Deep.Look(ref pausedSaved, "produceLoopMapComponent");
                Scribe.saver.FinalizeSaving();
                pausedXmlHasPausedNode =
                    File.ReadAllText(path).IndexOf("<paused", StringComparison.Ordinal) >= 0;

                Scribe.loader.InitLoading(path);
                Scribe_Deep.Look(ref pausedLoaded, "produceLoopMapComponent", map);
                Scribe.loader.FinalizeLoading();

                ProduceLoopRecord pausedRecord = pausedLoaded?.Find(expectedCell);
                pausedRecordFound = pausedRecord != null;
                if (pausedRecordFound)
                {
                    pausedLoadedPaused = pausedRecord.paused;
                }
            }
            catch (Exception ex)
            {
                pausedFailure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                Scribe.ForceStop();
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            try
            {
                Scribe.saver.InitSaving(path, "intercolonyProduceLoopTest");
                Scribe_Deep.Look(ref saved, "produceLoopMapComponent");
                Scribe.saver.FinalizeSaving();
                oldXmlHasPausedNode =
                    File.ReadAllText(path).IndexOf("<paused", StringComparison.Ordinal) >= 0;

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
                    loadedPaused = record.paused;
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
                pausedSaved?.Disable(expectedCell);
                loaded?.Disable(expectedCell);
                pausedLoaded?.Disable(expectedCell);
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

            bool pausePersistenceOk = pausedFailure == null &&
                                      failure == null &&
                                      pausedRecordFound &&
                                      pausedLoadedPaused &&
                                      pausedXmlHasPausedNode &&
                                      recordFound &&
                                      !oldXmlHasPausedNode &&
                                      !loadedPaused;
            string pausePersistenceDetail =
                $"cell {expectedCell}; blueprint present paused=no, old=no; " +
                $"record survived paused {(pausedRecordFound ? "yes" : "no")}, " +
                $"old {(recordFound ? "yes" : "no")}; " +
                $"paused save loaded paused {(pausedLoadedPaused ? "yes" : "no")}; " +
                $"paused XML paused node present " +
                $"{(pausedXmlHasPausedNode ? "yes" : "no")}; " +
                $"old XML paused node present {(oldXmlHasPausedNode ? "yes" : "no")}; " +
                $"old save loaded paused {(loadedPaused ? "yes" : "no")}" +
                $"{(pausedFailure == null ? "" : $"; paused failure {pausedFailure}")}" +
                $"{(failure == null ? "" : $"; old-save failure {failure}")}";
            r.Check(
                pausePersistenceOk,
                "a paused loop reloads paused, and an old record reloads running",
                pausePersistenceDetail);
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

        private static void CheckSafely(
            Results r,
            string label,
            Func<bool> assertion,
            Func<string> detailFactory)
        {
            try
            {
                r.Check(assertion(), label, detailFactory == null ? null : detailFactory());
            }
            catch (Exception ex)
            {
                string detail = $"{ex.GetType().Name}: {ex.Message}";
                if (detailFactory != null)
                {
                    try
                    {
                        detail = $"{detailFactory()}; {detail}";
                    }
                    catch (Exception detailException)
                    {
                        detail = $"{detail}; detail exception " +
                                 $"{detailException.GetType().Name}: {detailException.Message}";
                    }
                }

                r.Check(false, label, detail);
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
            r.Skip("a cell with work under way gains no second blueprint", reason);
            r.Skip("Disable stops the next repetition", reason);
            r.Skip("Disable does not cancel work under way", reason);
            r.Skip("a paused loop places no blueprint", reason);
            r.Skip("a paused loop keeps its record, unlike Stop", reason);
            r.Skip("resuming places a blueprint again", reason);
            r.Skip("pausing leaves work already under way alone", reason);
            r.Skip("vanilla Cancel ends the loop for that cell", reason);
            r.Skip("cancelling elsewhere leaves the loop alone", reason);
            r.Skip(
                "the area resume command starts a program on an eligible cell",
                reason);
            r.Skip("the area resume command resumes a paused program", reason);
            r.Skip("the area pause command pauses a running program", reason);
            r.Skip("the area stop command deletes the record", reason);
            r.Skip("an area command refuses a cell it cannot act on", reason);
            r.Skip("a drag applies the command to every eligible cell it covers", reason);
            r.Skip(
                "a paused loop reloads paused, and an old record reloads running",
                reason);
        }

        private static void SkipBlueprintAssertions(Results r, string reason)
        {
            r.Skip("re-blueprints an empty cell", reason);
            r.Skip("the blueprint carries the recorded material", reason);
            r.Skip("a cell with work under way gains no second blueprint", reason);
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
