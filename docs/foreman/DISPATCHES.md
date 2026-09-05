# Delegated role instances

| Slice | Role | Owner identity | State | Dispatch content | Expected next action |
|---|---|---|---|---|---|
| `S01` | Worker | `/root/worker_s01` | resumed/running | Full unchanged `docs/foreman/SLICE_S01.md` Contract, `ENVIRONMENT_FAILURE` verdict, project rules, proof conditions, escalation route and Execution Envelope; changed recovery strategy requires diagnosis of process/port/Steam/mod-list/log/launch preconditions and independent listener proof before any new test run | Restore or diagnose the required bridge capability without an equivalent blind rerun; then collect positive and negative-control Evidence and submit the Candidate, or return a precise failed-attempt report. |
| `S01-C1` | Evaluator | `/root/evaluator_s01_c1` | completed | Full unchanged `S01` Contract, Candidate working-tree identity, `docs/foreman/evidence/S01.md`, all nine mandatory evaluation questions, verdict requirements, and Execution Envelope; no Worker reasoning context; resumed after a non-returning turn with direction to conclude from existing Evidence and avoid repeating the failed bridge sequence | Returned conforming `ENVIRONMENT_FAILURE`; full verdict recorded at `docs/foreman/evaluations/S01-C1.md` and applied by Supervisor. |

The owner identity is the canonical live-agent identity exposed by this deployment. It is distinct
from the Contract's `authoritative_owner`, which names `ContractService.AdvanceAutoReady` as the
system owner of the automated-readiness decision.

The Worker's first turn was interrupted after verification processes ended without a submission; the
same owner was immediately resumed, so role-instance identity and Contract context remained
unchanged. It was interrupted again only after the durable Candidate Evidence appeared, freezing
Candidate `S01-C1` for a separate Evaluator.

The Evaluator's first turn was interrupted after repeated status checks found it still active without
a returned verdict. The same separate role instance was resumed against the unchanged Candidate and
Evidence, with no permission to edit or repeat the materially equivalent failed bridge attempt.

After the Evaluator returned `ENVIRONMENT_FAILURE`, the same Worker role instance was resumed with
the full Contract and a materially different prerequisite-first recovery strategy. Its identity and
next action are recorded above, so no duplicate Worker is to be dispatched for S01.
