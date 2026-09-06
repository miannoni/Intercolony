# The delegated environment for Intercolony

## The constraint

Foreman's readiness gate requires at least one representative check executed through a delegated job, because an operator-run check proves nothing about the environment a Worker gets. For Intercolony that check has to be the in-game suite, and that is the one thing a delegated process could not do.

## What was measured

`codex exec --sandbox workspace-write` does not run its child as the operator. It runs it under a dedicated local account that Codex creates:

| Sandbox | Account | SID suffix |
|---|---|---|
| `workspace-write` | `matteoasus\codexsandboxoffline` | `-1004` |
| `workspace-write` with `sandbox_workspace_write.network_access=true` | `matteoasus\codexsandboxonline` | `-1005` |
| operator | `matteoasus\matte` | `-1001` |

`USERPROFILE` is inherited, so file paths still resolve to `C:\Users\matte` and the delegate can read `Player.log` and write the repository. `HKCU` follows the token, not the environment variable, so `HKCU:\Software\Valve\Steam\ActiveProcess\ActiveUser` is empty for the delegate and `64255983` for the operator.

As `CLAUDE.md`'s existing note on a logged-out Steam client records, with no Steam session RimWorld fails `SteamAPI.Init()`, deactivates every Workshop mod including Harmony, and Intercolony then dies with a `TypeLoadException` before the dev bridge opens. That is exactly the observed `RimWorld exited before the bridge became ready`.

Because that log lives at the operator's profile path, a `Player.log` line showing the bridge listening is not by itself evidence about a delegated run - it may have been written by an earlier operator-run game.

## The decision

Option (b) from `agent-foreman/docs/NEXT_EXTERNAL_DEPLOYMENT.md`: the operator side provisions the game and the delegated job drives the already-running bridge. Chosen by the operator on 2026-09-06, with the explicit constraint that it stay Intercolony-specific and not become a general process-management feature in Foreman. It is therefore implemented entirely in this repository, as `tools/foreman/delegated-game-check.ps1`. Foreman sees only a command and an expected string; no launcher code changed. The scripts live in `tools/foreman/`, alongside `bound-tree-check.ps1`, which proves the Mods junction resolves to this repository so the game loads the assembly this repository builds.

## What the check proves, and what it stopped proving

Proves: the bound tree is what the game loads; a genuinely fresh `-quicktest` world; and that a delegated Worker can run Intercolony's real suite and return a faithful verdict.

Stopped proving: that a Worker can provision the game itself, or recover it if the game dies mid-Run. Both now belong to the operator side. Every game-dependent job in a Run must declare the `rimworld` and `dev-bridge` resources, and delegates must never be given `-Fresh`, `dev.ps1 stop` or `dev.ps1 bridge`.

## Why the check does not trust the delegate's word

### The verdict channel

The delegate's verdict is not parsed out of codex's stdout. `codex exec` is invoked with `--output-schema tools/foreman/delegate-verdict.schema.json` and `-o <invocation-unique file>`, so the agent's final message is written, alone, to a file the wrapper names per run with a fresh GUID. The wrapper reads and parses that JSON. stdout remains only a human diagnostic stream. The path being invocation-unique matters: a leftover file from an earlier run cannot satisfy it, and the wrapper deletes it again in its `finally` block.

The schema's shape is `result` (`OK` or `FAILED`), `exitCode`, `passed`, `failed`, `skipped`, and `reason`.

`exitCode` must be 0 rather than being scraped from a console line because `dev.ps1 test` returns 0 only when assertions passed and the run gained no new `Player.log` exceptions; 1 on a failed assertion; and 2 on a clean-assertions run whose log gained exceptions. Exit code 0 therefore carries the log signal.

A delegate could report OK without running anything, so the verdict is necessary but not sufficient. The script requires this independent evidence:

- the raw suite output artifact must exist;
- it must be newer than the moment the job was dispatched;
- it must carry an `N passed, 0 failed.` summary with a non-zero pass count;
- it must contain no anchored `FAIL` assertion line;
- the delegate's reported `exitCode` must be 0; and
- the delegate's reported counts must agree with the artifact.

The counts cross-check is skipped when the delegate reports `-1`, the sentinel for a value it genuinely could not read, which is what a bridge failure produces.

The reasons stdout was abandoned as a transport were three traps. Each cost a check that could never pass or, worse, one that flaked:

- `codex exec` echoes its prompt into stdout, so a substring scan for the failure marker matched the instructions on a successful run.
- codex may append footer lines after the agent's final message. An observed successful run ended `INTERCOLONY_DELEGATED_CHECK_OK` / `tokens used` / `19,397`, so a last-non-empty-line rule read `19,397` and failed a run that was actually 28 passed, 0 failed. This one is worse than the first because it is intermittent: the same rule had passed on earlier runs.
- `dev.ps1` writes only the suite's raw output to the artifact; `Test signal:` and `Log signal:` are `Write-Host` console lines and never reach the file.

The first two are both consequences of treating a human-readable stream as a machine interface, and the fix was to stop doing that rather than to match harder.

## Evidence

| Run | Result | Basis |
|---|---|---|
| delegated suite against an operator-provisioned bridge, four independent runs | PASS | `{"result":"OK","exitCode":0,"passed":28,"failed":0,"skipped":0}` each time |
| the same delegated command with no bridge running | FAIL | `result FAILED`, `exitCode 1`, reason `Bridge connection failed; target 127.0.0.1:34117 actively refused.`, counts `-1` |
| one assertion mutated in `IntercolonyJobPostingSelfTest.cs` | FAIL | 27 passed, 1 failed; reason named the assertion: the census is taken once per cycle, not once per question |
| `foreman readiness` with that mutation in place | FAIL | exit 2, `READINESS NOT ESTABLISHED`, failed REQUIRED check `target.delegated-game` |
| `foreman run` from that state | REFUSED | exit 2, `REFUSING TO RUN: READINESS NOT ESTABLISHED` |
| `foreman readiness` with the mutation reverted | PASS | `FOREMAN READY`, all eight checks green |
| `bound-tree-check.ps1` against the real junction | PASS | same size and SHA256 on both sides |
| `bound-tree-check.ps1` against a real directory, and against a missing path | FAIL | each with its own reason |

Every check has been observed green and observed red for the right reason, and the four consecutive green delegated runs are what establishes stability - the transport this replaced flaked on its second run of three.
