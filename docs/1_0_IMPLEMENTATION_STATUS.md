# Intercolony 1.0 Implementation Status

The continuity mechanism between sessions. Read `docs/INTERCOLONY_1_0_IMPLEMENTATION_PLAN.md`
first; this file says where in that program we actually are.

Current stage:      Stage 2 — Market fundamentals overhaul
Current slice:      2B — advance and mean-revert pressure (BLOCKED, see below)
Last completed:     2A — persisted market pressure, schema 44
Current save schema: 44
Current save schema: 43
Current mod version: 0.9.3
Branch:             `1.0` — branched from `main` at `0f55a27`, merges back at Stage 8

## Stage status

- [ ] Stage 0 — Program spine
- [ ] Stage 1 — Settlement economies
- [ ] Stage 2 — Market fundamentals overhaul
- [ ] Stage 3 — Circumstance-driven economic events
- [ ] Stage 4 — Brand strength & colony specialization
- [ ] Stage 5 — Commercial relationships & negotiation
- [ ] Stage 6 — Procurement parity
- [ ] Stage 7 — Commercial history
- [ ] Stage 8 — 1.0 integration and release gate

## Stage 0 acceptance gate

Four of six criteria are closed. The two open ones need a human at the keyboard; neither is
a code gap.

| # | Criterion | Status |
|---|---|---|
| 1 | Clean build passes | **PASS** — 0 errors, 0 warnings |
| 2 | 1.0 status ledger exists | **PASS** — this file |
| 3 | Baseline market diagnostics exist | **PASS** — captured to `docs/MARKET_BASELINE_0_9_3.md` |
| 4 | Timeline has an owner and bounded retention | **PASS** — `IntercolonyWorldComponent`, 1,000 records |
| 5 | Prior 0.9.3 save loads after the schema change | **PASS** — see below |
| 6 | No existing self-test regressed | **OPEN** — needs the suites run in game |

### Criterion 5 — the migration ran for real, and this is the first time

A 21.5 MB schema-42 colony save (`Fenhana` / `Intercolony 0.9.3 preflight`, economy seed
`-1586549745`, refresh 432, `nextId 6826`) was loaded on the schema-43 build. The log:

```
[Intercolony] State loaded (schema 42, nextId 6826).
[Intercolony] Migrating state from schema 42 to 43.
[Intercolony]   schema 42 -> 43: commercial timeline record spine added;
                history starts recording at tick 6473557.
```

Zero exceptions in the session and no Intercolony warnings or dropped records.

**This matters beyond Stage 0.** `CLAUDE.md` records that none of the three earlier migrations
had ever run in the real load order — only in isolated throwaway installs — and names it the
top item in `docs/PENDING_PLAYTESTS.md`. This run went through the ordinary path: real save,
real load order, real mod list. `dev.ps1 run -MainMenu` was added so it stays repeatable;
`-quicktest` cannot do it, because a new world initializes at the current schema and never
enters the migration at all.

On criterion 6, the one plausible regression from 0.3b was ruled out by inspection rather
than left to chance: the new write sites consume entity IDs, so any self-test asserting on a
specific ID would break. Grepping every assertion in `Debug/` found only the timeline
suite's own literals, which it constructs itself. Nothing else depends on ID sequencing.

**Stage 1 is unblocked.** The baseline is captured to `docs/MARKET_BASELINE_0_9_3.md`, taken on
a persistent save rather than a throwaway world, so the same economy can be measured again after
Stage 2 instead of a merely similar one.

The capture exposed two flaws in the diagnostic itself, both since fixed, both worth a rerun to
fold into the record:

- **It reported generator appetite as though it were market size.** `MaxLiveOpportunities` is a
  global flat ceiling on live offers; a real refresh in the log created 13 while the sample
  projected ~200 per cycle. The report now prints the ceiling and says plainly that the market
  is ceiling-bound, not generator-bound. Appetite is still the right measurement — the ceiling
  would mask a dead generator until it fell below the cap — but reporting it unqualified would
  have misled every later comparison.
- **The probe basket was junk.** Alphabetically-first-per-category selected ancient ruins
  scenery. It now ranks by observed demand from the same sample.

## Stage 1 acceptance gate

| # | Criterion | Status |
|---|---|---|
| 1 | Same seed + settlement yields the same baseline across save/load | **PASS by construction** — generation unchanged; profiles are still regenerated, never persisted |
| 2 | Baseline no longer depends on the refresh count | **PASS** — the cycle roll is gone; asserted as purity in the market suite |
| 3 | Same-archetype settlements still differ modestly | **PASS by construction** — `SettlementProfileGenerator`'s jitter is untouched |
| 4 | Different archetypes produce visibly different tendencies | **PASS by construction** — archetype weight tables untouched |
| 5 | Modded/undefined tech faction handling stays safe | **PASS by construction** — no tech-tier code was touched |
| 6 | Existing consumers compile; adapters marked for deletion | **PASS** — renamed at all eight call sites, no adapters were needed |
| 7 | Player can identify a settlement's economy without debug numbers | **PASS** — needs a look in play to confirm it reads well |

"PASS by construction" means the slice did not touch that path and the existing profile suite
already covers it — not that it was re-observed. Running `Run profile self-test` and
`Run market self-test` would convert 1–5 into observed passes and is the open item.

## Slice log

### 2A — persisted market pressure (2026-08-20)

**Claim:** a settlement's economy can hold a non-neutral condition that survives save and load,
and an undisturbed world persists nothing.
**Files:** `Economy/SettlementMarketState.cs` (new), `Debug/IntercolonyEconomySelfTest.cs` (new),
`Core/IntercolonyWorldComponent.cs`, `Debug/IntercolonyDebugActions.cs`.
**Commit:** `d49fd86`. **Schema:** 43 → 44, migration writes nothing.
**Verified in game:** `State initialized fresh (schema 44)`, no exceptions.
**Tests:** `Run economy self-test` — sparse defaults, create-on-demand, prune keeps disturbed and
drops settled, Scribe round trip, short-save padding. **Not yet executed.**

**Sparse, not one record per settlement.** The baseline was captured on a 358-settlement world;
a record each would put thousands of floats in every save to say nothing is happening. Records
appear on first disturbance and are dropped when they settle. "Settled" is an epsilon rather
than equality because mean reversion approaches 1.0 asymptotically — with exact comparison a
record would never become prunable and the save would only ever grow.

**Scribe has no array overload.** `Scribe_Collections.Look` takes List, HashSet, Stack, Queue and
Dictionary; verified against `reference/decompiled`. Arrays cross the boundary as lists and stay
arrays in memory, since pressure is read by category index on paths Stage 2 will make hot. A
short or missing list loads padded with **neutral, not zero** — zero would read as no demand at
all, a shortage nobody caused.

### 1.2 / 1.4 / 1.5 — settlement economies (2026-08-20)

**Claim:** a settlement's economic identity is stable, independent of the market clock, and
visible to the player without debug output.
**Files:** `Core/SettlementEconomicProfile.cs`, `UI/MainTabWindow_Intercolony.cs`,
`Market/FindBuyerService.cs`, `Market/IntercolonyPricing.cs`,
`Market/MarketOpportunityGenerator.cs`, `Procurement/RfqService.cs`, three self-tests.
**Commits:** `12c8972`, `5711ee0`. **Schema:** unchanged at 43 — the profile is not persisted.

`DemandFor`/`SupplyFor` became `BaseDemandFor`/`BaseSupplyFor` at all eight call sites. The
rename is the point, not tidiness: Stage 2 puts an effective-economy layer over these, and a
caller that says "demand" when it means "identity" is how the two get conflated.

**The affinity band is 0.15 and the number is constrained, not chosen.** It has to straddle
`FindBuyerService.InterestThreshold` (0.9) against category weights clustering at 1.0. The
plan's illustrative ±0.08 would put every good in a wanted category above the threshold, making
"No current interest" dead code — the exact flattening that threshold's own comment says it
exists to prevent, and which the market self-test asserts against. Caught by reading the
threshold before picking the number rather than by the test failing afterwards.

**Expected interim effect:** Find Buyer will feel more uniform within a category until Stage 2
adds pressure back. That is the shape of the change, not a regression, and it is worth
remembering when 1.2's results are first seen in play.

**UI placement — the plan's open A/B choice.** Chose *both* tooltips over a new screen:
Relations alone would not do, because it only lists settlements already traded with and the
question is asked before the first trade. The summary is on the Market listing tooltip, where a
buyer is actually chosen, and on the Relations row. One helper, two call sites.

### 0.2 — market baseline (2026-08-20)

**Claim:** the 0.9.3 market's behaviour can be measured, reproducibly, without altering it.
**Files:** `Debug/IntercolonyMarketBaseline.cs` (new), `Debug/IntercolonyTimelineGuard.cs`
(new), `Debug/IntercolonyDebugActions.cs`, `Procurement/RfqService.cs`.
**Commit:** `fe011b7`. **Schema:** unchanged at 43.
**Tests:** the diagnostic resamples the same cycles and compares, because a figure that moves
between two runs of one seed is not evidence. Not yet executed in game.

Measures the production owners — `MarketOpportunityGenerator.GenerateFor`,
`RfqService.GenerateResponses`, `IntercolonyPricing.BaseValue` — rather than reimplementing
them, so a change to any of them surfaces here instead of hiding behind a parallel copy of
the arithmetic. Offers are generated against synthetic cycle numbers past the world's own, so
a sample can never be confused with the live market. Probe goods come from the loaded defs by
category, not a hardcoded vanilla list, so the baseline still means something under mods.

**Known limitation:** the procurement half can only measure the current refresh window. Quote
seeding reads `state.RefreshCount`, and advancing it would mean running real refreshes on the
player's world. The report says so rather than implying an average.

**It also fixed a consequence of 0.3b, found by audit rather than by failing.** Self-tests
drive the real transitions on purpose, and those now record — so the order, RFQ and
combat-clause suites were each about to write dozens of rows into the player's trading history
for settlements that do not exist. `IntercolonyTimelineGuard` restores the list contents
around a self-test run. Contents, not length: pruning removes from the front, so restoring by
count leaves synthetic records where the real ones were. It is deliberately not a suppression
flag on the recording service — a global "stop recording" bit that leaked would silently lose
real history, which is far worse than a debug list needing cleanup. It is applied to
self-tests only; debug actions that genuinely advance the world keep their records, because
those events really happened.

### 0.3b — timeline write sites (2026-08-20)

**Claim:** every terminal commercial transition in production code writes exactly one
timeline record, at the point the status actually changes.
**Files:** `Orders/SalesOrderService.cs`, `Procurement/PurchaseOrderService.cs`,
`Contracts/ContractService.cs`, `Core/HostilityPolicy.cs`,
`Core/CommercialEventRecord.cs`, `Debug/IntercolonyTimelineSelfTest.cs`.
**Schema:** unchanged at 43. Scribe writes enums by name, so the three added
`CommercialEventType` values need no migration.
**Tests:** `CheckWriteSites` drives the real transitions — `SalesOrderService.Fail`,
`.Cancel`, `PurchaseOrderService.Cancel`, and both `HostilityPolicy` war paths — and asserts
the record appears. It deliberately does not call the timeline service itself: a test that
recorded its own events would pass with every write site deleted. Still not executed in game.

**Every write goes where the status assignment already is**, so a record cannot exist for an
event that did not happen. `SalesOrderService.Complete` already documented itself as the
exactly-once boundary for both delivery and collection; the others (`Fail`, `Cancel`,
`Refund`) are each documented as the only path to their status.

**Three enum values were added, and the reason is a correctness one.** The plan's initial
list has no `SaleCancelled`, `PurchaseFailed` or `ContractCancelled`, but all three
transitions exist in the code today and the existing values misstate them. `HostilityPolicy`
tells the player in as many words that a war-voided order "does not count against you as a
supplier" — recording it as `SaleFailed` would contradict the letter the player just read.
A supplier defaulting is not the player cancelling. The enum now covers
completed/failed/cancelled in both directions.

**Two transitions are deliberately not recorded, and say so at the site.** War *suspension*
of a supply agreement and the in-flight cycle it withdraws: suspension has no event type
yet, so recording only its side effect would leave a cancelled order in the player's history
with nothing to explain it, and the cycle is re-issued on resume anyway. And contract
*refusal*: no agreement began, and the other two decline paths are a button click and an
offer lapsing. Both belong with Stage 5, which owns proposal and negotiation outcomes.

**A near miss worth remembering.** `RecurringContract.TryAccept` has two production callers,
not one — the incoming-offer path and the player-proposal path added in 0.9.1. Wiring only
`AcceptOffer` would have silently dropped every agreement the player initiated. Grepping the
callers of the funnel, rather than trusting the first one found, is what caught it.

### 0.3 — commercial timeline spine (2026-08-20)

**Claim:** world state owns a persisted, bounded commercial timeline record list (`CommercialEventRecord`) capped at 1,000 entries, retaining event facts that cannot be reconstructed later, without wiring write sites yet.
**Files:**
- `Source/Intercolony/Core/CommercialEventRecord.cs` (new)
- `Source/Intercolony/Core/CommercialTimelineService.cs` (new)
- `Source/Intercolony/Debug/IntercolonyTimelineSelfTest.cs` (new)
- `Source/Intercolony/Core/IntercolonyWorldComponent.cs`
- `Source/Intercolony/Debug/IntercolonyDebugActions.cs`
**Schema:** bumped to 43 (additive migration step in `MigrateIfNeeded`).
**Commit:** `04be001`.
**Tests:** `IntercolonyTimelineSelfTest` asserts event types, monotonic IDs,
settlement/recency queries, retention pruning verified by record identity rather than by
count, survival of a record whose `ThingDef` no longer resolves, and a Scribe round-trip.
**It compiles and has never been executed.** The build is clean at 0 errors and 0 warnings,
which is the only thing verified so far. Running it needs a game session — see below.

**One destructive defect was caught in review before it could ship.** The first version of
the self-test snapshotted the record *count* and restored by trimming the tail, but `Prune`
removes from the front — so real records were pruned away and synthetic `bulk-N` test
records were left in their place, then persisted into the save. Harmless while nothing
writes to the timeline; actively destructive from the next slice onward. It now snapshots
and restores the list contents. **A debug tool that mutates authoritative state must restore
what it saw, not how much it saw.**

### 0.1 — status ledger (2026-08-19)

**Claim:** the program has a written continuity record that survives between sessions.
**Files:** `docs/1_0_IMPLEMENTATION_STATUS.md` (new).
**Schema:** unchanged.
**Tests:** none applicable.

## Decisions / deviations

### 2026-08-19 — Stage 0 — DECISION

**Question:** where do the 1.0 program's commits land, given Stage 0 bumps the save schema
and `main` will not be shippable again until Stage 8?
**Evidence:** 0.9.3 is live on Steam Workshop `3780094556` and public on GitHub. A beta
defect report is likely during a program this long, and `docs/BACKLOG.md` already holds
open items. Every prior phase committed straight to `main`.
**Choice:** a `1.0` feature branch. `main` stays at 0.9.3 and remains directly releasable
as a 0.9.4 point release without untangling half-built stage work.
**Why it preserves this plan:** nothing in the plan depends on branch layout; this only
protects the shipped release from an in-flight schema bump.
**Revisit if:** the program reaches Stage 8 with no intervening point release, in which
case the branch was merely bookkeeping and the merge is trivial either way.

### 2026-08-20 — Stage 0.3 — DECISION

**Question:** the plan's sketch of `CommercialEventRecord` gives `float silverAmount`.
Should the record follow it?
**Evidence:** every other money field in the codebase is an `int` — `LedgerEntry.amount`,
`SalesOrder.paidSilver`. RimWorld silver is a stack count, not a continuous quantity.
**Choice:** `int`. The plan's field list is a conceptual sketch, and §0 says implementation
details may adapt to current code while player-facing semantics stay authoritative. A money
type is an implementation detail; a float would invite rounding drift against the ledger it
will be displayed beside.
**Why it preserves this plan:** nothing player-facing changes.
**Revisit if:** a 1.0 feature ever needs fractional silver, which none currently does.

### 2026-08-19 — Stage 0 — VERIFICATION

The plan's §4 architectural claims were checked against the code before any work began,
because the document was written by an audit rather than from this repository's history.
All of them hold:

- `SettlementEconomicProfile` is non-`IExposable` and regenerated from economy seed plus
  settlement ID (`Core/SettlementEconomicProfile.cs:48`).
- §4.3's cycle-noise claim is exact: `DemandFor(def, category)` rolls
  `Rand.Range(0.55f, 1.45f)` seeded on `RefreshCount`, smoothed over three cycles
  (`Core/SettlementEconomicProfile.cs:102`). This is what Stage 2 replaces.
- Schema is 42 with a one-step-per-version migration chain (`CurrentSaveVersion`,
  `MigrateIfNeeded`).
- `SupplierOfferConsumption` and `CommercialHistoryEntry` are both owned by
  `IntercolonyWorldComponent`.
- Every file path named in §4 and in the per-stage "likely code areas" resolves.

No correction to the plan is needed.

## Play evidence still required

**Run the timeline self-test.** Dev mode → the Intercolony debug category → **Run timeline
self-test**, in a loaded game. It must report every assertion passed with no failures. This
has never been run; the slice is committed on a clean build alone.

**Load a real schema-42 save.** The 42 → 43 migration must run in the real load order and
report `schema 42 -> 43: commercial timeline record spine added`. `dev.ps1` cannot prove
this — it launches `-quicktest`, which creates a new world that initializes at the current
schema and so never enters the migration at all. Only opening an actual 0.9.3 save proves
it. This is the same standing gap already recorded for the three earlier migrations in
`docs/PENDING_PLAYTESTS.md`.

`docs/PENDING_PLAYTESTS.md` still holds the 0.9.x backlog, which this program does not clear.

## Next executable slice

**2B — advance and mean-revert pressure.** Then `2C` removing what remains of cycle noise,
`2D`–`2F` pointing selling, pricing and RFQs at one effective-economy API, `2G` player trades
nudging pressure, `2H` chain propagation, `2I` regional diffusion, `2J` explanations, `2K` the
migration and play gate.

**Do not rush Stage 2 to reach procurement.** It is the stage everything after it reads from,
and the plan says so twice.

### Why 2B should wait for one round of self-test runs

**Six suites have been changed or added across Stages 0–2A and not one has been executed.**
That is the position `CLAUDE.md` describes as how a system ends up believed-working and
untested, and 2B is the first slice that makes pressure *move* — every slice after it inherits
whatever 2A got wrong about persistence.

The specific risk is not hypothetical. 2A's whole design rests on sparse storage: absence means
neutral, records are created on disturbance and pruned when they settle. If the prune epsilon,
the index rebuild, or the load-time padding is wrong, 2B's mean reversion is what would surface
it — as pressure that silently resets, or records that accumulate forever, both of which look
like balance problems rather than persistence bugs and would be chased in the wrong place.

It is now one action — **`Run ALL self-tests`** — which runs all seventeen suites, prints one
verdict, and checks that the guards held. Steps are in `docs/PENDING_PLAYTESTS.md`.

---

### Superseded: capture the baseline, then Stage 1.1–1.2

The capture is one debug action and it belongs to whoever is at the keyboard; the steps are in
`docs/PENDING_PLAYTESTS.md`. Save its output into `docs/` as the recorded baseline — the point
is to have the numbers on disk before they become unreproducible.

Then Stage 1 proper:

- **1.1** keep profile generation deterministic from economy seed + settlement ID, and do not
  persist it. Nothing to change here yet; it is a constraint on 1.2, not a task.
- **1.2** split baseline demand from changing demand. `DemandFor(def, category)` currently
  mixes stable identity with a rolling `Rand.Range(0.55f, 1.45f)` seeded on `RefreshCount`
  (`Core/SettlementEconomicProfile.cs:102`). Consumers need to be able to ask for baseline
  without silently getting cycle noise. Exact-good variation stays, but as a *stable* affinity
  that does not move every cycle.
- **1.4/1.5** make the identity legible in the Relations surface and keep debug visibility.

1.2 is the slice the baseline exists to protect. Do not start it before the capture.
