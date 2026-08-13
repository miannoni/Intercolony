# Intercolony 0.9.1 Release Prep

## Baseline

- v0.9.0 commit: `b8744e49eedc49aac1d61e13b680427015ef4ba3`
- Starting HEAD: `fe32d70c68fddb0f2542c6cad7a6d6503c3545d5`
- Current branch: `main` (started 30 commits ahead of `origin/main`)
- Save schema: 31 (`IntercolonyWorldComponent.CurrentSaveVersion`)

## Release scope

Since 0.9.0, Intercolony has added cancellable procurement with retained history; safer buyer pickup
timing and colony binding; live, commitment-aware Find Buyer stock; trade-history-based standing
agreements; procurement and agreement UX improvements; opt-in buy-only goods; purchase-request
material/quality constraints; and economy/labor tuning. Animal trading is implemented in the tree
but remains wholly unplayed and is not approved for player-facing 0.9.1 claims without verification.

## Checklist

- [x] Bootstrap repository documents, git baseline, release delta and production save schema.
- [x] Create the resume-safe release checkpoint.
- [x] Audit every material 0.9.1 behavior against current production code.
- [x] Reconcile current self-test names/procedures with `docs/PENDING_PLAYTESTS.md`.
- [x] Run order, contract and RFQ self-tests and record their actual output.
- [ ] Load and round-trip a 0.9.0-era schema-24 save.
- [ ] Open Business, Selling, Procurement, Labor and Relations without red Intercolony errors.
- [ ] Manually verify procurement cancellation, silver consequence, history and reload.
- [ ] Manually verify buyer-pickup timing and en-route reload.
- [ ] Manually verify buyer pickup from a second player map; test a non-home map if practical.
- [ ] Manually sanity-check Find Buyer commitments, double-commit protection and live refresh.
- [ ] Make and record the release-blocker verdict.
- [ ] Prepare truthful 0.9.1 metadata and player-facing release notes.
- [ ] Build and verify `dist/Intercolony-0.9.1` and its ZIP.
- [ ] Re-read `docs/RELEASE_PROCEDURE.md` and prepare the safe Steam update handoff.
- [ ] Prepare, but do not create, the annotated tag and GitHub pre-release.
- [ ] Independently review the release-prep changes and verification evidence.
- [ ] Commit only the release-preparation changes.
- [ ] Stop before tag creation, pushing, Workshop upload or GitHub release creation.

## Tests actually run

| Test/action | Result | Evidence |
|---|---|---|
| Read-only bootstrap and git audit | PASS | 2026-08-13: clean worktree; `main` at `fe32d70`; v0.9.0 target `b8744e4`; production schema constant 31. |
| `dotnet build Source/Intercolony/Intercolony.csproj -v minimal` | PASS | 2026-08-13: 0 errors; 2 NU1900 warnings because vulnerability metadata could not reach NuGet; fresh DLL SHA-256 `883262C1FD5369ECEE01A556A687B5C8E0CFF49992435993B38A1A7FA82E1BDC`. |
| `Run order self-test` | PASS | 2026-08-13 after the buy-only obligation fix: `93 passed, 0 failed`; no error/exception after the test header. The recorded-map-vs-first-home assertion was explicitly skipped because the test world has one home map, so the required two-map manual test remains open. Live-offer acceptance checks also skipped because no live offer existed; the correction-batch availability, timing and buy-only blocks ran. |
| `Run contract self-test` | PASS | 2026-08-13: `38 passed, 0 failed`; 3 cycles ran to Completed and a real history-based offer was generated (`1050x bear meat @ 1.76 vs spot 1.53`). No failure, exception or prerequisite skip after the test header. |
| `Run RFQ self-test` | PASS | 2026-08-13: `69 passed, 0 failed`; 24 requests produced empty/full/partial outcomes with price and quantity variation, 2 modded defs were exercised, and commodity/weapon/chair/workbench construction ran. No failure, exception or skip after the test header. |
| Schema 24 -> 31 real-save migration and reload | NOT RUN | A current-schema load exists in the log, but that does not exercise migration. |

## Manual verification

| Check | Result | Evidence |
|---|---|---|
| Existing 0.9.0-era save load/migrate/save/reload | NOT RUN | Requires a normal game launch and real schema-24 save. |
| Business, Selling, Procurement, Labor, Relations | NOT RUN | No observation from this release-prep session. |
| Procurement cancellation and retained conclusion | NOT RUN | Code inspection is not counted as manual evidence. |
| Buyer-pickup deadline and en-route reload | NOT RUN | Code inspection is not counted as manual evidence. |
| Two-map buyer pickup | NOT RUN | Production code persists `SalesOrder.fulfillmentMap`; play proof still required. |
| Non-home/camp-map buyer pickup | NOT RUN | Practicality not yet established. |
| Find Buyer commitments and live refresh | NOT RUN | Code inspection is not counted as manual evidence. |

## Known non-blocking limitations

- Availability is a soft logical commitment, not a physical reservation; colonists and bills may
  consume promised goods.
- Seller-delivery cargo is conservatively counted as committed until its order completes.
- Testing is limited to the recorded local environment; unowned DLC and most mod combinations remain
  untested rather than unsupported.
- Animal trading is implemented but wholly unplayed; it must not be advertised as verified.

## Blockers

- **Under release-gate review:** purchase-order delivery and refund still resolve through
  `Find.AnyPlayerHomeMap`. In a multi-colony game this silently places goods or silver at the first
  home map rather than the colony that placed the order (`docs/BACKLOG.md`, "Procurement delivers
  and refunds to the wrong colony"). Determine severity from the authoritative blocker policy and
  focused verification before packaging.

## Decisions made during release prep

- Git and production code are authoritative where historical progress/design documents conflict.
- Historical 0.9.0 release notes, paths, tag facts and schema-24 statements remain unchanged.
- `package.ps1` will be invoked with `-Version 0.9.1`; its default 0.9.0 value is historical/tooling
  behavior and will not be changed without a repository-level reason.
- An unexecuted self-test, prerequisite skip or code inspection is never recorded as a pass.
- After a code change and successful build, leave RimWorld open in development mode on a test map so
  Matteo can immediately execute the next requested in-game check.
- Animal trading already exists in production code; no additional animal feature work is authorized.
- Ordinary release-prep commits may be pushed to `main` when Matteo explicitly requests it. No
  version tag, GitHub release, Steam upload or other release publication is authorized by that.

## Current stopping point

- Just completed: order, contract and RFQ self-tests pass at 93/0, 38/0 and 69/0. The buy-only
  obligation regression found by the first order run is fixed; its rerun passed. One-home-map and
  no-live-offer skips retain their stated limits. Project docs were reconciled for this evidence.
- Next recommended short iteration: inspect and decide the known multi-colony procurement
  delivery/refund defect against the 0.9.1 release-blocker policy; do not modify it in the same
  iteration.
- Exact starting points: `docs/BACKLOG.md` under "Procurement delivers and refunds to the wrong
  colony", `PurchaseOrder.cs`, and `PurchaseOrderService.cs` delivery/refund sites.
