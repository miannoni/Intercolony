using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Generates and hands off purchased animals. Every method which accepts ownership of a
    /// generated pawn either completes the handoff or discards that pawn before returning false.
    /// </summary>
    public static class AnimalPurchaseUtility
    {
        private const float FinalStageMinimumMarginYears = 0.1f;

        public static bool TryGenerateAnimal(
            ThingDef race, AnimalSpec spec, out Pawn pawn, out string reason)
        {
            pawn = null;
            if (spec == null)
            {
                reason = "missing animal specification";
                return false;
            }

            if (!spec.TryValidateFor(race, requireKind: true, out reason))
            {
                return false;
            }

            if (Faction.OfPlayer == null)
            {
                reason = "the player faction is unavailable";
                return false;
            }

            Gender? generationGender = spec.gender;
            if (spec.pregnant == true)
            {
                if (spec.gender.HasValue && spec.gender.Value != Gender.Female)
                {
                    reason = "pregnancy requires a female animal";
                    return false;
                }

                generationGender = Gender.Female;
            }

            if (!GenderRequestIsSupported(race.race, spec.kind, generationGender, out reason))
            {
                return false;
            }

            float? biologicalAge = null;
            DevelopmentalStage developmentalStage = DevelopmentalStage.Adult;
            if (spec.lifeStage != null)
            {
                if (!TryChooseBiologicalAge(race, spec.lifeStage, out float chosenAge, out reason))
                {
                    return false;
                }

                biologicalAge = chosenAge;
                developmentalStage = spec.lifeStage.developmentalStage;
            }

            try
            {
                PawnGenerationRequest request = new PawnGenerationRequest(
                    kind: spec.kind,
                    faction: Faction.OfPlayer,
                    context: PawnGenerationContext.NonPlayer,
                    forceGenerateNewPawn: true,
                    allowDead: false,
                    allowDowned: false,
                    canGeneratePawnRelations: false,
                    allowPregnant: false,
                    fixedBiologicalAge: biologicalAge,
                    fixedGender: generationGender,
                    developmentalStages: developmentalStage);

                pawn = PawnGenerator.GeneratePawn(request);
                if (pawn == null)
                {
                    reason = "PawnGenerator returned no pawn";
                    return false;
                }

                if (pawn.def != race || pawn.kindDef != spec.kind)
                {
                    reason = "PawnGenerator returned a different race or pawn kind";
                    return FailGeneratedPawn(ref pawn);
                }

                if (pawn.Faction != Faction.OfPlayer)
                {
                    reason = "generated animal is not in the player faction";
                    return FailGeneratedPawn(ref pawn);
                }

                if (pawn.Destroyed || pawn.Dead || pawn.Downed)
                {
                    reason = "generated animal is dead, destroyed, or downed";
                    return FailGeneratedPawn(ref pawn);
                }

                if (spec.lifeStage != null && pawn.ageTracker?.CurLifeStage != spec.lifeStage)
                {
                    reason = $"generated life stage is " +
                             $"{pawn.ageTracker?.CurLifeStage?.defName ?? "<missing>"}, not " +
                             spec.lifeStage.defName;
                    return FailGeneratedPawn(ref pawn);
                }

                if (spec.pregnant == true && !TryApplyPregnancy(pawn, spec, out reason))
                {
                    return FailGeneratedPawn(ref pawn);
                }

                if (!AnimalTradeUtility.Matches(pawn, race, spec))
                {
                    reason = "generated animal does not satisfy the complete specification";
                    return FailGeneratedPawn(ref pawn);
                }

                reason = null;
                return true;
            }
            catch (Exception exception)
            {
                reason = $"{exception.GetType().Name}: {exception.Message}";
                if (pawn != null)
                {
                    DiscardGeneratedPawn(pawn);
                    pawn = null;
                }

                return false;
            }
        }

        /// <summary>
        /// Consumes <paramref name="pawn"/>. Success leaves it spawned; failure discards it.
        /// </summary>
        public static bool TryDeliverToColony(
            Pawn pawn, Map map, out IntVec3 spawnCell, out string reason)
        {
            spawnCell = IntVec3.Invalid;
            if (!IsLivePlayerAnimal(pawn, out reason))
            {
                DiscardGeneratedPawn(pawn);
                return false;
            }

            if (map == null)
            {
                reason = "delivery map is unavailable";
                DiscardGeneratedPawn(pawn);
                return false;
            }

            try
            {
                if (!RCellFinder.TryFindRandomPawnEntryCell(
                        out IntVec3 entryCell, map, CellFinder.EdgeRoadChance_Animal))
                {
                    reason = "no animal-safe map entry cell was found";
                    DiscardGeneratedPawn(pawn);
                    return false;
                }

                spawnCell = CellFinder.RandomClosewalkCellNear(entryCell, map, 12);
                if (!spawnCell.IsValid || !spawnCell.InBounds(map) ||
                    GenSpawn.Spawn(pawn, spawnCell, map, Rot4.Random) != pawn || !pawn.Spawned)
                {
                    reason = "the animal could not be spawned at the colony entry";
                    DiscardGeneratedPawn(pawn);
                    return false;
                }

                reason = null;
                return true;
            }
            catch (Exception exception)
            {
                reason = $"{exception.GetType().Name}: {exception.Message}";
                DiscardGeneratedPawn(pawn);
                return false;
            }
        }

        /// <summary>
        /// Consumes <paramref name="pawn"/>. Success makes it a caravan member; failure discards it.
        /// </summary>
        public static bool TryDeliverToCaravan(Pawn pawn, Caravan caravan, out string reason)
        {
            if (!IsLivePlayerAnimal(pawn, out reason))
            {
                DiscardGeneratedPawn(pawn);
                return false;
            }

            if (caravan == null || caravan.Faction != Faction.OfPlayer)
            {
                reason = "the destination is not a player caravan";
                DiscardGeneratedPawn(pawn);
                return false;
            }

            try
            {
                caravan.AddPawn(pawn, addCarriedPawnToWorldPawnsIfAny: true);
                if (!caravan.ContainsPawn(pawn))
                {
                    reason = "the caravan did not accept the animal as a member";
                    DiscardGeneratedPawn(pawn);
                    return false;
                }

                reason = null;
                return true;
            }
            catch (Exception exception)
            {
                if (caravan.ContainsPawn(pawn))
                {
                    reason = null;
                    return true;
                }
                else
                {
                    reason = $"{exception.GetType().Name}: {exception.Message}";
                    DiscardGeneratedPawn(pawn);
                    return false;
                }
            }
        }

        /// <summary>Vanilla's generated-pawn failure cleanup, including prior world registration.</summary>
        public static void DiscardGeneratedPawn(Pawn pawn)
        {
            if (pawn == null || pawn.Discarded)
            {
                return;
            }

            if (pawn.Spawned)
            {
                pawn.DeSpawn(DestroyMode.Vanish);
            }

            if (Find.WorldPawns != null && Find.WorldPawns.Contains(pawn))
            {
                Find.WorldPawns.RemovePawn(pawn);
            }

            Find.WorldPawns?.PassToWorld(pawn, PawnDiscardDecideMode.Discard);
        }

        private static bool GenderRequestIsSupported(
            RaceProperties race, PawnKindDef kind, Gender? requested, out string reason)
        {
            if (!requested.HasValue)
            {
                reason = null;
                return true;
            }

            if (requested.Value != Gender.Female && requested.Value != Gender.Male)
            {
                reason = $"{requested.Value} is not a selectable animal sex";
                return false;
            }

            if (!race.hasGenders)
            {
                reason = "the race definition is genderless";
                return false;
            }

            if (race.forceGender != Gender.None && race.forceGender != requested.Value)
            {
                reason = $"the race definition forces {race.forceGender}";
                return false;
            }

            if (kind.fixedGender.HasValue && kind.fixedGender.Value != requested.Value)
            {
                reason = $"pawn kind {kind.defName ?? "<unnamed>"} forces {kind.fixedGender.Value}";
                return false;
            }

            reason = null;
            return true;
        }

        private static bool TryChooseBiologicalAge(
            ThingDef race, LifeStageDef requestedStage, out float age, out string reason)
        {
            age = 0f;
            List<LifeStageAge> stages = race.race.lifeStageAges;
            int index = -1;
            for (int i = 0; i < stages.Count; i++)
            {
                if (stages[i]?.def == requestedStage)
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                reason = $"life stage {requestedStage.defName ?? "<unnamed>"} is absent from the race";
                return false;
            }

            float minimum = stages[index].minAge;
            if (index + 1 < stages.Count)
            {
                float nextMinimum = stages[index + 1].minAge;
                if (nextMinimum <= minimum)
                {
                    reason = "the race's life-stage ages are not strictly increasing";
                    return false;
                }

                age = Mathf.Lerp(minimum, nextMinimum, 0.5f);
            }
            else
            {
                float margin = Mathf.Max(
                    FinalStageMinimumMarginYears,
                    Mathf.Min(1f, race.race.lifeExpectancy * 0.01f));
                age = minimum + margin;
            }

            reason = null;
            return true;
        }

        private static bool TryApplyPregnancy(Pawn pawn, AnimalSpec spec, out string reason)
        {
            if (pawn.gender != Gender.Female)
            {
                reason = "generated animal is not female";
                return false;
            }

            Hediff_Pregnant pregnancy =
                pawn.health?.hediffSet?.GetFirstHediffOfDef(HediffDefOf.Pregnant)
                as Hediff_Pregnant;
            if (pregnancy == null)
            {
                pregnancy = HediffMaker.MakeHediff(HediffDefOf.Pregnant, pawn) as Hediff_Pregnant;
                if (pregnancy == null)
                {
                    reason = "the animal pregnancy hediff could not be constructed";
                    return false;
                }

                if (spec.minGestationProgress.HasValue &&
                    pregnancy.GestationProgress < spec.minGestationProgress.Value)
                {
                    pregnancy.Severity = spec.minGestationProgress.Value;
                }

                pawn.health.AddHediff(pregnancy);
            }
            else if (spec.minGestationProgress.HasValue &&
                     pregnancy.GestationProgress < spec.minGestationProgress.Value)
            {
                pregnancy.Severity = spec.minGestationProgress.Value;
            }

            Hediff_Pregnant installed =
                pawn.health?.hediffSet?.GetFirstHediffOfDef(HediffDefOf.Pregnant)
                as Hediff_Pregnant;
            if (installed == null)
            {
                reason = "the animal pregnancy hediff was not installed";
                return false;
            }

            if (spec.minGestationProgress.HasValue &&
                installed.GestationProgress < spec.minGestationProgress.Value)
            {
                reason = $"gestation progress {installed.GestationProgress} is below " +
                         spec.minGestationProgress.Value;
                return false;
            }

            reason = null;
            return true;
        }

        private static bool IsLivePlayerAnimal(Pawn pawn, out string reason)
        {
            if (pawn?.RaceProps == null || !pawn.RaceProps.Animal || pawn.RaceProps.Humanlike)
            {
                reason = "the generated pawn is not a non-humanlike animal";
                return false;
            }

            if (pawn.Faction != Faction.OfPlayer)
            {
                reason = "the generated animal is not in the player faction";
                return false;
            }

            if (pawn.Destroyed || pawn.Dead || pawn.Downed)
            {
                reason = "the generated animal is dead, destroyed, or downed";
                return false;
            }

            reason = null;
            return true;
        }

        private static bool FailGeneratedPawn(ref Pawn pawn)
        {
            DiscardGeneratedPawn(pawn);
            pawn = null;
            return false;
        }
    }
}
