# Intercolony 1.0 Implementation Status

The continuity mechanism between sessions. Read `docs/INTERCOLONY_1_0_IMPLEMENTATION_PLAN.md`
first; this file says where in that program we actually are.

Current stage:      Stage 0 — Program spine
Current slice:      0.2 — capture the 0.9.3 market baseline
Last completed:     0.3b — timeline write sites (committed, self-test not yet run)
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

## Slice log

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

**0.2 — capture the 0.9.3 market baseline**, the last item in Stage 0. Deterministic debug
output over several forced refreshes recording opportunities per settlement, category
distribution, exact-good turnover, lot sizes, unit-price factor spread, RFQ response rate,
full/partial/zero quote frequency, effective supplier quantities, and how all of it differs
by archetype.

It is not there to preserve 0.9.3's balance. It is the evidence that tells us whether the
Stage 2 economy has accidentally produced no offers, one dominant category everywhere,
infinite supply, runaway prices, or archetypes that stopped mattering. **It has to be taken
before Stage 1 touches `SettlementEconomicProfile`**, and market generation is still
untouched today, so this is the moment.

Then the Stage 0 acceptance gate, then Stage 1.
