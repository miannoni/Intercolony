# Intercolony 0.9.1 Release Prep

## Baseline

- v0.9.0 commit: `b8744e49eedc49aac1d61e13b680427015ef4ba3`
- Starting HEAD: `fe32d70c68fddb0f2542c6cad7a6d6503c3545d5`
- Current branch: `main` (started 30 commits ahead of `origin/main`)
- Save schema: 39 (`IntercolonyWorldComponent.CurrentSaveVersion`)

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

### The 2026-08-14 player-feedback batch

A further batch landed on 2026-08-14, taking the save schema from 33 to **39**. In player terms:

- **Order history is bounded.** The hundred most recent closed sales orders and closed purchase
  orders are kept in detail and older ones are dropped automatically. **Clear completed history**
  was added to the closed sales list, the concluded purchases list and the concluded requests list.
  What contract eligibility relies on is kept separately and survives clearing.
- **Contract proposals from settlements can be refused.** A master switch plus six product-category
  filters, persisted per save, **off by default** — including for existing saves, since unwanted
  proposals were the complaint that prompted it. Proposals already offered are never deleted.
- **The player can propose their own supply agreements**, choosing the settlement, item, quantity
  and price in one window rather than through a chain of menus.
- **A proposal is sent, not accepted.** The settlement answers after a wait that is shortest for
  very good and very bad offers and longest for middling ones, and it can refuse.
- **Price is a single lever**, from nothing up to twice the going market rate. Below the going rate
  improves standing with the buyer's faction; above it costs standing. A price penalty can never
  make a faction hostile.
- **Find Buyer advertises the price actually paid**, including the fulfilment terms.
- **Fixed:** an order could not be marked ready while another order held stock, wrongly reporting
  zero units free.

Schema steps in this batch: 33 -> 34 durable commercial-history aggregate, which contract
eligibility now reads instead of scanning every completed order; 34 -> 35 the proposal controls;
35 -> 36 a discount on a sales order; 36 -> 37 the same on a recurring contract; 37 -> 38 the going
market rate each deal was struck against; 38 -> 39 a proposal's decision due date and appeal.

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
- [x] Land the 2026-08-14 player-feedback batch: bounded history, proposal controls, player-proposed agreements, the single price lever and two-way goodwill.
- [x] Rerun the order, market and contract self-tests after that batch.
- [ ] Exercise and round-trip the schema 33 -> 39 migrations on a real save (37 reached; 38 and 39 unproven).
- [x] Prepare truthful 0.9.1 metadata and player-facing release notes (`f249741`).
- [x] Build and verify `dist/Intercolony-0.9.1` and its ZIP.
- [x] Re-read `docs/RELEASE_PROCEDURE.md` and prepare the safe Steam update handoff.
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
| Schema 24 -> 37 and 33 -> 37 real-save migration | PASS | 2026-08-14 Player.log evidence: `[Intercolony] State loaded (schema 24, nextId 1).`<br>`[Intercolony] Migrating state from schema 24 to 37.`<br>`[Intercolony] State loaded (schema 33, nextId 6660).`<br>`[Intercolony] Migrating state from schema 33 to 37.` Two real saves migrated the full chain to 37 with no exception. Supersedes the earlier 32 -> 33 row. |
| Post-procurement-fix `dev.ps1 build` | PASS | 2026-08-13 at `4bc9adc`: 0 errors; 2 NU1900 warnings because NuGet vulnerability metadata was unreachable. |
| Post-procurement-fix `-quicktest` clean load | PASS | 2026-08-13 at `4bc9adc`: `[Intercolony] loaded.`, `Harmony patches applied.`, `Trade blacklist rebuilt: 1 rule def(s), 10 def(s) excluded.`, and `State initialized fresh (schema 32).`; no red errors. This fresh current-schema world did not exercise the 31 -> 32 migration. |
| Post-procurement-fix `Run order self-test` | PASS (sell-side no-regression only) | 2026-08-13 at `4bc9adc`: `93 passed, 0 failed`; the same recorded-map-vs-`Find.AnyPlayerHomeMap` and live-offer checks skipped. The suite is entirely sell-side (`SalesOrderService`, Find Buyer, buyer pickup and section 99 goods matching) and does not cover the changed procurement code: no self-test calls `PurchaseOrderService.Refund` or `GiveSilver`; only the pure `RefundableSilver` helper is asserted in `IntercolonyAnimalSelfTest.cs:415-419`. |
| Order, market and contract self-tests after the 2026-08-14 batch | PASS (Matteo's report) | 2026-08-14: Matteo ran all three in game and reported them green. Counts were not captured in this session's evidence and are deliberately not quoted here. |
| Schema 33 -> 39 real-save migration and reload | NOT RUN | A real save has been observed migrating as far as **37**. The six steps this batch added end at 39, and 38 and 39 have never been exercised from a real save — only in worlds created at the current schema, which `-quicktest` always produces. This is the outstanding risk before packaging. |

| `package.ps1 -Version 0.9.1` | PASS | 2026-08-15: 9 files, 1.60 MiB folder, 1.15 MiB zip. Contents are `About/`, `Assemblies/`, `Defs/`, `LICENSE`, `README.md` and nothing else — no source, no `reference/`, and `About/PublishedFileId.txt` correctly absent. The packaged DLL's SHA-256 matches the repository build exactly (`3ce2380ac17ef143ec5934e56c310b812f074154d6d6727330775e794dd20256`). `About/Preview.png` is 933,975 bytes, under Steam's 1 MB cap. |

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
| Propose-agreement window, price lever, pending decision and history clearing | PASS (Matteo's report) | 2026-08-14: Matteo exercised all of them in play and reported them working. No log output was captured. The session found two defects, both since fixed: proposal eligibility was served from a cache that survived the window closing, so it stayed empty for a session unless a tab switch cleared it (`44c6509`); and the Find Buyer unit column carried an unwanted quantity annotation (`8aad4ea`). Neither fix has itself been re-checked in play. |

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

- **Development for 0.9.1 is complete.** The player-feedback batch of 2026-08-14 landed in full,
  taking the save schema to 39, and the order, market and contract self-tests were reported green
  after it. Matteo exercised the new proposal window, the price lever, the pending-decision flow and
  history clearing in play; the two defects that surfaced are fixed in `44c6509` and `8aad4ea`.
- **The remaining work is packaging, not development.** Truthful 0.9.1 metadata and player-facing
  release notes, then building and verifying the versioned distribution and ZIP, then the
  release-procedure handoff — all without publishing. No tag, push, Workshop upload or GitHub
  release is authorized.
- **The one outstanding risk is the save schema.** A real save has been observed migrating to 37.
  Steps 38 and 39 have never run against one, because `-quicktest` always creates a world already at
  the current version. A broken migration is the single class of defect a point release cannot
  undo afterwards, so open a real pre-batch save and round-trip it before packaging.
- Everything shipped in this batch is otherwise unproven in the sense a beta expects: the proposal
  wait, the price lever and the goodwill penalty are self-tested and recorded in
  `docs/PENDING_PLAYTESTS.md` as never having been observed working. That is the state 0.9.0
  shipped in and is not a blocker.
