# Foreman state — Intercolony

Stage: 2 — Produce becomes programmable
Unit: 2.0 — Read-only recon: the seams for F03 (area Produce/Pause/Stop) and F04 (programmable)
Worker: running — `C:\Users\matte\AppData\Local\Temp\claude\C--dev\0e66c849-e19d-4229-9f25-19e3ad1f4bf6\scratchpad\unit-2-0.out`
Last done: STAGE 1 CLOSED — F01, F02, F13, F15 all shipped with mutation evidence; last commit cd69b9c
Updated: 2026-09-07 07:25
Wakes: 11 · last full load at wake 10
Owed: four stage-1 playtests are recorded in `docs/PENDING_PLAYTESTS.md` and outstanding. The F15
save-compatibility one is the one that matters — it is the only stage-1 change that touches saves
that already exist.
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
| 🔨 | 2 — Produce becomes programmable | F03, F04 | in progress |
| ⬜ | 3 — Agreement and employee UX | F14, F16/F17, F18, F10 | not started |
| ⬜ | 4 — Player-side logistics | F05, F12 | not started |
| ⬜ | 5 — Market geography | F21, F11 | not started |
| ⬜ | 6 — Business intelligence and costing | F07, F19, F20 | not started |
| ⬜ | 7 — Two-sided labor market | F25, F23, F24, F22 | not started |
| ⬜ | 8 — Commercial relationships | F08, F09 | not started |

## Units — stage 2

| | Unit | Status |
|---|---|---|
| 🔨 | 2.0 — recon: the seams for F03 (area orders) and F04 (programmable produce) | worker running |

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

- **2026-09-07** — The wake loop was paused by the operator for maintenance and re-armed on
  resume. The run adopted Foreman `10ee860` mid-run at the operator's request; the plan and its
  stage/unit decomposition were carried over untouched. Wake counters restart at 0 because the
  cadence they drive was introduced by that version.
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
