using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    public enum JobPostingStatus
    {
        /// <summary>Open, and re-examined against the world pool on every market refresh.</summary>
        Open,

        /// <summary>Every position was filled.</summary>
        Filled,

        /// <summary>Ran its course. May have filled some positions first.</summary>
        Expired,

        /// <summary>Taken down by the player.</summary>
        Withdrawn
    }

    /// <summary>
    /// A worker who answered a posting (DESIGN.md §35.2).
    ///
    /// **Deliberately has no asking wage, and that absence is the whole inversion.** A
    /// <see cref="LaborCandidate"/> quotes a price and the player decides whether to pay it; an
    /// applicant has already accepted the price the player named. §35.2 is the market seen from the
    /// other side, and the two types differ in exactly that one field.
    ///
    /// Unlike a candidate, an applicant **is** persisted, because a posting is a standing order that
    /// spans refreshes and saves. That means the pawn has to be pinned in <c>WorldPawns</c> as
    /// <c>KeepForever</c> — the same mechanism a travelling employee uses, with the same hazard
    /// recorded in docs/LABOR_TECHNICAL_NOTES.md: a pin with no matching discard keeps the pawn
    /// forever. Every exit from this type routes through <see cref="Discard"/>.
    /// </summary>
    public class JobApplicant : IExposable
    {
        public Pawn pawn;

        public int settlementId;
        public string settlementName = "";
        public string factionName = "";
        public Faction faction;

        public float distanceTiles;
        public int travelDays;

        /// <summary>Level in the skill the posting asked for, frozen so the list reads after the pawn is gone.</summary>
        public int requiredSkillLevel;

        /// <summary>
        /// What this worker would have charged on the open market. Kept for the player's benefit
        /// only — they are being paid the posted wage, not this — because "asks 34, you offered 38"
        /// is the single most useful thing to know when choosing between applicants.
        /// </summary>
        public int openMarketAsk;

        /// <summary>When they applied, so a stale applicant can be aged out.</summary>
        public int appliedTick;

        public string Name => pawn?.LabelShortCap ?? "?";

        public float DaysWaiting => (GenTicks.TicksGame - appliedTick) / (float)GenDate.TicksPerDay;

        /// <summary>How much cheaper than their market rate this hire is. Negative never happens — they would not have applied.</summary>
        public int Bargain(int wageOffered) => wageOffered - openMarketAsk;

        public string SkillSummary(int count = 3)
        {
            if (pawn?.skills == null)
            {
                return "no skills";
            }

            List<SkillRecord> ranked = new List<SkillRecord>(pawn.skills.skills);
            ranked.RemoveAll(s => s.TotallyDisabled);
            ranked.Sort((a, b) => b.Level.CompareTo(a.Level));

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < count && i < ranked.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(ranked[i].def.skillLabel.CapitalizeFirst()).Append(' ').Append(ranked[i].Level);

                if (ranked[i].passion == Passion.Major)
                {
                    sb.Append("!!");
                }
                else if (ranked[i].passion == Passion.Minor)
                {
                    sb.Append('!');
                }
            }

            return sb.Length > 0 ? sb.ToString() : "no usable skills";
        }

        /// <summary>Hands the pawn to a caller that will keep it alive. The applicant stops owning it.</summary>
        public Pawn Release()
        {
            Pawn p = pawn;
            pawn = null;
            return p;
        }

        /// <summary>
        /// Throws away the pawn. Safe to call twice.
        ///
        /// Identical to <see cref="LaborCandidate.Discard"/> and for the same reason: hand-rolled
        /// destroy-then-discard adds the pawn to <c>WorldPawns</c> on the way out and then fails to
        /// remove it. Both branches go through vanilla's own disposal.
        /// </summary>
        public void Discard()
        {
            if (pawn == null)
            {
                return;
            }

            if (Find.WorldPawns == null)
            {
                pawn = null;
                return;
            }

            if (Find.WorldPawns.Contains(pawn))
            {
                Find.WorldPawns.RemoveAndDiscardPawnViaGC(pawn);
            }
            else
            {
                Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.Discard);
            }

            pawn = null;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(ref settlementId, "settlementId", -1);
            Scribe_Values.Look(ref settlementName, "settlementName", "");
            Scribe_Values.Look(ref factionName, "factionName", "");
            Scribe_Values.Look(ref distanceTiles, "distanceTiles", 0f);
            Scribe_Values.Look(ref travelDays, "travelDays", 0);
            Scribe_Values.Look(ref requiredSkillLevel, "requiredSkillLevel", 0);
            Scribe_Values.Look(ref openMarketAsk, "openMarketAsk", 0);
            Scribe_Values.Look(ref appliedTick, "appliedTick", 0);
        }

        /// <summary>An applicant whose pawn did not survive the load has nobody left to hire.</summary>
        public bool IsValidAfterLoad => pawn != null;

        public override string ToString()
        {
            return $"{Name} ({SkillSummary()}) from {settlementName}, asks {openMarketAsk}/day";
        }
    }

    /// <summary>
    /// A standing advertisement for workers (DESIGN.md §35.2, §114).
    ///
    /// §35.2's closing line is the design goal: *"This turns hiring into an actual market."* The
    /// inversion from §35.1 is that the **player** names the price. A settlement's listing quotes
    /// what a worker wants; a posting says what the colony will pay, and the world decides whether
    /// that is enough.
    ///
    /// It is a *standing* order rather than a one-shot request: it is re-examined against the world
    /// labor pool on every market refresh until it fills or lapses. That is what makes it worth
    /// using over the Hire tab — browsing shows who is available this minute, a posting catches
    /// whoever turns up over the next season.
    /// </summary>
    public class JobPosting : IExposable
    {
        public int id;

        // --- Terms (§35.2's example screen, field for field) -------------------------------

        /// <summary>The skill the job needs. Null means "any worker".</summary>
        public SkillDef skill;

        public int minSkillLevel;

        public int termDays;

        /// <summary>Silver per day the colony is offering. The whole point: the player sets this.</summary>
        public int wageOffered;

        public WageStructure wageStructure = WageStructure.Daily;

        public CombatClause combatClause = CombatClause.Civilian;

        // --- Lifecycle ---------------------------------------------------------------------

        public int postedTick;
        public int expiryTick;

        public JobPostingStatus status = JobPostingStatus.Open;
        public string outcomeNote = "";

        /// <summary>Positions already filled from this posting.</summary>
        public int hired;

        /// <summary>
        /// Refreshes this posting has been examined against without drawing anyone. Drives the
        /// "nobody answered, and here is why" letter, which fires once rather than every cycle.
        /// </summary>
        public int emptyCycles;

        public bool noAnswerNotified;

        private List<JobApplicant> applicants = new List<JobApplicant>();

        public List<JobApplicant> Applicants => applicants;

        public bool IsOpen => status == JobPostingStatus.Open;

        public bool NeverExpires => expiryTick == -1;

        /// <summary>How long the posting remains available, in words. Never formats the sentinel.</summary>
        public string ExpiryLabel
        {
            get
            {
                if (NeverExpires)
                {
                    return "stays up until filled";
                }

                float daysUntilExpiry =
                    (expiryTick - GenTicks.TicksGame) / (float)GenDate.TicksPerDay;
                return $"{Mathf.Max(0f, daysUntilExpiry):0.#}d left";
            }
        }

        public float DaysPosted => (GenTicks.TicksGame - postedTick) / (float)GenDate.TicksPerDay;

        /// <summary>What each worker taken on from this posting costs over the full term.</summary>
        public int TotalCommitment =>
            WageStructureUtility.TotalCost(wageStructure, wageOffered, termDays);

        public string SkillLabel =>
            skill == null ? "any work" : $"{skill.skillLabel.CapitalizeFirst()} {minSkillLevel}+";

        /// <summary>§35.2's headline, one line.</summary>
        public string Headline()
        {
            return $"{SkillLabel} — open, {termDays}d, {wageOffered} silver/day " +
                   $"{wageStructure.Label()}, {combatClause.Label()}";
        }

        public string StatusLine()
        {
            switch (status)
            {
                case JobPostingStatus.Open:
                    if (applicants.Count > 0)
                    {
                        return $"{applicants.Count} applicant{(applicants.Count == 1 ? "" : "s")} waiting" +
                               (hired > 0 ? $", {hired} hired so far" : "");
                    }

                    if (emptyCycles > 0)
                    {
                        return $"no replies yet — {ExpiryLabel}";
                    }

                    return $"posted, awaiting replies — {ExpiryLabel}";
                case JobPostingStatus.Filled:
                    return $"filled — {hired} hired";
                case JobPostingStatus.Withdrawn:
                    return "withdrawn";
                default:
                    return outcomeNote.NullOrEmpty()
                        ? $"expired — {hired} hired"
                        : outcomeNote;
            }
        }

        /// <summary>Whether this worker meets the skill bar. Null skill accepts anyone.</summary>
        public bool MeetsRequirement(Pawn pawn)
        {
            if (skill == null)
            {
                return true;
            }

            if (pawn?.skills == null)
            {
                return false;
            }

            SkillRecord record = pawn.skills.GetSkill(skill);
            return record != null && !record.TotallyDisabled && record.Level >= minSkillLevel;
        }

        /// <summary>
        /// The census-record twin of <see cref="MeetsRequirement(Pawn)"/>. Kept beside it so the
        /// two rules stay visibly the same: a worker who qualifies as a record must still qualify
        /// once they are a pawn, or the applicant who arrives is not the one who was advertised.
        /// </summary>
        public bool MeetsRequirement(LaborProspect prospect)
        {
            if (skill == null)
            {
                return true;
            }

            return prospect != null && prospect.CanDo(skill) && prospect.LevelOf(skill) >= minSkillLevel;
        }

        public int SkillLevelOf(Pawn pawn)
        {
            if (skill == null || pawn?.skills == null)
            {
                return 0;
            }

            SkillRecord record = pawn.skills.GetSkill(skill);
            return record == null || record.TotallyDisabled ? 0 : record.Level;
        }

        /// <summary>Discards every waiting applicant. Called whenever the posting stops being open.</summary>
        public void DiscardApplicants()
        {
            for (int i = 0; i < applicants.Count; i++)
            {
                applicants[i]?.Discard();
            }

            applicants.Clear();
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", 0);
            Scribe_Defs.Look(ref skill, "skill");
            Scribe_Values.Look(ref minSkillLevel, "minSkillLevel", 0);
            Scribe_Values.Look(ref termDays, "termDays", 0);
            Scribe_Values.Look(ref wageOffered, "wageOffered", 0);
            Scribe_Values.Look(ref wageStructure, "wageStructure", WageStructure.Daily);
            Scribe_Values.Look(ref combatClause, "combatClause", CombatClause.Civilian);

            Scribe_Values.Look(ref postedTick, "postedTick", 0);
            Scribe_Values.Look(ref expiryTick, "expiryTick", 0);
            Scribe_Values.Look(ref status, "status", JobPostingStatus.Open);
            Scribe_Values.Look(ref outcomeNote, "outcomeNote", "");
            Scribe_Values.Look(ref hired, "hired", 0);
            Scribe_Values.Look(ref emptyCycles, "emptyCycles", 0);
            Scribe_Values.Look(ref noAnswerNotified, "noAnswerNotified", false);

            // Deep, unlike everything else that holds a pawn in this mod. An applicant is not
            // stored anywhere the game saves on its own — no map, no caravan, no world object — so
            // the posting is its only owner and has to write it out in full.
            Scribe_Collections.Look(ref applicants, "applicants", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (applicants == null)
                {
                    applicants = new List<JobApplicant>();
                }

                // An applicant whose pawn failed to resolve is unhireable and would otherwise sit
                // in the list forever showing "?".
                applicants.RemoveAll(a => a == null || !a.IsValidAfterLoad);
            }
        }

        public bool IsValidAfterLoad => termDays > 0 && wageOffered > 0;

        public override string ToString()
        {
            return $"Posting #{id} {Headline()} [{status}] — {applicants.Count} applicant(s), " +
                   $"{hired} hired";
        }
    }
}
