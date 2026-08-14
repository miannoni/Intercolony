# Intercolony 0.9.1 Release Prep

## Baseline

- v0.9.0 commit: `b8744e49eedc49aac1d61e13b680427015ef4ba3`
- Starting HEAD: `fe32d70c68fddb0f2542c6cad7a6d6503c3545d5`
- Current branch: `main` (started 30 commits ahead of `origin/main`)
- Save schema: 32 (`IntercolonyWorldComponent.CurrentSaveVersion`)

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
| Schema 24 -> 32 real-save migration and reload | NOT RUN | A current-schema load exists in the log, but that does not exercise migration. |
| Post-procurement-fix `dev.ps1 build` | PASS | 2026-08-13 at `4bc9adc`: 0 errors; 2 NU1900 warnings because NuGet vulnerability metadata was unreachable. |
| Post-procurement-fix `-quicktest` clean load | PASS | 2026-08-13 at `4bc9adc`: `[Intercolony] loaded.`, `Harmony patches applied.`, `Trade blacklist rebuilt: 1 rule def(s), 10 def(s) excluded.`, and `State initialized fresh (schema 32).`; no red errors. This fresh current-schema world did not exercise the 31 -> 32 migration. |
| Post-procurement-fix `Run order self-test` | PASS (sell-side no-regression only) | 2026-08-13 at `4bc9adc`: `93 passed, 0 failed`; the same recorded-map-vs-`Find.AnyPlayerHomeMap` and live-offer checks skipped. The suite is entirely sell-side (`SalesOrderService`, Find Buyer, buyer pickup and section 99 goods matching) and does not cover the changed procurement code: no self-test calls `PurchaseOrderService.Refund` or `GiveSilver`; only the pure `RefundableSilver` helper is asserted in `IntercolonyAnimalSelfTest.cs:415-419`. |

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

- **Resolved in code; not verified in play:** purchase-order delivery and refund now resolve the
  colony recorded when the order was accepted, falling back to `Find.AnyPlayerHomeMap` only when
  that map was not recorded or is no longer loaded. The same work uncovered and fixed the more
  serious falsely-reported-refund defect: when no home map existed, an order was finalized as
  Supplier default before any silver was placed, then reported as refunded despite paying nothing
  and could never retry. Zero-placement refunds now hold and retry; partial placement finalizes with
  the amount actually placed in the ledger, log and player message. Neither fault is verified in play.

## Decisions made during release prep

- Git and production code are authoritative where historical progress/design documents conflict.
- Historical 0.9.0 release notes, paths, tag facts and schema-24 statements remain unchanged.
- `package.ps1` will be invoked with `-Version 0.9.1`; its default 0.9.0 value is historical/tooling
  behavior and will not be changed without a repository-level reason.
- An unexecuted self-test, prerequisite skip or code inspection is never recorded as a pass.
- After a code change and successful build, leave RimWorld open in development mode on a test map so
  Matteo can immediately execute the next requested in-game check.
- Animal trading already exists in production code; no additional animal feature work is authorized.
- The procurement defect was graded, and the decision was to fix both the colony misrouting and the
  falsely-reported-refund fault now and accept the schema 32 bump in this point release.
- Do not build new self-test infrastructure for the changed procurement paths. Two of the three
  cases require a two-colony or map-less world, and the zero-placement case requires an injectable
  placement seam; evidence is to come from play, not from more code.
- Ordinary release-prep commits may be pushed to `main` when Matteo explicitly requests it. No
  version tag, GitHub release, Steam upload or other release publication is authorized by that.

## Current stopping point

- Just completed: the procurement colony-routing and falsely-reported-refund faults are fixed in
  code at `4bc9adc`, with the additive schema 32 destination-map field. The post-fix build passed,
  and a fresh schema-32 `-quicktest` world loaded cleanly with no red errors.
- The post-fix order self-test reran at 93/0, but it is entirely sell-side and is only a
  no-regression signal; it does not touch `PurchaseOrderService`. The fresh quicktest world likewise
  did not exercise the 31 -> 32 migration. None of the changed procurement behavior is verified in
  play.
- Next recommended short iteration: use one two-colony session to verify both buyer pickup and
  purchase delivery/refund routing from the second colony, then exercise the real-save migration
  and round trip. The map-less and zero-placement refund holds remain without practical manual
  reproduction.
