using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Intercolony
{
    /// <summary>
    /// The business dashboard (DESIGN.md §117, §45, §75).
    ///
    /// §117's brief is half goal and half warning: *"Help the player understand the business without
    /// turning the mod into accounting software."* So this page is deliberately two short blocks and
    /// a list, not a spreadsheet. Every figure earns its place by being the other half of a question
    /// §45 asks — revenue next to what the workforce costs, contract income next to what buying the
    /// goods instead would cost, silver on hand next to how long payroll is covered for.
    ///
    /// It is the leftmost tab because by this point in the mod's life "how is the business doing" is
    /// the question a player opens the window to answer.
    /// </summary>
    public partial class MainTabWindow_Intercolony
    {
        private int businessWindowDays = BusinessReportService.QuadrumDays;
        private Vector2 businessScroll;

        /// <summary>
        /// Height of the content as actually drawn last pass.
        ///
        /// Measured rather than predicted. The first version computed it from a formula — so many
        /// pixels per block, so many per contract — and the formula was simply wrong, which handed
        /// the scroll view a viewport taller than anything in it: the page scrolled with nothing
        /// below to reach, and the thumb sat at the bottom of a track it had no reason to fill.
        /// Every draw method returns its final y, so the real number is available for free and
        /// cannot drift out of step with the layout the way a constant does.
        /// </summary>
        private float businessContentHeight = 400f;

        private const float LineHeight = 24f;
        private const float CashFlowColumnGap = 8f;
        private const string CashFlowHeadingTooltip =
            "This table counts commitments already made: open sales orders, agreement cycles falling due, and scheduled payroll. " +
            "It does not predict spot sales or opportunities you have not accepted.";
        private const string CashFlowDayTooltip =
            "Each bucket is a rolling 24-hour window from now, not a calendar day.";

        public override void PostOpen()
        {
            base.PostOpen();
            CashFlowForecast.Invalidate();
        }

        private void DrawBusiness(Rect inRect, IntercolonyWorldComponent state)
        {
            Rect outRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height);

            // A viewport no taller than the panel means no scrollbar and no scrolling when the
            // content fits, which is the common case for this page.
            float viewHeight = Mathf.Max(businessContentHeight, outRect.height);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 20f, viewHeight);

            BeginPageScrollView(outRect, ref businessScroll, viewRect);

            if (debugThrowOnNextBusinessDraw)
            {
                // Deliberately throw after opening the scroll view so the debug action proves the
                // guard repairs GUI state as well as replacing the page and suppressing repeats.
                debugThrowOnNextBusinessDraw = false;
                throw new System.InvalidOperationException(
                    "Deliberate Intercolony Business page draw failure.");
            }

            float y = 0f;
            y = DrawCashPosition(viewRect, y, state);
            y += 12f;
            CashFlowReport cashFlow = CashFlowForecast.Current(state);
            y = DrawCashFlowForecast(viewRect, y, cashFlow);
            y += 12f;
            y = DrawBrandSummary(viewRect, y, state);
            y += 12f;
            y = DrawPeriodReport(viewRect, y, state);

            EndPageScrollView();

            businessContentHeight = y + 12f;
        }

        private float DrawCashFlowForecast(Rect inRect, float y, CashFlowReport report)
        {
            Text.Font = GameFont.Medium;
            string heading = $"Cash flow — next {CashFlowForecast.WindowDays} days";
            float headingWidth = Mathf.Max(1f, inRect.width - 12f);
            float headingHeight = Text.CalcHeight(heading, headingWidth);
            Rect headingRect = new Rect(0f, y, headingWidth, headingHeight);
            Widgets.Label(headingRect, heading);
            if (ShouldBuildTooltip(headingRect))
            {
                TooltipHandler.TipRegion(headingRect, CashFlowHeadingTooltip);
            }

            Text.Font = GameFont.Small;
            y += headingHeight + 4f;

            float tableWidth = Mathf.Max(1f, inRect.width - 12f);
            int dayCount = report.days.Count;
            int numberColumnCount = dayCount + 1;
            float labelWidth = tableWidth * 0.3f;
            float numberWidth = Mathf.Max(1f,
                (tableWidth - labelWidth - numberColumnCount * CashFlowColumnGap) /
                numberColumnCount);
            float labelX = 6f;
            float numberX = labelX + labelWidth + CashFlowColumnGap;

            string totalHeader = $"Next {CashFlowForecast.WindowDays} days";
            string[] dayHeaders = new string[dayCount];
            float[] dayHeaderHeights = new float[dayCount];
            float blankHeaderHeight = Text.CalcHeight(string.Empty, labelWidth);
            float totalHeaderHeight = Text.CalcHeight(totalHeader, numberWidth);
            float headerHeight = Mathf.Max(blankHeaderHeight, totalHeaderHeight);
            for (int i = 0; i < dayCount; i++)
            {
                dayHeaders[i] = $"Day {report.days[i].dayIndex + 1}";
                dayHeaderHeights[i] = Text.CalcHeight(dayHeaders[i], numberWidth);
                headerHeight = Mathf.Max(headerHeight, dayHeaderHeights[i]);
            }

            for (int i = 0; i < dayCount; i++)
            {
                float dayX = numberX + i * (numberWidth + CashFlowColumnGap);
                DrawMeasuredCashFlowLabel(
                    new Rect(dayX, y, numberWidth, dayHeaderHeights[i]), dayHeaders[i],
                    TextAnchor.UpperRight);

                Rect dayHeaderRect = new Rect(dayX, y, numberWidth, headerHeight);
                if (ShouldBuildTooltip(dayHeaderRect))
                {
                    TooltipHandler.TipRegion(dayHeaderRect, CashFlowDayTooltip);
                }
            }

            float totalX = numberX + dayCount * (numberWidth + CashFlowColumnGap);
            DrawMeasuredCashFlowLabel(
                new Rect(totalX, y, numberWidth, totalHeaderHeight), totalHeader,
                TextAnchor.UpperRight);
            y += headerHeight + 2f;

            List<int> revenue = new List<int>(numberColumnCount);
            List<int> expenses = new List<int>(numberColumnCount);
            List<int> net = new List<int>(numberColumnCount);
            for (int i = 0; i < dayCount; i++)
            {
                CashFlowDay day = report.days[i];
                revenue.Add(day.revenue);
                expenses.Add(day.expenses);
                net.Add(day.Net);
            }

            // The report owns these totals; the UI deliberately does not recompute a second summary.
            revenue.Add(report.TotalRevenue);
            expenses.Add(report.TotalExpenses);
            net.Add(report.TotalNet);

            y = DrawCashFlowRow(y, labelX, labelWidth, numberX, numberWidth,
                "Expected revenue", revenue, colourNet: false);
            y = DrawCashFlowRow(y, labelX, labelWidth, numberX, numberWidth,
                "Expected expenses", expenses, colourNet: false);
            y = DrawCashFlowRow(y, labelX, labelWidth, numberX, numberWidth,
                "Net", net, colourNet: true);
            return y;
        }

        private static float DrawCashFlowRow(
            float y,
            float labelX,
            float labelWidth,
            float numberX,
            float numberWidth,
            string rowLabel,
            List<int> amounts,
            bool colourNet)
        {
            float rowLabelHeight = Text.CalcHeight(rowLabel, labelWidth);
            string[] amountLabels = new string[amounts.Count];
            float[] amountHeights = new float[amounts.Count];
            float rowHeight = rowLabelHeight;
            for (int i = 0; i < amounts.Count; i++)
            {
                amountLabels[i] = amounts[i].ToString("N0");
                amountHeights[i] = Text.CalcHeight(amountLabels[i], numberWidth);
                rowHeight = Mathf.Max(rowHeight, amountHeights[i]);
            }

            DrawMeasuredCashFlowLabel(
                new Rect(labelX, y, labelWidth, rowLabelHeight), rowLabel, TextAnchor.UpperLeft);
            for (int i = 0; i < amountLabels.Length; i++)
            {
                float x = numberX + i * (numberWidth + CashFlowColumnGap);
                Rect amountRect = new Rect(x, y, numberWidth, amountHeights[i]);
                if (colourNet)
                {
                    DrawCashFlowNet(amountRect, amountLabels[i], amounts[i]);
                }
                else
                {
                    DrawMeasuredCashFlowLabel(amountRect, amountLabels[i], TextAnchor.UpperRight);
                }
            }

            return y + rowHeight;
        }

        private static void DrawMeasuredCashFlowLabel(Rect rect, string text, TextAnchor anchor)
        {
            TextAnchor previousAnchor = Text.Anchor;
            Text.Anchor = anchor;
            try
            {
                Widgets.Label(rect, text);
            }
            finally
            {
                Text.Anchor = previousAnchor;
            }
        }

        private static void DrawCashFlowNet(Rect rect, string text, int net)
        {
            Color previousColor = GUI.color;
            try
            {
                GUI.color = net >= 0
                    ? new Color(0.6f, 0.9f, 0.6f) // Match the existing positive money colour.
                    : new Color(1f, 0.75f, 0.75f); // Match the existing negative contract-margin colour.
                DrawMeasuredCashFlowLabel(rect, text, TextAnchor.UpperRight);
            }
            finally
            {
                GUI.color = previousColor;
            }
        }

        /// <summary>
        /// Where the colony stands right now: silver, the wage bill, and how long one covers the
        /// other.
        ///
        /// The runway line is the most useful sentence on the page and the reason this block sits
        /// above the historical report — §45's "should I hire or train?" is answered by knowing
        /// whether payroll is covered for a season or for four days, not by last quadrum's total.
        /// </summary>
        private float DrawCashPosition(Rect inRect, float y, IntercolonyWorldComponent state)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, 400f, 32f), "Where you stand");
            Text.Font = GameFont.Small;
            y += 36f;

            int silver = BusinessReportService.SilverOnHand();
            int daily = BusinessReportService.DailyWageBill(state);
            float runway = BusinessReportService.PayrollRunwayDays(state);

            Widgets.Label(new Rect(6f, y, inRect.width, LineHeight),
                $"Silver in storage: {silver}");
            y += LineHeight;

            if (daily <= 0)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.6f);
                Widgets.Label(new Rect(6f, y, inRect.width, LineHeight),
                    "No wages owed — nobody is currently employed.");
                GUI.color = Color.white;
                return y + LineHeight;
            }

            Widgets.Label(new Rect(6f, y, inRect.width, LineHeight),
                $"Wage bill: {daily} silver a day across the workforce");
            y += LineHeight;

            // Coloured, because this is the line that should make a player act. §39's arrears
            // escalation is only playable if running dry is visible before it bites.
            GUI.color = runway < 5f ? new Color(1f, 0.55f, 0.55f)
                : runway < 15f ? new Color(1f, 0.9f, 0.6f)
                : new Color(0.6f, 0.9f, 0.6f);

            Widgets.Label(new Rect(6f, y, inRect.width, LineHeight),
                runway < 1f
                    ? "Payroll is not covered for even a day. Wages will go into arrears."
                    : $"That is covered for about {runway:0} more days at the current rate.");

            GUI.color = Color.white;
            return y + LineHeight;
        }

        /// <summary>
        /// §4.9's summary lives here because Business is already the mod's compact answer to
        /// "how is the colony doing?". The rows are category-level reputation milestones, not a
        /// ThingDef ledger, so a mature colony still gets a short readable page.
        /// </summary>
        private float DrawBrandSummary(Rect inRect, float y, IntercolonyWorldComponent state)
        {
            Text.Font = GameFont.Medium;
            string title = "Brand reputation";
            float titleWidth = Mathf.Max(1f, inRect.width - 12f);
            float titleHeight = Text.CalcHeight(title, titleWidth);
            Widgets.Label(new Rect(0f, y, titleWidth, titleHeight), title);
            Text.Font = GameFont.Small;
            y += titleHeight + 4f;

            ProductBrandUiService.BrandSummary summary =
                ProductBrandUiService.BuildSummary(state);
            if (summary.IsEmpty)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.6f);
                float emptyWidth = Mathf.Max(1f, inRect.width - 12f);
                float emptyHeight = Text.CalcHeight(summary.emptyState, emptyWidth);
                Widgets.Label(new Rect(6f, y, emptyWidth, emptyHeight), summary.emptyState);
                GUI.color = Color.white;
                return y + emptyHeight;
            }

            if (summary.knownFor.Count > 0)
            {
                y = DrawBrandSummaryGroup(inRect, y, "Known for", summary.knownFor, positive: true);
            }

            if (summary.weakReputation.Count > 0)
            {
                if (summary.knownFor.Count > 0)
                {
                    y += 8f;
                }

                y = DrawBrandSummaryGroup(
                    inRect, y, "Weak reputation", summary.weakReputation, positive: false);
            }

            return y;
        }

        private static float DrawBrandSummaryGroup(
            Rect inRect,
            float y,
            string heading,
            List<ProductBrandUiService.BrandSummaryRow> rows,
            bool positive)
        {
            float headingWidth = Mathf.Max(1f, inRect.width - 12f);
            Text.Font = GameFont.Medium;
            float headingHeight = Text.CalcHeight(heading, headingWidth);
            Widgets.Label(new Rect(0f, y, headingWidth, headingHeight), heading);
            Text.Font = GameFont.Small;
            y += headingHeight + 4f;

            float contentWidth = Mathf.Max(1f, inRect.width - 40f);
            float keyWidth = Mathf.Min(220f, contentWidth * 0.6f);
            float valueWidth = Mathf.Max(1f, contentWidth - keyWidth - 12f);
            float valueX = 20f + keyWidth + 12f;

            for (int i = 0; i < rows.Count; i++)
            {
                ProductBrandUiService.BrandSummaryRow row = rows[i];
                string key = row.category.Label();
                float keyHeight = Text.CalcHeight(key, keyWidth);
                float valueHeight = Text.CalcHeight(row.bandName, valueWidth);
                float rowHeight = Mathf.Max(keyHeight, valueHeight);

                Widgets.Label(new Rect(20f, y, keyWidth, keyHeight), key);
                GUI.color = positive
                    ? new Color(0.6f, 0.9f, 0.6f)
                    : new Color(1f, 0.75f, 0.75f);
                Rect valueRect = new Rect(valueX, y, valueWidth, valueHeight);
                Widgets.Label(valueRect, row.bandName);
                GUI.color = Color.white;

                if (ShouldBuildTooltip(valueRect) && !row.tooltip.NullOrEmpty())
                {
                    TooltipHandler.TipRegion(valueRect, row.tooltip);
                }

                y += rowHeight + 4f;
            }

            return y;
        }

        /// <summary>§117's report, verbatim in shape: revenue, the costs, and the bottom line.</summary>
        private float DrawPeriodReport(Rect inRect, float y, IntercolonyWorldComponent state)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, 300f, 32f),
                businessWindowDays == BusinessReportService.QuadrumDays ? "Last quadrum" : "Last year");
            Text.Font = GameFont.Small;

            // Nudged down a couple of pixels so the button's centre lines up with the medium-font
            // heading's cap height rather than its box.
            if (Widgets.ButtonText(new Rect(300f, y + 4f, 120f, 26f),
                    businessWindowDays == BusinessReportService.QuadrumDays ? "Show year" : "Show quadrum"))
            {
                businessWindowDays = businessWindowDays == BusinessReportService.QuadrumDays
                    ? BusinessReportService.YearDays
                    : BusinessReportService.QuadrumDays;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }

            y += 36f;

            LedgerService.Report report = LedgerService.Summarise(state, businessWindowDays);

            if (report.entryCount == 0)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.6f);
                string emptyMessage = state.LedgerStartTick == LedgerService.NoHistory
                        ? "Nothing has moved yet. Sell something, hire someone, and this fills in."
                        : "No money moved in this period.";
                float emptyMessageWidth = inRect.width - 12f;
                float emptyMessageHeight = Text.CalcHeight(emptyMessage, emptyMessageWidth);
                Widgets.Label(new Rect(6f, y, emptyMessageWidth, emptyMessageHeight), emptyMessage);
                GUI.color = Color.white;
                return y + emptyMessageHeight;
            }

            // Said plainly rather than shown as a confident total. A colony twelve days old reading
            // "last quadrum: +180" is not reporting a quiet quadrum, it is reporting twelve days,
            // and a player comparing that against a target would be comparing against nothing.
            if (report.partial)
            {
                GUI.color = new Color(1f, 0.9f, 0.6f);
                Widgets.Label(new Rect(6f, y, inRect.width, LineHeight),
                    $"Only {report.daysCovered:0} " +
                    (Mathf.RoundToInt(report.daysCovered) == 1 ? "day" : "days") +
                    " of history so far — this is not a full period.");
                GUI.color = Color.white;
                y += LineHeight;
            }

            foreach (LedgerKind kind in LedgerEntry.ReportOrder)
            {
                int amount = report.Of(kind);
                if (amount == 0)
                {
                    continue;
                }

                Widgets.Label(new Rect(20f, y, 260f, LineHeight), LedgerEntry.Label(kind));

                GUI.color = amount > 0 ? new Color(0.6f, 0.9f, 0.6f) : new Color(1f, 0.75f, 0.75f);
                Text.Anchor = TextAnchor.UpperRight;
                Widgets.Label(new Rect(280f, y, 140f, LineHeight), amount.ToString("N0"));
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;

                y += LineHeight;
            }

            Widgets.DrawLineHorizontal(20f, y + 2f, 400f);
            y += 8f;

            Widgets.Label(new Rect(20f, y, 260f, LineHeight), "Net cash movement");

            GUI.color = report.Net >= 0 ? new Color(0.6f, 0.9f, 0.6f) : new Color(1f, 0.55f, 0.55f);
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(new Rect(280f, y, 140f, LineHeight), report.Net.ToString("N0"));
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            return y + LineHeight + 4f;
        }

    }
}
