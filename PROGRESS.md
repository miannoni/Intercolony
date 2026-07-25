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
