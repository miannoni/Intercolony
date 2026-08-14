# Intercolony 0.9.1 Release Prep

## Baseline

- v0.9.0 commit: `b8744e49eedc49aac1d61e13b680427015ef4ba3`
- Starting HEAD: `fe32d70c68fddb0f2542c6cad7a6d6503c3545d5`
- Current branch: `main` (started 30 commits ahead of `origin/main`)
- Save schema: 33 (`IntercolonyWorldComponent.CurrentSaveVersion`)

## Release scope

Since 0.9.0, Intercolony has added cancellable procurement with retained history; safer buyer pickup
timing and colony binding; live, commitment-aware Find Buyer stock; trade-history-based standing
agreements; procurement and agreement UX improvements; opt-in buy-only goods; purchase-request
material/quality constraints; and economy/labor tuning. Animal trading is implemented in the tree
but remains wholly unplayed and is not approved for player-facing 0.9.1 claims without verification.

Four follow-up commits are also in scope: `d350f2e` fixes the false "0 units free" block;
`0b1dfe9` makes Find Buyer listing and commit use the same fulfilment-aware rate and labels its
priced quantity; `fd6fbe7` deliberately lets local demand, quoted prices and contract offers drift
between cycles; and `86aa768` consumes a settlement's current-cycle appetite and adds the schema
32 -> 33 sales-order completion tick. None has been verified in play, and the self-tests have not
been rerun since these commits.

## Checklist

- [x] Bootstrap repository documents, git baseline, release delta and production save schema.
- [x] Create the resume-safe release checkpoint.
- [x] Audit every material 0.9.1 behavior against current production code.
- [x] Reconcile current self-test names/procedures with `docs/PENDING_PLAYTESTS.md`.
- [x] Run order, contract and RFQ self-tests and record their actual output.
- [x] Load and round-trip a 0.9.0-era schema-23 save through schema 32.
- [x] Open Business, Selling, Procurement, Labor and Relations without red Intercolony errors.
- [x] Manually verify two-colony procurement delivery/refund routing.
- [x] Manually verify procurement cancellation.
- [x] Manually verify buyer-pickup timing.
- [x] Manually verify buyer pickup from a second player map.
- [ ] Test buyer pickup from a non-home/camp map if practical.
- [x] Manually sanity-check Find Buyer commitment behaviour.
- [x] Grade and disposition the three Find Buyer defects; all three were fixed for 0.9.1.
- [ ] Exercise and round-trip the schema 32 -> 33 migration.
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
| Schema 23 -> 32 real-save migration and reload | PASS | 2026-08-13 Player.log evidence: `[Intercolony] State loaded (schema 23, nextId 1001).`<br>`[Intercolony] Migrating state from schema 23 to 32.`<br>`[Intercolony]   schema 23 -> 24: employee incapacitation warnings added; existing employments start unwarned.`<br>`[Intercolony] State loaded (schema 31, nextId 5203).`<br>`[Intercolony] Migrating state from schema 31 to 32.`<br>`[Intercolony] State loaded (schema 32, nextId 5215).`<br>`[Intercolony] State loaded (schema 32, nextId 5395).` This proves a real schema-23 save migrated through the full chain to 32, a separate 31 -> 32 migration ran, and subsequent loads returned at schema 32. A full Player.log scan for `Exception`, `NullReference`, Intercolony errors and Intercolony warnings returned no matches apart from the quoted migration-step line, where `warnings` is part of the migration description rather than an actual warning. |
| Schema 32 -> 33 real-save migration and reload | NOT RUN | The earlier migration evidence covers the chain through schema 32 only. The additive 32 -> 33 completion-tick step has not yet been exercised. |
| Post-procurement-fix `dev.ps1 build` | PASS | 2026-08-13 at `4bc9adc`: 0 errors; 2 NU1900 warnings because NuGet vulnerability metadata was unreachable. |
| Post-procurement-fix `-quicktest` clean load | PASS | 2026-08-13 at `4bc9adc`: `[Intercolony] loaded.`, `Harmony patches applied.`, `Trade blacklist rebuilt: 1 rule def(s), 10 def(s) excluded.`, and `State initialized fresh (schema 32).`; no red errors. This fresh current-schema world did not exercise the 31 -> 32 migration. |
| Post-procurement-fix `Run order self-test` | PASS (sell-side no-regression only) | 2026-08-13 at `4bc9adc`: `93 passed, 0 failed`; the same recorded-map-vs-`Find.AnyPlayerHomeMap` and live-offer checks skipped. The suite is entirely sell-side (`SalesOrderService`, Find Buyer, buyer pickup and section 99 goods matching) and does not cover the changed procurement code: no self-test calls `PurchaseOrderService.Refund` or `GiveSilver`; only the pure `RefundableSilver` helper is asserted in `IntercolonyAnimalSelfTest.cs:415-419`. |

## Manual verification

| Check | Result | Evidence |
|---|---|---|
| Existing 0.9.0-era save load/migrate/save/reload | PASS | Captured Player.log evidence quoted above shows the real schema-23 save migrating through schema 32 and later loading at schema 32; a separate schema 31 -> 32 migration also ran. |
| Business, Selling, Procurement, Labor, Relations | PASS (log evidence) | The full Player.log scan found no `Exception`, `NullReference`, Intercolony error or Intercolony warning entries. This satisfies the no-red-Intercolony-errors check as far as the captured log can show. |
| Two-colony procurement delivery and refund routing | PASS (Matteo's report) | Matteo directly observed both behaviours working during the 2026-08-13 play session; no supporting log output was captured. |
| Procurement cancellation | PASS (Matteo's report) | Matteo directly observed procurement cancellation working during the 2026-08-13 play session; no supporting log output was captured. |
| Buyer-pickup timing | PASS (Matteo's report) | Matteo directly observed buyer-pickup timing working during the 2026-08-13 play session; no supporting log output was captured. |
| Two-map buyer pickup | PASS (Matteo's report) | Matteo directly observed buyer pickup from a second player map during the 2026-08-13 play session; no supporting log output was captured. |
| Non-home/camp-map buyer pickup | NOT RUN | Practicality not yet established. |
| Find Buyer commitment behaviour | PASS (Matteo's report) | Matteo directly observed the targeted Find Buyer commitment behaviour working during the 2026-08-13 play session; no supporting log output was captured. The same session exposed separate demand-consumption, unit-price and false already-committed symptoms recorded in `docs/BACKLOG.md`; their later fixes have not been verified in play. |

## Known non-blocking limitations

- Availability is a soft logical commitment, not a physical reservation; colonists and bills may
  consume promised goods.
- Seller-delivery cargo is conservatively counted as committed until its order completes.
- Testing is limited to the recorded local environment; unowned DLC and most mod combinations remain
  untested rather than unsupported.
- Animal trading is implemented but wholly unplayed; it must not be advertised as verified.

## Blockers

- **Resolved in code; colony routing verified in play on Matteo's report:** purchase-order delivery
  and refund now resolve the
  colony recorded when the order was accepted, falling back to `Find.AnyPlayerHomeMap` only when
  that map was not recorded or is no longer loaded. The same work uncovered and fixed the more
  serious falsely-reported-refund defect: when no home map existed, an order was finalized as
  Supplier default before any silver was placed, then reported as refunded despite paying nothing
  and could never retry. Zero-placement refunds now hold and retry; partial placement finalizes with
  the amount actually placed in the ledger, log and player message. Matteo directly observed
  two-colony delivery and refund routing working on 2026-08-13; that observation was not captured
  in Player.log. The map-less and zero-placement paths remain without practical play reproduction.

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
- The three Find Buyer defects found during verification were graded and fixed for 0.9.1 in
  `d350f2e`, `0b1dfe9` and `86aa768`; their fixes still require verification.
- Per-good demand now varies by market cycle (`fd6fbe7`), smoothed across three cycles. This is a
  deliberate economy change, not a bug fix: quoted prices and contract offers move between
  refreshes, while an accepted `SalesOrder` keeps its locked unit price.
- Do not build new self-test infrastructure for the changed procurement paths. Two of the three
  cases require a two-colony or map-less world, and the zero-placement case requires an injectable
  placement seam; evidence is to come from play, not from more code.
- Ordinary release-prep commits may be pushed to `main` when Matteo explicitly requests it. No
  version tag, GitHub release, Steam upload or other release publication is authorized by that.

## Current stopping point

- The three Find Buyer defects found during the play session are diagnosed and fixed in `d350f2e`,
  `0b1dfe9` and `86aa768`; `fd6fbe7` also adds the deliberate per-cycle demand change. None of the
  four commits has been played, and the self-tests have not been rerun since them.
- Earlier Player.log evidence proves migration only through schema 32. The new schema 32 -> 33 step
  has not yet been exercised or round-tripped.
- Before packaging, verify the four follow-up commits, exercise the 32 -> 33 migration, and prepare
  truthful 0.9.1 metadata and player-facing release notes. After that, build and verify the
  versioned distribution and ZIP, and continue the remaining release-procedure handoff steps
  without publishing.
