using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// The one place anything asks "what does this settlement want right now, and what can it
    /// supply right now?" (docs/INTERCOLONY_1_0_IMPLEMENTATION_PLAN.md Stage 2.2).
    ///
    /// The plan's actual instruction is a prohibition rather than a feature: do not let
    /// <see cref="MarketOpportunityGenerator"/>, <see cref="FindBuyerService"/>,
    /// <see cref="RfqService"/>, contracts and <see cref="IntercolonyPricing"/> each invent their
    /// own interpretation of "current demand". Five interpretations is how a shortage raises a
    /// price without raising the offer that quoted it.
    ///
    /// Three layers compose here and nowhere else:
    ///
    /// <code>
    /// stable profile baseline  x  persistent market pressure  x  (Stage 3) event modifier
    /// </code>
    ///
    /// with the second and third bounded together as a single market *condition* — see
    /// <see cref="Bound"/>.
    ///
    /// **This is a read model. It never writes.** Nothing here creates, stamps or advances a
    /// pressure record. Reading has to be free of consequence, because the UI reads on hover:
    /// a read that created records would fill the sparse map with one neutral entry per settlement
    /// and undo the whole reason <see cref="SettlementMarketState"/> is sparse, and a read that
    /// advanced would make the economy depend on how often somebody looked at it. Movement belongs
    /// to <see cref="MarketPressureService"/>, called from the market refresh.
    /// </summary>
    public static class EffectiveEconomyService
    {
        /// <summary>
        /// The most a settlement's current condition may multiply its standing identity by.
        ///
        /// Bounding the *condition* rather than the composed result is deliberate. Clamping
        /// effective demand itself would flatten the archetype differences Stage 1 exists to
        /// establish — a military settlement's appetite for weapons is supposed to be visibly
        /// larger than a tribal one's — so what is bounded is only how far the dynamic layers may
        /// move that identity, in either direction.
        ///
        /// Above <see cref="MarketPressureService.MaxPressure"/> on purpose: pressure alone is
        /// already clamped tighter, and this headroom now binds only when an event modifier
        /// multiplies a settlement that is *already* under pressure. Bounding that composition is
        /// what prevents the 5x price swing the plan rules out without clipping ordinary pressure.
        /// </summary>
        public const float MaxCondition = 2.0f;

        /// <summary>
        /// The floor, as the exact multiplicative inverse of <see cref="MaxCondition"/>, for the
        /// same reason <see cref="MarketPressureService.MinPressure"/> is: a glut has to be exactly
        /// as strong as the equivalent shortage, and a literal here would invite an asymmetry
        /// nothing in the file explained.
        /// </summary>
        public const float MinCondition = 1f / MaxCondition;

        /// <summary>Label for a condition that makes a settlement keener or leaves it shorter.</summary>
        public const string ShortageLabel = "Current shortage";

        /// <summary>Label for the other side of the same axis.</summary>
        public const string SurplusLabel = "Current surplus";

        /// <summary>Label for the factor that reconciles explanation rows to the condition bound.</summary>
        public const string ConditionLimitLabel = "Market condition limit";

        /// <summary>
        /// This settlement's current demand pressure for a category, or
        /// <see cref="SettlementMarketState.Neutral"/> when it has no record.
        ///
        /// Absence means neutral. It is never an error and never worth creating a record over.
        /// </summary>
        public static float CurrentDemandPressure(
            IntercolonyWorldComponent state,
            int settlementId,
            IntercolonyProductCategory category)
        {
            SettlementMarketState record = state?.MarketStateFor(settlementId);
            return record == null
                ? SettlementMarketState.Neutral
                : record.DemandPressureFor(category);
        }

        /// <summary>
        /// This settlement's current supply pressure for a category, or neutral when undisturbed.
        ///
        /// **Above neutral means scarcer, not more plentiful** — that is the direction
        /// <see cref="MarketPressureService.ApplySupplyShock"/> defines, and it is the opposite of
        /// what <see cref="SettlementEconomicProfile.BaseSupplyFor"/> means by a high number. The
        /// two are reconciled once, in <see cref="EffectiveSupply"/>, and that reconciliation is
        /// most of the reason this service exists.
        /// </summary>
        public static float CurrentSupplyPressure(
            IntercolonyWorldComponent state,
            int settlementId,
            IntercolonyProductCategory category)
        {
            SettlementMarketState record = state?.MarketStateFor(settlementId);
            return record == null
                ? SettlementMarketState.Neutral
                : record.SupplyPressureFor(category);
        }

        /// <summary>
        /// What this settlement wants from a category right now: its standing appetite under
        /// current conditions.
        /// </summary>
        public static float EffectiveDemand(
            IntercolonyWorldComponent state,
            SettlementEconomicProfile profile,
            IntercolonyProductCategory category)
        {
            if (profile == null)
            {
                return 0f;
            }

            return profile.BaseDemandFor(category) * DemandCondition(state, profile, category);
        }

        /// <summary>
        /// What this settlement wants of one specific good right now.
        ///
        /// Pressure is per category, so the good's standing affinity rides through it unchanged:
        /// a shortage of manufactured goods makes a settlement keener on every manufactured good
        /// in the same proportion, and does not reorder which ones it prefers. That is the
        /// separation Stage 1 established — affinity is identity, pressure is circumstance.
        /// </summary>
        public static float EffectiveDemand(
            IntercolonyWorldComponent state,
            SettlementEconomicProfile profile,
            ThingDef def,
            IntercolonyProductCategory category)
        {
            if (profile == null)
            {
                return 0f;
            }

            return profile.BaseDemandFor(def, category) * DemandCondition(state, profile, category);
        }

        /// <summary>
        /// What this settlement can supply from a category right now.
        ///
        /// **Scarcity is inverted here, and this is the only place it may be.** Supply pressure
        /// counts upward toward *scarce*, while a supply weight counts upward toward *able to
        /// sell*, so a settlement under supply pressure supplies less. Multiplying the two
        /// together — the obvious mistake, and one a caller reimplementing this would make once
        /// each — would turn every shortage into a glut and quietly invert procurement.
        /// </summary>
        public static float EffectiveSupply(
            IntercolonyWorldComponent state,
            SettlementEconomicProfile profile,
            IntercolonyProductCategory category)
        {
            if (profile == null)
            {
                return 0f;
            }

            return profile.BaseSupplyFor(category) * SupplyCondition(state, profile, category);
        }

        /// <summary>
        /// The bounded multiplier current conditions apply to this settlement's standing demand.
        /// 1.0 means circumstances are not changing what it normally wants.
        /// </summary>
        public static float DemandCondition(
            IntercolonyWorldComponent state,
            SettlementEconomicProfile profile,
            IntercolonyProductCategory category)
        {
            if (profile == null)
            {
                return SettlementMarketState.Neutral;
            }

            float condition = CurrentDemandPressure(state, profile.settlementId, category);
            Settlement settlement = SettlementForEvents(profile);
            if (settlement != null)
            {
                condition *= EconomicEventService.DemandMultiplier(state, settlement, category);
            }

            // Pressure and active events multiply before one shared bound. Two layers each
            // individually "restrained" still multiply to an unrestrained number, while bounding
            // them separately would make the answer depend on their order.
            return Bound(condition);
        }

        /// <summary>
        /// The bounded multiplier current conditions apply to this settlement's standing ability
        /// to supply. Below 1.0 means it is currently shorter than usual.
        /// </summary>
        public static float SupplyCondition(
            IntercolonyWorldComponent state,
            SettlementEconomicProfile profile,
            IntercolonyProductCategory category)
        {
            return SupplyCondition(
                state, profile, category, SettlementForEvents(profile));
        }

        /// <summary>
        /// The same supply condition calculation when the caller already resolved the settlement.
        /// </summary>
        internal static float SupplyCondition(
            IntercolonyWorldComponent state,
            SettlementEconomicProfile profile,
            IntercolonyProductCategory category,
            Settlement settlement)
        {
            if (profile == null)
            {
                return SettlementMarketState.Neutral;
            }

            float scarcity = CurrentSupplyPressure(state, profile.settlementId, category);
            if (settlement != null)
            {
                // Event data is stored in the scarcity direction on purpose. It therefore
                // multiplies pressure directly here and is inverted exactly once below with the
                // composed scarcity, never on its own at this call site.
                scarcity *= EconomicEventService.SupplyScarcityMultiplier(
                    state, settlement, category);
            }

            // Bounded before inverting rather than after. Inverting first and bounding the result
            // gives the same interval only because the bounds are exact inverses of one another,
            // and relying on that would break silently the day either constant moved on its own.
            return 1f / Bound(scarcity);
        }

        /// <summary>
        /// Clamps a composed market condition into the range the dynamic layers are allowed to
        /// move a settlement's identity through.
        ///
        /// Public because Stage 3 must route its event modifier through the same bound rather than
        /// inventing a second one, and because a bound worth having is worth asserting directly.
        /// </summary>
        public static float Bound(float condition)
        {
            return Mathf.Clamp(condition, MinCondition, MaxCondition);
        }

        /// <summary>
        /// The named factors behind <see cref="EffectiveDemand(IntercolonyWorldComponent, SettlementEconomicProfile, ThingDef, IntercolonyProductCategory)"/>,
        /// for Stage 2.11's explanation surfaces.
        ///
        /// Reuses <see cref="PriceFactor"/> rather than introducing a second explanation type,
        /// which is what the plan asks for: these splice straight into a price breakdown.
        ///
        /// **The factors multiply to exactly the effective value, so a caller uses one or the
        /// other and never both.** Multiplying an effective demand that already contains pressure
        /// by a factor list that also contains it is the double-counting §2.10 forbids, and it
        /// would not look wrong at either site.
        ///
        /// A neutral condition contributes no line at all. A row reading "x1.00" tells the player
        /// nothing and buries the row that does.
        /// </summary>
        public static List<PriceFactor> ExplainDemand(
            IntercolonyWorldComponent state,
            SettlementEconomicProfile profile,
            ThingDef def,
            IntercolonyProductCategory category)
        {
            List<PriceFactor> factors = new List<PriceFactor>();
            if (profile == null)
            {
                return factors;
            }

            factors.Add(new PriceFactor("Local demand", profile.BaseDemandFor(def, category)));
            float pressure = CurrentDemandPressure(state, profile.settlementId, category);
            AddConditionFactor(factors, pressure);

            Settlement settlement = SettlementForEvents(profile);
            List<EconomicEvent> events = EconomicEventService.DemandEvents(
                state, settlement, category);
            float eventMultiplier = AddDemandEventFactors(factors, events, category);
            AddBoundFactor(factors, pressure * eventMultiplier,
                Bound(pressure * eventMultiplier), supplyDirection: false);
            return factors;
        }

        /// <summary>
        /// The supply-side counterpart. The condition line is labelled from the settlement's own
        /// stock — a settlement short of a good shows <see cref="ShortageLabel"/> whether the
        /// player is buying from it or selling to it — so the same circumstance reads the same way
        /// on both sides of the market.
        /// </summary>
        public static List<PriceFactor> ExplainSupply(
            IntercolonyWorldComponent state,
            SettlementEconomicProfile profile,
            IntercolonyProductCategory category)
        {
            return ExplainSupply(
                state, profile, category, SettlementForEvents(profile));
        }

        /// <summary>
        /// The same supply explanation when the caller already resolved the settlement.
        /// </summary>
        internal static List<PriceFactor> ExplainSupply(
            IntercolonyWorldComponent state,
            SettlementEconomicProfile profile,
            IntercolonyProductCategory category,
            Settlement settlement)
        {
            List<PriceFactor> factors = new List<PriceFactor>();
            if (profile == null)
            {
                return factors;
            }

            factors.Add(new PriceFactor("Local supply", profile.BaseSupplyFor(category)));

            // Reported as it applies to supply: a shortage is the multiplier below 1, because that
            // is the number that moved the answer. The label still names the shortage, not the
            // arithmetic.
            float pressure = CurrentSupplyPressure(state, profile.settlementId, category);
            float pressureCondition = 1f / pressure;
            if (!Mathf.Approximately(pressureCondition, SettlementMarketState.Neutral))
            {
                factors.Add(new PriceFactor(
                    pressureCondition < SettlementMarketState.Neutral
                        ? ShortageLabel
                        : SurplusLabel,
                    pressureCondition));
            }

            List<EconomicEvent> events = EconomicEventService.SupplyScarcityEvents(
                state, settlement, category);
            float eventScarcity = AddSupplyEventFactors(factors, events, category);
            float rawScarcity = pressure * eventScarcity;
            AddBoundFactor(factors, rawScarcity, Bound(rawScarcity), supplyDirection: true);

            return factors;
        }

        private static Settlement SettlementForEvents(SettlementEconomicProfile profile)
        {
            // -1 is the generic-estimate sentinel, not a world-object ID to look up. Keeping this
            // guard explicit prevents an estimate from inheriting whichever real settlement an
            // accessor might otherwise return for a malformed or future synthetic profile.
            if (profile == null || profile.settlementId == EconomicEvent.NoSettlement)
            {
                return null;
            }

            return IntercolonyMarketAccess.FindSettlement(profile.settlementId);
        }

        private static float AddDemandEventFactors(
            List<PriceFactor> factors,
            List<EconomicEvent> events,
            IntercolonyProductCategory category)
        {
            float total = EconomicEvent.Neutral;
            for (int i = 0; i < events.Count; i++)
            {
                EconomicEvent economicEvent = events[i];
                bool typeAlreadyAdded = false;
                for (int earlier = 0; earlier < i; earlier++)
                {
                    if (events[earlier].type == economicEvent.type)
                    {
                        typeAlreadyAdded = true;
                        break;
                    }
                }

                if (typeAlreadyAdded)
                {
                    continue;
                }

                float typeMultiplier = EconomicEvent.Neutral;
                for (int matching = i; matching < events.Count; matching++)
                {
                    if (events[matching].type == economicEvent.type)
                    {
                        typeMultiplier *= EconomicEventService.ModifierFor(
                            events[matching].demandModifier, category);
                    }
                }

                total *= typeMultiplier;
                if (typeMultiplier != EconomicEvent.Neutral)
                {
                    factors.Add(new PriceFactor(economicEvent.type.Label(), typeMultiplier));
                }
            }

            return total;
        }

        private static float AddSupplyEventFactors(
            List<PriceFactor> factors,
            List<EconomicEvent> events,
            IntercolonyProductCategory category)
        {
            float totalScarcity = EconomicEvent.Neutral;
            for (int i = 0; i < events.Count; i++)
            {
                EconomicEvent economicEvent = events[i];
                bool typeAlreadyAdded = false;
                for (int earlier = 0; earlier < i; earlier++)
                {
                    if (events[earlier].type == economicEvent.type)
                    {
                        typeAlreadyAdded = true;
                        break;
                    }
                }

                if (typeAlreadyAdded)
                {
                    continue;
                }

                float typeScarcity = EconomicEvent.Neutral;
                for (int matching = i; matching < events.Count; matching++)
                {
                    if (events[matching].type == economicEvent.type)
                    {
                        typeScarcity *= EconomicEventService.ModifierFor(
                            events[matching].supplyScarcityModifier, category);
                    }
                }

                totalScarcity *= typeScarcity;
                if (typeScarcity != EconomicEvent.Neutral)
                {
                    factors.Add(new PriceFactor(economicEvent.type.Label(), 1f / typeScarcity));
                }
            }

            return totalScarcity;
        }

        private static void AddBoundFactor(
            List<PriceFactor> factors,
            float raw,
            float bounded,
            bool supplyDirection)
        {
            if (raw == bounded)
            {
                return;
            }

            // Event and pressure rows retain their own meanings. This final row records only the
            // shared safety bound, so the displayed product still reconstructs the effective value
            // instead of silently assigning clipping to whichever event happened to be listed last.
            factors.Add(new PriceFactor(
                ConditionLimitLabel,
                supplyDirection ? raw / bounded : bounded / raw));
        }

        private static void AddConditionFactor(List<PriceFactor> factors, float condition)
        {
            if (Mathf.Approximately(condition, SettlementMarketState.Neutral))
            {
                return;
            }

            factors.Add(new PriceFactor(
                condition > SettlementMarketState.Neutral ? ShortageLabel : SurplusLabel,
                condition));
        }
    }
}
