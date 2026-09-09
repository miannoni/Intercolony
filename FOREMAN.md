# Foreman state — Intercolony

Stage: 9 — F06, the last finding, recon first. **STAGES 1-6 AND 8 ARE CLOSED.** Stage 7 is done apart from F22, which awaits the operator.
Unit: NONE. **THE RUN HAS REACHED A CLEAN HALT AND EVERYTHING EXECUTABLE IS DONE.** Both remaining
findings need the operator: F22 needs a decision, F06 needs a sixth and seventh Harmony patch.
Worker: idle
Last done: the batch milestone record in `PROGRESS.md`. The whole suite on a fresh world is
**1536 passed, 0 failed, 17 skipped, exit 0**.
Updated: 2026-09-09 07:05
Foreman load: 2026-09-09 07:03
Foreman: e46c835 · source C:\dev\agent-foreman · https://github.com/Vector-Consulting-IA-Operacional/agent-foreman.git
Fallback: if `Skill(foreman)` is unknown, read `C:\dev\agent-foreman\skill\SKILL.md` and follow it, then re-run its section 0.

<!-- Everything above this line is the header. A fresh session reads only the header. -->

## 7.9 is accepted, and it uncovered a worse defect than the one it was sent to fix

7.9 is committed at `2d737e7`: every site that shows or sums a daily wage names both the ask and
what the colony is charged, and the business payroll estimate and F20's direct-labour figure
stopped summing the raw figure. Labor 48/0/0, payroll 42/0/0, both exit 0. Production only — its
assertions are still owed.

**THE DEFECT IT EXPOSED — `dailyWage` MEANS TWO DIFFERENT THINGS DEPENDING ON WHICH HIRE PATH MADE
THE CONTRACT, AND ONE OF THEM IS CHARGED TWICE.** All verified in the source:

  - `EmploymentService.cs:138` — the DIRECT hire path stores `EffectiveDailyWage(structure, ask)`,
    the charged rate, and says so in its comment: "payroll reads dailyWage straight off it".
  - `EmploymentContract.cs:353` — `PeriodPayment => PeriodCost(wageStructure, dailyWage)`, and
    `WageStructure.cs:185` applies the premium AGAIN. **A 100/day ask on Daily terms is quoted at
    135, stored as 135, and charged 182.**
  - `TryHireApplicant`, F25's path, stores the raw ask, so the same field there means the ask and
    the same payroll charges it once, correctly.
  - `PayrollService.cs:88` and `:349` — the PARTIAL-period branches use `contract.dailyWage` raw,
    so a part period is charged at a different rate from a full one in the same function.

**THE RECON'S ANSWER, 2026-09-09, and one correction to me.** I said shipped 1.0 used the direct
path only. It did not: the posting path is `8afd6a3`, 2026-07-30, and `git merge-base --is-ancestor
8afd6a3 v1.0.0` succeeds — I checked. `v1.0.0`'s `TryHireApplicant` stores `posting.wageOffered`
raw. **So the released game already contains both cohorts, in the same save, with nothing in the
persisted shape to tell them apart.** F25 changed which raw number the posting path stores; it did
not create the ambiguity.

Also confirmed, and worse than the payroll double: `EmploymentService.cs:228`'s hire message passes
the already-charged local wage into `Explain`, which applies the premium a second time — so the
message the player reads after hiring quotes a different number from the dialog they just accepted.

**DECISION — `dailyWage` MEANS THE WORKER'S RAW ASK. The charged rate is always derived.** Recorded
in Decisions below. The alternative was to store the charged rate and add a persisted `workerAsk`
node; it was rejected because neither option can rescue both legacy cohorts, and this one needs no
new persisted state, matches the convention every `WageStructureUtility` method already follows,
and makes no legacy contract worse than it is today — a legacy direct-hire Daily contract is
already charged the doubled rate on full periods, so consistency costs it nothing.

What it cannot fix: a legacy direct-hire contract will keep displaying its stored 135 as the ask.
There is no provenance in the save to recover the real 100 from. Accepted.

Cut into units: **7.9b1** one owner for the charged rate and every payroll path through it
(accepted, `7e230ad`), **7.9b3a** assertions for the two payroll branches that have none,
**7.9b2** the writer at `:138` and the hire message at `:228`, **7.9b3b** the rest of the
assertions, which 7.9 also still owes.

**A GAP THE MUTATION FOUND, and it is why 7.9b3a jumped the queue.** Reverting the partial
scheduled period to the raw stored wage left `cash-flow` at 11/0/0. Not a hollow assertion: the
fixture cannot reach the branch, because its contract ends half a day into a one-day Daily interval,
so `daysLeftInTerm` ceilings to 1 and the guard `1 < 1` sends it down the FULL period path. **The
partial scheduled period and the end-of-employment settlement have no assertion at all**, and did
not before this batch either. A colony could be billed one rate for a full period and another for a
part period with nothing to catch it.

## What 7.9 was asked to do

Fix known defect 1, which I verified in the code myself. `EmploymentService.TryHireApplicant`
stores an applicant's raw `openMarketAsk` in `contract.dailyWage`, the applicant row tells the
player they are "paid <ask>/day", and Daily payroll then multiplies by
`WageStructureUtility.EffectiveDailyWage`'s 35% premium (`WageStructure.cs:53-58`,
`EmploymentContract.cs:353`). So an ask of 100/day is shown and charged at 135/day.

**THE ECONOMICS ARE NOT THE BUG.** The premium is real — paying daily rather than up front costs
more, which is what `WageStructure` is for. The instruction was to DISCLOSE BOTH numbers wherever a
wage is shown, to make payroll and runway figures use the CHARGED rate and "what this worker asks"
figures use the ask, and to change nothing about `EffectiveDailyWage`, `PeriodCost`, the premium
constant, or what is stored in `contract.dailyWage`. It was also asked to report every site that
displays or sums a daily wage, and to check the business payroll estimate specifically, which the
audit says sums the raw figure and therefore understates what employees cost.

## Method version note — migrated to `e46c835`, 2026-09-08

**There is no Sol audit lane any more, and no review state.** `Run started`, `Run base`,
`Review checkpoint` and `Review` were removed from the header by this migration; the findings the
single review it ran produced are kept below as ordinary units, because they are real defects in
shipped code and a method migration must not reopen or discard settled work.

The method now is: **Sol high read-only for reconnaissance only**, **Luna max workspace-write for
implementation**, one recon/work lane so the two never overlap and two Sols never run at once, a
15-minute heartbeat, and a full method reload about every two hours of wall clock (`Foreman load`),
never a wake count.

## F22 — Sol recon, 2026-09-09. NOT STARTED, and it needs an operator decision

Read-only; the tree was untouched and the evidence spot-checked.

**F22 IS ABOUT NINE UNITS** before balancing or playtesting: custody proof, persisted
listing/offer/assignment records, the Supply page and its eligibility rules, deterministic offer
generation, offer display and expiry, departure with save/load and map retargeting, return and
payment, then training and risk as two more.

**THE HAZARD THAT SHAPES EVERYTHING: a bare custom world pawn is NOT recognised as borrowed by
vanilla's game-over and population systems** (`QuestUtility.cs:734`, `GameEnder.cs:94`). If the last
colonist leaves on an F22 job, the game could end incorrectly. So the first unit must be the custody
proof — vanilla lending versus a custom world-pawn lifecycle — because that choice changes the
persisted shape and the last-colonist behaviour. Everything else waits on it.

Also established: an away pawn is handled by NO Intercolony map-removal or abandonment path, and
vanilla's lending waits for a valid return map rather than resolving. F22 needs an explicit
abandonment policy; the recon recommends following vanilla and holding the pawn away.

**SIX QUESTIONS THE SOURCE PLAN DOES NOT ANSWER**, and they are design decisions rather than code:
what makes a colonist eligible; whether minimum compensation is daily, total or take-home, and when
it is paid; what job determines the training a returning colonist gains; what drives a settlement's
labour demand, since no such field exists; what happens to carried inventory, bonded animals, beds
and titles on departure; and what happens if no player map ever returns.

## 7.11 — what the plan actually says about Pause and Stop

They differ ON PURPOSE, and the difference is the whole unit
(`docs/PLAYTEST_BATCH_SOURCE_PLAN.md:125-137`):

  - **Pause** — construction under way may finish, and once installed **it remains installed**. So
    an outstanding Uninstall designation has to be cancelled, or the object vanishes while paused
    and the paused loop refuses to replace it.
  - **Stop** — a committed uninstall **may finish**. Stop cancels nothing.

Making the two behave alike would be a misreading in either direction. Accepted cost, recorded
rather than discovered later: nothing in a designation records who placed it, so a player's own
hand-placed Uninstall on the loop's building is cancelled by Pause too.

## THE RUN IS AT A CLEAN HALT — TWO QUESTIONS FOR THE OPERATOR, 2026-09-09

Twenty-three of twenty-five findings are built, asserted and disclosed across eight stages. The
whole suite on a fresh world is 1536 / 0 / 17, exit 0. `PROGRESS.md` carries the milestone record
and `docs/PENDING_PLAYTESTS.md` carries everything a suite cannot settle.

**Nothing further can be dispatched without an answer to one of these:**

  1. **F22** — start it, take only the custody proof, or drop it? About nine units, and the first
     one must be the custody proof because of the game-over hazard.
  2. **F06** — may the Harmony allowance go from five patches to seven? Without that the finding
     cannot be built at all. And separately: should apparel-policy removal that leaves an
     employee's own gear in colony storage count as returned for the equipment bond?

Nothing else in the batch is waiting on anything. The unbuilt parts of F12, F21, F23 and F24 were
scoped out deliberately and are recorded as such.

## Why stage 9 is blocked — F06 needs two more Harmony patches

Sol recon, 2026-09-09, read-only. I verified the two gates and the patch count myself.

**An employee already has everything F06 needs except permission from vanilla.** After the faction
transfer they carry a real `Pawn_OutfitTracker` with a real `ApparelPolicy`
(`EmploymentService.cs:890`, `PawnComponentsUtility.cs:280`), and the tracker is saved with the
pawn. But they are a quest lodger, and vanilla vetoes them **twice, deliberately**:

  - `PawnColumnWorker_Outfit.cs:41` — the Assign tab shows "Unchangeable" instead of the dropdown.
  - `JobGiver_OptimizeApparel.cs:68` — the optimizer returns immediately, so nothing is enforced.

Both confirmed by reading the decompiled source. **So F06 cannot be built without patching those
two seams**, and there is no non-Harmony route: they are private vanilla decisions inside vanilla
methods.

**THIS COLLIDES WITH A CONSTRAINT THE OPERATOR SET.** The Harmony allowance was declared fully
spent at five patches when the crafting-completion observer landed — `HarmonyPatches.cs` lines 33,
71, 115, 172 and 220, counted just now. F06 needs a sixth and a seventh.

**Everything else about F06 is small and needs nothing.** No new apparel system, no filters, no
uniform model, no second schema bump: one additive `bool` on the contract, default false, absent on
an old save meaning unmanaged. Recon's honest estimate is twelve units.

**A SECOND OPERATOR QUESTION, from the same recon and worth answering together:** vanilla's
optimizer can remove disallowed worn apparel and haul it to storage
(`JobGiver_OptimizeApparel.cs:88`). F23's bond only matches items still on the pawn
(`EmploymentEquipment.cs:280`, `:384`), so a policy could strip an employee's own coat into a
stockpile and the colony would keep the bond for gear it did not take. F23 distinguishes returned
from deliberately retained; it says nothing about policy-driven removal.

## Stage 8 — the recon, and what I decided from it, 2026-09-09

Sol recon, read-only, tree untouched. Three load-bearing claims spot-checked against the source
myself: commercial reputation is per settlement keyed by `WorldObject.ID`
(`IntercolonyWorldComponent.cs:522-535`), Preferred begins at 80 (`CommercialReputation.cs:78-86`),
and **vanilla turns a faction into an ALLY at goodwill 75** (`FactionRelation.cs:34-36`).

**No second schema bump.** F08 needs no persisted state at all. F09 needs two additive nodes on
`EmploymentContract` with zero defaults, which ride 58 legitimately.

**F08 is 3-4 units. F09 is 7-9 and is a small system**, because nothing in this mod samples mood
today and F09 is defined in terms of consolidated employment experience.

**DECIDED, from the recon and the plan's explicit "balance is open" (`:616-621`). These are
starting values for the play sitting, not laws:**

  - **D1 — eligibility.** Any settlement at Preferred, 80 or above, whose CURRENT owning faction is
    not hostile and not at war. Live owning faction, never the saved faction-name string, because
    settlements change hands and the existing lookup already tracks that.
  - **D2 — one faction, one tick.** Deduplicate per faction: three Preferred towns of one faction
    produce one application, not three.
  - **D3 — the ceiling is 60, and that number is the whole safety property.** Vanilla allies at 75.
    F08 must never be a hostility-to-alliance engine, so commercial pressure stops well below the
    threshold that would create one. `+1` per quadrum, and nothing at all once base goodwill is at
    or above the ceiling.
  - **D4 — cap against BASE goodwill, and skip while a goodwill situation is suppressing the
    effective value**, so credit cannot accumulate invisibly and then appear when a diplomatic
    restriction expires.
  - **D5 — no double credit.** Sale-price generosity already moves goodwill on its own
    (`SalesOrderService.cs:588-647`) and F10's purchase count is already inside the commercial
    score. F08 reads the score and nothing else.

**F09's decisions, same authority — the plan leaves magnitude and window open (`:372-377`), so
these are starting values for the play sitting:**

  - **D6 — a lifetime average, never a moment.** Sample the worker's mood once a day and keep a
    running total and count on the contract. The plan's own words are that momentary mood must not
    be farmable, and an average over the whole employment is what makes it unfarmable.
  - **D7 — no result before ten samples.** A ten-day minimum, so a two-day hire cannot move
    diplomacy at all. Absent nodes on an old save read as zero samples, which means no result
    rather than a fabricated neutral one.
  - **D8 — ±3 goodwill, once, at the end.** Average at or above 0.75 gives +3; at or below 0.35
    gives -3; anything between gives nothing. Small enough that it complements employer reputation
    rather than replacing it, which is what the plan asks for.
  - **D9 — never on an outcome that is already priced.** Death, walkout, combat misuse,
    safe-passage denial and a skipped notice already move the origin faction's goodwill
    (`EmployerReputationService.cs:141-175`). F09 produces NOTHING for those; it is for ordinary
    completed and dismissed employments only. Two penalties for one act is the failure mode here.
  - **D10 — never positive to a hostile origin, and nothing at all for a war severance.** F08 has
    the same rule for the same reason.

## ALL SEVEN KNOWN DEFECTS ARE CLOSED, 2026-09-09

Every one is fixed, asserted and mutation-proven — `2d737e7`, `7e230ad`, `66af13b`, `ca5860f`,
`477ad58`, `1bdac06`, `b723d41`, `eb2d11c`, `35e1f2b`, `fe4c1ce`, `c67cd50`, `b4cc6c9`. Two of them
also turned out to be worse than the audit said: the wage defect was a live overcharge on shipped
1.0, and the partial-period branch it exposed had no assertion at all.

**WHAT THE RUN NEEDS FROM THE OPERATOR NOW: a decision on F22.** It is the only thing left in stage
7 and it is about nine units, with a game-over hazard in its first one. Stages 8 and 9 do not depend
on it and can run first.

## The seven defects, kept for the record

Found on 2026-09-09 by a one-off audit of this run's own commits, back when the method still had an
audit lane. The lane is gone; these stay, because they are defects in shipped code. Schema-58
persistence audited clean. I verified the two most serious against the code myself.

| # | Sev | Finding | Disposition |
|---|---|---|---|
| 1 | High | **F25 shows the applicant's ask but charges 35% more.** `TryHireApplicant` stores the raw `openMarketAsk` in `contract.dailyWage`, and Daily payroll then applies `WageStructureUtility.EffectiveDailyWage`'s 35% premium on top. **VERIFIED**: `WageStructure.cs:53-58` applies the premium; `EmploymentContract.cs:353` routes `PeriodPayment` through `PeriodCost`; the applicant row says the worker is paid the ask. A displayed figure and a charged figure must come from one calculation — this run's own standing rule. `0189a8a`'s assertion is hollow at that seam: it checks `dailyWage == openMarketAsk` and never advances payroll. | **FIX NOW — 7.9** |
| 2 | High | **F19's direct-input value never reaches the margin.** `ContractEstimate.Margin` subtracts `inputsIfBought`, the finished-good figure, and never `directInputsIfBought`. **VERIFIED** at `BusinessReportService.cs:69-71`. The UI shows both rows, so the new figure looks incorporated and is not. F19 exists to make profitability account for self-produced inputs, so this is its purpose unmet. **My under-specification** — I asked for a figure beside the old one and never said the margin should use it. | **FIX NOW — 7.10** |
| 3 | High | F24's emergency pool empty under real market data. | **ALREADY FIXED** at `4a78e0e`, after this review's fixed target. No action. |
| 4 | Med | **Pause after an uninstall designation can still uninstall the building.** `Pause` sets the flag but leaves an outstanding designation, and vanilla may finish it while the paused loop suppresses the replacement blueprint. F03 says an installed object stays installed while paused. | **SCHEDULE — 7.11** |
| 5 | Med | **F23 values a bond by def and stuff but matches returns by quality.** So a worker can leave with a masterwork and forfeit only a normal-quality bond. **Asymmetric in the player's favour and an exploit.** Mine: I specified `BaseValue` without quality. | **FIX NOW — 7.12** |
| 6 | Low | F24 dropped the old 1-20 day clamp on ORDINARY travel by delegating to `LogisticsQuote.TravelDaysFor`. Scope leak beyond the toggle. | **SCHEDULE — 7.13** |
| 7 | Low | F11's timing assertion bypasses `WorldComponentTick`, the game-facing caller. | **SCHEDULE — 7.14** |

Order: 7.9, then 7.12, then 7.10 — money first, then the exploit, then the unmet purpose. F22
continues after them.

## What remains in the run

The known defects above, then F22, then stage 8 (F08, F09) and stage 9 (F06) — each of those three
starting with recon, which means **Sol high read-only**, not Luna.

## Standing rules this run has paid for

  - **When a field stops being written, grep every reader.** A `> 0` check treats zero as corruption.
  - **When something stops happening immediately, find everything that assumed it was instant.**
  - **A number chosen without looking at the data the game generates is a guess.** F24's original
    two-day window was five times smaller than the nearest settlement in the world.
  - **The seam nobody asserts is the one between the caller and the service.**
  - **A skip is not evidence** — but a skip can BE the finding, as it was for F24.
  - **A mutation that fails to compile looks exactly like one that found nothing.** Check the anchor
    is unique and the replacement builds; read the run's log, not the summary line.
  - **When a mutation does not bite, suspect the mutation before the assertion.**
  - **A fixture that assumes a world shape is flaky, and it will fail on a world that is merely
    small.** 7.13's travel-ceiling case needed a tile more than 246 tiles away; the next generated
    world had none, and the suite went red for a reason that had nothing to do with the code. Where
    a world cannot exercise a bound, SKIP with the reason and the measurement — a red suite that
    means "small world" teaches everyone to ignore the colour.
  - **An oracle that reads state the test itself has already mutated is measuring the wrong world.**
    F24's U1 looked like an off-by-one in production; in fact the fixture's own earlier hire had
    called `Release()` on the shared candidate, nulling its pawn, so the oracle's `pawn != null`
    filter dropped it. Snapshot the ranking before the act, not after.
  - **Never let a zero mean unknown.** Say it in words.
  - **A charge the player was never shown is worse than the problem it fixes.**
  - Use `dev.ps1 bridge -Save`, not `run -Save`, to load a save.
  - Write files with the file tools; PowerShell `Get-Content` + `Out-File` double-encodes UTF-8.
  - The Bash tool's working directory persists between calls — `cd` back to `C:\dev\Intercolony`
    after visiting another repo, or `dev.ps1` will not be found.

## Open for the operator

Twenty-one play observations are owed, all in `docs/PENDING_PLAYTESTS.md`. The three that matter
most: the F15 save-compatibility check; the partial-delivery defect in `DeliverToColony`, which
costs the player silver and needs a design decision; and F20's equal wage split — someone who only
ever makes chairs still has half their wage charged to tables.


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
| F24 urgent dispatch | 7 | PART-BUILT and mutation-proven, `edb99ac` + `4a78e0e` + `a264c22`. Emergency dispatch is a MODE ON THE IMMEDIATE DIRECT-HIRE PATH, which is why it needs NO persisted state — the recon's "needs new state" is true only of a post-and-wait urgent request. The pool is the nearest `ceil(N x 0.5)` candidates, the wage carries a 4x premium, arrival is `ceil(ordinary / 3)` with a one-day floor, and all of it is disclosed before the player commits. Its first version used an absolute 2-day window and could never produce a candidate, real markets being 10-19 travel days away; the rule is now relative to the market. NOT built: drop-pod arrival, which F24 wants most but which should be gated on a settlement logistics capability F21 never built; any queued urgent request; equipment level in the request. Known defect 6 is outstanding against it — it dropped the old 1-20 day clamp on ORDINARY travel |
| F22 reverse listing and offer queue | 7 | RECONNOITRED, NOT STARTED, `d83a500`. About NINE units. First unit must be the custody proof: a bare custom world pawn is not recognised as borrowed by vanilla's game-over check, so the last colonist leaving could end the game. Six design questions unanswered by the source plan. AWAITING AN OPERATOR DECISION on whether to start it, take only the custody proof, or defer behind stages 8 and 9 |
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

### Next executable work, in dependency order — rewritten 2026-09-09

F23's bond half and F24's emergency dispatch are BUILT; both stay part-built with their unbuilt
parts named. What remains, in order:

1. **7.9 — F25's wage disclosure.** IN FLIGHT, Luna running; see the header. Known defect 1.
2. **7.12 — F23's bond ignores quality.** Known defect 5, an exploit.
3. **7.10 — F19's figure must reach the margin.** Known defect 2.
4. **7.11, 7.13, 7.14** — known defects 4, 6 and 7, scheduled rather than urgent.
5. **F22** — about nine units, AWAITING AN OPERATOR DECISION. See the F22 recon section: the first
   unit must be the custody proof because of the game-over hazard.
6. **Stage 8** — F08 and F09. Recon first, which now means Sol high read-only.
7. **Stage 9** — F06. Recon first; it has had none.

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
| 🔨 | 7 — Two-sided labor market | F25, F23, F24, F22 | F25, F23, F24 built; F22 reconnoitred only |
| ✅ | 8 — Commercial relationships | F08, F09 | closed 2026-09-09, both built, asserted and disclosed |
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
- **2026-09-09** — `EmploymentContract.dailyWage` stores the WORKER'S ASK. The charged rate is
  derived from it, never stored, and every payroll and display path goes through one owner. No
  `workerAsk` node, no second schema bump. Neither this nor the alternative can rescue both legacy
  cohorts — shipped 1.0 already mixes them with no discriminator — so the tie was broken on
  convention and on the fact that this option makes no existing contract worse. Do not re-litigate.
- **2026-09-07** — Procurement needs no F01 change. Recon established there is no per-cycle
  procurement success letter to suppress; the only procurement success letter is the terminal
  `Procurement agreement completed` notice (`ProcurementContractService.cs:1185`), which is a
  whole-agreement outcome and stays.

## Units — stage 7

| | Unit | Status |
|---|---|---|
| ✅ | 7.1–7.5c — F25, the buyer-side labour market | accepted, through `d8ad4ed` |
| ✅ | 7.6–7.6f — F23's equipment bond, both halves + assertions | accepted, `ed99423` `a173619` `91dc10d` `9841ec9` |
| ✅ | 7.7–7.7f — F24's emergency dispatch, its window fix, assertions, play entry | accepted, `4a78e0e` `a264c22` |
| ✅ | 7.8.0 — F22 recon (Sol high, read-only) | accepted, `d83a500` |
| ✅ | 7.9 — F25's wage: show what is charged, not only what is asked | accepted, `2d737e7`; assertions still owed |
| ✅ | 7.9b — recon: what `dailyWage` means (Sol high, read-only) | accepted; decision recorded |
| ✅ | 7.9b1 — one owner for the charged rate, every payroll path through it | accepted, `7e230ad` |
| ✅ | 7.9b3a — assertions for the partial period and the end settlement | accepted, `66af13b` |
| ✅ | 7.9b2 — the writer at `EmploymentService.cs:138` and the hire message | accepted, `ca5860f` |
| ✅ | 7.9b3b+c — displayed equals charged, with the literal oracle restored | accepted, `477ad58` |
| ✅ | 7.12 — F23's bond ignores quality when valuing | accepted, `1bdac06`; unasserted by design |
| ✅ | 7.12b — E1's oracle becomes quality-aware, fixture gets a masterwork | accepted, `b723d41` |
| ✅ | 7.10 + 7.10b — F19's figure reaches the margin, and W4 pins it | accepted, `eb2d11c` |
| ✅ | 7.11 — Pause must not let a committed uninstall finish | accepted, `35e1f2b`; unasserted |
| ✅ | 7.11b — its assertions, both directions | accepted, `fe4c1ce` |
| ✅ | 7.13 — F24 restored the 1-20 day ordinary travel clamp | accepted, `c67cd50` |
| ✅ | 7.14 — F11's timing assertion must go through `WorldComponentTick` | accepted, `b4cc6c9` |
| ✅ | 7.15 — the play entry for the seven-defect series | accepted, `1f4440c` |
| ⬜ | 7.8 — F22 implementation, ~9 units | AWAITING OPERATOR DECISION |

## Units — stage 8

| | Unit | Status |
|---|---|---|
| ✅ | 8.0 — recon: what relationship state exists, and how big F08+F09 really are | accepted; decisions D1-D5 recorded |
| ✅ | 8.1 — F08's decision logic, pure and uncalled | accepted, `91fc5e9` |
| ✅ | 8.2 — apply it on a quadrum cadence from the world tick | accepted, `ccd1bdf` |
| ✅ | 8.4 — F08's assertions, five of them, through the world tick | accepted, `d103024` |
| ✅ | 8.3 — disclose it in the Relations row | accepted, `08412cf`. **F08 IS COMPLETE** |
| ✅ | 8.5 + 8.5b — F09's sampler, and two assertion repairs | accepted, `10aa547` |
| ✅ | 8.6 — F09's end evaluation and its guards | accepted, `c07fae7` |
| ✅ | 8.8 — F09's five assertions | accepted, `ecf314a` |
| ✅ | 8.7 — F09's disclosure in the departure letter | accepted, `d1640f3`. **STAGE 8 COMPLETE** |
| ✅ | 8.9 — the stage-8 play entry | accepted, `400d0b3` |

## Units — stage 9

| | Unit | Status |
|---|---|---|
| ✅ | 9.0 — F06 recon: what vanilla already does about apparel policies | accepted; two claims spot-checked |
| ⛔ | 9.1+ — F06 implementation, ~12 units | **BLOCKED: needs a sixth and seventh Harmony patch** |

## Closed-stage unit history

The unit history for closed stages 1–6 lives in the git log on branch
`foreman/playtest-batch-2026-09-06`. Run `git log --oneline` on this branch to see it.
