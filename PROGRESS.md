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

---

## Phase 2 — Debug framework  (2026-07-25)

Implemented:
- `Source/Intercolony/Debug/IntercolonyDebugWindow.cs` — the Intercolony debug window (§95). Derives from `LudeonTK.EditWindow` (verified in `reference/decompiled/LudeonTK/EditWindow.cs`), which sets `resizeable`, `draggable`, `doCloseX`, and `preventCameraMotion = false`, so the window can stay open during play instead of blocking the game. Live state dump in a `DevGUI` scroll view plus two rows of action buttons via the inherited `DoRowButton`. `Toggle()` follows the vanilla dev-window idiom (`WindowStack.TryRemove(Type)`, per `DebugWindowsOpener.cs:147`).
- Refresh cadence scaffolding (§59, §84): `RefreshIntervalTicks = 60000` (one in-game day), fired from `WorldComponentTick()` by a single `GenTicks.IsTickInterval` modulo test. Schedule is derived from absolute tick rather than from `lastRefreshTick`, so it cannot drift and can be staggered per settlement later. `ForceRefreshNow()` backs the "advance refresh" dev action. `lastRefreshTick` / `refreshCount` are persisted.
- `Source/Intercolony/Core/IntercolonyTestRecord.cs` — throwaway persisted entity carrying an ID from the generator, a `createdTick`, a label, and a `Pending -> Active -> Closed` state machine with `TryAdvance()` that refuses and logs illegal transitions (§73). Its real purpose is to de-risk the `Scribe_Collections.Look(ref list, ..., LookMode.Deep)` round trip before sales orders and employment contracts depend on it.
- `ClearTestState()` (§95 "clear test state") — resets every probe field but deliberately does not rewind `nextId`, so an ID is never reissued.
- `ValidateIds()` (§67 "validate IDs/references") — if any persisted record's ID is >= `nextId`, advances the counter and warns. Prevents a corrupt save from handing out duplicate IDs.
- Save schema bumped 1 -> 2 with a real, exercised migration step (§62). New fields are additive, but bumping deliberately turned the previously untested migration path into tested code, using the Phase 1 save as the fixture.
- `IntercolonyDebugActions` grown to nine actions (open window, dump state, create test record, advance all test records, advance refresh, clear test state, set test values, counter +1, allocate ID). Repeated null-check boilerplate collapsed into a `WithState(Action<...>)` helper.

Not implemented:
- No "print serialized state" in the §67 sense of dumping the actual Scribe XML. `DebugStateSummary()` is structured text. Dumping real XML outside a save cycle means driving Scribe machinery manually; not worth the risk yet.
- No mod settings (§66), so `IntercolonyLog.Verbose` is still gated on `Prefs.DevMode`.
- No seeded per-refresh RNG (§60). The refresh currently generates nothing, so there is no RNG to isolate; needed before opportunity generation lands.
- "Create test entity" creates the throwaway `IntercolonyTestRecord`, not a domain entity — no domain entities exist until Phase 3.
- No dev palette registration, no custom keybinding for the window. The debug actions menu (`/`) is the only entry point.

Known limitations:
- `IntercolonyTestRecord`, `testCounter`, `testString`, and `testRecords` are all scaffolding. They must be deleted when real persisted entities arrive, which will mean another schema bump.
- Forcing a refresh does not shift the schedule — the next scheduled refresh still lands on the next multiple of `RefreshIntervalTicks`. Intentional (no drift), documented on `ForceRefreshNow`, but surprising if you expect "advance" to reset the timer.
- The refresh fires whenever `TicksGame % 60000 == 0`, so it also fires at tick 0. Harmless while the refresh is a no-op; revisit when it does real work.
- `DebugStateSummary()` is rebuilt every frame the window is open (once for the dump, and the button row calls it again on click). Fine for a dev window at current state size; do not reuse this pattern for player-facing UI (§84 "lazy UI calculations").

Manual test:
- `dotnet build Source/Intercolony/Intercolony.csproj` — 0 warnings, 0 errors. `Assemblies/` still contains only `Intercolony.dll`; no Harmony DLL leak.
- In-game, dev mode. Note: the action sequence actually run differed from the scripted one, so specific counter values were not compared against predicted numbers. The behaviours below were each confirmed:
  - Loading the existing schema-1 Phase 1 save logged the `1 -> 2` migration and came up at `saveVersion 2`.
  - The debug window opened from the debug actions menu and stayed usable during play.
  - Test records created through the window persisted across save -> **quit to main menu** -> reload, retaining IDs, labels, and state-machine states. This is the `LookMode.Deep` list round trip, the main thing Phase 2 was meant to de-risk.
  - `Clear test state` zeroed the probe fields without rewinding `nextId`; a record created afterwards received an ID above the deleted ones.
  - `Advance refresh` incremented the refresh counter and recorded the tick.
- No red errors in the in-game dev debug log window. The §95 acceptance criterion is met: a known test state can be forced in seconds without waiting through gameplay.
- No red errors in the in-game dev debug log window. All four §94 acceptance criteria pass.
