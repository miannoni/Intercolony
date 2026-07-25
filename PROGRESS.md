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

---

## Phase 3 — Settlement economic profiles  (2026-07-25)

Implemented:
- `Source/Intercolony/Core/IntercolonyProductCategory.cs` — the six product buckets straight from DESIGN.md §10 (commodities, intermediate, manufactured, furniture, capital equipment, art/unique), with a cached `All` array. Weighting buckets only; mapping concrete (possibly modded) ThingDefs into them is a market-phase problem (§64).
- `Source/Intercolony/Core/SettlementEconomicProfile.cs` — the §9 profile: settlement/faction ids, tech tier, wealth tier, archetype, per-category demand and supply weights, quality preference, labor placeholder, volatility, and the derivation seed.
- `Source/Intercolony/Core/SettlementProfileGenerator.cs` — deterministic generation and eligibility.
  - **Deterministic regeneration rather than persistence**, which §96 explicitly permits. Only a single `economySeed` int is saved; each profile is derived from `Gen.HashCombineInt(economySeed, settlement.ID)`. This buys three acceptance criteria directly: destroyed settlements need no orphan cleanup, modded factions put nothing in the save file, and save/load is stable because the same seed reproduces the same profile. It also means the profile shape can change without a schema migration.
  - Rolls run inside `Rand.PushState(seed)` / `Rand.PopState()` so the global random stream is untouched (§60).
  - `NormalizeTech` resolves `TechLevel.Undefined` to Industrial. Undefined is the enum's zero value, so without this every `tech <= TechLevel.Medieval` test silently classified unset-tech factions as neolithic and pushed them toward the Tribal archetype — while `TechSupplyFactor` treated the same value as industrial. Modded factions routinely leave `techLevel` unset (§63, §64).
  - `GenerateFrom(...)` takes plain values rather than a `Settlement`, so generation is a pure function and can be exercised against inputs vanilla never produces (§83.1).
  - Tech gates supply far harder than demand (§50): a neolithic settlement cannot manufacture a fabrication bench, but wanting one is plausible.
  - Eligibility (§51 "simplest intuitive rule") checks only structurally stable traits — spawned, has a faction, not the player's, not hidden/temporary, not a permanent enemy. Deliberately excludes goodwill so profiles do not wink in and out as relations drift; relationship/comms gating belongs to the market layer.
- `IntercolonyWorldComponent`: persisted `economySeed` (derived from `world.info.Seed`, not drawn from `Rand`, so the economy does not depend on *when* the first profile was requested), a non-persisted profile cache with faction-change invalidation, `AllProfiles()`, `ClearProfileCache()`, `RerollEconomySeed()`, and `PruneProfileCache()` on the coarse refresh for §87.
- Save schema bumped 2 -> 3 with a migration step.
- Debug inspector (§96): `Dump settlement profiles`, a `ToolWorld` click-a-settlement inspector, a profiles pane in the debug window (throttled to ~4 rebuilds/sec — building 60+ StringBuilders per GUI pass was visibly janky), `Clear profile cache`, `Reroll economy seed`.
- `Source/Intercolony/Debug/IntercolonyProfileSelfTest.cs` — in-game assertions (§83.2) covering the two criteria that cannot be reached by playing a vanilla world: synthetic modded-faction inputs, and §60 RNG isolation.
- `Test settlement removal (DESTRUCTIVE)` debug action — exercises §87 end to end and prints PASS/FAIL.
- `IntercolonyLog` now prefixes **every** line of a multi-line entry. RimWorld writes multi-line messages as plain consecutive lines in `Player.log` with nothing marking continuations, so any tag-grep filter kept the header of a state dump and silently dropped the body.
- `dev.ps1` added to the repo (was sitting one directory above, where `$PSScriptRoot` made `$Proj` unresolvable) and its argument binding fixed: `$Note` declared `[Parameter(Position = 1)]` while `$Task` had no `[Parameter()]` attribute, so PowerShell bound the first positional argument to the lowest *declared* position. Every invocation silently ran the default `cycle` task, rebuilding and restarting the game regardless of the argument given.

Not implemented:
- Profiles are not persisted, by design. Anything that genuinely accumulates — commercial reputation, demand saturation, order history — must be stored separately when it arrives; it cannot live on the profile.
- No market access gating (§51: discovery, comms console, prior trade, relationship thresholds). Eligibility is structural only.
- Labor is a single placeholder multiplier (§96 "labor tendency placeholder"). No worker pools.
- No pricing, no opportunities, no ThingDef-to-category mapping. Phase 4+ (§97).
- `RerollEconomySeed` draws from `Rand` rather than deriving. Acceptable because it is a manual dev action, not something that happens in play.

Known limitations:
- **"Modded factions do not crash" is verified synthetically, not with a faction-adding mod installed.** The self-test drives every `TechLevel` including `Undefined`, null names, and extreme IDs through `GenerateFrom`, which covers the code paths — but no third-party faction has actually been loaded. Install one (e.g. a faction pack) and re-run `Dump settlement profiles` plus the self-test before treating this criterion as fully closed.
- The profile cache is pruned only on the coarse refresh or manually, so a destroyed settlement's entry can linger up to one in-game day. Harmless: `AllProfiles()` iterates live settlements and `GetProfile` rejects unspawned ones.
- Forcing a refresh still does not shift the schedule (carried over from Phase 2).
- An intermittent `Root level exception in OnGUI(): NullReferenceException` at `UIRoot_Play.UIRootOnGUI` was seen on 2 of 6 `-quicktest` launches. **Not attributable to Intercolony**: it reproduces on neither a fixed build nor a fixed configuration (identical Phase 3 code gave 4 occurrences twice and 0 occurrences four times), the trace contains no Intercolony frames, it fires before the world component initializes, and the only Intercolony OnGUI surface (the debug window) was closed. A plausible vanilla mechanism is `WorldRendererUtility.CurrentWorldRenderMode` dereferencing `Find.CurrentMap.generatorDef` during map generation (`reference/decompiled/RimWorld.Planet/WorldRendererUtility.cs:35`), but this is unproven. Watch for it during normal, non-quicktest play.

Manual test:
- `dotnet build` via `dev.ps1 build` — 0 warnings, 0 errors; `Assemblies/` contains only `Intercolony.dll`.
- Startup: `[Intercolony] loaded.` then `[Intercolony] State initialized fresh (schema 3).`, no Intercolony errors.
- **Every eligible settlement gets a profile**: 64 eligible, 64 profiles in one world; 48 and 57 in later worlds. No gaps.
- **Profiles differ**: all 8 archetypes present in a 64-settlement world (14 Agricultural, 12 Mixed, 10 Frontier, 8 Industrial, 7 Affluent, 6 Tribal, 4 Military, 3 TradeHub) and all 4 wealth tiers.
- **Deterministic regeneration**: dumps before and after `Clear profile cache` were byte-identical by md5 (768-line bodies). A reroll produced a different dump, confirming the comparison was actually sensitive.
- **Save/load stable**: dump before save vs. after save -> quit to main menu -> reload were byte-identical by md5 (577 tagged lines each, 48 settlements). Critically, **no `Derived economy seed` line appeared after the reload** — proving the seed was read from the save rather than re-derived, which would have coincidentally produced the same value and masked a persistence failure.
- **§50 tech gating**: mean capital-equipment supply was 0.02 for 33 neolithic settlements (at the floor; highest individual 0.03) versus 0.76 for 31 industrial ones, while neolithic *demand* stayed at 0.65. The Tribal archetype appeared only at Neolithic (6/6).
- **Profile self-test**: 154 passed, 0 failed. The count cross-checks against the test design (8 tech levels x 17 assertions, + 12 awkward-ID checks, + 4 determinism, + 1 variety, + 1 RNG isolation = 154), confirming every assertion ran rather than silently skipping. Includes the §60 check that generation leaves the global `Rand` stream untouched — invisible from the UI and never previously verified.
- **Destroyed settlements (§87)**: `Test settlement removal` reported PASS — victim `Planeton` (id 0), 57 eligible before with profile and cache entry present, 56 eligible after with `IsEligible=False` and `GetProfile` null, and the cache entry gone after a prune.
- Evidence for the last two items was read from the in-game dev debug log window and pasted back, not from `Player.log`: a second RimWorld instance was launched and closed during that window, so `Player.log` may belong to the short-lived process.
- All five §96 acceptance criteria pass, with the modded-faction caveat recorded above.

---

## Phase 4 — Market opportunity generation  (2026-07-25)

Implemented:
- `Source/Intercolony/Market/MarketOpportunity.cs` — the §7.2 entity: buyer, item, quantity, unit price, expiry, delivery deadline, distance, and a pre-computed price explanation. Persisted (§61) with an `Available -> Expired` state machine (§73). `IsValidAfterLoad` detects an unresolvable `ThingDef`, which is what a removed mod looks like on load (§64, §86).
- `Source/Intercolony/Market/IntercolonyProductClassifier.cs` — maps `ThingDef` to a §10 category by category ancestry and def properties, never a hard-coded vanilla defName list (§63). A modded steel-equivalent lands in IntermediateGoods without Intercolony knowing the mod exists.
- `Source/Intercolony/Market/IntercolonyTradeBlacklistDef.cs` + `IntercolonyTradeBlacklist.cs` + `Defs/IntercolonyTradeBlacklistDefs/TradeBlacklist.xml` — the §64 "debug/settings tooling to blacklist problematic items". Rule-based (exclude by comp, by category, or by def) rather than a defName list, and additive across defs so other mods or the player extend it with new XML instead of overriding. The shipped rule excludes anything with `CompHatcher`, which is how vanilla filters the same thing (`ThingFilter.disallowWithComp`), so modded fertilized eggs are caught too.
- `Source/Intercolony/Market/IntercolonyPricing.cs` — the single place prices are computed (§46 explicitly: "do not scatter pricing formulas"). Starts from `BaseMarketValue`, then applies local demand, buyer wealth, lot size, distance, and quality expectations, each recorded as a named `PriceFactor` so the §47 breakdown is real data rather than a recomputation. §13 saturation is a continuous 1.22x -> 0.96x decay, so there is no tier cliff to game.
- `Source/Intercolony/Market/MarketOpportunityGenerator.cs` — §11 demand generation on the existing coarse refresh (§59, §84), seeded per (economy seed, settlement, refresh number) inside a pushed `Rand` state (§60). Quantity targets a silver value rather than a unit count, so 5 healer mech serums and 1,025 units of meat are both plausible asks.
- `Source/Intercolony/Market/IntercolonyMarketAccess.cs` — §51 market access. Hostile factions neither generate demand nor keep existing listings. Kept separate from `SettlementProfileGenerator.IsEligible`: eligibility is structural and must stay stable so profiles do not regenerate as goodwill drifts, while access is volatile and answers "can the player trade right now".
- `Source/Intercolony/UI/MainTabWindow_Intercolony.cs` + `Defs/MainButtonDefs/Intercolony_MainButtons.xml` — the §53 market tab. Columns per §53 including Distance, all sortable by clicking the header (click again to reverse), with numeric columns defaulting to descending and ties broken on id so rows do not jitter between frames. Distance filter slider per §53/§66, persisted per save. Row tooltip shows the §47 price breakdown.
- Save schema 3 -> 4 (Phase 1/2 test probes retired now that a real persisted entity exists; `IntercolonyTestRecord` deleted) and 4 -> 5 (distance filter and per-opportunity distance).
- `Source/Intercolony/Debug/IntercolonyMarketSelfTest.cs` and debug actions: dump opportunities, dump product classification, dump trade blacklist, advance refresh, expire all, clear.

Not implemented:
- Accepting an opportunity. It stays non-binding; turning one into a Sales Order is Phase 5 (§98). The market tab is deliberately read-only.
- Only stackable items are traded (fungible lots, §23.1). Furniture, capital equipment, and art classify correctly but are excluded, because they need the unique-item snapshot path (§23.2, §24).
- The other §51 access gates: settlement discovered, comms console, caravan contact, prior trade. §51 asks for the simplest intuitive rule first, so only hostility is enforced.
- The other §53 filters: faction, category, item, quality, minimum value, fulfillment mode. Only distance exists.
- The §53 Fulfillment column, which needs the logistics models in §25.
- §66 mod settings. The distance filter is per save, not a global setting, and `IntercolonyLog.Verbose` is still gated on `Prefs.DevMode`.
- Demand saturation is per lot only. §13's "temporary market state" — a settlement's appetite decaying as it repeatedly buys the same good — needs order history that does not exist yet.

Known limitations:
- Repeated forced refreshes at the same tick stack up demand, because generation is per refresh number rather than per elapsed time. Harmless for a dev action, but it means the debug button is not a faithful simulation of a day passing.
- Distance is `WorldGrid.ApproxDistanceInTiles` from the player's home map, which ignores terrain and actual caravan travel time. §17 wants travel time surfaced to the player before an order can be silently missed; that needs real routing.
- Runtime blacklist exclusions (`AddRuntimeExclusion`) are session-only and not persisted. XML rules are the durable path; the runtime one exists for the §66 settings hook.
- The per-settlement cap counts all listings including those hidden by the distance filter, so a filtered view can look emptier than the cap suggests.
- The saturation curve, wealth factors, and archetype weight tables are all first-pass guesses. §78 balance work has not started.

Manual test:
- `dev.ps1 build` — 0 warnings, 0 errors. Custom def type `Intercolony.IntercolonyTradeBlacklistDef` and the `MainButtonDef` both parse with no XML errors; 16 `MainButtonDef`s load against vanilla's 15.
- Blacklist verified from the log: 10 fertilized egg defs excluded (chicken, cobra, iguana, tortoise, cassowary, emu, ostrich, turkey, duck, goose), each reported with its reason; 160 tradable fungible defs remain. Confirmed in game that no fertilized eggs appear in the market.
- Market self-test: **39 passed, 0 failed**, over a sample of **86 generated opportunities from 12 settlements**, with **31 inaccessible settlements present** so the access assertions had real subjects. Covers classifier coverage, blacklist enforcement at both classification and generation, saturation monotonicity, that the §47 breakdown reconstructs the price it explains, no quality factor on goods without `CompQuality`, generation determinism, unique IDs, and RNG isolation.
- Opportunity counts per refresh fell from ~36 to ~9-11 once §51 access gating landed, consistent with 31 of ~57 settlements being inaccessible.
- Market tab verified in game: table renders, all columns sort ascending and descending, price-breakdown tooltip appears on hover, distance filter narrows the list.
- Save/load: after save -> quit to main menu -> reload, the log shows `State loaded (schema 5, nextId 44)` and a dump of 43 opportunities with IDs #1-#43, prices and price explanations intact, zero `<missing def>` entries and zero drop warnings. This exercises `Scribe_Defs` reference resolution, a path Phases 1-3 never touched — an unresolvable def would have been reported by `IsValidAfterLoad` and printed as `<missing def>`. Caveat: no pre-save dump was taken, so this is not the byte-for-byte comparison used in Phase 3; it confirms survival and def resolution, not field-level equality.
- All five §97 acceptance criteria pass.

Bugs found and fixed during the phase:
- **Quality expectations applied to goods that cannot have quality.** Reported from a screenshot showing a "Quality expectations -3.4%" line on chemfuel. `QualityPremium` ran unconditionally; it is now gated on `def.HasComp(typeof(CompQuality))`. Since every vanilla quality-bearing item has `stackLimit 1` and Phase 4 only trades stackables, the factor is now dormant until §24 — correct rather than dead. A self-test sweep now prices every tradable def and fails if the factor reappears on a non-quality good.
- **The market self-test was passing vacuously.** It reported "26 passed" while testing exactly one (settlement, refresh) pair; generation is ~35% per settlement, so that pair usually produced nothing, the four per-opportunity assertions ran zero times, and a `|| runA.Count == 0` escape hatch made the "different refresh changes the roll" check pass without evidence. Rewritten to sweep up to 60 cycles to find one that generates, drop the escape hatch, and sweep invariants over a 12-settlement x 25-cycle sample. The report now prints its sample size so a vacuous pass is visible rather than inferred.
- No red errors in the in-game dev debug log window. All four §94 acceptance criteria pass.
