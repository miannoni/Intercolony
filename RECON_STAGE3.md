# Stage 3 Reconnaissance Seam Map

This is a read-only seam map for F14, F16/F17, F18, and F10. The requirement anchors are the
four sections of `docs/PLAYTEST_BATCH_SOURCE_PLAN.md`: F16/F17 at `:393-418`, F10 at `:424-438`,
F14 at `:442-452`, and F18 at `:468-472`.

Citation convention: within a paragraph or table row, a bare `:N` reference resolves to the full
repository path named in the same evidence block; every such reference is intended as a file:line
citation. Where a block discusses more than one file, the file name is repeated or the full path is
given before the line reference.

The body below records what is present in the repository. Design opinions are isolated in the final
section.

## READ — F14: contract lists and collapsible-entry seams

### A. Selling agreements

The Contracts tab dispatches to `DrawContracts` at `Source/Intercolony/UI/MainTabWindow_Intercolony.cs:350-353`.
The list is built from `state.Contracts` at `:3741`, sorted by `ContractRank` at `:3754-3758`,
and each entry is passed to `DrawContractRow` at `:3792-3799`. The exact one-entry draw method is
`DrawContractRow` at `:4183-4188`.

What the current selling entry exposes:

- Identity: contract id, settlement, quantity, item, and cadence from `ContractIdentity` at
  `:3875-3879`; it is drawn at `:4199-4201`.
- Payment: delivery count, discounted silver per delivery, and discounted total from
  `ContractPaymentSummary` at `:3881-3894`; it is drawn at `:4203-4207`. The visible summary adds
  the raw agreed unit rate only when `DiscountFraction > 0` at `:3887-3891`.
- Status: `ContractStatusText` covers a pending player proposal, offer expiry, active delivery
  progress or next delivery, consecutive-miss warning, renewal offer, war suspension, and terminal
  outcome text at `:3897-3940`. The delivered/missed counts and status are drawn at `:4247-4252`.
- Active estimate: the list prepares active estimates at `:3760-3769`; when one exists, the row
  draws `DrawContractEstimate` at `:4254-4260`. Its measured block reserves rows for revenue
  payable, buying-input cost, wage bill, delivery premium, estimated margin, and the interpretation
  at `:4005-4029`.
- Active buyer-pickup automation: `CanShowContractAutoReady` tests active plus buyer pickup at
  `:4034-4038`; the checkbox and its tooltip are drawn at `:4262-4283`.
- Hover detail: the row tooltip includes settlement/faction, quantity/item/cadence, per-delivery
  payment, total, agreed rate, discount, deadline, and the consecutive-miss breach threshold at
  `:4286-4305`.
- Actions: renewal `Renew`/`Decline` at `:4310-4321`, offer `Accept`/`Decline` at `:4324-4336`,
  and active or suspended `Withdraw` at `:4338-4355`.

Selling rows are not a fixed pixel height. `ContractBaseRowHeight` measures identity, payment, and
status and clamps the base to a minimum of 74 units at `:3967-3977`; `ContractRowHeight` then adds
the optional estimate block and optional auto-ready row at `:4055-4067`. The list calculates one
height per contract before drawing and uses those heights for the scroll view at `:3772-3799`.

There is no expanded argument in `DrawContractRow` (`:4183-4188`), no expanded value in the list's
row call (`:3795-3797`), and no selling-contract expansion field in the list state (`:3162-3164`).
The exact negative search is recorded under “NOT FOUND” below.

### A. Procurement agreements

The Procurement agreement tab dispatches to `DrawProcurementContracts` at
`Source/Intercolony/UI/MainTabWindow_Intercolony.cs:344-347`. The list is assembled from
`state.ProcurementContracts` at `:3189-3199`, sorted by `ProcurementContractRank` at `:3214-3218`,
and each entry is passed to `DrawProcurementContractRow` at `:3234-3240`. The exact one-entry draw
method is `DrawProcurementContractRow` at `:3405-3409`.

What the current procurement entry exposes:

- Identity: contract id, settlement, quantity, item, and cadence from
  `ProcurementContractIdentity` at `:3271-3275`; it is drawn at `:3420-3422`.
- Payment: total cycle count, whole-cycle payment, and agreement total from
  `ProcurementContractPaymentSummary` at `:3277-3283`; it is drawn at `:3424-3428`. The helper
  computes the whole-cycle payment from `unitPrice` and `quantityPerCycle` through
  `IntercolonyPricing.TotalPayment` at `:3286-3289`; it does not print the per-unit rate.
- Status: `ProcurementContractStatusText` exposes waiting for the settlement, a final supplier
  counter, active cycle-in-progress or next-cycle timing, suspension, and terminal outcome text at
  `:3292-3327`. The row prefixes it with completed and failed cycle counts at `:3430-3435`, and
  status colours distinguish pending/counter, suspended, completed, active, and other states at
  `:3330-3355`.
- Active automation: `CanShowProcurementAutoReady` is active-only at `:3357-3360`; its checkbox and
  explanatory tooltip are drawn at `:3438-3456`.
- Actions: pending-proposal `Cancel` at `:3462-3475`, final-counter `Accept`/`Decline` at
  `:3477-3495`, and active or suspended `Withdraw` at `:3498-3513`.

Procurement rows are also dynamic. `ProcurementContractRowHeight` measures identity, payment, and
status, adds the active auto-ready row, and clamps the result to a minimum of 74 units at
`:3377-3392`. The list computes these per-entry heights before opening the scroll view at
`:3222-3239`. There is no expanded argument in `DrawProcurementContractRow` (`:3405-3409`) and no
procurement expansion value in the list fields (`:3162-3164`).

Neither contract list currently has expand/collapse behavior or per-entry expansion state. Both
entries draw their full current content every time their row method is called; the only nearby
list fields are two scroll positions and the proposal-settlement cache at `:3162-3164`.

### B. UI-only expansion state and lifecycle

There is an existing UI-only expansion precedent, but it is a single selected id rather than a
`HashSet`: Relations declares `expandedRelationSettlementId` with a sentinel at
`Source/Intercolony/UI/MainTabWindow_Intercolony.cs:4418-4422`. Relations compares each settlement
id against that field while calculating and drawing rows at `:4455-4474`; clicking the header toggles
the field between the sentinel and the row id at `:4536-4541`; and the expanded height is
`58f + detail` at `:4551-4554`.

The main-tab lifecycle already resets transient UI state in `PreOpen`: it clears the proposal
eligibility cache and the relation expansion at `:216-233`, specifically
`expandedRelationSettlementId = NoExpandedRelation` at `:229-230`. `PostClose` currently resets
market caches and then calls the base implementation at `:235-239`.

The standing lifecycle rule is recorded at `CLAUDE.md:217-218`: “A UI cache must reset at its own
lifecycle boundary. Main-tab windows survive being closed.” Therefore, if F14 adds a contract-id
set or map to this window, its reset belongs in the window's own reset boundary, alongside the
existing `PreOpen` reset block at `:216-233` (or an explicitly chosen close boundary), not in the
row renderer.

The codebase has no existing contract-specific `HashSet`/dictionary expansion pattern. See the
exact no-match search in the “NOT FOUND” section.

### C. Code-testable exceptional selling states

F14 asks for selling entries to start collapsed unless a strong contextual exceptional state needs
attention at `docs/PLAYTEST_BATCH_SOURCE_PLAN.md:442-448`. The current selling lifecycle enum is
`ContractStatus` at `Source/Intercolony/Contracts/RecurringContract.cs:14-31`:
`Offered`, `Active`, `Completed`, `Breached`, `Cancelled`, `Declined`, and `Suspended`.

The real predicates and substates available to a collapse rule are:

| Code-testable state | Existing value/property and current UI evidence |
|---|---|
| Settlement offer awaiting the player | `IsOffer` means `status == Offered` with both decision markers at their sentinels at `RecurringContract.cs:101-111` and `:157-161`; it is ranked first at `MainTabWindow_Intercolony.cs:3858-3864` and exposes `offer expires` at `:3897-3907`. |
| Player proposal awaiting the settlement | `IsPendingPlayerProposal` is the other `Offered` marker combination at `RecurringContract.cs:163-167`; its UI status is `awaiting the settlement's answer` at `MainTabWindow_Intercolony.cs:3897-3902` and it also ranks first at `:3858-3864`. |
| Active routine / next delivery | `IsActive` is `status == Active` at `RecurringContract.cs:169`; the normal text is next-delivery timing at `MainTabWindow_Intercolony.cs:3909-3913`. |
| Active order in progress | `activeOrderId` is the recorded in-flight sales-order id at `RecurringContract.cs:140-141`; active UI distinguishes it from the next-cycle case at `MainTabWindow_Intercolony.cs:3909-3913`. |
| Active consecutive-miss warning | `consecutiveFailures` and the breach threshold of 2 are real fields/constants at `RecurringContract.cs:147-151`; the UI appends “one more miss ends it” when failures are positive at `MainTabWindow_Intercolony.cs:3914-3916`. |
| Renewal decision waiting | `renewalOffered` and `renewalExpiryTick` are persisted at `RecurringContract.cs:121-125`, with a remaining-time property at `:137-138`; the row says there are `d` days to answer at `MainTabWindow_Intercolony.cs:3922-3925`. |
| War suspension | `Suspended` is a non-terminal enum state with `suspendedTick` at `RecurringContract.cs:25-31` and `:113-117`; the UI reports the faction and remaining deliveries at `MainTabWindow_Intercolony.cs:3927-3930`. |
| Terminal history | `Completed`, `Breached`, `Cancelled`, and `Declined` are enum values at `RecurringContract.cs:18-23`; the fallback includes `outcomeNote` at `MainTabWindow_Intercolony.cs:3933-3939`. |

These are the states the current code can test without inventing a new “exceptional” value. The
current rank function already treats offers/pending answers, active agreements, suspended
agreements, and terminal history as separate groups at `MainTabWindow_Intercolony.cs:3858-3873`,
but that ordering is not an expand/collapse policy.

## READ — F16/F17: employee card

### D. Current `DrawEmployeeRow` contents and controls

F16/F17 names pay per day, time remaining, auto-renew state, and worker/combat type as the primary
surface at `docs/PLAYTEST_BATCH_SOURCE_PLAN.md:393-418`. The current row is `DrawEmployeeRow` at
`Source/Intercolony/UI/MainTabWindow_Intercolony_Labor.cs:740-915`.

The row's complete current surface is:

| Lines | What is currently drawn or controlled |
|---|---|
| `:742-747` | Alternating-row and mouse-over highlights. |
| `:749-752` | `EmployeeRowLayout` is applied; `canAutoRenew` is true only for an active, fixed-term worker not serving notice. |
| `:754-755` | First label: `workerName` and `workerSkills`. |
| `:759-769` | Right-aligned combat clause label; appends `{clauseBreaches} BREACHED` when breaches are positive and colours that condition red. |
| `:771-781` | Status-coloured detail string containing settlement name, faction name, `dailyWage/day`, `TermLabel`, wage structure label, `paidSilver`, `StatusLine()`, and—only when `canAutoRenew`—`auto-renew: on` or `auto-renew: off`; it is passed to a fixed 22-unit label at `:780`. |
| `:783-787` | Whole-row employee tooltip registration. The tooltip content is listed below. |
| `:789-795` | Invisible click region over the text; when the pawn is spawned it jumps to and selects the worker through `CameraJumper`. |
| `:797-810` | Live-renewal detection, action-menu tooltip, and the `...` menu button. The button is active when a renewal offer or auto-renew applies. |
| `:813-840` | Menu options: `Renew — {wage} silver a day`, `Let them go at the end of the term`, and `Auto-renew: on/off`; opening the options creates a `FloatMenu`. |
| `:845-855` | If arrears exist, a `Pay {arrearsSilver}` button calls `PayrollService.TryPayArrears`. |
| `:857-866` | A severed worker has no live dismissal control; the right slot draws the `leaving` label and returns. |
| `:871-883` | A live permanent-transition offer draws `Keep them` and `Not now`, then returns. |
| `:890-905` | A live renewal offer draws `Renew {renewalWage}` and `Let go`, then returns. |
| `:910-915` | Otherwise the right action is `Cancel` for travelling workers or `Dismiss` for other workers, calling `ConfirmDismiss`. |

The tooltip registered by the row adds information that is not on the primary surface. It builds
worker/faction, home settlement, skills at hire, term, daily wage, advance paid, combat clause,
clause explanation, and death compensation at `:939-952`; combat incidents and out-of-clause
breaches at `:954-960`; transition eligibility or its blocker at `:962-971`; compensation already
paid at `:973-976`; travelling arrival/cancellation economics at `:978-982`; severance, war, and
safe-passage text at `:983-997`; and remaining term, work/bed/caravan capability, faction ownership,
and the draft-breach warning at `:998-1008`.

The four items named primary by F16/F17 map to existing content as follows: pay/day is in the detail
string at `Source/Intercolony/UI/MainTabWindow_Intercolony_Labor.cs:772-774`; time remaining is supplied by `StatusLine()` at
`Source/Intercolony/Labor/EmploymentContract.cs:372-416` (normal active work uses `RemainingLabel`);
auto-renew state is appended at `Source/Intercolony/UI/MainTabWindow_Intercolony_Labor.cs:775-778`; and the current
worker/combat surfaces are name/skills at `Source/Intercolony/UI/MainTabWindow_Intercolony_Labor.cs:754-755` and combat clause at `Source/Intercolony/UI/MainTabWindow_Intercolony_Labor.cs:759-767`. Settlement,
faction, wage structure, paid amount, tooltip detail, jump behavior, and the action controls are
also present on or behind this row at the lines listed above.

The employee list declares a fixed `EmployeeRowHeight = 52f` at
`Source/Intercolony/UI/MainTabWindow_Intercolony_Labor.cs:28-29`. The list allocates exactly that
height to every row and advances by it at `:253-264`; the visible block is capped by
`Mathf.Min(live.Count * EmployeeRowHeight, inRect.height - 140f)` at `:253-256`.

`EmployeeRowLayout` reserves `ActionWidth = 110f` and `MenuWidth = 28f` at `:705-708`. It places
the two action slots and menu at `:722-730`, always reserves both action slots and the menu at
`:732-734`, and computes the text/click width as `layout.contractActions.x - rect.x - 6f` at
`:735`. Algebraically, with a row width `W`, those reservations give the text region `W - 268`;
that width is a consequence of the exact constants and rect formulas at `:707-735`.

### E. Known detail-line overflow

The current repository records the extreme observation directly: a deliberately long row measured
the detail line at about 1367 units against 720 available, and says the `Widgets.Label` overflow is
a known unfixed F16/F17 risk at `docs/PENDING_PLAYTESTS.md:113-128`. This static pass confirms the
geometry that permits it: the runtime-interpolated `detail` string is assembled at
`MainTabWindow_Intercolony_Labor.cs:771-778` and then sent to a literal 22-unit-high label at
`:780`, while the row itself remains 52 units at `:28` and `:253-264`. The 1367/720 value is
therefore confirmed as a repository-recorded playtest measurement, not independently recomputed
from runtime font metrics here.

The local vanilla reference confirms the relevant APIs: `Text.CalcHeight(string, float)` delegates
to the current font style at `reference/decompiled/Verse/Text.cs:209-213`; ordinary
`Widgets.Label` calls `GUI.Label` at `reference/decompiled/Verse/Widgets.cs:888-910`; and the
ellipsis variant explicitly clamps text before calling `Label` at `:893-896`.

The project constraint is explicit in `CLAUDE.md:74-81`:

> **Text composed at runtime is measured, never boxed.** Any `Widgets.Label` fed by a builder or an interpolated string gets `Text.CalcHeight(text, width)` — never a literal pixel height. `Widgets.Label` neither clips nor scrolls: it paints the whole string from the top-left regardless of the rect, so an oversized body silently overdraws whatever is beneath it.

For all detail text to fit, the row/list allocation would need to measure the detail against the
actual `textWidth` and propagate that measured height into each row's height and the `viewRect`,
which means changing the fixed `EmployeeRowHeight` path at `:253-264`. If the intended surface is
deliberately one line, it needs an explicit truncation path such as the confirmed
`Widgets.LabelEllipses` wrapper at `reference/decompiled/Verse/Widgets.cs:893-896`; the current
literal-height `Widgets.Label` at `:780` is not a fitting or measurement strategy.

## READ — F18: procurement unit-price seam

F18 asks for the effective unit price in procurement agreement displays at
`docs/PLAYTEST_BATCH_SOURCE_PLAN.md:468-472`.

The per-unit value already exists in the persisted model. `ProcurementContract` stores
`quantityPerCycle`, `unitPrice`, `cadenceDays`, and `totalCycles` at
`Source/Intercolony/Procurement/ProcurementContract.cs:135-145`; `unitPrice` is documented as
“Silver paid per unit for every cycle” at `:138-139`. The model's `paymentPerCycle` property calls
the shared calculator with `unitPrice` and `quantityPerCycle` at `:290-296`.

The procurement preparation path selects the agreed unit price at
`Source/Intercolony/Procurement/ProcurementContractService.cs:471-481`, calculates payment per
cycle and total payment through `IntercolonyPricing.TotalPayment` at `:499-502`, and stores the
selected terms at `:503-513`. The agreement-cycle path passes the persisted `contract.unitPrice`
and `contract.quantityPerCycle` to the paid-order service at `:1012-1028`.

The missing presentation is specifically the standing-agreement row. Its visible summary prints
whole-cycle payment and total at `MainTabWindow_Intercolony.cs:3277-3283`, and the row draws that
summary at `:3424-3428`; neither line prints `contract.unitPrice`. A per-unit rate is already
visible elsewhere: the proposal settlement chooser prints `{preview.unitPrice:F2} silver/unit` at
`Source/Intercolony/UI/Dialog_ProposeProcurementAgreement.cs:201-209`, the selected-price control
prints `Unit price` at `:348-360`, and final-counter confirmation includes `Unit price`, payment per
cycle, and total at `MainTabWindow_Intercolony.cs:3551-3565`. Thus the value is not missing from the
model or all procurement UI; it is missing from the persisted agreement-list presentation.

No derivation from a displayed total is required. The authoritative per-unit field is
`ProcurementContract.unitPrice` at `:138-139`; the whole-cycle figure is derived from it and
`quantityPerCycle` by `IntercolonyPricing.TotalPayment` at `:290-296`. The shared arithmetic method
is `IntercolonyPricing.TotalPayment`, whose implementation validates inputs and rounds
`unitPrice * quantity` at `Source/Intercolony/Market/IntercolonyPricing.cs:315-322`. The charged
figure uses the same method in `PurchaseOrderService.TryCreatePaidOrder` at
`Source/Intercolony/Procurement/PurchaseOrderService.cs:264-269`, and the affordability guard uses
it at `:327-359`; the transaction then records the charged amount at `:304-309`.

The standing invariant is recorded at `CLAUDE.md:211-214`: “A displayed figure and a charged figure
come from one calculation.” Here, `IntercolonyPricing.TotalPayment` is the shared arithmetic
authority and `PurchaseOrderService.TryCreatePaidOrder` is the charge boundary; a new list label
would read the stored unit price and must not reconstruct it by dividing rounded totals.

## READ — F10: procurement relationship progression

F10 actually asks procurement to develop an explicit relationship progression analogous to Selling,
conceptually `Stranger → Trusted Customer → Long-term Agreement`, with thresholds and labels
integrated into existing commercial reputation and agreement architecture rather than duplicated.
It also asks repeated buying to build the relationship just as repeated supplying does, so
procurement does not remain the less-developed half of trade; these requirements are at
`docs/PLAYTEST_BATCH_SOURCE_PLAN.md:424-438`.

The repository already has a shared, per-settlement commercial relationship record. `ReputationTier`
has `Untrusted`, `Unproven`, `Known`, `Reliable`, and `Preferred` at
`Source/Intercolony/Reputation/CommercialReputation.cs:10-17`; the record is explicitly a
settlement's opinion of the colony as a trading partner at `:20-33`; its purchase counters are at
`:49-61`; and its score thresholds and player-facing labels are at `:74-106`. `TotalDealings`
includes purchases and purchase cancellations at `:108-111`.

Repeated buying already affects that generic score: `ReputationService` assigns `PurchaseCompleted
= 2` and `PurchaseCancelled = -4` at `Source/Intercolony/Reputation/ReputationService.cs:19-25`,
and the purchase completion/cancellation hooks increment the purchase counters and apply those
adjustments at `:173-196`. The real purchase-order lifecycle calls the completion hook at
`Source/Intercolony/Procurement/PurchaseOrderService.cs:673-680` and the cancellation hook at
`:850-854`. Procurement negotiation also reads the same reputation score and tier label rather
than creating a second meter at `Source/Intercolony/Contracts/IntercolonyNegotiationEvaluator.cs:542-556`.

Selling has a more explicit agreement gate: its service defines minimum reputation 62 and two
completed sales of the exact good at `Source/Intercolony/Contracts/ContractService.cs:141-145`,
checks the reputation threshold at `:887-894`, and checks the completed-sale count at `:692-705`.
Procurement does enforce one standing agreement per supplier/product through
`Source/Intercolony/Procurement/ProcurementContractService.cs:485-492` and
`Source/Intercolony/Core/IntercolonyWorldComponent.cs:540-566`, but that is a
duplicate-relationship guard, not a progression tier.

The finding is therefore only partially represented: generic reputation and purchase events exist,
but the explicit procurement progression, its requested customer/agreement labels, and a
procurement-specific threshold/gating path are not present. The exact negative search for those
names and fields is recorded below.

## READ — G. Self-test surface

The self-test registry is the authoritative list: `reputation`, `contract`, and `cash-flow` are
registered at `Source/Intercolony/Debug/IntercolonyAllSelfTests.cs:144-149`; `labor` is registered
at `:161-162`.

| Finding | Self-test id and file where assertions would attach | What can be asserted | Drawing-only surface that cannot be asserted by the current self-tests |
|---|---|---|---|
| F14 | `contract` → `Source/Intercolony/Debug/IntercolonyContractSelfTest.cs`, registered at `IntercolonyAllSelfTests.cs:146-147`. Existing selling state assertions are at `IntercolonyContractSelfTest.cs:128-171`. | Contract status predicates, outcome notes, stable ids, and any pure collapse-policy/helper function if one is factored out. | Click-to-expand behavior, collapsed-vs-expanded row painting, actual row geometry, and scanability of the rendered lists. |
| F16/F17 | `labor` → `Source/Intercolony/Debug/IntercolonyLaborSelfTest.cs`, registered at `IntercolonyAllSelfTests.cs:161-162`. Existing employment/pay/arrival assertions are at `IntercolonyLaborSelfTest.cs:114-169`. | Daily-wage arithmetic, term/status data, renewal flags, combat-clause data, and any pure measured-layout helper. | Card hierarchy, which controls are on the primary surface or menu, text wrapping/ellipsis, pixel fit, and the 1367-versus-720 visual overflow. |
| F18 | `contract` for agreement terms and `cash-flow` for the charged-cycle path, registered at `IntercolonyAllSelfTests.cs:146-149`. The cash-flow procurement assertion uses `contract.paymentPerCycle` at `IntercolonyCashFlowSelfTest.cs:155-190`. | Stored unit price, quantity, shared per-cycle arithmetic, forecast amount, and charged amount. | Whether the standing agreement row visibly prints “per unit,” where that label sits, and whether supplier comparisons are visually quick. |
| F10 | `reputation` → `Source/Intercolony/Debug/IntercolonyReputationSelfTest.cs`, plus `contract` for procurement agreement transitions; registry entries are at `IntercolonyAllSelfTests.cs:144-147`. Reputation tier assertions are at `IntercolonyReputationSelfTest.cs:58-71` and milestone assertions begin at `:313-340`. | Score/tier transitions, purchase-event counters, milestone records, and any new pure procurement progression/gate. | Customer/agreement label presentation and any visual relationship progression in a window. |

The current relevant self-tests contain no calls to the three row renderers or their row-height
methods. The exact search and result are recorded below, so the drawing-only limitations above are
not guesses about a hidden UI test.

## NOT FOUND ledger

Each item below is a search whose result was no matching line in the searched scope.

- No contract-specific expanded/collapsed field or `HashSet`/dictionary was found:

  `rg -n -i 'expandedContract|collapsedContract|expandedProcurement|collapsedProcurement|contractExpanded|procurementExpanded|HashSet<int>|Dictionary<int, bool>|Dictionary<int,bool>' Source\Intercolony\UI\MainTabWindow_Intercolony.cs Source\Intercolony\UI\MainTabWindow_Intercolony_Labor.cs`

- No explicit selling exceptional-state predicate/name was found:

  `rg -n -i 'exceptional|exceptionalState|needsAttention|needs.*attention|attention.*needed|urgentState|priorityState' Source\Intercolony\Contracts\RecurringContract.cs Source\Intercolony\UI\MainTabWindow_Intercolony.cs`

- No procurement-specific progression threshold, purchase-counter gate, or requested F10 label was
  found in the procurement contract service:

  `rg -n -i 'MinimumReputation|MinimumCompletedOrdersForAgreement|purchasesCompleted|purchaseCancellations|Trusted Customer|Long-term Agreement|Stranger|ProcurementRelationship|CustomerTier|ProcurementTier' Source\Intercolony\Procurement\ProcurementContractService.cs`

- No F10-specific self-test id, requested label, or relationship-progression assertion was found:

  `rg -n -i 'F10|Trusted Customer|Long-term Agreement|Stranger|procurement relationship|purchase relationship|relationship progression' Source\Intercolony\Debug --glob 'Intercolony*SelfTest.cs'`

- No relevant self-test calls the contract or employee row renderers/height methods or the relevant
  drawing APIs:

  `rg -n -i 'DrawContractRow|DrawProcurementContractRow|DrawEmployeeRow|ContractRowHeight|ProcurementContractRowHeight|EmployeeRowHeight|Widgets\.Label|Widgets\.Button|GUI\.color' Source\Intercolony\Debug --glob 'IntercolonyContractSelfTest.cs' --glob 'IntercolonyLaborSelfTest.cs' --glob 'IntercolonyReputationSelfTest.cs' --glob 'IntercolonyCashFlowSelfTest.cs'`

## CONCLUDE — design opinions for cutting stage 3

1. F14 should be cut as a two-renderer contract-list unit with one lifecycle/state subunit: selling
   attaches at `DrawContracts`/`DrawContractRow`
   (`Source/Intercolony/UI/MainTabWindow_Intercolony.cs:3687`, `:4183`) and procurement attaches at
   `DrawProcurementContracts`/`DrawProcurementContractRow` (`:3170`, `:3405`). The Relations single-id
   pattern and the `PreOpen` reset (`:4418-4422`, `:216-233`) are the closest existing shape, but
   the two lists have different optional content and row-height functions (`:4055-4067`, `:3377-3392`).

2. F16/F17 should be one employee-card restructure plus a separate geometry/overflow acceptance
   slice, because the content is assembled in `DrawEmployeeRow`
   (`Source/Intercolony/UI/MainTabWindow_Intercolony_Labor.cs:740-915`) while the fixed 52-unit
   allocation and action reservations live at `:253-264` and `:705-735`. Moving actions to a
   secondary surface without changing measurement would leave the known failure mode in place.

3. F10 is a new procurement relationship behavior/data slice, not a label-only change: it must
   connect the existing purchase hooks and shared reputation
   (`Source/Intercolony/Reputation/ReputationService.cs:173-196`) to procurement-specific progression
   without duplicating the selling gate (`Source/Intercolony/Contracts/ContractService.cs:141-145`).
   F18 is the smaller neighboring procurement-list presentation slice at `DrawProcurementContractRow`
   (`Source/Intercolony/UI/MainTabWindow_Intercolony.cs:3405`) and should reuse the stored unit price
   plus `IntercolonyPricing.TotalPayment`
   (`Source/Intercolony/Market/IntercolonyPricing.cs:315-322`) rather than create a second money path.
