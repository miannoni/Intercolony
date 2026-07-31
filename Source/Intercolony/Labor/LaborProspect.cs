using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// One worker in the labor census — someone who exists as far as the market is concerned, but
    /// not yet as a <see cref="Pawn"/> (DESIGN.md §35.2, §114).
    ///
    /// **Why this type exists.** A job posting is answered by whoever in the world will take the
    /// offer, and that question is only interesting if "the world" is deep. With a few dozen
    /// workers the answer is lumpy: one silver either way takes a posting from nobody interested to
    /// everybody interested, because there were only three qualified people and they all cost about
    /// the same. A market needs hundreds of workers before an offer has a *shape*.
    ///
    /// Hundreds of <c>PawnGenerator.GeneratePawn</c> calls per refresh is not available — generating
    /// pawns is the expensive part of building any listing, which is why the advertised pool is
    /// capped at twenty. But the matcher never needs a pawn. It needs two things: can this person do
    /// the work, and what do they charge. Both are a handful of numbers.
    ///
    /// So the census is deep and cheap, and a real pawn is built only for the few who actually
    /// apply — see <see cref="Materialise"/>. The pawn is then aligned to the record, so the person
    /// who turns up is the person the market described.
    /// </summary>
    public class LaborProspect
    {
        public int settlementId;
        public string settlementName = "";
        public string factionName = "";
        public Faction faction;

        public float distanceTiles;
        public int travelDays;

        /// <summary>Level per skill, indexed by <c>SkillDef.index</c>. -1 means the skill is disabled.</summary>
        public int[] skillLevels;

        /// <summary>Passion per skill, same indexing.</summary>
        public Passion[] passions;

        /// <summary>
        /// The top-three-with-passion figure the wage formula prices off, computed once.
        ///
        /// Precomputed because the matcher asks for it once per open posting per worker, and the
        /// census is hundreds of workers deep — recomputing a sort on every question would undo the
        /// whole point of not building pawns.
        /// </summary>
        public float pricedSkillValue;

        public int LevelOf(SkillDef skill)
        {
            if (skill == null || skillLevels == null || skill.index >= skillLevels.Length)
            {
                return 0;
            }

            return Mathf.Max(0, skillLevels[skill.index]);
        }

        public bool CanDo(SkillDef skill)
        {
            return skill == null ||
                   (skillLevels != null && skill.index < skillLevels.Length && skillLevels[skill.index] >= 0);
        }

        /// <summary>Highest level across every skill they can use — the "how good are they" summary.</summary>
        public int BestSkillLevel
        {
            get
            {
                int best = 0;
                if (skillLevels == null)
                {
                    return 0;
                }

                for (int i = 0; i < skillLevels.Length; i++)
                {
                    if (skillLevels[i] > best)
                    {
                        best = skillLevels[i];
                    }
                }

                return best;
            }
        }

        /// <summary>
        /// Builds the actual person, once they have applied for something.
        ///
        /// The generated pawn's own skills are overwritten with the census record's, and that is the
        /// point rather than a compromise: the player chose an offer against a market where this
        /// worker had these skills at this price, so the worker who arrives must have them. A pawn
        /// whose skills were merely *near* the advertised ones would make the going-rate band a
        /// polite fiction.
        ///
        /// Skills the backstory disabled are left alone — the census records those as -1 and never
        /// prices them, so there is nothing to align.
        /// </summary>
        public Pawn Materialise()
        {
            if (faction == null)
            {
                return null;
            }

            PawnKindDef kind = faction.RandomPawnKind();
            if (kind?.RaceProps == null || !kind.RaceProps.Humanlike)
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
                IntercolonyLog.Warning($"Could not materialise an applicant for {settlementName}: {ex.Message}");
                return null;
            }

            // Never hire a faction leader: SetFaction fires Notify_LeaderLost on their faction
            // (docs/LABOR_TECHNICAL_NOTES.md).
            if (pawn.Faction != null && pawn.Faction.leader == pawn)
            {
                Find.WorldPawns?.RemoveAndDiscardPawnViaGC(pawn);
                return null;
            }

            AlignSkills(pawn);
            return pawn;
        }

        private void AlignSkills(Pawn pawn)
        {
            if (pawn?.skills == null || skillLevels == null)
            {
                return;
            }

            foreach (SkillRecord record in pawn.skills.skills)
            {
                if (record.TotallyDisabled || record.def.index >= skillLevels.Length)
                {
                    continue;
                }

                int level = skillLevels[record.def.index];
                if (level < 0)
                {
                    continue;
                }

                record.Level = Mathf.Clamp(level, 0, 20);
                record.passion = passions != null && record.def.index < passions.Length
                    ? passions[record.def.index]
                    : Passion.None;

                // Fresh arrivals should not come with a bar half-full towards the next level.
                record.xpSinceLastLevel = 0f;
            }
        }

        /// <summary>Top skills, highest first — the same summary shape a candidate shows.</summary>
        public string SkillSummary(int count = 3)
        {
            if (skillLevels == null)
            {
                return "no skills";
            }

            List<SkillDef> ranked = new List<SkillDef>(DefDatabase<SkillDef>.AllDefsListForReading);
            ranked.RemoveAll(s => s.index >= skillLevels.Length || skillLevels[s.index] <= 0);
            ranked.Sort((a, b) => skillLevels[b.index].CompareTo(skillLevels[a.index]));

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < count && i < ranked.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(ranked[i].skillLabel.CapitalizeFirst()).Append(' ')
                  .Append(skillLevels[ranked[i].index]);
            }

            return sb.Length > 0 ? sb.ToString() : "no usable skills";
        }

        public override string ToString()
        {
            return $"{SkillSummary()} from {settlementName} ({distanceTiles:0} tiles)";
        }
    }
}
