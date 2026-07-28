using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Generates the pool of hireable workers (DESIGN.md §35.1).
    ///
    /// The pool is session state, not save state — see <see cref="LaborCandidate"/> for why.
    /// It is held here rather than on the world component so that nothing tries to scribe it.
    /// </summary>
    public static class LaborCandidateService
    {
        /// <summary>Base silver per day before skills, distance and term length.</summary>
        private const float BaseDailyWage = 8f;

        /// <summary>Silver per day per level, summed over the worker's best <see cref="SkillsPriced"/> skills.</summary>
        private const float SilverPerSkillLevel = 1f;

        private const int SkillsPriced = 3;

        /// <summary>Workers offered per settlement, when that settlement is drawn.</summary>
        private const int CandidatesPerSettlement = 2;

        /// <summary>
        /// Ceiling on the whole listing. Generating a pawn is not cheap, and an unbounded pool
        /// grows with the number of settlements — on a stock map that was 48 pawns built and
        /// thrown away on every look. §5.2's opportunity flood was the same shape of mistake.
        /// </summary>
        private const int MaxCandidates = 20;

        private static readonly List<LaborCandidate> pool = new List<LaborCandidate>();

        /// <summary>Which market refresh the current pool belongs to; -1 when there is no pool.</summary>
        private static int poolRefreshCount = -1;

        public static IReadOnlyList<LaborCandidate> Pool => pool;

        /// <summary>
        /// The current listing, rebuilt only when the market has moved on. Looking at the pool
        /// must not regenerate it: the workers on offer should not reshuffle because the player
        /// closed and reopened a window, and rebuilding discards pawns to build new ones.
        /// </summary>
        public static List<LaborCandidate> Refresh(IntercolonyWorldComponent state, bool force = false)
        {
            if (state == null || Find.WorldObjects == null)
            {
                return pool;
            }

            if (!force && poolRefreshCount == state.RefreshCount && pool.Count > 0)
            {
                return pool;
            }

            Clear();
            poolRefreshCount = state.RefreshCount;

            List<Settlement> sources = new List<Settlement>();
            foreach (Settlement settlement in Find.WorldObjects.Settlements)
            {
                if (settlement.Faction == null || settlement.Faction.IsPlayer)
                {
                    continue;
                }

                if (IntercolonyMarketAccess.IsAccessible(settlement))
                {
                    sources.Add(settlement);
                }
            }

            // Seeded shuffle, so which settlements are hiring this cycle varies but is stable
            // for the cycle, and so the draw does not perturb the global RNG stream (§60).
            Rand.PushState(Gen.HashCombineInt(state.EconomySeed, state.RefreshCount) ^ 0x4C41_424F);
            try
            {
                for (int i = sources.Count - 1; i > 0; i--)
                {
                    int j = Rand.RangeInclusive(0, i);
                    Settlement swap = sources[i];
                    sources[i] = sources[j];
                    sources[j] = swap;
                }

                foreach (Settlement settlement in sources)
                {
                    if (pool.Count >= MaxCandidates)
                    {
                        break;
                    }

                    SettlementEconomicProfile profile = state.GetProfile(settlement);
                    if (profile == null)
                    {
                        continue;
                    }

                    for (int i = 0; i < CandidatesPerSettlement && pool.Count < MaxCandidates; i++)
                    {
                        LaborCandidate candidate = Generate(settlement, profile);
                        if (candidate != null)
                        {
                            pool.Add(candidate);
                        }
                    }
                }
            }
            finally
            {
                Rand.PopState();
            }

            pool.Sort((a, b) => a.dailyWage.CompareTo(b.dailyWage));
            return pool;
        }

        /// <summary>Discards every unhired candidate and empties the pool.</summary>
        public static void Clear()
        {
            foreach (LaborCandidate candidate in pool)
            {
                candidate.Discard();
            }

            pool.Clear();
            poolRefreshCount = -1;
        }

        /// <summary>Removes a candidate from the pool without discarding its pawn (it has been hired).</summary>
        public static void Take(LaborCandidate candidate)
        {
            pool.Remove(candidate);
        }

        private static LaborCandidate Generate(Settlement settlement, SettlementEconomicProfile profile)
        {
            Faction faction = settlement.Faction;

            // Faction.RandomPawnKind, not def.basicMemberKind: only *player* faction defs
            // define basicMemberKind, so keying off it excludes every possible employer.
            // See docs/LABOR_TECHNICAL_NOTES.md.
            PawnKindDef kind = faction.RandomPawnKind();
            if (kind == null || kind.RaceProps == null || !kind.RaceProps.Humanlike)
            {
                return null;
            }

            Pawn pawn;
            try
            {
                pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, faction,
                    PawnGenerationContext.NonPlayer,
                    forceGenerateNewPawn: true,
                    canGeneratePawnRelations: false,
                    mustBeCapableOfViolence: false,
                    allowFood: true));
            }
            catch (System.Exception ex)
            {
                IntercolonyLog.Warning($"Could not generate a worker for {settlement.Label}: {ex.Message}");
                return null;
            }

            // A faction leader must never be hireable: SetFaction fires Notify_LeaderLost on
            // their faction (docs/LABOR_TECHNICAL_NOTES.md).
            if (pawn.Faction != null && pawn.Faction.leader == pawn)
            {
                Find.WorldPawns?.RemoveAndDiscardPawnViaGC(pawn);
                return null;
            }

            float distance = MarketOpportunityGenerator.DistanceToPlayer(settlement);
            int minTerm = MinimumTermDays(profile);

            return new LaborCandidate
            {
                pawn = pawn,
                settlementId = settlement.ID,
                settlementName = settlement.Label ?? "unnamed",
                factionName = faction.Name ?? "",
                faction = faction,
                distanceTiles = distance,
                travelDays = TravelDays(distance),
                minTermDays = minTerm,
                dailyWage = DailyWage(pawn, profile, distance, minTerm)
            };
        }

        /// <summary>Days a hired worker spends travelling to the colony.</summary>
        public static int TravelDays(float distance)
        {
            // Same rate the procurement lead time uses (RfqService.LeadTimeDays), so a worker
            // and a crate from the same settlement take comparable time to arrive.
            return distance < 0f ? 3 : Mathf.Clamp(Mathf.RoundToInt(distance / 12f), 1, 20);
        }

        /// <summary>
        /// Silver per day. Priced off the worker's best skills, then adjusted for how far they
        /// must travel, how much spare labor the settlement has, and how short the term is.
        /// </summary>
        public static int DailyWage(Pawn pawn, SettlementEconomicProfile profile, float distance, int termDays)
        {
            float skillValue = 0f;
            if (pawn?.skills != null)
            {
                List<SkillRecord> ranked = new List<SkillRecord>(pawn.skills.skills);
                ranked.RemoveAll(s => s.TotallyDisabled);
                ranked.Sort((a, b) => b.Level.CompareTo(a.Level));

                for (int i = 0; i < SkillsPriced && i < ranked.Count; i++)
                {
                    float level = ranked[i].Level;

                    // Passion is worth paying for: it is the difference between a skill that
                    // stays where it is and one that grows over a long contract.
                    if (ranked[i].passion == Passion.Major)
                    {
                        level *= 1.15f;
                    }
                    else if (ranked[i].passion == Passion.Minor)
                    {
                        level *= 1.07f;
                    }

                    skillValue += level;
                }
            }

            float wage = BaseDailyWage + skillValue * SilverPerSkillLevel;

            // Travel is unpaid time the worker still has to live through, so distant labor
            // costs more (§35.2 lists distance as a factor in the hiring market).
            if (distance > 0f)
            {
                wage *= 1f + Mathf.Min(distance, 200f) / 200f * 0.25f;
            }

            // A settlement with spare labor undercuts one that does not.
            wage *= Mathf.Lerp(1.25f, 0.8f, Mathf.InverseLerp(0.5f, 1.5f, profile?.laborSupplyModifier ?? 1f));

            // §36.1: short contracts are "relatively expensive per day".
            wage *= ShortTermPremium(termDays);

            return Mathf.Max(1, Mathf.RoundToInt(wage));
        }

        public static float ShortTermPremium(int termDays)
        {
            if (termDays <= 5)
            {
                return 1.25f;
            }

            if (termDays <= 15)
            {
                return 1.1f;
            }

            return 1f;
        }

        private static int MinimumTermDays(SettlementEconomicProfile profile)
        {
            // §35.1 shows minimums ranging from a few days to a full year. Phase 16 only
            // supports fixed-term day contracts, so the range stays short; longer commitments
            // arrive with payroll in Phase 18 (§111).
            switch (profile?.wealthTier ?? IntercolonyWealthTier.Modest)
            {
                case IntercolonyWealthTier.Destitute:
                    return Rand.RangeInclusive(2, 5);
                case IntercolonyWealthTier.Wealthy:
                    return Rand.RangeInclusive(10, 20);
                default:
                    return Rand.RangeInclusive(5, 12);
            }
        }
    }
}
