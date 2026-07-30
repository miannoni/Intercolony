using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// The Labor tab (DESIGN.md §35.1, §110).
    ///
    /// §110's acceptance criterion is "a player can make a hiring decision without dev tools or
    /// hidden information", so everything the decision needs is on screen at once: who is
    /// available, what they are good at, where they are from, what they cost per day, the
    /// shortest term they will accept, and how long they take to arrive. Employed workers sit
    /// above the listing rather than behind a sub-tab, because "how long is my mason here for"
    /// is a question the player asks while deciding whether to hire another one.
    ///
    /// Split into its own file: <see cref="MainTabWindow_Intercolony"/> is already 75 KB of six
    /// tabs, and a seventh does not need to be read by anyone editing the other six.
    /// </summary>
    public partial class MainTabWindow_Intercolony
    {
        private Vector2 employeeScroll;
        private Vector2 candidateScroll;

        private const float EmployeeRowHeight = 52f;
        private const float CandidateRowHeight = 32f;

        /// <summary>The longest term the player may commit to in Phase 16/17 terms (§36.3 is Phase 22).</summary>
        private const int MaxTermDays = 60;

        private enum WorkerColumn
        {
            Worker = 0,
            Skills = 1,
            Wage = 2,
            MinTerm = 3,
            Travel = 4,
            Source = 5
        }

        private WorkerColumn workerSortColumn = WorkerColumn.Wage;
        private bool workerSortDescending;

        private void DrawLabor(Rect inRect, IntercolonyWorldComponent state)
        {
            float y = inRect.y;

            // --- Employed workers ---
            List<EmploymentContract> live = new List<EmploymentContract>();
            foreach (EmploymentContract contract in state.Employments)
            {
                if (contract.IsOpen)
                {
                    live.Add(contract);
                }
            }

            live.Sort((a, b) => a.id.CompareTo(b.id));

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, 400f, 34f), "On the payroll");
            Text.Font = GameFont.Small;

            DrawPayrollSummary(new Rect(400f, y + 6f, inRect.width - 400f, 24f), live);
            y += 38f;

            // Height is content-driven up to a cap, so one employee does not leave a huge empty
            // panel and eight do not push the listing off screen.
            float employeeBlock = live.Count == 0
                ? 46f
                : Mathf.Min(live.Count * EmployeeRowHeight, EmployeeRowHeight * 4f);

            if (live.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(6f, y, inRect.width - 12f, 44f),
                    "Nobody hired. Workers below will come and work for a fixed term, then go home.");
                GUI.color = Color.white;
            }
            else
            {
                Rect outRect = new Rect(0f, y, inRect.width, employeeBlock);
                Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, live.Count * EmployeeRowHeight);
                Widgets.BeginScrollView(outRect, ref employeeScroll, viewRect);

                float rowY = 0f;
                for (int i = 0; i < live.Count; i++)
                {
                    DrawEmployeeRow(new Rect(0f, rowY, viewRect.width, EmployeeRowHeight), live[i], i);
                    rowY += EmployeeRowHeight;
                }

                Widgets.EndScrollView();
            }

            y += employeeBlock + 8f;

            y = DrawDebts(inRect, y, state);

            Widgets.DrawLineHorizontal(0f, y, inRect.width);
            y += 10f;

            // --- Available workers (§35.1) ---
            List<LaborCandidate> pool = new List<LaborCandidate>(LaborCandidateService.Refresh(state));

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, 400f, 34f), "Workers for hire");
            Text.Font = GameFont.Small;

            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            Widgets.Label(new Rect(400f, y + 8f, inRect.width - 400f, 24f),
                "Who is hiring changes when the market refreshes.");
            GUI.color = Color.white;
            y += 38f;

            if (pool.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(6f, y, inRect.width - 12f, 60f),
                    "No workers on offer.\n\n" +
                    "Settlements you can reach are not releasing labor at the moment. The listing " +
                    "changes with the market — check back after the next refresh.");
                GUI.color = Color.white;
                return;
            }

            SortCandidates(pool);

            Rect headerRect = new Rect(0f, y, inRect.width - 16f, HeaderHeight);
            DrawWorkerHeader(headerRect);
            y += HeaderHeight + 2f;

            Rect listRect = new Rect(0f, y, inRect.width, inRect.yMax - y);
            Rect listView = new Rect(0f, 0f, inRect.width - 16f, pool.Count * CandidateRowHeight);
            Widgets.BeginScrollView(listRect, ref candidateScroll, listView);

            float candidateY = 0f;
            for (int i = 0; i < pool.Count; i++)
            {
                DrawCandidateRow(new Rect(0f, candidateY, listView.width, CandidateRowHeight), pool[i], i, state);
                candidateY += CandidateRowHeight;
            }

            Widgets.EndScrollView();
        }

        /// <summary>
        /// Unpaid wages left behind by workers who have already gone (§39 step 6).
        ///
        /// Shown even though the employment is over, because that is the point of the record: the
        /// obligation outlives the worker, Phase 19 will price future labor off it, and a debt the
        /// player cannot see is one they cannot choose to make good.
        /// </summary>
        private float DrawDebts(Rect inRect, float y, IntercolonyWorldComponent state)
        {
            List<LaborDebt> unsettled = new List<LaborDebt>();
            foreach (LaborDebt debt in state.LaborDebts)
            {
                if (!debt.IsSettled)
                {
                    unsettled.Add(debt);
                }
            }

            if (unsettled.Count == 0)
            {
                return y + 2f;
            }

            unsettled.Sort((a, b) => b.amountOwed.CompareTo(a.amountOwed));

            int total = 0;
            foreach (LaborDebt debt in unsettled)
            {
                total += debt.amountOwed;
            }

            GUI.color = new Color(1f, 0.55f, 0.55f);
            Widgets.Label(new Rect(0f, y, inRect.width, 24f),
                $"Unpaid wages owed: {total} silver across {unsettled.Count} " +
                $"worker{(unsettled.Count == 1 ? "" : "s")} who have gone home.");
            GUI.color = Color.white;
            y += 26f;

            // Capped: the list is a prompt to settle up, not a ledger to browse. Two rows is
            // enough to show the shape of the problem without crowding out the hiring listing.
            int shown = Mathf.Min(unsettled.Count, 2);
            for (int i = 0; i < shown; i++)
            {
                LaborDebt debt = unsettled[i];
                Rect row = new Rect(0f, y, inRect.width, 26f);
                Widgets.DrawHighlightIfMouseover(row);

                GUI.color = new Color(1f, 1f, 1f, 0.75f);
                Widgets.Label(new Rect(6f, y + 2f, inRect.width - 130f, 22f),
                    $"{debt.amountOwed} silver to {debt.settlementName} for {debt.workerName} " +
                    $"— {debt.DaysOutstanding:F0} days outstanding");
                GUI.color = Color.white;

                Rect payRect = new Rect(inRect.width - 120f, y, 110f, 24f);
                if (Widgets.ButtonText(payRect, $"Settle {debt.amountOwed}"))
                {
                    if (!PayrollService.TrySettleDebt(debt, Find.CurrentMap, out string failReason))
                    {
                        Messages.Message(failReason, MessageTypeDefOf.RejectInput, historical: false);
                    }
                }

                y += 26f;
            }

            if (unsettled.Count > shown)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.5f);
                Widgets.Label(new Rect(6f, y, inRect.width, 22f),
                    $"...and {unsettled.Count - shown} more.");
                GUI.color = Color.white;
                y += 24f;
            }

            return y + 6f;
        }

        private static void DrawPayrollSummary(Rect rect, List<EmploymentContract> live)
        {
            if (live.Count == 0)
            {
                return;
            }

            int daily = 0;
            int paid = 0;
            int arrears = 0;
            foreach (EmploymentContract contract in live)
            {
                daily += contract.dailyWage;
                paid += contract.paidSilver;
                arrears += contract.arrearsSilver;
            }

            Text.Anchor = TextAnchor.MiddleRight;

            // §38's payroll screen, compressed to a line: what is owed, what is in the bank, and
            // the shortfall named as a shortfall. Arrears are shown in red because §38's whole
            // point is that running out of silver has to be visible before it bites.
            GUI.color = arrears > 0 ? new Color(1f, 0.55f, 0.55f) : new Color(1f, 1f, 1f, 0.75f);

            string text = $"{live.Count} hired   {daily} silver/day combined   {paid} paid so far";
            if (arrears > 0)
            {
                text += $"   —   {arrears} IN ARREARS";
            }

            Widgets.Label(rect, text);

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawEmployeeRow(Rect rect, EmploymentContract contract, int index)
        {
            if (index % 2 == 1)
            {
                Widgets.DrawLightHighlight(rect);
            }

            Widgets.DrawHighlightIfMouseover(rect);

            float actionWidth = 110f;
            float textWidth = rect.width - actionWidth - 12f;

            Widgets.Label(new Rect(rect.x + 6f, rect.y + 3f, textWidth, 22f),
                $"{contract.workerName}  —  {contract.workerSkills}");

            GUI.color = StatusColour(contract);
            Widgets.Label(new Rect(rect.x + 6f, rect.y + 25f, textWidth, 22f),
                $"{contract.settlementName} ({contract.factionName})   " +
                $"{contract.dailyWage}/day × {contract.termDays}d {contract.wageStructure.Label()}, " +
                $"{contract.paidSilver} paid   — {contract.StatusLine()}");
            GUI.color = Color.white;

            TooltipHandler.TipRegion(rect, () => EmployeeTooltip(contract), contract.id * 7919);

            // Click the row to jump to the worker, the way the colonist bar does. Only useful
            // once they are actually on a map.
            if (contract.pawn != null && contract.pawn.Spawned &&
                Widgets.ButtonInvisible(new Rect(rect.x, rect.y, textWidth, rect.height)))
            {
                CameraJumper.TryJumpAndSelect(contract.pawn);
            }

            // Paying what is owed takes priority over dismissing: it is the action that fixes
            // the situation, and §39's escalation is only playable if stopping it is easy to find.
            if (contract.arrearsSilver > 0)
            {
                Rect payRect = new Rect(rect.xMax - actionWidth * 2f - 8f, rect.y + 11f, actionWidth, 30f);
                if (Widgets.ButtonText(payRect, $"Pay {contract.arrearsSilver}"))
                {
                    if (!PayrollService.TryPayArrears(contract, Find.CurrentMap, out string failReason))
                    {
                        Messages.Message(failReason, MessageTypeDefOf.RejectInput, historical: false);
                    }
                }
            }

            Rect endRect = new Rect(rect.xMax - actionWidth - 4f, rect.y + 11f, actionWidth, 30f);
            if (Widgets.ButtonText(endRect, contract.status == EmploymentStatus.Travelling ? "Cancel" : "Dismiss"))
            {
                ConfirmDismiss(contract);
            }
        }

        private static Color StatusColour(EmploymentContract contract)
        {
            if (contract.status == EmploymentStatus.Travelling)
            {
                return new Color(0.6f, 0.9f, 1f);
            }

            if (contract.termLapsedNotified)
            {
                return Color.yellow;
            }

            return contract.DaysRemaining <= 2f
                ? new Color(1f, 0.85f, 0.5f)
                : new Color(1f, 1f, 1f, 0.7f);
        }

        private static string EmployeeTooltip(EmploymentContract contract)
        {
            string text =
                $"{contract.workerName} of {contract.factionName}\n" +
                $"Home settlement: {contract.settlementName}\n" +
                $"Skills at hire: {contract.workerSkills}\n\n" +
                $"Term: {contract.termDays} days at {contract.dailyWage} silver/day\n" +
                $"Paid in advance: {contract.paidSilver} silver\n";

            if (contract.status == EmploymentStatus.Travelling)
            {
                text += $"\nArrives in {Mathf.Max(0f, contract.DaysUntilArrival):0.#} days.\n" +
                        "Cancelling now does not return the wage — they are already on the road.";
            }
            else
            {
                text += $"\n{Mathf.Max(0f, contract.DaysRemaining):0.#} days left on the term.\n\n" +
                        "They can be given work priorities, drafted, assigned a bed and sent on " +
                        "caravans like a colonist — but they are not one. They belong to their own " +
                        "faction and go home when the term ends.";
            }

            return text;
        }

        private void ConfirmDismiss(EmploymentContract contract)
        {
            bool travelling = contract.status == EmploymentStatus.Travelling;

            string body = travelling
                ? $"Cancel {contract.workerName}'s contract before they arrive?\n\n" +
                  $"They are {Mathf.Max(0f, contract.DaysUntilArrival):0.#} days away and will turn back."
                : $"Send {contract.workerName} home {Mathf.Max(0f, contract.DaysRemaining):0.#} days early?\n\n" +
                  "They will stop working immediately and leave the map.";

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                body,
                () => EmploymentService.End(
                    contract,
                    EmploymentStatus.Dismissed,
                    travelling
                        ? $"{contract.workerName}'s contract was cancelled before they arrived"
                        : $"{contract.workerName} was dismissed early"),
                destructive: true));
        }

        private void DrawWorkerHeader(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.16f, 0.16f, 0.16f));

            float[] widths = CandidateColumnWidths(rect.width);
            float x = rect.x;

            for (int i = 0; i < widths.Length; i++)
            {
                WorkerColumn column = (WorkerColumn)i;
                Rect cell = new Rect(x + 6f, rect.y, widths[i] - 8f, rect.height);

                string label = WorkerColumnLabel(column);
                if (workerSortColumn == column)
                {
                    label += workerSortDescending ? " v" : " ^";
                }

                if (Widgets.ButtonInvisible(cell))
                {
                    if (workerSortColumn == column)
                    {
                        workerSortDescending = !workerSortDescending;
                    }
                    else
                    {
                        workerSortColumn = column;
                        workerSortDescending = column == WorkerColumn.Wage;
                    }
                }

                Widgets.DrawHighlightIfMouseover(cell);
                Widgets.Label(cell, label);
                x += widths[i];
            }
        }

        private static string WorkerColumnLabel(WorkerColumn column)
        {
            switch (column)
            {
                case WorkerColumn.Worker: return "Worker";
                case WorkerColumn.Skills: return "Best skills";
                case WorkerColumn.Wage: return "Silver/day";
                case WorkerColumn.MinTerm: return "Min term";
                case WorkerColumn.Travel: return "Arrives in";
                default: return "From";
            }
        }

        /// <summary>Column widths, proportional so the table fits whatever the window is.</summary>
        private static float[] CandidateColumnWidths(float total)
        {
            float action = 90f;
            float usable = total - action;
            return new[]
            {
                usable * 0.16f, // Worker
                usable * 0.34f, // Skills
                usable * 0.11f, // Wage
                usable * 0.11f, // Min term
                usable * 0.11f, // Travel
                usable * 0.17f  // Source
            };
        }

        private void SortCandidates(List<LaborCandidate> pool)
        {
            pool.Sort((a, b) =>
            {
                int result;
                switch (workerSortColumn)
                {
                    case WorkerColumn.Worker:
                        result = string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase);
                        break;
                    case WorkerColumn.Skills:
                        // Skills is a free-text summary, so sort by the thing the player is
                        // actually comparing: the highest level on offer.
                        result = TopSkillLevel(a).CompareTo(TopSkillLevel(b));
                        break;
                    case WorkerColumn.MinTerm:
                        result = a.minTermDays.CompareTo(b.minTermDays);
                        break;
                    case WorkerColumn.Travel:
                        result = a.travelDays.CompareTo(b.travelDays);
                        break;
                    case WorkerColumn.Source:
                        result = string.Compare(a.settlementName, b.settlementName,
                            System.StringComparison.OrdinalIgnoreCase);
                        break;
                    default:
                        result = a.dailyWage.CompareTo(b.dailyWage);
                        break;
                }

                if (result == 0)
                {
                    result = a.dailyWage.CompareTo(b.dailyWage);
                }

                return workerSortDescending ? -result : result;
            });
        }

        private static int TopSkillLevel(LaborCandidate candidate)
        {
            if (candidate.pawn?.skills == null)
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

        private void DrawCandidateRow(
            Rect rect, LaborCandidate candidate, int index, IntercolonyWorldComponent state)
        {
            if (index % 2 == 1)
            {
                Widgets.DrawLightHighlight(rect);
            }

            Widgets.DrawHighlightIfMouseover(rect);

            float[] widths = CandidateColumnWidths(rect.width);
            float x = rect.x;

            void Cell(int column, string text)
            {
                Widgets.Label(new Rect(x + 6f, rect.y + 5f, widths[column] - 8f, 22f), text);
                x += widths[column];
            }

            Cell((int)WorkerColumn.Worker, candidate.Name);
            Cell((int)WorkerColumn.Skills, candidate.SkillSummary());
            Cell((int)WorkerColumn.Wage, candidate.dailyWage.ToString());
            Cell((int)WorkerColumn.MinTerm, $"{candidate.minTermDays}d");
            Cell((int)WorkerColumn.Travel, $"{candidate.travelDays}d");
            Cell((int)WorkerColumn.Source, candidate.settlementName);

            TooltipHandler.TipRegion(rect, () => CandidateTooltip(candidate), candidate.GetHashCode());

            Rect hireRect = new Rect(rect.xMax - 86f, rect.y + 2f, 80f, 28f);
            if (Widgets.ButtonText(hireRect, "Hire"))
            {
                OpenHireDialog(state, candidate);
            }
        }

        private static string CandidateTooltip(LaborCandidate candidate)
        {
            string text =
                $"{candidate.Name} — {candidate.factionName}\n" +
                $"From {candidate.settlementName}, " +
                $"{(candidate.distanceTiles < 0f ? "unknown distance" : $"{candidate.distanceTiles:0} tiles")}\n\n";

            if (candidate.pawn?.skills != null)
            {
                List<SkillRecord> ranked = new List<SkillRecord>(candidate.pawn.skills.skills);
                ranked.RemoveAll(s => s.TotallyDisabled);
                ranked.Sort((a, b) => b.Level.CompareTo(a.Level));

                foreach (SkillRecord skill in ranked)
                {
                    string passion = skill.passion == Passion.Major ? "  (burning)"
                        : skill.passion == Passion.Minor ? "  (interested)" : "";
                    text += $"{skill.def.skillLabel.CapitalizeFirst()}: {skill.Level}{passion}\n";
                }

                text += "\n";
            }

            text += $"Asks {candidate.dailyWage} silver/day for their {candidate.minTermDays}-day minimum, " +
                    "paid in full up front.\n" +
                    "Longer terms cost less per day.\n" +
                    $"Takes {candidate.travelDays} days to reach the colony.";

            return text;
        }

        /// <summary>
        /// The hiring commitment. Term length lives here rather than in the tab, matching every
        /// other commitment in the mod: read the terms, choose the size, commit.
        /// </summary>
        private void OpenHireDialog(IntercolonyWorldComponent state, LaborCandidate candidate)
        {
            Map map = Find.CurrentMap;
            Settlement settlement = IntercolonyMarketAccess.FindSettlement(candidate.settlementId);
            SettlementEconomicProfile profile = settlement == null ? null : state.GetProfile(settlement);

            Find.WindowStack.Add(new Dialog_HireWorker(
                candidate, profile, map, MaxTermDays,
                (termDays, structure) =>
                {
                    EmploymentContract contract = EmploymentService.TryHire(
                        state, candidate, termDays, map, out string failReason, structure);

                    if (contract == null)
                    {
                        Messages.Message(failReason ?? "Could not hire.",
                            MessageTypeDefOf.RejectInput, historical: false);
                    }
                }));
        }
    }
}
