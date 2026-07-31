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

### Phase 23 — a worker becomes a colonist (§44, §116)

**This is the riskiest code in the phase and has never been run.** The conversion works by removing
the pawn from the quest's departure list *before* ending the quest. If that is wrong, the brand-new
colonist walks off the map — which is exactly what every other employment ending is supposed to do.

**The self-test already passes (21/21)**, so the gates, the fee, negotiation and the defection cost
are proven. What is left is entirely about the pawn.

**Setup — about two minutes, no waiting.** All four steps are **F12** → **orange bug icon** → search
→ click:

1. search `hire` → **Hire cheapest worker**
2. search `arrive` → **Arrive employees now**
3. search `attachment` → **Force attachment offer** — backdates their tenure past the 30-day bar and
   makes them ask immediately. It reports the release fee in the log.
4. Open **Intercolony → Labor → Employees**. Their row should show **Keep them** / **Not now**.

*(Doing it the slow way instead: hire someone open-ended on a daily wage, keep silver in storage so
payroll never misses, never draft them, and let 30 days pass. The employee tooltip shows progress —
"Settling here permanently: Served 12 of 30 days.")*

**Expect.** A letter, *"(name) has grown attached"*, and **Keep them** / **Not now** buttons on their
row.

**Then test all three routes** (on separate saves, or reload between them):

1. **Pay the fee.** Expect them to become a normal colonist — still standing there, no wage, no
   term, no departure. **Watch for them walking off the map, which is the failure this is testing
   for.** Then search `verify` → **Verify converted employees**, which checks it properly: still on a
   map, player faction, no longer a quest lodger, `IsColonist` true, drafter present. It prints PASS
   or names the failing line. Eyeballing is not enough here — a pawn that has been handed an exit
   order looks fine for a while before it leaves.
2. **Keep them without paying.** Expect their faction's goodwill to collapse and probably turn
   hostile, and expect everything you had booked with them to be voided in the same moment.
3. **Not now.** They should carry on working as an employee, and ask again about 30 days later.

**Also worth a look:** the fee dialog shows the asking price and what your best Social colonist
negotiates it down to. Worth checking the saving feels worth having a negotiator for.

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

**Steps.** Hire on a **fixed** term, treat them properly — pay every period on time, never draft
them — and wait until 5 days before the term ends.

**Expect.** A letter titled "*(name)* would like to stay", and Renew / Let go buttons on their row in
Labor → Employees. Renewing extends the same employment in place at about 5% more per day.

**The other half.** Do the same but mistreat them — miss a payroll, or draft a civilian into a fight
— and expect a *different* letter saying no offer is coming, naming which of those it was.

### Phase 22 — supply agreement renewal (§115, §107)

**Steps.** Complete every delivery of a recurring supply agreement without missing one.

**Expect.** The settlement offers another run on the same terms, answerable in the Contracts tab, and
the offer expires after 8 days if ignored. A run with any missed delivery should produce a
completion letter that says renewal was *not* offered and why.

### Phase 21 — the Posts tab and posting dialog (§35.2)

Never seen in play; only the self-test has exercised the matcher.

**Steps.** Intercolony → Labor → **Posts** → **New posting**.

**Expect.** The going-rate band updates live as skill, level, term and clause change, with a verdict
line placing your offer in it. Post below the band and expect a "No applicants" letter naming the
reason. Applicants arrive on market refreshes and appear under their posting with a "Take on" button.

**Worth watching.** Whether the band's numbers feel right against what the Hire tab is charging —
they are two separate samples of one formula, so they will not match exactly, and it is worth knowing
whether the gap is noticeable.

### §115's first acceptance criterion — long-run stability

> *"Employees can remain for long periods without faction-state drift or save corruption."*

The self-test proves the arithmetic holds to five in-game years. It cannot prove the quest-lodger
mechanism does. This has been the open question in `LABOR_TECHNICAL_NOTES.md` since the Phase 15
spike, which measured a single instant.

**Steps.** Keep one employee — ideally open-ended — through several seasons, saving and reloading
along the way. Renew a fixed-term worker two or three times.

**Watch for.** Their `kindDef` staying correct, ideoligion intact, no raid-point inflation, and no
"could not resolve reference" on load.

### Employee edge cases never exercised (§33 q12, q13, q20)

Carried from the Phase 15 spike and still open:

- **Downed employee** — what happens if one is incapacitated but not killed.
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
- ~~**Cross-game state leak**~~ — 2026-07-30. Quicktest world → new colony → hire → save → reload,
  which previously produced duplicate thing IDs and a null-relation flood. Clean.

---

## Not testable, and deliberately so

- **Non-minifiable buildings cannot be traded.** A caravan physically cannot carry them. Permanent
  exclusion, not a gap — recorded here so it is not rediscovered as a bug.
