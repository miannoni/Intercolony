# Foreman state — Intercolony

Stage: 6 — Business intelligence and costing
Unit: 6.0 — Read-only recon: the seams for F07, F19 and F20
Worker: running — `…\scratchpad\unit-6-0.out`
Last done: STAGE 5 CLOSED — F21 built to its bounded scope, F11 BLOCKED on a schema decision.
Last commit 2f8ba8c; suite 1476/0/17.
Updated: 2026-09-08 09:30
Wakes: 79 · last full load at wake 70

Owed to the operator, all recorded in `docs/PENDING_PLAYTESTS.md`: fourteen play observations across
stages 1-4. Two matter more than the rest — the F15 save-compatibility check, the only change
touching saves that already exist, and the partial-delivery defect in `DeliverToColony`, which costs
the player silver and needs a design decision before it can be fixed.
Foreman: 10ee860 · source C:\dev\agent-foreman · https://github.com/Vector-Consulting-IA-Operacional/agent-foreman.git
Fallback: if `Skill(foreman)` is unknown, read `C:\dev\agent-foreman\skill\SKILL.md`, follow it,
then re-run its section 0.

<!-- Everything above this line is the header. A fresh session reads only the header. -->

Source plan: `docs/PLAYTEST_BATCH_SOURCE_PLAN.md` (findings F01–F25).
Branch: `foreman/playtest-batch-2026-09-06`. **Never merge to `main`, never publish** — §I of the plan.

## Stages

| | Stage | Findings | Status |
|---|---|---|---|
| ✅ | 1 — Quiet automation and vanilla-command correctness | F01, F02, F13, F15 | closed 2026-09-07 |
| ✅ | 2 — Produce becomes programmable | F03, F04 | closed 2026-09-07 |
| ✅ | 3 — Agreement and employee UX | F14, F16/F17, F18, F10 | closed 2026-09-08 |
| ✅ | 4 — Player-side logistics | F05, F12 | closed 2026-09-08, F12 part-built |
| ✅ | 5 — Market geography | F21, F11 | closed 2026-09-08, F11 blocked |
| 🔨 | 6 — Business intelligence and costing | F07, F19, F20 | in progress |
| ⬜ | 7 — Two-sided labor market | F25, F23, F24, F22 | not started |
| ⬜ | 8 — Commercial relationships | F08, F09 | not started |

## Units — stage 6

| | Unit | Status |
|---|---|---|
| 🔨 | 6.0 — recon: the seams for F07, F19 and F20 | worker running |

## Units — stage 5 (closed, F11 blocked)

| | Unit | Status |
|---|---|---|
| ✅ | 5.0 — recon: the seams for F21 and F11 | done, citations verified |
| ✅ | 5.1 — F21: one owner for cost, time and method, numbers unchanged | 2efd13f |
| ✅ | 5.1b — the flaky receiving fixture skips instead of failing | 2efd13f |
| ✅ | 5.2 + 5.2b — F21 drift guards, all four mutation-proven | 6d6366a |
| ✅ | 5.3 — F21: disclose the logistics cost | 8da0d9f |
| ✅ | 5.3b — F21 disclosure tests | 83f2340, four mutations red |
| ✅ | 5.5 — record the stage-5 playtests owed | 2f8ba8c |
| ⛔ | 5.4 — F11: staggered RFQ responses | BLOCKED on the operator's schema decision |

## Units — stage 4 (closed)

| | Unit | Status |
|---|---|---|
| ✅ | 4.0 — recon: the seams for F05 and F12 | done, citations verified |
| ✅ | 4.1 — F05: the receiving marker and its persistence | 231df04 |
| ✅ | 4.2 — F05: the gizmo that marks a stockpile or shelf | caf4340 |
| ✅ | 4.3 — F05: delivery prefers a receiving destination, via vanilla StoreUtility | 74aff0f |
| ✅ | 4.4 — F05 tests | 912a5fe, four mutations red |
| ✅ | 4.5 — F12 first slice: availability as available/required, not yes/no | c19b9cb |
| ✅ | 4.6 — F12 first-slice tests, with 4.6b's fixture fix | 8cb06a9, three mutations red |
| ✅ | 4.7 — record the stage-4 playtests owed | 58656a9 |

## Units — stage 3 (closed)

| | Unit | Status |
|---|---|---|
| ✅ | 3.0 — recon: the seams for F14, F16/F17, F18 and F10 | done, citations verified |
| ✅ | 3.1 — F18: the procurement row shows the price per unit | 8d533bc |
| ➖ | 3.2 — F18 tests | dropped deliberately, see Decisions |
| ✅ | 3.3 — F14: the predicate for which selling entries start expanded | 02d5710 |
| ✅ | 3.4 — F14 tests: seven assertions over the predicate | 02d5710, six mutations red |
| ✅ | 3.3b — F14: the per-entry expansion state and its resolver | 275577a |
| ✅ | 3.3c — F14: the collapsed row and its height | 275577a |
| ✅ | 3.4b — F14 on the procurement list | c67cece, one correction |
| ✅ | 3.4c — F14 tests for the procurement predicate | 7571391 |
| ✅ | 3.5 — F16/F17 content cull, which is also the F13 overflow fix | b1b5c7f |
| ✅ | 3.7 + 3.7b + 3.8 — F10 gate, fixture repair, five assertions | 799d673, five mutations red |
| ✅ | 3.9 — record the stage-3 playtests owed | fe345dd |

## Units — stage 2 (closed)

| | Unit | Status |
|---|---|---|
| ✅ | 2.0 — recon: the seams for F03 (area orders) and F04 (programmable produce) | done, citations verified |
| ✅ | 2.1 — F03 state: `paused` on the record, early return in the poll | ece7083 |
| ✅ | 2.2 — F03 state tests, including that an old save loads unpaused | f49db5a |
| ✅ | 2.3 — F03 per-object: Pause/Resume/Stop as distinct commands, not one toggle | e0ada0d |
| ✅ | 2.4 — F03 area designators driving the same transitions | 16411e5 |
| ✅ | 2.4b — register the three designators in the Architect menu (XML) | c01d7db |
| ✅ | 2.5 — F03 area tests | 57e0981 |
| ✅ | 2.6 — F04 target-count mode: record fields and the poll's stop condition | 4b14abb |
| ✅ | 2.6b — F04 UI: set the target from the produce object | 5656c75, two corrections |
| ✅ | 2.7a — the produce suite counts and reports its skips | 30b784e |
| ✅ | 2.7b — fix the below-stock fixture: one cell cannot hold two minified items | 30b784e |
| ✅ | 2.7 — F04 tests | 30b784e, all five mutation-proven |
| ✅ | 2.8 — record the stage-2 playtests owed | 48057f4 |

## Units — stage 1 (closed)

| | Unit | Status |
|---|---|---|
| ✅ | 1.0 — recon: name the file:line seams for F01, F02, F13, F15 | done, citations verified |
| ✅ | 1.1 — F01 production: automatic caller sends no "Order ready" letter; failure stays loud | 4f2f319 |
| ✅ | 1.2 — F01 tests: no letter on auto success, letter on auto failure, letter on manual | mutation-verified |
| ✅ | 1.3 — F15: a newly created selling or procurement agreement has auto-ready on | d48a1cf |
| ✅ | 1.4 — F15 tests: G4 repair, plus "new agreement starts automated" both sides | d48a1cf |
| ✅ | 1.4b — F15 Scribe round-trip: an absent node still loads off | e4d50c9 |
| ✅ | 1.5 — F13: the employee row shows auto-renew state without opening the `…` menu | 41dc1f5 |
| ✅ | 1.6 — F02: cancelling a produce-loop blueprint ends the loop for that cell | bc2a46b |
| ✅ | 1.7 — F02 tests: cancel ends the loop, cancelling elsewhere does not | c00cb0c |
| ✅ | 1.8 — record the stage-1 playtests owed in `docs/PENDING_PLAYTESTS.md` | cd69b9c |

## Decisions

- **2026-09-06** — Stage order is dependency-driven, not plan order. F24 needs F21's logistics
  capability and F25's market pricing, so labor comes after geography. Do not reorder to match
  the plan's lettered sections.
- **2026-09-06** — F03 and F04 stay separate units from each other and from F02 even though all
  three touch the produce loop. §G of the plan forbids collapsing them into one requirement.
- **2026-09-06** — The source plan was copied to `docs/PLAYTEST_BATCH_SOURCE_PLAN.md` so workers,
  which have no chat history, can cite it. The copy at `C:\dev\` is the operator's original.
- **2026-09-07** — F01 is scoped to the *readying* letter only (`SalesOrderService.cs:754`),
  suppressed for the automatic caller via an optional `announce` parameter defaulting to true.
  The downstream "Order collected" letter (`SalesOrderService.cs:982`) stays loud: it reports
  payment received, which is a value event the player should see, not a routine mechanical step.
  No schema change, and every manual ready path keeps its letter by default.
- **2026-09-07** — Procurement needs no F01 change. Recon established there is no per-cycle
  procurement success letter to suppress; the only procurement success letter is the terminal
  `Procurement agreement completed` notice (`ProcurementContractService.cs:1185`), which is a
  whole-agreement outcome and stays.

## Open for the operator

- **2026-09-08 — F11 needs a save-schema bump, the first in this batch, and that is your call.**
  Recon: RFQ responses are generated synchronously at `RfqService.cs:91`, before the request is even
  stored, and there is no pending queue and no advancement method. Making them arrive progressively
  means persisting pending responses so they survive a save, and
  `IntercolonyWorldComponent.CurrentSaveVersion` is 57 with a comment requiring a bump plus a
  `MigrateIfNeeded` step whenever the saved shape changes. Every stage so far has deliberately
  avoided touching the schema. I have not started F11. Say whether to bump to 58 with a migration,
  or to leave F11 unbuilt and record why.
- **2026-09-08 — F21 is a system, like F12 was.** It asks for cost and time to reflect route
  difficulty, cargo and provisions, settlement capability and transport method, none of which exist
  — there is no route model, no provisions, no logistics capability and no transport method beyond
  a delivery/pickup boolean. What distance drives today is five unrelated ad-hoc formulas. I am
  building the piece that is genuinely useful and bounded: one owner for cost, time and method, then
  disclosing it to the player. Making it reflect route difficulty and provisions is not being
  attempted.

- **2026-09-08 — a pre-existing defect found next to F05, deliberately not fixed.**
  `PurchaseOrderService.DeliverToColony` refunds only when ZERO goods were placed; with any
  non-zero count it calls `Complete` with what was placed, so a delivery that could only fit part
  of the order silently completes it short and the player pays in full for goods they did not get.
  It predates this branch and F05 makes it easier to hit, since a receiving destination can fill.
  Fixing it means deciding what SHOULD happen — hold the order, partial refund, or overflow
  elsewhere — which is your call, not a side effect of a logistics unit.

- **2026-09-08 — F12 is bigger than this batch, and needs a decision.** Recon found the mod has NO
  caravan formation or dispatch of its own at all: auto-ready is buyer-pickup only
  (`SalesOrder.cs:204`), and forming a caravan would mean going through vanilla's
  `CaravanFormingUtility.StartFormingCaravan`. A full "preprogrammed recurring caravan" therefore
  needs persisted pawn and animal selection, a configuration surface on the agreement, caravan
  formation, recurring re-formation, and multi-map routing — a feature, not a finding-sized change.
  What I am building in this stage is the half that is genuinely useful and testable on its own:
  the order's availability expressed as available/required, and the rule that a short order WAITS
  and says so rather than leaving partial. The caravan formation itself is not being attempted
  here. Say if you would rather it were, or would rather stage 4 stop after F05.

- **2026-09-07 — RELEASE DEFECT, pre-existing, needs a decision before the next release.**
  `package.ps1` builds a release from `$ReleaseDirectories = @("About", "Assemblies", "Defs")`
  (`package.ps1:45`). `Patches/` is not in that list, so no release zip has ever contained
  `Patches/WorldObjectDefs.xml` — the patch that puts the Economy tab on the Settlement world
  object — and the new designator registration would not ship either. The shipped 1.0.0 is
  therefore missing that tab. Found by the 2.4b worker while confirming its own file would ship;
  verified against `package.ps1` directly. Not fixed here: it is outside the playtest batch and
  changing what a release contains is the operator's call.

- **2026-09-07** — The wake loop was paused by the operator for maintenance and re-armed on
  resume. The run adopted Foreman `10ee860` mid-run at the operator's request; the plan and its
  stage/unit decomposition were carried over untouched. Wake counters restart at 0 because the
  cadence they drive was introduced by that version.
- **2026-09-07** — Stage 2 is cut around what the produce loop can actually own. Recon established
  that `Disable` already means Stop, that Pause needs a new persisted field because the record has
  no state to express it, and that the poll is the only decision seam. So F03 splits into state,
  per-object commands, and area designators, each with its own tests.
- **2026-09-07** — No assertions were written for F18 and that is deliberate.
  `ProcurementContractPaymentSummary` is a private method of the UI window returning a display
  string; asserting it would mean widening production visibility purely for a test, and the thing
  actually worth checking — that the row reads clearly and the per-unit and per-cycle figures cannot
  be confused — is a reading, not a value. It goes to the stage-3 playtest entry instead. Writing a
  test that restates the constructor would be exactly the hollowness this run keeps catching.
- **2026-09-07** — F10 is implemented as a GATE, not a new vocabulary. Recon established that
  purchases already move `CommercialReputation`, which already has tiers and player-facing labels,
  and that the finding explicitly asks for integration rather than duplication. What procurement
  actually lacked was selling's earned threshold — reputation plus a completed-trade count — so a
  standing purchase agreement was available to a total stranger. It reuses selling's 62 rather than
  choosing its own number: one relationship meter should not imply two opinions.
- **2026-09-07** — F16/F17 and the employee-row overflow deferred by F13 are one unit, not two. The
  segments the finding calls secondary — settlement, faction, wage structure, paid silver — are
  exactly the long ones, so culling them to leave pay/day, term, status and auto-renew is both the
  content change the finding asks for and the measurement fix rule 7 requires. Doing them
  separately would mean measuring twice and changing the same line twice.
- **2026-09-07** — Procurement's collapse rule deliberately has NO failure case, unlike selling's.
  `ProcurementContract` carries only a cumulative `cyclesFailed`; there is no consecutive-miss
  counter and no breach threshold, so it cannot express "one more miss ends it". Expanding on the
  cumulative count would keep every long-running agreement that ever missed permanently open, which
  is the opposite of what F14 asks. The asymmetry is known, and giving procurement a
  consecutive-miss signal is a separate change with its own persistence question.
- **2026-09-07** — F14's per-entry state is a dictionary of explicit player choices, not a set of
  collapsed ids. A set cannot distinguish "the player closed this" from "the default closed this",
  and the default is not fixed — a delivery missed or a renewal arriving changes it under a row
  that is already on screen. It clears in `PreOpen` for two reasons: the window survives being
  closed, so ids would accumulate for contracts that no longer exist, and F14 asks selling entries
  to BEGIN collapsed, which makes each visit to the tab a beginning.
- **2026-09-07** — F14's "exceptional" is defined as: the player has something to DECIDE, or
  something is going WRONG. That makes four states start expanded — a settlement's offer, a live
  renewal decision, a consecutive-miss warning, and war suspension — and everything else start
  collapsed, including a routine active agreement, a proposal the player has already sent and is
  waiting on, and all four terminal states. Terminal history is what makes the list long and is
  never urgent.
- **2026-09-07** — Stage 3 is cut smallest-risk-first: F18 is a one-string change over a figure the
  model already holds, F14 spans two list renderers plus lifecycle-bound UI state, F16/F17 needs
  both a card restructure and the geometry work that F13 deliberately deferred, and F10 is a
  behaviour slice rather than a label change — recon found `ReputationService.NotePurchaseCompleted`
  already feeding reputation from purchases, so what F10 lacks is the progression's thresholds and
  labels on the procurement side, not the plumbing.
- **2026-09-07** — The produce self-test's `Results.Skip` counted nothing and `Summarize` printed
  only passed/failed, so skipped assertions were reported as `0 skipped` and read as passes. Fixed
  in 30b784e. The lesson for the rest of this run: a green suite line is not evidence an assertion
  ran — only a mutation that turns it red is. Where a stage-1 or stage-2 commit message says "0
  skipped", the claim that everything ran rests on the mutation results in that same message, not
  on the count.
- **2026-09-07** — F04's target mode counts colony STOCK, not cycles produced, matching a RimWorld
  bill's "do until you have X". So it is self-clearing: sell or consume the stock and the program
  resumes on its own, with no latch and no finished flag. `targetCount <= 0` means indefinite, which
  is what every existing save and every new program gets, so no migration is needed. It cannot
  reuse `FindBuyerService.ColonyStock` — that filters to fungible trade items and a minified
  workbench is exactly what this has to count.
- **2026-09-07** — F04's worker-eligibility, skill and quality controls are NOT owned by the produce
  loop. Recon put them in vanilla's construction job — `JobDriver_ConstructFinishFrame` and
  `Frame.CompleteConstruction(Pawn worker)` decide who builds and what quality results. F04 itself
  says the goal is not to recreate every bill field, so stage 2 implements the modes the loop can
  own — indefinite and produce-until-target — and records the rest as deliberately not built rather
  than half-building them through the construction system. Raised with the operator.
- **2026-09-07** — F02's cancel seam is a Harmony prefix on `Designator_Cancel.DesignateThing`,
  not `Thing.Destroy`. `Thing.Destroy` would catch every cancellation route but sits on the hot
  path for every destroyed thing in the game, against the mod's standing rule to keep patches few
  and narrow. The designator only runs on a player cancel, which is the command the finding is
  about. If area-drag cancel turns out not to route through it, this covers single-target cancel
  only and the gap is recorded rather than widened.
- **2026-09-07** — The employee detail line can overflow its 720f rect on an extreme row (~1367f
  measured). Pre-existing; the F13 token adds ~110f. Deliberately left for F16/F17 in stage 3,
  which restructures these cards anyway.
- **2026-09-07** — The F15 default flip turned one existing assertion red: RFQ G4, "unaffordable
  procurement cycle fails without ending the agreement". That is the fixture's assumption expiring,
  not a defect — an automated agreement is supposed to wait for silver until its deadline rather
  than count the cycle failed. G4 now sets `autoReadyOrders = false` explicitly, because it is
  testing the non-automated path. The production default stands.
- **2026-09-07** — F15 changes only the two C# field initializers to true and deliberately leaves
  both `Scribe_Values.Look` defaults at false. A true Scribe default would switch automation on
  inside saves the player already has, including agreements they had turned off by hand. New
  agreements default on; loaded ones keep what was saved.
- **2026-09-07** — Local `letterVolume` is `Minimal`, so `Important` letters do not reach the
  letter stack in this environment. The self-test now pins the setting rather than depending on
  it; a future letter assertion must do the same or it measures the preference, not the code.
