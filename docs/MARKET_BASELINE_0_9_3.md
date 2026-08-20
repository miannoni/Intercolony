# 0.9.3 market baseline

What the market did before the 1.0 program changed it. Captured 2026-08-20 by the
**Capture market baseline** debug action (`Debug/IntercolonyMarketBaseline.cs`), slice 0.2.

**This is not a balance target.** It is the evidence that answers a question nobody can answer
from memory once Stage 2 lands: did the new economy stop producing offers, collapse onto one
category, hand out unlimited supply, inflate prices, or quietly stop letting archetype matter?
Each of those looks like "the market feels off" in play and like nothing at all in a self-test.

## The world it was taken on

```
economy seed      -1586549745
refresh count     432
settlements       358 accessible
cycles sampled    20 (synthetic, past the world's own refresh count)
save              Fenhana / "Intercolony 0.9.3 preflight", schema 42
```

**The seed and refresh count are recorded so this is repeatable.** Both saves carry economy
seed `-1586549745` at refresh `432`, so re-running the capture on either after Stage 2 measures
*the same world*, not a similar one. A `-quicktest` world cannot be used for this — every launch
generates a new one. Use `dev.ps1 run -MainMenu` and load the save.

Determinism check: **PASS** — resampling the same cycles produced identical offers.

## Settlement census

| Archetype | Settlements |
|---|---|
| Agricultural | 56 |
| Industrial | 74 |
| Military | 37 |
| Affluent | 44 |
| Frontier | 46 |
| Tribal | 14 |
| TradeHub | 36 |
| Mixed | 51 |

## Offer generation — appetite

3,995 offers over 20 cycles across 358 settlements.

```
per cycle              199.75
per settlement/cycle   0.558
per-settlement cap     3 outstanding
```

**These are what the generator wants to post, not what the player sees.** `GenerateOpportunities`
stops at a global ceiling (`MaxLiveOpportunities`, the `activeOpportunities` setting) however many
settlements ask — a real refresh in the log created **13**. The code comment on that ceiling
records why it exists: without it, total demand scaled with settlement count and "produced 695
live offers on a full-size world."

So the market is **ceiling-bound, not generator-bound**, and the great majority of what the
generator offers is never listed. Appetite is still the right thing to measure — the ceiling
would mask a generator that had stopped working right up until the moment it fell below the cap —
but these figures must never be read as market size. *(The ceiling value itself was added to the
report after this capture; rerun to record it.)*

### By archetype

| Archetype | Offers | Per settlement/cycle |
|---|---|---|
| Agricultural | 586 | 0.523 |
| Industrial | 828 | 0.559 |
| Military | 425 | 0.574 |
| Affluent | 522 | 0.593 |
| Frontier | 511 | 0.555 |
| Tribal | 160 | 0.571 |
| TradeHub | 407 | 0.565 |
| Mixed | 556 | 0.545 |

**Archetype barely affects how often a settlement posts** — the whole spread is 0.523 to 0.593,
about 13%. That is expected rather than wrong: `PostChance` is a flat 0.35 and only commercial
reputation modulates it. Archetype is meant to shape *what* a settlement wants, not *how often*.
Worth stating plainly because Stage 1 makes identity more legible, and someone could easily read
a flat posting rate afterwards as a regression it never was.

## Demand by category

| Category | Offers | Share |
|---|---|---|
| commodities | 862 | 21.6% |
| intermediate | 878 | 22.0% |
| manufactured | 810 | 20.3% |
| capital equip | 581 | 14.5% |
| furniture | 487 | 12.2% |
| art/unique | 377 | 9.4% |

## Exact-good turnover

419 distinct goods demanded. The top of the distribution:

```
SculptureSmall          144      TableButcher             38
SculptureGrand          120      TableSculpting           36
SculptureLarge          113      DeepDrill                34
Telescope                45      HandTailoringBench       33
SimpleResearchBench      44      Brewery                  32
ElectricSmithy           39      ElectricSmelter          32
FueledSmithy             38      SubcoreEncoder           31
```

**The three sculptures are 377 offers — exactly the whole art/unique category.** That category
has three defs to spread across while the others have hundreds, so its demand concentrates.
Not a defect, but it means any Stage 2 change to art/unique demand shows up as a change to
sculpture demand specifically.

## Lots and prices

```
lot size            min 1     mean 197.0   max 5440
unit price factor   min 0.24  mean 1.16    max 4.83   (offered / base value, n=3995)

constrained         quality 13%   material 9%   condition 11%
buyer pickup        20%
```

The mean price factor of **1.16** and the spread **0.24–4.83** are the numbers most worth
watching. Stage 2.10 adds named price factors, and §22 forbids double-counting; if the mean
climbs well above 1.16 after that work, a factor is probably being applied twice.

## Procurement

Refresh window 432 only. Quote seeding reads `state.RefreshCount`, so only the current window can
be measured without running real refreshes on the player's world — this is a snapshot, not an
average.

```
goods probed           12 (asking 50 units each)
answered by anyone     11/12 (92%)
full vs partial        225 full, 765 partial
suppliers considered   358
```

Response rate ran about 100–130 quotes per good out of 358 settlements, roughly a third.

**The probe basket for this capture was poor and has since been fixed.** It took the
alphabetically first classifiable def per category, which selected `AncientAPC`,
`AncientBandNode` and `AncientCryptosleepCasket` — ruins scenery nobody trades. Measuring supply
for goods no demand ever asks about says nothing about whether procurement works. The diagnostic
now probes the most-demanded goods per category from the sample, so both halves of the report
describe one economy. **Rerun to replace this section.**

That those ancient structures were quoted at all is a separate observation, logged in
`docs/BACKLOG.md`.

## What to compare after Stage 2

Rerun on the same save and check:

1. Determinism still **PASS**.
2. Offers per settlement/cycle has not collapsed toward zero or exploded.
3. No single category has taken over — 0.9.3's largest share is 22.0%.
4. Mean unit price factor is still near 1.16, not double it.
5. Archetypes still differ in *what* they demand; the posting-rate spread staying flat is not
   a regression.
6. Procurement still answers most goods, and the full/partial split has not gone all-full
   (which would mean supply became unlimited).
