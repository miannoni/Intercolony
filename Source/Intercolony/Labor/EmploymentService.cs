using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Hiring, arrival, expiry and departure for temporary employees (DESIGN.md §109).
    ///
    /// The control model is Strategy A with quest-lodger marking, proven by the Phase 15 spike
    /// and written up in docs/LABOR_TECHNICAL_NOTES.md. The short version:
    ///
    /// * transferring the worker into the player faction is what makes them a usable worker —
    ///   <c>workSettings</c>, <c>drafter</c>, bed ownership and bills all gate on
    ///   <c>Faction.IsPlayer</c>, and no Harmony patch is needed;
    /// * marking them a quest lodger is what keeps that transfer honest — it preserves their
    ///   <c>kindDef</c>, keeps them out of <c>DefaultThreatPointsNow</c>, and restores their
    ///   faction if they die.
    ///
    /// Departure is deliberately vanilla: the quest carries a <c>QuestPart_Leave</c>, so ending
    /// the quest restores the faction and walks the worker off the map through the same code
    /// path lodger quests use.
    /// </summary>
    public static class EmploymentService
    {
        /// <summary>
        /// Hires a worker under one of §37's wage structures. Only prepaid takes silver now;
        /// periodic structures take nothing at hire and pay at the end of each period (§38).
        /// </summary>
        public static EmploymentContract TryHire(
            IntercolonyWorldComponent state, LaborCandidate candidate, int termDays, Map paymentMap,
            out string failReason, WageStructure structure = WageStructure.Prepaid)
        {
            failReason = null;

            if (state == null || candidate?.pawn == null)
            {
                failReason = "No candidate.";
                return null;
            }

            if (paymentMap == null)
            {
                failReason = "No colony to pay from or send the worker to.";
                return null;
            }

            if (termDays < candidate.minTermDays)
            {
                failReason = $"{candidate.Name} will not work for less than {candidate.minTermDays} days.";
                return null;
            }

            Settlement settlement = IntercolonyMarketAccess.FindSettlement(candidate.settlementId);
            if (settlement == null)
            {
                failReason = $"{candidate.settlementName} no longer exists.";
                return null;
            }

            if (!IntercolonyMarketAccess.IsAccessible(settlement, out string reason))
            {
                failReason = $"{candidate.settlementName} will not deal with you: {reason}.";
                return null;
            }

            // Re-price for the term actually chosen. Quoting at the minimum and then billing a
            // longer contract at the same daily rate would sell the short-term premium for free
            // — the same mistake the sales confirmation slider made in Phase 12.
            SettlementEconomicProfile profile = state.GetProfile(settlement);
            int dailyWage = LaborCandidateService.DailyWage(
                candidate.pawn, profile, candidate.distanceTiles, termDays);

            // Only prepaid is charged now. A periodic hire that demanded the full term up front
            // would defeat the point of offering the choice.
            int upFront = WageStructureUtility.UpFrontCost(structure, dailyWage, termDays);

            int available = PurchaseOrderService.CountColonySilver(paymentMap);
            if (available < upFront)
            {
                failReason = $"Not enough silver in storage: {available} of {upFront} needed.";
                return null;
            }

            if (upFront > 0 && !PurchaseOrderService.TryTakeSilver(paymentMap, upFront))
            {
                failReason = "Could not collect the silver.";
                return null;
            }

            // Read the skill summary *before* Release(): releasing nulls the candidate's pawn,
            // and the summary is computed from it. Doing this the other way round froze the
            // string "no skills" into every completed record.
            string skills = candidate.SkillSummary();

            Pawn worker = candidate.Release();
            LaborCandidateService.Take(candidate);

            EmploymentContract contract = new EmploymentContract
            {
                id = state.NextId(),
                settlementId = candidate.settlementId,
                settlementName = candidate.settlementName,
                factionName = candidate.factionName,
                pawn = worker,
                employerFaction = worker.Faction ?? candidate.faction,
                originalKind = worker.kindDef,
                destinationMap = paymentMap,
                workerName = worker.LabelShortCap,
                workerSkills = skills,
                dailyWage = dailyWage,
                termDays = termDays,
                wageStructure = structure,
                paidSilver = upFront,
                hiredTick = GenTicks.TicksGame,
                arrivalTick = GenTicks.TicksGame + candidate.travelDays * GenDate.TicksPerDay,
                status = EmploymentStatus.Travelling
            };

            state.AddEmployment(contract);

            // Until they arrive the worker exists only as a generated pawn held by the contract,
            // and nothing in the game would save them. Park them in the world pawn pool —
            // KeepForever, not Decide: a pawn passed with Decide is fair game for the world pawn
            // GC, which knows nothing about this contract and would happily discard an employee
            // the player has already paid for.
            if (!Find.WorldPawns.Contains(worker))
            {
                Find.WorldPawns.PassToWorld(worker, PawnDiscardDecideMode.KeepForever);
            }

            Messages.Message(
                $"Hired {contract.workerName} from {contract.settlementName} — " +
                $"{dailyWage} silver/day × {termDays} days, " +
                $"{WageStructureUtility.Explain(structure, dailyWage, termDays)} " +
                $"Arrives in {candidate.travelDays} days.",
                MessageTypeDefOf.PositiveEvent, historical: false);

            IntercolonyLog.Message($"Hired: {contract}");
            return contract;
        }

        /// <summary>
        /// Arrivals and expiries. Called on the world component's hourly beat — a worker
        /// arriving or leaving up to an hour late is invisible, and per-tick checks are not
        /// worth the cost (§84).
        /// </summary>
        public static void Advance(List<EmploymentContract> contracts)
        {
            if (contracts == null)
            {
                return;
            }

            int now = GenTicks.TicksGame;

            // Snapshot: ending a contract can mutate the list via state changes elsewhere.
            for (int i = contracts.Count - 1; i >= 0; i--)
            {
                EmploymentContract contract = contracts[i];

                if (contract.status == EmploymentStatus.Travelling && now >= contract.arrivalTick)
                {
                    Arrive(contract);
                    continue;
                }

                if (contract.status != EmploymentStatus.Active)
                {
                    continue;
                }

                // A worker who died or was destroyed while employed. QuestPart_ExtraFaction has
                // already put them back in their own faction; all that is left is to close the
                // record so nothing ticks a corpse.
                if (contract.pawn == null || contract.pawn.Destroyed || contract.pawn.Dead)
                {
                    End(contract, EmploymentStatus.Failed,
                        $"{contract.workerName} died before the term ended");
                    continue;
                }

                if (contract.endTick >= 0 && now >= contract.endTick)
                {
                    // A worker inside a caravan cannot walk home — they are not on any map.
                    // Ending the contract anyway would send them through
                    // LeaveQuestPartUtility.MakePawnLeave, which pulls them out of the caravan
                    // and leaves them unspawned in no faction's territory. Hold instead, and
                    // tell the player once so an overdue employee is not a silent mystery.
                    if (!contract.pawn.Spawned)
                    {
                        if (!contract.termLapsedNotified)
                        {
                            contract.termLapsedNotified = true;
                            Find.LetterStack.ReceiveLetter(
                                "Employment term ended",
                                $"{contract.workerName}'s {contract.termDays}-day term has ended, but they are " +
                                "away from the colony and cannot leave from where they are.\n\n" +
                                "They will go home once they are back on a map. No further wages are owed.",
                                LetterDefOf.NeutralEvent);
                        }

                        continue;
                    }

                    End(contract, EmploymentStatus.Completed,
                        $"{contract.workerName} served the full {contract.termDays} days");
                }
            }
        }

        /// <summary>Puts the worker on the map, in the player faction, as a quest lodger.</summary>
        public static void Arrive(EmploymentContract contract)
        {
            Pawn worker = contract.pawn;
            Map map = contract.destinationMap ?? Find.AnyPlayerHomeMap;

            if (worker == null || worker.Destroyed || worker.Dead)
            {
                End(contract, EmploymentStatus.Failed,
                    $"{contract.workerName} never arrived");
                return;
            }

            // §88 wants a considered policy for a source faction that turns hostile mid-contract,
            // and that policy is Phase 18's to write. What must not happen in the meantime is
            // spawning an enemy combatant inside the colony because the player hired them a week
            // ago, so the contract simply fails at the gate.
            if (contract.employerFaction != null && contract.employerFaction.HostileTo(Faction.OfPlayer))
            {
                End(contract, EmploymentStatus.Failed,
                    $"{contract.factionName} turned hostile; {contract.workerName} never arrived");
                return;
            }

            if (map == null || !Find.Maps.Contains(map))
            {
                // The colony they were hired for is gone. Wages are not refunded — the worker
                // travelled — but the contract cannot continue.
                End(contract, EmploymentStatus.Failed,
                    $"{contract.workerName} had nowhere to arrive");
                return;
            }

            try
            {
                if (!RCellFinder.TryFindRandomPawnEntryCell(out IntVec3 cell, map,
                        CellFinder.EdgeRoadChance_Friendly))
                {
                    cell = DropCellFinder.TradeDropSpot(map);
                }

                if (Find.WorldPawns.Contains(worker))
                {
                    Find.WorldPawns.RemovePawn(worker);
                }

                GenSpawn.Spawn(worker, cell, map);

                contract.quest = MakeEmploymentQuest(contract);
                worker.SetFaction(Faction.OfPlayer);

                // Belt and braces. Lodger status should have stopped ChangeKind from firing at
                // all; if some other mod's patch got there first, this is the cheap correction.
                if (contract.originalKind != null && worker.kindDef != contract.originalKind)
                {
                    IntercolonyLog.Warning(
                        $"{contract.workerName}'s kindDef was rewritten to {worker.kindDef?.defName} " +
                        $"despite lodger status; restoring {contract.originalKind.defName}.");
                    worker.kindDef = contract.originalKind;
                }

                contract.status = EmploymentStatus.Active;
                contract.endTick = GenTicks.TicksGame + contract.termDays * GenDate.TicksPerDay;

                // The pay clock runs from the first day of work, not from hiring: a worker who
                // spent a week on the road has not earned a week's wage.
                PayrollService.BeginPayroll(contract);

                Find.LetterStack.ReceiveLetter(
                    "Employee arrived",
                    $"{contract.workerName} of {contract.factionName} has arrived from {contract.settlementName} " +
                    $"to work for {contract.termDays} days.\n\n" +
                    $"Skills: {contract.workerSkills}\n" +
                    $"Wage: {contract.dailyWage} silver/day, {contract.paidSilver} silver paid in advance.\n\n" +
                    "They can be assigned work, drafted and given a bed like a colonist, but they are not one: " +
                    "they belong to their own faction and will leave when the term ends.",
                    LetterDefOf.PositiveEvent, worker);

                IntercolonyLog.Message($"Arrived: {contract}");
            }
            catch (System.Exception ex)
            {
                IntercolonyLog.Warning($"Employment #{contract.id} failed on arrival: {ex}");
                End(contract, EmploymentStatus.Failed, $"{contract.workerName} could not be received");
            }
        }

        /// <summary>
        /// Ends employment and sends the worker home.
        ///
        /// The departure itself is <c>QuestPart_Leave.Cleanup</c>: ending the quest restores the
        /// worker's faction from the <c>QuestPart_ExtraFaction</c>, clears master/guest state,
        /// drops anything carried, and puts them under a <c>LordJob_ExitMapBest</c>. Doing it by
        /// hand would mean reimplementing <c>LeaveQuestPartUtility</c> less well.
        /// </summary>
        public static void End(EmploymentContract contract, EmploymentStatus status, string note)
        {
            if (contract == null || !contract.IsOpen)
            {
                return;
            }

            contract.status = status;
            contract.outcomeNote = note ?? "";

            Quest quest = contract.quest;
            Pawn worker = contract.pawn;

            // Pay for the days actually worked since the last payday before anything else — the
            // pawn's references are cleared below, and the arrears calculation needs them.
            PayrollService.SettleOnEnd(contract, status, IntercolonyWorldComponent.Current?.LaborDebts,
                IntercolonyWorldComponent.Current);

            // A worker who downed tools is leaving anyway; clear the flag so a stale "refusing"
            // state cannot outlive the contract.
            contract.refusingWork = false;

            try
            {
                if (quest != null && !quest.Historical)
                {
                    // Ends the quest, which cleans up its parts, which sends the worker home.
                    quest.End(status == EmploymentStatus.Completed
                        ? QuestEndOutcome.Success
                        : QuestEndOutcome.Fail, sendLetter: false, playSound: false);
                }
                else if (worker != null && !worker.Destroyed && worker.Faction == Faction.OfPlayer)
                {
                    // No quest to clean up (the worker never arrived, or the quest was lost).
                    // Put them back in their own faction by hand rather than leave a stray
                    // player-faction pawn behind.
                    worker.SetFaction(contract.employerFaction);
                }
            }
            catch (System.Exception ex)
            {
                IntercolonyLog.Warning($"Employment #{contract.id} threw while ending: {ex}");
            }

            if (worker != null && !worker.Destroyed && contract.originalKind != null &&
                worker.kindDef != contract.originalKind)
            {
                worker.kindDef = contract.originalKind;
            }

            // A worker dismissed before arrival was never spawned and is only alive because
            // TryHire pinned them in the world pawn pool. Unpin and discard, or every cancelled
            // hire leaves a pawn the GC has been told never to collect.
            if (worker != null && !worker.Spawned && Find.WorldPawns.Contains(worker))
            {
                Find.WorldPawns.RemoveAndDiscardPawnViaGC(worker);
            }

            // A letter, not a message. Arrival sends one, so departure must too — and a
            // transient corner toast is the wrong weight for "the worker you were relying on is
            // gone": it vanishes, leaves nothing in the history, and is easy to miss entirely
            // while the camera is elsewhere.
            SendDepartureLetter(contract, status, worker);

            // A closed record must not hold live references: the pawn walks off the map and may
            // be garbage-collected out of the world, and a dangling Scribe_References target
            // produces "could not resolve reference" errors on every subsequent load.
            contract.pawn = null;
            contract.quest = null;
            contract.destinationMap = null;

            IntercolonyLog.Message($"Ended: {contract} — {note}");
        }

        private static void SendDepartureLetter(EmploymentContract contract, EmploymentStatus status, Pawn worker)
        {
            string label;
            LetterDef def;

            switch (status)
            {
                case EmploymentStatus.Completed:
                    label = "Employment ended";
                    def = LetterDefOf.NeutralEvent;
                    break;
                case EmploymentStatus.Dismissed:
                    label = "Employee dismissed";
                    def = LetterDefOf.NeutralEvent;
                    break;
                case EmploymentStatus.Quit:
                    label = "Employee walked out";
                    def = LetterDefOf.NegativeEvent;
                    break;
                default:
                    label = "Employment failed";
                    def = LetterDefOf.NegativeEvent;
                    break;
            }

            string body = $"{contract.outcomeNote}.\n\n" +
                          $"{contract.workerName} of {contract.factionName} " +
                          $"({contract.workerSkills}) is returning to {contract.settlementName}.\n" +
                          $"Term: {contract.termDays} days at {contract.dailyWage} silver/day, " +
                          $"{contract.paidSilver} silver paid in advance.";

            // Deliberately says nothing about refunds. Nothing else in RimWorld or Intercolony
            // refunds anything, so raising the subject is what would make a player expect one.

            // A worker who left the map has no target to look at; one still walking out does.
            if (worker != null && worker.Spawned)
            {
                Find.LetterStack.ReceiveLetter(label, body, def, worker);
            }
            else
            {
                Find.LetterStack.ReceiveLetter(label, body, def);
            }
        }

        /// <summary>Whether any employee is currently working. Cheap enough to call from a patch.</summary>
        public static bool AnyActiveEmployee()
        {
            List<EmploymentContract> contracts = IntercolonyWorldComponent.Current?.Employments;
            if (contracts == null)
            {
                return false;
            }

            for (int i = 0; i < contracts.Count; i++)
            {
                if (contracts[i].status == EmploymentStatus.Active)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether this pawn is working under an Intercolony employment contract.</summary>
        public static bool IsEmployee(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }

            List<EmploymentContract> contracts = IntercolonyWorldComponent.Current?.Employments;
            if (contracts == null)
            {
                return false;
            }

            for (int i = 0; i < contracts.Count; i++)
            {
                if (contracts[i].status == EmploymentStatus.Active && contracts[i].pawn == pawn)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The hidden quest that marks the worker a lodger.
        ///
        /// <c>root</c> is not optional: <c>Quest.CleanupQuestParts</c> ends with
        /// <c>if (root.hideOnCleanup)</c>, so a <c>MakeRaw</c> quest with a null root throws a
        /// NullReferenceException the moment it ends — that is, on every dismissal.
        /// </summary>
        private static Quest MakeEmploymentQuest(EmploymentContract contract)
        {
            Quest quest = Quest.MakeRaw();
            quest.root = IntercolonyQuestDefOf.Intercolony_Employment;
            quest.name = $"Employment: {contract.workerName}";
            quest.hidden = true;
            quest.hiddenInUI = true;

            QuestPart_ExtraFaction allegiance = new QuestPart_ExtraFaction
            {
                quest = quest,
                extraFaction = new ExtraFaction(contract.employerFaction, ExtraFactionType.HomeFaction)
            };
            allegiance.affectedPawns.Add(contract.pawn);
            quest.AddPart(allegiance);

            // leaveOnCleanup is what makes ending the quest send the worker home. The letter is
            // suppressed because EmploymentService sends its own, which says why they left.
            QuestPart_Leave departure = new QuestPart_Leave
            {
                quest = quest,
                leaveOnCleanup = true,
                sendStandardLetter = false
            };
            departure.pawns.Add(contract.pawn);
            quest.AddPart(departure);

            Find.QuestManager.Add(quest);
            quest.Accept(null);
            return quest;
        }
    }
}
