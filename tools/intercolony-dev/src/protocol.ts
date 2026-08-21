export type BridgeCommand =
  | "status"
  | "tests.list"
  | "tests.run"
  | "tests.run_all"
  | "state.summary"
  | "world_pawns.count"
  | "postings.count";

export interface BridgeRequest<TArgs extends Record<string, unknown> = Record<string, never>> {
  id: string;
  command: BridgeCommand;
  args: TArgs;
}

export interface BridgeResponse<TResult = unknown> {
  id: string;
  ok: boolean;
  error: string | null;
  result: TResult | null;
}

export interface StatusResult {
  bridgeVersion?: number | string;
  intercolonyVersion?: string;
  processId?: number;
  worldLoaded: boolean;
  mapLoaded: boolean;
  mapIsPlayerHome?: boolean;
  tick?: number;
  worldComponentAvailable?: boolean;
  saveSchema?: number | string;
  currentSaveSchema?: number | string;
  [key: string]: unknown;
}

export interface TestDescriptor {
  id: string;
  label: string;
  requiresMap: boolean;
  [key: string]: unknown;
}

export interface TestsListResult {
  tests: TestDescriptor[];
  [key: string]: unknown;
}

export interface TestRunArgs extends Record<string, unknown> {
  name: string;
}

export interface TestRunResult {
  id: string;
  label: string;
  passed: number;
  failed: number;
  skipped: number;
  success: boolean;
  durationMs: number;
  output: string;
  preconditionError?: string | null;
  exceptionText?: string | null;
  [key: string]: unknown;
}

export interface RunAllTestsResult {
  success: boolean;
  clean: boolean;
  passed: number;
  failed: number;
  skipped: number;
  durationMs: number;
  tests: TestRunResult[];
  output: string;
  [key: string]: unknown;
}

export interface StateSummaryResult {
  summary: string;
  [key: string]: unknown;
}

export interface WorldPawnCountResult {
  allPawnsAliveOrDead: number;
  [key: string]: unknown;
}

export interface PostingCountResult {
  total: number;
  open: number;
  [key: string]: unknown;
}

export type StatusRequest = BridgeRequest<Record<string, never>>;
export type StatusResponse = BridgeResponse<StatusResult>;
export type TestsListRequest = BridgeRequest<Record<string, never>>;
export type TestsListResponse = BridgeResponse<TestsListResult>;
export type TestRunRequest = BridgeRequest<TestRunArgs>;
export type TestRunResponse = BridgeResponse<TestRunResult>;
export type RunAllTestsRequest = BridgeRequest<Record<string, never>>;
export type RunAllTestsResponse = BridgeResponse<RunAllTestsResult>;
export type StateSummaryRequest = BridgeRequest<Record<string, never>>;
export type StateSummaryResponse = BridgeResponse<StateSummaryResult>;
export type WorldPawnCountRequest = BridgeRequest<Record<string, never>>;
export type WorldPawnCountResponse = BridgeResponse<WorldPawnCountResult>;
export type PostingCountRequest = BridgeRequest<Record<string, never>>;
export type PostingCountResponse = BridgeResponse<PostingCountResult>;

export type ResultParser<T> = (value: unknown) => T;

export function parseBridgeResponse(value: unknown): BridgeResponse {
  const object = requireRecord(value, "response");
  const id = requireString(object.id, "response.id");
  const ok = requireBoolean(object.ok, "response.ok");

  if (!ok) {
    const error = requireString(object.error, "response.error");
    if (error.trim().length === 0) {
      throw new Error("response.error must be a non-empty string when ok is false");
    }
    return { id, ok, error, result: object.result ?? null };
  }

  if (!(object.error === null || object.error === undefined || typeof object.error === "string")) {
    throw new Error("response.error must be a string or null");
  }

  return {
    id,
    ok,
    error: typeof object.error === "string" ? object.error : null,
    result: object.result ?? null,
  };
}

export const parseStatusResult: ResultParser<StatusResult> = (value) => {
  const object = requireRecord(value, "status result");
  return {
    ...object,
    worldLoaded: requireBoolean(object.worldLoaded, "status.worldLoaded"),
    mapLoaded: requireBoolean(object.mapLoaded, "status.mapLoaded"),
    ...optionalNumberOrStringField(object, "bridgeVersion"),
    ...optionalStringField(object, "intercolonyVersion"),
    ...optionalNumberField(object, "processId"),
    ...optionalBooleanField(object, "mapIsPlayerHome"),
    ...optionalNumberField(object, "tick"),
    ...optionalBooleanField(object, "worldComponentAvailable"),
    ...optionalNumberOrStringField(object, "saveSchema"),
    ...optionalNumberOrStringField(object, "currentSaveSchema"),
  };
};

export const parseTestsListResult: ResultParser<TestsListResult> = (value) => {
  const object = requireRecord(value, "tests.list result");
  if (!Array.isArray(object.tests)) {
    throw new Error("tests.list result.tests must be an array");
  }
  return { ...object, tests: object.tests.map((test, index) => parseTestDescriptor(test, index)) };
};

export const parseTestRunResult: ResultParser<TestRunResult> = (value) => {
  const object = requireRecord(value, "test result");
  return {
    ...object,
    id: requireString(object.id, "test.id"),
    label: requireString(object.label, "test.label"),
    passed: requireNumber(object.passed, "test.passed"),
    failed: requireNumber(object.failed, "test.failed"),
    skipped: requireNumber(object.skipped, "test.skipped"),
    success: requireBoolean(object.success, "test.success"),
    durationMs: requireNumber(object.durationMs, "test.durationMs"),
    output: optionalString(object.output, "test.output") ?? "",
    ...optionalNullableStringField(object, "preconditionError"),
    ...optionalNullableStringField(object, "exceptionText"),
  };
};

export const parseRunAllTestsResult: ResultParser<RunAllTestsResult> = (value) => {
  const object = requireRecord(value, "tests.run_all result");
  if (!Array.isArray(object.tests)) {
    throw new Error("tests.run_all result.tests must be an array");
  }
  return {
    ...object,
    success: requireBoolean(object.success, "tests.run_all.success"),
    clean: requireBoolean(object.clean, "tests.run_all.clean"),
    passed: requireNumber(object.passed, "tests.run_all.passed"),
    failed: requireNumber(object.failed, "tests.run_all.failed"),
    skipped: requireNumber(object.skipped, "tests.run_all.skipped"),
    durationMs: requireNumber(object.durationMs, "tests.run_all.durationMs"),
    tests: object.tests.map(parseTestRunResult),
    output: optionalString(object.output, "tests.run_all.output") ?? "",
  };
};

export const parseStateSummaryResult: ResultParser<StateSummaryResult> = (value) => {
  const object = requireRecord(value, "state.summary result");
  return { ...object, summary: requireString(object.summary, "state.summary") };
};

export const parseWorldPawnCountResult: ResultParser<WorldPawnCountResult> = (value) => {
  const object = requireRecord(value, "world_pawns.count result");
  return {
    ...object,
    allPawnsAliveOrDead: requireNumber(
      object.allPawnsAliveOrDead,
      "world_pawns.count.allPawnsAliveOrDead",
    ),
  };
};

export const parsePostingCountResult: ResultParser<PostingCountResult> = (value) => {
  const object = requireRecord(value, "postings.count result");
  return {
    ...object,
    total: requireNumber(object.total, "postings.count.total"),
    open: requireNumber(object.open, "postings.count.open"),
  };
};

function parseTestDescriptor(value: unknown, index: number): TestDescriptor {
  const object = requireRecord(value, `tests[${index}]`);
  return {
    ...object,
    id: requireString(object.id, `tests[${index}].id`),
    label: requireString(object.label, `tests[${index}].label`),
    requiresMap: requireBoolean(object.requiresMap, `tests[${index}].requiresMap`),
  };
}

function requireRecord(value: unknown, label: string): Record<string, unknown> {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    throw new Error(`${label} must be an object`);
  }
  return value as Record<string, unknown>;
}

function requireString(value: unknown, label: string): string {
  if (typeof value !== "string") {
    throw new Error(`${label} must be a string`);
  }
  return value;
}

function optionalString(value: unknown, label: string): string | undefined {
  if (value === undefined) return undefined;
  return requireString(value, label);
}

function requireBoolean(value: unknown, label: string): boolean {
  if (typeof value !== "boolean") {
    throw new Error(`${label} must be a boolean`);
  }
  return value;
}

function requireNumber(value: unknown, label: string): number {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    throw new Error(`${label} must be a finite number`);
  }
  return value;
}

function optionalStringField(
  object: Record<string, unknown>,
  key: string,
): Record<string, string> {
  const value = optionalString(object[key], `status.${key}`);
  return value === undefined ? {} : { [key]: value };
}

function optionalNumberField(
  object: Record<string, unknown>,
  key: string,
): Record<string, number> {
  if (object[key] === undefined) return {};
  return { [key]: requireNumber(object[key], `status.${key}`) };
}

function optionalBooleanField(
  object: Record<string, unknown>,
  key: string,
): Record<string, boolean> {
  if (object[key] === undefined) return {};
  return { [key]: requireBoolean(object[key], `status.${key}`) };
}

function optionalNumberOrStringField(
  object: Record<string, unknown>,
  key: string,
): Record<string, number | string> {
  const value = object[key];
  if (value === undefined) return {};
  if (typeof value !== "number" && typeof value !== "string") {
    throw new Error(`status.${key} must be a number or string`);
  }
  return { [key]: value };
}

function optionalNullableStringField(
  object: Record<string, unknown>,
  key: string,
): Record<string, string | null> {
  const value = object[key];
  if (value === undefined) return {};
  if (value === null || typeof value === "string") return { [key]: value };
  throw new Error(`test.${key} must be a string or null`);
}
