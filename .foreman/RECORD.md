# Durable Execution Record — run-2026-09-06-intercolony

Target: `C:/dev/Intercolony`, branch `foreman/playtest-batch-run2`.
Plan source: `C:/dev/INTERCOLONY_PLAYTEST_BATCH_SOURCE_PLAN.md` (external to the target repository).
Bootstrap evidence: `.foreman/BOOTSTRAP_RECORD.md`. Deployment: `.foreman/deployment.json`.

## Position

| Item | State |
| --- | --- |
| Plan Requirements extracted | No — the Plan has not yet been read |
| Decomposition | Not formed |
| Coverage Map | Not formed |
| Slices | None exist |
| Open Human Blockers | None |
| Decision Records | None |
| In flight | `R01-PLAN-INGEST` — record repair, dispatched turn 1 |

No Slice exists, so no lifecycle transition has occurred and there is nothing to
evaluate. `RUN_SETTLED` and `PLAN_COMPLETE` conditions do not hold and have not been
assessed.

## The record defect found on turn 1, and the repair in flight

`foreman/DURABLE_RECORD.md` requires `.foreman/PLAN.md` — "the authoritative Plan under
execution" — to be part of the record. It is absent. `.foreman/deployment.json` points
`plan` at `../INTERCOLONY_PLAYTEST_BATCH_SOURCE_PLAN.md`, a path outside the target
repository, so the Plan has never been inside the record at all.

**Environment fact a later turn must not re-derive.** The Supervisor role is launched by
`runtime/roles.mjs` with `--add-dir` taken from `deployment.json` `roles.supervisor.addDirs`,
which lists only `C:/dev/agent-foreman`. The Supervisor's permitted directories are therefore
`C:/dev/Intercolony` and `C:/dev/agent-foreman`. Every attempt to read the Plan from its
external path is refused by the harness before any tool runs:

- `Read` on `C:\dev\INTERCOLONY_PLAYTEST_BATCH_SOURCE_PLAN.md` — permission not granted;
- `cat` via the Bash tool — `blocked ... may only concatenate files from the allowed working
  directories for this session: 'C:\dev\Intercolony', 'C:\dev\agent-foreman'`;
- `Get-Content` via PowerShell — `blocked ... may only access files in the allowed working
  directories`.

Widening `addDirs` to `C:/dev` was rejected as the repair: expanding an access boundary is
reserved to the operator under `foreman/AUTONOMY.md` §3, and it is also the wrong fix, because
it would leave the record still without its Plan and still unreconstructable from a fresh
checkout.

The repair is the one the record layout already prescribes: place a **verbatim byte-for-byte
copy** of the Plan at `.foreman/PLAN.md` and commit it. That is reversible, confined to the
target repository, and widens no boundary. The copy is performed by the launcher's own Codex
delegate, whose sandbox can read the source path, under job `R01-PLAN-INGEST`.

`R01-PLAN-INGEST` is **record repair, not a Slice**. It carries no Behavioral Claim, produces
no Candidate, and is not evaluated. Extraction of Plan Requirements from the copied Plan
remains Supervisor authority and was not delegated.

Once `.foreman/PLAN.md` exists, it is the authoritative Plan for this Run. Its fidelity to the
external source is established by the SHA-256 comparison recorded in the job result, and it
must never be edited: a change to authorized intent is a Plan revision requiring reserved
authority under `foreman/PLAN_PROTOCOL.md` §9.

## Turn log

| Turn | Woken by | Action taken |
| --- | --- | --- |
| 1 | `run-start` | Reconstructed position; found the record has no `PLAN.md` and the Plan is unreadable from the Supervisor's permitted directories; dispatched `R01-PLAN-INGEST` to copy it into the record. |
