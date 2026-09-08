# Foreman state — Intercolony

Stage: 7 — F23 in progress; F24 and F22 after it. **STAGES 1 THROUGH 6 ARE CLOSED.**
Unit: 7.6c — assertions for the equipment bond
Worker: running — `…\scratchpad\unit-7-6c.out`
Last done: 7.6b at `a173619` — F23's bond settles on every one of the nine paths that end an
employment. Suite 1506/0/16, log clean.
Updated: 2026-09-08, wake 172
Wakes: 172 · last full load at wake 172

READ THE "RESUME BRIEF" BELOW. It is what a session with no memory should trust.

<!-- ONE WORKING NOTE, AT MOST ABOUT FIFTEEN LINES. Replace it each wake; never prepend a second.
     If it needs more than that, the surplus belongs in a commit message. The header has run away
     three times and crept back a fourth by the note simply getting longer each wake. -->

WORKING NOTE — wake 172. F23's first two parts are whole: the gear an employee arrives with is
recorded, valued, bonded, disclosed at hire beside the wage, and refunded proportionally when it
comes back. 7.6c is adding the assertions, which two commits of economic code currently lack — E3
(part returned is part refunded, premium included) and E4 (a bond settles ONCE) are the two that
matter, the second because two paths both firing would pay the player from nothing.
Not built of F23, deliberately: equipment tiers, availability gating, and the severe consequence for
stripping body modifications. Remaining in the run after F23: F24, F22, then stage 8 (F08, F09) and
stage 9 (F06), each starting with recon.
For the play sitting: hiring an equipped worker now costs roughly 70% more up front — 918 prepaid
wages against a 641 bond on one real example.

STANDING RULES THIS RUN HAS PAID FOR, kept here because they survive a compaction:
  - **When a field stops being written, grep every reader.** A `> 0` check treats zero as
    corruption. Two defects at once.
  - **When something stops happening immediately, find everything that assumed it was instant.**
    A third defect: replies scheduled past a deadline that then deleted them.
  - **The seam nobody asserts is the one between the caller and the service.**
  - **A skip is not evidence**, and a mutation run that skips its target proves nothing.
  - **A mutation that fails to compile looks exactly like one that found nothing.** Assert the
    anchor is unique before editing; read the run's log, not the summary line.
  - **Never let a zero mean unknown.** Say it in words.
  - **A charge the player was never shown is worse than the problem it fixes.** Disclose at the
    moment of decision, not at the moment of collection.
  - Use `dev.ps1 bridge -Save`, not `run -Save`: only the bridge path stages the autostart copy.
  - Write files with the file tools; PowerShell `Get-Content` + `Out-File` double-encodes UTF-8.

Owed to the operator, all in `docs/PENDING_PLAYTESTS.md`: twenty play observations across stages
1-7. The three that matter most are the F15 save-compatibility check, the partial-delivery defect in
`DeliverToColony` which costs the player silver and needs a design decision, and F20's equal wage
split — someone who only ever makes chairs still has half their wage charged to tables.
Foreman: 10ee860 · source C:\dev\agent-foreman · https://github.com/Vector-Consulting-IA-Operacional/agent-foreman.git
Fallback: if `Skill(foreman)` is unknown, read `C:\dev\agent-foreman\skill\SKILL.md`, follow it,
then re-run its section 0.

<!-- Everything above this line is the header. A fresh session reads only the header. -->

## RESUME BRIEF — current at HEAD `47c1f5d`, 2026-09-08

Branch: `foreman/playtest-batch-2026-09-06`. HEAD is `47c1f5d`, `test: a reply is never scheduled
past the deadline that would delete it`. The latest suite figure to carry forward is **1503 passed /
0 failed / 15 skipped**.

F25's design call remains deliberate: `wageOffered` stays exactly as it is saved, so old open
postings keep their shape, and new postings stop treating it as the binding price. That is what
keeps F25 a no-bump change.

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
| F11 progressive RFQ responses | 5 | BUILT, mutation-proven, `bd04ff6` + `47c1f5d`: quotes are still generated at request time and only their reveal is delayed; no price change, and one final letter rather than one per reply |
| F07 commitment vs production | 6 | BUILT, mutation-proven, `e1467fd` + `5833061`: committed/day is compared with ledger-completed/day over five days; no stockpile inference or worker attribution, and suspended agreements remain counted as live |
| F19 material replacement cost | 6 | BUILT, mutation-proven, `fae0dbe` + `be507f9`: direct ingredients only, deterministic and non-recursive; the existing finished-good figure is unchanged |
| F20 labour from actual work | 6 | BUILT, mutation-proven, `86f3868` + `78d6eba`: the relevant-workforce approximation shares eligible wages across eligible goods; measured-time attribution is not built |
| F25 buyer-side labour market | 7 | BUILT, mutation-proven, `0189a8a` + `680c39e` + `3125bd6` + `fbb5290`: requirement-first postings, worker asks, a seeded spread, own-ask pay, and the save/create seams; no new persisted state or Harmony patch, and no reverse market |
| F23 equipment and bond state | 7 | PART-BUILT, `ed99423` + `a173619`. The gear an employee ARRIVES with is recorded, valued at replacement plus a 10% premium, disclosed as its own row at hire beside the wage, charged there, and refunded proportionally item by item when the contract ends — on all nine ending paths, idempotently. NOT built: equipment tiers, availability gating by settlement wealth/tech/scarcity, and the severe consequence for stripping body modifications. Assertions in progress at 7.6c |
| F24 urgent dispatch | 7 | UNBLOCKED, NOT STARTED; needs new persisted state additive on schema 58; no Harmony patch |
| F22 reverse listing and offer queue | 7 | UNBLOCKED, NOT STARTED; needs new persisted state additive on schema 58; no Harmony patch |
| F08 F09 commercial relationships | 8 | not started |
| **F06 optional apparel policies** | **9** | **PLACED IN STAGE 9 — not started, recon first; see the gap below** |

### THE F06 GAP — a real omission, found 2026-09-08

`docs/PLAYTEST_BATCH_SOURCE_PLAN.md:338` is "F06 — Optional apparel policies for employees". The
original stage table covered 24 of the 25 source-plan findings and omitted F06; the table above now
places it in stage 9. The omission was inherited, not introduced, but it was also not caught until
now. F06 has had NO recon and NO work.

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
   stage in this batch had touched the schema when this question was raised. It was needed by F11
   (a pending-response queue must survive a save) and F07/F20 (rolling history); durable F19 price
   history remains conditional, while the shipped F19 slice is derived direct-input costing.
2. **May a Harmony patch be added on vanilla's crafting completion?** Needed by F07 and F20.

**ANSWER TO 1: YES, and it is now landed.** The schema is 58, with the narrow migration this batch
requires, **including prior-save verification**. The real pre-58 `Edithor Alliance` save migrated in
one step with no exceptions; a `-quicktest` world would not have proved that path.

**ANSWER TO 2: YES, and it is now landed** as **one narrowly scoped observational patch** on the
crafting-completion seam. It reads that something was made and by whom, and changes nothing about
what vanilla does. This is the fifth Harmony patch and the whole allowance.

**THE F20 QUALIFICATION, from the operator and binding:** actual-work attribution is preferred
**only where technically defensible**. If the completion seam would produce false precision — and
attributing a product's whole labour cost to whoever happened to finish it is exactly that, since
the seam carries a finisher, not hours worked — or if getting real hours would mean materially more
invasive instrumentation, then use the source plan's explicitly authorised **relevant-workforce
approximation** instead. F20 made that call on the seam it actually received: the completion
observation carries a finisher, not hours worked, so the shipped feature uses the approximation and
does not claim measured time.

**Scope of the bump.** One bump, to 58, designed to carry this batch's new persisted state:
production history (F07/F20), the RFQ pending queue (F11), equipment bond state (F23), urgent
dispatch (F24), the reverse listing and offer queue (F22). Later additions inside the batch ride on
58 as additive nodes with safe defaults — `Scribe_Values.Look` omits a value equal to its default
(`reference/decompiled/Verse/Scribe_Values.cs:29`), so an absent node IS the old shape and reads
correctly. If a stage ever needs a shape change a default cannot express, that is a second bump and
it comes back to the operator.

### Stage 6 seams, so they need not be rediscovered

Full detail is in `RECON_STAGE6.md` (committed, `2cc12d5`). The load-bearing findings and the seam
that actually shipped are:

- **Before stage 6, nothing in the mod observed an item being COMPLETED.** Production code polled
  stored stack counts (`ProduceLoopMapComponent.cs:180`, `:202`, `:205`), and F07 explicitly forbids
  inferring production from stockpile change. The completed figure now comes from the ledger instead.
- **The shipped vanilla seam is `RecordsUtility.Notify_BillDone(Pawn billDoer, List<Thing> products)`**
  (`reference/decompiled/RimWorld/RecordsUtility.cs:52`), called once a bill's products are
  materialised. The fifth Harmony patch records each player-faction product's def and stack count;
  it carries who finished the bill, not elapsed work.
- **Nothing records employee work on a product.** `PayrollService.cs:339` `workedTicks` is an
  employment period, not production work. That is why F20 uses the authorised relevant-workforce
  approximation rather than claiming measured time.
- **The Business view's report service is `Source/Intercolony/Core/BusinessReportService.cs`**; note
  `:137`-`:143`, where suspended agreements are deliberately treated as live. F07's shipped row
  keeps that existing meaning, and play still has to judge whether it reads well.

### Next executable work, in dependency order

The built findings are closed. What remains is:

1. **F23** — not started; its new persisted state is additive on schema 58 and needs no Harmony
   patch.
2. **F24** — not started; its new persisted state is additive on schema 58 and needs no Harmony
   patch.
3. **F22** — not started; its new persisted state is additive on schema 58 and needs no Harmony
   patch.
4. **Stage 8** — F08 and F09. Recon first.
5. **Stage 9** — F06. Recon first; it has had none.

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
  The shipped bounded slice is the half that is genuinely useful and testable on its own: the
  order's availability expressed as available/required, and the rule that a short order WAITS and
  says so rather than leaving partial. The caravan formation itself was not attempted here. Say if
  you would rather it were, or would rather stage 4 stop after F05.

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
| ✅ | 5 — Market geography | F21, F11 | closed 2026-09-08 |
| ✅ | 6 — Business intelligence and costing | F07, F19, F20 | closed 2026-09-08 |
| 🔨 | 7 — Two-sided labor market | F25, F23, F24, F22 | F25 closed; F23, F24 and F22 not started |
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
| ⬜ | F23, F24 and F22 | not started; next executable work |

## Closed-stage unit history

The unit history for closed stages 1–6 lives in the git log on branch
`foreman/playtest-batch-2026-09-06`. Run `git log --oneline` on this branch to see it.
