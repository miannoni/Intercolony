# Intercolony — Playtest Finalization Execution Plan

**Repository:** `miannoni/Intercolony`  
**Execution mode:** Claude Code + Agent Foreman  
**Audited base:** `main` at `e79fba0c3343993470464752112473971a41fff6` (`1.1.0` merged/tagged/released)  
**Current released save schema at that base:** `58`  
**Purpose:** finish the remaining product decisions from the F01–F25 playtest batch without reopening already-closed work, and do so with bounded, targeted reconnaissance rather than broad rediscovery.

---

# 0. Authority and intended use

This document is the newest product direction for the findings it explicitly changes:

- **F04** — Produce controls
- **F06** — employee apparel policies + equipment-bond interaction
- **F16** — employee-card redesign; this also supplies the newest presentation direction for F13/F17
- **F19 + F20** — contract economics / material and labor costing
- **F21 + F24** — settlement rapid-logistics capability + emergency hiring
- **F23** — requested employee equipment level

Where this plan conflicts with `docs/PLAYTEST_BATCH_SOURCE_PLAN.md`, `docs/PLAYTEST_CORRECTION_PLAN.md`, old recon notes, comments, or historical progress records, **this plan wins for those findings**.

Do not re-run completed correction work merely because older plans still exist in the repository.

## Hard scope

Implement only the findings listed above.

Keep explicitly frozen:

- **F12 — recurring/preprogrammed player caravans**
- **F22 — player-supplied reverse labor market**

Treat these as regression-only unless this run directly breaks them:

- F01, F02, F03, F05, F07, F08, F09, F10, F11, F14, F15, F18, F25

F13 and F17 are **not separate implementation slices in this run**. Their accepted semantics are folded into the F16 redesign:

- Auto-renew must remain directly visible and directly toggleable.
- Occasional actions must not clutter the collapsed employee card.

Do not dispatch work for any other finding.

---

# 1. Foreman operating mode — intentionally more on-rails

The previous run demonstrated that broad recon can consume substantial tokens while rediscovering facts already known. This plan therefore supplies a bounded technical map for each stage.

## 1.1 Recon policy

**Do not begin each stage with a repo-wide Sol recon.**

For each stage below, this plan lists:

- the known production files;
- the exact technical unknowns that still deserve inspection;
- the narrow vanilla reference area, if any;
- the expected tests.

Foreman should follow this rule:

> Inspect the named production files first. Dispatch a read-only recon agent only for a question explicitly marked **FOCUSED RECON REQUIRED**. The recon should answer only those questions and stop.

A focused recon should normally:

- read fewer than ~10 files;
- avoid `rg` sweeps across the entire repository unless a named symbol moved;
- avoid re-reading `DESIGN.md`, the whole source plan, all old RECON files, or all of `PROGRESS.md`;
- avoid producing a long standalone recon document unless a genuine blocker is discovered;
- return a compact answer: observed seam, safest implementation route, exact files/methods, and any incompatibility.

If the named implementation path is still present and coherent, **implement it**. Do not ask Sol to independently redesign the feature.

## 1.2 When broader recon is justified

Broaden only when one of these is true:

1. the named method/class no longer exists;
2. a vanilla seam assumed below is materially different in the installed 1.6 references;
3. a proposed additive field would conflict with an existing owner;
4. a targeted mutation proves the proposed seam does not control the production behavior;
5. save/load or pawn ownership semantics become uncertain enough that guessing could corrupt a save.

When that happens, quarantine only that dependency chain and continue independent stages.

## 1.3 Slice discipline

Each implementation slice should still have one primary behavioral claim.

Prefer this sequence inside a stage:

1. narrow source inspection;
2. add/adjust model/service state;
3. targeted self-test;
4. mutation/negative-control where useful;
5. player-facing UI;
6. save/load assertion if persisted state changed;
7. commit.

Do not combine unrelated cleanup just because the same large UI file is open.

---

# 2. Git, branch and release discipline

At run start:

1. `git fetch`.
2. Confirm `main` contains the 1.1.0 release baseline or a newer commit that contains it.
3. Create a fresh working branch from current `main` unless the operator has already prepared one.

Recommended branch name:

```text
foreman/playtest-finalization-2026-09-13
```

Do not continue development directly on the old published `foreman/playtest-batch-2026-09-06` branch.

Before editing:

```text
git status
git log -1 --oneline
dotnet build
```

Hard authority boundaries:

- do not merge to `main`;
- do not tag;
- do not create a GitHub release;
- do not publish/update Steam Workshop;
- do not perform destructive repository operations.

Those remain operator actions.

---

# 3. Baseline facts — do not spend recon rediscovering these

The plan is authored against the released 1.1.0 code where these facts are already established.

## Produce

- `ProduceLoopRecord` is the persisted per-loop map record.
- `ProduceLoopMapComponent` is the per-map owner and currently supports pause plus `targetCount`.
- `ProduceGizmoPatch` currently exposes `Produce`, `Pause production`, and a `Set target` slider.
- The target UI is hard-capped at 100 in `ProduceGizmoPatch`.
- Target counting currently scans storage groups for minified finished instances of the produced `ThingDef`.
- Existing Produce loops place ordinary vanilla construction blueprints.
- `IntercolonyProduceSelfTest` is the principal automated test home.

## Labor / employee equipment

- Employees are real player-faction quest lodgers while active.
- Their original weapons and worn apparel are captured into `EmploymentEquipmentRecord` at hire.
- `EmploymentEquipmentService` values that gear and charges a refundable bond.
- Bond settlement currently matches returned equipment by def + stuff + quality; normal wear is ignored.
- Existing records do not yet remember that a particular quantity was deliberately bought out/forfeited during the employment.
- Vanilla apparel policy UI/optimizer blocks quest lodgers in specific vanilla methods; the previous recon already established this is not solvable by merely toggling an Intercolony bool.
- `IntercolonyLaborSelfTest` is the principal automated labor test home.

## Employee UI

- `MainTabWindow_Intercolony_Labor.cs` owns the employee page and current employee-row layout.
- Auto-renew is already a persisted `EmploymentContract.autoRenew` bool and is directly clickable after the correction run.
- The full card redesign was intentionally not done in the correction run.

## Business economics

- `BusinessReportService.ContractEstimate` currently contains several overlapping comparison figures:
  - finished-good purchase estimate;
  - direct-input estimate;
  - whole payroll;
  - relevant-workforce payroll;
  - transport/premium approximation.
- The UI currently exposes several of those rows, which is the source of the confusing presentation.
- `EstimateDirectInputs` is recipe-oriented and must be expanded to understand constructed/Produce furniture.
- `EstimateDirectLabor` is a relevant-workforce approximation, not measured work-time attribution.
- The current seller-delivery `transport` figure is derived from the price premium rather than measured caravan cost.
- `IntercolonyLedgerSelfTest` is the principal test home for these calculations.

## Settlement / emergency labor

- `SettlementEconomicProfile` is deterministic, regenerated identity rather than persisted state.
- `SettlementProfileGenerator` already derives tech, wealth, archetype and stable per-settlement variation.
- `LogisticsQuote` already centralizes distance/time/method for normal trade logistics.
- Current F24 direct-hire emergency mode:
  - keeps the nearest half of the listing;
  - multiplies wage by 4x;
  - compresses travel to one third;
  - floors arrival at one day;
  - has no drop-pod capability model.
- `LaborCandidateService` owns those emergency constants and filters.
- The Labor UI owns the Emergency Dispatch toggle.

## Job postings / F23

- F25 is already implemented: postings specify requirements and applicants quote their own wage.
- `JobPosting` persists skill, minimum level, term, wage structure and combat clause.
- `JobPostingService` matches the same world labor census against postings.
- Applicants materialize as pawns only after a lightweight prospect qualifies.
- Equipment bond exists at hire, but the posting has no requested equipment-level field.

---

# 4. Persistence strategy for this run

Several labor additions require persisted fields. Avoid a chain of unnecessary schema bumps.

At run start, verify `IntercolonyWorldComponent.CurrentSaveVersion` is still 58. If it changed after this plan was written, use the actual current version and increment from there.

## Recommended one-time labor schema tranche

If the repo still uses schema 58, reserve the next schema for the additive labor state required by F06/F23/F24.

Expected additive fields are conceptually:

```text
EmploymentEquipmentRecord
  boughtOutQuantity / forfeitedQuantity

EmploymentContract
  apparelBondDecision   // pending / allowed / denied
  arrivalTransport      // conventional / drop pod

JobPosting
  requestedEquipmentLevel  // Any / None / Standard / Professional / Elite
```

Exact names may differ, but preserve those semantics.

Migration/defaults for existing saves:

```text
forfeited quantity        -> 0
apparel decision          -> Pending / not yet asked
arrival transport         -> Conventional
requested equipment level -> Any
```

Do not rewrite old bonds, old employees, or old postings.

If implementation proves one of these facts can remain entirely transient without weakening save/load behavior, omit that field rather than persisting redundant state.

Produce settings live in the per-map `ProduceLoopRecord` and do **not** require a world save-schema bump. Existing loop records must receive safe defaults when new fields are absent.

---

# 5. Stage P0 — Scope lock and baseline

Before feature work:

1. Create the working branch from current `main`.
2. Record the run in `FOREMAN.md` / the current run ledger.
3. Run the existing whole-suite baseline only once before edits if practical.
4. Record this scope matrix:

| Finding | This run |
|---|---|
| F04 | implement expanded Produce Controls |
| F06 | implement apparel policy + bond consent/buyout |
| F16 | implement full employee-card redesign |
| F19 + F20 | replace economics presentation/calculation shape |
| F21 + F24 | implement rapid-logistics capability + real emergency arrival |
| F23 | implement requested equipment level |
| F12 | frozen |
| F22 | frozen |
| all other findings | regression-only |

Do not dispatch recon for closed findings merely to confirm they are still closed.

---

# 6. Stage P1 — F04: Produce Controls

## 6.1 Player-facing result

The current `Set target` gizmo becomes **Produce controls**.

Clicking it opens a dedicated popup for the selected Produce program.

The popup configures the existing per-object Produce program; it does **not** create a global library of named profiles.

### Required controls

**Mode**

```text
○ Produce indefinitely
● Maintain stock
```

When `Maintain stock` is selected:

```text
Target:       [ 100 ]  [-10] [-1] [+1] [+10]
Resume below: [  80 ]  [-10] [-1] [+1] [+10]
```

Rules:

- Target is a typed integer field, not a slider.
- Zero is reserved for the indefinite mode internally; the UI should not make the player reason about `0 = infinite` if the mode radio already expresses it.
- `Resume below` must be lower than `Target`.
- If the player lowers Target below Resume Below, clamp/repair the resume value visibly rather than storing an impossible program.

**Workers**

```text
Worker eligibility:
○ Any eligible pawn
● Selected pawns

[ ] Pawn A
[ ] Pawn B
[ ] Employee C

Minimum Construction skill: [ 8 ]
```

Rules:

- Multiple allowed pawns may participate across cycles.
- This does not mean two pawns may work the same vanilla frame simultaneously.
- Normal vanilla work priorities, reachability and job selection still apply.
- This control only says who is allowed to take this Produce-owned construction work.

**Materials**

For a stuffable product, show only valid stuffs for that building.

```text
Allowed materials
[x] Wood
[x] Steel
[ ] Silver
...
```

For non-stuffable products, show a read-only/fixed-material explanation rather than an empty filter.

There is **no quality filter** in F04.

Do not add:

- ingredient radius;
- stockpile destination;
- work priority scheduling;
- time-of-day scheduling;
- bill ordering;
- quality constraints;
- global named Produce profiles;
- a second production-planning system.

## 6.2 Target semantics

This is product-locked:

> Target counts finished products available in colony storage that match the Produce program.

Count:

- completed/minified copies of the produced `ThingDef`;
- only allowed stuffs where the product is stuffable.

Do not count:

- the currently installed production anchor;
- blueprints;
- frames;
- an item merely because construction has started;
- disallowed material variants.

The existing storage-group/minified-item counting logic is the right conceptual base.

## 6.3 Resume-below hysteresis

The current code resumes as soon as stock becomes one unit lower than target. Replace that with a latched hysteresis state.

Conceptual loop state:

```text
if indefinite:
    operate normally

if not waitingForResume and stored >= target:
    waitingForResume = true

if waitingForResume:
    if stored > resumeBelow:
        do not start a new cycle
    else:
        waitingForResume = false
        resume production
```

Persist the latch if necessary so save/load does not make a program at `99/100, resume at 80` unexpectedly restart after reload.

In-flight vanilla construction may finish. The threshold governs **starting another cycle**, not destroying ongoing work.

## 6.4 Produce target maximum setting

Move the hardcoded 100 out of `ProduceGizmoPatch`.

Add a mod setting conceptually:

```text
Maximum Produce target
Default: 1000
Suggested range: 100–10000
```

Known files:

- `Source/Intercolony/IntercolonySettings.cs`
- `Source/Intercolony/IntercolonyMod.cs`

Semantics:

- setting limits what the Produce Controls UI may newly enter;
- do not silently rewrite an existing saved program if the player later lowers the setting below that program's target;
- opening that existing program should display its actual value and require an explicit edit before clamping it.

## 6.5 Known production files

Start here; do not recon the whole mod:

- `Source/Intercolony/Production/ProduceLoopRecord.cs`
- `Source/Intercolony/Production/ProduceLoopMapComponent.cs`
- `Source/Intercolony/Production/ProduceGizmoPatch.cs`
- `Source/Intercolony/Debug/IntercolonyProduceSelfTest.cs`
- `Source/Intercolony/IntercolonySettings.cs`
- `Source/Intercolony/IntercolonyMod.cs`

Create a narrow popup file such as:

```text
Source/Intercolony/UI/Dialog_ProduceControls.cs
```

unless the existing UI folder has a stronger local naming convention.

## 6.6 FOCUSED RECON REQUIRED — only two construction questions

Do one bounded vanilla-reference inspection before implementing worker/material enforcement.

Answer only:

1. **Worker gate:** what is the narrowest vanilla construction-job seam where a pawn about to deliver to / finish a Produce-owned blueprint or frame can be rejected based on the loop record, without changing ordinary construction?
2. **Stuff selection:** what existing vanilla helper should be reused to validate stuff/build costs and determine whether a selected allowed stuff can build this `ThingDef`?

Inspect only the relevant 1.6 construction workgiver/job-driver/reference files, beginning with the construction workgivers and `GenConstruct`/build-cost helpers.

Preferred implementation shape:

- identify Produce-owned blueprint/frame by map + cell + `ProduceLoopRecord`;
- ordinary construction stays untouched;
- for Produce-owned work only, enforce selected-pawn/min-skill eligibility;
- avoid replacing vanilla construction jobs with a custom job driver.

## 6.7 Material-choice rule

The loop must place one concrete vanilla blueprint, so an allowed-material set eventually needs one selected stuff for the next cycle.

Use this deterministic rule unless targeted vanilla evidence offers an equally simple native mechanism:

1. if the previous/current preferred stuff is allowed and enough is available, keep it;
2. otherwise choose among allowed valid stuffs that have enough material for the build;
3. prefer the candidate with the greatest usable available amount;
4. tie-break by stable `defName` order;
5. if no allowed stuff can currently satisfy the stuff requirement, do not start a new blueprint yet.

Do not place a blueprint locked to a disallowed stuff merely because that was the original furniture's material.

For old Produce records, initialize the allowed set to the record's existing `stuffDef` so old saves preserve old behavior until edited.

## 6.8 Acceptance tests

Extend `IntercolonyProduceSelfTest` to prove at minimum:

1. old record without new fields loads as its old single-stuff, any-eligible-pawn program;
2. indefinite mode still loops;
3. target 100 stops new cycles at 100;
4. target 100 / resume below 80 remains stopped at 99, 90 and 81;
5. it resumes at 80 or lower;
6. save/reload preserves the waiting-for-resume latch;
7. stored matching material variants count; disallowed material variants do not;
8. installed anchor / blueprint / frame do not count;
9. selected-worker mode rejects a non-selected builder;
10. selected-worker mode allows either of two selected pawns;
11. minimum Construction rejects an under-skilled allowed pawn;
12. allowed-material selection chooses an available permitted stuff deterministically;
13. no allowed material available -> no replacement blueprint, no exception/spam;
14. existing Pause/Resume/Stop and F02 cancel semantics remain intact.

### Human evidence

The popup itself requires a human UI check:

- typed field usability;
- step buttons;
- worker list readability;
- material list readability;
- no clipping at the normal tested UI scale.

Do not claim the popup is visually accepted from model-only tests.

---

# 7. Stage P2 — Labor persistence spine for F06/F23/F24

Before implementing the three labor features, add their additive saved fields in one coherent migration if the project migration convention permits it.

Known files:

- `Source/Intercolony/Labor/EmploymentContract.cs`
- `Source/Intercolony/Labor/EmploymentEquipment.cs`
- `Source/Intercolony/Labor/JobPosting.cs`
- `Source/Intercolony/Core/IntercolonyWorldComponent.cs`
- migration/status tests already used by the project

Do not add behavior yet beyond safe defaults and round-trip assertions.

Required round-trip checks:

- old employment record -> zero forfeited gear, apparel consent pending, conventional arrival;
- old job posting -> equipment `Any`;
- save/reload preserves non-default values once assigned.

Commit the persistence spine separately so later labor slices can rely on stable saved fields.

---

# 8. Stage P3 — F06: vanilla apparel policies for employees, with bond buyout

## 8.1 Player-facing rule

Employees should participate in the colony's **vanilla apparel policy system by default**.

Do not build an Intercolony outfit/filter system.

The first time vanilla apparel management wants to remove original employee-owned/bonded apparel, interrupt once with a simple consent card/dialog:

```text
<Worker> wants to change apparel.

Removing their issued apparel means you keep that gear and forfeit the refundable bond attached to it.

Bond at risk now: X silver

[Keep issued apparel]   [Allow change]
```

Exact copy may be improved, but keep it brief.

The goal is **one meaningful consent moment**, not repeated confirmations for every shirt/pants swap.

## 8.2 Consent semantics

Use a small persisted state such as:

```text
Pending
Allowed
Denied
```

- **Pending:** default for new and migrated active employees.
- **Allowed:** vanilla apparel management may remove original bonded apparel; each removed original item is bought out/forfeited.
- **Denied:** automatic removal of original bonded apparel is blocked and the prompt does not repeat every optimizer pass.

If the player later wants to reverse a denial, expose a small secondary employee action (for example in the employee overflow/secondary actions surface) to allow apparel changes. Do not add another permanent primary-card control.

## 8.3 Bond semantics

Do **not** charge silver again when original gear is removed.

The colony already paid the refundable equipment bond at hire.

When an original bonded item is deliberately removed under approved apparel management or approved force-drop:

> the corresponding portion of the bond becomes permanently non-refundable.

This is effectively the colony buying/keeping that piece.

Persist the bought-out/forfeited quantity on the original equipment record. Once forfeited, later giving the pawn an identical item must **not** recreate refund eligibility.

Existing settlement should become conceptually:

```text
refundable original quantity
= original quantity
- bought-out/forfeited quantity
```

Only the still-refundable portion participates in end-of-employment matching/refund.

## 8.4 Colony-supplied gear ownership

Anything the colony supplies **after hire** belongs to the colony.

At employment end:

- original gear still refundable and still with the worker may leave with the worker and return its bond share;
- original gear previously bought out is colony property and is not refundable again;
- colony-supplied apparel/equipment must not be allowed to leave the map with the departing employee.

Reuse existing equipment-custody/settlement seams where possible. Do not create a second general inventory-ownership system.

## 8.5 Force-drop interaction

Player-forced dropping/removal of original bonded weapon/apparel must obey the same economics.

The player may do it, but only after agreeing to forfeit the associated bond share.

Avoid multiple independent economic rules for:

- apparel optimizer removal;
- force-drop apparel;
- force-drop weapon.

All should route through one narrow service conceptually like:

```text
CanReleaseBondedItem(...)
ConfirmBondBuyout(...)
MarkBoughtOut(...)
```

## 8.6 Known mod files

Start with:

- `Source/Intercolony/Labor/EmploymentContract.cs`
- `Source/Intercolony/Labor/EmploymentEquipment.cs`
- `Source/Intercolony/Labor/EmploymentService.cs`
- `Source/Intercolony/UI/MainTabWindow_Intercolony_Labor.cs` only for the secondary consent override if needed
- `Source/Intercolony/Debug/IntercolonyLaborSelfTest.cs`

Create one narrow Harmony/compatibility file for employee apparel behavior rather than scattering patch methods across unrelated files.

## 8.7 FOCUSED RECON REQUIRED — vanilla apparel/drop seams only

Do not reopen the labor architecture.

Inspect only the relevant vanilla 1.6 references already identified by prior recon, plus the exact gear-drop UI/trackers:

- `PawnColumnWorker_Outfit` / Assign apparel-policy restriction;
- `JobGiver_OptimizeApparel` quest-lodger early return;
- `Pawn_ApparelTracker` removal/drop path;
- `Pawn_EquipmentTracker` drop path;
- gear-tab command implementation only as needed to identify player-forced drops.

Answer:

1. What two narrow patches make apparel policy selection and vanilla apparel optimization work for **active Intercolony employees only**?
2. Where can the mod intercept a player-forced bonded drop **before** it occurs, so confirmation is real rather than an after-the-fact message?
3. Where can the mod reliably observe which original apparel item vanilla removed after consent, so the matching equipment record is marked bought out exactly once?

Do not patch `Pawn.IsColonist` or globally weaken quest-lodger restrictions.

Additional Harmony patches required specifically for this behavior are authorized. Do not preserve an obsolete numeric patch-count limit at the expense of the feature.

## 8.8 Acceptance tests

Automated:

1. migrated employee defaults to consent pending;
2. non-employee quest lodgers remain vanilla-unchangeable;
3. Intercolony employee may be assigned a vanilla apparel policy;
4. denied consent blocks removal of bonded apparel without repeated state changes;
5. allowed consent marks removed original apparel bought out exactly once;
6. bought-out quantity no longer contributes to end-of-contract refund;
7. later identical replacement apparel does not resurrect refund eligibility;
8. approved force-drop of original weapon/apparel marks the same bought-out state;
9. rejected force-drop leaves gear and bond state unchanged;
10. colony-supplied gear is recovered before ordinary employee departure;
11. normal wear still does not reduce refund on non-bought-out original gear;
12. bond still settles once.

Human:

- first clothing swap produces one understandable consent card;
- accepting causes normal vanilla outfit behavior afterward without prompt spam;
- declining does not spam every optimization tick;
- employee no longer inevitably ends up tattered/naked when the colony has suitable apparel.

---

# 9. Stage P4 — F16: employee-card redesign

This is a UI/hierarchy redesign. Do not alter employment economics merely to make the new card easier to draw.

F16 is the newest presentation authority and **supersedes the old blanket F17 instruction that every occasional action must live behind `...`**. The new rule is:

- collapsed card is extremely sparse;
- expanded card may expose the specified action stack;
- truly secondary/destructive/rare actions may stay in `...`.

## 9.1 Collapse state

Default employee cards to **collapsed**.

Use UI/session state keyed by employment ID unless there is an existing persisted UI-state convention. Do not bump save schema just to remember whether a row was expanded.

## 9.2 Collapsed card

The collapsed card should answer only:

> Who is this person, what kind of employee are they, and will the contract auto-renew?

Layout intent:

```text
[portrait]  Worker Name                         Security Contractor
            brief secondary identity            Auto-renew  [✓]
                                                    [...] / Dismiss if appropriate
```

Required:

- portrait first;
- name immediately to the right;
- employment/combat type at far right;
- Auto-renew directly visible and clickable beneath/near the type;
- at most Dismiss/Cancel or the compact secondary menu;
- no wage table, bond detail, mood history, payment detail, renewal narrative, etc.

F13 regression requirement:

- ON/OFF state must be visible without opening another menu;
- direct click toggles the saved `autoRenew` field.

## 9.3 Expanded card

Expansion reveals a clean contract table.

Required rows/fields:

```text
Pay                 actual charged pay / day
Time remaining      X days / Open-ended / notice state where applicable
Payment             Daily / Per quadrum / Prepaid
Equipment bond      refundable/current bond state
Death compensation  current applicable figure
Happiness           one-word summary
```

Important:

- show what the player is actually charged, not the worker's historical ask;
- if F06 has bought out part of the bond, the bond row should make the remaining refundable amount legible rather than pretending the original full deposit is still refundable;
- do not add a duplicate paragraph explaining the same values below the table.

### Happiness vocabulary

Use a compact, stable five-band vocabulary unless an existing RimWorld label is clearly better:

```text
Miserable
Unhappy
Content
Happy
Excellent
```

The one-word state is the card value. A short tooltip may show the underlying current/observed mood value; do not paste employment-history prose into it.

## 9.4 Expanded action stack

At the side of the expanded card, reserve stable button positions for:

```text
Renew
Keep them
Negotiate
```

Rules:

- available action -> normal button;
- unavailable action -> visible but dimmed/disabled;
- do not move buttons around based on eligibility;
- `Renew` uses the existing renewal path;
- `Keep them` uses the existing stay/transition path;
- **do not invent employment renegotiation in this stage**. If no real employment-negotiation domain action exists, `Negotiate` remains disabled with a terse tooltip.

Dismiss/Cancel may remain a compact destructive/secondary action rather than joining the main stack.

## 9.5 Tooltip cleanup

Remove or drastically shorten employee-card tooltips that merely repeat:

- old hiring terms;
- historical ask;
- information visibly present in the card;
- long narrative descriptions of already-completed events.

Tooltips should answer only hidden/current questions such as:

- why a button is disabled;
- what Auto-renew does;
- what the remaining bond means;
- what the happiness word maps to.

## 9.6 Known file

This should be mostly contained in:

```text
Source/Intercolony/UI/MainTabWindow_Intercolony_Labor.cs
```

Read the employment-domain service only when wiring an existing action. Do not dispatch a broad recon.

## 9.7 Acceptance evidence

Automated/model-level where practical:

- row-height/layout helper calculations do not overlap at representative widths;
- Auto-renew mutates the correct contract;
- disabled actions cannot execute;
- expanded/collapsed state does not alter domain state.

Human evidence is required for the actual UI:

- several employees visible at once while collapsed;
- long names do not collide with type/Auto-renew;
- expanded table remains readable;
- stable action stack does not jump;
- 1.75x UI scale sanity if that remains the project's standard manual scale;
- tooltips feel shorter rather than merely rearranged.

---

# 10. Stage P5 — F19 + F20: replace the Business contract economics block

## 10.1 Product goal

The contract economics section should answer one question in about five seconds:

> Is producing for this agreement economically attractive?

Stop presenting several alternative accounting questions in one block.

## 10.2 Final presentation

Replace the existing multi-row comparison with a compact P&L:

| | Per cycle | Per unit |
|---|---:|---:|
| Revenue | +X | +X/unit |
| Materials | -X | -X/unit |
| Paid labor | -X | -X/unit |
| **Production margin** | **+X** | **+X/unit** |
| **Margin** | **Y%** | |

Below the P&L, visually separate the market benchmark:

```text
Your sale price:       X / unit
Median market price:   Y / unit
```

The second number means:

> approximately the median unit price the current procurement market would quote the player for the same product/specification from other suppliers.

It is a market benchmark, not the player's brand value and not a finished-good cost used in the margin calculation.

## 10.3 Delete from the player-facing block

Remove these concepts from this UI:

- `If you bought the goods instead`;
- whole-company/whole-cycle wage bill;
- separate duplicated fallback/direct rows;
- `making rather than buying is worth ...` prose;
- the current pseudo-transport cost derived from delivery premium.

Do not keep them hidden in a tooltip merely because the code already calculates them.

If an old field is unused after the new design and has no other caller, remove it. If another system consumes it, leave the internal field but stop presenting it here.

## 10.4 F19 — Materials

Material cost means:

> replacement/purchase value of the direct inputs consumed to make the contracted output.

Internally produced input is not free.

### Price hierarchy

For each direct input, use this order:

1. recent actual procurement price the player paid for that exact input/specification, using a robust recent statistic rather than one pathological outlier;
2. current procurement-market estimate for that input;
3. generic market-value fallback.

Do not recursively decompose intermediates.

Examples:

```text
Chair -> wood
Component-using product -> component
```

Do not turn:

```text
Component -> steel + work -> ore ...
```

### Production-route resolution

Support at least two routes:

**Crafted goods**

- resolve recipe(s) that actually produce the ThingDef;
- use the chosen defensible recipe's direct ingredient requirements.

**Constructed / Produce furniture**

- use the building's construction cost for `ThingDef + stuffDef`;
- include the stuff amount and any fixed `costList`/adjusted construction inputs;
- do not report `No known inputs` merely because a chair has no `RecipeDef`.

### FOCUSED RECON REQUIRED — one vanilla construction-cost question

Inspect only the vanilla `ThingDef`/construction-cost helper used by normal building placement and answer:

> What existing helper returns the adjusted concrete material list for a stuffable building, so Intercolony does not reimplement `CostStuffCount + costList` incorrectly?

Use that helper if available.

## 10.5 F20 — Paid labor

Paid labor means:

> estimated wage cost attributable to Intercolony employees relevant to producing this contracted good.

Do not charge ordinary colonists a fictitious silver wage.

Keep the current philosophy of a **relevant-workforce approximation** rather than adding continuous per-tick labor instrumentation.

Extend the eligibility model so it understands:

- recipe/crafting production;
- Construction/Produce production.

For construction/Produce, consider:

- Construction work eligibility;
- required Construction skill where the ThingDef imposes one;
- current active Intercolony employees only.

If no paid employee is relevant:

```text
Paid labor: 0
```

That is a legitimate economic answer, not an error state.

Only show unavailable if the production route itself genuinely cannot be resolved safely.

## 10.6 Margin semantics

For this batch:

```text
Production margin = Revenue - Materials - Paid labor
Margin % = Production margin / Revenue
```

Do **not** subtract player-caravan delivery cost because Intercolony does not currently know that cost defensibly.

If the contract is seller-delivery, show a compact note outside the arithmetic:

```text
Seller delivery — caravan cost not included in production margin
```

Do not convert the seller-delivery price premium into a fake expense.

If a required material cost is genuinely unavailable, do not silently substitute “what buying the finished good costs” and still print a precise margin. Show the unresolved row and suppress the precise margin until the needed cost is defensible.

## 10.7 Median market price

Implement one read-only market benchmark service/helper rather than rolling a fake live RFQ into world state.

Desired semantic input:

- same `ThingDef`;
- same `stuffDef` where meaningful;
- same relevant quality/specification where the contract expresses one;
- current accessible supplier market and current economic conditions;
- no player brand premium.

Preferred evidence order for this **current-market** benchmark:

1. current matching supplier listings / current matching procurement quotes already present in market state;
2. if the existing architecture can safely produce non-mutating indicative supplier prices through the same pricing owner, sample accessible suppliers without creating a request, consuming stock, advancing IDs, or perturbing global RNG;
3. if no current market evidence exists, show `—` rather than relabeling generic base market value as a median.

Take the **median unit price**, not the arithmetic mean.

Do not include the player's own sale offer in the sample.

Tooltip can say, briefly:

> Median current procurement price for this product across available suppliers.

## 10.8 Known files

Start with:

- `Source/Intercolony/Core/BusinessReportService.cs`
- `Source/Intercolony/UI/MainTabWindow_Intercolony.cs`
- `Source/Intercolony/Market/IntercolonyPricing.cs`
- `Source/Intercolony/Procurement/RfqService.cs`
- `Source/Intercolony/Procurement/SupplierListingService.cs`
- purchase-order/history records only as needed for recent paid input prices
- `Source/Intercolony/Debug/IntercolonyLedgerSelfTest.cs`

Do not recon the entire economy stack.

## 10.9 Acceptance tests

Use concrete fixtures.

1. Wooden chair contract resolves wood material quantity/cost.
2. Steel chair resolves steel, not wood.
3. Crafted good resolves recipe ingredients.
4. Intermediate ingredient stays intermediate; no recursive BOM decomposition.
5. Recent actual purchase price outranks current market estimate for material replacement cost.
6. Current procurement estimate outranks generic fallback when no recent purchase exists.
7. No relevant paid employee -> Paid labor 0.
8. Relevant paid Construction employee contributes to a Produce/furniture contract.
9. Unrelated employee does not contribute.
10. Same employee eligible for several live contract goods is not charged in full to every product.
11. Production margin arithmetic contains only revenue/materials/paid labor.
12. Seller-delivery premium is not subtracted as a pretend transport cost.
13. Median market price uses supplier-side market evidence and is median, not mean.
14. Missing market sample displays unavailable benchmark cleanly.
15. UI no longer renders the removed duplicate comparison rows.

Human evidence:

- inspect at least one furniture agreement and one crafted-good agreement;
- player can read the P&L without tooltip archaeology;
- market benchmark clearly looks separate from the production-cost calculation.

---

# 11. Stage P6 — F21 + F24: rapid-logistics capability and real emergency hiring

This stage deliberately **does not** implement the original full F21 logistics ambition.

Do not add:

- route difficulty;
- provisions;
- animal feed burden;
- player-caravan costing;
- deep freight simulation;
- procurement by drop pod;
- automatic caravan selection.

F21's purpose in this run is to give settlements one meaningful rapid-logistics capability that F24 can consume.

## 11.1 Settlement capability

Add a stable profile property conceptually:

```text
Rapid logistics:
- Conventional only
- Drop-pod capable
```

This belongs to `SettlementEconomicProfile` / deterministic generation, not mutable world state.

### Generation intent

Hard rules:

- Neolithic/tribal tech -> no drop pods.
- Medieval -> no drop pods.

Industrial and above use a deterministic tendency from:

- tech tier;
- wealth tier;
- archetype;
- stable settlement-specific variation.

Expected shape:

- poor/rural Industrial -> usually no;
- ordinary Industrial -> sometimes;
- wealthy Industrial / Military / TradeHub -> frequently;
- Spacer/high-tech -> almost always.

A simple bounded score + stable seeded roll is preferred to a complex capability tree.

Starting coefficients are an engineering/balance choice; do not ask the operator solely to choose 25% vs 30%.

Add the result to profile debug output and the existing settlement/economic detail surface where a player can understand it:

```text
Rapid logistics: Drop pods available
```

or

```text
Rapid logistics: Conventional transport only
```

## 11.2 Emergency eligibility replaces nearest-half behavior

Remove the current conceptual rule:

```text
nearest 50% of direct-hire market
+ travel / 3
+ 1-day floor
```

Emergency Dispatch should instead answer:

> Can this worker's source settlement physically get them here within an urgent window?

A candidate qualifies if either:

### Conventional emergency arrival

The ordinary travel estimate is already very short.

Use an initial constant around **2 in-game days maximum** for conventional emergency eligibility. This is a tuning constant, not a new user setting.

Do not artificially divide that already-short travel by three. If the settlement is close enough, its ordinary rapid caravan is the explanation.

### Drop-pod emergency arrival

The source settlement is `Drop-pod capable`.

Distance may have at most a small effect on ETA. The target experience is **same-day, within a few in-game hours**, not a one-day minimum.

Choose an initial value in the few-hours range and keep it as one named balance constant. Do not spend a recon cycle optimizing 3h vs 5h.

## 11.3 Emergency scarcity remains real

Emergency mode uses the same existing direct-hire market.

It does not:

- create extra workers;
- guarantee a Security Contractor;
- reroll the market;
- synthesize elite pawns because the player can afford them.

Candidates from incapable distant settlements simply disappear from the emergency view.

## 11.4 Pricing

Keep emergency expensive.

The existing 4x wage multiplier is a valid starting point and may remain unless tests expose a direct reason to change it.

UI should make the reason legible:

```text
Emergency premium: ...
Arrival: 4h — Drop pod
```

or

```text
Arrival: 1.2d — Emergency caravan
```

Do not create a second independent labor-value model merely to split urgency and pod freight into two hidden formulas. They may be conceptually explained as urgency + rapid transport while sharing the existing premium calculation.

## 11.5 Persist the promised arrival method

A worker hired by drop pod must still arrive by drop pod after save/reload.

Do not infer arrival mode later from current settlement profile or current distance.

Freeze the chosen mode into the EmploymentContract at hire:

```text
Conventional
DropPod
```

The already-stored `arrivalTick` remains the authoritative due time.

## 11.6 Real drop-pod arrival

If the offer says `Drop pod`, the pawn must actually enter the colony map through vanilla drop-pod arrival presentation.

Do not substitute:

- silent spawn;
- edge walk-in;
- letter + teleport.

### FOCUSED RECON REQUIRED — vanilla arrival API only

Inspect the smallest relevant vanilla 1.6 drop-pod arrival path and answer:

1. What vanilla helper/container should be used to drop an already-generated pawn onto the player's map safely?
2. What cell-selection helper provides a valid colony-map arrival cell?
3. What cleanup/faction ownership assumptions must be satisfied for the existing quest-lodger pawn?

Do not recon transport pods as a whole game system. We need only one safe pawn-arrival path.

Prefer reusing vanilla skyfaller/drop-pod containers rather than hand-animating anything.

If a promised drop-pod arrival cannot be executed safely at the due tick, do not silently change it to edge arrival. Preserve the contract/pawn and surface a technical failure for retry/debug rather than deleting paid value.

## 11.7 Known files

Start with:

- `Source/Intercolony/Core/SettlementEconomicProfile.cs`
- `Source/Intercolony/Core/SettlementProfileGenerator.cs`
- `Source/Intercolony/Market/LogisticsQuote.cs` only if a small shared label/helper belongs there; do not force labor pods into trade logistics if that muddies ownership
- `Source/Intercolony/Labor/LaborCandidateService.cs`
- `Source/Intercolony/Labor/EmploymentContract.cs`
- `Source/Intercolony/Labor/EmploymentService.cs`
- `Source/Intercolony/UI/MainTabWindow_Intercolony_Labor.cs`
- `Source/Intercolony/Debug/IntercolonyProfileSelfTest.cs`
- `Source/Intercolony/Debug/IntercolonyLaborSelfTest.cs`

## 11.8 Acceptance tests

Profile/capability:

1. Neolithic/Medieval profiles can never be drop-pod capable.
2. Same seed + settlement identity reproduces the same capability.
3. Different industrial profiles can differ.
4. Wealthy/Military/Spacer samples show the intended higher tendency in deterministic test fixtures.

Emergency market:

5. nearby conventional candidate qualifies;
6. distant conventional-only candidate does not;
7. distant drop-pod-capable candidate qualifies;
8. emergency mode does not create a new candidate;
9. ordinary hire mode is unchanged;
10. 4x or chosen emergency premium remains preview/transaction consistent.

Arrival:

11. conventional emergency hire uses its conventional arrival path;
12. drop-pod hire freezes DropPod mode on contract;
13. save/reload preserves mode and exact arrival tick;
14. when due, drop-pod worker arrives in an actual drop pod and becomes the same active employee the ordinary path would create;
15. no duplicate pawn/quest/arrival is created;
16. normal hires from drop-pod-capable settlements still arrive normally.

Human evidence:

- watch at least one emergency worker actually drop into the colony;
- confirm the event feels visually distinct from ordinary hiring;
- verify the Emergency list explains method + ETA clearly.

---

# 12. Stage P7 — F23: requested equipment level on Post a Job

## 12.1 Player-facing selector

Add to `Dialog_CreateJobPosting`:

```text
Equipment
[Any ▼]
```

Allowed values:

```text
Any
None
Standard
Professional
Elite
```

These are requirements on the posting, not direct item purchases.

### Semantics

**Any**

- no gear filter;
- preserve today's behavior;
- candidate may arrive with whatever normal generated loadout they have.

**None**

- the source settlement supplies no bondable weapon/apparel for this hire;
- equipment bond is zero;
- do not reinterpret this as “cheap gear.”

**Standard / Professional / Elite**

- source settlement must be capable of supplying a loadout at that level;
- actual loadout is shown before hire;
- equipment bond prices the actual supplied gear using the existing bond system.

Do not let the player select exact weapon/apparel defs here.

This must not become a procurement shop inside Labor.

## 12.2 Combat-clause-aware meaning

The tier is interpreted through the worker's job/combat clause.

### Civilian

Higher equipment request means better appropriate work/protective apparel, not “give the cook a rifle because Elite was selected.”

### Armed Employee

Tier may cover a coherent weapon + protection package.

### Security Contractor

Tier may represent increasingly capable combat loadouts; Elite is where genuinely high-end gear may appear **if the source settlement can support it**.

## 12.3 Settlement capability gate

Before an applicant is materialized, the lightweight prospect's source settlement must pass a coarse capability test for the requested tier.

Use:

- settlement tech;
- wealth;
- archetype;
- combat clause;
- stable scarcity/capability logic.

Desired shape:

- `Any` / `None` broadly possible;
- `Standard` common;
- `Professional` restricted;
- `Elite` rare and strongly biased to wealthy/military/high-tech sources.

A low-tech settlement must not materialize endgame gear merely because the posting asked for Elite.

Demanding combinations such as:

```text
Shooting 15+
Security Contractor
Elite equipment
```

should drastically shrink replies because all requirements must clear together.

Do not add a separate arbitrary “elite applicant rarity” roll after capability already expresses scarcity unless testing proves one is necessary.

## 12.4 Actual loadout must satisfy the promise

A settlement capability check alone is not enough. The pawn shown to the player must actually carry the promised tier.

### FOCUSED RECON REQUIRED — current materialisation + vanilla gear helpers

Inspect only:

- `LaborProspect.Materialise` and its immediate pawn-generation path;
- the relevant vanilla pawn gear/apparel generation helpers used by that path;
- no unrelated pawn-generation systems.

Answer:

1. Does normal faction/pawn generation already produce loadouts strongly enough correlated with faction tech that a bounded re-materialisation/classification strategy can satisfy tiers safely?
2. Is there a vanilla gear-generation/budget seam that can request/upgrade equipment without inventing a bespoke item shop?

Implementation preference order:

1. keep the naturally generated loadout if it already satisfies the requested tier;
2. use a vanilla/faction-aware gear-generation helper if it can safely bring the pawn to the requested tier;
3. only if no safe vanilla seam exists, implement a **narrow def-driven loadout allocator** for Labor — not a generalized equipment market.

Any retry/regeneration loop must be tightly bounded and deterministic enough not to create a performance/reroll exploit.

## 12.5 Tier classification

Do not classify tiers by silver value alone.

Use a bounded composite that considers, as applicable:

- settlement/piece tech level;
- item quality;
- weapon effectiveness for combat roles;
- protective apparel effectiveness/coverage;
- market/replacement value only as a secondary signal.

Do not use hardcoded `defName` lists such as “AssaultRifle = Professional.”

Use representative vanilla fixtures in tests to anchor the bands.

## 12.6 `None` behavior

If `None` is requested:

- strip/omit bondable weapons and apparel supplied by the settlement before the applicant is presented;
- the resulting hire quote must show zero equipment bond;
- do not destroy colony-owned items, because the pawn has not joined the colony yet;
- do not touch implants/body parts.

Bionics/implants remain outside F23.

## 12.7 Candidate/applicant UI

The posting itself should show the requirement, e.g.:

```text
Construction 12+ — 20d — Civilian — Equipment: Professional
```

Each applicant row/details should show:

```text
Equipment: Professional
Assault rifle — Good
Flak vest — Normal
Flak pants — Good
Equipment bond: 1,840 silver
```

Use the existing `EmploymentEquipmentService.Quote`/hire-cost path so preview and charged bond remain one calculation.

If a candidate does not actually satisfy the promised tier, do not show them as a valid applicant and hope the hire path fixes it later.

## 12.8 Known files

Start with:

- `Source/Intercolony/UI/Dialog_CreateJobPosting.cs`
- `Source/Intercolony/UI/MainTabWindow_Intercolony_Labor.cs`
- `Source/Intercolony/Labor/JobPosting.cs`
- `Source/Intercolony/Labor/JobPostingService.cs`
- `Source/Intercolony/Labor/LaborCandidateService.cs`
- `Source/Intercolony/Labor/EmploymentEquipment.cs`
- `Source/Intercolony/Labor/EmploymentService.cs`
- `Source/Intercolony/Debug/IntercolonyLaborSelfTest.cs`

Create one focused equipment-tier service rather than placing tier rules across dialog, matcher, candidate service and equipment bond.

Suggested ownership:

```text
LaborEquipmentTierService
  capability by settlement/profile
  classify actual pawn loadout
  prepare/validate requested loadout
  player-facing tier label
```

The Equipment Bond remains owned by `EmploymentEquipmentService`.

## 12.9 Acceptance tests

1. migrated old posting defaults to `Any`.
2. `Any` preserves current matching/loadout behavior.
3. `None` applicant has no bondable supplied gear and bond 0.
4. Standard request filters out a source explicitly incapable of Standard in a deterministic fixture.
5. Professional is rarer/requires stronger capability than Standard.
6. Elite is unavailable to low-tech/poor source fixture.
7. Elite is possible for a capable high-tech/wealthy/military fixture.
8. Civilian Elite does not automatically add an offensive weapon merely because the tier is high.
9. Armed/Security tiers produce role-appropriate loadouts.
10. applicant's actual loadout classifies at or above the requested tier.
11. applicant row/preview shows the exact actual gear.
12. equipment bond equals the existing replacement-value-plus-premium quote for that gear.
13. hiring charges the same bond shown in preview.
14. save/reload of open posting preserves requested tier and waiting applicants.
15. demanding skill + combat clause + Elite reduces candidate eligibility rather than synthesizing replacements.

Human evidence:

- create postings with Any, None and Elite;
- compare applicant quantity and visible gear;
- verify the selector feels like a requirement, not a hidden equipment shop.

---

# 13. Explicit freezes — F12 and F22

These remain frozen even though adjacent labor/logistics code is being edited.

## F12

Do not add:

- saved caravan pawn/animal selection;
- recurring player caravan creation;
- auto-formation;
- auto-dispatch;
- agreement-owned caravans.

F21/F24 rapid transport must not become a back door into F12.

## F22

Do not add:

- colonist labor listings;
- off-map player-pawn employment;
- custody/game-over workarounds;
- external training/risk simulation;
- return lifecycle.

F23 labor RFQ improvements apply only to hiring workers **from** the market.

At final documentation, keep both headings visibly marked frozen so a future Foreman does not redispatch them from the original source plan.

---

# 14. Cross-stage integration checks

After all implementation slices pass narrowly, run these combined scenarios before the full suite.

## 14.1 Produce + Business

1. Configure a wooden-chair Produce loop with wood allowed.
2. Target 10, resume below 5.
3. Let an allowed Construction employee produce chairs.
4. Business production rate still records real completions (F07 regression).
5. Business contract economics resolves wood material cost and paid employee labor.
6. At target, loop stops; selling inventory down to 6 does not resume; selling to 5 resumes.

## 14.2 Apparel + employee card + bond

1. Hire an employee with bonded apparel.
2. Employee card starts collapsed and readable.
3. Vanilla apparel policy wants a change.
4. First removal prompts once.
5. Accept.
6. Bond row in expanded card reflects reduced refundable bond.
7. Employee later wears colony apparel.
8. At contract end, colony apparel stays with colony; original non-bought-out gear settles normally.

## 14.3 Emergency + equipment

F23 does not need to become an Emergency selector in this run, but the systems must coexist:

1. Create a high-tier applicant/job-post path and verify its bond.
2. Separately hire an emergency direct-market worker from a drop-pod-capable settlement.
3. Emergency transport does not mutate the worker's equipment bond or apparel ownership semantics.
4. Save before arrival; reload; worker still arrives by pod with the same gear and contract.

## 14.4 Frozen systems

Search the final diff for accidental implementation of:

- recurring player caravans;
- reverse player labor.

No new code should belong to those behaviors.

---

# 15. Recommended execution order and dispatch shape

Use this order unless a compile-level dependency requires a small local reorder:

| Stage | Work | Recon expectation |
|---|---|---|
| P0 | scope lock / baseline | none |
| P1 | F04 Produce Controls | one focused construction recon |
| P2 | labor persistence spine | none |
| P3 | F06 apparel + bond buyout | one focused vanilla apparel/drop recon |
| P4 | F16 employee card | no recon; direct implementation |
| P5 | F19/F20 Business economics | one tiny vanilla construction-cost lookup only |
| P6 | F21/F24 rapid logistics + pods | one focused drop-pod API recon |
| P7 | F23 equipment levels | one focused pawn-gear materialisation recon |
| P8 | integration / full regression / docs | none unless a real failure appears |

### Foreman dispatch rule

Do not dispatch five recon agents at P0.

A good run should look roughly like:

```text
P0 baseline
→ P1 focused recon + implementation
→ tests/commit
→ P2 implementation
→ tests/commit
→ P3 focused recon + implementation
→ tests/commit
...
```

The recon agent should be called just-in-time when the stage reaches its one genuine technical unknown.

Avoid parallel workers on:

- `MainTabWindow_Intercolony_Labor.cs`;
- `EmploymentContract.cs` / `EmploymentEquipment.cs`;
- world save-schema/migration code.

These stages are small enough that serialization is safer and cheaper than merge/reconciliation churn.

---

# 16. Testing and mutation policy

## Narrow first

After each stage, run the smallest self-test that owns the behavior.

Expected homes:

```text
F04        IntercolonyProduceSelfTest
F06        IntercolonyLaborSelfTest
F16        layout/helper assertions + human UI evidence
F19/F20    IntercolonyLedgerSelfTest
F21        IntercolonyProfileSelfTest
F24        IntercolonyLaborSelfTest
F23        IntercolonyLaborSelfTest
```

Use existing test runners/bridge commands rather than creating a second test harness.

## Mutation

Mutation-prove the important control seams, especially:

- Resume-below latch actually controls restart.
- Produce worker gate actually blocks a non-selected pawn.
- bought-out bond quantity actually changes refund.
- Business material cost actually uses construction inputs.
- emergency capability actually changes candidate eligibility.
- drop-pod mode actually controls arrival path.
- requested equipment tier actually filters/validates applicants.

A passing test that still passes after the governing condition is removed is not evidence.

Do not mutate giant files with brittle line-number surgery if a small test-only parameter/helper can prove the behavior more safely.

---

# 17. Final acceptance gate

Before clean halt:

## Build / suite

Run the repository-standard equivalents of:

```text
dotnet build
dev.ps1 test all -Fresh
```

Require:

- build success;
- whole suite exit 0;
- no new unexplained Player.log errors;
- no new skipped test used as evidence for a completed requirement;
- save/load coverage for every newly persisted field.

## Behavioral checklist

### F04

- Produce Controls popup replaces target slider.
- typed target supports >100 and respects configurable max.
- Resume below hysteresis works.
- selected pawns / min Construction / allowed materials actually govern Produce work.
- target counts only finished stored matching products.
- no quality filter exists.

### F06

- employee can use vanilla apparel policy by default.
- first bonded-apparel removal asks once.
- accepting forfeits only the relevant bond share, without a second silver charge.
- denial does not spam.
- force drop cannot steal bonded gear for free.
- colony-provided gear does not leave with employee.

### F16

- collapsed cards are sparse and scannable.
- portrait/name/type/Auto-renew hierarchy matches the product spec.
- expanded table contains only current contract facts.
- Renew / Keep them / Negotiate positions stay stable; unavailable actions dim.
- no new employment negotiation was invented.
- tooltips are materially shorter.

### F19/F20

- P&L shows Revenue / Materials / Paid labor / Production margin / Margin %.
- furniture/Produce inputs resolve.
- paid Construction employees can be costed.
- ordinary colonists do not gain fictitious wages.
- seller delivery cost is explicitly excluded rather than fabricated.
- `Median market price` reflects current procurement-side market evidence.

### F21/F24

- settlement rapid-logistics identity is stable and visible.
- pre-industrial settlements cannot use pods.
- nearby ordinary emergency candidate can respond conventionally.
- distant conventional-only candidate cannot.
- drop-pod-capable source can respond rapidly.
- drop-pod offer really arrives by drop pod after save/load.
- ordinary hiring remains ordinary.

### F23

- Post a Job has Any / None / Standard / Professional / Elite.
- Any preserves legacy behavior.
- None has no bondable supplied gear.
- higher tiers restrict the market.
- low-tech source cannot magic up Elite gear.
- actual shown loadout meets the requested tier.
- existing equipment-bond system prices and settles it.

### Scope

- F12 still frozen.
- F22 still frozen.
- closed findings remain closed.

---

# 18. Documentation closeout

Update at minimum:

- `PROGRESS.md`;
- `FOREMAN.md` / current run ledger;
- `docs/PENDING_PLAYTESTS.md` for human-only checks;
- `docs/PLAYTEST_BATCH_SOURCE_PLAN.md` disposition markers or an adjacent durable status section so these findings cannot disappear/reopen ambiguously;
- release notes only when/if the operator later decides a release is being prepared.

Record final disposition for:

```text
F04 implemented / evidence
F06 implemented / evidence
F16 implemented / human UI evidence still needed or passed
F19 implemented / evidence
F20 implemented / evidence
F21 implemented to the intentionally narrow capability scope
F23 implemented / evidence
F24 implemented / evidence
F12 frozen
F22 frozen
```

Do not rewrite history to imply the original broad F21 logistics model was completed. State explicitly that the accepted product scope for this pass is the rapid-transport capability needed by emergency labor.

---

# 19. Clean-halt report

When no in-scope work remains, push the branch and halt.

Report:

```text
Branch:
Base commit:
Latest commit:
Working tree:
Commits pushed:
Current save schema:
Whole-suite result:
Log signal:

Implemented:
- F04 ...
- F06 ...
- F16 ...
- F19/F20 ...
- F21/F24 ...
- F23 ...

Frozen:
- F12
- F22

Regression-only findings touched because of an actual regression:
- ... / none

Human playtests still required:
- ...

Reserved operator actions not performed:
- no merge to main
- no tag/release
- no Workshop publish/update
```

Do not continue into Administration, a new feature batch, F12, or F22 after this clean halt.

---

# 20. Definition of success

This run is successful when the last unresolved playtest-product decisions become concrete player-facing systems **without turning the run into another broad redesign**.

The intended outcome is:

- Produce behaves like a compact programmable production tool rather than a target slider;
- long-term employees can dress normally without turning their original gear into free loot;
- employee cards become readable at a glance;
- Business economics reads like a small P&L rather than an accounting thought experiment;
- settlement logistics identity matters exactly where emergency hiring needs it;
- emergency reinforcements can genuinely arrive as the cavalry;
- job postings can ask the labor market for a level of supplied equipment without becoming a gear shop;
- the two intentionally frozen large systems remain frozen;
- Foreman spends tokens implementing known decisions instead of rediscovering them.
