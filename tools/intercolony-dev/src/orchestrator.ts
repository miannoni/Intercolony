import { execFile } from "node:child_process";
import { existsSync } from "node:fs";
import { dirname, parse, resolve } from "node:path";
import { fileURLToPath } from "node:url";

import {
  BridgeClient,
  BridgeInfrastructureError,
  BridgeServerCommandError,
} from "./bridge-client.js";
import type {
  PostingCountResult,
  RunAllTestsResult,
  StateSummaryResult,
  StatusResult,
  TestRunResult,
  TestsListResult,
  WorldPawnCountResult,
} from "./protocol.js";

const DEFAULT_READY_TIMEOUT_MS = 5 * 60_000;
const DEFAULT_POLL_INTERVAL_MS = 1_000;
const DEFAULT_TEST_TIMEOUT_MS = 2 * 60_000;
const DEFAULT_ALL_TESTS_TIMEOUT_MS = 10 * 60_000;

export interface TestOptions {
  fresh?: boolean;
  responseTimeoutMs?: number;
  readyTimeoutMs?: number;
  requireMap?: boolean;
}

export interface WaitForReadyOptions {
  timeoutMs?: number;
  requireMap?: boolean;
  pollIntervalMs?: number;
}

export interface FreshWorldOptions extends WaitForReadyOptions {}

export interface TestEnvironment {
  worldLoaded: boolean;
  mapLoaded: boolean;
  openPostingsBefore: number;
  openPostingsAfter: number;
  worldPawnsBefore: number;
  worldPawnsAfter: number;
  worldPawnsDelta: number;
}

export interface TestRunWithEnvironment {
  result: TestRunResult | null;
  environment: TestEnvironment;
  environmentSetupFailure?: string;
}

export interface AllTestsRunWithEnvironment {
  result: RunAllTestsResult;
  environment: TestEnvironment;
}

export class Orchestrator {
  constructor(readonly client = new BridgeClient()) {}

  getStatus(): Promise<StatusResult> {
    return this.client.status();
  }

  listTests(): Promise<TestsListResult> {
    return this.client.listTests();
  }

  async runTest(name: string, options: TestOptions = {}): Promise<TestRunResult> {
    if (options.fresh === true) {
      await this.launchFreshWorld(freshWorldOptions(options));
    }
    return this.client.runTest(name, {
      responseTimeoutMs: options.responseTimeoutMs ?? DEFAULT_TEST_TIMEOUT_MS,
    });
  }

  async runAllTests(options: TestOptions = {}): Promise<RunAllTestsResult> {
    if (options.fresh === true) {
      await this.launchFreshWorld(freshWorldOptions(options));
    }
    return this.client.runAllTests({
      responseTimeoutMs: options.responseTimeoutMs ?? DEFAULT_ALL_TESTS_TIMEOUT_MS,
    });
  }

  stateSummary(): Promise<StateSummaryResult> {
    return this.client.stateSummary();
  }

  worldPawnCount(): Promise<WorldPawnCountResult> {
    return this.client.worldPawnCount();
  }

  postingCount(): Promise<PostingCountResult> {
    return this.client.postingCount();
  }

  async recentLog(): Promise<string> {
    const repoRoot = findRepoRoot();
    const devScript = resolve(repoRoot, "dev.ps1");
    return invokeDevScript(devScript, ["new"]);
  }

  async waitForReady(options: WaitForReadyOptions = {}): Promise<StatusResult> {
    const timeoutMs = positiveValue(options.timeoutMs, DEFAULT_READY_TIMEOUT_MS, "timeoutMs");
    const pollIntervalMs = positiveValue(
      options.pollIntervalMs,
      DEFAULT_POLL_INTERVAL_MS,
      "pollIntervalMs",
    );
    const requireMap = options.requireMap ?? false;
    const deadline = Date.now() + timeoutMs;
    let lastStatus: StatusResult | undefined;
    let lastError: Error | undefined;

    while (true) {
      const requestBudgetMs = deadline - Date.now();
      if (requestBudgetMs <= 0) break;
      try {
        lastStatus = await this.client.status({
          connectTimeoutMs: Math.min(this.client.connectTimeoutMs, requestBudgetMs),
          responseTimeoutMs: Math.min(this.client.responseTimeoutMs, requestBudgetMs),
        });
        lastError = undefined;
        if (lastStatus.worldLoaded && (!requireMap || lastStatus.mapLoaded)) {
          return lastStatus;
        }
      } catch (error) {
        if (!(error instanceof BridgeInfrastructureError || error instanceof BridgeServerCommandError)) {
          throw error;
        }
        lastError = error;
      }

      const remainingMs = deadline - Date.now();
      if (remainingMs <= 0) break;
      await delay(Math.min(pollIntervalMs, remainingMs));
    }

    const worldState = lastStatus === undefined ? "unknown" : lastStatus.worldLoaded ? "ready" : "not ready";
    const mapState = !requireMap
      ? "not required"
      : lastStatus === undefined
        ? "unknown"
        : lastStatus.mapLoaded
          ? "ready"
          : "not ready";
    const lastErrorText = lastError === undefined ? "" : ` Last status error: ${lastError.message}`;
    throw new BridgeInfrastructureError(
      `timed out after ${timeoutMs} ms waiting for RimWorld readiness: world was ${worldState}; map was ${mapState}.${lastErrorText}`,
    );
  }

  async launchFreshWorld(options: FreshWorldOptions = {}): Promise<StatusResult> {
    const repoRoot = findRepoRoot();
    const devScript = resolve(repoRoot, "dev.ps1");
    await invokeDevScript(devScript, ["bridge", "-Fresh"]);
    return this.waitForReady(options);
  }

  async runTestWithEnvironment(
    name: string,
    options: TestOptions = {},
  ): Promise<TestRunWithEnvironment> {
    if (options.fresh === true) {
      await this.launchFreshWorld(freshWorldOptions(options));
    }

    const beforeStatus = await this.getStatus();
    const [beforePawns, beforePostings] = await Promise.all([
      this.worldPawnCount(),
      this.postingCount(),
    ]);

    if (options.fresh === true && beforePostings.open !== 0) {
      const environment = makeEnvironment(
        beforeStatus,
        beforePostings.open,
        beforePostings.open,
        beforePawns.allPawnsAliveOrDead,
        beforePawns.allPawnsAliveOrDead,
      );
      return {
        result: null,
        environment,
        environmentSetupFailure:
          `fresh-world isolation failed: expected 0 open postings before ${name}, found ${beforePostings.open}`,
      };
    }

    const result = await this.client.runTest(name, {
      responseTimeoutMs: options.responseTimeoutMs ?? DEFAULT_TEST_TIMEOUT_MS,
    });
    const [afterPawns, afterPostings] = await Promise.all([
      this.worldPawnCount(),
      this.postingCount(),
    ]);

    return {
      result,
      environment: makeEnvironment(
        beforeStatus,
        beforePostings.open,
        afterPostings.open,
        beforePawns.allPawnsAliveOrDead,
        afterPawns.allPawnsAliveOrDead,
      ),
    };
  }

  async runAllTestsWithEnvironment(
    options: TestOptions = {},
  ): Promise<AllTestsRunWithEnvironment> {
    if (options.fresh === true) {
      await this.launchFreshWorld(freshWorldOptions(options));
    }

    const beforeStatus = await this.getStatus();
    const [beforePawns, beforePostings] = await Promise.all([
      this.worldPawnCount(),
      this.postingCount(),
    ]);
    const result = await this.client.runAllTests({
      responseTimeoutMs: options.responseTimeoutMs ?? DEFAULT_ALL_TESTS_TIMEOUT_MS,
    });
    const [afterPawns, afterPostings] = await Promise.all([
      this.worldPawnCount(),
      this.postingCount(),
    ]);

    return {
      result,
      environment: makeEnvironment(
        beforeStatus,
        beforePostings.open,
        afterPostings.open,
        beforePawns.allPawnsAliveOrDead,
        afterPawns.allPawnsAliveOrDead,
      ),
    };
  }

  fullLogCommand(): string {
    const devScript = resolve(findRepoRoot(), "dev.ps1");
    return `powershell -ExecutionPolicy Bypass -File ${quotePowerShellArgument(devScript)} new`;
  }
}

export function findRepoRoot(moduleUrl = import.meta.url): string {
  let current = dirname(fileURLToPath(moduleUrl));
  const root = parse(current).root;

  while (true) {
    if (
      existsSync(resolve(current, "dev.ps1")) &&
      existsSync(resolve(current, "Source"))
    ) {
      return current;
    }
    if (current === root) break;
    current = dirname(current);
  }

  throw new BridgeInfrastructureError(
    `could not find the Intercolony repository root while walking up from ${dirname(fileURLToPath(moduleUrl))}; expected a directory containing dev.ps1 and Source`,
  );
}

function makeEnvironment(
  status: StatusResult,
  openPostingsBefore: number,
  openPostingsAfter: number,
  worldPawnsBefore: number,
  worldPawnsAfter: number,
): TestEnvironment {
  return {
    worldLoaded: status.worldLoaded,
    mapLoaded: status.mapLoaded,
    openPostingsBefore,
    openPostingsAfter,
    worldPawnsBefore,
    worldPawnsAfter,
    worldPawnsDelta: worldPawnsAfter - worldPawnsBefore,
  };
}

function freshWorldOptions(options: TestOptions): FreshWorldOptions {
  const result: FreshWorldOptions = { requireMap: options.requireMap ?? true };
  if (options.readyTimeoutMs !== undefined) result.timeoutMs = options.readyTimeoutMs;
  return result;
}

function invokeDevScript(devScript: string, args: string[]): Promise<string> {
  const commandText = [
    "powershell",
    "-ExecutionPolicy",
    "Bypass",
    "-File",
    quotePowerShellArgument(devScript),
    ...args,
  ].join(" ");

  return new Promise((resolvePromise, rejectPromise) => {
    execFile(
      "powershell",
      ["-ExecutionPolicy", "Bypass", "-File", devScript, ...args],
      { encoding: "utf8", maxBuffer: 16 * 1024 * 1024 },
      (error, stdout, stderr) => {
        if (error !== null) {
          const detail = stderr.trim() || stdout.trim() || error.message;
          rejectPromise(
            new BridgeInfrastructureError(
              `development command failed: ${commandText}\n${detail}`,
              { cause: error },
            ),
          );
          return;
        }
        resolvePromise(stdout);
      },
    );
  });
}

function quotePowerShellArgument(value: string): string {
  return `'${value.replaceAll("'", "''")}'`;
}

function positiveValue(value: number | undefined, fallback: number, label: string): number {
  const result = value ?? fallback;
  if (!Number.isFinite(result) || result <= 0) {
    throw new BridgeInfrastructureError(`${label} must be a positive number of milliseconds`);
  }
  return result;
}

function delay(milliseconds: number): Promise<void> {
  return new Promise((resolvePromise) => setTimeout(resolvePromise, milliseconds));
}
