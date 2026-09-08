# Foreman state — Intercolony

Stage: 6 — Business intelligence and costing (F07, F19, F20). Stage 7's F25 is closed; F23, F24 and
F22 remain open and come after stage 6 in the dependency order.
Unit: 6.1 — schema 57→58, the migration, and the persisted production history F07 and F20 read
Worker: none
Last done: 7.8 — trimmed this file. Before it, F25 closed complete at `d8ad4ed`.
Updated: 2026-09-08, wake 115
Wakes: 115 · last full load at wake 111

NOTHING IS BLOCKED ON A HUMAN. The operator answered both decisions YES on 2026-09-08: the schema
may move 57→58 with a migration and prior-save verification, and one narrowly scoped observational
Harmony patch may go on vanilla's crafting completion. F06 is placed as stage 9. Scope, wording and
the F20 qualification are in the RESUME BRIEF below, which is what a cold session should read first.

WHAT 6.1 NEEDS FROM THE OPERATOR, and it is the only thing outstanding: **prior-save verification
needs a real pre-58 save.** A `-quicktest` launch generates a world already at the current schema
and never enters the migration path, so it cannot prove a migration. The route is the
autostart-a-copy technique in `CLAUDE.md` — autostart a COPY, never the original, and delete it
afterwards or it hijacks every later launch including `-Fresh`.

F25's RESULT, so nobody re-derives it: the posting dialog asks for a requirement and reports the
going rate; who applies is decided by the requirement alone; a worker who qualifies for several
postings takes the one that pays them most; the waiting list is a deterministic seeded spread rather
than the strongest few; a hired applicant is paid their own ask. Suite 1484/0/16, log clean, on two
independently generated worlds. Every assertion was watched going red for the right reason.

TWO DEFECTS WERE INTRODUCED AND FIXED INSIDE F25, both mine, both from one reasoning error — I made
the dialog stop writing `wageOffered` and checked that the field still persisted without checking
who READS it. Postings were silently deleted on load (`74e42fe`, asserted `3125bd6`) and could not
be created at all (`fbb5290`). **When a field stops being written, grep every reader before assuming
it can quietly hold its default.** A default is only harmless if nothing treats it as meaningful,
and a `> 0` validity check treats zero as corruption.

THREE THINGS THIS STAGE TAUGHT THE SUITE, worth carrying into stage 6:
  - **The seam nobody asserts is the seam between the caller and the service.** A suite that only
    ever calls the service will never see a dead user-facing path; 29 assertions passed over a
    feature the player could not use.
  - **`interested` comes from a counting helper and `queued` comes from the matcher.** An assertion
    about matching that reads only the helper is hollow and stays green while matching is broken.
  - **A skip is not evidence.** A mutation run that skips the target assertion proves nothing; read
    the skip line, never just the counts.

Owed to the operator, all recorded in `docs/PENDING_PLAYTESTS.md`: sixteen play observations across
stages 1-7. Three matter more than the rest — the F15 save-compatibility check, the partial-delivery
defect in `DeliverToColony` which costs the player silver and needs a design decision, and F25's
market read, which asks whether the waiting list actually feels like a market to choose from.
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

### Open items retained from the operator list

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

- **2026-09-07** — F15 changes only the two C# field initializers to true and deliberately leaves
  both `Scribe_Values.Look` defaults at false. A true Scribe default would switch automation on
  inside saves the player already has, including agreements they had turned off by hand. New
  agreements default on; loaded ones keep what was saved.

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

## Units — stage 7

| | Unit | Status |
|---|---|---|
| 🔨 | 7.0 — recon: what stage 7 can build unblocked | worker running |

## Closed-stage unit history

The unit history for closed stages 1–6 lives in the git log on branch
`foreman/playtest-batch-2026-09-06`. Run `git log --oneline` on this branch to see it.
