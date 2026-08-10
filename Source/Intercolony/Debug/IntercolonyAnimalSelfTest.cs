using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RimWorld;
using Verse;

namespace Intercolony
{
    /// <summary>Pure animal-spec assertions. No pawn or world record is created or changed.</summary>
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

            List<Pawn> colonyAnimals = ColonyAnimals(map);
            CheckMatcher(Check, Skip, colonyAnimals);
            CheckEligibility(Check, Skip, state, map);

            sb.AppendLine($"  {passed} passed, {failed} failed, {skipped} skipped");
            return sb.ToString();
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
                pregnantTrue = NewFullSpec(kind, stage, true),
                pregnantFalse = NewFullSpec(kind, stage, false),
                pregnantNull = NewFullSpec(kind, stage, null),
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
                failure == null && SameFullSpec(loaded?.pregnantTrue, kind, stage, true), failure);
            check("AnimalSpec round-trips pregnant=false as a real state",
                failure == null && SameFullSpec(loaded?.pregnantFalse, kind, stage, false), failure);
            check("AnimalSpec round-trips pregnant=null as don't care",
                failure == null && SameFullSpec(loaded?.pregnantNull, kind, stage, null), failure);
            check("fully-null AnimalSpec stays fully null",
                failure == null && loaded?.allNull != null && loaded.allNull.kind == null &&
                !loaded.allNull.gender.HasValue && loaded.allNull.lifeStage == null &&
                !loaded.allNull.pregnant.HasValue, failure);
        }

        private static AnimalSpec NewFullSpec(PawnKindDef kind, LifeStageDef stage, bool? pregnant)
        {
            return new AnimalSpec
            {
                kind = kind,
                gender = Gender.Female,
                lifeStage = stage,
                pregnant = pregnant
            };
        }

        private static bool SameFullSpec(
            AnimalSpec spec, PawnKindDef kind, LifeStageDef stage, bool? pregnant)
        {
            return spec != null && spec.kind == kind && spec.gender == Gender.Female &&
                   spec.lifeStage == stage && spec.pregnant == pregnant;
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
