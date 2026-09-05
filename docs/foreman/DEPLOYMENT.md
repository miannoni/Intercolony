# Foreman deployment binding and readiness record

Produced by following `BOOTSTRAP.md` in the Foreman clone at `C:/dev/agent-foreman`, commit
`4f82f4f`, on 2026-09-05. This file is the operator-visible durable record that BOOTSTRAP Step 1
requires, so a fresh session can see why each branch was taken.

This file records the deployment. It is **not** Foreman state. The Durable Execution Record for the
Run is the rest of `docs/foreman/`, and the Supervisor owns it.

---

## Step 1 — Host inspection

All observations taken on 2026-09-05 between 14:35 and 15:10 −03:00 on host `MATTEO-PC`.

| Detect | Observed | Command / source |
|---|---|---|
| OS, architecture, shell | Microsoft Windows 11 Home, AMD64, Windows PowerShell 5.1.26100.9168 | `Get-CimInstance Win32_OperatingSystem`, `$env:PROCESSOR_ARCHITECTURE`, `$PSVersionTable` |
| POSIX shell for hooks | `sh` → `C:\Program Files\Git\bin\sh.exe` (Git for Windows) | `Get-Command sh` |
| `git` | 2.39.1.windows.1 | `git --version` |
| Elixir | 1.19.5, compiled with Erlang/OTP 28 | `elixir --version` |
| Erlang/OTP | 28 (erts-16.4, 64-bit, jit) | `erl -noshell -eval 'io:format("~s",[erlang:system_info(otp_release)])'` |
| `mix` | 1.19.5 (with Elixir) | `mix --version` |
| Version managers | `mise` absent, `asdf` absent | `Get-Command mise`, `Get-Command asdf` |
| `node` / `npm` | 24.14.0 / 11.11.1 | `node --version`, `npm --version` |
| `python` | 3.14.3 | `python --version` |
| `docker` | absent | `Get-Command docker` |
| Agent harnesses | Codex CLI 0.149.0; Claude Code 2.1.220 | `codex --version`, `claude --version` |
| Provider CLI | GitHub CLI 2.97.0, authenticated as `miannoni`, keyring-stored token, scopes `gist, read:org, repo` | `gh auth status` (token value never printed or stored) |
| .NET SDK | 9.0.316 | `dotnet --version` |
| Repo root | `C:/dev/Intercolony`, inside a work tree | `git rev-parse --show-toplevel`, `--is-inside-work-tree` |
| Remotes | `origin` → `https://github.com/miannoni/Intercolony.git` (fetch and push), provider GitHub | `git remote -v` |
| Base line | `1.0.1` at `6ec6c16`, 46 commits ahead of `main`; not superseded | `git rev-list --left-right --count main...1.0.1` |
| Working branch | `foreman/playtest-batch-2026-09-05`, created from `1.0.1`; `main` untouched | `git checkout -b` |
| RimWorld | installed, `Version.txt` = `1.6.4871 rev590`; `Mods\Intercolony` junction present | filesystem |
| Steam | client running, `HKCU:\Software\Valve\Steam\ActiveProcess\ActiveUser` = `64255983` (non-zero, so logged in) | registry |
| Dev bridge tool | `tools/intercolony-dev/dist/cli.js` built and present | filesystem |
| `reference/` | `decompiled/` and `vanilla-defs/` present | filesystem |

**Is this directory the project to be operated on?** No. `C:/dev/agent-foreman` is the Foreman
distribution that launches work elsewhere; `C:/dev/Intercolony` is the project under work.

### The target project's actual checks

Found in `CLAUDE.md`, `docs/DEV_TEST_BRIDGE.md`, and `dev.ps1`. No CI configuration declares
anything further; nothing was invented from the bundled runtime.

| Check | Command |
|---|---|
| Build | `dotnet build Source/Intercolony/Intercolony.csproj` |
| Build with dev bridge | `dotnet build Source/Intercolony/Intercolony.csproj -p:EnableDevBridge=true` |
| One in-game suite | `powershell -ExecutionPolicy Bypass -File dev.ps1 test <suite>` |
| One suite, isolated | `dev.ps1 test <suite> -Fresh` |
| Whole suite, isolated | `dev.ps1 test all -Fresh` |
| Log delta | `dev.ps1 new` / `dev.ps1 log` |
| Shell equivalent of the bridge | `node tools/intercolony-dev/dist/cli.js tests all --json` |
| Lint | none declared |

Exit codes for the bridge: `0` clean, `1` assertions failed, `2` everything else **including a run
whose assertions passed but whose `Player.log` gained new exceptions**.

---

## Step 2 — Execution layer

**Branch 1 of the ordered procedure: the bundled Symphony reference runtime.**

The reference runtime's prerequisites are *already present on this host*, so the first branch matches
and no acquisition and no operator decision was required. `symphony/upstream/elixir/mise.toml` pins
`erlang = "28"` and `elixir = "1.19.5-otp-28"`; the host has exactly Elixir 1.19.5 compiled with
OTP 28 and `erl` reporting OTP release 28. The absence of `mise` is irrelevant: BOOTSTRAP Step 2
requires the prerequisites to be present *or* obtainable within the Envelope, and they are present.

No system-level install was performed, proposed, or needed. Branches 2 and 3 were never reached.

Sequence actually run, from `C:/dev/agent-foreman/symphony/upstream/elixir`:

    mix local.hex --force --if-missing
    mix local.rebar --force --if-missing
    mix setup            # deps.get
    mix build            # escript.build -> bin/symphony

Launch command:

    escript bin/symphony \
      --i-understand-that-this-will-be-running-without-the-usual-guardrails \
      --logs-root C:/dev/foreman-deployment/log \
      C:/dev/foreman-deployment/WORKFLOW.md

The acknowledgement switch is a mandatory flag of the bundled CLI (`lib/symphony_elixir/cli.ex`),
not a local relaxation of any sandbox or approval setting.

Only build outputs (`deps/`, `_build/`, `bin/symphony`) were produced inside
`symphony/upstream/`; both are ignored by that directory's own `.gitignore`. No source under
`symphony/upstream/` was modified, and `git status` in `C:/dev/agent-foreman` is clean.

---

## Step 3 — Deployment binding

The checked-in template `workflows/foreman/WORKFLOW.md` is **unmodified**. The runtime is pointed at
a deployment-owned copy at `C:/dev/foreman-deployment/WORKFLOW.md` carrying only deployment facts.

### 3.1 Adapter feasibility check — non-mutating, performed before any binding

Read-only provider queries only; nothing was created, renamed, or reconfigured.

    gh api "repos/miannoni/Intercolony/issues?state=all&per_page=100" --jq '[.[] | .state] | unique'
    -> ["closed","open"]

Cross-checked against the adapter source rather than only its documentation:
`symphony/upstream/elixir/lib/symphony_elixir/github/client.ex` normalizes `issue.state` straight
from GitHub's REST `state` field (lines 182–204), and `github_state_query/1` (lines 364–374)
recognizes only `open` and `closed` — any other requested state name yields no provider request at
all.

**Result: outcome 2 of BOOTSTRAP Step 3.1 — clearly two representable states.**

Linear, Jira, Asana and GitLab were considered and rejected: no credential for any of them exists on
this host (`LINEAR_API_KEY`, `JIRA_*`, `ASANA_PAT`, `GITLAB_PAT` are unset in process, user and
machine scopes), and GitLab's adapter is likewise two-state (`opened`/`closed`).

### 3.2 Degraded mode — a permanent, recorded limitation of this deployment

Under `workflows/foreman/STATE_MAPPING.md` §3.1 the third option is taken knowingly:

- the `settled` role is **left unbound**;
- a Run that reaches `RUN_SETTLED` **stays in an active state** (`open`);
- it therefore **keeps consuming turns with no work to do**;
- `RUN_SETTLED` is instead reported in the Durable Execution Record and in the operator workpad
  comment.

This limitation is permanent for this deployment until its tracker gains a third state, and every
readiness report must carry it.

### 3.3 Bindings

| Binding | Value |
|---|---|
| `tracker.kind` | `github` |
| `tracker.provider.repo` | `miannoni/Intercolony` |
| `tracker.provider.api_url` | `https://api.github.com` |
| `tracker.provider.token` | `$GITHUB_TOKEN` — `$VAR_NAME` form, host-side only |
| `tracker.required_labels` | `["foreman-run"]` |
| `queued` → | `open` (active) |
| `executing` → | `open` (active) |
| `settled` → | **unbound** — not representable |
| `complete` → | `closed` (terminal) |
| `abandoned` → | `closed` (terminal) |
| Run work item | `miannoni/Intercolony` issue **#3**, identifier `GH-3` |
| `workspace.root` | `C:/dev/foreman-deployment/workspaces` |

`required_labels` is load-bearing here and not decoration: without it, every other open issue in the
repository is a dispatch candidate. Issue #1 (`hello`) is open and unlabelled, and the poll check
below confirms it is excluded.

No Foreman Slice lifecycle state and no Disposition was mapped to a tracker state; the check for that
is recorded below.

### 3.4 Workspace population — a mount, not a clone

`hooks.after_create` creates Windows directory junctions rather than cloning. BOOTSTRAP Step 3.4
permits "a clone, a copy, or a mount"; here the choice is forced, and getting it wrong would produce
false Evidence rather than a visible failure:

- `<RIMWORLD_INSTALL>/Mods/Intercolony` is a junction to `C:/dev/Intercolony`, so the running game
  loads **that** tree's `Assemblies/Intercolony.dll`. A cloned workspace would build one assembly and
  test another, and the self-test suite would pass against code that was never changed.
- `reference/` (junction to RimWorld's `Data`, plus decompiled sources) and
  `tools/intercolony-dev/dist/` are gitignored and absent from any clone, while `CLAUDE.md` hard
  rule 1 requires grepping `reference/` before writing any RimWorld API.
- The dev bridge is a single loopback listener in a single game process on port 34117. Two populated
  workspaces could not both drive it.

`agent.max_concurrent_agents` is `1`, so there is no second workspace to collide with.

Junction safety was verified empirically before binding, because Symphony's terminal cleanup removes
workspaces recursively: Elixir's `File.rm_rf` on a directory containing a junction removed the
**link** and left the target and its contents intact (test performed 2026-09-05 in the scratchpad,
target file survived). `hooks.before_remove` additionally removes each link explicitly with
`cmd //c rmdir` before any recursive removal reaches it.

The Foreman distribution is mounted read-only in intent at `foreman/`, `workflows/`, `symphony/` and
`foreman-docs/` so that every `foreman/...` path cited in the Supervisor prompt resolves from the
workspace root. The Envelope forbids changing anything under those four paths.

`hooks.before_run` asserts five paths that only a correctly populated workspace has, and its failure
aborts the attempt before the agent launches.

### 3.5 Role-to-model resolution, and the substitution this deployment had to make

`workflows/foreman/DEPLOYMENT_PROFILE.md` is non-normative. Its preferred profile is **Claude Code
with the newest Opus as Supervisor** and **Codex with the newest Luna-family model as Worker**.

**The Supervisor half of that profile is not reachable on the branch BOOTSTRAP prefers.** The bundled
reference runtime launches exactly one coding agent per dispatched work item, over the Codex
app-server JSON-RPC protocol (`lib/symphony_elixir/codex/app_server.ex`, `codex.command` default
`codex app-server`). Claude Code has no app-server mode, so the dispatched Supervisor session must be
Codex. Recorded as a substitution, and filed as a Foreman finding.

| Role | Harness | Model | Effort | How established |
|---|---|---|---|---|
| Supervisor (dispatched session) | Codex CLI 0.149.0, `codex app-server` | `gpt-5.6-sol` | `high` | `~/.codex/config.toml` top-level `model` and `model_reasoning_effort`, which the app server inherits when Symphony passes no override |
| Worker / Evaluator (delegated by the Supervisor) | Supervisor's choice within the session | — | — | delegated role instances, per `workflows/foreman/ROLES.md` |
| Substitution | Claude Code + Opus → Codex + `gpt-5.6-sol` for the Supervisor | | | forced by the execution-layer branch, not a preference |

No model was asked to identify itself. `claude --version` reports 2.1.220 and `codex --version`
reports 0.149.0, both older than the versions recorded in `DEPLOYMENT_PROFILE.md`'s 2026-09-02
resolution; that file records one machine at one time and is explicitly not a pin.

### 3.6 Credentials

`GITHUB_TOKEN` is exported into the orchestrator process only, from `gh auth token`, and is never
written to a repository file. The workflow references it as `$GITHUB_TOKEN`. The GitHub adapter
strips `GITHUB_TOKEN`, `GH_TOKEN`, `GITHUB_ENTERPRISE_TOKEN` and `GH_ENTERPRISE_TOKEN` from the Codex
child environment and executes the `github_api` tool host-side, so the agent never holds the token.
Verified by inspecting the bound adapter's `secret_environment_names`.

---

## Step 4 — Execution Envelope

Permitted, being reversible and confined to the project under work:

- changing Intercolony's sources and configuration on `foreman/playtest-batch-2026-09-05`;
- running its builds and checks, including launching RimWorld through `dev.ps1`;
- obtaining project-scoped dependencies;
- committing that branch and pushing it to `origin`, which is how the record survives workspace
  removal;
- reporting position on **work item GH-3 only**, by changing only its state field and maintaining
  only its single operator workpad comment, through the `github_api` tool.

Reserved to the operator and outside the Envelope: merging to `main` or any other branch; publishing
to the Steam Workshop; creating a release; deleting or rewriting shared history; any change under
`foreman/`, `workflows/`, `symphony/` or `foreman-docs/`; any other work item in any repository;
creating or deleting work items; repository administration; credential or security-boundary changes;
and any other irreversible, shared, external, access-changing or cost-incurring action.

During bootstrap: no credential or security scope was expanded, no destructive external setup was
performed, no system-level runtime was installed, no secret was written into a repository file, and
no sandbox or approval setting was weakened to make delegation easier.

---

## Step 5 — Checks executed

| # | Check | Result |
|---|---|---|
| 1 | Workflow parses; front matter delimiters handled; body returned separately | **PASS** — top-level keys are exactly the six core keys `tracker`, `polling`, `workspace`, `hooks`, `agent`, `codex`; no extra top-level key, so nothing to flag; body returned separately, 15,553 bytes |
| 2 | Strict prompt render, only `issue` and `attempt` | **PASS** — rendered with `attempt` absent (15,023 bytes), `attempt: nil` (identical), and `attempt: 3` (15,505 bytes, continuation block present). A second render with `issue.url` nil exercised the conditional and omitted the reference line. No unrendered tags; no unknown variable or filter |
| 3 | Dispatch preflight (§6.3) | **PASS** — `tracker.kind` = `github`, `tracker.provider` bound and accepted after `$VAR_NAME` resolution, adapter `validate_config` returned `:ok`, effective `codex.command` = `codex app-server`, present and non-empty |
| 4 | State-role bindings, degraded mode | **PASS** — `active_states` = `["open"]`, `terminal_states` = `["closed"]`, `settled` unbound and the limitation recorded; no Foreman lifecycle state leaked into either list |
| 5 | Fresh workspace is populated | **PASS** — a newly created workspace contained `repo/dev.ps1`, `repo/CLAUDE.md`, `repo/Source/Intercolony/Intercolony.csproj`, `repo/docs/DEV_TEST_BRIDGE.md`, `repo/reference/decompiled`, `foreman/SPEC.md`, `workflows/foreman/STATE_MAPPING.md`, `symphony/upstream/SPEC.md` and `foreman-docs/ARCHITECTURE.md`. `before_run` on it returned `:ok` |
| 6 | Reused, unpopulated workspace aborts before the agent launches | **PASS** — a pre-existing directory holding only `leftover.txt` skipped `after_create` as specified, and `before_run` returned `{:error, {:workspace_hook_failed, "before_run", 1, ""}}`. The workspace was **not** destructively reset; `leftover.txt` survived |
| 7 | Execution layer polls the configured provider | **PASS** — read-only poll returned 1 open issue before GH-3 existed (`GH-1`, unlabelled), and the `required_labels` filter left it dispatch-ineligible. Terminal-state fetch returned `GH-2` |
| 8 | Coding-agent launch and app-server handshake | **PASS** — `Codex session started for issue_id=3 issue_identifier=GH-3 session_id=01a072cd-…` at 15:20:55 −03:00, followed by live `item/started`, `item/commandExecution/outputDelta` and `thread/tokenUsage/updated` notifications. Not treated as evidence about any Slice |
| 9 | The target project's own checks | **PASS** — `dotnet build` succeeded, 0 warnings, 0 errors. `dev.ps1 test all -Fresh` returned **1421 passed / 0 failed / 15 skipped**, world-pawn delta 0, postings 0 → 0, log signal CLEAN, exit code 0. The 15 skips are reported as skips, not passes; full output retained at `C:/dev/foreman-deployment/checks/baseline-test-all-fresh.txt`. Lint: **not declared** |
| 10 | Conformance C1–C7 with `--answers` | **NOT PASSED** — see below |

### Check 10 — why the conformance gate is not passed

`node conformance/run.js --answers <file>` exits `0` only when every `REQUIRED` assertion matches.
Answers must be derived from the configured execution layer; BOOTSTRAP forbids fabricating them, and
copying them from each fixture's `expected` block would be exactly that.

The comparison in `conformance/run.js` (`assertionFields`, lines 453–465, used by
`compareFixtureAnswers`, lines 648–687) is a deep strict equality over **every** assertion field
except `id` and `citations`. Across the seven fixtures, 12 of 46 assertions require reproducing
either free-text prose or scenario-invented predicate keys verbatim — for example
`forbidden_by: "an open Slice and a pending Plan Requirement Disposition"`, or a `when` object whose
members include `human_message_precondition`, `coverage_complete`, and per-scenario `why_failed`
sentences. Every one of the seven fixtures contains at least one such assertion.

A blind answering run was performed to measure this rather than assert it; its result is recorded in
`docs/foreman/CONFORMANCE.md` together with the exact mismatches.

Consequently this deployment **does not report the phrase `FOREMAN READY`**. Readiness is reported
qualified: checks 1–9 pass, check 10 is unpassed.

---

## Readiness

**Not `FOREMAN READY`.** Checks 1 through 9 pass, including the target project's own build and its
full in-game suite. Check 10, the C1–C7 `--answers` run, does not pass and is not treatable as
passing.

Pending, and what would be required:

- **C1–C7.** Requires the suite to compare the semantic answer fields rather than free-text and
  scenario-invented predicate members, or requires the schema of each fixture's `when` object to be
  normative and derivable. Filed against Foreman.
- **`RUN_SETTLED` representation.** Permanently unavailable on this tracker; a settled Run will
  remain `open` and keep consuming turns. Carried in every readiness report.

Nothing else is outstanding for the operator to decide or do in order for the Run to proceed.
