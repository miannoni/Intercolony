# Intercolony 1.0.1 — Next work

Three approved pieces of work for branch `1.0.1` (1.0.2 queue), decided by Matteo on **2026-08-29**.
All three items are complete and closed as recorded below. Anything in `docs/BACKLOG.md` is **NOT part
of this queue**.

## Item 1 — A bad employer record must cost you applicants in BOTH number and quality

**Decision:** “fewer applicants AND worse ones.”
**Status: DONE**, commit `108b5af`.

The posting census now generates through the same draw-and-keep the HIRE listing uses, via the
existing `EmployerReputationService.CandidateQualityBias`.

### Outcome and verified measurements

- Measured on one fresh world:
  - Mean best skill: `12.04` at an exploitative standing, `13.64` at mid, `15.20` at sought-after.
  - Census records: `380` / `836` / `900`.
  - Generation draws: `760` / `836` / `1800`, so a mid-range colony still draws once and pays
    nothing extra.
- Three mutations each reddened exactly their own assertion at `27/1/0`.

## Item 2 — A five-day cash flow table on the Business tab

**Decision:** the Business tab gets a table of the next five days showing, per day, expected revenue,
expected expenses and the net.
**Status: DONE**, commit `7201c53`.

### Outcome and scope resolution

The five-day cash flow table was built on the Business tab for committed obligations. Two scope
premises in the initial brief did not match the code and were resolved:

- **Purchase orders:** purchase orders are paid in full at creation (`PurchaseOrderService` takes the
  silver with `TryTakeSilver`), so they have no future money due and contribute nothing.
- **Payroll cadence:** payroll moves on paydays, every `wageStructure.IntervalDays()`, not daily, so
  the table books it on the payday.

### Verification

- Eight assertions covering the table contract, day-by-day booking, and net calculation.
- Seven mutations, six of which isolated their own assertion; the seventh (shortening the window) was
  caught by an exception in an earlier fixture instead, because every fixture indexes the window it
  shrinks.

## Item 3 — The full self-test run intermittently ends one world pawn up

**Decision:** INVESTIGATION.
**Status: DONE — NOT A LEAK**, commit `6a74a78`, with harness work in `13ee1e5`.

### Chain of evidence

The investigation followed the required chain of evidence in order, naming the pawn before drawing
conclusions:

1. **Named:** the extra pawn was captured and identified — `"Verea Roiro"`, `Tribal_HeavyArcher`,
   faction `The Gaalboir League`, situation `Free`, not spawned (observed in one of four fresh full
   runs).
2. **Attributed:** by running the twenty-two suites one at a time against one bridge session, the
   behavior was isolated to the payroll suite, and only on the first run in a world, because that suite
   hires a real worker and drives them to walk out.
3. **Measured:** across four fresh payroll runs, `WorldPawns.ForcefullyKeptPawns` was `12` before and
  `12` after every time, and each pawn that appeared carried `keptForever=False`. One run gained an
   identity at a net delta of zero, which is the GC collecting while the suites create.
4. **Guard:** the guard that now exists is for the real hazard, a pin never released: the payroll
   suite asserts the departed worker is not in `ForcefullyKeptPawns` and that the pinned set did not
   grow. Both were proven by mutation (`40/2/0` when a departed worker is pinned in
   `EmploymentService.Release`).

## Not in this queue

- Open items already recorded: `docs/BACKLOG.md`
- Play observations: `docs/PENDING_PLAYTESTS.md`
