# Bootstrap record - Intercolony

Run `run-2026-09-06-intercolony`, recorded 2026-09-06.

## Environment

| Item | Observed | How |
| --- | --- | --- |
| OS | Microsoft Windows 11 Home 10.0.26200.0 | `(Get-CimInstance Win32_OperatingSystem).Caption` |
| Shell | Windows PowerShell 5.1 | host |
| node | v24.14.0 | `node --version` |
| Claude Code | 2.1.220 | `claude --version` |
| Codex CLI | codex-cli 0.153.4 | `codex --version` |
| git | 2.39.1.windows.1 | `git --version` |

## Repository

| Item | Observed |
| --- | --- |
| remote | `https://github.com/miannoni/Intercolony.git` |
| development line | `1.0.1`, which is 46 commits ahead of `main` and contains it |
| working branch | `foreman/playtest-batch-run2`, created off `1.0.1` |
| working tree at bootstrap | clean apart from one pre-existing untracked file the operator owns |

The branch was cut fresh off `1.0.1`, and the previous Foreman Run's branch, decomposition, Contracts and Candidates were deliberately not reused, per `agent-foreman/docs/NEXT_EXTERNAL_DEPLOYMENT.md`.

## The target's own rules

`CLAUDE.md` was read and governs. The four constraints that bear on delegation are: never invent a RimWorld API without grepping `reference/` first; `DESIGN.md` is design intent, not an API reference; one vertical slice at a time; and every feature must be tested for save/load and across games, not only across loads.

## Real build and check commands

The real build and check commands are taken from `CLAUDE.md` and `dev.ps1`, never invented:

| Purpose | Command |
| --- | --- |
| build | `dotnet build Source/Intercolony/Intercolony.csproj` |
| in-game suite, one test | `powershell -ExecutionPolicy Bypass -File dev.ps1 test <id>` |
| in-game suite, clean world | `powershell -ExecutionPolicy Bypass -File dev.ps1 test <id> -Fresh` |
| whole suite | `dev.ps1 test all -Fresh` |
| lint | none declared |

Test ids come from `tests.list`, not from display names, and `dev.ps1 test` returns 0 clean, 1 assertions failed, 2 everything else including a run whose assertions passed but whose log gained new exceptions.

## Single-instance resources

Intercolony's verification drives a real game process and a fixed TCP port, so both are declared exclusive in `.foreman/deployment.json`:

- `rimworld` - `no-process` assertion on `RimWorldWin64.exe`
- `dev-bridge` - `no-listener` assertion on port 34117

## How the work sees the repository

Mode 1, the bound working tree, not a git worktree, is used. RimWorld loads the mod through `<RIMWORLD_INSTALL>\Mods\Intercolony`, a junction to `C:\dev\Intercolony`, and `reference/` and `tools/intercolony-dev/dist/` are gitignored and absent from a clone. A clone would build one artifact and test another while every check still passed. `tools/foreman/bound-tree-check.ps1` is a REQUIRED readiness check for exactly this reason.

## Readiness

`foreman readiness` printed `FOREMAN READY` with all eight checks green. The check ids are: `role.supervisor`, `role.worker`, `role.evaluator`, `target.git`, `record.writable`, `target.build`, `target.bound-tree`, `target.delegated-game`. The gate was proven fail-closed by mutating a real assertion, which produced exit 2 and made `foreman run` refuse, and the mutation was then reverted. Full evidence is in `docs/FOREMAN_DELEGATED_ENVIRONMENT.md`.
