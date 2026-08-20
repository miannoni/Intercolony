using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>One settlement's appetite for a specific good (DESIGN.md §12).</summary>
    public class BuyerOffer
    {
        public Settlement settlement;
        public SettlementEconomicProfile profile;

        /// <summary>What the offer is for. Carried so creating the sale needs no extra context.</summary>
        public ThingDef def;

        public ThingDef stuff;

        /// <summary>
        /// Non-null only for an existing-animal offer. The race remains <see cref="def"/>,
        /// just as it does on an animal order line.
        /// </summary>
        public AnimalSpec animalSpec;

        public bool IsAnimalOffer => animalSpec != null;

        /// <summary>Most units this settlement would take before saturating (§13).</summary>
        public int maxQuantity;

        /// <summary>
        /// Unit price for the quantity actually offered, with the default buyer-pickup terms.
        /// </summary>
        public float unitPrice;

        /// <summary>Units this offer covers — the lesser of what is held and what is wanted.</summary>
        public int quantity;

        public float distanceTiles;

        /// <summary>Set when the settlement will not buy this at all, with the reason shown to the player.</summary>
        public string noInterestReason;

        public bool Interested => noInterestReason == null && maxQuantity > 0;

        public int TotalPrice => Mathf.RoundToInt(unitPrice * quantity);

        /// <summary>Price factor breakdown, for the §47 tooltip.</summary>
        public List<PriceFactor> factors = new List<PriceFactor>();
    }

    /// <summary>
    /// Anonymous, presently sellable colony animals with the same promise-relevant state.
    /// This is deliberately a read model: it carries no pawn identity, relationship, training,
    /// or reservation. A later caravan handoff must revalidate the live pawns it receives.
    /// </summary>
    public class AnimalStockGroup
    {
        public ThingDef race;
        public AnimalSpec spec;
        public int quantity;
    }

    /// <summary>
    /// "I already have a huge surplus. Who wants it?" (DESIGN.md §12, Phase 9 §102).
    ///
    /// Deliberately does **not** search posted opportunities. A surplus rarely matches
    /// whatever a settlement happened to advertise, and §12's worked example shows demand
    /// bands and a "No current interest" row — that is latent appetite derived from
    /// settlement profiles, not a listing lookup. Searching listings would answer a much less
    /// useful question and would usually return nothing.
    /// </summary>
    public static class FindBuyerService
    {
        /// <summary>
        /// Below this category demand weight a settlement is simply not in the market for the
        /// good, and is reported as uninterested rather than quoted a derisory price.
        ///
        /// Demand weights cluster around 1.0, so this has to sit close to that to bite at all.
        /// At 0.55 every settlement in a 31-settlement world was interested in everything,
        /// which made §12's "No current interest" outcome dead code and flattened the ranking:
        /// if everyone buys everything, choosing a buyer stops being a decision.
        /// </summary>
        internal const float InterestThreshold = 0.9f;

        /// <summary>
        /// Who would buy <paramref name="quantity"/> of this good, best offer first.
        /// Uninterested settlements are included but sort last: §12 shows them explicitly, and
        /// "nobody near you wants this" is a useful answer.
        /// </summary>
        public static List<BuyerOffer> FindBuyers(
            IntercolonyWorldComponent state,
            ThingDef def,
            ThingDef stuff,
            int quantity,
            bool includeUninterested = true)
        {
            List<BuyerOffer> offers = new List<BuyerOffer>();
            if (state == null || def == null || quantity <= 0)
            {
                return offers;
            }

            IntercolonyProductCategory? category = IntercolonyProductClassifier.Classify(def);
            if (!category.HasValue)
            {
                return offers;
            }

            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements == null)
            {
                return offers;
            }

            foreach (Settlement settlement in settlements)
            {
                if (!IntercolonyMarketAccess.IsAccessible(settlement, out string reason))
                {
                    continue;
                }

                SettlementEconomicProfile profile = state.GetProfile(settlement);
                if (profile == null)
                {
                    continue;
                }

                BuyerOffer offer = Evaluate(
                    state, settlement, profile, def, stuff, category.Value, quantity);
                if (offer.Interested || includeUninterested)
                {
                    offers.Add(offer);
                }
            }

            offers.Sort(CompareOffers);
            return offers;
        }

        /// <summary>
        /// Who would buy this anonymous group of colony animals, best offer first. Animals are
        /// intentionally in the commodities demand bucket: they are pawns rather than product
        /// classifier inputs, and must not widen that classifier's pawn exclusion.
        /// </summary>
        public static List<BuyerOffer> FindAnimalBuyers(
            IntercolonyWorldComponent state,
            AnimalStockGroup group,
            bool includeUninterested = true)
        {
            if (group == null)
            {
                return new List<BuyerOffer>();
            }

            return FindAnimalBuyers(
                state, group.race, group.spec, group.quantity, includeUninterested);
        }

        /// <summary>Animal overload kept separate from goods so the product classifier stays pawn-free.</summary>
        public static List<BuyerOffer> FindAnimalBuyers(
            IntercolonyWorldComponent state,
            ThingDef race,
            AnimalSpec spec,
            int quantity,
            bool includeUninterested = true)
        {
            List<BuyerOffer> offers = new List<BuyerOffer>();
            if (state == null || race == null || spec == null || quantity <= 0 ||
                !spec.IsValidFor(race))
            {
                return offers;
            }

            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements == null)
            {
                return offers;
            }

            foreach (Settlement settlement in settlements)
            {
                if (!IntercolonyMarketAccess.IsAccessible(settlement, out _))
                {
                    continue;
                }

                SettlementEconomicProfile profile = state.GetProfile(settlement);
                if (profile == null)
                {
                    continue;
                }

                BuyerOffer offer = EvaluateAnimal(
                    state, settlement, profile, race, spec, quantity);
                if (offer.Interested || includeUninterested)
                {
                    offers.Add(offer);
                }
            }

            offers.Sort(CompareOffers);
            return offers;
        }

        private static int CompareOffers(BuyerOffer a, BuyerOffer b)
        {
            // Interested first, then by what the player would actually receive.
            if (a.Interested != b.Interested)
            {
                return a.Interested ? -1 : 1;
            }

            int byTotal = b.TotalPrice.CompareTo(a.TotalPrice);
            if (byTotal != 0)
            {
                return byTotal;
            }

            // Equal money: prefer the shorter haul.
            float da = a.distanceTiles < 0f ? float.MaxValue : a.distanceTiles;
            float db = b.distanceTiles < 0f ? float.MaxValue : b.distanceTiles;
            return da.CompareTo(db);
        }

        private static BuyerOffer Evaluate(
            IntercolonyWorldComponent state,
            Settlement settlement,
            SettlementEconomicProfile profile,
            ThingDef def,
            ThingDef stuff,
            IntercolonyProductCategory category,
            int wantedQuantity)
        {
            BuyerOffer offer = new BuyerOffer
            {
                settlement = settlement,
                profile = profile,
                def = def,
                stuff = stuff,
                distanceTiles = MarketOpportunityGenerator.DistanceToPlayer(settlement)
            };

            float demand = profile.BaseDemandFor(def, category);
            if (demand < InterestThreshold)
            {
                offer.noInterestReason = "no current interest";
                return offer;
            }

            int maxAppetite = MaximumAppetite(def, stuff, profile, category);
            if (maxAppetite <= 0)
            {
                offer.noInterestReason = "cannot afford a worthwhile lot";
                return offer;
            }

            offer.maxQuantity = Mathf.Max(
                0, maxAppetite - ConsumedAppetite(state, settlement.ID, def));
            if (offer.maxQuantity <= 0)
            {
                offer.noInterestReason = "already buying enough";
                return offer;
            }

            offer.quantity = Mathf.Min(wantedQuantity, offer.maxQuantity);
            offer.unitPrice = SellRateFor(
                offer, offer.quantity, FulfillmentMode.BuyerPickup,
                out List<PriceFactor> factors);
            offer.factors = factors;

            return offer;
        }

        private static BuyerOffer EvaluateAnimal(
            IntercolonyWorldComponent state,
            Settlement settlement,
            SettlementEconomicProfile profile,
            ThingDef race,
            AnimalSpec spec,
            int wantedQuantity)
        {
            BuyerOffer offer = new BuyerOffer
            {
                settlement = settlement,
                profile = profile,
                def = race,
                animalSpec = spec.Copy(),
                distanceTiles = MarketOpportunityGenerator.DistanceToPlayer(settlement)
            };

            const IntercolonyProductCategory category = IntercolonyProductCategory.Commodities;
            float demand = profile.BaseDemandFor(race, category);
            if (demand < InterestThreshold)
            {
                offer.noInterestReason = "no current interest";
                return offer;
            }

            int maxAppetite = MaxAnimalAppetite(race, offer.animalSpec, profile, demand);
            if (maxAppetite <= 0)
            {
                offer.noInterestReason = "cannot afford a worthwhile lot";
                return offer;
            }

            offer.maxQuantity = Mathf.Max(
                0, maxAppetite - ConsumedAppetite(state, settlement.ID, race));
            if (offer.maxQuantity <= 0)
            {
                offer.noInterestReason = "already buying enough";
                return offer;
            }

            offer.quantity = Mathf.Min(wantedQuantity, offer.maxQuantity);
            offer.unitPrice = SellRateFor(
                offer, offer.quantity, FulfillmentMode.BuyerPickup,
                out List<PriceFactor> factors);
            offer.factors = factors;
            return offer;
        }

        /// <summary>
        /// Unit rate a buyer pays for this lot size and fulfilment mode. Shared by the listing
        /// and confirmation so the advertised rate is the rate used to create the order.
        /// </summary>
        internal static float SellRateFor(
            BuyerOffer offer, int quantity, FulfillmentMode fulfillment)
        {
            return SellRateFor(offer, quantity, fulfillment, out _);
        }

        private static float SellRateFor(
            BuyerOffer offer,
            int quantity,
            FulfillmentMode fulfillment,
            out List<PriceFactor> factors)
        {
            float rate;
            if (offer?.def == null || offer.profile == null)
            {
                // BuyerOffer.unitPrice is stored with the listing's default pickup terms.
                // Recover its pre-logistics rate before applying the mode currently selected.
                PriceFactor listingLogistics =
                    IntercolonyPricing.LogisticsFactor(FulfillmentMode.BuyerPickup);
                float storedRate = offer?.unitPrice ?? 0f;
                factors = offer?.factors == null
                    ? new List<PriceFactor>()
                    : new List<PriceFactor>(offer.factors);
                if (listingLogistics.multiplier <= 0f || float.IsNaN(listingLogistics.multiplier) ||
                    float.IsInfinity(listingLogistics.multiplier))
                {
                    IntercolonyLog.Error($"Find Buyer unit price for {offer?.def?.defName ?? "<null>"} is using its stored unscaled value because the buyer-pickup logistics multiplier {listingLogistics.multiplier} is invalid.");
                    return storedRate;
                }
                rate = storedRate / listingLogistics.multiplier;
                int lastFactor = factors.Count - 1;
                if (lastFactor >= 0 &&
                    factors[lastFactor].label == listingLogistics.label &&
                    Mathf.Approximately(
                        factors[lastFactor].multiplier, listingLogistics.multiplier))
                {
                    factors.RemoveAt(lastFactor);
                }
            }
            else
            {
                IntercolonyProductCategory category =
                    IntercolonyProductClassifier.Classify(offer.def)
                    ?? IntercolonyProductCategory.Commodities;

                rate = offer.IsAnimalOffer
                    ? IntercolonyPricing.UnitPrice(
                        offer.def, null, offer.animalSpec, Mathf.Max(1, quantity),
                        offer.profile, IntercolonyProductCategory.Commodities,
                        offer.distanceTiles, null, out factors)
                    : IntercolonyPricing.UnitPrice(
                        offer.def, offer.stuff, Mathf.Max(1, quantity), offer.profile,
                        category, offer.distanceTiles, null, out factors);
            }

            PriceFactor logistics = IntercolonyPricing.LogisticsFactor(fulfillment);
            factors.Add(logistics);
            return rate * logistics.multiplier;
        }

        /// <summary>
        /// How much of this good a settlement would absorb, expressed in units rather than
        /// silver so the player can compare it against a stockpile (§12's "Demand: up to
        /// 2,000"). Bounded by wealth and appetite, then clamped by how the good travels —
        /// the same crated-goods reasoning as generation (docs/unique-goods-spike.md).
        /// </summary>
        internal static int MaximumAppetite(
            ThingDef def,
            ThingDef stuff,
            SettlementEconomicProfile profile,
            IntercolonyProductCategory category)
        {
            if (def == null || profile == null)
            {
                return 0;
            }

            float demand = profile.BaseDemandFor(def, category);
            return MaxAppetite(def, stuff, profile, demand);
        }

        private static int MaxAppetite(
            ThingDef def, ThingDef stuff, SettlementEconomicProfile profile, float demand)
        {
            float budget = WealthBudget(profile.wealthTier) * demand;
            float unitValue = Mathf.Max(0.4f, IntercolonyPricing.BaseValue(def, stuff));
            int units = Mathf.RoundToInt(budget / unitValue);
            int tier = (int)profile.wealthTier;

            if (def.category == ThingCategory.Building)
            {
                return Mathf.Clamp(units, 0, AppetiteCeiling(
                    profile, def, stuff, 1 + tier * 2, 3 + tier * 3));
            }

            if (def.stackLimit <= 1)
            {
                return Mathf.Clamp(units, 0, AppetiteCeiling(
                    profile, def, stuff, 2 + tier * 3, 5 + tier * 5));
            }

            return Mathf.Clamp(units, 0, AppetiteCeiling(
                profile, def, stuff, 2000 + tier * 750, 2750 + tier * 750));
        }

        /// <summary>
        /// Animal demand is deliberately measured in a small number of heads, rather than the
        /// large stack-based appetite used by cargo. The specification value still constrains
        /// the lot through the same settlement wealth budget used by goods.
        /// </summary>
        private static int MaxAnimalAppetite(
            ThingDef race, AnimalSpec spec, SettlementEconomicProfile profile, float demand)
        {
            float budget = WealthBudget(profile.wealthTier) * demand;
            float animalValue = Mathf.Max(0.4f, IntercolonyPricing.BaseValue(race, null, spec));
            int affordableHeads = Mathf.RoundToInt(budget / animalValue);
            int tier = (int)profile.wealthTier;
            int headCeiling = 3 + tier * 2;
            return Mathf.Clamp(affordableHeads, 0, headCeiling);
        }

        /// <summary>
        /// Quantity this settlement has already committed to buy in the current refresh
        /// window. Open orders remain commitments across refreshes; completed orders count
        /// only until the next refresh advances the window.
        /// </summary>
        private static int ConsumedAppetite(
            IntercolonyWorldComponent state, int settlementId, ThingDef def)
        {
            if (state == null || def == null)
            {
                return 0;
            }

            int consumed = 0;
            foreach (SalesOrder order in state.Orders)
            {
                if (order == null || order.settlementId != settlementId || order.ThingDef != def)
                {
                    continue;
                }

                bool completedThisRefresh =
                    order.status == SalesOrderStatus.Completed &&
                    order.completedTick != SalesOrder.NeverCompletedTick &&
                    order.completedTick >= state.LastRefreshTick;
                if (order.IsOpen || completedThisRefresh)
                {
                    consumed += order.Quantity;
                }
            }

            return consumed;
        }

        /// <summary>
        /// Stable per-settlement variation keeps low-volume goods from exposing one universal
        /// clamp while leaving RimWorld's global random stream untouched.
        /// </summary>
        private static int AppetiteCeiling(
            SettlementEconomicProfile profile, ThingDef def, ThingDef stuff, int min, int max)
        {
            int seed = Gen.HashCombineInt(
                profile.seed, def.shortHash, stuff?.shortHash ?? 0, 0x4150_5045);
            Rand.PushState(seed);
            try
            {
                return Rand.RangeInclusive(min, max);
            }
            finally
            {
                Rand.PopState();
            }
        }

        private static float WealthBudget(IntercolonyWealthTier wealth)
        {
            switch (wealth)
            {
                case IntercolonyWealthTier.Destitute: return 900f;
                case IntercolonyWealthTier.Modest: return 2200f;
                case IntercolonyWealthTier.Comfortable: return 4500f;
                default: return 9000f;
            }
        }

        /// <summary>
        /// Colony stock worth offering, as def -> count. Counts only what is in storage:
        /// loose items scattered across the map are not a surplus the player is choosing to
        /// sell, and including them would make the list unusable.
        /// </summary>
        public static List<KeyValuePair<ThingDef, int>> ColonyStock(Map map)
        {
            List<KeyValuePair<ThingDef, int>> result = new List<KeyValuePair<ThingDef, int>>();
            if (map == null)
            {
                return result;
            }

            Dictionary<ThingDef, int> counts = new Dictionary<ThingDef, int>();
            foreach (Thing thing in map.listerThings.AllThings)
            {
                Thing inner = thing.GetInnerIfMinified();
                if (inner?.def == null || !IntercolonyProductClassifier.IsFungibleTradeItem(inner.def))
                {
                    continue;
                }

                if (!OrderValidator.IsAvailableColonyStock(thing))
                {
                    continue;
                }

                int units = OrderValidator.CountableUnits(thing);
                counts.TryGetValue(inner.def, out int existing);
                counts[inner.def] = existing + units;
            }

            foreach (KeyValuePair<ThingDef, int> entry in counts)
            {
                result.Add(entry);
            }

            result.Sort((a, b) => b.Value.CompareTo(a.Value));
            return result;
        }

        /// <summary>
        /// Physical colony stock still free to promise through Find Buyer. This is a read model
        /// over storage and existing orders, not a physical reservation: pawns remain free to
        /// consume, haul, move or lose the goods.
        /// </summary>
        public static List<KeyValuePair<ThingDef, int>> AvailableColonyStock(
            IntercolonyWorldComponent state, Map map)
        {
            List<KeyValuePair<ThingDef, int>> result = new List<KeyValuePair<ThingDef, int>>();
            foreach (KeyValuePair<ThingDef, int> entry in ColonyStock(map))
            {
                int available = Mathf.Max(0, entry.Value - CommittedQuantity(state, entry.Key));
                if (available > 0)
                {
                    result.Add(new KeyValuePair<ThingDef, int>(entry.Key, available));
                }
            }

            result.Sort((a, b) => b.Value.CompareTo(a.Value));
            return result;
        }

        /// <summary>
        /// Spawned player-owned animal candidates that pass the full sale predicate. Exposed
        /// for the self-test and for read-only discovery; callers must not treat this list as
        /// a reservation or retain the pawn references for later fulfilment.
        /// </summary>
        public static List<Pawn> EligibleColonyAnimalCandidates(Map map)
        {
            List<Pawn> result = new List<Pawn>();
            if (map?.mapPawns?.AllPawnsSpawned == null)
            {
                return result;
            }

            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (AnimalTradeUtility.IsEligibleForSale(pawn))
                {
                    result.Add(pawn);
                }
            }

            return result;
        }

        /// <summary>
        /// Presently sellable colony animals grouped by race and each current promise-relevant
        /// trait. A missing or ambiguous life stage is not offered, because it cannot form a
        /// valid, unambiguous <see cref="AnimalSpec"/> promise.
        /// </summary>
        public static List<AnimalStockGroup> ColonyAnimals(Map map)
        {
            List<AnimalStockGroup> result = new List<AnimalStockGroup>();
            foreach (Pawn pawn in EligibleColonyAnimalCandidates(map))
            {
                ThingDef race = pawn.def;
                LifeStageDef lifeStage = pawn.ageTracker?.CurLifeStage;
                if (race == null || lifeStage == null || !HasUnambiguousLifeStage(race, lifeStage))
                {
                    continue;
                }

                bool pregnant = pawn.health?.hediffSet?.GetFirstHediffOfDef(HediffDefOf.Pregnant) != null;
                AnimalStockGroup group = FindAnimalGroup(result, race, pawn.gender, lifeStage, pregnant);
                if (group == null)
                {
                    AnimalSpec spec = new AnimalSpec
                    {
                        gender = pawn.gender,
                        lifeStage = lifeStage,
                        pregnant = pregnant
                    };

                    // This also excludes malformed content (for example an egg layer with a
                    // pregnancy hediff) rather than showing a promise that cannot be fulfilled.
                    if (!spec.IsValidFor(race))
                    {
                        continue;
                    }

                    group = new AnimalStockGroup { race = race, spec = spec };
                    result.Add(group);
                }

                group.quantity++;
            }

            result.Sort((a, b) => b.quantity.CompareTo(a.quantity));
            return result;
        }

        /// <summary>
        /// Animal availability mirrors goods availability: existing open orders subtract an
        /// anonymous head count per race. Applying that count to each group is conservative,
        /// but never creates a pawn reservation or lets the same head be offered twice.
        /// </summary>
        public static List<AnimalStockGroup> AvailableColonyAnimals(
            IntercolonyWorldComponent state, Map map)
        {
            List<AnimalStockGroup> result = new List<AnimalStockGroup>();
            foreach (AnimalStockGroup group in ColonyAnimals(map))
            {
                int available = Mathf.Max(0, group.quantity - CommittedQuantity(state, group.race));
                if (available > 0)
                {
                    result.Add(new AnimalStockGroup
                    {
                        race = group.race,
                        spec = group.spec.Copy(),
                        quantity = available
                    });
                }
            }

            result.Sort((a, b) => b.quantity.CompareTo(a.quantity));
            return result;
        }

        /// <summary>
        /// Heads matching one anonymous animal specification which are still free for a new
        /// direct sale. This is the binding-boundary counterpart to the Animals UI read model.
        /// </summary>
        public static int AvailableAnimalQuantity(
            IntercolonyWorldComponent state,
            Map map,
            ThingDef race,
            AnimalSpec spec,
            int excludedOrderId = 0)
        {
            if (race == null || spec == null || map == null)
            {
                return 0;
            }

            int physical = 0;
            foreach (Pawn pawn in EligibleColonyAnimalCandidates(map))
            {
                if (AnimalTradeUtility.Matches(pawn, race, spec))
                {
                    physical++;
                }
            }

            return Mathf.Max(
                0, physical - CommittedQuantity(state, race, excludedOrderId));
        }

        private static AnimalStockGroup FindAnimalGroup(
            List<AnimalStockGroup> groups,
            ThingDef race,
            Gender gender,
            LifeStageDef lifeStage,
            bool pregnant)
        {
            foreach (AnimalStockGroup group in groups)
            {
                if (group.race == race && group.spec.gender == gender &&
                    group.spec.lifeStage == lifeStage && group.spec.pregnant == pregnant)
                {
                    return group;
                }
            }

            return null;
        }

        private static bool HasUnambiguousLifeStage(ThingDef race, LifeStageDef lifeStage)
        {
            int occurrences = 0;
            List<LifeStageAge> stages = race.race?.lifeStageAges;
            if (stages == null)
            {
                return false;
            }

            foreach (LifeStageAge stage in stages)
            {
                if (stage?.def == lifeStage)
                {
                    occurrences++;
                }
            }

            return occurrences == 1;
        }

        /// <summary>
        /// Physical units of one good still free to promise through Find Buyer. An excluded
        /// order is omitted from the commitment side so a binding path can later validate an
        /// existing order against the stock available to that order itself.
        /// </summary>
        public static int AvailableQuantity(
            IntercolonyWorldComponent state, Map map, ThingDef def, int excludedOrderId = 0)
        {
            if (def == null)
            {
                return 0;
            }

            int physical = 0;
            foreach (KeyValuePair<ThingDef, int> entry in ColonyStock(map))
            {
                if (entry.Key == def)
                {
                    physical = entry.Value;
                    break;
                }
            }

            return Mathf.Max(0, physical - CommittedQuantity(state, def, excludedOrderId));
        }

        /// <summary>
        /// Units already promised from today's stock: direct Find Buyer sales, plus any open
        /// order whose buyer is already travelling because the player marked its goods ready.
        /// </summary>
        public static int CommittedQuantity(
            IntercolonyWorldComponent state, ThingDef def, int excludedOrderId = 0)
        {
            if (state == null || def == null)
            {
                return 0;
            }

            int committed = 0;
            foreach (SalesOrder order in state.Orders)
            {
                if (order == null || !order.IsOpen || order.ThingDef != def ||
                    (excludedOrderId != 0 && order.id == excludedOrderId))
                {
                    continue;
                }

                if (!order.IsDirectFindBuyerSale &&
                    order.status != SalesOrderStatus.AwaitingCollection)
                {
                    continue;
                }

                // A direct seller-delivery order remains committed after its goods are loaded:
                // physical storage has fallen while RemainingQuantity has not. This conservative
                // understatement is deliberate until cargo can be allocated to a specific order.
                committed += order.RemainingQuantity;
            }

            return committed;
        }
    }
}
