import net from "node:net";

const host = "127.0.0.1";
const port = Number(process.env.INTERCOLONY_DEV_BRIDGE_PORT ?? 34117);
const mode = process.env.FAKE_BRIDGE_MODE ?? "normal";

const server = net.createServer((socket) => {
  let buffer = "";
  socket.setEncoding("utf8");
  socket.on("data", (chunk) => {
    buffer += chunk;
    const newline = buffer.indexOf("\n");
    if (newline === -1) return;

    if (mode === "malformed") {
      socket.end("this is not json\n");
      return;
    }

    const request = JSON.parse(buffer.slice(0, newline));
    const id = mode === "id-mismatch" ? `${request.id}-wrong` : request.id;
    const response = { id, ok: true, error: null, result: resultFor(request) };
    socket.end(`${JSON.stringify(response)}\n`);
  });
});

server.listen(port, host, () => {
  process.stdout.write(`fake bridge listening on ${host}:${port} (${mode})\n`);
});

function resultFor(request) {
  switch (request.command) {
    case "status":
      return {
        bridgeVersion: "fake-1",
        intercolonyVersion: "test",
        processId: process.pid,
        worldLoaded: true,
        mapLoaded: true,
        mapIsPlayerHome: true,
        tick: 12345,
        worldComponentAvailable: true,
        saveSchema: 2,
        currentSaveSchema: 2,
      };
    case "tests.list":
      return {
        tests: [
          { id: "passing", label: "Passing canned test", requiresMap: false },
          { id: "failing", label: "Failing canned test", requiresMap: false },
        ],
      };
    case "tests.run": {
      const failing = request.args?.name === "failing";
      return testResult(request.args?.name ?? "unnamed", failing);
    }
    case "tests.run_all": {
      const passing = testResult("passing", false);
      const skipped = mode === "skipped-all";
      if (skipped) {
        passing.skipped = 1;
        passing.output = "SKIPPED canned assertion";
      }
      return {
        success: true,
        clean: !skipped,
        passed: 1,
        failed: 0,
        skipped: skipped ? 1 : 0,
        durationMs: 12,
        tests: [passing],
        output: "all canned tests passed",
      };
    }
    case "state.summary":
      return { summary: "Fake world is ready." };
    case "world_pawns.count":
      return { allPawnsAliveOrDead: 7, freeColonists: 3 };
    case "postings.count":
      return { total: 0, open: 0 };
    default:
      throw new Error(`unsupported fake command: ${request.command}`);
  }
}

function testResult(id, failing) {
  return {
    id,
    label: `${id} canned test`,
    passed: failing ? 0 : 1,
    failed: failing ? 1 : 0,
    skipped: 0,
    success: !failing,
    durationMs: 5,
    output: failing ? "FAIL expected true but found false" : "PASS canned assertion",
    preconditionError: null,
    exceptionText: null,
  };
}
