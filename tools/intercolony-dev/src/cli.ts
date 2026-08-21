#!/usr/bin/env node

import {
  BridgeInfrastructureError,
  BridgeServerCommandError,
  BridgeTestFailureError,
} from "./bridge-client.js";
import { Orchestrator, type TestEnvironment } from "./orchestrator.js";
import type { RunAllTestsResult, TestRunResult } from "./protocol.js";

interface ParsedArguments {
  positionals: string[];
  json: boolean;
  fresh: boolean;
}

async function main(argv: string[]): Promise<number> {
  let parsed: ParsedArguments;
  try {
    parsed = parseArguments(argv);
  } catch (error) {
    process.stderr.write(`${errorMessage(error)}\n\n${usage()}\n`);
    return 2;
  }

  const orchestrator = new Orchestrator();
  const [command, subcommand, name, ...extra] = parsed.positionals;

  try {
    if (extra.length > 0) throw new Error(`unexpected argument: ${extra[0]}`);

    if (command === "status" && subcommand === undefined) {
      rejectFresh(parsed);
      const result = await orchestrator.getStatus();
      print(result, parsed.json, formatKeyValues);
      return 0;
    }

    if (command === "tests" && subcommand === "list" && name === undefined) {
      rejectFresh(parsed);
      const result = await orchestrator.listTests();
      print(
        result,
        parsed.json,
        (value) => value.tests.map((test) => `${test.id}\t${test.requiresMap ? "map" : "world"}\t${test.label}`).join("\n"),
      );
      return 0;
    }

    if (command === "tests" && subcommand === "run" && name !== undefined) {
      if (name === "job-posting") {
        const run = await orchestrator.runTestWithEnvironment(name, { fresh: parsed.fresh });
        if (run.environmentSetupFailure !== undefined) {
          printError(
            parsed.json,
            "environment-setup",
            run.environmentSetupFailure,
            { environment: run.environment },
          );
          return 2;
        }
        if (run.result === null) {
          throw new BridgeInfrastructureError("job-posting test returned no result");
        }
        print(
          { result: run.result, environment: run.environment },
          parsed.json,
          (value) => `${formatTest(value.result)}\n${formatEnvironment(value.environment)}`,
        );
        return testExitCode(run.result);
      }

      const result = await orchestrator.runTest(name, { fresh: parsed.fresh });
      print(result, parsed.json, formatTest);
      return testExitCode(result);
    }

    if (command === "tests" && subcommand === "all" && name === undefined) {
      const result = await orchestrator.runAllTests({ fresh: parsed.fresh });
      print(result, parsed.json, formatAllTests);
      return suiteExitCode(result);
    }

    if (command === "state" && subcommand === undefined) {
      rejectFresh(parsed);
      const result = await orchestrator.stateSummary();
      print(result, parsed.json, (value) => value.summary);
      return 0;
    }

    if (command === "pawns" && subcommand === undefined) {
      rejectFresh(parsed);
      const result = await orchestrator.worldPawnCount();
      print(result, parsed.json, formatKeyValues);
      return 0;
    }

    if (command === "postings" && subcommand === undefined) {
      rejectFresh(parsed);
      const result = await orchestrator.postingCount();
      print(result, parsed.json, formatKeyValues);
      return 0;
    }

    if (command === "log" && subcommand === undefined) {
      rejectFresh(parsed);
      const result = await orchestrator.recentLog();
      print(result, parsed.json, (value) => value);
      return 0;
    }

    throw new Error(`unknown or incomplete command: ${parsed.positionals.join(" ") || "(none)"}`);
  } catch (error) {
    if (error instanceof BridgeTestFailureError) {
      printError(parsed.json, error.kind, error.message);
      return 1;
    }
    const kind =
      error instanceof BridgeInfrastructureError
        ? error.kind
        : error instanceof BridgeServerCommandError
          ? error.kind
          : "setup";
    printError(parsed.json, kind, errorMessage(error));
    if (!(error instanceof BridgeInfrastructureError || error instanceof BridgeServerCommandError)) {
      process.stderr.write(parsed.json ? "" : `${usage()}\n`);
    }
    return 2;
  }
}

function parseArguments(argv: string[]): ParsedArguments {
  const positionals: string[] = [];
  let json = false;
  let fresh = false;

  for (const argument of argv) {
    if (argument === "--json") json = true;
    else if (argument === "--fresh") fresh = true;
    else if (argument.startsWith("--")) throw new Error(`unknown option: ${argument}`);
    else positionals.push(argument);
  }
  return { positionals, json, fresh };
}

function rejectFresh(parsed: ParsedArguments): void {
  if (parsed.fresh) throw new Error("--fresh is only valid with 'tests run' or 'tests all'");
}

function print<T>(value: T, json: boolean, formatter: (value: T) => string): void {
  const text = json ? JSON.stringify(value, null, 2) : formatter(value);
  process.stdout.write(`${text.endsWith("\n") ? text : `${text}\n`}`);
}

function printError(
  json: boolean,
  kind: string,
  message: string,
  details?: Record<string, unknown>,
): void {
  if (json) {
    process.stderr.write(`${JSON.stringify({ ok: false, errorKind: kind, message, ...details }, null, 2)}\n`);
  } else {
    process.stderr.write(`Error: ${message}\n`);
    if (details?.environment !== undefined) {
      process.stderr.write(`${formatEnvironment(details.environment as TestEnvironment)}\n`);
    }
  }
}

function formatKeyValues(value: object): string {
  return Object.entries(value)
    .map(([key, field]) => `${key}: ${formatScalar(field)}`)
    .join("\n");
}

function formatTest(result: TestRunResult): string {
  const status = result.success && result.failed === 0 ? "PASS" : "FAIL";
  const lines = [
    `${status} ${result.id} — ${result.label}`,
    `passed: ${result.passed}; failed: ${result.failed}; skipped: ${result.skipped}; duration: ${result.durationMs} ms`,
  ];
  if (result.preconditionError) lines.push(`precondition: ${result.preconditionError}`);
  if (result.exceptionText) lines.push(`exception: ${result.exceptionText}`);
  if (result.output) lines.push(result.output);
  return lines.join("\n");
}

function formatAllTests(result: RunAllTestsResult): string {
  const status = result.success && result.failed === 0 ? "PASS" : "FAIL";
  const lines = [
    `${status} all self-tests`,
    `success: ${result.success}; clean: ${result.clean}`,
    `passed: ${result.passed}; failed: ${result.failed}; skipped: ${result.skipped}; duration: ${result.durationMs} ms`,
    ...result.tests.map((test) => `${test.success && test.failed === 0 ? "PASS" : "FAIL"} ${test.id}`),
  ];
  if (result.output) lines.push(result.output);
  return lines.join("\n");
}

function formatEnvironment(environment: TestEnvironment): string {
  return [
    "environment:",
    `  worldLoaded: ${environment.worldLoaded}`,
    `  mapLoaded: ${environment.mapLoaded}`,
    `  openPostings: ${environment.openPostingsBefore} -> ${environment.openPostingsAfter}`,
    `  worldPawns: ${environment.worldPawnsBefore} -> ${environment.worldPawnsAfter} (delta ${environment.worldPawnsDelta})`,
  ].join("\n");
}

function formatScalar(value: unknown): string {
  return typeof value === "string" ? value : JSON.stringify(value);
}

function testExitCode(result: TestRunResult): 0 | 1 {
  if (result.failed === 0 && result.success) return 0;
  const failure = new BridgeTestFailureError([result.id], result.failed);
  void failure;
  return 1;
}

function suiteExitCode(result: RunAllTestsResult): 0 | 1 {
  if (result.failed === 0 && result.success) return 0;
  const failureIds = result.tests
    .filter((test) => test.failed > 0 || !test.success)
    .map((test) => test.id);
  const failure = new BridgeTestFailureError(failureIds, result.failed);
  void failure;
  return 1;
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

function usage(): string {
  return [
    "Usage:",
    "  ic status [--json]",
    "  ic tests list [--json]",
    "  ic tests run <name> [--fresh] [--json]",
    "  ic tests all [--fresh] [--json]",
    "  ic state [--json]",
    "  ic pawns [--json]",
    "  ic postings [--json]",
    "  ic log [--json]",
  ].join("\n");
}

const exitCode = await main(process.argv.slice(2));
process.exitCode = exitCode;
