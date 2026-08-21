# The dev test bridge

A loopback socket inside a running RimWorld that lets a development tool ask the game questions
and run Intercolony's self-tests, so verifying a change no longer requires a person opening the
debug menu and reading the result out.

It is a **development tool only**. A released build does not contain it.

---

## The two gates

Both are needed, and neither is redundant.

**Compile gate.** The bridge lives behind `#if INTERCOLONY_DEV_BRIDGE`. That constant is defined
only when MSBuild is given the property:

```powershell
dotnet build Source\Intercolony\Intercolony.csproj                          # no bridge in the DLL
dotnet build Source\Intercolony\Intercolony.csproj -p:EnableDevBridge=true  # bridge compiled in
```

**Runtime gate.** Even a bridge-enabled build starts nothing unless the *game process* has:

```
INTERCOLONY_DEV_BRIDGE=1
INTERCOLONY_DEV_BRIDGE_PORT=34117   (optional, this is the default)
```

**Packaging proof.** `package.ps1` never builds — it copies whatever `Assemblies\Intercolony.dll`
is sitting there, which during development is routinely the last local build. So it reads the
artefact and refuses to package one containing the bridge's markers. Neither gate is visible in a
built DLL, and "the code looks gated" is not proof.

The listener binds `IPAddress.Loopback` and **there is deliberately no setting for the address.**
This runs self-tests inside the player's game; the gap between "reachable from this machine" and
"reachable from the network" is the gap between a dev tool and remote code execution. The port
moves, the address does not.

---

## Using it

Almost always through `dev.ps1`:

```powershell
.\dev.ps1 bridge                    # build bridge-enabled, restart, wait until it answers
.\dev.ps1 test job-posting          # one suite, against the game that is already running
.\dev.ps1 test job-posting -Fresh   # restart into a clean -quicktest world first
.\dev.ps1 test all -Fresh           # the whole suite on a clean world
```

Readiness is a `status` poll, never a fixed sleep and never a log substring — a substring says the
mod loaded, which is not the same as the world and map being ready.

`-Fresh` verifies its own preconditions. If a supposedly fresh world does not have zero open
postings it reports an environment setup failure and **does not run the test**, because a run that
cannot prove its isolation must not claim it.

Exit codes:

```
0   clean
1   assertions failed
2   connection, build, protocol, or environment-setup failure,
    or assertions passed but the log gained new exceptions
```

`1` means specifically "the assertions ran and some failed". Everything that is not that answer
is `2`, including a launch that never got far enough to run anything — a caller that cannot tell
those apart will report a broken launch as a broken build.

Three signals come back and all three matter: the test result, the skip count, and the new
`Player.log` lines. **A suite can pass while the log fills with exceptions. That is not a clean
run**, which is why it exits 2 rather than 0.

A skipped assertion is a third thing. It is **not** a failure and does not turn the exit code red
— a healthy full run skips thirteen in the animal suite alone — but it is not proof either, so it
is reported on its own line and kept out of `success`.

---

## The protocol

Newline-delimited UTF-8 JSON over TCP. **One request and one response per connection**, then the
server closes. Open a new connection per command.

```json
--> {"id":"a1b2c3","command":"tests.run","args":{"name":"job-posting"}}
<-- {"id":"a1b2c3","ok":true,"error":null,"result":{ }}
```

The id is echoed. `ok` is always present, so a client never infers failure from a missing field.
Requests are capped at 64 KB; an over-long one is **drained and then answered**, because replying
while the client is still sending makes the OS reset the connection and discard the answer.

JSON is hand-rolled in `IntercolonyDevBridgeProtocol`. Only `Intercolony.dll` ships and the bridge
may not add a second assembly, and every command returns a differently shaped result, so a
serializer would have wanted a DTO per command anyway.

### Commands

| command | returns |
|---|---|
| `status` | bridge/mod version, pid, worldLoaded, mapLoaded, mapIsPlayerHome, tick, saveSchema |
| `tests.list` | every suite: `id`, `label`, `requiresMap` |
| `tests.run` | args `{name}`; counts, success, durationMs, raw output, precondition/exception |
| `tests.run_all` | totals, `success`, `clean`, per-suite results, combined output |
| `state.summary` | `IntercolonyWorldComponent.DebugStateSummary()` |
| `world_pawns.count` | `AllPawnsAliveOrDead` |
| `postings.count` | total and open job postings |

`status` answers at the main menu with no world loaded. That is the point of it — "not ready yet"
is a successful response describing the state, not a connection failure.

**`world_pawns.count` exists for a specific reason.** The self-test runner's leak check watches the
commercial timeline, market pressure and entity ids — but *not* world pawns. The job-posting leak
is a world-pawn leak, so nothing in the game would otherwise report it. Reading this either side
of a run turns a suspicion into a number.

`success` and `clean` are separate on `tests.run_all`: `success` means nothing failed and nothing
was blocked, `clean` additionally means nothing was skipped. Collapsing them either silences the
skip warning or drowns the failure signal.

---

## Threading — the rule that holds it together

**No Verse, RimWorld, Unity, `Find.*`, world, map or pawn code may run on the socket thread.**

Verse is not thread-safe and does not pretend to be. Touching it from a socket thread does not
reliably throw — it corrupts, or works a hundred times and deadlocks on the hundred and first.

The socket thread may only: accept, read, parse, enqueue, wait, serialize, write, close. That
includes logging: `IntercolonyLog` reaches `Verse.Log`, so the accept loop does not log either.

Everything else runs on the Unity main thread via `IntercolonyDevBridgePump`, a `MonoBehaviour`
created only when the bridge is enabled. It drains a bounded number of commands per `Update()`.

**`Update()` rather than a tick**, because a tick callback stops when the game is paused, and
answering a paused game's `status` is most of the point. Not a Harmony patch: a dev-only
MonoBehaviour that exists in no shipped build is a far smaller compatibility liability
(DESIGN.md §63).

A command whose caller timed out is **tombstoned and not executed**. Otherwise the next frame runs
a self-test that nobody is waiting for any more, possibly while a different one is being requested.

---

## Adding a command

The surface is narrow on purpose. It grows by adding a verb with a contract, never by adding an
interpreter.

**Never add** `eval`, `execute_csharp`, `invoke_method`, `set_field`, `reflection_call`,
`run_debug_action_by_name`, `spawn_anything`, `delete_anything`, or `set_arbitrary_state`. This
executes inside the player's game; a general-purpose evaluator turns a dev tool into a hole.

To add one:

1. Add a `case` in `IntercolonyDevBridgeHost.Dispatch`.
2. Write a private method returning a `Dictionary<string, object>`. It runs on the main thread.
3. Degrade rather than throw when the world or map is absent — return a structured precondition
   error, and **never a fake skip and never a pass**.
4. Reuse existing state accessors. Do not write a second state dump or a second test list; the
   runner's registry is the single source and a parallel list drifts the first time a suite is
   added.

---

## Self-tests run through one registry

`IntercolonyAllSelfTests` owns the list. Each entry has a stable machine `Id` (`job-posting`), the
display `Label` the table has always printed (`job posting`), and whether it needs a map. `List()`,
`RunOne()` and the whole-suite run all read it, and so does the bridge.

Counts come from each suite's own `N passed, M failed[, K skipped]` summary line, matched
case-insensitively — **not** from counting `PASS` markers. Two output styles exist: some suites
print a line per assertion, others print only failures and count passes silently, so marker
counting would report a healthy suite as zero passes. A suite whose output has no summary line at
all is treated as not having completed, never as having passed.

**Every path that runs a suite goes through `IntercolonyDiagnosticGuard`.** Suites drive real
transitions on synthetic orders and deliberately miss payrolls; without the guard those land in the
player's real history and employer standing. Automation means running them repeatedly, so a path
that skipped the guard would do that damage on every invocation.
