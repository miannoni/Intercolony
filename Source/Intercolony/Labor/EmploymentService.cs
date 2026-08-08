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
        /// Hires a worker under one of §37's wage structures and one of §42's combat clauses. Only
        /// prepaid takes silver now; periodic structures take nothing at hire and pay at the end of
        /// each period (§38).
        /// </summary>
        /// <param name="clause">
        /// §42's combat clause. **Required, not defaulted**, and for the reason Phase 19 recorded
        /// about employer standing: the clause is a pricing input, and a defaulted pricing input is
        /// how a call site ends up billing a different number than the dialog quoted. The compiler
        /// naming every site is the point.
        /// </param>
        public static EmploymentContract TryHire(
            IntercolonyWorldComponent state, LaborCandidate candidate, int termDays, Map paymentMap,
            out string failReason, WageStructure structure, CombatClause clause)
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

            // 0 means open-ended (§36.4), which is longer than any minimum by definition — so the
            // minimum-term check must not reject it for being small.
            bool openEnded = termDays <= 0;

            if (!openEnded && termDays < candidate.minTermDays)
            {
                failReason = $"{candidate.Name} will not work for less than {candidate.minTermDays} days.";
                return null;
            }

            if (openEnded && structure == WageStructure.Prepaid)
            {
                // There is no whole term to pay up front. Refused rather than silently converted,
                // because the wage structure is a commitment the player made deliberately (§37).
                failReason = "An open-ended contract cannot be prepaid — there is no term to pay for.";
                return null;
            }

            Settlement settlement = IntercolonyMarketAccess.FindSettlement(candidate.settlementId);
            if (settlement == null)
            {
                failReason = $"{candidate.settlementName} no longer exists.";
                return null;
            }

            // The candidate pool is static and outlives a game; the owner check in
            // LaborCandidateService is what keeps it honest, and this is the backstop for any other
            // path that could hand over a stale candidate. A contract built on a Faction from a
            // discarded world saves a reference that cannot resolve, and its pawn carries thing IDs
            // from another world's counter — both of which are silent until the next load.
            if (candidate.faction != null &&
                Find.FactionManager?.AllFactionsListForReading?.Contains(candidate.faction) == false)
            {
                failReason = $"{candidate.Name} is not from this world. Reopen the Labor tab.";
                IntercolonyLog.Warning(
                    $"Refused to hire {candidate.Name}: faction {candidate.faction.Name} is not " +
                    "registered in this game. A stale candidate survived a game change.");
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

            // Open-ended work is priced as the longest engagement rather than as a zero-day one.
            // Passing 0 straight into the formula would hit §36.1's short-term premium and make
            // permanent employment the *most* expensive per day, which is backwards.
            int pricingTerm = openEnded ? LaborCandidateService.MaxTermDays : termDays;

            int dailyWage = LaborCandidateService.DailyWage(
                candidate.pawn, profile, candidate.distanceTiles, pricingTerm,
                EmployerReputationService.ScoreFor(state), clause);

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

            if (upFront > 0)
            {
                LedgerService.Record(LedgerKind.WagePayment, -upFront, candidate.settlementName,
                    $"{candidate.Name}, {termDays}d prepaid");
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
                combatClause = clause,
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
                $"Hired {contract.workerName} from {contract.settlementName} as a {clause.Label()} — " +
                $"{dailyWage} silver/day × {termDays} days, " +
                $"{WageStructureUtility.Explain(structure, dailyWage, termDays)} " +
                $"Arrives in {candidate.travelDays} days.",
                MessageTypeDefOf.PositiveEvent, historical: false);

            IntercolonyLog.Message($"Hired: {contract}");
            return contract;
        }

        /// <summary>
        /// Hires someone who answered a job posting (§35.2, §114).
        ///
        /// Deliberately a separate entry point from <see cref="TryHire"/> rather than an overload
        /// with an optional wage. The two hires are different transactions: a candidate quotes a
        /// price and this method's caller *set* one, so the wage arrives as data rather than being
        /// computed. Folding them together behind a nullable wage would recreate exactly the
        /// hazard Phase 19 recorded — a pricing input easy to omit, and a hire that silently
        /// charges a number the player never saw.
        /// </summary>
        public static EmploymentContract TryHireApplicant(
            IntercolonyWorldComponent state, JobApplicant applicant, JobPosting posting,
            Map paymentMap, out string failReason)
        {
            failReason = null;

            if (state == null || applicant?.pawn == null || posting == null)
            {
                failReason = "No applicant.";
                return null;
            }

            if (paymentMap == null)
            {
                failReason = "No colony to pay from or send the worker to.";
                return null;
            }

            Settlement settlement = IntercolonyMarketAccess.FindSettlement(applicant.settlementId);
            if (settlement == null)
            {
                failReason = $"{applicant.settlementName} no longer exists.";
                return null;
            }

            if (!IntercolonyMarketAccess.IsAccessible(settlement, out string reason))
            {
                failReason = $"{applicant.settlementName} will not deal with you: {reason}.";
                return null;
            }

            // Same backstop as TryHire: never write a faction from a discarded world into a live
            // contract. An applicant survives saves, so it has more chances to go stale than a
            // candidate does.
            if (applicant.faction != null &&
                Find.FactionManager?.AllFactionsListForReading?.Contains(applicant.faction) == false)
            {
                failReason = $"{applicant.Name} is not from this world.";
                IntercolonyLog.Warning(
                    $"Refused to hire applicant {applicant.Name}: faction {applicant.faction.Name} " +
                    "is not registered in this game.");
                return null;
            }

            // The posted wage, not a computed one. This is the whole of §35.2's inversion: the
            // player named the price and the worker accepted it.
            int dailyWage = posting.wageOffered;
            int upFront = WageStructureUtility.UpFrontCost(posting.wageStructure, dailyWage, posting.termDays);

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

            if (upFront > 0)
            {
                LedgerService.Record(LedgerKind.WagePayment, -upFront, applicant.settlementName,
                    $"{applicant.Name}, {posting.termDays}d prepaid from a posting");
            }

            string skills = applicant.SkillSummary();
            Pawn worker = applicant.Release();

            EmploymentContract contract = new EmploymentContract
            {
                id = state.NextId(),
                settlementId = applicant.settlementId,
                settlementName = applicant.settlementName,
                factionName = applicant.factionName,
                pawn = worker,
                employerFaction = worker.Faction ?? applicant.faction,
                originalKind = worker.kindDef,
                destinationMap = paymentMap,
                workerName = worker.LabelShortCap,
                workerSkills = skills,
                dailyWage = dailyWage,
                termDays = posting.termDays,
                combatClause = posting.combatClause,
                wageStructure = posting.wageStructure,
                paidSilver = upFront,
                hiredTick = GenTicks.TicksGame,
                arrivalTick = GenTicks.TicksGame + applicant.travelDays * GenDate.TicksPerDay,
                status = EmploymentStatus.Travelling
            };

            state.AddEmployment(contract);

            // Already pinned as an applicant, but a posting that is closed in the same tick would
            // have discarded them — so the pin is asserted here rather than assumed.
            if (!Find.WorldPawns.Contains(worker))
            {
                Find.WorldPawns.PassToWorld(worker, PawnDiscardDecideMode.KeepForever);
            }

            Messages.Message(
                $"Hired {contract.workerName} from {contract.settlementName} as a {posting.combatClause.Label()} " +
                $"at your posted {dailyWage} silver/day × {posting.termDays} days. " +
                $"Arrives in {applicant.travelDays} days.",
                MessageTypeDefOf.PositiveEvent, historical: false);

            IntercolonyLog.Message($"Hired from posting #{posting.id}: {contract}");
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

                // A released worker walking out under safe passage (§88). The record is already
                // closed; what is left is to notice when they are clear and put them back in their
                // own faction.
                if (contract.status == EmploymentStatus.Severed)
                {
                    AdvanceSafePassage(contract, now);
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
                    NoteDeath(contract);

                    End(contract, EmploymentStatus.Failed,
                        $"{contract.workerName} died before the term ended");
                    continue;
                }

                // Kidnapping does not make the pawn a prisoner or immediately change faction. The
                // raider's tracker is authoritative while the pawn is held; the faction fallback
                // catches the later vanilla recruitment tick that removes them from that tracker.
                if (IsCapturedEmployee(contract))
                {
                    End(contract, EmploymentStatus.Captured,
                        $"{contract.workerName} was captured and taken from the colony");
                    continue;
                }

                if (contract.pawn.Downed)
                {
                    if (!contract.downedNotified)
                    {
                        contract.downedNotified = true;
                        IntercolonyLetters.Send(
                            IntercolonyLetterImportance.Always,
                            "Employee downed — treatment needed",
                            $"{contract.workerName} is down and needs rescue and treatment. Wages continue " +
                            "while they are incapacitated.\n\n" +
                            $"If they die in your service, {contract.settlementName} will expect " +
                            $"{CompensationService.DeathCompensation(contract)} silver in compensation.",
                            LetterDefOf.ThreatSmall, contract.pawn);
                    }
                }
                else
                {
                    // A later downing is a new event with a new risk to the worker and the colony.
                    contract.downedNotified = false;
                }

                // Renewal is offered before expiry, not at it (§115): a worker who would stay says
                // so while there is still time to answer.
                RenewalService.Advance(contract);

                // §44's larger sibling: a worker who has been here long enough, and been treated
                // well enough, asks to stay for good rather than just for another term.
                TransitionService.Advance(IntercolonyWorldComponent.Current, contract);

                // An open-ended contract has no expiry to reach; a dismissal notice does, and
                // reaching it ends the employment the same way a served term would.
                int ends = contract.ServingNotice ? contract.noticeEndTick : contract.endTick;
                bool byNotice = contract.ServingNotice;

                if (ends >= 0 && now >= ends)
                {
                    // QuestPart_Leave restores faction while downed but only gives an exit lord to
                    // pawns who can walk. Keep the employment record alive until vanilla can perform
                    // a real departure. Wages ran normally throughout the agreed term; the lapsed
                    // flag keeps payroll from inventing a new, unagreed extension after it.
                    if (contract.pawn.Downed)
                    {
                        if (!contract.termLapsedNotified)
                        {
                            contract.termLapsedNotified = true;
                            IntercolonyLetters.Send(
                                IntercolonyLetterImportance.Always,
                                "Employee term ended — recovery needed",
                                $"{contract.workerName}'s {(byNotice ? "notice" : "term")} has ended, but they " +
                                "are incapacitated and cannot leave.\n\n" +
                                "Rescue and treat them. Their employment will close through the normal departure " +
                                "as soon as they can walk. The agreed term is over, so no further wages accrue.",
                                LetterDefOf.ThreatSmall, contract.pawn);
                        }

                        continue;
                    }

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
                            IntercolonyLetters.Send(
                                IntercolonyLetterImportance.Chatty,
                                "Employment ended",
                                $"{contract.workerName}'s {(byNotice ? "notice" : "term")} has ended, but they " +
                                "are away from the colony and cannot leave from where they are.\n\n" +
                                "They will go home once they are back on a map. No further wages are owed.",
                                LetterDefOf.NeutralEvent);
                        }

                        continue;
                    }

                    End(contract,
                        byNotice ? EmploymentStatus.Dismissed : EmploymentStatus.Completed,
                        byNotice
                            ? $"{contract.workerName} worked out their notice and left"
                            : $"{contract.workerName} served the full {contract.termDays} days");
                }
            }
        }

        private static bool IsCapturedEmployee(EmploymentContract contract)
        {
            Pawn worker = contract?.pawn;
            if (worker == null)
            {
                return false;
            }

            if (PawnUtility.IsKidnappedPawn(worker))
            {
                return true;
            }

            // KidnappedPawnsTracker eventually recruits a captive, changing faction and removing
            // the tracker entry. A legitimate off-map employee remains in the player faction (and
            // a caravan is explicit), so this is the state left by that vanilla transition.
            return !worker.Spawned && worker.GetCaravan() == null && worker.Faction != null &&
                   worker.Faction != Faction.OfPlayer && worker.Faction != contract.employerFaction;
        }

        /// <summary>
        /// Records a death against the colony's name and settles what §43 says it costs.
        ///
        /// Order matters: the compensation is computed from <c>dailyWage</c>, the combat clause and
        /// the breach count, all of which <see cref="End"/> leaves alone — but it also needs
        /// <c>destinationMap</c> to find silver, and that <see cref="End"/> clears. So this runs
        /// before the record closes, every time.
        /// </summary>
        private static void NoteDeath(EmploymentContract contract)
        {
            IntercolonyWorldComponent state = IntercolonyWorldComponent.Current;

            // §40 lists "preventable death" as a negative signal and §112 asks for injury/death
            // effects. Whether it was preventable is not something this can tell — the penalty is
            // the same either way, which is a known simplification. What §42 *can* tell is whether
            // the player had been drafting them against the clause, and that doubles the bill.
            EmployerReputationService.NoteEmployeeDied(state, contract);
            CompensationService.ClaimOnDeath(state, contract);
        }

        // --- Safe passage (§88, §113) ------------------------------------------------------

        /// <summary>
        /// Ends employment because the worker's own faction went to war, and starts them walking
        /// out without making them an enemy first (§88).
        ///
        /// The quest is deliberately **left running**. It is what marks the worker a lodger, and
        /// letting it live keeps their <c>kindDef</c> safe through the faction changes and keeps the
        /// home faction resolvable for the restore at the end. <see cref="FinishSafePassage"/> ends
        /// it once they are clear.
        /// </summary>
        public static void BeginSafePassage(EmploymentContract contract, bool offMap)
        {
            if (contract == null || !contract.IsOpen)
            {
                return;
            }

            IntercolonyWorldComponent state = IntercolonyWorldComponent.Current;

            // Wages for the days actually worked, exactly as any other ending. The war is not a
            // reason to stiff someone for work already done.
            PayrollService.SettleOnEnd(contract, EmploymentStatus.Severed, state?.LaborDebts, state);
            CompensationService.ClaimOnEnd(state, contract);

            contract.status = EmploymentStatus.Severed;
            contract.outcomeNote =
                $"{contract.factionName} went to war; {contract.workerName} was released";
            contract.ResumeWork();
            contract.refusingWork = false;

            if (offMap)
            {
                // In a caravan: nothing to walk out of, and pulling them out of it would leave them
                // nowhere (docs/LABOR_TECHNICAL_NOTES.md). Hold until they are back on a map.
                contract.safePassage = false;
                contract.safePassageEndTick = -1;
                IntercolonyLog.Message($"Severed (held off-map): {contract}");
                return;
            }

            contract.safePassage = true;
            contract.safePassageEndTick =
                GenTicks.TicksGame + HostilityPolicy.SafePassageDays * GenDate.TicksPerDay;

            StartWalkingOut(contract);

            IntercolonyLog.Message($"Severed (safe passage): {contract}");
        }

        /// <summary>
        /// Sends a released worker on their way, using vanilla for everything except the one thing
        /// vanilla gets wrong for this case.
        ///
        /// <c>LeaveQuestPartUtility.MakePawnsLeave</c> does all the housekeeping a departure needs
        /// and that reimplementing badly is exactly what docs/LABOR_TECHNICAL_NOTES.md warns about:
        /// it clears master and guest status, drops anything carried, and restores the worker's own
        /// faction from the still-running <c>QuestPart_ExtraFaction</c>. It is called with the quest
        /// rather than after ending it, which is what makes that lookup resolve at all.
        ///
        /// The one thing it cannot do is our case: it puts the worker back in a faction that is now
        /// at war and hands them an exit lord under it, so they would walk out as an enemy and be
        /// shot on the way by the colony's own turrets. So the faction is immediately overridden to
        /// none — and because <c>SetFaction</c> ends the pawn's lord, the exit lord has to be made
        /// after that, not before.
        /// </summary>
        private static void StartWalkingOut(EmploymentContract contract)
        {
            Pawn worker = contract.pawn;
            if (worker == null || !worker.Spawned)
            {
                return;
            }

            try
            {
                if (contract.quest != null && !contract.quest.Historical)
                {
                    LeaveQuestPartUtility.MakePawnsLeave(
                        new List<Pawn> { worker }, sendLetter: false, contract.quest);
                }
            }
            catch (System.Exception ex)
            {
                IntercolonyLog.Warning(
                    $"Employment #{contract.id} threw preparing safe passage: {ex}");
            }

            HostilityPolicy.WalkOutFactionless(worker);
        }

        /// <summary>
        /// Watches a released worker until they are gone, then finishes the record.
        ///
        /// Four ways out, all of them stated to the player when they happen: they die, they leave a
        /// caravan and can finally walk, they reach the map edge, or safe passage runs out because
        /// the colony would not let them go.
        /// </summary>
        private static void AdvanceSafePassage(EmploymentContract contract, int now)
        {
            Pawn worker = contract.pawn;
            if (worker == null)
            {
                // Already finished, or the pawn did not survive a load. Nothing left to do.
                contract.safePassage = false;
                return;
            }

            if (worker.Dead || worker.Destroyed)
            {
                // Killed on the way out. §43's bill applies in full: a departing employee under
                // contract is still an employee, and this is precisely the case the letter warned
                // about. NoteDeath must run before the references are cleared.
                NoteDeath(contract);
                FinishSafePassage(contract,
                    $"{contract.workerName} was killed leaving under safe passage");
                return;
            }

            // A caravan member is not spawned but is not gone either — checked first, because the
            // "not spawned" test below would otherwise read as "they made it home".
            if (worker.GetCaravan() != null)
            {
                if (!contract.termLapsedNotified)
                {
                    contract.termLapsedNotified = true;
                    IntercolonyLetters.Send(
                        IntercolonyLetterImportance.Always,
                        "Released employee is away",
                        $"{contract.workerName}'s contract has ended — their faction is at war with you " +
                        "— but they are travelling with one of your caravans and cannot leave from " +
                        "where they are.\n\nThey will go home once they are back on a map.",
                        LetterDefOf.NeutralEvent);
                }

                return;
            }

            if (!worker.Spawned)
            {
                FinishSafePassage(contract, $"{contract.workerName} reached the border and went home");
                return;
            }

            // Back on a map after a caravan: now they can actually walk.
            if (!contract.safePassage)
            {
                contract.safePassage = true;
                contract.safePassageEndTick = now + HostilityPolicy.SafePassageDays * GenDate.TicksPerDay;
                StartWalkingOut(contract);
                return;
            }

            if (now < contract.safePassageEndTick)
            {
                return;
            }

            // Time is up and they are still here — walled in, downed, or simply blocked. The
            // guarantee was for the walk out, not for indefinite neutrality inside the colony.
            contract.safePassage = false;

            // Recorded as conduct, and this is not decoration. Once the record closes, killing the
            // pawn costs nothing — so without this, walling a released worker in for two days would
            // be a free way to dispose of them, strictly cheaper than letting them go. Charging the
            // colony for the detention itself closes that off at the source rather than trying to
            // keep the contract alive to catch a later death.
            EmployerReputationService.NoteSafePassageDenied(IntercolonyWorldComponent.Current, contract);

            // Says "still heading for the border" because that is what actually happens: ending the
            // quest hands them a LordJob_ExitMapBest under their own faction, so they keep walking.
            // What changes is that everything in the colony now counts them as an enemy while they
            // do it. A letter promising an attack that never comes would be worse than no letter.
            IntercolonyLetters.Send(
                IntercolonyLetterImportance.Always,
                "Safe passage expired",
                $"{contract.workerName} did not get clear of the colony in time and has rejoined " +
                $"{contract.factionName}.\n\n" +
                "They are still heading for the border, but they are an enemy now — your turrets and " +
                "any drafted colonist will treat them as one.\n\n" +
                $"{contract.factionName} holds you responsible for not letting a released worker go, " +
                "and other settlements have heard about it.",
                LetterDefOf.ThreatBig, worker);

            FinishSafePassage(contract,
                $"{contract.workerName} was still in the colony when safe passage ran out");
        }

        /// <summary>
        /// Puts the worker back in their own faction and closes the record.
        ///
        /// Ending the quest is what does the faction restore, through the same
        /// <c>QuestPart_Leave</c> path every other departure uses — see
        /// docs/LABOR_TECHNICAL_NOTES.md on why this must not be hand-rolled. By this point the
        /// pawn is either unspawned or standing in no faction, so <c>MakePawnsLeave</c> has nothing
        /// left to do but the bookkeeping.
        /// </summary>
        private static void FinishSafePassage(EmploymentContract contract, string note)
        {
            Pawn worker = contract.pawn;
            Quest quest = contract.quest;

            contract.outcomeNote = note ?? contract.outcomeNote;
            contract.safePassage = false;
            contract.safePassageEndTick = -1;

            try
            {
                // A factionless pawn is not what MakePawnsLeave restores from — it only acts when
                // the pawn is in the player faction. Put them back by hand first, then let the
                // quest end do everything else.
                if (worker != null && !worker.Destroyed && worker.Faction == null &&
                    contract.employerFaction != null)
                {
                    worker.SetFaction(contract.employerFaction);
                }

                if (quest != null && !quest.Historical)
                {
                    quest.End(QuestEndOutcome.Fail, sendLetter: false, playSound: false);
                }
                else if (worker != null && !worker.Destroyed && worker.Faction == Faction.OfPlayer)
                {
                    worker.SetFaction(contract.employerFaction);
                }
            }
            catch (System.Exception ex)
            {
                IntercolonyLog.Warning($"Employment #{contract.id} threw finishing safe passage: {ex}");
            }

            if (worker != null && !worker.Destroyed && contract.originalKind != null &&
                worker.kindDef != contract.originalKind)
            {
                worker.kindDef = contract.originalKind;
            }

            contract.pawn = null;
            contract.quest = null;
            contract.destinationMap = null;

            IntercolonyLog.Message($"Safe passage complete: {contract} — {note}");
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

            // A worker whose homeland declared war does not walk through the gate. The sweep in
            // HostilityPolicy normally catches this an hour after the declaration, well before
            // arrival; this is the backstop for a war declared inside the same hour the worker was
            // due, and it must answer the same way the policy does rather than inventing a second
            // one. §113 is explicit that the two halves must not drift apart.
            if (HostilityPolicy.IsAtWar(contract.employerFaction))
            {
                HostilityPolicy.Sweep(IntercolonyWorldComponent.Current);

                // The sweep releases them and sends the letter. If it somehow did not, fail closed
                // rather than spawn an enemy in the colony.
                if (contract.IsOpen)
                {
                    End(contract, EmploymentStatus.Severed,
                        $"{contract.factionName} went to war; {contract.workerName} turned back");
                }

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
                contract.arrivedTick = GenTicks.TicksGame;

                // An open-ended engagement has no end tick at all (§36.4). Everything that reads
                // endTick already treats -1 as "no deadline", so this needs no special case beyond
                // not setting one.
                contract.endTick = contract.IsOpenEnded
                    ? -1
                    : GenTicks.TicksGame + contract.termDays * GenDate.TicksPerDay;

                // §43 pays for harm the colony did, so what they walked in with does not count.
                // Snapshotted here rather than at hire because the journey is not the colony's
                // responsibility either.
                contract.permanentInjuriesOnArrival = CompensationService.CountPermanentInjuries(worker);

                // Any attack recorded before employment belongs to their previous life. Without
                // this, a mercenary generated mid-firefight would arrive already in breach.
                contract.countedAttackTick = worker.mindState?.lastAttackTargetTick ?? -99999;

                // The pay clock runs from the first day of work, not from hiring: a worker who
                // spent a week on the road has not earned a week's wage.
                PayrollService.BeginPayroll(contract);

                IntercolonyLetters.Send(
                    IntercolonyLetterImportance.Chatty,
                    "Employee arrived",
                    $"{contract.workerName} of {contract.factionName} has arrived from {contract.settlementName} " +
                    $"to work for {contract.termDays} days.\n\n" +
                    $"Skills: {contract.workerSkills}\n" +
                    $"Wage: {contract.dailyWage} silver/day, {contract.paidSilver} silver paid in advance.\n" +
                    $"Terms: {contract.combatClause.LabelCap()}. {contract.combatClause.Explain()}\n\n" +
                    "They can be assigned work and given a bed like a colonist, but they are not one: " +
                    "they belong to their own faction and will leave when the term ends.\n\n" +
                    $"If they die while employed, {contract.settlementName} expects " +
                    $"{CompensationService.DeathCompensation(contract)} silver in compensation.",
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

            // Capture uses the same amount as death because both mean the colony lost the
            // employer's person, but it has its own labels throughout: nobody is reported dead.
            if (status == EmploymentStatus.Captured)
            {
                EmployerReputationService.NoteEmployeeCaptured(
                    IntercolonyWorldComponent.Current, contract);
                CompensationService.ClaimOnCapture(IntercolonyWorldComponent.Current, contract);
            }
            // §43's injury half, for every ordinary ending. Death and capture payouts already
            // cover the loss. Runs here because it needs the pawn and map cleared below.
            else if (status != EmploymentStatus.Failed)
            {
                CompensationService.ClaimOnEnd(IntercolonyWorldComponent.Current, contract);
            }

            // A worker who downed tools is leaving anyway; clear the flag so a stale "refusing"
            // state cannot outlive the contract.
            contract.refusingWork = false;
            contract.refusalReason = WorkRefusalReason.None;

            // Conduct is recorded here rather than at each call site, so no future caller can end
            // an employment without it counting (§40). Quit is already recorded by PayrollService,
            // which knows the arrears figure; Failed covers deaths, recorded at detection.
            IntercolonyWorldComponent standingOwner = IntercolonyWorldComponent.Current;
            if (status == EmploymentStatus.Completed)
            {
                EmployerReputationService.NoteContractCompleted(standingOwner, contract);
            }
            else if (status == EmploymentStatus.Dismissed)
            {
                EmployerReputationService.NoteEarlyDismissal(standingOwner, contract);
            }

            bool endingQuest = quest != null && !quest.Historical;
            try
            {
                if (endingQuest)
                {
                    // Ends the quest, which cleans up its parts, which sends the worker home.
                    // For a captive, the pawn is already off-map: the leave part restores their
                    // employer faction without creating an exit lord, and the kidnap tracker stays.
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
                IntercolonyLog.Error(
                    endingQuest
                        ? $"Employment #{contract.id} for {worker} threw while ending its quest: {ex}"
                        : $"Employment #{contract.id} for {worker} threw while restoring its faction " +
                          $"without a quest: {ex}");

                // The failed path may have left the worker's allegiance unchanged. The full vanilla
                // departure cannot safely be recreated here, but leaving a player-faction pawn
                // behind after the record closes would turn them into a free colonist.
                try
                {
                    if (worker != null && !worker.Destroyed && worker.Faction == Faction.OfPlayer)
                    {
                        worker.SetFaction(contract.employerFaction);
                    }
                }
                catch (System.Exception fallbackEx)
                {
                    // Reference clearing below must still run even if the last-resort restore also
                    // fails; retaining unresolved Scribe references would corrupt every later load.
                    IntercolonyLog.Error(
                        $"Employment #{contract.id} for {worker} could not restore the worker's " +
                        $"faction after employment teardown failed. Original exception: {ex}\n" +
                        $"Faction fallback exception: {fallbackEx}");
                }
            }

            // This is the same sequence vanilla's bed assignment component uses: release through
            // Pawn_Ownership, then invalidate the room assignment display. It sits outside quest
            // teardown so even the fallback path cannot leave a bed permanently reserved.
            try
            {
                Building_Bed ownedBed = worker?.ownership?.OwnedBed;
                worker?.ownership?.UnclaimBed();
                ownedBed?.NotifyRoomAssignedPawnsChanged();
            }
            catch (System.Exception ex)
            {
                // Reference clearing must still win over a broken bed or another mod's component.
                IntercolonyLog.Warning(
                    $"Employment #{contract.id} for {worker} could not release its bed: {ex}");
            }

            if (worker != null && !worker.Destroyed && contract.originalKind != null &&
                worker.kindDef != contract.originalKind)
            {
                worker.kindDef = contract.originalKind;
            }

            // A worker dismissed before arrival was never spawned and is only alive because
            // TryHire pinned them in the world pawn pool. Unpin and discard, or every cancelled
            // hire leaves a pawn the GC has been told never to collect.
            if (status != EmploymentStatus.Captured && worker != null && !worker.Spawned &&
                Find.WorldPawns.Contains(worker))
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
            // Severance sends its own letter from HostilityPolicy, which can say what a generic
            // departure letter cannot: which faction went to war, and what happened to the money.
            // A second letter here would just repeat it worse.
            if (status == EmploymentStatus.Severed)
            {
                return;
            }

            // Capture sends its own consequence-first compensation letter. A generic departure
            // letter would falsely claim that the kidnapped pawn is returning home.
            if (status == EmploymentStatus.Captured)
            {
                return;
            }

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

            IntercolonyLetterImportance importance =
                status == EmploymentStatus.Completed
                    ? IntercolonyLetterImportance.Chatty
                    : status == EmploymentStatus.Dismissed
                        ? IntercolonyLetterImportance.Important
                        : IntercolonyLetterImportance.Always;

            // Deliberately says nothing about refunds. Nothing else in RimWorld or Intercolony
            // refunds anything, so raising the subject is what would make a player expect one.

            // A worker who left the map has no target to look at; one still walking out does.
            if (worker != null && worker.Spawned)
            {
                IntercolonyLetters.Send(importance, label, body, def, worker);
            }
            else
            {
                IntercolonyLetters.Send(importance, label, body, def);
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
