using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Intercolony
{
    public enum EconomicEventType
    {
        Drought,
        WarMobilization,
        Epidemic,
        ConstructionBoom,
        Migration,
        AnimalDisease
    }

    /// <summary>
    /// One persisted temporary disturbance to the world economy (the 1.0 program Stage 3A).
    ///
    /// This is deliberately only the saved model. Deciding which settlements fall within an
    /// event's scope needs world lookups and belongs in the economy service; putting that here
    /// would turn a passive record into a second, competing source of economic behaviour.
    /// </summary>
    public class EconomicEvent : IExposable
    {
        public const float Neutral = 1f;

        /// <summary>
        /// No settlement scope. Compared exactly, never used in arithmetic and never printed: the
        /// project has repeatedly been bitten by a value chosen to mean "none" being read as a
        /// quantity, and a WorldObject ID of zero is a plausible real settlement.
        /// </summary>
        public const int NoSettlement = -1;

        /// <summary>
        /// No radial scope. Compared exactly, never used in arithmetic and never printed: treating
        /// this sentinel as a real distance would make a non-radial event look one tile wide.
        /// </summary>
        public const float NoRadius = -1f;

        /// <summary>
        /// No faction scope. Compared exactly, never used in arithmetic and never printed: treating
        /// this sentinel as a load ID would attach a world-wide event to a faction that does not exist.
        /// </summary>
        public const int NoFaction = -1;

        public int id;
        public EconomicEventType type;
        public int startTick;
        public int endTick;
        public int anchorSettlementId = NoSettlement;
        public float radiusTiles = NoRadius;
        public int factionLoadId = NoFaction;
        /// <summary>
        /// Per category, how much this event multiplies what an affected settlement wants.
        /// Above <see cref="Neutral"/> means it wants more than usual.
        /// </summary>
        public float[] demandModifier;

        /// <summary>
        /// Per category, how much this event multiplies an affected settlement's *scarcity*.
        /// **Above <see cref="Neutral"/> means scarcer** — a drought raises this, it does not lower it.
        ///
        /// The name carries the direction on purpose. Every event in §3.2 is described in fiction as
        /// supply going *down* ("drought: food supply down"), while this composes with
        /// <see cref="SettlementMarketState.supplyPressure"/>, which counts *up* toward scarce. A
        /// field named merely `supplyModifier` would read as "ability to supply" and invite a drought to be
        /// written as 0.7, which would produce a glut — the exact inversion
        /// <see cref="EffectiveEconomyService.EffectiveSupply"/> exists to hold in one place, since
        /// otherwise each caller reinvents it and half of them get it backwards.
        /// </summary>
        public float[] supplyScarcityModifier;

        public EconomicEvent()
        {
            demandModifier = NeutralArray();
            supplyScarcityModifier = NeutralArray();
        }

        /// <summary>
        /// True from the first tick through the tick before <see cref="endTick"/>. The interval is
        /// half-open so an event ending exactly when another begins does not overlap it for one tick.
        /// </summary>
        public bool IsActiveAt(int tick)
        {
            return tick >= startTick && tick < endTick;
        }

        private static float[] NeutralArray()
        {
            float[] values = new float[IntercolonyProductCategoryUtility.Count];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = Neutral;
            }

            return values;
        }

        /// <summary>
        /// Scribe has no array overload — <c>Scribe_Collections.Look</c> takes List, HashSet, Stack,
        /// Queue and Dictionary and nothing else — so the arrays cross the boundary as lists. The
        /// in-memory form stays an array because modifiers will be read by category index.
        /// </summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", 0);
            Scribe_Values.Look(ref type, "type", EconomicEventType.Drought);
            Scribe_Values.Look(ref startTick, "startTick", 0);
            Scribe_Values.Look(ref endTick, "endTick", 0);
            Scribe_Values.Look(ref anchorSettlementId, "anchorSettlementId", NoSettlement);
            Scribe_Values.Look(ref radiusTiles, "radiusTiles", NoRadius);
            Scribe_Values.Look(ref factionLoadId, "factionLoadId", NoFaction);

            List<float> demand = Scribe.mode == LoadSaveMode.Saving
                ? new List<float>(demandModifier)
                : null;
            List<float> supply = Scribe.mode == LoadSaveMode.Saving
                ? new List<float>(supplyScarcityModifier)
                : null;

            Scribe_Collections.Look(ref demand, "demandModifier", LookMode.Value);
            Scribe_Collections.Look(ref supply, "supplyScarcityModifier", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                demandModifier = FromSaved(demand);
                supplyScarcityModifier = FromSaved(supply);
            }
        }

        /// <summary>
        /// Rebuilds a modifier array from whatever the save actually held.
        ///
        /// A missing node loads as null, and the number of product categories could differ from the
        /// version that wrote the save. Both are answered the same way: anything not present is
        /// neutral. Padding with <see cref="Neutral"/> rather than zero matters — a zeroed modifier
        /// would silently mean "this event annihilates demand" instead of "this event does not touch it".
        /// </summary>
        internal static float[] FromSaved(List<float> saved)
        {
            float[] values = NeutralArray();
            if (saved == null)
            {
                return values;
            }

            int shared = Mathf.Min(values.Length, saved.Count);
            for (int i = 0; i < shared; i++)
            {
                values[i] = saved[i];
            }

            return values;
        }
    }
}
