# Intercolony — Post-0.9.0 Development Revision Plan

**Purpose:** implementation handoff for the next Intercolony development revision after the first public beta.  
**Current public release:** **v0.9.0**  
**Repository:** `miannoni/Intercolony`  
**Baseline inspected:** `main` at `46fd65b70459724ada347f0a7dff63eab5da8e01`  
**Current RimWorld target:** 1.6  
**Current save schema at this baseline:** **24**  
**Suggested planning label:** *Post-0.9.0 beta revision / candidate next numbered phase*. If the repository has advanced before implementation starts, use the next available phase number rather than assuming “Phase 27”.

---

## 0. Why this revision exists

The first external/family playtests exposed a useful cluster of issues. Some are direct correctness defects, some are missing affordances around systems that already exist, and two are genuine feature expansions.

The scope of this revision is:

1. **Unified inventory reservation / committed quantities**
2. **Inventory refresh**
3. **Pickup ETA**
4. **Stone blocks trading**
5. **Cancel procurement**
6. **Animals trading**
7. **Supply contracts based on supply history**

The goal is **not** to redesign Intercolony. The goal is to close the gap between what the current systems imply to the player and what they actually do, then add two natural extensions: animal trade and history-driven recurring supply agreements.

The strongest design principle coming out of the playtest is:

> Once the player makes an economic commitment, every other Intercolony surface should know that the commitment exists.

A second principle is:

> The world should remember what the player actually did. Long-term commercial opportunities should be a consequence of past trade, not unrelated RNG.

---

# 1. Mandatory execution protocol for Claude

This document is a product/implementation plan, **not an API reference**. Follow the repository's `CLAUDE.md` rules above anything written here.

For every numbered item:

1. **Read the relevant current code before editing it.**
2. **Reproduce or prove the current behavior first.**
3. **Check `reference/decompiled/` and `reference/vanilla-defs/` before using any RimWorld API or XML field not already used by the mod.**
4. **Implement one vertical slice at a time.**
5. **Build after the slice.**
6. **Add or extend a self-test that exercises the real production path.**
7. **Play-test the path that cannot be proven by a self-test.**
8. **Save mid-feature, quit to menu, reload, and verify state.**
9. **Where static/cache state is touched, also start a different colony and make sure state did not leak across games.**
10. **Commit the working slice before starting the next one.**
11. At phase/revision completion:
    - append the actual result to `PROGRESS.md`;
    - add still-unproven human/gameplay paths to `docs/PENDING_PLAYTESTS.md`;
    - update `CLAUDE.md` current-state line;
    - strike/move any picked-up backlog entries according to the repo's existing convention.

Do **not** widen a slice because an adjacent idea is attractive. If an implementation exposes a separate defect, record it and continue only if it blocks the current acceptance criteria.

---

# 2. Repository reality check

Several playtest notes look larger than they are because the underlying code already contains part of the intended behavior.

| Item | What the code already has | What is actually missing |
|---|---|---|
| Inventory commitments | Order validation, physical stock checks, order state machines | A shared notion of stock already committed to another Intercolony sale |
| Inventory refresh | Manual refresh, refresh-on-tab-entry, cached Find Buyer scan | Frequent automatic refresh/reconciliation while the page stays open |
| Pickup ETA | `buyerArrivalTick`, `DaysUntilBuyerArrives`, actual countdown in Orders after `Mark ready` | A pre-acceptance estimate in the **Market** flow and consistent wording |
| Stone blocks | `ThingCategoryDefOf.StoneBlocks` already classifies as `IntermediateGoods` | Must reproduce why playtest could not sell them; may be the stale stock cache rather than classifier |
| Cancel procurement | `PurchaseOrderService.Cancel()` already exists; `PurchaseRequest.TryCancel()` exists | Player-facing UI/actions and confirmation |
| Animals | Explicitly excluded from ordinary goods (`ThingCategory.Pawn`) | A deliberate live-animal trade path |
| Contract history | Settlement reputation and completed-order hooks already exist | Per-good supply memory; contract item selection is currently random |

This matters: **do not rebuild systems that are already there.**

### Architecture rule for this revision

Prefer this order of solutions:

1. **derive from existing authoritative state;**
2. reuse an existing service/state transition;
3. add a narrow read-model/helper;
4. add persisted state only if the first three cannot satisfy an acceptance criterion.

In particular, do not introduce persistence merely to make a query convenient. `SalesOrder` history and order state already answer more of this revision than the initial playtest wording suggests.

---

### Third-pass architecture audit — decisions intentionally locked

This revision has been reviewed against the current code specifically to remove unnecessary machinery.

The following are deliberate constraints, not suggestions:

- **No reservation entity and no order-origin enum** for feature 1. Existing `opportunityId`, `contractId`, status and `RemainingQuantity` are enough for the scoped commitment rule.
- **No WorldComponent/UI invalidation bus** for feature 2. Use the existing local UI-cache pattern and real-time throttling.
- **No persisted supply-history aggregate** for feature 7. Retained `SalesOrder`s are the history source of truth.
- **No schema bump for features 1–5 or 7** unless implementation uncovers a genuinely missing persisted fact.
- **No generalized trade-asset framework for animals.** The animal spike chooses the smallest representation only after verifying vanilla pawn handoff APIs.
- **Buyer-pickup readiness deadline must be coherent before its ETA is advertised.** `AwaitingCollection` should represent that the player already met the readiness deadline.
- **Pending contract renewal/suspension counts as an existing commercial relationship for offer-generation gating.**

If current code has materially changed since this audit, preserve these design reasons rather than mechanically preserving file names or method names.

---

# 3. Recommended development order

The numeric list is retained for traceability, but implementation should follow dependencies:

### Block A — Economic integrity
**1 → 2**

Item 2 should consume the availability model created in item 1. Fixing refresh first would merely refresh the wrong number more frequently.

### Block B — Low-risk beta UX/correctness
**3 → 4 → 5**

Item 4 must be re-tested after item 2 because stale Find Buyer stock may be the entire stone-block symptom.

### Block C — World memory
**7**

This is a contained extension of the existing reputation/contract flow and is lower technical risk than live pawns.

### Block D — Live-animal trade
**6**

Animals should be implemented last in this revision because they are the only item that changes the kind of object being traded from ordinary goods/buildings to living pawns. Start with a technical spike and do not force them through `ThingMaker`/inventory code.

If item 6's spike shows that safe animal handoff requires a significantly larger subsystem than described here, split it into its own subsequent phase rather than destabilizing the completed 1–5/7 revision.

---

# 4. Feature 1 — Unified inventory reservation / committed quantities

**Original estimate:** M–L  
**Priority:** P0 / economic integrity

## 4.1 Player problem

Today Find Buyer answers a physical question:

> “How much of this item is in storage?”

But the player actually needs the economic question:

> “How much of this item is still free for me to promise to someone else?”

Example from playtest:

- colony has 10 units;
- player uses Find Buyer and commits 8 to Buyer A;
- those 8 are still physically in storage;
- Find Buyer still sees 10 and lets the player create another commitment;
- the player can therefore promise the same inventory twice.

A second version is buyer pickup:

- the player marks a pickup order ready;
- the buyer is already travelling for the goods;
- those goods still appear in Find Buyer stock because the physical stacks remain in storage.

This is an integrity issue, not merely a display issue.

---

## 4.2 Design decision: logical commitment, not physical locking

**Do not implement a RimWorld stockpile reservation system.**

Intercolony should not prevent:

- colonists eating committed food;
- bills consuming committed ingredients;
- hauling;
- deterioration;
- destruction;
- the player manually moving/using the item.

That would require invasive patches into unrelated RimWorld systems and would radically change what “accepting an order” means.

Instead implement a **logical Intercolony commitment layer**:

> Goods can remain physically usable by the colony, but once Intercolony has counted them toward a committed sale, Intercolony must stop presenting the same quantity as free for a second sale.

The player can still create a shortfall by consuming committed stock. That is part of the game. The bug is allowing Intercolony itself to knowingly double-promise the same currently available surplus.

---

## 4.3 V1 commitment scope

Do **not** make every open sales obligation reserve current colony stock. That is safer mathematically, but it changes gameplay beyond the playtest request: a Market order accepted for future production would immediately prevent the player from selling today's surplus even if they intend to replenish before the deadline.

The current architecture already carries enough provenance to implement the narrower rule without adding saved state.

For this revision, a sales order counts against **Find Buyer available stock** when either is true:

1. **It is a direct Find Buyer sale and is still open.**
   - In the current production paths, this is identifiable as:
     ```text
     opportunityId == 0 && contractId == 0
     ```
   - Do not use `opportunityId == 0` alone: recurring-contract cycle orders also have no Market opportunity and instead carry `contractId`.
   - A computed helper/property is acceptable; a new persisted order-origin enum is not justified.

2. **It is `AwaitingCollection`.**
   - This means the player explicitly marked a buyer-pickup order ready and a buyer is travelling for it.
   - At that point the stock has been operationally committed even if the order originally came from the Market.

Therefore:

```text
committed = sum(RemainingQuantity)
            for each open SalesOrder of the ThingDef
            where IsDirectFindBuyerSale || status == AwaitingCollection

available = max(0, physical colony stock - committed)
```

This intentionally does **not** reserve:

- an ordinary Market seller-delivery order merely because it was accepted;
- a recurring-contract seller-delivery cycle merely because it exists;
- a Market buyer-pickup order before the player marks it ready.

Those are obligations, but not yet allocations of the colony's current stock.

This matches the two actual playtest complaints:

- a direct Find Buyer sale must not let the same surplus be sold again;
- goods explicitly marked ready for collection must disappear from the free-to-sell pool.

### Known conservative edge case

A direct Find Buyer seller-delivery order remains logically committed after its goods are loaded into a caravan. The goods leave colony storage, while `RemainingQuantity` stays committed until delivery.

That can temporarily understate free colony stock.

Accept that conservative undercount in this revision. Correcting it requires assigning specific caravan cargo to specific orders, which is a separate allocation system and is not justified by the current bug.

---

## 4.4 Data model: keep the existing stock shape

Do not create a second authoritative inventory database, reservation entity, or persisted stock ledger.

Also do not introduce a `StockAvailability` view-model class unless the implementation genuinely needs it. The current Find Buyer UI already consumes a simple:

```text
List<KeyValuePair<ThingDef, int>>
```

Keep that shape if possible.

Recommended layering:

### Existing low-level physical query

Keep:

```text
FindBuyerService.ColonyStock(Map)
```

meaning:

> physical tradeable stock currently in storage.

Do not change that method's semantic meaning because it is a useful primitive.

### New derived Find Buyer query

Add a narrow method conceptually like:

```text
FindBuyerService.AvailableColonyStock(state, map)
```

It should:

1. obtain physical stock from `ColonyStock(map)`;
2. calculate committed quantity from existing `SalesOrder`s using the predicate above;
3. subtract commitments per ThingDef;
4. omit zero/negative available entries;
5. return the same simple def → available-count shape the UI already knows how to render.

A second narrow helper such as:

```text
AvailableQuantity(state, map, def, excludedOrderId = 0)
```

is justified for commitment-time revalidation if it avoids duplicating the calculation.

The optional exclusion matters for `MarkReadyForPickup()`: a direct Find Buyer pickup order is already part of the commitment total before it is marked ready, so readiness must test stock against **other** commitments rather than subtracting the order from itself.

Do not cache this in `IntercolonyWorldComponent`. It is a read model over physical map state plus already-persisted orders.

### Matching granularity

Commit by `ThingDef`, because Find Buyer itself currently groups stock by ThingDef and does not separate material/quality variants in its left-hand stock list.

Do not build a quality/stuff allocation engine in this revision.

## 4.5 Suggested implementation structure

Relevant current files:

- `Source/Intercolony/Orders/OrderValidation.cs`
- `Source/Intercolony/Market/FindBuyerService.cs`
- `Source/Intercolony/Orders/SalesOrder.cs`
- `Source/Intercolony/Orders/SalesOrderService.cs`
- `Source/Intercolony/UI/MainTabWindow_Intercolony.cs`
- `Source/Intercolony/Debug/IntercolonyOrderSelfTest.cs`
- `Source/Intercolony/Debug/IntercolonyMarketSelfTest.cs`

### Step 1 — Add one derived availability calculation

Keep `OrderValidator.IsAvailableColonyStock()` exactly as the lower-level physical test it is today.

Add the commitment calculation beside the Find Buyer stock logic, where the question actually belongs.

The smallest good design is:

- physical stock comes from existing `ColonyStock(map)`;
- direct-stock commitments come from existing `SalesOrder` state;
- Find Buyer availability is derived from the two.

No new persistent owner is created.

### Step 2 — Switch Find Buyer to available stock

Keep the current stock-list UI shape unless a real usability reason appears.

The left-hand count should become:

> quantity available for a **new Find Buyer sale**

rather than raw physical quantity.

A tooltip showing physical / committed / available is optional, not required for correctness. Do not widen the slice just to add accounting detail.

If committed quantity exceeds physical stock, available clamps to zero. A debug dump may expose the shortfall, but the normal UI only needs to avoid offering stock that is not free.

### Step 3 — Revalidate at both commitment boundaries

Never trust the cached UI count.

There are **two** moments when Intercolony newly claims current stock.

#### A. Creating a direct Find Buyer order

Immediately before `SalesOrderService.CreateFromOffer()` binds the new order:

1. recompute live physical stock;
2. compute existing stock-backed commitments;
3. compare available quantity against requested quantity;
4. if insufficient, reject with an actionable message;
5. create nothing and take no state transition.

Example:

> Only 6 rice are still available for a new sale; 1,200 are already committed.

#### B. Marking a buyer-pickup order ready

`MarkReadyForPickup()` is also a commitment boundary for Market-origin pickup orders.

Before changing status to `AwaitingCollection`:

1. keep the existing `OrderValidator.ValidateColony()` check for the order's quality/stuff/condition requirements;
2. compute def-level available stock against **other commitments**, excluding this order's own ID;
3. require at least `RemainingQuantity` to be free;
4. if not, refuse Mark Ready with a message explaining that some matching stock is already committed elsewhere;
5. only then transition to `AwaitingCollection`.

The exclusion is required because a direct Find Buyer pickup order is already stock-backed from creation; it must not fail readiness by counting its own commitment twice.

This closes both double-commit routes without storing reservations.

### Step 4 — Let existing order state release commitments automatically

Because commitment is derived rather than stored:

- direct Find Buyer order accepted → committed;
- direct Find Buyer partial delivery → only `RemainingQuantity` remains committed;
- Market buyer pickup before Mark Ready → not committed;
- buyer pickup after Mark Ready / `AwaitingCollection` → committed;
- Completed / Failed / Cancelled → contributes zero.

No reserve/release calls belong in `SalesOrderService`.

`SalesOrderService` remains the owner of transitions; Find Buyer availability remains a read model over those transitions.

### Step 5 — Remove outdated UI copy

The Find Buyer confirmation currently says, in substance:

> “Your stock is not reserved…”

That becomes misleading.

Replace it with wording that explains the actual V1 semantics:

> “This quantity is committed against your available Intercolony stock, so it will not be offered to another buyer. The goods are not physically locked; your colony can still consume or move them, and you remain responsible for having them at fulfillment.”

### Step 6 — Add debug visibility

Add a debug dump or extend an existing one so a report can show:

```text
Rice
  physical: 2200
  committed: 700
  available: 1500

Steel
  physical: 320
  committed: 500
  available: 0
  shortfall: 180
```

This will make future inventory bugs dramatically easier to diagnose.

---

## 4.6 Tests

### Self-test cases

1. 10 physical, no orders → 10 available.
2. Direct Find Buyer order for 8 → 2 available.
3. Attempt second Find Buyer order for 3 → rejected at binding.
4. Attempt second order for 2 → succeeds.
5. Cancel first direct order → its remaining quantity becomes available again.
6. Partial delivery of a direct order reduces its committed amount.
7. Completed/failed/cancelled direct orders contribute zero.
8. Market seller-delivery order merely accepted → does **not** consume Find Buyer availability.
9. Recurring-contract seller-delivery cycle → does **not** consume Find Buyer availability merely by existing.
10. Market buyer-pickup order before Mark Ready → does not consume availability.
11. With 10 physical and 8 already committed elsewhere, a Market pickup order for 8 cannot be marked ready.
12. With 10 physical and no competing commitment, that Market pickup can be marked ready and then consumes 8 availability.
13. A direct Find Buyer buyer-pickup order does not block its **own** Mark Ready check; readiness excludes its own ID from competing commitments.
14. Two Market pickup orders can coexist unready, but with only enough free stock for one, the first Mark Ready succeeds and the second is refused.
15. Physical stock may fall below a direct/ready commitment; available clamps to zero without exception.

### Manual playtest

- Start with a known rice stack.
- Create two direct sales and verify the second cannot exceed what remains.
- Consume some committed rice with normal colony activity.
- Verify Intercolony shows a shortfall rather than inventing stock.
- Cancel an order and see stock free immediately after the refresh behavior from feature 2.

### Save/load

Save with:

- at least one direct Find Buyer order open;
- one marked-ready pickup order;
- physical stock below at least one commitment.

Reload. Availability must derive to the same numbers without a dedicated reservation save object.

---

## 4.7 Definition of done

- The same physical units cannot be committed twice through Find Buyer.
- Marked-ready pickup quantities stop appearing as free stock.
- `MarkReadyForPickup()` cannot claim stock already committed to another order.
- A direct pickup order does not double-count its own commitment during Mark Ready.
- No vanilla stockpile/recipe behavior is patched or blocked.
- Both direct-sale creation and readiness transitions recheck live availability.
- Cancellation/completion/failure automatically free logical commitment.
- Debug output explains physical vs committed vs available.
- Save/load produces the same availability from existing persisted orders.

**Suggested commit:**  
`fix: stop Find Buyer from double-committing colony stock`

---

# 5. Feature 2 — Inventory refresh

**Original estimate:** S–M  
**Priority:** P0 visible bug  
**Depends on:** Feature 1

## 5.1 Player problem

The Find Buyer page deliberately caches colony stock because a full map scan every GUI frame is too expensive.

Current behavior already:

- scans on page entry;
- scans when the player presses **Refresh**;
- otherwise leaves the cache alone.

The playtest observed a stale count: roughly 1,500 rice shown while 2,200 existed, then a later refresh showed the full amount.

Once feature 1 exists, stale data becomes worse because the page can also display stale **commitment** availability.

---

## 5.2 Design goal

The page should feel live without becoming a per-frame map scan.

Target experience:

> If storage changes while the Find Buyer page remains open, the displayed availability corrects itself within a few seconds, and immediately after an Intercolony transaction that changes commitments.

Keep the manual Refresh button as an instant override and debugging affordance.

---

## 5.3 Implementation steps

### Step 1 — Centralize stock-page refresh in one method

The UI should have one method that:

1. rebuilds the availability snapshot;
2. records when it was refreshed;
3. reconciles the selected ThingDef;
4. invalidates buyer offers if the selected available quantity changed.

Do not scatter `stockCache = null` and selection repair across multiple new call sites if one helper can own the behavior.

### Step 2 — Add throttled automatic refresh while Find Buyer is visible

Do **not** scan on every Layout/Repaint pass.

Prefer a **real-time throttle**, not an in-game-tick throttle.

The repository already uses `Time.realtimeSinceStartup` in `IntercolonyDebugWindow` to rebuild expensive UI text at a bounded real-time frequency. Reuse that local pattern rather than inventing another timing mechanism.

Why real time is preferable here:

- game speed 3 should not triple the number of stock scans per wall-clock second;
- pausing should not make the freshness mechanism conceptually depend on game time;
- this is UI-cache freshness, not world simulation.

Start with a conservative interval around 1–2 real seconds, then keep or adjust it based on the existing performance profile / a developed-colony measurement.

The requirement is:

- stale stock corrects itself quickly enough to trust;
- the map is not scanned per frame.

### Step 3 — Reconcile the current selection

After a refresh:

If selected def still has available stock:

- update `selectedStockCount`;
- clamp `sellQuantity`;
- invalidate `findBuyerCache` if the count changed.

If selected def is now unavailable:

- clear `selectedStockDef`;
- clear selected quantity;
- clear buyer offers;
- show the normal “select something” empty state.

This is essential. Merely rebuilding the left-hand list while leaving the right side priced against the old `selectedStockCount` would preserve the bug in another form.

### Step 4 — Do not add a cross-system invalidation bus

Keep cache invalidation local to the existing UI.

For actions initiated by the Find Buyer UI itself, invalidate/rebuild its local cache after the action succeeds.

For changes that happen elsewhere — order cancellation on another page, hourly buyer collection, normal colony consumption/production — the throttled automatic refresh is the reconciliation mechanism.

Do **not** add:

- a `stockAvailabilityRevision` field to world state;
- a general event bus;
- domain-service references to the UI.

The existing architecture does not need them for this problem.

### Step 5 — Keep manual Refresh

Change tooltip wording from:

> “Stock is not tracked live…”

to something accurate, e.g.:

> “Refresh now. Stock also updates automatically at a throttled interval.”

---

## 5.4 Performance guard

Before and after implementation, time the stock rebuild on a developed colony.

Acceptance target is not a magic millisecond number; it is:

- no per-frame rebuild;
- no visible frame hitch under the tested environment;
- no runaway allocations while the page is open.

If the periodic full scan is measurably expensive, optimize the **scan** rather than returning to stale data. Reuse RimWorld lister indexes where they can preserve minified-item support and correctness.

Do not introduce a stale long-lived inventory database just to avoid scanning.

---

## 5.5 Tests

1. Open Find Buyer with 1,500 rice.
2. Spawn/add another 700 to storage while page stays open.
3. Display updates to ~2,200 without changing tabs or pressing Refresh.
4. Remove/consume stock; number falls.
5. Create a commitment; available count falls immediately.
6. Cancel it; available rises.
7. Selected item disappears entirely; selection is cleared cleanly.
8. Buyer offer totals are recomputed from the new quantity.
9. Performance profile confirms refresh is throttled.

**Suggested commit:**  
`fix: keep Find Buyer stock current without per-frame scans`

---

# 6. Feature 3 — Pickup ETA in the Market flow

**Original estimate:** S  
**Priority:** quick UX win

## 6.1 Current reality

The domain already has:

- `SalesOrder.buyerArrivalTick`;
- `SalesOrder.DaysUntilBuyerArrives`;
- an actual buyer-arrival countdown in the Orders row after goods are marked ready.

`SalesOrderService.MarkReadyForPickup()` already computes travel time from settlement distance and sends a letter with the approximate number of days.

The missing information is **before the player accepts the order**.

In the Market table, a Buyer Pickup listing currently shows essentially “collected” in the timing/deadline column, which tells the player *who moves the goods* but not *how long collection is likely to take*.

---

## 6.2 Design goal

Before accepting a Buyer Pickup opportunity, the player should understand both clocks:

1. **their readiness obligation** — how long they have to get the goods ready;
2. **the buyer's travel estimate** — approximately how long the buyer takes after readiness.

Do not invent a second ETA formula in the UI.

---

## 6.3 Implementation steps

### Step 1 — Extract the current travel estimate

The logic currently embedded in `MarkReadyForPickup()` should become one shared method, conceptually:

```text
EstimateBuyerPickupTravelDays(distanceTiles)
```

Use that same method for:

- actual `buyerArrivalTick` creation;
- Market display;
- acceptance confirmation;
- tooltips.

Do not duplicate the formula in UI code.

### Step 2 — Resolve the existing deadline contradiction

The current player-facing copy says a buyer-pickup order must be **marked ready within the deadline**.

The state machine currently does something different: `FailOverdue()` fails every open order, including `AwaitingCollection`, so a buyer already travelling can make the order fail simply because their trip extends past the readiness deadline.

Make the implementation match the promise already presented to the player:

> For Buyer Pickup, the deadline is the deadline to **declare the goods ready**. Once the order transitions to `AwaitingCollection`, the buyer's travel time no longer causes deadline failure.

Minimal implementation:

1. Before `MarkReadyForPickup()` changes state, reject/fail a buyer-pickup order whose deadline has already passed.
2. `FailOverdue()` must continue to fail:
   - seller-delivery orders still not completed by deadline;
   - buyer-pickup orders still in `Accepted` because they were never marked ready.
3. `FailOverdue()` must **not** fail an `AwaitingCollection` order merely because `deadlineTick` passed while the buyer was travelling.
4. If the promised goods disappear before the buyer arrives, the existing collection-time validation/failure remains the enforcement mechanism.

Do not extend the deadline or rewrite `deadlineTick`. The state transition itself records that the player met the readiness deadline.

This is a narrow correctness fix required to make the ETA wording truthful, not a new contract system.

### Step 3 — Fix Market timing text

For Buyer Pickup listings, replace the non-informative “collected” value with a compact estimate.

Possible compact cell:

```text
~5d pickup
```

The tooltip should explain both clocks:

```text
Mark ready within 12 days.
Once marked ready, this buyer is expected to take about 5 days to arrive.
```

For Seller Delivery, retain the existing delivery-deadline behavior.

### Step 4 — Fix confirmation wording

The acceptance dialog should say:

```text
You must mark the goods ready within 12 days.
After that, the buyer is expected to arrive in about 5 days.
```

Do not say “deliver within 12 days” for a buyer-pickup order.

### Step 5 — Preserve the actual countdown after readiness

The Orders page already displays the live arrival countdown.

Once `buyerArrivalTick` exists:

- it is authoritative;
- do not recalculate a new estimate every frame;
- do not display the readiness deadline as if it were still outstanding.

### Step 6 — Sentinel discipline

Never format `buyerArrivalTick == -1` as a date or duration.

Only show an actual arrival countdown while `BuyerEnRoute` is true.

---

## 6.4 Tests

- Near buyer → short estimate.
- Distant buyer → longer estimate.
- Unknown distance → fallback shown in Market equals the formula used at dispatch.
- Market estimate and dispatch letter agree.
- Buyer pickup marked ready **before** the deadline does not fail while the buyer travels past that deadline.
- Buyer pickup first marked ready **after** the deadline is refused/failed; it cannot use `AwaitingCollection` to escape expiry.
- Seller-delivery order still fails normally after its deadline.
- Buyer-pickup order that was never marked ready still fails after its deadline.
- Goods missing when the buyer arrives still trigger the existing collection failure.
- Save/reload while buyer is en route preserves actual arrival ETA and remains immune to the already-satisfied readiness deadline.

**Suggested commit:**  
`fix: make buyer-pickup ETA and readiness deadline coherent`

---

---

# 7. Feature 4 — Stone blocks trading

**Original estimate:** S–M  
**Priority:** quick win, but diagnosis first

## 7.1 Important finding

Do **not** start by adding stone blocks to the classifier.

The current classifier already explicitly places `ThingCategoryDefOf.StoneBlocks` in `IntermediateGoods`.

The default blacklist also does not intentionally exclude stone blocks.

Therefore the playtest report:

> “Can't sell stone blocks”

must first be reproduced and traced.

There is a realistic chance this was another manifestation of feature 2: blocks produced after the Find Buyer cache was built would not appear until refresh.

---

## 7.2 Diagnosis sequence

### Step 1 — Reproduce with a known vanilla stone block

Use a normal stored stack such as one of the vanilla block defs present in the installed 1.6 definitions.

Verify, using the actual definitions rather than memory:

- market value passes the minimum;
- `tradeability.PlayerCanSell()` passes;
- category ancestry reaches `StoneBlocks`;
- not blacklisted;
- `IsFungibleTradeItem()` returns true;
- the stack is in storage.

### Step 2 — Check classifier/debug histogram

Prove whether the def appears in `IntercolonyProductClassifier.TradableDefs`.

If yes, do not edit classifier rules.

### Step 3 — Check Find Buyer stock enumeration

With the feature-2 refresh path active:

- physical stack exists;
- availability snapshot contains it;
- it appears in the stock list.

If it now works, close this playtest item as a regression test covered by the inventory refresh fix.

### Step 4 — Check Find Buyer demand path

If stock appears but nobody can buy:

- trace `FindBuyerService.FindBuyers()`;
- distinguish “the item is not tradeable” from “current settlements have no interest”.

“No current interest” is valid gameplay. The UI must make that distinction legible.

### Step 5 — Check Market opportunity generation separately

The player report was “can't sell”, but also verify stone blocks are eligible to appear in generated demand.

Because opportunity generation is probabilistic, use debug/self-test entry points rather than waiting for a random listing.

---

## 7.3 If a real classifier defect is found

Fix it definition-first.

Do **not** add hardcoded `BlocksGranite`, `BlocksMarble`, etc. lists.

The existing design deliberately supports modded content through category/def behavior. Preserve that.

If the failure reason is difficult to inspect, a small debug method such as “explain tradability” is preferable to guessing. It should say the first gate that rejected a def:

```text
BlocksGranite
- market value: pass
- player can sell: pass
- blacklist: pass
- category: StoneBlocks
- fungible item: pass
```

That debug affordance will pay for itself on future modded-item reports.

---

## 7.4 Regression tests

At least one StoneBlocks def must:

- classify as `IntermediateGoods`;
- be considered fungible;
- appear in stored Find Buyer stock;
- survive the availability/commitment layer;
- produce normal buyer evaluation.

Also test a modded/alternate stone-block def if one is available in the local test environment, but do not claim broad compatibility from one example.

**Suggested commit if code changes:**  
`fix: restore stone block trading`

If the issue is fully resolved by feature 2 and only a regression test is needed:

`test: cover stone blocks in Find Buyer stock`

---

# 8. Feature 5 — Cancel procurement

**Original estimate:** M in playtest; likely S–M against current code  
**Priority:** expected player control

## 8.1 Current reality

There are two different things the player may mean by “cancel procurement”:

### A. Purchase Request / RFQ
No binding purchase yet.

The domain already has `PurchaseRequest.TryCancel()`.

### B. Accepted Purchase Order
Silver has already been paid and a supplier is preparing/delivering goods.

The domain already has `PurchaseOrderService.Cancel()`.

Its current rule is explicit:

> cancellation forfeits the payment because the supplier already produced/committed the goods.

It also records a procurement cancellation in commercial reputation.

The missing piece is primarily the player-facing action.

---

## 8.2 Design rule

These two cancellations must not look equivalent.

### Withdraw RFQ

- no goods bought;
- no silver lost;
- no breach;
- simply closes the request and its quotes.

### Cancel accepted purchase

- irreversible;
- already-paid silver is forfeited under current rules;
- goods will no longer arrive/be collectible;
- commercial reputation records the cancellation.

The UI must say this **before** the player confirms.

---

## 8.3 Implementation steps

### Step 1 — Inspect existing request UI

Before adding anything, verify whether `DrawRequestBlock` already exposes `TryCancel()`.

If absent:

- add **Withdraw request** only when request is open;
- confirmation can be light because no money is lost;
- call the existing transition, not `request.status = ...` from UI.

If the UI currently mutates status directly anywhere, route it through the authoritative transition while touching this slice.

### Step 2 — Add Cancel to open Purchase Order rows

Current Procurement “On order” rows show status but no action.

Add a clear action for any order where `order.IsOpen` is true.

Do not make the row so cramped that the status/ETA becomes unreadable at the tested 1.75x UI scale. Measure if needed.

### Step 3 — Confirmation must show the actual consequence

Example:

```text
Cancel purchase #123?

You already paid 4,850 silver.
Cancelling now forfeits the full payment and the goods will not be delivered.

This also counts as a cancelled purchase in your trading record with Red Creek.
```

Buttons:

- Keep order
- Cancel purchase

Use destructive confirmation styling/pattern already used elsewhere in the mod.

### Step 4 — Call existing domain service and give immediate feedback

Use `PurchaseOrderService.Cancel(order)`.

Do not duplicate:

- status transition;
- outcome note;
- reputation bookkeeping;
- logging.

One small gap matters: the current Procurement view only renders **open** purchase orders. A cancelled purchase therefore disappears from that active list immediately.

Do **not** turn this slice into a purchase-order history redesign.

Instead ensure the cancellation path gives an explicit player-visible message such as:

> Purchase #123 cancelled. 4,850 silver was forfeited.

Put the message in the authoritative cancellation path if practical so every caller gets the same feedback.

### Step 5 — Verify order advancement ignores cancelled orders

`AdvanceOrders()` should already do this through status checks. Add a regression assertion so a cancelled:

- supplier-delivery order never spawns goods;
- pickup order never becomes collectable/refunds later.

### Step 6 — Preserve history

Cancelled requests/orders should remain visible according to the existing retention policy, with `outcomeNote` explaining what happened.

Do not delete them from world state just to remove them from the active list.

---

## 8.4 Tests

### RFQ
- create request;
- withdraw before accepting;
- quotes can no longer be accepted;
- no silver/reputation effect.

### Confirmed supplier delivery
- buy;
- cancel before arrival;
- silver stays spent;
- no goods arrive later;
- cancellation recorded.

### Ready for player pickup
- let goods become ready;
- cancel;
- silver forfeited;
- caravan can no longer collect;
- no later refund timer fires.

### Save/load
Save a cancelled PO and an open PO. Reload and verify only the open one advances.

**Suggested commit:**  
`feat: expose procurement cancellation in the player UI`

---

# 9. Feature 7 — Supply contracts should come from supply history

**Original estimate:** L  
**Priority:** major world-reactivity improvement

> Implement this before animals unless another dependency appears. Keep animal history out of recurring contracts in this revision.

## 9.1 Player problem

Current recurring supply agreements are gated by settlement reputation, which is good, but once a settlement qualifies the contract item is selected from a broad list of tradable stackable goods.

That creates incoherent outcomes:

> The player repeatedly supplies meat to Settlement A, proves they can reliably deliver meat, and Settlement A responds by offering a recurring contract for clothing or weapons.

The playtest expectation is stronger and more intuitive:

> A settlement should ask for a standing agreement in something the player has successfully supplied to **that settlement** before.

This makes recurring contracts a consequence of behavior.

---

## 9.2 Design rule

For this revision:

### Exact-good history, not category history

If the player supplied **meat**, history can create a meat contract.

Do not silently generalize that into:

> “You sold food, therefore they trust you to supply rice.”

That may become a future expansion, but the current point is to make the contract causally legible.

### Successful transactions matter

Failed/cancelled orders must not establish supply competence.

Any completed delivery counts as completed exact-good history. Overall reliability remains the job of the existing `CommercialReputation` system; do not create a second per-good reliability model here.

### No history = no new standing supply offer

If a settlement has high commercial reputation but no eligible successful supply history, it should simply not generate a recurring supply agreement yet.

Do not fall back to random goods, because that recreates the playtest problem.

---

## 9.3 Data model: do not add one

The repository already persists the transaction history needed for this feature.

`IntercolonyWorldComponent` deliberately retains completed and failed `SalesOrder` objects instead of deleting them. Those orders already contain:

- settlement ID;
- ThingDef;
- quantity;
- status;
- delivered quantity;
- payment;
- contract ID / opportunity traceability where relevant.

Therefore the first implementation should derive supply history from `state.Orders`.

Do **not** add:

- `SupplyHistoryEntry`;
- a second persisted per-good history list;
- new Scribe fields;
- a save-schema bump for this feature;
- migration/backfill code.

A persisted aggregate beside the persisted orders would create two sources of truth and a new failure mode: “history says four deliveries, retained orders say three”.

### Transient aggregation

When contract offers are evaluated, build one transient aggregation of **completed** sales orders:

```text
(settlementId, ThingDef) -> completed order count
```

Optionally also aggregate:

- units delivered;
- total silver paid;

only if they are actually used by selection or UI.

Build the aggregation once per `OfferContracts()` pass, not once per settlement, so the cost is O(orders + settlements), not O(orders × settlements).

There is no need to persist the aggregate because the underlying orders already survive save/load.

### Reliability stays where it already lives

Do not reconstruct per-good “on-time” history unless the current order model actually persists enough information to do that reliably.

The existing `CommercialReputation` already captures overall reliability and is already the gate for standing agreements. Use:

- **completed exact-good history** to answer “what do they know us for supplying?”;
- **existing commercial reputation** to answer “do they trust us enough for a standing agreement?”.

That separation uses the current architecture instead of duplicating reputation logic.

---

## 9.4 Eligibility rule for the first implementation

Keep it legible.

Suggested starting threshold:

> At least **2 completed orders of the exact good** to that settlement.

Why two:

- one sale proves almost nothing;
- two establishes repeat behavior;
- four successful meat deliveries, the playtest example, will unquestionably qualify.

Do not add an item-specific on-time/reliability calculation in this revision. The current retained order does not persist a final completion tick, while `CommercialReputation` already owns the overall reliability question.

Use the existing split:

- exact-good completed history → **what** this settlement knows the colony for supplying;
- existing commercial-reputation threshold → **whether** it trusts the colony enough for a standing agreement.

If gameplay later shows two is too permissive, change the single threshold constant. Do not replace it with a confidence framework.

---

## 9.5 Contract-offer liveness guard

Before changing candidate selection, preserve the existing invariant stated in `ContractService`:

> one live proposal or agreement per settlement.

The current `IntercolonyWorldComponent.HasContractWith()` only checks `IsOffer || IsActive`.

That misses two states that are still live relationships:

- a `Completed` contract with `renewalOffered == true`;
- a `Suspended` contract.

This matters because `DoRefresh()` calls `AdvanceContracts()` and then `OfferContracts()` in the same refresh. A contract can complete, immediately receive a renewal offer, then still look absent to `HasContractWith()` and become eligible for a second unrelated offer.

Narrow fix:

- make `HasContractWith()` treat pending renewal and suspended agreements as existing/live for offer-generation purposes;
- add a regression test.

Do not redesign the contract state machine. This is a guard required before making offer generation more history-aware.

---

## 9.6 Contract item selection

Current `ContractService.BuildOffer()` selects from all tradable stackable candidates.

Replace that candidate source with the settlement's eligible supply-history entries.

Then filter again against current reality:

- ThingDef still exists;
- still tradeable by Intercolony;
- still a stackable item suitable for recurring contracts;
- not blacklisted now.

This protects saves where a content mod was removed or a blacklist changed.

### Candidate weighting

If only one good qualifies, use it.

If several qualify, use deterministic seeded weighted randomness based primarily on repeat history.

Simple first rule:

```text
weight = completedOrders
```

A good supplied five times should be more likely than one supplied twice.

Before consuming seeded randomness, sort eligible candidates by a stable key such as `ThingDef.defName`. Do **not** rely on dictionary enumeration order: the same saved history must produce the same candidate sequence before the seeded choice is applied.

Do not let total silver dominate so expensive weapons automatically erase repeated commodity relationships.

Use the existing seeded contract-offer random state so save/load/reopening cannot reroll the answer.

---

## 9.7 Player-facing causality

The offer should tell the player **why this appeared**.

Example:

```text
Red Creek has bought meat from you 4 times and now wants a standing supply agreement.
```

This is more valuable than a hidden history algorithm because it teaches the system:

> what I do with a settlement changes what they ask from me later.

The Relations screen may eventually deserve a full trade-history breakdown, but that is not required for this slice.

A tooltip/debug line is enough for the revision if adding a whole new Relations panel would widen scope.

---

## 9.8 Existing beta saves

No migration should be required for this feature if history remains derived from retained orders.

A v0.9.0/schema-24 save already contains its retained `SalesOrder` history. After loading, the same transient aggregation should immediately produce the same eligible goods.

Completed recurring-contract cycle orders may count as real supply history as well. They are actual fulfilled sales to that settlement. Do not special-case them out unless gameplay later shows that completed contracts dominate future variety too strongly.

This is preferable to a migration:

- no new authoritative state;
- no backfill pass;
- no double-counting risk;
- old saves gain the behavior automatically.

Only introduce persistence here if implementation proves that the retained orders genuinely lack information required by an acceptance criterion. If that happens, stop and justify the schema change before adding it.

---

## 9.9 Debug tooling

Add a compact dump:

```text
Red Creek — supply history
  Raw meat
    completed: 4
    units: 2,200
    value: 8,450
    contract eligible: yes

  Cloth
    completed: 1
    contract eligible: no (needs 2)
```

Add a debug route to force a contract offer using the **real** candidate selection path.

Do not create a fake contract object in the test and then claim selection works.

---

## 9.10 Tests

1. Settlement A: four completed meat orders → meat qualifies.
2. Settlement A: no clothing sales → clothing cannot be offered.
3. Settlement B has separate history.
4. Failed meat order does not increase completed history.
5. Cancelled order does not increase it.
6. One completed meat order → no contract yet.
7. Two completed meat orders → eligible.
8. Meat 4 / rice 2 → meat is more likely across controlled seeds.
9. Candidate list is stably ordered before seeded selection.
10. Removed/blacklisted def is filtered from candidates.
11. No eligible history → no contract offer, not random fallback.
12. Pending renewal makes `HasContractWith()` true for offer-generation purposes.
13. Suspended agreement makes `HasContractWith()` true for offer-generation purposes.
14. v0.9.0-style retained completed orders participate immediately without migration.
15. Save/reload preserves the same derived candidate set and seeded result.

### Existing contract self-test must not lose coverage

`IntercolonyContractSelfTest` currently calls the real `ContractService.BuildOffer()` to prove that contract terms beat spot price. Today it can do that without any historical prerequisites.

After history-gating is introduced, a no-history test world would correctly make `BuildOffer()` return null, and the existing test would silently print “no contract candidates; price check skipped”. That would weaken coverage.

Update the fixture instead of accepting the skip:

1. choose one valid recurring-contract good;
2. add the minimum number of temporary **Completed `SalesOrder`** records for that same settlement/good to `state.Orders`;
3. call the real `BuildOffer()` production path;
4. run the existing price/term assertions;
5. remove every temporary history order during cleanup.

Do not create a fake “eligible=true” test-only bypass in `ContractService`.

### Manual playtest

Start with a settlement that has no history.

- Complete repeated sales of one good.
- Build reputation above the standing-agreement threshold.
- Force/await contract offer.
- Confirm the offered good is one the settlement actually bought from the colony.
- Confirm offer copy references that history.

**Suggested commit:**  
`feat: base supply agreements on proven trade history`

---

# 10. Feature 6 — Buy and sell animals

**Original estimate:** M–L  
**Risk:** highest in this revision

## 10.1 Scope statement

Support **animals**, not humans.

This revision must not accidentally become the human/slave trading system. Human pawns, prisoners, employees and colonists remain out of scope.

Likewise, do not add:

- recurring livestock contracts;
- breeding contracts;
- genetic guarantees;
- training guarantees;
- animal auctions;
- animal-specific relationship systems.

The first target is simply:

> The player can intentionally procure ordinary animals from settlements and sell ordinary animals through Intercolony without treating them like stackable items.

---

## 10.2 Why this is not “remove the Pawn exclusion”

Animals are live `Pawn` objects.

Current goods systems assume things such as:

- `ThingDef` classification;
- colony storage;
- stack counts;
- `CaravanInventoryUtility.AllInventoryItems`;
- `ThingMaker.MakeThing`;
- minified wrappers;
- destroying/splitting stacks on handoff.

Those assumptions are wrong for animals.

Therefore:

> Do not route a live pawn through ordinary item removal/spawn logic just because `Pawn` inherits from `Thing`.

---

## 10.3 Stage 6A — Mandatory RimWorld API spike

**Architecture gate:** do not decide before the spike whether animals should extend `SalesOrder` / `PurchaseOrder`, use a subject discriminator, or use narrow animal-specific order entities. The current goods model is strongly item-shaped, and choosing a representation before verifying the vanilla pawn handoff paths would be architecture by guesswork.

The spike must compare the smallest viable options against the current code and explicitly recommend the one with the least new state and fewest special cases. Do not build a generic “trade asset” framework.

Before production implementation, inspect RimWorld 1.6 decompiled code and vanilla trade/caravan behavior.

Write `docs/ANIMAL_TRADE_SPIKE.md` with the exact verified answers.

Questions the spike must settle:

1. How does vanilla identify an ordinary animal eligible for trade?
2. What stable definition best represents a species in an order:
   - race `ThingDef`,
   - `PawnKindDef`,
   - or both?
3. How does vanilla compute a live animal's trade/market value?
4. How are animals represented in a caravan?
5. What is the safe vanilla path for removing/selling an animal from a caravan?
6. What is the safe path for adding a newly acquired animal to a caravan?
7. What is the safe path for creating and spawning a purchased animal at the colony?
8. Which faction should the pawn have before and after handoff?
9. Which temporary/quest/lodger states must be excluded?
10. What happens to bonds/relations when a player voluntarily sells an animal?
11. Can one species map to multiple PawnKindDefs, and if so which identity must the quotation lock?

Do not guess method names. Cite verified local source paths in the spike.

### Decision gate

After the spike, choose the smallest representation that preserves species identity.

Preferred if valid:

- reuse race `ThingDef` where it uniquely represents the species;
- add `PawnKindDef` only where generation requires it.

If that is not stable, store explicit `PawnKindDef` in animal quotations/orders.

Do **not** redesign every existing goods entity into a universal polymorphic trade framework unless the spike proves it necessary.

Do not treat “animals are the second subject type” as sufficient reason by itself to add a discriminator to existing persisted order entities. Reuse the existing entities only if the spike shows that their lifecycle and validation semantics remain coherent after the extension; otherwise a narrow parallel vertical slice may be safer.

---

## 10.4 V1 feature surface

To constrain risk, implement animal trade through the two explicit “I want X” workflows first:

### Buy animals
**Procurement / RFQ**

The player asks for an animal species.

### Sell animals
**Find Buyer**

The player chooses a species/eligible animal stock and looks for interested settlements.

Do **not** add random animal listings to the general Market generator in the first animal slice.

Do **not** add animals to recurring supply contracts in feature 7.

---

## 10.5 Selling animals — recommended first implementation

### Physical execution rule

For the initial implementation, support **seller delivery by caravan** for animal sales.

Reason:

- the player explicitly decides which animals to put into the caravan;
- no code has to automatically choose/destroy a bonded/trained/pregnant animal from the home map;
- the delivery site can validate actual animals physically present in the caravan.

Buyer pickup for live animals can be added later once there is a safe explicit animal-selection/collection interaction.

If product direction requires buyer pickup in this same revision, it must include a selection dialog for exact animals. Never auto-pick “any two muffalo” from the colony.

### Stock/search UI

The Find Buyer page can have a separate **Animals** subsection/mode rather than pretending animals are stored goods.

List eligible owned animals by species and count.

Example:

```text
Muffalo            6
Alpaca             3
Husky              2
```

The count is informational. The actual animals delivered are validated at caravan handoff.

### Eligibility

At minimum:

- live;
- animal race;
- currently controlled/owned by player according to verified vanilla rules;
- not humanlike;
- not employee/quest lodger/temporary pawn.

Do not invent extra exclusions without checking vanilla trade behavior.

### Buyer evaluation

Reuse settlement access, distance, wealth and demand concepts where practical, but keep animal appetite separate from stackable-goods quantity formulas.

A settlement wanting:

> 2 muffalo

should not derive from a “silver budget / stackLimit 75” commodity rule.

Use small head-count ranges bounded by settlement wealth/supply profile.

### Price

Use the same economic philosophy as Intercolony but start from the **vanilla-appropriate live animal value**, verified in the spike.

Do not price an animal by a race ThingDef's arbitrary base value if vanilla has a pawn-aware market value that accounts for age/health/training.

For a direct sale quotation, price may be based on the actual eligible animals or on a disclosed per-head baseline. Pick one and make UI consistent.

### Delivery validation

Animal sales orders need a validation branch that scans caravan pawns, not inventory items.

At delivery:

- count eligible animals of the promised species;
- if enough exist, hand over the requested number using a verified vanilla-safe path;
- pay after successful handoff;
- update normal Intercolony commercial reputation;
- never split/destroy a Pawn as if it were a stack.

---

## 10.6 Procuring animals

### RFQ input

Allow the player to switch the procurement request from Goods to Animals.

The request should ask for:

- species;
- quantity;
- desired timing;
- fulfillment preference if both modes can be safely supported.

Do not show material/quality controls for animals.

### Supplier response

A settlement may:

- not supply the species;
- offer fewer animals than requested;
- charge a per-head price;
- offer a lead time.

Keep scarcity meaningful.

### What exactly is promised?

The UI must state the V1 guarantee.

Recommended simple guarantee:

> healthy ordinary animals of the quoted species, with no promise of training, special genetics or sex unless those properties are explicitly stored on the quote.

Do not imply the player is buying a specific legendary/bonded/trained animal if the order only stores a species.

### Generation

Use the RimWorld generation path verified in the spike.

Do not use `ThingMaker.MakeThing` for a pawn.

For supplier delivery:

- create the promised animals safely;
- assign them to the player through the verified vanilla path;
- spawn/arrive them in a sensible colony location;
- complete the PurchaseOrder only after successful handoff.

For player pickup:

- add them to the caravan through the verified caravan-pawn path;
- if the caravan cannot safely accept the animals, keep the order collectible rather than deleting paid-for value.

---

## 10.7 Persistence

If current order entities gain an animal subject field, this is a save-shape change.

Baseline before this revision was schema 24; use the actual current schema number at implementation time and add a migration.

Old goods orders must load unchanged.

Animal orders must survive:

- request with quotes;
- accepted purchase before ready;
- ready-for-pickup;
- sales order before delivery.

Do not deep-save generated supplier animals before they are actually handed over unless the spike proves that is the correct model. Prefer storing the promise and generating at fulfillment if that matches the guarantee.

---

## 10.8 UI rules

Animals must be visibly distinct from goods.

Examples:

```text
2x Muffalo
Live animals — seller delivery required
```

Procurement quote:

```text
3x Alpaca
420 silver each
Ready in 4 days
Healthy ordinary animals; sex/training not guaranteed
```

Do not show:

- stuff/material;
- item condition floor;
- stack terminology;
- “in storage”.

---

## 10.9 Tests

### Classification/eligibility
- ordinary animal eligible;
- human colonist rejected;
- employee rejected;
- prisoner/human rejected;
- dead animal/corpse rejected.

### Sell
- Find Buyer can list an owned species.
- Create seller-delivery animal order.
- Caravan with correct animals validates.
- Caravan without enough rejects with clear reason.
- Successful handoff removes/transfers exact live animals safely and pays once.
- Partial animal delivery behavior is explicitly decided and tested; simplest is reject partial in V1 unless existing order semantics can support it safely.

### Procure
- Create animal RFQ.
- Some settlements can decline.
- Quote locks species and head count.
- Accept purchase deducts correct silver.
- Supplier delivery creates the correct species.
- Caravan pickup adds live animals correctly.
- Cancel path from feature 5 works on an animal purchase if animal POs reuse the same state machine.

### Save/load
- RFQ with animal quote.
- Confirmed animal PO.
- Ready animal pickup.
- Open animal sale.
- Start another game and verify no generated pawn/cache leaks.

### Compatibility sanity
- at least one Core animal;
- at least one Biotech/available DLC animal if present;
- if a normal modded animal is installed, verify the definition-driven path without claiming universal compatibility.

---

## 10.10 Definition of done

- Animals are not made “tradeable” by bypassing the Pawn exclusion globally.
- Buy path works through Procurement.
- Sell path works through Find Buyer and physical caravan delivery.
- Humanlike pawns remain impossible through this feature.
- Price/value uses verified pawn-aware RimWorld behavior.
- No paid-for animal disappears because a caravan handoff failed.
- Save/load works through every persisted stage.
- The implementation has a technical spike documenting the exact RimWorld APIs used.

**Suggested commits:** split this feature.

1. `docs: spike the RimWorld animal trade lifecycle`
2. `feat: add animal procurement`
3. `feat: add live-animal sales by caravan`

Do not force all animal work into one commit.

---

# 11. Cross-feature interactions to test

These seven items touch each other. After all slices are complete, run an integration pass.

## 11.1 Find Buyer + commitments + refresh

Scenario:

1. Have 2,200 rice.
2. Sell 700 through Find Buyer.
3. Page immediately shows ~1,500 available.
4. Create a second order for 1,000.
5. Page shows ~500 available.
6. Try to sell 600 from stale dialog/cache → binding revalidation refuses it.
7. Cancel first order → ~1,200 available again.
8. Consume 300 rice through normal colony activity → physical/available numbers reconcile automatically.

## 11.2 Buyer pickup timing + commitment

1. Accept a Market buyer-pickup order.
2. Before Mark Ready it does not consume current-stock availability.
3. Create a competing direct Find Buyer commitment so there is no longer enough free stock.
4. Verify Mark Ready is refused even though the raw physical stack is large enough.
5. Release/reduce the competing commitment.
6. Mark Ready succeeds.
7. The pickup order now consumes availability while the buyer travels.
8. Market ETA estimate and actual arrival formula agree.
9. Collection completes and the commitment disappears because the order closes.

## 11.3 Stone blocks after refresh

1. Craft blocks while Find Buyer page remains open.
2. They appear automatically.
3. Sell some.
4. Availability drops.
5. No hardcoded vanilla def special case was required.

## 11.4 Procurement cancellation

1. RFQ goods.
2. Withdraw one request.
3. Accept another.
4. Cancel accepted order.
5. Money remains spent and no delivery occurs.
6. Repeat with animal procurement if feature 6 reuses PO lifecycle.

## 11.5 Supply history

1. Sell the same good repeatedly to one settlement.
2. History updates only on completed orders.
3. Raise reputation.
4. Contract offer is for proven supplied good.
5. A second settlement with different history produces a different candidate set.

---

# 12. Save schema strategy

Baseline inspected: **schema 24**.

Features 1–5 should avoid persistent shape changes:

- availability derived from existing orders plus physical map stock;
- refresh cache is UI/runtime state;
- ETA already has persisted arrival tick;
- stone-block fix may be no persistence;
- procurement cancellation states already exist.

Feature 7 should **not** introduce persisted per-good history in the first implementation; derive it from retained `SalesOrder` history.

Feature 6 may introduce persisted animal subject identity depending on the spike.

Rules:

1. Do not bump schema for a cache or a derived read model.
2. Bump only when the persisted shape actually changes.
3. If a schema change is needed, add migrations sequentially.
4. Never discard a live order because a new optional field is absent.
5. Supply history requires **no** backfill/migration because it is read directly from retained completed orders.
6. If the animal implementation introduces new persisted subject identity, test migration/default behavior from a real v0.9.0/schema-24 save, not only synthetic state.
7. Record the final schema in `CLAUDE.md`, `PROGRESS.md`, and release notes only if it actually changed.

Do not pre-commit to “schema 25” or “26” from this document; use whatever the repository's actual next version is if a persistent animal slice genuinely requires one.

---

# 13. Suggested revision acceptance checklist

The next beta revision is ready for external testing when all included scope meets this list:

### Economic integrity
- [ ] Find Buyer cannot double-commit the same available stock.
- [ ] Direct Find Buyer commitments and marked-ready pickup stock are unavailable to new direct sales.
- [ ] Binding transition revalidates live availability.
- [ ] Normal RimWorld consumption can still create a shortfall; stock is not physically locked.

### Inventory UX
- [ ] Find Buyer updates automatically while left open.
- [ ] Manual Refresh still works.
- [ ] Selection and buyer offers reconcile after count changes.
- [ ] Refresh is throttled and profiled.

### Pickup UX
- [ ] Buyer-pickup Market listing gives an approximate travel ETA.
- [ ] Readiness deadline and pickup travel time are both understandable.
- [ ] Actual post-readiness countdown remains authoritative.

### Stone blocks
- [ ] Vanilla stone blocks are proven sellable through Find Buyer.
- [ ] Root cause is documented.
- [ ] No hardcoded per-stone def list is introduced.

### Procurement
- [ ] Open RFQ can be withdrawn.
- [ ] Accepted purchase can be cancelled.
- [ ] Confirmation clearly states forfeited silver.
- [ ] Cancelled purchase cannot later deliver/refund/advance.

### Supply history
- [ ] Per-settlement, per-good successful supply history is derived correctly from retained completed orders; no duplicate persisted aggregate exists.
- [ ] Existing v0.9.0 order history immediately participates without migration/backfill.
- [ ] Contracts only choose eligible goods actually supplied to that settlement.
- [ ] No random unrelated fallback.
- [ ] Offer explains the connection to previous trade.

### Animals
- [ ] API spike is written and cites verified RimWorld 1.6 implementation paths.
- [ ] Ordinary animals can be procured.
- [ ] Ordinary animals can be sold by physical caravan delivery.
- [ ] Humanlike pawns cannot enter the animal path.
- [ ] No animal is processed through stack/item destruction logic.
- [ ] Failed handoff cannot silently destroy paid-for value.

### Stability
- [ ] Clean build, no warnings/errors introduced.
- [ ] Relevant self-tests pass.
- [ ] Every persisted path tested save → menu → reload.
- [ ] Static/cache changes tested across two separate games.
- [ ] No new red errors in normal tested flows.
- [ ] `PROGRESS.md`, `PENDING_PLAYTESTS.md`, `CLAUDE.md` updated.

---

# 14. What explicitly is **not** part of this revision

Keep these out even if adjacent code makes them tempting:

- payment schedules / trade credit for ordinary goods;
- human/slave trading;
- recurring procurement contracts;
- supplier marketplace;
- purchase-orders page redesign beyond what cancellation needs;
- physical locking of committed stock;
- specific cargo-to-order assignment;
- generalized financial system;
- relationship reward expansion;
- special recruit/event system;
- Intercolony pawn traits / operational traits;
- recurring animal/livestock agreements;
- breeding/genetics/training guarantees;
- category-based contract history (“you sold meat, therefore rice counts too”).

Those can be future phases. This revision should leave the existing game more coherent, not exponentially wider.

---

# 15. Suggested handoff prompt to Claude Code

Use this after placing the document in the repository:

> Read `CLAUDE.md` first, then read this revision plan. Inspect the current repository rather than assuming the baseline SHA is still current. Implement the revision as separate vertical slices in the recommended dependency order. For each slice, reproduce/prove the current behavior before editing, verify any unfamiliar RimWorld API against `reference/`, add a production-path self-test, build, perform the required manual/save-load check, document what is still unproven, and commit the working slice before moving on. Do not widen scope. If an assumption in this plan disagrees with the current code, preserve the design intent but report the discrepancy before choosing a substantially broader architecture.

---

# 16. Bite-sized execution slices

The feature sections above describe behavior. Claude should **not** implement each whole feature as one coding session.

Use these bounded slices. Each slice should have one main behavioral claim, one production-path test target, and one commit boundary.

| Slice | Scope | Must not include |
|---|---|---|
| **A1** | Add derived committed-quantity predicate using existing order provenance/state + self-tests | UI, persistence, order-origin enum |
| **A2** | Add `AvailableColonyStock`/equivalent by composing physical stock + A1; switch Find Buyer left list to available counts | auto-refresh, new view-model unless proven necessary |
| **A3** | Revalidate available quantity inside the binding path before a direct Find Buyer order is created | UI-only validation, reservation state |
| **A4** | Revalidate stock at `MarkReadyForPickup()`, excluding the current order from competing commitments | specific-stack allocation, new reservation state |
| **A5** | Add real-time-throttled Find Buyer refresh + selection/buyer-cache reconciliation | WorldComponent tick work, event bus |
| **B1** | Extract one buyer-pickup travel-day estimator and test it | UI changes |
| **B2** | Make buyer-pickup deadline mean “Mark Ready by this time”: late Mark Ready blocked; `AwaitingCollection` immune to readiness expiry | broader deadline redesign |
| **B3** | Expose ETA and correct buyer-pickup timing wording in Market/confirmation; preserve live countdown after readiness | new timing model |
| **B4** | Reproduce stone-block report after A5; fix only the proven gate or add regression coverage if already resolved | classifier rewrite |
| **B5** | Expose RFQ withdrawal and PurchaseOrder cancellation using existing transitions; add explicit cancellation feedback | procurement-history redesign |
| **C0** | Fix contract liveness guard so pending renewal/suspended agreement suppresses new offers | contract state-machine rewrite |
| **C1** | Build one transient, deterministic completed-order history aggregation for offer generation + self-tests | persisted history/schema bump |
| **C2** | Make `ContractService` choose only exact goods proven with that settlement; add causal offer copy | Relations-screen redesign |
| **D1** | Write `ANIMAL_TRADE_SPIKE.md`; compare smallest viable representations against verified RimWorld 1.6 APIs | production animal code |
| **D2** | Implement the smallest proven animal-procurement vertical slice | animal sales, recurring animal contracts |
| **D3** | Implement physical caravan animal-sale vertical slice | buyer pickup/random animal Market listings |

A slice is complete only when:

1. current behavior was reproduced or proven from a deterministic production path;
2. the change uses the existing authoritative owner where one exists;
3. a production-path self-test exists for what can be automated;
4. build is clean;
5. required in-game/save-load evidence is recorded or explicitly placed in `docs/PENDING_PLAYTESTS.md`;
6. the slice is committed independently.

If a slice starts needing a new persisted entity, a Harmony patch, or ownership in a second subsystem, stop and run the decision heuristics from the companion execution guide before proceeding.

---

# 17. Short implementation map

For quick orientation:

| Slice | Main current code to inspect first | Expected type of change |
|---|---|---|
| 1. Committed stock | `FindBuyerService.cs`, `SalesOrder.cs`, `SalesOrderService.cs`, `MainTabWindow_Intercolony.cs` | Derived direct-stock commitment + binding guard; no persistence |
| 2. Refresh | `MainTabWindow_Intercolony.cs`, `FindBuyerService.cs`, existing `Time.realtimeSinceStartup` pattern | Real-time-throttled local UI cache refresh |
| 3. Pickup ETA | `SalesOrderService.cs`, `SalesOrder.cs`, `IntercolonyWorldComponent.cs` deadline sweep, Market UI | One estimator + readiness-deadline coherence + pre-acceptance display |
| 4. Stone blocks | `IntercolonyProductClassifier.cs`, blacklist, Find Buyer | Reproduce after refresh fix; likely regression/test or small fix |
| 5. Cancel procurement | `PurchaseRequest.cs`, `PurchaseOrderService.cs`, Procurement UI | Expose existing transitions + explicit feedback |
| 7. Contract history | retained `SalesOrder`s, `IntercolonyWorldComponent.HasContractWith`, `ContractService.cs` | Transient deterministic aggregation; no new saved history |
| 6. Animals | classifier boundary, RFQ/PO, order validation, caravan handoff, local decompiled references | API/representation spike, then narrow live-pawn vertical slices |

---

## Final design summary

This revision should make Intercolony feel less like several independent menus and more like one economic system.

After the revision:

- stock already committed by a direct Find Buyer sale, or explicitly marked ready for pickup, is not offered again;
- the stock screen stays current enough to trust;
- pickup orders expose their real timing before commitment;
- ordinary materials such as stone blocks behave consistently;
- procurement commitments can be intentionally terminated with known consequences;
- settlements remember what the colony has actually supplied and offer standing agreements accordingly;
- animals become a deliberate, physically executed category of trade rather than being forced through item code.

The two most important invariants are:

> **One unit explicitly committed from current stock cannot be offered twice by Intercolony.**

and

> **A recurring supply relationship must be grounded in a supply relationship that actually happened.**
