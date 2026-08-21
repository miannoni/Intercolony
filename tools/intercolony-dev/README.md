# Intercolony RimWorld development client

This package connects external development tools to the opt-in Intercolony test bridge inside RimWorld. It provides a shared TypeScript client/orchestrator, a PowerShell-friendly CLI, and an MCP server over stdio. The MCP process does not listen on a TCP port; only RimWorld owns the loopback bridge listener.

## Requirements

- Node.js 24.14.0 or newer
- A bridge-enabled Intercolony build
- RimWorld launched with `INTERCOLONY_DEV_BRIDGE=1`

From the repository root:

```powershell
npm --prefix 'tools/intercolony-dev' install
npm --prefix 'tools/intercolony-dev' run build
npm --prefix 'tools/intercolony-dev' run ic -- status
```

The bridge address is always `127.0.0.1`. Its port resolves in this order: an explicit library option, `INTERCOLONY_DEV_BRIDGE_PORT`, then `34117`.

## CLI

```text
npm --prefix tools/intercolony-dev run ic -- status [--json]
npm --prefix tools/intercolony-dev run ic -- tests list [--json]
npm --prefix tools/intercolony-dev run ic -- tests run <name> [--fresh] [--json]
npm --prefix tools/intercolony-dev run ic -- tests all [--fresh] [--json]
npm --prefix tools/intercolony-dev run ic -- state [--json]
npm --prefix tools/intercolony-dev run ic -- pawns [--json]
npm --prefix tools/intercolony-dev run ic -- postings [--json]
npm --prefix tools/intercolony-dev run ic -- log [--json]
```

Exit codes are stable for automation:

- `0`: command succeeded, or tests had no failures
- `1`: a test result reported failures
- `2`: connection, protocol, command, launch, or environment-setup failure

`--fresh` delegates to `powershell -ExecutionPolicy Bypass -File <repo>\dev.ps1 bridge -Fresh` and polls bridge status until the world and map are ready. It never substitutes a non-fresh launch. The `job-posting` scenario also captures pawn and posting counts before and after execution; a fresh run with pre-existing open postings is rejected as an environment-setup failure.

## MCP server

Build first, then configure an MCP client to run:

```text
npm --prefix <repo>\tools\intercolony-dev run mcp
```

The stdio server exposes exactly these tools:

- `rimworld_status`
- `rimworld_list_self_tests`
- `rimworld_run_self_test`
- `rimworld_run_all_self_tests`
- `rimworld_state_summary`
- `rimworld_world_pawn_count`
- `rimworld_posting_count`
- `rimworld_recent_log`

Large state, log, and test-output fields are truncated in MCP responses. Each truncated response includes a command that retrieves the full text.

## Library layout

- `protocol.ts` defines the wire contract and narrow runtime parsers.
- `bridge-client.ts` owns one-request-per-connection TCP exchange and typed commands.
- `orchestrator.ts` owns readiness polling, fresh-world launch, log retrieval, and environment capture.
- `cli.ts` and `mcp.ts` are presentation adapters over the same orchestrator.

The package uses ESM and TypeScript `NodeNext` resolution so emitted imports match the official MCP SDK's ESM exports.
