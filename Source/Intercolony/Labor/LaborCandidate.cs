using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// A worker a settlement is willing to hire out (DESIGN.md §35.1).
    ///
    /// Candidates are deliberately **not persisted**. The pawn behind a candidate has not been
    /// hired, is not spawned, and belongs to no collection the game saves; keeping a reference
    /// to it across a save would either leak a world pawn or dangle on load. The listing is
    /// regenerated instead — the same choice §96 makes for anything derivable.
    ///
    /// A candidate owns its pawn until <see cref="Release"/> is called. Whoever hires it takes
    /// ownership; whoever discards the listing must discard the pawns with it, or the generated
    /// pawns pile up in memory.
    /// </summary>
    public class LaborCandidate
    {
        public Pawn pawn;

        public int settlementId;
        public string settlementName = "";
        public string factionName = "";
        public Faction faction;

        public float distanceTiles;

        /// <summary>Silver per day, quoted for <see cref="minTermDays"/>. Longer terms are cheaper per day.</summary>
        public int dailyWage;

        public int minTermDays;

        /// <summary>Days of travel between the source settlement and the colony.</summary>
        public int travelDays;

        public string Name => pawn?.LabelShortCap ?? "?";

        /// <summary>Top skills, highest first — the summary §35.1 shows in a worker listing.</summary>
        public string SkillSummary(int count = 3)
        {
            if (pawn?.skills == null)
            {
                return "no skills";
            }

            List<SkillRecord> ranked = new List<SkillRecord>(pawn.skills.skills);
            ranked.RemoveAll(s => s.TotallyDisabled);
            ranked.Sort((a, b) => b.Level.CompareTo(a.Level));

            StringBuilder sb = new StringBuilder();
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

        /// <summary>Hands the pawn to a caller that will keep it alive. The candidate stops owning it.</summary>
        public Pawn Release()
        {
            Pawn p = pawn;
            pawn = null;
            return p;
        }

        /// <summary>Throws away the generated pawn. Safe to call twice.</summary>
        public void Discard()
        {
            if (pawn == null)
            {
                return;
            }

            // RemoveAndDiscardPawnViaGC is the vanilla disposal path (Verse.DebugToolsGeneral:168),
            // but it starts with RemovePawn, which logs a red error when the pawn was never in
            // WorldPawns — and a listed candidate never is. Unguarded, refreshing the pool once
            // filled the log with "Tried to remove pawn X, but it's not here".
            if (Find.WorldPawns != null && Find.WorldPawns.Contains(pawn))
            {
                Find.WorldPawns.RemoveAndDiscardPawnViaGC(pawn);
            }
            else
            {
                // What RemoveAndDiscardPawnViaGC does past the removal: destroy, then discard.
                if (!pawn.Destroyed)
                {
                    pawn.Destroy(DestroyMode.Vanish);
                }

                if (!pawn.Discarded)
                {
                    pawn.Discard(silentlyRemoveReferences: true);
                }
            }

            pawn = null;
        }

        public override string ToString()
        {
            return $"{Name} ({SkillSummary()}) — {dailyWage}/day, min {minTermDays}d, from {settlementName}";
        }
    }
}
