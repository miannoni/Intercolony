using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Animal specification and procurement assertions. Every generated pawn is discarded in a
    /// finally block, including successful generation probes.
    /// </summary>
    public static class IntercolonyAnimalSelfTest
    {
        public static string Run(IntercolonyWorldComponent state, Map map)
        {
            StringBuilder sb = new StringBuilder();
            int passed = 0;
            int failed = 0;
            int skipped = 0;

            void Check(string name, bool ok, string detail = null)
            {
                if (ok)
                {
                    passed++;
                }
                else
                {
                    failed++;
                    sb.AppendLine($"  FAIL  {name}{(detail == null ? "" : " — " + detail)}");
                }
            }

            void Skip(string name, string reason)
            {
                skipped++;
                sb.AppendLine($"  SKIPPED  {name} — {reason}");
            }

            sb.AppendLine("Animal specification self-test");

            CheckSerializationRoundTrip(Check, Skip);
            CheckValidity(Check);
            CheckGoodsDiscriminators(Check);
            CheckRequestDialogContracts(Check, Skip, state);
            CheckPricing(Check, Skip);
            CheckGenerationAndDelivery(Check, Skip);

            List<Pawn> colonyAnimals = ColonyAnimals(map);
            CheckMatcher(Check, Skip, colonyAnimals);
            CheckEligibility(Check, Skip, state, map);
            CheckAnimalSalePath(Check, Skip, state, map);

            sb.AppendLine($"  {passed} passed, {failed} failed, {skipped} skipped");
            return sb.ToString();
        }

        private static void CheckRequestDialogContracts(
            Action<string, bool, string> check, Action<string, string> skip,
            IntercolonyWorldComponent state)
        {
            List<ThingDef> races = Dialog_CreateRequest.OfferableAnimalRaces();
            ThingDef humanlikeLeak = races.Find(r => r?.race?.Humanlike == true);
            check("request dialog offerable races exclude humanlikes", humanlikeLeak == null,
                humanlikeLeak?.defName);

            CheckUiExpressibleSpecifications(check, skip, races);
            ThingDef multiKindRace = races.Find(
                r => Dialog_CreateRequest.OfferableKinds(r).Count > 1);
            if (multiKindRace == null)
            {
                skip("multi-kind request starts with a valid exact kind",
                    "no offerable race has multiple pawn kinds");
            }
            else
            {
                AnimalSpec defaulted = new AnimalSpec();
                Dialog_CreateRequest.NormalizeAnimalSpec(multiKindRace, defaulted);
                bool validDefault = defaulted.TryValidateFor(
                    multiKindRace, requireKind: true, out string reason);
                check("multi-kind request starts with a valid exact kind",
                    defaulted.kind != null && validDefault,
                    reason);
            }

            LifeStageDef stageDef = new LifeStageDef { defName = "IntercolonyDialogStage" };
            ThingDef eggLayer = SyntheticAnimal("IntercolonyDialogEgg", stageDef, 1, 5f, eggLayer: true);
            ThingDef noGestation = SyntheticAnimal("IntercolonyDialogNoGestation", stageDef, 1, 0f, eggLayer: false);
            check("request dialog pregnancy is unavailable for egg-layers", !Dialog_CreateRequest.PregnancyOfferable(eggLayer, Gender.Female), null);
            check("request dialog pregnancy is unavailable without gestation", !Dialog_CreateRequest.PregnancyOfferable(noGestation, Gender.Female), null);

            ThingDef liveBearer = SyntheticAnimal(
                "IntercolonyDialogLiveBearer", stageDef, 1, 5f, eggLayer: false);
            AnimalSpec dependent = new AnimalSpec
            {
                gender = Gender.Male,
                pregnant = true,
                minGestationProgress = 0.5f
            };
            Dialog_CreateRequest.NormalizeAnimalSpec(liveBearer, dependent);
            check("request dialog clears pregnancy after sex changes to male",
                !dependent.pregnant.HasValue && !dependent.minGestationProgress.HasValue, null);

            dependent.gender = Gender.Female;
            dependent.pregnant = false;
            dependent.minGestationProgress = 0.5f;
            Dialog_CreateRequest.NormalizeAnimalSpec(liveBearer, dependent);
            check("request dialog clears gestation floor when pregnancy is not required",
                !dependent.minGestationProgress.HasValue, null);

            if (state == null)
            {
                skip("request factory animal round-trip", "no world component");
                skip("request factory goods remains unchanged", "no world component");
                return;
            }

            CheckGoodsRequestFactoryRegression(check, state);

            if (races.Count == 0)
            {
                skip("request factory animal round-trip", "no offerable animal race");
                return;
            }

            ThingDef raceForRequest = races[0];
            List<PawnKindDef> kinds = Dialog_CreateRequest.OfferableKinds(raceForRequest);
            if (kinds.Count == 0)
            {
                skip("request factory animal round-trip", "race has no pawn kind");
                return;
            }

            List<LifeStageDef> stages = Dialog_CreateRequest.UnambiguousLifeStages(raceForRequest);
            AnimalSpec animal = new AnimalSpec
            {
                kind = kinds[0],
                gender = Gender.Female,
                lifeStage = stages.Count > 0 ? stages[0] : null,
                minHealthFraction = 0.73f
            };
            if (Dialog_CreateRequest.PregnancyOfferable(raceForRequest, animal.gender))
            {
                animal.pregnant = true;
                animal.minGestationProgress = 0.42f;
            }
            Dialog_CreateRequest.NormalizeAnimalSpec(raceForRequest, animal);
            PurchaseRequest animalRequest = null;
            try
            {
                animalRequest = RfqService.CreateRequest(state, raceForRequest, null, 1, 1,
                    ProcurementFulfillmentPreference.Either, animal);
                AnimalSpec saved = animalRequest?.animalSpec;
                check("request factory round-trips the complete animal specification",
                    animalRequest?.thingDef == raceForRequest && saved != null &&
                    saved.kind == animal.kind && saved.gender == animal.gender &&
                    saved.lifeStage == animal.lifeStage && saved.pregnant == animal.pregnant &&
                    saved.minHealthFraction == animal.minHealthFraction &&
                    saved.minGestationProgress == animal.minGestationProgress,
                    saved?.Describe(raceForRequest));
            }
            finally
            {
                if (animalRequest != null) state.Requests.Remove(animalRequest);
            }
        }

        private static void CheckUiExpressibleSpecifications(
            Action<string, bool, string> check, Action<string, string> skip,
            List<ThingDef> races)
        {
            if (races.Count == 0)
            {
                skip("every request-dialog animal specification validates",
                    "no offerable animal race");
                return;
            }

            int checkedSpecifications = 0;
            string firstFailure = null;
            Gender?[] genders = { null, Gender.Male, Gender.Female };
            float?[] healthFloors = { null, 0f, 0.5f, 1f };
            float?[] gestationFloors = { null, 0f, 0.5f, 1f };

            foreach (ThingDef race in races)
            {
                List<LifeStageDef> stages = new List<LifeStageDef> { null };
                stages.AddRange(Dialog_CreateRequest.UnambiguousLifeStages(race));
                foreach (PawnKindDef kind in Dialog_CreateRequest.OfferableKinds(race))
                {
                    foreach (LifeStageDef stage in stages)
                    {
                        foreach (Gender? gender in genders)
                        {
                            bool?[] pregnancies = Dialog_CreateRequest.PregnancyOfferable(race, gender)
                                ? new bool?[] { null, false, true }
                                : new bool?[] { null };
                            foreach (bool? pregnant in pregnancies)
                            {
                                float?[] possibleGestationFloors = pregnant == true
                                    ? gestationFloors
                                    : new float?[] { null };
                                foreach (float? health in healthFloors)
                                {
                                    foreach (float? gestation in possibleGestationFloors)
                                    {
                                        AnimalSpec spec = new AnimalSpec
                                        {
                                            kind = kind,
                                            gender = gender,
                                            lifeStage = stage,
                                            pregnant = pregnant,
                                            minHealthFraction = health,
                                            minGestationProgress = gestation
                                        };
                                        checkedSpecifications++;
                                        if (!spec.TryValidateFor(
                                                race, requireKind: true, out string reason) &&
                                            firstFailure == null)
                                        {
                                            firstFailure = $"{race.defName}/{kind.defName}: {reason}";
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            check("every request-dialog animal specification validates",
                checkedSpecifications > 0 && firstFailure == null,
                firstFailure ?? $"{checkedSpecifications} combinations checked");
        }

        private static void CheckGoodsRequestFactoryRegression(
            Action<string, bool, string> check, IntercolonyWorldComponent state)
        {
            PurchaseRequest request = null;
            try
            {
                request = RfqService.CreateRequest(state, ThingDefOf.WoodLog, null, 3, 7);
                check("request factory goods remains unchanged",
                    request != null && request.thingDef == ThingDefOf.WoodLog &&
                    request.stuffDef == null && request.quantityRequested == 3 &&
                    request.desiredDays == 7 && request.IsOpen &&
                    request.fulfillmentPreference == ProcurementFulfillmentPreference.Either &&
                    request.animalSpec == null && !request.IsAnimalOrder,
                    request?.ToString());
            }
            finally
            {
                if (request != null)
                {
                    state.Requests.Remove(request);
                }
            }
        }

        private static void CheckGenerationAndDelivery(
            Action<string, bool, string> check, Action<string, string> skip)
        {
            CheckPregnancyCapabilityRefusal(check);
            CheckGenderDefinitionRefusal(check);
            CheckPartialAnimalRefundAccounting(check);

            if (!TryFindGenerationSpec(out ThingDef race, out AnimalSpec spec, out string unavailable))
            {
                skip("generated animal closes the specification matcher loop", unavailable);
                skip("generated animal sex", unavailable);
                skip("generated animal life stage", unavailable);
                skip("generated animal pregnancy", unavailable);
                skip("generated animal gestation floor", unavailable);
                skip("failed animal delivery leaves no world pawn", unavailable);
            }
            else
            {
                Pawn generated = null;
                try
                {
                    bool made = AnimalPurchaseUtility.TryGenerateAnimal(
                        race, spec, out generated, out string failure);
                    check("animal generation succeeds for a supported specification",
                        made && generated != null, failure);
                    if (made && generated != null)
                    {
                        Hediff_Pregnant pregnancy =
                            generated.health?.hediffSet?.GetFirstHediffOfDef(HediffDefOf.Pregnant)
                            as Hediff_Pregnant;
                        check("generated animal closes the specification matcher loop",
                            generated.kindDef == spec.kind &&
                            AnimalTradeUtility.Matches(generated, race, spec), generated.LabelShort);
                        check("generated animal sex matches the request",
                            generated.gender == Gender.Female, generated.gender.ToString());
                        check("generated animal life stage matches the race-relative request",
                            generated.ageTracker?.CurLifeStage == spec.lifeStage,
                            generated.ageTracker?.CurLifeStage?.defName);
                        check("generated animal pregnancy matches the request",
                            pregnancy != null, generated.LabelShort);
                        check("generated animal meets the gestation floor",
                            pregnancy != null && spec.minGestationProgress.HasValue &&
                            pregnancy.GestationProgress >= spec.minGestationProgress.Value,
                            pregnancy == null ? "no pregnancy" : pregnancy.GestationProgress.ToString("P1"));
                    }
                    else
                    {
                        const string blocked = "the supported generation probe failed";
                        skip("generated animal closes the specification matcher loop", blocked);
                        skip("generated animal sex", blocked);
                        skip("generated animal life stage", blocked);
                        skip("generated animal pregnancy", blocked);
                        skip("generated animal gestation floor", blocked);
                    }
                }
                finally
                {
                    AnimalPurchaseUtility.DiscardGeneratedPawn(generated);
                }

                CheckFailedDeliveryDoesNotLeak(check, race, spec);
            }

            CheckGoodsFulfilmentRegression(check, skip);
        }

        private static void CheckPregnancyCapabilityRefusal(
            Action<string, bool, string> check)
        {
            LifeStageDef stage = new LifeStageDef { defName = "IntercolonyGenerationGateStage" };
            ThingDef noGestation = SyntheticAnimal(
                "IntercolonyGenerationGateRace", stage, 1, -1f, eggLayer: false);
            PawnKindDef kind = new PawnKindDef
            {
                defName = "IntercolonyGenerationGateKind",
                race = noGestation
            };
            AnimalSpec spec = new AnimalSpec
            {
                kind = kind,
                gender = Gender.Female,
                pregnant = true
            };

            Pawn refused = null;
            try
            {
                bool generated = AnimalPurchaseUtility.TryGenerateAnimal(
                    noGestation, spec, out refused, out string reason);
                check("pregnancy-incapable race is refused before generation",
                    !generated && refused == null && reason != null &&
                    reason.Contains("gestation"), reason);
            }
            finally
            {
                AnimalPurchaseUtility.DiscardGeneratedPawn(refused);
            }
        }

        private static void CheckGenderDefinitionRefusal(
            Action<string, bool, string> check)
        {
            LifeStageDef stage = new LifeStageDef { defName = "IntercolonyGenderGateStage" };
            ThingDef genderless = SyntheticAnimal(
                "IntercolonyGenderGateRace", stage, 1, 5f, eggLayer: false);
            genderless.race.hasGenders = false;
            PawnKindDef kind = new PawnKindDef
            {
                defName = "IntercolonyGenderGateKind",
                race = genderless
            };
            AnimalSpec spec = new AnimalSpec
            {
                kind = kind,
                gender = Gender.Female,
                pregnant = false
            };

            Pawn refused = null;
            try
            {
                bool generated = AnimalPurchaseUtility.TryGenerateAnimal(
                    genderless, spec, out refused, out string reason);
                check("genderless race is refused before FixedGender can force the result",
                    !generated && refused == null && reason != null &&
                    reason.Contains("genderless"), reason);
            }
            finally
            {
                AnimalPurchaseUtility.DiscardGeneratedPawn(refused);
            }
        }

        private static void CheckPartialAnimalRefundAccounting(
            Action<string, bool, string> check)
        {
            PurchaseOrder animalRemainder = new PurchaseOrder
            {
                quantity = 2,
                unitPrice = 100f,
                paidSilver = 500,
                animalSpec = new AnimalSpec()
            };
            PurchaseOrder goodsRemainder = new PurchaseOrder
            {
                quantity = 2,
                unitPrice = 100f,
                paidSilver = 500
            };

            check("partial animal refund covers only the head still owed",
                PurchaseOrderService.RefundableSilver(animalRemainder) == 200,
                PurchaseOrderService.RefundableSilver(animalRemainder).ToString());
            check("goods refund accounting remains unchanged",
                PurchaseOrderService.RefundableSilver(goodsRemainder) == 500,
                PurchaseOrderService.RefundableSilver(goodsRemainder).ToString());
        }

        private static bool TryFindGenerationSpec(
            out ThingDef race, out AnimalSpec spec, out string reason)
        {
            foreach (PawnKindDef kind in DefDatabase<PawnKindDef>.AllDefsListForReading)
            {
                ThingDef candidate = kind?.race;
                RaceProperties properties = candidate?.race;
                if (properties == null || !properties.Animal || properties.Humanlike ||
                    !properties.hasGenders ||
                    (properties.forceGender != Gender.None && properties.forceGender != Gender.Female) ||
                    (kind.fixedGender.HasValue && kind.fixedGender.Value != Gender.Female) ||
                    properties.lifeStageAges == null || properties.lifeStageAges.Count == 0)
                {
                    continue;
                }

                LifeStageDef stage = null;
                for (int i = properties.lifeStageAges.Count - 1; i >= 0; i--)
                {
                    LifeStageDef candidateStage = properties.lifeStageAges[i]?.def;
                    if (candidateStage != null && !candidateStage.alwaysDowned &&
                        CountStage(properties.lifeStageAges, candidateStage) == 1)
                    {
                        stage = candidateStage;
                        break;
                    }
                }

                if (stage == null)
                {
                    continue;
                }

                AnimalSpec candidateSpec = new AnimalSpec
                {
                    kind = kind,
                    gender = Gender.Female,
                    lifeStage = stage,
                    pregnant = true,
                    minHealthFraction = 0f,
                    minGestationProgress = 0.35f
                };
                if (!candidateSpec.TryValidateFor(candidate, requireKind: true, out _))
                {
                    continue;
                }

                race = candidate;
                spec = candidateSpec;
                reason = null;
                return true;
            }

            race = null;
            spec = null;
            reason = "no loaded live-bearing female-capable animal kind with an unambiguous mobile life stage";
            return false;
        }

        private static void CheckFailedDeliveryDoesNotLeak(
            Action<string, bool, string> check, ThingDef race, AnimalSpec spec)
        {
            int before = Find.WorldPawns?.AllPawnsAliveOrDead?.Count ?? 0;
            Pawn pawn = null;
            string detail = null;
            bool failedAndDiscarded = false;
            try
            {
                if (!AnimalPurchaseUtility.TryGenerateAnimal(race, spec, out pawn, out detail))
                {
                    check("failed animal delivery leaves no world pawn", false, detail);
                    return;
                }

                // Deliberately exercise cleanup from the dangerous state: the generated animal
                // is registered as a kept world pawn, then delivery is forced to fail with no map.
                Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
                bool delivered = AnimalPurchaseUtility.TryDeliverToColony(
                    pawn, null, out _, out detail);
                failedAndDiscarded = !delivered && pawn.Discarded;
            }
            finally
            {
                AnimalPurchaseUtility.DiscardGeneratedPawn(pawn);
            }

            int after = Find.WorldPawns?.AllPawnsAliveOrDead?.Count ?? 0;
            check("failed animal delivery leaves no world pawn",
                failedAndDiscarded && after == before,
                $"world pawns {before} -> {after}; {detail}");
        }

        private static void CheckGoodsFulfilmentRegression(
            Action<string, bool, string> check, Action<string, string> skip)
        {
            Map deliveryMap = Find.AnyPlayerHomeMap;
            ThingDef def = ThingDefOf.WoodLog;
            if (deliveryMap == null || def == null)
            {
                skip("ordinary goods purchase still fulfils through the goods path",
                    deliveryMap == null ? "no player home map" : "WoodLog is unavailable");
                return;
            }

            Dictionary<Thing, int> originalStacks = new Dictionary<Thing, int>();
            int before = 0;
            foreach (Thing thing in deliveryMap.listerThings.ThingsOfDef(def))
            {
                originalStacks[thing] = thing.stackCount;
                before += thing.stackCount;
            }

            PurchaseOrder order = new PurchaseOrder
            {
                id = -93001,
                settlementId = -1,
                settlementName = "animal self-test supplier",
                thingDef = def,
                quantity = 3,
                paidSilver = 0,
                supplierDelivers = true,
                readyTick = GenTicks.TicksGame,
                status = PurchaseOrderStatus.Confirmed
            };

            int after = before;
            try
            {
                PurchaseOrderService.AdvanceOrders(new List<PurchaseOrder> { order });
                after = 0;
                foreach (Thing thing in deliveryMap.listerThings.ThingsOfDef(def))
                {
                    after += thing.stackCount;
                }

                check("ordinary goods purchase still fulfils through the goods path",
                    !order.IsAnimalOrder && order.status == PurchaseOrderStatus.Completed &&
                    after - before == 3,
                    $"status {order.status}, units {before} -> {after}");
            }
            finally
            {
                List<Thing> current = new List<Thing>(deliveryMap.listerThings.ThingsOfDef(def));
                foreach (Thing thing in current)
                {
                    if (originalStacks.TryGetValue(thing, out int originalCount))
                    {
                        thing.stackCount = originalCount;
                    }
                    else if (!thing.Destroyed)
                    {
                        thing.Destroy(DestroyMode.Vanish);
                    }
                }
            }
        }

        private static void CheckSerializationRoundTrip(
            Action<string, bool, string> check, Action<string, string> skip)
        {
            ThingDef race = null;
            PawnKindDef kind = null;
            foreach (ThingDef candidate in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (candidate?.race == null || !candidate.race.Animal || candidate.race.Humanlike ||
                    candidate.race.lifeStageAges == null || candidate.race.lifeStageAges.Count == 0)
                {
                    continue;
                }

                foreach (PawnKindDef candidateKind in DefDatabase<PawnKindDef>.AllDefsListForReading)
                {
                    if (candidateKind?.race == candidate)
                    {
                        race = candidate;
                        kind = candidateKind;
                        break;
                    }
                }

                if (kind != null)
                {
                    break;
                }
            }

            if (race == null || kind == null)
            {
                skip("AnimalSpec serialization round-trip", "no loaded animal race with a pawn kind");
                return;
            }

            LifeStageDef stage = race.race.lifeStageAges[0].def;
            AnimalSpecRoundTripProbe saved = new AnimalSpecRoundTripProbe
            {
                pregnantTrue = NewFullSpec(kind, stage, true, 0.8f, 0.35f),
                pregnantFalse = NewFullSpec(kind, stage, false, 0.6f, null),
                pregnantNull = NewFullSpec(kind, stage, null, null, null),
                allNull = new AnimalSpec()
            };

            string path = Path.Combine(
                Path.GetTempPath(), $"Intercolony-AnimalSpec-{Guid.NewGuid():N}.xml");
            AnimalSpecRoundTripProbe loaded = null;
            string failure = null;
            try
            {
                Scribe.saver.InitSaving(path, "intercolonyAnimalSpecTest");
                Scribe_Deep.Look(ref saved, "probe");
                Scribe.saver.FinalizeSaving();

                Scribe.loader.InitLoading(path);
                Scribe_Deep.Look(ref loaded, "probe");
                Scribe.loader.FinalizeLoading();
            }
            catch (Exception exception)
            {
                failure = $"{exception.GetType().Name}: {exception.Message}";
            }
            finally
            {
                Scribe.ForceStop();
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            check("AnimalSpec round-trips kind, gender, life stage and pregnant=true",
                failure == null && SameFullSpec(
                    loaded?.pregnantTrue, kind, stage, true, 0.8f, 0.35f), failure);
            check("AnimalSpec round-trips pregnant=false as a real state",
                failure == null && SameFullSpec(
                    loaded?.pregnantFalse, kind, stage, false, 0.6f, null), failure);
            check("AnimalSpec round-trips pregnant=null as don't care",
                failure == null && SameFullSpec(
                    loaded?.pregnantNull, kind, stage, null, null, null), failure);
            check("fully-null AnimalSpec stays fully null",
                failure == null && loaded?.allNull != null && loaded.allNull.kind == null &&
                !loaded.allNull.gender.HasValue && loaded.allNull.lifeStage == null &&
                !loaded.allNull.pregnant.HasValue && !loaded.allNull.minHealthFraction.HasValue &&
                !loaded.allNull.minGestationProgress.HasValue, failure);
            check("null animal floors are omitted from labels and descriptions",
                failure == null && loaded?.allNull != null &&
                !loaded.allNull.ShortLabel(race).Contains("health") &&
                !loaded.allNull.ShortLabel(race).Contains("gestation") &&
                !loaded.allNull.Describe(race).Contains("Minimum health") &&
                !loaded.allNull.Describe(race).Contains("Minimum gestation"), failure);
        }

        private static AnimalSpec NewFullSpec(
            PawnKindDef kind,
            LifeStageDef stage,
            bool? pregnant,
            float? minHealthFraction,
            float? minGestationProgress)
        {
            return new AnimalSpec
            {
                kind = kind,
                gender = Gender.Female,
                lifeStage = stage,
                pregnant = pregnant,
                minHealthFraction = minHealthFraction,
                minGestationProgress = minGestationProgress
            };
        }

        private static bool SameFullSpec(
            AnimalSpec spec,
            PawnKindDef kind,
            LifeStageDef stage,
            bool? pregnant,
            float? minHealthFraction,
            float? minGestationProgress)
        {
            return spec != null && spec.kind == kind && spec.gender == Gender.Female &&
                   spec.lifeStage == stage && spec.pregnant == pregnant &&
                   spec.minHealthFraction == minHealthFraction &&
                   spec.minGestationProgress == minGestationProgress;
        }

        private static void CheckValidity(Action<string, bool, string> check)
        {
            LifeStageDef present = new LifeStageDef { defName = "IntercolonyTestPresentStage" };
            LifeStageDef absent = new LifeStageDef { defName = "IntercolonyTestAbsentStage" };
            ThingDef race = SyntheticAnimal("IntercolonyTestRace", present, 1, 5f, eggLayer: false);
            ThingDef otherRace = SyntheticAnimal("IntercolonyTestOtherRace", present, 1, 5f, eggLayer: false);

            AnimalSpec wrongKind = new AnimalSpec
            {
                kind = new PawnKindDef { defName = "IntercolonyTestWrongKind", race = otherRace }
            };
            check("validity rejects a pawn kind belonging to another race",
                !wrongKind.IsValidFor(race), null);

            check("validity rejects a life stage absent from the race",
                !new AnimalSpec { lifeStage = absent }.IsValidFor(race), null);

            ThingDef duplicateRace = SyntheticAnimal(
                "IntercolonyTestDuplicateStageRace", present, 2, 5f, eggLayer: false);
            check("validity rejects a life stage occurring twice",
                !new AnimalSpec { lifeStage = present }.IsValidFor(duplicateRace), null);

            ThingDef eggLayer = SyntheticAnimal(
                "IntercolonyTestEggLayer", present, 1, 5f, eggLayer: true);
            check("validity rejects pregnancy requested for an egg-layer",
                !new AnimalSpec { pregnant = true }.IsValidFor(eggLayer), null);

            ThingDef noGestation = SyntheticAnimal(
                "IntercolonyTestNoGestation", present, 1, -1f, eggLayer: false);
            check("validity rejects pregnancy without configured gestation",
                !new AnimalSpec { pregnant = true }.IsValidFor(noGestation), null);

            check("validity rejects a health floor below zero",
                !new AnimalSpec { minHealthFraction = -0.01f }.IsValidFor(race), null);
            check("validity rejects a health floor above one",
                !new AnimalSpec { minHealthFraction = 1.01f }.IsValidFor(race), null);
            check("validity rejects a gestation floor outside zero to one",
                !new AnimalSpec { pregnant = true, minGestationProgress = 1.01f }.IsValidFor(race), null);
            check("validity rejects a gestation floor unless pregnancy is required",
                !new AnimalSpec { minGestationProgress = 0.25f }.IsValidFor(race) &&
                !new AnimalSpec { pregnant = false, minGestationProgress = 0.25f }.IsValidFor(race), null);

            PurchaseRequest generationRequest = new PurchaseRequest
            {
                thingDef = race,
                quantityRequested = 1,
                animalSpec = new AnimalSpec()
            };
            check("buy-side request requires an exact pawn kind",
                !generationRequest.TryValidateAfterLoad(out _), null);
        }

        private static ThingDef SyntheticAnimal(
            string defName, LifeStageDef stage, int occurrences, float gestationDays, bool eggLayer)
        {
            RaceProperties properties = new RaceProperties
            {
                gestationPeriodDays = gestationDays,
                lifeStageAges = new List<LifeStageAge>()
            };
            for (int i = 0; i < occurrences; i++)
            {
                properties.lifeStageAges.Add(new LifeStageAge { def = stage, minAge = i });
            }

            ThingDef race = new ThingDef { defName = defName, race = properties };
            if (eggLayer)
            {
                race.comps.Add(new CompProperties_EggLayer());
            }

            return race;
        }

        private static void CheckGoodsDiscriminators(Action<string, bool, string> check)
        {
            check("goods OrderLine is not an animal order", !new OrderLine().IsAnimalOrder, null);
            check("goods PurchaseRequest is not an animal order", !new PurchaseRequest().IsAnimalOrder, null);
            check("goods Quotation is not an animal order", !new Quotation().IsAnimalOrder, null);
            check("goods PurchaseOrder is not an animal order", !new PurchaseOrder().IsAnimalOrder, null);
            check("animal purchase order cannot enter MakeGoods",
                PurchaseOrderService.MakeGoods(new PurchaseOrder
                {
                    thingDef = ThingDefOf.WoodLog,
                    quantity = 1,
                    animalSpec = new AnimalSpec()
                }).Count == 0, null);
        }

        private static void CheckPricing(
            Action<string, bool, string> check, Action<string, string> skip)
        {
            ThingDef race = null;
            LifeStageDef adultStage = DefDatabase<LifeStageDef>.GetNamedSilentFail("AnimalAdult");
            float minimumStageFactor = 0f;
            foreach (ThingDef candidate in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                List<LifeStageAge> stages = candidate?.race?.lifeStageAges;
                if (candidate?.race == null || !candidate.race.Animal || candidate.race.Humanlike ||
                    candidate.BaseMarketValue <= 0f || candidate.race.gestationPeriodDays <= 0f ||
                    candidate.HasComp<CompEggLayer>() || stages == null || stages.Count == 0 ||
                    adultStage == null || CountStage(stages, adultStage) != 1)
                {
                    continue;
                }

                float minimum = float.MaxValue;
                foreach (LifeStageAge entry in stages)
                {
                    if (entry?.def != null)
                    {
                        minimum = Mathf.Min(minimum, entry.def.marketValueFactor);
                    }
                }

                if (minimum == float.MaxValue)
                {
                    continue;
                }

                race = candidate;
                minimumStageFactor = minimum;
                break;
            }

            if (race == null)
            {
                skip("animal pricing arithmetic", "no loaded positive-value live-bearing animal with the Core AnimalAdult stage");
            }
            else
            {
                float expectedUnspecified = race.BaseMarketValue * minimumStageFactor;
                float actualUnspecified = IntercolonyPricing.BaseValue(
                    race, null, new AnimalSpec());
                check("unspecified-stage animal price uses the race's minimum stage factor",
                    actualUnspecified == expectedUnspecified,
                    $"{race.defName}: expected {expectedUnspecified:R}, actual {actualUnspecified:R}");

                AnimalSpec adultFemale = new AnimalSpec
                {
                    lifeStage = adultStage,
                    gender = Gender.Female
                };
                float expectedAdultFemale =
                    race.BaseMarketValue * adultStage.marketValueFactor * 1.20f;
                float actualAdultFemale = IntercolonyPricing.BaseValue(race, null, adultFemale);
                check("adult female animal price exactly applies the 1.20 breeding premium",
                    actualAdultFemale == expectedAdultFemale,
                    $"{race.defName}: expected {expectedAdultFemale:R}, actual {actualAdultFemale:R}");

                AnimalSpec adultFemalePregnant = adultFemale.Copy();
                adultFemalePregnant.pregnant = true;
                float expectedAdultFemalePregnant =
                    race.BaseMarketValue * adultStage.marketValueFactor * 1.20f * 1.40f;
                float actualAdultFemalePregnant = IntercolonyPricing.BaseValue(
                    race, null, adultFemalePregnant);
                check("adult female pregnant animal price exactly applies 1.20 then 1.40",
                    actualAdultFemalePregnant == expectedAdultFemalePregnant,
                    $"{race.defName}: expected {expectedAdultFemalePregnant:R}, actual {actualAdultFemalePregnant:R}");

                SettlementEconomicProfile animalProfile = FixedPricingProfile();
                float animalUnitPrice = IntercolonyPricing.UnitPrice(
                    race,
                    ThingDefOf.Steel,
                    adultFemalePregnant,
                    1,
                    animalProfile,
                    IntercolonyProductCategory.Commodities,
                    -1f,
                    QualityCategory.Legendary,
                    out List<PriceFactor> animalFactors);
                string explanation = IntercolonyPricing.Explain(
                    race, ThingDefOf.Steel, adultFemalePregnant, 1, animalUnitPrice, animalFactors);
                check("animal explanation names species, female and pregnancy factors",
                    explanation.Contains("Species base") &&
                    explanation.Contains("Sex (female)") &&
                    explanation.Contains("Pregnancy required") &&
                    !explanation.Contains("Legendary") && !explanation.Contains("Steel"),
                    explanation);
            }

            CheckExactGoodsPriceRegression(check);
        }

        private static void CheckExactGoodsPriceRegression(Action<string, bool, string> check)
        {
            const int quantity = 100;
            SettlementEconomicProfile profile = FixedPricingProfile();
            IntercolonyProductCategory category = IntercolonyProductCategory.Commodities;
            float previousDifficulty = IntercolonyMod.Settings.economyDifficulty;
            try
            {
                IntercolonyMod.Settings.economyDifficulty = 1f;

                // This is the pre-animal goods formula, repeated deliberately rather than
                // reconstructed from the returned factors. Bit equality protects its operation
                // order as well as its numerical result.
                float expected = ThingDefOf.Steel.BaseMarketValue;
                expected *= Mathf.Clamp(profile.DemandFor(ThingDefOf.Steel, category), 0.4f, 2f);
                expected *= 0.95f;
                expected *= Mathf.Lerp(1.22f, 0.96f, Mathf.Clamp01(quantity / 2000f));
                expected *= 1f;
                expected = Mathf.Max(0.01f, expected);

                float actual = IntercolonyPricing.UnitPrice(
                    ThingDefOf.Steel, null, quantity, profile, category, -1f, null, out _);
                int expectedBits = BitConverter.ToInt32(BitConverter.GetBytes(expected), 0);
                int actualBits = BitConverter.ToInt32(BitConverter.GetBytes(actual), 0);
                check("goods price is bit-for-bit unchanged from the pre-animal formula",
                    actualBits == expectedBits,
                    $"expected {expected:R} (0x{expectedBits:X8}), actual {actual:R} (0x{actualBits:X8})");
            }
            finally
            {
                IntercolonyMod.Settings.economyDifficulty = previousDifficulty;
            }
        }

        private static SettlementEconomicProfile FixedPricingProfile()
        {
            SettlementEconomicProfile profile = new SettlementEconomicProfile
            {
                seed = 12345,
                wealthTier = IntercolonyWealthTier.Modest,
                qualityPreference = 1f
            };
            profile.demandWeights[(int)IntercolonyProductCategory.Commodities] = 1.1f;
            return profile;
        }

        private static void CheckMatcher(
            Action<string, bool, string> check, Action<string, string> skip, List<Pawn> animals)
        {
            if (animals.Count == 0)
            {
                skip("matcher assertions", "the current colony has no spawned colony animal");
                return;
            }

            Pawn pawn = animals[0];
            AnimalSpec unconstrained = new AnimalSpec();
            check("matcher accepts the correct species with null constraints",
                AnimalTradeUtility.Matches(pawn, pawn.def, unconstrained), pawn.LabelShort);

            LifeStageDef syntheticStage = new LifeStageDef { defName = "IntercolonyMatcherStage" };
            ThingDef wrongRace = SyntheticAnimal(
                "IntercolonyMatcherWrongRace", syntheticStage, 1, 5f, eggLayer: false);
            check("matcher rejects the wrong species",
                !AnimalTradeUtility.Matches(pawn, wrongRace, new AnimalSpec()), pawn.LabelShort);

            Pawn gendered = animals.Find(p => p.gender == Gender.Female || p.gender == Gender.Male);
            if (gendered == null)
            {
                skip("gender matcher", "no current colony animal has male or female gender");
            }
            else
            {
                Gender opposite = gendered.gender == Gender.Female ? Gender.Male : Gender.Female;
                check("gender constraint independently accepts the matching sex",
                    AnimalTradeUtility.Matches(
                        gendered, gendered.def, new AnimalSpec { gender = gendered.gender }),
                    gendered.LabelShort);
                check("gender constraint independently rejects the other sex",
                    !AnimalTradeUtility.Matches(
                        gendered, gendered.def, new AnimalSpec { gender = opposite }),
                    gendered.LabelShort);
            }

            Pawn staged = null;
            LifeStageDef otherStage = null;
            foreach (Pawn candidate in animals)
            {
                LifeStageDef current = candidate.ageTracker?.CurLifeStage;
                List<LifeStageAge> stages = candidate.RaceProps?.lifeStageAges;
                if (current == null || stages == null)
                {
                    continue;
                }

                foreach (LifeStageAge entry in stages)
                {
                    if (entry?.def != null && entry.def != current &&
                        CountStage(stages, entry.def) == 1)
                    {
                        staged = candidate;
                        otherStage = entry.def;
                        break;
                    }
                }

                if (staged != null) break;
            }

            if (staged == null)
            {
                skip("life-stage matcher", "no colony animal race has a second unambiguous life stage");
            }
            else
            {
                check("life-stage constraint independently accepts the current race stage",
                    AnimalTradeUtility.Matches(
                        staged, staged.def, new AnimalSpec { lifeStage = staged.ageTracker.CurLifeStage }),
                    staged.LabelShort);
                check("life-stage constraint independently rejects another race stage",
                    !AnimalTradeUtility.Matches(
                        staged, staged.def, new AnimalSpec { lifeStage = otherStage }),
                    staged.LabelShort);
            }

            Pawn injured = animals.Find(p =>
                p.health?.summaryHealth != null &&
                p.health.summaryHealth.SummaryHealthPercent < 1f);
            if (injured == null)
            {
                skip("minimum-health matcher", "no current colony animal has summary health below 100%");
            }
            else
            {
                float currentHealth = injured.health.summaryHealth.SummaryHealthPercent;
                float requiredHealth = Mathf.Min(1f, currentHealth + 0.01f);
                check("minimum-health constraint rejects an animal below the floor",
                    !AnimalTradeUtility.Matches(
                        injured, injured.def, new AnimalSpec { minHealthFraction = requiredHealth }),
                    $"{injured.LabelShort}: {currentHealth:P1} below {requiredHealth:P1}");
            }

            Pawn pregnancyCapable = animals.Find(p =>
                new AnimalSpec { pregnant = true }.IsValidFor(p.def));
            if (pregnancyCapable == null)
            {
                skip("pregnancy matcher", "no current colony animal has a live-bearing race");
            }
            else
            {
                bool current = pregnancyCapable.health?.hediffSet?.HasHediff(HediffDefOf.Pregnant) == true;
                check("pregnancy constraint independently accepts the pawn's current state",
                    AnimalTradeUtility.Matches(
                        pregnancyCapable, pregnancyCapable.def, new AnimalSpec { pregnant = current }),
                    pregnancyCapable.LabelShort);
                check("pregnancy constraint independently rejects the opposite state",
                    !AnimalTradeUtility.Matches(
                        pregnancyCapable, pregnancyCapable.def, new AnimalSpec { pregnant = !current }),
                    pregnancyCapable.LabelShort);
            }

            Pawn pregnantBelowFloor = null;
            Hediff_Pregnant pregnancy = null;
            foreach (Pawn candidate in animals)
            {
                Hediff_Pregnant candidatePregnancy =
                    candidate.health?.hediffSet?.GetFirstHediffOfDef(HediffDefOf.Pregnant)
                    as Hediff_Pregnant;
                if (candidatePregnancy != null && candidatePregnancy.GestationProgress < 1f &&
                    new AnimalSpec { pregnant = true }.IsValidFor(candidate.def))
                {
                    pregnantBelowFloor = candidate;
                    pregnancy = candidatePregnancy;
                    break;
                }
            }

            if (pregnantBelowFloor == null)
            {
                skip("minimum-gestation matcher", "no current colony animal is pregnant below 100% gestation");
            }
            else
            {
                float requiredProgress = Mathf.Min(1f, pregnancy.GestationProgress + 0.01f);
                check("minimum-gestation constraint rejects an animal below the floor",
                    !AnimalTradeUtility.Matches(
                        pregnantBelowFloor,
                        pregnantBelowFloor.def,
                        new AnimalSpec
                        {
                            pregnant = true,
                            minGestationProgress = requiredProgress
                        }),
                    $"{pregnantBelowFloor.LabelShort}: {pregnancy.GestationProgress:P1} below {requiredProgress:P1}");
            }

            Pawn pairA = null;
            Pawn pairB = null;
            for (int i = 0; i < animals.Count && pairA == null; i++)
            {
                for (int j = i + 1; j < animals.Count; j++)
                {
                    if (animals[i].def == animals[j].def &&
                        (animals[i].gender != animals[j].gender ||
                         IsPregnant(animals[i]) != IsPregnant(animals[j])))
                    {
                        pairA = animals[i];
                        pairB = animals[j];
                        break;
                    }
                }
            }

            if (pairA == null)
            {
                skip("null constraints match both ways",
                    "no same-species colony-animal pair differs in sex or pregnancy");
            }
            else
            {
                AnimalSpec dontCare = new AnimalSpec();
                check("null sex/stage/pregnancy constraints match both differing pawns",
                    AnimalTradeUtility.Matches(pairA, pairA.def, dontCare) &&
                    AnimalTradeUtility.Matches(pairB, pairB.def, dontCare),
                    $"{pairA.LabelShort}, {pairB.LabelShort}");
            }
        }

        private static int CountStage(List<LifeStageAge> stages, LifeStageDef stage)
        {
            int count = 0;
            foreach (LifeStageAge entry in stages)
            {
                if (entry?.def == stage) count++;
            }

            return count;
        }

        private static bool IsPregnant(Pawn pawn)
        {
            return pawn.health?.hediffSet?.HasHediff(HediffDefOf.Pregnant) == true;
        }

        private static void CheckEligibility(
            Action<string, bool, string> check, Action<string, string> skip,
            IntercolonyWorldComponent state, Map map)
        {
            Pawn humanlike = null;
            if (map?.mapPawns?.AllPawnsSpawned != null)
            {
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (pawn?.RaceProps?.Humanlike == true)
                    {
                        humanlike = pawn;
                        break;
                    }
                }
            }
            if (humanlike == null)
            {
                skip("humanlike eligibility rejection", "no spawned humanlike pawn is present");
            }
            else
            {
                bool eligible = AnimalTradeUtility.TryValidateSaleEligibility(
                    humanlike, out string reason);
                check("eligibility rejects a humanlike at the first gate",
                    !eligible && reason == "humanlike", $"{humanlike.LabelShort}: {reason}");
            }

            Pawn employee = null;
            foreach (EmploymentContract contract in state.Employments)
            {
                if (contract.status == EmploymentStatus.Active && contract.pawn != null)
                {
                    employee = contract.pawn;
                    break;
                }
            }

            if (employee == null)
            {
                skip("employee eligibility rejection", "no active Intercolony employee is present");
            }
            else
            {
                check("eligibility rejects an active Intercolony employee",
                    EmploymentService.IsEmployee(employee) &&
                    !AnimalTradeUtility.IsEligibleForSale(employee), employee.LabelShort);
            }
        }

        private static void CheckAnimalSalePath(
            Action<string, bool, string> check, Action<string, string> skip,
            IntercolonyWorldComponent state, Map map)
        {
            CheckDiscoveryExclusions(check, skip, state, map);
            CheckAnonymousAnimalCommitment(check, skip, state, map);
            CheckCaravanHandoffRevalidation(check, skip);
            CheckBondSaleEffects(check, skip, map);
            CheckGoodsCaravanValidationUnchanged(check, skip);

            // This exact arithmetic regression predates the animal sell path. Keeping it in
            // the sell test cluster makes the "goods unchanged" requirement explicit.
            CheckExactGoodsPriceRegression(check);
        }

        private static void CheckGoodsCaravanValidationUnchanged(
            Action<string, bool, string> check, Action<string, string> skip)
        {
            List<Caravan> caravans = Find.WorldObjects?.Caravans;
            if (caravans != null)
            {
                foreach (Caravan caravan in caravans)
                {
                    if (!caravan.IsPlayerControlled)
                    {
                        continue;
                    }

                    foreach (Thing thing in CaravanInventoryUtility.AllInventoryItems(caravan))
                    {
                        Thing inner = thing.GetInnerIfMinified();
                        if (inner?.def == null ||
                            !IntercolonyProductClassifier.IsFungibleTradeItem(inner.def))
                        {
                            continue;
                        }

                        SalesOrder goods = new SalesOrder
                        {
                            line = new OrderLine(inner.def, 1),
                            status = SalesOrderStatus.Accepted
                        };
                        int stackCountBefore = thing.stackCount;
                        OrderValidationResult validation =
                            OrderValidator.ValidateCaravan(goods, caravan);
                        check("goods caravan selling validation is unchanged",
                            !goods.IsAnimalOrder && validation.matchedQuantity == 1 &&
                            thing.stackCount == stackCountBefore,
                            inner.LabelShort);
                        return;
                    }
                }
            }

            skip("goods caravan selling validation is unchanged",
                "no player caravan currently carries a fungible trade item");
        }

        private static void CheckDiscoveryExclusions(
            Action<string, bool, string> check, Action<string, string> skip,
            IntercolonyWorldComponent state, Map map)
        {
            List<Pawn> discovered = FindBuyerService.EligibleColonyAnimalCandidates(map);
            List<Pawn> realPawns = new List<Pawn>();
            if (map?.mapPawns?.AllPawnsSpawned != null)
            {
                realPawns.AddRange(map.mapPawns.AllPawnsSpawned);
            }

            CheckRealExcludedPawn("humanlike", realPawns.Find(p => p?.RaceProps?.Humanlike == true),
                discovered, check, skip);
            CheckRealExcludedPawn("prisoner", realPawns.Find(p => p?.IsPrisoner == true),
                discovered, check, skip);
            CheckRealExcludedPawn("slave", realPawns.Find(p => p?.IsSlave == true),
                discovered, check, skip);
            CheckRealExcludedPawn("quest lodger", realPawns.Find(p => p?.IsQuestLodger() == true),
                discovered, check, skip);

            Pawn employee = null;
            if (state != null)
            {
                employee = realPawns.Find(EmploymentService.IsEmployee);
            }
            CheckRealExcludedPawn("employee", employee, discovered, check, skip);
        }

        private static void CheckRealExcludedPawn(
            string kind, Pawn pawn, List<Pawn> discovered,
            Action<string, bool, string> check, Action<string, string> skip)
        {
            string assertion = $"animal discovery excludes a real {kind}";
            if (pawn == null)
            {
                skip(assertion, $"no real {kind} is present on the current colony map");
                return;
            }

            check(assertion, !discovered.Contains(pawn), pawn.LabelShort);
        }

        private static void CheckAnonymousAnimalCommitment(
            Action<string, bool, string> check, Action<string, string> skip,
            IntercolonyWorldComponent state, Map map)
        {
            if (state == null || map == null)
            {
                skip("committed animal is not offered twice", "no world state or colony map");
                return;
            }

            List<AnimalStockGroup> groups = FindBuyerService.ColonyAnimals(map);
            AnimalStockGroup group = groups.Find(g =>
                FindBuyerService.AvailableAnimalQuantity(
                    state, map, g.race, g.spec) > 0);
            if (group == null)
            {
                skip("committed animal is not offered twice",
                    "no eligible uncommitted colony-animal group");
                return;
            }

            int before = FindBuyerService.AvailableAnimalQuantity(
                state, map, group.race, group.spec);
            SalesOrder planted = new SalesOrder
            {
                id = -917_401,
                opportunityId = 0,
                contractId = 0,
                line = new OrderLine(group.race, 1)
                {
                    animalSpec = group.spec.Copy()
                },
                status = SalesOrderStatus.Accepted,
                fulfillment = FulfillmentMode.SellerDelivery
            };

            state.Orders.Add(planted);
            try
            {
                int after = FindBuyerService.AvailableAnimalQuantity(
                    state, map, group.race, group.spec);
                check("committed animal is not offered twice",
                    after == Mathf.Max(0, before - 1),
                    $"{group.spec.ShortLabel(group.race)}: {before} before, {after} after");
            }
            finally
            {
                state.Orders.Remove(planted);
            }
        }

        private static void CheckCaravanHandoffRevalidation(
            Action<string, bool, string> check, Action<string, string> skip)
        {
            Caravan caravan = null;
            Pawn animal = null;
            List<Caravan> caravans = Find.WorldObjects?.Caravans;
            if (caravans != null)
            {
                foreach (Caravan candidateCaravan in caravans)
                {
                    if (!candidateCaravan.IsPlayerControlled)
                    {
                        continue;
                    }

                    animal = candidateCaravan.PawnsListForReading.Find(p =>
                        AnimalTradeUtility.IsEligibleForSale(p) &&
                        (p.gender == Gender.Female || p.gender == Gender.Male));
                    if (animal != null)
                    {
                        caravan = candidateCaravan;
                        break;
                    }
                }
            }

            if (animal == null)
            {
                skip("handoff revalidation rejects a changed animal",
                    "no eligible gendered animal is travelling in a player caravan");
                return;
            }

            Gender originalGender = animal.gender;
            SalesOrder order = new SalesOrder
            {
                line = new OrderLine(animal.def, 1)
                {
                    animalSpec = new AnimalSpec { gender = originalGender }
                },
                status = SalesOrderStatus.Accepted
            };

            try
            {
                OrderValidationResult before = OrderValidator.ValidateCaravan(order, caravan);
                animal.gender = originalGender == Gender.Female ? Gender.Male : Gender.Female;
                OrderValidationResult changed = OrderValidator.ValidateCaravan(order, caravan);
                check("handoff revalidation rejects a changed animal",
                    before.matchedQuantity == 1 && changed.matchedQuantity == 0,
                    animal.LabelShort);
            }
            finally
            {
                animal.gender = originalGender;
            }
        }

        private static void CheckBondSaleEffects(
            Action<string, bool, string> check, Action<string, string> skip, Map map)
        {
            Pawn animal = null;
            Pawn colonist = null;
            Pawn negotiator = null;
            List<Pawn> candidates = FindBuyerService.EligibleColonyAnimalCandidates(map);
            List<Caravan> caravans = Find.WorldObjects?.Caravans;
            if (caravans != null)
            {
                foreach (Caravan caravan in caravans)
                {
                    if (!caravan.IsPlayerControlled)
                    {
                        continue;
                    }

                    if (negotiator == null)
                    {
                        negotiator = SalesOrderService.FindAnimalSaleNegotiator(caravan);
                    }

                    candidates.AddRange(caravan.PawnsListForReading.FindAll(
                        AnimalTradeUtility.IsEligibleForSale));
                }
            }

            foreach (Pawn candidate in candidates)
            {
                List<DirectPawnRelation> relations = candidate.relations?.DirectRelations;
                if (relations == null)
                {
                    continue;
                }

                DirectPawnRelation bond = relations.Find(r =>
                    r?.def == PawnRelationDefOf.Bond && r.otherPawn?.IsColonist == true &&
                    r.otherPawn.needs?.mood?.thoughts?.memories != null &&
                    r.otherPawn.GetMostImportantRelation(candidate) == PawnRelationDefOf.Bond);
                if (bond != null)
                {
                    animal = candidate;
                    colonist = bond.otherPawn;
                    break;
                }
            }

            if (negotiator == null && map?.mapPawns?.AllPawnsSpawned != null)
            {
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (pawn?.IsColonist == true && pawn.Faction == Faction.OfPlayer &&
                        pawn.RaceProps?.Humanlike == true && !pawn.Downed && !pawn.InMentalState)
                    {
                        negotiator = pawn;
                        break;
                    }
                }
            }

            if (animal == null || negotiator == null)
            {
                skip("bond is removed and sold thoughts are applied",
                    animal == null
                        ? "the colony has no eligible bonded pair with a mood-capable colonist"
                        : "no valid player negotiator is present");
                return;
            }

            MemoryThoughtHandler memories = colonist.needs.mood.thoughts.memories;
            List<Thought_Memory> beforeMemories = new List<Thought_Memory>(memories.Memories);
            bool bondRemoved = false;
            bool allThoughtsApplied = false;
            try
            {
                // This is the exact vanilla relation notification reached by
                // Pawn.PreTraded(PlayerSells). It is isolated here so the self-test never
                // removes, destroys, discards or faction-clears a real colony animal.
                animal.relations.Notify_PawnSold(negotiator);
                bondRemoved = !animal.relations.DirectRelationExists(
                    PawnRelationDefOf.Bond, colonist);

                allThoughtsApplied = true;
                foreach (ThoughtDef thought in PawnRelationDefOf.Bond.soldThoughts)
                {
                    if (!memories.Memories.Exists(m =>
                            !beforeMemories.Contains(m) && m.def == thought &&
                            m.otherPawn == negotiator))
                    {
                        allThoughtsApplied = false;
                        break;
                    }
                }

                check("bond is removed and sold thoughts are applied",
                    bondRemoved && allThoughtsApplied,
                    $"{animal.LabelShort} bonded to {colonist.LabelShort}");
            }
            finally
            {
                List<Thought_Memory> added = memories.Memories.FindAll(
                    memory => !beforeMemories.Contains(memory));
                foreach (Thought_Memory memory in added)
                {
                    memories.RemoveMemory(memory);
                }

                if (!animal.relations.DirectRelationExists(PawnRelationDefOf.Bond, colonist))
                {
                    animal.relations.AddDirectRelation(PawnRelationDefOf.Bond, colonist);
                }
            }
        }

        private static List<Pawn> ColonyAnimals(Map map)
        {
            List<Pawn> result = new List<Pawn>();
            if (map?.mapPawns?.AllPawnsSpawned == null)
            {
                return result;
            }

            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn?.IsColonyAnimal == true)
                {
                    result.Add(pawn);
                }
            }

            return result;
        }

        public class AnimalSpecRoundTripProbe : IExposable
        {
            public AnimalSpec pregnantTrue;
            public AnimalSpec pregnantFalse;
            public AnimalSpec pregnantNull;
            public AnimalSpec allNull;

            public void ExposeData()
            {
                Scribe_Deep.Look(ref pregnantTrue, "pregnantTrue");
                Scribe_Deep.Look(ref pregnantFalse, "pregnantFalse");
                Scribe_Deep.Look(ref pregnantNull, "pregnantNull");
                Scribe_Deep.Look(ref allNull, "allNull");
            }
        }
    }
}
