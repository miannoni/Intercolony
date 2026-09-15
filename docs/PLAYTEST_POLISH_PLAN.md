# Intercolony — Playtest Polish Correction Plan

**Repository:** `miannoni/Intercolony`  
**Base branch:** `foreman/playtest-finalization-2026-09-13`  
**Audited base HEAD:** `04bd776`  
**Current save schema:** `59`  
**Execution mode:** Claude Code + Agent Foreman  
**Purpose:** correct the first human-playtest findings from the finalization run without reopening the completed batch or broadening into unrelated backlog work.

---

# 0. Authority and scope

This plan is the newest product direction for the corrections below:

- **F04** — reusable Produce presets + Architect bulk application
- **F16** — replace the employee-card `...` menu with a direct contextual lifecycle button
- **F19** — allow optional material/stuff specificity on stuffable selling agreements
- **F23** — make requested equipment tiers exist at useful frequencies in the underlying labor market
- **F24** — make emergency hiring route-specific (1–4h pod / 5–9h conventional), visibly arrive by pod where applicable, and work from Job Postings

Explicitly accepted as good and therefore **regression-only**:

- **F06** apparel policies / bond consent
- **F20** direct labor / economics
- **F21** settlement rapid-logistics capability

Still frozen:

- **F12** recurring/preprogrammed player caravans
- **F22** player-supplied labor

Do not implement the two unrelated defects already recorded at the end of the previous run unless one blocks this correction plan:

- `PurchaseOrderService.DeliverToColony` partial-delivery completion defect
- recurring contract destination becoming inaccessible

Do not perform release actions.

---

# 1. Foreman / recon discipline

This is a correction pass over code that was just implemented. Do **not** start with a repo-wide Sol recon.

For every stage:

1. inspect the exact files named below;
2. reproduce the observed behavior where practical;
3. use focused recon only for the explicitly marked unknowns;
4. implement the narrow product correction;
5. run targeted tests + mutation/negative-control where useful;
6. commit before moving to the next stage.

Broaden only if the named seam materially differs from this audited branch.

A focused recon should answer a concrete API/seam question, not re-explain the feature or redesign it.

---

# 2. Branch and baseline

Start from the completed finalization branch, **not `main`**, because the relevant F04/F06/F16/F19/F20/F21/F23/F24 implementation has not been merged yet.

Recommended:

```text
git switch foreman/playtest-finalization-2026-09-13
git pull --ff-only
git switch -c foreman/playtest-polish-2026-09-14
```

Before edits:

```text
git status
git log -1 --oneline
dotnet build
```

Expected base:

```text
04bd776
schema 59
whole suite previously 1674 / 0 / 19
```

If HEAD has advanced only through operator-approved continuation commits, record the actual base and continue. Do not reset valid work.

---

# 3. Product assumptions locked for this plan

These resolve small edge cases so the run does not stop for ordinary product interpretation.

## 3.1 Produce presets are map-local saved templates

A named Produce preset belongs to the current map/colony because worker assignment contains map-specific pawn references.

It is saved with `ProduceLoopMapComponent`, alongside the loops it configures.

No global export/import library is required.

## 3.2 Preset material compatibility

A preset stores its allowed material set.

When applying it to a target product:

- intersect the preset's materials with stuffs that can actually make that target `ThingDef`;
- if at least one compatible stuff remains, apply the preset using that compatible set;
- if a stuffable target has **zero** compatible preset materials, do **not** silently substitute a different material; reject/skip that target and give one concise player-facing message after the drag operation.

This prevents a preset called `Steel Furniture` from silently becoming wood on an incompatible object.

## 3.3 Employee contextual button owns lifecycle actions, not debt payment

Removing `...` must not remove the ability to pay arrears.

The collapsed-card contextual button displays the currently relevant **lifecycle** action:

- `Not now` for a live stay/permanent-transition offer
- `Let them go` for a live renewal offer that the player wants to decline
- `Cancel` while the worker is still travelling
- `Dismiss` for an ordinary active worker when no higher-priority offer is awaiting a response

Arrears payment remains available through the expanded employee detail / existing debt-payment surfaces, not through a hidden overflow menu.

## 3.4 Equipment-tier targets describe the underlying market, not the per-posting queue

Do **not** interpret the requested counts as "show 8–10 applicants on one posting".

The existing waiting-applicant cap may remain small (currently 6) if that remains the cleanest UX. The balancing target is the **underlying labor market/census** available in a representative ordinary world.

For a permissive requirement such as:

```text
Shooting 1+
normal term
neutral/good employer standing
accessible ordinary world
```

the intended underlying market shape at **default mod settings** should be approximately:

```text
Standard-or-better      many workers
Professional-or-better  ~8–10 workers
Elite                    ~2–4 workers
```

The point is that requesting equipment must narrow the market without making a valid category effectively unusable. A permissive Professional or Elite posting should normally have somebody to answer; if Elite naturally produces zero in ordinary representative worlds, the category is functionally dead and the balance is wrong.

These are market-shape targets, not a rule to synthesize arbitrary pawns for impossible combinations. A highly restrictive request such as `Shooting 20 + Elite + Emergency` may still legitimately return nobody.

### Player-tunable equipment abundance

Expose the equipment-market distribution in **Mod Settings**. Add three independent abundance controls for newly generated labor-market prospects:

```text
Labor equipment abundance
Standard      100%
Professional  100%
Elite         100%
```

Recommended implementation semantics:

- each setting is a multiplier on the baseline weight/chance of assigning that **exact promised tier** to an otherwise capable prospect;
- default is `100%`, preserving the market-shape targets above;
- recommended range is `0%–300%`, with a practical step such as `10%`;
- `0%` means that exact tier is not newly assigned by the market generator, while higher tiers may still satisfy lower-tier job requests in the normal way;
- `Any` is a wildcard request, not an equipment tier and gets no abundance slider;
- `None` is a request mode / residual no-bondable-gear outcome, not one of the player-tuned equipment-quality tiers;
- the multiplier must **not** bypass settlement tech/wealth/archetype capability gates. Cranking Elite abundance must not make a tribal or otherwise incapable settlement produce Elite workers;
- changing a setting affects prospect generation on the **next labor-market/census refresh**. Do not rewrite already-waiting applicants, active contracts, or previously generated pawn gear.

The settings control abundance, not exact counts. World composition, employer standing, accessibility and skill filters still shape the realized market.

## 3.5 Emergency transport has two distinct timescales

Emergency dispatch must be useful on an in-game tactical timescale, and the transport method should matter visibly.

**Drop-pod emergency arrival:**

```text
1–4 in-game hours
```

- only from settlements capable of drop-pod dispatch;
- deterministic variation inside the range;
- visible vanilla-style pod descent is mandatory;
- an inbound letter must identify the source and jump to the actual landing location.

**Conventional emergency arrival:**

```text
5–9 in-game hours
```

- reserved for sufficiently close sources that can plausibly provide a rushed ground/caravan arrival;
- should generally feel centered around ~7h rather than matching pod speed;
- it is still an emergency service, not the normal multi-day labor-travel clock.

Anything materially slower than this is not an emergency option and should not be surfaced/accepted as emergency dispatch.

Do not use one shared hardcoded ETA for both routes, and do not retain the old `<= 2 days` rule.

---

# 4. Stage C0 — baseline and scope lock

1. Create the polish branch from the finalization branch.
2. Record this plan as the current Foreman run.
3. Build once.
4. Run narrow baseline tests for Produce, Labor, Contract/Business before edits if practical.
5. Confirm current save schema is 59.
6. Record the following disposition:

| Finding | This run |
|---|---|
| F04 | expand with named presets + Architect bulk assignment |
| F06 | regression-only |
| F16 | contextual direct action button |
| F19 | optional contract stuff/material selection |
| F20 | regression-only |
| F21 | regression-only |
| F23 | rebalance/fix underlying equipment-tier labor market |
| F24 | route-specific emergency timing + visible pod arrival + emergency job posts |
| F12 | frozen |
| F22 | frozen |

Then continue automatically.

---

# 5. Stage C1 — F04: named Produce presets and bulk application

## 5.1 Problem

The current Produce Controls implementation is useful but still object-by-object.

Current audited shape:

- `ProduceLoopRecord` stores the complete per-object program;
- `Dialog_ProduceControls` edits one loop by map + cell;
- `ProduceLoopMapComponent` owns all map loops;
- Architect > Orders already has `Produce / resume`, `Pause production`, `Stop production` through `ProduceDesignators.xml`;
- no reusable preset abstraction exists.

The correction is to make Produce Controls usable like reusable production blueprints.

## 5.2 Required player experience

There are three access paths to the same preset system.

### A. Existing object-level Produce Controls popup

Keep the current popup and controls.

Add at the top:

```text
Preset name: [________________]
[Save as preset]
```

If the loop was originally applied from a preset, show that relationship as informational UI if cheap, but the loop must remain an independent copy after application.

Saving creates/updates a named preset from the current program settings.

Do **not** keep live references where editing a preset unexpectedly rewrites every existing loop already using it. Applying a preset copies its settings into each loop.

### B. Architect > Orders

Add a generic `Produce controls` action alongside:

- Produce / resume
- Pause production
- Stop production

This generic action opens a compact preset picker/manager so the player can:

- create a new preset;
- select a preset and enter apply mode;
- edit/rename/remove presets.

It should not require selecting an existing furniture object first.

### C. New Architect category: `Production`

Add a new Architect category named **Production**.

Each saved preset appears as its own designator/action in this category.

For a preset action:

- **left click** activates a drag designator;
- drag over eligible objects applies that preset to all eligible objects touched;
- objects without a Produce loop get Produce enabled and configured in one action;
- objects with an existing Produce loop get their program settings replaced by the preset;
- existing product identity/cell/rotation/style remain target-specific;
- **right click** on the preset action exposes:
  - `Edit`
  - `Rename`
  - `Remove`

No Export action.

## 5.3 Preset model

Create a dedicated saved map-level record, conceptually:

```text
ProduceControlPreset
  int id
  string name
  int targetCount
  int resumeBelow
  bool restrictToSelectedWorkers
  List<Pawn> allowedWorkers
  int minConstructionSkill
  List<ThingDef> allowedStuff
```

Do **not** persist:

- cell
- rotation
- product `ThingDef`
- style
- current `waitingForResume` latch
- paused state

Those belong to the concrete loop or runtime state, not the reusable template.

Preset names must be unique on the map after trim/case-insensitive comparison. If the player duplicates a name, ask to overwrite or require a different name; do not create visually identical duplicates silently.

`ProduceLoopMapComponent` should own:

```text
List<ProduceControlPreset> presets
nextPresetId
```

Map save compatibility:

- old save with no preset collection -> empty preset list;
- existing loops remain unchanged.

No world-schema bump solely for this map-component addition.

## 5.4 Applying a preset

Create one authoritative application method in the map component/service, conceptually:

```text
TryApplyPreset(IntVec3 cell, ProduceControlPreset preset, out string reason)
```

It should:

1. resolve the target through existing `ProduceSubjectUtility` logic;
2. create the Produce loop if needed;
3. copy target/resume/worker/min-skill settings;
4. calculate valid material intersection;
5. preserve target-specific product/rotation/style;
6. reset `waitingForResume` based on current stock rather than copying another object's latch;
7. leave Pause/Stop semantics unchanged.

Bulk designator must call this same method for every cell.

Do not duplicate preset application logic between popup and Architect designator.

## 5.5 Drag behavior

Reuse the current `Designator_ProduceBase` / `Designator_Cells` shape.

A preset designator should accept a cell when:

- a valid player-owned Produce subject exists there;
- preset can produce a valid configuration for that target.

During one drag:

- apply to all valid unique targets;
- skip duplicate cells/objects;
- collect failures by reason;
- after drag, show at most one concise summary message such as:

```text
Applied “Steel Furniture” to 12 objects. 2 skipped: no compatible allowed material.
```

Do not produce one message per cell.

## 5.6 Dynamic Architect category

### FOCUSED RECON REQUIRED

Inspect only the RimWorld 1.6 Architect/designator registration path needed to answer:

1. What is the narrowest supported way to expose **dynamic runtime designators** from a saved list in a `DesignationCategoryDef`?
2. What command/designator hook supports a right-click float menu on an Architect entry?
3. If the locally installed Blueprints mod source is available, inspect only its dynamic Architect-entry / right-click management pattern for UX reference. Do not add a dependency and do not copy unrelated architecture.

Stop recon once those three questions are answered.

Preferred implementation:

- one `Production` category;
- runtime designators generated from current map presets;
- no XML def generated per player preset;
- no external mod dependency.

## 5.7 Known files

Start with:

- `Source/Intercolony/Production/ProduceLoopRecord.cs`
- `Source/Intercolony/Production/ProduceLoopMapComponent.cs`
- `Source/Intercolony/Production/ProduceDesignators.cs`
- `Source/Intercolony/Production/ProduceGizmoPatch.cs`
- `Source/Intercolony/UI/Dialog_ProduceControls.cs`
- `Patches/ProduceDesignators.xml`
- Produce self-tests

Add narrow files for:

- preset record;
- preset manager/picker dialog if necessary;
- preset designator/runtime Architect registration.

## 5.8 Acceptance tests

Automated/model tests:

1. old map save with loops and no presets loads unchanged;
2. create preset from existing loop;
3. preset round-trips save/load including selected workers;
4. applying preset to 10 compatible chairs creates/configures 10 independent loops;
5. editing preset afterward does not mutate those 10 existing loops;
6. applying again intentionally updates them;
7. target/resume/min-skill/worker settings copy exactly;
8. waiting latch is not copied from source loop;
9. valid material intersection applies correctly;
10. zero compatible materials rejects target without corrupting its current loop;
11. rename preserves preset ID and action behavior;
12. remove deletes Architect action but does not delete existing loops created from it;
13. Pause/Resume/Stop still work on preset-applied loops;
14. individual object-level Produce Controls still work.

Human UI evidence:

- create preset from popup;
- see it under Architect > Production;
- drag over a room of chairs/tables;
- verify all are configured;
- right-click -> Edit/Rename/Remove;
- generic Architect > Orders `Produce controls` can create/manage/apply presets without selecting one chair first.

---

# 6. Stage C2 — F16: remove `...`, use the direct contextual lifecycle button

## 6.1 Product result

The employee card no longer has a `...` button.

The card already expands for details, so hidden overflow for the primary lifecycle decision is unnecessary.

Replace the menu region with one direct button whose label/action reflects the current lifecycle state.

Priority order:

1. live permanent/stay transition -> `Not now`
2. live renewal offer -> `Let them go`
3. travelling -> `Cancel`
4. otherwise dismissable active employee -> `Dismiss`
5. no applicable lifecycle action -> no active button / disabled space as layout requires

The button must directly invoke the same existing service path the old menu item invoked.

## 6.2 Arrears handling

The old overflow also contains `Pay X` when arrears exist.

Do not lose this capability.

Preferred placement:

- expanded employee card: show an explicit `Pay arrears` action when arrears > 0;
- existing payroll/debt surface remains available as a second path.

Do not reintroduce overflow solely for arrears.

## 6.3 Layout change

Current audited code reserves:

```text
EmployeeRowLayout.MenuWidth = 28f
contractActions
```

Replace that reservation with a normal text-button width appropriate to the longest contextual label.

Recompute `textRight` / expansion regions from the same central layout struct so the fix does not reintroduce the historical click-overlap bug documented in that file.

## 6.4 Known file

Primarily:

```text
Source/Intercolony/UI/MainTabWindow_Intercolony_Labor.cs
```

No recon required.

## 6.5 Acceptance

1. normal active employee -> `Dismiss` visible directly;
2. travelling employee -> `Cancel`;
3. live stay offer -> `Not now`;
4. live renewal offer -> `Let them go`;
5. each button calls the pre-existing authoritative service path;
6. no `...` remains on employee card;
7. arrears can still be paid;
8. expanded-card action stack remains `Renew / Keep them / Negotiate` as previously designed;
9. collapsed card remains uncluttered and has no overlapping click regions.

Human screenshot/play evidence required.

---

# 7. Stage C3 — F19: optional material specificity for stuffable selling agreements

## 7.1 Root cause and product intent

The data model already has the right field:

```text
RecurringContract.stuffDef
```

The Business estimator already uses:

```text
EstimateDirectInputs(state, contract.thingDef, contract.stuffDef)
```

and intentionally reports unavailable when a stuffable construction contract has no `stuffDef`.

The correction is **not** to guess a material in Business and **not** to force every player-created furniture agreement to specify one.

The player should have the **option** to make the agreement material-specific.

Examples:

```text
Chair                  -> generic chair agreement, any valid material
Chair + Steel          -> steel-chair agreement
Dining chair + Wood    -> wood-chair agreement
```

If the player leaves material unspecified, the agreement remains intentionally generic and the Business direct-material replacement row may remain unavailable/ambiguous for that contract. That is preferable to inventing a material the player never promised.

## 7.2 Player-proposed supply agreement UI

In `Dialog_ProposeAgreement`, when `selectedItem.MadeFromStuff`, show an optional control such as:

```text
Material: [ Any material ▼ ]
```

Menu:

```text
Any material
Steel
Wood
Stone...
```

List only stuffs that can validly make the selected item.

Default must be:

```text
Any material
```

Do not auto-force a concrete material merely because the player selected a stuffable product.

When item changes:

- keep the selected material if it is still valid;
- otherwise fall back to `Any material`;
- refresh settlement price/reference/acceptance previews immediately.

The item list itself remains by product; do not explode it into one row per `ThingDef × StuffDef` pair.

## 7.3 Pricing and negotiation

When a concrete material is selected, all previews and terms that depend on product value must use the exact:

```text
ThingDef + StuffDef
```

including:

- reference unit price;
- proposed unit price / allowed range;
- acceptance evaluation if item value feeds it;
- Business material replacement cost after acceptance.

Do not show a generic or wooden-chair reference price while proposing steel chairs.

When `Any material` is selected:

- preserve the current generic-product pricing behavior unless a narrower existing market-price seam already supports generic stuffable goods cleanly;
- do not fabricate a concrete `stuffDef` solely to make F19 numeric;
- make the UI/Business state clear that direct material replacement cost is unresolved because the contract did not specify material.

## 7.4 Contract service API

Thread optional `ThingDef stuffDef` through the authoritative selling-contract proposal/build path rather than mutating it in UI after creation.

Expected affected methods include the player-proposal overloads and internal preparation/build functions in `ContractService`.

Validation:

- non-stuffable product -> `stuffDef` null/ignored;
- stuffable product + `Any material` -> `stuffDef == null` is valid;
- stuffable product + concrete material -> material must be valid and `CanMake(product)`;
- invalid pair -> proposal refused before any state is written.

`RecurringContract.stuffDef` already persists; no schema bump is required solely for this correction.

## 7.5 Settlement-generated contract offers

Do not broaden this stage by forcing a new material-selection model onto settlement-generated offers unless current code already naturally chooses/specifies material.

If a settlement-generated offer already carries a concrete valid `stuffDef`, preserve and test it.

If it is currently generic (`stuffDef == null`), that is acceptable for this correction pass. The key requirement is that player-proposed agreements can deliberately become material-specific and that Business then knows what material to price.

## 7.6 Fulfillment

When a contract has a concrete `stuffDef`, verify the existing cycle-order / fulfillment path requires that material.

When `stuffDef == null`, fulfillment should continue accepting any otherwise-valid material under the existing generic agreement semantics.

If this distinction already exists, add regression evidence and do not rewrite it.

## 7.7 Legacy contracts

Existing loaded contracts where:

```text
thingDef.MadeFromStuff == true
stuffDef == null
```

remain valid generic agreements.

Do not invent a material on load and silently change their obligation.

Business may continue to show direct material cost unavailable for those ambiguous contracts.

## 7.8 Known files

Start with:

- `Source/Intercolony/UI/Dialog_ProposeAgreement.cs`
- `Source/Intercolony/Contracts/ContractService.cs`
- `Source/Intercolony/Contracts/RecurringContract.cs` (field already exists; likely little/no model change)
- `Source/Intercolony/Core/BusinessReportService.cs` (clarity/regression, not a new estimator)
- `Source/Intercolony/Market/IntercolonyPricing.cs`
- selling contract/order self-tests

No broad recon required.

## 7.9 Acceptance

1. propose generic chair contract with `Any material` -> saved `stuffDef == null` and contract remains valid;
2. propose steel chair contract -> saved `stuffDef == Steel`;
3. propose wood chair contract -> saved correct wood stuff def;
4. material-specific price previews differ when material value differs;
5. Business Materials resolves for concrete steel/wood contracts;
6. Business explicitly remains unavailable/ambiguous for generic stuffable contract rather than guessing;
7. concrete steel contract rejects wrong-stuff furniture at fulfillment;
8. generic chair contract accepts otherwise-valid material under existing semantics;
9. non-stuffable contracts unchanged;
10. legacy null-stuff contract loads unchanged.

---

# 8. Stage C4 — F23: make equipment tiers exist in the market before a posting asks for them

## 8.1 Observed failure

Human playtest:

```text
Shooting 1+
Equipment: Standard
```

produced no applicants.

The requested correction is **not** "show more than six applicants on one posting". The posting queue can remain capped around its current size.

The problem is that the underlying market does not reliably contain enough workers whose equipment can satisfy the requested tiers.

A representative ordinary market should have roughly:

```text
Professional-or-better  ~8–10 prospects
Elite                    ~2–4 prospects
Standard-or-better       many prospects
```

before a specific job posting narrows that pool by skill, clause, employer standing, etc.

## 8.2 Why the current implementation is fragile

Current audited flow for non-`Any` requests is approximately:

```text
prospect qualifies by skill
→ settlement passes CanSupply(tier)
→ materialise pawn
→ inspect naturally generated gear
→ retry natural pawn gear up to 3 times
→ reject prospect if RNG never happens to satisfy requested tier
```

This makes equipment availability a late three-roll lottery rather than a property of the labor market.

That is the primary seam to correct.

Keep `MaxWaitingApplicants = 6` unless playtest proves the queue itself is a UX problem. Do **not** raise it merely to hit the 8–10 Professional market target; those 8–10 belong to the census/market, not one posting's visible waiting list.

## 8.3 First diagnostic — bounded

Before redesigning the equipment market, instrument one deterministic representative census and count:

```text
all prospects
Standard-capable / Standard-equipped
Professional-capable / Professional-equipped
Elite-capable / Elite-equipped
```

Then for a permissive `Shooting 1+` posting, count:

```text
skill-qualified
source capability rejected
promised equipment tier rejected
materialised
final actual-loadout validation rejected
accepted applicant
queue-cap rejected
```

The trace should make clear whether scarcity comes from settlement capability, census composition, promised tier assignment, materialisation, or final gear construction.

Remove/disable temporary diagnostics before the final candidate.

## 8.4 Give the lightweight market record an equipment promise/capability

The market should know a prospect's equipment level **before** a job posting asks for it.

Preferred model:

- extend the lightweight `LaborProspect`/census record with enough data to express an equipment promise/tier (or equivalent capability descriptor);
- assign that tier during census generation using settlement tech/wealth/archetype + deterministic market RNG;
- keep this lightweight: do not materialise hundreds of pawns just to know equipment availability;
- use settlement `CanSupply` as an upper bound, not as the only signal.

Conceptually:

```text
LaborProspect
  ...existing identity/skill/economics fields...
  equipmentTier / promisedEquipmentLevel
```

This tier represents what the source settlement is willing/able to send **with this worker** if hired, not a fake UI label detached from actual items.

## 8.5 Balance the census, not the posting queue

Tune the prospect-tier distribution against representative ordinary-world settlement compositions.

At the default `100% / 100% / 100%` equipment-abundance settings, the desired shape is:

```text
Standard-or-better      common
Professional-or-better  about 8–10 in the market
Elite                    about 2–4 in the market
```

under neutral/good employer standing and a normal mature world with enough accessible settlements.

Guidelines:

- Standard should be broadly available across ordinary capable sources;
- Professional should be meaningfully scarcer but not rare enough that ordinary postings routinely get zero;
- Elite should be rare and concentrated in high-tech/wealthy/military sources, but present often enough that the category is worth exposing to the player;
- pre-industrial/poor sources must still fail appropriate upper tiers;
- do not synthesize exactly N workers after the fact or ignore world composition;
- use weighted/proportional generation and, if necessary, a light deterministic floor/correction pass over a representative census so the category does not collapse to zero by random chance.

### Mod-setting abundance multipliers

Add three settings to the existing Intercolony mod settings surface:

```text
Standard equipment abundance      100%
Professional equipment abundance  100%
Elite equipment abundance         100%
```

Use them in the lightweight prospect-tier assignment, **after** determining what tiers the source settlement is capable of supplying.

Preferred implementation shape:

```text
baseline exact-tier weight
× player abundance multiplier
→ normalized weighted selection among tiers this source can actually supply
```

Important constraints:

- these settings tune **exact promised tiers**, not applicant queue size;
- Professional + Elite together naturally determine the `Professional-or-better` pool because Elite satisfies Professional requests;
- a higher Elite multiplier may therefore also make Professional postings easier to fill, which is expected;
- never use an abundance multiplier to relax `CanSupply`/capability ceilings;
- setting one tier to `0%` must not crash or leave the weighted selector with no legal outcome; fall back to another allowed tier / residual no-special-gear outcome as appropriate;
- the default values must reproduce the intended baseline counts;
- settings affect the next census/market refresh only; existing waiting applicants and active employees remain untouched.

The correction must preserve the feeling of a market, not become a vending machine.

## 8.6 Matching should filter by the prospect's promised tier cheaply

For a posting with requested equipment:

```text
skill requirement
+ source accessibility/reputation
+ prospect promised equipment tier >= requested tier
```

should be enough to decide whether the lightweight prospect may answer.

Do not materialise a pawn just to discover that the market record never had enough equipment in the first place.

`Any` remains wildcard behavior.

`None` remains a special case where the hired worker is supplied with no bondable gear.

## 8.7 Materialisation must fulfill the promise

### FOCUSED RECON REQUIRED

Inspect only:

- current `LaborProspect.Materialise` path;
- vanilla RimWorld 1.6 pawn gear-generation helpers relevant to equipping an already-created pawn.

Answer:

1. Can vanilla generate/regenerate gear for an existing pawn without rerolling their identity/skills?
2. Can that be constrained by faction/tech/value well enough to satisfy a promised Standard/Professional/Elite tier?

Stop recon after those questions.

### Required behavior

When a prospect with a promised tier actually becomes an applicant:

```text
materialise pawn once
→ keep natural gear if it already satisfies the promised/requested tier
→ otherwise fill/adjust gear through a narrow faction/tech-aware allocator
→ classify actual resulting gear
→ accept only if actual gear satisfies the promise
```

The final gear must be real `Thing`s and remain the surface priced by the Equipment Bond.

Do not display Professional/Elite while handing the pawn Standard gear.

A final-loadout failure should become exceptional/configuration evidence, not the normal source of market scarcity.

## 8.8 Narrow allocator fallback

If vanilla has no safe helper, implement one narrow Labor equipment allocator that:

- filters weapons/apparel by source tech/capability;
- respects combat clause;
- uses `LaborEquipmentTierService.Classify` as final truth;
- does not use hardcoded `defName` whitelists;
- chooses the least excessive package that satisfies the promised tier;
- remains deterministic for the same market seed/prospect/source.

Civilian semantics remain:

- better tiers improve appropriate work/protection apparel;
- do not add an offensive weapon merely because the civilian has an Elite equipment promise.

## 8.9 Atomic posting creation

Fix the current dialog-only mutation seam while touching this system.

`JobPostingService.TryPost(...)` should receive/store `requestedEquipmentLevel` as part of creation rather than posting first and mutating the last list element afterward.

C5 will add the emergency flag through the same seam.

## 8.10 Market-shape and posting tests

Separate **market tests** from **queue tests**.

### Market tests

On a representative deterministic world fixture:

```text
Professional-or-better market prospects ≈ 8–10
Elite market prospects ≈ 2–4
Standard-or-better market prospects = clearly common
```

Use sensible broad bands rather than brittle exact counts.

Also test the Mod Settings semantics on the same deterministic fixture:

- default `100% / 100% / 100%` reproduces the intended baseline bands;
- increasing Professional abundance monotonically increases or preserves the number of exact-Professional prospects over the same deterministic world/refresh;
- increasing Elite abundance monotonically increases or preserves Elite prospects;
- setting Elite to `0%` produces no newly assigned exact-Elite prospects while leaving lower tiers functional;
- abundance changes never make an incapable low-tech source pass an upper-tier capability gate;
- changing settings does not mutate already-waiting applicants; the new distribution appears on the next census refresh.

The fixture must contain enough accessible settlement diversity to test the intended economy. If it does not, fix the fixture rather than spawning magical workers.

### Posting tests

With permissive `Shooting 1+`:

- Standard posting should not routinely be empty;
- Professional posting should normally draw applicants;
- Elite posting should normally draw some applicants in the representative world;
- visible queue may remain capped at 6;
- harder skill requirements should reduce/possibly eliminate responses;
- low-tech source must not supply Elite;
- actual applicant gear must satisfy requested tier;
- bond must price actual gear;
- save/load of waiting applicants remains clean;
- `Any` preserves legacy behavior;
- `None` produces no bondable supplied gear.

## 8.11 Explain silence

Extend the no-applicant explanation so an empty posting can distinguish at least:

- nobody met the skill requirement;
- matching workers existed but not at the requested equipment tier;
- promised/capable workers existed but actual gear fulfillment failed (should be rare and treated as a defect signal).

Do not tell the player "there are no Professional workers" when the actual reason was an unrelated skill requirement.

## 8.12 Known files

Start with:

- `Source/Intercolony/Labor/JobPosting.cs`
- `Source/Intercolony/Labor/JobPostingService.cs`
- `Source/Intercolony/Labor/LaborCandidateService.cs`
- `Source/Intercolony/Labor/LaborEquipmentTierService.cs`
- the `LaborProspect` definition/materialisation file
- `Source/Intercolony/IntercolonySettings.cs`
- `Source/Intercolony/IntercolonyMod.cs`
- `Source/Intercolony/UI/Dialog_CreateJobPosting.cs`
- `Source/Intercolony/UI/MainTabWindow_Intercolony_Labor.cs`
- labor self-tests

---

# 9. Stage C5 — F24: emergency hiring should feel like premium rapid reinforcement

## 9.1 Product goal

Emergency hiring is a premium service and must visibly justify its premium.

Two valid experiences:

```text
Drop pod:
hire urgently
→ 1–4h ETA
→ inbound letter identifies source + landing location
→ Jump to location
→ visible vanilla-style pod descends
→ worker activates from the pod
```

or:

```text
Rushed conventional arrival:
hire urgently from a very close source
→ 5–9h ETA
→ conventional arrival
→ still same-day and tactically useful
```

Not acceptable:

```text
pay emergency premium
→ wait 1–2 days
→ pawn eventually walks in like a normal hire
```

## 9.2 Remove the two-day emergency rule

Delete/replace the current product rule:

```text
EmergencyConventionalMaxDays = 2
```

Do not derive emergency eligibility from whole-day ordinary `travelDays` thresholds.

Emergency dispatch gets a dedicated route/ETA quote.

## 9.3 Route-specific emergency ETA

### Drop-pod-capable source

- route: `DropPod`;
- ETA: deterministic **1–4 in-game hours**;
- distance/source capability may bias the value inside the band;
- do not hardcode one universal `4h` arrival.

### Conventional-only source

- only sufficiently close settlements qualify;
- route: `Conventional`;
- ETA: deterministic **5–9 in-game hours**;
- target feel around ~7h is fine;
- farther settlements simply do not qualify for emergency dispatch.

This is intentionally slower than pods while still useful within the same in-game day.

Do not compress every normal 1–2 day caravan into 5–9h. Eligibility should remain distance/capability-aware so only genuinely close conventional sources can offer this rush service.

## 9.4 One authoritative emergency quote

Introduce/reuse one authoritative route quote, conceptually:

```text
EmergencyArrivalQuote
  bool available
  EmploymentArrivalTransport transport
  int arrivalTicks
  string methodLabel
```

Both direct hire and job-posting paths must use the same quote for:

- market filtering;
- player-facing ETA;
- wage premium quote;
- contract `arrivalTick`;
- persisted `arrivalTransport`.

Do not independently recompute route after the player has paid.

## 9.5 Direct-hire UI

When Emergency Dispatch is enabled, the candidate list must show the emergency route and emergency ETA rather than ordinary `travelDays`.

Examples:

```text
2.3h — Drop pod
6.8h — Emergency caravan
```

The hire dialog repeats:

- emergency premium;
- exact quoted emergency ETA;
- transport method;
- source settlement.

The player must know what they are buying before confirming.

## 9.6 The pod must visibly exist

The current code already reaches `DropPodUtility.MakeDropPodAt(...)` for contracts marked `DropPod`, but human playtest did not see a pod.

This stage is not complete until the player can visibly watch a vanilla-style drop-pod skyfaller descend.

### FOCUSED RECON REQUIRED

Compare only the current `EmploymentService.LaunchDropPodArrival` seam against vanilla RimWorld 1.6 raid/reinforcement drop-pod arrival.

Answer:

1. Does the current `DropPodUtility.MakeDropPodAt` invocation create the same visible descent/skyfaller lifecycle as vanilla raids?
2. If not, what narrow vanilla helper/container invocation gives that visual presentation while preserving the already-generated worker pawn?
3. What object should be used as the letter's `LookTarget` so Jump to location lands on the actual incoming pod/landing site?

Stop recon there. Do not broaden into transport-pod systems generally.

## 9.7 Pick landing location first and announce it

For a drop-pod emergency arrival:

1. choose the valid landing cell;
2. create the actual inbound pod/skyfaller at that cell through the vanilla-style path;
3. immediately send an emergency inbound letter targeting that pod/skyfaller (or the exact landing cell if vanilla API requires it);
4. let the player click Jump to location and watch descent;
5. only complete employee activation after the pod actually resolves/lands.

Suggested letter:

```text
Emergency reinforcements inbound

<Worker> is inbound from <Settlement> under your emergency contract.
Drop-pod landing site: <colony/map location>.
ETA: <quoted hours> / imminent as appropriate at launch.
```

Use a reliably visible letter importance. This is premium tactical reinforcement, not chatty background noise.

The letter must be sent **before** the spectacle is over.

## 9.8 Do not spawn the pawn beside the pod or before it

For pod arrivals:

- the hired pawn remains contained by the transport/pod path while airborne;
- no edge-walking placeholder pawn;
- no pre-spawned worker who later gets visually decorated with a pod;
- contract becomes Active only from the actual pod landing/completion path.

This is important both visually and for save/load correctness.

## 9.9 Emergency Job Posting

Add an `Emergency hire` checkbox to `Dialog_CreateJobPosting`.

It is a persisted posting term:

```text
bool emergencyDispatch
```

Default/migration:

```text
false
```

Use the project's next schema number if the persistence policy requires a world-schema bump.

The posting headline/status must visibly include `Emergency`.

## 9.10 Atomic posting creation API

C4 already widens `JobPostingService.TryPost` to receive equipment level.

Extend the same authoritative creation call to receive:

```text
requestedEquipmentLevel
emergencyDispatch
```

Do not create a posting and mutate these terms afterward in the dialog.

## 9.11 Emergency posting market behavior

An emergency posting should become useful immediately rather than waiting for the next ordinary market refresh.

On creation:

1. inspect/reuse the current deep labor census;
2. apply skill requirement;
3. apply F23 prospect equipment promise/tier;
4. require an available emergency route quote;
5. calculate each applicant's emergency premium and freeze the quoted route/ETA relevant to that application;
6. populate the normal waiting queue up to its existing cap.

Do **not** create extra magical prospects outside the census simply because Emergency is checked.

If nobody qualifies, say so immediately and explain whether the blocker was skill, equipment, or emergency reach.

Subsequent normal market refreshes may re-evaluate the still-open posting.

## 9.12 Emergency applicant data

Persist or otherwise freeze enough per-applicant route data that the UI and later acceptance cannot disagree.

An emergency applicant row should show something equivalent to:

```text
Emergency
Arrival: 2.1h — Drop pod
From: <settlement>
```

or:

```text
Arrival: 6.4h — Emergency caravan
```

When the player accepts:

- use the already quoted emergency premium;
- use the already quoted transport and arrival tick/duration semantics;
- use the exact same EmploymentService arrival pipeline as direct emergency hire.

Do not maintain a second drop-pod implementation for job postings.

## 9.13 Pricing

Keep the existing strong emergency wage premium as the starting balance unless tests show it is inconsistently applied.

The key correction is:

```text
premium
+ tactically useful speed
+ clear route
+ pod spectacle when applicable
```

Preview and charged wage must match exactly.

## 9.14 Save/load

Prove both routes survive save/load:

- direct drop-pod emergency hire saved before launch;
- direct conventional emergency hire saved before arrival;
- emergency job posting saved with waiting applicants;
- accepted emergency applicant saved before arrival;
- after reload, quoted method/timing remain stable and worker arrives exactly once.

For pods: no duplicate pawn, duplicate pod, duplicate employment, or fallback edge-spawn.

## 9.15 Acceptance tests

Automated:

1. drop-pod-capable direct emergency hire -> ETA >=1h and <=4h;
2. two otherwise similar pod candidates can receive different stable ETAs within 1–4h;
3. close conventional emergency source -> ETA >=5h and <=9h;
4. distant conventional source rejected from emergency market;
5. no emergency path uses ordinary 1–2 day timing;
6. ordinary hire unchanged;
7. emergency candidate preview uses emergency ETA/method, not ordinary travel days;
8. accepted direct emergency hire freezes quoted route + exact timing;
9. emergency job posting persists flag;
10. emergency post performs immediate matching against existing census;
11. emergency ask includes the same premium later charged;
12. emergency applicant acceptance uses the same rapid-arrival pipeline;
13. save/reload before arrival produces exactly one worker and, for pod route, exactly one pod;
14. inbound pod letter carries a valid look target to the actual landing pod/site.

Human evidence — mandatory:

1. hire at least one direct emergency worker from a drop-pod-capable settlement;
2. verify quoted ETA is 1–4h;
3. receive the inbound letter naming the source;
4. click Jump to location;
5. visibly watch a vanilla-style pod descend at the announced site;
6. worker becomes active from that pod arrival, not by walking/spawning nearby;
7. hire/test a close conventional emergency worker and verify a visibly different 5–9h route/ETA;
8. repeat the pod path through an Emergency Job Posting applicant.

Do not close C5 on model-only evidence. If human evidence cannot be produced during the unattended run, leave those exact visual checks pending rather than claiming them passed.

---

# 10. Stage C6 — integration and regression

Run these cross-system scenarios before the whole suite.

## 10.1 Produce preset integration

1. Create preset `Steel Chairs`.
2. Target 20 / resume below 10 / min Construction / worker settings.
3. Drag over several steel chairs.
4. Every target gets the same control settings.
5. Edit preset target to 40.
6. Existing loops remain at 20 until preset is deliberately applied again.
7. Apply again -> all selected loops move to 40.
8. Pause/Resume/Stop area controls still work.

## 10.2 Stuff contract -> Business

1. Propose generic chair agreement with `Any material`; it remains valid and Business does not invent a direct material cost.
2. Propose steel-chair agreement.
3. Accept/activate it through normal test path.
4. Business Materials resolves steel replacement cost.
5. A wooden-chair agreement produces a different material cost.
6. Wrong-stuff inventory does not satisfy the steel-chair contract.
7. Generic chair agreement continues to accept otherwise-valid materials under existing generic semantics.

## 10.3 F23 market population sanity

With a representative settlement world, inspect the underlying census before one posting consumes it:

- Standard-or-better is common;
- Professional-or-better is roughly 8–10 workers;
- Elite is roughly 2–4 workers;
- those are market counts, not required simultaneous applicant-row counts;
- a permissive Professional/Elite posting draws some of those workers into the normal capped queue;
- actual gear shown matches the promised/requested tier;
- no low-tech source creates impossible Elite gear.

## 10.4 Emergency + equipment

Post:

```text
Shooting 1+
Security Contractor
Professional equipment
Emergency hire ON
```

Verify:

- immediate matching uses existing census;
- only rapid-capable sources answer;
- equipment tier remains real;
- premium is visible;
- if routed by pod, accepted worker lands within 1–4h through the pod path;
- if routed conventionally, ETA is 5–9h;
- letter jumps to the actual pod landing site for pod arrivals.

## 10.5 Regression-only features

Recheck:

- F06 apparel policy + one-time bond consent;
- F20 direct labor figures;
- F21 settlement rapid-logistics profile visibility/stability;
- F12/F22 remain untouched.

---

# 11. Testing / mutation expectations

Targeted first:

```text
F04 -> IntercolonyProduceSelfTest
F16/F23/F24 -> IntercolonyLaborSelfTest + UI helper assertions
F19 -> contract/order/business/ledger tests
```

Mutation/negative-control where useful:

- preset application test must fail if copy step is removed;
- material contract test must fail if `stuffDef` is dropped before contract creation;
- F23 population test must fail if final-loadout tier validation is bypassed;
- emergency test must fail if ordinary multi-day `travelDays` timing is substituted;
- pod-route test must fail if its ETA leaves 1–4h or transport changes to Conventional;
- conventional-emergency test must fail if its ETA leaves 5–9h or is mislabeled as DropPod.

Then full:

```text
dotnet build
dev.ps1 test all -Fresh
```

Require exit 0 and no new unexplained log signal.

---

# 12. Documentation / halt

Update the current Foreman/progress docs with this correction run's exact evidence.

Do not rewrite the earlier run as though its implementation was “wrong”; record that these are **human-playtest refinements**.

At clean halt report:

```text
Branch
Base
HEAD
Schema
Build
Fresh whole-suite result

Corrected:
- F04 presets/bulk controls
- F16 direct lifecycle button
- F19 material-specific agreements
- F23 applicant market/equipment supply
- F24 rapid visible emergency arrivals + emergency postings

Regression checked:
- F06
- F20
- F21

Frozen:
- F12
- F22

Human evidence still pending/passed
Non-blocking defects not touched
No merge/tag/release/Workshop action
```

Push the working branch and halt.

---

# 13. Definition of success

This correction run is done when:

- Produce Controls can be authored once and painted across many production objects through a named Architect preset;
- the employee card no longer hides its current lifecycle decision behind `...`;
- a player can optionally make a stuffable selling agreement material-specific, while leaving `Any material` valid and explicitly ambiguous for direct material-cost reporting;
- equipment tiers exist in the underlying labor market at useful frequencies, so Standard is common, Professional is meaningfully available and Elite is rare-but-real rather than routinely zero;
- Emergency uses route-specific tactical timing: 1–4h by drop pod, 5–9h by rushed conventional arrival; the player can see who is inbound, jump to the announced pod landing site, and actual vanilla-style drop pods visibly descend;
- the same emergency workflow is available through Job Postings;
- already-good F06/F20/F21 behavior remains intact;
- F12/F22 remain frozen;
- no release action occurs.
