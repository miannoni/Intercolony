# Intercolony — Playtest Development Batch

## Purpose

This Plan captures product findings and desired outcomes from hands-on playtesting of the current `1.0.1` development line.

It is intentionally a **product/source Plan**, not an implementation prescription.

The Foreman Supervisor is expected to inspect the current repository and RimWorld behavior, convert these findings into traceable Plan Requirements and Slice Contracts, determine appropriate implementation boundaries and dependencies, and establish verification evidence.

Preserve the intent of every finding.

Where this Plan explicitly changes behavior that the existing repository documentation or code comments describe as intentional, **this Plan is the newer product decision for this batch**. Do not reject the requirement merely because the previous design chose something else.

Exact numerical balance parameters that are not explicitly declared product requirements may be chosen conservatively for an initial implementation and refined through later playtesting.

---

# Product principles for this batch

Several common principles run through these findings.

### Routine success should become quiet; exceptions should demand attention

Repeated commercial automation should not create notification spam. When something works exactly as configured, the player generally does not need a letter. When intervention is required, the player should understand why.

### Automation should reduce repetitive player work without hiding failures

Produce loops, recurring caravans, auto-ready agreements and similar systems should be genuinely “set and forget” when their conditions are satisfied.

They must not silently turn partial or impossible states into apparent success.

### Prefer existing RimWorld semantics when they already solve the problem

Storage filters, hauling destinations, caravan behavior, work restrictions, apparel policies and other vanilla concepts should be reused where practical instead of creating parallel systems with slightly different rules.

### Markets should behave like markets

The player should increasingly interact with supply, demand, scarcity, distance, reputation, quality and market offers rather than directly setting arbitrary outcomes.

A particularly strong application of this principle is F25: labor job postings are changing from player-set wages to market-priced offers.

### Internally produced resources still have economic value

Business profitability should distinguish cash expenditure from economic consumption.

Wood grown locally is not economically free if the colony would otherwise have to purchase that wood.

### Prefer measured reality over fake accounting precision

Where reliable event-driven observation can produce useful operational costing, prefer it.

Where doing so would require expensive polling, invasive instrumentation or unreliable inference, use a clearly understood approximation instead of presenting false precision.

### Settlement identity should matter

Distance, wealth, tech level, archetype, economic specialization and logistical capabilities should increasingly make settlements feel materially different from one another.

---

# A. Production control and automation

## F01 — Successful ready-order fulfillment should be silent

### Observed pain

Long-term agreements can repeatedly notify the player when an automatically readied order succeeds normally.

This creates noise without creating an actionable decision.

### Desired behavior

Routine successful ready-order fulfillment should not generate a player letter.

If fulfillment cannot proceed correctly, the player should receive an appropriate warning explaining the exception.

Examples include insufficient goods, invalid logistics or another condition that prevents the expected order from proceeding.

### Product intent

Notifications represent exceptions requiring attention, not routine business operations.

### Acceptance intent

- A normal successful automated order cycle completes without a letter.
- A deliberately unfulfillable order produces actionable feedback.
- Commercial history or other appropriate records may still record successful operations.

---

## F02 — Cancel on a Produce blueprint must actually stop the Produce loop

### Observed pain

When a blueprint belongs to an active Produce loop, vanilla Cancel can cancel the blueprint only for the Produce system to recreate it.

From the player's perspective Cancel did not cancel anything.

### Desired behavior

Cancelling the active blueprint associated with a Produce loop must also terminate that Produce program for that object.

The blueprint should not immediately respawn.

### Product intent

Vanilla-facing commands must retain the meaning the player expects.

### Acceptance intent

Cancelling the blueprint leaves no replacement blueprint and no active Produce loop for that production object.

---

## F03 — Area-level Produce / Pause / Stop Production orders

Produce should support colony-level operational control analogous to Architect orders so that many production loops can be managed together rather than one object at a time.

### Required semantics

**Produce / Resume**

Starts or resumes eligible Produce programs.

**Pause Production**

A production object already being constructed may finish construction.

Once installed, it remains installed and does not begin the next uninstall/rebuild cycle.

Resuming allows its program to continue.

**Stop Production**

Permanently ends the selected Produce programs.

If an object is already committed to its current uninstall step, that current uninstall may finish, but Stop must not generate a replacement blueprint or begin another cycle.

### Product intent

Pause is temporary operational suspension.

Stop is termination.

These meanings should remain predictable at both individual-object and area-control levels.

---

## F04 — Programmable Produce behavior

Right-clicking or otherwise configuring Produce should support a program concept similar in spirit to RimWorld bills.

The player should be able to configure production rather than only run an infinite uncontrolled loop.

### Desired capabilities

The system should support useful modes such as:

- produce indefinitely;
- produce until a quantity/target has been reached;
- appropriate quality or condition constraints where meaningful;
- worker eligibility / assignment controls;
- skill-related controls where relevant;
- more than one worker being able to participate when the underlying work allows it.

The objective is not to recreate every possible vanilla bill field whether useful or not.

The objective is to make Produce sufficiently programmable for contract-driven manufacturing and repeated colony production.

### Product intent

Produce should become a genuine production-control mechanism rather than merely an infinite uninstall/rebuild toggle.

---

# B. Logistics and order handling

## F05 — Receiving locations

The player should be able to designate one or more storage destinations as valid **receiving locations** for Intercolony deliveries.

### Desired behavior

A stockpile, shelf or other appropriate vanilla storage destination may be marked as accepting Intercolony deliveries.

Its existing storage filters remain authoritative.

When goods arrive, any receiving destination that accepts those goods and has capacity is eligible.

Prefer vanilla storage/hauling destination logic rather than inventing a parallel priority system unless the repository or vanilla API requires otherwise.

If eligible receiving capacity is exhausted, excess goods may use a sensible nearby fallback consistent with existing delivery behavior.

If no receiving location has been configured, preserve a safe/default form of the current delivery behavior.

### Product intent

A player should be able to build a warehouse and say “commercial deliveries go here” without separately configuring Intercolony item filters that duplicate RimWorld storage rules.

---

## F11 — Request Goods responses should arrive progressively

### Observed pain

A procurement request feels artificial if the entire world responds at once.

### Desired behavior

Supplier responses to a Request Goods / RFQ-style action should arrive over time.

Distance should strongly influence response latency.

Nearby settlements usually receive and answer the opportunity sooner, while distant settlements tend to respond later.

Distance should influence timing rather than create a perfectly deterministic nearest-to-farthest queue.

Other appropriate market characteristics may contribute.

### Product intent

The request should feel as though information is propagating through a world economy rather than querying a synchronous database.

---

## F12 — Preprogrammed and recurring player caravans for agreements

Long-term Selling and Procurement agreements should allow the player to preconfigure the caravan that will perform future delivery or pickup operations.

The objective is genuine **set-and-forget caravan logistics**.

### Configuration intent

An agreement should expose an appropriate configuration surface, such as its `[…]` menu, where the player can define the pawns and animals intended for:

- one upcoming order; or
- recurring future orders for that agreement.

The exact UI and internal representation are implementation decisions.

### Selling / delivery behavior

A programmed caravan must not leave with a knowingly partial order.

Example:

- agreement order requires 10 chairs;
- only 5 eligible chairs currently exist;
- the caravan remains waiting;
- the player is informed that only 5/10 are currently available;
- `Ready order` remains unavailable until the complete order can actually be fulfilled.

This should follow the same conceptual behavior used when orders are transported by other settlements: insufficient goods are surfaced rather than silently shipped partially.

### Interaction with Auto-ready

When the complete required stock and all other necessary dispatch conditions exist:

- with Auto-ready OFF, the order may wait for the player's Ready Order action;
- with Auto-ready ON, the configured recurring caravan may proceed automatically.

### Invalid programmed caravan

If configured pawns, animals or another necessary dispatch resource have become unavailable, do not silently improvise a materially different caravan.

Surface the problem as an actionable exception and keep the agreement/program in a comprehensible state.

### Product intent

Recurring logistics should remove repeated caravan setup work, not remove the player's control over whether contractual obligations are actually fulfilled.

---

## F21 — Logistics should become economically meaningful

The current logistics cost model is too shallow.

Distance and transportation difficulty should materially affect both cost and delivery time.

### Desired logistics model

A shipment should conceptually be influenced by:

- world distance;
- route/travel difficulty where available;
- cargo burden;
- expected caravan travel duration;
- food / provisions / animal or equivalent transport burden;
- settlement logistical capabilities;
- settlement technology, wealth and identity;
- transport method.

The exact formula is an engineering and balancing decision, but simple flat or weak distance-only costing is insufficient.

### Settlement logistics capability

Settlements should differ in what logistics they can reasonably perform.

For example, some sufficiently capable settlements may have access to fast transport such as transport pods, while others depend primarily on conventional caravans.

Capability should emerge sensibly from settlement identity such as:

- tech level;
- wealth tier;
- archetype;
- stable settlement-specific variation.

These should act as tendencies/capabilities rather than unnecessarily rigid archetype restrictions.

### Transport method selection

Having transport pods available does not mean every delivery should use transport pods.

Routine commerce should normally choose a method that makes economic sense.

A nearby bulk shipment may still use a caravan even when the supplier could theoretically use pods.

Fast transport should cost substantially more but offer substantially shorter delivery times.

### Player-facing information

Where meaningful, commercial offers/orders should expose logistics information such as:

- logistics cost;
- expected arrival time;
- transport method.

### Product intent

Distance should create geography.

Settlements should feel like actual economic places with differing logistical infrastructure instead of abstract endpoints connected to the player at approximately the same cost.

---

# C. Employees and employment UX

## F06 — Optional apparel policies for employees

Employees should optionally be controllable by the colony's apparel-policy system.

### Default behavior

The current conservative behavior remains the default: an employee does not automatically begin equipping arbitrary colony apparel merely because they are present.

### Optional behavior

The player may explicitly place an employee under an appropriate apparel policy / allow them to use the colony's apparel management.

Reuse vanilla apparel semantics where feasible.

### Product intent

Players who want tighter integration of long-term employees should have it, while employment must not automatically cause visitors to consume or rearrange colony gear.

---

## F09 — Employee happiness should modestly affect faction goodwill

A settlement/faction should care somewhat about how its people were treated while employed by the player's colony.

### Desired behavior

The employee's overall employment experience may produce a small goodwill consequence with the employee's originating faction.

The preferred conceptual model is based on consolidated experience / meaningful checkpoints rather than continuously farming goodwill from momentary mood ticks.

A long, highly positive employment experience may generate a modest positive effect.

A sufficiently bad experience may generate a negative one.

### Constraints

- Effect must remain small.
- It must not become an easy infinite goodwill farm.
- It should complement Employer Reputation, not replace it.
- Exact magnitude and evaluation window are balance decisions.

### Product intent

Employment should participate in inter-settlement relationships.

---

## F13 — Auto-renew status visible directly on employee cards

The employee card should expose whether Auto-renew is currently ON or OFF without requiring the player to open a secondary menu.

Changing the setting may remain behind an appropriate interaction if necessary, but current state should be immediately visible.

---

## F16/F17 — Simplify employee cards

Current employee cards contain too much secondary information and too many actions in their primary surface.

### Primary card should emphasize

- pay / day or equivalent primary pay metric;
- time remaining;
- Auto-renew state;
- worker / combat type.

### Secondary actions/details

Actions such as:

- Keep them;
- Not now;
- Dismiss;

and other secondary detail should move behind the employee's `[…]` menu or another compact secondary surface.

### Product intent

The card answers “who is this person, what are they costing me, how long are they here, and what kind of worker are they?” at a glance.

Actions that are only occasionally used should not dominate the card.

---

# D. Procurement and agreement UX

## F10 — Procurement relationship progression

Procurement should develop an explicit relationship progression analogous to Selling.

Conceptually:

**Stranger → Trusted Customer → Long-term Agreement**

The exact thresholds and labels should integrate with the repository's existing commercial reputation and agreement architecture rather than duplicate it unnecessarily.

### Product intent

Repeatedly buying from settlements should build a relationship just as repeatedly supplying settlements does.

Procurement should not feel like the less-developed half of Intercolony trade.

---

## F14 — Contracts should be collapsible

Selling and Procurement agreement/contract presentations should support collapsing individual entries.

Selling contracts should begin collapsed by default unless there is a strong existing contextual reason an exceptional state must draw immediate attention.

The collapsed state should still show enough summary information for the player to identify the contract and recognize important status.

### Product intent

Long lists of commercial relationships should remain scannable.

---

## F15 — Agreements default to Auto-ready ON

New Selling and Procurement agreements should begin with **Auto-ready enabled by default**.

The player can disable it.

### Product intent

Long-term agreements are intended to automate repeated commerce; automation should be the default rather than an opt-in hidden behind repeated manual readiness work.

---

## F18 — Procurement agreements should show price per unit

Procurement agreement displays should expose the effective unit price, not only total order/payment figures.

The player should be able to compare supplier economics quickly.

---

# E. Business intelligence and costing

## F07 — Production commitment versus actual recent production

The Business view should help the player understand whether production capacity is keeping up with active commitments.

### Desired presentation

For goods with active production commitments, show conceptually:

- committed quantity / day;
- actual recent average completed production / day.

Example:

`Dining chair — Commitment 8.0/day — Recent production 6.4/day`

### Production meaning

Production means items actually completed.

Do not infer production from stockpile change.

Selling 20 existing chairs must not appear as negative chair production.

### Window

A recent rolling period such as approximately five in-game days is appropriate as a product concept.

The exact technically robust window may be chosen during implementation.

### Product intent

The player should see capacity shortfalls before a contract fails.

---

## F19 — Material replacement cost / purchased-input estimate

Business profitability should account for the economic value of internally produced or extracted inputs.

### Meaning

This metric answers:

> If the colony had not produced/extracted these required inputs internally and had instead needed to purchase them, approximately how much would those inputs have cost?

It does **not** mean:

> How much would the finished competitor product have cost?

### Example

A chair made from internally harvested wood does not have zero material cost economically.

The wood consumed represents resources the colony would otherwise need to acquire.

### Preferred price hierarchy

For each relevant consumed input, prefer economically meaningful evidence in roughly this conceptual order:

1. recent procurement prices actually paid by the player for that input;
2. a current/recent Intercolony procurement-market estimate for that input;
3. a sensible generic market-value fallback if no usable Intercolony market evidence exists.

The exact aggregation/statistical method should resist individual outliers and may be selected during implementation.

### Intermediate goods

If production consumes an intermediate tradeable good such as Components, the economic input is the Components consumed.

Do not automatically decompose every intermediate product recursively into ultimate raw materials unless the established costing architecture makes that clearly preferable.

### Product intent

The Business screen should show economic production margin, not merely silver that visibly left the colony.

---

## F20 — Direct labor cost should reflect actual recent work where practical

The current wage-bill attribution to product profitability should become materially more accurate.

### Preferred model

Where technically reliable and computationally reasonable, measure the actual employee labor used to produce a product over a recent rolling period.

Conceptually:

- observe employee time actually spent on production attributable to a product;
- value that time according to that employee's compensation;
- combine relevant employees;
- divide appropriately by resulting production to estimate recent direct labor cost per unit.

Example:

An employee costing 30 silver/day spends approximately 40% of their working production time on chairs during a five-day observation period.

Approximately 60 silver of that period's wage exposure belongs to chair production rather than attributing the employee's entire wage to every good they are capable of producing.

### Window

A recent rolling period such as the last several days or an appropriate recent production sample is desirable.

The exact method should favor statistical usefulness and technical reliability rather than rigidly requiring “5 days” or “50 items”.

### Performance constraint

Do not implement constant expensive full-map/pawn polling merely to claim precise accounting.

Prefer event-driven observation or inexpensive accumulation if suitable hooks exist.

### Fallback

If reliable actual-work attribution would require invasive, expensive or brittle instrumentation, use a coarser **relevant-workforce approximation** based on the employees realistically eligible/assigned to the work.

A coarse honest estimate is preferable to fake precision.

### Product intent

When the player looks at the profitability of chairs, Cooking employees and unrelated payroll should not inflate chair labor cost.

---

## F08 — Commercial reputation may support limited positive goodwill

Investigate the current interaction between RimWorld goodwill deterioration and Intercolony commercial relationships.

Excellent commercial relationships should be capable of exerting a modest positive influence on faction goodwill.

### Desired behavior

Very strong commercial reputation may gradually push goodwill toward a limited modest positive ceiling.

Commercial activity alone should not turn hostile factions into allies or become an unlimited diplomacy engine.

This is conceptually a bounded positive pressure:

> “We do a lot of useful business with these people, so our political relationship does not easily remain terrible.”

### Constraints

- influence is limited;
- it should not trivially overpower major vanilla hostility/diplomatic events;
- commercial reputation remains distinct from vanilla goodwill;
- exact curve/cap is a balance decision informed by existing RimWorld behavior.

---

# F. Labor market redesign and expansion

The following findings together deliberately evolve Labor into a deeper two-sided market.

Some existing repository documentation describes the current player-set-wage Job Posting design as intentional.

**F25 below explicitly supersedes that decision.**

---

## F25 — Job postings become labor RFQs; the market quotes the wage

### Current behavior being replaced

The current Job Posting model allows the player to name the wage and workers apply when the offered wage clears their asking price.

This batch intentionally changes that philosophy.

### New desired behavior

The player specifies **what work is required**, not what the worker must cost.

A posting defines requirements such as:

- requested skill;
- minimum skill;
- employment/combat regime;
- term;
- other relevant job requirements such as equipment or urgency where applicable.

The market then returns candidates with their own wage offers.

Conceptually:

`Construction 12+, Civilian, 20 days`

might return:

- Anna — Construction 13 — 52/day
- Bob — Construction 17 — 91/day
- Carlos — Construction 12 — 46/day
- Dmitri — Construction 19 — 138/day

### Market shape

Worker quality and price should correlate without becoming deterministic.

High-skill cheap workers should be possible but rare.

High-skill expensive workers should be substantially more common than high-skill bargains.

Poor or mediocre offers can exist; the player simply does not have to accept them.

### Existing economics

Reuse the existing labor-economic concepts where appropriate:

- skill;
- labor supply;
- distance;
- settlement profile;
- employer reputation;
- term;
- combat clause;
- market scarcity.

Do not create a second disconnected model of labor value unless clearly required.

### Product intent

The player posts a requirement and discovers the market price.

This should feel structurally closer to Procurement/RFQ behavior than to guessing a hidden clearing wage.

---

## F22 — The player can supply their own colonists to the labor market

The labor market should become two-sided.

The player should be able to make eligible colonists available for external employment and receive offers from other settlements.

### Listing configuration

The player should be able to define terms such as:

- which colonist is available;
- allowed employment/combat regime:
  - Civilian;
  - Armed Employee;
  - Security Contractor;
- minimum acceptable term;
- maximum acceptable term;
- minimum acceptable compensation.

There is no meaningful need for a maximum acceptable wage.

If somebody wants to overpay, the colony may accept the offer.

### Offer generation

Other settlements may send offers over time.

A highly capable colonist should generally:

- attract more interest;
- receive offers sooner;
- receive better-paying opportunities.

A poor candidate may:

- attract only weak offers;
- take a long time to receive an offer;
- sometimes receive none at the current minimum acceptable compensation.

Lowering the player's minimum acceptable compensation should increase the likelihood that offers clear the threshold.

### Relationship to F25

This should feel like the reciprocal side of the same labor economy rather than a bespoke mission generator.

Settlements are purchasing labor from the player in the same economic world in which the player purchases labor from settlements.

### Training

A colonist who completes outside employment should gain appropriate skill experience related to the work performed.

The system does not need to simulate another full settlement map.

A reliable abstract employment outcome is acceptable.

### Risk

Outside employment is not perfectly risk-free.

Risk should depend strongly on job type.

**Civilian**

Usually very safe.

**Armed Employee**

Some meaningful risk.

**Security Contractor**

Materially higher risk, potentially including injury and, rarely, death.

The exact event rates require later balance testing.

### Product intent

Sending a valuable pawn away should be an economic decision:

income + training opportunity versus absence + risk.

---

## F23 — Requested equipment quality and refundable equipment bond

When hiring workers, especially Armed Employees and Security Contractors, the player should be able to request a level of supplied equipment.

### Concept

The player may request progressively more capable loadouts.

The exact labels/tier count may be designed during implementation.

A high-end Security Contractor may arrive in powerful armor with powerful weapons.

### Availability

Requesting high-end equipment must materially restrict the available candidate pool.

A low-tech or poorly supplied source settlement must not magically materialize endgame equipment merely because the player selected the highest tier.

Equipment availability should relate to the worker, settlement, tech level, wealth and market scarcity.

### Pricing

Equipment should not simply be incorporated as ordinary wages.

Use a substantial **refundable equipment bond/deposit** concept.

Conceptually:

`market/replacement value of issued equipment + premium`

A starting premium on the order of ~10% may be used for initial balancing, but the exact value is not sacred.

### Return

If the employee returns the contracted equipment, the applicable bond is refunded.

Normal wear is not a reason to nickel-and-dime the player.

The system should care about meaningful loss/non-return rather than durability micro-accounting.

### Equipment retained by the colony

If the colony deliberately retains issued equipment, the corresponding bond is retained.

This is not inherently an exploit.

The player has effectively purchased scarce gear through the labor market.

The principal balance control is that rare gear must be genuinely scarce and appropriately priced.

### Extreme body modification / implanted equipment

The same economic principle applies more strongly to valuable body parts and implants.

If the player removes a rare prosthetic, bionic or similarly valuable body component from an employee — for example deliberately removing an arm to keep an exceptional prosthesis — the financial consequence must be **far higher than simply paying the item's ordinary market value**.

It represents serious bodily harm, breach of employment obligations, replacement cost, compensation and reputational liability.

The player may technically be able to do it, but it should be an economically and reputationally severe act rather than a cheap method of buying implants.

### Product intent

Equipment is borrowed capital associated with the employee, not free loot.

At the same time, Intercolony generally prefers pricing consequences to arbitrary prohibitions.

---

## F24 — Emergency / urgent hiring

The player should be able to issue an emergency labor request when ordinary market arrival times are insufficient.

This is especially relevant to combat/security hiring during imminent threats.

### Desired experience

The player may request something like:

- number of workers;
- Security Contractor / other clause;
- high required combat skill;
- short term;
- equipment level;
- **Emergency dispatch**.

Emergency dispatch accesses a much narrower and much more expensive subset of the current labor market.

### Scarcity

Emergency mode does not guarantee fulfillment.

A worker must still:

- exist in the available labor market;
- satisfy the requested requirements;
- be available for urgent dispatch;
- come from a source capable of reaching the colony within the urgent window.

Requesting ten elite soldiers must not spawn ten elite soldiers simply because the player can pay.

### Pricing

Urgency should create a very large premium.

It does not need to be a fixed hardcoded `10x` or `20x` multiplier if a more coherent market model works better.

Offers should simply become dramatically more expensive because:

- the eligible pool is smaller;
- workers demand an urgency premium;
- mobilization costs more;
- fast transportation costs more.

### Logistics interaction

F24 should use F21's settlement logistics capabilities.

A distant settlement with rapid transport capability may be able to satisfy an emergency request.

A distant settlement without rapid logistics may simply be unable to make a useful emergency offer.

### Arrival experience

Transport pods are particularly desirable for emergency hires when the supplying settlement can support them.

This should feel exceptional and visceral.

Routine standard-market employees should generally not arrive by costly transport pod.

Emergency workers arriving by drop pod within hours creates a strong distinction between:

**ordinary hiring** and **“I need soldiers now.”**

A sufficiently nearby conventional source may instead be able to reach the map quickly without pods.

### Product intent

Emergency hiring buys **priority and speed**, not guaranteed pawns.

---

# G. Cross-system relationships

The findings above should remain individually traceable, but several product relationships are intentional.

### Production automation

F02, F03 and F04 form one coherent production-control family.

Do not collapse them into one unverifiable mega-requirement; preserve their distinct behavioral claims.

### Contract automation

F01, F12 and F15 together move repeated agreements toward quiet set-and-forget execution while retaining exception reporting.

### Business profitability

F07, F19 and F20 together improve the Business view from static accounting toward operational decision support:

- Are we producing enough?
- What are the economically consumed materials worth?
- What is the labor actually costing us?

### Settlement logistics

F05 handles the player's receiving side.

F12 handles reusable player-side caravan execution.

F21 provides the broader economic model of distance and transport infrastructure.

### Commercial relationships

F08, F09 and F10 make recurring commerce and employment affect longer-lived inter-settlement relationships in controlled ways.

### Two-sided labor economy

F22–F25 are intended to reinforce one another:

- F25 establishes labor RFQ / market-priced hiring;
- F23 adds equipment as part of worker supply;
- F24 adds urgent high-cost mobilization;
- F22 lets the player become a labor supplier too;
- F21 provides the logistics layer that explains how workers and goods physically move.

Treat these as one economic world rather than separate minigames.

---

# H. Verification expectations

The implementation decomposition belongs to Foreman, but acceptance evidence should reflect the behavioral nature of the findings.

For affected systems, use the repository's existing build and dev-bridge infrastructure wherever capable of proving the behavior.

Expected evidence may include, where applicable:

- targeted self-tests;
- fresh-world tests where state isolation matters;
- whole-suite `test all -Fresh` integration gates;
- clean `Player.log` delta;
- save/load verification;
- cross-game/static-state verification;
- mutation or negative-control tests where a passing test might otherwise fail to prove the intended behavior;
- deterministic economic-model checks;
- bounded performance checks for systems that accumulate work/activity data.

Do not claim visual or experiential UI behavior is proven by a model-only assertion when actual human observation is necessary.

When a finding truly requires human visual/product judgment and cannot be established through repository instrumentation, record the exact remaining Human Evidence required rather than treating the Slice as automatically proven.

---

# I. Scope and authority boundaries

This batch authorizes ordinary reversible engineering work necessary to deliver the findings above.

It does not authorize:

- merging to `main`;
- publishing to Steam Workshop;
- creating a public release;
- destructive repository operations;
- security/credential changes;
- irreversible external actions.

Those remain reserved for the operator.

Within the codebase, the Supervisor should resolve normal reversible engineering choices autonomously rather than interrupting the operator for routine implementation alternatives.

When genuine product ambiguity remains despite this Plan and the repository's design context, quarantine only the affected dependency chain according to Foreman's blocker semantics and continue independent executable work.

---

# J. Plan completion

This Plan is complete only when every finding F01–F25 represented above has a durable disposition:

- implemented and accepted with appropriate Evidence;
- explicitly invalidated by stronger repository/product evidence;
- explicitly deferred through authorized scope handling;
- or blocked by a genuine Human Blocker.

No finding may disappear merely because it was inconvenient to decompose.

The final Run record should preserve traceability from each source finding to the resulting Plan Requirement(s), Slice Contract(s), Evidence and final disposition.
