# Intercolony — Playtest Correction Pass II Execution Plan

## 0. FOREMAN BOOTSTRAP — FIRST INSTRUCTION, BEFORE PROJECT WORK

This plan must be executed through Agent Foreman.

Before touching the repository:

1. Invoke/load the registered Foreman skill.
2. Read the canonical `SKILL.md` and `RUN_CONTRACT.md` in full to refresh the Supervisor method. Read `WORKER.md` before the first worker dispatch.
3. Understand and obey the core loop:

   ```text
   WAKE
     -> read durable state
     -> check the ONE active worker once
     -> verify/disposition if finished
     -> choose ONE next bounded unit
     -> dispatch exactly ONE Sol recon OR Luna work worker
     -> persist FOREMAN.md
     -> END THE TURN
   ```

   The worker then runs without Supervisor activity until a native completion wake or the heartbeat starts a new Supervisor turn.
4. Treat dispatch as a HARD TURN BARRIER. After launching a worker, do no more project work in that turn: no polling/sleeping, no future-stage inspection, no future prompts, no pre-recon, no unrelated tests, no second worker. Persist state/status and END.
5. If the Foreman method cannot be located/read, or if the supervising AI cannot understand and follow at least this loop, END THE TURN immediately and tell the operator that Foreman execution was not possible and why. Do not improvise another orchestration style.

Only after this bootstrap gate passes may project execution begin.

---

**Repository:** `miannoni/Intercolony`  
**Audited base branch:** `foreman/playtest-polish-2026-09-14`  
**Audited base HEAD:** `b4bab42afdbfac24389a3c3b282a93105948ae0c`  
**Current save schema:** `59`  
**Previous whole-suite gate:** `1732 passed / 0 failed / 19 skipped`, exit `0`  
**Execution mode:** Claude Code + Agent Foreman  
**Recommended child branch:** `foreman/playtest-corrections-2026-09-18`

**Purpose:** apply the second human-playtest correction pass without reopening already-proven systems. This pass is primarily UX consolidation, Business-report usefulness, F23 equipment diversity/performance, and completion of the F24 Emergency Job Posting/arrival experience.

---

# 1. Authority and scope

This document is the newest product authority for the corrections described here to:

- **F04** — Produce Controls UX consolidation;
- **F16** — employee-card action layout;
- **F19/F20** — Business market benchmark and direct-labor consistency audit;
- **F23** — equipment-package diversity and applicant-generation performance;
- **F24** — Emergency Job Posting arrival quote and drop-pod attention flow.

Where this plan conflicts with older playtest/finalization plans for these specific behaviors, this plan wins.

## In scope

- make every UI action named `Produce controls` mean the same thing;
- move all Produce-related Architect orders into the existing `Production` category;
- make the employee card use explicit action groups rather than contextual swapping for renewal/transition decisions;
- make material-specific contract `Median market price` useful without inventing fake market evidence;
- prove that material choice alone does not change direct labor cost under the existing F20 approximation;
- replace F23's repeated deterministic equipment kits with varied, believable, tier-valid packages;
- reduce/eliminate the severe synchronous hiccup when posting an Emergency + high-equipment job;
- freeze emergency route/ETA onto Job Posting applicants and use that same quote when hired;
- make the drop-pod arrival letter pause the game and provide a functioning vanilla-style jump target.

## Regression-only

- **F06** employee apparel/bond behavior;
- **F19** optional `Any material` / concrete material contract semantics already accepted in play;
- **F20** underlying relevant-workforce approximation except for the consistency audit explicitly in P3;
- **F21** settlement rapid-logistics capability;
- existing F23 abundance Mod Settings;
- existing direct-market F24 emergency timing and physical pod landing;
- existing Produce preset drag / right-click / save-load mechanics.

Touch these only when an in-scope change causes a demonstrated regression or when the plan explicitly calls for a narrow correction.

## Frozen / out of scope

- **F12** recurring/preprogrammed player caravans — FROZEN;
- **F22** player-supplied labor — FROZEN;
- Administration systems;
- unrelated Commercial/Procurement redesign;
- unrelated partial-delivery defect;
- unrelated inaccessible recurring-contract destination defect;
- new labor-accounting instrumentation that attempts to measure exact per-pawn work time;
- global equipment shopping / exact player-picked employee loadouts;
- release-note/Workshop rewrite beyond any tiny documentation correction required by this run.

## Operator-reserved actions

Do **not**:

- merge to `main`;
- tag;
- create a GitHub release;
- publish/update Steam Workshop;
- perform irreversible release/publish actions.

The clean halt is a completed, tested, pushed development branch.

---

# 2. Human-playtest facts — accepted evidence, do not rediscover

The following were directly observed by the operator after the previous clean halt.

| Area | Human evidence |
|---|---|
| F04 bulk application | dragging a saved Produce preset across multiple objects works |
| F04 preset menu | right-click Edit / Rename / Remove works |
| F04 persistence | preset save/load works |
| F04 dead/left worker | assigned pawn remains shown as `(unavailable)` and can be explicitly unassigned |
| F16 lifecycle | Dismiss, Cancel and Not now were exercised and worked |
| F19 material term | optional material selection appears correct; Materials resolves for material-specific stuffable contracts |
| F23 market | equipment-tier postings do produce applicants |
| F24 direct Market | emergency arrival timing is already in hour-scale ranges |
| F24 pod visual | a real drop pod was seen descending and landing in live play |
| F24 Job Posting matching | Emergency posting returns several responses immediately, which is desirable |
| F24 Job Posting hire | after taking an applicant, travel reverts to ordinary multi-day arrival — confirmed defect |
| F24 letter | inbound letter appears, but `Jump to location` is disabled and the game does not pause |

Do not spend Sol/Luna work re-proving these observations unless needed as a regression check.

---

# 3. Baseline facts — do not rediscover these

These were audited on the base branch and are intended to prevent broad reconnaissance.

## 3.1 F04 owners

- `Patches/ProduceDesignators.xml` currently injects `ProduceResume`, `ProducePause`, `ProduceStop`, and `ProducePresetManager` into vanilla **Orders**.
- `Source/Intercolony/Production/ProduceGizmoPatch.cs` owns object-side Produce gizmos.
- The object-side `Produce controls` action currently opens `Dialog_ProduceControls(map, cell)`.
- `Source/Intercolony/UI/Dialog_ProducePresetManager.cs` is the Architect-side manager and is also titled **Produce controls**.
- `ProduceLoopMapComponent` owns map-local loops/presets.
- existing preset designators already support bulk painting and right-click management.

The UX mismatch is real: the same label currently opens two different conceptual windows.

## 3.2 F16 owners

- `Source/Intercolony/UI/MainTabWindow_Intercolony_Labor.cs` owns employee-card layout and action stack.
- `RenewalService` owns fixed-term renewal offers and decline behavior.
- `TransitionService` owns the long-tenure permanent-stay offer and `Not now`/decline behavior.
- fixed-term renewal is offered five days before expiry when eligible; Auto-renew can consume the live offer immediately, which explains why `Let them go` is rarely seen in ordinary play.

## 3.3 F19/F20 Business owners

- `BusinessReportService.Estimate(...)` owns the contract estimate.
- `TryGetCurrentProcurementMarketMedianUnitPrice(...)` currently reads only **already-existing current** `SupplierListing` and open-RFQ `Quotation` evidence matching product/stuff/quality.
- if no such exact current evidence exists, `Median market price` is `—`.
- the contract's own sale price/reference price is intentionally not treated as external market evidence.
- `EstimateDirectLabor(...)` is the accepted F20 relevant-workforce approximation, not measured job time.
- direct labor currently keys production ability by `ThingDef`, not `stuffDef`.
- each eligible employee's charged wage is divided across the distinct current agreement goods they could produce, then multiplied by the contract cadence in days.

Therefore material alone should not alter direct labor for otherwise identical contracts; cadence, quantity presentation, eligible workforce, and the set of other active goods may legitimately change what the player sees.

## 3.4 F23 equipment owners and current cause of sameness

- `LaborProspect.equipmentTier` is the lightweight market promise.
- `LaborEquipmentTierService` is the final authority for actual tier classification and settlement capability.
- Mod Settings already expose Standard / Professional / Elite abundance multipliers.
- `LaborEquipmentAllocator.TryFulfil(...)` currently builds candidates by scanning definitions/qualities, sorting them, building apparel plans, and trying deterministic combinations until it finds a package that crosses the promised-tier threshold.
- the allocator validates the **real generated Things** with `LaborEquipmentTierService.Classify(...)` before accepting the package.
- **Elite must not be re-gated to Spacer.** The previous run measured a Spacer-gated Elite market at **0 of 616** prospects in a representative base-game world, making the category structurally dead. The accepted current gate is **Industrial or better + Comfortable wealth + the stronger Elite capability threshold**; tribal/medieval and weak/poor sources remain excluded.
- **Capability gate and item ceiling are different concepts.** First, the settlement profile decides whether it may plausibly promise Standard / Professional / Elite. Only after that gate passes may the allocator use the equipment ceiling associated with the promised tier to fulfil that promise. Do not re-couple the item ceiling to the source's literal tech tier in a way that makes an already-authorized Elite promise impossible to materialize.

This architecture explains both symptoms observed in play:

1. applicants in the same tier converge on the same or nearly the same minimum-passing package;
2. producing several high-tier applicants can perform substantial definition/quality/package work synchronously.

## 3.5 F24 Job Posting persistence/hire bug

- `JobPosting.emergencyDispatch` is already persisted.
- `JobApplicant` is persisted and currently stores pawn/source/distance/`travelDays`/skill/ask/applied tick.
- `JobApplicant` currently does **not** persist the emergency arrival quote/transport.
- direct Market hiring already computes an `EmergencyArrivalQuote`, stores `arrivalTicks`, and stores `arrivalTransport` on the resulting `EmploymentContract`.
- the current conventional-emergency eligibility threshold is **12 world tiles**. In the previous run, real-world measurement found **0 qualifying conventional emergency routes out of 748 examined source cases**. That rarity is known and accepted for this correction pass: **do not retune the threshold here**. P6 must make the route correct when such a source exists; availability/balance can be revisited separately with new product authority. Constructed fixtures are valid evidence for the 5–9h conventional band.
- `EmploymentService.TryHireApplicant(...)` currently builds the contract with:

  ```text
  arrivalTick = now + applicant.travelDays * TicksPerDay
  ```

  and does not carry the emergency transport.

This is the confirmed reason Emergency Job Posting applicants revert to ordinary 2-day / 7-day arrival after `Take on`.

## 3.6 F24 pod letter

- the physical pod path already uses vanilla `DropPodUtility.MakeDropPodAt(...)` and has been visually proven in play;
- the current code attempts to send an inbound letter with `LookTargets` built from the incoming pod or landing cell;
- in live play, the letter's `Jump to location` is disabled;
- the current inbound letter does not pause the game;
- because time keeps running, the pod may land before the player can react.

Do not redesign the pod transport itself unless focused vanilla evidence proves the current launch seam prevents the required attention behavior.

## 3.7 Graphify/workspace fact

- Graphify is adopted and active in the repository workflow, but `graphify-out/` is a **derived local artifact and is intentionally untracked**.
- The previous run measured a rebuild at roughly **26 seconds**, and `graph.json` could reserialize with about **41,551 changed lines** even without a meaningful source change.
- Do not reintroduce Graphify output into version control, do not treat regenerated graph output as product work, and do not let it prevent a clean halt. Use Graphify as a tool; regenerate the derived artifact locally when needed.

---

# 4. Persistence strategy

**Current world schema: 59.**

Most work in this pass should remain transient/derived:

- F04 UI/category changes: no new persistence;
- F16 layout: no new persistence;
- F19 indicative benchmark: derived/read-only, no persistence;
- F23 equipment catalogue/cache: transient and rebuildable from loaded defs;
- F23 package variation: actual generated gear already persists with the applicant pawn;
- P0 performance instrumentation: temporary, not persisted.

## F24 applicant emergency quote

The emergency applicant's route/ETA is a fact shown to the player and must survive save/load unchanged. Persist enough on `JobApplicant` to freeze at least:

```text
arrival transport
arrival duration/ticks
method/route identity if not derivable safely from transport
```

Do not rely on recomputing an emergency quote at `Take on` time.

Because this adds persisted applicant state and must migrate old emergency postings safely, prefer a **schema 59 -> 60** migration rather than silently changing old saved applicants.

Migration behavior for existing saves:

- ordinary posting applicants remain ordinary and preserve existing `travelDays` semantics;
- applicants under an already-saved Emergency posting receive one deterministic emergency quote during migration/load repair if their source still qualifies;
- if no safe emergency quote can be established, do not silently hire them with ordinary multi-day travel under an Emergency label; invalidate/remove that stale application with a clear log reason and let the posting rematch normally;
- never duplicate or discard the pinned pawn without following existing `JobApplicant` ownership/discard rules.

If repository reality proves the project has a safer already-established additive-state migration mechanism that achieves exactly these semantics without a schema bump, Foreman may use it — but this is an engineering substitution, not permission to drop save/load stability.

---

# 5. Recommended stage order

| Stage | Scope | Recon expectation |
|---|---|---|
| P0 | baseline + performance measurement + scope lock | none unless baseline contradicts audited facts |
| P1 | F04 Produce Controls UX consolidation | none |
| P2 | F16 employee action layout | none |
| P3 | F19/F20 Business benchmark + labor consistency | one focused pricing recon only if pure quote seam is unclear |
| P4 | F23 diversified equipment packages | direct implementation from known allocator; no architecture tour |
| P5 | F23/Emergency posting performance | measurement-driven; broaden only if P4 does not remove stall |
| P6 | F24 Emergency Job Posting quote persistence/hiring | none; seam already identified |
| P7 | F24 raid-like pod attention letter | one focused vanilla letter/attention recon |
| P8 | integration, pending-playtest cleanup, whole-suite gate | none |

Serialize P2/P6 where they collide with Labor UI/domain files. Do not run parallel workers.

---

# Stage P0 — baseline, scope lock and posting-performance measurement

## Player-facing result / behavioral claim

No player-facing change yet. Establish a trustworthy baseline and measure the observed UI hitch before changing the allocator.

## Locked semantics

- the Emergency Job Posting should still feel immediate;
- several responses appearing together is desirable;
- performance work must not reduce market depth or fabricate applicants;
- no background threading over Verse/RimWorld objects unless a known project-safe seam already exists — assume main-thread game objects are not thread-safe.

## Known files / symbols

Start with:

- `JobPostingService.TryPost`
- emergency immediate-match path in `JobPostingService`
- `ApplyInterested` / applicant materialisation path
- `LaborProspect.Materialise`
- `LaborEquipmentAllocator.TryFulfil`
- existing labor self-tests / timing helpers if any

## Implementation shape / preference

Add **temporary, localized timing instrumentation** sufficient to split one Emergency+Elite posting into at least:

```text
TryPost total
lightweight census/filtering
candidate/application selection
Pawn materialisation
equipment fulfilment
final applicant publication / UI return
```

Use a deterministic representative test/debug fixture if possible. Also capture one real in-game log sample if the existing dev bridge supports it cleanly.

Do not scatter timing code across unrelated systems. Keep it one-file/one-helper or similarly easy to remove.

## Acceptance evidence

Record a baseline for a stress case approximately:

```text
Emergency ON
Equipment Elite
permissive skill requirement
sufficient eligible market prospects
posting queue can fill to its normal cap
```

The point is not a universal benchmark; it is to identify the dominant synchronous cost before optimization.

## Scope boundary

Do not optimize yet. P0 only proves where the time is going and that the base branch still builds/tests.

---

# Stage P1 — F04: one Produce Controls concept everywhere

## Player-facing result / behavioral claim

Every action called **Produce controls** opens the same Produce manager. All Produce-related Architect actions live in `Architect > Production`.

## Locked semantics

### One meaning for `Produce controls`

Both:

```text
object gizmo -> Produce controls
Architect -> Production -> Produce controls
```

open the same `Dialog_ProducePresetManager`-style management surface.

If opened from an object, the manager may receive that object/map/cell as contextual convenience, but it must remain the same conceptual window and action set.

### `New production`

The manager gains a clear:

```text
New production
```

button/action that opens the existing per-production editor/configurator currently reached directly through the object's `Produce controls` gizmo.

Do not clone the editor into a second implementation.

Preferred conceptual flow:

```text
Produce controls
  -> Production manager
       -> New production
            -> existing production editor
```

### Architect location

Move all of these to `Architect > Production`:

```text
Produce / resume
Pause production
Stop production
Produce controls
saved production presets
```

Remove the Produce commands from `Architect > Orders` once Production owns them. No duplicate Orders copy.

### Unavailable assigned worker

Preserve the play-proven behavior:

```text
assigned worker dies/leaves
-> stays visible as "(unavailable)"
-> explicit Unassign removes them
```

Critically, a preset/loop whose selected workers are all unavailable must **not** silently become `Any eligible pawn`.

## Current implementation facts

- XML currently injects four special designators into `Orders`.
- object gizmo opens `Dialog_ProduceControls` directly.
- Architect manager is `Dialog_ProducePresetManager` and already owns Apply/Edit/Rename/Remove.
- preset drag/right-click/save-load are proven in live play and are regression-only.

## Known files / symbols

Start with:

- `Patches/ProduceDesignators.xml`
- Production `DesignationCategoryDef` / related defs created in the previous pass
- `Source/Intercolony/Production/ProduceGizmoPatch.cs`
- `Source/Intercolony/UI/Dialog_ProducePresetManager.cs`
- `Source/Intercolony/UI/Dialog_ProduceControls.cs`
- `ProduceLoopMapComponent`
- `ProducePresetDesignators.cs`
- Produce self-tests

## Implementation shape / preference

Reuse the manager as the single entry surface. Add a narrow optional context object/cell only if it materially improves `New production` flow.

Do not merge manager and editor into one giant dialog; the product decision is **one manager entrypoint**, not one monolithic class.

## Persistence / migration

None expected.

## Acceptance evidence

Automated / structural:

1. object-side `Produce controls` routes to manager, not directly to editor;
2. Architect `Produce controls` routes to same manager class/path;
3. `New production` routes to the existing production editor;
4. `Produce/Resume`, `Pause`, `Stop`, `Produce controls` are registered only under Production;
5. no duplicate Produce special designators remain in Orders;
6. all-unavailable selected-worker state remains restricted;
7. preset bulk application, Edit/Rename/Remove, save/load regressions remain green.

Mutation/negative control:

- changing the object gizmo back to direct editor must fail the entrypoint assertion;
- stripping the worker restriction when every selected pawn is unavailable must fail the worker-gate assertion.

## Human evidence

After the run, one short UI check:

- object and Architect `Produce controls` feel like the same action;
- `New production` is discoverable;
- Production category contains the full coherent toolset and Orders no longer does.

## Scope boundary

Do not redesign Produce loop semantics, preset persistence, target hysteresis, or material/worker rules.

---

# Stage P2 — F16: explicit employee decision groups

## Player-facing result / behavioral claim

The employee card stops using one contextual header slot for unrelated lifecycle decisions. Ending employment remains in the header; renewal and permanent-stay choices live explicitly in the expanded card.

## Locked semantics

### Header / collapsed row

Use the employment-ending action only:

```text
Active worker      -> Dismiss
Travelling worker  -> Cancel
```

Do not put `Not now` or `Let them go` in this header slot.

For states where termination is not valid, preserve stable layout with a disabled/empty slot according to the existing layout conventions rather than repurposing it for another decision.

### Expanded action groups

Organize as:

```text
Renew
Let them go

Keep them
Negotiate
Not now

Pay arrears (when applicable)
```

`Renew` and `Let them go` are the fixed-term renewal pair.

- `Renew` enabled only when `RenewalService.HasLiveOffer(contract)`.
- `Let them go` enabled under the same live renewal offer and calls the existing renewal-decline path; the worker serves the existing term and leaves normally.

`Keep them`, `Negotiate`, `Not now` are the permanent-transition group.

- `Keep them` enabled only when `TransitionService.HasLiveOffer(contract)`.
- `Negotiate` stays in the logical position under `Keep them`; while negotiation is not implemented, it remains disabled with concise tooltip.
- `Not now` is standalone and enabled only for a live permanent-transition offer; it calls the existing decline/reoffer path.

`Pay arrears` remains conditional and functional.

## Current implementation facts

- current action stack already has Renew / Keep them / Negotiate / optional Pay arrears;
- current separate lifecycle slot swaps among Dismiss/Cancel/Not now/Let them go;
- `RenewalService.OfferLeadDays == 5` and Auto-renew can consume a live offer immediately, explaining why `Let them go` is rarely observed manually.

## Known files / symbols

Start with:

- `Source/Intercolony/UI/MainTabWindow_Intercolony_Labor.cs`
- `RenewalService.HasLiveOffer / Accept / Decline`
- `TransitionService.HasLiveOffer / Decline`
- employee-card layout/self-tests

## Implementation shape / preference

Keep one authoritative `EmployeeRowLayout`. Recalculate expanded action slot count/height from the explicit action list; do not hand-position an extra button outside that layout.

## Persistence / migration

None.

## Acceptance evidence

Force state fixtures for:

```text
ordinary active fixed-term
travelling
live renewal offer
live permanent-transition offer
arrears
open-ended active
```

Prove:

- ordinary active header = `Dismiss`;
- travelling header = `Cancel`;
- renewal offer exposes both `Renew` and `Let them go` in expanded stack;
- transition offer exposes `Keep them` + enabled `Not now`;
- no transition offer leaves `Not now` disabled;
- `Negotiate` remains visible but disabled;
- Pay arrears still works;
- no overlap/dead click regions.

Mutation/negative control:

- wiring `Let them go` to immediate dismissal rather than renewal decline must fail;
- making `Not now` decline renewal instead of transition must fail.

## Human evidence

One expanded-card screenshot/readability pass after implementation.

## Scope boundary

Do not redesign renewal economics, auto-renew behavior, transition eligibility, or negotiation mechanics.

---

# Stage P3 — F19/F20: make the Business benchmark useful and prove labor consistency

## Player-facing result / behavioral claim

Material-specific contracts should normally show a defensible current `Median market price` even when the player has not manually created an RFQ for that exact item. Direct labor must remain independent of material itself under the accepted F20 approximation.

## Locked semantics

### Market benchmark

For an **exactly specified product** such as:

```text
Steel Chair
Gold Chair
Silver Chair
```

use market evidence in this order:

```text
1. current exact SupplierListing / open-RFQ Quotation evidence
2. non-mutating indicative supplier pricing from currently eligible suppliers
3. "—" only if no defensible current external price can be produced
```

Do **not**:

- use the player's own contract price as market evidence;
- relabel vanilla generic MarketValue as `Median market price`;
- create a real PurchaseRequest/RFQ merely to populate the Business report;
- mutate inventory, IDs, reputation, market state or global RNG while rendering Business.

For a stuffable contract left at **Any material**, there is no exact stuff specification. Preserve honest ambiguity:

```text
Median market price: —
```

unless the existing procurement model already has a clearly defined generic-any-stuff market benchmark. Do not mix wood/gold/steel into one number merely to avoid a dash.

Prefer a short tooltip such as `Agreement does not specify a material` where that is the reason.

### Paid labor

Material does not change the direct-labor approximation by itself.

For two contracts with:

```text
same ThingDef
same quantity
same cadence
same fulfillment/other terms
same current active workforce
same current set of relevant agreement goods
```

Gold vs Silver stuff must have:

```text
same Paid labor per cycle
same Paid labor per unit
```

while `Materials` may and should differ.

Different cadence may legitimately alter cycle labor. Different quantity may legitimately alter the displayed per-unit figure under the current approximation. Different active relevant goods/workforce may also alter apportionment.

Do not redesign F20 into measured work attribution in this pass.

## Current implementation facts

- exact median currently scans only existing current listings/quotes;
- `EstimateDirectLabor` ignores `stuffDef` and uses relevant workforce + cadence;
- relevant production goods are deduplicated by `ThingDef`;
- tooltips already explain this is an approximation rather than measured time.

## Known files / symbols

Start with:

- `Source/Intercolony/Core/BusinessReportService.cs`
- `TryGetCurrentProcurementMarketMedianUnitPrice`
- `EstimateDirectLabor`
- `Source/Intercolony/UI/MainTabWindow_Intercolony.cs`
- `Source/Intercolony/Market/IntercolonyPricing.cs`
- `Source/Intercolony/Procurement/RfqService.cs`
- `SupplierListing` / `Quotation` generation seams
- Business/market self-tests

## FOCUSED RECON REQUIRED — pure supplier quote seam only

If there is not already an obvious pure helper, dispatch Sol read-only to answer only:

1. What existing pricing path computes what one eligible supplier would charge for an exact `ThingDef + StuffDef + quality` without creating a request or mutating state?
2. Which inputs are deterministic settlement/profile facts and which parts currently consume RNG?
3. What smallest extraction/refactor would allow a **read-only indicative quote** for Business without perturbing real procurement pricing or global RNG?

Do not recon Business broadly. Do not redesign procurement. Stop once the pure/non-mutating quote seam is identified.

## Implementation shape / preference

Prefer extracting/reusing one pure pricing calculation from the authoritative procurement/pricing owner rather than reimplementing supplier pricing inside Business.

For the indicative median:

- one observation per eligible supplier/settlement;
- exact product/stuff/quality semantics;
- deterministic for the current world/refresh;
- no state creation.

## Persistence / migration

None.

## Acceptance evidence

1. exact Steel/Gold/Silver chair contract with eligible suppliers produces a non-dash median without a prior player RFQ;
2. current exact listing/quote evidence still takes precedence as intended;
3. concrete stuff is respected — Gold and Silver benchmarks can differ;
4. `Any material` stuffable contract remains honestly unresolved rather than inventing a stuff;
5. rendering/estimating Business does not change request count, next IDs, supplier inventory/state, reputation, refresh count, or global RNG state;
6. same-term Gold/Silver Chair fixtures have identical direct labor and different direct materials;
7. a deliberate cadence or quantity presentation change behaves according to the documented approximation.

Mutation/negative control:

- substituting generic `MarketValue` must fail a benchmark-source assertion;
- adding `stuffDef` to the labor partition key must fail the material-invariance assertion.

## Human evidence

After implementation:

- inspect a few material-specific contracts and confirm Median market price is normally populated;
- compare otherwise-identical Gold/Silver chairs and confirm only Materials/market economics, not labor purely because of stuff, diverge.

## Scope boundary

Do not change optional material contract semantics. Do not build recursive BOM costing or per-tick labor tracking.

---

# Stage P4 — F23: varied, believable equipment packages instead of tier uniforms

## Player-facing result / behavioral claim

Workers at the same requested equipment tier should look like different people equipped to roughly the same **overall standard**, not clones wearing a fixed tier uniform.

`Standard`, `Professional`, and `Elite` describe a **minimum package-level promise**, not the tier of every individual item and not a requirement to fill every possible slot.

## Locked semantics

### Package-level promise

The final actual loadout remains authoritative:

```text
real generated Things
-> LaborEquipmentTierService.Classify(pawn, clause)
-> actual tier must meet or exceed promised tier
```

Never label a worker Professional/Elite if actual gear does not qualify.

Settlement capability remains the hard gate on **whether a settlement may promise a tier**. Preserve the accepted base-game Elite gate: Industrial-or-better source, Comfortable-or-better wealth, and the stronger Elite capability threshold; do **not** restore a Spacer-only requirement.

After that capability gate passes, the allocator may use the equipment ceiling associated with the **promised tier** to fulfil the promise. Do not confuse this with allowing incapable settlements to promise higher tiers: capability decides *whether* the promise is legal; the promised tier then constrains *which gear pool* may satisfy that legal promise.

Abundance Mod Settings continue to control how often the market promises each tier; they do **not** choose exact items.

Civilian semantics remain: higher equipment tier may improve work/protective apparel but must not hand a civilian an offensive weapon solely because the tier is high.

### Individual-item bands are overlapping inputs, not rigid recipes

Build/use an internal item score/band approximately:

```text
basic / Standard-ish
Professional-ish
Elite-ish
```

Derive it from real def/stat information already used by the equipment system, such as:

```text
tech level
market value
quality
weapon DPS / accuracy / combat usefulness
armor sharp / blunt / heat
coverage
item role/type
```

Do not use a `defName` whitelist as the authoritative classifier. DLC/modded gear should naturally participate when its stats/tech make sense.

### Calibration examples — examples only, not whitelist

Use Core-style examples to sanity-check the score bands.

| Approximate band | Example flavor — not authoritative item list |
|---|---|
| Basic / Standard-ish | tribalwear, pants, shirts, ordinary dusters/parkas, simple helmets; bows, autopistols, revolvers, machine pistols, basic shotguns/rifles |
| Professional-ish | flak vest/pants/jacket/helmet, better protective/work clothing; SMGs, assault rifles, LMGs, chain shotguns, stronger conventional rifles, high-quality ordinary firearms |
| Elite-ish | recon/marine/high-tech protection where source capability allows; charge/advanced weapons where allowed; exceptional-quality Professional-class equipment; other high-stat advanced gear |

The examples exist to catch absurd scoring, not to freeze selection to these names.

### Overlapping package composition

A **Standard** worker:

- is mostly basic/common gear;
- may be incomplete;
- may occasionally have one Professional-ish item;
- is not required to be fully covered in mediocre gear.

A **Professional** worker:

- is usually a mix of Standard-ish and Professional-ish gear;
- should often have several useful pieces, but not necessarily a full flak-style uniform;
- may occasionally have one Elite-ish piece;
- may still have a mundane filler piece.

An **Elite** worker:

- is usually a mix of Professional-ish and Elite-ish gear;
- should clearly read above average overall;
- is **not** guaranteed full best-in-slot armor + best weapon + best helmet;
- may carry Professional filler and occasionally a weaker mundane piece while still satisfying the overall Elite package.

### Quality and completeness also vary

Do not always use the same quality.

A Good/Excellent lower-tech item may compete with a Normal higher-tech item depending on the package score. A worker can qualify through one exceptional item plus decent support gear or through several consistently good items.

### Special-purpose/disposable gear

Single-use launchers, rockets and unusually specialized weapons must not become the default primary weapon for every Professional worker merely because their stats/value happen to score well.

Prefer a generic rarity/selection penalty derived from item role/comps/tags/usage characteristics where possible. Do not solve this with a hardcoded `rocketswarm` blacklist unless vanilla exposes no usable semantic signal and focused evidence justifies a very narrow exception.

### Stable variation

Use deterministic seeded variation based on stable prospect/source/market identity so that:

- different prospects tend to produce different packages;
- the same applicant does not reroll because a window redraws;
- save/load preserves the already-materialized applicant's actual gear;
- tests remain reproducible.

Avoid the current pattern of globally sorting candidates and accepting the same first minimum-passing package for every similar pawn.

## Current implementation facts

- current allocator scans definitions by quality, sorts candidates deterministically, constructs candidate apparel plans, then walks weapon × apparel combinations until the first package passes;
- actual classification and bond pricing already use the real generated gear and must remain authoritative;
- the current design is def-driven and mod-friendly in principle; preserve that strength while changing selection strategy.

## Known files / symbols

Start with:

- `Source/Intercolony/Labor/LaborEquipmentAllocator.cs`
- `Source/Intercolony/Labor/LaborEquipmentTierService.cs`
- `Source/Intercolony/Labor/LaborProspect.cs`
- `Source/Intercolony/Labor/LaborCandidateService.cs`
- `Source/Intercolony/Labor/JobPostingService.cs`
- `IntercolonySettings` / `IntercolonyMod` only for abundance regression
- labor self-tests

## Implementation shape / preference

Prefer a **cached def-driven equipment catalogue + bounded weighted package assembler**.

The catalogue may precompute stable expensive facts such as:

```text
eligible def/stuff families
tech
role
base stat strength
armor/weapon score inputs
market value
special-purpose penalty
```

Then per applicant:

```text
select eligible pools under source capability
-> weighted seeded sample
-> assemble a bounded package
-> instantiate real Things
-> classify actual result
-> bounded retry if below promised tier
```

Do not pre-materialize hundreds of pawns. Do not turn F23 into a player equipment shop.

## Persistence / migration

No new persisted equipment-plan record is required if the applicant's actual pawn/gear is materialized once and already persists.

Transient catalogue/cache should rebuild from loaded defs and must not become authoritative save state.

## Acceptance evidence

Use deterministic fixtures that generate a meaningful sample — enough to detect cloning, not to enforce exact RNG aesthetics.

Prove:

1. every accepted applicant meets/exceeds promised tier by the existing classifier;
2. Standard/Professional/Elite remain constrained by source capability;
3. civilian high tiers do not gain weapons solely because of tier;
4. equipment bond equals actual delivered gear;
5. several dozen generated Professional applicants produce multiple distinct package signatures when the loaded defs provide alternatives;
6. several dozen Elite applicants likewise produce meaningful package diversity;
7. the allocator does not collapse to the same weapon/apparel signature for nearly every worker under an ordinary rich Core-like pool;
8. mixed-band packages occur: e.g. Professional package can contain a Standard-ish piece, Elite package can contain Professional-ish filler, without violating final tier;
9. high-tier applicants are not systematically fully best-in-slot;
10. existing abundance sliders still change **market tier abundance**, not exact gear selection.

Use broad diversity thresholds rather than brittle exact item expectations.

Mutation/negative control:

- force deterministic `first passing package` selection and ensure diversity evidence goes red;
- bypass final `Classify` check and ensure tier-validity evidence goes red.

## Human evidence

After the run, inspect a pageful of applicants across Standard/Professional/Elite and judge whether they look varied and believable rather than like three uniforms.

## Scope boundary

Do not expose exact item-selection UI. Do not add bionics. Do not change F23 market abundance targets in this pass unless the new allocator demonstrably makes an existing tier impossible to fulfill.

---

# Stage P5 — F23 / Emergency Job Posting performance: remove the click freeze

## Player-facing result / behavioral claim

Clicking `Post` for an Emergency + high-equipment job must not visibly freeze the game for ~1–1.5 seconds. The desirable behavior — several emergency responses appearing quickly — must remain.

## Locked semantics

- keep the deep lightweight census;
- keep the normal visible applicant queue cap;
- keep immediate emergency matching as a product behavior;
- do not fake responses or reduce equipment validation;
- do not use unsafe background threads over game objects.

## Current implementation facts

- the census itself is intentionally lightweight;
- real Pawn generation is the expensive boundary;
- F23 equipment fulfilment currently performs broad definition/quality/package work during applicant materialisation;
- P0 records baseline timings; P4 should already remove a large share of repeated allocator work through caching/bounded sampling.

## Implementation shape / preference

### First choice — make synchronous work cheap enough

Use the P4 catalogue/cache and bounded sampling so applicant materialisation no longer rescans/sorts the loaded equipment universe repeatedly.

Re-run P0 timings.

### Second choice — amortize materialisation if still necessary

If real Pawn generation/materialisation still causes a perceptible one-frame stall after P4, preserve immediate **matching** but amortize heavy materialisation across subsequent safe game/UI ticks.

Conceptually:

```text
Post click
-> write posting immediately
-> cheap census match immediately
-> queue selected prospect records for applicant materialisation
-> return UI
-> materialise a bounded amount per subsequent safe tick
-> publish the batch of responses together once ready (or within a very short searching state)
```

A tiny `Searching...` state is acceptable. A frozen UI is not.

Do not make persisted half-applicants with ambiguous pawn ownership unless necessary. Prefer transient pending-materialisation state that is reconstructable from the open posting/census if the game saves mid-search; if saveability becomes a real issue, choose the simplest consistent model and test it.

## Known files / symbols

Start with:

- P0 timing seam
- `JobPostingService.TryPost`
- emergency immediate-match helper
- `ApplyInterested`
- `LaborProspect.Materialise`
- P4 equipment catalogue/allocator
- world/hourly/tick owner already used for bounded Intercolony work, if amortization is necessary

## Acceptance evidence

Before/after timing must be recorded for the same deterministic stress fixture.

Minimum acceptance:

- no single synchronous step in the click path remains in the same severe hundreds-of-ms-to-second range as the observed defect;
- Emergency+Elite posting returns control without a human-visible freeze comparable to baseline;
- the same number/quality of valid applicants eventually appears;
- no pawn leak, duplicate pawn, duplicate application or stale pending job is introduced;
- ordinary non-emergency postings remain correct;
- save/load during any pending search/materialisation state is safe if such a state exists.

Mutation/negative control:

- disabling the cache/bounded path should measurably regress the benchmark or trip a guard designed to detect repeated catalogue rebuilds.

## Human evidence

One live click-feel check on Emergency + Elite after the run.

## Scope boundary

Do not reduce census depth merely to make the benchmark green. Do not change queue size. Do not remove immediate emergency matching.

---

# Stage P6 — F24: freeze and honor Emergency Job Posting arrival quotes

## Player-facing result / behavioral claim

An applicant who answers an **Emergency Job Posting** must arrive using the emergency method/ETA shown in the application, not ordinary multi-day travel after the player clicks `Take on`.

## Locked semantics

### Emergency route bands

Preserve the current product rule:

```text
Drop pod emergency:       1–4 in-game hours
Conventional emergency:   5–9 in-game hours
```

Distant conventional settlements that cannot satisfy the emergency ground threshold remain ineligible. Preserve the current **12-tile** threshold in this pass even though the previous real-world measurement found 0 qualifying conventional routes in 748 examined cases; this stage fixes quote preservation/arrival correctness, not availability balance.

### Freeze the quote at application time

For an Emergency posting, when a prospect becomes a `JobApplicant`, freeze the exact quote shown to the player:

```text
transport
arrival duration/ticks
method label or derivable route label
source settlement
open-market/emergency ask already shown
```

Applicant UI reads this frozen quote.

At `Take on`, `EmploymentService.TryHireApplicant(...)` uses **that same quote** to build:

```text
arrivalTick
arrivalTransport
charged wage/premium already quoted
```

Do not recalculate route/ETA after payment.

### Ordinary posting

Ordinary non-emergency posting applicants continue using ordinary `travelDays` semantics.

## Current implementation facts

- Emergency posting filtering/matching exists and works;
- the applicant does not currently store emergency transport/ticks;
- `TryHireApplicant` currently uses `applicant.travelDays * TicksPerDay` and therefore drops the emergency route;
- direct Market hire already has the correct quote-to-contract pattern; reuse that authority rather than inventing a second emergency model.

## Known files / symbols

Start with:

- `Source/Intercolony/Labor/JobPosting.cs` (`JobApplicant`, `JobPosting`)
- `Source/Intercolony/Labor/JobPostingService.cs`
- `Source/Intercolony/Labor/LaborCandidateService.cs` (`EmergencyArrivalQuote`)
- `Source/Intercolony/Labor/EmploymentService.cs` (`TryHire`, `TryHireApplicant`)
- applicant UI in `MainTabWindow_Intercolony_Labor.cs`
- labor save/load self-tests

## Implementation shape / preference

Prefer reusing `EmergencyArrivalQuote` semantics and one helper that converts/fills a persisted applicant quote.

Do not store a second unrelated set of route constants on `JobApplicant`.

Do not make `TryHireApplicant` call the ordinary travel-day path when `posting.emergencyDispatch` is true.

## Persistence / migration

Follow section 4. Expected schema: **60** unless an equivalent safe migration mechanism already exists.

Prove:

- emergency applicant quote survives save/load before acceptance;
- accepting after reload preserves exact route/timing;
- accepted travelling contract survives save/load and arrives exactly once;
- old emergency applicants are migrated/repaired safely rather than silently reverting to ordinary travel.

## Acceptance evidence

1. Emergency posting + pod-capable source shows a 1–4h Drop pod quote;
2. `Take on` creates contract with that exact transport/duration;
3. Emergency posting + close conventional source shows 5–9h Emergency caravan;
4. `Take on` creates corresponding conventional emergency contract;
5. ordinary posting still uses ordinary multi-day `travelDays`;
6. previewed emergency premium equals charged wage logic;
7. save/load applicant then accept -> exact same quote;
8. save/load accepted travelling employee -> arrives once via correct route;
9. no emergency applicant can be accepted into a 2-day/7-day ordinary arrival without an explicit migration/error path.

Mutation/negative control:

- restoring `applicant.travelDays * TicksPerDay` for an emergency applicant must fail;
- dropping `arrivalTransport` must fail the pod/conventional route assertions.

## Human evidence

No need to re-prove the physical pod itself in this stage; it was already seen. Human retest should confirm a Job Posting emergency hire now uses hours rather than days.

## Scope boundary

Do not alter the direct Market emergency product except shared-helper refactoring needed to keep both paths identical.

---

# Stage P7 — F24: raid-like attention for inbound emergency pods

## Player-facing result / behavioral claim

When an emergency employee pod is launched, the player gets the same practical attention affordance expected from an important vanilla drop-pod event:

```text
pod enters map
-> game pauses
-> inbound letter appears
-> Jump to location is enabled
-> jump points to the actual incoming pod / landing site
-> game remains paused
-> player resumes and can watch the pod descend
```

The physical pod transport itself is already play-proven and should not be replaced without evidence.

## Locked semantics

- letter must arrive while the pod is still meaningfully inbound, not after landing;
- the game pauses automatically;
- `Jump to location` must be enabled;
- target must resolve to the real incoming pod/skyfaller when possible, with an exact valid landing-cell target only as a vanilla-correct fallback;
- source settlement and worker remain named in the letter;
- do not create a second decorative pod or pre-spawn the pawn;
- employee activates only through the existing actual landing lifecycle.

## Current implementation facts

- `DropPodUtility.MakeDropPodAt(...)` is physically working;
- current code attempts `ThingAt<DropPodIncoming>(cell)` then a cell `LookTargets` fallback;
- live play says the resulting letter jump control is disabled;
- current `PositiveEvent`/letter path does not pause the game.

## Known files / symbols

Start with:

- `EmploymentService.LaunchDropPodArrival`
- `IntercolonyLetters.Send`
- current `LookTargets` construction
- vanilla 1.6 raid/drop-pod letter/arrival code only as needed below

## FOCUSED RECON REQUIRED — vanilla attention/letter behavior only

Dispatch Sol read-only to answer:

1. What exact vanilla 1.6 path makes an important raid/drop-pod arrival pause the game?
2. Is pause behavior a `LetterDef`/letter property, an explicit tick-manager call, or incident logic surrounding the letter?
3. What exact target type/object does vanilla pass so `Jump to location` is enabled for incoming pod/skyfaller events?
4. At what point relative to skyfaller creation does vanilla send the letter so the target is valid and the pod is still visible inbound?

Inspect only the relevant vanilla raid/drop-pod/letter seams. Do not recon transport pods broadly. Return the smallest pattern Intercolony should mirror, then stop.

## Implementation shape / preference

Mirror vanilla's attention pattern rather than inventing custom camera behavior.

If vanilla uses an explicit pause plus ordinary letter, reproduce that narrowly. If it uses a particular LetterDef or target wrapper, use that pattern. Do not globally change `IntercolonyLetters` semantics for unrelated letters unless the cleanest implementation is an explicit opt-in parameter.

## Persistence / migration

None expected. Letter attention is event presentation; the contract/pod state already persists elsewhere.

## Acceptance evidence

Automated evidence can prove:

- launch produces a valid non-empty look target tied to the created skyfaller/landing location;
- the chosen attention path invokes the same pause/letter mechanism as the focused vanilla pattern;
- repeated `Advance` does not duplicate letters or pods;
- fallback target remains valid if the exact skyfaller lookup timing differs.

Do **not** claim visual success from these tests.

## Human evidence — mandatory before release, but not a blocker for clean development halt

1. hire/accept one emergency pod worker;
2. letter fires while inbound;
3. game is paused automatically;
4. `Jump to location` is enabled;
5. click it and camera lands on the incoming pod/landing site;
6. game remains paused;
7. resume and visibly watch the pod descend;
8. worker activates from that pod exactly once.

## Scope boundary

Do not redesign general Intercolony letter volume or all event pausing. This behavior is specific to premium emergency pod arrival.

---

# Stage P8 — integration, documentation and final gate

## 8.1 Cross-stage integration scenarios

### Produce manager consistency

1. select a configured chair -> `Produce controls`;
2. open Architect > Production -> `Produce controls`;
3. both expose the same manager behavior;
4. `New production` reaches the same editor;
5. saved preset paints multiple targets;
6. right-click Edit/Rename/Remove still works;
7. unavailable assigned worker remains explicitly unavailable and does not widen eligibility.

### Employee decision separation

Exercise/fixture:

```text
ordinary active
travelling
renewal offer
transition offer
arrears
```

Verify ending-employment actions remain in header while renewal/transition choices remain in expanded groups.

### Business exact-spec economics

Create otherwise-identical:

```text
Gold Chair contract
Silver Chair contract
```

with same quantity/cadence/workforce.

Verify:

```text
Materials differ as expected
Median market price resolves independently for exact stuff
Paid labor per cycle is identical
Paid labor per unit is identical
```

Then change only cadence/quantity and confirm any display difference follows documented F20 semantics rather than stuff.

### F23 diversity + performance

With representative capable sources:

1. post Standard / Professional / Elite jobs;
2. sample several applicants per tier over deterministic refresh fixtures;
3. all actual packages satisfy tier;
4. package signatures are meaningfully varied;
5. Professional and Elite are not fixed uniforms;
6. bond matches actual gear;
7. posting Emergency + Elite does not recreate the severe click freeze.

### F24 posting -> arrival

Exercise both:

```text
Emergency Job Posting -> Drop pod applicant -> Take on
Emergency Job Posting -> close conventional applicant -> Take on
```

Verify quoted route/ETA == contract route/ETA == actual arrival pipeline.

Also verify ordinary Job Posting remains ordinary multi-day travel.

## 8.2 Regression checks

At minimum:

- F06 apparel policy/bond targeted suite;
- F19 optional material contract semantics;
- F20 relevant-workforce economics outside the narrow invariant correction;
- F21 rapid-logistics capability;
- F23 abundance settings / capability gates, including the accepted non-Spacer Elite gate and promise-tier equipment ceiling distinction;
- direct Market emergency hire;
- physical pod landing path;
- Produce target/resume/pause/stop behavior;
- F12/F22 remain untouched/frozen.

## 8.3 Pending playtest documentation cleanup

Update `docs/PENDING_PLAYTESTS.md` to reflect evidence already produced by the operator.

Move/mark as proven in play where appropriate:

- F04 multi-object drag;
- F04 Edit/Rename/Remove;
- F04 preset save/load;
- F04 unavailable dead/left worker display/removal behavior;
- F16 Dismiss;
- F16 Cancel;
- F16 Not now;
- F19 optional material selector/material-cost resolution behavior observed;
- F24 physical visible drop-pod descent.

Do **not** claim as human-proven yet:

- final F04 consolidated manager UX;
- F16 `Let them go` in live play;
- final material-specific indicative median benchmark;
- final F23 diversified loadout feel;
- final F23 posting performance feel;
- fixed F24 Job Posting route/ETA;
- fixed F24 pause + Jump to location attention flow;
- latest conventional emergency 5–9h live observation.

Retire/update stale text claiming nobody has ever seen an employee pod descend; that is now false.

## 8.4 Remove temporary diagnostics

Temporary P0 timing instrumentation must be removed/disabled from release-path production code unless it is deliberately retained as lightweight debug-only tooling.

If retained, it must not spam ordinary logs or add material runtime cost.

---

# 6. Final acceptance gate

Before clean halt:

1. targeted tests for every changed stage;
2. mutation/negative-control evidence where specified;
3. schema migration/save-load tests for emergency applicant quote state;
4. clean build with zero unexpected errors;
5. project-standard fresh whole suite — `dev.ps1 test all -Fresh` or current canonical equivalent;
6. exit code `0`;
7. no new skips added merely to obtain green;
8. inspect final logs for new unexpected exceptions/errors;
9. run P8 integration scenarios that are automatable;
10. update `docs/PENDING_PLAYTESTS.md` honestly;
11. update Foreman/project durable run status;
12. push every accepted commit;
13. branch remote aligned (`0 ahead` after push);
14. working tree clean except documented pre-existing operator files and intentionally untracked/regenerable Graphify derived output as defined in baseline §3.7;
15. Graphify derived output remains untracked and is not committed merely to satisfy a clean-tree check;
16. no merge/tag/release/Workshop action.

Human-only visual evidence may remain pending at clean halt if implementation and automated prerequisites are complete. Record it exactly; do not pretend it passed.

---

# 7. Clean-halt report

Report once, at actual clean halt:

```text
Branch:
Base commit:
HEAD:
Remote/push state:
Working tree:
Save schema:
Build result:
Whole-suite result:
Log signal:

Implemented:
- P1 F04 Produce Controls UX consolidation
- P2 F16 explicit employee action groups
- P3 F19/F20 Business benchmark/labor consistency
- P4 F23 diversified equipment packages
- P5 posting performance correction
- P6 F24 Emergency Job Posting quote persistence/hiring
- P7 F24 pod attention letter

Performance:
- before timing
- after timing
- dominant cost before
- final synchronous click-path cost / amortization behavior

Regression checked:
- F06
- F19 material semantics
- F20 core approximation
- F21
- F23 abundance/capability
- direct Market F24
- Produce existing behavior

F12/F22:
- confirm frozen and untouched

Human evidence already incorporated into docs:
- ...

Human evidence still required:
- final F04 manager/category UX
- F16 Let them go/readability if still unseen
- F19 median benchmark live read
- F23 loadout variety + click feel
- F24 Job Posting hours-scale arrival
- F24 pause + working Jump to location + visible descent
- any other exact item honestly still pending

Non-blocking defects discovered:
- ... or none

Meaningful deviations from plan:
- ... with evidence/reason, or none

Operator-reserved actions NOT performed:
- no merge to main
- no tag
- no GitHub release
- no Workshop update
```

---

# 8. Definition of success

This pass is successful when all of the following are true:

- `Produce controls` is one coherent manager concept everywhere and all Produce Architect tools live under Production;
- employee-card actions are semantically grouped rather than swapped through one contextual button;
- exact material-specific selling agreements normally have a defensible current market benchmark without requiring the player to manually seed an RFQ;
- generic `Any material` agreements remain honestly ambiguous rather than receiving fabricated precise material/market numbers;
- Gold/Silver/etc. material choice alone does not change direct labor under the existing F20 model;
- Standard/Professional/Elite workers look like varied people at different overall equipment standards, not three repeated uniforms;
- actual gear always satisfies the advertised tier and settlement capability remains authoritative;
- F23 equipment generation no longer performs wasteful repeated full-catalog/package search per applicant;
- posting Emergency + Elite no longer causes the severe visible UI freeze;
- Emergency Job Posting applicants arrive on the emergency route/ETA shown before hire;
- drop-pod arrival pauses the game and provides a working Jump to location pointing at the actual inbound pod/landing site;
- previously proven physical pod landing remains intact;
- save/load is safe across the new emergency applicant quote state;
- F12 and F22 remain frozen;
- the full suite is green and the branch halts cleanly without release actions.
