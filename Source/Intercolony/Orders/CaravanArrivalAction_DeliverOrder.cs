using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Delivering an order by caravan (DESIGN.md §25.1 seller delivery, §98 "physical
    /// delivery").
    ///
    /// Implemented as a <see cref="CaravanArrivalAction"/> because that is how RimWorld
    /// already models "take this caravan somewhere and do a thing on arrival" — the player
    /// gets the option in the same float menu as Trade and Visit, with no new UI concept to
    /// learn. It also keeps §26's abstraction boundary intact: the order knows about origin,
    /// destination, cargo, and completion, not about caravan internals.
    /// </summary>
    public class CaravanArrivalAction_DeliverOrder : CaravanArrivalAction
    {
        private Settlement settlement;

        /// <summary>
        /// Stored by id rather than by reference. Orders live in the world component, not in
        /// the Scribe reference graph, so a reference would not resolve on load.
        /// </summary>
        private int orderId;

        public CaravanArrivalAction_DeliverOrder()
        {
        }

        public CaravanArrivalAction_DeliverOrder(Settlement settlement, SalesOrder order)
        {
            this.settlement = settlement;
            orderId = order.id;
        }

        public override string Label
        {
            get
            {
                SalesOrder order = FindOrder();
                return order == null
                    ? "Deliver Intercolony order"
                    : $"Deliver order #{order.id} ({order.RemainingQuantity}x {order.ThingDef?.label})";
            }
        }

        public override string ReportString =>
            $"Delivering an order to {settlement?.Label ?? "a settlement"}.";

        private SalesOrder FindOrder()
        {
            return IntercolonyWorldComponent.Current?.FindOrder(orderId);
        }

        public override FloatMenuAcceptanceReport StillValid(Caravan caravan, PlanetTile destinationTile)
        {
            FloatMenuAcceptanceReport report = base.StillValid(caravan, destinationTile);
            if (!report)
            {
                return report;
            }

            if (settlement != null && settlement.Tile != destinationTile)
            {
                return false;
            }

            return CanDeliver(caravan, settlement, FindOrder());
        }

        public override void Arrived(Caravan caravan)
        {
            IntercolonyWorldComponent state = IntercolonyWorldComponent.Current;
            SalesOrder order = FindOrder();
            if (state == null || order == null)
            {
                Messages.Message("That order no longer exists.", MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            OrderValidationResult result = SalesOrderService.Deliver(state, order, caravan);

            // A short delivery is not silently swallowed: §18 wants the shortfall reported.
            if (!result.Success && order.IsOpen)
            {
                Messages.Message(
                    $"Order #{order.id}: {result.Summary()}",
                    MessageTypeDefOf.CautionInput,
                    historical: false);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref settlement, "settlement");
            Scribe_Values.Look(ref orderId, "orderId", 0);
        }

        /// <summary>
        /// Whether this caravan can deliver this order here. Also gates the float menu entry,
        /// so an option never appears that would immediately fail.
        /// </summary>
        public static FloatMenuAcceptanceReport CanDeliver(Caravan caravan, Settlement settlement, SalesOrder order)
        {
            if (order == null || !order.IsOpen)
            {
                return false;
            }

            if (settlement == null || !settlement.Spawned || settlement.ID != order.settlementId)
            {
                return false;
            }

            if (settlement.Faction == null || settlement.Faction.HostileTo(Faction.OfPlayer))
            {
                return FloatMenuAcceptanceReport.WithFailReason("buyer is hostile");
            }

            if (OrderValidator.CountMatching(order, caravan) <= 0)
            {
                // Shown greyed-out with the reason, rather than hidden: a player who took a
                // caravan out specifically to deliver needs to know why the option is absent.
                return FloatMenuAcceptanceReport.WithFailReason(
                    $"carrying no {order.ThingDef?.label ?? "goods"}");
            }

            return true;
        }

        /// <summary>
        /// One menu entry per open order for this settlement. A caravan carrying goods for
        /// two different orders to the same buyer gets two entries.
        /// </summary>
        public static IEnumerable<FloatMenuOption> GetFloatMenuOptions(Caravan caravan, Settlement settlement)
        {
            IntercolonyWorldComponent state = IntercolonyWorldComponent.Current;
            if (state == null || settlement == null)
            {
                yield break;
            }

            foreach (SalesOrder order in state.Orders)
            {
                if (!order.IsOpen || order.settlementId != settlement.ID)
                {
                    continue;
                }

                SalesOrder localOrder = order;
                foreach (FloatMenuOption option in CaravanArrivalActionUtility.GetFloatMenuOptions(
                             () => CanDeliver(caravan, settlement, localOrder),
                             () => new CaravanArrivalAction_DeliverOrder(settlement, localOrder),
                             $"Deliver order #{localOrder.id}: {localOrder.RemainingQuantity}x " +
                             $"{localOrder.ThingDef?.label ?? "goods"}",
                             caravan,
                             settlement.Tile,
                             settlement))
                {
                    yield return option;
                }
            }
        }
    }
}
