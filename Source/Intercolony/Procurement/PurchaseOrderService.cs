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
        /// Accepts a quote: takes payment, creates the order, and reduces the request remainder.
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

            if (!request.IsOpen)
            {
                Messages.Message($"That request is {request.status}.",
                    MessageTypeDefOf.RejectInput, historical: false);
                return null;
            }

            // Membership is the consumption token. Once removed, the same quotation object
            // cannot create another order even while its request remains open.
            if (!request.quotes.Contains(quote))
            {
                Messages.Message("That quotation is no longer available.",
                    MessageTypeDefOf.RejectInput, historical: false);
                return null;
            }

            int maximum = Mathf.Min(quote.quantityOffered, request.QuantityOutstanding);
            if (maximum <= 0)
            {
                request.status = PurchaseRequestStatus.Ordered;
                Messages.Message("That request has already been fully ordered.",
                    MessageTypeDefOf.RejectInput, historical: false);
                return null;
            }

            quantity = Mathf.Clamp(quantity, 1, maximum);

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

            if (!TryCreatePaidOrder(
                    state,
                    paymentMap,
                    quote.refreshWindow,
                    request.id,
                    quote.id,
                    PurchaseOrder.NoSupplierListing,
                    quote.settlementId,
                    quote.settlementName,
                    quote.factionName,
                    request.thingDef,
                    quote.offeredStuff ?? request.stuffDef,
                    quote.offeredQuality,
                    quantity,
                    quote.animalSpec,
                    quote.unitPrice,
                    quote.supplierDelivers,
                    quote.leadTimeDays,
                    out PurchaseOrder order,
                    out string failureReason))
            {
                Messages.Message(
                    failureReason ?? "Could not create the purchase order.",
                    MessageTypeDefOf.RejectInput, historical: false);
                return null;
            }

            request.quantityOrdered += quantity;
            if (request.QuantityOutstanding == 0)
            {
                request.status = PurchaseRequestStatus.Ordered;
            }

            IntercolonyLog.Message(
                $"Purchase {order.id}: {order.quantity}x {order.ItemLabel()} from {order.settlementName} " +
                $"for {order.paidSilver} silver, " +
                $"{(order.supplierDelivers ? "delivered" : "pickup")} in {quote.leadTimeDays}d.");
            Messages.Message(
                order.supplierDelivers
                    ? $"Ordered {order.quantity}x {order.thingDef.label}. Arriving in {quote.leadTimeDays} days."
                    : $"Ordered {order.quantity}x {order.thingDef.label}. Ready to collect in {quote.leadTimeDays} days.",
                MessageTypeDefOf.PositiveEvent, historical: false);

            return order;
        }

        /// <summary>
        /// Constructs the common persisted order from already validated transaction terms. The
        /// caller supplies the next ID so this seam has no world-state side effects when a later
        /// payment check refuses the transaction.
        /// </summary>
        internal static PurchaseOrder CreateOrder(
            int orderId,
            int requestId,
            int quotationId,
            int supplierListingId,
            int settlementId,
            string settlementName,
            string factionName,
            Map destinationMap,
            ThingDef thingDef,
            ThingDef stuffDef,
            QualityCategory? quality,
            int quantity,
            AnimalSpec animalSpec,
            float unitPrice,
            int paidSilver,
            bool supplierDelivers,
            int leadTimeDays)
        {
            if (thingDef == null || quantity <= 0 || unitPrice <= 0f ||
                float.IsNaN(unitPrice) || float.IsInfinity(unitPrice) || paidSilver < 0 ||
                leadTimeDays < 0)
            {
                return null;
            }

            int readyTick = GenTicks.TicksGame + leadTimeDays * GenDate.TicksPerDay;
            return new PurchaseOrder
            {
                id = orderId,
                requestId = requestId,
                quotationId = quotationId,
                supplierListingId = supplierListingId,
                settlementId = settlementId,
                settlementName = settlementName ?? "",
                factionName = factionName ?? "",
                destinationMap = destinationMap,
                thingDef = thingDef,
                stuffDef = stuffDef,
                quality = quality,
                quantity = quantity,
                animalSpec = animalSpec?.Copy(),
                unitPrice = unitPrice,
                paidSilver = paidSilver,
                supplierDelivers = supplierDelivers,
                orderedTick = GenTicks.TicksGame,
                readyTick = readyTick,
                pickupExpiryTick = readyTick + PickupGraceDays * GenDate.TicksPerDay,
                status = PurchaseOrderStatus.Confirmed
            };
        }

        /// <summary>
        /// Pays for and registers one ordinary purchase order. RFQ and supplier-listing origins
        /// use this same boundary so payment and finite supplier consumption cannot diverge.
        /// </summary>
        internal static bool TryCreatePaidOrder(
            IntercolonyWorldComponent state,
            Map paymentMap,
            int refreshWindow,
            int requestId,
            int quotationId,
            int supplierListingId,
            int settlementId,
            string settlementName,
            string factionName,
            ThingDef thingDef,
            ThingDef stuffDef,
            QualityCategory? quality,
            int quantity,
            AnimalSpec animalSpec,
            float unitPrice,
            bool supplierDelivers,
            int leadTimeDays,
            out PurchaseOrder order,
            out string failureReason)
        {
            order = null;
            failureReason = null;

            if (state == null)
            {
                failureReason = "No procurement state is loaded.";
                return false;
            }

            if (paymentMap == null)
            {
                failureReason = "No colony to pay from.";
                return false;
            }

            if (thingDef == null)
            {
                failureReason = "The purchased item is no longer available.";
                return false;
            }

            if (quantity <= 0)
            {
                failureReason = "Purchase quantity must be positive.";
                return false;
            }

            if (unitPrice <= 0f || float.IsNaN(unitPrice) || float.IsInfinity(unitPrice))
            {
                failureReason = "The supplier's published price is invalid.";
                return false;
            }

            if (leadTimeDays < 0)
            {
                failureReason = "The supplier's lead time is invalid.";
                return false;
            }

            if (!CanPayForPurchase(paymentMap, unitPrice, quantity, out failureReason))
            {
                return false;
            }

            int price = IntercolonyPricing.TotalPayment(unitPrice, quantity);

            // Peek while building the local order. The stable ID is committed only after the
            // payment succeeds, so a refused purchase does not consume an ID either.
            order = CreateOrder(
                state.PeekNextId(),
                requestId,
                quotationId,
                supplierListingId,
                settlementId,
                settlementName,
                factionName,
                paymentMap,
                thingDef,
                stuffDef,
                quality,
                quantity,
                animalSpec,
                unitPrice,
                price,
                supplierDelivers,
                leadTimeDays);
            if (order == null)
            {
                failureReason = "The purchase terms could not be recorded.";
                return false;
            }

            if (!TryTakeSilver(paymentMap, price))
            {
                order = null;
                failureReason = "Could not collect the silver.";
                return false;
            }

            // No operation below this point refuses: the order exists, payment is collected,
            // and these are the shared durable registration steps for every purchase origin.
            state.NextId();
            LedgerService.Record(state, LedgerKind.PurchasePayment, -price, settlementName,
                $"{quantity}x {thingDef.label ?? "goods"}");
            state.AddPurchaseOrder(order);
            state.ConsumeSupplierOffer(refreshWindow, thingDef, settlementId, quantity);
            return true;
        }

        /// <summary>
        /// Checks the read-only payment boundary shared by the supplier-market read model and
        /// the order-creation path. The transaction still performs the final silver take after
        /// this check, because another action may have spent the colony's money between frames.
        /// </summary>
        internal static bool CanPayForPurchase(
            Map paymentMap, float unitPrice, int quantity, out string failureReason)
        {
            failureReason = null;
            if (paymentMap == null)
            {
                failureReason = "No colony to pay from.";
                return false;
            }

            if (quantity <= 0)
            {
                failureReason = "Purchase quantity must be positive.";
                return false;
            }

            if (unitPrice <= 0f || float.IsNaN(unitPrice) || float.IsInfinity(unitPrice))
            {
                failureReason = "The supplier's published price is invalid.";
                return false;
            }

            int price = IntercolonyPricing.TotalPayment(unitPrice, quantity);
            int available = CountColonySilver(paymentMap);
            if (available < price)
            {
                failureReason = $"Not enough silver in storage: {available} of {price} needed.";
                return false;
            }

            return true;
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
            Map map = ResolveDestinationMap(order, out bool usedFallback);
            if (map == null)
            {
                // Nowhere to put them. Hold rather than destroy; the player may resettle.
                return;
            }

            // Live pawns diverge before the goods path touches ThingMaker, stacks, stuff,
            // quality, item placement, or item destruction.
            if (order.IsAnimalOrder)
            {
                DeliverAnimalsToColony(order, map, usedFallback);
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
                $"{order.settlementName} delivered {spawned}x {order.thingDef.label}." +
                DestinationFallbackNotice(map, usedFallback, "the delivery"),
                new LookTargets(DropCellFinder.TradeDropSpot(map), map),
                MessageTypeDefOf.PositiveEvent, historical: true);
        }

        private static Map ResolveDestinationMap(PurchaseOrder order, out bool usedFallback)
        {
            // Removed maps can remain referenced until reload after leaving Find.Maps.
            // Treat that dangling reference like the null Scribe resolves after loading.
            if (order.destinationMap != null &&
                Find.Maps?.Contains(order.destinationMap) == true)
            {
                usedFallback = false;
                return order.destinationMap;
            }

            Map fallback = Find.AnyPlayerHomeMap;
            usedFallback = fallback != null;
            return fallback;
        }

        private static string DestinationFallbackNotice(
            Map map, bool usedFallback, string subject)
        {
            return usedFallback
                ? $" The original destination is unavailable, so {subject} was sent to {map.Parent.Label}."
                : string.Empty;
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

            // An animal is a caravan member. It must never enter MakeGoods or a carrier's
            // item inventory.
            if (order.IsAnimalOrder)
            {
                return CollectAnimalsWithCaravan(order, caravan);
            }

            List<Thing> goods = MakeGoods(order);
            if (goods.Count == 0)
            {
                Refund(order, "The supplier had nothing to hand over.");
                // Refund leaves the order open when it cannot pay anything.
                if (order.IsOpen)
                {
                    Messages.Message(
                        "The refund could not be delivered.",
                        MessageTypeDefOf.RejectInput, historical: false);
                }

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

        private static void DeliverAnimalsToColony(
            PurchaseOrder order, Map map, bool usedFallback)
        {
            int requested = order.quantity;
            int delivered = 0;
            IntVec3 lastCell = IntVec3.Invalid;
            string failure = null;

            for (int i = 0; i < requested; i++)
            {
                if (!AnimalPurchaseUtility.TryGenerateAnimal(
                        order.thingDef, order.animalSpec, out Pawn pawn, out failure))
                {
                    break;
                }

                if (!AnimalPurchaseUtility.TryDeliverToColony(
                        pawn, map, out IntVec3 spawnCell, out failure))
                {
                    break;
                }

                lastCell = spawnCell;
                delivered++;
            }

            if (delivered <= 0)
            {
                IntercolonyLog.Warning(
                    $"Purchase {order.id}: animal colony delivery failed: {failure ?? "unknown failure"}.");
                Refund(order, "The supplier could not deliver the purchased animals.");
                return;
            }

            if (delivered >= requested)
            {
                Complete(order, $"Delivered {delivered} animals to the colony.");
                Messages.Message(
                    $"{order.settlementName} delivered {delivered}x {order.thingDef.label}." +
                    DestinationFallbackNotice(map, usedFallback, "the delivery"),
                    new LookTargets(lastCell, map),
                    MessageTypeDefOf.PositiveEvent, historical: true);
                return;
            }

            // Identical to partial goods collection: only successful handoffs reduce the
            // remaining obligation, and the open order retains its original prepaid balance.
            order.quantity -= delivered;
            IntercolonyLog.Warning(
                $"Purchase {order.id}: partial animal delivery {delivered}; " +
                $"{order.quantity} still owed. {failure ?? "handoff stopped"}.");
            Messages.Message(
                $"{order.settlementName} delivered {delivered}x {order.thingDef.label}; " +
                $"{order.quantity} are still owed." +
                DestinationFallbackNotice(map, usedFallback, "the delivery"),
                new LookTargets(lastCell, map),
                MessageTypeDefOf.CautionInput, historical: true);
        }

        private static bool CollectAnimalsWithCaravan(PurchaseOrder order, Caravan caravan)
        {
            int requested = order.quantity;
            int delivered = 0;
            string failure = null;
            bool generationFailed = false;

            for (int i = 0; i < requested; i++)
            {
                if (!AnimalPurchaseUtility.TryGenerateAnimal(
                        order.thingDef, order.animalSpec, out Pawn pawn, out failure))
                {
                    generationFailed = true;
                    break;
                }

                if (!AnimalPurchaseUtility.TryDeliverToCaravan(pawn, caravan, out failure))
                {
                    break;
                }

                delivered++;
            }

            if (delivered <= 0)
            {
                if (generationFailed)
                {
                    IntercolonyLog.Warning(
                        $"Purchase {order.id}: animal generation failed: {failure ?? "unknown failure"}.");
                    Refund(order, "The supplier had no matching animals to hand over.");
                }
                else
                {
                    IntercolonyLog.Warning(
                        $"Purchase {order.id}: caravan animal handoff failed: " +
                        $"{failure ?? "unknown failure"}.");
                }

                return false;
            }

            if (delivered >= requested)
            {
                Complete(order, $"Collected {delivered} animals by caravan.");
                Messages.Message(
                    $"Collected {delivered}x {order.thingDef.label} from {order.settlementName}.",
                    MessageTypeDefOf.PositiveEvent, historical: true);
            }
            else
            {
                order.quantity -= delivered;
                IntercolonyLog.Warning(
                    $"Purchase {order.id}: partial animal pickup {delivered}; " +
                    $"{order.quantity} still waiting. {failure ?? "handoff stopped"}.");
                Messages.Message(
                    $"Collected {delivered}x {order.thingDef.label}; " +
                    $"{order.quantity} are still waiting.",
                    MessageTypeDefOf.CautionInput, historical: false);
            }

            return true;
        }

        /// <summary>
        /// Internal so self-tests exercise the real completion transition; a hand-completed order
        /// would only verify arithmetic chosen by the test itself.
        /// </summary>
        internal static void Complete(PurchaseOrder order, string note)
        {
            order.status = PurchaseOrderStatus.Completed;
            order.outcomeNote = note;
            IntercolonyWorldComponent state = IntercolonyWorldComponent.Current;
            IntercolonyProductCategory? category = order.IsAnimalOrder
                ? IntercolonyProductCategory.Commodities
                : IntercolonyProductClassifier.Classify(order.thingDef);
            if (category.HasValue)
            {
                MarketPressureService.NudgeSupplyUp(
                    state, order.settlementId, category.Value, order.paidSilver);
            }

            ReputationService.NotePurchaseCompleted(state, order);
            CommercialTimelineService.Record(
                CommercialEventType.PurchaseCompleted,
                order.settlementId,
                order.settlementName,
                order.id,
                order.thingDef,
                order.quantity,
                order.paidSilver);
            IntercolonyLog.Message($"Purchase {order.id} completed. {note}");
        }

        /// <summary>Refunds a failed order. The only path to SupplierDefault.</summary>
        public static void Refund(PurchaseOrder order, string reason)
        {
            if (order == null || !order.IsOpen)
            {
                return;
            }

            // Purchases are prepaid. After a partial animal handoff, quantity is only the head
            // still owed, so only that proportional balance remains refundable. Goods retain
            // their established accounting unchanged.
            int requestedRefund = RefundableSilver(order);
            int refundedSilver = 0;
            Map map = null;
            bool usedFallback = false;
            if (requestedRefund > 0)
            {
                map = ResolveDestinationMap(order, out usedFallback);
                refundedSilver = map == null ? 0 : GiveSilver(map, requestedRefund);
                // A refund that paid nothing is not a default; hold and retry.
                if (map == null || refundedSilver <= 0)
                {
                    return;
                }
            }

            order.status = PurchaseOrderStatus.SupplierDefault;
            order.outcomeNote = reason;
            if (order.IsAnimalOrder)
            {
                // Status UI uses paidSilver as the displayed refunded amount after default.
                order.paidSilver = refundedSilver;
            }

            if (refundedSilver > 0)
            {
                LedgerService.Record(LedgerKind.Refund, refundedSilver, order.settlementName,
                    $"{order.quantity}x {order.thingDef?.label ?? "goods"} refunded");
            }

            // Recorded here rather than at the status assignment above so the timeline carries the
            // silver actually returned. The early return before it means a refund that paid nothing
            // is not a default, and so is not an event either.
            CommercialTimelineService.Record(
                CommercialEventType.PurchaseFailed,
                order.settlementId,
                order.settlementName,
                order.id,
                order.thingDef,
                order.quantity,
                refundedSilver,
                reason);

            IntercolonyLog.Message($"Purchase {order.id} failed: {reason} Refunded {refundedSilver} silver.");
            Messages.Message(
                $"{order.settlementName} defaulted on your order. {refundedSilver} silver refunded." +
                DestinationFallbackNotice(map, usedFallback, "the refund"),
                MessageTypeDefOf.NegativeEvent, historical: true);
        }

        internal static int RefundableSilver(PurchaseOrder order)
        {
            if (order == null || order.paidSilver <= 0)
            {
                return 0;
            }

            return order.IsAnimalOrder
                ? Mathf.Min(order.paidSilver,
                    IntercolonyPricing.TotalPayment(order.unitPrice, order.quantity))
                : order.paidSilver;
        }

        public static bool Cancel(PurchaseOrder order)
        {
            return Cancel(order, out _);
        }

        /// <summary>
        /// Cancels a live purchase order and returns the service-owned refusal explanation when
        /// the order has already concluded or is unavailable.
        /// </summary>
        public static bool Cancel(PurchaseOrder order, out string refusalReason)
        {
            refusalReason = null;
            if (order == null)
            {
                refusalReason = "That purchase order is no longer available.";
                return false;
            }

            if (!order.IsOpen)
            {
                refusalReason = $"Purchase #{order.id} is already {order.status}.";
                return false;
            }

            // Cancelling forfeits the payment: the supplier already produced the goods.
            order.status = PurchaseOrderStatus.Cancelled;
            order.outcomeNote = $"Cancelled by the player. {order.paidSilver} silver forfeited.";
            ReputationService.NotePurchaseCancelled(IntercolonyWorldComponent.Current, order);
            CommercialTimelineService.Record(
                CommercialEventType.PurchaseCancelled,
                order.settlementId,
                order.settlementName,
                order.id,
                order.thingDef,
                order.quantity,
                order.paidSilver,
                "Withdrawn by the player; payment forfeited");
            IntercolonyLog.Message($"Purchase {order.id} cancelled; {order.paidSilver} silver forfeited.");
            Messages.Message(
                $"Purchase #{order.id} cancelled; {order.paidSilver} silver was forfeited.",
                MessageTypeDefOf.NegativeEvent, historical: true);
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
            if (order == null || order.IsAnimalOrder || order.thingDef == null || order.quantity <= 0)
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

        /// <summary>
        /// Removes <paramref name="amount"/> silver from colony storage. Public because
        /// employment pays wages from the same purse (§109).
        /// </summary>
        public static bool TryTakeSilver(Map map, int amount)
        {
            if (map == null || amount <= 0)
            {
                return amount <= 0;
            }

            // Keep this operation atomic for callers that must leave payment untouched when a
            // transaction refuses. The snapshot below can otherwise consume a prefix and then
            // discover that the requested total was not actually present.
            if (CountColonySilver(map) < amount)
            {
                return false;
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

        private static int GiveSilver(Map map, int amount)
        {
            int remaining = amount;
            int placed = 0;
            IntVec3 cell = DropCellFinder.TradeDropSpot(map);
            while (remaining > 0)
            {
                int stack = Mathf.Min(remaining, ThingDefOf.Silver.stackLimit);
                Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
                silver.stackCount = stack;
                if (!GenPlace.TryPlaceThing(
                        silver, cell, map, ThingPlaceMode.Near,
                        (placedThing, placedCount) => placed += placedCount))
                {
                    break;
                }

                remaining -= stack;
            }

            if (placed < amount)
            {
                IntercolonyLog.Warning(
                    $"Refund silver placement was incomplete: requested {amount}, actually placed {placed}.");
            }

            return placed;
        }
    }
}
