using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// In-game assertions over profile generation (DESIGN.md §83.2 "in-game dev tests").
    ///
    /// This exists because two of §96's acceptance criteria cannot be reached by playing a
    /// vanilla world. "Modded factions do not crash" needs factions with unset tech levels
    /// and missing names, which vanilla never produces, and §60's "no weird global RNG side
    /// effects" is invisible from the UI. Both are checked here against synthetic inputs.
    /// </summary>
    public static class IntercolonyProfileSelfTest
    {
        private static readonly TechLevel[] AllTechLevels =
            (TechLevel[])Enum.GetValues(typeof(TechLevel));

        public static string Run()
        {
            StringBuilder sb = new StringBuilder();
            int passed = 0;
            int failed = 0;

            void Check(string name, bool ok, string detail = null)
            {
                if (ok)
                {
                    passed++;
                }
                else
                {
                    failed++;
                    sb.AppendLine($"  FAIL  {name}{(detail == null ? "" : " — " + detail)}");
                }
            }

            sb.AppendLine("Profile generation self-test");

            // --- Every tech level, including Undefined and Animal, must produce a sane profile.
            foreach (TechLevel tech in AllTechLevels)
            {
                SettlementEconomicProfile p = null;
                string error = null;
                try
                {
                    p = SettlementProfileGenerator.GenerateFrom(12345, 7, 3, "Testville", "Testers", tech);
                }
                catch (Exception ex)
                {
                    error = ex.GetType().Name + ": " + ex.Message;
                }

                Check($"tech {tech} generates", error == null, error);
                if (p == null)
                {
                    continue;
                }

                Check($"tech {tech} normalized", p.techTier != TechLevel.Undefined,
                    "Undefined leaked through NormalizeTech");
                Check($"tech {tech} quality in range", p.qualityPreference >= 0f && p.qualityPreference <= 1f,
                    $"was {p.qualityPreference}");
                Check($"tech {tech} labor positive", p.laborSupplyModifier > 0f,
                    $"was {p.laborSupplyModifier}");
                Check($"tech {tech} volatility sane", p.volatility > 0f && p.volatility < 1f,
                    $"was {p.volatility}");

                foreach (IntercolonyProductCategory category in IntercolonyProductCategoryUtility.All)
                {
                    float demand = p.DemandFor(category);
                    float supply = p.SupplyFor(category);
                    Check($"tech {tech} {category.Label()} weights finite",
                        !float.IsNaN(demand) && !float.IsInfinity(demand) &&
                        !float.IsNaN(supply) && !float.IsInfinity(supply),
                        $"demand {demand}, supply {supply}");

                    // Nothing is ever truly impossible, only improbable (§9).
                    Check($"tech {tech} {category.Label()} above floor", demand > 0f && supply > 0f,
                        $"demand {demand}, supply {supply}");
                }
            }

            // --- Missing names and hostile IDs, as a modded or damaged world might supply.
            int[] awkwardIds = { 0, -1, int.MaxValue, int.MinValue };
            foreach (int id in awkwardIds)
            {
                string error = null;
                SettlementEconomicProfile p = null;
                try
                {
                    p = SettlementProfileGenerator.GenerateFrom(int.MinValue, id, -1, null, null, TechLevel.Undefined);
                }
                catch (Exception ex)
                {
                    error = ex.GetType().Name + ": " + ex.Message;
                }

                Check($"id {id} with null names generates", error == null, error);
                if (p != null)
                {
                    Check($"id {id} name defaulted", !string.IsNullOrEmpty(p.settlementName));
                    Check($"id {id} faction defaulted", !string.IsNullOrEmpty(p.factionName));
                }
            }

            // --- Determinism: identical inputs must give identical output.
            SettlementEconomicProfile a = SettlementProfileGenerator.GenerateFrom(999, 42, 1, "A", "F", TechLevel.Industrial);
            SettlementEconomicProfile b = SettlementProfileGenerator.GenerateFrom(999, 42, 1, "A", "F", TechLevel.Industrial);
            Check("same inputs give same archetype", a.archetype == b.archetype);
            Check("same inputs give same wealth", a.wealthTier == b.wealthTier);
            Check("same inputs give same seed", a.seed == b.seed);
            bool weightsMatch = true;
            foreach (IntercolonyProductCategory category in IntercolonyProductCategoryUtility.All)
            {
                if (Math.Abs(a.DemandFor(category) - b.DemandFor(category)) > 0.0001f ||
                    Math.Abs(a.SupplyFor(category) - b.SupplyFor(category)) > 0.0001f)
                {
                    weightsMatch = false;
                }
            }

            Check("same inputs give same weights", weightsMatch);

            // --- Different settlements must actually differ, or the seeding is broken.
            HashSet<string> shapes = new HashSet<string>();
            for (int id = 0; id < 40; id++)
            {
                SettlementEconomicProfile p =
                    SettlementProfileGenerator.GenerateFrom(4242, id, 1, "S" + id, "F", TechLevel.Industrial);
                shapes.Add($"{p.archetype}/{p.wealthTier}/{p.StrongestSupply}");
            }

            Check("40 settlements produce varied profiles", shapes.Count >= 8, $"only {shapes.Count} distinct shapes");

            // --- §60: generation must not disturb RimWorld's global random stream.
            Rand.PushState(20260725);
            int expected = Rand.Int;
            Rand.PopState();

            Rand.PushState(20260725);
            SettlementProfileGenerator.GenerateFrom(31337, 5, 2, "RngProbe", "F", TechLevel.Spacer);
            int actual = Rand.Int;
            Rand.PopState();

            Check("generation leaves global RNG untouched", expected == actual,
                $"next Rand.Int was {actual}, expected {expected}");

            sb.AppendLine($"  {passed} passed, {failed} failed, 0 skipped.");
            return sb.ToString();
        }
    }
}
