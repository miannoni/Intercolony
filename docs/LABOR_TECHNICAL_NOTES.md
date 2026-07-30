# Technical note — employee pawn control

**Phase 15 spike (DESIGN.md §108, §33, §34). Written 2026-07-26.**

§108 requires: chosen strategy, patches/hooks required, known incompatibilities, restoration
behaviour, unresolved risks. §34 sets the standard for the choice: *"Choose based on
experiments, not aesthetics."*

Evidence: `Source/Intercolony/Debug/IntercolonyLaborSpike.cs`, run in game on RimWorld 1.6
with Core, Biotech, Hospitality, CommonSense, RT Fuse, TilledSoil and
FilthVanishesWithRainAndTime loaded. Subject was a generated `Mercenary_Gunner` of a
non-hostile outlander faction, ideoligion "Rustican".

---

## Answer to §108's acceptance question

> "Can outside employees behave like useful workers without corrupting faction/pawn state?"

**Yes, via Strategy A, with the employee marked as a quest lodger.** Control is not the hard
part; restoration fidelity is — and the quest-lodger mechanism handles the worst of it for
free. Details and the untested list below.

---

## Chosen strategy: A — temporary transfer into the player faction

§34 offers two hypotheses. The experiment settles it.

### Why not Strategy B (retain original faction)

Measured on the foreign pawn *before* transfer:

| Component | Present on foreign pawn |
|---|---|
| `workSettings` | yes (but not enabled) |
| `drafter` | **no** |
| `outfits` / `drugs` / `timetable` | **no** |
| `foodRestriction` | **no** |
| `playerSettings` | **no** |
| `IsColonist` | **false** |

`PawnComponentsUtility.AddAndRemoveDynamicComponents` creates `outfits`, `drugs`,
`timetable`, `reading`, `inventoryStock` and `drafter` **only** when `pawn.Faction.IsPlayer`,
and explicitly sets `drafter = null` otherwise. `Pawn.IsColonist` hard-requires
`Faction.IsPlayer` (`Pawn.cs:522`), and the work, bed, bill, caravan and selection UI all
gate on it.

Strategy B would therefore require Harmony patches on `IsColonist` — a property read
constantly by base game and mods alike — plus manual construction of six trackers the game
assumes are absent. That is a large, permanent compatibility liability for a conceptual
tidiness benefit. Rejected on evidence.

### What Strategy A delivers

After `pawn.SetFaction(Faction.OfPlayer)`, every control question §33 asks that can be
settled in code came back working:

| §33 question | Result |
|---|---|
| 1. Selectable | yes |
| 2. Work priorities assignable | yes — set and read back |
| 3. Workbench eligibility | yes (real use still needs observation) |
| 4. Bed assignable | yes |
| 5. Food policies | yes |
| 6. Areas assignable | yes |
| 7. Draftable | yes — drafted and undrafted cleanly |
| 8. Combat trackable | records tracker present |
| 9. Caravan eligible | **NO for a lodger — see below** |
| 10. Return to colony | yes (same gate) |
| 17. Ideoligion | **retained** — "Rustican" survived both transfers |

---

## Patches and hooks required

**One, and only for caravans.** Strategy A needs no Harmony patch to make an employee work,
sleep, be drafted or be given orders — the vanilla systems simply function once the faction is
the player's. Caravans are the single exception, and it is caused by lodger status rather than
by the faction transfer. See "The caravan exception" below.

Hooks that a labor implementation *will* need:

1. **State capture and restore** around the two `SetFaction` calls — see below.
2. **A tick or event hook** to end employment on contract expiry, death, or the source
   faction turning hostile (§88).
3. **Death and incapacitation interception** if compensation is owed (§43).

---

## Restoration behaviour

Restoration is *nearly* clean. Measured after transferring back:

| Property | Restored |
|---|---|
| Faction | yes |
| `IsColonist` cleared | yes |
| `drafter` removed | yes |
| Ideoligion | yes |
| Direct relations count | unchanged (0 → 0, but see caveat) |
| **`kindDef`** | **NO — `Mercenary_Gunner` became `Colonist` permanently** |

### The kindDef defect

`Pawn.SetFaction` calls `ChangeKind(newFaction.def.basicMemberKind)` for any humanlike
joining the player. The asymmetry that makes this a bug rather than a wash: **only player
faction defs define `basicMemberKind`** (`Colonist`, or `Tribesperson` for a tribal start).
Outlander, tribal and pirate faction defs leave it null, so nothing rewrites the kind on the
way back.

Consequence if unhandled: every employee returns home permanently reclassified as a colonist
kind, losing its original role, equipment expectations and generation identity.

**Two fixes, and the second is better.** Capturing `pawn.kindDef` before transfer and
reassigning it afterwards works, but is silent if forgotten — nothing errors, the pawn is
simply wrong forever. Marking the employee as a **quest lodger** prevents the rewrite from
happening at all, because `SetFaction`'s `ChangeKind` call is guarded by
`!this.IsQuestLodger()`. Prefer the lodger route and keep the capture as a belt-and-braces
restore. See "The quest-lodger mechanism" below.

*(This same asymmetry also broke the first version of this spike: it selected employers by
`def.basicMemberKind != null`, which excluded every non-player faction in the game. Use
`Faction.RandomPawnKind()` instead.)*

---

## Known incompatibilities and side effects

### Storyteller effects — CORRECTED 2026-07-27, and solved

**The first version of this note got this wrong.** It claimed the `GainedColonist`
notification "drives raid scaling" and flagged it as the sharpest unresolved risk. Follow-up
reading of the consuming code shows two *separate* mechanisms, only one of which matters:

1. `watcherPopAdaptation.adaptDays` is merely **reset to zero** by `GainedColonist`
   (`StoryWatcher_PopAdaptation.cs:22`). It feeds `populationIntentFactorFromPopAdaptDaysCurve`
   → **population intent**, i.e. how keen the storyteller is to *offer you more colonists*.
   It does **not** feed threat points. Hiring therefore suppresses new-colonist events for a
   while — a mild, self-correcting effect, not a difficulty spiral.
2. Threat points come from `StorytellerUtility.DefaultThreatPointsNow`, which counts
   `IsFreeColonist` pawns. An employee in the player faction **would** add raid points. This
   is the real effect, and it is live-computed rather than accumulated, so it reverses itself
   when employment ends.

**Both are avoidable outright.** `DefaultThreatPointsNow` explicitly skips pawns for which
`IsQuestLodger()` is true (`StorytellerUtility.cs:142`).

### The quest-lodger mechanism solves three problems at once

RimWorld already models "this pawn lives in your colony but belongs to someone else" — that
is what a quest lodger *is*. Marking an employee as one requires a `Quest` carrying a
`QuestPart_ExtraFaction` with `ExtraFactionType.HomeFaction` set to the employer.

Doing so gives, from vanilla and with no patches:

| Problem | Resolution |
|---|---|
| Employee inflates raid points | `DefaultThreatPointsNow` skips quest lodgers |
| `kindDef` rewritten to Colonist | `SetFaction`'s `ChangeKind` call is guarded by `!this.IsQuestLodger()` — it simply does not fire |
| Faction restoration on death | `QuestPart_ExtraFaction.Notify_PawnKilled` calls `SetFaction(pawn.HomeFaction)` automatically |

That third row also partly answers §33 q14, which this spike had listed as unresolved.

### The caravan exception — CORRECTED 2026-07-29

**This note originally answered §33 q9 "Can the pawn join a caravan?" with "yes". That was
wrong, and the mistake was in how it was measured.** The spike tested `pawn.IsFreeColonist`,
reasoning that caravan forming lists free colonists. The actual gate is
`CaravanFormingUtility.AllSendablePawns`, whose predicate includes
`(!pawn.IsQuestLodger() || allowLodgers)` — and `Dialog_FormCaravan.AllSendablePawns` passes
`allowLodgers: false`. Vanilla deliberately keeps lodgers off caravans.

So lodger status, which buys `kindDef` safety and threat-point exclusion for free, costs
caravan eligibility. It went unnoticed until Matteo played with an employee and tried to send
them out. **The lesson is the recurring one: assert against the gate the game actually uses,
not against a property that seems equivalent.** The self-test now checks
`Dialog_FormCaravan.AllSendablePawns(map, reform: false).Contains(worker)`.

Fixed in Phase 16 with the mod's only labor patch, a postfix on
`CaravanFormingUtility.AllSendablePawns`. It calls the same method again with
`allowLodgers: true` behind a re-entry guard and keeps only pawns that are Intercolony
employees. That way vanilla's rules about downed, mental state, prisoners and lords still
apply unchanged, no other mod's lodgers are affected, and the long vanilla predicate is not
duplicated where it could drift.

One consequence to handle: **do not end a contract whose worker is inside a caravan.**
`LeaveQuestPartUtility.MakePawnLeave` handles a caravan member by calling
`caravan.RemovePawn(pawn)` — which, for a pawn who is off-map and not spawned, leaves them
nowhere. Employment therefore holds an expired term open while the worker is unspawned, tells
the player once, and lets them go home when they are back on a map.

This is strictly better than the mitigations the first draft proposed (patch the
notification, or accept the cost). It uses the same machinery vanilla lodger quests use, so
mods that already understand lodgers will understand employees.

**Recommendation:** an employment contract should create a lightweight quest holding a
`QuestPart_ExtraFaction`, and end it on departure. Do not Harmony-patch the storyteller.

### Building the quest — three things that are not optional

Found while implementing this in Phase 16 (`Source/Intercolony/Labor/EmploymentService.cs`).

1. **`quest.root` must be set.** `Quest.CleanupQuestParts` ends with `if (root.hideOnCleanup)`
   (`Quest.cs:628`), and `Quest.MakeRaw()` leaves `root` null. The result is a
   `NullReferenceException` every single time an employment ends — i.e. on every dismissal.
   The Phase 15 spike hit this and reported it as a bare `EXCEPTION during spike`. Intercolony
   ships `Defs/QuestScriptDefs/Intercolony_Employment.xml` purely to have something to point
   `root` at; it is `randomlySelectable false` so the storyteller can never fire it.

2. **Add a `QuestPart_Leave` with `leaveOnCleanup = true`.** Then ending the quest *is* the
   departure. `QuestPart_Leave.Cleanup` calls `LeaveQuestPartUtility.MakePawnsLeave`, which
   restores the worker's faction from the `QuestPart_ExtraFaction`, clears master and guest
   state, drops anything they were carrying, and puts them under a `LordJob_ExitMapBest` to walk
   off the map. Reimplementing that by hand would be strictly worse.

3. **The ordering works, but only because vanilla passes `forQuest`.** `Quest.End` sets
   `ended = true` *before* calling `CleanupQuestParts`, so by the time `QuestPart_Leave.Cleanup`
   runs the quest is no longer `Ongoing` — and `GetExtraFactionsFromQuestParts` normally filters
   on exactly that. It still resolves because the lookup is `quest.State == Ongoing || quest ==
   forQuest` (`QuestUtility.cs:713`) and `MakePawnsLeave` passes the quest. Any hand-rolled
   departure that calls `GetExtraHomeFaction()` with no argument after ending the quest will get
   `null` and send the worker to a **randomly chosen faction** — the fallback branch in
   `MakePawnLeave`. Do not hand-roll it.

Also confirmed by reading `Quest.QuestTick`: a quest with no expiry and no activable parts never
ends on its own, so an employment quest safely lives as long as the contract does.

### Owning the worker before they arrive

A hired worker travels for a few days before spawning. During that window they are a generated,
unspawned `Pawn` that nothing in the game saves — so they must go into `Find.WorldPawns`, and
with `PawnDiscardDecideMode.KeepForever`, not `Decide`. `Decide` leaves them exposed to
`WorldPawnGC`, which knows nothing about the employment contract and would discard an employee
the player has already paid for. `WorldPawns.RemovePawn` clears the forceful-keep flag, so
spawning on arrival unpins them normally; a hire cancelled before arrival must call
`RemoveAndDiscardPawnViaGC` or the pinned pawn is kept forever.

### Other observed effects of `SetFaction`

- **Guest status is cleared** (`guest?.SetGuestStatus(null)`). Hiring a pawn who is currently
  a guest of the colony discards that status. Note the spike's readout of `Guest` afterwards
  is the enum's default value, not evidence the status survived.
- **Surgery bills are cleared** (`health.surgeryBills.Clear()`).
- **Medical care resets** (`playerSettings?.ResetMedicalCare()`).
- **Mind is cleared** (`ClearMind_NewTemp`), cancelling current jobs — acceptable at a hire
  or fire boundary.
- `Find.GameEnder.CheckOrUpdateGameOver()` runs on every transfer.
- If the pawn is a faction leader, `Notify_LeaderLost` fires on their faction. **Never hire a
  faction leader.**

### Mod compatibility (§33 q18) — untested

No pawn-control mod was loaded during the spike. Mods that assume "player faction implies
permanent colonist" — colonist bars, work tab replacements, roster mods — are the obvious
risk surface. Unproven either way.

---

## Unresolved risks

Listed as UNRESOLVED rather than guessed at, per the spike's purpose:

1. ~~Storyteller population adaptation~~ — **resolved**; see the corrected section above.
   The remaining effect (threat points from an extra free colonist) is avoided by quest-lodger
   status and reverses itself regardless.
2. **Death (§33 q14)** — partly answered: `QuestPart_ExtraFaction.Notify_PawnKilled` restores
   the home faction automatically for a lodger. Still untested are the colonist-death
   notification, mood effects on real colonists, and any faction goodwill loss. §43
   compensation depends on those.
3. **Incapacitation (q12)** and **capture (q13)** — not tested.
4. **Save/load mid-employment (q15)** — not tested. Employment metadata must persist
   alongside a pawn that is temporarily in the player faction.
5. **Source faction turning hostile mid-contract (q16)** — not tested. §88 demands a
   deliberate policy; the pawn would be a player-faction member of a now-enemy polity.
6. **Social relations formed during employment (q20)** — the probe pawn had zero relations,
   so "unchanged 0 → 0" is weak evidence. A pawn that forms bonds while employed is untested.
7. **Long-run behaviour** — the spike measures a single instant. Whether an employee actually
   hauls, cooks and sleeps over days needs observation, not assertions.

---

## Recommendation

Strategy A is viable and should be the basis for the labor phases. Before any labor economy
is built:

1. Create a lightweight quest with a `QuestPart_ExtraFaction` (HomeFaction = employer) per
   employment. This is the single highest-value step: it prevents the kindDef rewrite, keeps
   the employee out of raid-point maths, and restores the faction on death.
2. Still capture and restore the full pawn snapshot as a safety net.
3. Run a long-form observation: hire one worker, watch it live in the colony for several
   in-game days, then fire it.
4. Test downing, capture and save/load mid-employment explicitly.

§33's instruction stands: *"Do not build the full labor economy until the control model is
proven."* Control is proven. Lifecycle is not.
