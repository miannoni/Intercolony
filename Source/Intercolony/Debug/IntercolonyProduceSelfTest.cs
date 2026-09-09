using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
            // A printed skip is not proof; count it so the aggregator cannot turn it into a pass.
            public int skipped;

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
                skipped++;
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

        private sealed class CraftedSubject
        {
            public RecipeDef recipe;
            public ThingDef productDef;
            public ThingDef stuffDef;
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
            List<Zone_Stockpile> testZones = new List<Zone_Stockpile>();
            List<Designation> addedDesignations = new List<Designation>();

            try
            {
                CheckProductionLedger(r, state);
                CheckBillDonePatch(r, state, map);
                CheckProductionLedgerMigration(r, state);
                CheckProductionCapture(
                    r, state, map, loops, reservedCells, testRects, addedDesignations);

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
                    CheckPauseStopUninstallBehavior(
                        r, map, loops, subject, reservedCells, testRects, addedDesignations);
                    CheckTargetBehavior(
                        r, map, loops, subject, reservedCells, testRects, testZones);
                    CheckDesignatorCancel(r, map, subject, reservedCells, testRects);
                    CheckProduceDesignators(r, map, subject, reservedCells, testRects);
                    CheckBlueprintRotation(r, map, loops, subject, reservedCells, testRects);
                }

                CheckNullDefDrop(r, map, loops, subject, reservedCells, testRects);

                if (subject == null)
                {
                    SkipTargetAssertions(r, "no loaded minifiable stuff-built building");
                    r.Skip(
                        "a record survives a save/load round trip",
                        "no loaded minifiable stuff-built building");
                    r.Skip(
                        "a paused loop reloads paused, and an old record reloads running",
                        "no loaded minifiable stuff-built building");
                    r.Skip(
                        "a target survives a save and a load, and an old record loads unlimited",
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
                CleanupZones(testZones, r);
                ClearLoops(loops, r);
            }

            return Summarize(r);
        }

        private static void CheckProductionLedger(Results r, IntercolonyWorldComponent state)
        {
            const string recordingAssertion =
                "a production record reads back over the rolling window";
            const string bucketAssertion =
                "same-day production records share one bucket";
            const string pruningAssertion =
                "production pruning keeps the oldest in-window day and drops the newest out-of-window day";

            List<ProductionBucket> buckets = state.ProductionLedger;
            ThingDef product = ThingDefOf.Steel;
            if (buckets == null || product == null)
            {
                string reason = buckets == null
                    ? "the world production ledger is unavailable"
                    : "vanilla ThingDefOf.Steel is unavailable";
                r.Skip(recordingAssertion, reason);
                r.Skip(bucketAssertion, reason);
                r.Skip(pruningAssertion, reason);
                return;
            }

            List<ProductionBucket> savedBuckets = new List<ProductionBucket>(buckets);
            try
            {
                buckets.Clear();

                const int recordedQuantity = 20;
                // Independently known: 20 units over the five-day WindowDays contract is 4/day.
                const float expectedPerDay = 4f;
                ProductionLedgerService.Record(state, product, recordedQuantity);
                float reportedPerDay = ProductionLedgerService.CompletedPerDay(state, product);
                r.Check(
                    reportedPerDay == expectedPerDay,
                    recordingAssertion,
                    $"recorded {recordedQuantity} units; reported {reportedPerDay:0.###} per day, " +
                    $"expected {expectedPerDay:0.###}");

                buckets.Clear();

                const int firstRecording = 7;
                const int secondRecording = 11;
                int currentDay = GenDate.DaysPassedAt(GenTicks.TicksGame);
                ProductionLedgerService.Record(state, product, firstRecording);
                ProductionLedgerService.Record(state, product, secondRecording);
                int sameDayTotal = 0;
                for (int i = 0; i < buckets.Count; i++)
                {
                    ProductionBucket bucket = buckets[i];
                    if (bucket != null && bucket.thingDef == product && bucket.day == currentDay)
                    {
                        sameDayTotal += bucket.count;
                    }
                }

                bool oneBucketWithBothRecordings = buckets.Count == 1 &&
                    buckets[0] != null &&
                    buckets[0].thingDef == product &&
                    buckets[0].day == currentDay &&
                    buckets[0].count == firstRecording + secondRecording;
                r.Check(
                    oneBucketWithBothRecordings,
                    bucketAssertion,
                    $"{buckets.Count} bucket(s) on day {currentDay}; " +
                    $"{sameDayTotal} units, " +
                    $"expected {firstRecording + secondRecording}");

                buckets.Clear();

                int today = GenDate.DaysPassedAt(GenTicks.TicksGame);
                int oldestSurvivingDay = today - (ProductionLedgerService.WindowDays - 1);
                int newestOutsideDay = today - ProductionLedgerService.WindowDays;
                int olderOutsideDay = newestOutsideDay - 1;
                const int currentCount = 5;
                const int oldestCount = 7;
                const int newestOutsideCount = 11;
                const int olderOutsideCount = 13;
                buckets.Add(new ProductionBucket
                {
                    thingDef = product,
                    day = today,
                    count = currentCount
                });
                buckets.Add(new ProductionBucket
                {
                    thingDef = product,
                    day = oldestSurvivingDay,
                    count = oldestCount
                });
                buckets.Add(new ProductionBucket
                {
                    thingDef = product,
                    day = newestOutsideDay,
                    count = newestOutsideCount
                });
                buckets.Add(new ProductionBucket
                {
                    thingDef = product,
                    day = olderOutsideDay,
                    count = olderOutsideCount
                });

                int removed = ProductionLedgerService.Prune(state);
                bool currentSurvived = HasProductionBucket(buckets, product, today, currentCount);
                bool oldestSurvived = HasProductionBucket(
                    buckets, product, oldestSurvivingDay, oldestCount);
                bool newestOutsideDropped = !HasProductionBucket(
                    buckets, product, newestOutsideDay, newestOutsideCount);
                bool olderOutsideDropped = !HasProductionBucket(
                    buckets, product, olderOutsideDay, olderOutsideCount);
                r.Check(
                    removed == 2 && buckets.Count == 2 && currentSurvived && oldestSurvived &&
                    newestOutsideDropped && olderOutsideDropped,
                    pruningAssertion,
                    $"day {today} kept; oldest surviving day {oldestSurvivingDay} kept; " +
                    $"newest outside day {newestOutsideDay} dropped; older outside day " +
                    $"{olderOutsideDay} dropped; removed {removed}, left {buckets.Count}");
            }
            finally
            {
                buckets.Clear();
                buckets.AddRange(savedBuckets);
                r.Info($"production ledger restored to {buckets.Count} bucket(s).");
            }
        }

        private static void CheckBillDonePatch(
            Results r, IntercolonyWorldComponent state, Map map)
        {
            const string playerAssertion =
                "Notify_BillDone records exactly the products made by a player-faction pawn";
            const string nonPlayerAssertion =
                "Notify_BillDone records nothing for a non-player-faction pawn";
            const int firstProductCount = 7;
            const int secondProductCount = 11;

            List<ProductionBucket> buckets = state?.ProductionLedger;
            ThingDef firstProductDef = ThingDefOf.Steel;
            ThingDef secondProductDef = ThingDefOf.WoodLog;
            if (buckets == null || firstProductDef == null || secondProductDef == null)
            {
                string reason = buckets == null
                    ? "the world production ledger is unavailable"
                    : "vanilla Steel or WoodLog is unavailable";
                r.Skip(playerAssertion, reason);
                r.Skip(nonPlayerAssertion, reason);
                return;
            }

            Pawn playerPawn = FindExistingBillDoer(map, playerFaction: true);
            Pawn nonPlayerPawn = FindExistingBillDoer(map, playerFaction: false);
            if (playerPawn == null || nonPlayerPawn == null)
            {
                string reason = playerPawn == null
                    ? "no existing living player-faction pawn with a records tracker"
                    : "no existing living non-player-faction pawn with a records tracker";
                r.Skip(playerAssertion, reason);
                r.Skip(nonPlayerAssertion, reason);
                return;
            }

            List<ProductionBucket> savedBuckets = new List<ProductionBucket>(buckets);
            List<Thing> products = new List<Thing>();
            try
            {
                try
                {
                    Thing firstProduct = ThingMaker.MakeThing(firstProductDef);
                    if (firstProduct == null)
                    {
                        throw new InvalidOperationException("ThingMaker returned a null product");
                    }

                    firstProduct.stackCount = firstProductCount;
                    products.Add(firstProduct);

                    Thing secondProduct = ThingMaker.MakeThing(secondProductDef);
                    if (secondProduct == null)
                    {
                        throw new InvalidOperationException("ThingMaker returned a null product");
                    }

                    secondProduct.stackCount = secondProductCount;
                    products.Add(secondProduct);
                }
                catch (Exception ex)
                {
                    string reason = $"could not build the unspawned product fixture: {ex.Message}";
                    r.Skip(playerAssertion, reason);
                    r.Skip(nonPlayerAssertion, reason);
                    return;
                }

                buckets.Clear();
                int currentDay = GenDate.DaysPassedAt(GenTicks.TicksGame);
                RecordsUtility.Notify_BillDone(playerPawn, products);
                bool playerRecordedExactly = buckets.Count == products.Count &&
                    HasProductionBucket(
                        buckets, firstProductDef, currentDay, firstProductCount) &&
                    HasProductionBucket(
                        buckets, secondProductDef, currentDay, secondProductCount);
                r.Check(
                    playerRecordedExactly,
                    playerAssertion,
                    $"pawn {playerPawn.ToStringSafe()}; {DescribeProductionBuckets(buckets)}; " +
                    $"expected {firstProductDef.defName}={firstProductCount}, " +
                    $"{secondProductDef.defName}={secondProductCount}");

                buckets.Clear();
                RecordsUtility.Notify_BillDone(nonPlayerPawn, products);
                r.Check(
                    buckets.Count == 0,
                    nonPlayerAssertion,
                    $"pawn {nonPlayerPawn.ToStringSafe()} faction " +
                    $"{nonPlayerPawn.Faction?.Name ?? "null"}; " +
                    $"{DescribeProductionBuckets(buckets)}; expected 0 bucket(s)");
            }
            finally
            {
                buckets.Clear();
                buckets.AddRange(savedBuckets);
                for (int i = 0; i < products.Count; i++)
                {
                    Thing product = products[i];
                    if (product != null && !product.Destroyed)
                    {
                        product.Destroy(DestroyMode.Vanish);
                    }
                }

                r.Info($"Notify_BillDone fixture restored production ledger to " +
                       $"{buckets.Count} bucket(s); no pawn was created.");
            }
        }

        private static void CheckProductionLedgerMigration(
            Results r, IntercolonyWorldComponent state)
        {
            const string assertion =
                "a pre-58 state migrates to an existing empty production ledger";
            const int preMigrationVersion = 57;
            const int postMigrationVersion = 58;

            FieldInfo saveVersionField = typeof(IntercolonyWorldComponent).GetField(
                "saveVersion", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo productionLedgerField = typeof(IntercolonyWorldComponent).GetField(
                "productionLedger", BindingFlags.Instance | BindingFlags.NonPublic);
            if (saveVersionField == null || productionLedgerField == null)
            {
                r.Skip(
                    assertion,
                    saveVersionField == null
                        ? "the private saveVersion field is unavailable"
                        : "the private productionLedger field is unavailable");
                return;
            }

            List<ProductionBucket> savedBuckets = state.ProductionLedger;
            int savedSaveVersion = state.SaveVersion;
            LoadSaveMode savedScribeMode = Scribe.mode;
            try
            {
                if (Scribe.loader == null)
                {
                    r.Skip(
                        assertion,
                        "RimWorld Scribe.loader was null, so the PostLoadInit path could not run");
                    return;
                }

                // The normal load path repairs a missing list before calling MigrateIfNeeded.
                // Exercise that path first, then repeat the exact 57 -> 58 branch with the
                // repaired field made null again so removing its initializer cannot hide behind
                // ExposeData's earlier invariant repair.
                saveVersionField.SetValue(state, preMigrationVersion);
                productionLedgerField.SetValue(state, null);
                Scribe.mode = LoadSaveMode.PostLoadInit;
                state.ExposeData();
                List<ProductionBucket> loadPathBuckets = state.ProductionLedger;
                int loadPathSaveVersion = state.SaveVersion;
                bool loadPathIsEmpty = loadPathBuckets != null &&
                    loadPathBuckets.Count == 0 &&
                    loadPathSaveVersion == postMigrationVersion;

                saveVersionField.SetValue(state, preMigrationVersion);
                productionLedgerField.SetValue(state, null);
                state.MigrateIfNeeded();
                List<ProductionBucket> migratedBuckets = state.ProductionLedger;
                int migratedSaveVersion = state.SaveVersion;
                bool migrationIsEmpty = migratedBuckets != null &&
                    migratedBuckets.Count == 0 &&
                    migratedSaveVersion == postMigrationVersion;

                r.Check(
                    loadPathIsEmpty && migrationIsEmpty,
                    assertion,
                    $"PostLoadInit ledger {DescribeBucketCount(loadPathBuckets)} bucket(s), " +
                    $"version {loadPathSaveVersion}; isolated 57->58 ledger " +
                    $"{DescribeBucketCount(migratedBuckets)} bucket(s), version " +
                    $"{migratedSaveVersion}; expected 0 bucket(s), version " +
                    $"{postMigrationVersion}");
            }
            finally
            {
                Scribe.mode = savedScribeMode;
                productionLedgerField.SetValue(state, savedBuckets);
                saveVersionField.SetValue(state, savedSaveVersion);
                r.Info($"production migration fixture restored ledger to " +
                       $"{DescribeBucketCount(savedBuckets)} bucket(s) and save version " +
                       $"{state.SaveVersion}.");
            }
        }

        private static void CheckProductionCapture(
            Results r,
            IntercolonyWorldComponent state,
            Map map,
            ProduceLoopMapComponent loops,
            HashSet<IntVec3> reservedCells,
            List<CellRect> testRects,
            List<Designation> addedDesignations)
        {
            // This block belongs to the existing produce suite because that suite already owns
            // the map fixture and the ledger restoration boundary. Each assertion below still
            // restores the world ledger independently so a failed capture cannot poison the next
            // production check.
            CheckCraftedItemRate(r, state, map);
            CheckConstructedFurnitureRate(r, state, map, loops, reservedCells, testRects);
            CheckProduceFurnitureRate(
                r, state, map, loops, reservedCells, testRects, addedDesignations);
            CheckMultipleConstructedFurnitureRate(
                r, state, map, loops, reservedCells, testRects);
            CheckSaleAndRemovalDoNotProduce(
                r, state, map, loops, reservedCells, testRects);
            CheckNoProductionState(r, state);
            CheckProductionLedgerScribeRoundTrip(r);
            CheckMinifiableCraftedGood(r, state, map);
        }

        private static void CheckCraftedItemRate(
            Results r, IntercolonyWorldComponent state, Map map)
        {
            const string assertion = "one crafted item records 0.2/day";
            List<ProductionBucket> buckets = state?.ProductionLedger;
            List<ProductionBucket> savedBuckets =
                buckets == null ? null : new List<ProductionBucket>(buckets);
            CraftedSubject subject = FindCraftedSubject(requireMinifiable: false);
            Pawn worker = FindExistingBillDoer(map, playerFaction: true);
            List<Thing> products = new List<Thing>();
            List<Thing> ingredients = new List<Thing>();
            bool ok = false;
            int units = -1;
            float rate = 0f;
            string failure = null;

            try
            {
                if (buckets == null)
                {
                    failure = "the world production ledger is unavailable";
                }
                else if (IntercolonyWorldComponent.Current != state)
                {
                    failure =
                        "the self-test state is not IntercolonyWorldComponent.Current, so the " +
                        "real bill observer result cannot be attributed to this ledger";
                }
                else if (subject == null)
                {
                    failure = "no vanilla one-unit non-minifiable crafted recipe was available";
                }
                else if (worker == null)
                {
                    failure =
                        "no existing living player-faction pawn with a records tracker was available";
                }
                else
                {
                    buckets.Clear();
                    if (!TryMakeRecipeProducts(
                            subject, worker, products, ingredients, out failure))
                    {
                        // The helper supplies the precise fixture failure.
                    }
                    else
                    {
                        RecordsUtility.Notify_BillDone(worker, products);
                        units = CountCurrentProductionUnits(buckets, subject.productDef);
                        rate = ProductionLedgerService.CompletedPerDay(state, subject.productDef);
                        ok = products.Count == 1 && units == 1 && rate == 0.2f;
                        failure =
                            $"{subject.productDef.defName}; recorded {units} unit(s); " +
                            $"reported {rate:R}/day; expected 1 unit and literal 0.2/day";
                    }
                }
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                string cleanupFailure = DestroyFixtureThings(products);
                if (cleanupFailure != null)
                {
                    failure = AppendFailure(failure, cleanupFailure);
                }

                cleanupFailure = DestroyFixtureThings(ingredients);
                if (cleanupFailure != null)
                {
                    failure = AppendFailure(failure, cleanupFailure);
                }

                cleanupFailure = RestoreProductionBuckets(buckets, savedBuckets);
                if (cleanupFailure != null)
                {
                    failure = AppendFailure(failure, cleanupFailure);
                }
            }

            r.Check(ok, assertion, failure);
        }

        private static void CheckConstructedFurnitureRate(
            Results r,
            IntercolonyWorldComponent state,
            Map map,
            ProduceLoopMapComponent loops,
            HashSet<IntVec3> reservedCells,
            List<CellRect> testRects)
        {
            const string assertion = "one constructed furniture item records 0.2/day";
            List<ProductionBucket> buckets = state?.ProductionLedger;
            List<ProductionBucket> savedBuckets =
                buckets == null ? null : new List<ProductionBucket>(buckets);
            Subject subject = FindFurnitureSubject();
            Pawn worker = FindConstructionWorker(map);
            Frame frame;
            Building finished;
            IntVec3 cell;
            bool completed = false;
            bool ok = false;
            int units = -1;
            float rate = 0f;
            string failure = null;

            try
            {
                if (buckets == null)
                {
                    failure = "the world production ledger is unavailable";
                }
                else if (IntercolonyWorldComponent.Current != state)
                {
                    failure =
                        "the self-test state is not IntercolonyWorldComponent.Current, so the " +
                        "real construction observer result cannot be attributed to this ledger";
                }
                else if (subject == null)
                {
                    failure =
                        "vanilla DiningChair is unavailable or is not a minifiable stuff-built " +
                        "furniture definition";
                }
                else if (worker == null)
                {
                    failure =
                        "no existing living player-faction pawn with skills could be used as the " +
                        "real-frame worker";
                }
                else
                {
                    buckets.Clear();
                    completed = TryCompleteRealFurnitureFrame(
                        map,
                        loops,
                        subject,
                        worker,
                        reservedCells,
                        testRects,
                        out cell,
                        out frame,
                        out finished,
                        out failure);
                    if (completed)
                    {
                        units = CountCurrentProductionUnits(buckets, subject.thingDef);
                        rate = ProductionLedgerService.CompletedPerDay(state, subject.thingDef);
                        ok = frame != null && frame.Destroyed && finished != null &&
                            units == 1 && rate == 0.2f;
                        failure =
                            $"{subject.thingDef.defName} at {cell}; real frame completed " +
                            $"{(frame != null && frame.Destroyed ? "yes" : "no")}; " +
                            $"finished building {(finished == null ? "missing" : "present")}; " +
                            $"recorded {units} unit(s); reported {rate:R}/day; " +
                            "expected 1 unit and literal 0.2/day";
                    }
                }
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                string cleanupFailure = RestoreProductionBuckets(buckets, savedBuckets);
                if (cleanupFailure != null)
                {
                    failure = AppendFailure(failure, cleanupFailure);
                }
            }

            r.Check(ok, assertion, failure);
        }

        private static void CheckProduceFurnitureRate(
            Results r,
            IntercolonyWorldComponent state,
            Map map,
            ProduceLoopMapComponent loops,
            HashSet<IntVec3> reservedCells,
            List<CellRect> testRects,
            List<Designation> addedDesignations)
        {
            const string assertion =
                "one furniture item completed through Produce records 0.2/day exactly once";
            List<ProductionBucket> buckets = state?.ProductionLedger;
            List<ProductionBucket> savedBuckets =
                buckets == null ? null : new List<ProductionBucket>(buckets);
            Subject subject = FindFurnitureSubject();
            Pawn worker = FindConstructionWorker(map);
            Building seed = null;
            Blueprint_Build blueprint = null;
            Frame frame = null;
            Building finished = null;
            IntVec3 cell = IntVec3.Invalid;
            int seedUnits = -1;
            int afterCompletionUnits = -1;
            int afterSecondPassUnits = -1;
            float rate = 0f;
            bool seedWasNotRecorded = false;
            bool blueprintWasPlaced = false;
            bool completed = false;
            bool ok = false;
            string failure = null;

            try
            {
                if (buckets == null)
                {
                    failure = "the world production ledger is unavailable";
                }
                else if (IntercolonyWorldComponent.Current != state)
                {
                    failure =
                        "the self-test state is not IntercolonyWorldComponent.Current, so the " +
                        "real Produce construction result cannot be attributed to this ledger";
                }
                else if (subject == null)
                {
                    failure =
                        "vanilla DiningChair is unavailable or is not a minifiable stuff-built " +
                        "furniture definition";
                }
                else if (worker == null)
                {
                    failure =
                        "no existing living player-faction pawn with skills could be used as the " +
                        "real-frame worker";
                }
                else if (!TryFindBuildCell(
                             map,
                             loops,
                             subject,
                             Rot4.North,
                             reservedCells,
                             out cell))
                {
                    failure =
                        "no empty valid cell was available for the Produce seed and its ordinary " +
                        "vanilla blueprint";
                }
                else
                {
                    buckets.Clear();
                    RememberCell(cell, subject.thingDef, Rot4.North, reservedCells, testRects);
                    seed = SpawnFinishedBuilding(map, subject, cell, Rot4.North);
                    if (seed == null)
                    {
                        failure = "could not spawn the finished furniture seed";
                    }
                    else
                    {
                        loops.Enable(cell, Rot4.North, subject.thingDef, subject.stuffDef, null);
                        loops.RunPass();
                        seedUnits = CountCurrentProductionUnits(buckets, subject.thingDef);
                        seedWasNotRecorded = seedUnits == 0 &&
                            !ProductionLedgerService.HasRecordedProduction(
                                state, subject.thingDef);

                        Designation seedDesignation = map.designationManager.DesignationOn(
                            seed, DesignationDefOf.Uninstall);
                        if (seedDesignation != null && !addedDesignations.Contains(seedDesignation))
                        {
                            addedDesignations.Add(seedDesignation);
                        }

                        seed.Destroy(DestroyMode.Vanish);
                        loops.RunPass();
                        blueprint = FindBlueprint(map, cell, subject.thingDef);
                        blueprintWasPlaced = blueprint != null && blueprint.Spawned;
                        if (!blueprintWasPlaced)
                        {
                            failure =
                                "Produce did not place an ordinary spawned Blueprint_Build after " +
                                "its finished seed was removed";
                        }
                        else
                        {
                            completed = TryCompleteBlueprintFrame(
                                map,
                                blueprint,
                                subject,
                                worker,
                                out frame,
                                out finished,
                                out failure);
                            if (completed)
                            {
                                afterCompletionUnits =
                                    CountCurrentProductionUnits(buckets, subject.thingDef);
                                rate = ProductionLedgerService.CompletedPerDay(
                                    state, subject.thingDef);

                                // The loop sees the finished result again and may add its next
                                // Uninstall designation, but it must not record another completion.
                                loops.RunPass();
                                afterSecondPassUnits =
                                    CountCurrentProductionUnits(buckets, subject.thingDef);
                                Designation finishedDesignation =
                                    finished == null
                                        ? null
                                        : map.designationManager.DesignationOn(
                                            finished, DesignationDefOf.Uninstall);
                                if (finishedDesignation != null &&
                                    !addedDesignations.Contains(finishedDesignation))
                                {
                                    addedDesignations.Add(finishedDesignation);
                                }

                                ok = seedWasNotRecorded && blueprintWasPlaced &&
                                    frame != null && frame.Destroyed && finished != null &&
                                    afterCompletionUnits == 1 && rate == 0.2f &&
                                    afterSecondPassUnits == 1;
                                failure =
                                    $"seed units {seedUnits}; ordinary Blueprint_Build " +
                                    $"{(blueprintWasPlaced ? "placed" : "missing")}; real frame " +
                                    $"completed {(frame != null && frame.Destroyed ? "yes" : "no")}; " +
                                    $"units after completion {afterCompletionUnits}; units after " +
                                    $"second Produce pass {afterSecondPassUnits}; reported {rate:R}/day; " +
                                    "expected seed 0, one completed unit, unchanged one-unit total, " +
                                    "and literal 0.2/day";
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                string cleanupFailure = RestoreProductionBuckets(buckets, savedBuckets);
                if (cleanupFailure != null)
                {
                    failure = AppendFailure(failure, cleanupFailure);
                }
            }

            r.Check(ok, assertion, failure);
        }

        private static void CheckMultipleConstructedFurnitureRate(
            Results r,
            IntercolonyWorldComponent state,
            Map map,
            ProduceLoopMapComponent loops,
            HashSet<IntVec3> reservedCells,
            List<CellRect> testRects)
        {
            const string assertion = "two constructed chairs aggregate to 0.4/day";
            List<ProductionBucket> buckets = state?.ProductionLedger;
            List<ProductionBucket> savedBuckets =
                buckets == null ? null : new List<ProductionBucket>(buckets);
            Subject subject = FindFurnitureSubject();
            Pawn worker = FindConstructionWorker(map);
            Frame firstFrame;
            Frame secondFrame;
            Building firstFinished;
            Building secondFinished;
            IntVec3 firstCell;
            IntVec3 secondCell;
            bool firstCompleted = false;
            bool secondCompleted = false;
            bool ok = false;
            int units = -1;
            float rate = 0f;
            string failure = null;

            try
            {
                if (buckets == null)
                {
                    failure = "the world production ledger is unavailable";
                }
                else if (IntercolonyWorldComponent.Current != state)
                {
                    failure =
                        "the self-test state is not IntercolonyWorldComponent.Current, so the " +
                        "real construction observer result cannot be attributed to this ledger";
                }
                else if (subject == null)
                {
                    failure =
                        "vanilla DiningChair is unavailable or is not a minifiable stuff-built " +
                        "furniture definition";
                }
                else if (worker == null)
                {
                    failure =
                        "no existing living player-faction pawn with skills could be used as the " +
                        "real-frame worker";
                }
                else
                {
                    buckets.Clear();
                    firstCompleted = TryCompleteRealFurnitureFrame(
                        map,
                        loops,
                        subject,
                        worker,
                        reservedCells,
                        testRects,
                        out firstCell,
                        out firstFrame,
                        out firstFinished,
                        out failure);
                    if (!firstCompleted)
                    {
                        // The helper supplies the precise fixture failure.
                    }
                    else
                    {
                        secondCompleted = TryCompleteRealFurnitureFrame(
                            map,
                            loops,
                            subject,
                            worker,
                            reservedCells,
                            testRects,
                            out secondCell,
                            out secondFrame,
                            out secondFinished,
                            out failure);
                        if (secondCompleted)
                        {
                            units = CountCurrentProductionUnits(buckets, subject.thingDef);
                            rate = ProductionLedgerService.CompletedPerDay(
                                state, subject.thingDef);
                            ok = firstFrame != null && firstFrame.Destroyed &&
                                firstFinished != null && secondFrame != null &&
                                secondFrame.Destroyed && secondFinished != null &&
                                units == 2 && rate == 0.4f;
                            failure =
                                $"chairs at {firstCell} and {secondCell}; real frames completed " +
                                $"{(firstFrame != null && firstFrame.Destroyed ? "yes" : "no")}/" +
                                $"{(secondFrame != null && secondFrame.Destroyed ? "yes" : "no")}; " +
                                $"recorded {units} unit(s); reported {rate:R}/day; " +
                                "expected 2 units and literal 0.4/day";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                string cleanupFailure = RestoreProductionBuckets(buckets, savedBuckets);
                if (cleanupFailure != null)
                {
                    failure = AppendFailure(failure, cleanupFailure);
                }
            }

            r.Check(ok, assertion, failure);
        }

        private static void CheckSaleAndRemovalDoNotProduce(
            Results r,
            IntercolonyWorldComponent state,
            Map map,
            ProduceLoopMapComponent loops,
            HashSet<IntVec3> reservedCells,
            List<CellRect> testRects)
        {
            const string assertion =
                "selling or removing an existing item leaves production unchanged";
            List<ProductionBucket> buckets = state?.ProductionLedger;
            List<ProductionBucket> savedBuckets =
                buckets == null ? null : new List<ProductionBucket>(buckets);
            Subject subject = FindFurnitureSubject();
            Pawn worker = FindConstructionWorker(map);
            List<Thing> detachedSaleThings = new List<Thing>();
            Building saleSource = null;
            Thing saleWrapper = null;
            Building removedSource = null;
            Frame baselineFrame;
            Building baselineFinished;
            IntVec3 baselineCell;
            IntVec3 saleCell;
            IntVec3 removedCell;
            bool baselineCompleted = false;
            bool saleCompleted = false;
            bool removalCompleted = false;
            int baselineUnits = -1;
            int saleUnits = -1;
            int removalUnits = -1;
            float baselineRate = 0f;
            float saleRate = 0f;
            float removalRate = 0f;
            bool ok = false;
            string failure = null;

            try
            {
                if (buckets == null)
                {
                    failure = "the world production ledger is unavailable";
                }
                else if (IntercolonyWorldComponent.Current != state)
                {
                    failure =
                        "the self-test state is not IntercolonyWorldComponent.Current, so the " +
                        "real construction baseline cannot be attributed to this ledger";
                }
                else if (subject == null)
                {
                    failure =
                        "vanilla DiningChair is unavailable or is not a minifiable stuff-built " +
                        "furniture definition";
                }
                else if (worker == null)
                {
                    failure =
                        "no existing living player-faction pawn with skills could be used as the " +
                        "real-frame worker";
                }
                else
                {
                    buckets.Clear();
                    baselineCompleted = TryCompleteRealFurnitureFrame(
                        map,
                        loops,
                        subject,
                        worker,
                        reservedCells,
                        testRects,
                        out baselineCell,
                        out baselineFrame,
                        out baselineFinished,
                        out failure);
                    if (!baselineCompleted)
                    {
                        // The helper supplies the precise fixture failure.
                    }
                    else
                    {
                        baselineUnits =
                            CountCurrentProductionUnits(buckets, subject.thingDef);
                        baselineRate = ProductionLedgerService.CompletedPerDay(
                            state, subject.thingDef);

                        if (!TryFindBuildCell(
                                map,
                                loops,
                                subject,
                                Rot4.North,
                                reservedCells,
                                out saleCell))
                        {
                            failure = "no empty valid cell was available for the sale fixture";
                        }
                        else
                        {
                            RememberCell(
                                saleCell,
                                subject.thingDef,
                                Rot4.North,
                                reservedCells,
                                testRects);
                            saleSource = SpawnFinishedBuilding(
                                map, subject, saleCell, Rot4.North);
                            if (saleSource == null)
                            {
                                failure = "could not spawn the existing furniture sale fixture";
                            }
                            else
                            {
                                saleWrapper = saleSource.TryMakeMinified();
                                if (saleWrapper != null && saleWrapper != saleSource)
                                {
                                    detachedSaleThings.Add(saleWrapper);
                                }

                                detachedSaleThings.Add(saleSource);
                                bool saleFixtureIsReal = saleWrapper is MinifiedThing &&
                                    saleWrapper.GetInnerIfMinified()?.def == subject.thingDef;
                                if (!saleFixtureIsReal)
                                {
                                    failure =
                                        "the existing chair could not become a real minified sale fixture";
                                }
                                else
                                {
                                    // Settlement_TraderTracker calls this exact vanilla hook for a
                                    // player sale. It does not fabricate production or call the
                                    // construction observer.
                                    saleWrapper.PreTraded(
                                        TradeAction.PlayerSells, worker, trader: null);
                                    saleCompleted = true;
                                    saleWrapper.Destroy(DestroyMode.Vanish);
                                    saleUnits = CountCurrentProductionUnits(
                                        buckets, subject.thingDef);
                                    saleRate = ProductionLedgerService.CompletedPerDay(
                                        state, subject.thingDef);

                                    if (!TryFindBuildCell(
                                            map,
                                            loops,
                                            subject,
                                            Rot4.North,
                                            reservedCells,
                                            out removedCell))
                                    {
                                        failure =
                                            "no empty valid cell was available for the destroy fixture";
                                    }
                                    else
                                    {
                                        RememberCell(
                                            removedCell,
                                            subject.thingDef,
                                            Rot4.North,
                                            reservedCells,
                                            testRects);
                                        removedSource = SpawnFinishedBuilding(
                                            map, subject, removedCell, Rot4.North);
                                        if (removedSource == null)
                                        {
                                            failure =
                                                "could not spawn the existing furniture destroy fixture";
                                        }
                                        else
                                        {
                                            removedSource.Destroy(DestroyMode.Vanish);
                                            removalCompleted = true;
                                            removalUnits = CountCurrentProductionUnits(
                                                buckets, subject.thingDef);
                                            removalRate =
                                                ProductionLedgerService.CompletedPerDay(
                                                    state, subject.thingDef);
                                            ok = baselineCompleted && saleCompleted &&
                                                removalCompleted && baselineFrame != null &&
                                                baselineFrame.Destroyed && baselineFinished != null &&
                                                baselineUnits == 1 && saleUnits == 1 &&
                                                removalUnits == 1 && baselineRate == 0.2f &&
                                                saleRate == 0.2f && removalRate == 0.2f;
                                            failure =
                                                $"baseline {baselineUnits} unit(s) at {baselineRate:R}/day; " +
                                                $"after PlayerSells {saleUnits} at {saleRate:R}/day; " +
                                                $"after Destroy {removalUnits} at {removalRate:R}/day; " +
                                                "expected each state to remain 1 unit and literal " +
                                                "0.2/day, never negative";
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                string cleanupFailure = DestroyFixtureThings(detachedSaleThings);
                if (cleanupFailure != null)
                {
                    failure = AppendFailure(failure, cleanupFailure);
                }

                cleanupFailure = RestoreProductionBuckets(buckets, savedBuckets);
                if (cleanupFailure != null)
                {
                    failure = AppendFailure(failure, cleanupFailure);
                }
            }

            r.Check(ok, assertion, failure);
        }

        private static void CheckNoProductionState(
            Results r, IntercolonyWorldComponent state)
        {
            const string assertion = "no positive completion reports no production";
            List<ProductionBucket> buckets = state?.ProductionLedger;
            List<ProductionBucket> savedBuckets =
                buckets == null ? null : new List<ProductionBucket>(buckets);
            ThingDef product = ThingDefOf.DiningChair;
            bool hasRecordedProduction = false;
            int units = -1;
            float rate = 0f;
            bool ok = false;
            string failure = null;

            try
            {
                if (buckets == null)
                {
                    failure = "the world production ledger is unavailable";
                }
                else if (product == null)
                {
                    failure = "vanilla DiningChair is unavailable for the empty-window fixture";
                }
                else
                {
                    buckets.Clear();
                    units = CountCurrentProductionUnits(buckets, product);
                    hasRecordedProduction =
                        ProductionLedgerService.HasRecordedProduction(state, product);
                    rate = ProductionLedgerService.CompletedPerDay(state, product);
                    ok = units == 0 && !hasRecordedProduction && rate == 0f;
                    failure =
                        $"current-window units {units}; HasRecordedProduction " +
                        $"{(hasRecordedProduction ? "true" : "false")}; reported {rate:R}/day; " +
                        "expected 0 units, false/no-production state, and literal 0f only as " +
                        "that state—not as a measured production rate";
                }
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                string cleanupFailure = RestoreProductionBuckets(buckets, savedBuckets);
                if (cleanupFailure != null)
                {
                    failure = AppendFailure(failure, cleanupFailure);
                }
            }

            r.Check(ok, assertion, failure);
        }

        private static void CheckProductionLedgerScribeRoundTrip(Results r)
        {
            const string assertion =
                "a non-empty production ledger survives a real Scribe round trip";
            ThingDef product = ThingDefOf.ComponentIndustrial ?? ThingDefOf.DiningChair;
            IntercolonyWorldComponent source = null;
            IntercolonyWorldComponent loaded = null;
            string path = Path.Combine(
                Path.GetTempPath(), $"Intercolony-ProductionLedger-{Guid.NewGuid():N}.xml");
            string failure = null;
            string cleanupFailure = null;
            bool xmlContainsLedger = false;
            int loadedBucketCount = -1;
            int loadedUnits = -1;
            float loadedRate = 0f;
            bool loadedHasProduction = false;
            bool ok = false;
            LoadSaveMode savedScribeMode = Scribe.mode;

            try
            {
                if (product == null)
                {
                    failure = "vanilla ComponentIndustrial and DiningChair were unavailable";
                }
                else
                {
                    source = new IntercolonyWorldComponent(null);
                    int day = GenDate.DaysPassedAt(GenTicks.TicksGame);
                    source.ProductionLedger.Add(new ProductionBucket
                    {
                        thingDef = product,
                        day = day,
                        count = 1
                    });

                    if (Scribe.loader == null)
                    {
                        failure =
                            "RimWorld Scribe.loader was null, so a real save/load round trip was " +
                            "not reachable in this self-test";
                    }
                    else
                    {
                        Scribe.saver.InitSaving(path, "intercolonyProductionLedgerTest");
                        IntercolonyWorldComponent savedState = source;
                        Scribe_Deep.Look(ref savedState, "state");
                        Scribe.saver.FinalizeSaving();
                        xmlContainsLedger = File.ReadAllText(path).IndexOf(
                            "<productionLedger", StringComparison.Ordinal) >= 0;

                        Scribe.loader.InitLoading(path);
                        Scribe_Deep.Look(ref loaded, "state", (object)null);
                        Scribe.loader.FinalizeLoading();

                        List<ProductionBucket> loadedBuckets = loaded?.ProductionLedger;
                        loadedBucketCount = loadedBuckets?.Count ?? -1;
                        if (loadedBuckets != null && loadedBuckets.Count == 1 &&
                            loadedBuckets[0] != null)
                        {
                            loadedUnits = loadedBuckets[0].count;
                            loadedRate = ProductionLedgerService.CompletedPerDay(
                                loaded, product);
                            loadedHasProduction =
                                ProductionLedgerService.HasRecordedProduction(loaded, product);
                            ok = xmlContainsLedger && loadedBuckets[0].thingDef == product &&
                                loadedBuckets[0].day == day && loadedUnits == 1 &&
                                loadedHasProduction && loadedRate == 0.2f;
                        }

                        failure =
                            $"XML productionLedger {(xmlContainsLedger ? "present" : "missing")}; " +
                            $"loaded buckets {loadedBucketCount}; loaded units {loadedUnits}; " +
                            $"loaded HasRecordedProduction " +
                            $"{(loadedHasProduction ? "true" : "false")}; loaded rate " +
                            $"{loadedRate:R}/day; expected one non-empty bucket with 1 unit and " +
                            "literal 0.2/day after the real Scribe round trip";
                    }
                }
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                try
                {
                    Scribe.ForceStop();
                }
                catch (Exception ex)
                {
                    cleanupFailure = $"Scribe cleanup failed: {ex.GetType().Name}: {ex.Message}";
                }

                Scribe.mode = savedScribeMode;
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch (Exception ex)
                {
                    cleanupFailure = AppendFailure(
                        cleanupFailure,
                        $"temporary Scribe file cleanup failed: {ex.GetType().Name}: {ex.Message}");
                }

                source?.ProductionLedger?.Clear();
                loaded?.ProductionLedger?.Clear();
            }

            if (cleanupFailure != null)
            {
                failure = AppendFailure(failure, cleanupFailure);
            }

            r.Check(ok, assertion, failure);
        }

        private static void CheckMinifiableCraftedGood(
            Results r, IntercolonyWorldComponent state, Map map)
        {
            const string assertion =
                "one crafted minifiable good records the good, not its wrapper";
            List<ProductionBucket> buckets = state?.ProductionLedger;
            List<ProductionBucket> savedBuckets =
                buckets == null ? null : new List<ProductionBucket>(buckets);
            ThingDef goodDef = DefDatabase<ThingDef>.GetNamedSilentFail("SculptureSmall");
            ThingDef stuffDef = goodDef != null && goodDef.MadeFromStuff
                ? ThingDefOf.WoodLog
                : null;
            Pawn worker = null;
            List<Thing> products = new List<Thing>();
            Thing wrapper = null;
            Thing inner = null;
            int goodUnits = -1;
            int wrapperUnits = -1;
            float rate = 0f;
            bool ok = false;
            bool fixtureUnavailable = false;
            bool notificationCalled = false;
            string failure = null;

            try
            {
                if (buckets == null)
                {
                    fixtureUnavailable = true;
                    failure = "the world production ledger is unavailable";
                }
                else if (IntercolonyWorldComponent.Current != state)
                {
                    failure =
                        "the self-test state is not IntercolonyWorldComponent.Current, so the " +
                        "real bill observer result cannot be attributed to this ledger";
                }
                else if (goodDef == null)
                {
                    fixtureUnavailable = true;
                    failure = "vanilla SculptureSmall was unavailable for the direct " +
                        "minifiable-good fixture";
                }
                else if (!goodDef.Minifiable)
                {
                    fixtureUnavailable = true;
                    failure = "vanilla SculptureSmall was available but not minifiable";
                }
                else if (goodDef.MadeFromStuff && stuffDef == null)
                {
                    fixtureUnavailable = true;
                    failure = "vanilla WoodLog was unavailable for the SculptureSmall " +
                        "minifiable-good fixture";
                }
                else
                {
                    worker = FindExistingBillDoer(map, playerFaction: true);
                    if (worker == null)
                    {
                        fixtureUnavailable = true;
                        failure =
                            "no existing living player-faction pawn with a records tracker was " +
                            "available";
                    }
                }

                if (failure == null)
                {
                    buckets.Clear();
                    Thing good = ThingMaker.MakeThing(goodDef, stuffDef);
                    if (good == null)
                    {
                        fixtureUnavailable = true;
                        failure =
                            "ThingMaker returned no SculptureSmall for the minifiable-good fixture";
                    }
                    else
                    {
                        good.stackCount = 1;
                        products.Add(good);
                        wrapper = good.MakeMinified();
                        if (wrapper == null)
                        {
                            fixtureUnavailable = true;
                            failure =
                                "Thing.MakeMinified returned no MinifiedThing for SculptureSmall";
                        }
                        else
                        {
                            products[0] = wrapper;
                            inner = wrapper.GetInnerIfMinified();
                            if (inner == null)
                            {
                                fixtureUnavailable = true;
                                failure =
                                    "Thing.MakeMinified returned a wrapper with no inner good";
                            }
                            else
                            {
                                // Drive the real vanilla completion notification. The Harmony
                                // postfix under test observes this call, just as it does for a
                                // materialized GenRecipe product.
                                notificationCalled = true;
                                RecordsUtility.Notify_BillDone(worker, products);
                                goodUnits = CountCurrentProductionUnits(buckets, goodDef);
                                wrapperUnits = CountCurrentProductionUnits(buckets, wrapper.def);
                                rate = ProductionLedgerService.CompletedPerDay(state, goodDef);
                                ok = products.Count == 1 && wrapper is MinifiedThing &&
                                    wrapper.def != goodDef && inner.def == goodDef &&
                                    inner.stackCount == 1 && goodUnits == 1 &&
                                    wrapperUnits == 0 && rate == 0.2f;
                                failure =
                                    $"wrapper {wrapper?.def?.defName ?? "null"}; " +
                                    $"inner {inner?.def?.defName ?? "null"}; " +
                                    $"good units {goodUnits}; wrapper units {wrapperUnits}; " +
                                    $"reported good rate {rate:R}/day; expected inner good 1 unit, " +
                                    "wrapper 0 units, and literal 0.2/day";
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                fixtureUnavailable = !notificationCalled;
                failure = fixtureUnavailable
                    ? $"could not build the direct minifiable-good fixture: " +
                      $"{ex.GetType().Name}: {ex.Message}"
                    : $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                string cleanupFailure = DestroyFixtureThings(products);
                if (cleanupFailure != null)
                {
                    failure = AppendFailure(failure, cleanupFailure);
                }

                cleanupFailure = RestoreProductionBuckets(buckets, savedBuckets);
                if (cleanupFailure != null)
                {
                    failure = AppendFailure(failure, cleanupFailure);
                }
            }

            if (fixtureUnavailable)
            {
                r.Skip(assertion, failure);
            }
            else
            {
                r.Check(ok, assertion, failure);
            }
        }

        private static Subject FindFurnitureSubject()
        {
            ThingDef chair = ThingDefOf.DiningChair;
            if (chair == null || chair.building == null || !chair.building.isSittable)
            {
                return null;
            }

            return BuildSubjectForDefinition(chair);
        }

        private static Subject BuildSubjectForDefinition(ThingDef def)
        {
            if (def == null ||
                !def.Minifiable ||
                def.category != ThingCategory.Building ||
                !def.MadeFromStuff ||
                def.IsFrame ||
                def.blueprintDef == null ||
                def.frameDef == null ||
                def.building == null ||
                def.thingClass == null ||
                !typeof(Building).IsAssignableFrom(def.thingClass) ||
                !def.CanHaveFaction)
            {
                return null;
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
                return null;
            }

            ThingDef defaultStuff = GenStuff.DefaultStuffFor(def);
            ThingDef selectedStuff = null;
            for (int i = 0; i < validStuffs.Count; i++)
            {
                if (validStuffs[i] == defaultStuff &&
                    def.GetStatValueAbstract(StatDefOf.WorkToBuild, validStuffs[i]) > 0f)
                {
                    selectedStuff = validStuffs[i];
                    break;
                }
            }

            if (selectedStuff == null)
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

            return selectedStuff == null
                ? null
                : new Subject
                {
                    thingDef = def,
                    stuffDef = selectedStuff,
                    nonDefaultStuff = selectedStuff != defaultStuff ? selectedStuff : null,
                    validStuffCount = validStuffs.Count
                };
        }

        private static CraftedSubject FindCraftedSubject(bool requireMinifiable)
        {
            ThingDef preferredProduct = requireMinifiable
                ? DefDatabase<ThingDef>.GetNamedSilentFail("SculptureSmall")
                : ThingDefOf.ComponentIndustrial;
            RecipeDef preferredRecipe = null;
            if (!requireMinifiable)
            {
                preferredRecipe = DefDatabase<RecipeDef>.GetNamedSilentFail(
                    "Make_ComponentIndustrial");
            }

            if (preferredRecipe != null &&
                IsCraftedRecipeCandidate(preferredRecipe, requireMinifiable) &&
                preferredRecipe.products[0].thingDef == preferredProduct)
            {
                return MakeCraftedSubject(preferredRecipe);
            }

            CraftedSubject fallback = null;
            foreach (RecipeDef recipe in DefDatabase<RecipeDef>.AllDefs)
            {
                if (!IsCraftedRecipeCandidate(recipe, requireMinifiable))
                {
                    continue;
                }

                CraftedSubject candidate = MakeCraftedSubject(recipe);
                if (candidate == null)
                {
                    continue;
                }

                if (candidate.productDef == preferredProduct)
                {
                    return candidate;
                }

                fallback = fallback ?? candidate;
            }

            return fallback;
        }

        private static bool IsCraftedRecipeCandidate(
            RecipeDef recipe, bool requireMinifiable)
        {
            if (recipe == null ||
                recipe.products == null ||
                recipe.products.Count != 1 ||
                recipe.products[0] == null ||
                recipe.products[0].thingDef == null ||
                recipe.products[0].count != 1 ||
                recipe.specialProducts != null && recipe.specialProducts.Count > 0 ||
                recipe.efficiencyStat != null ||
                recipe.UsesUnfinishedThing)
            {
                return false;
            }

            ThingDef product = recipe.products[0].thingDef;
            bool isExpectedCategory = requireMinifiable
                ? product.category == ThingCategory.Building
                : product.category == ThingCategory.Item;
            if (!isExpectedCategory || product.Minifiable != requireMinifiable)
            {
                return false;
            }

            return !product.HasComp<CompQuality>() || recipe.workSkill != null;
        }

        private static CraftedSubject MakeCraftedSubject(RecipeDef recipe)
        {
            ThingDef product = recipe?.products?[0]?.thingDef;
            if (recipe == null || product == null)
            {
                return null;
            }

            ThingDef stuff = null;
            if (product.MadeFromStuff)
            {
                foreach (ThingDef allowedStuff in GenStuff.AllowedStuffsFor(product))
                {
                    if (allowedStuff != null)
                    {
                        stuff = allowedStuff;
                        break;
                    }
                }
            }

            if (product.MadeFromStuff && stuff == null)
            {
                return null;
            }

            return new CraftedSubject
            {
                recipe = recipe,
                productDef = product,
                stuffDef = stuff
            };
        }

        private static Pawn FindConstructionWorker(Map map)
        {
            IReadOnlyList<Pawn> pawns = map?.mapPawns?.AllPawnsSpawned;
            if (pawns == null)
            {
                return null;
            }

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (IsExistingBillDoer(pawn, playerFaction: true) && pawn.skills != null)
                {
                    return pawn;
                }
            }

            return null;
        }

        private static bool TryMakeRecipeProducts(
            CraftedSubject subject,
            Pawn worker,
            List<Thing> products,
            List<Thing> ingredients,
            out string failure)
        {
            failure = null;
            if (subject?.recipe == null || subject.productDef == null || worker == null)
            {
                failure = "the crafted recipe or player worker fixture was unavailable";
                return false;
            }

            Thing dominantIngredient = null;
            try
            {
                if (subject.stuffDef != null)
                {
                    dominantIngredient = ThingMaker.MakeThing(subject.stuffDef);
                    if (dominantIngredient == null)
                    {
                        failure = "ThingMaker returned no dominant stuff ingredient";
                        return false;
                    }

                    ingredients.Add(dominantIngredient);
                }

                foreach (Thing product in GenRecipe.MakeRecipeProducts(
                             subject.recipe,
                             worker,
                             ingredients,
                             dominantIngredient,
                             billGiver: null))
                {
                    if (product != null)
                    {
                        products.Add(product);
                    }
                }

                Thing inner = products.Count == 1
                    ? products[0].GetInnerIfMinified()
                    : null;
                if (products.Count != 1 || inner == null || inner.def != subject.productDef ||
                    inner.stackCount != 1)
                {
                    failure =
                        $"GenRecipe produced {products.Count} product(s); expected one stack of " +
                        $"{subject.productDef.defName}, got " +
                        $"{inner?.def?.defName ?? "null"} x {inner?.stackCount.ToString() ?? "0"}";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                failure =
                    $"could not materialize the real vanilla recipe product: " +
                    $"{ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        private static bool TryCompleteRealFurnitureFrame(
            Map map,
            ProduceLoopMapComponent loops,
            Subject subject,
            Pawn worker,
            HashSet<IntVec3> reservedCells,
            List<CellRect> testRects,
            out IntVec3 cell,
            out Frame frame,
            out Building finished,
            out string failure)
        {
            cell = IntVec3.Invalid;
            frame = null;
            finished = null;
            failure = null;
            if (worker == null || worker.Faction != Faction.OfPlayer)
            {
                failure = "the real-frame worker was not a player-faction pawn";
                return false;
            }

            if (!TryFindBuildCell(
                    map,
                    loops,
                    subject,
                    Rot4.North,
                    reservedCells,
                    out cell))
            {
                failure = "no empty valid cell was available for a real furniture frame";
                return false;
            }

            RememberCell(cell, subject.thingDef, Rot4.North, reservedCells, testRects);
            try
            {
                Blueprint_Build blueprint = GenConstruct.PlaceBlueprintForBuild(
                    subject.thingDef,
                    cell,
                    map,
                    Rot4.North,
                    Faction.OfPlayer,
                    subject.stuffDef);
                if (blueprint == null || !blueprint.Spawned ||
                    blueprint.def.entityDefToBuild != subject.thingDef)
                {
                    failure =
                        "GenConstruct did not create a spawned ordinary Blueprint_Build for the " +
                        "furniture definition";
                    return false;
                }

                return TryCompleteBlueprintFrame(
                    map,
                    blueprint,
                    subject,
                    worker,
                    out frame,
                    out finished,
                    out failure);
            }
            catch (Exception ex)
            {
                failure =
                    $"could not create or complete the real furniture frame: " +
                    $"{ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        private static bool TryCompleteBlueprintFrame(
            Map map,
            Blueprint_Build blueprint,
            Subject subject,
            Pawn worker,
            out Frame frame,
            out Building finished,
            out string failure)
        {
            frame = null;
            finished = null;
            failure = null;
            Thing createdThing = null;
            try
            {
                if (blueprint == null || !blueprint.Spawned || subject?.thingDef == null ||
                    worker == null || worker.Faction != Faction.OfPlayer)
                {
                    failure = "the Produce blueprint or player-faction worker fixture was invalid";
                    return false;
                }

                bool jobEnded;
                if (!blueprint.TryReplaceWithSolidThing(
                        worker, out createdThing, out jobEnded))
                {
                    failure =
                        $"Blueprint_Build.TryReplaceWithSolidThing returned false " +
                        $"(job ended {(jobEnded ? "yes" : "no")})";
                    return false;
                }

                frame = createdThing as Frame;
                if (frame == null || !frame.Spawned || frame.Map != map ||
                    frame.BuildDef != subject.thingDef || frame.Faction != Faction.OfPlayer)
                {
                    failure =
                        "the ordinary vanilla blueprint did not become a spawned player-faction " +
                        "Frame for the furniture definition";
                    return false;
                }

                // This is the vanilla completion seam under test. The Harmony observer must hear
                // this call; calling the patch method directly would prove nothing about vanilla.
                frame.CompleteConstruction(worker);
                finished = FindFinishedBuildingAt(map, frame.Position, subject.thingDef);
                if (finished == null)
                {
                    failure =
                        "Frame.CompleteConstruction returned without a spawned finished furniture " +
                        "building at the frame cell";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                failure =
                    $"could not complete the ordinary vanilla Frame: " +
                    $"{ex.GetType().Name}: {ex.Message}";
                return false;
            }
            finally
            {
                if (createdThing != null && !createdThing.Spawned && !createdThing.Destroyed)
                {
                    try
                    {
                        createdThing.Destroy(DestroyMode.Vanish);
                    }
                    catch (Exception ex)
                    {
                        failure = AppendFailure(
                            failure,
                            $"unspawned frame cleanup failed: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
        }

        private static Building FindFinishedBuildingAt(
            Map map, IntVec3 cell, ThingDef thingDef)
        {
            if (map == null || thingDef == null || !cell.InBounds(map))
            {
                return null;
            }

            List<Thing> things = map.thingGrid.ThingsListAt(cell);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is Building building && building.def == thingDef)
                {
                    return building;
                }
            }

            return null;
        }

        private static int CountCurrentProductionUnits(
            List<ProductionBucket> buckets, ThingDef thingDef)
        {
            if (buckets == null || thingDef == null)
            {
                return 0;
            }

            int today = GenDate.DaysPassedAt(GenTicks.TicksGame);
            int count = 0;
            for (int i = 0; i < buckets.Count; i++)
            {
                ProductionBucket bucket = buckets[i];
                if (bucket != null && bucket.thingDef == thingDef && bucket.day == today)
                {
                    count += bucket.count;
                }
            }

            return count;
        }

        private static string RestoreProductionBuckets(
            List<ProductionBucket> buckets, List<ProductionBucket> savedBuckets)
        {
            if (buckets == null)
            {
                return null;
            }

            try
            {
                buckets.Clear();
                if (savedBuckets != null)
                {
                    buckets.AddRange(savedBuckets);
                }

                return null;
            }
            catch (Exception ex)
            {
                return $"production ledger restoration failed: {ex.GetType().Name}: {ex.Message}";
            }
        }

        private static string DestroyFixtureThings(List<Thing> things)
        {
            if (things == null || things.Count == 0)
            {
                return null;
            }

            HashSet<Thing> seen = new HashSet<Thing>();
            StringBuilder failures = new StringBuilder();
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing == null || !seen.Add(thing) || thing.Destroyed)
                {
                    continue;
                }

                try
                {
                    thing.Destroy(DestroyMode.Vanish);
                }
                catch (Exception ex)
                {
                    if (failures.Length > 0)
                    {
                        failures.Append("; ");
                    }

                    failures.Append("fixture thing cleanup failed: ")
                        .Append(ex.GetType().Name)
                        .Append(": ")
                        .Append(ex.Message);
                }
            }

            return failures.Length == 0 ? null : failures.ToString();
        }

        private static string AppendFailure(string failure, string additional)
        {
            if (additional == null)
            {
                return failure;
            }

            return failure == null ? additional : failure + "; " + additional;
        }

        private static Pawn FindExistingBillDoer(Map map, bool playerFaction)
        {
            if (Faction.OfPlayer == null)
            {
                return null;
            }

            Pawn found = FindExistingBillDoerOnMap(map, playerFaction);
            if (found != null)
            {
                return found;
            }

            if (Find.Maps != null)
            {
                for (int i = 0; i < Find.Maps.Count; i++)
                {
                    found = FindExistingBillDoerOnMap(Find.Maps[i], playerFaction);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }

            List<Pawn> worldPawns = Find.WorldPawns?.AllPawnsAliveOrDead;
            if (worldPawns != null)
            {
                for (int i = 0; i < worldPawns.Count; i++)
                {
                    if (IsExistingBillDoer(worldPawns[i], playerFaction))
                    {
                        return worldPawns[i];
                    }
                }
            }

            return null;
        }

        private static Pawn FindExistingBillDoerOnMap(Map map, bool playerFaction)
        {
            IReadOnlyList<Pawn> pawns = map?.mapPawns?.AllPawnsSpawned;
            if (pawns == null)
            {
                return null;
            }

            for (int i = 0; i < pawns.Count; i++)
            {
                if (IsExistingBillDoer(pawns[i], playerFaction))
                {
                    return pawns[i];
                }
            }

            return null;
        }

        private static bool IsExistingBillDoer(Pawn pawn, bool playerFaction)
        {
            return pawn != null && pawn.health != null && !pawn.Dead && pawn.records != null &&
                (playerFaction
                    ? pawn.Faction == Faction.OfPlayer
                    : pawn.Faction != null && pawn.Faction != Faction.OfPlayer);
        }

        private static string DescribeProductionBuckets(List<ProductionBucket> buckets)
        {
            if (buckets == null)
            {
                return "null ledger";
            }

            if (buckets.Count == 0)
            {
                return "0 bucket(s)";
            }

            StringBuilder description = new StringBuilder();
            for (int i = 0; i < buckets.Count; i++)
            {
                if (i > 0)
                {
                    description.Append(", ");
                }

                ProductionBucket bucket = buckets[i];
                description.Append(bucket?.thingDef?.defName ?? "null");
                description.Append("=");
                description.Append(bucket?.count ?? 0);
                description.Append("@");
                description.Append(bucket?.day ?? -1);
            }

            return description.ToString();
        }

        private static string DescribeBucketCount(List<ProductionBucket> buckets)
        {
            return buckets == null ? "null" : buckets.Count.ToString();
        }

        private static bool HasProductionBucket(
            List<ProductionBucket> buckets, ThingDef product, int day, int count)
        {
            for (int i = 0; i < buckets.Count; i++)
            {
                ProductionBucket bucket = buckets[i];
                if (bucket != null && bucket.thingDef == product &&
                    bucket.day == day && bucket.count == count)
                {
                    return true;
                }
            }

            return false;
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

        private static void CheckPauseStopUninstallBehavior(
            Results r,
            Map map,
            ProduceLoopMapComponent loops,
            Subject subject,
            HashSet<IntVec3> reservedCells,
            List<CellRect> testRects,
            List<Designation> addedDesignations)
        {
            const string pauseLabel = "Pause cancels an outstanding Uninstall designation";
            const string stopLabel = "Stop does not cancel an outstanding Uninstall designation";

            IntVec3 pauseCell;
            if (!TryFindBuildCell(
                    map, loops, subject, Rot4.North, reservedCells, out pauseCell))
            {
                r.Skip(pauseLabel, "no empty valid cell for the Pause uninstall fixture");
            }
            else
            {
                RememberCell(
                    pauseCell,
                    subject.thingDef,
                    Rot4.North,
                    reservedCells,
                    testRects);
                Building building = null;
                Designation uninstall = null;
                try
                {
                    string failure;
                    if (!TryPrepareInstalledUninstall(
                            map,
                            loops,
                            subject,
                            pauseCell,
                            out building,
                            out uninstall,
                            out failure))
                    {
                        r.Skip(pauseLabel, failure);
                    }
                    else
                    {
                        addedDesignations.Add(uninstall);
                        CheckSafely(
                            r,
                            pauseLabel,
                            () =>
                            {
                                loops.Pause(pauseCell);
                                return map.designationManager.DesignationOn(
                                           building,
                                           DesignationDefOf.Uninstall) == null &&
                                       building.Spawned &&
                                       building.Map == map &&
                                       building.Position == pauseCell;
                            },
                            () => $"cell {pauseCell}; Uninstall present " +
                            $"{(map.designationManager.DesignationOn(
                                building,
                                DesignationDefOf.Uninstall) == null ? "no" : "yes")}; " +
                            $"building spawned at cell " +
                            $"{(building.Spawned && building.Map == map &&
                                building.Position == pauseCell ? "yes" : "no")}");
                    }
                }
                finally
                {
                    try
                    {
                        loops.Disable(pauseCell);
                    }
                    finally
                    {
                        RemoveAllDesignationsOn(
                            map,
                            building,
                            DesignationDefOf.Uninstall,
                            r);
                        DestroyThingsInRect(
                            map,
                            GenAdj.OccupiedRect(
                                pauseCell,
                                Rot4.North,
                                subject.thingDef.Size));
                    }
                }
            }

            IntVec3 stopCell;
            if (!TryFindBuildCell(
                    map, loops, subject, Rot4.North, reservedCells, out stopCell))
            {
                r.Skip(stopLabel, "no empty valid cell for the Stop uninstall fixture");
                return;
            }

            RememberCell(
                stopCell,
                subject.thingDef,
                Rot4.North,
                reservedCells,
                testRects);
            Building stopBuilding = null;
            Designation stopUninstall = null;
            try
            {
                string failure;
                if (!TryPrepareInstalledUninstall(
                        map,
                        loops,
                        subject,
                        stopCell,
                        out stopBuilding,
                        out stopUninstall,
                        out failure))
                {
                    r.Skip(stopLabel, failure);
                }
                else
                {
                    addedDesignations.Add(stopUninstall);
                    CheckSafely(
                        r,
                        stopLabel,
                        () =>
                        {
                            loops.Disable(stopCell);
                            return map.designationManager.DesignationOn(
                                       stopBuilding,
                                       DesignationDefOf.Uninstall) == stopUninstall;
                        },
                        () => $"cell {stopCell}; Uninstall present " +
                        $"{(map.designationManager.DesignationOn(
                            stopBuilding,
                            DesignationDefOf.Uninstall) == null ? "no" : "yes")}; " +
                        $"building spawned at cell " +
                        $"{(stopBuilding.Spawned && stopBuilding.Map == map &&
                            stopBuilding.Position == stopCell ? "yes" : "no")}");
                }
            }
            finally
            {
                try
                {
                    loops.Disable(stopCell);
                }
                finally
                {
                    RemoveAllDesignationsOn(
                        map,
                        stopBuilding,
                        DesignationDefOf.Uninstall,
                        r);
                    DestroyThingsInRect(
                        map,
                        GenAdj.OccupiedRect(
                            stopCell,
                            Rot4.North,
                            subject.thingDef.Size));
                }
            }
        }

        private static void CheckTargetBehavior(
            Results r,
            Map map,
            ProduceLoopMapComponent loops,
            Subject subject,
            HashSet<IntVec3> reservedCells,
            List<CellRect> testRects,
            List<Zone_Stockpile> testZones)
        {
            const string metTargetLabel = "a met target stops the next cycle";
            const string aboveStockLabel = "a target above stock still produces";
            const string zeroTargetLabel = "a zero target produces without limit";
            const string spendingLabel = "spending stock below the target resumes production";

            IntVec3 cell;
            if (!TryFindBuildCell(
                    map, loops, subject, Rot4.North, reservedCells, out cell))
            {
                const string reason = "no empty valid cell for the target fixture";
                SkipTargetAssertions(r, reason);
                return;
            }

            RememberCell(cell, subject.thingDef, Rot4.North, reservedCells, testRects);

            IntVec3 storageCell;
            string storageFailure;
            if (!TryCreateTargetStorage(
                    map, reservedCells, testZones, out storageCell, out storageFailure))
            {
                SkipTargetAssertions(r, storageFailure);
                return;
            }

            RememberCell(storageCell, null, Rot4.North, reservedCells, testRects);
            CellRect buildRect = GenAdj.OccupiedRect(
                cell, Rot4.North, subject.thingDef.Size);
            CellRect storageRect = new CellRect(storageCell.x, storageCell.z, 1, 1);

            List<Thing> fixtureStock = null;
            try
            {
                int target = 0;
                int countedStock = 0;
                bool blueprintAppeared = false;
                string stockFailure;
                if (!TrySpawnStoredTargetStock(
                        map,
                        subject,
                        storageCell,
                        1,
                        out fixtureStock,
                        out countedStock,
                        out stockFailure))
                {
                    r.Skip(metTargetLabel, stockFailure);
                }
                else
                {
                    target = countedStock;
                    CheckSafely(
                        r,
                        metTargetLabel,
                        () =>
                        {
                            loops.Enable(
                                cell,
                                Rot4.North,
                                subject.thingDef,
                                subject.stuffDef,
                                null);
                            loops.SetTargetCount(cell, target);
                            loops.RunPass();
                            countedStock = CountStoredTargetStock(map, subject.thingDef);
                            blueprintAppeared =
                                FindBlueprint(map, cell, subject.thingDef) != null;
                            return target > 0 &&
                                   countedStock >= target &&
                                   !blueprintAppeared;
                        },
                        () => TargetDetail(
                            cell, target, countedStock, blueprintAppeared));
                }
            }
            finally
            {
                DestroyStoredTargetStock(fixtureStock);
                DestroyThingsInRect(map, storageRect);
                loops.Disable(cell);
                DestroyThingsInRect(map, buildRect);
            }

            fixtureStock = null;
            try
            {
                int target = 0;
                int countedStock = 0;
                bool blueprintAppeared = false;
                string stockFailure;
                if (!TrySpawnStoredTargetStock(
                        map,
                        subject,
                        storageCell,
                        1,
                        out fixtureStock,
                        out countedStock,
                        out stockFailure))
                {
                    r.Skip(aboveStockLabel, stockFailure);
                }
                else
                {
                    target = countedStock + 1;
                    CheckSafely(
                        r,
                        aboveStockLabel,
                        () =>
                        {
                            loops.Enable(
                                cell,
                                Rot4.North,
                                subject.thingDef,
                                subject.stuffDef,
                                null);
                            loops.SetTargetCount(cell, target);
                            loops.RunPass();
                            countedStock = CountStoredTargetStock(map, subject.thingDef);
                            blueprintAppeared =
                                FindBlueprint(map, cell, subject.thingDef) != null;
                            return target > countedStock && blueprintAppeared;
                        },
                        () => TargetDetail(
                            cell, target, countedStock, blueprintAppeared));
                }
            }
            finally
            {
                DestroyStoredTargetStock(fixtureStock);
                DestroyThingsInRect(map, storageRect);
                loops.Disable(cell);
                DestroyThingsInRect(map, buildRect);
            }

            fixtureStock = null;
            try
            {
                const int target = 0;
                int countedStock = 0;
                bool blueprintAppeared = false;
                string stockFailure;
                if (!TrySpawnStoredTargetStock(
                        map,
                        subject,
                        storageCell,
                        1,
                        out fixtureStock,
                        out countedStock,
                        out stockFailure))
                {
                    r.Skip(zeroTargetLabel, stockFailure);
                }
                else
                {
                    CheckSafely(
                        r,
                        zeroTargetLabel,
                        () =>
                        {
                            loops.Enable(
                                cell,
                                Rot4.North,
                                subject.thingDef,
                                subject.stuffDef,
                                null);
                            loops.SetTargetCount(cell, target);
                            loops.RunPass();
                            countedStock = CountStoredTargetStock(map, subject.thingDef);
                            blueprintAppeared =
                                FindBlueprint(map, cell, subject.thingDef) != null;
                            return countedStock > 0 && blueprintAppeared;
                        },
                        () => TargetDetail(
                            cell, target, countedStock, blueprintAppeared));
                }
            }
            finally
            {
                DestroyStoredTargetStock(fixtureStock);
                DestroyThingsInRect(map, storageRect);
                loops.Disable(cell);
                DestroyThingsInRect(map, buildRect);
            }

            fixtureStock = null;
            try
            {
                int target = 0;
                int countedStockBefore = 0;
                int countedStockAfter = 0;
                bool blueprintBefore = false;
                bool blueprintAppeared = false;
                bool stockRemoved = false;
                string stockFailure;
                if (!TrySpawnStoredTargetStock(
                        map,
                        subject,
                        storageCell,
                        1,
                        out fixtureStock,
                        out countedStockBefore,
                        out stockFailure))
                {
                    r.Skip(spendingLabel, stockFailure);
                }
                else
                {
                    target = countedStockBefore;
                    string failure = null;
                    try
                    {
                        loops.Enable(
                            cell,
                            Rot4.North,
                            subject.thingDef,
                            subject.stuffDef,
                            null);
                        loops.SetTargetCount(cell, target);
                        loops.RunPass();
                        blueprintBefore =
                            FindBlueprint(map, cell, subject.thingDef) != null;
                        countedStockBefore =
                            CountStoredTargetStock(map, subject.thingDef);
                        stockRemoved = DestroyStoredTargetStock(fixtureStock);
                        countedStockAfter = CountStoredTargetStock(map, subject.thingDef);
                        loops.RunPass();
                        blueprintAppeared =
                            FindBlueprint(map, cell, subject.thingDef) != null;
                    }
                    catch (Exception ex)
                    {
                        failure = $"{ex.GetType().Name}: {ex.Message}";
                    }

                    string detail = TargetDetail(
                        cell,
                        target,
                        countedStockAfter,
                        blueprintAppeared) +
                        $"; counted stock before {countedStockBefore}; stock removed " +
                        $"{(stockRemoved ? "yes" : "no")}; blueprint before " +
                        $"{(blueprintBefore ? "yes" : "no")}";
                    if (failure != null)
                    {
                        r.Check(false, spendingLabel, $"{detail}; {failure}");
                    }
                    else if (!stockRemoved)
                    {
                        r.Skip(
                            spendingLabel,
                            "the stored target fixture could not be removed cleanly");
                    }
                    else
                    {
                        r.Check(
                            !blueprintBefore &&
                            countedStockAfter < target &&
                            blueprintAppeared,
                            spendingLabel,
                            detail);
                    }
                }
            }
            finally
            {
                DestroyStoredTargetStock(fixtureStock);
                DestroyThingsInRect(map, storageRect);
                loops.Disable(cell);
                DestroyThingsInRect(map, buildRect);
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
            const int expectedTarget = 3;
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
            pausedSaved.SetTargetCount(expectedCell, expectedTarget);

            ProduceLoopMapComponent loaded = null;
            ProduceLoopMapComponent pausedLoaded = null;
            string failure = null;
            string pausedFailure = null;
            bool recordFound = false;
            bool pausedRecordFound = false;
            bool pausedLoadedPaused = false;
            bool pausedXmlHasPausedNode = false;
            bool pausedXmlHasTargetCountNode = false;
            bool oldXmlHasPausedNode = false;
            bool oldXmlHasTargetCountNode = false;
            bool loadedPaused = false;
            int pausedLoadedTargetCount = 0;
            int loadedTargetCount = 0;
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
                pausedXmlHasTargetCountNode =
                    File.ReadAllText(path).IndexOf(
                        "<targetCount", StringComparison.Ordinal) >= 0;

                Scribe.loader.InitLoading(path);
                Scribe_Deep.Look(ref pausedLoaded, "produceLoopMapComponent", map);
                Scribe.loader.FinalizeLoading();

                ProduceLoopRecord pausedRecord = pausedLoaded?.Find(expectedCell);
                pausedRecordFound = pausedRecord != null;
                if (pausedRecordFound)
                {
                    pausedLoadedPaused = pausedRecord.paused;
                    pausedLoadedTargetCount = pausedRecord.targetCount;
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
                oldXmlHasTargetCountNode =
                    File.ReadAllText(path).IndexOf(
                        "<targetCount", StringComparison.Ordinal) >= 0;

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
                    loadedTargetCount = record.targetCount;
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

            bool targetPersistenceOk = pausedFailure == null &&
                                       failure == null &&
                                       pausedRecordFound &&
                                       pausedLoadedTargetCount == expectedTarget &&
                                       pausedXmlHasTargetCountNode &&
                                       recordFound &&
                                       !oldXmlHasTargetCountNode &&
                                       loadedTargetCount == 0;
            string targetPersistenceDetail =
                $"cell {expectedCell}; target {expectedTarget}; counted stock n/a; " +
                $"blueprint appeared n/a; target loaded " +
                $"{pausedLoadedTargetCount}; target XML node present " +
                $"{(pausedXmlHasTargetCountNode ? "yes" : "no")}; old target XML " +
                $"node present {(oldXmlHasTargetCountNode ? "yes" : "no")}; " +
                $"old target loaded {loadedTargetCount}" +
                $"{(pausedFailure == null ? "" : $"; paused failure {pausedFailure}")}" +
                $"{(failure == null ? "" : $"; old-save failure {failure}")}";
            r.Check(
                targetPersistenceOk,
                "a target survives a save and a load, and an old record loads unlimited",
                targetPersistenceDetail);
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

        private static bool TryCreateTargetStorage(
            Map map,
            HashSet<IntVec3> reservedCells,
            List<Zone_Stockpile> testZones,
            out IntVec3 storageCell,
            out string failure)
        {
            storageCell = IntVec3.Invalid;
            failure = null;
            if (map == null || map.zoneManager == null)
            {
                failure = "the map has no zone manager for the target stock fixture";
                return false;
            }

            IntVec3 root = DropCellFinder.TradeDropSpot(map);
            if (root.IsValid)
            {
                foreach (IntVec3 candidate in GenRadial.RadialCellsAround(
                    root, 12f, useCenter: true))
                {
                    if (IsTargetStorageCellAvailable(map, candidate, reservedCells))
                    {
                        storageCell = candidate;
                        break;
                    }
                }
            }

            if (!storageCell.IsValid)
            {
                foreach (IntVec3 candidate in map.AllCells)
                {
                    if (IsTargetStorageCellAvailable(map, candidate, reservedCells))
                    {
                        storageCell = candidate;
                        break;
                    }
                }
            }

            if (!storageCell.IsValid)
            {
                failure = "no empty unzoned cell for the target stock fixture";
                return false;
            }

            Zone_Stockpile zone = new Zone_Stockpile(
                StorageSettingsPreset.DefaultStockpile, map.zoneManager);
            try
            {
                map.zoneManager.RegisterZone(zone);
                testZones.Add(zone);
                zone.AddCell(storageCell);
                return true;
            }
            catch (Exception ex)
            {
                failure = $"could not create target stock storage: {ex.Message}";
                return false;
            }
        }

        private static bool IsTargetStorageCellAvailable(
            Map map, IntVec3 candidate, HashSet<IntVec3> reservedCells)
        {
            return candidate.InBounds(map) &&
                   candidate.Standable(map) &&
                   (reservedCells == null || !reservedCells.Contains(candidate)) &&
                   map.zoneManager.ZoneAt(candidate) == null &&
                   IsCellEmpty(map, candidate);
        }

        private static bool TrySpawnStoredTargetStock(
            Map map,
            Subject subject,
            IntVec3 storageCell,
            int amount,
            out List<Thing> spawnedStock,
            out int countedStock,
            out string failure)
        {
            spawnedStock = new List<Thing>();
            countedStock = 0;
            failure = null;
            if (map == null || subject?.thingDef == null || !storageCell.IsValid || amount <= 0)
            {
                failure = "the target stock fixture inputs were unavailable";
                return false;
            }

            int before;
            try
            {
                before = CountStoredTargetStock(map, subject.thingDef);
            }
            catch (Exception ex)
            {
                failure = $"could not inspect target stock before spawning: {ex.Message}";
                return false;
            }

            for (int i = 0; i < amount; i++)
            {
                Thing original = null;
                Thing minified = null;
                try
                {
                    original = ThingMaker.MakeThing(subject.thingDef, subject.stuffDef);
                    minified = original.TryMakeMinified();
                    if (minified != null)
                    {
                        original = null;
                    }

                    if (minified == null ||
                        minified.GetInnerIfMinified()?.def != subject.thingDef)
                    {
                        failure = "the subject building could not be minified for storage";
                        return false;
                    }

                    Thing spawned = GenSpawn.Spawn(minified, storageCell, map);
                    if (spawned != null && !spawned.Destroyed)
                    {
                        spawnedStock.Add(spawned);
                    }

                    if (spawned == null ||
                        spawned.Destroyed ||
                        !(spawned is MinifiedThing) ||
                        !OrderValidator.IsAvailableColonyStock(spawned))
                    {
                        failure =
                            "the spawned subject building was not genuinely available in colony storage";
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    failure = $"could not create stored target stock: {ex.Message}";
                    return false;
                }
                finally
                {
                    if (original != null && !original.Destroyed)
                    {
                        original.Destroy(DestroyMode.Vanish);
                    }

                    if (minified != null &&
                        !minified.Destroyed &&
                        !spawnedStock.Contains(minified))
                    {
                        minified.Destroy(DestroyMode.Vanish);
                    }
                }
            }

            try
            {
                countedStock = CountStoredTargetStock(map, subject.thingDef);
            }
            catch (Exception ex)
            {
                failure = $"could not inspect target stock after spawning: {ex.Message}";
                return false;
            }

            if (countedStock - before < amount)
            {
                failure =
                    $"stored target stock increased by {countedStock - before}, expected {amount}";
                return false;
            }

            return true;
        }

        private static bool DestroyStoredTargetStock(List<Thing> spawnedStock)
        {
            bool removed = true;
            if (spawnedStock == null)
            {
                return true;
            }

            for (int i = 0; i < spawnedStock.Count; i++)
            {
                Thing thing = spawnedStock[i];
                if (thing == null || thing.Destroyed)
                {
                    continue;
                }

                try
                {
                    thing.Destroy(DestroyMode.Vanish);
                }
                catch
                {
                    removed = false;
                }
            }

            for (int i = 0; i < spawnedStock.Count; i++)
            {
                if (spawnedStock[i] != null && !spawnedStock[i].Destroyed)
                {
                    removed = false;
                }
            }

            return removed;
        }

        private static int CountStoredTargetStock(Map map, ThingDef thingDef)
        {
            if (map?.haulDestinationManager == null || thingDef == null)
            {
                return 0;
            }

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
                    if (inner?.def == thingDef)
                    {
                        count += inner.stackCount;
                    }
                }
            }

            return count;
        }

        private static string TargetDetail(
            IntVec3 cell, int target, int countedStock, bool blueprintAppeared)
        {
            return $"cell {cell}; target {target}; counted stock {countedStock}; " +
                   $"blueprint appeared {(blueprintAppeared ? "yes" : "no")}";
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

        private static bool TryPrepareInstalledUninstall(
            Map map,
            ProduceLoopMapComponent loops,
            Subject subject,
            IntVec3 cell,
            out Building building,
            out Designation uninstall,
            out string failure)
        {
            building = null;
            uninstall = null;
            failure = null;
            try
            {
                building = SpawnFinishedBuilding(map, subject, cell, Rot4.North);
                if (building == null)
                {
                    failure = "could not spawn the finished building fixture";
                    return false;
                }

                loops.Enable(cell, Rot4.North, subject.thingDef, subject.stuffDef, null);
                loops.RunPass();
                uninstall = map.designationManager.DesignationOn(
                    building,
                    DesignationDefOf.Uninstall);
                if (uninstall == null)
                {
                    failure =
                        "a real loop pass did not create an outstanding Uninstall designation";
                    return false;
                }

                if (!building.Spawned || building.Map != map || building.Position != cell)
                {
                    failure =
                        "the real loop pass created an Uninstall designation, but the building " +
                        "was not still spawned at the fixture cell";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                failure =
                    $"could not drive a real loop pass to the designated state: " +
                    $"{ex.GetType().Name}: {ex.Message}";
                return false;
            }
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

        private static void CleanupZones(List<Zone_Stockpile> testZones, Results r)
        {
            if (testZones == null)
            {
                return;
            }

            for (int i = testZones.Count - 1; i >= 0; i--)
            {
                Zone_Stockpile zone = testZones[i];
                if (zone == null)
                {
                    continue;
                }

                try
                {
                    zone.Delete(playSound: false);
                }
                catch (Exception ex)
                {
                    r.sb.AppendLine($"  CLEANUP EXCEPTION: {ex}");
                    r.failed++;
                }
            }

            testZones.Clear();
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

        private static void SkipTargetAssertions(Results r, string reason)
        {
            r.Skip("a met target stops the next cycle", reason);
            r.Skip("a target above stock still produces", reason);
            r.Skip("a zero target produces without limit", reason);
            r.Skip("spending stock below the target resumes production", reason);
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
            r.Skip("Pause cancels an outstanding Uninstall designation", reason);
            r.Skip("Stop does not cancel an outstanding Uninstall designation", reason);
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
            r.sb.AppendLine($"  {r.passed} passed, {r.failed} failed, {r.skipped} skipped.");
            return r.sb.ToString();
        }
    }
}
