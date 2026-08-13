# Save schema 24 → current: one consolidated test

**Why this file exists.** Schema moved several times in a single development run
(2026-08-09 onward). Testing each step as it landed was not practical and was deliberately
deferred: most steps are additive with safe defaults, so the risk is substantially the same
whether they are tested one at a time or all at once. Schema 29 → 30 is the stated exception:
it deliberately repairs existing request statuses.
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

Except for the explicitly described 29 → 30 repair, each step is additive. An absent field means
the old behaviour, so those additive steps do not need to move data.

| From → To | What was added | Behaviour for existing records |
|---|---|---|
| 24 → 25 | Optional animal specifications on sales orders, purchase requests, quotations and purchase orders | No specification present ⇒ the record is ordinary goods, exactly as before |
| 25 → 26 | Each sales order remembers the colony it is fulfilled from | No map recorded ⇒ falls back to the first player home map, which is the old behaviour |
| 26 → 27 | Animal health and gestation floors on the specification | No floors ⇒ unrestricted, and only animal records have a specification at all |
| 27 → 28 | The animals set aside for a buyer to collect | No list ⇒ nothing designated, and no pre-existing order is an animal order anyway |
| 28 → 29 | A supply agreement's fulfilment mode | Defaults to seller delivery, which is what every existing agreement was created under, so the default *is* the truth about them |
| 29 → 30 | **Repairs data** — relabels purchase requests that were wrongly marked Cancelled | The only step so far that changes existing values rather than adding a field. See below. |
| 30 → 31 | A purchase request's minimum workmanship | No floor ⇒ take whatever is offered, which is how every existing request already behaved |

**29 → 30 is the exception to "every step is additive."** Accepting a quotation used to mark
the request `Cancelled`, a status meaning "withdrawn by the player" — so every successful
purchase left a record claiming the player had abandoned it. The step relabels those to
`Ordered`. It identifies them exactly rather than guessing: a purchase order stores the
`requestId` it came from, so only requests that genuinely produced an order are touched, and
anything with no matching order really was withdrawn and is left alone. The log line reports
how many were relabelled — **a count of 0 on a save with purchase history would be
suspicious**, not reassuring.

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
seven steps above. A save already at the current schema prints `State loaded (schema N, …)`
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
