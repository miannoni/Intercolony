# Intercolony

Intercolony gives every settlement on RimWorld's world map its own commercial economy. Market
pressure and regional diffusion change local demand, while circumstance events can disturb it. Your
colony builds product brands and a specialization through what it makes, buys and delivers.

The loop is to read a settlement's economy, choose what to produce or source, negotiate bounded
terms, fulfill the agreement, and build a durable commercial history with that settlement. Commercial
relationships continue after acceptance through bounded renegotiation. When the colony needs inputs,
it can use the supplier market, request quotations, place purchase orders, or keep a recurring
procurement contract.

## What is in the mod

- Per-settlement demand, market pressure, regional diffusion and circumstance events.
- Product brand strength and colony specialization that shape commercial outcomes.
- Sales orders with quantities, deadlines, delivery terms, quality and condition requirements, plus
  direct buyer searches for stock the colony already holds.
- Full procurement parity: a supplier market, RFQs, purchase orders and recurring procurement
  contracts.
- Commercial relationships with bounded negotiation, post-acceptance renegotiation and reputation.
- Per-settlement commercial history that records trade and relationship outcomes.
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

The public release remains 0.9.3 on `main`. The broader 1.0 work is in development on branch `1.0`:
Stages 0–7 are closed and Stage 8 is in progress. 1.0 is not released; the branch has not merged,
the play sitting has not happened, and the remaining release documentation is still to be written.
Keep normal backups of saves used to try it.

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
