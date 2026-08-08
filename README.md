# Intercolony

Intercolony gives the settlements on the world map concrete economic relationships with your
colony. They want things, you decide what is worth producing, and the agreement matters after you
accept it.

The working loop is straightforward:

> See what settlements want → accept work you can fulfill → make or gather the goods → deliver them
> or wait for collection → get paid → build a record with that settlement.

When the colony cannot make what it needs, it can ask settlements for quotations and buy from the
suppliers that answer. When production needs more hands, it can hire workers for fixed or open-ended
terms. Wages, arrears, combat clauses, dismissal, injury and death are obligations rather than free
temporary colonists. Commercial reputation follows deliveries and defaults; employer reputation
follows how hired people are treated.

## What is in the mod

- Settlement demand for commodities, intermediate goods, manufactured products, furniture, art,
  weapons, apparel and minifiable equipment.
- Sales orders with quantities, deadlines, delivery terms, quality and condition requirements where
  those requirements make sense.
- Direct buyer searches for stock the colony already holds.
- Purchase requests, supplier quotations, pickup or delivery, and purchase orders.
- Commercial reputation and recurring supply agreements with settlements that come to trust the
  colony.
- Direct hiring, job postings, fixed and open-ended employment, payroll, arrears, conduct rules and
  employer reputation.
- A business summary for cash movement, payroll runway and standing agreements.

Distance, availability, faction relations and physical transport still matter. Intercolony is not
an unlimited catalog and does not replace caravans or ordinary RimWorld trade.

## Requirements

- RimWorld 1.6.
- Harmony, loaded before Intercolony. It is declared as a dependency in the mod metadata.

Intercolony is **English-only**. This is a project decision, not a missing localization task: much
of the player-facing writing is composed at runtime, and this project is not taking on the rewrite
needed to localize that prose well.

## Compatibility and known limits

Testing has been done on one machine with Biotech, at 1.75x UI scale. Royalty, Ideology, Anomaly and
all other DLC are untested, not declared unsupported. Hospitality, Common Sense, RT Fuse, Tilled
Soil, and FSF Filth Vanishes With Rain And Time were present throughout Phase 25 without an
Intercolony exception, but that is not a systematic test of every interaction. Other mods are
untested.

Products are selected from their RimWorld def properties rather than a vanilla-only list, so
ordinary DLC and modded items follow the same path as Core items. That construction is useful, but
it is not a guarantee for every special item or mod interaction. Non-minifiable buildings are a
permanent exclusion because caravans cannot carry them.

The tested environment, reasoning, Harmony patch surface and bug-report details are recorded in
[docs/COMPATIBILITY.md](https://github.com/miannoni/Intercolony/blob/main/docs/COMPATIBILITY.md).

## Project status

This is a hobby mod in pre-release testing. It has never been in anyone else's hands. Its systems
have been built and exercised in the author's own games, but it has not had outside compatibility,
balance or usability testing. Keep normal backups of saves used to try it.

## Reporting problems

Use the [GitHub issue tracker](https://github.com/miannoni/Intercolony/issues). Include reproduction
steps, the active mod list and load order, UI scale for layout problems, and `Player.log` from the
failing session. Intercolony log lines begin with `[Intercolony]`.

## Development

The project targets .NET Framework 4.7.2. Build it from the repository root with:

```powershell
dotnet build Source/Intercolony/Intercolony.csproj
```

The full design and its constraints are in [DESIGN.md](https://github.com/miannoni/Intercolony/blob/main/DESIGN.md). The code is licensed under the
[MIT License](./LICENSE).
