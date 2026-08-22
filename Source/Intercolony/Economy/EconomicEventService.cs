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
        /// Forty percent leaves a visible pressure tail without duplicating the full live modifier.
        /// Using the whole modifier here is the trap: the event would apply its headline effect
        /// twice while live, once as pressure and once as the active multiplier.
        /// </summary>
        public const float StartShockFraction = 0.40f;

        /// <summary>
        /// Three permits occasional overlap while preventing event generation from turning the
        /// whole economy into a permanent stack of crises.
        /// </summary>
        public const int MaxConcurrentEvents = 3;

        /// <summary>
        /// A four-percent daily-default refresh roll starts roughly one event every twenty-five
        /// days. Naming the cadence avoids the tuning trap where a plausible-looking probability
        /// silently becomes spam after the refresh interval is considered.
        /// </summary>
        public const float EventChancePerRefresh = 0.04f;

        /// <summary>
        /// Twenty-four settlements are enough for a faction event to feel broad without letting a
        /// 358-settlement world turn one refresh into unbounded category work.
        /// </summary>
        public const int MaxShockedSettlementsPerEvent = 24;

        /// <summary>
        /// This salt isolates lifecycle generation from other economy rolls. Reusing another
        /// stream is the trap because an unrelated contract or opportunity retune could then
        /// change which event a saved world receives on the same refresh.
        /// </summary>
        private const int GenerationSeedSalt = 0x3D0E;

        internal readonly struct GenerationDecision
        {
            public readonly float roll;
            public readonly EconomicEventType type;
            public readonly Settlement anchor;

            public GenerationDecision(float roll, EconomicEventType type, Settlement anchor)
            {
                this.roll = roll;
                this.type = type;
                this.anchor = anchor;
            }

            public bool Starts => roll < EventChancePerRefresh;
        }

        /// <summary>
        /// Ends expired events and then attempts one deterministic start. This must run before
        /// pressure advancement: chains only see persisted pressure, so moving the start later
        /// would make the event's shock miss propagation on the refresh in which it was born.
        /// </summary>
        internal static EconomicEvent AdvanceLifecycle(
            IntercolonyWorldComponent state, int currentTick, bool allowGeneration = true)
        {
            if (state == null)
            {
                return null;
            }

            for (int i = state.EconomicEvents.Count - 1; i >= 0; i--)
            {
                EconomicEvent economicEvent = state.EconomicEvents[i];
                if (economicEvent == null || economicEvent.endTick <= currentTick)
                {
                    state.EconomicEvents.RemoveAt(i);
                }
            }

            return allowGeneration ? TryGenerate(state, currentTick) : null;
        }

        internal static EconomicEvent TryGenerate(
            IntercolonyWorldComponent state, int currentTick, bool forceStart = false)
        {
            if (state == null || state.EconomicEvents.Count >= MaxConcurrentEvents)
            {
                return null;
            }

            List<Settlement> candidates = EligibleSettlements(accessibleOnly: true);
            if (candidates.Count == 0)
            {
                return null;
            }

            GenerationDecision decision = DecideGeneration(state, candidates);
            if (!forceStart && !decision.Starts)
            {
                return null;
            }

            EconomicEvent started = EconomicEventDefinitions.Build(
                state, decision.type, decision.anchor, currentTick);
            state.EconomicEvents.Add(started);
            ApplyStartShock(state, started);
            return started;
        }

        internal static GenerationDecision DecideGeneration(
            IntercolonyWorldComponent state, List<Settlement> candidates = null)
        {
            candidates = candidates ?? EligibleSettlements(accessibleOnly: true);
            if (state == null || candidates.Count == 0)
            {
                return new GenerationDecision(1f, default, null);
            }

            float roll;
            EconomicEventType type;
            Settlement anchor;
            Rand.PushState(Gen.HashCombineInt(
                state.EconomySeed, state.RefreshCount, GenerationSeedSalt, 0));
            try
            {
                roll = Rand.Value;
                type = EconomicEventDefinitions.DefinedTypes[
                    Rand.Range(0, EconomicEventDefinitions.DefinedTypes.Length)];
                anchor = candidates[Rand.Range(0, candidates.Count)];
            }
            finally
            {
                Rand.PopState();
            }

            return new GenerationDecision(roll, type, anchor);
        }

        /// <summary>
        /// Applies the persisted tail to a stable prefix of settlement IDs. World-object iteration
        /// order is not stable across reloads, so using it directly is the determinism trap; sorting
        /// before the cap makes the same event disturb the same settlements every time.
        /// </summary>
        internal static int ApplyStartShock(
            IntercolonyWorldComponent state, EconomicEvent economicEvent)
        {
            if (state == null || economicEvent == null)
            {
                return 0;
            }

            List<Settlement> settlements = EligibleSettlements(accessibleOnly: false);
            int shocked = 0;
            for (int i = 0; i < settlements.Count && shocked < MaxShockedSettlementsPerEvent; i++)
            {
                Settlement settlement = settlements[i];
                if (!IsInScope(economicEvent, settlement))
                {
                    continue;
                }

                for (int categoryIndex = 0;
                    categoryIndex < IntercolonyProductCategoryUtility.Count;
                    categoryIndex++)
                {
                    IntercolonyProductCategory category =
                        (IntercolonyProductCategory)categoryIndex;
                    float demand = ModifierFor(economicEvent.demandModifier, category);
                    float scarcity = ModifierFor(economicEvent.supplyScarcityModifier, category);
                    if (demand != EconomicEvent.Neutral)
                    {
                        MarketPressureService.ApplyDemandShock(
                            state, settlement.ID, category,
                            (demand - EconomicEvent.Neutral) * StartShockFraction);
                    }

                    if (scarcity != EconomicEvent.Neutral)
                    {
                        MarketPressureService.ApplySupplyShock(
                            state, settlement.ID, category,
                            (scarcity - EconomicEvent.Neutral) * StartShockFraction);
                    }
                }

                shocked++;
            }

            return shocked;
        }

        private static List<Settlement> EligibleSettlements(bool accessibleOnly)
        {
            List<Settlement> result = new List<Settlement>();
            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements == null)
            {
                return result;
            }

            for (int i = 0; i < settlements.Count; i++)
            {
                Settlement settlement = settlements[i];
                if (SettlementProfileGenerator.IsEligible(settlement) &&
                    (!accessibleOnly || IntercolonyMarketAccess.IsAccessible(settlement)))
                {
                    result.Add(settlement);
                }
            }

            result.Sort((left, right) => left.ID.CompareTo(right.ID));
            return result;
        }

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

        internal static bool IsInScope(EconomicEvent economicEvent, Settlement settlement)
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
