# Stage 2 seam map — F03 and F04

Read-only reconnaissance for the area-level Produce / Pause / Stop request (F03) and the
programmable Produce request (F04). The findings below are based on the current source and the
checked-in RimWorld 1.6 decompilation. The body is evidence; design opinions are isolated in the
final section.

## A. Read — the current Produce loop, end to end

### Owner, creation, storage, and persistence

`ProduceLoopMapComponent` is the map-owned state holder. It derives from `MapComponent` at
`Source/Intercolony/Production/ProduceLoopMapComponent.cs:11`, and its private loop set is
`List<ProduceLoopRecord> loops` at `Source/Intercolony/Production/ProduceLoopMapComponent.cs:13`.
The lookup used by the rest of the mod is `ProduceLoopMapComponent.For(Map)`, which calls
`map.GetComponent<ProduceLoopMapComponent>()`, at
`Source/Intercolony/Production/ProduceLoopMapComponent.cs:143-145`.

Vanilla fills a map with every non-`CustomMapComponent` subclass of `MapComponent` by enumerating
`typeof(MapComponent).AllSubclassesNonAbstract()`, constructing each with the map, and adding it
to `components` at `reference/decompiled/Verse/Map.cs:710-727`. `Map.GetComponent<T>()` returns the
matching component from that list at `reference/decompiled/Verse/Map.cs:1205-1215`. There is no
separate Intercolony registration call for this component in the checked-in source; its subclass
construction is supplied by that vanilla component-fill path.

`ProduceLoopRecord` is an `IExposable` at
`Source/Intercolony/Production/ProduceLoopRecord.cs:5`. Its complete persisted state is five
fields: `cell`, `rotation`, `thingDef`, `stuffDef`, and `styleDef`, at
`Source/Intercolony/Production/ProduceLoopRecord.cs:7-11`. The five fields are written directly in
`ExposeData()` at `Source/Intercolony/Production/ProduceLoopRecord.cs:13-20`; there is currently no
mode, pause/stop state, target count, quality constraint, worker restriction, or pawn reference in
the record.

The map component's own comment says its state is saved in the map save block and is independent of
the world-level Intercolony schema/migration ladder at
`Source/Intercolony/Production/ProduceLoopMapComponent.cs:7-9`. The list is deep-scribed under the
key `loops` at `Source/Intercolony/Production/ProduceLoopMapComponent.cs:194-198`; a null loaded
list is replaced with a new empty list during `PostLoadInit` at
`Source/Intercolony/Production/ProduceLoopMapComponent.cs:199-202`.

### Loop creation

The only runtime creation/replacement method is `ProduceLoopMapComponent.Enable` at
`Source/Intercolony/Production/ProduceLoopMapComponent.cs:166-182`. It first removes every old
record at the same cell at `:173`, then adds a new `ProduceLoopRecord` containing the five current
fields at `:174-181`. `Find(IntVec3)` is a linear cell-keyed lookup at
`Source/Intercolony/Production/ProduceLoopMapComponent.cs:153-164`; `IsEnabled` means only
“a record exists at this cell” at `Source/Intercolony/Production/ProduceLoopMapComponent.cs:148-151`.

### Polling pass

`MapComponentTick()` runs the pass only on `map.IsHashIntervalTick(60)` at
`Source/Intercolony/Production/ProduceLoopMapComponent.cs:19-29`. The vanilla map tick dispatcher
calls each map component's `MapComponentTick()` at
`reference/decompiled/Verse/MapComponentUtility.cs:24-37`.

`RunPass()` snapshots the current list and calls `TickLoop` once for every snapshot record at
`Source/Intercolony/Production/ProduceLoopMapComponent.cs:31-38`. This snapshot is the boundary
between a pass and mutations caused by that pass.

`TickLoop` currently behaves as follows:

1. A null `thingDef`, out-of-bounds cell, or non-minifiable definition immediately calls
   `Disable(loop.cell)` at `Source/Intercolony/Production/ProduceLoopMapComponent.cs:40-45`.
2. It reads every `Thing` at the loop cell at `Source/Intercolony/Production/ProduceLoopMapComponent.cs:52-55`.
   A matching `Blueprint` causes an early return at `:56-59`; a matching `Frame` causes an early
   return at `:61-65`. Thus existing blueprint/frame work blocks duplicate blueprint placement.
3. It then looks for a finished `Building` whose `def` equals the recorded `thingDef` at
   `Source/Intercolony/Production/ProduceLoopMapComponent.cs:67-75`.
4. For that finished building, an existing `Uninstall` or `Deconstruct` designation, or failed
   vanilla uninstall eligibility, causes an early return at
   `Source/Intercolony/Production/ProduceLoopMapComponent.cs:77-84`. The eligibility predicate is
   the local `PassesVanillaUninstallEligibility` method at
   `Source/Intercolony/Production/ProduceLoopMapComponent.cs:124-141`.
5. Otherwise it claims the building for the player if needed at
   `Source/Intercolony/Production/ProduceLoopMapComponent.cs:86-89`, immediately uninstalls
   zero-work/frame cases at `:91-94`, or adds a vanilla `Uninstall` designation at `:95-99`.
   The method then returns at `:101`, so blueprint placement is a later pass.
6. If there is no current matching building, it asks
   `GenConstruct.CanPlaceBlueprintAt` with the recorded definition, cell, rotation, and stuff at
   `Source/Intercolony/Production/ProduceLoopMapComponent.cs:104-111`.
7. If placement is accepted, it calls `GenConstruct.PlaceBlueprintForBuild` with the recorded
   definition, cell, rotation, player faction, stuff, and style at
   `Source/Intercolony/Production/ProduceLoopMapComponent.cs:114-121`.

The vanilla placement call creates a `Blueprint_Build`, sets its faction, stuff, and style, spawns
it, and returns it; it does not perform construction work, at
`reference/decompiled/RimWorld/GenConstruct.cs:167-179`.

### Every loop-set mutation found

The runtime mutation points are:

- `Enable` removes same-cell records and adds a new record at
  `Source/Intercolony/Production/ProduceLoopMapComponent.cs:166-182`.
- `Disable` removes records outright with `loops.RemoveAll(...)` at
  `Source/Intercolony/Production/ProduceLoopMapComponent.cs:184-187`.
- The polling pass reaches that destructive `Disable` operation for malformed/out-of-bounds/non-
  minifiable records at `Source/Intercolony/Production/ProduceLoopMapComponent.cs:42-45`.
- The production gizmo calls `Disable` or `Enable` from its toggle action at
  `Source/Intercolony/Production/ProduceGizmoPatch.cs:121-132`.
- The vanilla Cancel Harmony prefix calls `component.Disable(loop.cell)` when it sees a matching
  blueprint/frame at `Source/Intercolony/Compatibility/HarmonyPatches.cs:172-196`.
- Save/load replaces or initializes the list through `Scribe_Collections.Look` and the null-list
  fallback at `Source/Intercolony/Production/ProduceLoopMapComponent.cs:194-202`.

No other production runtime caller of `Enable` or `Disable` was found. The exact source search was:

```text
rg -n --glob '*.cs' "\.Enable\(|\.Disable\(|ProduceLoopMapComponent|ProduceLoopRecord|\.Loops|IsEnabled\(|RunPass\(|TickLoop\(" Source
```

The self-test is a separate, test-only mutation surface. It deliberately constructs a detached
component so player loops are not ticked or replaced, as documented at
`Source/Intercolony/Debug/IntercolonyProduceSelfTest.cs:11-14` and constructed at `:67`. Its
`Enable` calls are at `:148`, `:209`, `:255`, `:282`, `:414-419`, `:472-477`, `:612`, `:637-642`,
and `:660-665`. Its `Disable` calls are at `:219`, `:343`, `:444`, `:535`, `:709-710`, and
`:1211`. The test also mutates a loaded record's `thingDef` directly at
`Source/Intercolony/Debug/IntercolonyProduceSelfTest.cs:643-650` and temporarily mutates
`record.rotation` at `:318-337`; those are record-fixture mutations, not additional production
loop-set APIs.

## B. Read — current player start/stop UI

### Gizmo: the only dedicated Produce entry point

The mod patches `Thing.GetGizmos` at
`Source/Intercolony/Production/ProduceGizmoPatch.cs:12-18`. Its `CreateProduceGizmo(Thing)` helper
rejects anything that is not a spawned, mapped, player-faction thing at
`Source/Intercolony/Production/ProduceGizmoPatch.cs:42-47`; it then supports a matching
`Frame`, build `Blueprint`, or `Building` at `:56-105`.

The command captures one `Thing`'s map, position, and rotation at
`Source/Intercolony/Production/ProduceGizmoPatch.cs:49-51`. It returns one `Command_Toggle` with
label `Produce` at `Source/Intercolony/Production/ProduceGizmoPatch.cs:108-120`. Its active state is
`loopComponent.IsEnabled(cell)` at `:121`, and its action calls `Disable(cell)` when active or
`Enable(cell, rotation, thingDef, stuffDef, styleDef)` when inactive at `:122-132`.

This is a per-object/per-cell path. It does not receive an area, a collection of selected things, or
a drag-cell sequence; its closure is bound to the one position captured from the one `Thing` at
`Source/Intercolony/Production/ProduceGizmoPatch.cs:49-51` and `:121-130`. The stable
`groupKeyIgnoreContent` value at `:15-16` and `:114-120` is only the command's grouping metadata;
the action still reads and mutates one cell.

The current toggle's description explicitly describes the existing semantics: work already under
way is allowed to finish and turning the toggle off stops the next repetition, at
`Source/Intercolony/Production/ProduceGizmoPatch.cs:114-118`. It is therefore closer to the current
Stop behavior than to a resumable Pause state.

### Designator: only a narrow Cancel side effect

The mod does not define a Produce designator. It does patch the vanilla
`Designator_Cancel.DesignateThing` method at
`Source/Intercolony/Compatibility/HarmonyPatches.cs:165-175`. The prefix ignores anything that is
not a `Blueprint` or `Frame` at `:177-181`, finds the loop by the canceled thing's position at
`:184-191`, and removes the matching record at `:196` before vanilla cancellation proceeds.

This is not a three-command Produce UI. It is a per-thing observation hook for a cancellation
operation already being performed by vanilla. Vanilla `Designator_Cancel.DesignateSingleCell`
iterates the cell's things and calls `DesignateThing` for accepted things at
`reference/decompiled/RimWorld/Designator_Cancel.cs:48-69`; the base class's drag implementation
calls `DesignateSingleCell` for each accepted drag cell at
`reference/decompiled/Verse/Designator.cs:261-286`. Consequently, dragging vanilla Cancel can
indirectly remove matching Produce records for blueprint/frame targets in the dragged area, but
only through this narrow matching-target prefix. It is not an area-level Produce / Pause / Stop
path, and it does not uniformly stop a record whose current target is a finished building or an
uninstall designation because the prefix's first guard requires `Blueprint` or `Frame` at
`Source/Intercolony/Compatibility/HarmonyPatches.cs:177-191`.

### Float menu, main tab, and multi-select status

**NOT FOUND:** no Produce-specific float-menu entry was found. The production directory has no
`GetFloatMenuOptions` or `FloatMenuOption` reference. Exact search:

```text
rg -n -i "GetFloatMenuOptions|FloatMenuOption" Source/Intercolony/Production
```

**NOT FOUND:** no Produce-specific main-tab entry was found. The mod's main tab class is
`MainTabWindow_Intercolony` at `Source/Intercolony/UI/MainTabWindow_Intercolony.cs:19`, but the
Produce-specific searches below returned no match:

```text
rg -n -i "Produce.*MainTab|MainTab.*Produce|Produce.*Dialog|Dialog.*Produce" Source/Intercolony
rg -n -i "produce|loop" Source/Intercolony/UI/MainTabWindow_Intercolony.cs Source/Intercolony/UI/MainTabWindow_Intercolony_Labor.cs Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs
```

**NOT FOUND:** no dedicated Produce designator, area-order class, or explicit multi-select Produce
path exists in the mod source. Exact searches:

```text
rg -n -i "class .*Designator|new .*Designator|Designator_.*Produce|Produce.*Designator" Source/Intercolony
rg -n -i "DesignateMultiCell|DesignateSingleCell|CanDesignateCell|CanDesignateThing|DesignateThing" Source/Intercolony/Production
rg -n -i "MultiSelect|SelectedThings|area order|area" Source/Intercolony/Production
```

Therefore the current dedicated start path is the per-object gizmo; the current dedicated stop path
is that gizmo's `Disable`, with the separate narrow Cancel observation described above. No current
area-level Produce / Pause / Stop command exists.

## C. Read — record state required by F03

### Pause and Stop are not the same state transition

The current record has no status field, only the five fields listed at
`Source/Intercolony/Production/ProduceLoopRecord.cs:7-11`, and `Disable` deletes the record at
`Source/Intercolony/Production/ProduceLoopMapComponent.cs:184-187`.

| F03 operation | What the current data can express | Required state behavior |
|---|---|---|
| Pause | Not expressible by current `Disable`: deleting the record loses the loop's definition, so there is nothing for Resume to find (`Source/Intercolony/Production/ProduceLoopMapComponent.cs:184-187`). | Keep the record and make the polling pass skip new uninstall/blueprint actions while existing vanilla work can finish (`Source/Intercolony/Production/ProduceLoopMapComponent.cs:52-121`; `Source/Intercolony/Debug/IntercolonyProduceSelfTest.cs:201-222`). |
| Resume | No current API or record state exists beyond record lookup/presence (`Source/Intercolony/Production/ProduceLoopMapComponent.cs:148-164`). | Re-enable polling for the retained record (`Source/Intercolony/Production/ProduceLoopMapComponent.cs:40-121`). |
| Stop | Expressible by current `Disable`: remove the record permanently (`Source/Intercolony/Production/ProduceLoopMapComponent.cs:184-187`). | Do not retain a resumable record; an existing vanilla designation/job may finish, but no later pass can create a replacement because the record is gone (`Source/Intercolony/Debug/IntercolonyProduceSelfTest.cs:201-222` and `:341-348`). |

Pause needs something Stop does not: durable retained state distinguishing “temporarily inactive”
from “no program exists.” The smallest record-local name would be `bool paused` on
`ProduceLoopRecord`, with a corresponding `Scribe_Values.Look(ref paused, "paused", false)` in
`ProduceLoopRecord.ExposeData()` alongside the existing field calls at
`Source/Intercolony/Production/ProduceLoopRecord.cs:13-20`. The polling guard would attach after
the existing invalid-record cleanup at `Source/Intercolony/Production/ProduceLoopMapComponent.cs:42-45`
and before the current thing scan and finished-building branch at `:52-101`; otherwise a paused
installed building could still enter the uninstall/rebuild path. This preserves cleanup of malformed
records while suspending new loop actions.
Resume would clear that field without removing the record. Stop would instead continue to use the
destructive `Disable` path.

The current self-test establishes the intended non-destructive part of Stop's behavior: it calls
`Disable` after an Uninstall designation has been created and asserts that the designation remains
at `Source/Intercolony/Debug/IntercolonyProduceSelfTest.cs:201-222`; it separately removes the loop,
destroys the current blueprint/frame, runs another pass, and asserts that no next blueprint appears
at `:341-348`. This is evidence that record removal does not cancel in-progress vanilla work and
does prevent the next repetition.

Because `IsEnabled` currently means only “`Find(cell)` returned a record” at
`Source/Intercolony/Production/ProduceLoopMapComponent.cs:148-164`, a paused record would still
appear active to the existing gizmo unless the F03 work adds a distinct paused-state readout or
replaces the current toggle with explicit Produce / Pause / Stop commands.

### Save-schema answer

The named Intercolony schema constant is `IntercolonyWorldComponent.CurrentSaveVersion = 57` at
`Source/Intercolony/Core/IntercolonyWorldComponent.cs:25-30`. That class persists its own
`saveVersion` and other world-level fields at
`Source/Intercolony/Core/IntercolonyWorldComponent.cs:1161-1179`, and its comment says to bump the
constant and add a migration whenever the saved shape of that world-level state changes at
`Source/Intercolony/Core/IntercolonyWorldComponent.cs:25-30`.

For a new field on `ProduceLoopRecord`, the current wiring says **do not bump that world-level
constant solely for this map-record field**: the Produce component explicitly says it is independent
of the world schema at `Source/Intercolony/Production/ProduceLoopMapComponent.cs:7-9`, and the
record is deep-scribed inside the map component at `:194-198`. Existing fields on this exact record
are handled by adding direct `Scribe_Values` / `Scribe_Defs` calls, with no version branch or
migration code, at `Source/Intercolony/Production/ProduceLoopRecord.cs:13-20`. No separate Produce
map-schema constant or migration ladder was found:

```text
rg -n -i "CurrentSaveVersion|saveVersion|schema|migration" Source/Intercolony/Production
```

The resulting implementation seam is therefore a new persisted record field plus its Scribe call,
not a new step in `IntercolonyWorldComponent`'s world migration ladder.

## D. Read — vanilla area-designator precedent

### Base class and method relationship

The vanilla base type is `Verse.Designator`: the file declares namespace `Verse` and
`public abstract class Designator : Command` at `reference/decompiled/Verse/Designator.cs:8-10`.
`Designator_Cancel` derives directly from it at
`reference/decompiled/RimWorld/Designator_Cancel.cs:6-8`.

The base API is split as follows:

- `CanDesignateThing(Thing)` is virtual and rejects by default at
  `reference/decompiled/Verse/Designator.cs:249-252`.
- `DesignateThing(Thing)` is virtual and throws by default at
  `reference/decompiled/Verse/Designator.cs:254-257`.
- `CanDesignateCell(IntVec3)` is abstract, so every concrete designator must implement it, at
  `reference/decompiled/Verse/Designator.cs:259`.
- `DesignateMultiCell(IEnumerable<IntVec3>)` is virtual. Its inherited implementation checks
  `CanDesignateCell` for each dragged cell, calls `DesignateSingleCell` for accepted cells, and
  finalizes once at `reference/decompiled/Verse/Designator.cs:261-286`.
- `DesignateSingleCell(IntVec3)` is virtual and throws by default at
  `reference/decompiled/Verse/Designator.cs:288-290`.

The designator manager calls `DesignateSingleCell` for a single accepted click at
`reference/decompiled/Verse/DesignatorManager.cs:93-108`, and calls
`DesignateMultiCell(dragger.DragCells)` when a drag ends at
`reference/decompiled/Verse/DesignatorManager.cs:124-129`.

`Designator_Cancel` shows the existing-thing pattern: its `CanDesignateCell` checks bounds,
designations, and things at that cell at
`reference/decompiled/RimWorld/Designator_Cancel.cs:27-45`; its `DesignateSingleCell` removes
cancelable designations and calls `DesignateThing` for accepted things at `:48-69`; its
`CanDesignateThing` accepts cancelable targets, frames, and blueprints at `:71-96`; and its
`DesignateThing` destroys a frame/blueprint or removes designations at `:98-126`.

`Designator_Uninstall` is the closer behavior precedent for a building action: it derives from
`Designator` and implements a cell test plus cell-to-thing delegation at
`reference/decompiled/RimWorld/Designator_Uninstall.cs:7-9` and `:25-45`, then performs the
per-thing uninstall at `:61-77` and validates building targets at `:79-113`.

### What a new Intercolony designator must implement

A new F03 area command would derive from `Verse.Designator`. The language-level required override
is `CanDesignateCell`, because it is abstract at
`reference/decompiled/Verse/Designator.cs:259`. To make drag-over-area behavior perform work, it
also needs a concrete `DesignateSingleCell`; the base multi-cell method will then reuse that method
for every accepted cell at `reference/decompiled/Verse/Designator.cs:261-290`.

If the command is also meant to be invoked directly on an existing selected thing or through
reverse-designator gizmos, it must provide matching `CanDesignateThing` and `DesignateThing`
implementations, because those are the virtual thing-level hooks at
`reference/decompiled/Verse/Designator.cs:249-257`. It does not need to override
`DesignateMultiCell` for ordinary cell-by-cell area semantics; overriding it would be the seam for
batch-specific deduplication or one-shot behavior rather than a requirement imposed by the base
class. The current mod has no such class; see the NOT FOUND searches in section B.

## E. Read — F04 per-object configuration UI seam

There is an existing gizmo surface on a produce-looped building: the `Thing.GetGizmos` postfix
creates the `Command_Toggle` at
`Source/Intercolony/Production/ProduceGizmoPatch.cs:12-18` and `:108-132`. There is no
Produce-specific right-click float-menu surface in the production directory; the exact search is
listed in section B.

The closest existing mod dialog pattern for configuring one existing object is
`Dialog_Counteroffer`. It stores a target `MarketOpportunity` in a readonly field at
`Source/Intercolony/UI/Dialog_Counteroffer.cs:30-42`, receives that target in its constructor and
initializes local editable values from it at `:44-61`, draws editors for the target's permitted
terms at `:194-220` and `:236-264`, and submits the edited target through the service at
`Source/Intercolony/UI/Dialog_Counteroffer.cs:342-379`. It is not attached to a building or a
Produce record, but it is the existing “open a modal for one persisted object, edit local controls,
validate/commit through a service” pattern.

`Dialog_CreateJobPosting` is a second, less direct form pattern: it holds dialog-local skill and
minimum-level values at `Source/Intercolony/UI/Dialog_CreateJobPosting.cs:93-101` and accepts an
`onConfirm` callback at `:113-129`. It creates a new posting rather than configuring an existing
produce-looped object, so `Dialog_Counteroffer` is the closer per-object precedent.

## F. Read — F04 mode collisions and missing concepts

### Target-count observation

The current Produce poll has no target-count branch: after the finished-building logic it goes
straight to `CanPlaceBlueprintAt` at
`Source/Intercolony/Production/ProduceLoopMapComponent.cs:77-111`, then places the replacement
blueprint at `:114-121`. The current record also has no count or mode field at
`Source/Intercolony/Production/ProduceLoopRecord.cs:7-20`.

The mod does have stock/inventory read models:

- `FindKnownInventory(Map, ThingDef)` scans `map.listerThings.AllThings`, filters with
  `OrderValidator.IsAvailableColonyStock`, unwraps minified things, and returns the first matching
  def at `Source/Intercolony/Market/FindBuyerService.cs:611-633`.
- `FindBuyerService.ColonyStock(Map)` aggregates a `ThingDef -> count` result at
  `Source/Intercolony/Market/FindBuyerService.cs:712-767`. It reads storage slot groups at
  `:725-736`, unwraps minified things and filters them through
  `IntercolonyProductClassifier.IsFungibleTradeItem` at `:743-747`, then counts units through
  `OrderValidator.CountableUnits` at `:749-756`.
- `AvailableColonyStock` subtracts quantities already committed to orders at
  `Source/Intercolony/Market/FindBuyerService.cs:769-788`.

These are existing stock read models, not a Produce-loop completion counter: `ColonyStock` is
storage-oriented, filters to the mod's fungible trade-item classifier, and does not count every
map thing. A target count for produced buildings therefore has no existing general-purpose mod
counter in the Produce path; it would need an explicit observation rule at the pass's decision
seam.

Vanilla's bill system supplies a confirmed target-count precedent, but it is attached to bills and
recipes rather than this map loop. `Bill_Production` stores repeat mode, repeat count, target count,
pause-when-satisfied, quality range, and related fields at
`reference/decompiled/RimWorld/Bill_Production.cs:10-40`; its `ExposeData()` persists those values
at `:113-147`. Its `ShouldDoNow()` calls `recipe.WorkerCounter.CountProducts(this)` and applies
target/pause logic at `reference/decompiled/RimWorld/Bill_Production.cs:207-243`.
`RecipeWorkerCounter.CountProducts` counts resource-counter values or walks map things, minified
things, carried items, haul sources, and optional pawn equipment/inventory at
`reference/decompiled/Verse/RecipeWorkerCounter.cs:19-91`; its product filter includes hit points,
`CompQuality`, allowed stuff, and fog at `:120-148`.

The loop is not currently a bill: `Bill` owns an unsaved `BillStack` reference at
`reference/decompiled/RimWorld/Bill.cs:10-13`, `BillStack` owns an `IBillGiver` at
`reference/decompiled/RimWorld/BillStack.cs:8-13`, while a build blueprint derives from
`ThingWithComps` and implements `IConstructible` at
`reference/decompiled/RimWorld/Blueprint.cs:7-10`. No Produce record or blueprint is connected to
that bill stack in the inspected source.

### Quality

There is no quality or pawn field on `ProduceLoopRecord`; its complete field list is only at
`Source/Intercolony/Production/ProduceLoopRecord.cs:7-20`. The loop places a blueprint by passing
definition, cell, rotation, faction, stuff, and style, with no pawn or quality argument, at
`Source/Intercolony/Production/ProduceLoopMapComponent.cs:114-121`.

The actual quality assignment happens later in vanilla when a frame completes. `Frame.CompleteConstruction(Pawn worker)` creates the built `Thing`, obtains `CompQuality`, and calls
`QualityUtility.GenerateQualityCreatedByPawn(worker, SkillDefOf.Construction)` before setting the
quality at `reference/decompiled/RimWorld/Frame.cs:262-296`. Thus a minimum-quality mode would
collide with a result that is determined by the eventual construction worker, not by the current
Produce record or blueprint-placement call.

Vanilla bill UI conditionally exposes quality controls only when the produced thing has
`CompQuality` at `reference/decompiled/RimWorld/Dialog_BillConfig.cs:184-193`; that conditional
pattern is confirmed for bills but does not exist in the mod's Produce UI.

### Worker eligibility, skill controls, and multiple workers

The current Produce record has no pawn, worker, skill, or eligibility concept, and the placement
call does not choose a pawn, at `Source/Intercolony/Production/ProduceLoopRecord.cs:7-20` and
`Source/Intercolony/Production/ProduceLoopMapComponent.cs:104-121`.

After a blueprint/frame exists, vanilla owns construction eligibility. The construction workgiver
requests `BuildingFrame` things at
`reference/decompiled/RimWorld/WorkGiver_ConstructFinishFrames.cs:6-10`, checks the target and
calls `GenConstruct.CanConstruct(frame, pawn, checkSkills: true, forced)` at `:17-43`, and returns
the `FinishFrame` job at `:43`. `GenConstruct.CanConstruct` checks active work/reservation/reach,
burning, Construction skill, Artistic skill, and mech fixed skill levels at
`reference/decompiled/RimWorld/GenConstruct.cs:251-329`.

The finish-frame driver reserves the target with `maxPawns = 1` at
`reference/decompiled/RimWorld/JobDriver_ConstructFinishFrame.cs:18-21`. Its single build actor
learns Construction, advances `frame.workDone` from that actor's `ConstructionSpeed`, and calls
`frame.CompleteConstruction(actor)` at
`reference/decompiled/RimWorld/JobDriver_ConstructFinishFrame.cs:44-78`.
The current construction path therefore has no mod-owned worker selection and no multi-worker
reservation for one frame; a F04 “more than one worker” mode would collide with this one-reserver,
one-actor job path unless it introduces a new construction-job model or a compatible vanilla hook.

Vanilla bill worker controls are real and confirmed, but are not connected to Produce: `Bill` has
`suspended`, `allowedSkillRange`, pawn restriction, and worker-category fields at
`reference/decompiled/RimWorld/Bill.cs:29-43`, persists them at `:205-220`, and checks the
restricted pawn/category and skill range at `:234-272`. `Dialog_BillConfig` exposes the worker
dropdown and allowed skill range at `reference/decompiled/RimWorld/Dialog_BillConfig.cs:248-254`
and its suspension button at `:259-273`. Reusing the idea would require an explicit bridge from a
Produce record to the construction job; no such bridge is present.

## G. Read — self-test surface

The self-test registration id is `produce`, mapped to
`IntercolonyProduceSelfTest.Run`, at
`Source/Intercolony/Debug/IntercolonyAllSelfTests.cs:175-176`. The debug action is labeled “Run
produce self-test” and invokes the same runner at
`Source/Intercolony/Debug/IntercolonyDebugActions.cs:1914-1918`. The test class and its output
header are at `Source/Intercolony/Debug/IntercolonyProduceSelfTest.cs:15` and `:56-59`.

The existing `Check*` methods are:

- `Results.Check` at `Source/Intercolony/Debug/IntercolonyProduceSelfTest.cs:23-35`;
- `CheckFinishedBuilding` at `:120-180`;
- `CheckDisablePreservesWork` at `:182-223`;
- `CheckDeconstructGuard` at `:225-260`;
- `CheckBlueprintPlacement` at `:262-349`;
- `CheckDesignatorCancel` at `:351-582`;
- `CheckBlueprintRotation` at `:584-617`;
- `CheckNullDefDrop` at `:619-653`;
- `CheckRecordRoundTrip` at `:655-737`; and
- `CheckSafely` at `:1061-1075`.

`Run` invokes the existing behavior checks in order at
`Source/Intercolony/Debug/IntercolonyProduceSelfTest.cs:81-103`. The most direct F03 seams are
`CheckDisablePreservesWork` (`:182-223`), `CheckBlueprintPlacement` (`:262-349`), and
`CheckDesignatorCancel` (`:351-582`). The persistence seam for F03/F04 record modes is
`CheckRecordRoundTrip` (`:655-737`), which currently asserts only cell, rotation, thing def, and
stuff def after load at `:687-737`.

## NOT FOUND — exact searches for absent paths

The following absences are findings, not inferred API behavior:

```text
rg -n -i "class .*Designator|new .*Designator|Designator_.*Produce|Produce.*Designator" Source/Intercolony
rg -n -i "Produce.*FloatMenu|FloatMenu.*Produce|Produce.*MainTab|MainTab.*Produce|Produce.*Dialog|Dialog.*Produce" Source/Intercolony
rg -n -i "GetFloatMenuOptions|FloatMenuOption" Source/Intercolony/Production
rg -n -i "produce|loop" Source/Intercolony/UI/MainTabWindow_Intercolony.cs Source/Intercolony/UI/MainTabWindow_Intercolony_Labor.cs Source/Intercolony/UI/MainTabWindow_Intercolony_Business.cs
rg -n -i "DesignateMultiCell|DesignateSingleCell|CanDesignateCell|CanDesignateThing|DesignateThing" Source/Intercolony/Production
rg -n -i "MultiSelect|SelectedThings|area order|area" Source/Intercolony/Production
rg -n -i "Quality|SkillDef|Skill|Pawn|Worker|target.?count|TargetCount|count.?target|Bill|Recipe|Job" Source/Intercolony/Production
rg -n -i "BillStack|IBillGiver|Bill_Production|RecipeWorkerCounter" Source/Intercolony/Production reference/decompiled/RimWorld/Blueprint.cs
rg -n -i "CurrentSaveVersion|saveVersion|schema|migration" Source/Intercolony/Production
```

These searches establish that the mod has no dedicated Produce designator, area/multi-select
Produce implementation, Produce float menu, Produce main-tab entry, Produce-specific dialog, or
Produce-owned count/quality/worker/skill/bill/job model. The existing vanilla Cancel side effect
and the existing per-object gizmo are the only UI-adjacent paths found in the requested scope;
their positive evidence is cited in sections B and E.

## Conclusions — design opinions and proposed Stage 2 unit boundaries

### Exact attachment anchors

The following is the requested file:line map from the read findings to the work that Stage 2 would
have to attach.

| Work item | Existing attachment seam |
|---|---|
| F03 persisted Pause state | Add the record field beside the five current fields at `Source/Intercolony/Production/ProduceLoopRecord.cs:7-11`; add its Scribe call in `ExposeData()` at `:13-20`. |
| F03 poll suspension | Add the paused early-out after invalid-record cleanup and before the current thing scan/finished-building handling at `Source/Intercolony/Production/ProduceLoopMapComponent.cs:42-45` and `:52-101`. |
| F03 Resume / Stop transitions | Extend the map-component lookup and state API around `IsEnabled` / `Find` / `Enable` / `Disable` at `Source/Intercolony/Production/ProduceLoopMapComponent.cs:148-187`; Stop can use the existing destructive `Disable` path at `:184-187`. |
| F03 per-object controls | Replace or extend the current `Command_Toggle` action at `Source/Intercolony/Production/ProduceGizmoPatch.cs:114-132`. |
| F03 area controls | Add a new `Designator`-derived class using the vanilla cell/thing hooks at `reference/decompiled/Verse/Designator.cs:249-290`; the current mod has no production designator, as recorded by the exact searches above. |
| F03 Cancel compatibility | Reconcile the existing blueprint/frame-only prefix at `Source/Intercolony/Compatibility/HarmonyPatches.cs:172-196`. |
| F04 mode/target/quality/worker persistence | Extend `ProduceLoopRecord` and its Scribe surface at `Source/Intercolony/Production/ProduceLoopRecord.cs:7-20`; the current map list is deep-scribed at `ProduceLoopMapComponent.cs:194-202`. |
| F04 target-count observation | Attach the decision to the existing poll/placement seam at `Source/Intercolony/Production/ProduceLoopMapComponent.cs:40-121`; the reusable mod stock reads are `Source/Intercolony/Market/FindBuyerService.cs:611-633` and `:712-788`. |
| F04 per-object configuration | Extend the gizmo seam at `ProduceGizmoPatch.cs:108-132`, with the existing object-targeted modal pattern at `Source/Intercolony/UI/Dialog_Counteroffer.cs:30-61` and `:342-379`. |
| F04 worker/skill/quality policy | The current blueprint creation seam is `ProduceLoopMapComponent.cs:104-121`; actual worker eligibility and completion are vanilla at `reference/decompiled/RimWorld/WorkGiver_ConstructFinishFrames.cs:17-43`, `reference/decompiled/RimWorld/JobDriver_ConstructFinishFrame.cs:18-86`, and `reference/decompiled/RimWorld/Frame.cs:262-296`. |
| F03/F04 tests | Add assertions to the registered `produce` suite at `Source/Intercolony/Debug/IntercolonyProduceSelfTest.cs:81-103`, beside behavior checks at `:182-223`, `:262-349`, `:351-582`, and persistence at `:655-737`. |

1. **Cut F03 first at the state machine, not at the UI.** `Disable` currently deletes the only
   record (`Source/Intercolony/Production/ProduceLoopMapComponent.cs:184-187`), while the poller
   can uninstall a finished building and place the replacement blueprint
   (`Source/Intercolony/Production/ProduceLoopMapComponent.cs:77-121`). A Pause/Resume unit should
   therefore own a persisted `paused` state and a poll guard before that branch; a separate Stop
   unit should retain destructive record removal and prove that in-progress vanilla work is not
   canceled (`Source/Intercolony/Debug/IntercolonyProduceSelfTest.cs:201-222` and `:341-348`).

2. **Cut F03 UI into one shared transition API plus two adapters.** The per-object gizmo is the
   existing adapter (`Source/Intercolony/Production/ProduceGizmoPatch.cs:114-132`); the area adapter
   should follow `Verse.Designator`'s cell/multi-cell contract
   (`reference/decompiled/Verse/Designator.cs:249-290`) and call the same map-component state
   transitions. Keep the Cancel prefix as a compatibility edge case, because its current guard is
   limited to blueprint/frame cancellation (`Source/Intercolony/Compatibility/HarmonyPatches.cs:177-196`).

3. **Cut F04 into configuration/persistence, observation, and construction-policy units.** The
   current map pass is the only loop decision seam (`Source/Intercolony/Production/ProduceLoopMapComponent.cs:40-121`),
   the mod's existing stock read model is storage/fungible-item oriented
   (`Source/Intercolony/Market/FindBuyerService.cs:712-788`), and actual quality/skill/worker
   outcomes belong to the vanilla finish-frame job (`reference/decompiled/RimWorld/JobDriver_ConstructFinishFrame.cs:18-86`
   and `reference/decompiled/RimWorld/Frame.cs:262-296`). This argues for separate tests and state
   seams for target-count observation, quality constraints, worker eligibility, and any
   multi-worker policy rather than treating F04 as a cosmetic expansion of the current toggle.

4. **Extend the existing self-test at the current behavior seams.** Pause/resume/stop assertions
   belong beside `CheckDisablePreservesWork`, blueprint-repeat assertions, and Cancel behavior;
   mode persistence belongs beside `CheckRecordRoundTrip`; and worker/quality/count policy needs
   new assertions because no such concept currently appears in the production test fixture. The
   current registration and invocation surface is `produce` at
   `Source/Intercolony/Debug/IntercolonyAllSelfTests.cs:175-176` and
   `Source/Intercolony/Debug/IntercolonyProduceSelfTest.cs:81-103`.
