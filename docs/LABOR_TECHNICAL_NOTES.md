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

**Yes, via Strategy A, provided the implementation restores `kindDef` explicitly and accepts
one unresolved side effect on storyteller population adaptation.** Control is not the hard
part; restoration fidelity is. Details and the untested list below.

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
| 9. Caravan eligible | yes |
| 10. Return to colony | yes (same gate) |
| 17. Ideoligion | **retained** — "Rustican" survived both transfers |

---

## Patches and hooks required

**None for control.** This is the headline practical finding: Strategy A needs no Harmony
patch to make an employee work, sleep, be drafted, or join a caravan. The vanilla systems
simply function once the faction is the player's.

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

**Required:** capture `pawn.kindDef` before transfer and reassign it after restoring the
faction. Cheap, but silent if forgotten — nothing errors, the pawn is simply wrong forever.

*(This same asymmetry also broke the first version of this spike: it selected employers by
`def.basicMemberKind != null`, which excluded every non-player faction in the game. Use
`Faction.RandomPawnKind()` instead.)*

---

## Known incompatibilities and side effects

### Storyteller population adaptation — unresolved, and the sharpest risk

`SetFaction` into the player faction calls:

- `Find.StoryWatcher.watcherPopAdaptation.Notify_PawnEvent(this, PopAdaptationEvent.GainedColonist)`
- `Find.StoryWatcher.statsRecord.UpdateGreatestPopulation()`
- `Find.World.StoryState.RecordPopulationIncrease()`

Population adaptation drives **raid scaling**. Nothing observed reverses these on departure.
A labor system could therefore make raids progressively harder with every worker hired, in a
way no player would attribute to the mod. Not measured by this spike — flagged as the first
thing to test before shipping labor.

Possible mitigations, in order of preference: keep employees out of the player faction for
the *accounting* purposes only (not possible under Strategy A), patch the notification
during a hire, or accept and document it as a balance cost of employing outsiders.

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

1. **Storyteller population adaptation** (above) — highest priority.
2. **Death (§33 q14)** — not tested. Death of a player-faction pawn triggers colonist death
   notifications, mood effects on real colonists, and possibly faction goodwill loss.
   §43 compensation depends on this.
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

1. Implement capture/restore of `kindDef` (and ideally the full snapshot the spike uses).
2. Measure and resolve the population-adaptation side effect.
3. Run a long-form observation: hire one worker, watch it live in the colony for several
   in-game days, then fire it.
4. Test death, downing and save/load mid-employment explicitly.

§33's instruction stands: *"Do not build the full labor economy until the control model is
proven."* Control is proven. Lifecycle is not.
