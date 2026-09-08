# Foreman state — Intercolony

Stage: 7 — Two-sided labour market. NOTHING IS BLOCKED ON A HUMAN. The operator answered both
decisions YES on 2026-09-08: schema 57→58 with a migration and prior-save verification, and one
narrowly scoped observational Harmony patch on crafting completion. Stage 6 is next after F25, and
F06 is now stage 9. The dependency order is in "Next executable work" in the brief below.
Unit: 7.3 — F25's last slice: the posting dialog stops asking for a wage
Worker: running — `…\scratchpad\unit-7-3.out`
7.2d–7.2g committed together at `0189a8a`, suite green 29/0/0 with all three assertions proven red
under mutation. F25's code is done after 7.3; what remains is the dead `wageOffered` parameter on
`TryPost`, which is unit 7.4, and the play entry, 7.6.

7.2f fixed the crash and the suite is green at 29/0/0. But the reproducibility assertion STILL does
not guard anything: under the unseeded-shuffle mutation it SKIPS rather than fails — "applicant from
Bustpe asking 84/day matched multiple census records (170, 172); source identity is ambiguous".
An assertion that skips exactly when the thing it guards breaks is not a guard, and with several
hundred prospects two of them sharing a settlement, an ask and a skill profile is not rare, so it
would skip in ordinary runs too. The cause is a step that need not exist: it resolves each applicant
back to a unique census record so it can compare indices. 7.2g drops that and compares the two runs'
applicant lists element-wise on settlement, faction, travel days, ask and skill levels. Two records
identical in all of those are interchangeable in every way the market cares about; what an unseeded
shuffle changes is which six of several hundred get drawn.

A NOTE ON SKIPS, since this is the second time one has hidden something: the suite reports them and
they do not redden the exit code, which is correct — but a skip is not evidence, and a mutation run
that skips the target assertion proves nothing at all. Read the skip line, never just the counts.

7.2e re-keyed identity correctly — census index / settlement / travel days / ask / skill vector, no
pawn and no generated name — but added a probe that crashes the suite at 10 passed, 1 failed:
`InvalidOperationException: Stack empty` from `Rand.PopState()` in `CheckMarketReproduces`. The
probe pushes a seed, records two `Rand.Int` values, runs the match, reads two more, and pops a
SECOND time if the stream did not return to where it started — on the theory that a missing
`PopState` inside `MatchAll` would have left a frame behind. THE THEORY IS WRONG: matching
materialises applicants, pawn generation draws from the current frame, so the stream legitimately
moves on every healthy run, the conditional pop fires every time, and it pops a frame the test does
not own. 7.2f deletes the probe and leaves one push and one unconditional pop.

Good news from that same run, which reached them before the crash: A1, A2 and A3 all pass with real
separation, and **B1 has far more headroom than I feared** — queued mean best skill 11.83 against a
top-six mean of 20.00, a gap of 8.17 on a 1.00 margin. A lucky shuffle will not fail it.

7.2d's mutation results in full: baseline 28/1/0 (the one red is B2, below); B1 red under a queue
re-sort; B3 red when `TryHireApplicant` reverts to the posted wage — 27/2/0, the second red being
B2 again. B1 and B3 are proven and must not be touched. The B1 and B2 runs stopped at 11 and 11
assertions rather than 29, so those mutations threw partway; that does not weaken the evidence,
because the target assertion had already gone red, but do not read their totals as suite counts.

B2 FAILS AT BASELINE — the assertion is wrong, not the code, and unit 7.2e must fix it. The draw IS
reproducible: both matches selected prospect ids 26, 72, 9, 54, 75, 56 in that order, from the same
settlements, with identical skill vectors and identical asks of 101, 150, 84, 147, 151, 156. What
differed was three of the six generated pawns' NAMES — "Lisa-Marie 'Lima' Schmid" against "Jill Yu",
and so on. So pawn materialisation is outside the seeded stream, and the assertion keyed identity on
the pawn instead of on the census record it was made from. Re-key it to the prospect: id, settlement,
travel, ask and skills — all of which already reproduce exactly.
Not a play defect: `MatchAll` runs once per refresh, and once an applicant is materialised the pawn
itself is persisted, so a reload sees the saved pawn rather than a regenerated one. But it does mean
nobody may claim "the same world regenerates the same applicants" about pawns, only about the draw.

B1 IS PROVEN. Re-sorting the queue by ability reddened it at gap 0.00 against a 1.00 margin, with
535 qualified and 6 queued — and reddened A2 as a bonus, since a ranked unfiltered draw (20.0) then
beats the 16+ draw (17.3).
  B1 the waiting list is a spread, not the top N — mutation re-sorts the queue by ability;
  B2 the same seed and refresh reproduce the same applicants — mutation makes the shuffle unseeded;
  B3 a hired applicant is paid their own ask — mutation reverts `TryHireApplicant` to the posted
     wage, which is the exact regression 7.1 exists to prevent and nothing has tested until now.
Last done: 7.2c at `680c39e`. Suite green again at 26/0/0, and this time with mutation evidence:
M1 (the requirement stops testing the minimum level) reddens A1, A2 and the silence explanation;
M2 (a wage filter restored in `MatchAll`) reddens A3 and nothing else.
Updated: 2026-09-08, wake 95
Wakes: 101 · last full load at wake 95

DRIFT CHECK AT THE TENTH-WAKE FULL LOAD, and one thing failed it. The method says this file is a
header plus a stage/unit table plus a short decisions list. It is 478 lines. The RESUME BRIEF earns
its place — it is what a cold session reads — but the unit tables now carry eight stages of history
that the commit log records better, and a state file nobody can skim has the same failure mode as
the stale header I just cut. TRIM THE BODY WHEN F25 CLOSES, before starting 6.1: keep the brief, the
decisions, and the current stage's units; drop closed stages to one line each. Everything else is
already in the commits. Not doing it mid-stage, because the tables are load-bearing right now.
Everything else passed: one worker at a time, nothing committed without a suite run, mutation
evidence on every unit that added an assertion, cron alive, reports kept to the tables.

READ THE "RESUME BRIEF" SECTION BELOW THE HEADER FIRST — every finding's disposition, the answered
decisions and their scope, the stage-6 seams, the F06 placement, and the dependency order.

THE MOST USEFUL THING LEARNED THIS STAGE, and it must not be lost. Under M1 the per-minimum
"interested" counts still fell — 620, 350, 144, 54, 14, 0 — because `CountInterested` filters
independently of the matcher. An assertion resting on that number alone would have stayed GREEN
while the matcher had stopped filtering entirely. A1 went red only on its third clause, that a
demanding posting must QUEUE fewer people than an open one; the matcher had queued its full six for
a 20+ posting nobody on the planet qualified for. A3 behaved the same way under M2 — "interested"
was 573 either side, and only the queued count differed. **In this suite, `interested` comes from a
counting helper and `queued` comes from the matcher. An assertion about matching that reads only
`interested` is hollow.**

STILL OWED, and not asserted anywhere — unit 7.2d:
  - the waiting list is a SPREAD, not the top N by skill. This guards 7.2b's central decision and
    nothing tests it.
  - the same seed and refresh count reproduce the same applicants after a reload.
  - a hired applicant's contract rate equals their `openMarketAsk`. That is 7.1's whole point and it
    has no assertion at all.

WORTH KEEPING, learned the hard way this session: a suite run pops a game window the operator may be
sitting in front of, and their closing it looks exactly like infrastructure failure from this side.
Before reading a bridge timeout as a defect, check whether a human was at the keyboard.

Owed to the operator, all recorded in `docs/PENDING_PLAYTESTS.md`: fourteen play observations across
stages 1-5. Two matter more than the rest — the F15 save-compatibility check, the only change
touching saves that already exist, and the partial-delivery defect in `DeliverToColony`, which costs
the player silver and needs a design decision before it can be fixed.
Foreman: 10ee860 · source C:\dev\agent-foreman · https://github.com/Vector-Consulting-IA-Operacional/agent-foreman.git
Fallback: if `Skill(foreman)` is unknown, read `C:\dev\agent-foreman\skill\SKILL.md`, follow it,
then re-run its section 0.

<!-- Everything above this line is the header. A fresh session reads only the header. -->

## RESUME BRIEF — written 2026-09-08 for a context compaction

HEAD `a564c18` on `foreman/playtest-batch-2026-09-06`. Working tree: `FOREMAN.md` modified (this
edit), `Playtesting annotations.docx` untracked and not ours. Suite last green at 1476/0/17.

UPDATED AFTER THE COMPACTION, 2026-09-08 12:40. The stage-7 recon finished and is committed at
`a564c18` as `RECON_STAGE7.md`. It changes one thing in the picture below: **F25 is not blocked.**
It needs no schema bump and no new Harmony patch, because `JobPosting.cs:154` already persists each
applicant's `openMarketAsk` and `LaborCandidateService.cs:269-274` regenerates the census rather
than saving it. F23, F24 and F22 do need new persisted state; none of them needs a Harmony patch.
Unit 7.1 is running against F25 and is the first work in this batch that is neither recon nor
documentation since stage 5 closed.

**SUPERSEDED LATER THE SAME DAY: both operator decisions came back YES.** The schema may move
57→58 with a migration and prior-save verification, and one narrowly scoped observational patch may
go on crafting completion. F11, F07, F19, F20, F23, F24 and F22 are all unblocked; F06 is placed as
stage 9. Read the decisions section and "Next executable work" below — they are current, and any
sentence anywhere in this file calling something blocked is older than they are.

My one design call on F25, made rather than escalated because the source plan already decides the
substance: `wageOffered` stays exactly as it is saved, so old open postings keep their shape, and
new postings stop treating it as the binding price. That is what keeps F25 a no-bump change.

A worker IS RUNNING: unit 7.0, stage-7 recon, output at
`…\scratchpad\unit-7-0.out`. It is read-only and writes only `RECON_STAGE7.md`. Check it with
`grep -q "^tokens used"` before dispatching anything — one worker at a time.

### Every finding's disposition

| Finding | Stage | State |
|---|---|---|
| F01 silent auto-ready | 1 | DONE, mutation-proven, `4f2f319` + `db9627a` |
| F02 Cancel ends the produce loop | 1 | DONE, `bc2a46b` + `c00cb0c` |
| F13 auto-renew on the employee row | 1 | DONE, `41dc1f5`; its overflow later fixed by F16/F17 |
| F15 agreements default auto-ready on | 1 | DONE, `d48a1cf` + `e4d50c9` |
| F03 area Produce/Pause/Stop | 2 | DONE, `ece7083` `e0ada0d` `16411e5` `c01d7db`, tests `f49db5a` `57e0981` |
| F04 programmable produce | 2 | DONE for indefinite + produce-until-target, `4b14abb` `5656c75` `30b784e`. Worker eligibility, skill and quality controls NOT built — they live in vanilla's construction job, not the loop |
| F14 collapsible contracts | 3 | DONE both lists, `02d5710` `275577a` `c67cece` `7571391` |
| F16/F17 employee card | 3 | DONE, `b1b5c7f`; also fixed F13's measured overflow, 1367f → 620f against 720f |
| F18 procurement unit price | 3 | DONE, `8d533bc`. No assertions, deliberately — private UI string |
| F10 procurement progression | 3 | DONE as an earned gate, `799d673`. Count is settlement-wide, not per product |
| F05 receiving locations | 4 | DONE, `231df04` `caf4340` `74aff0f` `912a5fe` |
| F12 programmed caravans | 4 | PART-BUILT. Only `OrderAvailability` + `GetAvailability` exist (`c19b9cb`, tests `8cb06a9`) — an order can report available/required. NOT built: the caravan itself, pawn/animal selection, the configuration surface, recurrence, multi-map routing, and the waiting behaviour. The mod has NO caravan formation of its own |
| F21 logistics meaningful | 5 | PART-BUILT. `LogisticsQuote` is one owner for cost/time/method (`2efd13f`), drift-guarded (`6d6366a`), and the cost is disclosed (`8da0d9f` + `83f2340`). NOT built: route difficulty, provisions, settlement capability, real transport-method choice — none of those models exist |
| F11 progressive RFQ responses | 5 | BLOCKED — decision 1 below |
| F07 commitment vs production | 6 | BLOCKED — decisions 1 AND 2 |
| F19 material replacement cost | 6 | BLOCKED — decision 1 |
| F20 labour from actual work | 6 | BLOCKED — decisions 1 AND 2 |
| F25 F23 F24 F22 labour market | 7 | recon 7.0 running, nothing built |
| F08 F09 commercial relationships | 8 | not started |
| **F06 optional apparel policies** | **NONE** | **NOT IN ANY STAGE — see the gap below** |

### THE F06 GAP — a real omission, found 2026-09-08

`docs/PLAYTEST_BATCH_SOURCE_PLAN.md:338` is "F06 — Optional apparel policies for employees". The
stage table in this file covers 24 findings and F06 is not one of them. The table predates this
session and the omission was inherited, not introduced, but it was also not caught until now. F06
has had NO recon and NO work.

**DISPOSITION, decided 2026-09-08: F06 becomes STAGE 9, and the run cannot complete without it.**
The operator required an executable stage or disposition, and dropping it was not chosen. Its
natural home was stage 3 (employee UX), which is closed, and reopening a closed stage to bolt on an
unreconnoitred finding is worse than giving it its own. Stage 9 runs last because it depends on
nothing: apparel policy is per-employee configuration, not economy state. It starts with a recon
unit — 9.0 — because unlike every other finding in this batch, nobody has yet established where it
would attach or whether it needs persisted state of its own. If that recon finds it needs a schema
change a default cannot express, that is the second bump and it returns to the operator.

### THE TWO OPERATOR DECISIONS — BOTH ANSWERED YES, 2026-09-08

The questions, kept verbatim because the answers only mean something beside them:

1. **May `IntercolonyWorldComponent.CurrentSaveVersion` move from 57 to 58, with a migration?**
   Its comment requires a bump plus a `MigrateIfNeeded` step whenever the saved shape changes. No
   stage in this batch has touched the schema. Needed by: F11 (a pending-response queue must
   survive a save), F07 and F20 (rolling history), F19 (durable price history, conditional).
2. **May a Harmony patch be added on vanilla's crafting completion?** Needed by F07 and F20.

**ANSWER TO 1: YES.** 57 → 58, with the narrow migration this batch requires, **including
prior-save verification** — the operator asked for that explicitly and it is not optional. A
`-quicktest` launch cannot prove a migration, because it generates a world already at the current
schema and never enters the migration path; the autostart-a-copy technique in `CLAUDE.md` is the
route, and the copy gets deleted afterwards or it hijacks every later launch.

**ANSWER TO 2: YES**, for **one narrowly scoped observational patch** on the crafting-completion
seam. Observational: it reads that something was made and by whom, and changes nothing about what
vanilla does. This repo keeps patches deliberately few (DESIGN.md §63) and there are four today
(`HarmonyPatches.cs:25`, `:64`, `:99`, `:165`); this makes five and that is the whole allowance.

**THE F20 QUALIFICATION, from the operator and binding:** actual-work attribution is preferred
**only where technically defensible**. If the completion seam would produce false precision — and
attributing a product's whole labour cost to whoever happened to finish it is exactly that, since
the seam carries a finisher, not hours worked — or if getting real hours would mean materially more
invasive instrumentation, then use the source plan's explicitly authorised **relevant-workforce
approximation** instead. That call gets made at F20's unit, on what the seam actually yields, and
the reasoning goes in the commit body either way.

**Scope of the bump.** One bump, to 58, designed to carry this batch's new persisted state:
production history (F07/F20), the RFQ pending queue (F11), equipment bond state (F23), urgent
dispatch (F24), the reverse listing and offer queue (F22). Later additions inside the batch ride on
58 as additive nodes with safe defaults — `Scribe_Values.Look` omits a value equal to its default
(`reference/decompiled/Verse/Scribe_Values.cs:29`), so an absent node IS the old shape and reads
correctly. If a stage ever needs a shape change a default cannot express, that is a second bump and
it comes back to the operator.

### Stage 6 seams, so they need not be rediscovered

Full detail is in `RECON_STAGE6.md` (committed, `2cc12d5`). The load-bearing findings:

- **Nothing in the mod observes an item being COMPLETED.** Production code polls stored stack counts
  (`ProduceLoopMapComponent.cs:180`, `:202`, `:205`). The only completion handlers are sales and
  purchase transitions (`SalesOrderService.cs:527` `:560` `:571`, `PurchaseOrderService.cs:659`
  `:673`). F07 explicitly forbids inferring production from stockpile change, so its number cannot
  be computed at all today.
- **The vanilla seam is `GenRecipe`**, which calls `Notify_RecipeProduced(worker)` at
  `reference/decompiled/Verse/GenRecipe.cs:36`. It carries the worker pawn, so it is the only place
  BOTH F07 (something was made) and F20 (who made it) could be observed. Reaching it means a comp on
  every producible thing, or a Harmony patch on a hot crafting path.
- **Nothing records employee work on a product.** `PayrollService.cs:339` `workedTicks` is an
  employment period, not production work.
- **The Business view's report service is `Source/Intercolony/Core/BusinessReportService.cs`**; note
  `:137`-`:143`, where suspended agreements are deliberately treated as live. Whether F07's "active"
  rows should include them is an open product question.

### Next executable work, in dependency order — rewritten 2026-09-08 after both answers

Nothing in this batch is blocked on a human any more. What remains is ordering.

1. **Finish F25** — units 7.1 to 7.4. It is already running and needs neither the bump nor the
   patch, so it stays in front regardless. Do not interrupt it; the operator said so and
   one-worker-at-a-time says so anyway.
2. **The schema unit, 6.1.** 57 → 58, the `MigrateIfNeeded` step, and the persisted production
   history F07 and F20 both read. This is first among the schema-dependent work because everything
   else additive rides on the shape it establishes. It ends with **prior-save verification on a
   copy of a real pre-58 save**, not a `-quicktest` world.
3. **The observational patch, 6.2**, on `GenRecipe`'s `Notify_RecipeProduced(worker)`
   (`reference/decompiled/Verse/GenRecipe.cs:36`), feeding the ledger from 6.1. One patch, reads
   only. This is the seam nothing in the mod has today.
4. **F07, 6.3** — the committed-per-day against actually-completed-per-day rows in the Business
   view, off the ledger. Open product question to settle at the unit: whether "active" includes the
   suspended agreements `BusinessReportService.cs:137-143` deliberately treats as live.
5. **F19, 6.4** — value self-produced inputs at what buying them would have cost.
6. **F20, 6.5** — labour attribution, under the qualification above. Decide actual-work vs
   relevant-workforce on what the seam yields, and say which in the commit.
7. **F11** — the RFQ pending-response queue, additive on 58. Stage 5 is otherwise closed; this
   reopens it for one unit.
8. **F23, F24, F22** — the rest of stage 7, additive on 58, none needing a patch.
9. **Stage 8** — F08, F09. Recon first.
10. **Stage 9 — F06**, see its disposition above. Recon first; it has had none.

Then the run's remaining obligation is the play sitting, not code.

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
| ⬜ | 6 — Business intelligence and costing | F07, F19, F20 | UNBLOCKED 2026-09-08, next after F25 |
| 🔨 | 7 — Two-sided labor market | F25, F23, F24, F22 | F25 building; the other three unblocked |
| ⬜ | 8 — Commercial relationships | F08, F09 | not started |
| ⬜ | 9 — Optional apparel policies | F06 | placed 2026-09-08, recon first, runs last |

Also reopening for one unit: **stage 5's F11**, the RFQ pending-response queue, unblocked by the
schema answer.

## Units — stage 7

| | Unit | Status |
|---|---|---|
| 🔨 | 7.0 — recon: what stage 7 can build unblocked | worker running |

## Units — stage 6 (blocked)

| | Unit | Status |
|---|---|---|
| ✅ | 6.0 — recon: the seams for F07, F19 and F20 | 2cc12d5 |

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

- **2026-09-08 — STAGE 6 IS BLOCKED and the schema decision now gates two stages.** F07 needs a
  number the mod cannot compute: nothing observes an item being COMPLETED, and the finding
  explicitly forbids inferring production from stockpile change. The only vanilla seam is
  `GenRecipe`, which calls `Notify_RecipeProduced` with the worker pawn
  (`reference/decompiled/Verse/GenRecipe.cs:36`) — reaching it means a comp on every producible
  thing, or a Harmony patch on a hot crafting path in a repo whose rule is that patches stay few.
  That same seam is the only place F20 could learn who worked on what. All three of F07, F19 and F20
  then need persisted rolling history, hence the schema.
  So there are now TWO decisions, and they are yours:
  1. may `CurrentSaveVersion` move from 57 to 58, with a migration? F11 and all of stage 6 need it;
  2. may a Harmony patch be added on vanilla's crafting completion? F07 and F20 need it.
  Answering (1) alone unblocks F11 and F19. Answering both unblocks stage 6 entirely. Answering
  neither leaves four findings unbuilt, which is a legitimate outcome and would be recorded as such.

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
