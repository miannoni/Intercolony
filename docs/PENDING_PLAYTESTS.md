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

### Self-tests written but never run

These are dev actions, not play-tests, but they are outstanding verification and belong on the same
list. All of them: **F12** → **orange bug icon** (top-right toolbar) → type the search term → click
the action. Output goes to the debug log; no need to copy anything out, the dev script reads it.

None of the assertions added in the 2026-08-09 correction batch has been clicked. A clean build and
game launch prove that the assembly loads; they do not prove these branches.

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

### Schema migration 24 → current — see `docs/SCHEMA_24_TO_CURRENT.md`

**Moved to its own file on 2026-08-09**, because the chain kept growing during one development
run and the owner chose — reasonably — to test the whole chain once at the end rather than
interrupt the work for each step. Every step is additive with no data to move, so the risk is
the same either way. That file is kept current as steps land; **read it rather than this
summary** when the time comes to test.

The short version is below and may lag behind the file.

Added 2026-08-09. **This is the highest-value single check on this list**, because one action
settles several things at once and because migration is the one failure mode that damages a save.

Schema moved **24 → 27** in one day: 25 added animal specifications, 26 added each sales order's
fulfilment colony, 27 added the animal health and gestation floors. Every step is additive with no
data to move, and each was seen running — but only in **isolated throwaway RimWorld installations
with a stripped mod list**, never in the real load order.

**Steps.** Launch the game normally and load any existing save.

**Pass.** Player.log contains, in order:

```
[Intercolony] Migrating state from schema 24 to 27.
[Intercolony]   schema 24 -> 25: optional animal specifications added; existing records remain goods.
[Intercolony]   schema 25 -> 26: sales orders now remember their fulfilment colony; existing orders fall back to the first player home.
[Intercolony]   schema 26 -> 27: animal health and gestation floors added; existing specifications have no floors.
```

(The starting number depends on the save. A save already at 27 prints `State loaded (schema 27, …)`
and no migration lines — that is also a pass, it just does not exercise the chain.)

Then **save, quit to the menu, and reload**. The second load must say `State loaded (schema 27, …)`
and **not** `State initialized fresh` — that distinction is what proves the round trip rather than a
silent re-initialization.

**Failure.** Any red error during load, any order or purchase reported as dropped, a lower schema
number than expected, or `State initialized fresh` on the reload.

**Why this cannot be settled any other way.** `dev.ps1` launches with `-quicktest`, which creates a
*new* world — and a new world initializes at the current schema, so the migration chain never runs.
Its log reader also targets the real user profile while a sandboxed game writes elsewhere, so the
displayed log can be stale and show an old schema entirely. Neither the dev loop nor a self-test can
prove this; only opening a real save can.

### Two debug actions exist to make the tests below bearable

Added 2026-08-10. **F12** → **orange bug icon** → type the name.

- **`Arrive purchase orders now`** — pulls every confirmed purchase order's ready time to now
  and runs the ordinary advance, so procurement can be tested without playing out the
  supplier's lead time. It moves the clock and uses the real fulfilment path rather than
  calling delivery directly, so the status transitions, the animal branch and the refund
  branches are all genuinely exercised. Orders already waiting to be collected are reported
  separately and left alone — they are waiting for a caravan, not for time.
- **`Explain unsold animals`** — lists the animals some trader buys but none sells, and says
  whether the setting below is on. With Core + Biotech this should be exactly one entry:
  Thrumbo, 4000 silver.

**There is no equivalent for the sell side yet.** Buyer-pickup tests still need the buyer to
travel in real game time, which makes the designated-animal test below slow. Worth adding an
"arrive buyers now" sibling before attempting it.

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

### Animal trade, end to end — the whole feature is unplayed

Added 2026-08-10. All five animal slices are built and **not one of them has been seen
working**. Everything below is believed correct and unproven. This is the largest block of
unproven work in the project, and it is deliberately listed as one entry because the pieces
only mean anything together.

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

- **Pass:** marking ready sets aside particular animals; when the buyer arrives *those*
  animals go and you are paid.
- **Must check:** after marking ready, **kill or slaughter one of the designated animals**
  before the buyer arrives. The order must deliver the rest and treat the missing one as a
  shortfall — it must **not** quietly substitute another matching animal from your colony.
  Substitution would mean parting with an animal you never agreed to sell.
- **Also worth one attempt:** two pickup orders for the same species at once. They must never
  designate the same animal.

**Not proven by any of this:** balance. Whether animal prices are sane against the rest of
the economy needs play, not a test.

#### Animal specification, matcher and eligibility — `Run animal self-test`

Added 2026-08-09 with the `AnimalSpec` slice (schema 25). **F12** → **orange bug icon** → type
`Run animal self-test` → click **Intercolony → Run animal self-test**.

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

**Also worth trying once:** abandon the colony an order was marked ready on, then let the buyer
arrive. It should fall back rather than throw. Both a same-session abandonment and one across a
save/reload are covered by different guards, so ideally test both.

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
