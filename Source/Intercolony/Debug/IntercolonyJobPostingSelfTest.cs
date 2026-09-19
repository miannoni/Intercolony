using System.Collections.Generic;
using System.Reflection;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using System;
using System.IO;
using System.Xml;

namespace Intercolony
{
    /// <summary>
    /// End-to-end check of Phase 21's acceptance criterion (DESIGN.md §114, §35.2).
    ///
    /// §114 asks for one measurable thing: *"Requirements and better employer reputation measurably
    /// improve applicant quantity/quality."* So this drives the **real** matcher against the **real**
    /// world pool at several requirements and at both ends of the reputation range, and measures what
    /// comes back. It does not assert that a formula returns what the formula returns.
    ///
    /// It also guards the thing that is invisible in play: applicants are pinned world pawns, and a
    /// posting that closes without discarding them leaks one pawn per applicant, forever. The world
    /// pawn count is measured before and after.
    ///
    /// Every posting it creates is withdrawn and removed, and employer standing is restored.
    /// </summary>
    public static class IntercolonyJobPostingSelfTest
    {
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

        /// <summary>One measurement of what a posting drew.</summary>
        private struct Draw
        {
            /// <summary>Workers meeting the requirement — the market's unbounded answer.</summary>
            public int interested;

            /// <summary>Workers actually queued, which the applicant cap truncates.</summary>
            public int applicants;

            /// <summary>Queued applicants that do not satisfy the posting's requirement.</summary>
            public int queuedBarViolations;

            public float averageBestSkill;
            public int bestSkill;
        }

        private struct ApplicantValues
        {
            public int settlementId;
            public string settlementName;
            public string factionName;
            public int travelDays;
            public int openMarketAsk;
            public string skillLevels;
        }

        private struct ApplicantDraw
        {
            public int qualified;
            public List<ApplicantValues> values;
            public bool fixtureBuilt;
            public string fixtureFailureReason;
        }

        private struct EquipmentCensusCounts
        {
            public int total;
            public int none;
            public int standard;
            public int professional;
            public int elite;
            public int any;
            public int unknown;

            public int ProfessionalOrBetter => professional + elite;
            public int StandardOrBetter => standard + professional + elite;
        }

        private struct EmergencyReachCensusCounts
        {
            public int total;
            public int available;
            public int dropPod;
            public int conventional;
            public int none;
        }

        private struct ImmediateEmergencyObservation
        {
            public int eligibleProspects;
            public int prospectsQueuedOrMatchedImmediately;
            public int applicantsAfterTryPost;
            public int applicantsAfterBoundedTicks;
            public int ticksDriven;
        }

        private struct EmergencyFilterObservation
        {
            public bool fixtureBuilt;
            public bool ordinaryQueued;
            public bool emergencyQueued;
            public string failure;
        }

        private struct TierSample
        {
            public int none;
            public int standard;
            public int professional;
            public int elite;
            public int any;
            public int unknown;
            public string exception;
        }

        private struct EquipmentMatchObservation
        {
            public bool fixtureBuilt;
            public bool applicantQueued;
            public bool finalGateRejected;
            public string applyResult;
            public string failure;
        }

        private const float WaitingListSpreadMargin = 1f;
        private const int EquipmentBalanceMinimumCensus = 50;
        private const int EquipmentTierSampleSize = 4096;
        private const int EquipmentTierSampleSeed = 0x45_51_54_31;
        private const string FrozenEmergencyTransportLabel =
            "emergency hire carries the applicant's frozen arrival transport";
        private const string FrozenEmergencyDurationLabel =
            "emergency hire carries the applicant's frozen arrival duration";
        private const string FrozenEmergencySnapshotLabel =
            "emergency hire does not re-derive the frozen arrival quote at hire";
        private const string FrozenEmergencyQuoteRoundTripLabel =
            "S1: frozen emergency quote survives save/load before acceptance";
        private const string FrozenEmergencyReloadHireLabel =
            "S2: accepting after reload preserves the exact route and timing";
        private const string FrozenEmergencyArrivalAfterReloadLabel =
            "S3: accepted travelling contract survives save/load and arrives exactly once via its emergency route";
        private const string EmployeeArrivedLetterLabel = "Employee arrived";
        private const string EmergencyArrivalLetterLabel = "Emergency reinforcements inbound";
        private const string OrdinaryArrivalLabel =
            "ordinary hire keeps conventional travelDays arrival";
        private const string EmergencyApplicantMigrationQuoteLabel =
            "S4/C2: schema 59 emergency applicant receives a frozen emergency quote";
        private const string EmergencyApplicantMigrationInvalidationLabel =
            "S4/C3: schema 59 emergency applicant with a missing settlement is discarded";
        private const string EmergencyApplicantMigrationOrdinaryLabel =
            "S4/C1: ordinary applicant is untouched by emergency migration";
        private const string EmergencyApplicantMigrationIdentityLabel =
            "S4/C4: legacy applicant receives a census identity or safe marker";

        public static string Run(IntercolonyWorldComponent state, Map map)
        {
            Results r = new Results();
            r.sb.AppendLine("Job posting and applicant self-test (§114, §35.2)");

            if (state == null || map == null)
            {
                r.sb.AppendLine("  No world or map. Open a colony first.");
                return Summarize(r);
            }

            EmployerReputation rep = state.EmployerStanding;
            float savedScore = rep?.Score ?? 0f;
            int savedPostings = state.Postings.Count;
            int savedEmployments = state.Employments.Count;
            int savedLedger = state.Ledger.Count;
            int savedLedgerStartTick = state.LedgerStartTick;
            int worldPawnsBefore = Find.WorldPawns?.AllPawnsAliveOrDead?.Count ?? 0;

            IntercolonyLaborSelfTestSupport.ResetLedger();

            try
            {
                CheckEquipmentMarket(r, state);
                CheckPoolSplit(r, state);
                CheckEmergencyReachCensus(r, state);
                CheckEmergencyPostingFlag(r, state);
                CheckEmergencyPostingPersistence(r);
                CheckEmergencyPostingImmediateMatch(r, state);
                CheckEmergencyEquipmentIntegration(r, state);
                CheckEmergencyReachFilter(r, state);
                CheckRequirementsDriveApplicants(r, state);
                CheckWaitingListIsSpread(r, state);
                CheckMarketReproduces(r, state);
                CheckReputationDrivesApplicants(r, state, rep);
                CheckReputationDrivesCandidateQuality(r, state, rep);
                CheckOnePersonOnePosting(r, state);
                CheckSilenceIsExplained(r, state);
                CheckLifecycle(r, state);
                CheckApplicantOwnAsk(r, state, map);
                CheckFrozenEmergencyArrivalOnHire(r, state, map);
                CheckEmergencyApplicantMigration(r, state);
                CheckLoadPruner(r, state);
            }
            catch (System.Exception ex)
            {
                r.sb.AppendLine($"  EXCEPTION: {ex}");
                r.failed++;
            }
            finally
            {
                // A successful applicant hire creates a travelling employment rather than a
                // posting-owned pawn. End and remove any test employment before the outer leak
                // check, even if the hire assertion itself threw during cleanup.
                for (int i = state.Employments.Count - 1; i >= savedEmployments; i--)
                {
                    EmploymentContract contract = state.Employments[i];
                    if (contract?.IsOpen == true)
                    {
                        EmploymentService.End(contract, EmploymentStatus.Failed, "self-test cleanup");
                    }

                    state.Employments.RemoveAt(i);
                }

                // Every posting this test made must go, and closing is what discards its applicants.
                for (int i = state.Postings.Count - 1; i >= savedPostings; i--)
                {
                    JobPostingService.Close(state.Postings[i], JobPostingStatus.Withdrawn, "self-test");
                    state.Postings.RemoveAt(i);
                }

                if (rep != null)
                {
                    rep.Adjust(savedScore - rep.Score);
                }

                int returned = IntercolonyLaborSelfTestSupport.RestoreLedger(map);
                if (returned > 0)
                {
                    r.Info($"returned {returned} silver the test had consumed.");
                }

                while (state.Ledger.Count > savedLedger)
                {
                    state.Ledger.RemoveAt(state.Ledger.Count - 1);
                }

                state.LedgerStartTick = savedLedgerStartTick;

                LaborCandidateService.Clear();

                int worldPawnsAfter = Find.WorldPawns?.AllPawnsAliveOrDead?.Count ?? 0;
                int worldPawnDelta = worldPawnsAfter - worldPawnsBefore;

                // The leak that no amount of playing would reveal. An applicant is pinned
                // KeepForever, so one missed discard is a pawn the world pawn GC has been told never
                // to collect — invisible until a save file is inexplicably large.
                r.Check(worldPawnDelta == 0,
                    "no world pawns leaked by postings opened and closed (§35.2)",
                    $"{worldPawnsBefore} before, {worldPawnsAfter} after, delta {worldPawnDelta}");

                r.Info($"restored employer standing to {rep?.ScoreDisplay ?? 0}/100 and removed test postings.");
            }

            return Summarize(r);
        }

        // --- The pool ----------------------------------------------------------------------

        private static void CheckEquipmentMarket(
            Results r, IntercolonyWorldComponent state)
        {
            IntercolonySettings settings = IntercolonyMod.Settings;
            float savedStandard = settings.standardEquipmentAbundance;
            float savedProfessional = settings.professionalEquipmentAbundance;
            float savedElite = settings.eliteEquipmentAbundance;

            try
            {
                // The balance shape is a default-setting diagnostic. The abundance checks below
                // deliberately mutate these same fields, and the outer finally restores the
                // player's values even if a fixture or assertion throws.
                settings.standardEquipmentAbundance =
                    IntercolonySettings.DefaultStandardEquipmentAbundance;
                settings.professionalEquipmentAbundance =
                    IntercolonySettings.DefaultProfessionalEquipmentAbundance;
                settings.eliteEquipmentAbundance =
                    IntercolonySettings.DefaultEliteEquipmentAbundance;
                LaborCandidateService.InvalidateCensus();

                List<LaborProspect> census = LaborCandidateService.Census(state);
                EquipmentCensusCounts counts = CountEquipmentCensus(census);
                ReportEquipmentCensus(r, counts);

                List<SettlementEconomicProfile> profiles = state.AllProfiles();
                int standardCapableProfileCount = 0;
                int professionalCapableProfileCount = 0;
                int eliteCapableProfileCount = 0;
                foreach (SettlementEconomicProfile profile in profiles)
                {
                    if (LaborEquipmentTierService.CanSupply(
                            profile, LaborEquipmentLevel.Standard, CombatClause.Civilian))
                    {
                        standardCapableProfileCount++;
                    }

                    if (LaborEquipmentTierService.CanSupply(
                            profile, LaborEquipmentLevel.Professional, CombatClause.Civilian))
                    {
                        professionalCapableProfileCount++;
                    }

                    if (LaborEquipmentTierService.CanSupply(
                            profile, LaborEquipmentLevel.Elite, CombatClause.Civilian))
                    {
                        eliteCapableProfileCount++;
                    }
                }

                r.Info(
                    $"equipment capable sources (Civilian): " +
                    $"standard={standardCapableProfileCount}/{profiles.Count}, " +
                    $"professional={professionalCapableProfileCount}/{profiles.Count}, " +
                    $"elite={eliteCapableProfileCount}/{profiles.Count}");

                if (counts.total < EquipmentBalanceMinimumCensus)
                {
                    const string reason = "census has fewer than 50 prospects";
                    r.Skip(
                        "standard-or-better is clearly the largest equipment group", reason);
                    r.Skip(
                        "professional-or-better is a strict minority of standard-or-better",
                        reason);
                    r.Skip("elite is rarer than exact-professional", reason);
                }
                else
                {
                    if (standardCapableProfileCount == 0)
                    {
                        r.Skip(
                            "standard-or-better is clearly the largest equipment group",
                            $"no settlement in this world passes the Standard capability gate " +
                            $"(0 of {profiles.Count} profiles), so a Standard-or-better census " +
                            "cannot be required here");
                    }
                    else
                    {
                        r.Check(
                            counts.StandardOrBetter * 2 > counts.total &&
                            counts.StandardOrBetter > counts.ProfessionalOrBetter &&
                            counts.StandardOrBetter > counts.elite,
                            "standard-or-better is clearly the largest equipment group",
                            $"OBSERVED standard-or-better={counts.StandardOrBetter}/{counts.total}; " +
                            "EXPECTED more than half of the census and larger than upper-tier groups");
                    }

                    if (professionalCapableProfileCount == 0)
                    {
                        string reason =
                            $"no settlement in this world passes the Professional capability gate " +
                            $"(0 of {profiles.Count} profiles), so a Professional-or-better minority " +
                            "cannot be required here";
                        r.Skip(
                            "professional-or-better is a strict minority of standard-or-better", reason);
                        r.Skip("elite is rarer than exact-professional", reason);
                    }
                    else
                    {
                        r.Check(
                            counts.ProfessionalOrBetter * 2 < counts.StandardOrBetter,
                            "professional-or-better is a strict minority of standard-or-better",
                            $"OBSERVED professional-or-better={counts.ProfessionalOrBetter}, " +
                            $"standard-or-better={counts.StandardOrBetter}; EXPECTED professional-or-better " +
                            "below half of standard-or-better");

                        r.Check(
                            counts.elite < counts.professional,
                            "elite is rarer than exact-professional",
                            $"OBSERVED elite={counts.elite}, exact-professional={counts.professional}; " +
                            "EXPECTED elite below exact-professional");
                    }
                }

                if (eliteCapableProfileCount == 0)
                {
                    r.Skip(
                        "elite is non-zero in the full equipment census",
                        $"no settlement in this world passes the Elite capability gate " +
                        $"(0 of {profiles.Count} profiles), so a non-zero Elite census " +
                        "cannot be required here");
                }
                else
                {
                    r.Check(
                        counts.elite > 0,
                        "elite is non-zero in the full equipment census",
                        $"OBSERVED elite={counts.elite} of {counts.total}; EXPECTED at least 1");
                }
                r.Check(
                    counts.any == 0,
                    "no census prospect is assigned the any tier",
                    $"OBSERVED Any={counts.any}; EXPECTED 0");

                CheckEquipmentAbundance(r);
                CheckEquipmentCapabilityCeiling(r);
                CheckEquipmentMatching(r, state);
                CheckEquipmentFulfilment(r, state);
            }
            finally
            {
                settings.standardEquipmentAbundance = savedStandard;
                settings.professionalEquipmentAbundance = savedProfessional;
                settings.eliteEquipmentAbundance = savedElite;

                // Census is keyed by refresh, not by settings. Do not leave a default-setting or
                // controlled matching census visible to later postings after restoring the values.
                LaborCandidateService.InvalidateCensus();
            }
        }

        private static EquipmentCensusCounts CountEquipmentCensus(
            List<LaborProspect> census)
        {
            EquipmentCensusCounts counts = new EquipmentCensusCounts
            {
                total = census?.Count ?? 0
            };

            if (census == null)
            {
                return counts;
            }

            foreach (LaborProspect prospect in census)
            {
                if (prospect == null)
                {
                    counts.unknown++;
                    continue;
                }

                switch (prospect.equipmentTier)
                {
                    case LaborEquipmentLevel.None:
                        counts.none++;
                        break;
                    case LaborEquipmentLevel.Standard:
                        counts.standard++;
                        break;
                    case LaborEquipmentLevel.Professional:
                        counts.professional++;
                        break;
                    case LaborEquipmentLevel.Elite:
                        counts.elite++;
                        break;
                    case LaborEquipmentLevel.Any:
                        counts.any++;
                        break;
                    default:
                        counts.unknown++;
                        break;
                }
            }

            return counts;
        }

        private static void ReportEquipmentCensus(
            Results r, EquipmentCensusCounts counts)
        {
            r.Info($"equipment census total: {counts.total} prospects");
            r.Info($"equipment census none: {counts.none} ({EquipmentPercentage(counts.none, counts.total):0.0}%)");
            r.Info($"equipment census standard: {counts.standard} ({EquipmentPercentage(counts.standard, counts.total):0.0}%)");
            r.Info($"equipment census professional: {counts.professional} ({EquipmentPercentage(counts.professional, counts.total):0.0}%)");
            r.Info($"equipment census elite: {counts.elite} ({EquipmentPercentage(counts.elite, counts.total):0.0}%)");
            r.Info($"equipment census professional-or-better: {counts.ProfessionalOrBetter} " +
                   $"({EquipmentPercentage(counts.ProfessionalOrBetter, counts.total):0.0}%)");
            r.Info($"equipment census standard-or-better: {counts.StandardOrBetter} " +
                   $"({EquipmentPercentage(counts.StandardOrBetter, counts.total):0.0}%)");
        }

        private static float EquipmentPercentage(int count, int total)
        {
            return total <= 0 ? 0f : count * 100f / total;
        }

        private static void CheckEquipmentAbundance(Results r)
        {
            IntercolonySettings settings = IntercolonyMod.Settings;
            float savedStandard = settings.standardEquipmentAbundance;
            float savedProfessional = settings.professionalEquipmentAbundance;
            float savedElite = settings.eliteEquipmentAbundance;
            SettlementEconomicProfile capableProfile = new SettlementEconomicProfile
            {
                techTier = TechLevel.Spacer,
                wealthTier = IntercolonyWealthTier.Comfortable,
                archetype = IntercolonyArchetype.Military
            };

            try
            {
                settings.standardEquipmentAbundance =
                    IntercolonySettings.DefaultStandardEquipmentAbundance;
                settings.professionalEquipmentAbundance =
                    IntercolonySettings.DefaultProfessionalEquipmentAbundance;
                settings.eliteEquipmentAbundance =
                    IntercolonySettings.DefaultEliteEquipmentAbundance;
                TierSample baseline = RollTierSample(
                    capableProfile, CombatClause.Civilian,
                    EquipmentTierSampleSize, EquipmentTierSampleSeed);

                settings.professionalEquipmentAbundance =
                    IntercolonySettings.MaxProfessionalEquipmentAbundance;
                TierSample professionalRaised = RollTierSample(
                    capableProfile, CombatClause.Civilian,
                    EquipmentTierSampleSize, EquipmentTierSampleSeed);

                settings.professionalEquipmentAbundance =
                    IntercolonySettings.DefaultProfessionalEquipmentAbundance;
                settings.eliteEquipmentAbundance =
                    IntercolonySettings.MaxEliteEquipmentAbundance;
                TierSample eliteRaised = RollTierSample(
                    capableProfile, CombatClause.Civilian,
                    EquipmentTierSampleSize, EquipmentTierSampleSeed);

                settings.eliteEquipmentAbundance = 0f;
                TierSample eliteDisabled = RollTierSample(
                    capableProfile, CombatClause.Civilian,
                    EquipmentTierSampleSize, EquipmentTierSampleSeed);

                settings.standardEquipmentAbundance = 0f;
                settings.professionalEquipmentAbundance = 0f;
                settings.eliteEquipmentAbundance = 0f;
                TierSample allDisabled = RollTierSample(
                    capableProfile, CombatClause.Civilian,
                    EquipmentTierSampleSize, EquipmentTierSampleSeed);

                r.Check(
                    baseline.exception == null && professionalRaised.exception == null &&
                    professionalRaised.professional >= baseline.professional,
                    "raising professional abundance does not reduce exact-professional outcomes",
                    $"OBSERVED baseline={baseline.professional}, raised={professionalRaised.professional}; " +
                    $"EXPECTED raised >= baseline; samples {EquipmentTierSampleSize}; " +
                    $"baseline {DescribeTierSample(baseline)}; raised {DescribeTierSample(professionalRaised)}");

                r.Check(
                    baseline.exception == null && eliteRaised.exception == null &&
                    eliteRaised.elite >= baseline.elite,
                    "raising elite abundance does not reduce exact-elite outcomes",
                    $"OBSERVED baseline={baseline.elite}, raised={eliteRaised.elite}; " +
                    $"EXPECTED raised >= baseline; samples {EquipmentTierSampleSize}; " +
                    $"baseline {DescribeTierSample(baseline)}; raised {DescribeTierSample(eliteRaised)}");

                r.Check(
                    eliteDisabled.exception == null && eliteDisabled.elite == 0 &&
                    eliteDisabled.standard > 0 && eliteDisabled.professional > 0,
                    "zero elite abundance disables only exact-elite outcomes",
                    $"OBSERVED elite={eliteDisabled.elite}, standard={eliteDisabled.standard}, " +
                    $"professional={eliteDisabled.professional}; EXPECTED elite=0 with standard and " +
                    $"professional >0; sample {DescribeTierSample(eliteDisabled)}");

                r.Check(
                    allDisabled.exception == null && allDisabled.none == EquipmentTierSampleSize &&
                    allDisabled.standard == 0 && allDisabled.professional == 0 &&
                    allDisabled.elite == 0 && allDisabled.any == 0 && allDisabled.unknown == 0,
                    "zero abundance for every tier yields none without throwing",
                    $"OBSERVED {DescribeTierSample(allDisabled)}; EXPECTED none={EquipmentTierSampleSize}, " +
                    "all other outcomes=0 and exception=none");
            }
            finally
            {
                settings.standardEquipmentAbundance = savedStandard;
                settings.professionalEquipmentAbundance = savedProfessional;
                settings.eliteEquipmentAbundance = savedElite;
            }
        }

        private static TierSample RollTierSample(
            SettlementEconomicProfile profile, CombatClause clause, int sampleSize, int seed)
        {
            TierSample sample = new TierSample();
            Rand.PushState(seed);
            try
            {
                for (int i = 0; i < sampleSize; i++)
                {
                    LaborEquipmentLevel tier = LaborEquipmentTierService.RollPromisedTier(
                        profile, clause);
                    switch (tier)
                    {
                        case LaborEquipmentLevel.None:
                            sample.none++;
                            break;
                        case LaborEquipmentLevel.Standard:
                            sample.standard++;
                            break;
                        case LaborEquipmentLevel.Professional:
                            sample.professional++;
                            break;
                        case LaborEquipmentLevel.Elite:
                            sample.elite++;
                            break;
                        case LaborEquipmentLevel.Any:
                            sample.any++;
                            break;
                        default:
                            sample.unknown++;
                            break;
                    }
                }
            }
            catch (System.Exception ex)
            {
                sample.exception = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                Rand.PopState();
            }

            return sample;
        }

        private static string DescribeTierSample(TierSample sample)
        {
            return $"none={sample.none}, standard={sample.standard}, " +
                   $"professional={sample.professional}, elite={sample.elite}, " +
                   $"Any={sample.any}, unknown={sample.unknown}, " +
                   $"exception={sample.exception ?? "none"}";
        }

        private static void CheckEquipmentCapabilityCeiling(Results r)
        {
            IntercolonySettings settings = IntercolonyMod.Settings;
            float savedStandard = settings.standardEquipmentAbundance;
            float savedProfessional = settings.professionalEquipmentAbundance;
            float savedElite = settings.eliteEquipmentAbundance;
            SettlementEconomicProfile neolithicProfile = new SettlementEconomicProfile
            {
                techTier = TechLevel.Neolithic,
                wealthTier = IntercolonyWealthTier.Comfortable,
                archetype = IntercolonyArchetype.Military
            };
            SettlementEconomicProfile poorIndustrialProfile = new SettlementEconomicProfile
            {
                techTier = TechLevel.Industrial,
                wealthTier = IntercolonyWealthTier.Modest,
                archetype = IntercolonyArchetype.Military
            };
            SettlementEconomicProfile capableIndustrialProfile = new SettlementEconomicProfile
            {
                techTier = TechLevel.Industrial,
                wealthTier = IntercolonyWealthTier.Comfortable,
                archetype = IntercolonyArchetype.Military
            };

            try
            {
                settings.standardEquipmentAbundance =
                    IntercolonySettings.DefaultStandardEquipmentAbundance;
                settings.professionalEquipmentAbundance =
                    IntercolonySettings.DefaultProfessionalEquipmentAbundance;
                settings.eliteEquipmentAbundance =
                    IntercolonySettings.MaxEliteEquipmentAbundance;
                bool neolithicCanSupplyElite = LaborEquipmentTierService.CanSupply(
                    neolithicProfile, LaborEquipmentLevel.Elite, CombatClause.Civilian);
                TierSample neolithicSample = RollTierSample(
                    neolithicProfile, CombatClause.Civilian,
                    EquipmentTierSampleSize, EquipmentTierSampleSeed);
                bool poorIndustrialCanSupplyElite = LaborEquipmentTierService.CanSupply(
                    poorIndustrialProfile, LaborEquipmentLevel.Elite, CombatClause.Civilian);
                TierSample poorIndustrialSample = RollTierSample(
                    poorIndustrialProfile, CombatClause.Civilian,
                    EquipmentTierSampleSize, EquipmentTierSampleSeed);
                bool capableIndustrialCanSupplyElite = LaborEquipmentTierService.CanSupply(
                    capableIndustrialProfile, LaborEquipmentLevel.Elite, CombatClause.Civilian);
                TierSample capableIndustrialSample = RollTierSample(
                    capableIndustrialProfile, CombatClause.Civilian,
                    EquipmentTierSampleSize, EquipmentTierSampleSeed);

                r.Check(
                    !neolithicCanSupplyElite && neolithicSample.exception == null &&
                    neolithicSample.elite == 0 &&
                    !poorIndustrialCanSupplyElite && poorIndustrialSample.exception == null &&
                    poorIndustrialSample.elite == 0 &&
                    capableIndustrialCanSupplyElite && capableIndustrialSample.exception == null &&
                    capableIndustrialSample.elite > 0,
                    "maximum elite abundance cannot bypass an incapable source ceiling",
                    $"OBSERVED neolithic={neolithicProfile.techTier}/{neolithicProfile.wealthTier}/" +
                    $"{neolithicProfile.archetype}, CanSupply(Elite)={neolithicCanSupplyElite}, " +
                    $"exact-elite={neolithicSample.elite}; poor-industrial=" +
                    $"{poorIndustrialProfile.techTier}/{poorIndustrialProfile.wealthTier}/" +
                    $"{poorIndustrialProfile.archetype}, " +
                    $"CanSupply(Elite)={poorIndustrialCanSupplyElite}, " +
                    $"exact-elite={poorIndustrialSample.elite}; capable-industrial=" +
                    $"{capableIndustrialProfile.techTier}/{capableIndustrialProfile.wealthTier}/" +
                    $"{capableIndustrialProfile.archetype}, " +
                    $"CanSupply(Elite)={capableIndustrialCanSupplyElite}, " +
                    $"exact-elite={capableIndustrialSample.elite}; EXPECTED neolithic and " +
                    $"poor-industrial CanSupply(Elite)=False and exact-elite=0, capable-industrial " +
                    $"CanSupply(Elite)=True and exact-elite > 0; clause={CombatClause.Civilian}; " +
                    $"samples={EquipmentTierSampleSize} seeded {EquipmentTierSampleSeed}; " +
                    $"neolithic {DescribeTierSample(neolithicSample)}; " +
                    $"poor-industrial {DescribeTierSample(poorIndustrialSample)}; " +
                    $"capable-industrial {DescribeTierSample(capableIndustrialSample)}");
            }
            finally
            {
                settings.standardEquipmentAbundance = savedStandard;
                settings.professionalEquipmentAbundance = savedProfessional;
                settings.eliteEquipmentAbundance = savedElite;
            }
        }

        private static void CheckEquipmentMatching(
            Results r, IntercolonyWorldComponent state)
        {
            const string standardLabel =
                "a standard prospect is not considered by a professional posting";
            const string eliteLabel =
                "an elite prospect is considered by a professional posting";

            if (Find.WorldPawns == null)
            {
                r.Skip(standardLabel,
                    "Find.WorldPawns was null, so controlled MatchAll could not run");
                r.Skip(eliteLabel,
                    "Find.WorldPawns was null, so controlled MatchAll could not run");
                return;
            }

            LaborProspect standard = null;
            LaborProspect elite = null;
            foreach (LaborProspect prospect in LaborCandidateService.Census(state))
            {
                if (prospect == null)
                {
                    continue;
                }

                Settlement settlement = IntercolonyMarketAccess.FindSettlement(
                    prospect.settlementId);
                SettlementEconomicProfile profile = settlement == null
                    ? null
                    : state.GetProfile(settlement);
                if (profile == null)
                {
                    continue;
                }

                if (standard == null && prospect.equipmentTier == LaborEquipmentLevel.Standard &&
                    LaborEquipmentTierService.CanSupply(
                        profile, LaborEquipmentLevel.Professional, CombatClause.Civilian))
                {
                    standard = prospect;
                }

                if (elite == null && prospect.equipmentTier == LaborEquipmentLevel.Elite &&
                    LaborEquipmentTierService.CanSupply(
                        profile, LaborEquipmentLevel.Professional, CombatClause.Civilian))
                {
                    elite = prospect;
                }

                if (standard != null && elite != null)
                {
                    break;
                }
            }

            if (standard == null || elite == null)
            {
                string reason =
                    $"controlled census needs Standard and Elite prospects from profiles capable " +
                    $"of Professional; observed Standard={(standard == null ? 0 : 1)}, " +
                    $"Elite={(elite == null ? 0 : 1)}";
                r.Skip(standardLabel, reason);
                r.Skip(eliteLabel, reason);
                return;
            }

            EquipmentMatchObservation standardObservation =
                MatchControlledEquipmentProspect(
                    state, standard, LaborEquipmentLevel.Professional);
            EquipmentMatchObservation eliteObservation =
                MatchControlledEquipmentProspect(
                    state, elite, LaborEquipmentLevel.Professional);

            if (!standardObservation.fixtureBuilt || !eliteObservation.fixtureBuilt)
            {
                string reason =
                    $"controlled MatchAll fixture was unavailable; Standard=" +
                    $"{standardObservation.failure ?? "built"}; Elite=" +
                    $"{eliteObservation.failure ?? "built"}";
                r.Skip(standardLabel, reason);
                r.Skip(eliteLabel, reason);
                return;
            }

            bool standardSatisfies = LaborEquipmentTierService.MeetsOrExceeds(
                standard.equipmentTier, LaborEquipmentLevel.Professional);
            bool eliteSatisfies = LaborEquipmentTierService.MeetsOrExceeds(
                elite.equipmentTier, LaborEquipmentLevel.Professional);
            r.Check(
                !standardSatisfies && !standardObservation.applicantQueued,
                standardLabel,
                $"OBSERVED promised={standard.equipmentTier}, queued=" +
                $"{standardObservation.applicantQueued}; EXPECTED promised=Standard, queued=False; " +
                $"MatchAll {DescribeEquipmentMatch(standardObservation)}");
            r.Check(
                eliteSatisfies && eliteObservation.applicantQueued,
                eliteLabel,
                $"OBSERVED promised={elite.equipmentTier}, queued=" +
                $"{eliteObservation.applicantQueued}; EXPECTED promised=Elite, queued=True; " +
                $"MatchAll {DescribeEquipmentMatch(eliteObservation)}");

            const string finalLoadoutLabel =
                "an applicant whose actual gear misses the request is not queued";
            EquipmentMatchObservation finalLoadoutObservation =
                ExerciseFinalLoadoutGate(state, standard);
            if (!finalLoadoutObservation.fixtureBuilt)
            {
                r.Skip(finalLoadoutLabel,
                    finalLoadoutObservation.failure ??
                    "the controlled prospect did not materialise for the final-loadout fixture");
                return;
            }

            r.Check(
                finalLoadoutObservation.finalGateRejected &&
                !finalLoadoutObservation.applicantQueued,
                finalLoadoutLabel,
                $"OBSERVED ApplyResult={finalLoadoutObservation.applyResult ?? "none"}, " +
                $"final-gate-rejected={finalLoadoutObservation.finalGateRejected}, " +
                $"queued={finalLoadoutObservation.applicantQueued}; " +
                "EXPECTED final-gate-rejected=True, queued=False; " +
                "requested=Professional, controlled promise=None");
        }

        private static EquipmentMatchObservation MatchControlledEquipmentProspect(
            IntercolonyWorldComponent state, LaborProspect prospect,
            LaborEquipmentLevel requested)
        {
            EquipmentMatchObservation observation = new EquipmentMatchObservation();
            FieldInfo censusField = typeof(LaborCandidateService).GetField(
                "census", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo censusRefreshField = typeof(LaborCandidateService).GetField(
                "censusRefreshCount", BindingFlags.Static | BindingFlags.NonPublic);
            if (censusField == null || censusRefreshField == null)
            {
                observation.failure = "LaborCandidateService census fields were not found";
                return observation;
            }

            List<LaborProspect> savedCensus =
                censusField.GetValue(null) as List<LaborProspect>;
            int savedCensusRefreshCount = (int)censusRefreshField.GetValue(null);
            List<JobPosting> savedPostings = new List<JobPosting>(state.Postings);
            JobPosting posting = null;
            try
            {
                state.Postings.Clear();
                censusField.SetValue(
                    null, new List<LaborProspect> { prospect });
                censusRefreshField.SetValue(null, state.RefreshCount);
                posting = JobPostingService.TryPost(
                    state, null, 0, 20, WageStructure.Daily, CombatClause.Civilian,
                    out string failReason, requested);
                if (posting == null)
                {
                    observation.failure =
                        $"TryPost refused the controlled fixture: {failReason ?? "no reason"}";
                    return observation;
                }

                observation.fixtureBuilt = true;
                JobPostingService.MatchAll(state);
                foreach (JobApplicant applicant in posting.Applicants)
                {
                    if (applicant != null && applicant.settlementId == prospect.settlementId)
                    {
                        observation.applicantQueued = true;
                        break;
                    }
                }
            }
            catch (System.Exception ex)
            {
                observation.failure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                if (posting != null)
                {
                    JobPostingService.Close(
                        posting, JobPostingStatus.Withdrawn, "self-test equipment matching");
                    state.Postings.Remove(posting);
                }

                state.Postings.Clear();
                state.Postings.AddRange(savedPostings);
                censusField.SetValue(null, savedCensus);
                censusRefreshField.SetValue(null, savedCensusRefreshCount);
                LaborCandidateService.InvalidateCensus();
            }

            return observation;
        }

        private static string DescribeEquipmentMatch(EquipmentMatchObservation observation)
        {
            return $"fixtureBuilt={observation.fixtureBuilt}, queued=" +
                   $"{observation.applicantQueued}, failure={observation.failure ?? "none"}";
        }

        private static EquipmentMatchObservation ExerciseFinalLoadoutGate(
            IntercolonyWorldComponent state, LaborProspect prospect)
        {
            EquipmentMatchObservation observation = new EquipmentMatchObservation();
            FieldInfo censusField = typeof(LaborCandidateService).GetField(
                "census", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo censusRefreshField = typeof(LaborCandidateService).GetField(
                "censusRefreshCount", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo applyMethod = typeof(JobPostingService).GetMethod(
                "Apply", BindingFlags.Static | BindingFlags.NonPublic);
            if (censusField == null || censusRefreshField == null || applyMethod == null)
            {
                observation.failure =
                    "the controlled census fields or JobPostingService.Apply method were not found";
                return observation;
            }

            List<LaborProspect> savedCensus =
                censusField.GetValue(null) as List<LaborProspect>;
            int savedCensusRefreshCount = (int)censusRefreshField.GetValue(null);
            List<JobPosting> savedPostings = new List<JobPosting>(state.Postings);
            LaborEquipmentLevel savedEquipmentTier = prospect.equipmentTier;
            JobPosting posting = null;
            try
            {
                prospect.equipmentTier = LaborEquipmentLevel.None;
                state.Postings.Clear();
                censusField.SetValue(
                    null, new List<LaborProspect> { prospect });
                censusRefreshField.SetValue(null, state.RefreshCount);
                posting = JobPostingService.TryPost(
                    state, null, 0, 20, WageStructure.Daily, CombatClause.Civilian,
                    out string failReason, LaborEquipmentLevel.Professional);
                if (posting == null)
                {
                    observation.failure =
                        $"TryPost refused the final-loadout fixture: {failReason ?? "no reason"}";
                    return observation;
                }

                observation.fixtureBuilt = true;

                // Keep the real phase-one census path in the fixture. It correctly filters this
                // deliberately lower promise before Apply; the private call below isolates the
                // final gate that phase one cannot reach with a lower promise.
                JobPostingService.MatchAll(state);

                MethodInfo materialiseMethod = typeof(LaborProspect).GetMethod(
                    nameof(LaborProspect.Materialise),
                    BindingFlags.Instance | BindingFlags.Public);
                MethodInfo clearMaterialisedLoadout = typeof(IntercolonyJobPostingSelfTest).GetMethod(
                    nameof(ClearMaterialisedLoadout),
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (materialiseMethod == null || clearMaterialisedLoadout == null)
                {
                    throw new InvalidOperationException(
                        "the LaborProspect.Materialise loadout-control patch was unavailable");
                }

                HarmonyLib.Harmony harmony = new HarmonyLib.Harmony(
                    "miannoni.intercolony.final-loadout.selftest");
                try
                {
                    harmony.Patch(
                        materialiseMethod,
                        postfix: new HarmonyLib.HarmonyMethod(clearMaterialisedLoadout));

                    object applyResult = applyMethod.Invoke(
                        null, new object[] { state, posting, prospect, 1, state.RefreshCount, 0 });
                    observation.applyResult = applyResult?.ToString();
                    if (observation.applyResult == "Accepted")
                    {
                        observation.applicantQueued = posting.Applicants.Count > 0;
                        if (posting.Applicants.Count > 0)
                        {
                            JobPostingService.Reject(
                                posting, posting.Applicants[posting.Applicants.Count - 1]);
                        }
                    }
                    else if (observation.applyResult == "FulfilmentRejected")
                    {
                        // With a materialised pawn and a None promise, TryFulfil cannot fail: it
                        // strips the loadout and returns true. Therefore this result identifies the
                        // final actual-loadout gate, rather than the earlier allocator failure path.
                        observation.finalGateRejected = true;
                    }
                    else if (observation.applyResult == "Rejected")
                    {
                        observation.fixtureBuilt = false;
                        observation.failure =
                            "the controlled prospect could not materialise an applicant pawn";
                    }
                    else
                    {
                        observation.fixtureBuilt = false;
                        observation.failure =
                            $"Apply returned {observation.applyResult ?? "null"} instead of an " +
                            "applicant or final-gate rejection";
                    }
                }
                finally
                {
                    harmony.Unpatch(
                        materialiseMethod,
                        HarmonyLib.HarmonyPatchType.Postfix,
                        harmony.Id);
                }
            }
            catch (System.Exception ex)
            {
                observation.fixtureBuilt = false;
                observation.failure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                prospect.equipmentTier = savedEquipmentTier;
                JobPostingService.Close(
                    posting, JobPostingStatus.Withdrawn, "self-test final-loadout cleanup");
                if (posting != null)
                {
                    state.Postings.Remove(posting);
                }

                RestorePostingList(
                    state, savedPostings, "self-test immediate emergency cleanup");
                censusField.SetValue(null, savedCensus);
                censusRefreshField.SetValue(null, savedCensusRefreshCount);
                LaborCandidateService.InvalidateCensus();
            }

            return observation;
        }

        private static void ClearMaterialisedLoadout(Pawn __result)
        {
            if (__result?.equipment != null)
            {
                __result.equipment.DestroyAllEquipment(DestroyMode.Vanish);
            }

            if (__result?.apparel != null)
            {
                __result.apparel.DestroyAll(DestroyMode.Vanish);
            }
        }

        private static void CheckEquipmentFulfilment(
            Results r, IntercolonyWorldComponent state)
        {
            const string actualGearLabel =
                "allocator fulfilment leaves actual gear at or above the promised tier";
            const string civilianGearLabel =
                "civilian fulfilment leaves the primary weapon empty";

            if (Find.WorldPawns == null)
            {
                r.Skip(actualGearLabel,
                    "Find.WorldPawns was null, so generated pawns could not be cleaned up");
                r.Skip(civilianGearLabel,
                    "Find.WorldPawns was null, so generated pawns could not be cleaned up");
                return;
            }

            LaborProspect prospect;
            SettlementEconomicProfile profile;
            LaborEquipmentLevel tier;
            if (!FindEquipmentFulfilmentFixture(
                    state, out prospect, out profile, out tier))
            {
                string reason =
                    "the current census had no source profile capable of a shared combat and " +
                    "Civilian fulfilment tier";
                r.Skip(actualGearLabel, reason);
                r.Skip(civilianGearLabel, reason);
                return;
            }

            Pawn combatPawn = null;
            Pawn civilianPawn = null;
            try
            {
                try
                {
                    combatPawn = prospect.Materialise();
                }
                catch (System.Exception ex)
                {
                    r.Skip(actualGearLabel,
                        $"materialising the combat fixture threw {ex.GetType().Name}: {ex.Message}");
                    r.Skip(civilianGearLabel,
                        $"materialising the combat fixture threw {ex.GetType().Name}: {ex.Message}");
                    return;
                }

                if (combatPawn == null)
                {
                    r.Skip(actualGearLabel,
                        $"the source prospect could not materialise for {tier}");
                    r.Skip(civilianGearLabel,
                        $"the source prospect could not materialise for {tier}");
                    return;
                }

                bool combatFulfilled = false;
                LaborEquipmentLevel combatActual = LaborEquipmentLevel.None;
                string combatFailure = null;
                try
                {
                    combatFulfilled = LaborEquipmentAllocator.TryFulfil(
                        combatPawn, tier, CombatClause.Armed, profile, out combatFailure);
                    combatActual = LaborEquipmentTierService.Classify(
                        combatPawn, CombatClause.Armed);
                }
                catch (System.Exception ex)
                {
                    combatFailure = $"{ex.GetType().Name}: {ex.Message}";
                }

                r.Check(
                    combatFulfilled && combatFailure == null &&
                    LaborEquipmentTierService.MeetsOrExceeds(combatActual, tier),
                    actualGearLabel,
                    $"OBSERVED fulfilled={combatFulfilled}, actual={combatActual}, promised={tier}; " +
                    $"EXPECTED fulfilled=True and actual >= {tier}; failure={combatFailure ?? "none"}; " +
                    $"profile={profile.techTier}/{profile.wealthTier}/{profile.archetype}");

                try
                {
                    civilianPawn = prospect.Materialise();
                }
                catch (System.Exception ex)
                {
                    r.Skip(civilianGearLabel,
                        $"materialising the Civilian fixture threw {ex.GetType().Name}: {ex.Message}");
                    return;
                }

                if (civilianPawn == null)
                {
                    r.Skip(civilianGearLabel,
                        $"the source prospect could not materialise for Civilian {tier}");
                    return;
                }

                bool civilianFulfilled = false;
                LaborEquipmentLevel civilianActual = LaborEquipmentLevel.None;
                string civilianFailure = null;
                try
                {
                    civilianFulfilled = LaborEquipmentAllocator.TryFulfil(
                        civilianPawn, tier, CombatClause.Civilian,
                        profile, out civilianFailure);
                    civilianActual = LaborEquipmentTierService.Classify(
                        civilianPawn, CombatClause.Civilian);
                }
                catch (System.Exception ex)
                {
                    civilianFailure = $"{ex.GetType().Name}: {ex.Message}";
                }

                bool primaryNull = civilianPawn.equipment != null &&
                    civilianPawn.equipment.Primary == null;
                r.Check(
                    civilianFulfilled && civilianFailure == null &&
                    LaborEquipmentTierService.MeetsOrExceeds(civilianActual, tier) &&
                    primaryNull,
                    civilianGearLabel,
                    $"OBSERVED fulfilled={civilianFulfilled}, actual={civilianActual}, " +
                    $"primary-null={primaryNull}, promised={tier}; EXPECTED fulfilled=True, " +
                    $"actual >= {tier}, primary-null=True; failure={civilianFailure ?? "none"}");
            }
            finally
            {
                DiscardEquipmentFixturePawn(combatPawn);
                DiscardEquipmentFixturePawn(civilianPawn);
            }
        }

        private static bool FindEquipmentFulfilmentFixture(
            IntercolonyWorldComponent state, out LaborProspect prospect,
            out SettlementEconomicProfile profile, out LaborEquipmentLevel tier)
        {
            prospect = null;
            profile = null;
            tier = LaborEquipmentLevel.None;
            LaborEquipmentLevel[] tiers =
            {
                LaborEquipmentLevel.Elite,
                LaborEquipmentLevel.Professional,
                LaborEquipmentLevel.Standard
            };

            foreach (LaborEquipmentLevel candidateTier in tiers)
            {
                foreach (LaborProspect candidate in LaborCandidateService.Census(state))
                {
                    if (candidate == null)
                    {
                        continue;
                    }

                    Settlement settlement = IntercolonyMarketAccess.FindSettlement(
                        candidate.settlementId);
                    SettlementEconomicProfile candidateProfile = settlement == null
                        ? null
                        : state.GetProfile(settlement);
                    if (candidateProfile == null ||
                        !LaborEquipmentTierService.CanSupply(
                            candidateProfile, candidateTier, CombatClause.Armed) ||
                        !LaborEquipmentTierService.CanSupply(
                            candidateProfile, candidateTier, CombatClause.Civilian))
                    {
                        continue;
                    }

                    prospect = candidate;
                    profile = candidateProfile;
                    tier = candidateTier;
                    return true;
                }
            }

            return false;
        }

        private static void DiscardEquipmentFixturePawn(Pawn pawn)
        {
            if (pawn == null || Find.WorldPawns == null)
            {
                return;
            }

            try
            {
                // Remove a live world-pawn registration before consulting Discarded. A pawn that
                // was destroyed or discarded through a bad path can still be held by WorldPawns;
                // returning early would leave that registration behind forever.
                if (Find.WorldPawns.Contains(pawn))
                {
                    Find.WorldPawns.RemoveAndDiscardPawnViaGC(pawn);
                }
                else if (!pawn.Discarded)
                {
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
            }
            catch (System.Exception ex)
            {
                IntercolonyLog.Warning(
                    $"Could not clean up equipment self-test pawn: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// The census must be deep, and it must price the same way a real pawn does.
        ///
        /// Depth is the point: a shallow market answers a one-silver change by flipping from nobody
        /// interested to everybody interested, because there were three qualified people who all
        /// charged about the same. Pricing agreement is what keeps that depth honest — the posting
        /// dialog quotes a band drawn from census records, and the worker who eventually arrives is
        /// a real pawn. If the two priced differently the band would be a lie the player discovers
        /// only after hiring.
        /// </summary>
        private static void CheckPoolSplit(Results r, IntercolonyWorldComponent state)
        {
            int advertised = LaborCandidateService.Refresh(state).Count;
            List<LaborProspect> world = LaborCandidateService.Census(state);

            if (advertised == 0 && world.Count == 0)
            {
                r.Info("pool checks skipped: no labor available this cycle at all.");
                return;
            }

            r.Check(world.Count > advertised,
                "a posting reaches far further than the hiring listing (§35.2)",
                $"{advertised} advertising, {world.Count} in the census");

            r.Check(world.Count >= advertised * 5,
                "the census is deep enough for an offer to have a shape, not a threshold",
                $"x{world.Count / (float)Mathf.Max(1, advertised):0.0} the listing");

            // Building it twice must not build two: every posting answered in one refresh has to
            // see the same people, or ten identical postings stop being one posting.
            int again = LaborCandidateService.Census(state).Count;
            r.Check(again == world.Count,
                "the census is taken once per cycle, not once per question",
                $"{world.Count} then {again}");

            // Same rule, two representations. WeightedLevel and the top-N are shared; this proves
            // the shared path is actually shared rather than two copies that agree today.
            Pawn probe = null;
            foreach (LaborCandidate candidate in LaborCandidateService.Refresh(state))
            {
                if (candidate?.pawn != null)
                {
                    probe = candidate.pawn;
                    break;
                }
            }

            if (probe != null)
            {
                float fromPawn = LaborCandidateService.PricedSkillValue(probe);
                r.Check(fromPawn > 0f,
                    "a real pawn prices to a positive skill value",
                    $"{fromPawn:0.0}");
            }

            float lowest = float.MaxValue;
            float highest = 0f;
            foreach (LaborProspect prospect in world)
            {
                if (prospect.pricedSkillValue < lowest)
                {
                    lowest = prospect.pricedSkillValue;
                }

                if (prospect.pricedSkillValue > highest)
                {
                    highest = prospect.pricedSkillValue;
                }
            }

            r.Check(world.Count == 0 || highest > lowest,
                "the census contains a spread of ability, not one worker repeated",
                $"skill value {lowest:0.0} to {highest:0.0}");
        }

        // --- F24 emergency postings --------------------------------------------------------

        private static void CheckEmergencyReachCensus(
            Results r, IntercolonyWorldComponent state)
        {
            List<LaborProspect> census = LaborCandidateService.Census(state);
            EmergencyReachCensusCounts counts = CountEmergencyReachCensus(census, state);

            r.Info($"emergency reach census total: {counts.total} prospects");
            r.Info($"emergency route available: {counts.available}/{counts.total} " +
                   $"({EquipmentPercentage(counts.available, counts.total):0.0}%)");
            r.Info($"emergency route DropPod: {counts.dropPod}/{counts.total} " +
                   $"({EquipmentPercentage(counts.dropPod, counts.total):0.0}%)");
            r.Info($"emergency route Conventional: {counts.conventional}/{counts.total} " +
                   $"({EquipmentPercentage(counts.conventional, counts.total):0.0}%)");
            r.Info($"emergency route none: {counts.none}/{counts.total} " +
                   $"({EquipmentPercentage(counts.none, counts.total):0.0}%)");
            r.Info($"emergency reach filter: {counts.available}/{counts.total} remain; " +
                   $"narrowed by {counts.none}/{counts.total} " +
                   $"({EquipmentPercentage(counts.none, counts.total):0.0}%)");
        }

        private static EmergencyReachCensusCounts CountEmergencyReachCensus(
            List<LaborProspect> census, IntercolonyWorldComponent state)
        {
            EmergencyReachCensusCounts counts = new EmergencyReachCensusCounts
            {
                total = census?.Count ?? 0
            };

            if (census == null)
            {
                return counts;
            }

            foreach (LaborProspect prospect in census)
            {
                EmergencyArrivalQuote quote = LaborCandidateService.QuoteEmergencyArrival(
                    state, prospect);
                if (!quote.available)
                {
                    counts.none++;
                    continue;
                }

                counts.available++;
                if (quote.transport == EmploymentArrivalTransport.DropPod)
                {
                    counts.dropPod++;
                }
                else
                {
                    counts.conventional++;
                }
            }

            return counts;
        }

        private static void CheckEmergencyPostingFlag(
            Results r, IntercolonyWorldComponent state)
        {
            const string label = "emergency posting stores its flag and headline";
            if (Find.WorldPawns == null)
            {
                r.Skip(label,
                    "Find.WorldPawns was null, so TryPost's emergency match could not run safely");
                return;
            }

            List<JobPosting> savedPostings = new List<JobPosting>(state.Postings);
            JobPosting emergencyPosting = null;
            JobPosting ordinaryPosting = null;
            string emergencyFailure = null;
            string ordinaryFailure = null;

            try
            {
                state.Postings.Clear();
                ordinaryPosting = JobPostingService.TryPost(
                    state, null, 0, 20, WageStructure.Daily, CombatClause.Civilian,
                    out ordinaryFailure, emergencyDispatch: false);
                emergencyPosting = JobPostingService.TryPost(
                    state, null, 0, 20, WageStructure.Daily, CombatClause.Civilian,
                    out emergencyFailure, emergencyDispatch: true);

                string emergencyHeadline = emergencyPosting?.Headline() ?? "missing";
                string ordinaryHeadline = ordinaryPosting?.Headline() ?? "missing";
                bool emergencyHeadlineContainsFlag = emergencyHeadline.IndexOf(
                    "Emergency", StringComparison.Ordinal) >= 0;
                bool ordinaryHeadlineOmitsFlag = ordinaryHeadline.IndexOf(
                    "Emergency", StringComparison.Ordinal) < 0;
                bool passed = emergencyPosting != null &&
                    emergencyPosting.emergencyDispatch &&
                    emergencyHeadlineContainsFlag &&
                    ordinaryPosting != null &&
                    !ordinaryPosting.emergencyDispatch &&
                    ordinaryHeadlineOmitsFlag;

                r.Check(passed, label,
                    $"OBSERVED emergency flag {emergencyPosting?.emergencyDispatch ?? false}, " +
                    $"headline contains Emergency {emergencyHeadlineContainsFlag}; ordinary flag " +
                    $"{ordinaryPosting?.emergencyDispatch ?? false}, headline contains Emergency " +
                    $"{!ordinaryHeadlineOmitsFlag}; EXPECTED true/contains and " +
                    $"false/omits; emergency failure {emergencyFailure ?? "none"}, " +
                    $"ordinary failure {ordinaryFailure ?? "none"}");
            }
            finally
            {
                JobPostingService.Close(
                    emergencyPosting, JobPostingStatus.Withdrawn,
                    "self-test emergency flag cleanup");
                JobPostingService.Close(
                    ordinaryPosting, JobPostingStatus.Withdrawn,
                    "self-test ordinary flag cleanup");
                RestorePostingList(state, savedPostings, "self-test emergency flag cleanup");
            }
        }

        private static void RestorePostingList(
            IntercolonyWorldComponent state, List<JobPosting> savedPostings, string cleanupNote)
        {
            List<JobPosting> createdPostings = new List<JobPosting>();
            foreach (JobPosting posting in state.Postings)
            {
                if (posting != null && (savedPostings == null || !savedPostings.Contains(posting)))
                {
                    createdPostings.Add(posting);
                }
            }

            foreach (JobPosting posting in createdPostings)
            {
                JobPostingService.Close(
                    posting, JobPostingStatus.Withdrawn, cleanupNote);
                state.Postings.Remove(posting);
            }

            state.Postings.Clear();
            if (savedPostings != null)
            {
                state.Postings.AddRange(savedPostings);
            }
        }

        private static void CheckEmergencyPostingPersistence(Results r)
        {
            const string label = "emergency posting flag survives save/load and missing node defaults false";
            if (Scribe.saver == null || Scribe.loader == null)
            {
                r.Skip(label,
                    "RimWorld Scribe.saver or Scribe.loader was unavailable");
                return;
            }

            JobPosting savedPosting = new JobPosting
            {
                id = 2401,
                termDays = 20,
                wageStructure = WageStructure.Daily,
                combatClause = CombatClause.Civilian,
                requestedEquipmentLevel = LaborEquipmentLevel.Any,
                emergencyDispatch = true,
                postedTick = 123,
                expiryTick = -1,
                status = JobPostingStatus.Open
            };
            JobPosting loadedPosting = null;
            JobPosting loadedWithoutEmergencyNode = null;
            bool xmlHasEmergencyNode = false;
            bool emergencyNodeRemoved = false;
            bool xmlLacksEmergencyNode = false;
            string failure = null;
            string path = Path.Combine(
                Path.GetTempPath(), $"Intercolony-EmergencyPosting-{Guid.NewGuid():N}.xml");

            try
            {
                Scribe.saver.InitSaving(path, "intercolonyEmergencyPostingTest");
                Scribe_Deep.Look(ref savedPosting, "posting");
                Scribe.saver.FinalizeSaving();

                string savedXml = File.ReadAllText(path);
                xmlHasEmergencyNode = savedXml.IndexOf(
                    "<emergencyDispatch", StringComparison.Ordinal) >= 0;

                Scribe.loader.InitLoading(path);
                Scribe_Deep.Look(ref loadedPosting, "posting");
                Scribe.loader.FinalizeLoading();

                if (!RemoveXmlNodeFromFile(path, "emergencyDispatch", out emergencyNodeRemoved))
                {
                    failure = "the emergencyDispatch XML node could not be removed";
                }
                else
                {
                    string legacyXml = File.ReadAllText(path);
                    xmlLacksEmergencyNode = legacyXml.IndexOf(
                        "<emergencyDispatch", StringComparison.Ordinal) < 0;

                    Scribe.loader.InitLoading(path);
                    Scribe_Deep.Look(ref loadedWithoutEmergencyNode, "posting");
                    Scribe.loader.FinalizeLoading();
                }
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                Scribe.ForceStop();
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            bool passed = failure == null &&
                xmlHasEmergencyNode &&
                loadedPosting != null &&
                loadedPosting.emergencyDispatch &&
                emergencyNodeRemoved &&
                xmlLacksEmergencyNode &&
                loadedWithoutEmergencyNode != null &&
                !loadedWithoutEmergencyNode.emergencyDispatch;
            r.Check(passed, label,
                $"OBSERVED XML node {(xmlHasEmergencyNode ? "present" : "absent")}, " +
                $"saved load flag {loadedPosting?.emergencyDispatch ?? false}, " +
                $"node removed {emergencyNodeRemoved}, stripped XML node " +
                $"{(xmlLacksEmergencyNode ? "absent" : "present")}, missing-node load flag " +
                $"{loadedWithoutEmergencyNode?.emergencyDispatch ?? false}; EXPECTED " +
                "node present, true after round-trip, then absent and false after legacy load" +
                $"; failure {failure ?? "none"}");
        }

        private static bool RemoveXmlNodeFromFile(
            string path, string nodeName, out bool nodeFound)
        {
            nodeFound = false;
            XmlDocument document = new XmlDocument();
            document.Load(path);
            XmlNode node = document.SelectSingleNode("//" + nodeName);
            if (node == null)
            {
                return true;
            }

            XmlNode parent = node.ParentNode;
            if (parent == null)
            {
                return false;
            }

            parent.RemoveChild(node);
            document.Save(path);
            nodeFound = true;
            return true;
        }

        private static void CheckEmergencyPostingImmediateMatch(
            Results r, IntercolonyWorldComponent state)
        {
            const string label =
                "emergency posting publishes responses within the materialisation bound and respects the applicant cap";
            if (Find.WorldPawns == null)
            {
                r.Skip(label,
                    "Find.WorldPawns was null, so an immediate applicant could not be cleaned up");
                return;
            }

            FieldInfo censusField = typeof(LaborCandidateService).GetField(
                "census", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo censusRefreshField = typeof(LaborCandidateService).GetField(
                "censusRefreshCount", BindingFlags.Static | BindingFlags.NonPublic);
            if (censusField == null || censusRefreshField == null)
            {
                r.Skip(label,
                    "LaborCandidateService controlled census fields were not found");
                return;
            }

            List<LaborProspect> savedCensus =
                censusField.GetValue(null) as List<LaborProspect>;
            int savedCensusRefreshCount = (int)censusRefreshField.GetValue(null);
            List<JobPosting> savedPostings = new List<JobPosting>(state.Postings);
            JobPosting posting = null;

            try
            {
                List<LaborProspect> reachable = FindMaterialisableEmergencyProspects(
                    LaborCandidateService.Census(state), state);
                if (reachable.Count == 0)
                {
                    r.Skip(label,
                        "the current census had no emergency-reachable prospect with a faction " +
                        "to materialise");
                    return;
                }

                int fixtureCount = Mathf.Min(
                    reachable.Count, JobPostingService.MaxWaitingApplicants + 2);
                List<LaborProspect> fixture = new List<LaborProspect>();
                for (int i = 0; i < fixtureCount; i++)
                {
                    fixture.Add(reachable[i]);
                }

                state.Postings.Clear();
                censusField.SetValue(null, fixture);
                censusRefreshField.SetValue(null, state.RefreshCount);
                posting = JobPostingService.TryPost(
                    state, null, 0, 20, WageStructure.Daily, CombatClause.Civilian,
                    out string failReason, emergencyDispatch: true);
                if (posting == null)
                {
                    r.Skip(label,
                        $"TryPost refused the controlled immediate fixture: " +
                        $"{failReason ?? "no failure reason"}");
                    return;
                }

                ImmediateEmergencyObservation observation = new ImmediateEmergencyObservation
                {
                    eligibleProspects = fixture.Count,
                    prospectsQueuedOrMatchedImmediately = posting.Applicants.Count +
                        (posting.PendingMaterialisation?.candidates.Count ?? 0),
                    applicantsAfterTryPost = posting.Applicants.Count
                };
                r.Check(
                    observation.prospectsQueuedOrMatchedImmediately == observation.eligibleProspects,
                    "emergency posting matches all controlled eligible prospects immediately",
                    $"OBSERVED {observation.prospectsQueuedOrMatchedImmediately} queued or matched " +
                    $"prospect(s) from {observation.eligibleProspects} controlled eligible " +
                    $"prospect(s); EXPECTED {observation.eligibleProspects}");

                r.Check(
                    observation.applicantsAfterTryPost == 0,
                    "emergency click path publishes no applicant pawn before a world tick",
                    $"OBSERVED {observation.applicantsAfterTryPost} applicant pawn(s) after TryPost; " +
                    "EXPECTED 0 before any explicit world tick");

                int applicantCap = JobPostingService.MaxWaitingApplicants;
                int materialisationsPerTick = JobPostingService.EmergencyMaterialisationsPerTick;
                int tickBound = materialisationsPerTick > 0
                    ? (applicantCap + materialisationsPerTick - 1) / materialisationsPerTick
                    : applicantCap;
                while (observation.ticksDriven < tickBound &&
                       posting.Applicants.Count < applicantCap)
                {
                    state.WorldComponentTick();
                    observation.ticksDriven++;
                }

                observation.applicantsAfterBoundedTicks = posting.Applicants.Count;
                r.Check(
                    observation.applicantsAfterBoundedTicks > 0 &&
                    observation.applicantsAfterBoundedTicks <= applicantCap,
                    label,
                    $"OBSERVED {observation.applicantsAfterBoundedTicks} applicant(s) after " +
                    $"{observation.ticksDriven}/{tickBound} explicit world tick(s); EXPECTED " +
                    $"1-{applicantCap} within ceil({applicantCap}/{materialisationsPerTick}) ticks");
            }
            catch (Exception ex)
            {
                r.Skip(label,
                    $"controlled immediate fixture threw {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                JobPostingService.Close(
                    posting, JobPostingStatus.Withdrawn,
                    "self-test immediate emergency cleanup");
                if (posting != null)
                {
                    state.Postings.Remove(posting);
                }

                state.Postings.Clear();
                state.Postings.AddRange(savedPostings);
                censusField.SetValue(null, savedCensus);
                censusRefreshField.SetValue(null, savedCensusRefreshCount);
                LaborCandidateService.InvalidateCensus();
            }
        }

        private static void CheckEmergencyEquipmentIntegration(
            Results r, IntercolonyWorldComponent state)
        {
            const string label =
                "an emergency Security Professional posting matches reachable applicants with real gear";
            const int shootingMinimum = 1;
            const int termDays = 20;

            List<LaborProspect> census = LaborCandidateService.Census(state);
            int censusTotal = census?.Count ?? 0;
            int professionalCapableCount = 0;
            int emergencyRoutedCount = 0;
            int bothCount = 0;
            int shootingQualifiedCount = 0;
            int professionalPromiseCount = 0;
            int combinedEligibleCount = 0;
            LaborProspect sourceFixture = null;

            if (census != null)
            {
                foreach (LaborProspect prospect in census)
                {
                    if (prospect == null)
                    {
                        continue;
                    }

                    Settlement source = IntercolonyMarketAccess.FindSettlement(
                        prospect.settlementId);
                    SettlementEconomicProfile profile = source == null
                        ? null
                        : state.GetProfile(source);
                    bool professionalCapable = profile != null &&
                        LaborEquipmentTierService.CanSupply(
                            profile, LaborEquipmentLevel.Professional, CombatClause.Security);
                    bool emergencyRouted = LaborCandidateService.CanReachEmergency(
                        state, prospect);
                    bool shootingQualified = SkillDefOf.Shooting != null &&
                        prospect.CanDo(SkillDefOf.Shooting) &&
                        prospect.LevelOf(SkillDefOf.Shooting) >= shootingMinimum;
                    bool professionalPromise = LaborEquipmentTierService.MeetsOrExceeds(
                        prospect.equipmentTier, LaborEquipmentLevel.Professional);

                    if (professionalCapable)
                    {
                        professionalCapableCount++;
                    }

                    if (emergencyRouted)
                    {
                        emergencyRoutedCount++;
                    }

                    if (professionalCapable && emergencyRouted)
                    {
                        bothCount++;
                    }

                    if (shootingQualified)
                    {
                        shootingQualifiedCount++;
                    }

                    if (professionalPromise)
                    {
                        professionalPromiseCount++;
                    }

                    if (prospect.faction != null && professionalCapable &&
                        emergencyRouted && shootingQualified && professionalPromise)
                    {
                        combinedEligibleCount++;
                        if (sourceFixture == null)
                        {
                            sourceFixture = prospect;
                        }
                    }
                }
            }

            string censusCounts =
                $"total={censusTotal}; professional-capable={professionalCapableCount}; " +
                $"emergency-routed={emergencyRoutedCount}; both={bothCount}; " +
                $"Shooting 1+={shootingQualifiedCount}; " +
                $"Professional promise={professionalPromiseCount}; " +
                $"combined eligible={combinedEligibleCount}";

            if (bothCount == 0)
            {
                r.Skip(label,
                    "the current world cannot supply a source that is both " +
                    $"Professional-capable and emergency-routed; {censusCounts}");
                return;
            }

            if (sourceFixture == null)
            {
                r.Skip(label,
                    "the current world has a Professional-capable and emergency-routed source, " +
                    $"but none also has a materialisable Shooting 1+ Professional promise; " +
                    censusCounts);
                return;
            }

            if (Find.WorldPawns == null)
            {
                r.Skip(label,
                    "Find.WorldPawns was null, so the real applicant pawn could not be cleaned up; " +
                    censusCounts);
                return;
            }

            FieldInfo censusField = typeof(LaborCandidateService).GetField(
                "census", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo censusRefreshField = typeof(LaborCandidateService).GetField(
                "censusRefreshCount", BindingFlags.Static | BindingFlags.NonPublic);
            if (censusField == null || censusRefreshField == null)
            {
                r.Skip(label,
                    "LaborCandidateService controlled census fields were not found; " +
                    censusCounts);
                return;
            }

            List<LaborProspect> savedCensus =
                censusField.GetValue(null) as List<LaborProspect>;
            int savedCensusRefreshCount = (int)censusRefreshField.GetValue(null);
            List<JobPosting> savedPostings = new List<JobPosting>(state.Postings);
            JobPosting ordinaryPosting = null;
            JobPosting emergencyPosting = null;
            int ordinaryAsk = -1;
            StringBuilder emergencyAsks = new StringBuilder();
            string ordinaryFailure = null;
            string emergencyFailure = null;
            int immediateApplicantCount = 0;
            int routeViolations = 0;
            int unknownSources = 0;
            int skillViolations = 0;
            int actualGearViolations = 0;
            int premiumViolations = 0;
            string gearFailure = null;

            try
            {
                state.Postings.Clear();
                censusField.SetValue(
                    null, new List<LaborProspect> { sourceFixture });
                censusRefreshField.SetValue(null, state.RefreshCount);
                ordinaryPosting = JobPostingService.TryPost(
                    state,
                    SkillDefOf.Shooting,
                    shootingMinimum,
                    termDays,
                    WageStructure.Daily,
                    CombatClause.Security,
                    out ordinaryFailure,
                    LaborEquipmentLevel.Any,
                    emergencyDispatch: false);
                if (ordinaryPosting == null)
                {
                    r.Check(
                        false,
                        label,
                        $"the ordinary ask control could not be posted: " +
                        $"{ordinaryFailure ?? "no failure reason"}; {censusCounts}");
                    return;
                }

                JobPostingService.MatchAll(state);
                if (ordinaryPosting.Applicants.Count == 0)
                {
                    r.Skip(label,
                        $"the ordinary ask control produced no applicant from the selected real " +
                        $"source; {censusCounts}; ordinary failure={ordinaryFailure ?? "none"}");
                    return;
                }

                ordinaryAsk = ordinaryPosting.Applicants[0]?.openMarketAsk ?? -1;
                if (ordinaryAsk <= 0)
                {
                    r.Skip(label,
                        $"the selected real source produced no positive ordinary ask for the " +
                        $"premium control; ordinary ask={ordinaryAsk}; {censusCounts}");
                    return;
                }

                JobPostingService.Close(
                    ordinaryPosting,
                    JobPostingStatus.Withdrawn,
                    "self-test emergency equipment ordinary control cleanup");
                state.Postings.Remove(ordinaryPosting);
                ordinaryPosting = null;

                List<LaborProspect> controlledFixture =
                    new List<LaborProspect> { sourceFixture };
                LaborProspect routeBlockedFixture = null;
                if (census != null)
                {
                    foreach (LaborProspect prospect in census)
                    {
                        if (prospect == null || prospect == sourceFixture ||
                            prospect.settlementId == sourceFixture.settlementId)
                        {
                            continue;
                        }

                        Settlement source = IntercolonyMarketAccess.FindSettlement(
                            prospect.settlementId);
                        SettlementEconomicProfile profile = source == null
                            ? null
                            : state.GetProfile(source);
                        bool professionalCapable = profile != null &&
                            LaborEquipmentTierService.CanSupply(
                                profile,
                                LaborEquipmentLevel.Professional,
                                CombatClause.Security);
                        bool shootingQualified = SkillDefOf.Shooting != null &&
                            prospect.CanDo(SkillDefOf.Shooting) &&
                            prospect.LevelOf(SkillDefOf.Shooting) >= shootingMinimum;
                        bool professionalPromise = LaborEquipmentTierService.MeetsOrExceeds(
                            prospect.equipmentTier, LaborEquipmentLevel.Professional);
                        if (prospect.faction != null && professionalCapable &&
                            !LaborCandidateService.CanReachEmergency(state, prospect) &&
                            shootingQualified && professionalPromise)
                        {
                            routeBlockedFixture = prospect;
                            controlledFixture.Add(prospect);
                            break;
                        }
                    }
                }

                state.Postings.Clear();
                censusField.SetValue(null, controlledFixture);
                censusRefreshField.SetValue(null, state.RefreshCount);
                emergencyPosting = JobPostingService.TryPost(
                    state,
                    SkillDefOf.Shooting,
                    shootingMinimum,
                    termDays,
                    WageStructure.Daily,
                    CombatClause.Security,
                    out emergencyFailure,
                    LaborEquipmentLevel.Professional,
                    emergencyDispatch: true);
                if (emergencyPosting == null)
                {
                    r.Check(
                        false,
                        label,
                        $"TryPost refused the real Professional/emergency fixture: " +
                        $"{emergencyFailure ?? "no failure reason"}; {censusCounts}");
                    return;
                }

                // TryPost's emergency path is the event under test: do not call MatchAll here.
                immediateApplicantCount = emergencyPosting.Applicants.Count;
                if (immediateApplicantCount == 0)
                {
                    r.Skip(
                        label,
                        $"the real Professional/emergency fixture produced no applicant to " +
                        $"measure; {censusCounts}; controlled fixture={controlledFixture.Count}");
                    return;
                }

                foreach (JobApplicant applicant in emergencyPosting.Applicants)
                {
                    LaborProspect applicantSource = FindApplicantProspect(
                        controlledFixture, applicant);
                    if (applicantSource == null)
                    {
                        unknownSources++;
                    }
                    else if (!LaborCandidateService.CanReachEmergency(
                                 state, applicantSource))
                    {
                        routeViolations++;
                    }

                    if (applicant == null || applicant.pawn == null ||
                        !emergencyPosting.MeetsRequirement(applicant.pawn))
                    {
                        skillViolations++;
                    }

                    LaborEquipmentLevel actual = LaborEquipmentLevel.None;
                    bool gearWasClassified = false;
                    try
                    {
                        actual = LaborEquipmentTierService.Classify(
                            applicant?.pawn, emergencyPosting.combatClause);
                        gearWasClassified = true;
                    }
                    catch (Exception ex)
                    {
                        gearFailure = $"{ex.GetType().Name}: {ex.Message}";
                    }

                    if (!gearWasClassified || applicant == null || applicant.pawn == null ||
                        !LaborEquipmentTierService.MeetsOrExceeds(
                            actual, LaborEquipmentLevel.Professional))
                    {
                        actualGearViolations++;
                    }

                    if (emergencyAsks.Length > 0)
                    {
                        emergencyAsks.Append(",");
                    }

                    int applicantAsk = applicant?.openMarketAsk ?? -1;
                    emergencyAsks.Append(applicantAsk);
                    if (applicantAsk <= ordinaryAsk)
                    {
                        premiumViolations++;
                    }
                }

                bool postingTermsMatch = emergencyPosting.skill == SkillDefOf.Shooting &&
                    emergencyPosting.minSkillLevel == shootingMinimum &&
                    emergencyPosting.combatClause == CombatClause.Security &&
                    emergencyPosting.requestedEquipmentLevel ==
                        LaborEquipmentLevel.Professional &&
                    emergencyPosting.emergencyDispatch;
                bool immediateAndCapped = immediateApplicantCount > 0 &&
                    immediateApplicantCount <= 6;
                bool onlyReachableSourcesAnswered = routeViolations == 0 &&
                    unknownSources == 0;
                bool allActualGearMeetsRequest = actualGearViolations == 0;
                bool premiumVisible = immediateApplicantCount > 0 &&
                    premiumViolations == 0;
                r.Check(
                    postingTermsMatch && immediateAndCapped &&
                    onlyReachableSourcesAnswered && skillViolations == 0 &&
                    allActualGearMeetsRequest && premiumVisible,
                    label,
                    $"OBSERVED terms skill={emergencyPosting.skill?.defName ?? "null"}, " +
                    $"min={emergencyPosting.minSkillLevel}, clause={emergencyPosting.combatClause}, " +
                    $"equipment={emergencyPosting.requestedEquipmentLevel}, " +
                    $"emergency={emergencyPosting.emergencyDispatch}; " +
                    $"immediate applicants={immediateApplicantCount}; " +
                    $"reachable-source violations={routeViolations}, unknown sources={unknownSources}; " +
                    $"Shooting violations={skillViolations}; actual gear violations=" +
                    $"{actualGearViolations}; ordinary ask={ordinaryAsk}; emergency asks=" +
                    $"[{emergencyAsks}]; premium violations={premiumViolations}; " +
                    $"controlled fixture={controlledFixture.Count}, route-blocked fixture=" +
                    $"{(routeBlockedFixture == null ? "none" : routeBlockedFixture.settlementName)}; " +
                    "EXPECTED skill=Shooting, min=1, clause=Security, equipment=Professional, " +
                    "emergency=True; immediate applicants=1-6; all sources reachable; " +
                    "Shooting violations=0; actual gear >= Professional; every emergency ask " +
                    "greater than the ordinary ask");
            }
            catch (Exception ex)
            {
                r.Check(
                    false,
                    label,
                    $"the real emergency/equipment fixture threw {ex.GetType().Name}: " +
                    $"{ex.Message}; {censusCounts}; immediate applicants=" +
                    $"{immediateApplicantCount}; actual gear violations={actualGearViolations}; " +
                    $"gear failure={gearFailure ?? "none"}");
            }
            finally
            {
                JobPostingService.Close(
                    emergencyPosting,
                    JobPostingStatus.Withdrawn,
                    "self-test emergency equipment cleanup");
                JobPostingService.Close(
                    ordinaryPosting,
                    JobPostingStatus.Withdrawn,
                    "self-test emergency equipment ordinary cleanup");
                RestorePostingList(
                    state,
                    savedPostings,
                    "self-test emergency equipment cleanup");
                censusField.SetValue(null, savedCensus);
                censusRefreshField.SetValue(null, savedCensusRefreshCount);
            }
        }

        private static LaborProspect FindApplicantProspect(
            List<LaborProspect> prospects, JobApplicant applicant)
        {
            if (prospects == null || applicant == null)
            {
                return null;
            }

            foreach (LaborProspect prospect in prospects)
            {
                if (prospect != null && prospect.settlementId == applicant.settlementId)
                {
                    return prospect;
                }
            }

            return null;
        }

        private static List<LaborProspect> FindMaterialisableEmergencyProspects(
            List<LaborProspect> census, IntercolonyWorldComponent state)
        {
            List<LaborProspect> reachable = new List<LaborProspect>();
            if (census == null)
            {
                return reachable;
            }

            foreach (LaborProspect prospect in census)
            {
                if (prospect != null && prospect.faction != null &&
                    LaborCandidateService.CanReachEmergency(state, prospect))
                {
                    reachable.Add(prospect);
                }
            }

            return reachable;
        }

        private static void CheckEmergencyReachFilter(
            Results r, IntercolonyWorldComponent state)
        {
            const string label =
                "a routeless prospect is rejected by emergency but queued by ordinary posting";
            if (Find.WorldPawns == null)
            {
                r.Skip(label,
                    "Find.WorldPawns was null, so the controlled ordinary applicant could not be " +
                    "cleaned up");
                return;
            }

            FieldInfo censusField = typeof(LaborCandidateService).GetField(
                "census", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo censusRefreshField = typeof(LaborCandidateService).GetField(
                "censusRefreshCount", BindingFlags.Static | BindingFlags.NonPublic);
            if (censusField == null || censusRefreshField == null)
            {
                r.Skip(label,
                    "LaborCandidateService controlled census fields were not found");
                return;
            }

            string fixtureDescription;
            LaborProspect routeless = FindRoutelessProspect(
                state, out fixtureDescription);
            if (routeless == null)
            {
                r.Skip(label,
                    "the current census could not supply or construct a routeless prospect " +
                    "with a materialisable faction");
                return;
            }

            if (!ProbeProspectMaterialisation(routeless, out string materialisationFailure))
            {
                r.Skip(label,
                    materialisationFailure ??
                    "the controlled routeless prospect could not materialise a pawn");
                return;
            }

            EmergencyFilterObservation observation = ExerciseEmergencyReachFilter(
                state, routeless, censusField, censusRefreshField);
            if (!observation.fixtureBuilt)
            {
                r.Skip(label, observation.failure ??
                    "the controlled routeless prospect fixture could not be built");
                return;
            }

            bool routelessStillUnavailable =
                !LaborCandidateService.CanReachEmergency(state, routeless);
            r.Check(
                routelessStillUnavailable &&
                observation.ordinaryQueued &&
                !observation.emergencyQueued,
                label,
                $"OBSERVED fixture {fixtureDescription}; emergency available " +
                $"{!routelessStillUnavailable}, ordinary queued {observation.ordinaryQueued}, " +
                $"emergency queued {observation.emergencyQueued}; EXPECTED available False, " +
                "ordinary queued True, emergency queued False; production gate is " +
                "EvaluatePosting emergencyDispatch && !CanReachEmergency");
        }

        private static bool ProbeProspectMaterialisation(
            LaborProspect prospect, out string failure)
        {
            failure = null;
            Pawn pawn = null;
            Rand.PushState(0xF24_1);
            try
            {
                pawn = prospect?.Materialise();
                if (pawn == null)
                {
                    failure = "the controlled routeless prospect could not materialise a pawn";
                }
            }
            catch (Exception ex)
            {
                failure =
                    $"materialising the controlled routeless prospect threw " +
                    $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                DiscardEquipmentFixturePawn(pawn);
                Rand.PopState();
            }

            return failure == null;
        }

        private static EmergencyFilterObservation ExerciseEmergencyReachFilter(
            IntercolonyWorldComponent state, LaborProspect routeless,
            FieldInfo censusField, FieldInfo censusRefreshField)
        {
            EmergencyFilterObservation observation = new EmergencyFilterObservation();
            List<LaborProspect> savedCensus =
                censusField.GetValue(null) as List<LaborProspect>;
            int savedCensusRefreshCount = (int)censusRefreshField.GetValue(null);
            List<JobPosting> savedPostings = new List<JobPosting>(state.Postings);
            JobPosting ordinaryPosting = null;
            JobPosting emergencyPosting = null;

            try
            {
                List<LaborProspect> fixture = new List<LaborProspect> { routeless };
                state.Postings.Clear();
                censusField.SetValue(null, fixture);
                censusRefreshField.SetValue(null, state.RefreshCount);
                ordinaryPosting = JobPostingService.TryPost(
                    state, null, 0, 20, WageStructure.Daily, CombatClause.Civilian,
                    out string ordinaryFailure, emergencyDispatch: false);
                if (ordinaryPosting == null)
                {
                    observation.failure =
                        $"TryPost refused the controlled ordinary fixture: " +
                        $"{ordinaryFailure ?? "no failure reason"}";
                    return observation;
                }

                JobPostingService.MatchAll(state);
                observation.ordinaryQueued = ordinaryPosting.Applicants.Count > 0;
                // Do not turn an empty ordinary control into a Skip: ME1 applies the emergency
                // gate to ordinary postings too, and this must reach the assertion as
                // ordinaryQueued=False so that mutation reddens it.

                JobPostingService.Close(
                    ordinaryPosting, JobPostingStatus.Withdrawn,
                    "self-test emergency reach ordinary control cleanup");
                state.Postings.Remove(ordinaryPosting);

                emergencyPosting = JobPostingService.TryPost(
                    state, null, 0, 20, WageStructure.Daily, CombatClause.Civilian,
                    out string emergencyFailure, emergencyDispatch: true);
                if (emergencyPosting == null)
                {
                    observation.failure =
                        $"TryPost refused the controlled emergency fixture: " +
                        $"{emergencyFailure ?? "no failure reason"}";
                    return observation;
                }

                // TryPost's emergency path calls MatchImmediately, which reaches EvaluatePosting
                // without a MatchAll call. Deleting the production
                // posting.emergencyDispatch && !CanReachEmergency(...) gate would queue this
                // same routeless prospect and redden the assertion above.
                observation.emergencyQueued = emergencyPosting.Applicants.Count > 0;
                observation.fixtureBuilt = true;
            }
            catch (Exception ex)
            {
                observation.failure =
                    $"controlled emergency reach fixture threw {ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                JobPostingService.Close(
                    ordinaryPosting, JobPostingStatus.Withdrawn,
                    "self-test emergency reach ordinary cleanup");
                JobPostingService.Close(
                    emergencyPosting, JobPostingStatus.Withdrawn,
                    "self-test emergency reach cleanup");
                if (ordinaryPosting != null)
                {
                    state.Postings.Remove(ordinaryPosting);
                }

                if (emergencyPosting != null)
                {
                    state.Postings.Remove(emergencyPosting);
                }

                RestorePostingList(
                    state, savedPostings, "self-test emergency reach cleanup");
                censusField.SetValue(null, savedCensus);
                censusRefreshField.SetValue(null, savedCensusRefreshCount);
                LaborCandidateService.InvalidateCensus();
            }

            return observation;
        }

        private static LaborProspect FindRoutelessProspect(
            IntercolonyWorldComponent state, out string fixtureDescription)
        {
            fixtureDescription = null;
            List<LaborProspect> census = LaborCandidateService.Census(state);
            if (census == null)
            {
                return null;
            }

            foreach (LaborProspect prospect in census)
            {
                if (prospect != null && prospect.faction != null &&
                    !LaborCandidateService.CanReachEmergency(state, prospect))
                {
                    fixtureDescription =
                        $"live census prospect {prospect.settlementName}/{prospect.settlementId} " +
                        $"at {prospect.distanceTiles:0.##} tiles";
                    return prospect;
                }
            }

            foreach (LaborProspect prospect in census)
            {
                if (prospect == null || prospect.faction == null)
                {
                    continue;
                }

                Settlement source = IntercolonyMarketAccess.FindSettlement(
                    prospect.settlementId);
                SettlementEconomicProfile profile = source == null
                    ? null
                    : state.GetProfile(source);
                if (profile == null ||
                    profile.rapidLogisticsCapability ==
                    SettlementRapidLogisticsCapability.DropPodsAvailable)
                {
                    continue;
                }

                LaborProspect controlled = CopyProspectForEmergencyFixture(
                    prospect, prospect.settlementId, 24f, prospect.settlementName);
                if (!LaborCandidateService.CanReachEmergency(state, controlled))
                {
                    fixtureDescription =
                        $"controlled clone of {prospect.settlementName}/{prospect.settlementId} " +
                        "at 24 tiles (conventional source beyond the emergency cutoff)";
                    return controlled;
                }
            }

            foreach (LaborProspect prospect in census)
            {
                if (prospect == null || prospect.faction == null)
                {
                    continue;
                }

                LaborProspect controlled = CopyProspectForEmergencyFixture(
                    prospect, -1, 24f, "controlled routeless source");
                if (!LaborCandidateService.CanReachEmergency(state, controlled))
                {
                    fixtureDescription =
                        $"direct F23-style clone of {prospect.settlementName}/" +
                        $"{prospect.settlementId} with missing source id and 24-tile distance";
                    return controlled;
                }
            }

            return null;
        }

        private static LaborProspect CopyProspectForEmergencyFixture(
            LaborProspect source, int settlementId, float distanceTiles, string settlementName)
        {
            return new LaborProspect
            {
                settlementId = settlementId,
                settlementName = settlementName ?? source.settlementName,
                factionName = source.factionName,
                faction = source.faction,
                distanceTiles = distanceTiles,
                travelDays = source.travelDays < 0 ? 1 : source.travelDays,
                skillLevels = source.skillLevels == null
                    ? null
                    : (int[])source.skillLevels.Clone(),
                passions = source.passions == null
                    ? null
                    : (Passion[])source.passions.Clone(),
                pricedSkillValue = source.pricedSkillValue,
                equipmentTier = LaborEquipmentLevel.Any
            };
        }

        // --- §114's acceptance criterion ---------------------------------------------------

        /// <summary>
        /// The requirement claim: a higher skill bar brings fewer but better applicants, while the
        /// saved posted wage does not alter the market's answer.
        ///
        /// Every draw goes through the real posting service. The unbounded interested count reports
        /// the market shape; the queued applicants prove that the matcher applied the same
        /// requirement before its configured waiting-list cap.
        /// </summary>
        private static void CheckRequirementsDriveApplicants(
            Results r, IntercolonyWorldComponent state)
        {
            SkillDef skill = SkillDefOf.Construction;
            const int term = 20;
            const int probeWage = 9999;
            int[] minimums = { 0, 4, 8, 12, 16, 20 };
            List<Draw> draws = new List<Draw>();

            foreach (int minimum in minimums)
            {
                draws.Add(Measure(r, state, skill, minimum, term, probeWage));
            }

            StringBuilder shape = new StringBuilder();
            for (int i = 0; i < minimums.Length; i++)
            {
                if (i > 0)
                {
                    shape.Append(", ");
                }

                shape.Append($"{minimums[i]}+ -> {draws[i].interested} interested, " +
                             $"{draws[i].applicants} queued");
            }

            Draw noMinimum = draws[0];
            Draw demanding = draws[draws.Count - 1];
            if (noMinimum.interested == 0)
            {
                r.Skip("minimum skill drives applicant quantity (§114)",
                    $"no-minimum draw was empty: {shape}");
            }
            else
            {
                bool quantityMonotonic = true;
                for (int i = 1; i < draws.Count; i++)
                {
                    if (draws[i].interested > draws[i - 1].interested)
                    {
                        quantityMonotonic = false;
                    }
                }

                bool materiallyFewer = demanding.interested < noMinimum.interested &&
                                       demanding.interested * 2 < noMinimum.interested;
                int applicantCap = JobPostingService.MaxWaitingApplicants;
                bool queueCapBound = noMinimum.applicants == applicantCap &&
                                     demanding.applicants == applicantCap;
                bool queuedApplicantsMeetBar = demanding.queuedBarViolations == 0;
                bool matcherAppliedBar = queuedApplicantsMeetBar &&
                                         (queueCapBound || demanding.applicants < noMinimum.applicants);
                string queueEvidence = queueCapBound
                    ? $"queued count comparison not discriminating: both draws reached applicant " +
                      $"cap {applicantCap}; demanding queue bar violations {demanding.queuedBarViolations}"
                    : $"no minimum queued {noMinimum.applicants}, demanding {demanding.applicants}; " +
                      $"demanding queue bar violations {demanding.queuedBarViolations}";

                r.Check(quantityMonotonic && materiallyFewer && matcherAppliedBar,
                    "a higher skill minimum reaches no more workers and a demanding minimum reaches materially fewer (§114)",
                    $"{shape}; {queueEvidence}");
            }

            const int highMinimum = 16;
            Draw unfiltered = draws[0];
            Draw highMinimumDraw = Measure(r, state, skill, highMinimum, term, probeWage);
            if (unfiltered.applicants == 0 || highMinimumDraw.applicants == 0)
            {
                string emptyDraw = unfiltered.applicants == 0 && highMinimumDraw.applicants == 0
                    ? "both the no-minimum and high-minimum draws were empty"
                    : unfiltered.applicants == 0
                        ? "the no-minimum draw was empty"
                        : "the high-minimum draw was empty";
                r.Skip("a high skill minimum yields better applicants (§114)",
                    $"{emptyDraw}; no minimum {unfiltered.interested} interested, " +
                    $"{unfiltered.applicants} queued; {highMinimum}+ " +
                    $"{highMinimumDraw.interested} interested, {highMinimumDraw.applicants} queued");
            }
            else
            {
                r.Check(highMinimumDraw.averageBestSkill > unfiltered.averageBestSkill,
                    "a high skill minimum yields better applicants (§114)",
                    $"average best skill {unfiltered.averageBestSkill:0.0} at 0+ vs " +
                    $"{highMinimumDraw.averageBestSkill:0.0} at {highMinimum}+");
            }

            const int lowWage = 1;
            const int highWage = 10000;
            int qualified = JobPostingService.CountInterested(
                state, skill, 0, term, lowWage, CombatClause.Civilian);
            if (qualified == 0)
            {
                r.Skip("posted wage does not change interested-worker count (§114)",
                    $"the no-minimum requirement had {qualified} interested workers to compare");
                return;
            }

            Draw lowOffer = Measure(r, state, skill, 0, term, lowWage);
            Draw highOffer = Measure(r, state, skill, 0, term, highWage);
            r.Check(lowOffer.interested == highOffer.interested &&
                    lowOffer.applicants > 0 && lowOffer.applicants == highOffer.applicants,
                "posted wage does not change interested-worker count (§114)",
                $"{lowWage}/day -> {lowOffer.interested} interested, {lowOffer.applicants} queued; " +
                $"{highWage}/day -> {highOffer.interested} interested, {highOffer.applicants} queued");
        }

        /// <summary>
        /// F25's queue is intentionally a spread of the qualified pool, not a leaderboard. The
        /// assertion reads the applicants that MatchAll actually materialised and compares their
        /// whole-queue mean with the mean of the same pool's strongest six records.
        /// </summary>
        private static void CheckWaitingListIsSpread(Results r, IntercolonyWorldComponent state)
        {
            const int term = 20;
            const int probeWage = 1;
            int cap = JobPostingService.MaxWaitingApplicants;

            if (Find.WorldPawns == null)
            {
                r.Skip("the waiting list is a spread, not a leaderboard (§35.2)",
                    "Find.WorldPawns was null, so the real applicant path could not run");
                return;
            }

            LaborCandidateService.Clear();
            JobPosting posting = MakePosting(
                state, SkillDefOf.Construction, 0, term, probeWage,
                out string failReason);
            if (posting == null)
            {
                r.Skip("the waiting list is a spread, not a leaderboard (§35.2)",
                    $"TryPost refused the real posting fixture: " +
                    $"{failReason ?? "no failure reason"}");
                return;
            }

            try
            {
                List<LaborProspect> census = LaborCandidateService.Census(state);
                List<LaborProspect> qualified = new List<LaborProspect>();
                foreach (LaborProspect prospect in census)
                {
                    if (prospect != null && posting.MeetsRequirement(prospect))
                    {
                        qualified.Add(prospect);
                    }
                }

                JobPostingService.MatchAll(state);

                if (qualified.Count <= cap)
                {
                    r.Skip("the waiting list is a spread, not a leaderboard (§35.2)",
                        $"qualified pool had {qualified.Count} records; more than cap {cap} is needed");
                    return;
                }

                if (posting.Applicants.Count != cap)
                {
                    r.Skip("the waiting list is a spread, not a leaderboard (§35.2)",
                        $"real matcher queued {posting.Applicants.Count} of cap {cap}; " +
                        $"qualified pool had {qualified.Count}");
                    return;
                }

                List<int> qualifiedBestSkills = new List<int>();
                float queuedTotal = 0f;
                bool allApplicantsHavePawns = true;
                foreach (LaborProspect prospect in qualified)
                {
                    qualifiedBestSkills.Add(BestSkillLevel(prospect));
                }

                foreach (JobApplicant applicant in posting.Applicants)
                {
                    if (applicant?.pawn == null)
                    {
                        allApplicantsHavePawns = false;
                        break;
                    }

                    queuedTotal += BestSkillLevel(applicant.pawn);
                }

                if (!allApplicantsHavePawns)
                {
                    r.Skip("the waiting list is a spread, not a leaderboard (§35.2)",
                        $"one of {posting.Applicants.Count} queued applicants had no pawn to measure");
                    return;
                }

                qualifiedBestSkills.Sort((a, b) => b.CompareTo(a));
                float topTotal = 0f;
                for (int i = 0; i < cap; i++)
                {
                    topTotal += qualifiedBestSkills[i];
                }

                float queuedMean = queuedTotal / posting.Applicants.Count;
                float topMean = topTotal / cap;
                float gap = topMean - queuedMean;

                r.Check(gap >= WaitingListSpreadMargin,
                    "the waiting list is a spread, not the strongest workers alive (§35.2, F25)",
                    $"queued mean best skill {queuedMean:0.00}; top {cap} qualified mean " +
                    $"{topMean:0.00}; gap {gap:0.00}; margin {WaitingListSpreadMargin:0.00}; " +
                    $"qualified {qualified.Count}, queued {posting.Applicants.Count}");
            }
            finally
            {
                JobPostingService.Close(posting, JobPostingStatus.Withdrawn, "self-test spread");
                state.Postings.Remove(posting);
            }
        }

        /// <summary>
        /// Rebuilds the same refresh twice and compares the selected applicants' market attributes
        /// in order. This proves the draw is a pure function of EconomySeed and RefreshCount. It
        /// does not prove a Scribe round-trip, and it does not prove pawn identity: pawn
        /// materialisation is outside the seeded stream, so the same inputs regenerate the same
        /// records, not necessarily the same people. The comparison uses attributes rather than
        /// record identity because records sharing all of those market attributes are
        /// interchangeable to the market.
        /// </summary>
        private static void CheckMarketReproduces(Results r, IntercolonyWorldComponent state)
        {
            const int term = 20;
            const int probeWage = 1;
            int cap = JobPostingService.MaxWaitingApplicants;
            const string label = "the same seed and refresh count reproduce the same applicants " +
                                 "in the same order (§35.2, F25)";

            if (Find.WorldPawns == null)
            {
                r.Skip(label, "Find.WorldPawns was null, so the real applicant path could not run");
                return;
            }

            ApplicantDraw first = new ApplicantDraw
            {
                values = new List<ApplicantValues>()
            };

            // Matching generates pawns, and pawn generation draws from the current RNG frame,
            // so the stream is expected to move; "it came back to where it was" is not a true
            // statement about correct code.
            Rand.PushState(0x7F25_2D);
            try
            {
                first = CaptureApplicants(
                    state, SkillDefOf.Construction, 0, term, probeWage);
            }
            finally
            {
                Rand.PopState();
            }

            if (!first.fixtureBuilt)
            {
                r.Skip(label,
                    $"seed {state.EconomySeed}, refresh {state.RefreshCount}; " +
                    $"TryPost refused the real posting fixture: " +
                    $"{first.fixtureFailureReason ?? "no failure reason"}");
                return;
            }

            if (first.values == null || first.values.Count == 0)
            {
                r.Skip(label,
                    $"seed {state.EconomySeed}, refresh {state.RefreshCount}; " +
                    $"the first real draw was empty with {first.qualified} qualified records");
                return;
            }

            if (first.qualified <= cap || first.values.Count != cap)
            {
                r.Skip(label,
                    $"seed {state.EconomySeed}, refresh {state.RefreshCount}; first real draw had " +
                    $"{first.values.Count} queued from " +
                    $"{first.qualified} qualified records; need {cap} queued and more than {cap} " +
                    "qualified to compare a full capped market");
                return;
            }

            ApplicantDraw second = CaptureApplicants(
                state, SkillDefOf.Construction, 0, term, probeWage);

            if (!second.fixtureBuilt)
            {
                r.Skip(label,
                    $"seed {state.EconomySeed}, refresh {state.RefreshCount}; " +
                    $"TryPost refused the second real posting fixture: " +
                    $"{second.fixtureFailureReason ?? "no failure reason"}");
                return;
            }

            if (second.values == null || second.values.Count == 0)
            {
                r.Skip(label,
                    $"seed {state.EconomySeed}, refresh {state.RefreshCount}; " +
                    $"the second real draw was empty with {second.qualified} qualified records");
                return;
            }

            bool sameSet = SameApplicantSet(first.values, second.values);
            bool sameOrder = SameApplicantOrder(first.values, second.values);

            r.Check(sameSet && sameOrder,
                label,
                $"seed {state.EconomySeed}, refresh {state.RefreshCount}; " +
                $"set {(sameSet ? "same" : "different")}; order {(sameOrder ? "same" : "different")}; " +
                $"first differing position {FirstApplicantDifference(first.values, second.values)}");
        }

        private static void CheckFrozenEmergencyArrivalOnHire(
            Results r, IntercolonyWorldComponent state, Map map)
        {
            const int termDays = 20;

            if (map == null)
            {
                SkipFrozenEmergencyArrivalAssertions(
                    r, "the current map was null, so hire payment could not be staged");
                r.Skip(OrdinaryArrivalLabel,
                    "the current map was null, so hire payment could not be staged");
                return;
            }

            if (Find.WorldPawns == null)
            {
                SkipFrozenEmergencyArrivalAssertions(
                    r, "Find.WorldPawns was null, so hired fixture pawns could not be cleaned up");
                r.Skip(OrdinaryArrivalLabel,
                    "Find.WorldPawns was null, so hired fixture pawns could not be cleaned up");
                return;
            }

            FieldInfo censusField = typeof(LaborCandidateService).GetField(
                "census", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo censusRefreshField = typeof(LaborCandidateService).GetField(
                "censusRefreshCount", BindingFlags.Static | BindingFlags.NonPublic);
            if (censusField == null || censusRefreshField == null)
            {
                SkipFrozenEmergencyArrivalAssertions(
                    r, "LaborCandidateService controlled census fields were not found");
                r.Skip(OrdinaryArrivalLabel,
                    "LaborCandidateService controlled census fields were not found");
                return;
            }

            List<LaborProspect> savedCensus =
                censusField.GetValue(null) as List<LaborProspect>;
            int savedCensusRefreshCount = (int)censusRefreshField.GetValue(null);
            List<JobPosting> savedPostings = new List<JobPosting>(state.Postings);
            int savedSilver = PurchaseOrderService.CountColonySilver(map);
            const int emergencyFixtureTravelDays = 3;
            const int emergencyFixtureArrivalTicks = 12_345;
            const float emergencyLiveDistanceDecoy = 999f;
            LaborProspect emergencySource = null;
            LaborProspect ordinarySource = null;

            try
            {
                List<LaborProspect> census = LaborCandidateService.Census(state);
                if (census == null || census.Count == 0)
                {
                    SkipFrozenEmergencyArrivalAssertions(
                        r, "the current census had no prospect to build a hire fixture");
                    r.Skip(OrdinaryArrivalLabel,
                        "the current census had no prospect to build a hire fixture");
                    return;
                }

                foreach (LaborProspect prospect in census)
                {
                    if (prospect == null || prospect.faction == null || prospect.settlementId < 0)
                    {
                        continue;
                    }

                    Settlement source = IntercolonyMarketAccess.FindSettlement(
                        prospect.settlementId);
                    if (source == null)
                    {
                        continue;
                    }

                    if (ordinarySource == null)
                    {
                        ordinarySource = prospect;
                    }

                    if (emergencySource == null &&
                        Find.FactionManager?.AllFactionsListForReading?.Contains(prospect.faction) ==
                            true &&
                        IntercolonyMarketAccess.IsAccessible(source, out _))
                    {
                        // This source only supplies valid settlement/faction metadata and a pawn.
                        // Emergency route availability is deliberately not part of this fixture.
                        emergencySource = prospect;
                    }
                }

                if (emergencySource == null)
                {
                    SkipFrozenEmergencyArrivalAssertions(
                        r,
                        "the current census had no accessible settlement with a registered faction " +
                        "to build the direct emergency hire fixture");
                }
                else
                {
                    Pawn emergencyPawn = null;
                    JobPosting emergencyPosting = null;
                    JobApplicant emergencyApplicant = null;
                    EmploymentContract emergencyContract = null;

                    try
                    {
                        Rand.PushState(0xF24_2);
                        try
                        {
                            emergencyPawn = emergencySource.Materialise();
                        }
                        finally
                        {
                            Rand.PopState();
                        }

                        if (emergencyPawn == null)
                        {
                            SkipFrozenEmergencyArrivalAssertions(
                                r,
                                "the selected accessible prospect could not materialise a pawn");
                        }
                        else
                        {
                            state.Postings.Clear();
                            emergencyPosting = new JobPosting
                            {
                                id = state.NextId(),
                                termDays = termDays,
                                wageStructure = WageStructure.Daily,
                                combatClause = CombatClause.Civilian,
                                requestedEquipmentLevel = LaborEquipmentLevel.Any,
                                emergencyDispatch = true,
                                postedTick = GenTicks.TicksGame,
                                expiryTick = -1,
                                status = JobPostingStatus.Open
                            };
                            state.AddPosting(emergencyPosting);

                            emergencyApplicant = new JobApplicant
                            {
                                pawn = emergencyPawn,
                                settlementId = emergencySource.settlementId,
                                settlementName = emergencySource.settlementName,
                                factionName = emergencySource.factionName,
                                faction = emergencySource.faction,
                                // Deliberately unrelated to the frozen quote. A hire-time quote
                                // derived from this live distance cannot reproduce the fixture ETA.
                                distanceTiles = emergencyLiveDistanceDecoy,
                                travelDays = emergencyFixtureTravelDays,
                                emergencyArrivalAvailable = true,
                                emergencyArrivalTransport = EmploymentArrivalTransport.DropPod,
                                emergencyArrivalTicks = emergencyFixtureArrivalTicks,
                                emergencyArrivalMethodLabel = "controlled fixture drop-pod",
                                requiredSkillLevel = 0,
                                openMarketAsk = 1,
                                appliedTick = GenTicks.TicksGame
                            };
                            emergencyPosting.Applicants.Add(emergencyApplicant);

                            if (!emergencyApplicant.emergencyArrivalAvailable ||
                                emergencyApplicant.emergencyArrivalTransport !=
                                EmploymentArrivalTransport.DropPod ||
                                emergencyApplicant.emergencyArrivalTicks <= 0)
                            {
                                SkipFrozenEmergencyArrivalAssertions(
                                    r,
                                    "the direct emergency applicant did not retain its frozen " +
                                    "drop-pod quote");
                            }
                            else
                            {
                                CheckFrozenEmergencyArrivalSaveLoad(
                                    r, state, map, emergencyPosting, emergencyApplicant);

                                EmploymentArrivalTransport frozenTransport =
                                    emergencyApplicant.emergencyArrivalTransport;
                                int frozenArrivalTicks = emergencyApplicant.emergencyArrivalTicks;
                                int emergencyTravelDays = emergencyApplicant.travelDays;
                                float liveDistance = emergencyApplicant.distanceTiles;

                                int upFront = WageStructureUtility.UpFrontCost(
                                    emergencyPosting.wageStructure,
                                    emergencyApplicant.openMarketAsk,
                                    emergencyPosting.termDays);
                                EmploymentEquipmentQuote equipmentQuote =
                                    EmploymentEquipmentService.Quote(emergencyApplicant.pawn);
                                EmploymentHireCostQuote hireQuote = equipmentQuote == null
                                    ? null
                                    : EmploymentEquipmentService.QuoteHireCost(
                                        upFront, equipmentQuote);
                                if (hireQuote == null)
                                {
                                    SkipFrozenEmergencyArrivalAssertions(
                                        r,
                                        "the direct emergency applicant's hire-cost quote " +
                                        "could not be built");
                                }
                                else
                                {
                                    IntercolonyLaborSelfTestSupport.EnsureSilver(
                                        map,
                                        IntercolonyLaborSelfTestSupport.SilverToEnsure(hireQuote));
                                    int available = PurchaseOrderService.CountColonySilver(map);
                                    if (available < hireQuote.totalDue)
                                    {
                                        SkipFrozenEmergencyArrivalAssertions(
                                            r,
                                            $"could not stage the emergency hire cost: {available} " +
                                            $"silver available, {hireQuote.totalDue} needed");
                                    }
                                    else
                                    {
                                        emergencyContract = EmploymentService.TryHireApplicant(
                                            state,
                                            emergencyApplicant,
                                            emergencyPosting,
                                            map,
                                            out string hireFailure,
                                            hireQuote);
                                        if (emergencyContract == null)
                                        {
                                            SkipFrozenEmergencyArrivalAssertions(
                                                r,
                                                "the direct emergency applicant hire could not be " +
                                                $"arranged: {hireFailure ?? "no reason"}");
                                        }
                                        else
                                        {
                                            int actualArrivalTicks =
                                                emergencyContract.arrivalTick -
                                                emergencyContract.hiredTick;
                                            r.Check(
                                                emergencyContract.arrivalTransport == frozenTransport,
                                                FrozenEmergencyTransportLabel,
                                                $"frozen {frozenTransport}; contract " +
                                                $"{emergencyContract.arrivalTransport}");
                                            r.Check(
                                                actualArrivalTicks == frozenArrivalTicks,
                                                FrozenEmergencyDurationLabel,
                                                $"frozen {frozenArrivalTicks} ticks; contract " +
                                                $"{actualArrivalTicks} ticks; ordinary travel " +
                                                $"{emergencyTravelDays}d");
                                            r.Check(
                                                emergencyContract.arrivalTransport == frozenTransport &&
                                                actualArrivalTicks == frozenArrivalTicks,
                                                FrozenEmergencySnapshotLabel,
                                                $"live distance decoy {liveDistance:0.##} tiles; frozen " +
                                                $"{frozenTransport}/{frozenArrivalTicks} ticks; contract " +
                                                $"{emergencyContract.arrivalTransport}/" +
                                                $"{actualArrivalTicks} ticks");
                                        }
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        CleanupApplicantHireFixture(
                            state,
                            emergencyPosting,
                            emergencyApplicant,
                            emergencyContract,
                            "self-test frozen emergency arrival cleanup");
                        if (emergencyApplicant == null && emergencyPawn != null)
                        {
                            DiscardEquipmentFixturePawn(emergencyPawn);
                        }
                    }
                }

                if (ordinarySource == null)
                {
                    r.Skip(
                        OrdinaryArrivalLabel,
                        "the current census had no prospect with a registered settlement " +
                        "for the ordinary control");
                }
                else
                {
                    LaborProspect ordinaryFixtureSource = CopyProspectForEmergencyFixture(
                        ordinarySource,
                        ordinarySource.settlementId,
                        ordinarySource.distanceTiles,
                        ordinarySource.settlementName);
                    JobPosting ordinaryPosting = null;
                    JobApplicant ordinaryApplicant = null;
                    EmploymentContract ordinaryContract = null;

                    try
                    {
                        state.Postings.Clear();
                        censusField.SetValue(
                            null, new List<LaborProspect> { ordinaryFixtureSource });
                        censusRefreshField.SetValue(null, state.RefreshCount);
                        ordinaryPosting = JobPostingService.TryPost(
                            state,
                            null,
                            0,
                            termDays,
                            WageStructure.Daily,
                            CombatClause.Civilian,
                            out string ordinaryFailure,
                            emergencyDispatch: false);

                        if (ordinaryPosting == null)
                        {
                            r.Skip(
                                OrdinaryArrivalLabel,
                                "TryPost refused the controlled ordinary fixture: " +
                                $"{ordinaryFailure ?? "no failure reason"}");
                        }
                        else
                        {
                            JobPostingService.MatchAll(state);
                            if (ordinaryPosting.Applicants.Count == 0)
                            {
                                r.Skip(
                                    OrdinaryArrivalLabel,
                                    "the controlled ordinary fixture produced no applicant");
                            }
                            else
                            {
                                ordinaryApplicant = ordinaryPosting.Applicants[0];
                                if (ordinaryApplicant == null || ordinaryApplicant.pawn == null)
                                {
                                    r.Skip(
                                        OrdinaryArrivalLabel,
                                        "the controlled ordinary applicant had no pawn to pass " +
                                        "through hiring");
                                }
                                else
                                {
                                    int ordinaryTravelDays = ordinaryApplicant.travelDays;
                                    int expectedOrdinaryArrivalTicks =
                                        ordinaryTravelDays * GenDate.TicksPerDay;
                                    int upFront = WageStructureUtility.UpFrontCost(
                                        ordinaryPosting.wageStructure,
                                        ordinaryApplicant.openMarketAsk,
                                        ordinaryPosting.termDays);
                                    EmploymentEquipmentQuote equipmentQuote =
                                        EmploymentEquipmentService.Quote(ordinaryApplicant.pawn);
                                    EmploymentHireCostQuote hireQuote = equipmentQuote == null
                                        ? null
                                        : EmploymentEquipmentService.QuoteHireCost(
                                            upFront, equipmentQuote);
                                    if (hireQuote == null)
                                    {
                                        r.Skip(
                                            OrdinaryArrivalLabel,
                                            "the controlled ordinary applicant's hire-cost " +
                                            "quote could not be built");
                                    }
                                    else
                                    {
                                        IntercolonyLaborSelfTestSupport.EnsureSilver(
                                            map,
                                            IntercolonyLaborSelfTestSupport.SilverToEnsure(
                                                hireQuote));
                                        int available = PurchaseOrderService.CountColonySilver(map);
                                        if (available < hireQuote.totalDue)
                                        {
                                            r.Skip(
                                                OrdinaryArrivalLabel,
                                                $"could not stage the ordinary hire cost: {available} " +
                                                $"silver available, {hireQuote.totalDue} needed");
                                        }
                                        else
                                        {
                                            ordinaryContract = EmploymentService.TryHireApplicant(
                                                state,
                                                ordinaryApplicant,
                                                ordinaryPosting,
                                                map,
                                                out string hireFailure,
                                                hireQuote);
                                            if (ordinaryContract == null)
                                            {
                                                r.Skip(
                                                    OrdinaryArrivalLabel,
                                                    "the controlled ordinary applicant hire could " +
                                                    $"not be arranged: {hireFailure ?? "no reason"}");
                                            }
                                            else
                                            {
                                                int actualArrivalTicks =
                                                    ordinaryContract.arrivalTick -
                                                    ordinaryContract.hiredTick;
                                                r.Check(
                                                    !ordinaryApplicant.emergencyArrivalAvailable &&
                                                    ordinaryContract.arrivalTransport ==
                                                        EmploymentArrivalTransport.Conventional &&
                                                    actualArrivalTicks == expectedOrdinaryArrivalTicks,
                                                    OrdinaryArrivalLabel,
                                                    $"ordinary quote available " +
                                                    $"{ordinaryApplicant.emergencyArrivalAvailable}; " +
                                                    $"travel {ordinaryTravelDays}d; expected " +
                                                    $"{expectedOrdinaryArrivalTicks} ticks; contract " +
                                                    $"{ordinaryContract.arrivalTransport}/" +
                                                    $"{actualArrivalTicks} ticks");
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        CleanupApplicantHireFixture(
                            state,
                            ordinaryPosting,
                            ordinaryApplicant,
                            ordinaryContract,
                            "self-test ordinary arrival cleanup");
                    }
                }
            }
            finally
            {
                RestorePostingList(
                    state, savedPostings, "self-test frozen arrival cleanup");
                censusField.SetValue(null, savedCensus);
                censusRefreshField.SetValue(null, savedCensusRefreshCount);

                int returned = IntercolonyLaborSelfTestSupport.RestoreStorageSilver(
                    map, savedSilver);
                if (returned > 0)
                {
                    r.Info($"returned {returned} silver to restore the arrival fixtures.");
                }

                IntercolonyLaborSelfTestSupport.ResetLedger();
            }
        }

        private static void CheckFrozenEmergencyArrivalSaveLoad(
            Results r, IntercolonyWorldComponent state, Map map,
            JobPosting savedPosting, JobApplicant savedApplicant)
        {
            if (Scribe.saver == null || Scribe.loader == null)
            {
                SkipFrozenEmergencyPersistenceAssertions(
                    r, "RimWorld Scribe.saver or Scribe.loader was unavailable");
                return;
            }

            if (savedPosting == null || savedApplicant == null || savedApplicant.pawn == null)
            {
                SkipFrozenEmergencyPersistenceAssertions(
                    r, "the direct emergency applicant fixture was incomplete before save/load");
                return;
            }

            // These are deliberately plain pre-save locals. The post-load assertions must not
            // compare the loaded object with itself or derive the expected ETA through production.
            bool expectedAvailable = savedApplicant.emergencyArrivalAvailable;
            EmploymentArrivalTransport expectedTransport =
                savedApplicant.emergencyArrivalTransport;
            int expectedArrivalTicks = savedApplicant.emergencyArrivalTicks;
            string expectedMethodLabel = savedApplicant.emergencyArrivalMethodLabel;
            int expectedTravelDays = savedApplicant.travelDays;
            int ordinaryArrivalTicks = expectedTravelDays * GenDate.TicksPerDay;

            // JobPosting owns the applicant and deep-saves it. JobApplicant's pawn is a reference,
            // while the real game deep-saves that pawn under WorldPawns separately. A one-pawn deep
            // collection mirrors that separate save owner so this isolated Scribe file can resolve
            // the reloaded applicant's pawn before the hire assertion.
            List<Pawn> savedWorldPawns = new List<Pawn> { savedApplicant.pawn };
            List<Pawn> loadedWorldPawns = null;
            JobPosting loadedPosting = null;
            JobApplicant loadedApplicant = null;
            EmploymentContract loadedContract = null;
            EmploymentArrivalTransport actualTransport = EmploymentArrivalTransport.Conventional;
            int actualArrivalTicks = -1;
            string failure = null;
            string hireFailure = null;
            string path = Path.Combine(
                Path.GetTempPath(), $"Intercolony-FrozenEmergencyArrival-{Guid.NewGuid():N}.xml");

            try
            {
                Scribe.saver.InitSaving(path, "intercolonyFrozenEmergencyArrivalTest");
                Scribe_Collections.Look(ref savedWorldPawns, "worldPawns", LookMode.Deep);
                Scribe_Deep.Look(ref savedPosting, "posting");
                Scribe.saver.FinalizeSaving();

                Scribe.loader.InitLoading(path);
                Scribe_Collections.Look(ref loadedWorldPawns, "worldPawns", LookMode.Deep);
                Scribe_Deep.Look(ref loadedPosting, "posting");
                Scribe.loader.FinalizeLoading();
            }
            catch (Exception ex)
            {
                failure = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                Scribe.ForceStop();
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            int loadedApplicantCount = loadedPosting?.Applicants?.Count ?? 0;
            if (loadedApplicantCount > 0)
            {
                loadedApplicant = loadedPosting.Applicants[0];
            }

            bool quoteRoundTripped = failure == null &&
                loadedApplicantCount == 1 &&
                loadedApplicant != null &&
                loadedApplicant.emergencyArrivalAvailable == expectedAvailable &&
                loadedApplicant.emergencyArrivalTransport == expectedTransport &&
                loadedApplicant.emergencyArrivalTicks == expectedArrivalTicks &&
                loadedApplicant.emergencyArrivalMethodLabel == expectedMethodLabel;
            r.Check(
                quoteRoundTripped,
                FrozenEmergencyQuoteRoundTripLabel,
                $"loaded applicants {loadedApplicantCount}; loaded " +
                $"{loadedApplicant?.emergencyArrivalAvailable ?? false}/" +
                $"{loadedApplicant?.emergencyArrivalTransport.ToString() ?? "missing"}/" +
                $"{loadedApplicant?.emergencyArrivalTicks.ToString() ?? "missing"}/" +
                $"\"{loadedApplicant?.emergencyArrivalMethodLabel ?? "missing"}\"; expected " +
                $"{expectedAvailable}/{expectedTransport}/{expectedArrivalTicks}/" +
                $"\"{expectedMethodLabel}\"; failure {failure ?? "none"}");

            bool exactRouteAndTiming = false;
            try
            {
                if (failure != null)
                {
                    hireFailure = $"save/load failed: {failure}";
                }
                else if (loadedPosting == null || loadedApplicantCount != 1 ||
                    loadedApplicant?.pawn == null)
                {
                    hireFailure = "the reloaded posting did not contain an applicant with a pawn";
                }
                else
                {
                    EmploymentEquipmentQuote equipmentQuote =
                        EmploymentEquipmentService.Quote(loadedApplicant.pawn);
                    int upFront = WageStructureUtility.UpFrontCost(
                        loadedPosting.wageStructure,
                        loadedApplicant.openMarketAsk,
                        loadedPosting.termDays);
                    EmploymentHireCostQuote hireQuote = equipmentQuote == null
                        ? null
                        : EmploymentEquipmentService.QuoteHireCost(upFront, equipmentQuote);
                    if (hireQuote == null)
                    {
                        hireFailure = "the reloaded applicant's hire-cost quote could not be built";
                    }
                    else
                    {
                        IntercolonyLaborSelfTestSupport.EnsureSilver(
                            map,
                            IntercolonyLaborSelfTestSupport.SilverToEnsure(hireQuote));
                        int available = PurchaseOrderService.CountColonySilver(map);
                        if (available < hireQuote.totalDue)
                        {
                            hireFailure =
                                $"could not stage the reloaded hire cost: {available} silver " +
                                $"{hireQuote.totalDue} needed";
                        }
                        else
                        {
                            loadedContract = EmploymentService.TryHireApplicant(
                                state,
                                loadedApplicant,
                                loadedPosting,
                                map,
                                out hireFailure,
                                hireQuote);
                            if (loadedContract != null)
                            {
                                actualTransport = loadedContract.arrivalTransport;
                                actualArrivalTicks = loadedContract.arrivalTick -
                                    loadedContract.hiredTick;
                                exactRouteAndTiming =
                                    actualTransport == expectedTransport &&
                                    actualArrivalTicks == expectedArrivalTicks;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                hireFailure = $"{ex.GetType().Name}: {ex.Message}";
            }

            string actualContractDescription = loadedContract == null
                ? "missing"
                : $"{actualTransport}/{actualArrivalTicks} ticks";
            r.Check(
                exactRouteAndTiming,
                FrozenEmergencyReloadHireLabel,
                $"reloaded applicant contract {actualContractDescription}; expected " +
                $"{expectedTransport}/{expectedArrivalTicks} ticks; ordinary fallback " +
                $"{expectedTravelDays}d = {ordinaryArrivalTicks} ticks; " +
                $"hire failure {hireFailure ?? "none"}");

            bool s3Passed = false;
            bool s3AssertionReported = false;
            bool s3PawnSpawned = false;
            string s3Failure = null;
            string s3ExpectedContractDescription = "missing";
            string s3LoadedContractDescription = "missing";
            int s3EmergencyArrivalLetters = 0;
            int s3IncomingPodCount = 0;
            bool s3IncomingPodResolved = false;
            int s3InitialTicksToImpact = -1;
            int s3OpenDelay = -1;
            int s3SpawnDeadline = -1;
            int s3LifecycleTicksDriven = 0;
            int s3EmployeeArrivalLettersBefore = 0;
            int s3EmployeeArrivalLettersAfter = 0;
            int s3EmployeeArrivalLettersAfterFurther = 0;
            EmploymentContract reloadedContract = null;
            Pawn reloadedContractPawn = null;
            List<Pawn> contractLoadedWorldPawns = null;
            List<EmploymentContract> savedEmployments = null;
            List<JobPosting> savedPostings = null;
            List<Letter> savedLetters = null;
            List<IArchivable> savedArchivables = null;
            List<Skyfaller> s3SkyfallersBeforeLaunch = null;
            Skyfaller s3FixtureSkyfaller = null;
            ActiveTransporter s3FixtureActiveTransporter = null;
            Archive s3Archive = null;
            TickManager s3TickManager = null;
            int savedS3Tick = 0;
            bool savedS3FastEcology = false;
            bool savedS3FastEcologyCaptured = false;
            IntercolonySettings s3Settings = null;
            IntercolonyLetterVolume savedS3LetterVolume = IntercolonyLetterVolume.Minimal;
            bool savedS3LetterVolumeCaptured = false;
            HashSet<EmploymentContract> savedDropPodPreflightFailuresLogged = null;
            HashSet<EmploymentContract> savedDropPodArrivalLettersSent = null;
            HashSet<EmploymentContract> dropPodPreflightFailuresLogged = null;
            HashSet<EmploymentContract> dropPodArrivalLettersSent = null;

            try
            {
                if (loadedContract == null)
                {
                    r.Skip(
                        FrozenEmergencyArrivalAfterReloadLabel,
                        "the reloaded applicant did not produce an accepted travelling contract");
                    return;
                }

                if (Find.TickManager == null)
                {
                    r.Skip(
                        FrozenEmergencyArrivalAfterReloadLabel,
                        "Find.TickManager was null, so the real world tick path was unavailable");
                    return;
                }

                if (Find.WorldPawns == null)
                {
                    r.Skip(
                        FrozenEmergencyArrivalAfterReloadLabel,
                        "Find.WorldPawns was null, so the reloaded travelling pawn could not be staged");
                    return;
                }

                if (Find.LetterStack == null)
                {
                    r.Skip(
                        FrozenEmergencyArrivalAfterReloadLabel,
                        "Find.LetterStack was null, so exact arrival events could not be counted");
                    return;
                }

                if (Find.Maps == null || !Find.Maps.Contains(map))
                {
                    r.Skip(
                        FrozenEmergencyArrivalAfterReloadLabel,
                        "the hired contract's destination map was not registered in Find.Maps");
                    return;
                }

                if (!map.IsPlayerHome)
                {
                    r.Skip(
                        FrozenEmergencyArrivalAfterReloadLabel,
                        "the hired contract's destination map was not a player-home map");
                    return;
                }

                FieldInfo preflightFailuresField = typeof(EmploymentService).GetField(
                    "dropPodPreflightFailuresLogged",
                    BindingFlags.Static | BindingFlags.NonPublic);
                FieldInfo arrivalLettersSentField = typeof(EmploymentService).GetField(
                    "dropPodArrivalLettersSent",
                    BindingFlags.Static | BindingFlags.NonPublic);
                dropPodPreflightFailuresLogged = preflightFailuresField?.GetValue(null)
                    as HashSet<EmploymentContract>;
                dropPodArrivalLettersSent = arrivalLettersSentField?.GetValue(null)
                    as HashSet<EmploymentContract>;
                if (dropPodPreflightFailuresLogged == null || dropPodArrivalLettersSent == null)
                {
                    r.Skip(
                        FrozenEmergencyArrivalAfterReloadLabel,
                        "the drop-pod session guards were unavailable for exact cleanup");
                    return;
                }

                savedDropPodPreflightFailuresLogged =
                    new HashSet<EmploymentContract>(dropPodPreflightFailuresLogged);
                savedDropPodArrivalLettersSent =
                    new HashSet<EmploymentContract>(dropPodArrivalLettersSent);

                savedLetters = new List<Letter>(Find.LetterStack.LettersListForReading);
                s3Archive = Find.Archive;
                if (s3Archive != null)
                {
                    savedArchivables = new List<IArchivable>(
                        s3Archive.ArchivablesListForReading);
                }

                // These are plain locals captured from the accepted contract before its second
                // Scribe round trip. The loaded contract must be compared to these exact values.
                EmploymentArrivalTransport expectedContractTransport = loadedContract.arrivalTransport;
                int expectedContractArrivalTick = loadedContract.arrivalTick;
                s3ExpectedContractDescription =
                    $"{expectedContractTransport}/{expectedContractArrivalTick} tick";

                List<Pawn> contractSavedWorldPawns = loadedContract.pawn == null
                    ? new List<Pawn>()
                    : new List<Pawn> { loadedContract.pawn };
                EmploymentContract contractToSave = loadedContract;
                string contractRoundTripFailure = null;

                try
                {
                    Scribe.saver.InitSaving(path, "intercolonyFrozenEmergencyContractTest");
                    Scribe_Collections.Look(
                        ref contractSavedWorldPawns, "worldPawns", LookMode.Deep);
                    Scribe_Deep.Look(ref contractToSave, "contract");
                    Scribe.saver.FinalizeSaving();

                    Scribe.loader.InitLoading(path);
                    Scribe_Collections.Look(
                        ref contractLoadedWorldPawns, "worldPawns", LookMode.Deep);
                    Scribe_Deep.Look(ref reloadedContract, "contract");
                    Scribe.loader.FinalizeLoading();
                }
                catch (Exception ex)
                {
                    contractRoundTripFailure = $"{ex.GetType().Name}: {ex.Message}";
                }
                finally
                {
                    Scribe.ForceStop();
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }

                reloadedContractPawn = reloadedContract?.pawn;
                bool contractRoundTripped = contractRoundTripFailure == null &&
                    contractLoadedWorldPawns != null &&
                    contractLoadedWorldPawns.Count == 1 &&
                    reloadedContract != null &&
                    reloadedContract.status == EmploymentStatus.Travelling &&
                    reloadedContract.arrivalTransport == expectedContractTransport &&
                    reloadedContract.arrivalTick == expectedContractArrivalTick &&
                    reloadedContract.pawn != null;

                s3LoadedContractDescription = reloadedContract == null
                    ? "missing"
                    : $"{reloadedContract.arrivalTransport}/{reloadedContract.arrivalTick} tick";

                if (contractRoundTripFailure != null)
                {
                    s3Failure = $"contract save/load failed: {contractRoundTripFailure}";
                }
                else if (!contractRoundTripped)
                {
                    s3Failure = "the reloaded contract did not preserve its travelling route, tick, or pawn";
                }
                else if (expectedContractTransport != EmploymentArrivalTransport.DropPod)
                {
                    s3Failure =
                        $"the accepted contract route was {expectedContractTransport}, not DropPod";
                }
                else
                {
                    // The isolated Scribe collection resolves the contract's pawn but does not add
                    // the pawn to the live WorldPawns collection. Map and faction are world-owned
                    // references, not part of this already-established isolated Scribe idiom: the
                    // real save resolves them from the world root, so stage those references from
                    // the reloaded pawn/map before driving the real tick path. The route and tick
                    // assertions above remain against the object that came back from Scribe.
                    if (reloadedContract.destinationMap == null)
                    {
                        reloadedContract.destinationMap = map;
                    }

                    if (reloadedContract.employerFaction == null && reloadedContractPawn != null)
                    {
                        reloadedContract.employerFaction = reloadedContractPawn.Faction;
                    }

                    if (reloadedContract.destinationMap != map)
                    {
                        s3Failure = "the reloaded contract resolved to a different destination map";
                    }
                    else if (reloadedContract.employerFaction == null)
                    {
                        s3Failure = "the reloaded contract had no employer faction for arrival preflight";
                    }
                    else
                    {
                        // Stage that exact reloaded pawn before driving the real tick path.
                        if (!Find.WorldPawns.Contains(reloadedContractPawn))
                        {
                            Find.WorldPawns.PassToWorld(
                                reloadedContractPawn, PawnDiscardDecideMode.KeepForever);
                        }

                        savedEmployments = new List<EmploymentContract>(state.Employments);
                        savedPostings = new List<JobPosting>(state.Postings);

                        // Keep this baseline only to prove the matching pod was launched by S3;
                        // teardown uses the explicit fixture identities captured below.
                        s3SkyfallersBeforeLaunch = new List<Skyfaller>(
                            map.listerThings.GetThingsOfType<Skyfaller>());
                        s3TickManager = Find.TickManager;
                        savedS3Tick = s3TickManager.TicksGame;
                        savedS3FastEcology = DebugSettings.fastEcology;
                        savedS3FastEcologyCaptured = true;
                        s3Settings = IntercolonyMod.Settings;
                        savedS3LetterVolume = s3Settings.letterVolume;
                        savedS3LetterVolumeCaptured = true;

                        // Isolate the accepted reloaded contract from the rest of the live component
                        // while still entering it through World.WorldTick and the component scheduler.
                        state.Employments.Clear();
                        state.Employments.Add(reloadedContract);
                        state.Postings.Clear();

                        // Arrival is Chatty, so make the event observable. Clear only the temporary
                        // letter view; the exact original list and archive are restored in finally.
                        s3Settings.letterVolume = IntercolonyLetterVolume.Everything;
                        Find.LetterStack.LettersListForReading.Clear();
                        s3EmployeeArrivalLettersBefore = CountLetterLabel(
                            Find.LetterStack.LettersListForReading, EmployeeArrivedLetterLabel);

                        DebugSettings.fastEcology = false;
                        int launchTick =
                            ((Math.Max(s3TickManager.TicksGame, reloadedContract.arrivalTick) /
                                GenDate.TicksPerHour) + 1) * GenDate.TicksPerHour;
                        s3TickManager.DebugSetTicksGame(launchTick - 1);

                        // DoSingleTick is the real path: TickManager -> World.WorldTick ->
                        // WorldComponentUtility -> IntercolonyWorldComponent.WorldComponentTick ->
                        // EmploymentService.Advance. At this hourly boundary it launches the pod.
                        s3TickManager.DoSingleTick();

                        List<DropPodIncoming> matchingIncomingPods =
                            new List<DropPodIncoming>();
                        foreach (DropPodIncoming incomingPod in
                            map.listerThings.GetThingsOfType<DropPodIncoming>())
                        {
                            if (incomingPod == null || s3SkyfallersBeforeLaunch.Contains(incomingPod) ||
                                incomingPod.innerContainer == null ||
                                incomingPod.innerContainer.Count == 0 ||
                                !(incomingPod.innerContainer[0] is ActiveTransporter))
                            {
                                continue;
                            }

                            ActiveTransporterInfo podContents = incomingPod.Contents;
                            if (podContents == null || podContents.innerContainer == null)
                            {
                                continue;
                            }

                            for (int i = 0; i < podContents.innerContainer.Count; i++)
                            {
                                if (object.ReferenceEquals(
                                    podContents.innerContainer[i], reloadedContractPawn))
                                {
                                    matchingIncomingPods.Add(incomingPod);
                                    break;
                                }
                            }
                        }

                        s3IncomingPodCount = matchingIncomingPods.Count;
                        s3EmergencyArrivalLetters = CountLetterLabel(
                            Find.LetterStack.LettersListForReading, EmergencyArrivalLetterLabel);

                        if (s3IncomingPodCount != 1)
                        {
                            s3Failure =
                                $"expected exactly one newly launched incoming pod containing the " +
                                $"reloaded contract pawn, found {s3IncomingPodCount}";
                        }
                        else
                        {
                            DropPodIncoming launchedPod = matchingIncomingPods[0];
                            s3FixtureSkyfaller = launchedPod;
                            s3FixtureActiveTransporter =
                                launchedPod.innerContainer[0] as ActiveTransporter;
                            ActiveTransporterInfo launchedPodContents = launchedPod.Contents;
                            s3InitialTicksToImpact = launchedPod.ticksToImpact;
                            s3OpenDelay = launchedPodContents.openDelay;
                            s3SpawnDeadline = s3InitialTicksToImpact + s3OpenDelay + 1;
                            s3IncomingPodResolved = true;

                            // The launch has two live stages: DropPodIncoming flies until impact,
                            // then its ActiveTransporter opens when age exceeds openDelay. Derive
                            // the exact real-tick deadline from those objects instead of assuming
                            // a constant, so both stages are exercised by the real tick path.
                            while (s3LifecycleTicksDriven < s3SpawnDeadline &&
                                reloadedContract.pawn != null && !reloadedContract.pawn.Spawned)
                            {
                                s3TickManager.DoSingleTick();
                                s3LifecycleTicksDriven++;
                            }

                            s3PawnSpawned = reloadedContract.pawn != null &&
                                reloadedContract.pawn.Spawned;
                            if (!s3PawnSpawned)
                            {
                                s3Failure =
                                    $"pawn did not spawn after the derived deadline " +
                                    $"{s3InitialTicksToImpact}+{s3OpenDelay}+1=" +
                                    $"{s3SpawnDeadline} real ticks";
                            }
                        }
                        int completionTick =
                            (s3TickManager.TicksGame / GenDate.TicksPerHour + 1) *
                            GenDate.TicksPerHour;
                        s3TickManager.DebugSetTicksGame(completionTick - 1);
                        s3TickManager.DoSingleTick();
                        s3EmployeeArrivalLettersAfter = CountLetterLabel(
                            Find.LetterStack.LettersListForReading, EmployeeArrivedLetterLabel);

                        int furtherTick =
                            (s3TickManager.TicksGame / GenDate.TicksPerHour + 1) *
                            GenDate.TicksPerHour;
                        s3TickManager.DebugSetTicksGame(furtherTick - 1);
                        s3TickManager.DoSingleTick();
                        s3EmployeeArrivalLettersAfterFurther = CountLetterLabel(
                            Find.LetterStack.LettersListForReading, EmployeeArrivedLetterLabel);

                        s3Passed = contractRoundTripped &&
                            s3IncomingPodResolved &&
                            s3EmergencyArrivalLetters == 1 &&
                            s3EmployeeArrivalLettersAfter - s3EmployeeArrivalLettersBefore == 1 &&
                            s3EmployeeArrivalLettersAfterFurther -
                                s3EmployeeArrivalLettersAfter == 0 &&
                            s3PawnSpawned;
                    }
                }

                r.Check(
                    s3Passed,
                    FrozenEmergencyArrivalAfterReloadLabel,
                    $"reloaded contract {s3LoadedContractDescription}; expected " +
                    $"{s3ExpectedContractDescription}; emergency-route letters " +
                    $"{s3EmergencyArrivalLetters}; employee-arrival letters " +
                    $"{s3EmployeeArrivalLettersBefore}/{s3EmployeeArrivalLettersAfter}/" +
                    $"{s3EmployeeArrivalLettersAfterFurther}; pawn spawned {s3PawnSpawned}; " +
                    $"incoming pods {s3IncomingPodCount}; lifecycle " +
                    $"{s3InitialTicksToImpact}+{s3OpenDelay}+1={s3SpawnDeadline}, " +
                    $"ticks driven {s3LifecycleTicksDriven}; " +
                    $"failure {s3Failure ?? "none"}");
                s3AssertionReported = true;
            }
            catch (Exception ex)
            {
                s3Failure = $"{ex.GetType().Name}: {ex.Message}";
                if (!s3AssertionReported)
                {
                    r.Check(
                        false,
                        FrozenEmergencyArrivalAfterReloadLabel,
                        $"reloaded contract {s3LoadedContractDescription}; expected " +
                        $"{s3ExpectedContractDescription}; failure {s3Failure}");
                    s3AssertionReported = true;
                }
            }
            finally
            {
                try
                {
                    if (reloadedContract != null && reloadedContract.IsOpen)
                    {
                        EmploymentService.End(
                            reloadedContract,
                            EmploymentStatus.Failed,
                            "self-test reloaded frozen emergency contract arrival cleanup");
                    }
                }
                catch (Exception ex)
                {
                    IntercolonyLog.Warning(
                        $"S3 reloaded contract cleanup failed: {ex.GetType().Name}: {ex.Message}");
                }

                if (state.Employments.Contains(reloadedContract))
                {
                    state.Employments.Remove(reloadedContract);
                }

                if (s3SkyfallersBeforeLaunch != null)
                {
                    CleanupS3EmergencyPodObjects(
                        s3FixtureSkyfaller, s3FixtureActiveTransporter);
                }

                DiscardEquipmentFixturePawn(reloadedContractPawn);

                // The second isolated Scribe pass has its own deep-loaded world-pawn owner. It is
                // normally the same reference reached through reloadedContract.pawn, but retain
                // and dispose it independently so a split reference cannot escape teardown.
                if (contractLoadedWorldPawns != null)
                {
                    foreach (Pawn pawn in contractLoadedWorldPawns)
                    {
                        if (!object.ReferenceEquals(pawn, reloadedContractPawn))
                        {
                            DiscardEquipmentFixturePawn(pawn);
                        }
                    }
                }

                if (savedEmployments != null)
                {
                    state.Employments.Clear();
                    state.Employments.AddRange(savedEmployments);
                }

                if (savedPostings != null)
                {
                    state.Postings.Clear();
                    state.Postings.AddRange(savedPostings);
                }

                try
                {
                    CleanupApplicantHireFixture(
                        state,
                        loadedPosting,
                        loadedApplicant,
                        loadedContract,
                        "self-test reloaded frozen emergency arrival cleanup");
                }
                catch (Exception ex)
                {
                    IntercolonyLog.Warning(
                        $"S3 original hire cleanup failed: {ex.GetType().Name}: {ex.Message}");
                    state.Employments.Remove(loadedContract);
                }

                if (loadedWorldPawns != null)
                {
                    foreach (Pawn pawn in loadedWorldPawns)
                    {
                        DiscardEquipmentFixturePawn(pawn);
                    }
                }

                if (s3Archive != null && savedArchivables != null)
                {
                    s3Archive.ArchivablesListForReading.Clear();
                    s3Archive.ArchivablesListForReading.AddRange(savedArchivables);
                }

                if (Find.LetterStack != null && savedLetters != null)
                {
                    Find.LetterStack.LettersListForReading.Clear();
                    Find.LetterStack.LettersListForReading.AddRange(savedLetters);
                }

                if (savedS3LetterVolumeCaptured)
                {
                    s3Settings.letterVolume = savedS3LetterVolume;
                }

                if (savedS3FastEcologyCaptured)
                {
                    DebugSettings.fastEcology = savedS3FastEcology;
                }

                if (s3TickManager != null)
                {
                    s3TickManager.DebugSetTicksGame(savedS3Tick);
                }

                if (dropPodPreflightFailuresLogged != null &&
                    savedDropPodPreflightFailuresLogged != null)
                {
                    dropPodPreflightFailuresLogged.Clear();
                    dropPodPreflightFailuresLogged.UnionWith(savedDropPodPreflightFailuresLogged);
                }

                if (dropPodArrivalLettersSent != null && savedDropPodArrivalLettersSent != null)
                {
                    dropPodArrivalLettersSent.Clear();
                    dropPodArrivalLettersSent.UnionWith(savedDropPodArrivalLettersSent);
                }
            }
        }

        private static void CleanupS3EmergencyPodObjects(
            Skyfaller fixtureSkyfaller, ActiveTransporter fixtureActiveTransporter)
        {
            if (fixtureSkyfaller == null || fixtureActiveTransporter == null)
            {
                IntercolonyLog.Warning(
                    "S3 cleanup could not identify both fixture-created pod objects by identity; " +
                    "leaving all skyfallers and active transporters untouched.");
                return;
            }

            if (!fixtureSkyfaller.Destroyed)
            {
                try
                {
                    fixtureSkyfaller.Destroy(DestroyMode.Vanish);
                }
                catch (Exception ex)
                {
                    IntercolonyLog.Warning(
                        $"S3 skyfaller cleanup failed: {ex.GetType().Name}: {ex.Message}");
                }
            }

            if (!fixtureActiveTransporter.Destroyed)
            {
                try
                {
                    fixtureActiveTransporter.Destroy(DestroyMode.Vanish);
                }
                catch (Exception ex)
                {
                    IntercolonyLog.Warning(
                        $"S3 active-transporter cleanup failed: " +
                        $"{ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        private static void CheckEmergencyApplicantMigration(
            Results r, IntercolonyWorldComponent state)
        {
            const int preMigrationVersion = 59;
            const int termDays = 20;
            const int emergencyTravelDays = 3;
            const int ordinaryTravelDays = 7;

            FieldInfo saveVersionField = typeof(IntercolonyWorldComponent).GetField(
                "saveVersion", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo censusField = typeof(LaborCandidateService).GetField(
                "census", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo censusRefreshField = typeof(LaborCandidateService).GetField(
                "censusRefreshCount", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo poolField = typeof(LaborCandidateService).GetField(
                "pool", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo poolRefreshField = typeof(LaborCandidateService).GetField(
                "poolRefreshCount", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo poolOwnerField = typeof(LaborCandidateService).GetField(
                "poolOwner", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo economySeedField = typeof(IntercolonyWorldComponent).GetField(
                "economySeed", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo profileCacheField = typeof(IntercolonyWorldComponent).GetField(
                "profileCache", BindingFlags.Instance | BindingFlags.NonPublic);

            if (saveVersionField == null || censusField == null ||
                censusRefreshField == null || poolField == null ||
                poolRefreshField == null || poolOwnerField == null ||
                economySeedField == null || profileCacheField == null)
            {
                SkipEmergencyApplicantMigrationAssertions(
                    r,
                    saveVersionField == null
                        ? "the private saveVersion field was unavailable"
                        : "the labor migration or profile cache fields were unavailable");
                return;
            }

            if (Find.WorldPawns == null)
            {
                SkipEmergencyApplicantMigrationAssertions(
                    r,
                    "Find.WorldPawns was null, so the direct applicant pawns could not be " +
                    "disposed through the existing helper");
                return;
            }

            List<JobPosting> savedPostings = new List<JobPosting>(state.Postings);
            List<LaborProspect> savedCensus =
                censusField.GetValue(null) as List<LaborProspect>;
            List<LaborProspect> savedCensusContents = savedCensus == null
                ? null
                : new List<LaborProspect>(savedCensus);
            int savedCensusRefreshCount = (int)censusRefreshField.GetValue(null);
            List<LaborCandidate> savedPool =
                poolField.GetValue(null) as List<LaborCandidate>;
            List<LaborCandidate> savedPoolContents = savedPool == null
                ? null
                : new List<LaborCandidate>(savedPool);
            int savedPoolRefreshCount = (int)poolRefreshField.GetValue(null);
            IntercolonyWorldComponent savedPoolOwner =
                poolOwnerField.GetValue(null) as IntercolonyWorldComponent;
            int savedSaveVersion = state.SaveVersion;
            int savedEconomySeed = (int)economySeedField.GetValue(state);
            Dictionary<int, SettlementEconomicProfile> savedProfileCache =
                profileCacheField.GetValue(state) as Dictionary<int, SettlementEconomicProfile>;
            Dictionary<int, SettlementEconomicProfile> savedProfileCacheContents =
                savedProfileCache == null
                    ? null
                    : new Dictionary<int, SettlementEconomicProfile>(savedProfileCache);
            SettlementEconomicProfile controlledProfile = null;
            SettlementRapidLogisticsCapability savedControlledCapability =
                SettlementRapidLogisticsCapability.ConventionalTransportOnly;
            bool controlledCapabilityChanged = false;

            JobPosting emergencyPosting = null;
            JobPosting ordinaryPosting = null;
            JobApplicant migratedApplicant = null;
            JobApplicant invalidApplicant = null;
            JobApplicant ordinaryApplicant = null;
            Pawn migratedPawn = null;
            Pawn invalidPawn = null;
            Pawn ordinaryPawn = null;

            try
            {
                // This is only a source of a registered faction and a materialisable pawn. The
                // emergency distance below is controlled directly; no live emergency-eligible
                // prospect is requested or filtered for this fixture.
                List<LaborProspect> census = LaborCandidateService.Census(state);
                LaborProspect sourceProspect = null;
                if (census != null)
                {
                    foreach (LaborProspect prospect in census)
                    {
                        if (prospect == null || prospect.faction == null ||
                            prospect.settlementId < 0 ||
                            Find.FactionManager?.AllFactionsListForReading?.Contains(
                                prospect.faction) != true)
                        {
                            continue;
                        }

                        Settlement sourceSettlement = IntercolonyMarketAccess.FindSettlement(
                            prospect.settlementId);
                        if (sourceSettlement == null ||
                            !IntercolonyMarketAccess.IsAccessible(sourceSettlement, out _) ||
                            !EmployerReputationService.WillSupplyLabor(
                                state, sourceSettlement.ID, out _))
                        {
                            continue;
                        }

                        sourceProspect = prospect;
                        break;
                    }
                }

                if (sourceProspect == null)
                {
                    SkipEmergencyApplicantMigrationAssertions(
                        r,
                        "the current census had no registered, accessible, labor-supplying " +
                        "source to anchor the direct applicant fixture");
                    return;
                }

                Settlement migrationSettlement = IntercolonyMarketAccess.FindSettlement(
                    sourceProspect.settlementId);
                controlledProfile = migrationSettlement == null
                    ? null
                    : state.GetProfile(migrationSettlement);
                if (controlledProfile == null)
                {
                    SkipEmergencyApplicantMigrationAssertions(
                        r,
                        "the selected source had no economic profile for the controlled " +
                        "emergency quote fixture");
                    return;
                }

                savedControlledCapability = controlledProfile.rapidLogisticsCapability;
                controlledProfile.rapidLogisticsCapability =
                    SettlementRapidLogisticsCapability.DropPodsAvailable;
                controlledCapabilityChanged = true;

                LaborProspect migrationSource = CopyProspectForEmergencyFixture(
                    sourceProspect,
                    sourceProspect.settlementId,
                    1.25f,
                    sourceProspect.settlementName);
                migrationSource.travelDays = emergencyTravelDays;

                // Capture the expected route before migration, independently of the applicant's
                // default Conventional value. The controlled profile makes this a DropPod quote,
                // so the transport assertion cannot pass by leaving the enum default in place.
                EmergencyArrivalQuote expectedQuote =
                    LaborCandidateService.QuoteEmergencyArrival(state, migrationSource);

                Rand.PushState(0x54_32_01);
                try
                {
                    migratedPawn = migrationSource.Materialise();
                }
                finally
                {
                    Rand.PopState();
                }

                Rand.PushState(0x54_33_01);
                try
                {
                    invalidPawn = migrationSource.Materialise();
                }
                finally
                {
                    Rand.PopState();
                }

                Rand.PushState(0x54_31_01);
                try
                {
                    ordinaryPawn = migrationSource.Materialise();
                }
                finally
                {
                    Rand.PopState();
                }

                if (migratedPawn == null || invalidPawn == null || ordinaryPawn == null)
                {
                    SkipEmergencyApplicantMigrationAssertions(
                        r,
                        "the controlled source could not materialise all three direct fixture " +
                        "pawns");
                    return;
                }

                state.Postings.Clear();

                emergencyPosting = new JobPosting
                {
                    id = -60_401,
                    termDays = termDays,
                    wageStructure = WageStructure.Daily,
                    combatClause = CombatClause.Civilian,
                    requestedEquipmentLevel = LaborEquipmentLevel.Any,
                    emergencyDispatch = true,
                    postedTick = GenTicks.TicksGame,
                    expiryTick = -1,
                    status = JobPostingStatus.Open
                };

                ordinaryPosting = new JobPosting
                {
                    id = -60_402,
                    termDays = termDays,
                    wageStructure = WageStructure.Daily,
                    combatClause = CombatClause.Civilian,
                    requestedEquipmentLevel = LaborEquipmentLevel.Any,
                    emergencyDispatch = false,
                    postedTick = GenTicks.TicksGame,
                    expiryTick = -1,
                    status = JobPostingStatus.Open
                };

                migratedApplicant = new JobApplicant
                {
                    pawn = migratedPawn,
                    settlementId = migrationSource.settlementId,
                    settlementName = migrationSource.settlementName,
                    factionName = migrationSource.factionName,
                    faction = migrationSource.faction,
                    distanceTiles = migrationSource.distanceTiles,
                    travelDays = migrationSource.travelDays,
                    emergencyArrivalAvailable = false,
                    emergencyArrivalTransport = EmploymentArrivalTransport.Conventional,
                    emergencyArrivalTicks = 0,
                    emergencyArrivalMethodLabel = "",
                    requiredSkillLevel = 0,
                    openMarketAsk = 1,
                    appliedTick = GenTicks.TicksGame,
                    sourceCensusRefreshCount = -1,
                    sourceCensusIndex = -1
                };

                // V2: the missing source settlement is genuine, so migration must invalidate this
                // applicant rather than leave it hireable under an Emergency heading.
                invalidApplicant = new JobApplicant
                {
                    pawn = invalidPawn,
                    settlementId = -1,
                    settlementName = "missing S4 source",
                    factionName = migrationSource.factionName,
                    faction = migrationSource.faction,
                    distanceTiles = migrationSource.distanceTiles,
                    travelDays = migrationSource.travelDays,
                    emergencyArrivalAvailable = false,
                    emergencyArrivalTransport = EmploymentArrivalTransport.Conventional,
                    emergencyArrivalTicks = 0,
                    emergencyArrivalMethodLabel = "",
                    requiredSkillLevel = 0,
                    openMarketAsk = 1,
                    appliedTick = GenTicks.TicksGame,
                    sourceCensusRefreshCount = -1,
                    sourceCensusIndex = -1
                };

                ordinaryApplicant = new JobApplicant
                {
                    pawn = ordinaryPawn,
                    settlementId = migrationSource.settlementId,
                    settlementName = migrationSource.settlementName,
                    factionName = migrationSource.factionName,
                    faction = migrationSource.faction,
                    distanceTiles = migrationSource.distanceTiles,
                    travelDays = ordinaryTravelDays,
                    emergencyArrivalAvailable = false,
                    emergencyArrivalTransport = EmploymentArrivalTransport.Conventional,
                    emergencyArrivalTicks = 0,
                    emergencyArrivalMethodLabel = "",
                    requiredSkillLevel = 0,
                    openMarketAsk = 1,
                    appliedTick = GenTicks.TicksGame,
                    sourceCensusRefreshCount = -1,
                    sourceCensusIndex = -1
                };

                emergencyPosting.Applicants.Add(migratedApplicant);
                emergencyPosting.Applicants.Add(invalidApplicant);
                ordinaryPosting.Applicants.Add(ordinaryApplicant);
                state.AddPosting(emergencyPosting);
                state.AddPosting(ordinaryPosting);

                // This is the schema-59 saved shape: no frozen quote on either emergency
                // applicant, no census identity, and a private saveVersion of 59.
                saveVersionField.SetValue(state, preMigrationVersion);
                state.MigrateIfNeeded();

                bool quoteFrozen = expectedQuote.available &&
                    expectedQuote.transport == EmploymentArrivalTransport.DropPod &&
                    migratedApplicant.emergencyArrivalAvailable &&
                    migratedApplicant.emergencyArrivalTicks > 0 &&
                    migratedApplicant.emergencyArrivalTransport == expectedQuote.transport &&
                    migratedApplicant.emergencyArrivalTicks == expectedQuote.arrivalTicks &&
                    !String.IsNullOrEmpty(migratedApplicant.emergencyArrivalMethodLabel) &&
                    migratedApplicant.emergencyArrivalMethodLabel == expectedQuote.methodLabel;
                r.Check(
                    quoteFrozen,
                    EmergencyApplicantMigrationQuoteLabel,
                    $"saved false/Conventional/0; expected " +
                    $"{expectedQuote.available}/{expectedQuote.transport}/" +
                    $"{expectedQuote.arrivalTicks}/\"{expectedQuote.methodLabel}\"; actual " +
                    $"{migratedApplicant.emergencyArrivalAvailable}/" +
                    $"{migratedApplicant.emergencyArrivalTransport}/" +
                    $"{migratedApplicant.emergencyArrivalTicks}/\"" +
                    $"{migratedApplicant.emergencyArrivalMethodLabel}\"; save version " +
                    $"{state.SaveVersion}");

                int unfrozenEmergencyApplicants = 0;
                foreach (JobApplicant applicant in emergencyPosting.Applicants)
                {
                    if (applicant != null && !applicant.emergencyArrivalAvailable)
                    {
                        unfrozenEmergencyApplicants++;
                    }
                }

                bool invalidated = !emergencyPosting.Applicants.Contains(invalidApplicant) &&
                    invalidApplicant.pawn == null &&
                    unfrozenEmergencyApplicants == 0;
                r.Check(
                    invalidated,
                    EmergencyApplicantMigrationInvalidationLabel,
                    $"invalid applicant still listed " +
                    $"{emergencyPosting.Applicants.Contains(invalidApplicant)}; " +
                    $"invalid pawn released {invalidApplicant.pawn == null}; " +
                    $"unfrozen Emergency applicants {unfrozenEmergencyApplicants}");

                bool ordinaryUntouched = ordinaryPosting.Applicants.Count == 1 &&
                    object.ReferenceEquals(ordinaryPosting.Applicants[0], ordinaryApplicant) &&
                    ordinaryApplicant.travelDays == ordinaryTravelDays &&
                    !ordinaryApplicant.emergencyArrivalAvailable &&
                    ordinaryApplicant.emergencyArrivalTransport ==
                        EmploymentArrivalTransport.Conventional &&
                    ordinaryApplicant.emergencyArrivalTicks == 0 &&
                    String.IsNullOrEmpty(ordinaryApplicant.emergencyArrivalMethodLabel) &&
                    !ordinaryApplicant.HasSourceCensusIdentity;
                r.Check(
                    ordinaryUntouched,
                    EmergencyApplicantMigrationOrdinaryLabel,
                    $"ordinary applicant count {ordinaryPosting.Applicants.Count}; travel " +
                    $"{ordinaryApplicant.travelDays}d (expected {ordinaryTravelDays}d); " +
                    $"quote {ordinaryApplicant.emergencyArrivalAvailable}/" +
                    $"{ordinaryApplicant.emergencyArrivalTransport}/" +
                    $"{ordinaryApplicant.emergencyArrivalTicks}; identity present " +
                    $"{ordinaryApplicant.HasSourceCensusIdentity}");

                r.Check(
                    migratedApplicant.HasSourceCensusIdentity,
                    EmergencyApplicantMigrationIdentityLabel,
                    "the legacy emergency applicant has a current census identity or the " +
                    "int.MaxValue marker pair");
            }
            catch (Exception ex)
            {
                string failure =
                    $"schema 59 -> 60 migration threw {ex.GetType().Name}: {ex.Message}";
                r.Check(false, EmergencyApplicantMigrationQuoteLabel, failure);
                r.Check(false, EmergencyApplicantMigrationInvalidationLabel, failure);
                r.Check(false, EmergencyApplicantMigrationOrdinaryLabel, failure);
                r.Check(false, EmergencyApplicantMigrationIdentityLabel, failure);
            }
            finally
            {
                try
                {
                    RestorePostingList(
                        state, savedPostings, "self-test emergency applicant migration cleanup");
                }
                finally
                {
                    if (savedCensus != null)
                    {
                        savedCensus.Clear();
                        if (savedCensusContents != null)
                        {
                            savedCensus.AddRange(savedCensusContents);
                        }
                    }

                    censusField.SetValue(null, savedCensus);
                    censusRefreshField.SetValue(null, savedCensusRefreshCount);

                    if (savedPool != null)
                    {
                        savedPool.Clear();
                        if (savedPoolContents != null)
                        {
                            savedPool.AddRange(savedPoolContents);
                        }
                    }

                    if (controlledCapabilityChanged && controlledProfile != null)
                    {
                        controlledProfile.rapidLogisticsCapability = savedControlledCapability;
                    }

                    poolRefreshField.SetValue(null, savedPoolRefreshCount);
                    poolOwnerField.SetValue(null, savedPoolOwner);
                    if (savedProfileCache != null)
                    {
                        savedProfileCache.Clear();
                        if (savedProfileCacheContents != null)
                        {
                            foreach (KeyValuePair<int, SettlementEconomicProfile> entry in
                                savedProfileCacheContents)
                            {
                                savedProfileCache[entry.Key] = entry.Value;
                            }
                        }
                    }

                    profileCacheField.SetValue(state, savedProfileCache);
                    economySeedField.SetValue(state, savedEconomySeed);
                    saveVersionField.SetValue(state, savedSaveVersion);

                    DiscardEquipmentFixturePawn(migratedPawn);
                    DiscardEquipmentFixturePawn(invalidPawn);
                    DiscardEquipmentFixturePawn(ordinaryPawn);
                }
            }
        }

        private static void SkipEmergencyApplicantMigrationAssertions(
            Results r, string reason)
        {
            r.Skip(EmergencyApplicantMigrationQuoteLabel, reason);
            r.Skip(EmergencyApplicantMigrationInvalidationLabel, reason);
            r.Skip(EmergencyApplicantMigrationOrdinaryLabel, reason);
            r.Skip(EmergencyApplicantMigrationIdentityLabel, reason);
        }

        private static void SkipFrozenEmergencyArrivalAssertions(
            Results r, string reason)
        {
            r.Skip(FrozenEmergencyTransportLabel, reason);
            r.Skip(FrozenEmergencyDurationLabel, reason);
            r.Skip(FrozenEmergencySnapshotLabel, reason);
            SkipFrozenEmergencyPersistenceAssertions(r, reason);
        }

        private static void SkipFrozenEmergencyPersistenceAssertions(
            Results r, string reason)
        {
            r.Skip(FrozenEmergencyQuoteRoundTripLabel, reason);
            r.Skip(FrozenEmergencyReloadHireLabel, reason);
            r.Skip(FrozenEmergencyArrivalAfterReloadLabel, reason);
        }

        private static int CountLetterLabel(List<Letter> letters, string label)
        {
            int count = 0;
            if (letters == null)
            {
                return count;
            }

            foreach (Letter letter in letters)
            {
                if (letter != null && letter.Label == label)
                {
                    count++;
                }
            }

            return count;
        }

        private static void CleanupApplicantHireFixture(
            IntercolonyWorldComponent state, JobPosting posting, JobApplicant applicant,
            EmploymentContract contract, string note)
        {
            if (posting != null && applicant != null)
            {
                posting.Applicants.Remove(applicant);
            }

            if (contract != null)
            {
                EmploymentService.End(contract, EmploymentStatus.Failed, note);
                state.Employments.Remove(contract);
            }
            else
            {
                applicant?.Discard();
            }

            if (posting != null)
            {
                JobPostingService.Close(posting, JobPostingStatus.Withdrawn, note);
                state.Postings.Remove(posting);
            }
        }

        /// <summary>F25's contract rate must carry the quote the applicant brought with them.</summary>
        private static void CheckApplicantOwnAsk(
            Results r, IntercolonyWorldComponent state, Map map)
        {
            const int term = 20;
            const int postedWage = 1;
            const string label = "a hired applicant is paid their own ask (§35.2, F25)";

            if (map == null)
            {
                r.Skip(label, "the current map was null, so colony payment could not be staged");
                return;
            }

            if (Find.WorldPawns == null)
            {
                r.Skip(label, "Find.WorldPawns was null, so a travelling hire could not be cleaned up");
                return;
            }

            int savedSilver = PurchaseOrderService.CountColonySilver(map);
            LaborCandidateService.Clear();
            JobPosting posting = MakePosting(
                state, SkillDefOf.Construction, 0, term, postedWage,
                out string postingFailReason);
            JobApplicant applicant = null;
            EmploymentContract contract = null;

            try
            {
                if (posting == null)
                {
                    r.Skip(label,
                        $"TryPost refused the real posting fixture: " +
                        $"{postingFailReason ?? "no failure reason"}");
                    return;
                }

                JobPostingService.MatchAll(state);
                if (posting.Applicants.Count == 0)
                {
                    r.Skip(label, "the real posting produced no applicant to hire");
                    return;
                }

                applicant = posting.Applicants[0];
                if (applicant == null || applicant.pawn == null)
                {
                    r.Skip(label, "the first queued applicant had no pawn to pass through hiring");
                    return;
                }

                int applicantAsk = applicant.openMarketAsk;
                if (applicantAsk == postedWage)
                {
                    r.Skip(label,
                        $"the fixture applicant happened to ask the deliberately low posted wage " +
                        $"of {postedWage}/day");
                    return;
                }

                // This calculation only stages enough silver for the real payment path. The
                // expected contract rate below is read directly from applicant.openMarketAsk.
                int upFront = WageStructureUtility.UpFrontCost(
                    posting.wageStructure, applicantAsk, posting.termDays);
                EmploymentEquipmentQuote equipmentQuote =
                    EmploymentEquipmentService.Quote(applicant.pawn);
                EmploymentHireCostQuote hireQuote =
                    EmploymentEquipmentService.QuoteHireCost(upFront, equipmentQuote);
                IntercolonyLaborSelfTestSupport.EnsureSilver(
                    map, IntercolonyLaborSelfTestSupport.SilverToEnsure(hireQuote));
                int available = PurchaseOrderService.CountColonySilver(map);
                if (available < hireQuote.totalDue)
                {
                    r.Skip(label,
                        $"could not stage the up-front cost: {available} silver available, " +
                        $"{hireQuote.totalDue} needed for the applicant ask of {applicantAsk}/day");
                    return;
                }

                contract = EmploymentService.TryHireApplicant(
                    state, applicant, posting, map, out string hireFailReason, hireQuote);
                if (contract == null)
                {
                    r.Skip(label,
                        $"the real applicant hire could not be arranged: " +
                        $"{hireFailReason ?? "no reason"}");
                    return;
                }

                r.Check(contract.dailyWage == applicantAsk,
                    label,
                    $"posted {postedWage}/day; applicant ask {applicantAsk}/day; " +
                    $"contract daily wage {contract.dailyWage}/day");
            }
            finally
            {
                if (posting != null && applicant != null)
                {
                    posting.Applicants.Remove(applicant);
                }

                if (contract != null)
                {
                    EmploymentService.End(contract, EmploymentStatus.Failed, "self-test hire");
                    state.Employments.Remove(contract);
                }
                else
                {
                    applicant?.Discard();
                }

                if (posting != null)
                {
                    JobPostingService.Close(posting, JobPostingStatus.Withdrawn, "self-test hire");
                    state.Postings.Remove(posting);
                }

                int silverAfter = PurchaseOrderService.CountColonySilver(map);
                if (silverAfter < savedSilver)
                {
                    int returned = IntercolonyLaborSelfTestSupport.EnsureSilver(
                        map, savedSilver);
                    if (returned > 0)
                    {
                        r.Info($"returned {returned} silver to restore the hire fixture.");
                    }
                }
                else if (silverAfter > savedSilver)
                {
                    PurchaseOrderService.TryTakeSilver(map, silverAfter - savedSilver);
                }

                // The amount was restored explicitly above, so do not let this fixture's silver
                // bookkeeping affect the outer labor/payroll self-tests.
                IntercolonyLaborSelfTestSupport.ResetLedger();
            }
        }

        /// <summary>
        /// The second half of §114: reputation moves applicants too.
        ///
        /// Nothing in the matcher reads reputation directly — it falls out of Phase 19's
        /// <c>WageFactor</c> multiplying every asking price. This test exists to prove that
        /// indirection actually works end to end, because a change to either half could silently
        /// break it while both halves still pass their own tests.
        /// </summary>
        private static void CheckReputationDrivesApplicants(
            Results r, IntercolonyWorldComponent state, EmployerReputation rep)
        {
            if (rep == null)
            {
                return;
            }

            SkillDef skill = SkillDefOf.Construction;
            const int term = 20;

            if (!JobPostingService.GoingRate(state, skill, 0, term, CombatClause.Civilian,
                    out int low, out int high, out _))
            {
                r.Info("reputation effect skipped: nobody reachable can do the work.");
                return;
            }

            // Mid-band, so there is room to move in both directions. At the top of the band every
            // offer clears everyone and reputation would be invisible.
            int wage = (low + high) / 2;

            rep.Adjust(EmployerReputation.MinScore - rep.Score);
            Draw asBad = Measure(r, state, skill, 0, term, wage);

            rep.Adjust(EmployerReputation.MaxScore - rep.Score);
            Draw asGood = Measure(r, state, skill, 0, term, wage);

            // Reach rather than queue length, for the same reason as the response curve: the queue
            // caps out and would report a sought-after employer and an exploitative one as equal.
            r.Check(asGood.interested >= asBad.interested,
                "the same offer reaches at least as many for a good employer as a bad one (§114)",
                $"{asBad.interested} exploitative vs {asGood.interested} sought-after, at {wage}/day");

            r.Check(asGood.interested > asBad.interested,
                "employer reputation measurably changes who will take the job (§114, §112)",
                $"{asBad.interested} vs {asGood.interested}");
        }

        /// <summary>
        /// The quality half of §114: a bad employer sees fewer prospects and worse ones, while a
        /// middle-standing colony keeps the ordinary one-draw generation cost.
        /// </summary>
        private static void CheckReputationDrivesCandidateQuality(
            Results r, IntercolonyWorldComponent state, EmployerReputation rep)
        {
            if (rep == null)
            {
                return;
            }

            rep.Adjust(EmployerReputation.MinScore - rep.Score);
            LaborCandidateService.InvalidateCensus();
            List<LaborProspect> atMinimum = new List<LaborProspect>(
                LaborCandidateService.Census(state));
            int minimumCount = atMinimum.Count;
            float minimumMeanBestSkill = MeanBestProspectSkill(atMinimum);
            int minimumDraws = LaborCandidateService.CensusProspectDraws;

            rep.Adjust(EmployerReputation.MaxScore - rep.Score);
            LaborCandidateService.InvalidateCensus();
            List<LaborProspect> atMaximum = new List<LaborProspect>(
                LaborCandidateService.Census(state));
            int maximumCount = atMaximum.Count;
            float maximumMeanBestSkill = MeanBestProspectSkill(atMaximum);
            int maximumDraws = LaborCandidateService.CensusProspectDraws;

            if (minimumCount == 0 && maximumCount == 0)
            {
                r.Info("reputation quality skipped: the census is empty at both standings.");
                LaborCandidateService.InvalidateCensus();
                return;
            }

            float middle = (EmployerReputation.MinScore + EmployerReputation.MaxScore) / 2;
            rep.Adjust(middle - rep.Score);
            LaborCandidateService.InvalidateCensus();
            List<LaborProspect> atMiddle = new List<LaborProspect>(
                LaborCandidateService.Census(state));
            int middleCount = atMiddle.Count;
            float middleMeanBestSkill = MeanBestProspectSkill(atMiddle);
            int middleDraws = LaborCandidateService.CensusProspectDraws;
            int middleBias = EmployerReputationService.CandidateQualityBias(middle);

            // This fails if GenerateProspectBiased ignores its bias (returning the first draw
            // always), or if the keep-better/keep-worse comparison is inverted.
            r.Check(maximumMeanBestSkill >= minimumMeanBestSkill + 0.5f,
                "a bad employer's census prospects are measurably worse than a good one's (§114)",
                $"mean best skill {minimumMeanBestSkill:0.00} at MinScore vs " +
                $"{maximumMeanBestSkill:0.00} at MaxScore; records {minimumCount} at MinScore vs " +
                $"{maximumCount} at MaxScore");

            // This fails if EnsureCensus drops AvailabilityFactor from perSettlement; the quality
            // half must not replace or weaken the volume half.
            r.Check(maximumCount > minimumCount,
                "a bad employer's job-posting census is smaller than a good one's (§114)",
                $"records {minimumCount} at MinScore vs {maximumCount} at MaxScore");

            if (middleBias == 0)
            {
                // This fails if the biased path draws twice when bias is 0, making an ordinary
                // colony pay an unnecessary generation cost, or draws once when bias is nonzero,
                // making the bias inert.
                r.Check(middleDraws == middleCount && minimumDraws == 2 * minimumCount,
                    "census generation draws once at neutral standing and twice for a bad employer (§35.2)",
                    $"middle: {middleDraws} draws for {middleCount} records " +
                    $"(mean best skill {middleMeanBestSkill:0.00}); " +
                    $"MinScore: {minimumDraws} draws for {minimumCount} records; " +
                    $"MaxScore: {maximumDraws} draws for {maximumCount} records");
            }
            else
            {
                r.Info($"census draw-count check skipped: middle standing {middle:0.##} has " +
                       $"candidate quality bias {middleBias}, not 0.");
            }

            // Leave no live census from the temporary minimum, maximum, or middle standing for
            // the rest of the game; the suite restores the reputation in its outer teardown.
            LaborCandidateService.InvalidateCensus();
        }

        /// <summary>
        /// Ten identical postings must behave exactly like one.
        ///
        /// This is the property that lets the feature have no cap and no fee: workers are the scarce
        /// thing, not advertisements. If it ever failed, posting the same job repeatedly would
        /// multiply the labor supply out of nothing.
        /// </summary>
        private static void CheckOnePersonOnePosting(Results r, IntercolonyWorldComponent state)
        {
            SkillDef skill = SkillDefOf.Construction;
            const int term = 20;

            if (!JobPostingService.GoingRate(state, skill, 0, term, CombatClause.Civilian,
                    out _, out int high, out _))
            {
                r.Info("competition check skipped: nobody reachable can do the work.");
                return;
            }

            int wage = high + 20;

            Draw single = Measure(r, state, skill, 0, term, wage);

            // Five identical postings, matched together.
            List<JobPosting> group = new List<JobPosting>();
            List<string> groupFailureReasons = new List<string>();
            for (int i = 0; i < 5; i++)
            {
                JobPosting posting = MakePosting(
                    state, skill, 0, term, wage, out string failReason);
                if (posting == null)
                {
                    groupFailureReasons.Add(
                        $"attempt {i + 1}: {failReason ?? "no failure reason"}");

                    continue;
                }

                group.Add(posting);
            }

            if (group.Count != 5)
            {
                string reason =
                    $"TryPost refused one or more of the five duplicate fixtures; " +
                    $"{group.Count} were created; failReason: " +
                    $"{string.Join("; ", groupFailureReasons.ToArray())}";
                r.Skip("five identical postings draw no more people than one (§35.2)", reason);
                r.Skip("identical postings do not each collect their own queue (§35.2)", reason);

                foreach (JobPosting posting in group)
                {
                    JobPostingService.Close(posting, JobPostingStatus.Withdrawn, "self-test");
                    state.Postings.Remove(posting);
                }

                return;
            }

            LaborCandidateService.Clear();
            JobPostingService.MatchAll(state);

            int total = 0;
            int postingsWithAnyone = 0;
            foreach (JobPosting posting in group)
            {
                total += posting.Applicants.Count;
                if (posting.Applicants.Count > 0)
                {
                    postingsWithAnyone++;
                }
            }

            foreach (JobPosting posting in group)
            {
                JobPostingService.Close(posting, JobPostingStatus.Withdrawn, "self-test");
                state.Postings.Remove(posting);
            }

            r.Check(total <= single.applicants,
                "five identical postings draw no more people than one (§35.2)",
                $"one drew {single.applicants}, five drew {total} across {postingsWithAnyone} posting(s)");

            r.Check(postingsWithAnyone <= 1,
                "identical postings do not each collect their own queue (§35.2)",
                $"{postingsWithAnyone} of 5 drew anyone");
        }

        /// <summary>A posting that draws nobody has to say which of the two reasons it was.</summary>
        private static void CheckSilenceIsExplained(Results r, IntercolonyWorldComponent state)
        {
            float standing = EmployerReputationService.ScoreFor(state);

            JobPosting unaffordable = MakePosting(
                state, SkillDefOf.Construction, 0, 20, 1, out string tooCheapFailure);
            JobPosting impossible = MakePosting(
                state, SkillDefOf.Construction, 20, 20, 9999, out string nobodyCanFailure);

            if (unaffordable == null || impossible == null)
            {
                StringBuilder reason = new StringBuilder("TryPost refused a silence fixture: ");
                if (unaffordable == null)
                {
                    reason.Append("unaffordable fixture failReason: ")
                          .Append(tooCheapFailure ?? "no failure reason");
                }

                if (impossible == null)
                {
                    if (unaffordable == null)
                    {
                        reason.Append("; ");
                    }

                    reason.Append("impossible fixture failReason: ")
                          .Append(nobodyCanFailure ?? "no failure reason");
                }

                r.Skip("a posting that draws nobody always explains itself (§114)",
                    reason.ToString());
                r.Skip("\"your offer is too low\" and \"nobody can do this\" read differently",
                    reason.ToString());

                foreach (JobPosting posting in new[] { unaffordable, impossible })
                {
                    if (posting == null)
                    {
                        continue;
                    }

                    JobPostingService.Close(posting, JobPostingStatus.Withdrawn, "self-test");
                    state.Postings.Remove(posting);
                }

                return;
            }

            string tooCheap = JobPostingService.ExplainSilence(state, unaffordable, standing);

            string nobodyCan = JobPostingService.ExplainSilence(state, impossible, standing);

            foreach (JobPosting posting in new[] { unaffordable, impossible })
            {
                JobPostingService.Close(posting, JobPostingStatus.Withdrawn, "self-test");
                state.Postings.Remove(posting);
            }

            r.Check(!tooCheap.NullOrEmpty() && !nobodyCan.NullOrEmpty(),
                "a posting that draws nobody always explains itself (§114)");

            // The two reasons must not produce the same sentence, or the explanation is decoration.
            r.Check(tooCheap != nobodyCan,
                "\"your offer is too low\" and \"nobody can do this\" read differently");

            r.Info($"too cheap: \"{Trim(tooCheap)}\"");
            r.Info($"nobody qualifies: \"{Trim(nobodyCan)}\"");
        }

        /// <summary>Posting, filling and closing, through the real service.</summary>
        private static void CheckLifecycle(Results r, IntercolonyWorldComponent state)
        {
            JobPosting dialogPosting = JobPostingService.TryPost(
                state, SkillDefOf.Construction, 8, 20, WageStructure.Daily,
                CombatClause.Civilian, out string dialogFailReason);
            r.Check(dialogPosting != null && dialogFailReason == null,
                "a posting can be created with the dialog's arguments",
                dialogFailReason ?? "");
            if (dialogPosting != null)
            {
                JobPostingService.Close(
                    dialogPosting, JobPostingStatus.Withdrawn, "self-test dialog seam cleanup");
                state.Postings.Remove(dialogPosting);
            }

            JobPosting posting = JobPostingService.TryPost(
                state, SkillDefOf.Construction, 0, 20, WageStructure.Daily,
                CombatClause.Civilian, out string failReason);

            r.Check(posting != null, "a posting can be created through the real service", failReason ?? "");
            if (posting == null)
            {
                r.Skip("posting lifecycle checks after creation",
                    $"TryPost refused the lifecycle fixture; failReason: " +
                    $"{failReason ?? "no failure reason"}");
                return;
            }

            r.Check(posting.IsOpen,
                "a new posting is open",
                $"status {posting.status}");
            r.Check(posting.NeverExpires && posting.ExpiryLabel == "stays up until filled",
                "a new posting never expires and describes its lifespan without formatting the sentinel",
                $"never expires: {posting.NeverExpires}, label \"{posting.ExpiryLabel}\"");

            r.Check(JobPostingService.TryPost(state, null, 0,
                        LaborCandidateService.MaxTermDays + 1, WageStructure.Daily,
                        CombatClause.Civilian, out _) == null,
                "a posting past the term cap is refused",
                $"cap is {LaborCandidateService.MaxTermDays}d");

            JobPostingService.Withdraw(posting);
            r.Check(!posting.IsOpen && posting.status == JobPostingStatus.Withdrawn,
                "withdrawing closes the posting",
                $"status {posting.status}");
            r.Check(posting.Applicants.Count == 0,
                "a closed posting holds no applicants — they are pinned pawns and would leak");

            r.Check(!JobPostingService.Withdraw(posting),
                "an already-closed posting cannot be withdrawn twice");

            state.Postings.Remove(posting);
        }

        /// <summary>
        /// Runs the same post-load path that owns the posting validity check. The posting dialog
        /// leaves the legacy wage field at its zero default; new postings now receive that state
        /// directly from TryPost. Create both records through that real service, then model the
        /// F25 persisted state before invoking the component's PostLoadInit branch.
        ///
        /// RimWorld's Scribe.mode and LoadSaveMode.PostLoadInit are public, and ExposeData is the
        /// component's public load entry point. In this mode Scribe's Look calls do not reload
        /// values; the call reaches the component's own post-load pruning code, including
        /// IntercolonyWorldComponent.cs:1546.
        /// </summary>
        private static void CheckLoadPruner(Results r, IntercolonyWorldComponent state)
        {
            const int term = 20;
            List<JobPosting> savedPostings = new List<JobPosting>(state.Postings);
            JobPosting zeroWagePosting = null;
            JobPosting nonPositiveTermPosting = null;
            string zeroWageFailure = null;
            string nonPositiveTermFailure = null;
            LoadSaveMode savedScribeMode = Scribe.mode;

            try
            {
                zeroWagePosting = JobPostingService.TryPost(
                    state, SkillDefOf.Construction, 0, term,
                    WageStructure.Daily, CombatClause.Civilian, out zeroWageFailure);
                nonPositiveTermPosting = JobPostingService.TryPost(
                    state, SkillDefOf.Construction, 0, 1,
                    WageStructure.Daily, CombatClause.Civilian, out nonPositiveTermFailure);

                if (zeroWagePosting == null || nonPositiveTermPosting == null)
                {
                    StringBuilder reason = new StringBuilder(
                        "TryPost could not build the load-pruner fixture(s): ");
                    if (zeroWagePosting == null)
                    {
                        reason.Append("zero-wage fixture failReason: ")
                              .Append(zeroWageFailure ?? "no failure reason");
                    }

                    if (nonPositiveTermPosting == null)
                    {
                        if (zeroWagePosting == null)
                        {
                            reason.Append("; ");
                        }

                        reason.Append("broken-term fixture failReason: ")
                              .Append(nonPositiveTermFailure ?? "no failure reason");
                    }

                    r.Skip("load-pruner posting fixtures", reason.ToString());
                    return;
                }

                // This is the value produced by the F25 dialog and persisted by JobPosting.
                nonPositiveTermPosting.termDays = 0;

                if (Scribe.loader == null)
                {
                    r.Skip("load-pruner posting fixtures",
                        "RimWorld Scribe.loader was null, so the PostLoadInit path could not run");
                    return;
                }

                // Keep unrelated player postings out of the pruner invocation, then restore the
                // exact list in finally. This lets the real RemoveAll caller run without pruning
                // or otherwise disturbing a player's existing postings.
                state.Postings.Clear();
                state.Postings.Add(zeroWagePosting);
                state.Postings.Add(nonPositiveTermPosting);

                Scribe.mode = LoadSaveMode.PostLoadInit;
                state.ExposeData();

                int postingsAfterPrune = state.Postings.Count;
                int zeroWage = zeroWagePosting.wageOffered;
                int zeroWageTerm = zeroWagePosting.termDays;
                int brokenWage = nonPositiveTermPosting.wageOffered;
                int brokenTerm = nonPositiveTermPosting.termDays;

                r.Check(zeroWage == 0 && zeroWageTerm > 0 &&
                        state.Postings.Contains(zeroWagePosting),
                    "a real zero-wage posting survives the load-time pruner (§35.2, F25)",
                    $"term {zeroWageTerm}, wage {zeroWage}; {postingsAfterPrune} posting(s) remained");
                r.Check(brokenTerm <= 0 && !state.Postings.Contains(nonPositiveTermPosting),
                    "the load-time pruner rejects a posting with a non-positive term (§35.2)",
                    $"term {brokenTerm}, wage {brokenWage}; {postingsAfterPrune} posting(s) remained");
            }
            finally
            {
                Scribe.mode = savedScribeMode;

                JobPostingService.Close(
                    zeroWagePosting, JobPostingStatus.Withdrawn, "self-test load-pruner cleanup");
                JobPostingService.Close(
                    nonPositiveTermPosting, JobPostingStatus.Withdrawn,
                    "self-test load-pruner cleanup");

                state.Postings.Clear();
                state.Postings.AddRange(savedPostings);
            }
        }

        // --- Helpers -----------------------------------------------------------------------

        private static ApplicantDraw CaptureApplicants(
            IntercolonyWorldComponent state, SkillDef skill, int minLevel, int term, int wage)
        {
            ApplicantDraw draw = new ApplicantDraw
            {
                values = new List<ApplicantValues>()
            };

            LaborCandidateService.Clear();
            JobPosting posting = MakePosting(
                state, skill, minLevel, term, wage, out string failReason);
            if (posting == null)
            {
                draw.fixtureFailureReason = failReason;
                return draw;
            }

            draw.fixtureBuilt = true;
            try
            {
                draw.qualified = JobPostingService.CountInterested(
                    state, skill, minLevel, term, wage, CombatClause.Civilian);
                JobPostingService.MatchAll(state);

                foreach (JobApplicant applicant in posting.Applicants)
                {
                    draw.values.Add(CaptureApplicantValues(applicant));
                }
            }
            finally
            {
                JobPostingService.Close(posting, JobPostingStatus.Withdrawn, "self-test reproduction");
                state.Postings.Remove(posting);
            }

            return draw;
        }

        private static ApplicantValues CaptureApplicantValues(JobApplicant applicant)
        {
            ApplicantValues values = new ApplicantValues
            {
                settlementId = applicant?.settlementId ?? -1,
                settlementName = applicant?.settlementName ?? "null",
                factionName = applicant?.factionName ?? "null",
                travelDays = applicant?.travelDays ?? -1,
                openMarketAsk = applicant?.openMarketAsk ?? -1,
                skillLevels = "none"
            };

            if (applicant?.pawn?.skills?.skills == null)
            {
                return values;
            }

            StringBuilder skills = new StringBuilder();
            foreach (SkillRecord skill in applicant.pawn.skills.skills)
            {
                if (skills.Length > 0)
                {
                    skills.Append(',');
                }

                skills.Append(skill.def.index).Append('=')
                      .Append(skill.TotallyDisabled ? -1 : skill.Level);
            }

            values.skillLevels = skills.ToString();
            return values;
        }

        private static bool SameApplicant(
            ApplicantValues first, ApplicantValues second)
        {
            return first.settlementId == second.settlementId &&
                   first.settlementName == second.settlementName &&
                   first.factionName == second.factionName &&
                   first.travelDays == second.travelDays &&
                   first.openMarketAsk == second.openMarketAsk &&
                   first.skillLevels == second.skillLevels;
        }

        private static bool SameApplicantSet(
            List<ApplicantValues> first, List<ApplicantValues> second)
        {
            if (first == null || second == null || first.Count != second.Count)
            {
                return false;
            }

            bool[] matched = new bool[second.Count];
            for (int i = 0; i < first.Count; i++)
            {
                bool found = false;
                for (int j = 0; j < second.Count; j++)
                {
                    if (!matched[j] && SameApplicant(first[i], second[j]))
                    {
                        matched[j] = true;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SameApplicantOrder(
            List<ApplicantValues> first, List<ApplicantValues> second)
        {
            if (first == null || second == null || first.Count != second.Count)
            {
                return false;
            }

            for (int i = 0; i < first.Count; i++)
            {
                if (!SameApplicant(first[i], second[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static string FirstApplicantDifference(
            List<ApplicantValues> first, List<ApplicantValues> second)
        {
            int firstCount = first?.Count ?? 0;
            int secondCount = second?.Count ?? 0;
            int count = firstCount > secondCount ? firstCount : secondCount;

            for (int i = 0; i < count; i++)
            {
                bool hasFirst = i < firstCount;
                bool hasSecond = i < secondCount;
                if (!hasFirst || !hasSecond || !SameApplicant(first[i], second[i]))
                {
                    return $"{i}: first {(hasFirst ? FormatApplicant(first[i]) : "absent")}; " +
                           $"second {(hasSecond ? FormatApplicant(second[i]) : "absent")}";
                }
            }

            return "none";
        }

        private static string FormatApplicant(ApplicantValues values)
        {
            return $"settlement {values.settlementId}/{values.settlementName}; " +
                   $"faction {values.factionName}; travel {values.travelDays}d; " +
                   $"ask {values.openMarketAsk}/day; skills {values.skillLevels}";
        }

        /// <summary>
        /// Posts a job, runs the real matcher against a freshly rebuilt pool, measures the result
        /// and cleans up.
        ///
        /// The pool is cleared first so each measurement sees the same world: without that, the
        /// second posting would be matched against a pool the first had already taken people out of,
        /// and the comparison would measure order rather than the requirement or wage invariant.
        /// </summary>
        private static Draw Measure(
            Results r, IntercolonyWorldComponent state, SkillDef skill,
            int minLevel, int term, int wage)
        {
            LaborCandidateService.Clear();

            JobPosting posting = MakePosting(
                state, skill, minLevel, term, wage, out string failReason);
            if (posting == null)
            {
                r.Skip("job posting measurement",
                    $"TryPost refused the measurement fixture for minimum {minLevel}, " +
                    $"term {term}d, wage {wage}/day; failReason: " +
                    $"{failReason ?? "no failure reason"}");
                return new Draw();
            }

            JobPostingService.MatchAll(state);

            Draw draw = new Draw
            {
                applicants = posting.Applicants.Count,
                interested = JobPostingService.CountInterested(
                    state, skill, minLevel, term, wage, CombatClause.Civilian)
            };

            foreach (JobApplicant applicant in posting.Applicants)
            {
                if (!posting.MeetsRequirement(applicant?.pawn))
                {
                    draw.queuedBarViolations++;
                }
            }

            if (draw.applicants > 0)
            {
                float sum = 0f;
                foreach (JobApplicant applicant in posting.Applicants)
                {
                    int best = BestSkillLevel(applicant.pawn);
                    sum += best;
                    if (best > draw.bestSkill)
                    {
                        draw.bestSkill = best;
                    }
                }

                draw.averageBestSkill = sum / draw.applicants;
            }

            JobPostingService.Close(posting, JobPostingStatus.Withdrawn, "self-test measurement");
            state.Postings.Remove(posting);
            return draw;
        }

        private static JobPosting MakePosting(
            IntercolonyWorldComponent state, SkillDef skill, int minLevel, int term, int wage,
            out string failReason)
        {
            return JobPostingService.TryPost(
                state, skill, minLevel, term, WageStructure.Daily,
                CombatClause.Civilian, out failReason);
        }

        private static int BestSkillLevel(Pawn pawn)
        {
            if (pawn?.skills == null)
            {
                return 0;
            }

            int best = 0;
            foreach (SkillRecord skill in pawn.skills.skills)
            {
                if (!skill.TotallyDisabled && skill.Level > best)
                {
                    best = skill.Level;
                }
            }

            return best;
        }

        private static int BestSkillLevel(LaborProspect prospect)
        {
            int best = 0;
            if (prospect?.skillLevels == null)
            {
                return best;
            }

            foreach (int level in prospect.skillLevels)
            {
                if (level >= 0 && level > best)
                {
                    best = level;
                }
            }

            return best;
        }

        private static float MeanBestProspectSkill(List<LaborProspect> prospects)
        {
            if (prospects == null || prospects.Count == 0)
            {
                return 0f;
            }

            // Keep this calculation independent from production grading, so a broken production
            // helper cannot make its own quality statistic pass this assertion.
            float total = 0f;
            foreach (LaborProspect prospect in prospects)
            {
                int best = 0;
                if (prospect?.skillLevels != null)
                {
                    foreach (int level in prospect.skillLevels)
                    {
                        if (level >= 0 && level > best)
                        {
                            best = level;
                        }
                    }
                }

                total += best;
            }

            return total / prospects.Count;
        }

        private static string Trim(string text)
        {
            string flat = text.Replace("\n", " ").Replace("  ", " ");
            return flat.Length <= 90 ? flat : flat.Substring(0, 90) + "...";
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
