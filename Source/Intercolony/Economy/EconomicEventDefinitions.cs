using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// The deliberately small Stage 3C definition table and its construction boundary.
    ///
    /// Migration and animal disease remain enum values but are deliberately absent here. Migration
    /// still needs a clean basic-goods shape, while animal availability is not category-shaped and
    /// the plan only permits herd loss where animal trade already supports it. Pretending either is
    /// defined would create a no-op event that looks implemented.
    /// </summary>
    public static class EconomicEventDefinitions
    {
        /// <summary>
        /// Drought demand is 1.25: food urgency should be visible without consuming the headroom up
        /// to EffectiveEconomyService's 2.0 condition cap by itself.
        /// </summary>
        public const float DroughtCommoditiesDemand = 1.25f;

        /// <summary>
        /// Drought scarcity is 1.30: the supply shock is its defining effect, but pressure can already
        /// reach 1.60 and must retain room to compose rather than pinning the effective cap alone.
        /// </summary>
        public const float DroughtCommoditiesScarcity = 1.30f;

        /// <summary>
        /// A drought lasts at least 18 days so it crosses several ordinary market refreshes instead of
        /// disappearing between them.
        /// </summary>
        public const int DroughtMinimumDays = 18;

        /// <summary>
        /// A drought lasts at most 30 days because a regional shortage should matter for a season, not
        /// become the settlement's permanent baseline.
        /// </summary>
        public const int DroughtMaximumDays = 30;

        /// <summary>
        /// Twelve tiles makes drought regional without turning the scope into the accidental global
        /// event that §3.3 explicitly rejects.
        /// </summary>
        public const float DroughtRadiusTiles = 12f;

        /// <summary>
        /// Zero tiles means the anchor settlement alone. Naming it avoids the scope trap where a
        /// literal zero looks like an unset radius even though <see cref="EconomicEvent.NoRadius"/>
        /// is the distinct sentinel for no radial scope.
        /// </summary>
        public const float SingleSettlementRadiusTiles = 0f;

        /// <summary>
        /// War mobilization demand is 1.25: a faction procurement push should be material, while the
        /// demand chain still needs headroom to pull intermediate inputs secondarily.
        /// </summary>
        public const float WarManufacturedDemand = 1.25f;

        /// <summary>
        /// Mobilization lasts at least 15 days so faction scope is not paid for by a fleeting effect.
        /// </summary>
        public const int WarMinimumDays = 15;

        /// <summary>
        /// Mobilization lasts at most 25 days to keep this initial demand shock disruptive rather than
        /// a long-lived replacement for the faction's baseline economy.
        /// </summary>
        public const int WarMaximumDays = 25;

        /// <summary>
        /// Furniture demand is 1.20: rebuilding should be noticeable, while its two demand-chain links
        /// already spread the shock into commodities and intermediates.
        /// </summary>
        public const float ConstructionFurnitureDemand = 1.20f;

        /// <summary>
        /// Capital-equipment demand is 1.15: tools rise modestly during rebuilding and should not rival
        /// the boom's primary furniture demand.
        /// </summary>
        public const float ConstructionCapitalEquipmentDemand = 1.15f;

        /// <summary>
        /// A construction boom lasts at least 12 days because rebuilding is sustained work, not a
        /// single purchase pulse.
        /// </summary>
        public const int ConstructionMinimumDays = 12;

        /// <summary>
        /// A construction boom lasts at most 24 days so a local settlement eventually returns to its
        /// profile rather than carrying a near-permanent special demand shape.
        /// </summary>
        public const int ConstructionMaximumDays = 24;

        /// <summary>
        /// Epidemic manufactured demand is 1.30: medicine urgency is the sharpest initial demand shock,
        /// but remains conservative beside the effective-economy cap.
        /// </summary>
        public const float EpidemicManufacturedDemand = 1.30f;

        /// <summary>
        /// Epidemic commodities demand is 1.20: basic food rises too, but less than medicine so the
        /// coarse category model does not overstate the secondary need.
        /// </summary>
        public const float EpidemicCommoditiesDemand = 1.20f;

        /// <summary>
        /// An epidemic lasts at least 8 days so even the short edge remains economically observable.
        /// </summary>
        public const int EpidemicMinimumDays = 8;

        /// <summary>
        /// An epidemic lasts at most 14 days because this definition is intended to be short and sharp,
        /// leaving longer aftereffects to later lifecycle work rather than baking them into duration.
        /// </summary>
        public const int EpidemicMaximumDays = 14;

        /// <summary>
        /// This salt keeps event duration rolls in their own deterministic economy stream; sharing a
        /// stream with contracts would let an unrelated retune change event lifetimes.
        /// </summary>
        private const int DurationSeedSalt = 0x3C0E;

        internal enum Scope
        {
            Regional,
            Faction,
            Settlement
        }

        internal sealed class Definition
        {
            public readonly int minimumDays;
            public readonly int maximumDays;
            public readonly Scope scope;
            public readonly float radiusTiles;
            public readonly float[] demandModifier;
            public readonly float[] supplyScarcityModifier;

            public Definition(
                int minimumDays,
                int maximumDays,
                Scope scope,
                float radiusTiles,
                float[] demandModifier,
                float[] supplyScarcityModifier)
            {
                this.minimumDays = minimumDays;
                this.maximumDays = maximumDays;
                this.scope = scope;
                this.radiusTiles = radiusTiles;
                this.demandModifier = demandModifier;
                this.supplyScarcityModifier = supplyScarcityModifier;
            }
        }

        internal static readonly EconomicEventType[] DefinedTypes =
        {
            EconomicEventType.Drought,
            EconomicEventType.WarMobilization,
            EconomicEventType.ConstructionBoom,
            EconomicEventType.Epidemic
        };

        private static readonly Dictionary<EconomicEventType, Definition> Definitions =
            new Dictionary<EconomicEventType, Definition>
            {
                [EconomicEventType.Drought] = new Definition(
                    DroughtMinimumDays,
                    DroughtMaximumDays,
                    Scope.Regional,
                    DroughtRadiusTiles,
                    Modifiers((IntercolonyProductCategory.Commodities, DroughtCommoditiesDemand)),
                    Modifiers((IntercolonyProductCategory.Commodities, DroughtCommoditiesScarcity))),
                [EconomicEventType.WarMobilization] = new Definition(
                    WarMinimumDays,
                    WarMaximumDays,
                    Scope.Faction,
                    EconomicEvent.NoRadius,
                    Modifiers((IntercolonyProductCategory.ManufacturedGoods, WarManufacturedDemand)),
                    Modifiers()),
                [EconomicEventType.ConstructionBoom] = new Definition(
                    ConstructionMinimumDays,
                    ConstructionMaximumDays,
                    Scope.Settlement,
                    SingleSettlementRadiusTiles,
                    Modifiers(
                        (IntercolonyProductCategory.Furniture, ConstructionFurnitureDemand),
                        (IntercolonyProductCategory.CapitalEquipment,
                            ConstructionCapitalEquipmentDemand)),
                    Modifiers()),
                [EconomicEventType.Epidemic] = new Definition(
                    EpidemicMinimumDays,
                    EpidemicMaximumDays,
                    Scope.Settlement,
                    SingleSettlementRadiusTiles,
                    Modifiers(
                        (IntercolonyProductCategory.ManufacturedGoods, EpidemicManufacturedDemand),
                        (IntercolonyProductCategory.Commodities, EpidemicCommoditiesDemand)),
                    Modifiers())
            };

        /// <summary>
        /// Builds but does not activate an event. The caller owns insertion and later lifecycle work;
        /// silently adding here would make a factory call mutate world event state twice when the next
        /// slice's generator performs its explicit add.
        /// </summary>
        public static EconomicEvent Build(
            IntercolonyWorldComponent state,
            EconomicEventType type,
            Settlement anchor,
            int startTick)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (anchor == null)
            {
                throw new ArgumentNullException(nameof(anchor));
            }

            if (!Definitions.TryGetValue(type, out Definition definition))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(type), type, "This event type is deliberately not defined in Stage 3C.");
            }

            if (definition.scope == Scope.Faction && anchor.Faction == null)
            {
                throw new ArgumentException(
                    "A faction-wide event cannot be built from a factionless anchor.", nameof(anchor));
            }

            int seed = Gen.HashCombineInt(state.EconomySeed, anchor.ID, startTick, (int)type);
            seed = Gen.HashCombineInt(seed, DurationSeedSalt);
            int durationDays;
            Rand.PushState(seed);
            try
            {
                durationDays = Rand.RangeInclusive(definition.minimumDays, definition.maximumDays);
            }
            finally
            {
                Rand.PopState();
            }

            EconomicEvent economicEvent = new EconomicEvent
            {
                id = state.NextId(),
                type = type,
                startTick = startTick,
                endTick = startTick + durationDays * GenDate.TicksPerDay,
                demandModifier = (float[])definition.demandModifier.Clone(),
                supplyScarcityModifier = (float[])definition.supplyScarcityModifier.Clone()
            };

            switch (definition.scope)
            {
                case Scope.Regional:
                    economicEvent.anchorSettlementId = anchor.ID;
                    economicEvent.radiusTiles = definition.radiusTiles;
                    break;
                case Scope.Faction:
                    economicEvent.factionLoadId = anchor.Faction.loadID;
                    break;
                case Scope.Settlement:
                    economicEvent.anchorSettlementId = anchor.ID;
                    economicEvent.radiusTiles = SingleSettlementRadiusTiles;
                    break;
            }

            return economicEvent;
        }

        internal static Definition Get(EconomicEventType type)
        {
            return Definitions[type];
        }

        private static float[] Modifiers(
            params (IntercolonyProductCategory category, float value)[] configured)
        {
            float[] values = new float[IntercolonyProductCategoryUtility.Count];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = EconomicEvent.Neutral;
            }

            for (int i = 0; i < configured.Length; i++)
            {
                values[(int)configured[i].category] = configured[i].value;
            }

            return values;
        }
    }
}
