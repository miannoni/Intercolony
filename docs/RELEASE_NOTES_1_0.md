> **UNRELEASED - prepared release body. Delete this line when 1.0 ships.**

# Intercolony 1.0 - release notes

Intercolony 1.0 is not released. The `1.0` branch has not merged, and the 1.0 play sitting has not
happened. These are the prepared notes for that release.

Compared with 0.9.3, settlements now give the player a changing regional economy to read, trade
into, and build a reputation within.

## What's in it

- **Settlements have real economies.** Settlements differ in what they produce, need, and can
  supply. Their shortages and surpluses persist, drift over time, and normalise instead of being
  replaced by unrelated market noise every cycle.
- **Events create circumstances worth trading into.** A circumstance can change what a settlement
  needs or can supply, giving the colony a reason to act on the market while that condition matters.
- **What your colony is KNOWN for matters.** Quality goods build product-specific brand strength.
  Your reputation carries most strongly within the relevant product family, so a colony known for
  one kind of work is not automatically known for everything.
- **Deals can be negotiated.** Important offers can be countered within bounded rules. When a deal
  you accepted becomes difficult to meet, you can request a constrained renegotiation; the original
  obligation remains in force unless the new terms are explicitly accepted.
- **Buying is a full system.** Find supply in the supplier market, receive and compare quotations,
  place purchase orders, and use standing procurement agreements when a one-off purchase is not
  enough.
- **Relationships have a history.** Open a settlement's relationship view and see the commercial
  history of your colony's dealings with it in one place: completed and failed work, purchases,
  agreements, renegotiations, brand milestones, and other lasting commercial moments.

## Compatibility

Existing saves upgrade from schema 42 through schema 56. No existing obligation changes price or
quantity; that is asserted, not hoped.
