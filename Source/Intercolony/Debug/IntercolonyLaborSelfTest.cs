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
        // Independent F24 assertion oracles. Keep wage expectations separate from production
        // calculations so a pricing change must explain what moved instead of moving its own
        // goalposts with the implementation.
        private const float ExpectedEmergencyWageMultiplier = 4f;
        // Keep this schema pin as a literal independent of CurrentSaveVersion: bumping the
        // production constant must fail here until the migration coverage is deliberately reviewed.
        private const int ExpectedCurrentSaveVersion = 60;

        // DailyWageFor rounds once after applying the urgency multiplier, while the ordinary
        // listing exposes its already-rounded daily wage. The independent integer oracle is
        // therefore allowed the two-silver maximum induced by that two-observation rounding gap.
        private const int EmergencyWageRoundingTolerance = 2;
        // The live fixture may not contain a conventional source at both sides of the route
        // cutoff, so the quote checks reuse a real candidate identity at these test distances.
        private const float CloseConventionalFixtureDistanceTiles = 10f;
        private const float DistantConventionalFixtureDistanceTiles = 24f;

        // F23 diversity evidence: 32 is large enough to expose a cloned package while keeping
        // this debug suite well below the hundreds of pawn generations the census deliberately
        // avoids. The civilian sample only checks the no-weapon invariant, so it is smaller.
        private const int EquipmentDiversityApplicantsPerTier = 32;
        private const int EquipmentDiversityCivilianApplicantsPerTier = 8;
        // Four distinct exact signatures and a 75% largest-group ceiling are deliberately broad
        // anti-cloning floors, not predictions of exact RNG aesthetics. D3 raises the structural
        // floor to six across the combined 64 combat applicants and uses the same 75% ceiling.
        private const int EquipmentDiversityMinimumDistinctSignatures = 4;
        private const int EquipmentDiversityMinimumMixedPackages = 2;
        private const int EquipmentDiversityMarketIdentityBase = 0x5F23_0000;

        private const string EquipmentDiversityD1Label =
            "D1 Professional applicants have diverse actual package signatures";
        private const string EquipmentDiversityD2Label =
            "D2 Elite applicants have diverse actual package signatures";
        private const string EquipmentDiversityD3Label =
            "D3 a rich Core-like pool does not clone weapon/apparel signatures";
        private const string EquipmentDiversityD4Label =
            "D4 every accepted diversity applicant meets its promised tier by Classify";
        private const string EquipmentDiversityD5Label =
            "D5 civilian high-tier packages contain no weapon";
        private const string EquipmentDiversityD6Label =
            "D6 valid high-tier packages include mixed item bands";
        private const string EquipmentDiversityD7Label =
            "D7 high-tier packages are not systematically fully best-in-slot";

        private sealed class EmployeeCardFixture
        {
            public readonly string label;
            public readonly EmploymentContract contract;

            public EmployeeCardFixture(string label, EmploymentContract contract)
            {
                this.label = label;
                this.contract = contract;
            }
        }

        private sealed class EmployeeActionObservation
        {
            public Rect rect;
            public string label;
            public bool available;
            public string tooltip;
            public System.Action callback;
        }

        private static List<EmployeeActionObservation> employeeActionObservations;

        private sealed class EquipmentPackageObservation
        {
            public string packageSignature;
            public string weaponApparelSignature;
            public int itemCount;
            public bool hasWeapon;
            public bool hasBasicBand;
            public bool hasProfessionalBand;
            public bool hasEliteBand;
            public bool fullyBestInSlot;
            public LaborEquipmentLevel actualTier;
            public bool tierValid;
        }

        private sealed class EquipmentPackageSample
        {
            public readonly int requested;
            public readonly List<EquipmentPackageObservation> accepted =
                new List<EquipmentPackageObservation>();
            public readonly HashSet<int> prospectIdentities = new HashSet<int>();
            public int prospectIdentityCollisions;
            public int materialiseFailures;
            public int fulfilmentFailures;
            public int exceptionCount;
            public int tierMismatchCount;
            public string firstFailure;

            public EquipmentPackageSample(int requested)
            {
                this.requested = requested;
            }
        }

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
                CheckEmployeeCardLayout(r);
                CheckEmployeeCardLifecycle(r);
                CheckAutoRenewPersistence(r);
                CheckLaborSpineRoundTrip(r);
                CheckEquipmentTierRules(r, map);
                CheckEquipmentPackageDiversity(
                    r, state, fixturePawns, fixtureItems);
                CheckEquipmentBondBuyout(r);
                CheckRefundableBondAgreesWithSettlement(r);
                CheckSettingsDefaultMigration(r);

                // --- Candidate pool ---
                List<LaborCandidate> pool = LaborCandidateService.Refresh(state);
                if (pool.Count == 0)
                {
                    r.Skip(
                        "candidate pool is not empty",
                        "the current world supplied no eligible direct-hire candidate");
                }
                else
                {
                    r.Check(pool.Count > 0, "candidate pool is not empty", $"{pool.Count} workers offered");
                }
                ReportTravelDayDistribution(r, pool);

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
                Dictionary<LaborCandidate, EmergencyArrivalQuote> f24EmergencyQuoteSnapshot =
                    new Dictionary<LaborCandidate, EmergencyArrivalQuote>();
                foreach (LaborCandidate f24Candidate in f24OrdinaryPool)
                {
                    f24EmergencyQuoteSnapshot[f24Candidate] =
                        LaborCandidateService.QuoteEmergencyArrival(f24Candidate);
                }

                // --- Hire ---
                LaborCandidate candidate = pool[0];
                LaborCandidate podEmergencyCandidate = FindEmergencyFixtureCandidate(
                    f24EmergencyPool, requireDropPod: true);
                if (ReferenceEquals(candidate, podEmergencyCandidate) && pool.Count > 1)
                {
                    // Leave a pod-capable emergency fixture available for U3 when the ordinary
                    // listing has another worker the existing F23 path can hire instead.
                    foreach (LaborCandidate alternative in pool)
                    {
                        if (alternative?.pawn != null &&
                            !ReferenceEquals(alternative, podEmergencyCandidate))
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
                    SkipArrivalSafetyChecks(
                        r, $"the existing hired-contract fixture was unavailable: " +
                        $"{failReason ?? "no failure reason supplied"}");
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
                    f24OrdinaryPool, f24EmergencyPool, f24EmergencyQuoteSnapshot);

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
                if (!CheckArrivalSafety(r, state, map, contract))
                {
                    return Summarize(r);
                }

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

        private static void CheckEquipmentTierRules(Results r, Map map)
        {
            const string eliteSupplyLabel =
                "a pre-industrial or poor settlement can never supply Elite";
            const string wildcardSupplyLabel =
                "Any and None are supplyable by every settlement";
            const string deterministicLabel = "the capability gate is deterministic";
            const string emptyLoadoutLabel =
                "a pawn carrying nothing classifies as None for every clause";
            const string wildcardMatchLabel =
                "Any accepts any actual tier, and Any is never an acceptable actual tier";
            const string orderingLabel =
                "a higher actual tier satisfies a lower request, and not the reverse";
            const string headlineLabel =
                "a posting headline names its requirement only when one was asked for";

            List<TechLevel> techLevels = new List<TechLevel>();
            foreach (TechLevel level in Enum.GetValues(typeof(TechLevel)))
            {
                techLevels.Add(level);
            }

            List<IntercolonyWealthTier> wealthLevels =
                new List<IntercolonyWealthTier>();
            foreach (IntercolonyWealthTier level in
                     Enum.GetValues(typeof(IntercolonyWealthTier)))
            {
                wealthLevels.Add(level);
            }

            List<IntercolonyArchetype> archetypes = new List<IntercolonyArchetype>();
            foreach (IntercolonyArchetype archetype in
                     Enum.GetValues(typeof(IntercolonyArchetype)))
            {
                archetypes.Add(archetype);
            }

            List<CombatClause> clauses = new List<CombatClause>();
            foreach (CombatClause clause in Enum.GetValues(typeof(CombatClause)))
            {
                clauses.Add(clause);
            }

            List<LaborEquipmentLevel> equipmentLevels =
                new List<LaborEquipmentLevel>();
            List<LaborEquipmentLevel> realEquipmentLevels =
                new List<LaborEquipmentLevel>();
            foreach (LaborEquipmentLevel level in
                     Enum.GetValues(typeof(LaborEquipmentLevel)))
            {
                equipmentLevels.Add(level);
                if (level != LaborEquipmentLevel.Any)
                {
                    realEquipmentLevels.Add(level);
                }
            }

            // The union is deliberate: the assertion must exercise each hard source gate
            // independently. A pre-industrial but wealthy profile and an
            // industrial-or-better but poor profile can both have a high enough capability score
            // if either gate leaks into the score instead of remaining a gate.
            List<SettlementEconomicProfile> allProfiles =
                new List<SettlementEconomicProfile>();
            List<SettlementEconomicProfile> incapableEliteProfiles =
                new List<SettlementEconomicProfile>();
            List<SettlementEconomicProfile> preSpacerOrPoorProfiles =
                new List<SettlementEconomicProfile>();
            foreach (TechLevel techTier in techLevels)
            {
                foreach (IntercolonyWealthTier wealthTier in wealthLevels)
                {
                    foreach (IntercolonyArchetype archetype in archetypes)
                    {
                        SettlementEconomicProfile profile = new SettlementEconomicProfile
                        {
                            techTier = techTier,
                            wealthTier = wealthTier,
                            archetype = archetype
                        };
                        allProfiles.Add(profile);
                        if (techTier < TechLevel.Industrial ||
                            wealthTier < IntercolonyWealthTier.Comfortable)
                        {
                            incapableEliteProfiles.Add(profile);
                        }

                        if (techTier < TechLevel.Spacer ||
                            wealthTier < IntercolonyWealthTier.Comfortable)
                        {
                            preSpacerOrPoorProfiles.Add(profile);
                        }
                    }
                }
            }

            int supplyCombinationCount =
                preSpacerOrPoorProfiles.Count * clauses.Count;
            int incapableEliteCombinationCount =
                incapableEliteProfiles.Count * clauses.Count;
            int eliteSupplyableCount = 0;
            string firstEliteSupplyable = null;
            foreach (SettlementEconomicProfile profile in incapableEliteProfiles)
            {
                foreach (CombatClause clause in clauses)
                {
                    bool observed = LaborEquipmentTierService.CanSupply(
                        profile, LaborEquipmentLevel.Elite, clause);
                    if (observed)
                    {
                        eliteSupplyableCount++;
                        if (firstEliteSupplyable == null)
                        {
                            firstEliteSupplyable =
                                $"{profile.techTier}/{profile.wealthTier}/" +
                                $"{profile.archetype}/{clause} => {observed}";
                        }
                    }
                }
            }

            r.Check(eliteSupplyableCount == 0, eliteSupplyLabel,
                $"OBSERVED Elite=true {eliteSupplyableCount}; EXPECTED Elite=true 0; " +
                $"examined {incapableEliteCombinationCount} combinations; first true " +
                $"{firstEliteSupplyable ?? "none"}");

            int anyFalseCount = 0;
            int noneFalseCount = 0;
            string firstAnyFalse = null;
            string firstNoneFalse = null;
            foreach (SettlementEconomicProfile profile in preSpacerOrPoorProfiles)
            {
                foreach (CombatClause clause in clauses)
                {
                    bool anyObserved = LaborEquipmentTierService.CanSupply(
                        profile, LaborEquipmentLevel.Any, clause);
                    bool noneObserved = LaborEquipmentTierService.CanSupply(
                        profile, LaborEquipmentLevel.None, clause);
                    if (!anyObserved)
                    {
                        anyFalseCount++;
                        if (firstAnyFalse == null)
                        {
                            firstAnyFalse =
                                $"{profile.techTier}/{profile.wealthTier}/" +
                                $"{profile.archetype}/{clause} => {anyObserved}";
                        }
                    }

                    if (!noneObserved)
                    {
                        noneFalseCount++;
                        if (firstNoneFalse == null)
                        {
                            firstNoneFalse =
                                $"{profile.techTier}/{profile.wealthTier}/" +
                                $"{profile.archetype}/{clause} => {noneObserved}";
                        }
                    }
                }
            }

            r.Check(anyFalseCount == 0 && noneFalseCount == 0, wildcardSupplyLabel,
                $"examined {supplyCombinationCount} combinations; Any true " +
                $"{supplyCombinationCount - anyFalseCount}/{supplyCombinationCount}, " +
                $"None true {supplyCombinationCount - noneFalseCount}/" +
                $"{supplyCombinationCount}; first Any=false " +
                $"{firstAnyFalse ?? "none"}; first None=false " +
                $"{firstNoneFalse ?? "none"}");

            const int deterministicObservationsPerCase = 32;
            int deterministicCaseCount = 0;
            int deterministicCallCount = 0;
            int deterministicMismatchCount = 0;
            string firstDeterministicMismatch = null;
            foreach (SettlementEconomicProfile profile in allProfiles)
            {
                foreach (CombatClause clause in clauses)
                {
                    foreach (LaborEquipmentLevel requested in equipmentLevels)
                    {
                        bool firstObserved = LaborEquipmentTierService.CanSupply(
                            profile, requested, clause);
                        deterministicCallCount++;
                        for (int observation = 1;
                             observation < deterministicObservationsPerCase;
                             observation++)
                        {
                            bool observed = LaborEquipmentTierService.CanSupply(
                                profile, requested, clause);
                            deterministicCallCount++;
                            if (observed != firstObserved)
                            {
                                deterministicMismatchCount++;
                                if (firstDeterministicMismatch == null)
                                {
                                    firstDeterministicMismatch =
                                        $"{profile.techTier}/{profile.wealthTier}/" +
                                        $"{profile.archetype}/{clause}/{requested}: " +
                                        $"first {firstObserved}, observed {observed}";
                                }
                            }
                        }

                        deterministicCaseCount++;
                    }
                }
            }

            r.Check(deterministicMismatchCount == 0, deterministicLabel,
                $"observed {deterministicCaseCount} profile/tier/clause cases, " +
                $"{deterministicCallCount} calls, {deterministicObservationsPerCase} " +
                $"observations per case; mismatches {deterministicMismatchCount}; " +
                $"first mismatch {firstDeterministicMismatch ?? "none"}");

            Pawn emptyLoadoutPawn = null;
            List<ThingWithComps> savedEquipment = new List<ThingWithComps>();
            List<Apparel> savedApparel = new List<Apparel>();
            bool emptyLoadoutPass = false;
            StringBuilder emptyLoadoutDetail = new StringBuilder();
            try
            {
                if (map?.mapPawns?.AllPawnsSpawned != null)
                {
                    foreach (Pawn candidate in map.mapPawns.AllPawnsSpawned)
                    {
                        if (candidate != null && candidate.Spawned && !candidate.Dead &&
                            !candidate.Discarded && candidate.RaceProps != null &&
                            candidate.RaceProps.Humanlike)
                        {
                            emptyLoadoutPawn = candidate;
                            break;
                        }
                    }

                    if (emptyLoadoutPawn == null)
                    {
                        foreach (Pawn candidate in map.mapPawns.AllPawnsSpawned)
                        {
                            if (candidate != null && candidate.Spawned && !candidate.Dead &&
                                !candidate.Discarded)
                            {
                                emptyLoadoutPawn = candidate;
                                break;
                            }
                        }
                    }
                }

                if (emptyLoadoutPawn == null)
                {
                    emptyLoadoutDetail.Append("no live spawned pawn was available on the map");
                }
                else
                {
                    if (emptyLoadoutPawn.equipment?.AllEquipmentListForReading != null)
                    {
                        foreach (ThingWithComps item in
                                 emptyLoadoutPawn.equipment.AllEquipmentListForReading)
                        {
                            if (item != null)
                            {
                                savedEquipment.Add(item);
                            }
                        }
                    }

                    if (emptyLoadoutPawn.apparel?.WornApparel != null)
                    {
                        foreach (Apparel item in emptyLoadoutPawn.apparel.WornApparel)
                        {
                            if (item != null)
                            {
                                savedApparel.Add(item);
                            }
                        }
                    }

                    foreach (ThingWithComps item in savedEquipment)
                    {
                        if (emptyLoadoutPawn.equipment != null &&
                            emptyLoadoutPawn.equipment.Contains(item))
                        {
                            emptyLoadoutPawn.equipment.Remove(item);
                        }
                    }

                    foreach (Apparel item in savedApparel)
                    {
                        if (emptyLoadoutPawn.apparel != null &&
                            emptyLoadoutPawn.apparel.Contains(item))
                        {
                            emptyLoadoutPawn.apparel.Remove(item);
                        }
                    }

                    bool allClausesAreNone = true;
                    StringBuilder observedClauses = new StringBuilder();
                    foreach (CombatClause clause in clauses)
                    {
                        LaborEquipmentLevel observed = LaborEquipmentTierService.Classify(
                            emptyLoadoutPawn, clause);
                        allClausesAreNone &= observed == LaborEquipmentLevel.None;
                        if (observedClauses.Length > 0)
                        {
                            observedClauses.Append(", ");
                        }

                        observedClauses.Append(clause).Append('=').Append(observed);
                    }

                    emptyLoadoutPass = allClausesAreNone;
                    emptyLoadoutDetail.Append($"pawn {emptyLoadoutPawn.LabelShortCap}; ")
                        .Append($"saved equipment {savedEquipment.Count}, apparel " +
                                $"{savedApparel.Count}; after clearing equipment " +
                                $"{emptyLoadoutPawn.equipment?.AllEquipmentListForReading?.Count ?? 0}, " +
                                $"apparel {emptyLoadoutPawn.apparel?.WornApparel?.Count ?? 0}; ")
                        .Append($"observed [{observedClauses}]");
                }
            }
            catch (Exception ex)
            {
                emptyLoadoutPass = false;
                emptyLoadoutDetail.Append($"fixture threw {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                try
                {
                    if (emptyLoadoutPawn?.equipment != null)
                    {
                        foreach (ThingWithComps item in savedEquipment)
                        {
                            if (item != null && !item.Destroyed &&
                                !emptyLoadoutPawn.equipment.Contains(item))
                            {
                                emptyLoadoutPawn.equipment.AddEquipment(item);
                            }
                        }
                    }

                    if (emptyLoadoutPawn?.apparel != null)
                    {
                        foreach (Apparel item in savedApparel)
                        {
                            if (item != null && !item.Destroyed &&
                                !emptyLoadoutPawn.apparel.WornApparel.Contains(item))
                            {
                                emptyLoadoutPawn.apparel.Wear(
                                    item, dropReplacedApparel: false);
                            }
                        }
                    }

                    bool equipmentRestored = emptyLoadoutPawn == null ||
                        emptyLoadoutPawn.equipment == null ||
                        savedEquipment.TrueForAll(item =>
                            item == null || emptyLoadoutPawn.equipment.Contains(item));
                    bool apparelRestored = emptyLoadoutPawn == null ||
                        emptyLoadoutPawn.apparel == null ||
                        savedApparel.TrueForAll(item =>
                            item == null || emptyLoadoutPawn.apparel.WornApparel.Contains(item));
                    if (!equipmentRestored || !apparelRestored)
                    {
                        emptyLoadoutPass = false;
                        if (emptyLoadoutDetail.Length > 0)
                        {
                            emptyLoadoutDetail.Append("; ");
                        }

                        emptyLoadoutDetail.Append(
                            $"restoration equipment={equipmentRestored}, apparel={apparelRestored}");
                    }
                }
                catch (Exception ex)
                {
                    emptyLoadoutPass = false;
                    if (emptyLoadoutDetail.Length > 0)
                    {
                        emptyLoadoutDetail.Append("; ");
                    }

                    emptyLoadoutDetail.Append(
                        $"restoration threw {ex.GetType().Name}: {ex.Message}");
                }
            }

            if (emptyLoadoutPawn == null)
            {
                r.Skip(emptyLoadoutLabel, emptyLoadoutDetail.ToString());
            }
            else
            {
                r.Check(emptyLoadoutPass, emptyLoadoutLabel, emptyLoadoutDetail.ToString());
            }

            int requestWildcardFailureCount = 0;
            int actualWildcardFailureCount = 0;
            StringBuilder wildcardObservations = new StringBuilder();
            foreach (LaborEquipmentLevel actual in equipmentLevels)
            {
                bool observed = LaborEquipmentTierService.MeetsOrExceeds(
                    actual, LaborEquipmentLevel.Any);
                if (!observed)
                {
                    requestWildcardFailureCount++;
                }

                if (wildcardObservations.Length > 0)
                {
                    wildcardObservations.Append(", ");
                }

                wildcardObservations.Append(actual).Append("->Any=").Append(observed);
            }

            foreach (LaborEquipmentLevel requested in realEquipmentLevels)
            {
                bool observed = LaborEquipmentTierService.MeetsOrExceeds(
                    LaborEquipmentLevel.Any, requested);
                if (observed)
                {
                    actualWildcardFailureCount++;
                }

                wildcardObservations.Append(", Any->")
                    .Append(requested).Append('=').Append(observed);
            }

            r.Check(requestWildcardFailureCount == 0 && actualWildcardFailureCount == 0,
                wildcardMatchLabel,
                $"request wildcard observations [{wildcardObservations}]; failures " +
                $"Any-request={requestWildcardFailureCount}, Any-actual=" +
                $"{actualWildcardFailureCount}");

            int orderedPairCount = 0;
            int orderedPairMismatchCount = 0;
            StringBuilder orderedPairObservations = new StringBuilder();
            for (int actualIndex = 0; actualIndex < realEquipmentLevels.Count; actualIndex++)
            {
                for (int requestedIndex = 0;
                     requestedIndex < realEquipmentLevels.Count;
                     requestedIndex++)
                {
                    LaborEquipmentLevel actual = realEquipmentLevels[actualIndex];
                    LaborEquipmentLevel requested = realEquipmentLevels[requestedIndex];
                    bool observed = LaborEquipmentTierService.MeetsOrExceeds(
                        actual, requested);
                    bool expected = actualIndex >= requestedIndex;
                    if (observed != expected)
                    {
                        orderedPairMismatchCount++;
                    }

                    if (orderedPairObservations.Length > 0)
                    {
                        orderedPairObservations.Append(", ");
                    }

                    orderedPairObservations.Append(actual).Append("->")
                        .Append(requested).Append('=').Append(observed);
                    orderedPairCount++;
                }
            }

            r.Check(orderedPairMismatchCount == 0, orderingLabel,
                $"observed {orderedPairCount} ordered pairs; mismatches " +
                $"{orderedPairMismatchCount}; results [{orderedPairObservations}]");

            JobPosting anyPosting = new JobPosting
            {
                termDays = 30,
                wageStructure = WageStructure.Daily,
                combatClause = CombatClause.Civilian,
                requestedEquipmentLevel = LaborEquipmentLevel.Any
            };
            JobPosting professionalPosting = new JobPosting
            {
                termDays = 30,
                wageStructure = WageStructure.Daily,
                combatClause = CombatClause.Civilian,
                requestedEquipmentLevel = LaborEquipmentLevel.Professional
            };
            string anyHeadline = anyPosting.Headline();
            string professionalHeadline = professionalPosting.Headline();
            string professionalShortLabel = LaborEquipmentTierService.ShortLabel(
                LaborEquipmentLevel.Professional);
            string professionalLongLabel = LaborEquipmentTierService.Label(
                LaborEquipmentLevel.Professional);
            bool anyOmitsEquipmentText = anyHeadline.IndexOf(
                "equipment", StringComparison.OrdinalIgnoreCase) < 0;
            bool professionalContainsShortLabel = professionalHeadline.IndexOf(
                professionalShortLabel, StringComparison.Ordinal) >= 0;
            bool professionalOmitsLongLabel = professionalHeadline.IndexOf(
                professionalLongLabel, StringComparison.Ordinal) < 0;
            r.Check(anyOmitsEquipmentText && professionalContainsShortLabel &&
                    professionalOmitsLongLabel, headlineLabel,
                $"Any headline \"{anyHeadline}\" (equipment text absent=" +
                $"{anyOmitsEquipmentText}); Professional headline " +
                $"\"{professionalHeadline}\" (short \"{professionalShortLabel}\" " +
                $"present={professionalContainsShortLabel}, long " +
                $"\"{professionalLongLabel}\" absent={professionalOmitsLongLabel})");

            const string legacyAnyLabel = "Any preserves legacy behavior";
            JobPosting legacyAnyPosting = new JobPosting
            {
                termDays = 30,
                wageStructure = WageStructure.Daily,
                combatClause = CombatClause.Civilian
            };
            int anyCapabilityFailureCount = 0;
            string firstAnyCapabilityFailure = null;
            int anyCapabilityObservationCount = 0;
            foreach (SettlementEconomicProfile profile in allProfiles)
            {
                foreach (CombatClause clause in clauses)
                {
                    bool admitted = LaborEquipmentTierService.CanSupply(
                        profile,
                        legacyAnyPosting.requestedEquipmentLevel,
                        clause);
                    anyCapabilityObservationCount++;
                    if (!admitted)
                    {
                        anyCapabilityFailureCount++;
                        if (firstAnyCapabilityFailure == null)
                        {
                            firstAnyCapabilityFailure =
                                $"{profile.techTier}/{profile.wealthTier}/" +
                                $"{profile.archetype}/{clause} => {admitted}";
                        }
                    }
                }
            }

            string legacyAnyHeadline = legacyAnyPosting.Headline();
            bool legacyAnyOmitsEquipmentText = legacyAnyHeadline.IndexOf(
                "equipment", StringComparison.OrdinalIgnoreCase) < 0;
            bool legacyAnyPreserved =
                legacyAnyPosting.requestedEquipmentLevel == LaborEquipmentLevel.Any &&
                anyCapabilityObservationCount > 0 &&
                anyCapabilityFailureCount == 0 &&
                legacyAnyOmitsEquipmentText;
            r.Check(
                legacyAnyPreserved,
                legacyAnyLabel,
                $"new posting default {legacyAnyPosting.requestedEquipmentLevel}; " +
                $"capability gate admitted {anyCapabilityObservationCount - anyCapabilityFailureCount}/" +
                $"{anyCapabilityObservationCount} profile/clause combinations; " +
                $"headline \"{legacyAnyHeadline}\" (equipment text absent=" +
                $"{legacyAnyOmitsEquipmentText}); first capability failure " +
                $"{firstAnyCapabilityFailure ?? "none"}");
            r.Info(
                "Any applicant materialisation and AddApplicant ownership were not exercised " +
                "because that legacy path necessarily generates a pawn.");
        }

        private static void CheckEquipmentPackageDiversity(
            Results r, IntercolonyWorldComponent state,
            List<Pawn> fixturePawns, List<Thing> fixtureItems)
        {
            if (!TryFindEquipmentDiversitySource(
                    state,
                    out Settlement sourceSettlement,
                    out SettlementEconomicProfile sourceProfile,
                    out int skillCount,
                    out string sourceFailure))
            {
                SkipEquipmentDiversityAssertions(r, sourceFailure);
                return;
            }

            // The catalogue is only a readiness diagnostic. Every D1-D7 observation below is
            // still taken from the instantiated pawn after the production allocator returns.
            int professionalWeaponAlternatives = LaborEquipmentCatalogue.EntriesFor(
                LaborEquipmentCatalogueRole.PrimaryWeapon, TechLevel.Industrial).Count;
            int professionalApparelAlternatives = LaborEquipmentCatalogue.EntriesFor(
                LaborEquipmentCatalogueRole.Apparel, TechLevel.Industrial).Count;
            int eliteWeaponAlternatives = LaborEquipmentCatalogue.EntriesFor(
                LaborEquipmentCatalogueRole.PrimaryWeapon, TechLevel.Spacer).Count;
            int eliteApparelAlternatives = LaborEquipmentCatalogue.EntriesFor(
                LaborEquipmentCatalogueRole.Apparel, TechLevel.Spacer).Count;
            bool professionalAlternatives = professionalWeaponAlternatives >= 2 &&
                professionalApparelAlternatives >= 2;
            bool eliteAlternatives = eliteWeaponAlternatives >= 2 &&
                eliteApparelAlternatives >= 2;

            EquipmentPackageSample professional = new EquipmentPackageSample(
                EquipmentDiversityApplicantsPerTier);
            EquipmentPackageSample elite = new EquipmentPackageSample(
                EquipmentDiversityApplicantsPerTier);
            EquipmentPackageSample civilianProfessional = new EquipmentPackageSample(
                EquipmentDiversityCivilianApplicantsPerTier);
            EquipmentPackageSample civilianElite = new EquipmentPackageSample(
                EquipmentDiversityCivilianApplicantsPerTier);

            string generationFailure = null;
            bool randomStatePushed = false;
            try
            {
                // Pawn materialisation is the same real LaborProspect.Materialise path used by
                // JobPostingService. The pushed state only makes this fixture reproducible; the
                // allocator's own package seed remains the production seed.
                Rand.PushState(Gen.HashCombineInt(
                    sourceProfile.seed, EquipmentDiversityMarketIdentityBase));
                randomStatePushed = true;

                GenerateEquipmentApplicants(
                    sourceSettlement,
                    sourceProfile,
                    LaborEquipmentLevel.Professional,
                    CombatClause.Armed,
                    EquipmentDiversityApplicantsPerTier,
                    EquipmentDiversityMarketIdentityBase +
                        (int)LaborEquipmentLevel.Professional * 10 +
                        (int)CombatClause.Armed,
                    skillCount,
                    0,
                    fixturePawns,
                    fixtureItems,
                    professional);
                GenerateEquipmentApplicants(
                    sourceSettlement,
                    sourceProfile,
                    LaborEquipmentLevel.Elite,
                    CombatClause.Armed,
                    EquipmentDiversityApplicantsPerTier,
                    EquipmentDiversityMarketIdentityBase +
                        (int)LaborEquipmentLevel.Elite * 10 +
                        (int)CombatClause.Armed,
                    skillCount,
                    1000,
                    fixturePawns,
                    fixtureItems,
                    elite);
                GenerateEquipmentApplicants(
                    sourceSettlement,
                    sourceProfile,
                    LaborEquipmentLevel.Professional,
                    CombatClause.Civilian,
                    EquipmentDiversityCivilianApplicantsPerTier,
                    EquipmentDiversityMarketIdentityBase +
                        (int)LaborEquipmentLevel.Professional * 10 +
                        (int)CombatClause.Civilian,
                    skillCount,
                    2000,
                    fixturePawns,
                    fixtureItems,
                    civilianProfessional);
                GenerateEquipmentApplicants(
                    sourceSettlement,
                    sourceProfile,
                    LaborEquipmentLevel.Elite,
                    CombatClause.Civilian,
                    EquipmentDiversityCivilianApplicantsPerTier,
                    EquipmentDiversityMarketIdentityBase +
                        (int)LaborEquipmentLevel.Elite * 10 +
                        (int)CombatClause.Civilian,
                    skillCount,
                    3000,
                    fixturePawns,
                    fixtureItems,
                    civilianElite);
            }
            catch (Exception ex)
            {
                generationFailure =
                    $"equipment diversity fixture threw {ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                if (randomStatePushed)
                {
                    Rand.PopState();
                }
            }

            r.Info(
                $"equipment diversity source {sourceSettlement.Label ?? "unnamed"} " +
                $"({sourceProfile.techTier}/{sourceProfile.wealthTier}/" +
                $"{sourceProfile.archetype}); production samples Professional " +
                $"{professional.accepted.Count}/{professional.requested}, Elite " +
                $"{elite.accepted.Count}/{elite.requested}, civilian " +
                $"{civilianProfessional.accepted.Count}/{civilianProfessional.requested}+" +
                $"{civilianElite.accepted.Count}/{civilianElite.requested}; " +
                $"loaded alternatives P weapon/apparel={professionalWeaponAlternatives}/" +
                $"{professionalApparelAlternatives}, E weapon/apparel=" +
                $"{eliteWeaponAlternatives}/{eliteApparelAlternatives}");

            if (generationFailure != null)
            {
                r.Check(false, EquipmentDiversityD1Label, generationFailure);
                r.Check(false, EquipmentDiversityD2Label, generationFailure);
                r.Check(false, EquipmentDiversityD3Label, generationFailure);
                r.Check(false, EquipmentDiversityD4Label, generationFailure);
                r.Check(false, EquipmentDiversityD5Label, generationFailure);
                r.Check(false, EquipmentDiversityD6Label, generationFailure);
                r.Check(false, EquipmentDiversityD7Label, generationFailure);
                return;
            }

            int professionalDistinct = DistinctEquipmentSignatures(
                professional.accepted, structural: false);
            int professionalLargestGroup = LargestEquipmentSignatureGroup(
                professional.accepted, structural: false);
            int eliteDistinct = DistinctEquipmentSignatures(
                elite.accepted, structural: false);
            int eliteLargestGroup = LargestEquipmentSignatureGroup(
                elite.accepted, structural: false);

            List<EquipmentPackageObservation> highTierObservations =
                new List<EquipmentPackageObservation>();
            highTierObservations.AddRange(professional.accepted);
            highTierObservations.AddRange(elite.accepted);
            int highTierDistinctStructural = DistinctEquipmentSignatures(
                highTierObservations, structural: true);
            int highTierLargestStructuralGroup = LargestEquipmentSignatureGroup(
                highTierObservations, structural: true);
            int highTierValidCount = CountValidEquipmentObservations(highTierObservations);
            int highTierFullyBestInSlot = CountFullyBestInSlotPackages(
                highTierObservations);
            int professionalMixedBands = CountProfessionalMixedBandPackages(
                professional.accepted);
            int eliteProfessionalFiller = CountEliteProfessionalFillerPackages(
                elite.accepted);
            int civilianWeaponViolations = CountCivilianWeaponViolations(
                civilianProfessional.accepted) +
                CountCivilianWeaponViolations(civilianElite.accepted);
            int acceptedCount = professional.accepted.Count + elite.accepted.Count +
                civilianProfessional.accepted.Count + civilianElite.accepted.Count;
            int tierMismatchCount = professional.tierMismatchCount +
                elite.tierMismatchCount + civilianProfessional.tierMismatchCount +
                civilianElite.tierMismatchCount;

            if (!professionalAlternatives)
            {
                r.Skip(
                    EquipmentDiversityD1Label,
                    $"loaded Professional pool had only " +
                    $"{professionalWeaponAlternatives} weapon and " +
                    $"{professionalApparelAlternatives} apparel entries; " +
                    "there were no two alternatives in both required roles");
            }
            else
            {
                // D1 turns red if the allocator is changed to take the deterministic first
                // passing package: this exact-signature count/max-group pair becomes uniform.
                r.Check(
                    professional.accepted.Count >= professional.requested &&
                    professional.prospectIdentityCollisions == 0 &&
                    professionalDistinct >= EquipmentDiversityMinimumDistinctSignatures &&
                    professionalLargestGroup * 4 <= professional.accepted.Count * 3,
                    EquipmentDiversityD1Label,
                    $"accepted={professional.accepted.Count}/{professional.requested}; " +
                    $"distinct signatures={professionalDistinct} (>= " +
                    $"{EquipmentDiversityMinimumDistinctSignatures}); largest exact group=" +
                    $"{professionalLargestGroup}/{professional.accepted.Count} (<=75%); " +
                    DescribeEquipmentSample(professional));
            }

            if (!eliteAlternatives)
            {
                r.Skip(
                    EquipmentDiversityD2Label,
                    $"loaded Elite pool had only {eliteWeaponAlternatives} weapon and " +
                    $"{eliteApparelAlternatives} apparel entries; there were no two " +
                    "alternatives in both required roles");
            }
            else
            {
                // D2 turns red under the same deterministic-first-passing mutation: all Elite
                // applicants then observe the same instantiated package signature.
                r.Check(
                    elite.accepted.Count >= elite.requested &&
                    elite.prospectIdentityCollisions == 0 &&
                    eliteDistinct >= EquipmentDiversityMinimumDistinctSignatures &&
                    eliteLargestGroup * 4 <= elite.accepted.Count * 3,
                    EquipmentDiversityD2Label,
                    $"accepted={elite.accepted.Count}/{elite.requested}; distinct signatures=" +
                    $"{eliteDistinct} (>= {EquipmentDiversityMinimumDistinctSignatures}); " +
                    $"largest exact group={eliteLargestGroup}/{elite.accepted.Count} (<=75%); " +
                    DescribeEquipmentSample(elite));
            }

            if (!professionalAlternatives || !eliteAlternatives)
            {
                r.Skip(
                    EquipmentDiversityD3Label,
                    $"the rich-pool comparison needs both role pools to expose alternatives; " +
                    $"Professional alternatives={professionalAlternatives}, Elite " +
                    $"alternatives={eliteAlternatives}");
            }
            else
            {
                // D3 turns red if deterministic first-passing selection collapses the actual
                // weapon/apparel shape, even if quality-only differences remain elsewhere.
                r.Check(
                    professional.accepted.Count >= professional.requested &&
                    elite.accepted.Count >= elite.requested &&
                    highTierDistinctStructural >=
                        EquipmentDiversityMinimumDistinctSignatures + 2 &&
                    highTierLargestStructuralGroup * 4 <= highTierObservations.Count * 3,
                    EquipmentDiversityD3Label,
                    $"actual high-tier applicants={highTierObservations.Count}; distinct " +
                    $"weapon/apparel signatures={highTierDistinctStructural} (>=6); " +
                    $"largest structural group={highTierLargestStructuralGroup}/" +
                    $"{highTierObservations.Count} (<=75%); source pool is " +
                    $"{sourceProfile.techTier}/{sourceProfile.wealthTier}/" +
                    $"{sourceProfile.archetype}");
            }

            // D4 turns red when a production edit bypasses the final Classify/MeetsOrExceeds
            // guard: the real generated pawn remains under-tier and this observed mismatch is
            // non-zero. The test intentionally calls Classify on the returned pawn again.
            r.Check(
                acceptedCount > 0 && tierMismatchCount == 0,
                EquipmentDiversityD4Label,
                $"accepted={acceptedCount}; under-tier actual loadouts={tierMismatchCount}; " +
                $"Professional [{DescribeEquipmentSample(professional)}]; Elite " +
                $"[{DescribeEquipmentSample(elite)}]; civilian " +
                $"[{DescribeEquipmentSample(civilianProfessional)} / " +
                $"{DescribeEquipmentSample(civilianElite)}]");

            // D5 turns red if the civilian branch starts admitting a generated weapon. This is
            // counted from the pawn's actual equipment tracker, never from a planner's flag.
            r.Check(
                civilianProfessional.accepted.Count >=
                    EquipmentDiversityCivilianApplicantsPerTier &&
                civilianElite.accepted.Count >= EquipmentDiversityCivilianApplicantsPerTier &&
                civilianWeaponViolations == 0,
                EquipmentDiversityD5Label,
                $"civilian accepted Professional={civilianProfessional.accepted.Count}/" +
                $"{civilianProfessional.requested}, Elite={civilianElite.accepted.Count}/" +
                $"{civilianElite.requested}; actual weapon-bearing civilians=" +
                $"{civilianWeaponViolations}");

            if (!professionalAlternatives || !eliteAlternatives)
            {
                r.Skip(
                    EquipmentDiversityD6Label,
                    "mixed-band evidence needs both Professional and Elite alternative pools");
                r.Skip(
                    EquipmentDiversityD7Label,
                    "best-in-slot spread evidence needs both Professional and Elite alternative pools");
            }
            else
            {
                // D6 turns red if the item-band inputs become a rigid recipe (for example, a
                // production edit that disallows Basic Professional filler or Professional Elite
                // filler). Both counters use only valid, instantiated loadouts.
                r.Check(
                    professionalMixedBands >= EquipmentDiversityMinimumMixedPackages &&
                    eliteProfessionalFiller >= EquipmentDiversityMinimumMixedPackages,
                    EquipmentDiversityD6Label,
                    $"valid mixed Professional packages={professionalMixedBands} (>= " +
                    $"{EquipmentDiversityMinimumMixedPackages}); valid Elite packages with " +
                    $"Professional-ish filler={eliteProfessionalFiller} (>= " +
                    $"{EquipmentDiversityMinimumMixedPackages}); bands come from " +
                    "LaborEquipmentItemScore on actual Things");

                // D7 turns red if high-tier selection is made a fully-maximal recipe. The
                // intentionally strong proxy is every actual item in the Elite item band and
                // Excellent-or-better quality; a package promise must not require that.
                r.Check(
                    highTierValidCount >=
                        EquipmentDiversityApplicantsPerTier * 2 &&
                    highTierFullyBestInSlot * 4 <= highTierValidCount * 3,
                    EquipmentDiversityD7Label,
                    $"valid high-tier packages={highTierValidCount}; fully best-in-slot " +
                    $"proxy={highTierFullyBestInSlot} (<=75%); proxy requires every actual " +
                    "item to be Elite-band and Excellent-or-better");
            }
        }

        private static void SkipEquipmentDiversityAssertions(Results r, string reason)
        {
            string detail = reason ?? "no suitable rich Core-like source was available";
            r.Skip(EquipmentDiversityD1Label, detail);
            r.Skip(EquipmentDiversityD2Label, detail);
            r.Skip(EquipmentDiversityD3Label, detail);
            r.Skip(EquipmentDiversityD4Label, detail);
            r.Skip(EquipmentDiversityD5Label, detail);
            r.Skip(EquipmentDiversityD6Label, detail);
            r.Skip(EquipmentDiversityD7Label, detail);
        }

        private static bool TryFindEquipmentDiversitySource(
            IntercolonyWorldComponent state,
            out Settlement sourceSettlement,
            out SettlementEconomicProfile sourceProfile,
            out int skillCount,
            out string failure)
        {
            sourceSettlement = null;
            sourceProfile = null;
            skillCount = 0;
            failure = null;

            if (state == null || Find.WorldPawns == null)
            {
                failure = "the world component or WorldPawns registry was unavailable";
                return false;
            }

            List<SkillDef> skills = DefDatabase<SkillDef>.AllDefsListForReading;
            if (skills != null)
            {
                foreach (SkillDef skill in skills)
                {
                    if (skill != null)
                    {
                        skillCount = Math.Max(skillCount, skill.index + 1);
                    }
                }
            }

            if (skillCount <= 0)
            {
                failure = "the loaded defs exposed no indexed skills for distinct prospects";
                return false;
            }

            List<Settlement> settlements = Find.WorldObjects?.Settlements;
            if (settlements != null)
            {
                foreach (Settlement settlement in settlements)
                {
                    if (settlement?.Faction == null ||
                        settlement.Faction == Faction.OfPlayer)
                    {
                        continue;
                    }

                    SettlementEconomicProfile profile = state.GetProfile(settlement);
                    if (profile == null || profile.techTier != TechLevel.Industrial ||
                        profile.wealthTier < IntercolonyWealthTier.Comfortable ||
                        !LaborEquipmentTierService.CanSupply(
                            profile, LaborEquipmentLevel.Professional, CombatClause.Armed) ||
                        !LaborEquipmentTierService.CanSupply(
                            profile, LaborEquipmentLevel.Elite, CombatClause.Armed) ||
                        !LaborEquipmentTierService.CanSupply(
                            profile, LaborEquipmentLevel.Professional, CombatClause.Civilian) ||
                        !LaborEquipmentTierService.CanSupply(
                            profile, LaborEquipmentLevel.Elite, CombatClause.Civilian))
                    {
                        continue;
                    }

                    PawnKindDef kind = settlement.Faction.RandomPawnKind();
                    if (kind?.RaceProps == null || !kind.RaceProps.Humanlike)
                    {
                        continue;
                    }

                    sourceSettlement = settlement;
                    sourceProfile = profile;
                    return true;
                }
            }

            failure =
                "no non-player Industrial, Comfortable-or-better settlement with a " +
                "humanlike pawn kind could supply both high tiers for Armed and Civilian";
            return false;
        }

        private static void GenerateEquipmentApplicants(
            Settlement sourceSettlement,
            SettlementEconomicProfile sourceProfile,
            LaborEquipmentLevel promisedTier,
            CombatClause clause,
            int applicantCount,
            int marketIdentity,
            int skillCount,
            int ordinalOffset,
            List<Pawn> fixturePawns,
            List<Thing> fixtureItems,
            EquipmentPackageSample sample)
        {
            for (int index = 0; index < applicantCount; index++)
            {
                int ordinal = ordinalOffset + index;
                LaborProspect prospect = BuildEquipmentDiversityProspect(
                    sourceSettlement, promisedTier, skillCount, ordinal);
                int prospectIdentity = LaborEquipmentPackagePlanner.StableProspectIdentity(
                    prospect);
                if (!sample.prospectIdentities.Add(prospectIdentity))
                {
                    // N4: identity is deliberately varied by skills, passions and price; a
                    // collision here means this fixture accidentally recreated the production
                    // limitation it is meant to avoid.
                    sample.prospectIdentityCollisions++;
                }

                Pawn pawn;
                try
                {
                    pawn = prospect.Materialise();
                }
                catch (Exception ex)
                {
                    sample.exceptionCount++;
                    if (sample.firstFailure == null)
                    {
                        sample.firstFailure =
                            $"prospect materialisation threw {ex.GetType().Name}: {ex.Message}";
                    }

                    continue;
                }

                if (pawn == null)
                {
                    sample.materialiseFailures++;
                    if (sample.firstFailure == null)
                    {
                        sample.firstFailure = "prospect.Materialise returned null";
                    }

                    continue;
                }

                TrackPawn(fixturePawns, pawn);
                try
                {
                    string failReason;
                    // This is the production overload. It receives the real census prospect and
                    // one stable posting/market identity, exactly like JobPostingService.Apply.
                    bool fulfilled = LaborEquipmentAllocator.TryFulfil(
                        pawn,
                        prospect,
                        marketIdentity,
                        promisedTier,
                        clause,
                        sourceProfile,
                        out failReason);
                    if (!fulfilled)
                    {
                        sample.fulfilmentFailures++;
                        if (sample.firstFailure == null)
                        {
                            sample.firstFailure = failReason ?? "allocator returned false";
                        }

                        continue;
                    }

                    EquipmentPackageObservation observation = ObserveEquipmentPackage(pawn);
                    observation.actualTier = LaborEquipmentTierService.Classify(
                        pawn, clause);
                    observation.tierValid = LaborEquipmentTierService.MeetsOrExceeds(
                        observation.actualTier, promisedTier);
                    if (!observation.tierValid)
                    {
                        sample.tierMismatchCount++;
                    }

                    sample.accepted.Add(observation);
                }
                catch (Exception ex)
                {
                    sample.exceptionCount++;
                    if (sample.firstFailure == null)
                    {
                        sample.firstFailure =
                            $"allocator observation threw {ex.GetType().Name}: {ex.Message}";
                    }
                }
                finally
                {
                    // The outer Run finally owns this shared list. Capture even a failed
                    // fulfilment's remaining loadout so an unexpected allocator failure cannot
                    // turn into a fixture Thing leak.
                    TrackGeneratedLoadout(fixtureItems, pawn);
                }
            }
        }

        private static LaborProspect BuildEquipmentDiversityProspect(
            Settlement sourceSettlement,
            LaborEquipmentLevel promisedTier,
            int skillCount,
            int ordinal)
        {
            int[] skillLevels = new int[skillCount];
            Passion[] passions = new Passion[skillCount];
            for (int index = 0; index < skillCount; index++)
            {
                skillLevels[index] = 3 + ((ordinal * 11 + index * 5) % 9);
            }

            int specialty = ordinal % skillCount;
            int secondary = (ordinal * 5 + 3) % skillCount;
            skillLevels[specialty] = 8 + (ordinal % 13);
            passions[specialty] = ordinal % 3 == 0
                ? Passion.Major
                : ordinal % 3 == 1 ? Passion.Minor : Passion.None;
            if (secondary != specialty)
            {
                skillLevels[secondary] = Math.Max(
                    skillLevels[secondary], 7 + ((ordinal * 3) % 9));
                passions[secondary] = ordinal % 4 == 0
                    ? Passion.Minor
                    : Passion.None;
            }

            LaborProspect prospect = new LaborProspect
            {
                settlementId = sourceSettlement.ID,
                settlementName = sourceSettlement.Label ?? "unnamed",
                factionName = sourceSettlement.Faction?.Name ?? "",
                faction = sourceSettlement.Faction,
                distanceTiles = 0f,
                travelDays = 0,
                skillLevels = skillLevels,
                passions = passions,
                equipmentTier = promisedTier
            };
            prospect.pricedSkillValue = EquipmentDiversityProspectPrice(
                skillLevels, passions);
            return prospect;
        }

        private static float EquipmentDiversityProspectPrice(
            int[] skillLevels, Passion[] passions)
        {
            List<int> rankedLevels = new List<int>();
            List<Passion> rankedPassions = new List<Passion>();
            for (int index = 0; index < skillLevels.Length; index++)
            {
                if (skillLevels[index] >= 0)
                {
                    rankedLevels.Add(skillLevels[index]);
                    rankedPassions.Add(
                        passions != null && index < passions.Length
                            ? passions[index]
                            : Passion.None);
                }
            }

            for (int left = 0; left < rankedLevels.Count; left++)
            {
                for (int right = left + 1; right < rankedLevels.Count; right++)
                {
                    if (rankedLevels[right] <= rankedLevels[left])
                    {
                        continue;
                    }

                    int level = rankedLevels[left];
                    rankedLevels[left] = rankedLevels[right];
                    rankedLevels[right] = level;
                    Passion passion = rankedPassions[left];
                    rankedPassions[left] = rankedPassions[right];
                    rankedPassions[right] = passion;
                }
            }

            float price = 0f;
            for (int index = 0;
                 index < LaborCandidateService.PricedSkillCount &&
                 index < rankedLevels.Count;
                 index++)
            {
                price += LaborCandidateService.WeightedLevel(
                    rankedLevels[index], rankedPassions[index]);
            }

            return price;
        }

        private sealed class EquipmentItemObservation
        {
            public string packageKey;
            public string structuralKey;
            public LaborEquipmentItemBand band;
            public bool hasQuality;
            public QualityCategory quality;
        }

        private static EquipmentPackageObservation ObserveEquipmentPackage(Pawn pawn)
        {
            List<string> packageKeys = new List<string>();
            List<string> weaponKeys = new List<string>();
            List<string> apparelKeys = new List<string>();
            bool hasWeapon = false;
            bool hasBasicBand = false;
            bool hasProfessionalBand = false;
            bool hasEliteBand = false;
            bool fullyBestInSlot = true;

            if (pawn?.equipment?.AllEquipmentListForReading != null)
            {
                foreach (ThingWithComps item in pawn.equipment.AllEquipmentListForReading)
                {
                    EquipmentItemObservation observed = ObserveEquipmentItem(item);
                    if (observed == null)
                    {
                        continue;
                    }

                    packageKeys.Add(observed.packageKey);
                    weaponKeys.Add(observed.structuralKey);
                    hasWeapon |= item.def != null && item.def.IsWeapon;
                    hasBasicBand |= observed.band == LaborEquipmentItemBand.Basic;
                    hasProfessionalBand |=
                        observed.band == LaborEquipmentItemBand.Professional;
                    hasEliteBand |= observed.band == LaborEquipmentItemBand.Elite;
                    fullyBestInSlot &= observed.band == LaborEquipmentItemBand.Elite &&
                        observed.hasQuality && observed.quality >= QualityCategory.Excellent;
                }
            }

            if (pawn?.apparel?.WornApparel != null)
            {
                foreach (Apparel item in pawn.apparel.WornApparel)
                {
                    EquipmentItemObservation observed = ObserveEquipmentItem(item);
                    if (observed == null)
                    {
                        continue;
                    }

                    packageKeys.Add(observed.packageKey);
                    apparelKeys.Add(observed.structuralKey);
                    hasBasicBand |= observed.band == LaborEquipmentItemBand.Basic;
                    hasProfessionalBand |=
                        observed.band == LaborEquipmentItemBand.Professional;
                    hasEliteBand |= observed.band == LaborEquipmentItemBand.Elite;
                    fullyBestInSlot &= observed.band == LaborEquipmentItemBand.Elite &&
                        observed.hasQuality && observed.quality >= QualityCategory.Excellent;
                }
            }

            packageKeys.Sort(StringComparer.Ordinal);
            weaponKeys.Sort(StringComparer.Ordinal);
            apparelKeys.Sort(StringComparer.Ordinal);
            int itemCount = packageKeys.Count;
            if (itemCount == 0)
            {
                fullyBestInSlot = false;
            }

            return new EquipmentPackageObservation
            {
                packageSignature = String.Join("|", packageKeys.ToArray()),
                weaponApparelSignature =
                    $"W[{String.Join("|", weaponKeys.ToArray())}]" +
                    $"A[{String.Join("|", apparelKeys.ToArray())}]",
                itemCount = itemCount,
                hasWeapon = hasWeapon,
                hasBasicBand = hasBasicBand,
                hasProfessionalBand = hasProfessionalBand,
                hasEliteBand = hasEliteBand,
                fullyBestInSlot = fullyBestInSlot
            };
        }

        private static EquipmentItemObservation ObserveEquipmentItem(Thing item)
        {
            if (item == null || item.Destroyed || item.def == null || item.stackCount <= 0)
            {
                return null;
            }

            QualityCategory quality = QualityCategory.Normal;
            bool hasQuality = item.TryGetQuality(out quality);
            LaborEquipmentItemScoreResult score = LaborEquipmentItemScore.Evaluate(
                item.def, item.Stuff, quality);
            return new EquipmentItemObservation
            {
                packageKey = EquipmentItemSignature(item, hasQuality, quality),
                structuralKey = EquipmentItemSignature(item, false, quality),
                band = score.Band,
                hasQuality = hasQuality,
                quality = quality
            };
        }

        private static string EquipmentItemSignature(
            Thing item, bool includeQuality, QualityCategory quality)
        {
            string qualityPart = includeQuality ? ((int)quality).ToString() : "*";
            return (item?.def?.defName ?? "<null>") + ":" +
                   (item?.Stuff?.defName ?? "<none>") + ":" + qualityPart;
        }

        private static int DistinctEquipmentSignatures(
            List<EquipmentPackageObservation> observations, bool structural)
        {
            HashSet<string> signatures = new HashSet<string>(StringComparer.Ordinal);
            foreach (EquipmentPackageObservation observation in observations)
            {
                if (observation != null)
                {
                    signatures.Add(structural
                        ? observation.weaponApparelSignature
                        : observation.packageSignature);
                }
            }

            return signatures.Count;
        }

        private static int LargestEquipmentSignatureGroup(
            List<EquipmentPackageObservation> observations, bool structural)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>(
                StringComparer.Ordinal);
            int largest = 0;
            foreach (EquipmentPackageObservation observation in observations)
            {
                if (observation == null)
                {
                    continue;
                }

                string signature = structural
                    ? observation.weaponApparelSignature
                    : observation.packageSignature;
                counts.TryGetValue(signature, out int count);
                count++;
                counts[signature] = count;
                largest = Math.Max(largest, count);
            }

            return largest;
        }

        private static int CountValidEquipmentObservations(
            List<EquipmentPackageObservation> observations)
        {
            int count = 0;
            foreach (EquipmentPackageObservation observation in observations)
            {
                if (observation != null && observation.tierValid)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountFullyBestInSlotPackages(
            List<EquipmentPackageObservation> observations)
        {
            int count = 0;
            foreach (EquipmentPackageObservation observation in observations)
            {
                if (observation != null && observation.tierValid &&
                    observation.fullyBestInSlot)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountProfessionalMixedBandPackages(
            List<EquipmentPackageObservation> observations)
        {
            int count = 0;
            foreach (EquipmentPackageObservation observation in observations)
            {
                if (observation != null && observation.tierValid &&
                    observation.hasBasicBand &&
                    (observation.hasProfessionalBand || observation.hasEliteBand))
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountEliteProfessionalFillerPackages(
            List<EquipmentPackageObservation> observations)
        {
            int count = 0;
            foreach (EquipmentPackageObservation observation in observations)
            {
                if (observation != null && observation.tierValid &&
                    observation.hasEliteBand && observation.hasProfessionalBand)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountCivilianWeaponViolations(
            List<EquipmentPackageObservation> observations)
        {
            int count = 0;
            foreach (EquipmentPackageObservation observation in observations)
            {
                if (observation != null && observation.hasWeapon)
                {
                    count++;
                }
            }

            return count;
        }

        private static string DescribeEquipmentSample(EquipmentPackageSample sample)
        {
            return $"accepted={sample.accepted.Count}/{sample.requested}, " +
                   $"materialise failures={sample.materialiseFailures}, " +
                   $"fulfilment failures={sample.fulfilmentFailures}, " +
                   $"exceptions={sample.exceptionCount}, " +
                   $"tier mismatches={sample.tierMismatchCount}, " +
                   $"prospect identity collisions={sample.prospectIdentityCollisions}, " +
                   $"first failure={sample.firstFailure ?? "none"}";
        }

        private static void SkipArrivalSafetyChecks(Results r, string reason)
        {
            string detail = $"arrival fixture unavailable: {reason ?? "no reason supplied"}";
            r.Skip("a pod preflight failure preserves the contract and the pawn", detail);
            r.Skip("an ordinary arrival still completes in one pass", detail);
        }

        private static bool CheckArrivalSafety(
            Results r, IntercolonyWorldComponent state, Map map, EmploymentContract contract)
        {
            const string preflightLabel =
                "a pod preflight failure preserves the contract and the pawn";
            const string ordinaryLabel = "an ordinary arrival still completes in one pass";

            if (state == null || map == null || Find.WorldPawns == null || Find.Maps == null ||
                !Find.Maps.Contains(map) || contract == null || !state.Employments.Contains(contract))
            {
                SkipArrivalSafetyChecks(
                    r, $"world={state != null}, map={map != null}, map present=" +
                    $"{(map != null && Find.Maps != null && Find.Maps.Contains(map))}, " +
                    $"world-pawn registry={Find.WorldPawns != null}, " +
                    $"contract={contract != null}, listed=" +
                    $"{(contract != null && state != null && state.Employments.Contains(contract))}");
                return false;
            }

            Pawn worker = contract.pawn;
            if (worker == null || worker.Destroyed || worker.Spawned ||
                !Find.WorldPawns.Contains(worker) ||
                contract.status != EmploymentStatus.Travelling)
            {
                SkipArrivalSafetyChecks(
                    r, $"worker={worker != null}, destroyed={worker?.Destroyed ?? false}, " +
                    $"spawned={worker?.Spawned ?? false}, in WorldPawns=" +
                    $"{(worker != null && Find.WorldPawns.Contains(worker))}, " +
                    $"status={contract.status}");
                return false;
            }

            EmploymentArrivalTransport savedTransport = contract.arrivalTransport;
            Map savedDestinationMap = contract.destinationMap;
            int savedArrivalTick = contract.arrivalTick;
            int savedArrivedTick = contract.arrivedTick;
            bool preflightAssertionMade = false;
            bool ordinaryAssertionMade = false;

            try
            {
                // The null destination is a deterministic, real preflight failure. It happens
                // before the transporter is created, so there is no pod to clean up and no
                // synthetic pawn movement in this assertion.
                int preflightDueTick = GenTicks.TicksGame;
                contract.arrivalTransport = EmploymentArrivalTransport.DropPod;
                contract.destinationMap = null;
                contract.arrivalTick = preflightDueTick;
                EmploymentService.Advance(state.Employments);

                bool pawnSurvived = ReferenceEquals(worker, contract.pawn) &&
                    contract.pawn != null && !contract.pawn.Destroyed &&
                    Find.WorldPawns.Contains(contract.pawn);
                bool preflightPreserved = contract.status == EmploymentStatus.Travelling &&
                    contract.arrivedTick == EmploymentContract.NotArrived && pawnSurvived;
                preflightAssertionMade = true;
                r.Check(
                    preflightPreserved,
                    preflightLabel,
                    $"due {preflightDueTick}; transport {contract.arrivalTransport}; " +
                    $"destination null {contract.destinationMap == null}; status {contract.status}; " +
                    $"arrivedTick {contract.arrivedTick} (expected " +
                    $"{EmploymentContract.NotArrived}); pawn same {ReferenceEquals(worker, contract.pawn)}; " +
                    $"pawn null {contract.pawn == null}; destroyed {contract.pawn?.Destroyed ?? false}; " +
                    $"in WorldPawns {contract.pawn != null && Find.WorldPawns.Contains(contract.pawn)}");

                // Reuse the same already-hired pawn for the ordinary path. This is deliberately
                // the public hourly entry point, not a direct call to Arrive or a manual spawn.
                contract.arrivalTransport = EmploymentArrivalTransport.Conventional;
                contract.destinationMap = map;
                int ordinaryDueTick = GenTicks.TicksGame;
                contract.arrivalTick = ordinaryDueTick;
                int ordinaryPassTick = GenTicks.TicksGame;
                EmploymentService.Advance(state.Employments);

                bool ordinaryCompleted = contract.status == EmploymentStatus.Active &&
                    contract.arrivedTick == ordinaryPassTick;
                ordinaryAssertionMade = true;
                r.Check(
                    ordinaryCompleted,
                    ordinaryLabel,
                    $"due {ordinaryDueTick}; pass {ordinaryPassTick}; status {contract.status}; " +
                    $"arrivedTick {contract.arrivedTick}; worker spawned " +
                    $"{contract.pawn != null && contract.pawn.Spawned}");
            }
            catch (Exception ex)
            {
                string detail = $"{ex.GetType().Name}: {ex.Message}; status {contract.status}; " +
                    $"arrivedTick {contract.arrivedTick}; pawn same " +
                    $"{ReferenceEquals(worker, contract.pawn)}; pawn destroyed " +
                    $"{contract.pawn?.Destroyed ?? false}";
                if (!preflightAssertionMade)
                {
                    r.Check(false, preflightLabel, detail);
                }

                if (!ordinaryAssertionMade)
                {
                    r.Check(false, ordinaryLabel, detail);
                }
            }
            finally
            {
                // The route mutations are test setup only. Keep the real ordinary-arrival state
                // if it succeeded, while restoring the saved route fields for every other exit.
                contract.arrivalTransport = savedTransport;
                contract.destinationMap = savedDestinationMap;
                if (contract.status == EmploymentStatus.Travelling)
                {
                    contract.arrivalTick = savedArrivalTick;
                    contract.arrivedTick = savedArrivedTick;
                }
            }

            return contract.status == EmploymentStatus.Active &&
                contract.pawn != null && contract.pawn.Spawned;
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

        private static LaborCandidate FindStableEmergencyControlCandidate(
            List<LaborCandidate> candidates,
            IntercolonyWorldComponent state,
            out SettlementEconomicProfile sourceProfile)
        {
            sourceProfile = null;
            LaborCandidate selected = null;
            if (candidates == null || state == null)
            {
                return null;
            }

            foreach (LaborCandidate candidate in candidates)
            {
                if (candidate?.pawn == null || candidate.travelDays < 0)
                {
                    continue;
                }

                Settlement source = IntercolonyMarketAccess.FindSettlement(
                    candidate.settlementId);
                SettlementEconomicProfile profile = source == null
                    ? null
                    : state.GetProfile(source);
                if (profile == null)
                {
                    continue;
                }

                bool isEarlierStableCandidate = selected == null ||
                    candidate.settlementId < selected.settlementId ||
                    (candidate.settlementId == selected.settlementId &&
                        candidate.pawn.thingIDNumber < selected.pawn.thingIDNumber);
                if (isEarlierStableCandidate)
                {
                    selected = candidate;
                    sourceProfile = profile;
                }
            }

            return selected;
        }

        /// <summary>
        /// F24's emergency mode is deliberately a direct-hire slice: the ordinary listing is
        /// filtered, the same candidate is priced with an independent premium, and its arrival
        /// route follows the authoritative emergency quote. This stays next to F23's bond
        /// assertion because both checks drive the real employment transaction rather than a
        /// private copy of it.
        /// </summary>
        private static void CheckEmergencyDispatch(
            Results r, IntercolonyWorldComponent state, Map map, List<Pawn> fixturePawns,
            EmploymentContract ordinaryContract,
            List<LaborCandidate> ordinaryPoolSnapshot,
            List<LaborCandidate> emergencyPoolSnapshot,
            Dictionary<LaborCandidate, EmergencyArrivalQuote> emergencyQuoteSnapshot)
        {
            int savedSilver = PurchaseOrderService.CountColonySilver(map);
            int savedEmployments = state.Employments.Count;
            int savedLedger = state.Ledger.Count;
            int savedLedgerStartTick = state.LedgerStartTick;
            EmployerReputation savedStandingOwner = state.EmployerStanding;
            float savedStanding = savedStandingOwner?.Score ?? 0f;
            SettlementEconomicProfile controlledSourceProfile = null;
            SettlementRapidLogisticsCapability savedControlledCapability =
                SettlementRapidLogisticsCapability.ConventionalTransportOnly;
            bool controlledCapabilityChanged = false;

            try
            {
                r.Check(IntercolonyWorldComponent.CurrentSaveVersion == ExpectedCurrentSaveVersion,
                    "U4 CurrentSaveVersion remains 60",
                    $"expected 60, actual {IntercolonyWorldComponent.CurrentSaveVersion}");

                // Capture U1 before the main F23 hire consumes one candidate; otherwise the
                // eligibility comparison would be measuring a changed market. U2-U4 deliberately
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

                LaborCandidate controlledCandidate = FindStableEmergencyControlCandidate(
                    ordinaryPool, state, out controlledSourceProfile);
                if (controlledCandidate != null && controlledSourceProfile != null)
                {
                    savedControlledCapability = controlledSourceProfile.rapidLogisticsCapability;
                    controlledSourceProfile.rapidLogisticsCapability =
                        SettlementRapidLogisticsCapability.DropPodsAvailable;
                    controlledCapabilityChanged = true;
                }

                List<LaborCandidate> emergencyPool =
                    new List<LaborCandidate>(ordinaryPool);
                emergencyPool.RemoveAll(candidate =>
                    !LaborCandidateService.CanReachEmergency(candidate));

                CheckEmergencyUiFilter(r);

                List<string> eligibilityDisagreements = new List<string>();
                int expectedEmergencyCount = 0;
                // emergencyPoolForU1 is the pre-hire result of filtering this snapshot with
                // CanReachEmergency. Use membership rather than calling it again: the main F23
                // hire has already released one pawn by the time this check runs.
                foreach (LaborCandidate candidateInSnapshot in ordinaryPoolForU1)
                {
                    EmergencyArrivalQuote expectedQuote;
                    bool expectedEligible = ExpectedEmergencyEligibility(
                        candidateInSnapshot, emergencyQuoteSnapshot, out expectedQuote);
                    bool actualEligible = emergencyPoolForU1.Contains(candidateInSnapshot);
                    if (expectedEligible)
                    {
                        expectedEmergencyCount++;
                    }

                    if (actualEligible != expectedEligible)
                    {
                        eligibilityDisagreements.Add(
                            EmergencyEligibilityDetail(
                                candidateInSnapshot, expectedQuote, actualEligible));
                    }
                }

                bool emergencyEligibilityMatches =
                    emergencyPoolForU1.Count == expectedEmergencyCount &&
                    eligibilityDisagreements.Count == 0;
                r.Check(emergencyEligibilityMatches,
                    "U1 emergency eligibility follows the authoritative route quote",
                    $"ordinary {ordinaryPoolForU1.Count}, expected eligible {expectedEmergencyCount}, " +
                    $"actual emergency {emergencyPoolForU1.Count}, disagreements " +
                    $"[{(eligibilityDisagreements.Count == 0
                        ? "none"
                        : string.Join("; ", eligibilityDisagreements))}]");

                LaborCandidate podArrivalCandidate = FindEmergencyFixtureCandidate(
                    emergencyPool, requireDropPod: true);
                LaborCandidate conventionalArrivalCandidate = FindConventionalArrivalFixture(
                    ordinaryPool, state);
                LaborCandidate distantConventionalArrivalCandidate =
                    FindDistantConventionalArrivalFixture(ordinaryPool, state);
                CheckEmergencyArrivalTicks(
                    r, emergencyPool, podArrivalCandidate,
                    conventionalArrivalCandidate, distantConventionalArrivalCandidate);

                LaborCandidate emergencyCandidate = podArrivalCandidate ??
                    FindEmergencyFixtureCandidate(emergencyPool, requireDropPod: false);

                if (emergencyCandidate == null)
                {
                    string reason =
                        "no emergency candidate was available in the current direct-hire market; " +
                        $"travel days found [{CandidateTravelDaysDetail(ordinaryPoolForU1)}]";
                    r.Skip("U2 emergency dispatch wage premium", reason);
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
                if (controlledCapabilityChanged && controlledSourceProfile != null)
                {
                    controlledSourceProfile.rapidLogisticsCapability = savedControlledCapability;
                }

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
                "U1 labor UI applies the emergency eligibility filter",
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
            List<LaborCandidate> emergencyPool,
            bool requireDropPod)
        {
            if (emergencyPool == null)
            {
                return null;
            }

            foreach (LaborCandidate candidate in emergencyPool)
            {
                if (candidate?.pawn == null)
                {
                    continue;
                }

                EmergencyArrivalQuote quote =
                    LaborCandidateService.QuoteEmergencyArrival(candidate);
                if (quote.available &&
                    (!requireDropPod || quote.transport == EmploymentArrivalTransport.DropPod))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static LaborCandidate FindConventionalArrivalFixture(
            List<LaborCandidate> ordinaryPool, IntercolonyWorldComponent state)
        {
            if (ordinaryPool == null)
            {
                return null;
            }

            foreach (LaborCandidate candidate in ordinaryPool)
            {
                if (candidate?.pawn == null || IsDropPodCapable(state, candidate))
                {
                    continue;
                }

                // Use a real conventional candidate identity at a known close distance because
                // the representative world may not happen to list a nearby source.
                LaborCandidate closeCandidate = CandidateAtDistance(
                    candidate, CloseConventionalFixtureDistanceTiles);
                EmergencyArrivalQuote quote =
                    LaborCandidateService.QuoteEmergencyArrival(closeCandidate);
                if (quote.available && quote.transport == EmploymentArrivalTransport.Conventional)
                {
                    return closeCandidate;
                }
            }

            return null;
        }

        private static LaborCandidate FindDistantConventionalArrivalFixture(
            List<LaborCandidate> ordinaryPool, IntercolonyWorldComponent state)
        {
            if (ordinaryPool == null)
            {
                return null;
            }

            foreach (LaborCandidate candidate in ordinaryPool)
            {
                if (candidate?.pawn == null || IsDropPodCapable(state, candidate))
                {
                    continue;
                }

                // Keep the same real source and pawn as the close fixture, but put it clearly
                // beyond the named production cutoff to prove it is unavailable, not slow.
                LaborCandidate distantCandidate = CandidateAtDistance(
                    candidate, DistantConventionalFixtureDistanceTiles);
                EmergencyArrivalQuote quote =
                    LaborCandidateService.QuoteEmergencyArrival(distantCandidate);
                if (!quote.available)
                {
                    return distantCandidate;
                }
            }

            return null;
        }

        private static LaborCandidate CandidateAtDistance(
            LaborCandidate source, float distanceTiles)
        {
            return new LaborCandidate
            {
                pawn = source.pawn,
                settlementId = source.settlementId,
                settlementName = source.settlementName,
                factionName = source.factionName,
                faction = source.faction,
                distanceTiles = distanceTiles,
                dailyWage = source.dailyWage,
                minTermDays = source.minTermDays,
                travelDays = source.travelDays
            };
        }

        private static bool ExpectedEmergencyEligibility(
            LaborCandidate candidate,
            Dictionary<LaborCandidate, EmergencyArrivalQuote> quoteSnapshot,
            out EmergencyArrivalQuote quote)
        {
            if (quoteSnapshot != null && quoteSnapshot.TryGetValue(candidate, out quote))
            {
                return quote.available;
            }

            quote = LaborCandidateService.QuoteEmergencyArrival(candidate);
            return quote.available;
        }

        private static bool IsDropPodCapable(
            IntercolonyWorldComponent state, LaborCandidate candidate)
        {
            if (state == null || candidate == null)
            {
                return false;
            }

            Settlement source = IntercolonyMarketAccess.FindSettlement(candidate.settlementId);
            if (source == null)
            {
                return false;
            }

            EmergencyArrivalQuote quote = LaborCandidateService.QuoteEmergencyArrival(candidate);
            return quote.available && quote.transport == EmploymentArrivalTransport.DropPod;
        }

        private static string EmergencyEligibilityDetail(
            LaborCandidate candidate, EmergencyArrivalQuote quote, bool actual)
        {
            string name = candidate == null ? "null" : candidate.Name;
            return $"{name}={candidate?.travelDays ?? -1}d, quote available " +
                $"{quote.available}, route {quote.transport}, method {quote.methodLabel}, " +
                $"ETA ticks {quote.arrivalTicks} " +
                $"({quote.arrivalTicks / (float)GenDate.TicksPerHour:0.##}h), actual " +
                $"{actual}";
        }

        private static void ReportTravelDayDistribution(
            Results r, List<LaborCandidate> candidates)
        {
            List<int> travelDays = new List<int>();
            int emergencyEligibleCount = 0;
            if (candidates != null)
            {
                foreach (LaborCandidate candidate in candidates)
                {
                    if (candidate == null || candidate.travelDays < 0)
                    {
                        continue;
                    }

                    travelDays.Add(candidate.travelDays);
                    EmergencyArrivalQuote quote =
                        LaborCandidateService.QuoteEmergencyArrival(candidate);
                    if (quote.available)
                    {
                        emergencyEligibleCount++;
                    }
                }
            }

            if (travelDays.Count == 0)
            {
                r.Info("direct-hire travel-day distribution: min n/a, median n/a, max n/a; " +
                    "emergency eligible: 0/0 candidates");
                return;
            }

            travelDays.Sort();
            float median = travelDays.Count % 2 == 1
                ? travelDays[travelDays.Count / 2]
                : (travelDays[travelDays.Count / 2 - 1] +
                    travelDays[travelDays.Count / 2]) / 2f;
            r.Info($"direct-hire travel-day distribution: min {travelDays[0]}d, " +
                $"median {median:0.##}d, max {travelDays[travelDays.Count - 1]}d; " +
                $"emergency eligible: {emergencyEligibleCount}/{travelDays.Count} candidates");
        }

        private static void CheckEmergencyArrivalTicks(
            Results r, List<LaborCandidate> candidatePool,
            LaborCandidate podCandidate, LaborCandidate conventionalCandidate,
            LaborCandidate distantConventionalCandidate)
        {
            int examinedCandidates = candidatePool?.Count ?? 0;
            int podCapableCandidates = 0;
            if (candidatePool != null)
            {
                foreach (LaborCandidate candidate in candidatePool)
                {
                    EmergencyArrivalQuote quote =
                        LaborCandidateService.QuoteEmergencyArrival(candidate);
                    if (quote.available && quote.transport == EmploymentArrivalTransport.DropPod)
                    {
                        podCapableCandidates++;
                    }
                }
            }

            EmergencyArrivalQuote conventionalQuote = conventionalCandidate == null
                ? new EmergencyArrivalQuote()
                : LaborCandidateService.QuoteEmergencyArrival(conventionalCandidate);
            int actualConventionalArrivalTicks = conventionalCandidate == null
                ? -1
                : LaborCandidateService.ArrivalTicksFor(conventionalCandidate, true);
            bool conventionalArrivalMatches = conventionalCandidate != null &&
                conventionalQuote.available &&
                conventionalQuote.transport == EmploymentArrivalTransport.Conventional &&
                conventionalQuote.methodLabel == "Emergency caravan" &&
                actualConventionalArrivalTicks == conventionalQuote.arrivalTicks &&
                actualConventionalArrivalTicks >= 5 * GenDate.TicksPerHour &&
                actualConventionalArrivalTicks <= 9 * GenDate.TicksPerHour;
            if (conventionalCandidate == null)
            {
                r.Skip(
                    "U3 conventional emergency quotes 5-9 hours",
                    $"the current candidate pool had no non-drop-pod source settlement for a " +
                    $"conventional emergency fixture; examined {examinedCandidates} candidate(s)");
            }
            else
            {
                r.Check(conventionalArrivalMatches,
                    "U3 conventional emergency quotes 5-9 hours",
                    $"OBSERVED {conventionalCandidate.Name} " +
                    $"arrivalTicks={actualConventionalArrivalTicks} " +
                    $"({actualConventionalArrivalTicks / (float)GenDate.TicksPerHour:0.##}h), " +
                    $"transport={conventionalQuote.transport}; EXPECTED between " +
                    $"{5 * GenDate.TicksPerHour} and {9 * GenDate.TicksPerHour} ticks " +
                    "(5h-9h), transport=Conventional");
            }

            EmergencyArrivalQuote distantQuote = distantConventionalCandidate == null
                ? new EmergencyArrivalQuote()
                : LaborCandidateService.QuoteEmergencyArrival(distantConventionalCandidate);
            int distantArrivalTicks = distantConventionalCandidate == null
                ? -1
                : LaborCandidateService.ArrivalTicksFor(distantConventionalCandidate, true);
            bool distantConventionalUnavailable = distantConventionalCandidate != null &&
                !distantQuote.available &&
                !LaborCandidateService.CanReachEmergency(distantConventionalCandidate) &&
                distantArrivalTicks == distantQuote.arrivalTicks &&
                distantArrivalTicks == 0;
            if (distantConventionalCandidate == null)
            {
                r.Skip(
                    "U3 distant conventional emergency is unavailable",
                    $"the current candidate pool had no non-drop-pod source settlement for a " +
                    $"distant conventional emergency fixture; examined {examinedCandidates} candidate(s)");
            }
            else
            {
                r.Check(distantConventionalUnavailable,
                    "U3 distant conventional emergency is unavailable",
                    $"{distantConventionalCandidate.Name}: quote available " +
                    $"{distantQuote.available}, ETA ticks {distantArrivalTicks} " +
                    $"({distantArrivalTicks / (float)GenDate.TicksPerHour:0.##}h)");
            }

            CheckEmergencyQuoteDeterminism(r, candidatePool);
            CheckEmergencyQuoteVariation(r, candidatePool);
            CheckEmergencyQuoteUpperBound(r, candidatePool);

            if (podCapableCandidates == 0 || podCandidate == null)
            {
                string reason = podCapableCandidates == 0
                    ? $"no pod-capable source settlement; examined {examinedCandidates} " +
                      $"candidate(s), {podCapableCandidates} pod-capable"
                    : $"the post-hire candidate pool had no pod-capable fixture; the pre-hire " +
                      $"pool contained {podCapableCandidates} pod-capable candidate(s) out of " +
                      $"{examinedCandidates} examined";
                r.Skip("U3 emergency pod quotes 1-4 hours", reason);
                return;
            }

            EmergencyArrivalQuote podQuote = LaborCandidateService.QuoteEmergencyArrival(podCandidate);
            int actualPodArrivalTicks = podCandidate == null
                ? -1
                : LaborCandidateService.ArrivalTicksFor(podCandidate, true);
            bool podArrivalInHours = podCandidate != null &&
                podQuote.available &&
                podQuote.transport == EmploymentArrivalTransport.DropPod &&
                podQuote.methodLabel == "Drop pod" &&
                actualPodArrivalTicks == podQuote.arrivalTicks &&
                actualPodArrivalTicks >= GenDate.TicksPerHour &&
                actualPodArrivalTicks <= 4 * GenDate.TicksPerHour;
            r.Check(podArrivalInHours,
                "U3 emergency pod quotes 1-4 hours",
                $"OBSERVED {podCandidate?.Name ?? "missing"} " +
                $"arrivalTicks={actualPodArrivalTicks} " +
                $"({actualPodArrivalTicks / (float)GenDate.TicksPerHour:0.##}h), " +
                $"transport={podQuote.transport}; EXPECTED between " +
                $"{GenDate.TicksPerHour} and {4 * GenDate.TicksPerHour} ticks " +
                "(1h-4h), transport=DropPod");
        }

        private static void CheckEmergencyQuoteDeterminism(
            Results r, List<LaborCandidate> candidatePool)
        {
            const string label = "the same emergency candidate gets the same arrival ticks twice";
            LaborCandidate candidate = null;
            if (candidatePool != null)
            {
                foreach (LaborCandidate possible in candidatePool)
                {
                    EmergencyArrivalQuote possibleQuote =
                        LaborCandidateService.QuoteEmergencyArrival(possible);
                    if (possible?.pawn != null && possibleQuote.available)
                    {
                        candidate = possible;
                        break;
                    }
                }
            }

            if (candidate == null)
            {
                r.Skip(label,
                    "the current candidate pool had no candidate with an available emergency route");
                return;
            }

            EmergencyArrivalQuote firstQuote =
                LaborCandidateService.QuoteEmergencyArrival(candidate);
            EmergencyArrivalQuote secondQuote =
                LaborCandidateService.QuoteEmergencyArrival(candidate);
            r.Check(firstQuote.arrivalTicks == secondQuote.arrivalTicks,
                label,
                $"OBSERVED {candidate.Name}: first arrivalTicks={firstQuote.arrivalTicks} " +
                $"({firstQuote.arrivalTicks / (float)GenDate.TicksPerHour:0.##}h), " +
                $"second arrivalTicks={secondQuote.arrivalTicks} " +
                $"({secondQuote.arrivalTicks / (float)GenDate.TicksPerHour:0.##}h); " +
                "EXPECTED identical arrivalTicks");
        }

        private static void CheckEmergencyQuoteVariation(
            Results r, List<LaborCandidate> candidatePool)
        {
            const string label = "pod emergency arrival ticks vary across stable identities";
            const int MinimumLivePodCandidatesForPopulationSample = 5;
            const int SyntheticPodProbeCount = 8;
            const int SyntheticSettlementId = 4242;
            const int SyntheticPawnIdStart = 9001;

            if (candidatePool == null)
            {
                r.Skip(label,
                    "the current world supplied no candidate pool, so no emergency population " +
                    "fixture was available for the variation check");
                return;
            }

            List<LaborCandidate> podCandidates = new List<LaborCandidate>();
            HashSet<int> podPawnIds = new HashSet<int>();
            foreach (LaborCandidate candidate in candidatePool)
            {
                EmergencyArrivalQuote quote =
                    LaborCandidateService.QuoteEmergencyArrival(candidate);
                if (candidate?.pawn != null && quote.available &&
                    quote.transport == EmploymentArrivalTransport.DropPod &&
                    podPawnIds.Add(candidate.pawn.thingIDNumber))
                {
                    podCandidates.Add(candidate);
                }
            }

            if (podCandidates.Count >= MinimumLivePodCandidatesForPopulationSample)
            {
                HashSet<int> distinctArrivalTicks = new HashSet<int>();
                bool allInBand = true;
                foreach (LaborCandidate candidate in podCandidates)
                {
                    EmergencyArrivalQuote quote =
                        LaborCandidateService.QuoteEmergencyArrival(candidate);
                    distinctArrivalTicks.Add(quote.arrivalTicks);
                    allInBand = allInBand &&
                        quote.arrivalTicks >= GenDate.TicksPerHour &&
                        quote.arrivalTicks <= 4 * GenDate.TicksPerHour;
                }

                bool livePopulationVaries = allInBand && distinctArrivalTicks.Count >= 2;
                r.Check(livePopulationVaries, label,
                    $"OBSERVED {podCandidates.Count} unique pod candidate(s), " +
                    $"{distinctArrivalTicks.Count} distinct arrivalTicks; EXPECTED at least " +
                    "two distinct in-band tick values");
                return;
            }

            // A fresh world can expose fewer than a handful of pod candidates. Probe the same
            // reducer with stable synthetic settlement/pawn IDs so the assertion measures a
            // population instead of making a two-candidate collision the product of the test.
            HashSet<int> syntheticArrivalTicks = new HashSet<int>();
            bool syntheticValuesInBand = true;
            for (int i = 0; i < SyntheticPodProbeCount; i++)
            {
                int syntheticPawnId = SyntheticPawnIdStart + i;
                int identityHash = Gen.HashCombineInt(SyntheticSettlementId, syntheticPawnId);
                int arrivalTicks = LaborCandidateService.DeterministicEmergencyArrivalTicks(
                    identityHash, 1, 4);
                syntheticArrivalTicks.Add(arrivalTicks);
                syntheticValuesInBand = syntheticValuesInBand &&
                    arrivalTicks >= GenDate.TicksPerHour &&
                    arrivalTicks <= 4 * GenDate.TicksPerHour;
            }

            bool syntheticPopulationVaries =
                syntheticValuesInBand && syntheticArrivalTicks.Count >= 2;
            r.Check(syntheticPopulationVaries, label,
                $"OBSERVED {podCandidates.Count} live pod candidate(s); synthetic probe " +
                $"population={SyntheticPodProbeCount}, distinct arrivalTicks=" +
                $"{syntheticArrivalTicks.Count}; EXPECTED at least two distinct values between " +
                $"{GenDate.TicksPerHour} and {4 * GenDate.TicksPerHour} ticks (1h-4h)");
        }

        private static void CheckEmergencyQuoteUpperBound(
            Results r, List<LaborCandidate> candidatePool)
        {
            const string label = "no emergency quote uses ordinary multi-day timing";
            int availableCount = 0;
            StringBuilder violations = new StringBuilder();
            if (candidatePool != null)
            {
                foreach (LaborCandidate candidate in candidatePool)
                {
                    EmergencyArrivalQuote quote =
                        LaborCandidateService.QuoteEmergencyArrival(candidate);
                    if (!quote.available)
                    {
                        continue;
                    }

                    availableCount++;
                    if (quote.arrivalTicks > 9 * GenDate.TicksPerHour)
                    {
                        if (violations.Length > 0)
                        {
                            violations.Append("; ");
                        }

                        violations.Append(candidate?.Name ?? "missing")
                            .Append('=')
                            .Append(quote.arrivalTicks)
                            .Append(" ticks");
                    }
                }
            }

            if (availableCount == 0)
            {
                r.Skip(label,
                    "the current candidate pool had no available emergency quote to sweep");
                return;
            }

            r.Check(violations.Length == 0, label,
                $"OBSERVED {availableCount} available quote(s), violations " +
                $"[{(violations.Length == 0 ? "none" : violations.ToString())}]; EXPECTED every " +
                $"available arrivalTicks <= {9 * GenDate.TicksPerHour} ticks (9h)");
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
                new[]
                {
                    "equipmentBond", "moodSampleTotal", "moodSampleCount",
                    // Emergency pod hires write DropPod while ordinary hires keep the Conventional
                    // default, which Scribe omits; presence reflects the route, not schema drift.
                    "arrivalTransport"
                },
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

        private sealed class SettingsRoundTrip
        {
            public IntercolonySettings loaded;
            public HashSet<string> savedNodes;
            public string failure;
        }

        private static void CheckSettingsDefaultMigration(Results r)
        {
            if (Scribe.saver == null || Scribe.loader == null)
            {
                r.Skip(
                    "settings defaults and migration round trips",
                    "vanilla Scribe saver or loader was unavailable");
                return;
            }

            IntercolonySettings freshSaved = new IntercolonySettings();
            bool freshConstructorDefaults = freshSaved.settingsVersion == 1 &&
                SettingsHaveFreshDefaults(freshSaved);
            SettingsRoundTrip fresh = RoundTripSettings(
                freshSaved, "intercolony-settings-fresh", null);
            r.Check(
                freshConstructorDefaults &&
                fresh.savedNodes != null && fresh.savedNodes.Contains("settingsVersion") &&
                fresh.failure == null && SettingsHaveFreshDefaults(fresh.loaded),
                "fresh-install settings use the new defaults",
                $"constructor={DescribeSettings(freshSaved)}; loaded={DescribeSettings(fresh.loaded)}; " +
                    $"failure={fresh.failure ?? "none"}");

            IntercolonySettings untouchedSaved = new IntercolonySettings
            {
                settingsVersion = -1
            };
            SettingsRoundTrip untouched = RoundTripSettings(
                untouchedSaved,
                "intercolony-settings-legacy-untouched",
                RemoveMigratedSettingsNodes);
            r.Check(
                untouched.failure == null && SettingsHaveLegacyDefaults(untouched.loaded),
                "pre-version untouched settings keep their old effective values",
                $"loaded={DescribeSettings(untouched.loaded)}; " +
                    $"failure={untouched.failure ?? "none"}");

            IntercolonySettings explicitSaved = new IntercolonySettings
            {
                settingsVersion = -1,
                letterVolume = IntercolonyLetterVolume.Everything,
                refreshDays = 0.25f,
                activeOpportunities = 37,
                enabledBuyOnlyTradeCategoryKeys = new HashSet<string>(
                    new[] { "FoodMeals" }),
                commercialGoodwillIntervalDays = 7,
                commercialGoodwillPerInterval = 5,
                commercialGoodwillCeiling = 15,
                commercialReputationRequired = 85,
                minimumEmploymentDaysForGoodwill = 4,
                employmentGoodwillImpact = 2
            };
            SettingsRoundTrip explicitRoundTrip = RoundTripSettings(
                explicitSaved, "intercolony-settings-legacy-explicit", null);
            r.Check(
                explicitRoundTrip.failure == null &&
                explicitRoundTrip.savedNodes != null &&
                !explicitRoundTrip.savedNodes.Contains("settingsVersion") &&
                HasAllMigratedSettingsNodes(explicitRoundTrip.savedNodes) &&
                SettingsHaveExplicitValues(explicitRoundTrip.loaded),
                "pre-version explicitly saved settings keep their exact values",
                $"savedNodes={NodeNamesDetail(explicitRoundTrip.savedNodes)}; " +
                    $"loaded={DescribeSettings(explicitRoundTrip.loaded)}; " +
                    $"failure={explicitRoundTrip.failure ?? "none"}");
        }

        private static SettingsRoundTrip RoundTripSettings(
            IntercolonySettings savedSettings,
            string label,
            Action<XmlDocument> editDocument)
        {
            SettingsRoundTrip result = new SettingsRoundTrip();
            string path = Path.Combine(
                Path.GetTempPath(), $"Intercolony-{label}-{Guid.NewGuid():N}.xml");

            try
            {
                Scribe.ForceStop();
                Scribe.saver.InitSaving(path, label);
                Scribe_Deep.Look(ref savedSettings, "settings");
                Scribe.saver.FinalizeSaving();

                XmlDocument document = new XmlDocument();
                document.Load(path);
                result.savedNodes = SettingsNodeNames(document);
                if (editDocument != null)
                {
                    editDocument(document);
                    document.Save(path);
                }

                Scribe.loader.InitLoading(path);
                Scribe_Deep.Look(ref result.loaded, "settings");
                Scribe.loader.FinalizeLoading();
            }
            catch (Exception ex)
            {
                result.failure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                try
                {
                    Scribe.ForceStop();
                }
                catch (Exception ex)
                {
                    if (result.failure == null)
                    {
                        result.failure =
                            $"Scribe cleanup {ex.GetType().Name}: {ex.Message}";
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
                    if (result.failure == null)
                    {
                        result.failure =
                            $"temporary XML cleanup {ex.GetType().Name}: {ex.Message}";
                    }
                }
            }

            return result;
        }

        private static HashSet<string> SettingsNodeNames(XmlDocument document)
        {
            XmlNode settingsNode = document.SelectSingleNode("//settings");
            if (settingsNode == null)
            {
                return null;
            }

            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            foreach (XmlNode child in settingsNode.ChildNodes)
            {
                if (child.NodeType == XmlNodeType.Element)
                {
                    names.Add(child.Name);
                }
            }

            return names;
        }

        private static void RemoveMigratedSettingsNodes(XmlDocument document)
        {
            XmlNode settingsNode = document.SelectSingleNode("//settings");
            if (settingsNode == null)
            {
                throw new InvalidOperationException("Scribe settings wrapper was missing");
            }

            foreach (string name in MigratedSettingsNodeNames())
            {
                XmlNode node = settingsNode[name];
                if (node != null)
                {
                    settingsNode.RemoveChild(node);
                }
            }
        }

        private static string[] MigratedSettingsNodeNames()
        {
            return new[]
            {
                "settingsVersion",
                "letterVolume",
                "refreshDays",
                "activeOpportunities",
                "enabledBuyOnlyTradeCategoryKeys",
                "commercialGoodwillIntervalDays",
                "commercialGoodwillPerInterval",
                "commercialGoodwillCeiling",
                "commercialReputationRequired",
                "minimumEmploymentDaysForGoodwill",
                "employmentGoodwillImpact"
            };
        }

        private static bool HasAllMigratedSettingsNodes(HashSet<string> nodes)
        {
            if (nodes == null)
            {
                return false;
            }

            foreach (string name in MigratedSettingsNodeNames())
            {
                if (name == "settingsVersion")
                {
                    continue;
                }

                if (!nodes.Contains(name))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SettingsHaveFreshDefaults(IntercolonySettings settings)
        {
            return settings != null && settings.settingsVersion == 1 &&
                settings.letterVolume == IntercolonyLetterVolume.Minimal &&
                settings.refreshDays == 0.25f &&
                settings.activeOpportunities == 50 &&
                HasExactly(settings.enabledBuyOnlyTradeCategoryKeys,
                    "FoodMeals", "StoneBlocks") &&
                settings.commercialGoodwillIntervalDays == 7 &&
                settings.commercialGoodwillPerInterval == 2 &&
                settings.commercialGoodwillCeiling == 15 &&
                settings.commercialReputationRequired == 85 &&
                settings.minimumEmploymentDaysForGoodwill == 4 &&
                settings.employmentGoodwillImpact == 2;
        }

        private static bool SettingsHaveLegacyDefaults(IntercolonySettings settings)
        {
            return settings != null && settings.settingsVersion == 1 &&
                settings.letterVolume == IntercolonyLetterVolume.ImportantOnly &&
                settings.refreshDays == 1f &&
                settings.activeOpportunities == 60 &&
                HasExactly(settings.enabledBuyOnlyTradeCategoryKeys) &&
                settings.commercialGoodwillIntervalDays == 15 &&
                settings.commercialGoodwillPerInterval == 1 &&
                settings.commercialGoodwillCeiling == 60 &&
                settings.commercialReputationRequired == 80 &&
                settings.minimumEmploymentDaysForGoodwill == 10 &&
                settings.employmentGoodwillImpact == 3;
        }

        private static bool SettingsHaveExplicitValues(IntercolonySettings settings)
        {
            return settings != null && settings.settingsVersion == 1 &&
                settings.letterVolume == IntercolonyLetterVolume.Everything &&
                settings.refreshDays == 0.25f &&
                settings.activeOpportunities == 37 &&
                HasExactly(settings.enabledBuyOnlyTradeCategoryKeys, "FoodMeals") &&
                settings.commercialGoodwillIntervalDays == 7 &&
                settings.commercialGoodwillPerInterval == 5 &&
                settings.commercialGoodwillCeiling == 15 &&
                settings.commercialReputationRequired == 85 &&
                settings.minimumEmploymentDaysForGoodwill == 4 &&
                settings.employmentGoodwillImpact == 2;
        }

        private static bool HasExactly(HashSet<string> actual, params string[] expected)
        {
            if (actual == null || actual.Count != expected.Length)
            {
                return false;
            }

            foreach (string value in expected)
            {
                if (!actual.Contains(value))
                {
                    return false;
                }
            }

            return true;
        }

        private static string DescribeSettings(IntercolonySettings settings)
        {
            if (settings == null)
            {
                return "missing";
            }

            return $"version={settings.settingsVersion}; letter={settings.letterVolume}; " +
                $"refresh={settings.refreshDays}; active={settings.activeOpportunities}; " +
                $"categories={NodeNamesDetail(settings.enabledBuyOnlyTradeCategoryKeys)}; " +
                $"goodwill={settings.commercialGoodwillIntervalDays}/" +
                $"{settings.commercialGoodwillPerInterval}/" +
                $"{settings.commercialGoodwillCeiling}/" +
                $"{settings.commercialReputationRequired}; minimumDays=" +
                $"{settings.minimumEmploymentDaysForGoodwill}; impact=" +
                $"{settings.employmentGoodwillImpact}";
        }

        private static void CheckEmployeeCardLayout(Results r)
        {
            // These are model-level checks only. They exercise the production height and
            // happiness functions, plus the window-local expansion state, but they cannot prove
            // screen geometry, click feel, readability, or any other visual property.
            const float rowWidth = 720f;
            const string collapsedLabel = "a collapsed employee card is shorter than an expanded one";
            const string positiveLabel = "every employee row height is positive and finite";
            const string sumLabel = "the rows' total height is the sum of the rows";
            const string happinessLabel =
                "the happiness word is one of the five bands, or Unmeasured";
            const string unmeasuredLabel = "an unmeasured mood never reports a band";
            const string expansionLabel = "expanding a card changes no contract state";

            MethodInfo employeeRowHeight = typeof(MainTabWindow_Intercolony).GetMethod(
                "EmployeeRowHeight", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo employeeRowsHeight = typeof(MainTabWindow_Intercolony).GetMethod(
                "EmployeeRowsHeight", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo employeeHappinessLine = typeof(MainTabWindow_Intercolony).GetMethod(
                "EmployeeHappinessLine", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo expansionField = typeof(MainTabWindow_Intercolony).GetField(
                "expandedEmployeeContractIds", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo drawEmployeeRow = typeof(MainTabWindow_Intercolony).GetMethod(
                "DrawEmployeeRow", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo expansionAdd = typeof(HashSet<int>).GetMethod(
                "Add", new[] { typeof(int) });
            MethodInfo expansionRemove = typeof(HashSet<int>).GetMethod(
                "Remove", new[] { typeof(int) });

            float collapsedHeight = 0f;
            float expandedHeight = 0f;
            float rowsHeight = 0f;
            float sumHeight = 0f;
            float rowDifference = 0f;
            float minimumRowHeight = 0f;
            float maximumRowHeight = 0f;
            float collapsedHeightAfterToggle = 0f;
            float expandedHeightAfterToggle = 0f;
            int rowCount = 0;
            int positiveRowCount = 0;
            int finiteRowCount = 0;
            int happinessSampleCount = 0;
            int unexpectedHappinessCount = 0;
            int persistedFieldCount = 0;
            int changedPersistedFieldCount = 0;
            bool collapsedIsShorter = false;
            bool allRowsPositiveAndFinite = false;
            bool rowsSumMatches = false;
            bool happinessWordsAllowed = false;
            bool unmeasuredMoodIsUnmeasured = false;
            bool expansionStateIsIsolated = false;
            bool collapsedBeforeToggle = false;
            bool expandedAfterFirstToggle = false;
            bool collapsedAfterSecondToggle = false;
            bool autoRenewBefore = false;
            bool autoRenewAfter = false;
            bool productionToggleUsesWindowState = false;
            bool productionDrawStoresContractField = false;
            string unmeasuredWord = null;
            string failure = null;
            StringBuilder rowHeightValues = new StringBuilder();
            StringBuilder happinessValues = new StringBuilder();
            StringBuilder changedPersistedFields = new StringBuilder();

            try
            {
                if (employeeRowHeight == null || employeeRowsHeight == null ||
                    employeeHappinessLine == null || expansionField == null)
                {
                    throw new InvalidOperationException(
                        "one or more employee-card private members were unavailable");
                }

                MainTabWindow_Intercolony window = new MainTabWindow_Intercolony();
                HashSet<int> expansionIds = expansionField.GetValue(window) as HashSet<int>;
                if (expansionIds == null)
                {
                    throw new InvalidOperationException(
                        "the employee expansion field was not a HashSet<int>");
                }

                int now = GenTicks.TicksGame;
                EmploymentContract openEnded = new EmploymentContract
                {
                    id = 161601,
                    settlementName = "Layout test settlement",
                    factionName = "",
                    workerName = "",
                    workerSkills = "",
                    dailyWage = 100,
                    termDays = 0,
                    combatClause = CombatClause.Civilian,
                    wageStructure = WageStructure.Daily,
                    nextPaymentTick = now + GenDate.TicksPerDay,
                    hiredTick = now,
                    arrivalTick = now,
                    arrivedTick = now,
                    status = EmploymentStatus.Active,
                    autoRenew = true
                };
                EmploymentContract servingNotice = new EmploymentContract
                {
                    id = 161602,
                    settlementName = "Notice test settlement",
                    factionName = "Notice test faction",
                    workerName = "Notice worker",
                    workerSkills = "Plants 8",
                    dailyWage = 120,
                    termDays = 0,
                    combatClause = CombatClause.Armed,
                    wageStructure = WageStructure.Daily,
                    nextPaymentTick = now + GenDate.TicksPerDay,
                    hiredTick = now,
                    arrivalTick = now,
                    arrivedTick = now,
                    noticeEndTick = now + GenDate.TicksPerDay * 2,
                    status = EmploymentStatus.Active,
                    autoRenew = true
                };
                EmploymentContract prepaidWithBond = new EmploymentContract
                {
                    id = 161603,
                    settlementName = "Prepaid test settlement",
                    factionName = "Prepaid test faction",
                    workerName = "Prepaid worker",
                    workerSkills = "Construction 10",
                    dailyWage = 180,
                    termDays = 30,
                    endTick = now + GenDate.TicksPerDay * 30,
                    combatClause = CombatClause.Security,
                    wageStructure = WageStructure.Prepaid,
                    arrivedEquipment = new List<EmploymentEquipmentRecord>(),
                    equipmentBond = 500,
                    hiredTick = now,
                    arrivalTick = now,
                    arrivedTick = now,
                    status = EmploymentStatus.Active,
                    autoRenew = true
                };
                EmploymentContract dailyWithoutBond = new EmploymentContract
                {
                    id = 161604,
                    settlementName = "Daily test settlement",
                    factionName = "Daily test faction",
                    workerName = "A deliberately long worker name for row measurement",
                    workerSkills = "Mining 6",
                    dailyWage = 75,
                    termDays = 14,
                    endTick = now + GenDate.TicksPerDay * 14,
                    combatClause = CombatClause.Civilian,
                    wageStructure = WageStructure.Daily,
                    nextPaymentTick = now + GenDate.TicksPerDay,
                    hiredTick = now,
                    arrivalTick = now,
                    arrivedTick = now,
                    status = EmploymentStatus.Active,
                    autoRenew = false
                };
                List<EmploymentContract> contracts = new List<EmploymentContract>
                {
                    openEnded,
                    servingNotice,
                    prepaidWithBond,
                    dailyWithoutBond
                };
                rowCount = contracts.Count;

                // A single contract is measured through the real height function in both states;
                // no copy of EmployeeRowLayout's arithmetic is used here.
                collapsedHeight = (float)employeeRowHeight.Invoke(
                    window, new object[] { rowWidth, prepaidWithBond });
                expansionIds.Add(prepaidWithBond.id);
                expandedHeight = (float)employeeRowHeight.Invoke(
                    window, new object[] { rowWidth, prepaidWithBond });
                expansionIds.Remove(prepaidWithBond.id);
                collapsedIsShorter = expandedHeight > collapsedHeight;

                bool rowsPositiveAndFinite = true;
                for (int i = 0; i < contracts.Count; i++)
                {
                    float height = (float)employeeRowHeight.Invoke(
                        window, new object[] { rowWidth, contracts[i] });
                    if (i > 0)
                    {
                        rowHeightValues.Append(", ");
                    }

                    rowHeightValues.Append(height.ToString("0.###"));
                    if (height > 0f)
                    {
                        positiveRowCount++;
                    }

                    if (!float.IsNaN(height) && !float.IsInfinity(height))
                    {
                        finiteRowCount++;
                    }

                    if (height <= 0f || float.IsNaN(height) || float.IsInfinity(height))
                    {
                        rowsPositiveAndFinite = false;
                    }

                    if (i == 0 || height < minimumRowHeight)
                    {
                        minimumRowHeight = height;
                    }

                    if (i == 0 || height > maximumRowHeight)
                    {
                        maximumRowHeight = height;
                    }
                }

                allRowsPositiveAndFinite = rowsPositiveAndFinite &&
                    positiveRowCount == rowCount && finiteRowCount == rowCount;

                rowsHeight = (float)employeeRowsHeight.Invoke(
                    window, new object[] { contracts, rowWidth });
                for (int i = 0; i < contracts.Count; i++)
                {
                    sumHeight += (float)employeeRowHeight.Invoke(
                        window, new object[] { rowWidth, contracts[i] });
                }

                rowDifference = rowsHeight - sumHeight;
                rowsSumMatches = rowsHeight == sumHeight;

                EmploymentContract moodContract = new EmploymentContract
                {
                    id = 161605,
                    workerName = "Mood probe",
                    workerSkills = "none",
                    factionName = "Mood test faction",
                    dailyWage = 1,
                    termDays = 30,
                    endTick = now + GenDate.TicksPerDay * 30,
                    status = EmploymentStatus.Active,
                    moodSampleCount = 1
                };
                HashSet<string> allowedHappinessWords = new HashSet<string>(StringComparer.Ordinal)
                {
                    "Miserable",
                    "Unhappy",
                    "Content",
                    "Happy",
                    "Excellent",
                    "Unmeasured"
                };
                for (int step = 0; step <= 20; step++)
                {
                    float averageMood = step / 20f;
                    moodContract.moodSampleTotal = averageMood;
                    string word = (string)employeeHappinessLine.Invoke(
                        null, new object[] { moodContract });
                    happinessSampleCount++;
                    if (step > 0)
                    {
                        happinessValues.Append(", ");
                    }

                    happinessValues.Append($"{averageMood:0.00}={word ?? "<null>"}");
                    if (!allowedHappinessWords.Contains(word))
                    {
                        unexpectedHappinessCount++;
                    }
                }

                happinessWordsAllowed = happinessSampleCount == 21 &&
                    unexpectedHappinessCount == 0;

                moodContract.moodSampleTotal = 1f;
                moodContract.moodSampleCount = 0;
                unmeasuredWord = (string)employeeHappinessLine.Invoke(
                    null, new object[] { moodContract });
                unmeasuredMoodIsUnmeasured = unmeasuredWord == "Unmeasured";

                // The expansion event needs a screen to synthesize, so drive the same window-local
                // set that DrawEmployeeRow owns. Snapshot every EmploymentContract instance field
                // (including autoRenew and the private Scribe-backed dictionary) before doing so.
                FieldInfo[] persistedFields = typeof(EmploymentContract).GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                Dictionary<string, object> persistedBefore =
                    new Dictionary<string, object>(StringComparer.Ordinal);
                for (int i = 0; i < persistedFields.Length; i++)
                {
                    persistedBefore[persistedFields[i].Name] =
                        persistedFields[i].GetValue(prepaidWithBond);
                }

                expansionIds.Remove(prepaidWithBond.id);
                collapsedBeforeToggle = !expansionIds.Contains(prepaidWithBond.id);
                autoRenewBefore = prepaidWithBond.autoRenew;
                expansionIds.Add(prepaidWithBond.id);
                expandedAfterFirstToggle = expansionIds.Contains(prepaidWithBond.id);
                expandedHeightAfterToggle = (float)employeeRowHeight.Invoke(
                    window, new object[] { rowWidth, prepaidWithBond });
                expansionIds.Remove(prepaidWithBond.id);
                collapsedAfterSecondToggle = !expansionIds.Contains(prepaidWithBond.id);
                collapsedHeightAfterToggle = (float)employeeRowHeight.Invoke(
                    window, new object[] { rowWidth, prepaidWithBond });
                autoRenewAfter = prepaidWithBond.autoRenew;

                for (int i = 0; i < persistedFields.Length; i++)
                {
                    FieldInfo field = persistedFields[i];
                    object before = persistedBefore[field.Name];
                    object after = field.GetValue(prepaidWithBond);
                    if (object.Equals(before, after))
                    {
                        continue;
                    }

                    changedPersistedFieldCount++;
                    if (changedPersistedFields.Length > 0)
                    {
                        changedPersistedFields.Append(", ");
                    }

                    changedPersistedFields.Append(field.Name);
                }

                persistedFieldCount = persistedFields.Length;
                productionToggleUsesWindowState = CallsMethod(drawEmployeeRow, expansionAdd) &&
                    CallsMethod(drawEmployeeRow, expansionRemove);

                // A direct contract assignment in the expansion branch would be an accidental
                // persisted-state write. Detect that model-level regression without drawing a row.
                byte[] drawEmployeeRowIl = drawEmployeeRow?.GetMethodBody()?.GetILAsByteArray();
                if (drawEmployeeRowIl != null)
                {
                    for (int i = 0; i + 4 < drawEmployeeRowIl.Length; i++)
                    {
                        if (drawEmployeeRowIl[i] != 0x7D)
                        {
                            continue;
                        }

                        int token = drawEmployeeRowIl[i + 1] |
                            (drawEmployeeRowIl[i + 2] << 8) |
                            (drawEmployeeRowIl[i + 3] << 16) |
                            (drawEmployeeRowIl[i + 4] << 24);
                        try
                        {
                            FieldInfo storedField = drawEmployeeRow.Module.ResolveField(token);
                            if (storedField?.DeclaringType == typeof(EmploymentContract))
                            {
                                productionDrawStoresContractField = true;
                                break;
                            }
                        }
                        catch (Exception)
                        {
                            // An unrelated IL token is not evidence of a contract write.
                        }
                    }
                }

                expansionStateIsIsolated = collapsedBeforeToggle &&
                    expandedAfterFirstToggle && collapsedAfterSecondToggle &&
                    changedPersistedFieldCount == 0 && autoRenewBefore == autoRenewAfter &&
                    productionToggleUsesWindowState && !productionDrawStoresContractField;
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
            }

            string failureDetail = failure == null ? "" : $"; failure={failure}";
            r.Check(
                collapsedIsShorter && failure == null,
                collapsedLabel,
                $"collapsed {collapsedHeight:0.###}, expanded {expandedHeight:0.###}, " +
                $"delta {expandedHeight - collapsedHeight:0.###}{failureDetail}");
            r.Check(
                allRowsPositiveAndFinite && failure == null,
                positiveLabel,
                $"rows {positiveRowCount}/{rowCount} positive, finite {finiteRowCount}/{rowCount}, " +
                $"min {minimumRowHeight:0.###}, max {maximumRowHeight:0.###}, " +
                $"heights [{rowHeightValues}]{failureDetail}");
            r.Check(
                rowsSumMatches && failure == null,
                sumLabel,
                $"rows total {rowsHeight:0.###}, summed rows {sumHeight:0.###}, " +
                $"difference {rowDifference:0.######}, count {rowCount}{failureDetail}");
            r.Check(
                happinessWordsAllowed && failure == null,
                happinessLabel,
                $"samples {happinessSampleCount}, unexpected {unexpectedHappinessCount}, " +
                $"observed [{happinessValues}]{failureDetail}");
            r.Check(
                unmeasuredMoodIsUnmeasured && failure == null,
                unmeasuredLabel,
                $"mood total 1, samples 0, observed {unmeasuredWord ?? "<null>"}{failureDetail}");
            r.Check(
                expansionStateIsIsolated && failure == null,
                expansionLabel,
                $"heights {collapsedHeightAfterToggle:0.###} -> " +
                $"{expandedHeightAfterToggle:0.###} -> {collapsedHeightAfterToggle:0.###}; " +
                $"collapsed before {collapsedBeforeToggle}, expanded after first " +
                $"{expandedAfterFirstToggle}, collapsed after second {collapsedAfterSecondToggle}; " +
                $"persisted fields {persistedFieldCount}, changed {changedPersistedFieldCount}" +
                $" [{changedPersistedFields}], autoRenew {autoRenewBefore} -> {autoRenewAfter}; " +
                $"draw uses window Add/Remove {productionToggleUsesWindowState}, " +
                $"direct contract field store {productionDrawStoresContractField}{failureDetail}");
        }

        private static void CheckEmployeeCardLifecycle(Results r)
        {
            int now = GenTicks.TicksGame;
            EmployeeCardFixture ordinary = new EmployeeCardFixture(
                "ordinary active fixed-term",
                BuildEmployeeCardFixtureContract(161701, "Ordinary worker", EmploymentStatus.Active,
                    30, 0, now));
            EmployeeCardFixture travelling = new EmployeeCardFixture(
                "travelling",
                BuildEmployeeCardFixtureContract(161702, "Travelling worker",
                    EmploymentStatus.Travelling, 30, 0, now));
            EmployeeCardFixture liveRenewal = new EmployeeCardFixture(
                "live renewal offer",
                BuildEmployeeCardFixtureContract(161703, "Renewal worker", EmploymentStatus.Active,
                    30, 0, now));
            liveRenewal.contract.renewalOffered = true;
            liveRenewal.contract.renewalWage = 125;

            EmployeeCardFixture liveTransition = new EmployeeCardFixture(
                "live permanent-transition offer",
                BuildEmployeeCardFixtureContract(161704, "Transition worker",
                    EmploymentStatus.Active, 30, 0, now));
            liveTransition.contract.transitionOffered = true;

            EmployeeCardFixture arrears = new EmployeeCardFixture(
                "arrears",
                BuildEmployeeCardFixtureContract(161705, "Arrears worker", EmploymentStatus.Active,
                    30, 37, now));
            EmployeeCardFixture openEnded = new EmployeeCardFixture(
                "open-ended active",
                BuildEmployeeCardFixtureContract(161706, "Open-ended worker", EmploymentStatus.Active,
                    0, 0, now));

            EmployeeCardFixture[] fixtures =
            {
                ordinary,
                travelling,
                liveRenewal,
                liveTransition,
                arrears,
                openEnded
            };

            MainTabWindow_Intercolony.EmployeeLifecycleActionKind ordinaryHeader =
                MainTabWindow_Intercolony.ResolveLifecycleAction(ordinary.contract);
            string ordinaryHeaderLabel = MainTabWindow_Intercolony.LifecycleActionLabel(
                ordinaryHeader);
            r.Check(
                ordinaryHeader == MainTabWindow_Intercolony.EmployeeLifecycleActionKind.Dismiss &&
                ordinaryHeaderLabel == "Dismiss",
                "A1 ordinary active fixed-term header action is Dismiss",
                $"ResolveLifecycleAction -> {ordinaryHeader}, label " +
                $"\"{ordinaryHeaderLabel ?? "<null>"}\"; EXPECTED Dismiss");

            MainTabWindow_Intercolony.EmployeeLifecycleActionKind travellingHeader =
                MainTabWindow_Intercolony.ResolveLifecycleAction(travelling.contract);
            string travellingHeaderLabel = MainTabWindow_Intercolony.LifecycleActionLabel(
                travellingHeader);
            r.Check(
                travellingHeader == MainTabWindow_Intercolony.EmployeeLifecycleActionKind.Cancel &&
                travellingHeaderLabel == "Cancel",
                "A2 travelling header action is Cancel",
                $"ResolveLifecycleAction -> {travellingHeader}, label " +
                $"\"{travellingHeaderLabel ?? "<null>"}\"; EXPECTED Cancel");

            // These existing terminal/null resolver checks remain independent of the six card
            // fixtures and continue to observe the real one-argument resolver.
            EmploymentContract severedContract = new EmploymentContract
            {
                status = EmploymentStatus.Severed
            };
            MainTabWindow_Intercolony.EmployeeLifecycleActionKind severedObserved =
                MainTabWindow_Intercolony.ResolveLifecycleAction(severedContract);
            r.Check(
                severedObserved == MainTabWindow_Intercolony.EmployeeLifecycleActionKind.None,
                "severed with no offers has no action",
                $"OBSERVED {severedObserved}; EXPECTED " +
                $"{MainTabWindow_Intercolony.EmployeeLifecycleActionKind.None}");

            MainTabWindow_Intercolony.EmployeeLifecycleActionKind nullObserved =
                MainTabWindow_Intercolony.ResolveLifecycleAction(null);
            r.Check(
                nullObserved == MainTabWindow_Intercolony.EmployeeLifecycleActionKind.None,
                "null contract has no action",
                $"OBSERVED {nullObserved}; EXPECTED " +
                $"{MainTabWindow_Intercolony.EmployeeLifecycleActionKind.None}");

            MethodInfo employeeActionDefinitionsFor = typeof(MainTabWindow_Intercolony).GetMethod(
                "EmployeeActionDefinitionsFor",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                new[] { typeof(EmploymentContract) },
                null);
            Type layoutType = typeof(MainTabWindow_Intercolony).GetNestedType(
                "EmployeeRowLayout", BindingFlags.NonPublic);
            MethodInfo layoutFor = layoutType?.GetMethod(
                "For",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(Rect), typeof(EmploymentContract), typeof(bool), typeof(bool) },
                null);
            MethodInfo drawEmployeeActionStack = typeof(MainTabWindow_Intercolony).GetMethod(
                "DrawEmployeeActionStack",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { layoutType, typeof(EmploymentContract), typeof(bool), typeof(bool) },
                null);
            MethodInfo drawEmployeeActionButton = typeof(MainTabWindow_Intercolony).GetMethod(
                "DrawEmployeeActionButton",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                new[] { typeof(Rect), typeof(string), typeof(bool), typeof(string),
                    typeof(System.Action) },
                null);
            FieldInfo layoutDefinitionsField = layoutType?.GetField(
                "actionDefinitions", BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic);
            FieldInfo actionStackField = layoutType?.GetField(
                "actionStack", BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic);
            FieldInfo layoutHeightField = layoutType?.GetField(
                "height", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            MethodInfo renewalHasLiveOffer = typeof(RenewalService).GetMethod(
                "HasLiveOffer", BindingFlags.Static | BindingFlags.Public,
                null, new[] { typeof(EmploymentContract) }, null);
            MethodInfo transitionHasLiveOffer = typeof(TransitionService).GetMethod(
                "HasLiveOffer", BindingFlags.Static | BindingFlags.Public,
                null, new[] { typeof(EmploymentContract) }, null);
            MethodInfo renewalAccept = typeof(RenewalService).GetMethod(
                "Accept", BindingFlags.Static | BindingFlags.Public,
                null, new[] { typeof(EmploymentContract), typeof(string).MakeByRefType() }, null);
            MethodInfo renewalDecline = typeof(RenewalService).GetMethod(
                "Decline", BindingFlags.Static | BindingFlags.Public,
                null, new[] { typeof(EmploymentContract) }, null);
            MethodInfo transitionDecline = typeof(TransitionService).GetMethod(
                "Decline", BindingFlags.Static | BindingFlags.Public,
                null, new[] { typeof(EmploymentContract) }, null);
            MethodInfo payArrears = typeof(PayrollService).GetMethod(
                "TryPayArrears", BindingFlags.Static | BindingFlags.Public,
                null, new[] { typeof(EmploymentContract), typeof(Map),
                    typeof(string).MakeByRefType() }, null);
            MethodInfo openTransitionDialog = typeof(MainTabWindow_Intercolony).GetMethod(
                "OpenTransitionDialog", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(EmploymentContract) }, null);
            MethodInfo drawEmployeeRow = typeof(MainTabWindow_Intercolony).GetMethod(
                "DrawEmployeeRow", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(Rect), typeof(EmploymentContract), typeof(int) }, null);

            Array[] definitions = new Array[fixtures.Length];
            Array[] layoutDefinitions = new Array[fixtures.Length];
            object[] layouts = new object[fixtures.Length];
            List<EmployeeActionObservation>[] observations =
                new List<EmployeeActionObservation>[fixtures.Length];
            bool[] hasLiveRenewalOffer = new bool[fixtures.Length];
            bool[] hasLiveTransitionOffer = new bool[fixtures.Length];
            string observationFailure = null;
            const float rowWidth = 720f;
            Rect rowRect = new Rect(0f, 0f, rowWidth, 0f);

            try
            {
                if (employeeActionDefinitionsFor == null || layoutType == null || layoutFor == null ||
                    drawEmployeeActionStack == null || drawEmployeeActionButton == null ||
                    layoutDefinitionsField == null || actionStackField == null ||
                    layoutHeightField == null || renewalHasLiveOffer == null ||
                    transitionHasLiveOffer == null || renewalAccept == null ||
                    renewalDecline == null || transitionDecline == null || payArrears == null ||
                    openTransitionDialog == null || drawEmployeeRow == null)
                {
                    throw new InvalidOperationException(
                        "one or more employee-card production members were unavailable");
                }

                MainTabWindow_Intercolony window = new MainTabWindow_Intercolony();
                HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(
                    "miannoni.intercolony.employee-card.selftest");
                MethodInfo captureButton = typeof(IntercolonyLaborSelfTest).GetMethod(
                    nameof(CaptureEmployeeActionButton),
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (captureButton == null)
                {
                    throw new InvalidOperationException(
                        "employee-card button capture method was unavailable");
                }

                try
                {
                    harmony.Patch(
                        drawEmployeeActionButton,
                        prefix: new HarmonyLib.HarmonyMethod(captureButton));

                    for (int i = 0; i < fixtures.Length; i++)
                    {
                        EmployeeCardFixture fixture = fixtures[i];
                        definitions[i] = (Array)employeeActionDefinitionsFor.Invoke(
                            null, new object[] { fixture.contract });
                        layouts[i] = layoutFor.Invoke(
                            null, new object[] { rowRect, fixture.contract, false, true });
                        layoutDefinitions[i] = layoutDefinitionsField.GetValue(layouts[i]) as Array;
                        hasLiveRenewalOffer[i] = RenewalService.HasLiveOffer(fixture.contract);
                        hasLiveTransitionOffer[i] = TransitionService.HasLiveOffer(fixture.contract);

                        employeeActionObservations = new List<EmployeeActionObservation>();
                        drawEmployeeActionStack.Invoke(
                            window,
                            new object[]
                            {
                                layouts[i],
                                fixture.contract,
                                hasLiveRenewalOffer[i],
                                hasLiveTransitionOffer[i]
                            });
                        observations[i] = employeeActionObservations;
                    }
                }
                finally
                {
                    try
                    {
                        harmony.Unpatch(
                            drawEmployeeActionButton,
                            HarmonyLib.HarmonyPatchType.Prefix,
                            harmony.Id);
                    }
                    finally
                    {
                        employeeActionObservations = null;
                    }
                }
            }
            catch (Exception ex)
            {
                observationFailure = $"{ex.GetType().Name}: {ex.Message}";
                employeeActionObservations = null;
            }

            bool productionReadsRenewalOffer = CallsMethod(drawEmployeeRow, renewalHasLiveOffer);
            bool productionReadsTransitionOffer = CallsMethod(drawEmployeeRow, transitionHasLiveOffer);

            EmployeeActionObservation renew = FindEmployeeAction(
                observations[2], "Renew", startsWith: false);
            EmployeeActionObservation letThemGo = FindEmployeeAction(
                observations[2], "Let them go", startsWith: false);
            bool renewalDefinitionsPresent = ContainsEmployeeAction(
                    definitions[2], "Renew") &&
                ContainsEmployeeAction(definitions[2], "LetThemGo") &&
                ContainsEmployeeAction(layoutDefinitions[2], "Renew") &&
                ContainsEmployeeAction(layoutDefinitions[2], "LetThemGo");
            bool renewalOfferIsLive = hasLiveRenewalOffer[2];
            bool renewalBindingsCorrect = renew != null && letThemGo != null &&
                renew.available == renewalOfferIsLive &&
                letThemGo.available == renewalOfferIsLive &&
                CallsMethod(renew.callback?.Method, renewalAccept) &&
                CallsMethod(letThemGo.callback?.Method, renewalDecline);

            // D3 negative control: changing Let them go to () => ConfirmDismiss(contract) must turn
            // this assertion red by removing the renewal-decline callback from this observed slot.
            r.Check(
                observationFailure == null && productionReadsRenewalOffer &&
                renewalDefinitionsPresent && renewalOfferIsLive && renewalBindingsCorrect,
                "A3 live renewal offer exposes enabled Renew and Let them go actions",
                $"definitions {DescribeEmployeeActions(definitions[2])}; " +
                $"Renew {DescribeEmployeeAction(renew)}; " +
                $"Let them go {DescribeEmployeeAction(letThemGo)}; " +
                $"RenewalService.HasLiveOffer={renewalOfferIsLive}; " +
                $"failure={observationFailure ?? "none"}");

            EmployeeActionObservation keepThem = FindEmployeeAction(
                observations[3], "Keep them", startsWith: false);
            EmployeeActionObservation notNowWithOffer = FindEmployeeAction(
                observations[3], "Not now", startsWith: false);
            bool transitionDefinitionsPresent = ContainsEmployeeAction(
                    definitions[3], "KeepThem") &&
                ContainsEmployeeAction(definitions[3], "NotNow") &&
                ContainsEmployeeAction(layoutDefinitions[3], "KeepThem") &&
                ContainsEmployeeAction(layoutDefinitions[3], "NotNow");
            bool transitionOfferIsLive = hasLiveTransitionOffer[3];
            bool transitionBindingsCorrect = keepThem != null && notNowWithOffer != null &&
                keepThem.available == transitionOfferIsLive &&
                notNowWithOffer.available == transitionOfferIsLive &&
                CallsMethod(keepThem.callback?.Method, openTransitionDialog) &&
                CallsMethod(notNowWithOffer.callback?.Method, transitionDecline);

            // D3 negative control: changing Not now to () => RenewalService.Decline(contract) must
            // turn this assertion red because the transition-decline callback disappears.
            r.Check(
                observationFailure == null && productionReadsTransitionOffer &&
                transitionDefinitionsPresent && transitionOfferIsLive &&
                transitionBindingsCorrect,
                "A4 live permanent-transition offer exposes Keep them and enabled Not now",
                $"definitions {DescribeEmployeeActions(definitions[3])}; " +
                $"Keep them {DescribeEmployeeAction(keepThem)}; " +
                $"Not now {DescribeEmployeeAction(notNowWithOffer)}; " +
                $"TransitionService.HasLiveOffer={transitionOfferIsLive}; " +
                $"failure={observationFailure ?? "none"}");

            EmployeeActionObservation notNowWithoutOffer = FindEmployeeAction(
                observations[0], "Not now", startsWith: false);
            bool noTransitionOffer = !hasLiveTransitionOffer[0];

            // D3 negative control: the same Not now -> RenewalService.Decline mutation must also
            // fail this no-offer assertion because the observed disabled action loses its real
            // transition-decline service binding.
            r.Check(
                observationFailure == null && noTransitionOffer && notNowWithoutOffer != null &&
                !notNowWithoutOffer.available &&
                CallsMethod(notNowWithoutOffer.callback?.Method, transitionDecline),
                "A5 without a transition offer Not now is present but disabled",
                $"Not now {DescribeEmployeeAction(notNowWithoutOffer)}; " +
                $"TransitionService.HasLiveOffer={hasLiveTransitionOffer[0]}; " +
                $"failure={observationFailure ?? "none"}");

            bool everyFixtureHasDisabledNegotiation = observationFailure == null;
            StringBuilder negotiationDetails = new StringBuilder();
            for (int i = 0; i < fixtures.Length; i++)
            {
                EmployeeActionObservation negotiate = FindEmployeeAction(
                    observations[i], "Negotiate", startsWith: false);
                if (i > 0)
                {
                    negotiationDetails.Append("; ");
                }

                negotiationDetails.Append(fixtures[i].label).Append("=")
                    .Append(DescribeEmployeeAction(negotiate));
                if (negotiate == null || negotiate.available || negotiate.callback != null)
                {
                    everyFixtureHasDisabledNegotiation = false;
                }
            }

            r.Check(
                everyFixtureHasDisabledNegotiation,
                "A6 Negotiate is present and always disabled",
                negotiationDetails.ToString());

            bool payArrearsOnlyForOutstandingDebt = observationFailure == null;
            bool arrearsPayrollBinding = false;
            StringBuilder arrearsDetails = new StringBuilder();
            for (int i = 0; i < fixtures.Length; i++)
            {
                bool hasOutstandingArrears = fixtures[i].contract.arrearsSilver > 0;
                bool definitionPresent = ContainsEmployeeAction(
                    definitions[i], "PayArrears");
                EmployeeActionObservation pay = FindEmployeeAction(
                    observations[i], "Pay arrears (", startsWith: true);
                bool observedPresent = pay != null;
                if (i > 0)
                {
                    arrearsDetails.Append("; ");
                }

                arrearsDetails.Append(fixtures[i].label).Append("=")
                    .Append(observedPresent ? pay.label : "absent");
                if (definitionPresent != hasOutstandingArrears ||
                    observedPresent != hasOutstandingArrears)
                {
                    payArrearsOnlyForOutstandingDebt = false;
                }

                if (i == 4)
                {
                    arrearsPayrollBinding = pay != null && pay.available &&
                        CallsMethod(pay.callback?.Method, payArrears);
                }
            }

            r.Check(
                payArrearsOnlyForOutstandingDebt && arrearsPayrollBinding,
                "A7 Pay arrears appears only with outstanding arrears and invokes PayrollService.TryPayArrears",
                $"{arrearsDetails}; payroll binding={arrearsPayrollBinding}; " +
                $"failure={observationFailure ?? "none"}");

            bool everyActionLayoutFits = observationFailure == null;
            StringBuilder layoutDetails = new StringBuilder();
            for (int i = 0; i < fixtures.Length; i++)
            {
                string fixtureLayoutDetail = "<layout missing>";
                bool fits = layouts[i] != null &&
                    EmployeeActionSlotsFit(
                        layouts[i], layoutDefinitions[i], actionStackField, layoutHeightField,
                        rowRect, out fixtureLayoutDetail);
                if (i > 0)
                {
                    layoutDetails.Append("; ");
                }

                layoutDetails.Append(fixtures[i].label).Append("=")
                    .Append(fixtureLayoutDetail);
                if (!fits)
                {
                    everyActionLayoutFits = false;
                }
            }

            r.Check(
                everyActionLayoutFits,
                "A8 every expanded action slot is disjoint from its neighbours and inside the reported row height",
                $"{layoutDetails}; failure={observationFailure ?? "none"}");
        }

        private static EmploymentContract BuildEmployeeCardFixtureContract(
            int id, string workerName, EmploymentStatus status, int termDays,
            int arrearsSilver, int now)
        {
            return new EmploymentContract
            {
                id = id,
                settlementName = "Employee-card fixture settlement",
                factionName = "Employee-card fixture faction",
                workerName = workerName,
                workerSkills = "Construction 8",
                dailyWage = 100,
                termDays = termDays,
                combatClause = CombatClause.Civilian,
                wageStructure = WageStructure.Daily,
                nextPaymentTick = now + GenDate.TicksPerDay,
                hiredTick = now,
                arrivalTick = status == EmploymentStatus.Travelling
                    ? now + GenDate.TicksPerDay
                    : now,
                arrivedTick = status == EmploymentStatus.Travelling
                    ? EmploymentContract.NotArrived
                    : now,
                endTick = termDays > 0 ? now + termDays * GenDate.TicksPerDay : -1,
                status = status,
                arrearsSilver = arrearsSilver
            };
        }

        private static bool CaptureEmployeeActionButton(
            Rect rect, string label, bool available, string tooltip, System.Action action)
        {
            if (employeeActionObservations != null)
            {
                employeeActionObservations.Add(new EmployeeActionObservation
                {
                    rect = rect,
                    label = label,
                    available = available,
                    tooltip = tooltip,
                    callback = action
                });
            }

            // The real DrawEmployeeActionStack has already supplied the label, enable condition,
            // tooltip, and callback. Skip Widgets.ButtonText so this observation does not need a
            // live GUI event or invoke any mutating service callback.
            return false;
        }

        private static EmployeeActionObservation FindEmployeeAction(
            List<EmployeeActionObservation> observations, string label, bool startsWith)
        {
            if (observations == null)
            {
                return null;
            }

            for (int i = 0; i < observations.Count; i++)
            {
                EmployeeActionObservation observation = observations[i];
                if (startsWith
                    ? observation.label?.StartsWith(label, StringComparison.Ordinal) == true
                    : observation.label == label)
                {
                    return observation;
                }
            }

            return null;
        }

        private static bool ContainsEmployeeAction(Array definitions, string kindName)
        {
            if (definitions == null)
            {
                return false;
            }

            FieldInfo kindField = definitions.GetType().GetElementType()?.GetField(
                "kind", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (kindField == null)
            {
                return false;
            }

            for (int i = 0; i < definitions.Length; i++)
            {
                object definition = definitions.GetValue(i);
                object kind = kindField.GetValue(definition);
                if (kind?.ToString() == kindName)
                {
                    return true;
                }
            }

            return false;
        }

        private static string DescribeEmployeeActions(Array definitions)
        {
            if (definitions == null)
            {
                return "<missing>";
            }

            FieldInfo kindField = definitions.GetType().GetElementType()?.GetField(
                "kind", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (kindField == null)
            {
                return "<kind field missing>";
            }

            StringBuilder result = new StringBuilder();
            for (int i = 0; i < definitions.Length; i++)
            {
                if (i > 0)
                {
                    result.Append(", ");
                }

                result.Append(kindField.GetValue(definitions.GetValue(i)));
            }

            return result.ToString();
        }

        private static string DescribeEmployeeAction(EmployeeActionObservation observation)
        {
            if (observation == null)
            {
                return "<missing>";
            }

            return $"label=\"{observation.label ?? "<null>"}\", " +
                $"available={observation.available}, " +
                $"callback={observation.callback?.Method?.Name ?? "<null>"}";
        }

        private static bool EmployeeActionSlotsFit(
            object layout, Array definitions, FieldInfo actionStackField,
            FieldInfo layoutHeightField, Rect rowRect, out string detail)
        {
            detail = "<unmeasured>";
            if (layout == null || definitions == null || actionStackField == null ||
                layoutHeightField == null)
            {
                detail = "layout, definitions, action stack, or height was unavailable";
                return false;
            }

            Rect[] slots = actionStackField.GetValue(layout) as Rect[];
            if (slots == null)
            {
                detail = "action stack was unavailable";
                return false;
            }

            float rowHeight = (float)layoutHeightField.GetValue(layout);
            bool fits = slots.Length == definitions.Length;
            int overlapCount = 0;
            int outsideCount = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                Rect slot = slots[i];
                if (slot.y < rowRect.y || slot.y + slot.height > rowRect.y + rowHeight)
                {
                    outsideCount++;
                    fits = false;
                }

                for (int j = i + 1; j < slots.Length; j++)
                {
                    if (!EmployeeRectsDisjoint(slot, slots[j]))
                    {
                        overlapCount++;
                        fits = false;
                    }
                }
            }

            detail = $"slots={slots.Length}, definitions={definitions.Length}, " +
                $"rowHeight={rowHeight:0.###}, overlaps={overlapCount}, outside={outsideCount}";
            return fits;
        }

        private static bool EmployeeRectsDisjoint(Rect first, Rect second)
        {
            bool separatedHorizontally = first.x + first.width <= second.x ||
                second.x + second.width <= first.x;
            bool separatedVertically = first.y + first.height <= second.y ||
                second.y + second.height <= first.y;
            return separatedHorizontally || separatedVertically;
        }

        private static void CheckAutoRenewPersistence(Results r)
        {
            EmploymentContract enabled = RoundTripAutoRenew(
                true, "intercolony-labor-auto-renew-on",
                out string enabledFailure, out bool enabledNodePresent);
            EmploymentContract disabled = RoundTripAutoRenew(
                false, "intercolony-labor-auto-renew-off",
                out string disabledFailure, out bool disabledNodePresent);

            r.Check(
                enabledFailure == null && enabledNodePresent &&
                enabled != null && enabled.autoRenew,
                "auto-renew=true survives an EmploymentContract save/load",
                $"saved true, node present {enabledNodePresent}, loaded " +
                $"{(enabled == null ? "missing" : enabled.autoRenew.ToString())}; " +
                $"failure={enabledFailure ?? "none"}");
            r.Check(
                disabledFailure == null && !disabledNodePresent &&
                disabled != null && !disabled.autoRenew,
                "auto-renew=false survives an EmploymentContract save/load",
                $"saved false, node present {disabledNodePresent}, loaded " +
                $"{(disabled == null ? "missing" : disabled.autoRenew.ToString())}; " +
                $"failure={disabledFailure ?? "none"}");
        }

        private static void CheckLaborSpineRoundTrip(Results r)
        {
            const string contractRoundTripLabel =
                "an employment contract's apparel consent and arrival transport survive a save";
            const string contractLegacyLabel =
                "a contract saved before this feature loads as Pending and Conventional";
            const string contractDefaultsLabel =
                "a default contract writes no apparel-consent or arrival-transport node";
            const string equipmentRoundTripLabel =
                "an equipment record's bought-out quantity survives a save, and reduces the refundable part";
            const string equipmentLegacyLabel =
                "an equipment record saved before this feature is fully refundable";
            const string postingRoundTripLabel =
                "a job posting's requested equipment level survives, and an old posting is Any";

            EmploymentContract loadedContract = null;
            bool contractApparelNodePresent = false;
            bool contractTransportNodePresent = false;
            string contractFailure = null;
            string contractPath = Path.Combine(
                Path.GetTempPath(), $"Intercolony-LaborSpine-Contract-{Guid.NewGuid():N}.xml");
            try
            {
                if (Scribe.saver == null || Scribe.loader == null)
                {
                    contractFailure = "vanilla Scribe saver or loader was unavailable";
                }
                else
                {
                    EmploymentContract savedContract = new EmploymentContract
                    {
                        apparelBondDecision = ApparelBondDecision.Allowed,
                        arrivalTransport = EmploymentArrivalTransport.DropPod
                    };

                    Scribe.saver.InitSaving(contractPath, "intercolony-labor-spine-contract");
                    Scribe_Deep.Look(ref savedContract, "contract");
                    Scribe.saver.FinalizeSaving();

                    string xml = File.ReadAllText(contractPath);
                    contractApparelNodePresent =
                        xml.IndexOf("<apparelBondDecision", StringComparison.Ordinal) >= 0;
                    contractTransportNodePresent =
                        xml.IndexOf("<arrivalTransport", StringComparison.Ordinal) >= 0;

                    Scribe.loader.InitLoading(contractPath);
                    Scribe_Deep.Look(ref loadedContract, "contract");
                    Scribe.loader.FinalizeLoading();
                }
            }
            catch (Exception ex)
            {
                contractFailure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                try
                {
                    Scribe.ForceStop();
                }
                catch (Exception ex)
                {
                    if (contractFailure == null)
                    {
                        contractFailure =
                            $"Scribe cleanup {ex.GetType().Name}: {ex.Message}";
                    }
                }

                try
                {
                    if (File.Exists(contractPath))
                    {
                        File.Delete(contractPath);
                    }
                }
                catch (Exception ex)
                {
                    if (contractFailure == null)
                    {
                        contractFailure =
                            $"temporary XML cleanup {ex.GetType().Name}: {ex.Message}";
                    }
                }
            }

            r.Check(
                contractFailure == null && contractApparelNodePresent &&
                contractTransportNodePresent && loadedContract != null &&
                loadedContract.apparelBondDecision == ApparelBondDecision.Allowed &&
                loadedContract.arrivalTransport == EmploymentArrivalTransport.DropPod,
                contractRoundTripLabel,
                $"loaded apparelBondDecision " +
                    $"{(loadedContract == null ? "missing" : loadedContract.apparelBondDecision.ToString())}; " +
                    $"arrivalTransport " +
                    $"{(loadedContract == null ? "missing" : loadedContract.arrivalTransport.ToString())}; " +
                    $"XML nodes apparelBondDecision " +
                    $"{(contractApparelNodePresent ? "present" : "absent")}, " +
                    $"arrivalTransport {(contractTransportNodePresent ? "present" : "absent")}; " +
                    $"failure={contractFailure ?? "none"}");

            EmploymentContract loadedLegacyContract = null;
            bool legacyContractApparelNodeBeforeStrip = false;
            bool legacyContractTransportNodeBeforeStrip = false;
            bool legacyContractApparelNodeAfterStrip = false;
            bool legacyContractTransportNodeAfterStrip = false;
            string legacyContractFailure = null;
            string legacyContractStripFailure = null;
            string legacyContractPath = Path.Combine(
                Path.GetTempPath(), $"Intercolony-LaborSpine-ContractLegacy-{Guid.NewGuid():N}.xml");
            try
            {
                if (Scribe.saver == null || Scribe.loader == null)
                {
                    legacyContractFailure = "vanilla Scribe saver or loader was unavailable";
                }
                else
                {
                    EmploymentContract savedContract = new EmploymentContract
                    {
                        apparelBondDecision = ApparelBondDecision.Allowed,
                        arrivalTransport = EmploymentArrivalTransport.DropPod
                    };

                    Scribe.saver.InitSaving(
                        legacyContractPath, "intercolony-labor-spine-contract-legacy");
                    Scribe_Deep.Look(ref savedContract, "contract");
                    Scribe.saver.FinalizeSaving();

                    string xml = File.ReadAllText(legacyContractPath);
                    legacyContractApparelNodeBeforeStrip =
                        xml.IndexOf("<apparelBondDecision", StringComparison.Ordinal) >= 0;
                    legacyContractTransportNodeBeforeStrip =
                        xml.IndexOf("<arrivalTransport", StringComparison.Ordinal) >= 0;
                    string[] nodeNames =
                    {
                        "apparelBondDecision",
                        "arrivalTransport"
                    };

                    for (int i = 0; i < nodeNames.Length; i++)
                    {
                        string nodeName = nodeNames[i];
                        string nodePrefix = "<" + nodeName;
                        int nodeStart = xml.IndexOf(
                            nodePrefix, StringComparison.Ordinal);
                        if (nodeStart < 0)
                        {
                            legacyContractStripFailure =
                                $"expected XML node <{nodeName}> was not present";
                            break;
                        }

                        int tagEnd = xml.IndexOf('>', nodeStart);
                        if (tagEnd < 0)
                        {
                            legacyContractStripFailure =
                                $"XML node <{nodeName}> had no closing angle bracket";
                            break;
                        }

                        int nodeEnd;
                        if (xml[tagEnd - 1] == '/')
                        {
                            nodeEnd = tagEnd + 1;
                        }
                        else
                        {
                            string closeTag = "</" + nodeName + ">";
                            int closeStart = xml.IndexOf(
                                closeTag, tagEnd + 1, StringComparison.Ordinal);
                            if (closeStart < 0)
                            {
                                legacyContractStripFailure =
                                    $"XML node <{nodeName}> had no closing tag";
                                break;
                            }

                            nodeEnd = closeStart + closeTag.Length;
                        }

                        xml = xml.Remove(nodeStart, nodeEnd - nodeStart);
                    }

                    legacyContractApparelNodeAfterStrip =
                        xml.IndexOf("<apparelBondDecision", StringComparison.Ordinal) >= 0;
                    legacyContractTransportNodeAfterStrip =
                        xml.IndexOf("<arrivalTransport", StringComparison.Ordinal) >= 0;
                    if (legacyContractStripFailure == null &&
                        (legacyContractApparelNodeAfterStrip || legacyContractTransportNodeAfterStrip))
                    {
                        legacyContractStripFailure =
                            "one or more expected XML nodes were still present after stripping";
                    }

                    if (legacyContractStripFailure == null)
                    {
                        File.WriteAllText(legacyContractPath, xml);
                        Scribe.loader.InitLoading(legacyContractPath);
                        Scribe_Deep.Look(ref loadedLegacyContract, "contract");
                        Scribe.loader.FinalizeLoading();
                    }
                }
            }
            catch (Exception ex)
            {
                legacyContractFailure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                try
                {
                    Scribe.ForceStop();
                }
                catch (Exception ex)
                {
                    if (legacyContractFailure == null)
                    {
                        legacyContractFailure =
                            $"Scribe cleanup {ex.GetType().Name}: {ex.Message}";
                    }
                }

                try
                {
                    if (File.Exists(legacyContractPath))
                    {
                        File.Delete(legacyContractPath);
                    }
                }
                catch (Exception ex)
                {
                    if (legacyContractFailure == null)
                    {
                        legacyContractFailure =
                            $"temporary XML cleanup {ex.GetType().Name}: {ex.Message}";
                    }
                }
            }

            r.Check(
                legacyContractFailure == null && legacyContractStripFailure == null &&
                legacyContractApparelNodeBeforeStrip && legacyContractTransportNodeBeforeStrip &&
                !legacyContractApparelNodeAfterStrip && !legacyContractTransportNodeAfterStrip &&
                loadedLegacyContract != null &&
                loadedLegacyContract.apparelBondDecision == ApparelBondDecision.Pending &&
                loadedLegacyContract.arrivalTransport == EmploymentArrivalTransport.Conventional,
                contractLegacyLabel,
                $"loaded apparelBondDecision " +
                    $"{(loadedLegacyContract == null ? "missing" : loadedLegacyContract.apparelBondDecision.ToString())}; " +
                    $"arrivalTransport " +
                    $"{(loadedLegacyContract == null ? "missing" : loadedLegacyContract.arrivalTransport.ToString())}; " +
                    $"XML before strip apparelBondDecision " +
                    $"{(legacyContractApparelNodeBeforeStrip ? "present" : "absent")}, " +
                    $"arrivalTransport " +
                    $"{(legacyContractTransportNodeBeforeStrip ? "present" : "absent")}; " +
                    $"after strip apparelBondDecision " +
                    $"{(legacyContractApparelNodeAfterStrip ? "present" : "absent")}, " +
                    $"arrivalTransport " +
                    $"{(legacyContractTransportNodeAfterStrip ? "present" : "absent")}; " +
                    $"stripFailure={legacyContractStripFailure ?? "none"}; " +
                    $"failure={legacyContractFailure ?? "none"}");

            bool defaultContractApparelNodePresent = false;
            bool defaultContractTransportNodePresent = false;
            string defaultContractFailure = null;
            string defaultContractPath = Path.Combine(
                Path.GetTempPath(), $"Intercolony-LaborSpine-ContractDefaults-{Guid.NewGuid():N}.xml");
            try
            {
                if (Scribe.saver == null)
                {
                    defaultContractFailure = "vanilla Scribe saver was unavailable";
                }
                else
                {
                    EmploymentContract savedContract = new EmploymentContract();
                    Scribe.saver.InitSaving(
                        defaultContractPath, "intercolony-labor-spine-contract-defaults");
                    Scribe_Deep.Look(ref savedContract, "contract");
                    Scribe.saver.FinalizeSaving();

                    string xml = File.ReadAllText(defaultContractPath);
                    defaultContractApparelNodePresent =
                        xml.IndexOf("<apparelBondDecision", StringComparison.Ordinal) >= 0;
                    defaultContractTransportNodePresent =
                        xml.IndexOf("<arrivalTransport", StringComparison.Ordinal) >= 0;
                }
            }
            catch (Exception ex)
            {
                defaultContractFailure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                try
                {
                    Scribe.ForceStop();
                }
                catch (Exception ex)
                {
                    if (defaultContractFailure == null)
                    {
                        defaultContractFailure =
                            $"Scribe cleanup {ex.GetType().Name}: {ex.Message}";
                    }
                }

                try
                {
                    if (File.Exists(defaultContractPath))
                    {
                        File.Delete(defaultContractPath);
                    }
                }
                catch (Exception ex)
                {
                    if (defaultContractFailure == null)
                    {
                        defaultContractFailure =
                            $"temporary XML cleanup {ex.GetType().Name}: {ex.Message}";
                    }
                }
            }

            r.Check(
                defaultContractFailure == null && !defaultContractApparelNodePresent &&
                !defaultContractTransportNodePresent,
                contractDefaultsLabel,
                $"XML nodes apparelBondDecision " +
                    $"{(defaultContractApparelNodePresent ? "present" : "absent")}, " +
                    $"arrivalTransport " +
                    $"{(defaultContractTransportNodePresent ? "present" : "absent")}; " +
                    $"failure={defaultContractFailure ?? "none"}");

            EmploymentEquipmentRecord loadedEquipmentRecord = null;
            bool equipmentBoughtOutNodePresent = false;
            string equipmentFailure = null;
            string equipmentPath = Path.Combine(
                Path.GetTempPath(), $"Intercolony-LaborSpine-Equipment-{Guid.NewGuid():N}.xml");
            try
            {
                if (Scribe.saver == null || Scribe.loader == null)
                {
                    equipmentFailure = "vanilla Scribe saver or loader was unavailable";
                }
                else
                {
                    EmploymentEquipmentRecord savedEquipmentRecord =
                        new EmploymentEquipmentRecord
                        {
                            quantity = 3,
                            boughtOutQuantity = 1
                        };

                    Scribe.saver.InitSaving(equipmentPath, "intercolony-labor-spine-equipment");
                    Scribe_Deep.Look(ref savedEquipmentRecord, "equipmentRecord");
                    Scribe.saver.FinalizeSaving();

                    string xml = File.ReadAllText(equipmentPath);
                    equipmentBoughtOutNodePresent =
                        xml.IndexOf("<boughtOutQuantity", StringComparison.Ordinal) >= 0;

                    Scribe.loader.InitLoading(equipmentPath);
                    Scribe_Deep.Look(ref loadedEquipmentRecord, "equipmentRecord");
                    Scribe.loader.FinalizeLoading();
                }
            }
            catch (Exception ex)
            {
                equipmentFailure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                try
                {
                    Scribe.ForceStop();
                }
                catch (Exception ex)
                {
                    if (equipmentFailure == null)
                    {
                        equipmentFailure =
                            $"Scribe cleanup {ex.GetType().Name}: {ex.Message}";
                    }
                }

                try
                {
                    if (File.Exists(equipmentPath))
                    {
                        File.Delete(equipmentPath);
                    }
                }
                catch (Exception ex)
                {
                    if (equipmentFailure == null)
                    {
                        equipmentFailure =
                            $"temporary XML cleanup {ex.GetType().Name}: {ex.Message}";
                    }
                }
            }

            r.Check(
                equipmentFailure == null && equipmentBoughtOutNodePresent &&
                loadedEquipmentRecord != null && loadedEquipmentRecord.quantity == 3 &&
                loadedEquipmentRecord.boughtOutQuantity == 1 &&
                loadedEquipmentRecord.RefundableQuantity == 2,
                equipmentRoundTripLabel,
                $"loaded quantity " +
                    $"{(loadedEquipmentRecord == null ? "missing" : loadedEquipmentRecord.quantity.ToString())}; " +
                    $"boughtOutQuantity " +
                    $"{(loadedEquipmentRecord == null ? "missing" : loadedEquipmentRecord.boughtOutQuantity.ToString())}; " +
                    $"RefundableQuantity " +
                    $"{(loadedEquipmentRecord == null ? "missing" : loadedEquipmentRecord.RefundableQuantity.ToString())}; " +
                    $"XML node boughtOutQuantity " +
                    $"{(equipmentBoughtOutNodePresent ? "present" : "absent")}; " +
                    $"failure={equipmentFailure ?? "none"}");

            EmploymentEquipmentRecord loadedLegacyEquipmentRecord = null;
            bool legacyEquipmentBoughtOutNodeBeforeStrip = false;
            bool legacyEquipmentBoughtOutNodeAfterStrip = false;
            string legacyEquipmentFailure = null;
            string legacyEquipmentStripFailure = null;
            string legacyEquipmentPath = Path.Combine(
                Path.GetTempPath(), $"Intercolony-LaborSpine-EquipmentLegacy-{Guid.NewGuid():N}.xml");
            try
            {
                if (Scribe.saver == null || Scribe.loader == null)
                {
                    legacyEquipmentFailure = "vanilla Scribe saver or loader was unavailable";
                }
                else
                {
                    EmploymentEquipmentRecord savedEquipmentRecord =
                        new EmploymentEquipmentRecord
                        {
                            quantity = 3,
                            boughtOutQuantity = 1
                        };

                    Scribe.saver.InitSaving(
                        legacyEquipmentPath, "intercolony-labor-spine-equipment-legacy");
                    Scribe_Deep.Look(ref savedEquipmentRecord, "equipmentRecord");
                    Scribe.saver.FinalizeSaving();

                    string xml = File.ReadAllText(legacyEquipmentPath);
                    legacyEquipmentBoughtOutNodeBeforeStrip =
                        xml.IndexOf("<boughtOutQuantity", StringComparison.Ordinal) >= 0;
                    string nodeName = "boughtOutQuantity";
                    string nodePrefix = "<" + nodeName;
                    int nodeStart = xml.IndexOf(nodePrefix, StringComparison.Ordinal);
                    if (nodeStart < 0)
                    {
                        legacyEquipmentStripFailure =
                            $"expected XML node <{nodeName}> was not present";
                    }
                    else
                    {
                        int tagEnd = xml.IndexOf('>', nodeStart);
                        if (tagEnd < 0)
                        {
                            legacyEquipmentStripFailure =
                                $"XML node <{nodeName}> had no closing angle bracket";
                        }
                        else
                        {
                            int nodeEnd;
                            if (xml[tagEnd - 1] == '/')
                            {
                                nodeEnd = tagEnd + 1;
                            }
                            else
                            {
                                string closeTag = "</" + nodeName + ">";
                                int closeStart = xml.IndexOf(
                                    closeTag, tagEnd + 1, StringComparison.Ordinal);
                                if (closeStart < 0)
                                {
                                    legacyEquipmentStripFailure =
                                        $"XML node <{nodeName}> had no closing tag";
                                    nodeEnd = -1;
                                }
                                else
                                {
                                    nodeEnd = closeStart + closeTag.Length;
                                }
                            }

                            if (legacyEquipmentStripFailure == null)
                            {
                                xml = xml.Remove(nodeStart, nodeEnd - nodeStart);
                            }
                        }
                    }

                    legacyEquipmentBoughtOutNodeAfterStrip =
                        xml.IndexOf("<boughtOutQuantity", StringComparison.Ordinal) >= 0;
                    if (legacyEquipmentStripFailure == null &&
                        legacyEquipmentBoughtOutNodeAfterStrip)
                    {
                        legacyEquipmentStripFailure =
                            "the <boughtOutQuantity> node was still present after stripping";
                    }

                    if (legacyEquipmentStripFailure == null)
                    {
                        File.WriteAllText(legacyEquipmentPath, xml);
                        Scribe.loader.InitLoading(legacyEquipmentPath);
                        Scribe_Deep.Look(
                            ref loadedLegacyEquipmentRecord, "equipmentRecord");
                        Scribe.loader.FinalizeLoading();
                    }
                }
            }
            catch (Exception ex)
            {
                legacyEquipmentFailure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                try
                {
                    Scribe.ForceStop();
                }
                catch (Exception ex)
                {
                    if (legacyEquipmentFailure == null)
                    {
                        legacyEquipmentFailure =
                            $"Scribe cleanup {ex.GetType().Name}: {ex.Message}";
                    }
                }

                try
                {
                    if (File.Exists(legacyEquipmentPath))
                    {
                        File.Delete(legacyEquipmentPath);
                    }
                }
                catch (Exception ex)
                {
                    if (legacyEquipmentFailure == null)
                    {
                        legacyEquipmentFailure =
                            $"temporary XML cleanup {ex.GetType().Name}: {ex.Message}";
                    }
                }
            }

            r.Check(
                legacyEquipmentFailure == null && legacyEquipmentStripFailure == null &&
                legacyEquipmentBoughtOutNodeBeforeStrip &&
                !legacyEquipmentBoughtOutNodeAfterStrip && loadedLegacyEquipmentRecord != null &&
                loadedLegacyEquipmentRecord.quantity == 3 &&
                loadedLegacyEquipmentRecord.boughtOutQuantity == 0 &&
                loadedLegacyEquipmentRecord.RefundableQuantity ==
                    loadedLegacyEquipmentRecord.quantity,
                equipmentLegacyLabel,
                $"loaded quantity " +
                    $"{(loadedLegacyEquipmentRecord == null ? "missing" : loadedLegacyEquipmentRecord.quantity.ToString())}; " +
                    $"boughtOutQuantity " +
                    $"{(loadedLegacyEquipmentRecord == null ? "missing" : loadedLegacyEquipmentRecord.boughtOutQuantity.ToString())}; " +
                    $"RefundableQuantity " +
                    $"{(loadedLegacyEquipmentRecord == null ? "missing" : loadedLegacyEquipmentRecord.RefundableQuantity.ToString())}; " +
                    $"XML before strip boughtOutQuantity " +
                    $"{(legacyEquipmentBoughtOutNodeBeforeStrip ? "present" : "absent")}; " +
                    $"after strip " +
                    $"{(legacyEquipmentBoughtOutNodeAfterStrip ? "present" : "absent")}; " +
                    $"stripFailure={legacyEquipmentStripFailure ?? "none"}; " +
                    $"failure={legacyEquipmentFailure ?? "none"}");

            JobPosting loadedPosting = null;
            bool postingNodePresent = false;
            string postingFailure = null;
            string postingPath = Path.Combine(
                Path.GetTempPath(), $"Intercolony-LaborSpine-Posting-{Guid.NewGuid():N}.xml");
            try
            {
                if (Scribe.saver == null || Scribe.loader == null)
                {
                    postingFailure = "vanilla Scribe saver or loader was unavailable";
                }
                else
                {
                    JobPosting savedPosting = new JobPosting
                    {
                        requestedEquipmentLevel = LaborEquipmentLevel.Elite
                    };

                    Scribe.saver.InitSaving(postingPath, "intercolony-labor-spine-posting");
                    Scribe_Deep.Look(ref savedPosting, "posting");
                    Scribe.saver.FinalizeSaving();

                    string xml = File.ReadAllText(postingPath);
                    postingNodePresent =
                        xml.IndexOf("<requestedEquipmentLevel", StringComparison.Ordinal) >= 0;

                    Scribe.loader.InitLoading(postingPath);
                    Scribe_Deep.Look(ref loadedPosting, "posting");
                    Scribe.loader.FinalizeLoading();
                }
            }
            catch (Exception ex)
            {
                postingFailure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                try
                {
                    Scribe.ForceStop();
                }
                catch (Exception ex)
                {
                    if (postingFailure == null)
                    {
                        postingFailure =
                            $"Scribe cleanup {ex.GetType().Name}: {ex.Message}";
                    }
                }

                try
                {
                    if (File.Exists(postingPath))
                    {
                        File.Delete(postingPath);
                    }
                }
                catch (Exception ex)
                {
                    if (postingFailure == null)
                    {
                        postingFailure =
                            $"temporary XML cleanup {ex.GetType().Name}: {ex.Message}";
                    }
                }
            }

            JobPosting loadedLegacyPosting = null;
            bool legacyPostingNodeBeforeStrip = false;
            bool legacyPostingNodeAfterStrip = false;
            string legacyPostingFailure = null;
            string legacyPostingStripFailure = null;
            string legacyPostingPath = Path.Combine(
                Path.GetTempPath(), $"Intercolony-LaborSpine-PostingLegacy-{Guid.NewGuid():N}.xml");
            try
            {
                if (Scribe.saver == null || Scribe.loader == null)
                {
                    legacyPostingFailure = "vanilla Scribe saver or loader was unavailable";
                }
                else
                {
                    JobPosting savedPosting = new JobPosting
                    {
                        requestedEquipmentLevel = LaborEquipmentLevel.Elite
                    };

                    Scribe.saver.InitSaving(
                        legacyPostingPath, "intercolony-labor-spine-posting-legacy");
                    Scribe_Deep.Look(ref savedPosting, "posting");
                    Scribe.saver.FinalizeSaving();

                    string xml = File.ReadAllText(legacyPostingPath);
                    legacyPostingNodeBeforeStrip =
                        xml.IndexOf("<requestedEquipmentLevel", StringComparison.Ordinal) >= 0;
                    string nodeName = "requestedEquipmentLevel";
                    string nodePrefix = "<" + nodeName;
                    int nodeStart = xml.IndexOf(nodePrefix, StringComparison.Ordinal);
                    if (nodeStart < 0)
                    {
                        legacyPostingStripFailure =
                            $"expected XML node <{nodeName}> was not present";
                    }
                    else
                    {
                        int tagEnd = xml.IndexOf('>', nodeStart);
                        if (tagEnd < 0)
                        {
                            legacyPostingStripFailure =
                                $"XML node <{nodeName}> had no closing angle bracket";
                        }
                        else
                        {
                            int nodeEnd;
                            if (xml[tagEnd - 1] == '/')
                            {
                                nodeEnd = tagEnd + 1;
                            }
                            else
                            {
                                string closeTag = "</" + nodeName + ">";
                                int closeStart = xml.IndexOf(
                                    closeTag, tagEnd + 1, StringComparison.Ordinal);
                                if (closeStart < 0)
                                {
                                    legacyPostingStripFailure =
                                        $"XML node <{nodeName}> had no closing tag";
                                    nodeEnd = -1;
                                }
                                else
                                {
                                    nodeEnd = closeStart + closeTag.Length;
                                }
                            }

                            if (legacyPostingStripFailure == null)
                            {
                                xml = xml.Remove(nodeStart, nodeEnd - nodeStart);
                            }
                        }
                    }

                    legacyPostingNodeAfterStrip =
                        xml.IndexOf("<requestedEquipmentLevel", StringComparison.Ordinal) >= 0;
                    if (legacyPostingStripFailure == null && legacyPostingNodeAfterStrip)
                    {
                        legacyPostingStripFailure =
                            "the <requestedEquipmentLevel> node was still present after stripping";
                    }

                    if (legacyPostingStripFailure == null)
                    {
                        File.WriteAllText(legacyPostingPath, xml);
                        Scribe.loader.InitLoading(legacyPostingPath);
                        Scribe_Deep.Look(ref loadedLegacyPosting, "posting");
                        Scribe.loader.FinalizeLoading();
                    }
                }
            }
            catch (Exception ex)
            {
                legacyPostingFailure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                try
                {
                    Scribe.ForceStop();
                }
                catch (Exception ex)
                {
                    if (legacyPostingFailure == null)
                    {
                        legacyPostingFailure =
                            $"Scribe cleanup {ex.GetType().Name}: {ex.Message}";
                    }
                }

                try
                {
                    if (File.Exists(legacyPostingPath))
                    {
                        File.Delete(legacyPostingPath);
                    }
                }
                catch (Exception ex)
                {
                    if (legacyPostingFailure == null)
                    {
                        legacyPostingFailure =
                            $"temporary XML cleanup {ex.GetType().Name}: {ex.Message}";
                    }
                }
            }

            r.Check(
                postingFailure == null && legacyPostingFailure == null &&
                legacyPostingStripFailure == null && postingNodePresent &&
                legacyPostingNodeBeforeStrip && !legacyPostingNodeAfterStrip &&
                loadedPosting != null &&
                loadedPosting.requestedEquipmentLevel == LaborEquipmentLevel.Elite &&
                loadedLegacyPosting != null &&
                loadedLegacyPosting.requestedEquipmentLevel == LaborEquipmentLevel.Any,
                postingRoundTripLabel,
                $"loaded requestedEquipmentLevel " +
                    $"{(loadedPosting == null ? "missing" : loadedPosting.requestedEquipmentLevel.ToString())}; " +
                    $"old loaded requestedEquipmentLevel " +
                    $"{(loadedLegacyPosting == null ? "missing" : loadedLegacyPosting.requestedEquipmentLevel.ToString())}; " +
                    $"XML explicit node " +
                    $"{(postingNodePresent ? "present" : "absent")}; " +
                    $"old XML before strip " +
                    $"{(legacyPostingNodeBeforeStrip ? "present" : "absent")}, after strip " +
                    $"{(legacyPostingNodeAfterStrip ? "present" : "absent")}; " +
                    $"stripFailure={legacyPostingStripFailure ?? "none"}; " +
                    $"failure={postingFailure ?? "none"}; " +
                    $"oldFailure={legacyPostingFailure ?? "none"}");
        }

        private static EmploymentContract RoundTripAutoRenew(
            bool savedValue, string label, out string failure, out bool autoRenewNodePresent)
        {
            EmploymentContract savedContract = new EmploymentContract
            {
                autoRenew = savedValue
            };
            EmploymentContract loadedContract = null;
            failure = null;
            autoRenewNodePresent = false;
            string path = Path.Combine(
                Path.GetTempPath(), $"Intercolony-{label}-{Guid.NewGuid():N}.xml");

            try
            {
                if (Scribe.saver == null || Scribe.loader == null)
                {
                    failure = "vanilla Scribe saver or loader was unavailable";
                    return null;
                }

                Scribe.saver.InitSaving(path, label);
                Scribe_Deep.Look(ref savedContract, "contract");
                Scribe.saver.FinalizeSaving();

                XmlDocument document = new XmlDocument();
                document.Load(path);
                autoRenewNodePresent = document.SelectSingleNode("//autoRenew") != null;

                Scribe.loader.InitLoading(path);
                Scribe_Deep.Look(ref loadedContract, "contract");
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

            return loadedContract;
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
                    "safePassage", "safePassageEndTick",
                    "apparelBondDecision", "arrivalTransport"
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

        private static void CheckRefundableBondAgreesWithSettlement(Results r)
        {
            // These are display/settlement-rule checks only. SettleBond's end-to-end path needs
            // a spawned pawn with live trackers and is covered by the existing world-pawn-delta
            // fixture below; these checks pin both displays to that same prorating rule.
            ThingDef syntheticThingDef = new ThingDef
            {
                defName = "IntercolonyLaborSelfTestBondThing"
            };

            EmploymentEquipmentRecord BuildRecord(
                float unitValue, int quantity, int boughtOutQuantity = 0)
            {
                return new EmploymentEquipmentRecord
                {
                    thingDef = syntheticThingDef,
                    unitValue = unitValue,
                    quantity = quantity,
                    boughtOutQuantity = boughtOutQuantity
                };
            }

            EmploymentContract BuildContract(
                List<EmploymentEquipmentRecord> records, int equipmentBond,
                bool equipmentBondSettled = false)
            {
                return new EmploymentContract
                {
                    arrivedEquipment = records,
                    equipmentBond = equipmentBond,
                    equipmentBondSettled = equipmentBondSettled
                };
            }

            // (a) With no buy-outs, the whole already-charged bond is refundable. The record
            // sets deliberately include both premium-rounding directions.
            EmploymentContract fullReturnDownContract = BuildContract(
                new List<EmploymentEquipmentRecord>
                {
                    BuildRecord(2f, 1)
                },
                2);
            EmploymentContract fullReturnUpContract = BuildContract(
                new List<EmploymentEquipmentRecord>
                {
                    BuildRecord(7f, 1)
                },
                8);
            EmploymentContract fullReturnMixedContract = BuildContract(
                new List<EmploymentEquipmentRecord>
                {
                    BuildRecord(2f, 1),
                    BuildRecord(7f, 1)
                },
                10);
            EmploymentContract fullReturnQuantityContract = BuildContract(
                new List<EmploymentEquipmentRecord>
                {
                    BuildRecord(2f, 2),
                    BuildRecord(3f, 1)
                },
                8);

            int fullReturnDownObserved = EmploymentEquipmentService.RefundableBondFor(
                fullReturnDownContract);
            int fullReturnUpObserved = EmploymentEquipmentService.RefundableBondFor(
                fullReturnUpContract);
            int fullReturnMixedObserved = EmploymentEquipmentService.RefundableBondFor(
                fullReturnMixedContract);
            int fullReturnQuantityObserved = EmploymentEquipmentService.RefundableBondFor(
                fullReturnQuantityContract);
            bool fullReturnAgrees =
                fullReturnDownObserved == fullReturnDownContract.equipmentBond &&
                fullReturnUpObserved == fullReturnUpContract.equipmentBond &&
                fullReturnMixedObserved == fullReturnMixedContract.equipmentBond &&
                fullReturnQuantityObserved == fullReturnQuantityContract.equipmentBond;
            r.Check(
                fullReturnAgrees,
                "the refundable bond is the whole bond until something is bought out",
                "[2] premium 2.2 -> bond 2, observed " +
                $"{fullReturnDownObserved}; [7] premium 7.7 -> bond 8, observed " +
                $"{fullReturnUpObserved}; [2,7] premium 9.9 -> bond 10, observed " +
                $"{fullReturnMixedObserved}; [2x2,3] premium 7.7 -> bond 8, observed " +
                $"{fullReturnQuantityObserved}");

            // (b) The expected value is plain arithmetic over the recorded values and the
            // already-charged bond. Each case also proves that freshly rounding the remainder
            // would produce a different answer before the production return is trusted.
            EmploymentEquipmentRecord twoAndThreeBoughtOutRecord = BuildRecord(2f, 1, 1);
            EmploymentEquipmentRecord twoAndThreeRemainingRecord = BuildRecord(3f, 1);
            EmploymentContract twoAndThreeContract = BuildContract(
                new List<EmploymentEquipmentRecord>
                {
                    twoAndThreeBoughtOutRecord,
                    twoAndThreeRemainingRecord
                },
                6);
            EmploymentEquipmentRecord twoAndFourBoughtOutRecord = BuildRecord(2f, 1, 1);
            EmploymentEquipmentRecord twoAndFourRemainingRecord = BuildRecord(4f, 1);
            EmploymentContract twoAndFourContract = BuildContract(
                new List<EmploymentEquipmentRecord>
                {
                    twoAndFourBoughtOutRecord,
                    twoAndFourRemainingRecord
                },
                7);
            EmploymentEquipmentRecord twoAndFourteenBoughtOutRecord = BuildRecord(2f, 1, 1);
            EmploymentEquipmentRecord twoAndFourteenRemainingRecord = BuildRecord(14f, 1);
            EmploymentContract twoAndFourteenContract = BuildContract(
                new List<EmploymentEquipmentRecord>
                {
                    twoAndFourteenBoughtOutRecord,
                    twoAndFourteenRemainingRecord
                },
                18);

            float twoAndThreeTotalValue =
                twoAndThreeBoughtOutRecord.unitValue * twoAndThreeBoughtOutRecord.quantity +
                twoAndThreeRemainingRecord.unitValue * twoAndThreeRemainingRecord.quantity;
            float twoAndThreeRemainingValue =
                twoAndThreeRemainingRecord.unitValue * twoAndThreeRemainingRecord.RefundableQuantity;
            int twoAndThreeProrated = Mathf.RoundToInt(
                twoAndThreeContract.equipmentBond * twoAndThreeRemainingValue /
                twoAndThreeTotalValue);
            int twoAndThreeFreshlyRounded = Mathf.RoundToInt(
                twoAndThreeRemainingValue * 1.10f);
            int twoAndThreeObserved = EmploymentEquipmentService.RefundableBondFor(
                twoAndThreeContract);

            float twoAndFourTotalValue =
                twoAndFourBoughtOutRecord.unitValue * twoAndFourBoughtOutRecord.quantity +
                twoAndFourRemainingRecord.unitValue * twoAndFourRemainingRecord.quantity;
            float twoAndFourRemainingValue =
                twoAndFourRemainingRecord.unitValue * twoAndFourRemainingRecord.RefundableQuantity;
            int twoAndFourProrated = Mathf.RoundToInt(
                twoAndFourContract.equipmentBond * twoAndFourRemainingValue /
                twoAndFourTotalValue);
            int twoAndFourFreshlyRounded = Mathf.RoundToInt(
                twoAndFourRemainingValue * 1.10f);
            int twoAndFourObserved = EmploymentEquipmentService.RefundableBondFor(
                twoAndFourContract);

            float twoAndFourteenTotalValue =
                twoAndFourteenBoughtOutRecord.unitValue *
                    twoAndFourteenBoughtOutRecord.quantity +
                twoAndFourteenRemainingRecord.unitValue *
                    twoAndFourteenRemainingRecord.quantity;
            float twoAndFourteenRemainingValue =
                twoAndFourteenRemainingRecord.unitValue *
                twoAndFourteenRemainingRecord.RefundableQuantity;
            int twoAndFourteenProrated = Mathf.RoundToInt(
                twoAndFourteenContract.equipmentBond * twoAndFourteenRemainingValue /
                twoAndFourteenTotalValue);
            int twoAndFourteenFreshlyRounded = Mathf.RoundToInt(
                twoAndFourteenRemainingValue * 1.10f);
            int twoAndFourteenObserved = EmploymentEquipmentService.RefundableBondFor(
                twoAndFourteenContract);

            bool boughtOutProratingAgrees =
                twoAndThreeContract.equipmentBond == Mathf.RoundToInt(
                    twoAndThreeTotalValue * 1.10f) &&
                twoAndFourContract.equipmentBond == Mathf.RoundToInt(
                    twoAndFourTotalValue * 1.10f) &&
                twoAndFourteenContract.equipmentBond == Mathf.RoundToInt(
                    twoAndFourteenTotalValue * 1.10f) &&
                twoAndThreeProrated != twoAndThreeFreshlyRounded &&
                twoAndFourProrated != twoAndFourFreshlyRounded &&
                twoAndFourteenProrated != twoAndFourteenFreshlyRounded &&
                twoAndThreeObserved == twoAndThreeProrated &&
                twoAndFourObserved == twoAndFourProrated &&
                twoAndFourteenObserved == twoAndFourteenProrated;
            r.Check(
                boughtOutProratingAgrees,
                "a bought-out item costs the bond its prorated share, not a freshly rounded one",
                $"2+3: bond {twoAndThreeContract.equipmentBond}, remaining value " +
                $"{twoAndThreeRemainingValue:0.###}, prorated {twoAndThreeProrated}, " +
                $"freshly rounded {twoAndThreeFreshlyRounded}, observed {twoAndThreeObserved}; " +
                $"2+4: bond {twoAndFourContract.equipmentBond}, remaining value " +
                $"{twoAndFourRemainingValue:0.###}, prorated {twoAndFourProrated}, " +
                $"freshly rounded {twoAndFourFreshlyRounded}, observed {twoAndFourObserved}; " +
                $"2+14: bond {twoAndFourteenContract.equipmentBond}, remaining value " +
                $"{twoAndFourteenRemainingValue:0.###}, prorated {twoAndFourteenProrated}, " +
                $"freshly rounded {twoAndFourteenFreshlyRounded}, observed " +
                $"{twoAndFourteenObserved}");

            // (c) The warning is the drop in the same refundable amount that the card reports
            // after the buy-out is really recorded. The fresh item-bond figures are included to
            // keep these cases sensitive to the old warning calculation as well.
            EmploymentEquipmentRecord warningTwoRecord = BuildRecord(2f, 1);
            EmploymentEquipmentRecord warningThreeRecord = BuildRecord(3f, 1);
            EmploymentContract warningTwoAndThreeContract = BuildContract(
                new List<EmploymentEquipmentRecord>
                {
                    warningTwoRecord,
                    warningThreeRecord
                },
                6);
            EmploymentEquipmentRecord warningFourRecord = BuildRecord(4f, 1);
            EmploymentContract warningTwoAndFourContract = BuildContract(
                new List<EmploymentEquipmentRecord>
                {
                    BuildRecord(2f, 1),
                    warningFourRecord
                },
                7);
            EmploymentEquipmentRecord warningTwoStackRecord = BuildRecord(2f, 2);
            EmploymentContract warningStackContract = BuildContract(
                new List<EmploymentEquipmentRecord>
                {
                    warningTwoStackRecord,
                    BuildRecord(3f, 1)
                },
                8);

            EmploymentContract[] warningContracts =
            {
                warningTwoAndThreeContract,
                warningTwoAndFourContract,
                warningStackContract
            };
            EmploymentEquipmentRecord[] warningRecords =
            {
                warningThreeRecord,
                warningFourRecord,
                warningTwoStackRecord
            };
            int[] warningUnits = { 1, 1, 2 };
            bool warningAndCardAgree = true;
            List<string> warningDetails = new List<string>();
            for (int i = 0; i < warningContracts.Length; i++)
            {
                EmploymentContract warningContract = warningContracts[i];
                EmploymentEquipmentRecord warningRecord = warningRecords[i];
                int units = warningUnits[i];
                int before = EmploymentEquipmentService.RefundableBondFor(warningContract);
                int bondAtRisk = EmploymentEquipmentService.BondAtRiskFor(
                    warningContract, warningRecord, units);
                float removedValue = warningRecord.unitValue * units;
                int freshlyRoundedRemovedBond = Mathf.RoundToInt(removedValue * 1.10f);

                warningRecord.boughtOutQuantity += units;
                int after = EmploymentEquipmentService.RefundableBondFor(warningContract);
                bool caseAgrees = bondAtRisk + after == before &&
                    bondAtRisk != freshlyRoundedRemovedBond;
                warningAndCardAgree &= caseAgrees;
                warningDetails.Add(
                    $"case {i + 1}: units {units}, before {before}, warning {bondAtRisk}, " +
                    $"after {after}, sum {bondAtRisk + after}, freshly rounded removed " +
                    $"{freshlyRoundedRemovedBond}");
            }

            r.Check(
                warningAndCardAgree,
                "what the player is warned they will lose is what the card then stops offering",
                string.Join("; ", warningDetails));

            // (d) Both hypothetical overloads must be observational. Capture every mutable field
            // they could be tempted to borrow before asking the same questions repeatedly.
            EmploymentEquipmentRecord unchangedFirstRecord = BuildRecord(2f, 3, 1);
            EmploymentEquipmentRecord unchangedSecondRecord = BuildRecord(7f, 2);
            EmploymentEquipmentRecord unchangedThirdRecord = BuildRecord(3f, 1);
            EmploymentContract unchangedContract = BuildContract(
                new List<EmploymentEquipmentRecord>
                {
                    unchangedFirstRecord,
                    unchangedSecondRecord,
                    unchangedThirdRecord
                },
                25);
            int[] quantitiesBefore =
            {
                unchangedFirstRecord.quantity,
                unchangedSecondRecord.quantity,
                unchangedThirdRecord.quantity
            };
            int[] boughtOutQuantitiesBefore =
            {
                unchangedFirstRecord.boughtOutQuantity,
                unchangedSecondRecord.boughtOutQuantity,
                unchangedThirdRecord.boughtOutQuantity
            };
            int equipmentBondBefore = unchangedContract.equipmentBond;
            bool equipmentBondSettledBefore = unchangedContract.equipmentBondSettled;

            int publicRiskFirst = EmploymentEquipmentService.BondAtRiskFor(
                unchangedContract, unchangedFirstRecord, 1);
            int publicRiskSecond = EmploymentEquipmentService.BondAtRiskFor(
                unchangedContract, unchangedSecondRecord, 1);
            int publicRiskThird = EmploymentEquipmentService.BondAtRiskFor(
                unchangedContract, unchangedThirdRecord, 1);
            int publicRiskRepeat = EmploymentEquipmentService.BondAtRiskFor(
                unchangedContract, unchangedFirstRecord, 1);
            int combinedRiskFirst = EmploymentEquipmentService.BondAtRiskFor(
                unchangedContract,
                new List<EmploymentEquipmentRecord>
                {
                    unchangedFirstRecord,
                    unchangedSecondRecord
                },
                new List<int> { 1, 1 });
            int combinedRiskSecond = EmploymentEquipmentService.BondAtRiskFor(
                unchangedContract,
                new List<EmploymentEquipmentRecord>
                {
                    unchangedSecondRecord,
                    unchangedThirdRecord
                },
                new List<int> { 1, 1 });
            int combinedRiskRepeat = EmploymentEquipmentService.BondAtRiskFor(
                unchangedContract,
                new List<EmploymentEquipmentRecord>
                {
                    unchangedFirstRecord,
                    unchangedSecondRecord
                },
                new List<int> { 1, 1 });

            bool hypotheticalIsNonMutating =
                publicRiskFirst == publicRiskRepeat &&
                combinedRiskFirst == combinedRiskRepeat &&
                unchangedFirstRecord.quantity == quantitiesBefore[0] &&
                unchangedSecondRecord.quantity == quantitiesBefore[1] &&
                unchangedThirdRecord.quantity == quantitiesBefore[2] &&
                unchangedFirstRecord.boughtOutQuantity == boughtOutQuantitiesBefore[0] &&
                unchangedSecondRecord.boughtOutQuantity == boughtOutQuantitiesBefore[1] &&
                unchangedThirdRecord.boughtOutQuantity == boughtOutQuantitiesBefore[2] &&
                unchangedContract.equipmentBond == equipmentBondBefore &&
                unchangedContract.equipmentBondSettled == equipmentBondSettledBefore;
            r.Check(
                hypotheticalIsNonMutating,
                "asking what a buy-out would cost changes nothing",
                $"public [{publicRiskFirst}, {publicRiskSecond}, {publicRiskThird}, " +
                $"repeat {publicRiskRepeat}], combined [{combinedRiskFirst}, " +
                $"{combinedRiskSecond}, repeat {combinedRiskRepeat}]; quantities " +
                $"[{unchangedFirstRecord.quantity}, {unchangedSecondRecord.quantity}, " +
                $"{unchangedThirdRecord.quantity}] before " +
                $"[{quantitiesBefore[0]}, {quantitiesBefore[1]}, {quantitiesBefore[2]}]; " +
                $"bought out [{unchangedFirstRecord.boughtOutQuantity}, " +
                $"{unchangedSecondRecord.boughtOutQuantity}, " +
                $"{unchangedThirdRecord.boughtOutQuantity}] before " +
                $"[{boughtOutQuantitiesBefore[0]}, {boughtOutQuantitiesBefore[1]}, " +
                $"{boughtOutQuantitiesBefore[2]}]; bond {unchangedContract.equipmentBond} " +
                $"before {equipmentBondBefore}, settled {unchangedContract.equipmentBondSettled} " +
                $"before {equipmentBondSettledBefore}");

            // (e) A settled deposit, a zero deposit, and missing/empty snapshots have no refund
            // or hypothetical loss left to offer.
            EmploymentEquipmentRecord emptyBondProbeRecord = BuildRecord(2f, 1);
            EmploymentContract settledContract = BuildContract(
                new List<EmploymentEquipmentRecord>
                {
                    emptyBondProbeRecord
                },
                2,
                equipmentBondSettled: true);
            EmploymentContract zeroBondContract = BuildContract(
                new List<EmploymentEquipmentRecord>
                {
                    BuildRecord(2f, 1)
                },
                0);
            EmploymentContract nullRecordsContract = BuildContract(null, 2);
            EmploymentContract emptyRecordsContract = BuildContract(
                new List<EmploymentEquipmentRecord>(),
                2);
            EmploymentContract[] emptyBondContracts =
            {
                settledContract,
                zeroBondContract,
                nullRecordsContract,
                emptyRecordsContract
            };
            string[] emptyBondCaseNames =
            {
                "settled",
                "zero bond",
                "null records",
                "empty records"
            };
            bool emptyBondIsNotRefundable = true;
            List<string> emptyBondDetails = new List<string>();
            for (int i = 0; i < emptyBondContracts.Length; i++)
            {
                EmploymentContract emptyBondContract = emptyBondContracts[i];
                int refundable = EmploymentEquipmentService.RefundableBondFor(emptyBondContract);
                int publicRisk = EmploymentEquipmentService.BondAtRiskFor(
                    emptyBondContract, emptyBondProbeRecord, 1);
                int combinedRisk = EmploymentEquipmentService.BondAtRiskFor(
                    emptyBondContract,
                    new List<EmploymentEquipmentRecord> { emptyBondProbeRecord },
                    new List<int> { 1 });
                bool caseIsEmpty = refundable == 0 && publicRisk == 0 && combinedRisk == 0;
                emptyBondIsNotRefundable &= caseIsEmpty;
                emptyBondDetails.Add(
                    $"{emptyBondCaseNames[i]}: refundable {refundable}, public risk " +
                    $"{publicRisk}, combined risk {combinedRisk}");
            }

            r.Check(
                emptyBondIsNotRefundable,
                "a settled or empty bond is refundable to nobody",
                string.Join("; ", emptyBondDetails));
        }

        private static void CheckEquipmentBondBuyout(Results r)
        {
            const string boughtOutUnitLabel = "a bought-out unit is not refunded";
            const string fullyBoughtOutLabel = "a fully bought-out record refunds nothing";
            const string replacementLabel =
                "a replacement item does not resurrect a bought-out unit";
            const string wearLabel = "ordinary wear does not reduce the refund";

            bool[] emitted = new bool[4];
            bool skipped = false;
            List<Thing> createdItems = new List<Thing>();
            Map refundMap = null;
            int savedRefundSilver = 0;
            List<Thing> silverBefore = new List<Thing>();

            void CheckEquipment(int index, string label, bool condition, string detail)
            {
                emitted[index] = true;
                r.Check(condition, label, detail);
            }

            try
            {
                Pawn carrier = null;
                List<Pawn> worldPawns = Find.WorldPawns?.AllPawnsAliveOrDead;
                if (worldPawns != null)
                {
                    // An unspawned real pawn is enough: SettleBond reads the pawn's trackers and
                    // does not require an employee quest, a map, or an arrival transition here.
                    foreach (Pawn pawn in worldPawns)
                    {
                        if (pawn != null && !pawn.Dead && !pawn.Discarded && !pawn.Spawned &&
                            pawn.inventory?.innerContainer != null)
                        {
                            carrier = pawn;
                            break;
                        }
                    }

                    if (carrier == null)
                    {
                        foreach (Pawn pawn in worldPawns)
                        {
                            if (pawn != null && !pawn.Dead && !pawn.Discarded &&
                                pawn.inventory?.innerContainer != null)
                            {
                                carrier = pawn;
                                break;
                            }
                        }
                    }
                }

                if (carrier == null && Find.Maps != null)
                {
                    foreach (Map map in Find.Maps)
                    {
                        if (map?.mapPawns?.AllPawnsSpawned == null)
                        {
                            continue;
                        }

                        foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                        {
                            if (pawn != null && !pawn.Dead && !pawn.Discarded &&
                                pawn.inventory?.innerContainer != null)
                            {
                                carrier = pawn;
                                break;
                            }
                        }

                        if (carrier != null)
                        {
                            break;
                        }
                    }
                }

                if (carrier == null)
                {
                    skipped = true;
                    SkipEquipmentBondBuyoutChecks(
                        r, "no existing pawn with an inventory tracker was available");
                    return;
                }

                ThingDef thingDef = ThingDefOf.Apparel_Parka;
                ThingDef stuffDef = ThingDefOf.Cloth;
                if (thingDef == null || stuffDef == null || !thingDef.MadeFromStuff ||
                    !stuffDef.IsStuff)
                {
                    skipped = true;
                    SkipEquipmentBondBuyoutChecks(
                        r, "Core parka or cloth ThingDef was unavailable for the item fixture");
                    return;
                }

                QualityCategory? fixtureQuality = null;
                bool fixtureReady = false;
                string fixtureFailure = null;
                QualityCategory[] qualityCandidates =
                {
                    QualityCategory.Legendary,
                    QualityCategory.Masterwork,
                    QualityCategory.Excellent,
                    QualityCategory.Good,
                    QualityCategory.Normal,
                    QualityCategory.Poor,
                    QualityCategory.Awful
                };

                foreach (QualityCategory requestedQuality in qualityCandidates)
                {
                    Apparel probe = null;
                    try
                    {
                        probe = ThingMaker.MakeThing(thingDef, stuffDef) as Apparel;
                        if (probe == null)
                        {
                            fixtureFailure = "ThingMaker did not create a parka fixture";
                            break;
                        }

                        createdItems.Add(probe);
                        CompQuality qualityComp = probe.TryGetComp<CompQuality>();
                        if (qualityComp != null)
                        {
                            qualityComp.SetQuality(
                                requestedQuality, ArtGenerationContext.Outsider);
                        }

                        QualityCategory? observedQuality = null;
                        if (probe.TryGetQuality(out QualityCategory observed))
                        {
                            observedQuality = observed;
                        }

                        if (probe.def != thingDef || probe.Stuff != stuffDef)
                        {
                            fixtureFailure =
                                $"parka fixture did not preserve its tuple: " +
                                $"def {probe.def?.defName ?? "null"}, " +
                                $"stuff {probe.Stuff?.defName ?? "null"}";
                            break;
                        }

                        EmploymentEquipmentRecord probeRecord = new EmploymentEquipmentRecord
                        {
                            thingDef = thingDef,
                            stuffDef = stuffDef,
                            quality = observedQuality,
                            unitValue = 100f,
                            quantity = 1
                        };
                        bool collidesWithCarrier = false;
                        foreach (Thing existing in CaptureCarriedEquipmentForTest(carrier))
                        {
                            if (SameEquipment(probeRecord, existing))
                            {
                                collidesWithCarrier = true;
                                break;
                            }
                        }

                        if (!collidesWithCarrier)
                        {
                            fixtureQuality = observedQuality;
                            fixtureReady = true;
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        fixtureFailure =
                            $"vanilla item fixture construction threw {ex.GetType().Name}: " +
                            ex.Message;
                        break;
                    }
                }

                if (!fixtureReady)
                {
                    skipped = true;
                    SkipEquipmentBondBuyoutChecks(
                        r, fixtureFailure ?? "every available quality tuple collided with the carrier");
                    return;
                }

                refundMap = Find.AnyPlayerHomeMap;
                if (refundMap == null && Find.Maps != null && Find.Maps.Count > 0)
                {
                    refundMap = Find.Maps[0];
                }

                if (refundMap != null)
                {
                    savedRefundSilver = PurchaseOrderService.CountColonySilver(refundMap);
                    if (ThingDefOf.Silver != null && refundMap.listerThings != null)
                    {
                        foreach (Thing silver in refundMap.listerThings.ThingsOfDef(ThingDefOf.Silver))
                        {
                            silverBefore.Add(silver);
                        }
                    }
                }

                bool RefundWasDelivered(
                    EmploymentEquipmentSettlement settlement, int expected)
                {
                    if (settlement == null || settlement.matchedBond != expected)
                    {
                        return false;
                    }

                    // A world without a destination can still exercise SettleBond's arithmetic;
                    // in the ordinary map case the actual silver placement must also agree.
                    return refundMap == null
                        ? settlement.returnedSilver == 0 && settlement.undeliveredSilver == expected
                        : settlement.returnedSilver == expected && settlement.undeliveredSilver == 0;
                }

                string SettlementDetail(
                    EmploymentContract contract, EmploymentEquipmentRecord record, int carried,
                    EmploymentEquipmentSettlement settlement)
                {
                    return
                        $"record quantity {record?.quantity ?? -1}, boughtOutQuantity " +
                        $"{record?.boughtOutQuantity ?? -1}, RefundableQuantity " +
                        $"{record?.RefundableQuantity ?? -1}; carried {carried}, matched " +
                        $"{settlement?.returnedQuantity ?? -1}/{record?.quantity ?? -1} " +
                        $"(denominator), retained quantity {settlement?.retainedQuantity ?? -1}; " +
                        $"bond {contract?.equipmentBond ?? -1}, matchedBond " +
                        $"{settlement?.matchedBond ?? -1}, returnedSilver " +
                        $"{settlement?.returnedSilver ?? -1}, retainedSilver " +
                        $"{settlement?.retainedSilver ?? -1}, undeliveredSilver " +
                        $"{settlement?.undeliveredSilver ?? -1}, settled " +
                        $"{contract?.equipmentBondSettled.ToString() ?? "missing"}";
                }

                EmploymentEquipmentSettlement SettleFixture(
                    int quantity, int boughtOutQuantity, int carriedQuantity, int bond, bool damaged,
                    out EmploymentContract contract, out EmploymentEquipmentRecord record,
                    out int carried, out int hitPointsBefore, out int hitPointsAfter,
                    out int hitPointsMax, out string failure)
                {
                    contract = null;
                    record = null;
                    carried = 0;
                    hitPointsBefore = -1;
                    hitPointsAfter = -1;
                    hitPointsMax = -1;
                    failure = null;
                    List<Thing> caseItems = new List<Thing>();

                    try
                    {
                        record = new EmploymentEquipmentRecord
                        {
                            thingDef = thingDef,
                            stuffDef = stuffDef,
                            quality = fixtureQuality,
                            unitValue = 100f,
                            quantity = quantity,
                            boughtOutQuantity = boughtOutQuantity
                        };
                        contract = new EmploymentContract
                        {
                            pawn = carrier,
                            destinationMap = refundMap,
                            arrivedEquipment = new List<EmploymentEquipmentRecord> { record },
                            equipmentBond = bond,
                            settlementName = "labor self-test",
                            workerName = "equipment bond fixture"
                        };

                        for (int i = 0; i < carriedQuantity; i++)
                        {
                            Apparel item = ThingMaker.MakeThing(thingDef, stuffDef) as Apparel;
                            if (item == null)
                            {
                                throw new InvalidOperationException(
                                    "ThingMaker did not create a parka fixture item.");
                            }

                            caseItems.Add(item);
                            createdItems.Add(item);
                            CompQuality qualityComp = item.TryGetComp<CompQuality>();
                            if (qualityComp != null && fixtureQuality.HasValue)
                            {
                                qualityComp.SetQuality(
                                    fixtureQuality.Value, ArtGenerationContext.Outsider);
                            }

                            QualityCategory? observedQuality = null;
                            if (item.TryGetQuality(out QualityCategory observed))
                            {
                                observedQuality = observed;
                            }

                            if (item.def != record.thingDef || item.Stuff != record.stuffDef ||
                                observedQuality != record.quality)
                            {
                                throw new InvalidOperationException(
                                    "fixture item did not match the recorded def, stuff and quality.");
                            }

                            item.stackCount = 1;
                            if (i == 0)
                            {
                                hitPointsBefore = item.HitPoints;
                                hitPointsMax = item.MaxHitPoints;
                            }

                            if (damaged && item.HitPoints > 1)
                            {
                                item.HitPoints = Mathf.Max(1, item.MaxHitPoints / 4);
                            }

                            if (i == 0)
                            {
                                hitPointsAfter = item.HitPoints;
                            }

                            if (!carrier.inventory.innerContainer.TryAdd(
                                    item, canMergeWithExistingStacks: false))
                            {
                                throw new InvalidOperationException(
                                    "the carrier inventory rejected a fixture item.");
                            }

                            carried++;
                        }

                        return EmploymentEquipmentService.SettleBond(contract);
                    }
                    catch (Exception ex)
                    {
                        failure = $"{ex.GetType().Name}: {ex.Message}";
                        return null;
                    }
                    finally
                    {
                        foreach (Thing item in caseItems)
                        {
                            CleanupFixtureItem(r, item);
                        }
                    }
                }

                EmploymentContract boughtOutUnitContract;
                EmploymentEquipmentRecord boughtOutUnitRecord;
                int boughtOutUnitCarried;
                int ignoredHitPointsBefore;
                int ignoredHitPointsAfter;
                int ignoredHitPointsMax;
                string boughtOutUnitFailure;
                EmploymentEquipmentSettlement boughtOutUnitSettlement = SettleFixture(
                    quantity: 3, boughtOutQuantity: 1, carriedQuantity: 3, bond: 300,
                    damaged: false,
                    out boughtOutUnitContract, out boughtOutUnitRecord,
                    out boughtOutUnitCarried, out ignoredHitPointsBefore,
                    out ignoredHitPointsAfter, out ignoredHitPointsMax,
                    out boughtOutUnitFailure);
                CheckEquipment(
                    0, boughtOutUnitLabel,
                    boughtOutUnitFailure == null &&
                    boughtOutUnitContract?.equipmentBondSettled == true &&
                    boughtOutUnitSettlement?.returnedQuantity == 2 &&
                    boughtOutUnitSettlement.retainedQuantity == 1 &&
                    RefundWasDelivered(boughtOutUnitSettlement, 200),
                    $"{boughtOutUnitFailure ?? "no setup failure"}; " +
                    SettlementDetail(
                        boughtOutUnitContract, boughtOutUnitRecord,
                        boughtOutUnitCarried, boughtOutUnitSettlement) +
                    "; expected matchedBond 200 from 2 refundable units over denominator 3");

                EmploymentContract fullyBoughtOutContract;
                EmploymentEquipmentRecord fullyBoughtOutRecord;
                int fullyBoughtOutCarried;
                string fullyBoughtOutFailure;
                EmploymentEquipmentSettlement fullyBoughtOutSettlement = SettleFixture(
                    quantity: 2, boughtOutQuantity: 2, carriedQuantity: 2, bond: 200,
                    damaged: false,
                    out fullyBoughtOutContract, out fullyBoughtOutRecord,
                    out fullyBoughtOutCarried, out ignoredHitPointsBefore,
                    out ignoredHitPointsAfter, out ignoredHitPointsMax,
                    out fullyBoughtOutFailure);
                CheckEquipment(
                    1, fullyBoughtOutLabel,
                    fullyBoughtOutFailure == null &&
                    fullyBoughtOutContract?.equipmentBondSettled == true &&
                    fullyBoughtOutSettlement?.returnedQuantity == 0 &&
                    fullyBoughtOutSettlement.retainedQuantity == 2 &&
                    RefundWasDelivered(fullyBoughtOutSettlement, 0),
                    $"{fullyBoughtOutFailure ?? "no setup failure"}; " +
                    SettlementDetail(
                        fullyBoughtOutContract, fullyBoughtOutRecord,
                        fullyBoughtOutCarried, fullyBoughtOutSettlement) +
                    "; expected matchedBond 0 from 0 refundable units over denominator 2");

                EmploymentContract replacementContract;
                EmploymentEquipmentRecord replacementRecord;
                int replacementCarried;
                string replacementFailure;
                EmploymentEquipmentSettlement replacementSettlement = SettleFixture(
                    quantity: 1, boughtOutQuantity: 1, carriedQuantity: 2, bond: 100,
                    damaged: false,
                    out replacementContract, out replacementRecord,
                    out replacementCarried, out ignoredHitPointsBefore,
                    out ignoredHitPointsAfter, out ignoredHitPointsMax,
                    out replacementFailure);
                CheckEquipment(
                    2, replacementLabel,
                    replacementFailure == null && replacementCarried == 2 &&
                    replacementContract?.equipmentBondSettled == true &&
                    replacementSettlement?.returnedQuantity == 0 &&
                    replacementSettlement.retainedQuantity == 1 &&
                    RefundWasDelivered(replacementSettlement, 0),
                    $"{replacementFailure ?? "no setup failure"}; " +
                    SettlementDetail(
                        replacementContract, replacementRecord,
                        replacementCarried, replacementSettlement) +
                    "; expected matchedBond 0 with one bought-out unit");

                EmploymentContract wearContract;
                EmploymentEquipmentRecord wearRecord;
                int wearCarried;
                int wearHitPointsBefore;
                int wearHitPointsAfter;
                int wearHitPointsMax;
                string wearFailure;
                EmploymentEquipmentSettlement wearSettlement = SettleFixture(
                    quantity: 1, boughtOutQuantity: 0, carriedQuantity: 1, bond: 100,
                    damaged: true,
                    out wearContract, out wearRecord, out wearCarried,
                    out wearHitPointsBefore, out wearHitPointsAfter,
                    out wearHitPointsMax, out wearFailure);
                bool wearApplied = wearHitPointsBefore > 1 &&
                    wearHitPointsAfter >= 1 && wearHitPointsAfter < wearHitPointsBefore;
                CheckEquipment(
                    3, wearLabel,
                    wearFailure == null && wearApplied && wearContract?.equipmentBondSettled == true &&
                    wearSettlement?.returnedQuantity == 1 &&
                    wearSettlement.retainedQuantity == 0 &&
                    RefundWasDelivered(wearSettlement, 100),
                    $"{wearFailure ?? "no setup failure"}; " +
                    SettlementDetail(wearContract, wearRecord, wearCarried, wearSettlement) +
                    $"; condition {wearHitPointsAfter}/{wearHitPointsMax} " +
                    $"(was {wearHitPointsBefore})" +
                    "; expected matchedBond 100 despite ordinary wear");

                // Idempotence is deliberately not re-asserted here because E4 "an equipment
                // bond settles once" already owns that claim with a working silver oracle;
                // a second weaker copy of an assertion is worse than none.
            }
            catch (Exception ex)
            {
                if (!skipped)
                {
                    string detail =
                        $"equipment-bond settlement fixture threw {ex.GetType().Name}: " +
                        ex.Message;
                    if (!emitted[0])
                    {
                        CheckEquipment(0, boughtOutUnitLabel, false, detail);
                    }

                    if (!emitted[1])
                    {
                        CheckEquipment(1, fullyBoughtOutLabel, false, detail);
                    }

                    if (!emitted[2])
                    {
                        CheckEquipment(2, replacementLabel, false, detail);
                    }

                    if (!emitted[3])
                    {
                        CheckEquipment(3, wearLabel, false, detail);
                    }
                }
            }
            finally
            {
                try
                {
                    if (refundMap != null && ThingDefOf.Silver != null &&
                        refundMap.listerThings != null)
                    {
                        foreach (Thing silver in refundMap.listerThings.ThingsOfDef(ThingDefOf.Silver))
                        {
                            if (!ContainsReference(silverBefore, silver) &&
                                !createdItems.Contains(silver))
                            {
                                createdItems.Add(silver);
                            }
                        }

                        IntercolonyLaborSelfTestSupport.RestoreStorageSilver(
                            refundMap, savedRefundSilver);
                    }
                }
                catch (Exception ex)
                {
                    r.Check(false, "equipment-bond fixture silver cleans up",
                        $"{ex.GetType().Name}: {ex.Message}");
                }

                foreach (Thing item in createdItems)
                {
                    CleanupFixtureItem(r, item);
                }
            }
        }

        private static void SkipEquipmentBondBuyoutChecks(Results r, string reason)
        {
            string detail =
                $"equipment-bond buyout fixture unavailable: {reason ?? "no reason supplied"}";
            r.Skip("a bought-out unit is not refunded", detail);
            r.Skip("a fully bought-out record refunds nothing", detail);
            r.Skip("a replacement item does not resurrect a bought-out unit", detail);
            r.Skip("ordinary wear does not reduce the refund", detail);
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

        private static void TrackGeneratedLoadout(List<Thing> fixtureItems, Pawn pawn)
        {
            if (pawn?.equipment?.AllEquipmentListForReading != null)
            {
                foreach (ThingWithComps item in pawn.equipment.AllEquipmentListForReading)
                {
                    TrackThing(fixtureItems, item);
                }
            }

            if (pawn?.apparel?.WornApparel != null)
            {
                foreach (Apparel item in pawn.apparel.WornApparel)
                {
                    TrackThing(fixtureItems, item);
                }
            }
        }

        private static void TrackThing(List<Thing> fixtureItems, Thing item)
        {
            if (item != null && !fixtureItems.Contains(item))
            {
                fixtureItems.Add(item);
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
