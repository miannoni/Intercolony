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

        /// <summary>
        /// What a labor cost of 100% means, relative to the rate this mod shipped with. The
        /// original figures made hiring cheap enough that it was never weighed against doing
        /// the work yourself, and doubling them did not fix it.
        /// </summary>
        public const float LaborBaselineMultiplier = 3f;

        /// <summary>Silver per day per level, summed over the worker's best <see cref="SkillsPriced"/> skills.</summary>
        private const float SilverPerSkillLevel = 1f;

        private const int SkillsPriced = 3;

        /// <summary>Workers offered per settlement, when that settlement is drawn.</summary>
        private const int CandidatesPerSettlement = 2;

        /// <summary>
        /// Longest term a worker will sign for (§36.2). Lives here rather than in the hiring window
        /// because it is a labor rule with an economic consequence, not a widget bound: §42's clause
        /// pricing only deters the meat-shield strategy *below a term length that depends on this
        /// number*, so anything that changes it changes the balance. See
        /// <see cref="IntercolonyCombatClauseSelfTest"/>, which asserts the margin at exactly this
        /// cap and will fail loudly if it is raised past the crossover — which Phase 22 (§115,
        /// long-term and open-ended contracts) is going to want to do.
        /// </summary>
        public const int MaxTermDays = 60;

        /// <summary>
        /// Ceiling on the whole listing. Generating a pawn is not cheap, and an unbounded pool
        /// grows with the number of settlements — on a stock map that was 48 pawns built and
        /// thrown away on every look. §5.2's opportunity flood was the same shape of mistake.
        /// </summary>
        private const int MaxCandidates = 20;

        /// <summary>
        /// Workers per settlement in the census — the deep population a job posting is answered by
        /// (§35.2).
        ///
        /// **This is thirty times the advertised listing on purpose.** A market only behaves like
        /// one when it is deep: with a few dozen workers in the world, moving a posted wage by a
        /// single silver takes it from nobody interested to everybody interested, because there were
        /// three qualified people and they all charged about the same. Hundreds of workers give an
        /// offer a shape, so raising it buys a few more replies rather than flipping a switch.
        ///
        /// Affordable only because a census record is not a pawn — see <see cref="LaborProspect"/>.
        /// </summary>
        private const int ProspectsPerSettlement = 30;

        /// <summary>Hard ceiling on the census, so a huge world map cannot make a refresh expensive.</summary>
        private const int MaxCensus = 900;

        private static readonly List<LaborCandidate> pool = new List<LaborCandidate>();

        /// <summary>
        /// Everyone in the world who would consider working here, as lightweight records.
        ///
        /// **Built lazily**, only when something actually asks — a player who never posts a job pays
        /// nothing for it.
        /// </summary>
        private static List<LaborProspect> census = new List<LaborProspect>();

        /// <summary>
        /// Test-visible instrumentation: one int counting synthetic prospect draws, so the
        /// ordinary colony's no-extra-cost path is checkable rather than asserted in prose.
        /// </summary>
        internal static int CensusProspectDraws { get; private set; }

        /// <summary>Which refresh <see cref="census"/> belongs to; -1 when it has not been built.</summary>
        private static int censusRefreshCount = -1;

        /// <summary>Which market refresh the current pool belongs to; -1 when there is no pool.</summary>
        private static int poolRefreshCount = -1;

        /// <summary>
        /// Which *game* the pool belongs to. Null when there is no pool.
        ///
        /// This exists because <see cref="pool"/> is static and therefore lives as long as the
        /// process, while everything inside it — pawns, `Faction` objects, thing IDs — belongs to
        /// one game. Quitting to the menu and starting or loading another leaves the pool intact
        /// and pointing at a world that no longer exists, and <see cref="poolRefreshCount"/> alone
        /// will not notice: a fresh game starts at refresh 0, which is exactly what the previous
        /// game's pool was last keyed to.
        ///
        /// A world component is created per game, on both new-game and load, so its identity is
        /// the cheapest correct answer to "is this still the same game".
        /// </summary>
        private static IntercolonyWorldComponent poolOwner;

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

            // A pool built in another game is worse than no pool: every candidate carries a `Pawn`
            // and a `Faction` from a world that has been thrown away. Hiring one writes a dead
            // faction into a live contract and puts a pawn with another world's thing IDs onto this
            // map — which is exactly what happened, and it surfaced as a wall of "Faction X has null
            // relation with Y" plus duplicate-thingID errors on the next load. Checked before the
            // refresh-count test, because that test would happily accept the stale pool.
            if (!ReferenceEquals(poolOwner, state))
            {
                Abandon();
                poolOwner = state;
            }

            // Keyed on the refresh count alone, deliberately not on "the pool is non-empty".
            // Two reasons: GUI code runs at least twice per frame, so a pool emptied by hiring
            // would otherwise regenerate 20 pawns every frame; and a player who could drain the
            // listing and have it instantly repopulate could re-roll until a great worker
            // appeared. Who is hiring changes when the market does, and not before.
            if (!force && poolRefreshCount == state.RefreshCount)
            {
                return pool;
            }

            // Only the listing — the census is keyed to the same refresh and rebuilding it here
            // would throw away work that is about to be redone identically.
            ClearPool();
            poolRefreshCount = state.RefreshCount;

            // §39 step 9, the general half: a bad employer sees fewer workers on offer at all.
            float standing = EmployerReputationService.ScoreFor(state);
            int ceiling = Mathf.Clamp(
                Mathf.RoundToInt(MaxCandidates * EmployerReputationService.AvailabilityFactor(standing)),
                2, MaxCandidates);
            int qualityBias = EmployerReputationService.CandidateQualityBias(standing);

            List<Settlement> sources = EligibleSources(state);

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
                    if (pool.Count >= ceiling)
                    {
                        break;
                    }

                    SettlementEconomicProfile profile = state.GetProfile(settlement);
                    if (profile == null)
                    {
                        continue;
                    }

                    for (int i = 0; i < CandidatesPerSettlement && pool.Count < ceiling; i++)
                    {
                        LaborCandidate candidate = GenerateBiased(settlement, profile, standing, qualityBias);
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

        /// <summary>
        /// Settlements willing to supply labor to this colony at all. Shared by the advertised
        /// listing and the latent pool so the two can never disagree about who is dealing with the
        /// player — a settlement owed wages must not quietly reappear as an applicant.
        /// </summary>
        private static List<Settlement> EligibleSources(IntercolonyWorldComponent state)
        {
            List<Settlement> sources = new List<Settlement>();
            if (Find.WorldObjects == null)
            {
                return sources;
            }

            foreach (Settlement settlement in Find.WorldObjects.Settlements)
            {
                if (settlement.Faction == null || settlement.Faction.IsPlayer)
                {
                    continue;
                }

                if (!IntercolonyMarketAccess.IsAccessible(settlement))
                {
                    continue;
                }

                // §39 step 9, the specific half: a settlement still owed wages does not send
                // another worker. The grievance outranks the general reputation, which is why it
                // is checked per settlement rather than folded into the score.
                if (!EmployerReputationService.WillSupplyLabor(state, settlement.ID, out _))
                {
                    continue;
                }

                sources.Add(settlement);
            }

            return sources;
        }

        /// <summary>
        /// Everyone a job posting can reach this cycle (§35.2, §114).
        ///
        /// **One census, exposed to every posting.** That is what makes posting ten identical jobs
        /// no different from posting one — the world has the workers it has, and they are the scarce
        /// thing rather than the advertisements. It is also why nothing caps or charges for
        /// advertising: the market limits itself.
        ///
        /// Built on first call in a cycle and reused for the rest of it, so every posting answered
        /// in the same refresh sees exactly the same people.
        /// </summary>
        public static List<LaborProspect> Census(IntercolonyWorldComponent state)
        {
            if (state == null || Find.WorldObjects == null)
            {
                return census;
            }

            if (!ReferenceEquals(poolOwner, state))
            {
                Abandon();
                poolOwner = state;
            }

            EnsureCensus(state);
            return census;
        }

        /// <summary>
        /// Takes the world's labor census for this refresh.
        ///
        /// Seeded like everything else in the economy (§60), so a posting answered before a save and
        /// re-examined after it sees the same world — the census is never persisted, only
        /// reproduced.
        /// </summary>
        private static void EnsureCensus(IntercolonyWorldComponent state)
        {
            if (censusRefreshCount == state.RefreshCount)
            {
                return;
            }

            census.Clear();
            CensusProspectDraws = 0;
            censusRefreshCount = state.RefreshCount;

            float standing = EmployerReputationService.ScoreFor(state);

            // Reputation reaches quality as well as reach (§39 step 9). A colony nobody wants to
            // work for does not get to bypass that by posting a notice, while a mid-range standing
            // still draws each prospect once so the common case is unchanged.
            float availability = EmployerReputationService.AvailabilityFactor(standing);
            int perSettlement = Mathf.Max(1, Mathf.RoundToInt(ProspectsPerSettlement * availability));
            int qualityBias = EmployerReputationService.CandidateQualityBias(standing);

            List<Settlement> sources = EligibleSources(state);
            if (sources.Count == 0)
            {
                return;
            }

            List<SkillDef> skills = DefDatabase<SkillDef>.AllDefsListForReading;
            int skillCount = 0;
            foreach (SkillDef skill in skills)
            {
                if (skill.index >= skillCount)
                {
                    skillCount = skill.index + 1;
                }
            }

            Rand.PushState(Gen.HashCombineInt(state.EconomySeed, state.RefreshCount) ^ 0x4C41_5445);
            try
            {
                foreach (Settlement settlement in sources)
                {
                    if (census.Count >= MaxCensus)
                    {
                        break;
                    }

                    SettlementEconomicProfile profile = state.GetProfile(settlement);
                    if (profile == null || settlement.Faction == null)
                    {
                        continue;
                    }

                    float distance = MarketOpportunityGenerator.DistanceToPlayer(settlement);
                    int travel = TravelDays(distance);

                    for (int i = 0; i < perSettlement && census.Count < MaxCensus; i++)
                    {
                        census.Add(GenerateProspectBiased(
                            settlement, profile, distance, travel, skills, skillCount, qualityBias));
                    }
                }
            }
            finally
            {
                Rand.PopState();
            }

            IntercolonyLog.Verbose(
                $"Labor census taken for refresh {state.RefreshCount}: {census.Count} worker(s) " +
                $"across {sources.Count} settlement(s).");
        }

        /// <summary>
        /// One census record.
        ///
        /// The shape aims at what pawn generation actually produces rather than a flat roll: most
        /// people are unremarkable at most things and good at one or two. That matters because the
        /// posting dialog quotes a band drawn from these records and the applicants who arrive are
        /// priced by the same formula — if the census were uniformly average, every offer would draw
        /// either nobody or everybody, which is the problem the census exists to fix.
        /// </summary>
        private static LaborProspect GenerateProspect(
            Settlement settlement, SettlementEconomicProfile profile, float distance, int travel,
            List<SkillDef> skills, int skillCount)
        {
            CensusProspectDraws++;

            int[] levels = new int[skillCount];
            Passion[] passions = new Passion[skillCount];

            for (int i = 0; i < skillCount; i++)
            {
                levels[i] = -1;
            }

            // A couple of skills the backstory ruled out entirely, as real pawns have.
            int disabled = Rand.RangeInclusive(0, 3);

            List<SkillDef> shuffled = new List<SkillDef>(skills);
            for (int i = shuffled.Count - 1; i > 0; i--)
            {
                int j = Rand.RangeInclusive(0, i);
                SkillDef swap = shuffled[i];
                shuffled[i] = shuffled[j];
                shuffled[j] = swap;
            }

            // Wealthier settlements release better-trained people; §35.1 already prices labor
            // supply, and this is the same idea applied to who exists rather than what they cost.
            int specialityBonus = profile?.wealthTier == IntercolonyWealthTier.Wealthy ? 3
                : profile?.wealthTier == IntercolonyWealthTier.Destitute ? -2
                : 0;

            int specialities = Rand.RangeInclusive(1, 3);

            for (int i = 0; i < shuffled.Count; i++)
            {
                SkillDef skill = shuffled[i];
                if (skill.index >= skillCount)
                {
                    continue;
                }

                if (i < disabled)
                {
                    levels[skill.index] = -1;
                    continue;
                }

                bool speciality = i >= disabled && i < disabled + specialities;

                int level = speciality
                    ? Mathf.Clamp(Rand.RangeInclusive(7, 17) + specialityBonus, 0, 20)
                    : Mathf.Clamp(Rand.RangeInclusive(0, 8) + Mathf.Min(0, specialityBonus), 0, 20);

                levels[skill.index] = level;

                if (speciality)
                {
                    float roll = Rand.Value;
                    passions[skill.index] = roll < 0.18f ? Passion.Major
                        : roll < 0.45f ? Passion.Minor
                        : Passion.None;
                }
            }

            LaborProspect prospect = new LaborProspect
            {
                settlementId = settlement.ID,
                settlementName = settlement.Label ?? "unnamed",
                factionName = settlement.Faction.Name ?? "",
                faction = settlement.Faction,
                distanceTiles = distance,
                travelDays = travel,
                skillLevels = levels,
                passions = passions
            };

            prospect.pricedSkillValue = PricedSkillValue(prospect, skillCount);
            return prospect;
        }

        /// <summary>
        /// Draws a census prospect, and at the extremes of employer reputation draws twice and
        /// keeps the better or worse record. In the middle it draws once, so the ordinary case
        /// costs nothing extra — synthetic records are cheap, but the common path should still
        /// consume the same random sequence.
        /// </summary>
        private static LaborProspect GenerateProspectBiased(
            Settlement settlement, SettlementEconomicProfile profile, float distance, int travel,
            List<SkillDef> skills, int skillCount, int bias)
        {
            LaborProspect first = GenerateProspect(settlement, profile, distance, travel, skills, skillCount);
            if (bias == 0 || first == null)
            {
                return first;
            }

            LaborProspect second = GenerateProspect(settlement, profile, distance, travel, skills, skillCount);
            if (second == null)
            {
                return first;
            }

            int firstBest = BestSkillLevel(first);
            int secondBest = BestSkillLevel(second);

            bool keepSecond = bias > 0 ? secondBest > firstBest : secondBest < firstBest;

            // Census prospects are synthetic records, not pawns, so the discarded draw owns
            // nothing that needs Discard().
            return keepSecond ? second : first;
        }

        /// <summary>
        /// The census-record twin of <see cref="PricedSkillValue(Pawn)"/>. Same rule, same top-N,
        /// same passion weighting — the two must not drift or the advertised band stops matching
        /// the workers who arrive.
        /// </summary>
        private static float PricedSkillValue(LaborProspect prospect, int skillCount)
        {
            List<int> ranked = new List<int>();
            List<Passion> rankedPassions = new List<Passion>();

            for (int i = 0; i < skillCount; i++)
            {
                if (prospect.skillLevels[i] >= 0)
                {
                    ranked.Add(prospect.skillLevels[i]);
                    rankedPassions.Add(prospect.passions[i]);
                }
            }

            // Sort both together, descending by level.
            for (int i = 0; i < ranked.Count; i++)
            {
                for (int j = i + 1; j < ranked.Count; j++)
                {
                    if (ranked[j] > ranked[i])
                    {
                        int level = ranked[i];
                        ranked[i] = ranked[j];
                        ranked[j] = level;

                        Passion passion = rankedPassions[i];
                        rankedPassions[i] = rankedPassions[j];
                        rankedPassions[j] = passion;
                    }
                }
            }

            float value = 0f;
            for (int i = 0; i < SkillsPriced && i < ranked.Count; i++)
            {
                value += WeightedLevel(ranked[i], rankedPassions[i]);
            }

            return value;
        }

        /// <summary>Discards every unhired candidate and empties the listing and the census.</summary>
        public static void Clear()
        {
            ClearPool();

            // Census records own nothing — no pawn, no faction claim — so they are dropped rather
            // than discarded. That is the whole reason a census can be hundreds deep.
            census.Clear();
            censusRefreshCount = -1;
        }

        /// <summary>
        /// Resets only the derived census without discarding the advertised pawn pool. The
        /// performance profile and the job-posting self-test use it to rebuild the census for the
        /// current refresh.
        /// </summary>
        internal static void InvalidateCensus()
        {
            // Clear retains capacity and would understate the first population of a 900-record
            // census. The empty-list construction remains outside the timed region.
            census = new List<LaborProspect>();
            censusRefreshCount = -1;
        }

        private static void ClearPool()
        {
            foreach (LaborCandidate candidate in pool)
            {
                candidate.Discard();
            }

            pool.Clear();
            poolRefreshCount = -1;
        }

        /// <summary>
        /// Drops a pool belonging to a game that is no longer loaded, without touching it.
        ///
        /// Deliberately **not** <see cref="Clear"/>. `Discard` routes through `Find.WorldPawns`,
        /// which now belongs to the *current* game — so discarding a previous game's pawns would
        /// ask this world's registry to dispose of pawns it has never heard of. The old game's
        /// object graph is unreachable and will be collected on its own; the only thing that has to
        /// happen here is that this list stops pointing at it.
        /// </summary>
        private static void Abandon()
        {
            if (pool.Count > 0)
            {
                IntercolonyLog.Verbose(
                    $"Dropped {pool.Count} candidate(s) left over from a previous game.");
            }

            pool.Clear();
            poolRefreshCount = -1;

            census.Clear();
            censusRefreshCount = -1;

            poolOwner = null;
        }

        /// <summary>Removes a candidate from the pool without discarding its pawn (it has been hired).</summary>
        public static void Take(LaborCandidate candidate)
        {
            // Tried against both halves, because a worker taken on as an applicant to a posting
            // comes from the latent pool rather than the advertised one, and either way the pool
            // must stop owning the pawn or it will be discarded out from under the contract.
            pool.Remove(candidate);
        }

        /// <summary>
        /// Draws a candidate, and at the extremes of employer reputation draws twice and keeps the
        /// better or worse of the two (§112 "a bad employer experiences meaningfully worse hiring
        /// conditions"). In the middle it draws once, so the ordinary case costs nothing extra —
        /// generating pawns is the expensive part of building this listing.
        /// </summary>
        private static LaborCandidate GenerateBiased(
            Settlement settlement, SettlementEconomicProfile profile, float standing, int bias)
        {
            LaborCandidate first = Generate(settlement, profile, standing);
            if (bias == 0 || first == null)
            {
                return first;
            }

            LaborCandidate second = Generate(settlement, profile, standing);
            if (second == null)
            {
                return first;
            }

            int firstBest = BestSkillLevel(first);
            int secondBest = BestSkillLevel(second);

            bool keepSecond = bias > 0 ? secondBest > firstBest : secondBest < firstBest;
            LaborCandidate kept = keepSecond ? second : first;
            LaborCandidate discarded = keepSecond ? first : second;

            discarded.Discard();
            return kept;
        }

        private static int BestSkillLevel(LaborCandidate candidate)
        {
            if (candidate?.pawn?.skills == null)
            {
                return 0;
            }

            int best = 0;
            foreach (SkillRecord skill in candidate.pawn.skills.skills)
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
            if (prospect?.skillLevels == null || prospect.skillLevels.Length == 0)
            {
                return 0;
            }

            int best = 0;
            foreach (int level in prospect.skillLevels)
            {
                if (level >= 0 && level > best)
                {
                    best = level;
                }
            }

            return best;
        }

        private static LaborCandidate Generate(
            Settlement settlement, SettlementEconomicProfile profile, float standing)
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

                // The listed rate is the civilian rate — the cheapest terms available, and the
                // baseline the hiring dialog prices the other two clauses against. Listing anything
                // else would advertise a price nobody had chosen yet.
                dailyWage = DailyWage(pawn, profile, distance, minTerm, standing, CombatClause.Civilian)
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
        /// <param name="employerStanding">
        /// Colony employer reputation (§40). Deliberately **required**, not defaulted: a default
        /// would let a call site silently price at neutral while the listing showed a bad
        /// employer's premium, and the hire would then charge a different number than it quoted.
        /// That exact shape of bug has appeared twice before (the Phase 12 quantity slider and the
        /// Phase 10 gold bed), both times because a pricing input was easy to omit.
        /// </param>
        /// <param name="clause">
        /// §42's combat clause, and required for the same reason. This is the largest single
        /// multiplier in the whole formula — a security contractor costs two and a half times a
        /// civilian — so a defaulted value here would be the most expensive possible mispricing.
        /// </param>
        public static int DailyWage(
            Pawn pawn, SettlementEconomicProfile profile, float distance, int termDays,
            float employerStanding, CombatClause clause)
        {
            return DailyWageFor(PricedSkillValue(pawn), profile, distance, termDays,
                employerStanding, clause);
        }

        /// <summary>
        /// What a pawn's skills are worth to the wage formula: their best few, weighted for passion.
        ///
        /// Split out so a <see cref="LaborProspect"/> — a census record with no pawn behind it — can
        /// produce the same number from the same rule. The two **must** agree: the posting dialog
        /// quotes a going-rate band computed from census records, and the worker who eventually
        /// arrives is a real pawn. If the two priced differently, the band would be a lie the player
        /// only discovers after hiring.
        /// </summary>
        public static float PricedSkillValue(Pawn pawn)
        {
            if (pawn?.skills == null)
            {
                return 0f;
            }

            List<SkillRecord> ranked = new List<SkillRecord>(pawn.skills.skills);
            ranked.RemoveAll(s => s.TotallyDisabled);
            ranked.Sort((a, b) => b.Level.CompareTo(a.Level));

            float skillValue = 0f;
            for (int i = 0; i < SkillsPriced && i < ranked.Count; i++)
            {
                skillValue += WeightedLevel(ranked[i].Level, ranked[i].passion);
            }

            return skillValue;
        }

        /// <summary>
        /// Passion is worth paying for: it is the difference between a skill that stays where it is
        /// and one that grows over a long contract.
        /// </summary>
        public static float WeightedLevel(int level, Passion passion)
        {
            if (passion == Passion.Major)
            {
                return level * 1.15f;
            }

            return passion == Passion.Minor ? level * 1.07f : level;
        }

        /// <summary>How many of a worker's best skills the wage prices.</summary>
        public static int PricedSkillCount => SkillsPriced;

        /// <summary>
        /// The wage formula proper, given an already-computed skill value. Everything that is not
        /// the worker themselves — distance, the settlement's spare labor, term length, employer
        /// standing, combat clause — is applied here, once, for pawns and census records alike.
        /// </summary>
        public static int DailyWageFor(
            float skillValue, SettlementEconomicProfile profile, float distance, int termDays,
            float employerStanding, CombatClause clause)
        {
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

            // §39 step 9: a bad employer pays a risk premium, a good one gets a discount because
            // people want the job. The widest of the reputation effects, per §112's "meaningfully".
            wage *= EmployerReputationService.WageFactor(employerStanding);

            // §42: "higher wage" for an armed employee, "much higher wage" for a security
            // contractor. Applied last so it multiplies everything else — a distant soldier for a
            // bad employer is expensive on every axis at once, which is the intent.
            wage *= clause.WageFactor();

            // The player's own thumb on the scale, applied last so it scales the finished
            // figure rather than compounding oddly with the factors above. Read live, so it
            // only ever affects wages being quoted now: an employment already agreed keeps the
            // wage it was signed at, exactly as economy difficulty leaves agreed prices alone.
            wage *= LaborBaselineMultiplier * IntercolonyMod.Settings.laborCostMultiplier;

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
