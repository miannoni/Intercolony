# Intercolony — Agent Context

RimWorld mod. Hobby project. See `DESIGN.md` for the product spec.

**Package ID:** `miannoni.intercolony`
**Root namespace:** `Intercolony`
**Target RimWorld version:** 1.6 (confirm against `Version.txt` — do not assume)

---

## Local paths

Fill these in and do not commit machine-specific values anywhere else.

```
RIMWORLD_INSTALL = C:\Program Files (x86)\Steam\steamapps\common\RimWorld
MOD_LINK         = <RIMWORLD_INSTALL>\Mods\Intercolony   (junction to this repo)
LOG              = %USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player.log
SAVES            = %USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Saves
```

`./reference/` is gitignored and contains:

- `reference/vanilla-defs/` → junction to `<RIMWORLD_INSTALL>\Data` (Core + all DLC XML)
- `reference/decompiled/` → `Assembly-CSharp.dll` decompiled to C# via `ilspycmd`
- `reference/mods/` → cloned open-source mods used as pattern references

---

## Hard rules

1. **Never invent a RimWorld API.** Before writing any XML tag, class name, method
   signature, or Harmony patch target, grep `./reference/` to confirm it exists.
   If you cannot find it, say so instead of guessing.

2. **DESIGN.md is not an API reference.** It was written before the code existed.
   Any class name, method, or modding pattern it mentions is a *suggestion* and must
   be verified against `./reference/decompiled/`. Design intent is authoritative;
   implementation detail is not.

3. **Read DESIGN.md selectively.** It is ~4,000 lines. Sections are numbered — read
   only the ones relevant to the current task (e.g. `sed -n '3225,3270p' DESIGN.md`).
   Do not load the whole file.

4. **One vertical slice at a time.** A working 150-line slice beats a 3,000-line
   framework for systems that do not exist yet. Do not build abstraction ahead of
   a second concrete use case.

5. **Ask before widening scope.** If a task requires touching a system outside the
   current phase, stop and say so rather than expanding silently.

6. **UI presents key/value rows, not prose.** Set by Matteo on 2026-08-17 as the
   standard for *every* popup and panel in the mod. On the face of a dialog put only
   what the player is agreeing to and the number they get, as labelled rows; push the
   *why* — rationale, side effects, "this is binding" — into tooltips; delete any
   sentence that restates what a control already shows. This is not cosmetic. Six
   paragraphs on the "Sell to this buyer?" dialog put a fixed 12-day *mark ready*
   deadline in a sentence adjacent to the buyer's *arrival* estimate, and it misled
   the mod's own author into reading them as one number (backlog finding 3). Prose
   lets two unrelated facts read as one; a labelled row cannot.

7. **Text composed at runtime is measured, never boxed.** Any `Widgets.Label` fed by a
   builder or an interpolated string gets `Text.CalcHeight(text, width)` — never a
   literal pixel height. `Widgets.Label` neither clips nor scrolls: it paints the whole
   string from the top-left regardless of the rect, so an oversized body silently
   overdraws whatever is beneath it. That is one defect, not two, and it produced both
   the overlap and the clipping in backlog finding 1. A dialog whose content varies
   sizes to its measured content, clamped to a fraction of `UI.screenHeight`, and
   scrolls past the clamp.

---

## Build and test loop

```bash
dotnet build Source/Intercolony/Intercolony.csproj   # outputs to ./Assemblies/
```

Then: launch RimWorld → enable Intercolony → check for red errors → test → read
`Player.log` if something breaks.

Useful: add `-quicktest` to RimWorld's Steam launch options to boot straight into a
throwaway map instead of the main menu.

**Every feature must be tested for save/load.** Save mid-feature, quit to menu,
reload, verify state survived. This is not optional — see §61 and §82.

**Also test across games, not just across loads.** Static state outlives a game: quit to the
menu, start a *different* colony, and check nothing from the old one came with it.
`LaborCandidateService`'s static pool leaked pawns and `Faction` objects between games for
four phases before a save/load test caught it, because every symptom surfaced somewhere else
— duplicate thing IDs, and a faction reporting a null relation with every other faction.

---

## Automated in-game tests — run them yourself, do not ask Matteo to click

When a change can be checked by a self-test, **run it through the dev test bridge**. Do not ask
Matteo to open the debug menu and press "Run ALL self-tests". That request is no longer the
default and should only come back if the bridge itself is broken, or the check is genuinely
visual.

```powershell
powershell -ExecutionPolicy Bypass -File dev.ps1 test job-posting          # against the live game
powershell -ExecutionPolicy Bypass -File dev.ps1 test job-posting -Fresh   # clean -quicktest world first
powershell -ExecutionPolicy Bypass -File dev.ps1 test all -Fresh           # whole suite, clean world
powershell -ExecutionPolicy Bypass -File dev.ps1 bridge                    # just launch a bridge-enabled game
```

Use `-Fresh` when isolation matters — it rebuilds bridge-enabled, restarts into a new world,
waits for the bridge to answer, and **refuses to run rather than claim an isolation it cannot
prove**. Without `-Fresh` nothing restarts, which is what you want while iterating; do not call
such a run "clean" or "isolated".

The report gives you passed/failed/skipped, the **world-pawn delta**, postings before and after,
and the new `Player.log` lines. Exit codes: `0` clean, `1` assertions failed, `2` connection,
build, or environment-setup failure. Read all three signals — **a suite can pass while the log
fills with exceptions, and that is not a clean run.** A skipped assertion is not a failure and
does not turn the exit code red, but it is not proof either, so it is reported separately.

Test ids come from `tests.list`, not from the display names: `job-posting`, `combat-clause`,
`employer-reputation`, `long-term`, and the plain ones (`economy`, `market`, `payroll`, …).

**If the bridge does not answer**, in this order: is RimWorld running; was it built with
`-p:EnableDevBridge=true`; was it launched with `INTERCOLONY_DEV_BRIDGE=1` in its environment;
is something else already on port 34117 (the log says so by name). A normal build has no bridge
in it at all, which is deliberate — see `docs/DEV_TEST_BRIDGE.md`.

**The bridge is a development tool and must never ship.** `package.ps1` reads the built assembly
and refuses to package one containing it, so a release cannot accidentally carry a listener.

---

## Dependencies

- `Krafs.Rimworld.Ref` — reference assemblies, so no game DLLs are committed
- `Microsoft.NETFramework.ReferenceAssemblies` — lets the plain SDK build `net472`
- `Lib.Harmony` — **must** be `ExcludeAssets="runtime"` / `Private="false"`.
  The Harmony DLL must NOT be copied into `Assemblies/`. Declare the Harmony
  workshop mod as a `modDependency` and in `loadAfter` in `About.xml` instead.

Target framework is `net472`.

---

## Current state

**Phase:** 26 complete. **0.9.3 is live and public** (2026-08-18) — Workshop item `3780094556`
updated, `main` pushed, tag `v0.9.3` and a GitHub pre-release with `Intercolony-0.9.3.zip` attached.
0.9.2 shipped 2026-08-17, 0.9.1 on 2026-08-15 and 0.9.0 on 2026-08-08, all to the same standard.

0.9.3 is the Tier 2 UI batch from `docs/BACKLOG.md` plus a labor-UI polish pass: dialog and
empty-state text is measured rather than boxed (a live defect — the sell dialog drew over its own
controls), the sell and market-acceptance dialogs state their terms as labelled rows, sales orders
became a sortable table showing each order's value, Find Buyer can mark a sale ready as it is
created, and Post a job was rebuilt on a grid. **The only behaviour change is that a job posting has
no position count and never expires** — `JobPosting.positions` was deleted, field and Scribe line
both. **Save schema stayed at 42 and no migration was added, because none was needed.**

**Two things changed under existing saves, and the pre-flight that cleared them is worth reusing.**
An old save carries a `<positions>` node with nothing to read it into, and a posting made before
`f594594` keeps its finite `expiryTick` and still expires. The first could not be tested by any real
save: every posting ever made used 1 position, which is the Scribe default, and RimWorld **omits a
value equal to its default**, so no `<positions>` node exists on disk anywhere. The test was a copy
of a real save with `<positions>3</positions>` injected and one posting reopened on its original
finite expiry; it loaded at schema 42 with zero exceptions and nothing dropped. **When a field is
deleted, check whether its old value was the Scribe default before assuming a real save exercises
the removal.**

0.9.2 is a bug-fix release closing six defects from the first real play of 0.9.1. **Animal trade did
not work at all** until it; see below. The others: the employee signing fee was disclosed only when a
hire was refused; procurement quotes could be re-rolled by withdrawing and re-requesting; a
supplier's stock reset on re-request; accepting one quotation closed the whole request; and orders
resolved colonies through `Find.AnyPlayerHomeMap`.
**Save schema is 42.** Schema 40 records the distance behind a buyer-pickup promise; 41 records how
much of a purchase request has been ordered; 42 records how much of a supplier's offer the player has
already bought within a market refresh window.

0.9.1 fixed what the first beta players hit and added the other half of the agreement system: the
player can now propose a standing supply agreement rather than only receiving offers. A proposal is
*sent* and answered later, and can be refused. The response is fastest at both extremes of how
attractive it is and slowest in the middle, because a middling offer is the only one genuinely in
doubt. Price is one lever from zero to twice the going rate — below it earns faction goodwill on a
completed delivery, above it costs goodwill, and a penalty is clamped against RimWorld's own hostile
threshold so a price control can never start a war. Incoming proposals are now **off by default**,
including in migrated saves. Order history is bounded at a hundred closed records each, clearable by
hand, which is only safe because contract eligibility moved onto a durable commercial-history
aggregate first.

**A real schema 22 save migrated cleanly to 39**, which closed the last release risk. Read
`docs/RELEASE_0.9.1_PREP.md` for the full record and `PROGRESS.md` for the milestone entry.

**Three rules this batch established, and a future change must not break:**

- **A displayed figure and a charged figure come from one calculation.** `0b1dfe9` fixed a live
  defect where Find Buyer advertised a rate the commit then changed. Every preview since — contract
  terms, proposal payment, delivery count — is computed by the same method construction uses. If you
  find yourself multiplying money in a UI file, stop.
- **A deal records the market rate it was struck against.** Demand now drifts between refreshes, so
  recomputing spot at completion answers a different question than the player agreed to.
- **A UI cache must reset at its own lifecycle boundary.** Main-tab windows survive being closed;
  `44c6509` was an eligibility list that outlived the window and stayed empty for a session.

**A real defect from that play was fixed the same day.** Accepting a quotation marked the purchase
request `Cancelled` — a status that means "withdrawn by the player" — so every successful purchase
left behind a record claiming the player had abandoned it, and Find seller filled with them. There is
now an `Ordered` status, Find seller shows only live requests, concluded ones sit under Orders, and
**schema 30 repairs existing saves** by matching `PurchaseOrder.requestId` back to its request rather
than guessing. It is the first migration step that changes existing values rather than adding a
field.

D2/D3 — animal trade — are now **built in full** (see below). **Save schema is now 42**; the whole
chain from 24 is documented as one consolidated test in `docs/SCHEMA_24_TO_CURRENT.md`, which is the
file to read rather than reconstructing the steps. Next: continue beta corrections in point releases;
there is no Phase 27 plan yet.

**Animal trade is COMPLETE, and was first played on 2026-08-16 — it did not work.**
E1 `c0a0f91` representation, E2 `4f199fc` pricing, E3a `b0d8f9a` generation and delivery,
E3b `574d5d1` the request UI, E4 `75b87d2` sell by caravan, E5 `ab0dc0e` sell by buyer pickup.

Mark Ready was a silent no-op for every animal order, fixed in `b50b2e2`: validation treated *any*
same-species animal that failed the specification as a fatal failure, so one rooster blocked a hen
order permanently, and RimWorld draws an inactive `Widgets.ButtonText` identically to a live one so
the refusal was invisible. **E5 sell-by-pickup is now proven end to end** — a chicken and a bonded
labrador both sold, the bond warning naming the right colonist. **E3a animal procurement and E4
sell-by-caravan are still unplayed.** Do not treat the whole system as proven because half of it is.

The rules that hold it together, and that a future change must not break:

- **A specification promises only what it states**, so an unspecified term prices at the cheapest
  animal that would satisfy it. The buyer pays for what is guaranteed, not what they might luckily
  receive. Vanilla prices sex and pregnancy at *zero*, so those multipliers are Intercolony's own,
  named constants in `IntercolonyPricing`.
- **The sex gate is checked on the race def before generating, never on the result.**
  `PawnGenerator` assigns `FixedGender` in the first branch of its chain, so it forces the request
  true and a post-generation check proves nothing (`reference/decompiled/Verse/PawnGenerator.cs:741`).
- **A live pawn must never reach the item paths** — `ThingMaker`, stacks, stuff, quality, item
  inventory, or `SplitOff`/`Destroy`. Every animal branch is taken on `IsAnimalOrder` first.
- **Handoffs go through `Pawn.PreTraded(PlayerSells, …)`**, then `PassToWorld(…, Discard)`. Skipping
  `PreTraded` silently deletes a bond and produces no thought — invisible in testing, wrong in play.
- **Pickup designates individual animals** and never substitutes. Committed head count stops a second
  order being marked ready; it does not stop two orders naming the *same* animal, which is why
  selection skips animals another open order has set aside.
- Bonded animals are sellable; **bonding is a warning, never a spec attribute** — never matched on,
  never priced. The confirmation applies the same three conditions vanilla's `Notify_PawnSold` does,
  so it names only colonists who will actually lose the bond.
- **Animals no trader sells are gated behind a default-off setting.** Vanilla withholds a thrumbo
  through *trade tags*, not tradeability: `AnimalExotic` appears in traders' buy lists and no
  trader's sell list. The rule asks whether any loaded stock generator would sell the animal, so no
  def name is hardcoded — the same call as the stone-blocks decision.

**Two debug actions were added to make this testable:** `Arrive purchase orders now` (skips the
supplier lead time through the real advance path) and `Explain unsold animals`. **There is still no
"arrive buyers now"**, so every sell-side pickup test costs real travel time — worth building first
if the sell side is being tested.

**None of the three migrations has ever run in the real load order** — only in isolated throwaway
installs. `dev.ps1` cannot prove a migration: it launches `-quicktest`, which creates a *new* world
that initializes at the current schema, and its log reader can show a stale profile entirely. Only
opening a real save proves it. This is the top item in `docs/PENDING_PLAYTESTS.md`.

**A real 0.9.0 defect was found and fixed on 2026-08-09: buyer pickup collected from the wrong
colony.** Mark Ready validated against `Find.CurrentMap` while collection used
`Find.AnyPlayerHomeMap` — the *first* player home map, not the relevant one — and `SalesOrder`
persisted no map at all. With two or more colonies the order either failed with "the goods were not
there" while they sat where the player left them, or took stock from the wrong base. `SalesOrder`
now persists its fulfilment colony, following the `EmploymentContract.destinationMap` idiom.
**`PurchaseOrderService` has the identical latent flaw at its delivery and refund sites and was
deliberately left alone** — it is its own fix, and it is in `docs/BACKLOG.md`.

**The correction batch is self-tested but not yet proven in play.** On 2026-08-13 the order,
contract and RFQ suites passed 93/0, 38/0 and 69/0. The first order run exposed a real regression:
disabling a buy-only category stranded an already accepted pickup order at Mark Ready. The fix now
uses physically matching stock minus other commitments for existing obligations, and the rerun
passed. The one-home-map run explicitly skipped the assertion that distinguishes the recorded map
from `Find.AnyPlayerHomeMap`, so the two-colony reproduction is still required.
`docs/PENDING_PLAYTESTS.md` has the remaining manual paths.

**Animal trade (D2/D3) is decided, and buyer pickup is in scope** — Matteo confirmed on 2026-08-09,
so animals are sold both by seller delivery and by buyer collection, with the player designating
animals at Mark Ready. Trade is by *specification* (species, sex, life stage, pregnancy — each
independently selectable and separately priced), not by individual identity. Planned as five
slices: representation + schema 24→25, pricing, procurement, sell-by-delivery, sell-by-pickup.
Read `docs/ANIMAL_TRADE_SPIKE.md` **including both addenda** — the second one corrects two claims in
the first that would each have produced a defect, most importantly that a post-generation gender
check is a no-op because `PawnGenerator` forces `FixedGender` before consulting the race at all.

**`docs/ROAD_TO_1_0.md` audits §120's 36 criteria: 25 met and proven, 11 met but unproven, 0 not
met.** Read it before planning anything — it is the honest picture of how far 1.0 actually is.

**There is no known missing implementation for 1.0.** The three criteria that were once "not met"
were one cluster — downed employees, capture, beds left claimed on departure — and all three were
implemented and confirmed in play on 2026-08-08. **Do not reopen them.** The one branch still
unproven is narrow and named in `ROAD_TO_1_0.md`: a worker whose term expired while downed
recovering and then actually walking off. The hold was observed; the completion was not.

### The 11 unproven criteria are beta targets, not release blockers

This is the standing policy and it should not be re-litigated each session. "Met but unproven" means
the code path exists and looks right but nobody has *seen* it work. That is exactly what a beta is
for. **Do not treat an unproven criterion as a blocker to shipping 0.9.0, and do not build more code
to substitute for missing evidence** — that is how a project grows features instead of confidence.

Only an actual serious defect blocks the release: save corruption, a crash on a normal path, silent
loss of the player's silver or obligations, or anything that destroys the player's things. Ordinary
bugs found in beta get fixed in a point release; that is what a pre-release flag is for.

§119's focus — balance, exploits, compatibility, UX, unexpected pawn interactions — needs *other
people playing it*, which no amount of coding substitutes for. The remaining work is play-testing
and distribution, not development.

### 0.9.0 is released — where everything lives

**Shipped 2026-08-08, both channels the same day** (the earlier "GitHub first, Steam a week later"
plan was dropped):

- **GitHub:** pre-release `v0.9.0` on `b8744e4`, asset `Intercolony-0.9.0.zip` (1,135,903 bytes).
  Repository is **public**.
- **Steam Workshop:** item **`3780094556`**, public. Harmony is set as a Required Item.
- **Workshop ID is saved at `.workshop/PublishedFileId.txt`** (gitignored). **The next Workshop
  update depends on it** — see below.

**Read `docs/RELEASE_PROCEDURE.md` before any future release.** Every claim in it was verified
against `reference/decompiled/`. The three that matter:

1. **RimWorld uploads the mod's `RootDir` wholesale** — no filtering, no exclude list, no junction
   handling — and only accepts a mod whose `Source` is the Mods folder. Uploading through the
   `Mods\Intercolony` junction would publish this entire repo, including `reference/vanilla-defs`
   (a junction to RimWorld's whole `Data` directory) and `reference/mods` (other authors' work).
   **Never point a Workshop upload at the repo folder.** `package.ps1` exists for this reason.
2. **`About/PublishedFileId.txt` binds a folder to one Workshop item.** `package.ps1` deliberately
   does not copy it and `dist/` is rebuilt on every run, so an update must restore it from
   `.workshop/` by hand. **The check is that the menu reads "Update on Steam Workshop", not
   "Upload"** — if it says Upload, stop, or you create a second item.
3. **The description is create-only** (`SetItemDescription` is inside `if (creating)`), so the
   Workshop copy in `docs/WORKSHOP_DESCRIPTION.bbcode` survives re-uploads. The **title** is re-sent
   every time from `About.xml`'s `<name>`.

Also verified the hard way: **Steam caps the preview image at 1 MB and RimWorld enforces nothing**,
only checking `File.Exists`. A 1.81 MB preview would have failed silently at Steam's end.
`About/Preview.png` is currently 933,975 bytes — keep it under the cap.

`docs/RELEASE_NOTES_0.9.0.md` holds the published release body; `docs/BETA_QUESTIONS.md` holds the
feedback set. Beta findings go to `docs/BACKLOG.md` and get fixed in point releases, not on the day.

**Localization was dropped, not deferred: the mod is English-only** and §118 is amended to say so.

**Sentinels have bitten five times now. Compare them exactly, and never format them.**
`arrivedTick < 0`, `ledgerStartTick < 0` and printing `DaysRemaining` for an open-ended contract were
all the same mistake: a value chosen to mean "none" being read as a quantity. A tick is only
non-negative because the game has been running a while, and `float.MaxValue` renders as
34028230000000000000000000000000000000. Two of the three were silent.

Phase 25's tooltip pass found two more, which is why the count keeps rising: the employee tooltip
printed `termDays` raw, so an open-ended contract read "0 days"; and the dismissal confirmation could
still format `DaysRemaining` for an open-ended worker serving notice — the *same* bug as the third,
resurfacing in a different method after the first fix. `EmploymentContract` has `TermLabel` and
`RemainingLabel` precisely so the raw fields are never formatted. **Use them. Grep for `termDays` and
`DaysRemaining` before adding any new display of a contract's duration.**

**Play-testing is done by a Dispatch computer-use session, not by Matteo at the keyboard.** The
handoff runs through `DISPATCH_NOTES.md` — append-only, timestamped, game output verbatim. Claude
Code does not read it automatically; Matteo nudges with "read DISPATCH_NOTES.md and continue". Write
requests there with exact steps, and reply in the same file.

**Read `docs/LABOR_TECHNICAL_NOTES.md` before touching any labor code.** It records the chosen
control strategy (faction transfer + quest lodger) and the non-obvious rules the implementation
depends on: an employment quest must have a non-null `root`, departure must go through
`QuestPart_Leave` rather than being hand-rolled, and a travelling worker must be pinned in
`WorldPawns` as `KeepForever`.

`docs/PENDING_PLAYTESTS.md` lists what has shipped but has never been seen working — the things a
self-test cannot settle. **Add to it when a phase completes, and check it before claiming a system
is proven.** Asking Matteo to play something and then losing the request when the conversation
moves on is how a system ends up believed-working and untested.

`docs/unique-goods-spike.md` holds the unique-item representation decision from Phase 7.
Read it before touching anything that moves an individual object.

Update this line when a phase completes.

---

## Open commitments

Promises made to Matteo that are not yet kept. Raise these at the next natural point;
do not let them quietly expire.

- ~~**Everything should be tradeable.**~~ **KEPT in Phase 8 (2026-07-25).** Furniture, art,
  weapons, apparel and minifiable equipment are now generated as demand and deliverable;
  proven in play by an 8-sculpture order paid at 4,500 silver. One permanent exclusion
  remains and is not a gap: **non-minifiable buildings** cannot be crated, so a caravan
  physically cannot carry them. No future phase changes that.

- ~~**Condition constraints are enforced but never generated.**~~ **MAPPED (2026-07-29) into
  Phase 25 (§118)** as a decide-or-delete task, with the design question added to §125 Goods.
  No longer floating.

- ~~**Recurring contracts never renew.**~~ **MAPPED (2026-07-29) into Phase 22 (§115)**, which
  builds renewal and non-renewal for employment anyway. One renewal mechanism, both contract
  kinds. No longer floating.

- ~~**Hostile source faction mid-contract has no policy (§88).**~~ **KEPT in Phase 20 (2026-07-29).**
  Both halves shipped in one file, `Core/HostilityPolicy.cs`, under one stated principle: a war ends
  what has not been performed, whoever holds the other side's value keeps it, and the player is told
  exactly that. Employee released under safe passage in no faction; sales order cancelled at no cost
  and not as a breach; prepaid purchase order lost with the silver named; supply agreement suspended
  and resumable. The Phase 16 placeholder is gone.

Nothing is currently floating outside the plan. When a promise is made to Matteo that does not
fit the current phase, either map it into a numbered phase in `DESIGN.md` or list it here — the
first is better, because this list is only read when someone remembers to look.

**Ideas that are not promises go in `docs/BACKLOG.md`**, not here and not into the current phase.
That file is for work worth doing that is not mapped to a numbered phase — read it when planning a
phase, and add to it rather than widening the phase in flight. The first entry is Matteo's, from
2026-08-07: procurement should eventually be as complete a system as selling (a supplier market,
a purchase-orders screen, and recurring procurement contracts the player offers to suppliers).
Deliberately deferred until after 1.0.

---

## Milestone records

At the end of each milestone, append to `PROGRESS.md`:

Anything the phase could not prove without a human at the keyboard goes in
`docs/PENDING_PLAYTESTS.md` at the same time.

```
## Phase N — <name>  (YYYY-MM-DD)

Implemented:
- ...

Not implemented:
- ...

Known limitations:
- ...

Manual test:
- ...
```

This is the guard against hallucinated completion. Do not skip it.

**Append it with the file tools, not the shell.** PowerShell's `Add-Content -Encoding utf8` on
already-UTF-8 text double-encodes it, so every em dash becomes `â€"` and every `§` becomes `Â§`.
That happened to the whole Phase 20 entry and had to be repaired by re-decoding.

---

## Commits

Conventional-ish, one coherent unit of work per commit:
`chore:` `feat:` `fix:` `refactor:` `docs:`

Commit after each working slice so broken experiments can be rolled back cheaply.

## Dev loop — use this, don't ask me to paste logs

Full cycle (build, restart game, wait, show filtered log):
    powershell -ExecutionPolicy Bypass -File dev.ps1

Other tasks: `dev.ps1 build`, `dev.ps1 run`, `dev.ps1 log`, `dev.ps1 stop`

You can run these yourself. The log is readable while the game is running —
you never need to ask me to close it. `dev.ps1 log` shows only Intercolony
lines plus errors; use `-Full` only if something is genuinely missing.

After any code change, run the cycle and read the output yourself before
reporting back to me.

## Reading the game log during play

The game stays running. You can read its log at any time — do not ask me
to close it or paste anything.

    powershell -ExecutionPolicy Bypass -File dev.ps1 new

That returns only lines written since your last check. Use it every time I
say I did something in game. `dev.ps1 log` gives the whole session if you
need context; `dev.ps1 reset` re-shows everything from the top.

After any code change, run the full cycle yourself and read the result
before reporting back to me.
