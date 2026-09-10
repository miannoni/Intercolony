# Foreman state — Intercolony

Stage: C3 — settings for F08, F09 and F11. **STAGE D IS CLOSED; the correction plan has RESUMED.**
Unit: C3.3 — the Relations row stops stating the old fixed numbers as facts
Worker: luna running — `…\scratchpad\unit-c3-3.out`
Last done: C2.2 + C2.2b, F07's eight assertions, accepted at `14ad416` — produce 45/0/0, and two
mutations bite: removing the construction observer turns four red, removing the unwrap turns one.
Updated: 2026-09-09 19:05
Foreman load: 2026-09-09 15:48
Foreman: e46c835 · source C:\dev\agent-foreman · https://github.com/Vector-Consulting-IA-Operacional/agent-foreman.git
Fallback: if `Skill(foreman)` is unknown, read `C:\dev\agent-foreman\skill\SKILL.md` and follow it, then re-run its section 0.

<!-- Everything above this line is the header. A fresh session reads only the header. -->

## THE PLAN CHANGED — read this before dispatching anything

**`C:\dev\intercolony_playtest_correction_execution_plan.md` is now the authority** for the
behaviours it covers, and it beats `docs/PLAYTEST_BATCH_SOURCE_PLAN.md` wherever the two disagree.
The older plan still governs everything the correction plan does not mention. A copy lives at
`docs/PLAYTEST_CORRECTION_PLAN.md` so workers, which have no chat history, can cite it.

**It also answers the two questions the previous run halted on, and the answers are both "no":**
F22 is FROZEN and F06 is deferred. Neither is a pending decision any more, and **this run must not
stop to ask about either**.

### IN SCOPE — only these eight findings

F01, F07, F08, F09, F10, F11, F13, F17.

### FROZEN — documented, never advanced

**F12** and **F22**. Existing work stays; no implementation unit may follow their documentation
freeze. F22's recon stays as historical technical information and is not a plan.

### DEFERRED — do not touch, do not recon, do not "finish"

F04, F06, F16, F19, F20, F21, F23, F24. Newer product direction is coming for each. If an in-scope
unit turns out to depend on one, **stop at the boundary, record why, and do the independent work**.

### REGRESSION-ONLY — closed, touch only if an in-scope change breaks them

F02, F03, F05, F14, F15, F18, F25. A break there is a regression to fix narrowly, not a reopening.

### Standing constraints, unchanged

Stay on `foreman/playtest-batch-2026-09-06`. **Never merge to `main`, never release.** Push
regularly. Avoid a schema bump unless persisted world state genuinely needs one — ordinary mod
settings never do. Prefer existing simulation events and mod-owned seams over polling, and the
existing settings infrastructure over a second configuration mechanism.

### Serialisation the plan requires

F08, F09 and F11's settings are ONE coherent slice or strictly serialised — they share the settings
surface. F13 and F17 both touch the employee card and are serialised or owned by one worker. Never
two workers on the same large UI or settings file.


## Stages — the correction run

| | Stage | Scope | Status |
|---|---|---|---|
| ✅ | C0 — scope lock and regression baseline | — | closed, `f049bfb` |
| ✅ | C1 — F01: a routine contract cycle must be silent | F01 | closed, `1c5cc56` |
| ✅ | C2 — F07: the production rate must count real completions | F07 | closed, `14ad416` |
| 🔨 | C3 — settings for goodwill pressure, employment experience and RFQ pacing | F08, F09, F11 | resumed at C3.3 |
| ✅ | D — runtime defect triage, out of plan order, by operator instruction | — | closed, `28acfd1` |
| ⬜ | C4 — F10: progression gates standing agreements, not Find Seller | F10 | not started |
| ⬜ | C5 — F13 and F17: the employee card's interaction surface | F13, F17 | not started |
| ⬜ | C6 — freeze F12 in the documentation | — | not started |
| ⬜ | C7 — freeze F22 in the documentation | — | not started |
| ⬜ | C8 — whole-suite regression and clean halt | — | not started |

## Units — stages C0 and C1

| | Unit | Status |
|---|---|---|
| ✅ | C0.1 — copy the plan into `docs/`, record the scope lock in `PROGRESS.md` | accepted, `f049bfb` |
| ✅ | C1.0 — recon: the `Contract delivery due` emission and what it knows | accepted; decisions below |
| ✅ | C1.1 — the due letter becomes a log, the warning moves after auto-ready | accepted, `9b8e05e` |
| ✅ | C1.2 + C1.2b — C1's seven assertions and the fixture repair | accepted, `1c5cc56`. **C1 COMPLETE** |

## C2 — the recon, and what I decided from it

Sol recon, read-only. Three load-bearing claims spot-checked in the source myself.

**THE PLAN IS WRONG ABOUT THE DENOMINATOR, and it matters that nobody "fixes" it.**
`ProductionLedger.cs:103` already divides by a constant `WindowDays`, and
`HasRecordedProduction` turns true on the first positive bucket (`:128`). One chair today already
computes 1/5 = 0.2/day. **The only real defect is capture: no chair bucket ever reaches that
arithmetic.** Recorded here so a later unit does not go looking for a bug that is not there.

**A SIXTH HARMONY PATCH IS NEEDED, and this plan authorises it** where the F06 question could not
be answered: C2 says to prefer an existing vanilla event, then an Intercolony seam, and only then a
narrow observation-only patch *with the justification written down*. The justification is:
`Frame.CompleteConstruction(Pawn)` (`reference/decompiled/RimWorld/Frame.cs:262`) is the
authoritative moment a frame becomes the finished Thing, it is `void`, the Thing is a local
variable, and nothing in Intercolony is called by ordinary vanilla construction at all.

**DECIDED:**

  - **C2-D1 — one observer covers construction AND Produce.** Produce places an ordinary vanilla
    build blueprint (`ProduceLoopMapComponent.cs:119`), so its completions go through the same
    frame. **Do NOT also record at Produce's `finishedBuilding` branch** (`:82`) — that branch
    cannot tell a newly finished object from a seed object the player enabled Produce on, and
    recording in both places would count a Produce chair twice.
  - **C2-D2 — the bill observer records the wrong def for minifiable goods.** `GenRecipe` wraps a
    minifiable product in a `MinifiedThing` before returning it
    (`reference/decompiled/Verse/GenRecipe.cs:121`), and the postfix records `product.def`, so a
    crafted minifiable good is recorded under the wrapper. Normalise through the inner Thing.
  - **C2-D3 — minification and reinstallation must stay invisible.** They do not call
    `CompleteConstruction`, so a chair built once counts once and selling it counts nothing. That
    is the property the assertions must pin.
  - **C2-D4 — the extractive paths are OUT OF SCOPE and recorded, not silently skipped.** Recon
    found that harvesting, mining, wool and milk, eggs and fishing all create contractable goods
    without any bill and without any Intercolony seam. C2's own list — bills, constructed
    furniture, Produce loops — is what this stage covers. Whether *harvesting* is "production" is a
    product decision, and the honest estimate for that answer is another 12-18 production units.
    It goes to the operator rather than into this stage.

## Units — stage C2

| | Unit | Status |
|---|---|---|
| ✅ | C2.0 — recon: the missing completion paths, and the denominator question | accepted; D1-D4 recorded |
| ✅ | C2.1 — normalise minified bill products, add the construction observer | accepted, `8cb5774` |
| ✅ | C2.2 + C2.2b — C2's eight assertions and the wrapper re-cut | accepted, `14ad416`. **C2 COMPLETE** |

## STAGE D — TWO RUNTIME DEFECTS FROM REAL PLAY, 2026-09-09

The operator hit both repeatedly in a real game and asked for triage before any more feature work.
**Feature dispatch is paused. It resumes at C3.3.** This does not authorise anything the correction
plan freezes or defers.

**DEFECT A — `Object with load ID Lord_140 is referenced (xml node name: lord) but is not
deep-saved.`** Potentially save-corrupting, treated as P0 until disproven.

**DEFECT B — a repeated `NullReferenceException` through `JoyGiver_VisitGrave`.** Note it is
`JoyGiver`, not `JobGiver` as reported; it is reached through `JobGiver_IdleJoy` →
`JobGiver_GetJoy`.

### Evidence captured before it was lost

The live `Player.log` had already been rotated away by my own test runs. **The operator's play log is
preserved at `…\scratchpad\playtest-evidence\Player-prev-CAPTURED.log`** and the play save is
`Saves\Playtest 1.0.rws` (19:18) — the `Autosave-*.rws` files are from test runs, not play.

**THIRTEEN OTHER MODS WERE ACTIVE**, and two of them matter: **Orion.Hospitality**, which owns its
own Lords for guests, and **avilmask.CommonSense**, which prefixes the exact job giver in defect B's
stack. Nothing here may be blamed on Intercolony merely because it happened in an Intercolony game.

### THE DISPOSITIONS, 2026-09-09. They differ, and both are evidenced.

**DEFECT B IS OURS, AND IT IS A REAL DEFECT IN SHIPPED CODE.** `EmploymentService.cs:1093-1102`
discards a worker with the guard `!worker.Spawned && Find.WorldPawns.Contains(worker)`, under a
comment that says "a worker dismissed before arrival". **A DEAD employee satisfies that predicate**
— a dead pawn is despawned, and `WorldPawns.Contains` includes the dead collection
(`reference/decompiled/RimWorld.Planet/WorldPawns.cs:191`, `:388`). The comment states an intent
the code never implemented.

The chain, every step cited by recon and the last two verified by me: vanilla holds the dead pawn
inside the corpse BY REFERENCE (`Corpse.cs:163-167`); our sweep discards it; the save writes the
discarded reference as null (`Scribe_References.cs:60-75`); the load drops the null entry
(`ThingOwner.cs:53-63`); the grave is left holding **a corpse with an empty container**; and
vanilla `JoyGiver_VisitGrave` then throws on `Corpse.InnerPawn.Faction` for every colonist looking
for joy.

**The operator's save contains the end state**: grave `Grave508055`, corpse `Corpse_Human849086`,
`<innerList />`. The pawn was the employee **Sinni**, contract 110039, "Sinni died before the term
ended".

**DEFECT A IS NOT OURS, and it must not be patched defensively.** The captured log names the
holder outright: `curParent=Moth` (`Player-prev-CAPTURED.log:3023`). Moth is a `Town_Trader` in
Faction_14, a departed trade caravan pawn with no Intercolony contract, quest or record. The
reference lives in **Hospitality's `CompGuest.lord`** — its own serialized field, which vanilla
cannot clear when it removes a Lord because vanilla only knows about `Pawn.lord`. Intercolony
creates exit Lords through `QuestPart_Leave` and one directly in safe passage, but it never stores
or persists a Lord reference.

Worth knowing: the save has since been rewritten by continued play and Moth's node now reads
`<lord>null</lord>` — the stale reference resolved to null on load and was saved back as null. The
warning is noisy rather than progressive, and the original bytes that reproduce it are gone.

**They are independent.** Different pawns, factions, objects and causes; the only thing they share
is the shape of a saved reference outliving its target.

### What I established myself, before the recon returned

**Defect B's null is identified.** `JoyGiver_VisitGrave`'s validator evaluates
`building_Grave.Corpse.InnerPawn.Faction`, and `Corpse.InnerPawn` **returns null when its inner
container is empty** (`reference/decompiled/Verse/Corpse.cs:29-38`). So the broken state is a grave
holding a corpse that has lost its pawn without being destroyed.

**Defect A's holder is almost certainly a Job.** Only eight vanilla types scribe a field named
`lord`, and the one that travels with a pawn is **`Verse.AI/Job.cs:503`**. A job outlives the lord
it references.

**And the mechanism is in plain sight:** `Pawn.SetFaction` calls
`GetLord()?.Notify_PawnLost(this, ChangedFaction)` (`reference/decompiled/Verse/Pawn.cs:2714`) and
**does not clear the pawn's job**. Any mod changing a pawn's faction while it holds a lord-linked
job leaves a dangling reference. Intercolony changes faction at arrival
(`EmploymentService.cs:892`), and `HostilityPolicy.cs:180` already has a comment showing the mod
knows about this interaction.

**The screenshots put an Intercolony arrival 18 seconds before the warning** — 12:40:06 "Breixo of
Coalition of Braga has arrived", 12:40:24 the Lord_140 warning. Suggestive, not proof: the warning
is emitted when SAVING, so that is an autosave firing near an arrival. **Ownership is not
established until the save says which pawn holds the reference.**

## C3 — the recon, and what I decided from it

Sol recon, read-only. The settings idiom, the nine constants and F11's scheduler are all mapped.

**A DEFECT THE RECON FOUND, and it would have shipped: making the delta configurable BREAKS F08's
safety property.** `CommercialGoodwillPressureService` only asks whether base goodwill is already at
the ceiling and then applies the whole delta (`:203`, `:220`). At today's fixed `+1` that can never
overshoot. At a configurable delta of 5 with a ceiling of 74, a faction at 73 lands on 78 — past
vanilla's ally threshold of 75 (`reference/decompiled/RimWorld/DiplomacyTuning.cs:25`). The entire
point of the ceiling is that commerce cannot buy an alliance.

**DECIDED:**

  - **C3-D1 — one owner for the settings surface, and it goes first.** All nine settings —
    declaration, `Scribe` keys, ranges, validation, sections, labels, tooltips — land in ONE unit
    before any consumer is touched. The plan demands it and the recon confirms
    `IntercolonySettings.cs` and `IntercolonyMod.cs` cannot take two workers.
  - **C3-D2 — the application clamps to remaining headroom**, not merely "is it below the
    ceiling". And the ceiling setting itself clamps to at most 74. Two guards, because either alone
    still lets a large delta jump the threshold.
  - **C3-D3 — F09's thresholds are validated in both places.** `ExposeData` clamps on load and
    save, but the UI writes live, so a consumer can see an invalid pair mid-drag. The invariant
    holds at assignment too.
  - **C3-D4 — attractiveness means price relative to its siblings.** Every quote for a request is
    still in `request.quotes` while arrivals are scheduled (`RfqService.cs:242`), already sorted by
    quantity then total price, so a bounded bias against the cheaper offers is available without
    inventing a score. Bounded, and combined with the existing independent jitter, so the best
    price is a tendency and never deterministically last — the plan is explicit about that.
  - **C3-D5 — the five-day cap is on ARRIVAL, and the request keeps its six-day life.** Expiry is
    inclusive (`PurchaseRequest.cs:225`) and the reveal path rejects an expired request before
    checking whether a reply is due, so a request that expired at day 5 would discard the very
    reply the cap scheduled.

## Units — stage C3

| | Unit | Status |
|---|---|---|
| ✅ | C3.0 — recon: the settings surface, the nine constants, F11's scheduling | accepted; D1-D5 recorded |
| ✅ | C3.1 — the settings surface, one owner, no consumer touched | accepted, `2439f4a` |
| ✅ | C3.2 — F08 reads the settings, and clamps to remaining headroom | accepted, `3b742f9` |
| 🔨 | C3.3 — F08's Relations row stops hard-coding Preferred, quadrum, 60 | Luna running |
| ⬜ | C3.4 — F09 reads the settings at resolution | not started |
| ⬜ | C3.5 — F11's front-loaded scheduler, the cap, and the lifetime | not started |
| ⬜ | C3.6 → C3.8 — assertions, one unit per host suite | not started |

## Units — stage D

| | Unit | Status |
|---|---|---|
| ✅ | D.0 — recon on both defects, against the captured log and the play save | accepted; both claims spot-checked |
| ✅ | D.1 — the discard guard must mean what its comment says | accepted, `68ad1ea` |
| ✅ | D.2 — the regression assertion, through a real corpse and a save | accepted, `1bf7d30`; mutation turns three red |
| ✅ | D.3 — the durable dispositions and the play entry | accepted, `28acfd1`. **STAGE D COMPLETE** |

## C1 — the recon, and what I decided from it

Sol recon, read-only. Both load-bearing claims spot-checked in the source myself.

**ONE emission site, and it fires before anything is known.** `ContractService.RaiseCycleOrder`
sends `Contract delivery due` unconditionally at `ContractService.cs:1702`, immediately after
creating the order — *before* `AdvanceAutoReady` has asked whether the cycle can proceed
(`ContractService.cs:1272`). That ordering is the whole defect: the letter cannot know what it is
announcing.

**The exception path already mostly exists.** `AdvanceAutoReady` sends
`Agreement delivery needs attention` naming the settlement, the order, the quantity, the reason and
where to act, throttled by `order.autoReadyFailureNotified` (`ContractService.cs:1297-1320`).

**DECIDED — three cases, and the plan's silent path only covers one of them:**

  - **C1-D1 — auto-ready on and the cycle readies: SILENT.** No letter at all. This is the routine
    success the plan wants quiet.
  - **C1-D2 — auto-ready on and the cycle cannot ready: the existing warning, unchanged.** It
    already answers which order, what is blocked and what to do.
  - **C1-D3 — the player must act: ONE actionable letter, after auto-ready has run, not before.**
    That covers auto-ready being off AND seller delivery, which *can never auto-ready* because
    `SalesOrder.CanMarkReady` requires buyer pickup (`SalesOrder.cs:204`). Deleting the due letter
    without this would leave both cases with no notice at all — a silent regression dressed as a
    fix.

**C1-D4 — no cross-reload deduplication.** The plan asks that repeated *ticks* not spam, and the
existing transient marker does that. Making it survive a reload would mean persisted state for a
letter, and the plan says avoid a bump unless world state genuinely needs one. Accepted and
recorded rather than discovered later: the same unresolved problem can warn once more after a load.

**C1-D5 — no feasibility model for seller delivery.** Recon is right that nothing can answer
"can this delivery proceed" for seller delivery without caravan dispatch, and **that is frozen
F12**. The boundary is respected by giving seller-delivery cycles the actionable letter instead of
a prediction.


## The baseline, established before any edit

Branch `foreman/playtest-batch-2026-09-06` at `56180ea`, pushed, working tree clean apart from an
untracked `Playtesting annotations.docx` that is not ours. The milestone record for stages 1-8 is in
`PROGRESS.md`. The whole suite on a fresh world: **1536 passed, 0 failed, 17 skipped, exit 0** —
run at `56180ea`'s tree, which no edit has touched since, so it stands as this run's baseline.

## What the correction plan changes about work already done

Three of the eight in-scope findings were closed in the previous run and are being REOPENED by
newer product direction, not by defect:

  - **F01** shipped scoped to the sales *readying* letter only. The correction plan says the
    `Contract delivery due` letter on a recurring supply cycle must also be silent when the cycle
    can be fulfilled normally, and actionable only when it cannot.
  - **F07** shipped comparing commitments against a ledger fed by ONE observer, vanilla's
    bill-completion seam. The plan says that observer misses constructed furniture, and that the
    rolling five-day rate must show `0.2/day` after a single completion rather than waiting for the
    window to fill.
  - **F08, F09 and F11** shipped with fixed constants. The plan wants them player-configurable
    through the existing settings surface, defaults reproducing today's behaviour.
  - **F10** shipped as an earned gate on procurement. The plan says the gate leaked onto spot
    procurement: Find Seller must work with a supplier you have never bought from, and only the
    STANDING agreement stays earned.
  - **F13** shipped as an auto-renew row on the employee card. The plan wants the state readable at
    a glance and directly toggleable, like the existing Auto-ready control.
  - **F17** shipped an employee card that still carries occasional actions inline. They move to the
    `...` menu, with no lifecycle change.

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
  - **A fixture that fails for the wrong reason passes for the wrong reason too.** C1.2's
    missing-goods case produced exactly one correctly-labelled warning and still failed, because
    `CanMarkReadyNow` has six different refusal branches and the fixture was tripping one of the
    others. The letter count was right, the letter was wrong. **Assert on the branch you meant to
    exercise, and print the reason in the failure detail** — labels alone cost a whole extra run to
    diagnose.
  - **A green suite whose COUNT dropped is not a green suite — find out why before accepting.**
    The transition suite went 21 to 20 with no failure and no skip while a defect fix was in the
    tree. It turned out to be world variance: one assertion there is gated on the best negotiator
    having any Social at all, and that world rolled a zero. Checked rather than assumed, because a
    silently unexecuted assertion looks exactly like a passing one.
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

- **2026-09-09, from the C1 recon and deliberately not acted on.** A recurring contract whose
  counterparty becomes inaccessible is cancelled silently: status and a `ContractCancelled` timeline
  record, no letter (`ContractService.cs:1655`). C1's philosophy suggests that deserves the player's
  attention, but the contract is already terminal and there is nothing for them to do about it, so a
  letter would be noise of a different kind. Left alone; say if you want it announced.


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


## Closed history — the first run, stages 1 to 9

Its unit tables and stage tables were replaced when the correction plan arrived. Every unit
is in the git log on this branch, and `PROGRESS.md` carries the milestone record. Nothing
from that run is pending: it halted cleanly, and the two questions it halted on have been
answered by the correction plan as FROZEN and DEFERRED.
