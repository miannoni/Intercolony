# Foreman state — Intercolony

Stage: **C0 closed. C1 in progress** — F04 named Produce presets + Architect bulk application.
Unit: C1.1 — `ProduceControlPreset` record + map-component ownership.
Worker: luna running · out `C:\Users\matte\.claude\jobs\439462af\tmp\C1.1-out.txt`
Last done: C1.0 recon closed without a Sol dispatch — both §5.6 and §9.6 answered from the 1.6 references. See C1-D1..D5 and C5-D1..D4.
Updated: 2026-09-15 00:12
Foreman load: 2026-09-14 23:45
Foreman: e46c835 · source C:\dev\agent-foreman · https://github.com/Vector-Consulting-IA-Operacional/agent-foreman.git
Fallback: if `Skill(foreman)` is unknown, read `C:\dev\agent-foreman\skill\SKILL.md` and follow it, then re-run its section 0.

<!-- Everything above this line is the header. A fresh session reads only the header. -->

## THE RUN — read this before dispatching anything

**`C:\dev\INTERCOLONY_PLAYTEST_POLISH_CORRECTION_PLAN.md` is the authority.** A copy for workers,
which have no chat history, lives at `docs/PLAYTEST_POLISH_PLAN.md`. It beats
`docs/PLAYTEST_FINALIZATION_PLAN.md`, every older plan, every old RECON file and every historical
progress record **for the five findings it covers**.

This is a **human-playtest polish pass** over the finalization run, not a correction of a wrong
implementation. Branch `foreman/playtest-polish-2026-09-14`, cut from
`foreman/playtest-finalization-2026-09-13` at `04bd776` (schema 59, whole suite 1674/0/19). That
finalization work is **complete and must not be reset, discarded or recreated**.

**The operator authorised this run to proceed autonomously, start to finish, without stopping between
stages to ask permission.** Continue whenever another executable unit exists. Stop only for a genuine
Human Blocker: a player-facing product decision the plan does not already make, an irreversible or
external action, a save-compatibility/data-loss risk with no safe conservative route, or two explicit
plan requirements that cannot both hold. A hard implementation, a failing test and an unfamiliar
vanilla API are **not** Human Blockers. If one chain blocks, quarantine it and keep executing the
others.

### IN SCOPE — only these five findings

**F04** reusable Produce presets + Architect bulk application · **F16** contextual employee lifecycle
button replacing `...` · **F19** optional material specificity on stuffable selling agreements ·
**F23** equipment-tier abundance in the underlying labor market + three Mod Settings controls ·
**F24** genuinely rapid route-specific emergency hiring, visible drop-pod arrival, arrival letter,
Emergency Job Postings.

### REGRESSION-ONLY — accepted as good, touch only if this run breaks them

**F06** apparel policies / bond consent · **F20** direct labor economics · **F21** settlement
rapid-logistics capability. Also: existing Produce Pause/Resume/Stop, the employment contract
lifecycle, and save compatibility from schema 59. Do not "improve" them opportunistically.

### FROZEN — documented, never advanced

**F12** recurring/preprogrammed player caravans and **F22** player-supplied labor. F21/F24 rapid
transport must not become a back door into F12. F23 applies only to hiring workers **from** the
market. The final diff gets searched for both.

### Out of scope even though known

Administration, unrelated Commercial redesign, the `PurchaseOrderService.DeliverToColony`
partial-delivery defect, and the inaccessible recurring-contract destination defect — unless one
directly blocks this plan. A new unrelated issue gets documented and the run continues.

### Standing constraints

Stay on `foreman/playtest-polish-2026-09-14`. **Never merge to `main`, never tag, never create a
GitHub release, never publish or update the Workshop.** Push regularly. Schema is 59; a world-schema
bump is authorised only where the plan names one (§9.9 emergency posting flag, if the persistence
policy requires it).

### Recon policy — this plan is deliberately on-rails

**Do not open a stage with a repo-wide recon.** Inspect the production files the plan names, verify
the audited seam still exists, and implement directly when it does. Dispatch Sol read-only **only**
for a question the plan marks FOCUSED RECON REQUIRED, or where concrete code evidence proves the
named seam is no longer valid — and only that question. No whole-repo sweeps, no re-reading
`DESIGN.md` / the old source plan / old RECON files / all of `PROGRESS.md`, no long standalone recon
essay. Return from recon to implementation immediately. Optimise for implementation tokens, not
rediscovery tokens.

The plan marks exactly three: §5.6 dynamic Architect category, §8.7 pawn gear regeneration, §9.6
visible drop-pod descent + letter look target.

### Serialisation the plan requires

Never two workers on `MainTabWindow_Intercolony_Labor.cs`, on `ProduceLoopMapComponent.cs`, on
`JobPostingService.cs`/`LaborCandidateService.cs`, or on world schema/migration code.

### Evidence policy — suite green is not evidence

Per stage: targeted behavioural assertion; negative control / mutation where the governing seam could
otherwise be fake; targeted suite; save/load proof for new persisted state; and visual behaviour
recorded honestly as **human evidence owed**, never claimed. Specifically, F24 must not claim human
visual acceptance because `DropPodUtility` was called, a skyfaller class exists, or the pawn
eventually spawned.

## Stages

| | Stage | Scope | Status |
|---|---|---|---|
| ✅ | C0 — baseline and scope lock | — | closed. Branch cut at `04bd776`, build clean, schema 59 |
| ⏳ | C1 — F04 Produce presets / Architect production controls | F04 | in progress |
| ⬜ | C2 — F16 contextual employee lifecycle button | F16 | not started |
| ⬜ | C3 — F19 optional material specificity for selling agreements | F19 | not started |
| ⬜ | C4 — F23 equipment-tier market abundance + settings | F23 | not started |
| ⬜ | C5 — F24 emergency hiring polish | F24 | not started |
| ⬜ | C6 — integration and regression | — | not started |

## Units — stage C0

| | Unit | Status |
|---|---|---|
| ✅ | C0.1 — branch `foreman/playtest-polish-2026-09-14` cut from the finalization branch | done, base `04bd776` |
| ✅ | C0.2 — baseline `dotnet build` | done, 0 warnings 0 errors |
| ✅ | C0.3 — schema confirmed 59 | `IntercolonyWorldComponent.cs:31` |
| ✅ | C0.4 — plan copied to `docs/PLAYTEST_POLISH_PLAN.md`, scope matrix recorded above | done |

## Units — stage C1

| | Unit | Status |
|---|---|---|
| ✅ | C1.0 — recon: runtime Architect designators from a saved list; right-click float menu on an Architect entry | closed by Foreman, no Sol needed; C1-D1..D5 |
| ⏳ | C1.1 — `ProduceControlPreset` record + map-component ownership, old-save safe | luna running |
| ⬜ | C1.2 — `TryApplyPreset` as the single authoritative application path | not started |
| ⬜ | C1.3 — `Dialog_ProduceControls` gains name + `Save as preset` | not started |
| ⬜ | C1.4 — Architect `Production` category with one runtime designator per preset, drag + right-click | not started |
| ⬜ | C1.5 — Architect > Orders generic `Produce controls` preset manager | not started |
| ⬜ | C1.6 — assertions + mutation for preset copy, independence, material intersection | not started |

## Decisions

Recorded as the run makes them. Numbered per stage.

- **C0-D1.** Base is exactly `04bd776`; local and `origin` agreed, so there were no operator
  continuation commits to preserve.
- **C0-D2.** Two pre-existing untracked operator files — `Playtesting annotations.docx` and
  `tools/Compress-Images.ps1` — are left strictly alone: never deleted, moved, modified or committed.
- **C0-D3.** Worker prompts and outputs live in `C:\Users\matte\.claude\jobs\439462af\tmp\`, outside
  the repository, so nothing scratch can reach a commit.

### C1 — §5.6 recon, answered by Foreman from the 1.6 references (no Sol dispatch needed)

- **C1-D1.** Architect entries come from `DesignationCategoryDef.AllResolvedDesignators`, which
  returns the **live** backing `resolvedDesignators` list
  (`reference/decompiled/Verse/DesignationCategoryDef.cs:77`, field at `:40`). `ResolveDesignators()`
  (`:274`) `Clear()`s and rebuilds it, and is called exactly once per game launch from
  `ResolveReferences()` via `LongEventHandler.ExecuteWhenFinished` (`:264-272`). `resolvedDesignators`
  is `[Unsaved(false)]`. Therefore **mutating `AllResolvedDesignators` at runtime is the supported
  route**, and `specialDesignatorClasses` — a `List<Type>` instantiated exactly once at `:277-293` —
  **cannot** carry a runtime-variable count.
- **C1-D2.** Right-click on an Architect entry: `ArchitectCategoryTab.cs:49` draws the panel with
  `GizmoGridDrawer.DrawGizmoGrid(def.ResolvedAllowedDesignators, ...)`, and `GizmoGridDrawer` consults
  `Gizmo.RightClickFloatMenuOptions` at `:354`, `:366` and `:431`. That member is declared
  `virtual` on `Verse/Gizmo.cs:33` and already overridden on `Verse/Designator.cs:100`. So Edit /
  Rename / Remove come from **overriding `RightClickFloatMenuOptions`** on the preset designator. **No
  Harmony patch is required.**
- **C1-D3.** `resolvedDesignators` is per-Def and therefore global, while presets are per-map. The
  registry must re-sync against `Find.CurrentMap`'s preset list. Sync on a cheap guard — current map
  reference plus `ProduceLoopMapComponent.PresetsRevision` — from `MapComponentUpdate()`
  (`reference/decompiled/Verse/MapComponent.cs:12`) gated on `map == Find.CurrentMap`, plus on
  `FinalizeInit()` (`:32`) and on every preset mutation.
- **C1-D4.** UX reference confirmed against `Blueprints Forked - 1.6` source at
  `C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\3525001145\Source\Blueprints\`
  (path contains spaces). `BlueprintController.Initialize()` takes
  `named.AllResolvedDesignators`, `Clear()`s it, re-adds its own designators, and `Add`/`Remove`
  mutate that same list live; `Designator_Blueprint.RightClickFloatMenuOptions` supplies
  Edit/Rename/Remove. **Pattern confirmed, no dependency added, none of its architecture copied.**
  Intercolony must NOT `Clear()` the whole list the way Blueprints does — see C1-D5.
- **C1-D5.** Intercolony's `Production` category is authored once in XML with
  `Designator_Cancel` plus the generic preset manager in `specialDesignatorClasses`. The runtime sync
  must remove and re-add **only its own preset designators**, never `Clear()` the list, so a
  third-party mod that also injected into this category is not wiped.

### C5 — §9.6 recon, answered by Foreman from the 1.6 references (no Sol dispatch needed)

- **C5-D1.** `DropPodUtility.MakeDropPodAt` (`reference/decompiled/RimWorld/DropPodUtility.cs:12-25`)
  **already produces the identical visible descent vanilla raids use**: it spawns a real
  `ThingDefOf.DropPodIncoming` skyfaller through `SkyfallerMaker.SpawnSkyfaller`
  (`reference/decompiled/RimWorld/SkyfallerMaker.cs:40-58`) → `GenSpawn.Spawn`. So the missing piece
  is **not** the skyfaller. The current code at `EmploymentService.cs:1134` never tells the player to
  look: no letter, no look target, and a pod descent lasts seconds. That is why the playtest saw
  nothing.
- **C5-D2.** `MakeDropPodAt` returns `void`, so the letter's look target is obtained by reading the
  spawned `Skyfaller` back off the landing cell immediately after the call, rather than by
  duplicating vanilla's four-line body. `LookTargets` has a `Thing` ctor
  (`reference/decompiled/Verse/LookTargets.cs:46`) and an `IntVec3 + Map` ctor (`:58`) as the
  fallback if the skyfaller cannot be resolved.
- **C5-D3.** The letter goes through `IntercolonyLetters.Send(importance, label, text, def,
  lookTargets)` (`Source/Intercolony/Core/IntercolonyLetters.cs:29`) at
  `IntercolonyLetterImportance.Always` — §9.7 requires a reliably visible importance, and `Always`
  is the only level the player's letter-volume setting cannot suppress.
- **C5-D4.** The audited emergency-ETA defect is confirmed exactly as the plan describes:
  `LaborCandidateService.ArrivalTicksFor` (`:820-833`) returns one hardcoded
  `EmergencyPodArrivalHours` for every pod route, and falls through to
  `candidate.travelDays * GenDate.TicksPerDay` — i.e. **ordinary multi-day timing** — for a
  conventional emergency. `EmergencyConventionalMaxDays = 2` at `:32` is the `<= 2 days` rule the plan
  orders deleted.

## Open for the operator

- Human playtest evidence owed by this run, accumulated per stage. Nothing yet.
