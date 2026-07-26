# Technical note — unique goods and capital equipment

**Phase 7 spike (DESIGN.md §100). Written 2026-07-25.**

Deliverable required by §100: chosen representation, serialization strategy, unsupported
edge cases, compatibility risks. Evidence comes from
`Source/Intercolony/Debug/IntercolonyUniqueGoodsSpike.cs`, run in game against RimWorld
1.6 with Core, Biotech, Hospitality, CommonSense, RT Fuse, TilledSoil and
FilthVanishesWithRainAndTime loaded.

---

## Result

All seven §100 prototype cases pass: **23 assertions, 0 failures**, plus a separate
save/load probe that returned PASS.

| # | Case | Result |
|---|---|---|
| 1 | Sell one Masterwork chair | pass |
| 2 | Sell one sculpture with art metadata | pass |
| 3 | Buy one stove | pass (crate/uncrate path) |
| 4 | Save/load before completion | pass |
| 5 | Install purchased equipment | pass (no custom code needed) |
| 6 | Preserve quality/material/HP | pass |
| 7 | One modded minifiable building | pass — `Building_RTCircuitBreaker` (RT Fuse) |

---

## Chosen representation

**Move the actual `Thing`. Never destroy and recreate it.**

This contradicts the phrasing in DESIGN.md §23.2, which anticipates "unique item
snapshots". The spike's job was to choose a representation, and snapshots turn out to be
the wrong mechanism *for transfer*. They remain the right mechanism for *description* —
see "Two different jobs" below.

A minifiable building becomes a `MinifiedThing` via `MinifyUtility.MakeMinified`. That
wrapper is itself a `ThingWithComps` holding the real building inside a `ThingOwner`
(`MinifiedThing.innerContainer`). The original object — with every comp still attached —
is what travels. Nothing is copied, so nothing can be lost in the copy.

Confirmed to survive crating and un-crating unchanged:

- quality (`CompQuality`)
- material / stuff (`Thing.Stuff`)
- hit points
- art title, author and tale reference (`CompArt`)

### Why not snapshots

A snapshot has to enumerate what it preserves. Anything it does not know about is
dropped. The things it would drop are exactly the things that matter:

- `CompArt.taleRef` — a `TaleReference` into the world's tale registry. Rebuilding an
  "equivalent" sculpture produces art with no history.
- Ideoligion style and precept sources (`StyleSourcePrecept`).
- **Any comp added by any mod.** This is the decisive argument. DESIGN.md §64 flags
  "unsafe custom comps" as a known hazard, and a snapshot is precisely the construct that
  turns an unknown comp into silent data loss. Moving the object cannot lose a comp it
  has never heard of.

### Two different jobs

The confusion worth naming: "represent a unique item" means two unrelated things.

| Job | Representation | Why |
|---|---|---|
| **Describe** an item in a listing or order — something the player does not own yet | `OrderLine`: ThingDef + optional quality / stuff / condition constraints | Must be comparable against many candidate items. Must persist without referencing an object that may not exist. |
| **Transfer** an item that exists | The `Thing` itself, minified when applicable | Must lose nothing. |

Phase 6's `OrderLine` already does the first job, and `OrderValidator.Matches` already
unwraps minified things to do the comparison. No new type is needed for either job.

---

## Serialization strategy

**Intercolony serializes nothing about a unique object.**

- Orders persist a `ThingDef` reference plus constraint values — `Scribe_Defs` and
  `Scribe_Values`, no object graph.
- The object itself lives in a vanilla container: a caravan pawn's inventory
  (`Pawn.inventory.innerContainer`), or the map. RimWorld serializes those containers
  and the whole comp graph inside them.
- Therefore save/load correctness for unique goods is RimWorld's problem, not ours, which
  is the point of choosing this representation.

Verified: a crated masterwork wooden chair at half hit points and a crated sculpture with
a custom art title were placed on the map, the game was saved, quit to main menu, and
reloaded. Both came back with quality, hit points, art title and author intact.

One consequence worth stating: because the delivered object is destroyed at hand-over
(`SalesOrderService.RemoveFromCaravan` → `Destroy(DestroyMode.Vanish)`), a sold sculpture's
tale reference leaves the world with it. That is correct for a sale — the goods are gone —
but it means art sold to a settlement has no continuing existence. If a future phase wants
counterparties to *display* bought art, this is where that would change.

---

## Unsupported edge cases

1. **Non-minifiable buildings.** `def.Minifiable` is false when `minifiedDef` is null.
   These cannot be crated and so cannot be carried by caravan at all. Phase 8 generation
   must filter on `def.Minifiable`, not merely on category.
2. **Things already inside a container.** `MakeMinified` refuses to minify a `Thing` whose
   `holdingOwner` is set, and logs a warning. Minify before placing into an inventory,
   never after.
3. **Installation is a player action.** A `MinifiedThing` placed on the map is installed
   through vanilla's own `Blueprint_Install` flow. No custom install code is needed — this
   is a finding, not a gap — but the player must have a legal cell and a builder. Nothing
   should assume installation completes.
4. **Stack semantics.** A minified item has `stackCount == 1` regardless of the inner
   thing. `OrderValidator.CountableUnits` treats it as one unit rather than trusting the
   wrapper. An order for "20 chairs" therefore means 20 separate crates, with the caravan
   mass to match.
5. **Art authorship after sale.** The author name stays on the piece. Whether a buyer
   should re-attribute is a design question, deliberately not answered here.
6. **Quantity realism.** 20 crated chairs is a lot of caravan mass. Phase 8 should keep
   unique-good lot sizes small; this spike does not address balance (§78).

---

## Compatibility risks

| Risk | Severity | Mitigation |
|---|---|---|
| Mods adding comps with state we would have dropped | **High if snapshotting** | Eliminated by moving the object rather than copying it |
| Mods with custom `minifiedDef` wrappers | Medium | One tested: RT Fuse's `Building_RTCircuitBreaker` crated and unwrapped correctly. Generic `MinifyUtility` path used throughout, no assumptions about the wrapper subclass |
| Mods subclassing `MinifiedThing` (e.g. vehicle frameworks) | Medium, **untested** | `GetInnerIfMinified` uses an `is MinifiedThing` check, so subclasses are handled; no such mod was loaded to confirm |
| A def becoming invalid after a mod is removed mid-save | Low | Already handled: `SalesOrder.IsValidAfterLoad` drops orders whose `ThingDef` no longer resolves, and logs at error level (§62) |
| Problem items needing exclusion | Low | The §64 blacklist from Phase 4 already accepts rules by comp, category or def, with no code change |

**Only one modded minifiable building was available to test.** Case 7 passed against RT
Fuse, which is genuine third-party coverage, but it is a single data point from a simple
mod. A vehicle or furniture-framework mod would be a stronger test and has not been run.

---

## Recommendation for Phase 8 (§101)

The strategy is sound; generalized implementation can proceed.

1. Widen generation in `IntercolonyProductClassifier.IsFungibleTradeItem` to admit
   buildings **where `def.Minifiable` is true**. Non-minifiable buildings stay excluded
   permanently, not temporarily — they cannot physically be delivered.
2. Keep unique-good lot sizes small.
3. No changes needed to delivery, validation, or persistence. The existing paths already
   handle minified things: `Matches` unwraps, `CountableUnits` counts correctly, and
   `RemoveFromCaravan` splits a stack of one cleanly.
4. Art and quality display in listings is a UI task, not a representation task.

This closes the open commitment recorded in `CLAUDE.md`: "everything should be tradeable".
Phase 8 is where it lands, and this note is the evidence that it can land safely.
