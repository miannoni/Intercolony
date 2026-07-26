using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// In-game assertions over Phase 4 market generation (DESIGN.md §83.2).
    ///
    /// Covers the invariants that are tedious or slow to confirm by playing: saturation
    /// behaviour (§13), that the §47 price breakdown actually reconstructs the price,
    /// generation determinism (§60), and the opportunity state machine (§73).
    /// </summary>
    public static class IntercolonyMarketSelfTest
    {
        public static string Run(IntercolonyWorldComponent state)
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

            sb.AppendLine("Market generation self-test");

            // --- Classifier ---
            List<ThingDef> tradable = IntercolonyProductClassifier.TradableDefs;
            Check("tradable def set is non-empty", tradable.Count > 0, $"count {tradable.Count}");

            bool allClassified = true;
            foreach (ThingDef def in tradable)
            {
                if (!IntercolonyProductClassifier.Classify(def).HasValue)
                {
                    allClassified = false;
                }
            }

            Check("every tradable def classifies", allClassified);

            // --- Blacklist (§64) ---
            int blacklistedButTraded = 0;
            string firstLeak = null;
            foreach (ThingDef def in tradable)
            {
                if (IntercolonyTradeBlacklist.IsBlacklisted(def))
                {
                    blacklistedButTraded++;
                    firstLeak = firstLeak ?? def.defName;
                }
            }

            Check("no blacklisted def is tradable", blacklistedButTraded == 0,
                $"{blacklistedButTraded} leaked, first: {firstLeak}");

            // Fertilized eggs specifically: the rule is comp-based, so verify it actually
            // bites rather than trusting that the XML parsed.
            int hatchers = 0;
            int hatchersTradable = 0;
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.HasComp(typeof(CompHatcher)))
                {
                    hatchers++;
                    if (IntercolonyProductClassifier.IsFungibleTradeItem(def))
                    {
                        hatchersTradable++;
                    }
                }
            }

            Check("fertilized eggs exist to be excluded", hatchers > 0, $"found {hatchers}");
            Check("no fertilized egg is tradable", hatchersTradable == 0, $"{hatchersTradable} tradable");

            IntercolonyProductCategory[] traded =
            {
                IntercolonyProductCategory.Commodities,
                IntercolonyProductCategory.IntermediateGoods,
                IntercolonyProductCategory.ManufacturedGoods
            };
            foreach (IntercolonyProductCategory category in traded)
            {
                int n = IntercolonyProductClassifier.DefsInCategory(category).Count;
                Check($"category {category.Label()} has candidates", n > 0, $"count {n}");
            }

            // --- Saturation (§13): more units must never mean a better unit price ---
            float previous = float.MaxValue;
            bool monotonic = true;
            for (int q = 1; q <= 4000; q += 137)
            {
                float f = IntercolonyPricing.SaturationFactor(q);
                if (f > previous + 0.0001f)
                {
                    monotonic = false;
                }

                previous = f;
            }

            Check("saturation is non-increasing in quantity", monotonic);
            Check("saturation starts at a premium", IntercolonyPricing.SaturationFactor(1) > 1.15f,
                IntercolonyPricing.SaturationFactor(1).ToString("F3"));
            Check("saturation bottoms out below par", IntercolonyPricing.SaturationFactor(100000) < 1f,
                IntercolonyPricing.SaturationFactor(100000).ToString("F3"));

            // --- Pricing: the §47 breakdown must reconstruct the price it explains ---
            List<SettlementEconomicProfile> profiles = state.AllProfiles();
            if (profiles.Count == 0)
            {
                sb.AppendLine("  (no eligible settlements; pricing and generation checks skipped)");
            }
            else if (tradable.Count > 0)
            {
                SettlementEconomicProfile profile = profiles[0];
                ThingDef sample = tradable[0];
                IntercolonyProductCategory category =
                    IntercolonyProductClassifier.Classify(sample) ?? IntercolonyProductCategory.Commodities;

                float price = IntercolonyPricing.UnitPrice(
                    sample, 500, profile, category, 25f, null, out List<PriceFactor> factors);

                Check("unit price is positive", price > 0f, price.ToString("F3"));
                Check("breakdown has factors", factors.Count >= 3, $"count {factors.Count}");

                float reconstructed = sample.BaseMarketValue;
                foreach (PriceFactor factor in factors)
                {
                    reconstructed *= factor.multiplier;
                }

                Check("breakdown reconstructs the price",
                    Mathf.Abs(reconstructed - price) < 0.01f,
                    $"explained {reconstructed:F4} vs actual {price:F4}");

                string explanation = IntercolonyPricing.Explain(sample, 500, price, factors);
                Check("explanation mentions the unit price", explanation.Contains("Unit price"));

                // A quality-expectations factor on something that cannot carry quality is an
                // unexplainable line in the player-facing breakdown (§47). Sweep every
                // tradable def rather than trusting one sample.
                int qualityOnNonQuality = 0;
                string firstOffender = null;
                foreach (ThingDef def in tradable)
                {
                    if (IntercolonyPricing.CanHaveQuality(def))
                    {
                        continue;
                    }

                    IntercolonyProductCategory c =
                        IntercolonyProductClassifier.Classify(def) ?? IntercolonyProductCategory.Commodities;
                    IntercolonyPricing.UnitPrice(def, 100, profile, c, 10f, null, out List<PriceFactor> defFactors);
                    foreach (PriceFactor factor in defFactors)
                    {
                        if (factor.label.Contains("Quality"))
                        {
                            qualityOnNonQuality++;
                            firstOffender = firstOffender ?? def.defName;
                        }
                    }
                }

                Check("no quality factor on non-quality goods", qualityOnNonQuality == 0,
                    $"{qualityOnNonQuality} offender(s), first: {firstOffender}");

                // --- Generation determinism (§60) ---
                Settlement settlement = null;
                foreach (Settlement candidate in Find.WorldObjects.Settlements)
                {
                    if (SettlementProfileGenerator.IsEligible(candidate))
                    {
                        settlement = candidate;
                        break;
                    }
                }

                if (settlement == null)
                {
                    sb.AppendLine("  (no eligible settlement; generation checks skipped)");
                }
                else
                {
                    SettlementEconomicProfile settlementProfile = state.GetProfile(settlement);

                    // Generation is probabilistic (roughly a third of settlements post on any
                    // given cycle), so a single (settlement, refresh) pair frequently yields
                    // nothing. Testing one pair meant the per-opportunity assertions below
                    // silently ran zero times. Sweep refresh numbers until output appears, and
                    // separately gather a large sample for the invariant checks.
                    int fertileRefresh = -1;
                    List<MarketOpportunity> runA = null;
                    for (int r = 0; r < 60 && fertileRefresh < 0; r++)
                    {
                        int c = 1000;
                        List<MarketOpportunity> candidate = MarketOpportunityGenerator.GenerateFor(
                            settlement, settlementProfile, 4242, r, 0, () => c++);
                        if (candidate.Count > 0)
                        {
                            fertileRefresh = r;
                            runA = candidate;
                        }
                    }

                    Check("found a refresh cycle that generates demand", fertileRefresh >= 0,
                        "60 consecutive cycles produced nothing");

                    if (fertileRefresh >= 0)
                    {
                        int counterB = 1000;
                        List<MarketOpportunity> runB = MarketOpportunityGenerator.GenerateFor(
                            settlement, settlementProfile, 4242, fertileRefresh, 0, () => counterB++);

                        Check("same seed gives same opportunity count", runA.Count == runB.Count,
                            $"{runA.Count} vs {runB.Count}");

                        bool identical = runA.Count == runB.Count;
                        for (int i = 0; i < Mathf.Min(runA.Count, runB.Count); i++)
                        {
                            if (runA[i].thingDef != runB[i].thingDef ||
                                runA[i].quantity != runB[i].quantity ||
                                Mathf.Abs(runA[i].unitPrice - runB[i].unitPrice) > 0.0001f)
                            {
                                identical = false;
                            }
                        }

                        Check("same seed gives identical opportunities", identical);

                        // A different cycle must actually change the roll, or the determinism
                        // check above would be passing for the wrong reason. No escape hatch:
                        // if every other cycle matched, that is a genuine failure.
                        bool anyDiffers = false;
                        for (int r = 0; r < 60 && !anyDiffers; r++)
                        {
                            if (r == fertileRefresh)
                            {
                                continue;
                            }

                            int c = 1000;
                            List<MarketOpportunity> other = MarketOpportunityGenerator.GenerateFor(
                                settlement, settlementProfile, 4242, r, 0, () => c++);
                            if (other.Count != runA.Count)
                            {
                                anyDiffers = true;
                                break;
                            }

                            for (int i = 0; i < other.Count; i++)
                            {
                                if (other[i].thingDef != runA[i].thingDef ||
                                    other[i].quantity != runA[i].quantity)
                                {
                                    anyDiffers = true;
                                    break;
                                }
                            }
                        }

                        Check("a different refresh cycle changes the roll", anyDiffers,
                            "every cycle produced identical demand");
                    }

                    Check("respects the per-settlement cap",
                        MarketOpportunityGenerator.GenerateFor(
                            settlement, settlementProfile, 4242, 7,
                            MarketOpportunityGenerator.MaxPerSettlement, () => 1).Count == 0);

                    // Invariant sweep over a large generated sample rather than whatever one
                    // cycle happened to produce.
                    List<MarketOpportunity> sampleSet = new List<MarketOpportunity>();
                    int idCounter = 5000;
                    int settlementsTried = 0;
                    foreach (Settlement s in Find.WorldObjects.Settlements)
                    {
                        if (!SettlementProfileGenerator.IsEligible(s) || settlementsTried >= 12)
                        {
                            continue;
                        }

                        settlementsTried++;
                        SettlementEconomicProfile p = state.GetProfile(s);
                        for (int r = 0; r < 25; r++)
                        {
                            sampleSet.AddRange(MarketOpportunityGenerator.GenerateFor(
                                s, p, 4242, r, 0, () => idCounter++));
                        }
                    }

                    Check("generated a meaningful sample", sampleSet.Count >= 20,
                        $"only {sampleSet.Count} opportunities from {settlementsTried} settlements");

                    int badQuantity = 0, badPrice = 0, badExpiry = 0, badDeadline = 0;
                    int blacklisted = 0, untradable = 0, badTotal = 0;
                    HashSet<int> seenIds = new HashSet<int>();
                    int duplicateIds = 0;

                    foreach (MarketOpportunity o in sampleSet)
                    {
                        if (o.quantity <= 0) badQuantity++;
                        if (o.unitPrice <= 0f) badPrice++;
                        if (o.expiryTick <= GenTicks.TicksGame) badExpiry++;
                        if (o.deadlineDays < 1 || o.deadlineDays > 60) badDeadline++;
                        if (o.TotalPrice <= 0) badTotal++;
                        if (IntercolonyTradeBlacklist.IsBlacklisted(o.thingDef)) blacklisted++;
                        if (!IntercolonyProductClassifier.IsFungibleTradeItem(o.thingDef)) untradable++;
                        if (!seenIds.Add(o.id)) duplicateIds++;
                    }

                    Check($"all {sampleSet.Count} quantities positive", badQuantity == 0, $"{badQuantity} bad");
                    Check($"all {sampleSet.Count} prices positive", badPrice == 0, $"{badPrice} bad");
                    Check($"all {sampleSet.Count} totals positive", badTotal == 0, $"{badTotal} bad");
                    Check($"all {sampleSet.Count} expiries in the future", badExpiry == 0, $"{badExpiry} bad");
                    Check($"all {sampleSet.Count} deadlines sane", badDeadline == 0, $"{badDeadline} bad");
                    // --- Quality constraints are actually generated (§99) ---
                    // Without this, the whole quality path could be dead code and every other
                    // assertion would still pass.
                    int qualityCapable = 0;
                    int withConstraint = 0;
                    int constraintOnNonQuality = 0;
                    foreach (MarketOpportunity o in sampleSet)
                    {
                        if (IntercolonyPricing.CanHaveQuality(o.thingDef))
                        {
                            qualityCapable++;
                            if (o.minQuality.HasValue)
                            {
                                withConstraint++;
                            }
                        }
                        else if (o.minQuality.HasValue)
                        {
                            constraintOnNonQuality++;
                        }
                    }

                    Check("quality-capable goods appear in generated demand", qualityCapable > 0,
                        $"none of {sampleSet.Count} sampled offers could carry quality");
                    Check("some quality demands carry a minimum", withConstraint > 0,
                        $"0 of {qualityCapable} quality-capable offers asked for a minimum");
                    Check("no quality demand on goods without quality", constraintOnNonQuality == 0,
                        $"{constraintOnNonQuality} offer(s)");
                    sb.AppendLine($"  ({withConstraint} of {qualityCapable} quality-capable offers " +
                                  $"carried a minimum quality)");

                    // --- §101 finished goods: crated buildings reach the market ---
                    int cratedGoods = 0;
                    int oversizedCratedLot = 0;
                    int nonMinifiableBuilding = 0;
                    int withStuff = 0;
                    int stuffOnNonStuffable = 0;
                    foreach (MarketOpportunity o in sampleSet)
                    {
                        if (o.thingDef.category == ThingCategory.Building)
                        {
                            cratedGoods++;
                            if (o.quantity > 8)
                            {
                                oversizedCratedLot++;
                            }

                            // A non-minifiable building could never be delivered — the caravan
                            // physically cannot carry it (docs/unique-goods-spike.md).
                            if (!o.thingDef.Minifiable)
                            {
                                nonMinifiableBuilding++;
                            }
                        }

                        if (o.stuffDef != null)
                        {
                            withStuff++;
                            if (!o.thingDef.MadeFromStuff)
                            {
                                stuffOnNonStuffable++;
                            }
                        }
                    }

                    Check("furniture and equipment reach the market", cratedGoods > 0,
                        $"0 of {sampleSet.Count} sampled offers were buildings");
                    Check("no non-minifiable building is ever demanded", nonMinifiableBuilding == 0,
                        $"{nonMinifiableBuilding} undeliverable offer(s)");
                    Check("crated lots stay small", oversizedCratedLot == 0,
                        $"{oversizedCratedLot} lot(s) over 8 crates");
                    Check("some demands specify a material", withStuff > 0,
                        "no offer in the sample required a material");
                    Check("no material demand on a def that cannot use one", stuffOnNonStuffable == 0,
                        $"{stuffOnNonStuffable} offer(s)");
                    sb.AppendLine($"  ({cratedGoods} crated-good offers, {withStuff} with a required material)");

                    // Material-aware valuation (§101): the same def in a costlier material must
                    // be worth more, or "material-aware" is a label with nothing behind it.
                    ThingDef stuffable = null;
                    foreach (ThingDef def in tradable)
                    {
                        if (def.MadeFromStuff)
                        {
                            stuffable = def;
                            break;
                        }
                    }

                    if (stuffable != null)
                    {
                        float wood = IntercolonyPricing.BaseValue(stuffable, ThingDefOf.WoodLog);
                        float plasteel = IntercolonyPricing.BaseValue(stuffable, ThingDefOf.Plasteel);
                        float stuffless = IntercolonyPricing.BaseValue(stuffable, null);
                        Check("material changes base value",
                            !Mathf.Approximately(wood, plasteel),
                            $"{stuffable.defName}: wood {wood:F1} vs plasteel {plasteel:F1}");
                        Check("costlier material is worth more", plasteel > wood,
                            $"wood {wood:F1}, plasteel {plasteel:F1}");
                        Check("stuffless base value is still positive", stuffless > 0f);
                    }

                    Check("no generated item is blacklisted", blacklisted == 0, $"{blacklisted} leaked");
                    Check("every generated item is a fungible trade item", untradable == 0, $"{untradable} bad");
                    Check("allocated IDs are unique", duplicateIds == 0, $"{duplicateIds} duplicates");

                    // --- Market access (§51): hostile factions must not post demand ---
                    int hostileSettlements = 0;
                    int hostileGeneratedAnyway = 0;
                    foreach (Settlement s in Find.WorldObjects.Settlements)
                    {
                        if (!SettlementProfileGenerator.IsEligible(s))
                        {
                            continue;
                        }

                        if (IntercolonyMarketAccess.IsAccessible(s))
                        {
                            continue;
                        }

                        hostileSettlements++;
                        SettlementEconomicProfile p = state.GetProfile(s);
                        int c = 9000;
                        for (int r = 0; r < 20; r++)
                        {
                            hostileGeneratedAnyway += MarketOpportunityGenerator
                                .GenerateFor(s, p, 4242, r, 0, () => c++).Count;
                        }
                    }

                    Check("inaccessible settlements generate nothing", hostileGeneratedAnyway == 0,
                        $"{hostileGeneratedAnyway} from {hostileSettlements} inaccessible settlement(s)");

                    sb.AppendLine($"  (sample {sampleSet.Count} opportunities from {settlementsTried} " +
                                  $"settlements; {hostileSettlements} inaccessible settlement(s) present)");

                    // Every live listing must belong to a currently-accessible buyer.
                    int listedButInaccessible = 0;
                    foreach (MarketOpportunity o in state.Opportunities)
                    {
                        if (o.IsAvailable && !IntercolonyMarketAccess.IsStillValid(o))
                        {
                            listedButInaccessible++;
                        }
                    }

                    Check("no live listing has an inaccessible buyer", listedButInaccessible == 0,
                        $"{listedButInaccessible} stale (run Advance refresh to withdraw them)");

                    // --- Distance (§48, §53) ---
                    int missingDistance = 0;
                    foreach (MarketOpportunity o in sampleSet)
                    {
                        // -1 is legitimate only when the player has no home map at all.
                        if (o.distanceTiles < 0f && Find.AnyPlayerHomeMap != null)
                        {
                            missingDistance++;
                        }
                    }

                    Check("generated opportunities record a distance", missingDistance == 0,
                        $"{missingDistance} missing");
                }
            }

            // --- Global ceiling (§5.2 "No infinite global catalog") ---
            // Regression guard: the per-settlement cap alone let total demand scale with world
            // size, reaching 695 live offers on a full-size map.
            int beforeRefreshes = state.Opportunities.Count;
            for (int i = 0; i < 12; i++)
            {
                state.GenerateOpportunities();
            }

            Check("live offers stay under the global ceiling",
                state.ActiveOpportunityCount <= IntercolonyWorldComponent.MaxLiveOpportunities,
                $"{state.ActiveOpportunityCount} live, ceiling {IntercolonyWorldComponent.MaxLiveOpportunities}");
            sb.AppendLine($"  (12 extra refreshes took live offers from {beforeRefreshes} to " +
                          $"{state.ActiveOpportunityCount}, ceiling {IntercolonyWorldComponent.MaxLiveOpportunities})");

            // --- Opportunity state machine (§73) ---
            MarketOpportunity probe = new MarketOpportunity
            {
                id = 1,
                quantity = 10,
                expiryTick = GenTicks.TicksGame - 1,
                state = MarketOpportunityState.Available
            };

            Check("lapsed opportunity reports expired", probe.HasExpired(GenTicks.TicksGame));
            Check("first expire succeeds", probe.TryExpire());
            Check("state is Expired", probe.state == MarketOpportunityState.Expired);
            Check("second expire is refused", !probe.TryExpire());
            Check("expired opportunity is not available", !probe.IsAvailable);

            // --- Global RNG isolation, same discipline as profiles (§60) ---
            Rand.PushState(777001);
            int expected = Rand.Int;
            Rand.PopState();

            Rand.PushState(777001);
            IntercolonyPricing.SaturationFactor(123);
            if (profiles.Count > 0 && tradable.Count > 0)
            {
                IntercolonyPricing.UnitPrice(tradable[0], 100, profiles[0],
                    IntercolonyProductCategory.Commodities, 10f, null, out _);
            }

            int actual = Rand.Int;
            Rand.PopState();
            Check("pricing leaves global RNG untouched", expected == actual,
                $"got {actual}, expected {expected}");

            sb.AppendLine($"  {passed} passed, {failed} failed.");
            return sb.ToString();
        }
    }
}
