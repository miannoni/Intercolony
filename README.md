# Intercolony

> **Faction-driven commerce, contracts, equipment markets, and outside labor for RimWorld.**

Intercolony is a RimWorld mod concept built around one idea:

> The settlements around the player should be more than places to visit or random trade partners. They should be customers, suppliers, employers, employees, and long-term economic relationships.

The mod is intended to let the player deliberately build an economic operation instead of depending almost entirely on random traders and opportunistic sales.

A typical Intercolony loop should feel like:

> **Find demand → plan production → source inputs → hire labor → fulfill orders → get paid → build trust → expand capacity.**

The objective is **not** to turn RimWorld into an abstract stock-market simulator, nor to give the player an infinite catalog where any item can be summoned for silver. Scarcity, geography, diplomacy, logistics, quality, deadlines, and risk should remain meaningful.

---

## The fantasy

Intercolony should support stories like:

> We started as six colonists growing rice for survival. A nearby settlement began buying our surplus. After several successful deliveries they offered us a recurring food contract. We hired seasonal workers for harvests, bought a better stove from an industrial settlement, expanded the freezer, and eventually became the region's main food supplier.

Or:

> Our best builder became the foundation of a premium furniture business. We import wood, manufacture Excellent and Masterwork chairs, and fill purchase orders from wealthy settlements.

Or:

> We run an art colony. Unique sculptures are sold individually based on material, quality, and authorship, while hired workers handle hauling and agriculture so our artists can focus on high-value production.

The important part is that the economic system should create **new reasons to engage with RimWorld's existing systems**: farming, crafting, storage, caravans, work priorities, pawn skills, construction, bedrooms, combat, medical care, and diplomacy.

---

# Core systems

## 1. Faction and settlement commerce

Known settlements can have temporary demand for goods.

Examples:

- 1,200 corn;
- 400 medicine;
- 20 dining chairs of at least Excellent quality;
- 3 Large Sculptures;
- weapons;
- apparel;
- components;
- furniture;
- production equipment.

The player can:

- browse market opportunities;
- accept sales orders;
- search for buyers for existing surplus;
- deliver orders personally;
- allow buyers to collect orders where appropriate;
- build commercial reputation with repeat customers.

---

## 2. Procurement

The player can deliberately request goods instead of waiting for a random trader.

Example:

> Need 40 Components within 15 days.

Intercolony asks known counterparties for quotations.

Possible results:

- one settlement can provide all 40;
- several settlements can provide partial quantities;
- the only supplier is expensive;
- the item is available but far away;
- nobody currently has it;
- a hostile or technologically incapable settlement refuses or cannot supply it.

This preserves scarcity while giving the player agency.

---

## 3. Finished goods and capital equipment

The market is not limited to commodities.

Intercolony should support buying and selling:

- raw resources;
- intermediate goods;
- weapons and apparel;
- furniture;
- art;
- installable production equipment;
- other sensible physical RimWorld `Thing`s.

This means silver can buy **productive capacity**, not only consumables.

Examples:

> Buy an Excellent stove instead of building one.

> Buy a high-quality bed or chair.

> Purchase a Masterwork sculpture.

> Sell a Legendary sculpture to a wealthy settlement.

> Build a business around premium furniture instead of raw materials.

---

## 4. Long-term commercial relationships

Repeated successful business should matter.

Intercolony should maintain a **Commercial Reputation** separate from normal faction goodwill.

A trusted supplier may receive:

- larger orders;
- better opportunities;
- more predictable demand;
- access to scarce goods;
- recurring contracts;
- favorable commercial terms.

Eventually, a settlement that has repeatedly purchased food might offer:

> Deliver 800–1,200 units of food every quadrum for one year.

This transforms random trade into strategic production planning.

---

## 5. Labor market

The player should be able to hire workers from factions and settlements with which they have contact.

Examples:

- four farm workers for three days;
- one skilled builder for a quadrum;
- two cooks for six months;
- a long-term specialist paid every quadrum.

Possible payment structures:

- daily wage;
- quadrum payroll;
- fixed-term prepaid contract;
- longer recurring employment.

Employees are **not simply free temporary colonists**.

They should remain conceptually connected to their source faction and create obligations:

- wages;
- housing;
- safety;
- medical care;
- contract duration;
- possible combat restrictions;
- possible death/injury compensation;
- consequences for non-payment or abuse.

Intercolony should eventually maintain an **Employer Reputation** separate from Commercial Reputation.

---

# Design principles

### Faction-driven, not anonymous

The player should know **who** is buying, selling, or supplying workers.

Geography and relationships should matter.

### Demand creates production decisions

The system should make the player think:

> "I have a customer for this, so I should expand production."

rather than only:

> "I made this. I hope someone eventually buys it."

### Silver buys capacity

Money can be reinvested in:

- inputs;
- equipment;
- labor;
- logistics;
- infrastructure enabled by those purchases.

### Scarcity survives

Intercolony is not a universal vending machine.

Not every item is always available.

### Logistics stay physical

Distance, weight, caravans, delivery time, pickup, storage, and risk remain part of the game.

### Relationships persist

Trade history should change future opportunities.

### Simulation where it creates decisions; abstraction where it does not

The mod does **not** need to simulate the exact inventory, population, and production lines of every NPC settlement.

A believable economic profile is enough unless deeper simulation proves worthwhile during development.

### Compatibility matters

Whenever possible, Intercolony should reason from RimWorld definitions and object properties rather than giant hard-coded vanilla item lists.

---

# First playable milestone

The first version should be deliberately small:

> **A nearby settlement publishes a demand opportunity. The player accepts it as a Sales Order, physically delivers the required goods before the deadline, and receives payment. The order survives save/load and has clear success and failure states.**

That single vertical slice should be made reliable before procurement, labor, recurring contracts, or advanced economic simulation are added.

---

# Finished-product objective

A mature Intercolony release should allow the player to:

1. discover demand from known settlements;
2. accept and fulfill one-off sales orders;
3. search for buyers for surplus stock;
4. sell commodities, manufactured products, furniture, art, and equipment;
5. issue purchase requests and receive competing supplier quotations;
6. buy both ordinary goods and productive equipment;
7. choose between pickup and delivery where appropriate;
8. develop commercial reputation with counterparties;
9. receive recurring supply contracts;
10. hire short-term and long-term outside workers;
11. manage payroll and employment obligations;
12. develop employer reputation;
13. experience meaningful consequences for defaults, missed deliveries, unsafe employment, and contract breaches;
14. use the entire system without making vanilla traders, caravans, quests, or exploration irrelevant.

The exact UI, formulas, and implementation architecture are intentionally open to revision during playtesting.

---

# Development philosophy

Intercolony should be built in vertical slices.

For every major feature:

1. inspect the actual target RimWorld version and assemblies;
2. build the narrowest working version;
3. compile;
4. test in game;
5. test save/load;
6. test failure states;
7. play enough to determine whether the feature creates interesting decisions;
8. revise the design if reality is better than the document;
9. only then generalize.

Do not implement the entire economic simulation before the first transaction can be completed in a normal game.

---

# Suggested project identifiers

- **Repository:** `Intercolony`
- **Package ID:** `miannoni.intercolony`
- **Root C# namespace:** `Intercolony`

These are suggestions and may change before public release.

---

# Project status

**Pre-alpha / design and bootstrap stage.**

See [`DESIGN.md`](./DESIGN.md) for the full product specification, domain model, technical guardrails, implementation phases, testing strategy, and definition of done.

---

# License

To be decided before public release.

Code and original assets may eventually use different licensing terms.
