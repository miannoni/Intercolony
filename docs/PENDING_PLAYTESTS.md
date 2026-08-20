# Pending play-tests

Things a self-test cannot settle, and that are therefore not proven until someone plays them.

**Why this file exists.** Self-tests prove arithmetic, state transitions and invariants. They cannot
prove that a pawn walks where it is meant to, that a turret holds its fire, that a window reads well,
or that a system behaves over a season. Those get asked for in conversation and then quietly lost
when the conversation moves on. Anything listed here has shipped and is believed to work, but has
not been seen working.

**How to use it.** When a phase completes, add what it could not prove. When something is played,
move it to *Proven in play* with the date and what was observed — a struck-through line with
evidence is worth more than a deleted one, because the next person asking "was this ever tested?"
gets an answer instead of silence.

---

## The test environment, and what may therefore be claimed

**Everything is verified on one machine, in one load order.** That is the whole truth of this
project's testing, and the documentation must say so rather than implying broader coverage.

- **DLC:** Biotech only. Royalty, Ideology and Anomaly are **not owned and will not be bought**, so
  they cannot be tested here — ever, not just "not yet".
- **Mods:** Hospitality, Common Sense, RT Fuse, Tilled Soil, FSF Filth Vanishes With Rain And Time.
  Intercolony has run alongside these throughout Phase 25 without incident.
- **UI scale:** 1.75x. This is the scale the layout has actually been judged at.

**How to write compatibility notes and documentation under this constraint** (§118, Pass C):

State what was tested, on what, and say plainly that everything else is untested rather than
unsupported. "Tested with Biotech; other DLC untested" is honest and useful. "Compatible with all
DLC" would be a claim nobody here can stand behind, and the first bug report from an Ideology player
would prove it false. The same applies to mods: name the five, and say the rest is unknown.

Reasoning from the defs is legitimate for DLC that cannot be tested — e.g. checking whether a def a
DLC adds would flow through the trade classifier — but it must be labelled as reasoning, not as a
test result.

---

## Outstanding

### 1.0 program — Stage 0

#### ~~Capture the market baseline~~ — DONE 2026-08-20

Captured on the `Fenhana` / `Intercolony 0.9.3 preflight` save (schema 42, economy seed
`-1586549745`, refresh 432), determinism **PASS**, written up in
`docs/MARKET_BASELINE_0_9_3.md`. Taken on a persistent save rather than a `-quicktest` world,
so the same economy is re-measurable after Stage 2.

**Rerun wanted, low priority.** The capture exposed two flaws in the diagnostic, both fixed
since: it reported generator appetite without the global live-offer ceiling that actually
governs market size, and its probe basket was alphabetically chosen and full of ancient ruins
scenery. Rerunning on the same save replaces the two affected sections. Not blocking — the
figures that matter for Stage 2 comparison (offer rate, category shares, price factor spread)
are unaffected by either flaw.

#### ~~Schema 42 → 43 migration under a real save~~ — DONE 2026-08-20

Ran in the real load order on a 21.5 MB schema-42 colony, `nextId 6826`, zero exceptions and no
dropped records. **This was the first time any Intercolony migration ran outside a throwaway
install** — see the top of this file's Outstanding list, which said so for the three earlier
ones. `dev.ps1 run -MainMenu` now exists to keep it repeatable.

### Stage 0 self-tests (commercial timeline, `04be001` / `a502b63`)

#### Timeline self-test — never run

**Full path.** From RimWorld's main menu, open **Options** → **General** and enable
**Development mode**. Load any colony. Press **F12**, click the **orange bug icon** in the
top-right toolbar, type `Run timeline self-test`, and click the exact action
**Intercolony → Run timeline self-test**.

**Pass.** The debug log begins `Commercial timeline spine self-test`, ends with a line
reading `N passed, 0 failed`, and contains no `FAIL` line and no red exception. It also
prints `commercial timeline restored to N record(s)` — that line is not decoration. The test
deliberately overfills the timeline past its 1,000-record cap and prunes it, so if the
restore is wrong it destroys real history. Confirm that count matches what was there before.

**Why it is not proven.** Both slices are committed on a clean build alone. The test now
drives the real production transitions (`SalesOrderService.Fail`/`.Cancel`,
`PurchaseOrderService.Cancel`, and both `HostilityPolicy` war paths) rather than recording
its own events, so a passing run is genuine evidence that the write sites fire — but nobody
has run it.

#### The three suites that now touch the timeline — rerun to confirm no regression

Stage 0 gate criterion 6. `IntercolonyOrderSelfTest`, `IntercolonyRfqSelfTest` and
`IntercolonyCombatClauseSelfTest` all drive transitions that now record commercial events, and
each is wrapped in `IntercolonyTimelineGuard` so the records are rolled back afterwards.

**Full path.** Same F12 → orange bug icon route. Run `Run order self-test`,
`Run RFQ self-test` and `Run combat clause self-test`.

**Pass.** Each reports its usual counts with **0 failed** and no new skips, and — the point of
the guard — running `Dump commercial timeline` afterwards shows the **same record count as
before the suites ran**, with no `Testholme`, `MatrixTest` or `Test faction` rows. A row from
a settlement that does not exist means the guard is not working.

#### Contract timeline events — no self-test coverage

`ContractStarted`, `ContractCompleted`, `ContractFailed` and `ContractCancelled` are wired at
six sites in `ContractService` but are **not** covered by the timeline self-test: driving them
needs a live contract with cycles running, which `IntercolonyContractSelfTest` already builds.
The cheapest proof is play — accept a supply agreement and confirm a `ContractStarted` row
appears in **Dump commercial timeline** (same F12 menu). Worth folding into the contract
self-test if Stage 5 touches these paths anyway.

#### Schema 42 → 43 migration under a real save — never run

**Full path.** Load an actual save made with 0.9.3 (schema 42) — not a new colony. Then read
the log with `powershell -ExecutionPolicy Bypass -File dev.ps1 log`.

**Pass.** The log contains `Migrating state from schema 42 to 43` followed by
`schema 42 -> 43: commercial timeline record spine added; history starts recording at tick N`,
with no red errors, and every existing order, contract, request and employment still present
afterwards.

**Why `dev.ps1` cannot prove this.** It launches `-quicktest`, which creates a *new* world
that initializes at the current schema and therefore never enters the migration path at all.
Only opening a real save exercises it. This is the same standing gap already recorded below
for the three earlier migrations.

### Correction-batch self-tests

These are dev actions, not play-tests, but their procedures remain here so later changes can rerun
them. All of them: **F12** → **orange bug icon** (top-right toolbar) → type the search term → click
the action. Output goes to the debug log; no need to copy anything out, the dev script reads it.

**Run 2026-08-13 during 0.9.1 preparation:** order **93 passed, 0 failed**, contract **38 passed,
0 failed**, RFQ **69 passed, 0 failed**. The first order run found a real buy-only obligation
regression; after the focused fix, the stated result is the rerun. The order suite explicitly
skipped recorded-map collection versus `Find.AnyPlayerHomeMap` because the world had one home map,
and skipped live-offer acceptance because no offer existed. Those limits remain manual work below.

#### Find Buyer, availability and pickup timing — `Run order self-test`

**Full path.** From RimWorld's main menu, open **Options** → **General** and enable
**Development mode**. Load a colony with a home map. Press **F12**, click the **orange bug icon** in
the top-right toolbar, type `Run order self-test`, and click the exact action
**Intercolony → Run order self-test**.

**Pass.** The debug log begins `Sales order self-test`, contains no `FAIL` line or red exception,
and ends with `0 failed`. This covers the 1.5-second refresh boundary; selection clamping and buyer-
offer invalidation; physical stock minus the deliberately narrow commitment set; direct-sale
revalidation; Mark Ready self-exclusion and competing pickups; the shared pickup ETA and unknown-
route fallback; and the rule that an en-route buyer survives the old readiness deadline.

**Failure.** Any `FAIL` line, a non-zero failed count, or a red exception is a failure. A prerequisite
skip which prevents the new availability/pickup assertions from running is not evidence for those
assertions and must not be marked as a pass.

#### Contract liveness and completed-history offers — `Run contract self-test`

**Full path.** From RimWorld's main menu, open **Options** → **General** and enable
**Development mode**. Load a colony. Press **F12**, click the **orange bug icon** in the top-right
toolbar, type `Run contract self-test`, and click the exact action
**Intercolony → Run contract self-test**.

**Pass.** The debug log begins `Recurring contract self-test`, contains no `FAIL` line or red
exception, and ends with `0 failed`. The new checks distinguish live offers, active and suspended
agreements, pending and lapsed renewals; require two completed sales of the exact good to the exact
settlement; reject failed, cancelled, cross-settlement, missing-def and blacklisted history; and keep
seeded selection stable. The test temporarily isolates and then restores the colony's contracts,
orders and reputation.

**Failure.** Any `FAIL` line, a non-zero failed count, a red exception, or an early return because no
accessible settlement/economic profile exists is a failure to verify this batch. Do not count
"nothing visibly changed" as a pass.

#### Procurement cancellation and concluded-order selection — `Run RFQ self-test`

**Full path.** From RimWorld's main menu, open **Options** → **General** and enable
**Development mode**. Load a colony. Press **F12**, click the **orange bug icon** in the top-right
toolbar, type `Run RFQ self-test`, and click the exact action
**Intercolony → Run RFQ self-test**.

**Pass.** The debug log begins `RFQ self-test`, contains no `FAIL` line or red exception, and ends
with `0 failed`. The new checks cover cancellation from Confirmed and Ready for pickup without a
refund, refusal to recancel a terminal order, the reputation hook, cancelled orders remaining inert
during hourly advance, all four concluded statuses being selected for display, and open purchases
sorting ahead of concluded ones. A `SKIPPED` line for a def absent from this installation is allowed
only for that named older item case; it does not excuse a skipped cancellation/display check.

**Failure.** Any `FAIL` line, a non-zero failed count, or a red exception is a failure. If the
cancellation settlement prerequisite cannot be resolved, the test itself emits a failed check. An
initial `(no tradable defs or no settlements; skipped)` with no final count is also not a pass. Do
not rerun until a quiet result and mark the first attempt passed.

#### Buy-only unlock, including the obligation guard — `Run order self-test`

Added 2026-08-09 with the buy-only setting. Same action as the availability checks above, so one
click covers both, but read the `Buy-only trade unlock:` block specifically.

**Pass.** Under `Buy-only trade unlock:` every line passes: discovery finds a testable item; a
disabled category is not a trade candidate; an enabled one is, and permits both directions; and
**an order created while enabled still validates, marks ready and completes by buyer pickup after
the category is disabled**. That last one is the important assertion — it is the guard against
toggling the setting off stranding an obligation. The final line asserts that toggle-off restores
the exact pre-modification value rather than assuming it was `Buyable`.

**Failure.** Any `FAIL` in that block. Also treat as a failure any red exception mentioning
`Tradeability` or `BuyOnlyTradeUnlock`, and — importantly — **any lasting change to the game after
the test**: the test spawns an item, completes a sale and moves silver, then undoes all of it. If
silver, letters or orders differ afterwards, the restoration is wrong even if every check passed.

### Schema migration 39 → 41 — see `docs/SCHEMA_24_TO_CURRENT.md`

The chain through 39 was proven from a real schema-22 save on 2026-08-15. Two steps landed on
2026-08-16 and must now be proved in one real load: 39 → 40 records the distance behind a
buyer-pickup promise, and 40 → 41 records how much of a purchase request has been ordered. Neither
new step has run in the real load order.

**Steps.** Launch the game normally and load a schema-39 save containing at least one sales order and
one open purchase request. Do not use `dev.ps1`: it launches `-quicktest`, which creates a fresh world
already at schema 41 and therefore cannot exercise either migration.

**Pass.** Player.log names both migration steps, in order, and reports schema 41. Existing orders and
requests still appear. Then **save, quit to the menu, and reload**; the second load must say
`State loaded (schema 41, …)` and not `State initialized fresh`.

**Failure.** Any red error during load, any order or purchase reported as dropped, a lower schema
number than expected, or `State initialized fresh` on the reload.

**Why this cannot be settled any other way.** A new world initializes at the current schema, so the
migration chain never runs. Only opening a real older save proves both steps in their real order.

### Procurement quotes are deterministic within a market refresh

Added 2026-08-16 with `a11a97f`. Raise a request and note its quotes, withdraw it, then raise the same
request again before the market refreshes. **Pass:** the quote set is identical. Changing only the
quantity must not reroll the per-unit offers either. A changed settlement, price or lead time means
the request is still seeding from fresh request state.

### A partial quote acceptance leaves the request open

Added 2026-08-16 with `f1e6852` (schema 41). Request a quantity larger than one quotation can fill and
accept that quotation. **Pass:** the request stays open for exactly the remainder, and its other
quotes remain acceptable until the market refresh. The request disappearing, retaining the original
quantity, or invalidating all other quotes is a failure.

### The 2026-08-10 playtest-feedback batch

Four changes from Matteo's own play. None has been re-tested.

**Worker wages are now double by default.** Options → Mod options → Intercolony → **Worker
wages**. The slider names a concrete worker so the multiplier cannot be misread, and 100%
reproduces the old rate. **Check specifically:** hire someone, then change the slider. The
existing employee's wage must not move — only new quotes should. A renewal is a fresh quote
and *is* expected to reprice.

**The supply agreement popup now negotiates, resizes and routes.** Selling → Contracts →
accept an offer.
- The quantity can move a tenth either way. Confirm the payment line follows it.
- A colonist negotiates the rate up, to a maximum of +15% at Social 20. The popup names them
  and shows the rate before and after. **Check with no free colonist too** — it should say so
  plainly rather than showing a silent zero change.
- **The fulfilment choice is the one with real consequences:** pick *Buyer collects* and
  confirm that every cycle's order actually arrives as a pickup, needing Mark ready, rather
  than a delivery. That is a new persisted field, so also check it survives a save and reload.
- Cancelling the popup now leaves the offer open instead of declining it. Declining is the
  separate button on the row.

**Procurement has sub-tabs** mirroring Selling: Market, Find seller, Orders, Contracts.
Market and Contracts are disabled placeholders with an "under development" tooltip.
**Check at UI scale 1.75:** four tab captions must fit the row without clipping, and the
group badge should count open requests plus open purchases.

**Purchase requests no longer lie about their fate.** Found in Matteo's play: Find seller had
filled with rows reading "Cancelled" for requests he had successfully bought from, because
accepting a quotation marked the request `Cancelled` — a status meaning "withdrawn by the
player".

**Load the save that showed the problem** and check the log for
`schema 29 -> 30: ... relabelled N`. **N should be greater than zero** on any save with
purchase history; a zero there would mean the repair did not find the records it was meant to
fix. Then: Find seller should show only live requests, and Orders should carry a "Concluded
requests" section where former purchases read *Ordered from a supplier* rather than
*Cancelled*. Genuinely withdrawn requests must still read *Withdrawn*.

**Trade logistics now default to staying home.** A purchase request opens on *Supplier
delivers*; a Find Buyer sale and a supply agreement open on *Buyer collects*. **Check that
existing orders and agreements are unaffected** — only the dialogs' opening selection changed,
deliberately, because these fields omit a value equal to their scribe default and moving that
default would have silently reinterpreted old saves.

**Find Buyer stays put after a sale**, so several sales in a row are possible without
navigating back. Accepting from Market still navigates, since that listing is consumed.

**Purchase requests can state a material and a minimum quality.** Both controls appear only
for items that carry those properties — check a stuffable item (a bed), a quality item, one
that is both, and one that is neither (rice) shows neither control. Switching the selected
item must clear both.

**The quality floor's interesting case:** ask for *Masterwork or better* on something ordinary
and expect **fewer or no quotes**, with the usual "nobody answered" reason — settlements that
cannot work to that standard decline rather than quoting below it. If a high floor still
returns the same number of quotes at the same price, the floor is not doing anything.

**Economy and labor were recentred on 2026-08-10** from Matteo's own play. Economy difficulty
100% now means what 135% meant; labor 100% now means three times the original rate.

**Both settings sliders reset to 100% on first launch after this build, deliberately** — the
scibe keys were renamed because the old saved numbers were chosen against a different scale
and reading them back would have compounded. **If a slider still shows an old value, the
rename did not take.**

**How you pay now changes what you pay:** prepaid cheapest, per-quadrum in the middle, daily
dearest, and pay-as-you-go takes a fee at signing. Check the hiring dialog's three structure
lines quote three visibly different totals, then **hire on daily and confirm payroll actually
pays the premium rate** — it is folded into the contract at hire, and a premium quoted but not
charged is the whole feature failing silently.

**Also check a daily job posting**: its going-rate band should be higher than the same posting
per quadrum. If it is not, postings can hire at per-quadrum rates and dodge the premium.

**One unresolved observation.** Two root-level OnGUI `NullReferenceException`s appeared once
during this work and did not recur across two further cycles on the same build; the baseline
before the change showed none. No Intercolony frame appeared in either trace. **If these
resurface, note what was on screen** — that is the missing piece.

**Not yet done, and worth knowing:** nothing makes Social visible *before* accepting a
contract — the player cannot tell a good negotiator is worth waiting for until the popup is
open.

### Three debug actions exist to make the tests below bearable

First added 2026-08-10; expanded 2026-08-16. **F12** → **orange bug icon** → type the name.

- **`Arrive purchase orders now`** — pulls every confirmed purchase order's ready time to now
  and runs the ordinary advance, so procurement can be tested without playing out the
  supplier's lead time. It moves the clock and uses the real fulfilment path rather than
  calling delivery directly, so the status transitions, the animal branch and the refund
  branches are all genuinely exercised. Orders already waiting to be collected are reported
  separately and left alone — they are waiting for a caravan, not for time.
- **`Arrive buyers now`** — added in `4fbec43`. Pulls travelling buyers forward and runs the real
  collection handler rather than completing orders directly. It has already arrived six orders in
  play; use it for the remaining buyer-pickup edge cases below.
- **`Explain unsold animals`** — lists the animals some trader buys but none sells, and says
  whether the setting below is on. With Core + Biotech this should be exactly one entry:
  Thrumbo, 4000 silver.

### Buying animals no trader sells — off by default

Added 2026-08-10. Intercolony was offering thrumbos, which vanilla never does: a thrumbo is
tagged `AnimalExotic`, a tag that appears in traders' buy lists and no trader's sell list.
Now gated behind **Options → Mod options → Intercolony → Animals no trader sells**.

**Pass.** With the setting off, no thrumbo appears in the animal list in **Procurement → New
request**. Tick the setting, reopen the dialog, and it appears — the cached list is dropped on
toggle, so it must not need a restart. Ordering one costs full market price.

**Failure.** A thrumbo visible while the setting is off; the list not changing until restart;
or *more* than one animal listed by `Explain unsold animals` in a Core + Biotech load order,
which would mean the rule is catching animals it should not.

### Animal trade, end to end — buyer pickup proven, other paths remain

Added 2026-08-10. All five animal slices are built. Buyer pickup was proven on 2026-08-16 with a
chicken and, in a separate save, a bonded labrador retriever whose warning appeared correctly. The
buying and seller-delivery paths and the pickup edge cases below remain unproven.

**Buying.** Open **Intercolony** → **Procurement** → **New request**, switch the mode from
goods to animals, pick a species, and set some combination of sex, life stage and pregnancy.
Send it, accept a quotation, and wait for delivery.

- **Pass:** the animals that arrive match the specification exactly, are tame, in your
  faction, and alive. A pregnant one is actually pregnant when inspected.
- **Watch for:** an animal of the wrong sex or age, an untamed or hostile animal, an animal
  arriving dead or downed, or a pregnancy that is not there. Any of those means the
  post-generation verification is not doing its job.
- **The subtle one:** request an animal with **no life stage specified** and check the price
  is low. Unspecified terms are priced at the cheapest animal that would satisfy them,
  because the supplier chooses. If an unspecified request costs the same as an adult, the
  pricing rule is wrong.

**Selling by your own caravan.** **Selling** → **Find buyer**, pick an animal group, sell
with **You deliver**, load matching animals into a caravan and take them.

- **Pass:** the animals leave the caravan at the settlement and you are paid.
- **Must check:** sell a **bonded** animal. The confirmation must name the specific colonist,
  and afterwards that colonist must actually have the sold-bond mood. If the confirmation
  names nobody, or names someone who then has no memory, the handoff is skipping vanilla's
  sale path — the failure that is invisible unless you look for it.

**Selling by buyer pickup.** Same, but choose **Buyer collects**, then **Mark ready**.

- **Proven 2026-08-16:** order 4215 sold a chicken end to end, and a separate save sold a bonded
  labrador retriever with the bond warning appearing correctly.
- **Must check:** after marking ready, **kill or slaughter one of the designated animals**
  before the buyer arrives. The order must deliver the rest and treat the missing one as a
  shortfall — it must **not** quietly substitute another matching animal from your colony.
  Substitution would mean parting with an animal you never agreed to sell.
- **Also worth one attempt:** two pickup orders for the same species at once. They must never
  designate the same animal.

**Not proven by any of this:** balance. Whether animal prices are sane against the rest of
the economy needs play, not a test.

#### Animal specification, matcher and eligibility — `Run animal spec self-test`

Added 2026-08-09 with the `AnimalSpec` slice (schema 25). **F12** → **orange bug icon** → type
`Run animal spec self-test` → click **Intercolony → Run animal spec self-test**.

**Pass.** No `FAIL` line, no red exception, and a zero failed count. The one assertion that matters
most is that eligibility **rejects a humanlike outright** — several vanilla trade interfaces traffic
in `Pawn` rather than "animal", and admitting a colonist here would be the worst defect this feature
could have.

**Read the `SKIPPED` lines, do not ignore them.** This test refuses to fabricate pawns, so most of it
skips in a colony that lacks the right animals. A run that is *all* skips proves nothing. To get real
coverage the colony needs, at minimum: a spawned animal, a sexed animal, a live-bearing (non
egg-laying) race, two same-species animals differing in sex, a humanlike pawn, and ideally an active
Intercolony employee. A muffalo or alpaca herd plus any colonist covers most of it.

**Failure.** Any `FAIL`, any red exception, or the humanlike assertion reporting `SKIPPED` when a
colonist is plainly standing on the map — that last one means discovery is not finding humanlikes,
which is worse than a failed assertion.

**Pricing assertions were added to this same action on 2026-08-09 (schema 27).** They are exact
rather than approximate: each expected price is computed by hand in the test and compared for
equality, so a changed multiplier fails loudly instead of drifting. Two are worth knowing about:

- `goods price is bit-for-bit unchanged from the pre-animal formula` — reconstructs the old Steel
  formula independently and compares float *bits*, so operation order is protected as well as the
  number. **If this ever fails, animal work has perturbed goods pricing**, which is the single worst
  outcome this slice could produce.
- `animal explanation names species, female and pregnancy factors` — deliberately passes a material
  and a quality alongside an animal specification and requires that *neither* appears, proving
  animals cannot leak into material or craftsmanship valuation.

The pricing group skips only if no positive-value live-bearing race using Core's `AnimalAdult` stage
is loaded, which in practice means it runs almost always.

#### Which colony a buyer collects from — `Run order self-test`

Added 2026-08-09 with the fulfilment-colony fix (schema 26). Same action as the availability checks.

**Pass.** Mark Ready records the map it was called on, and an order with no recorded map still
completes through the fallback.

**This is deliberately not fully testable in a one-colony world**, and the test says so rather than
faking it: the assertion that collection uses the order's *recorded* map instead of
`Find.AnyPlayerHomeMap` emits an explicit `SKIPPED` line when only one home map exists, because with
one colony the two values are identical and the assertion would pass vacuously. **Seeing that
`SKIPPED` is the correct result in a single-colony save.** The real proof is the play-test below.

**Run 2026-08-17 in a two-colony save — the assertion finally executed, and passed.** Matteo ran
`Run order self-test` in a world with two player colonies, twice, with the same result: the
`Buyer-pickup colony binding` section reports no `SKIPPED` line at all, and the suite ends **107
passed, 0 failed**. This is the first time `recorded-map collection vs AnyPlayerHomeMap` has run for
real since it was written — it had exactly one prior execution in its history and it *failed* it.

**Do not read this as the whole fix being proven.** Two limits stand:

- The suite is a self-test, not a play-test. **The manual two-colony reproduction below is still
  owed** — the assertion checks the resolution logic, not that a buyer physically walks to the right
  colony and takes goods from that stockpile.
- **The same run skipped a different assertion**: "enough matching colony animals validate alongside
  non-matching same-species animals — no eligible, uncommitted opposite-sex pair of one species on
  this map". The two-colony world traded one blind spot for another. To clear it, the save needs a
  male and a female of one species, both eligible and uncommitted, before the run.

Both runs reported the identical `107 passed, 0 failed` as the single-colony run that skipped the
colony assertion instead — **the summary line did not mention a skip in either case.** That is the
project's own "a SKIP is not a pass" rule with nothing enforcing it, and it is being fixed: the
summary now reports the skip count and re-lists the skipped assertions at the end.

### The buyer-pickup colony fix needs two colonies

Added 2026-08-09. This is the only way to actually prove the bug is gone, and a self-test cannot do
it. **It is also a reproduction of a real 0.9.0 defect**, so it is worth doing even casually.

**Setup.** A save with **two** player colonies. Note which was founded first — that is the one the
old code always used. Put the goods for the order in the stockpile of the **second** colony, and make
sure the first colony has **none** of that item.

**Steps.** Switch to the second colony. Open **Intercolony** → **Selling**, take a buyer-pickup order
for an item stocked there, and click **Mark ready**. Let the buyer travel and arrive.

**Pass.** The order completes and the goods leave the **second** colony's stockpile. Player.log
records the completion normally.

**Failure — and this is exactly what 0.9.0 does:** the order fails with "The buyer arrived and the
goods were not there" even though the goods are plainly sitting in the second colony, or the goods
vanish from the *first* colony instead. Either result means the fix did not take.

**Do not reuse this as the abandoned-colony test.** Since `b6e868e`, collection must never substitute
another colony after the fulfilment colony disappears; that refusal path is the separate test below.

### Procurement delivery and refund use the paying colony

Added 2026-08-13 with the purchase-order destination fix (schema 32). Neither path is verified in
play.

**Setup.** A game with **two** player colonies. Note which was founded first, switch to the
**second** colony, and have enough silver stored there to pay for two purchase orders.

**Steps.** From the second colony, open **Intercolony** → **Procurement** → **Request goods...**.
Create one goods request with **Supplier delivers** and another with **We collect**; wait for and
accept a quotation for each, paying both from the second colony. Enable Development mode, remain on
the second colony map, press **/** to open **Debug actions**, open the **Intercolony** category, and
click **Arrive purchase orders now**. This sets each confirmed order's ready time to now and runs
the normal order advance: the delivery order should arrive, while the pickup order becomes ready to
collect. Do not send a caravan or cancel the pickup order. In **Procurement** → **Orders**, let its
`collect within ...d` countdown reach zero; the debug action does not move this expiry, which remains
the original supplier lead time plus the 10-day pickup grace. The next hourly or coarse refresh
refunds it as uncollected goods.

**Pass.** The delivered goods arrive at the **second** colony. When the uncollected pickup order
defaults, its refunded silver is placed at the **second** colony rather than the first home map;
the second colony gains the refunded amount and the first colony gains none. Either outcome appearing
at the first colony means the fix did not take.

**Use the same world for the sales-side check above.** This is exactly the two-colony setup needed
by the buyer-pickup assertion that `Run order self-test` keeps skipping, so one two-colony session
can settle both the sales side and the procurement side.

**Not covered:** the no-home-map refund hold and the zero-placement refund hold have no coverage and
are not practically reachable by hand. Do not treat this two-colony test as evidence for either.

### Destination-colony fallbacks after `b6e868e`

Added 2026-08-16. Both paths require a game with **two player colonies** so there is a wrong colony
available to expose an accidental substitution.

**Taking from the player — collection refuses.** Mark a buyer-pickup order ready at the second
colony, then abandon that fulfilment colony before the buyer arrives. Use **Arrive buyers now**.
**Pass:** the collection refuses, the order fails with a reason, and no matching goods or animals are
taken from the surviving colony. Completion from substitute stock is a failure.

**Giving to the player — fallback is disclosed.** From the second colony, create supplier-delivery
and refund paths, then remove that destination while the first colony survives. **Pass:** delivered
goods and refunded silver fall back to the surviving colony, and the player-facing message names it.
Silent placement, lost value, or naming the vanished colony is a failure.

### ~~A proposed agreement is answered after a wait~~

**Done 2026-08-15.** Matteo proposed at several prices and reported the flow working, including the differing response times and the refusal path.

Added 2026-08-14 with `4dc2ae1` and `feb19e5`. Proposing a supply agreement used to accept it on the
spot, which made the terms meaningless. It is now sent, sits pending, and the settlement answers when
its decision falls due. Nothing below has been played.

How appealing the proposal looks is scored from the price against the going market rate — the
dominant term — plus the quantity against what that settlement actually wants, and existing
commercial reputation. The wait is **shortest at both extremes and longest in the middle**: a superb
offer earns a quick yes, an absurd one a quick no, and a middling one takes the longest because it is
the only one genuinely in doubt. Acceptance runs from roughly a tenth at worst appeal to nine tenths
at best, so a generous offer can still be refused.

**Steps.** Open **Intercolony** → **Selling** → **Contracts** and use **Propose supply agreement**.
Against the *same* settlement and item, make three proposals in turn at different prices: well below
the going rate, at about the going rate, and near twice it. Note how long each takes to be answered
and what the answer was.

**Pass.** Both extremes are answered noticeably faster than the middling one. While pending, the row
says it is awaiting the settlement's answer and offers no accept or decline buttons. A letter arrives
for acceptance and for refusal alike.

### ~~A proposal's answer does not change on reload~~

**Done 2026-08-15.** Reported working; the answer held across a reload.

Added 2026-08-14. The answer is seeded from the world economy seed and the contract id on purpose, so
reloading cannot be used to fish for a better outcome.

**Steps.** Leave a proposal pending and save. Let the decision fall due and note the answer. Reload
the save and let the same day pass again.

**Pass.** The same answer both times. A different answer means the seeding regressed and the mechanic
can be save-scummed.

### ~~Price moves faction goodwill both ways, and never starts a war~~

**Done 2026-08-15.** Reported working in both directions, with no hostility resulting from a price penalty.

Added 2026-08-14 with `84c80f0` and `b41af87`. Price is a single lever: below the going rate is
generosity and earns faction goodwill when a delivery completes; above it is greed and costs
goodwill. A penalty is clamped against RimWorld's own hostile threshold and can never tip a faction
into war. None of this has been seen in play.

**Steps.** Complete a delivery on an agreement priced **below** the going rate and check the buyer
faction's goodwill in the Factions tab before and after. Repeat with one priced **above** it. For the
clamp, price an expensive agreement near twice the rate with a faction whose goodwill is already low,
and let it deliver.

**Pass.** Goodwill rises in the first case and falls in the second. It never reaches the hostile
threshold as a result of a price penalty, and no hostility letter arrives from one. Commercial
reputation continues to move on its own terms — being liked and being trusted are separate.

### ~~Save schema 38 and 39 have been migrated from a real save~~

**Done 2026-08-15.** A real **schema 22** save was opened and migrated cleanly all the way to 39 — a longer chain than this entry anticipated, and the last outstanding release risk for 0.9.1.

Added 2026-08-14. A real save has migrated as far as **37** successfully, which is recorded above. The
two steps added since — the market rate a deal was struck against, and the pending-decision fields —
have only ever existed in worlds created at the current schema. `-quicktest` cannot prove them; it
builds a new world already at the latest version.

**Steps.** Open a save made before this batch, watch the log for the migration lines, then save, quit
to the menu and reload.

**Pass.** The log names each step through 39 in order, the second load reports the current schema, and
no exception appears.

### The buy-only setting itself has never been seen

Added 2026-08-09. The code path is asserted by the self-test above; the **player-facing control has
never been rendered**, and nothing here is proven.

**Setup.** Main menu → **Options** → **Mod options** → **Intercolony**. Scroll to **Buy-only items**
at the bottom.

**Steps.** Read the warning paragraph. Hover a category row to see its tooltip. Tick **Stone
blocks**, close the settings window, then open **Intercolony** → **Selling** → **Find buyer** and
look for blocks. Untick it and check they disappear from the list. Then quit to the menu, restart
the game, and reopen the settings.

**Pass.** Exactly two categories appear in vanilla + Biotech: stone blocks and cooked meals, each
with a plausible item count and a tooltip naming the items. Ticking makes blocks appear in Find
Buyer; unticking removes them. The tick survives a full restart. The warning says plainly that the
change affects every trader in the game, not only Intercolony.

**Failure.** More than those two categories in this load order (something is wrong with the
discovery filter); an empty or nonsense tooltip; a row whose label is a defName rather than a
readable category; the setting not surviving restart; clipped or overlapping text; or the scroll
view not reaching the bottom row — the section is measured in a separate pass from the one that
draws it, so a measurement bug shows up exactly as unreachable content.

**Also worth checking, since it cannot be asserted:** with the setting **on**, sell blocks to an
ordinary vanilla trade caravan. That is the global consequence the warning promises, and confirming
it is what makes the warning honest rather than theoretical.

### Find Buyer shows uncommitted availability, not raw stock

**Setup.** Put a known quantity of one stackable good in a stockpile and choose a lot small enough
that at least one interested settlement can buy most of it. Open **Intercolony** → **Selling** →
**Find buyer** and write down the left-hand count.

**Steps.** Select that good, click **Sell** for an interested settlement, choose a definite quantity
in the confirmation, and create the order. Return to **Find buyer**. The left-hand count should have
fallen by exactly the committed quantity, even though the stack is still physically in storage.
Try to create another direct sale for more than the remaining count, including from a confirmation
left open long enough to become stale.

**Pass.** If 100 were present and 30 committed, the list shows 70. A second order may use at most
70; a stale attempt above 70 is refused with the live available and already-committed numbers, and
no extra order appears.

**Failure.** The list still shows 100, falls by the wrong amount, permits commitments totalling more
than physical stock, silently clamps a stale confirmation to a smaller sale, or creates any order
after saying the amount is unavailable.

### Find Buyer refreshes while open, at speed 3 and while paused

**Setup.** Arrange a bill or ordinary consumption job that will add or remove a visible Find Buyer
good while the page is open. Open **Intercolony** → **Selling** → **Find buyer** and do not press
**Refresh**.

**Steps.** At normal speed, let the stock change and time how long the left-hand count takes to
follow it. Repeat at game speed 3. For the paused case, pause immediately after a stack changes but
before the cached count has caught up, and leave the page open for two real-time seconds.

**Pass.** Each count corrects itself within roughly 1.5 seconds of wall-clock time (allow a little
observation delay), without the Refresh button. Speed 3 does not make the page scan three times as
often, and pausing does not freeze the update. If the selected quantity is now too high, it clamps;
if the selected good disappeared, the selection and buyer offers clear.

**Failure.** The old number remains until Refresh is pressed, refresh stops while paused, refresh
rate visibly scales with game speed, the right-hand offers remain priced for an impossible old
quantity, or the page repeatedly hitches during the scan.

### Buyer-pickup readiness deadline is fair

This is the most valuable visual check in the batch. The old code could fail an order after the
player had met the stated deadline, solely because the buyer travelled too slowly.

**Setup.** In **Intercolony** → **Selling** → **Market**, accept an opportunity whose timing
column reads `~Nd pickup`; its tooltip must say to mark the goods ready within the order deadline
and then expect approximately N more travel days. Have the exact goods in storage. Wait until fewer
days remain on the readiness deadline than the displayed pickup journey will take.

**Steps.** Before the deadline expires, open **Selling** → **Orders** and click **Mark ready**.
Confirm the row changes to the buyer-en-route countdown. Save in this state, quit to the main menu,
reload, and let the original readiness deadline pass while the buyer is still travelling.

**Pass.** The order remains Awaiting collection after the original deadline, retains its arrival
countdown across reload, and eventually completes when the buyer arrives. An otherwise identical
pickup left unready past its deadline should still fail, and Mark ready should be unavailable after
that deadline.

**Failure.** The ready order becomes Failed when the old deadline passes, the arrival countdown
resets or becomes a sentinel-looking number after reload, late Mark ready rescues an expired order,
or the UI tells the player to deliver a buyer-pickup order.

### Procurement cancellation forfeits payment and keeps the record

**Setup.** Open **Intercolony** → **Procurement** → **Request goods...**, submit a request, wait
for quotations, and accept one. Record the colony's silver before acceptance and after payment, and
record the order number and paid amount under **On order**.

**Steps.** Click **Cancel** on that open purchase. Read the confirmation, choose
**Cancel purchase**, read the resulting message, recount silver, then scroll to
**Concluded purchases**. Save, quit to the main menu, reload, and inspect Procurement again.

**Pass.** No silver is returned. The message names `Purchase #N` and the exact forfeited amount.
The order remains visible as `Cancelled by player — N silver forfeited`, with its outcome in the
tooltip, both immediately and after reload.

**Failure.** Silver is refunded or deducted twice, the message omits or misstates the forfeiture,
the order disappears, moves back to On order, loses its outcome on reload, or is shown as Supplier
default/war loss instead of player cancellation.

### Supply agreements name the exact history that caused them

**Setup.** Complete at least two sales of the same stackable good to the same settlement, and raise
commercial reputation with that settlement to at least 62 through completed trade. Keep a note of
the settlement, good and completed count. A single completion, failed/cancelled sales, and sales to
a different settlement must not qualify this pair.

**Steps.** From RimWorld's main menu, open **Options** → **General** and enable
**Development mode**. Load the prepared colony. Press **F12**, click the **orange bug icon** in the
top-right toolbar, type `Advance refresh`, and click the exact action
**Intercolony → Advance refresh**. Repeat refreshes until normal contract chance produces a
**Supply agreement offered** letter; do not use `Offer contract (force)`, because that diagnostic
action creates an offer without sending the player-facing causal letter. Open
**Intercolony** → **Selling** → **Contracts** and compare the offer with the letter and history.

**Pass.** The offered good is one the named settlement has actually bought at least twice, and the
letter says `<settlement> has bought <good> from you twice/N times and now wants a standing supply
agreement`. The Contracts row names the same settlement and exact good.

**Failure.** The offer is for an unsupplied good or category cousin, history leaks from another
settlement, one/failed/cancelled sale qualifies, the letter omits the causal history, the letter and
Contracts row disagree, or a settlement with no qualifying history receives an offer.

### Correction-batch state survives load and does not cross games

**Setup.** In one test colony, leave a direct Find Buyer order open so its stock is committed, mark
a buyer-pickup order ready so its buyer is en route, cancel a prepaid purchase so it is concluded,
and leave a history-based supply agreement offered or active. Record order IDs, quantities,
availability, paid/forfeited silver, statuses and countdowns.

**Steps.** Save, quit to the main menu, reload that save and inspect Find buyer, Orders,
Procurement and Contracts. Then quit to the main menu again and start a genuinely different colony
rather than loading another save from the first world. Open the same four pages there.

**Pass.** Reload preserves every recorded ID, quantity, commitment, terminal outcome, contract good
and buyer-arrival state without duplicating payment or letters. The different colony contains none
of the first world's orders, purchase history, contracts, buyer offers or selected Find Buyer state,
and produces no red Intercolony error.

**Failure.** Anything disappears or reopens on reload, a timer/silver amount changes for reasons
other than elapsed game time, an already-paid/refused action repeats, or any settlement/order/
selection from the first colony appears in the second. A red cross-reference, duplicate-ID or null-
relation error also fails the test even if the screens look correct.

### §115's first acceptance criterion — long-run stability

> *"Employees can remain for long periods without faction-state drift or save corruption."*

The self-test proves the arithmetic holds to five in-game years. It cannot prove the quest-lodger
mechanism does. This has been the open question in `LABOR_TECHNICAL_NOTES.md` since the Phase 15
spike, which measured a single instant.

**Steps.** Keep one employee — ideally open-ended — through several seasons, saving and reloading
along the way. Renew a fixed-term worker two or three times.

**Watch for.** Their `kindDef` staying correct, ideoligion intact, no raid-point inflation, and no
"could not resolve reference" on load.

### The Business report at full density (§117)

The only judgement still outstanding on the dashboard. Reviewed empty and with payroll alone; never
seen with **revenue, purchases and payroll on the report at once**, which is where crowding would
actually show and where §117's "not accounting software" line gets its real test.

**Steps.** Sell something (Find buyer → accept → deliver), buy something (Procurement), and have an
employee on payroll — then open **Business**. Ideally let a quadrum pass so the figures are real
rather than one movement each.

**Worth watching.** Whether four or five rows plus a rule and a net line still reads as a summary, or
starts to look like a table. If it tips, cutting a row is the right answer rather than shrinking the
font.

### Mod compatibility (§33 q18)

**Partially answered, 2026-08-05, incidentally rather than by design.** The play-test session for
Phase 25 was running with **Hospitality, Common Sense, RT Fuse, Tilled Soil and FSF Filth Vanishes**
active alongside Intercolony. A full hire ran through that load order — posting, applicants, take-on,
travelling employment — with no exception in the session. Hospitality is squarely in the risk class
named below, since it manages non-colonist pawns on the player's map.

Treat this as evidence, not a test: nobody exercised the interaction deliberately, and an absence of
exceptions in one session is not the same as checking that a colonist bar or work tab replacement
renders an employee correctly.

Still unexercised: anything assuming "player faction implies permanent colonist" — colonist bars,
work tab replacements, roster mods. A deliberate test wants one of those installed and an employee
on the map, looking at whether the employee appears where a colonist would and whether the mod
tries to assign them work.

---

## Proven in play

- ~~**Animal sale by buyer pickup, including the bond warning**~~ — 2026-08-16. After `b50b2e2`,
  order 4215 sold a chicken end to end. In a separate save, a bonded labrador retriever was sold and
  the confirmation displayed the bond warning correctly: the first player-proven animal trade.
- ~~**`Arrive buyers now` uses the real collection handler**~~ — 2026-08-16, `4fbec43`. The debug
  action arrived six orders through the ordinary buyer-collection path rather than bypassing it.
- ~~**The distributed build works as distributed**~~ — 2026-08-08, Steam Workshop item `3780094556`.
  Not a gameplay test: the thing it settles is that what a stranger downloads is what was built.
  Subscribed to the hidden item and verified what Steam *serves*, not what was uploaded — all 9
  release files byte-identical by SHA-256 to `dist/Intercolony-0.9.0`, the only extra being the
  `About/PublishedFileId.txt` RimWorld writes itself, and no `Source`, `reference`, `docs`,
  `Screenshots` or dev scripts. The local copy was removed first so it could not mask the download,
  and the log confirms the source:
  `Adding miannoni.intercolony(...\workshop\content\294100\3780094556)` with `Adding mods from mods
  folder:` empty. All five screens opened, then save and reload gave
  `[Intercolony] State loaded (schema 24, nextId 1).` — `State loaded`, not `State initialized
  fresh`, which is what distinguishes a real round trip from a silent re-initialization. No
  exceptions and no GUI-stack imbalance. **Also seen:**
  `[Intercolony] Dropped 15 candidate(s) left over from a previous game.` — the
  `LaborCandidateService` static-pool guard working in a build anyone can download.
  **Not proven by this:** anything about balance or long-run behaviour, and Market was never seen
  *populated* here because a just-created world has run no refresh cycle.
- ~~**Employee edge cases (§33 q12, q13, q20)**~~ — 2026-08-08, one session, zero exceptions.
  **Downed:** **Employee downed — treatment needed** fired twice for separate events. Eric was
  rescued and treated; employment stayed Active and wages continued. When a term expired while its
  worker was downed, **Employee term ended — recovery needed** fired and departure was held instead
  of attempting the vanilla exit path that excludes downed pawns. **Not yet seen:** that
  term-expired worker recovering and completing the final departure. **Captured:** **Employee
  captured — 1441 silver compensation** named Octopus as captured, not dead; 1441 was owed, 800 was
  paid from storage and 641 became debt to Brío Valley. Player.log recorded
  `Compensation (captured) for Octopus: 1441 owed, 800 paid, 641 outstanding.` and the closed
  employment as `[Captured]`. **Cleanup:** bed release was confirmed in play. **Relations:** while
  active, `Verify converted employees` reported `Blue: lover with Bolton (started tick 272486,
  formed during employment)`; after departure it reported Bolton's lover relation with former
  employee Blue as surviving, with counterpart faction The Nelou Treaty, `destroyed False`,
  `lodger False`, `employee False`, and no `REVIEW ANOMALY` marker.

  **Verification lesson:** shown letters previously went only to the letter stack, while Player.log
  recorded only suppressed letters. Searching the log for a shown letter therefore returned nothing
  and was misread as the test not having run. `IntercolonyLetters` now records every letter and marks
  each line as shown or not shown; do not treat an older log's silence as evidence that a letter did
  not fire.
- ~~**Phase 25 — hiring from a job posting with several applicants**~~ — 2026-08-05. The Phase 21
  play-test had exercised this, but only ever by taking on the *bottom* applicant row, which is the
  one arrangement that could not fail: `DrawPostingBlock` iterates applicants descending, and
  `TryAccept` closing a filled posting clears that same list mid-loop. Taking the top row of two
  threw `ArgumentOutOfRangeException` out of the draw pass. Fixed by deferring hire, turn-away and
  withdraw until after the loop and after `EndScrollView`, which is now in a `finally` so a future
  exception cannot leave the GUI stack unbalanced. Re-tested with a two-applicant posting: hired
  clean, no exception anywhere in the session.
- ~~**Phase 25 — condition floors, generated and refused**~~ — 2026-08-05. §118's decide-or-delete on
  `OrderLine.minHitPointsPercent`, resolved as *keep and make real*. Seen end to end: an order
  generated as `2x Psychic insanity lance (60%+ cond)`, and delivery refused with
  `2 offered below the condition floor (25% offered; 60% required)`. Buyer-pickup path confirmed via
  the disabled **Mark ready** tooltip. **Not seen:** the seller-delivery/caravan refusal path, which
  shares the same validator but has its own gizmo.
- ~~**Buyer pickup, end to end**~~ — 2026-08-07/08. Player.log recorded
  `[Intercolony] Order 441 completed by buyer pickup. Collected by the buyer. 458 units for 1354 silver.`
  and `[Intercolony] Order 630 completed by buyer pickup. Collected by the buyer. 1 units for 61 silver.`
  Order 630 was the packed-shelf test, which also proves minified furniture is found by the indexed
  validator and consumed correctly. **Still not seen:** the seller-delivery/caravan condition-refusal
  path, which shares the validator but has its own gizmo.
- ~~**Per-source trade classification**~~ — 2026-08-08. The classification result was 406 tradable
  defs: Core 337, Biotech 67 and RT Fuse 2, with zero from Common Sense, Hospitality, Tilled Soil and
  FSF Filth. This proves DLC and modded defs are classified correctly with no special handling; it
  does not prove that an item from those sources has been traded end to end.
- ~~**Phase 25 — save migration across five schema versions**~~ — 2026-08-06. A **schema 17** save
  loaded and walked 17 → 22 in one pass: job postings, open-ended employment, transition, ledger and
  condition floors. No errors, nothing dropped. Better evidence than the single-step 21 → 22 that was
  asked for. It also surfaced that the migration chain runs ascending 2→13 then *descending* 22→14 —
  harmless today because every step from 14 on is a bare log line, but a false contract against the
  "falls through to the next" comment. Reordered rather than left for the first migration that
  actually moves data.
- ~~**Save migration 22 → 23**~~ — 2026-08-08. Player.log recorded
  `[Intercolony] Migrating state from schema 22 to 23.` followed by
  `[Intercolony]   schema 22 -> 23: procurement fulfilment preference added; existing requests allow either.`
  Migration has now been exercised twice in play: the schema 17 save walked five steps to 22 on
  2026-08-06, and this save took the single step from 22 to 23.
- ~~**§88 safe passage, happy path**~~ — 2026-07-29. Hired, arrived, forced a war; worker walked out
  factionless and reached the border. No exceptions or warnings in the session.
- ~~**§43 death compensation during safe passage**~~ — 2026-07-30. A civilian on 19/day was killed
  walking out; billed 19 × 60 = 1,140 exactly, 800 paid from storage and 340 booked as debt.
- ~~**§88 safe-passage deadline**~~ — 2026-07-30. Worker did not get clear in two days; detention
  penalty applied and they rejoined their own faction on the map, as the letter says they will.
- ~~**Severed contract across save/load**~~ — 2026-07-30. An autosave holding a closed record with a
  live pawn reference, past its deadline, loaded twice and resolved both times.
- ~~**Phase 22 — open-ended employment and notice (§36.4)**~~ — 2026-08-03. All three dismissal
  options present and correct: 3-day minimum notice, pay-in-lieu arithmetic verified at 3 x 23 = 69,
  letter clear. Found a display bug in the same pass — the row printed the open-ended sentinels raw
  as `0d ... 34028230000000000000000000000000000000d left`, and never showed that notice was
  running. Both fixed; *the corrected row display has not itself been re-checked.*
- ~~**Phase 22 — renewal, both halves (§115)**~~ — 2026-08-03. Treated well, the worker asked to stay
  at 26/day against 25 and renewing extended the same employment in place. Treated badly — one
  drafted civilian — no offer came, and the refusal named exactly which thing caused it.
- ~~**Phase 22 — supply agreement renewal (§115, §107)**~~ — 2026-08-03. Offer created, accepted,
  agreement credited to completion, and the settlement offered another run answerable in the
  Contracts tab. Found a real bug on the first attempt: completion was unreachable except through an
  order resolving, so a credited agreement sat Active forever with an empty reason string.
- ~~**Phase 24 — ledger and Business tab**~~ — 2026-08-03. Ledger self-test 23/23, including the
  assertion that it agrees with real silver leaving storage. Business tab reviewed empty and
  populated and judged "a summary, not accounting software"; the runway line works and was called the
  best line on the screen. Three layout bugs found and fixed. **Still unseen:** the report with
  revenue, purchases and payroll present at once, which is where crowding would show.
- ~~**Phase 21 — the Posts tab and posting dialog (§35.2)**~~ — 2026-08-03. Band updates live
  (skill 8→18 moved 139 workers to 8, and the band 25–84 to 50–71; switching to security contractor
  took it to 125–177). Verdict escalates amber→red. The no-applicants letter names the reason. Two
  clipped strings and an invisible selection state found and fixed.
- ~~**Phase 23 — a worker becomes a colonist (§44, §116)**~~ — 2026-07-31, all three routes. Paid:
  colonist in place, 3,583 debited exactly as quoted. Defected: colonist in place, faction hostile at
  -80, bookings voided, nothing paid. Declined: stays an employee. `Verify converted employees`
  reported PASS on both conversions, and again after four in-game hours at 3x — the pawn does not
  drift to the map edge, which was the whole risk.
- ~~**Cross-game state leak**~~ — 2026-07-30. Quicktest world → new colony → hire → save → reload,
  which previously produced duplicate thing IDs and a null-relation flood. Clean.

---

## Not testable, and deliberately so

- **Non-minifiable buildings cannot be traded.** A caravan physically cannot carry them. Permanent
  exclusion, not a gap — recorded here so it is not rediscovered as a bug.
