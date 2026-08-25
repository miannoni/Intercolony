using RimWorld;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// A finite, public procurement offer from one settlement. This is a listing rather than a
    /// quotation response: accepting it will later create the same <see cref="PurchaseOrder"/>
    /// used by RFQs, while the remaining quantity stays authoritative on this record until then.
    /// </summary>
    public class SupplierListing : IExposable
    {
        /// <summary>Stable ID allocated by <see cref="IntercolonyWorldComponent.NextId"/>.</summary>
        public int id;

        /// <summary>Stable <c>WorldObject.ID</c> of the supplying settlement.</summary>
        public int settlementId = -1;

        /// <summary>Item the supplier is offering.</summary>
        public ThingDef thingDef;

        /// <summary>Required material, or null when the item has no material specification.</summary>
        public ThingDef stuffDef;

        /// <summary>Quality the supplier is offering, or null for items without quality.</summary>
        public QualityCategory? quality;

        /// <summary>Units still available from this listing's finite offer.</summary>
        public int quantityAvailable;

        /// <summary>Silver charged per unit, fixed for the life of this listing.</summary>
        public float unitPrice;

        /// <summary>
        /// How the goods move: <see cref="FulfillmentMode.SellerDelivery"/> means the supplier
        /// delivers, while <see cref="FulfillmentMode.BuyerPickup"/> means the player collects.
        /// </summary>
        public FulfillmentMode fulfillment = FulfillmentMode.SellerDelivery;

        /// <summary>Days before the goods are ready or arrive, according to <see cref="fulfillment"/>.</summary>
        public int leadTimeDays;

        /// <summary>Tick at which this listing was created.</summary>
        public int createdTick;

        /// <summary>Tick at which this listing expires; <see cref="NoExpiryTick"/> means never.</summary>
        public int expiryTick;

        /// <summary>Market refresh window that owns this finite offer.</summary>
        public int refreshWindow;

        /// <summary>Sentinel for an intentionally non-expiring listing; never format as a quantity.</summary>
        public const int NoExpiryTick = -1;

        /// <summary>
        /// True only while quantity remains and the listing has not expired. This is derived so
        /// consumption cannot leave a stored availability flag disagreeing with the quantity.
        /// </summary>
        public bool IsAvailable => quantityAvailable > 0 && !HasExpired(GenTicks.TicksGame);

        /// <summary>Whether the listing has expired at the supplied absolute game tick.</summary>
        public bool HasExpired(int nowTick)
        {
            return expiryTick != NoExpiryTick && nowTick >= expiryTick;
        }

        /// <summary>
        /// Whether this record can remain in the live listing collection after loading. A missing
        /// item definition means its supplying mod was removed, so retaining it would make later
        /// consumers dereference an unresolved item.
        /// </summary>
        public bool IsValidAfterLoad => thingDef != null && quantityAvailable > 0;

        /// <summary>Writes the durable listing terms and finite availability.</summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", 0);
            Scribe_Values.Look(ref settlementId, "settlementId", -1);
            Scribe_Defs.Look(ref thingDef, "thingDef");
            Scribe_Defs.Look(ref stuffDef, "stuffDef");
            Scribe_Values.Look(ref quality, "quality");
            Scribe_Values.Look(ref quantityAvailable, "quantityAvailable", 0);
            Scribe_Values.Look(ref unitPrice, "unitPrice", 0f);
            Scribe_Values.Look(ref fulfillment, "fulfillment", FulfillmentMode.SellerDelivery);
            Scribe_Values.Look(ref leadTimeDays, "leadTimeDays", 0);
            Scribe_Values.Look(ref createdTick, "createdTick", 0);
            Scribe_Values.Look(ref expiryTick, "expiryTick", NoExpiryTick);
            Scribe_Values.Look(ref refreshWindow, "refreshWindow", 0);
        }
    }
}
