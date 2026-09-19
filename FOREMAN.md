# Foreman state — Intercolony

Stage: **P4 — F23: varied, believable equipment packages instead of tier uniforms**
Unit: P4.9 — P4 stage gate: whole suite
Worker: idle/none — Foreman running `dev.ps1 test all -Fresh` itself · log `C:\Users\matte\.claude\jobs\439462af\tmp\p4-gate.log` · bg id `btufhcdw2`
Loop: WAITING_ON_WORKER
Last done: **P4.8 COMPLETE — both of the plan's mutation clauses discharged.** Mutations reverted, both `MeetsOrExceeds` gates restored, tree clean.
Updated: 2026-09-19 18:50

## P4.8 mutation proofs — complete

| Plan clause | Mutation applied | Result |
|---|---|---|
| force deterministic first-passing selection must redden diversity evidence | `RandomizeCandidateOrder(plans, seed)` → `plans` | **115/4/0** — Professional and Elite each collapse to **1 signature, 32/32**; D3 to 2 |
| bypassing the final `Classify` must redden tier-validity evidence | both `MeetsOrExceeds` guards → `if (false)` | **116/3/0** — D4 red with **74 of 80 accepted loadouts under-tier**; Elite 32/32, civilian 8/8 on both paths |

The first mutation reproduces *exactly* the state the stage began in — one identical package for
everyone — which is the strongest possible confirmation that the diversity assertions measure the
real mechanism and not something incidental.

Both `Classify` gates were mutated rather than only the combat one, so the civilian apparel path
could not quietly hold D4 green.

**Watch at verification:** the planner is past 2,000 lines after four selection redesigns layered on
each other. R8 told the worker to remove whatever the seeded-order walk supersedes rather than leave
two rival mechanisms live. Confirm it actually deleted the superseded path — a file carrying both a
weighted sampler and an order-randomised search, with neither owning the outcome, is how the next
person inherits a mystery.

## Three approaches, one verdict

| Approach | Elite distinct signatures, 32 applicants |
|---|---|
| weight floor | 1 |
| escalation ladder | 1 |
| package-wide quality strata | **1** |

Professional went 2 → 4 under strata, and D1 met its count bar while still failing concentration at
29/32. **Elite has never moved.** Every Elite applicant still falls through to the deterministic
final attempt, which gives everyone the same package by construction.

**So the approach is wrong, not mistuned** — which is what I wrote the standard for before running
it, precisely so the decision would not be made under the temptation of a near-miss.

### The re-cut, and why it should work where sampling could not

An Elite **package** needs 0.78; an Elite **item** band is 0.46. Independent draws almost never
compose a jointly valid whole-package combination, and recon established that is what the
deterministic search actually finds — "not simply item count, item band, or one magic slot".

**So stop trying to make random draws clear 0.78. Keep the proven candidate space and the
authoritative accept test, and randomise the ORDER it is walked, seeded per applicant.** Different
applicants then walk the passing combinations in different orders and settle on *different valid*
packages. Diversity comes from which valid package is found first — which is exactly what F23 wants:
varied packages that still meet the promise.

This is recon's own recommended fallback, offered as the reliable alternative when the quality floor
could not be measured. It should have been taken one attempt earlier.

**Accepted cost, flagged for P5:** walking and classifying more candidates means more `Thing`
creation. P0 measured this path at 500 ms of a 530 ms posting, and P5 owns it. The worker is asked
to report the candidate count per applicant as P5's input.

**Explicitly forbidden:** loosening the diversity thresholds so the numbers agree. If this re-cut
also fails, F23 gets recorded as not achieved rather than declared done on softened evidence.

## The root cause — two different scales, conflated

| Scale | Professional | Elite |
|---|---|---|
| **item band** (`LaborEquipmentItemScore.cs:49-50`) | 0.36 | 0.46 |
| **package threshold** (`LaborEquipmentTierService.cs:14-15`) | 0.52 | **0.78** |

All four numbers verified by Foreman. **An "Elite-band" item implies nothing close to an Elite
package.** The planner was built to pick Elite-band *items* and then wondered why Elite *packages*
did not appear.

**Why the deterministic search works:** it samples a SHARED QUALITY STRATUM — loops quality upward,
filters the weapon *and every apparel piece* to that same quality (`BuildQualityChoices`,
`LaborEquipmentPackagePlanner.cs:856`), builds conflict-aware sets, and tries combinations until the
real `Classify` passes. Weighted sampling picks each item's quality independently, so packages mix
qualities and never cohere.

**And the counter-intuitive part that explains two failed fixes:** once coverage saturates, apparel
is an **average**, so adding weaker filler *lowers* the package score. Escalating piece count made
things worse, not better. The civilian case is hardest because apparel must carry the whole 0.78
with no weapon term to offset it.

**Decision: sample one package-wide quality stratum per attempt**, escalate the stratum upward
across attempts, and **do not hardcode an Elite quality floor** — the recon marks the exact floor
*não determinado* and `Classify` is the authority on what passes. Diversity moves to definitions,
materials and set composition within the stratum; only mixed-quality packages are given up.

**Recon honesty preserved:** it corrected me that sampling is not structurally incapable of Elite,
merely extremely improbable, and noted the fixture does not prove which attempt accepted a package.
Exact score gaps are *não determinado* without runtime instrumentation, which the read-only task
forbade.

**`FOREMAN.md` is getting long and §9 says keep it small.** Once P4 closes, prune it back to the
current run: header, stage table, current-stage units, live decisions, and the operator items.
The closed-stage narrative for P0–P3 can go — those stages are recorded in their commits.

## The most important finding of this stage

```text
D1  32 Professional applicants ->  2 distinct signatures, largest group 31/32
D2  32 Elite applicants        ->  1 distinct signature,  largest group 32/32
D3  64 high-tier applicants    ->  3 distinct weapon/apparel signatures
D6  Elite packages with Professional-band filler -> 0
```

**Elite applicants are identical to each other.** For Elite, the tier-uniform problem F23 exists to
fix is exactly as bad as before the stage started.

**Not the known ID-collision limitation** — the fixture reports `prospect identity collisions=0`,
so genuinely distinct prospects produce identical gear.

**The cause is my own decision in `1dbfd21`, accepted in writing at the time.** The final attempt
reproduces the old deterministic search, so everyone reaching it gets the same package. I accepted
that on the assumption most applicants would succeed on attempts 1–3. For Elite essentially none
do — so the fallback IS the normal path and diversity is zero.

**The whole suite was green at 1750/0/19 with the feature dead.** That is exactly what "green is not
evidence" means, and the only reason it surfaced is that the diversity assertions were written and
run before the commit rather than after.

### Why this is recon and not a third knob

Two rounds of tuning — weight floors, then the escalation ladder — failed to make sampled Elite
packages classify Elite. **I still do not know why a sampled package falls short**, and turning a
third knob would be guessing. I said last time that if it failed again it goes to Sol, and it has.

The recon must produce numbers: the actual Elite/Professional thresholds and score formula, what a
sampled package really scores versus the old search's package, where the difference comes from, and
the smallest selection change that closes it. The civilian apparel-only case is called out
separately because with no weapon the apparel carries the entire score.

**The apparel-cap hypothesis was not needed.** The real search fixed it, and the 5-vs-12 question
never had to be answered — worth recording because I was one step from changing the cap on a
plausible theory instead of running the test first.

### What is proven, and what is not

The suite proves packages are valid, wearable, and meet their promise. **It does not prove they are
varied**, which is the entire point of F23. A green suite here is consistent with the allocator
having produced five hundred identical Elite loadouts.

**The trap this unit must avoid:** `TryFulfil` has a compatibility overload that synthesises a
prospect and seeds from `pawn.thingIDNumber`. Diversity assertions written against it would measure
the adapter rather than the feature and would pass regardless. The prompt requires the PRODUCTION
overload with genuinely distinct `LaborProspect` values — and distinctness matters doubly because
identical prospects from one settlement still collide by design.

## Why this is a design change, not another knob

Two attempts have now been spent tuning weights and escalation against the same reachability
problem. **Turning a third knob would be the blind retry WORKER.md warns about.**

The decisive fact was already in hand and had not been used: **the OLD allocator passed these exact
assertions.** It did not sample — it walked weapon × apparel combinations, conflict-aware, until one
passed. An Elite package often needs a specific strong *combination*, and weighted sampling rarely
lands on it however hard it is biased. So the gap is structural, not a tuning error.

**Decision: the final attempt reproduces the old search.** Attempts 1 to N−1 keep the seeded
weighted sampling untouched — that is where diversity comes from — and the last attempt is the
proven deterministic conflict-aware search.

**The consequence is accepted on purpose:** an applicant who reaches the final attempt gets a
package that looks like the old deterministic one. That is correct. A promise the market makes must
be keepable, and diversity is delivered by the earlier attempts succeeding in the common case. The
floor becomes "no worse at fulfilment than before this stage", which is the right floor for a change
that was only ever meant to add variety.

**Explicitly not done:** porting "take the top-scoring item per slot" instead of the real search.
Naive per-slot bests can conflict with each other and yield a weaker set than the combination walk
found — that shortcut would look like the old behaviour and quietly not be it.

**If this fails too, stop tuning and route to Sol recon** to establish what `Classify` actually
requires for Elite and whether the pools can produce it at all.

### What the audit found

Enforced and accounted for: item category and `generateAllowChance`, apparel/weapon role tests,
item tech ceiling with the Undefined→Industrial fallback, stuff eligibility via
`GenStuff.AllowedStuffsFor`, quality rules, per-item wearability and body parts, duplicate-apparel
rejection, the apparel cap, mutual set compatibility, civilian weapon exclusion, item creation and
equip verification, the final authoritative `Classify`, and failed-package cleanup.

**Deliberately replaced, not lost:** the greedy forward/reverse/anchor search and the sorted
first-success ordering. Those two *were* the tier-uniform bug, and weighted seeded sampling is their
intended replacement.

**Newly stricter than the old path:** no duplicate apparel definitions, a five-piece cap against the
old twelve, and explicit package-composition gates.

### The worker corrected my prompt for the second time in this stage

`ApparelUtility.CanWearTogetherAsSet` **does not exist in RimWorld.** It was a local helper in the
old allocator, and the real vanilla API is `ApparelUtility.CanWearTogether(ThingDef, ThingDef,
BodyDef)` — confirmed by Foreman at `reference/decompiled/RimWorld/ApparelUtility.cs:89` and at the
call site `LaborEquipmentPackagePlanner.cs:847`. I had repeated the helper's name as though it were
vanilla. Earlier in this stage it also corrected "body type" to `RaceProps.body`/`BodyDef`.
**Both corrections came from the instruction to verify every API against `reference/decompiled`
rather than trusting the prompt** — which is why that instruction is in every dispatch.

### Boundaries held

Catalogue: zero matches for `Pawn` or `BodyDef`. Planner: no `Pawn` parameter or field — its only
three mentions are comments. `BodyDef` is captured once in the allocator and passed as plain data.

## Stopping the whack-a-mole, deliberately

Replacing the selection walk dropped the constraints the old candidate builder enforced, and they
are being rediscovered one suite run at a time:

| Run | Failure | Constraint that was missing |
|---|---|---|
| 1 | `could not wear generated apparel Apparel_KidShirt` | per-item wearability — fixed |
| 2 | `classified as Professional, below Elite` | tier reachability — fixed |
| 3 | `selected apparel set conflicted for this pawn's body` | **set compatibility — not fixed** |

Three runs, three constraints, each found only by breaking. **P4.3e is framed to end that pattern:**
go back to the original allocator before `b12649a`, read its candidate builder in full, and
enumerate EVERY constraint it enforced with a verdict per constraint — enforced, deliberately
dropped, or newly added. **That list is the deliverable; the code fix follows from it.**

This is the honest reading of WORKER.md's "after two failed attempts, do not blindly retry". These
were three different real defects rather than three attempts at one, and each narrowed the gap —
but continuing one-at-a-time would be exactly the blind retry that rule warns about.

**Design constraint: pass the `BodyDef`, not the `Pawn`.** A BodyDef is plain data, so the planner
can run the real `ApparelUtility.CanWearTogetherAsSet` check while still never receiving a Pawn, and
the catalogue stays free of both.

**The escalation ladder, for the record:** attempt 1 keeps the original weighted draw and apparel
count distribution; attempt 2 adds band boost 0.25 with score exponent 2; attempt 3 boost 0.75,
exponent 4; attempt 4 boost 2.00, exponent 6. Weaponless packages get additional band and quality
bias and target 3–5, then 4–5, then all five apparel slots — because with no weapon the apparel has
to carry the whole package score alone.

`Classify`, the tier thresholds, the scorer and the catalogue were all left alone, which was the
point: the gear has to genuinely qualify.

**If this run is green, P4.3b through P4.3d commit together as one coherent slice.** They are three
halves of one change — wire the planner in, restore the filter the old walk carried, and make the
promise reachable — and none of them is meaningful alone.

## P4.3d — the design working too literally

```text
FAIL  allocator fulfilment leaves actual gear at or above the promised tier
      (promised=Elite; failure=Generated combat package classified as Professional, below Elite.)
FAIL  civilian fulfilment leaves the primary weapon empty
      (failure=Generated apparel classified as Standard, below Elite.)
FAIL  an elite prospect is considered by a professional posting  (queued=False)
```

Packages are now wearable but miss the promised tier, and the third failure is downstream of the
first two. This is not a bug in the fix — it is weighted sampling doing exactly what it was asked
to do. It favours plausible-but-not-optimal items and varies completeness, so an Elite draw often
lands below the Elite threshold, and four *independent* attempts all miss. The civilian case is
worst because a civilian carries no weapon, so apparel alone must carry the entire package score.

**Foreman's decision: retry ESCALATION, not more retries and not a lower bar.** Attempt 1 stays the
most varied draw — that is where diversity comes from — and each later attempt biases progressively
toward higher-band items and fuller packages, with the last strongly biased. Escalation is a safety
net for the tail, not the normal route; most applicants should still succeed on attempt 1 or 2 and
therefore still differ from one another.

**Explicitly rejected:** making attempt 1 greedy (that is the tier-uniform bug returning by another
door), raising the attempt count as the primary fix, and touching `Classify` or the tier thresholds.
The gear must genuinely qualify — never lower the bar until weak gear passes.

**The restored checks are the real vanilla ones, not a reinvention:**
`def.apparel.PawnCanWear(pawn, ignoreGender: true)` and `ApparelUtility.HasPartsToWear(pawn, def)`,
both cited to `reference/decompiled` and both what the old allocator used.

**The worker corrected the prompt, and it was right to:** I wrote "body type", but the old allocator
used `RaceProps.body`/`BodyDef`, not the graphical `pawn.story.bodyType`. Worth keeping — those are
different things and confusing them would have filtered the wrong way.

## P4.3b regression — a real defect of mine, not a stale test

```text
FAIL  allocator fulfilment leaves actual gear at or above the promised tier
      (fulfilled=False, actual=None, promised=Elite;
       failure=Pawn could not wear generated apparel Apparel_KidShirt.)
FAIL  civilian fulfilment leaves the primary weapon empty
      (failure=Pawn could not wear generated apparel Apparel_KidTribal.)
FAIL  an elite prospect is considered by a professional posting  (queued=False)
```

**The planner is choosing CHILD apparel for adult applicants.** All four attempts fail, the
applicant is discarded, and the third failure is a downstream consequence of the first two.

**This is precisely the seam flagged in P4.2 and then not closed.** The catalogue is a pure
function of loaded defs and deliberately omits pawn-dependent wearability — correct for
cacheability — and the note said that filter belonged "later". The old allocator's candidate
builder took the `Pawn` and filtered against it; that filter was deleted with the selection walk
and never replaced.

**P4.3b is NOT committed.** Committing a broken allocator would leave HEAD with Elite fulfilment
dead. The working tree carries P4.3b plus P4.3c's fix, and both commit together once green.

**Design constraint for the fix:** the catalogue must stay pure and the planner must stay
`Pawn`-free, so the allocator computes the wearability filter once per applicant and passes it in
as a predicate. Fixing this by making either of them take a `Pawn` would undo the reason they were
split in the first place.

This is also the vindication of running the whole suite on the first behaviour-changing unit of a
stage rather than trusting a clean build.

**Scope flag RESOLVED.** `JobPostingService.cs` changed by exactly 4 lines: passing `worker` and
`posting.id` into `TryFulfil`. No posting logic, no matching, no threshold. Exactly the forced
call-site change predicted, and nothing riding along.

**The allocator is 140 insertions against 786 deletions** — the old first-passing walk is gone
rather than left dead beside the new path, which is what the plan asked for.

### The compatibility overload is the safe kind, but it matters for P4.4

`TryFulfil` has two overloads. The old signature is kept as an adapter for existing internal
callers including the unchanged self-test. It does **not** silently ignore arguments — the P2.1
failure mode — it *synthesises* the missing ones via `BuildCompatibilityProspect(pawn, profile)`
and uses `pawn.thingIDNumber` as market identity, then routes to the same planner-backed path.

**But it seeds differently from production.** A test calling the old overload exercises
`thingIDNumber`-seeded variation, not prospect-identity-seeded variation. **P4.4's diversity
assertions must call the PRODUCTION overload**, or they will be measuring the adapter rather than
the feature.

**P4.3b is the first unit in this stage that changes what a player sees.** Everything before it was
inert: a scorer with no callers, a catalogue with no callers, a planner with no callers. This is
where the allocator stops taking the first passing package.

### Known limitation carried forward from P4.3 — the first suspect if diversity looks weak

`LaborProspect` has **no unique stable prospect ID**, so the planner derives identity from stable
record fields. Two prospects from the same settlement with identical skills, passions and price will
collide and receive an identical package. That is a genuine cloning case, not a rounding detail.

P4.4's diversity assertions must be able to see it. **If diversity evidence comes in weak, look here
before touching the weighting** — the fix would be a stable prospect identity, which is a larger
change than it sounds because it likely implies persistence.

**P4.3 is split deliberately.** The plan's flow is plan -> instantiate -> classify -> bounded retry.
Only the PLAN half is in this unit: it returns data, creates no `Thing`, touches no `Pawn` and does
not call `Classify`. Instantiation, the authoritative classify and the retry are the next unit. That
keeps the riskiest change — replacing the allocator's selection strategy — off the critical path
until the planner itself can be inspected.

**The defect being fixed, stated plainly:** today the allocator sorts candidates deterministically
and takes the first package that passes, so every similar pawn gets the same minimum-passing
loadout. That is the tier-uniform problem. A planner that always picks the best item would be the
same bug wearing better clothes, which is why the prompt demands weighted sampling and asks the
worker to show how it avoided collapsing to always-best.

**Determinism is the load-bearing requirement in P4.2**, more than caching. The assembler will take
a *seeded sample* over what the catalogue exposes, and a seeded sample over a `Dictionary` or
`HashSet` enumeration order is not reproducible. The plan requires the same applicant not to reroll
on a window redraw and requires tests to be reproducible, so every exposed collection must have a
stated ordering key.

### A stronger finding than "the assertion passes"

`EstimateDirectLabor(IntercolonyWorldComponent state, ThingDef product, float days)` **does not take
`stuffDef` at all**, and `RelevantProductionGoods` returns `List<ThingDef>`. Material-invariance of
Paid labor is therefore **structural, not incidental** — labor cannot depend on stuff without
changing that signature. That is a much more durable guarantee than one green world, and it is why
the mutation had to thread the parameter in rather than flip a comparison.

**WORKING TREE IS DELIBERATELY DIRTY** — three edits to `BusinessReportService.cs`: an optional
`ThingDef stuff = null` parameter on `EstimateDirectLabor` (optional so other callers still
compile), `contract.stuffDef` passed at the call site, and the apportionment scaled by a
stuff-derived factor. **If interrupted, restore with
`git checkout -- Source/Intercolony/Core/BusinessReportService.cs`.** Committed state `05cc1f4` is
correct.

**P3.5 is expected to be an AUDIT, not a fix.** The plan records that `EstimateDirectLabor` already
ignores `stuffDef` and keys production ability by `ThingDef`, so these assertions should pass
against unmodified production. The worker is instructed that if they do NOT, it must stop and
report rather than change production — a real material-dependence in labor would be a finding for
Foreman to decide on, not something to quietly fix inside a test unit.

The hollow shape guarded against here: an invariance assertion whose two inputs are secretly
identical proves nothing, so the unit must separately prove the materials figures really differed.

**The re-aimed fixture is sound, and that was checked.** It uses `ThingDefOf.Silver`, and
`IntercolonyProductClassifier.cs:159` excludes Silver from procurement as an explicit structural
invariant — the comment says it is enforced in code rather than in the §64 blacklist precisely so
another mod's XML cannot remove it. So both tiers are genuinely empty for Silver, and the assertion
is stable rather than world-luck dependent.

**B6 is the one worth watching.** It captures `PeekNextId()` before and after `Estimate`, plus
request, quotation, listing, contract, order, reputation, market-state, timeline, supplier-consumption,
refresh and RNG state — that is the "rendering a report creates nothing" guarantee, which is the
hardest of the plan's prohibitions to keep honest.

**Push: DONE at the P3 gate.** `origin/foreman/playtest-corrections-2026-09-18`, 22 commits,
upstream tracking configured. Push again as later stages close. Merging to `main`, tagging,
releasing and Workshop publishing all remain operator-reserved and untouched.

**HEAD is deliberately red on one assertion.** The production change is correct and the test is
stale; holding correct code hostage to a stale assertion would have been worse, and the reason is
recorded in the commit body as well as here. P3.4 re-aims it.

**Do not read a coverage delta out of gate totals.** They drift with the world: 1751, 1757, 1757,
1758, 1758 across this run's gates. That P2.2 netted +4 assertions is not visible there and should
not be claimed from it. The evidence that the new assertions exist and bite is the isolated `labor`
run (**107/0/5**, all five skips pre-existing) plus the two mutation proofs.
Foreman load: 2026-09-19 16:10
Plan: C:\dev\INTERCOLONY_PLAYTEST_CORRECTION_PASS_II_PLAN.md · worker copy `docs/PLAYTEST_CORRECTION_PASS_II_PLAN.md`
Mode: autonomous run-to-halt
Foreman: 89fdf02 · source C:\dev\agent-foreman · https://github.com/Vector-Consulting-IA-Operacional/agent-foreman.git
Fallback: if `Skill(foreman)` is unknown, read `C:\dev\agent-foreman\skill\SKILL.md` then `RUN_CONTRACT.md`.

<!-- Everything above this line is the header. A fresh session reads only the header. -->

## The run

**Authority:** `C:\dev\INTERCOLONY_PLAYTEST_CORRECTION_PASS_II_PLAN.md`, copied for workers to
`docs/PLAYTEST_CORRECTION_PASS_II_PLAN.md`. It is the newest product authority for F04, F16,
F19/F20, F23 and F24 and beats older playtest/finalization plans for those behaviors.

**Branch:** `foreman/playtest-corrections-2026-09-18`, cut from `foreman/playtest-polish-2026-09-14`
at `b4bab42` (schema 59, previous whole-suite gate 1732/0/19). The polish run is complete and must
not be reset or recreated.

**Autonomy.** Autonomous run-to-halt. Halt only for a genuine Human Blocker: an unresolved
player-facing product decision, an irreversible/external action, a save-data-loss risk with no safe
route, or two plan requirements that cannot both hold. A hard implementation, a failing test and an
unfamiliar vanilla API are not blockers. If one chain blocks, quarantine it and keep executing the
others.

**Operator-reserved — do not perform:** merge to `main`, tag, GitHub release, Steam Workshop
publish/update, any irreversible release action. Clean halt is a completed, tested, pushed
development branch.

## Stages

| Stage | Scope | Status |
|---|---|---|
| P0 | baseline + performance measurement + scope lock | **closed** — gate 1733/0/18, exit 0 |
| P1 | F04 Produce Controls UX consolidation | **closed** — gate 1741/0/17, delta 0 |
| P2 | F16 employee action layout | **closed** — gate 1739/0/19, delta 0 |
| P3 | F19/F20 Business benchmark + labor consistency | **closed** — gate 1749/0/19, delta 0 |
| P4 | F23 diversified equipment packages | in progress |
| P5 | F23/Emergency posting performance | not started |
| P6 | F24 Emergency Job Posting quote persistence/hiring | not started |
| P7 | F24 raid-like pod attention letter | not started |
| P8 | integration, pending-playtest cleanup, whole-suite gate | not started |

Serialize P2/P6 where they collide with Labor UI/domain files. Never run parallel workers.

## Stage P0 units

| Unit | Scope | Status |
|---|---|---|
| P0.0 | branch cut, baseline build | done — build 0/0 at `ae3b5e6` |
| P0.1 | timing instrumentation across the Emergency posting path | done — `333f979`, build 0/0 |
| P0.2 | deterministic Emergency+Elite stress fixture | done — build 0/0 |
| P0.3 | register it as bridge-runnable self-test `posting-timings` | done — `748d5bd` |
| P0.4 | live baseline measurement (Foreman) | done — measured, see below |
| P0.5 | fix the fixture's self-accusing log wording | done — `e95a5e1`, exit 2 → exit 0 |
| P0.6 | baseline whole-suite gate (Foreman; needs live game) | FAILED — 2 failures, see below |
| P0.7 | keep the measurement suite out of the gate | done — `6e48636` |
| P0.8 | re-run the gate | done — **1733/0/18, exit 0, log CLEAN** |

Compared with the plan's stated previous gate of 1732/0/19: one more pass, one fewer skip. Skips
here are world-dependent, so that difference is world variation, not a coverage change.

**`Shooting violations=1` did not reproduce, and is closed as unattributed.** Removing the
measurement's pool pollution is the one thing that changed, but a single passing fresh-world run
cannot separate "caused by the pollution" from "world luck" — this suite has a documented
world-luck history (`f63a831`). It is not being called fixed. If it returns, attribute it against
base `b4bab42` before touching product code.

## P1.4 mutation proof — complete

| Step | Result |
|---|---|
| baseline `produce -Fresh` | 90/0/2, exit 0, log CLEAN; suite 85 → 90, both skips pre-existing Scribe probes, so no new assertion silently no-opped |
| A1 mutated (`ProduceGizmoPatch.cs:138` → `new Dialog_ProduceControls(map, cell)`) | **89/1/2** — sole failure `object-side Produce controls opens Dialog_ProducePresetManager (actual gizmo action opened no manager window)`; other four stayed green, correct since the mutation only touches the object gizmo |
| reverted | 90/0/2, exit 0, log CLEAN, tree clean |

This is the plan's first mutation clause discharged. The second (stripping the worker restriction
when every selected pawn is unavailable) belongs to P1.5.

## Stage P1 units

| Unit | Scope | Status |
|---|---|---|
| P1.1 | move the four Produce designators Orders → `IntercolonyProduction` | done — `3f8da48` |
| P1.2 | object gizmo routes to the manager; manager gains `New production` | done — `e8ac950` |
| P1.3 | entrypoint + registration assertions (acceptance 1–5) | done — `a49c9bc` |
| T1 | prerequisite: fix `dev.ps1` Player.log lock race | done — `77b2a88`, proven |
| P1.4 | mutation proof of those assertions (Foreman; needs game) | done — green/red/green |
| P1.5 | worker-gate assertion (acceptance 6) | done — `ce2171c` |
| P1.6 | mutation proof of the worker gate (Foreman; needs game) | done — green/red |
| P1.7 | stage gate: whole suite (+ P1.6 green re-confirm) | green — 1740/0/17, but pawn delta +1 |
| P1.8 | stop the worker-gate fixture leaking its pawn | done — `87ac8fb` |
| P1.9 | prove delta 0 on `produce` | done — 91/0/2, delta 0 |
| P1.10 | final whole-suite stage gate; delta 0 required | 1737/1/19, delta 0 — blocked on A1 |
| A1 | attribute the intermittent `Shooting violations=1` failure | done — world-dependent ~40%, not ours |
| A2 | recon: can a below-minimum applicant match? | done — **yes, real defect** |
| A3 | fix: recheck the requirement against the materialised pawn | done — `16209e7` |
| A4 | green proof: 10× `job-posting -Fresh` | done — 0 Shooting failures in 10 |
| P1.11 | P1 stage gate, re-run after the labor fix | **green — 1741/0/17, delta 0** |
| P1.12 | record F04 human evidence in `docs/PENDING_PLAYTESTS.md` | done — `4ec90b2` |

## Stage P2 units

| Unit | Scope | Status |
|---|---|---|
| P2.1 | header = termination only; explicit renewal/transition groups | done — `69ffd8a` |
| P2.2 | six-state assertions + delete the compatibility scaffolding | done — `b6d0ef7` |
| P2.3 | both mutation proofs (cross-wiring `Let them go` / `Not now`) | done — both red |
| P2.4 | stage gate: whole suite | **green — 1739/0/19, delta 0** |
| P2.5 | pending-playtest entry for F16 | done — `bf39b1d` |

## Stage P3 units

| Unit | Scope | Status |
|---|---|---|
| P3.1 | FOCUSED RECON: pure non-mutating supplier quote seam | done — answered |
| P3.2 | extract one-supplier quote helper (zero behavior change) | done — `f4f2fa0`, rfq 240/0/0 |
| P3.3 | wire Business tier 2 using the helper | done — `8239479`, 1 stale assertion red |
| P3.4 | re-aim stale assertion + acceptance assertions B1–B6 | done — `d1eb86d`, ledger 51/0/0 |
| P3.5 | labor-invariance audit (material must not move Paid labor) | done — `05cc1f4`, ledger 55/0/0 |
| P3.6 | the plan's two mutation clauses | done — both red |
| P3.7 | stage gate: whole suite | running |
| P3.8 | pending-playtest entry for F19/F20 | done — `f49fbcd` |

## Stage P4 units

| Unit | Scope | Status |
|---|---|---|
| P4.1 | pure def-driven item scorer and bands, no callers | done — `ad8293d` |
| P4.2 | cached def-driven equipment catalogue | done — `d347deb` |
| P4.3 | seeded weighted package planner (plan only) | done — `b12649a` |
| P4.3b | wire into allocator: instantiate, Classify, bounded retry | written, UNCOMMITTED — broke fulfilment |
| P4.3c | restore the pawn wearability filter | done — wearability failures cleared |
| P4.3d | retry escalation so a promised tier is achievable | done — ladder in place |
| P4.3e | port every remaining old-allocator constraint | done — set conflicts cleared |
| P4.3f | final attempt = the old deterministic search | done |
| **P4.3b–f** | **committed together as `1dbfd21` — suite 1750/0/19** | **done** |
| P4.4 | prove packages are actually diverse (D1–D7) | done — `3ada034`, **RED: feature broken** |
| P4.5 | FOCUSED RECON: why sampled packages miss Elite | done — cause found |
| P4.6 | package-wide quality stratum sampling | done — helped Professional, not Elite |
| P4.7 | RE-CUT: seeded ORDER over the proven search | **done — `567b293`, labor 119/0/0** |
| P4.8 | both mutation clauses | **done — both red** |
| P4.9 | stage gate: whole suite | running |
| P4.10 | pending-playtest entry for F23 | not started |

**P4 is the biggest stage in the plan and the one most able to break shipped behaviour.** It is a
selection-strategy rewrite, so it is split at the foundation: the scorer is pure and callerless,
the catalogue caches it, and only then does the assembler replace the existing walk.

**Locked, so no worker trades them away:**
- The real generated loadout stays authoritative — `LaborEquipmentTierService.Classify` decides
  whether a package meets its promise. The scorer is an INPUT to selection, never a substitute.
- Settlement capability is the hard gate on whether a tier may be *promised*; the promised tier
  then constrains which gear pool may satisfy it. Those are different concepts and must not merge.
- **Elite must not be re-gated to Spacer** — measured 0 of 616 prospects and structurally dead.
- No `defName` whitelist as the authoritative classifier; DLC and modded gear participate by stats.
- Civilians never gain an offensive weapon merely because the tier is high.
- Abundance sliders control how often a tier is promised, never which items are chosen.

**Interaction with P0 and P5, worth remembering:** P0 measured equipment fulfilment at
**500 ms of a 530 ms `TryPost`, 94%**. P4 rewrites exactly that path, so the P0 baseline stops
being comparable the moment P4.3 lands, and P5 must re-measure rather than trusting the old number.
The cached catalogue in P4.2 is also the most likely place that cost improves.

## P3.6 mutation proofs — complete

| Plan clause | Mutation applied | Result |
|---|---|---|
| substituting generic `MarketValue` must fail a benchmark-source assertion | tier-2 median replaced with `productDef.BaseMarketValue` | **54/1/0** — sole failure B3, detail `DiningChair; Gold 118.8; Plasteel 118.8` |
| adding `stuffDef` to the labor partition key must fail the material-invariance assertion | `stuff` threaded into `EstimateDirectLabor` and the apportionment scaled by it | **52/3/0** — both invariance assertions red (Gold labour 165 vs Silver 151.5), plus one pre-existing apportionment assertion as expected collateral of scaling that line |

The second mutation's failure detail also confirms the fixtures are genuinely different — materials
−10350 vs −1035 — so the invariance pair was never two secretly identical inputs.

**The plan sets exactly two mutation clauses for P3**, and both are P3.6's job:

1. substituting generic `MarketValue` must fail a benchmark-source assertion;
2. adding `stuffDef` to the labor partition key must fail the material-invariance assertion.

The second cannot run until P3.5 exists, which is why the labor audit comes before the mutations.

## The stale benchmark assertion — checked, not assumed

`IntercolonyLedgerSelfTest.cs:602`, `the market benchmark is unavailable rather than invented`,
failed on `product TextBook; has median True; observed 425.973; matching listing/request search
returned none`.

**The worry was a real one:** the contract it builds sets `stuffDef = null`, so if `TextBook` were
`MadeFromStuff` then tier 2 had just fired on an `Any material` contract — a straight D-G/D7
violation and a genuine defect.

**It is not.** `TextBook` inherits `BookBase` (`reference/vanilla-defs/Core/Defs/Books/BookDefs.xml`),
which declares no `stuffCategories`. It is not made from stuff, so `stuffDef = null` IS its exact
specification and quoting it is correct.

So the assertion encoded "tier 1 empty means dash" — precisely the behaviour the plan replaces, and
its acceptance criterion 1 explicitly requires a non-dash median with no prior player RFQ.

**It is being re-aimed, not deleted.** The guarantee it protects — unavailable rather than invented
— is D10 and still wanted; it now belongs to the case where NEITHER tier has an observation.

## P3.1 recon result, and the decisions it forced

**Verified by Foreman, not taken on report:** `RollSupplierNegotiationMultiplier` really is
`Rand.Range(0.94f, 1.1f)` (`IntercolonyPricing.cs:615`); `TryCalculateReferenceUnitPrice` exists at
`ProcurementContractService.cs:587`, called at `:501`; and Business's quality match really is a
FLOOR, not equality (`BusinessReportService.cs:1198`, comment: *minQuality is a floor, shown as
"Quality+"*). That last one corrected my prompt's premise.

**The key finding: the price formula is already RNG-free.** Randomness enters only at the
negotiation multiplier and at eligibility/terms selection. The full preview path does touch global
`Verse.Rand`, but inside seeded push/pop scopes that restore the ambient stream.

### Decisions — Foreman's, recorded so no worker re-opens them

- **D-A. Use the scoped `PushState`/`PopState` + `finally` idiom, not a hand-rolled hash-to-float.**
  The project already establishes and *tests* that idiom (`IntercolonyProfileSelfTest.cs:143`
  asserts generation restores global RNG). D4's intent is that rendering must not perturb world
  RNG; a scope that restores it satisfies that. A parallel hash-based price would create a SECOND
  pricing formula, which is precisely what the plan forbids.
- **D-B. Deterministic seed** = `EconomySeedForReadOnly` + `RefreshCount` + settlement ID + def
  hash + stuff hash/sentinel + quality/sentinel, under a Business-specific salt so it cannot
  collide with real procurement's stream.
- **D-C. Compute once per report, never per frame.** Rendering is per-frame; N seeded supplier
  quotes per frame is the same class of synchronous cost P0 measured at 500 ms. The plan's own
  wording — deterministic for the current world/refresh — makes it cacheable by construction.
- **D-D. Tier-2 eligibility = the deterministic effective-supply threshold (>= 0.35), WITHOUT the
  response roll.** Sol flagged "eligible supplier" as *nao determinado* because RFQ, listing and
  contract-preview each gate differently. The response roll answers "did they bother to reply",
  which is not a fact about whether a price exists. Foreman takes the deterministic half.
- **D-E. The indicative benchmark is a DELIVERED price, and the tooltip must say so.** Sol flagged
  the fulfillment convention as undetermined, and it changes the number. Delivered is the complete
  landed cost and the one comparable to what the player actually pays. **This is a player-facing
  number the plan did not decide — operator may want to revisit it.**
- **D-F. Tier 2 quotes the EXACT requested quality**, unlike tier 1's floor. Tier 1's floor
  behaviour is pre-existing and out of scope; the asymmetry is deliberate and recorded here so it
  is not later "fixed" by accident.
- **D-G. `Any material` stays a dash.** Sol confirms real RFQs select a concrete stuff before
  pricing, so there is no honest generic-any-stuff benchmark to show.

### Also flagged by the recon, not actioned

Tier 1 does not deduplicate by settlement — it can count several listings or quotations from the
same supplier in one median. That is pre-existing, outside this pass's scope, and recorded rather
than fixed.

**Why this stage gets the run's second recon.** The plan marks it `FOCUSED RECON REQUIRED`, and the
questions are not greppable: whether the supplier pricing path consumes global RNG, and what the
smallest safe extraction is. RNG is the crux — if that path draws from global RNG, calling it while
rendering a panel would both make the number flicker between frames and perturb world RNG as a side
effect of drawing UI. Foreman deliberately did not pre-empt the recon by tracing it first.

**Locked for P3, so a worker cannot quietly trade them away:** the player's own contract price is
not market evidence; vanilla generic `MarketValue` must not be relabelled as `Median market price`;
no real PurchaseRequest or RFQ may be created to populate a report; `Any material` keeps an honest
dash rather than a blended wood/gold/steel number; and F20 is not redesigned into measured work
attribution in this pass.

## P2.3 mutation proofs — complete

| Step | Result |
|---|---|
| baseline `labor -Fresh` | **107/0/5**, exit 0, log CLEAN, pawn delta 0; all five skips pre-existing emergency-route ones, so A1–A8 genuinely ran |
| `Let them go` -> `ConfirmDismiss(contract)` | **105/1/6** — sole failure A3 |
| `Not now` -> `RenewalService.Decline(contract)` | **109/2/1** — failures A4 *and* A5 |
| both reverted | tree clean, confirmed by `git status` and grep |

**Both failure reports name the actual bound delegate** (`callback=<DrawEmployeeActionStack>b__1`,
`b__3`) rather than the button label. That is the point of these assertions: a cross-wired action
keeps its label, its position and its enabled state, and only the binding betrays it.
| P2.4 | stage gate + pending-playtest entry | not started |

**Scaffolding P2.2 must remove, not inherit.** P2.1 left a three-argument
`ResolveLifecycleAction(contract, hasLiveTransitionOffer, hasLiveRenewalOffer)` that **ignores both
offer arguments** and delegates to the one-argument resolver, plus `DeclineTransition` and
`DeclineRenewal` enum members that nothing can return. They exist only to keep the old self-test
surface compiling. A parameter that is silently ignored, and an enum member nothing produces, both
read as supported behaviour to whoever arrives next — so they go once the tests are updated.

**The defect P2's mutations exist to catch is cross-wiring.** `Renew` + `Let them go` are the
fixed-term renewal pair; `Keep them` + `Negotiate` + `Not now` are the permanent-transition group.
`Let them go` must call renewal DECLINE — the worker serves out the term and leaves normally, it is
not a dismissal — and `Not now` must touch transition only. The plan requires that wiring
`Let them go` to immediate dismissal fails, and that making `Not now` decline renewal fails.

`PROGRESS.md` is deliberately NOT written per stage — it is recorded once at P8, which the plan
defines as integration and pending-playtest cleanup.

### A4 result, stated precisely

| | pre-fix (`87ac8fb`) | post-fix (`16209e7`) |
|---|---|---|
| runs | 5 | 10 |
| Shooting assertion failed | **2** | **0** |
| ran and passed | 3 | 9 |
| skipped (assertion never ran) | 0 | 1 |

The one skip is counted separately and not as a pass: in run 10 the ordinary-ask control produced
no applicant, so the assertion did not execute. Nine executions, zero failures.

## New finding for Stage P4 — record, do not fix here

Run 10 failed a *different* assertion: `elite is non-zero in the full equipment census`
(`OBSERVED elite=0 of 550; EXPECTED at least 1`), and two Professional-posting assertions skipped
for want of an Elite prospect.

This is F23 territory and belongs to **P4**, not here. It matters to P4's premise: the plan (§3.4)
records that a *Spacer-gated* Elite market measured 0 of 616 and was structurally dead, and that
the accepted gate is Industrial-or-better + Comfortable wealth + the stronger Elite threshold. This
run shows a world producing **0 Elite of 550 under the accepted gate** — so Elite scarcity is not
fully solved by the current gating, and P4 should treat "Elite exists at all" as world-dependent
rather than assumed. That world's census: professional-capable 264, emergency-routed 154, both 154,
Shooting 1+ 428, Professional promise 58, combined eligible 28.

**No new assertion was written for A3, deliberately.** The existing `job-posting` assertion already
covers exactly this and has already been seen red — 2 of 5 runs pre-fix, plus two gate failures.
Sol found no deterministic injection seam for `PawnGenerator` (*não determinado*), so a forced
fixture would mean building one, which is a larger change than the fix itself. The honest evidence
here is the failure rate moving from ~40% to 0, not a new test that has never failed.

## A2 — the defect, and why it is a defect

**Sol recon's finding, with Foreman's own verification of each material claim.**

`JobPosting` carries two requirement predicates — `MeetsRequirement(Pawn)` at `JobPosting.cs:296`
and `MeetsRequirement(LaborProspect)` at `:317`. Their shared doc comment at `:313-315` states the
contract outright:

> *"Kept beside it so the two rules stay visibly the same: a worker who qualifies as a record must
> still qualify once they are a pawn, or the applicant who arrives is not the one who was
> advertised."*

**Verified by Foreman:** in `JobPostingService.cs`, `MeetsRequirement` is called only at `:441` and
`:848`, and both are on the prospect record. **The pawn overload has no caller in the posting path.**

Mechanism: the prospect qualifies on its synthetic census record; `LaborProspect.Materialise` then
generates an *independent* pawn whose own backstory can make the required skill `TotallyDisabled`;
`AlignSkills` deliberately skips disabled skills, so the census value never lands; `AddApplicant`
queues the pawn with no recheck. `MeetsRequirement(Pawn)` tests exactly the condition being
skipped — `!record.TotallyDisabled && record.Level >= minSkillLevel`.

**Player-facing consequence:** a posting that asks for Shooting ≥ 1 can return — and hire — an
applicant who cannot shoot at all. This is in shipped 1.0 code, not something this run introduced.

**Why it is a bug and not a design choice:** the comment above is explicit, there is no emergency
relaxation anywhere (emergency only *adds* a reachability check at `JobPostingService.cs:466`), and
the stale comment near `LaborProspect.cs:108` shows the faulty assumption that a pawn-disabled skill
is also `-1` in the census.

**Recon honesty, preserved:** Sol marked *não determinado* for the exact failed pawn's cause (the
run was not captured) and for a deterministic test seam, and corrected my prompt's premise — the
candidate pool does not contain a below-minimum *prospect*; the mismatch is created at
materialisation. Do not overstate either point.

### Decision — fix it in this run

Scope is justified on three grounds, not on it being interesting: it **blocks clean halt** (the
plan's whole-suite gate fails ~40% of runs, and RUN_CONTRACT §13 requires that gate); it
**invalidates evidence for P6**, which changes hiring in this exact path; and the fix **restores
documented intent** rather than changing any locked semantic. The 12-tile threshold, market depth,
queue cap and the `skill == null` wildcard are all untouched.

**Deliberately deferred:** `TryAccept` (`JobPostingService.cs:972`) also fails to revalidate, so a
wrongly queued applicant could still be hired. Fixing the queue removes the cause; hire-side
defence in depth is a separate decision and a separate unit.

## A1 — the intermittent job-posting failure

Assertion: `an emergency Security Professional posting matches reachable applicants with real gear`,
owned by `IntercolonyJobPostingSelfTest.cs:1665` (suite id `job-posting`). It fails on
`Shooting violations=1` — one matched applicant below the `min=1` Shooting requirement — while every
other observed count matches expectation.

| Gate run | Result |
|---|---|
| P0.6 | FAIL |
| P0.8 | pass |
| P1.7 | pass |
| P1.10 | FAIL |

**What the history already rules out.** It first failed at P0.6, when the only changes on this
branch were measure-only timing instrumentation and a measurement suite — before any P1 Produce
work existed — and it passed at P1.7 with all the P1 work in place. So it does not track this
branch's changes. Nothing committed in P0/P1 touches labor matching: the changes are timing
scopes, a self-test registry flag, a PowerShell retry, an Architect category move, Produce UI
routing, and produce-suite assertions.

**Experiment result — world-dependent, ~40%.** Five `job-posting -Fresh` runs, identical HEAD
`87ac8fb`:

```text
run 1: exit=1  50/1/0  Shooting violations=1  immediate applicants=1
run 2: exit=0  51/0/0
run 3: exit=0  51/0/0
run 4: exit=1  50/1/0  Shooting violations=1  immediate applicants=1
run 5: exit=0  51/0/0
```

Same code, different generated world, so the variable is world generation. **Attribution settled:
this is not a regression from this run.**

**But "world luck" is NOT the conclusion, and must not be recorded as one.** The world may simply
be exposing a real matcher defect: the posting asks for Shooting min 1 and an applicant below that
is being matched. Either the matcher deliberately relaxes the minimum somewhere, or it has a bug
that only surfaces when the generated pool happens to contain a below-minimum candidate. Those have
opposite fixes. A2 (Sol recon) answers which, before anyone edits anything.

This matters beyond the gate: **P5 and P6 both work inside this code.** Entering them without
knowing whether the matcher honours its own skill minimum would build on an unknown.

**Do not touch labor product code until this is attributed.** The plan marks F20's workforce
approximation regression-only and the emergency threshold locked at 12 tiles; "fixing" a world-luck
assertion by editing matching would violate both.

### Remaining for P1 after the gate

- **Human evidence owed** (plan's "Human evidence" list, cannot be automated): object and Architect
  `Produce controls` feel like the same action; `New production` is discoverable; the Production
  category holds the full toolset and Orders no longer does. Record in `docs/PENDING_PLAYTESTS.md`
  at stage close, per the project rule that anything shipped-but-unseen goes there.
- The disabled-`New production` tooltip wording is part of that check — it is the one product
  decision this stage made that the plan did not.

## P1.8 — the fixture leak, attributed by controlled comparison

The P1.7 gate passed but reported `World pawns: 16 -> 17 (delta 1)`. Attributed to the P1.5
fixture, not assumed:

| Run | Pawn delta |
|---|---|
| `produce` before the assertion existed (`p1-4-green-confirm`) | 14 → 14, **0** |
| `produce` after it (`p1-6-green`) | 14 → 15, **1** |
| `produce` mutated (`p1-6-red`) | 12 → 13, **1** |
| whole suite (`p1-7-gate`) | 16 → 17, **1** |

Every added pawn is `kind=Colonist faction=New Arrivals situation=Dead spawned=False
keptForever=False`, and in the mutated run it was named **Brianna Chouinard** — the same pawn that
run's failing assertion named as its selected unavailable worker. That is conclusive.

**Severity: low but not ignorable.** `keptForever` stayed 12 → 12 in every run, so nothing is
pinned and RimWorld's WorldPawns GC can collect it — unlike the earlier `posting-timings` leak,
which pinned its pawn permanently. It is still a test fixture leaving world state behind, in a
project whose CLAUDE.md records four phases lost to exactly that class of bug, so it gets fixed
rather than recorded.

## P1.6 mutation proof — complete

| Step | Result |
|---|---|
| baseline | 91/0/2 — was 90/0/2 before the assertion, so it ran rather than skipping |
| gate mutated (`ProduceWorkerGatePatch.cs:47` requires a spawned selected worker) | **90/1/2** — sole failure `an all-unavailable selected-worker Produce program stays restricted`, detail `Brianna; Spawned False; Map null` |
| reverted | confirmed in the P1.7 whole-suite run |

Both plan mutation clauses discharged: the object gizmo entrypoint (P1.4) and the worker gate (P1.6).
| P1.5 | stage gate: `produce` suite + whole suite | not started |

**D6 (this stage). `New production` with no context cell is disabled, not hidden.** The plan did
not decide this case. The editor `Dialog_ProduceControls(Map, IntVec3)` requires a cell, and the
Architect entry point has no object and so no cell. The plan requires one conceptual window and
one action set from both entry points, so the action is always present; it is disabled with a
tooltip explaining that a production is created on a specific object. Rejected alternatives:
hiding the button (breaks "same action set"), a target-picking mode (new mechanism the plan did
not ask for), and letting the editor accept a null cell (weakens an invariant for a UI convenience).
Reversible, and the plan's human-evidence check — "`New production` is discoverable" — will catch
it if the call is wrong.

## P0.6 gate failure — 2026-09-18 19:30, fresh world

`dev.ps1 test all -Fresh`, exit 1, `Log signal: CLEAN`, 23 assertions skipped.

```text
FAIL  an emergency Security Professional posting matches reachable applicants with real gear
      (Shooting violations=1; all other observed counts matched expectation)
FAIL  no world pawns leaked by postings opened and closed (35.2)  (22 before, 23 after)
```

**Failure 2 is ours and diagnosed.** `posting-timings` posts a real Emergency + Elite job through
the live `TryPost`, which pins an applicant as keptForever and leaves the posting open. +1 pawn and
`Postings: 0 -> 1` match exactly. It is a measurement, not a regression test, and does not belong
in the gate. P0.7 excludes it from `RunAll` while keeping it runnable by id.

**Failure 1 is NOT yet attributed.** Do not assume it is ours and do not assume it is world luck —
this run's changes were instrumentation-only, but the measurement suite runs before it and pollutes
the candidate pool, which is a live hypothesis. `-Fresh` generates a new world each time, and this
project has a documented history of world-luck suite failures (`f63a831`). P0.8 re-runs the gate
with the pollution removed; if it persists, attribute it by a controlled comparison against base
`b4bab42` before touching any product code.

## P0 baseline — measured 2026-09-18 19:17, fresh world

`dev.ps1 test posting-timings -Fresh`, world census 616 workers across 28 settlements.

```text
TryPost total:                          529.887 ms
  lightweight census/filtering:          12.606 ms
  candidate/application selection:        3.575 ms
  Pawn materialisation:                   5.407 ms  (calls=1)
  equipment fulfilment:                 500.041 ms  (calls=1, mean=500.041 ms/call)
  final applicant publication / UI:       7.017 ms
```

**The dominant synchronous cost is `LaborEquipmentAllocator.TryFulfil` — 94% of TryPost, in a
single applicant.** Everything else together is under 30 ms. P5 optimisation belongs in the
allocator and nowhere else; census, selection, materialisation and publication are not worth
touching. At the `MaxWaitingApplicants` cap of 6 this extrapolates to roughly 3 s of synchronous
work, which matches the hitch seen in play.

Caveat recorded honestly: this run drew **1 applicant, not a full queue of 6**, so the per-call
figure is one sample and the extrapolation is arithmetic, not measurement. Do not quote 3 s as
measured.

## Non-blocking observations

- **Exit 2 on the baseline run was a false positive of our own making.** `dev.ps1:949` scans new
  log lines with `(?i)\bException\b`; the fixture logged `exception=none`, the only match in the
  entire 2,115-line Player.log. Assertions were 2/0/0 and the run was genuinely clean. P0.5 fixes
  the wording, not the detector.
- **World pawns kept-forever grew by 1** (`Tammy Hobbs`, pinned applicant). Pre-existing applicant
  pinning behaviour, not caused by this run's changes. Noted, not actioned.
- **`dev.ps1 ... -Fresh` has a Player.log file-lock race — now seen TWICE.** Second occurrence at
  P1.4, and it fired even though Foreman had confirmed the handle was free immediately before
  launching, which locates the race after `Launching RimWorld -quicktest...`: the script opens the
  log with exclusive sharing while the process it just started already holds it. It is intermittent
  and clears on retry, but it is now interfering with the verification loop rather than being a
  one-off. **If it costs a third run, fix it in `dev.ps1` before continuing the plan** — open the
  log with `FileShare.ReadWrite` / retry with backoff — under RUN_CONTRACT §8 (fix the smallest
  prerequisite, prove it, return to the plan).
- **`dev.ps1 ... -Fresh` first occurrence.** P0.6 attempt died with
  `The process cannot access the file ... Player.log because it is being used by another process`:
  the script stopped the previous RimWorld and reopened the log before the dying process released
  its handle. Not a product defect and not caused by this run's changes. Worked around by stopping
  RimWorld and confirming the lock was free before relaunching. Worth a durable fix in `dev.ps1`
  (wait for handle release after `Stop-Process`) if it recurs.
- **RimWorld could not be muted: it opens no render audio session** under the bridge launch, so
  there is nothing to mute. Verified per-app, system volume never touched.

## Decisions — this run

- **D1. The base is `b4bab42`, not `main`.** The plan audited that HEAD; the branch was cut there so
  the stated baseline facts hold.
- **D2. The whole-suite gate is Foreman's own work, not a worker's.** `dev.ps1 test all -Fresh`
  launches and drives a live RimWorld process; workers stop at safe build/test boundaries and cannot
  own a long-lived interactive process.
- **D3. Elite must not be re-gated to Spacer.** A Spacer-gated Elite market measured 0 of 616
  prospects and is structurally dead. The accepted gate is Industrial-or-better + Comfortable wealth
  + the stronger Elite capability threshold. Capability gate and item ceiling stay distinct concepts.
- **D4. The 12-tile conventional-emergency threshold is not retuned in this pass.** 0 qualifying
  routes in 748 examined source cases is known and accepted; P6 makes the route correct when a
  qualifying source exists. Constructed fixtures are valid evidence for the 5–9h conventional band.
- **D5. `graphify-out/` stays untracked.** Regenerated graph output is not product work and must not
  block a clean halt.

## Human evidence accepted — do not re-prove

Plan §2 records the operator's own play observations for F04 (bulk apply, preset menu, persistence,
unavailable worker), F16 lifecycle, F19 material term, F23 applicant supply, F24 direct-Market
timing, pod visual, and the two confirmed F24 defects (hire reverts to ordinary travel; letter
`Jump to location` disabled and no pause). Do not spend worker time re-proving these.

## Operational note — dispatch with the `codex` bash shim, never `codex.cmd`

P1.12 took six attempts. Two separate causes, and the second is the one that matters.

**1. Non-ASCII bytes in the prompt (exits 126 / "Access is denied").** That prompt quoted
CLAUDE.md's UTF-8 double-encoding warning and embedded the literal mojibake bytes as examples.
Passing them as an argv element produced a command line the launcher refused, surfacing as a
permission error rather than an encoding one. Describing the mangling in words instead fixed it.
**Keep dispatch prompts ASCII.** Em dashes have survived; anything pre-mangled or Latin-1 will not.

**2. `codex.cmd` silently truncates a multi-line prompt to its FIRST LINE.** This is the dangerous
one: the run exits 0 and looks successful. The worker replied *"Understood... What would you like
me to handle?"* because all it ever received was the `Repository:` line, and it changed nothing.
cmd.exe cannot carry embedded newlines in an argument; the bash shim passes argv intact, which is
why every earlier dispatch this run worked.

**So: dispatch with `codex`, not `codex.cmd`.** The shim's single "bad interpreter" failure was
transient and is not a reason to switch launchers.

Three checks worth keeping:

- Exit 126 / "Access is denied" means the launcher never started; nothing was modified.
- A PowerShell `*>` redirect swallowed the real error and reported exit 0, with a zero-byte log.
- **A worker that reports success while `git status` shows nothing changed did not run the task.**
  Verify the diff, not the exit code.

## Operator items

- Heartbeat cron `38e7d64d` is **session-only** and auto-expires after 7 days; it dies with this
  Claude session. A fresh session must re-arm it.
