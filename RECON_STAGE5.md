# Stage 5 seam map: F21 market geography and F11 progressive RFQ responses

## Scope and reading rule

This is a source-only recon. `READ` records what is present in the repository; `CONCLUDE` records the seam assessment and the proposed cut. A `NOT FOUND` item includes the exact search used. The vanilla references below are from the repository's decompiled/reference tree.

## A. F21: what the finding actually asks for

### READ

F21 asks shipment cost and delivery time to materially reflect world distance, route or travel difficulty, cargo burden, expected caravan duration, provisions or animal/transport burden, settlement logistics capability, technology/wealth/identity, and transport method; it explicitly rejects a flat or weak distance-only formula (`docs/PLAYTEST_BATCH_SOURCE_PLAN.md:282-293`). It also asks settlements to differ in logistics capability and for method choice to be economically sensible, so fast transport can be substantially dearer and faster without being automatic (`docs/PLAYTEST_BATCH_SOURCE_PLAN.md:297-318`). The player-facing offer or order should expose logistics cost, expected arrival time, and transport method, making settlements feel like geographically distinct economic places rather than equivalent endpoints (`docs/PLAYTEST_BATCH_SOURCE_PLAN.md:320-332`).

### Already exists that serves F21

- A shared distance seam exists: `DistanceToPlayer` returns an approximate distance from the player's home map to a settlement and uses `Find.WorldGrid.ApproxDistanceInTiles` (`Source/Intercolony/Market/MarketOpportunityGenerator.cs:385-397`). The reference implementation confirms that the two-tile overload converts the tile centers' spherical distance to approximate tiles (`reference/decompiled/RimWorld.Planet/PlanetLayer.cs:973-982`).
- RFQ availability already varies with distance: `TryQuote` subtracts a distance penalty from response chance and can return silence on the random roll (`Source/Intercolony/Procurement/RfqService.cs:253-260`). Technical capability and effective supply can also prevent a quote before that roll (`Source/Intercolony/Procurement/RfqService.cs:242-250`).
- RFQ price and delivery terms already vary with distance. Goods use the shared supplier price function, which adds a capped distance factor and a supplier-delivery factor (`Source/Intercolony/Market/IntercolonyPricing.cs:434-499`); animal RFQs have a separate distance factor (`Source/Intercolony/Procurement/RfqService.cs:533-566`). Delivery is less likely beyond 60 tiles, and a delivered quote gets a distance-derived lead time (`Source/Intercolony/Procurement/RfqService.cs:598-620`).
- A simple logistics mode already exists: an RFQ quotation records supplier delivery versus player pickup, lead time, distance, and an explanation (`Source/Intercolony/Procurement/PurchaseRequest.cs:61-89`), and the RFQ row exposes supplier, quantity, price, lead, terms, and distance (`Source/Intercolony/UI/MainTabWindow_Intercolony.cs:4974-5029`).
- The market-opportunity side already selects buyer pickup versus seller delivery from distance, prices the mode, stores distance/deadline/mode, and gives the UI a distance-based pickup estimate (`Source/Intercolony/Market/MarketOpportunityGenerator.cs:131-178`; `Source/Intercolony/UI/MainTabWindow_Intercolony.cs:1304-1327`). Buyer-pickup sales orders then store the distance and turn the estimate into a persisted buyer-arrival tick when the goods are marked ready (`Source/Intercolony/Orders/SalesOrder.cs:131-138`; `Source/Intercolony/Orders/SalesOrder.cs:315-318`; `Source/Intercolony/Orders/SalesOrderService.cs:724-754`).
- Crated goods already have a physical-form constraint: generation caps building lots and the market detail warns that each crate consumes caravan capacity (`Source/Intercolony/Market/MarketOpportunityGenerator.cs:337-355`; `Source/Intercolony/Market/MarketOpportunity.cs:425-429`; `Source/Intercolony/UI/MainTabWindow_Intercolony.cs:1379-1383`). This is a cap and warning, not a general cargo-cost or provisioning calculation.
- The settlement identity inputs F21 names already exist as economic inputs: profiles contain technology tier, wealth tier, archetype, volatility, and supply/demand weights (`Source/Intercolony/Core/SettlementEconomicProfile.cs:75-106`), and profile generation derives archetype, wealth, volatility, and weights deterministically (`Source/Intercolony/Core/SettlementProfileGenerator.cs:95-115`). Those are economic identity/supply inputs, not a logistics-infrastructure model.

### Does not exist yet

- Beyond the crated-good cap/warning, the current model has no route/path difficulty, provision requirement, explicit logistics capability/infrastructure, transport-method record, or pod/caravan method selection in the procurement/market code. `NOT FOUND`: the searches `rg -n -i "route.*difficulty|travel.*difficulty|path.*cost|terrain.*difficulty|terrain.*cost" Source/Intercolony/Procurement Source/Intercolony/Market Source/Intercolony/Core`, `rg -n -i "cargo.*burden|cargo.*mass|provision|caravan.*food|food.*caravan|animal.*transport" Source/Intercolony/Procurement Source/Intercolony/Market Source/Intercolony/Core`, `rg -n -i "logistics.*(capabil|infrastructure)|capabil.*logistics" Source/Intercolony/Procurement Source/Intercolony/Market Source/Intercolony/Core`, and `rg -n -i "\bTransportPod\b|\btransport pods?\b|\btransport method\b|\btransportMethod\b" Source/Intercolony` all returned `NO MATCHES`.
- There is an abstract travel estimate, not a route simulation: delivered RFQ lead time is `prep + round(distance / 12)`, while buyer-pickup sales travel is `round(distance / 14)` with a fallback for unknown distance (`Source/Intercolony/Procurement/RfqService.cs:610-620`; `Source/Intercolony/Orders/SalesOrderService.cs:924-932`).
- The stored per-quote RFQ logistics mode is only the boolean `supplierDelivers`; no `MaxMarketDistance` filter is applied in the RFQ path. The UI distance filter is attached to `MarketOpportunity` rows (`Source/Intercolony/UI/MainTabWindow_Intercolony.cs:847-880`, `Source/Intercolony/UI/MainTabWindow_Intercolony.cs:952-965`), while RFQ candidate selection calls accessibility and profile checks (`Source/Intercolony/Procurement/RfqService.cs:127-167`). `NOT FOUND`: `rg -n -i "MaxMarketDistance|maxMarketDistance" Source/Intercolony/Procurement` returned `NO MATCHES`.

## B. Request Goods / RFQ path end to end

### READ

| Step | Current seam | What happens |
|---|---|---|
| 1. Player opens the request surface | `Source/Intercolony/UI/MainTabWindow_Intercolony.cs:2817-2821` | The Find seller tab's `Request goods...` button opens `Dialog_CreateRequest`. |
| 2. Player submits the request | `Source/Intercolony/UI/Dialog_CreateRequest.cs:713-728` | `Send` validates an animal request when relevant, then calls `RfqService.CreateRequest` with item, material, quantity, deadline, fulfillment preference, animal spec, and quality. |
| 3. Request record is constructed | `Source/Intercolony/Procurement/RfqService.cs:66-89` | The service allocates the request ID, stores the item/quantity/deadline/preferences, stamps `createdTick` and `expiryTick`, and sets status `Open`. |
| 4. Responses are invoked | `Source/Intercolony/Procurement/RfqService.cs:91-92` | `CreateRequest` calls `GenerateResponses` and only afterward adds the request to world state. |
| 5. Candidate settlements are gathered | `Source/Intercolony/Procurement/RfqService.cs:125-167` | `GenerateResponses` reads `Find.WorldObjects.Settlements`, classifies the request, seeds the random scope, walks settlements, rejects inaccessible settlements or missing profiles, and passes each remaining settlement to `TryQuote`. |
| 6. Each candidate answers | `Source/Intercolony/Procurement/RfqService.cs:229-311` | `TryQuote` applies technical and effective-supply gates, rolls distance-adjusted response chance, chooses offered material/quality/quantity, chooses delivery versus pickup, computes price and lead time, and constructs one `Quotation` or returns `null` for silence. |
| 7. Quotes enter the request | `Source/Intercolony/Procurement/RfqService.cs:170-204` | Stock already consumed in the refresh window is subtracted, nonempty quotes receive IDs and are appended, then the list is sorted; an empty response gets one aggregate `noResponseReason`. |
| 8. The request is owned by the world component | `Source/Intercolony/Core/IntercolonyWorldComponent.cs:339-345`; `Source/Intercolony/Core/IntercolonyWorldComponent.cs:450-455` | `requests` is the authoritative list and `AddRequest` inserts the completed request. The request's `quotes` list is nested inside that record (`Source/Intercolony/Procurement/PurchaseRequest.cs:165-203`). |
| 9. Player gets the immediate creation result | `Source/Intercolony/Procurement/RfqService.cs:94-108` | The service immediately reports either the total number of supplier answers or that no supplier can provide the item. |
| 10. Player sees live requests and quotes | `Source/Intercolony/UI/MainTabWindow_Intercolony.cs:2825-2877`; `Source/Intercolony/UI/MainTabWindow_Intercolony.cs:4889-4971` | Find seller keeps only open requests, shows quote count or the no-response reason, and renders every stored quote row. |
| 11. Player inspects or accepts a quote | `Source/Intercolony/UI/MainTabWindow_Intercolony.cs:4974-5029`; `Source/Intercolony/UI/MainTabWindow_Intercolony.cs:5140-5196` | A row shows supplier, offered quantity, unit/total price, lead, terms, and distance; Buy opens quantity confirmation and eventually calls `PurchaseOrderService.AcceptQuote`. |
| 12. Request expiry is advanced | `Source/Intercolony/Core/IntercolonyWorldComponent.cs:1741-1748`; `Source/Intercolony/Procurement/RfqService.cs:623-638` | The coarse refresh expires open requests whose `expiryTick` has passed. |

## C. Timing: all at once or over time?

### READ

F11's desired behavior is progressive supplier responses, with distance strongly influencing latency, nearby settlements tending to answer sooner, and timing remaining non-deterministic rather than a strict nearest-first queue (`docs/PLAYTEST_BATCH_SOURCE_PLAN.md:202-222`).

Responses are computed all at once at request creation. The service explicitly documents that quotes are rolled once at creation (`Source/Intercolony/Procurement/RfqService.cs:18-19`); the call to `GenerateResponses` occurs before `state.AddRequest` (`Source/Intercolony/Procurement/RfqService.cs:91-92`), and the response method walks the whole settlement list in one synchronous `foreach` (`Source/Intercolony/Procurement/RfqService.cs:149-189`). The per-settlement decision is the distance-adjusted `Rand.Value > chance` refusal at `Source/Intercolony/Procurement/RfqService.cs:253-260`, followed by the quotation construction at `Source/Intercolony/Procurement/RfqService.cs:289-311`.

The world tick drives the world component, but it does not drive RFQ responses: vanilla `World.WorldTick` invokes `WorldComponentUtility.WorldComponentTick` (`reference/decompiled/RimWorld.Planet/World.cs:213-222`), the mod component's tick currently schedules refreshes and hourly deadline work (`Source/Intercolony/Core/IntercolonyWorldComponent.cs:1594-1665`), and the refresh calls `RfqService.ExpireStale` rather than generating new responses (`Source/Intercolony/Core/IntercolonyWorldComponent.cs:1728-1748`).

The F11 instantaneous attachment is therefore the `GenerateResponses` call in `CreateRequest` (`Source/Intercolony/Procurement/RfqService.cs:91`), with the whole-world loop at `Source/Intercolony/Procurement/RfqService.cs:125-198` and the one-settlement calculator at `Source/Intercolony/Procurement/RfqService.cs:229-311` as the split points.

## D. Existing ways for something to happen later

### READ

| Existing mechanism | Evidence | Boundary and limitation |
|---|---|---|
| World tick and coarse/hourly drivers | Vanilla calls `WorldComponentTick` from `WorldTick` (`reference/decompiled/RimWorld.Planet/World.cs:213-222`). The mod has an absolute-tick refresh and an hourly check (`Source/Intercolony/Core/IntercolonyWorldComponent.cs:1598-1665`). | This is a driver, not an RFQ response queue. `NOT FOUND`: `rg -n -i "pending.*(rfq|quote|response)|(rfq|quote|response).*pending" Source/Intercolony`, `rg -n -i "(response|quote).*(tick|delay|schedule|queue)|(tick|delay|schedule|queue).*(response|quote)" Source/Intercolony/Procurement Source/Intercolony/Debug/IntercolonyRfqSelfTest.cs`, and `rg -n -i "Advance.*(Rfq|RFQ|Request)|(Rfq|RFQ|Request).*Advance" Source/Intercolony` all returned `NO MATCHES`. |
| A queue, but not a gameplay delay queue | The queue found in the code is the dev bridge's `ConcurrentQueue` (`Source/Intercolony/Debug/Bridge/IntercolonyDevBridgePump.cs:41-50`), which is drained by Unity `Update`, explicitly because it must work while paused (`Source/Intercolony/Debug/Bridge/IntercolonyDevBridgePump.cs:18-20`; `Source/Intercolony/Debug/Bridge/IntercolonyDevBridgePump.cs:101-117`). `JobPostingService` has a local applicant `queue` (`Source/Intercolony/Labor/JobPostingService.cs:109-225`). | Neither is a persisted game-time queue: the bridge is dev-only main-thread plumbing, and the applicant queue is built, sorted, and applied inside one `MatchAll` call (`Source/Intercolony/Labor/JobPostingService.cs:136-221`). |
| Scheduled world event interval | `EconomicEvent` stores `startTick` and `endTick`, defines `IsActiveAt`, and saves both ticks (`Source/Intercolony/Economy/EconomicEvent.cs:65-70`; `Source/Intercolony/Economy/EconomicEvent.cs:98-105`; `Source/Intercolony/Economy/EconomicEvent.cs:123-148`). `AdvanceLifecycle` removes ended events and starts a new event at the current tick; `StartEvent` adds it to world state (`Source/Intercolony/Economy/EconomicEventService.cs:67-90`; `Source/Intercolony/Economy/EconomicEventService.cs:112-140`). | This is a persisted active interval and lifecycle sweep, not a general future callback queue. The world refresh is the call site (`Source/Intercolony/Core/IntercolonyWorldComponent.cs:1714-1719`). |
| Deadline or ready tick on a record | RFQs have `expiryTick` (`Source/Intercolony/Procurement/PurchaseRequest.cs:181-219`) and are expired by a tick read during refresh (`Source/Intercolony/Procurement/RfqService.cs:623-638`). Paid purchase orders have `readyTick`, save it, and advance when `now >= readyTick` (`Source/Intercolony/Procurement/PurchaseOrder.cs:80-114`; `Source/Intercolony/Procurement/PurchaseOrderService.cs:364-387`). | These are existing time boundaries that can be followed by a state transition, but neither currently represents a pending RFQ answer. |

The required procurement-proposal precedent is already more specific than those generic deadlines. A `ProcurementContract` stores a sentinel-backed `decisionDueTick` for the delayed supplier answer (`Source/Intercolony/Procurement/ProcurementContract.cs:190-198`), proposal creation sets it from the shared delay function and stores the evaluator result (`Source/Intercolony/Procurement/ProcurementContractService.cs:298-321`), and `AdvanceProposals` applies every due answer by comparing `GenTicks.TicksGame` with that persisted tick (`Source/Intercolony/Procurement/ProcurementContractService.cs:858-878`). The decision-due field is part of the contract's `ExposeData` path (`Source/Intercolony/Procurement/ProcurementContract.cs:482-506`), and the evaluator's answer is captured once while the later transition is delayed (`Source/Intercolony/Procurement/ProcurementContractService.cs:324-326`). This is the closest precedent for staggering RFQ responses because it is already a persisted, one-shot, per-procurement answer with a due tick.

The supply-agreement offer expiry is a separate precedent. `RecurringContract` has `offerExpiryTick` and `decisionDueTick` fields (`Source/Intercolony/Contracts/RecurringContract.cs:93-108`), the offer lifespan is eight days (`Source/Intercolony/Contracts/ContractService.cs:147-151`), and `AdvanceContracts` lapses an unanswered offer when `now >= offerExpiryTick` (`Source/Intercolony/Contracts/ContractService.cs:1195-1215`). Both fields are persisted (`Source/Intercolony/Contracts/RecurringContract.cs:264-294`). Offer expiry is useful for cleanup; `ProcurementContract.decisionDueTick` is the better response-staggering pattern.

## E. Distance and geography already in the mod

### READ

The canonical distance is approximate world-tile distance from the player's home map. The mod returns `-1` when there is no home/world grid and otherwise calls `Find.WorldGrid.ApproxDistanceInTiles(home.Tile, settlement.Tile)` (`Source/Intercolony/Market/MarketOpportunityGenerator.cs:385-397`). The decompiled vanilla implementation confirms this is a spherical tile-center distance, not route/path distance (`reference/decompiled/RimWorld.Planet/PlanetLayer.cs:973-982`).

| Quantity that varies | Current implementation | Exact seam |
|---|---|---|
| RFQ response availability | Distance reduces response chance; a failed roll means no quote. | `Source/Intercolony/Procurement/RfqService.cs:253-260` |
| Technical/supply availability | A settlement that fails the technical gate, has effective supply below `0.35`, or offers zero quantity returns no quote. | `Source/Intercolony/Procurement/RfqService.cs:242-250`; `Source/Intercolony/Procurement/RfqService.cs:274-286` |
| RFQ delivery availability | With `Either`, delivery is selected using `DeliveryChance`; wealth sets the base and distance beyond 60 tiles halves it. | `Source/Intercolony/Procurement/RfqService.cs:289-295`; `Source/Intercolony/Procurement/RfqService.cs:598-607` |
| RFQ price | Shared supplier pricing adds a capped distance multiplier; supplier delivery adds a separate 1.12 multiplier. | `Source/Intercolony/Market/IntercolonyPricing.cs:467-499`; `Source/Intercolony/Market/IntercolonyPricing.cs:502-508` |
| RFQ lead time | Pickup is preparation plus small random variation; supplier delivery is preparation plus `round(distance / 12)`. | `Source/Intercolony/Procurement/RfqService.cs:610-620` |
| Sales-market price | Market `UnitPrice` adds a distance factor when distance is known. | `Source/Intercolony/Market/IntercolonyPricing.cs:240-250`; `Source/Intercolony/Market/IntercolonyPricing.cs:550-558` |
| Sales-market mode and travel | A farther buyer is less likely to pick up, the opportunity stores the mode/distance/deadline, and the shared pickup estimate is displayed and later stored as `buyerArrivalTick`. | `Source/Intercolony/Market/MarketOpportunityGenerator.cs:131-178`; `Source/Intercolony/Orders/SalesOrderService.cs:924-932`; `Source/Intercolony/Orders/SalesOrderService.cs:739-754` |

F21 can build on those seams, but the current “travel time” is an abstract distance divisor. The RFQ quotation persists only distance, lead time, delivery boolean, and price explanation (`Source/Intercolony/Procurement/PurchaseRequest.cs:55-86`); the paid order persists the resulting ready tick and delivery boolean (`Source/Intercolony/Procurement/PurchaseOrder.cs:77-94`). There is no stored route, cargo mass, food/provision requirement, transport capability, or transport method in that RFQ-to-order record path; the exact absence searches are listed in section A.

## F. Persistence and save-schema consequence

### READ

RFQ state is world-scoped. The component identifies itself as the single authoritative owner of persistent economic state, explicitly says it is a `WorldComponent` rather than map-level state, and says it is saved with the world (`Source/Intercolony/Core/IntercolonyWorldComponent.cs:11-23`). The request list is the component's private world list (`Source/Intercolony/Core/IntercolonyWorldComponent.cs:339-345`), and the world `ExposeData` deep-saves it as `requests` (`Source/Intercolony/Core/IntercolonyWorldComponent.cs:1161-1195`). Each request deep-saves its nested quotations (`Source/Intercolony/Procurement/PurchaseRequest.cs:276-310`), and quotation timing/price/distance fields are already part of that nested save (`Source/Intercolony/Procurement/PurchaseRequest.cs:101-116`). `NOT FOUND`: `rg -n -i "PurchaseRequest|RfqService|Quotation" Source/Intercolony --glob "*MapComponent.cs"` returned `NO MATCHES`.

The current responses are persisted, but the pending-response state is not. The only request lifecycle states are `Open`, `Expired`, `Cancelled`, and `Ordered`, with `Open` documented as “suppliers have answered and the quotes stand” (`Source/Intercolony/Procurement/PurchaseRequest.cs:8-25`); the request fields have no pending-response collection or due-tick field (`Source/Intercolony/Procurement/PurchaseRequest.cs:165-203`). The exact pending/scheduling absence searches are recorded in sections C and D, and the RFQ-test absence search is recorded in section G.

Adding a pending-response queue that survives a save would change the saved shape, whether the queue is a new world collection or fields/children on `PurchaseRequest`. The current schema constant is `IntercolonyWorldComponent.CurrentSaveVersion = 57`, and its comment explicitly requires bumping the version whenever saved shape changes and adding a migration in `MigrateIfNeeded` (`Source/Intercolony/Core/IntercolonyWorldComponent.cs:25-30`). `MigrateIfNeeded` separately requires safe defaults and says active obligations must never be silently dropped (`Source/Intercolony/Core/IntercolonyWorldComponent.cs:1963-1985`); the existing purchase-request/quotation and procurement-decision migrations show the additive pattern (`Source/Intercolony/Core/IntercolonyWorldComponent.cs:2053-2058`; `Source/Intercolony/Core/IntercolonyWorldComponent.cs:2566-2575`).

## G. RFQ self-test surface

### READ

The suite id is `rfq`, registered in `IntercolonyAllSelfTests.Definitions` to call `IntercolonyRfqSelfTest.Run` (`Source/Intercolony/Debug/IntercolonyAllSelfTests.cs:125-151`). The direct debug action uses the same runner (`Source/Intercolony/Debug/IntercolonyDebugActions.cs:1099-1103`). The covering file is `Source/Intercolony/Debug/IntercolonyRfqSelfTest.cs`, whose `Run` method labels the output “RFQ self-test” and invokes the RFQ checks (`Source/Intercolony/Debug/IntercolonyRfqSelfTest.cs:14-24`; `Source/Intercolony/Debug/IntercolonyRfqSelfTest.cs:36-99`).

What the RFQ checks already assert:

- It creates sampled requests immediately and checks that empty requests explain themselves, quotes have positive price/quantity/lead time, no quote exceeds the request, partial and full quotes occur, and suppliers vary in price and quantity (`Source/Intercolony/Debug/IntercolonyRfqSelfTest.cs:159-244`; `Source/Intercolony/Debug/IntercolonyRfqSelfTest.cs:278-298`).
- It checks a high-tech item against supplier tech capability (`Source/Intercolony/Debug/IntercolonyRfqSelfTest.cs:246-270`).
- It checks forced supplier-delivery and player-pickup terms, logistics/economy explanation rows, and the delivery premium (`Source/Intercolony/Debug/IntercolonyRfqSelfTest.cs:300-367`).
- It checks that generated quotes stay stable once generated and that request expiry/cancellation transitions are one-way (`Source/Intercolony/Debug/IntercolonyRfqSelfTest.cs:374-395`).
- It checks that effective supply pressure reduces RFQ response count, using `GenerateResponses` on synthetic requests (`Source/Intercolony/Debug/IntercolonyRfqSelfTest.cs:9071-9114`; `Source/Intercolony/Debug/IntercolonyRfqSelfTest.cs:9129-9324`).
- Its Stage 8A save/load matrix includes the request collection and verifies that a request and one already-generated quotation survive reload (`Source/Intercolony/Debug/IntercolonyRfqSelfTest.cs:7310-7325`; `Source/Intercolony/Debug/IntercolonyRfqSelfTest.cs:8360-8412`). That is persistence coverage for completed RFQ generation, not for a pending response queue.
- The same file tests delayed procurement proposals, but those are `ProcurementContract` tests rather than Request Goods response-arrival tests (`Source/Intercolony/Debug/IntercolonyRfqSelfTest.cs:3724-3756`; `Source/Intercolony/Debug/IntercolonyRfqSelfTest.cs:4849-4929`).

What cannot be asserted today is “some quotes are visible now and more arrive after later ticks,” distance-biased arrival ordering with non-deterministic variation, or save/load preservation of a pending RFQ response queue. The RFQ tests call `RfqService.CreateRequest` and inspect its already-populated quotes (`Source/Intercolony/Debug/IntercolonyRfqSelfTest.cs:172-197`), and there is no RFQ advancement method or staggered-arrival assertion: `NOT FOUND` for `rg -n -i "Advance.*(Rfq|RFQ|Request)|(Rfq|RFQ|Request).*Advance" Source/Intercolony` and `rg -n -i "stagger|progressive|progress.*arrival|arrival.*progress" Source/Intercolony/Debug/IntercolonyRfqSelfTest.cs`.

## CONCLUDE: attachment map and stage-5 cut

### F11: progressive Request Goods responses

F11 cannot be completed as one narrow implementation slice because the current seams are separate: synchronous construction (`Source/Intercolony/Procurement/RfqService.cs:66-92`), world-time advancement (`Source/Intercolony/Core/IntercolonyWorldComponent.cs:1594-1665`), world persistence (`Source/Intercolony/Core/IntercolonyWorldComponent.cs:1161-1195`), and immediate quote rendering (`Source/Intercolony/UI/MainTabWindow_Intercolony.cs:4900-4971`). Cut it into a vertical slice whose smallest useful first piece is: retain the existing `TryQuote` calculation, enqueue one pending response per eligible settlement at request creation, advance due responses from the world component, show “awaiting” alongside quotes, and round-trip that pending state through save/load. The exact attachment points are:

| Work piece | Attach point |
|---|---|
| Replace the synchronous whole-world call | `Source/Intercolony/Procurement/RfqService.cs:91-92` |
| Split candidate enumeration from one settlement's answer | `Source/Intercolony/Procurement/RfqService.cs:125-198` and `Source/Intercolony/Procurement/RfqService.cs:229-311` |
| Drive due responses during game time | `Source/Intercolony/Core/IntercolonyWorldComponent.cs:1618-1638` for the existing hourly beat, or `Source/Intercolony/Core/IntercolonyWorldComponent.cs:1598-1603` for the world-tick refresh driver |
| Stop pending work on expiry/withdrawal | `Source/Intercolony/Procurement/RfqService.cs:623-638` and `Source/Intercolony/Procurement/PurchaseRequest.cs:246-268` |
| Persist pending entries and request progress | `Source/Intercolony/Procurement/PurchaseRequest.cs:165-203`; `Source/Intercolony/Procurement/PurchaseRequest.cs:276-310`; `Source/Intercolony/Core/IntercolonyWorldComponent.cs:1188-1191` |
| Show partial answers and remaining pending work | `Source/Intercolony/UI/MainTabWindow_Intercolony.cs:4900-4961` and `Source/Intercolony/UI/MainTabWindow_Intercolony.cs:4964-5029` |
| Update creation/arrival notifications | `Source/Intercolony/Procurement/RfqService.cs:94-108` |
| Add the timing/save assertions | `Source/Intercolony/Debug/IntercolonyRfqSelfTest.cs:150-244`, registered under `rfq` at `Source/Intercolony/Debug/IntercolonyAllSelfTests.cs:150-151` |
| Bump and migrate the saved shape | `Source/Intercolony/Core/IntercolonyWorldComponent.cs:25-30` and `Source/Intercolony/Core/IntercolonyWorldComponent.cs:1963-1985` |

The procurement-proposal pattern is the direct design precedent: capture a deterministic one-shot answer, persist a due tick, and apply it from an advancement pass (`Source/Intercolony/Procurement/ProcurementContractService.cs:298-326`; `Source/Intercolony/Procurement/ProcurementContractService.cs:858-878`). F11 should not reuse the current RFQ's single `Rand` scope unchanged if responses are separated in time; the existing scope covers the entire settlement loop (`Source/Intercolony/Procurement/RfqService.cs:149-153`). A delayed implementation needs a persisted deterministic basis for each pending settlement, or an equivalent stable per-settlement seed, so save/load and response order do not change the quote result (`Source/Intercolony/Procurement/RfqService.cs:114-117`).

### F21: economically meaningful market geography

F21 cannot be completed as one formula/UI slice: the plan names multiple logistics inputs and outputs (`docs/PLAYTEST_BATCH_SOURCE_PLAN.md:282-332`), while the current RFQ/order records expose only distance, lead time, delivery boolean, and price explanation (`Source/Intercolony/Procurement/PurchaseRequest.cs:55-86`; `Source/Intercolony/Procurement/PurchaseOrder.cs:77-94`). Cut it into vertical slices; the smallest useful first piece is one shared logistics result for supplier-delivered RFQ offers and their resulting purchase orders: use the existing distance and profile identity to select a named method, cost multiplier, and expected time; persist and display those three values; defer actual route/path cost, provisions, and transport-pod/caravan execution.

| Work piece | Attach point |
|---|---|
| Canonical geographic input | `Source/Intercolony/Market/MarketOpportunityGenerator.cs:389-397` |
| Shared supplier cost calculation | `Source/Intercolony/Market/IntercolonyPricing.cs:442-499`; RFQ enters it at `Source/Intercolony/Procurement/RfqService.cs:533-537` |
| RFQ delivery/method and timing | `Source/Intercolony/Procurement/RfqService.cs:289-295` and `Source/Intercolony/Procurement/RfqService.cs:598-620` |
| Supplier-listing parity | `Source/Intercolony/Procurement/SupplierListingService.cs:226-269` |
| Sales-market parity | `Source/Intercolony/Market/MarketOpportunityGenerator.cs:131-178` and `Source/Intercolony/Market/IntercolonyPricing.cs:371-393` |
| Persist the selected logistics result into a paid purchase | `Source/Intercolony/Procurement/PurchaseOrder.cs:77-94`; construction is `Source/Intercolony/Procurement/PurchaseOrderService.cs:148-197` |
| Show the result before purchase | RFQ rows: `Source/Intercolony/UI/MainTabWindow_Intercolony.cs:4993-5029`; sales-market columns/details: `Source/Intercolony/UI/MainTabWindow_Intercolony.cs:1304-1327` and `Source/Intercolony/UI/MainTabWindow_Intercolony.cs:1374-1383` |
| Test distance/cost/method differences | Current RFQ suite: `Source/Intercolony/Debug/IntercolonyRfqSelfTest.cs:300-367`; current sales travel precedent: `Source/Intercolony/Debug/IntercolonyOrderSelfTest.cs:90-103` |

## Final opinions

- F11 is a persistence-and-scheduling feature whose first credible proof is partial quotes surviving a reload and advancing on later world time; changing only the UI would leave the synchronous behavior intact (`Source/Intercolony/Procurement/RfqService.cs:91-92`; `Source/Intercolony/Core/IntercolonyWorldComponent.cs:25-30`).
- F21 should start with a shared named logistics result rather than a second pricing formula: supplier pricing is already centralized (`Source/Intercolony/Market/IntercolonyPricing.cs:434-499`), while transport capability and method are absent from the RFQ/order shape (`Source/Intercolony/Procurement/PurchaseRequest.cs:55-86`; `Source/Intercolony/Procurement/PurchaseOrder.cs:77-94`) and from the exact searches in section A.
- Staggered RFQ responses would require a save-schema bump. The deciding citation is the `CurrentSaveVersion` comment: it is `57` and requires a bump whenever saved shape changes, plus a `MigrateIfNeeded` step (`Source/Intercolony/Core/IntercolonyWorldComponent.cs:25-30`).
