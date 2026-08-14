# Backlog

Ideas worth building that are **not** mapped into a numbered phase in `DESIGN.md`.

**Why this file exists.** `DESIGN.md` holds the plan; `PROGRESS.md` holds what happened;
`PENDING_PLAYTESTS.md` holds what shipped but is unproven. None of them hold "we should build this
one day". Without somewhere to put those, an idea either gets forced into the current phase — which
is how a phase quietly becomes "whatever we thought of this week" — or it is lost when the
conversation moves on.

**How to use it.** Add an item when something is worth doing but is not this phase's work. When an
item is picked up, map it into a numbered phase in `DESIGN.md` and strike it here with a pointer,
rather than deleting it — the next person asking "was this ever considered?" gets an answer.

Nothing here is committed to. An item may be rejected later; record that too, with the reason.

---

## Procurement should be as complete a system as selling

**Raised:** 2026-08-07, by Matteo, while looking at the tab structure.
**Size:** large — a phase of its own, probably more than one.
**Status:** open, deliberately deferred until after 1.0.

Selling has four surfaces. Buying has one. The asymmetry is not a design decision, it is just the
order things were built in: selling came first and grew, procurement was built once in Phase 11 and
never revisited.

| Selling has | Buying has |
|---|---|
| **Market** — buyers advertise what they want | *nothing* |
| **Find buyer** — you go looking for a buyer | **Procurement** — the RFQ flow, which is exactly this |
| **Orders** — what you committed to sell | partial: purchase orders exist but have no screen of their own |
| **Contracts** — recurring supply agreements you fulfil | *nothing* |

### What already exists — do not rebuild it

- **The RFQ flow is "find seller".** `RfqService`, `PurchaseRequest`, `Quotation`. The player states
  what they need, suppliers answer with full or partial quotes, the player picks one. That is the
  mirror of Find Buyer and it works.
- **Purchase orders exist** — `PurchaseOrder`, `PurchaseOrderService`, with delivery and collection,
  a pickup grace period, refunds on supplier failure, and payment taken at acceptance.
- **Fulfilment terms are chosen on the request** as of 2026-08-07: supplier delivers, we collect, or
  either.
- **Quotes sort by column** as of the same date.

### What is genuinely missing

1. **A supplier market.** Settlements advertising what they have surplus of, the inverse of
   `MarketOpportunityGenerator`. The player browses rather than asks. This is the largest piece and
   the one with the most new state.
2. **A purchase orders screen.** Purchase orders are currently visible only inside the Procurement
   tab alongside live requests. They deserve the same treatment sales orders get.
3. **Recurring procurement contracts.** Matteo's phrasing: *"Recurring procurement contracts that I
   offer to suppliers, why not?"* The player offers a standing agreement — so much, so often, at
   this price — and a supplier accepts or declines. The mirror of `SupplyAgreement`, with the
   direction of the offer reversed: today the settlement proposes and the player answers, here the
   player proposes and the settlement answers.

### Open design questions

- **Who proposes?** For sales agreements the settlement proposes and the player accepts. Reversing
  that for procurement means the player names terms and a supplier judges them — which needs a
  supplier-side acceptance model that does not exist yet. Closest existing thing is
  `JobPostingService`'s "you name the terms, they answer", which is the same shape and worth reading
  first.
- **Can a supplier default on a recurring contract?** §125 Procurement already asks "Can suppliers
  default?" and it is still open. A recurring commitment makes that question unavoidable rather than
  theoretical.
- **Does the player pay per cycle or up front?** Purchases currently take payment at acceptance
  (`PurchaseOrderService`, and the comment there notes paying on delivery needs a debt and default
  policy that does not exist). A recurring contract makes up-front payment implausible.
- **Does a supplier market make the RFQ flow redundant?** Probably not — browsing and asking are
  different actions, exactly as Market and Find Buyer are. Worth confirming rather than assuming.

### Why it is deferred

It is four systems, not one, and each has a live counterpart on the selling side that already works.
Nothing about the current mod is broken without it. It belongs after 1.0, when the selling half has
been played enough to know which of its four surfaces actually earn their place — because the answer
to that should shape which of the four get mirrored.

---

## Empty-state paragraphs use hard-coded heights and can clip

**Raised:** 2026-08-08, by Matteo, during the 0.9.0 Steam smoke test.
**Size:** small — one focused pass across five call sites.
**Status:** open. Non-blocking beta UX issue; deliberately not fixed on launch day.

The explanatory paragraph on the **Relations** screen is vertically clipped at 1.75x UI scale: its
second wrapped line is cut off.

The cause is a hard-coded `Rect` height rather than a measured one. Relations reserves `60f` at
`Source/Intercolony/UI/MainTabWindow_Intercolony.cs:1916` for content that occupies about four line
heights — a title line, a blank line, and a ~189-character paragraph that wraps to two lines under
`GameFont.Small`.

**This is systemic, not local to Relations.** Every empty-state paragraph does the same thing:

| Screen | Height | Location |
|---|---|---|
| Relations | `60f` | `MainTabWindow_Intercolony.cs:1916` |
| Selling/Market | `60f` | `MainTabWindow_Intercolony.cs:602` |
| Procurement | `70f` | `MainTabWindow_Intercolony.cs:1561` |
| Labor | `76f` | `MainTabWindow_Intercolony_Labor.cs:181` |
| Business | `44f` | `MainTabWindow_Intercolony_Business.cs:220` |

Relations is simply the one that broke first: the longest text in nearly the smallest box. The
others are not correct, only luckier, and any wording change or UI scale could tip them the same way.

**Severity: cosmetic.** The empty-state branch returns immediately after drawing
(`MainTabWindow_Intercolony.cs:1922`) without advancing `y`, so nothing below is positioned relative
to that height. There is no overlap and no mispositioning. It is also only reachable with no trading
history at all, so a player with any record never sees it.

**The fix**, when it is picked up: bind each label string to a local, measure with
`Text.CalcHeight(text, width)` under the font already in effect, and use that for the rect. RimWorld
exposes this at `reference/decompiled/Verse/Text.cs:209`, and the codebase already uses it for Market
table rows at `MainTabWindow_Intercolony.cs:962` — so this is applying an existing local pattern to
paragraphs, not introducing a new one.

**Why it was deferred.** It was found between a verified Workshop upload and its smoke test. Layout
code is exactly what should not be touched in that window, and the defect is invisible to any player
who has traded once. It belongs in a point release with any other beta UX findings.

---

## ~~Procurement delivers and refunds to the wrong colony~~

**Added 2026-08-09.** The same defect that was just fixed for sales orders, still live in
procurement. `PurchaseOrderService` delivers purchased goods at
`Find.AnyPlayerHomeMap` (`PurchaseOrderService.cs:180`) and refunds a cancelled or defaulted order to
the same place (`:280`). `Find.AnyPlayerHomeMap` returns the **first** map with `IsPlayerHome`, not
the colony that placed the order — and `PurchaseOrder` persists no map.

**Severity: real, not cosmetic, in a multi-colony game.** Goods you paid for arrive at the wrong
base, and a refund lands somewhere you may not be. Single-colony games are unaffected, which is why
it has gone unnoticed. Unlike the sales-order case there is no "goods were not there" failure to
alert the player — delivery to the wrong colony succeeds silently, which is arguably worse.

**The fix is already written, next door.** `SalesOrder.fulfillmentMap` (commit `25a5308`) follows
`EmploymentContract.destinationMap`: persist the map with `Scribe_References`, record it where the
order is bound to a colony, resolve per record at use, fall back when null, and check membership in
`Find.Maps` to catch a map abandoned mid-session. Apply the same shape to `PurchaseOrder`. It needs
its own schema bump.

**Why it was deferred.** It was found while fixing the sales-order half. Folding it in would have
made one revertable commit into two unrelated changes to different subsystems. It is a small,
well-understood, self-contained fix — good candidate for the next point release.

**Resolved 2026-08-13** (`209bafd`, `9e2c2c2`, `5681c2e`). Delivery and refunds now use the ordering
colony persisted on `PurchaseOrder` (save schema 32). The same work found and fixed a more serious
defect: with no home map available, a refund could be falsely reported and finalized without paying
anything, permanently losing the player's silver; it now holds and retries. Fixed in code but **not
verified in play**; a two-colony reproduction has never been run.

---

## Rejected or superseded

*(nothing yet)*
