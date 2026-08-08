using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Intercolony
{
    /// <summary>
    /// What happens to business already booked when a counterparty goes to war (DESIGN.md §88, §113).
    ///
    /// §88 demands "a dedicated edge-case policy ... before public labor release", and §113 insists
    /// the trade half and the labor half be written together, because *"a policy split across two
    /// phases is how the trade half and the labor half end up contradicting each other."* So both
    /// live in this one file, and both follow one principle:
    ///
    /// **A war ends what has not been performed. Whoever is holding the other side's value when it
    /// breaks out keeps it — and the player is told exactly that, in the letter, at the time.**
    ///
    /// Applied, that gives four outcomes, and the asymmetry between them is the principle showing
    /// through rather than inconsistency:
    ///
    /// * **an employee** is released and walks out under safe passage — nobody is holding anything,
    ///   and a worker under contract is not a combatant;
    /// * **a sales order** is cancelled and costs the player nothing, because a sales order pays on
    ///   delivery and nothing had changed hands;
    /// * **a purchase order** is lost with its prepayment, because the enemy is holding the silver
    ///   and there is nobody left to ask;
    /// * **a supply agreement** is *suspended*, not broken, because it is a relationship rather than
    ///   a transaction, and a war it survives is a disruption rather than a loss.
    ///
    /// Nothing here is silent. Every branch sends a letter naming the money.
    /// </summary>
    public static class HostilityPolicy
    {
        /// <summary>
        /// How long a released employee has to reach the map edge before safe passage lapses. Long
        /// enough to cross any map at a walk; short enough that a worker the player has walled in
        /// does not stay in limbo indefinitely.
        /// </summary>
        public const int SafePassageDays = 2;

        /// <summary>
        /// Checks every open commitment against its counterparty's current relations.
        ///
        /// Polled on the world component's hourly beat rather than hooked to a goodwill event.
        /// A declaration of war is not instantaneous in its consequences — the raid still has to
        /// arrive — so an hour's latency is invisible, and polling means no faction-relations patch
        /// and no dependence on which of several vanilla paths actually flipped the relation.
        /// </summary>
        public static void Sweep(IntercolonyWorldComponent state)
        {
            if (state == null || Faction.OfPlayer == null)
            {
                return;
            }

            SweepEmployments(state);
            SweepSalesOrders(state);
            SweepPurchaseOrders(state);
            SweepContracts(state);
        }

        // --- Labor half (§88, §113) --------------------------------------------------------

        private static void SweepEmployments(IntercolonyWorldComponent state)
        {
            List<EmploymentContract> contracts = state.Employments;
            for (int i = contracts.Count - 1; i >= 0; i--)
            {
                EmploymentContract contract = contracts[i];
                if (!contract.IsOpen || !IsAtWar(contract.employerFaction))
                {
                    continue;
                }

                if (contract.status == EmploymentStatus.Travelling)
                {
                    ReleaseTravellingWorker(contract);
                }
                else
                {
                    ReleaseWorkingEmployee(contract);
                }
            }
        }

        /// <summary>
        /// A hired worker still on the road whose homeland declares war. They turn back.
        ///
        /// Phase 16 shipped this outcome as an admitted placeholder — it failed the contract at the
        /// arrival gate purely to avoid spawning an enemy in the colony. The outcome is the same;
        /// what §113 required was that it be a *decision*, and that the money be accounted for out
        /// loud rather than quietly vanishing. Prepaid wages are not recovered, because the party
        /// holding them is now an enemy and there is nobody to invoice.
        /// </summary>
        private static void ReleaseTravellingWorker(EmploymentContract contract)
        {
            string money = contract.paidSilver > 0
                ? $"\n\n{contract.paidSilver} silver was paid in advance. It is not coming back — the " +
                  "settlement holding it is now an enemy, and there is nobody left to ask."
                : "\n\nNothing had been paid, so nothing is lost.";

            IntercolonyLetters.Send(
                IntercolonyLetterImportance.Always,
                "Employee turned back",
                $"{contract.factionName} is now at war with your colony.\n\n" +
                $"{contract.workerName}, who was travelling here from {contract.settlementName} to work " +
                $"for {contract.termDays} days, has turned back. They never reached the colony." + money,
                LetterDefOf.NegativeEvent);

            EmploymentService.End(contract, EmploymentStatus.Severed,
                $"{contract.factionName} went to war; {contract.workerName} turned back on the road");
        }

        /// <summary>
        /// An employee already living and working in the colony whose homeland declares war.
        ///
        /// §88's requirement is explicit: *"hostile employees should not silently become enemies in
        /// the middle of a bedroom without an intentional design."* This is the intentional design.
        /// The contract ends, and the worker walks out — but they are put into **no faction at all**
        /// for the walk, so nothing in the colony treats them as a target. A factionless pawn is
        /// nobody's enemy, which means turrets hold their fire and drafted colonists do not
        /// auto-engage. Their real faction is restored once they are off the map, by
        /// <see cref="EmploymentService.FinishSafePassage"/>.
        ///
        /// Shooting them anyway is available, and costs what killing an employee costs — which is
        /// the correct price for it.
        /// </summary>
        private static void ReleaseWorkingEmployee(EmploymentContract contract)
        {
            Pawn worker = contract.pawn;
            if (worker == null || worker.Dead || worker.Destroyed)
            {
                EmploymentService.End(contract, EmploymentStatus.Severed,
                    $"{contract.factionName} went to war; {contract.workerName} was already gone");
                return;
            }

            // A worker in a caravan cannot walk to a map edge — they are not on a map. Same hazard
            // as an expired term (docs/LABOR_TECHNICAL_NOTES.md): ending the quest would pull them
            // out of the caravan and leave them nowhere. Hold, and finish when they are back.
            bool offMap = !worker.Spawned;

            IntercolonyLetters.Send(
                IntercolonyLetterImportance.Always,
                "Employee released — war",
                $"{contract.factionName} is now at war with your colony.\n\n" +
                $"{contract.workerName}'s contract ends today. They are not a combatant and will not " +
                "fight you.\n\n" +
                (offMap
                    ? "They are away from the colony and cannot leave from where they are. They will " +
                      "go home once they are back on a map."
                    : $"They are walking out under safe passage and will not be hostile until they are " +
                      $"off the map. They have {SafePassageDays} days to get clear; after that they " +
                      "rejoin their own people, here, armed.\n\n" +
                      "If you kill them on the way out, compensation is owed exactly as if they had " +
                      $"died working for you: {CompensationService.DeathCompensation(contract)} silver.") +
                "\n\nNo further wages are owed.",
                LetterDefOf.ThreatSmall, offMap ? null : worker);

            EmploymentService.BeginSafePassage(contract, offMap);
        }

        /// <summary>
        /// Gives a released worker no faction and an order to leave. Kept here rather than in
        /// <see cref="EmploymentService"/> because it is the §88 policy expressed in code, and
        /// separating them is how the two would drift apart.
        /// </summary>
        public static void WalkOutFactionless(Pawn worker)
        {
            if (worker == null || !worker.Spawned)
            {
                return;
            }

            // Order matters. SetFaction ends the pawn's current lord
            // (Pawn.SetFaction -> GetLord().Notify_PawnLost), so the exit lord has to be made
            // after the last faction change or it is thrown away immediately.
            if (worker.Faction != null)
            {
                worker.SetFaction(null);
            }

            // canDefendSelf, not canFight: LordToil_ExitMapFighting is only entered on
            // Trigger_PawnHarmed, so they walk unless someone shoots them.
            LordMaker.MakeNewLord(null,
                new LordJob_ExitMapBest(LocomotionUrgency.Walk, canDig: true, canDefendSelf: true),
                worker.MapHeld, new List<Pawn> { worker });
        }

        // --- Trade half (§88, §113) --------------------------------------------------------

        /// <summary>
        /// Sales orders die with the war and cost the player nothing but the stock they built.
        ///
        /// A sales order pays on delivery, so at the moment war breaks out neither side is holding
        /// the other's value: the player still has the goods, the buyer still has the silver.
        /// Cancelling is therefore the whole of the correct answer — and explicitly *not* a breach,
        /// because failing to deliver to someone who is shooting at you is not a failure of yours.
        /// No commercial reputation is lost.
        /// </summary>
        private static void SweepSalesOrders(IntercolonyWorldComponent state)
        {
            List<SalesOrder> orders = state.Orders;
            for (int i = orders.Count - 1; i >= 0; i--)
            {
                SalesOrder order = orders[i];
                if (order.IsOpen && IsAtWar(FactionOf(order.settlementId, order.factionName)))
                {
                    ApplyToSalesOrder(order);
                }
            }
        }

        /// <summary>
        /// The sales-order half of the policy, for one order. Public so the self-test can drive the
        /// real transition instead of asserting against a reimplementation of it.
        /// </summary>
        public static bool ApplyToSalesOrder(SalesOrder order, bool sendLetter = true)
        {
            if (order == null || !order.IsOpen)
            {
                return false;
            }

            bool wasCollecting = order.BuyerEnRoute;

            order.status = SalesOrderStatus.Cancelled;
            order.outcomeNote = $"Cancelled: {order.factionName} went to war.";

            if (sendLetter)
            {
                IntercolonyLetters.Send(
                    IntercolonyLetterImportance.Always,
                    "Order cancelled — war",
                    $"{order.settlementName} is now at war with your colony. Your order for " +
                    $"{order.Quantity}x {order.ThingDef?.label ?? "goods"} is void.\n\n" +
                    (wasCollecting ? "Their caravan is not coming. " : "") +
                    "Nothing had been paid, so nothing is lost but the stock you set aside — that is " +
                    "yours to sell elsewhere.\n\n" +
                    "This does not count against you as a supplier.",
                    LetterDefOf.NeutralEvent);
            }

            IntercolonyLog.Message($"Sales order {order.id} cancelled by war with {order.factionName}.");
            return true;
        }

        /// <summary>
        /// Purchase orders are prepaid, so a war takes the silver with it.
        ///
        /// This is the one branch that costs the player real money, and it is deliberate. The mod
        /// already asks the player to carry exactly this risk when they prepay a wage — §37 names it
        /// outright: *"if they die or you change your mind, the silver is spent."* A prepaid order
        /// to a settlement whose goodwill is visibly sliding is the same bet, and goodwill decays
        /// long before it flips, so the player can see it coming and collect early.
        ///
        /// Note this is a statement rather than a new deduction: the silver left storage when the
        /// order was placed. What §113 required was that it not disappear quietly.
        /// </summary>
        private static void SweepPurchaseOrders(IntercolonyWorldComponent state)
        {
            List<PurchaseOrder> orders = state.PurchaseOrders;
            for (int i = orders.Count - 1; i >= 0; i--)
            {
                PurchaseOrder order = orders[i];
                if (order.IsOpen && IsAtWar(FactionOf(order.settlementId, order.factionName)))
                {
                    ApplyToPurchaseOrder(order);
                }
            }
        }

        /// <summary>
        /// The purchase-order half of the policy, for one order. Public for the self-test, which
        /// has to prove the silver is *not* refunded — the one thing a reader of the enum could
        /// reasonably assume from <see cref="PurchaseOrderStatus.SupplierDefault"/> sitting next to it.
        /// </summary>
        public static bool ApplyToPurchaseOrder(PurchaseOrder order, bool sendLetter = true)
        {
            if (order == null || !order.IsOpen)
            {
                return false;
            }

            order.status = PurchaseOrderStatus.LostToWar;
            order.outcomeNote =
                $"{order.factionName} went to war. {order.paidSilver} silver was not recovered.";

            if (sendLetter)
            {
                IntercolonyLetters.Send(
                    IntercolonyLetterImportance.Always,
                    "Order lost to war",
                    $"{order.settlementName} is now at war with your colony. Your order for " +
                    $"{order.quantity}x {order.ItemLabel()} is void.\n\n" +
                    $"{order.paidSilver} silver was paid in advance and will not be returned. Nobody " +
                    "is coming, and there is no one left to ask.",
                    LetterDefOf.NegativeEvent);
            }

            IntercolonyLog.Message(
                $"Purchase order {order.id} lost to war with {order.factionName}; " +
                $"{order.paidSilver} silver forfeited.");
            return true;
        }

        /// <summary>
        /// A standing supply agreement is suspended rather than broken, and resumes if peace comes.
        ///
        /// §88 offers "suspended or cancelled" and this is the one place suspension is clearly
        /// right. §29's whole purpose is that a recurring contract is *plannable* — "a future demand
        /// commitment causes the player to expand capacity" — and a player who built a farm around
        /// eight quadrums of rice should not lose it to one raid. Suspension makes war a disruption.
        ///
        /// The cycle clock is pushed forward by however long the suspension lasted, so no delivery
        /// is lost and none is counted as missed: the player still owes exactly the number of
        /// deliveries they signed for.
        /// </summary>
        private static void SweepContracts(IntercolonyWorldComponent state)
        {
            foreach (RecurringContract contract in state.Contracts)
            {
                Faction faction = FactionOf(contract.settlementId, contract.factionName);

                if (contract.IsActive && IsAtWar(faction))
                {
                    Suspend(state, contract);
                    continue;
                }

                if (contract.status == ContractStatus.Suspended && faction != null && !IsAtWar(faction))
                {
                    Resume(state, contract);
                }
            }
        }

        /// <summary>
        /// Suspends one agreement. Public, and with a letter switch, so the self-test can drive the
        /// real transition and the real clock arithmetic without spraying letters at the player.
        /// </summary>
        public static bool Suspend(IntercolonyWorldComponent state, RecurringContract contract,
            bool sendLetter = true)
        {
            if (contract == null || !contract.IsActive)
            {
                return false;
            }

            contract.status = ContractStatus.Suspended;
            contract.suspendedTick = GenTicks.TicksGame;
            contract.outcomeNote = $"Suspended: {contract.factionName} went to war.";

            // The cycle in flight is withdrawn rather than failed — the player cannot deliver to an
            // enemy, so counting it against them would be punishing them for the war.
            if (contract.activeOrderId != 0)
            {
                SalesOrder order = state?.FindOrder(contract.activeOrderId);
                if (order != null && order.IsOpen)
                {
                    order.status = SalesOrderStatus.Cancelled;
                    order.outcomeNote = "Withdrawn: the agreement is suspended by war.";
                }

                // Cleared so the cycle is re-issued on resume instead of resolved as a failure.
                contract.activeOrderId = 0;
            }

            if (sendLetter)
            {
                IntercolonyLetters.Send(
                    IntercolonyLetterImportance.Always,
                    "Supply agreement suspended",
                    $"{contract.settlementName} is now at war with your colony.\n\n" +
                    $"Your agreement — {contract.quantityPerCycle}x {contract.ItemLabel()} every " +
                    $"{contract.CadenceDays:F0} days, {contract.cyclesCompleted} of {contract.totalCycles} " +
                    "delivered — is suspended, not broken.\n\n" +
                    "No deliveries are due and none will count as missed. If relations recover the " +
                    "agreement resumes with every remaining delivery intact.",
                    LetterDefOf.NeutralEvent);
            }

            IntercolonyLog.Message($"Contract {contract.id} suspended by war with {contract.factionName}.");
            return true;
        }

        /// <summary>Resumes a suspended agreement, moving its clock forward by the outage.</summary>
        public static bool Resume(IntercolonyWorldComponent state, RecurringContract contract,
            bool sendLetter = true)
        {
            if (contract == null || contract.status != ContractStatus.Suspended)
            {
                return false;
            }

            int suspendedFor = Mathf.Max(0, GenTicks.TicksGame - contract.suspendedTick);

            contract.status = ContractStatus.Active;
            contract.outcomeNote = "";

            // Push the clock by the outage, so the player gets a full cycle to make the next
            // delivery rather than waking up already behind.
            contract.nextCycleTick += suspendedFor;
            contract.suspendedTick = 0;

            if (!sendLetter)
            {
                IntercolonyLog.Message($"Contract {contract.id} resumed (silent) after war.");
                return true;
            }

            IntercolonyLetters.Send(
                IntercolonyLetterImportance.Chatty,
                "Supply agreement resumed",
                $"Relations with {contract.settlementName} have recovered and your supply agreement " +
                "is live again.\n\n" +
                $"{contract.CyclesRemaining} deliveries remain — {contract.quantityPerCycle}x " +
                $"{contract.ItemLabel()} every {contract.CadenceDays:F0} days. The next window opens " +
                $"in {contract.DaysUntilNextCycle:F0} days.\n\n" +
                $"It was suspended for {suspendedFor / (float)GenDate.TicksPerDay:F0} days, and the " +
                "schedule has been moved back by the same amount.",
                LetterDefOf.PositiveEvent);

            IntercolonyLog.Message($"Contract {contract.id} resumed after war with {contract.factionName}.");
            return true;
        }

        // --- Shared ------------------------------------------------------------------------

        /// <summary>
        /// Whether this faction is at war with the colony right now.
        ///
        /// **The single definition of "at war" for the whole mod.**
        /// <see cref="IntercolonyMarketAccess.IsAccessible"/> calls this rather than repeating the
        /// test, which is what makes it impossible for the market to keep trading with a faction
        /// this policy is ending contracts over — the specific contradiction §113 warns about.
        /// </summary>
        public static bool IsAtWar(Faction faction)
        {
            if (faction == null || faction.IsPlayer)
            {
                return false;
            }

            // Probe with allowNull first. `Faction.HostileTo` and `PlayerRelationKind` both go
            // through `RelationWith(other)` with allowNull false, which does not fail quietly — it
            // writes a red `Log.Error` and hands back a dummy relation. A world containing a faction
            // with an empty relation table therefore turns every hostility check in the mod into an
            // error in the player's log, and the hourly sweep asks about every settlement.
            //
            // A faction with no recorded relation is not at war with anyone, so answering "no"
            // costs nothing and is the truthful reading.
            if (faction.RelationWith(Faction.OfPlayer, allowNull: true) == null)
            {
                return false;
            }

            return faction.HostileTo(Faction.OfPlayer) ||
                   faction.PlayerRelationKind == FactionRelationKind.Hostile;
        }

        /// <summary>
        /// The counterparty's faction, by settlement if it still exists.
        ///
        /// A destroyed settlement is §87's problem, not §88's, so a lookup that finds nothing
        /// returns null and the sweep leaves the record alone — the existing expiry and default
        /// paths already handle a counterparty that ceased to exist. The name is accepted only as
        /// a fallback for a settlement that has gone while its faction lives on.
        /// </summary>
        private static Faction FactionOf(int settlementId, string factionName)
        {
            Settlement settlement = IntercolonyMarketAccess.FindSettlement(settlementId);
            if (settlement?.Faction != null)
            {
                return settlement.Faction;
            }

            if (factionName.NullOrEmpty() || Find.FactionManager == null)
            {
                return null;
            }

            foreach (Faction faction in Find.FactionManager.AllFactionsListForReading)
            {
                if (faction.Name == factionName)
                {
                    return faction;
                }
            }

            return null;
        }
    }
}
