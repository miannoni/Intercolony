# Intercolony 1.0 — session seed prompt

Paste this as the first message of a new session to continue the 1.0 program.
Keep it current: when the program moves, update the "Where the program stands"
and "Your next slice" sections rather than writing a new file.

---

You are continuing a multi-session implementation program on branch `1.0` of
`C:\dev\Intercolony`. Read this whole prompt before touching anything.

## Orient first

1. Read `docs/1_0_IMPLEMENTATION_STATUS.md` **in full**. It is the continuity
   record and says where the program actually is. Its slice log carries the
   reasoning behind each decision, not just what changed.
2. Read `docs/INTERCOLONY_1_0_IMPLEMENTATION_PLAN.md` **by stage, never whole**
   (~2,400 lines). Read §17 (proceed/decide/ask rails), §18 (do-not-get-stuck),
   §19 (adjacent issue rule) and §20 (testing rules) now — they govern how you
   work. Read the rest per stage as you reach it.
3. `CLAUDE.md` has the project's hard rules. Rule 1 especially: never invent a
   RimWorld API, grep `./reference/` to confirm anything before writing it.

## Where the program stands

Two of nine stages complete and **Stage 2 is code-complete** — every slice is
built, and only its play gate remains. Save schema **44**. Mod version 0.9.3 on
`main`; `main` stays releasable, all 1.0 work lands on branch `1.0` and merges
at Stage 8. Last commit `cf2557f`.

Stage 2 slices done, in the order they were actually built — **not** ledger
letter order:

- **§2.2** one authoritative effective-economy API (`EffectiveEconomyService`)
- **2D/2E** selling and pricing read effective demand
- **2F** RFQs quote against effective supply
- **2C** market opportunity *size* reads current demand
- **2G** completed trades nudge pressure (the write side)
- **2H** coarse economic chains between categories
- **2I** modest regional pressure diffusion
- **2J** price breakdowns name the current shortage or surplus

The full suite runs **968–969 passed / 0 failed / 13–14 skipped** on a
`-quicktest` world, and **944 / 0 / 9** on the real colony save (that figure
predates 2J's 19 assertions).

## Your next slice — 2K, and it is not a coding task

**Everything left in Stage 2 is a judgement a self-test cannot make** (§20.4).
Do not write more code to substitute for it; `CLAUDE.md` names that as how a
project grows features instead of confidence.

**The migration half is already proven** — the 42 → 43 → 44 chain ran on the
real 22.5 MB colony with zero exceptions and the suite then passed 944/0/9
against it. `dev.ps1 bridge -Save <name>` makes it one command. Re-run it as a
regression check if something later touches persistence; it is not open work.

**What remains is feel.** Does the market read as alive rather than flat or
chaotic? Do regions form? Every Stage 2 coefficient — `ReversionRetention`
(0.82), `NudgeValueScale`, the chain table, `DiffusionCoefficient` — was chosen
conservatively and documented as retune-at-2K, per §18. Expect to move numbers,
not structure. Fold in **Stage 1 criterion 7** (whether a settlement's economy
reads from the Market and Relations tooltips), since it is the same kind of
question.

**The gate is written as exact steps in `docs/PENDING_PLAYTESTS.md`** — dev
mode, the world map, `/` for the debug menu, `Shock settlement economy`, and
what to compare against what. It needs Matteo at the keyboard; you can prepare
the world but not answer the question.

---

## Delegation — this changed, and Matteo corrected me on it mid-session

**Send implementation to `codex:codex-rescue`. Do not write code yourself in
the main session.** His words, on seeing me writing a service and a self-test
by hand: *"I'm seeing heavy writes and I believe you should be delegating
these to codex."* The trigger is **volume of writing**, not difficulty. Small
size is not a reason to keep it — the global `CLAUDE.md` is explicit that
"it's only one file" and "it would be faster" are not on the list.

Keep in the main session: the design decision, the prompt that specifies it,
review of what comes back, verification runs, and the ledger write-up.

`agy` is for massive reads and images only. Never for producing code.

**Codex cannot restart RimWorld** — its sandbox is denied permission to kill
the process. Tell it to stop at the build; you run the suite. It also
sometimes hands off to its own background task and returns only a task id;
when that happens, wait for the working tree to change and read the diff off
disk rather than waiting for a report.

**Spot-check what it reports.** It has been reliable on this program, but one
subagent explicitly relayed Codex's claims without verifying them. Read the
diff yourself before believing a description of it.

---

## You can test your own work — including migrations

A **dev test bridge** (loopback TCP inside a running RimWorld) plus an MCP
server (`intercolony-rimworld`) lets you run the suites against the live game
and read results directly. Full detail in `docs/DEV_TEST_BRIDGE.md`.

```powershell
powershell -ExecutionPolicy Bypass -File dev.ps1 test all -Fresh    # clean -quicktest world
powershell -ExecutionPolicy Bypass -File dev.ps1 test economy       # one suite, game already up
powershell -ExecutionPolicy Bypass -File dev.ps1 bridge -Save Fenhana   # boot into a REAL save
```

Test ids come from `tests.list`: `economy`, `timeline`, `profile`, `market`,
`reputation`, `contract`, `rfq`, `order`, `animal`, `ledger`, `labor`,
`payroll`, `transition`, `job-posting`, `combat-clause`,
`employer-reputation`, `long-term`.

### `bridge -Save` is new and it closed a standing item

The old seed said the bridge could not prove a migration. **That was true of
`-quicktest` and false in general.** RimWorld loads a save named exactly
`autostart` at boot in dev mode, via the real `GameDataSaveLoader.LoadGame`
(`reference/decompiled/Verse/Root_Entry.cs:18-23`). `dev.ps1 bridge -Save <name>`
stages a copy, launches, waits, and deletes the copy.

Three things that cost real time to learn:

- **Autostart must not be combined with `-quicktest`.** `Root_Entry` and
  `Root_Play` consume the same one-shot `Root.checkedAutostartSaveFile`, so
  Play then calls `InitNewGame()` on the already-loaded save and throws at
  `Find.get_WorldObjects`. The launch passes no arguments at all.
- **Always a copy, never the original**, and delete it after. A leftover
  `Autostart.rws` hijacks every later launch **including `-Fresh`**, which
  goes on claiming an isolation it silently lost.
- Running the suite on the real colony converts skips (13 → 9) because a real
  colony has the prisoner, caravan and bonded pair a bare map lacks.

### Reading a run honestly

`success` = nothing failed. `clean` = additionally nothing was skipped. **A
suite can pass while the log fills with exceptions — that is not a clean run**
and exits 2, not 0. A skipped assertion is neither failure nor proof (§20.1).
Exit codes: `0` clean, `1` assertions failed, `2` everything else.

**A logged-out Steam client breaks the bridge and does not look like it does.**
Cost ~15 minutes on 2026-08-21. `SteamAPI.Init()` fails, RimWorld finds no
Workshop mods, **deactivates Harmony**, rewrites `ModsConfig.xml` down to Core
plus DLC, and Intercolony dies at `TypeLoadException ... HarmonyPatch` — which
surfaces only as a bridge connection timeout. Check
`HKCU:\Software\Valve\Steam\ActiveProcess\ActiveUser`; **`0` means logged out**,
and Steam merely running is not enough. Only Matteo can fix it — ask, do not
work around it. Recover the overwritten mod list from `Player-prev.log`, which
prints it in load order. Full detail is in `CLAUDE.md`.

**A rebuilt assembly needs a game restart** — a running game holds the old DLL,
so a test run after a rebuild silently tests the previous code. You have
standing authorization to kill and relaunch RimWorld yourself.

Watch `world_pawns.count` either side of a run; the leak check does not.

---

## The testing standard — learned the hard way this session, do not relax it

**Mutation proves *sensitivity*. It does not prove *stability*. These are two
different properties and both need evidence.**

Two assertions were shipped this session that had been mutation-tested, gone
red, and were trusted on that basis. Both were flaky: same code, one fresh
world green, the next red. A statistical assertion over a small sample can be
perfectly sensitive to the bug and still fail at random.

Before trusting any new assertion:

1. **Sensitive** — revert the production change, run, confirm red, restore.
2. **Stable** — run unmutated on **at least four fresh worlds**, all green.

Two further rules that came out of the same failures:

- **A skip guard must measure the same quantity the assertion compares.** The
  RFQ check skipped below 8 *settlements* while comparing *quotations*, and a
  21-settlement world returned 2 quotations, so the guard never fired.
- **When an assertion is statistical, enlarge the sample rather than loosening
  `<` to `<=`.** Loosening makes it pass with the feature deleted, which is
  how a flaky test usually gets "fixed".

When you delegate a test, state **what it must be able to fail on**, not just
what it should claim. Three delegated tests looked like coverage and were
hollow before that was added to the prompts.

---

## Per-slice discipline

Build clean (0 warnings), run the slice's suite, run the **full** suite on
four fresh worlds before committing, confirm world-pawn delta 0 and both leak
guards `OK`, and check the log for exceptions. Commit each working slice and
add its entry to the ledger's slice log in the style of the existing ones —
they record *why*, including the trap avoided, not just what changed.

**Push to `origin/1.0` as you go — standing authorization, no need to ask.**
Never force-push, never push to `main`.

Verify before claiming. Do not report a system as working on a clean build
alone.

## How to work — autonomy

**Decide and continue by default.** §17 is the rail. HIGH confidence → decide
and keep going. MEDIUM → take the smaller option, log it as a DECISION in the
ledger, continue. Only LOW-confidence or structural questions come back to
Matteo.

Do not stop to ask permission to proceed, to confirm an obvious reading, or to
report a passing test. Adjacent issues go to `docs/BACKLOG.md` per §19 unless
they are RED.

**Correct the record when you find it wrong.** Several claims in these
documents turned out to be false this session — that the bridge could not
prove a migration, that `volatility` was dead code, that diffusion shared the
chains' stability budget. Each was written down and corrected in place. A
grep with an exclusion in it is not an audit.

## How to report back

**Every time you return to Matteo for input, lead with this table**, updated:

| Stage | Status | Where it stands |
|---|---|---|
| 0 — Program spine | ✅ Complete | |
| 1 — Settlement economies | ✅ Complete | |
| 2 — Market fundamentals | 🔨 Code complete | Only the 2K play gate, which needs Matteo |
| 3 — Circumstance events | ⬜ Not started | |
| 4 — Brand strength | ⬜ Not started | |
| 5 — Relationships & negotiation | ⬜ Not started | |
| 6 — Procurement parity | ⬜ Not started | |
| 7 — Commercial history | ⬜ Not started | |
| 8 — Integration & release gate | ⬜ Not started | |

Macro stages only — Matteo does not want per-item status. Follow it with next
steps and anything genuinely needing his decision. Keep it short; skip detail
he cannot act on.

## Standing open items a self-test cannot settle

- **The Stage 2 play gate (2K)** — does the market feel alive rather than flat
  or chaotic? The coefficients (`ReversionRetention`, `NudgeValueScale`, the
  chain and diffusion coefficients) are all deliberately conservative and
  documented as retune-at-2K.
- **Stage 1 criterion 7** — whether a settlement's economy reads clearly from
  the Market and Relations tooltips. A judgement about text.
- **The `job posting` pawn-count anomaly is still not explained**, but it did
  **not** reproduce on a 74-pawn world — its original failing condition, which
  had never been retried until this session. Every earlier non-reproduction was
  on a 12-pawn world. Not closed; the world has moved on since the failure, so
  a changed condition is as good a hypothesis as a fixed defect.
- **Known gap in 2I:** no assertion separates diffusing the *difference* from
  diffusing the *level* with a symmetric transfer, since that variant is still
  conservative. It would pump rather than average. Close it only if diffusion
  looks wrong in play.

~~The 43 → 44 migration has never run on a real save.~~ **Closed 2026-08-21**
— it ran on the 22.5 MB `Fenhana` colony with zero exceptions.

## One lesson from 2J worth carrying, because it cost a design

**A rule that disposes of the awkward case usually disposes of the point.** 2J
splits a price's demand row into standing appetite plus current condition. The
first rule collapsed it back to one fused row whenever the `[0.4, 2.0]` price
clamp bound — which sounds conservative and is the opposite: baseline demand of
0.53 is ordinary, so an everyday glut hits the floor and the explanation would
vanish *exactly* where the price is most extreme, the one case §2.11 exists for.
The first test run said so; review had not. When a rule's edge case is "and then
we show nothing", check how often the edge is actually reached before accepting
it.

Start by orienting, then run the 2K play gate with Matteo.
