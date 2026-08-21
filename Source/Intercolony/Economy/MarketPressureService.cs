using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// The lifecycle of market pressure: shocks go in, and pressure decays back toward neutral over
    /// market cycles (docs/INTERCOLONY_1_0_IMPLEMENTATION_PLAN.md Stage 2.4).
    ///
    /// <see cref="SettlementMarketState"/> is the record; this owns how it *moves*. Keeping the two
    /// apart is what lets the record stay a dumb persisted value while the rules for changing it
    /// live in one place that every later stage — events in Stage 3, player trades in 2G, chain
    /// propagation in 2H — is required to go through rather than writing the arrays directly.
    ///
    /// The required properties, from the plan, in preference to any particular coefficient:
    /// shocks persist long enough to matter, they do not permanently distort the save, and the
    /// economy does not converge instantly.
    /// </summary>
    public static class MarketPressureService
    {
        /// <summary>
        /// Fraction of the distance from neutral that survives one market refresh.
        ///
        /// Chosen to match the plan's illustrative curve (1.40 → ~1.33 → ~1.27, a ratio of ~0.82
        /// each cycle) rather than derived from anything. Half-life is about 3.5 refreshes, and a
        /// shock of +0.40 falls inside <see cref="SettlementMarketState.NeutralEpsilon"/> — and so
        /// becomes prunable — after roughly 22. This is balance tuning and is expected to be
        /// retuned in play; the self-test asserts direction, monotonicity and boundedness, never
        /// this number.
        /// </summary>
        public const float ReversionRetention = 0.82f;

        /// <summary>
        /// Silver value over which a completed trade makes a substantial pressure nudge.
        /// This is conservative balance tuning: ordinary lots should barely move a regional
        /// market, while exceptional lots should move it clearly. Retune at the Stage 2K play
        /// gate; tests intentionally assert direction, composition and bounds, not this value.
        /// </summary>
        public const float NudgeValueScale = 20_000f;

        /// <summary>
        /// The most extreme shortage or keenness pressure may reach.
        ///
        /// Pressure is not a price multiplier — the effective-economy layer bounds it again before
        /// pricing sees it — but it is the input to that layer, so an unbounded value here becomes
        /// an unbounded price there. The plan asks for a restrained range rather than 5x swings.
        /// </summary>
        public const float MaxPressure = 1.60f;

        /// <summary>
        /// The floor, defined as the exact multiplicative inverse of <see cref="MaxPressure"/> so a
        /// glut is precisely as strong as the equivalent shortage. Writing it as a literal invited
        /// an asymmetry where shortages moved prices further than surpluses and nothing said why.
        /// </summary>
        public const float MinPressure = 1f / MaxPressure;

        /// <summary>
        /// Mean-reverts every disturbed settlement up to the world's current refresh, and reports
        /// how many records it moved.
        ///
        /// Called from the market refresh before the neutral prune, so a record that settles on
        /// this cycle is dropped on this cycle rather than lingering until the next one.
        /// </summary>
        public static int AdvanceAll(IntercolonyWorldComponent state)
        {
            if (state?.MarketStates == null)
            {
                return 0;
            }

            int advanced = 0;
            foreach (SettlementMarketState record in state.MarketStates)
            {
                if (Advance(record, state.RefreshCount))
                {
                    advanced++;
                }
            }

            return advanced;
        }

        /// <summary>
        /// Mean-reverts one record to <paramref name="toRefresh"/>. True when pressure actually
        /// moved, which is false for a record that was already stamped at this refresh or has
        /// never been advanced.
        ///
        /// Reversion is applied in closed form over the number of elapsed refreshes rather than by
        /// stepping. A save reopened many cycles later must land where it would have landed had the
        /// game been running, and iterating to get there would be a loop of unbounded length driven
        /// by how long a save sat on disk.
        /// </summary>
        public static bool Advance(SettlementMarketState record, int toRefresh)
        {
            if (record == null)
            {
                return false;
            }

            // Compared exactly, never arithmetic. A record that has never been advanced has no
            // baseline to measure elapsed cycles from, and treating the sentinel as a refresh
            // number would compute an elapsed span of toRefresh + 1 and erase a fresh shock on the
            // very next cycle. Stamp it and let it decay from here.
            if (record.lastAdvancedRefresh == SettlementMarketState.NeverAdvanced)
            {
                record.lastAdvancedRefresh = toRefresh;
                return false;
            }

            int elapsed = toRefresh - record.lastAdvancedRefresh;
            if (elapsed <= 0)
            {
                return false;
            }

            float retained = Mathf.Pow(ReversionRetention, elapsed);
            for (int i = 0; i < record.demandPressure.Length; i++)
            {
                record.demandPressure[i] = Revert(record.demandPressure[i], retained);
                record.supplyPressure[i] = Revert(record.supplyPressure[i], retained);
            }

            record.lastAdvancedRefresh = toRefresh;
            return true;
        }

        /// <summary>
        /// Moves one value a fraction of the way back to neutral. Multiplying the *offset* rather
        /// than the value is what makes this converge on <see cref="SettlementMarketState.Neutral"/>
        /// from both sides and never overshoot it: the offset shrinks toward zero and cannot change
        /// sign, because <see cref="ReversionRetention"/> is positive.
        /// </summary>
        private static float Revert(float pressure, float retained)
        {
            return SettlementMarketState.Neutral +
                   (pressure - SettlementMarketState.Neutral) * retained;
        }

        /// <summary>
        /// Adds <paramref name="delta"/> to a settlement's demand pressure for one category,
        /// creating the record if this is its first disturbance, and returns the resulting value.
        /// </summary>
        public static float ApplyDemandShock(
            IntercolonyWorldComponent state,
            int settlementId,
            IntercolonyProductCategory category,
            float delta)
        {
            SettlementMarketState record = PrepareForShock(state, settlementId);
            if (record == null)
            {
                return SettlementMarketState.Neutral;
            }

            int index = (int)category;
            record.demandPressure[index] = Clamp(record.demandPressure[index] + delta);
            return record.demandPressure[index];
        }

        /// <summary>
        /// The supply-side counterpart of <see cref="ApplyDemandShock"/>. Above neutral means
        /// scarcer than usual.
        /// </summary>
        public static float ApplySupplyShock(
            IntercolonyWorldComponent state,
            int settlementId,
            IntercolonyProductCategory category,
            float delta)
        {
            SettlementMarketState record = PrepareForShock(state, settlementId);
            if (record == null)
            {
                return SettlementMarketState.Neutral;
            }

            int index = (int)category;
            record.supplyPressure[index] = Clamp(record.supplyPressure[index] + delta);
            return record.supplyPressure[index];
        }

        /// <summary>
        /// Relieves demand after value is sold into a settlement. The exponential multiplies the
        /// pressure offset exactly as <see cref="Revert"/> does, but aims it at a bound instead of
        /// neutral. Consequently split trades compose by value: exp(-a/K) * exp(-b/K) equals
        /// exp(-(a+b)/K). Do not replace this with a concave per-trade delta; with f(0) = 0 that
        /// shape is subadditive, so f(a) + f(b) &gt; f(a+b) and splitting trades becomes an exploit.
        /// </summary>
        public static float NudgeDemandDown(
            IntercolonyWorldComponent state,
            int settlementId,
            IntercolonyProductCategory category,
            float value)
        {
            // The sparse-representation invariant requires invalid nudges to leave undisturbed
            // settlements absent, because absence itself represents neutral pressure.
            if (value <= 0f || float.IsNaN(value))
            {
                return state?.MarketStateFor(settlementId)?.DemandPressureFor(category) ??
                    SettlementMarketState.Neutral;
            }

            SettlementMarketState record = PrepareForShock(state, settlementId);
            if (record == null)
            {
                return record?.DemandPressureFor(category) ?? SettlementMarketState.Neutral;
            }

            int index = (int)category;
            record.demandPressure[index] = Nudge(record.demandPressure[index], MinPressure, value);
            return record.demandPressure[index];
        }

        /// <summary>Tightens supply after value is purchased from a settlement.</summary>
        public static float NudgeSupplyUp(
            IntercolonyWorldComponent state,
            int settlementId,
            IntercolonyProductCategory category,
            float value)
        {
            // The sparse-representation invariant requires invalid nudges to leave undisturbed
            // settlements absent, because absence itself represents neutral pressure.
            if (value <= 0f || float.IsNaN(value))
            {
                return state?.MarketStateFor(settlementId)?.SupplyPressureFor(category) ??
                    SettlementMarketState.Neutral;
            }

            SettlementMarketState record = PrepareForShock(state, settlementId);
            if (record == null)
            {
                return record?.SupplyPressureFor(category) ?? SettlementMarketState.Neutral;
            }

            int index = (int)category;
            record.supplyPressure[index] = Nudge(record.supplyPressure[index], MaxPressure, value);
            return record.supplyPressure[index];
        }

        private static float Nudge(float current, float bound, float value)
        {
            float newPressure = bound +
                (current - bound) * Mathf.Exp(-value / NudgeValueScale);
            return Clamp(newPressure);
        }

        /// <summary>
        /// Fetches or creates the record and gives it a decay baseline.
        ///
        /// Stamping here rather than leaving it to the next <see cref="AdvanceAll"/> matters: the
        /// shock knows which refresh it happened on, and a record stamped later would get a free
        /// extra cycle at full strength for no reason the player could observe.
        /// </summary>
        private static SettlementMarketState PrepareForShock(
            IntercolonyWorldComponent state,
            int settlementId)
        {
            SettlementMarketState record = state?.MarketStateFor(settlementId, createIfMissing: true);
            if (record == null)
            {
                return null;
            }

            if (record.lastAdvancedRefresh == SettlementMarketState.NeverAdvanced)
            {
                record.lastAdvancedRefresh = state.RefreshCount;
            }

            return record;
        }

        public static float Clamp(float pressure)
        {
            return Mathf.Clamp(pressure, MinPressure, MaxPressure);
        }
    }
}
