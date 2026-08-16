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
anything, permanently losing the player's silver; it now holds and retries. Matteo directly observed
two-colony delivery and refund routing working on 2026-08-13; no supporting log output was captured.
The map-less and zero-placement paths remain without practical play reproduction.

---

## ~~Find Buyer demand is never consumed~~

**Raised:** 2026-08-13, by Matteo, during 0.9.1 release-prep verification.

A settlement advertises that it will take a quantity, such as 12 units. After the player sells all
12, the advertised amount does not decrease. The player can immediately create another independent
12-unit trade with the same settlement, repeatedly and without limit. In play, this makes the
advertised demand meaningless and permits unlimited selling to one settlement.

**Resolved 2026-08-14** (`86aa768`). Advertised appetite is now reduced from order creation by the
player's open orders to that settlement, plus orders completed at or after the current cycle's
`lastRefreshTick`; a refresh moves the window, so completed sales stop counting without reset code.
Counting at creation prevents several full-size orders being stacked before **Mark Ready**, which
does not gate appetite. `SalesOrder` gained a completion tick through the additive schema 32 -> 33
migration. The fix has not been verified in play, and the self-tests have not been rerun since it.

---

## ~~Find Buyer advertised unit price does not match the price paid~~

**Raised:** 2026-08-13, by Matteo, during 0.9.1 release-prep verification.

A buyer advertised demand for 4,000 rice at 2 silver per unit, but did not pay that unit price when
the player sold a smaller quantity, such as 200. The player's expectation is that selling below the
advertised quantity should still pay at least the advertised unit price.

**Resolved 2026-08-14** (`0b1dfe9`). The listing omitted the fulfilment multiplier while commit
applied buyer pickup x0.85 by default or seller delivery x1.12, and the listing priced the lot the
player could sell while displaying the buyer's full appetite beside it. Listing and commit now
share `FindBuyerService.SellRateFor`, and the priced quantity is shown next to the rate. This defect
predates 0.9.0: the logistics factor came from `a07a41f`/`cda25cf`, not the recent correction batch.
The fix has not been verified in play, and the self-tests have not been rerun since it.

---

## ~~Find Buyer falsely reports inventory is already committed elsewhere~~

**Raised:** 2026-08-13, by Matteo, during 0.9.1 release-prep verification.

With 10,000 rice in storage and one existing 3,500-unit order, the player could not create a second
3,000-unit order. The UI reported words to the effect of "0 units free" and that units were already
committed elsewhere.

**Resolved 2026-08-14** (`d350f2e`). Matteo's report was correct, but his guess that inventory
refreshed only periodically and **Mark Ready** should re-scan was not. `OrderValidation` capped
`matchedQuantity` at the current order's 3,000-unit requirement, then `1b8ec67` subtracted the other
order's whole 3,500-unit commitment from that capped base, which clamped to zero. Re-scanning at
**Mark Ready** would therefore not have fixed it. Validation now keeps the scan's uncapped total and
subtracts other commitments from that. `1b8ec67`'s original
buy-only-obligation protection is preserved; it was introduced after the 0.9.0 tag and was never in
a release. A regression assertion was added to the order self-test, but the suite has not been rerun
since the fix and the fix has not been verified in play.

---

## Concluded purchase requests have no retention cap

**Raised:** 2026-08-14, during the player-feedback batch.
**Size:** small.
**Status:** open; deliberately not decided in that batch.

Closed sales orders and closed purchase orders are each capped at the hundred most recent and pruned
automatically (`8fc1ece`). Concluded **purchase requests** are not. They got a manual **Clear
completed history** action in `32b3864`, but nothing trims them on their own, so a long game keeps
every request it ever raised — the Orders page was observed showing "25 of 61", where the 25 is only
a display limit.

The removal rule already exists as a shared predicate in `OrderHistoryService` alongside the other
two, so adding a cap is small work. It was left out because the other two collections had their
retention chosen deliberately and stated, and this one should be decided the same way rather than
inheriting a number by accident.

---

## 0.9.1 play-test findings — 2026-08-15/16

**Raised:** 2026-08-15/16, by Matteo, during one play-test session against the released 0.9.1.

These are the player's own observations from that session.

### 1. "Sell to this buyer?" dialog is visually broken and over-verbose

**Verdict:** DEFECT + design request.

Text renders on top of other text, some text is clipped and unreadable, and there is simply too much
prose on the popup. A screenshot confirms a line of the explanatory paragraph is cut off mid-render
and overlapped by the **Fulfillment:** row below it — the content overflows its area without
scrolling. Matteo wants more use of tooltips and less text directly on popups.

### 2. The employee signing fee is never disclosed before hiring

**Verdict:** DEFECT.

Nothing in the UI shows that an up-front signing fee is required for daily-paid employees. From
what the player sees, they can hire; then on attempting to hire they are refused because they need
X silver to start the contract. The cost is disclosed only at the point of failure.

### 3. Buyer travel time looks inconsistent with distance

**Verdict:** NEEDS DIAGNOSIS.

A settlement 47 tiles away took 3 days to collect an order; one 35 tiles away took 12 days. A
screenshot shows 160 tiles quoted at 11 days. Working hypothesis to be verified, not assumed: the
**Distance:** figure shown is straight-line tiles while travel uses real world-path cost, so a short
hop across mountains or water genuinely takes longer — which would make this a display defect
rather than a travel-model defect. Unconfirmed.

### 4. Animal sales are dead — Mark Ready is a silent no-op

**Verdict:** DEFECT — highest priority.

Selling chickens did not work; pigs did not either. The order can be created, but clicking **Mark
ready** does absolutely nothing: no dialog, no flash, no message, no log line, no error. Evidence
from `Player.log`: "Created order 3755 from Find Buyer: 1x chicken for Chess Township, 95 silver,
12d, BuyerPickup." appears, but unlike every non-animal order in the same session (3771, 3772, 3773,
3774, 3791, 4021, each of which logged "goods declared ready"), order 3755 never logged it. The
session threw zero exceptions. This indicates an early return before anything renders.

This is the first time animal trade has ever been played — `CLAUDE.md` records all five slices as
built but never exercised. Likely two stacked defects: the silent return itself (a refusal must
always tell the player something), and whatever made the animal ineligible underneath.

### 5. Add a "mark this order ready" toggle to the "Sell to this buyer?" popup

**Verdict:** ENHANCEMENT.

Default on, with the default changeable in mod settings.

### 6. Ready sales orders should be sortable and better laid out

**Verdict:** ENHANCEMENT.

Possibly a table. As it stands the information is poorly arranged and the player cannot tell what
will arrive where, because there is too much text everywhere.

### 7. Ready sales orders should show total order value

**Verdict:** ENHANCEMENT.

The player needs to know when money is coming in.

### 8. Procurement quotes can be re-rolled — this is an exploit

**Verdict:** DEFECT — exploit.

A generated procurement request must persist until the market refreshes. Otherwise the player can
generate N requests for the same need and roll the die until they get a much lower price than they
should, from a close enough settlement. The log shows "Request 4094: 20x medicine - 99 quote(s)", so
each reroll re-rolls ninety-nine quotes at once.

Decision taken: fix via deterministic quotes — seed quote generation on the market-refresh counter
plus the item, so withdrawing and re-requesting returns the same quote set, making the reroll
pointless rather than forbidden. The seed must NOT include quantity, or the player nudges the amount
by one to reroll; prices scale from a fixed per-unit roll instead. The refresh counter is already
durably persisted, so no new state is needed for the seed.

### 9. Accepting one quote should not withdraw the whole procurement request

**Verdict:** DEFECT.

Unless the player clicks the withdraw button, the request should stay open. Matteo requested 1000
iron; he may want to accept more than one offer, particularly when a single offer does not fill the
requested amount. Decisions taken: remaining quotes stay live until the next market refresh, and
accepting a partial quantity leaves the request outstanding for the remainder.

This is closely coupled to finding 8 — both are to be implemented as one procurement-request-
lifecycle change. It likely requires a save schema bump from 39 to 40.

### Agreed order of work

1. Record findings (this task).
2. Diagnose 4 and 3.
3. Fix 4.
4. Fix 8 and 9 together.
5. Fix 2.
6. UI pass covering 1, 5, 6, 7 — deliberately deferred to last at Matteo's request, but explicitly
   not dropped.
7. Real-save migration test, then `/codex:review --background`.

---

## Rejected or superseded

*(nothing yet)*
