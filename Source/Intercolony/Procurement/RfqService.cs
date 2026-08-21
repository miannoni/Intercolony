using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Creates purchase requests and generates supplier responses
    /// (DESIGN.md §19 RFQs, §20 scarcity model, Phase 10 §103).
    ///
    /// §20 calls this "the core anti-vending-machine design", and that is the whole point:
    /// a request must be able to come back with nothing. Everything here is built so that
    /// asking for something scarce, advanced, or far away genuinely fails, rather than always
    /// returning a price with a bigger number on it.
    ///
    /// Quotes are rolled once, at creation, and then stand. Re-rolling on demand would let a
    /// player refresh until they liked the price — the reroll exploit §76.1 warns about.
    /// </summary>
    public static class RfqService
    {
        /// <summary>How long a request and its quotes stand before lapsing.</summary>
        public const int RequestLifespanDays = 6;

        /// <summary>Baseline chance a plausible supplier bothers to answer at all.</summary>
        private const float BaseResponseChance = 0.55f;

        public static PurchaseRequest CreateRequest(
            IntercolonyWorldComponent state,
            ThingDef def,
            ThingDef stuff,
            int quantity,
            int desiredDays,
            ProcurementFulfillmentPreference fulfillmentPreference =
                ProcurementFulfillmentPreference.Either)
        {
            return CreateRequest(state, def, stuff, quantity, desiredDays, fulfillmentPreference, null);
        }

        /// <summary>Creates a request carrying an animal promise when supplied.</summary>
        public static PurchaseRequest CreateRequest(
            IntercolonyWorldComponent state,
            ThingDef def,
            ThingDef stuff,
            int quantity,
            int desiredDays,
            ProcurementFulfillmentPreference fulfillmentPreference,
            AnimalSpec animalSpec)
        {
            return CreateRequest(
                state, def, stuff, quantity, desiredDays, fulfillmentPreference, animalSpec, null);
        }

        /// <summary>Creates a request that also states a minimum workmanship.</summary>
        public static PurchaseRequest CreateRequest(
            IntercolonyWorldComponent state,
            ThingDef def,
            ThingDef stuff,
            int quantity,
            int desiredDays,
            ProcurementFulfillmentPreference fulfillmentPreference,
            AnimalSpec animalSpec,
            QualityCategory? minQuality)
        {
            if (state == null || def == null || quantity <= 0)
            {
                return null;
            }

            PurchaseRequest request = new PurchaseRequest
            {
                id = state.NextId(),
                thingDef = def,
                stuffDef = stuff,
                quantityRequested = quantity,
                desiredDays = Mathf.Max(1, desiredDays),
                createdTick = GenTicks.TicksGame,
                expiryTick = GenTicks.TicksGame + RequestLifespanDays * GenDate.TicksPerDay,
                status = PurchaseRequestStatus.Open,
                fulfillmentPreference = fulfillmentPreference,
                animalSpec = animalSpec?.Copy(),

                // Only meaningful for things that carry workmanship at all; storing it on
                // anything else would show a constraint the player cannot have asked for.
                minQuality = animalSpec == null && IntercolonyPricing.CanHaveQuality(def)
                    ? minQuality
                    : null
            };

            GenerateResponses(state, request);
            state.AddRequest(request);

            if (request.AnyQuotes)
            {
                IntercolonyLog.Message(
                    $"Request {request.id}: {quantity}x {def.label} — {request.quotes.Count} quote(s).");
                Messages.Message(
                    $"{request.quotes.Count} supplier(s) answered your request for {quantity}x {def.label}.",
                    MessageTypeDefOf.NeutralEvent, historical: false);
            }
            else
            {
                IntercolonyLog.Message(
                    $"Request {request.id}: {quantity}x {def.label} — no quotes ({request.noResponseReason}).");
                Messages.Message(
                    $"No supplier can provide {quantity}x {def.label} right now.",
                    MessageTypeDefOf.CautionInput, historical: false);
            }

            return request;
        }

        /// <summary>
        /// Rolls each accessible settlement's answer. Seeded on the market refresh and requested
        /// def so repeating the same request within one market window produces the same quotes,
        /// which keeps save/load stable and makes a reported problem reproducible (§60).
        /// </summary>
        /// <remarks>
        /// Internal rather than private so the Stage 0.2 market baseline can quote a throwaway
        /// request without adding it to world state or announcing it to the player. The baseline
        /// has to measure the real quoting path — a diagnostic that reimplemented it would be
        /// measuring itself.
        /// </remarks>
        internal static void GenerateResponses(IntercolonyWorldComponent state, PurchaseRequest request)
        {
            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements == null)
            {
                request.noResponseReason = "no settlements";
                return;
            }

            IntercolonyProductCategory? category = request.IsAnimalOrder
                ? IntercolonyProductCategory.Commodities
                : IntercolonyProductClassifier.Classify(request.thingDef);
            if (!category.HasValue)
            {
                request.noResponseReason = "nobody trades this";
                return;
            }

            int considered = 0;
            int couldNotSupply = 0;

            Rand.PushState(Gen.HashCombineInt(
                state.EconomySeed, state.RefreshCount, request.thingDef.shortHash, 0x7C21));
            try
            {
                foreach (Settlement settlement in settlements)
                {
                    if (!IntercolonyMarketAccess.IsAccessible(settlement))
                    {
                        continue;
                    }

                    SettlementEconomicProfile profile = state.GetProfile(settlement);
                    if (profile == null)
                    {
                        continue;
                    }

                    considered++;
                    Quotation quote = TryQuote(state, request, settlement, profile, category.Value);
                    if (quote != null)
                    {
                        // Roll the complete quotation first, then subtract stock already bought.
                        // This preserves both the deterministic offer and every later random draw.
                        quote.refreshWindow = state.RefreshCount;
                        int consumed = state.SupplierOfferConsumptionFor(
                            quote.refreshWindow, request.thingDef, settlement.ID);
                        quote.quantityOffered = Mathf.Max(0, quote.quantityOffered - consumed);
                        if (quote.quantityOffered == 0)
                        {
                            couldNotSupply++;
                            continue;
                        }

                        quote.id = state.NextId();
                        request.quotes.Add(quote);
                    }
                    else
                    {
                        couldNotSupply++;
                    }
                }
            }
            finally
            {
                Rand.PopState();
            }

            // Sort cheapest-complete first; the UI can re-sort, but the default should be the
            // answer the player most often wants.
            request.quotes.Sort(CompareQuotes);

            if (!request.AnyQuotes)
            {
                request.noResponseReason = considered == 0
                    ? "you have no reachable trading partners"
                    : $"none of {couldNotSupply} reachable suppliers can provide this";
            }
        }

        private static int CompareQuotes(Quotation a, Quotation b)
        {
            // Complete quotes outrank partial ones — a partial answer is worth less than a
            // whole one even when it is cheaper in absolute silver.
            bool aFull = a.quantityOffered >= b.quantityOffered;
            if (a.quantityOffered != b.quantityOffered)
            {
                return b.quantityOffered.CompareTo(a.quantityOffered);
            }

            int byPrice = a.TotalPrice.CompareTo(b.TotalPrice);
            return byPrice != 0 ? byPrice : a.leadTimeDays.CompareTo(b.leadTimeDays);
        }

        /// <summary>
        /// One settlement's answer, or null for silence. Must be called inside a pushed Rand
        /// state.
        ///
        /// The factors are §20's list: category, settlement technology, profile, requested
        /// quantity, distance, and random variation.
        /// </summary>
        private static Quotation TryQuote(
            IntercolonyWorldComponent state,
            PurchaseRequest request,
            Settlement settlement,
            SettlementEconomicProfile profile,
            IntercolonyProductCategory category)
        {
            // §50 applied per *def*, not per category. Category supply weights treat a bionic
            // ear and a shirt as the same capability, so any settlement that makes clothing
            // appeared able to make bionics. With ~30 reachable settlements each rolling an
            // independent chance to answer, that made a failed request effectively impossible
            // (odds of all declining were about 1 in 5 million) and turned procurement into
            // the vending machine §20 exists to prevent.
            if (!CanTechnicallySupply(request.thingDef, profile))
            {
                return null;
            }

            float supply = EffectiveEconomyService.EffectiveSupply(state, profile, category);
            if (supply < 0.35f)
            {
                return null;
            }

            float distance = MarketOpportunityGenerator.DistanceToPlayer(settlement);

            // Distant suppliers answer less often: they have local buyers too.
            float distancePenalty = distance < 0f ? 0f : Mathf.Min(distance, 200f) / 200f * 0.3f;
            float chance = Mathf.Clamp01(BaseResponseChance * supply - distancePenalty);
            if (Rand.Value > chance)
            {
                return null;
            }

            // Material is chosen *before* pricing and quantity, and both use it. Picking it
            // afterwards meant a supplier could offer a gold bed and charge the price of a
            // stuffless one — 120 silver for several hundred silver of gold, which is a money
            // exploit by way of the deconstruct button.
            ThingDef offeredStuff = request.IsAnimalOrder
                ? null
                : request.stuffDef ?? PickSupplierStuff(request.thingDef);
            QualityCategory? offeredQuality = request.IsAnimalOrder
                ? (QualityCategory?)null
                : PickOfferedQuality(request.thingDef, profile, request.minQuality);

            // A settlement that cannot work to the standard asked for does not quote. Offering
            // below the floor would be answering a different question, and quietly raising
            // every supplier to it would make the floor free.
            if (request.minQuality.HasValue && !request.IsAnimalOrder &&
                (!offeredQuality.HasValue || offeredQuality.Value < request.minQuality.Value))
            {
                return null;
            }

            int offered = OfferedQuantity(request, offeredStuff, profile, supply);
            if (offered <= 0)
            {
                return null;
            }

            bool delivers = request.fulfillmentPreference ==
                                ProcurementFulfillmentPreference.SupplierDelivers ||
                            (request.fulfillmentPreference == ProcurementFulfillmentPreference.Either &&
                             Rand.Value < DeliveryChance(profile, distance));
            float unitPrice = QuotedUnitPrice(request, offeredStuff, offeredQuality, profile,
                category, supply, distance, delivers, out string explanation);
            int leadTime = LeadTimeDays(distance, delivers, supply);

            return new Quotation
            {
                offeredQuality = offeredQuality,
                offeredStuff = offeredStuff,
                animalSpec = request.animalSpec?.Copy(),
                settlementId = settlement.ID,
                settlementName = settlement.Label ?? "unnamed",
                factionName = settlement.Faction?.Name ?? "",
                quantityOffered = offered,
                unitPrice = unitPrice,
                leadTimeDays = leadTime,
                supplierDelivers = delivers,
                distanceTiles = distance,
                priceExplanation = explanation
            };
        }

        /// <summary>
        /// Quality the supplier will provide, centred on how much that settlement cares about
        /// craftsmanship. Must be called inside a pushed Rand state.
        /// </summary>
        private static QualityCategory? PickOfferedQuality(
            ThingDef def, SettlementEconomicProfile profile, QualityCategory? minQuality)
        {
            if (!IntercolonyPricing.CanHaveQuality(def))
            {
                return null;
            }

            float roll = Rand.Value * 0.6f + profile.qualityPreference * 0.4f;
            QualityCategory natural =
                roll > 0.82f ? QualityCategory.Excellent :
                roll > 0.62f ? QualityCategory.Good :
                roll > 0.3f ? QualityCategory.Normal :
                QualityCategory.Poor;

            if (!minQuality.HasValue)
            {
                return natural;
            }

            // A settlement can stretch to better work than it habitually produces, but only so
            // far. Beyond that ceiling it declines rather than promising what it cannot make,
            // which is what makes a high floor a real trade-off instead of a free upgrade.
            if (minQuality.Value > BestQualityAvailableFrom(profile))
            {
                return natural < minQuality.Value ? (QualityCategory?)null : natural;
            }

            return natural < minQuality.Value ? minQuality.Value : natural;
        }

        /// <summary>The finest work a settlement will take on, from its quality preference.</summary>
        private static QualityCategory BestQualityAvailableFrom(SettlementEconomicProfile profile)
        {
            float preference = profile?.qualityPreference ?? 1f;
            if (preference >= 1.15f) return QualityCategory.Masterwork;
            if (preference >= 0.9f) return QualityCategory.Excellent;
            if (preference >= 0.6f) return QualityCategory.Good;
            return QualityCategory.Normal;
        }

        /// <summary>Material the supplier happens to work in. Must be called inside a pushed Rand state.</summary>
        private static ThingDef PickSupplierStuff(ThingDef def)
        {
            if (def == null || !def.MadeFromStuff)
            {
                return null;
            }

            List<ThingDef> options = new List<ThingDef>();
            foreach (ThingDef candidate in GenStuff.AllowedStuffsFor(def))
            {
                if (candidate.BaseMarketValue > 0f)
                {
                    options.Add(candidate);
                }
            }

            return options.Count == 0 ? GenStuff.DefaultStuffFor(def) : options[Rand.Range(0, options.Count)];
        }

        /// <summary>
        /// Whether a settlement's tech base could plausibly produce or stock this specific
        /// item (DESIGN.md §50: "A tribal settlement should not routinely supply fabrication
        /// benches").
        ///
        /// This is what makes scarcity structural rather than statistical. Randomness across
        /// many settlements always finds someone; a hard capability gate does not.
        /// </summary>
        public static bool CanTechnicallySupply(ThingDef def, SettlementEconomicProfile profile)
        {
            TechLevel required = def.techLevel;

            // Most raw goods leave techLevel unset; those are universally available.
            if (required == TechLevel.Undefined || required <= profile.techTier)
            {
                return true;
            }

            // Trade hubs import, so they can source one tier above their own base — but only
            // one, and only sometimes. Everything further out is genuinely unobtainable here,
            // which is the point: some things you cannot buy locally at any price.
            TechLevel oneTierUp = (TechLevel)((int)profile.techTier + 1);
            if (profile.archetype == IntercolonyArchetype.TradeHub && required <= oneTierUp)
            {
                return Rand.Value < 0.5f;
            }

            return false;
        }

        /// <summary>
        /// How much a supplier can actually spare. Partial answers are a first-class outcome
        /// (§20), so this is deliberately allowed to fall short of the request.
        /// </summary>
        private static int OfferedQuantity(
            PurchaseRequest request, ThingDef stuff, SettlementEconomicProfile profile, float supply)
        {
            float capacity = SupplyCapacity(profile.wealthTier) * supply;
            float unitValue = Mathf.Max(0.4f, request.IsAnimalOrder
                ? IntercolonyPricing.BaseValue(request.thingDef, null, request.animalSpec)
                : IntercolonyPricing.BaseValue(request.thingDef, stuff));
            int affordable = Mathf.RoundToInt(capacity / unitValue);

            // Crated goods and single-stack items are produced, not stockpiled in bulk.
            if (request.thingDef.category == ThingCategory.Building)
            {
                affordable = Mathf.Min(affordable, 6);
            }
            else if (request.thingDef.stackLimit <= 1)
            {
                affordable = Mathf.Min(affordable, 12);
            }

            int offered = Mathf.Min(request.quantityRequested, affordable);

            // Even a capable supplier often cannot fill a large order outright, which is what
            // makes "shop around and combine" a real decision rather than flavour text.
            if (offered >= request.quantityRequested && Rand.Value < 0.35f)
            {
                offered = Mathf.RoundToInt(request.quantityRequested * Rand.Range(0.4f, 0.85f));
            }

            return Mathf.Max(0, offered);
        }

        private static float SupplyCapacity(IntercolonyWealthTier wealth)
        {
            switch (wealth)
            {
                case IntercolonyWealthTier.Destitute: return 700f;
                case IntercolonyWealthTier.Modest: return 1800f;
                case IntercolonyWealthTier.Comfortable: return 4000f;
                default: return 8000f;
            }
        }

        /// <summary>
        /// Buying costs more than selling. The player is the one who needs something, so the
        /// spread runs against them — a settlement with little of a good charges more for it,
        /// which is how scarcity shows up in the price rather than only in availability (§46).
        /// </summary>
        /// <summary>
        /// What a supplier adds over a good's base value for selling it to you.
        ///
        /// Named because §45's margin estimate has to answer "should I buy inputs or produce them?"
        /// with the same number a real quote would use. Two copies of it would let the dashboard
        /// recommend buying at a price procurement does not actually offer.
        /// </summary>
        public const float SupplierMargin = 1.15f;

        private static float QuotedUnitPrice(
            PurchaseRequest request,
            ThingDef stuff,
            QualityCategory? quality,
            SettlementEconomicProfile profile,
            IntercolonyProductCategory category,
            float supply,
            float distance,
            bool delivers,
            out string explanation)
        {
            List<PriceFactor> factors = new List<PriceFactor>();
            float baseValue = request.IsAnimalOrder
                ? IntercolonyPricing.BaseValue(request.thingDef, null, request.animalSpec)
                : IntercolonyPricing.BaseValue(request.thingDef, stuff);

            // Better craftsmanship costs more, the same way a quality floor does on the sell
            // side. Without this a supplier could offer Excellent work at Normal prices.
            if (!request.IsAnimalOrder && quality.HasValue)
            {
                factors.Add(new PriceFactor(
                    $"{quality.Value.GetLabel()} workmanship", QualityCostFactor(quality.Value)));
            }

            // Buyer's spread: the counterparty is selling, so they mark up.
            factors.Add(new PriceFactor("Supplier margin", SupplierMargin));

            // Scarcity: a supplier with plenty charges less than one scraping the barrel.
            float scarcity = Mathf.Clamp(1.6f - supply * 0.5f, 0.9f, 1.6f);
            factors.Add(new PriceFactor("Local scarcity", scarcity));

            if (distance >= 0f)
            {
                float haul = 1f + Mathf.Min(distance, 150f) * 0.0012f;
                factors.Add(new PriceFactor("Distance", haul));
            }

            // Wealthy settlements are not desperate for the sale.
            float wealth = profile.wealthTier >= IntercolonyWealthTier.Comfortable ? 1.08f : 0.96f;
            factors.Add(new PriceFactor("Supplier standing", wealth));

            factors.Add(new PriceFactor("Negotiation", Rand.Range(0.94f, 1.1f)));

            factors.Add(ProcurementLogisticsFactor(delivers));
            factors.Add(IntercolonyPricing.BuyingEconomyDifficultyFactor());

            float price = baseValue;
            foreach (PriceFactor factor in factors)
            {
                price *= factor.multiplier;
            }

            price = Mathf.Max(0.01f, price);
            explanation = request.IsAnimalOrder
                ? IntercolonyPricing.Explain(request.thingDef, null, request.animalSpec,
                    request.quantityRequested, price, factors)
                : IntercolonyPricing.Explain(
                    request.thingDef, stuff, request.quantityRequested, price, factors);
            return price;
        }

        /// <summary>
        /// Supplier delivery is a paid service; collection carries no logistics fee because
        /// the player's caravan absorbs that cost instead.
        /// </summary>
        public static PriceFactor ProcurementLogisticsFactor(bool supplierDelivers)
        {
            return supplierDelivers
                ? new PriceFactor("Supplier delivery", 1.12f)
                : new PriceFactor("You collect", 1f);
        }

        /// <summary>What a supplier charges for better work. Mirrors the sell-side premium.</summary>
        private static float QualityCostFactor(QualityCategory quality)
        {
            switch (quality)
            {
                case QualityCategory.Awful: return 0.6f;
                case QualityCategory.Poor: return 0.8f;
                case QualityCategory.Normal: return 1f;
                case QualityCategory.Good: return 1.3f;
                case QualityCategory.Excellent: return 1.75f;
                case QualityCategory.Masterwork: return 2.5f;
                default: return 3.8f;
            }
        }

        private static float DeliveryChance(SettlementEconomicProfile profile, float distance)
        {
            // Delivery is a service; better-off and closer settlements offer it more (§25.4).
            float chance = profile.wealthTier >= IntercolonyWealthTier.Comfortable ? 0.5f : 0.25f;
            if (distance > 60f)
            {
                chance *= 0.5f;
            }

            return chance;
        }

        private static int LeadTimeDays(float distance, bool delivers, float supply)
        {
            // Pickup is "ready in N days"; delivery adds travel on top.
            int prep = Mathf.RoundToInt(Mathf.Lerp(5f, 1f, Mathf.Clamp01(supply / 2f)));
            if (!delivers)
            {
                return Mathf.Max(1, prep + Rand.RangeInclusive(0, 2));
            }

            int travel = distance < 0f ? 3 : Mathf.RoundToInt(distance / 12f);
            return Mathf.Max(1, prep + travel);
        }

        /// <summary>Lapses requests past their expiry. Called from the coarse refresh.</summary>
        public static int ExpireStale(List<PurchaseRequest> requests)
        {
            int now = GenTicks.TicksGame;
            int expired = 0;
            foreach (PurchaseRequest request in requests)
            {
                if (request.IsOpen && request.HasExpired(now))
                {
                    request.TryExpire();
                    expired++;
                }
            }

            return expired;
        }
    }
}
