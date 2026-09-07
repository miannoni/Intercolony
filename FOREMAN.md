# Foreman state — Intercolony

Stage: 1 — Quiet automation and vanilla-command correctness
Unit: 1.1 — F01 production: the automatic auto-ready caller sends no "Order ready" letter
Worker: running — `C:\Users\matte\AppData\Local\Temp\claude\C--dev\0e66c849-e19d-4229-9f25-19e3ad1f4bf6\scratchpad\unit-1-1.out`
Last done: 1.0 — recon; 16 cited file:line spot-checked, all resolve (`RECON_STAGE1.md`)
Updated: 2026-09-07 02:00
Foreman: d68bd37 · source C:\dev\agent-foreman · registered junction OK

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
| 🔨 | 1.1 — F01 production: automatic caller sends no "Order ready" letter; failure stays loud | worker running |
| ⬜ | 1.2 — F01 tests | not started |
| ⬜ | 1.3 — F15: a newly created selling or procurement agreement has auto-ready on | not started |
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

- none
