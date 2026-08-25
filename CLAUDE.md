# Intercolony — Agent Context

RimWorld mod. Hobby project. See `DESIGN.md` for the product spec.

**Package ID:** `miannoni.intercolony`
**Root namespace:** `Intercolony`
**Target RimWorld version:** 1.6 (confirm against `Version.txt` — do not assume)

## After a context compaction, read this first

If the conversation has just been compacted or summarized, **invoke the `resume-1-0` skill via the Skill tool before doing anything else.** Do not reconstruct the working method from the summary.

The skill restores the dispatch/review rhythm, the delegation command, the verification discipline, and the report shape in one read; re-deriving those from a summary is both slower and less accurate.

This applies to an **automatic** compaction just as much as a manual `/compact`: an auto-compact gives no warning, so this file is the only thing guaranteed to still be in context afterwards. **That is the whole reason this instruction lives here** rather than only in a hook.

A `SessionStart` hook in `.claude/settings.local.json` also nudges this, but a hook can fail silently and `.claude/` is gitignored, so **this line is the reliable one and must not be deleted**.

The skill reads current state itself, so it works from a cold context; it does not depend on anything surviving the compaction.

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
and the new `Player.log` lines. Exit codes: `0` clean, `1` assertions failed, `2` everything else
— connection, build, environment-setup failure, **and a run whose assertions passed but whose log
gained new exceptions.** That last one is deliberate: **a suite can pass while the log fills with
exceptions, and that is not a clean run**, so it does not get to exit 0. A skipped assertion is a
different thing again — not a failure, so it does not turn the exit code red, but not proof
either, so it is reported on its own line.

Test ids come from `tests.list`, not from the display names: `job-posting`, `combat-clause`,
`employer-reputation`, `long-term`, and the plain ones (`economy`, `market`, `payroll`, …).

**If the bridge does not answer**, in this order: **is the Steam client running and logged in**
(see below); is RimWorld running; was it built with `-p:EnableDevBridge=true`; was it launched with
`INTERCOLONY_DEV_BRIDGE=1` in its environment; is something else already on port 34117 (the log says
so by name). A normal build has no bridge in it at all, which is deliberate — see
`docs/DEV_TEST_BRIDGE.md`.

**A logged-out Steam client breaks the bridge, and it does not look like Steam's fault.** Cost about
fifteen minutes on 2026-08-21. `SteamAPI.Init()` fails, so `Adding mods from Steam:` finds nothing
and RimWorld **deactivates every Workshop mod including Harmony**, then rewrites `ModsConfig.xml`
down to Core plus DLC. Intercolony still loads — it is a local mod — and dies at
`TypeLoadException: Could not resolve type ... 'HarmonyLib.HarmonyPatch'`, so the bridge never opens
and `dev.ps1` reports a plain connection timeout. Three things follow from that:

- **The tell is in `Player.log`, not in the timeout.** Look for `SteamAPI.Init() failed` and
  `Deactivating not-installed mods`. Check `HKCU:\Software\Valve\Steam\ActiveProcess\ActiveUser` —
  **`0` means logged out**, and Steam merely *running* is not enough.
- **Only Matteo can fix it** (password and 2FA). Ask; do not work around it. Copying the Workshop
  Harmony into `Mods\` would create a duplicate `brrainz.harmony` that outlives the session — the
  same leftover-state trap as `Autostart.rws`.
- **Restore the mod list afterwards.** RimWorld has already overwritten it, so the previous list is
  gone from disk; recover it from `Player-prev.log`, which prints every active mod in load order
  under `Initializing new game with mods:`.

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

**Branch:** `1.0`. **Stage:** 8 — 1.0 integration, balance and release gate. Stages 0–7 are
closed, and Stage 8 slices 8A–8C are complete. The current save schema is 56; the in-game suite
contains around 1,350 assertions.

`main` stays at 0.9.3 and is not touched until Stage 8 merges. The 1.0 branch is not released: the
§8.3–§8.7 play sitting still needs a human, and the remaining documentation and release-gate work
are recorded in `docs/PENDING_PLAYTESTS.md` and `docs/1_0_IMPLEMENTATION_STATUS.md`.

---

## Operational rules and pointers

For 1.0 work, read `docs/1_0_IMPLEMENTATION_STATUS.md` first; it is the continuity record. Read
`docs/INTERCOLONY_1_0_IMPLEMENTATION_PLAN.md` by stage rather than loading the whole plan.

### General invariants

**Three rules this batch established, and a future change must not break:**

- **A displayed figure and a charged figure come from one calculation.** `0b1dfe9` fixed a live
  defect where Find Buyer advertised a rate the commit then changed. Every preview since — contract
  terms, proposal payment, delivery count — is computed by the same method construction uses. If you
  find yourself multiplying money in a UI file, stop.
- **A deal records the market rate it was struck against.** Demand now drifts between refreshes, so
  recomputing spot at completion answers a different question than the player agreed to.
- **A UI cache must reset at its own lifecycle boundary.** Main-tab windows survive being closed;
  `44c6509` was an eligibility list that outlived the window and stayed empty for a session.

**When a field is deleted, check whether its old value was the Scribe default before assuming a real
save exercises the removal.**

### Sentinel values

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

The count reached six during Stage 6; the rule is unchanged.

### Release procedure

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
   Workshop copy in `docs/WORKSHOP_DESCRIPTION.bbcode` survives re-uploads. The title is re-sent
   every time from `About.xml`'s `<name>`.

Also verified the hard way: **Steam caps the preview image at 1 MB and RimWorld enforces nothing**,
only checking `File.Exists`. A 1.81 MB preview would have failed silently at Steam's end.
`About/Preview.png` is currently 933,975 bytes — keep it under the cap.

### Labor

**Read `docs/LABOR_TECHNICAL_NOTES.md` before touching any labor code.** It records the chosen
control strategy (faction transfer + quest lodger) and the non-obvious rules the implementation
depends on: an employment quest must have a non-null `root`, departure must go through
`QuestPart_Leave` rather than being hand-rolled, and a travelling worker must be pinned in
`WorldPawns` as `KeepForever`.

### Autostart migration technique

A save named exactly `autostart` is loaded automatically at boot by `Root_Entry.Start()` through the
real `GameDataSaveLoader.LoadGame` (`reference/decompiled/Verse/Root_Entry.cs:18-23`). **Autostart
must not be combined with `-quicktest`** — both
`Root_Entry` and `Root_Play` consume the same one-shot `Root.checkedAutostartSaveFile`, so with
`-quicktest` the game calls `InitNewGame()` on the already-loaded save and throws at
`Find.get_WorldObjects`. Launch with no arguments at all.

**Autostart a copy, never the original, and delete it when done** — a leftover `Autostart.rws`
hijacks every later launch including `-Fresh`, which then goes on claiming an isolation it lost.

What remains true: a plain `-quicktest` launch cannot prove a migration, because it generates a new
world that initializes at the current schema and never enters the migration path, and the log reader
can show a stale profile entirely. `dev.ps1 -MainMenu` still exists for opening a save by hand.
`docs/PENDING_PLAYTESTS.md` has the remaining ones.

### 1.0 evidence policy

**`docs/ROAD_TO_1_0.md` audits §120's 36 criteria: 25 met and proven, 11 met but unproven, 0 not
met.** Read it before planning anything — it is the honest picture of how far 1.0 actually is.

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

### Known map-resolution follow-up

**A real 0.9.0 defect was found and fixed on 2026-08-09: buyer pickup collected from the wrong
colony.** Mark Ready validated against `Find.CurrentMap` while collection used
`Find.AnyPlayerHomeMap` — the *first* player home map, not the relevant one — and `SalesOrder`
persisted no map at all. With two or more colonies the order either failed with "the goods were not
there" while they sat where the player left them, or took stock from the wrong base. `SalesOrder`
now persists its fulfilment colony, following the `EmploymentContract.destinationMap` idiom.
**`PurchaseOrderService` has the identical latent flaw at its delivery and refund sites and was
deliberately left alone** — it is its own fix, and it is in `docs/BACKLOG.md`.

### Play-testing handoff

**Play-testing is done by a Dispatch computer-use session, not by Matteo at the keyboard.** The
handoff runs through `DISPATCH_NOTES.md` — append-only, timestamped, game output verbatim. Claude
Code does not read it automatically; Matteo nudges with "read DISPATCH_NOTES.md and continue". Write
requests there with exact steps, and reply in the same file.

### Documentation pointers and animal-trade invariants

`docs/PENDING_PLAYTESTS.md` lists what has shipped but has never been seen working — the things a
self-test cannot settle. **Add to it when a phase completes, and check it before claiming a system
is proven.** Asking Matteo to play something and then losing the request when the conversation
moves on is how a system ends up believed-working and untested.

`docs/unique-goods-spike.md` holds the unique-item representation decision from Phase 7.
Read it before touching anything that moves an individual object.

**Animal trade (D2/D3) is decided, and buyer pickup is in scope** — Matteo confirmed on 2026-08-09,
so animals are sold both by seller delivery and by buyer collection, with the player designating
animals at Mark Ready. Trade is by *specification* (species, sex, life stage, pregnancy — each
independently selectable and separately priced), not by individual identity. Planned as five
slices: representation + schema 24→25, pricing, procurement, sell-by-delivery, sell-by-pickup.
Read `docs/ANIMAL_TRADE_SPIKE.md` **including both addenda** — the second one corrects two claims in
the first that would each have produced a defect, most importantly that a post-generation gender
check is a no-op because `PawnGenerator` forces `FixedGender` before consulting the race at all.

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
