# Intercolony — Playtest Correction Execution Plan

**Target repository:** `miannoni/Intercolony`  
**Working branch:** `foreman/playtest-batch-2026-09-06`  
**Execution mode:** Claude Code using **Agent Foreman**  
**Purpose:** apply the next correction pass from hands-on playtesting without expanding into product areas that have not yet been re-specified.

---

## 0. Authority and scope

This document is the **newest product instruction for the items it explicitly covers**.

Where it conflicts with the earlier `docs/PLAYTEST_BATCH_SOURCE_PLAN.md`, this correction plan wins for the behaviors described here.

The earlier source plan remains authoritative for everything not changed here.

### Hard scope rule

Work only on the findings explicitly assigned for implementation or cleanup in this plan:

- F01
- F07
- F08
- F09
- F10
- F11
- F13
- F17

Preserve already-closed behavior for:

- F02
- F03
- F05
- F14
- F15
- F18
- F25

Freeze, document, and do not advance:

- F12
- F22

Do **not** modify, redesign, “finish,” opportunistically clean up, or dispatch implementation units for the following findings yet:

- F04
- F06
- F16
- F19
- F20
- F21
- F23
- F24

Those areas will receive separate product instructions later.

If work on an in-scope item appears to require changing behavior belonging to one of those deferred findings, stop that dependency at the boundary, record it clearly, and continue with other independent work.

---

# 1. Foreman execution rules

Use Foreman as the execution supervisor for this plan.

Foreman should:

1. Recon the current branch before changing code.
2. Convert each stage below into traceable requirements / slice contracts.
3. Keep changes small enough that failures can be localized.
4. Prefer existing Intercolony architecture and RimWorld semantics over parallel systems.
5. Reuse existing settings infrastructure rather than introducing a second configuration mechanism.
6. Prefer existing simulation events or mod-owned lifecycle seams over polling.
7. Avoid new save-schema bumps unless persistent world state genuinely requires them.
8. Treat UI/settings values as live configuration where practical.
9. Preserve deterministic behavior and save/load stability.
10. Add acceptance evidence for every changed behavioral claim.

### Verification discipline

For every behavioral change:

- add or update a targeted assertion;
- mutation-prove the assertion where production logic can be meaningfully broken;
- run the narrowest relevant suite first;
- run broader regression coverage before closing the stage.

At the end of the run:

```text
dotnet build
dev.ps1 test all -Fresh
```

The final whole-suite run must exit successfully with no new log exceptions.

### Git discipline

- Stay on `foreman/playtest-batch-2026-09-06`.
- Do not merge to `main`.
- Do not release.
- Commit coherent slices.
- Push progress regularly.
- At clean halt, push everything and report:
  - branch;
  - commit count / latest commit;
  - clean/dirty working-tree state;
  - whole-suite result;
  - anything intentionally left frozen or deferred.

---

# 2. Stage C0 — Scope lock and regression baseline

Before implementing anything:

1. Confirm the working branch is `foreman/playtest-batch-2026-09-06`.
2. Confirm the previously reported milestone is present.
3. Run a baseline targeted build/test pass sufficient to prove the branch is healthy before edits.
4. Record in `PROGRESS.md` that this correction run:
   - advances only the explicitly in-scope findings;
   - freezes F12 and F22;
   - leaves F04, F06, F16, F19, F20, F21, F23 and F24 untouched pending later product instructions.

Do not dispatch implementation for a deferred finding just because recon discovers an easy improvement.

---

# 3. Stage C1 — F01: quiet routine contract cycles

## Product problem

A `Contract delivery due` letter still appears when a recurring supply commitment reaches its cycle even when everything is proceeding normally.

That is contrary to the desired notification philosophy:

> routine success should be quiet; exceptions should demand attention.

## Required behavior

A normal long-term supply cycle should **not** interrupt the player merely because the delivery date arrived.

### Silent path

Do not send `Contract delivery due` when:

- the cycle becomes due;
- required stock is available;
- fulfillment can proceed normally;
- Auto-ready can complete the configured flow;
- no player intervention is required.

It is acceptable and desirable to keep:

- logging;
- commercial history;
- status changes;
- timeline/accounting records.

The requirement concerns the **player interruption**, not historical observability.

### Exception path

Send an actionable warning only when the commitment cannot be fulfilled as expected.

Examples:

- insufficient eligible goods;
- fulfillment/logistics path invalid;
- expected dispatch cannot occur;
- other condition requiring player intervention.

The warning should answer:

1. which contract/order is affected;
2. what is missing or blocked;
3. what the player needs to do.

Avoid duplicate letters for the same failure condition.

## Recon guidance

Inspect the recurring-contract cycle creation path and the existing `Contract delivery due` emission.

Do not merely hide the letter using global letter-volume settings. Fix the semantic trigger.

## Acceptance evidence

Prove at least:

- due cycle + fully fulfillable -> no letter;
- due cycle + successful Auto-ready -> no letter;
- due cycle + insufficient goods -> actionable warning;
- successful cycle still records the appropriate history/state;
- no duplicate warning spam from repeated ticks while the same exception remains unresolved.

---

# 4. Stage C2 — F07: production capture must match real completed output

## Product problem

The Business view can show:

`no production in the last 5 days`

even while relevant goods — specifically furniture/chairs — have been produced.

The metric is intended to be a rolling five-day rate. The player must **not** need to wait five full days before the first value appears.

If one chair has been completed during the current five-day window, the report should already be able to show approximately:

`0.2 / day`

## Required accounting semantics

Keep the denominator as the full rolling five-day window:

```text
completed quantity in rolling 5-day window / 5
```

Examples:

- 1 completed chair -> 0.2/day
- 5 completed chairs -> 1.0/day
- 10 completed chairs -> 2.0/day

Do not divide by “days since observation began.”

### What counts as production

Production means **a good actually completed by the colony**.

Do not infer production from:

- stockpile delta;
- current inventory;
- sales;
- hauling;
- minification alone;
- item disappearance/reappearance without a genuine completion event.

Selling existing chairs must never become negative production.

## Recon task

Audit every relevant completion path that can create a good used in commercial commitments, including at minimum:

- bill/crafting completion;
- constructed furniture/buildings that become tradeable/minifiable goods;
- Intercolony Produce loops;
- any other current path by which a player can genuinely “produce” a contracted good.

The existing bill-completion observer is not sufficient if it misses constructed furniture.

### Implementation preference

Prefer, in order:

1. an existing authoritative vanilla completion event already safely observable;
2. an existing Intercolony-owned completion/lifecycle seam;
3. a narrowly scoped Harmony observation only if no reliable non-patch seam exists.

Do not add full-map polling.

If a new Harmony patch is genuinely necessary, keep it observation-only, narrow, fail-safe, and document why no existing seam could prove completion.

## Acceptance evidence

Explicitly test:

- one crafted item -> 0.2/day;
- one constructed/minified furniture item -> 0.2/day;
- one furniture item completed through Produce -> 0.2/day;
- multiple completions aggregate correctly;
- sale/removal of an existing item does not count as production;
- no positive completion in the window -> correct no-production state;
- save/load preserves the rolling production evidence.

---

# 5. Stage C3 — Settings expansion: F08, F09 and F11

Implement these settings in one coherent pass so configuration UI, persistence, validation, labels and tooltips remain consistent.

Use the existing `IntercolonySettings` / `IntercolonyMod` settings surface.

Defaults must reproduce current behavior unless this plan explicitly changes the default.

No world schema bump should be required for ordinary mod settings.

---

## 5A. F08 — configurable commercial goodwill pressure

### Product intent

Commercial goodwill pressure currently feels too infrequent and too weak.

Expose its core balance parameters so the player can tune them in-game.

### Add settings

Create a clear section such as:

**Commercial relationships**

Add:

| Setting | Default | Suggested allowed range |
|---|---:|---:|
| Commercial goodwill interval | 15 days | 1–60 days |
| Goodwill per interval | +1 | 0–5 |
| Commercial goodwill ceiling | 60 | 0–74 |
| Commercial reputation required | current Preferred threshold | 0–100 |

Use player-facing labels/tooltips that explain the gameplay meaning rather than implementation names.

### Required semantics

- Read settings live for future applications.
- Do not grant retroactive goodwill when cadence changes.
- Continue deduplicating by faction.
- Continue blocking positive commercial pressure for hostile/at-war factions.
- Continue respecting vanilla goodwill restrictions.
- Clamp the ceiling below vanilla Ally threshold.
- Setting goodwill per interval to `0` should effectively disable positive pressure without disabling the rest of reputation.

### Acceptance evidence

Prove:

- defaults reproduce current behavior;
- shorter interval applies more frequently;
- larger delta changes the amount applied;
- configurable threshold changes qualification;
- configurable ceiling stops pressure at the configured value;
- ceiling cannot be configured to produce alliance by itself;
- save/reload preserves the settings.

---

## 5B. F09 — configurable employment-experience goodwill

### Product intent

The employment-experience system should remain bounded, but its thresholds and impact should be tunable.

### Add settings

In the same relationships/labor configuration area:

| Setting | Default | Suggested allowed range |
|---|---:|---:|
| Minimum employment days for goodwill | 10 | 1–60 |
| Positive experience threshold | 75% mood | 50–95% |
| Negative experience threshold | 35% mood | 5–50% |
| Employment goodwill impact | 3 | 0–10 |

### Required semantics

Keep:

- one normalized mood sample per day;
- one final goodwill resolution at the end of ordinary employment;
- no infinite goodwill farming;
- existing guards for outcomes already priced elsewhere;
- existing hostility rule for positive goodwill.

Ensure:

- negative threshold cannot equal/exceed positive threshold after validation/clamping;
- setting impact to zero disables the goodwill change but does not break experience recording;
- changed settings affect future resolution, not already-finished contracts.

### Acceptance evidence

Prove with non-default values:

- minimum-day setting changes eligibility;
- positive threshold changes positive qualification;
- negative threshold changes negative qualification;
- magnitude changes applied goodwill;
- neutral band produces zero;
- existing breach/double-pricing guards remain intact.

---

## 5C. F11 — configurable and front-loaded RFQ response timing

### Product problem

Supplier RFQ responses can take too long before the player sees the first useful proposal.

The desired experience is progressive, but not sluggish.

### Desired default pacing

For a normal request with several plausible suppliers:

- by the first in-game day, the player should commonly have 1–2 proposals;
- additional proposals may arrive on days 2–3;
- unusually attractive proposals may tend to arrive later;
- after day 5, no new proposals should arrive for that request.

This is a tendency, not a guaranteed deterministic rank order.

### Add setting

Add a simple global setting:

**RFQ response speed**

Suggested range:

```text
0.5x – 2.0x
```

Default:

```text
1.0x
```

Use a tooltip explaining:

- higher values make supplier replies arrive sooner;
- distance still matters;
- price/offer quality may still influence response timing.

### Scheduling model

Keep the existing principle:

- quote terms are generated once;
- arrival timing is generated once;
- neither terms nor timing reroll on UI refresh or save/load.

Rebalance response scheduling using:

1. distance;
2. preparation / existing quote lead-time signals where appropriate;
3. small deterministic jitter;
4. offer attractiveness as a timing tendency;
5. configured response-speed multiplier;
6. hard maximum arrival at 5 in-game days after request creation.

### Important constraint

Do not make the best price always arrive last.

Attractiveness may shift probability/timing, but the market must still feel organic.

### Existing requests

Changing the setting should affect **newly scheduled requests**.

Do not rewrite persisted arrival times for RFQs already in progress.

### Request lifetime

Make the request/response lifecycle coherent with the five-day hard arrival cap.

Do not keep extending a request merely because an old scheduling rule would have produced a later reply.

### Acceptance evidence

Create deterministic fixtures proving:

- normal multi-supplier request gets at least one plausible early response;
- responses remain spread over time;
- farther suppliers tend to be slower;
- attractive offers can tend to arrive later without deterministic ordering;
- no response arrival is scheduled past day 5;
- 0.5x and 2.0x produce measurably different timing;
- save/load preserves exact scheduled arrivals;
- quote terms remain identical regardless of when they are revealed.

---

# 6. Stage C4 — F10: progression gates long-term procurement, not Find Seller

## Product correction

Procurement relationship progression should be the mirror of selling.

The player must be able to use **Find Seller / spot procurement** with suppliers they have never bought from before.

Relationship/history gates are for **standing long-term procurement agreements**, not ordinary one-off purchasing.

## Required model

### Spot procurement

`Find Seller` / RFQ / ordinary purchase:

- available without prior purchase history;
- available without long-term procurement reputation threshold;
- may quote any otherwise accessible/economically valid supplier;
- remains subject to ordinary item availability, scarcity, distance, hostility/access rules, etc.

### Long-term procurement agreement

A standing procurement contract should remain earned.

It may require:

- completed prior purchases from that settlement;
- commercial reputation;
- existing long-term-agreement eligibility rules.

### Conceptual mirror

Selling:

```text
Find Buyer / spot sales
        ->
repeat business / relationship
        ->
long-term supply agreement
```

Procurement:

```text
Find Seller / spot purchases
        ->
repeat business / relationship
        ->
long-term procurement agreement
```

## Recon task

Audit all F10-related gates and call sites.

Look specifically for progression/reputation checks that accidentally leaked into:

- RFQ supplier discovery;
- quotation generation;
- ordinary spot purchase acceptance;
- Find Seller UI availability.

Remove those gates from spot procurement while preserving them on standing-agreement proposal/acceptance.

## Acceptance evidence

Prove:

- no history + reachable valid supplier -> supplier can quote through Find Seller;
- no history -> player can complete a spot purchase;
- no history -> long-term procurement agreement remains blocked;
- required history/reputation -> long-term procurement becomes eligible;
- changing this does not weaken selling-side long-term progression;
- no duplicate or parallel procurement reputation model is introduced.

---

# 7. Stage C5 — F13 and F17: clean the current employee-card interaction surface

This stage is intentionally narrow.

Do not redesign the full employee card here.

A broader employee-card product specification will arrive later.

---

## 7A. F13 — visible Auto-renew state and direct toggle

### Problem

Auto-renew was implemented, but the current presentation is not sufficiently direct.

The state should read visually like the existing Auto-ready control used in supply/procurement.

### Required behavior

On the employee card:

- show Auto-renew state directly;
- use a compact clear visual control;
- preferably reuse the existing Auto-ready visual convention/component where practical;
- allow the player to toggle it without opening `...`.

Desired visual semantics:

- check/tick for ON;
- X/off state for OFF;
- short tooltip only.

Do not create a second full text action if a compact control can express the state.

### Scope boundary

Do not perform the future full employee-card redesign in this stage.

Only make Auto-renew:

- immediately visible;
- directly interactive;
- visually consistent with Intercolony's existing automation toggles.

### Acceptance evidence

Prove:

- ON state visible at a glance;
- OFF state visible at a glance;
- direct click toggles the persisted contract field;
- reopening the UI reflects the correct state;
- `...` is not required merely to discover or change Auto-renew.

---

## 7B. F17 — secondary employee actions must leave the primary card

### Problem

Secondary actions still occupy the employee card even though the product intent was to keep the primary surface focused.

### Move out of the primary body

Actions such as, when applicable:

- Keep them;
- Not now;
- Dismiss;
- other equivalent occasional contract actions.

Move them to the existing `...` secondary action surface.

### Behavior preservation

Do not change the underlying lifecycle semantics.

This is a presentation cleanup:

- same action;
- same eligibility;
- same consequences;
- less primary-card clutter.

Where an unavailable action needs to explain why it is unavailable, preserve a disabled/tooltip representation in the secondary menu if appropriate.

### Scope boundary

Do not implement:

- collapsible employee cards;
- expanded information tables;
- a new Negotiate workflow;
- large tooltip redesign;
- broader employee-card layout changes.

Those are outside this run.

### Acceptance evidence

Prove:

- occasional actions are no longer primary inline controls;
- actions remain reachable through `...`;
- action enable/disable semantics are preserved;
- no contract lifecycle regression.

---

# 8. Stage C6 — Freeze F12 explicitly

## Status

F12 remains intentionally incomplete.

Current implemented behavior may remain:

- order availability checks;
- protections already built around complete/partial fulfillment.

## Do not implement in this run

Do not add:

- preprogrammed caravan formation;
- saved pawn selection;
- saved animal selection;
- recurring automatic player caravans;
- automatic caravan dispatch;
- multi-map routing;
- new caravan state machines.

## Documentation action

Update progress/status docs so future Foreman runs do not mistake F12 for an unfinished task that should be resumed automatically.

Mark it clearly:

```text
F12 — FROZEN.
Existing order-availability work retained.
Recurring/preprogrammed caravan design intentionally deferred.
Do not dispatch further implementation without a newer product plan.
```

No implementation unit should follow the documentation freeze.

---

# 9. Stage C7 — Freeze F22 explicitly

## Status

Do not implement the reverse labor market in this release line.

The existing recon may remain as historical technical information.

## Do not implement

Do not build:

- player-colonist labor listings;
- external pawn custody;
- game-over custody workaround;
- off-map employment lifecycle;
- external employment offer generation;
- abstract training;
- injury/death risk;
- return-to-colony lifecycle.

## Documentation action

Mark it clearly:

```text
F22 — FROZEN.
Reverse/player-supplied labor market intentionally not part of the current release scope.
Do not dispatch custody proof or any implementation unit without a newer product plan.
```

Foreman should not stop the run later asking whether it may begin F22.

---

# 10. Regression-only findings

The following are considered closed for this correction run:

- F02
- F03
- F05
- F14
- F15
- F18
- F25

Do not spend implementation effort on them unless:

1. an in-scope change breaks one of their assertions; or
2. a directly related regression is discovered while testing the changed behavior.

If a regression is discovered, fix the regression narrowly and record it as a regression, not as a reopening of product scope.

---

# 11. Explicitly deferred product areas — do not touch

The following findings will receive newer product direction separately:

- F04
- F06
- F16
- F19
- F20
- F21
- F23
- F24

For this run:

- do not implement missing source-plan portions;
- do not redesign their UI;
- do not “complete” them based on old plan text;
- do not infer product decisions;
- do not dispatch speculative recon solely for them;
- do not opportunistically refactor their behavior unless required to prevent a regression in an in-scope item.

If an in-scope task reveals a dependency on one of these areas:

1. isolate the dependency;
2. record exactly why it blocks or constrains the in-scope requirement;
3. implement the rest of the independent work;
4. leave the product decision untouched.

---

# 12. Recommended dispatch order

Foreman should execute in this order unless recon proves a direct dependency requires a small adjustment.

| Stage | Scope | Reason for order |
|---|---|---|
| C0 | Scope lock + baseline | Prevent accidental expansion before coding |
| C1 | F01 | Small isolated behavioral correction |
| C2 | F07 | Accounting/capture correctness before UI tuning |
| C3 | F08 + F09 + F11 settings | Shared settings infrastructure in one coherent pass |
| C4 | F10 | Procurement relationship semantics |
| C5 | F13 + F17 | Narrow labor UI cleanup without broader redesign |
| C6 | Freeze F12 | Prevent automatic continuation |
| C7 | Freeze F22 | Prevent automatic continuation |
| C8 | Full regression + clean halt | Verify branch as a whole |

Avoid parallel workers modifying the same large UI/settings files at the same time.

In particular:

- F08/F09/F11 settings should be coordinated as one settings slice or serialized;
- F13/F17 both touch employee UI and should be serialized or owned by one worker.

---

# 13. Final acceptance gate

Before clean halt:

## Behavioral acceptance

Confirm:

- F01 routine contract cycles are silent;
- F01 contract exceptions remain actionable;
- F07 real completed furniture/Produce output appears immediately in the rolling five-day rate;
- F08 balance knobs are configurable in settings;
- F09 experience goodwill is configurable in settings;
- F10 Find Seller remains open spot procurement while standing agreements remain earned;
- F11 RFQs are front-loaded, progressive, configurable, deterministic and capped at five days;
- F13 Auto-renew is visible and directly toggleable;
- F17 occasional employee actions are no longer primary-card clutter;
- F12 is frozen;
- F22 is frozen;
- no deferred product area was advanced.

## Technical acceptance

Run:

```text
dotnet build
dev.ps1 test all -Fresh
```

Require:

- build success;
- whole suite exit 0;
- zero new failing assertions;
- zero new runtime/log exceptions caused by this correction batch;
- save/load stability for settings and any affected persisted state;
- mutation evidence for new behavioral assertions where practical.

## Documentation acceptance

Update at minimum:

- `PROGRESS.md`;
- pending/manual-playtest notes where human verification is still valuable;
- any status/Foreman plan record that could otherwise cause F12 or F22 to be redispatched automatically.

Do not rewrite the original source-plan history to pretend frozen/deferred items were completed.

Preserve the distinction between:

- implemented;
- frozen;
- deferred by newer product direction.

---

# 14. Clean-halt report

When no further in-scope work can be dispatched, stop cleanly and report:

```text
Branch:
Latest commit:
Commits pushed:
Working tree:
Whole-suite result:

Completed in this correction run:
- ...

Frozen:
- F12
- F22

Preserved / regression-only:
- F02
- F03
- F05
- F14
- F15
- F18
- F25

Left untouched pending newer product direction:
- F04
- F06
- F16
- F19
- F20
- F21
- F23
- F24

Manual playtests still recommended:
- ...
```

Do not merge or release automatically.

---

# 15. Definition of success

This run succeeds when the current batch becomes **more faithful to the intended player experience without reopening every unfinished idea at once**.

The desired result is:

- less notification spam;
- trustworthy production-rate reporting;
- tuneable relationship/RFQ pacing;
- correct procurement relationship progression;
- cleaner employee-card interaction;
- explicit boundaries around features intentionally not being advanced yet;
- a green, mutation-backed branch ready for the next product-specification pass.
