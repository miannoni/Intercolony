# Intercolony 1.0 Implementation Status

The continuity mechanism between sessions. Read `docs/INTERCOLONY_1_0_IMPLEMENTATION_PLAN.md`
first; this file says where in that program we actually are.

Current stage:      Stage 2 — Market fundamentals overhaul
Current slice:      2H — coarse economic chains between categories
Last completed:     2G — completed trades nudge local pressure (2026-08-21)
Current save schema: 44
Current mod version: 0.9.3
Branch:             `1.0` — branched from `main` at `0f55a27`, merges back at Stage 8

## Stage status

- [x] Stage 0 — Program spine (gate closed 2026-08-21)
- [x] Stage 1 — Settlement economies (gate closed 2026-08-21; criterion 7 is a UI read, see below)
- [ ] Stage 2 — Market fundamentals overhaul
- [ ] Stage 3 — Circumstance-driven economic events
- [ ] Stage 4 — Brand strength & colony specialization
- [ ] Stage 5 — Commercial relationships & negotiation
- [ ] Stage 6 — Procurement parity
- [ ] Stage 7 — Commercial history
- [ ] Stage 8 — 1.0 integration and release gate

## Second full-suite run — 2026-08-21, and 2B is unblocked

Run through the dev test bridge (MCP `rimworld_run_all_self_tests`) against a live `-quicktest`
world, no human at the keyboard. **17/17 suites ran, 847 passed, 0 failed, 13 skipped.** World-pawn
delta 0 (12 → 12), open postings 0 → 0, and both leak guards read `OK` — timeline unchanged at 0
records, market pressure unchanged at 0 settlements. `Player.log` gained no exceptions; the only
`Error` matches are RimWorld's own `Error check all defs` profiler line. 566 entity ids consumed.

**Every fix from the 2026-08-20 triage is confirmed.** The two animal assertions, the payroll
signing-fee assertion, both ledger deltas and the `long term` interference all pass. The six suites
that had been written and never executed have now run: `economy` 19, `timeline` 47, `profile` 154,
`market` 81.

**The `job posting` failure did not reproduce, and was checked in isolation first.** Run alone on a
world with 0 open postings and 12 world pawns, it passed 25/0 with a world-pawn delta of **0** — then
0 again inside the full run. That is consistent with the standing `MatchAll` hypothesis (applicants
materialised for postings the suite did not create), because this world had no foreign postings for
it to match against. **It is not proof of the hypothesis.** The failing run was on a 74-pawn world
and this one is a bare 12-pawn world, so the condition that produced it may simply be absent rather
than fixed. The decisive experiment — open a posting, then run the suite — is not reachable through
the bridge, which exposes no posting-creation action. Left recorded rather than closed.

**The 13 skips are the world, not the code**, same as before and now two more of them: `animal` 11
and `order` 2. A bare `-quicktest` map has no prisoner, slave, caravan, bonded pair or pregnant
animal, and one home map cannot exercise the two-colony assertion. Re-running the suite on the
`Fenhana` save would convert most of these and exercise the migration path again.

## First full-suite run — 2026-08-20

`Run ALL self-tests` on a `-quicktest` world. **17/17 suites ran, 853 passed, 8 failed**, and
the leak check read `OK` on both lines — the guards held, nothing was written into the player's
history. 3,108 entity ids consumed, which is expected.

**The runner had a bug of its own, found by its first run.** The animal suite ends
`58 passed, 3 failed, 8 SKIPPED — not a clean run`, and the table reported `skipped 0`. The
summary regex was case-sensitive and the suite shouts `SKIPPED`, so the third group never
matched. An aggregator that hides skips is worse than no aggregator — §20.1 exists precisely
because a skipped assertion is not proof, and this quietly turned eight of them into proof.
Fixed with `RegexOptions.IgnoreCase`.

### The two animal failures — neither was a regression

**`goods price is bit-for-bit unchanged from the pre-animal formula`** — expected 2.702, got
1.756, a ratio of exactly 0.65. `EffectiveEconomyDifficulty` is the slider times
`EconomyDifficultyBaseline` (1.35), and the selling factor is `2 - that`, so a slider set to
1.0 leaves **0.65, not 1.0**. The test's economy-difficulty slot read `expected *= 1f` and had
done since before the difficulty scales were recentred on 2026-08-10 — so it claimed production
had changed when only a constant had. The test was wrong; production was right.

**`adult female pregnant animal price exactly applies 1.20 then 1.40`** — expected 1176, got
1176.00012, about one float32 ULP. It cannot be a Stage 1 regression: that path calls
`IntercolonyPricing.BaseValue(race, null, spec)`, which takes no profile, so demand never
reaches it. Two multiplications in a different order differ in the last bit, and exact `==`
cannot tell that from a changed formula. It now compares within one ULP.

Worth noting *why this surfaced now*: the assertion picks whichever race the current world
happens to have loaded, so its result depended on the save. This run drew `Bear_Grizzly` and hit
a rounding edge that other worlds do not.

### The other four failures — one structural, two stale assertions, one still open

Triaged by Codex; every file:line it cited was checked against the source before acting on it,
and it corrected one of my own hypotheses.

**`long term` — INTERFERENCE, and the cause was the runner.** Employer standing is global to the
colony, not per settlement. The payroll suite drives real missed payrolls and a walkout on
purpose (−6 each, −18) and its cleanup restores silver and candidates but never the reputation.
`RenewalService.MinimumStandingToRenew` is 40, so by the time the long-term suite asked whether
a well-treated worker would stay, payroll had already put standing below the threshold.

**This was worse than a test-ordering problem: running the payroll self-test permanently
damaged the player's real standing as an employer**, and always had — it ran under plain
`WithState`. The diagnostic guard now snapshots and restores employer standing, and *every*
self-test action is wrapped in it rather than the four I had covered. The contract save/load
probes stay unguarded deliberately: they exist to plant state that survives.

**`ledger` ×2 — the assertions measured the wrong thing.** `LedgerService.Summarise` reports the
whole ledger; the test added three fixtures and asserted on the total, so on a colony that had
actually traded it was counting the world's real sales too — 13,851 against a ceiling of 1,700.
The windowing was working correctly. Both assertions are deltas now.

*My hypothesis here was wrong and worth recording as such:* I guessed a low game tick on a
`-quicktest` world. It cannot be — the window cutoff and the fixture ages are both computed by
subtracting from `GenTicks.TicksGame`, so it cancels out entirely.

**`payroll` — a stale assertion.** It claimed a periodic hire takes nothing up front, which
stopped being true when daily and per-quadrum hires gained a five-day signing fee;
`WageStructureUtility.UpFrontCost` returns `SigningFee` for every non-prepaid structure, and
0.9.2 shipped a fix specifically to *disclose* that fee. The same suite already asserts signing
fees exist sixty lines earlier. It now checks the fee is exactly the signing fee and that the
whole term is not charged.

**`job posting` — did not reproduce on 2026-08-21, and is not closed.** Six world pawns appear
inside that suite's own measurement interval (74 → 80), so it is not earlier suites' pawns being
counted. It may be `MatchAll` materialising applicants for postings it did not create. **This
project has history here** — `CLAUDE.md` records a static pool that leaked pawns and `Faction`
objects between games for four phases — so this is not something to quiet down while unexplained.
The prescribed check (the suite alone, fresh map, no open postings) was run and passed 25/0 with a
delta of 0, as did the full run. That is consistent with the `MatchAll` hypothesis but does not
confirm it: the failing world had 74 pawns and the passing one has 12, so the *condition* may be
absent rather than the *defect* fixed. See the 2026-08-21 run above.

### The 8 skipped assertions are the world, not the code

All eight are the animal suite reporting honestly that a bare `-quicktest` map has no prisoner,
no slave, no caravan, no bonded pair and no pregnant animal to test against. **The animal suite
is only fully meaningful on a real colony** — worth re-running the full suite on the `Fenhana`
save, which would also exercise the migration path again.

## Stage 0 acceptance gate

**All six criteria are closed as of 2026-08-21.** Criterion 6 was the last, and the dev test bridge
closed it without a human at the keyboard.

| # | Criterion | Status |
|---|---|---|
| 1 | Clean build passes | **PASS** — 0 errors, 0 warnings |
| 2 | 1.0 status ledger exists | **PASS** — this file |
| 3 | Baseline market diagnostics exist | **PASS** — captured to `docs/MARKET_BASELINE_0_9_3.md` |
| 4 | Timeline has an owner and bounded retention | **PASS** — `IntercolonyWorldComponent`, 1,000 records |
| 5 | Prior 0.9.3 save loads after the schema change | **PASS** — see below |
| 6 | No existing self-test regressed | **PASS** — 17/17 suites, 847 passed, 0 failed (2026-08-21) |

### Criterion 5 — the migration ran for real, and this is the first time

A 21.5 MB schema-42 colony save (`Fenhana` / `Intercolony 0.9.3 preflight`, economy seed
`-1586549745`, refresh 432, `nextId 6826`) was loaded on the schema-43 build. The log:

```
[Intercolony] State loaded (schema 42, nextId 6826).
[Intercolony] Migrating state from schema 42 to 43.
[Intercolony]   schema 42 -> 43: commercial timeline record spine added;
                history starts recording at tick 6473557.
```

Zero exceptions in the session and no Intercolony warnings or dropped records.

**This matters beyond Stage 0.** `CLAUDE.md` records that none of the three earlier migrations
had ever run in the real load order — only in isolated throwaway installs — and names it the
top item in `docs/PENDING_PLAYTESTS.md`. This run went through the ordinary path: real save,
real load order, real mod list. `dev.ps1 run -MainMenu` was added so it stays repeatable;
`-quicktest` cannot do it, because a new world initializes at the current schema and never
enters the migration at all.

On criterion 6, the one plausible regression from 0.3b was ruled out by inspection rather
than left to chance: the new write sites consume entity IDs, so any self-test asserting on a
specific ID would break. Grepping every assertion in `Debug/` found only the timeline
suite's own literals, which it constructs itself. Nothing else depends on ID sequencing.

**Stage 1 is unblocked.** The baseline is captured to `docs/MARKET_BASELINE_0_9_3.md`, taken on
a persistent save rather than a throwaway world, so the same economy can be measured again after
Stage 2 instead of a merely similar one.

The capture exposed two flaws in the diagnostic itself, both since fixed, both worth a rerun to
fold into the record:

- **It reported generator appetite as though it were market size.** `MaxLiveOpportunities` is a
  global flat ceiling on live offers; a real refresh in the log created 13 while the sample
  projected ~200 per cycle. The report now prints the ceiling and says plainly that the market
  is ceiling-bound, not generator-bound. Appetite is still the right measurement — the ceiling
  would mask a dead generator until it fell below the cap — but reporting it unqualified would
  have misled every later comparison.
- **The probe basket was junk.** Alphabetically-first-per-category selected ancient ruins
  scenery. It now ranks by observed demand from the same sample.

## Stage 1 acceptance gate

| # | Criterion | Status |
|---|---|---|
| 1 | Same seed + settlement yields the same baseline across save/load | **PASS by construction** — generation unchanged; profiles are still regenerated, never persisted |
| 2 | Baseline no longer depends on the refresh count | **PASS** — the cycle roll is gone; asserted as purity in the market suite |
| 3 | Same-archetype settlements still differ modestly | **PASS by construction** — `SettlementProfileGenerator`'s jitter is untouched |
| 4 | Different archetypes produce visibly different tendencies | **PASS by construction** — archetype weight tables untouched |
| 5 | Modded/undefined tech faction handling stays safe | **PASS by construction** — no tech-tier code was touched |
| 6 | Existing consumers compile; adapters marked for deletion | **PASS** — renamed at all eight call sites, no adapters were needed |
| 7 | Player can identify a settlement's economy without debug numbers | **PASS** — needs a look in play to confirm it reads well |

"PASS by construction" meant the slice did not touch that path and the existing profile suite
already covers it — not that it was re-observed. **That open item is now closed:** on 2026-08-21 the
profile suite ran 154 passed / 0 failed and the market suite 81 / 0, so criteria 1–5 are observed
rather than argued.

**Criterion 7 is the one that a self-test cannot settle.** Whether a settlement's economy reads
clearly from the Market and Relations tooltips is a judgement about text, and it needs eyes on it in
play. It is not a code gap and it does not block Stage 2 — logged in `docs/PENDING_PLAYTESTS.md`.

## Slice log

### 2G — completed trades nudge local pressure (2026-08-21)

**Claim:** the player's concluded trades move a settlement's economy in proportion to total value,
and splitting a trade cannot multiply the effect.
**Files:** `Economy/MarketPressureService.cs`, `Orders/SalesOrderService.cs`,
`Procurement/PurchaseOrderService.cs`, `Debug/IntercolonyEconomySelfTest.cs`, plus flakiness repairs
in `Debug/IntercolonyMarketSelfTest.cs` and `Debug/IntercolonyRfqSelfTest.cs`.
**Commit:** `94f512b`. **Schema:** unchanged at 44 — pressure was already persisted by 2A.
**Tests:** full suite green on **four consecutive fresh worlds** (935–937 passed, 0 failed, 13–14
skipped), world-pawn delta 0, both leak guards OK, log clean. Economy suite 91 → 97.

**This is the first slice that writes pressure from gameplay.** Until now only a debug action moved
it. Both writes sit at the existing exactly-once completion boundaries, so a settlement's economy
cannot move for a trade that did not conclude — the same reasoning that put the timeline record
there in 0.3b.

**The magnitude formula is the slice, and the obvious version is a live exploit.** Pressure moves
multiplicatively in its *offset*:

```
bound + (current - bound) * exp(-value / NudgeValueScale)
```

The natural implementation — a diminishing per-trade delta such as `MaxNudge * v / (v + K)` — is
**subadditive**: any concave `f` with `f(0) = 0` satisfies `f(a) + f(b) > f(a+b)`, so ten small
trades move pressure *further* than one large one of the same total. That is precisely the
split-to-multiply lever §2.5 forbids and acceptance criterion 10 tests for. **Measured, not
argued:** with the naive shape temporarily in place, ten 1,000-silver trades reached 0.855 where one
10,000-silver trade reached 0.875 — a 16% larger effect for identical value. The exponential
composes exactly (`exp(-a/K)·exp(-b/K) = exp(-(a+b)/K)`), so splitting gains **nothing at all**,
not merely less. It is also the same shape as 2B's mean reversion, aimed at a bound rather than at
neutral.

**Two `Complete` methods became `internal`** so the suite drives the real transition, following the
`ContractService.BuildOffer` precedent already in the codebase. The first version used reflection;
that keeps compiling after a rename while silently testing nothing.

### The flakiness that this slice exposed, and the reasoning error behind it

**Two assertions committed earlier the same day were flaky, and both were mine.** Same production
code, one fresh world green and the next red:

- **2C lot size** sampled one settlement in one cycle. `PickQuantity` clamps crated goods to
  `MaxCratedLotSize` and single-stack goods to `MaxSingleStackLotSize`, so a cycle that drew only
  those had lots pinned *at their cap* and scaling changed nothing — seen as `8 -> 8`, `9 -> 9`,
  `16 -> 16`. Now aggregates twelve settlements; totals run 480–564, far outside the clamp regime.
- **2F RFQ counts** skipped below eight *settlements* while comparing *quotations*. A 21-settlement
  world returned two of them, and a strict `<` cannot reliably move a total of two. **The honesty
  guard measured a proxy rather than the quantity under test, so it never fired.** Now aggregates
  twelve defs and guards on the undisturbed quotation total; totals run 76–86.

**The reasoning error is worth more than either fix. Mutation proves *sensitivity*, not
*stability*.** Both assertions were mutation-tested when written and both went red, and that was
treated as sufficient. It shows only that the test notices when the code is wrong; it says nothing
about whether it passes reliably when the code is right. Those are separate properties and both
need evidence. The standard from here:

1. **Sensitive** — revert the production change, confirm red, restore.
2. **Stable** — run unmutated on **at least four fresh worlds**, all green.

And a skip guard must measure the same quantity the assertion compares. When an assertion is
statistical, enlarge the sample rather than loosening `<` to `<=` — loosening makes it pass with the
feature deleted, which is how a flaky test usually gets "fixed". Both repairs here were re-verified
in both directions: green on four worlds, red on two under mutation.

### 2C — market opportunity size reads current demand (2026-08-21)

**Claim:** a settlement under demand pressure asks for larger lots and one with a glut asks for
smaller ones, so a shortage changes the opportunities themselves and not only their prices.
**Files:** `Market/MarketOpportunityGenerator.cs`, `Core/SettlementEconomicProfile.cs`,
`Debug/IntercolonyMarketSelfTest.cs`.
**Commit:** `8d69f99`. **Schema:** unchanged at 44.
**Tests:** full suite 930 passed / 0 failed / 13 skipped, world-pawn delta 0 (16 → 16), both leak
guards OK, log clean. Market suite 81 → 84.

**2C turned out to be an addition, not the deletion its name implies.** The `0.55–1.45` roll the
plan writes against was already gone — Stage 1.2 removed it, which was the whole point of the
`BaseDemandFor` rename — so the two stacked dynamic systems §2.3 warns about never existed. What the
audit actually found was the opposite problem: `PickQuantity` opened with `Rand.Range(400f, 3000f)`,
a 7.5x multiplier re-rolled every cycle that read no demand at all. That is §2.3's "large random
multiplier masquerading as demand state", and §2.8's step 5 requires size to come from the effective
economic context. **2E migrated price and left size behind**, so acceptance criterion 4 — a forced
shortage changes selling prices *and opportunities* — was only half met and nothing said so.

**Size scales by the demand condition, not by effective demand, and the distinction is the slice.**
`PickCategory` already weights categories by effective demand a few lines earlier, so the
settlement's baseline appetite is counted in *which* category got picked. Multiplying size by the
full effective value would count that standing appetite a second time. The condition is only the
"right now" part. This is §2.10's no-double-counting rule applied to quantity instead of price —
worth noting that the rule generalises, because the plan only ever states it about pricing.

**The assertion is deterministic, not statistical, and that was a design choice.** Shocking all six
categories *equally* leaves category selection untouched: `PickCategory` draws `Rand.Range(0f, total)`
and compares against running sums, so scaling every weight by the same factor scales the draw and the
thresholds together and the same category, def and rolls come out. `targetSilver` is then the only
thing that moves. Shocking one category would have changed which good was picked and made the
comparison meaningless.

**Verified by mutation.** Replacing the multiplication with `* 1f` turns both directional assertions
red at `8 -> 8`; restoring it turns them green. The third assertion — the upper bound — stays green
under the mutation, which is correct and worth understanding rather than "fixing": a bound is
trivially satisfied when nothing moves, so it is not the assertion that detects the deletion.

**`volatility` keeps its field and loses its docstring's claim.** See the CORRECTION below. It is
live — `SettlementProfileGenerator.FillWeights` reads it to jitter both weight arrays at generation
time — but its comment still described a per-refresh swing that Stage 1.2 removed. A field whose
documented meaning outlived its behaviour is the same failure this project keeps meeting in the
sentinel bugs, one level up: the value was fine and the story about it was false.

### 2F — RFQs quote against effective supply (2026-08-21)

**Claim:** a settlement whose category is currently scarce answers fewer RFQs, and one with a
surplus answers more, through the same authoritative API selling uses.
**Files:** `Procurement/RfqService.cs`, `Debug/IntercolonyRfqSelfTest.cs`.
**Commit:** `bfe9457`. **Schema:** unchanged at 44.
**Tests:** full suite 927 passed / 0 failed / 13 skipped, world-pawn delta 0 (12 → 12), both leak
guards OK, log clean. RFQ suite 75 → 82.

**The production change is three lines and the direction is structural, not tuned.** Both gates
`supply` feeds — the `0.35` response floor and `Clamp01(BaseResponseChance * supply - distancePenalty)`
— are monotonic in it, so scarcity can only reduce answers. The call site inverts nothing itself;
supply pressure counts toward *scarce* and a supply weight counts toward *able to sell*, and that
reconciliation lives in `EffectiveSupply` alone.

**Finite offer consumption is untouched, per §2.9.** Pressure is broad current scarcity;
consumption is "you already bought 80 of the 100 units this supplier offered in this window". They
answer different questions and folding either into the other loses one of them.

**The first version of the test did not test the change, and this is the third time today.** All six
of its assertions called `EffectiveEconomyService.EffectiveSupply` directly and none called
`RfqService`, so the whole method would have passed with the production line reverted — the economy
suite's assertions relocated into the rfq suite. §20.2 exists for exactly this. The added assertion
goes through `GenerateResponses`, which is `internal` precisely so a diagnostic can quote a
throwaway request and which pushes a fixed seed, so the same request in the same refresh window is
deterministic and pressure is the only variable.

**It was verified by mutation, not by watching it pass.** Reverting `RfqService.cs:244` to
`BaseSupplyFor` turns the assertion red — `28 settlements, 6 -> 6 quotations` — and restoring it
turns it green. A test that has never been seen failing is not known to test anything; this one has.

**It skips rather than passes when the world is too small.** Below 8 accessible settlements the
count legitimately might not move, and a pass there would be a pass for the wrong reason. The rfq
suite already had a `SKIPPED` channel and it reuses it.

**The pattern behind all three misses is in how the work was specified, not who did it.** Each
prompt said *what to assert* without saying *what the assertion must be able to fail on*. A
zero-baseline multiplication, and twice a formula asserted beside the production owner rather than
through it. Specify the failure mode, not just the claim.

### 2D/2E — selling and pricing read effective demand (2026-08-21)

**Claim:** a shortage in a settlement widens the opportunities it generates, the appetite it offers
and the price it pays, because all three now ask the effective-economy API instead of reading the
settlement's standing identity.
**Files:** `Market/MarketOpportunityGenerator.cs`, `Market/FindBuyerService.cs`,
`Market/IntercolonyPricing.cs`, `Core/SettlementEconomicProfile.cs`, plus state threaded through
`Contracts/ContractService.cs`, `Orders/SalesOrderService.cs`, `UI/MainTabWindow_Intercolony.cs`,
`UI/Dialog_CreateRequest.cs` and four self-tests.
**Commit:** `4e4b31b`. **Schema:** unchanged at 44.
**Tests:** full suite 920 passed / 0 failed / 13 skipped, world-pawn delta 0 (14 → 14), both leak
guards OK, log clean. Economy suite 87 → 91.

**Pressure is applied exactly once.** `"Local demand"` is the same single price factor it always
was, now fed the effective value rather than the baseline. No second shortage multiplier was added
anywhere in the chain — that is §2.10's double-counting prohibition, and it is the defect that would
not look wrong at either site. The `0.4–2.0` clamp in pricing is a *price sanity* bound and stays;
it is a different concern from the API's condition bound.

**`MainTabWindow_Intercolony.cs:1233-1234` deliberately still reads baseline.** That is the Stage 1
economic-identity tooltip. It answers what a settlement *is*, not what it is currently going
through, and moving it onto effective demand would make a settlement's advertised character change
every time its market moved — undoing the separation Stage 1 exists to establish.

**A latent defect became live and was fixed at its source.** `SettlementEconomicProfile.settlementId`
had no initializer, so it defaulted to `0`. That was harmless until this slice, because nothing
looked pressure up by it — and pressure is looked up by the id *on the profile*. Synthetic profiles
built for a generic estimate never set the field, and `Dialog_CreateRequest.CreatePreviewProfile`
is one, feeding the procurement dialog's "Market estimate". Settlement ID `0` is a plausible real
`WorldObject.ID`, so a generic preview would have priced itself against whatever settlement zero
happened to be going through: a wrong number, shown to the player, attributable to nothing. The
field now defaults to `-1`, which `MarketStateFor` already treats as no settlement. Fixed at the
field rather than the call site, which also covers four synthetic profiles in the self-tests.

**The regression test for it had to be rewritten before it meant anything.** The obvious version —
construct a profile, assert its effective demand equals its baseline — cannot fail: a default
profile's `demandWeights` are zero, and zero times any pressure is still zero. It now sets a
non-zero weight and pairs two profiles with identical weights differing *only* in whether they name
a settlement, one moving under the shock and one not. Without that pairing the assertion cannot
distinguish "the sentinel works" from "pressure is not being read at all". §20.1 again, in a new
disguise: this one was not a skip, it was a check that could only ever pass.

**Suite counts wobble with the world and that is not a regression.** Across three runs today the
animal suite read 58/11, 57/12 and 58/11, and rfq 74 then 75, with zero failures throughout. Those
suites size themselves to what the loaded `-quicktest` world contains. Compare failures and the
leak guards across runs; do not compare raw totals.

### 2.2 — one authoritative effective-economy API (2026-08-21)

**Claim:** every market system can ask one owner what a settlement wants and can supply right now,
and get baseline identity composed with current pressure, bounded, without reading either directly.
**Files:** `Economy/EffectiveEconomyService.cs` (new), `Debug/IntercolonyEconomySelfTest.cs`.
**Commit:** `0823d49`. **Schema:** unchanged at 44 — a read model persists nothing.
**Tests:** economy suite 46 → 87 assertions. Full suite 914 passed / 0 failed / 14 skipped,
world-pawn delta 0 (18 → 18), both leak guards OK, log clean.

**It has no slice letter, and that is the point.** The ledger jumped 2B → 2C, but §2.3 only permits
deleting the old `0.55–1.45` roll *once all consumers use the new API*, so §2.2 has to exist and be
adopted first. Order is 2.2 → 2D–2F → 2C.

**Supply pressure and supply weight count in opposite directions, and this is where they are
reconciled.** Pressure counts upward toward *scarce*; `BaseSupplyFor` counts upward toward *able to
sell*. So effective supply divides where effective demand multiplies. Multiplying is the natural
mistake, it looks correct at the call site, and each of the five consumers would have made it
independently — which is most of the argument for a single owner rather than a helper.

**Reads are free of consequence, and two separate defects are guarded.** Nothing in the service
creates, stamps or advances a record. A read using `createIfMissing: true` would put one neutral
entry per settlement into the save on the first UI hover, undoing 2A's sparseness; a read that
advanced reversion would make a shortage decay faster the more often the player looked at it.
Asserted directly: ten reads move neither the value nor `lastAdvancedRefresh`.

**The bound is on the *condition*, not on effective demand.** Clamping the composed result would
clip archetype differences — a military settlement's appetite for weapons is meant to be visibly
larger — so what is bounded is only how far the dynamic layers may move that identity.
`MaxCondition` is 2.0 against pressure's own 1.60, so today it is headroom and the strongest
possible shock arrives unclipped (asserted). It begins to bind in Stage 3, where an event modifier
multiplies a settlement that is already under pressure; two layers each individually restrained
still multiply to an unrestrained number. Floor is the exact inverse, same discipline as 2B.

**Explanations reuse `PriceFactor` and multiply to exactly the value they explain**, per §2.11's
instruction to use the existing explanation system rather than build a second one. The assertion
that the factors' product equals the effective value is the guard against §2.10's double counting:
that defect arrives as a caller multiplying an effective value that already contains pressure by a
factor list that contains it again, and it looks correct at both sites. A neutral condition
contributes no line — a row reading `x1.00` buries the row that matters.

**Nothing consumes it yet.** That is deliberate and is the same safe state 2B left: pressure moves,
and after this it can be *read* correctly, but no player-facing system has been repointed. 2D–2F do
that.

**One skip count moved and is not explained.** The full run skips 14 where 2B's skipped 13 — animal
11 → 12 with its passes 58 → 57. Nothing in this slice touches the animal path, and that suite's
skips depend on what the loaded world happens to contain, so this is most likely the world rather
than the code. Recorded rather than assumed.

### 2B — advance and mean-revert pressure (2026-08-21)

**Claim:** a shock to a settlement's economy decays back toward neutral over market cycles, bounded
at both ends, driven by elapsed cycles rather than by how often something reads the record.
**Files:** `Economy/MarketPressureService.cs` (new), `Debug/IntercolonyEconomySelfTest.cs`,
`Core/IntercolonyWorldComponent.cs`.
**Commit:** `1da331a`. **Schema:** unchanged at 44 — 2A already persisted `lastAdvancedRefresh`.
**Tests:** economy suite 19 → 46 assertions, all passing. Full suite 874/0 afterwards, world-pawn
delta 0, both leak guards OK, no exceptions.

**Reversion is closed-form over elapsed refreshes, never stepped.** `lastAdvancedRefresh` was
persisted in 2A for exactly this: a save reopened many cycles later must land where a running game
would have put it. Stepping there would be a loop whose length is how long the save sat on disk.
Asserted directly — five single-cycle advances land on the same float as one five-cycle advance
(1.18537 both ways).

**`NeverAdvanced` is compared exactly and never used as arithmetic — the fifth sentinel.**
Subtracting from `-1` computes an elapsed span of `toRefresh + 1`, which would erase a fresh shock a
cycle early and look like a balance problem. A never-advanced record is stamped and decays from
there; `ApplyShock` stamps at shock time rather than leaving it to the next advance, so a shock does
not get a free cycle at full strength.

**The floor is the exact multiplicative inverse of the ceiling** (0.625 against 1.60), so a glut is
precisely as strong as the equivalent shortage. As a literal it invited an asymmetry that nothing in
the file would have explained. Asserted as `min × max == 1`.

**`AdvanceAll` runs before the neutral prune in `DoRefresh`**, so a record that settles on a cycle is
dropped on that cycle rather than surviving one refresh past the point it meant anything.

**The coefficient is deliberately not asserted.** `ReversionRetention` is 0.82, matching the plan's
illustrative 1.40 → 1.33 → 1.27 curve; the suite asserts direction, monotonicity, no overshoot from
either side, and that the strongest possible shock reaches the prune epsilon (25 refreshes) without
being over before the player can trade on it. The plan calls the coefficient balance tuning and it
is expected to move in play.

**Not in this slice, and deliberately:** the §2.12 diagnostic — force a shock, advance N refreshes,
print baseline vs pressure vs effective — belongs with 2J. Nothing reads pressure yet; 2D–2F are
what make it visible.

### 2A — persisted market pressure (2026-08-20)

**Claim:** a settlement's economy can hold a non-neutral condition that survives save and load,
and an undisturbed world persists nothing.
**Files:** `Economy/SettlementMarketState.cs` (new), `Debug/IntercolonyEconomySelfTest.cs` (new),
`Core/IntercolonyWorldComponent.cs`, `Debug/IntercolonyDebugActions.cs`.
**Commit:** `d49fd86`. **Schema:** 43 → 44, migration writes nothing.
**Verified in game:** `State initialized fresh (schema 44)`, no exceptions.
**Tests:** `Run economy self-test` — sparse defaults, create-on-demand, prune keeps disturbed and
drops settled, Scribe round trip, short-save padding. **Executed 2026-08-21: 19 passed, 0 failed.**

**Sparse, not one record per settlement.** The baseline was captured on a 358-settlement world;
a record each would put thousands of floats in every save to say nothing is happening. Records
appear on first disturbance and are dropped when they settle. "Settled" is an epsilon rather
than equality because mean reversion approaches 1.0 asymptotically — with exact comparison a
record would never become prunable and the save would only ever grow.

**Scribe has no array overload.** `Scribe_Collections.Look` takes List, HashSet, Stack, Queue and
Dictionary; verified against `reference/decompiled`. Arrays cross the boundary as lists and stay
arrays in memory, since pressure is read by category index on paths Stage 2 will make hot. A
short or missing list loads padded with **neutral, not zero** — zero would read as no demand at
all, a shortage nobody caused.

### 1.2 / 1.4 / 1.5 — settlement economies (2026-08-20)

**Claim:** a settlement's economic identity is stable, independent of the market clock, and
visible to the player without debug output.
**Files:** `Core/SettlementEconomicProfile.cs`, `UI/MainTabWindow_Intercolony.cs`,
`Market/FindBuyerService.cs`, `Market/IntercolonyPricing.cs`,
`Market/MarketOpportunityGenerator.cs`, `Procurement/RfqService.cs`, three self-tests.
**Commits:** `12c8972`, `5711ee0`. **Schema:** unchanged at 43 — the profile is not persisted.

`DemandFor`/`SupplyFor` became `BaseDemandFor`/`BaseSupplyFor` at all eight call sites. The
rename is the point, not tidiness: Stage 2 puts an effective-economy layer over these, and a
caller that says "demand" when it means "identity" is how the two get conflated.

**The affinity band is 0.15 and the number is constrained, not chosen.** It has to straddle
`FindBuyerService.InterestThreshold` (0.9) against category weights clustering at 1.0. The
plan's illustrative ±0.08 would put every good in a wanted category above the threshold, making
"No current interest" dead code — the exact flattening that threshold's own comment says it
exists to prevent, and which the market self-test asserts against. Caught by reading the
threshold before picking the number rather than by the test failing afterwards.

**Expected interim effect:** Find Buyer will feel more uniform within a category until Stage 2
adds pressure back. That is the shape of the change, not a regression, and it is worth
remembering when 1.2's results are first seen in play.

**UI placement — the plan's open A/B choice.** Chose *both* tooltips over a new screen:
Relations alone would not do, because it only lists settlements already traded with and the
question is asked before the first trade. The summary is on the Market listing tooltip, where a
buyer is actually chosen, and on the Relations row. One helper, two call sites.

### 0.2 — market baseline (2026-08-20)

**Claim:** the 0.9.3 market's behaviour can be measured, reproducibly, without altering it.
**Files:** `Debug/IntercolonyMarketBaseline.cs` (new), `Debug/IntercolonyTimelineGuard.cs`
(new), `Debug/IntercolonyDebugActions.cs`, `Procurement/RfqService.cs`.
**Commit:** `fe011b7`. **Schema:** unchanged at 43.
**Tests:** the diagnostic resamples the same cycles and compares, because a figure that moves
between two runs of one seed is not evidence. Not yet executed in game.

Measures the production owners — `MarketOpportunityGenerator.GenerateFor`,
`RfqService.GenerateResponses`, `IntercolonyPricing.BaseValue` — rather than reimplementing
them, so a change to any of them surfaces here instead of hiding behind a parallel copy of
the arithmetic. Offers are generated against synthetic cycle numbers past the world's own, so
a sample can never be confused with the live market. Probe goods come from the loaded defs by
category, not a hardcoded vanilla list, so the baseline still means something under mods.

**The market suite covering this ran clean on 2026-08-21** (81 passed, 0 failed). The baseline
*diagnostic* is a debug action rather than a suite, so it is still only as re-verified as its last
manual capture.

**Known limitation:** the procurement half can only measure the current refresh window. Quote
seeding reads `state.RefreshCount`, and advancing it would mean running real refreshes on the
player's world. The report says so rather than implying an average.

**It also fixed a consequence of 0.3b, found by audit rather than by failing.** Self-tests
drive the real transitions on purpose, and those now record — so the order, RFQ and
combat-clause suites were each about to write dozens of rows into the player's trading history
for settlements that do not exist. `IntercolonyTimelineGuard` restores the list contents
around a self-test run. Contents, not length: pruning removes from the front, so restoring by
count leaves synthetic records where the real ones were. It is deliberately not a suppression
flag on the recording service — a global "stop recording" bit that leaked would silently lose
real history, which is far worse than a debug list needing cleanup. It is applied to
self-tests only; debug actions that genuinely advance the world keep their records, because
those events really happened.

### 0.3b — timeline write sites (2026-08-20)

**Claim:** every terminal commercial transition in production code writes exactly one
timeline record, at the point the status actually changes.
**Files:** `Orders/SalesOrderService.cs`, `Procurement/PurchaseOrderService.cs`,
`Contracts/ContractService.cs`, `Core/HostilityPolicy.cs`,
`Core/CommercialEventRecord.cs`, `Debug/IntercolonyTimelineSelfTest.cs`.
**Schema:** unchanged at 43. Scribe writes enums by name, so the three added
`CommercialEventType` values need no migration.
**Tests:** `CheckWriteSites` drives the real transitions — `SalesOrderService.Fail`,
`.Cancel`, `PurchaseOrderService.Cancel`, and both `HostilityPolicy` war paths — and asserts
the record appears. It deliberately does not call the timeline service itself: a test that
recorded its own events would pass with every write site deleted. **Executed 2026-08-21 as part of
the timeline suite: 47 passed, 0 failed.**

**Every write goes where the status assignment already is**, so a record cannot exist for an
event that did not happen. `SalesOrderService.Complete` already documented itself as the
exactly-once boundary for both delivery and collection; the others (`Fail`, `Cancel`,
`Refund`) are each documented as the only path to their status.

**Three enum values were added, and the reason is a correctness one.** The plan's initial
list has no `SaleCancelled`, `PurchaseFailed` or `ContractCancelled`, but all three
transitions exist in the code today and the existing values misstate them. `HostilityPolicy`
tells the player in as many words that a war-voided order "does not count against you as a
supplier" — recording it as `SaleFailed` would contradict the letter the player just read.
A supplier defaulting is not the player cancelling. The enum now covers
completed/failed/cancelled in both directions.

**Two transitions are deliberately not recorded, and say so at the site.** War *suspension*
of a supply agreement and the in-flight cycle it withdraws: suspension has no event type
yet, so recording only its side effect would leave a cancelled order in the player's history
with nothing to explain it, and the cycle is re-issued on resume anyway. And contract
*refusal*: no agreement began, and the other two decline paths are a button click and an
offer lapsing. Both belong with Stage 5, which owns proposal and negotiation outcomes.

**A near miss worth remembering.** `RecurringContract.TryAccept` has two production callers,
not one — the incoming-offer path and the player-proposal path added in 0.9.1. Wiring only
`AcceptOffer` would have silently dropped every agreement the player initiated. Grepping the
callers of the funnel, rather than trusting the first one found, is what caught it.

### 0.3 — commercial timeline spine (2026-08-20)

**Claim:** world state owns a persisted, bounded commercial timeline record list (`CommercialEventRecord`) capped at 1,000 entries, retaining event facts that cannot be reconstructed later, without wiring write sites yet.
**Files:**
- `Source/Intercolony/Core/CommercialEventRecord.cs` (new)
- `Source/Intercolony/Core/CommercialTimelineService.cs` (new)
- `Source/Intercolony/Debug/IntercolonyTimelineSelfTest.cs` (new)
- `Source/Intercolony/Core/IntercolonyWorldComponent.cs`
- `Source/Intercolony/Debug/IntercolonyDebugActions.cs`
**Schema:** bumped to 43 (additive migration step in `MigrateIfNeeded`).
**Commit:** `04be001`.
**Tests:** `IntercolonyTimelineSelfTest` asserts event types, monotonic IDs,
settlement/recency queries, retention pruning verified by record identity rather than by
count, survival of a record whose `ThingDef` no longer resolves, and a Scribe round-trip.
**Executed 2026-08-21 through the dev test bridge: 47 passed, 0 failed.** Until then the only thing
verified was a clean build at 0 errors and 0 warnings.

**One destructive defect was caught in review before it could ship.** The first version of
the self-test snapshotted the record *count* and restored by trimming the tail, but `Prune`
removes from the front — so real records were pruned away and synthetic `bulk-N` test
records were left in their place, then persisted into the save. Harmless while nothing
writes to the timeline; actively destructive from the next slice onward. It now snapshots
and restores the list contents. **A debug tool that mutates authoritative state must restore
what it saw, not how much it saw.**

### 0.1 — status ledger (2026-08-19)

**Claim:** the program has a written continuity record that survives between sessions.
**Files:** `docs/1_0_IMPLEMENTATION_STATUS.md` (new).
**Schema:** unchanged.
**Tests:** none applicable.

## Decisions / deviations

### 2026-08-21 — Stage 2C — CORRECTION

An earlier entry in this file claimed `SettlementEconomicProfile.volatility` was "written and
never read outside its own debug line", and proposed deleting it. **That was wrong**, and the
error is worth naming because it nearly deleted live behaviour: the grep that produced it
explicitly excluded `SettlementProfileGenerator.cs`, which is the one file that reads the field.
`FillWeights` passes it to `Jitter` for both weight arrays, so `volatility` is what makes two
settlements of the same archetype differ from each other. Deleting it would have changed every
profile in every world.

What is actually wrong with it is only its doc comment, which still claims it controls how much
"prices and opportunities swing between refreshes" — untrue since Stage 1.2 removed the per-refresh
swing. The field is kept and the comment corrected. **A grep with an exclusion in it is not an
audit**; the exclusion was there to reduce noise and it removed the only evidence that mattered.

### 2026-08-21 — Stage 2C — DECISION

**Question:** `ContractService.ContractQuantity` sizes a standing-agreement offer from
`Rand.Range(1500f, 5000f)`, seeded on `RefreshCount` and reading no demand at all. Should 2C make
it demand-aware, as 2C is doing for market opportunity size?
**Evidence:** §2.8's step list explicitly names opportunity sizing, and §2.3 targets "a large
random multiplier masquerading as demand state". Contract *terms* are already demand-aware —
`CalculateContractTerms` prices through `IntercolonyPricing`, which reads effective demand as of
2E — and its seed deliberately excludes `RefreshCount` so accepted terms are durable across a
reload. So the only part that ignores the economy is the size of the initial offer.
**Choice:** leave it, and revisit at the 2K play gate.
**Why it preserves this plan:** contract offer size is balance, and §18 says balance is tuned at
the stage's play gate rather than guessed at during implementation. Changing it now would alter
what the player is offered with no evidence about whether current sizes are already right, and a
contract is a much larger commitment than a spot lot — getting its size wrong is more costly than
getting an opportunity's wrong.
**Revisit if:** the 2K play gate shows contract offers feel disconnected from a settlement's
visible economy, which is exactly the symptom this would cause.

### 2026-08-19 — Stage 0 — DECISION

**Question:** where do the 1.0 program's commits land, given Stage 0 bumps the save schema
and `main` will not be shippable again until Stage 8?
**Evidence:** 0.9.3 is live on Steam Workshop `3780094556` and public on GitHub. A beta
defect report is likely during a program this long, and `docs/BACKLOG.md` already holds
open items. Every prior phase committed straight to `main`.
**Choice:** a `1.0` feature branch. `main` stays at 0.9.3 and remains directly releasable
as a 0.9.4 point release without untangling half-built stage work.
**Why it preserves this plan:** nothing in the plan depends on branch layout; this only
protects the shipped release from an in-flight schema bump.
**Revisit if:** the program reaches Stage 8 with no intervening point release, in which
case the branch was merely bookkeeping and the merge is trivial either way.

### 2026-08-20 — Stage 0.3 — DECISION

**Question:** the plan's sketch of `CommercialEventRecord` gives `float silverAmount`.
Should the record follow it?
**Evidence:** every other money field in the codebase is an `int` — `LedgerEntry.amount`,
`SalesOrder.paidSilver`. RimWorld silver is a stack count, not a continuous quantity.
**Choice:** `int`. The plan's field list is a conceptual sketch, and §0 says implementation
details may adapt to current code while player-facing semantics stay authoritative. A money
type is an implementation detail; a float would invite rounding drift against the ledger it
will be displayed beside.
**Why it preserves this plan:** nothing player-facing changes.
**Revisit if:** a 1.0 feature ever needs fractional silver, which none currently does.

### 2026-08-19 — Stage 0 — VERIFICATION

The plan's §4 architectural claims were checked against the code before any work began,
because the document was written by an audit rather than from this repository's history.
All of them hold:

- `SettlementEconomicProfile` is non-`IExposable` and regenerated from economy seed plus
  settlement ID (`Core/SettlementEconomicProfile.cs:48`).
- §4.3's cycle-noise claim is exact: `DemandFor(def, category)` rolls
  `Rand.Range(0.55f, 1.45f)` seeded on `RefreshCount`, smoothed over three cycles
  (`Core/SettlementEconomicProfile.cs:102`). This is what Stage 2 replaces.
- Schema is 42 with a one-step-per-version migration chain (`CurrentSaveVersion`,
  `MigrateIfNeeded`).
- `SupplierOfferConsumption` and `CommercialHistoryEntry` are both owned by
  `IntercolonyWorldComponent`.
- Every file path named in §4 and in the per-stage "likely code areas" resolves.

No correction to the plan is needed.

## The bridge CAN prove a migration — 43 → 44 ran on a real save, unattended (2026-08-21)

**This corrects a claim in `CLAUDE.md`, `docs/DEV_TEST_BRIDGE.md` and the session seed.** All three
say the bridge cannot prove a migration because it launches `-quicktest`, which creates a new world
that initializes at the current schema and never enters the migration path. That is true of
`-quicktest` and **false in general**, and it had the standing 43 → 44 item blocked on a human for
the whole program.

RimWorld has a stock dev-mode feature: with `Prefs.DevMode` on, a save named exactly `autostart`
is loaded automatically at boot by `Root_Entry.Start()` through the real `GameDataSaveLoader.LoadGame`
(`reference/decompiled/Verse/Root_Entry.cs:18-23`, `SaveGameFilesUtility.cs:42-49`). A copy of the
real 22.5 MB `Fenhana` colony save was placed as `Autostart.rws` and a bridge-enabled game launched:

```
[Intercolony] State loaded (schema 42, nextId 6826).
[Intercolony] Migrating state from schema 42 to 44.
[Intercolony]   schema 42 -> 43: commercial timeline record spine added; history starts recording at tick 6473557.
[Intercolony]   schema 43 -> 44: market pressure added; every settlement starts undisturbed.
```

**Zero exceptions.** The bridge then answered `worldLoaded: true, mapIsPlayerHome: true,
tick: 6474021, saveSchema: 44`. **The 43 → 44 step has now run in the real load order on a real
colony**, which is the thing 2A left open.

**The trap, which cost one crashed launch: autostart must not be combined with `-quicktest`.** Both
`Root_Entry.Start()` and `Root_Play.Start()` consume the same one-shot static
`Root.checkedAutostartSaveFile`. With `-quicktest`, Entry takes the autostart and sets the flag, then
Play's `else` branch finds `Current.Game != null`, skips `SetupForQuickTestPlay()` and calls
`InitNewGame()` on the already-loaded game — `NullReferenceException` at `Find.get_WorldObjects`. The
autostart launch passes **no** arguments at all.

**Only ever autostart a copy, and delete it afterward.** The source save is never opened for writing
and nothing is saved, so the real colony is untouched — verified by size and mtime after the run. A
leftover `Autostart.rws` silently hijacks every later launch **including `-Fresh`**, which would go
on claiming an isolation it no longer has.

### The full suite on a real colony — 944/0/9, and four skips converted

Run against the migrated colony rather than a bare test map: **17/17 suites, 944 passed, 0 failed,
9 skipped**, world-pawn delta 0 on a **74-pawn** world, both leak guards OK, log clean.

Skips fell from 13 to 9 because a real colony has what a `-quicktest` map does not: `animal` 8
rather than 11–12, `order` 1 rather than 2, and `combat clause` ran 54 assertions against 43.

**The `job posting` pawn-count anomaly did not reproduce under its original condition.** It was
recorded as "74-pawn world failed, 12-pawn world passes", and every non-reproduction so far had been
on a 12-pawn world — which is why it stayed open. This run is a 74-pawn world and the suite passed
25/0 with a world-pawn delta of **0**. That is the closest reproduction attempt yet and it came back
clean. **Still not an explanation**: the world has moved on since the failure, so a changed condition
is as good a hypothesis as a fixed defect. But it is no longer true that the failing condition has
never been retried.

## Play evidence still required

~~**Run the timeline self-test.**~~ **DONE 2026-08-21** — 47 passed, 0 failed, through the bridge.

~~**Load a real schema-42 save.**~~ **DONE 2026-08-20** (`9a588a2`) — the 42 → 43 step ran in the
real load order on the 21.5 MB `Fenhana` save, zero exceptions, nothing dropped. Repeat it with
`dev.ps1 run -MainMenu`; neither `dev.ps1 -Fresh` nor the bridge can prove a migration, because both
launch `-quicktest`, which creates a new world that initializes at the current schema and never
enters the migration at all.

~~**Still required — the 43 → 44 step has never run on a real save.**~~ **DONE 2026-08-21, and
without a human at the keyboard** — see the autostart section above. The 22.5 MB `Fenhana` colony
migrated 42 → 43 → 44 in the real load order with zero exceptions, and the load-time padding in
`SettlementMarketState.FromSaved` and the index rebuild both ran on that path. The full suite then
passed 944/0/9 against the migrated colony.

**Still required — criterion 7, the Stage 1 UI read.** Whether a settlement's economy is legible
from the Market listing and Relations tooltips, without debug numbers.

`docs/PENDING_PLAYTESTS.md` still holds the 0.9.x backlog, which this program does not clear.

## Next executable slice

**2H — coarse economic chains between categories** (plan §2.6), then `2I` regional diffusion,
`2J` explanations, `2K` the migration and play gate.

**Both halves of Stage 2's core now work.** Every consumer reads effective values, and completed
trades write back into pressure. The only deliberate read holdout is
`UI/MainTabWindow_Intercolony.cs:1233-1234`, the Stage 1 identity tooltip, which answers what a
settlement *is* rather than what it is going through.

**What §2.6 asks for, and what it explicitly does not.** Small directional relationships between the
six existing categories, centralised in one table or service — *not* a bill-of-materials
simulation. The plan's initial links: manufactured-goods demand lifts intermediate-goods demand;
furniture demand lifts commodities and intermediates; capital-equipment demand lifts intermediates;
tight commodity supply tightens intermediates and then, more weakly, furniture and capital
equipment. Coefficients deliberately small. The goal is "a steel shortage has consequences", not a
500-node input-output matrix, and the tests must prove **direction and boundedness** rather than a
supposedly correct coefficient.

**The trap is feedback.** Propagation writes pressure that later propagation reads. Without a rule
about ordering and iteration depth, a chain can amplify itself across refreshes — categories that
lift each other in a cycle diverge to the bounds and stay there, which reads as a permanently broken
economy rather than a bug. Decide propagation depth explicitly, apply it from a snapshot rather than
in place so the order categories are visited in cannot change the result, and assert that repeated
refreshes without a new shock still converge toward neutral. 2B's mean reversion is what keeps the
system stable; propagation must not outrun it.

**Testing note carried forward.** A pressure *write* has no reproducible-seed seam the way the
generator does, so drive the real path and read the record either side. And per 2G: verify any new
assertion in **both** directions — red under mutation, green on four fresh worlds.

**Do not rush Stage 2 to reach procurement.** It is the stage everything after it reads from,
and the plan says so twice.

### Resolved: why 2B was waiting for one round of self-test runs

Six suites had been changed or added across Stages 0–2A and not one had been executed — the position
`CLAUDE.md` describes as how a system ends up believed-working and untested. 2B is the first slice
that makes pressure *move*, so every slice after it would have inherited whatever 2A got wrong about
persistence: a wrong prune epsilon, index rebuild or load-time padding would surface as pressure
that silently resets or records that accumulate forever, both of which look like balance problems
and would be chased in the wrong place.

**All six ran clean on 2026-08-21** and the hold is lifted. Worth keeping the reasoning: the block
was correct, and it cost one bridge call to clear rather than a stage of misattributed debugging.

---

### Superseded: capture the baseline, then Stage 1.1–1.2

The capture is one debug action and it belongs to whoever is at the keyboard; the steps are in
`docs/PENDING_PLAYTESTS.md`. Save its output into `docs/` as the recorded baseline — the point
is to have the numbers on disk before they become unreproducible.

Then Stage 1 proper:

- **1.1** keep profile generation deterministic from economy seed + settlement ID, and do not
  persist it. Nothing to change here yet; it is a constraint on 1.2, not a task.
- **1.2** split baseline demand from changing demand. `DemandFor(def, category)` currently
  mixes stable identity with a rolling `Rand.Range(0.55f, 1.45f)` seeded on `RefreshCount`
  (`Core/SettlementEconomicProfile.cs:102`). Consumers need to be able to ask for baseline
  without silently getting cycle noise. Exact-good variation stays, but as a *stable* affinity
  that does not move every cycle.
- **1.4/1.5** make the identity legible in the Relations surface and keep debug visibility.

1.2 is the slice the baseline exists to protect. Do not start it before the capture.
