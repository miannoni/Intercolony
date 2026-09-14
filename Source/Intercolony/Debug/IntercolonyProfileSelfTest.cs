using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using RimWorld;
using RimWorld.Planet;
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
            Info(sb, CurrentWorldSettlementCapabilityMeasurement());
            CheckRapidLogisticsAssertions(sb, ref passed, ref failed);

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
                    float demand = p.BaseDemandFor(category);
                    float supply = p.BaseSupplyFor(category);
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
                if (Math.Abs(a.BaseDemandFor(category) - b.BaseDemandFor(category)) > 0.0001f ||
                    Math.Abs(a.BaseSupplyFor(category) - b.BaseSupplyFor(category)) > 0.0001f)
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

        private static void Info(StringBuilder sb, string line)
        {
            sb.AppendLine($"        {line}");
        }

        private static void CheckRapidLogisticsAssertions(
            StringBuilder sb, ref int passed, ref int failed)
        {
            int observedPassed = 0;
            int observedFailed = 0;

            void CheckObserved(string label, bool ok, string observed)
            {
                if (ok)
                {
                    observedPassed++;
                }
                else
                {
                    observedFailed++;
                }

                sb.AppendLine($"  {(ok ? "PASS" : "FAIL")} {label}  ({observed})");
            }

            // (a) The raw pre-industrial tiers are exercised over enough identities to cover a
            // range of the generated wealth and archetype outcomes. Undefined is deliberately
            // excluded: NormalizeTech treats it as Industrial, so it is not pre-industrial.
            int preIndustrialCombinations = 0;
            int preIndustrialCapable = 0;
            HashSet<IntercolonyWealthTier> observedPreIndustrialWealth =
                new HashSet<IntercolonyWealthTier>();
            HashSet<IntercolonyArchetype> observedPreIndustrialArchetypes =
                new HashSet<IntercolonyArchetype>();
            int preIndustrialTechTiers = 0;

            foreach (TechLevel tech in AllTechLevels)
            {
                if (tech == TechLevel.Undefined || tech > TechLevel.Medieval)
                {
                    continue;
                }

                preIndustrialTechTiers++;
                for (int economySeed = -48; economySeed <= 48; economySeed++)
                {
                    for (int settlementId = -12; settlementId <= 12; settlementId++)
                    {
                        SettlementEconomicProfile profile =
                            SettlementProfileGenerator.GenerateFrom(
                                economySeed, settlementId, 7, "Preindustrial", "Testers", tech);
                        preIndustrialCombinations++;
                        observedPreIndustrialWealth.Add(profile.wealthTier);
                        observedPreIndustrialArchetypes.Add(profile.archetype);
                        if (profile.rapidLogisticsCapability ==
                            SettlementRapidLogisticsCapability.DropPodsAvailable)
                        {
                            preIndustrialCapable++;
                        }
                    }
                }
            }

            CheckObserved(
                "a pre-industrial settlement is never drop-pod capable",
                preIndustrialTechTiers > 0 && preIndustrialCombinations > 0 &&
                    preIndustrialCapable == 0,
                $"examined {preIndustrialCombinations} combinations across " +
                $"{preIndustrialTechTiers} raw tech tiers, " +
                $"{observedPreIndustrialWealth.Count} wealth tiers and " +
                $"{observedPreIndustrialArchetypes.Count} archetypes; capable " +
                $"{preIndustrialCapable}");

            // (b) Repeating a seeded identity must not consume a shared RNG stream. Several IDs
            // make a single coincidental equality insufficient evidence.
            int[] deterministicIds = { -101, -1, 0, 1, 7, 42, 999, int.MaxValue };
            bool capabilitiesRepeat = true;
            StringBuilder repeatedCapabilities = new StringBuilder();
            foreach (int settlementId in deterministicIds)
            {
                SettlementEconomicProfile first = SettlementProfileGenerator.GenerateFrom(
                    7919, settlementId, 23, "Determinism", "Testers", TechLevel.Spacer);
                SettlementEconomicProfile second = SettlementProfileGenerator.GenerateFrom(
                    7919, settlementId, 23, "Determinism", "Testers", TechLevel.Spacer);
                bool match = first.rapidLogisticsCapability == second.rapidLogisticsCapability;
                capabilitiesRepeat &= match;
                if (repeatedCapabilities.Length > 0)
                {
                    repeatedCapabilities.Append(", ");
                }

                repeatedCapabilities.Append($"id {settlementId}: ")
                    .Append(first.rapidLogisticsCapability)
                    .Append('/')
                    .Append(second.rapidLogisticsCapability);
            }

            CheckObserved(
                "the same settlement generates the same capability twice",
                capabilitiesRepeat,
                $"economy seed 7919, {deterministicIds.Length} IDs; " +
                $"pairs [{repeatedCapabilities}]");

            // (c) Compare the public profile with a capability-free replay of the existing
            // profile-roll stream under the exact same derived seed. Comparing every
            // non-capability field makes this a scope test, not just another capability equality
            // check: a capability draw inserted into the shared stream before a neighbour fails.
            const int scopeProbeEconomySeed = 20817;
            const int scopeProbeSettlementId = 913;
            SettlementEconomicProfile scopedProfile = SettlementProfileGenerator.GenerateFrom(
                scopeProbeEconomySeed, scopeProbeSettlementId, 11,
                "ScopeProbe", "Testers", TechLevel.Spacer);
            SettlementEconomicProfile scopeReference = null;
            string scopeReferenceFailure = null;
            try
            {
                scopeReference = GenerateNonCapabilityProfileReference(
                    scopeProbeEconomySeed, scopeProbeSettlementId, 11,
                    "ScopeProbe", "Testers", TechLevel.Spacer);
            }
            catch (Exception ex)
            {
                Exception cause = ex is TargetInvocationException && ex.InnerException != null
                    ? ex.InnerException
                    : ex;
                scopeReferenceFailure = cause.GetType().Name + ": " + cause.Message;
            }

            string scopeMismatch = null;
            bool scopeMatch = scopeReference != null &&
                ProfilesMatchExceptCapability(
                    scopedProfile, scopeReference, out scopeMismatch);
            CheckObserved(
                "capability does not shift the rest of the profile",
                scopeMatch,
                $"seed {scopedProfile.seed}; first capability " +
                $"{scopedProfile.rapidLogisticsCapability}; reference available " +
                $"{scopeReference != null}; non-capability fields match {scopeMatch}; " +
                $"mismatch {scopeMismatch ?? "none"}; reference failure " +
                $"{scopeReferenceFailure ?? "none"}");

            // (d) These are two fixed settlement identities observed over many economy seeds.
            // Only samples that actually generated the named wealth tier enter the comparison,
            // so the labels describe the profiles being compared rather than an aspiration about
            // what a random roll might have produced.
            const int highTechIdentity = 1701;
            const int poorIndustrialIdentity = 2701;
            const int comparisonSeedCount = 2048;
            int highTechWealthySamples = 0;
            int highTechWealthyCapable = 0;
            int poorIndustrialSamples = 0;
            int poorIndustrialCapable = 0;
            int highTechProfiles = 0;
            int poorIndustrialProfiles = 0;
            for (int economySeed = 0; economySeed < comparisonSeedCount; economySeed++)
            {
                SettlementEconomicProfile highTech = SettlementProfileGenerator.GenerateFrom(
                    economySeed, highTechIdentity, 31, "High-tech identity", "Testers",
                    TechLevel.Spacer);
                SettlementEconomicProfile poorIndustrial = SettlementProfileGenerator.GenerateFrom(
                    economySeed, poorIndustrialIdentity, 31, "Poor industrial identity", "Testers",
                    TechLevel.Industrial);

                highTechProfiles++;
                poorIndustrialProfiles++;
                if (highTech.wealthTier == IntercolonyWealthTier.Wealthy)
                {
                    highTechWealthySamples++;
                    if (highTech.rapidLogisticsCapability ==
                        SettlementRapidLogisticsCapability.DropPodsAvailable)
                    {
                        highTechWealthyCapable++;
                    }
                }

                if (poorIndustrial.wealthTier == IntercolonyWealthTier.Destitute)
                {
                    poorIndustrialSamples++;
                    if (poorIndustrial.rapidLogisticsCapability ==
                        SettlementRapidLogisticsCapability.DropPodsAvailable)
                    {
                        poorIndustrialCapable++;
                    }
                }
            }

            float highTechRate = highTechWealthySamples == 0
                ? 0f
                : highTechWealthyCapable * 100f / highTechWealthySamples;
            float poorIndustrialRate = poorIndustrialSamples == 0
                ? 0f
                : poorIndustrialCapable * 100f / poorIndustrialSamples;
            bool ordering = highTechWealthySamples > 0 && poorIndustrialSamples > 0 &&
                (long)highTechWealthyCapable * poorIndustrialSamples >
                (long)poorIndustrialCapable * highTechWealthySamples;
            CheckObserved(
                "a high-tech wealthy settlement is more often capable than a poor industrial one",
                ordering,
                $"high-tech identity {highTechIdentity}: {highTechWealthyCapable}/" +
                $"{highTechWealthySamples} capable ({highTechRate:0.0}%), " +
                $"poor-industrial identity {poorIndustrialIdentity}: " +
                $"{poorIndustrialCapable}/{poorIndustrialSamples} capable " +
                $"({poorIndustrialRate:0.0}%); generated profiles {highTechProfiles}/" +
                $"{poorIndustrialProfiles}");

            passed += observedPassed;
            failed += observedFailed;
        }

        private static SettlementEconomicProfile GenerateNonCapabilityProfileReference(
            int economySeed, int settlementId, int factionLoadId,
            string settlementName, string factionName, TechLevel rawTech)
        {
            MethodInfo normalizeTech = GeneratorPrivateMethod("NormalizeTech");
            MethodInfo rollArchetype = GeneratorPrivateMethod("RollArchetype");
            MethodInfo rollWealth = GeneratorPrivateMethod("RollWealth");
            MethodInfo rollQualityPreference = GeneratorPrivateMethod("RollQualityPreference");
            MethodInfo rollLaborModifier = GeneratorPrivateMethod("RollLaborModifier");
            MethodInfo fillWeights = GeneratorPrivateMethod("FillWeights");
            if (normalizeTech == null || rollArchetype == null || rollWealth == null ||
                rollQualityPreference == null || rollLaborModifier == null || fillWeights == null)
            {
                throw new MissingMethodException(
                    "SettlementProfileGenerator profile-roll helper was unavailable");
            }

            int seed = Gen.HashCombineInt(economySeed, settlementId);
            TechLevel techTier = (TechLevel)normalizeTech.Invoke(null, new object[] { rawTech });
            SettlementEconomicProfile profile = new SettlementEconomicProfile
            {
                settlementId = settlementId,
                factionLoadId = factionLoadId,
                settlementName = string.IsNullOrEmpty(settlementName) ? "unnamed" : settlementName,
                factionName = string.IsNullOrEmpty(factionName) ? "no faction" : factionName,
                techTier = techTier,
                seed = seed
            };

            Rand.PushState(seed);
            try
            {
                profile.archetype = (IntercolonyArchetype)rollArchetype.Invoke(
                    null, new object[] { profile.techTier });
                profile.wealthTier = (IntercolonyWealthTier)rollWealth.Invoke(
                    null, new object[] { profile.archetype, profile.techTier });
                profile.volatility = Rand.Range(0.05f, 0.35f);
                profile.qualityPreference = (float)rollQualityPreference.Invoke(
                    null, new object[] { profile.archetype, profile.wealthTier });
                profile.laborSupplyModifier = (float)rollLaborModifier.Invoke(
                    null, new object[] { profile.archetype, profile.wealthTier });
                fillWeights.Invoke(null, new object[] { profile });
            }
            finally
            {
                Rand.PopState();
            }

            return profile;
        }

        private static MethodInfo GeneratorPrivateMethod(string name)
        {
            return typeof(SettlementProfileGenerator).GetMethod(
                name, BindingFlags.Static | BindingFlags.NonPublic);
        }

        private static bool ProfilesMatchExceptCapability(
            SettlementEconomicProfile actual, SettlementEconomicProfile expected,
            out string mismatch)
        {
            List<string> mismatches = new List<string>();
            if (actual == null || expected == null)
            {
                mismatch = "actual or expected profile was null";
                return false;
            }

            if (actual.settlementId != expected.settlementId)
            {
                mismatches.Add("settlementId");
            }

            if (actual.factionLoadId != expected.factionLoadId)
            {
                mismatches.Add("factionLoadId");
            }

            if (actual.settlementName != expected.settlementName)
            {
                mismatches.Add("settlementName");
            }

            if (actual.factionName != expected.factionName)
            {
                mismatches.Add("factionName");
            }

            if (actual.techTier != expected.techTier ||
                actual.archetype != expected.archetype ||
                actual.wealthTier != expected.wealthTier ||
                actual.seed != expected.seed)
            {
                mismatches.Add("identity tiers or seed");
            }

            if (!FloatMatches(actual.volatility, expected.volatility))
            {
                mismatches.Add("volatility");
            }

            if (!FloatMatches(actual.qualityPreference, expected.qualityPreference))
            {
                mismatches.Add("qualityPreference");
            }

            if (!FloatMatches(actual.laborSupplyModifier, expected.laborSupplyModifier))
            {
                mismatches.Add("laborSupplyModifier");
            }

            CompareWeights(actual.demandWeights, expected.demandWeights, "demandWeights", mismatches);
            CompareWeights(actual.supplyWeights, expected.supplyWeights, "supplyWeights", mismatches);
            mismatch = mismatches.Count == 0 ? null : string.Join(", ", mismatches);
            return mismatches.Count == 0;
        }

        private static void CompareWeights(
            float[] actual, float[] expected, string label, List<string> mismatches)
        {
            if (actual == null || expected == null || actual.Length != expected.Length)
            {
                mismatches.Add(label);
                return;
            }

            for (int i = 0; i < actual.Length; i++)
            {
                if (!FloatMatches(actual[i], expected[i]))
                {
                    mismatches.Add(label);
                    return;
                }
            }
        }

        private static bool FloatMatches(float actual, float expected)
        {
            return Math.Abs(actual - expected) <= 0.0001f;
        }

        private static string CurrentWorldSettlementCapabilityMeasurement()
        {
            IntercolonyWorldComponent state = IntercolonyWorldComponent.Current;
            Dictionary<TechLevel, int[]> byTechTier = new Dictionary<TechLevel, int[]>();
            int eligibleSettlements = 0;
            int capableSettlements = 0;

            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements != null)
            {
                foreach (Settlement settlement in settlements)
                {
                    if (!SettlementProfileGenerator.IsEligible(settlement))
                    {
                        continue;
                    }

                    eligibleSettlements++;
                    SettlementEconomicProfile profile = state?.GetProfile(settlement);
                    if (profile == null)
                    {
                        continue;
                    }

                    if (!byTechTier.TryGetValue(profile.techTier, out int[] tierCounts))
                    {
                        tierCounts = new int[2];
                        byTechTier.Add(profile.techTier, tierCounts);
                    }

                    tierCounts[0]++;
                    if (profile.rapidLogisticsCapability ==
                        SettlementRapidLogisticsCapability.DropPodsAvailable)
                    {
                        capableSettlements++;
                        tierCounts[1]++;
                    }
                }
            }

            float capablePercentage = eligibleSettlements == 0
                ? 0f
                : capableSettlements * 100f / eligibleSettlements;
            List<string> tierBreakdown = new List<string>();
            foreach (TechLevel tech in AllTechLevels)
            {
                if (byTechTier.TryGetValue(tech, out int[] tierCounts))
                {
                    tierBreakdown.Add(
                        $"{tech}: {tierCounts[0]} total, {tierCounts[1]} capable");
                }
            }

            return $"world settlement capability: eligible settlements {eligibleSettlements}; " +
                $"drop-pod capable {capableSettlements}/{eligibleSettlements} " +
                $"({capablePercentage:0.0}%); by tech tier " +
                $"[{(tierBreakdown.Count == 0 ? "none" : string.Join("; ", tierBreakdown))}]";
        }
    }
}
