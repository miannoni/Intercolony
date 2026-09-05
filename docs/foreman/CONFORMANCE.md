# Conformance result for this deployment — C1–C7

`BOOTSTRAP.md` Step 5 check 10. Run 2026-09-05. **The check does not pass**, which is why this
deployment reports readiness qualified rather than `FOREMAN READY`.

This file is an operator/deployment artifact recording a bootstrap check. It is not Foreman state,
and it is not part of the Supervisor's Durable Execution Record for the Run.

## How the answers were produced

BOOTSTRAP forbids fabricating answers, and each fixture ships its own `expected` block, so answering
from the fixtures as they stand would have been copying the answer key.

Each fixture was therefore mechanically blinded: `starting_record`, `event`, `description` and
`requirement` kept intact; for every assertion, `id`, `kind`, `citations` and the **names** of the
fields to supply kept; every answer value and the `negative_control` removed. A separate agent, given
only the blinded fixtures, the six documents under `foreman/`, and `conformance/README.md`, and
forbidden to read the real fixtures, produced `answers.json` plus per-assertion notes written before
any comparison.

Artifacts, all outside this repository in the deployment directory
`C:/dev/foreman-deployment/checks/`:

| Artifact | Path |
|---|---|
| Blinding script | `blind_fixtures.js` |
| Blinded fixtures | `blind-fixtures/` |
| Answers | `answers.json` |
| Answerer's notes | `conformance-notes.md` |
| Checker output | `conformance-answers-run.txt` |

## Result

    node conformance/run.js --answers answers.json
    fixtures checked: 7
    integrity failures: 0
    negative controls that do not discriminate: 0
    REQUIRED assertions that failed: 15
    implementation tested: yes
    scenario coverage: PASS (C1 through C7 present)
    exit code: 1

31 of 46 assertions matched. Every one of the seven fixtures failed at least one `REQUIRED`
assertion, so no partial pass is available.

## What the failures are

Classified by whether a closed-vocabulary or Boolean field disagreed, or only free text and predicate
shape:

| Failure class | Count |
|---|---:|
| Only `forbidden_by` prose and/or the `when` predicate object differ; every semantic field identical | 11 |
| A field carrying question identity differs — `slice_id`, `plan_requirement_id`, `outcome` | 4 |

The second class is not a semantic disagreement either. C6 `A2` and `A3` are exactly swapped: both
Slice states were derived correctly, but which Slice `A2` asks about is not derivable once the
identifying field is blinded, and that field is itself listed as part of the answer. C4 `A7` and C7
`A7` are the same problem.

**No failure was a disagreement about a Slice lifecycle state, an Evaluator verdict, a Disposition,
or whether a report was permitted** — the five things `foreman/SPEC.md` §Conformance says two
implementations must agree on. The answering agent independently flagged 13 assertions as
under-determined before seeing any expected value.

## Consequence for this deployment

- `FOREMAN READY` is not reported and cannot be reported honestly here.
- Checks 1–9 pass and are recorded in `DEPLOYMENT.md`.
- Filed against Foreman as
  `Vector-Consulting-IA-Operacional/agent-foreman` issue **#20**.
