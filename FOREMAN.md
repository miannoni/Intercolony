# Foreman state — Intercolony

Stage: **ALL NINE STAGES CLOSED — P0 through P8**
Unit: none — run complete
Worker: idle/none
Loop: CLEAN_HALT
Last done: **Playtest Correction Pass II is complete.** 51 commits on `foreman/playtest-corrections-2026-09-18`, cut from `b4bab42`. **Final acceptance gate: whole suite 1770 passed / 0 failed / 18 skipped, exit 0, log CLEAN, world pawns 14 → 14, kept-forever 12 → 12, build 0 warnings / 0 errors** — and clean on **five** separately generated worlds across the closing sweeps.
**Operator-reserved, deliberately NOT performed:** merge to `main`, tag, GitHub release, Steam Workshop publish. A clean halt is a completed, tested, pushed development branch, and that is what this is.
**What needs the operator's hands:** everything in `docs/PENDING_PLAYTESTS.md`. Two items need preparation rather than just play — loading a save made **before** this pass to exercise the schema 59 → 60 emergency-applicant migration, and posting an Emergency job **while paused**, which reads as broken but is not. **P7 has no automated assertions at all**, so its eight checks carry more weight than the rest.
**Heartbeat cron `38e7d64d` is session-only and should now be removed — there is nothing left for it to wake.**
Updated: 2026-09-21 20:01 — RUN COMPLETE
Foreman load: 2026-09-20 02:57 — SKILL.md + RUN_CONTRACT.md held in session context since the 22:49 activation load; loop restated: wake → check one → verify → dispatch one → persist → end turn.
Plan: C:\dev\INTERCOLONY_PLAYTEST_CORRECTION_PASS_II_PLAN.md · worker copy `docs/PLAYTEST_CORRECTION_PASS_II_PLAN.md`
Mode: autonomous run-to-halt
Foreman: 89fdf02 · source C:\dev\agent-foreman · https://github.com/Vector-Consulting-IA-Operacional/agent-foreman.git
Fallback: if `Skill(foreman)` is unknown, read `C:\dev\agent-foreman\skill\SKILL.md` then `RUN_CONTRACT.md`.

<!-- Everything above this line is the header. A fresh session reads only the header. -->

## The run

**Authority:** `C:\dev\INTERCOLONY_PLAYTEST_CORRECTION_PASS_II_PLAN.md`, worker copy
`docs/PLAYTEST_CORRECTION_PASS_II_PLAN.md`. Branch `foreman/playtest-corrections-2026-09-18`, cut
from `b4bab42`. Autonomous run-to-halt; halt only for a genuine Human Blocker.

**Operator-reserved — not performed:** merge to `main`, tag, GitHub release, Steam Workshop publish.
Clean halt is a completed, tested, pushed development branch.

## Stages

| Stage | Scope | Status |
|---|---|---|
| P0 | baseline + performance measurement + scope lock | closed — 1733/0/18 |
| P1 | F04 Produce Controls UX consolidation | closed — 1741/0/17 |
| P2 | F16 employee action layout | closed — 1739/0/19 |
| P3 | F19/F20 Business benchmark + labor consistency | closed — 1749/0/19 |
| P4 | F23 diversified equipment packages | closed — 1756/0/19 |
| P5 | F23/Emergency posting performance | closed — 1757/0/20 |
| P6 | F24 emergency quote persistence + hiring | closed — 1770/0/18 |
| P7 | F24 raid-like pod attention letter | closed — 1770/0/18 |
| P8 | integration, pending-playtest cleanup, whole-suite gate | closed — 1770/0/18 |

## Stage P8 units

| Unit | Scope | Status |
|---|---|---|
| P8.3 | mark operator-proven playtests; retire the pod-descent claim | done `63b6b63` |
| P8.4 | prove the P0 timing instrumentation is free when off | done — no change needed |
| P8.1/8.2 | cross-stage integration + regression verification | done — 4 worlds clean after P8.6 |
| P8.6 | make the Elite assertion deterministic | done `4ff2a2b` |
| P8.5 | PROGRESS.md milestone, push, clean halt | done `5bf43aa` |

## Stage P7 units — closed

| Unit | Scope | Status |
|---|---|---|
| P7.1 | focused recon: how vanilla pauses, targets and times a pod-arrival letter | done — findings in the header |
| P7.2 | new `LetterDef` with `pauseMode` MajorThreat; look target becomes the pawn | done `bea6a82` |
| P7.2b | reach the def through `[DefOf]`, not a `GetNamed` string | done `bea6a82` |
| P7.3a | recon: why the gear-gate assertion is flaky | done — findings in the header |
| P7.3b | force the gear fixture's actual loadout so it stops being a lottery | done `bea6a82` |
| P7.3 | the four letter assertions L1–L4 | written, all four failing |
| P7.3c | repair the fixture so `Advance` actually launches | failed twice |
| P7.3d | sol recon: the S3-vs-P7 launch differential | done — preflight guard named |
| P7.3e | falsify both `IsCapturedEmployee` clauses + loud preflight assertion | fix failed; the loud assertion works |
| P7.3f | instrument the preflight failure — measure, do not reason | done, and it paid |
| P7.3g | set `employerFaction` to the pawn's own faction | done — preflight now passes |
| P7.3h | first tick unpatched + prefix counters | done — 68/0/1, all green |
| P7.4 | stage gate + pending-playtest entry | not started |

**Decision point if P7.3h does not resolve it:** stop sinking cycles into L4's re-entry. Drop the
Harmony machinery, keep L1–L3, and move L4's duplicate-launch claim to `docs/PENDING_PLAYTESTS.md`
as human evidence. The stage's player-facing value is the pause and the live jump target, both of
which L1–L3 cover.

## P7 locked semantics

- Letter arrives while the pod is still meaningfully inbound, **after** `MakeDropPodAt`, in the
  same synchronous call. Do not move the send.
- The pause comes from the **def**, never a `TickManager` call, and stays preference-gated — a
  player who turned auto-pause off keeps that choice.
- The look target is the **employee pawn**; `CameraJumper` resolves it through `ParentHolder` to
  the skyfaller, then the closed pod, then the pawn. A cell target is a guarded fallback only.
- Source settlement and worker stay named in the letter.
- No second decorative pod, no pre-spawned pawn; the landing lifecycle is play-proven and
  untouched.

## Stage P6 units — closed, kept as the index to its evidence

| Unit | Scope | Status |
|---|---|---|
| P6.1 | persist the frozen quote on `JobApplicant`, schema 59 → 60 + migration | done `b28a834` |
| P6.1b | repair 3 self-test sites the schema bump broke (one had been skipping since P5) | done `b28a834` |
| P6.2 | `TryHireApplicant` honours the frozen quote instead of `travelDays` | done `38821b6` |
| P6.3a/a2 | in-memory hire assertions + direct-construction fixture | done `8f9b7cf` |
| P6.4 | mutation sweep, all four seen red | done `8f9b7cf` |
| P6.3b | save/load proofs S1 quote survives, S2 accept after reload | done `64b2c17` |
| P6.3c | S3 travelling contract survives save/load and arrives exactly once | done `1bb8339` |
| P6.3c4 | close the payroll fixture's pre-existing world-pawn leak it exposed | done `1bb8339` |
| P6.3d | S4 old emergency applicants migrated or invalidated, never silently ordinary | done `a96c0da` |
| P6.3e | five labor emergency assertions made deterministic, not world-dependent | done `a00b0f4` |
| P6.3f | derive S3's tick budget; flake proven by negative control | done `108fb6b` |
| P6.5 | P6 human-evidence entry in `docs/PENDING_PLAYTESTS.md` | done `040bd90` |
| P6.6 | stage gate + close P6 | done — 1770/0/18, exit 0, CLEAN, pushed |

## P6 locked semantics

- **Route bands:** drop pod 1–4 in-game hours; conventional 5–9. The **12-tile** conventional
  threshold is preserved in this pass — the previous run measured 0 qualifying routes in 748 cases,
  and this stage fixes quote *correctness*, not availability balance.
- **Freeze at application time:** transport, arrival ticks, method/route label, source settlement,
  and the ask already shown. The applicant UI reads the frozen quote.
- **`Take on` uses that same quote** to build `arrivalTick`, `arrivalTransport` and the charged
  wage. **Never recalculate route or ETA after payment.**
- **Ordinary postings keep ordinary `travelDays` semantics.**
- Reuse `EmergencyArrivalQuote`; do **not** invent a second emergency model or add a second set of
  route constants to `JobApplicant`.
- Schema **60**, with old emergency applicants migrated or repaired safely — never silently
  reverting to ordinary multi-day travel under an Emergency label.

**Carried from P5, and it belongs in this same migration:** applicants in existing saves have no
`sourceCensusIndex`, and the code currently fences further emergency matching until they are
removed. The 59 → 60 migration must resolve that so the fencing does not outlive the upgrade.

## Standing conventions, paid for in this run

- **Dispatch prompts must be pure ASCII.** Non-ASCII bytes in an argv prompt make the launcher fail
  with exit 126 / "Access is denied". Check with
  `LC_ALL=C grep -c '[^ -~\t]' <prompt>` before dispatching.
- **Dispatch with the `codex` bash shim, never `codex.cmd`** — `codex.cmd` silently truncates a
  multi-line prompt to its first line and exits 0, so the worker answers a question it was never
  asked and changes nothing.
- **A worker reporting success while `git status` shows nothing changed did not run the task.**
  Verify the diff, not the exit code.
- **`dev.ps1 -Fresh` has a Player.log lock race.** Stop RimWorld and confirm the handle is free
  before relaunching; it fired three times before `77b2a88` added a retry.
- **Mute is impossible and unnecessary:** RimWorld opens no render audio session under the bridge
  launch, so there is nothing to mute. Verified per-app; system volume never touched.

## Non-blocking defects and observations

- **Tier-1 market evidence is not deduplicated by settlement** — one supplier can contribute several
  listings or quotations to a single median. Pre-existing, out of scope, recorded not fixed.
- **`Elite` can be structurally absent from a world**, and this stopped being merely an observation:
  it fails the final gate about one run in three (0 Elite of 594 at economy seed 507228411). Being
  fixed in P8.6.
- **`LaborProspect` has no unique stable identity.** Two prospects from one settlement with
  identical skills, passions and price receive the same equipment package. Distinct applicants vary.
- **`IntercolonyAllSelfTests` `state == null` early-out** reports `notRun = Definitions.Length`,
  now one too many since `posting-timings` is excluded from `all`. Cosmetic, error path only.
- **The `Shooting violations=1` job-posting assertion** failed intermittently at ~40% before
  `16209e7` fixed the missing pawn-side requirement recheck; 0 failures in 10 runs after.

## Human evidence owed

All recorded in `docs/PENDING_PLAYTESTS.md`: F04 (`4ec90b2`), F16 (`bf39b1d`), F19/F20 (`f49fbcd`),
F23 (`809a5bd`), P5 click-feel including the paused-game case (`e24c220`), P6/F24 emergency
arrival including the old-save migration case (`040bd90`).

## Operator items

- Heartbeat cron `38e7d64d` is **session-only** and auto-expires after 7 days; it dies with this
  Claude session. A fresh session must re-arm it.
- **Branch pushed** to `origin/foreman/playtest-corrections-2026-09-18`. Push again as stages close.
- **P8 clean-halt checklist** (RUN_CONTRACT §13): clean build; targeted and whole-suite gates;
  **save/load verification — live from P6 onward, since P6 adds persisted state**; log signal;
  durable status docs; push; working tree clean apart from the two operator files; human evidence
  listed; non-blocking defects listed; deviations stated; reserved actions named; `Loop = CLEAN_HALT`.
