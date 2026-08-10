# Intercolony — Claude Execution & Decision Guide

**Companion to:** `Intercolony_Post_0.9.0_Development_Revision_v3.md`  
**Purpose:** tell Claude **how to execute** the revision without getting stuck, silently widening scope, or repeatedly asking Matteo to arbitrate low-risk implementation details.

This file is procedural. The revision plan says **what behavior to build**. This guide says **how to decide what to do next when the code, tests, or RimWorld API do not line up perfectly with the plan.**

---

# 1. Read order

At the start of a development session, read in this order:

1. `CLAUDE.md`
2. this execution guide
3. `Intercolony_Post_0.9.0_Development_Revision_v3.md`
4. only the relevant sections of `DESIGN.md`
5. relevant technical notes/spikes already named by `CLAUDE.md`
6. current production code for the slice
7. `reference/decompiled/` or `reference/vanilla-defs/` for any RimWorld API not already proven locally

Do not read the entire `DESIGN.md` as a ritual. The repository explicitly says not to.

Before editing:

- inspect `git status`;
- inspect the current branch/HEAD;
- confirm the actual current version/schema rather than assuming the baseline in the revision document;
- run a clean build;
- do not overwrite unrelated local/user changes.

The revision document was audited against a particular `main` snapshot. File names and line numbers may move. **Design reasons survive refactors; stale line numbers do not.**

---

# 2. Core operating principle

Use the smallest solution that makes the behavior true **in the current architecture**.

Preferred order:

1. derive from existing authoritative state;
2. reuse an existing service/state transition;
3. add a narrow helper/read model;
4. add runtime cache only where performance requires it;
5. add persisted state only when a required fact cannot be reconstructed;
6. add a new subsystem only when two real concrete use cases demand one.

If you are about to introduce a new:

- persisted entity;
- WorldComponent field;
- general event bus;
- order-origin framework;
- generic trade-asset framework;
- Harmony patch;
- global static cache;

stop and prove why the existing owners cannot solve the slice first.

“Cleaner architecture” by itself is not a reason to add architecture to this mod.

---

# 3. Source-of-truth map

Use this before deciding where code belongs.

| Question | Current authority |
|---|---|
| What economic state survives save/load? | `IntercolonyWorldComponent` |
| What is physically in colony storage? | RimWorld map / listers + `OrderValidator.IsAvailableColonyStock` |
| What has the player promised to sell? | persisted `SalesOrder`s |
| What state is a sales order in? | `SalesOrderService` transitions + `SalesOrder.status` |
| What state is a purchase in? | `PurchaseOrderService` + `PurchaseOrder.status` |
| What state is an RFQ in? | `PurchaseRequest` transitions / `RfqService` lifecycle |
| What does a settlement think of player reliability? | `CommercialReputation` / `ReputationService` |
| What has actually been supplied historically? | retained completed `SalesOrder`s |
| What is a settlement economically like? | derived `SettlementEconomicProfile`, cached but not authoritative |
| What does Find Buyer currently display? | local UI caches, not world state |
| What is physically in a caravan? | the actual RimWorld caravan/pawns/inventories |
| What is “in transit”? | the actual caravan, not a parallel saved flag |
| What is unproven gameplay evidence? | `docs/PENDING_PLAYTESTS.md` |
| What is an out-of-scope worthwhile idea/bug? | `docs/BACKLOG.md` |

If your proposed implementation creates a second answer to one of these questions, reconsider it.

---

# 4. One slice, one claim

Do not start a slice with “implement feature X”.

Start it with one behavioral claim.

Examples:

### A1

> Given existing orders, the mod can compute how much of a ThingDef is already committed from current stock without saving reservation state.

### A3

> A stale Find Buyer dialog cannot create a direct sale for more stock than is currently free.

### B2

> A buyer-pickup order marked ready before its deadline does not fail merely because the buyer is still travelling after that deadline.

### C1

> From retained completed SalesOrders, the mod can deterministically derive which exact goods a settlement has repeatedly bought.

A slice is done when that claim is proven, not when every nearby method is prettier.

---

# 5. The execution loop for every slice

Use this loop.

## Step 1 — State the current behavior

Before changing code, identify:

- the production entry point;
- the authoritative state owner;
- the exact current behavior;
- why that behavior fails the slice's claim.

If the current behavior is not actually wrong, do not “fix” it because the plan assumed it was.

## Step 2 — Reproduce or prove

Use the cheapest reliable evidence:

1. existing self-test/debug action;
2. targeted new test against the real production method;
3. deterministic code-path trace;
4. exact in-game reproduction;
5. minimal temporary diagnostic logging.

Do not start with a large refactor in order to make the bug easier to test.

## Step 3 — Identify the smallest owner to change

Ask:

> Which existing owner is already responsible for this decision?

Examples:

- sales order transition → `SalesOrderService`;
- Find Buyer stock projection → `FindBuyerService` + local UI;
- purchase cancellation → `PurchaseOrderService`;
- recurring contract candidate generation → `ContractService`.

Prefer adding one helper beside that owner to introducing a new service.

## Step 4 — Make the smallest behavior change

Do not combine:

- correctness fix;
- refactor;
- UI redesign;
- new abstraction;

in one patch unless the correctness fix literally cannot be expressed otherwise.

## Step 5 — Test the real path

A test that rebuilds the algorithm in the test file proves the test, not production.

Prefer:

- calling the production calculation;
- calling the authoritative transition;
- generating through the real candidate builder;
- validating a real spawned Thing where feasible.

If a new production prerequisite makes an old self-test start skipping, **fix the fixture rather than accepting reduced coverage**. For example, history-gated `ContractService.BuildOffer()` should be tested by planting temporary completed `SalesOrder` history, then calling the real builder and cleaning the history afterward.

The repository has already caught false confidence from synthetic tests; do not repeat that mistake.

## Step 6 — Build and inspect the log

After code change:

```powershell
powershell -ExecutionPolicy Bypass -File dev.ps1
```

Read the output yourself.

Do not ask Matteo to paste the log.

## Step 7 — Decide what evidence still requires play

Anything involving:

- physical pawn movement;
- caravan handoff;
- actual UI readability/layout;
- interaction sequencing that a self-test cannot reproduce;
- season-long behavior;

belongs in a real play/Dispatch check.

If it is not blocking the next independent slice, record it and continue rather than stopping the whole revision.

## Step 8 — Commit the slice

One coherent working slice per commit.

Do not keep three working slices uncommitted because “the feature is not done yet”.

---

# 6. Confidence heuristic: proceed, log, or raise a hand

Use three levels.

## HIGH confidence — proceed

Proceed without asking Matteo when:

- current code clearly identifies the authoritative owner;
- reference/decompiled code confirms the RimWorld API;
- the change is local and reversible;
- behavior is directly covered by the revision spec;
- a deterministic test can prove it.

Examples:

- helper name/location;
- using `Time.realtimeSinceStartup` for a UI cache because the repo already does;
- sorting candidates by `defName` before seeded randomness;
- adding a cancel button that calls an already-existing `Cancel()` transition;
- changing wording so it matches already-decided semantics.

Do not log every trivial HIGH-confidence decision.

---

## MEDIUM confidence — decide, log, continue

Proceed without blocking Matteo when:

- there are two plausible local implementations;
- both preserve the same player-facing behavior;
- the decision is easy to reverse;
- it does not alter save shape or public economic semantics.

Choose the smaller implementation.

Record it in the **Decision & Uncertainty Log** at the bottom of this file.

Examples:

- 1.0 s vs 1.5 s real-time stock refresh after profiling shows both are safe;
- a private helper in `FindBuyerService` vs a computed property on `SalesOrder`;
- exact compact label wording;
- whether a debug summary lives in an existing debug action or a nearby helper.

Use this template:

```text
### YYYY-MM-DD — Slice A4 — DECISION
Question:
Evidence:
Decision:
Why this is safe to decide locally:
Revisit if:
```

Then continue.

---

## LOW confidence / structural — raise a hand

Stop that slice and ask Matteo before committing the structural decision when any of these is true:

- the only solution appears to require a new persisted field/entity not already authorized;
- a save-schema migration would be needed unexpectedly;
- a Harmony patch is needed where the mod currently has none for that behavior;
- the solution changes the economic meaning of a contract/order rather than implementing the stated meaning;
- two plausible options produce meaningfully different player strategies;
- the solution would delete, convert, or reinterpret existing player obligations/value;
- the animal spike produces two materially different persistent order models with no clearly smaller safe option;
- verified RimWorld APIs make the requested behavior materially different from the assumed design;
- fixing the slice requires taking ownership away from the current authoritative service;
- the slice now requires a generic framework for hypothetical future features;
- a normal-path crash/save corruption/silent value loss is discovered and the fix is not narrow/obvious.

When raising a hand, do **not** send an open-ended “what should I do?” question.

Send:

1. what was discovered;
2. the exact code/evidence;
3. two or at most three viable options;
4. your recommended option;
5. the cost/risk of each;
6. which slice is blocked;
7. which independent slice you can continue meanwhile, if any.

This minimizes back-and-forth.

---

# 7. Do not get stuck: bounded investigation rule

If a reported bug does not reproduce immediately, do not loop indefinitely.

Use this sequence:

### Pass 1 — Trace

Follow the real call path and identify the gates that could cause the symptom.

### Pass 2 — Force

Use debug/self-test state to force the relevant object/input rather than waiting for RNG.

### Pass 3 — Instrument narrowly

If needed, add temporary or debug-only output at the decision boundary:

- classified / rejected and why;
- physical / committed / available;
- candidate set before seeded choice;
- state before a transition.

If those three passes do not establish a defect:

- do **not** write a speculative fix;
- record `NOT REPRODUCED` in the decision log;
- add a targeted regression test if the expected behavior can still be proven;
- move to the next independent slice.

This is particularly important for **stone blocks**.

“Could not reproduce” is a legitimate result. A guessed classifier rewrite is not.

---

# 8. Adjacent bug triage

During a slice, you will find unrelated things.

Classify them before touching them.

## RED — must not be ignored

Examples:

- save corruption;
- normal-path crash;
- silent loss of silver/items/pawns;
- duplicate payment;
- obligation disappears;
- exploit that creates/refunds value incorrectly;
- a bug that makes the current slice's acceptance criterion false.

If the fix is narrow, local, and obvious:

- fix it;
- add a regression test;
- record it as an adjacent fix in the slice/commit notes.

If the fix is structural:

- log it;
- raise a hand;
- do not bury a redesign inside the slice.

---

## YELLOW — record, do not widen

Examples:

- unrelated visible UI issue;
- awkward wording;
- balance concern;
- non-blocking compatibility issue;
- another feature idea;
- old code smell that is not causing the current defect.

Put it in `docs/BACKLOG.md` with:

- what was observed;
- why it matters;
- why it was not fixed now.

Continue the slice.

---

## GRAY — ignore for now

Examples:

- “this method could be cleaner”;
- naming preference;
- possible future abstraction;
- theoretical performance issue with no evidence.

Do not create backlog noise for every refactor temptation.

---

# 9. When a plan assumption conflicts with current code

The revision plan is not executable scripture.

If current code proves an assumption wrong:

1. preserve the **player-facing design intent**;
2. identify the actual authoritative owner;
3. choose the smallest implementation compatible with it;
4. log a `DEVIATION` entry;
5. continue if the difference is local/reversible;
6. raise a hand only if the difference changes semantics or architecture materially.

Template:

```text
### YYYY-MM-DD — Slice C1 — DEVIATION
Plan assumed:
Current code proves:
Design intent preserved:
Implementation chosen:
Why no user decision is required:
```

Do not force the repository to resemble the plan document.

---

# 10. Specific locked heuristics for this revision

These were architecture-audited already. Do not re-litigate them unless current code changed materially.

---

## A. Inventory commitment

The feature is **not** “reserve every open sales obligation”.

For Find Buyer availability, count a sales order when:

```text
(opportunityId == 0 && contractId == 0 && IsOpen)
OR
(status == AwaitingCollection)
```

Interpretation:

- direct Find Buyer sale → stock-backed from creation;
- Market buyer pickup → stock-backed only after Mark Ready;
- ordinary Market seller-delivery order → future obligation, not current-stock reservation;
- recurring-contract seller-delivery cycle → future obligation, not current-stock reservation.

There are two commitment boundaries:

1. creating a direct Find Buyer order;
2. transitioning a pickup order to `AwaitingCollection`.

Both must revalidate live free stock.

For `MarkReadyForPickup()`, calculate competing commitment **excluding the current order ID**, because a direct Find Buyer pickup is already counted from creation and must not reserve against itself.

Do not add a persisted order-origin enum.

Do not physically lock stacks.

Do not assign specific stacks to orders.

---

## B. Inventory refresh

Use local UI cache ownership.

Use real-time throttling, following the repository's existing `Time.realtimeSinceStartup` pattern.

Do not:

- move refresh work to `WorldComponentTick`;
- add an event bus;
- add a world-state revision counter.

The automatic refresh reconciles changes made elsewhere.

Actions initiated by the Find Buyer page can invalidate its local cache directly.

---

## C. Buyer-pickup deadline

The intended player-facing contract is:

> goods must be marked ready by the deadline; buyer travel happens after that.

Therefore:

- late Mark Ready must not escape failure;
- `AwaitingCollection` must not fail merely because readiness `deadlineTick` passes;
- missing goods at arrival still fail through collection validation.

Do not extend/rewrite the saved deadline to fake this.

The state transition itself is the evidence that readiness happened on time.

---

## D. Stone blocks

Classifier already explicitly recognizes StoneBlocks.

Reproduce after inventory refresh is fixed.

Do not add hardcoded block defNames unless verified current definitions prove category-based support is impossible.

If it works after A5:

- add regression coverage;
- close the item;
- do not invent another fix.

---

## E. Procurement cancellation

Reuse existing transitions:

- `PurchaseRequest.TryCancel()`;
- `PurchaseOrderService.Cancel()`.

Do not redesign payment/refund policy in this revision.

Because cancelled purchase orders disappear from the current active-only list, give explicit player feedback naming forfeited silver.

A full purchase-order history screen is out of scope.

---

## F. Supply-contract history

History source of truth is retained completed `SalesOrder`s.

Do not add:

- `SupplyHistoryEntry`;
- Scribe fields;
- migration;
- cached authoritative history.

Build one transient aggregation per `OfferContracts()` pass.

Use exact ThingDef history.

Use `CommercialReputation` for general reliability.

Stable-sort candidate defs before seeded weighted randomness.

Completed recurring-contract cycle orders may count as real supply history.

---

## G. Contract liveness gate

Before history-aware generation, ensure “one live proposal/agreement per settlement” is actually true.

`HasContractWith()` should treat as live:

- Offered;
- Active;
- Suspended;
- Completed with `renewalOffered == true`.

Do not rewrite the state machine.

This is a narrow gate around offer generation.

---

## H. Animals

D1 is a **spike**, not an excuse to start coding.

The spike must first verify:

- vanilla animal trade eligibility;
- correct pawn/species identity;
- valuation;
- caravan add/remove;
- pawn generation;
- faction transfer;
- temporary/lodger exclusions.

Then compare the smallest viable representations.

Do not select a generic architecture in advance.

If one option is clearly smaller and preserves existing goods semantics, choose it and continue.

If two options require materially different persisted shapes or behavior, raise a hand with a recommendation before D2.

Humans remain out of scope.

---

# 11. Tests: choose the right evidence

Not every behavior needs the same kind of proof.

| Behavior | Best evidence |
|---|---|
| arithmetic / pure availability | self-test |
| legal/illegal status transition | self-test calling production service |
| candidate filtering | self-test calling real builder/helper |
| seeded determinism | repeated controlled-seed self-test |
| UI label exists | code + play check if layout matters |
| UI readability at 1.75x | Dispatch/play |
| caravan physical handoff | real caravan play |
| animal pawn ownership/faction | real play + log/debug inspection |
| save persistence | plant → save → menu → reload → verify |
| static leak | quit to menu → different colony |
| performance | existing profiler / measured production call |
| compatibility beyond installed environment | reasoning only unless actually tested |

Do not claim play-proven from a self-test.

Do not claim compatibility from code reasoning.

---

# 12. Save/load heuristic

Ask one question:

> Did this slice change authoritative persisted state or the interpretation of persisted state?

### If no

Examples:

- local UI refresh;
- derived stock availability;
- derived supply history;
- ETA display helper.

A new schema migration is not justified.

Still perform ordinary save/load regression where the feature depends on existing saved orders.

### If yes

Before adding the field:

1. prove the fact cannot be derived;
2. identify old-save default semantics;
3. define migration;
4. define removed-mod behavior;
5. define save/load self-test;
6. log/raise hand if this persistence was not already authorized.

Never bump schema because it feels cleaner.

---

# 13. Performance heuristic

Do not optimize from fear.

First identify whether the code runs:

- per frame;
- per game tick;
- hourly;
- per scheduled refresh;
- only on button click.

Rules:

- per-frame work must be tiny;
- UI expensive work should be cached/throttled;
- daily/refresh-time O(number of retained orders) is acceptable until measured otherwise;
- do not create a persistent index merely to avoid one unmeasured linear scan per refresh.

For Find Buyer freshness:

- full map scans per frame are forbidden;
- a measured scan every ~1–2 real seconds while that one page is visible is the starting design;
- adjust only from evidence.

For contract history:

- aggregate retained orders once per `OfferContracts()` call;
- do not scan all orders separately for every settlement.

---

# 14. Determinism heuristic

Any result that is supposed to be seeded/reproducible must not depend on unstable collection enumeration.

Before consuming seeded randomness:

- build the candidate list;
- sort by a stable key;
- then roll.

Do not rely on:

- dictionary enumeration order;
- current UI sort order;
- object reference order.

This applies directly to history-driven contract candidate selection.

---

# 15. Sentinel heuristic

The repository has repeatedly been bitten by sentinel values.

Before formatting any:

- tick;
- duration;
- optional counter;
- `float.MaxValue`;
- `-1`;

check the semantic guard first.

For this revision:

- `buyerArrivalTick == -1` means no arrival scheduled;
- only show actual arrival countdown when `BuyerEnRoute`.

Do not turn a sentinel into player-facing arithmetic.

---

# 16. UI mutation heuristic

GUI methods can execute multiple times per frame and lists can change underneath them.

Do not:

- perform expensive map scans every layout/repaint;
- mutate the collection you are currently enumerating unless the existing pattern explicitly defers the action;
- let a domain transition be owned by the UI.

When a button causes a transition:

1. UI gathers player intent;
2. authoritative service performs the transition;
3. UI invalidates its local cache/selection afterward.

This separation is already used throughout the mod.

---

# 17. When to continue without a play-test result

A slice may be technically complete but require human/Dispatch evidence.

If the missing evidence is not a release-critical RED condition:

1. add exact steps to `docs/PENDING_PLAYTESTS.md`;
2. if an immediate play request is useful, append it to `DISPATCH_NOTES.md`;
3. commit the implemented slice;
4. move to the next **independent** slice.

Do not write more code as a substitute for missing play evidence.

Do not block all development waiting for a cosmetic/UX observation.

If the next slice depends on the unproven physical behavior being correct, stop at that dependency boundary.

---

# 18. How to write a Dispatch test request

Make it executable without interpretation.

Bad:

> Test animal trade.

Good:

```text
Test D3 — live animal sale by caravan

1. Load save X.
2. Open Find Buyer → Animals.
3. Create a sale for 2 muffalo to <settlement>.
4. Form caravan with exactly 2 eligible muffalo plus one colonist.
5. Arrive and execute Deliver.
6. Verify:
   - exactly 2 muffalo leave player ownership;
   - payment is received once;
   - order becomes Completed;
   - no red error;
   - no human/colonist appears as eligible.
7. Save, return to menu, reload and confirm the completed order remains closed.
8. Record relevant Intercolony/log lines verbatim.
```

The person running the test should not have to infer what “works” means.

---

# 19. File routing: where discoveries go

Use the right file so decisions do not disappear.

## This guide — Decision & Uncertainty Log

Use for:

- a medium-confidence choice made during this revision;
- a plan/code deviation;
- a temporary blocker;
- an explicit “not reproduced” result.

Do not use it for every minor implementation detail.

## `docs/BACKLOG.md`

Use for:

- worthwhile out-of-scope bug;
- future feature;
- refactor worth considering later;
- non-blocking issue deliberately deferred.

## `docs/PENDING_PLAYTESTS.md`

Use for:

- implementation exists;
- self-tests/build look right;
- real play evidence is still missing.

## `DISPATCH_NOTES.md`

Use for:

- exact play-test request;
- verbatim test result/handoff.

## `PROGRESS.md`

Use at revision/phase completion for:

- what actually shipped;
- what did not;
- known limitations;
- manual test evidence.

## `CLAUDE.md`

Update the current-state line when the revision/phase is actually complete.

---

# 20. What must cause a revision-plan update

Do not casually edit the revision plan while implementing.

Update the plan only when:

- an audited assumption is definitively wrong;
- a slice boundary is impossible in the current architecture;
- an item is proven already fixed by an earlier slice;
- a structural decision was approved that changes later steps.

For local implementation choices, use this guide's decision log instead.

The plan should remain a stable statement of scope, not a running scratchpad.

---

# 21. Commit heuristic

Commit when one behavioral claim is true and proven.

Good examples:

```text
fix: account for committed stock in Find Buyer
fix: revalidate direct sales against live available stock
fix: refresh Find Buyer stock without per-frame scans
fix: make buyer pickup deadline end at readiness
fix: show buyer pickup ETA before acceptance
test: cover stone blocks in Find Buyer
feat: expose procurement cancellation
fix: prevent duplicate supply offers during renewal
feat: base supply agreements on proven trade history
docs: spike RimWorld animal trade lifecycle
feat: add animal procurement
feat: add live-animal caravan sales
```

Avoid:

```text
feat: implement beta feedback
refactor: improve economy architecture
fix: various things
```

A commit should be cheap to revert without losing unrelated progress.

---

# 22. Pre-commit checklist for each slice

Before committing:

- [ ] Did I reproduce/prove the original behavior?
- [ ] Did I use the existing authoritative owner?
- [ ] Did I avoid duplicate persisted state?
- [ ] Did I avoid a new generic abstraction without two real uses?
- [ ] Did I verify any unfamiliar RimWorld API locally?
- [ ] Does the production path, not a synthetic duplicate, have test coverage?
- [ ] Did the build pass?
- [ ] Did I read the post-change log?
- [ ] Did I avoid leaving debug/test residue in the player's save?
- [ ] If play evidence is still needed, is it in `PENDING_PLAYTESTS.md`?
- [ ] If I made a medium-confidence choice, did I log it below?
- [ ] If I found unrelated work, did I classify it instead of silently widening scope?

If any answer is “no”, do not commit yet.

---

# 23. Revision completion checklist

Before declaring the whole revision done:

- [ ] Every included slice has its own working commit.
- [ ] All relevant self-tests pass.
- [ ] No new red errors in normal tested paths.
- [ ] Save/load checks completed for stateful paths.
- [ ] Cross-game check completed for any changed static/cache behavior.
- [ ] Pending real-play evidence is honestly listed.
- [ ] `PROGRESS.md` appended.
- [ ] `docs/PENDING_PLAYTESTS.md` updated.
- [ ] picked-up backlog items are struck/mapped according to repo convention.
- [ ] `CLAUDE.md` current state updated.
- [ ] version/schema docs reflect the code that actually shipped.
- [ ] no undocumented structural deviation remains in the decision log.

Do not call “done” because code compiles.

---

# 24. How to raise a hand efficiently

When a real decision requires Matteo, use this exact structure:

```text
BLOCKED: <slice>

What I found:
<one paragraph>

Evidence:
- <file/method/reference>
- <test/log result>

Why the plan's default is insufficient:
<one paragraph>

Options:
A. <option> — <cost / player consequence>
B. <option> — <cost / player consequence>

Recommendation:
<one option and why>

What I can continue without this decision:
<slice(s), or "nothing dependent remains">
```

Do not ask five separate questions.

Do not ask Matteo to choose between implementation details that produce the same gameplay.

---

# 25. Default response when unsure

Use this mental test:

### “Can I undo this with one local commit revert and leave old saves/semantics intact?”

If **yes**, and evidence supports it:

> decide, log if needed, continue.

If **no**:

> stop and assess whether it is an authorized structural change.

A reversible local decision is development.

An irreversible semantic/persistence decision is product architecture.

Treat them differently.

---

# 26. Decision & Uncertainty Log

Append only meaningful entries below this line during the revision.

Do not delete an old uncertainty because it was later resolved. Add a resolution beneath it.

---

### 2026-08-08 — Session start — INTAKE

Matteo reported two findings from his own play of 0.9.0, alongside handing over this revision plan.
Both were triaged against §8 before any code was touched.

**Finding 1 — "Can't cancel a procurement contract."**

Classification: **in scope**, already the subject of slice **B5**. Not a new item.

Traced before delegating. The report is accurate but narrower than its wording suggests:

- RFQ withdrawal *already exists* — a "Withdraw" button on any open request, at
  `Source/Intercolony/UI/MainTabWindow_Intercolony.cs:2052-2059`, calling `PurchaseRequest.TryCancel()`.
- What has no action of any kind is the **purchase order**. `DrawPurchaseOrders()`
  (`MainTabWindow_Intercolony.cs:1605-1668`) draws each open order as three labels — item,
  settlement, ETA — and nothing else. There is no button.
- `PurchaseOrderService.Cancel()` has existed since Phase 11 at
  `Source/Intercolony/Procurement/PurchaseOrderService.cs:294`, complete with forfeit rule,
  `outcomeNote` and reputation bookkeeping. **It was simply never wired to anything.**

So the defect is one missing button and its confirmation, not a missing capability. This matches
§8.1 of the revision plan, which predicted exactly this.

**Finding 2 — "Concluded procurement contracts are not showing properly; not sure if they appear
as cancelled or just disappear."**

Classification: **RED-adjacent, in scope, but not in the plan as its own slice.** Recorded here
because it would otherwise fall between the plan's slices.

The answer to Matteo's uncertainty is **they just disappear**. `DrawPurchaseOrders()` builds its
list with a single filter at `MainTabWindow_Intercolony.cs:1610`:

```csharp
if (order.IsOpen)
```

and `PurchaseOrder.IsOpen` is `Confirmed || ReadyForPickup` (`PurchaseOrder.cs:88`). Every terminal
state — `Completed`, `Cancelled`, `SupplierDefault`, `LostToWar` — is therefore invisible on the
Procurement tab. The order object survives in world state and keeps its `outcomeNote`; nothing is
lost. It is purely a display omission.

Why this matters more than it looks: **the asymmetry is the bug.** Purchase *requests* do not
behave this way. `DrawProcurement()` lists every request regardless of status, sorting open ones
first and rendering terminal ones greyed with their status
(`MainTabWindow_Intercolony.cs:1570-1578` and `2042-2046`). A player therefore learns from the
requests half of the screen that concluded things stay listed, and then finds that orders do not.

It also directly undercuts slice B5: §8.3 Step 4 of the plan tells us to compensate for the
disappearance with a message naming the forfeited silver, precisely because the cancelled order
vanishes. That compensation exists because of this defect. Fixing the display is the better answer
to the same problem, so the two are being done together as **B5** and **B5b**, in that order and as
separate commits.

Not raised as a hand: no persisted state changes, no economic semantics change, and it is revertible
in one commit. Per §25 this is development, not architecture.

**Revisit if:** the concluded-orders list turns out to need retention/pruning policy of its own —
that would be a purchase-order history screen, which §14 of the plan puts explicitly out of scope,
and it would become a `docs/BACKLOG.md` entry instead.

---

### 2026-08-09 — Slice B4 — BLOCKED (root cause found; fix withheld)

**"Can't sell stone blocks" is not an Intercolony defect. It is vanilla behaviour.**

`Core/Defs/ThingDefs_Misc/Various_Stone.xml`, the abstract `StoneBlocksBase`, declares:

```xml
<tradeability>Buyable</tradeability>
```

`Buyable` permits the player to buy but not to sell — `TradeabilityUtility.PlayerCanSell()` requires
`All` or `Sellable`. RimWorld forbids selling stone blocks to anyone, through any trader. The tester's
report is accurate and reproducible, and nothing in this mod caused it.

This vindicates §7.1 of the revision plan, which insisted on diagnosis before touching the classifier.
The classifier was never at fault: it already classifies `StoneBlocks` as `IntermediateGoods`, and the
blacklist never excluded them. Had we "fixed" the classifier we would have changed correct code and
left the real gate untouched.

**Why the obvious fix is withheld.** A definition-first patch was written and is preserved beside this
document as `b4-stone-blocks.patch` and `b4-StoneBlocksTradeability.xml`. It matches the category
rather than concrete defNames, so it covers modded stone too, and it is exactly the shape §7.3 asks
for. It also **changes vanilla globally**: blocks become sellable to every trader in the game, not
only through Intercolony.

That is very likely why vanilla set the flag. Blocks are cut from chunks, chunks are effectively
unlimited and free, and `MarketValue` is 0.9 each. Making them player-sellable creates an
unbounded silver source, hands it to every other mod in the load order, and does so on the authority
of a mod the player installed to trade with other colonies.

Per §6 this is a raise-a-hand: it changes the economic meaning of the game outside this mod's own
systems, and two plausible options produce meaningfully different player strategies.

**Options put to Matteo:**

- **(a)** Ship the patch. Honest to the tester's expectation; accepts the exploit and the global
  vanilla override.
- **(b)** Bypass `PlayerCanSell()` on the Intercolony path only. Leaves vanilla intact but makes
  Intercolony itself the exploit vector, which is worse — the mod would be the only way to mint
  silver from rubble.
- **(c)** Respect vanilla. Blocks are not player-sellable by design. Fix the *legibility* instead, so
  Find Buyer explains the absence rather than silently omitting the item.

**Recommendation: (c).** It is the only option that does not create free money, and it fixes the
defect the tester actually experienced, which was not "blocks are missing" but "I could not tell
why". §7.4 already requires that "cannot be traded" and "nobody wants this right now" be
distinguishable; this is that requirement with a concrete case behind it.

**Why nothing was committed.** The regression tests written for this slice assert
`blocks.tradeability.PlayerCanSell()`, so they only pass with the patch applied. Committing the
diagnostic half alone would have left knowingly-failing assertions in the tree. The whole slice is
therefore held rather than split, `main` is left building clean, and no work is lost.

**Also produced and worth keeping regardless of the decision:** an "Explain item tradability" debug
action that names the *first* gate to reject a def. Under option (c) it becomes the primary tool for
answering this class of report. It is inside the preserved patch. Note for whoever applies it: it
mirrors the classifier's `0.4` market-value floor as a local constant rather than reading it from the
classifier, which is a second source of truth and should be resolved on the way in.

---

### 2026-08-09 — Slice B4 — RESOLVED by Matteo: option (d), an opt-in setting

Matteo's answer supersedes the three options above. Neither shipping the override nor refusing it:
**make it a mod setting, defaulted off, with the global consequence stated on the control.**

This is better than anything offered. (a) imposed a vanilla change on every player; (c) refused a
capability that is perfectly reasonable to want. A toggle lets the player decide with the facts in
front of them, which is the same principle the rest of this mod already follows — say what actually
happens and let the player choose.

**The preserved XML patch is now the wrong vehicle and must not be applied.** `PatchOperation`s run
once during def loading, before settings are meaningful, and cannot re-run — so nothing driven by a
patch can be toggled. `tradeability` is a plain field on `ThingDef`, so the setting instead assigns it
at startup and on every settings change. That is also strictly better than the patch: it is
reversible. `b4-stone-blocks.patch` is kept only for its "Explain item tradability" debug action,
which is still wanted.

**The filter, stated precisely.** Offer a toggle for defs that Intercolony would otherwise trade but
which the player cannot sell — that is, `tradeability == Buyable`. In vanilla this is exactly two
things, stone blocks and cooked meals; `Buyable` appears three times in the entire game and nowhere
else. Modded content is picked up automatically, so nothing is hardcoded. Group the toggles by
`ThingCategory` so the player sees "Stone blocks", not five separate stone types.

**Deliberately excluded: `tradeability == None`.** That means untradeable in either direction — quest
items, unfinished things. `Buyable` at least means the game already accepts the item in trade, just
one-directionally. Offering to unlock `None` would be a footgun.

**Two implementation rules that are easy to get wrong:**

1. Cache each def's original `tradeability` before changing it, and restore *that* on toggle-off.
   Assuming the original was `Buyable` would silently clobber another mod's patch.
2. Toggling off while an order is open must not strand an obligation (§62). `tradeability` is
   consulted in exactly one place in this codebase — `IntercolonyProductClassifier.cs:154` — which
   gates listing and creation, not delivery. Existing orders should therefore complete normally while
   new ones are refused. That is the wanted behaviour and it appears to be free, but it must be
   proven by a test rather than assumed.

**Warning copy agreed**, stating the consequence rather than asking twice, and naming the vanilla
rule being overridden so the choice is informed:

> **Stone blocks** — normally the player cannot sell these.
> Enabling this changes the item itself, so stone blocks become sellable to **every trader in the
> game**, not only through Intercolony. Other mods are affected too.
> RimWorld disallows this by default; the same flag is used for cooked meals, and only for those two.

**Balance note for the record, since the first assessment overstated it.** One chunk yields 20 blocks
for 1600 work at 0.9 silver each, so a dedicated stonecutter earns on the order of a mediocre
crafter — not a game-breaking rate. The reasons to keep it opt-in are that the input is genuinely
unlimited, and that the recipe sets `workSkillLearnFactor` to 0, meaning vanilla designed stonecutting
as filler labour rather than an income. Enabling this converts idle time into money, which is a real
change in how a colony plays, but a defensible one to want.

**On hardcoding.** Matteo explicitly offered an exception to the no-hardcoded-defNames rule if it
proved necessary. It is not, and the exception is declined rather than banked: filtering on
`tradeability == Buyable` produces exactly stone blocks and cooked meals in vanilla, needs no def
names at all, and extends to modded stone and modded meals without further work. A hardcoded list
would be more code and strictly worse. Recorded here so nobody later reads the general rule as having
been bent for this slice.

**Status:** ready to implement as its own slice. Not started — session ended.

---

### 2026-08-09 — Slice B4 — IMPLEMENTED as specified above

Built and committed as `0710a08` (the setting) and `610981c` (the debug action). Nothing in the
resolution above was changed on contact; recorded here only because two details were decided during
implementation and one claim needs correcting.

**Decided during implementation, both minor and reversible:**

1. **Blacklisted defs are excluded from discovery.** Not stated above, but forced: unlocking a
   blacklisted def globally would still not make Intercolony trade it, so offering the toggle would
   promise something the mod then refuses. It would also override the player's own exclusion.
2. **Defs with no thing category** group under a stable internal key displayed as "Uncategorized
   items", rather than being dropped or filed under an unrelated vanilla category. `FirstThingCategory`
   can legitimately be null and vanilla has no rule against it.

**A claim in the implementation report that is not supported and should not be repeated.** The report
said the post-change log showed a schema-23 save being migrated to 24. It does not — the log reads
`State loaded (schema 24, nextId 1924)` with no migration line, because the save was already at 24.
The load itself is real and clean; only the migration claim is wrong. Recorded because this file's
whole purpose is that a plausible-sounding verification claim gets checked against the artifact.

**Not proven.** The self-test assertions exist but have never been clicked, and the settings control
has never been rendered. Both are in `docs/PENDING_PLAYTESTS.md` with exact steps. The one thing
worth stressing to whoever runs them: the obligation guard — an order created while the category is
enabled must still complete after it is disabled — is the assertion that matters, because it is the
one protecting a player's existing commitment.

---
