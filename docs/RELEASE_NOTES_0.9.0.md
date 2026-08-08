# Intercolony 0.9.0 — first public beta

Intercolony gives the settlements on the world map concrete economic relationships with your colony.
They want things, you decide what is worth producing, and the agreement matters after you accept it.

> See what settlements want → accept work you can fulfill → make or gather the goods → deliver them
> or wait for collection → get paid → build a record with that settlement.

This is Intercolony's first public beta and its first external testing round.

## What's in it

- **Selling.** Settlement demand for commodities, intermediate goods, manufactured products,
  furniture, art, weapons, apparel and minifiable equipment. Sales orders carry quantities,
  deadlines, delivery terms, and quality or condition requirements where those make sense.
- **Direct buyers.** Search for someone who wants stock you already hold.
- **Procurement.** Ask settlements for quotations, compare what comes back, and buy — with pickup or
  delivery.
- **Reputation and standing agreements.** Deliveries and defaults build a commercial record;
  settlements that come to trust you offer recurring supply agreements.
- **Hired labor.** Direct hiring or public job postings, fixed and open-ended terms, payroll,
  arrears, conduct rules, dismissal with notice, renewal, and a separate employer reputation.
  Injury, capture and death carry obligations. Workers are not free temporary colonists.
- **Business report.** Cash movement, payroll runway and standing agreements on one screen.

Distance, availability, faction relations and physical transport still matter. Intercolony is not an
unlimited catalog and does not replace caravans or ordinary RimWorld trade.

## Requirements

- RimWorld **1.6**
- **Harmony**, loaded before Intercolony (declared as a dependency in the mod metadata)

## Install

Unzip `Intercolony-0.9.0.zip` into your RimWorld `Mods` folder, so you end up with
`Mods/Intercolony-0.9.0/About/About.xml`. Enable it in the mod list below Harmony, and restart.

## Why this is a pre-release

Core systems have been extensively tested internally, on one machine and one load order. This beta
is the first external test of balance, compatibility, UX and long-running behavior.

**Keep normal backups of any save you use it with.**

## The tested environment

Stating this plainly rather than implying broader coverage:

- **RimWorld 1.6.4871 rev590.**
- **DLC: Biotech only.** Royalty, Ideology and Anomaly are untested — not declared unsupported.
- **UI scale: 1.75x.** This is the scale the layout has been judged at. Other scales are untested.
- **Alongside:** Hospitality, Common Sense, RT Fuse, Tilled Soil, and FSF Filth Vanishes With Rain
  And Time, with no exception attributed to their interaction. That is ordinary play, not a
  systematic compatibility test.

Products are selected from their RimWorld def properties rather than a vanilla-only list, so DLC and
modded items follow the same path as Core items. That is a reason to expect ordinary modded content
to work — not a guarantee for every special item or mod interaction.

The full reasoning, the Harmony patch surface, and what a useful bug report contains are in
[docs/COMPATIBILITY.md](https://github.com/miannoni/Intercolony/blob/main/docs/COMPATIBILITY.md).

## Known limits

- **Non-minifiable buildings can never be traded.** A caravan cannot crate and carry one. This is a
  permanent exclusion, not an unfinished feature.
- **English only.** A project decision rather than a missing translation task: much of the
  player-facing writing is composed at runtime.
- **Employees are quest lodgers in the player faction.** Mods that assume "player faction" means
  "permanent colonist" — colonist bars, work-tab replacements, roster mods — are the main
  compatibility risk, and none of them has been tried.

## Reporting problems

Open an issue on the [tracker](https://github.com/miannoni/Intercolony/issues) with reproduction
steps, your full mod list and load order, UI scale for anything visual, and `Player.log` from the
failing session. Intercolony's own lines all begin with `[Intercolony]`.

The most valuable reports are the three that internal testing cannot reach:

1. **A real economy decision** — a sale or purchase you accepted and one you rejected, and what made
   the difference.
2. **A repeatable trick** — the strongest strategy you found for making money or dodging a cost.
3. **Your setup, plus one thing you actually observed** — a DLC or modded item traded, or an
   employee viewed through a colonist-bar or work-tab mod.

## Saves

Save schema 24. Intercolony migrates older saves forward, and the intention is to preserve saves made
with this beta where that stays practical. This is a pre-release, though, and that is not a
guarantee — if a change makes migration impractical, the release notes for it will say so plainly.
