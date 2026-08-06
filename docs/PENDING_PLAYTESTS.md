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

### Employee edge cases never exercised (§33 q12, q13, q20)

Carried from the Phase 15 spike and still open:

- **Downed employee** — what happens if one is incapacitated but not killed. *(A test was armed on
  a target on 2026-08-03 but the session ended before any result; nothing should be read into that.)*
- **Captured employee** — not modelled at all; unknown what the game does.
- **Social relations formed during employment** — the spike's probe pawn had zero relations, so
  "unchanged 0 → 0" was weak evidence. A worker who forms bonds and then leaves is untested.

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
- ~~**Phase 25 — save migration across five schema versions**~~ — 2026-08-05. A **schema 17** save
  loaded and walked 17 → 22 in one pass: job postings, open-ended employment, transition, ledger and
  condition floors. No errors, nothing dropped. Better evidence than the single-step 21 → 22 that was
  asked for. It also surfaced that the migration chain runs ascending 2→13 then *descending* 22→14 —
  harmless today because every step from 14 on is a bare log line, but a false contract against the
  "falls through to the next" comment. Reordered rather than left for the first migration that
  actually moves data.
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
