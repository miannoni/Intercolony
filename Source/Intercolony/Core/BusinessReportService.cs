using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Turns the mod's state into the two screens §117 asks for, and answers §45's questions.
    ///
    /// §45 calls this "the heart of the finished product" and lists what a player should be able to
    /// work out: is this contract profitable, should I hire or train, should I buy inputs or produce
    /// them, should I deliver myself. Each of those is a comparison, so each figure here exists to
    /// sit next to another one rather than to be impressive on its own.
    ///
    /// §117's warning governs the whole file: *"Use estimates carefully."* Everything backward-looking
    /// comes from the ledger and is fact. Everything forward-looking is an estimate, is labelled as
    /// one, and is built from numbers the mod would really charge — not from a model of the player's
    /// production, which the mod cannot see and should not pretend to.
    /// </summary>
    public static class BusinessReportService
    {
        /// <summary>A quadrum. §117's report window.</summary>
        public const int QuadrumDays = GenDate.DaysPerQuadrum;

        /// <summary>A year, the longer view. Matches the ledger's retention exactly.</summary>
        public const int YearDays = GenDate.DaysPerYear;

        /// <summary>
        /// Completed purchase evidence is useful for a year, not forever: material prices drift
        /// with the market, and a purchase from three years ago is not evidence about today.
        /// </summary>
        private const int RecentPurchaseWindowTicks = YearDays * GenDate.TicksPerDay;

        // --- Forward-looking: is this contract worth it? (§45) -----------------------------

        /// <summary>
        /// One recurring agreement's economics per cycle, in §45's exact shape.
        /// </summary>
        public class ContractEstimate
        {
            public RecurringContract contract;

            /// <summary>Agreed, not estimated — the price is locked for the contract's life.</summary>
            public int revenue;

            /// <summary>What procuring the same goods would cost. Negative.</summary>
            public int inputsIfBought;

            /// <summary>What the selected recipe or construction route's direct inputs would cost. Negative when known.</summary>
            public int directInputsIfBought;

            /// <summary>Direct-input price and resolution state for the contracted good.</summary>
            public DirectInputEstimate directInputs;

            /// <summary>Whole active-employee wage bill over this cycle. Negative.</summary>
            public int payroll;

            /// <summary>Wages of the relevant workforce for this good over the cycle. Negative.</summary>
            public int directPayroll;

            /// <summary>Resolution and explanation state for the direct-labour estimate.</summary>
            public DirectLaborEstimate directLabor;

            /// <summary>The delivery premium, shown as what hauling it yourself is worth. Negative.</summary>
            public int transport;

            public bool HasDirectLaborEstimate =>
                directLabor != null && directLabor.status != DirectLaborCostStatus.Unavailable;

            // Direct-input reports have explicit unresolved states rather than the labour report's
            // single Unavailable sentinel. Only Resolved carries a usable direct-input figure;
            // NoKnownRecipe and CannotBePriced must retain the finished-good fallback.
            public bool HasDirectInputEstimate =>
                directInputs != null && directInputs.status == DirectInputCostStatus.Resolved;

            // A malformed/incomplete report must not turn an unavailable direct estimate into a
            // numeric zero. Normal report reads resolve this state or explicitly find no eligible
            // employees; the old whole bill is retained only as the defensive fallback.
            public int Margin => revenue +
                (HasDirectInputEstimate ? directInputsIfBought : inputsIfBought) +
                (HasDirectLaborEstimate ? directPayroll : payroll) +
                transport;

            /// <summary>
            /// Revenue less direct materials and relevant paid labour, per cycle. The transport
            /// premium is deliberately excluded because it is not a measured caravan cost.
            /// </summary>
            public int ProductionMargin => revenue + directInputsIfBought + directPayroll;
        }

        /// <summary>
        /// One Business row comparing a good's live seller commitments with completed production
        /// recorded in the ledger's rolling window. Rows are keyed by ThingDef because that is the
        /// identity used by <see cref="ProductionBucket"/>; agreements for the same good are summed.
        /// </summary>
        public class ProductionCommitment
        {
            public ThingDef thingDef;
            public float committedPerDay;
            public float completedPerDay;
            public bool hasRecordedProduction;
        }

        public enum DirectInputCostStatus
        {
            Resolved,
            NoKnownRecipe,
            CannotBePriced
        }

        public enum DirectInputPriceTier
        {
            None,
            RecentCompletedPurchaseMedian,
            ProcurementMarketEstimate,
            GenericMarketValue,
            Mixed
        }

        /// <summary>
        /// Replacement price for the immediate ingredients or construction materials of one output
        /// unit. This deliberately stops at the selected recipe's direct ingredients or the
        /// construction cost list; an intermediate good is priced as the good that production
        /// consumes rather than being recursively decomposed.
        /// </summary>
        public class DirectInputEstimate
        {
            public DirectInputCostStatus status;
            public float costPerUnit;
            public bool hasDirectInputs;
            public string recipeDefName;
            /// <summary>Names the vanilla construction route when it supplied the estimate.</summary>
            public string constructionRouteName;
            /// <summary>
            /// Price evidence used by the estimate. Mixed means different direct inputs used
            /// different tiers; <see cref="ingredientPriceTiers"/> preserves their route order.
            /// </summary>
            public DirectInputPriceTier priceTier;
            public List<DirectInputPriceTier> ingredientPriceTiers =
                new List<DirectInputPriceTier>();
            public string reason;
        }

        public enum DirectLaborCostStatus
        {
            Resolved,
            NoEligibleEmployees,
            LessThanOneSilver,
            Unavailable
        }

        /// <summary>
        /// Relevant-workforce approximation for one good over one agreement cycle. The cost is
        /// positive here, while <see cref="ContractEstimate.directPayroll"/> follows the report's
        /// negative-cost convention.
        /// </summary>
        public class DirectLaborEstimate
        {
            public DirectLaborCostStatus status;
            public float cost;
            public int eligibleEmployeeCount;
        }

        /// <summary>
        /// Estimates a contract's per-cycle economics.
        ///
        /// **Inputs are priced as "what buying it instead would cost", and that choice is the
        /// answer to §45's question rather than a shortcut around it.** The mod cannot see what a
        /// player's rice costs to grow — soil, work, a stove, a season — and any number it invented
        /// for that would be fiction with a decimal point. What it *can* say precisely is what the
        /// same goods would cost through procurement, which is exactly the alternative the player is
        /// weighing. The line reads "if you bought it" for that reason.
        /// </summary>
        public static ContractEstimate Estimate(IntercolonyWorldComponent state, RecurringContract contract)
        {
            ContractEstimate estimate = new ContractEstimate { contract = contract };
            if (contract == null)
            {
                return estimate;
            }

            estimate.revenue = contract.DiscountedCyclePayment;

            // Base value plus what a supplier marks up, using procurement's own constant so the
            // dashboard cannot recommend buying at a price procurement would not offer.
            float unit = IntercolonyPricing.BaseValue(contract.thingDef, contract.stuffDef) *
                         RfqService.SupplierMargin;
            estimate.inputsIfBought = -Mathf.RoundToInt(unit * contract.quantityPerCycle);

            estimate.directInputs = EstimateDirectInputs(
                state, contract.thingDef, contract.stuffDef);
            if (estimate.directInputs.status == DirectInputCostStatus.Resolved)
            {
                estimate.directInputsIfBought = -Mathf.RoundToInt(
                    estimate.directInputs.costPerUnit * contract.quantityPerCycle);
            }

            estimate.payroll = -PayrollForPeriod(state, contract.CadenceDays);
            estimate.directLabor = EstimateDirectLabor(
                state, contract.thingDef, contract.CadenceDays);
            if (estimate.directLabor.status == DirectLaborCostStatus.Resolved ||
                estimate.directLabor.status == DirectLaborCostStatus.LessThanOneSilver)
            {
                estimate.directPayroll = -Mathf.RoundToInt(estimate.directLabor.cost);
            }

            // §45's "should I deliver myself?". A recurring agreement is always seller-delivery, and
            // the price already carries a premium for that — so the transport line is that premium,
            // shown as the cost of the caravan trips that earn it. A quadrum of pickup orders would
            // show nothing here, which is the honest answer: nothing was hauled.
            float deliver = IntercolonyPricing.LogisticsFactor(FulfillmentMode.SellerDelivery).multiplier;
            float collect = IntercolonyPricing.LogisticsFactor(FulfillmentMode.BuyerPickup).multiplier;
            float premiumShare = deliver <= 0f ? 0f : (deliver - collect) / deliver;
            estimate.transport = -Mathf.RoundToInt(estimate.revenue * premiumShare);

            return estimate;
        }

        /// <summary>
        /// Estimates the wages of employees who could produce one good over a period.
        ///
        /// This is deliberately F20's relevant-workforce fallback, not actual-work attribution.
        /// It now covers both production routes: recipes that produce the good and the vanilla
        /// player-buildable construction route when the good has a blueprint.
        /// The only completion seam available to this mod is the postfix on
        /// RecordsUtility.Notify_BillDone, which receives the pawn who finished the bill and the
        /// products, but no elapsed time. Inferring time from a recipe's nominal work amount and
        /// choosing one recipe when several produce the good would stack two assumptions into a
        /// figure the player would read as measured. Per-tick job instrumentation would be the
        /// constant expensive polling F20 rules out, so the report uses current active employees'
        /// current skills and work priorities instead.
        ///
        /// Each eligible employee's daily wage is divided equally across the distinct current
        /// agreement goods that employee could produce. That prevents one wage being counted in
        /// full against every good while still combining all eligible employees for each good.
        /// </summary>
        public static DirectLaborEstimate EstimateDirectLabor(
            IntercolonyWorldComponent state, ThingDef product, float days)
        {
            DirectLaborEstimate estimate = new DirectLaborEstimate
            {
                status = DirectLaborCostStatus.Unavailable
            };

            if (state == null || product == null || days <= 0f)
            {
                return estimate;
            }

            List<ThingDef> goods = RelevantProductionGoods(state, product);
            Dictionary<ThingDef, List<RecipeDef>> recipesByGood =
                new Dictionary<ThingDef, List<RecipeDef>>();
            for (int i = 0; i < goods.Count; i++)
            {
                recipesByGood[goods[i]] = RecipesProducing(goods[i]);
            }

            Dictionary<RecipeDef, List<WorkTypeDef>> workTypesByRecipe =
                WorkTypesByRecipe(recipesByGood);

            List<RecipeDef> productRecipes = recipesByGood[product];
            if (productRecipes.Count == 0 && !IsPlayerBuildable(product))
            {
                estimate.status = DirectLaborCostStatus.Unavailable;
                return estimate;
            }

            float total = 0f;
            int eligibleEmployees = 0;
            if (state.Employments == null)
            {
                return estimate;
            }

            foreach (EmploymentContract employee in state.Employments)
            {
                if (employee == null || employee.status != EmploymentStatus.Active ||
                    employee.pawn == null ||
                    !CanProduceGood(
                        employee.pawn, product, productRecipes, workTypesByRecipe))
                {
                    continue;
                }

                int eligibleGoods = 0;
                for (int i = 0; i < goods.Count; i++)
                {
                    if (CanProduceGood(
                            employee.pawn, goods[i], recipesByGood[goods[i]], workTypesByRecipe))
                    {
                        eligibleGoods++;
                    }
                }

                if (eligibleGoods <= 0)
                {
                    continue;
                }

                total += employee.ChargedDailyWage * days / eligibleGoods;
                eligibleEmployees++;
            }

            estimate.eligibleEmployeeCount = eligibleEmployees;
            if (eligibleEmployees == 0)
            {
                estimate.status = DirectLaborCostStatus.NoEligibleEmployees;
                return estimate;
            }

            estimate.cost = total;
            estimate.status = total > 0f && Mathf.RoundToInt(total) == 0
                ? DirectLaborCostStatus.LessThanOneSilver
                : DirectLaborCostStatus.Resolved;
            return estimate;
        }

        /// <summary>
        /// Derives one seller agreement's commitment from the fields already stored on it:
        /// quantityPerCycle divided by its cadence length in days. The fallback of one day matches
        /// the existing contract presentation and prevents malformed zero-cadence state from
        /// producing a divide-by-zero result.
        /// </summary>
        public static float CommittedPerDay(RecurringContract contract)
        {
            if (contract == null || contract.thingDef == null || contract.quantityPerCycle <= 0)
            {
                return 0f;
            }

            return contract.quantityPerCycle / Mathf.Max(1f, contract.CadenceDays);
        }

        private static List<ThingDef> RelevantProductionGoods(
            IntercolonyWorldComponent state, ThingDef requestedProduct)
        {
            List<ThingDef> goods = new List<ThingDef>();
            if (state.Contracts != null)
            {
                foreach (RecurringContract contract in state.Contracts)
                {
                    if (!IsBusinessLive(contract) || contract.thingDef == null ||
                        goods.Contains(contract.thingDef))
                    {
                        continue;
                    }

                    goods.Add(contract.thingDef);
                }
            }

            if (requestedProduct != null && !goods.Contains(requestedProduct))
            {
                goods.Add(requestedProduct);
            }

            return goods;
        }

        private static bool IsPlayerBuildable(ThingDef product)
        {
            return product != null && product.blueprintDef != null;
        }

        private static List<RecipeDef> RecipesProducing(ThingDef product)
        {
            List<RecipeDef> recipes = new List<RecipeDef>();
            List<RecipeDef> allRecipes = DefDatabase<RecipeDef>.AllDefsListForReading;
            if (product == null || allRecipes == null)
            {
                return recipes;
            }

            for (int i = 0; i < allRecipes.Count; i++)
            {
                RecipeDef recipe = allRecipes[i];
                if (recipe == null || recipe.IsSurgery || recipe.products == null ||
                    FindProducedProduct(recipe, product) == null)
                {
                    continue;
                }

                recipes.Add(recipe);
            }

            return recipes;
        }

        private static bool CanProduceAny(
            Pawn pawn,
            List<RecipeDef> recipes,
            Dictionary<RecipeDef, List<WorkTypeDef>> workTypesByRecipe)
        {
            if (pawn == null || recipes == null || workTypesByRecipe == null)
            {
                return false;
            }

            for (int i = 0; i < recipes.Count; i++)
            {
                RecipeDef recipe = recipes[i];
                List<WorkTypeDef> workTypes;
                if (recipe == null || !workTypesByRecipe.TryGetValue(recipe, out workTypes) ||
                    !CanPerformRecipe(pawn, recipe, workTypes))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static bool CanProduceGood(
            Pawn pawn,
            ThingDef product,
            List<RecipeDef> recipes,
            Dictionary<RecipeDef, List<WorkTypeDef>> workTypesByRecipe)
        {
            return CanProduceAny(pawn, recipes, workTypesByRecipe) ||
                   (IsPlayerBuildable(product) && CanPerformConstruction(pawn, product));
        }

        private static Dictionary<RecipeDef, List<WorkTypeDef>> WorkTypesByRecipe(
            Dictionary<ThingDef, List<RecipeDef>> recipesByGood)
        {
            Dictionary<RecipeDef, List<WorkTypeDef>> result =
                new Dictionary<RecipeDef, List<WorkTypeDef>>();
            foreach (List<RecipeDef> recipes in recipesByGood.Values)
            {
                for (int i = 0; i < recipes.Count; i++)
                {
                    RecipeDef recipe = recipes[i];
                    if (recipe != null && !result.ContainsKey(recipe))
                    {
                        result[recipe] = WorkTypesForRecipe(recipe);
                    }
                }
            }

            return result;
        }

        private static List<WorkTypeDef> WorkTypesForRecipe(RecipeDef recipe)
        {
            List<WorkTypeDef> workTypes = new List<WorkTypeDef>();
            if (recipe == null)
            {
                return workTypes;
            }

            if (recipe.requiredGiverWorkType != null)
            {
                workTypes.Add(recipe.requiredGiverWorkType);
                return workTypes;
            }

            // A recipe without requiredGiverWorkType can still be restricted by the bill giver:
            // vanilla stores that work type on WorkGiverDef, whose fixed bill-giver defs intersect
            // RecipeDef.recipeUsers. If no such static link exists, the recipe has not given us a
            // defensible work-type gate, so skill requirements remain the only gate.
            if (recipe.recipeUsers == null)
            {
                return workTypes;
            }

            List<WorkGiverDef> workGivers = DefDatabase<WorkGiverDef>.AllDefsListForReading;
            if (workGivers == null)
            {
                return workTypes;
            }

            for (int i = 0; i < workGivers.Count; i++)
            {
                WorkGiverDef workGiver = workGivers[i];
                if (workGiver == null || workGiver.workType == null ||
                    workGiver.giverClass == null ||
                    !typeof(WorkGiver_DoBill).IsAssignableFrom(workGiver.giverClass) ||
                    workGiver.fixedBillGiverDefs == null)
                {
                    continue;
                }

                for (int j = 0; j < recipe.recipeUsers.Count; j++)
                {
                    if (recipe.recipeUsers[j] != null &&
                        workGiver.fixedBillGiverDefs.Contains(recipe.recipeUsers[j]))
                    {
                        if (!workTypes.Contains(workGiver.workType))
                        {
                            workTypes.Add(workGiver.workType);
                        }

                        break;
                    }
                }
            }

            return workTypes;
        }

        private static bool CanPerformRecipe(
            Pawn pawn, RecipeDef recipe, List<WorkTypeDef> workTypes)
        {
            if (pawn == null || recipe == null)
            {
                return false;
            }

            // EmploymentContract.workerSkills is a frozen display string, not a structured
            // hiring requirement. The live pawn is the only current record of what this employee
            // can do, so use its actual skill record and current work assignment here.
            if (recipe.workSkill != null)
            {
                if (pawn.skills == null)
                {
                    return false;
                }

                SkillRecord workSkill = pawn.skills.GetSkill(recipe.workSkill);
                if (workSkill == null || workSkill.TotallyDisabled)
                {
                    return false;
                }
            }

            if (workTypes != null && workTypes.Count > 0)
            {
                bool canDoWorkType = false;
                for (int i = 0; i < workTypes.Count; i++)
                {
                    if (CanPerformWorkType(pawn, workTypes[i]))
                    {
                        canDoWorkType = true;
                        break;
                    }
                }

                if (!canDoWorkType)
                {
                    return false;
                }
            }

            return recipe.PawnSatisfiesSkillRequirements(pawn);
        }

        private static bool CanPerformWorkType(Pawn pawn, WorkTypeDef workType)
        {
            return pawn != null && workType != null && pawn.workSettings != null &&
                   !pawn.WorkTypeIsDisabled(workType) &&
                   pawn.workSettings.WorkIsActive(workType);
        }

        private static bool CanPerformConstruction(Pawn pawn, ThingDef product)
        {
            if (!CanPerformWorkType(pawn, WorkTypeDefOf.Construction))
            {
                return false;
            }

            int requiredSkill = product.constructionSkillPrerequisite;
            if (requiredSkill <= 0)
            {
                return true;
            }

            if (pawn.skills != null)
            {
                SkillRecord constructionSkill = pawn.skills.GetSkill(SkillDefOf.Construction);
                if (constructionSkill == null || constructionSkill.Level < requiredSkill)
                {
                    return false;
                }
            }
            else if (!pawn.IsColonyMech)
            {
                return false;
            }

            return !pawn.IsColonyMech ||
                   pawn.RaceProps.mechFixedSkillLevel >= requiredSkill;
        }

        /// <summary>
        /// Prices one product's direct recipe inputs or construction materials as replacement
        /// purchases. Recipe selection is deterministic: the non-surgery recipe with the smallest
        /// ordinal defName that produces the product is selected. An ingredient filter may allow
        /// several definitions; the lowest priced acceptable definition wins, with ordinal defName
        /// as its tie-breaker.
        /// </summary>
        public static DirectInputEstimate EstimateDirectInputs(ThingDef product, ThingDef stuffDef)
        {
            return EstimateDirectInputs(
                IntercolonyWorldComponent.Current, product, stuffDef);
        }

        /// <summary>
        /// State-aware overload used by contract reports so direct inputs can read the colony's
        /// purchase history and already-published procurement quotes without creating new state.
        /// </summary>
        public static DirectInputEstimate EstimateDirectInputs(
            IntercolonyWorldComponent state, ThingDef product, ThingDef stuffDef)
        {
            DirectInputEstimate estimate = new DirectInputEstimate();
            if (product == null)
            {
                estimate.status = DirectInputCostStatus.CannotBePriced;
                estimate.reason = "The product definition is unavailable.";
                return estimate;
            }

            RecipeDef recipe = FindDirectInputRecipe(product);
            if (recipe == null)
            {
                if (product.MadeFromStuff && stuffDef == null)
                {
                    // Do not pass null stuff to vanilla's defaulting overload: it logs an error
                    // for a stuffable product, and a clean report cannot tolerate that fallback.
                    estimate.status = DirectInputCostStatus.CannotBePriced;
                    estimate.reason = "The construction route cannot be priced because the contract has no stuffDef for this stuffable product.";
                    return estimate;
                }

                if (!IsPlayerBuildable(product))
                {
                    estimate.status = DirectInputCostStatus.NoKnownRecipe;
                    estimate.reason = "No non-surgery recipe in the loaded defs produces this good, and the product is not player-buildable.";
                    return estimate;
                }

                List<ThingDefCountClass> costList = product.CostListAdjusted(stuffDef);
                if (costList == null || costList.Count == 0)
                {
                    estimate.status = DirectInputCostStatus.CannotBePriced;
                    estimate.reason = "The player-buildable product does not expose a construction cost list.";
                    return estimate;
                }

                float constructionInputCost = 0f;
                for (int i = 0; i < costList.Count; i++)
                {
                    ThingDefCountClass costEntry = costList[i];
                    float ingredientCost;
                    DirectInputPriceTier priceTier;
                    if (!TryPriceDirectIngredient(
                            state, costEntry, out ingredientCost, out priceTier))
                    {
                        estimate.status = DirectInputCostStatus.CannotBePriced;
                        estimate.reason = "At least one construction material has no usable definition, count, or BaseValue price.";
                        return estimate;
                    }

                    RecordIngredientPriceTier(estimate, priceTier);
                    constructionInputCost += ingredientCost;
                    if (!IsUsablePositive(constructionInputCost))
                    {
                        estimate.status = DirectInputCostStatus.CannotBePriced;
                        estimate.reason = "The construction material total is not a usable price.";
                        return estimate;
                    }
                }

                estimate.status = DirectInputCostStatus.Resolved;
                estimate.hasDirectInputs = true;
                estimate.costPerUnit = constructionInputCost;
                estimate.constructionRouteName = "CostListAdjusted";
                return estimate;
            }

            estimate.recipeDefName = recipe.defName;
            ThingDefCountClass productEntry = FindProducedProduct(recipe, product);
            if (productEntry == null || productEntry.count <= 0 || recipe.ingredients == null)
            {
                estimate.status = DirectInputCostStatus.CannotBePriced;
                estimate.reason = "The selected recipe does not expose a usable product or ingredient list.";
                return estimate;
            }

            if (recipe.ingredients.Count == 0)
            {
                estimate.status = DirectInputCostStatus.Resolved;
                estimate.hasDirectInputs = false;
                return estimate;
            }

            float recipeInputCost = 0f;
            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                IngredientCount ingredient = recipe.ingredients[i];
                float ingredientCost;
                DirectInputPriceTier priceTier;
                if (!TryPriceDirectIngredient(
                        state, ingredient, recipe, out ingredientCost, out priceTier))
                {
                    estimate.status = DirectInputCostStatus.CannotBePriced;
                    estimate.reason = "At least one direct ingredient has no usable allowed definition or BaseValue price.";
                    return estimate;
                }

                RecordIngredientPriceTier(estimate, priceTier);
                recipeInputCost += ingredientCost;
                if (!IsUsablePositive(recipeInputCost))
                {
                    estimate.status = DirectInputCostStatus.CannotBePriced;
                    estimate.reason = "The selected recipe's direct input total is not a usable price.";
                    return estimate;
                }
            }

            float costPerUnit = recipeInputCost / productEntry.count;
            if (!IsUsablePositive(costPerUnit))
            {
                estimate.status = DirectInputCostStatus.CannotBePriced;
                estimate.reason = "The selected recipe's output count does not produce a usable unit price.";
                return estimate;
            }

            estimate.status = DirectInputCostStatus.Resolved;
            estimate.hasDirectInputs = true;
            estimate.costPerUnit = costPerUnit;
            return estimate;
        }

        private static RecipeDef FindDirectInputRecipe(ThingDef product)
        {
            RecipeDef selected = null;
            List<RecipeDef> recipes = DefDatabase<RecipeDef>.AllDefsListForReading;
            if (recipes == null)
            {
                return null;
            }

            for (int i = 0; i < recipes.Count; i++)
            {
                RecipeDef candidate = recipes[i];
                if (candidate == null || candidate.IsSurgery || candidate.products == null)
                {
                    continue;
                }

                if (FindProducedProduct(candidate, product) == null)
                {
                    continue;
                }

                if (selected == null ||
                    string.CompareOrdinal(candidate.defName ?? string.Empty,
                        selected.defName ?? string.Empty) < 0)
                {
                    selected = candidate;
                }
            }

            return selected;
        }

        private static ThingDefCountClass FindProducedProduct(RecipeDef recipe, ThingDef product)
        {
            if (recipe == null || recipe.products == null)
            {
                return null;
            }

            for (int i = 0; i < recipe.products.Count; i++)
            {
                ThingDefCountClass productEntry = recipe.products[i];
                if (productEntry != null && productEntry.thingDef == product && productEntry.count > 0)
                {
                    return productEntry;
                }
            }

            return null;
        }

        private static void RecordIngredientPriceTier(
            DirectInputEstimate estimate, DirectInputPriceTier priceTier)
        {
            if (estimate == null || priceTier == DirectInputPriceTier.None)
            {
                return;
            }

            if (estimate.ingredientPriceTiers == null)
            {
                estimate.ingredientPriceTiers = new List<DirectInputPriceTier>();
            }

            estimate.ingredientPriceTiers.Add(priceTier);
            if (estimate.ingredientPriceTiers.Count == 1)
            {
                estimate.priceTier = priceTier;
            }
            else if (estimate.priceTier != priceTier)
            {
                estimate.priceTier = DirectInputPriceTier.Mixed;
            }
        }

        private static bool TryPriceDirectIngredient(
            IntercolonyWorldComponent state,
            IngredientCount ingredient,
            RecipeDef recipe,
            out float ingredientCost,
            out DirectInputPriceTier priceTier)
        {
            ingredientCost = 0f;
            priceTier = DirectInputPriceTier.None;
            if (ingredient == null || ingredient.filter == null)
            {
                return false;
            }

            List<ThingDef> allowedDefs = new List<ThingDef>();
            foreach (ThingDef allowedDef in ingredient.filter.AllowedThingDefs)
            {
                if (allowedDef != null)
                {
                    allowedDefs.Add(allowedDef);
                }
            }

            allowedDefs.Sort((left, right) => string.CompareOrdinal(
                left.defName ?? string.Empty, right.defName ?? string.Empty));

            bool foundPrice = false;
            ThingDef selectedDef = null;
            float selectedCost = 0f;
            for (int i = 0; i < allowedDefs.Count; i++)
            {
                ThingDef allowedDef = allowedDefs[i];
                int requiredCount;
                try
                {
                    requiredCount = ingredient.CountRequiredOfFor(allowedDef, recipe);
                }
                catch (Exception)
                {
                    continue;
                }

                if (requiredCount <= 0)
                {
                    continue;
                }

                float candidateCost;
                DirectInputPriceTier candidateTier;
                if (!TryPriceDirectIngredient(
                        state, allowedDef, requiredCount, out candidateCost, out candidateTier))
                {
                    continue;
                }

                if (!foundPrice || candidateCost < selectedCost ||
                    (candidateCost == selectedCost &&
                     string.CompareOrdinal(allowedDef.defName ?? string.Empty,
                         selectedDef.defName ?? string.Empty) < 0))
                {
                    foundPrice = true;
                    selectedDef = allowedDef;
                    selectedCost = candidateCost;
                    priceTier = candidateTier;
                }
            }

            if (!foundPrice)
            {
                return false;
            }

            ingredientCost = selectedCost;
            return true;
        }

        private static bool TryPriceDirectIngredient(
            IntercolonyWorldComponent state,
            ThingDefCountClass ingredient,
            out float ingredientCost,
            out DirectInputPriceTier priceTier)
        {
            ingredientCost = 0f;
            priceTier = DirectInputPriceTier.None;
            if (ingredient == null)
            {
                return false;
            }

            return TryPriceDirectIngredient(
                state, ingredient.thingDef, ingredient.count,
                out ingredientCost, out priceTier);
        }

        private static bool TryPriceDirectIngredient(
            IntercolonyWorldComponent state,
            ThingDef ingredientDef,
            int requiredCount,
            out float ingredientCost,
            out DirectInputPriceTier priceTier)
        {
            ingredientCost = 0f;
            priceTier = DirectInputPriceTier.None;
            if (ingredientDef == null || requiredCount <= 0)
            {
                return false;
            }

            float unitPrice;
            if (!TryPriceDirectIngredientUnit(
                    state, ingredientDef, null, out unitPrice, out priceTier))
            {
                return false;
            }

            float candidateCost = priceTier == DirectInputPriceTier.GenericMarketValue
                ? requiredCount * unitPrice * RfqService.SupplierMargin
                : requiredCount * unitPrice;
            if (!IsUsablePositive(candidateCost))
            {
                return false;
            }

            ingredientCost = candidateCost;
            return true;
        }

        private static bool TryPriceDirectIngredientUnit(
            IntercolonyWorldComponent state,
            ThingDef ingredientDef,
            ThingDef stuffDef,
            out float unitPrice,
            out DirectInputPriceTier priceTier)
        {
            unitPrice = 0f;
            priceTier = DirectInputPriceTier.None;

            if (ingredientDef == null)
            {
                return false;
            }

            if (TryGetRecentPurchaseMedianUnitPrice(
                    state, ingredientDef, stuffDef, out unitPrice))
            {
                priceTier = DirectInputPriceTier.RecentCompletedPurchaseMedian;
                return true;
            }

            if (TryGetCurrentProcurementMarketUnitPrice(
                    state, ingredientDef, stuffDef, out unitPrice))
            {
                priceTier = DirectInputPriceTier.ProcurementMarketEstimate;
                return true;
            }

            float unitBaseValue = IntercolonyPricing.BaseValue(ingredientDef, stuffDef);
            if (!IsUsablePositive(unitBaseValue))
            {
                return false;
            }

            // Keep the old fallback expression exactly: absent evidence, direct-input pricing
            // must remain BaseValue multiplied by procurement's existing supplier margin.
            unitPrice = unitBaseValue;
            priceTier = DirectInputPriceTier.GenericMarketValue;
            return true;
        }

        private static bool TryGetRecentPurchaseMedianUnitPrice(
            IntercolonyWorldComponent state,
            ThingDef ingredientDef,
            ThingDef stuffDef,
            out float unitPrice)
        {
            unitPrice = 0f;
            if (state?.PurchaseOrders == null)
            {
                return false;
            }

            int nowTick = GenTicks.TicksGame;
            List<float> prices = new List<float>();
            foreach (PurchaseOrder order in state.PurchaseOrders)
            {
                if (order == null || order.status != PurchaseOrderStatus.Completed ||
                    order.quantity <= 0 || order.orderedTick < 0 ||
                    !MatchesDirectIngredientSpecification(order, ingredientDef, stuffDef))
                {
                    continue;
                }

                long ageTicks = (long)nowTick - order.orderedTick;
                if (ageTicks < 0 || ageTicks > RecentPurchaseWindowTicks ||
                    !IsUsablePositive(order.unitPrice))
                {
                    continue;
                }

                prices.Add(order.unitPrice);
            }

            if (prices.Count == 0)
            {
                return false;
            }

            // This local list is a copy of the stored prices. Sort it and take the lower middle
            // for an even count, so the median is always a price somebody actually paid rather
            // than an average invented between two purchases.
            prices.Sort();
            unitPrice = prices[(prices.Count - 1) / 2];
            return IsUsablePositive(unitPrice);
        }

        private static bool MatchesDirectIngredientSpecification(
            PurchaseOrder order, ThingDef ingredientDef, ThingDef stuffDef)
        {
            // Direct raw ingredients have no quality requirement. Nullable equality is deliberate:
            // a quality-bearing order is not evidence for a quality-less ingredient. This follows
            // the existing def/stuff/quality comparison used by EmploymentEquipment.Matches.
            return order != null && order.thingDef == ingredientDef &&
                   order.stuffDef == stuffDef && order.quality == null;
        }

        private static bool TryGetCurrentProcurementMarketUnitPrice(
            IntercolonyWorldComponent state,
            ThingDef ingredientDef,
            ThingDef stuffDef,
            out float unitPrice)
        {
            unitPrice = 0f;
            if (state == null)
            {
                return false;
            }

            bool found = false;
            if (state.SupplierListings != null)
            {
                foreach (SupplierListing listing in state.SupplierListings)
                {
                    if (listing == null || !listing.IsAvailable ||
                        listing.refreshWindow != state.RefreshCount ||
                        listing.thingDef != ingredientDef || listing.stuffDef != stuffDef ||
                        listing.quality.HasValue || !IsUsablePositive(listing.unitPrice) ||
                        !IsCurrentProcurementSupplier(listing.settlementId))
                    {
                        continue;
                    }

                    if (!found || listing.unitPrice < unitPrice)
                    {
                        found = true;
                        unitPrice = listing.unitPrice;
                    }
                }
            }

            int nowTick = GenTicks.TicksGame;
            if (state.Requests != null)
            {
                foreach (PurchaseRequest request in state.Requests)
                {
                    if (request == null || !request.IsOpen || request.HasExpired(nowTick) ||
                        request.thingDef != ingredientDef || request.stuffDef != stuffDef ||
                        request.quotes == null)
                    {
                        continue;
                    }

                    foreach (Quotation quote in request.quotes)
                    {
                        if (quote == null || quote.quantityOffered <= 0 ||
                            quote.offeredStuff != stuffDef || quote.offeredQuality.HasValue ||
                            !IsUsablePositive(quote.unitPrice) ||
                            !IsCurrentProcurementSupplier(quote.settlementId))
                        {
                            continue;
                        }

                        if (!found || quote.unitPrice < unitPrice)
                        {
                            found = true;
                            unitPrice = quote.unitPrice;
                        }
                    }
                }
            }

            // SupplierListing/Quotation.unitPrice is already the supplier quote produced by
            // IntercolonyPricing.SupplierUnitPrice. Read that published value; do not call the
            // generator or SupplierUnitPrice again, because its negotiation roll would perturb
            // global RNG and a new request would mutate procurement state.
            return found && IsUsablePositive(unitPrice);
        }

        private static bool IsCurrentProcurementSupplier(int settlementId)
        {
            var settlement = IntercolonyMarketAccess.FindSettlement(settlementId);
            return settlement != null && IntercolonyMarketAccess.IsAccessible(settlement);
        }

        private static bool IsUsablePositive(float value)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        /// <summary>
        /// Returns one row per good with a Business-live seller commitment. This deliberately shares
        /// the same active predicate as the contract economics report, including suspended
        /// agreements, so the Business page does not define "live" two different ways.
        /// </summary>
        public static List<ProductionCommitment> ActiveProductionCommitments(
            IntercolonyWorldComponent state)
        {
            List<ProductionCommitment> rows = new List<ProductionCommitment>();
            if (state == null || state.Contracts == null)
            {
                return rows;
            }

            foreach (RecurringContract contract in state.Contracts)
            {
                if (!IsBusinessLive(contract))
                {
                    continue;
                }

                float committedPerDay = CommittedPerDay(contract);
                if (committedPerDay <= 0f)
                {
                    continue;
                }

                ProductionCommitment row = rows.Find(
                    existing => existing.thingDef == contract.thingDef);
                if (row == null)
                {
                    row = new ProductionCommitment
                    {
                        thingDef = contract.thingDef
                    };
                    rows.Add(row);
                }

                row.committedPerDay += committedPerDay;
            }

            foreach (ProductionCommitment row in rows)
            {
                row.hasRecordedProduction = ProductionLedgerService.HasRecordedProduction(
                    state, row.thingDef);
                row.completedPerDay = ProductionLedgerService.CompletedPerDay(state, row.thingDef);
            }

            return rows;
        }

        /// <summary>
        /// The colony's charged wage bill over a stretch of days.
        ///
        /// Every active employee, not a share apportioned to one contract. Apportioning would need
        /// the mod to know who works on what, which it does not — and a made-up allocation is worse
        /// than an honest total, because it looks precise. The dashboard labels it as the whole
        /// wage bill so the comparison the player makes is the true one: does this agreement cover
        /// what the workforce costs.
        /// </summary>
        public static int PayrollForPeriod(IntercolonyWorldComponent state, float days)
        {
            if (state == null || days <= 0f)
            {
                return 0;
            }

            int daily = 0;
            foreach (EmploymentContract contract in state.Employments)
            {
                if (contract.status == EmploymentStatus.Active)
                {
                    daily += contract.ChargedDailyWage;
                }
            }

            return Mathf.RoundToInt(daily * days);
        }

        /// <summary>Every live agreement, estimated, best margin first.</summary>
        public static List<ContractEstimate> ActiveEstimates(IntercolonyWorldComponent state)
        {
            List<ContractEstimate> estimates = new List<ContractEstimate>();
            if (state == null)
            {
                return estimates;
            }

            foreach (RecurringContract contract in state.Contracts)
            {
                if (IsBusinessLive(contract))
                {
                    estimates.Add(Estimate(state, contract));
                }
            }

            estimates.Sort((a, b) => b.Margin.CompareTo(a.Margin));
            return estimates;
        }

        private static bool IsBusinessLive(RecurringContract contract)
        {
            // Suspension pauses delivery, but the Business view already treats the agreement as
            // live for its other economics; F07 must use that same definition of commitment.
            return contract != null &&
                   (contract.IsActive || contract.status == ContractStatus.Suspended);
        }

        // --- Backward-looking helpers (§75, §117) ------------------------------------------

        /// <summary>
        /// The colony's current daily wage commitment, for the "should I hire?" question.
        /// </summary>
        public static int DailyWageBill(IntercolonyWorldComponent state)
        {
            return PayrollForPeriod(state, 1f);
        }

        /// <summary>
        /// Silver on hand across every player map, so the report can say how long the wage bill is
        /// covered for. That runway figure is the single most useful thing the dashboard can tell a
        /// player who is deciding whether to take on another worker.
        /// </summary>
        public static int SilverOnHand()
        {
            int total = 0;
            foreach (Map map in Find.Maps)
            {
                if (map.IsPlayerHome)
                {
                    total += PurchaseOrderService.CountColonySilver(map);
                }
            }

            return total;
        }

        /// <summary>
        /// Days the colony can meet payroll for out of what it holds, or -1 when nothing is owed.
        /// </summary>
        public static float PayrollRunwayDays(IntercolonyWorldComponent state)
        {
            int daily = DailyWageBill(state);
            return daily <= 0 ? -1f : SilverOnHand() / (float)daily;
        }
    }
}
