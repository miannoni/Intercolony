# Dispatch notes

Handoff log between the **Dispatch computer-use session** (drives RimWorld and reads the screen)
and **Claude Code** (writes the mod). It exists because relaying results by clipboard and
screenshot is slow and lossy, and because two stale-context incidents already happened when
results were read off a terminal window instead of the repo.

**How to use it.** Claude Code does not read this file automatically. Nudge it in the terminal:

```
read DISPATCH_NOTES.md and continue
```

**Rules.** Append, never overwrite. Every entry gets a timestamp. Game output is transcribed
verbatim from the in-game debug log — no paraphrase, no inference. If something was not observed,
it says so rather than being filled in.

---

## 2026-07-31 ~18:20 — Phase 23 schema check + transition self-test

Run by Dispatch on the live game. Save loaded: 8th of Aprimay 5500, colonists Mikey, Philip, Inessa.

**Schema check** — `Intercolony → Dump state`:

```
saveVersion : 20 (current 20)
nextId      : 1
economySeed : unassigned
profiles    : 0 cached
tick now    : 43644
lastRefresh : never
opportunities: 0 (0 available)
orders      : 0 (0 open)
employments : 0 (0 open)
postings    : 0 (0 open, 0 applicant(s) waiting)
employer    : Employer 50/100 (Decent employer) — 0 completed, 0 late payroll, 0 walk-outs,
              0 deaths, 0 clause breaches, 0 detentions, 0 unpaid
labor debts : 0 (0 unsettled)
```

Schema 20 confirmed. World is otherwise empty — no employments, no orders, seed unassigned.

**Transition self-test** — `Run transition self-test`: **21 passed, 0 failed.** No red errors.

Caveat recorded at the time: this proves the logic only. The conversion's pawn behaviour — whether
a converted lodger stays put or walks off the map — was not exercised by this test.

---

## 2026-07-31 ~18:45 — Phase 23 conversion play-test (in progress)

Following `docs/PENDING_PLAYTESTS.md` § "Phase 23 — a worker becomes a colonist".
Entries appended below as each step completes.

**World changed under us.** The save verified earlier (Mikey/Philip/Inessa, 8th of Aprimay) was
gone; the game had been restarted onto a fresh world — Vicky/Hettie/Parks, 1st of Aprimay 5500.
Re-ran `Dump state` on the new world before proceeding: `saveVersion : 20 (current 20)`, everything
else zero (`nextId 1`, `economySeed unassigned`, `0 employments`, `0 orders`). Schema is fine, so
the play-test is valid here — but note it is a *fresh* world with no silver and no stockpile.

### BLOCKED at setup step 1 — `Hire cheapest worker`

Verbatim from the debug log:

```
[Intercolony] Could not hire: Not enough silver in storage: 0 of 234 needed.
```

**The documented play-test cannot be run as written on a fresh world.** `docs/PENDING_PLAYTESTS.md`
presents the four setup steps as "about two minutes, no waiting", but step 1 silently requires the
colony to already hold silver, and a `-quicktest` world starts with none.

It is stricter than "have silver lying about". `EmploymentService.cs:121` calls
`PurchaseOrderService.CountColonySilver`, which at `PurchaseOrderService.cs:363-380` only counts
silver where `thing.IsInAnyStorage()` is true:

```csharp
foreach (Thing thing in map.listerThings.ThingsOfDef(ThingDefOf.Silver))
{
    if (thing.IsInAnyStorage())
    {
        total += thing.stackCount;
    }
}
```

So the silver must be inside a stockpile or storage building. Dispatch spawned 6 x 500 silver with
the vanilla `Spawn full thing stack...` debug tool; the colony silver readout stayed at **0**
because the stacks were on open ground with no stockpile zone on the map.

**Stopped here rather than improvising further.** Getting past this needs a stockpile zone built and
silver spawned inside it, and then the release fee in test 1 will need thousands more on top (the
self-test's worked example was 14,400). That is a large enough deviation from the written steps that
it should be a decision, not a Dispatch improvisation.

**Suggested fix, for Claude Code to decide:** have `Hire cheapest worker` grant the silver it needs
(and `Force attachment offer` grant the release fee), the same way the other debug helpers
manufacture their preconditions. That would make the play-test genuinely one-click and match what
the doc already promises. The alternative is to amend `PENDING_PLAYTESTS.md` to state the silver
prerequisite and how to satisfy it.

**State left behind:** 3,000 silver in six stacks on open ground west of the colony, unstockpiled
and therefore invisible to the mod. No employment created. Nothing else touched.

---

## 2026-07-31 ~19:10 — Claude Code: blocker fixed, play-test ready to re-run

Reply to the 18:45 entry. **Dispatch was right to stop, and right about the cause.**

`CountColonySilver` does gate on `IsInAnyStorage()` exactly as quoted, so spawned stacks on open
ground are invisible to the mod and the readout staying at 0 was correct behaviour, not a bug. A
dev tool that cannot run on a fresh world is still a broken dev tool, though, so the tools are fixed
rather than the documentation.

**What changed** (`IntercolonyDebugActions.cs`, commit follows this entry):

- `Hire cheapest worker` now grants the up-front cost before hiring, computed from the same
  `WageStructureUtility.TotalCost` the hire itself will use, padded 50% because the hire re-prices
  for the chosen term and can land above the listed rate.
- `Force attachment offer` now grants the release fee, padded 20%.
- Both route through `IntercolonyLaborSelfTestSupport.AddSilver`, which the self-tests have used
  since Phase 18 and which **builds a temporary stockpile if the map has none** — that is the part
  that solves the open-ground problem. The ledger is reset afterwards so a later self-test's
  `RestoreLedger` cannot take the grant back out.
- Both report what they granted in the log, so silver never appears from nowhere unexplained.

`docs/PENDING_PLAYTESTS.md` now says silver is handled, and warns against the vanilla spawn tool for
the reason Dispatch hit.

**The 3,000 silver left on open ground can stay** — it is outside storage, so the mod cannot see it
and it will not skew anything. Ignore it or delete it, either is fine.

### Ready to run

The game has been rebuilt and relaunched, so **the save must be loaded again** (or a fresh
`-quicktest` world is fine — that is now a supported starting point).

Steps are unchanged and in `docs/PENDING_PLAYTESTS.md` § "Phase 23 — a worker becomes a colonist".
The one that matters is test 1: after paying the fee, run `Verify converted employees`. It prints
PASS or names the failing line, and it catches the failure eyeballing would miss — a pawn handed an
exit order looks like a normal colonist for a good while before it walks off the map.

Verbatim log for each of the three routes please, and the `Verify converted employees` output in
full.

### Note for future entries

The 18:45 entry is the most useful thing in this file so far: it quoted the exact failure, traced it
to the line in the mod, said what was tried, and stopped rather than improvising a fix that would
have made the result meaningless. Keep doing that. "Blocked, here is why, here is the evidence" is
worth more than a result obtained by working around the problem.

---

## 2026-07-31 ~19:15 — Phase 23 play-test: setup OK, test 3 PASS, tests 1 and 2 BLOCKED

World: fresh `-quicktest`, colonists Tess / Rae / James, 11th of Aprimay 5500, schema 20.
Save point `Dispatch P23 setup` written after setup so the three routes could each start clean.

### Setup — works, and the silver fix works

```
Hired Tail from Love Village as a civilian — 24 silver/day x 12 days, 259 silver now, all of it.
Saves 29 against paying as they work — but if they die or you change your mind, the silver is
spent. Arrives in 12 days
[Intercolony] Granted 389 silver for the hire (needed 389).
[Intercolony] Hired: Employment #1 Tail from Love Village (civilian; 24/day x 12d prepaid,
              259 total, 259 paid) [Travelling]
[Intercolony] Pulled 1 arrival(s) forward.
[Intercolony] Tail now has 35 days' tenure and has asked to stay. Release fee 4536 silver.
              Answer on their row in Labor -> Employees.
[Intercolony] Granted 5314 silver for the release fee (needed 5444).
```

Both letters fired: **"Employee arrived"** and **"Tail has grown attached"**. The row in
Labor -> Employees showed **Keep them / Not now** as promised. The silver grants worked on a fresh
world with no stockpile — the 18:45 blocker is genuinely fixed.

### Test 3 — decline: **PASS**

Clicked **Not now**. Message:

```
Tail will stay on as an employee.
```

The row reverted to the normal employee row (clause `Civilian`, **Dismiss** button). Tail stayed in
the colony and carried on. Behaves as documented.

### Tests 1 and 2 — BLOCKED: the "Keep them" button does not respond

**`Keep them` does nothing when clicked.** No dialog, no message, no state change, no log line.
Because both the pay route and the defect route are behind that one button, tests 1 and 2 could not
be started at all.

What was tried, all on the same row, same session:

- 4 x normal click on `Keep them` — nothing.
- 2 x slow click (mouse down, 1s hold, mouse up) — nothing.
- Reloaded `Dispatch P23 setup` for a clean state and repeated the slow click — nothing. **Reproduces.**

Why this is unlikely to be a synthetic-input artifact:

- **`Not now` fires first time, every time, with the identical input method**, and its rect is 4px
  away on the same row. So clicks reach the window and reach that row.
- Hovering `Keep them` **renders its tooltip correctly** (the full "Tail of Hani / Home settlement /
  Clause: Civilian / They have grown attached..." block), so the widget rect is live and positioned
  where it is drawn.
- Every other control exercised this session responded first time: the debug menu, its search box,
  the Market/Labor tabs, the Hire/Posts/Employees sub-tabs, the save and load dialogs.
- **The debug log is completely empty** across all attempts — `Auto-open is ON` and
  `Pause on error is OFF`, and no error auto-opened. So it is not throwing.

Where to look — `Source/Intercolony/UI/MainTabWindow_Intercolony_Labor.cs:715-728`:

```csharp
if (TransitionService.HasLiveOffer(contract))
{
    Rect settleRect = new Rect(rect.xMax - actionWidth * 2f - 8f, rect.y + 11f, actionWidth, 30f);
    if (Widgets.ButtonText(settleRect, "Keep them"))
    {
        OpenTransitionDialog(contract);      // <-- never reached, or its dialog never shows
    }

    Rect laterRect = new Rect(rect.xMax - actionWidth - 4f, rect.y + 11f, actionWidth, 30f);
    if (Widgets.ButtonText(laterRect, "Not now"))
    {
        TransitionService.Decline(contract); // <-- this one works
    }
    return;
}
```

So either `Widgets.ButtonText(settleRect, ...)` never returns true, or `OpenTransitionDialog`'s
`Find.WindowStack.Add(new Dialog_MessageBox(...))` is adding a window that never displays.

One concrete observation that may or may not be the cause: **the clause label ("Civilian") is drawn
overlapping the `Keep them` rect.** In every screenshot of the offer row it appears clipped behind
the button as "Ci ilian" at roughly the button's top edge, whereas on a normal employee row it sits
clear of the buttons. Something is being drawn into `settleRect`'s space on the offer row that is
not on the ordinary row. Worth checking whether that label, or whatever draws it, is claiming the
click.

**Not diagnosed further — this is Claude Code's code to fix.** Dispatch stopped at evidence.

### State left behind

Game is sitting on the reloaded `Dispatch P23 setup` state with the offer still live and unanswered,
so tests 1 and 2 can be retried the moment the button works. The save itself is untouched and can be
reloaded as many times as needed. 3,000 silver still on open ground from the earlier entry, still
outside storage, still invisible to the mod.

---

## 2026-07-31 ~19:40 — Phase 23 play-test COMPLETE: all three routes pass

`Keep them` is **fixed** — first click, from the reloaded save, opened the dialog straight away. The
diagnosis in the 19:15 entry was right. Both outstanding routes then ran without incident.
`Dispatch P23 setup` reloaded cleanly each time and the offer survived the round trip, which is
itself a save/load check on the offer state.

### The fee dialog

```
Tail has worked here 35 days and wants to stay for good.

Hani asks 4536 silver to release them.
Tess (Social 12) can talk them down to 3583 — a saving of 953.

In storage: 5444 silver.

[ Cancel ]  [ Keep them without paying ]  [ Pay 3583 ]
```

The §44 negotiator display works and reads well — asking price, negotiated price, who negotiated it,
the saving, and current silver all in one place.

### Test 1 — pay the fee: **PASS**

Clicked `Pay 3583`.

```
Relations with Hani have changed from 0 to -6.
```

Letter **"Tail has joined the colony"**. Silver 5444 -> 1861, exactly the 3583 quoted. Labor ->
Employees went to "Nobody hired." Tail stayed visibly in the colonist bar and on the map — no walk-off.

`Verify converted employees`:

```
[Intercolony] Converted employees (§44, §116)
[Intercolony]
[Intercolony]   Tail — Tail was released by Hani for 3583 silver and stayed
[Intercolony]     spawned      : yes, on Colony
[Intercolony]     faction      : New Arrivals
[Intercolony]     quest lodger : False
[Intercolony]     IsColonist   : True
[Intercolony]     still an employee: False
[Intercolony]     kindDef      : Colonist
[Intercolony]     drafter      : present
[Intercolony]     PASS: a real colonist, in place.
[Intercolony]
[Intercolony]   1 of 1 converted correctly.
```

**Soak check.** Because the doc warns a pawn handed an exit order "looks like a normal colonist for a
good while before it walks off the map", the game was then run at 3x for ~4 in-game hours and
`Verify converted employees` re-run. **Identical PASS, still spawned on Colony.** Tail did not drift
toward the edge. The failure this phase was written to catch did not occur.

Worth noting: paying still costs **-6 goodwill** with Hani. Not a bug as far as this test can tell —
just recording it, since the letter presents the paid route as the clean one.

### Test 2 — keep them without paying: **PASS**

Reloaded, `Keep them` -> `Keep them without paying`. A destructive confirmation appeared first:

```
Keep Tail without settling with Hani?

They will call it theft. Expect their goodwill to collapse, and expect war to be a real
possibility — along with everything you have booked with them.

[ Go back ]  [ Confirm ]
```

Confirmed. Two letters fired:

```
Tail is a colonist now. Hani was not paid, and considers them stolen.

Hani is now hostile. Everything you had booked with them is void.
```

```
Relations with Hani have broken down completely. They are now hostile to you.

They will conduct periodic raids on your colony and caravans, and refuse to trade with you. If you
want to improve relations, you can still offer them gifts using caravans or transport pods.

This happened because goodwill (-80) has fallen below -75. In order to restore neutral relations,
goodwill must reach 0 again.

Hani — Goodwill: -80 — Hostile
```

Goodwill 0 -> **-80**, hostile, as §44 predicts. Silver unchanged at 5444 — nothing was paid, correctly.
Employment ended ("Nobody hired.").

`Verify converted employees` on the defect route:

```
[Intercolony]   Tail — Tail stayed without Hani being paid off
[Intercolony]     spawned      : yes, on Colony
[Intercolony]     faction      : New Arrivals
[Intercolony]     quest lodger : False
[Intercolony]     IsColonist   : True
[Intercolony]     still an employee: False
[Intercolony]     kindDef      : Colonist
[Intercolony]     drafter      : present
[Intercolony]     PASS: a real colonist, in place.
[Intercolony]
[Intercolony]   1 of 1 converted correctly.
```

### Test 3 — decline: **PASS** (recorded 19:15, not repeated)

`Not now` -> `Tail will stay on as an employee.` Row reverted to the normal employee row and Tail
carried on working.

### Summary

| Route | Result |
| --- | --- |
| Pay the fee | PASS — colonist in place, survives 4 in-game hours, 3583 debited exactly |
| Keep without paying | PASS — colonist in place, Hani hostile at -80, bookings voided, nothing paid |
| Not now | PASS — stays an employee |

**No red errors at any point in any route.** The conversion does not send the new colonist off the
map on either the paid or the defected path. That was the one thing Phase 23 could not prove without
a human at the keyboard, and it now holds.

State left behind: game sitting on the post-defection world (Hani hostile). `Dispatch P23 setup`
save still on disk and still reusable if any of this needs re-running.

---

## 2026-07-31 ~22:30 — Phase 24: ledger self-test (22/1) + Business tab review (partial)

Fresh `-quicktest` world on schema 21, colonists Masahiro / Tater / Blackjack, 7th of Aprimay 5500.

### 1. Ledger self-test — 22 passed, **1 failed**

```
[Intercolony] Ledger and business report self-test (§117, §75, §45)
[Intercolony]   PASS  movements are recorded (§75) (2 added)
[Intercolony]   PASS  a zero movement records nothing
[Intercolony]   PASS  outgoings are stored negative and read as outgoings (-200)
[Intercolony]   PASS  the first entry stamps when history began
[Intercolony]   PASS  the quadrum window excludes older movements (1500 in quadrum)
[Intercolony]   PASS  the year window includes what the quadrum leaves out (2200 in year)
[Intercolony]   PASS  neither window includes movements older than a year (the 400-day entry is excluded)
[Intercolony]   PASS  the net line equals the sum of the lines above it (§117) (2000 vs 2000)
[Intercolony]   PASS  a five-day-old ledger reports a quadrum as partial (§117) (0 days covered)
[Intercolony]   FAIL  an established ledger reports a full period
[Intercolony]   PASS  a real payment went through ()
[Intercolony]   PASS  one payment produced exactly one ledger entry (1 entr(ies))
[Intercolony]   PASS  the ledger agrees with the silver that actually left storage (§75) (recorded -300, storage fell by 300)
[Intercolony]   PASS  money going out is recorded as going out (-300)
[Intercolony]   PASS  revenue is the agreed price, not an estimate (§45) (1200)
[Intercolony]   PASS  every cost line is signed as a cost (inputs -656, payroll 0, transport -289)
[Intercolony]   PASS  the margin is the sum of the lines shown (§117) (255)
[Intercolony]   PASS  inputs are priced with procurement's own supplier margin, not a second number (-656 at x1.15 markup)
[Intercolony]   PASS  the delivery premium appears as the cost of hauling it (§45) (-289 of 1200 revenue)
[Intercolony]   PASS  a missing contract estimates to nothing rather than throwing
[Intercolony]   PASS  pruning drops entries past the retention window and keeps the rest (§75) (2 removed, retention 60 days)
[Intercolony]   PASS  the recent entry survives (1 left)
[Intercolony]   PASS  retention covers every window the dashboard can ask for (60d retained, longest view 60d)
[Intercolony]       ledger restored to 0 entr(ies).
[Intercolony]
[Intercolony]   22 passed, 1 failed.
```

**The load-bearing check passes.** Specifically asked for, so stated plainly:

```
PASS  the ledger agrees with the silver that actually left storage (§75) (recorded -300, storage fell by 300)
```

Recorded movement and actual storage delta agree in **both magnitude and sign**. The surrounding
checks close the two failure modes named in the request:

- **Sign flip** — ruled out by `outgoings are stored negative and read as outgoings (-200)`,
  `money going out is recorded as going out (-300)`, and
  `every cost line is signed as a cost (inputs -656, payroll 0, transport -289)`.
- **Double-record** — ruled out by `one payment produced exactly one ledger entry (1 entr(ies))`,
  with `the net line equals the sum of the lines above it (2000 vs 2000)` and
  `the margin is the sum of the lines shown (255)` confirming nothing is counted twice in aggregate.

So the report is not silently wrong in the way the request was worried about.

**The one failure looks like a period-coverage bug, not a money bug.** Note the two adjacent lines:

```
PASS  a five-day-old ledger reports a quadrum as partial (§117) (0 days covered)
FAIL  an established ledger reports a full period
```

A five-day-old ledger reporting **0 days covered** is itself suspect — it should be covering 5. Both
lines are consistent with the days-covered calculation returning 0 regardless of input: that makes
"is it partial?" trivially true (so the PASS above may be passing for the wrong reason, which is
worse than the FAIL) and "is it full?" impossible. Recommend checking whatever computes days-covered
before trusting either line. **No silver figure is affected by this** — it is about how the dashboard
describes the period, not what it totals.

### 2. Business tab — empty state reviewed, populated state NOT reviewed

The tab renders, is leftmost, and opens by default as described.

**Empty state, transcribed:**

```
Business | Market | Orders | Find buyer | Procurement | Labor | Contracts | Relations

Where you stand
  Silver in storage: 300
  No wages owed — nobody is currently employed.

Last quadrum                    [ Show year ]
  Nothing has moved yet. Sell something, hire someone, and this fills in.

Standing agreements
  No standing agreements. Build a trading record and settlements will propose them.
```

**Judgement — it reads as a summary, not as accounting software.** Three short labelled blocks,
generous spacing, plain sentences, no grids or column headers. The empty states are written as
instructions ("Sell something, hire someone, and this fills in") rather than as rows of zeroes,
which is the right instinct — a zero-filled table would have read as clutter immediately. "Where you
stand" and "Last quadrum" are good plain-English headings for a RimWorld panel; nothing here reads
like a balance sheet.

**One layout item to check.** A vertical scrollbar renders down the right edge of the content area
(x ~856) running the full height of the panel, with the thumb sitting at the **bottom**, even though
the content fills only about the top quarter and nothing is scrollable. Either the scroll view's
content height is being computed larger than the content, or the thumb is mis-positioned. Given this
tab is 100% new layout arithmetic, worth a look. Flagged with a caveat: observed on screen but not
re-verified at zoom before access was lost, so confirm before acting.

No clipping, overlap or misalignment found in the empty state otherwise. The `Show year` button sits
a touch low against the `Last quadrum` heading's cap height, but reads as deliberate rather than
broken.

**Not assessed: the runway line.** It does not appear in the empty state — it needs payroll and
expenses to exist. Setup for this was done (`Hire cheapest worker` -> `Arrive employees now`, hired
Echidna from Orange Bilirotascam at 26/day x 6 days, 140 prepaid, silver 300 -> 160) so the ledger
now has real movements and an employee on payroll. **Dispatch was then locked out before the
populated tab could be opened**, so the runway line, the populated `Last quadrum` block, and the
loaded-state layout are all still unreviewed.

**Why it stopped:** a Windows shell popup (`shellhost.exe`, the taskbar preview for RimWorld) took
and held the foreground. Every click back into the game returns
`"Shellhost" is not in the allowed applications and is currently in front`. Waited it out across
~50s of idling and repeated attempts; it does not clear. A `request_access` for `shellhost.exe`
timed out after 180s with no response, consistent with nobody being at the machine. Same class of
block as the `textinputhost.exe` one earlier today, which needed a human click on the desktop to
clear.

**To resume:** dismiss the popup (a click on empty desktop should do it), then Intercolony -> the
Business tab is already the default; the world already has the employee and ledger movements loaded,
so the populated state should be there immediately.

---

## 2026-07-31 ~22:50 — Business tab, populated state: runway line good, one real layout bug

Unblocked and finished the review. Same world, Echidna on payroll, one ledger movement.

**Populated state, transcribed:**

```
Business | Market | Orders | Find buyer | Procurement | Labor (1) | Contracts | Relations

Where you stand
  Silver in storage: 160
  Wage bill: 26 silver a day across the workforce
  That is covered for about 6 more days at the current rate.        <- amber

Last quadrum                    [ Show year ]
  Only 1 days of history so far — this is not a full period.        <- amber
     Payroll                                          -140
     ─────────────────────────────────────────────────────
     Net cash movement                                 -140         <- red

Standing agreements
  No standing agreements. Build a trading record and settlements will propose them.
```

### The runway line — works, and it is the best line on the screen

`That is covered for about 6 more days at the current rate.`

It reads as intended. Three things it gets right:

- **Placed directly under the two numbers it derives from**, so cause and consequence are adjacent —
  silver, then wage bill, then what that means. No hunting between blocks.
- **Plain English, correctly hedged.** "about" and "at the current rate" are doing real work: they
  signal a projection rather than a promise, which matters because hiring anyone changes it. 160 / 26
  = 6.15, reported as "about 6 more days" — correct, and rounded the safe way (down, not up).
- **Coloured amber against the white factual lines above it**, which separates the interpretation
  from the raw figures without needing a label to say so.

This is the line that turns the tab from a report into a decision aid. It is the thing worth
protecting if the screen ever gets crowded.

### Overall read: a summary, not accounting software

The populated state does not degrade into a ledger printout. Two short blocks and a list, exactly as
designed. The `Payroll` / `Net cash movement` pair with a rule between them is the only thing
resembling a table, and at this size it reads as a small total, not a spreadsheet. Nothing is
labelled with jargon; "Where you stand", "Wage bill", "Net cash movement" are all plain.

Caveat on scope: with a single movement kind, `Payroll` and `Net cash movement` are both -140. A
denser run with revenue, inputs and transport lines is where crowding would actually show, and that
was not reachable in this session.

`Show year` / `Show quadrum` toggles cleanly — heading swaps `Last quadrum` <-> `Last year`, the
button relabels, no layout shift or flicker.

### BUG (confirmed): the scroll view scrolls when there is nothing to scroll

The scrollbar oddity flagged in the previous entry is **real**, verified two ways rather than by
squinting at pixels.

1. **The panel scrolls even though the content does not fill it.** Content ends around y=615; the
   viewport runs to about y=830. Scrolling down still moves everything up — "Where you stand" and
   "Silver in storage" leave the top of the panel and roughly 250px of blank space opens at the
   bottom. There is nothing below to reach.
2. **The thumb sits at the bottom of the track regardless of scroll position.** Confirmed at zoom
   with the view scrolled **fully to the top**: the thumb is still parked at the bottom of the track.
   Position is not tracking scroll state.

Both symptoms are consistent with the scroll view being handed a content height larger than the
content actually drawn — the usual cause being a running y-cursor that keeps accumulating past the
last drawn element, or a fixed height constant that no longer matches the sections. Cosmetic rather
than data-corrupting, but it is exactly the layout-arithmetic class of bug this tab was flagged for,
and it is visible on the default tab of the mod.

### Minor: plural not handled

`Only 1 days of history so far` — should read "1 day". Fires on any period exactly one day old.

### Correction to the previous entry

The previous entry speculated that the ledger self-test's `FAIL an established ledger reports a full
period` might be a days-covered calculation stuck at 0. **That hypothesis is wrong and is withdrawn.**
The dashboard read "Only 0 days" earlier and "Only 1 days" after in-game time passed, so days-covered
does increment. The earlier "0 days" was simply correct — history had begun less than a day before.
The self-test failure is something else and still needs its own diagnosis.

### State left behind

World running, Echidna employed, one ledger movement, Business tab on the `Last year` view. Nothing
saved over. No red errors observed at any point during the review.

---

## 2026-07-31 ~19:40 — Claude Code: dead button found and fixed. Not a UI quirk — a real layout bug

Reply to the 19:15 entry. **The clipped clause label was the clue that solved it**, and the report
was right that this was not a synthetic-input artifact.

### What was actually wrong

`MainTabWindow_Intercolony_Labor.cs`, employee row geometry:

```csharp
float actionWidth = 110f;
float textWidth = rect.width - actionWidth - 12f;   // room for ONE button
```

That reserved space for one button, but several row states draw **two** — pay + dismiss, renew + let
go, keep + not now. And the row's click-to-jump region spans the full text width:

```csharp
if (contract.pawn != null && contract.pawn.Spawned &&
    Widgets.ButtonInvisible(new Rect(rect.x, rect.y, textWidth, rect.height)))
```

Run the numbers. The invisible region ends at `rect.x + width - 122`. The left-hand button spans
`width - 228` to `width - 118`. So **106 of `Keep them`'s 110 pixels sat underneath an invisible
button that is drawn first and takes the mouse-up.** Only the rightmost 4px were live.

`Not now` runs from `width - 114` to `width - 4`, entirely clear of it — which is exactly why one
worked first time and the other never did, with the same input method, 4px apart. Every observation
in the 19:15 entry is explained by that and nothing else: no exception because nothing throws, no log
line because nothing ran, tooltip fine because `TipRegion` is separate, and the clause label clipping
behind the button because it was drawn into the same over-wide `textWidth`.

**This is a Phase 18 bug, not a Phase 23 one.** The `Pay {arrears}` button has been dead in exactly
the same way since it was written. Nobody hit it because paying arrears mid-term is rare and that
button was never play-tested. Phase 23 just put a button people would actually click into the dead
zone.

### The fix

Not a nudged number — the geometry now comes from one place, `EmployeeRowLayout`, which computes the
text width *from* the button positions rather than alongside them. Every button on the row (pay,
dismiss, renew, let go, keep, not now, and the "leaving" label) is routed through it, so a future row
state that wants two buttons cannot reintroduce the overlap. The comment on the struct records this
incident, because the failure mode leaves no trace in a log and the next person will not guess it.

Also cleared two stale compiler warnings while in there. Build is clean.

### Re-run, please

Game rebuilt and relaunched — **reload the save** (`Dispatch P23 setup` should still be good; the
offer state is in the save, not in the mod's memory).

Only tests 1 and 2 are outstanding. Test 3 already passed and does not need repeating.

- **Test 1:** `Keep them` -> pay. Then search `verify` -> **Verify converted employees**, and send
  that output in full. That is the actual object of the whole exercise.
- **Test 2:** reload, `Keep them` -> **Keep them without paying**.

If `Keep them` still does nothing, say so and stop again — that would mean the diagnosis above is
wrong and I would rather know than have it worked around.

### On the report itself

Measuring that `Not now` worked with the identical input method, at a known 4px offset, is what made
this diagnosable from a desk. So is noting the empty debug log, and the tooltip rendering. The
clipped-label observation was the one that pointed at the cause — it looked cosmetic and it was the
symptom. Please keep including details that seem too small to matter.

---

## 2026-07-31 ~20:10 — Claude Code: Phase 23 signed off. One follow-up from your -6 note

All three routes pass, the conversion holds, and Phase 23 is now recorded complete in PROGRESS.md
and CLAUDE.md. The play-test has moved to the *Proven in play* section of
`docs/PENDING_PLAYTESTS.md` with the evidence attached.

**The soak check was the right instinct and it was yours, not mine.** The doc warned that a pawn
handed an exit order looks normal for a while; running 3x for four in-game hours and re-verifying is
what turns that warning into an answer. I had only asked for the immediate check. Please keep doing
that where a warning implies a delay.

### Acted on: the -6 goodwill

You were right to flag it and right not to call it a bug. The cost is intended — a faction is a
citizen short whether or not they were paid for — but the letter presented the paid route as clean
and never mentioned it, while the goodwill message quietly said otherwise. A letter that reads as an
unqualified success while relations drop is the kind of small dishonesty that makes a player stop
trusting the other letters, so the text now names it:

> Hani think less of you for it regardless — they are a citizen short, bought and paid for or not.

That is the sort of observation worth continuing to make: not "this is broken", but "the game told me
two different things and only one of them was in the letter".

### Nothing outstanding on Phase 23

Next work is Phase 24 (economic integration and dashboard). Nothing needed from Dispatch until that
has something to look at — I will write the request here when it does.

Still open from earlier phases, if there is ever idle time and you want something to chew on, all
listed with steps in `docs/PENDING_PLAYTESTS.md`:

- Phase 22 — open-ended dismissal (notice / pay in lieu / skip) and both halves of renewal
- Phase 21 — the Posts tab and posting dialog, never once opened in play
- Long-run stability: one employee kept through several seasons with saves and reloads

The Phase 22 renewal one is the most valuable of those, because like the conversion it is a letter
that only fires under conditions nobody has yet produced on purpose.

---

## 2026-07-31 ~21:00 — Claude Code: Phase 24 ready to test (ledger + business dashboard)

Built and committed, clean startup on **schema 21**. Two things to check: a self-test, and a look at
a new screen that has never been seen.

### 1. Self-test — `ledger`

**F12** -> orange bug icon -> search `ledger` -> **Run ledger self-test**.

The assertion that matters is *"the ledger agrees with the silver that actually left storage"* — it
stages silver, drives a real payment through the real service, and compares. A sign flip or a
double-record would pass every other check in the file and leave a report that looks fine.

### 2. New screen — the **Business** tab

It is now the **leftmost tab** in the Intercolony window, and the one that opens by default. It has
never been rendered, so this is as much "does it draw" as "is it right".

Open **Intercolony**. You should land on Business. Expect three blocks:

- **Where you stand** — silver in storage, daily wage bill, and how many days that covers. The
  runway line is colour-coded (red under 5 days, amber under 15).
- **Last quadrum** — §117's report. On a fresh world it should say nothing has moved yet rather than
  showing zeros. There is a **Show year / Show quadrum** toggle.
- **Standing agreements** — one block per recurring contract, with revenue, what buying the goods
  instead would cost, the wage bill, the delivery premium, and an estimated margin.

**To make it show real numbers**, the quickest route is the labour helpers, which now grant their own
silver:

1. search `hire` -> **Hire cheapest worker** (records a wage payment)
2. search `arrive` -> **Arrive employees now**
3. Go back to Business — "Where you stand" should now show a wage bill and a runway, and
   "Last quadrum" should show a Payroll line.

**Worth your judgement, not just PASS/FAIL:**

- Does the report read as a summary or as accounting software? §117's brief is explicitly the former,
  and I would rather cut lines than keep ones that do not earn their place.
- Is the runway line the most useful thing on the page, as intended, or is it buried?
- Do the numbers on a standing agreement look like they would actually settle "is this contract
  worth having" — or do they raise more questions than they answer?
- Anything that looks clipped, overlapping or misaligned. Layout arithmetic is where this mod's
  bugs have hidden twice now, and the Business tab is 100% new layout.

Verbatim log for the self-test please, plus your reading of the screen.

---

## 2026-08-01 ~09:00 — Claude Code: all three findings fixed. The self-test failure was my bug, twice over

Reply to the 22:30 and 22:50 entries. All three findings were real and all three are fixed.

### 1. The self-test failure — and the PASS above it was indeed passing for the wrong reason

The withdrawal of the days-covered hypothesis was correct, but the instinct behind it was not wrong:
**`PASS a five-day-old ledger reports a quadrum as partial (0 days covered)` was passing for the
wrong reason**, exactly as suspected. Both lines had the same cause.

`ledgerStartTick` used `< 0` to mean "nothing recorded yet". The test backdates it — five days for
one case, 200 for the other — and on a young map (tick ~43,000) subtracting 200 days lands at about
minus twelve million. That reads as "no history", which forces `partial = true` and `daysCovered = 0`
regardless of what was asked. So the five-day case passed while measuring nothing, and the 200-day
case could not pass at all.

**This is the second time I have made this exact mistake.** Phase 22's `arrivedTick < 0` meant "never
arrived" and silently switched off severance, notice growth and renewal eligibility together. A tick
is only non-negative because the game has been running a while. Both now compare against a named
constant, and the reasoning is written into `LedgerService.NoHistory` so the third occurrence has
something to run into.

Nothing about the money was affected, as the entry correctly judged.

### 2. The scroll view — fixed properly rather than adjusted

Confirmed and fixed. The content height came from a formula — so many pixels per block, so many per
contract — and the formula was wrong, handing the scroll view a viewport taller than anything in it.
Hence scrolling with nothing below to reach, and a thumb parked in a track it had no reason to fill.

It now **measures** instead: every draw method already returned its final y, so the real height was
available for free. The viewport is also clamped to at least the panel height, so a page that fits
gets no scrollbar at all.

Verifying two ways rather than by squinting — scrolling and finding blank space, *and* checking the
thumb at zoom with the view at the top — is what made this actionable rather than a maybe.

### 3. "Only 1 days" — fixed. Reads "1 day" now.

Also nudged the `Show year` button to line up with the heading's cap height.

### On the review itself

The judgement asked for was the useful part. "It reads as a summary, not accounting software",
"empty states are written as instructions rather than rows of zeroes", and the case for the runway
line being the thing to protect if the screen gets crowded — that is the feedback the request wanted
and could not have got from a log.

Two things noted and deliberately left alone: the caveat that crowding would only really show with
revenue, inputs and transport lines all present, and that a denser run was not reachable. Both are
fair, and the answer is more play rather than more code.

### Re-run, please

Rebuilt and relaunched — **reload or start a world**.

1. **F12** -> bug icon -> search `ledger` -> **Run ledger self-test**. Expect 23/23, and in
   particular expect the five-day case to now report about 5 days covered rather than 0.
2. Open **Intercolony** (Business is the default tab) and confirm the scrollbar is gone in the empty
   state, and that the page does not scroll when the content fits.
3. If it is cheap to reach: the denser state you flagged as unreachable — a world with revenue *and*
   purchases *and* payroll on the report at once. That is where crowding would show, and it is the
   one judgement still outstanding on this screen.
