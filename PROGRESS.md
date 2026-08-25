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

## Phase 18 — Payroll and arrears  (2026-07-29)

§111's goal: "Make employment economically binding." Acceptance: "Insufficient silver creates
understandable escalating consequences rather than crashes or silent deletion."

Raised by Matteo after playtesting Phase 17: paying everything up front is limiting. It was
mapped (§37, §38, §39) but §111's build list only said "payment schedule" — the *choice* of
structure was implicit, which is how it would have been missed. Now named explicitly there.

Implemented:
- **Three wage structures (§37)**, chosen at hire in `UI/Dialog_HireWorker.cs`:
  - Prepaid — whole term now, 10% cheaper. §37 gives prepaid a discounted total, and its stated
    risk is real: if the worker dies or the player changes their mind, the silver is spent.
  - Per quadrum — every 15 days worked. §37's "likely default for longer employment", and the
    dialog's default.
  - Daily — at the end of each day worked.
  All three are priced side by side against the term currently selected, because §111's criterion
  is that the trade-off is visible at the moment of hiring, not discovered later. Periodic hires
  take nothing up front, so a long contract is no longer a lump sum the player must first raise.
- **`Labor/PayrollService.cs`** — the pay beat on the world component's existing hourly tick. Pays
  what the colony has, records the shortfall as arrears, advances the clock from the *scheduled*
  time so a late payment does not drift the schedule, and settles pro rata for a partial final
  period so a dismissed worker is neither stiffed nor overpaid.
- **§39's escalation, in full:**
  1. first miss — warning letter, arrears recorded, work continues;
  2. second miss — the worker downs tools; their priorities are saved and zeroed;
  3. third miss — the worker walks out (new `EmploymentStatus.Quit`) and the unpaid wages become
     a `LaborDebt` that outlives the employment.
  Paying up at any point clears the arrears, resets the counter and restores the exact priorities
  they had rather than leaving the player to rebuild the work plan.
- **`Labor/LaborDebt.cs`** — persisted per settlement, keeping `originalAmount` so settling
  clears the balance without erasing the history. Phase 19 (§112) reads these.
- **Mood penalty as a situational thought** (`Intercolony_UnpaidWages` +
  `ThoughtWorker_UnpaidWages`), not a memory: being owed wages is a state, so it lifts the instant
  the debt is paid instead of lingering on a timer.
- **Labor tab**: arrears in red, per-employee "Pay N", a debt panel with "Settle N" for workers
  already gone, next-payday countdown, structure shown per contract, and a `Labor (!)` badge on
  the tab whenever anything is owed — §39's escalation is only playable if it is noticed without
  going looking.
- Debug actions: run payroll self-test, force payroll now, dump labor debts.
- **Save schema 14 -> 15.** Existing employments migrate to Prepaid, which is exactly what they
  were; `nextPaymentTick` stays -1 so no payroll is conjured for an already-paid worker.

Not implemented:
- No employer reputation effect from arrears, and no source-faction goodwill effect (§39 steps 7
  and 8). Phase 19 (§112) — the record exists for it to consume.
- No effect on future wages or applicant quality (§39 step 9). Also Phase 19.
- No open-ended employment or renewal (§36.4) — Phase 22 (§115), raised by Matteo and mapped.
- No death/injury compensation (§43) — Phase 20 (§113).

Known limitations:
- **Refusing work is not enforced against the player.** Priorities are zeroed once; if the player
  sets them back, the worker works. Deliberate — §39 makes refusal a warning stage on the way to
  the worker leaving, not a wall — but it is a soft consequence, not a hard one.
- Prepaid is never refunded, on dismissal or death. That is §37's stated risk rather than an
  omission, but it means an early dismissal of a prepaid worker is a total loss.
- Arrears are paid from the map that hired the worker; multi-colony payroll is untested.

Manual test:
- `Run payroll self-test`: **39 passed, 0 failed.** The test deliberately starves the colony,
  because §38's requirement — a shortfall creates arrears rather than blocking — cannot be proven
  with money in the bank. Walks warning -> tools down (13 work types zeroed, mood at stage 1) ->
  paid off (priorities restored, mood lifted immediately) -> starved again -> walk-out -> debt
  recorded -> debt settled. §37's cost invariants are checked across 960 wage/term combinations
  rather than one.
- `Run labor self-test`: **36 passed, 0 failed** — Phases 16 and 17 still intact.
- Verified in play by Matteo, including hiring on a daily wage, forcing payroll with no silver,
  and saving/reloading while in arrears.
- Startup clean, 7917 def nodes, no def errors.

Bugs found and fixed during the phase:
- **A Phase 16 assertion became wrong and was corrected rather than left to fail.** The labor
  self-test asserted `paidSilver == dailyWage * termDays`; with §37's prepay discount that is no
  longer the right expectation. It now checks `TotalCommitment` and separately asserts that
  prepaying really is cheaper than the gross rate.
- **18 warnings per pool refresh: "Tried to discard a world pawn X."** The previous fix guarded
  the *removal* but hand-rolled the disposal, and `Pawn.Destroy` ends with
  `if (!IsBeingDiscarded && !Contains) PassToWorld(this)` — so destroying an unregistered pawn
  adds it to `WorldPawns`, and the following `Discard` then refused and did nothing, leaving all
  18 pawns alive in the world pool. `WorldPawns.DiscardPawn` is correct only because it sets the
  being-discarded flag first, which is exactly what inlining omits. Both branches now go through
  vanilla. The same misunderstanding of this one method produced two different warnings across
  two phases, so it is written up in `docs/LABOR_TECHNICAL_NOTES.md` as "never hand-roll pawn
  disposal".
- **`EmploymentContract.ToString` read as a zero-value contract.** "(22/day x 20d = 0)" for a
  periodic hire, because `paidSilver` is legitimately 0 before the first payday. Now shows the
  structure, the total commitment, what has been paid and what is owed.

## Phase 19 — Employer reputation  (2026-07-29)

§112's goal: "Make treatment of workers affect future labor supply." Acceptance: "A bad employer
experiences meaningfully worse hiring conditions."

This is the missing tail of Phase 18's escalation. §39 lists nine steps; steps 1–6 shipped in
Phase 18, and steps 7–9 (reputation falls, source faction goodwill falls, future workers become
more expensive or unavailable) land here.

Implemented:
- **`Reputation/EmployerReputation.cs`** — one colony-wide score, 0–100, starting neutral, with
  the five tiers §40 implies ("Tier: Decent Employer") and §40's four on-screen counters:
  contracts completed, late payroll incidents, employee deaths, unpaid compensation. Plus walk-outs
  and early dismissals, which the escalation produces.
- **Colony-wide, unlike `CommercialReputation`, which is per settlement.** The asymmetry is
  deliberate. A trading record is bilateral — a settlement knows whether *it* was paid. How a
  colony treats the people who work there is not private between two parties; it is a reputation,
  and word gets around. §40 illustrates it as a single score for that reason.
- **Per-settlement grievance still exists where it belongs.** A settlement still owed wages sends
  nobody at all until the debt is settled, carried by the existing `LaborDebt` rather than by a
  second score. The specific grievance outranks the general standing.
- **`Reputation/EmployerReputationService.cs`** — event-driven throughout, per §40's "avoid
  expensive continuous calculations when event-driven updates are sufficient". Nothing is computed
  on a tick.
- **Effects, sized for §112's "meaningfully":**
  - wages ×1.25 at the bottom to ×0.85 at the top — a 40% spread;
  - labor on offer 35% to 115% of what a neutral employer sees;
  - candidate quality bias: at the extremes the generator draws twice and keeps the better or the
    worse worker. A neutral employer draws once, so the common case costs no extra pawn generation.
- **Faction goodwill (§39 step 8)** — the worker's own faction loses goodwill on a walk-out (−8) or
  a death on the job (−5).
- Conduct is recorded in `EmploymentService.End` rather than at each call site, so no future caller
  can end an employment without it counting. Negatives outweigh positives: a walk-out costs more
  than a completed contract earns, which is asserted rather than assumed.
- **Labor tab** shows tier, score and the wage effect beside "Workers for hire", with §40's screen
  and the full effect breakdown in the tooltip.
- Debug actions: run employer reputation self-test, dump employer standing.
- **Save schema 15 -> 16.** A colony with a labor history starts neutral rather than having a score
  reconstructed from past employments — §40 is a record of conduct, and inventing conduct that was
  never recorded would be inventing a past. Same call schema 10 -> 11 made for trading records.

Not implemented:
- No living-conditions or medical-treatment signals, though §40 lists them as positive. Measuring
  them is §41's subject and is not in §112's build list.
- Preventable vs unpreventable death is not distinguished — any employee death carries the same
  penalty. §40 says "preventable death"; telling the difference needs damage-source attribution.
- No effect on applicant quantity/quality in the job-posting sense (§35.2) — that flow is Phase 21
  (§114). What exists is the effect on the available-worker pool.
- No renewal willingness effect (§40 lists "voluntary renewal" as a positive) — renewal itself is
  Phase 22 (§115).

Known limitations:
- The quality bias is a best-of-two draw, not a distribution shift. It reads correctly in play but
  is a coarse instrument.
- Goodwill changes are applied without a custom `HistoryEventDef`, so the vanilla goodwill panel
  shows them without a labor-specific reason string.
- Reputation is not shown outside the Labor tab; the Relations tab still covers trading only.

Manual test:
- `Run employer reputation self-test`: **33 passed, 0 failed.** Effect curves checked at every
  point from 0 to 100 for monotonicity rather than at the endpoints. Every §40 signal driven
  through the real service. Same world priced twice, as a sought-after and as an exploitative
  employer: 20 workers versus 7, average best skill 11.5 versus 8.4. Score restored and test debts
  removed afterwards, so a dev check does not brand the colony.
- Startup clean, schema 16, no def errors.

Bugs found and fixed during the phase:
- **A new pricing input with a default value is a bug waiting to happen.** `DailyWage` first took
  employer standing as an optional parameter defaulting to neutral. It compiled clean, which was
  the problem: every existing call site would have priced at neutral while the listing showed a bad
  employer's premium, so the hire would charge a different number than it quoted. Same shape as the
  Phase 12 quantity slider and the Phase 10 gold bed, both of which were mispricings caused by an
  input that was easy to omit. The parameter is now required so the compiler names every site.
- **A self-test assertion that could pass or fail on luck.** "A bad employer pays more on average"
  compared two *different* candidate pools — a bad employer sees fewer, weaker workers, and a weak
  worker is individually cheap even at a premium. It passed on the first draw and would eventually
  have failed for no reason. Replaced with the invariant that cannot flake: the *same* pawn priced
  at 0, 50 and 100 must cost strictly more, then less. The cross-pool comparison is now reported as
  information, and the pool claim that does hold — average best skill — is asserted instead.
- **`git checkout` on a file to undo two lines discarded the whole schema-16 change.** Caught
  immediately and re-applied; recorded here because the mistake was mine and not the tool's.

## Phase 20 — Combat clauses and compensation  (2026-07-29)

§113's goal: "Prevent hired workers from becoming economically optimal disposable shields."
Acceptance: (a) "Using civilian workers aggressively in combat has meaningful cost." (b) "A source
faction turning hostile mid-contract produces a stated, understandable outcome for both the employee
and any booked trade obligations — never a silently voided obligation."

§113 bundles three things on purpose and all three shipped together: §42's combat clauses, §43's
death and injury compensation, and the whole of §88's hostility policy — trade half and labor half in
one file, which is the thing §113 is explicit about ("a policy split across two phases is how the
trade half and the labor half end up contradicting each other").

Implemented:
- **Three combat clauses (§42)**, priced as the largest multiplier in the wage formula: civilian x1,
  armed employee x1.5, security contractor x2.5. Chosen in the hiring dialog above the wage
  structure, so the structures below are priced against a rate the player has already settled. Each
  row shows the daily rate *and* what a death under it would cost, side by side — that pairing is the
  whole of §42's economics and it has to be visible before hiring, not after.
- **Armed and security differ by place, not just activity.** §42 gives an armed employee "colony
  defense", so drafting them on a player home map is within terms and marching them to someone
  else's map is not. Security has no restriction.
- **Breach detection with no Harmony patch.** `Pawn_MindState.lastAttackTargetTick` is stamped by
  `Verb` on every verb a pawn casts, melee or ranged (Verb.cs:485), and it is saved. Sampling it
  against `Pawn.Drafted` every 60 ticks answers exactly the question §42 poses — did the player point
  this worker at something — without touching combat, damage or the storyteller. Drafted is the whole
  test, and that is the design: §42 says self-defense is acceptable and aggressive use is not, and
  drafting is precisely that line.
- **Escalation reusing §39's shape** — warn, down tools, walk out — so it reads as a mechanic the
  player already knows. First breach: letter, reputation, goodwill. Second: refuses work. Third:
  leaves mid-term. A one-hour incident cooldown makes a firefight one breach rather than fifty.
- **A combat refusal cannot be bought off.** `WorkRefusalReason` exists because two escalations end
  in the same visible state and only one is about money; paying wages must not put a worker back to
  work who stopped because you drafted them.
- **Death and injury compensation (§43)**, at dailyWage x days-per-clause: 60 days civilian, 30 armed,
  12 security. A civilian at 40 silver/day is 2,400 — §43's own worked example, reproduced rather
  than invented. Permanent injuries pay a quarter of that each, capped at the death figure so a
  maimed worker is never dearer than a dead one. Snapshotted on arrival, so the colony pays only for
  harm it did.
- **The breach surcharge compounds rather than doubling once**, and that is a correction the self-test
  forced, not a flourish. A flat 2x passes at a 20-day term and *inverts* at 90 days: a security
  contractor's 2.5x wage eventually overtakes a fixed civilian payout, which would have made the
  meat-shield strategy correct on exactly the long contracts a player uses it on. Now 1+breaches,
  capped at 4x, and asserted across seven term lengths.
- **Compensation shortfalls become `LaborDebt`** of a new `Compensation` kind, feeding §40's
  "unpaid compensation" line — which existed since Phase 19 with nothing to fill it but wage arrears.
- **Death reputation is now clause-aware.** A security contractor's death costs 0.4x what a
  civilian's does, and any breach multiplies it. Expressed as one multiplier rather than three
  constants so the ordering is guaranteed by construction.
- **§88 labor half — safe passage.** An employee whose faction declares war has their contract ended,
  and then walks out **in no faction at all**. A factionless pawn is nobody's enemy, so turrets hold
  fire and colonists do not auto-engage — which is what makes "they will not be hostile until they
  are off the map" true rather than a promise. Their real faction is restored only once they are
  clear. Vanilla `LeaveQuestPartUtility.MakePawnsLeave` does all the housekeeping (master, guest
  status, carried things, faction restore); the one thing overridden is the faction, because vanilla
  correctly restores them to a faction that is now at war.
- **Safe passage has a two-day deadline and a price for missing it.** A worker still inside the colony
  when it lapses rejoins their own people, and the colony takes a death-sized reputation and goodwill
  hit for the detention. That is not decoration: once the record closes, killing the pawn costs
  nothing, so without it, walling a released worker in for two days would be strictly cheaper than
  letting them go.
- **§88 trade half.** Sales orders are cancelled and cost nothing (they pay on delivery, so nothing
  had changed hands) and are explicitly *not* a breach. Prepaid purchase orders end as a new
  `LostToWar` status with the silver forfeited and named in the letter — kept separate from
  `SupplierDefault` precisely because that one refunds. Recurring agreements are **suspended, not
  broken**, with the cycle clock pushed forward by the outage so every remaining delivery survives;
  they resume on their own if relations recover, and withdrawing while suspended costs no reputation.
- **The travelling-worker placeholder from Phase 16 is replaced.** The outcome is the same — they turn
  back and the prepaid wage is not recovered — but it is now a decision, with a letter that names the
  faction and the exact silver rather than a contract quietly failing at a gate.
- Schema 17, additive by construction: old contracts load as civilian with zero breaches, old debts
  as `Wages`. Nothing is reconstructed, so no past employment acquires a discount or a crime.
- Debug action **"Force war with an employee's faction"** (confirmation-gated; it permanently changes
  real relations) so safe passage can be watched rather than only asserted.

Not implemented:
- Security contractor as a **separate labor category** with its own applicant pool. §42 lists that as
  "may eventually be" — the clause is a contract term here, not a different kind of worker. Applicant
  flow is Phase 21 (§114).
- Capture and incapacitation of an employee (§33 q12, q13) are still untested and unhandled. A downed
  employee is treated as any other; a captured one is not modelled at all.
- Preventable vs unpreventable death is still not distinguished — carried over from Phase 19 and
  unchanged. What §42 *can* now tell is whether the player had been drafting them against the clause.
- No mood or thought effect on real colonists from an employee's death or from a clause breach.

Known limitations:
- **Drafted-and-attacking is a proxy, not a truth.** A player who undrafts before each shot would go
  unrecorded, and a worker drafted only to be moved out of danger who then fires once in self-defense
  is recorded as a breach. The 60-tick sample keeps the window tight, but the proxy is a proxy.
- **The escalation is not the player's cheapest way out.** A breached civilian who has downed tools is
  dead weight for the rest of the term and still paid, so the cheapest response to a second breach is
  to dismiss them — which costs less reputation than the walk-out would have.
- **A worker's bed stays claimed after they leave.** Pre-existing since Phase 16 and not introduced
  here: `Pawn.ExitMap` only unclaims ownership for prisoners and slaves, and `MakePawnLeave` does not
  either. Out of this phase's scope; recorded so it is not rediscovered as a Phase 20 regression.
- Suspension resumes on *any* recovery of relations, with no cooling-off period. A faction that
  flickers in and out of hostility would send paired suspend/resume letters.
- A faction with an empty relation table is a live hazard for vanilla, not just for this mod: any
  call reaching `GoodwillSituationManager` for it throws. Intercolony no longer triggers it and the
  self-test names the faction. The cause turned out to be Intercolony's own (see the stale-pool bug
  below), but the hardening stays: a save can arrive in that state for reasons this mod cannot see.

Manual test:
- `Run combat clause self-test`: **51 passed, 0 failed.** Clause pricing driven through the real
  wage formula on a live candidate (28 / 43 / 71 silver per day, x2.54 civilian to security). The
  §88 contradiction guard checked against all 94 settlements in the world: 69 at war, 0 of them still
  open for business. Every §88 transition driven through the real `HostilityPolicy` on
  really-constructed objects, including idempotence — the sweep runs hourly for as long as a war
  lasts, so re-applying must be a no-op. Employer standing and test debts restored afterwards.
- Startup clean, schema 17, no def errors.
- **Safe passage, played through end to end.** Hired a civilian (Dragon of Barxe Kinship, 48/day x
  5d), let them travel and arrive, then forced the war from the debug menu. The full chain ran with
  **no exceptions and no warnings anywhere in the session**:

  ```
  Hired      -> Travelling
  Arrived    -> Active
  War        -> Severed, factionless, walking out under safe passage
  Border     -> Safe passage complete, faction restored, quest ended, references cleared
  ```

  The last step is the one most likely to have broken: it restores the faction on an unspawned world
  pawn and then runs `MakePawnsLeave` a second time through `quest.End()`. No "tried to discard a
  world pawn", no unresolved-reference error on the way out.
- **Save/load, and the cross-game path that broke it.** Reproduced the exact sequence that had
  failed: quicktest world, quit to menu, new colony, open the Labor tab, hire, let them arrive, save,
  quit to menu, reload.

  ```
  State initialized fresh                                  quicktest world, pool built
  State initialized fresh                                  new colony
  Dropped 15 candidate(s) left over from a previous game    the fix firing
  Hired Vince from Devil's River                            a candidate of this world
  Arrived -> Active
  Loading game from file New Arrivals22
  State loaded (schema 17, nextId 2)
  ```

  Zero exceptions, zero warnings, zero unresolved references in the whole session, where the same
  path previously produced three duplicate-thingID errors and a continuous null-relation flood.
- **Death during safe passage, and §43 paying out in play.** Vince (civilian, 19 silver/day) was
  killed while walking out. The bill came to `19 x 60 = 1140` exactly, of which 800 was taken from
  storage and 340 booked as a `Compensation` debt, with the matching goodwill and reputation hits.
  That is the specific case the release letter warns about — *"if you kill them on the way out,
  compensation is owed exactly as if they had died working for you"* — and it is now the branch
  proven rather than the promise.

  ```
  Goodwill with Delrofenler -5: Vince died working for New Arrivals.
  Compensation (death) for Vince: 1140 owed, 800 paid, 340 outstanding.
  Safe passage complete: ... — Vince was killed leaving under safe passage
  ```
- **The safe-passage deadline.** On a second run the worker did not get clear, and after two days
  (two market refreshes, ticks 60000 and 120000) the expiry fired: the detention penalty landed and
  the pawn rejoined their own faction while still standing in the colony. That last part is the
  stated consequence, not a defect — the worker becomes an enemy on the map, which is what the
  letter says will happen.

  ```
  Goodwill with Delrofenler -6: Vince was held in New Arrivals past their release.
  Safe passage complete: ... — Vince was still in the colony when safe passage ran out
  ```
- **The `Severed` state across save/load.** An autosave containing a severed contract — a closed
  record still holding a live pawn reference, past its deadline — was loaded twice. Both times the
  reference resolved and the contract finished on the next hourly beat. Zero unresolved references
  and zero exceptions in the whole session. This was the riskiest state Phase 20 added.
- **Not measured:** why the worker failed to reach the map edge on the expiry run. The happy path
  works (an earlier worker walked out normally), so the exit lord functions; whether that run was
  blocked deliberately, downed, or pathing-stuck was not established.

Bugs found and fixed during the phase:
- **A failing test that was right to fail, about a claim that was wrong.** The first version of the
  shield-economics assertion claimed a drafted civilian is dearer than a security contractor at every
  term length. It failed at 120 days, and no choice of constants can fix it: the shield costs
  `w*T + C_civ` against `2.5*w*T + C_sec`, so a long enough term always favours the shield because
  compensation is fixed while both wage bills grow. Raising the surcharge moves the crossover, it
  cannot remove it. The criterion is now defended in two parts — money below the hiring cap, and
  the walk-out above it — and the test *locates* the crossover (100 days) rather than assuming it, so
  raising `MaxTermDays` fails loudly instead of quietly making the exploit correct. The tempting
  wrong move was to weaken the assertion until it passed; what it actually needed was for the claim
  to become true.
- **`HoldWork` recorded the refusal only as a side effect of succeeding.** It returned early when the
  pawn had no usable `workSettings`, *before* setting `refusingWork` and `refusalReason` — so a worker
  with no usable work types would get the "they have downed tools" letter while the contract still
  read as working normally, and the next payroll run would have "resumed" a refusal that was never
  recorded. The flags are a fact about the contract and are now set first; only the priority-saving
  depends on the pawn. Found by the self-test running the escalation against a pawnless contract,
  which is exactly the case that exposed it.
- **A flat 2x breach surcharge inverted the economics on long contracts** — see above. Now
  `1 + breaches`, capped at 4x.
- **`MaxTermDays` was a private UI constant that the balance depends on.** Moved to
  `LaborCandidateService` with a comment saying why, because two copies would let the hiring window
  offer a term the combat-clause balance was never checked against.
- **The safe-passage-expired letter promised something false.** It said killing the worker would still
  cost compensation, but the record is closed by then, so it costs nothing — which also meant walling
  a released worker in for two days was strictly cheaper than letting them go. Fixed at the source
  rather than in the wording: denying safe passage now carries its own death-sized reputation and
  goodwill penalty, and the letter says that instead.
- **`Faction.RelationWith` does not fail quietly, and that detonated the war debug action.** Clicking
  confirm threw a `NullReferenceException` inside RimWorld. The chain is worth recording because none
  of it is Intercolony's: `RelationWith` returns a **dummy `FactionRelation` whose `other` is null**
  when no relation exists, and `GoodwillToMakeHostile` walks `GoodwillWith` → `GetMaxGoodwill` →
  `GetSituations` → `Recalculate` → `CheckHostilityChanged` → `Notify_GoodwillSituationsChanged` →
  `CheckKindThresholds`, which calls `GoodwillWith(relation.other)` — null — and
  `GoodwillSituationManager.GetSituations(null)` returns null for `GetMaxGoodwill` to dereference.
  Any faction with an empty relation table detonates that whole path, and this test world has one:
  "The Breigua Treaty" reports a null relation with every faction including the player. The action
  now uses `Faction.SetRelation`, which *rebuilds* the entry on both sides instead of reading it,
  guards with `CanChangeGoodwillFor`, and catches — an exception escaping a `Dialog_MessageBox`
  callback becomes "Exception filling window" and repeats every frame until the dialog is closed.
  - Both sides get `baseGoodwill` set explicitly: `SetRelation` copies only `kind` to the mirror,
    leaving it at the default +100, which `CheckKindThresholds` would flip back to neutral within a
    thousand ticks. A dev tool whose effect silently undoes itself is worse than one that fails.
- **The mod turned that broken world into a wall of red errors.** `Faction.HostileTo` and
  `PlayerRelationKind` both call `RelationWith` with `allowNull: false`, which writes a `Log.Error`
  when the relation is missing — and the §88 sweep asks about every settlement every hour. 29 red
  errors in one short session, which is exactly the noise that hides a real one. The check now probes
  with `allowNull: true` first and answers "not at war", which is the truthful reading for a faction
  with no recorded relation.
  - Fixing it collapsed the duplication listed as a known limitation an hour earlier:
    `IntercolonyMarketAccess.IsAccessible` now **calls** `HostilityPolicy.IsAtWar` rather than
    repeating its two tests, so the market cannot keep trading with a faction the policy is ending
    contracts over. The self-test assertion that guarded the duplication is kept — it is what fails
    the day someone reintroduces the second copy — and a new one names any faction in the world whose
    player relation is missing, so this is reported rather than rediscovered.
- **The hire pool leaked across games, and I blamed the world for it first.** Found by the save/load
  test: reloading mid-safe-passage produced `Exception registering Verse.Pawn Bireamb ... unique
  thing ID 13720` for the employee and both pieces of their apparel. Reading the save showed a
  map-generation `Filth_RubbleRock13720` sharing that number — two different Things with one
  `thingIDNumber`, and no other duplicates anywhere in the file.

  `LaborCandidateService.pool` is **static**, so it lives as long as the process, while everything in
  it — pawns, `Faction` objects, thing IDs — belongs to one game. Quitting to the menu and starting
  another left the pool intact and pointing at a discarded world, and `poolRefreshCount` could not
  notice: a fresh game starts at refresh 0, which is exactly what the previous game's pool was keyed
  to. The proof is in the log: **"Barxe Kinship" is the employer faction in two different worlds**,
  either side of an `Initializing new game`. Randomly generated faction names do not repeat.

  So the worker hired in the new colony was generated in the old one, and brought with them a
  `Faction` unregistered in the new `FactionManager` — which is why it reported a null relation with
  *every* faction — and thing IDs from the old counter.

  **This is not a Phase 20 bug.** The static pool is Phase 16 code and every earlier session had it;
  the earlier "The Breigua Treaty" spam was the same leak one generation back. Phase 20 only made it
  visible, by adding faction-relation reads on an hourly beat and a debug action that pointed at the
  employer.

  The pool now records which world component it belongs to and is abandoned — not `Clear`ed, because
  `Discard` routes through the *current* game's `WorldPawns` — when that changes. `TryHire` refuses a
  candidate whose faction is not in `FactionManager` as a backstop, and the self-test asserts that no
  employment references a foreign faction.

  **The lesson is the one that cost the most time here:** I wrote "that is a world-generation
  artefact, not an Intercolony record" into this file and into the self-test's own output, on the
  strength of the faction looking broken in a way my code does not touch. It was mine. Static state
  that outlives a game is invisible precisely because every symptom appears somewhere else.
- **Appending to PROGRESS.md with PowerShell `Add-Content -Encoding utf8` double-encoded every
  non-ASCII character**, turning each em dash into `â€"` and each `§` into `Â§` throughout the Phase
  20 entry. Caught and repaired by re-decoding; recorded because the file is the record and a
  corrupted record is worse than a missing one. Write it with the file tools, not the shell.

## Phase 21 — Job postings and applicants  (2026-07-30)

§114's goal: "Turn labor into a two-sided market." Acceptance: "Higher wages and better employer
reputation measurably improve applicant quantity/quality."

Implemented:
- **Job postings (§35.2)**, the inversion of the existing listing: the player names the skill
  requirement, positions, term, wage, wage structure and combat clause, and the world decides whether
  that is enough. §35 wants both workflows, so the Hire listing is untouched.
- **One rule does the matching, and it is the existing pricing formula.** A worker applies if they
  meet the skill bar and the offered wage clears what they would have charged on the open market.
  §114's acceptance criterion falls out of that rather than being tuned: better workers ask more, so
  a higher offer clears more of them and better ones; and Phase 19's `WageFactor` already multiplies
  every ask by employer reputation, so a bad employer's offer clears fewer people with no separate
  mechanism. A purpose-built "attractiveness" model was the obvious approach and the wrong one —
  two models of what a worker is worth would drift, and the two tabs would quote different numbers
  for the same person.
- **A labor census, roughly 40x the advertised listing.** Up to 900 lightweight `LaborProspect`
  records, 30 per settlement, built lazily and only when a posting exists. A real pawn is generated
  only for the few who actually apply, then aligned to the record so the person who turns up is the
  person the market described.
- **Two-phase matching.** Every worker picks their preferred posting ignoring whether it is full,
  then each posting takes the *best* of the people who want it. Ignoring room is what makes ten
  identical postings behave like one; ranking is what keeps a generous offer worth making once the
  queue is full.
- **Standing orders**: a posting is re-examined against every market refresh until filled or lapsed,
  which is what makes it worth placing over watching the Hire tab.
- **A measured going-rate band** in the posting dialog — it asks the matcher's own question of the
  matcher's own census, so the numbers shown are the numbers that decide who applies. Shown as a band
  because the requirement genuinely spans one.
- **A posting that draws nobody says which of the two reasons it was** — the wage is below what
  anyone with that skill will take, or nobody reachable has the skill. Measured, not guessed.
- Labor tab split into **Hire | Posts | Employees** sub-tabs (§56), with an applicants badge on the
  main tab because applicants have a patience and will go home unanswered.
- Schema 18, additive.

Not implemented:
- **Housing and safety** as applicant factors, which §35.2 lists. They are §41's subject, still
  unmeasured since Phase 19, and Phase 22's renewal work wants them too — half-building a
  living-conditions model here would mean building it twice.
- Multiple requirements per posting (§35.2 shows one). Category or "any of" requirements are a later
  addition, not a gap.
- Counter-offers or negotiation. An applicant accepts the posted wage or does not apply.

Known limitations:
- **Two populations, one formula.** The Hire tab still generates real pawns and does not draw from
  the census, so its prices are a separate small sample rather than a slice of the band. Unifying
  them would be better architecture but means rewriting the working Phase 16-19 hire path.
- **A hired applicant reappears in the next cycle's census**, which regenerates from the seed. The
  Hire listing already behaves that way so it is at least consistent, but the world does not remember
  who it has already sent.
- The applicant queue is capped at positions + 3. Above that a better offer buys better applicants
  rather than more, which is intended — but it does mean the player never sees how deep their reach
  actually was.
- Census skill distribution is modelled rather than sampled from `PawnGenerator`, so it approximates
  what real pawn generation produces rather than matching it. The self-test asserts a spread and that
  both representations price through one formula, which catches drift but not calibration.

Manual test:
- `Run job posting self-test`: **25 passed, 0 failed.** Census of 594 workers across 30 settlements,
  39.6x the advertised listing, skill values spanning 13.0 to 58.9. Interest across the going-rate
  band `1 2 4 18 32 45 67 83 94 105 110 118 119 119 120 122` — biggest single step 18% of the total.
  Reputation moved the same offer from 115 workers to 744. Five identical postings drew 9 between
  them, all on one posting. No world pawns leaked.
- Startup clean, schema 18, no def errors.

Bugs found and fixed during the phase:
- **The pool was too shallow to be a market, found in play.** Moving a posted wage one silver took a
  posting from nobody interested to every qualified worker, because there were three of them and they
  all charged about the same. Fixed by the census — depth without pawn generation, since the matcher
  never needed pawns, only "can they do the work" and "what do they charge".
- **The matcher took the first N workers in census order, not the best.** So above the queue cap a
  generous offer bought nothing at all: 145 workers qualified and the player got the same arbitrary 9
  whether they offered 32 or 68. The deep census was working and the last step threw the signal away.
  Fixed by ranking each posting's interested workers by the advertised skill.
- **Identical postings were spilling into each other.** Because room was checked while choosing, a
  full posting pushed workers onto the next identical notice — so five postings collected five queues
  and advertising the same job repeatedly multiplied the labor supply out of nothing. Fixed by
  deciding preference before considering room.
- **Three self-test assertions were measuring the queue, not the market.** The queue is capped by
  design, so it saturated a third of the way up the band and reported every offer above that as
  identical — hiding the smoothness the census exists to create. Quantity and reputation are now
  measured as reach, quality on the ranked queue. The lesson is the recurring one: assert against the
  thing the design is about, not the nearest number to hand.

## Phase 22 — Long-term employment  (2026-07-30)

§115's goal: "Support stable recurring workforces." Acceptance: employees can remain for long
periods without faction-state drift or save corruption; and neither employment nor supply
agreements end by silently lapsing.

**A correction to §115's own premise, found before building.** A RimWorld year is 60 days
(`GenDate.DaysPerYear`) and `MaxTermDays` was already 60 — so §36.3's "long fixed-term contract:
one year" was already reachable, and the balance warning Phase 20 left applied to a narrower case
than it sounded. Only §36.4's open-ended employment breaks it, because a contract with no term has
no term for compensation to scale against.

Implemented:
- **Open-ended contracts (§36.4).** `termDays` 0, no expiry tick, ended only by rule. Priced as the
  longest engagement rather than a zero-day one — passing 0 into the wage formula would have hit
  §36.1's short-term premium and made permanent employment the *dearest* per day, which is
  backwards. Prepaid is refused rather than silently converted: there is no term to pay for.
- **Tenure-scaled severance (§43, §115), which is the structural fix Phase 20's dated warning
  demanded.** Compensation was a fixed number of days' wage while both wage bills grow with the
  term, so past 99 days a drafted civilian became the cheap way to field a fighter. §36.4 removes
  the cap rather than raising it, so no constant could fix it. Severance now accrues per day served
  — 0.6 days of wage for a civilian against 0.1 for a security contractor — so the gap widens with
  tenure instead of being outrun by wages. Uncapped on purpose: a cap would restore the same problem
  further out.
- **Notice periods (§36.4).** Growing with service, 3 to 20 days, settled by working them out or
  paying in lieu at exactly the same cost — the choice is whether the colony wants the labour or the
  silver. Skipping notice entirely is deliberately available and is remembered against §40. Without
  a cost, open-ended employment would be strictly better for the player than any fixed term, and
  §36.2 and §36.3 would be dead options.
- **Renewal, one mechanism for both contract kinds (§115, §107).** The counterparty offers and the
  player answers; whether an offer comes at all depends on the player's record. A worker who was
  paid late, is still owed arrears, or was drafted against their clause does not ask to stay — and
  the letter says which. Renewal a player could simply buy would have made §40's reputation
  decorative.
- **Supply-agreement renewal**, the half §107 listed and Phase 14 never built. Same shape, gated on
  commercial reputation and a clean delivery run instead of employer standing. Offers expire and say
  so.
- **Voluntary non-renewal** on both sides, distinct from dismissal: the worker serves out the term
  they agreed to and goes home on time.
- Accepting a renewal extends the same employment in place — no departure, no second arrival, no
  faction round trip. The safest way not to drift across a renewal is not to touch the pawn at all.
- Schema 19, additive. `arrivedTick` replaces "endTick is still -1" as the test for whether a worker
  ever started, which stops being true once contracts have no end.

Not implemented:
- **Worker-initiated resignation.** §36.4 says "either side terminates", and only the colony's side
  has rules. A worker still leaves over unpaid wages (§39) or combat misuse (§42), but nothing makes
  a well-treated long-term employee decide to go home on their own.
- **Living conditions as a renewal input.** §41 lists housing and safety, and §40 lists them as
  reputation signals. Still unmeasured since Phase 19; renewal reads conduct the code already
  records rather than adding a half-built model.
- Renegotiating terms at renewal — the worker names one wage and the answer is yes or no. No
  counter-offer.
- Long fixed terms beyond a year. The cap stays at 60 days because a year is 60 days; open-ended
  covers everything longer.

Known limitations:
- **The long-run stability claim is not proven.** §115's first acceptance criterion is about
  hundreds of days of play, which no self-test settles — what is proven is that the arithmetic
  governing long engagements does not break, out to five in-game years. Whether a quest lodger
  survives that long in practice is still the open question the technical notes have carried since
  Phase 15.
- Severance is uncapped, so a worker kept for many years becomes very expensive to lose. That is
  intended, but it has not been played.
- A renewal offer is raised once per term. Declining it and changing your mind before the term ends
  is not possible.
- The notice period does not interact with §88's safe passage: a war during a notice period severs
  the contract and the notice simply stops mattering.

Manual test:
- `Run long-term employment self-test`: **30 passed, 0 failed.** The shield economics hold at every
  tenure from 5 to 300 days, tightest at 300 days and still 1.19x — where Phase 20 recorded a
  crossover at 99. At 100 days specifically: shield 18,400 against contractor 12,200, where the two
  were exactly equal before. Notice grows 3 days at one week served to 20 at 180 days. Every refusal
  to renew carries a stated reason.
- Startup clean, schema 19, no def errors.

Play-tested (2026-08-03), all three outstanding items:
- **Open-ended dismissal** — three options present and correct, 3-day minimum notice, pay-in-lieu
  arithmetic verified at 3 x 23 = 69, letter clear.
- **Renewal, both halves** — treated well, the worker asked to stay at 26/day against 25, and
  renewing extended the same employment in place. Treated badly (one drafted civilian), no offer came
  and the refusal named exactly which thing caused it. That second half is the one worth having: it
  proves §40's record is load-bearing rather than decorative.
- **Supply agreement renewal** — offer created, accepted, agreement credited to completion, another
  run offered and answerable in the Contracts tab.

Bugs found and fixed during the phase:
- **A supply agreement could be credited as delivered and never complete.** Found by the play-test:
  the log read `Ellis completed (4 cycle(s) credited). No renewal offered:` with an empty reason, then
  three further runs crediting nothing. Completion lived inline in `ResolveCycle`, which only runs
  when an *order* resolves — so crediting cycles directly could never reach it, the status stayed
  Active with nothing left to deliver, and the empty reason was `outcomeNote`, which nothing had
  written. Completion is now its own method and the only route to `Completed`, and
  `AdvanceContracts` rescues any agreement stranded with no cycles and no order in flight. Normal
  play could not reach that state, which is exactly why it went unnoticed.
- **The employee row printed two sentinels raw.** `termDays = 0` and `DaysRemaining = float.MaxValue`
  are how §36.4 says "open-ended", and the row rendered them as
  `23/day × 0d daily ... 34028230000000000000000000000000000000d left`. The contract now has
  `TermLabel` and `RemainingLabel` and the display cannot reach the raw fields. Third occurrence of
  this class in the project; the first two were silent.
- **A worker under notice looked exactly like one who was not.** `StatusLine` had no case for it,
  despite the notice period being the whole of §36.4's dismissal rules.
- **A sign test used as a sentinel switched off three features at once, silently.** `TenureDays`
  guarded with `arrivedTick < 0`, treating any negative tick as "never arrived". That is only true
  because a live game's tick is positive — anything constructing a contract with a backdated start
  lands on a negative tick meaning the opposite. The result read as tenure zero forever, which
  disabled severance, notice growth *and* renewal eligibility together, with nothing throwing. Now
  compared against an explicit `NotArrived` constant. Six of the self-test's failures were this one
  cause.
- **Open-ended hires were rejected by the minimum-term check**, since 0 is below every candidate's
  minimum — and would have been priced with §36.1's short-term premium if they had got past it. Both
  found by reading the hire path rather than by the test, which never reached it.

## Phase 23 — Employee-to-colonist transition  (2026-07-30)

§116's goal: "Add late-game narrative conversion." Acceptance: "Conversion is rare/meaningful and
cannot be exploited as cheap recruitment."

**Status: verified.** Self-test 21/21, and all three of §44's routes played through — including the
conversion itself, which was the one thing here that could have been quietly wrong.

Implemented:
- **All five of §44's outcomes.** A worker who has served two quadrums with a spotless record asks
  to stay for good; the player pays the release fee, negotiates it down with Social, keeps them
  without paying (defection), or declines. The worker asks — the same direction as §115's renewal,
  because attachment is renewal's larger sibling and the player earning the offer is the point.
- **Two quadrums as the eligibility bar**, chosen over a full year: long enough that both sides have
  committed, short enough that a player running the labour system well actually sees it, with the
  fee rather than the wait doing the work of keeping it out of reach.
- **The release fee is 180 days of the worker's own wage**, which already encodes their skills,
  passions, distance and the colony's reputation from Phases 16–19. So the fee tracks all of it for
  free, and the workers most worth keeping are exactly the ones hardest to afford. A flat fee would
  have made this a shop — cheap for precisely the people a player would want to exploit it on.
  Modified by the source faction's goodwill: a faction that likes you parts with a citizen more
  easily.
- **Social negotiation cuts the fee**, up to 35% at Social 20, and is capped for the same reason the
  fee is scaled: talking can make it cheaper, never cheap. The dialog shows the asking price and the
  negotiated one together, so having a good negotiator is visibly worth something.
- **Defection is available and priced in diplomacy** (§44 "pawn defects, causing diplomacy
  consequences"). Keeping someone without settling costs 80 goodwill — enough to turn a neutral
  faction hostile — at which point §88's policy takes over the wreckage on the same beat, so a
  player who just started a war finds out immediately what it cost them.
- **Declining is not an ending.** They keep working under the contract they have and may ask again
  after 30 days.
- Progress towards attachment is shown in the employee tooltip. §116 wants this rare, which makes it
  worth showing how far off it is — a rare outcome nobody can see approaching is indistinguishable
  from one that does not exist.
- Schema 20, additive.

The technical part — turning a quest lodger into a colonist in place:
- The worker is already in the player faction; that is how they work at all. Joining is therefore
  not a faction change, it is the **removal of lodger status**, and lodger status is the quest.
- Ending the quest normally would send them home, because `QuestPart_Leave` carries
  `leaveOnCleanup` — that is how every other departure in this mod works. So the pawn is removed
  from that part's list first, and only then is the quest ended.
- `QuestPart_ExtraFaction.Cleanup` is safe to run: it only sets a relations-gain cooldown.
- Once the quest is no longer `Ongoing`, `QuestUtility.IsQuestLodger` goes false because it resolves
  through `HasExtraHomeFaction`. The pawn is then a colonist by every test the game applies — threat
  points count them, caravans take them, nothing holds a claim.
- Deliberately **not** routed through `EmploymentService.End`, which restores the original `kindDef`
  and sends the worker home. Both correct for a departure; both wrong here.

Not implemented:
- **The worker cannot refuse.** §44's "source faction agrees" is modelled as a price rather than a
  decision — pay it and they are released. A faction that simply says no was the alternative and was
  rejected: a refusal after the player has committed to a plan is frustrating in a way a high price
  is not.
- No counter-offer or haggling round. Negotiation is one number, computed from the best available
  negotiator.
- Nothing distinguishes *which* colonist negotiated beyond their Social level — no trait, gear or
  ideology effects, though `TradePriceImprovement` would be the vanilla stat to use if this is ever
  deepened.
- No mood or thought effect on the new colonist, or on the colony, from a conversion or a defection.

Known limitations:
- **The conversion mechanism is the riskiest code in the phase and has never been run.** If the pawn
  is not removed from `QuestPart_Leave.pawns` before the quest ends, the brand-new colonist walks off
  the map. The self-test deliberately does not exercise it — joining someone permanently to the
  colony is not a side effect a dev check should have — so this is a play-test, not an assertion.
- A defection that turns the faction hostile while the pawn is mid-conversion has not been reasoned
  through against §88's safe passage. The contract is closed before the goodwill hit lands, so the
  sweep should find nothing to release, but that ordering is unproven.
- The fee ignores the worker's actual skills except through their wage. Two workers on the same wage
  cost the same to keep even if one is far more useful to this particular colony.
- Eligibility is checked on the hourly beat, so an offer can arrive up to an hour after the moment it
  describes. Invisible at this scale, but it is why the letter says "has worked here N days" rather
  than naming a threshold.

Bugs found and fixed during the phase:
- **A button that did nothing, and had been dead since Phase 18.** The employee row reserved text
  width for *one* action button (`rect.width - actionWidth - 12f`) while several row states draw two,
  and the row's click-to-jump `ButtonInvisible` spans that text width. It is drawn first, so it took
  the mouse-up for anything underneath — leaving 106 of the left-hand button's 110 pixels dead. The
  right-hand button sat clear and worked perfectly, which made it look like one broken button rather
  than a broken layout.

  Nothing threw and nothing logged; the only visible trace was the combat-clause label clipping
  behind the button, which is the same over-wide text width showing itself somewhere it could be
  seen. Found from a play-test report that measured the working button was 4px away and used the
  identical input, noted the debug log was empty, and mentioned the clipped label as an aside.

  Phase 23 did not introduce it — `Pay {arrears}` has been dead the same way since Phase 18, unhit
  because paying arrears mid-term is rare and that button was never play-tested. Phase 23 merely put
  a button people would actually click into the dead zone. Fixed structurally: an
  `EmployeeRowLayout` computes the text width *from* the button positions, and every button on the
  row is routed through it, so a future two-button state cannot reintroduce it.
- **The paid route's letter read as an unqualified success while relations quietly dropped 6.** The
  goodwill cost is intended — a faction is a citizen short either way — but a letter that does not
  mention it is the kind of small dishonesty that makes a player stop trusting the other letters.
  Now named in the text. Caught by a play-test noting the discrepancy rather than assuming it was a
  bug.

Manual test:
- `Run transition self-test`: **21 passed, 0 failed** (2026-07-31, run in a live save at schema 20,
  no red errors). Every eligibility gate driven separately and each reporting what is missing; the
  fee scaling proven on the comparison §116 is really about — 14,400 to keep an 80/day worker against
  4,800 for a year of employing them; negotiation capped at 65% of asking; defection costing more
  (-20) than settling gains (+10).
- **All three of §44's routes played through** (2026-07-31), on a fresh world, no red errors anywhere:

  | Route | Result |
  |---|---|
  | Pay the fee | colonist in place; 3,583 debited exactly as quoted |
  | Keep without paying | colonist in place; faction hostile at -80; bookings voided; nothing paid |
  | Not now | stays an employee |

  The negotiator display read well in play — asking price, negotiated price, who negotiated it, the
  saving and current silver all in one place (`Hani asks 4536... Tess (Social 12) can talk them down
  to 3583 — a saving of 953`).
- **The conversion is proven, including the failure it was written to catch.** `Verify converted
  employees` reported *spawned on Colony, faction New Arrivals, quest lodger False, IsColonist True,
  still an employee False, kindDef Colonist, drafter present* on both the paid and defected routes.
  Then, because the doc warns a pawn handed an exit order looks normal for a while before leaving,
  the game was run at 3x for four in-game hours and re-verified: identical PASS, no drift toward the
  map edge. Removing the pawn from `QuestPart_Leave` before ending the quest does what it was meant
  to.
- The offer state survived a save/load round trip — the play-test reloaded the same save point before
  each route and the pending offer came back intact each time.
- Two debug actions added afterwards to make that play-test practical rather than a thirty-day wait:
  **Force attachment offer** backdates an active employment past the tenure bar (backdating
  `arrivedTick` rather than setting a flag, so severance, notice and the gates all price the worker
  as genuinely long-serving), and **Verify converted employees** checks faction, lodger status,
  `IsColonist` and whether the pawn is still on a map at all — which is the failure worth catching,
  and one that eyeballing would miss if the pawn simply wandered off later.

## Phase 24 — Economic integration and dashboard  (2026-08-03)

§117's goal: "Help the player understand the business without turning the mod into accounting
software." §45 calls it "the heart of the finished product".

Implemented:
- **§75's transaction ledger, which had to come first.** Every cash figure already existed as a
  cumulative total on an entity — `SalesOrder.paidSilver`, `EmploymentContract.compensationPaid` —
  which answers "how much in total" and cannot answer "how much last quadrum". §117's whole screen is
  the second question. Seven movement kinds, recorded at every point silver actually moves: sales,
  purchases, wages (prepaid, periodic, arrears, final settlement, notice in lieu), compensation,
  release fees, refunds, debt settlements.
- **History starts now, and says so.** An old save's totals know how much but not when, and
  reconstructing dates would be fiction presented as a record. A twelve-day-old colony reading "last
  quadrum: +180" is not reporting a quiet quadrum, so the report names how far back it actually goes.
- **A Business tab**, leftmost and the default. Two short blocks and a list rather than a
  spreadsheet — §117's brief is half warning. Every figure sits next to the thing that makes it a
  decision: silver next to how many days of payroll it covers, contract revenue next to what buying
  the goods instead would cost, the delivery premium next to the hauling that earned it.
- **§45's contract estimate**, with inputs priced as "what buying them instead would cost" through
  procurement's own supplier margin — now a named constant rather than a literal, so the dashboard
  cannot recommend buying at a price procurement would not offer. The mod cannot see what a player's
  rice costs to grow, and any number invented for that would be fiction with a decimal point.
- **The transport line is the delivery premium**, which is the only honest cash answer available:
  caravans cost time and risk, not silver, but seller-delivery is priced above buyer-pickup and that
  gap is what hauling earns. A quadrum of pickup orders shows nothing there, correctly.
- Ledger pruned to a rolling year on the daily refresh, with a hard entry ceiling as a backstop.
- Schema 21, additive.

Not implemented:
- No graphs or trend lines. §117 shows a table; anything more is the accounting software it warns
  against.
- No per-contract payroll apportionment. The mod does not know who works on what, and a made-up
  allocation would look precise while being invented. The wage bill is shown whole and labelled as
  such.
- No real caravan cost model — food consumed, time spent, risk. Considered and rejected as a system
  of its own for the sake of one line.
- Nothing spends the ledger's history: no "compare to last quadrum", no alerts.

Known limitations:
- **The going-rate band and the applicants who arrive are two samples of one formula**, so they do
  not match exactly. Measured in play at 8 workers/125 silver against 4/118 — noticeable, judged
  acceptable. Unifying them means rewriting the Phase 16–19 hire path.
- The estimate assumes the whole wage bill is chargeable against each agreement, so a colony with
  several contracts sees the same payroll subtracted from each. Correct as "does this cover the
  workforce", misleading if read as "this contract's share".
- Partial-period reporting is a sentence, not a scaled figure. The report does not pro-rate.

Manual test:
- `Run ledger self-test`: **23 passed, 0 failed** after the fixes below. The load-bearing assertion
  is not that the arithmetic adds up but that the ledger agrees with the colony's *real* silver — a
  real payment through the real service, storage measured either side. Verified at -300 recorded
  against storage falling by 300, in both magnitude and sign.
- **Business tab reviewed in play**, empty and populated. Read as intended: "a summary, not
  accounting software... empty states are written as instructions rather than rows of zeroes". The
  runway line ("covered for about 6 more days at the current rate") was judged the best line on the
  screen and the one to protect if it ever gets crowded — placed under the two numbers it derives
  from, hedged, and coloured apart from the facts above it.
- **Not yet seen:** the report with revenue, purchases and payroll all present at once. That is where
  crowding would actually show.

Bugs found and fixed during the phase:
- **The sentinel mistake, for the third time — and this one reached the screen.** `ledgerStartTick`
  used `< 0` to mean "no history". A test backdating it 200 days on a young map lands about twelve
  million below zero, which reads as "no history" and forces the period to report as partial with
  zero days covered. So one assertion could not pass, and *the one above it was passing while
  measuring nothing* — which the play-test correctly suspected before withdrawing its specific
  hypothesis.

  Separately, §36.4's open-ended contracts use `termDays = 0` and `DaysRemaining = float.MaxValue` as
  sentinels, and the employee row printed both raw:
  `23/day × 0d daily ... 34028230000000000000000000000000000000d left`. Fixed by giving the contract
  `TermLabel` and `RemainingLabel` and never formatting the raw fields — the display cannot reach the
  sentinel any more.

  Three occurrences now: `arrivedTick < 0`, `ledgerStartTick < 0`, and formatting `DaysRemaining`.
  The first two were silent. The reasoning is written into `LedgerService.NoHistory` and
  `EmploymentContract.TermLabel` so a fourth has something to run into.
- **A scroll view that scrolled with nothing to scroll.** The Business tab's content height came from
  a formula — so many pixels per block, so many per contract — and the formula was wrong, handing the
  view a viewport taller than its content: the page scrolled into blank space and the thumb sat at
  the bottom of a track it had no reason to fill. It now measures, since every draw method already
  returned its final y, and clamps to the panel height so content that fits gets no scrollbar.
- **A worker under notice looked exactly like one who was not.** `StatusLine` had no case for it
  despite the notice period being the whole of §36.4's dismissal rules.
- **Selection was invisible on the posting dialog's clause and payment rows.**
  `Widgets.ButtonText` paints its own background, so a `DrawHighlightSelected` drawn *before* it is
  simply covered up. The hire dialog looked right because it highlights a plain row rather than a
  button. Now drawn after, via one shared helper.
- Two hint strings clipped at the posting dialog's right edge; shortened and given room.
- "Only 1 days of history" — plural.

Tooling added afterwards:
- **`Force renewal offer` and `Force supply agreement to complete`.** Four of the six remaining
  play-tests were blocked on the same gap: nothing could fast-forward a contract, so renewal and
  supply renewal needed sitting through real in-game weeks. A feature that can only be tested by
  waiting is a feature that does not get tested. Both move the clock rather than setting a flag, so
  the eligibility rules weigh the record they actually find — including refusing when the record is
  bad, which is the half worth checking.

## Phase 25 — Polish and compatibility, pass A of three  (2026-08-06)

**This entry is deliberately partial.** §118 is eleven tasks plus a decide-or-delete, and was cut
into three passes rather than built in one. Pass A took the items that stop *other people's* games
breaking. Pass B (settings, tooltip polish, UI scaling) and pass C (DLC matrix, modded-content
tests, compatibility notes, documentation) are not started. The phase is not complete and this
entry does not claim it is.

Implemented:
- **A crash found in play, fixed.** Taking on the top of two applicants threw
  `ArgumentOutOfRangeException` out of the draw pass: `DrawPostingBlock` iterates `Applicants`
  descending while `TryAccept`, closing a filled posting, clears that same list mid-loop. Phase 21
  play-tested this screen and missed it, because it only ever took the *bottom* row — the one
  arrangement that cannot fail, being the last iteration. Hire, turn away and withdraw now record
  intent and run after the loop and after the scroll view closes.
- **§125's used-goods question decided: kept, as a quality floor.** The question posed a false
  choice. `minHitPointsPercent` is a *minimum*, so making it real never required a secondhand
  market — only buyers who will not accept a nearly-broken chair. One demand in five on durable
  finished goods now carries a floor of 60/75/85%, never 100%. Enforced since Phase 6, generated
  since now.
- **Schema 22, and the migration chain put back in ascending order.** It ran 2→13 then 22→14. That
  is harmless while every step from 14 on is a bare log line, but the "falls through to the next"
  contract was false for half the chain, and the first migration that actually moves data would
  have run out of order silently. Reordered while every step is still a no-op.
- **A draw-time exception can no longer flood the log or strand the player.** The tab selector
  draws outside the guard, so a broken page can be navigated away from; the failure is logged once
  with its stack, keyed on page plus exception plus stack; scroll views and text/colour state are
  restored. A debug action deliberately breaks a page so the guard can be watched working.
- **Performance profiled at real scale**, with a `Run performance profile` debug action reporting
  cold and warm separately — a warm figure alone hides what the player pays on first hit.
- Three per-frame costs removed, the significant one being dynamic tooltips building their full
  strings for every rendered row every frame regardless of hover. `TooltipHandler` applies that
  gate only *after* receiving the text.
- Colony order validation switched from scanning every thing on the map to RimWorld's per-def
  index, covering minified furniture explicitly.

Not implemented:
- **Localization: dropped, not deferred.** The mod is English-only and §118 has been amended to say
  so. Its text is composed prose built at runtime from fragments; keying it well means rewriting
  how those sentences are assembled.
- Passes B and C in full.
- A true secondhand market. Undesigned and unbuilt; it would need its own pricing story.
- Caching for the six pages that rebuild and sort temporary lists every draw, and for Business's
  per-draw ledger aggregates. These need lifecycle-aware invalidation and were measured as not
  currently worth it.

Known limitations:
- **The guard's repeat suppression is bounded by player action, not by frames.** A failed page is
  latched and never redrawn, so there is no per-frame spam. But closing and reopening the window
  clears the latch, and a persistently broken page logs again — with a placeholder trace, because
  Harmony renders full frames only on first access. The first report after a log clear is the one
  with the usable stack.
- The condition floor is not generated on supply agreements. Their generator sets neither quality
  nor material either, and a constraint that repeats every cycle is a different proposition.
- The seller-delivery/caravan condition refusal has never been seen. It shares the validator with
  buyer pickup but has its own gizmo.

Manual test:
- **The crash**, retested with a two-applicant posting: hired clean, no exception in the session.
- **The condition floor**, both halves: an order generated as `2x Psychic insanity lance (60%+
  cond)`, and delivery refused with `2 offered below the condition floor (25% offered; 60%
  required)` via the buyer-pickup path.
- **Save migration**, better than asked for: a **schema 17** save walked five steps to 22 in one
  load — job postings, open-ended employment, transition, ledger, condition floors — with no errors.
- **The guard**, via the deliberate-failure action: fallback message shown, other tabs navigable,
  one log line across many frames, and after the fix a real stack trace naming `DrawBusiness`,
  `DrawPage` and `DrawPageGuarded` with offsets.
- **Performance, on the real save** (252 settlements, 900 workers, 25,416 map things): full daily
  refresh **3.1 ms**, market generation 0.9 ms, classification 6.5 ms cold and ~0 warm, settlement
  profiles 0.94 ms cold and 0.03 ms warm, labor census 5.6 ms cold and ~0 warm.
- **Not yet verified:** that the indexed validator is actually faster than the 1.777 ms it
  replaced, and that minified furniture is still found by it.

Bugs found and fixed during the pass:
- **The guard was eating its own evidence.** It logged failures with no stack trace, which is a net
  loss against the red screen it replaced — that screen's trace is how the posting-hire crash was
  localized in the first place. Harmony's RimWorld mod patches `Environment.GetStackTrace` and
  returns full frames only on first access, a `[Ref X] Duplicate stacktrace` placeholder after. The
  guard read `exception.StackTrace` to build its suppression key, consuming the one good rendering
  on a string it never printed, so `ToString()` got the placeholder. It destroyed the evidence by
  measuring it. Now `ToString()` is called once and reused for both.
- **Two methods orphaned by this phase's own work**, `CountMatchingInColony` and
  `CountMatching(SalesOrder, Caravan)`, whose callers had been replaced by the validator. Removed.
  A refactor that replaces a call site is how dead code is created, so that check belongs in the
  same change rather than a later audit.

Worth recording about method:
- **Measuring beat predicting, including against me.** The first profile ran on a quicktest map and
  reported a 42 ms daily refresh; the prediction from that was "expect 2–3× worse at real scale".
  The real save came back at 3.1 ms — 14× *better*. The 42 ms was a cold-start artifact: a fresh
  map generating ten opportunities against empty caches, where the steady state generates one
  against warm ones. Reporting the quicktest figure as representative would have sent the next pass
  optimizing market generation, which is not a problem. The only genuine finding was the one item
  the first run skipped.

## Phase 25 — Polish and compatibility, passes B and C  (2026-08-08)

**Phase 25 is complete.** Pass A is recorded above. This entry covers the rest of §118 and closes
the phase.

Implemented:
- **Mod settings**, three knobs chosen by Matteo — letter volume, market pacing, economy difficulty.
  Subsystem on/off switches were offered and rejected: disabling labor mid-game with employees under
  contract means abandoning live obligations or writing teardown for each system.
- **Economy difficulty made into an actual difficulty axis.** As first built it applied one
  multiplier to both what buyers pay and what suppliers charge, so 150% inflated both sides and
  largely cancelled. Matteo asked what 150% meant and the honest answer was "not what it says". Now
  two named factors moving in opposite directions — selling `2-d`, buying `d`.
- **Letter volume rebalanced** after the first classification left the default tier suppressing
  exactly one message type. The rule is stated in the code: Always is money owed, a broken promise,
  or a decision required before a deadline; Chatty is routine successes. Chatty went from one path
  to eight.
- **UI that survives 1.75× scale**, which is what Matteo actually plays at. Six defects, all one
  bug: fixed row heights sized for one line, clipping text that wraps to three.
- **Eight top-level tabs grouped into five**: Business, Selling (Market, Find buyer, Orders,
  Contracts), Procurement, Labor, Relations. Presentation only; no page moved.
- **Tooltip pass** across all twelve `TipRegion` sites — three changed, nine left because they were
  already answering a question the screen raised.
- **`docs/COMPATIBILITY.md` and a rewritten `README.md`**, both stating their own limits.
- **`docs/BACKLOG.md`**, which the project did not have, plus `docs/ROAD_TO_1_0.md` auditing §120.

Not implemented:
- **Localization: dropped, not deferred.** §118 amended to say so. The mod is English-only.
- A DLC matrix beyond Biotech. Royalty, Ideology and Anomaly are not owned and never will be.

Known limitations:
- Everything is verified on one machine, one load order, one UI scale. `docs/COMPATIBILITY.md` says
  so rather than implying coverage.
- Classification of DLC and modded defs is proven; **trading such an item end to end is not**.

Manual test:
- Settings, the five-tab layout, all six layout fixes, the tooltips, the difficulty text and the
  letter rebalance were each confirmed in play at 1.75×.
- **Per-source classification, measured:** 406 tradable defs — Core 337, **Biotech 67**, RT Fuse 2,
  and zero from the four behaviour/terrain mods. Biotech spans five of six categories with no
  Biotech-specific code. RT Fuse's two minifiable buildings were picked up automatically, which is
  the stronger result: an arbitrary third-party mod's content became tradeable with nobody
  designing for it.
- Save migration exercised twice more: schema 17 walking five steps to 22, and 22 → 23.

Bugs found and fixed during these passes:
- **Buyer pickup was unreachable from Find Buyer.** The order was built without ever assigning
  `fulfillment`, so it silently took the default. Half a shipped mechanic had no route to it.
- **Buyer interest did not vary by good.** Demand was held per *category*, so wood and steel
  returned the same number for a settlement — and that one value drove the interest gate, the
  appetite and the price. The same settlements were out for every good.
- **Installed buildings were sellable, and could be destroyed.** A shelf is a storage building on
  its own storage cells, so it reported `IsInAnyStorage()` while installed. The validator used the
  same rule, so fulfilling a shelf order could `Destroy(Vanish)` shelves off the player's map.
  Found because Matteo noticed they looked wrong in a list.
- **Two more sentinel leaks**, taking the count to five: an employee tooltip printing `termDays` raw
  as "0 days", and the dismissal confirmation still formatting `DaysRemaining` — *the same bug as
  the third occurrence, in a different method, after that one was fixed.*
- **A settings slider that stuck at its centre.** `Widgets.HorizontalSlider` builds its drag ID from
  the rect's screen-space `y`; the description above it changed height as the value crossed 1.0, so
  the control's identity changed mid-drag and Unity dropped it. **This is the inverse of the other
  layout bugs**: measuring accurately every frame is what caused it. Both are one rule — the layout
  must be correct *and* stable.
- **An exception during quest teardown could strand a pawn untracked.** `EmploymentService.End`
  logged a warning and then cleared the only references to the worker, so a failed departure left a
  spawned employee in the player faction with no record — a free colonist the mod no longer knew
  about. Now falls back to restoring their faction, logs at error level, and still clears references.

Worth recording about method:
- **Roughly half this phase's work was not on §118's list.** It came from Matteo playing. Two of
  those finds were real defects rather than polish, and one could have quietly demolished furniture.
  A phase plan written before the code existed cannot anticipate what play reveals; the plan was
  right to be a plan and wrong to be treated as the whole scope.
- **The 1.0 audit found documentation drift, and the drift was mine.** Schema 23's migration and
  buyer-pickup completion were both observed in the log and reported in conversation, and neither
  was written into `docs/PENDING_PLAYTESTS.md` — the exact failure that file exists to prevent,
  committed while using it to hold other work to account. Verifying something and *saying* it is not
  the same as recording it.

## Phase 26 — public beta  (2026-08-08)

0.9.0 is public on both GitHub and the Steam Workshop. No gameplay code changed in this phase; it
was entirely release engineering, verification and distribution.

Implemented:
- **GitHub pre-release `v0.9.0`**, annotated tag on `b8744e4`, with `Intercolony-0.9.0.zip`
  (1,135,903 bytes) attached. Release body is `docs/RELEASE_NOTES_0.9.0.md`, kept in the repo so the
  published text can be diffed against what was written.
- **Steam Workshop item `3780094556`**, created hidden, smoke-tested, then made public.
- **`docs/RELEASE_PROCEDURE.md`** — the upload and update procedure, every claim verified against
  `reference/decompiled/` rather than recollection. Three facts shape it: RimWorld uploads the mod's
  `RootDir` wholesale with no filtering and no junction handling; `About/PublishedFileId.txt` is the
  only thing binding a folder to a Workshop item; and `SetItemVisibility` is never called, so a new
  item takes Steam's default.
- **`docs/WORKSHOP_DESCRIPTION.bbcode`** — the Workshop copy, in BBCode. `SetItemDescription` is
  inside `if (creating)`, so it is pasted once on the website and survives every later re-upload.
- **README doc links repointed at GitHub.** They were relative, and `docs/` and `DESIGN.md` are not
  in the distributable, so both were broken for anyone reading the README out of an unzipped
  release. `./LICENSE` stayed relative because LICENSE ships.
- **`About/Preview.png` replaced** with the Business screen over the colony.
- **Repository made public**, after an audit of the tree and all 66 commits: no secrets, and no
  `reference/`, decompiled source, vanilla defs or other authors' content ever committed.

Not implemented:
- **The Relations empty-state clipping was left unfixed**, deliberately. Recorded in
  `docs/BACKLOG.md`. It is cosmetic and systemic across all five screens, and a launch day is the
  wrong time to touch layout code.
- **No 0.9.1.** Nothing found during the smoke test warranted one.

Known limitations:
- **The preview image nearly shipped broken.** At 1,902,315 bytes it was 1.81x Steam's 1 MB preview
  cap. RimWorld enforces no limit and only checks `File.Exists`, so nothing local would have caught
  it — it would have failed silently at Steam's end. Resized to 933,975 bytes.
- **`package.ps1` does not carry `PublishedFileId.txt` forward**, and `dist/` is rebuilt on every
  run. Correct for a downloadable zip, which must not carry the Workshop identity — but it means the
  next release must restore the ID by hand or it creates a *second* Workshop item. The saved copy
  lives in the gitignored `.workshop/`, and the check is that the menu reads **Update on Steam
  Workshop** rather than **Upload**.
- **Market has never been seen populated on a fresh world.** The smoke test confirmed the screen
  renders; a just-created world has run no refresh cycle, so there was nothing to render.

Manual test:
- **Steam-served build, end to end, 2026-08-08.** Subscribed to the hidden item and verified what
  Steam actually serves rather than what was uploaded: all 9 release files byte-identical by SHA-256
  to `dist/Intercolony-0.9.0`, the only extra being `About/PublishedFileId.txt` that RimWorld itself
  writes. No `Source`, `reference`, `docs`, `Screenshots` or dev scripts. `Intercolony.dll` loads as
  a valid assembly.
- **Loaded from the Workshop, not locally** — `Adding miannoni.intercolony(...\workshop\content\
  294100\3780094556)`, with the `Adding mods from mods folder:` section empty, the local staging copy
  having been removed first precisely so it could not mask the download.
- **All five screens opened**: Business, Selling/Market, Procurement, Labor, Relations. Zero
  exceptions, and specifically no GUI-stack imbalance, which is the failure mode the Phase 25
  applicant-list bug produced.
- **Save and reload** through the Workshop build: `[Intercolony] State loaded (schema 24, nextId 1).`
  — `State loaded`, not `State initialized fresh`, which is what proves the save round-tripped
  instead of silently re-initializing. No unresolved cross-references.
- **The cross-game leak guard was seen working in the shipped build**:
  `[Intercolony] Dropped 15 candidate(s) left over from a previous game.` That is
  `LaborCandidateService.Abandon()`, the fix for the static-pool leak that went undetected for four
  phases, firing correctly in a build a stranger could download.

Worth recording about method:
- **Three verification failures in this phase were defects in the checks, not the artifacts.** A
  UTF-8 file read as ANSI made a byte-identical release body look like a mismatch; a scratchpad path
  containing the word "claude" tripped a forbidden-content scan; and `@() -notmatch` on an empty
  array read as false. Every one produced a FAIL on something that was correct. A verification script
  is code, and it fails the same way code does — the reflex to trust a red result over the artifact
  is exactly as wrong as the reverse.
- **The most valuable checks were the ones against reality rather than intent.** Re-downloading the
  published asset and hashing it, and reading what Steam served rather than what was uploaded, each
  cost a minute and are the only reason the distribution can be claimed rather than assumed.

## Post-0.9.0 playtest corrections  (2026-08-09)

Implemented:
- **Find Buyer availability (A1–A5).** The stock list now subtracts the narrow set of commitments
  that already claim today's goods: open direct Find Buyer sales and buyer-pickup orders whose
  buyers are travelling. Direct creation and Mark Ready both re-check live availability, with the
  latter excluding its own direct order so it cannot block itself. The visible count rebuilds every
  1.5 seconds of real time and reconciles a stale selection and its buyer offers.
- **Buyer-pickup timing (B1–B3).** One travel estimator now feeds dispatch, Market, confirmations
  and tooltips. The order deadline means "mark ready by this time": a pickup marked ready on time no
  longer fails merely because the buyer's journey crosses that deadline, while a pickup still in
  Accepted after the deadline does fail. Unknown routes consistently use the documented three-day
  fallback.
- **Procurement cancellation and retained conclusions (B5, plus the adjacent B5b correction).** The
  existing RFQ withdrawal and purchase-order cancellation transitions are now reachable from the
  Procurement tab. Cancellation says before and after confirmation that prepaid silver is
  forfeited. Completed, cancelled, supplier-default and war-loss orders remain visible under
  Concluded purchases, with the refund distinction and retained outcome note intact.
- **Supply-agreement coherence (C0–C2).** Suspended agreements and unexpired renewal offers now
  suppress a second relationship with the same settlement. New offers are derived from retained
  completed sales: at least two completions of the exact good to that exact settlement, with no
  random fallback. The offer letter names that history, and selection remains deterministic and
  weighted by the number of completed deliveries.
- **Animal-trade research (D1 only).** The spike established that caravan animals are member pawns,
  not inventory items, and that a safe implementation needs a dedicated pawn handoff plus an
  explicit goods/animal discriminator in persisted order state. Its addendum verified the settled
  specification model's species, sex, life-stage and pregnancy dimensions, including buy-side
  pregnancy for verified live-bearing animals. No production animal-trade code shipped.
- No saved fields were added. `IntercolonyWorldComponent.CurrentSaveVersion` remains 24, so existing
  0.9.0 history drives the new contract rule without migration.

- **B4 — stone blocks, resolved as an opt-in setting** (built later the same day; this entry was
  written while it was still outstanding). Core gives `StoneBlocksBase` tradeability `Buyable`,
  which permits buying and forbids player selling to *every* trader; the Intercolony classifier was
  already correct and the report was never an Intercolony defect. Rather than override vanilla for
  everyone or refuse the capability, a default-off setting now assigns `tradeability` from C# —
  the preserved definition patch stays unapplied, because a `PatchOperation` runs during def loading
  and cannot be toggled. Discovery filters on `tradeability == Buyable`, so it finds exactly stone
  blocks and cooked meals in vanilla and picks up modded content without naming a single def. Each
  def's original value is cached at first modification and that exact value restored on toggle-off,
  so another mod's patch is not clobbered. Toggling off does not strand an obligation: tradeability
  gates listing and creation but not delivery, which is now asserted through the production path
  rather than assumed. Also shipped: an "Explain item tradability" debug action naming the first
  gate to reject a def.

Not implemented:
- **D2 and D3 — animal procurement and physical caravan animal sales.** The owner has settled the
  scope: trade by specification rather than individual identity; species, sex, life stage and
  pregnancy selectable and priced separately; goods rules retained wherever technically possible,
  including partial delivery; bonded-animal confirmation names the affected colonist; and buy-side
  pregnancy is allowed for verified live-bearing animals. Implementing that promise needs new
  persisted fields and a migration from schema 24. Neither slice has started.

Known limitations:
- Availability is a logical commitment, not a physical reservation. Colonists may still eat or use
  promised goods, bills may consume them, and hauling or deterioration may create a fulfilment
  shortfall after the order is accepted.
- A direct seller-delivery order remains committed after its goods are loaded into a caravan, so
  Find Buyer temporarily understates free colony stock. Correcting that conservative undercount
  requires assigning caravan cargo to a specific order, which is a separate system and was not
  justified by this correction.
- Find Buyer's 1.5-second refresh is deliberately real-time rather than tick-based, so it continues
  while paused and does not scan three times as often at speed 3. The visible update and hitch
  behaviour have not been observed by a human.
- A settlement with fewer than two completed sales of one exact eligible good now offers no supply
  agreement. There is deliberately no unrelated random fallback.
- The animal spike found no vanilla API that quotes an exact pawn-aware value from a specification
  without generating a pawn, and no universal predicate proving an arbitrary modded race safely
  supports animal pregnancy. Those remain implementation constraints for D2/D3, not shipped
  capabilities.

Manual test:
- A clean build completed, followed by a `dev.ps1` cycle in which RimWorld launched, Intercolony
  loaded, Harmony applied, and schema-24 state loaded with no red Intercolony errors.
- The new assertions in `Run order self-test`, `Run contract self-test` and `Run RFQ self-test` were
  written but **never executed**. They are RimWorld debug actions that still require a human to
  enable development mode and click them; no assertion result is claimed here.

## 0.9.1 release preparation — focused self-tests  (2026-08-13)

Implemented:
- **The buy-only obligation guard now holds through the production path.** The first order self-test
  run found that turning a buy-only category off after accepting a pickup order made Mark Ready
  report zero available stock. The physical goods still matched the binding order; only the
  new-sale classifier rejected them. Existing goods orders now count physically matching stock and
  subtract other commitments instead of reapplying current listing eligibility.
- The regression assertion is split between Mark Ready and collection so a future failure identifies
  which boundary broke.
- Release-state evidence is tracked in `docs/RELEASE_0.9.1_PREP.md`.

Known limitations:
- The order suite's recorded-map-versus-first-home assertion skipped in a one-home-map test world.
  The two-colony buyer-pickup reproduction remains required.
- Live-offer acceptance checks skipped because the test world had no live offer. The focused
  correction-batch availability, timing and buy-only assertions still ran.
- The schema-24-to-31 migration and focused manual UI/save-load pass remain unrun.
- Multi-colony procurement still delivers or refunds through `Find.AnyPlayerHomeMap`; its blocker
  verdict remains under review.

Manual test:
- `Run order self-test`: **93 passed, 0 failed** after the fix.
- `Run contract self-test`: **38 passed, 0 failed**; three cycles completed and a real
  history-derived offer was generated.
- `Run RFQ self-test`: **69 passed, 0 failed**; empty/full/partial quotations, price and quantity
  variation, two modded defs and all four goods-construction examples ran.

## 0.9.1 — agreements, prices and corrections  (2026-08-15)

Implemented:
- **Player-proposed supply agreements.** A settlement could offer the player a standing deal; the
  player could not offer one back. `ContractService.ProposeContract` builds one through the same
  construction, eligibility, cadence, pricing and renewal path a settlement-initiated agreement
  uses — only the 12% per-refresh roll and weighted item selection are skipped, because the player
  is choosing rather than being rolled for.
- **Proposals are answered, not granted.** A proposal is stored `Offered` with a decision tick and
  an appeal score, and `AdvanceContracts` resolves it. Appeal weighs price against the going rate
  (dominant), quantity against the settlement's appetite, and reputation. The wait is shortest at
  *both* extremes and longest in the middle — a superb offer earns a quick yes, an absurd one a
  quick no, and a middling one is the only one genuinely in doubt. The roll is seeded from the
  economy seed and contract id so reloading cannot fish for a better answer.
- **One price lever, from zero to twice spot.** Below the going rate is generosity and above it is
  greed; each deal records the market rate it was struck against, because demand now drifts and
  recomputing spot later would answer a different question. `FactionGiftUtility.GetGoodwillChange`
  values the gap through unspawned Silver in a minimal `IThingHolder`, so vanilla's own maths and
  relations curve are used rather than reimplemented. A penalty is clamped against
  `DiplomacyTuning.BecomeHostileThreshold` and can never start a war.
- **Incoming proposal controls**, persisted per save: a master switch plus six category filters,
  stored as the *disabled* set so an added category is enabled for free. Filtering happens during
  candidate selection, never after generation.
- **Bounded order history.** The hundred most recent closed sales and purchase orders are retained
  and pruned alongside the ledger, with `Clear completed history` on three lists. Contract
  eligibility moved onto a durable `CommercialHistoryEntry` aggregate first — that is what made
  pruning safe at all.
- **A dedicated proposal window** replacing a button, two float menus and a confirm dialog.
- Fixes: purchases deliver and refund to the ordering colony; a refund that cannot be paid is no
  longer reported as paid; the false "0 units free" Mark Ready block; Find Buyer advertising a rate
  the commit then changed; agreements listing undiscounted money; reputation scaling orders past
  their transport limits; and proposal eligibility served from a cache that outlived the window.

Not implemented:
- No counteroffer or negotiation. A settlement takes the offered terms or refuses them.
- Concluded purchase requests can be cleared by hand but have no automatic retention cap. The
  shared predicate exists; the number was left undecided rather than inherited.

Known limitations:
- Availability remains a logical commitment, not a physical reservation.
- Animal trading is still wholly unplayed and is not advertised.
- Buyer pickup from a non-home or camp map is still untested; practicality never established.

Manual test:
- Order, market and contract self-tests rerun and reported green after the batch.
- The proposal window, price lever, pending-decision flow and history clearing exercised in play.
  Two defects found there and fixed before release (`44c6509`, `8aad4ea`).
- **A real schema 22 save migrated cleanly to 39** — a longer chain than the release required, and
  the last outstanding release risk.
- Shipped: Workshop item `3780094556` updated, `main` pushed, tag `v0.9.1` and GitHub pre-release
  created to the 0.9.0 standard.

## 0.9.2 — animal sales and procurement fixes  (2026-08-17)

Written retrospectively on 2026-08-18: this entry was missed when the release shipped. It is
reconstructed from the twenty commits in `v0.9.1..v0.9.2`, their messages, and
`docs/RELEASE_NOTES_0.9.2.md` — not from memory of the session. Work landed 2026-08-15 and -16; the
tag is dated 2026-08-16 and both channels went out on 2026-08-17.

A bug-fix release closing six defects from the first real play of 0.9.1. No new player-facing
features. **Animal trade did not work at all until it.**

Implemented:
- **Animal orders could never be marked ready** (`b50b2e2`), and this is the repair the release
  exists for. Validation treated *any* same-species animal that failed the specification as a fatal
  failure, so one rooster blocked a hen order permanently. RimWorld draws an inactive
  `Widgets.ButtonText` identically to a live one, so the refusal was completely invisible — no
  message, no error, nothing. Two defects compounding: a wrong rule, and a UI that could not show
  it had refused.
- **The employee signing fee is disclosed before hiring** (`66848bb`). It had been shown only when
  a hire was *refused* for insufficient silver — after the decision, too late to plan for.
- **Procurement quotes cannot be re-rolled** (`a11a97f`). Withdrawing a request and raising it again
  produced fresh prices, which made retrying a strictly dominant strategy. Quotes are now seeded so
  they hold until the market refreshes; changing only the quantity does not reroll them either.
- **A supplier's offer is finite within a market window** (`4b5b5ef`, **schema 42**), so buying a
  supplier out and re-requesting no longer restores their stock.
- **Accepting one quotation leaves the rest of the request open** (`f1e6852`, **schema 41**). The
  first acceptance used to close the whole request; it now stays open for exactly the remainder with
  its other quotes still live. `d457465` fixed the quotation list showing more than the quantity
  actually offered.
- **The buyer-pickup distance a promise was made from is persisted** (`ec1ccdd`, **schema 40**),
  because recomputing it later answers a different question than the player agreed to.
- **Colonies are no longer resolved through `Find.AnyPlayerHomeMap`** (`b6e868e`), which returns the
  *first* player home map and is correct only in a single-colony game. The two remaining shipped
  sites were deliberately fixed in opposite directions, and the asymmetry is the point: **taking
  from the player must never substitute a colony**, so buyer collection now refuses and fails the
  order with a reason; **giving to the player may substitute but must disclose**, so procurement
  delivery and refunds fall back to a surviving colony and name it, holding for retry when there is
  no colony at all. No schema change — both maps already persisted.
- **Supply-agreement cycle orders could not be marked ready** (`929f173`).
- **`Arrive buyers now` debug action** (`4fbec43`), pulling travelling buyers forward through the
  real collection handler rather than completing orders directly. Its absence had made every
  sell-side pickup test cost real travel time.
- **The running build identifies itself at startup** (`6579857`), read from `About.xml`, so a bug
  report can be tied to the build that produced it. `930d496` then made `package.ps1` take its
  version from the same field, so the two can no longer disagree.

Not implemented:
- No new features. Everything above is a repair, a debug affordance, or build identification.

Known limitations:
- **Only half of animal trade was proven.** E5 sell-by-buyer-pickup was played end to end on
  2026-08-16 — a chicken, and in a separate save a bonded labrador whose warning named the right
  colonist. **E3a animal procurement and E4 sell-by-caravan were still entirely unplayed at
  release**, and the whole system must not be read as proven because half of it is.
- The manual two-colony reproduction of buyer-pickup collection remained outstanding.
- `PurchaseOrderService` was left with the same latent flaw at its delivery and refund sites that
  `b6e868e` addressed elsewhere; recorded in `docs/BACKLOG.md` rather than widened into this batch.

Manual test:
- **Matteo ran the order self-test in a real two-colony save, and it found a regression the
  single-colony world could not.** The assertion "collection uses the order's recorded colony, not
  `AnyPlayerHomeMap`" had reported `SKIPPED` since 0.9.0 for want of a second colony; **its first
  ever real run failed.** `9ca5062` fixed two wrong-colony regressions introduced by `b6e868e` and
  `929f173`, both from conflating "no colony was ever recorded" — normal for older and for cycle
  orders — with "the recorded colony is gone". The first of those destroyed a completed sale and
  cost reputation with no player action involved. Ten more assertions execute in a two-colony world
  than in a one-colony one, which is the durable lesson.
- Animal sale by buyer pickup proven in play, as above; recorded in `docs/PENDING_PLAYTESTS.md`.
- Shipped: Workshop item `3780094556` updated, `main` pushed, tag `v0.9.2` and a GitHub pre-release
  with `Intercolony-0.9.2.zip` attached.

## 0.9.3 — the Tier 2 UI pass  (2026-08-18)

Eighteen commits since `v0.9.2`, one batch: `docs/BACKLOG.md`'s Tier 2 UI work plus a labor-UI
polish pass that grew out of it. Presentation throughout, with a single behaviour change.

Implemented:
- **Text composed at runtime is measured, not boxed** (`c1610af`). `Widgets.Label` neither clips nor
  scrolls — it paints the whole string from the top-left regardless of the rect — so a builder-fed
  body silently overdrew the controls beneath it. This was a live defect on the sell dialog, not a
  cosmetic one. Dialogs and empty states now size to `Text.CalcHeight`.
- **The sell dialog states its terms as labelled rows** (`713dd1e`), and the market acceptance
  dialog followed (`d8f83e4`). This is CLAUDE.md rule 6 applied to the two dialogs that most needed
  it: prose let two unrelated facts read as one number, which had already misled the mod's own
  author once.
- **Sales orders are a sortable table showing each order's value** (`c8c4d1a`), with the discount
  slider seated in its own row (`854b7f1`) and numeric columns right-aligned (`0585605`).
- **"Mark ready now" on the Find Buyer sale dialog** (`dbf4e4f`), default on, with a mod setting.
  It refuses without creating the order and keeps the dialog open, rather than creating an
  obligation the player cannot meet.
- **The signing fee is named rather than labelled "Due now"** (`69b6041`).
- **Job postings never expire and the Advertise slider is gone** (`f594594`); the "No end date"
  checkbox no longer sits on the term slider (`7955395`).
- **A job posting has no position count** (`a283de0`) — it stays open and the player hires as many
  applicants as they like. **The only behaviour change in the release.** `JobPosting.positions` was
  deleted, field and Scribe line both; `IsValidAfterLoad` became correspondingly *less* strict, so
  no old posting can be dropped by it.
- **Post a job rebuilt on a grid** (`4f39cb0`, `bc5e7aa`, `cbb7374`, `853f9a0`, `5a5d114`) — one
  margin, an even rhythm, every slider given room for the labels it draws above itself, and it fits
  on one screen again.
- **A skipped self-test assertion is now visible in the summary** (`51ef4d7`), closing the gap where
  a `SKIPPED` line could be read as a pass because the count never mentioned it.

Not implemented:
- Backlog finding 10 (player-posted market sale offers; quality and material when selling) was
  explicitly held out of scope for a point release.

Known limitations:
- **Save schema stayed at 42 and no migration was added**, because none was needed —
  `IntercolonyWorldComponent.cs` is not in the diff at all. Two things nonetheless changed under
  existing saves: an old save carries a `<positions>` node with nothing to read it into, and a
  posting created before `f594594` keeps its finite `expiryTick` and will still expire. The second
  is deliberate, but a player holding an old posting sees different behaviour from a new one.
- Nothing in this batch had a full regression pass. Each UI change was confirmed visually as it
  landed.
- PROGRESS.md had no 0.9.2 entry when this one was written. It was backfilled on the same day, from
  the commit range rather than from memory, and is marked as retrospective.

Manual test:
- **The save-compatibility pre-flight passed before packaging.** No save on the machine actually
  contained a `<positions>` node — every posting ever made used 1, the Scribe default, which
  RimWorld omits — so a real save would not have exercised the deleted field at all. A copy of
  **Fenhana** was prepared with `<positions>3</positions>` injected into all three postings and
  posting #4054 reopened while keeping its original finite `expiryTick` (7110770, ~10.5 days ahead
  of the save's tick). Loaded on 2026-08-18: `[Intercolony] loaded, version 0.9.3.` and
  `[Intercolony] State loaded (schema 42, nextId 6826).` — **loaded, not initialized fresh** — with
  **zero exceptions in Player.log**, no dropped-posting warning, and the posting rendering as
  `Crafting 15+ — open, 60d, 130 silver/day per quadrum, civilian` / `no replies yet — 10.5d left`.
- The two-colony manual reproduction of buyer-pickup collection remains outstanding and was
  **explicitly decided not to block this release** — it is a 0.9.0-era fix untouched by this batch,
  and it stays in `docs/PENDING_PLAYTESTS.md`.
- Shipped: Workshop item `3780094556` updated (menu confirmed reading **Update**, not Upload),
  `main` pushed, tag `v0.9.3` and GitHub pre-release created to the 0.9.0 standard.

## Dev test bridge — running the self-tests without a human  (2026-08-21)

Implemented:
- A loopback request/response socket inside the running game (`Source/Intercolony/Debug/Bridge/`),
  gated twice: compiled out unless built with `-p:EnableDevBridge=true`, and dormant unless the
  process has `INTERCOLONY_DEV_BRIDGE=1`. Binds `IPAddress.Loopback` with no setting for the
  address. Seven verbs: `status`, `tests.list`, `tests.run`, `tests.run_all`, `state.summary`,
  `world_pawns.count`, `postings.count`.
- The socket thread never touches Verse. Commands execute on the Unity main thread through a
  dev-only `MonoBehaviour` pump running in `Update()` rather than a tick, so a paused or loading
  game can still answer `status` — which is what an orchestrator polls for readiness.
- `IntercolonyAllSelfTests` gained a registry: stable machine ids (`job-posting`) beside the
  display names the table already printed (`job posting`), plus `List()`, `RunOne()` and
  structured results. One list, read by both the debug action and the bridge. Rendered output is
  byte-identical — order, labels and map requirements were diffed against the previous version.
- `dev.ps1 bridge` and `dev.ps1 test <name> [-Fresh]`, with readiness by `status` poll rather than
  a log substring or a sleep. `package.ps1` refuses to package an assembly containing the bridge.
- A TypeScript CLI and stdio MCP server (`tools/intercolony-dev/`) over one shared orchestrator,
  and a repo-relative `.mcp.json`.

Not implemented:
- No `tests.run` on a named save, and no save/load orchestration. Every run has been on a
  `-quicktest` world.
- No general-purpose verbs. `eval`, reflection and "run the debug action called X" are refused by
  design and listed as such in `docs/DEV_TEST_BRIDGE.md`.

Known limitations:
- **The bridge has never run against a real save, a migration, or two colonies.** `tests.run`
  resolves `Find.CurrentMap`, and this mod has a documented history of one-map assumptions being
  wrong. Listed in `docs/PENDING_PLAYTESTS.md` along with the untriggered crash, port-in-use and
  command-timeout paths.
- `dist/` is gitignored, so a fresh clone must `npm install && npm run build` once before the MCP
  server will start.
- The Steam `Mods\Intercolony` junction points at the other checkout, so testing required
  repointing it by hand and restoring it afterwards.

Manual test:
- Proven against a live game on 2026-08-21: both builds; the normal build contains no bridge
  markers and `package.ps1` rejects a bridge build and accepts a normal one, checked on real DLLs;
  `status` answers with world, map, tick and schema 44; malformed, unknown, oversized (70 KB and
  2 MB), unescaped-control-character and bad-number requests all return structured errors with the
  listener healthy afterwards; CLI exit codes 0/1/2 including a skipped-only run exiting 0; all
  eight MCP tools driven over stdio from the repo root.
- **The motivating scenario did not reproduce.** `job-posting` alone on a verified-fresh world
  with zero open postings: 25 passed, 0 failed, world pawns 16 → 16 and 17 → 17, delta 0. The
  suspected pawn leak is not present, and is now cheap to re-check.
- **Two real defects found, both in the payroll suite, neither in production.** It hired before
  funding the colony, so the hire was refused and everything after it was unreachable; fixing that
  took the suite from 7 assertions to 39 and exposed a signing-fee assertion that had never once
  executed and double-applied the daily premium. Payroll is now 40/0; the full suite is 847 passed,
  0 failed, 14 skipped, clean log, no world-pawn drift.

## 1.0 program — stages 0 through 8  (2026-08-23)

The 1.0 program reached Stage 8 with Stages 0–7 closed and the integration evidence recorded.

Implemented:
- **Stage 0 — program spine:** gate closed.
- **Stage 1 — settlement economies:** gate closed.
- **Stage 2 — market fundamentals:** gate closed at 12/12; play calibration was deferred to the
  final sitting.
- **Stage 3 — circumstance events:** 8/10 criteria closed; criteria 9 and 10 were deferred to the
  final sitting.
- **Stage 4 — brand strength and colony specialization:** gate closed at 13/13.
- **Stage 5 — relationships and negotiation:** gate closed at 12/12.
- **Stage 6 — procurement parity:** gate closed at 12/12.
- **Stage 7 — commercial history:** gate closed at 9/9.
- **Stage 8 — 1.0 integration and release gate:** the full save/load matrix across all twelve
  persistent kinds, the migration matrix from 42 to 56, and the performance profile across seven
  paths were completed.

Not implemented:
- The §8.3–§8.7 play sitting, which needs a human.
- Stage 3 criteria 9 and 10 and Stage 2 calibration, both deferred to that sitting.
- `DESIGN.md`, the release notes and the Workshop description, still to write.

Known limitations:
- Stage 7B's W2 idempotence claim is unproven.
- Two vacuous-pass assertions are logged in `docs/BACKLOG.md`.
- The sales-side cancellation penalty is still an inline literal.

Manual test:
- Remaining human play and calibration paths are in [docs/PENDING_PLAYTESTS.md](docs/PENDING_PLAYTESTS.md).

## 1.0 release  (2026-08-24)

Implemented:
- Intercolony 1.0 shipped: mod version `1.0.0`, RimWorld `1.6`, save schema `56`.
- `package.ps1` built the clean package: 9 files, 2.23 MiB; the shipped assembly contains no dev
  test bridge.
- Branch `1.0` was merged into `main` as a `--no-ff` merge commit `e7053b6` (149 commits), tagged
  `v1.0.0` and pushed. GitHub release `Intercolony 1.0` was published, not a pre-release, with
  `dist/Intercolony-1.0.0.zip` attached.
- Workshop item `3780094556` received an UPDATE, not a new item, from a real directory copy in the
  `Mods` folder, never through the repo junction; the junction has since been restored. The 1.0
  Workshop description and change notes are in `docs/WORKSHOP_DESCRIPTION.bbcode` and
  `docs/WORKSHOP_CHANGENOTES_1_0.bbcode`.

Not implemented:
- The §8.3–§8.7 play sitting never happened. 1.0 shipped without it, deliberately, on Matteo's
  decision. Stage 2's play calibration and Stage 3's criteria 9 and 10 were folded into that
  sitting and are therefore also still outstanding. The agenda remains in `docs/PENDING_PLAYTESTS.md`.

Known limitations:
- Stage 7B's W2 idempotence claim is unproven.
- Two vacuous-pass assertions are parked in `docs/BACKLOG.md`.
- The sales-side contract cancellation penalty is still an inline literal.

Manual test:
- Testing reach was one machine, one load order, Biotech only, UI scale 1.75x.
