# Pending play-tests

Things a self-test cannot settle, and that are therefore not proven until someone plays them.

**Why this file exists.** Self-tests prove arithmetic, state transitions and invariants. They cannot
prove that a pawn walks where it is meant to, that a turret holds its fire, that a window reads well,
or that a system behaves over a season. Those get asked for in conversation and then quietly lost
when the conversation moves on. Anything listed here has shipped and is believed to work, but has
not been seen working.

**How to use it.** When a phase completes, add what it could not prove. When something is played,
move it to *Proven in play* with the date and what was observed — a struck-through line with
evidence is worth more than a deleted one, because the next person asking "was this ever tested?"
gets an answer instead of silence.

---

## The test environment, and what may therefore be claimed

**Everything is verified on one machine, in one load order.** That is the whole truth of this
project's testing, and the documentation must say so rather than implying broader coverage.

- **DLC:** Biotech only. Royalty, Ideology and Anomaly are **not owned and will not be bought**, so
  they cannot be tested here — ever, not just "not yet".
- **Mods:** Hospitality, Common Sense, RT Fuse, Tilled Soil, FSF Filth Vanishes With Rain And Time.
  Intercolony has run alongside these throughout Phase 25 without incident.
- **UI scale:** 1.75x. This is the scale the layout has actually been judged at.

**How to write compatibility notes and documentation under this constraint** (§118, Pass C):

State what was tested, on what, and say plainly that everything else is untested rather than
unsupported. "Tested with Biotech; other DLC untested" is honest and useful. "Compatible with all
DLC" would be a claim nobody here can stand behind, and the first bug report from an Ideology player
would prove it false. The same applies to mods: name the five, and say the rest is unknown.

Reasoning from the defs is legitimate for DLC that cannot be tested — e.g. checking whether a def a
DLC adds would flow through the trade classifier — but it must be labelled as reasoning, not as a
test result.

---

## Outstanding

### Self-tests written but never run

These are dev actions, not play-tests, but they are outstanding verification and belong on the same
list. All of them: **F12** → **orange bug icon** (top-right toolbar) → type the search term → click
the action. Output goes to the debug log; no need to copy anything out, the dev script reads it.

*(none outstanding — all written self-tests have been run.)*

### §115's first acceptance criterion — long-run stability

> *"Employees can remain for long periods without faction-state drift or save corruption."*

The self-test proves the arithmetic holds to five in-game years. It cannot prove the quest-lodger
mechanism does. This has been the open question in `LABOR_TECHNICAL_NOTES.md` since the Phase 15
spike, which measured a single instant.

**Steps.** Keep one employee — ideally open-ended — through several seasons, saving and reloading
along the way. Renew a fixed-term worker two or three times.

**Watch for.** Their `kindDef` staying correct, ideoligion intact, no raid-point inflation, and no
"could not resolve reference" on load.

### The Business report at full density (§117)

The only judgement still outstanding on the dashboard. Reviewed empty and with payroll alone; never
seen with **revenue, purchases and payroll on the report at once**, which is where crowding would
actually show and where §117's "not accounting software" line gets its real test.

**Steps.** Sell something (Find buyer → accept → deliver), buy something (Procurement), and have an
employee on payroll — then open **Business**. Ideally let a quadrum pass so the figures are real
rather than one movement each.

**Worth watching.** Whether four or five rows plus a rule and a net line still reads as a summary, or
starts to look like a table. If it tips, cutting a row is the right answer rather than shrinking the
font.

### Mod compatibility (§33 q18)

**Partially answered, 2026-08-05, incidentally rather than by design.** The play-test session for
Phase 25 was running with **Hospitality, Common Sense, RT Fuse, Tilled Soil and FSF Filth Vanishes**
active alongside Intercolony. A full hire ran through that load order — posting, applicants, take-on,
travelling employment — with no exception in the session. Hospitality is squarely in the risk class
named below, since it manages non-colonist pawns on the player's map.

Treat this as evidence, not a test: nobody exercised the interaction deliberately, and an absence of
exceptions in one session is not the same as checking that a colonist bar or work tab replacement
renders an employee correctly.

Still unexercised: anything assuming "player faction implies permanent colonist" — colonist bars,
work tab replacements, roster mods. A deliberate test wants one of those installed and an employee
on the map, looking at whether the employee appears where a colonist would and whether the mod
tries to assign them work.

---

## Proven in play

- ~~**The distributed build works as distributed**~~ — 2026-08-08, Steam Workshop item `3780094556`.
  Not a gameplay test: the thing it settles is that what a stranger downloads is what was built.
  Subscribed to the hidden item and verified what Steam *serves*, not what was uploaded — all 9
  release files byte-identical by SHA-256 to `dist/Intercolony-0.9.0`, the only extra being the
  `About/PublishedFileId.txt` RimWorld writes itself, and no `Source`, `reference`, `docs`,
  `Screenshots` or dev scripts. The local copy was removed first so it could not mask the download,
  and the log confirms the source:
  `Adding miannoni.intercolony(...\workshop\content\294100\3780094556)` with `Adding mods from mods
  folder:` empty. All five screens opened, then save and reload gave
  `[Intercolony] State loaded (schema 24, nextId 1).` — `State loaded`, not `State initialized
  fresh`, which is what distinguishes a real round trip from a silent re-initialization. No
  exceptions and no GUI-stack imbalance. **Also seen:**
  `[Intercolony] Dropped 15 candidate(s) left over from a previous game.` — the
  `LaborCandidateService` static-pool guard working in a build anyone can download.
  **Not proven by this:** anything about balance or long-run behaviour, and Market was never seen
  *populated* here because a just-created world has run no refresh cycle.
- ~~**Employee edge cases (§33 q12, q13, q20)**~~ — 2026-08-08, one session, zero exceptions.
  **Downed:** **Employee downed — treatment needed** fired twice for separate events. Eric was
  rescued and treated; employment stayed Active and wages continued. When a term expired while its
  worker was downed, **Employee term ended — recovery needed** fired and departure was held instead
  of attempting the vanilla exit path that excludes downed pawns. **Not yet seen:** that
  term-expired worker recovering and completing the final departure. **Captured:** **Employee
  captured — 1441 silver compensation** named Octopus as captured, not dead; 1441 was owed, 800 was
  paid from storage and 641 became debt to Brío Valley. Player.log recorded
  `Compensation (captured) for Octopus: 1441 owed, 800 paid, 641 outstanding.` and the closed
  employment as `[Captured]`. **Cleanup:** bed release was confirmed in play. **Relations:** while
  active, `Verify converted employees` reported `Blue: lover with Bolton (started tick 272486,
  formed during employment)`; after departure it reported Bolton's lover relation with former
  employee Blue as surviving, with counterpart faction The Nelou Treaty, `destroyed False`,
  `lodger False`, `employee False`, and no `REVIEW ANOMALY` marker.

  **Verification lesson:** shown letters previously went only to the letter stack, while Player.log
  recorded only suppressed letters. Searching the log for a shown letter therefore returned nothing
  and was misread as the test not having run. `IntercolonyLetters` now records every letter and marks
  each line as shown or not shown; do not treat an older log's silence as evidence that a letter did
  not fire.
- ~~**Phase 25 — hiring from a job posting with several applicants**~~ — 2026-08-05. The Phase 21
  play-test had exercised this, but only ever by taking on the *bottom* applicant row, which is the
  one arrangement that could not fail: `DrawPostingBlock` iterates applicants descending, and
  `TryAccept` closing a filled posting clears that same list mid-loop. Taking the top row of two
  threw `ArgumentOutOfRangeException` out of the draw pass. Fixed by deferring hire, turn-away and
  withdraw until after the loop and after `EndScrollView`, which is now in a `finally` so a future
  exception cannot leave the GUI stack unbalanced. Re-tested with a two-applicant posting: hired
  clean, no exception anywhere in the session.
- ~~**Phase 25 — condition floors, generated and refused**~~ — 2026-08-05. §118's decide-or-delete on
  `OrderLine.minHitPointsPercent`, resolved as *keep and make real*. Seen end to end: an order
  generated as `2x Psychic insanity lance (60%+ cond)`, and delivery refused with
  `2 offered below the condition floor (25% offered; 60% required)`. Buyer-pickup path confirmed via
  the disabled **Mark ready** tooltip. **Not seen:** the seller-delivery/caravan refusal path, which
  shares the same validator but has its own gizmo.
- ~~**Buyer pickup, end to end**~~ — 2026-08-07/08. Player.log recorded
  `[Intercolony] Order 441 completed by buyer pickup. Collected by the buyer. 458 units for 1354 silver.`
  and `[Intercolony] Order 630 completed by buyer pickup. Collected by the buyer. 1 units for 61 silver.`
  Order 630 was the packed-shelf test, which also proves minified furniture is found by the indexed
  validator and consumed correctly. **Still not seen:** the seller-delivery/caravan condition-refusal
  path, which shares the validator but has its own gizmo.
- ~~**Per-source trade classification**~~ — 2026-08-08. The classification result was 406 tradable
  defs: Core 337, Biotech 67 and RT Fuse 2, with zero from Common Sense, Hospitality, Tilled Soil and
  FSF Filth. This proves DLC and modded defs are classified correctly with no special handling; it
  does not prove that an item from those sources has been traded end to end.
- ~~**Phase 25 — save migration across five schema versions**~~ — 2026-08-06. A **schema 17** save
  loaded and walked 17 → 22 in one pass: job postings, open-ended employment, transition, ledger and
  condition floors. No errors, nothing dropped. Better evidence than the single-step 21 → 22 that was
  asked for. It also surfaced that the migration chain runs ascending 2→13 then *descending* 22→14 —
  harmless today because every step from 14 on is a bare log line, but a false contract against the
  "falls through to the next" comment. Reordered rather than left for the first migration that
  actually moves data.
- ~~**Save migration 22 → 23**~~ — 2026-08-08. Player.log recorded
  `[Intercolony] Migrating state from schema 22 to 23.` followed by
  `[Intercolony]   schema 22 -> 23: procurement fulfilment preference added; existing requests allow either.`
  Migration has now been exercised twice in play: the schema 17 save walked five steps to 22 on
  2026-08-06, and this save took the single step from 22 to 23.
- ~~**§88 safe passage, happy path**~~ — 2026-07-29. Hired, arrived, forced a war; worker walked out
  factionless and reached the border. No exceptions or warnings in the session.
- ~~**§43 death compensation during safe passage**~~ — 2026-07-30. A civilian on 19/day was killed
  walking out; billed 19 × 60 = 1,140 exactly, 800 paid from storage and 340 booked as debt.
- ~~**§88 safe-passage deadline**~~ — 2026-07-30. Worker did not get clear in two days; detention
  penalty applied and they rejoined their own faction on the map, as the letter says they will.
- ~~**Severed contract across save/load**~~ — 2026-07-30. An autosave holding a closed record with a
  live pawn reference, past its deadline, loaded twice and resolved both times.
- ~~**Phase 22 — open-ended employment and notice (§36.4)**~~ — 2026-08-03. All three dismissal
  options present and correct: 3-day minimum notice, pay-in-lieu arithmetic verified at 3 x 23 = 69,
  letter clear. Found a display bug in the same pass — the row printed the open-ended sentinels raw
  as `0d ... 34028230000000000000000000000000000000d left`, and never showed that notice was
  running. Both fixed; *the corrected row display has not itself been re-checked.*
- ~~**Phase 22 — renewal, both halves (§115)**~~ — 2026-08-03. Treated well, the worker asked to stay
  at 26/day against 25 and renewing extended the same employment in place. Treated badly — one
  drafted civilian — no offer came, and the refusal named exactly which thing caused it.
- ~~**Phase 22 — supply agreement renewal (§115, §107)**~~ — 2026-08-03. Offer created, accepted,
  agreement credited to completion, and the settlement offered another run answerable in the
  Contracts tab. Found a real bug on the first attempt: completion was unreachable except through an
  order resolving, so a credited agreement sat Active forever with an empty reason string.
- ~~**Phase 24 — ledger and Business tab**~~ — 2026-08-03. Ledger self-test 23/23, including the
  assertion that it agrees with real silver leaving storage. Business tab reviewed empty and
  populated and judged "a summary, not accounting software"; the runway line works and was called the
  best line on the screen. Three layout bugs found and fixed. **Still unseen:** the report with
  revenue, purchases and payroll present at once, which is where crowding would show.
- ~~**Phase 21 — the Posts tab and posting dialog (§35.2)**~~ — 2026-08-03. Band updates live
  (skill 8→18 moved 139 workers to 8, and the band 25–84 to 50–71; switching to security contractor
  took it to 125–177). Verdict escalates amber→red. The no-applicants letter names the reason. Two
  clipped strings and an invisible selection state found and fixed.
- ~~**Phase 23 — a worker becomes a colonist (§44, §116)**~~ — 2026-07-31, all three routes. Paid:
  colonist in place, 3,583 debited exactly as quoted. Defected: colonist in place, faction hostile at
  -80, bookings voided, nothing paid. Declined: stays an employee. `Verify converted employees`
  reported PASS on both conversions, and again after four in-game hours at 3x — the pawn does not
  drift to the map edge, which was the whole risk.
- ~~**Cross-game state leak**~~ — 2026-07-30. Quicktest world → new colony → hire → save → reload,
  which previously produced duplicate thing IDs and a null-relation flood. Clean.

---

## Not testable, and deliberately so

- **Non-minifiable buildings cannot be traded.** A caravan physically cannot carry them. Permanent
  exclusion, not a gap — recorded here so it is not rediscovered as a bug.
