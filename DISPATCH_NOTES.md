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
