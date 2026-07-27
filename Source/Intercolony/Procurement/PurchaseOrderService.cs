using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Owns every purchase order transition (DESIGN.md §21, §70, §73, §104).
    ///
    /// Nothing outside this class assigns <see cref="PurchaseOrder.status"/>.
    ///
    /// Payment is taken **up front**, at acceptance. §21 lists a PlayerDefault branch, which
    /// implies paying on delivery, but that needs a debt and default policy that does not
    /// exist yet — and taking the silver now means a purchase can never arrive at a colony
    /// that cannot pay for it. Refunds on supplier default keep it honest in the other
    /// direction.
    /// </summary>
    public static class PurchaseOrderService
    {
        /// <summary>Days a supplier holds goods before reselling them (§21 SupplierDefault).</summary>
        private const int PickupGraceDays = 10;

        /// <summary>
        /// Accepts a quote: takes payment, creates the order, and closes the request.
        /// Returns null with a message if it cannot proceed.
        /// </summary>
        public static PurchaseOrder AcceptQuote(
            IntercolonyWorldComponent state, PurchaseRequest request, Quotation quote, Map paymentMap)
        {
            return AcceptQuote(state, request, quote, paymentMap, quote?.quantityOffered ?? 0);
        }

        /// <summary>
        /// Buys part of a quote. The player may take fewer units than offered — useful when
        /// silver is short — but never more, since the quoted unit price was struck for that
        /// lot.
        /// </summary>
        public static PurchaseOrder AcceptQuote(
            IntercolonyWorldComponent state, PurchaseRequest request, Quotation quote,
            Map paymentMap, int quantity)
        {
            if (state == null || request == null || quote == null)
            {
                return null;
            }

            quantity = Mathf.Clamp(quantity, 1, quote.quantityOffered);

            if (!request.IsOpen)
            {
                Messages.Message($"That request is {request.status}.",
                    MessageTypeDefOf.RejectInput, historical: false);
                return null;
            }

            Settlement settlement = IntercolonyMarketAccess.FindSettlement(quote.settlementId);
            if (settlement == null)
            {
                Messages.Message($"{quote.settlementName} no longer exists.",
                    MessageTypeDefOf.RejectInput, historical: false);
                return null;
            }

            if (!IntercolonyMarketAccess.IsAccessible(settlement, out string reason))
            {
                Messages.Message($"{quote.settlementName} can no longer supply this: {reason}.",
                    MessageTypeDefOf.RejectInput, historical: false);
                return null;
            }

            if (paymentMap == null)
            {
                Messages.Message("No colony to pay from.", MessageTypeDefOf.RejectInput, historical: false);
                return null;
            }

            int price = Mathf.RoundToInt(quote.unitPrice * quantity);
            int available = CountColonySilver(paymentMap);
            if (available < price)
            {
                Messages.Message(
                    $"Not enough silver in storage: {available} of {price} needed.",
                    MessageTypeDefOf.RejectInput, historical: false);
                return null;
            }

            if (!TryTakeSilver(paymentMap, price))
            {
                Messages.Message("Could not collect the silver.", MessageTypeDefOf.RejectInput, historical: false);
                return null;
            }

            int readyTick = GenTicks.TicksGame + quote.leadTimeDays * GenDate.TicksPerDay;

            PurchaseOrder order = new PurchaseOrder
            {
                id = state.NextId(),
                requestId = request.id,
                quotationId = quote.id,
                settlementId = quote.settlementId,
                settlementName = quote.settlementName,
                factionName = quote.factionName,
                thingDef = request.thingDef,
                stuffDef = quote.offeredStuff ?? request.stuffDef,
                quality = quote.offeredQuality,
                quantity = quantity,
                unitPrice = quote.unitPrice,
                paidSilver = price,
                supplierDelivers = quote.supplierDelivers,
                orderedTick = GenTicks.TicksGame,
                readyTick = readyTick,
                pickupExpiryTick = readyTick + PickupGraceDays * GenDate.TicksPerDay,
                status = PurchaseOrderStatus.Confirmed
            };

            state.AddPurchaseOrder(order);

            // The request is answered; remaining quotes are no longer on the table. Leaving it
            // open would let the player buy the same goods repeatedly off one request.
            request.TryCancel();
            request.status = PurchaseRequestStatus.Cancelled;

            IntercolonyLog.Message(
                $"Purchase {order.id}: {order.quantity}x {order.ItemLabel()} from {order.settlementName} " +
                $"for {price} silver, {(order.supplierDelivers ? "delivered" : "pickup")} in {quote.leadTimeDays}d.");
            Messages.Message(
                order.supplierDelivers
                    ? $"Ordered {order.quantity}x {order.thingDef.label}. Arriving in {quote.leadTimeDays} days."
                    : $"Ordered {order.quantity}x {order.thingDef.label}. Ready to collect in {quote.leadTimeDays} days.",
                MessageTypeDefOf.PositiveEvent, historical: false);

            return order;
        }

        /// <summary>
        /// Advances orders whose lead time has elapsed. Delivered goods arrive at the colony;
        /// pickup orders become collectable. Called from the coarse refresh and an hourly tick.
        /// </summary>
        public static void AdvanceOrders(List<PurchaseOrder> orders)
        {
            int now = GenTicks.TicksGame;

            foreach (PurchaseOrder order in orders)
            {
                if (order.status == PurchaseOrderStatus.Confirmed && now >= order.readyTick)
                {
                    if (order.supplierDelivers)
                    {
                        DeliverToColony(order);
                    }
                    else
                    {
                        order.status = PurchaseOrderStatus.ReadyForPickup;
                        IntercolonyLog.Message($"Purchase {order.id} is ready to collect at {order.settlementName}.");
                        Messages.Message(
                            $"{order.quantity}x {order.thingDef.label} ready to collect at {order.settlementName}.",
                            MessageTypeDefOf.NeutralEvent, historical: true);
                    }

                    continue;
                }

                // Goods left uncollected are eventually resold (§21 SupplierDefault). The
                // player is refunded: they paid for goods they never received.
                if (order.status == PurchaseOrderStatus.ReadyForPickup && now >= order.pickupExpiryTick)
                {
                    Refund(order, "Goods were resold after going uncollected.");
                }
            }
        }

        private static void DeliverToColony(PurchaseOrder order)
        {
            Map map = Find.AnyPlayerHomeMap;
            if (map == null)
            {
                // Nowhere to put them. Hold rather than destroy; the player may resettle.
                return;
            }

            int spawned = SpawnGoods(order, map, DropCellFinder.TradeDropSpot(map));
            if (spawned <= 0)
            {
                Refund(order, "The supplier could not deliver.");
                return;
            }

            Complete(order, $"Delivered {spawned} to the colony.");
            Messages.Message(
                $"{order.settlementName} delivered {spawned}x {order.thingDef.label}.",
                new LookTargets(DropCellFinder.TradeDropSpot(map), map),
                MessageTypeDefOf.PositiveEvent, historical: true);
        }

        /// <summary>
        /// Hands collected goods to a caravan at the supplier's settlement (§25.3 player pickup).
        /// </summary>
        public static bool CollectWithCaravan(PurchaseOrder order, Caravan caravan)
        {
            if (order == null || !order.AwaitingCollection || caravan == null)
            {
                return false;
            }

            List<Thing> goods = MakeGoods(order);
            if (goods.Count == 0)
            {
                Refund(order, "The supplier had nothing to hand over.");
                return false;
            }

            int delivered = 0;
            foreach (Thing thing in goods)
            {
                Pawn carrier = CaravanInventoryUtility.FindPawnToMoveInventoryTo(
                    thing, caravan.PawnsListForReading, null);

                if (carrier == null || !carrier.inventory.innerContainer.TryAdd(thing))
                {
                    // Out of carrying capacity. Keep the order collectable rather than
                    // destroying goods the player has already paid for.
                    thing.Destroy(DestroyMode.Vanish);
                    IntercolonyLog.Warning(
                        $"Purchase {order.id}: caravan could not carry everything; {delivered} taken.");
                    Messages.Message(
                        $"The caravan could not carry all of it — {delivered} collected, the rest is still waiting.",
                        MessageTypeDefOf.CautionInput, historical: false);
                    break;
                }

                delivered += OrderValidator.CountableUnits(thing);
            }

            if (delivered <= 0)
            {
                return false;
            }

            if (delivered >= order.quantity)
            {
                Complete(order, $"Collected {delivered} by caravan.");
                Messages.Message(
                    $"Collected {delivered}x {order.thingDef.label} from {order.settlementName}.",
                    MessageTypeDefOf.PositiveEvent, historical: true);
            }
            else
            {
                // Partial collection: reduce what is still owed and leave it collectable.
                order.quantity -= delivered;
            }

            return true;
        }

        private static void Complete(PurchaseOrder order, string note)
        {
            order.status = PurchaseOrderStatus.Completed;
            order.outcomeNote = note;
            IntercolonyLog.Message($"Purchase {order.id} completed. {note}");
        }

        /// <summary>Refunds a failed order. The only path to SupplierDefault.</summary>
        public static void Refund(PurchaseOrder order, string reason)
        {
            if (order == null || !order.IsOpen)
            {
                return;
            }

            order.status = PurchaseOrderStatus.SupplierDefault;
            order.outcomeNote = reason;

            Map map = Find.AnyPlayerHomeMap;
            if (map != null && order.paidSilver > 0)
            {
                GiveSilver(map, order.paidSilver);
            }

            IntercolonyLog.Message($"Purchase {order.id} failed: {reason} Refunded {order.paidSilver} silver.");
            Messages.Message(
                $"{order.settlementName} defaulted on your order. {order.paidSilver} silver refunded.",
                MessageTypeDefOf.NegativeEvent, historical: true);
        }

        public static bool Cancel(PurchaseOrder order)
        {
            if (order == null || !order.IsOpen)
            {
                return false;
            }

            // Cancelling forfeits the payment: the supplier already produced the goods.
            order.status = PurchaseOrderStatus.Cancelled;
            order.outcomeNote = $"Cancelled by the player. {order.paidSilver} silver forfeited.";
            IntercolonyLog.Message($"Purchase {order.id} cancelled; {order.paidSilver} silver forfeited.");
            return true;
        }

        /// <summary>
        /// Builds the purchased goods with exactly the promised properties — §104's acceptance
        /// criterion is that they "preserve expected properties", so material, quality and
        /// count all come from the order rather than being defaulted.
        /// </summary>
        public static List<Thing> MakeGoods(PurchaseOrder order)
        {
            List<Thing> result = new List<Thing>();
            if (order?.thingDef == null || order.quantity <= 0)
            {
                return result;
            }

            ThingDef def = order.thingDef;
            ThingDef stuff = def.MadeFromStuff
                ? (order.stuffDef ?? GenStuff.DefaultStuffFor(def))
                : null;

            int remaining = order.quantity;
            while (remaining > 0)
            {
                int stack = Mathf.Min(remaining, Mathf.Max(1, def.stackLimit));
                Thing thing = ThingMaker.MakeThing(def, stuff);
                thing.stackCount = stack;

                if (order.quality.HasValue)
                {
                    thing.TryGetComp<CompQuality>()?
                        .SetQuality(order.quality.Value, ArtGenerationContext.Outsider);
                }

                // Buildings must arrive crated, or they cannot be hauled or installed
                // (docs/unique-goods-spike.md).
                result.Add(def.Minifiable ? thing.TryMakeMinified() : thing);
                remaining -= stack;
            }

            return result;
        }

        private static int SpawnGoods(PurchaseOrder order, Map map, IntVec3 cell)
        {
            int placed = 0;
            foreach (Thing thing in MakeGoods(order))
            {
                int units = OrderValidator.CountableUnits(thing);
                if (GenPlace.TryPlaceThing(thing, cell, map, ThingPlaceMode.Near))
                {
                    placed += units;
                }
                else
                {
                    thing.Destroy(DestroyMode.Vanish);
                }
            }

            return placed;
        }

        /// <summary>Silver held in colony storage. Loose silver on the ground does not count.</summary>
        public static int CountColonySilver(Map map)
        {
            if (map == null)
            {
                return 0;
            }

            int total = 0;
            foreach (Thing thing in map.listerThings.ThingsOfDef(ThingDefOf.Silver))
            {
                if (thing.IsInAnyStorage())
                {
                    total += thing.stackCount;
                }
            }

            return total;
        }

        private static bool TryTakeSilver(Map map, int amount)
        {
            if (map == null || amount <= 0)
            {
                return amount <= 0;
            }

            // Snapshot first: destroying stacks mutates the lister mid-iteration.
            List<Thing> stacks = new List<Thing>();
            foreach (Thing thing in map.listerThings.ThingsOfDef(ThingDefOf.Silver))
            {
                if (thing.IsInAnyStorage())
                {
                    stacks.Add(thing);
                }
            }

            int remaining = amount;
            foreach (Thing stack in stacks)
            {
                if (remaining <= 0)
                {
                    break;
                }

                int take = Mathf.Min(remaining, stack.stackCount);
                stack.SplitOff(take).Destroy(DestroyMode.Vanish);
                remaining -= take;
            }

            return remaining <= 0;
        }

        private static void GiveSilver(Map map, int amount)
        {
            int remaining = amount;
            IntVec3 cell = DropCellFinder.TradeDropSpot(map);
            while (remaining > 0)
            {
                int stack = Mathf.Min(remaining, ThingDefOf.Silver.stackLimit);
                Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
                silver.stackCount = stack;
                GenPlace.TryPlaceThing(silver, cell, map, ThingPlaceMode.Near);
                remaining -= stack;
            }
        }
    }
}
