# Operator log — Foreman deployment

Operator-owned. **The Supervisor must not rewrite this file**; it records acts taken outside the Run
by the person and tooling that deployed it. The Run's own state is in `RECORD.md` and the files it
cites, and this file never overrides them.

That separation is not something Foreman provides. It was added after the Supervisor silently
replaced `DEPLOYMENT.md` with a summary of its own during its first turn, recovering it four minutes
later only because the operator had committed it first
(`Vector-Consulting-IA-Operacional/agent-foreman` issue #15).

---

## 2026-09-05 — Run started

Orchestrator launched at 15:20:48 −03:00:

    escript bin/symphony \
      --i-understand-that-this-will-be-running-without-the-usual-guardrails \
      --logs-root C:/dev/foreman-deployment/log \
      C:/dev/foreman-deployment/WORKFLOW.md

from `C:/dev/agent-foreman/symphony/upstream/elixir`, with `GITHUB_TOKEN` in the process environment
only. First Codex session for `GH-3` started 15:20:55.

## 2026-09-05 — Record persisted on the Run's behalf

The Supervisor recorded that it could not commit: the sandbox the bundled workflow selects denies
writes to `.git`, so `git add` could not create `.git/index.lock`. It reported that as an environment
limitation rather than skipping the report, which is the conforming behaviour, and correctly refused
to treat any parked or terminal transition as legitimate while it held.

The operator therefore committed and pushed the record from outside the sandbox. This is the
deployment-provided persistence route, and it is a human act inside a loop that is supposed not to
need one. Filed as agent-foreman issue #19.

Commits on `foreman/playtest-batch-2026-09-05`:

| Commit | Contents |
|---|---|
| `3c8a149` | the authorized Plan |
| `8ba4e84` | the deployment binding and readiness record |
| `040e031` | the Run record: decomposition, Coverage Map, `S01` Contract, Candidate Evidence, Evaluator verdict, dispatch ledger |
| `3a62ca9` | the `S01` Candidate — **unaccepted**, `S01` is `IN_PROGRESS` |

## 2026-09-05 — Operator control run of the target's verification

Run at 15:58–16:02 on the Candidate working tree, to establish whether the capability the Evaluator
named as unavailable exists at all on this host:

    powershell -ExecutionPolicy Bypass -File dev.ps1 test long-term -Fresh
    Test: long-term
    Passed/failed/skipped: 48/0/0
    Success: True
    World pawns: 15 -> 15 (delta 0)   Postings: 0 -> 0
    Test signal: PASS   Log signal: CLEAN
    EXITCODE=0

**This is deployment evidence, not Slice Evidence.** It establishes that the machinery works on this
host. It does not discharge `S01`: the Slice still owes its own run performed by its Worker, plus the
mandatory negative control its Contract requires — deliberately break the quiet-success behaviour,
observe the focused assertion fail for that semantic reason, restore, and re-verify. Nothing here may
be cited as proof of the Behavioral Claim.

The control run left `Assemblies/Intercolony.dll` as a bridge-enabled development build, because
`-Fresh` builds with `-p:EnableDevBridge=true`. `package.ps1` refuses to package such an artifact, so
no release path is affected; the next ordinary build restores it.

## 2026-09-05 16:13 — Run halted by the operator

**Cause: the Codex workspace ran out of credits.** Running the agent directly returned
`ERROR: Your workspace is out of credits. Add credits to continue.`

The deployment did not detect this. Every dispatch started a session, errored, and "completed", and
the orchestrator immediately re-dispatched: **274 sessions in 8 minutes 44 seconds**, logged only as
`debug: Codex notification: "error"`. `polling.interval_ms`, `agent.max_turns` and
`agent.max_retry_backoff_ms` all failed to bound it, for the reasons in agent-foreman issue #23. The
operator killed the orchestrator at 16:13.

The Run is **halted, not abandoned and not settled**. Its last established position is intact and
resumable from `RECORD.md`: `S01` is `IN_PROGRESS` after a conforming `ENVIRONMENT_FAILURE` on
Candidate `S01-C1`; `S02`–`S37` are `DRAFT`; no Plan Requirement carries a Disposition; no Human
Blocker is open. `GH-3` remains `open`, which is correct — `closed` would assert `PLAN_COMPLETE`.

## What is required to resume

1. **Credits on the Codex workspace.** Without them no dispatched session can run.
2. **A decision on the agent's identity, which is reserved authority.** The dispatched agent executes
   as a synthetic Windows user — `MatteoAsus\CodexSandboxOnline`, or `CodexSandboxOffline` without
   network — not as the operator. Its RimWorld process therefore has a different profile, cache and
   Steam session, and the dev bridge never became ready for it in three attempts, while the identical
   command passed 48/0/0 for the operator minutes later. Most of this Plan's Slices need that bridge.

   Running the agent at `danger-full-access` would resolve it and is **not** taken on the
   Supervisor's or the operator-agent's authority: `BOOTSTRAP.md` Step 4 forbids weakening
   sandboxing to make delegation easier, and `foreman/AUTONOMY.md` §3 reserves both expanding a
   security boundary and providing-or-accepting-the-absence-of an environment capability required to
   judge a Behavioral Claim. Filed as agent-foreman issue #22.

Restart command, once those are settled: the launch command at the top of this file. The Run resumes
as the same Run from `RECORD.md`; nothing needs to be re-derived.
