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
        private const int MaxApparelItems = 12;
        private const int MaxAlternativeApparelAnchors = 8;

        private static readonly QualityCategory[] QualityOrder =
        {
            QualityCategory.Awful,
            QualityCategory.Poor,
            QualityCategory.Normal,
            QualityCategory.Good,
            QualityCategory.Excellent,
            QualityCategory.Masterwork,
            QualityCategory.Legendary
        };

        internal static bool TryFulfil(
            Pawn pawn, LaborEquipmentLevel promisedTier, CombatClause clause,
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

            string lastFailure = null;
            try
            {
                if (clause == CombatClause.Civilian)
                {
                    for (int qualityIndex = 0;
                         qualityIndex < QualityOrder.Length;
                         qualityIndex++)
                    {
                        QualityCategory quality = QualityOrder[qualityIndex];
                        List<ItemChoice> apparelChoices = BuildChoices(
                            pawn, profile, apparel: true, quality: quality);
                        if (apparelChoices.Count == 0)
                        {
                            continue;
                        }

                        List<List<ItemChoice>> apparelPlans = BuildApparelPlans(
                            pawn, apparelChoices);
                        string packageFailure;
                        if (TryApplyApparelPlans(
                                pawn, apparelPlans, promisedTier, clause, out packageFailure))
                        {
                            return true;
                        }

                        lastFailure = packageFailure;
                    }

                    failReason = lastFailure ??
                        $"No compatible apparel package could fulfil {promisedTier}.";
                    return false;
                }

                if (pawn.equipment == null)
                {
                    failReason = "The pawn had no equipment tracker for a combat package.";
                    return false;
                }

                for (int qualityIndex = 0;
                     qualityIndex < QualityOrder.Length;
                     qualityIndex++)
                {
                    QualityCategory quality = QualityOrder[qualityIndex];
                    List<ItemChoice> weaponChoices = BuildChoices(
                        pawn, profile, apparel: false, quality: quality);
                    List<ItemChoice> apparelChoices = BuildChoices(
                        pawn, profile, apparel: true, quality: quality);
                    List<List<ItemChoice>> apparelPlans = BuildApparelPlans(
                        pawn, apparelChoices);

                    // Classify deliberately treats one qualifying combat item as Standard, but
                    // caps an incomplete loadout below Professional. Preserve that authority:
                    // Standard may use apparel alone or a weapon alone; higher tiers need both.
                    if (promisedTier == LaborEquipmentLevel.Standard && apparelPlans.Count > 0)
                    {
                        string apparelOnlyFailure;
                        if (TryApplyApparelPlans(
                                pawn, apparelPlans, promisedTier, clause,
                                out apparelOnlyFailure))
                        {
                            return true;
                        }

                        lastFailure = apparelOnlyFailure;
                    }

                    if (weaponChoices.Count == 0)
                    {
                        continue;
                    }

                    if (promisedTier == LaborEquipmentLevel.Standard)
                    {
                        // An empty apparel set is valid only for the classifier's Standard
                        // incomplete-package case, and is prevalidated as a trivial set below.
                        apparelPlans.Insert(0, new List<ItemChoice>());
                    }

                    if (apparelPlans.Count == 0)
                    {
                        continue;
                    }

                    string packageFailure;
                    if (TryApplyCombatPackages(
                            pawn, weaponChoices, apparelPlans, promisedTier, clause,
                            out packageFailure))
                    {
                        return true;
                    }

                    lastFailure = packageFailure;
                }
            }
            catch (Exception ex)
            {
                failReason =
                    $"Candidate gear construction threw {ex.GetType().Name}: {ex.Message}";
                return false;
            }

            failReason = lastFailure ??
                $"No compatible combat package could fulfil {promisedTier}.";
            return false;
        }

        private static List<ItemChoice> BuildChoices(
            Pawn pawn, SettlementEconomicProfile profile, bool apparel, QualityCategory quality)
        {
            List<ItemChoice> choices = new List<ItemChoice>();
            List<ThingDef> allDefs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int defIndex = 0; defIndex < allDefs.Count; defIndex++)
            {
                ThingDef def = allDefs[defIndex];
                if (!IsCandidateDefinition(pawn, profile, def, apparel))
                {
                    continue;
                }

                bool hasQuality = def.HasComp(typeof(CompQuality));
                if (!hasQuality && quality != QualityCategory.Normal)
                {
                    continue;
                }

                List<ThingDef> stuffChoices = BuildStuffChoices(def, profile);
                for (int stuffIndex = 0; stuffIndex < stuffChoices.Count; stuffIndex++)
                {
                    ItemChoice choice = new ItemChoice
                    {
                        thingDef = def,
                        stuffDef = stuffChoices[stuffIndex],
                        quality = hasQuality ? quality : QualityCategory.Normal,
                        hasQuality = hasQuality
                    };
                    choices.Add(choice);
                }
            }

            choices.Sort(CompareChoices);
            return choices;
        }

        private static bool IsCandidateDefinition(
            Pawn pawn, SettlementEconomicProfile profile, ThingDef def, bool apparel)
        {
            if (def == null || def.category != ThingCategory.Item ||
                def.generateAllowChance <= 0f || !IsWithinSourceTech(def, profile) ||
                def.thingClass == null)
            {
                return false;
            }

            if (apparel)
            {
                if (!def.IsApparel || !typeof(Apparel).IsAssignableFrom(def.thingClass) ||
                    def.apparel.layers == null || def.apparel.layers.Count == 0 ||
                    def.apparel.bodyPartGroups == null || def.apparel.bodyPartGroups.Count == 0 ||
                    !def.apparel.PawnCanWear(pawn, ignoreGender: true))
                {
                    return false;
                }

                return ApparelUtility.HasPartsToWear(pawn, def);
            }

            // IsWeapon and EquipmentType are properties, not a list of known vanilla names. This
            // keeps modded and DLC weapons eligible while excluding non-primary utility items.
            return def.IsWeapon && def.equipmentType == EquipmentType.Primary &&
                   typeof(ThingWithComps).IsAssignableFrom(def.thingClass);
        }

        private static bool IsWithinSourceTech(
            ThingDef def, SettlementEconomicProfile profile)
        {
            // CanSupply owns the tier/wealth/archetype capability decision. This second, narrower
            // check applies its tech ceiling to each actual item, so a low-tech source cannot
            // leak a spacer item through a Standard or malformed Elite promise. Classifier scoring
            // already treats an item's undefined tech as Industrial; use that same fallback here.
            if (def == null || profile == null || profile.techTier == TechLevel.Undefined)
            {
                return false;
            }

            TechLevel itemTech = def.techLevel == TechLevel.Undefined
                ? TechLevel.Industrial
                : def.techLevel;
            return (int)itemTech <= (int)profile.techTier;
        }

        private static List<ThingDef> BuildStuffChoices(
            ThingDef def, SettlementEconomicProfile profile)
        {
            List<ThingDef> choices = new List<ThingDef>();
            if (!def.MadeFromStuff)
            {
                choices.Add(null);
                return choices;
            }

            foreach (ThingDef stuff in GenStuff.AllowedStuffsFor(
                         def, profile.techTier, checkAllowedInStuffGeneration: true))
            {
                if (stuff == null || !stuff.IsStuff ||
                    !IsWithinSourceStuffTech(stuff, profile) ||
                    choices.Contains(stuff))
                {
                    continue;
                }

                choices.Add(stuff);
            }

            choices.Sort(CompareThingDefs);
            return choices;
        }

        private static bool IsWithinSourceStuffTech(
            ThingDef stuff, SettlementEconomicProfile profile)
        {
            if (stuff == null || profile == null || profile.techTier == TechLevel.Undefined)
            {
                return false;
            }

            // Core and many modded material Defs intentionally inherit Undefined. GenStuff uses
            // the same zero-value ordering, so an unspecified material does not claim a tech era
            // above the source; the crafted item's own tech level remains the hard ceiling.
            return stuff.techLevel == TechLevel.Undefined ||
                   (int)stuff.techLevel <= (int)profile.techTier;
        }

        private static List<List<ItemChoice>> BuildApparelPlans(
            Pawn pawn, List<ItemChoice> choices)
        {
            List<List<ItemChoice>> plans = new List<List<ItemChoice>>();
            HashSet<string> planKeys = new HashSet<string>(StringComparer.Ordinal);

            AddApparelPlan(
                plans, planKeys, BuildGreedyApparelPlan(
                    pawn, choices, forward: true, replaceConflicts: false, anchorIndex: -1));
            AddApparelPlan(
                plans, planKeys, BuildGreedyApparelPlan(
                    pawn, choices, forward: true, replaceConflicts: true, anchorIndex: -1));
            AddApparelPlan(
                plans, planKeys, BuildGreedyApparelPlan(
                    pawn, choices, forward: false, replaceConflicts: false, anchorIndex: -1));

            List<int> anchorIndices = new List<int>();
            int halfAnchorCount = MaxAlternativeApparelAnchors / 2;
            for (int index = 0; index < halfAnchorCount && index < choices.Count; index++)
            {
                if (!anchorIndices.Contains(index))
                {
                    anchorIndices.Add(index);
                }
            }

            int highAnchorStart = Math.Max(halfAnchorCount, choices.Count - halfAnchorCount);
            for (int index = highAnchorStart; index < choices.Count; index++)
            {
                if (!anchorIndices.Contains(index))
                {
                    anchorIndices.Add(index);
                }
            }

            foreach (int anchorIndex in anchorIndices)
            {
                AddApparelPlan(
                    plans, planKeys, BuildGreedyApparelPlan(
                        pawn, choices, forward: true, replaceConflicts: false,
                        anchorIndex: anchorIndex));
                AddApparelPlan(
                    plans, planKeys, BuildGreedyApparelPlan(
                        pawn, choices, forward: true, replaceConflicts: true,
                        anchorIndex: anchorIndex));
                AddApparelPlan(
                    plans, planKeys, BuildGreedyApparelPlan(
                        pawn, choices, forward: false, replaceConflicts: false,
                        anchorIndex: anchorIndex));
                AddApparelPlan(
                    plans, planKeys, BuildGreedyApparelPlan(
                        pawn, choices, forward: false, replaceConflicts: true,
                        anchorIndex: anchorIndex));
            }

            // The first successful package is the answer. Compare the strongest constituent first,
            // then package size and value, so a promised floor does not default to best-in-database.
            plans.Sort(CompareApparelPlans);
            return plans;
        }

        private static List<ItemChoice> BuildGreedyApparelPlan(
            Pawn pawn, List<ItemChoice> choices, bool forward, bool replaceConflicts,
            int anchorIndex)
        {
            List<ItemChoice> selected = new List<ItemChoice>();
            if (anchorIndex >= 0 && anchorIndex < choices.Count)
            {
                selected.Add(choices[anchorIndex]);
            }

            int step = forward ? 1 : -1;
            int index = forward ? 0 : choices.Count - 1;
            while (index >= 0 && index < choices.Count)
            {
                ItemChoice candidate = choices[index];
                index += step;
                if (candidate == (anchorIndex >= 0 ? choices[anchorIndex] : null))
                {
                    continue;
                }

                List<ItemChoice> conflicts = FindConflicts(pawn, selected, candidate);
                if (conflicts.Count == 0)
                {
                    if (selected.Count < MaxApparelItems)
                    {
                        selected.Add(candidate);
                    }

                    continue;
                }

                if (!replaceConflicts || !MoreExcessiveThan(candidate, conflicts))
                {
                    continue;
                }

                for (int conflictIndex = selected.Count - 1;
                     conflictIndex >= 0;
                     conflictIndex--)
                {
                    if (conflicts.Contains(selected[conflictIndex]))
                    {
                        selected.RemoveAt(conflictIndex);
                    }
                }

                if (selected.Count < MaxApparelItems)
                {
                    selected.Add(candidate);
                }
            }

            return selected;
        }

        private static List<ItemChoice> FindConflicts(
            Pawn pawn, List<ItemChoice> selected, ItemChoice candidate)
        {
            List<ItemChoice> conflicts = new List<ItemChoice>();
            for (int index = 0; index < selected.Count; index++)
            {
                if (!ApparelUtility.CanWearTogether(
                        candidate.thingDef, selected[index].thingDef, pawn.RaceProps.body))
                {
                    conflicts.Add(selected[index]);
                }
            }

            return conflicts;
        }

        private static bool MoreExcessiveThan(
            ItemChoice candidate, List<ItemChoice> conflicts)
        {
            for (int index = 0; index < conflicts.Count; index++)
            {
                if (CompareChoices(candidate, conflicts[index]) <= 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static void AddApparelPlan(
            List<List<ItemChoice>> plans, HashSet<string> planKeys, List<ItemChoice> plan)
        {
            if (plan == null || plan.Count == 0)
            {
                return;
            }

            List<string> keys = new List<string>();
            for (int index = 0; index < plan.Count; index++)
            {
                keys.Add(ChoiceKey(plan[index]));
            }

            string planKey = PlanKey(keys);
            if (planKeys.Add(planKey))
            {
                plans.Add(plan);
            }
        }

        private static int CompareApparelPlans(
            List<ItemChoice> left, List<ItemChoice> right)
        {
            ItemChoice leftStrongest = left[0];
            for (int index = 1; index < left.Count; index++)
            {
                if (CompareChoices(left[index], leftStrongest) > 0)
                {
                    leftStrongest = left[index];
                }
            }

            ItemChoice rightStrongest = right[0];
            for (int index = 1; index < right.Count; index++)
            {
                if (CompareChoices(right[index], rightStrongest) > 0)
                {
                    rightStrongest = right[index];
                }
            }

            int comparison = CompareChoices(leftStrongest, rightStrongest);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.Count.CompareTo(right.Count);
            if (comparison != 0)
            {
                return comparison;
            }

            float leftValue = 0f;
            for (int index = 0; index < left.Count; index++)
            {
                leftValue += CandidateValue(left[index].thingDef, left[index].stuffDef);
            }

            float rightValue = 0f;
            for (int index = 0; index < right.Count; index++)
            {
                rightValue += CandidateValue(right[index].thingDef, right[index].stuffDef);
            }

            comparison = CompareFloat(leftValue, rightValue);
            if (comparison != 0)
            {
                return comparison;
            }

            return StringComparer.Ordinal.Compare(
                PlanKeyForChoices(left), PlanKeyForChoices(right));
        }

        private static string PlanKey(List<string> keys)
        {
            keys.Sort(StringComparer.Ordinal);
            return String.Join("|", keys.ToArray());
        }

        private static string PlanKeyForChoices(List<ItemChoice> choices)
        {
            List<string> keys = new List<string>();
            for (int index = 0; index < choices.Count; index++)
            {
                keys.Add(ChoiceKey(choices[index]));
            }

            return PlanKey(keys);
        }

        private static bool TryApplyApparelPlans(
            Pawn pawn, List<List<ItemChoice>> plans, LaborEquipmentLevel promisedTier,
            CombatClause clause, out string failureReason)
        {
            failureReason = null;
            string lastFailure = null;
            for (int planIndex = 0; planIndex < plans.Count; planIndex++)
            {
                string packageFailure;
                if (TryApplyApparelPackage(
                        pawn, plans[planIndex], promisedTier, clause, out packageFailure))
                {
                    return true;
                }

                lastFailure = packageFailure;
            }

            failureReason = lastFailure ?? "Every generated apparel plan was unavailable.";
            return false;
        }

        private static bool TryApplyCombatPackages(
            Pawn pawn, List<ItemChoice> weapons, List<List<ItemChoice>> apparelPlans,
            LaborEquipmentLevel promisedTier, CombatClause clause, out string failureReason)
        {
            failureReason = null;
            string lastFailure = null;
            for (int weaponIndex = 0; weaponIndex < weapons.Count; weaponIndex++)
            {
                for (int planIndex = 0; planIndex < apparelPlans.Count; planIndex++)
                {
                    string packageFailure;
                    if (TryApplyCombatPackage(
                            pawn, weapons[weaponIndex], apparelPlans[planIndex], promisedTier,
                            clause, out packageFailure))
                    {
                        return true;
                    }

                    lastFailure = packageFailure;
                }
            }

            failureReason = lastFailure ?? "Every generated combat package was unavailable.";
            return false;
        }

        private static bool TryApplyApparelPackage(
            Pawn pawn, List<ItemChoice> apparelPlan, LaborEquipmentLevel promisedTier,
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
                    ItemChoice choice = apparelPlan[index];
                    Thing item = ThingMaker.MakeThing(choice.thingDef, choice.stuffDef);
                    if (item == null)
                    {
                        failureReason = $"Could not make apparel {choice.thingDef.defName}.";
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
                        failureReason = $"ThingDef {choice.thingDef.defName} did not make apparel.";
                        return false;
                    }

                    pawn.apparel.Wear(apparel, dropReplacedApparel: false);
                    if (!pawn.apparel.Contains(apparel))
                    {
                        failureReason =
                            $"Pawn could not wear generated apparel {choice.thingDef.defName}.";
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
            Pawn pawn, ItemChoice weaponChoice, List<ItemChoice> apparelPlan,
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
                    weaponChoice.thingDef, weaponChoice.stuffDef);
                if (weaponItem == null)
                {
                    failureReason = $"Could not make weapon {weaponChoice.thingDef.defName}.";
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
                        $"ThingDef {weaponChoice.thingDef.defName} did not make equipment.";
                    return false;
                }

                pawn.equipment.AddEquipment(weapon);
                if (!pawn.equipment.Contains(weapon) || pawn.equipment.Primary != weapon)
                {
                    failureReason =
                        $"Pawn could not equip generated weapon {weaponChoice.thingDef.defName}.";
                    return false;
                }

                for (int index = 0; index < apparelPlan.Count; index++)
                {
                    ItemChoice choice = apparelPlan[index];
                    Thing item = ThingMaker.MakeThing(choice.thingDef, choice.stuffDef);
                    if (item == null)
                    {
                        failureReason = $"Could not make apparel {choice.thingDef.defName}.";
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
                        failureReason = $"ThingDef {choice.thingDef.defName} did not make apparel.";
                        return false;
                    }

                    pawn.apparel.Wear(apparel, dropReplacedApparel: false);
                    if (!pawn.apparel.Contains(apparel))
                    {
                        failureReason =
                            $"Pawn could not wear generated apparel {choice.thingDef.defName}.";
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

        private static bool CanWearTogetherAsSet(Pawn pawn, List<ItemChoice> apparelPlan)
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
                            apparelPlan[leftIndex].thingDef,
                            apparelPlan[rightIndex].thingDef, body))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool TrySetQuality(
            Thing item, ItemChoice choice, out string failureReason)
        {
            failureReason = null;
            CompQuality quality = item.TryGetComp<CompQuality>();
            if (quality == null)
            {
                if (!choice.hasQuality)
                {
                    return true;
                }

                failureReason =
                    $"Generated {choice.thingDef.defName} had no CompQuality.";
                return false;
            }

            quality.SetQuality(choice.quality, ArtGenerationContext.Outsider);
            if (quality.Quality != choice.quality)
            {
                failureReason =
                    $"Generated {choice.thingDef.defName} kept quality {quality.Quality} " +
                    $"instead of {choice.quality}.";
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

        private static int CompareChoices(ItemChoice left, ItemChoice right)
        {
            int comparison = CompareTech(left, right);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.quality.CompareTo(right.quality);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = CompareFloat(
                CandidateValue(left.thingDef, left.stuffDef),
                CandidateValue(right.thingDef, right.stuffDef));
            if (comparison != 0)
            {
                return comparison;
            }

            return StringComparer.Ordinal.Compare(ChoiceKey(left), ChoiceKey(right));
        }

        private static int CompareTech(ItemChoice left, ItemChoice right)
        {
            int leftTech = Math.Max(
                (int)EffectiveCandidateTech(left.thingDef), left.stuffDef == null
                    ? (int)TechLevel.Undefined
                    : (int)left.stuffDef.techLevel);
            int rightTech = Math.Max(
                (int)EffectiveCandidateTech(right.thingDef), right.stuffDef == null
                    ? (int)TechLevel.Undefined
                    : (int)right.stuffDef.techLevel);
            return leftTech.CompareTo(rightTech);
        }

        private static TechLevel EffectiveCandidateTech(ThingDef def)
        {
            return def != null && def.techLevel != TechLevel.Undefined
                ? def.techLevel
                : TechLevel.Industrial;
        }

        private static int CompareThingDefs(ThingDef left, ThingDef right)
        {
            if (left == null)
            {
                return right == null ? 0 : -1;
            }

            if (right == null)
            {
                return 1;
            }

            int comparison = ((int)left.techLevel).CompareTo((int)right.techLevel);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = CompareFloat(left.BaseMarketValue, right.BaseMarketValue);
            if (comparison != 0)
            {
                return comparison;
            }

            return StringComparer.Ordinal.Compare(left.defName, right.defName);
        }

        private static float CandidateValue(ThingDef def, ThingDef stuff)
        {
            float value = def == null ? 0f : def.BaseMarketValue;
            if (stuff != null)
            {
                value += stuff.BaseMarketValue;
            }

            return IsFinite(value) ? value : 0f;
        }

        private static int CompareFloat(float left, float right)
        {
            float safeLeft = IsFinite(left) ? left : 0f;
            float safeRight = IsFinite(right) ? right : 0f;
            return safeLeft.CompareTo(safeRight);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static string ChoiceKey(ItemChoice choice)
        {
            return (choice.thingDef?.defName ?? "") + ":" +
                   (choice.stuffDef?.defName ?? "") + ":" +
                   ((int)choice.quality).ToString();
        }

        private sealed class ItemChoice
        {
            public ThingDef thingDef;
            public ThingDef stuffDef;
            public QualityCategory quality;
            public bool hasQuality;
        }
    }
}
