# Backlog

Ideas worth building that are **not** mapped into a numbered phase in `DESIGN.md`.

**Why this file exists.** `DESIGN.md` holds the plan; `PROGRESS.md` holds what happened;
`PENDING_PLAYTESTS.md` holds what shipped but is unproven. None of them hold "we should build this
one day". Without somewhere to put those, an idea either gets forced into the current phase — which
is how a phase quietly becomes "whatever we thought of this week" — or it is lost when the
conversation moves on.

**How to use it.** Add an item when something is worth doing but is not this phase's work. When an
item is picked up, map it into a numbered phase in `DESIGN.md` and strike it here with a pointer,
rather than deleting it — the next person asking "was this ever considered?" gets an answer.

Nothing here is committed to. An item may be rejected later; record that too, with the reason.

---

## Procurement should be as complete a system as selling

**Raised:** 2026-08-07, by Matteo, while looking at the tab structure.
**Size:** large — a phase of its own, probably more than one.
**Status:** open, deliberately deferred until after 1.0.

Selling has four surfaces. Buying has one. The asymmetry is not a design decision, it is just the
order things were built in: selling came first and grew, procurement was built once in Phase 11 and
never revisited.

| Selling has | Buying has |
|---|---|
| **Market** — buyers advertise what they want | *nothing* |
| **Find buyer** — you go looking for a buyer | **Procurement** — the RFQ flow, which is exactly this |
| **Orders** — what you committed to sell | partial: purchase orders exist but have no screen of their own |
| **Contracts** — recurring supply agreements you fulfil | *nothing* |

### What already exists — do not rebuild it

- **The RFQ flow is "find seller".** `RfqService`, `PurchaseRequest`, `Quotation`. The player states
  what they need, suppliers answer with full or partial quotes, the player picks one. That is the
  mirror of Find Buyer and it works.
- **Purchase orders exist** — `PurchaseOrder`, `PurchaseOrderService`, with delivery and collection,
  a pickup grace period, refunds on supplier failure, and payment taken at acceptance.
- **Fulfilment terms are chosen on the request** as of 2026-08-07: supplier delivers, we collect, or
  either.
- **Quotes sort by column** as of the same date.

### What is genuinely missing

1. **A supplier market.** Settlements advertising what they have surplus of, the inverse of
   `MarketOpportunityGenerator`. The player browses rather than asks. This is the largest piece and
   the one with the most new state.
2. **A purchase orders screen.** Purchase orders are currently visible only inside the Procurement
   tab alongside live requests. They deserve the same treatment sales orders get.
3. **Recurring procurement contracts.** Matteo's phrasing: *"Recurring procurement contracts that I
   offer to suppliers, why not?"* The player offers a standing agreement — so much, so often, at
   this price — and a supplier accepts or declines. The mirror of `SupplyAgreement`, with the
   direction of the offer reversed: today the settlement proposes and the player answers, here the
   player proposes and the settlement answers.

### Open design questions

- **Who proposes?** For sales agreements the settlement proposes and the player accepts. Reversing
  that for procurement means the player names terms and a supplier judges them — which needs a
  supplier-side acceptance model that does not exist yet. Closest existing thing is
  `JobPostingService`'s "you name the terms, they answer", which is the same shape and worth reading
  first.
- **Can a supplier default on a recurring contract?** §125 Procurement already asks "Can suppliers
  default?" and it is still open. A recurring commitment makes that question unavoidable rather than
  theoretical.
- **Does the player pay per cycle or up front?** Purchases currently take payment at acceptance
  (`PurchaseOrderService`, and the comment there notes paying on delivery needs a debt and default
  policy that does not exist). A recurring contract makes up-front payment implausible.
- **Does a supplier market make the RFQ flow redundant?** Probably not — browsing and asking are
  different actions, exactly as Market and Find Buyer are. Worth confirming rather than assuming.

### Why it is deferred

It is four systems, not one, and each has a live counterpart on the selling side that already works.
Nothing about the current mod is broken without it. It belongs after 1.0, when the selling half has
been played enough to know which of its four surfaces actually earn their place — because the answer
to that should shape which of the four get mirrored.

---

## Rejected or superseded

*(nothing yet)*
