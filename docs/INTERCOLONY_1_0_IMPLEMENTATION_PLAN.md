# Intercolony 1.0 — Implementation Program

**Purpose:** This is the implementation handoff for the work from Intercolony 0.9.3 to 1.0.  
**Audience:** Claude or another coding agent working directly in the Intercolony repository.  
**Repository audited:** `miannoni/Intercolony`, `main`, current 0.9.3-era architecture, save schema 42.  
**Status:** Product direction is decided. Implementation details may adapt to current code, but the player-facing semantics and dependency order in this document are the default authority for the 1.0 program.

---

# 0. How to use this document

This is not an ideas backlog. It is the ordered implementation program for 1.0.

At the beginning of every development session:

1. Read `CLAUDE.md`.
2. Read this document.
3. Read `docs/1_0_IMPLEMENTATION_STATUS.md` if it exists.
4. Read only the relevant current sections of `DESIGN.md`, `PROGRESS.md`, `docs/PENDING_PLAYTESTS.md`, and technical notes for the slice being worked.
5. Inspect the actual current code before trusting a file name, line number, class shape, schema number, or old design assumption.
6. Inspect `git status`, current branch and HEAD.
7. Run a clean build before editing.
8. Continue from the **first unfinished slice whose dependencies are complete**. Do not re-plan the whole roadmap every session.

If `docs/1_0_IMPLEMENTATION_STATUS.md` does not exist, create it before substantive 1.0 work. It should contain:

```text
# Intercolony 1.0 Implementation Status

Current stage:
Current slice:
Last completed slice:
Current save schema:
Current mod version:

## Stage status
- [ ] Stage 0 — Program spine
- [ ] Stage 1 — Settlement economies
- [ ] Stage 2 — Market fundamentals overhaul
- [ ] Stage 3 — Circumstance-driven economic events
- [ ] Stage 4 — Brand strength & colony specialization
- [ ] Stage 5 — Commercial relationships & negotiation
- [ ] Stage 6 — Procurement parity
- [ ] Stage 7 — Commercial history
- [ ] Stage 8 — 1.0 integration and release gate

## Decisions / deviations
...

## Play evidence still required
...

## Next executable slice
...
```

Update that status file after every coherent commit or phase gate. It is the continuity mechanism between sessions.

The old `Intercolony_Claude_Execution_and_Decision_Guide.md` remains useful procedural guidance. This document does not replace its principles such as:

- one slice, one behavioral claim;
- use existing authoritative owners;
- smallest solution first;
- test the real production path;
- proceed on high-confidence implementation details;
- decide and log medium-confidence reversible details;
- raise a hand only for genuinely structural/player-strategy decisions;
- do not widen a slice because an adjacent code smell exists.

Where the older guide conflicts with this 1.0 plan because this plan explicitly authorizes new 1.0 state or systems, **this plan wins for the authorized 1.0 feature**.

---

# 1. The 1.0 product definition

Intercolony 1.0 should make RimWorld's settlements feel like participants in a lightweight regional economy.

A settlement should have a stable economic character. Its market should have understandable supply and demand conditions rather than mostly cycle-to-cycle noise. Conditions should move over time, propagate modestly through related markets, and be disrupted by RimWorld-style events. The player should be able to both sell and procure through comparably deep systems, build long-term commercial relationships, negotiate meaningful deals, develop a reputation for the quality of specific products, and later inspect the history of those relationships.

The target is **more economic fundamentals, not an economics doctorate**.

The player should be able to tell a story like:

> A nearby industrial settlement normally supplies components and buys food. A drought tightens food supply across the region, components become more expensive as industrial costs rise, and several settlements begin paying more for food. My colony has a strong reputation for high-quality firearms, so a military settlement is willing to accept my counteroffer on a rifle contract. I source components through an existing procurement agreement, fulfill the order, and that transaction becomes part of the commercial history between our colonies.

The mod should create that story through a small number of connected systems rather than through dozens of isolated mechanics.

---

# 2. Final 1.0 feature scope

These seven player-facing items are the 1.0 scope.

## 2.1 Settlement economies

Give each settlement a stable economic identity that influences what it normally produces, has in surplus, consumes, values, and can plausibly supply.

## 2.2 Market fundamentals overhaul

Replace mostly independent random demand variation with lightweight persistent supply, demand, scarcity, and surplus pressures that influence selling, buying, availability and pricing and can propagate modestly between related markets and nearby settlements.

## 2.3 Circumstance-driven economic events

Periodically disturb the underlying market with understandable RimWorld-style shocks such as droughts, wars, epidemics, migrations, or construction booms.

## 2.4 Brand strength & colony specialization

Build product-specific brand strength from the actual quality of goods sold, with reputation carrying strongly between similar products and only weakly between unrelated ones, rewarding both craftsmanship and specialization.

## 2.5 Commercial relationships & limited negotiation

Make established counterparties meaningfully different from strangers and allow important deals to be countered or binding obligations to be renegotiated in a constrained, legible way.

## 2.6 Procurement parity

Give buying comparable depth to selling through a supplier market, RFQs, a dedicated purchase-order surface, and recurring procurement agreements.

## 2.7 Commercial history

Give each settlement a readable history of deals, contracts, renegotiations, successes, failures, brand milestones, and other meaningful commercial moments with the player's colony.

---

# 3. Explicitly cut from the 1.0 program

Do **not** silently re-add these while implementing adjacent systems:

- Receiving / Dispatch zones.
- Counterparty personality traits such as Aggressive, Bargain Hunter, Flexible, etc.
- Abstract competitor simulation or opportunities disappearing because rival traders took them.
- Exclusive/preferred/minimum-volume contract portfolio types beyond what is necessary for ordinary recurring sale and procurement agreements.
- Cross-system labor recommendation widgets.
- Economic world-map overlay.
- Banking.
- Loans.
- Credit markets.
- Insurance.
- Taxes.
- Multiple currencies.
- Stock markets.
- Corporate entities.
- Marketing campaigns.
- Logos/advertising as a branding minigame.
- Full NPC-to-NPC transaction simulation.
- Physical simulation of every NPC caravan.
- A factory/production overhaul.
- A diplomacy overhaul.
- A generalized event bus built for hypothetical future systems.

If one of these becomes necessary to make an approved 1.0 behavior technically possible, record the conflict and raise a hand rather than quietly expanding scope.

---

# 4. Architectural facts already present in the repository

Before designing new architecture, preserve what the current code already gets right.

## 4.1 `IntercolonyWorldComponent` owns persistent world economic state

This remains the authoritative owner of Intercolony state that survives save/load.

Do not introduce another authoritative singleton or map-level owner for the new regional economy.

New persisted market state, economic events, brand records, negotiation state that truly needs persistence, and commercial timeline records should ultimately be owned by the world component, directly or through persisted entities owned by it.

## 4.2 `SettlementEconomicProfile` already represents stable identity

The current profile already carries:

- settlement ID and faction identity;
- tech tier;
- wealth tier;
- archetype;
- category demand weights;
- category supply weights;
- quality preference;
- labor supply modifier;
- volatility.

It is deliberately generated deterministically from the world economy seed and settlement ID rather than persisted.

**Keep that concept.**

Do not turn `SettlementEconomicProfile` into a bag containing every changing market variable. Stable identity and changing state are different things.

## 4.3 Current exact-good demand contains cycle noise

The current `SettlementEconomicProfile.DemandFor(ThingDef, category)` adds a deterministic rolling per-cycle multiplier to baseline demand. This was useful to keep old demand from feeling static, but it is exactly the kind of noisy variation the 1.0 market overhaul is intended to replace.

The dynamic part of demand should move out of the profile and into explicit market state.

## 4.4 `IntercolonyPricing` is already the centralized pricing owner

Preserve that rule.

Do not start calculating brand premiums, scarcity premiums, negotiation prices, or event premiums in UI files or order services.

Pricing inputs may become richer, but agreed transaction prices should still be formed through one authoritative pricing layer.

## 4.5 Selling and RFQs already consume the same settlement profile

Selling uses profile demand. Procurement/RFQs use profile supply.

This is why the market overhaul must happen **before** Procurement parity: both directions should be pointed at the new economic model once, then the supplier-market surface should be built against the finished interface.

## 4.6 Commercial reputation already answers "will they trust us to keep promises?"

`CommercialReputation` is per settlement and already tracks reliability-oriented outcomes.

Do not overload it with craftsmanship.

For 1.0, keep three ideas conceptually separate:

- **Faction goodwill:** political relationship.
- **Commercial reputation:** reliability as a trading partner.
- **Brand strength:** expected quality of a specific product or closely related products.

## 4.7 Commercial history already has a compact aggregate

`CommercialHistoryEntry` currently retains completed sale count and total supplied quantity by settlement and exact item.

Keep that aggregate if current contract eligibility or other logic depends on it.

The new readable commercial timeline should complement it, not turn this compact aggregate into an all-purpose history monster.

---

# 5. Locked design principles for the whole 1.0 program

These are defaults. Do not re-litigate them during ordinary implementation.

## 5.1 Identity, state, and events are separate layers

Think of the economy as:

```text
Stable settlement identity
        ↓
Persistent current market pressure
        ↓
Temporary event shocks
        ↓
Effective demand / supply / price / availability
        ↓
Selling, procurement, contracts and negotiation
```

- `SettlementEconomicProfile` = what the settlement **normally is**.
- Market state = what its economy is **currently experiencing**.
- Economic events = unusual circumstances **pushing it away from normal**.

## 5.2 Simulate pressure, not an entire invisible economy

Do not simulate households, factories, warehouses, GDP, NPC balance sheets, or every NPC transaction.

Persist bounded indexes such as demand pressure and supply pressure and let them influence the systems the player can actually interact with.

## 5.3 Randomness remains, but it does not define the economy

Randomness can:

- choose among several plausible goods;
- vary exact quantities modestly;
- vary event timing;
- vary whether a supplier answers an RFQ;
- break ties.

Randomness should **not** be the main reason a settlement suddenly wants something completely different from its identity and current conditions.

The market should answer "why is this happening?" more often than "the seed rolled it."

## 5.4 Binding transaction economics do not move underneath the player

Once an order or accepted agreement has a price/quantity/deadline, later market movement does not silently rewrite that obligation.

Only an explicit renegotiation may change agreed terms.

This is essential for player trust.

## 5.5 Coarse refresh, not per-tick simulation

The economy advances on existing coarse market refreshes or similarly bounded scheduled updates.

Do not add per-tick economic simulation.

## 5.6 Mod compatibility remains def-driven

Prefer RimWorld `ThingDef` metadata, current classifier rules, categories, comps, tags, tech requirements and existing generic paths.

Do not solve product similarity or economic classification by building giant vanilla defName whitelists.

## 5.7 The player sees causes, not formulas

Expose:

- "Food shortage";
- "Strong local demand";
- "Drought";
- "Excellent firearms brand";
- "Trusted commercial partner".

Do not expose a screen full of hidden coefficients unless in dev/debug output.

## 5.8 Every accumulating collection has a retention policy

Persistent history/events/records must be bounded or compacted.

Never add an append-only list with no explicit retention rule.

## 5.9 New state earns its schema bump

If saved shape changes:

- increment the schema;
- write a narrow migration;
- load an actual prior-version save;
- verify active obligations survive;
- record the migration in `PROGRESS.md` and pending evidence where appropriate.

Do not avoid a necessary persisted field merely to avoid a schema bump.

Do not add persisted state when the fact can be reconstructed safely.

---

# 6. Master dependency order

The feature order is intentionally:

```text
Stage 0  Program spine
   ↓
Stage 1  Settlement economies
   ↓
Stage 2  Market fundamentals overhaul
   ↓
Stage 3  Circumstance-driven economic events
   ↓
Stage 4  Brand strength & colony specialization
   ↓
Stage 5  Commercial relationships & negotiation
   ↓
Stage 6  Procurement parity
   ↓
Stage 7  Commercial history
   ↓
Stage 8  1.0 integration / stabilization
```

Do not move Procurement parity ahead of Stage 2.

Do not build negotiation before Brand unless a narrow preparatory refactor is required.

Do not wait until Stage 7 to begin recording the events Stage 3–6 will later need to display.

---

# 7. Stage 0 — Program spine

**Goal:** Create the minimum infrastructure needed to execute the 1.0 program without losing decisions, history, or test baselines.

This is not a player-facing feature release by itself.

---

## 0.1 Establish the status ledger

Create and maintain `docs/1_0_IMPLEMENTATION_STATUS.md` using the template above.

Each slice entry should say:

- behavioral claim;
- files changed;
- tests run;
- whether schema changed;
- evidence still requiring play;
- any medium-confidence decision;
- exact next slice.

This prevents "what were we doing?" sessions.

---

## 0.2 Capture the 0.9.3 market baseline before changing it

Add or extend debug/self-test tooling to record a representative baseline for:

- opportunities per settlement over several refreshes;
- category distribution;
- exact-good turnover;
- average lot sizes;
- average unit price factor distribution;
- RFQ response rate;
- full vs partial vs zero quote frequency;
- effective supplier quantities;
- differences across current settlement archetypes.

This is not to preserve old balance.

It provides evidence when the new economy accidentally produces:

- no offers;
- one dominant category everywhere;
- effectively infinite supply;
- massive price inflation;
- settlements whose archetypes cease to matter.

Prefer deterministic debug output that can run without normal play.

---

## 0.3 Introduce a compact commercial timeline spine

Commercial History is implemented visually in Stage 7, but Stages 3–6 will generate information that cannot be reconstructed later.

Add a small persisted event record now.

Suggested conceptual shape:

```csharp
CommercialEventRecord
{
    int id;
    int tick;
    int settlementId;
    CommercialEventType type;

    // Optional references / compact context:
    int relatedEntityId;
    ThingDef thingDef;
    int quantity;
    float silverAmount;
    string compactDetail;
}
```

Do not treat this as a generalized event bus.

It is simply a retained **commercial timeline record** owned by `IntercolonyWorldComponent`.

Initial event types only need to cover facts already present or immediately needed, for example:

- SaleCompleted
- SaleFailed
- PurchaseCompleted
- PurchaseCancelled
- ContractStarted
- ContractCompleted
- ContractFailed

Later stages may add:

- EconomicEventStarted/Ended only if useful in a settlement's commercial story
- BrandMilestone
- CounterofferAccepted/Rejected
- RenegotiationAccepted/Rejected
- ProcurementContractStarted/Ended

### Retention

Detailed timeline must be bounded.

Default recommendation:

- retain compact cumulative aggregates indefinitely;
- retain the most recent ~1,000 detailed commercial timeline records globally as an initial safe cap;
- profile serialized size before changing that number;
- prune oldest timeline records only, never active obligations or compact aggregates.

If a better cap is clearly demonstrated by profiling, choose it and record the decision. Do not ask solely about 800 vs 1,000 vs 1,200 records.

---

## 0.4 Do not attempt a perfect historical migration

Existing saves may contain enough retained orders to reconstruct some prior events, but not necessarily every precise historical moment.

Rules:

- backfill only what can be reconstructed with trustworthy timestamps and semantics;
- never invent transaction dates;
- preserve existing compact aggregate history;
- Stage 7 may state "Detailed history recorded since version X" if necessary.

---

## Stage 0 acceptance gate

Do not begin Stage 1 until:

- clean build passes;
- 1.0 status ledger exists;
- baseline market diagnostics exist or current self-tests clearly provide equivalent coverage;
- commercial timeline has an authoritative owner and bounded retention policy;
- prior 0.9.3 save loads if schema changed;
- no existing sales/procurement/labor self-test regressed.

---

# 8. Stage 1 — Settlement economies

**Player-facing goal:** Settlements have stable, legible economic identities that meaningfully define their normal supply and demand.

**Architectural goal:** Make `SettlementEconomicProfile` an explicit baseline API that Stage 2 can safely layer dynamic market state over.

---

## 1.1 Preserve deterministic settlement identity

Keep profile generation deterministic by economy seed + settlement identity.

Do not persist the profile just because Stage 2 will persist current market conditions.

The profile remains safe to regenerate and cache.

---

## 1.2 Separate baseline demand from changing demand

The current exact-good `DemandFor(def, category)` mixes stable economic identity with rolling cycle variance.

Refactor the API so consumers can clearly ask for **baseline** demand/supply without silently getting current-cycle noise.

Possible shape:

```csharp
float BaseDemandFor(IntercolonyProductCategory category);
float BaseDemandFor(ThingDef def, IntercolonyProductCategory category);
float BaseSupplyFor(IntercolonyProductCategory category);
```

The exact names may differ.

The important rule is semantic:

> Profile methods return stable identity. Dynamic market state is applied elsewhere.

For exact goods, a small deterministic **stable affinity** is acceptable so every item inside one category is not identical. It should not change every market cycle.

Example:

```text
Industrial settlement:
  intermediate baseline demand: 1.35
  steel stable affinity: 1.08
  components stable affinity: 0.95
```

These are identity differences, not temporary shortages.

---

## 1.3 Keep archetypes probabilistic

Agricultural does not mean "can only sell food."

Industrial does not mean "never needs components."

Archetypes shift weights and capability; they do not create hard categorical bans except where tech/content rules genuinely make supply impossible.

---

## 1.4 Make economic identity legible

Add a compact player-facing representation using the least disruptive existing surface, preferably the Relations/settlement detail area rather than another major tab.

Minimum useful information:

```text
Economic profile: Industrial / Comfortable
Usually supplies: components, processed materials, machinery
Usually demands: food, raw inputs, apparel
Quality preference: moderate
```

Do not show six raw float arrays.

If the best current UI home is ambiguous, choose between:

**A.** settlement detail/Relations panel, or  
**B.** an existing settlement tooltip/details surface.

Choose the option that adds less navigation/UI duplication and log the choice. This is a medium-confidence UI placement choice; do not block development on it.

---

## 1.5 Preserve debug visibility

Profile debug output should still make it possible to compare:

- archetype;
- wealth;
- tech;
- baseline category demand;
- baseline category supply;
- stable exact-good affinity where relevant.

This becomes critical when Stage 2 adds current pressure on top.

---

## Stage 1 likely code areas

Expect to inspect/change at minimum:

- `Source/Intercolony/Core/SettlementEconomicProfile.cs`
- `Source/Intercolony/Core/SettlementProfileGenerator.cs`
- `Source/Intercolony/Core/IntercolonyProductCategory.cs`
- `Source/Intercolony/Debug/IntercolonyProfileSelfTest.cs`
- relevant Relations/UI code

Do not assume these are the only files.

---

## Stage 1 acceptance gate

Prove:

1. Same economy seed + same settlement still yields the same baseline profile across save/load.
2. Baseline profile no longer depends on current refresh count.
3. Same-archetype settlements can still differ modestly.
4. Different archetypes produce visibly different economic tendencies.
5. Modded/undefined tech faction handling remains safe.
6. Existing consumers can still compile while Stage 2 is prepared; if temporary compatibility adapters exist, mark them for deletion in Stage 2.
7. Player can identify what a settlement is broadly good at supplying and likely to demand without reading debug numbers.

Commit Stage 1 before beginning Stage 2.

---

# 9. Stage 2 — Market fundamentals overhaul

**Player-facing goal:** The market stops feeling like noisy seeded rerolls and begins behaving like a lightweight market with persistent conditions.

**This is the foundational 1.0 stage.**

Do not rush through it merely to reach Procurement parity.

---

## 2.1 Add persisted current market state

Introduce a compact per-settlement market-state record owned by `IntercolonyWorldComponent`.

Recommended conceptual shape:

```csharp
SettlementMarketState : IExposable
{
    int settlementId;

    // Centered around 1.0.
    float[] demandPressure; // one per IntercolonyProductCategory
    float[] supplyPressure; // one per IntercolonyProductCategory

    int lastAdvancedRefresh;
}
```

Prefer fixed arrays keyed by the existing six `IntercolonyProductCategory` values unless real implementation evidence shows that a different compact representation is materially safer.

Do **not** begin with a persisted state per ThingDef across every settlement. That scales poorly and is more simulation detail than the player needs.

Exact-good differences should primarily come from:

- stable profile affinity;
- event-specific targeted modifiers where needed;
- current finite offer consumption;
- actual accepted transactions.

---

## 2.2 Create one authoritative effective-economy API

Do not let `MarketOpportunityGenerator`, `FindBuyerService`, `RfqService`, contracts and pricing each invent their own interpretation of "current demand."

Add a narrow service/read model that answers questions such as:

```text
EffectiveDemand(settlement/profile, def, category)
EffectiveSupply(settlement/profile, def, category)
CurrentDemandPressure(...)
CurrentSupplyPressure(...)
ExplainDemand(...)
ExplainSupply(...)
```

Exact class/method names are local implementation details.

This owner should combine:

```text
stable profile baseline
× persistent market pressure
× active event modifier
```

with explicit bounds.

All player-facing market systems should migrate to this API during Stage 2.

---

## 2.3 Remove cycle-to-cycle demand noise as the main driver

Delete or neutralize the old rolling `0.55–1.45`-style exact-good demand drift once all consumers use the new effective-economy API.

Do not leave two dynamic systems stacked together.

A small random choice among plausible goods is fine.

A large random multiplier masquerading as demand state is not.

---

## 2.4 Pressure lifecycle: mean reversion

Pressure should gradually return toward normal in the absence of new shocks.

Example conceptual behavior:

```text
Demand pressure = 1.40
next refresh without new pressure -> ~1.33
then ~1.27
...
eventually back toward 1.0
```

The exact coefficient is balance tuning.

Required property:

- shocks persist long enough to matter;
- they do not permanently distort the save;
- the economy does not converge instantly.

Keep pressure bounded. Normal non-event conditions should generally stay in a restrained range rather than creating 5x price swings.

---

## 2.5 Player transactions nudge, not dominate, local pressure

The player's real trades should have modest economic consequences.

Examples:

- completing a very large sale into a settlement slightly relieves that settlement's demand pressure for the relevant category;
- buying heavily from a supplier slightly tightens that supplier's supply pressure;
- small transactions barely move a regional market.

Use diminishing impact so splitting one trade into ten tiny trades cannot create ten times the pressure effect.

Pressure impact should be derived from **total economic quantity/value**, not transaction count.

Do not let the player trivially pump a market back and forth for profit.

---

## 2.6 Add coarse economic chains

Economic chains should be **small directional relationships between the existing broad categories**, not a full bill-of-materials simulation.

Centralize the relationships in one table/service.

Initial conceptual links:

```text
Higher demand for ManufacturedGoods
    -> mild upward demand pressure on IntermediateGoods

Higher demand for Furniture
    -> mild upward demand pressure on Commodities + IntermediateGoods

Higher demand for CapitalEquipment
    -> upward demand pressure on IntermediateGoods

Tight Commodity supply
    -> mild tightening of IntermediateGoods supply
    -> weaker secondary tightening of Furniture/CapitalEquipment

Tight IntermediateGoods supply
    -> mild tightening of ManufacturedGoods/Furniture/CapitalEquipment supply
```

Coefficients should be deliberately small.

The goal is:

> "A steel/components shortage has consequences."

not:

> "We need to solve a 500-node input-output matrix."

Use tests that prove directionality and boundedness rather than a supposedly "correct" economic coefficient.

---

## 2.7 Add modest regional pressure diffusion

Nearby economies should influence one another enough to create regions without homogenizing the whole world.

On coarse refresh:

- sample/iterate a bounded set of nearby eligible settlements;
- blend a small amount of relevant pressure;
- weight by distance;
- cap the effect;
- do not perform all-pairs expensive work every tick.

A settlement's own identity and own shocks must remain more important than neighbors.

Test that:

- nearby settlements correlate modestly after a shock;
- distant settlements do not instantly mirror it;
- the world does not converge to one global pressure vector.

---

## 2.8 Selling integration

Update at least:

- `MarketOpportunityGenerator`
- `FindBuyerService`
- `IntercolonyPricing`
- any direct-sale appetite/availability calculations
- contract proposal generation where it reads market demand

Market opportunity generation should now follow this logic:

```text
1. Is this settlement economically accessible?
2. What categories does its stable profile normally demand?
3. What categories are currently under demand pressure?
4. Choose among plausible categories/goods with weighted randomness.
5. Size and price the opportunity using the effective economic context.
6. Apply relationship/reputation factors.
7. Freeze the resulting offer terms.
```

Randomness chooses among plausible outcomes; it no longer invents the economic context.

---

## 2.9 Procurement/RFQ integration

Update `RfqService` and related supplier calculations so:

```text
effective supply =
stable supplier capability
× current supply pressure
× event modifiers
```

Existing within-window finite supplier offer consumption remains useful.

Do not replace it with pressure.

They answer different questions:

- **Supply pressure:** broad current scarcity.
- **Offer consumption:** "you already bought 80 of the 100 units this supplier offered in this market window."

RFQs should become a consumer of the new market model **before** the supplier market is built in Stage 6.

---

## 2.10 Pricing integration

`IntercolonyPricing` remains the single pricing owner.

Add named price factors such as:

- Local demand
- Current shortage
- Current surplus
- Buyer wealth
- Distance
- Logistics
- Reputation
- later: Brand strength

Avoid double-counting.

If effective demand already combines baseline demand and pressure, do not separately multiply by the same pressure again under another name.

Every named factor must have a clear single semantic meaning.

---

## 2.11 Explainability

For any unusually high/low current offer, the player should be able to understand at least the major cause.

Examples:

```text
Local demand          x1.12
Food shortage         x1.18
Buyer wealth          x1.05
You deliver           x1.12
```

Do not expose every propagation coefficient.

Use the explanation system already present in pricing rather than building a second explanation framework.

---

## 2.12 Market diagnostics

Add/extend debug tests that can:

- print one settlement's baseline vs current pressure vs effective demand/supply;
- force a category pressure shock;
- advance several refreshes;
- verify mean reversion;
- verify chain propagation;
- verify regional diffusion;
- verify save/load retains current pressure;
- compare opportunity generation before/after a shock;
- compare RFQ supply before/after a shock.

---

## Stage 2 likely code areas

At minimum inspect:

- `Core/IntercolonyWorldComponent.cs`
- `Core/SettlementEconomicProfile.cs`
- new current-market state/service files, likely under `Core` or a dedicated narrow `Economy` area if justified
- `Market/MarketOpportunityGenerator.cs`
- `Market/FindBuyerService.cs`
- `Market/IntercolonyPricing.cs`
- `Procurement/RfqService.cs`
- `Contracts/ContractService.cs`
- market/profile/RFQ self-tests
- UI pricing explanation surfaces

A new narrow `Economy` folder is authorized if it cleanly holds the new state/service and avoids turning `Core` into an unstructured pile. Do not create a generalized domain framework.

---

## Stage 2 migration rule

This stage will almost certainly change save shape.

For an existing 0.9.3 save:

- initialize each settlement's market pressure to neutral `1.0`;
- do not try to infer fake historical shortages from old random rolls;
- preserve economy seed, profiles, active opportunities and all obligations;
- existing accepted order terms do not change;
- new opportunities after migration use the new system.

---

## Stage 2 acceptance gate

Do not begin Stage 3 until all are true:

1. Market pressure survives save/load.
2. Current demand does not depend primarily on per-cycle random multipliers.
3. Settlement archetype still matters strongly.
4. A forced shortage changes selling prices/opportunities in the expected direction.
5. The same shortage changes supplier/RFQ behavior in the expected direction.
6. Pressure mean-reverts.
7. Chain propagation is bounded.
8. Regional influence is bounded.
9. Existing accepted orders retain their stored economics.
10. No obvious buy-low/sell-high infinite arbitrage was introduced.
11. Existing market/RFQ/order self-tests pass with no hidden skips.
12. Debug output can explain why a market moved.

This stage deserves real play before proceeding if the resulting market feels obviously flat, chaotic, or economically inverted.

---

# 10. Stage 3 — Circumstance-driven economic events

**Player-facing goal:** The market has understandable RimWorld-style disruptions that create opportunities, shortages, and stories.

**Architectural goal:** Events are shocks applied to the Stage 2 market, not a parallel market generator.

---

## 3.1 Add a small persisted economic-event model

Conceptual shape:

```csharp
EconomicEvent : IExposable
{
    int id;
    EconomicEventType type;
    int startTick;
    int endTick;

    // Scope:
    int anchorSettlementId;
    float radiusTiles; // where appropriate
    int factionLoadId; // where appropriate

    // Effect summary:
    category demand/supply modifiers
    optional targeted-good selector
}
```

Do not use a generic game-wide event bus.

The economy service should be able to ask:

> Which active economic events affect this settlement/category/good?

---

## 3.2 Initial event set

Start with a restrained set that exercises different economic directions.

Recommended first six:

### Drought / Poor Harvest

- food/commodity supply down;
- food demand up;
- nearby agricultural settlements affected most strongly.

### War Mobilization

- manufactured goods demand up;
- medicine/weapons/armor-like goods favored;
- intermediate demand up secondarily.

### Epidemic / Disease Outbreak

- medicine and basic food demand up;
- optional labor effects only if they can be implemented without dragging Stage 3 into a Labor rewrite.

### Construction Boom / Rebuilding

- commodities/intermediate demand up;
- furniture demand follows;
- capital equipment demand may rise modestly.

### Migration / Refugee Influx

- food/apparel/basic-goods demand up;
- keep labor-side consequences out of 1.0 unless already trivial through existing labor supply modifiers.

### Animal Disease / Herd Loss

- animal availability/supply down where animal trade already supports it;
- food/textile secondary effects only if current classifier/data makes them clean.

It is acceptable to launch Stage 3 with four well-implemented events rather than six shallow ones. The minimum variety should include:

- one supply shock;
- one demand shock;
- one multi-category shock;
- one geographically regional event.

---

## 3.3 Event scope

Use the smallest believable scope:

- regional radius around an anchor settlement;
- faction-wide where the fiction clearly implies faction action;
- single settlement for local rebuilding/epidemic where appropriate.

Do not make every event global.

Do not require a bespoke map object.

---

## 3.4 Events disturb pressure; they do not replace baseline

An event should do one or both of:

1. apply an active temporary modifier while the event is live;
2. push underlying market pressure at start so aftereffects decay naturally through Stage 2 mean reversion.

Preferred pattern:

```text
Drought begins
→ immediate food supply pressure shock
→ active drought modifier while live
→ drought ends
→ active modifier disappears
→ remaining pressure gradually normalizes
```

This creates a tail without permanent distortion.

---

## 3.5 Event generation

Events may be scheduled from the coarse economy refresh.

Use deterministic/semi-deterministic seeded rolls appropriate to the existing project style.

Do not spam.

Target a world where:

- periods of relative normality exist;
- sometimes one region has an event;
- occasionally two events overlap;
- the entire economy is not permanently under five simultaneous crises.

Exact frequency is a balance choice to be tuned in play.

---

## 3.6 Player messaging

The event should be understandable before the player notices the price table.

Example:

```text
Poor harvest near Lanca

Several settlements in the region are reporting weak harvests.
Food supply is expected to remain tight for roughly 18 days.
```

Then pricing/tooltips can show:

```text
Poor harvest x1.14
```

Do not turn every event into a major red RimWorld alert. Use notification severity proportional to importance.

---

## 3.7 Event interactions with accepted obligations

Events may change:

- future offers;
- future RFQs;
- future negotiation leverage;
- the chance a future procurement contract cycle can be supplied.

Events do **not** silently change:

- already agreed sales price;
- already agreed purchase price;
- existing order quantity;
- existing deadline.

Renegotiation in Stage 5 is the explicit mechanism for modifying binding terms.

---

## 3.8 Debug controls

Provide dev actions to:

- force each event type;
- specify/identify anchor settlement;
- print affected settlements;
- advance/end event;
- inspect before/after pressure.

Do not rely on waiting for RNG to test a drought.

---

## Stage 3 acceptance gate

Prove:

1. Event survives save/load.
2. Event starts and ends cleanly.
3. Event has a geographically/faction-appropriate scope.
4. Relevant effective demand/supply changes in the correct direction.
5. Existing Stage 2 propagation carries part of the shock naturally.
6. Event end does not snap all conditions instantly to baseline if underlying pressure remains.
7. Accepted obligations do not mutate.
8. Event explanation appears in relevant market/pricing context.
9. Event frequency does not flood normal play.
10. At least one event produces an obvious player decision in ordinary play.

---

# 11. Stage 4 — Brand strength & colony specialization

**Player-facing goal:** Better craftsmanship becomes economically valuable, and repeatedly producing high-quality goods creates an earned specialization.

Brand here means **earned product-quality reputation**, not logos, advertising campaigns, or marketing spend.

---

## 4.1 Brand is global to the player's colony, product-specific, and bounded

Base scale:

```text
-100 = infamously poor quality
   0 = unknown / neutral
+100 = exceptional expected quality
```

New colony starts with no product-specific evidence; effective brand is neutral unless inherited from similar known products.

Brand does not belong to an individual customer.

It is the market's expectation of the player's output.

---

## 4.2 Store direct brand evidence by exact product

Recommended concept:

```csharp
ProductBrandRecord : IExposable
{
    ThingDef thingDef;
    float directScore;       // -100..100
    float evidenceWeight;    // confidence/exposure
    int unitsDelivered;
}
```

Keep the stored record about **direct experience with that product**.

Do not persist a record for every possible target product merely because another product can transfer reputation to it.

Transferred reputation should be derived on read.

---

## 4.3 Actual delivered quality updates brand

Use the real goods consumed/delivered by the sale path.

Do not update brand from:

- minimum-quality requirement;
- advertised quality;
- a UI selection that was not actually delivered.

For a mixed batch, calculate an actual batch quality result before the goods disappear from the fulfillment path.

Suggested quality target mapping for initial tuning:

```text
Awful       -100
Poor         -60
Normal         0
Good          35
Excellent     65
Masterwork    90
Legendary    100
```

The exact balance can move, but preserve:

- Normal ≈ neutral.
- Good meaningfully positive.
- Masterwork/Legendary strongly positive.
- Poor/Awful negative.

Update should move existing brand **toward** the delivered batch's target, weighted by meaningful volume/value with diminishing returns.

One small Masterwork sale should not instantly create +90 brand.

A large flood of Normal goods should be capable of diluting an established premium brand.

Splitting one shipment into 20 orders must not create 20x the brand movement.

---

## 4.4 Similar-product carryover

This is the core specialization mechanic.

A target product receives inherited brand from similar products.

The similarity service must be:

- deterministic;
- centralized;
- def-driven;
- bounded;
- understandable in debug output.

Target carryover tiers:

```text
Same exact product                       100%

Very close product / narrow family       ~93–97%
Example: revolver -> bolt-action rifle

Related product in same industry         ~60–90%
Example: one firearm family -> another weapon family

Same broad Intercolony category only     ~20–50%

Very unrelated products                  ~3–7%
Example: chair -> bolt-action rifle
```

Do not use a hard 0% floor for unrelated known-quality manufacturing unless implementation evidence shows the tiny floor creates an exploit. The intended design is that generalized craftsmanship prestige can carry a **very small** amount, while specialization carries nearly all of the useful benefit.

### Similarity evidence

Prefer, in order:

1. exact `ThingDef`;
2. shared narrow RimWorld `ThingCategoryDef` / meaningful product metadata;
3. weapon/apparel/building metadata or tags already present in defs;
4. existing `IntercolonyProductCategory`;
5. unrelated floor.

Do not infer similarity from display-name string matching.

Add explicit Core-item regression examples:

- revolver ↔ bolt-action rifle = very high;
- revolver ↔ another ranged firearm = high;
- chair ↔ table = meaningfully related;
- chair ↔ rifle = near-floor;
- apparel ↔ furniture = low;
- modded defs without expected metadata = safe fallback, never crash.

---

## 4.5 Do not let inherited brands stack infinitely

If the colony has +70 revolvers, +65 rifles and +60 pistols, a new firearm must not receive "+195 brand."

Recommended rule:

- derive the strongest or confidence-weighted inherited signal;
- blend direct evidence toward inherited reputation only while direct evidence is weak;
- as direct product evidence accumulates, direct brand increasingly dominates.

Conceptually:

```text
effective brand =
    lerp(best inherited brand,
         direct product brand,
         direct evidence confidence)
```

This lets a renowned gunsmith launch a new rifle with strong inherited expectations without preventing that rifle from earning its own reputation.

Negative evidence must work symmetrically enough that a terrible directly proven product cannot hide forever behind a positive related brand.

---

## 4.6 Immediate quality value vs future brand value

Keep two mechanics distinct.

### Actual item quality

When the buyer is purchasing **known current inventory** (especially Find Buyer/direct selling), actual quality should affect the immediate valuation where the path can honestly know what is being sold.

A Masterwork item should not be intrinsically priced like an Awful item.

Use RimWorld's existing quality/value semantics where possible rather than inventing a parallel quality price table.

### Brand strength

Brand changes what buyers are willing to offer **before the next product is inspected**, because they expect a certain level of quality from the colony.

This is a prospective premium/discount.

For already binding Market orders:

- agreed payment remains fixed;
- delivering above the requirement does not silently rewrite the contract price;
- superior/poor delivered quality updates future brand.

This preserves contract trust while still rewarding quality through direct-sale valuation and future opportunities.

---

## 4.7 Brand pricing factor

Add brand through `IntercolonyPricing`, not UI/order arithmetic.

Keep the first version bounded.

A +100 brand should be economically exciting but not produce infinite-profit arbitrage.

A -100 brand should hurt price and buyer willingness without making the product literally unsellable everywhere.

Brand can affect:

- willingness to pay;
- Find Buyer interest;
- later negotiation tolerance.

It should **not** change commercial reliability reputation.

---

## 4.8 Brand milestones and history

When a product/family crosses meaningful bands, write a commercial timeline event.

Example thresholds:

```text
+25 Established
+50 Respected
+75 Renowned

-25 Questionable
-50 Poor reputation
-75 Notorious
```

The exact names are UI copy, not separate mechanics.

Do not create events for every +1 score.

---

## 4.9 Brand UI

Do not display a 300-row spreadsheet of every ThingDef.

Preferred user-facing approach:

```text
Known for
  Firearms      Renowned
  Furniture     Respected

Weak reputation
  Apparel       Questionable
```

When viewing/selling a specific good, show:

```text
Relevant brand strength: +68
Mostly inherited from your firearms reputation.
```

The detailed similarity trace belongs in tooltip/debug output.

---

## Stage 4 likely code areas

Inspect/change at minimum:

- new Brand record/service
- `IntercolonyWorldComponent`
- `SalesOrderService` fulfillment paths where real delivered quality is available
- `FindBuyerService`
- `IntercolonyPricing`
- relevant sales dialogs/tooltips
- Relations/Business surface only if it is clearly the least invasive brand summary location
- self-tests for quality capture, score update, similarity and pricing

---

## Stage 4 migration

Existing save begins with neutral direct brand unless trustworthy completed-order data includes actual delivered quality.

Do not fabricate old brand from `minQuality`.

If retained completed sales do not prove actual delivered quality, brand begins neutral and says nothing retroactively.

---

## Stage 4 acceptance gate

Prove:

1. Brand score is bounded -100..100.
2. Normal-quality sales keep a neutral brand broadly neutral.
3. Repeated Excellent/Masterwork sales build positive brand gradually.
4. Repeated Poor/Awful sales build negative brand.
5. Shipment splitting does not multiply brand gains.
6. Revolver reputation transfers strongly to a bolt-action rifle.
7. Chair reputation barely transfers to a rifle.
8. Direct evidence eventually dominates inherited reputation.
9. Brand changes future pricing/interest.
10. Commercial reliability reputation is unchanged by craftsmanship alone.
11. Direct sale of known Masterwork inventory is worth more than equivalent Awful inventory where the path can know actual quality.
12. Binding order payment does not silently change at delivery.
13. Save/load preserves direct brand evidence.

---

# 12. Stage 5 — Commercial relationships & limited negotiation

**Player-facing goal:** Repeated business creates meaningful trust, and important deals can be negotiated without turning the mod into a bargaining simulator.

---

## 5.1 Deepen the existing commercial relationship; do not add another generic relationship meter

Build on `CommercialReputation` and its existing per-settlement tiers.

Do not create:

- Relationship XP;
- Friendship Level;
- Loyalty Score;

on top of commercial reputation unless a fact cannot be represented by existing history/reputation.

The current reputation answers:

> "Will this counterparty trust us to keep our commitments?"

That is the relationship spine.

Brand contributes product-quality leverage separately.

---

## 5.2 Make relationship tiers qualitatively matter

At minimum, relationship/reputation should influence:

- willingness to accept a counteroffer;
- willingness to grant an extension;
- tolerance for quantity reduction;
- probability/quality of recurring contract proposals or acceptance;
- possibly whether a particularly favorable negotiated term is considered.

Avoid simply adding another flat price multiplier to every tier if existing reputation already affects price.

Prefer new behavior over double-counting the same benefit.

---

## 5.3 Pre-acceptance negotiation

Provide a constrained **Counteroffer** action on appropriate sales opportunities/contracts.

First version may allow changing a subset of:

- price;
- quantity;
- deadline;
- fulfillment mode where the original opportunity supports both modes.

Do not allow arbitrary text negotiation.

Do not create an infinite back-and-forth loop.

Recommended flow:

```text
Original offer
   ↓
Player accepts / declines / counters
   ↓
Counterparty evaluates
   ↓
Accepted
OR
One final counter
OR
Rejected
```

One counter round plus at most one counterparty response is enough for 1.0.

---

## 5.4 Negotiation evaluation

Centralize negotiation economics.

Inputs may include:

- current market conditions;
- settlement baseline identity/wealth;
- event urgency;
- commercial reputation;
- relevant brand strength;
- quantity change;
- price change;
- deadline change;
- logistics/fulfillment change.

The evaluator returns a reasoned outcome.

Do not scatter "if reputation > 70 then +10%" across dialogs.

Debug output should be able to print the acceptance calculation.

---

## 5.5 Future-proof negotiation for Stage 6 without overgeneralizing

Stage 6 will need procurement-contract proposals.

Therefore the Stage 5 evaluator may have a narrow direction/context concept such as:

```text
Sale
Purchase / Procurement
```

Do not build procurement UI now.

Do not create a generic negotiation framework for unrelated future domains.

Two known directions are enough to justify a shared evaluator.

---

## 5.6 Post-acceptance renegotiation

For a binding **sales** obligation, allow the player to request only changes that make sense after agreement.

Initial allowed requests:

- deadline extension;
- quantity reduction;
- mutual cancellation.

Do **not** let the player increase their own payment after accepting because the market moved.

Do not let a request mutate the order before acceptance.

Flow:

```text
Request change
   ↓
Counterparty evaluates relationship + size of concession + current circumstances
   ↓
Accepted: mutate persisted terms explicitly + timeline entry
Rejected: order remains exactly unchanged
```

A rejected renegotiation should not itself count as a default.

Repeated abusive requests may have a small reputation consequence only if deliberately designed and tested; do not invent this as an incidental punishment.

---

## 5.7 Make event circumstances matter

Stage 3 now gives a reason a counterparty might behave differently.

Examples:

- a settlement under epidemic pressure is less willing to reduce a medicine order;
- a drought-struck buyer may pay more for food;
- a trusted partner may grant a deadline extension despite tight conditions.

Do not add bespoke negotiation code inside each event.

Events should expose economic urgency that the evaluator can read.

---

## 5.8 Timeline recording

Record:

- counteroffer accepted;
- meaningful counteroffer rejected if worth retaining;
- deadline extension granted;
- quantity reduction granted;
- mutual cancellation;
- major relationship-tier milestone.

Do not record every button click.

---

## Stage 5 likely code areas

Inspect/change:

- `Reputation/CommercialReputation.cs`
- `Reputation/ReputationService.cs`
- new narrow negotiation evaluator/terms model
- `Market/MarketOpportunity.cs`
- sales acceptance dialogs
- `Orders/SalesOrder*`
- `Contracts/ContractService.cs`
- timeline service/records
- relevant self-tests

---

## Stage 5 acceptance gate

Prove:

1. Counteroffer terms are evaluated centrally.
2. Higher commercial trust improves reasonable counteroffers without guaranteeing absurd ones.
3. Strong relevant brand improves price leverage.
4. Unrelated brand gives almost no leverage.
5. Market/event urgency influences willingness rationally.
6. Rejected counteroffer leaves original offer unchanged.
7. Accepted counteroffer freezes final terms.
8. Binding-order renegotiation supports deadline extension, quantity reduction and mutual cancellation.
9. Rejected renegotiation leaves the binding order unchanged.
10. Save/load preserves explicitly renegotiated obligations.
11. UI always shows final agreed terms before commitment.
12. No infinite negotiation loop exists.

---

# 13. Stage 6 — Procurement parity

**Player-facing goal:** Buying becomes as complete a commercial system as selling.

Final Procurement structure should approximately mirror the conceptual depth of Selling:

```text
SELLING                         PROCUREMENT

Market                          Supplier Market
Find Buyer                      Request Quotations
Orders                          Purchase Orders
Contracts                       Procurement Contracts
```

The two directions should feel related but do not need identical mechanics.

---

## 6.1 Build the supplier market against the Stage 2 economy

Do **not** copy today's Market opportunity generator and change "demand" to "supply."

Create a procurement-side listing model whose authoritative inputs are:

- stable supplier profile;
- effective current supply;
- active event effects;
- tech capability;
- finite offer availability;
- distance/logistics;
- current pricing model.

Conceptual model:

```csharp
SupplierListing : IExposable
{
    id;
    settlementId;
    thingDef;
    stuffDef;
    quality/specification;
    quantityAvailable;
    unitPrice;
    fulfillment;
    leadTime;
    createdTick;
    expiryTick;
    refreshWindow;
}
```

Exact fields should reuse existing `Quotation`/`PurchaseOrder` semantics where clean.

Do not make `Quotation` pretend it was always a public market listing if that creates confusing lifecycle semantics.

---

## 6.2 Unify finite supplier availability across Supplier Market and RFQs

This is important.

If one settlement can currently supply 100 components in the market window:

- buying 80 through Supplier Market should leave roughly 20 available for RFQs;
- buying 80 through an RFQ should reduce or remove the public listing.

Reuse/evolve the existing `SupplierOfferConsumption` concept rather than creating independent stock pools.

There should be one answer to:

> "How much of this supplier's current offer capacity has the player already consumed?"

Pressure is not that answer; finite offer consumption is.

---

## 6.3 Supplier Market UI

Create a browse surface that answers:

- what is available;
- from whom;
- how much;
- quality/material/specification;
- price;
- delivery/pickup;
- lead time;
- relevant shortage/surplus reason.

Use the established row/tooltip design language from the 0.9.3 UI cleanup.

Do not write paragraph-heavy confirmation dialogs.

---

## 6.4 Accepting a supplier listing creates a normal `PurchaseOrder`

Reuse `PurchaseOrderService`.

Do not create a second fulfillment engine for market purchases.

Supplier listing is an **origin**, not a separate purchase lifecycle.

Purchase orders from RFQs and Supplier Market should use the same:

- payment;
- delivery;
- pickup;
- refund;
- cancellation;
- failure;
- history.

---

## 6.5 Dedicated Purchase Orders surface

Purchase orders should no longer be buried as a side-effect inside the RFQ/request surface.

Create a dedicated Procurement `Orders` view comparable in clarity to Sales Orders.

Minimum columns/info:

- order ID;
- supplier;
- item;
- quantity;
- total price;
- status;
- fulfillment;
- ETA/pickup deadline;
- action where applicable.

Do not duplicate request history in the purchase-order list.

RFQ/request and resulting purchase order are distinct entities.

---

## 6.6 Recurring procurement contracts

The player proposes a standing purchasing agreement to a supplier.

Concept:

```text
"We want 200 components every 15 days
for the next year at X silver per unit.
Supplier delivers / we collect."
```

Supplier evaluates the proposed terms using:

- its stable economic profile;
- current effective supply;
- current market price;
- quantity relative to supply;
- relationship/reputation;
- relevant current events;
- logistics;
- negotiation terms.

---

## 6.7 Payment policy for procurement contracts is locked

Do **not** prepay an entire multi-cycle procurement contract.

Do **not** add debt/credit.

Each contract cycle creates/commits a purchase order and uses the **existing purchase payment policy for that cycle**.

If current purchase orders pay at acceptance, each cycle pays at its own acceptance/creation point.

This preserves simple money semantics.

If a supplier defaults under the existing purchase-order failure model, the relevant cycle follows the existing refund policy.

Do not design banking merely because recurring purchases exist.

---

## 6.8 Procurement contract data model

Audit `RecurringContract` before choosing implementation.

Default recommendation:

- keep existing sales `RecurringContract` stable;
- create a dedicated procurement recurring-contract record if the existing class is deeply sale-direction-specific;
- extract only narrow shared lifecycle helpers if two concrete implementations genuinely duplicate scheduling/term logic.

Do **not** convert the existing persisted sales contract into a giant bidirectional model purely for theoretical elegance.

However, if inspection proves the existing record is already direction-neutral except for a few fields and a safe additive direction field substantially reduces code duplication without risky migration, that is an authorized **medium-confidence A/B decision**:

**A. Dedicated procurement contract model** — default/safest.  
**B. Additive bidirectional contract model** — choose only if current code clearly supports it with a narrow migration.

Log the evidence and choice. Do not ask unless the choice changes existing sales-contract behavior or migration safety is uncertain.

---

## 6.9 Supplier failure under recurring contracts

Do not add a Supplier Reliability personality/stat system.

A recurring supplier may fail a cycle when actual current supply conditions make the promise impossible or when existing purchase-order failure logic reaches that state.

Keep failure:

- uncommon under ordinary conditions;
- more plausible under severe shortage/events;
- visible and explained;
- subject to existing refund rules.

A standing agreement should reduce procurement uncertainty, not eliminate the existence of economic shocks.

---

## 6.10 Procurement negotiation

Reuse Stage 5 negotiation semantics.

For proposed procurement contracts the player can propose:

- price;
- quantity;
- cycle interval/term;
- fulfillment.

The supplier evaluates once, may accept/counter/reject, then terms freeze.

Do not create a second procurement-specific bargaining engine.

---

## 6.11 Market interaction

After Stage 6, all four major procurement channels must consume the same underlying economics:

```text
Supplier Market
RFQ responses
Purchase-order failure/availability where appropriate
Recurring procurement contract evaluation/cycles
```

This is the payoff for doing Stage 2 first.

---

## 6.12 Timeline integration

Record:

- Supplier Market purchase completed;
- procurement contract started;
- cycle completed;
- supplier failed/defaulted;
- procurement contract ended/cancelled;
- meaningful negotiated agreement.

---

## Stage 6 likely code areas

At minimum inspect/change:

- `Procurement/RfqService.cs`
- `Procurement/PurchaseRequest.cs`
- `Procurement/PurchaseOrder.cs`
- `Procurement/PurchaseOrderService.cs`
- new Supplier Market listing/generator
- current `SupplierOfferConsumption` in `IntercolonyWorldComponent`
- Procurement UI
- `Contracts/RecurringContract.cs`
- `Contracts/ContractService.cs`
- Stage 5 negotiation service
- `IntercolonyPricing`
- RFQ/purchase/contract self-tests

---

## Stage 6 acceptance gate

Prove:

1. Supplier Market listings are driven by effective supply, not an independent random economy.
2. Supplier Market and RFQs consume one finite supplier availability pool.
3. Purchasing a listing creates a normal PurchaseOrder.
4. Supplier delivery and player pickup still work.
5. Dedicated Purchase Orders screen accurately reflects RFQ and Supplier Market purchases.
6. Player can propose a recurring procurement contract.
7. Supplier can accept/counter/reject using Stage 5 negotiation.
8. Each procurement-contract cycle uses ordinary per-cycle payment.
9. Supplier failure/refund remains explicit and does not create/destroy silver.
10. Market shocks change supplier listings and future contract cycles but never silently rewrite paid/accepted PurchaseOrders.
11. Save/load works with active supplier listing, RFQ, purchase order, sales recurring contract and procurement recurring contract simultaneously.
12. Selling behavior is not regressed by shared economic/negotiation changes.

This stage requires substantial ordinary play before moving to Stage 7.

---

# 14. Stage 7 — Commercial history

**Player-facing goal:** The player can understand what their colony's economic relationship with a settlement has actually been over time.

Most recording should already exist because Stage 0 established the timeline and every later stage wrote to it.

Stage 7 is primarily the **read model, aggregation and UI** stage.

---

## 7.1 Commercial history is per settlement

The natural entry point is the relationship with a settlement.

Minimum overview:

```text
Lanca
Commercial standing: Reliable supplier
Trading since: 5503
Completed sales: 17
Completed purchases: 8
Active contracts: 2
Total known trade value: 48,200 silver
```

Only show figures the retained data can support honestly.

---

## 7.2 Timeline

Show recent meaningful events chronologically.

Example:

```text
5505 Apr 12 — Delivered 400 cloth, 1,120 silver
5505 Apr 03 — Deadline extension granted for Order #81
5505 Mar 29 — Firearms brand reached Renowned
5505 Mar 14 — Procurement agreement started: 80 components / 15 days
5505 Feb 18 — Purchase completed: 120 medicine
5505 Jan 09 — Failed furniture order
```

Do not show every market refresh or tiny score adjustment.

---

## 7.3 Summary and timeline are separate

Use compact existing/expanded aggregates for long-term totals.

Use the bounded timeline for narrative recency.

If old saves lack detailed history:

- keep aggregate counts;
- start detailed timeline from the migration/version where it became reliable;
- state this gracefully rather than fabricating events.

---

## 7.4 Brand context

Commercial history may show:

- which product families the colony is known for;
- brand milestones;
- recent quality-driven brand changes.

Do not turn Settlement History into the main brand-management screen.

Brand is global to the player; the settlement history merely shows relevant commercial interactions.

---

## 7.5 Economic-event context

If a commercial transaction was materially associated with an active economic event, the timeline may display that context:

```text
Delivered emergency medicine during regional epidemic
```

Do not copy every economy event into every settlement's history when no trade occurred.

---

## 7.6 UI placement

Default recommendation: deepen the existing Relations/settlement relationship view.

Avoid another top-level Intercolony tab solely for History unless current UI architecture makes the Relations view unworkable.

If current Relations UI has no clean detail surface, choose:

**A.** expandable settlement detail inside Relations, or  
**B.** a modal/detail window opened from the settlement row.

Choose the cleaner existing UI pattern and log it.

Do not ask solely about modal vs detail pane.

---

## 7.7 Retention and pruning

Before shipping:

- profile save size with a populated timeline;
- confirm pruning preserves newest records;
- confirm aggregates remain correct after detailed events are pruned;
- no history pruning may affect contract eligibility, brand score, reputation, or active obligations.

History is a read feature; deleting old display records must not delete authoritative economic state.

---

## Stage 7 acceptance gate

Prove:

1. Settlement history reads sales and purchases.
2. Contract events appear.
3. Negotiation/renegotiation events appear.
4. Brand milestones appear.
5. Old detailed events prune safely.
6. Aggregates remain after timeline pruning.
7. An upgraded old save does not invent fake dates.
8. Player can distinguish commercial reliability from product brand.
9. A player can answer "what has happened between me and this settlement?" without opening three other tabs.

---

# 15. Stage 8 — 1.0 integration, balance and release gate

Stage 8 adds **no major new feature**.

If a new system idea appears here, put it in `docs/BACKLOG.md` unless it closes a demonstrated hole in the approved 1.0 features.

---

## 8.1 Full save/load matrix

Exercise a current-schema save containing simultaneously:

- active market opportunities;
- active economic event;
- non-neutral market pressure;
- positive and negative brand records;
- negotiated sales order;
- active RFQ;
- Supplier Market listing;
- active PurchaseOrder;
- recurring sales contract;
- recurring procurement contract;
- hired workers/payroll state;
- commercial history timeline.

Save, reload, advance, complete several objects, save again.

---

## 8.2 Migration matrix

At minimum test:

- 0.9.3/schema-42 save -> current;
- one intermediate 1.0-development schema if a migration chain exists;
- fresh world at current schema.

No active old obligation may disappear or have price/quantity silently changed.

---

## 8.3 Economic sanity playtest

Play several market refreshes across multiple settlement archetypes and answer:

- Can I tell agricultural from industrial/affluent economies?
- Do shortages/surpluses persist enough to plan around?
- Do they eventually normalize?
- Are events noticeable without dominating every decision?
- Does one region sometimes differ meaningfully from another?
- Does Procurement reflect the same market conditions as Selling?
- Are there obvious arbitrage loops?
- Are ordinary goods still buyable/sellable often enough to be useful?
- Does scarcity create choices rather than merely empty screens?

Record decisions, not just generated rows.

---

## 8.4 Brand sanity playtest

Verify in normal play:

- high-skill production can build a valuable brand;
- mediocre output can dilute it;
- pivoting to an unrelated industry does not penalize the new industry beyond the tiny carryover floor;
- moving from revolvers to rifles meaningfully carries reputation;
- brand premium is useful but not an infinite money printer;
- a player can understand why their brand changed.

---

## 8.5 Negotiation sanity playtest

Verify:

- negotiation is optional, not required for every trade;
- outcomes feel connected to terms and relationship;
- absurd demands get rejected even at high reputation;
- strong brand helps the relevant product;
- events matter;
- renegotiation is useful when a real obligation becomes difficult;
- failed negotiation does not destroy the original opportunity/order.

---

## 8.6 Procurement parity sanity playtest

Verify complete loops:

```text
Supplier Market -> PurchaseOrder -> delivery
Supplier Market -> PurchaseOrder -> pickup
RFQ -> quote -> PurchaseOrder -> delivery
RFQ -> quote -> PurchaseOrder -> pickup
Procurement contract -> cycle -> PurchaseOrder -> completion
Procurement contract -> supplier failure -> refund/outcome
```

---

## 8.7 UX pass

Check:

- 1.0x, 1.25x, 1.5x, 1.75x UI scale where practical;
- no paragraph-heavy dialog regressions;
- pricing factors remain legible;
- event cause is visible;
- brand is understandable;
- negotiation final terms are explicit;
- Procurement tabs do not feel like a different product from Selling;
- Commercial History is readable when dense.

Use measured text/layout rules already established in the project.

---

## 8.8 Performance

Profile:

- coarse economy refresh;
- pressure propagation;
- regional diffusion;
- event application;
- Supplier Market generation;
- Brand effective-score lookup;
- history rendering with a populated timeline.

No new economic system belongs in a per-frame or per-tick hot path without evidence that it is cheap.

Cache derived product similarity where profiling justifies it, but do not make a global static cache authoritative.

---

## 8.9 Documentation

Update:

- `README.md`
- `DESIGN.md`
- `PROGRESS.md`
- `docs/ROAD_TO_1_0.md`
- `docs/BACKLOG.md`
- `docs/PENDING_PLAYTESTS.md`
- compatibility notes if affected
- Workshop description
- 1.0 release notes

The old "shortest path to 1.0 is evidence only" conclusion is superseded by this expanded 1.0 product scope.

Do not erase the historical audit; mark it as the earlier 0.9.x criterion set and document the expanded 1.0 target.

---

# 16. Development slicing rule

Do not implement an entire numbered stage in one giant change.

Each slice must make one behavioral claim true.

Example Stage 2 slices:

```text
2A — Persist neutral per-settlement market pressure.
2B — Advance/mean-revert pressure deterministically.
2C — Replace exact-good cycle noise with stable baseline affinity.
2D — Selling opportunity selection reads effective demand.
2E — Pricing reads effective market pressure.
2F — RFQs read effective supply.
2G — Completed sales/purchases nudge pressure.
2H — Add coarse chain propagation.
2I — Add bounded regional diffusion.
2J — Add explanations/debug output.
2K — Migration and play gate.
```

Example Stage 4 slices:

```text
4A — Capture actual delivered quality before goods are consumed.
4B — Persist direct product brand evidence.
4C — Update direct score from delivered quality and volume.
4D — Implement deterministic product similarity.
4E — Derive effective brand with direct-evidence confidence.
4F — Apply brand to future sale pricing/interest.
4G — Apply actual quality to direct known-inventory sale value.
4H — UI/tooltips and milestone history.
4I — Migration and play gate.
```

Example Stage 6 slices:

```text
6A — Supplier listing model.
6B — Generate listings from effective supply.
6C — Share finite supplier availability with RFQs.
6D — Accept listing into PurchaseOrderService.
6E — Supplier Market UI.
6F — Dedicated Purchase Orders UI.
6G — Procurement contract model.
6H — Supplier contract evaluation using negotiation.
6I — Contract cycle creates ordinary PurchaseOrder.
6J — Failure/refund path.
6K — Save/load and integrated play gate.
```

Commit coherent slices independently.

---

# 17. Proceed / decide / ask rails

The goal is to reduce unnecessary back-and-forth without letting an implementation agent silently redesign the game.

---

## 17.1 HIGH confidence — decide and continue

Do not ask Matteo when:

- choice is local and reversible;
- behavior is already specified here;
- existing authoritative owner is clear;
- RimWorld API can be verified locally;
- choice does not alter save semantics;
- choice does not materially change player strategy.

Examples:

- helper/class naming;
- private data structure details;
- exact debug output format;
- tooltip wording;
- whether a computed read model is a struct or small class;
- 0.15 vs 0.16 internal diffusion coefficient after tests show both meet the behavioral bounds.

---

## 17.2 MEDIUM confidence — choose the smaller option, log it, continue

Use this when:

- two implementations produce the same player-facing behavior;
- both are easy to reverse;
- there is no irreversible migration difference;
- no new player strategy is introduced.

Log:

```text
### YYYY-MM-DD — Stage/Slice — DECISION
Question:
Evidence:
Choice:
Why it preserves this plan:
Revisit if:
```

Examples:

- Relations detail pane vs modal for economic profile/history;
- exact timeline retention cap after profiling;
- dedicated procurement recurring-contract record vs additive direction field **only if** both preserve existing sales behavior safely;
- exact product-similarity metadata precedence where two generic RimWorld tags both work.

---

## 17.3 LOW confidence / structural — raise a hand

Ask Matteo only when:

1. the requested behavior appears technically impossible or fundamentally different under verified RimWorld APIs;
2. two viable options produce meaningfully different player strategies;
3. an unexpected migration would reinterpret or destroy existing obligations/value;
4. the solution requires moving authoritative ownership away from the established owner;
5. a new Harmony patch is required for a core 1.0 behavior where no existing supported path can implement it;
6. save corruption/value loss is discovered and the fix is structural rather than narrow;
7. the only apparent solution requires reintroducing a feature explicitly cut from 1.0;
8. a stage acceptance criterion cannot be met without changing the player-facing design.

Do not ask:

> "How do you want me to implement this?"

Ask:

```text
I found X.
The current code/API proves Y.

Option A:
- behavior
- cost/risk

Option B:
- behavior
- cost/risk

Recommendation: A, because ...

Blocked slice: 5C.
Independent work I can continue meanwhile: 5D/5E.
```

Maximum three options.

---

# 18. Do-not-get-stuck rule

For an uncertain defect/behavior:

1. Trace the production path.
2. Force the condition through self-test/debug state.
3. Instrument the decision boundary narrowly.
4. If still not reproduced, document `NOT REPRODUCED`, prove the intended behavior if possible, and continue.

Never write a speculative fix merely because the roadmap expected a problem.

For balance uncertainty:

1. establish directionality and bounds in tests;
2. use conservative initial values;
3. expose debug summaries;
4. continue implementation;
5. tune during the stage's play gate.

Do not block architecture waiting for the perfect coefficient.

---

# 19. Adjacent issue rule

During a 1.0 slice:

## RED — fix or stop

- crash;
- save corruption;
- silent item/silver/pawn loss;
- duplicate value;
- active obligation disappears;
- current slice's invariant is false.

Narrow obvious fix: repair + regression test.  
Structural fix: log + raise hand.

## YELLOW — backlog

- unrelated UX defect;
- balance idea outside current stage;
- compatibility concern;
- new feature idea;
- non-blocking old bug.

Add to `docs/BACKLOG.md` with why it was not included.

## GRAY — ignore

- naming preference;
- theoretical refactor;
- generic abstraction opportunity;
- "this could be cleaner."

Do not derail the 1.0 program.

---

# 20. Testing rules

## 20.1 A passing test may not hide skips

The repository already learned this lesson.

A skipped assertion is not proof.

Every stage gate must report skips explicitly.

## 20.2 Test production owners

Do not reproduce market formulas inside tests and call that proof.

Tests should call:

- the real economy service;
- the real pricing owner;
- the real order transition;
- the real RFQ builder;
- the real negotiation evaluator;
- the real contract-cycle path.

## 20.3 Debug determinism

Where seeded randomness remains, tests must control the seed or assert ranges/directions rather than exact incidental rolls.

## 20.4 Play evidence

Self-tests do not prove:

- UI readability;
- physical caravan interaction;
- whether a market feels noisy/flat;
- whether negotiation is annoying;
- whether events are memorable;
- whether brand progression feels worthwhile.

Record these in `docs/PENDING_PLAYTESTS.md` and continue only where the next stage does not depend on subjective evidence being resolved.

Stage 2 market feel and Stage 6 Procurement parity **do** deserve meaningful play gates because later stages build directly on them.

---

# 21. Schema / migration rules

For every save-schema change:

1. State why the new fact must persist.
2. Add the field/entity.
3. Increment schema.
4. Add one explicit migration step from previous schema.
5. Initialize new state conservatively.
6. Load a real prior save.
7. Verify active objects survive.
8. Run all relevant self-tests.
9. Update `PROGRESS.md`.
10. Add unresolved real-play migration verification to `docs/PENDING_PLAYTESTS.md`.

Safe initialization defaults for this program:

- market demand pressure = `1.0`;
- market supply pressure = `1.0`;
- no active economic event;
- brand evidence absent/neutral;
- no fabricated negotiation history;
- timeline begins when trustworthy recording begins.

---

# 22. Price integrity rules

These apply across every stage.

1. `IntercolonyPricing` remains authoritative for forming transaction prices.
2. Store the final agreed price on the binding object.
3. Never re-price an existing obligation merely because current market pressure changed.
4. Negotiation creates a new agreed term **before** acceptance or through an explicit accepted renegotiation.
5. Brand is one named price input, not a replacement for item value.
6. Actual quality value and brand premium are separate.
7. Event pressure and ordinary market pressure must not be double-counted.
8. Buying and selling difficulty factors continue to squeeze the player symmetrically enough to avoid trivial arbitrage.
9. Any new price factor must appear in explanation/debug output.

---

# 23. Source-of-truth map after 1.0

The intended authority map is:

| Question | Authority |
|---|---|
| What is this settlement normally like economically? | `SettlementEconomicProfile` |
| What is its economy experiencing right now? | persisted settlement market state + economy service |
| What temporary shock is affecting it? | persisted active economic events |
| What price should a new deal form at? | `IntercolonyPricing` using effective economy context |
| What price/quantity/deadline did we actually agree? | persisted order/contract terms |
| Will this settlement trust us to keep promises? | `CommercialReputation` / reputation service |
| What quality does the market expect from our product? | Brand service / product brand records |
| How similar is one product's brand to another? | single product-similarity service |
| What has the player promised to sell? | `SalesOrder` / sales service |
| What has the player promised to buy? | `PurchaseOrder` / purchase service |
| What can a supplier currently offer? | effective supply + shared finite supplier-offer consumption |
| What happened historically? | compact aggregates + bounded commercial timeline |
| What physically exists in colony/caravan inventory? | RimWorld's actual map/caravan state |

If an implementation introduces a second authoritative answer to one of these questions, reconsider it.

---

# 24. 1.0 completion checklist

Intercolony is not 1.0 merely because every class compiles.

The program is complete when:

## Settlement economies
- [ ] Stable identities are deterministic and legible.
- [ ] Archetypes materially influence normal supply/demand.

## Market fundamentals
- [ ] Current market pressure persists.
- [ ] Pressure mean-reverts.
- [ ] Related markets propagate bounded pressure.
- [ ] Nearby settlements influence one another modestly.
- [ ] Selling and RFQs consume the same economy.
- [ ] Old noisy cycle demand is no longer the main driver.

## Circumstance events
- [ ] Multiple event types exist.
- [ ] Events have clear causes/effects.
- [ ] Events create temporary regional opportunities/shortages.
- [ ] Events do not mutate binding deals.

## Brand
- [ ] Actual delivered quality changes brand.
- [ ] Brand is -100..100.
- [ ] Similar products inherit strongly.
- [ ] Unrelated products inherit minimally.
- [ ] Brand affects future selling/negotiation.
- [ ] Immediate known-inventory quality has value where appropriate.

## Relationships / negotiation
- [ ] Commercial reputation remains distinct from brand.
- [ ] Counteroffers exist and are bounded.
- [ ] Renegotiation exists for deadline/quantity/cancellation.
- [ ] Market conditions, relationship and brand affect outcomes.
- [ ] Final agreed terms are explicit and stable.

## Procurement parity
- [ ] Supplier Market exists.
- [ ] RFQs remain useful.
- [ ] Shared finite supplier availability exists.
- [ ] Purchase Orders have a dedicated view.
- [ ] Recurring procurement contracts exist.
- [ ] Per-cycle payment uses existing purchase semantics.
- [ ] Supplier failure/refund is safe and legible.

## Commercial history
- [ ] Sales/purchases/contracts/negotiations are readable per settlement.
- [ ] Brand milestones appear.
- [ ] Timeline is bounded.
- [ ] Aggregates survive timeline pruning.
- [ ] Old saves do not fabricate history.

## Integration
- [ ] Current-schema dense save/load passes.
- [ ] 0.9.3 migration passes.
- [ ] Full self-test suite is clean with skips visible.
- [ ] Real play confirms market is less noisy and more understandable.
- [ ] Real play confirms Procurement uses the same economy.
- [ ] Real play confirms brand/negotiation are worthwhile but not exploitable.
- [ ] UI remains readable at supported scales.
- [ ] Performance remains acceptable.
- [ ] README/Workshop/release docs describe actual 1.0 behavior.

---

# 25. The implementation philosophy in one page

When uncertain, preserve these statements:

> **Settlements have identities. Markets have state. Events create shocks.**

> **Randomness chooses among plausible outcomes; it does not substitute for economics.**

> **The economy is pressure-based, not a hidden NPC spreadsheet simulator.**

> **A binding deal stays binding unless both sides explicitly renegotiate it.**

> **Commercial reputation means reliability; brand means expected product quality.**

> **Brand is earned through actual delivered craftsmanship and follows similar products, not arbitrary skill-tree choices.**

> **Procurement is built after the market overhaul so selling and buying consume one economic truth.**

> **Commercial history is recorded as the work happens, then made readable at the end.**

> **Every stage adds a layer; later stages should not require rewriting the foundational meaning of earlier stages.**

> **If a coefficient is uncertain, bound it, test the direction, log it, and continue. Do not stop development for perfect balance.**

> **If a player-facing strategic choice is genuinely ambiguous, raise a hand with two concrete options and a recommendation.**

That is the 1.0 program.
