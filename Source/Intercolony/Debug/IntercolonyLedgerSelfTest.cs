using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// End-to-end check of Phase 24 (DESIGN.md §117, §75, §45).
    ///
    /// A ledger's only virtue is being right, and the way a ledger goes wrong is quietly: a movement
    /// recorded twice, one recorded with the wrong sign, or one that never gets recorded at all
    /// because a new payment path forgot to. None of those throw, and all of them produce a report
    /// that looks perfectly plausible.
    ///
    /// So the load-bearing test here is not "does Summarise add up" — it is **does the ledger agree
    /// with the colony's actual silver**. A real payment is put through the real service and the
    /// storage count is measured either side of it.
    /// </summary>
    public static class IntercolonyLedgerSelfTest
    {
        private class Results
        {
            public readonly StringBuilder sb = new StringBuilder();
            public int passed;
            public int failed;
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

        public static string Run(IntercolonyWorldComponent state, Map map)
        {
            Results r = new Results();
            r.sb.AppendLine("Ledger and business report self-test (§117, §75, §45)");

            if (state == null || map == null)
            {
                r.sb.AppendLine("  No world or map. Open a colony first.");
                return Summarize(r);
            }

            int savedCount = state.Ledger.Count;
            int savedStart = state.LedgerStartTick;

            try
            {
                CheckRecording(r, state);
                CheckWindowing(r, state);
                CheckPartialHistoryIsAdmitted(r, state);
                CheckAgreesWithRealSilver(r, state, map);
                CheckContractEstimate(r, state);
                CheckDirectInputEstimate(r, state);
                CheckDirectLaborAttribution(r, state);
                CheckProductionCommitments(r, state);
                CheckPruning(r, state);
            }
            catch (System.Exception ex)
            {
                r.sb.AppendLine($"  EXCEPTION: {ex}");
                r.failed++;
            }
            finally
            {
                while (state.Ledger.Count > savedCount)
                {
                    state.Ledger.RemoveAt(state.Ledger.Count - 1);
                }

                state.LedgerStartTick = savedStart;
                r.Info($"ledger restored to {state.Ledger.Count} entr(ies).");
            }

            return Summarize(r);
        }

        // --- Recording ---------------------------------------------------------------------

        private static void CheckRecording(Results r, IntercolonyWorldComponent state)
        {
            int before = state.Ledger.Count;

            LedgerService.Record(state, LedgerKind.SalePayment, 500, "Testholme", "probe");
            LedgerService.Record(state, LedgerKind.PurchasePayment, -200, "Testholme", "probe");

            r.Check(state.Ledger.Count == before + 2,
                "movements are recorded (§75)", $"{state.Ledger.Count - before} added");

            // A zero movement is not an event. Without this the ledger fills with rows for payments
            // that did not happen — a payroll run that paid nothing, a refund of nothing — and the
            // detail list becomes unreadable.
            LedgerService.Record(state, LedgerKind.WagePayment, 0, "Testholme", "nothing");
            r.Check(state.Ledger.Count == before + 2,
                "a zero movement records nothing");

            LedgerEntry entry = state.Ledger[state.Ledger.Count - 1];
            r.Check(entry.amount == -200 && !entry.IsIncome,
                "outgoings are stored negative and read as outgoings",
                $"{entry.amount}");

            r.Check(state.LedgerStartTick >= 0,
                "the first entry stamps when history began");
        }

        private static void CheckWindowing(Results r, IntercolonyWorldComponent state)
        {
            int start = state.Ledger.Count;

            // Baselines first. Summarise reports the *whole* ledger, so on any colony that has
            // actually traded these assertions were measuring the world's real sales plus the
            // fixtures below. In the first full-suite run that read 13,851 against an expected
            // ceiling of 1,700 — the windowing was working perfectly and the assertion was
            // measuring the wrong thing. Everything below is a delta.
            int quadrumBefore = LedgerService
                .Summarise(state, BusinessReportService.QuadrumDays).Of(LedgerKind.SalePayment);
            int yearBefore = LedgerService
                .Summarise(state, BusinessReportService.YearDays).Of(LedgerKind.SalePayment);

            // Inside the quadrum window.
            state.Ledger.Add(Aged(LedgerKind.SalePayment, 1000, 3));
            // Inside the year window but outside the quadrum.
            state.Ledger.Add(Aged(LedgerKind.SalePayment, 700, 40));
            // Outside both.
            state.Ledger.Add(Aged(LedgerKind.SalePayment, 9999, 400));

            int quadrumAdded = LedgerService
                .Summarise(state, BusinessReportService.QuadrumDays)
                .Of(LedgerKind.SalePayment) - quadrumBefore;
            int yearAdded = LedgerService
                .Summarise(state, BusinessReportService.YearDays)
                .Of(LedgerKind.SalePayment) - yearBefore;

            r.Check(quadrumAdded == 1000,
                "the quadrum window excludes older movements",
                $"{quadrumAdded} of the 11,699 added fell inside the quadrum");

            r.Check(yearAdded == 1700,
                "the year window includes what the quadrum leaves out",
                $"{yearAdded} of the 11,699 added fell inside the year");

            r.Check(yearAdded < 9999,
                "neither window includes movements older than a year",
                "the 400-day entry is excluded");

            // The bottom line has to be the sum of the parts, or §117's report lies about itself.
            LedgerService.Report report =
                LedgerService.Summarise(state, BusinessReportService.YearDays);
            int summed = 0;
            foreach (LedgerKind kind in LedgerEntry.ReportOrder)
            {
                summed += report.Of(kind);
            }

            r.Check(summed == report.Net,
                "the net line equals the sum of the lines above it (§117)",
                $"{summed} vs {report.Net}");

            while (state.Ledger.Count > start)
            {
                state.Ledger.RemoveAt(state.Ledger.Count - 1);
            }
        }

        /// <summary>
        /// A young colony must not be shown a confident quarter.
        ///
        /// This is the one piece of honesty the report cannot get from arithmetic: twelve days of
        /// trading summed under the heading "last quadrum" is not a wrong number, it is a wrong
        /// claim, and a player comparing it against a target would be comparing against nothing.
        /// </summary>
        private static void CheckPartialHistoryIsAdmitted(Results r, IntercolonyWorldComponent state)
        {
            int savedStart = state.LedgerStartTick;

            state.LedgerStartTick = GenTicks.TicksGame - 5 * GenDate.TicksPerDay;
            LedgerService.Report young =
                LedgerService.Summarise(state, BusinessReportService.QuadrumDays);

            r.Check(young.partial,
                "a five-day-old ledger reports a quadrum as partial (§117)",
                $"{young.daysCovered:0} days covered");

            state.LedgerStartTick = GenTicks.TicksGame - 200 * GenDate.TicksPerDay;
            LedgerService.Report mature =
                LedgerService.Summarise(state, BusinessReportService.QuadrumDays);

            r.Check(!mature.partial,
                "an established ledger reports a full period");

            state.LedgerStartTick = savedStart;
        }

        // --- The one that matters ----------------------------------------------------------

        /// <summary>
        /// The ledger must agree with the colony's actual silver.
        ///
        /// Everything else in this file tests arithmetic on numbers the test itself supplied. This
        /// drives a **real** payment through the **real** service and measures storage either side,
        /// because the way a ledger goes wrong is by disagreeing with reality — a sign flipped, a
        /// payment recorded twice, or a new payment path that forgot to record at all. None of those
        /// throw, and all of them leave a report that looks fine.
        /// </summary>
        private static void CheckAgreesWithRealSilver(Results r, IntercolonyWorldComponent state, Map map)
        {
            IntercolonyLaborSelfTestSupport.ResetLedger();
            IntercolonyLaborSelfTestSupport.EnsureSilver(map, 600);

            int silverBefore = PurchaseOrderService.CountColonySilver(map);
            if (silverBefore < 500)
            {
                r.Info("silver agreement skipped: could not stage enough silver.");
                IntercolonyLaborSelfTestSupport.RestoreLedger(map);
                return;
            }

            int ledgerBefore = state.Ledger.Count;

            // A debt settlement is the cleanest real payment to drive: one call, one movement, no
            // pawn, no contract lifecycle to unwind afterwards.
            LaborDebt debt = new LaborDebt
            {
                id = -860,
                settlementId = -1,
                settlementName = "Testholme",
                factionName = "Test Confederacy",
                workerName = "Probe",
                kind = LaborDebtKind.Wages,
                amountOwed = 300,
                originalAmount = 300,
                incurredTick = GenTicks.TicksGame
            };

            bool paid = PayrollService.TrySettleDebt(debt, map, out string failReason);
            r.Check(paid, "a real payment went through", failReason ?? "");

            if (!paid)
            {
                IntercolonyLaborSelfTestSupport.RestoreLedger(map);
                return;
            }

            int silverAfter = PurchaseOrderService.CountColonySilver(map);
            int actuallyLeft = silverBefore - silverAfter;

            int recorded = 0;
            for (int i = ledgerBefore; i < state.Ledger.Count; i++)
            {
                recorded += state.Ledger[i].amount;
            }

            r.Check(state.Ledger.Count == ledgerBefore + 1,
                "one payment produced exactly one ledger entry",
                $"{state.Ledger.Count - ledgerBefore} entr(ies)");

            r.Check(recorded == -actuallyLeft,
                "the ledger agrees with the silver that actually left storage (§75)",
                $"recorded {recorded}, storage fell by {actuallyLeft}");

            r.Check(recorded < 0,
                "money going out is recorded as going out",
                $"{recorded}");

            IntercolonyLaborSelfTestSupport.RestoreLedger(map);
            IntercolonyLaborSelfTestSupport.ResetLedger();
        }

        // --- §45's estimate ----------------------------------------------------------------

        private static void CheckContractEstimate(Results r, IntercolonyWorldComponent state)
        {
            RecurringContract contract = new RecurringContract
            {
                id = -861,
                settlementName = "Testholme",
                factionName = "Test Confederacy",
                thingDef = ThingDefOf.Steel,
                quantityPerCycle = 300,
                cadenceTicks = GenDate.TicksPerQuadrum,
                totalCycles = 8,
                unitPrice = 4f,
                status = ContractStatus.Active
            };

            BusinessReportService.ContractEstimate estimate =
                BusinessReportService.Estimate(state, contract);

            r.Check(estimate.revenue == contract.CycleValue,
                "revenue is the agreed price, not an estimate (§45)",
                $"{estimate.revenue}");

            r.Check(estimate.inputsIfBought < 0 && estimate.transport <= 0 && estimate.payroll <= 0,
                "every cost line is signed as a cost",
                $"inputs {estimate.inputsIfBought}, payroll {estimate.payroll}, " +
                $"transport {estimate.transport}");

            r.Check(estimate.Margin ==
                    estimate.revenue + estimate.inputsIfBought + estimate.payroll + estimate.transport,
                "the margin is the sum of the lines shown (§117)",
                $"{estimate.Margin}");

            // §45's "should I buy inputs or produce them?" only has an answer if buying is priced
            // the way procurement would really price it.
            float expectedUnit = IntercolonyPricing.BaseValue(ThingDefOf.Steel, null) *
                                 RfqService.SupplierMargin;
            int expectedInputs = -Mathf.RoundToInt(expectedUnit * contract.quantityPerCycle);

            r.Check(estimate.inputsIfBought == expectedInputs,
                "inputs are priced with procurement's own supplier margin, not a second number",
                $"{estimate.inputsIfBought} at x{RfqService.SupplierMargin} markup");

            // The transport line is the delivery premium. On a pickup-priced world it would be zero;
            // a recurring agreement always delivers, so it must not be.
            r.Check(estimate.transport < 0,
                "the delivery premium appears as the cost of hauling it (§45)",
                $"{estimate.transport} of {estimate.revenue} revenue");

            r.Check(BusinessReportService.Estimate(state, null) != null,
                "a missing contract estimates to nothing rather than throwing");
        }

        // --- F19 direct-input estimate -----------------------------------------------------

        private static void CheckDirectInputEstimate(
            Results r, IntercolonyWorldComponent state)
        {
            const string summedAssertion =
                "I1 direct ingredients are summed, supplier-priced, and scaled";
            const string deterministicAssertion =
                "I2 the same world reports the same direct-input figure twice";
            const string noRecipeAssertion =
                "I3 no recipe is reported as such, not as zero";
            const string boundedAssertion =
                "I4 a craftable direct ingredient is priced at its own value, without recursion";

            ThingDef summedProduct = ThingDefOf.ComponentSpacer;
            RecipeDef summedRecipe =
                DefDatabase<RecipeDef>.GetNamedSilentFail("Make_ComponentSpacer");
            if (summedProduct == null || summedRecipe == null ||
                summedRecipe.ingredients == null || summedRecipe.ingredients.Count < 2)
            {
                r.Skip(summedAssertion,
                    "vanilla ComponentSpacer and its multi-ingredient recipe are unavailable");
            }
            else
            {
                const int quantityPerCycle = 3;
                int expectedDirectInputs;
                int outputCount;
                float expectedBatchCost;
                string ingredientDetail;
                string reason;
                if (!TryBuildIndependentDirectInputExpectation(
                        summedRecipe, summedProduct, quantityPerCycle,
                        out expectedDirectInputs, out outputCount, out expectedBatchCost,
                        out ingredientDetail, out reason))
                {
                    r.Skip(summedAssertion, reason);
                }
                else
                {
                    RecurringContract contract = new RecurringContract
                    {
                        id = -866,
                        settlementName = "Testholme",
                        factionName = "Test Confederacy",
                        thingDef = summedProduct,
                        quantityPerCycle = quantityPerCycle,
                        cadenceTicks = GenDate.TicksPerDay,
                        totalCycles = 5,
                        unitPrice = 4f,
                        status = ContractStatus.Active
                    };

                    BusinessReportService.ContractEstimate estimate =
                        BusinessReportService.Estimate(state, contract);
                    r.Check(
                        estimate != null && estimate.directInputs != null &&
                        estimate.directInputs.status ==
                            BusinessReportService.DirectInputCostStatus.Resolved &&
                        estimate.directInputsIfBought == expectedDirectInputs,
                        summedAssertion,
                        $"reported {DirectInputFigure(estimate)} silver; expected " +
                        $"{expectedDirectInputs} from {ingredientDetail}; batch " +
                        $"{expectedBatchCost:0.###} / {outputCount} output x {quantityPerCycle}");
                }
            }

            ThingDef deterministicProduct = ThingDefOf.Chemfuel;
            RecipeDef organicsRecipe =
                DefDatabase<RecipeDef>.GetNamedSilentFail("Make_ChemfuelFromOrganics");
            RecipeDef woodRecipe =
                DefDatabase<RecipeDef>.GetNamedSilentFail("Make_ChemfuelFromWood");
            List<RecipeDef> recipes = DefDatabase<RecipeDef>.AllDefsListForReading;
            if (deterministicProduct == null || recipes == null || organicsRecipe == null ||
                woodRecipe == null || !recipes.Contains(organicsRecipe) ||
                !recipes.Contains(woodRecipe) ||
                FindTestProduct(organicsRecipe, deterministicProduct) == null ||
                FindTestProduct(woodRecipe, deterministicProduct) == null)
            {
                r.Skip(deterministicAssertion,
                    "vanilla Chemfuel and both direct-input recipes are unavailable");
            }
            else
            {
                List<RecipeDef> savedRecipes = new List<RecipeDef>(recipes);
                try
                {
                    // Put each known Chemfuel recipe first in turn. An ordinal resolver must
                    // return the same recipe even when the loaded collection changes order.
                    RecurringContract contract = new RecurringContract
                    {
                        id = -867,
                        settlementName = "Testholme",
                        factionName = "Test Confederacy",
                        thingDef = deterministicProduct,
                        quantityPerCycle = 35,
                        cadenceTicks = GenDate.TicksPerDay,
                        totalCycles = 5,
                        unitPrice = 2.5f,
                        status = ContractStatus.Active
                    };

                    MoveRecipeToFront(recipes, organicsRecipe);
                    BusinessReportService.ContractEstimate first =
                        BusinessReportService.Estimate(state, contract);

                    MoveRecipeToFront(recipes, woodRecipe);
                    BusinessReportService.ContractEstimate second =
                        BusinessReportService.Estimate(state, contract);

                    r.Check(
                        first != null && second != null &&
                        first.directInputs != null && second.directInputs != null &&
                        first.directInputs.status ==
                            BusinessReportService.DirectInputCostStatus.Resolved &&
                        second.directInputs.status ==
                            BusinessReportService.DirectInputCostStatus.Resolved &&
                        first.directInputs.recipeDefName == second.directInputs.recipeDefName &&
                        first.directInputsIfBought == second.directInputsIfBought,
                        deterministicAssertion,
                        $"first {DirectInputFigure(first)} silver via " +
                        $"{DirectInputRecipe(first)}; second {DirectInputFigure(second)} " +
                        $"silver via {DirectInputRecipe(second)} after recipe-order change");
                }
                finally
                {
                    recipes.Clear();
                    recipes.AddRange(savedRecipes);
                    r.Info($"direct-input recipe order restored to {recipes.Count} recipe(s).");
                }
            }

            ThingDef noRecipeProduct = ThingDefOf.Silver;
            if (noRecipeProduct == null)
            {
                r.Skip(noRecipeAssertion, "vanilla Silver ThingDef is unavailable");
            }
            else
            {
                RecurringContract contract = new RecurringContract
                {
                    id = -868,
                    settlementName = "Testholme",
                    factionName = "Test Confederacy",
                    thingDef = noRecipeProduct,
                    quantityPerCycle = 7,
                    cadenceTicks = GenDate.TicksPerDay,
                    totalCycles = 5,
                    unitPrice = 2f,
                    status = ContractStatus.Active
                };

                BusinessReportService.ContractEstimate estimate =
                    BusinessReportService.Estimate(state, contract);
                r.Check(
                    estimate != null && estimate.directInputs != null &&
                    estimate.directInputs.status ==
                        BusinessReportService.DirectInputCostStatus.NoKnownRecipe &&
                    estimate.directInputsIfBought == 0,
                    noRecipeAssertion,
                    $"status {DirectInputStatus(estimate)}; figure " +
                    $"{DirectInputFigure(estimate)} silver");
            }

            ThingDef boundedProduct = ThingDefOf.ComponentIndustrial;
            RecipeDef boundedRecipe =
                DefDatabase<RecipeDef>.GetNamedSilentFail("Make_ComponentIndustrial");
            RecipeDef steelRecipe =
                DefDatabase<RecipeDef>.GetNamedSilentFail("ExtractMetalFromSlag");
            if (boundedProduct == null || boundedRecipe == null || steelRecipe == null ||
                boundedRecipe.ingredients == null || boundedRecipe.ingredients.Count != 1 ||
                FindTestProduct(boundedRecipe, boundedProduct) == null ||
                FindTestProduct(steelRecipe, ThingDefOf.Steel) == null)
            {
                r.Skip(boundedAssertion,
                    "vanilla ComponentIndustrial, Steel, and their recipes are unavailable");
            }
            else
            {
                ThingDef boundedIngredient;
                string ingredientReason;
                if (!TryGetSingleAllowedThingDef(
                        boundedRecipe.ingredients[0], out boundedIngredient, out ingredientReason) ||
                    boundedIngredient != ThingDefOf.Steel)
                {
                    r.Skip(boundedAssertion,
                        ingredientReason ?? "Make_ComponentIndustrial no longer consumes only Steel");
                }
                else
                {
                    const int quantityPerCycle = 2;
                    int expectedDirectInputs;
                    int outputCount;
                    float expectedBatchCost;
                    string ingredientDetail;
                    string reason;
                    if (!TryBuildIndependentDirectInputExpectation(
                            boundedRecipe, boundedProduct, quantityPerCycle,
                            out expectedDirectInputs, out outputCount, out expectedBatchCost,
                            out ingredientDetail, out reason))
                    {
                        r.Skip(boundedAssertion, reason);
                    }
                    else
                    {
                        RecurringContract contract = new RecurringContract
                        {
                            id = -869,
                            settlementName = "Testholme",
                            factionName = "Test Confederacy",
                            thingDef = boundedProduct,
                            quantityPerCycle = quantityPerCycle,
                            cadenceTicks = GenDate.TicksPerDay,
                            totalCycles = 5,
                            unitPrice = 3f,
                            status = ContractStatus.Active
                        };

                        BusinessReportService.ContractEstimate estimate =
                            BusinessReportService.Estimate(state, contract);
                        r.Check(
                            estimate != null && estimate.directInputs != null &&
                            estimate.directInputs.status ==
                                BusinessReportService.DirectInputCostStatus.Resolved &&
                            estimate.directInputs.hasDirectInputs &&
                            estimate.directInputs.recipeDefName == boundedRecipe.defName &&
                            estimate.directInputsIfBought == expectedDirectInputs,
                            boundedAssertion,
                            $"reported {DirectInputFigure(estimate)} silver; expected " +
                            $"{expectedDirectInputs} from {ingredientDetail}; batch " +
                            $"{expectedBatchCost:0.###} / {outputCount} output x {quantityPerCycle}; " +
                            $"Steel remains a direct input while {steelRecipe.defName} can craft it");
                    }
                }
            }
        }

        private static void MoveRecipeToFront(List<RecipeDef> recipes, RecipeDef target)
        {
            int index = recipes.IndexOf(target);
            if (index > 0)
            {
                recipes.RemoveAt(index);
                recipes.Insert(0, target);
            }
        }

        private static ThingDefCountClass FindTestProduct(RecipeDef recipe, ThingDef product)
        {
            if (recipe == null || recipe.products == null || product == null)
            {
                return null;
            }

            for (int i = 0; i < recipe.products.Count; i++)
            {
                ThingDefCountClass productEntry = recipe.products[i];
                if (productEntry != null && productEntry.thingDef == product &&
                    productEntry.count > 0)
                {
                    return productEntry;
                }
            }

            return null;
        }

        private static bool TryGetSingleAllowedThingDef(
            IngredientCount ingredient, out ThingDef allowedDef, out string reason)
        {
            allowedDef = null;
            reason = null;
            if (ingredient == null || ingredient.filter == null)
            {
                reason = "the recipe exposes an ingredient without a usable filter";
                return false;
            }

            int allowedCount = 0;
            foreach (ThingDef candidate in ingredient.filter.AllowedThingDefs)
            {
                if (candidate == null)
                {
                    continue;
                }

                allowedDef = candidate;
                allowedCount++;
            }

            if (allowedCount != 1)
            {
                reason = $"the fixture expected one allowed ingredient definition, found {allowedCount}";
                return false;
            }

            return true;
        }

        private static bool TryBuildIndependentDirectInputExpectation(
            RecipeDef recipe,
            ThingDef product,
            int quantity,
            out int expectedDirectInputs,
            out int outputCount,
            out float expectedBatchCost,
            out string ingredientDetail,
            out string reason)
        {
            expectedDirectInputs = 0;
            outputCount = 0;
            expectedBatchCost = 0f;
            ingredientDetail = null;
            reason = null;

            // Reconstruct the expected silver independently from public vanilla recipe data;
            // this deliberately does not call BusinessReportService's direct-input resolver.
            ThingDefCountClass productEntry = FindTestProduct(recipe, product);
            if (productEntry == null || recipe.ingredients == null || recipe.ingredients.Count == 0)
            {
                reason = "the vanilla recipe does not expose a usable product and ingredient list";
                return false;
            }

            List<string> parts = new List<string>();
            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                IngredientCount ingredient = recipe.ingredients[i];
                ThingDef allowedDef;
                string ingredientReason;
                if (!TryGetSingleAllowedThingDef(
                        ingredient, out allowedDef, out ingredientReason))
                {
                    reason = $"ingredient {i} cannot be read independently: {ingredientReason}";
                    return false;
                }

                float valuePerUnit = recipe.IngredientValueGetter.ValuePerUnitOf(allowedDef);
                if (valuePerUnit <= 0f || float.IsNaN(valuePerUnit) ||
                    float.IsInfinity(valuePerUnit))
                {
                    reason = $"ingredient {i} has no usable vanilla unit value";
                    return false;
                }

                int requiredCount = Mathf.CeilToInt(
                    ingredient.GetBaseCount() / valuePerUnit);
                float baseValue = IntercolonyPricing.BaseValue(allowedDef, null);
                if (requiredCount <= 0 || baseValue <= 0f || float.IsNaN(baseValue) ||
                    float.IsInfinity(baseValue))
                {
                    reason = $"ingredient {i} has no usable BaseValue price";
                    return false;
                }

                expectedBatchCost +=
                    requiredCount * baseValue * RfqService.SupplierMargin;
                parts.Add(
                    $"{requiredCount} {allowedDef.defName} @ {baseValue:0.###} x " +
                    $"{RfqService.SupplierMargin:0.###}");
            }

            outputCount = productEntry.count;
            expectedDirectInputs = -Mathf.RoundToInt(
                expectedBatchCost / outputCount * quantity);
            ingredientDetail = string.Join(", ", parts.ToArray());
            return true;
        }

        private static string DirectInputFigure(BusinessReportService.ContractEstimate estimate)
        {
            return estimate == null ? "<missing estimate>" :
                estimate.directInputsIfBought.ToString();
        }

        private static string DirectInputStatus(BusinessReportService.ContractEstimate estimate)
        {
            return estimate == null || estimate.directInputs == null
                ? "<missing status>"
                : estimate.directInputs.status.ToString();
        }

        private static string DirectInputRecipe(BusinessReportService.ContractEstimate estimate)
        {
            return estimate == null || estimate.directInputs == null
                ? "<missing recipe>"
                : estimate.directInputs.recipeDefName ?? "<none>";
        }

        // --- F20 direct-labour attribution ------------------------------------------------

        private static void CheckDirectLaborAttribution(
            Results r, IntercolonyWorldComponent state)
        {
            const string ineligibleAssertion =
                "W1 an ineligible employee's wage does not reach the good's direct labour";
            const string sharedAssertion =
                "W2 a wage is shared across eligible goods, not counted twice";
            const string noEligibleAssertion =
                "W3 no eligible employee is reported as such, not as zero";
            const string marginAssertion =
                "W4 the margin uses direct labour, not the whole wage bill";

            List<RecurringContract> contracts = state?.Contracts;
            List<EmploymentContract> employments = state?.Employments;
            ThingDef mealProduct = ThingDefOf.MealSimple;
            ThingDef componentProduct = ThingDefOf.ComponentIndustrial;
            ThingDef noEligibleProduct = ThingDefOf.ComponentSpacer;
            SkillDef cookingSkill = SkillDefOf.Cooking;
            SkillDef craftingSkill = SkillDefOf.Crafting;
            WorkTypeDef cookingWorkType =
                DefDatabase<WorkTypeDef>.GetNamedSilentFail("Cooking");
            WorkTypeDef craftingWorkType = WorkTypeDefOf.Crafting;
            RecipeDef mealRecipe =
                DefDatabase<RecipeDef>.GetNamedSilentFail("CookMealSimple");
            RecipeDef componentRecipe =
                DefDatabase<RecipeDef>.GetNamedSilentFail("Make_ComponentIndustrial");
            RecipeDef noEligibleRecipe =
                DefDatabase<RecipeDef>.GetNamedSilentFail("Make_ComponentSpacer");

            if (contracts == null || employments == null || Find.WorldPawns == null)
            {
                string reason = contracts == null
                    ? "the world recurring-contract collection is unavailable"
                    : employments == null
                        ? "the world employment collection is unavailable"
                        : "Find.WorldPawns was null, so real pawn employees could not be arranged";
                r.Skip(ineligibleAssertion, reason);
                r.Skip(sharedAssertion, reason);
                r.Skip(noEligibleAssertion, reason);
                r.Skip(marginAssertion, reason);
                return;
            }

            if (mealProduct == null || componentProduct == null || noEligibleProduct == null ||
                cookingSkill == null || craftingSkill == null || cookingWorkType == null ||
                craftingWorkType == null || PawnKindDefOf.Colonist == null ||
                Faction.OfPlayer == null || mealRecipe == null || componentRecipe == null ||
                noEligibleRecipe == null || FindTestProduct(mealRecipe, mealProduct) == null ||
                FindTestProduct(componentRecipe, componentProduct) == null ||
                FindTestProduct(noEligibleRecipe, noEligibleProduct) == null ||
                mealRecipe.requiredGiverWorkType != cookingWorkType ||
                mealRecipe.workSkill != cookingSkill ||
                componentRecipe.requiredGiverWorkType != null ||
                componentRecipe.workSkill != craftingSkill ||
                noEligibleRecipe.requiredGiverWorkType != null ||
                noEligibleRecipe.workSkill != craftingSkill)
            {
                string reason =
                    "vanilla MealSimple, ComponentIndustrial, ComponentSpacer, Cooking/Crafting, " +
                    "or the three F20 recipes are unavailable or no longer have their expected gates";
                r.Skip(ineligibleAssertion, reason);
                r.Skip(sharedAssertion, reason);
                r.Skip(noEligibleAssertion, reason);
                r.Skip(marginAssertion, reason);
                return;
            }

            List<RecurringContract> savedContracts = new List<RecurringContract>(contracts);
            List<EmploymentContract> savedEmployments =
                new List<EmploymentContract>(employments);
            Pawn ineligiblePawn = null;
            Pawn sharedPawn = null;
            bool randomStatePushed = false;

            const int cycleDays = 5;
            const int ineligibleDailyWage = 18;
            const int sharedDailyWage = 42;
            const int sharedEligibleGoodCount = 2;
            // Independent fixture arithmetic: each employee's daily wage is spread over the
            // two current agreement goods this shared employee can make.
            const float expectedSharedDailyShare =
                sharedDailyWage / (float)sharedEligibleGoodCount;
            const float expectedMealLabor =
                (ineligibleDailyWage + expectedSharedDailyShare) * cycleDays;
            const float expectedComponentLabor =
                expectedSharedDailyShare * cycleDays;
            int expectedMealDirectPayroll = -Mathf.RoundToInt(expectedMealLabor);
            int expectedComponentDirectPayroll = -Mathf.RoundToInt(expectedComponentLabor);
            int expectedWholePayroll =
                -Mathf.RoundToInt((ineligibleDailyWage + sharedDailyWage) * cycleDays);

            try
            {
                System.Predicate<Pawn> workerValidator = pawn =>
                    pawn != null && pawn.skills != null && pawn.workSettings != null &&
                    HasUsableSkill(pawn, cookingSkill) && HasUsableSkill(pawn, craftingSkill) &&
                    !pawn.WorkTypeIsDisabled(cookingWorkType) &&
                    !pawn.WorkTypeIsDisabled(craftingWorkType);

                try
                {
                    // A fixed stream makes the fixture reproducible and restores the live RNG
                    // frame below; the validator makes an unavailable humanlike skill a fixture
                    // failure rather than silently weakening the employee arrangement.
                    Rand.PushState(0xF20_2026);
                    randomStatePushed = true;
                    PawnGenerationRequest request = new PawnGenerationRequest(
                        PawnKindDefOf.Colonist,
                        Faction.OfPlayer,
                        PawnGenerationContext.NonPlayer,
                        forceGenerateNewPawn: true,
                        canGeneratePawnRelations: false,
                        mustBeCapableOfViolence: false,
                        allowFood: true,
                        validatorPostGear: workerValidator,
                        forceNoGear: true);
                    ineligiblePawn = PawnGenerator.GeneratePawn(request);
                    sharedPawn = PawnGenerator.GeneratePawn(request);
                }
                catch (System.Exception ex)
                {
                    string reason =
                        $"could not generate two real colonist employees: {ex.Message}";
                    r.Skip(ineligibleAssertion, reason);
                    r.Skip(sharedAssertion, reason);
                    r.Skip(noEligibleAssertion, reason);
                    r.Skip(marginAssertion, reason);
                    return;
                }

                if (ineligiblePawn == null || sharedPawn == null)
                {
                    string reason =
                        "PawnGenerator could not produce two colonists capable of Cooking and Crafting";
                    r.Skip(ineligibleAssertion, reason);
                    r.Skip(sharedAssertion, reason);
                    r.Skip(noEligibleAssertion, reason);
                    r.Skip(marginAssertion, reason);
                    return;
                }

                string configurationReason;
                if (!TryConfigureDirectLaborPawn(
                        ineligiblePawn, cookingSkill, cookingWorkType, craftingSkill,
                        craftingWorkType, canCook: true, canCraft: false,
                        out configurationReason) ||
                    !TryConfigureDirectLaborPawn(
                        sharedPawn, cookingSkill, cookingWorkType, craftingSkill,
                        craftingWorkType, canCook: true, canCraft: true,
                        out configurationReason))
                {
                    string reason =
                        $"could not configure the real employee fixture: {configurationReason}";
                    r.Skip(ineligibleAssertion, reason);
                    r.Skip(sharedAssertion, reason);
                    r.Skip(noEligibleAssertion, reason);
                    r.Skip(marginAssertion, reason);
                    return;
                }

                RecurringContract mealContract = new RecurringContract
                {
                    id = -870,
                    settlementName = "Testholme",
                    factionName = "Test Confederacy",
                    thingDef = mealProduct,
                    quantityPerCycle = 1,
                    cadenceTicks = cycleDays * GenDate.TicksPerDay,
                    totalCycles = 5,
                    unitPrice = 100f,
                    status = ContractStatus.Active
                };
                RecurringContract componentContract = new RecurringContract
                {
                    id = -871,
                    settlementName = "Testholme",
                    factionName = "Test Confederacy",
                    thingDef = componentProduct,
                    quantityPerCycle = 1,
                    cadenceTicks = cycleDays * GenDate.TicksPerDay,
                    totalCycles = 5,
                    unitPrice = 100f,
                    status = ContractStatus.Active
                };
                RecurringContract noEligibleContract = new RecurringContract
                {
                    id = -872,
                    settlementName = "Testholme",
                    factionName = "Test Confederacy",
                    thingDef = noEligibleProduct,
                    quantityPerCycle = 1,
                    cadenceTicks = cycleDays * GenDate.TicksPerDay,
                    totalCycles = 5,
                    unitPrice = 100f,
                    status = ContractStatus.Active
                };

                EmploymentContract ineligibleEmployment = new EmploymentContract
                {
                    id = -870,
                    settlementName = "Testholme",
                    factionName = "Test Confederacy",
                    pawn = ineligiblePawn,
                    employerFaction = Faction.OfPlayer,
                    originalKind = ineligiblePawn.kindDef,
                    workerName = "F20 Cooking specialist",
                    workerSkills = "Cooking 20",
                    dailyWage = ineligibleDailyWage,
                    termDays = cycleDays,
                    status = EmploymentStatus.Active
                };
                EmploymentContract sharedEmployment = new EmploymentContract
                {
                    id = -871,
                    settlementName = "Testholme",
                    factionName = "Test Confederacy",
                    pawn = sharedPawn,
                    employerFaction = Faction.OfPlayer,
                    originalKind = sharedPawn.kindDef,
                    workerName = "F20 Cooking and Crafting worker",
                    workerSkills = "Cooking 20, Crafting 20",
                    dailyWage = sharedDailyWage,
                    termDays = cycleDays,
                    status = EmploymentStatus.Active
                };

                contracts.Clear();
                employments.Clear();
                state.AddContract(mealContract);
                state.AddContract(componentContract);
                state.AddEmployment(ineligibleEmployment);
                state.AddEmployment(sharedEmployment);

                BusinessReportService.ContractEstimate mealEstimate =
                    BusinessReportService.Estimate(state, mealContract);
                BusinessReportService.ContractEstimate componentEstimate =
                    BusinessReportService.Estimate(state, componentContract);

                r.Check(
                    componentEstimate != null && componentEstimate.directLabor != null &&
                    componentEstimate.directLabor.status ==
                        BusinessReportService.DirectLaborCostStatus.Resolved &&
                    componentEstimate.directLabor.eligibleEmployeeCount == 1 &&
                    Mathf.Abs(componentEstimate.directLabor.cost - expectedComponentLabor) < 0.001f,
                    ineligibleAssertion,
                    $"{componentProduct.defName}: reported {DirectLaborFigure(componentEstimate)} " +
                    $"silver; ineligible {ineligibleDailyWage}/day; eligible " +
                    $"{sharedDailyWage}/day / {sharedEligibleGoodCount} goods = " +
                    $"{expectedSharedDailyShare:0.###}/day x {cycleDays}d; expected " +
                    $"{expectedComponentLabor:0.###}, eligible employees " +
                    $"{componentEstimate?.directLabor?.eligibleEmployeeCount.ToString() ?? "<missing>"}");

                r.Check(
                    mealEstimate != null && mealEstimate.directLabor != null &&
                    componentEstimate != null && componentEstimate.directLabor != null &&
                    mealEstimate.directLabor.status ==
                        BusinessReportService.DirectLaborCostStatus.Resolved &&
                    componentEstimate.directLabor.status ==
                        BusinessReportService.DirectLaborCostStatus.Resolved &&
                    mealEstimate.directLabor.eligibleEmployeeCount == 2 &&
                    componentEstimate.directLabor.eligibleEmployeeCount == 1 &&
                    mealEstimate.directPayroll == expectedMealDirectPayroll &&
                    componentEstimate.directPayroll == expectedComponentDirectPayroll &&
                    Mathf.Abs(mealEstimate.directLabor.cost - expectedMealLabor) < 0.001f &&
                    Mathf.Abs(componentEstimate.directLabor.cost - expectedComponentLabor) < 0.001f,
                    sharedAssertion,
                    $"wages {ineligibleDailyWage}+{sharedDailyWage}/day over " +
                    $"{cycleDays}d; shared employee eligible for {sharedEligibleGoodCount} goods " +
                    $"so share is {sharedDailyWage}/{sharedEligibleGoodCount} = " +
                    $"{expectedSharedDailyShare:0.###}/day; {mealProduct.defName} reported " +
                    $"{DirectLaborFigure(mealEstimate)} silver, expected " +
                    $"{expectedMealLabor:0.###}; {componentProduct.defName} reported " +
                    $"{DirectLaborFigure(componentEstimate)} silver, expected " +
                    $"{expectedComponentLabor:0.###}");

                SkillRecord sharedCrafting = sharedPawn.skills.GetSkill(craftingSkill);
                sharedCrafting.Level = 0;
                sharedPawn.workSettings.SetPriority(craftingWorkType, 0);

                BusinessReportService.ContractEstimate noEligibleEstimate =
                    BusinessReportService.Estimate(state, noEligibleContract);
                r.Check(
                    noEligibleEstimate != null && noEligibleEstimate.directLabor != null &&
                    noEligibleEstimate.directLabor.status ==
                        BusinessReportService.DirectLaborCostStatus.NoEligibleEmployees &&
                    noEligibleEstimate.directLabor.eligibleEmployeeCount == 0,
                    noEligibleAssertion,
                    $"{noEligibleProduct.defName}: status {DirectLaborStatus(noEligibleEstimate)}; " +
                    $"reported {DirectLaborFigure(noEligibleEstimate)} silver; wages " +
                    $"{ineligibleDailyWage}+{sharedDailyWage}/day over {cycleDays}d; shared " +
                    $"good count {sharedEligibleGoodCount}, resulting share " +
                    $"{expectedSharedDailyShare:0.###}/day before the no-eligible probe; " +
                    $"eligible employees " +
                    $"{noEligibleEstimate?.directLabor?.eligibleEmployeeCount.ToString() ?? "<missing>"}");

                int expectedNarrowMargin = componentEstimate == null
                    ? 0
                    : componentEstimate.revenue + componentEstimate.directInputsIfBought +
                      componentEstimate.directPayroll + componentEstimate.transport;
                int finishedGoodMargin = componentEstimate == null
                    ? 0
                    : componentEstimate.revenue + componentEstimate.inputsIfBought +
                      componentEstimate.directPayroll + componentEstimate.transport;
                int wholePayrollMargin = componentEstimate == null
                    ? 0
                    : componentEstimate.revenue + componentEstimate.directInputsIfBought +
                      componentEstimate.payroll + componentEstimate.transport;
                r.Check(
                    componentEstimate != null && componentEstimate.directLabor != null &&
                    componentEstimate.HasDirectLaborEstimate &&
                    componentEstimate.directPayroll == expectedComponentDirectPayroll &&
                    componentEstimate.payroll == expectedWholePayroll &&
                    componentEstimate.Margin == expectedNarrowMargin &&
                    componentEstimate.Margin != wholePayrollMargin &&
                    componentEstimate.Margin != finishedGoodMargin,
                    marginAssertion,
                    $"{componentProduct.defName}: wages {ineligibleDailyWage}+{sharedDailyWage}/day " +
                    $"x {cycleDays}d; shared eligible-good count {sharedEligibleGoodCount}, " +
                    $"resulting share {expectedSharedDailyShare:0.###}/day; direct payroll " +
                    $"{componentEstimate?.directPayroll.ToString() ?? "<missing>"}, whole payroll " +
                    $"{componentEstimate?.payroll.ToString() ?? "<missing>"}; direct-input cost " +
                    $"{componentEstimate?.directInputsIfBought.ToString() ?? "<missing>"}, " +
                    $"finished-good input cost {componentEstimate?.inputsIfBought.ToString() ?? "<missing>"}; " +
                    $"expected margin {expectedNarrowMargin}, whole-payroll margin " +
                    $"{wholePayrollMargin}, finished-good margin {finishedGoodMargin}, reported " +
                    $"{componentEstimate?.Margin.ToString() ?? "<missing>"}");
            }
            catch (System.Exception ex)
            {
                r.sb.AppendLine($"  EXCEPTION: direct-labour fixture {ex}");
                r.failed++;
            }
            finally
            {
                contracts.Clear();
                contracts.AddRange(savedContracts);
                employments.Clear();
                employments.AddRange(savedEmployments);
                new LaborCandidate { pawn = ineligiblePawn }.Discard();
                new LaborCandidate { pawn = sharedPawn }.Discard();
                if (randomStatePushed)
                {
                    Rand.PopState();
                }

                r.Info($"direct-labour fixture removed; restored {employments.Count} employment(s) " +
                       $"and {contracts.Count} agreement(s), and discarded its generated pawns.");
            }
        }

        private static bool HasUsableSkill(Pawn pawn, SkillDef skill)
        {
            if (pawn == null || pawn.skills == null || skill == null)
            {
                return false;
            }

            SkillRecord record = pawn.skills.GetSkill(skill);
            return record != null && !record.TotallyDisabled;
        }

        private static bool TryConfigureDirectLaborPawn(
            Pawn pawn,
            SkillDef cookingSkill,
            WorkTypeDef cookingWorkType,
            SkillDef craftingSkill,
            WorkTypeDef craftingWorkType,
            bool canCook,
            bool canCraft,
            out string reason)
        {
            reason = null;
            if (pawn == null || pawn.skills == null || pawn.workSettings == null)
            {
                reason = "the generated colonist has no skills or work settings";
                return false;
            }

            SkillRecord cooking = pawn.skills.GetSkill(cookingSkill);
            SkillRecord crafting = pawn.skills.GetSkill(craftingSkill);
            if (cooking == null || crafting == null || cooking.TotallyDisabled ||
                crafting.TotallyDisabled || pawn.WorkTypeIsDisabled(cookingWorkType) ||
                pawn.WorkTypeIsDisabled(craftingWorkType))
            {
                reason = "the generated colonist cannot expose both Cooking and Crafting records";
                return false;
            }

            if (!pawn.workSettings.Initialized)
            {
                pawn.workSettings.EnableAndInitialize();
            }

            cooking.Level = canCook ? 20 : 0;
            crafting.Level = canCraft ? 20 : 0;
            pawn.workSettings.SetPriority(cookingWorkType, canCook ? 3 : 0);
            pawn.workSettings.SetPriority(craftingWorkType, canCraft ? 3 : 0);
            return true;
        }

        private static string DirectLaborFigure(BusinessReportService.ContractEstimate estimate)
        {
            return estimate == null || estimate.directLabor == null
                ? "<missing estimate>"
                : estimate.directLabor.cost.ToString("0.###");
        }

        private static string DirectLaborStatus(BusinessReportService.ContractEstimate estimate)
        {
            return estimate == null || estimate.directLabor == null
                ? "<missing status>"
                : estimate.directLabor.status.ToString();
        }

        // --- F07 production commitments ----------------------------------------------------

        private static void CheckProductionCommitments(
            Results r, IntercolonyWorldComponent state)
        {
            const string committedAssertion =
                "R1 committed quantity per cycle is reported per cycle-length day";
            const string completedAssertion =
                "R2 completed production is reported for each good from the ledger";
            const string noProductionAssertion =
                "R3 no recorded production is flagged instead of presented as zero";
            const string suspendedAssertion =
                "R4 suspended seller agreements remain counted in Business commitments";

            List<RecurringContract> contracts = state?.Contracts;
            List<ProductionBucket> buckets = state?.ProductionLedger;
            ThingDef committedProduct = ThingDefOf.Steel;
            ThingDef secondProduct = ThingDefOf.WoodLog;
            ThingDef noProductionProduct = ThingDefOf.Plasteel;
            ThingDef suspendedProduct = ThingDefOf.Silver;

            if (contracts == null || buckets == null || committedProduct == null ||
                secondProduct == null || noProductionProduct == null || suspendedProduct == null)
            {
                string reason = contracts == null
                    ? "the world recurring-contract collection is unavailable"
                    : buckets == null
                        ? "the world production ledger is unavailable"
                        : "vanilla Steel, WoodLog, Plasteel, or Silver ThingDef is unavailable";
                r.Skip(committedAssertion, reason);
                r.Skip(completedAssertion, reason);
                r.Skip(noProductionAssertion, reason);
                r.Skip(suspendedAssertion, reason);
                return;
            }

            List<RecurringContract> savedContracts = new List<RecurringContract>(contracts);
            List<ProductionBucket> savedBuckets = new List<ProductionBucket>(buckets);

            const int committedQuantity = 17;
            const int committedCadenceDays = 2;
            // Independent fixture arithmetic: 17 units / 2 days = 8.5 per day.
            const float expectedCommittedPerDay = committedQuantity / (float)committedCadenceDays;

            const int firstRecordedQuantity = 5;
            const int secondRecordedQuantity = 10;
            // Independent fixture arithmetic: 5 / 5 days = 1, and 10 / 5 days = 2.
            const float expectedFirstCompletedPerDay = firstRecordedQuantity / 5f;
            const float expectedSecondCompletedPerDay = secondRecordedQuantity / 5f;

            const int noProductionQuantity = 9;
            const int noProductionCadenceDays = 3;
            const int suspendedQuantity = 8;
            const int suspendedCadenceDays = 4;
            // Independent fixture arithmetic: 8 units / 4 days = 2 per day.
            const float expectedSuspendedPerDay = suspendedQuantity / (float)suspendedCadenceDays;

            List<RecurringContract> fixtures;
            try
            {
                fixtures = new List<RecurringContract>
                {
                    new RecurringContract
                    {
                        id = -862,
                        settlementName = "Testholme",
                        factionName = "Test Confederacy",
                        thingDef = committedProduct,
                        quantityPerCycle = committedQuantity,
                        cadenceTicks = committedCadenceDays * GenDate.TicksPerDay,
                        totalCycles = 7,
                        unitPrice = 3.5f,
                        status = ContractStatus.Active
                    },
                    new RecurringContract
                    {
                        id = -863,
                        settlementName = "Testholme",
                        factionName = "Test Confederacy",
                        thingDef = secondProduct,
                        quantityPerCycle = 6,
                        cadenceTicks = 3 * GenDate.TicksPerDay,
                        totalCycles = 5,
                        unitPrice = 2.25f,
                        status = ContractStatus.Active
                    },
                    new RecurringContract
                    {
                        id = -864,
                        settlementName = "Testholme",
                        factionName = "Test Confederacy",
                        thingDef = noProductionProduct,
                        quantityPerCycle = noProductionQuantity,
                        cadenceTicks = noProductionCadenceDays * GenDate.TicksPerDay,
                        totalCycles = 5,
                        unitPrice = 4.75f,
                        status = ContractStatus.Active
                    },
                    new RecurringContract
                    {
                        id = -865,
                        settlementName = "Testholme",
                        factionName = "Test Confederacy",
                        thingDef = suspendedProduct,
                        quantityPerCycle = suspendedQuantity,
                        cadenceTicks = suspendedCadenceDays * GenDate.TicksPerDay,
                        totalCycles = 5,
                        unitPrice = 2.5f,
                        status = ContractStatus.Suspended
                    }
                };
            }
            catch (System.Exception ex)
            {
                string reason = $"could not build the production commitment fixture: {ex.Message}";
                r.Skip(committedAssertion, reason);
                r.Skip(completedAssertion, reason);
                r.Skip(noProductionAssertion, reason);
                r.Skip(suspendedAssertion, reason);
                return;
            }

            try
            {
                contracts.Clear();
                buckets.Clear();
                for (int i = 0; i < fixtures.Count; i++)
                {
                    state.AddContract(fixtures[i]);
                }

                ProductionLedgerService.Record(state, committedProduct, firstRecordedQuantity);
                ProductionLedgerService.Record(state, secondProduct, secondRecordedQuantity);

                List<BusinessReportService.ProductionCommitment> rows =
                    BusinessReportService.ActiveProductionCommitments(state);
                BusinessReportService.ProductionCommitment committedRow = rows.Find(
                    row => row.thingDef == committedProduct);
                BusinessReportService.ProductionCommitment firstCompletedRow = rows.Find(
                    row => row.thingDef == committedProduct);
                BusinessReportService.ProductionCommitment secondCompletedRow = rows.Find(
                    row => row.thingDef == secondProduct);
                BusinessReportService.ProductionCommitment noProductionRow = rows.Find(
                    row => row.thingDef == noProductionProduct);
                BusinessReportService.ProductionCommitment suspendedRow = rows.Find(
                    row => row.thingDef == suspendedProduct);

                r.Check(
                    committedRow != null &&
                    committedRow.committedPerDay == expectedCommittedPerDay,
                    committedAssertion,
                    $"reported {CommittedValue(committedRow)} per day; expected " +
                    $"{expectedCommittedPerDay:0.###} from {committedQuantity} units / " +
                    $"{committedCadenceDays} days");

                r.Check(
                    firstCompletedRow != null && secondCompletedRow != null &&
                    firstCompletedRow.completedPerDay == expectedFirstCompletedPerDay &&
                    secondCompletedRow.completedPerDay == expectedSecondCompletedPerDay,
                    completedAssertion,
                    $"{committedProduct.defName} {CompletedValue(firstCompletedRow)} per day " +
                    $"vs {expectedFirstCompletedPerDay:0.###}; " +
                    $"{secondProduct.defName} {CompletedValue(secondCompletedRow)} per day vs " +
                    $"{expectedSecondCompletedPerDay:0.###}");

                r.Check(
                    noProductionRow != null && !noProductionRow.hasRecordedProduction,
                    noProductionAssertion,
                    $"flag {(noProductionRow == null ? "<missing row>" :
                        noProductionRow.hasRecordedProduction.ToString())}; completed " +
                    $"{CompletedValue(noProductionRow)} per day");

                r.Check(
                    suspendedRow != null && suspendedRow.committedPerDay == expectedSuspendedPerDay,
                    suspendedAssertion,
                    $"reported {CommittedValue(suspendedRow)} per day; expected " +
                    $"{expectedSuspendedPerDay:0.###} from {suspendedQuantity} units / " +
                    $"{suspendedCadenceDays} days");
            }
            finally
            {
                contracts.Clear();
                contracts.AddRange(savedContracts);
                buckets.Clear();
                buckets.AddRange(savedBuckets);
                r.Info($"production commitment fixture restored {buckets.Count} bucket(s) and " +
                       $"{contracts.Count} agreement(s).");
            }
        }

        private static string CommittedValue(BusinessReportService.ProductionCommitment row)
        {
            return row == null ? "<missing row>" : row.committedPerDay.ToString("0.###");
        }

        private static string CompletedValue(BusinessReportService.ProductionCommitment row)
        {
            return row == null ? "<missing row>" : row.completedPerDay.ToString("0.###");
        }

        // --- Retention ---------------------------------------------------------------------

        private static void CheckPruning(Results r, IntercolonyWorldComponent state)
        {
            int start = state.Ledger.Count;

            state.Ledger.Add(Aged(LedgerKind.SalePayment, 100, 10));
            state.Ledger.Add(Aged(LedgerKind.SalePayment, 100, LedgerService.RetentionDays + 20));
            state.Ledger.Add(Aged(LedgerKind.SalePayment, 100, LedgerService.RetentionDays + 90));

            int removed = LedgerService.Prune(state);

            r.Check(removed == 2,
                "pruning drops entries past the retention window and keeps the rest (§75)",
                $"{removed} removed, retention {LedgerService.RetentionDays} days");

            r.Check(state.Ledger.Count == start + 1,
                "the recent entry survives", $"{state.Ledger.Count - start} left");

            r.Check(LedgerService.RetentionDays >= BusinessReportService.YearDays,
                "retention covers every window the dashboard can ask for",
                $"{LedgerService.RetentionDays}d retained, longest view {BusinessReportService.YearDays}d");

            while (state.Ledger.Count > start)
            {
                state.Ledger.RemoveAt(state.Ledger.Count - 1);
            }
        }

        // --- Helpers -----------------------------------------------------------------------

        private static LedgerEntry Aged(LedgerKind kind, int amount, int daysAgo)
        {
            return new LedgerEntry(kind, amount, "Testholme", "probe")
            {
                tick = GenTicks.TicksGame - daysAgo * GenDate.TicksPerDay
            };
        }

        private static string Summarize(Results r)
        {
            r.sb.AppendLine();
            r.sb.AppendLine($"  {r.passed} passed, {r.failed} failed" +
                            (r.skipped == 0 ? "." : $", {r.skipped} skipped."));
            return r.sb.ToString();
        }
    }
}
