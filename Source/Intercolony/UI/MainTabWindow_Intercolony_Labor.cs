using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

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

        /// <summary>
        /// The longest term on offer. Delegated to <see cref="LaborCandidateService.MaxTermDays"/>
        /// rather than duplicated: it is a balance number the combat-clause economics depend on, and
        /// two copies would let the window offer a term the balance was never checked against.
        /// </summary>
        private const int MaxTermDays = LaborCandidateService.MaxTermDays;

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

        /// <summary>
        /// The three faces of the labor market (DESIGN.md §56, §35).
        ///
        /// Sub-tabs rather than one long page, because §35 describes two *complementary* hiring
        /// workflows and §56 lists seven sections in total — a single scroll would put the thing the
        /// player came for below three things they did not. Hire and Posts are the two workflows;
        /// Employees is what came of them.
        /// </summary>
        private enum LaborPage
        {
            Hire,
            Posts,
            Employees
        }

        private LaborPage laborPage = LaborPage.Hire;
        private Vector2 postingScroll;
        private readonly float[] candidateColumnWidths = new float[6];

        private const float LaborTabRowHeight = 30f;

        private void DrawLabor(Rect inRect, IntercolonyWorldComponent state)
        {
            float y = inRect.y;

            DrawLaborTabs(new Rect(0f, y, inRect.width, LaborTabRowHeight), state);
            y += LaborTabRowHeight + 8f;

            Rect body = new Rect(inRect.x, y, inRect.width, inRect.yMax - y);

            switch (laborPage)
            {
                case LaborPage.Posts:
                    DrawPostsPage(body, state);
                    break;
                case LaborPage.Employees:
                    DrawEmployeesPage(body, state);
                    break;
                default:
                    DrawHirePage(body, state);
                    break;
            }
        }

        /// <summary>
        /// The sub-tab strip. Each label carries its own count, so the player can see there are
        /// applicants waiting without opening the page to find out.
        /// </summary>
        private void DrawLaborTabs(Rect rect, IntercolonyWorldComponent state)
        {
            int employees = 0;
            foreach (EmploymentContract contract in state.Employments)
            {
                if (contract.IsOpen || contract.IsLeavingUnderSafePassage)
                {
                    employees++;
                }
            }

            int posts = state.OpenPostingCount;
            int applicants = state.WaitingApplicantCount;

            string postLabel = applicants > 0
                ? $"Posts ({posts}) — {applicants} waiting"
                : posts > 0 ? $"Posts ({posts})" : "Posts";

            string employeeLabel = employees > 0 ? $"Employees ({employees})" : "Employees";
            string[] labels = { "Hire", postLabel, employeeLabel };
            float[] widths = MeasureTabWidths(rect.width, labels, 4f);

            float x = rect.x;
            DrawLaborTab(new Rect(x, rect.y, widths[0], rect.height), LaborPage.Hire, labels[0],
                false, state);
            x += widths[0] + 4f;
            DrawLaborTab(new Rect(x, rect.y, widths[1], rect.height), LaborPage.Posts, labels[1],
                applicants > 0, state);
            x += widths[1] + 4f;
            DrawLaborTab(new Rect(x, rect.y, widths[2], rect.height), LaborPage.Employees, labels[2],
                false, state);

            Widgets.DrawLineHorizontal(rect.x, rect.yMax, rect.width);
        }

        private void DrawLaborTab(
            Rect rect, LaborPage page, string label, bool attention, IntercolonyWorldComponent state)
        {
            bool selected = laborPage == page;

            if (selected)
            {
                Widgets.DrawHighlightSelected(rect);
            }
            else
            {
                Widgets.DrawHighlightIfMouseover(rect);
            }

            // Attention colour only when something is actually waiting on the player. A tab that is
            // always highlighted stops meaning anything.
            GUI.color = attention && !selected ? new Color(0.65f, 0.95f, 0.65f) : Color.white;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, label);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            if (!selected && Widgets.ButtonInvisible(rect))
            {
                laborPage = page;
                SelectTab(Tab.Labor, state);
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }
        }

        /// <summary>§35.1 — who is on the market right now, at their asking price.</summary>
        private void DrawHirePage(Rect inRect, IntercolonyWorldComponent state)
        {
            float y = inRect.y;

            List<LaborCandidate> pool = new List<LaborCandidate>(LaborCandidateService.Refresh(state));

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, 400f, 34f), "Workers for hire");
            Text.Font = GameFont.Small;

            DrawEmployerStanding(new Rect(360f, y + 4f, inRect.width - 360f, 28f), state);
            y += 38f;

            if (pool.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(6f, y, inRect.width - 12f, 76f),
                    "No workers on offer.\n\n" +
                    "Settlements you can reach are not releasing labor at the moment. The listing " +
                    "changes with the market — check back after the next refresh, or post a job and " +
                    "let people come to you.");
                GUI.color = Color.white;
                return;
            }

            SortCandidates(pool);

            Rect headerRect = new Rect(0f, y, inRect.width - 16f, HeaderHeight);
            SetCandidateColumnWidths(headerRect.width, candidateColumnWidths);
            DrawWorkerHeader(headerRect, candidateColumnWidths);
            y += HeaderHeight + 2f;

            Rect listRect = new Rect(0f, y, inRect.width, inRect.yMax - y);
            Rect listView = new Rect(0f, 0f, inRect.width - 16f, pool.Count * CandidateRowHeight);
            BeginPageScrollView(listRect, ref candidateScroll, listView);

            float candidateY = 0f;
            for (int i = 0; i < pool.Count; i++)
            {
                DrawCandidateRow(new Rect(0f, candidateY, listView.width, CandidateRowHeight), pool[i], i,
                    state, candidateColumnWidths);
                candidateY += CandidateRowHeight;
            }

            EndPageScrollView();
        }

        private void DrawEmployeesPage(Rect inRect, IntercolonyWorldComponent state)
        {
            float y = inRect.y;

            // --- Employed workers ---
            List<EmploymentContract> live = new List<EmploymentContract>();
            foreach (EmploymentContract contract in state.Employments)
            {
                // A worker released by a war is still on the map and still the player's problem
                // until they are clear of it, so they stay on this list even though the employment
                // itself has ended (§88).
                if (contract.IsOpen || contract.IsLeavingUnderSafePassage)
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
            if (live.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(6f, y, inRect.width - 12f, 44f),
                    "Nobody hired. Find someone under Hire, or post a job and let them come to you.");
                GUI.color = Color.white;
                y += 48f;
            }
            else
            {
                // The page is the employees' now, so the list gets the room rather than a quarter
                // of it — the four-row cap existed because two other sections were below it.
                float employeeBlock = Mathf.Min(live.Count * EmployeeRowHeight, inRect.height - 140f);

                Rect outRect = new Rect(0f, y, inRect.width, employeeBlock);
                Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, live.Count * EmployeeRowHeight);
                BeginPageScrollView(outRect, ref employeeScroll, viewRect);

                float rowY = 0f;
                for (int i = 0; i < live.Count; i++)
                {
                    DrawEmployeeRow(new Rect(0f, rowY, viewRect.width, EmployeeRowHeight), live[i], i);
                    rowY += EmployeeRowHeight;
                }

                EndPageScrollView();
                y += employeeBlock + 8f;
            }

            DrawDebts(inRect, y, state);
        }

        /// <summary>
        /// §35.2 — what the colony is advertising, and who has answered.
        ///
        /// Applicants are drawn *under* their posting rather than in a list of their own, because
        /// an applicant only means anything in the context of what they applied to: the same worker
        /// is a bargain against one offer and an overpayment against another.
        /// </summary>
        private void DrawPostsPage(Rect inRect, IntercolonyWorldComponent state)
        {
            float y = inRect.y;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, 400f, 34f), "Job posts");
            Text.Font = GameFont.Small;

            if (Widgets.ButtonText(new Rect(inRect.width - 160f, y + 2f, 150f, 30f), "New posting"))
            {
                Find.WindowStack.Add(new Dialog_CreateJobPosting(state,
                    (skill, minLevel, positions, termDays, wage, structure, clause, lifespan) =>
                    {
                        if (JobPostingService.TryPost(state, skill, minLevel, positions, termDays,
                                wage, structure, clause, lifespan, out string failReason) == null)
                        {
                            Messages.Message(failReason ?? "Could not post.",
                                MessageTypeDefOf.RejectInput, historical: false);
                        }
                    }));
            }

            y += 38f;

            List<JobPosting> live = new List<JobPosting>();
            foreach (JobPosting posting in state.Postings)
            {
                if (posting.IsOpen)
                {
                    live.Add(posting);
                }
            }

            live.Sort((a, b) => a.id.CompareTo(b.id));

            if (live.Count == 0)
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(6f, y, inRect.width - 12f, 120f),
                    "Nothing advertised.\n\n" +
                    "A posting says what you need and what you will pay. Workers who can do the job, " +
                    "and who will work for that, apply as the market brings them past — including " +
                    "people who are not advertising themselves and would never appear under Hire.\n\n" +
                    "It costs nothing to leave one up. There are only so many workers in the world, " +
                    "so posting the same job twice gets you the same people.");
                GUI.color = Color.white;
                return;
            }

            Rect outRect = new Rect(0f, y, inRect.width, inRect.yMax - y);
            float height = 0f;
            foreach (JobPosting posting in live)
            {
                height += PostingBlockHeight(posting);
            }

            Rect viewRect = new Rect(0f, 0f, inRect.width - 16f, height);
            System.Action pendingAction = null;
            BeginPageScrollView(outRect, ref postingScroll, viewRect);

            try
            {
                float rowY = 0f;
                for (int i = 0; i < live.Count; i++)
                {
                    float blockHeight = PostingBlockHeight(live[i]);
                    DrawPostingBlock(new Rect(0f, rowY, viewRect.width, blockHeight), live[i],
                        state, i, ref pendingAction);
                    rowY += blockHeight;
                }
            }
            finally
            {
                EndPageScrollView();
            }

            // Posting actions can change the applicant list that supplied both the measured height
            // and these rows. Let the draw pass finish against one stable collection first.
            pendingAction?.Invoke();
        }

        private const float PostingHeaderHeight = 54f;
        private const float ApplicantRowHeight = 46f;

        private static float PostingBlockHeight(JobPosting posting)
        {
            return PostingHeaderHeight + posting.Applicants.Count * ApplicantRowHeight + 8f;
        }

        private void DrawPostingBlock(
            Rect rect, JobPosting posting, IntercolonyWorldComponent state, int index,
            ref System.Action pendingAction)
        {
            if (index % 2 == 1)
            {
                Widgets.DrawLightHighlight(new Rect(rect.x, rect.y, rect.width, PostingHeaderHeight));
            }

            Widgets.Label(new Rect(rect.x + 6f, rect.y + 3f, rect.width - 280f, 22f), posting.Headline());

            GUI.color = posting.Applicants.Count > 0
                ? new Color(0.65f, 0.95f, 0.65f)
                : new Color(1f, 1f, 1f, 0.65f);
            Widgets.Label(new Rect(rect.x + 6f, rect.y + 25f, rect.width - 280f, 22f),
                posting.StatusLine());
            GUI.color = Color.white;

            Rect tipRect = new Rect(rect.x, rect.y, rect.width, PostingHeaderHeight);
            if (ShouldBuildTooltip(tipRect))
            {
                TooltipHandler.TipRegion(
                    tipRect, new TipSignal(PostingTooltip(posting, state), posting.id * 5701));
            }

            Rect withdrawRect = new Rect(rect.xMax - 120f, rect.y + 12f, 110f, 30f);
            if (Widgets.ButtonText(withdrawRect, "Withdraw"))
            {
                pendingAction = () => Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        $"Take down this posting?\n\n{posting.Headline()}\n\n" +
                        (posting.Applicants.Count > 0
                            ? $"{posting.Applicants.Count} applicant(s) are waiting on it and will go home."
                            : "Nobody is waiting on it."),
                        () => JobPostingService.Withdraw(posting),
                        destructive: true));
            }

            float y = rect.y + PostingHeaderHeight;
            for (int i = posting.Applicants.Count - 1; i >= 0; i--)
            {
                DrawApplicantRow(new Rect(rect.x, y, rect.width, ApplicantRowHeight),
                    posting, posting.Applicants[i], state, ref pendingAction);
                y += ApplicantRowHeight;
            }
        }

        private void DrawApplicantRow(
            Rect rect, JobPosting posting, JobApplicant applicant, IntercolonyWorldComponent state,
            ref System.Action pendingAction)
        {
            Widgets.DrawHighlightIfMouseover(rect);

            float actionWidth = 100f;
            float textWidth = rect.width - actionWidth * 2f - 20f;

            Widgets.Label(new Rect(rect.x + 24f, rect.y + 2f, textWidth, 22f),
                $"{applicant.Name}  —  {applicant.SkillSummary(4)}");

            // The bargain is the useful number: they accepted your wage, but what were they worth?
            int bargain = applicant.Bargain(posting.wageOffered);
            string value = bargain > 0
                ? $"asks {applicant.openMarketAsk}/day on the open market — you are paying " +
                  $"{bargain} over"
                : $"asks {applicant.openMarketAsk}/day on the open market — your offer matches";

            GUI.color = new Color(1f, 1f, 1f, 0.65f);
            Widgets.Label(new Rect(rect.x + 24f, rect.y + 22f, textWidth, 22f),
                $"{applicant.settlementName} ({applicant.factionName}), {applicant.travelDays}d away — " +
                value);
            GUI.color = Color.white;

            Rect hireRect = new Rect(rect.xMax - actionWidth * 2f - 14f, rect.y + 8f, actionWidth, 30f);
            if (Widgets.ButtonText(hireRect, "Take on"))
            {
                pendingAction = () =>
                {
                    if (JobPostingService.TryAccept(state, posting, applicant, Find.CurrentMap,
                            out string failReason) == null)
                    {
                        Messages.Message(failReason ?? "Could not hire.",
                            MessageTypeDefOf.RejectInput, historical: false);
                    }
                };
            }

            Rect rejectRect = new Rect(rect.xMax - actionWidth - 4f, rect.y + 8f, actionWidth, 30f);
            if (Widgets.ButtonText(rejectRect, "Turn away"))
            {
                pendingAction = () => JobPostingService.Reject(posting, applicant);
            }
        }

        private static string PostingTooltip(JobPosting posting, IntercolonyWorldComponent state)
        {
            string text =
                $"{posting.Headline()}\n\n" +
                $"Posted {posting.DaysPosted:0.#} days ago, " +
                $"{Mathf.Max(0f, posting.DaysUntilExpiry):0.#} days left.\n" +
                $"{posting.hired} of {posting.positions} filled.\n\n" +
                $"If every position is filled and served out: {posting.TotalCommitment} silver.\n" +
                $"Compensation if one of them dies: " +
                $"{posting.wageOffered * posting.combatClause.DeathCompensationDays()} silver each.";

            if (posting.Applicants.Count == 0 && posting.emptyCycles > 0)
            {
                text += "\n\n" + JobPostingService.ExplainSilence(
                    state, posting, EmployerReputationService.ScoreFor(state));
            }

            return text;
        }

        /// <summary>
        /// Standing as an employer (§40), shown against the hiring listing because that is where
        /// it acts: the score sets wages, how many settlements are willing to send anyone, and
        /// whether the colony gets first pick of the workers it does see.
        /// </summary>
        private static void DrawEmployerStanding(Rect rect, IntercolonyWorldComponent state)
        {
            EmployerReputation rep = state.EmployerStanding;
            if (rep == null)
            {
                return;
            }

            float wageFactor = EmployerReputationService.WageFactor(rep.Score);
            int wagePercent = Mathf.RoundToInt((wageFactor - 1f) * 100f);

            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = TierColour(rep.Tier);

            string effect = wagePercent == 0
                ? "wages at the going rate"
                : wagePercent > 0
                    ? $"wages +{wagePercent}%"
                    : $"wages {wagePercent}%";

            Widgets.Label(rect, $"{rep.TierLabel()} — {rep.ScoreDisplay}/100, {effect}");

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            int availabilityPercent =
                Mathf.RoundToInt(EmployerReputationService.AvailabilityFactor(rep.Score) * 100f);

            if (ShouldBuildTooltip(rect))
            {
                string tooltip = rep.Summary() + "\n\n" +
                    $"Workers ask {(wagePercent >= 0 ? "+" : "")}{wagePercent}% against the base rate.\n" +
                    $"Roughly {availabilityPercent}% of the labor a neutral employer would see is on offer.\n" +
                    QualityNote(rep.Score) + "\n\n" +
                    "Paying on time and seeing contracts through raises this. Missed payroll, workers " +
                    "walking out and deaths on the job lower it. A settlement still owed wages sends " +
                    "nobody at all until the debt is settled.\n\n" +
                    "Who is hiring also changes when the market refreshes.";
                TooltipHandler.TipRegion(rect, new TipSignal(tooltip, rect.GetHashCode()));
            }
        }

        private static string QualityNote(float score)
        {
            int bias = EmployerReputationService.CandidateQualityBias(score);
            if (bias > 0)
            {
                return "Good workers come to you first — you see the better of two candidates.";
            }

            return bias < 0
                ? "Only the workers nobody else wants will consider you."
                : "No pick of the crop either way.";
        }

        private static Color TierColour(EmployerTier tier)
        {
            switch (tier)
            {
                case EmployerTier.Exploitative: return new Color(1f, 0.45f, 0.45f);
                case EmployerTier.Poor: return new Color(1f, 0.7f, 0.45f);
                case EmployerTier.Good: return new Color(0.7f, 0.95f, 0.7f);
                case EmployerTier.SoughtAfter: return new Color(0.55f, 0.95f, 0.85f);
                default: return new Color(1f, 1f, 1f, 0.7f);
            }
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

            int compensation = 0;
            foreach (LaborDebt debt in unsettled)
            {
                if (debt.kind == LaborDebtKind.Compensation)
                {
                    compensation += debt.amountOwed;
                }
            }

            GUI.color = new Color(1f, 0.55f, 0.55f);
            Widgets.Label(new Rect(0f, y, inRect.width, 24f),
                $"Owed to settlements: {total} silver across {unsettled.Count} " +
                $"worker{(unsettled.Count == 1 ? "" : "s")} who have gone home" +
                (compensation > 0 ? $" — {compensation} of it compensation for the dead and maimed." : "."));
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
                    $"{debt.amountOwed} silver to {debt.settlementName} — {debt.KindLabel()} for " +
                    $"{debt.workerName}, {debt.DaysOutstanding:F0} days outstanding");
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

        /// <summary>
        /// One employee row's geometry, computed in a single place so the clickable text region and
        /// the action buttons cannot disagree about where they are.
        ///
        /// **This type exists because they did disagree, and it cost a play-test.** The text width
        /// reserved room for *one* button, but several row states draw two — pay + dismiss, renew +
        /// let go, keep + not now. The row's invisible click-to-jump region spans the text width, is
        /// drawn before the buttons, and therefore took the mouse-up for anything underneath it. The
        /// left-hand button was 106 of its 110 pixels under that region and was simply dead; the
        /// right-hand one sat clear and worked perfectly, which made it look like a problem with one
        /// specific button rather than with the layout.
        ///
        /// Nothing threw, nothing logged, and the only visible clue was the clause label clipping
        /// behind the button — the same over-wide text width showing itself in a way that could be
        /// seen. Deriving all of it from one place is what stops the next state that wants two
        /// buttons from reintroducing it.
        /// </summary>
        private struct EmployeeRowLayout
        {
            public const float ActionWidth = 110f;

            /// <summary>Width available for labels *and* the click-to-jump region.</summary>
            public float textWidth;

            /// <summary>Where a second-from-right button goes, when the row draws two.</summary>
            public Rect leftAction;

            /// <summary>Where the rightmost button goes.</summary>
            public Rect rightAction;

            public static EmployeeRowLayout For(Rect rect)
            {
                EmployeeRowLayout layout = new EmployeeRowLayout
                {
                    rightAction = new Rect(rect.xMax - ActionWidth - 4f, rect.y + 11f, ActionWidth, 30f),
                    leftAction = new Rect(rect.xMax - ActionWidth * 2f - 8f, rect.y + 11f, ActionWidth, 30f)
                };

                // Always reserved for two, even on rows that draw one. A row that reserved space
                // conditionally would put the click region back under a button the moment a new
                // state added a second one.
                layout.textWidth = layout.leftAction.x - rect.x - 6f;
                return layout;
            }
        }

        private void DrawEmployeeRow(Rect rect, EmploymentContract contract, int index)
        {
            if (index % 2 == 1)
            {
                Widgets.DrawLightHighlight(rect);
            }

            Widgets.DrawHighlightIfMouseover(rect);

            EmployeeRowLayout layout = EmployeeRowLayout.For(rect);
            float textWidth = layout.textWidth;

            Widgets.Label(new Rect(rect.x + 6f, rect.y + 3f, textWidth, 22f),
                $"{contract.workerName}  —  {contract.workerSkills}");

            // The clause goes on the name line, not buried in the detail line: it is the thing the
            // player needs to know before they hit the draft key, and a tooltip is too late.
            string clause = contract.combatClause.LabelCap();
            if (contract.clauseBreaches > 0)
            {
                clause += $", {contract.clauseBreaches} BREACHED";
            }

            GUI.color = contract.clauseBreaches > 0 ? new Color(1f, 0.55f, 0.55f) : new Color(1f, 1f, 1f, 0.6f);
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(new Rect(rect.x + 6f, rect.y + 3f, textWidth - 6f, 22f), clause);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            GUI.color = StatusColour(contract);
            Widgets.Label(new Rect(rect.x + 6f, rect.y + 25f, textWidth, 22f),
                $"{contract.settlementName} ({contract.factionName})   " +
                $"{contract.dailyWage}/day × {contract.TermLabel} {contract.wageStructure.Label()}, " +
                $"{contract.paidSilver} paid   — {contract.StatusLine()}");
            GUI.color = Color.white;

            if (ShouldBuildTooltip(rect))
            {
                TooltipHandler.TipRegion(
                    rect, new TipSignal(EmployeeTooltip(contract), contract.id * 7919));
            }

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
                Rect payRect = layout.leftAction;
                if (Widgets.ButtonText(payRect, $"Pay {contract.arrearsSilver}"))
                {
                    if (!PayrollService.TryPayArrears(contract, Find.CurrentMap, out string failReason))
                    {
                        Messages.Message(failReason, MessageTypeDefOf.RejectInput, historical: false);
                    }
                }
            }

            // A severed worker cannot be dismissed — the employment is already over and they are
            // walking out. Showing a live button that does nothing would be worse than no button.
            if (contract.status == EmploymentStatus.Severed)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.5f);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(layout.rightAction, "leaving");
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }

            // An offer to stay for good outranks everything, including renewal: it is the rarest
            // thing this tab ever shows and it is a decision the player has earned (§44).
            if (TransitionService.HasLiveOffer(contract))
            {
                Rect settleRect = layout.leftAction;
                if (Widgets.ButtonText(settleRect, "Keep them"))
                {
                    OpenTransitionDialog(contract);
                }

                Rect laterRect = layout.rightAction;
                if (Widgets.ButtonText(laterRect, "Not now"))
                {
                    TransitionService.Decline(contract);
                }

                return;
            }

            // A live renewal offer outranks the dismiss button: it expires on its own, and it is
            // the thing the player is being asked about (§115).
            if (RenewalService.HasLiveOffer(contract))
            {
                Rect renewRect = layout.leftAction;
                if (Widgets.ButtonText(renewRect, $"Renew {contract.renewalWage}"))
                {
                    if (!RenewalService.Accept(contract, out string failReason))
                    {
                        Messages.Message(failReason, MessageTypeDefOf.RejectInput, historical: false);
                    }
                }

                Rect declineRect = layout.rightAction;
                if (Widgets.ButtonText(declineRect, "Let go"))
                {
                    RenewalService.Decline(contract);
                }

                return;
            }

            Rect endRect = layout.rightAction;
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

            if (contract.status == EmploymentStatus.Severed)
            {
                return new Color(1f, 0.7f, 0.4f);
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
                $"Paid in advance: {contract.paidSilver} silver\n\n" +

                // §42 and §43 in the tooltip, together, because they are one decision: what you may
                // ask of them, and what it costs if it goes wrong.
                $"Clause: {contract.combatClause.LabelCap()}\n" +
                $"{contract.combatClause.Explain()}\n" +
                $"Compensation on death: {CompensationService.DeathCompensation(contract)} silver\n";

            if (contract.combatIncidents > 0)
            {
                text += $"Fights drafted into: {contract.combatIncidents}";
                text += contract.clauseBreaches > 0
                    ? $", {contract.clauseBreaches} of them outside the clause\n"
                    : ", all within the clause\n";
            }

            if (contract.status == EmploymentStatus.Active)
            {
                // §116 wants this rare, which makes it worth showing how far off it is — a rare
                // outcome nobody can see approaching is indistinguishable from one that does not
                // exist.
                text += TransitionService.IsEligible(
                    IntercolonyWorldComponent.Current, contract, out string blocker)
                    ? "\nThey have grown attached and would stay permanently.\n"
                    : $"\nSettling here permanently: {blocker}\n";
            }

            if (contract.compensationPaid > 0)
            {
                text += $"Compensation already paid: {contract.compensationPaid} silver\n";
            }

            if (contract.status == EmploymentStatus.Travelling)
            {
                text += $"\nArrives in {Mathf.Max(0f, contract.DaysUntilArrival):0.#} days.\n" +
                        "Cancelling now does not return the wage — they are already on the road.";
            }
            else if (contract.status == EmploymentStatus.Severed)
            {
                text += $"\n{contract.factionName} is at war with you, so this contract is over.\n\n" +
                        "They are walking out in no faction at all and will not fight. Nothing in " +
                        "the colony will shoot them unless you order it — and if they die on the " +
                        "way out, compensation is owed in full.";

                if (contract.safePassageEndTick > 0)
                {
                    float daysLeft = (contract.safePassageEndTick - GenTicks.TicksGame) /
                                     (float)GenDate.TicksPerDay;
                    text += $"\n\nSafe passage lasts another {Mathf.Max(0f, daysLeft):0.#} days. " +
                            "After that they rejoin their own people, here.";
                }
            }
            else
            {
                text += $"\n{contract.RemainingLabel} on the term.\n\n" +
                        "They can be given work priorities, assigned a bed and sent on caravans " +
                        "like a colonist — but they are not one. They belong to their own faction " +
                        "and go home when the term ends.";

                if (!contract.CombatUsePermittedNow)
                {
                    text += "\n\nDrafting them into a fight breaches their contract.";
                }
            }

            return text;
        }

        private void ConfirmDismiss(EmploymentContract contract)
        {
            // §36.4: an open-ended worker is owed notice, and the player picks how to settle it.
            // Three routes rather than a yes/no, because the interesting decision is whether the
            // colony wants the remaining labour, the silver, or neither.
            if (contract.IsOpenEnded && contract.status == EmploymentStatus.Active &&
                !contract.ServingNotice)
            {
                ConfirmOpenEndedDismiss(contract);
                return;
            }

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

        /// <summary>
        /// Ending an open-ended engagement (§36.4). Work the notice, pay it off, or skip it and
        /// wear the consequence — the last is deliberately available, because the rules are meant
        /// to price the decision rather than remove it.
        /// </summary>
        private void ConfirmOpenEndedDismiss(EmploymentContract contract)
        {
            int days = RenewalService.NoticeDays(contract);
            int inLieu = RenewalService.PayInLieu(contract);

            string body =
                $"{contract.workerName} is on an open-ended contract and has served " +
                $"{contract.TenureDays:0} days.\n\n" +
                $"Notice owed: {days} days ({inLieu} silver).";

            Find.WindowStack.Add(new Dialog_MessageBox(
                body,
                $"Work out the {days} days",
                () => RenewalService.GiveNotice(contract),
                $"Pay {inLieu} and end it now",
                () =>
                {
                    if (!RenewalService.TryPayInLieu(contract, Find.CurrentMap, out string failReason))
                    {
                        Messages.Message(failReason, MessageTypeDefOf.RejectInput, historical: false);
                    }
                })
            {
                buttonCText = "Dismiss with no notice",
                buttonCAction = () => Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    $"Send {contract.workerName} home today, owing them {days} days' notice?\n\n" +
                    "Word gets around. Your standing as an employer will suffer, and so will " +
                    $"{contract.factionName}'s opinion of you.",
                    () => RenewalService.DismissWithoutNotice(contract),
                    destructive: true))
            });
        }

        /// <summary>
        /// §44's decision, with all three of its routes on one screen.
        ///
        /// The negotiated figure and the unnegotiated one are shown together rather than the player
        /// being told only the final number — seeing that a Social 14 colonist saved 2,800 silver is
        /// the whole reward for having one.
        /// </summary>
        private void OpenTransitionDialog(EmploymentContract contract)
        {
            IntercolonyWorldComponent state = IntercolonyWorldComponent.Current;
            Map map = Find.CurrentMap;

            int asking = TransitionService.ReleaseFee(state, contract);
            Pawn negotiator = TransitionService.BestNegotiator(map);
            int fee = TransitionService.NegotiatedFee(state, contract, negotiator);

            string body =
                $"{contract.workerName} has worked here {contract.TenureDays:0} days and wants to stay " +
                $"for good.\n\n" +
                $"{contract.factionName} asks {asking} silver to release them.\n" +
                (negotiator != null && fee < asking
                    ? $"{negotiator.LabelShortCap} (Social " +
                      $"{negotiator.skills.GetSkill(SkillDefOf.Social).Level}) can talk them down to " +
                      $"{fee} — a saving of {asking - fee}.\n"
                    : "Nobody here can negotiate the price down.\n") +
                $"\nIn storage: {PurchaseOrderService.CountColonySilver(map)} silver.";

            Find.WindowStack.Add(new Dialog_MessageBox(
                body,
                $"Pay {fee}",
                () =>
                {
                    if (!TransitionService.TrySettle(state, contract, negotiator, map,
                            out string failReason))
                    {
                        Messages.Message(failReason, MessageTypeDefOf.RejectInput, historical: false);
                    }
                },
                "Cancel",
                null)
            {
                buttonCText = "Keep them without paying",
                buttonCAction = () => Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    $"Keep {contract.workerName} without settling with {contract.factionName}?\n\n" +
                    "They will call it theft. Expect their goodwill to collapse, and expect war to " +
                    "be a real possibility — along with everything you have booked with them.",
                    () => TransitionService.Defect(state, contract),
                    destructive: true))
            });
        }

        private void DrawWorkerHeader(Rect rect, float[] widths)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.16f, 0.16f, 0.16f));

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
        private static void SetCandidateColumnWidths(float total, float[] widths)
        {
            float action = 90f;
            float usable = total - action;
            widths[(int)WorkerColumn.Worker] = usable * 0.16f;
            widths[(int)WorkerColumn.Skills] = usable * 0.34f;
            widths[(int)WorkerColumn.Wage] = usable * 0.11f;
            widths[(int)WorkerColumn.MinTerm] = usable * 0.11f;
            widths[(int)WorkerColumn.Travel] = usable * 0.11f;
            widths[(int)WorkerColumn.Source] = usable * 0.17f;
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
            Rect rect, LaborCandidate candidate, int index, IntercolonyWorldComponent state, float[] widths)
        {
            if (index % 2 == 1)
            {
                Widgets.DrawLightHighlight(rect);
            }

            Widgets.DrawHighlightIfMouseover(rect);

            float x = rect.x;

            void Cell(int column, string text)
            {
                Widgets.Label(new Rect(x + 6f, rect.y + 5f, widths[column] - 8f, 22f), text);
                x += widths[column];
            }

            Cell((int)WorkerColumn.Worker, candidate.Name);
            Cell((int)WorkerColumn.Skills, candidate.SkillSummary());
            // "from" because the listed rate is the civilian rate (§42's cheapest clause) and the
            // hiring dialog can only price it upwards. A bare number here would read as the price.
            Cell((int)WorkerColumn.Wage, $"from {candidate.dailyWage}");
            Cell((int)WorkerColumn.MinTerm, $"{candidate.minTermDays}d");
            Cell((int)WorkerColumn.Travel, $"{candidate.travelDays}d");
            Cell((int)WorkerColumn.Source, candidate.settlementName);

            if (ShouldBuildTooltip(rect))
            {
                TooltipHandler.TipRegion(
                    rect, new TipSignal(CandidateTooltip(candidate), candidate.GetHashCode()));
            }

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

            text += $"Asks {candidate.dailyWage} silver/day for their {candidate.minTermDays}-day minimum " +
                    "as a civilian.\n" +
                    "Longer terms cost less per day. Agreeing to fight costs more:\n" +
                    $"  armed employee {Mathf.RoundToInt(candidate.dailyWage * CombatClause.Armed.WageFactor())}/day, " +
                    $"security contractor {Mathf.RoundToInt(candidate.dailyWage * CombatClause.Security.WageFactor())}/day.\n" +
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
                (termDays, structure, clause) =>
                {
                    EmploymentContract contract = EmploymentService.TryHire(
                        state, candidate, termDays, map, out string failReason, structure, clause);

                    if (contract == null)
                    {
                        Messages.Message(failReason ?? "Could not hire.",
                            MessageTypeDefOf.RejectInput, historical: false);
                    }
                }));
        }
    }
}
