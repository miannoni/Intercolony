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

**Phase:** 0 complete (2026-07-25). Next: Phase 1 — persistent core state (DESIGN.md §94).

Update this line when a phase completes.

---

## Milestone records

At the end of each milestone, append to `PROGRESS.md`:

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

---

## Commits

Conventional-ish, one coherent unit of work per commit:
`chore:` `feat:` `fix:` `refactor:` `docs:`

Commit after each working slice so broken experiments can be rolled back cheaply.
