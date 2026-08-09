using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Owns every Sales Order state transition (DESIGN.md §70 SalesOrderService, §73 "Every
    /// important entity should have authoritative transitions. UI should not arbitrarily
    /// mutate status fields").
    ///
    /// Nothing outside this class may assign <see cref="SalesOrder.status"/>.
    /// </summary>
    public static class SalesOrderService
    {
        /// <summary>
        /// Turns an opportunity into a binding order (§98 "accept opportunity; create Sales
        /// Order"). Returns null and logs if the opportunity is no longer acceptable.
        /// </summary>
        public static SalesOrder Accept(IntercolonyWorldComponent state, MarketOpportunity opportunity)
        {
            return Accept(state, opportunity, opportunity?.quantity ?? 0);
        }

        /// <summary>
        /// Accepts part of an offer. The player may commit to less than the buyer asked for —
        /// supplying 50 of 200 is a legitimate deal — but never more, since the advertised
        /// unit price was computed for the full lot and saturation (§13) makes a smaller lot
        /// worth more per unit, not less.
        /// </summary>
        public static SalesOrder Accept(
            IntercolonyWorldComponent state, MarketOpportunity opportunity, int quantity)
        {
            if (state == null || opportunity == null)
            {
                return null;
            }

            quantity = Mathf.Clamp(quantity, 1, opportunity.quantity);

            if (!opportunity.IsAvailable)
            {
                IntercolonyLog.Warning($"Opportunity {opportunity.id} is {opportunity.state}; cannot accept.");
                return null;
            }

            Settlement settlement = IntercolonyMarketAccess.FindSettlement(opportunity.settlementId);
            if (settlement == null)
            {
                IntercolonyLog.Warning(
                    $"Cannot accept opportunity {opportunity.id}: the buyer no longer exists.");
                return null;
            }

            if (!IntercolonyMarketAccess.IsAccessible(settlement, out string reason))
            {
                IntercolonyLog.Warning($"Cannot accept opportunity {opportunity.id}: {reason}.");
                return null;
            }

            // Claim the offer only once everything else has passed, so a transient refusal
            // (buyer gone, relations soured) leaves the listing intact instead of silently
            // consuming it. Nothing below this line can fail, and the claim itself is what
            // stops a second caller holding the same reference from producing a duplicate
            // order (§76.1) — removal from the world's list cannot, since the caller already
            // has the object.
            if (!opportunity.TryAccept())
            {
                return null;
            }

            SalesOrder order = new SalesOrder
            {
                id = state.NextId(),
                opportunityId = opportunity.id,
                settlementId = opportunity.settlementId,
                settlementName = opportunity.settlementName,
                factionName = settlement.Faction?.Name ?? "",
                line = new OrderLine(opportunity.thingDef, quantity)
                {
                    // Constraints advertised in the market must carry into the binding order,
                    // or the player could be held to terms different from the ones shown.
                    minQuality = opportunity.minQuality,
                    allowedStuff = opportunity.stuffDef,
                    minHitPointsPercent = opportunity.minHitPointsPercent
                },
                // Re-priced for the quantity actually accepted, so the order matches what the
                // confirmation showed. A smaller lot earns a better rate (§13).
                unitPrice = IntercolonyPricing.RepriceForQuantity(
                    opportunity, state.GetProfile(settlement), quantity, out _),
                acceptedTick = GenTicks.TicksGame,
                fulfillment = opportunity.fulfillment,

                // The deadline starts counting at acceptance, which is what the market tab
                // advertised as "Nd after accepting" (§17).
                deadlineTick = GenTicks.TicksGame + opportunity.deadlineDays * GenDate.TicksPerDay,
                status = SalesOrderStatus.Accepted
            };

            state.AddOrder(order);

            // The offer is consumed: it must not remain available for a second acceptance.
            state.RemoveOpportunity(opportunity);

            int pickupTravelDays = EstimateBuyerPickupTravelDays(opportunity.distanceTiles);
            string deadlineAction = opportunity.fulfillment == FulfillmentMode.BuyerPickup
                ? $"{opportunity.deadlineDays}d to mark ready, then ~{pickupTravelDays}d pickup"
                : $"{opportunity.deadlineDays}d to deliver";
            IntercolonyLog.Message(
                $"Accepted order {order.id}: {order.Quantity} of {opportunity.quantity}x " +
                $"{order.ThingDef.label} for " +
                $"{order.settlementName}, {order.TotalPayment} silver, " +
                $"{deadlineAction}.");

            string nextStep = opportunity.fulfillment == FulfillmentMode.BuyerPickup
                ? $"Mark the goods ready within {opportunity.deadlineDays} days. " +
                  $"Pickup is expected about {pickupTravelDays} days after that."
                : $"Deliver within {opportunity.deadlineDays} days.";
            Messages.Message(
                $"Order accepted: {order.Quantity}x {order.ThingDef.label} for {order.settlementName}. " +
                nextStep,
                MessageTypeDefOf.PositiveEvent,
                historical: false);

            return order;
        }

        /// <summary>
        /// Creates an order directly from a Find Buyer result (DESIGN.md §102 "create sale
        /// from result"), with no market listing involved.
        ///
        /// This is a second entry point into the same binding commitment, so it repeats the
        /// access checks rather than trusting the caller: an offer computed a few seconds ago
        /// could reference a settlement that has since turned hostile or been destroyed.
        /// </summary>
        public static SalesOrder CreateFromOffer(
            IntercolonyWorldComponent state, Map map, BuyerOffer offer, int quantity,
            int deadlineDays, FulfillmentMode fulfillment)
        {
            if (state == null || offer?.settlement == null || offer.def == null || quantity <= 0)
            {
                return null;
            }

            if (!IntercolonyMarketAccess.IsAccessible(offer.settlement, out string reason))
            {
                IntercolonyLog.Warning($"Cannot sell to {offer.settlement.Label}: {reason}.");
                Messages.Message($"{offer.settlement.Label} will not trade: {reason}.",
                    MessageTypeDefOf.RejectInput, historical: false);
                return null;
            }

            int available = FindBuyerService.AvailableQuantity(state, map, offer.def);
            if (available < quantity)
            {
                int committed = FindBuyerService.CommittedQuantity(state, offer.def);
                Messages.Message(
                    $"Only {available:N0} {offer.def.label} are still available for a new sale; " +
                    $"{committed:N0} are already committed.",
                    MessageTypeDefOf.RejectInput, historical: false);
                return null;
            }

            SalesOrder order = new SalesOrder
            {
                id = state.NextId(),
                opportunityId = 0,
                settlementId = offer.settlement.ID,
                settlementName = offer.settlement.Label ?? "unnamed",
                factionName = offer.settlement.Faction?.Name ?? "",
                line = new OrderLine(offer.def, quantity)
                {
                    allowedStuff = offer.stuff
                },
                unitPrice = offer.unitPrice,
                acceptedTick = GenTicks.TicksGame,
                fulfillment = fulfillment,
                deadlineTick = GenTicks.TicksGame + deadlineDays * GenDate.TicksPerDay,
                status = SalesOrderStatus.Accepted
            };

            state.AddOrder(order);

            IntercolonyLog.Message(
                $"Created order {order.id} from Find Buyer: {quantity}x {offer.def.label} " +
                $"for {order.settlementName}, {order.TotalPayment} silver, {deadlineDays}d, " +
                $"{fulfillment}.");
            int pickupTravelDays = EstimateBuyerPickupTravelDays(offer.distanceTiles);
            string nextStep = fulfillment == FulfillmentMode.BuyerPickup
                ? $"Mark the goods ready within {deadlineDays} days. " +
                  $"Pickup is expected about {pickupTravelDays} days after that."
                : $"Deliver within {deadlineDays} days.";
            Messages.Message(
                $"Order created: {quantity}x {offer.def.label} for {order.settlementName}. " +
                nextStep,
                MessageTypeDefOf.PositiveEvent, historical: false);

            return order;
        }

        /// <summary>
        /// Hands over whatever the caravan is carrying against this order and pays for it
        /// (§98 "physical delivery; validation; payment").
        ///
        /// Partial delivery is allowed and paid pro-rata: the order stays open so the player
        /// can come back with the rest. Silently rejecting a short delivery would strand the
        /// goods and teach nothing.
        /// </summary>
        public static OrderValidationResult Deliver(
            IntercolonyWorldComponent state, SalesOrder order, Caravan caravan)
        {
            OrderValidationResult result = OrderValidator.ValidateCaravan(order, caravan);
            if (result.matchedQuantity <= 0)
            {
                return result;
            }

            int handedOver = RemoveFromCaravan(order, caravan, result.matchedQuantity);
            if (handedOver <= 0)
            {
                result.failures.Add("Could not take the goods from the caravan.");
                return result;
            }

            order.deliveredQuantity += handedOver;

            // Per-delivery payment is floored so a run of partial deliveries can never overpay
            // past the agreed total. On the delivery that *completes* the order, pay the exact
            // remainder instead: otherwise the quoted total (rounded) and the sum of floored
            // instalments disagree, and the player is visibly short-changed — an order
            // advertised at 537 silver paid out 536.
            int payment = order.RemainingQuantity <= 0
                ? order.TotalPayment - order.paidSilver
                : order.PaymentFor(handedOver);

            payment = Mathf.Max(0, payment);
            order.paidSilver += payment;

            GiveSilver(caravan, payment);

            LedgerService.Record(LedgerKind.SalePayment, payment, order.settlementName,
                $"{order.deliveredQuantity}x {order.ThingDef?.label ?? "goods"}, delivered");

            if (order.RemainingQuantity <= 0)
            {
                Complete(state, order);
            }
            else
            {
                IntercolonyLog.Message(
                    $"Order {order.id}: partial delivery {handedOver} units, " +
                    $"{order.RemainingQuantity} still owed. Paid {payment} silver.");
                Messages.Message(
                    $"Delivered {handedOver}x {order.ThingDef.label}. " +
                    $"{order.RemainingQuantity} still owed. Received {payment} silver.",
                    MessageTypeDefOf.NeutralEvent,
                    historical: false);
            }

            // Re-validate so the caller sees the post-delivery position.
            return OrderValidator.ValidateCaravan(order, caravan);
        }

        private static void Complete(IntercolonyWorldComponent state, SalesOrder order)
        {
            order.status = SalesOrderStatus.Completed;
            order.outcomeNote = $"Delivered {order.deliveredQuantity} units for {order.paidSilver} silver.";

            // §27: on-time delivery is worth more than a late one, so the distinction is made
            // here where the deadline is still known.
            ReputationService.NoteOrderCompleted(state, order, !order.IsOverdue(GenTicks.TicksGame));

            IntercolonyLog.Message($"Order {order.id} completed. {order.outcomeNote}");
            Messages.Message(
                $"Order complete: {order.settlementName} paid {order.paidSilver} silver.",
                MessageTypeDefOf.PositiveEvent,
                historical: true);
        }

        /// <summary>
        /// Declares a buyer-pickup order ready, dispatching the buyer's caravan (§25.2).
        ///
        /// The player must actually have the goods. Letting them announce readiness on an
        /// empty stockpile would just move the failure to the arrival, which §17 warns
        /// against — a player should not discover a problem at the deadline.
        /// </summary>
        public static bool MarkReadyForPickup(SalesOrder order, Map map)
        {
            if (order == null || !order.CanMarkReady)
            {
                return false;
            }

            if (order.IsOverdue(GenTicks.TicksGame))
            {
                Messages.Message(
                    $"Order #{order.id}: the deadline to mark the goods ready has passed.",
                    MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }

            OrderValidationResult validation = OrderValidator.ValidateColony(order, map);
            if (!validation.Success)
            {
                Messages.Message(
                    $"Order #{order.id}: {validation.Summary()}",
                    MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }

            IntercolonyWorldComponent state = IntercolonyWorldComponent.Current;
            int available = FindBuyerService.AvailableQuantity(
                state, map, order.ThingDef, order.id);
            if (available < order.RemainingQuantity)
            {
                int committedElsewhere = FindBuyerService.CommittedQuantity(
                    state, order.ThingDef, order.id);
                Messages.Message(
                    $"Order #{order.id}: only {available:N0} {order.ThingDef.label} are free; " +
                    $"{committedElsewhere:N0} matching units are already committed elsewhere. " +
                    $"This order still needs {order.RemainingQuantity:N0}.",
                    MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }

            float distance = -1f;
            Settlement settlement = IntercolonyMarketAccess.FindSettlement(order.settlementId);
            if (settlement != null)
            {
                distance = MarketOpportunityGenerator.DistanceToPlayer(settlement);
            }

            int travelDays = EstimateBuyerPickupTravelDays(distance);
            order.status = SalesOrderStatus.AwaitingCollection;
            order.buyerArrivalTick = GenTicks.TicksGame + travelDays * GenDate.TicksPerDay;

            IntercolonyLog.Message(
                $"Order {order.id}: goods declared ready; {order.settlementName} arriving in {travelDays}d.");

            // §25.2's worked example is exactly this letter.
            IntercolonyLetters.Send(
                IntercolonyLetterImportance.Always,
                "Order ready",
                BuyerPickupDispatchLetterText(order, travelDays),
                LetterDefOf.PositiveEvent);

            return true;
        }

        /// <summary>
        /// Shared buyer-pickup travel estimate. A negative distance means the route is unknown;
        /// the same three-day fallback is used by dispatch and every pre-acceptance display.
        /// </summary>
        public static int EstimateBuyerPickupTravelDays(float distanceTiles)
        {
            float estimatedDays = distanceTiles < 0f ? 3f : distanceTiles / 14f;
            return Mathf.Clamp(Mathf.RoundToInt(estimatedDays), 1, 20);
        }

        internal static string BuyerPickupDispatchLetterText(SalesOrder order, int travelDays)
        {
            return $"{order.settlementName} will arrive in approximately {travelDays} days to collect " +
                   $"order #{order.id}: {order.RemainingQuantity}x {order.line.ShortLabel()}.\n\n" +
                   "Keep the goods in storage until they arrive.";
        }

        /// <summary>
        /// Handles buyers arriving to collect (§25.2). Called on the coarse and hourly ticks.
        /// </summary>
        public static void ProcessBuyerCollections(List<SalesOrder> orders)
        {
            int now = GenTicks.TicksGame;
            Map map = Find.AnyPlayerHomeMap;

            foreach (SalesOrder order in orders)
            {
                if (!order.BuyerEnRoute || order.buyerArrivalTick < 0 || now < order.buyerArrivalTick)
                {
                    continue;
                }

                if (map == null)
                {
                    continue;
                }

                int owed = order.RemainingQuantity;
                int taken = OrderValidator.TakeFromColony(order, map, owed);

                if (taken <= 0)
                {
                    // The goods were promised and are gone. That is a failed order, not a
                    // silent no-op — the buyer travelled for nothing.
                    Fail(order, "The buyer arrived and the goods were not there.");
                    continue;
                }

                int payment = taken >= owed
                    ? order.TotalPayment - order.paidSilver
                    : order.PaymentFor(taken);

                order.deliveredQuantity += taken;
                order.paidSilver += Mathf.Max(0, payment);
                GiveSilverToColony(map, Mathf.Max(0, payment));

                LedgerService.Record(LedgerKind.SalePayment, Mathf.Max(0, payment),
                    order.settlementName,
                    $"{taken}x {order.ThingDef?.label ?? "goods"}, collected");

                if (order.RemainingQuantity <= 0)
                {
                    order.status = SalesOrderStatus.Completed;
                    order.outcomeNote =
                        $"Collected by the buyer. {order.deliveredQuantity} units for {order.paidSilver} silver.";
                    ReputationService.NoteOrderCompleted(
                        IntercolonyWorldComponent.Current, order, !order.IsOverdue(now));
                    IntercolonyLog.Message($"Order {order.id} completed by buyer pickup. {order.outcomeNote}");
                    IntercolonyLetters.Send(
                        IntercolonyLetterImportance.Chatty,
                        "Order collected",
                        $"{order.settlementName} collected {taken}x {order.line.ShortLabel()} " +
                        $"and paid {payment} silver.",
                        LetterDefOf.PositiveEvent);
                }
                else
                {
                    // Short on arrival: they take what is there, pay for it, and the rest
                    // still stands. Better than voiding the whole order over a shortfall.
                    order.status = SalesOrderStatus.Accepted;
                    order.buyerArrivalTick = -1;
                    IntercolonyLetters.Send(
                        IntercolonyLetterImportance.Always,
                        "Partial collection",
                        $"{order.settlementName} collected only {taken} of {owed} units and paid " +
                        $"{payment} silver. Declare the remainder ready when you have it.",
                        LetterDefOf.NeutralEvent);
                }
            }
        }

        /// <summary>Drops payment at the colony's trade spot, where a buyer's caravan would leave it.</summary>
        private static void GiveSilverToColony(Map map, int amount)
        {
            if (map == null || amount <= 0)
            {
                return;
            }

            IntVec3 cell = DropCellFinder.TradeDropSpot(map);
            int remaining = amount;
            while (remaining > 0)
            {
                int stack = Mathf.Min(remaining, ThingDefOf.Silver.stackLimit);
                Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
                silver.stackCount = stack;
                GenPlace.TryPlaceThing(silver, cell, map, ThingPlaceMode.Near);
                remaining -= stack;
            }
        }

        /// <summary>Marks an order failed. The only path to <see cref="SalesOrderStatus.Failed"/>.</summary>
        public static bool Fail(SalesOrder order, string note)
        {
            if (order == null || !order.IsOpen)
            {
                return false;
            }

            order.status = SalesOrderStatus.Failed;
            order.outcomeNote = note;
            ReputationService.NoteOrderFailed(IntercolonyWorldComponent.Current, order);
            IntercolonyLog.Message($"Order {order.id} failed: {note}");
            Messages.Message($"Order failed for {order.settlementName}: {note}",
                MessageTypeDefOf.NegativeEvent, historical: true);
            return true;
        }

        /// <summary>Player-initiated withdrawal. Distinct from failure so later reputation work can treat them differently (§27).</summary>
        public static bool Cancel(SalesOrder order)
        {
            if (order == null || !order.IsOpen)
            {
                return false;
            }

            order.status = SalesOrderStatus.Cancelled;
            order.outcomeNote = "Cancelled by the player.";
            ReputationService.NoteOrderCancelled(IntercolonyWorldComponent.Current, order);
            IntercolonyLog.Message($"Order {order.id} cancelled by the player.");
            return true;
        }

        /// <summary>
        /// Fails orders whose deadline has passed (§17). Called from the coarse refresh and
        /// on a lighter tick so a missed deadline is noticed promptly rather than up to a day
        /// later.
        /// </summary>
        public static int FailOverdue(List<SalesOrder> orders)
        {
            int now = GenTicks.TicksGame;
            int failed = 0;
            foreach (SalesOrder order in orders)
            {
                if (!order.IsOpen || !order.IsOverdue(now))
                {
                    continue;
                }

                // For buyer pickup, the deadline governs readiness, not the buyer's journey.
                // AwaitingCollection is the durable evidence that the player met that clock.
                if (order.fulfillment == FulfillmentMode.BuyerPickup && order.BuyerEnRoute)
                {
                    continue;
                }

                Fail(order, $"Deadline passed with {order.RemainingQuantity} units undelivered.");
                failed++;
            }

            return failed;
        }

        /// <summary>
        /// Takes units out of caravan pawn inventories. Returns how many were actually taken,
        /// which can be less than requested if the caravan changed between validation and here.
        /// </summary>
        private static int RemoveFromCaravan(SalesOrder order, Caravan caravan, int wanted)
        {
            int remaining = wanted;
            List<Thing> items = CaravanInventoryUtility.AllInventoryItems(caravan);

            // Iterate a copy: taking things mutates the underlying inventories.
            List<Thing> matching = new List<Thing>();
            foreach (Thing thing in items)
            {
                if (OrderValidator.Matches(order.line, thing))
                {
                    matching.Add(thing);
                }
            }

            foreach (Thing thing in matching)
            {
                if (remaining <= 0)
                {
                    break;
                }

                int take = Mathf.Min(remaining, thing.stackCount);
                Thing split = thing.SplitOff(take);
                split.Destroy(DestroyMode.Vanish);
                remaining -= take;
            }

            return wanted - remaining;
        }

        /// <summary>
        /// Puts silver into the caravan, following the same route vanilla uses when the player
        /// buys goods (<c>Caravan_TraderTracker.GiveSoldThingToPlayer</c>): find a pawn with
        /// room and add to their inventory.
        /// </summary>
        private static void GiveSilver(Caravan caravan, int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            // Silver stacks at 500, so a large payment needs several stacks.
            int remaining = amount;
            while (remaining > 0)
            {
                int stack = Mathf.Min(remaining, ThingDefOf.Silver.stackLimit);
                Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
                silver.stackCount = stack;

                Pawn carrier = CaravanInventoryUtility.FindPawnToMoveInventoryTo(
                    silver, caravan.PawnsListForReading, null);

                if (carrier == null || !carrier.inventory.innerContainer.TryAdd(silver))
                {
                    // Nobody can carry it. Dropping it at the settlement tile would be
                    // invisible to the player, so say so rather than deleting their money.
                    IntercolonyLog.Warning(
                        $"No caravan pawn could carry {stack} silver; payment left undelivered.");
                    Messages.Message(
                        $"The caravan could not carry {stack} silver — free up space and deliver again.",
                        MessageTypeDefOf.NegativeEvent, historical: false);
                    silver.Destroy(DestroyMode.Vanish);
                    return;
                }

                remaining -= stack;
            }
        }
    }
}
