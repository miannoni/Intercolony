# Intercolony progress log

## Phase 0 — Repository and build bootstrap  (2026-07-25)

Implemented:
- `About/About.xml` with packageId `miannoni.intercolony`, supportedVersions `1.6`, and Harmony (`brrainz.harmony`) declared in both `modDependencies` and `loadAfter`.
- `About/Preview.png` placeholder (640x360).
- `Source/Intercolony/Intercolony.csproj` targeting `net472`, root namespace `Intercolony`, output to `../../Assemblies/`. References `Krafs.Rimworld.Ref 1.6.4850`, `Microsoft.NETFramework.ReferenceAssemblies`, and `Lib.Harmony` with `ExcludeAssets="runtime"` and `Private="false"` so no Harmony DLL is copied.
- `Source/Intercolony/IntercolonyMod.cs`: single `Verse.Mod` subclass whose ctor calls `Log.Message("[Intercolony] loaded.")`.
- `.gitignore` extended to cover `.claude/`.

Not implemented:
- Anything in DESIGN.md sections 71+ (world state owner, IDs, state machines, market, orders, etc.). Deferred to Phase 1 and beyond.
- No `Defs/`, `Languages/`, `Textures/` folders. Not needed yet.
- No subfolders under `Source/Intercolony/` (Core/, Market/, Orders/, ...). Deliberately skipped per DESIGN.md §69 — "Do not create empty abstraction layers".

Known limitations:
- Only DLCs installed locally are Core and Biotech. Anything Phase 3+ that assumes Royalty, Ideology, Anomaly, or Odyssey will need to be gated.
- Confirmed RimWorld install version is `1.6.4871 rev590`, but the `Krafs.Rimworld.Ref` package pinned in the csproj is `1.6.4850`. Compiled fine against 4850 refs; if a later build hits API drift we may need to bump.
- Preview.png is programmatically generated placeholder text on a dark blue background. Replace before any public release.
- The `RimWorld\Mods\Intercolony` junction did not exist at the start of this session; it was created manually with `New-Item -ItemType Junction`. Any fresh clone on another machine needs the same command.

Manual test:
- `dotnet build Source/Intercolony/Intercolony.csproj` — clean, 0 warnings, 0 errors. `Assemblies/` contains only `Intercolony.dll`; no Harmony DLL leak.
- Enabled Intercolony in the in-game mod list, restarted RimWorld, quit. `Player.log` shows:
  - Line 35: `Adding miannoni.intercolony(...\Mods\Intercolony)` (discovery)
  - Line 116: `[Intercolony] loaded.` (our startup message, exactly once)
  - Line 125: `Loading Intercolony.IntercolonyMod mod class` (assembly loaded, our `Mod` subclass instantiated)
  - Line 660: `Initializing [miannoni.intercolony|Intercolony]` (mod init)
- No red errors anywhere in the log referencing Intercolony. All six §93 acceptance criteria pass.

---

## Phase 1 — Persistent core state  (2026-07-25)

Implemented:
- `Source/Intercolony/Core/IntercolonyWorldComponent.cs` — the single authoritative owner of persistent economic state (§71). Chosen as a `RimWorld.Planet.WorldComponent` after confirming in `reference/decompiled/RimWorld.Planet/World.cs:183` that `World.FillComponents()` reflects over `typeof(WorldComponent).AllSubclassesNonAbstract()` and instantiates exactly one instance per world, saved with the world. No singleton to manage, no duplicate-owner risk, not tied to a map.
  - `static Current` accessor via `Find.World?.GetComponent<IntercolonyWorldComponent>()`; returns null off-world (`Find.World` is null-safe at the main menu — `reference/decompiled/Verse/Find.cs:100`).
  - `DebugStateSummary()` renders every persisted field.
- Save schema version (§62): `CurrentSaveVersion = 1`, persisted `saveVersion` field with default 0 meaning "predates versioning". `MigrateIfNeeded()` runs at `LoadSaveMode.PostLoadInit`; logs on migration, warns without throwing when a save comes from a newer schema.
- Stable ID generator (§72): a single monotonic `nextId` counter on the world component with `NextId()`, clamped to >= 1 on load.
- Persistence probe (§94): `testCounter` / `testString`, marked in-code for deletion once real state exists.
- `Source/Intercolony/Core/IntercolonyLog.cs` — `[Intercolony]`-prefixed `Message` / `Warning` / `Error` / `Verbose` (§68). `IntercolonyMod` ctor switched to it.
- `Source/Intercolony/Debug/IntercolonyDebugActions.cs` — four actions under debug category "Intercolony": `Print state`, `Set test values (7 / "Intercolony")`, `Test counter +1`, `Allocate ID`. Discovery confirmed against `reference/decompiled/LudeonTK/DebugTabMenu_Actions.cs:30` (private static parameterless methods on any type in `GenTypes.AllTypes` are picked up; no registration needed).

Not implemented:
- No Harmony patches yet. None needed for world-level state.
- No mod settings (§66), so `IntercolonyLog.Verbose` is gated on `Prefs.DevMode` as a stand-in.
- No separate `IntercolonyIdGenerator` / `Persistence/` layer. The ID counter is a plain `int` field on the world component: deliberately avoids `Scribe_Deep` null-on-load handling for a single call site, per hard rule 4. Extract when a second consumer justifies it.
- Per-entity-kind ID namespaces and the short human-readable UI aliases from §72 (e.g. `SO-42`). One globally unique counter now; aliasing is a display concern for the phase that introduces sales orders.
- No transaction history, no state machines, no economic profiles (§73, §75, §96+).

Known limitations:
- `MigrateIfNeeded()` has no actual migration steps — schema 1 is the first version. The 0/1 -> 2 path is a comment, so the first real schema bump is untested code.
- The future-version branch warns and loads anyway rather than refusing. Acceptable pre-alpha; revisit before public testing per §62.
- `Prefs.DevMode` returns true when `Prefs.data` is null (very early startup), so verbose logging can be briefly on before prefs load. Harmless.
- The Visual Studio boilerplate `.gitignore` rule `[Dd]ebug/` (line 21) also matched our source folder `Source/Intercolony/Debug/`, which would have silently excluded `IntercolonyDebugActions.cs` from version control. Fixed by appending `!Source/Intercolony/Debug/`. Watch for the same trap if a future source folder is named `Release/`, `Debug/x64/`, or similar — the boilerplate has many such rules.

Manual test:
- `dotnet build Source/Intercolony/Intercolony.csproj` — 0 warnings, 0 errors. `Assemblies/` still contains only `Intercolony.dll`; no Harmony DLL leak.
- Verified the `<RIMWORLD_INSTALL>\Mods\Intercolony` junction still resolves to `C:\dev\Intercolony`.
- In-game (dev mode, debug actions menu — default key `/`, per `Core/Defs/Misc/KeyBindings/KeyBindings.xml:404`): ran `Set test values`, then `Allocate ID` twice, then `Print state`.
- Saved → **quit to main menu** → reloaded the save → `Print state`: `testCounter`, `testString`, `saveVersion`, and `nextId` all matched the pre-save values.
- `Allocate ID` after reload continued the sequence instead of restarting at 1 — the ID counter genuinely survived the round trip, which is the real acceptance test for §72.
- No red errors in the in-game dev debug log window. All four §94 acceptance criteria pass.
