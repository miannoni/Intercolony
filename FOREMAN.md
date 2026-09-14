# Foreman state — Intercolony

Stage: P0 — scope lock and baseline
Unit: P0.2 — the 1.1.0 milestone record PROGRESS.md never received
Worker: idle
Last done: P0.1 — `Patches/` now ships in a packaged release, mutation-proven, `6a7d0b8`
Updated: 2026-09-14 00:20
Foreman load: 2026-09-14 00:05
Foreman: e46c835 · source C:\dev\agent-foreman · https://github.com/Vector-Consulting-IA-Operacional/agent-foreman.git
Fallback: if `Skill(foreman)` is unknown, read `C:\dev\agent-foreman\skill\SKILL.md` and follow it, then re-run its section 0.

<!-- Everything above this line is the header. A fresh session reads only the header. -->

## THE RUN — read this before dispatching anything

**`C:\dev\INTERCOLONY_PLAYTEST_FINALIZATION_EXECUTION_PLAN.md` is the authority.** It beats
`docs/PLAYTEST_BATCH_SOURCE_PLAN.md`, `docs/PLAYTEST_CORRECTION_PLAN.md`, every old RECON file, and
every historical progress record **for the findings it covers**. A copy lives at
`docs/PLAYTEST_FINALIZATION_PLAN.md` so workers, which have no chat history, can cite it.

**The previous run is FINISHED AND SHIPPED.** 1.1.0 is on the Workshop, merged to `main`, tagged
`v1.1.0`, released on GitHub. Its stages C/D/E/F/G/H are closed and **must not be resumed**. This
branch starts from `main` at `e79fba0`.

**The operator authorised this run to proceed autonomously, start to finish, without stopping
between stages to ask permission.** Continue whenever another executable unit exists. Stop only for
a genuine Human Blocker: a player-facing product decision the plan does not already make, an
irreversible/external action, a save-compatibility or data-loss risk that cannot be resolved
safely, or two explicit requirements that cannot both be satisfied. If one chain blocks, quarantine
it and keep executing the others.

### IN SCOPE — only these findings

**F04, F06, F16, F19, F20, F21, F23, F24.** F13 and F17 are **not separate slices**; their accepted
semantics fold into the F16 redesign (auto-renew stays visible and directly toggleable; occasional
actions do not clutter the collapsed card). F16 is the newest presentation authority and supersedes
F17's blanket "everything behind `...`" rule.

### FROZEN — documented, never advanced

**F12** (recurring/preprogrammed player caravans) and **F22** (player-supplied reverse labor
market). F21/F24 rapid transport must not become a back door into F12. F23 applies only to hiring
workers **from** the market. The final diff gets searched for both.

### REGRESSION-ONLY — closed, touch only if this run breaks them

F01, F02, F03, F05, F07, F08, F09, F10, F11, F14, F15, F18, F25.

### Standing constraints

Stay on `foreman/playtest-finalization-2026-09-13`. **Never merge to `main`, never tag, never
create a GitHub release, never publish or update the Workshop.** Push regularly. One schema bump
at most (58 → 59) for the P2 labor tranche; a second one comes back to the operator.

### Recon policy — this plan is deliberately on-rails

**Do not open a stage with a repo-wide recon.** Inspect the production files the plan names. Dispatch
Sol read-only **only** for a question the plan marks FOCUSED RECON REQUIRED, and only that question:
under ~10 files, no whole-repo sweeps, no re-reading `DESIGN.md`/the source plan/old RECON files/all
of `PROGRESS.md`, no long standalone recon document unless a real blocker appears. If the named seam
exists and matches the plan, **implement it** — do not ask Sol to redesign a decided feature.

Broaden only when: the named symbol is gone; the assumed vanilla seam is materially different in the
1.6 references; an additive field collides with an existing owner; a mutation proves the seam does
not control the behaviour; or save/pawn-ownership semantics get uncertain enough that guessing could
corrupt a save.

### Serialisation the plan requires

Never two workers on `MainTabWindow_Intercolony_Labor.cs`, on `EmploymentContract.cs` /
`EmploymentEquipment.cs`, or on world schema/migration code. These stages are small enough that
serialising is cheaper than reconciling.

## Stages

| | Stage | Scope | Status |
|---|---|---|---|
| 🔨 | P0 — scope lock, baseline, pipeline repair | — | P0.1 accepted `6a7d0b8` |
| ⬜ | P1 — F04 Produce Controls | F04 | one focused construction recon |
| ⬜ | P2 — labor persistence spine | — | schema tranche for F06/F23/F24 |
| ⬜ | P3 — F06 apparel policies + bond buyout | F06 | one focused apparel/drop recon |
| ⬜ | P4 — F16 employee-card redesign | F16 (+F13/F17) | no recon |
| ⬜ | P5 — F19/F20 contract economics | F19, F20 | one tiny construction-cost lookup |
| ⬜ | P6 — F21/F24 rapid logistics + pod hiring | F21, F24 | one focused drop-pod API recon |
| ⬜ | P7 — F23 requested equipment levels | F23 | one focused pawn-gear recon |
| ⬜ | P8 — integration, regression, docs, clean halt | — | — |

## Units — stage P0

| | Unit | Status |
|---|---|---|
| ✅ | P0.1 — a packaged release contains `Patches/`, and cannot silently stop containing it | accepted, `6a7d0b8` |
| 🔨 | P0.2 — `PROGRESS.md` gains the 1.1.0 release milestone it never received | — |
| ⬜ | P0.3 — the plan copy lands in `docs/`, and the scope matrix is recorded | — |
| ⬜ | P0.4 — whole-suite baseline on a fresh world, before any feature edit | — |

## Decisions

- **2026-09-14 — P0-D1. `Patches/` shipping is a deliberate release-content decision, recorded in
  `package.ps1` where the allowlist comment demands one.** The four new assertions name literal
  relative paths rather than reading back from `$ReleaseDirectories`, so deleting the allowlist entry
  still trips them; proven by doing exactly that and watching packaging throw at `package.ps1:122`.
  The released 1.1.0 zip is untouched — the check ran under a `-Version 1.1.0-packagingcheck` name
  and its artefacts were deleted.
- **2026-09-14 — P0-D2. The shipped 1.1.0 is missing the area Produce/Pause/Stop designators and the
  Settlement Economy tab**, because both are registered only by the XML patches that never shipped.
  That is a defect in a published build and it is the operator's to decide about; this run repairs
  the pipeline only and does not prepare 1.1.1.
- **2026-09-14 — P0-D3. This FOREMAN.md was rewritten rather than appended to.** The previous run's
  stage narrative, unit tables and defect-gate detail are in the git log on
  `foreman/playtest-batch-2026-09-06` and in `PROGRESS.md`; carrying 1,300 lines of finished-run
  prose forward is exactly what made the old file claim the release was still pending. What survives
  here is the standing rules, the open operator items, and the frozen markers.

## Standing rules the project has already paid for

  - **When a field stops being written, grep every reader.** A `> 0` check treats zero as corruption.
  - **When something stops happening immediately, find everything that assumed it was instant.**
  - **A number chosen without looking at the data the game generates is a guess.** F24's original
    two-day window was five times smaller than the nearest settlement in the world.
  - **The seam nobody asserts is the one between the caller and the service.**
  - **A skip is not evidence** — but a skip can BE the finding, as it was for F24.
  - **A mutation that fails to compile looks exactly like one that found nothing.** Check the anchor
    is unique and the replacement builds; read the run's log, not the summary line.
  - **When a mutation does not bite, suspect the mutation before the assertion.**
  - **A fixture that fails for the wrong reason passes for the wrong reason too.** Assert on the
    branch you meant to exercise, and print the reason in the failure detail.
  - **A green suite whose COUNT dropped is not a green suite — find out why before accepting.**
    Assertion totals vary by world (1601–1606 observed) because some only run when the generated
    world supplies a fixture. **Report zero failures and a range, never one number.**
  - **A fixture that assumes a world shape is flaky, and it will fail on a world that is merely
    small.** Where a world cannot exercise a bound, SKIP with the reason and the measurement.
  - **An oracle that reads state the test itself has already mutated is measuring the wrong world.**
    Snapshot the ranking before the act, not after.
  - **A self-referential skip must fail, not skip.** Three assertions that skipped when their own
    production dependency returned empty now go red instead.
  - **Never let a zero mean unknown.** Say it in words.
  - **A charge the player was never shown is worse than the problem it fixes.**
  - **`package.ps1` refuses a bridge-enabled DLL**, which is correct — rebuild plain before packaging.
  - Use `dev.ps1 bridge -Save`, not `run -Save`, to load a save.
  - Write files with the file tools; PowerShell `Get-Content` + `Out-File` double-encodes UTF-8.
  - The Bash tool's working directory persists between calls — `cd` back to `C:\dev\Intercolony`
    after visiting another repo, or `dev.ps1` will not be found.

## Open for the operator

- **2026-09-14 — the published 1.1.0 has no `Patches/`.** See P0-D2. The pipeline is fixed on this
  branch; whether a 1.1.1 goes out, and when, is yours.
- **2026-09-13 — `docs/WORKSHOP_DESCRIPTION.bbcode` is stale.** Produce is entirely absent from it.
  Edit notes were prepared and deliberately not applied. Explicitly NOT a blocker for this run.
- **2026-09-09 — a recurring contract whose counterparty becomes inaccessible is cancelled
  silently** (`ContractService.cs:1655`): status plus a `ContractCancelled` timeline record, no
  letter. The contract is already terminal, so a letter would be noise of a different kind. Left
  alone; say if you want it announced.
- **2026-09-08 — `PurchaseOrderService.DeliverToColony` refunds only when ZERO goods were placed.**
  With any non-zero count it calls `Complete` with what was placed, so a partial delivery silently
  completes short and the player pays in full. Pre-existing. Fixing it means deciding what SHOULD
  happen — hold, partial refund, or overflow elsewhere.
- **Manual playtests owed: `docs/PENDING_PLAYTESTS.md`.** Not a blocker for this run; this run adds
  to it rather than clearing it.

## Closed history

**The finalization run's predecessor** — the F01–F25 playtest batch and its correction run — closed
on 2026-09-13 with 1.1.0 merged (`6322edf`), tagged `v1.1.0`, released on GitHub, and uploaded to
the Workshop as item `3780094556` by the operator. Its stage tables, unit tables, defect-gate
narrative and decision log are in the git history of `foreman/playtest-batch-2026-09-06` and in the
milestone records in `PROGRESS.md`. Nothing from it is pending.

Findings closed there: F01, F02, F03, F05, F07, F08, F09, F10, F11, F13, F14, F15, F17, F18, F25.
Findings it deliberately left for newer product direction, now covered by the finalization plan:
F04, F06, F16, F19, F20, F21, F23, F24. Frozen then and frozen now: **F12**, **F22**.
