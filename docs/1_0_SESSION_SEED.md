# Intercolony 1.0 — session seed prompt

Paste this as the first message of a new session to continue the 1.0 program.
Keep it current: when the program moves, update the "Where the program stands"
and "Your next slice" sections rather than writing a new file.

---

You are continuing a multi-session implementation program on branch `1.0` of
`C:\dev\Intercolony`. Read this whole prompt before touching anything.

## Orient first

1. Read `docs/1_0_IMPLEMENTATION_STATUS.md` **in full**. It is the continuity
   record and says where the program actually is.
2. Read `docs/INTERCOLONY_1_0_IMPLEMENTATION_PLAN.md` **by stage, never whole**
   (~2,400 lines). Read §17 (proceed/decide/ask rails), §18 (do-not-get-stuck),
   §19 (adjacent issue rule) and §20 (testing rules) now — they govern how you
   work. Read the rest per stage as you reach it.
3. `CLAUDE.md` has the project's hard rules. Rule 1 especially: never invent a
   RimWorld API, grep `./reference/` to confirm anything before writing it.

## Where the program stands

Two of nine stages complete. Save schema is **44**. Mod version 0.9.3 on `main`;
`main` stays releasable, all 1.0 work lands on branch `1.0` and merges at Stage 8.

Stage 0 (spine) and Stage 1 (settlement economies) are closed. Stage 2 (market
fundamentals) is ~20% done: 2A persisted market pressure, 2B made it mean-revert.
**Pressure moves and nothing reads it yet** — that is the safe state between slices.

## Your next slice — read this carefully, the ledger's letters are misleading

The next item is **plan §2.2, one authoritative effective-economy API**: a single
read model answering effective demand/supply per settlement per good, combining
stable profile baseline × persistent pressure × (later) event modifiers, bounded.

**§2.2 has no slice letter of its own** — the ledger jumps 2B → "2C remove the
cycle noise". Do not follow that order. §2.3 is explicit that the old
`0.55–1.45` roll may only be deleted *once all consumers use the new API*, so
2.2 must exist and be adopted first. Give it its own slice. After it: 2D–2F
migrate selling, pricing and RFQs onto it, then 2C removes the noise.

Do not rush Stage 2 to reach later stages. The plan says so twice. Everything
downstream reads from it.

---

## You can test your own work. This is new — use it.

For most of this project's life, verifying a change meant asking Matteo to open
RimWorld's debug menu, click "Run ALL self-tests", and read the result back. That
is no longer the default and **you should not ask him to do it.**

There is now a **dev test bridge**: a loopback TCP listener inside a running
RimWorld that answers questions about the live game and runs Intercolony's
seventeen self-test suites on demand. A stdio MCP server (`intercolony-rimworld`,
already approved) exposes it to you directly. Full detail in
`docs/DEV_TEST_BRIDGE.md` — read it before your first run.

What this means in practice: you can make a change, build it, restart the game,
run 874 assertions against the real running game, read the failures, and iterate
— entirely on your own. Matteo is needed only for things a self-test structurally
cannot settle, which is a short list (below).

### The tools

MCP: `rimworld_status`, `rimworld_list_self_tests`, `rimworld_run_self_test`,
`rimworld_run_all_self_tests`, `rimworld_state_summary`, `rimworld_posting_count`,
`rimworld_world_pawn_count`, `rimworld_recent_log`.

Or through `dev.ps1`, which also handles build-and-restart:

```powershell
dotnet build Source\Intercolony\Intercolony.csproj -p:EnableDevBridge=true
powershell -ExecutionPolicy Bypass -File dev.ps1 bridge              # launch a bridge-enabled game
powershell -ExecutionPolicy Bypass -File dev.ps1 test economy        # one suite, game already running
powershell -ExecutionPolicy Bypass -File dev.ps1 test economy -Fresh # clean -quicktest world first
powershell -ExecutionPolicy Bypass -File dev.ps1 test all -Fresh     # whole suite, clean world
```

Test ids come from `tests.list`, not display names: `economy`, `timeline`,
`profile`, `market`, `reputation`, `contract`, `rfq`, `order`, `animal`,
`ledger`, `labor`, `payroll`, `transition`, `job-posting`, `combat-clause`,
`employer-reputation`, `long-term`.

### It never ships

The bridge is behind two independent gates: a compile gate
(`-p:EnableDevBridge=true` defines `INTERCOLONY_DEV_BRIDGE`) and a runtime gate
(`INTERCOLONY_DEV_BRIDGE=1` in the game process's environment). A normal build
contains no listener at all. `package.ps1` reads the built assembly and refuses
to package one containing the bridge's markers, because "the code looks gated" is
not proof. The listener binds loopback only and there is deliberately no setting
for the address.

If you ever extend it: add a verb with a contract, never an interpreter. **Never
add** `eval`, `execute_csharp`, `invoke_method`, `set_field`, or anything that
runs arbitrary input — this executes inside a player's game.

### Reading a run honestly

Three signals come back and **all three matter**: the assertion result, the skip
count, and the new `Player.log` lines.

- `success` = nothing failed. `clean` = additionally nothing was skipped. They
  are separate on purpose; collapsing them silences one or the other.
- **A suite can pass while the log fills with exceptions. That is not a clean
  run** — it exits `2`, not `0`.
- A skipped assertion is not a failure and not a pass. A healthy full run skips
  ~13 in the animal suite alone, because a bare `-quicktest` map has no prisoner,
  slave, caravan, bonded pair or pregnant animal. Report skips; never count them
  as proof (§20.1).
- Exit codes: `0` clean, `1` assertions ran and some failed, `2` everything else
  — connection, build, environment-setup, or passing-with-new-exceptions.
- `-Fresh` verifies its own preconditions and **refuses to run rather than claim
  an isolation it cannot prove**. Without `-Fresh` nothing restarts, which is what
  you want while iterating — but do not call such a run "clean" or "isolated".
- `world_pawns.count` exists for a specific reason: the runner's leak check
  watches the timeline, market pressure and entity ids but **not world pawns**,
  and the one open anomaly is a world-pawn leak. Read it either side of a run.

### Three traps already paid for

- **A rebuilt assembly needs a game restart.** A running game holds the old DLL,
  so a test run after a rebuild silently tests the previous code. You have
  standing authorization to kill and relaunch RimWorld yourself.
- **`rimworld_recent_log` can return the stale startup profile.** To check for
  exceptions, grep `Player.log` directly, excluding RimWorld's own
  `Error check all defs` profiler line and `Fallback handler` noise.
- **The bridge cannot prove a migration.** It launches `-quicktest`, which
  creates a *new* world that initializes at the current schema and never enters
  the migration path at all. Only `dev.ps1 run -MainMenu` and a real save do.

If the bridge does not answer, in this order: is RimWorld running; was it built
with `-p:EnableDevBridge=true`; was it launched with `INTERCOLONY_DEV_BRIDGE=1`;
is something else on port 34117 (the log names it). Everything the MCP server can
do is also reachable as `node tools/intercolony-dev/dist/cli.js status` — which is
how you tell a broken bridge from a broken agent.

### Why the suites are safe to run repeatedly

Self-tests drive real transitions on synthetic orders and deliberately miss
payrolls. Every path that runs a suite goes through `IntercolonyDiagnosticGuard`,
which snapshots and restores the player's commercial timeline, market pressure and
employer standing. Without it, automation would damage real state on every
invocation — running the payroll suite once permanently cost the colony employer
standing before this was found. Do not add a suite-running path that bypasses it.

---

## Per-slice discipline

Build clean (0 warnings), run the slice's suite, run the **full** suite before
committing, confirm world-pawn delta 0 and both leak guards `OK`, and check the
log for exceptions. Commit each working slice and add its entry to the ledger's
slice log in the same style as the 2A and 2B entries.

**Push to `origin/1.0` as you go — standing authorization, no need to ask.**
Keeping the remote current is low-risk on a feature branch and it is the backup.
Push after each slice's commit rather than letting work pile up locally. Two
limits: never force-push, and never push to `main` — `main` stays at the released
0.9.3 and the 1.0 branch merges there only at Stage 8.

Verify before claiming. Do not report a system as working on a clean build alone
— that failure mode has bitten this project repeatedly and the ledger records it.

## How to work — autonomy

**Decide and continue by default.** The plan resolves most questions; §17 is the
rail. HIGH confidence → decide and keep going. MEDIUM → take the smaller option,
log it as a DECISION in the ledger, continue. Only LOW-confidence or structural
questions come back to Matteo.

Do not stop to ask permission to proceed, to confirm an obvious reading, or to
report a passing test. Do not widen scope mid-slice — adjacent issues go to
`docs/BACKLOG.md` per §19 unless they are RED (fix or stop).

## How to report back

**Every time you return to Matteo for input, lead with this table**, updated:

| Stage | Status | Where it stands |
|---|---|---|
| 0 — Program spine | ✅ Complete | |
| 1 — Settlement economies | ✅ Complete | |
| 2 — Market fundamentals | 🔨 ~20% | |
| 3 — Circumstance events | ⬜ Not started | |
| 4 — Brand strength | ⬜ Not started | |
| 5 — Relationships & negotiation | ⬜ Not started | |
| 6 — Procurement parity | ⬜ Not started | |
| 7 — Commercial history | ⬜ Not started | |
| 8 — Integration & release gate | ⬜ Not started | |

Macro stages only — Matteo does not want per-item status. Follow it with the next
steps, and anything genuinely needing his decision. Keep it short; skip detail he
cannot act on.

## Standing open items a self-test cannot settle

These are the things that legitimately need Matteo. Carry them forward:

- The **43 → 44 migration has never run on a real save**. The proven one was
  42 → 43, before 2A bumped the schema.
- **Stage 1 criterion 7** — whether a settlement's economy reads clearly from the
  Market and Relations tooltips. A judgement about text, needs eyes in play.
- **The `job posting` pawn-count anomaly** stopped reproducing without being
  explained (74-pawn world failed, 12-pawn world passes). Not closed.

Start by orienting, then begin §2.2.
