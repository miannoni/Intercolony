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

Self-test items now run unattended through the dev test bridge and are closed as a class.
They no longer need individual entries here, because every pass reports its own failures and skips.
What remains deliberately asks a human to watch two colonies, mod interactions, behaviour over seasons, or whether a screen reads well.
A shipped fix recorded in `PROGRESS.md` is still not a play observation, so it does not close those items.

### The five-day cash flow table needs a human read

Added 2026-08-29 on branch `1.0.1`. The new five-day cash flow table on the **Business** tab has
passed its self-tests, but no person has looked at it in play. It sits between the **Where you stand**
section above and the brand section below.

**Steps.** Open **Business** at several window sizes and check that the section renders as a transposed
table: **Day 1** through **Day 5** are columns followed by a **Next 5 days** column, while
**Expected revenue**, **Expected expenses**, and **Net** are rows, without overlapping the **Where you stand**
section above it or the brand section below it at any window size.
Hover the heading and confirm its tooltip appears and says the table counts commitments already made —
open sales orders, agreement cycles falling due and scheduled payroll — and does not predict spot sales.
Hover a **Day 1..Day 5** column and confirm its tooltip explains that each column is a rolling 24-hour
window from now rather than a calendar day, then judge whether that reading is clear and not confusing in play.

Leave the **Business** tab, change something that moves money, return to the tab, and confirm that the
numbers moved. Repeat this after selecting **Business** again to verify that the table refreshes every time
the tab is selected, not only when the window is opened.

With a real colony that has an active sales agreement and an employee, check that the numbers are
recognisable: payroll lands on the payday rather than being spread across every day, and a known contract
delivery shows the payment it will really pay. A sales order whose buyer is already collecting — shown as
**En route — N.Nd** on the **Selling → Orders** screen — must appear in the revenue on the day the buyer
arrives. Seven such orders worth 914, 1779, 480, 163, 536, 175 and 617 silver were invisible because the
table booked them on their deadline instead; confirm that the revenue matches the orders the player can see
arriving. Purchase orders deliberately contribute nothing because
they are paid in full when the order is created, so an apparently missing purchase-order expense is
correct behaviour and not a bug to report.

Finally, decide whether five days is the right window and whether **Day 1..Day 5** is the right label.
Those are calibration questions for the end-of-1.0 sitting, not defects.

### Procurement Contracts has never been used by a human

Added 2026-08-25 on branch `1.0.1`. In 1.0 this tab was an **Under development** placeholder,
although `ProcurementContractService` was complete and self-tested; no player has proposed a
standing purchase through the UI. It now lists procurement agreements, badges the tab with the live
count, and offers **Propose procurement agreement**, **Cancel** on a pending proposal,
**Accept/Decline** on a supplier's final counter, and **Withdraw** on a live or suspended agreement.

**Steps.** On a real colony, check that the propose dialog lists settlements and items, wait for the
supplier's delayed answer and read it on screen, then accept a final counter and verify the agreement
uses the counter's terms rather than the original ones. Let cycles arrive and be paid, and check the
row layout at **1.75x UI scale**. Agreements cannot be renewed; a term ending is expected behaviour,
as recorded in `docs/BACKLOG.md`.

### Supplier Market framerate fix needs to be felt, not measured

Added 2026-08-25 on branch `1.0.1`. Opening **Procurement → Market** used to drop the framerate for
as long as it stayed open. Rows and measured heights are now cached and rebuilt on entry, sort change,
listing-count change, after a purchase, and every half second; self-tests exercise the read model but
cannot observe framerate.

**Steps.** On a mature colony with many supplier listings, open the tab and confirm the game runs
normally. Sort a column and make a purchase, then confirm the table updates immediately rather than
lagging up to half a second behind. This remains unproven until the fix is felt in play.

### Selling Market framerate fix needs to be felt, not measured

Added 2026-08-28 on branch `1.0.1`. Opening **Selling → Market** used to re-filter and re-sort the
whole opportunity list every `OnGUI` pass, measure every row twice, and draw every row. Rows, heights
and the summed height are now cached and only visible rows are drawn; self-tests exercise the read
model but cannot observe framerate.

**Steps.** On a mature colony with many offers, open the tab and confirm it runs normally. Change a
filter, change the sort, and accept an offer; confirm the table updates immediately rather than
lagging up to half a second behind. A stale cache is the specific risk, and this remains unproven
until seen in play.

### Supply agreement proposal dialog needs a readability pass

Added 2026-08-28 on branch `1.0.1`. **Propose supply agreement** now has cadence in days, total
deliveries, and a seller-side fulfilment choice. Self-tests cannot settle whether the three-column
layout reads well at **1.75x UI scale**, whether the term-length clamp explains itself, or whether
the labelled terms rows make the commitment legible before sending.

**Steps.** At **1.75x**, send a proposal and inspect those controls and terms, then wait for the
settlement's answer, accept it, and confirm the chosen fulfilment survives into the live agreement
without being asked again at acceptance. This remains unproven until seen in play.

### Procurement Orders history button needs a layout check

Added 2026-08-28 on branch `1.0.1`. **Procurement → Orders** moved **Clear completed history** from
the concluded-orders section header to the page heading row.

**Steps.** At narrow window widths and **1.75x UI scale**, confirm it does not collide with the
heading and disappears when there is nothing clearable. Compare **Selling → Orders**, where the
equivalent button deliberately remains in its old position, and decide whether the difference is
acceptable. This remains unproven until seen in play.

### Proposal acceptance read-out needs campaign calibration

Added 2026-08-28 on branch `1.0.1`. **Selling** and **Procurement** propose screens now show one of
seven bands — **Hopeless**, **Very unlikely**, **Unlikely**, **Even odds**, **Likely**, **Very likely**,
**Near certain** — computed from continuous proposal appeal. The band is shown by default; a new
mod setting reveals the numeric appeal percentage on both screens. A test fixture spread them
sensibly: `0.80x` the reference price read **Very unlikely**, the reference rate **Even odds**, `1.05x`
**Likely**, and `1.20x` **Near certain**. Play must settle whether the bands feel right across real
settlements with varying reputation and brand, whether the names scan instantly, and whether the
percentage setting is discoverable. Continuous appeal changed real Selling acceptance odds because
the delayed answer now rolls against it, so a satisfying success rate is a calibration question for
play, not a self-test result.

**Steps.** On a real campaign, propose from both screens across settlements with different reputation
and brand. Read the band at a glance, find and toggle the percentage setting, and let delayed answers
resolve. Judge whether the names and resulting success rate feel right. This remains unproven until
seen in play.

### Agreement terms layout needs a two-dialog scroll check

Added 2026-08-28 on branch `1.0.1`. On both **Selling** and **Procurement** proposal dialogs, the
terms rows used to render at the far left over the settlement list because their hardcoded x was only
correct inside a scroll view; they now render under their own heading in the right column.

**Steps.** At **1.75x UI scale**, open both propose screens and confirm the terms sit under the heading.
Use long item labels and settlement names, and check that the rows do not collide with the controls
above. Add enough rows to require the section's scrollbar and confirm it still behaves. That scrolling
branch was deliberately left untouched and has not been seen since the change, so this remains
unproven until seen in play.

### 1.0 calibration sitting — Stage 8 remaining play

Stage 8A's full save/load matrix, Stage 8B's 42 → 56 migration matrix, and Stage 8C's seven-path
performance profile are complete. The following work still needs a human at the keyboard. Run it
on a real colony, record the world and load order, and write down decisions and visible outcomes;
do not turn a self-test pass into a play claim.

#### Economic sanity (§8.3)

Play several market refreshes across multiple settlement archetypes and answer these nine questions:

- Can I tell agricultural from industrial/affluent economies?
- Do shortages/surpluses persist enough to plan around?
- Do they eventually normalize?
- Are events noticeable without dominating every decision?
- Does one region sometimes differ meaningfully from another?
- Does Procurement reflect the same market conditions as Selling?
- Are there obvious arbitrage loops?
- Are ordinary goods still buyable/sellable often enough to be useful?
- Does scarcity create choices rather than merely empty screens?

Record decisions, not just generated rows.

#### Brand sanity (§8.4)

Verify in normal play:

- high-skill production can build a valuable brand;
- mediocre output can dilute it;
- pivoting to an unrelated industry does not penalize the new industry beyond the tiny carryover floor;
- moving from revolvers to rifles meaningfully carries reputation;
- brand premium is useful but not an infinite money printer;
- a player can understand why their brand changed.

#### Negotiation sanity (§8.5)

Verify:

- negotiation is optional, not required for every trade;
- outcomes feel connected to terms and relationship;
- absurd demands get rejected even at high reputation;
- strong brand helps the relevant product;
- events matter;
- renegotiation is useful when a real obligation becomes difficult;
- failed negotiation does not destroy the original opportunity/order.

Record the original terms, the attempted change, the response, and the surviving order or
opportunity after each failed or accepted negotiation.

#### Procurement parity (§8.6)

Complete each loop separately. Record the request or order ID, quantity, payment, destination,
terminal status, and whether the result is visible after reload:

1. `Supplier Market -> PurchaseOrder -> delivery`: accept a supplier listing, wait for delivery,
   and verify that the goods arrive at the ordering colony with the quoted properties.
2. `Supplier Market -> PurchaseOrder -> pickup`: accept a supplier listing, collect it with a
   caravan, and verify the goods and payment at the ordering colony.
3. `RFQ -> quote -> PurchaseOrder -> delivery`: request goods, accept a quote, wait for delivery,
   and verify the delivered quantity, price, and destination.
4. `RFQ -> quote -> PurchaseOrder -> pickup`: request goods, accept a quote, collect the purchase
   with a caravan, and verify the delivered quantity, price, and destination.
5. `Procurement contract -> cycle -> PurchaseOrder -> completion`: let one recurring procurement
   cycle create and resolve a purchase order, then verify completion and the commercial history.
6. `Procurement contract -> supplier failure -> refund/outcome`: force or observe a supplier
   failure, then verify the refund or other stated outcome, the terminal record, and its history.

#### UX pass (§8.7)

Review the relevant Selling, Procurement, Relations, negotiation, event, brand, and Commercial
History surfaces at **1.0x, 1.25x, 1.5x, and 1.75x UI scale** where practical. At each scale, check:

- no paragraph-heavy dialog regressions;
- pricing factors remain legible;
- event cause is visible;
- brand is understandable;
- negotiation final terms are explicit;
- Procurement tabs do not feel like a different product from Selling;
- Commercial History is readable when dense.

Use the measured text/layout rules already established in the project. Record the scale, screen,
content density, and any clipping, overlap, unreadable text, or ambiguous term.

#### Deferred Stage 3 play — criteria 9 and 10

**Criterion 9 — does event frequency flood normal play?** Play without forcing events for several
market refreshes. Generation rolls roughly a 12% chance per refresh and allows at most 3 concurrent
events. Record whether normal stretches, one-region events, occasional overlap, and no permanently
crisis-bound world are what the player actually sees. Retune the named chance or cap if the observed
frequency is wrong.

**Criterion 10 — does an event produce an obvious decision?** Use the dev action to force a drought
or war mobilization, then play through it. Record whether it changes a real choice: what to produce,
what stock to hold, which buyer to choose, or whether to delay a purchase. If the event only changes
numbers without changing a decision, record that the magnitude needs retuning. End the event and
watch the pressure tail: the live modifier should disappear while its pressure remains and decays
over roughly 25 refreshes.

#### Deferred Stage 2 play calibration

Run this on a real colony, not a `-quicktest` world. During several refreshes, answer:

1. Does the market read as alive rather than flat or chaotic?
2. Do regions actually form, with diffusion visible without making the world uniform?
3. Does a price breakdown explain itself, showing `Current shortage` or `Current surplus` only when
   conditions are moving the number?
4. Does a procurement quote read the same way, using `Local scarcity (shortage)` or
   `Local scarcity (surplus)` consistently with Selling?
5. Is Stage 1 criterion 7 met: is a settlement's economy legible from the Market listing and
   Relations tooltips without debug numbers?

For the shortage surfaces, enable Development mode, open the world map, press `/`, choose
**Intercolony -> Shock settlement economy**, click a settlement already used for trade, and apply
`manufactured: demand shortage` three or four times. Use **Dump effective economy** to confirm the
pressure, inspect a live offer and a procurement quote from the shocked settlement, then compare an
untouched settlement. Record the baseline, current condition, visible explanation, and whether the
player could make a different choice.

### The whole suite, in one action

**This no longer needs a human.** Since the dev test bridge landed, the whole suite runs from a
shell and reports its own counts:

```powershell
powershell -ExecutionPolicy Bypass -File dev.ps1 test all -Fresh
```

`-Fresh` restarts into a clean `-quicktest` world first and refuses to run at all rather than
claim an isolation it cannot verify. Exit code 0 means clean, 1 means assertions failed, 2 means
everything else — including a run whose assertions passed but whose log gained new exceptions.
See `docs/DEV_TEST_BRIDGE.md`. **Do not ask for the manual click-through below unless the bridge
itself is broken.**

The manual route, kept because it is the fallback when the bridge will not start:

**`Run ALL self-tests`.** Enable **Development mode** (Options → General), load a colony **with
a map**, press **F12**, click the **orange bug icon** top-right, type `Run ALL`, and click
**Intercolony → Run ALL self-tests**. Read it with
`powershell -ExecutionPolicy Bypass -File dev.ps1 log`.

It runs all seventeen suites, prints one table, and ends with a `VERDICT:` line. That line is
the whole answer:

- `all clean` — nothing more to do.
- `no failures, but assertions were skipped` — a skipped assertion is not proof; the table says
  which suite.
- `no failures, but not everything ran` — you were probably on the main menu rather than in a
  colony; ten suites need a map.
- `FAILURES` — the full output of only the failing suites is printed underneath it.

**It also answers the question no single suite can.** Suites drive real transitions on synthetic
orders, which since Stage 0.3b writes commercial events and from Stage 2B will move market
pressure. Each runs inside a guard that restores what it found, and a broken guard is invisible
from inside the suite that tripped it — so the runner records the world's counts before and
after and prints a `did anything leak into the world?` block. `OK` on both lines means the
guards held. `LEAK` means stop: a diagnostic is writing into the player's real history.

The individual per-suite actions still exist for when one of them is being worked on.

### OPEN — one full-suite assertion failed once in 18 runs, and its identity was lost

**Observed 2026-08-22. NOT REPRODUCED in 37 further runs — §18's outcome, not a clean bill.**
One `dev.ps1 test all -Fresh` run failed a single assertion. **Thirty-seven subsequent runs all
passed**, at 1002–1003 passed / 0 failed / 13–14 skipped, world-pawn delta 0, both leak guards `OK`
and clean logs throughout. Twenty of those were run specifically to catch it, after the failure
archiving below was in place, and it did not recur.

§18 says to trace the path, force the condition, instrument the boundary, and if it still will not
reproduce, record `NOT REPRODUCED`, prove what can be proven, and continue rather than writing a
speculative fix for a defect the roadmap expected. That is what this is. **It is not evidence that
nothing is wrong** — one occurrence at ≤1-in-38 is entirely consistent with a rare race or a
world-shape edge that these worlds did not produce.

**Which assertion it was is not known**, and that is the part worth fixing rather than the rate.
`dev.ps1` wrote the bridge's assertion output to a single fixed path that every later run
overwrote, so seventeen passes destroyed the only record of the one failure between them. The
harness now archives a timestamped copy whenever a run is not clean, so the next occurrence
identifies itself.

**Do not assume it was the retention work committed the same day.** Three separate mutations proved
those thirteen assertions sensitive, and they are deterministic — they build their own fixtures and
sample nothing from the world. The failure appeared in the first run after that change, which is
suggestive and is exactly why it is written down, but one occurrence in eighteen is not attribution.
Roughly a thousand assertions ran in each of those runs and several suites size themselves to
whatever the loaded world contains.

**This project has been here before and the rule came from it.** Two assertions were once shipped in
a single day having been mutation-tested and gone red, and both were still flaky — same code, one
fresh world green and the next red. **Mutation proves sensitivity; only repetition proves
stability.** A one-in-eighteen failure is precisely what the four-fresh-worlds standard exists to
surface, and precisely what a single run would have called clean.

**How to close it:** run the suite repeatedly and read the archived output the next time one is not
clean. If it recurs and names a statistical assertion, enlarge its sample rather than loosening its
comparison — loosening is how a flaky test usually gets "fixed", and it makes the assertion pass with
the feature deleted.

### The dev test bridge — what it has not shown

The bridge was proven against a live game on 2026-08-21: both builds, the runtime gate, all seven
commands, malformed and oversized and unknown requests, the CLI's three exit codes, and the eight
MCP tools driven over stdio. These are the parts that were **not** exercised.

- **The bridge has only ever run on `-quicktest` worlds.** Every run generated a fresh world. It
  has never been pointed at a real save, so it has never seen a schema migration, a colony with
  history, or a world with more than a handful of world pawns. `dev.ps1 run -MainMenu` exists for
  loading a real save; the bridge has not been used that way.
- **Two colonies.** Every bridge run had one map. `Find.CurrentMap` is what `tests.run` resolves,
  and the mod has a documented history of one-map assumptions being wrong — the 0.9.0 buyer-pickup
  defect was exactly that. A suite run through the bridge with two colonies loaded is untested.
- **A genuinely crashing suite.** `tests.run` claims to report an exception with its text and
  `success=false`, and the runner treats output with no summary line as not having completed.
  Neither path has been triggered on purpose, because no suite currently throws.
- **Port-in-use reporting.** The `AddressAlreadyInUse` branch names the likely cause, but two
  bridge-enabled games have never been run at once to see it.
- **The command timeout.** Ten minutes, chosen because the full suite runs synchronously on the
  main thread. No run has come close, so the timeout path and its tombstoning of an abandoned
  command are unexercised in play.
- **Anyone else's machine.** `.mcp.json` uses a repo-relative path and was driven from this repo
  root, but has only been loaded on one machine, with one Node version, one RimWorld install, and
  a `Mods\Intercolony` junction that had to be repointed by hand to test at all.

### 1.0 program — Stage 3, the two event criteria (added 2026-08-22)

**For the single calibration sitting at the end of 1.0**, alongside the Stage 2 gate below. Eight of
Stage 3's ten acceptance criteria are closed by assertion; these two are judgements only a player can
make.

**You can now cause any of this on demand** — §3.8 exists so nobody waits on RNG. Dev mode, world
map, press `/`, then under **Intercolony**:

| Action | Use |
|---|---|
| `Force economic event` | Click a settlement, pick Poor harvest / War mobilization / Construction boom / Epidemic |
| `Dump economic events` | Type, anchor, scope, days left, settlements affected, modifiers |
| `End economic events now` | End them early to watch the pressure tail decay |
| `Dump effective economy` | Baseline vs pressure vs effective, per category |
| `Shock settlement economy` | Direct pressure shock, four steps per category |

**Criterion 9 — does event frequency flood normal play?** Generation rolls roughly a 12% chance per
market refresh, capped at 3 concurrent events. §3.5 wants long stretches of normality, sometimes one
region affected, occasionally two overlapping, and never a world permanently in crisis. Play without
forcing anything and judge whether that is what you get. **This is a retune, not a rewrite** — the
chance and the cap are named constants.

**Criterion 10 — does an event produce an obvious decision?** The test is whether a drought or war
mobilization ever makes you *do something different*: change what you produce, hold stock back, pick
a different buyer, delay a purchase. If events are only visible as slightly different numbers and
never change a choice, the magnitudes are too small — also a retune.

**Worth watching while you judge those two:** whether the *tail* reads correctly. When an event ends,
its live modifier disappears but the pressure it caused remains and decays over roughly 25 refreshes.
That is deliberate (§3.4) and is what makes an event feel like it had consequences rather than being
switched off. If the aftermath instead feels like the event never ended, `StartShockFraction` is the
constant to move.

### 1.0 program — Stage 2, the 2K play gate

**Added 2026-08-21 when 2J closed the last Stage 2 code slice.** Every remaining Stage 2 question is
a judgement about feel or text, which is what §20.4 says a self-test cannot settle. Do these in one
sitting on a real colony, not a `-quicktest` world.

1. **Does the market read as alive rather than flat or chaotic?** Every Stage 2 coefficient was
   chosen conservatively and documented as retune-at-2K: `ReversionRetention` (0.82, a shock decays
   over ~25 refreshes), `NudgeValueScale`, the chain table and `DiffusionCoefficient`. Expect to move
   numbers, not structure.
2. **Do regions actually form?** Diffusion moves one hop per refresh within 40 tiles. If the world
   reads as uniform, or if nothing ever spreads, the coefficient is wrong in one direction or the
   other. The 2I entry names a known gap worth closing only if this looks wrong.
3. **Does a price breakdown explain itself?** New in 2J: a market opportunity, Find Buyer tooltip,
   sell confirmation or animal preview should show `Current shortage` or `Current surplus` as its own
   row when conditions are moving the number, and nothing at all when they are not.
4. **Does a procurement quote read the same way?** Its scarcity row is labelled
   `Local scarcity (shortage)` / `(surplus)`. The same circumstance should read the same way whether
   the player is buying or selling — that symmetry is the point, and only reading both settles it.

**Exact steps for 3 and 4**, since they need a shortage that will not occur on demand:

1. Turn dev mode on: **Options → Development mode**.
2. Open the world map (**the globe button**, bottom right), and press **`/`** — the debug actions
   menu (`Dev_ToggleDebugActionsMenu`, bound to Slash on this machine; check
   `Config/KeyPrefs.xml` if it does not open).
3. Choose **Intercolony → Shock settlement economy**. The cursor becomes a world tool.
4. **Click a settlement you already trade with.** A menu lists four steps per category; pick e.g.
   `manufactured: demand shortage`. Click it **three or four times** — one step is 0.30 and the
   effect is deliberately modest. The log names the resulting pressure after each click.
5. Confirm it landed: **Intercolony → Dump effective economy** prints baseline, pressure and
   effective for both sides of every category on each disturbed settlement.
6. Now open the Intercolony tab and **hover the price on an offer from that settlement**. Under a
   shortage the breakdown should carry a `Current shortage` row of its own, *beside* `Local demand`
   rather than folded into it.
7. **Then hover an offer from an untouched settlement.** It must show `Local demand` alone — no
   `x1.00` condition row. A row that says nothing buries the row that matters, and its absence is
   as much the design as its presence.
8. For step 4, raise a purchase request (**Find seller**) against the shocked settlement and hover
   the quoted price. Expect `Local scarcity (shortage)`. Note that this one is a *label* only: the
   number was already moving with pressure before 2J, because that factor is affine in supply and
   cannot be split without changing the quote.
5. **Stage 1 criterion 7**, carried here from the Stage 1 gate: whether a settlement's economy is
   legible from the Market listing and Relations tooltips without debug numbers. Same sitting, same
   kind of question.

(Matteo) Test Result: shortage did appear in the UI, as intended, but price didnt move much...

**Analysed from his log, 2026-08-21. The explanation surface works; the price surface could not
have moved, and that is a design finding rather than a coefficient one.**

What the log shows. He shocked `Craps's Settlement`, commodities demand, five times:

```
Craps's Settlement: commodities demand shortage; resulting pressure 1.300.
Craps's Settlement: commodities demand shortage; resulting pressure 1.600.
Craps's Settlement: commodities demand shortage; resulting pressure 1.600.   (x3 more)
```

**It saturated at `MaxPressure` (1.60) on the second click**, so this was the strongest shortage the
system can express — clicking more did nothing, which is correct and is why the value is logged.

**Why the board did not move, and could not have.** Three separate things stack up:

1. **A listed opportunity never re-prices.** `MarketOpportunity.unitPrice` and `priceExplanation`
   are computed once at generation, and `MainTabWindow_Intercolony.cs:1187` renders that stored
   string. So a shock is invisible on every offer already on the board — in price *and* in
   explanation. **This is deliberate and should stay**: the standing rule is that a deal records the
   market rate it was struck against, and re-pricing an offer under the player would be worse than
   not moving it.
2. **The board is pinned at its ceiling.** The market suite's own note on this run reads
   `12 extra refreshes took live offers from 100 to 100, ceiling 100`, and the refresh log shows
   turnover of 0–21 per cycle. New offers — the only ones that can carry the shock — arrive slowly.
3. **He shocked one settlement out of 358.** Even after turnover, the chance that a given new offer
   comes from that settlement is tiny.

So what he saw is consistent and correct: the shortage row appeared where prices are computed
**live** (Find Buyer, and the sell confirmation via `RepriceForQuantity`), and nothing changed on
the Market board, which is frozen by design.

**A second candidate explanation, derived from the constants rather than observed, and it is
testable.** If he was looking at a **live-priced** surface (Find Buyer, or the sell confirmation)
rather than the Market board, then the board explanation above does not apply and something else
did. The arithmetic says the price-sanity clamp is the likely culprit, and that it bites hardest
exactly where a shortage should matter most.

Let `d` be the settlement's baseline demand for the good (`BaseDemandFor(def, category)` — the
category weight times the good's affinity). Pressure maxes at `MarketPressureService.MaxPressure`
= 1.60, and `EffectiveEconomyService.MaxCondition` is 2.0, so the strongest possible demand
condition is 1.60. Pricing then clamps the composed value to `[0.4, 2.0]`, so the most a maximum
shortage can multiply the price by is:

```
min(2.0, d * 1.60) / d
```

- `d ≤ 1.25` → the full **×1.60**. The shock arrives unclipped.
- `d = 1.60` → only **×1.25**.
- `d ≥ 2.0` → **×1.00 — a maximum shortage changes the price by nothing at all**, because the
  settlement's standing appetite had already reached the ceiling on its own.

So the harder a settlement already wants a category, the less a shortage in it can move the price —
which is backwards from what a player would expect, and commodities is a category some archetypes
weight heavily. The `[0.4, 2.0]` bound is a *price sanity* clamp that predates market pressure
entirely; it was never chosen with a second multiplicative layer underneath it in mind.

**How to settle which explanation it was, in one look:** the new world-map Economy tab shows the
settlement's baseline demand beside its current condition. If `d` for that category is near or above
2.0, this is the clamp and the fix is the bound, not the pressure. If `d` is well under 1.25 and the
price still barely moved, neither explanation holds and it needs a fresh look.

**The 2K conclusion this points to: the lever is visibility, not `ReversionRetention`.** A shortage
at full strength decays below the prune epsilon in about 19 refreshes — which is exactly what
happened here, and why market pressure read `0 settlement(s)` by the time the suites were run
(`0.60 × 0.82¹⁹ ≈ 0.013`). Mean reversion behaved as designed. The problem is that a shortage's
whole life can pass without the player being able to see it anywhere they normally look. Retuning
the decay would not fix that; putting the economy where the player looks would — see the world-map
Economy tab raised from his Stage 1 feedback below.

**Not on this list, because it is proven:** the 42 → 43 → 44 migration ran on the real 22.5 MB
`Fenhana` colony with zero exceptions and the full suite then passed 944/0/9 against it.

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

#### ~~Timeline self-test — never run~~ — DONE 2026-08-21 by Matteo

**47 passed, 0 failed**, and the line that actually matters is present:
`commercial timeline restored to 10 record(s).` The suite deliberately overfills the timeline past
its 1,000-record cap and prunes it, so a wrong restore would have destroyed real history; it did
not. Run on the real colony after a live 42 → 44 migration, not on a throwaway world.

The steps below are kept because they are still the manual route.

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

(Matteo) Test Result: Ran it, didnt check the logs - you look.

#### Stage 1 — profile and market suites, plus a look at the new tooltip

`Run profile self-test` and `Run market self-test` (F12 → orange bug icon). Stage 1 rewrote
exact-good demand, so the market suite's per-good assertions are the ones that matter: they now
read the affinity-spread constant rather than the old 0.55–1.45 cycle range, and two new checks
prove the affinity depends on seed and def alone.

**Pass.** Both report 0 failed.

**CORRECTION 2026-08-21.** This entry used to say "specifically look for" three assertions by name —
`exact-good affinity depends only on seed and def`, `exact-good affinity differs between
settlements`, and `per-good demand crosses the interest threshold both ways`. All three exist
(`IntercolonyMarketSelfTest.cs:229`, `:231`, `:281`) but **the suite never prints a `PASS` line**,
only bracketed notes and a summary, so the instruction could not be followed and looking for them
would suggest they were missing. `0 failed` is the whole signal; the three matter because of what
they guard — the 0.15 affinity band still straddling the 0.9 interest threshold, without which
Find Buyer quietly stops ever saying "No current interest".

**Also worth 30 seconds of looking:** hover a row in the **Market** tab and a row in
**Relations**. Both tooltips should now carry four rows — economy, usually supplies, usually
demands, quality preference. The question is whether they read as identity or as noise, which no
self-test can answer.

(Matteo) Test Result: Ran the tests, you look at the log. In terms of UI, indeed it does show usually produces and usually supplies - in fact, in the "world" tab, when I click a settlement and I get the option to click the "planet" and "terrain" buttons, I think there should be a third "economy" button that shows that settlement's economic information. This only showing up in the intercolony tab means the player might not see it, and it would be natural to look at the settlements close to you using the world tab and "economy" button and check what they will usually demand and supply for you to plan your colony around that.... THIS IS IMPORTANT!!!!

**Suites: profile 154/0/0, market 84/0/0 — checked in his log, both clean.**

**The Economy tab is being built (2026-08-21), and it is feasible with no Harmony patch.** The
Planet and Terrain buttons he describes are `WITab_Planet` and `WITab_Terrain`, listed in
`<inspectorTabs>` on the abstract `StaticWorldObjectBase` world object def, which `Settlement`
inherits (`reference/vanilla-defs/Core/Defs/WorldObjectDefs/WorldObjects.xml:30-40`). `WITab` is
`public abstract` and subclassable (`reference/decompiled/RimWorld.Planet/WITab.cs`). So a third
button is a def patch plus one class — the supported route, not a patch.

**This also answers criterion 7 in a way the tooltips could not.** Stage 1 asked whether a
settlement's economy is legible without debug numbers, and the answer shipped as two tooltips
inside the mod's own tab. His objection is that a player choosing where to settle is looking at the
*world map*, and will never find it there. That is a placement failure rather than a content one —
the 1.4/1.5 ledger entry even records rejecting a dedicated screen in favour of tooltips, and this
is the third option nobody considered: put it on the object the player already clicks.

**And it is the natural home for the 2J visibility problem above.** The tab is the one surface that
can show current conditions *live* — a listed opportunity's price is frozen at generation, so the
Market board structurally cannot show a shortage that arrives later. The tab is being built with the
two kept deliberately apart: *what this place is* (baseline identity, unchanging) and *what it is
going through right now* (only shown when something actually is).

#### ~~The three suites that now touch the timeline — rerun to confirm no regression~~ -- CLOSED 2026-08-21

The bridge run closed this with order **107/0/1**, RFQ **81/0/0** and combat clause **54/0/0**; its
whole-run leak block also reported the commercial timeline unchanged at **12 records**, a stronger
check than dumping the timeline and inspecting it by eye. The detailed run record below is retained.

Stage 0 gate criterion 6. `IntercolonyOrderSelfTest`, `IntercolonyRfqSelfTest` and
`IntercolonyCombatClauseSelfTest` all drive transitions that now record commercial events, and
each is wrapped in `IntercolonyTimelineGuard` so the records are rolled back afterwards.

**Full path.** Same F12 → orange bug icon route. Run `Run order self-test`,
`Run RFQ self-test` and `Run combat clause self-test`.

**Pass.** Each reports its usual counts with **0 failed** and no new skips, and — the point of
the guard — running `Dump commercial timeline` afterwards shows the **same record count as
before the suites ran**, with no `Testholme`, `MatrixTest` or `Test faction` rows. A row from
a settlement that does not exist means the guard is not working.

(Matteo) Test Result: I didnt run this because it looks like something you can do yourself using the MCP.

**DONE 2026-08-21 through the bridge, and he was right that it needed no human.** Run against his
live migrated colony (schema 44, 69 world pawns, tick ~7,035,000) rather than a `-quicktest` world:
**order 107/0/1, RFQ 81/0/0, combat clause 54/0/0**, world-pawn delta 0 on each. The order suite's
single skip is the known one — `recorded-map collection vs AnyPlayerHomeMap`, which needs a second
colony and honestly reports itself rather than passing.

**The guard question is answered, and by the right measurement.** A full 17-suite run on the same
colony reported `OK commercial timeline unchanged at 12 record(s)` and
`OK market pressure unchanged at 0 settlement(s)`. No `Testholme`, `MatrixTest` or `Test faction`
rows survived. That is the leak block comparing counts either side of the whole run, which is
stronger than eyeballing a dump afterwards.

**One transient worth recording rather than alarming about.** The first full run showed a world-pawn
delta of **+2** (69 → 71). An immediate second identical run showed **0**, starting from 69 again —
so the two pawns were gone, not accumulated. A leak accumulates; this did not. The cause is that a
**live colony keeps ticking while the suite runs**, so world pawns legitimately come and go, which
is also why every `-quicktest` run reads exactly 0: those worlds are static. This is a strong
candidate explanation for the long-standing `job posting` pawn-count anomaly, which was recorded as
74 → 80 on a live colony and has never reproduced on a static one. Not proof, but it is the first
mechanism proposed that fits every observation.

#### ~~Contract timeline events — no self-test coverage~~ — CLOSED 2026-08-21 (`05d7bb7`)

**All seven write sites are now driven by the timeline suite**, which went 47 → 68 assertions. Three
of them are separate `ContractStarted` paths — incoming offer, player proposal, renewal — and each is
driven on its own, because covering one proves nothing about the others.

**Verified in both directions rather than watched passing.** Deleting `AcceptRenewal`'s `Record` call
turns exactly its two assertions red and leaves the other nineteen green, which is the result that
matters: the other two `ContractStarted` sites do **not** mask a missing one. Green on four
consecutive fresh worlds afterwards, all 21 running rather than skipping.

**This closes the item without needing a live contract in play**, which is what it had been waiting
for. The note below about needing the 0.9.3 save is superseded — and the reason it could never have
worked is worth keeping: a contract accepted *before* the schema-43 upgrade tick correctly has no
record, so an existing agreement could not have proved anything either way.

#### Superseded: contract timeline events needed a live contract

`ContractStarted`, `ContractCompleted`, `ContractFailed` and `ContractCancelled` are wired at
six sites in `ContractService` but are **not** covered by the timeline self-test: driving them
needs a live contract with cycles running, which `IntercolonyContractSelfTest` already builds.
The cheapest proof is play — accept a supply agreement and confirm a `ContractStarted` row
appears in **Dump commercial timeline** (same F12 menu). Worth folding into the contract
self-test if Stage 5 touches these paths anyway.

(Matteo) Test Result: You can test this, the 0.9.3 save has a contract - I'm pretty sure.

**STILL OPEN, and an existing contract cannot close it — this is worth understanding before anyone
tries again.** The timeline records an event *at the moment a status changes*. Schema 43 deliberately
started history at the upgrade tick rather than inventing a past, and this save reports
`timeline: 12 record(s), since tick 6473557`. Any contract accepted before that tick has no
`ContractStarted` row and never will, correctly. So the test needs a contract that **starts after**
the upgrade, not one that already exists.

**A second obstacle, and it is a real gap in our own tooling:** `IntercolonyWorldComponent`
maintains a `contracts` list (`Core/IntercolonyWorldComponent.cs:435`), but `DebugStateSummary`
never prints it — it reports opportunities, orders, employments, postings, timeline, ledger,
employer standing and labor debts, and stops. So neither the bridge nor `Dump state` can even
answer "does this save have a supply agreement?". `Dump contracts` exists as a debug action but is
not reachable over the bridge. **Fix the state summary first**; until then this check cannot be
automated at all, which is why it stayed open rather than being quietly attempted.

#### ~~Schema 42 → 43 migration under a real save — never run~~ -- CLOSED 2026-08-21

This was a duplicate of the already-closed **Schema 42 → 43 migration under a real save** entry above; listing the same closed item twice is part of why this file stopped being usable.

**Full path.** Load an actual save made with 0.9.3 (schema 42) — not a new colony. Then read
the log with `powershell -ExecutionPolicy Bypass -File dev.ps1 log`.

**Pass.** The log contains `Migrating state from schema 42 to 43` followed by
`schema 42 -> 43: commercial timeline record spine added; history starts recording at tick N`,
with no red errors, and every existing order, contract, request and employment still present
afterwards.

**Why a plain `dev.ps1` run cannot prove this — and what can, which is newer than this entry.**
A `-quicktest` launch creates a *new* world that initializes at the current schema and therefore
never enters the migration path at all. That much is still true and always will be. What has
changed since this was written is the conclusion drawn from it: `dev.ps1 bridge -Save <name>`
stages a copy of a real save as RimWorld's stock `autostart` file and boots into it through the
real `GameDataSaveLoader.LoadGame`, so a migration **can** now be proven unattended. The 42 → 43 → 44
chain was run that way on the 22.5 MB `Fenhana` colony with zero exceptions. Do not repeat the old
claim that only a human at the keyboard can exercise a migration.

### ~~Correction-batch self-tests~~ -- CLOSED 2026-08-21

These were self-test items, and the bridge now runs them unattended on every pass, so they do not
need re-listing here. The 2026-08-21 real-colony run reported order **107/0/1**, contract **39/0/0**
and RFQ **81/0/0**.

These are dev actions, not play-tests, but their procedures remain here so later changes can rerun
them. All of them: **F12** → **orange bug icon** (top-right toolbar) → type the search term → click
the action. Output goes to the debug log; no need to copy anything out, the dev script reads it.

**Run 2026-08-13 during 0.9.1 preparation:** order **93 passed, 0 failed**, contract **38 passed,
0 failed**, RFQ **69 passed, 0 failed**. The first order run found a real buy-only obligation
regression; after the focused fix, the stated result is the rerun. The order suite explicitly
skipped recorded-map collection versus `Find.AnyPlayerHomeMap` because the world had one home map,
and skipped live-offer acceptance because no offer existed. Those limits remain manual work below.

#### ~~Find Buyer, availability and pickup timing — `Run order self-test`~~ -- CLOSED 2026-08-21

The unattended real-colony run reported **107 passed, 0 failed, 1 skipped** for the order suite, so
this self-test item is closed; the skipped assertion remains subject to the file's no-proof rule.

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

#### ~~Contract liveness and completed-history offers — `Run contract self-test`~~ -- CLOSED 2026-08-21

The unattended real-colony run reported **39 passed, 0 failed, 0 skipped** for the contract suite.

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

#### ~~Procurement cancellation and concluded-order selection — `Run RFQ self-test`~~ -- CLOSED 2026-08-21

The unattended real-colony run reported **81 passed, 0 failed, 0 skipped** for the RFQ suite.

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

#### ~~Buy-only unlock, including the obligation guard — `Run order self-test`~~ -- CLOSED 2026-08-21

The unattended real-colony run reported **107 passed, 0 failed, 1 skipped** for the order suite; the
buy-only assertions ran cleanly, while the separately named skipped assertion remains unproven.

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

#### ~~Animal specification, matcher and eligibility — `Run animal spec self-test`~~ -- CLOSED 2026-08-21

The real-colony run reported **62 passed, 0 failed, 7 skipped**. This closes the suite item, but a
skip is not proof: the real colony converted some old skips into assertions, not all of them, so
those seven specific assertions remain unproven.

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

#### ~~Which colony a buyer collects from — `Run order self-test`~~ -- CLOSED 2026-08-21

This assertion had reported `SKIPPED` since 0.9.0 for want of a second colony. Matteo's first real
two-colony run exposed two wrong-colony regressions, `9ca5062` fixed them, and the assertion now
passes (`PROGRESS.md:2145-2152`). This closes only the self-test half: **The buyer-pickup colony fix
needs two colonies** remains open as the manual reproduction (`PROGRESS.md:2214-2216`).

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

### ~~Procurement delivery and refund use the paying colony~~ -- CLOSED 2026-08-21

Matteo directly observed two-colony delivery and refund routing working on 2026-08-13
(`docs/BACKLOG.md:229-234`). That observation closes the paying-colony paths, while the same record
says the map-less and zero-placement paths still lack practical play reproduction; they remain open
as the separate item below.

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

#### Map-less and zero-placement procurement paths remain unobserved

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

### ~~The world-map Economy tab has never been seen on screen~~ — CONFIRMED IN PLAY 2026-08-22

**Matteo confirmed it.** The Economy button appears on a settlement's world-map inspect pane and
vanilla's Planet and Terrain tabs are both still present, which was the check that mattered: had the
def patch replaced the inherited `inspectorTabs` list rather than merging into it, those two would
have disappeared from every settlement in the game. The `XmlInheritance` reading was right.

**This also closes Stage 1 acceptance criterion 7**, which had been open since the Stage 1 gate as
"whether a settlement's economy reads clearly without debug numbers". The answer turned out to be
that it read clearly but in the wrong place — the fix was placement, not wording.

The steps below are kept as the regression check if the patch or the tab class is ever touched.

### Original steps — kept as the regression check

**Added 2026-08-21 with `713408b`.** Raised by Matteo during the Stage 1 tooltip look: clicking a
settlement on the world map offers **Planet** and **Terrain**, and should offer **Economy** too,
because that is where a player plans a colony and they will otherwise never find the mod's own tab.

**What is already proven, so do not re-check it.** The full suite ran 968/0/14 after the change with
a clean log, and **both ways this could fail silently do log an error**: a non-matching xpath makes
`PatchOperationAdd` fail with a logged error, and a `WITab` type that cannot be resolved logs a type
error at def load. Neither fired and the session log has zero errors. It was also verified from
`reference/decompiled/Verse/XmlInheritance.cs` that the patch *merges* — `ApplyPatches()` runs before
`XmlInheritance.Resolve()`, and `RecursiveNodeCopyOverwriteElements` appends `li` nodes into the
inherited list — so vanilla's tabs are not replaced.

**What remains, and it needs eyes.** None of that is the same as the button being there and the pane
reading well.

**Steps.** Open the world map, click any settlement of a real faction, and look for a third button
beside Planet and Terrain.

**Pass.**
1. **Economy** appears as a third button, and Planet and Terrain both still work — if either vanilla
   tab is now missing, the def patch replaced the inherited list instead of merging into it, and
   that is a serious regression rather than a cosmetic one.
2. The pane shows four labelled rows: economy, usually supplies, usually demands, quality
   preference. They must match what the same settlement's tooltip says in the Market tab — they come
   from one shared helper now, so a disagreement means the refactor lost something.
3. Click a settlement with **no faction**, or an ancient ruin. A ruin must **not** have an Economy
   tab at all; a factionless settlement should say it is not an economic participant rather than
   drawing an empty pane.
4. Shock a settlement (`Intercolony → Shock settlement economy`) and reopen its Economy tab: a
   **Right now:** row appears naming the category and whether it is a shortage or a surplus. Then
   open an *undisturbed* settlement and confirm there is **no** Right now row at all — its absence is
   as much the design as its presence.
5. Nothing overdraws anything else, and long category lists wrap rather than painting over the rows
   beneath. The rows are measured with `Text.CalcHeight`, but that has been got wrong before in this
   mod and is the reason rule 7 exists.

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

### The Produce, auto-renew and auto-ready batch has never been seen working by a human

Added 2026-08-30. These four features shipped in this batch are visual, interactive, or about feel,
so a self-test cannot settle them. None has been seen working by a human.

**Produce toggle.** A **Produce** gizmo, using the vanilla Uninstall icon, appears on minifiable
player buildings and on build blueprints and frames for them. Turning it on should uninstall the
object, place an identical blueprint in the same cell with the same material and rotation, rebuild
it, and repeat. This has never been seen working by a human.

**Steps.** Check that the loop actually runs end to end in real time with colonists doing the work;
that a batch multi-select shows **ONE** merged **Produce** button and enabling it starts all of them;
that turning it off stops the next repetition without cancelling work under way; and that the
accumulating minified furniture does not jam the cell permanently when there is nowhere to haul it.
The last check is the likeliest real-play problem and no self-test can see it.

**Auto-renew.** The per-worker **Auto-renew** toggle in the employee row's **...** menu, and the
letter that arrives when a worker renews by itself, have never been seen working by a human.

**Steps.** Check that the menu only offers what applies to that worker and that the letter reads
correctly for a fixed-term contract.

**Auto-ready orders, selling side.** The toggle on an active buyer-pickup agreement in **Selling →
Contracts**, the cycle order readying itself when the goods are present, and the single **Agreement
delivery needs attention** letter when they are not have never been seen working by a human.

**Auto-ready orders, procurement side.** The toggle on an active agreement in **Procurement →
Contracts**, a cycle waiting rather than failing when the colony is short of silver, and the
**Procurement cycle waiting on silver** letter have never been seen working by a human.

**UI check for the whole batch.** Confirm that the **Business** tab no longer lists individual
agreements, and that the per-agreement margin estimate now appears on the **Selling → Contracts** row
instead, with the row growing to fit it and not overlapping the row's buttons or the row beneath.

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
