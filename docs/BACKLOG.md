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

## Procurement agreements can never be renewed.

**Raised:** 2026-08-25, while building the procurement agreements UI.
**Size:** unknown until the renewal path is implemented.
**Status:** open.

`ProcurementContract.cs` declares `renewalOffered`, `renewalExpiryTick` and `renewals`, and persists
all three through `ExposeData` (around lines 239–245 and 495). Nothing in the procurement code under
`Source/Intercolony` sets `renewalOffered` to `true`; `ProcurementContractService` has no
`AcceptRenewal` or `DeclineRenewal`, unlike the selling-side `ContractService`, whose UI answers a
renewal offer inline.

A procurement agreement runs its scheduled cycles and ends; the three fields are scribed into every
save and read back as defaults forever. The selling side renews and the buying side does not, breaking
the mirror the procurement work was built to hold; a persisted field nobody writes looks implemented
from the save schema and is not. The tab had no way to offer a renewal, which exposed this during UI
work.

## ~~Skip-reporting output still has suite-side format gaps~~ — FIXED in 1.0 (`ff93d94`)

**Raised:** 2026-08-23, during skip-reporting verification.
**Size:** small — test-output cleanup.
**Closed:** 2026-08-23 in commit `ff93d94`. The bare `SKIPPED` in
`IntercolonyUniqueGoodsSpike.cs` now carries a reason, and Brand, Event and Negotiation have
converged on `SKIPPED`, so `dev.ps1`'s filter now uses one pattern.

`IntercolonyUniqueGoodsSpike.cs:156` emits a bare `SKIPPED` with no reason, so it can never be
explained by the reporting filter; the fix belongs in the suite, not `dev.ps1`.

Three suites (`IntercolonyBrandSelfTest.cs`, `IntercolonyEventSelfTest.cs`, and
`IntercolonyNegotiationSelfTest.cs`) still emit the legacy singular `SKIP` while the rest use
`SKIPPED`; the formats should converge so the filter stops needing special cases.

## The Stage 3 drought pricing assertion can pass vacuously.

**Raised:** 2026-08-23, during four fresh-world full-suite runs.
**Size:** small — test-fixture fix.
**Status:** open.

The assertion `a drought changes a newly computed price without repricing the accepted deal`
failed once in four fresh-world full-suite runs on 2026-08-23 with `before 0.3763, current
0.3763` — the two values were identical.

**Cause:** it was the fixture, not the production code: nothing guaranteed that the drought it
triggered actually moved the price of the category the test then sampled, so in some worlds the
assertion compared a number to itself.

**Fix:** make the fixture assert its own precondition — that the drought moved the sampled
category's price at all — and SKIP with a reason when it did not, rather than comparing two
identical numbers and calling it a pass.

**Defect class:** a vacuous pass here is the same defect class as the hollow assertions caught by
mutation elsewhere in this program: green without the ability to go red.

## The market-shortage assertion can pass vacuously.

**Raised:** 2026-08-23, during the Stage 8 verification run.
**Size:** small — test-fixture fix.
**Status:** open.

The market-shortage assertion reported `27 -> 27` and passed. That is the same vacuous-pass class
as the Stage 3 drought assertion: the comparison stayed unchanged, so the green result did not
prove that a shortage changed the quantity or condition under test.

**Fix:** make the fixture assert the change it is supposed to observe, and SKIP with a reason when
the shortage does not move the sampled value. The assertion must be able to go red when the shortage
path is removed.

## The SALES-side contract cancellation penalty is still an inline literal.

**Raised:** 2026-08-23, during the Stage 8 documentation audit.
**Size:** small — constant extraction and calibration check.
**Status:** open.

The procurement-side cancellation penalty is now a named constant. The corresponding SALES-side
contract cancellation penalty is still an inline literal. The calibration sitting must hunt for the
literal, confirm the intended value and meaning, and give it the same named-constant treatment so
the two sides cannot drift silently.

## Stage 7B's W2 idempotence claim remains unproven.

**Raised:** 2026-08-23, during Stage 7B verification.
**Size:** unknown until the fixture is isolated.
**Status:** open; recorded as unproven, not as a passing proof.

Three mutations failed to isolate W2's idempotence claim. Two landed inside a guard the bounded
fixture never enters, because at exactly the bound the excess computed to zero; the fourth mutation
aborted the suite. The surrounding §7.7 safety assertions were red-on-cue, but that does not prove
W2's idempotence. Isolate a fixture that actually enters the guarded path, then rerun the mutation
and four-fresh-world stability checks.

## ~~A sold animal may leave dangling pawn relations in the save~~ — HYPOTHESIS DISPROVEN

**Raised:** 2026-08-21, from reading the log of Matteo's play session.
**Disproven:** 2026-08-21, the same day, by reading the discard path properly.
**Status:** the *observation* stands and is unexplained; the *accusation against our code* was wrong.

**The blaming of `SalesOrderService` below is incorrect, and it is left here rather than deleted
because a wrong theory that looks this plausible is worth being able to recognise again.**
`PassToWorld(pawn, Discard)` routes to `WorldPawns.DiscardPawn`, which calls `Pawn.Discard`, which
calls `relations.ClearAllRelations()` (`reference/decompiled/Verse/Pawn.cs:2442-2456`). That method
does **both** halves of the cleanup: it removes the pawn's own direct relations, and then walks
`pawnsWithDirectRelationsWithMe` removing every relation on *other* pawns whose `otherPawn` points
back at it (`reference/decompiled/RimWorld/Pawn_RelationsTracker.cs:844-855`). So the exact dangling
reference described below is cleaned up by the discard itself, and the parent/child relations that
`Notify_PawnSold` leaves behind are removed a moment later regardless.

**What the reasoning got wrong.** It correctly established that vanilla keeps a sold pawn while we
discard it, correctly established that `Notify_PawnSold` clears only `Bond`, and then stopped one
call short — at the difference, instead of following our own path to its end. The two verified facts
made the conclusion feel measured. **Every fact in it was true and the conclusion was still false**,
which is precisely the failure mode this project warns about when reading a delegate's output; it
turns out to be just as available first-hand.

**What is still unexplained, and is now the whole of the item.** Matteo's log really does carry 143
`Trying to save reference to a discarded thing Chicken66024 … label=otherPawn` warnings. Something
holds a `DirectPawnRelation` to a discarded pawn, and the clean-up above says it should not be
reachable through a sale. Candidates not yet examined: a chicken that died rather than being sold, a
stale `pawnsWithDirectRelationsWithMe` reverse index, or a mod. **Do not start from the sale path** —
it has been read and it is clean.

**Size:** unknown until the cause is found.
**Not in the 1.0 program**, and not RED: RimWorld tolerates the reference, logs it, and loads with it
null.

His `Player.log` carries **143 warnings**, all naming one pawn and one field:

```
Trying to save reference to a discarded thing Chicken66024 with saveDestroyedThings=true.
This means that it's not deep-saved anywhere and is no longer managed by anything in the
code, so saving its reference will always fail. , label=otherPawn
```

`otherPawn` is `DirectPawnRelation.otherPawn` (`reference/decompiled/RimWorld/DirectPawnRelation.cs:9`,
saved with `saveDestroyedThings: true` at line 28). So some surviving pawn still holds a direct
relation to a pawn that has been discarded. Earlier in the same session,
`Chess Township collected 1x Chicken (male, Adult, not pregnant) and paid 95 silver` — an
Intercolony buyer-pickup animal sale.

**Why this is plausibly ours, and it is a real difference from vanilla.** Our handoff is
`SalesOrderService.cs:1022-1024`: `DeSpawn` → `PreTraded(PlayerSells, …)` → `PassToWorld(…,
Discard)`. Vanilla's equivalent, `Pawn_TraderTracker.GiveSoldThingToTrader`
(`reference/decompiled/RimWorld/Pawn_TraderTracker.cs:156-160`), calls `PreTraded` and then
**`AddPawnToStock(pawn)`** — the sold pawn stays a live, managed pawn in the trader's stock, so
every relation pointing at it still resolves. We discard instead, and nothing else in the game
does that to a pawn that other pawns are related to.

**`PreTraded` does not clear enough to make discarding safe.** It ends at
`relations.Notify_PawnSold(playerNegotiator)`, which
(`reference/decompiled/RimWorld/Pawn_RelationsTracker.cs:940-963`) removes **only**
`PawnRelationDefOf.Bond`, and only for related pawns that are alive *and* have a mood need —
it `continue`s past everything else. Animals that breed acquire `Parent`/`Child` relations to
one another, and chickens breed constantly. Those relations are never removed, so after the
discard they dangle.

This is consistent with `CLAUDE.md`'s existing rule that handoffs must go through `PreTraded` —
that rule is about the *bond and the thought*, and it is correct as far as it goes. What it does
not cover is that vanilla never discards the pawn afterwards.

**The decisive test, which has not been run:** sell an animal that has a living parent or
offspring in the colony, then save, and watch for a warning naming that animal by id. If it
appears, the fix is to strip the remaining direct relations before discarding — or to stop
discarding.

**Why it is not RED and not being fixed mid-slice (§19).** No crash, no lost silver, no lost
obligation. RimWorld tolerates the dangling reference, logs it, and loads with the reference
null. The cost is log noise and a slightly degraded save, not corruption. It also predates
Stage 2 entirely and has nothing to do with the market work in flight.

## ~~Suppliers quote ancient ruins scenery~~ — NOT REACHABLE IN PLAY, closed 2026-08-22

**Raised:** 2026-08-20, from the Stage 0.2 market baseline.
**Closed:** 2026-08-22 by tracing the def chain and both production paths. **No code change was
needed, and none should be made.**

**The player cannot request or be offered an Ancient APC.** `TradableDefs` is built from
`IsFungibleTradeItem`, which requires `HasSupportedPhysicalForm` — `category == Item`, or a
`Building` that is **minifiable**. `ThingDef.Minifiable` is `minifiedDef != null`
(`reference/decompiled/Verse/ThingDef.cs:541`), and **no def in the ancient chain sets
`minifiedDef`**: `AncientAPC` → `NonDeconstructibleAncientBuildingBase` → `AncientBuildingBase` →
`BuildingBase`, all four checked, and `Buildings_Ancient_Outdoors.xml` and
`Buildings_Ancient_Indoors.xml` contain zero occurrences of it between them. So ancient scenery
never enters `TradableDefs`.

Both production paths are gated on that list, which was the other half worth checking rather than
assuming:

- **Demand:** `MarketOpportunityGenerator` picks from `DefsInCategory`, which iterates `TradableDefs`
  (`IntercolonyProductClassifier.cs:289-301`). So the entry's worry that "the same classifier rule
  presumably lets them be *demanded* too" is also unfounded.
- **Procurement:** `Dialog_CreateRequest.cs:885` draws its candidate list from `TradableDefs`.

**Where the 88 quotes actually came from — and this is the part worth keeping.** The baseline's
original `PickProbeGoods` (`fe011b7`) iterated `DefDatabase<ThingDef>.AllDefsListForReading` and
filtered **only** on `Classify(def).HasValue`. `Classify` maps a def to a category and says nothing
about tradability, so the diagnostic quoted defs the market can never offer, then reported real
quote counts for them. The numbers were true; the premise was not.

**The lesson generalises past this item: a diagnostic that bypasses the production gate reports
things the player can never see, and its output looks exactly as authoritative as a real finding.**
This one cost a backlog entry and an afternoon's suspicion of a defect that does not exist. The
probe basket was already changed to rank by observed demand from real opportunities, which fixed it
by accident — the current `PickProbeGoods` cannot select an untradeable def because it starts from
generated opportunities.

**If this is ever reopened**, the question worth asking is a different one: whether
`IntercolonyTradeBlacklistDef` should exclude ancient scenery *defensively*, in case a mod makes one
minifiable. That is speculative and not worth doing now.

The baseline's first probe basket took the alphabetically first classifiable def per category
and landed on `AncientAPC`, `AncientBandNode` and `AncientCryptosleepCasket`. All three were
quoted by suppliers: 88, 69 and 79 settlements respectively offered to sell them, an Ancient
Cryptosleep Casket at a mean 723 silver.

These are map scenery from ancient ruins. Whether a settlement should be able to *manufacture
and deliver* one is a real question — vanilla treats them as things you find, not things anyone
makes. `IntercolonyProductClassifier` classifies them as tradeable products, which is what puts
them in supplier reach.

Not fixed now because it is outside the 1.0 program's scope and nothing in that program depends
on it: the baseline's probe basket was changed to rank by observed demand instead, so the
diagnostic no longer asks about goods nobody trades. But a player who requests an Ancient APC
and gets 88 quotes is seeing something odd, and the same classifier rule presumably lets them
be *demanded* too.

Worth checking against `IntercolonyTradeBlacklistDef`, which already exists to exclude defs and
currently excludes 10.

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

## ~~Empty-state paragraphs use hard-coded heights and can clip~~ — FIXED in 0.9.3 (`c1610af`)

**Raised:** 2026-08-08, by Matteo, during the 0.9.0 Steam smoke test.
**Closed:** 2026-08-22 on audit. **It was fixed on 2026-08-18 and this entry was never struck** —
the fix arrived as part of 0.9.3's measured-text pass rather than as work on this item, so nobody
came back to the backlog to close it.

**Verified rather than assumed.** Every `emptyMessage` site in the UI now binds the string to a local
and sizes its rect with `Text.CalcHeight`, including the Relations paragraph that was the original
report — the "No trading history yet… Reputation is held per settlement" text, now measured at
`MainTabWindow_Intercolony.cs:3089`. Grepping for a `Widgets.Label` with any of the five recorded
literal heights (`60f`, `70f`, `76f`, `44f`) returns nothing. The commit that did it is
`c1610af`, *"measure dialog and empty-state text instead of boxing it"*.

**The line numbers in the table below are all stale**, which is itself the lesson: an entry that
pins a defect to `file.cs:1916` decays as soon as the file moves, and every one of those five now
points at unrelated code. Anchoring on a searchable symbol — here `emptyMessage` — survives edits
where a line number does not.

**Why it stayed open for four days after being fixed.** The 0.9.3 batch was scoped from
`docs/BACKLOG.md`'s Tier 2 UI list, and this defect was fixed *as an instance of the general rule*
rather than as this item, so the closing pass missed it. Worth checking the backlog against the
code after any batch that applies a rule broadly, not just after work aimed at a named entry.

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

### ~~2. The employee signing fee is never disclosed before hiring~~

**Verdict:** DEFECT.

Nothing in the UI shows that an up-front signing fee is required for daily-paid employees. From
what the player sees, they can hire; then on attempting to hire they are refused because they need
X silver to start the contract. The cost is disclosed only at the point of failure.

**Resolved 2026-08-16** (`66848bb`). The hiring UI now discloses the signing fee, and Matteo confirmed
the figure appears in play. The label says **Due now**, which reads oddly beside "signing fee"; Matteo
deliberately left that wording for the Tier 2 UI pass rather than reopening the functional fix.

### 3. ~~Buyer travel-time promise is not persisted~~; its presentation is confusing

**Verdict:** DEFECT — persistence resolved; presentation remains Tier 2.

The original hypothesis that the displayed distance is straight-line while travel uses real
world-path cost has been investigated and disproved. No pathfinder is invoked anywhere in `Source/`:
a grep for `WorldPathing`, `CaravanArrivalTimeEstimator` and `EstimatedTicksToArrive` across the whole
source tree returns nothing. Terrain, roads, mountains and coastline are not modelled at all.

Distance is great-circle geometry. `Source/Intercolony/Market/MarketOpportunityGenerator.cs:375-383`
calls `Find.WorldGrid.ApproxDistanceInTiles(home.Tile, settlement.Tile)` using
`Find.AnyPlayerHomeMap`. Travel time is calculated at
`Source/Intercolony/Orders/SalesOrderService.cs:638-641` as `distanceTiles / 14f`, clamped to 1-20
days, with a 3-day fallback when distance is negative.

The reported "35 tiles took 12 days" was almost certainly a misreading of the dialog, and the dialog
is at fault. `MainTabWindow_Intercolony.cs:1823` defines `const int DeadlineDays = 12` — a fixed
deadline to mark the goods ready — and shows it in a sentence directly adjacent to the arrival
estimate. 35 divided by 14 is 2.5, which rounds to 3, so a 35-tile settlement cannot produce a 12-day
arrival under this formula. This is concrete evidence for finding 1: the dialog is cluttered enough
to have misled the author.

A real defect remains underneath. The dialog promises an ETA computed from `offer.distanceTiles`
captured at offer generation, but dispatch at **Mark Ready** recomputes distance independently, and
`SalesOrder` never persists the promised value. In a multi-colony game the promise and the delivery
can disagree. This is the same "a displayed figure and a charged figure come from one calculation"
rule that `CLAUDE.md` records as established by commit `0b1dfe9`. Persisting the promised distance is
a Tier 1 defect; making the presentation unambiguous is Tier 2.

**Persistence resolved 2026-08-16** (`ec1ccdd`). `SalesOrder` now records the distance from which its
buyer-pickup promise was computed, and dispatch consumes that value instead of recomputing it. This
is schema 39 → 40. The confusing presentation described above is unchanged and remains in Tier 2.

### ~~4. Animal sales are dead — Mark Ready is a silent no-op~~

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

**Resolved and proven 2026-08-16** (`b50b2e2`). Order 4215 sold a chicken by buyer pickup, and a
separate save sold a bonded labrador retriever with the bond warning appearing correctly. This was
the first time animal trade had ever worked for a player.

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

### ~~8. Procurement quotes can be re-rolled — this is an exploit~~

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

**Resolved 2026-08-16** (`a11a97f`), not yet proven in play. Quote generation was already seeded, but
the seed used the fresh `request.id`, so recreating a request still rerolled it. It now keys on the
market refresh counter and requested def, deliberately excluding quantity.

### ~~9. Accepting one quote should not withdraw the whole procurement request~~

**Verdict:** DEFECT.

Unless the player clicks the withdraw button, the request should stay open. Matteo requested 1000
iron; he may want to accept more than one offer, particularly when a single offer does not fill the
requested amount. Decisions taken: remaining quotes stay live until the next market refresh, and
accepting a partial quantity leaves the request outstanding for the remainder.

This is closely coupled to finding 8 — both belong to the same procurement-request lifecycle.

**Resolved 2026-08-16** (`f1e6852`), not yet proven in play. A partial acceptance now leaves the
request open for the outstanding quantity and keeps its other quotes live. This is schema 40 → 41.

### 10. Quality and material do not affect the price when selling — only when buying

**Verdict:** FEATURE — not a defect.

When the player sells, the item's quality and material are ignored, so a def is treated as fungible.
The result is that the player has no incentive to offer *better* goods, only a *higher volume* of
goods.

Matteo's proposal: let the player post their own offering into the market. **Find Buyer** then becomes
the bulk / commodity channel — high volume, fungible goods like rice, with quality and material
irrelevant. The **Market** becomes the low-volume / high-quality channel, where the player posts a
specific sale offer — his example was 10 excellent-quality wool parkas — and quality and material do
factor into the price.

This gap is already a recorded known limitation. `PROGRESS.md:366` states: "No quality or material
selection when offering stock; the search treats a def as fungible. Selling a specific masterwork
item through Find Buyer is not possible."

**The pricing engine already supports it.**
`Source/Intercolony/Market/IntercolonyPricing.cs` already accepts `stuff` and `minQuality` on
`UnitPrice`, and applies `QualityPremium`, `MinQualityPremium` and material-aware
`BaseValue(def, stuff)` per `DESIGN.md` §101. `PROGRESS.md:282` confirms that quality demands already
appear in the market — for example, "Tuque (excellent+)" — with the premium visible in the price
tooltip. Quality and material are therefore already priced, but only when the *buyer* demands them,
never when the *player offers* them. The missing work is selection, matching and UI, not a new
pricing model.

This builds on a standing decision rather than conflicting with one: `PROGRESS.md:1697` records
§125's used-goods question as decided — "kept, as a quality floor". It is also the mirror image of
the backlog's first entry, Matteo's 2026-08-07 request that procurement become as complete a system
as selling: this asks that selling gain a market-posting mechanism.

**This is feature-sized work, a Phase 27 candidate, and must not be folded into a point release.**

### Agreed order of work

This is the current ranking, not a fixed plan; Matteo has asked that it be re-ranked as new items
arrive.

#### Tier 1 — defects, target a 0.9.2 point release

No open work. Findings 2, 4, 8 and 9 and the persistence half of finding 3 are resolved above.

#### Tier 2 — UI pass

**Started 2026-08-17.** Re-ranked into three slices after a design pass. The original list mixed a
structural defect with editorial work; separating them means the box is fixed before the text is
rewritten, so the trimming is a design choice rather than a bug workaround — and a later wording
change cannot reopen the overlap.

**The root cause of finding 1 is confirmed and is shared with the empty-state entry above.**
`Dialog_ConfirmQuantity.DoWindowContents` draws the body with a bare `Widgets.Label` into leftover
space (`Dialog_ConfirmQuantity.cs:171-176`) inside a fixed `520 × 536` window (`:148-150`).
`Widgets.Label` neither clips nor scrolls — it paints the whole string from the top-left of the rect
regardless of the rect's height — so a body taller than the space left overdraws the
**Fulfillment:** row at `:182`. The overlap and the clipping are one defect: **unmeasured text in a
fixed box.** The body it is handed for a buyer-pickup sale is six blocks, ~700 characters
(`MainTabWindow_Intercolony.cs:1842-1858`), which cannot fit at 1.75x UI scale.

The empty-state sweep is larger than the five sites listed above: the line numbers there date from
2026-08-08 and have drifted. As of 2026-08-17 there are roughly ten, including
`MainTabWindow_Intercolony.cs` at ~712, ~1260, ~1404, ~2875 (`60f`) and ~2046, ~2212 (`70f`),
`MainTabWindow_Intercolony_Labor.cs` at ~181 and ~243, and `MainTabWindow_Intercolony_Business.cs`
at ~149 and ~220. Sweep by pattern, not by that list.

**2a — measurement, no wording changes.** `Dialog_ConfirmQuantity` sizes to its measured body,
clamped to ~70% of `UI.screenHeight` and scrolling past the clamp, with the controls block pinned
outside the scroll view; plus `Text.CalcHeight` across every empty-state paragraph. Both auto-size
and scroll are required — auto-size alone still breaks at high UI scale on a small screen, and
scroll alone makes the player scroll a four-line body. Independently testable at 1.75x scale.
Now a hard rule in `CLAUDE.md` (#7).

**2b — the sell dialog, editorially.** Finding 1's verbosity, finding 3's presentation, finding 2's
**Due now** wording, and finding 5's toggle. The body becomes a labelled key/value block — payment,
distance, fulfilment rate, *mark ready by*, *buyer arrives* — with rationale moved into tooltips.
That resolves finding 3's display half as a side effect: the deadline and the arrival estimate
become two rows with different verbs and different units, so they can no longer be read as one
number, which is exactly the misreading that produced the original report.

**Matteo's decision, 2026-08-17: key/value rather than prose is now the standard for every popup
and every panel in the mod, not just this dialog.** Recorded as `CLAUDE.md` hard rule #6.

**Finding 5's toggle, as decided.** Default on, default changeable in mod settings. If **Mark ready
now** is ticked and the goods do not validate, the order is **not** created, the dialog stays open
so the player can correct a misclick, and the refusal speaks — per the standing rule that RimWorld
draws a disabled `Widgets.ButtonText` identically to a live one, so a silent refusal is an invisible
dead control. Because the toggle defaults on, its refusal must name its own escape hatch — "untick
**Mark ready now** to create the order and ready it later" — otherwise the ordinary "I will craft it
next week" flow is walled off on every sale.

**2c — the orders list.** Findings 6 and 7: a sortable table with columns `#`, buyer, goods,
quantity, value, status/ETA, sorted by soonest deadline by default. Finding 7 is narrower than it
looks — `OrderDetailText` already shows silver while an order is open
(`MainTabWindow_Intercolony.cs:3434-3436`), but the `BuyerEnRoute` branch drops it entirely
(`:3422-3426`), which is precisely the "ready orders show no value" report.

#### Tier 2 progress — 2026-08-17

**2a done, `c1610af`.** `Dialog_ConfirmQuantity` measures and scrolls; twelve empty-state
paragraphs measured. The sweep found roughly ten sites, not the five listed above.

**2b done, `713dd1e`.** `TermRow` (label, value, tooltip) plus a rows-builder overload; both sell
dialogs converted. **Confirmed in play the same day** — Matteo's screenshot shows the block
rendering correctly at his UI scale with no overlap and no clipping, and finding 3's presentation
half resolved: *Mark ready by 12 days from now* and *Buyer arrives about 6 days after you mark
ready* now read as two facts, which is what the original misreading turned on.

**2c done, `c8c4d1a`.** Sortable table with the agreed columns; `Value` uses
`DiscountedTotalPayment` in every state including en route, which was the actual gap.

Two defects the review caught before 2c landed, both worth remembering:

- **The sentinel self-test was orphaned.** `IntercolonyOrderSelfTest.cs` asserted "an unset
  buyer-arrival sentinel is never formatted as a duration" against `OrderDetailText`, but the table
  renders through a new `OrderStatusEtaText`. The assertion would have passed for ever while the
  live path went unguarded. It now targets the live method, `OrderDetailText` is deleted rather than
  kept alive by its own test, and a second case covers open and closed orders. **This is the same
  family as the two-colony SKIP: a green test that tests nothing.**
- **The closed-order outcome note was dropped**, which is the only place the player learns why an
  order failed and where `HostilityPolicy` writes its explanations. Restored as a row tooltip.

**`854b7f1`** seats the discount slider: `Widgets.HorizontalSlider` draws its end labels *above* the
track (`reference/decompiled/Verse/Widgets.cs:2116` — `rect.y - 18f + 3f`), so at an offset of `4f`
the `0%`/`100%` captions sat 11px above the row and crowded the fulfilment buttons. Confirmed fixed
in play. The same commit corrects two Business empty states that drew at `x=6f` while measuring at
the full `inRect.width` — the c1610af defect class, missed by c1610af.

**Self-test after 2c: 107 passed, 0 failed** — but on a single-colony quicktest world, so
`recorded-map collection vs AnyPlayerHomeMap` reported SKIPPED again. The changes were
presentation-only, so the risk is low, but **that assertion still has exactly one real execution to
its name and it failed on it.** A two-colony run is owed before the next release.

**`69b6041` names the signing fee**, replacing **Due now**. It turned out to be more than wording:
prepaid-pay workers have *no* signing fee — their whole discounted term is charged up front, which
is a different charge — so the disclosure now distinguishes **Signing fee**, **No signing fee** and
**Prepaid wages**, keyed off `WageStructure.IsPeriodic()` (literally `!= Prepaid`). Calling a term
prepayment a signing fee would have named a mechanic the game does not have. Both
insufficient-silver refusals in `EmploymentService` were aligned to the same vocabulary, which was
the actual confusion.

**`dbf4e4f` adds the Mark ready now toggle** (finding 5), default on, setting
`markReadyNowByDefault`. Shown only for buyer pickup, because `CanMarkReady` is
`status == Accepted && fulfillment == BuyerPickup` and a control that does nothing is worse than
none. On refusal nothing is created and the dialog stays open, per Matteo's decision.

The design rule it established: **the pre-check and the creation it predicts must be one
construction.** `MarkReadyForPickup`'s decision half is extracted into a non-mutating
`CanMarkReadyNow`, and `CreateFromOffer` and the pre-check both build through one
`BuildOrderFromOffer`, with the real id assigned after. It was first written as a second copy of the
same initializer — correct that day, and silently wrong the moment anyone adds a field to one of
them. Review also caught the transient dereferencing `offer.settlement` where `CreateFromOffer`
guards it, which crashed exactly where the real path fails safely.

Two invariants a future change must not break: a transient carries `id 0`, and `ReadyRefusal` keys
on `order.id > 0` to avoid naming an order that does not exist; and `CanMarkReadyNow` returns false
with **no reason** for `!CanMarkReady`, which is unreachable from the pre-check only because the
transient is always `Accepted` + `BuyerPickup`. Calling the pre-check for seller delivery would
produce a wordless refusal.

**`d8f83e4` converts the market acceptance dialog**, the last one building its body as prose and the
origin of the whole problem: it called `BuyerPickupTimingExplanation`, which states the readiness
deadline and the travel estimate as one sentence. `BuildListingTooltip` still calls that helper and
is deliberately unchanged — a tooltip is already where explanation belongs. The dialog also stopped
multiplying money in a UI file, and now shows the quality, material and condition constraints, which
were in the listing tooltip but not in front of the player at the moment of committing.

**Tier 2 is complete.** Remaining: finding 10 only, which is Tier 3 and must not enter a point
release.

### Follow-on: the hire dialog predates the measurement pass

**Raised 2026-08-17** by Matteo, playing the Tier 2 build: the **No end date** checkbox on the hire
popup was drawn on top of the term-length slider.

**Fixed the same day.** `Dialog_HireWorker.cs` positioned it with `new Rect(inRect.width - 220f,
y - 34f, 210f, 28f)`. The negative offset was written to reach back and sit the toggle *beside* the
term row, as its §36.4 comment says — but `y` advances twice between that row and the checkbox, so
it landed on the slider instead, which spans the full width. It now has its own measured row below
the slider, and `y` advances past it before the wage summary is drawn.

**This is a distinct defect class from `c1610af`'s** and worth naming separately: not an unmeasured
box, but **a rect positioned by a negative offset from a cursor that later gained an intervening
line**. It is invisible when written and breaks when someone inserts anything between the anchor and
the thing anchored to it. A layout that says "34px above wherever we are now" is a latent bug even
when it currently renders correctly.

**Two latent items in the same file, found by the sweep and deliberately left for later** — both are
hard-coded heights holding text that can wrap, i.e. the `c1610af` class:

- `Dialog_HireWorker.cs:94` — the candidate name uses a fixed `32f` title height with no wrap
  protection. A long name at high UI scale clips.
- `Dialog_HireWorker.cs:229` — the signing-fee / prepaid-wages disclosure uses a fixed `24f`. The
  wording got longer in `69b6041`, so this is likelier to wrap now than when it was written.

Neither was folded into the checkbox fix, to keep that one revertable. Small, well-understood, good
candidates for the next point release.

#### Tier 3 — features, Phase 27 candidates

Explicitly out of scope for any point release.

1. **Finding 10 — player-posted market sale offers.**

---

## ~~`Find.AnyPlayerHomeMap` is a systematic error class~~

**Raised:** 2026-08-16, while investigating finding 3 above.
**Size:** small — one dedicated API sweep, with resulting fixes scoped separately.
**Status:** swept 2026-08-16; two shipped sites fixed and the remaining occurrences reviewed.

`Find.AnyPlayerHomeMap` returns the first player home map, which is correct only in a single-colony
game. Five sites made this a systematic error class rather than an isolated mistake:

1. **Buyer pickup collection** — its original recorded-map fix is in `CLAUDE.md`.
2. **Mark Ready validation** — `Source/Intercolony/UI/MainTabWindow_Intercolony.cs:3333` uses
   `Find.CurrentMap ?? Find.AnyPlayerHomeMap` instead of the persisted `SalesOrder.fulfillmentMap`,
   part of the taking-side path corrected by the sweep.
3. **Distance computation** — `Source/Intercolony/Market/MarketOpportunityGenerator.cs:377` uses the
   first player home map; this was reviewed and is correct because generation has no order to key on.
4. **Purchase-order delivery and refund sites** — the giving-side fallback corrected by the sweep.
5. **`ProcessBuyerCollections`** — collection could fall back to another colony after the fulfilment
   colony disappeared; this was the fifth site found and the taking-side fallback corrected by the
   sweep.

**Resolved 2026-08-16** (`b6e868e`) at the two shipped fallback sites, with a deliberate asymmetry.
Taking from the player must never substitute a colony: buyer collection now refuses to collect and
fails the order with a reason if its fulfilment colony is gone. Giving to the player may substitute,
but must disclose it: procurement deliveries and refunds can fall back to a surviving colony and
name that colony.

The remaining occurrences were reviewed and are correct. Labor reads `contract.destinationMap`
first and uses `AnyPlayerHomeMap` only as a last resort; market generation has no order to key on;
and debug and self-test files do not ship.

---

## Rejected or superseded

*(nothing yet)*
