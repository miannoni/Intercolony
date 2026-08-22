using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
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

            return StartEvent(
                state, decision.type, decision.anchor, currentTick, out _);
        }

        /// <summary>
        /// Starts a chosen event through the same production sequence as a generated event.
        /// Keeping build, registration, the start shock and the letter together avoids the debug
        /// trap where a hand-built record looks active but never exercises the real event path.
        /// </summary>
        internal static EconomicEvent StartEvent(
            IntercolonyWorldComponent state,
            EconomicEventType type,
            Settlement anchor,
            int startTick,
            out int shockedSettlements)
        {
            shockedSettlements = 0;
            if (state == null || anchor == null ||
                state.EconomicEvents.Count >= MaxConcurrentEvents)
            {
                return null;
            }

            EconomicEvent started = EconomicEventDefinitions.Build(
                state, type, anchor, startTick);
            state.EconomicEvents.Add(started);
            shockedSettlements = ApplyStartShock(state, started);
            SendStartLetter(state, started);
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

        /// <summary>
        /// Returns the active events that reach one settlement. The tab needs event identity even
        /// when an event has no visible pressure row yet, so this deliberately does not filter by
        /// modifier or category; repeating the radius/faction rules in the UI would let the two
        /// explanations disagree about what the economy is experiencing.
        /// </summary>
        internal static List<EconomicEvent> ActiveEventsAffecting(
            IntercolonyWorldComponent state, Settlement settlement)
        {
            List<EconomicEvent> active = new List<EconomicEvent>();
            if (state == null || settlement == null)
            {
                return active;
            }

            int currentTick = GenTicks.TicksGame;
            for (int i = 0; i < state.EconomicEvents.Count; i++)
            {
                EconomicEvent economicEvent = state.EconomicEvents[i];
                if (economicEvent != null && economicEvent.IsActiveAt(currentTick) &&
                    IsInScope(economicEvent, settlement))
                {
                    active.Add(economicEvent);
                }
            }

            return active;
        }

        /// <summary>
        /// Selects the volume-aware severity for an event-start report. A relationship with any
        /// affected settlement makes the event relevant; using Always here would turn a distant
        /// drought into an interruption and bypass the player's letter-volume choice.
        /// </summary>
        internal static IntercolonyLetterImportance ImportanceForStartLetter(
            IntercolonyWorldComponent state, EconomicEvent economicEvent)
        {
            List<Settlement> affected = SettlementsInScope(economicEvent);
            for (int i = 0; i < affected.Count; i++)
            {
                if (HasCommercialRelationship(state, affected[i].ID))
                {
                    return IntercolonyLetterImportance.Important;
                }
            }

            return IntercolonyLetterImportance.Chatty;
        }

        /// <summary>
        /// Converts a finite event window into player-facing days. Keeping the tick subtraction at
        /// this naming boundary avoids the recurring bug where a value chosen to mean an end point
        /// is printed as though it were already a duration, including the earlier open-contract
        /// DaysRemaining defects.
        /// </summary>
        internal static int DaysRemaining(EconomicEvent economicEvent, int currentTick)
        {
            if (economicEvent == null)
            {
                return 0;
            }

            int remainingTicks = economicEvent.endTick - currentTick;
            if (remainingTicks <= 0)
            {
                return 0;
            }

            return Mathf.CeilToInt(remainingTicks / (float)GenDate.TicksPerDay);
        }

        /// <summary>Builds the measured duration phrase used by the economy tab.</summary>
        internal static string RemainingDurationLabel(
            EconomicEvent economicEvent, int currentTick)
        {
            int days = DaysRemaining(economicEvent, currentTick);
            return $"{days} {(days == 1 ? "day" : "days")} left";
        }

        /// <summary>Builds the approximate duration phrase used by an event-start letter.</summary>
        private static string ApproximateDurationLabel(
            EconomicEvent economicEvent, int currentTick)
        {
            return $"roughly {DaysRemaining(economicEvent, currentTick)} days";
        }

        internal static List<Settlement> SettlementsInScope(EconomicEvent economicEvent)
        {
            List<Settlement> affected = new List<Settlement>();
            if (economicEvent == null)
            {
                return affected;
            }

            List<Settlement> settlements = EligibleSettlements(accessibleOnly: false);
            for (int i = 0; i < settlements.Count; i++)
            {
                if (IsInScope(economicEvent, settlements[i]))
                {
                    affected.Add(settlements[i]);
                }
            }

            return affected;
        }

        private static bool HasCommercialRelationship(
            IntercolonyWorldComponent state, int settlementId)
        {
            if (state == null)
            {
                return false;
            }

            if (state.Reputations != null &&
                state.Reputations.TryGetValue(settlementId, out CommercialReputation reputation) &&
                reputation != null)
            {
                return true;
            }

            if (state.CommercialHistory == null)
            {
                return false;
            }

            for (int i = 0; i < state.CommercialHistory.Count; i++)
            {
                CommercialHistoryEntry entry = state.CommercialHistory[i];
                if (entry != null && entry.settlementId == settlementId)
                {
                    return true;
                }
            }

            return false;
        }

        private static void SendStartLetter(
            IntercolonyWorldComponent state, EconomicEvent economicEvent)
        {
            List<Settlement> affected = SettlementsInScope(economicEvent);
            if (affected.Count == 0)
            {
                return;
            }

            Settlement place = affected[0];
            string placeName = place.Label ?? "the region";
            string label = $"{economicEvent.type.Label()} near {placeName}";
            string text = EventReport(economicEvent.type) + "\n" +
                          EventOutlook(economicEvent.type, economicEvent);
            IntercolonyLetters.Send(
                ImportanceForStartLetter(state, economicEvent),
                label,
                text,
                LetterDefOf.NeutralEvent);
        }

        private static string EventReport(EconomicEventType type)
        {
            switch (type)
            {
                case EconomicEventType.Drought:
                    return "Several settlements in the region are reporting weak harvests.";
                case EconomicEventType.WarMobilization:
                    return "Settlements in the region are redirecting resources toward war mobilization.";
                case EconomicEventType.ConstructionBoom:
                    return "The settlement is undertaking a construction boom.";
                case EconomicEventType.Epidemic:
                    return "An epidemic is disrupting trade in the settlement.";
                default:
                    return $"Reports indicate {type.Label().ToLowerInvariant()} near the settlement.";
            }
        }

        private static string EventOutlook(
            EconomicEventType type, EconomicEvent economicEvent)
        {
            string duration = ApproximateDurationLabel(economicEvent, economicEvent.startTick);
            switch (type)
            {
                case EconomicEventType.Drought:
                    return $"Food supply is expected to remain tight for {duration}.";
                case EconomicEventType.WarMobilization:
                    return $"Demand for supplies is expected to remain elevated for {duration}.";
                case EconomicEventType.ConstructionBoom:
                    return $"Demand for building goods is expected to remain elevated for {duration}.";
                case EconomicEventType.Epidemic:
                    return $"Demand for medical supplies is expected to remain elevated for {duration}.";
                default:
                    return $"The disruption is expected to last for {duration}.";
            }
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
