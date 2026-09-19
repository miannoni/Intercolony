# Foreman state — Intercolony

Stage: **P6 — F24: freeze and honor Emergency Job Posting arrival quotes**
Unit: P6.1 — persist the frozen emergency quote on `JobApplicant`, schema 59 → 60
Worker: luna running — P6.1 quote persistence · log `C:\Users\matte\.claude\jobs\439462af\tmp\p6-1-worker.log` · bg id `boudh50f1`
Loop: WAITING_ON_WORKER
Last done: **P5 CLOSED.** Click 350–605 ms → **17–20 ms**; negative control 423 ms; gate 1757/0/20, pawn delta 0, log CLEAN; human evidence at `e24c220`.
Updated: 2026-09-19 23:21 — worker checked once, still running (reading `JobPosting.cs`); no source edits yet.
Foreman load: 2026-09-19 22:49
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
| P6.1 | persist the frozen quote on `JobApplicant`, schema 59 → 60 + migration | luna running |
| P6.2 | `TryHireApplicant` honours the frozen quote instead of `travelDays` | not started |
| P6.3 | assertions incl. the four save/load proofs | not started |
| P6.4 | mutations + stage gate + pending-playtest entry | not started |

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
