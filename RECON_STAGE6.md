# Stage 6 Recon Seam Map: F07, F19, F20

Read-only seam map for the three Business-view findings in
`docs/PLAYTEST_BATCH_SOURCE_PLAN.md`. The plan requires F07 to use actually completed
production rather than stock movement (`docs/PLAYTEST_BATCH_SOURCE_PLAN.md:493`,
`docs/PLAYTEST_BATCH_SOURCE_PLAN.md:495`, `docs/PLAYTEST_BATCH_SOURCE_PLAN.md:497`,
`docs/PLAYTEST_BATCH_SOURCE_PLAN.md:499`), F19 to price internally produced inputs as
replacement purchases (`docs/PLAYTEST_BATCH_SOURCE_PLAN.md:513`,
`docs/PLAYTEST_BATCH_SOURCE_PLAN.md:515`, `docs/PLAYTEST_BATCH_SOURCE_PLAN.md:519`,
`docs/PLAYTEST_BATCH_SOURCE_PLAN.md:521`), and F20 to use actual recent employee work
where practical (`docs/PLAYTEST_BATCH_SOURCE_PLAN.md:555`,
`docs/PLAYTEST_BATCH_SOURCE_PLAN.md:559`, `docs/PLAYTEST_BATCH_SOURCE_PLAN.md:561`,
`docs/PLAYTEST_BATCH_SOURCE_PLAN.md:565`).

## Executive disposition

All three are systems, not bounded UI slices. F07 lacks the production observation and
rolling production history needed to produce its second number; F19 lacks a recipe/input
cost model and a resolver over actual input prices; F20 lacks employee-to-product work
attribution. The concrete evidence is the stock-only production code at
`Source/Intercolony/Production/ProduceLoopMapComponent.cs:49` through
`Source/Intercolony/Production/ProduceLoopMapComponent.cs:51`, together with the no-match
costing searches and no-match production-labor searches recorded in
[Exact negative searches](#exact-negative-searches) and the existing seams cited below.

| Finding | Disposition | Smallest useful first piece | Save-schema result |
| --- | --- | --- | --- |
| F07 | **System** | One event-driven completed-product ledger for active seller commitments, then one Business row using its rolling total. | **Yes** for a persisted world-scoped rolling ledger. |
| F19 | **System** | Direct inputs for one selected recipe/product, priced by retained purchase orders, an existing procurement quote or generic fallback; no recursive decomposition. | **No** if it reads retained orders/quotes; **yes only** if a new durable dated price history is added. |
| F20 | **System** | One narrow production path recording worker, product and work ticks, then allocate compensation from that ledger. | **Yes** for persisted actual-work history; **no** for the plan's coarse fallback approximation. |

The plan itself leaves F07's technically robust window open beyond “approximately five
in-game days” (`docs/PLAYTEST_BATCH_SOURCE_PLAN.md:501`, `docs/PLAYTEST_BATCH_SOURCE_PLAN.md:503`,
`docs/PLAYTEST_BATCH_SOURCE_PLAN.md:505`) and leaves F19's outlier-resistant aggregation
method open (`docs/PLAYTEST_BATCH_SOURCE_PLAN.md:533`, `docs/PLAYTEST_BATCH_SOURCE_PLAN.md:541`).

## A. The Business view today

### READ

The top-level tab dispatch sends `Tab.Business` to `DrawBusiness` at
`Source/Intercolony/UI/MainTabWindow_Intercolony.cs:313`,
`Source/Intercolony/UI/MainTabWindow_Intercolony.cs:315`. The Business page's draw order is
currently:

1. `DrawCashPosition` at `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:73`;
2. `CashFlowForecast.Current(state)` followed by `DrawCashFlowForecast` at
   `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:75` and
   `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:76`;
3. `DrawBrandSummary` at `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:78`;
4. `DrawPeriodReport` at `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:80`.

The sections are:

| Section | What is drawn | Current computation |
| --- | --- | --- |
| “Where you stand” | Silver, the daily wage bill, and payroll runway at `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:251`, `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:256`, `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:257`, `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:258`, `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:273`, `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:283`. | `BusinessReportService.SilverOnHand`, `DailyWageBill`, and `PayrollRunwayDays` at `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:256`, `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:257`, `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:258`. |
| “Cash flow -- next N days” | Expected revenue, expected expenses and net at `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:90`, `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:162`, `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:164`, `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:166`. | `CashFlowForecast.Current(state)` at `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:75`. |
| “Brand reputation” | “Known for” and “Weak reputation” groups at `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:300`, `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:319`, `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:324`, `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:331`. | `ProductBrandUiService.BuildSummary(state)` at `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:307`, `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:308`. |
| “Last quadrum” / “Last year” | Period ledger rows and net cash movement at `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:388`, `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:395`, `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:405`, `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:434`, `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:456`. | `LedgerService.Summarise(state, businessWindowDays)` at `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:405`. |

`BusinessReportService` is the primary economic report service: its class comment says it
turns state into the screens and distinguishes ledger facts from estimates at
`Source/Intercolony/Core/BusinessReportService.cs:8`,
`Source/Intercolony/Core/BusinessReportService.cs:16`,
`Source/Intercolony/Core/BusinessReportService.cs:19`. Its contract estimate is defined at
`Source/Intercolony/Core/BusinessReportService.cs:34` and its active-agreement query is at
`Source/Intercolony/Core/BusinessReportService.cs:128`.

### CONCLUDE: attachment

There is no per-good operational section in the current draw chain: the chain goes from
brand directly to the period report at `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:78`
and `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:80`, and the page height is
closed from the returned `y` at `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:82`
and `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:84`.

The exact shared UI seam for F07, F19 and F20 is therefore
`Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:80`: insert one new per-good
report call after the brand block and before `DrawPeriodReport`, with that renderer returning
its final `y`. The corresponding report-data seam is
`Source/Intercolony/Core/BusinessReportService.cs:128`, immediately alongside the existing
active-agreement report methods; the current contract estimate is not used by Business itself,
but by the Contracts page at `Source/Intercolony/UI/MainTabWindow_Intercolony.cs:3810` and
`Source/Intercolony/UI/MainTabWindow_Intercolony.cs:3818`.

## B. F07's two numbers

### READ: committed quantity per day

`RecurringContract` is the seller-side standing supply agreement and describes its purpose as
a known production commitment at `Source/Intercolony/Contracts/RecurringContract.cs:34`,
`Source/Intercolony/Contracts/RecurringContract.cs:38`,
`Source/Intercolony/Contracts/RecurringContract.cs:42`. Its product identity is
`thingDef`, `stuffDef` and `minQuality` at `Source/Intercolony/Contracts/RecurringContract.cs:54`,
`Source/Intercolony/Contracts/RecurringContract.cs:55`,
`Source/Intercolony/Contracts/RecurringContract.cs:56`; its commitment is
`quantityPerCycle` at `Source/Intercolony/Contracts/RecurringContract.cs:58` and its cadence is
`cadenceTicks` at `Source/Intercolony/Contracts/RecurringContract.cs:61`.

There is no dedicated per-day field: the exact search for
`QuantityPerDay`, `quantityPerDay`, `CommittedQuantityPerDay` and
`committedQuantityPerDay` returned `NO MATCHES`. The existing `CadenceDays` property converts
ticks to days at `Source/Intercolony/Contracts/RecurringContract.cs:208`, and the contract UI
already presents the same derivation as `qty / Mathf.Max(1f, contract.CadenceDays)` at
`Source/Intercolony/UI/MainTabWindow_Intercolony.cs:4597` and
`Source/Intercolony/UI/MainTabWindow_Intercolony.cs:4598`.

Thus the committed number can be derived today as the sum, per matching good, of
`quantityPerCycle / CadenceDays` over `state.Contracts` entries whose `IsActive` is true.
The active predicate is `RecurringContract.IsActive` at
`Source/Intercolony/Contracts/RecurringContract.cs:169`, and the existing service iterates
`state.Contracts` and includes active contracts at
`Source/Intercolony/Core/BusinessReportService.cs:137` and
`Source/Intercolony/Core/BusinessReportService.cs:139`. The existing report also treats
suspended obligations as live for contract economics at
`Source/Intercolony/Core/BusinessReportService.cs:139`; whether F07 should show those beside
active rows is an unresolved product choice, not a missing field.

This is not the procurement-side agreement: `ProcurementContract` describes a supplier
promise to provide goods at `Source/Intercolony/Procurement/ProcurementContract.cs:126`,
`Source/Intercolony/Procurement/ProcurementContract.cs:135`,
`Source/Intercolony/Procurement/ProcurementContract.cs:141`. F07's production commitment is
the seller-side `RecurringContract` described above.

### READ: actual recent completed production

The mod's production loop is stock-based. It runs from a `MapComponent` tick at
`Source/Intercolony/Production/ProduceLoopMapComponent.cs:19` and stops starting a new cycle
when `CountStoredThings` reaches the target at
`Source/Intercolony/Production/ProduceLoopMapComponent.cs:49` and
`Source/Intercolony/Production/ProduceLoopMapComponent.cs:51`. That count walks storage groups
and adds `inner.stackCount` at `Source/Intercolony/Production/ProduceLoopMapComponent.cs:180`,
`Source/Intercolony/Production/ProduceLoopMapComponent.cs:195`,
`Source/Intercolony/Production/ProduceLoopMapComponent.cs:202`,
`Source/Intercolony/Production/ProduceLoopMapComponent.cs:205`; it is not a completion event.
The loop is saved as map-scoped loop configuration at
`Source/Intercolony/Production/ProduceLoopMapComponent.cs:274` and
`Source/Intercolony/Production/ProduceLoopMapComponent.cs:277`, not as a production ledger.

The exact vanilla completion symbols (`RecipeDef`, `Bill`, `JobDriver_DoBill`, `GenRecipe`,
`Notify_RecipeProduced`, `RecordsUtility`, `Notify_BillDone`,
`Notify_IterationCompleted`, `MakeRecipeProducts`) have no matches in
`Source/Intercolony` or `Patches`. The exact production-ledger symbols
(`productionLedger`, `ProductionLedger`, `ProductionHistory`, `CompletedProduction`,
`ActualProduction`, `ProductionSample`) also have no matches. The exact production-labor
symbols are absent as well; see [Exact negative searches](#exact-negative-searches).

There are completion transitions in the mod, but they are commercial transitions, not crafted
item observation: `SalesOrderService.Complete` changes a sales order to `Completed`, records
the delivered quantity, and records `SaleCompleted` at
`Source/Intercolony/Orders/SalesOrderService.cs:527`,
`Source/Intercolony/Orders/SalesOrderService.cs:560`,
`Source/Intercolony/Orders/SalesOrderService.cs:571`,
`Source/Intercolony/Orders/SalesOrderService.cs:578`. Likewise,
`PurchaseOrderService.Complete` records a completed purchase at
`Source/Intercolony/Procurement/PurchaseOrderService.cs:659`,
`Source/Intercolony/Procurement/PurchaseOrderService.cs:673`,
`Source/Intercolony/Procurement/PurchaseOrderService.cs:675`.

### CONCLUDE

F07's committed quantity/day is derivable now; F07's actual recent completed production/day
is **NOT FOUND**. There is currently no observer, hook, event handler, or persisted ledger that
can distinguish a newly completed chair from an existing chair sold out of storage, so counting
stock would directly violate the finding's rule at `docs/PLAYTEST_BATCH_SOURCE_PLAN.md:495`,
`docs/PLAYTEST_BATCH_SOURCE_PLAN.md:497`, and `docs/PLAYTEST_BATCH_SOURCE_PLAN.md:499`.

The exact F07 attachment is the Business call seam at
`Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:80`, a new per-good method beside
the existing report methods at `Source/Intercolony/Core/BusinessReportService.cs:128`, and a
new event write from the vanilla completion seam described in [C](#c-what-vanilla-offers-for-observing-a-completed-item).
That is a system because the observation, aggregation, windowing and persistence are all new;
the first useful piece is one narrowly defined completed-product ledger for the active
seller-commitment goods.

## C. What vanilla offers for observing a completed item

### READ

The decompiled bill job executes the recipe work and then the completion/storing toil at
`reference/decompiled/Verse.AI/JobDriver_DoBill.cs:109`,
`reference/decompiled/Verse.AI/JobDriver_DoBill.cs:110`,
`reference/decompiled/Verse.AI/JobDriver_DoBill.cs:111`,
`reference/decompiled/Verse.AI/JobDriver_DoBill.cs:112`.

The work toil initializes `billStartTick` and `ticksSpentDoingRecipeWork` at
`reference/decompiled/Verse.AI/Toils_Recipe.cs:74` and
`reference/decompiled/Verse.AI/Toils_Recipe.cs:75`. Every work interval increments
`ticksSpentDoingRecipeWork` and calls `Bill.Notify_PawnDidWork` at
`reference/decompiled/Verse.AI/Toils_Recipe.cs:91`,
`reference/decompiled/Verse.AI/Toils_Recipe.cs:103`,
`reference/decompiled/Verse.AI/Toils_Recipe.cs:104`; when work reaches zero it calls
`Notify_BillWorkFinished` at `reference/decompiled/Verse.AI/Toils_Recipe.cs:131` and
`reference/decompiled/Verse.AI/Toils_Recipe.cs:133`.

The completion toil calculates ingredients, creates the output list, consumes ingredients,
notifies the bill, and calls `RecordsUtility.Notify_BillDone(actor, list)` at
`reference/decompiled/Verse.AI/Toils_Recipe.cs:191`,
`reference/decompiled/Verse.AI/Toils_Recipe.cs:198`,
`reference/decompiled/Verse.AI/Toils_Recipe.cs:199`,
`reference/decompiled/Verse.AI/Toils_Recipe.cs:200`,
`reference/decompiled/Verse.AI/Toils_Recipe.cs:201`.

The lower-level product path is `GenRecipe.MakeRecipeProducts` at
`reference/decompiled/Verse/GenRecipe.cs:7` and
`reference/decompiled/Verse/GenRecipe.cs:9`. It creates each product and sets its stack count at
`reference/decompiled/Verse/GenRecipe.cs:20`,
`reference/decompiled/Verse/GenRecipe.cs:22`,
`reference/decompiled/Verse/GenRecipe.cs:23`, then calls the virtual
`Thing.Notify_RecipeProduced(worker)` at `reference/decompiled/Verse/GenRecipe.cs:36` before
post-processing at `reference/decompiled/Verse/GenRecipe.cs:37`. The virtual is declared at
`reference/decompiled/Verse/Thing.cs:1701`; `ThingWithComps` forwards it to each component at
`reference/decompiled/Verse/ThingWithComps.cs:775` and
`reference/decompiled/Verse/ThingWithComps.cs:778` through
`reference/decompiled/Verse/ThingWithComps.cs:782`.

Nearby bill and recipe-worker notification methods are virtual at
`reference/decompiled/RimWorld/Bill.cs:274`,
`reference/decompiled/RimWorld/Bill.cs:278`,
`reference/decompiled/RimWorld/Bill.cs:284`,
`reference/decompiled/RimWorld/Bill.cs:288`,
`reference/decompiled/RimWorld/Bill.cs:292`, and
`reference/decompiled/Verse/RecipeWorker.cs:49`. A recipe can select its worker type through
`RecipeDef.workerClass` at `reference/decompiled/Verse/RecipeDef.cs:10` and
`reference/decompiled/Verse/RecipeDef.cs:12`, but the mod has no recipe definitions or custom
recipe worker integration according to the exact search above.

### CONCLUDE

Vanilla provides two useful seams: `Thing.Notify_RecipeProduced` gives a product Thing and
worker, while the completion toil has the recipe, ingredients, output list and bill-doer in one
place at `reference/decompiled/Verse.AI/Toils_Recipe.cs:183`,
`reference/decompiled/Verse.AI/Toils_Recipe.cs:191`,
`reference/decompiled/Verse.AI/Toils_Recipe.cs:198`, and
`reference/decompiled/Verse.AI/Toils_Recipe.cs:201`.

For arbitrary existing vanilla bills, a narrow Harmony patch on a confirmed vanilla seam is the
only practical route available to this repository: there is no mod-owned production event, no
universal product component, and no custom worker covering existing recipes. It is not the only
theoretical extension in RimWorld because a mod that owns a recipe worker or installs a product
component could use the virtuals; this repo does not currently do either, and its patch policy
explicitly keeps Harmony patches few at `Source/Intercolony/Compatibility/HarmonyPatches.cs:9`,
`Source/Intercolony/Compatibility/HarmonyPatches.cs:10`, and
`Source/Intercolony/Compatibility/HarmonyPatches.cs:12`.

The current Harmony set covers settlement/caravan UI, caravan pawn filtering, canceling a
produce loop, storage gizmos and produce gizmos at
`Source/Intercolony/Compatibility/HarmonyPatches.cs:33`,
`Source/Intercolony/Compatibility/HarmonyPatches.cs:71`,
`Source/Intercolony/Compatibility/HarmonyPatches.cs:115`,
`Source/Intercolony/Compatibility/HarmonyPatches.cs:172`,
`Source/Intercolony/Logistics/ReceivingGizmoPatch.cs:12`,
`Source/Intercolony/Logistics/ReceivingGizmoPatch.cs:73`, and
`Source/Intercolony/Production/ProduceGizmoPatch.cs:12`; none is on a recipe completion path.

## D. F19's inputs

### READ

The current economic report has an `inputsIfBought` field at
`Source/Intercolony/Core/BusinessReportService.cs:41` and
`Source/Intercolony/Core/BusinessReportService.cs:42`, but its implementation prices the
contracted finished good itself: it calls `IntercolonyPricing.BaseValue(contract.thingDef,
contract.stuffDef)`, multiplies by the supplier margin, and multiplies by the contract quantity
at `Source/Intercolony/Core/BusinessReportService.cs:80`,
`Source/Intercolony/Core/BusinessReportService.cs:82`,
`Source/Intercolony/Core/BusinessReportService.cs:83`,
`Source/Intercolony/Core/BusinessReportService.cs:84`. The Contracts UI labels that number
“If you bought the goods instead” at `Source/Intercolony/UI/MainTabWindow_Intercolony.cs:4331`
and `Source/Intercolony/UI/MainTabWindow_Intercolony.cs:4332`.

`IntercolonyPricing.BaseValue` is a generic market-value calculation, including material when
the def is made from stuff, at `Source/Intercolony/Market/IntercolonyPricing.cs:680`,
`Source/Intercolony/Market/IntercolonyPricing.cs:684`,
`Source/Intercolony/Market/IntercolonyPricing.cs:691`,
`Source/Intercolony/Market/IntercolonyPricing.cs:693`, and
`Source/Intercolony/Market/IntercolonyPricing.cs:696`. It is not a recipe input resolver.
The exact search for `RecipeDef`, `ThingDefCountClass`, `InputCost`, `MaterialCost`,
`CostToMake`, `ProductionCost`, `CostPerUnit`, `PurchasePrice` and `RecentPurchase` in the
mod returned `NO MATCHES`.

The procurement price service does exist. `RfqService.SupplierUnitPrice` accepts an arbitrary
`ThingDef` argument, but also requires a world state, stuff/quality, settlement profile,
category, supply, distance, delivery mode and quantity at
`Source/Intercolony/Procurement/RfqService.cs:509`,
`Source/Intercolony/Procurement/RfqService.cs:514`,
`Source/Intercolony/Procurement/RfqService.cs:517`,
`Source/Intercolony/Procurement/RfqService.cs:519`,
`Source/Intercolony/Procurement/RfqService.cs:520`,
`Source/Intercolony/Procurement/RfqService.cs:521`,
`Source/Intercolony/Procurement/RfqService.cs:522`,
`Source/Intercolony/Procurement/RfqService.cs:523`, and
`Source/Intercolony/Procurement/RfqService.cs:524`. It delegates to the centralized pricing
service at `Source/Intercolony/Procurement/RfqService.cs:527` and
`Source/Intercolony/Market/IntercolonyPricing.cs:442`.

The centralized supplier calculation starts from `BaseValue` and layers supplier margin,
scarcity, distance, standing, negotiation, logistics and buying difficulty at
`Source/Intercolony/Market/IntercolonyPricing.cs:529`,
`Source/Intercolony/Market/IntercolonyPricing.cs:545`,
`Source/Intercolony/Market/IntercolonyPricing.cs:554`,
`Source/Intercolony/Market/IntercolonyPricing.cs:558`,
`Source/Intercolony/Market/IntercolonyPricing.cs:568`,
`Source/Intercolony/Market/IntercolonyPricing.cs:573`,
`Source/Intercolony/Market/IntercolonyPricing.cs:575`,
`Source/Intercolony/Market/IntercolonyPricing.cs:576`,
`Source/Intercolony/Market/IntercolonyPricing.cs:577`, and
`Source/Intercolony/Market/IntercolonyPricing.cs:579` through
`Source/Intercolony/Market/IntercolonyPricing.cs:586`. A settlement can refuse the item on
technical capability before quoting at `Source/Intercolony/Procurement/RfqService.cs:242`,
`Source/Intercolony/Procurement/RfqService.cs:244`, and
`Source/Intercolony/Procurement/RfqService.cs:398` through
`Source/Intercolony/Procurement/RfqService.cs:417`.

Actual purchase records contain `thingDef`, `stuffDef`, quality, quantity, `unitPrice` and
`paidSilver` at `Source/Intercolony/Procurement/PurchaseOrder.cs:69` through
`Source/Intercolony/Procurement/PurchaseOrder.cs:78`. However, the durable commercial
aggregate stores only exact-good identity, completed sales quantity and combined trade value
at `Source/Intercolony/Core/CommercialHistoryEntry.cs:11` through
`Source/Intercolony/Core/CommercialHistoryEntry.cs:23`; completed purchases add only
`order.paidSilver` at `Source/Intercolony/Core/IntercolonyWorldComponent.cs:317`,
`Source/Intercolony/Core/IntercolonyWorldComponent.cs:325`, and
`Source/Intercolony/Core/IntercolonyWorldComponent.cs:336`. Closed purchase-order detail is
bounded to the most recent 100 entries at
`Source/Intercolony/Core/OrderHistoryService.cs:12`,
`Source/Intercolony/Core/OrderHistoryService.cs:160` through
`Source/Intercolony/Core/OrderHistoryService.cs:190`, including the retention decision at
`Source/Intercolony/Core/OrderHistoryService.cs:177` and
`Source/Intercolony/Core/OrderHistoryService.cs:184`.

### CONCLUDE: attachment and availability

There is no product recipe cost-to-make service today. The existing `inputsIfBought` calculation
is a replacement price for the finished contracted good, not the value of the consumed inputs,
so it does not satisfy the distinction at `docs/PLAYTEST_BATCH_SOURCE_PLAN.md:523`,
`docs/PLAYTEST_BATCH_SOURCE_PLAN.md:525`, `docs/PLAYTEST_BATCH_SOURCE_PLAN.md:529`, and
`docs/PLAYTEST_BATCH_SOURCE_PLAN.md:531`.

An input's **modeled current procurement price** can be obtained through
`RfqService.SupplierUnitPrice` and its `IntercolonyPricing.SupplierUnitPrice` delegate, but it
is context-dependent and may be unavailable for a technically unsupported item. An input's
**recent actual paid price for an arbitrary ThingDef** cannot be obtained through an existing
service: raw order fields may still exist, but the durable history has no purchase quantity or
per-unit price, and no recent-price resolver exists in the exact search.

The exact F19 attachment is `Source/Intercolony/Core/BusinessReportService.cs:82` through
`Source/Intercolony/Core/BusinessReportService.cs:86`: replace the finished-good shortcut with
a per-input cost resolver and feed its total into the report model at
`Source/Intercolony/Core/BusinessReportService.cs:41` through
`Source/Intercolony/Core/BusinessReportService.cs:50`. Render the resulting per-good values at
the shared Business seam `Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:80`.
The first bounded implementation slice should use direct recipe inputs only: vanilla exposes
`RecipeDef.ingredients` at `reference/decompiled/Verse/RecipeDef.cs:31` and product definitions
through `ThingDefCountClass` at `reference/decompiled/Verse/RecipeDef.cs:49`,
`reference/decompiled/Verse/ThingDefCountClass.cs:7`, and
`reference/decompiled/Verse/ThingDefCountClass.cs:9`; the plan explicitly says an intermediate
tradeable good is the consumed input and should not be recursively decomposed automatically at
`docs/PLAYTEST_BATCH_SOURCE_PLAN.md:543`, `docs/PLAYTEST_BATCH_SOURCE_PLAN.md:545`, and
`docs/PLAYTEST_BATCH_SOURCE_PLAN.md:547`.

That first slice is still not the finding by itself: recipe selection, stuff/quality/byproducts,
price evidence, fallback policy and the choice of how much retained history to trust make F19 a
system. It does not inherently require a save-schema bump: the first price tier can read the
existing retained `PurchaseOrder` fields and then fall through to the quote/fallback tiers. A
new durable dated price-observation collection would be world-scoped and would require the bump;
a purely current-quote or retained-order calculation would not.

## E. F20's labour

### READ

The current per-product/contract estimate stores a payroll cost at
`Source/Intercolony/Core/BusinessReportService.cs:44` and
`Source/Intercolony/Core/BusinessReportService.cs:45`. `Estimate` assigns it as the negative
whole payroll over the contract cadence at `Source/Intercolony/Core/BusinessReportService.cs:86`.
`PayrollForPeriod` then loops every employment at
`Source/Intercolony/Core/BusinessReportService.cs:109`,
`Source/Intercolony/Core/BusinessReportService.cs:116`, and
`Source/Intercolony/Core/BusinessReportService.cs:117`, includes every active employment at
`Source/Intercolony/Core/BusinessReportService.cs:119`, and adds each contract's
`dailyWage` at `Source/Intercolony/Core/BusinessReportService.cs:120` through
`Source/Intercolony/Core/BusinessReportService.cs:125`. The service explicitly says it does not
know who works on what at `Source/Intercolony/Core/BusinessReportService.cs:100`,
`Source/Intercolony/Core/BusinessReportService.cs:103`, and
`Source/Intercolony/Core/BusinessReportService.cs:104`.

The Contracts UI exposes the same number as “Wage bill over the cycle” at
`Source/Intercolony/UI/MainTabWindow_Intercolony.cs:4334` and
`Source/Intercolony/UI/MainTabWindow_Intercolony.cs:4335`; its interpretation says that making
the goods rather than buying them is worth the margin at
`Source/Intercolony/UI/MainTabWindow_Intercolony.cs:4173` through
`Source/Intercolony/UI/MainTabWindow_Intercolony.cs:4176`.

Employment state knows the pawn, frozen worker name and daily wage at
`Source/Intercolony/Labor/EmploymentContract.cs:88`,
`Source/Intercolony/Labor/EmploymentContract.cs:89`,
`Source/Intercolony/Labor/EmploymentContract.cs:105`,
`Source/Intercolony/Labor/EmploymentContract.cs:106`, and
`Source/Intercolony/Labor/EmploymentContract.cs:109`. It also saves held work priorities at
`Source/Intercolony/Labor/EmploymentContract.cs:280` and
`Source/Intercolony/Labor/EmploymentContract.cs:281`, but that is a priority snapshot, not a
record of a bill, product or work duration.

Payroll does calculate elapsed contract-service time for an unfinished pay period: it computes
`workedTicks` from the period start and converts it to wage-days at
`Source/Intercolony/Labor/PayrollService.cs:309`,
`Source/Intercolony/Labor/PayrollService.cs:321`,
`Source/Intercolony/Labor/PayrollService.cs:325`,
`Source/Intercolony/Labor/PayrollService.cs:339`,
`Source/Intercolony/Labor/PayrollService.cs:340`, and
`Source/Intercolony/Labor/PayrollService.cs:342`. This is elapsed employment/term time; it does
not identify the production job or product.

The only fine-grained employee sampler currently called by the world tick is the combat-use
monitor at `Source/Intercolony/Core/IntercolonyWorldComponent.cs:1605` through
`Source/Intercolony/Core/IntercolonyWorldComponent.cs:1612`. That monitor is explicitly about
drafted attacks at `Source/Intercolony/Labor/CombatUseMonitor.cs:8` through
`Source/Intercolony/Labor/CombatUseMonitor.cs:15`, and samples active employments for attack
ticks at `Source/Intercolony/Labor/CombatUseMonitor.cs:51` through
`Source/Intercolony/Labor/CombatUseMonitor.cs:95`; it is not a production-work ledger.

### CONCLUDE: attachment and availability

Today's labour attribution is the whole active-workforce wage bill, based on `dailyWage` and
elapsed contract cadence, not actual recent work. Nothing records which employee worked on which
good or how many production ticks that good consumed; the exact production-labor search returned
`NO MATCHES`.

The exact F20 report attachment is the payroll assignment at
`Source/Intercolony/Core/BusinessReportService.cs:86` and the whole-workforce implementation at
`Source/Intercolony/Core/BusinessReportService.cs:109` through
`Source/Intercolony/Core/BusinessReportService.cs:125`. The observation seam is the vanilla work
interval at `reference/decompiled/Verse.AI/Toils_Recipe.cs:91` through
`reference/decompiled/Verse.AI/Toils_Recipe.cs:104`, paired with the output boundary at
`reference/decompiled/Verse.AI/Toils_Recipe.cs:198` through
`reference/decompiled/Verse.AI/Toils_Recipe.cs:201`; the rendered result belongs at
`Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs:80`.

F20 is a system. The smallest useful actual-work piece is a narrow, event-driven ledger for one
production path containing worker, product, work ticks and a timestamp, then valuing those ticks
from the employee compensation. If that instrumentation is too invasive or brittle, the plan's
honest fallback is a relevant-workforce approximation at
`docs/PLAYTEST_BATCH_SOURCE_PLAN.md:588`, `docs/PLAYTEST_BATCH_SOURCE_PLAN.md:590`, and
`docs/PLAYTEST_BATCH_SOURCE_PLAN.md:592`; that fallback is a bounded estimate but is not the
preferred actual-work finding.

## F. Persistence and schema

### READ

`IntercolonyWorldComponent` is the authoritative persistent owner because its state is world-level,
not map-level, and must survive multiple maps and caravans at
`Source/Intercolony/Core/IntercolonyWorldComponent.cs:11` through
`Source/Intercolony/Core/IntercolonyWorldComponent.cs:23`. Its schema constant is
`CurrentSaveVersion = 57` at `Source/Intercolony/Core/IntercolonyWorldComponent.cs:25` through
`Source/Intercolony/Core/IntercolonyWorldComponent.cs:30`; the comment requires a bump whenever
the saved shape changes and a migration in `MigrateIfNeeded` at
`Source/Intercolony/Core/IntercolonyWorldComponent.cs:26` through
`Source/Intercolony/Core/IntercolonyWorldComponent.cs:28`.

The existing rolling ledger is world-scoped and persisted at
`Source/Intercolony/Core/IntercolonyWorldComponent.cs:711` through
`Source/Intercolony/Core/IntercolonyWorldComponent.cs:720`; it is pruned to a rolling year at
`Source/Intercolony/Core/IntercolonyWorldComponent.cs:714` through
`Source/Intercolony/Core/IntercolonyWorldComponent.cs:716`. World persistence is wired in
`ExposeData` at `Source/Intercolony/Core/IntercolonyWorldComponent.cs:1161` through
`Source/Intercolony/Core/IntercolonyWorldComponent.cs:1200`, including contracts,
procurement contracts, employments and the cash ledger at
`Source/Intercolony/Core/IntercolonyWorldComponent.cs:1193` through
`Source/Intercolony/Core/IntercolonyWorldComponent.cs:1200`.

The existing produce-loop component is different: it explicitly says map state is independent of
the world schema at `Source/Intercolony/Production/ProduceLoopMapComponent.cs:7` through
`Source/Intercolony/Production/ProduceLoopMapComponent.cs:9`. Business cash already aggregates
across every player map at `Source/Intercolony/Core/BusinessReportService.cs:159` through
`Source/Intercolony/Core/BusinessReportService.cs:175`, so a global per-good Business history
belongs beside the other authoritative economic state, not only inside one map's loop component.

The migration code also rejects invented historical dates: the ledger migration was deliberately
not backfilled because old cumulative totals know how much but not when at
`Source/Intercolony/Core/IntercolonyWorldComponent.cs:2172` through
`Source/Intercolony/Core/IntercolonyWorldComponent.cs:2179`.

### CONCLUDE

- **F07:** a completed-production event ledger keyed by product identity and tick should live in
  `IntercolonyWorldComponent`; the Business window can aggregate it across maps. Persisting that
  new rolling history changes the world save shape, so it needs a bump from 57 and a migration.
- **F19:** a direct-input resolver can use retained `PurchaseOrder` fields, the existing quote
  service and the generic fallback with no new saved state, so that slice needs no bump. If the
  implementation adds durable dated purchase-price observations beyond the bounded order detail,
  those observations belong in world state and need a bump from 57 and a migration.
- **F20:** actual recent work samples need a persisted world-scoped labor ledger, so the full
  finding needs a bump from 57 and a migration. The relevant-workforce fallback can be computed
  from existing active employments and needs no new history, but it does not deliver actual
  employee-to-product attribution.

## G. Self-test surface

### READ

The all-suite registry includes `ledger`, `cash-flow` and `produce` at
`Source/Intercolony/Debug/IntercolonyAllSelfTests.cs:148`,
`Source/Intercolony/Debug/IntercolonyAllSelfTests.cs:159`, and
`Source/Intercolony/Debug/IntercolonyAllSelfTests.cs:175`; the runner executes every registered
definition at `Source/Intercolony/Debug/IntercolonyAllSelfTests.cs:258` through
`Source/Intercolony/Debug/IntercolonyAllSelfTests.cs:293`.

`IntercolonyLedgerSelfTest` is the current report-service suite: it labels itself “Ledger and
business report self-test” at `Source/Intercolony/Debug/IntercolonyLedgerSelfTest.cs:49` and
`Source/Intercolony/Debug/IntercolonyLedgerSelfTest.cs:52`, calls
`BusinessReportService.Estimate` at `Source/Intercolony/Debug/IntercolonyLedgerSelfTest.cs:299`
and `Source/Intercolony/Debug/IntercolonyLedgerSelfTest.cs:300`, and asserts the current
supplier-margin input formula at `Source/Intercolony/Debug/IntercolonyLedgerSelfTest.cs:316`
through `Source/Intercolony/Debug/IntercolonyLedgerSelfTest.cs:324`. It also covers ledger
windowing at `Source/Intercolony/Debug/IntercolonyLedgerSelfTest.cs:119` through
`Source/Intercolony/Debug/IntercolonyLedgerSelfTest.cs:157`.

`IntercolonyCashFlowSelfTest` directly covers `CashFlowForecast.Compute` at
`Source/Intercolony/Debug/IntercolonyCashFlowSelfTest.cs:43` through
`Source/Intercolony/Debug/IntercolonyCashFlowSelfTest.cs:52` and
`Source/Intercolony/Debug/IntercolonyCashFlowSelfTest.cs:130`; it is the forecast companion for
the Business page, not a UI-drawing test. `IntercolonyProduceSelfTest` tests stock-target
behavior, including “a met target stops the next cycle” at
`Source/Intercolony/Debug/IntercolonyProduceSelfTest.cs:612` through
`Source/Intercolony/Debug/IntercolonyProduceSelfTest.cs:624` and
`Source/Intercolony/Debug/IntercolonyProduceSelfTest.cs:677` through
`Source/Intercolony/Debug/IntercolonyProduceSelfTest.cs:690`.

The direct search for `DrawBusiness`, `MainTabWindow_Intercolony_Business`, `Business view`
and `Business tab` under `Source/Intercolony/Debug` returned `NO MATCHES`.

### CONCLUDE

There is no suite that exercises the Business UI itself. The closest coverage is
`IntercolonyLedgerSelfTest` for `BusinessReportService` and `IntercolonyCashFlowSelfTest` for
the forecast service; the current tests can assert the existing placeholder supplier-input
formula and its sign/margin arithmetic, but not the requested semantics.

The following cannot be asserted at all today:

- F07 actual recent completed production, because there is no completion observer or production
  ledger to feed a test.
- F19 per-input replacement costing, because there is no recipe/input resolver and no durable
  recent purchase-price model.
- F20 actual worker-to-good attribution or production work duration, because there is no
  production-task ledger.

## Exact negative searches

These were run against `Source/Intercolony` and `Patches` without modifying them:

```text
rg -n "\b(QuantityPerDay|quantityPerDay|CommittedQuantityPerDay|committedQuantityPerDay)\b" Source/Intercolony -g '*.cs'
NO MATCHES

rg -n "\b(RecipeDef|Bill|JobDriver_DoBill|GenRecipe|Notify_RecipeProduced|RecordsUtility|Notify_BillDone|Notify_IterationCompleted|MakeRecipeProducts)\b" Source/Intercolony Patches -g '*.cs' -g '*.xml'
NO MATCHES

rg -n "\b(productionLedger|ProductionLedger|ProductionHistory|CompletedProduction|ActualProduction|ProductionSample)\b" Source/Intercolony Patches -g '*.cs' -g '*.xml'
NO MATCHES

rg -n "\b(RecipeDef|ThingDefCountClass|InputCost|MaterialCost|CostToMake|ProductionCost|CostPerUnit|PurchasePrice|RecentPurchase)\b" Source/Intercolony Patches -g '*.cs' -g '*.xml'
NO MATCHES

rg -n "\b(ProductionLabor|LaborLedger|WorkLedger|WorkedTicks|WorkDuration|ProductWork|EmployeeProduction|PawnProduction|LaborHistory|ProductionWork)\b" Source/Intercolony Patches -g '*.cs' -g '*.xml'
NO MATCHES

rg -n "\b(ThingComp|CompProperties|Notify_RecipeProduced)\b" Source/Intercolony -g '*.cs'
NO MATCHES

rg -n "DrawBusiness|MainTabWindow_Intercolony_Business|Business view|Business tab" Source/Intercolony/Debug -g '*.cs'
NO MATCHES
```

## Unanswered by repository-only recon

The exact F07/F20 rolling-window statistic and the exact F19 outlier-resistant aggregation
cannot be answered from the repository because the plan intentionally leaves those methods for
implementation at `docs/PLAYTEST_BATCH_SOURCE_PLAN.md:505`,
`docs/PLAYTEST_BATCH_SOURCE_PLAN.md:541`, and
`docs/PLAYTEST_BATCH_SOURCE_PLAN.md:580`. The exact Harmony target to ship cannot be validated
against runtime behavior or other mods without launching the game; the decompiled seams and
theoretical extension points are documented in [C](#c-what-vanilla-offers-for-observing-a-completed-item).

The current live colony's empirical completion counts, recent purchase prices and worker task
allocation are also unanswered: the repository contains schemas and code paths, not the live
world values, and no runtime was launched for this read-only recon. Whether suspended seller
agreements should appear in F07's “active” rows remains a product decision because existing
economics deliberately treats suspended agreements as live at
`Source/Intercolony/Core/BusinessReportService.cs:137` through
`Source/Intercolony/Core/BusinessReportService.cs:143`.
