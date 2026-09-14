# Foreman state — Intercolony

Stage: **RUN COMPLETE — CLEAN HALT.** All nine stages closed; every executable requirement done.
Unit: none. Only human-playtest evidence remains, and it is recorded in `docs/PENDING_PLAYTESTS.md`.
Worker: idle/none
Last done: P8.9, `8ad2e16`. Branch `foreman/playtest-finalization-2026-09-13` pushed at `8ad2e16`.
Updated: 2026-09-15 11:35
Foreman load: 2026-09-15 07:35
Foreman: e46c835 · source C:\dev\agent-foreman · https://github.com/Vector-Consulting-IA-Operacional/agent-foreman.git
Fallback: if `Skill(foreman)` is unknown, read `C:\dev\agent-foreman\skill\SKILL.md` and follow it, then re-run its section 0.

<!-- Everything above this line is the header. A fresh session reads only the header. -->

**Nothing is waiting on a delegate. The remaining work is the operator's:** the playtest sittings in
`docs/PENDING_PLAYTESTS.md`, and the release actions this run was told not to perform — no merge to
`main`, no tag, no GitHub release, no Workshop publish. **None was performed.**

## THE RUN — read this before dispatching anything

**`C:\dev\INTERCOLONY_PLAYTEST_FINALIZATION_EXECUTION_PLAN.md` is the authority.** It beats
`docs/PLAYTEST_BATCH_SOURCE_PLAN.md`, `docs/PLAYTEST_CORRECTION_PLAN.md`, every old RECON file, and
every historical progress record **for the findings it covers**. A copy lives at
`docs/PLAYTEST_FINALIZATION_PLAN.md` so workers, which have no chat history, can cite it.

**The previous run is FINISHED AND SHIPPED.** 1.1.0 is on the Workshop, merged to `main`, tagged
`v1.1.0`, released on GitHub. Its stages C/D/E/F/G/H are closed and **must not be resumed**. This
branch starts from `main` at `e79fba0`.

**The operator authorised this run to proceed autonomously, start to finish, without stopping
between stages to ask permission.** Continue whenever another executable unit exists. Stop only for
a genuine Human Blocker: a player-facing product decision the plan does not already make, an
irreversible/external action, a save-compatibility or data-loss risk that cannot be resolved
safely, or two explicit requirements that cannot both be satisfied. If one chain blocks, quarantine
it and keep executing the others.

### IN SCOPE — only these findings

**F04, F06, F16, F19, F20, F21, F23, F24.** F13 and F17 are **not separate slices**; their accepted
semantics fold into the F16 redesign (auto-renew stays visible and directly toggleable; occasional
actions do not clutter the collapsed card). F16 is the newest presentation authority and supersedes
F17's blanket "everything behind `...`" rule.

### FROZEN — documented, never advanced

**F12** (recurring/preprogrammed player caravans) and **F22** (player-supplied reverse labor
market). F21/F24 rapid transport must not become a back door into F12. F23 applies only to hiring
workers **from** the market. The final diff gets searched for both.

### REGRESSION-ONLY — closed, touch only if this run breaks them

F01, F02, F03, F05, F07, F08, F09, F10, F11, F14, F15, F18, F25.

### Standing constraints

Stay on `foreman/playtest-finalization-2026-09-13`. **Never merge to `main`, never tag, never
create a GitHub release, never publish or update the Workshop.** Push regularly. One schema bump
at most (58 → 59) for the P2 labor tranche; a second one comes back to the operator.

### Recon policy — this plan is deliberately on-rails

**Do not open a stage with a repo-wide recon.** Inspect the production files the plan names. Dispatch
Sol read-only **only** for a question the plan marks FOCUSED RECON REQUIRED, and only that question:
under ~10 files, no whole-repo sweeps, no re-reading `DESIGN.md`/the source plan/old RECON files/all
of `PROGRESS.md`, no long standalone recon document unless a real blocker appears. If the named seam
exists and matches the plan, **implement it** — do not ask Sol to redesign a decided feature.

Broaden only when: the named symbol is gone; the assumed vanilla seam is materially different in the
1.6 references; an additive field collides with an existing owner; a mutation proves the seam does
not control the behaviour; or save/pawn-ownership semantics get uncertain enough that guessing could
corrupt a save.

### Serialisation the plan requires

Never two workers on `MainTabWindow_Intercolony_Labor.cs`, on `EmploymentContract.cs` /
`EmploymentEquipment.cs`, or on world schema/migration code. These stages are small enough that
serialising is cheaper than reconciling.

## Stages

| | Stage | Scope | Status |
|---|---|---|---|
| ✅ | P0 — scope lock, baseline, pipeline repair | — | closed, `158f731`. **1605/0/15, exit 0, log CLEAN** |
| ✅ | P1 — F04 Produce Controls | F04 | closed, `d5f65de`. **1626/0/16, exit 0, CLEAN** |
| ✅ | P2 — labor persistence spine | — | closed, `27e7e4c`. **schema 59, labor 72/0/0** |
| ✅ | P3 — F06 apparel policies + bond buyout | F06 | closed, `a688eb0`. **1636/0/16, exit 0, CLEAN** |
| ✅ | P4 — F16 employee-card redesign | F16 (+F13/F17) | closed, `9a1670b`. **1642/0/16, exit 0, CLEAN** |
| ✅ | P5 — F19/F20 contract economics | F19, F20 | closed, `ba35be9`. **1650/0/17, exit 0, CLEAN** |
| ✅ | P6 — F21/F24 rapid logistics + pod hiring | F21, F24 | closed, `e10df9c`. **1660/0/15, exit 0, CLEAN** |
| ✅ | P7 — F23 requested equipment levels | F23 | closed `b6ca829` |
| ✅ | P8 — integration, regression, docs, clean halt | — | closed `8ad2e16` |

## Units — stage P0

| | Unit | Status |
|---|---|---|
| ✅ | P0.1 — a packaged release contains `Patches/`, and cannot silently stop containing it | accepted, `6a7d0b8` |
| ✅ | P0.2 — `PROGRESS.md` gains the 1.1.0 release milestone it never received | accepted, `158f731` |
| ✅ | P0.3 — the plan copy lands in `docs/`, and the scope matrix is recorded | accepted, `158f731` |
| ✅ | P0.4 — whole-suite baseline on a fresh world, before any feature edit | **1605/0/15, exit 0, CLEAN, pawn delta 0** |

## Units — stage P1

| | Unit | Status |
|---|---|---|
| ✅ | P1.0 — recon: the worker gate seam, and the stuff/build-cost helpers | accepted; P1-D1..D5 |
| ✅ | P1.1 — `ProduceLoopRecord` gains its fields; an old record behaves exactly as today | accepted, `3cbafc4` |
| ✅ | P1.2 — the resume-below latch replaces "restart one below target" | accepted, `93f301b` |
| ✅ | P1.3 — assertions for the latch, the band, and the old-record defaults | accepted, `9319acf` |
| ✅ | P1.3b — the band fixture holds 3 units instead of 1 | accepted, `9319acf`. **51/0/0, zero skips** |
| ✅ | P1.4 + P1.4b — allowed materials; availability ranks, never vetoes | accepted, `80aae5c`. **51/0/0** |
| ✅ | P1.5 + P1.5b — assertions for counting and material choice | accepted, `de4129c`. **56/0/0** |
| ✅ | P1.6 — the worker gate: selected pawns and a Construction floor, Produce work only | accepted, `7dbe8d9` |
| ✅ | P1.6b — assertions + mutation for the worker gate | accepted, `4c8b792`. **61/0/0** |
| ✅ | P1.7 — `Maximum Produce target` setting replaces the hardcoded 100 | accepted, `6531d19` |
| ✅ | P1.8a — `Dialog_ProduceControls`: mode, target, resume band | accepted, `7f47b8c` |
| ✅ | P1.8b — the dialog's Workers and Materials sections | accepted, `f4bbef0` |
| ✅ | P1.9 — setter assertions, and the human UI check recorded | accepted, `75c70e5`. **67/0/0** |
| ✅ | P1.10 — the empty-stockpile guard skipped in a rich colony; make it run | accepted, `d5f65de` |

## Units — stage P2

| | Unit | Status |
|---|---|---|
| ✅ | P2.1 — additive labor fields, three enums, schema 58 -> 59 | accepted, `7386ee6` |
| ✅ | P2.1b — move the three schema pins to 59 without weakening them | accepted, `7386ee6`. **1627/0/15** |
| ✅ | P2.2 — round-trip assertions: an old save reads as the safe defaults | accepted, `27e7e4c`. **72/0/0** |

## Units — stage P3

| | Unit | Status |
|---|---|---|
| ✅ | P3.0 — recon: policy exclusions, forced-drop veto, removal observation | accepted; P3-D1..D7 |
| ✅ | P3.1 — settlement honours RefundableQuantity | accepted, `ff01208` |
| ✅ | P3.2 — the two transpilers: policy assignable, optimiser runs, employees only | accepted, `600da80` |
| ✅ | P3.3 — the consent state machine and its one card | accepted, `8673bf0`. **72/0/0** |
| ✅ | P3.5 — marking a buyout when apparel actually leaves | accepted, `08713f6` |
| ✅ | P3.4 — the forced-drop veto | accepted, `d116c05`. **1630/0/18** |
| ✅ | P3.6 + P3.6b — colony gear stays with the colony, at both departure routes | accepted, `1c59781` |
| ✅ | P3.7 + P3.7b — the buyout arithmetic through real settlement | accepted, `67c5284`. **76/0/0** |
| ✅ | P3.8 — F06's human evidence recorded in PENDING_PLAYTESTS | accepted, `a688eb0` |

## Units — stage P4

| | Unit | Status |
|---|---|---|
| ✅ | P4.1 — the collapsed card: portrait, name, type, Auto-renew | accepted, `8137c29` |
| ✅ | P4.2 — the expanded contract table and the stable action stack | accepted, `6ec950c` |
| ✅ | P4.2b — an unmeasured mood must not read as Content | accepted, `6ec950c`. **1637/0/15** |
| ✅ | P4.3 — tooltip cleanup | accepted, `c80b4fe`. **1636/0/16** |
| ✅ | P4.4 — layout assertions, and F16's human evidence recorded | accepted, `9a1670b`. **labor 82/0/0** |

## Units — stage P5

| | Unit | Status |
|---|---|---|
| ✅ | P5.1 — recon: the adjusted construction-cost helper | **not needed** — answered and spot-checked at P1.0 as P1-D5 |
| ✅ | P5.2 — F19 materials: direct inputs for crafted AND constructed goods | accepted, `8c13777` |
| ✅ | P5.2b — the F19 price hierarchy: recent purchase, then market, then base value | accepted, `13bbdd1` |
| ✅ | P5.3 — F20 paid labor: the relevant-workforce model learns Construction | accepted, `e7125ae` |
| ✅ | P5.4 — the P&L replaces the comparison block in the Business view | accepted, `997ec7d`. **1643/0/15** |
| ✅ | P5.5 — the median market benchmark, read-only | accepted, `2916464`. **1642/0/16** |
| ✅ | P5.6 + P5.6b — material-side assertions, two fixtures rebuilt | accepted, `d1e3b60`. **ledger 43/0/0** |
| ✅ | P5.7 — assertions for the labour side and the margin's contents | accepted, `1870277`. **ledger 45/0/0** |
| ✅ | P5.8 — F19/F20 human evidence recorded | accepted, `ba35be9` |

## Units — stage P6

| | Unit | Status |
|---|---|---|
| ✅ | P6.0 — recon: the vanilla drop-pod arrival path, three questions only | accepted; P6-D1..D5 |
| ✅ | P6.1 — F21: settlement rapid-logistics capability, deterministic | accepted, `4c006ae` |
| ✅ | P6.2 + P6.2b + P6.2c — eligibility, retired guards, and the measurement | accepted, `b536091`. **1654/0/15** |
| ✅ | P6.3 — the arrival mode is frozen onto the contract at hire | accepted, `b2a5a3b` |
| ✅ | P6.3b — U4 tolerates the one node an emergency hire adds | accepted, `3c1550c`. **labor 83/0/0** |
| ✅ | P6.4 — a pod hire really arrives by pod | accepted, `15eb3b0`. **1652/0/17** |
| ✅ | P6.5 — assertions and mutations | accepted, `47e135e`. **1660/0/15** |
| ✅ | P6.6 — F21/F24 human evidence recorded | accepted, `e10df9c` |

## Units — stage P7

| | Unit | Status |
|---|---|---|
| ✅ | P7.0 — recon: `LaborProspect.Materialise` and the vanilla gear seams | accepted; P7-D1..D5 |
| ✅ | P7.1 — `LaborEquipmentTierService`: classify a loadout, gate a settlement | accepted, `30b8bc7` |
| ✅ | P7.2 — the posting carries a requested level, and matching honours it | accepted, `9681390`. **1659/0/16** |
| ✅ | P7.4 — `None` strips bondable supplied gear; the bond quotes zero | accepted, `ede4cae`. **1659/0/16** |
| ✅ | P7.5 + P7.5b — the applicant UI shows the tier and the actual gear | accepted, `a1d7009` |
| ✅ | P7.6 — assertions and mutations | accepted, `d29524b`. **1666/0/16** |
| ✅ | P7.7 — F23 human evidence recorded | accepted, `b6ca829`. 14 added, 0 changed |

**P7 is closed.**

## Units — stage P8

| | Unit | Status |
|---|---|---|
| ✅ | P8.1 — recon: §14 seams traced, F12/F22 diff audit | accepted; P8-D1..D3 |
| ✅ | P8.2a — one authority for the refundable bond | accepted, `893e1ae`. labor **89/0/3** |
| ✅ | P8.2b — assertions for the bond authority | accepted, `f80095d`. labor **97/0/0** |
| ✅ | P8.3 — integration human evidence recorded | accepted, `58267e0`. 20 added, 0 changed |
| ✅ | P8.4 — full build + fresh whole suite | **1670/0/17**, CLEAN, delta 0, schema 59 |
| ✅ | P8.5 — `PROGRESS.md`, disposition markers | accepted, `5822740`. 11 markers |
| ✅ | P8.6 — audit: §17 save/load evidence + checklist coverage | accepted; P8-D4, P8-D5 |
| ✅ | P8.7 — save/load evidence for the five unproven Produce fields | accepted, `9ce7518`. **69/0/1** |
| ✅ | P8.8 — the three checklist bullets nobody recorded | accepted, `89c7ab0`. PT1/PT2/PT3 all bit |
| ✅ | P8.8b — final gate: build 0/0, fresh suite **1674/0/19**, CLEAN, delta 0 | passed |
| ✅ | P8.9 — the one save/load gap a person must close | accepted, `8ad2e16` |
| ✅ | P8.10 — clean halt | branch pushed; nothing left executable |

## Decisions

- **2026-09-15 — P8-D1. F12 AND F22 DID NOT LEAK IN.** Sol read the whole `main...HEAD` diff under
  `Source Defs Patches` (Source only; no Defs or Patches changed) and found no player-scheduled
  outbound caravan and no player-colonist labor export. The adjacent new code is
  `EmploymentArrivalTransport`, which is travel **into** the colony. Spot-checked.
- **2026-09-15 — P8-D2. THE CARD SHOWED A REFUNDABLE BOND THE SETTLEMENT WOULD NOT PAY.** Three
  methods answered the same question three ways: `SettleBond` prorates the already-rounded deposit
  through `BondShare`, while the employee card and the consent warning each re-ran `BondFor` on a
  subset and rounded a second time. Items worth 2 and 3 give a bond of 6; buy out the 2 and the card
  reads 3 where settlement pays 4. Citations spot-checked at `EmploymentEquipment.cs:297/622`,
  `MainTabWindow_Intercolony_Labor.cs:1175`, `EmployeeApparelPatch.cs:767`. **`SettleBond` is the
  authority because it is the one that pays.** Fixed in P8.2a; §14.2 step 6 is the plan step that
  required this.
- **2026-09-15 — P8-D4. FIVE NEW PERSISTED FIELDS HAD NO REAL SAVE EVIDENCE, AND TWO LOOKED LIKE
  THEY DID.** Sol audited every `Scribe` line the branch added. Six fields round-trip at non-default
  values and are genuinely proven. But `ProduceLoopRecord.allowedStuff` and `allowedWorkers` are
  never round-tripped at all, and `restrictToSelectedWorkers`, `minConstructionSkill` and
  `IntercolonySettings.maxProduceTarget` are **vacuous**: the only fixture that saves them leaves
  them at their defaults, and `Scribe_Values.Look` omits a default, so the node is never written and
  the load path never runs. A passing round trip at the default value is not evidence. Closed in
  P8.7. **Old-save safety is separately fine** — every absent node lands on a safe default, which
  Sol checked field by field.
- **2026-09-15 — P8-D5. THREE §17 CHECKLIST BULLETS ARE RECORDED NOWHERE.** Everything else is
  either asserted or written down as a human sitting. The exceptions: F04's "target counts only
  finished stored matching products" and "no quality filter exists", and F23's "Any preserves legacy
  behavior". Scope's three bullets (F12 frozen, F22 frozen, closed findings stay closed) are
  established by the P8.1 diff audit and the disposition markers rather than by an assertion, which
  is the right instrument for them.
- **2026-09-15 — P8-D3. MOST OF §14 IS HUMAN-ONLY, AND THAT IS THE HONEST ANSWER.** Sol classified
  each numbered step. §14.1 is almost entirely already asserted. §14.2 steps 2/3/4/7/8 and §14.3
  steps 1/2/4 need a real pawn, the real optimizer, or a real pod landing — a fixture that generated
  them would be indistinguishable from the leak the suite watches for. They go to
  `PENDING_PLAYTESTS.md`, not to a fabricated assertion.

- **2026-09-15 — P7-D1. VANILLA DOES NOT STOP A LOW-TECH SETTLEMENT PRODUCING HIGH-TECH GEAR.** Sol
  recon, spot-checked. Weapon and apparel generation roll the kind's `weaponMoney` / `apparelMoney`
  and filter by tags, price and commonality — there is **no faction-tech ceiling**
  (`PawnWeaponGenerator.cs:55,102`, `PawnApparelGenerator.cs:686,884`); the one faction-tech check in
  apparel generation applies only to free cold-weather layers (`:202,605,786`). So the plan's "a
  low-tech source must not materialise endgame gear" is **ours to enforce**, through the capability
  gate and the classifier. It is not free.
- **2026-09-15 — P7-D2. NEVER enforce the tier with `ValidatorPostGear`. It FAILS OPEN.** Verified at
  `PawnGenerator.cs:694-697`: at attempt 100 vanilla logs an error and sets `ignoreValidator = true`,
  then returns a pawn that did not pass. That is both a player handed gear below the tier they were
  promised AND a logged error, which this project treats as denying a clean run. **Classify after
  `Materialise()`, in our own code, failing closed.**
- **2026-09-15 — P7-D3. Bounded generate-and-classify, small cap, and NO applicant when it fails.**
  Regeneration is opportunistic sampling, not a guarantee — a kind whose eligible pool has no
  qualifying gear can never produce it, and full pawn generation is expensive by this codebase's own
  comments. Failing to find one is the scarcity the plan asks for, not an error.
- **2026-09-15 — P7-D4. A rejected candidate must be DISCARDED, and `Destroy()` alone leaks it.**
  An uncontained destroyed pawn passes itself back into `WorldPawns` (`Pawn.cs:2341`,
  `WorldPawns.cs:200,261`), and this suite reports a world-pawn delta. Use
  `Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.Discard)` for an uncontained pawn. **Do
  not copy `RemoveAndDiscardPawnViaGC`** from the leader cleanup — it calls `RemovePawn`, which logs
  an error for a pawn that was never contained.
- **2026-09-15 — P7-D5. Classification reads equipped weapons and worn apparel ONLY.** Inventory is
  deliberately excluded from the equipment bond (`EmploymentEquipment.cs:144`), so counting it toward
  a tier would break the premise that the existing bond prices exactly what the tier promised.
- **2026-09-15 — P6-D7. PROCESS LAPSE, recorded rather than buried: `b2a5a3b` was committed with a
  red assertion.** The suite run and the `git commit` were chained in one shell command, so the
  commit landed before the result could be read. The failure itself is the predicted and correct
  consequence of P6.3 — `U4 emergency hire has the ordinary save shape` reports
  `emergency-only [arrivalTransport]`, because an emergency pod hire now writes `DropPod` while an
  ordinary hire keeps the `Conventional` default that Scribe omits. P2.1b deliberately put that node
  in `expectedNodes` rather than the tolerated list, with a comment saying a later stage would have
  to move it; this is that stage. **Run the suite, read it, then commit — never in one command.**
- **2026-09-14 — P6-D6. MEASURED, AND THE CONSTANTS STAY. The alarm I raised was half right.**
  One world produced zero pod-capable candidates and an empty emergency market, which looked like
  F24's old defect returning — a standing rule here records that its original two-day window was five
  times smaller than the nearest settlement in the world, and the plan specified two days again
  without looking. So the suites were made to MEASURE instead of guess:

  > world settlement capability: eligible settlements 52; drop-pod capable 18/52 (34.6%);
  > by tech tier [Neolithic: 23 total, 0 capable; Industrial: 29 total, 18 capable]
  >
  > direct-hire travel-day distribution: min 6d, median 9d, max 16d; at or under 2d: 0/15 candidates

  **The capability model is healthy** — a third of settlements can send pods, no neolithic one can —
  and the empty world was pool sampling variance, not a broken generator: the next run had 4 of 15
  candidates qualify. **`EmergencyConventionalMaxDays` genuinely never fires, and stays at 2 anyway.**
  The nearest settlement observed is six days out, and a six-day caravan is not an answer to an
  imminent threat; charging a 4x premium for someone who arrives next week is worse than offering
  nothing. Pods carry this feature, which is what pods are for. Both measurements stay in the suites
  so the next person can see the distribution rather than re-deriving it.
- **2026-09-14 — P6-D1. The pod path is `TryFindSafeLandingSpotCloseToColony` → `ActiveTransporterInfo`
  → `ThingOwner.TryAdd` → `DropPodUtility.MakeDropPodAt`, and NOT `DropThingsNear`.** Sol recon,
  citations spot-checked. `DropThingsNear` silently falls back to a random walkable cell when its
  search fails (`DropPodUtility.cs:40-60`), which is exactly the silent downgrade the plan forbids.
  `MakeDropPodAt` removes contained world pawns from `WorldPawns` ITSELF (`DropPodUtility.cs:17`), so
  **do not pre-remove or unpin the employee** — that is the ordinary path's job, not this one's.
- **2026-09-14 — P6-D2. MY ASSUMPTION WAS WRONG AND THE RECON CAUGHT IT: a travelling employee is
  NOT yet a player-faction quest lodger.** The employment quest and `SetFaction(Faction.OfPlayer)`
  happen only AFTER the ordinary physical spawn (`EmploymentService.cs:907-909`, verified). So the
  pod arrival cannot assume lodger status on the way down; it must do the same post-spawn work the
  ordinary path does, in the same order, once the pawn is actually out of the pod.
- **2026-09-14 — P6-D3. ARRIVAL IS TWO-PHASE, and it needs no new persisted field.** The pod takes
  ~110 ticks to open and vanilla gives no completion callback, so declaring the employee arrived when
  the pod is launched would start payroll on someone still in the air. Instead: `Advance` launches
  the pod for a due `DropPod` contract and leaves the contract `Travelling`; on a later pass it sees
  `status == Travelling && pawn.Spawned` and runs the existing finish path. A second pod cannot be
  launched mid-descent because the pawn is no longer in `WorldPawns` and is held by the pod's
  container. A save during the descent is safe — the pod is a real saved Thing.
- **2026-09-14 — P6-D4. ON PREFLIGHT FAILURE, DO NOT CALL `End(...Failed...)`.** For a live,
  never-arrived, unspawned worker it removes and DISCARDS the pawn (`EmploymentService.cs:1128-1129`,
  verified) — the player would pay for an emergency hire and get a deleted pawn. Preservation means
  leaving the contract open, `arrivedTick` untouched, the pawn pinned, and surfacing a technical
  failure for retry.
- **2026-09-14 — P6-D5. Once `MakeDropPodAt` begins there is no rollback.** If the skyfaller insert
  fails, `SkyfallerMaker` destroys the transporter and its contents; if placement fails at open, the
  container clear destroys what remains. Everything checkable is therefore checked BEFORE `TryAdd`:
  a valid landing cell, the real `destinationMap` present and `IsPlayerHome` (never a silent retarget
  to another home map), the pawn alive, undestroyed, unspawned, uncaptured, not in a caravan, not
  held elsewhere, still in `WorldPawns`, and the employer not at war.
- **2026-09-14 — P3-D1. Two transpilers, and they must be CALL-TARGET swaps, not offset surgery.**
  Sol recon found both exclusions and I checked both. The Assign tab's is UI-only:
  `PawnColumnWorker_Outfit.DoCell` gates on `pawn.IsQuestLodger()` at `:41` and renders
  "Unchangeable", while the vanilla dropdown sits in the `else` at `:50`; the setter itself is
  ordinary. The behavioural one is `JobGiver_OptimizeApparel.TryGiveJob`, `if (pawn.IsQuestLodger())
  return null;` at `:68-70`. A postfix cannot express either allowance — `DoCell` is `void` so a
  postfix cannot make the skipped `else` run, and a `TryGiveJob` postfix receives the early `null`
  with no way to resume the optimiser. **Each transpiler finds the `IsQuestLodger` CALL instruction
  and swaps the target for an Intercolony helper returning `IsQuestLodger(p) && !IsEmployee(p)`.**
  Matching on the call target rather than an IL offset is what keeps it from breaking on the next
  RimWorld patch. Every other quest lodger stays unchangeable.
- **2026-09-14 — P3-D2. `boughtOutQuantity` currently does NOTHING, and settlement must consume
  `RefundableQuantity`.** Sol caught this and I verified it: `MatchQuantity` caps at `record.quantity`
  (`EmploymentEquipment.cs:443,448`) and the settlement loop totals `record.quantity` (`:318`), so
  incrementing the bought-out count would not reduce a refund by one silver. **The numerator caps at
  `RefundableQuantity`; the denominator stays `record.quantity`.** That is what makes the arithmetic
  right: a record of 3 with 1 bought out, whose worker returns the other 2, refunds two thirds of the
  bond and forfeits the third the colony kept. Capping the denominator too would refund the full bond
  and give the gear away free.
- **2026-09-14 — P3-D3. `TryDrop` cannot tell consent from theft, so it is an observer, not a gate.**
  `Pawn_ApparelTracker.TryDrop(Apparel, out Apparel, IntVec3, bool)` (`RimWorld/Pawn_ApparelTracker.cs:503`
  — the file is under RimWorld, not Verse) is the one method every successful worn-apparel removal
  passes through, once per item, and a postfix sees the pawn, the item and the result. But the Gear
  tab, a forced Wear, ForceTargetWear and `DropAll` all reach it too. **So the buyout is marked only
  when the contract's consent is already Allowed, or when a forced-drop confirmation set a one-shot
  marker.** Nothing is inferred from the drop alone.
- **2026-09-14 — P3-D4. Consent is decided at the optimiser, not at the tracker.** A postfix on
  `JobGiver_OptimizeApparel.TryGiveJob` CAN work where a gate cannot, because it inspects the job
  vanilla decided to return rather than resuming skipped logic: Pending suppresses the job and raises
  the card once per contract; Denied suppresses silently and never prompts again; Allowed lets it
  through. That is the "one meaningful consent moment" the plan asks for.
- **2026-09-14 — P3-D5. Forced drops are vetoed at the ordered-job seam.**
  `Pawn_JobTracker.TryTakeOrderedJob` runs before vanilla marks the job `playerForced`, so the prefix
  must not read that flag. It covers Gear-tab drop, right-click drop, forced Wear, ForceTargetWear
  and Equip-replacement. **`Designator_Strip.DesignateThing` is a separate route and needs its own
  prefix** — by the time `Pawn.Strip` runs it is a batch and too late for a clean veto. Odyssey's
  outfit stand (`JobDriver_UseOutfitStand.DoTransfer`) is a third. Patching the trackers instead is
  NOT a universal veto: replacement jobs assume the removal succeeded.
- **2026-09-14 — P3-D6. Vanilla's "bonded" test is unrelated to ours.**
  `EquipmentUtility.QuestLodgerCanUnequip` blocks only biocoded and bladelink gear; ordinary
  Intercolony-bonded equipment is droppable today. We are adding an economic consequence, not
  borrowing a vanilla prohibition.
- **2026-09-14 — P3-D7. Duplicate originals have no durable identity.** A record stores def + stuff +
  quality only, and matching uses exactly those three. Two identical shirts are indistinguishable, so
  marking a buyout must pick deterministically — the first record matching the tuple with
  `RefundableQuantity > 0` — rather than pretending to know which physical item left.
- **2026-09-14 — P1-D1. ONE Harmony prefix on `GenConstruct.CanConstruct(Thing, Pawn, bool, bool,
  JobDef)` (`reference/decompiled/RimWorld/GenConstruct.cs:261`) gates both construction paths.**
  Sol recon established it and I spot-checked every citation. The `(Thing, Pawn, WorkTypeDef, bool,
  JobDef)` overload at `:251` delegates to it at `:258`, so resource delivery to blueprints
  (`WorkGiver_ConstructDeliverResourcesToBlueprints.cs:32`) and to frames (`…ToFrames.cs:28`) both
  arrive there, and frame finishing calls it directly with `checkSkills: true`
  (`WorkGiver_ConstructFinishFrames.cs:39`). Rejecting is `__result = false` + skip original; vanilla
  turns that into `false` from `HasJobOnThing` or `null` from `JobOnThing`, with no log spam. Use
  `JobFailReason.Is(...)` on rejection, which is vanilla's own idiom on that seam
  (`GenConstruct.cs:307`), so a forced order explains itself.
- **2026-09-14 — P1-D2. The selected-worker list gates delivery AND finishing; the minimum
  Construction skill gates only what vanilla is already skill-checking.** The prefix applies the
  worker list on every Produce-owned call, because "only these pawns work this program" means the
  program. It applies the skill floor only when `checkSkills` is true — which is exactly vanilla's
  own rule for `constructionSkillPrerequisite` (`GenConstruct.cs:301-309`), and it means a hauler
  bringing wood under the Hauling work type is not blocked by a build-quality floor.
- **2026-09-14 — P1-D3. The prefix must early-out before doing anything.** `CanConstruct` is called
  across work scans, so the guard order is: `t is Blueprint or Frame` (type test), then the map's
  `ProduceLoopMapComponent` is non-null with at least one loop, then `Find(t.Position)` matches.
  Ordinary construction reaches the second test and leaves. That satisfies "ordinary construction is
  observably unchanged" in the sense that matters — results and vanilla policy are untouched.
- **2026-09-14 — P1-D4. Accepted limitation: blocking-work jobs bypass the gate.** Vanilla returns
  `HandleBlockingThingJob` *before* `CanConstruct` (blueprints `:28-30`, frames `:24-26`, finish
  `:35-37`), so a non-selected pawn may still clear a plant or rubble blocking a Produce cell. That
  is not producing, so it is left alone rather than patched at four more call sites.
- **2026-09-14 — P1-D5. The vanilla stuff helpers, verified:**
  `GenStuff.AllowedStuffsFor(BuildableDef, TechLevel, bool)` (`GenStuff.cs:121`, yields nothing when
  `!MadeFromStuff`); `stuffDef.stuffProps.CanMake(thingDef)` (`Verse/StuffProperties.cs:102`, throws
  on a null stuff — guard it); and `thingDef.CostListAdjusted(stuffDef)`
  (`RimWorld/CostListCalculator.cs:85`) for the concrete adjusted material list. **Its returned list
  is cached — never mutate it.** With `errorOnNullStuff: false` and a null stuff it can build a
  `ThingDefCountClass(null, …)`, so never pass that combination. P5 reuses the same helper.
- **2026-09-14 — P1-D6. `ProduceLoopRecord` needs no save-schema bump.** It lives in the MAP save
  block via `ProduceLoopMapComponent`, independent of `IntercolonyWorldComponent`'s ladder. New
  nodes are simply absent in old records, so each carries an explicit scribe default equal to
  today's behaviour — and `resumeBelow` uses **-1 for "never set"**, never 0, because 0 is a
  legitimate player choice. An unset record derives `targetCount - 1`, which is exactly what the
  released build does.
- **2026-09-14 — P1-D8. DEVIATION FROM THE PLAN, with evidence. Rule 6.7's step 5 — "if no allowed
  stuff can currently satisfy the stuff requirement, do not start a new blueprint yet" — was
  implemented literally and it broke the feature.** The produce suite went 51/0/0 → **36/10/5**:
  "a target above stock still produces", "a zero target produces without limit", "resuming places a
  blueprint again", "pausing leaves work already under way alone", "re-blueprints an empty cell" and
  the F07 capture assertion all failed with "blueprint appeared no", because a fresh colony has no
  stored wood and `ResourceCounter` therefore reported zero for every candidate.
  Two independent reasons the literal rule is wrong: `ResourceCounter` counts only what is **in
  storage**, so wood lying on the ground — which a pawn will happily haul to a blueprint — reads as
  zero; and vanilla's own contract is that you place a blueprint and pawns deliver later, so an
  unfilled blueprint is the feedback the player already expects.
  **Resolution: availability RANKS the allowed materials, it does not veto them.** With stock, the
  choice is unchanged. Without stock, the loop places its blueprint in its current material anyway
  and waits. Nothing is placed only when no allowed stuff can make the product at all, which is a
  broken configuration rather than a shortage. This also keeps F02/F03 regression-only, which the
  literal reading did not.
- **2026-09-14 — P1-D9. The UI must never let the player leave a stuffable loop with an empty
  allowed-material set.** `ResolveStuffForNextCycle` treats "nothing allowed can make this" as the
  one silent do-nothing case, and `CountStoredThings` treats an empty set as "no filter". Neither
  fires today because `Enable` and the PostLoadInit fallback both guarantee at least the loop's own
  stuff. P1.8 keeps that invariant.
- **2026-09-14 — P1-D7. Editing a program clears the resume latch.** Otherwise raising the target
  while latched leaves a program that will not restart until stock falls to a threshold the player
  has already changed.
- **2026-09-14 — P0-D1. `Patches/` shipping is a deliberate release-content decision, recorded in
  `package.ps1` where the allowlist comment demands one.** The four new assertions name literal
  relative paths rather than reading back from `$ReleaseDirectories`, so deleting the allowlist entry
  still trips them; proven by doing exactly that and watching packaging throw at `package.ps1:122`.
  The released 1.1.0 zip is untouched — the check ran under a `-Version 1.1.0-packagingcheck` name
  and its artefacts were deleted.
- **2026-09-14 — P0-D2. The shipped 1.1.0 is missing the area Produce/Pause/Stop designators and the
  Settlement Economy tab**, because both are registered only by the XML patches that never shipped.
  That is a defect in a published build and it is the operator's to decide about; this run repairs
  the pipeline only and does not prepare 1.1.1.
- **2026-09-14 — P0-D3. This FOREMAN.md was rewritten rather than appended to.** The previous run's
  stage narrative, unit tables and defect-gate detail are in the git log on
  `foreman/playtest-batch-2026-09-06` and in `PROGRESS.md`; carrying 1,300 lines of finished-run
  prose forward is exactly what made the old file claim the release was still pending. What survives
  here is the standing rules, the open operator items, and the frozen markers.

## Standing rules the project has already paid for

  - **When a field stops being written, grep every reader.** A `> 0` check treats zero as corruption.
  - **When something stops happening immediately, find everything that assumed it was instant.**
  - **A number chosen without looking at the data the game generates is a guess.** F24's original
    two-day window was five times smaller than the nearest settlement in the world.
  - **The seam nobody asserts is the one between the caller and the service.**
  - **A skip is not evidence** — but a skip can BE the finding, as it was for F24.
  - **A mutation that fails to compile looks exactly like one that found nothing.** Check the anchor
    is unique and the replacement builds; read the run's log, not the summary line.
  - **When a mutation does not bite, suspect the mutation before the assertion.**
  - **A fixture that fails for the wrong reason passes for the wrong reason too.** Assert on the
    branch you meant to exercise, and print the reason in the failure detail.
  - **A green suite whose COUNT dropped is not a green suite — find out why before accepting.**
    Assertion totals vary by world (1601–1606 observed) because some only run when the generated
    world supplies a fixture. **Report zero failures and a range, never one number.**
  - **A fixture that assumes a world shape is flaky, and it will fail on a world that is merely
    small.** Where a world cannot exercise a bound, SKIP with the reason and the measurement.
  - **An oracle that reads state the test itself has already mutated is measuring the wrong world.**
    Snapshot the ranking before the act, not after.
  - **A self-referential skip must fail, not skip.** Three assertions that skipped when their own
    production dependency returned empty now go red instead.
  - **Never let a zero mean unknown.** Say it in words.
  - **A charge the player was never shown is worse than the problem it fixes.**
  - **`package.ps1` refuses a bridge-enabled DLL**, which is correct — rebuild plain before packaging.
  - Use `dev.ps1 bridge -Save`, not `run -Save`, to load a save.
  - Write files with the file tools; PowerShell `Get-Content` + `Out-File` double-encodes UTF-8.
  - The Bash tool's working directory persists between calls — `cd` back to `C:\dev\Intercolony`
    after visiting another repo, or `dev.ps1` will not be found.
  - **Staging by explicit path silently skips NEW files.** `SettlementRapidLogisticsCapability.cs`
    was created by P6.1 and never staged, because that commit added its three *modified* files by
    name. Four commits in a row would have failed to compile in a fresh clone while building
    perfectly on this machine. **Check `git status --untracked-files=all Source/` before every
    commit**, and prove a stage boundary with a throwaway `git clone` + build rather than trusting
    the local tree.
  - **Run the suite, READ the result, then commit — never chained in one shell command.** `b2a5a3b`
    went in red because the run and the commit were one invocation.
  - **Never write a heredoc and launch `codex exec` in the same Bash call, and always redirect
    `< /dev/null`.** The heredoc consumes the shell's stdin, codex inherits it, and the run hangs
    forever on "Reading additional input from stdin..." with no output and no edits. It cost three
    dead dispatches — two of them ten-minute foreground timeouts — before the pattern was visible.
    Write the prompt file in one call, dispatch in the next, with stdin closed.
  - **`dev.ps1` can die on a `Player.log` lock** held by the RimWorld instance it just stopped, and
    the failure looks like a harness fault rather than a test result. The wrapper at
    `…\tmp\suite.ps1` stops the game, waits for the process to be gone and the log to be openable,
    catches the terminating error and retries; a locked run is never reported as a result.

## Open for the operator

- **2026-09-14 — the published 1.1.0 has no `Patches/`.** See P0-D2. The pipeline is fixed on this
  branch; whether a 1.1.1 goes out, and when, is yours.
- **2026-09-13 — `docs/WORKSHOP_DESCRIPTION.bbcode` is stale.** Produce is entirely absent from it.
  Edit notes were prepared and deliberately not applied. Explicitly NOT a blocker for this run.
- **2026-09-09 — a recurring contract whose counterparty becomes inaccessible is cancelled
  silently** (`ContractService.cs:1655`): status plus a `ContractCancelled` timeline record, no
  letter. The contract is already terminal, so a letter would be noise of a different kind. Left
  alone; say if you want it announced.
- **2026-09-08 — `PurchaseOrderService.DeliverToColony` refunds only when ZERO goods were placed.**
  With any non-zero count it calls `Complete` with what was placed, so a partial delivery silently
  completes short and the player pays in full. Pre-existing. Fixing it means deciding what SHOULD
  happen — hold, partial refund, or overflow elsewhere.
- **Manual playtests owed: `docs/PENDING_PLAYTESTS.md`.** Not a blocker for this run; this run adds
  to it rather than clearing it.

## Closed history

**The finalization run's predecessor** — the F01–F25 playtest batch and its correction run — closed
on 2026-09-13 with 1.1.0 merged (`6322edf`), tagged `v1.1.0`, released on GitHub, and uploaded to
the Workshop as item `3780094556` by the operator. Its stage tables, unit tables, defect-gate
narrative and decision log are in the git history of `foreman/playtest-batch-2026-09-06` and in the
milestone records in `PROGRESS.md`. Nothing from it is pending.

Findings closed there: F01, F02, F03, F05, F07, F08, F09, F10, F11, F13, F14, F15, F17, F18, F25.
Findings it deliberately left for newer product direction, now covered by the finalization plan:
F04, F06, F16, F19, F20, F21, F23, F24. Frozen then and frozen now: **F12**, **F22**.
