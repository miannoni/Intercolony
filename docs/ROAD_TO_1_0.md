# Road to 1.0

This is a conservative audit of the Phase 27 criteria in `DESIGN.md` §120. A criterion is
**met and proven** only when the implementation has a code path and the project record contains a
play observation or a self-test that asserts the relevant behavior. A working-looking code path is
**met but unproven** when the missing evidence is the result. A partial implementation is **not
met**.

All play evidence comes from one machine and one load order: RimWorld 1.6.4871, Biotech,
Hospitality, Common Sense, RT Fuse, Tilled Soil, FSF Filth Vanishes With Rain And Time, and 1.75x UI
scale. Royalty, Ideology and Anomaly were not installed. `docs/PENDING_PLAYTESTS.md:18-38` defines
that boundary. Evidence outside it is not implied.

## Met and proven

### Commerce

- **Player can accept Sales Orders.** `SalesOrderService.Accept` in
  `Source/Intercolony/Orders/SalesOrderService.cs:22` creates the binding order. The full loop was
  played without transaction debug tools: accept, caravan delivery, silver payment and return
  (`PROGRESS.md:196-233`).
- **Deadlines and delivery work.** `SalesOrderService.Deliver` and
  `SalesOrderService.FailOverdueOrders` are in
  `Source/Intercolony/Orders/SalesOrderService.cs:190` and `:441`. `IntercolonyOrderSelfTest`
  asserts deadline arithmetic and the overdue sweep, and seller delivery completed in play
  (`PROGRESS.md:228-232`). A missed deadline has not separately been watched in ordinary play; the
  deadline half is self-test evidence.
- **Quality and material conditions work.** The single matcher is
  `OrderValidator.Matches` in `Source/Intercolony/Orders/OrderValidation.cs:388`. A material-made,
  Normal-or-better sculpture order was delivered in play (`PROGRESS.md:342-347`), and a 60%
  condition floor was generated and correctly refused through buyer pickup
  (`docs/PENDING_PLAYTESTS.md:119-124`). The seller-delivery refusal uses the same matcher but its
  separate caravan gizmo remains unplayed.
- **Commodities and finished goods work.** `IntercolonyProductClassifier.Classify` and
  `OrderValidator.Matches` provide the shared paths. The commodity sale loop was played
  (`PROGRESS.md:228-233`); `IntercolonyOrderSelfTest` covers rice, cloth, quality weapons and
  minified chairs (`PROGRESS.md:277-283`); furniture and art were delivered in play
  (`PROGRESS.md:342-347`).
- **Furniture, art and equipment work.** Minified goods are unwrapped in
  `Source/Intercolony/Orders/OrderValidation.cs:374`, while
  `IntercolonyProductClassifier.Classify` separates art, furniture and capital equipment in
  `Source/Intercolony/Market/IntercolonyProductClassifier.cs:62-103`. A tube television and large
  sculptures completed real sales (`PROGRESS.md:342-347`), and `IntercolonyRfqSelfTest` constructs
  and inspects a crated electric stove (`PROGRESS.md:455-459`). This proves the equipment path by
  self-test, not a played equipment sale.
- **Find Buyer works.** `FindBuyerService.FindBuyers` is in
  `Source/Intercolony/Market/FindBuyerService.cs:69`; `SalesOrderService.CreateFromOffer` binds the
  selected result. A Find Buyer order was created, delivered and paid in play
  (`PROGRESS.md:374-377`).

### Procurement

- **RFQs work.** `RfqService.CreateRequest` and `RfqService.GenerateResponses` are in
  `Source/Intercolony/Procurement/RfqService.cs:29` and `:84`. Requests for berries and a table were
  created through the UI and returned quotes, and `IntercolonyRfqSelfTest` passed
  (`PROGRESS.md:416-421`).
- **Quotes can be zero, partial or full.** `RfqService.TryQuote` and its offered-quantity logic are in
  `Source/Intercolony/Procurement/RfqService.cs:170-237`. `IntercolonyRfqSelfTest` explicitly counted
  3 empty requests, 66 full quotes and 84 partial quotes (`PROGRESS.md:416-421`).
- **Purchases physically arrive or are collected.** The criterion says either. The delivery branch
  is `PurchaseOrderService.AdvanceOrders` and `DeliverToColony` in
  `Source/Intercolony/Procurement/PurchaseOrderService.cs:144-177`. A Good gold bed arrived at the
  colony after seven in-game days with its properties intact (`PROGRESS.md:455-459`). The caravan
  collection branch, `PurchaseOrderService.CollectWithCaravan`, exists but has no recorded play
  observation.
- **Capital equipment can be purchased.** `PurchaseOrderService.MakeGoods` in
  `Source/Intercolony/Procurement/PurchaseOrderService.cs:314` crates minifiable purchases.
  `IntercolonyRfqSelfTest` drives a crated electric stove through the purchase-goods construction and
  inspection path (`PROGRESS.md:455-459`). This is self-test proof; an equipment purchase has not
  been completed by a player.

### Logistics

- **There is no default magical instant transaction for everything.** Seller delivery removes goods
  from a real caravan through `SalesOrderService.Deliver`; supplier delivery waits for
  `PurchaseOrder.readyTick` before `PurchaseOrderService.DeliverToColony`; player pickup uses
  `PurchaseOrderService.CollectWithCaravan`. The seller loop was caravanned in play and the gold bed
  arrived after seven days (`PROGRESS.md:228-232`, `PROGRESS.md:455-459`). Buyer pickup is delayed
  but abstract: no buyer caravan world object is spawned (`PROGRESS.md:484-493`).
- **At least two meaningful fulfillment modes exist.** Seller delivery and buyer pickup have both
  completed in play. Buyer pickup is implemented by `SalesOrderService.MarkReadyForPickup` and
  `ProcessBuyerCollections` in `Source/Intercolony/Orders/SalesOrderService.cs:268-313`; two real
  orders completed through it, including packed minified furniture found and consumed by the indexed
  validator (`docs/PENDING_PLAYTESTS.md`, **Buyer pickup, end to end**). Save/reload while awaiting
  collection and partial or failed collection remain useful depth tests, but are not required to
  establish that the second mode exists and completes.

### Relationships

- **Commercial Reputation persists.** `CommercialReputation.ExposeData` in
  `Source/Intercolony/Reputation/CommercialReputation.cs:120` scribes the settlement record, and
  `ReputationService.For` retrieves it. `IntercolonyReputationSelfTest` passed and the real save
  migrated through the reputation schemas (`PROGRESS.md:539-544`).
- **Reputation changes opportunities.** `ReputationService.OpportunityFrequencyFactor`,
  `LotSizeFactor`, `PriceFactor` and `DeadlineDays` feed generation. The named reputation self-test
  held settlement and seeds constant: trusted history produced 101 offers averaging 213 units and
  16.5-day deadlines, versus 39 offers averaging 86 units and 11.1 days for distrusted history
  (`PROGRESS.md:539-543`).
- **Recurring supply contracts exist.** `RecurringContract.ExposeData` and
  `ContractService.AdvanceContracts` implement the persisted cycle. `IntercolonyContractSelfTest`
  asserts the lifecycle (`PROGRESS.md:577-581`), and a real agreement was offered, accepted, credited
  to completion and renewed in the Contracts tab (`docs/PENDING_PLAYTESTS.md:163-166`).

### Labor

- **Temporary workers can be hired.** `EmploymentService.TryHire` and `TryHireApplicant` are in
  `Source/Intercolony/Labor/EmploymentService.cs:39` and `:203`. Hiring through a two-applicant
  posting was re-tested after fixing the list-mutation crash (`docs/PENDING_PLAYTESTS.md:111-118`).
- **Longer contracts exist.** `EmploymentContract.ExposeData` persists fixed-term and open-ended
  terms; `RenewalService.Accept` extends an employment in place. Open-ended dismissal and both
  positive and refused renewal were played (`docs/PENDING_PLAYTESTS.md:155-162`). This criterion is
  about the contract forms existing. It does not close the outstanding several-season quest-lodger
  stability test (`docs/PENDING_PLAYTESTS.md:52-64`).
- **Wages and payroll work.** `PayrollService.Advance`, `BeginPayroll` and `SettleOnEnd` are in
  `Source/Intercolony/Labor/PayrollService.cs:31`, `:220` and `:239`.
  `IntercolonyPayrollSelfTest` passed 39 assertions, and daily wages, a forced shortfall, and reload
  while in arrears were played (`PROGRESS.md:879-888`).
- **Arrears have consequences.** `PayrollService.OnPeriodMissed` records the escalation and can end
  the contract as `Quit`; `LaborDebt.ExposeData` persists the remainder. The payroll self-test drove
  warning, tools down, restored work, walk-out, debt and settlement
  (`PROGRESS.md:879-885`). The tools-down stage is soft because the player can manually restore work
  priorities (`PROGRESS.md:871-874`), but the later walk-out and debt are enforced consequences.
- **Employer Reputation works.** `EmployerReputation.ExposeData` persists the score and
  `EmployerReputationService.WageFactor` affects hiring. `IntercolonyEmployerReputationSelfTest`
  checks every score from 0 to 100 and measured the same world at 20 versus 7 available workers,
  with a corresponding skill difference (`PROGRESS.md:967-972`).

### Reliability

- **Performance is acceptable in the tested large modded world.** `IntercolonyPerformanceProfile.Run`
  executes the production refresh, classification, profile and census paths. On the verified load
  order, a real save with 252 settlements, 900 workers and 25,416 map things measured a 3.1 ms daily
  refresh; the other reported paths were below 7 ms cold (`PROGRESS.md:1749-1753`). This is a narrow
  finding for one machine and five-mod load order, not a general performance guarantee. The indexed
  colony validator and minified-furniture lookup were not separately re-measured.

### Compatibility

- **Unsupported special items can be excluded cleanly.** `IntercolonyTradeBlacklist.ResolveReason`
  in `Source/Intercolony/Market/IntercolonyTradeBlacklist.cs:103` accepts def, category and comp
  rules. `IntercolonyMarketSelfTest` asserts blacklist enforcement at classification and generation,
  and fertilized eggs were observed absent in play (`PROGRESS.md:181-188`). Non-minifiable buildings
  are also rejected structurally because they cannot be carried.

### UX

- **The market is usable without debug tools.** The ordinary market Accept action and confirmation
  are in `Source/Intercolony/UI/MainTabWindow_Intercolony.cs:900-1125`. A market order completed
  without transaction debug tools (`PROGRESS.md:228-232`), Find Buyer completed in play
  (`PROGRESS.md:374-377`), and RFQs were created from their UI (`PROGRESS.md:416-421`). This is proven
  only at 1.75x UI scale in the recorded load order.

## Met but unproven

### Commerce

- **Settlements generate meaningful demand.** `MarketOpportunityGenerator.GenerateFor` in
  `Source/Intercolony/Market/MarketOpportunityGenerator.cs:33` uses settlement profile, wealth,
  category, quantity and distance, and `IntercolonyMarketSelfTest` checks deterministic and selective
  generation. The remaining word is subjective: quantities and balance are documented as first-pass
  guesses (`PROGRESS.md:174-179`, `PROGRESS.md:338-340`). To prove it, play several colonies through
  multiple refreshes and record offers that cause different production, rejection and logistics
  decisions rather than merely producing valid rows.

### Procurement

- **Scarcity remains meaningful.** `RfqService.CanTechnicallySupply` and `TryQuote` in
  `Source/Intercolony/Procurement/RfqService.cs:170-312` enforce technology, response chance,
  capacity and scarcity pricing. `IntercolonyRfqSelfTest` and a targeted psylink probe prove that
  zero quotes can happen, but the response, capacity and spread numbers remain first-pass guesses
  (`PROGRESS.md:411-425`). Prove “meaningful” in normal play across colony and supplier tech levels:
  scarcity should cause a wait, split order, substitution or decision not to buy, without making
  ordinary procurement arbitrary or routinely empty.

### Logistics

- **Distance affects decisions.** `MarketOpportunityGenerator` changes fulfillment likelihood by
  distance (`Source/Intercolony/Market/MarketOpportunityGenerator.cs:119-122`), and `RfqService`
  applies distance to response, price, mode and lead time. The UI exposes distance, but the project
  has not recorded a player choosing between near and far counterparties because of it. Prove it
  with otherwise comparable offers where price, deadline or mode makes the nearer or farther option
  preferable. The current measure is approximate tile distance, not routed caravan time
  (`PROGRESS.md:174-177`).

### Reliability

- **Save/load is robust.** `IntercolonyWorldComponent.ExposeData` in
  `Source/Intercolony/Core/IntercolonyWorldComponent.cs:671` scribes and normalizes the entity lists,
  and `MigrateIfNeeded` plus `ValidateIds` handle old state. Orders survived a byte-identical matrix
  (`PROGRESS.md:228-233`), schema 17 migrated to 22 in play, and the schema 22-to-23 procurement
  migration has now also run successfully (`docs/PENDING_PLAYTESTS.md`, **Save migration 22 → 23**).
  This remains unproven because no current-schema save/load matrix contains every active entity type,
  and long-run employee stability across seasons and reloads is explicitly outstanding. Prove those
  two remaining cases, including the long-run employee test in `docs/PENDING_PLAYTESTS.md:52-64`.
- **There is no known persistent-state corruption.** Load validation reports or drops unresolved
  active records rather than silently retaining invalid state in
  `IntercolonyWorldComponent.ExposeData`, and the historical cross-game candidate leak was re-tested
  clean (`docs/PENDING_PLAYTESTS.md`, **Cross-game state leak**). The two played migration paths add
  evidence, but they are not enough to turn absence of evidence into a universal result while the
  current-schema all-entity matrix and several-season employment remain untested. Those same two
  tests would establish the claim within the recorded environment.

### Compatibility

- **Common modded items work generically.** `IntercolonyProductClassifier.Classify` and
  `IsFungibleTradeItem` in `Source/Intercolony/Market/IntercolonyProductClassifier.cs:37-103` and
  `:206-219` branch on def properties, not content package. The measured load order contains 406
  eligible defs: Core 337, Biotech 67 and RT Fuse 2, with zero from the other five loaded mods
  (`docs/PENDING_PLAYTESTS.md`, **Per-source trade classification**). That proves DLC and modded defs
  are classified generically, not that one has completed a transaction. Prove this criterion by
  completing sales and purchases of ordinary items from at least Biotech and RT Fuse, then
  deliberately exercise a pawn-management mod with an active employee as requested in
  `docs/PENDING_PLAYTESTS.md:90-105`.
- **DLC checks do not assume every expansion is installed.** Production classification is driven by
  loaded defs through `IntercolonyProductClassifier`; no Royalty, Ideology, Anomaly or Biotech gate
  is required by that path. Only Biotech was installed, so the absence cases have not been observed.
  Prove this with a Core-only start and market/RFQ/save-load smoke test, then the same test with each
  available DLC combination. Unowned expansions remain untested rather than unsupported.

### UX

- **The player can understand obligations.** Market tooltips and confirmations state constraints,
  deadline, mode and price in `Source/Intercolony/UI/MainTabWindow_Intercolony.cs:999-1125`; active
  order rows show delivered quantity, time, payment and mode. Individual screens have been read in
  play, but no test has judged the complete market, procurement and contract obligation flow. Have a
  player accept one sale, purchase and recurring contract without prior explanation, then ask them
  to identify what, where and when each requires before they act. The full-density Business report
  remains outstanding (`docs/PENDING_PLAYTESTS.md:66-78`).
- **The player can understand why a transaction succeeded or failed.** `OrderValidationResult` and
  `MatchFailure` in `Source/Intercolony/Orders/OrderValidation.cs` carry structured reasons to the UI;
  the buyer-pickup condition refusal was understood in play. The seller-caravan refusal and the
  other major failure classes have not been reviewed together. Prove it by causing deadline,
  quality, material, condition, insufficient-stock, insufficient-silver, pickup-expiry and RFQ-expiry
  failures and checking that the relevant row, letter or message names both the cause and the next
  available action.
- **Important deadlines are visible.** Market expiry/deadline columns and near-expiry color are in
  `Source/Intercolony/UI/MainTabWindow_Intercolony.cs:650-999`; active orders show time remaining and
  warn below one day in the same file at `:2327-2350`. RFQs, contracts and pickups also expose time
  text, but no recorded play-test judged whether deadlines remain noticeable across tabs. Prove it
  at several UI scales with simultaneous near-term sale, RFQ, purchase pickup, employment and
  contract deadlines, including while the Business report is dense.

## Not met

### Labor

- **Combat and death risks are accounted for.** The implemented part is substantial:
  `CombatUseMonitor` escalates clause breaches and `CompensationService` prices death and permanent
  injury. A civilian death and compensation debt were played end to end
  (`docs/PENDING_PLAYTESTS.md:149-150`). The criterion is still partial because incapacitation is
  unhandled and capture is not modelled (`PROGRESS.md:1065-1073`); the pending-play record also says
  neither has been exercised (`docs/PENDING_PLAYTESTS.md:80-88`). Matteo should decide whether
  “risks” was intended to include these states. Under the conservative reading, code and tests for
  downing, recovery, capture, release and contract/payroll outcomes are missing.
- **Employees leave cleanly.** Normal and safe-passage exits are implemented in
  `EmploymentService.End` and `FinishSafePassage`
  (`Source/Intercolony/Labor/EmploymentService.cs:629-674`, `:800-893`), and ordinary exits are
  self-tested. It is not complete: a downed worker can have their faction restored while remaining
  on the map, capture has no model, social relations formed during employment have not been checked,
  and the worker's bed remains claimed after departure (`PROGRESS.md:1069-1084`,
  `docs/PENDING_PLAYTESTS.md:80-88`). Departure needs an explicit downed/captured policy, bed
  ownership cleanup, and a relation-preservation play-test.

### Reliability

- **Major edge cases degrade gracefully.** The UI draw guard does degrade a broken page to a usable
  fallback (`Source/Intercolony/UI/MainTabWindow_Intercolony.cs:175-220`), Harmony postfixes are
  guarded, and `EmploymentService.End` now catches quest-teardown failure, reports it as an error and
  makes a separately guarded attempt to restore a still-player-faction worker before references are
  cleared (`Source/Intercolony/Labor/EmploymentService.cs:844-916`). The criterion still does not
  move: downed and captured employees remain unhandled and need defined outcomes.

## Summary

- **Met and proven:** 23
- **Met but unproven:** 10
- **Not met:** 3
- **Total criteria audited:** 36

## Shortest path to 1.0

### Code work

1. Define and implement downed and captured employee lifecycles: contract state, payroll, medical or
   compensation treatment, recovery, release, faction restoration and player-facing outcome.
2. Explicitly unclaim the employee's bed on final departure.
3. Add focused self-tests for those transitions, including save/load while downed or captured.

Those changes close all three criteria currently in **not met**, subject to the ambiguity about how
broad “combat/death risks” was intended to be.

### Play-testing

1. Run the several-season employment test with repeated saves, reloads and renewals. Include a worker
   who forms social relations, and repeat the downed/captured departures after the code exists.
2. Run an all-entity schema-23 save/load matrix.
3. Exercise buyer pickup across save/load and a partial or failed collection, then make a real
   distance-driven choice between counterparties.
4. Judge demand and procurement scarcity over several colonies and refreshes. Record decisions, not
   only valid generation.
5. Complete ordinary Biotech and RT Fuse sale/purchase paths, a deliberate pawn-management-mod labor
   interaction, and a Core-only DLC smoke test.
6. Run the obligation, failure-reason and deadline UX checks at multiple UI scales, with the Business
   report populated by revenue, purchases and payroll at once.

Phase 26 still requires public beta play by other people. The code work above is small compared with
the evidence gap; coding cannot substitute for those independent sessions.

## Questions that need clarification

- What makes demand, scarcity or a fulfillment mode “meaningful”: any real choice, a balance target,
  or a minimum frequency over a defined play period?
- Does “capital equipment” mean the classifier category, any minifiable production building, or a
  narrower named set? The current proof uses a crated electric stove.
- Does “combat/death risks” require explicit downed and captured outcomes, and must preventable death
  be distinguished from unavoidable death?
- What scope makes save/load “robust” and performance acceptable in a “large modded world”? This
  audit used the one recorded load order and the 252-settlement save.
- Does “common modded items work generically” mean correct classification or a completed physical
  transaction? This audit requires the latter for proof.
- Does “the market is usable without debug tools” mean only commerce, or every market, procurement,
  contract and labor workflow in the window?

## Documentation and code drift found during the audit

During this audit, Player.log evidence was observed and reported in conversation but not entered in
`docs/PENDING_PLAYTESTS.md`: buyer-pickup completions, the 22-to-23 migration, and the per-source
classification result. That is the exact evidence-loss failure the play-test record exists to
prevent. The observations are now recorded there with their dates and scope limits rather than being
left only in conversation.

No current behavioral claim in `PROGRESS.md` or `docs/PENDING_PLAYTESTS.md` was disproved by the code.
One record has fallen behind the source:

- `PROGRESS.md:1682-1688` is still the deliberately partial Phase 25 pass-A entry and says settings,
  UI-scale and compatibility passes had not started. The current source contains
  `IntercolonySettings`, fulfillment preference UI and the classification dump, and
  `docs/COMPATIBILITY.md` now records the later compatibility work. The historical entry was true
  when written, but it is no longer a complete description of the repository.

The code-level cleanup-failure hole found during the audit is now guarded by a last-resort faction
restore. It does not change the recorded normal-departure results, and that exception path has not
been exercised in play.
