using System.Collections.Generic;
using System.Text;
using System.IO;
using System;
using System.Xml;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse.AI;
using Verse;
using Verse.AI.Group;

namespace Intercolony
{
    /// <summary>
    /// End-to-end check of Phase 16's acceptance criteria (DESIGN.md §109).
    ///
    /// This drives the **real** hire path — <see cref="EmploymentService.TryHire"/>,
    /// <see cref="EmploymentService.Advance"/>, <see cref="EmploymentService.End"/> — rather
    /// than a convenient stand-in. Phase 4 taught that a test built against a private copy of
    /// the logic passes vacuously, and Phase 14 that it can also fail spuriously.
    ///
    /// The employment record's ordinary save/load shape is checked elsewhere in this suite. The
    /// death fixture below also round-trips the vanilla corpse reference through a temporary
    /// Scribe file, because that is where a discarded pawn becomes an empty corpse.
    /// </summary>
    public static class IntercolonyLaborSelfTest
    {
        // Independent F24 assertion oracles. Keep these separate from the production constants:
        // changing a rule must make the assertion explain what moved instead of moving its own
        // goalposts with the implementation.
        private const float ExpectedEmergencyMarketFraction = 0.5f;
        private const float ExpectedEmergencyWageMultiplier = 4f;
        private const int ExpectedCurrentSaveVersion = 58;

        // DailyWageFor rounds once after applying the urgency multiplier, while the ordinary
        // listing exposes its already-rounded daily wage. The independent integer oracle is
        // therefore allowed the two-silver maximum induced by that two-observation rounding gap.
        private const int EmergencyWageRoundingTolerance = 2;
        private class Results
        {
            public readonly StringBuilder sb = new StringBuilder();
            public int passed;
            public int failed;
            public int skipped;

            public void Check(bool condition, string label, string detail = null)
            {
                if (condition)
                {
                    passed++;
                    sb.AppendLine($"  PASS  {label}{(detail == null ? "" : $"  ({detail})")}");
                }
                else
                {
                    failed++;
                    sb.AppendLine($"  FAIL  {label}{(detail == null ? "" : $"  ({detail})")}");
                }
            }

            public void Info(string line)
            {
                sb.AppendLine($"        {line}");
            }

            public void Skip(string label, string detail)
            {
                skipped++;
                sb.AppendLine($"  SKIPPED  {label}  ({detail})");
            }
        }

        public static string Run(IntercolonyWorldComponent state, Map map)
        {
            Results r = new Results();
            r.sb.AppendLine("Labor self-test (DESIGN.md §109)");

            if (state == null || map == null)
            {
                r.sb.AppendLine("  No world or map. Open a colony first.");
                return r.sb.ToString();
            }

            int savedSilver = PurchaseOrderService.CountColonySilver(map);
            int savedEmployments = state.Employments.Count;
            EmployerReputation savedStandingOwner = state.EmployerStanding;
            float savedStanding = savedStandingOwner?.Score ?? 0f;
            int savedLedger = state.Ledger.Count;
            int savedLedgerStartTick = state.LedgerStartTick;
            int savedTick = GenTicks.TicksGame;
            int worldPawnsBefore = Find.WorldPawns?.AllPawnsAliveOrDead?.Count ?? 0;
            List<Pawn> fixturePawns = new List<Pawn>();
            List<Thing> fixtureItems = new List<Thing>();
            List<Corpse> fixtureCorpses = new List<Corpse>();
            List<Building_Grave> fixtureGraves = new List<Building_Grave>();
            IntercolonyLaborSelfTestSupport.ResetLedger();

            try
            {
                // --- Candidate pool ---
                List<LaborCandidate> pool = LaborCandidateService.Refresh(state);
                r.Check(pool.Count > 0, "candidate pool is not empty", $"{pool.Count} workers offered");

                if (pool.Count == 0)
                {
                    SkipMainEquipmentBondChecks(r, "the labor candidate pool was empty");
                    SkipPartialEquipmentBondChecks(r, "the labor candidate pool was empty");
                    r.sb.AppendLine("  Cannot continue without a candidate.");
                    return Summarize(r);
                }

                CheckPricing(r, state, pool);
                List<LaborCandidate> f24OrdinaryPool =
                    new List<LaborCandidate>(pool);
                List<LaborCandidate> f24EmergencyPool =
                    new List<LaborCandidate>(f24OrdinaryPool);
                f24EmergencyPool.RemoveAll(candidate =>
                    !LaborCandidateService.CanReachEmergency(candidate));

                // --- Hire ---
                LaborCandidate candidate = pool[0];
                LaborCandidate longEmergencyCandidate = f24EmergencyPool.Find(
                    offered => offered?.pawn != null &&
                               EmergencyArrivalDaysForAssertion(offered.travelDays) <
                               offered.travelDays);
                if (ReferenceEquals(candidate, longEmergencyCandidate) && pool.Count > 1)
                {
                    // Leave the longer-trip urgent fixture available for U3 when the ordinary
                    // listing has another worker the existing F23 path can hire instead.
                    foreach (LaborCandidate alternative in pool)
                    {
                        if (alternative?.pawn != null &&
                            !ReferenceEquals(alternative, longEmergencyCandidate))
                        {
                            candidate = alternative;
                            break;
                        }
                    }
                }

                int term = candidate.minTermDays;
                List<Thing> mainFixtureItems = new List<Thing>();
                bool mainFixtureBuilt = BuildEquipmentBondFixture(
                    candidate, mainFixtureItems, out string mainFixtureFailureReason);
                fixtureItems.AddRange(mainFixtureItems);
                if (!mainFixtureBuilt)
                {
                    SkipMainEquipmentBondChecks(r, mainFixtureFailureReason);
                }

                EmploymentHireCostQuote hireQuote =
                    IntercolonyLaborSelfTestSupport.QuoteHireCost(
                        state, candidate, term, WageStructure.Prepaid, CombatClause.Civilian,
                        out string quoteFailReason);
                if (hireQuote == null)
                {
                    r.Check(false, "hire cost could be quoted", quoteFailReason);
                    if (mainFixtureBuilt)
                    {
                        SkipMainEquipmentBondChecks(
                            r, $"the hire path could not quote the fixture: {quoteFailReason}");
                    }

                    SkipPartialEquipmentBondChecks(
                        r, $"the hire path could not quote the first fixture: {quoteFailReason}");
                    return Summarize(r);
                }

                int expectedTotal = IntercolonyLaborSelfTestSupport.SilverToEnsure(hireQuote);

                // Budget for the second hire too, or the early-dismissal check silently skips —
                // which is how the KeepForever unpin path went unexercised on the first run.
                int budget = expectedTotal;
                if (pool.Count > 1)
                {
                    EmploymentHireCostQuote secondQuote =
                        IntercolonyLaborSelfTestSupport.QuoteHireCost(
                            state, pool[1], pool[1].minTermDays, WageStructure.Prepaid,
                            CombatClause.Civilian, out string secondQuoteFailReason);
                    if (secondQuote == null)
                    {
                        r.Info($"second hire budget skipped: {secondQuoteFailReason}");
                    }
                    else
                    {
                        budget = IntercolonyLaborSelfTestSupport.SilverToEnsure(
                            hireQuote.totalDue + secondQuote.totalDue);
                    }
                }

                int added = IntercolonyLaborSelfTestSupport.EnsureSilver(map, budget);
                if (added > 0)
                {
                    r.Info($"added {added} silver to storage so the hire path could run.");
                }

                int silverBefore = PurchaseOrderService.CountColonySilver(map);
                if (silverBefore < expectedTotal)
                {
                    r.Check(false, "colony can afford the cheapest worker",
                        $"{silverBefore} silver in storage, {expectedTotal} needed");
                    return Summarize(r);
                }

                Faction employer = candidate.faction;
                PawnKindDef originalKind = candidate.pawn.kindDef;
                string workerName = candidate.Name;

                // Captured before the hire, because hiring releases the pawn from the candidate
                // and anything read from it afterwards is a fallback string, not the truth.
                string expectedSkills = candidate.SkillSummary();

                EmploymentContract contract = EmploymentService.TryHire(
                    state, candidate, term, map, out string failReason,
                    WageStructure.Prepaid, CombatClause.Civilian, hireQuote);

                r.Check(contract != null, "hire succeeded", failReason ?? $"{workerName}, {term} days");
                if (contract == null)
                {
                    return Summarize(r);
                }

                TrackPawn(fixturePawns, contract.pawn);

                r.Check(contract.workerSkills == expectedSkills,
                    "the record froze the worker's real skills",
                    $"expected \"{expectedSkills}\", got \"{contract.workerSkills}\"");

                int silverAfter = PurchaseOrderService.CountColonySilver(map);
                if (mainFixtureBuilt)
                {
                    // 1.10f is the independent assertion oracle, not the production premium
                    // constant. The value comes from the fixture Things, not the quote snapshot.
                    float expectedReplacementValue = IndependentReplacementValue(
                        mainFixtureItems, out string mainEquipmentDetail);
                    float expectedBondBeforeRounding = expectedReplacementValue * 1.10f;
                    int expectedBond = Mathf.Max(0, Mathf.RoundToInt(expectedBondBeforeRounding));
                    long actualBondCharge = (long)silverBefore - silverAfter - contract.paidSilver;
                    r.Check(contract.equipmentBond == expectedBond && actualBondCharge == expectedBond,
                        "E1 equipment bond is recorded-item value plus 10%",
                        $"items [{mainEquipmentDetail}], replacement {expectedReplacementValue:0.###}, " +
                        $"value plus 10% {expectedBondBeforeRounding:0.###}, expected bond {expectedBond}, " +
                        $"recorded bond {contract.equipmentBond}, actual bond debit {actualBondCharge:0.###}");
                }

                // --- F24 emergency dispatch -------------------------------------------------
                // The emergency assertions sit beside F23's hire-time bond assertion because
                // both features are commitments made on this same real direct-hire path.
                CheckEmergencyDispatch(
                    r, state, map, fixturePawns, contract,
                    f24OrdinaryPool, f24EmergencyPool);

                long expectedWithdrawal = (long)contract.paidSilver + contract.equipmentBond;
                r.Check(silverBefore - silverAfter == expectedWithdrawal,
                    "hire cost was deducted exactly once",
                    $"{silverBefore} -> {silverAfter}, contract says {contract.paidSilver} wages + " +
                    $"{contract.equipmentBond} bond");
                // Not dailyWage x termDays: from Phase 18 a prepaid hire carries §37's discount,
                // so the gross rate is the wrong expectation and TotalCommitment is the right one.
                r.Check(contract.paidSilver == contract.TotalCommitment,
                    "the prepaid total matches the quoted commitment",
                    $"{contract.dailyWage}/day x {contract.termDays}d = {contract.TotalCommitment} " +
                    $"({contract.dailyWage * contract.termDays} before the prepay discount), " +
                    $"paid {contract.paidSilver}");
                r.Check(contract.TotalCommitment < contract.dailyWage * contract.termDays,
                    "prepaying costs less than the gross rate (§37)");
                r.Check(contract.status == EmploymentStatus.Travelling,
                    "contract starts as travelling", contract.status.ToString());
                r.Check(contract.pawn != null && !contract.pawn.Spawned,
                    "worker is not on the map yet");
                r.Check(contract.pawn != null && Find.WorldPawns.Contains(contract.pawn),
                    "worker is parked in the world pawn pool so nothing collects them");

                // --- Arrival ---
                contract.arrivalTick = GenTicks.TicksGame;
                EmploymentService.Advance(state.Employments);

                Pawn worker = contract.pawn;
                r.Check(contract.status == EmploymentStatus.Active,
                    "contract went active on arrival", contract.status.ToString());
                r.Check(worker != null && worker.Spawned, "worker is spawned on the map");

                if (worker == null || !worker.Spawned)
                {
                    return Summarize(r);
                }

                r.Check(worker.Faction == Faction.OfPlayer, "worker is in the player faction");
                r.Check(worker.IsFreeColonist, "worker is a free colonist (this is what makes them usable)");
                r.Check(worker.IsQuestLodger(), "worker is a quest lodger");
                r.Check(worker.kindDef == originalKind, "kindDef survived the transfer",
                    $"{originalKind?.defName} -> {worker.kindDef?.defName}");
                r.Check(worker.HomeFaction == employer, "home faction is still the employer",
                    $"{worker.HomeFaction?.Name ?? "none"} vs {employer?.Name ?? "none"}");
                r.Check(worker.workSettings != null, "work priorities are assignable");
                r.Check(worker.drafter != null, "worker is draftable");
                r.Check(contract.endTick > GenTicks.TicksGame,
                    "term clock starts at arrival, not at hire",
                    $"{(contract.endTick - GenTicks.TicksGame) / (float)GenDate.TicksPerDay:0.#}d remaining");

                // Informational: the storyteller exclusion the lodger route buys. Threat points
                // move with wealth too, so this is reported rather than asserted — the binding
                // evidence is the lodger flag above, which DefaultThreatPointsNow tests directly.
                r.Info($"threat points now: {StorytellerUtility.DefaultThreatPointsNow(map):0}");

                // --- Caravan eligibility (§33 q9) ---
                // Asserted against the list the caravan dialog actually builds, not against
                // IsFreeColonist. The Phase 15 spike checked IsFreeColonist and reported "caravan
                // eligible: yes" while vanilla was in fact filtering employees out for being
                // quest lodgers, and it took playtesting to notice.
                r.Check(Dialog_FormCaravan.AllSendablePawns(map, reform: false).Contains(worker),
                    "an employee can be loaded onto a caravan (§33 q9)");

                // --- Term expiring while the worker is away from any map ---
                // Simulated by despawning: that is exactly the state a pawn is in while inside a
                // caravan, and it is the condition Advance tests.
                IntVec3 restoreCell = worker.Position;
                worker.DeSpawn();
                contract.endTick = GenTicks.TicksGame;
                EmploymentService.Advance(state.Employments);

                r.Check(contract.status == EmploymentStatus.Active,
                    "an expired term is held, not ended, while the worker is off-map",
                    contract.status.ToString());
                r.Check(contract.termLapsedNotified, "the player is told the term lapsed while away");

                GenSpawn.Spawn(worker, restoreCell, map);

                bool allMainEquipmentCarried = false;
                bool damageApplied = false;
                int hitPointsBefore = -1;
                int hitPointsAfter = -1;
                int silverBeforeEquipmentSettlement = 0;
                if (mainFixtureBuilt)
                {
                    int totalMainQuantity;
                    int matchedMainQuantity = CountMatchedEquipment(
                        worker, contract.arrivedEquipment, out totalMainQuantity);
                    allMainEquipmentCarried = totalMainQuantity > 0 &&
                        matchedMainQuantity == totalMainQuantity;

                    Thing damagedEquipment = mainFixtureItems.Count > 0
                        ? mainFixtureItems[0]
                        : null;
                    hitPointsBefore = damagedEquipment == null ? -1 : damagedEquipment.HitPoints;
                    if (damagedEquipment != null && !damagedEquipment.Destroyed)
                    {
                        // Vanilla's own apparel wear-out path uses this same call:
                        // reference/decompiled/RimWorld/Pawn_ApparelTracker.cs:412.
                        damagedEquipment.TakeDamage(
                            new DamageInfo(DamageDefOf.Deterioration, 1f));
                    }

                    hitPointsAfter = damagedEquipment == null || damagedEquipment.Destroyed
                        ? -1
                        : damagedEquipment.HitPoints;
                    damageApplied = hitPointsBefore > 1 && hitPointsAfter == hitPointsBefore - 1;
                    silverBeforeEquipmentSettlement = PurchaseOrderService.CountColonySilver(map);
                }

                // --- Expiry and departure ---
                contract.endTick = GenTicks.TicksGame;
                EmploymentService.Advance(state.Employments);

                r.Check(contract.status == EmploymentStatus.Completed,
                    "contract completed when the term ran out", contract.status.ToString());
                r.Check(worker.Faction == employer, "faction restored to the employer",
                    $"{worker.Faction?.Name ?? "none"}");
                r.Check(!worker.IsColonist, "worker is no longer a colonist");
                r.Check(worker.kindDef == originalKind, "kindDef intact after departure",
                    $"{originalKind?.defName} -> {worker.kindDef?.defName}");
                r.Check(worker.GetLord() != null, "worker is walking off the map");
                r.Check(contract.pawn == null && contract.quest == null,
                    "closed record holds no live references (nothing to dangle on load)");

                if (mainFixtureBuilt)
                {
                    int silverAfterEquipmentSettlement =
                        PurchaseOrderService.CountColonySilver(map);
                    int equipmentRefund =
                        silverAfterEquipmentSettlement - silverBeforeEquipmentSettlement;
                    r.Check(allMainEquipmentCarried && contract.equipmentBondSettled &&
                            equipmentRefund == contract.equipmentBond,
                        "E2 everything returned is everything refunded",
                        $"all recorded gear carried before end {allMainEquipmentCarried}, " +
                        $"bond {contract.equipmentBond:0.###}, refund {equipmentRefund:0.###}, " +
                        $"silver {silverBeforeEquipmentSettlement:0.###} -> " +
                        $"{silverAfterEquipmentSettlement:0.###}");
                    r.Check(damageApplied && contract.equipmentBondSettled &&
                            equipmentRefund == contract.equipmentBond,
                        "E5 normal wear still refunds in full",
                        $"hit points {hitPointsBefore:0.###} -> {hitPointsAfter:0.###}, " +
                        $"bond {contract.equipmentBond:0.###}, refund {equipmentRefund:0.###}");
                }

                // --- Dismissal before arrival ---
                CheckEarlyDismissal(r, state, map, fixturePawns);
                CheckDeadEmployeeCorpse(
                    r, state, map, fixturePawns, fixtureCorpses, fixtureGraves);
                CheckPartialEquipmentBond(r, state, map, fixturePawns, fixtureItems);

                r.sb.AppendLine();
                r.sb.AppendLine("  Not covered here — check by hand:");
                r.sb.AppendLine("    * save mid-employment, quit to menu, reload, confirm the worker is still");
                r.sb.AppendLine("      employed, still a lodger, and the term clock did not reset (§61, §82);");
                r.sb.AppendLine("    * that the worker actually hauls, cooks and sleeps over several days.");
            }
            catch (System.Exception ex)
            {
                r.sb.AppendLine($"  EXCEPTION: {ex}");
                r.failed++;
            }
            finally
            {
                try
                {
                    CleanupAddedEmployments(r, state, savedEmployments);

                    savedStandingOwner?.Adjust(savedStanding - savedStandingOwner.Score);
                    while (state.Ledger.Count > savedLedger)
                    {
                        state.Ledger.RemoveAt(state.Ledger.Count - 1);
                    }

                    state.LedgerStartTick = savedLedgerStartTick;
                    LaborCandidateService.Clear();

                    // A grave owns its corpse, and the corpse owns the dead pawn by reference. Tear
                    // down in that order so the final pawn cleanup cannot empty a live fixture corpse.
                    foreach (Building_Grave fixtureGrave in fixtureGraves)
                    {
                        CleanupFixtureGrave(r, fixtureGrave);
                    }

                    foreach (Corpse fixtureCorpse in fixtureCorpses)
                    {
                        CleanupFixtureCorpse(r, fixtureCorpse);
                    }

                    foreach (Pawn fixturePawn in fixturePawns)
                    {
                        CleanupFixturePawn(r, fixturePawn);
                    }

                    foreach (Thing fixtureItem in fixtureItems)
                    {
                        CleanupFixtureItem(r, fixtureItem);
                    }

                    int returned =
                        IntercolonyLaborSelfTestSupport.RestoreStorageSilver(map, savedSilver);
                    if (returned > 0)
                    {
                        r.Info($"returned {returned} silver to restore the labor fixture.");
                    }

                    IntercolonyLaborSelfTestSupport.ResetLedger();

                    int worldPawnsAfter = Find.WorldPawns?.AllPawnsAliveOrDead?.Count ?? 0;
                    r.Check(worldPawnsAfter <= worldPawnsBefore,
                        "no world pawns leaked by the labor fixtures",
                        $"{worldPawnsBefore:0.###} before, {worldPawnsAfter:0.###} after");
                }
                finally
                {
                    if (Find.TickManager != null && Find.TickManager.TicksGame != savedTick)
                    {
                        Find.TickManager.DebugSetTicksGame(savedTick);
                        r.Info($"restored the game tick to {savedTick} after the labor fixtures.");
                    }
                }
            }

            return Summarize(r);
        }

        /// <summary>Wage rules that must hold regardless of which worker was rolled.</summary>
        private static void CheckPricing(Results r, IntercolonyWorldComponent state, List<LaborCandidate> pool)
        {
            CheckTravelBounds(r, state, pool);
            int positive = 0;
            int longerIsCheaperPerDay = 0;
            int sampled = 0;

            foreach (LaborCandidate candidate in pool)
            {
                if (candidate.dailyWage > 0)
                {
                    positive++;
                }

                SettlementEconomicProfile profile =
                    state.GetProfile(IntercolonyMarketAccess.FindSettlement(candidate.settlementId));

                float standing = EmployerReputationService.ScoreFor(state);
                int shortTerm = LaborCandidateService.DailyWage(
                    candidate.pawn, profile, candidate.distanceTiles, 3, standing,
                    CombatClause.Civilian);
                int longTerm = LaborCandidateService.DailyWage(
                    candidate.pawn, profile, candidate.distanceTiles, 30, standing,
                    CombatClause.Civilian);
                sampled++;
                if (longTerm <= shortTerm)
                {
                    longerIsCheaperPerDay++;
                }
            }

            r.Check(positive == pool.Count, "every quoted wage is positive",
                $"{positive}/{pool.Count}");
            r.Check(longerIsCheaperPerDay == sampled,
                "a longer term never costs more per day (§36.1)",
                $"{longerIsCheaperPerDay}/{sampled} sampled");
            r.Check(pool.TrueForAll(c => c.minTermDays > 0), "every candidate has a minimum term");
            r.Check(pool.TrueForAll(c => c.pawn != null && c.pawn.RaceProps.Humanlike),
                "every candidate is a humanlike pawn");
        }

        /// <summary>
        /// Drives the ordinary candidate refresh with the same source settlement at both distance
        /// extremes. The source is already in the pool, so the seeded refresh will include it again
        /// after its tile is moved temporarily; restoring the tile and refreshing in finally leaves
        /// the live market as it was. The expected days are literals rather than a second call to
        /// the production conversion.
        /// </summary>
        private static void CheckTravelBounds(
            Results r, IntercolonyWorldComponent state, List<LaborCandidate> pool)
        {
            Settlement source = null;
            PlanetTile originalTile = PlanetTile.Invalid;
            bool savedOriginalTile = false;

            try
            {
                if (state == null || pool == null || Find.WorldGrid == null ||
                    Find.WorldObjects == null || Find.AnyPlayerHomeMap == null)
                {
                    string failureDetail =
                        "chosen-distance candidate fixture needs a world, world grid, and home map";
                    r.Check(false, "every candidate's travel time respects the 1-day lower bound", failureDetail);
                    r.Check(false, "every candidate's travel time is bounded to 1-20 days", failureDetail);
                    return;
                }

                foreach (LaborCandidate candidate in pool)
                {
                    source = candidate == null
                        ? null
                        : IntercolonyMarketAccess.FindSettlement(candidate.settlementId);
                    if (source != null)
                    {
                        break;
                    }
                }

                if (source == null)
                {
                    string failureDetail = "chosen-distance candidate fixture found no live source settlement";
                    r.Check(false, "every candidate's travel time respects the 1-day lower bound", failureDetail);
                    r.Check(false, "every candidate's travel time is bounded to 1-20 days", failureDetail);
                    return;
                }

                SettlementEconomicProfile profile = state.GetProfile(source);
                if (profile == null)
                {
                    string failureDetail = $"chosen-distance candidate fixture could not profile {source.Label}";
                    r.Check(false, "every candidate's travel time respects the 1-day lower bound", failureDetail);
                    r.Check(false, "every candidate's travel time is bounded to 1-20 days", failureDetail);
                    return;
                }

                PlanetTile homeTile = Find.AnyPlayerHomeMap.Tile;
                PlanetTile farTile = FindFarthestTravelFixtureTile(
                    homeTile, out float farthestDistance);
                if (!farTile.Valid)
                {
                    string failureDetail = "chosen-distance candidate fixture found no world tile";
                    r.Check(false, "every candidate's travel time respects the 1-day lower bound", failureDetail);
                    r.Check(false, "every candidate's travel time is bounded to 1-20 days", failureDetail);
                    return;
                }

                originalTile = source.Tile;
                savedOriginalTile = true;

                source.Tile = homeTile;
                List<LaborCandidate> nearPool = LaborCandidateService.Refresh(state, force: true);
                LaborCandidate near = FindCandidateFromSource(nearPool, source.ID);

                int nearTravelDays = near?.travelDays ?? -1;
                float nearDistance = near?.distanceTiles ?? float.NaN;

                source.Tile = farTile;
                List<LaborCandidate> farPool = LaborCandidateService.Refresh(state, force: true);
                LaborCandidate far = FindCandidateFromSource(farPool, source.ID);

                int farTravelDays = far?.travelDays ?? -1;
                float farDistance = far?.distanceTiles ?? float.NaN;
                // Independent oracle: keep the expected distance arithmetic literal here instead
                // of calling the production travel conversion.
                int nearUnclampedTravelDays = near == null
                    ? -1
                    : Mathf.RoundToInt(nearDistance / 12f);
                int farUnclampedTravelDays = Mathf.RoundToInt(farthestDistance / 12f);
                bool listedCandidatesHaveLowerBound = pool.TrueForAll(candidate =>
                    candidate != null && candidate.travelDays >= 1);
                bool nearLowerBound = near != null && nearDistance < 6f &&
                    nearUnclampedTravelDays == 0 && nearTravelDays == 1;

                string detail =
                    $"near fixture {nearDistance:0.###} tiles -> {nearTravelDays}d (expected 1d; " +
                    $"literal unclamped arithmetic gives {nearUnclampedTravelDays}d); " +
                    $"farthest world tile {farthestDistance:0.###} tiles; " +
                    $"far fixture {farDistance:0.###} tiles -> {farTravelDays}d (literal unclamped " +
                    $"arithmetic gives {farUnclampedTravelDays}d); " +
                    $"listed candidates lower-bounded {listedCandidatesHaveLowerBound}";
                r.Check(listedCandidatesHaveLowerBound && nearLowerBound,
                    "every candidate's travel time respects the 1-day lower bound", detail);

                if (farUnclampedTravelDays <= 20)
                {
                    r.Skip("every candidate's travel time is bounded to 1-20 days",
                        $"upper-bound check skipped: farthest world tile is {farthestDistance:0.###} " +
                        $"tiles from the colony; literal unclamped travel is " +
                        $"{farUnclampedTravelDays} days, so it does not exceed the 20-day ceiling.");
                    return;
                }

                bool listedCandidatesBounded = farPool != null && farPool.TrueForAll(candidate =>
                    candidate != null && candidate.travelDays >= 1 && candidate.travelDays <= 20);
                bool upperBound = far != null &&
                    Mathf.Abs(farDistance - farthestDistance) < 0.01f && farTravelDays == 20 &&
                    listedCandidatesBounded;
                r.Check(upperBound,
                    "every candidate's travel time is bounded to 1-20 days",
                    detail + $"; listed candidates bounded {listedCandidatesBounded}");
            }
            catch (Exception ex)
            {
                string detail =
                    $"chosen-distance candidate fixture threw {ex.GetType().Name}: {ex.Message}";
                r.Check(false, "every candidate's travel time respects the 1-day lower bound", detail);
                r.Check(false, "every candidate's travel time is bounded to 1-20 days", detail);
            }
            finally
            {
                if (source != null && savedOriginalTile)
                {
                    source.Tile = originalTile;
                    LaborCandidateService.Refresh(state, force: true);
                }
            }
        }

        private static PlanetTile FindFarthestTravelFixtureTile(
            PlanetTile homeTile, out float farthestDistance)
        {
            PlanetTile farthestTile = PlanetTile.Invalid;
            farthestDistance = -1f;
            for (int tileId = 0; tileId < Find.WorldGrid.TilesCount; tileId++)
            {
                PlanetTile tile = new PlanetTile(tileId);
                float distance = Find.WorldGrid.ApproxDistanceInTiles(homeTile, tile);
                if (!farthestTile.Valid || distance > farthestDistance)
                {
                    farthestTile = tile;
                    farthestDistance = distance;
                }
            }

            return farthestTile;
        }

        private static LaborCandidate FindCandidateFromSource(
            List<LaborCandidate> candidates, int settlementId)
        {
            if (candidates == null)
            {
                return null;
            }

            foreach (LaborCandidate candidate in candidates)
            {
                if (candidate != null && candidate.settlementId == settlementId)
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// F24's emergency mode is deliberately a direct-hire slice: the ordinary listing is
        /// filtered, the same candidate is priced with an independent premium, and the existing
        /// arrival tick is shortened. This stays next to F23's bond assertion because both checks
        /// drive the real employment transaction rather than a private copy of it.
        /// </summary>
        private static void CheckEmergencyDispatch(
            Results r, IntercolonyWorldComponent state, Map map, List<Pawn> fixturePawns,
            EmploymentContract ordinaryContract,
            List<LaborCandidate> ordinaryPoolSnapshot,
            List<LaborCandidate> emergencyPoolSnapshot)
        {
            int savedSilver = PurchaseOrderService.CountColonySilver(map);
            int savedEmployments = state.Employments.Count;
            int savedLedger = state.Ledger.Count;
            int savedLedgerStartTick = state.LedgerStartTick;
            EmployerReputation savedStandingOwner = state.EmployerStanding;
            float savedStanding = savedStandingOwner?.Score ?? 0f;

            try
            {
                r.Check(IntercolonyWorldComponent.CurrentSaveVersion == ExpectedCurrentSaveVersion,
                    "U4 CurrentSaveVersion remains 58",
                    $"expected 58, actual {IntercolonyWorldComponent.CurrentSaveVersion}");

                // Capture U1 before the main F23 hire consumes one candidate; otherwise the exact
                // nearest-half comparison would be measuring a changed market. U2-U4 deliberately
                // refresh the live remaining listing.
                List<LaborCandidate> ordinaryPoolForU1 =
                    ordinaryPoolSnapshot == null
                        ? new List<LaborCandidate>(LaborCandidateService.Refresh(state))
                        : new List<LaborCandidate>(ordinaryPoolSnapshot);
                List<LaborCandidate> emergencyPoolForU1 =
                    emergencyPoolSnapshot == null
                        ? new List<LaborCandidate>(ordinaryPoolForU1)
                        : new List<LaborCandidate>(emergencyPoolSnapshot);
                if (emergencyPoolSnapshot == null)
                {
                    emergencyPoolForU1.RemoveAll(candidate =>
                        !LaborCandidateService.CanReachEmergency(candidate));
                }

                List<LaborCandidate> ordinaryPool =
                    new List<LaborCandidate>(LaborCandidateService.Refresh(state));
                List<LaborCandidate> emergencyPool =
                    new List<LaborCandidate>(ordinaryPool);
                emergencyPool.RemoveAll(candidate =>
                    !LaborCandidateService.CanReachEmergency(candidate));

                CheckEmergencyUiFilter(r);

                List<LaborCandidate> expectedEmergencyPool =
                    ExpectedEmergencyPool(ordinaryPoolForU1);
                bool ordinaryHasCandidate = expectedEmergencyPool.Count > 0;
                bool emergencyCandidateSetsDiffer =
                    !SameCandidateSet(emergencyPoolForU1, ordinaryPoolForU1);
                bool exactNearestHalf =
                    emergencyPoolForU1.Count == expectedEmergencyPool.Count &&
                    SameCandidateSet(emergencyPoolForU1, expectedEmergencyPool);
                bool strictWhenCandidatesDiffer =
                    !emergencyCandidateSetsDiffer ||
                    emergencyPoolForU1.Count < ordinaryPoolForU1.Count;
                bool nonEmptyWhenCandidateExists =
                    !ordinaryHasCandidate || emergencyPoolForU1.Count > 0;
                r.Check(exactNearestHalf && strictWhenCandidatesDiffer &&
                        nonEmptyWhenCandidateExists,
                    "U1 emergency pool is exactly the nearest half",
                    $"ordinary {ordinaryPoolForU1.Count} " +
                    $"[{CandidateTravelDaysDetail(ordinaryPoolForU1)}], expected nearest " +
                    $"{expectedEmergencyPool.Count} [{CandidateTravelDaysDetail(expectedEmergencyPool)}], " +
                    $"emergency {emergencyPoolForU1.Count} " +
                    $"[{CandidateTravelDaysDetail(emergencyPoolForU1)}], exact {exactNearestHalf}, " +
                    $"strict when different {strictWhenCandidatesDiffer}, " +
                    $"non-empty when available {nonEmptyWhenCandidateExists}");

                LaborCandidate emergencyCandidate =
                    FindEmergencyFixtureCandidate(emergencyPool, requireLongerTravel: true);
                bool hasLongEmergencyFixture = emergencyCandidate != null;
                if (!hasLongEmergencyFixture)
                {
                    // U2 and U4 do not require the emergency arrival to be shorter than ordinary
                    // travel. U3 reports its own named fixture skip because that precondition is
                    // specific to the arrival assertion.
                    emergencyCandidate =
                        FindEmergencyFixtureCandidate(emergencyPool, requireLongerTravel: false);
                }

                if (emergencyCandidate == null)
                {
                    string reason =
                        "no emergency candidate was available in the current direct-hire market; " +
                        $"travel days found [{CandidateTravelDaysDetail(ordinaryPoolForU1)}]";
                    r.Skip("U2 emergency dispatch wage premium", reason);
                    r.Skip("U3 emergency dispatch arrival is urgent", reason);
                    r.Skip("U4 emergency hire has the ordinary save shape", reason);
                    return;
                }

                EmploymentHireCostQuote emergencyQuote =
                    IntercolonyLaborSelfTestSupport.QuoteHireCost(
                        state, emergencyCandidate, emergencyCandidate.minTermDays,
                        WageStructure.Prepaid, CombatClause.Civilian,
                        out string emergencyQuoteFailure, emergencyDispatch: true);
                if (emergencyQuote == null)
                {
                    string reason = $"emergency quote unavailable: {emergencyQuoteFailure}";
                    r.Skip("U2 emergency dispatch wage premium", reason);
                    r.Skip("U3 emergency dispatch arrival is urgent", reason);
                    r.Skip("U4 emergency hire has the ordinary save shape", reason);
                    return;
                }

                int added = IntercolonyLaborSelfTestSupport.EnsureSilver(
                    map, IntercolonyLaborSelfTestSupport.SilverToEnsure(emergencyQuote));
                if (added > 0)
                {
                    r.Info($"added {added} silver from the emergency hire quote " +
                           $"({emergencyQuote.totalDue}) so F24 could run.");
                }

                int hireTick = GenTicks.TicksGame;
                EmploymentContract emergencyContract = EmploymentService.TryHire(
                    state, emergencyCandidate, emergencyCandidate.minTermDays, map,
                    out string emergencyHireFailure, WageStructure.Prepaid,
                    CombatClause.Civilian, emergencyQuote, emergencyDispatch: true);
                if (emergencyContract != null)
                {
                    TrackPawn(fixturePawns, emergencyContract.pawn);
                }

                int ordinaryWage = emergencyCandidate.dailyWage;
                int expectedEmergencyWage = Mathf.RoundToInt(
                    ordinaryWage * ExpectedEmergencyWageMultiplier);
                int actualEmergencyWage = emergencyContract?.dailyWage ?? -1;
                int wageDelta = actualEmergencyWage < 0
                    ? int.MaxValue
                    : Mathf.Abs(actualEmergencyWage - expectedEmergencyWage);
                r.Check(emergencyContract != null && ordinaryWage > 0 &&
                        wageDelta <= EmergencyWageRoundingTolerance,
                    "U2 emergency dispatch costs the premium",
                    $"ordinary {ordinaryWage}/day, multiplier {ExpectedEmergencyWageMultiplier:0.###}x, " +
                    $"expected {expectedEmergencyWage}/day, actual {actualEmergencyWage}/day, " +
                    $"delta {wageDelta}, tolerance {EmergencyWageRoundingTolerance}");

                int ordinaryArrivalTick = hireTick +
                    emergencyCandidate.travelDays * GenDate.TicksPerDay;
                int expectedEmergencyArrivalDays =
                    EmergencyArrivalDaysForAssertion(emergencyCandidate.travelDays);
                int expectedEmergencyArrivalTick = hireTick +
                    expectedEmergencyArrivalDays * GenDate.TicksPerDay;
                int actualEmergencyArrivalTick = emergencyContract?.arrivalTick ?? -1;
                if (!hasLongEmergencyFixture)
                {
                    r.Skip("U3 emergency dispatch arrives sooner",
                        "no emergency candidate had ordinary travel longer than its " +
                        "one-third emergency arrival; travel days found " +
                        $"[{CandidateTravelDaysDetail(ordinaryPoolForU1)}]");
                }
                else
                {
                    r.Check(emergencyContract != null &&
                            emergencyCandidate.travelDays > expectedEmergencyArrivalDays &&
                            actualEmergencyArrivalTick == expectedEmergencyArrivalTick &&
                            actualEmergencyArrivalTick != ordinaryArrivalTick,
                        "U3 emergency dispatch arrives sooner",
                        $"ordinary travel {emergencyCandidate.travelDays}d, urgent arrival " +
                        $"{expectedEmergencyArrivalDays}d, hire tick {hireTick}, " +
                        $"ordinary arrival tick {ordinaryArrivalTick}, expected urgent tick " +
                        $"{expectedEmergencyArrivalTick}, actual {actualEmergencyArrivalTick}");
                }

                if (ordinaryContract == null)
                {
                    r.Check(false, "U4 emergency hire has the ordinary save shape",
                        "the ordinary baseline contract was null");
                }
                else if (emergencyContract == null)
                {
                    r.Check(false, "U4 emergency hire has the ordinary save shape",
                        $"emergency hire failed: {emergencyHireFailure}; " +
                        $"ordinary baseline contract {ordinaryContract.id} exists");
                }
                else
                {
                    CheckEmergencyContractPersistence(r, ordinaryContract, emergencyContract);
                }
            }
            finally
            {
                CleanupAddedEmployments(r, state, savedEmployments);
                savedStandingOwner?.Adjust(savedStanding - savedStandingOwner.Score);
                while (state.Ledger.Count > savedLedger)
                {
                    state.Ledger.RemoveAt(state.Ledger.Count - 1);
                }

                state.LedgerStartTick = savedLedgerStartTick;
                int returned = IntercolonyLaborSelfTestSupport.RestoreStorageSilver(
                    map, savedSilver);
                if (returned > 0)
                {
                    r.Info($"returned {returned} silver to restore the F24 fixture.");
                }

                Scribe.ForceStop();
            }
        }

        private static void CheckEmergencyUiFilter(Results r)
        {
            // DrawHirePage keeps its filtered copy local and exposes no list-returning seam. Check
            // the compiled method that the player actually uses, including its generated lambda,
            // so deleting the UI filter cannot leave the behavioral copy below green.
            MethodInfo drawHirePage = typeof(MainTabWindow_Intercolony).GetMethod(
                "DrawHirePage", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo removeAll = typeof(List<LaborCandidate>).GetMethod(
                "RemoveAll", new[] { typeof(Predicate<LaborCandidate>) });
            MethodInfo canReachEmergency = typeof(LaborCandidateService).GetMethod(
                "CanReachEmergency", BindingFlags.Static | BindingFlags.Public);

            bool drawCallsRemoveAll = CallsMethod(drawHirePage, removeAll);
            MethodInfo reachabilityCaller = FindMethodCalling(
                typeof(MainTabWindow_Intercolony), canReachEmergency);
            bool uiCallsReachability = reachabilityCaller != null;

            r.Check(drawCallsRemoveAll && uiCallsReachability,
                "U1 labor UI applies the emergency nearest-half filter",
                $"DrawHirePage RemoveAll call {drawCallsRemoveAll}, " +
                $"CanReachEmergency call {uiCallsReachability} in " +
                $"{(reachabilityCaller == null ? "none" :
                    $"{reachabilityCaller.DeclaringType?.Name}.{reachabilityCaller.Name}")}");
        }

        private static MethodInfo FindMethodCalling(Type type, MethodInfo target)
        {
            if (type == null || target == null)
            {
                return null;
            }

            foreach (MethodInfo method in type.GetMethods(
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (CallsMethod(method, target))
                {
                    return method;
                }
            }

            foreach (Type nestedType in type.GetNestedTypes(
                BindingFlags.Public | BindingFlags.NonPublic))
            {
                MethodInfo nestedCaller = FindMethodCalling(nestedType, target);
                if (nestedCaller != null)
                {
                    return nestedCaller;
                }
            }

            return null;
        }

        private static bool CallsMethod(MethodInfo caller, MethodInfo target)
        {
            if (caller == null || target == null)
            {
                return false;
            }

            MethodBody body = caller.GetMethodBody();
            byte[] il = body?.GetILAsByteArray();
            if (il == null)
            {
                return false;
            }

            // call, callvirt, and newobj all carry a four-byte method token. The two
            // methods checked here are emitted as callvirt (List.RemoveAll) and call
            // (CanReachEmergency). Resolve the token instead of relying on compiler-specific
            // method names for the generated lambda.
            for (int i = 0; i + 4 < il.Length; i++)
            {
                if (il[i] != 0x28 && il[i] != 0x6F && il[i] != 0x73)
                {
                    continue;
                }

                int token = il[i + 1] |
                    (il[i + 2] << 8) |
                    (il[i + 3] << 16) |
                    (il[i + 4] << 24);
                MethodBase called;
                try
                {
                    Type[] genericTypeArguments = caller.DeclaringType?.IsGenericType == true
                        ? caller.DeclaringType.GetGenericArguments()
                        : null;
                    Type[] genericMethodArguments = caller.IsGenericMethod
                        ? caller.GetGenericArguments()
                        : null;
                    called = caller.Module.ResolveMethod(
                        token,
                        genericTypeArguments, genericMethodArguments);
                }
                catch (Exception)
                {
                    continue;
                }

                if (called != null && called.Name == target.Name &&
                    SameDeclaringType(called.DeclaringType, target.DeclaringType))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SameDeclaringType(Type actual, Type expected)
        {
            if (actual == expected)
            {
                return true;
            }

            return actual?.IsGenericType == true && expected?.IsGenericType == true &&
                   actual.GetGenericTypeDefinition() == expected.GetGenericTypeDefinition();
        }

        private static LaborCandidate FindEmergencyFixtureCandidate(
            List<LaborCandidate> emergencyPool, bool requireLongerTravel)
        {
            if (emergencyPool == null)
            {
                return null;
            }

            foreach (LaborCandidate candidate in emergencyPool)
            {
                if (candidate?.pawn != null &&
                    (!requireLongerTravel ||
                        EmergencyArrivalDaysForAssertion(candidate.travelDays) <
                        candidate.travelDays))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static int EmergencyArrivalDaysForAssertion(int ordinaryTravelDays)
        {
            return Mathf.Max(1, Mathf.CeilToInt(
                Mathf.Max(0, ordinaryTravelDays) / 3f));
        }

        private static List<LaborCandidate> ExpectedEmergencyPool(
            List<LaborCandidate> ordinaryPool)
        {
            // This is a pre-hire snapshot of candidate references, not a snapshot of each
            // candidate's mutable fields. TryHire calls LaborCandidate.Release(), which nulls the
            // selected candidate's pawn in the same object held by this list. Requiring pawn !=
            // null here would therefore erase a valid snapshot member and change N after the
            // market was captured. Keep the record by its own identity and travel estimate; the
            // display name is diagnostic only and may correctly become "?" after Release().
            List<LaborCandidate> ranked = ordinaryPool == null
                ? new List<LaborCandidate>()
                : new List<LaborCandidate>(ordinaryPool);
            ranked.RemoveAll(candidate => candidate == null || candidate.travelDays < 0);

            // Production ranks equal travel-day candidates by source distance, then by their
            // original market order. The fraction remains an independent assertion oracle.
            ranked.Sort((left, right) =>
            {
                int travelComparison = left.travelDays.CompareTo(right.travelDays);
                if (travelComparison != 0)
                {
                    return travelComparison;
                }

                int distanceComparison = left.distanceTiles.CompareTo(right.distanceTiles);
                if (distanceComparison != 0)
                {
                    return distanceComparison;
                }

                return ordinaryPool.IndexOf(left).CompareTo(ordinaryPool.IndexOf(right));
            });

            // Independent oracle: ceil(N x 0.5), with N taken from the captured ordinary pool.
            // Do not replace this with LaborCandidateService's candidate-count helper.
            int expectedCount = Mathf.CeilToInt(
                ranked.Count * ExpectedEmergencyMarketFraction);
            if (ranked.Count > expectedCount)
            {
                ranked.RemoveRange(expectedCount, ranked.Count - expectedCount);
            }

            return ranked;
        }

        private static bool SameCandidateSet(
            List<LaborCandidate> actual, List<LaborCandidate> expected)
        {
            if (actual == null || expected == null || actual.Count != expected.Count)
            {
                return false;
            }

            foreach (LaborCandidate candidate in actual)
            {
                if (!expected.Contains(candidate))
                {
                    return false;
                }
            }

            foreach (LaborCandidate candidate in expected)
            {
                if (!actual.Contains(candidate))
                {
                    return false;
                }
            }

            return true;
        }

        private static string CandidateTravelDaysDetail(List<LaborCandidate> candidates)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return "none";
            }

            StringBuilder detail = new StringBuilder();
            foreach (LaborCandidate candidate in candidates)
            {
                if (detail.Length > 0)
                {
                    detail.Append(", ");
                }

                string name = candidate == null ? "null" : candidate.Name;
                if (string.IsNullOrEmpty(name))
                {
                    name = "<unnamed>";
                }

                detail.Append(name)
                    .Append('=')
                    .Append(candidate?.travelDays ?? -1)
                    .Append('d');
            }

            return detail.ToString();
        }

        private static void CheckEmergencyContractPersistence(
            Results r, EmploymentContract ordinaryContract, EmploymentContract emergencyContract)
        {
            HashSet<string> ordinaryNodes = PersistedContractNodeNames(
                ordinaryContract, out string ordinaryFailure);
            HashSet<string> emergencyNodes = PersistedContractNodeNames(
                emergencyContract, out string emergencyFailure);
            if (ordinaryNodes == null || emergencyNodes == null)
            {
                r.Check(false, "U4 emergency hire has the ordinary save shape",
                    $"ordinary serializer: {ordinaryFailure ?? "ok"}; " +
                    $"emergency serializer: {emergencyFailure ?? "ok"}");
                return;
            }

            HashSet<string> expectedNodes = ExpectedEmploymentContractNodeNames();
            // The ordinary baseline is the existing F23 fixture hire, which deliberately carries
            // apparel. A generated emergency worker may or may not carry bondable gear, so the
            // presence of these already-known nodes is candidate-dependent, not F24 state. The
            // experience fields can likewise be absent when their default zero values are omitted.
            HashSet<string> candidateDependentNodes = new HashSet<string>(
                new[] { "equipmentBond", "moodSampleTotal", "moodSampleCount" },
                StringComparer.Ordinal);
            HashSet<string> ordinaryOnly = new HashSet<string>(
                ordinaryNodes, StringComparer.Ordinal);
            ordinaryOnly.ExceptWith(emergencyNodes);
            HashSet<string> emergencyOnly = new HashSet<string>(
                emergencyNodes, StringComparer.Ordinal);
            emergencyOnly.ExceptWith(ordinaryNodes);
            HashSet<string> ordinaryShapeOnly = new HashSet<string>(
                ordinaryOnly, StringComparer.Ordinal);
            ordinaryShapeOnly.ExceptWith(candidateDependentNodes);
            HashSet<string> emergencyShapeOnly = new HashSet<string>(
                emergencyOnly, StringComparer.Ordinal);
            emergencyShapeOnly.ExceptWith(candidateDependentNodes);
            HashSet<string> ordinaryUnexpected = new HashSet<string>(
                ordinaryNodes, StringComparer.Ordinal);
            ordinaryUnexpected.ExceptWith(expectedNodes);
            HashSet<string> emergencyUnexpected = new HashSet<string>(
                emergencyNodes, StringComparer.Ordinal);
            emergencyUnexpected.ExceptWith(expectedNodes);
            HashSet<string> unexpectedContractFields =
                UnexpectedEmploymentContractFieldNames(expectedNodes);

            bool sameShape = ordinaryShapeOnly.Count == 0 && emergencyShapeOnly.Count == 0;
            bool noNewPersistedNode = ordinaryUnexpected.Count == 0 &&
                emergencyUnexpected.Count == 0 && unexpectedContractFields.Count == 0;
            r.Check(sameShape && noNewPersistedNode,
                "U4 emergency hire has the ordinary save shape",
                $"ordinary id {ordinaryContract.id}, wage {ordinaryContract.dailyWage}/day, " +
                $"arrival tick {ordinaryContract.arrivalTick}; emergency id " +
                $"{emergencyContract.id}, wage {emergencyContract.dailyWage}/day, arrival tick " +
                $"{emergencyContract.arrivalTick}; ordinary-only [{NodeNamesDetail(ordinaryOnly)}], " +
                $"emergency-only [{NodeNamesDetail(emergencyOnly)}], unexpected ordinary " +
                $"[{NodeNamesDetail(ordinaryUnexpected)}], unexpected emergency " +
                $"[{NodeNamesDetail(emergencyUnexpected)}], candidate-dependent node allowed " +
                $"[{NodeNamesDetail(candidateDependentNodes)}], unexpected contract fields " +
                $"[{NodeNamesDetail(unexpectedContractFields)}]");
        }

        private static HashSet<string> PersistedContractNodeNames(
            EmploymentContract contract, out string failureReason)
        {
            failureReason = null;
            try
            {
                if (contract == null)
                {
                    failureReason = "contract was null";
                    return null;
                }

                if (Scribe.saver == null)
                {
                    failureReason = "vanilla Scribe saver was unavailable";
                    return null;
                }

                string xml = Scribe.saver.DebugOutputFor(contract);
                if (String.IsNullOrEmpty(xml))
                {
                    failureReason = "vanilla Scribe produced no XML";
                    return null;
                }

                XmlDocument document = new XmlDocument();
                document.LoadXml(xml);
                XmlElement root = document.DocumentElement;
                if (root == null)
                {
                    failureReason = "vanilla Scribe XML had no document element";
                    return null;
                }

                HashSet<string> nodes = new HashSet<string>(StringComparer.Ordinal);
                foreach (XmlNode child in root.ChildNodes)
                {
                    if (child.NodeType == XmlNodeType.Element)
                    {
                        nodes.Add(child.Name);
                    }
                }

                return nodes;
            }
            catch (Exception ex)
            {
                failureReason = $"vanilla Scribe XML inspection threw {ex.GetType().Name}: {ex.Message}";
                return null;
            }
            finally
            {
                Scribe.ForceStop();
            }
        }

        private static HashSet<string> ExpectedEmploymentContractNodeNames()
        {
            return new HashSet<string>(
                new[]
                {
                    "id", "settlementId", "settlementName", "factionName",
                    "pawn", "employerFaction", "quest", "destinationMap", "originalKind",
                    "workerName", "workerSkills",
                    "dailyWage", "termDays", "paidSilver", "arrivedEquipment",
                    "equipmentBond", "equipmentBondSettled",
                    "combatClause", "combatIncidents", "clauseBreaches",
                    "countedAttackTick", "lastIncidentTick", "permanentInjuriesOnArrival",
                    "compensationPaid",
                    "wageStructure", "nextPaymentTick", "arrearsSilver", "missedPayments",
                    "refusingWork", "refusalReason", "heldPriorities",
                    "hiredTick", "arrivalTick", "arrivedTick", "noticeEndTick",
                    "renewalOffered", "renewalDeclinedByWorker", "renewalDeclinedByPlayer",
                    "renewalWage", "renewals", "autoRenew",
                    "transitionOffered", "transitionOfferedTick", "transitionResolved", "endTick",
                    "moodSampleTotal", "moodSampleCount",
                    "status", "outcomeNote", "termLapsedNotified", "downedNotified",
                    "safePassage", "safePassageEndTick"
                },
                StringComparer.Ordinal);
        }

        private static HashSet<string> UnexpectedEmploymentContractFieldNames(
            HashSet<string> expectedNodes)
        {
            // Scribe_Values omits a default-valued field unless forceSave is requested. Keep this
            // guard deliberately broader than the emitted-node check: any new contract instance
            // field is a schema decision worth surfacing, even before a non-default value makes
            // it visible in XML.
            HashSet<string> actualFields = new HashSet<string>(StringComparer.Ordinal);
            foreach (FieldInfo field in typeof(EmploymentContract).GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                actualFields.Add(field.Name);
            }

            actualFields.ExceptWith(expectedNodes);
            return actualFields;
        }

        private static string NodeNamesDetail(HashSet<string> nodes)
        {
            if (nodes == null || nodes.Count == 0)
            {
                return "none";
            }

            List<string> ordered = new List<string>(nodes);
            ordered.Sort(StringComparer.Ordinal);
            return String.Join(", ", ordered.ToArray());
        }

        /// <summary>
        /// A hire cancelled before the worker arrives must not leave a pinned pawn behind.
        /// TryHire pins them as KeepForever, which the world pawn GC obeys forever if nothing
        /// unpins them.
        /// </summary>
        private static void CheckEarlyDismissal(
            Results r, IntercolonyWorldComponent state, Map map, List<Pawn> fixturePawns)
        {
            List<LaborCandidate> pool = LaborCandidateService.Refresh(state);
            if (pool.Count == 0)
            {
                r.Info("early-dismissal check skipped: no second candidate available.");
                return;
            }

            LaborCandidate candidate = pool[0];
            EmploymentHireCostQuote hireQuote =
                IntercolonyLaborSelfTestSupport.QuoteHireCost(
                    state, candidate, candidate.minTermDays, WageStructure.Prepaid,
                    CombatClause.Civilian, out string quoteFailReason);
            if (hireQuote == null)
            {
                r.Info($"early-dismissal check skipped: {quoteFailReason}");
                return;
            }

            int total = IntercolonyLaborSelfTestSupport.SilverToEnsure(hireQuote);
            int added = IntercolonyLaborSelfTestSupport.EnsureSilver(map, total);
            if (added > 0)
            {
                r.Info($"added {added} silver so the dismissal hire could run.");
            }

            EmploymentContract contract = EmploymentService.TryHire(
                state, candidate, candidate.minTermDays, map, out string failReason,
                WageStructure.Prepaid, CombatClause.Civilian, hireQuote);
            if (contract == null)
            {
                r.Check(false, "second hire for the dismissal check succeeded", failReason);
                return;
            }

            Pawn worker = contract.pawn;
            TrackPawn(fixturePawns, worker);
            EmploymentService.End(contract, EmploymentStatus.Dismissed, "dismissed by self-test");

            r.Check(contract.status == EmploymentStatus.Dismissed,
                "a travelling worker can be dismissed before arrival");
            r.Check(worker != null && !Find.WorldPawns.Contains(worker),
                "a dismissed traveller is unpinned from the world pawn pool");
            r.Check(contract.pawn == null, "dismissed record holds no pawn reference");
        }

        /// <summary>
        /// A real death after arrival must close the employment without discarding the pawn that
        /// vanilla's corpse still owns by reference. Advance is used for the ending because that
        /// is the hourly game path that notices an active worker's death and calls End(Failed).
        /// </summary>
        private static void CheckDeadEmployeeCorpse(
            Results r, IntercolonyWorldComponent state, Map map,
            List<Pawn> fixturePawns, List<Corpse> fixtureCorpses,
            List<Building_Grave> fixtureGraves)
        {
            List<LaborCandidate> pool = LaborCandidateService.Refresh(state, force: true);
            if (pool.Count == 0)
            {
                r.Skip("dead employee corpse regression", "a forced candidate refresh returned no worker");
                return;
            }

            LaborCandidate candidate = pool[0];
            EmploymentHireCostQuote hireQuote =
                IntercolonyLaborSelfTestSupport.QuoteHireCost(
                    state, candidate, candidate.minTermDays, WageStructure.Prepaid,
                    CombatClause.Civilian, out string quoteFailReason);
            if (hireQuote == null)
            {
                r.Skip("dead employee corpse regression", $"the hire cost could not be quoted: {quoteFailReason}");
                return;
            }

            int added = IntercolonyLaborSelfTestSupport.EnsureSilver(
                map, IntercolonyLaborSelfTestSupport.SilverToEnsure(hireQuote));
            if (added > 0)
            {
                r.Info($"added {added} silver so the dead-employee fixture could run.");
            }

            EmploymentContract contract = EmploymentService.TryHire(
                state, candidate, candidate.minTermDays, map, out string failReason,
                WageStructure.Prepaid, CombatClause.Civilian, hireQuote);
            if (contract == null)
            {
                r.Check(false, "dead-employee fixture hire succeeded", failReason);
                return;
            }

            Pawn worker = contract.pawn;
            TrackPawn(fixturePawns, worker);
            contract.arrivalTick = GenTicks.TicksGame;
            EmploymentService.Advance(state.Employments);

            r.Check(worker != null && worker.Spawned,
                "dead-employee fixture arrived before it was killed",
                worker == null ? "worker reference was null" : $"spawned={worker.Spawned}");
            if (worker == null || !worker.Spawned)
            {
                return;
            }

            // Pawn.Kill(null) is the vanilla death entry point. A spawned pawn takes the real
            // map-corpse branch, which constructs Corpse.InnerPawn and places that corpse.
            worker.Kill(null);
            r.Check(worker.Dead, "vanilla Kill marked the employee dead");

            Corpse corpse = FindCorpseForPawn(map, worker);
            TrackCorpse(fixtureCorpses, corpse);
            r.Check(corpse != null,
                "vanilla Kill created a real corpse for the employee",
                corpse == null ? "no map corpse contains this pawn" : corpse.ToString());
            if (corpse == null)
            {
                return;
            }

            // A grave is useful for exercising the same holder JoyGiver_VisitGrave reads. If the
            // map has no legal cell, the standalone corpse still gives the required container
            // and Scribe assertions below.
            Building_Grave grave = null;
            if (ThingDefOf.Grave == null)
            {
                r.Info("grave fixture skipped: ThingDefOf.Grave was unavailable.");
            }
            else if (!CellFinder.TryFindRandomCell(
                map,
                cell => !cell.Fogged(map) && cell.Standable(map) &&
                        cell.GetFirstBuilding(map) == null &&
                        cell.GetFirstItem(map) == null && cell.GetFirstPawn(map) == null,
                out IntVec3 graveCell))
            {
                r.Info("grave fixture skipped: no empty standable cell was available.");
            }
            else
            {
                grave = ThingMaker.MakeThing(ThingDefOf.Grave) as Building_Grave;
                if (grave == null)
                {
                    r.Info("grave fixture skipped: ThingDefOf.Grave did not make a Building_Grave.");
                }
                else
                {
                    TrackGrave(fixtureGraves, grave);
                    GenSpawn.Spawn(grave, graveCell, map);
                    grave.SetFactionDirect(Faction.OfPlayer);
                    if (corpse.Spawned)
                    {
                        corpse.DeSpawn();
                    }

                    bool accepted = grave.TryAcceptThing(corpse);
                    r.Check(accepted && grave.HasCorpse && ReferenceEquals(grave.Corpse, corpse),
                        "the real corpse was placed in a spawned grave",
                        $"accepted={accepted}, hasCorpse={grave.HasCorpse}");
                    if (!accepted)
                    {
                        r.Info("grave fixture fell back to the standalone corpse for Scribe coverage.");
                    }
                }
            }

            bool wasInWorldBeforeEnd = Find.WorldPawns != null && Find.WorldPawns.Contains(worker);
            r.Check(wasInWorldBeforeEnd,
                "vanilla death put the dead employee in WorldPawns before employment cleanup");

            // This is the real active-death ending path, not a direct call to EmploymentService.End.
            EmploymentService.Advance(state.Employments);

            bool stillInWorld = Find.WorldPawns != null && Find.WorldPawns.Contains(worker);
            r.Check(!worker.Discarded,
                "dead employee is not discarded by game-style employment ending");
            r.Check(stillInWorld,
                "dead employee remains in WorldPawns after game-style employment ending",
                $"inWorld={stillInWorld}");

            ThingOwner corpseContents = corpse.GetDirectlyHeldThings();
            r.Check(ReferenceEquals(corpse.InnerPawn, worker),
                "corpse.InnerPawn is still the dead employee");
            r.Check(corpseContents != null && corpseContents.Count > 0,
                "corpse inner container still contains the dead employee",
                $"contents={corpseContents?.Count ?? -1}");

            Building_Grave graveForRoundTrip =
                grave != null && grave.HasCorpse && ReferenceEquals(grave.Corpse, corpse)
                    ? grave
                    : null;
            CheckVisitGraveValidator(r, map, graveForRoundTrip);
            if (graveForRoundTrip == null && corpse.Spawned)
            {
                corpse.DeSpawn();
            }

            DeadEmployeeCorpseRoundTripProbe loaded =
                RoundTripDeadEmployeeCorpse(worker, corpse, graveForRoundTrip, out string roundTripFailure);
            if (roundTripFailure != null)
            {
                string roundTripShape = graveForRoundTrip == null
                    ? "standalone corpse"
                    : "grave-contained corpse";
                r.Skip($"{roundTripShape} survives a Scribe save/load round trip", roundTripFailure);
            }
            else
            {
                TrackGrave(fixtureGraves, loaded?.grave);
                Corpse loadedCorpse = loaded?.grave?.Corpse ?? loaded?.corpse;
                TrackCorpse(fixtureCorpses, loadedCorpse);
                if (loaded?.worldPawns != null)
                {
                    foreach (Pawn loadedPawn in loaded.worldPawns)
                    {
                        TrackPawn(fixturePawns, loadedPawn);
                    }
                }

                Pawn loadedWorker = loaded?.worldPawns != null && loaded.worldPawns.Count > 0
                    ? loaded.worldPawns[0]
                    : null;
                ThingOwner loadedContents = loadedCorpse?.GetDirectlyHeldThings();
                r.Check(loadedWorker != null && loadedCorpse != null &&
                        ReferenceEquals(loadedCorpse.InnerPawn, loadedWorker) &&
                        loadedContents != null && loadedContents.Count > 0,
                    "corpse reference and inner container survive a Scribe save/load round trip",
                    $"loadedPawn={loadedWorker != null}, loadedCorpse={loadedCorpse != null}, " +
                    $"innerPawnMatch={loadedCorpse != null && ReferenceEquals(loadedCorpse.InnerPawn, loadedWorker)}, " +
                    $"contents={loadedContents?.Count ?? -1}");
            }
        }

        private static void CheckVisitGraveValidator(
            Results r, Map map, Building_Grave grave)
        {
            if (grave == null || !grave.HasCorpse)
            {
                r.Skip("JoyGiver_VisitGrave validator does not throw for the intact grave",
                    "no spawned grave fixture was available");
                return;
            }

            JoyGiverDef visitGraveDef = null;
            foreach (JoyGiverDef joyGiverDef in DefDatabase<JoyGiverDef>.AllDefsListForReading)
            {
                if (joyGiverDef?.giverClass == typeof(JoyGiver_VisitGrave))
                {
                    visitGraveDef = joyGiverDef;
                    break;
                }
            }

            if (visitGraveDef == null)
            {
                r.Skip("JoyGiver_VisitGrave validator does not throw for the intact grave",
                    "vanilla JoyGiverDef was unavailable");
                return;
            }

            Pawn joyPawn = null;
            if (map?.mapPawns?.FreeColonists != null)
            {
                foreach (Pawn colonist in map.mapPawns.FreeColonists)
                {
                    if (colonist != null && colonist.Spawned && !colonist.Dead &&
                        colonist.needs?.joy != null &&
                        !grave.Fogged() &&
                        colonist.CanReserveAndReach(grave, PathEndMode.Touch, Danger.None))
                    {
                        joyPawn = colonist;
                        break;
                    }
                }
            }

            if (joyPawn == null)
            {
                r.Skip("JoyGiver_VisitGrave validator does not throw for the intact grave",
                    "no live colonist could reach the fixture grave");
                return;
            }

            Exception validatorFailure = null;
            try
            {
                // This is the actual vanilla entry point; it evaluates the grave validator before
                // making a job, including the Corpse.InnerPawn.Faction dereference.
                visitGraveDef.Worker.TryGiveJob(joyPawn);
            }
            catch (Exception ex)
            {
                validatorFailure = ex;
            }

            r.Check(validatorFailure == null,
                "JoyGiver_VisitGrave validator did not throw for the intact grave",
                validatorFailure == null
                    ? null
                    : $"{validatorFailure.GetType().Name}: {validatorFailure.Message}");
        }

        private static Corpse FindCorpseForPawn(Map map, Pawn pawn)
        {
            if (map?.listerThings == null || pawn == null)
            {
                return null;
            }

            foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse))
            {
                Corpse corpse = thing as Corpse;
                if (corpse != null && ReferenceEquals(corpse.InnerPawn, pawn))
                {
                    return corpse;
                }
            }

            return null;
        }

        private static DeadEmployeeCorpseRoundTripProbe RoundTripDeadEmployeeCorpse(
            Pawn worker, Corpse corpse, Building_Grave grave, out string failure)
        {
            string path = Path.Combine(
                Path.GetTempPath(), $"Intercolony-DeadEmployeeCorpse-{Guid.NewGuid():N}.xml");
            DeadEmployeeCorpseRoundTripProbe saved = new DeadEmployeeCorpseRoundTripProbe
            {
                worldPawns = new List<Pawn> { worker },
                grave = grave,
                corpse = grave == null ? corpse : null
            };
            DeadEmployeeCorpseRoundTripProbe loaded = null;
            failure = null;

            try
            {
                if (Scribe.saver == null || Scribe.loader == null)
                {
                    failure =
                        "RimWorld Scribe.saver or Scribe.loader was null, so a real temp-file " +
                        "save/load round trip was not reachable in this self-test";
                    return null;
                }

                Scribe.saver.InitSaving(path, "intercolonyDeadEmployeeCorpseTest");
                Scribe_Deep.Look(ref saved, "probe");
                Scribe.saver.FinalizeSaving();

                Scribe.loader.InitLoading(path);
                Scribe_Deep.Look(ref loaded, "probe");
                Scribe.loader.FinalizeLoading();
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                try
                {
                    Scribe.ForceStop();
                }
                catch (Exception ex)
                {
                    if (failure == null)
                    {
                        failure = $"Scribe cleanup {ex.GetType().Name}: {ex.Message}";
                    }
                }

                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch (Exception ex)
                {
                    if (failure == null)
                    {
                        failure = $"temporary XML cleanup {ex.GetType().Name}: {ex.Message}";
                    }
                }
            }

            return loaded;
        }

        private static void CheckPartialEquipmentBond(
            Results r, IntercolonyWorldComponent state, Map map,
            List<Pawn> fixturePawns, List<Thing> fixtureItems)
        {
            List<LaborCandidate> pool = LaborCandidateService.Refresh(state, force: true);
            if (pool.Count == 0)
            {
                SkipPartialEquipmentBondChecks(
                    r, "a forced candidate refresh returned no worker for the second fixture");
                return;
            }

            LaborCandidate candidate = pool[0];
            List<Thing> partialFixtureItems = new List<Thing>();
            bool fixtureBuilt = BuildEquipmentBondFixture(
                candidate, partialFixtureItems, out string fixtureFailureReason);
            fixtureItems.AddRange(partialFixtureItems);
            if (!fixtureBuilt)
            {
                SkipPartialEquipmentBondChecks(r, fixtureFailureReason);
                return;
            }

            EmploymentHireCostQuote hireQuote =
                IntercolonyLaborSelfTestSupport.QuoteHireCost(
                    state, candidate, candidate.minTermDays, WageStructure.Prepaid,
                    CombatClause.Civilian, out string quoteFailReason);
            if (hireQuote == null)
            {
                SkipPartialEquipmentBondChecks(
                    r, $"the hire path could not quote the fixture: {quoteFailReason}");
                return;
            }

            int added = IntercolonyLaborSelfTestSupport.EnsureSilver(
                map, IntercolonyLaborSelfTestSupport.SilverToEnsure(hireQuote));
            if (added > 0)
            {
                r.Info($"added {added} silver so the partial bond hire could run.");
            }

            EmploymentContract contract = EmploymentService.TryHire(
                state, candidate, candidate.minTermDays, map, out string failReason,
                WageStructure.Prepaid, CombatClause.Civilian, hireQuote);
            if (contract == null)
            {
                string detail = failReason ?? "the hire returned no contract";
                r.Check(false, "E3 partial bond fixture hire succeeded", detail);
                r.Check(false, "E4 second bond settlement fixture hire succeeded", detail);
                return;
            }

            TrackPawn(fixturePawns, contract.pawn);
            contract.arrivalTick = GenTicks.TicksGame;
            EmploymentService.Advance(state.Employments);

            Pawn worker = contract.pawn;
            if (contract.status != EmploymentStatus.Active || worker == null || !worker.Spawned)
            {
                string detail =
                    $"status {contract.status}, worker {(worker == null ? "null" : "not spawned")}";
                r.Check(false, "E3 partial bond worker arrived", detail);
                r.Check(false, "E4 second bond settlement worker arrived", detail);
                return;
            }

            // Remove the Normal tuque so the Masterwork parka remains worn and is the returned
            // item in this partial-bond path.
            Apparel removedItem = partialFixtureItems.Count > 1
                ? partialFixtureItems[1] as Apparel
                : null;
            bool removedWasWorn = removedItem != null && worker.apparel != null &&
                worker.apparel.WornApparel.Contains(removedItem);
            if (!removedWasWorn)
            {
                r.Check(false, "E3 partial bond fixture has a removable recorded item");
                r.Check(false, "E4 second bond settlement fixture has a live worker");
                return;
            }

            int totalQuantity;
            int matchedBeforeRemoval = CountMatchedEquipment(
                worker, contract.arrivedEquipment, out totalQuantity);
            worker.apparel.Remove(removedItem);
            int matchedAfterRemoval = CountMatchedEquipment(
                worker, contract.arrivedEquipment, out int ignoredTotalQuantity);

            float independentlyPricedReplacementValue = IndependentReplacementValue(
                partialFixtureItems, out string independentItemDetail);
            int independentlyPricedBond = Mathf.Max(
                0, Mathf.RoundToInt(independentlyPricedReplacementValue * 1.10f));
            int expectedPartialRefund = DescribePartialBond(
                partialFixtureItems, worker, independentlyPricedBond,
                out float totalReplacementValue, out float returnedReplacementValue,
                out string itemDetail);
            Apparel masterworkItem = partialFixtureItems.Count > 0
                ? partialFixtureItems[0] as Apparel
                : null;
            QualityCategory returnedQuality = default(QualityCategory);
            bool returnedMasterwork = ContainsReference(
                    CaptureCarriedEquipmentForTest(worker), masterworkItem) &&
                masterworkItem != null &&
                masterworkItem.TryGetQuality(out returnedQuality) &&
                returnedQuality == QualityCategory.Masterwork;
            int expectedMasterworkRefund = returnedMasterwork && totalReplacementValue > 0f
                ? Mathf.Clamp(
                    Mathf.RoundToInt(
                        independentlyPricedBond * VanillaDefinitionValue(masterworkItem) *
                        Mathf.Max(0, masterworkItem.stackCount) / totalReplacementValue),
                    0, independentlyPricedBond)
                : 0;
            bool returnedSomeButNotAll = matchedAfterRemoval > 0 &&
                matchedAfterRemoval < totalQuantity &&
                returnedReplacementValue > 0f &&
                returnedReplacementValue < totalReplacementValue;

            int silverBeforeFirstSettlement = PurchaseOrderService.CountColonySilver(map);
            EmploymentService.BeginSafePassage(contract, offMap: true);
            int silverAfterFirstSettlement = PurchaseOrderService.CountColonySilver(map);
            int firstRefund = silverAfterFirstSettlement - silverBeforeFirstSettlement;
            string settlementDetail =
                $"items [{itemDetail}], quantities {matchedBeforeRemoval:0.###} -> " +
                $"{matchedAfterRemoval:0.###}/{totalQuantity:0.###}, total value " +
                $"{totalReplacementValue:0.###}, returned value {returnedReplacementValue:0.###}, " +
                $"independent items [{independentItemDetail}], charged bond " +
                $"{contract.equipmentBond:0.###}, independently priced bond " +
                $"{independentlyPricedBond:0.###}, expected returned share " +
                $"{expectedPartialRefund:0.###}, first refund {firstRefund:0.###}";
            r.Check(returnedSomeButNotAll && contract.equipmentBondSettled &&
                    contract.equipmentBond == independentlyPricedBond &&
                    expectedPartialRefund > 0 && expectedPartialRefund < independentlyPricedBond &&
                    firstRefund == expectedPartialRefund,
                "E3 part returned is part refunded, including the premium",
                settlementDetail);
            r.Check(returnedMasterwork && firstRefund == expectedMasterworkRefund,
                "E3 returned Masterwork item refunds its Masterwork-priced bond share",
                $"returned item {masterworkItem?.def?.defName ?? "null"}, quality {returnedQuality}, " +
                $"unit value {VanillaDefinitionValue(masterworkItem):0.###}, total value " +
                $"{totalReplacementValue:0.###}, charged bond {independentlyPricedBond:0.###}, " +
                $"expected Masterwork share {expectedMasterworkRefund:0.###}, " +
                $"first refund {firstRefund:0.###}");

            Pawn workerAfterFirstSettlement = contract.pawn;
            int silverBeforeSecondSettlement = PurchaseOrderService.CountColonySilver(map);
            if (workerAfterFirstSettlement != null && workerAfterFirstSettlement.Spawned)
            {
                workerAfterFirstSettlement.DeSpawn();
            }

            EmploymentService.Advance(state.Employments);
            int silverAfterSecondSettlement = PurchaseOrderService.CountColonySilver(map);
            int secondRefund = silverAfterSecondSettlement - silverBeforeSecondSettlement;
            r.Check(contract.equipmentBondSettled && workerAfterFirstSettlement != null &&
                    contract.pawn == null && secondRefund == 0 &&
                    silverAfterSecondSettlement == silverAfterFirstSettlement,
                "E4 an equipment bond settles once",
                $"first refund {firstRefund:0.###}, second refund {secondRefund:0.###}, " +
                $"silver after first {silverAfterFirstSettlement:0.###}, after second " +
                $"{silverAfterSecondSettlement:0.###}, settled {contract.equipmentBondSettled}");
        }

        private static bool BuildEquipmentBondFixture(
            LaborCandidate candidate, List<Thing> createdItems, out string failureReason)
        {
            failureReason = null;
            if (candidate?.pawn == null)
            {
                failureReason = "candidate pawn was null";
                return false;
            }

            ThingDef parkaDef = ThingDefOf.Apparel_Parka;
            ThingDef tuqueDef = ThingDefOf.Apparel_Tuque;
            ThingDef clothDef = ThingDefOf.Cloth;
            if (parkaDef == null || tuqueDef == null || clothDef == null)
            {
                failureReason = "Core parka, tuque or cloth ThingDef was unavailable";
                return false;
            }

            if (!parkaDef.MadeFromStuff || !tuqueDef.MadeFromStuff || !clothDef.IsStuff)
            {
                failureReason =
                    $"vanilla fixture defs were not stuff-based as expected: " +
                    $"{parkaDef.defName} madeFromStuff={parkaDef.MadeFromStuff}, " +
                    $"{tuqueDef.defName} madeFromStuff={tuqueDef.MadeFromStuff}, " +
                    $"{clothDef.defName} isStuff={clothDef.IsStuff}";
                return false;
            }

            Pawn worker = candidate.pawn;
            if (worker.apparel == null)
            {
                failureReason = "candidate pawn had no apparel tracker";
                return false;
            }

            try
            {
                Apparel parka = ThingMaker.MakeThing(parkaDef, clothDef) as Apparel;
                createdItems.Add(parka);
                Apparel tuque = ThingMaker.MakeThing(tuqueDef, clothDef) as Apparel;
                createdItems.Add(tuque);
                if (parka == null || tuque == null)
                {
                    failureReason = "ThingMaker did not create both apparel instances";
                    return false;
                }

                CompQuality parkaQuality = parka.TryGetComp<CompQuality>();
                if (parkaQuality == null)
                {
                    failureReason = "parka did not instantiate CompQuality";
                    return false;
                }

                parkaQuality.SetQuality(
                    QualityCategory.Masterwork, ArtGenerationContext.Outsider);
                if (parkaQuality.Quality != QualityCategory.Masterwork)
                {
                    failureReason =
                        $"parka quality setter did not apply Masterwork: {parkaQuality.Quality}";
                    return false;
                }

                parka.stackCount = 1;
                tuque.stackCount = 1;
                if (parka.MaxHitPoints <= 1 || tuque.MaxHitPoints <= 1)
                {
                    failureReason =
                        $"fixture apparel did not have enough hit points: " +
                        $"{parka.def.defName} max {parka.MaxHitPoints}, " +
                        $"{tuque.def.defName} max {tuque.MaxHitPoints}";
                    return false;
                }

                // Start at full durability so the vanilla deterioration hit is observable and
                // cannot randomly destroy a fixture before settlement.
                parka.HitPoints = parka.MaxHitPoints;
                tuque.HitPoints = tuque.MaxHitPoints;

                worker.equipment?.DestroyAllEquipment(DestroyMode.Vanish);
                worker.apparel.DestroyAll(DestroyMode.Vanish);
                worker.apparel.Wear(parka, dropReplacedApparel: false);
                worker.apparel.Wear(tuque, dropReplacedApparel: false);
                if (!worker.apparel.WornApparel.Contains(parka) ||
                    !worker.apparel.WornApparel.Contains(tuque))
                {
                    failureReason =
                        $"candidate pawn could not wear both fixture items: " +
                        $"{parka.def.defName} worn={worker.apparel.WornApparel.Contains(parka)}, " +
                        $"{tuque.def.defName} worn={worker.apparel.WornApparel.Contains(tuque)}";
                    return false;
                }

                float parkaValue = VanillaDefinitionValue(parka);
                float tuqueValue = VanillaDefinitionValue(tuque);
                if (parkaValue <= 0f || tuqueValue <= 0f ||
                    float.IsNaN(parkaValue) || float.IsNaN(tuqueValue) ||
                    float.IsInfinity(parkaValue) || float.IsInfinity(tuqueValue))
                {
                    failureReason =
                        $"vanilla fixture values were not positive: " +
                        $"{parka.def.defName} {parkaValue:0.###}, " +
                        $"{tuque.def.defName} {tuqueValue:0.###}";
                    return false;
                }

                return true;
            }
            catch (System.Exception ex)
            {
                failureReason =
                    $"vanilla apparel fixture construction threw {ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        private static void SkipMainEquipmentBondChecks(Results r, string reason)
        {
            string detail =
                $"equipment-bond fixture unavailable: {reason ?? "no reason supplied"}";
            r.Skip("E1 equipment bond charge", detail);
            r.Skip("E2 full equipment bond refund", detail);
            r.Skip("E5 wear does not reduce the equipment bond refund", detail);
        }

        private static void SkipPartialEquipmentBondChecks(Results r, string reason)
        {
            string detail =
                $"partial equipment-bond fixture unavailable: {reason ?? "no reason supplied"}";
            r.Skip("E3 partial equipment bond refund", detail);
            r.Skip("E4 one-time equipment bond settlement", detail);
        }

        private static float IndependentReplacementValue(
            List<Thing> items, out string detail)
        {
            StringBuilder itemDetails = new StringBuilder();
            float total = 0f;
            if (items != null)
            {
                foreach (Thing item in items)
                {
                    int quantity = Mathf.Max(0, item?.stackCount ?? 0);
                    float unitValue = VanillaDefinitionValue(item);
                    float lineValue = unitValue * quantity;
                    total += lineValue;
                    if (itemDetails.Length > 0)
                    {
                        itemDetails.Append("; ");
                    }

                    itemDetails.Append(item?.def?.defName ?? "null")
                        .Append(" x").Append(quantity)
                        .Append($": unit {unitValue:0.###}, value {lineValue:0.###}");
                }
            }

            detail = itemDetails.ToString();
            return total;
        }

        private static int DescribePartialBond(
            List<Thing> fixtureItems, Pawn worker, int bond,
            out float totalValue, out float returnedValue, out string detail)
        {
            totalValue = 0f;
            returnedValue = 0f;
            List<Thing> carried = CaptureCarriedEquipmentForTest(worker);
            if (fixtureItems != null)
            {
                foreach (Thing item in fixtureItems)
                {
                    totalValue += VanillaDefinitionValue(item) *
                        Mathf.Max(0, item?.stackCount ?? 0);
                }
            }

            StringBuilder itemDetails = new StringBuilder();
            if (fixtureItems != null)
            {
                foreach (Thing item in fixtureItems)
                {
                    int quantity = Mathf.Max(0, item?.stackCount ?? 0);
                    float lineValue = VanillaDefinitionValue(item) * quantity;
                    bool returned = ContainsReference(carried, item);
                    if (returned)
                    {
                        returnedValue += lineValue;
                    }

                    if (itemDetails.Length > 0)
                    {
                        itemDetails.Append("; ");
                    }

                    float bondShare = totalValue > 0f
                        ? bond * lineValue / totalValue
                        : 0f;
                    itemDetails.Append(item?.def?.defName ?? "null")
                        .Append(" x").Append(quantity)
                        .Append($": value {lineValue:0.###}, bond share {bondShare:0.###}, ")
                        .Append(returned ? "returned" : "retained");
                }
            }

            detail = itemDetails.ToString();
            if (bond <= 0 || totalValue <= 0f || float.IsNaN(totalValue) ||
                float.IsInfinity(totalValue) || returnedValue <= 0f)
            {
                return 0;
            }

            return Mathf.Clamp(
                Mathf.RoundToInt(bond * returnedValue / totalValue), 0, bond);
        }

        private static float VanillaDefinitionValue(Thing item)
        {
            if (item?.def == null)
            {
                return 0f;
            }

            // Thing.MarketValue is vanilla's instance-aware MarketValue stat, so the assertion
            // includes the actual Thing's quality and remains independent of Intercolony pricing.
            return item.MarketValue;
        }

        private static int CountMatchedEquipment(
            Pawn worker, List<EmploymentEquipmentRecord> records, out int totalQuantity)
        {
            totalQuantity = 0;
            if (records == null)
            {
                return 0;
            }

            List<Thing> carried = CaptureCarriedEquipmentForTest(worker);
            List<int> remaining = new List<int>(carried.Count);
            foreach (Thing item in carried)
            {
                remaining.Add(Mathf.Max(0, item?.stackCount ?? 0));
            }

            int matchedTotal = 0;
            foreach (EmploymentEquipmentRecord record in records)
            {
                int needed = Mathf.Max(0, record?.quantity ?? 0);
                totalQuantity += needed;
                for (int i = 0; i < carried.Count && needed > 0; i++)
                {
                    if (remaining[i] <= 0 || !SameEquipment(record, carried[i]))
                    {
                        continue;
                    }

                    int matched = Mathf.Min(needed, remaining[i]);
                    remaining[i] -= matched;
                    needed -= matched;
                    matchedTotal += matched;
                }
            }

            return matchedTotal;
        }

        private static List<Thing> CaptureCarriedEquipmentForTest(Pawn worker)
        {
            List<Thing> carried = new List<Thing>();
            if (worker?.inventory?.innerContainer != null)
            {
                foreach (Thing item in worker.inventory.innerContainer)
                {
                    carried.Add(item);
                }
            }

            if (worker?.carryTracker?.CarriedThing != null)
            {
                carried.Add(worker.carryTracker.CarriedThing);
            }

            if (worker?.equipment != null)
            {
                foreach (ThingWithComps item in worker.equipment.AllEquipmentListForReading)
                {
                    carried.Add(item);
                }
            }

            if (worker?.apparel != null)
            {
                foreach (Apparel item in worker.apparel.WornApparel)
                {
                    carried.Add(item);
                }
            }

            return carried;
        }

        private static bool SameEquipment(EmploymentEquipmentRecord record, Thing item)
        {
            if (record == null || item == null || item.Destroyed || item.def == null ||
                item.def != record.thingDef || item.Stuff != record.stuffDef)
            {
                return false;
            }

            QualityCategory? quality = null;
            if (item.TryGetQuality(out QualityCategory observedQuality))
            {
                quality = observedQuality;
            }

            return quality == record.quality;
        }

        private static bool ContainsReference(List<Thing> things, Thing target)
        {
            if (things == null || target == null)
            {
                return false;
            }

            foreach (Thing item in things)
            {
                if (ReferenceEquals(item, target))
                {
                    return true;
                }
            }

            return false;
        }

        private static void TrackPawn(List<Pawn> fixturePawns, Pawn pawn)
        {
            if (pawn != null && !fixturePawns.Contains(pawn))
            {
                fixturePawns.Add(pawn);
            }
        }

        private static void TrackCorpse(List<Corpse> fixtureCorpses, Corpse corpse)
        {
            if (corpse != null && !fixtureCorpses.Contains(corpse))
            {
                fixtureCorpses.Add(corpse);
            }
        }

        private static void TrackGrave(List<Building_Grave> fixtureGraves, Building_Grave grave)
        {
            if (grave != null && !fixtureGraves.Contains(grave))
            {
                fixtureGraves.Add(grave);
            }
        }

        private static void CleanupAddedEmployments(
            Results r, IntercolonyWorldComponent state, int savedEmployments)
        {
            for (int i = state.Employments.Count - 1; i >= savedEmployments; i--)
            {
                EmploymentContract contract = state.Employments[i];
                try
                {
                    if (contract?.IsOpen == true)
                    {
                        EmploymentService.End(contract, EmploymentStatus.Failed,
                            "labor self-test cleanup");
                    }

                    if (contract?.status == EmploymentStatus.Severed && contract.pawn != null)
                    {
                        if (contract.pawn.Spawned)
                        {
                            contract.pawn.DeSpawn();
                        }

                        EmploymentService.Advance(state.Employments);
                    }
                }
                catch (System.Exception ex)
                {
                    r.Check(false, "labor fixture contracts clean up",
                        $"{contract?.id.ToString() ?? "unknown"}: {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    state.Employments.RemoveAt(i);
                }
            }
        }

        private static void CleanupFixturePawn(Results r, Pawn pawn)
        {
            try
            {
                if (pawn == null || pawn.Discarded || Find.WorldPawns == null)
                {
                    return;
                }

                if (pawn.Spawned)
                {
                    pawn.DeSpawn();
                }

                if (Find.WorldPawns.Contains(pawn))
                {
                    Find.WorldPawns.RemoveAndDiscardPawnViaGC(pawn);
                }
                else if (!pawn.Discarded)
                {
                    Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.Discard);
                }
            }
            catch (Exception ex)
            {
                r.Check(false, "labor fixture pawn cleans up",
                    $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void CleanupFixtureGrave(Results r, Building_Grave grave)
        {
            if (grave == null || grave.Destroyed)
            {
                return;
            }

            try
            {
                // Destroying the holder first clears its ThingOwner, including a corpse that was
                // buried there. The separate corpse pass below covers an unburied/fallback corpse.
                grave.Destroy(DestroyMode.Vanish);
            }
            catch (Exception ex)
            {
                r.Check(false, "labor fixture grave cleans up",
                    $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void CleanupFixtureCorpse(Results r, Corpse corpse)
        {
            if (corpse == null || corpse.Destroyed)
            {
                return;
            }

            try
            {
                corpse.Destroy(DestroyMode.Vanish);
            }
            catch (Exception ex)
            {
                r.Check(false, "labor fixture corpse cleans up",
                    $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void CleanupFixtureItem(Results r, Thing item)
        {
            try
            {
                if (item != null && !item.Destroyed)
                {
                    item.Destroy(DestroyMode.Vanish);
                }
            }
            catch (Exception ex)
            {
                r.Check(false, "labor fixture item cleans up",
                    $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        public class DeadEmployeeCorpseRoundTripProbe : IExposable
        {
            public List<Pawn> worldPawns;
            public Building_Grave grave;
            public Corpse corpse;

            public void ExposeData()
            {
                // Save the pawn deeply before the corpse's ThingOwner resolves its reference to
                // that pawn on load. This is the same reference shape WorldPawns uses for dead
                // pawns, but in a small temporary XML document rather than a player save.
                Scribe_Collections.Look(ref worldPawns, "worldPawns", true, LookMode.Deep);
                Scribe_Deep.Look(ref grave, "grave");
                Scribe_Deep.Look(ref corpse, "corpse");
            }
        }

        private static string Summarize(Results r)
        {
            r.sb.AppendLine();
            r.sb.AppendLine($"  {r.passed} passed, {r.failed} failed" +
                            (r.skipped == 0 ? "." : $", {r.skipped} skipped."));
            return r.sb.ToString();
        }
    }
}
