# Foreman state — Intercolony

Stage: **P6 — F24: freeze and honor Emergency Job Posting arrival quotes**
Unit: P6.5 — P6 human-evidence entry in `docs/PENDING_PLAYTESTS.md`
Worker: luna running — P6.5 pending-playtest entry · log `C:\Users\matte\.claude\jobs\439462af\tmp\p6-5-worker.log` · bg id `bp6lch2ja`
**P6.3f CLOSED at `108fb6b` — diagnosis confirmed by measurement, not arithmetic.** S3's 300-tick loop budgeted the pod's *flight* and forgot that impact spawns the pod, not the pawn: the pod opens only after `openDelay = 110` further ticks, so the real budget is `ticksToImpact + 111` = 231–311. It now derives the deadline from the launched objects and hard-fails if no pod, or more than one, was launched. Negative control — cap it back at 300 across six fresh worlds — reproduced the flake in three: `194+110+1=305`, `190+110+1=301`, `194+110+1=305`, each driving only 300 ticks, against a predicted threshold of 190; the three shorter-flight runs stayed green. With the fix: four whole-suite runs 1769/0/19, 1770/0/18, 1769/0/19, 1770/0/18, all exit 0, zero S3 failures, pawn delta 0.
**All nine P6 assertions are in, and every one has been seen red for its own reason.**
**Non-blocking defect, owed after P6:** `an applicant whose actual gear misses the request is not queued` failed once at HEAD (`Accepted`/`queued=True`) having passed at `8f9b7cf`, `64b2c17`, `1bb8339` — **flaky**, likely world-dependent like the others.
**Standing rule earned twice in this stage:** with `-Fresh`, two runs are never a controlled comparison — every run builds a new world.
Loop: WAITING_ON_WORKER
Last done: **P6.3c–P6.3f all closed** at `1bb8339`, `a96c0da`, `a00b0f4`, `108fb6b`. Their evidence lives in those commit messages; the unit table below is the index.
Updated: 2026-09-20 12:31
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
| P6 | F24 emergency quote persistence + hiring | in progress |
| P7 | F24 raid-like pod attention letter | not started |
| P8 | integration, pending-playtest cleanup, whole-suite gate | not started |

## Stage P6 units

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
| P6.5 | P6 human-evidence entry in `docs/PENDING_PLAYTESTS.md` | luna running |
| P6.6 | stage gate + close P6 | not started |

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
- **`Elite` can be structurally absent from a world.** A run measured **0 Elite of 550 prospects**
  under the currently accepted gate. P4 treats "Elite exists at all" as world-dependent.
- **`LaborProspect` has no unique stable identity.** Two prospects from one settlement with
  identical skills, passions and price receive the same equipment package. Distinct applicants vary.
- **`IntercolonyAllSelfTests` `state == null` early-out** reports `notRun = Definitions.Length`,
  now one too many since `posting-timings` is excluded from `all`. Cosmetic, error path only.
- **The `Shooting violations=1` job-posting assertion** failed intermittently at ~40% before
  `16209e7` fixed the missing pawn-side requirement recheck; 0 failures in 10 runs after.

## Human evidence owed

All recorded in `docs/PENDING_PLAYTESTS.md`: F04 (`4ec90b2`), F16 (`bf39b1d`), F19/F20 (`f49fbcd`),
F23 (`809a5bd`), P5 click-feel including the paused-game case (`e24c220`).

## Operator items

- Heartbeat cron `38e7d64d` is **session-only** and auto-expires after 7 days; it dies with this
  Claude session. A fresh session must re-arm it.
- **Branch pushed** to `origin/foreman/playtest-corrections-2026-09-18`. Push again as stages close.
- **P8 clean-halt checklist** (RUN_CONTRACT §13): clean build; targeted and whole-suite gates;
  **save/load verification — live from P6 onward, since P6 adds persisted state**; log signal;
  durable status docs; push; working tree clean apart from the two operator files; human evidence
  listed; non-blocking defects listed; deviations stated; reserved actions named; `Loop = CLEAN_HALT`.
