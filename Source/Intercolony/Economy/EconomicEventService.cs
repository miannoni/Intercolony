using System.Collections.Generic;
using RimWorld.Planet;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Read-only queries over the active economic disturbances that reach a settlement.
    ///
    /// The hot multiplier queries allocate nothing because pricing reaches them once per visible
    /// row. Explanation queries return lists on purpose; they are the cold path where callers need
    /// the event identities as well as their product.
    /// </summary>
    public static class EconomicEventService
    {
        /// <summary>
        /// Multiplies every active, in-scope demand modifier for this category.
        /// </summary>
        public static float DemandMultiplier(
            IntercolonyWorldComponent state,
            Settlement settlement,
            IntercolonyProductCategory category)
        {
            if (state == null || settlement == null)
            {
                return EconomicEvent.Neutral;
            }

            float multiplier = EconomicEvent.Neutral;
            List<EconomicEvent> events = state.EconomicEvents;
            int tick = GenTicks.TicksGame;
            for (int i = 0; i < events.Count; i++)
            {
                EconomicEvent economicEvent = events[i];
                if (economicEvent == null || !economicEvent.IsActiveAt(tick) ||
                    !IsInScope(economicEvent, settlement))
                {
                    continue;
                }

                multiplier *= ModifierFor(economicEvent.demandModifier, category);
            }

            return multiplier;
        }

        /// <summary>
        /// Multiplies every active, in-scope scarcity modifier for this category. Above one means
        /// the settlement is scarcer, not more able to supply.
        /// </summary>
        public static float SupplyScarcityMultiplier(
            IntercolonyWorldComponent state,
            Settlement settlement,
            IntercolonyProductCategory category)
        {
            if (state == null || settlement == null)
            {
                return EconomicEvent.Neutral;
            }

            float multiplier = EconomicEvent.Neutral;
            List<EconomicEvent> events = state.EconomicEvents;
            int tick = GenTicks.TicksGame;
            for (int i = 0; i < events.Count; i++)
            {
                EconomicEvent economicEvent = events[i];
                if (economicEvent == null || !economicEvent.IsActiveAt(tick) ||
                    !IsInScope(economicEvent, settlement))
                {
                    continue;
                }

                multiplier *= ModifierFor(economicEvent.supplyScarcityModifier, category);
            }

            return multiplier;
        }

        /// <summary>
        /// The active, in-scope events that make a non-neutral demand contribution. This list is
        /// for explanations, never the per-row pricing path.
        /// </summary>
        public static List<EconomicEvent> DemandEvents(
            IntercolonyWorldComponent state,
            Settlement settlement,
            IntercolonyProductCategory category)
        {
            return ContributingEvents(state, settlement, category, supplyScarcity: false);
        }

        /// <summary>
        /// The active, in-scope events that make a non-neutral scarcity contribution. This list is
        /// for explanations, never the per-row pricing path.
        /// </summary>
        public static List<EconomicEvent> SupplyScarcityEvents(
            IntercolonyWorldComponent state,
            Settlement settlement,
            IntercolonyProductCategory category)
        {
            return ContributingEvents(state, settlement, category, supplyScarcity: true);
        }

        private static List<EconomicEvent> ContributingEvents(
            IntercolonyWorldComponent state,
            Settlement settlement,
            IntercolonyProductCategory category,
            bool supplyScarcity)
        {
            List<EconomicEvent> contributing = new List<EconomicEvent>();
            if (state == null || settlement == null)
            {
                return contributing;
            }

            List<EconomicEvent> events = state.EconomicEvents;
            int tick = GenTicks.TicksGame;
            for (int i = 0; i < events.Count; i++)
            {
                EconomicEvent economicEvent = events[i];
                if (economicEvent == null || !economicEvent.IsActiveAt(tick) ||
                    !IsInScope(economicEvent, settlement))
                {
                    continue;
                }

                float modifier = ModifierFor(
                    supplyScarcity
                        ? economicEvent.supplyScarcityModifier
                        : economicEvent.demandModifier,
                    category);
                if (modifier != EconomicEvent.Neutral)
                {
                    contributing.Add(economicEvent);
                }
            }

            return contributing;
        }

        private static bool IsInScope(EconomicEvent economicEvent, Settlement settlement)
        {
            // Every set constraint is conjunctive. A single-settlement event is an anchor plus
            // radius zero, while keeping faction and radius independent lets a later slice express
            // "this faction, within 30 tiles of here" without redesigning the saved model. Each
            // sentinel is compared exactly and never enters arithmetic; treating NoRadius as a
            // distance would make a non-radial event look one tile wide.
            if (economicEvent.factionLoadId != EconomicEvent.NoFaction &&
                (settlement.Faction == null ||
                 settlement.Faction.loadID != economicEvent.factionLoadId))
            {
                return false;
            }

            if (economicEvent.radiusTiles != EconomicEvent.NoRadius &&
                economicEvent.anchorSettlementId != EconomicEvent.NoSettlement)
            {
                Settlement anchor =
                    IntercolonyMarketAccess.FindSettlement(economicEvent.anchorSettlementId);
                if (anchor == null || Find.WorldGrid == null ||
                    Find.WorldGrid.ApproxDistanceInTiles(anchor.Tile, settlement.Tile) >
                    economicEvent.radiusTiles)
                {
                    return false;
                }
            }

            return true;
        }

        internal static float ModifierFor(
            float[] modifiers,
            IntercolonyProductCategory category)
        {
            int index = (int)category;
            return modifiers == null || index < 0 || index >= modifiers.Length
                ? EconomicEvent.Neutral
                : modifiers[index];
        }
    }
}
