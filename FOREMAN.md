# Foreman state — Intercolony

Stage: 1 — Quiet automation and vanilla-command correctness
Unit: 1.3 — F15 production: both agreement types default auto-ready on for new agreements
Worker: running — `C:\Users\matte\AppData\Local\Temp\claude\C--dev\0e66c849-e19d-4229-9f25-19e3ad1f4bf6\scratchpad\unit-1-3.out`
Last done: 1.2 — three letter assertions, committed db9627a; 49/0/0 and all three mutations red
Updated: 2026-09-07 03:35
Wakes: 0 · last full load at wake 0
Foreman: 10ee860 · source C:\dev\agent-foreman · https://github.com/Vector-Consulting-IA-Operacional/agent-foreman.git
Fallback: if `Skill(foreman)` is unknown, read `C:\dev\agent-foreman\skill\SKILL.md`, follow it,
then re-run its section 0.

<!-- Everything above this line is the header. A fresh session reads only the header. -->

Source plan: `docs/PLAYTEST_BATCH_SOURCE_PLAN.md` (findings F01–F25).
Branch: `foreman/playtest-batch-2026-09-06`. **Never merge to `main`, never publish** — §I of the plan.

## Stages

| | Stage | Findings | Status |
|---|---|---|---|
| 🔨 | 1 — Quiet automation and vanilla-command correctness | F01, F02, F13, F15 | in progress |
| ⬜ | 2 — Produce becomes programmable | F03, F04 | not started |
| ⬜ | 3 — Agreement and employee UX | F14, F16/F17, F18, F10 | not started |
| ⬜ | 4 — Player-side logistics | F05, F12 | not started |
| ⬜ | 5 — Market geography | F21, F11 | not started |
| ⬜ | 6 — Business intelligence and costing | F07, F19, F20 | not started |
| ⬜ | 7 — Two-sided labor market | F25, F23, F24, F22 | not started |
| ⬜ | 8 — Commercial relationships | F08, F09 | not started |

## Units — stage 1

| | Unit | Status |
|---|---|---|
| ✅ | 1.0 — recon: name the file:line seams for F01, F02, F13, F15 | done, citations verified |
| ✅ | 1.1 — F01 production: automatic caller sends no "Order ready" letter; failure stays loud | 4f2f319 |
| ✅ | 1.2 — F01 tests: no letter on auto success, letter on auto failure, letter on manual | mutation-verified |
| 🔨 | 1.3 — F15: a newly created selling or procurement agreement has auto-ready on | worker running |
| ⬜ | 1.4 — F15 tests, including the schema-57 default and load of an older save | not started |
| ⬜ | 1.5 — F13: the employee row shows auto-renew state without opening the `…` menu | not started |
| ⬜ | 1.6 — F02: cancelling a produce-loop blueprint ends the loop for that cell | not started |
| ⬜ | 1.7 — F02 tests | not started |

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
- **2026-09-07** — F15 changes only the two C# field initializers to true and deliberately leaves
  both `Scribe_Values.Look` defaults at false. A true Scribe default would switch automation on
  inside saves the player already has, including agreements they had turned off by hand. New
  agreements default on; loaded ones keep what was saved.
- **2026-09-07** — Local `letterVolume` is `Minimal`, so `Important` letters do not reach the
  letter stack in this environment. The self-test now pins the setting rather than depending on
  it; a future letter assertion must do the same or it measures the preference, not the code.
