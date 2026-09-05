# Slice Contract `S01` — quiet successful auto-ready

- `id`: `S01`
- `source_plan_reference`: `F01`
- `behavioral_claim`: When an active selling agreement with Auto-ready enabled has a complete valid
  buyer-pickup order, the order becomes ready without adding a player letter; when the same
  automation cannot mark the order ready, the order remains open and adds one actionable attention
  letter naming the reason rather than silently claiming success.
- `non_goals`: Do not change manual Ready Order notification behavior; do not change final
  collection, partial collection, renewal, procurement auto-ready, letter-volume settings, order
  economics, travel timing, or fulfillment validation; do not implement `F15` defaults.
- `constraints`: Preserve commercial history and logging; preserve the existing one-notification
  throttle for repeated auto-ready failure; preserve `SalesOrderService.CanMarkReadyNow` as the
  readiness validation path; preserve the project's rule that routine success is quiet and
  exceptions are actionable; do not invent RimWorld APIs.
- `authoritative_owner`: `ContractService.AdvanceAutoReady`, which owns the automated selling-cycle
  decision and must request the existing readiness transition without manual-success notification.
- `compatibility_requirements`: Manual `SalesOrderService.MarkReadyForPickup` callers continue to
  receive the existing `Order ready` letter; existing saved orders/contracts load unchanged; seller
  delivery remains ineligible for buyer-pickup readiness; failure text and throttling remain usable;
  normal build remains bridge-free.
- `expected_edit_surface`: `Source/Intercolony/Contracts/ContractService.cs`,
  `Source/Intercolony/Orders/SalesOrderService.cs`, and focused assertions/helpers in
  `Source/Intercolony/Debug/IntercolonyLongTermSelfTest.cs`. `docs/foreman/evidence/S01.md` may be
  added for Candidate Evidence. Any other production file requires a Supervisor amendment before
  Candidate submission.
- `verification_plan`:
  1. Add a focused assertion around the real `AdvanceAutoReady` path that snapshots the letter
     stack and fails if a valid automated cycle adds a success letter.
  2. Retain or strengthen the unavailable-goods assertion so it fails if the order closes, the
     actionable attention letter is absent, or repeat polling adds duplicate attention letters.
  3. Add/retain a manual-ready compatibility assertion that fails if a direct player-style ready
     action no longer adds the existing success letter.
  4. Because this is a previously observed notification regression, establish Test Sensitivity by
     temporarily restoring the automated success notification, show the focused assertion failing
     for that semantic reason, restore the Candidate, and re-run it cleanly.
  5. From `repo/`, run `dotnet build Source/Intercolony/Intercolony.csproj`; it must fail on compile
     or reference errors. Read `docs/DEV_TEST_BRIDGE.md`, then run
     `powershell -ExecutionPolicy Bypass -File dev.ps1 test long-term -Fresh`; it must fail on any
     assertion failure, skip relevant to this claim, bridge/environment error, or new exception in
     the log delta. Report every skip on its own line.
- `escalation_triggers`: Stop and report if the behavior requires changing Plan intent; any edit
  outside the declared surface; an unavailable or unverified RimWorld API; an unavoidable persisted
  schema change; a bridge failure whose remediation requires credentials/operator login; any action
  outside the Execution Envelope; or Evidence showing the Contract is wrong/infeasible.
- `dependencies`: none.

## Formation and amendment history

- 2026-09-05 — Contract formed by Supervisor `/root` after inspecting
  `ContractService.AdvanceAutoReady`, `SalesOrderService.MarkReadyForPickup`, and the existing
  long-term self-test fixtures. No amendments.

