# Save schema 24 → current: one consolidated test

**Why this file exists.** Schema moved several times in a single development run
(2026-08-09 onward). Testing each step as it landed was not practical and was deliberately
deferred: every step is *additive with no data to move*, so the risk of any one of them is
low, and the risk is anyway identical whether they are tested one at a time or all at once.
This file is the single test that settles the whole chain.

**Status: NOT YET RUN.** Nothing below has been observed in the real load order. Each step
was seen executing only in isolated throwaway RimWorld installations with a stripped mod
list, which is not evidence about the player's game.

---

## Why the dev loop cannot prove this

Worth stating plainly, because it cost a session's time to rediscover:

- `dev.ps1` launches RimWorld with `-quicktest`, which creates a **new world**. A new world
  initialises at the *current* schema, so the migration chain never runs at all.
- `dev.ps1`'s log reader targets the real user profile, while a sandboxed game may write its
  log elsewhere. The displayed log can therefore be **stale**, showing an old schema — which
  looks like evidence and is not. One run reported schema 24 while the code was at 27.

Neither the dev loop nor any self-test can settle migration. **Only opening a real save
does.**

---

## The steps

Each is additive. An absent field means the old behaviour, which is why none of them needs
to move data.

| From → To | What was added | Behaviour for existing records |
|---|---|---|
| 24 → 25 | Optional animal specifications on sales orders, purchase requests, quotations and purchase orders | No specification present ⇒ the record is ordinary goods, exactly as before |
| 25 → 26 | Each sales order remembers the colony it is fulfilled from | No map recorded ⇒ falls back to the first player home map, which is the old behaviour |
| 26 → 27 | Animal health and gestation floors on the specification | No floors ⇒ unrestricted, and only animal records have a specification at all |
| 27 → 28 | The animals set aside for a buyer to collect | No list ⇒ nothing designated, and no pre-existing order is an animal order anyway |
| 28 → 29 | A supply agreement's fulfilment mode | Defaults to seller delivery, which is what every existing agreement was created under, so the default *is* the truth about them |

**28 was the last step for the animal feature**; 29 came from the contract rework.

One note on 27 → 28 specifically, since it is the only step holding *pawn references* rather
than plain values: those are saved as references, not deep saves, because the map already
owns the pawns. A reference that does not resolve on load becomes null, which the collection
path reads as "that animal is no longer there" — the same outcome as the animal having died.
So a save that loses one is degraded, not corrupted, and the order fails honestly rather than
throwing.

---

## The test

**Setup.** Any existing save. An older one exercises more of the chain, but any save works.

**Steps.**

1. Launch RimWorld normally — **not** through `dev.ps1`, for the reason above.
2. Load the save.
3. Read `Player.log`.
4. Save, quit **to the main menu**, and load again.
5. Read `Player.log` again.

**Pass, first load.** The log contains a `Migrating state from schema N to <current>` line
followed by one indented line per step, in ascending order. For a save at 24 that is all
three steps above. A save already at the current schema prints `State loaded (schema N, …)`
and no migration lines — also a pass, it simply exercises nothing.

**Pass, second load.** `State loaded (schema <current>, …)` and **not**
`State initialized fresh`. That distinction is the entire proof: it shows the state round
tripped through the save rather than being silently re-created from nothing. A re-init would
look superficially fine while having discarded everything.

**Failure — any of:**

- A red error during load.
- Any line reporting a dropped order, request or purchase. These say "unresolvable" and
  usually mean a def could not be found; in a load order that has not changed, that is a
  real defect, not a missing mod.
- A schema number lower than expected after loading.
- `State initialized fresh` on the second load.
- Silver, orders, contracts or employees differing from before the reload.

---

## What this test does not cover

It proves the chain runs and the state survives. It does **not** prove any of the *features*
those fields support — animal specifications, per-colony fulfilment, or the floors — because
an existing save has no animal records in it at all. Those are separate entries in
`docs/PENDING_PLAYTESTS.md`.
