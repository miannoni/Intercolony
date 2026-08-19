# Intercolony 0.9.3 — readable dialogs, and job postings that stay up

Intercolony 0.9.3 is mostly a presentation release. The trade and labor dialogs were rebuilt to state
their terms as labelled rows instead of paragraphs, so what you are agreeing to and the number you
get are visible at a glance. One thing genuinely behaves differently: job postings no longer name a
number of positions and no longer expire.

## The one behaviour change

- **A job posting has no position count, and never expires.** You post a job once and hire as many
  applicants as you like from it; it stays up until you take it down or it fills. The position
  spinner and the Advertise duration slider are both gone from the posting form.
  **Postings created before this release keep their original expiry** and will still lapse when it
  runs out — only new postings are open-ended. If an old posting disappears on you, that is why.

## What you will notice

**Selling**

- **The sell dialog no longer overdraws itself.** Long confirmations could paint text on top of
  their own buttons, which was a real defect and not just an ugly one. Dialogs now measure their
  text and size to it.
- **The sell dialog states its terms as rows.** Price, quantity, payment, deadline and fulfilment
  are labelled values with the explanation moved into tooltips, instead of six paragraphs where two
  unrelated numbers could read as one.
- **Sales orders are a sortable table, and each row shows what the order is worth.** Numeric columns
  are right-aligned so they can be compared down the column.
- **You can mark a sale ready as you create it.** The Find Buyer confirmation now offers
  *Mark ready now*, on by default, with a setting under Mod options if you would rather it were not.
  If the goods are not actually there it refuses and keeps the dialog open rather than creating an
  order you cannot fulfil.
- **The market acceptance dialog reads as rows too**, matching the sell dialog.

**Labor**

- **The Post a job form was rebuilt on a grid** and fits on one screen again, with the sliders given
  room for the labels they draw above themselves and an even spacing throughout.
- **It offers its terms the way the Hire dialog does**, so the two screens no longer disagree about
  how the same choice is presented.
- **The signing fee is named rather than labelled "Due now."**
- **The "No end date" checkbox no longer sits on top of the term slider.**

## Requirements

- RimWorld **1.6**
- **Harmony**, loaded before Intercolony

## Saves

**The save format did not change.** Saves stay at schema 42 and no migration runs — 0.9.2 saves open
directly.

Because job postings lost their position count, an old save carries a leftover `positions` value
with nothing to read it into. RimWorld's loader ignores unmatched fields, and this was verified
before release on a real save carrying job postings with that field present: the save loaded at
schema 42 with no errors, no posting was dropped, and an existing posting rendered correctly with
its original expiry intact.

**Keep normal backups of any save you use with this pre-release.**
