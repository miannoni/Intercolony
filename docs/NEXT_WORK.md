# Intercolony 1.0.1 — Next work

Two approved pieces of work for branch `1.0.1`, decided by Matteo on **2026-08-29**. The code
state recorded below was verified on that date. Anything in `docs/BACKLOG.md` is **NOT part of this
queue**.

## Item 1 — A bad employer record must cost you applicants in BOTH number and quality

**Decision:** “fewer applicants AND worse ones.”

### Verified current state

- `EmployerReputationService.AvailabilityFactor` lerps `0.35` to `1.15` across the standing range
  (`Source/Intercolony/Reputation/EmployerReputationService.cs:520-523`). It is applied in both
  places: `LaborCandidateService` around line 145 for the HIRE listing, and `EnsureCensus` around
  line 283 for the job-posting census (`Source/Intercolony/Labor/LaborCandidateService.cs:142-147`,
  `:280-285`). VOLUME already responds on both surfaces. The job-posting self-test asserts this,
  reporting 160 interested at an exploitative standing against 751 at a sought-after one for the
  same offer.
- `EmployerReputationService.CandidateQualityBias` exists and is documented as follows: above the
  midpoint, candidate generation draws twice and keeps the better worker; at the bottom it draws
  twice and keeps the worse; in the middle it draws once, so the common case costs nothing
  (`Source/Intercolony/Reputation/EmployerReputationService.cs:525-538`).
- `CandidateQualityBias` is called in exactly two places: `LaborCandidateService` line 147, which
  builds the HIRE listing, and `MainTabWindow_Intercolony_Labor.cs` line 542, which displays it. It
  is not applied to the posting census. `GenerateProspect` takes the settlement, profile, distance,
  travel and skill list, with no standing parameter at all
  (`Source/Intercolony/Labor/LaborCandidateService.cs:322-349`).

**Conclusion:** job postings already draw fewer applicants for a bad employer, but not worse ones.
The quality half of the rule exists and is simply not wired into the census.

### Required work

Apply the same quality bias to posting census generation that the HIRE listing already uses,
through the existing `EmployerReputationService.CandidateQualityBias`. Reuse its draw-and-keep
semantics; do not invent a second quality rule. Do not change `AvailabilityFactor`, which is already
correct on both surfaces.

### Tests must be able to fail on

- On one fixed seed, a census generated at a bad standing has a measurably worse skill distribution
  than one generated at a good standing. Compare a distribution statistic, not a single pawn; one
  draw proves nothing.
- The same comparison still shows fewer workers at the bad standing. The new quality bias must not
  replace or weaken the existing availability effect.
- A standing in the middle of the range still draws once per census record. An ordinary colony pays
  no extra generation cost.

## Item 2 — A five-day cash flow table on the Business tab

**Decision:** the Business tab gets a table of the next five days showing, per day, expected revenue,
expected expenses and the net.

### Scope

**COMMITTED OBLIGATIONS ONLY.** The table counts:

- recurring sales agreement cycles falling due;
- procurement agreement cycles falling due;
- purchase orders with money due; and
- payroll for active employees.

It does not forecast spot market sales, opportunities the player has not accepted, or anything
speculative. A spot-sale forecaster is explicitly out of scope.

### Face of the report

The Business-tab face must say what it counts. A short line or a tooltip on the heading must state
that the table covers commitments already made. It must not silently omit spot income in a way that
reads as a prediction of total income.

The output is five rows, one for each day in the window, with labelled values for expected revenue,
expected expenses and net.

### Constraints

- `CLAUDE.md` Rule 6: UI presents key/value rows, not prose. Put labels and values on the face;
  put explanations in tooltips.
- `CLAUDE.md` Rule 7: any runtime-composed label is measured with `Text.CalcHeight`; do not give
  dynamic text a literal pixel height.
- `CLAUDE.md` Rule 8: a summary never repeats what is already on the same screen. The five-day
  table must not be followed by a second summary that repeats its rows.
- `CLAUDE.md` General invariants (`:209-214`): every money figure comes from the same owner that
  will actually charge or pay it — `IntercolonyPricing` and the contract's own payment fields.
  Never multiply or re-create the amount in the UI.
- No per-frame recomputation. Three surfaces in this mod have already had to be rescued from
  per-frame work. Compute the table when the underlying obligations change or on a bounded interval,
  then cache it; the Business-tab draw reads the cached result. Reset the cache at its own lifecycle
  boundary, as required by the UI-cache invariant in `CLAUDE.md` (`:217-218`).

### Tests must be able to fail on

- A contract cycle due within the five-day window appears on its due day with the payment the
  contract will actually pay.
- Payroll for an active employee appears on every day of the window and stops on the day the term
  ends.
- A day with no obligations reads zero rather than being omitted. The table always has five rows.
- For every row, net equals revenue minus expenses. That result is computed once in the report data,
  not in the UI.

## Item 3 — The full self-test run intermittently ends one world pawn up

**Decision:** INVESTIGATION. If the cause is found, fix it and add an assertion that fails if the
leak returns. The cause is not identified.

### Measured evidence (2026-08-29)

- On a fresh world, the full suite reported a world-pawn delta of `1` on two of three runs and `0`
  on the third. Every run passed `1377` assertions with a clean log, and the suite's own leak
  guards for the commercial timeline and market pressure read `OK` each time.
- With that day's uncommitted batch stashed, the control was three consecutive full runs, all with
  delta `0`.
- With the batch restored, the suites were run individually: contract `57` assertions / delta `0`,
  job posting `25` assertions / delta `0`, and labor `36` assertions / delta `0`. No individual
  suite reproduces it.
- The job posting suite contains its own assertion that no world pawns leak from postings opened
  and closed, and it passes.

**Conclusion:** the delta appears only in a full run, only sometimes, and correlates with that batch
on samples of three against three. That is a correlation on small numbers and NOT an identified
mechanism.

### Hypotheses (UNVERIFIED)

- **HYPOTHESIS:** `LaborCandidateService` holds a static census pool whose prospects are generated
  pawns. It is rebuilt when the world refresh count changes and released through its `Abandon` path.
  A census rebuilt mid-run could still be holding a pawn when the harness takes its closing count.
- **HYPOTHESIS:** the labor suite completes an employment and leaves the worker walking off the map.
  A pawn still departing when the run ends may still be counted.

### First step

The harness currently reports a world-pawn COUNT before and after. Capture the world pawn LIST
instead and diff the identities, so the extra pawn can be NAMED and traced to whatever created it.
Until that exists, every explanation is speculation, and this item must not be closed on a run that
happens to come back `0`: the delta is intermittent, so a single clean run proves nothing.

### Why it matters

This repository has a documented history of exactly this static pool leaking pawns and faction
objects between games, which went unnoticed for four phases and surfaced as unrelated symptoms —
duplicate thing ids and a faction reporting a null relation with every other faction. One stray pawn
per run is small; the same mechanism at play scale bloats saves.

### Acceptable outcomes

- The pawn is named, traced and the leak fixed, with an assertion that fails if it returns.
- The pawn is named and shown to be benign — for example, a departing employee still legitimately
  in the world at the moment of measurement — in which case record that finding and, if appropriate,
  make the harness stop reporting it as a delta.

Guessing at a fix without naming the pawn is not an acceptable outcome.

A shorter version of this already exists in `docs/BACKLOG.md`; the two must not drift. If this item
is resolved, close the backlog entry in the same commit.

## Not in this queue

- Open items already recorded: `docs/BACKLOG.md`
- Play observations: `docs/PENDING_PLAYTESTS.md`
