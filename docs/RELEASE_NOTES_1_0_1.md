# Intercolony 1.0.1 — procurement fixes

Intercolony 1.0.1 fixes two defects found in play immediately after 1.0 shipped. Both were in Procurement: the Market tab was slow because its whole table was rebuilt every frame, and the Contracts tab said "Under development" even though standing procurement agreements were already built underneath.

## Procurement

- **The Market tab no longer rebuilds its table every frame.** It now builds the table once and reuses it, refreshing when you enter the tab, sort a column, buy something, or the listings change, and otherwise about twice a second. Nothing the table shows changed.
- **The Contracts tab is a real screen now.** Propose a standing purchase to a settlement, see your agreements with a count badge on the tab, withdraw a proposal that has not been answered, accept or decline the supplier's final counter after its exact terms are laid out, and withdraw from a live or war-suspended agreement. It no longer says "Under development".

## One limitation

- **Procurement agreements cannot be renewed yet.** When the agreed cycles are done the agreement ends and you propose a new one. The selling side does renew; the buying side does not yet.

## Saves

**The save format did not change.** Schema stays 56 and no migration runs, so a 1.0 colony opens directly. This was verified by checking that nothing in this release touches persistence.
