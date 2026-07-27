using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Phase 15 labor control spike (DESIGN.md §108, §33, §34).
    ///
    /// §33 is titled "Labor technical spike — mandatory" and lists twenty questions that must
    /// be answered before any labor economy is written. §34 lists two candidate strategies and
    /// ends with the instruction that matters: "Choose based on experiments, not aesthetics."
    ///
    /// This probe runs Strategy A — temporary transfer into the player faction — against a
    /// real generated pawn, records what actually changes, restores the pawn, and reports any
    /// residue. Findings are written up in <c>docs/LABOR_TECHNICAL_NOTES.md</c>, which is the
    /// phase's deliverable.
    ///
    /// Questions that cannot be answered without killing, capturing or waiting on a pawn are
    /// reported as UNRESOLVED rather than guessed at.
    /// </summary>
    public static class IntercolonyLaborSpike
    {
        private const string ProbeName = "IntercolonyLaborProbe";

        /// <summary>Snapshot of everything Strategy A is known to touch.</summary>
        private class PawnSnapshot
        {
            public Faction faction;
            public PawnKindDef kindDef;
            public string name;
            public Ideo ideo;
            public int directRelations;
            public bool hadWorkSettings;
            public bool hadDrafter;
            public bool hadOutfits;
            public bool hadDrugs;
            public bool hadTimetable;
            public bool hadFoodRestriction;
            public bool hadPlayerSettings;
            public bool wasColonist;
            public GuestStatus? guestStatus;
        }

        public static string Run(Map map)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Labor control spike (DESIGN.md §108, §33, §34) — Strategy A: faction transfer");

            if (map == null)
            {
                sb.AppendLine("  No map. Open a colony first.");
                return sb.ToString();
            }

            Faction employer = FindEmployerFaction();
            if (employer == null)
            {
                sb.AppendLine("  No non-player humanlike faction available.");
                return sb.ToString();
            }

            Pawn pawn = null;
            try
            {
                // Faction.RandomPawnKind draws from the faction's pawnGroupMakers.
                // def.basicMemberKind is NOT usable here: only the *player* faction defs
                // define it, so keying off it excluded every possible employer.
                PawnKindDef kind = employer.RandomPawnKind();
                if (kind == null)
                {
                    sb.AppendLine($"  {employer.Name} has no humanlike pawn kinds.");
                    return sb.ToString();
                }

                pawn = PawnGenerator.GeneratePawn(kind, employer);
                pawn.Name = new NameSingle(ProbeName);

                IntVec3 cell = DropCellFinder.TradeDropSpot(map);
                GenSpawn.Spawn(pawn, cell, map);

                sb.AppendLine($"  Subject: {pawn.LabelShort}, {employer.Name} ({employer.def.techLevel})");
                sb.AppendLine();

                PawnSnapshot before = Capture(pawn);
                ReportBefore(sb, before);

                // --- Apply Strategy A ---
                pawn.SetFaction(Faction.OfPlayer);
                sb.AppendLine();
                sb.AppendLine("  After transfer to the player faction:");
                ProbeControlQuestions(sb, pawn, map);

                // --- Restore (§33 q11, q19) ---
                sb.AppendLine();
                sb.AppendLine("  After restoring the original faction:");
                pawn.SetFaction(employer);
                ReportResidue(sb, pawn, before);
            }
            catch (System.Exception ex)
            {
                sb.AppendLine($"  EXCEPTION during spike: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                // The probe pawn must not be left in the player's world.
                if (pawn != null && !pawn.Destroyed)
                {
                    if (pawn.Spawned)
                    {
                        pawn.DeSpawn(DestroyMode.Vanish);
                    }

                    pawn.Destroy(DestroyMode.Vanish);
                }
            }

            sb.AppendLine();
            sb.AppendLine("  See docs/LABOR_TECHNICAL_NOTES.md for the written findings.");
            return sb.ToString();
        }

        private static void ReportBefore(StringBuilder sb, PawnSnapshot s)
        {
            sb.AppendLine("  Before transfer (foreign faction):");
            sb.AppendLine($"    kindDef            : {s.kindDef?.defName}");
            sb.AppendLine($"    IsColonist         : {s.wasColonist}");
            sb.AppendLine($"    workSettings       : {s.hadWorkSettings}");
            sb.AppendLine($"    drafter            : {s.hadDrafter}");
            sb.AppendLine($"    outfits/drugs/time : {s.hadOutfits}/{s.hadDrugs}/{s.hadTimetable}");
            sb.AppendLine($"    foodRestriction    : {s.hadFoodRestriction}");
            sb.AppendLine($"    playerSettings     : {s.hadPlayerSettings}");
            sb.AppendLine($"    direct relations   : {s.directRelations}");
            sb.AppendLine($"    ideo               : {s.ideo?.name ?? "none"}");
        }

        /// <summary>Answers as many of §33's twenty questions as can be settled in code.</summary>
        private static void ProbeControlQuestions(StringBuilder sb, Pawn pawn, Map map)
        {
            // q1 selectable
            sb.AppendLine($"    q1  selectable            : {pawn.Spawned && pawn.IsColonist} " +
                          "(colonist gates drive selection UI)");

            // q2 work priorities
            bool workOk = false;
            string workDetail = "no workSettings";
            if (pawn.workSettings != null)
            {
                WorkTypeDef work = FindEnabledWorkType(pawn);
                if (work != null)
                {
                    pawn.workSettings.SetPriority(work, 3);
                    workOk = pawn.workSettings.GetPriority(work) == 3;
                    workDetail = $"{work.defName} priority set and read back";
                }
                else
                {
                    workDetail = "pawn has no enabled work types";
                }
            }

            sb.AppendLine($"    q2  work priorities       : {workOk} ({workDetail})");

            // q3/q4 workbenches and beds — gated on being a colonist, but actual use needs
            // observation over time. Report what is checkable.
            sb.AppendLine($"    q3  workbench eligibility : {pawn.IsColonist} " +
                          "(bill workers require a free colonist; real use needs observation)");
            sb.AppendLine($"    q4  bed assignable        : {pawn.IsFreeColonist} " +
                          "(bed ownership requires a free colonist)");

            // q5 food policy
            sb.AppendLine($"    q5  food policy           : {pawn.foodRestriction != null}");

            // q6 areas
            bool areaOk = false;
            if (pawn.playerSettings != null)
            {
                pawn.playerSettings.AreaRestrictionInPawnCurrentMap = null;
                areaOk = true;
            }

            sb.AppendLine($"    q6  area assignable       : {areaOk}");

            // q7 drafting
            bool draftOk = false;
            if (pawn.drafter != null)
            {
                pawn.drafter.Drafted = true;
                draftOk = pawn.Drafted;
                pawn.drafter.Drafted = false;
            }

            sb.AppendLine($"    q7  draftable             : {draftOk}");

            // q8 combat records
            sb.AppendLine($"    q8  combat trackable      : {pawn.records != null} " +
                          "(records tracker present; kill attribution needs observation)");

            // q9/q10 caravans
            sb.AppendLine($"    q9  caravan eligible      : {pawn.IsFreeColonist} " +
                          "(caravan forming lists free colonists)");
            sb.AppendLine($"    q10 return to colony      : {pawn.IsFreeColonist} (same gate)");

            // q17 ideology
            sb.AppendLine($"    q17 ideo retained         : {pawn.Ideo?.name ?? "none"}");

            // Side effects worth naming explicitly — these are the ones that bite later.
            sb.AppendLine($"    !!  kindDef now           : {pawn.kindDef?.defName} " +
                          "(SetFaction calls ChangeKind for humanlikes joining the player)");
            sb.AppendLine($"    !!  guest status cleared  : {pawn.guest?.GuestStatus.ToString() ?? "no guest tracker"}");
        }

        private static void ReportResidue(StringBuilder sb, Pawn pawn, PawnSnapshot before)
        {
            PawnSnapshot after = Capture(pawn);

            bool factionRestored = after.faction == before.faction;
            bool kindRestored = after.kindDef == before.kindDef;
            bool relationsSame = after.directRelations == before.directRelations;
            bool ideoSame = after.ideo == before.ideo;
            bool drafterGone = !after.hadDrafter;
            bool colonistGone = !after.wasColonist;

            sb.AppendLine($"    faction restored          : {factionRestored}");
            sb.AppendLine($"    kindDef restored          : {kindRestored} " +
                          $"({before.kindDef?.defName} -> {after.kindDef?.defName})");
            sb.AppendLine($"    no longer a colonist      : {colonistGone}");
            sb.AppendLine($"    drafter removed           : {drafterGone}");
            sb.AppendLine($"    relations unchanged       : {relationsSame} " +
                          $"({before.directRelations} -> {after.directRelations})");
            sb.AppendLine($"    ideo unchanged            : {ideoSame}");

            bool clean = factionRestored && colonistGone && drafterGone && relationsSame && ideoSame;
            sb.AppendLine(clean && kindRestored
                ? "    VERDICT: restored with no detected residue."
                : "    VERDICT: residue detected — see the flags above and the notes document.");

            if (!kindRestored)
            {
                sb.AppendLine("    NOTE: kindDef is NOT restored automatically. Any implementation must " +
                              "capture and reapply it, or every employee returns home as a colonist-kind pawn.");
            }
        }

        private static PawnSnapshot Capture(Pawn pawn)
        {
            return new PawnSnapshot
            {
                faction = pawn.Faction,
                kindDef = pawn.kindDef,
                name = pawn.LabelShort,
                ideo = pawn.Ideo,
                directRelations = pawn.relations?.DirectRelations?.Count ?? 0,
                hadWorkSettings = pawn.workSettings != null,
                hadDrafter = pawn.drafter != null,
                hadOutfits = pawn.outfits != null,
                hadDrugs = pawn.drugs != null,
                hadTimetable = pawn.timetable != null,
                hadFoodRestriction = pawn.foodRestriction != null,
                hadPlayerSettings = pawn.playerSettings != null,
                wasColonist = pawn.IsColonist,
                guestStatus = pawn.guest?.GuestStatus
            };
        }

        private static WorkTypeDef FindEnabledWorkType(Pawn pawn)
        {
            foreach (WorkTypeDef work in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                if (!pawn.WorkTypeIsDisabled(work))
                {
                    return work;
                }
            }

            return null;
        }

        private static Faction FindEmployerFaction()
        {
            foreach (Faction faction in Find.FactionManager.AllFactionsListForReading)
            {
                if (faction.IsPlayer || faction.Hidden || faction.temporary)
                {
                    continue;
                }

                // basicMemberKind is deliberately not checked: only player faction defs set
                // it. Require a humanlike faction that is not at war and can actually produce
                // a pawn kind.
                if (faction.def.humanlikeFaction &&
                    !faction.HostileTo(Faction.OfPlayer) &&
                    faction.RandomPawnKind() != null)
                {
                    return faction;
                }
            }

            return null;
        }
    }
}
