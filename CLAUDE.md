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

## Dependencies

- `Krafs.Rimworld.Ref` — reference assemblies, so no game DLLs are committed
- `Microsoft.NETFramework.ReferenceAssemblies` — lets the plain SDK build `net472`
- `Lib.Harmony` — **must** be `ExcludeAssets="runtime"` / `Private="false"`.
  The Harmony DLL must NOT be copied into `Assemblies/`. Declare the Harmony
  workshop mod as a `modDependency` and in `loadAfter` in `About.xml` instead.

Target framework is `net472`.

---

## Current state

**Phase:** 22 complete (2026-07-30). Next: Phase 23 — Employee-to-colonist transition
(DESIGN.md §116, §44). Late-game narrative conversion: eligibility, the player's offer, the
worker's answer, and what it costs their home faction.

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