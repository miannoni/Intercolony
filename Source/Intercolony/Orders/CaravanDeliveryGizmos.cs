using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// "Deliver order" commands on a caravan that is parked at a buyer's settlement.
    ///
    /// The <see cref="CaravanArrivalAction_DeliverOrder"/> covers the case where the player
    /// *sends* a caravan to the settlement, but it fires only on arrival. A caravan already
    /// sitting on the tile — because it travelled there for another reason, or because the
    /// player loaded a save, or simply because they arrived and did something else first —
    /// has no arrival left to trigger and would otherwise have no way to deliver at all.
    ///
    /// Vanilla has exactly this shape for trading (<see cref="CaravanVisitUtility.TradeCommand"/>),
    /// so a gizmo on the caravan is the interaction players already know.
    /// </summary>
    public static class CaravanDeliveryGizmos
    {
        public static IEnumerable<Gizmo> GetGizmos(Caravan caravan)
        {
            if (caravan == null || !caravan.IsPlayerControlled)
            {
                yield break;
            }

            Settlement settlement = CaravanVisitUtility.SettlementVisitedNow(caravan);
            if (settlement == null)
            {
                yield break;
            }

            IntercolonyWorldComponent state = IntercolonyWorldComponent.Current;
            if (state == null)
            {
                yield break;
            }

            foreach (SalesOrder order in state.Orders)
            {
                if (!order.IsOpen || order.settlementId != settlement.ID)
                {
                    continue;
                }

                yield return BuildCommand(caravan, settlement, order, state);
            }

            // Player pickup of purchased goods (§25.3). Same gizmo shape as delivering a sale,
            // because it is the same player action from the other direction.
            foreach (PurchaseOrder purchase in state.PurchaseOrders)
            {
                if (!purchase.AwaitingCollection || purchase.settlementId != settlement.ID)
                {
                    continue;
                }

                yield return BuildCollectCommand(caravan, purchase);
            }
        }

        private static Command BuildCollectCommand(Caravan caravan, PurchaseOrder purchase)
        {
            Command_Action command = new Command_Action
            {
                defaultLabel = $"Collect purchase #{purchase.id}",
                defaultDesc =
                    $"Load {purchase.quantity}x {purchase.ItemLabel()} onto this caravan.\n\n" +
                    $"Already paid: {purchase.paidSilver} silver\n" +
                    $"Held for another {purchase.DaysUntilPickupExpires:F1} days before it is resold.",
                icon = BaseContent.BadTex,
                action = delegate { PurchaseOrderService.CollectWithCaravan(purchase, caravan); }
            };

            return command;
        }

        private static Command BuildCommand(
            Caravan caravan, Settlement settlement, SalesOrder order, IntercolonyWorldComponent state)
        {
            OrderValidationResult validation = OrderValidator.ValidateCaravan(order, caravan);
            int carried = validation.matchedQuantity;
            int owed = order.RemainingQuantity;

            Command_Action command = new Command_Action
            {
                defaultLabel = $"Deliver order #{order.id}",
                defaultDesc =
                    $"Hand over {order.line?.ShortLabel()} to {order.settlementName}.\n\n" +
                    $"Owed: {owed}\nCarried (meeting the requirements): {carried}\n" +
                    $"Payment: {order.unitPrice:F2} silver each",
                icon = BaseContent.BadTex
            };

            command.action = delegate
            {
                SalesOrderService.ConfirmAndDeliver(state, order, caravan, result =>
                {

                    // §18: report the shortfall rather than leaving the player guessing why the
                    // order did not close.
                    if (!result.Success && order.IsOpen)
                    {
                        Messages.Message($"Order #{order.id}: {result.Summary()}",
                            MessageTypeDefOf.CautionInput, historical: false);
                    }
                });
            };

            // Shown but disabled, with the reason, rather than hidden — a player who hauled
            // goods across the planet needs to see why the button will not work.
            if (settlement.Faction == null || settlement.Faction.HostileTo(Faction.OfPlayer))
            {
                command.Disable("The buyer is hostile.");
            }
            else if (carried <= 0)
            {
                command.Disable(validation.Summary());
            }

            return command;
        }
    }
}
