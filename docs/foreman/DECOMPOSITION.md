# Current decomposition

All Slices were created by Supervisor role instance `/root` on 2026-09-05. `S01` is `IN_PROGRESS`
after Candidate `S01-C1` received an `ENVIRONMENT_FAILURE` verdict; every
other Slice is `DRAFT` and must receive a full Contract and dependency check before becoming
executable. Stage order reflects the known dependency direction. A `DRAFT` entry is not delegated
work.

## `ST1` — Quiet agreement automation

Acceptance Gate: focused fresh-world Evidence shows routine automated selling success emits no
letter, failure emits actionable feedback, new agreements default to auto-ready, manual control is
preserved, and affected persistence checks pass.

| Slice | Behavioral Claim | State | Dependencies |
|---|---|---|---|
| `S01` | When an auto-ready selling cycle has all required goods, it becomes ready without a player letter, while an unfulfillable cycle reports the actionable reason. | `IN_PROGRESS` | none |
| `S02` | New selling agreements begin with Auto-ready enabled and the player can disable it. | `DRAFT` | none |
| `S03` | New procurement agreements begin with Auto-ready enabled and the player can disable it. | `DRAFT` | none |

## `ST2` — Produce lifecycle and programming

Acceptance Gate: focused Produce Evidence demonstrates cancel, area resume/pause/stop, configured
targets and worker controls through real lifecycle transitions, plus save/load and cross-game
isolation where state is persisted.

| Slice | Behavioral Claim | State | Dependencies |
|---|---|---|---|
| `S04` | Cancelling the active blueprint of a Produce loop removes that loop and no replacement blueprint appears. | `DRAFT` | none |
| `S05` | Area Produce/Resume starts or resumes every eligible selected Produce program. | `DRAFT` | none |
| `S06` | Area Pause lets a committed construction finish, then leaves the installed object idle until resumed. | `DRAFT` | `S05` |
| `S07` | Area Stop ends selected programs permanently and never begins another cycle after any committed uninstall completes. | `DRAFT` | `S05` |
| `S08` | A Produce program runs indefinitely or stops after its configured quantity target is met. | `DRAFT` | `S04` |
| `S09` | Produce enforces configured quality or condition constraints wherever those concepts are meaningful. | `DRAFT` | `S08` |
| `S10` | Produce work respects configured worker eligibility and relevant skill constraints. | `DRAFT` | `S08` |
| `S11` | More than one eligible worker can contribute when the underlying Produce work permits it. | `DRAFT` | `S10` |

## `ST3` — Receiving and progressive procurement

Acceptance Gate: deliveries select filter-compatible configured receiving storage with safe fallback,
and RFQ response timing Evidence shows distance-biased progressive arrival without a deterministic
global ordering.

| Slice | Behavioral Claim | State | Dependencies |
|---|---|---|---|
| `S12` | Arriving goods prefer configured vanilla storage destinations whose filters accept them and which have capacity. | `DRAFT` | none |
| `S13` | Delivery falls back safely when configured receiving capacity is exhausted or no receiving location exists. | `DRAFT` | `S12` |
| `S14` | Supplier RFQ responses arrive over time with distance strongly biasing, but not deterministically fixing, latency. | `DRAFT` | none |

## `ST4` — Settlement logistics and recurring caravans

Acceptance Gate: deterministic model Evidence and focused play-path checks show geography, cargo and
settlement capability affect cost/time/method, and programmed agreement caravans wait for complete
orders, dispatch under the configured policy, and surface invalid resources.

| Slice | Behavioral Claim | State | Dependencies |
|---|---|---|---|
| `S15` | Shipment estimates materially vary with distance, route difficulty, cargo burden and expected provisions. | `DRAFT` | none |
| `S16` | Stable settlement identity determines available logistics capabilities without making archetypes absolute gates. | `DRAFT` | `S15` |
| `S17` | Shipment method selection chooses an economically sensible available method and exposes method, cost and arrival estimate. | `DRAFT` | `S15`, `S16` |
| `S18` | A selling or procurement agreement can persist a player-configured caravan for one cycle or recurring cycles. | `DRAFT` | none |
| `S19` | A programmed caravan waits when the full order is unavailable and reports the available and required quantities. | `DRAFT` | `S18` |
| `S20` | A complete programmed caravan dispatches only after manual Ready with Auto-ready off, and automatically with Auto-ready on. | `DRAFT` | `S02`, `S03`, `S18`, `S19` |
| `S21` | An unavailable programmed pawn, animal or dispatch resource leaves the agreement comprehensible and produces an actionable exception without silent substitution. | `DRAFT` | `S18` |

## `ST5` — Employee and agreement UX

Acceptance Gate: focused UI/model Evidence shows apparel opt-in, bounded employment goodwill,
immediately visible employee essentials, collapsible contracts, and procurement unit prices while
preserving the project's key/value and measured-text rules.

| Slice | Behavioral Claim | State | Dependencies |
|---|---|---|---|
| `S22` | Employees default to conservative apparel behavior but can explicitly use a vanilla-compatible colony apparel policy. | `DRAFT` | none |
| `S23` | Consolidated employment experience applies a small bounded goodwill effect that cannot be farmed from momentary mood changes. | `DRAFT` | none |
| `S24` | Employee cards show pay, remaining term, Auto-renew state and worker/combat type at a glance. | `DRAFT` | none |
| `S25` | Keep, Not now, Dismiss and secondary employee details are available from the compact secondary menu rather than dominating the card. | `DRAFT` | `S24` |
| `S26` | Selling and procurement contract entries can be collapsed, with selling entries initially collapsed and exceptional state still recognizable. | `DRAFT` | none |
| `S27` | Procurement agreement displays expose effective price per unit. | `DRAFT` | none |

## `ST6` — Commercial relationships and business intelligence

Acceptance Gate: deterministic and fresh-world Evidence shows procurement progression, bounded
commercial goodwill, completed-production throughput, replacement-cost inputs and attributable labor
cost without stock-change inference or expensive full-map polling.

| Slice | Behavioral Claim | State | Dependencies |
|---|---|---|---|
| `S28` | Repeated procurement progresses from stranger through trusted customer to long-term agreement using the shared commercial relationship architecture. | `DRAFT` | none |
| `S29` | Excellent commercial reputation applies limited positive goodwill pressure without reversing hostility or overpowering major diplomacy. | `DRAFT` | `S28` |
| `S30` | For committed goods, Business shows committed quantity/day beside a rolling average of actually completed production/day, unaffected by stockpile sales. | `DRAFT` | none |
| `S31` | Business material cost values consumed inputs using recent paid procurement, then market estimate, then generic market value, without recursively decomposing tradeable intermediates. | `DRAFT` | none |
| `S32` | Business direct labor cost attributes recent employee production work to its resulting product without charging unrelated payroll or constant expensive polling. | `DRAFT` | `S30` |

## `ST7` — Two-sided labor market

Acceptance Gate: market-model, scarcity, persistence and real arrival/return Evidence shows hiring is
RFQ-priced, equipment is scarce borrowed capital, emergency dispatch buys costly speed rather than
guaranteed pawns, and player colonists can take abstract outside employment with training and
regime-scaled risk.

| Slice | Behavioral Claim | State | Dependencies |
|---|---|---|---|
| `S33` | A labor posting specifies work requirements and returns candidate wage quotes whose price correlates non-deterministically with quality and existing market factors. | `DRAFT` | `S17` |
| `S34` | Requested equipment tier restricts candidate availability according to worker and settlement capability, and issued equipment is backed by a refundable replacement-value-plus-premium bond. | `DRAFT` | `S33` |
| `S35` | Returned contracted equipment refunds its bond, retained gear keeps the corresponding bond, and deliberate removal of valuable body equipment incurs severe financial and reputation consequences. | `DRAFT` | `S34` |
| `S36` | Emergency labor requests quote only genuinely available workers reachable in the urgent window, with large scarcity/mobilization premiums and capability-appropriate rapid arrival. | `DRAFT` | `S17`, `S33`, `S34` |
| `S37` | Listed eligible colonists receive market offers conditioned by terms and capability, then return from accepted abstract employment with appropriate training and regime-scaled injury/death risk. | `DRAFT` | `S33` |
