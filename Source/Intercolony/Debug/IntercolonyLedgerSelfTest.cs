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
                CheckContractMaterialEconomics(r, state);
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

        private static void CheckContractMaterialEconomics(
            Results r, IntercolonyWorldComponent state)
        {
            const string constructionAssertion =
                "a built product resolves its material cost from the construction route";
            const string alternateStuffAssertion =
                "the same product in a different material costs differently";
            const string recipeAssertion =
                "a crafted product still resolves from its recipe";
            const string intermediateAssertion =
                "an intermediate ingredient is not decomposed";
            const string purchasePriorityAssertion =
                "a recent purchase outranks the market estimate";
            const string purchaseMedianAssertion =
                "one wild purchase does not move the price";
            const string cancelledPurchaseAssertion =
                "a cancelled purchase is not price evidence";
            const string benchmarkAssertion =
                "the market benchmark is unavailable rather than invented";

            List<ThingDef> thingDefs = DefDatabase<ThingDef>.AllDefsListForReading;
            List<RecipeDef> recipes = DefDatabase<RecipeDef>.AllDefsListForReading;
            if (thingDefs == null || recipes == null)
            {
                string reason = thingDefs == null
                    ? "the loaded ThingDef collection is unavailable"
                    : "the loaded RecipeDef collection is unavailable";
                r.Skip(constructionAssertion, reason);
                r.Skip(alternateStuffAssertion, reason);
                r.Skip(recipeAssertion, reason);
                r.Skip(intermediateAssertion, reason);
                r.Skip(purchasePriorityAssertion, reason);
                r.Skip(purchaseMedianAssertion, reason);
                r.Skip(cancelledPurchaseAssertion, reason);
                r.Skip(benchmarkAssertion, reason);
                return;
            }

            // Find a furniture-like buildable with no recipe so the production route under test
            // must be CostListAdjusted. The two stuffs are chosen from the loaded allowed-stuff
            // set, and their own BaseMarketValue is only used to avoid a coincidental equal pair.
            ThingDef builtProduct = null;
            ThingDef firstStuff = null;
            ThingDef secondStuff = null;
            for (int i = 0; i < thingDefs.Count && builtProduct == null; i++)
            {
                ThingDef candidate = thingDefs[i];
                if (candidate == null || candidate.category != ThingCategory.Building ||
                    candidate.building == null || candidate.IsFrame || !candidate.Minifiable ||
                    !candidate.MadeFromStuff || candidate.blueprintDef == null)
                {
                    continue;
                }

                bool hasRecipe = false;
                for (int j = 0; j < recipes.Count; j++)
                {
                    RecipeDef recipe = recipes[j];
                    if (recipe != null && !recipe.IsSurgery && recipe.products != null &&
                        FindTestProduct(recipe, candidate) != null)
                    {
                        hasRecipe = true;
                        break;
                    }
                }

                if (hasRecipe)
                {
                    continue;
                }

                List<ThingDef> allowedStuffs;
                try
                {
                    allowedStuffs = new List<ThingDef>(GenStuff.AllowedStuffsFor(candidate));
                }
                catch (System.Exception)
                {
                    continue;
                }

                allowedStuffs.Sort((left, right) => string.CompareOrdinal(
                    left?.defName ?? string.Empty, right?.defName ?? string.Empty));
                for (int leftIndex = 0;
                     leftIndex < allowedStuffs.Count && builtProduct == null;
                     leftIndex++)
                {
                    ThingDef leftStuff = allowedStuffs[leftIndex];
                    if (leftStuff == null || !leftStuff.IsStuff || leftStuff.BaseMarketValue <= 0f)
                    {
                        continue;
                    }

                    List<ThingDefCountClass> leftCostList;
                    try
                    {
                        leftCostList = candidate.CostListAdjusted(leftStuff);
                    }
                    catch (System.Exception)
                    {
                        continue;
                    }

                    if (leftCostList == null || leftCostList.Count == 0)
                    {
                        continue;
                    }

                    for (int rightIndex = leftIndex + 1;
                         rightIndex < allowedStuffs.Count;
                         rightIndex++)
                    {
                        ThingDef rightStuff = allowedStuffs[rightIndex];
                        if (rightStuff == null || !rightStuff.IsStuff ||
                            rightStuff.BaseMarketValue <= 0f ||
                            leftStuff.BaseMarketValue == rightStuff.BaseMarketValue)
                        {
                            continue;
                        }

                        List<ThingDefCountClass> rightCostList;
                        try
                        {
                            rightCostList = candidate.CostListAdjusted(rightStuff);
                        }
                        catch (System.Exception)
                        {
                            continue;
                        }

                        if (rightCostList == null || rightCostList.Count == 0)
                        {
                            continue;
                        }

                        builtProduct = candidate;
                        firstStuff = leftStuff;
                        secondStuff = rightStuff;
                        break;
                    }
                }
            }

            string builtSearchReason =
                "searched loaded minifiable Building ThingDefs made from stuff with a blueprint, " +
                "no non-surgery recipe, and two allowed positive-value stuffs";
            if (builtProduct == null)
            {
                r.Skip(constructionAssertion, builtSearchReason);
                r.Skip(alternateStuffAssertion, builtSearchReason);
            }
            else
            {
                BusinessReportService.DirectInputEstimate constructionEstimate = null;
                string constructionException = null;
                try
                {
                    constructionEstimate = BusinessReportService.EstimateDirectInputs(
                        state, builtProduct, firstStuff);
                }
                catch (System.Exception ex)
                {
                    constructionException = ex.Message;
                }

                r.Check(
                    constructionEstimate != null &&
                    constructionEstimate.status ==
                        BusinessReportService.DirectInputCostStatus.Resolved &&
                    constructionEstimate.hasDirectInputs &&
                    constructionEstimate.recipeDefName == null &&
                    constructionEstimate.constructionRouteName == "CostListAdjusted" &&
                    constructionEstimate.costPerUnit > 0f,
                    constructionAssertion,
                    $"product {builtProduct.defName}; stuff {firstStuff.defName}; " +
                    $"status {(constructionEstimate == null ? "<null>" :
                        constructionEstimate.status.ToString())}; cost " +
                    $"{(constructionEstimate == null ? 0f : constructionEstimate.costPerUnit):0.###}; " +
                    $"recipe {(constructionEstimate?.recipeDefName ?? "<none>")}; route " +
                    $"{(constructionEstimate?.constructionRouteName ?? "<none>")}" +
                    (constructionException == null ? "" : $"; exception {constructionException}"));

                BusinessReportService.DirectInputEstimate alternateConstructionEstimate = null;
                string alternateConstructionException = null;
                try
                {
                    alternateConstructionEstimate = BusinessReportService.EstimateDirectInputs(
                        state, builtProduct, secondStuff);
                }
                catch (System.Exception ex)
                {
                    alternateConstructionException = ex.Message;
                }

                r.Check(
                    constructionEstimate != null && alternateConstructionEstimate != null &&
                    constructionEstimate.status ==
                        BusinessReportService.DirectInputCostStatus.Resolved &&
                    alternateConstructionEstimate.status ==
                        BusinessReportService.DirectInputCostStatus.Resolved &&
                    constructionEstimate.constructionRouteName == "CostListAdjusted" &&
                    alternateConstructionEstimate.constructionRouteName == "CostListAdjusted" &&
                    constructionEstimate.costPerUnit != alternateConstructionEstimate.costPerUnit,
                    alternateStuffAssertion,
                    $"product {builtProduct.defName}; {firstStuff.defName} " +
                    $"{(constructionEstimate == null ? 0f : constructionEstimate.costPerUnit):0.###}; " +
                    $"{secondStuff.defName} " +
                    $"{(alternateConstructionEstimate == null ? 0f :
                        alternateConstructionEstimate.costPerUnit):0.###}; " +
                    $"routes {(constructionEstimate?.constructionRouteName ?? "<none>")} / " +
                    $"{(alternateConstructionEstimate?.constructionRouteName ?? "<none>")}" +
                    (alternateConstructionException == null ? "" :
                        $"; exception {alternateConstructionException}"));
            }

            // This deliberately overlaps the construction properties above. A recipe-only
            // product would not catch construction taking precedence over a real recipe. The
            // product need not be a building: being produced by a direct-input recipe is the
            // property this assertion is actually checking.
            ThingDef recipeProduct = null;
            RecipeDef selectedRecipe = null;
            int recipeThingDefsExamined = 0;
            int recipeDefinitionsExamined = 0;
            int recipeProductsWithDirectIngredients = 0;
            for (int i = 0; i < thingDefs.Count && recipeProduct == null; i++)
            {
                ThingDef candidate = thingDefs[i];
                if (candidate == null)
                {
                    continue;
                }

                recipeThingDefsExamined++;
                RecipeDef candidateRecipe = null;
                for (int j = 0; j < recipes.Count; j++)
                {
                    RecipeDef recipe = recipes[j];
                    if (recipe == null)
                    {
                        continue;
                    }

                    recipeDefinitionsExamined++;
                    if (recipe.IsSurgery || recipe.products == null ||
                        recipe.ingredients == null || recipe.ingredients.Count == 0 ||
                        FindTestProduct(recipe, candidate) == null)
                    {
                        continue;
                    }

                    if (candidateRecipe == null || string.CompareOrdinal(
                            recipe.defName ?? string.Empty,
                            candidateRecipe.defName ?? string.Empty) < 0)
                    {
                        candidateRecipe = recipe;
                    }
                }

                if (candidateRecipe != null)
                {
                    recipeProductsWithDirectIngredients++;
                    recipeProduct = candidate;
                    selectedRecipe = candidateRecipe;
                }
            }

            string recipeSearchReason =
                $"searched {recipeThingDefsExamined} loaded ThingDefs and " +
                $"{recipeDefinitionsExamined} loaded RecipeDefs for any ThingDef produced by a " +
                $"non-surgery recipe with at least one direct ingredient; found " +
                $"{recipeProductsWithDirectIngredients} matching product candidates";
            if (recipeProduct == null)
            {
                r.Skip(recipeAssertion, recipeSearchReason);
            }
            else
            {
                BusinessReportService.DirectInputEstimate recipeEstimate = null;
                string recipeException = null;
                try
                {
                    recipeEstimate = BusinessReportService.EstimateDirectInputs(
                        state, recipeProduct, null);
                }
                catch (System.Exception ex)
                {
                    recipeException = ex.Message;
                }

                r.Check(
                    recipeEstimate != null &&
                    recipeEstimate.status == BusinessReportService.DirectInputCostStatus.Resolved &&
                    recipeEstimate.recipeDefName == selectedRecipe.defName &&
                    recipeEstimate.constructionRouteName == null &&
                    recipeEstimate.hasDirectInputs,
                    recipeAssertion,
                    $"product {recipeProduct.defName}; status " +
                    $"{(recipeEstimate == null ? "<null>" : recipeEstimate.status.ToString())}; " +
                    $"cost {(recipeEstimate == null ? 0f : recipeEstimate.costPerUnit):0.###}; " +
                    $"recipe {(recipeEstimate?.recipeDefName ?? "<none>")}; route " +
                    $"{(recipeEstimate?.constructionRouteName ?? "<none>")}" +
                    (recipeException == null ? "" : $"; exception {recipeException}"));
            }

            // Choose the manufactured intermediate X first. Its own recipe must have a positive
            // direct-input price that differs from X's own price; otherwise the outer figure could
            // not distinguish pricing X from decomposing X. Then choose a product P whose selected
            // non-surgery recipe consumes X. Its recipe counts are kept as fixture data so the
            // comparison can isolate X's contribution without copying the production resolver's
            // price arithmetic.
            ThingDef intermediateProduct = null;
            RecipeDef intermediateProductRecipe = null;
            ThingDef manufacturedIntermediate = null;
            RecipeDef manufacturedIntermediateRecipe = null;
            ThingDefCountClass intermediateOutput = null;
            int intermediateRequiredCount = 0;
            float manufacturedIntermediatePrice = 0f;
            int intermediateThingDefsExamined = 0;
            int producedIntermediateCandidates = 0;
            int positiveIntermediateInputCandidates = 0;
            int positiveIntermediatePriceCandidates = 0;
            int distinctIntermediatePriceCandidates = 0;
            int intermediateCandidatesWithExistingPurchase = 0;
            int intermediateCandidatesWithoutExistingPurchase = 0;
            int outerProductThingDefsExamined = 0;
            int outerIngredientEntriesExamined = 0;
            int outerRecipeCandidates = 0;
            for (int i = 0; i < thingDefs.Count && intermediateProduct == null; i++)
            {
                ThingDef ingredientCandidate = thingDefs[i];
                if (ingredientCandidate == null)
                {
                    continue;
                }

                intermediateThingDefsExamined++;
                RecipeDef producer = null;
                for (int j = 0; j < recipes.Count; j++)
                {
                    RecipeDef recipe = recipes[j];
                    if (recipe == null || recipe.IsSurgery || recipe.products == null ||
                        FindTestProduct(recipe, ingredientCandidate) == null)
                    {
                        continue;
                    }

                    if (producer == null || string.CompareOrdinal(
                            recipe.defName ?? string.Empty,
                            producer.defName ?? string.Empty) < 0)
                    {
                        producer = recipe;
                    }
                }

                if (producer == null)
                {
                    continue;
                }

                producedIntermediateCandidates++;
                if (producer.ingredients == null || producer.ingredients.Count == 0)
                {
                    continue;
                }

                BusinessReportService.DirectInputEstimate ingredientInputEstimate = null;
                float ingredientPrice = 0f;
                try
                {
                    ingredientInputEstimate = BusinessReportService.EstimateDirectInputs(
                        state, ingredientCandidate, null);
                    ingredientPrice = IntercolonyPricing.BaseValue(ingredientCandidate, null);
                }
                catch (System.Exception)
                {
                    continue;
                }

                if (ingredientInputEstimate == null ||
                    ingredientInputEstimate.status !=
                        BusinessReportService.DirectInputCostStatus.Resolved ||
                    !ingredientInputEstimate.hasDirectInputs ||
                    ingredientInputEstimate.recipeDefName != producer.defName ||
                    ingredientInputEstimate.costPerUnit <= 0f ||
                    float.IsNaN(ingredientInputEstimate.costPerUnit) ||
                    float.IsInfinity(ingredientInputEstimate.costPerUnit))
                {
                    continue;
                }

                positiveIntermediateInputCandidates++;
                if (ingredientPrice <= 0f || float.IsNaN(ingredientPrice) ||
                    float.IsInfinity(ingredientPrice))
                {
                    continue;
                }

                positiveIntermediatePriceCandidates++;
                if (Mathf.Approximately(ingredientPrice, ingredientInputEstimate.costPerUnit))
                {
                    continue;
                }

                distinctIntermediatePriceCandidates++;
                bool hasExistingPurchase = false;
                if (state.PurchaseOrders != null)
                {
                    foreach (PurchaseOrder order in state.PurchaseOrders)
                    {
                        if (order != null && order.status == PurchaseOrderStatus.Completed &&
                            order.quantity > 0 && order.thingDef == ingredientCandidate &&
                            order.stuffDef == null && order.quality == null)
                        {
                            hasExistingPurchase = true;
                            break;
                        }
                    }
                }

                if (hasExistingPurchase)
                {
                    intermediateCandidatesWithExistingPurchase++;
                    continue;
                }

                intermediateCandidatesWithoutExistingPurchase++;
                for (int productIndex = 0;
                     productIndex < thingDefs.Count && intermediateProduct == null;
                     productIndex++)
                {
                    ThingDef productCandidate = thingDefs[productIndex];
                    if (productCandidate == null || productCandidate == ingredientCandidate)
                    {
                        continue;
                    }

                    outerProductThingDefsExamined++;
                    RecipeDef productRecipe = null;
                    for (int j = 0; j < recipes.Count; j++)
                    {
                        RecipeDef recipe = recipes[j];
                        if (recipe == null || recipe.IsSurgery || recipe.products == null ||
                            FindTestProduct(recipe, productCandidate) == null)
                        {
                            continue;
                        }

                        if (productRecipe == null || string.CompareOrdinal(
                                recipe.defName ?? string.Empty,
                                productRecipe.defName ?? string.Empty) < 0)
                        {
                            productRecipe = recipe;
                        }
                    }

                    if (productRecipe == null || productRecipe.ingredients == null ||
                        productRecipe.ingredients.Count == 0)
                    {
                        continue;
                    }

                    int requiredIntermediateCount = 0;
                    for (int ingredientIndex = 0;
                         ingredientIndex < productRecipe.ingredients.Count;
                         ingredientIndex++)
                    {
                        outerIngredientEntriesExamined++;
                        ThingDef allowedIntermediate;
                        string ignoredReason;
                        if (!TryGetSingleAllowedThingDef(
                                productRecipe.ingredients[ingredientIndex],
                                out allowedIntermediate,
                                out ignoredReason) ||
                            allowedIntermediate != ingredientCandidate)
                        {
                            continue;
                        }

                        int requiredCount;
                        try
                        {
                            requiredCount = productRecipe.ingredients[ingredientIndex].CountRequiredOfFor(
                                allowedIntermediate, productRecipe);
                        }
                        catch (System.Exception)
                        {
                            continue;
                        }

                        if (requiredCount > 0)
                        {
                            requiredIntermediateCount += requiredCount;
                        }
                    }

                    ThingDefCountClass output = FindTestProduct(productRecipe, productCandidate);

                    if (output == null || requiredIntermediateCount <= 0 || output.count <= 0)
                    {
                        continue;
                    }

                    outerRecipeCandidates++;
                    intermediateProduct = productCandidate;
                    intermediateProductRecipe = productRecipe;
                    manufacturedIntermediate = ingredientCandidate;
                    manufacturedIntermediateRecipe = producer;
                    intermediateOutput = output;
                    intermediateRequiredCount = requiredIntermediateCount;
                    manufacturedIntermediatePrice = ingredientPrice;
                }
            }

            string intermediateSearchReason =
                $"searched {intermediateThingDefsExamined} loaded ThingDefs as ingredient X for a " +
                $"non-surgery recipe producing X; {producedIntermediateCandidates} had a producer " +
                $"recipe, {positiveIntermediateInputCandidates} had a resolved positive own " +
                $"direct-input price, {positiveIntermediatePriceCandidates} had a positive X own " +
                $"price, and {distinctIntermediatePriceCandidates} had different X price/input " +
                $"figures; {intermediateCandidatesWithExistingPurchase} were excluded for existing " +
                $"completed purchase evidence and {intermediateCandidatesWithoutExistingPurchase} " +
                $"remained for the outer search, which examined {outerProductThingDefsExamined} " +
                $"loaded ThingDefs as product P, examined {outerIngredientEntriesExamined} direct " +
                $"ingredient entries, and found {outerRecipeCandidates} valid recipes consuming X";
            if (intermediateProduct == null || state.PurchaseOrders == null)
            {
                r.Skip(
                    intermediateAssertion,
                    intermediateProduct == null
                        ? intermediateSearchReason
                        : "the world purchase-order collection is unavailable for the evidence fixture");
            }
            else
            {
                PurchaseOrder intermediatePurchase = new PurchaseOrder
                {
                    id = -11041,
                    settlementId = 0,
                    settlementName = "Self-test evidence",
                    factionName = "Self-test faction",
                    thingDef = manufacturedIntermediate,
                    stuffDef = null,
                    quality = null,
                    quantity = 1,
                    unitPrice = manufacturedIntermediatePrice,
                    paidSilver = Mathf.RoundToInt(manufacturedIntermediatePrice),
                    orderedTick = GenTicks.TicksGame,
                    readyTick = GenTicks.TicksGame,
                    status = PurchaseOrderStatus.Completed
                };

                BusinessReportService.DirectInputEstimate pricedOuterEstimate = null;
                BusinessReportService.DirectInputEstimate decomposedOuterEstimate = null;
                BusinessReportService.DirectInputEstimate innerEstimate = null;
                float pricedFigure = 0f;
                float decomposedFigure = 0f;
                string intermediateException = null;
                PurchaseOrder decomposedPurchase = null;
                try
                {
                    // Measure X's own inputs before adding the synthetic purchase. The purchase
                    // then supplies X's real price to P, so these are two distinct real figures.
                    innerEstimate = BusinessReportService.EstimateDirectInputs(
                        state, manufacturedIntermediate, null);
                    decomposedPurchase = new PurchaseOrder
                    {
                        id = -11044,
                        settlementId = 0,
                        settlementName = "Self-test evidence",
                        factionName = "Self-test faction",
                        thingDef = manufacturedIntermediate,
                        stuffDef = null,
                        quality = null,
                        quantity = 1,
                        unitPrice = innerEstimate.costPerUnit,
                        paidSilver = Mathf.RoundToInt(innerEstimate.costPerUnit),
                        orderedTick = GenTicks.TicksGame,
                        readyTick = GenTicks.TicksGame,
                        status = PurchaseOrderStatus.Completed
                    };

                    state.PurchaseOrders.Add(intermediatePurchase);
                    pricedOuterEstimate = BusinessReportService.EstimateDirectInputs(
                        state, intermediateProduct, null);
                    state.PurchaseOrders.Remove(intermediatePurchase);
                    state.PurchaseOrders.Add(decomposedPurchase);
                    decomposedOuterEstimate = BusinessReportService.EstimateDirectInputs(
                        state, intermediateProduct, null);
                    pricedFigure = pricedOuterEstimate == null
                        ? 0f
                        : pricedOuterEstimate.costPerUnit;
                    decomposedFigure = decomposedOuterEstimate == null
                        ? 0f
                        : decomposedOuterEstimate.costPerUnit;
                }
                catch (System.Exception ex)
                {
                    intermediateException = ex.Message;
                }
                finally
                {
                    state.PurchaseOrders.Remove(intermediatePurchase);
                    if (decomposedPurchase != null)
                    {
                        state.PurchaseOrders.Remove(decomposedPurchase);
                    }
                }

                // If X's price and X's own input cost coincide, the outer figure cannot
                // distinguish pricing X as an intermediate from decomposing X, so skip rather
                // than passing vacuously.
                if (innerEstimate != null &&
                    innerEstimate.status ==
                        BusinessReportService.DirectInputCostStatus.Resolved &&
                    innerEstimate.hasDirectInputs &&
                    Mathf.Approximately(
                        manufacturedIntermediatePrice, innerEstimate.costPerUnit))
                {
                    r.Skip(
                        intermediateAssertion,
                        $"intermediate {manufacturedIntermediate.defName}; X price " +
                        $"{manufacturedIntermediatePrice:0.###} equals own-input cost " +
                        $"{innerEstimate.costPerUnit:0.###}; the two behaviours are not " +
                        "distinguishable");
                }
                else
                {
                    r.Check(
                        pricedOuterEstimate != null && decomposedOuterEstimate != null &&
                        innerEstimate != null &&
                        pricedOuterEstimate.status ==
                            BusinessReportService.DirectInputCostStatus.Resolved &&
                        decomposedOuterEstimate.status ==
                            BusinessReportService.DirectInputCostStatus.Resolved &&
                        innerEstimate.status ==
                            BusinessReportService.DirectInputCostStatus.Resolved &&
                        pricedOuterEstimate.hasDirectInputs &&
                        decomposedOuterEstimate.hasDirectInputs &&
                        innerEstimate.hasDirectInputs &&
                        pricedOuterEstimate.recipeDefName == intermediateProductRecipe.defName &&
                        decomposedOuterEstimate.recipeDefName == intermediateProductRecipe.defName &&
                        innerEstimate.recipeDefName == manufacturedIntermediateRecipe.defName &&
                        pricedOuterEstimate.ingredientPriceTiers != null &&
                        pricedOuterEstimate.ingredientPriceTiers.Contains(
                            BusinessReportService.DirectInputPriceTier.RecentCompletedPurchaseMedian) &&
                        decomposedOuterEstimate.ingredientPriceTiers != null &&
                        decomposedOuterEstimate.ingredientPriceTiers.Contains(
                            BusinessReportService.DirectInputPriceTier.RecentCompletedPurchaseMedian) &&
                        !Mathf.Approximately(pricedFigure, decomposedFigure) &&
                        Mathf.Approximately(
                            pricedFigure - decomposedFigure,
                            (manufacturedIntermediatePrice - innerEstimate.costPerUnit) *
                                intermediateRequiredCount / intermediateOutput.count),
                        intermediateAssertion,
                        $"outer {intermediateProduct.defName} via " +
                        $"{(pricedOuterEstimate?.recipeDefName ?? "<none>")} priced cost " +
                        $"{pricedFigure:0.###}; decomposed cost " +
                        $"{decomposedFigure:0.###}; " +
                        $"intermediate {manufacturedIntermediate.defName} via " +
                        $"{(innerEstimate?.recipeDefName ?? "<none>")} own-input cost " +
                        $"{(innerEstimate == null ? 0f : innerEstimate.costPerUnit):0.###}; " +
                        $"X price {manufacturedIntermediatePrice:0.###}; fixture " +
                        $"{intermediateRequiredCount}/{intermediateOutput.count} units at " +
                        $"X price; tiers " +
                        $"{(pricedOuterEstimate?.priceTier.ToString() ?? "<none>")} / " +
                        $"{(decomposedOuterEstimate?.priceTier.ToString() ?? "<none>")}" +
                        (intermediateException == null ? "" :
                            $"; exception {intermediateException}"));
                }
            }

            // A single direct input makes the purchase/market fixtures observable without
            // reimplementing the resolver. Existing completed purchases are excluded
            // conservatively so the three prices below are the only matching observations.
            ThingDef probeProduct = null;
            RecipeDef probeRecipe = null;
            ThingDef probeInput = null;
            int probeRequiredCount = 0;
            int probeOutputCount = 0;
            for (int i = 0; i < thingDefs.Count && probeProduct == null; i++)
            {
                ThingDef candidate = thingDefs[i];
                if (candidate == null)
                {
                    continue;
                }

                RecipeDef candidateRecipe = null;
                for (int j = 0; j < recipes.Count; j++)
                {
                    RecipeDef recipe = recipes[j];
                    if (recipe == null || recipe.IsSurgery || recipe.products == null ||
                        recipe.ingredients == null || recipe.ingredients.Count != 1 ||
                        FindTestProduct(recipe, candidate) == null)
                    {
                        continue;
                    }

                    if (candidateRecipe == null || string.CompareOrdinal(
                            recipe.defName ?? string.Empty,
                            candidateRecipe.defName ?? string.Empty) < 0)
                    {
                        candidateRecipe = recipe;
                    }
                }

                if (candidateRecipe == null)
                {
                    continue;
                }

                ThingDef allowedInput;
                string ignoredReason;
                if (!TryGetSingleAllowedThingDef(
                        candidateRecipe.ingredients[0], out allowedInput, out ignoredReason) ||
                    allowedInput == null)
                {
                    continue;
                }

                ThingDefCountClass output = FindTestProduct(candidateRecipe, candidate);
                int requiredCount;
                try
                {
                    requiredCount = candidateRecipe.ingredients[0].CountRequiredOfFor(
                        allowedInput, candidateRecipe);
                }
                catch (System.Exception)
                {
                    continue;
                }

                if (output == null || requiredCount <= 0 || output.count <= 0)
                {
                    continue;
                }

                bool hasExistingPurchase = false;
                if (state.PurchaseOrders != null)
                {
                    foreach (PurchaseOrder order in state.PurchaseOrders)
                    {
                        if (order != null && order.status == PurchaseOrderStatus.Completed &&
                            order.quantity > 0 && order.thingDef == allowedInput &&
                            order.stuffDef == null && order.quality == null)
                        {
                            hasExistingPurchase = true;
                            break;
                        }
                    }
                }

                if (hasExistingPurchase)
                {
                    continue;
                }

                probeProduct = candidate;
                probeRecipe = candidateRecipe;
                probeInput = allowedInput;
                probeRequiredCount = requiredCount;
                probeOutputCount = output.count;
            }

            string probeSearchReason =
                "searched loaded products with a selected non-surgery recipe containing exactly " +
                "one allowed direct input and no existing completed purchase evidence";

            int supplierSettlementId = -1;
            if (Find.WorldObjects != null)
            {
                foreach (var settlement in Find.WorldObjects.Settlements)
                {
                    if (settlement != null && IntercolonyMarketAccess.IsAccessible(settlement))
                    {
                        supplierSettlementId = settlement.ID;
                        break;
                    }
                }
            }

            if (probeProduct == null || state.PurchaseOrders == null ||
                state.SupplierListings == null || supplierSettlementId < 0)
            {
                string reason = probeProduct == null
                    ? probeSearchReason
                    : state.PurchaseOrders == null
                        ? "the world purchase-order collection is unavailable"
                        : state.SupplierListings == null
                            ? "the world supplier-listing collection is unavailable"
                            : "no accessible loaded settlement is available for a market listing fixture";
                r.Skip(purchasePriorityAssertion, reason);
            }
            else
            {
                const float marketPrice = 73.5f;
                const float purchasePrice = 0.125f;
                SupplierListing marketListing = new SupplierListing
                {
                    id = -11042,
                    settlementId = supplierSettlementId,
                    thingDef = probeInput,
                    stuffDef = null,
                    quality = null,
                    quantityAvailable = 100,
                    unitPrice = marketPrice,
                    createdTick = GenTicks.TicksGame,
                    expiryTick = SupplierListing.NoExpiryTick,
                    refreshWindow = state.RefreshCount
                };
                PurchaseOrder purchase = new PurchaseOrder
                {
                    id = -11043,
                    settlementId = supplierSettlementId,
                    settlementName = "Self-test evidence",
                    factionName = "Self-test faction",
                    thingDef = probeInput,
                    stuffDef = null,
                    quality = null,
                    quantity = 8,
                    unitPrice = purchasePrice,
                    paidSilver = Mathf.RoundToInt(purchasePrice * 8f),
                    orderedTick = GenTicks.TicksGame,
                    readyTick = GenTicks.TicksGame,
                    status = PurchaseOrderStatus.Completed
                };

                BusinessReportService.DirectInputEstimate marketEstimate = null;
                BusinessReportService.DirectInputEstimate purchaseEstimate = null;
                float observedPurchaseUnit = 0f;
                string purchasePriorityException = null;
                try
                {
                    state.SupplierListings.Add(marketListing);
                    marketEstimate = BusinessReportService.EstimateDirectInputs(
                        state, probeProduct, null);
                    state.PurchaseOrders.Add(purchase);
                    purchaseEstimate = BusinessReportService.EstimateDirectInputs(
                        state, probeProduct, null);
                    observedPurchaseUnit = purchaseEstimate == null
                        ? 0f
                        : purchaseEstimate.costPerUnit * probeOutputCount /
                          (float)probeRequiredCount;
                }
                catch (System.Exception ex)
                {
                    purchasePriorityException = ex.Message;
                }
                finally
                {
                    state.PurchaseOrders.Remove(purchase);
                    state.SupplierListings.Remove(marketListing);
                }

                r.Check(
                    marketEstimate != null && purchaseEstimate != null &&
                    marketEstimate.status ==
                        BusinessReportService.DirectInputCostStatus.Resolved &&
                    purchaseEstimate.status ==
                        BusinessReportService.DirectInputCostStatus.Resolved &&
                    marketEstimate.recipeDefName == probeRecipe.defName &&
                    purchaseEstimate.recipeDefName == probeRecipe.defName &&
                    marketEstimate.priceTier ==
                        BusinessReportService.DirectInputPriceTier.ProcurementMarketEstimate &&
                    purchaseEstimate.priceTier ==
                        BusinessReportService.DirectInputPriceTier.RecentCompletedPurchaseMedian &&
                    Mathf.Approximately(observedPurchaseUnit, purchasePrice) &&
                    purchaseEstimate.costPerUnit < marketEstimate.costPerUnit,
                    purchasePriorityAssertion,
                    $"input {probeInput.defName}; market fixture {marketPrice:0.###}; " +
                    $"market cost {(marketEstimate == null ? 0f : marketEstimate.costPerUnit):0.###} " +
                    $"tier {(marketEstimate?.priceTier.ToString() ?? "<none>")}; purchase fixture " +
                    $"{purchasePrice:0.###}; purchase cost " +
                    $"{(purchaseEstimate == null ? 0f : purchaseEstimate.costPerUnit):0.###} " +
                    $"unit {(observedPurchaseUnit):0.###}; tier " +
                    $"{(purchaseEstimate?.priceTier.ToString() ?? "<none>")}" +
                    (purchasePriorityException == null ? "" :
                        $"; exception {purchasePriorityException}"));
            }

            if (probeProduct == null || state.PurchaseOrders == null)
            {
                r.Skip(
                    purchaseMedianAssertion,
                    probeProduct == null
                        ? probeSearchReason
                        : "the world purchase-order collection is unavailable");
            }
            else
            {
                float[] medianPrices = { 2.0f, 2.1f, 9.0f };
                List<PurchaseOrder> medianPurchases = new List<PurchaseOrder>();
                BusinessReportService.DirectInputEstimate medianEstimate = null;
                float observedMedianUnitPrice = 0f;
                string purchaseMedianException = null;
                try
                {
                    for (int i = 0; i < medianPrices.Length; i++)
                    {
                        medianPurchases.Add(new PurchaseOrder
                        {
                            id = -11050 - i,
                            settlementId = 0,
                            settlementName = "Self-test evidence",
                            factionName = "Self-test faction",
                            thingDef = probeInput,
                            stuffDef = null,
                            quality = null,
                            quantity = 1,
                            unitPrice = medianPrices[i],
                            paidSilver = Mathf.RoundToInt(medianPrices[i]),
                            orderedTick = GenTicks.TicksGame,
                            readyTick = GenTicks.TicksGame,
                            status = PurchaseOrderStatus.Completed
                        });
                        state.PurchaseOrders.Add(medianPurchases[i]);
                    }

                    medianEstimate =
                        BusinessReportService.EstimateDirectInputs(state, probeProduct, null);
                    observedMedianUnitPrice = medianEstimate == null
                        ? 0f
                        : medianEstimate.costPerUnit * probeOutputCount /
                          (float)probeRequiredCount;
                }
                catch (System.Exception ex)
                {
                    purchaseMedianException = ex.Message;
                }
                finally
                {
                    for (int i = 0; i < medianPurchases.Count; i++)
                    {
                        state.PurchaseOrders.Remove(medianPurchases[i]);
                    }
                }

                r.Check(
                    medianEstimate != null &&
                    medianEstimate.status ==
                        BusinessReportService.DirectInputCostStatus.Resolved &&
                    medianEstimate.recipeDefName == probeRecipe.defName &&
                    medianEstimate.priceTier ==
                        BusinessReportService.DirectInputPriceTier.RecentCompletedPurchaseMedian &&
                    Mathf.Approximately(observedMedianUnitPrice, 2.1f),
                    purchaseMedianAssertion,
                    $"input {probeInput.defName}; purchases 2.0, 2.1, 9.0; observed " +
                    $"{(medianEstimate == null ? 0f : medianEstimate.costPerUnit):0.###}; " +
                    $"unit {(observedMedianUnitPrice):0.###}; expected median 2.1; " +
                    $"fixture scale {probeRequiredCount}/{probeOutputCount}; tier " +
                    $"{(medianEstimate?.priceTier.ToString() ?? "<none>")}" +
                    (purchaseMedianException == null ? "" :
                        $"; exception {purchaseMedianException}"));
            }

            if (probeProduct == null || state.PurchaseOrders == null)
            {
                r.Skip(
                    cancelledPurchaseAssertion,
                    probeProduct == null
                        ? probeSearchReason
                        : "the world purchase-order collection is unavailable");
            }
            else
            {
                BusinessReportService.DirectInputEstimate beforeCancelled = null;
                BusinessReportService.DirectInputEstimate afterCancelled = null;
                PurchaseOrder cancelledPurchase = new PurchaseOrder
                {
                    id = -11060,
                    settlementId = 0,
                    settlementName = "Self-test evidence",
                    factionName = "Self-test faction",
                    thingDef = probeInput,
                    stuffDef = null,
                    quality = null,
                    quantity = 1,
                    unitPrice = 999999f,
                    paidSilver = 999999,
                    orderedTick = GenTicks.TicksGame,
                    readyTick = GenTicks.TicksGame,
                    status = PurchaseOrderStatus.Cancelled
                };

                string cancelledPurchaseException = null;
                try
                {
                    beforeCancelled = BusinessReportService.EstimateDirectInputs(
                        state, probeProduct, null);
                    state.PurchaseOrders.Add(cancelledPurchase);
                    afterCancelled = BusinessReportService.EstimateDirectInputs(
                        state, probeProduct, null);
                }
                catch (System.Exception ex)
                {
                    cancelledPurchaseException = ex.Message;
                }
                finally
                {
                    state.PurchaseOrders.Remove(cancelledPurchase);
                }

                r.Check(
                    beforeCancelled != null && afterCancelled != null &&
                    beforeCancelled.status == afterCancelled.status &&
                    beforeCancelled.recipeDefName == afterCancelled.recipeDefName &&
                    beforeCancelled.priceTier == afterCancelled.priceTier &&
                    Mathf.Approximately(
                        beforeCancelled.costPerUnit, afterCancelled.costPerUnit) &&
                    beforeCancelled.hasDirectInputs == afterCancelled.hasDirectInputs,
                    cancelledPurchaseAssertion,
                    $"input {probeInput.defName}; cancelled fixture {cancelledPurchase.unitPrice:0.###}; " +
                    $"before {(beforeCancelled == null ? "<null>" :
                        beforeCancelled.status.ToString())} " +
                    $"{(beforeCancelled == null ? 0f : beforeCancelled.costPerUnit):0.###} " +
                    $"tier {(beforeCancelled?.priceTier.ToString() ?? "<none>")}; after " +
                    $"{(afterCancelled == null ? "<null>" : afterCancelled.status.ToString())} " +
                    $"{(afterCancelled == null ? 0f : afterCancelled.costPerUnit):0.###} " +
                    $"tier {(afterCancelled?.priceTier.ToString() ?? "<none>")}" +
                    (cancelledPurchaseException == null ? "" :
                        $"; exception {cancelledPurchaseException}"));
            }

            ThingDef benchmarkProduct = null;
            for (int i = 0; i < thingDefs.Count && benchmarkProduct == null; i++)
            {
                ThingDef candidate = thingDefs[i];
                if (candidate == null || candidate.BaseMarketValue <= 0f ||
                    (candidate.category != ThingCategory.Item &&
                     candidate.category != ThingCategory.Building))
                {
                    continue;
                }

                bool hasMatchingEvidence = false;
                if (state.SupplierListings != null)
                {
                    foreach (SupplierListing listing in state.SupplierListings)
                    {
                        if (listing != null && listing.thingDef == candidate &&
                            listing.stuffDef == null)
                        {
                            hasMatchingEvidence = true;
                            break;
                        }
                    }
                }

                if (!hasMatchingEvidence && state.Requests != null)
                {
                    foreach (PurchaseRequest request in state.Requests)
                    {
                        if (request != null && request.thingDef == candidate &&
                            request.stuffDef == null)
                        {
                            hasMatchingEvidence = true;
                            break;
                        }
                    }
                }

                if (!hasMatchingEvidence)
                {
                    benchmarkProduct = candidate;
                }
            }

            string benchmarkSearchReason =
                "searched loaded positive-BaseMarketValue Item or Building ThingDefs with no " +
                "matching supplier listing or purchase request";
            if (benchmarkProduct == null)
            {
                r.Skip(benchmarkAssertion, benchmarkSearchReason);
            }
            else
            {
                RecurringContract benchmarkContract = new RecurringContract
                {
                    id = -11070,
                    settlementName = "Self-test benchmark",
                    factionName = "Self-test faction",
                    thingDef = benchmarkProduct,
                    stuffDef = null,
                    quantityPerCycle = 1,
                    cadenceTicks = GenDate.TicksPerDay,
                    totalCycles = 1,
                    unitPrice = 1f,
                    status = ContractStatus.Active
                };
                BusinessReportService.ContractEstimate benchmarkEstimate = null;
                string benchmarkException = null;
                try
                {
                    benchmarkEstimate = BusinessReportService.Estimate(
                        state, benchmarkContract);
                }
                catch (System.Exception ex)
                {
                    benchmarkException = ex.Message;
                }

                r.Check(
                    benchmarkEstimate != null &&
                    !benchmarkEstimate.hasMarketMedianUnitPrice &&
                    benchmarkEstimate.marketMedianUnitPrice == 0f,
                    benchmarkAssertion,
                    $"product {benchmarkProduct.defName}; has median " +
                    $"{(benchmarkEstimate == null ? "<null>" :
                        benchmarkEstimate.hasMarketMedianUnitPrice.ToString())}; observed " +
                    $"{(benchmarkEstimate == null ? 0f :
                        benchmarkEstimate.marketMedianUnitPrice):0.###}; " +
                    "matching listing/request search returned none" +
                    (benchmarkException == null ? "" : $"; exception {benchmarkException}"));
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
