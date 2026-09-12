# Intercolony 1.1.0 — production, agreements, and a fuller labor market

Intercolony 1.1.0 is a feature release built from the branch history since 1.0.0, covering 91
player-facing commits. It brings production automation, agreements that can manage their own
routine work, a complete procurement workflow, destination-aware deliveries, a more useful
Business tab, and a larger hiring market.

## Produce

- **Produce** can be enabled on minifiable furniture, its blueprints, and its frames. The colony
  repeats the build/uninstall cycle in the same cell, so one object can keep producing without a
  new manual order each time.
- Production can run indefinitely or **until the colony has enough** of the selected good. The
  target is checked again as stock changes, so production can resume when the stock later falls.
- **Pause production** and **Stop production** are separate actions. Pause suspends the program;
  Stop ends it. Both are available as area commands under **Architect → Orders**, alongside
  Produce/resume.
- Cancelling the blueprint belonging to a Produce loop now ends that loop instead of allowing it
  to recreate itself.

## Agreements that look after themselves

- A selling agreement can ready its own buyer-pickup cycle when the goods are available. Missing
  goods still call for attention, while a routine successful automatic cycle no longer fills the
  letter stack with an **Order ready** notice.
- A procurement agreement can wait for silver and retry instead of immediately counting an
  unaffordable cycle as failed.
- Employees can auto-renew when the worker actually offers another term. The setting is visible
  and clickable on the employee card; the less common contract actions are behind its actions
  menu.
- New recurring agreements start with their automation enabled, while existing agreements keep
  their saved choices. Contract lists collapse ordinary entries and open entries that need the
  player's attention.

## Procurement is a real system now

- **Procurement → Contracts** is a working screen rather than a placeholder. It lists standing
  purchase agreements, pending proposals, counters, and concluded agreements.
- A proposal dialog lets the player choose the item, term, quantity and delivery arrangement,
  preview the deal before sending it, and see what it costs. Rows show per-unit and per-cycle
  prices clearly.
- A standing purchase agreement must be earned through reputation and completed purchases from
  the settlement, just as the selling side is earned. Furniture can be covered by an agreement.
- Supplier replies to ordinary requests now arrive over time instead of all appearing at once;
  nearer suppliers answer sooner. Proposal screens also show the likely acceptance band before
  the player commits.

## Deliveries and the Business tab

- Mark a stockpile or shelf as **Receive deliveries**. Supplier goods prefer a marked destination
  that accepts them and has room, and fall back to the old drop behaviour when no marked location
  can take them.
- **Business** now shows five days of committed cash flow: expected revenue, expenses and net,
  including scheduled payroll and agreement cycles rather than speculative spot sales.
- Production commitments are shown beside what the colony actually completed over a rolling five-
  day window. A made good is costed from its direct materials, and labour is charged only to
  employees who could make it, so the margin is more useful.

## Hiring

- Job postings ask for the work requirement and report the current going rate instead of asking
  the employer to guess a wage.
- Applicants are paid their own ask. A poor employer now attracts worse applicants, not merely
  fewer of them.
- Weapons and apparel arriving with an employee are shown as an **Equipment bond** at hire. The
  bond uses replacement value plus 10%, is included in **Due at hire**, and is returned item by
  item for gear the worker still carries when employment ends. The player is told what came back
  and what was kept.
- **Emergency dispatch** narrows the candidates to the nearest half by travel time, charges a 4×
  wage premium, and shortens the journey to one third with a one-day minimum. The premium and
  arrival time are shown before hiring.

## Relationships and settings

- Commercial standing can now improve a faction's goodwill over time, with a hard ceiling below
  alliance. The Relations row explains whether the pressure is active and why it is not.
- An employee's experience can follow them home: after enough ordinary employment, the worker's
  observed mood can improve or reduce goodwill with the settlement that sent them. The departure
  letter explains the result when there is one.
- Nine new settings control commercial goodwill timing and size, employment-experience thresholds
  and impact, and supplier-response speed. The controls are live and persist with the mod's
  settings.

## Fixes worth calling out

- Wage displays now distinguish the worker's ask from the colony's charge everywhere, direct hires
  are no longer quoted one amount and charged another, and partial employment periods use the
  same daily rate as a full period.
- A made good costs its materials in the profitability calculation rather than the price of
  buying the finished item, and equipment bonds include item quality.
- Selling and supplier markets cache their rows and draw only what is visible instead of rebuilding
  the whole table every frame. **Find Buyer** searches storage rather than the entire map.
- Ordinary hiring travel is bounded to one to twenty days.
- A newly dead employee is no longer discarded from the grave that contains them during cleanup.
  This prevents new save/load damage; it does not repair an already empty grave in an old save.

## New defaults

Fresh installs now use **Minimal** letter volume, a **0.25-day** market refresh, and **50** active
opportunities. Cooked meals and stone blocks start enabled as sellable buy-only categories. The
commercial-goodwill default is quicker but much more limited: **+2 every 7 days**, requiring **85**
commercial reputation and stopping at **15** goodwill. Employment goodwill uses four observed days
and an impact of **±2** by default.

An existing player's saved settings are preserved and nothing is reset. The settings version stamp
means the new defaults apply to a fresh settings file; an older settings file keeps the values the
player had chosen.

## Saves

Released 1.0 uses save schema **56**. This release uses schema **58** (`CurrentSaveVersion` in
`Source/Intercolony/Core/IntercolonyWorldComponent.cs`). Schema 57 added per-contract auto-renew
and auto-ready flags; schema 58 added the persisted production record.

An existing schema-56 save loads and upgrades during post-load initialization. The migration walks
56 → 57 → 58: the new agreement flags default off for agreements already in the save, and the
production record starts empty because older versions never observed completed production. No
historical production is invented, active agreements are not silently enabled, and the upgraded
state is written as schema 58.

## Known limitations and playtest status

The branch has automated coverage for the new code paths, but the pending playtest record still
requires human checks for the Produce controls and long-running loops, auto-renew and auto-ready,
receiving locations and overflow, the dense Business view, supplier-response pacing, contract-row
layout, and the equipment-bond and emergency-hiring experience. The new commercial and employment
goodwill effects also need a seasons-long read. This release does not present those behaviours as
human play-verified.

Several boundaries are deliberate:

- A Produce loop can stall if minified furniture has nowhere to be hauled.
- A pre-existing supplier-delivery defect can complete a partially placed delivery while charging
  the full order; marking a receiving location does not fix that case.
- There is no automatic recurring caravan/animal dispatch, reverse player-supplied labour market,
  optional employee apparel-policy system, full route/provisions/logistics model, equipment-tier
  request, or transport-pod arrival. The equipment bond and emergency-dispatch slices above are
  the shipped portions of those larger ideas.
- Automatic ready is for buyer-pickup cycles; seller-delivery agreements still need a hand-formed
  caravan.
- Existing empty corpses from old saves need the separate repair action; the new grave guard cannot
  reconstruct missing occupants.
- The new grave save/load guard still needs a clean real-play check; the automated coverage is not
  being presented as that check.

Manual coverage remains limited to one machine and load order, with Biotech and the five mods named
in `docs/PENDING_PLAYTESTS.md`; other DLC and mod combinations are untested.

Problems belong in the [GitHub issue tracker](https://github.com/miannoni/Intercolony/issues), with
your mod list and **Player.log**.
