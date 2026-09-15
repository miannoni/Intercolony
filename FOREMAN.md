# Foreman state — Intercolony

Stage: **C1** — F04 named Produce presets + Architect bulk application.
Unit: C1.4 — Architect `Production` category; verified, being accepted on this wake.
Worker: idle/none
Loop: PROCESSING_RESULT
Last done: C1.4 verified — build 0/0, produce 71/0/1, exit 0, log CLEAN, no def-load errors. C1.3 accepted at `4bf2746`.
Updated: 2026-09-15 01:34
Foreman load: 2026-09-15 01:34
Plan: C:\dev\INTERCOLONY_PLAYTEST_POLISH_CORRECTION_PLAN.md · worker copy `docs/PLAYTEST_POLISH_PLAN.md`
Mode: autonomous run-to-halt
Foreman: 89fdf02 · source C:\dev\agent-foreman · https://github.com/Vector-Consulting-IA-Operacional/agent-foreman.git
Fallback: if `Skill(foreman)` is unknown, read `C:\dev\agent-foreman\skill\SKILL.md` then `RUN_CONTRACT.md`.

<!-- Everything above this line is the header. A fresh session reads only the header. -->

## The run

**Authority:** `C:\dev\INTERCOLONY_PLAYTEST_POLISH_CORRECTION_PLAN.md`, copied for workers to
`docs/PLAYTEST_POLISH_PLAN.md`. It beats every older plan, RECON file and progress record for its five
findings. Branch `foreman/playtest-polish-2026-09-14`, cut from
`foreman/playtest-finalization-2026-09-13` at `04bd776` (schema 59, suite 1674/0/19). That
finalization work is complete and must not be reset or recreated.

**Autonomy.** The operator authorised this run start to finish without stopping between stages. Halt
only for a genuine Human Blocker: an unresolved player-facing product decision, an irreversible or
external action, a save/data-loss risk with no safe route, or two plan requirements that cannot both
hold. A hard implementation, a failing test and an unfamiliar vanilla API are not blockers. If one
chain blocks, quarantine it and keep executing the others.

- **In scope:** F04 presets/bulk apply · F16 contextual lifecycle button · F19 optional contract
  material · F23 equipment-tier market abundance + 3 Mod Settings · F24 route-specific emergency
  hiring, visible pod, arrival letter, Emergency Job Postings.
- **Regression-only:** F06, F20, F21, existing Produce Pause/Resume/Stop, employment lifecycle, schema-59
  save compatibility. Do not improve them opportunistically.
- **Frozen:** F12, F22. F21/F24 transport must not become a back door into F12; F23 covers hiring
  *from* the market only. The final diff gets searched for both.
- **Out of scope:** Administration, Commercial redesign, the `PurchaseOrderService.DeliverToColony`
  partial-delivery defect, the inaccessible recurring-contract destination defect. A new unrelated
  issue gets documented; the run continues.
- **Never:** merge to `main`, tag, GitHub release, Workshop publish. Push regularly. Schema stays 59
  unless the plan names a bump (§9.9 only).

## Foreman method — refreshed to `89fdf02` on operator request, 2026-09-15 01:34

The canonical clone was fast-forwarded `e46c835` → `89fdf02` (clean, not ahead, 8 behind) and
`SKILL.md` / `RUN_CONTRACT.md` / `WORKER.md` reloaded. The 15-minute heartbeat was re-armed with the
new state-machine prompt. This run continues under the new method from its existing durable state; no
accepted work was undone or redone.

**The loop is the method.** WAKE → read state → check the ONE worker once → verify/disposition if
finished → choose ONE bounded unit → dispatch ONE Sol recon or Luna worker → persist → **END THE
TURN**. Four durable loop states: `READY_TO_DISPATCH`, `WAITING_ON_WORKER`, `PROCESSING_RESULT`,
`CLEAN_HALT`.

**Dispatch is a hard turn barrier.** After a launch the Supervisor does no further project work in
that turn: no polling/sleep/watchers, no inspecting future stages, no pre-reconning, no pre-authoring
the next prompt, no unrelated tests, no second worker. Idle Supervisor time is correct; worker time
should dominate. Autonomous run-to-halt describes the multi-turn run, not one long turn.

**Recon roles are literal.** Where the plan marks FOCUSED RECON REQUIRED, that unit belongs to **Sol
high read-only** — always, even when Foreman believes it could answer from `reference/decompiled/`.
Foreman frames the question and later spot-checks the answer; it does not perform the recon.
Everywhere else, do not recon: inspect the files the plan names and implement. The plan marks three;
**§8.7 (C4) and §9.6 (C5) are owed to Sol.**

**Serialise:** never two workers on `MainTabWindow_Intercolony_Labor.cs`, `ProduceLoopMapComponent.cs`,
`JobPostingService.cs`/`LaborCandidateService.cs`, or world schema/migration code.

**Evidence.** Per stage: targeted behavioural assertion; mutation/negative control where the governing
seam could be fake; the relevant subsystem suite before accepting a slice where feasible; save/load
proof for new persisted state; visual behaviour recorded as **human evidence owed**, never claimed.
F24 must not claim visual acceptance because `DropPodUtility` was called, a skyfaller class exists, or
the pawn eventually spawned.

## Stages

| | Stage | Scope | Status |
|---|---|---|---|
| ✅ | C0 — baseline and scope lock | — | closed, `6e1b408`. Base `04bd776`, build 0/0, schema 59 |
| ⏳ | C1 — Produce presets / Architect production controls | F04 | in progress |
| ⬜ | C2 — contextual employee lifecycle button | F16 | not started |
| ⬜ | C3 — optional material specificity for selling agreements | F19 | not started |
| ⬜ | C4 — equipment-tier market abundance + settings | F23 | not started; §8.7 recon owed to Sol |
| ⬜ | C5 — emergency hiring polish | F24 | not started; §9.6 recon owed to Sol |
| ⬜ | C6 — integration and regression | — | not started |

## Units — stage C1

| | Unit | Status |
|---|---|---|
| ✅ | C1.0 — recon: runtime Architect designators; right-click on an Architect entry | closed; C1-D1..D5 |
| ✅ | C1.1 — `ProduceControlPreset` + map-component ownership, old-save safe | accepted, `117f66f` |
| ✅ | C1.2 — `TryApplyPreset`, the single authoritative application path | accepted, `1475025` |
| ✅ | C1.2b — key application on `Thing.Position` so one object gets one loop | accepted, `1475025`. **produce 71/0/1, exit 0, CLEAN** |
| ✅ | C1.3 — `Dialog_ProduceControls` gains name + `Save as preset` | accepted, `4bf2746`. **produce 71/0/1, exit 0, CLEAN** |
| ⏳ | C1.4 — Architect `Production` category: XML def, runtime registry, drag-apply + one summary | luna running |
| ⬜ | C1.5 — `Dialog_EditProducePreset` + rename prompt | not started |
| ⬜ | C1.6 — right-click float menu on the preset entry: Edit / Rename / Remove | not started |
| ⬜ | C1.7 — Architect > Orders generic `Produce controls` preset manager/picker | not started |
| ⬜ | C1.8 — assertions + mutation: preset copy, independence, material intersection, dedupe | not started |

## Decisions — this run

- **C0-D1.** Base is exactly `04bd776`; local and `origin` agreed, so no operator continuation commits
  existed to preserve.
- **C0-D2.** `Playtesting annotations.docx` and `tools/Compress-Images.ps1` are pre-existing untracked
  operator files: never deleted, moved, modified or committed.
- **C0-D3.** Worker prompts and outputs live in `C:\Users\matte\.claude\jobs\439462af\tmp\`, outside
  the repo, so nothing scratch can reach a commit.

### C1 — §5.6, from the 1.6 references

*Process note: Foreman did this recon itself, which the operator has since ruled a role violation.
Every claim was verified at a cited file:line and the units built on it are accepted, so it stands
rather than being re-derived. Sol owns §8.7 and §9.6.*

- **C1-D1.** `DesignationCategoryDef.AllResolvedDesignators` (`Verse/DesignationCategoryDef.cs:77`)
  returns the **live** `resolvedDesignators` list (field `:40`, `[Unsaved(false)]`).
  `ResolveDesignators()` (`:274`) clears and rebuilds it, once per launch, from `ResolveReferences()`
  (`:264`). So runtime mutation of that list is the supported route, and `specialDesignatorClasses` —
  a `List<Type>` instantiated once at `:277-293` — cannot carry a runtime-variable count.
- **C1-D2.** Right-click on an Architect entry: `ArchitectCategoryTab.cs:49` draws via
  `GizmoGridDrawer.DrawGizmoGrid(def.ResolvedAllowedDesignators, ...)`, which consults
  `Gizmo.RightClickFloatMenuOptions` at `:354/:366/:431`. That member is `virtual` on
  `Verse/Gizmo.cs:33`. Edit/Rename/Remove come from overriding it. **No Harmony patch.**
- **C1-D3.** `resolvedDesignators` is per-Def and therefore global, while presets are per-map. Sync
  against `Find.CurrentMap` on a two-comparison guard (cached map + `PresetsRevision`) from
  `MapComponentUpdate()`, plus `FinalizeInit()` and `MapRemoved()`.
- **C1-D4.** UX pattern confirmed against `Blueprints Forked - 1.6` source under
  `C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\3525001145\` (path has spaces): it
  mutates `AllResolvedDesignators` live and supplies Edit/Rename/Remove via
  `RightClickFloatMenuOptions`. No dependency added, no architecture copied.
- **C1-D5.** Intercolony must **not** `Clear()` the whole category list the way Blueprints does. The
  sync removes only elements that `is Designator_ProducePreset` — by type test, not by a remembered
  list, because static state outlives a game here (the `LaborCandidateService` leak in CLAUDE.md), and
  so a third-party injection into the same category survives.
- **C1-D6.** A Produce loop is keyed on the object's own `Thing.Position` — that is what the gizmo
  uses (`ProduceGizmoPatch.cs:66`). Preset application must canonicalise to it, or a drag across a
  2x2 object's four cells creates four loops, none of them the one its gizmo shows. C1.2b.

- **C1-D7.** The `produce` suite baseline on this branch is **71/0/1**, and the single skip is
  pre-existing and documented: `allowedWorkers` is a `LookMode.Reference` list, and the suite's
  detached `ProduceLoopMapComponent` probe has no registered pawn objects for Scribe's cross-reference
  pass, so the references come back null and PostLoadInit strips them. It skips rather than comparing
  an empty list to an empty list (`9ce7518`, `8ad2e16`). **Consequence for C1.8:** the plan's §5.8
  acceptance 3, "preset round-trips save/load including selected workers", cannot be machine-proven by
  that probe either. Assert the non-reference preset fields across a save and record the
  selected-worker round-trip as human evidence owed — do not skip silently, and do not assert
  empty-equals-empty.

## Open for the operator

- Human playtest evidence owed by this run, accumulated per stage. Nothing yet.
