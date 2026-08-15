# Intercolony 0.9.1 — agreements, prices and corrections

Intercolony 0.9.1 fixes what the first beta players ran into: orders, pickup, purchasing and prices
now behave the way the screen says they will. It also adds the other half of the agreement system —
until now a settlement could offer you a standing supply deal, but you could not offer them one —
and retunes trade and labor after the first public beta sessions.

## Fixes

- **Purchases stay with the colony that placed them.** Delivered goods now arrive at the colony
  that ordered and paid for them, and refunds return there too, rather than going to the first
  player colony.
- **Refund messages now match the silver actually returned.** If no refund can be placed, the
  order is held for another attempt instead of being closed and reported as paid. If only part can
  be placed, the message and business report use the amount actually returned.
- **Mark Ready no longer invents a shortage.** Stock already promised elsewhere is now subtracted
  from all matching stock, removing the false “0 units free” block when the colony still has enough
  to fill the order.
- **Find Buyer now shows the price you will actually receive.** The listing uses the same pickup
  terms as the confirmation, so the rate you see is the rate the order is created at.
- **Find Buyer no longer promises the same stock twice.** Its stock list accounts for existing
  commitments, updates while the page is open, and checks availability again when a sale is
  created or an order is marked ready. The goods are still not physically locked; colonists and
  bills can consume them.
- **Buyer pickup uses the right colony and the right clock.** Collection happens at the colony
  where the goods were marked ready. The deadline is now the time to mark them ready; once the
  buyer is travelling, a long journey no longer fails an order you prepared on time. The displayed
  pickup estimate uses the same timing as the arriving buyer.
- **Purchase history remains visible.** Completed, cancelled, defaulted and lost-to-war purchases
  stay under Procurement → Orders with their outcome. A fulfilled purchase request is shown as
  ordered rather than cancelled.
- **Generated orders respect their transport limits.** Better commercial standing can still grow
  an order, but it can no longer push crated buildings or non-stackable goods past their hard
  travel limits.

## New

- **Propose your own supply agreements.** A new window on the Contracts page lets you pick a
  settlement, an item, a quantity and a price, and shows what a delivery pays, how often deliveries
  fall due, how many there will be and what the whole agreement is worth — all before you commit.
  You need a settlement you have already sold that good to at least twice, and enough standing with
  them. It is the formal version of business you were already doing through Find Buyer by hand.
- **Proposals are answered, not granted.** A proposal is *sent*. The settlement replies after a
  while, and it can say no. A very good offer and a very poor one are both answered quickly — the
  first out of courtesy, the second out of disinterest — while a middling offer takes longest,
  because that is the one they genuinely have to weigh. Reloading does not change the answer.
- **You set the price, and it changes how the faction sees you.** The price runs from nothing up to
  twice the going market rate and starts at the going rate. Selling below it is treated as
  generosity: when a delivery completes, standing with the buyer's faction improves. Selling above
  it costs standing. Charging over the odds can cool a relationship a long way, but it will never
  by itself turn a faction hostile.
- **Unwanted agreement offers can be switched off.** Settlements proposing agreements to you can be
  turned off completely, or limited to particular kinds of goods, from the top of the Contracts
  page.

  **This is off by default, including in saves from an earlier version.** If you were receiving
  agreement offers before and they stop, that is why — turn them back on at the top of the Contracts
  page. Offers you had already been sent are not deleted.
- **Trade history no longer grows without limit.** The hundred most recent closed sales and the
  hundred most recent closed purchases are kept in full; older ones are dropped. **Clear completed
  history** is also available on the closed sales list, the concluded purchases list and the
  concluded requests list.

  Clearing removes only finished records. Your reputation, your ledger, anything still in progress,
  your agreements, and the trading record that qualifies a settlement for an agreement all survive
  it untouched.

## Changes

- **A buyer's “Will take” amount is now real.** Committing or completing a sale reduces what that
  settlement still wants. Cancelled or failed orders release that amount, completed sales stop
  counting after the next market refresh, and open orders continue to count.
- **What settlements want now changes over time.** Each market refresh can move local interest in
  a good instead of leaving it fixed forever. Because interest affects value, **quoted prices and
  standing-agreement offers can now move when the market refreshes**. An order you already accepted
  keeps its agreed unit price.
- **Standing supply agreements follow your trading history.** A settlement now offers an agreement
  only for a good you have successfully sold to it at least twice. When accepting, you can adjust
  the amount slightly, choose who travels, and let your best available negotiator improve the rate.
- **Procurement is easier to follow.** It now uses Market, Find seller, Orders and Contracts
  sub-tabs like Selling. Open purchases can be cancelled from the screen after a confirmation that
  states the lost payment and effect on your trading record.
- **Purchase requests can be more specific.** For suitable goods, you can request a material and a
  minimum quality. Suppliers that cannot meet the requested quality do not quote. New requests
  start with supplier delivery selected.
- **Common no-caravan choices are selected first.** Find Buyer and standing-agreement dialogs now
  open with buyer pickup selected; existing orders and agreements keep their saved terms.
- **Trade and labor have been rebalanced.** The 100% trade setting now uses the tighter economy
  tested after 0.9.0. Worker wages at 100% are three times the original beta rate. Paying up front
  remains cheapest, per-quadrum pay sits in the middle, and daily pay costs the most; pay-as-you-go
  hiring also has a signing fee. The two sliders reset to 100% once because their scales changed.
- **Buy-only goods can be opted into.** A default-off setting can make categories such as stone
  blocks and cooked meals sellable. The setting changes their normal RimWorld tradeability, so it
  affects other traders and mods as well as Intercolony.

## Requirements

- RimWorld **1.6**
- **Harmony**, loaded before Intercolony

## Known limits and not yet verified

- Proposing your own agreements, the price lever and the effect on faction standing are new in this
  release and have had only light play-testing.
- The Mark Ready, displayed-price, changing “Will take” amounts, refresh-to-refresh price movement
  and oversized-order corrections have had limited play-testing.
- Animal trading exists in the development tree but remains wholly unplayed. It is not presented
  here as a verified 0.9.1 feature.
- Buyer pickup from a second home colony has been exercised; pickup from a non-home or camp map has
  not.
- Find Buyer commitments are logical, not physical reservations. Colony activity can still consume
  promised goods, and seller-delivery cargo may remain counted as committed until its order closes.
- Testing remains limited to the environment described for 0.9.0. Unowned DLC and most mod
  combinations are untested rather than declared unsupported.

**Keep normal backups of any save you use with this pre-release.**

## Saves

0.9.1 uses a newer save format, and an existing save is upgraded automatically when you load it.
Once upgraded, that save cannot be opened again by an older version of the mod.

Saves from several earlier versions have been upgraded and reloaded successfully. The last two
upgrade steps in this release have only been exercised on new colonies, not on a save made before
them. **Keep a backup of any colony you care about before loading it with this pre-release** — which
is sensible practice for a beta in any case.
