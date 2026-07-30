# Intercolony — Product & Technical Design Specification

> **Document role:** North-star specification for the Intercolony RimWorld mod.  
> **Audience:** Human contributors and coding agents such as Claude Code.  
> **Status:** Living document.  
> **Product vision:** intentionally ambitious.  
> **Implementation strategy:** aggressively incremental.

---

# 0. Instructions to the coding agent

This document describes the intended **finished product**, not a request to implement everything at once.

Treat it as:

- a product vision;
- a domain model;
- a roadmap;
- a set of constraints;
- a source of acceptance criteria;
- a list of deliberate non-goals;
- a record of design intent.

The desired workflow is:

1. inspect the existing repository;
2. inspect the actual target RimWorld version and available assemblies;
3. determine what already exists;
4. choose the smallest milestone that can move the mod forward;
5. implement a narrow vertical slice;
6. compile;
7. launch RimWorld with the mod enabled;
8. inspect logs;
9. test the happy path;
10. test failure states;
11. save and reload during the feature;
12. document important discoveries;
13. commit a coherent unit of work;
14. only then widen scope.

Do **not** blindly trust class names, patch points, serialization APIs, or modding patterns written in this document. RimWorld changes over time and the repository may target a specific game version. Any implementation-specific suggestion here must be verified against the target game's assemblies and current modding environment.

Do not over-architect the first implementation.

A correct 150-line vertical slice that can be played is better than a 3,000-line abstraction framework for systems that do not yet exist.

The product vision is firm enough to guide architecture, but balance numbers, exact UI, formulas, terminology, and even some subsystem boundaries are expected to change through playtesting.

---

# 1. One-sentence pitch

**Intercolony turns RimWorld's surrounding factions and settlements into a civilian economy where the player can deliberately sell products, procure goods and equipment, hire outside workers, fulfill contracts, and build persistent commercial relationships.**

---

# 2. Problem statement

RimWorld already provides deep systems for:

- farming;
- ranching;
- mining;
- crafting;
- construction;
- storage;
- health;
- combat;
- caravans;
- pawn skills;
- work priorities;
- social relationships;
- colony growth.

The world map also contains factions and settlements that imply a larger civilization.

However, the player's normal economic relationship with that civilization is comparatively opportunistic:

- wait for traders;
- visit settlements;
- buy whatever happens to be available;
- sell whatever a trader happens to accept;
- complete occasional quests.

Intercolony adds a more intentional civilian economy.

The player should be able to ask:

- Who currently needs what I produce?
- Who can supply what I lack?
- Which customer pays enough to justify the trip?
- Can I commit to this order before I have produced it?
- Is a recurring contract worth expanding my farm?
- Should I buy a production bench instead of manufacturing one?
- Should I hire seasonal labor instead of recruiting another permanent colonist?
- Do I want to become a food exporter, a furniture manufacturer, an art studio, or a weapons producer?
- Can I afford this contract once wages and logistics are considered?
- Is this settlement becoming one of my major customers?

The central fantasy is not "numbers go up."

It is:

> **My colony can become an economic organization embedded in the world.**

---

# 3. Core player loop

The primary loop is:

```text
Observe demand
    ↓
Choose an opportunity
    ↓
Commit to an order
    ↓
Plan production
    ↓
Procure missing inputs / equipment
    ↓
Hire labor if needed
    ↓
Produce and stage goods
    ↓
Deliver or arrange pickup
    ↓
Receive payment
    ↓
Build reputation
    ↓
Unlock larger / recurring opportunities
    ↓
Expand capacity
```

The purchase-side loop is:

```text
Identify a shortage
    ↓
Create a purchase request / RFQ
    ↓
Receive zero, partial, or competing quotations
    ↓
Choose supplier(s)
    ↓
Pay / commit
    ↓
Collect or receive goods
    ↓
Use goods to survive, produce, or expand
```

The labor loop is:

```text
Identify labor need
    ↓
Browse workers or post a job
    ↓
Compare skills / wage / contract terms
    ↓
Hire
    ↓
Worker arrives
    ↓
Assign work
    ↓
Provide wages / living conditions / safety
    ↓
Renew, release, or lose worker
    ↓
Employer reputation changes
```

---

# 4. Product pillars

## 4.1 Faction-driven, not anonymous

Every meaningful transaction should have a counterparty.

Good:

> New Warsaw is willing to buy 1,200 corn.

Less desirable:

> Global corn demand +8%.

The identity of the counterparty creates:

- geography;
- history;
- diplomacy;
- specialization;
- recurring relationships;
- storytelling.

A global abstraction may exist later for convenience, but should not erase the settlement/faction behind the transaction.

---

## 4.2 Production should have customers before production

Intercolony should enable:

> Demand → commitment → production.

This is what causes deliberate business decisions.

Example:

> A settlement offers a one-year furniture contract.

The player then decides to:

- expand forestry;
- buy wood;
- hire builders;
- build another workshop;
- improve dining and housing to support more workers;
- invest in better equipment;
- reserve a caravan animal for deliveries.

The economic system becomes a reason to engage with the rest of RimWorld.

---

## 4.3 Silver should buy productive capacity

Silver should be useful not merely as a way to acquire consumables.

The player can spend on:

- raw materials;
- intermediate goods;
- finished goods;
- weapons;
- apparel;
- furniture;
- art;
- production equipment;
- labor;
- logistics;
- contract obligations.

This creates actual capital-allocation questions.

Example:

> Spend 4,000 silver hiring a skilled crafter, or use it to buy a better production setup and inputs?

---

## 4.4 Relationships should persist

Repeated successful business with the same faction or settlement should matter.

Two persistent reputation systems are planned:

### Commercial Reputation

Represents reliability as:

- seller;
- buyer;
- supplier;
- contract partner.

### Employer Reputation

Represents reliability and desirability as an employer.

These should unlock opportunities rather than merely display numbers.

---

## 4.5 Scarcity must survive

Intercolony is **not** intended to become a cheat terminal.

Requesting 30 Advanced Components should not guarantee:

> 30 Advanced Components, instantly available, at a predictable small markup.

Possible results:

- no quotation;
- one supplier with only 6;
- two suppliers with partial quantities;
- one distant supplier;
- a highly expensive supplier;
- long lead time;
- supplier requires better relations;
- settlement lacks the technology.

Scarcity creates meaningful procurement.

---

## 4.6 Logistics remain physical

The economy should usually involve actual time and movement.

The player should care about:

- distance;
- caravan travel;
- carrying capacity;
- pickup;
- storage;
- perishability;
- deadlines;
- risk;
- weather;
- colonists being absent during travel.

Intercolony should create more reasons to use RimWorld's logistics rather than bypass them.

---

## 4.7 Believable abstraction over unnecessary simulation

The mod does not initially need to simulate:

- every citizen in an NPC settlement;
- every factory;
- every warehouse;
- exact NPC inventories updated every tick;
- a global macroeconomic equilibrium.

A settlement can instead possess an abstract economic profile that creates believable opportunities.

Simulate deeper only when it creates a player-visible decision worth the complexity.

---

## 4.8 Compatibility-conscious by default

The architecture should avoid large hard-coded lists of vanilla-only content.

Where practical, infer behavior from:

- `ThingDef` characteristics;
- categories;
- stats;
- quality support;
- stuff/material support;
- minifiability/transportability;
- tradeability;
- mod extensions.

Unknown modded items should work automatically when their semantics are compatible.

---

# 5. Explicit non-goals

These are not "never" features. They are things the project should not require in order to succeed.

## 5.1 No stock exchange requirement

The finished core mod does not need:

- stocks;
- bonds;
- futures;
- options;
- public companies;
- macroeconomic charts.

---

## 5.2 No infinite global catalog

The player should not be guaranteed access to every item.

---

## 5.3 No complete macroeconomic simulation

NPC settlements do not need exact persistent inventories for every item unless later playtesting demonstrates clear value.

---

## 5.4 No replacement of vanilla trading

Vanilla traders, orbital traders, caravan trade, quests, and settlement visits should remain useful.

Intercolony adds structured economic options.

---

## 5.5 No trivial pawn purchasing

Employees are not intended to be cheap permanent colonists.

---

## 5.6 No obligation to build formal accounting software

The mod may display revenue, payroll, and estimated margins, but does not need double-entry accounting.

---

# 6. Emergent business archetypes

Intercolony should naturally support colonies that specialize as:

- food exporters;
- ranchers;
- textile producers;
- apparel manufacturers;
- furniture makers;
- arms manufacturers;
- art studios;
- miners;
- medicine producers;
- component suppliers;
- importers;
- resellers;
- luxury producers;
- seasonal businesses;
- vertically integrated manufacturers.

These should emerge from normal RimWorld systems plus market demand.

Do not create rigid "business class" selection screens unless later playtesting suggests value.

---

# 7. Stable domain terminology

The code and UI should converge on consistent vocabulary.

## 7.1 Counterparty

The faction or settlement on the other side of a transaction.

---

## 7.2 Market Opportunity

A temporary, non-binding indication that a counterparty wants to buy something.

Example:

> New Warsaw wants 800–1,500 units of raw food.

---

## 7.3 Sales Order

A binding agreement where the player is the seller.

Example:

> Deliver 1,200 Corn to New Warsaw by 9 Aprimay for 1,740 silver.

---

## 7.4 Purchase Request / RFQ

A player-created request asking known counterparties to quote supply.

Example:

> Need 40 Components within 15 days.

RFQ = Request for Quotation.

---

## 7.5 Quotation

A supplier response.

Example:

> New Warsaw can provide 25 Components for 950 silver, ready in 3 days.

---

## 7.6 Purchase Order

A confirmed transaction where the player accepts a quotation and becomes the buyer.

---

## 7.7 Contract

A longer-term or recurring agreement.

Example:

> Deliver 800–1,200 units of prepared food every quadrum for one year.

---

## 7.8 Employee

A pawn working for the player under a labor agreement while conceptually remaining connected to an outside faction.

---

## 7.9 Employment Contract

The agreement defining:

- employee;
- source faction;
- start;
- duration;
- wage;
- payment schedule;
- combat expectations;
- termination rules;
- injury/death compensation where used.

---

## 7.10 Commercial Reputation

The player's track record as a commercial counterparty.

---

## 7.11 Employer Reputation

The player's track record as an employer.

---

## 7.12 Trade Lot

A quantity or individual object referenced by a transaction line.

May represent:

- fungible goods;
- quality-constrained goods;
- unique items.

---

# 8. Economic actor model

The primary economic actor should be a **settlement**, with faction-level defaults.

This gives geography meaning.

Two settlements in the same faction can differ in:

- demand;
- supply;
- worker availability;
- local specialization;
- distance;
- wealth;
- current opportunities.

Faction-level data can provide:

- baseline tech level;
- diplomacy;
- broad cultural preferences;
- global modifiers.

Settlement-level data can provide:

- current demand;
- current supply;
- local specialization;
- labor pool;
- relationship history;
- market refresh state.

---

# 9. Settlement economic profiles

A settlement does not need a simulated company ledger.

A lightweight profile is sufficient.

Conceptual model:

```text
SettlementEconomicProfile
- settlementId
- factionId
- techTier
- wealthTier
- economicArchetype
- demandWeights
- supplyWeights
- qualityPreference
- laborSupplyModifier
- volatility
- lastMarketRefresh
- marketSeed / generation metadata
```

Possible archetypes:

- agricultural;
- industrial;
- military;
- affluent;
- frontier;
- tribal;
- trade hub;
- mixed.

Archetypes should influence probabilities, not become hard restrictions.

Example:

An industrial settlement is *more likely* to supply components, but not mechanically forbidden from demanding them.

---

# 10. Product categories

The architecture must not assume "trade = stackable resources."

## 10.1 Commodities

Examples:

- rice;
- corn;
- potatoes;
- steel;
- wood;
- stone blocks;
- cloth;
- chemfuel.

Mostly fungible.

---

## 10.2 Intermediate goods

Examples:

- components;
- advanced components;
- textiles;
- leather;
- processed materials.

Usually quantity-driven.

---

## 10.3 Manufactured goods

Examples:

- clothing;
- armor;
- weapons.

Relevant properties may include:

- quality;
- stuff/material;
- hit points;
- biocoding or equivalent restrictions;
- custom comps.

---

## 10.4 Furniture

Examples:

- dining chairs;
- stools;
- tables;
- beds;
- shelves;
- other physically transferable furniture.

Relevant properties may include:

- quality;
- material;
- hit points;
- beauty;
- comfort;
- actual stats derived from the object.

---

## 10.5 Capital equipment

This is a core design feature.

Examples:

- stoves;
- tailoring benches;
- machining tables;
- fabrication benches;
- other sensible production equipment.

Buying capital equipment should let the player increase productive capability faster than fabricating everything internally.

Important principle:

> **The market can sell productivity, not only consumables.**

Not every building should qualify.

Likely inappropriate examples:

- walls;
- terrain;
- map-bound infrastructure;
- geothermal generators;
- structures that cannot sensibly be transported.

Tradability should use generic capabilities where possible rather than a giant hard-coded list.

---

## 10.6 Art and unique goods

Examples:

- sculptures;
- exceptional weapons;
- rare furniture;
- named high-quality objects.

These should be treated individually when identity matters.

Possible value factors:

- quality;
- material;
- hit points;
- author;
- art description;
- beauty;
- rarity;
- buyer preference.

---

# 11. Demand generation

Demand opportunities are the main sales-side engine.

A lightweight formula is preferable.

Conceptual:

```text
base demand
× settlement category preference
× settlement wealth
× technology compatibility
× relationship modifier
× temporary market state
× randomness
= opportunity quantity / price tendency
```

A generated opportunity should contain enough information to become a Sales Order.

Example:

```text
Buyer: New Warsaw
Requested item: Corn
Quantity: 1,200
Unit price: 1.16 × reference value
Deadline: 12 days
Fulfillment: Seller delivery
Minimum quality: none
```

Quality order:

```text
Buyer: Imperial Settlement
Item: Dining Chair
Quantity: 20
Minimum quality: Excellent
Material: Any
Deadline: 18 days
Payment: 5,800 silver
```

Art order:

```text
Buyer: Imperial Settlement
Item: Large Sculpture
Quantity: 3
Minimum quality: Excellent
Preferred material: Marble
Preference bonus: +15%
```

---

# 12. "Find Buyer" workflow

A major quality-of-life feature:

> "I already have a huge surplus. Who wants it?"

The player chooses an item or category.

Example:

> Colony has 3,842 Rice.

Possible results:

```text
Yttakin Settlement
Demand: 400–900
Offer: 1.05/unit

New Warsaw
Demand: up to 2,000
Offer: 1.22/unit

Imperial Settlement
No current interest
```

This connects spontaneous RimWorld surplus to structured commerce.

---

# 13. Demand saturation

One settlement should not necessarily buy infinite volume at the best price.

Illustrative marginal demand:

```text
first 500 units       1.22×
next 500 units        1.16×
next 500 units        1.08×
remaining demand      0.96×
```

Possible implementation styles:

- tiered;
- continuous curve;
- hidden;
- partially explained in UI.

Purpose:

> Prevent one nearby settlement from becoming an infinite premium sink.

---

# 14. Sales Order lifecycle

Suggested high-level state machine:

```text
Available
   ↓ accept
Accepted
   ↓
Preparing
   ↓
Ready
   ↓
InTransit
   ↓
Delivered
   ↓
Completed
```

Failure branches:

```text
Cancelled
Expired
Failed
Disputed
```

The initial implementation does not need every state.

The important requirement is:

> State transitions are explicit and authoritative.

UI should not arbitrarily mutate status fields.

---

# 15. Sales Order data model

Conceptual model:

```text
SalesOrder
- id
- counterpartyFactionId
- counterpartySettlementId
- createdAt
- acceptedAt
- deadline
- status
- payment
- fulfillmentMode
- destination
- lineItems[]
- reputationImpactConfig
- penaltyConfig
- logisticsRecordId?
```

Line item:

```text
OrderLine
- itemSelector
- quantity
- minimumQuality?
- exactQuality?
- allowedStuff?
- preferredStuff?
- minimumHitPointPercent?
- uniqueItemRequirement?
```

This is conceptual, not mandated class design.

---

# 16. Order reservation

When an order is accepted, the player may want to reserve stock.

Potential approaches:

### Hard reservation

Reserved items cannot be consumed elsewhere.

Pros:
- reliable.

Cons:
- can feel restrictive.

### Soft reservation

UI tracks committed stock and warns if the colony uses it.

Pros:
- flexible.

Cons:
- harder to guarantee.

### Dedicated staging zone

The player stages goods physically.

Pros:
- very RimWorld.

Cons:
- additional management.

### Assignment at shipment time

No reservation until caravan loading.

Pros:
- simplest.

Cons:
- less planning support.

Recommendation:

Start simple. Do not build a complex inventory reservation framework before the first vertical slice.

---

# 17. Deadlines

Deadlines should create pressure without becoming opaque.

The UI should show:

- time remaining;
- required quantity;
- prepared quantity;
- approximate travel time;
- destination;
- warning if delivery appears impossible.

Do not silently fail an order because the player did not realize travel time counted.

---

# 18. Completion validation

Validation must inspect actual delivered goods.

For fungible items:

- matching `ThingDef` or category;
- correct quantity;
- required stuff/material;
- hit-point threshold if used.

For quality goods:

- minimum quality;
- exact quality if required;
- material constraints.

For unique goods:

- object identity or snapshot constraints where appropriate.

Return structured validation failures for UI.

Example:

```text
Order incomplete:
- 18 / 20 chairs delivered
- 2 chairs below Excellent quality
```

---

# 19. Purchase Requests / RFQs

The purchase side is deliberately not a store catalog.

The player states a need.

Example:

```text
Item: Component
Quantity: 40
Desired deadline: 15 days
```

Known counterparties may answer.

Example:

```text
New Warsaw
40 units
1,460 silver
Pickup ready in 4 days

Trade Hub
40 units
1,610 silver
Delivery in 1 day

Toju
28 units
1,008 silver
Pickup ready in 6 days
```

The player compares and chooses.

---

# 20. RFQ scarcity model

Supplier response probability and terms may depend on:

- item category;
- settlement technology;
- settlement profile;
- requested quantity;
- item rarity;
- commercial relationship;
- distance;
- current market state;
- random variation.

An RFQ may produce:

- no quotation;
- one quotation;
- partial quotations;
- multiple complete quotations;
- different lead times;
- different quality;
- different logistics modes.

This is the core anti-vending-machine design.

---

# 21. Purchase Order lifecycle

Suggested states:

```text
Quoted
   ↓ accept
Confirmed
   ↓
AwaitingProduction
   ↓
ReadyForPickup
   ↓
InTransit
   ↓
Delivered
   ↓
Completed
```

Failure branches:

```text
Cancelled
SupplierDefault
PlayerDefault
Expired
```

The first implementation may use fewer states.

---

# 22. Buying finished products

Buying finished products is core scope.

Examples:

```text
Industrial Stove
Quality: Excellent
Condition: 96%
Seller: New Warsaw
Price: 1,340
```

```text
Dining Chair
Quality: Masterwork
Material: [actual StuffDef]
Seller: Imperial Settlement
Price: 620
```

```text
Grand Sculpture
Quality: Legendary
Material: Marble
Artist: Jo "Pigeon" Almeida
Beauty: derived from actual object
Price: 8,900
```

This means Intercolony includes a **capital goods market**, not only a commodity market.

---

# 23. Representation of trade goods

The architecture should distinguish:

## 23.1 Generic/fungible lot descriptors

Useful when identity does not matter.

Conceptual:

```text
ThingDef
StuffDef?
Quantity
QualityConstraint?
MinHitPointsPercent?
Other selector rules
```

---

## 23.2 Unique item snapshots

Needed when the specific object matters.

Conceptual:

```text
TradeItemSnapshot
- stableTradeItemId
- ThingDef
- StuffDef?
- quality?
- hitPoints
- maxHitPoints
- author metadata?
- art metadata?
- relevant comps / custom data
- referenceValueSnapshot
```

Technical caution:

Do not casually serialize arbitrary live `Thing` object graphs into world-market data.

Prefer a deliberate transfer representation.

If an object physically exists in the player's colony, retaining the live object until shipment may be best.

If a foreign seller offers an object that does not yet physically exist on a map, a stable descriptor/snapshot may be more appropriate until delivery.

This should be treated as a focused technical spike before generalizing.

---

# 24. Selling furniture, art, and equipment

Examples:

> 20 Dining Chairs, minimum Excellent.

> 3 Large Sculptures, minimum Excellent, Marble preferred.

> 1 Masterwork Stove.

> 10 Excellent Beds.

A high-skill builder should therefore be economically valuable.

A colony can intentionally become:

- a premium furniture factory;
- an art studio;
- an equipment manufacturer.

This is an important design outcome.

---

# 25. Logistics models

Transactions should not default to magical teleportation.

## 25.1 Seller delivery

The player delivers goods.

Potential benefits:

- higher sale price;
- faster completion;
- control over timing.

Costs:

- caravan labor;
- travel;
- danger;
- carrying capacity.

---

## 25.2 Buyer pickup

The buyer sends a caravan after goods are ready.

Potential pricing:

> Lower payment because the buyer handles logistics.

Example event:

```text
ORDER READY

Union of Eridani will arrive in approximately 1.8 days
to collect Order #1438.
```

---

## 25.3 Player pickup

For purchases, the player collects from the supplier.

Potential benefit:

- cheaper.

---

## 25.4 Supplier delivery

Supplier brings purchased goods.

Potential cost:

- higher price;
- delivery fee;
- longer wait;
- relationship requirement.

---

# 26. Logistics abstraction boundary

The commerce system should care about:

- origin;
- destination;
- cargo;
- fulfillment mode;
- state;
- ETA/arrival event;
- completion.

It should avoid coupling every order directly to one specific caravan implementation.

This leaves room for:

- vanilla caravans;
- transport pods;
- vehicle mods;
- future compatibility layers.

---

# 27. Commercial Reputation

Commercial reputation is separate from ordinary goodwill.

Illustrative UI:

```text
Union of Eridani

Faction Goodwill: +32
Commercial Reputation: 74 / 100
Tier: Reliable Supplier

Orders completed: 17
Orders late: 1
Orders cancelled: 0
Purchases completed: 8
Defaults: 0
```

Possible positive inputs:

- order completed;
- on-time delivery;
- repeated business;
- large contract completion;
- prompt payment.

Possible negative inputs:

- missed delivery;
- cancellation after acceptance;
- payment default;
- severe lateness;
- contract breach.

---

# 28. Commercial reputation effects

Potential effects:

- larger orders;
- slightly better prices;
- more frequent opportunities;
- access to recurring contracts;
- access to scarce goods;
- better deadlines;
- lower deposits if deposits exist later;
- preferred-supplier status.

Avoid runaway positive feedback that turns high reputation into guaranteed infinite profit.

---

# 29. Recurring contracts

Recurring contracts are one of the main progression systems.

Example:

```text
SUPPLY AGREEMENT

Buyer: Union of Eridani
Category: Raw Food
Requirement: 800–1,200 units per quadrum
Duration: 1 year
Price: reference value × 1.12
Fulfillment: Seller delivery
```

The design objective is strategic:

> A future demand commitment causes the player to expand capacity.

---

# 30. Contract mechanics

Potential contract fields:

- buyer/seller;
- category or exact product;
- minimum quantity;
- maximum quantity;
- quality requirement;
- material preference;
- cadence;
- duration;
- fulfillment;
- price rule;
- grace period;
- breach conditions;
- renewal rules.

The first recurring-contract version should be simple.

Start with:

> fixed quantity, fixed cadence, fixed duration, fixed price formula.

---

# 31. Labor market — product objective

The labor system lets the player convert silver and relationships into temporary productive capacity.

Core use cases:

> "I need four workers for harvest."

> "I need a builder for one quadrum."

> "This food contract is profitable only if I hire two cooks."

> "I do not want another permanent colonist, but I need hauling labor for 20 days."

---

# 32. Employees are not ordinary colonists

This distinction is fundamental.

An employee should conceptually:

- originate from an outside faction;
- remain socially/economically connected to that faction;
- work under a contract;
- receive wages;
- leave when the contract ends;
- be able to refuse renewal;
- generate consequences if abused;
- not become a permanent colonist automatically.

The exact internal faction/control model is a technical decision, not a product requirement.

---

# 33. Labor technical spike — mandatory

Employee pawn control is likely one of the highest-risk engineering areas.

Before building full payroll, recruiting UI, or employer reputation, implement one isolated employee prototype.

The prototype must answer:

1. Can a foreign employee be selected?
2. Can work priorities be assigned?
3. Can the employee use player workbenches?
4. Can the employee use player beds?
5. Can food policies be assigned?
6. Can areas be assigned?
7. Can the pawn be drafted?
8. Can combat participation be tracked?
9. Can the pawn join a caravan?
10. Can the pawn return to the colony?
11. Can the employee leave cleanly?
12. What happens if incapacitated?
13. What happens if captured?
14. What happens if killed?
15. What happens on save/load?
16. What happens if the source faction becomes hostile?
17. What happens with ideology and other pawn-state systems?
18. What assumptions do common pawn-control mods make?
19. Can original faction identity be restored without residue?
20. What happens to social relations created during employment?

Do not build the full labor economy until the control model is proven.

---

# 34. Possible employee control strategies

These are hypotheses to test.

## Strategy A — temporary player-faction transfer

Temporarily transfer the pawn into the player's faction while storing employment metadata and original affiliation.

Potential benefits:

- work system may function naturally;
- drafting may be easier;
- caravan membership may be easier.

Potential risks:

- diplomacy;
- ideology;
- social relations;
- quest logic;
- faction assumptions;
- death notifications;
- prisoner/slave/guest semantics;
- restoration bugs;
- compatibility.

---

## Strategy B — retain original faction, add employee controllability

Keep external faction ownership and patch/extend the needed systems.

Potential benefits:

- conceptually clean affiliation.

Potential risks:

- invasive patches;
- work giver logic;
- drafting;
- selection;
- zones;
- bed ownership;
- policies;
- caravans;
- many UI assumptions.

Choose based on experiments, not aesthetics.

---

# 35. Labor sourcing models

Two complementary workflows are desirable.

## 35.1 Available worker pool

Example:

```text
UNION OF ERIDANI — AVAILABLE WORKERS

Mira
Plants 13
Cooking 7
Minimum contract: 5 days
Daily wage: 35 silver

Tomas
Construction 11
Mining 8
Minimum contract: 1 quadrum
Quadrum wage: 490 silver

Julia
Intellectual 15
Social 9
Minimum contract: 1 year
Annual equivalent: 3,800 silver
```

---

## 35.2 Job posting / applicant model

Player posts:

```text
HIRING

Requirement: Construction >= 10
Positions: 3
Duration: 1 year
Wage offered: 500 / quadrum
```

After time passes:

> 4 applicants.

Applicant quantity and quality may depend on:

- offered wage;
- employer reputation;
- distance;
- source faction relationship;
- housing;
- safety;
- labor availability.

This turns hiring into an actual market.

---

# 36. Employment contract types

## 36.1 Daily / short-term

Good for:

- harvest;
- emergency hauling;
- a construction project;
- production surge.

Flexible, relatively expensive per day.

---

## 36.2 Fixed-term quadrum contract

Example:

> 2 quadrums at 480 silver per quadrum.

---

## 36.3 Long fixed-term contract

Example:

> one year.

May be prepaid or periodic.

---

## 36.4 Open-ended employment

Worker stays until either side terminates under rules.

This creates recurring payroll.

Add only after fixed-term employment is robust.

---

# 37. Wage structures

Potential structures:

## Prepaid

> One year: 4,000 silver now.

Potential benefits:

- discounted total cost;
- simple administration.

Risk:

- employee may die;
- business conditions may change.

---

## Quadrum payroll

> 500 silver per quadrum.

Likely default for longer employment.

---

## Daily wage

> 42 silver per day.

Best for short-term flexibility.

---

# 38. Payroll

Payroll should be a real obligation.

Conceptual screen:

```text
PAYROLL DUE

Tomas       490
Mira        525
Julia       650
----------------
Total     1,665

Available silver: 1,205
Shortfall: 460
```

Lack of money should not simply block time.

It should create arrears.

---

# 39. Wage arrears

Possible escalation:

1. payment missed;
2. warning;
3. employee mood/satisfaction penalty;
4. employee refuses some work;
5. employee terminates;
6. outstanding debt remains;
7. employer reputation falls;
8. source faction goodwill may fall;
9. future workers become more expensive or unavailable.

The first missed payroll should not instantly destroy the colony.

Failure should be playable.

---

# 40. Employer Reputation

Illustrative:

```text
Employer Reputation: 46 / 100
Tier: Decent Employer

Contracts completed: 12
Late payroll incidents: 1
Employee deaths: 2
Unpaid compensation: 0
```

Positive signals:

- wages paid on time;
- safe contract completion;
- adequate living conditions;
- medical treatment;
- voluntary renewal.

Negative signals:

- missed payroll;
- severe untreated injury;
- starvation;
- imprisonment;
- abuse;
- contract violations;
- preventable death.

Avoid expensive continuous calculations when event-driven updates are sufficient.

---

# 41. Employee living conditions

Employees should interact with normal RimWorld needs.

Possible future bed role:

> Employee

But a custom bed type is not mandatory if ordinary ownership/assignment works.

Living conditions may influence:

- mood;
- willingness to renew;
- employer reputation;
- applicant quality.

This should reward humane conditions without becoming hotel-management micromanagement.

---

# 42. Combat clauses

Without constraints, hired workers become cheap meat shields.

Possible contract types:

## Civilian

Not expected to participate in offensive combat.

Self-defense is acceptable.

Aggressive use may create penalties.

---

## Armed Employee

May participate in colony defense.

Higher wage.

---

## Security Contractor

Explicit combat worker.

Much higher wage.

May eventually be a separate labor category.

---

# 43. Injury and death compensation

Employee death should create consequences.

Example:

```text
EMPLOYEE DEATH

Tomas Vega died while employed by your colony.

Contractual death compensation:
2,400 silver
```

Possible effects:

- compensation owed;
- employer reputation loss;
- faction goodwill loss;
- debt if unpaid;
- reduced future applicant pool.

Balance carefully so ordinary RimWorld danger does not make employees unusable.

---

# 44. Employee-to-colonist transition

Late-game / post-MVP mechanic.

After long positive employment:

```text
Tomas Vega has grown attached to your colony.

He would like to remain permanently.
```

Possible outcomes:

- pay release fee;
- negotiate using Social;
- source faction agrees;
- pawn defects, causing diplomacy consequences;
- decline.

Must not become a trivial recruitment exploit.

---

# 45. Labor + commerce integration

The systems should reinforce each other.

Example:

```text
Empire contract revenue      7,400 / quadrum

Ingredients                 -1,300
Employee payroll            -2,100
Transport                     -400
----------------------------------
Approx. operating margin     3,600
```

The player then asks:

- Is the contract profitable?
- Should I hire or train?
- Should I buy inputs or produce them?
- Should I buy another stove?
- Should I deliver myself?
- Should I expand employee housing?

This is the heart of the finished product.

---

# 46. Pricing philosophy

Use RimWorld's existing value concepts where possible, then apply economic context.

Conceptual formula:

```text
reference value
× quality/material effects already represented in value
× settlement demand/supply preference
× scarcity
× reputation
× urgency
× lot size / saturation
× logistics terms
× random variation
= transaction price
```

Centralize price logic.

Do not scatter pricing formulas across UI and transaction state code.

---

# 47. Price transparency

Players should understand why an offer is attractive.

Potential tooltip:

```text
Base value                 1,000
High local demand          +18%
Reliable supplier bonus     +4%
Seller delivery            +10%
Large-volume discount       -6%
--------------------------------
Offer                      1,260
```

Not every internal factor must be shown, but prices should not feel arbitrary.

---

# 48. Distance

Distance can affect:

- lead time;
- delivery cost;
- pickup value;
- employee availability;
- likelihood of short-deadline offers.

Distance should create **regional economics**.

Avoid making far settlements useless.

---

# 49. Diplomacy

Faction goodwill and hostility should influence access.

Minimum expectations:

- hostile factions normally do not trade;
- hostile factions normally do not provide workers;
- friendly/allied factions may offer better access;
- commercial defaults may affect goodwill;
- commercial reputation remains separate.

---

# 50. Technology level

Technology influences:

- what can be supplied;
- what is demanded;
- available equipment;
- worker skill distribution;
- quality expectations.

Example:

A tribal settlement should not routinely supply fabrication benches.

Exceptions can exist when interesting.

---

# 51. Market access and contact

A settlement should not necessarily appear simply because it exists.

Possible access requirements:

- settlement discovered;
- faction known;
- communications console;
- caravan contact;
- prior trade;
- relationship threshold.

The MVP should use the simplest intuitive rule.

Do not overcomplicate communications before commerce works.

---

# 52. Top-level UI

Likely main tab:

> **Intercolony**

Possible sections:

1. Market
2. Orders
3. Procurement
4. Contracts
5. Labor
6. Relationships

Do not implement all tabs at once.

A primitive developer window is acceptable early.

---

# 53. Market tab

Purpose:

> See what known settlements currently want.

Possible columns:

| Buyer | Item / Category | Qty | Price | Deadline | Fulfillment | Distance |
|---|---|---:|---:|---:|---|---:|

Potential filters:

- faction;
- distance;
- category;
- item;
- quality;
- minimum value;
- fulfillment mode.

Later:

- sort by estimated profit;
- "Find Buyer";
- counterparty drill-down.

---

# 54. Orders tab

Suggested groups:

- Active Sales
- Active Purchases
- Ready
- In Transit
- Completed
- Failed

Order detail should show:

- counterparty;
- requirements;
- prepared quantity;
- deadline;
- destination;
- payment;
- fulfillment;
- penalties;
- next action.

---

# 55. Procurement tab

Flow:

1. choose item;
2. choose quantity;
3. optional quality/material constraints;
4. choose desired deadline;
5. submit RFQ;
6. wait;
7. compare quotations;
8. accept one or several.

Later, allow split sourcing.

---

# 56. Labor tab

Possible sections:

- Available Workers
- Job Posts
- Applicants
- Current Employees
- Payroll
- Employment Contracts
- Employer Reputation

Employee detail:

```text
Tomas Vega
Source: Union of Eridani

Contract
- Type: Fixed term
- Remaining: 23 days
- Wage: 490 / quadrum
- Combat clause: Civilian

Skills
- Construction 11
- Mining 8

Status
- Payroll: Current
- Satisfaction: Neutral
```

---

# 57. Relationship view

Example:

```text
Union of Eridani

Goodwill                  +32
Commercial Reputation      74 / 100
Employer Reputation        46 / 100

Completed sales orders     17
Completed purchases         8
Failed contracts            1
Employees hired             6

Current opportunities       4
Current contracts            2
Available workers            7
```

This page makes the world feel relational.

---

# 58. Notifications

Use RimWorld-style messages/letters for important events only.

Good candidates:

- major contract offer;
- order near deadline;
- pickup caravan arrived;
- supplier default;
- payroll failure;
- employee death compensation;
- contract renewal;
- meaningful reputation tier change.

Do not spam letters every market refresh.

---

# 59. Market refresh cadence

Never regenerate the entire economy every tick.

Use coarse updates.

Possible designs:

- periodic refresh;
- staggered settlement refresh;
- on-demand refresh with cooldown;
- event-driven refresh.

Performance should scale to heavily modded worlds with many settlements.

---

# 60. Deterministic randomness

Market generation should avoid unintended interference with RimWorld's global random state.

Consider:

- local seeded RNG;
- stable settlement IDs;
- refresh number/date;
- persistent seeds.

Goals:

- no weird global RNG side effects;
- sensible save/load behavior;
- reproducible debugging where useful.

Exact technique depends on target APIs.

---

# 61. Persistence requirements

Binding obligations must survive save/load.

Likely persistent data:

- economic profiles;
- active market opportunities;
- sales orders;
- RFQs;
- quotations;
- purchase orders;
- recurring contracts;
- commercial reputation;
- employer reputation;
- employment contracts;
- wage arrears;
- unique item snapshots;
- transaction history;
- schema version.

---

# 62. Save schema versioning

Version the Intercolony save state early.

Conceptual:

```text
IntercolonySaveVersion = 1
```

When data structures change:

- migrate deliberately;
- provide safe defaults;
- log migration;
- avoid silently dropping active obligations.

Pre-alpha may tolerate breaking saves, but once public testing begins, migration quality matters.

---

# 63. Mod compatibility principles

Prefer definition-driven behavior.

Avoid:

- hard-coded vanilla-only item lists;
- broad Harmony patches when a narrow extension point works;
- assuming every item uses vanilla comps;
- mutating unrelated global defs;
- retaining unsafe references to map objects in world state.

Prefer:

- categories;
- generic stat/value APIs;
- `ThingDef` inspection;
- quality interfaces where available;
- `DefModExtension` metadata;
- optional compatibility adapters.

---

# 64. Unknown modded items

Default policy:

> If an item behaves like a normal tradable physical Thing and can be meaningfully transferred, Intercolony should attempt to support it.

Potential exclusions:

- quest-only items;
- map-bound infrastructure;
- non-transferable entities;
- unsafe custom comps;
- explicitly blacklisted defs.

Provide debug/settings tooling to blacklist problematic items.

---

# 65. DLC compatibility

Do not assume all DLC is installed.

Feature gates should check content availability.

Labor is especially sensitive to:

- ideology;
- slavery;
- royalty;
- biotech pawn states;
- anomaly systems;
- other pawn-affiliation systems.

The exact DLC compatibility matrix must be built against the chosen target version.

---

# 66. Settings

Potential settings:

- market refresh frequency;
- opportunity frequency;
- price volatility;
- contract frequency;
- commercial reputation strength;
- wage scale;
- death compensation multiplier;
- maximum market distance;
- enable capital-equipment trade;
- enable art/unique-item market;
- employee combat behavior;
- debug logging.

Do not expose dozens of settings before good defaults exist.

---

# 67. Debug tools

Strong dev tooling should appear early.

Suggested actions:

- generate market;
- clear market;
- create test opportunity;
- accept test order;
- force order deadline;
- complete/fail selected order;
- generate RFQ;
- force quotation;
- spawn purchase delivery;
- set commercial reputation;
- generate worker;
- force payroll;
- terminate employee;
- print serialized state;
- validate IDs/references;
- dump settlement profile.

Good debug tooling is a development multiplier.

---

# 68. Logging

Prefix logs consistently:

```text
[Intercolony]
```

Conceptual levels:

- error;
- warning;
- informational;
- verbose/debug behind setting.

No per-tick log spam.

---

# 69. Suggested repository structure

Conceptual only:

```text
Intercolony/
├── About/
│   └── About.xml
├── Defs/
├── Languages/
├── Textures/
├── Source/
│   └── Intercolony/
│       ├── Intercolony.csproj
│       ├── Core/
│       ├── Market/
│       ├── Orders/
│       ├── Procurement/
│       ├── Contracts/
│       ├── Labor/
│       ├── Reputation/
│       ├── Logistics/
│       ├── Pricing/
│       ├── Persistence/
│       ├── UI/
│       ├── Compatibility/
│       ├── Debug/
│       └── Utilities/
├── README.md
└── DESIGN.md
```

Do not create empty abstraction layers merely to match this tree.

---

# 70. Suggested domain service boundaries

Possible services:

### MarketService

- refresh opportunities;
- query settlement demand;
- find buyers.

### PricingService

- calculate transaction prices;
- produce price breakdowns.

### SalesOrderService

- accept;
- validate;
- transition;
- complete;
- fail.

### ProcurementService

- submit RFQ;
- generate quotations;
- accept quotation.

### LogisticsService

- shipment/pickup state;
- arrival hooks.

### ContractService

- recurring obligations;
- renewals;
- breach.

### LaborService

- candidate generation;
- hiring;
- arrival/departure.

### PayrollService

- payment;
- arrears;
- escalation.

### ReputationService

- commercial and employer reputation.

These are conceptual boundaries.

Avoid interface-heavy enterprise architecture unless code size justifies it.

---

# 71. World state owner

Intercolony needs a persistent game/world-level owner for economic state.

Requirements:

- one authoritative instance;
- low-frequency updates;
- save/load;
- settlement/faction access;
- not tied to one map;
- safe when multiple player maps exist.

The exact Verse/RimWorld base type must be chosen after inspecting the target version.

Do not store authoritative market state in a UI singleton.

---

# 72. IDs

Persistent entities require stable IDs:

- opportunity ID;
- sales order ID;
- RFQ ID;
- quotation ID;
- purchase order ID;
- recurring contract ID;
- employment contract ID;
- trade item ID.

IDs must survive save/load.

Human-readable UI IDs can be short aliases.

---

# 73. State-machine discipline

Every important entity should have authoritative transitions.

Example:

```text
Available -> Accepted -> Ready -> InTransit -> Completed
                     \-> Failed
```

Benefits:

- fewer impossible states;
- simpler save/load;
- easier debugging;
- safer UI;
- easier testing.

---

# 74. Validation architecture

Centralize matching logic.

Example questions:

- Does this Thing satisfy this order line?
- Does this shipment contain enough matching items?
- Does quality meet requirement?
- Is stuff/material permitted?
- Is condition sufficient?
- Is the exact unique item required?

Return structured results, not only booleans.

Example:

```text
ValidationResult
- success
- matchedQuantity
- missingQuantity
- failures[]
```

---

# 75. Transaction history

A lightweight transaction log is useful.

Possible entries:

- sale payment;
- purchase payment;
- wage payment;
- compensation;
- penalty;
- refund.

Later UI:

```text
Last 30 days

Sales revenue        8,220
Purchases           -3,140
Payroll             -2,100
Compensation          -600
```

This is not required for MVP.

---

# 76. Exploit resistance

Anticipate obvious player optimization.

## 76.1 Cancel/reaccept rerolls

Avoid unlimited price rerolling.

Possible tools:

- cooldown;
- opportunity removal;
- accepted-order cancellation penalty;
- persistent market state.

---

## 76.2 Fake quality manipulation

Validate actual goods at delivery.

---

## 76.3 Employee meat shields

Combat clauses + wage differences + compensation + reputation.

---

## 76.4 Employee recruitment exploit

Permanent conversion requires cost and conditions.

---

## 76.5 Infinite demand

Use demand caps, saturation, and refresh.

---

## 76.6 Guaranteed arbitrage

Some arbitrage can be fun.

Avoid deterministic zero-risk loops where the same market buys and sells the same item at guaranteed profit after trivial clicks.

Distance, time, spread, and demand should matter.

---

# 77. Failure should be playable

A failed contract should not always feel like a reload prompt.

Possible consequences:

- reduced payment;
- reputation loss;
- penalty;
- grace period;
- renegotiation;
- partial acceptance;
- temporary opportunity loss.

Later, Social skill could influence renegotiation.

---

# 78. Balance philosophy

Do not balance only by matching vanilla prices.

Structured commerce itself provides value:

- information;
- targeted demand;
- certainty;
- procurement access;
- predictable contracts;
- labor flexibility.

Therefore:

> A recurring buyer may pay less than a lucky random trader because the reliability of the demand is itself valuable.

Likewise:

> Guaranteed access to scarce equipment may justify a premium.

---

# 79. Information and uncertainty

Not all market knowledge needs to be perfectly precise.

Possible non-binding information:

```text
New Warsaw
Food demand: High
Expected refresh: ~4 days
```

Binding opportunity:

```text
Will buy exactly 1,200 Corn for 1,740 silver until 9 Aprimay.
```

This distinction allows useful uncertainty without arbitrary hidden rules.

---

# 80. Future story events

Possible later events:

- sudden medicine shortage;
- wealthy art buyer;
- temporary food premium;
- supplier shortage;
- labor shortage;
- employee family emergency;
- buyer default;
- contract renegotiation;
- war disrupting routes;
- local construction boom increasing furniture demand.

Do not build these before deterministic core systems are stable.

---

# 81. World-event demand shocks

Future optional integration:

- cold snap → food/fuel demand;
- war → weapons/medicine demand;
- disease → medicine demand;
- harvest surplus → lower food prices;
- disaster → construction-material demand.

Not required for the first complete release.

---

# 82. Technical quality bars

Every major subsystem should be evaluated against:

### Correctness

Does the state machine behave correctly?

### Persistence

Can the game save/reload in the middle of it?

### Performance

Does it avoid expensive per-tick world scans?

### Compatibility

Does it avoid unnecessary assumptions about vanilla-only content?

### Debuggability

Can a developer inspect and force states?

### Player legibility

Can the player understand what happened and why?

### Failure safety

If something goes wrong, does it fail gracefully instead of corrupting state?

---

# 83. Testing strategy

The project should use multiple layers.

## 83.1 Pure logic tests where practical

Ideal for:

- pricing;
- saturation curves;
- order state transitions;
- validation;
- reputation math;
- payroll schedules.

Avoid depending on a running map when logic can be isolated.

---

## 83.2 In-game dev tests

Required for:

- spawning Things;
- caravans;
- UI;
- pawn control;
- faction behavior;
- save/load;
- actual item metadata.

---

## 83.3 Save/load matrix

For each major persistent feature, test saving at multiple states.

Example Sales Order:

- available;
- accepted;
- partially prepared;
- ready;
- in transit;
- completed;
- failed.

---

## 83.4 Modded-content smoke tests

At minimum eventually test:

- one modded resource;
- one modded weapon/apparel item;
- one modded minifiable building;
- one modded pawn/faction case.

---

# 84. Performance guardrails

Avoid:

- scanning every map Thing every tick;
- refreshing every settlement every tick;
- expensive LINQ in hot paths without reason;
- repeated global object searches;
- huge persistent copies of live Things.

Prefer:

- coarse ticks;
- cached indices;
- event-driven updates;
- staggered refreshes;
- lazy UI calculations;
- explicit snapshots.

---

# 85. Localization

All player-facing strings should eventually be localizable.

Do not bake English text deep into domain code.

Early prototype strings may be hard-coded temporarily, but should be migrated before public release.

Primary initial language can be English.

Portuguese localization can be added later.

---

# 86. Error recovery

Persistent systems need safe fallbacks.

Examples:

- referenced settlement destroyed;
- faction removed by mod update;
- ThingDef no longer exists after mod-list change;
- employee pawn missing;
- active unique-item quote references invalid object data.

Prefer:

- cancel gracefully;
- refund or compensate where appropriate;
- log warning;
- remove impossible obligation;
- avoid game-breaking null-reference cascades.

---

# 87. Destruction or disappearance of settlements

If a counterparty settlement disappears mid-order:

Possible policy:

- order fails without player penalty;
- payment/deposit refunded;
- log/letter explains cause;
- related recurring contracts terminate.

Exact behavior can vary, but must be deterministic.

---

# 88. Faction hostility mid-contract

Possible policy:

- contracts suspended or cancelled;
- active employee contracts enter special state;
- hostile employees should not silently become enemies in the middle of a bedroom without an intentional design.

This is especially important for labor.

A dedicated edge-case policy must be defined before public labor release.

---

# 89. Multi-map colonies

Intercolony should not assume the player has only one map.

Potential requirements:

- order staging may belong to a specific map;
- employee contract may have assigned worksite;
- market state is world-level;
- goods may be delivered from any eligible player settlement if contract permits.

Do not over-build multi-map support in MVP, but avoid architecture that makes it impossible.

---

# 90. Multiplayer

Official support is not required unless deliberately chosen later.

However, state-machine discipline, deterministic IDs, and centralized transitions make future compatibility easier.

Do not claim multiplayer compatibility without testing.

---

# 91. Finished-product user journey

A mature save might look like this:

### Early game

The player has surplus rice.

They open Intercolony and use:

> Find Buyer.

A nearby settlement wants 700 rice.

The player accepts and delivers it.

The settlement now has a small commercial-history record.

### Early-mid game

The player sees repeat demand for food.

They accept several orders.

A new recurring opportunity appears.

The player needs more labor for harvest season.

They hire two workers for 10 days.

### Mid game

The player buys a high-quality stove and components through procurement.

They expand cooking capacity.

They sign a food contract.

Payroll becomes a real recurring expense.

### Mid-late game

The player diversifies into premium furniture.

An Excellent-chair order causes them to reserve their best builder for commercial production.

A rich settlement pays heavily for Masterwork pieces.

### Late game

The colony has:

- several commercial counterparties;
- recurring supply agreements;
- a stable employee base;
- seasonal workers;
- procurement relationships;
- capital equipment sourced externally;
- meaningful operating expenses;
- a reputation worth protecting.

The economy has become a story generator rather than an infinite silver faucet.

---

# 92. Roadmap philosophy

Each phase below should produce a testable artifact.

Do not start a later phase because the earlier one is "mostly there."

Advance when:

- core state is reliable;
- save/load works;
- the feature is understandable;
- no critical red errors exist;
- the current architecture can support the next layer.

---

# 93. Phase 0 — Repository and build bootstrap

## Goal

Create the smallest valid mod project that builds and loads.

## Tasks

- create standard RimWorld mod folder structure appropriate to target version;
- create `About.xml`;
- create C# project;
- reference target assemblies correctly;
- choose target framework/compiler compatible with the game;
- add Harmony only if needed;
- add root namespace;
- add one startup log line;
- document local dev paths without committing machine-specific paths;
- create repeatable build/copy workflow;
- create `.gitignore`;
- document exact target RimWorld version;
- record dependencies.

## Acceptance criteria

- mod appears in mod list;
- game launches with mod enabled;
- no Intercolony red errors;
- assembly loads;
- one startup message appears;
- clean checkout can be built using documented steps.

## Suggested commit

`chore: bootstrap Intercolony mod project`

---

# 94. Phase 1 — Persistent core state

## Goal

Prove world-level persistence before economic features.

## Build

- authoritative Intercolony world/game state;
- save schema version;
- stable ID generator;
- one trivial persisted record;
- debug inspection.

## Test

Set:

```text
TestCounter = 7
TestString = "Intercolony"
```

Save → quit → reload.

## Acceptance criteria

- data persists;
- no duplicate state owner;
- version field exists;
- debug action can inspect state.

---

# 95. Phase 2 — Debug framework

## Goal

Make iteration fast.

## Build

Dev actions for:

- open Intercolony debug window;
- dump current state;
- clear test state;
- advance refresh;
- create test entity.

## Acceptance criteria

A developer can force a known test state in seconds without waiting through normal gameplay.

---

# 96. Phase 3 — Settlement economic profiles

## Goal

Give eligible settlements stable economic identities.

## Build

- economic profile;
- demand weights;
- supply weights;
- quality tendency;
- labor tendency placeholder;
- persistence or deterministic regeneration;
- debug inspector.

## Acceptance criteria

- every eligible settlement gets a profile;
- profiles differ;
- modded factions do not crash;
- save/load stable;
- destroyed settlements handled gracefully.

---

# 97. Phase 4 — Market opportunity generation

## Goal

Generate non-binding demand.

## Build

- Market Opportunity entity;
- refresh cadence;
- expiration;
- simple item/category selection;
- quantity;
- price;
- deadline;
- counterparty;
- rudimentary market UI.

Begin with a narrow set of vanilla commodities if necessary.

## Acceptance criteria

- several settlements generate different opportunities;
- opportunities expire;
- refresh is not per tick;
- state survives save/load where intended;
- player can inspect each opportunity.

---

# 98. Phase 5 — First playable vertical slice: commodity Sales Order

## Goal

Deliver the first complete Intercolony gameplay loop:

> See demand → accept → deliver → receive silver.

## Scope

Use one simple item family first.

Example:

- raw food only.

## Build

- accept opportunity;
- create Sales Order;
- state machine;
- deadline;
- destination;
- physical delivery;
- validation;
- payment;
- success/failure;
- basic Orders UI.

## Acceptance criteria

A normal player can complete a transaction without dev tools.

Save/load tested at:

- accepted;
- preparing;
- transit/arrival state if applicable;
- completed;
- failed.

This milestone is the foundation of the entire mod.

---

# 99. Phase 6 — Generalized Sales Order item matching

## Goal

Support more than one commodity.

## Add

- arbitrary eligible ThingDefs;
- categories;
- stackables;
- quality constraints;
- stuff/material constraints;
- condition constraints.

## Test examples

- 1,000 Rice;
- 20 Excellent Dining Chairs;
- 5 Normal-or-better weapons;
- 200 Cloth.

## Acceptance criteria

One centralized validation path supports all test cases.

---

# 100. Phase 7 — Unique goods / capital equipment technical spike

## Goal

Prove safe representation and transfer of individual objects.

## Prototype cases

1. sell one Masterwork chair;
2. sell one sculpture with art metadata;
3. buy one stove;
4. save/load before completion;
5. install purchased equipment;
6. preserve quality/material/HP;
7. test one modded minifiable building.

## Deliverable

A written technical note inside the repo documenting:

- chosen representation;
- serialization strategy;
- unsupported edge cases;
- compatibility risks.

## Acceptance criteria

A robust strategy exists before generalized implementation.

---

# 101. Phase 8 — Finished goods market

## Goal

Make furniture, art, weapons, apparel, and equipment normal market participants.

## Build

- unique listing details;
- quality-aware valuation;
- material-aware valuation;
- art detail display;
- eligible minifiable equipment support;
- filters.

## Acceptance criteria

A colony can intentionally operate as a furniture or art business.

---

# 102. Phase 9 — Find Buyer

## Goal

Support surplus-first commerce.

## Flow

Player selects existing stock → Intercolony searches known demand → returns counterparties.

## Build

- item selection;
- demand lookup;
- saturation;
- offer comparison;
- create sale from result.

## Acceptance criteria

Player can turn a large existing surplus into deliberate sales without manually browsing every settlement.

---

# 103. Phase 10 — Procurement / RFQ MVP

## Goal

Allow targeted purchasing without universal availability.

## Build

- RFQ entity;
- item selection;
- quantity;
- supplier response generation;
- zero/partial/full quotes;
- expiry;
- comparison UI.

## Acceptance criteria

- requesting scarce goods can fail;
- suppliers differ in price and quantity;
- modded goods do not crash request generation.

---

# 104. Phase 11 — Purchase Order fulfillment

## Goal

Complete the buy-side loop.

> Request → quote → accept → physically receive/collect item.

## Build

- Purchase Order;
- payment;
- pickup or delivery;
- item creation/transfer;
- failure;
- save/load.

## Test

Buy:

- commodity;
- weapon/apparel item;
- chair;
- stove/workbench.

## Acceptance criteria

All arrive physically and preserve expected properties.

---

# 105. Phase 12 — Logistics expansion

## Goal

Offer meaningful logistics choices.

## Add

- seller delivery;
- buyer pickup;
- player pickup;
- supplier delivery;
- logistics pricing modifier;
- ready-for-pickup state;
- arrival events.

## Acceptance criteria

At least two fulfillment modes produce a real trade-off.

---

# 106. Phase 13 — Commercial reputation

## Goal

Make repeated commerce matter.

## Build

- reputation state;
- event-based updates;
- UI;
- reputation tiers;
- opportunity size/frequency effect.

## Acceptance criteria

Two colonies with different trade histories receive observably different future opportunities.

---

# 107. Phase 14 — Recurring contracts

## Goal

Turn customers into strategic commitments.

## Start simple

> Deliver X units of category Y every quadrum for N quadrums.

## Build

- contract entity;
- recurring order generation;
- payment;
- breach;
- completion;
- renewal;
- UI.

## Acceptance criteria

A multi-cycle contract survives save/load and affects production planning.

---

# 108. Phase 15 — Labor control feasibility prototype

## Goal

Solve employee pawn control.

This is a mandatory spike.

## Build

One employee, one simple temporary contract.

Test all questions from Section 33.

## Deliverable

`LABOR_TECHNICAL_NOTES.md` or equivalent containing:

- chosen strategy;
- patches/hooks required;
- known incompatibilities;
- restoration behavior;
- unresolved risks.

## Acceptance criteria

The team can confidently answer:

> "Can outside employees behave like useful workers without corrupting faction/pawn state?"

If not, redesign before proceeding.

---

# 109. Phase 16 — Basic temporary labor

## Goal

Hire one worker for a fixed period.

## Build

- worker candidate;
- hire action;
- arrival;
- work control;
- end date;
- departure;
- basic wage payment.

## Acceptance criteria

- employee works;
- employee survives save/load;
- contract expires;
- employee leaves cleanly;
- original affiliation restored or preserved correctly.

---

# 110. Phase 17 — Labor market UI

## Goal

Make hiring a proper gameplay loop.

## Add

- available workers;
- skills;
- source settlement;
- wage;
- minimum duration;
- contract comparison;
- current employee list.

## Acceptance criteria

A player can make a hiring decision without dev tools or hidden information.

---

# 111. Phase 18 — Payroll and arrears

## Goal

Make employment economically binding.

## Add

- recurring payroll;
- payment schedule;
- arrears;
- warnings;
- termination escalation;
- debt record.
- **A choice of wage structure at hire, per §37 — prepaid, periodic (quadrum), or daily.** This
  was implicit in "payment schedule" and is now named, because it is the player-facing half of
  the phase and the reason the phase matters. Phase 16 shipped prepaid-only as a deliberate
  scoping decision, which makes a long contract a large lump sum the player may not be able to
  raise, and removes the risk §37 lists as prepaid's whole downside ("employee may die").
  Prepaid should stay available and be *cheaper in total* — that is its stated benefit — with
  periodic payment as the default for anything long.

## Acceptance criteria

Insufficient silver creates understandable escalating consequences rather than crashes or silent deletion.

A player can choose how a worker is paid, and the trade-off between structures is visible at
the moment of hiring rather than discovered afterwards.

---

# 112. Phase 19 — Employer reputation

## Goal

Make treatment of workers affect future labor supply.

## Add

- employer reputation;
- completion;
- non-payment;
- injury/death effects;
- applicant/wage effects.

## Acceptance criteria

A bad employer experiences meaningfully worse hiring conditions.

---

# 113. Phase 20 — Combat clauses and compensation

## Goal

Prevent hired workers from becoming economically optimal disposable shields.

## Add

- civilian;
- armed employee;
- security contractor;
- death/injury compensation;
- combat-use tracking where technically feasible.
- **§88 hostility policy — the whole of it, trade and labor.** §88 requires a dedicated
  edge-case policy "before public labor release" and this is the phase whose subject matter it
  is: an employee whose home faction turns hostile is exactly §88's "enemies in the middle of a
  bedroom" case, and it is a combat-clause question before it is anything else.
  - active employment when the source faction turns hostile: suspend, terminate, or a special
    state — decided deliberately, not by whatever `SetFaction` happens to do;
  - in-flight sales orders, purchase orders and recurring contracts with a faction that turns
    hostile: currently `IntercolonyMarketAccess.IsAccessible` blocks *new* business but says
    nothing about obligations already booked and paid for;
  - kept in one phase on purpose: a policy split across two phases is how the trade half and
    the labor half end up contradicting each other.
  - **Phase 16 shipped a placeholder that must be replaced here:** a worker whose faction turns
    hostile while still travelling has the contract failed at the gate and forfeits the prepaid
    wage. That avoids spawning an enemy inside the colony; it is not a considered policy. A
    faction that turns hostile while the worker is already on the map is not handled at all.

## Acceptance criteria

Using civilian workers aggressively in combat has meaningful cost.

A source faction turning hostile mid-contract produces a stated, understandable outcome for
both the employee and any booked trade obligations — never a silent enemy inside the colony,
and never a silently voided obligation.

---

# 114. Phase 21 — Job postings and applicants

## Goal

Turn labor into a two-sided market.

## Build

Player specifies:

- skill needs;
- positions;
- duration;
- wage.

System returns applicants after delay.

## Acceptance criteria

Higher wages and better employer reputation measurably improve applicant quantity/quality.

---

# 115. Phase 22 — Long-term employment

## Goal

Support stable recurring workforces.

## Add

- long fixed-term contracts;
- open-ended contracts;
- renewal;
- voluntary non-renewal;
- termination rules.
- **Recurring *supply* contract renewal, which §107 listed and Phase 14 did not build.** A
  completed agreement currently just ends. Renewal belongs here rather than back in §107 because
  this phase builds renewal, voluntary non-renewal and termination anyway — and one renewal
  mechanism serving both employment and supply agreements is strictly better than two partial
  ones that drift apart. Either side must be able to decline, and reputation should influence
  whether the other side offers.

## Acceptance criteria

Employees can remain for long periods without faction-state drift or save corruption.

A recurring supply agreement that runs its course either renews or is declined for a stated
reason. Neither employment nor supply agreements end by silently lapsing.

---

# 116. Phase 23 — Employee-to-colonist transition

## Goal

Add late-game narrative conversion.

## Add

- eligibility;
- release fee;
- negotiation;
- diplomatic consequences;
- voluntary choice.

## Acceptance criteria

Conversion is rare/meaningful and cannot be exploited as cheap recruitment.

---

# 117. Phase 24 — Economic integration and dashboard

## Goal

Help the player understand the business without turning the mod into accounting software.

Possible UI:

```text
Last quadrum

Sales revenue           8,200
Purchases              -3,100
Payroll                -2,050
Transport                -450
Compensation               0
--------------------------------
Net cash movement       2,600
```

Contract estimate:

```text
Expected revenue        7,400
Estimated inputs       -1,300
Payroll allocation     -2,100
Estimated logistics     -400
--------------------------------
Estimated margin        3,600
```

Use estimates carefully.

---

# 118. Phase 25 — Polish and compatibility

## Goal

Prepare for broad public use.

## Tasks

- localization framework;
- settings;
- tooltip polish;
- error handling;
- performance profiling;
- modded content tests;
- DLC matrix;
- UI scaling;
- save migration;
- compatibility notes;
- documentation.
- **Resolve dangling mechanisms — decide or delete.** Code that is implemented and honoured but
  never exercised is a liability: it looks like a feature, is never tested by play, and rots.
  Each one gets a decision here, not a deferral.
  - `OrderLine.minHitPointsPercent` — enforced by the matcher since Phase 6, but no generator
    ever produces a demand that uses it, so used/damaged-goods trading does not exist in
    practice. Either generate secondhand demand (see the new §125 Goods question) or remove the
    field and its matcher branch. Deliberately landed in this phase and not earlier: it is a
    loose end, not a feature the economy is waiting on.

---

# 119. Phase 26 — Public beta

## Entry criteria

- no known save corruption;
- core commerce loop stable;
- procurement stable;
- finished goods/equipment stable;
- recurring contracts stable;
- labor stable enough for normal play;
- debug tools exist;
- migration policy exists.

## Beta focus

- balance;
- exploit discovery;
- compatibility;
- UX;
- performance;
- unexpected faction/pawn interactions.

---

# 120. Phase 27 — Finished product / 1.0 objective

Intercolony 1.0 should be considered feature-complete when the following are true.

## Commerce

- settlements generate meaningful demand;
- player can accept Sales Orders;
- deadlines and delivery work;
- quality/material conditions work;
- commodities and finished goods work;
- furniture/art/equipment work;
- Find Buyer works.

## Procurement

- RFQs work;
- quotes can be zero/partial/full;
- purchases physically arrive or are collected;
- capital equipment can be purchased;
- scarcity remains meaningful.

## Logistics

- at least two meaningful fulfillment modes exist;
- distance affects decisions;
- no default magical instant transaction for everything.

## Relationships

- Commercial Reputation persists;
- reputation changes opportunities;
- recurring supply contracts exist.

## Labor

- temporary workers can be hired;
- longer contracts exist;
- wages/payroll work;
- arrears have consequences;
- Employer Reputation works;
- combat/death risks are accounted for;
- employees leave cleanly.

## Reliability

- save/load robust;
- no known persistent-state corruption;
- major edge cases degrade gracefully;
- performance acceptable in large modded worlds.

## Compatibility

- common modded items work generically;
- unsupported special items can be excluded cleanly;
- DLC checks do not assume every expansion is installed.

## UX

- the player can understand obligations;
- the player can understand why a transaction succeeded or failed;
- important deadlines are visible;
- the market is usable without debug tools.

---

# 121. "Finished" does not mean frozen

After 1.0, possible expansions include:

- richer market shocks;
- trade routes;
- insurance;
- credit;
- deposits;
- commercial buildings;
- specialist contractors;
- inter-settlement services;
- warehousing;
- market intelligence;
- diplomacy-linked embargoes;
- deeper world-economy simulation.

These are explicitly **post-core**.

They should not delay the basic vision.

---

# 122. Recommended implementation order for Claude Code

When asked to "start building Intercolony," Claude Code should not attempt to infer the whole repo from this document.

Recommended process:

1. inspect all files;
2. determine whether bootstrap already exists;
3. identify current milestone;
4. summarize existing state;
5. propose the smallest next implementation;
6. name files to be added/changed;
7. implement;
8. build;
9. inspect compiler errors;
10. fix;
11. explain in-game test procedure;
12. update documentation if architecture changed.

At each milestone, create a short record:

```text
Implemented:
- ...

Not implemented:
- ...

Known limitations:
- ...

Manual test:
- ...
```

This reduces design drift and hallucinated completion.

---

# 123. Definition of a good coding task

Good:

> Implement persistent `MarketOpportunity` generation for eligible settlements, expose it through a minimal debug window, and verify save/load. Do not implement order acceptance yet.

Bad:

> Build the market system.

Good:

> Implement conversion from an existing Market Opportunity to a Sales Order and persist the order state. Do not implement delivery yet.

Bad:

> Build contracts.

Small tasks make this project much more likely to reach 1.0.

---

# 124. Documentation that should evolve with the code

As the project grows, consider adding:

```text
ARCHITECTURE.md
LABOR_TECHNICAL_NOTES.md
SAVE_FORMAT.md
COMPATIBILITY.md
CHANGELOG.md
CONTRIBUTING.md
```

Do not create these empty.

Create them when there is real information to preserve.

---

# 125. Questions intentionally left open

These should be resolved through implementation and playtesting.

### Commerce

- Should commercial reputation be faction-level, settlement-level, or both?
- How often should markets refresh?
- How much price variance feels believable?
- How should partial delivery work?
- Should late delivery receive partial payment?

### Goods

- Which buildings should be automatically eligible as capital equipment?
- How should unique modded comps be serialized?
- Should buyers care about artist identity?
- Should material preferences be hard requirements or bonuses?
- Should there be a market for used or damaged goods? The constraint exists in code
  (`OrderLine.minHitPointsPercent`) and is honoured, but nothing generates demand for it. If the
  answer is yes it becomes real work; if no, the field should go. Decided in Phase 25 (§118).

### Procurement

- Can one RFQ be split across suppliers?
- Are quotations binding immediately?
- When is payment taken?
- Can suppliers default?

### Logistics

- How much should delivery alter price?
- Should buyer pickup create an actual caravan?
- How should perishability interact with orders?
- How should vehicle mods integrate?

### Labor

- What internal faction model is safest?
- How much control should the player have over employees?
- Can civilians be drafted only for emergencies?
- How is workplace danger measured?
- What is fair death compensation?
- Should employees have explicit satisfaction beyond mood?
- Can employees join away missions?

### Balance

- How much more profitable should structured orders be than vanilla trade?
- How much should certainty reduce margins?
- How quickly should reputation rise?
- How expensive should skilled labor be?

These are not defects in the design.

They are deliberate playtest questions.

---

# 126. Design litmus tests

Before adding any major mechanic, ask:

### Does it create a new player decision?

If not, it may be unnecessary simulation.

### Does it strengthen the idea of relationships between colonies?

If not, it may belong elsewhere.

### Does it preserve RimWorld's physicality?

If it bypasses goods, pawns, logistics, or risk too easily, reconsider.

### Does it preserve scarcity?

If it guarantees every resource, reconsider.

### Can it survive save/load?

If not, it is not production-ready.

### Can a modded Thing plausibly work?

If only vanilla item names are supported, reconsider architecture.

### Can the player understand it?

If the rule needs a wiki to explain a basic failure, improve UX.

---

# 127. The core vision in one scenario

The finished product should make the following story possible without scripted quests:

> The colony starts with six people and a large rice harvest.

> The player opens Intercolony and discovers that a nearby settlement is willing to buy 900 rice.

> The colony accepts the order and delivers it.

> More food orders appear over the next year.

> The settlement's Commercial Reputation with the colony rises.

> Eventually it offers a recurring food agreement.

> The player realizes current labor is insufficient.

> They hire two seasonal farm workers and one cook.

> Payroll becomes a recurring cost.

> The colony buys a better stove from an industrial settlement through an RFQ.

> The stove arrives as a real object and is installed.

> Food production expands.

> A second settlement begins buying premium furniture.

> The colony's best builder now spends part of the year fulfilling commercial orders.

> A Masterwork chair earns a premium.

> The player must decide whether to keep that chair for colony comfort or sell it.

> A hired worker is injured during a raid, creating medical and contractual consequences.

> A year later the colony has customers, suppliers, employees, assets, obligations, and a reputation.

That is Intercolony.

Not a trading terminal.

Not a spreadsheet detached from RimWorld.

A civilian economic layer that gives the world map and the colony's productive capacity a new purpose.
