# Intercolony 0.9.2 — animal sales and procurement fixes

Intercolony 0.9.2 is a bug-fix release. It repairs animal selling, makes hiring costs visible before
you commit, and closes several ways that procurement and multi-colony trading could behave
unexpectedly. There are no new features in this release.

## Fixes

- **Animal sales now work.** Marking an animal order ready previously did nothing — no message and
  no error. In a mixed flock, even one animal that did not match the order could block the entire
  sale. That is fixed and proven in play: a chicken and a bonded labrador were both sold, with the
  bonded-animal warning naming the correct colonist.
- **The employee signing fee is shown before you hire.** It now appears when you create a job
  posting and beside every applicant, together with your current silver. Previously the fee was
  disclosed only after a hire was refused for insufficient silver, too late to plan or save for it.
- **Procurement quotes cannot be re-rolled.** Withdrawing a request and raising it again now returns
  the same quotes until the market refreshes, instead of offering new prices that could be gamed by
  retrying.
- **A supplier's stock is finite within each market window.** Buying out a supplier and submitting
  another request no longer restores that supplier's offer. Stock returns when the market refreshes.
- **Accepting one quotation leaves the rest of a purchase request open.** If you request 1,000
  steel and accept an offer for 300, the remaining 700 stays wanted and the other offers remain
  available. Previously the first accepted quotation closed the whole request.
- **Orders and deliveries use the correct colony.** In games with more than one colony, buyer
  collection could take goods from a base that never offered them. Collection now refuses instead
  of substituting another colony. Procurement deliveries and refunds fall back to another colony
  only when they must, and their messages name the colony that received them.
- **The running mod version is reported at startup.** Bug reports can now be tied to the exact build
  that produced them.

## Requirements

- RimWorld **1.6**
- **Harmony**, loaded before Intercolony

## Saves

Saves from 0.9.0 and 0.9.1 upgrade automatically. The upgrade has been verified on a real existing
save. Once upgraded, a save cannot be opened again by an older version of the mod.

**Keep normal backups of any save you use with this pre-release.**
