# Compatibility

This document records evidence and limits. It is not a promise that every combination works.

## Verified environment

Everything below was observed on one machine, in one load order. The installed game's
`Version.txt` reports **RimWorld 1.6.4871 rev590**.

- **DLC:** Biotech only. Royalty, Ideology and Anomaly are not owned and will never be tested on
  this machine. Every DLC other than Biotech is untested here, not declared unsupported.
- **Mods loaded:** Hospitality, Common Sense, RT Fuse, Tilled Soil, and FSF Filth Vanishes With Rain
  And Time. Intercolony ran alongside all five throughout Phase 25 with no exceptions attributed to
  their interaction. This was ordinary play, not a systematic compatibility test.
- **UI scale:** 1.75x. This is the scale at which the layout was judged. Other scales are untested.

## Content from DLC and mods

Intercolony does not enumerate vanilla products. The entry point is
`IntercolonyProductClassifier.IsFungibleTradeItem`. A def must first pass `IsTradableGood`: it needs
a base market value of at least 0.4, the player must be allowed to sell it, and it must not be
silver, a pawn, a corpse, or on Intercolony's trade blacklist. After that, any
`ThingCategory.Item` is accepted. A `ThingCategory.Building` is accepted only when it is
minifiable.

Classification then uses the def's category ancestry and properties such as weapon, apparel,
medicine, drug, building, interaction-cell and meal-source behavior. An otherwise eligible item
that matches no more specific rule falls into intermediate goods. None of those paths checks a
vanilla `defName` or the package that supplied the def. A normally defined item from a DLC or mod
therefore enters the same demand, order and procurement paths as a Core item.

That is a reason to expect ordinary modded content to work. It does **not** prove that every special
item has sensible prices or quantities, that every mod uses ordinary trade and category properties,
or that another mod's UI and Harmony patches cooperate with Intercolony. A mod can also blacklist a
specific def when generic treatment is wrong.

### Evidence

- **Observed in play:** Biotech content was generated as real demand.
- **Observed in the Phase 25 load order:** the five mods listed above remained loaded without an
  Intercolony exception. Hospitality was among them, but its pawn-management interaction was not
  deliberately exercised as a compatibility test.
- **Measured in the verified load order:** **406** tradable fungible defs. The per-source dump was:

  | Source | Kind | Package ID | Total | Commodities | Intermediate | Manufactured | Furniture | Capital equip | Art/unique |
  |---|---|---|---:|---:|---:|---:|---:|---:|---:|
  | Core | Core | `ludeon.rimworld` | 337 | 103 | 68 | 85 | 61 | 17 | 3 |
  | Biotech | official DLC | `ludeon.rimworld.biotech` | 67 | 5 | 23 | 22 | 16 | 1 | 0 |
  | RT Fuse | mod | `ratys.rtfuse` | 2 | 0 | 0 | 0 | 2 | 0 | 0 |
  | Common Sense | mod | `avilmask.commonsense` | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
  | Harmony | mod | `brrainz.harmony` | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
  | Hospitality | mod | `orion.hospitality` | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
  | Intercolony | mod | `miannoni.intercolony` | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
  | Tilled Soil | mod | `gt.sam.tilledsoil` | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
  | [FSF] Filth Vanishes With Rain And Time | mod | `frozensnowfox.filthvanisheswithrainandtime` | 0 | 0 | 0 | 0 | 0 | 0 | 0 |

Biotech contributes 67 of the 406 defs across five of the six categories. Examples include
`DetoxifierLung`, `DetoxifierKidney`, `Mechlink`, `ControlSublink`, and `RemoteRepairer`. There is no
Biotech-specific code in Intercolony, so this is evidence that the def-driven classifier classifies
DLC content correctly.

RT Fuse contributes `Building_RTCircuitBreaker` and `Building_RTMakeshiftFuse`. Both were classified
automatically as furniture because they are minifiable buildings. No special handling was added for
RT Fuse. Common Sense, Hospitality, Tilled Soil, and FSF Filth Vanishes With Rain And Time correctly
contribute zero: they change behavior or terrain rather than adding eligible trade goods. Zero here
is a correct classification result, not a failure.

These counts demonstrate classification. They do not demonstrate an end-to-end trade of a Biotech
mechlink or an RT Fuse circuit breaker.

### Reasoning, not a test result

Defs from untested DLC and mods should flow through the same property-driven classifier described
above. The classification dump identifies the pack that supplied each def through
`Def.modContentPack`, including its name, package ID, and Core/official flags. This supports an audit
of what the active def database contains; it does not exercise delivery, balance, UI, pawn behavior,
or cross-mod patches.

The reported source is the content pack which loaded the def. A later XML patch does not become the
def's source, so the dump does not attribute individual field changes to patch authors.

### Regenerating the source counts

Start a game with the load order being checked. Enable Development mode, press **F12**, click the
orange bug icon in the top-right toolbar, search for **Dump product classification**, and run it.
The `[Intercolony]` output in `Player.log` contains both the original category histogram and the
per-source table. Active mods with zero eligible defs are included with a zero total.

## Permanent exclusion

Non-minifiable buildings cannot be traded. A caravan cannot crate and carry them, so Intercolony
cannot complete a physical delivery. This is deliberate and permanent, not an unfinished
compatibility case.

## Harmony patches

Intercolony applies three postfixes:

- `RimWorld.Planet.Settlement.GetFloatMenuOptions`: yields the existing settlement float-menu
  options, then appends Intercolony sales-order delivery options.
- `RimWorld.Planet.Caravan.GetGizmos`: yields the existing caravan gizmos, then appends delivery
  gizmos when the caravan is parked at the buyer.
- `RimWorld.CaravanFormingUtility.AllSendablePawns`: when the ordinary call excludes quest lodgers
  and an Intercolony employee is active, it re-runs the vanilla method with lodgers allowed and adds
  only pawns recognized as Intercolony employees. A re-entry guard prevents the postfix from
  recursing into itself.

A conflict would have to touch those same extension surfaces: replace, suppress, filter or assume a
fixed order for settlement float-menu options or caravan gizmos; or change caravan pawn eligibility,
the meaning of `allowLodgers`, or the returned pawn list. The first two patches append without
removing existing entries. The third preserves vanilla eligibility checks and does not add another
mod's lodgers, but another patch on `AllSendablePawns` remains a real compatibility surface.

## Pawn-management mods

Employees belong to the player faction while working but remain quest lodgers rather than permanent
colonists. Mods which assume "player faction" means "permanent colonist" may display or manage them
incorrectly. Colonist-bar replacements, work-tab replacements and roster mods are the main risk.

Hospitality was loaded throughout Phase 25 without incident. That is useful evidence because it also
manages non-colonist pawns on the player's map, but it was not a deliberate interaction test. No
claim is made for other pawn-management mods.

## Reporting a useful bug

Open an issue in the [Intercolony issue tracker](https://github.com/miannoni/Intercolony/issues) and
include:

- the steps that reproduce the problem and what happened instead;
- the complete active mod list and load order, including DLC;
- the UI scale if the problem is visual;
- the affected save when it can be shared;
- `Player.log` from the failing session.

Intercolony prefixes its own messages with `[Intercolony]`, including every line of a multi-line
dump. Warnings, errors, dropped or unresolvable records, and the nearby stack trace matter most.
With Development mode enabled, world initialization also logs either
`State loaded (schema ..., nextId ...)` or `State initialized fresh (schema ...)`; older saves log
the migration path. Include those lines so the report identifies the save schema involved.
