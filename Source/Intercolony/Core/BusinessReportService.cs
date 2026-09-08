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

            /// <summary>What the selected recipe's direct inputs would cost. Negative when known.</summary>
            public int directInputsIfBought;

            /// <summary>Direct-input price and resolution state for the contracted good.</summary>
            public DirectInputEstimate directInputs;

            /// <summary>Share of the wage bill this cycle would cover. Negative.</summary>
            public int payroll;

            /// <summary>The delivery premium, shown as what hauling it yourself is worth. Negative.</summary>
            public int transport;

            public int Margin => revenue + inputsIfBought + payroll + transport;

            /// <summary>What making the goods rather than buying them is worth, per cycle.</summary>
            public int MakingSaves => -inputsIfBought;

            public float CadenceDays => contract?.CadenceDays ?? 1f;

            public float MarginPerDay => CadenceDays <= 0f ? 0f : Margin / CadenceDays;
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

        /// <summary>
        /// Replacement price for the immediate ingredients of one output unit. This deliberately
        /// stops at the selected recipe's direct ingredients; an intermediate good is priced as the
        /// good that production consumes rather than being recursively decomposed.
        /// </summary>
        public class DirectInputEstimate
        {
            public DirectInputCostStatus status;
            public float costPerUnit;
            public bool hasDirectInputs;
            public string recipeDefName;
            public string reason;
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

            estimate.directInputs = EstimateDirectInputs(contract.thingDef);
            if (estimate.directInputs.status == DirectInputCostStatus.Resolved)
            {
                estimate.directInputsIfBought = -Mathf.RoundToInt(
                    estimate.directInputs.costPerUnit * contract.quantityPerCycle);
            }

            estimate.payroll = -PayrollForPeriod(state, contract.CadenceDays);

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

        /// <summary>
        /// Prices one product's direct recipe inputs as replacement purchases. Recipe selection is
        /// deterministic: the non-surgery recipe with the smallest ordinal defName that produces
        /// the product is selected. An ingredient filter may allow several definitions; the lowest
        /// priced acceptable definition wins, with ordinal defName as its tie-breaker.
        /// </summary>
        public static DirectInputEstimate EstimateDirectInputs(ThingDef product)
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
                estimate.status = DirectInputCostStatus.NoKnownRecipe;
                estimate.reason = "No non-surgery recipe in the loaded defs produces this good.";
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
                if (!TryPriceDirectIngredient(ingredient, recipe, out ingredientCost))
                {
                    estimate.status = DirectInputCostStatus.CannotBePriced;
                    estimate.reason = "At least one direct ingredient has no usable allowed definition or BaseValue price.";
                    return estimate;
                }

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

        private static bool TryPriceDirectIngredient(
            IngredientCount ingredient,
            RecipeDef recipe,
            out float ingredientCost)
        {
            ingredientCost = 0f;
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

                float unitBaseValue = IntercolonyPricing.BaseValue(allowedDef, null);
                if (!IsUsablePositive(unitBaseValue))
                {
                    continue;
                }

                float candidateCost = requiredCount * unitBaseValue * RfqService.SupplierMargin;
                if (!IsUsablePositive(candidateCost))
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
                }
            }

            if (!foundPrice)
            {
                return false;
            }

            ingredientCost = selectedCost;
            return true;
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
        /// The colony's wage bill over a stretch of days.
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
                    daily += contract.dailyWage;
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
