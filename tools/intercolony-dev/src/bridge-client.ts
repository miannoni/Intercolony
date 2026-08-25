import { randomUUID } from "node:crypto";
import { Socket } from "node:net";
import { TextDecoder } from "node:util";

import {
  type BridgeCommand,
  type BridgeRequest,
  parseBridgeResponse,
  type PostingCountResult,
  parsePostingCountResult,
  type ResultParser,
  type RunAllTestsResult,
  parseRunAllTestsResult,
  type StateSummaryResult,
  parseStateSummaryResult,
  type StatusResult,
  parseStatusResult,
  type TestRunResult,
  parseTestRunResult,
  type TestsListResult,
  parseTestsListResult,
  type WorldPawnCountResult,
  parseWorldPawnCountResult,
} from "./protocol.js";

const HOST = "127.0.0.1";
const DEFAULT_PORT = 34_117;
const DEFAULT_CONNECT_TIMEOUT_MS = 3_000;
const DEFAULT_RESPONSE_TIMEOUT_MS = 30_000;
const MAX_REQUEST_BYTES = 64 * 1024;
const MAX_RESPONSE_BYTES = 16 * 1024 * 1024;

export interface BridgeClientOptions {
  port?: number;
  connectTimeoutMs?: number;
  responseTimeoutMs?: number;
}

export interface RequestOptions {
  connectTimeoutMs?: number;
  responseTimeoutMs?: number;
}

export class BridgeInfrastructureError extends Error {
  readonly kind = "infrastructure" as const;

  constructor(message: string, options?: ErrorOptions) {
    super(message, options);
    this.name = "BridgeInfrastructureError";
  }
}

export class BridgeServerCommandError extends Error {
  readonly kind = "server-command" as const;

  constructor(
    readonly command: BridgeCommand,
    readonly serverMessage: string,
  ) {
    super(`RimWorld bridge rejected ${command}: ${serverMessage}`);
    this.name = "BridgeServerCommandError";
  }
}

// Test failures are intentionally not thrown by BridgeClient. This exported type is
// available to presentation layers that want an Error object while preserving the
// distinction from transport and server-command failures.
export class BridgeTestFailureError extends Error {
  readonly kind = "test-failure" as const;

  constructor(
    readonly testIds: string[],
    readonly failedCount: number,
  ) {
    super(
      `${failedCount} test assertion${failedCount === 1 ? "" : "s"} failed` +
        (testIds.length > 0 ? ` (${testIds.join(", ")})` : ""),
    );
    this.name = "BridgeTestFailureError";
  }
}

export class BridgeClient {
  readonly port: number;
  readonly connectTimeoutMs: number;
  readonly responseTimeoutMs: number;

  constructor(options: BridgeClientOptions = {}) {
    this.port = resolvePort(options.port);
    this.connectTimeoutMs = positiveTimeout(
      options.connectTimeoutMs,
      DEFAULT_CONNECT_TIMEOUT_MS,
      "connectTimeoutMs",
    );
    this.responseTimeoutMs = positiveTimeout(
      options.responseTimeoutMs,
      DEFAULT_RESPONSE_TIMEOUT_MS,
      "responseTimeoutMs",
    );
  }

  status(options?: RequestOptions): Promise<StatusResult> {
    return this.request("status", {}, parseStatusResult, options);
  }

  listTests(options?: RequestOptions): Promise<TestsListResult> {
    return this.request("tests.list", {}, parseTestsListResult, options);
  }

  runTest(name: string, options?: RequestOptions): Promise<TestRunResult> {
    return this.request("tests.run", { name }, parseTestRunResult, options);
  }

  runAllTests(options?: RequestOptions): Promise<RunAllTestsResult> {
    return this.request("tests.run_all", {}, parseRunAllTestsResult, options);
  }

  stateSummary(options?: RequestOptions): Promise<StateSummaryResult> {
    return this.request("state.summary", {}, parseStateSummaryResult, options);
  }

  worldPawnCount(options?: RequestOptions): Promise<WorldPawnCountResult> {
    return this.request("world_pawns.count", {}, parseWorldPawnCountResult, options);
  }

  postingCount(options?: RequestOptions): Promise<PostingCountResult> {
    return this.request("postings.count", {}, parsePostingCountResult, options);
  }

  private async request<T>(
    command: BridgeCommand,
    args: Record<string, unknown>,
    parser: ResultParser<T>,
    options: RequestOptions = {},
  ): Promise<T> {
    const id = randomUUID().replaceAll("-", "").slice(0, 16);
    const request: BridgeRequest<Record<string, unknown>> = { id, command, args };
    const line = `${JSON.stringify(request)}\n`;
    const requestBytes = Buffer.byteLength(line, "utf8");
    if (requestBytes > MAX_REQUEST_BYTES) {
      throw new BridgeInfrastructureError(
        `request for ${command} is ${requestBytes} bytes; the bridge limit is ${MAX_REQUEST_BYTES} bytes`,
      );
    }

    const connectTimeoutMs = positiveTimeout(
      options.connectTimeoutMs,
      this.connectTimeoutMs,
      "connectTimeoutMs",
    );
    const responseTimeoutMs = positiveTimeout(
      options.responseTimeoutMs,
      this.responseTimeoutMs,
      "responseTimeoutMs",
    );

    const socket = new Socket();
    try {
      const responseLine = await exchangeOneLine(
        socket,
        line,
        this.port,
        connectTimeoutMs,
        responseTimeoutMs,
      );

      let decoded: unknown;
      try {
        decoded = JSON.parse(responseLine) as unknown;
      } catch (error) {
        throw new BridgeInfrastructureError(
          `protocol error from ${HOST}:${this.port}: response was not valid JSON`,
          { cause: error },
        );
      }

      let response;
      try {
        response = parseBridgeResponse(decoded);
      } catch (error) {
        throw new BridgeInfrastructureError(
          `protocol error from ${HOST}:${this.port}: ${errorMessage(error)}`,
          { cause: error },
        );
      }

      if (response.id !== id) {
        throw new BridgeInfrastructureError(
          `protocol error from ${HOST}:${this.port}: response id ${JSON.stringify(response.id)} did not match request id ${JSON.stringify(id)}`,
        );
      }

      if (!response.ok) {
        throw new BridgeServerCommandError(command, response.error ?? "unknown server error");
      }

      try {
        return parser(response.result);
      } catch (error) {
        throw new BridgeInfrastructureError(
          `protocol error from ${HOST}:${this.port} for ${command}: ${errorMessage(error)}`,
          { cause: error },
        );
      }
    } finally {
      socket.destroy();
    }
  }
}

export function resolvePort(explicitPort?: number): number {
  const candidate = explicitPort ?? parseEnvironmentPort(process.env.INTERCOLONY_DEV_BRIDGE_PORT) ?? DEFAULT_PORT;
  if (!Number.isInteger(candidate) || candidate < 1 || candidate > 65_535) {
    throw new BridgeInfrastructureError(
      `invalid RimWorld bridge port ${JSON.stringify(candidate)}; expected an integer from 1 to 65535`,
    );
  }
  return candidate;
}

function parseEnvironmentPort(value: string | undefined): number | undefined {
  if (value === undefined || value.trim() === "") return undefined;
  if (!/^\d+$/.test(value.trim())) {
    throw new BridgeInfrastructureError(
      `INTERCOLONY_DEV_BRIDGE_PORT=${JSON.stringify(value)} is not a valid TCP port`,
    );
  }
  return Number(value);
}

function positiveTimeout(value: number | undefined, fallback: number, label: string): number {
  const result = value ?? fallback;
  if (!Number.isFinite(result) || result <= 0) {
    throw new BridgeInfrastructureError(`${label} must be a positive number of milliseconds`);
  }
  return result;
}

function exchangeOneLine(
  socket: Socket,
  requestLine: string,
  port: number,
  connectTimeoutMs: number,
  responseTimeoutMs: number,
): Promise<string> {
  return new Promise((resolve, reject) => {
    let settled = false;
    let connected = false;
    let chunks: Buffer[] = [];
    let receivedBytes = 0;
    let responseTimer: NodeJS.Timeout | undefined;
    const decoder = new TextDecoder("utf-8", { fatal: true });

    const finish = (error?: Error, value?: string): void => {
      if (settled) return;
      settled = true;
      clearTimeout(connectTimer);
      if (responseTimer !== undefined) clearTimeout(responseTimer);
      if (error !== undefined) reject(error);
      else resolve(value ?? "");
    };

    const connectTimer = setTimeout(() => {
      finish(
        new BridgeInfrastructureError(
          `timed out connecting to RimWorld bridge at ${HOST}:${port} after ${connectTimeoutMs} ms`,
        ),
      );
    }, connectTimeoutMs);

    socket.on("error", (error: NodeJS.ErrnoException) => {
      finish(connectionError(error, port, connected));
    });

    socket.on("data", (chunk: Buffer) => {
      receivedBytes += chunk.length;
      if (receivedBytes > MAX_RESPONSE_BYTES) {
        finish(
          new BridgeInfrastructureError(
            `protocol error from ${HOST}:${port}: response exceeded ${MAX_RESPONSE_BYTES} bytes`,
          ),
        );
        return;
      }
      chunks.push(chunk);
      const combined = Buffer.concat(chunks, receivedBytes);
      const newlineIndex = combined.indexOf(0x0a);
      if (newlineIndex === -1) return;

      const lineBytes = combined.subarray(0, newlineIndex);
      try {
        finish(undefined, decoder.decode(lineBytes));
      } catch (error) {
        finish(
          new BridgeInfrastructureError(
            `protocol error from ${HOST}:${port}: response was not valid UTF-8`,
            { cause: error },
          ),
        );
      }
      chunks = [];
    });

    socket.on("end", () => {
      if (!settled) {
        finish(
          new BridgeInfrastructureError(
            `protocol error from ${HOST}:${port}: connection closed before a newline-terminated response arrived`,
          ),
        );
      }
    });

    socket.connect({ host: HOST, port }, () => {
      connected = true;
      clearTimeout(connectTimer);
      responseTimer = setTimeout(() => {
        finish(
          new BridgeInfrastructureError(
            `timed out waiting for a response from RimWorld bridge at ${HOST}:${port} after ${responseTimeoutMs} ms`,
          ),
        );
      }, responseTimeoutMs);
      socket.write(requestLine, "utf8");
    });
  });
}

function connectionError(error: NodeJS.ErrnoException, port: number, connected: boolean): BridgeInfrastructureError {
  if (error.code === "ECONNREFUSED") {
    return new BridgeInfrastructureError(
      `nothing is listening on ${HOST}:${port} -- is RimWorld running with INTERCOLONY_DEV_BRIDGE=1 and a bridge-enabled build?`,
      { cause: error },
    );
  }
  const phase = connected ? "communicating with" : "connecting to";
  const detail = error.code === undefined ? error.message : `${error.code}: ${error.message}`;
  return new BridgeInfrastructureError(
    `error ${phase} RimWorld bridge at ${HOST}:${port}: ${detail}`,
    { cause: error },
  );
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
