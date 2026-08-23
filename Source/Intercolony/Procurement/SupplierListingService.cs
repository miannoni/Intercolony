using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Creates the finite standing offers shown by the Supplier Market. This service owns neither
    /// purchase acceptance nor fulfilment; a listing is only the current-window offer snapshot.
    /// </summary>
    public static class SupplierListingService
    {
        /// <summary>Maximum standing offers one settlement can publish in one market window.</summary>
        public const int MaxPerSettlement = 3;

        private const int MinLifespanDays = 3;
        private const int MaxLifespanDays = 10;
        private const int GenerationSalt = 0x5A71;

        /// <summary>
        /// Accepts part or all of a live supplier listing as an ordinary paid purchase order.
        /// Listing-specific availability is checked here; payment, order registration and
        /// finite supplier consumption remain owned by <see cref="PurchaseOrderService"/>.
        /// </summary>
        public static bool TryPurchase(
            IntercolonyWorldComponent state,
            SupplierListing listing,
            int quantity,
            out PurchaseOrder order,
            out string failureReason)
        {
            order = null;
            failureReason = null;

            if (!CanPurchase(state, listing, quantity, out failureReason))
            {
                return false;
            }

            Settlement settlement = IntercolonyMarketAccess.FindSettlement(listing.settlementId);
            Map paymentMap = Find.CurrentMap ?? Find.AnyPlayerHomeMap;

            bool created = PurchaseOrderService.TryCreatePaidOrder(
                state,
                paymentMap,
                listing.refreshWindow,
                0,
                0,
                listing.id,
                listing.settlementId,
                settlement.Label ?? "unnamed",
                settlement.Faction?.Name ?? "",
                listing.thingDef,
                listing.stuffDef,
                listing.quality,
                quantity,
                null,
                listing.unitPrice,
                listing.fulfillment == FulfillmentMode.SellerDelivery,
                listing.leadTimeDays,
                out order,
                out failureReason);
            if (!created)
            {
                return false;
            }

            // The order and its durable consumption record now exist. Keep the listing as the
            // public face of the remaining quantity; refresh pruning will remove it later.
            listing.quantityAvailable -= quantity;
            IntercolonyLog.Message(
                $"Purchase {order.id}: {order.quantity}x {order.ItemLabel()} from " +
                $"{order.settlementName} for {order.paidSilver} silver, " +
                $"{(order.supplierDelivers ? "delivered" : "pickup")} in {listing.leadTimeDays}d.");
            Messages.Message(
                order.supplierDelivers
                    ? $"Ordered {order.quantity}x {order.thingDef.label}. Arriving in {listing.leadTimeDays} days."
                    : $"Ordered {order.quantity}x {order.thingDef.label}. Ready to collect in {listing.leadTimeDays} days.",
                MessageTypeDefOf.PositiveEvent, historical: false);
            return true;
        }

        /// <summary>
        /// Read-only purchase eligibility for the Supplier Market. It uses the same refusal text
        /// as <see cref="TryPurchase"/> so a disabled row cannot invent a second explanation for
        /// a transaction the purchase service would reject.
        /// </summary>
        internal static bool CanPurchase(
            IntercolonyWorldComponent state,
            SupplierListing listing,
            int quantity,
            out string failureReason)
        {
            failureReason = null;

            if (state == null)
            {
                failureReason = "No procurement state is loaded.";
                return false;
            }

            if (listing == null)
            {
                failureReason = "That supplier listing no longer exists.";
                return false;
            }

            int maximum = listing.quantityAvailable;
            if (quantity < 1 || quantity > maximum)
            {
                failureReason = $"Quantity must be between 1 and {maximum}.";
                return false;
            }

            if (!listing.IsAvailable)
            {
                failureReason = "That supplier listing is no longer available.";
                return false;
            }

            if (listing.thingDef == null)
            {
                failureReason = "The listed item is no longer available.";
                return false;
            }

            if (listing.unitPrice <= 0f || float.IsNaN(listing.unitPrice) ||
                float.IsInfinity(listing.unitPrice))
            {
                failureReason = "The supplier's published price is invalid.";
                return false;
            }

            if (listing.leadTimeDays < 0)
            {
                failureReason = "The supplier's lead time is invalid.";
                return false;
            }

            if (listing.fulfillment != FulfillmentMode.SellerDelivery &&
                listing.fulfillment != FulfillmentMode.BuyerPickup)
            {
                failureReason = "The supplier's fulfillment mode is invalid.";
                return false;
            }

            Settlement settlement = IntercolonyMarketAccess.FindSettlement(listing.settlementId);
            if (settlement == null)
            {
                failureReason = "The supplying settlement no longer exists.";
                return false;
            }

            if (!IntercolonyMarketAccess.IsAccessible(settlement, out string accessReason))
            {
                failureReason = $"The supplying settlement is no longer accessible: {accessReason}.";
                return false;
            }

            Map paymentMap = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
            return PurchaseOrderService.CanPayForPurchase(
                paymentMap, listing.unitPrice, quantity, out failureReason);
        }

        /// <summary>
        /// Generates a bounded, deterministic batch for one supplier in a refresh window. The
        /// caller owns insertion into world state so a direct generation remains side-effect free.
        /// </summary>
        public static List<SupplierListing> GenerateFor(
            IntercolonyWorldComponent state,
            Settlement settlement,
            SettlementEconomicProfile profile,
            int refreshWindow,
            int existingCount,
            Func<int> idAllocator)
        {
            List<SupplierListing> created = new List<SupplierListing>();
            if (state == null || settlement == null || profile == null || idAllocator == null ||
                existingCount >= MaxPerSettlement ||
                !IntercolonyMarketAccess.IsAccessible(settlement))
            {
                return created;
            }

            int seed = Gen.HashCombineInt(
                state.EconomySeed, settlement.ID, refreshWindow, GenerationSalt);
            float distance = MarketOpportunityGenerator.DistanceToPlayer(settlement);
            Dictionary<IntercolonyProductCategory, List<ThingDef>> candidates;

            Rand.PushState(seed);
            try
            {
                candidates = BuildCandidates(state, profile);
                int slots = MaxPerSettlement - existingCount;
                for (int i = 0; i < slots; i++)
                {
                    IntercolonyProductCategory? category = PickCategory(state, profile, candidates);
                    if (!category.HasValue)
                    {
                        break;
                    }

                    List<ThingDef> defs = candidates[category.Value];
                    ThingDef def = defs[Rand.Range(0, defs.Count)];
                    defs.Remove(def);

                    ThingDef stuff = RfqService.PickSupplierStuff(def);
                    QualityCategory? quality = RfqService.PickOfferedQuality(def, profile, null);
                    float supply = EffectiveEconomyService.EffectiveSupply(
                        state, profile, category.Value);
                    int grossQuantity = RfqService.SupplierOfferQuantity(
                        def, stuff, profile, supply);
                    int consumed = state.SupplierOfferConsumptionFor(
                        refreshWindow, def, settlement.ID);
                    int quantityAvailable = Mathf.Max(0, grossQuantity - consumed);
                    if (quantityAvailable <= 0)
                    {
                        continue;
                    }

                    bool delivers = Rand.Value < RfqService.DeliveryChance(profile, distance);
                    FulfillmentMode fulfillment = delivers
                        ? FulfillmentMode.SellerDelivery
                        : FulfillmentMode.BuyerPickup;
                    int leadTimeDays = RfqService.LeadTimeDays(distance, delivers, supply);
                    float unitPrice = RfqService.SupplierUnitPrice(
                        state, def, stuff, quality, profile, category.Value, supply, distance,
                        delivers, quantityAvailable, out _);
                    int lifespanDays = Rand.RangeInclusive(MinLifespanDays, MaxLifespanDays);

                    created.Add(new SupplierListing
                    {
                        id = idAllocator(),
                        settlementId = settlement.ID,
                        thingDef = def,
                        stuffDef = stuff,
                        quality = quality,
                        quantityAvailable = quantityAvailable,
                        unitPrice = unitPrice,
                        fulfillment = fulfillment,
                        leadTimeDays = leadTimeDays,
                        createdTick = GenTicks.TicksGame,
                        expiryTick = GenTicks.TicksGame + lifespanDays * GenDate.TicksPerDay,
                        refreshWindow = refreshWindow
                    });
                }
            }
            finally
            {
                Rand.PopState();
            }

            return created;
        }

        /// <summary>
        /// Expires the previous window and creates this window's offers on the same coarse market
        /// refresh used by MarketOpportunityGenerator. Existing listings for the current window
        /// are left untouched, making a repeated call idempotent.
        /// </summary>
        public static int Refresh(IntercolonyWorldComponent state)
        {
            if (state == null)
            {
                return 0;
            }

            PruneStale(state);
            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements == null)
            {
                return 0;
            }

            int created = 0;
            foreach (Settlement settlement in settlements)
            {
                if (settlement == null || HasCurrentWindowListing(state, settlement.ID))
                {
                    continue;
                }

                SettlementEconomicProfile profile = state.GetProfile(settlement);
                List<SupplierListing> fresh = GenerateFor(
                    state, settlement, profile, state.RefreshCount, 0, state.NextId);
                foreach (SupplierListing listing in fresh)
                {
                    state.SupplierListings.Add(listing);
                    created++;
                }
            }

            return created;
        }

        private static Dictionary<IntercolonyProductCategory, List<ThingDef>> BuildCandidates(
            IntercolonyWorldComponent state,
            SettlementEconomicProfile profile)
        {
            Dictionary<IntercolonyProductCategory, List<ThingDef>> candidates =
                new Dictionary<IntercolonyProductCategory, List<ThingDef>>();
            foreach (IntercolonyProductCategory category in IntercolonyProductCategoryUtility.All)
            {
                if (EffectiveEconomyService.EffectiveSupply(state, profile, category) <= 0f)
                {
                    continue;
                }

                List<ThingDef> defs = IntercolonyProductClassifier.DefsInCategory(category);
                for (int i = defs.Count - 1; i >= 0; i--)
                {
                    if (!RfqService.CanTechnicallySupply(defs[i], profile))
                    {
                        defs.RemoveAt(i);
                    }
                }

                if (defs.Count > 0)
                {
                    candidates[category] = defs;
                }
            }

            return candidates;
        }

        private static IntercolonyProductCategory? PickCategory(
            IntercolonyWorldComponent state,
            SettlementEconomicProfile profile,
            Dictionary<IntercolonyProductCategory, List<ThingDef>> candidates)
        {
            float total = 0f;
            foreach (IntercolonyProductCategory category in IntercolonyProductCategoryUtility.All)
            {
                if (candidates.ContainsKey(category))
                {
                    total += Mathf.Max(0f,
                        EffectiveEconomyService.EffectiveSupply(state, profile, category));
                }
            }

            if (total <= 0f)
            {
                return null;
            }

            float roll = Rand.Range(0f, total);
            float running = 0f;
            foreach (IntercolonyProductCategory category in IntercolonyProductCategoryUtility.All)
            {
                List<ThingDef> defs;
                if (!candidates.TryGetValue(category, out defs))
                {
                    continue;
                }

                running += Mathf.Max(0f,
                    EffectiveEconomyService.EffectiveSupply(state, profile, category));
                if (roll < running)
                {
                    return category;
                }
            }

            return null;
        }

        private static bool HasCurrentWindowListing(
            IntercolonyWorldComponent state, int settlementId)
        {
            foreach (SupplierListing listing in state.SupplierListings)
            {
                if (listing != null && listing.settlementId == settlementId &&
                    listing.refreshWindow == state.RefreshCount)
                {
                    return true;
                }
            }

            return false;
        }

        private static void PruneStale(IntercolonyWorldComponent state)
        {
            int now = GenTicks.TicksGame;
            for (int i = state.SupplierListings.Count - 1; i >= 0; i--)
            {
                SupplierListing listing = state.SupplierListings[i];
                if (listing == null || listing.refreshWindow != state.RefreshCount ||
                    listing.HasExpired(now) || !listing.IsValidAfterLoad)
                {
                    state.SupplierListings.RemoveAt(i);
                }
            }
        }
    }
}
