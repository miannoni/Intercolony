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

### Phase 22 — open-ended employment and notice (§36.4)

**Steps.** Intercolony → Labor → Hire → hire someone → in the pop-up tick **"No end date"** (top
right, beside the term slider) → wait for them to arrive → Labor → Employees → **Dismiss** on their
row.

**Expect.** Three options rather than a yes/no confirm: work out the notice, pay it in lieu at the
same cost, or dismiss with none. Notice length grows with how long they served (3 days minimum, 20
maximum). Dismissing with no notice costs employer reputation and faction goodwill.

**Worth watching.** Whether an open-ended worker behaves normally over several days with no end
tick — nothing should ever report them as "nearly finished", and payroll should keep running.

### Phase 22 — renewal (§115)

**Steps — no waiting any more.** F12 → orange bug icon → search → click:
`hire` → **Hire cheapest worker** (pick a *fixed* term), `arrive` → **Arrive employees now**,
then `renewal` → **Force renewal offer**. It winds the term to within a few days of its end and
raises the question, reporting in the log either the offer or the reason there is none.

*(The slow way, if you want the letter to arrive on its own: hire on a fixed term, treat them
properly — pay every period on time, never draft them — and wait until 5 days before the term ends.)*

**Expect.** A letter titled "*(name)* would like to stay", and Renew / Let go buttons on their row in
Labor → Employees. Renewing extends the same employment in place at about 5% more per day.

**The other half.** Do the same but mistreat them — miss a payroll, or draft a civilian into a fight
— and expect a *different* letter saying no offer is coming, naming which of those it was.

### Phase 22 — supply agreement renewal (§115, §107)

**Steps — no waiting any more.** If you have no agreement: `offer contract` →
**Offer contract (force)**, then `accept` → **Accept first offer**. Then `supply` →
**Force supply agreement to complete**, which credits every remaining cycle as delivered and lets the
settlement decide.

*(The slow way: complete every delivery of a recurring agreement without missing one.)*

**Expect.** The settlement offers another run on the same terms, answerable in the Contracts tab, and
the offer expires after 8 days if ignored. A run with any missed delivery should produce a
completion letter that says renewal was *not* offered and why.

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

No pawn-control mod has ever been loaded alongside this. The risk surface is anything assuming
"player faction implies permanent colonist" — colonist bars, work tab replacements, roster mods.

---

## Proven in play

- ~~**§88 safe passage, happy path**~~ — 2026-07-29. Hired, arrived, forced a war; worker walked out
  factionless and reached the border. No exceptions or warnings in the session.
- ~~**§43 death compensation during safe passage**~~ — 2026-07-30. A civilian on 19/day was killed
  walking out; billed 19 × 60 = 1,140 exactly, 800 paid from storage and 340 booked as debt.
- ~~**§88 safe-passage deadline**~~ — 2026-07-30. Worker did not get clear in two days; detention
  penalty applied and they rejoined their own faction on the map, as the letter says they will.
- ~~**Severed contract across save/load**~~ — 2026-07-30. An autosave holding a closed record with a
  live pawn reference, past its deadline, loaded twice and resolved both times.
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
