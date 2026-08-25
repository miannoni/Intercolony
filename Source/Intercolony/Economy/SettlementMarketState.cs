using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// What one settlement's economy is *currently experiencing*, as opposed to what it normally
    /// is (docs/INTERCOLONY_1_0_IMPLEMENTATION_PLAN.md Stage 2.1).
    ///
    /// The 1.0 economy is three layers: <see cref="SettlementEconomicProfile"/> is stable identity,
    /// this is persistent current pressure, and economic events are temporary shocks on top. Keeping
    /// them apart is the whole design — Stage 1 pulled cycle noise out of the profile precisely so
    /// that movement could live here instead, where it has a cause and a lifetime.
    ///
    /// Pressure is centred on <see cref="Neutral"/>. Above 1 means keener than usual or scarcer
    /// than usual; below means the opposite. It is *not* a price multiplier — pricing reads it
    /// through the effective-economy layer, which bounds it.
    ///
    /// **A neutral settlement stores nothing.** Records are created on first disturbance and
    /// dropped again once they revert, so the save carries only the part of the world that is
    /// actually unsettled. On a 358-settlement world, eagerly persisting a record each would put
    /// thousands of floats in every save to say "nothing is happening" — see
    /// <see cref="IsNeutral"/> and the prune on the market refresh.
    /// </summary>
    public class SettlementMarketState : IExposable
    {
        /// <summary>Undisturbed. A settlement with no record is at this value everywhere.</summary>
        public const float Neutral = 1f;

        /// <summary>
        /// How far from <see cref="Neutral"/> still counts as settled. Mean reversion approaches
        /// 1.0 asymptotically and would otherwise never quite arrive, so a record would never
        /// become prunable and the save would only ever grow.
        /// </summary>
        public const float NeutralEpsilon = 0.005f;

        /// <summary>
        /// <see cref="lastAdvancedRefresh"/> before this record has ever been advanced. Compared
        /// exactly, never printed — the project has been bitten repeatedly by a value chosen to
        /// mean "none" being read as a quantity.
        /// </summary>
        public const int NeverAdvanced = -1;

        public int settlementId = -1;

        /// <summary>Per <see cref="IntercolonyProductCategory"/>, centred on <see cref="Neutral"/>.</summary>
        public float[] demandPressure;

        public float[] supplyPressure;

        /// <summary>
        /// Refresh number this record last mean-reverted on, or <see cref="NeverAdvanced"/>.
        /// Stored so reversion is driven by elapsed market cycles rather than by how often
        /// something happened to read the record.
        /// </summary>
        public int lastAdvancedRefresh = NeverAdvanced;

        public SettlementMarketState()
        {
            ResetToNeutral();
        }

        public SettlementMarketState(int settlementId)
            : this()
        {
            this.settlementId = settlementId;
        }

        public float DemandPressureFor(IntercolonyProductCategory category)
        {
            return demandPressure[(int)category];
        }

        public float SupplyPressureFor(IntercolonyProductCategory category)
        {
            return supplyPressure[(int)category];
        }

        /// <summary>
        /// True when nothing is happening here worth remembering. The prune uses this, so an
        /// epsilon that is too tight keeps dead records alive forever and one that is too loose
        /// throws away a real shortage the player is still trading against.
        /// </summary>
        public bool IsNeutral
        {
            get
            {
                for (int i = 0; i < demandPressure.Length; i++)
                {
                    if (Mathf.Abs(demandPressure[i] - Neutral) > NeutralEpsilon ||
                        Mathf.Abs(supplyPressure[i] - Neutral) > NeutralEpsilon)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public void ResetToNeutral()
        {
            demandPressure = NeutralArray();
            supplyPressure = NeutralArray();
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
        /// in-memory form stays an array because pressure is read by category index on hot paths.
        /// </summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref settlementId, "settlementId", -1);
            Scribe_Values.Look(ref lastAdvancedRefresh, "lastAdvancedRefresh", NeverAdvanced);

            List<float> demand = Scribe.mode == LoadSaveMode.Saving
                ? new List<float>(demandPressure)
                : null;
            List<float> supply = Scribe.mode == LoadSaveMode.Saving
                ? new List<float>(supplyPressure)
                : null;

            Scribe_Collections.Look(ref demand, "demandPressure", LookMode.Value);
            Scribe_Collections.Look(ref supply, "supplyPressure", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                demandPressure = FromSaved(demand);
                supplyPressure = FromSaved(supply);
            }
        }

        /// <summary>
        /// Rebuilds a pressure array from whatever the save actually held.
        ///
        /// A missing node loads as null, and the number of product categories could differ from the
        /// version that wrote the save. Both are answered the same way: anything not present is
        /// neutral. Padding with <see cref="Neutral"/> rather than zero matters — a zeroed array
        /// would silently mean "no demand anywhere" instead of "undisturbed".
        /// </summary>
        private static float[] FromSaved(List<float> saved)
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

        public override string ToString()
        {
            return $"settlement {settlementId} demand[{Join(demandPressure)}] supply[{Join(supplyPressure)}]";
        }

        private static string Join(float[] values)
        {
            string[] parts = new string[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                parts[i] = values[i].ToString("F2");
            }

            return string.Join(" ", parts);
        }
    }
}
