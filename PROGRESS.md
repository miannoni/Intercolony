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

---

## Phase 5 — First playable vertical slice: commodity Sales Order  (2026-07-25)

The first complete gameplay loop: see demand -> accept -> deliver -> receive silver.

Implemented:
- `Source/Intercolony/Orders/SalesOrder.cs` — the §7.3/§15 entity. Persisted, with a locked-in unit price so later market drift cannot change an agreed deal, and `deliveredQuantity`/`paidSilver` so partial deliveries are first-class.
- Lifecycle `Accepted -> Completed | Failed | Cancelled` (§14). §14 sketches a longer chain, and explicitly says the initial implementation does not need every state. **There is deliberately no `InTransit` state**: the caravan *is* that state — the goods are physically on the map, owned by pawns, visible to the player. A parallel status field would be a second source of truth that could disagree with the world.
- `Source/Intercolony/Orders/SalesOrderService.cs` — the only place order status is assigned (§70, §73: "UI should not arbitrarily mutate status fields"). Accept, deliver, complete, fail, cancel, and the overdue sweep all live here.
- `Source/Intercolony/Orders/OrderValidation.cs` — structured validation (§18, §74: "Return structured results, not only booleans"). `OrderValidationResult` carries matched quantity, missing quantity and human-readable failures, so the UI never re-derives a reason and can never disagree with the authoritative check. `OrderValidator.Matches` is the single answer to "does this Thing satisfy this order line".
- `Source/Intercolony/Orders/CaravanArrivalAction_DeliverOrder.cs` — delivery when *sending* a caravan (§25.1, §98). A `CaravanArrivalAction` so it appears in the same float menu as Trade and Visit, with no new UI concept, and §26's abstraction boundary stays intact.
- `Source/Intercolony/Orders/CaravanDeliveryGizmos.cs` — delivery when a caravan is *already parked* at the buyer. Mirrors vanilla's `CaravanVisitUtility.TradeCommand`.
- `Source/Intercolony/Compatibility/HarmonyPatches.cs` — the project's first Harmony patches, both append-only postfixes (`Settlement.GetFloatMenuOptions`, `Caravan.GetGizmos`). Each addition is wrapped in try/catch: an exception while building a float menu or gizmo bar would otherwise cost the player the ability to command caravans at all, so an Intercolony bug degrades to "no delivery option" rather than "game unplayable" (§86).
- Payment follows vanilla's own route for giving goods to a caravan (`Caravan_TraderTracker.GiveSoldThingToPlayer`): find a pawn with room, add to inventory. If nobody can carry it, the player is told rather than having the silver deleted.
- Deadlines checked hourly rather than on the daily refresh (§17: an order must not silently fail; noticing up to a day late would make the message arrive long after the moment it describes).
- Market tab gained an Accept button with a confirmation dialog, and an Orders tab (§54) showing progress, time remaining, payment and outcome.
- Save schema 5 -> 6 (sales orders). Unresolvable orders on load are reported at **error** level, not warning: §62 forbids silently dropping active obligations, and a dropped order is a broken promise the player cannot see.
- `Source/Intercolony/Debug/IntercolonyOrderSelfTest.cs` and debug actions: dump orders, accept first offer, spawn goods for open orders, create order state matrix.

Not implemented:
- No reservation of stock, per §16's explicit "do not build a complex inventory reservation framework before the first vertical slice". Goods are taken at hand-over.
- Only fungible single-line orders. Quality, stuff, hit-point and unique-item matching is Phase 6 (§99); those fields are deliberately absent rather than present-and-unused.
- Only seller delivery (§25.1). Buyer pickup, player pickup and supplier delivery (§25.2-25.4) do not exist, so there is no fulfilment mode to choose.
- No reputation or penalty effects on success or failure (§27, §28). Failing an order currently costs nothing but the goods.
- No travel-time estimate or "delivery appears impossible" warning, which §17 asks for. The Orders tab shows time remaining and distance is in the market tab, but nothing computes whether the trip is actually achievable.
- No transaction history (§75).

Known limitations:
- **Opportunity flood at real-world scale.** A refresh on a full-size map generated 333 offers, reaching 695 live. The per-settlement cap of 3 has no global ceiling behind it, and §5.2 explicitly rejects an "infinite global catalog". Found during Phase 5 play-testing; it is a Phase 4 generation defect that only appears at scale. To be fixed next.
- Cancelling an order forfeits anything already delivered, with no partial settlement.
- The order list grows without bound: completed and failed orders are retained forever so the player can see what happened. Needs pruning or archiving eventually.
- `CaravanArrivalAction_DeliverOrder` stores the order by id rather than by reference, because orders live in the world component rather than the Scribe reference graph. Correct, but it means a deleted order leaves an arrival action that resolves to nothing — handled by returning early with a message.

Manual test:
- `dev.ps1 build` — 0 warnings, 0 errors. Both Harmony patches apply: `[Intercolony] Harmony patches applied.`, no patch errors.
- Order self-test: 38 passed, 0 failed. Covers the state machine refusing every illegal transition, payment arithmetic across partial deliveries (floored so repeated partials cannot overpay), deadline maths, the validation contract, def matching, and the overdue sweep touching only lapsed open orders.
- **Full loop played for real, without dev tools for the transaction itself**: accepted an offer in the market tab, took a caravan to the buyer, delivered, collected silver, returned. Log: `Order 364 completed. Delivered 25 units for 628 silver.`
- Save/load matrix (§98): five orders — open, partially delivered, completed, failed, cancelled — dumped, saved, quit to main menu, reloaded, dumped again. **Byte-identical by md5** (16 lines each). The partially delivered order retained both `deliveredQuantity` 40/100 and `paidSilver` 60/150, which is the state most likely to be lost.
- All §98 acceptance criteria pass.

Bugs found and fixed during the phase:
- **Double-accept duplication exploit.** The order self-test caught `Accept` creating two binding orders from one offer (#33 and #34, 9,507 silver each). `Accept` removed the opportunity from the world's list but never changed the opportunity's own state, so any caller still holding a reference — a UI row captured earlier in the frame, a second click on the confirmation dialog — saw it as available. Fixed by adding an `Available -> Accepted` transition on the opportunity itself (§14, §76.1): removal from a list cannot stop a caller that already has the object, but a state check on the object can. A first attempt at the fix claimed the offer *before* validating the buyer, which would have consumed a listing while creating no order on any transient failure; the claim now happens after all validation, where nothing below can fail.
- **No way to deliver from a parked caravan.** Delivery was implemented only as a `CaravanArrivalAction`, which fires on the transition into the tile. A caravan already sitting at the settlement — arrived earlier, travelled there for another reason, or loaded from a save — had no arrival left to trigger and could never deliver. Found immediately in play-testing. Fixed by adding the caravan gizmo, mirroring how vanilla exposes trading.

---

## Interlude — opportunity flood fix  (2026-07-25)

Found during Phase 5 play-testing on a full-size map: one refresh generated 333 offers, reaching 695 live. The per-settlement cap of 3 had no global ceiling behind it, so total demand scaled with world size — invisible on a small quicktest map, unusable on a real one, and squarely against §5.2 "No infinite global catalog".

- Added `MaxLiveOpportunities = 60` as a hard ceiling, checked before generating.
- Settlements are now visited in a **seeded shuffle** rather than world-object order. Iterating in order would let the same handful of settlements claim every slot on each refresh, so distant or late-indexed settlements would never post anything — the opposite of §48's "avoid making far settlements useless". Seeded so the choice stays reproducible (§60).
- Market self-test gained a regression assertion: 12 extra refreshes must not push live offers past the ceiling. Verified 0 -> 21 (small map), 0 -> 36 (later run), ceiling 60.

---

## Phase 6 — Generalized Sales Order item matching  (2026-07-25)

Implemented:
- `Source/Intercolony/Orders/OrderLine.cs` — the §15 order line: item, quantity, and optional minimum quality, required material, and minimum condition. Constraints are opt-in, so a plain commodity line carries no quality or material baggage.
- One centralized matcher, which is §99's entire acceptance criterion ("One centralized validation path supports all test cases"). `OrderValidator.Matches(OrderLine, Thing, out MatchFailure)` is the only place the question is answered; delivery, the market UI, gizmo availability and pricing all route through it.
- `MatchFailure` reason codes and per-reason aggregation, so a shortfall reads "2 carried below the required quality" rather than a generic "you are short 2". §18's worked example is exactly this distinction.
- **Minified-thing unwrapping.** Furniture and equipment travel as a `MinifiedThing` whose own def is "MinifiedThing", with the real item inside. Matching on `thing.def` would have meant §99's dining chairs could never match anything a caravan is physically able to carry. `CountableUnits` also treats a minified item as one unit rather than trusting the wrapper's stack count.
- Generation widened from stackable-only to all items. Phase 4's `stackLimit > 1` filter silently excluded **everything with a quality rating**, since every quality-bearing vanilla thing has `stackLimit 1` — so weapons and apparel, precisely what §99 needs, were unreachable. Tradable defs went from 160 to 307.
- Quality demands are generated for quality-capable goods only, weighted by the settlement's quality preference, and capped below Legendary — a demand nobody can reliably fill is not interesting, just an offer that never gets taken. Some buyers ask for a plain knife, some for an excellent one; that spread is intentional.
- Quality floors feed pricing as a named §47 factor (`Requires Excellent+`), scaling steeply because each step up is markedly rarer to produce.
- Constraints carry from the advertised opportunity into the binding order, so a player can never be held to terms different from the ones shown.
- Save schema 6 -> 7. `SalesOrder.ExposeData` still reads the schema-6 item and quantity nodes and rebuilds a line from them, so an order accepted before this change keeps its terms instead of becoming an empty promise (§62).
- Market table layout fixed: Accept now has its own column instead of being drawn over the last one, headers shortened, cells given a gutter so long values truncate honestly instead of running underneath the next column, and the deadline wording moved into the row tooltip.

Not implemented:
- **Buildings are still not generated as demand** — furniture, capital equipment and art. See "Open commitments" in `CLAUDE.md`: Matteo wants everything tradeable and accepted this deferral only on the condition it is raised again at Phase 7.
- Material and condition constraints exist on `OrderLine` and are enforced by the matcher, but nothing generates them yet. Only quality floors are produced.
- No exact-quality constraint (§15 `exactQuality`), only minimums.
- Still single-line orders. §15's `lineItems[]` remains a list of one, deliberately.
- No "find buyer" flow, no category-based selectors — the line names a concrete `ThingDef`.

Known limitations:
- Widening generation to all items means some odd asks are now possible (a settlement wanting a single high-value apparel item). Quantity is silver-targeted so the lots stay sane, but the item spread has not been balance-tested (§78).
- The quality-demand chance and price premiums are first-pass guesses.
- `MinifiedThing` unwrapping is verified by the self-test against a constructed chair, but no chair has been delivered by caravan in play, because generation never produces one.

Manual test:
- `dev.ps1 build` — 0 warnings, 0 errors.
- Order self-test: **43 passed, 0 failed**, including all four §99 cases driven through the single matcher with real spawned Things — 1,000 Rice (potatoes), 200 Cloth, 5 Normal-or-better knives, 20 Excellent dining chairs. Each constrained case also asserts an Awful item is rejected *with the quality reason specifically*.
- Market self-test: **40 passed, 0 failed**, sample of 69 generated opportunities across 12 settlements, 29 inaccessible settlements present.
- Classifier count rose 160 -> 307, confirming the single-stack widening actually reached weapons and apparel rather than being a no-op.
- Confirmed in game that quality demands appear in the market and display as e.g. "Airwire headset (normal+)", "Tuque (excellent+)", with the quality premium visible in the price tooltip.
- The §99 acceptance criterion is met: one validation path supports all four test cases.

---

## Phase 7 — Unique goods / capital equipment technical spike  (2026-07-25)

A spike, not a feature. §100's deliverable is a written technical note, and its acceptance criterion is "a robust strategy exists before generalized implementation".

Implemented:
- `docs/unique-goods-spike.md` — **the deliverable**. Documents the chosen representation, serialization strategy, unsupported edge cases and compatibility risks, as §100 requires.
- `Source/Intercolony/Debug/IntercolonyUniqueGoodsSpike.cs` — the evidence behind the note. Runs §100's cases 1, 2, 3/5, 6 and 7 in one pass, plus a two-part probe for case 4 (plant objects, save/load, verify) since that cannot complete in a single call.
- Three debug actions: run the spike, plant save/load probes, verify them after reload.

Findings (full reasoning in the note):
- **`Thing`s are moved, never copied.** This deliberately contradicts §23.2's phrasing about "unique item snapshots". A snapshot must enumerate what it preserves, so anything it does not know about is dropped — including `CompArt.taleRef`, ideoligion style sources, and **any comp added by any mod**. §64 flags unsafe custom comps as a hazard, and a snapshot is exactly the construct that turns an unknown comp into silent data loss. Moving the object cannot lose a comp it has never heard of.
- Snapshots are still right for *describing* an item in a listing the player does not own. Phase 6's `OrderLine` already fills that role, so no new type was needed for either job.
- **Installation needs no custom code.** A `MinifiedThing` placed on the map is installed through vanilla's own `Blueprint_Install` flow. That is a finding, not a gap.
- Intercolony serializes nothing about a unique object: orders persist a `ThingDef` plus constraints, and the object lives in vanilla containers whose serialization is RimWorld's responsibility.
- The existing Phase 5/6 delivery path already handles unique goods unchanged — `Matches` unwraps minified things, `CountableUnits` counts a crate as one, and `RemoveFromCaravan` splits a stack of one cleanly.

Not implemented:
- No production code changed. Generation still excludes buildings; that is Phase 8 (§101).
- Art re-attribution after sale is left as an open design question, not answered.
- No balance work on unique-good lot sizes (§78).

Known limitations:
- **Only one modded minifiable building was available to test.** Case 7 passed against `Building_RTCircuitBreaker` from RT Fuse, which is genuine third-party coverage, but a single data point from a simple mod. A vehicle or furniture-framework mod would be a stronger test and has not been run. Recorded as an open compatibility risk in the note rather than treated as covered.
- Mods that *subclass* `MinifiedThing` are handled in principle — `GetInnerIfMinified` uses an `is` check — but none was loaded to confirm.
- A sold sculpture's tale reference leaves the world with it, because delivery destroys the handed-over object. Correct for a sale, but it means art sold to a settlement has no continuing existence.

Manual test:
- Spike run in game: **23 passed, 0 failed** across cases 1, 2, 3/5, 6, 7.
- Case 4: a crated masterwork wooden chair at half hit points and a crated sculpture with a custom art title were planted, then the game was saved, quit to main menu and reloaded. Verification reported **PASS** — quality, reduced hit points, art title and author all intact.
- The §100 acceptance criterion is met: a robust strategy exists, written down, before generalized implementation.

---

## Phase 8 — Finished goods market  (2026-07-25)

Furniture, art, weapons, apparel and equipment are now normal market participants. Implements the recommendation written in `docs/unique-goods-spike.md`.

Implemented:
- Generation widened to buildings **where `def.Minifiable` is true**. Non-minifiable buildings stay excluded permanently, not temporarily: a caravan physically cannot carry a wall, so demanding one would be an offer nobody could ever fill. The self-test asserts none is ever generated. Tradable defs went 307 -> 407.
- **Material-aware valuation** (§101). `ThingDef.BaseMarketValue` is `GetStatValueAbstract(MarketValue)` with *no stuff*, so pricing off it quoted identical silver for a wooden longsword and a plasteel one. `IntercolonyPricing.BaseValue(def, stuff)` now uses the real material, and the §47 breakdown names it on the base line — otherwise the factors below would not reconstruct the quoted price and the explanation would quietly stop being true.
- **Material constraints are generated**, not merely enforced. Drawn from `GenStuff.AllowedStuffsFor` so a demand is always fillable; asking for a plasteel chair when the def forbids plasteel would be an unfillable listing.
- Lot sizes capped by how goods travel, not just by silver: crated buildings at 8, single-stack items at 15. A silver-reasonable lot of 40 sculptures is a caravan that cannot move.
- **Filters** (§53): category dropdown and minimum-value slider, alongside the existing distance filter.
- **Unique listing details and art detail display** (§101): the row tooltip states the quality floor, the required material, a caravan-capacity warning for crated goods (each travels as its own crate), and for artwork a note that quality drives the price and the piece keeps its title and author after sale.
- Material constraints carry from the advertised opportunity into the binding order, as quality already did.

Not implemented:
- No art *title* or author shown in listings, because the artwork does not exist until the player makes it. Only the requirement is described.
- Faction, item and quality filters from §53's list; only category, distance and minimum value exist.
- No balance pass on the widened item pool (§78).

Known limitations:
- The item spread is much wider now, so odd-looking asks are possible (a settlement wanting a single expensive piece of apparel). Quantity is silver-targeted and capped, but this has not been balance-tested.
- Art re-attribution after sale is still an open design question (see the spike note).

Manual test:
- `dev.ps1 build` — 0 warnings, 0 errors.
- **4x tube television** accepted, hauled and delivered in real play. First crated good to complete the full loop: generated as demand, matched through the minified wrapper, handed over, paid.
- **8x large sculpture (normal+)** accepted, spawned, hauled and delivered for **4,500 silver**. `SculptureLarge` is minifiable *and* made from stuff *and* carries `CompArt`, with a quality floor — so this single delivery closed all three paths that were unproven at the end of Phase 7: crated goods, material-made goods, and art, with a quality constraint checked through the crate.
- Classified tradable defs rose 307 -> 407, confirming buildings actually entered the pool rather than the widening being a no-op.
- The §101 acceptance criterion is met: a colony can intentionally operate as a furniture or art business, and did.

---

## Phase 9 — Find Buyer  (2026-07-25)

Surplus-first commerce: "I already have a huge surplus. Who wants it?" (§12, §102).

Implemented:
- `Source/Intercolony/Market/FindBuyerService.cs` — evaluates every accessible settlement's latent appetite for a given good and returns ranked offers.
  - **Deliberately does not search posted listings.** §12's worked example shows demand bands ("Demand: up to 2,000") and a "No current interest" row — that is latent appetite derived from settlement profiles, not a lookup of what happens to be advertised. A surplus of 3,842 rice rarely matches a posted order, so a listing search would answer a much less useful question and usually return nothing.
  - Appetite is expressed in **units**, bounded by the buyer's wealth and category demand, then clamped by how the good travels — the same crated-goods reasoning as generation.
  - Uninterested settlements are returned with a reason, because §12 lists them and "nobody nearby wants this" is a useful answer.
  - Prices route through the same `IntercolonyPricing` path as the market, so §13 saturation still applies. Without that, Find Buyer would be a way to dodge saturation by routing around the market entirely.
- `SalesOrderService.CreateFromOffer` — "create sale from result" (§102). A second entry point into the same binding commitment, so it re-runs the access checks rather than trusting the offer: a settlement evaluated seconds ago may since have turned hostile or been destroyed.
- Find buyer tab: colony stock on the left, ranked buyers on the right, sortable columns, a sell-quantity slider with All/Half, and a Sell button with a confirmation that states the stock is **not reserved** (§16 has no reservation system, so anything the colony eats still has to be replaced before the deadline).
- Stock counts only what is in storage. Loose items scattered across the map are not a surplus the player is choosing to sell.

Not implemented:
- No quality or material selection when offering stock; the search treats a def as fungible. Selling a specific masterwork item through Find Buyer is not possible.
- No category-level search ("who wants any textile?"), only per-def.
- No offer expiry — a Find Buyer quote is computed live and is not a standing commitment from the settlement.

Known limitations:
- Appetite and wealth budgets are first-pass guesses (§78).
- The buyer search runs over every accessible settlement each time the selection or quantity changes. Fine at current scale; would need indexing if settlement counts grow much.

Manual test:
- `dev.ps1 build` — 0 warnings, 0 errors.
- A Find Buyer order was created, delivered and paid in real play.
- The §102 acceptance criterion is met: a surplus can be turned into deliberate sales without browsing every settlement.

Bugs and issues found and fixed during the phase, all reported from play:
- **Severe frame-rate drop when the tab was open.** `FindBuyerService.ColonyStock` walks `map.listerThings.AllThings` and checks storage membership on each. I had throttled the buyer *search* but left the *stock scan* running on every GUI event — twice per frame — so a developed colony's entire thing list was scanned ~120 times a second. Stock is now scanned once on entering the tab and otherwise only on an explicit Refresh. A timed rebuild was rejected: it would make the stutter periodic rather than removing it, and nothing here needs tick accuracy.
- **Buyer columns were not sortable**, unlike the market table. Added, with the same click-to-sort convention and per-column defaults. Uninterested settlements always sort last regardless of column or direction — they have no numbers to compare, and reversing a sort would otherwise bury every real offer.
- **No way to sell part of a stockpile.** Added a quantity slider with All/Half. This is more than convenience: changing the quantity re-prices, because a smaller lot avoids §13 saturation and earns a better unit price, so splitting a surplus across buyers is a real decision.
- **"Will take" showed the amount being offered rather than the buyer's total appetite**, which is the number that makes splitting a surplus possible. Now always shows appetite.
- **Silver could be sold for silver.** Every transaction settles in silver, so this was a direct money printer — buy low, get paid more of the same commodity, repeat — exactly §76.6's "guaranteed arbitrage". Not only a Find Buyer problem: silver passed every tradability test, so the market could have generated silver purchase orders too. Excluded in `IntercolonyProductClassifier` as a **structural invariant rather than a §64 blacklist entry**: a blacklist entry can be removed by another mod's XML, and removing this one would silently reopen the exploit. Self-test now checks silver at every entry point — classifier, tradable set, live offers, Find Buyer — because one uncovered path is enough.
- Added a sweep so opportunities for goods that are no longer tradable are withdrawn on refresh. Without it a save made before an exclusion would keep advertising the item forever, since nothing else revisits an already-generated listing's eligibility.

---

## Phase 10 — Procurement / RFQ MVP  (2026-07-26)

The buy side. §20 calls this "the core anti-vending-machine design", and that framing drove every decision here.

Implemented:
- `Source/Intercolony/Procurement/PurchaseRequest.cs` — the §7.4 RFQ and §7.5 Quotation, both persisted (§61 lists them). Request lifecycle `Open -> Expired | Cancelled` (§73). A quote carries quantity offered, unit price, lead time, fulfilment mode and distance.
- `Source/Intercolony/Procurement/RfqService.cs` — request creation and supplier response generation against §20's factor list: category, settlement technology, profile, requested quantity, distance and random variation.
  - **Quotes are rolled once at creation and then stand until expiry.** Re-rolling on demand would let a player refresh until they liked the price — the §76.1 reroll exploit.
  - Seeded on the request id, so a given request always produces the same quotes and a reported problem is reproducible (§60).
  - Partial quotes are first-class (§20): even a capable supplier has a 35% chance of falling short of a large request, which makes combining two suppliers a real move.
  - Buying costs more than selling, with the spread running against the player: supplier margin plus a scarcity factor, so a settlement with little of a good charges more. Scarcity shows up in price as well as availability.
  - Lead time and fulfilment mode (pickup vs supplier delivery, §25.3/§25.4) vary by distance and wealth.
- `Source/Intercolony/UI/Dialog_CreateRequest.cs` — searchable item selection with quantity and desired deadline. A searchable list rather than a nested float menu, because there are 400+ tradable defs; matches are cached per search string for the same reason the Find Buyer stock scan had to be (§84).
- Procurement tab: requests with their quotes underneath, partial quotes flagged in amber, an explanation when nothing came back, and Withdraw.
- Save schema 7 -> 8, purely additive.

Not implemented:
- **Accepting a quote.** §103's build list ends at "comparison UI"; receiving goods and paying is Phase 11 (§104). The quote tooltip says so rather than leaving a dead button.
- No material or quality constraints on a request; the player asks for a def and a count.
- No relationship or commercial-reputation input to response probability (§20 lists it; §27 reputation does not exist yet).
- No "current market state" input (§20) — quotes do not react to what the settlement is currently buying or selling.

Known limitations:
- Response chances, supplier capacity and the price spread are first-pass guesses (§78).
- A common good can draw 15+ quotes, which is a lot to compare. §19's example shows three. Not capped, because silently dropping suppliers would misrepresent the market, but the list can get long.
- Trade hubs source one tech tier above their own base 50% of the time. That number is invented, not derived.

Manual test:
- `dev.ps1 build` — 0 warnings, 0 errors.
- RFQ self-test: **19 passed, 0 failed**. Diagnostics: 24 sampled requests produced 3 empty, 66 full quotes and 84 partial; 20 of 24 had differing prices between suppliers and 19 had differing quantities; 2 modded defs exercised without crashing.
- Targeted scarcity probe: a psylink neuroformer (Archotech) returned **0 quotes** — genuinely unobtainable rather than merely expensive.
- Requests created through the UI in play (berries, table 2x4) returned quotes correctly.
- All three §103 acceptance criteria met: requesting scarce goods can fail, suppliers differ in price and quantity, and modded goods do not crash request generation.

Bug found and fixed during the phase:
- **Procurement was a vending machine.** The self-test failed on its headline criterion: all 24 sampled requests found a supplier. The cause was structural rather than a tuning problem. Supply capability was checked at **category** level, so `SupplyFor(ManufacturedGoods)` treated a bionic ear and a shirt as the same capability and any settlement that made clothing appeared able to make bionics. Scarcity was then left entirely to a per-settlement dice roll — and with ~30 reachable settlements at roughly 40% each, the odds of all declining are about 1 in 5 million. Lowering that probability was the wrong lever: it would make *everything* unreliable rather than making *scarce things* unavailable.
  Fixed by applying §50's tech gate **per def** rather than per category: a settlement cannot supply an item above its own tech tier, except that trade hubs import one tier up half the time. The test also no longer relies on sampling luck — it sends a targeted probe for the highest-tech tradable def and asserts not every settlement can supply it.
  Worth recording that 14 of 15 assertions passed while this was broken. A test that only checked "quotes are well formed" would have called the phase done.

---

## Phase 11 — Purchase Order fulfilment  (2026-07-26)

Closes the buy-side loop: request → quote → accept → physically receive or collect.

Implemented:
- `Source/Intercolony/Procurement/PurchaseOrder.cs` — the §7.6 entity, recording exactly what was promised (def, material, quality, count) so §104's "preserve expected properties" is verifiable rather than a matter of trust.
- Lifecycle `Confirmed -> ReadyForPickup -> Completed`, plus `Cancelled` and `SupplierDefault` (§21). §21 sketches eight states and says the first implementation may use fewer. **There is no `Delivered` or `InTransit` state**, for the same reason sales orders have none: a caravan in motion *is* the in-transit state, and goods either arrive at the colony and complete immediately or wait at the supplier. A parallel flag would be a second source of truth.
- `Source/Intercolony/Procurement/PurchaseOrderService.cs` — the only place purchase status is assigned (§73). Payment, lead-time advancement, delivery to the colony, caravan collection, refunds and cancellation.
- **Payment is taken up front.** §21 lists a PlayerDefault branch, implying payment on delivery, but that needs a debt-and-default policy that does not exist. Taking silver at acceptance means a purchase can never arrive at a colony that cannot pay for it; refunds on supplier default keep it honest the other way.
- Quotations now advertise **offered quality and material**. §20 lists differing quality as an RFQ outcome, and putting the promise on record is what makes the arrival checkable.
- Delivery drops goods at the trade spot; pickup adds a **Collect purchase** caravan gizmo at the supplier, mirroring the sell-side delivery gizmo. Uncollected goods are resold after a grace period with a refund (§21 SupplierDefault) rather than sitting open forever.
- Goods are built with the promised properties and **crated when minifiable**, per `docs/unique-goods-spike.md` — a building that arrives uncrated cannot be hauled or installed.
- Save schema 8 -> 9. Unresolvable purchase orders are logged at **error** level: a purchase is silver already spent, so losing one is worse than losing a listing.

Not implemented:
- Payment on delivery and the §21 PlayerDefault branch.
- No partial refund on player cancellation — the payment is forfeited, since the supplier already produced the goods.
- No reputation effect from defaulting in either direction (§27).
- Delivery always lands at the trade drop spot; no choice of destination.

Known limitations:
- Lead times, the pickup grace period and the supplier margin are first-pass guesses (§78).
- A delivery with no player home map is held rather than delivered or refunded; it will arrive once a colony exists again.
- Partial caravan collection reduces the outstanding quantity but does not re-price; the player has already paid in full.

Manual test:
- `dev.ps1 build` — 0 warnings, 0 errors.
- RFQ self-test: **44 passed, 0 failed**, including §104's four named cases constructed and inspected — `120x Steel`, `3x Knife (plasteel, excellent)`, `4x Dining chair (wood, good) (crated)`, `1x Electric stove (crated)`.
- **Bought a bed in real play**: `Purchase 174: 1x Bed (gold, good) from Banedla for 120 silver, delivered in 7d` followed seven in-game days later by `Purchase 174 completed. Delivered 1 to the colony.` Material and quality both survived the whole path.
- §104's acceptance criterion is met: purchased goods arrive physically and preserve their expected properties.

Bug found and fixed during the phase:
- **Suppliers offered expensive materials at cheap prices.** The bed above is the evidence: 120 silver for a *gold* bed, when the gold alone is worth several hundred. The offered material was chosen *after* the price was computed, so pricing fell back to `request.stuffDef` — null whenever the player does not specify a material. Buy the gold bed, deconstruct it, keep the gold: a money printer of the same family as the silver-for-silver exploit.
  Fixed by choosing the material before pricing and quantity, and by adding a workmanship factor so quality costs money too — it had the identical hole, letting a supplier offer Excellent work at Normal prices.
  A regression assertion now fails any quote priced below the raw value of what it promises, which covers the class rather than the instance.
  Worth recording how this was caught: **the self-test did not find it, and could not have.** It verified goods are *constructed* with the right material and quality, which was never the broken part. Nothing checked that the *price* reflected the promise. A gold bed delivered correctly for 120 silver only looks wrong if you know what gold costs — it took a human reading a log line.

---

## Phase 12 — Logistics expansion  (2026-07-26)

Fulfilment becomes a choice rather than a label.

Implemented:
- **Buyer pickup (§25.2)**, the mode that did not exist. The player accepts, produces the goods, hits **Mark ready**, and the buyer's caravan travels to the colony, takes the goods out of storage and leaves silver at the trade spot. §25.2's worked example is an "ORDER READY … will arrive in approximately N days" letter; that is now literally what happens.
- New `FulfillmentMode` on both opportunities and orders, and a new `AwaitingCollection` order status — distinct from Accepted because once a buyer is en route the player can no longer quietly consume the stock.
- **Logistics pricing modifier (§105)**: seller delivery x1.12, buyer pickup x0.85, applied as a named §47 factor so the breakdown shows what the convenience costs. Roughly 32% more silver for taking on the round trip.
- Nearer buyers are likelier to collect; a buyer across the planet expects delivery.
- `OrderValidator.CountMatchingInColony` / `TakeFromColony` — the same centralized matcher (§74) applied to colony storage instead of a caravan, so quality, material and condition constraints are enforced identically on the pickup path.
- Arrival events as letters for ready, collected and partial collection.
- Save schema 9 -> 10; existing orders default to seller delivery, which is what they implicitly were.
- `Source/Intercolony/UI/Dialog_ConfirmQuantity.cs` — one shared confirmation dialog with a quantity slider, used by Accept, Buy and Sell. The body rebuilds as the slider moves, so the price shown is always the price being agreed to.
- Partial acceptance: the player may commit to fewer units than a buyer asked for, and fewer than a supplier offered. Never more — prices are struck for the advertised lot.

Not implemented:
- Player pickup and supplier delivery already existed from Phase 11; this phase added no new *purchase*-side modes.
- No choice of mode: the counterparty proposes one, the player takes it or leaves it. Negotiating fulfilment is not in §105's list.
- No transport pods, vehicles or alternative logistics (§26 leaves room; nothing uses it).
- Buyer caravans are abstract — no physical caravan spawns and travels.

Known limitations:
- The x1.12 / x0.85 split and the buyer travel speed are first-pass guesses (§78).
- A buyer arriving while the player has no home map is skipped rather than resolved; it will retry.
- Goods for a pickup order are not reserved, so the colony can consume them before the buyer arrives. That is consistent with §16 having no reservation system, and the arrival handles the shortfall, but it can surprise.

Manual test:
- `dev.ps1 build` — 0 warnings, 0 errors.
- Market table shows both modes in play (`12d haul` and `collected`), confirmed in game.
- Schema 9 -> 10 migration ran on a real save: `existing orders are seller-delivery`, no errors.
- Quantity sliders verified on all three confirmation dialogs.
- §105's acceptance criterion is met: two fulfilment modes with a real trade-off — more silver for hauling it yourself, less effort for letting them collect.

Issues found in play and fixed:
- **The per-unit price did not move with the confirmation slider.** Reported as cosmetic; it was a correctness bug. I had frozen the unit rate out of caution, but §13 saturation means a smaller lot genuinely earns a better rate, so the dialog showed an unchanging "2.50 each" while the amount changed. Worse, accepting a partial order would have **booked it at the full-lot rate** — the dialog and the contract would have disagreed. Prices are now re-computed for the chosen quantity on both sell paths, and the order is created at the re-priced rate. Reducing remains safe from exploitation because quantity falls faster than the unit rate rises, so the total always drops; that is also why the slider only ever reduces. Purchase quotes deliberately keep a fixed rate: a supplier quoted a price for their lot, and buying fewer does not make each unit dearer to them.
- Procurement dialog showed the player's silver holdings, which RimWorld already displays permanently. Replaced with the per-unit price to match the sales dialogs; the shortfall warning stays, since it is actionable.
- Removed the Find Buyer tab's quantity slider now that the amount is chosen at commitment like everywhere else.

---

## Phase 13 — Commercial reputation  (2026-07-26)

Repeated commerce now matters: a settlement remembers how you have dealt with it, and its future offers reflect that.

Implemented:
- `Source/Intercolony/Reputation/CommercialReputation.cs` — a 0-100 score with five tiers (Untrusted, Unproven, Known trader, Reliable supplier, Preferred partner) and §27's counters: completed, late, failed, cancelled, purchases, purchase cancellations.
- **Held per settlement**, keyed by stable `WorldObject.ID`. §27's illustrative UI is headed by a faction name, which is what I built first, but §8 is the stronger signal: "The primary economic actor should be a settlement, with faction-level defaults." It also fits everything else — profiles, demand, supply and access are all per-settlement, so faction-level reputation was the odd one out. Two towns of one faction can now rate you differently.
- **Separate from faction goodwill**, per §27. Goodwill is whether they shoot at you; reputation is whether they rely on you. The Relations tab shows both side by side so the distinction is visible.
- `Source/Intercolony/Reputation/ReputationService.cs` — event hooks and effects. Completing on time +4 plus a capped size bonus, late +1, failure -12, cancellation -6, purchase +2, purchase cancellation -4.
- Effects (§28): more frequent opportunities (x0.6 to x1.5), larger lots (x0.75 to x1.4), slightly better prices (x0.95 to x1.08) and more generous deadlines (-2 to +4 days).
- Relations tab (§57) listing settlements by score with their counters, faction and goodwill.
- Save schema 10 -> 11 -> 12. The 11 -> 12 step re-keys reputation from faction to settlement by reading a **new node name**, so schema-11 records are simply not loaded: a faction record cannot be split across its settlements without inventing history, and the old keys would otherwise be silently misread as settlement IDs.

**§28's anti-runaway constraint drove the numbers**, not balance taste:
- Gains diminish as the score rises; penalties always land at full weight. A reputation is harder to keep than to lose.
- Price is deliberately the smallest effect (~13% across the whole span), because it compounds with both size and frequency.
- The size bonus for a large contract is capped, or one enormous order would outweigh years of steady trade.
- The self-test asserts the **combined** best-case advantage, since three individually reasonable bounds can multiply into an absurd one.

Not implemented:
- No access to scarce goods, recurring contracts, lower deposits or preferred-supplier status (§28 lists them; recurring contracts are Phase 14).
- Reputation does not decay with time or inactivity.
- No effect on RFQ supplier response probability — reputation currently shapes the sell side only.
- No letters or notifications on tier change.

Known limitations:
- All weights and bounds are first-pass guesses (§78).
- Records for destroyed settlements are retained as history and shown as "(gone)". They are never pruned.
- A settlement changing hands keeps its reputation, which is arguable either way.

Manual test:
- `dev.ps1 build` — 0 warnings, 0 errors.
- Reputation self-test: **17 passed, 0 failed**.
- **§106's acceptance criterion demonstrated quantitatively.** Same settlement, same seeds, 120 refresh cycles each, varying only the trade history: a trusted partner produced **101 offers averaging 213 units and 16.5-day deadlines**; a distrusted one produced **39 offers averaging 86 units and 11.1-day deadlines**. Holding everything else constant is what makes the difference attributable to reputation rather than noise.
- Combined best-case advantage measured at **x2.27** against a neutral partner, inside the x3 bound.
- The schema 9 -> 12 migration chain ran on a real save, walking all three steps in order.

Design change during the phase:
- Reputation was first built per faction and re-keyed to per settlement at Matteo's request. His instinct matched §8 better than my reading of §27's UI mock-up did — the mock-up's faction heading is a presentation detail, while §8 is a structural statement about who the economic actor is.

---

## Phase 14 — Recurring contracts  (2026-07-26)

Standing supply agreements: a fixed quantity every quadrum for a fixed term.

Implemented:
- `Source/Intercolony/Contracts/RecurringContract.cs` — the §29/§30 entity with an `Offered -> Active -> Completed | Breached | Cancelled | Declined` lifecycle (§73). Deliberately the simple version §30 prescribes: **fixed quantity, fixed cadence, fixed duration, fixed price formula**. Category selectors, quantity ranges and negotiated terms are listed there as later work.
- `Source/Intercolony/Contracts/ContractService.cs` — offers, cycle advancement, breach, completion and cancellation. Owns every status transition.
- **Gated on commercial reputation** (62+), which makes §28's "access to recurring contracts" concrete and gives Phase 13 somewhere to lead. A settlement will not stake a year of supply on someone with no record.
- **Priced ~15% above spot.** Without a premium there is no reason to accept one, and §29's stated objective — "a future demand commitment causes the player to expand capacity" — never happens. The buyer is purchasing certainty; the player gives up the freedom to sell elsewhere.
- Each cycle raises a **real sales order** with the full cadence as its deadline, so contract deliveries flow through the existing delivery, validation, payment and reputation machinery rather than a parallel system. `SalesOrder.contractId` links them.
- **Breach after two consecutive misses** (§30 grace period), with a successful delivery clearing the strike. Breach costs -20 reputation; completing a full agreement gains +8; withdrawing costs -10.
- Contracts tab: offers first, then live agreements, then history. The acceptance dialog states the obligation as **units per day of sustained output**, since that is the number that tells the player whether they can hold the pace.
- Save schema 12 -> 13, purely additive.

Not implemented:
- **Renewal** (§107 lists it). A completed agreement simply ends; nothing offers to continue it.
- No category-based contracts — §107's "category Y" wording is deferred in favour of §30's "start simple" exact-product version.
- No quantity ranges (§29's example shows 800-1,200 per quadrum), quality or material requirements on contracts, or negotiated price rules.
- No buyer-pickup contracts; every cycle is seller delivery.
- No partial-cycle credit: a delivery either meets the full quantity by the deadline or counts as missed.

Known limitations:
- Offer chance, premium, cycle count and breach threshold are first-pass guesses (§78).
- Contract quantities can be large (a real offer in testing was 1,850 units per quadrum). That is intentional — §29 wants commitments that force capacity expansion — but it is unbalanced.
- Only one live agreement or proposal per settlement.

Manual test:
- `dev.ps1 build` — 0 warnings, 0 errors.
- Contract self-test: **17 passed, 0 failed**. Covers the state machine, a 3-cycle contract raising one order per cycle and completing, breach at exactly the threshold, a single miss *not* ending the agreement, and a later delivery clearing the strike.
- Real offer built through the shipped path: **1,850x caribou meat @ 3.23 vs spot 2.81, 5,977 silver per cycle** — the premium and the scale are both visible rather than merely asserted.
- **§107's acceptance criterion met**: a 4-cycle contract was planted, saved, reloaded from the main menu and verified — `Active, 1/4 delivered, 100x Steel @ 2.19`, terms and progress intact.
- The schema 9 -> 13 migration chain ran on a real save, walking all four steps.

Issues found and fixed during the phase:
- **The self-test produced a false failure.** It asserted "a contract pays more per unit than spot" against a contract *it had constructed itself* at `BaseValue x 1.15`, ignoring demand, wealth, saturation, distance and logistics — so it was checking its own arithmetic against an object the shipped code never produces. Fixed by extracting `ContractService.BuildOffer` and testing the real path. This is the inverse of the vacuous passes seen earlier in the project: a test wrong in the other direction, and just as misleading.
- **"Offer contract (force)" could not force anything.** It routed through `OfferContracts`, which rolls a 12% chance against a *fixed* seed, so for a given settlement and refresh it would deterministically fail forever no matter how many times it was clicked — the log's "try again" advice was impossible to act on. It now builds the offer directly.

---

## Phase 15 — Labor control feasibility prototype  (2026-07-26)

A mandatory spike (§33's own title). Deliverable is `docs/LABOR_TECHNICAL_NOTES.md`; no labor economy was built, per §33's closing instruction: "Do not build the full labor economy until the control model is proven."

Implemented:
- `Source/Intercolony/Debug/IntercolonyLaborSpike.cs` — generates a foreign pawn, snapshots its state, transfers it into the player faction, probes §33's questions, restores it, reports residue, and destroys the probe so nothing is left in the player's world.
- `docs/LABOR_TECHNICAL_NOTES.md` — **the deliverable**: chosen strategy, hooks required, known incompatibilities, restoration behaviour and unresolved risks, as §108 specifies.

Findings (full reasoning in the note):
- **Strategy A (temporary transfer into the player faction) chosen on evidence**, per §34's "choose based on experiments, not aesthetics". A foreign pawn has no `drafter`, `outfits`, `drugs`, `timetable`, `foodRestriction` or `playerSettings` — those are created only when the faction is the player's — and `Pawn.IsColonist` hard-requires it. Strategy B would mean Harmony-patching `IsColonist`, a property the whole game reads constantly, plus hand-building six trackers. Rejected as a permanent compatibility liability.
- **No Harmony patches are needed for control.** Once the faction is the player's, the vanilla systems simply work. That was the main open question and the answer is a clean yes.
- All ten programmatically checkable §33 control questions passed: selectable, work priorities settable and readable, workbench and bed eligibility, food policy, area assignment, drafting, combat records, caravan eligibility and return.
- **Ideoligion survives** both transfers intact.
- **One concrete restoration defect: `kindDef` is not restored.** `SetFaction` calls `ChangeKind(newFaction.def.basicMemberKind)` for humanlikes joining the player, and only *player* faction defs define `basicMemberKind` — so the rewrite is one-way. The probe subject went `Mercenary_Gunner -> Colonist` and stayed there. Any implementation must capture and reapply it; nothing errors if it is forgotten, the pawn is simply wrong forever.
- **Storyteller population adaptation is the sharpest unresolved risk.** `SetFaction` notifies `watcherPopAdaptation` of a `GainedColonist` and records a population increase, which feeds raid scaling, and nothing observed reverses it on departure. A labor system could make raids progressively harder per worker hired in a way no player would attribute to the mod. Found by reading the source, not by measurement — flagged as the first thing to test before shipping labor.

Not implemented:
- No employment contracts, payroll, hiring UI, worker pool or employer reputation. All of it waits on §33's instruction.
- No fix for the kindDef defect or the population-adaptation effect; both are recorded as requirements for the implementation phase rather than patched in a spike.

Known limitations — recorded as UNRESOLVED in the note rather than guessed at:
- Death, incapacitation and capture of an employee are untested.
- Save/load mid-employment is untested.
- Source faction turning hostile mid-contract is untested (§88 needs a deliberate policy).
- Social relations formed *during* employment are untested: the probe pawn had zero relations, so "unchanged 0 -> 0" is weak evidence.
- No pawn-control mod was loaded, so §33 q18 (mod assumptions) is unproven.
- The spike measures a single instant; whether an employee actually hauls, cooks and sleeps over days needs long-form observation.

Manual test:
- `dev.ps1 build` — 0 warnings, 0 errors.
- Spike run in game against a `Mercenary_Gunner` of a non-hostile outlander faction. Full transcript in the phase's log; verdict was "residue detected" naming the kindDef specifically, which is the correct outcome — the probe was built to catch exactly that.
- §108's acceptance question can be answered: yes, outside employees can behave like useful workers without corrupting faction or pawn state, provided `kindDef` is restored explicitly and the population-adaptation effect is resolved.

Bug found and fixed during the phase:
- The spike's first run selected employers by `faction.def.basicMemberKind != null` and found none, because **only player faction defs define that field** — the filter excluded every faction in the game. Fixed to use `Faction.RandomPawnKind()`. The same asymmetry turned out to be the root of the kindDef restoration defect, so the bug and the finding share a cause.

Bugs found and fixed during the phase:
- **Player short-changed by one silver.** An order advertised at 537 paid out 536. `TotalPayment` rounds while `PaymentFor` floors, so whenever `unitPrice x quantity` landed in `[n-0.5, n)` the quoted and paid totals disagreed. Flooring per delivery is deliberate — it stops a run of partial deliveries overpaying — so the fix pays the exact remainder on the delivery that *completes* the order. Regression test sweeps ~560 quantity/price combinations, delivering each in thirds, and fails unless instalments sum to the quoted total; a single hand-picked case would have missed it. Verified after the fix: an order advertised at 4,500 paid exactly 4,500.
- **`Spawn goods for open orders` debug helper had three faults**, reported as a red error in play. It called `ThingMaker.MakeThing` with no material, so RimWorld logged `madeFromStuff but stuff=null` and assigned a default. Worse, that default bore no relation to the order's `allowedStuff`, so the helper would spawn steel sculptures against a marble order, the delivery would correctly refuse them, and the matcher would look broken when it was doing its job — a convincing false bug report. It also spawned buildings *installed* rather than crated, forcing an uninstall before they could be caravanned. All three fixed; the helper now produces goods that genuinely satisfy the order line.
- No red errors in the in-game dev debug log window. All four §94 acceptance criteria pass.

## Phase 16 — Basic temporary labor  (2026-07-27)

§109's goal: "Hire one worker for a fixed period." Built on Phase 15's chosen control model —
Strategy A (transfer into the player faction) with the worker marked a **quest lodger**.

Implemented:
- `Labor/LaborCandidate` + `Labor/LaborCandidateService` — the hireable worker pool (§35.1).
  Wages are priced from the worker's best three skills (weighted up for passion), then adjusted
  for travel distance, the source settlement's labor supply, and a short-term premium so a
  short contract costs more per day (§36.1). Minimum term varies with settlement wealth.
  The pool is session state, never scribed: an unhired candidate's pawn is generated, so
  persisting it would either leak a world pawn or dangle on load.
- `Labor/EmploymentContract` — persisted fixed-term employment. **Save schema 13 -> 14**, with a
  migration step and ID validation.
- `Labor/EmploymentService` — hire, travel, arrival, work, expiry, departure. Wages are prepaid
  in full at hire (§37 "Prepaid"); recurring payroll and arrears are Phase 18 (§111).
- `Defs/QuestScriptDefs/Intercolony_Employment.xml` + `Core/IntercolonyQuestDefOf` — the marker
  quest script employment quests point `root` at. Never storyteller-selectable.
- Hourly advance on the world component's existing beat, alongside order deadlines and purchases.
- Debug actions: run labor self-test, list available workers, hire cheapest worker, arrive
  employees now, expire employment now, dump employments.
- `Debug/IntercolonyLaborSelfTest` — end-to-end through the real service, not a stand-in.

The control model, confirmed in play rather than by reading:
- The worker is transferred into the player faction, which is what makes them usable at all —
  work priorities, drafting, bed ownership and bills all gate on `Faction.IsPlayer`.
- Lodger status is what keeps that honest: `kindDef` is preserved (`SetFaction`'s `ChangeKind`
  is guarded by `!IsQuestLodger()`), the worker is skipped by `DefaultThreatPointsNow`, and
  `QuestPart_ExtraFaction.Notify_PawnKilled` restores their faction if they die.
- **Departure is entirely vanilla.** The quest carries a `QuestPart_Leave` with
  `leaveOnCleanup`, so ending the quest restores the faction, clears guest/master state, drops
  carried items and walks the worker off under a `LordJob_ExitMapBest`.
- No Harmony patch was needed for any of it.

Not implemented:
- No hiring UI. Hiring is dev-action only; §110 (Phase 17) is the labor market UI phase.
- No recurring payroll, arrears or termination escalation — Phase 18 (§111).
- No open-ended employment, renewal, or refusal to renew (§32, §36.4).
- No job-posting/applicant model (§35.2); only the available-worker pool.
- No compensation on death (§43), and no employer reputation effect from how workers are treated.

Known limitations:
- A source faction that turns hostile while the worker is travelling fails the contract at the
  gate and forfeits the wages. That is a placeholder, not the considered policy §88 asks for;
  Phase 18 should decide what actually happens. A faction that turns hostile *during* work is
  not handled at all yet.
- A worker downed at the moment of departure has their faction restored but stays on the map —
  `MakePawnsLeave` only lords up pawns that are spawned and not downed.
- Employment targets `destinationMap`, the map that paid. Multi-colony hiring is untested.
- Candidate listings do not survive a save; they regenerate against the current market refresh.
- Long-form behaviour (does the employee actually haul, cook and sleep over days?) is observed,
  not asserted.

Manual test:
- `Run labor self-test`: **32 passed, 0 failed.** Covers pool generation (20 workers), wage
  invariants across all 20, exact silver deduction, travelling state, world-pawn pinning,
  arrival, player faction, free colonist, lodger status, `kindDef` preserved
  (`Tribal_HeavyArcher` -> `Tribal_HeavyArcher`), home faction retained, work priorities,
  drafting, term clock starting at arrival, expiry, faction restored, no longer a colonist,
  `kindDef` intact after departure, walking off the map, no live references left on the record,
  and early dismissal of a traveller including unpinning from the world pawn pool.
- Hired a worker in play, pulled the arrival forward, **saved mid-employment, quit to menu and
  reloaded**: the employee came back Active, still a lodger, `kindDef` intact, term clock
  unchanged at 4 days remaining, `nextId` correct. This is §109's save/load criterion and the
  one thing the self-test cannot reach.
- Startup clean, no def errors, no red errors.

Bugs found and fixed during the phase:
- **`Quest.MakeRaw()` leaves `root` null, and `Quest.CleanupQuestParts` ends with
  `if (root.hideOnCleanup)`.** Every employment that ended would throw a
  `NullReferenceException` — that is, every dismissal. The Phase 15 spike hit this and reported
  it only as a bare `EXCEPTION during spike`. Fixed by shipping a marker `QuestScriptDef`.
- **Red error spam, ~20 lines per pool refresh.** `LaborCandidate.Discard()` called
  `RemoveAndDiscardPawnViaGC`, whose first step is `RemovePawn`, which `Log.Error`s when the
  pawn was never in `WorldPawns` — and a *listed* candidate never is. Guarded, with the
  destroy-then-discard path inlined for the unlisted case.
- **Every employment record froze `skills: no skills`.** `TryHire` read
  `candidate.SkillSummary()` in the object initialiser, after `candidate.Release()` had already
  nulled the pawn the summary reads from. The one field whose entire purpose is to outlive the
  worker's departure was being filled with the fallback string. Neither this nor the error spam
  was caught by the 28 assertions that passed on the first run — both were found by reading the
  log. The self-test now captures the expected summary before hiring and asserts equality.
- **Candidate pool had no ceiling: 48 pawns generated and discarded on every look**, the same
  shape of mistake as the §5.2 opportunity flood. Capped at 20 via a seeded settlement shuffle,
  and cached per market refresh so opening a listing no longer reshuffles who is hiring.
- **The early-dismissal check silently skipped for want of silver**, leaving the `KeepForever`
  unpin path unexercised — a vacuous skip of exactly the kind Phase 4 warned about. The test
  now budgets for both hires.

## Phase 16 addendum — playtest findings  (2026-07-29)

Matteo playtested Phase 16 and raised three points. One was a defect in the phase's own work,
one was a wrong answer I had recorded in Phase 15, and one was already the next phase.

**Employees could not be sent on caravans — and the Phase 15 spike said they could.**
§33 q9 asks "Can the pawn join a caravan?" The spike answered yes by testing
`pawn.IsFreeColonist`. The actual gate is `CaravanFormingUtility.AllSendablePawns`, whose
predicate includes `(!pawn.IsQuestLodger() || allowLodgers)`, and `Dialog_FormCaravan` passes
`allowLodgers: false` — vanilla deliberately keeps lodgers off caravans. So lodger status, which
buys `kindDef` preservation and threat-point exclusion for free, silently cost caravan work,
which §25.1 names as a real cost of self-delivery and §31 names as a thing hiring should buy.
Only playtesting caught it. `docs/LABOR_TECHNICAL_NOTES.md` now records the correction.

Fixed with the mod's only labor patch: a postfix on `CaravanFormingUtility.AllSendablePawns`
that re-runs the same method with `allowLodgers: true` behind a re-entry guard and keeps only
Intercolony employees. Vanilla's rules on downed, mental state, prisoners and lords therefore
still apply unchanged, no other mod's lodgers are affected, and the long vanilla predicate is
not duplicated where it could drift out of sync.

That exposed a follow-on: `LeaveQuestPartUtility.MakePawnLeave` handles a caravan member with
`caravan.RemovePawn`, which for an off-map pawn leaves them nowhere. An expired term is now
**held open** while the worker is unspawned; the player is told once, and the worker goes home
when they are back on a map. `EmploymentContract.termLapsedNotified` persists that (additive
field, safe default, no schema bump needed).

**No notification when employment ended.** Arrival sent a Letter; departure sent only a
transient corner Message, which vanishes and leaves nothing in the history. Departure now sends
a Letter for all three endings, naming the worker, their skills, the destination and the term.
Not mapped anywhere in DESIGN.md — it was an inconsistency inside Phase 16's own work.

**No employee list, contract durations or payroll view.** Already mapped: this is §110's build
list verbatim, including "current employee list", and its acceptance criterion is exactly the
complaint. Nothing changed; Phase 17 is next.

Also reverted on Matteo's call: the dismissal letter briefly stated that prepaid wages are not
refunded. Refunds do not exist anywhere in RimWorld or Intercolony, so naming one is what would
create the expectation. Cut; the reasoning stays as a code comment.

Manual test:
- `Run labor self-test`: **35 passed, 0 failed** — the previous 32 plus caravan eligibility
  asserted against `Dialog_FormCaravan.AllSendablePawns(map, reform: false)`, the off-map term
  hold, and its release once the worker is back on a map.
- Caravan loading confirmed in play.
- Startup clean, Harmony patches applied, no errors.

## Phase 17 — Labor market UI  (2026-07-29)

§110's goal: "Make hiring a proper gameplay loop." Acceptance: "A player can make a hiring
decision without dev tools or hidden information."

Implemented:
- New **Labor** tab (`UI/MainTabWindow_Intercolony_Labor.cs`), with a count badge for live
  employees. Split into its own file — the main window was already 75 KB of six tabs.
- **On the payroll** (top): every live contract with worker name, skills frozen at hire, source
  settlement and faction, wage, prepaid total, and status — travelling with an arrival
  countdown, working with days left, term-lapsed-while-away in yellow, under two days left in
  amber. Clicking a row jumps to and selects the worker. Per-row Dismiss, or Cancel before
  arrival, both behind a confirmation. Summary line gives headcount, combined silver/day and
  total prepaid.
- **Workers for hire** (bottom, §35.1): sortable on all six columns — worker, best skills,
  silver/day, minimum term, arrives in, source. Tooltip lists every skill with passion spelled
  out, plus distance and the pricing rule.
- **Hire dialog**: the shared `Dialog_ConfirmQuantity` with the term slider on the pop-up, where
  Matteo asked all commitment sliders to live. Opens at the worker's minimum term, reprices for
  the chosen term, and states the discount explicitly ("27/day instead of 31/day at their 8-day
  minimum") rather than leaving it to be inferred.
- `Dialog_ConfirmQuantity` gained `minQuantity` and a configurable field caption, so hiring uses
  the same commitment dialog as goods rather than a second near-identical one. "All"/"Half"
  become "Max"/"Min" only when there is a real floor, so the goods flows are unchanged.
- Tab bar rebuilt data-driven. It was seven hand-computed rects at a fixed 150px, positioned
  relative to each other in a different order than they appeared on screen; six already exceeded
  the 920px window, so a seventh could not fit. Adding a tab is now one array entry.
- Window grew to 1040x620 to hold seven tabs and two stacked tables.

Not implemented:
- No choice of wage structure — everything is prepaid in full. Phase 18 (§111), now explicitly
  named there after Matteo raised it.
- No long-term or open-ended employment, and no way to hire a recurring worker. Phase 22 (§115).
- No job-posting/applicant flow (§35.2) — that is Phase 21 (§114).
- No employer reputation shown, because there is none yet (§112).

Known limitations:
- The listing has no refresh button, deliberately: it is cached per market refresh, and a
  re-roll button would let the player drain and repopulate the pool until a great worker
  appeared. The tab states that hiring availability changes with the market.
- Candidate skills shown in the listing are the live pawn's; the employee list shows the summary
  frozen at hire. They can differ once a worker has been in the colony long enough to improve.
- Dismissing a traveller forfeits the wage. Stated in the confirmation and the tooltip; the
  actual early-termination policy is Phase 18's.

Manual test:
- Verified in play by Matteo: layout holds with nobody hired and with employees present, column
  sorting works, and hiring through the dialog reprices as the term changes.
- Startup clean, Harmony patches applied, no errors.

Decisions worth recording:
- The payroll summary says "prepaid", not "payroll/day". Wages are paid in full at hire, and
  calling it payroll would read as a recurring debit the player must keep covering — which is
  Phase 18's model, not this one. Wrong labels teach wrong mental models.
