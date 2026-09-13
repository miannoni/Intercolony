# Intercolony Stage 1 seam map

Read-only reconnaissance for F01, F02, F13, and F15. I read only those four sections of
`docs/PLAYTEST_BATCH_SOURCE_PLAN.md` (`:63`, `:91`, `:385`, and `:456`) and did not implement
any requested behavior.

## Executive findings

- F01 has one routine supply/selling success-letter call at
  `Source/Intercolony/Orders/SalesOrderService.cs:754`, reached by the contract auto-ready
  path at `Source/Intercolony/Contracts/ContractService.cs:1315`. The procurement loop has no
  per-cycle success letter; its success branch is `Source/Intercolony/Procurement/ProcurementContractService.cs:968`.
- F01 failure notification is throttled by inline booleans, not a named throttle method.
- F02's poll cannot distinguish a canceled blueprint from an absent/not-yet-built blueprint.
  The player path is `Designator_Cancel.DesignateThing` -> `Thing.Destroy(DestroyMode.Cancel)`.
  A cancel observation seam is therefore needed.
- F13's row renderer is `DrawEmployeeRow`; auto-renew is currently only in its `...` menu.
- F15's three gameplay agreement construction sites omit the auto-ready assignment, so the
  current C# default is false. Missing `autoReadyOrders` XML also loads false for old saves.

## F01 - silent routine auto-ready success, loud failure

### Letter plumbing and supply/selling path

The repository's letter wrapper is `IntercolonyLetters.Send` at
`Source/Intercolony/Core/IntercolonyLetters.cs:16`; it reaches
`Find.LetterStack.ReceiveLetter` at `Source/Intercolony/Core/IntercolonyLetters.cs:21`.
The target overload reaches the same API at `Source/Intercolony/Core/IntercolonyLetters.cs:35`.

The hourly world tick invokes the supply auto-ready pass at
`Source/Intercolony/Core/IntercolonyWorldComponent.cs:1631`. The chain is:

`ContractService.AdvanceAutoReady` (`Source/Intercolony/Contracts/ContractService.cs:1275`)
checks the active contract/order at `:1281`, obtains the fulfillment map at `:1297`, and
asks `SalesOrderService.CanMarkReadyNow` for the success/failure decision at
`Source/Intercolony/Contracts/ContractService.cs:1298`. On failure it sends the warning at
`Source/Intercolony/Contracts/ContractService.cs:1304` and sets the per-order notification
flag at `:1309`. The message text helper is
`Source/Intercolony/Contracts/ContractService.cs:1324`; it is a formatter, not a throttle.

On success, `AdvanceAutoReady` calls `SalesOrderService.MarkReadyForPickup` at
`Source/Intercolony/Contracts/ContractService.cs:1315`. That method starts at
`Source/Intercolony/Orders/SalesOrderService.cs:721`, changes the order to
`AwaitingCollection` at `:746-:748`, and sends the routine success letter at
`Source/Intercolony/Orders/SalesOrderService.cs:754` (label `Order ready`, positive event).
This is the direct success-letter call site that F01 must make silent for the automatic caller.
The method is shared with manual ready actions, so it is not an auto-ready-only notification
seam.

The lower-level order preflight is `CanMarkReadyNow` at
`Source/Intercolony/Orders/SalesOrderService.cs:788` and its implementation begins at `:793`.
The contract-cycle success/failure decision after the buyer order resolves is separately
`ContractService.ResolveCycle` at `Source/Intercolony/Contracts/ContractService.cs:1593`:
completed orders are recognized at `:1596`, while the failure path begins at `:1609`.

Failure remains loud through the existing branch at
`Source/Intercolony/Contracts/ContractService.cs:1301-:1309`. The one-per-reminder behavior
comes from `SalesOrder.autoReadyFailureNotified` at
`Source/Intercolony/Orders/SalesOrder.cs:150`, checked at `ContractService.cs:1302` and reset
after a successful/manual ready operation at `SalesOrderService.cs:734`.
No named helper matching a throttle was found. The searched terms were `throttl`, `notified`,
`failure.*letter`, `letter.*failure`, `AutoReadyFailure`, and `AutoReadyWait`; the throttle is
inline state plus the check/set sites above.

The success letter at `SalesOrderService.cs:754` is specifically buyer-pickup readying. The
load-bearing `CanMarkReady` property requires `BuyerPickup` at
`Source/Intercolony/Orders/SalesOrder.cs:205`, and the auto-ready code documents seller
delivery as unreachable at `Source/Intercolony/Contracts/ContractService.cs:1288`. Thus the
selling/supply auto-ready seam currently covers buyer-pickup orders, not seller-delivery ones.

### Procurement path

The world refresh invokes procurement cycle advancement at
`Source/Intercolony/Core/IntercolonyWorldComponent.cs:1746`; the loop is
`ProcurementContractService.AdvanceCycles` at
`Source/Intercolony/Procurement/ProcurementContractService.cs:833`.
For an auto-ready procurement agreement, the flag gate is at `:914`, the payment preflight is
at `:917`, and an insufficient-silver wait warning is sent at `:935`. That reminder is
throttled by `ProcurementContract.autoReadyWaitNotified` at
`Source/Intercolony/Procurement/ProcurementContract.cs:256`, checked at
`ProcurementContractService.cs:930` and set at `:945`. The wait-deadline helper is
`ProcurementContractService.AutoReadyWaitDeadline` at
`Source/Intercolony/Procurement/ProcurementContractService.cs:1075` (calculation at `:1079`);
it is not a letter-throttle helper.

The routine success branch is `TryCreateCycleOrder` returning true at
`Source/Intercolony/Procurement/ProcurementContractService.cs:968`; it resets the wait flag
at `:970`, advances the schedule at `:973`, and continues at `:975`. It calls paid-order
creation through `TryCreateCycleOrder` beginning at `:1005` and
`PurchaseOrderService.TryCreatePaidOrder` at `:1012`. There is no
`IntercolonyLetters.Send` in this per-cycle success branch or in `PurchaseOrderService`.
Therefore the per-cycle procurement success-letter call site is **NOT FOUND**: the relevant
search was all `IntercolonyLetters.Send` references in
`Source/Intercolony/Procurement/ProcurementContractService.cs` and
`Source/Intercolony/Procurement/PurchaseOrderService.cs`; the former's relevant calls are
failure/wait/terminal notifications and the latter has no letter calls.

The procurement failure call sites that remain loud are:

- supplier-default resolution at `Source/Intercolony/Procurement/ProcurementContractService.cs:887`;
- cycle-order creation failure at `:983`;
- insufficient-silver waiting warning at `:935` (a warning before the deadline, not yet a
  counted failure).

The whole-agreement success letter is separate from routine cycle success: final completion
calls `IntercolonyLetters.Send` at `Source/Intercolony/Procurement/ProcurementContractService.cs:1185`
with label `Procurement agreement completed`. Likewise, supply agreement terminal notices
are sent by `ContractService.OfferRenewal` at `Source/Intercolony/Contracts/ContractService.cs:1494`
and `:1509`. Those are downstream whole-agreement outcomes, not the routine ready/paid-cycle
success notice F01 describes. The supply due notice at `ContractService.cs:1702` is also an
order-due notice, not an auto-ready success notice.

### Commercial-history success seams

For supply/selling, marking ready is not yet sale completion. When the buyer pickup completes,
`SalesOrderService.Complete` records the completed sale at
`Source/Intercolony/Orders/SalesOrderService.cs:560` and records the commercial timeline event
at `:571`. A full pickup also emits the downstream positive `Order collected` letter at
`Source/Intercolony/Orders/SalesOrderService.cs:982`; a short pickup emits the failure/attention
letter at `:995`. These are collection outcomes shared by manual and automatic readying, not the
routine auto-ready success notice at `:754`. The durable aggregate is updated by
`IntercolonyWorldComponent.RecordCompletedSale` at
`Source/Intercolony/Core/IntercolonyWorldComponent.cs:280`, including the count and quantity at
`:304-:305`.

If F01 is intended to silence every success notification downstream of an automatic cycle, the
additional supply success seam is therefore `SalesOrderService.cs:982`; if it means the routine
auto-ready action itself, the dedicated seam is `SalesOrderService.cs:754`.

For procurement, an actually completed paid order is handled by
`PurchaseOrderService.Complete` at
`Source/Intercolony/Procurement/PurchaseOrderService.cs:659`. It updates aggregate purchase
history at `:673`, records the purchase-completed event at `:675`, and records the linked
procurement-cycle event at `:693`. The contract loop recognizes that order as a successful
cycle at `ProcurementContractService.cs:867` and increments `cyclesCompleted` at `:869`.

## F02 - Produce loop and vanilla Cancel

`ProduceLoopMapComponent` is the map component at
`Source/Intercolony/Production/ProduceLoopMapComponent.cs:11`. It is keyed by cell through
`Find(IntVec3 cell)` at `:153`; `Enable` removes/replaces the same-cell record at `:166` and
`Disable` removes it at `:184`.

The polling tick is `MapComponentTick` at `ProduceLoopMapComponent.cs:19`: it gates on a
60-tick hash interval at `:23-:26` and calls `RunPass` at `:28`. `RunPass` snapshots records
and calls `TickLoop` at `:36`; `TickLoop` starts at `:40`.

The loop inspects `thingGrid.ThingsListAt(loop.cell)` at `:52`, returns when it finds an
existing matching `Blueprint` at `:56` or `Frame` at `:61`, and otherwise tests placement at
`:104` before recreating the blueprint at `:114`. `ProduceLoopRecord` stores only cell,
rotation, thing def, stuff def, and style def at
`Source/Intercolony/Production/ProduceLoopRecord.cs:7-:11`; it stores no blueprint identity,
cancel marker, or last-seen state.

The vanilla player path is:

- `Designator_Cancel.DesignateThing` is the concrete designator method at
  `reference/decompiled/RimWorld/Designator_Cancel.cs:98`;
- for a `Frame` or `Blueprint`, it calls `t.Destroy(DestroyMode.Cancel)` at
  `reference/decompiled/RimWorld/Designator_Cancel.cs:102`;
- the virtual base method is `Thing.Destroy` at
  `reference/decompiled/Verse/Thing.cs:1043`, which despawns at `:1063`;
- a build blueprint's cancel-specific despawn handling is
  `reference/decompiled/RimWorld/Blueprint_Build.cs:165`, checks `DestroyMode.Cancel` at
  `:168`, and calls the base despawn at `:175`.

`Blueprint.Destroy(DestroyMode.Cancel)` was **NOT FOUND** as an override in the local vanilla
decompilation; the searched `reference/decompiled/RimWorld/Blueprint*.cs` and
`reference/decompiled/Verse/Thing.cs` for `Destroy` showed the `Thing.Destroy` path above.

The most specific cancel-observation seam is therefore a Harmony patch around
`Designator_Cancel.DesignateThing` at `Designator_Cancel.cs:98-:103`, where the canceled target
and its cell are available. A lower, broader seam is `Thing.Destroy` at `Thing.cs:1043`,
filtered to `DestroyMode.Cancel` and a blueprint/frame. Either seam could call the existing
component operation at `ProduceLoopMapComponent.cs:184`; there is no current cancel patch in
the mod's Harmony registration (`Source/Intercolony/Compatibility/HarmonyPatches.cs:14-:21`).

The existing poll **CANNOT distinguish** cancellation from "no blueprint/frame exists yet."
Both states reach the same `ThingsListAt`/placement logic, and the record has no state with
which to distinguish them. An obstruction after cancellation could make `CanPlaceBlueprintAt`
fail for an environmental reason, but that is not a reliable semantic cancel signal. A
Harmony/explicit cancel observation is needed if cancel must terminate the loop.

## F13 - auto-renew visible on the employee card

The employee page calls the one-row renderer at
`Source/Intercolony/UI/MainTabWindow_Intercolony_Labor.cs:262` with a row rect; row height is
`52f` at `:28`. The renderer is
`DrawEmployeeRow(Rect rect, EmploymentContract contract, int index)` at
`Source/Intercolony/UI/MainTabWindow_Intercolony_Labor.cs:740`.

`EmployeeRowLayout.For` starts at `:722`. Its current rects are:

- `leftAction`: `rect.xMax - ActionWidth * 2f - 8f`, `rect.y + 11f`, width `110f`, height
  `30f`, at `:724`;
- `contractActions` (the `...` menu): immediately left of `leftAction`, width `28f`, at
  `:727`;
- `rightAction`: `rect.xMax - ActionWidth - 4f`, `rect.y + 11f`, width `110f`, height `30f`,
  at `:728`;
- `textWidth`: from the row's left edge to the menu with a 6-pixel gap, at `:735`.

In current draw order, the row does the following:

1. Draws the row background/highlight at `:742-:747`.
2. Draws the employee name and skills in the top-left text rect
   `new Rect(rect.x + 6f, rect.y + 3f, textWidth, 22f)` at `:752-:753`.
3. Draws the current contract clause right-aligned in the same top band, using
   `new Rect(rect.x + 6f, rect.y + 3f, textWidth - 6f, 22f)` at `:763-:766`.
4. Draws settlement/faction, daily wage, term/wage structure, paid state, and status in the
   second-line rect `new Rect(rect.x + 6f, rect.y + 25f, textWidth, 22f)` at `:769-:773`.
5. Registers the full-row tooltip/click region at `:776-:788`.
6. Draws the `...` contract-actions button in `layout.contractActions` at `:803-:804`, builds
   its menu at `:806`, and owns the auto-renew toggle at `:825-:830`: label
   `Auto-renew: on/off` at `:828`, mutation of `contract.autoRenew` at `:829`, and menu
   insertion at `:832-:835`.
7. Draws conditional action buttons in `layout.leftAction` and `layout.rightAction`: Pay at
   `:838-:850`; `leaving` at `:852-:861`; transition Keep them/Not now at `:864-:880`;
   renewal Renew/Let go at `:883-:900`; and normal Dismiss/Cancel at `:905-:909`.

The state field is `EmploymentContract.autoRenew` at
`Source/Intercolony/Labor/EmploymentContract.cs:233` (Scribe load/save at `:468`). There is
currently no visible auto-renew state in the row's labels; the existing interaction is only
the `...` menu described above.

## F15 - default auto-ready ON for new agreements

### Gameplay construction sites

The two supply/selling agreement object construction sites are:

- explicit/player-proposed agreements: `ContractService.BuildExplicitContract` returns
  `new RecurringContract` at `Source/Intercolony/Contracts/ContractService.cs:1009`;
- settlement-offered agreements: `ContractService.BuildContract` returns
  `new RecurringContract` at `Source/Intercolony/Contracts/ContractService.cs:1041`.

The procurement agreement construction site is `ProcurementContractService.ProposeContract`,
where `new ProcurementContract` occurs at
`Source/Intercolony/Procurement/ProcurementContractService.cs:261`; it is registered at `:296`.
Acceptance/counter acceptance mutates the existing record through `ApplyAcceptedTerms` at
`ProcurementContractService.cs:730`, rather than constructing another agreement.

The repo-wide search for exact `new RecurringContract` / `new ProcurementContract` also found
debug-only fixtures, not player/runtime construction sites:

- `Source/Intercolony/Debug/IntercolonyCashFlowSelfTest.cs:114`, `:159`, `:327`, `:338`,
  `:378`, `:389`;
- `Source/Intercolony/Debug/IntercolonyContractSelfTest.cs:87`, `:1073`;
- `Source/Intercolony/Debug/IntercolonyLedgerSelfTest.cs:286`;
- `Source/Intercolony/Debug/IntercolonyLongTermSelfTest.cs:761`, `:1444`, `:1558`;
- `Source/Intercolony/Debug/IntercolonyTimelineSelfTest.cs:390`, `:706`, `:716`, `:1360`,
  `:1371`, `:1685`, `:1695`;
- `Source/Intercolony/Debug/IntercolonyRfqSelfTest.cs:3192`, `:3280`, `:3358`, `:3364`,
  `:3370`, `:3376`, `:3426`, `:3433`, `:3515`, `:3583`, `:4593`, `:7399`, `:7503`,
  `:8018`, `:8505`, `:8678`.

### Fields and Scribe behavior

Both agreement types use the same field name, `autoReadyOrders`:

- supply/selling: `Source/Intercolony/Contracts/RecurringContract.cs:134`;
- procurement: `Source/Intercolony/Procurement/ProcurementContract.cs:251`.

Neither declaration has an initializer, so a newly constructed C# object currently starts
false. The Scribe sites explicitly use false as the missing-value default:

- supply/selling: `RecurringContract.ExposeData` at
  `Source/Intercolony/Contracts/RecurringContract.cs:295`;
- procurement: `ProcurementContract.ExposeData` at
  `Source/Intercolony/Procurement/ProcurementContract.cs:517`.

On load, `Scribe_Values.Look` passes the supplied default to
`ScribeExtractor.ValueFromNode` at `reference/decompiled/Verse/Scribe_Values.cs:79-:82`.
When the XML node is absent, `ValueFromNode` returns that default at
`reference/decompiled/Verse/ScribeExtractor.cs:14-:18`. Therefore an OLD save that predates
`autoReadyOrders` loads `false` for both agreement types.

Consequently, flipping only the C# declaration default from false to true would make newly
constructed agreements default ON, but would **not** silently flip old saved agreements: the
existing Scribe calls would assign false when the field is missing. Changing the Scribe
default to true, or adding a migration that does so, would change old-save behavior.

## Build and self-test report

The single permitted build was run from the repository root:

`dotnet build Source/Intercolony/Intercolony.csproj`

It exited `0` and reported `Build succeeded`, with 0 errors. The only warning was `NU1900`
about being unable to load NuGet's vulnerability-data service index at
`https://api.nuget.org/v3/index.json`; it appeared as restore/network metadata output, not as
a compiler warning. No new source warning was observed.

The in-game self-test registry is `Source/Intercolony/Debug/IntercolonyAllSelfTests.cs:125`.
The bridge exposes `tests.list` at
`Source/Intercolony/Debug/Bridge/IntercolonyDevBridgeHost.cs:375-:376` and enumerates that
registry at `:399-:409`. The agreement test ID is `long-term`, registered at
`IntercolonyAllSelfTests.cs:173-:174` and backed by agreement checks including auto-ready at
`Source/Intercolony/Debug/IntercolonyLongTermSelfTest.cs:113-:115`. The Produce test ID is
`produce`, registered at `IntercolonyAllSelfTests.cs:175-:176`; its implementation is
`Source/Intercolony/Debug/IntercolonyProduceSelfTest.cs:56`.

## Prompt/codebase discrepancies and search negatives

- The working tree was not clean when the build ran. `git status --short --branch` showed
  pre-existing untracked `FOREMAN.md`, `Playtesting annotations.docx`, and
  `docs/PLAYTEST_BATCH_SOURCE_PLAN.md`. The build result is for the current working tree, not
  a clean checkout.
- The prompt's "throttling helper" is not a helper method in this code. The behavior is inline
  `autoReadyFailureNotified` / `autoReadyWaitNotified` state; only the failure-text and
  deadline helpers exist.
- There is no procurement per-cycle auto-ready success letter to suppress. The success branch
  creates the paid order and advances the schedule without a letter; only the terminal
  `Procurement agreement completed` notice at `ProcurementContractService.cs:1185` is a success
  letter on that broader path.
- The vanilla cancel path is not a `Blueprint.Destroy` override. It is the designator calling
  virtual `Thing.Destroy(DestroyMode.Cancel)`, with `Blueprint_Build.DeSpawn` handling the
  cancel-specific despawn work.
- The F15 old-save concern is reversed for the current Scribe code: changing only the C# field
  default does not flip missing fields in old saves because both Scribe defaults are explicitly
  false.
- Seller-delivery supply agreements exist, but the current ready gate requires buyer pickup;
  the auto-ready success path does not currently cover seller delivery.
