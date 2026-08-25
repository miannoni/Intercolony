#!/usr/bin/env node

import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import {
  CallToolRequestSchema,
  ListToolsRequestSchema,
  type CallToolResult,
} from "@modelcontextprotocol/sdk/types.js";

import { Orchestrator, type TestEnvironment } from "./orchestrator.js";
import type { RunAllTestsResult, TestRunResult } from "./protocol.js";

const TEXT_BUDGET = 4_096;
const NO_ARGUMENTS_SCHEMA = {
  type: "object",
  additionalProperties: false,
  properties: {},
} as const;
const FRESH_ARGUMENT_SCHEMA = {
  type: "object",
  additionalProperties: false,
  properties: {
    fresh: { type: "boolean", default: false },
  },
} as const;
const TEST_ARGUMENT_SCHEMA = {
  type: "object",
  additionalProperties: false,
  required: ["name"],
  properties: {
    name: { type: "string", minLength: 1 },
    fresh: { type: "boolean", default: false },
  },
} as const;

const TOOL_DEFINITIONS = [
  tool("rimworld_status", "Show RimWorld bridge, world, and map readiness.", NO_ARGUMENTS_SCHEMA),
  tool("rimworld_list_self_tests", "List Intercolony self-tests exposed by RimWorld.", NO_ARGUMENTS_SCHEMA),
  tool("rimworld_run_self_test", "Run one Intercolony self-test, optionally in a fresh world.", TEST_ARGUMENT_SCHEMA),
  tool("rimworld_run_all_self_tests", "Run all Intercolony self-tests, optionally in a fresh world.", FRESH_ARGUMENT_SCHEMA),
  tool("rimworld_state_summary", "Return the Intercolony in-game state summary.", NO_ARGUMENTS_SCHEMA),
  tool("rimworld_world_pawn_count", "Count world pawns known to RimWorld.", NO_ARGUMENTS_SCHEMA),
  tool("rimworld_posting_count", "Count all and open Intercolony postings.", NO_ARGUMENTS_SCHEMA),
  tool("rimworld_recent_log", "Return the recent RimWorld log through dev.ps1.", NO_ARGUMENTS_SCHEMA),
];

export async function startMcpServer(): Promise<void> {
  const server = new Server(
    { name: "intercolony-rimworld-dev", version: "0.1.0" },
    {
      capabilities: { tools: {} },
      instructions: "Use these tools to inspect and run Intercolony tests in a bridge-enabled RimWorld process.",
    },
  );
  const orchestrator = new Orchestrator();

  server.setRequestHandler(ListToolsRequestSchema, async () => ({
    tools: TOOL_DEFINITIONS.map((definition) => ({ ...definition })),
  }));

  server.setRequestHandler(CallToolRequestSchema, async (request): Promise<CallToolResult> => {
    try {
      const args = requireArguments(request.params.arguments);
      switch (request.params.name) {
        case "rimworld_status":
          requireNoArguments(args);
          return successResult(await orchestrator.getStatus());

        case "rimworld_list_self_tests": {
          requireNoArguments(args);
          const result = await orchestrator.listTests();
          return successResult({ tests: result.tests });
        }

        case "rimworld_run_self_test": {
          requireOnlyArguments(args, ["name", "fresh"]);
          const name = requireStringArgument(args, "name");
          const fresh = optionalBooleanArgument(args, "fresh", false);
          const run = await orchestrator.runTestWithEnvironment(name, { fresh });
          if (run.environmentSetupFailure !== undefined) {
            return errorResult({
              errorType: "environment-setup",
              message: run.environmentSetupFailure,
              environment: run.environment,
              fullLogCommand: orchestrator.fullLogCommand(),
            });
          }
          if (run.result === null) {
            return errorResult({
              errorType: "protocol",
              message: `test ${name} returned no result`,
              environment: run.environment,
              fullLogCommand: orchestrator.fullLogCommand(),
            });
          }
          return formatSingleTestResult(run.result, run.environment, orchestrator, name);
        }

        case "rimworld_run_all_self_tests": {
          requireOnlyArguments(args, ["fresh"]);
          const fresh = optionalBooleanArgument(args, "fresh", false);
          const run = await orchestrator.runAllTestsWithEnvironment({ fresh });
          return formatAllTestsResult(run.result, run.environment, orchestrator);
        }

        case "rimworld_state_summary": {
          requireNoArguments(args);
          const result = await orchestrator.stateSummary();
          const fullCommand = cliCommand(orchestrator, "state --json");
          return successResult({ summary: truncate(result.summary, TEXT_BUDGET, fullCommand) });
        }

        case "rimworld_world_pawn_count":
          requireNoArguments(args);
          return successResult(await orchestrator.worldPawnCount());

        case "rimworld_posting_count":
          requireNoArguments(args);
          return successResult(await orchestrator.postingCount());

        case "rimworld_recent_log": {
          requireNoArguments(args);
          const log = await orchestrator.recentLog();
          return successResult({
            log: truncate(log, TEXT_BUDGET, orchestrator.fullLogCommand()),
          });
        }

        default:
          return errorResult({
            errorType: "unknown-tool",
            message: `unknown tool: ${request.params.name}`,
          });
      }
    } catch (error) {
      return errorResult({
        errorType: error instanceof Error ? error.name : "Error",
        message: errorMessage(error),
        fullLogCommand: safeFullLogCommand(orchestrator),
      });
    }
  });

  await server.connect(new StdioServerTransport());
}

function formatSingleTestResult(
  result: TestRunResult,
  environment: TestEnvironment,
  orchestrator: Orchestrator,
  requestedName: string,
): CallToolResult {
  const failed = result.failed > 0 || !result.success;
  const fullTestCommand = cliCommand(
    orchestrator,
    `tests run ${quotePowerShellArgument(requestedName)} --json`,
  );
  if (failed) {
    const assertions = failLines(result.output, TEXT_BUDGET);
    return errorResult({
      failingTestId: result.id,
      counts: counts(result),
      failingAssertionLines: assertions.lines,
      failingAssertionLinesTruncated: assertions.truncated,
      exceptionText: truncateOptional(result.exceptionText, TEXT_BUDGET, fullTestCommand),
      preconditionError: result.preconditionError ?? null,
      environment,
      fullTestCommand,
      fullLogCommand: orchestrator.fullLogCommand(),
    });
  }

  return successResult({
    testId: result.id,
    label: result.label,
    success: result.success,
    counts: counts(result),
    durationMs: result.durationMs,
    environment,
    output: truncate(result.output, TEXT_BUDGET, fullTestCommand),
  });
}

function formatAllTestsResult(
  result: RunAllTestsResult,
  environment: TestEnvironment,
  orchestrator: Orchestrator,
): CallToolResult {
  const failedTests = result.tests.filter((test) => test.failed > 0 || !test.success);
  const fullTestCommand = cliCommand(orchestrator, "tests all --json");
  if (result.failed > 0 || !result.success) {
    return errorResult({
      success: result.success,
      clean: result.clean,
      counts: counts(result),
      failingTests: failedTests.slice(0, 10).map((test) => {
        const assertions = failLines(test.output, 512);
        return {
          failingTestId: test.id,
          counts: counts(test),
          failingAssertionLines: assertions.lines,
          failingAssertionLinesTruncated: assertions.truncated,
          exceptionText: truncateOptional(test.exceptionText, 256, fullTestCommand),
          preconditionError: test.preconditionError ?? null,
        };
      }),
      failingTestsTruncated: Math.max(0, failedTests.length - 10),
      suiteFailingAssertionLines: failLines(result.output, 1_024).lines,
      environment,
      fullTestCommand,
      fullLogCommand: orchestrator.fullLogCommand(),
    });
  }

  return successResult({
    success: result.success,
    clean: result.clean,
    counts: counts(result),
    durationMs: result.durationMs,
    tests: result.tests.map((test) => ({ id: test.id, success: test.success, ...counts(test) })),
    environment,
    output: truncate(result.output, TEXT_BUDGET, fullTestCommand),
  });
}

function successResult(value: unknown): CallToolResult {
  return {
    content: [{ type: "text", text: JSON.stringify({ ok: true, ...asObject(value) }, null, 2) }],
  };
}

function errorResult(value: unknown): CallToolResult {
  return {
    isError: true,
    content: [{ type: "text", text: JSON.stringify({ ok: false, ...asObject(value) }, null, 2) }],
  };
}

function asObject(value: unknown): Record<string, unknown> {
  if (typeof value === "object" && value !== null && !Array.isArray(value)) {
    return value as Record<string, unknown>;
  }
  return { value };
}

function counts(value: { passed: number; failed: number; skipped: number }): Record<string, number> {
  return { passed: value.passed, failed: value.failed, skipped: value.skipped };
}

function failLines(output: string, budget: number): { lines: string[]; truncated: boolean } {
  const matching = output.split(/\r?\n/u).filter((line) => /FAIL/iu.test(line));
  const lines: string[] = [];
  let remaining = budget;

  for (const line of matching) {
    if (remaining <= 0 || lines.length >= 100) break;
    if (line.length <= remaining) {
      lines.push(line);
      remaining -= line.length;
    } else {
      lines.push(`${line.slice(0, Math.max(0, remaining - 15))}...[truncated]`);
      remaining = 0;
    }
  }

  return { lines, truncated: lines.length < matching.length || remaining === 0 };
}

function truncate(value: string, budget: number, fullCommand: string): string {
  if (value.length <= budget) return value;

  let omitted = value.length;
  let note = "";
  let prefixLength = 0;
  do {
    note = `\n...[truncated ${omitted} characters; full text: ${fullCommand}]`;
    prefixLength = Math.max(0, budget - note.length);
    const nextOmitted = value.length - prefixLength;
    if (nextOmitted === omitted) break;
    omitted = nextOmitted;
  } while (true);

  return `${value.slice(0, prefixLength)}${note}`;
}

function truncateOptional(
  value: string | null | undefined,
  budget: number,
  fullCommand: string,
): string | null {
  return value == null ? null : truncate(value, budget, fullCommand);
}

function requireArguments(value: Record<string, unknown> | undefined): Record<string, unknown> {
  if (value === undefined) return {};
  return value;
}

function requireNoArguments(args: Record<string, unknown>): void {
  if (Object.keys(args).length !== 0) throw new Error("this tool does not accept arguments");
}

function requireOnlyArguments(args: Record<string, unknown>, allowed: string[]): void {
  const unknown = Object.keys(args).filter((key) => !allowed.includes(key));
  if (unknown.length > 0) throw new Error(`unknown argument${unknown.length === 1 ? "" : "s"}: ${unknown.join(", ")}`);
}

function requireStringArgument(args: Record<string, unknown>, name: string): string {
  const value = args[name];
  if (typeof value !== "string" || value.trim().length === 0) {
    throw new Error(`${name} must be a non-empty string`);
  }
  return value;
}

function optionalBooleanArgument(
  args: Record<string, unknown>,
  name: string,
  defaultValue: boolean,
): boolean {
  const value = args[name];
  if (value === undefined) return defaultValue;
  if (typeof value !== "boolean") throw new Error(`${name} must be a boolean`);
  return value;
}

function tool(
  name: string,
  description: string,
  inputSchema: Record<string, unknown>,
): { name: string; description: string; inputSchema: Record<string, unknown> } {
  return { name, description, inputSchema };
}

function cliCommand(orchestrator: Orchestrator, args: string): string {
  const logCommand = orchestrator.fullLogCommand();
  const match = /-File\s+'([^']+)'\s+new$/u.exec(logCommand);
  const repoRoot = match?.[1]?.replace(/\\dev\.ps1$/iu, "") ?? ".";
  const cliPath = `${repoRoot}\\tools\\intercolony-dev\\dist\\cli.js`;
  return `node ${quotePowerShellArgument(cliPath)} ${args}`;
}

function safeFullLogCommand(orchestrator: Orchestrator): string | undefined {
  try {
    return orchestrator.fullLogCommand();
  } catch {
    return undefined;
  }
}

function quotePowerShellArgument(value: string): string {
  return `'${value.replaceAll("'", "''")}'`;
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

await startMcpServer().catch((error: unknown) => {
  process.stderr.write(`Intercolony MCP server failed to start: ${errorMessage(error)}\n`);
  process.exitCode = 2;
});
