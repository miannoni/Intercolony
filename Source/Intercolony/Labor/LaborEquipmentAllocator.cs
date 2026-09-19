using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Intercolony
{
    // Vanilla's generators read their gear profile from pawn.kindDef and cannot be asked for a
    // promised tier (R2); money cannot express one because value is only 5% of the score (R3).
    // This allocator fills that narrow gap while leaving LaborEquipmentTierService authoritative.
    internal static class LaborEquipmentAllocator
    {
        internal static bool TryFulfil(
            Pawn pawn, LaborEquipmentLevel promisedTier, CombatClause clause,
            SettlementEconomicProfile profile, out string failReason)
        {
            // Production supplies the stable census prospect and posting identity through the
            // overload below. This adapter keeps existing internal callers (including the
            // unchanged self-test) on the same planner-backed path without reviving the old walk.
            LaborProspect compatibilityProspect = BuildCompatibilityProspect(pawn, profile);
            int marketIdentity = pawn == null ? 0 : pawn.thingIDNumber;
            return TryFulfil(
                pawn,
                compatibilityProspect,
                marketIdentity,
                promisedTier,
                clause,
                profile,
                out failReason);
        }

        internal static bool TryFulfil(
            Pawn pawn, LaborProspect prospect, int marketIdentity,
            LaborEquipmentLevel promisedTier, CombatClause clause,
            SettlementEconomicProfile profile, out string failReason)
        {
            failReason = null;

            if (pawn == null)
            {
                failReason = "The applicant pawn was null.";
                return false;
            }

            if (profile == null)
            {
                failReason = "The source settlement profile was unavailable.";
                return false;
            }

            if (promisedTier == LaborEquipmentLevel.Any)
            {
                failReason = "Any is a posting wildcard, not a fulfilment tier.";
                return false;
            }

            // A raised item ceiling is valid only after the source capability gate authorizes the
            // promised tier; callers cannot bypass that gate by invoking the allocator directly.
            if (!LaborEquipmentTierService.CanSupply(profile, promisedTier, clause))
            {
                failReason =
                    $"The source profile cannot supply {promisedTier} under {clause}: " +
                    $"{profile.techTier}/{profile.wealthTier}/{profile.archetype}.";
                return false;
            }

            if (promisedTier == LaborEquipmentLevel.None)
            {
                // None is not normally sent here, but keeping this branch makes the allocator's
                // contract total and leaves the posting path's established StripBondableEquipment
                // behavior untouched.
                DestroyExistingLoadout(pawn);
                return true;
            }

            if (pawn.apparel == null || pawn.RaceProps == null || pawn.RaceProps.body == null)
            {
                failReason = "The pawn had no usable apparel tracker or body definition.";
                return false;
            }

            BodyDef body = pawn.RaceProps.body;

            if (clause != CombatClause.Civilian && pawn.equipment == null)
            {
                failReason = "The pawn had no equipment tracker for a combat package.";
                return false;
            }

            string lastFailure = null;
            try
            {
                // Snapshot all pawn-dependent apparel compatibility once. The planner never
                // receives the Pawn or re-runs these checks while walking candidates.
                ISet<ThingDef> compatibleApparelDefinitions =
                    BuildCompatibleApparelDefinitions(pawn);
                IReadOnlyList<LaborEquipmentPackagePlan> deterministicPlans =
                    LaborEquipmentPackagePlanner.PlanDeterministicCandidates(
                        prospect,
                        profile,
                        promisedTier,
                        clause,
                        marketIdentity,
                        compatibleApparelDefinitions,
                        body,
                        retryAttempt: 0);
                if (deterministicPlans == null || deterministicPlans.Count == 0)
                {
                    lastFailure =
                        "The deterministic equipment search produced no candidate package.";
                }
                else
                {
                    // The planner supplies the complete candidate space in a seeded order. Each
                    // candidate goes through Thing creation and the authoritative Classify check;
                    // stop at the first package that keeps the promise.
                    for (int planIndex = 0;
                         planIndex < deterministicPlans.Count;
                         planIndex++)
                    {
                        string deterministicFailure;
                        if (TryApplyPlannedPackage(
                                pawn,
                                deterministicPlans[planIndex],
                                promisedTier,
                                clause,
                                out deterministicFailure))
                        {
                            return true;
                        }

                        lastFailure = deterministicFailure;
                    }
                }
            }
            catch (Exception ex)
            {
                failReason =
                    $"Planned equipment construction threw {ex.GetType().Name}: {ex.Message}";
                return false;
            }

            // Preserve the existing failure contract: the caller rejects and disposes the
            // applicant when fulfilment cannot meet the promise. Never return success for the
            // last under-tier loadout merely because the candidate sequence was exhausted.
            failReason = lastFailure ??
                $"No deterministic package could fulfil {promisedTier}.";
            return false;
        }

        private static ISet<ThingDef> BuildCompatibleApparelDefinitions(Pawn pawn)
        {
            HashSet<ThingDef> compatible = new HashSet<ThingDef>();
            IReadOnlyList<LaborEquipmentCatalogueDefinition> definitions =
                LaborEquipmentCatalogue.Definitions;
            for (int index = 0; index < definitions.Count; index++)
            {
                LaborEquipmentCatalogueDefinition definition = definitions[index];
                ThingDef apparel = definition?.Def;
                if (definition == null ||
                    definition.Role != LaborEquipmentCatalogueRole.Apparel ||
                    apparel?.apparel == null ||
                    !apparel.apparel.PawnCanWear(pawn, ignoreGender: true) ||
                    !ApparelUtility.HasPartsToWear(pawn, apparel))
                {
                    continue;
                }

                compatible.Add(apparel);
            }

            return compatible;
        }

        private static bool TryApplyPlannedPackage(
            Pawn pawn, LaborEquipmentPackagePlan plan,
            LaborEquipmentLevel promisedTier, CombatClause clause,
            out string failureReason)
        {
            failureReason = null;
            if (plan == null)
            {
                failureReason = "The equipment planner returned a null package.";
                return false;
            }

            // The planner already enforces this, but keep the allocator's player-facing rule
            // local: a civilian plan can never smuggle in an offensive weapon.
            if (clause == CombatClause.Civilian && plan.HasWeapon)
            {
                failureReason = "A civilian package contained an offensive weapon.";
                return false;
            }

            if (plan.HasWeapon)
            {
                return TryApplyCombatPackage(
                    pawn, plan.Weapon, plan.Apparel, promisedTier, clause, out failureReason);
            }

            return TryApplyApparelPackage(
                pawn, plan.Apparel, promisedTier, clause, out failureReason);
        }

        private static bool TryApplyApparelPackage(
            Pawn pawn, IReadOnlyList<LaborEquipmentPackageItemPlan> apparelPlan,
            LaborEquipmentLevel promisedTier,
            CombatClause clause, out string failureReason)
        {
            failureReason = null;
            List<Thing> createdItems = new List<Thing>();
            bool loadoutPrepared = false;
            bool keepPackage = false;
            try
            {
                if (!CanWearTogetherAsSet(pawn, apparelPlan))
                {
                    failureReason = "The selected apparel set conflicted for this pawn's body.";
                    return false;
                }

                DestroyExistingLoadout(pawn);
                loadoutPrepared = true;
                for (int index = 0; index < apparelPlan.Count; index++)
                {
                    LaborEquipmentPackageItemPlan choice = apparelPlan[index];
                    Thing item = ThingMaker.MakeThing(choice.ThingDef, choice.StuffDef);
                    if (item == null)
                    {
                        failureReason = $"Could not make apparel {choice.ThingDef.defName}.";
                        return false;
                    }

                    createdItems.Add(item);
                    if (!TrySetQuality(item, choice, out string qualityFailure))
                    {
                        failureReason = qualityFailure;
                        return false;
                    }

                    item.stackCount = 1;
                    Apparel apparel = item as Apparel;
                    if (apparel == null)
                    {
                        failureReason = $"ThingDef {choice.ThingDef.defName} did not make apparel.";
                        return false;
                    }

                    pawn.apparel.Wear(apparel, dropReplacedApparel: false);
                    if (!pawn.apparel.Contains(apparel))
                    {
                        failureReason =
                            $"Pawn could not wear generated apparel {choice.ThingDef.defName}.";
                        return false;
                    }
                }

                LaborEquipmentLevel actual = LaborEquipmentTierService.Classify(
                    pawn, clause);
                if (!LaborEquipmentTierService.MeetsOrExceeds(actual, promisedTier))
                {
                    failureReason =
                        $"Generated apparel classified as {actual}, below {promisedTier}.";
                    return false;
                }

                keepPackage = true;
                return true;
            }
            catch (Exception ex)
            {
                failureReason =
                    $"Apparel package construction threw {ex.GetType().Name}: {ex.Message}";
                return false;
            }
            finally
            {
                if (!keepPackage && loadoutPrepared)
                {
                    CleanupFailedPackage(pawn, createdItems);
                }
            }
        }

        private static bool TryApplyCombatPackage(
            Pawn pawn, LaborEquipmentPackageItemPlan weaponChoice,
            IReadOnlyList<LaborEquipmentPackageItemPlan> apparelPlan,
            LaborEquipmentLevel promisedTier, CombatClause clause, out string failureReason)
        {
            failureReason = null;
            List<Thing> createdItems = new List<Thing>();
            bool loadoutPrepared = false;
            bool keepPackage = false;
            try
            {
                if (!CanWearTogetherAsSet(pawn, apparelPlan))
                {
                    failureReason = "The selected apparel set conflicted for this pawn's body.";
                    return false;
                }

                DestroyExistingLoadout(pawn);
                loadoutPrepared = true;

                Thing weaponItem = ThingMaker.MakeThing(
                    weaponChoice.ThingDef, weaponChoice.StuffDef);
                if (weaponItem == null)
                {
                    failureReason = $"Could not make weapon {weaponChoice.ThingDef.defName}.";
                    return false;
                }

                createdItems.Add(weaponItem);
                if (!TrySetQuality(
                        weaponItem, weaponChoice, out string weaponQualityFailure))
                {
                    failureReason = weaponQualityFailure;
                    return false;
                }

                weaponItem.stackCount = 1;
                ThingWithComps weapon = weaponItem as ThingWithComps;
                if (weapon == null)
                {
                    failureReason =
                        $"ThingDef {weaponChoice.ThingDef.defName} did not make equipment.";
                    return false;
                }

                pawn.equipment.AddEquipment(weapon);
                if (!pawn.equipment.Contains(weapon) || pawn.equipment.Primary != weapon)
                {
                    failureReason =
                        $"Pawn could not equip generated weapon {weaponChoice.ThingDef.defName}.";
                    return false;
                }

                for (int index = 0; index < apparelPlan.Count; index++)
                {
                    LaborEquipmentPackageItemPlan choice = apparelPlan[index];
                    Thing item = ThingMaker.MakeThing(choice.ThingDef, choice.StuffDef);
                    if (item == null)
                    {
                        failureReason = $"Could not make apparel {choice.ThingDef.defName}.";
                        return false;
                    }

                    createdItems.Add(item);
                    if (!TrySetQuality(item, choice, out string qualityFailure))
                    {
                        failureReason = qualityFailure;
                        return false;
                    }

                    item.stackCount = 1;
                    Apparel apparel = item as Apparel;
                    if (apparel == null)
                    {
                        failureReason = $"ThingDef {choice.ThingDef.defName} did not make apparel.";
                        return false;
                    }

                    pawn.apparel.Wear(apparel, dropReplacedApparel: false);
                    if (!pawn.apparel.Contains(apparel))
                    {
                        failureReason =
                            $"Pawn could not wear generated apparel {choice.ThingDef.defName}.";
                        return false;
                    }
                }

                LaborEquipmentLevel actual = LaborEquipmentTierService.Classify(
                    pawn, clause);
                if (!LaborEquipmentTierService.MeetsOrExceeds(actual, promisedTier))
                {
                    failureReason =
                        $"Generated combat package classified as {actual}, below {promisedTier}.";
                    return false;
                }

                keepPackage = true;
                return true;
            }
            catch (Exception ex)
            {
                failureReason =
                    $"Combat package construction threw {ex.GetType().Name}: {ex.Message}";
                return false;
            }
            finally
            {
                if (!keepPackage && loadoutPrepared)
                {
                    CleanupFailedPackage(pawn, createdItems);
                }
            }
        }

        private static bool CanWearTogetherAsSet(
            Pawn pawn, IReadOnlyList<LaborEquipmentPackageItemPlan> apparelPlan)
        {
            if (apparelPlan == null || pawn?.RaceProps?.body == null)
            {
                return false;
            }

            if (apparelPlan.Count == 0)
            {
                return true;
            }

            BodyDef body = pawn.RaceProps.body;
            for (int leftIndex = 0; leftIndex < apparelPlan.Count; leftIndex++)
            {
                for (int rightIndex = leftIndex + 1;
                     rightIndex < apparelPlan.Count;
                     rightIndex++)
                {
                    if (!ApparelUtility.CanWearTogether(
                            apparelPlan[leftIndex].ThingDef,
                            apparelPlan[rightIndex].ThingDef, body))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool TrySetQuality(
            Thing item, LaborEquipmentPackageItemPlan choice, out string failureReason)
        {
            failureReason = null;
            CompQuality quality = item.TryGetComp<CompQuality>();
            if (quality == null)
            {
                if (!choice.HasQuality)
                {
                    return true;
                }

                failureReason =
                    $"Generated {choice.ThingDef.defName} had no CompQuality.";
                return false;
            }

            quality.SetQuality(choice.Quality, ArtGenerationContext.Outsider);
            if (quality.Quality != choice.Quality)
            {
                failureReason =
                    $"Generated {choice.ThingDef.defName} kept quality {quality.Quality} " +
                    $"instead of {choice.Quality}.";
                return false;
            }

            return true;
        }

        private static void DestroyExistingLoadout(Pawn pawn)
        {
            if (pawn?.equipment != null)
            {
                pawn.equipment.DestroyAllEquipment(DestroyMode.Vanish);
            }

            if (pawn?.apparel != null)
            {
                pawn.apparel.DestroyAll(DestroyMode.Vanish);
            }
        }

        private static void CleanupFailedPackage(Pawn pawn, List<Thing> createdItems)
        {
            DestroyExistingLoadout(pawn);
            for (int index = 0; index < createdItems.Count; index++)
            {
                Thing item = createdItems[index];
                if (item != null && !item.Destroyed)
                {
                    item.Destroy(DestroyMode.Vanish);
                }
            }
        }

        private static LaborProspect BuildCompatibilityProspect(
            Pawn pawn, SettlementEconomicProfile profile)
        {
            LaborProspect prospect = new LaborProspect
            {
                settlementId = profile == null ? -1 : profile.settlementId
            };
            List<SkillRecord> records = pawn?.skills?.skills;
            if (records == null || records.Count == 0)
            {
                return prospect;
            }

            int skillCount = 0;
            for (int index = 0; index < records.Count; index++)
            {
                SkillRecord record = records[index];
                if (record?.def != null)
                {
                    skillCount = Math.Max(skillCount, record.def.index + 1);
                }
            }

            int[] skillLevels = new int[skillCount];
            for (int index = 0; index < skillLevels.Length; index++)
            {
                skillLevels[index] = -1;
            }

            Passion[] passions = new Passion[skillCount];
            List<SkillRecord> ranked = new List<SkillRecord>();
            for (int index = 0; index < records.Count; index++)
            {
                SkillRecord record = records[index];
                if (record?.def == null || record.def.index >= skillCount)
                {
                    continue;
                }

                if (record.TotallyDisabled)
                {
                    skillLevels[record.def.index] = -1;
                    continue;
                }

                skillLevels[record.def.index] = record.Level;
                passions[record.def.index] = record.passion;
                ranked.Add(record);
            }

            ranked.Sort((left, right) => right.Level.CompareTo(left.Level));
            float pricedSkillValue = 0f;
            for (int index = 0;
                 index < LaborCandidateService.PricedSkillCount && index < ranked.Count;
                 index++)
            {
                pricedSkillValue += LaborCandidateService.WeightedLevel(
                    ranked[index].Level, ranked[index].passion);
            }

            prospect.skillLevels = skillLevels;
            prospect.passions = passions;
            prospect.pricedSkillValue = pricedSkillValue;
            return prospect;
        }

    }
}
