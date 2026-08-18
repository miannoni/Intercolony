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
            y = DrawPeriodReport(viewRect, y, state);
            y += 12f;
            y = DrawContractEstimates(viewRect, y, state);

            EndPageScrollView();

            businessContentHeight = y + 12f;
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
                float emptyMessageHeight = Text.CalcHeight(emptyMessage, inRect.width);
                Widgets.Label(new Rect(6f, y, inRect.width, emptyMessageHeight), emptyMessage);
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

        /// <summary>
        /// §45's screen: each standing agreement, and whether it is worth having.
        /// </summary>
        private float DrawContractEstimates(Rect inRect, float y, IntercolonyWorldComponent state)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, 400f, 32f), "Standing agreements");
            Text.Font = GameFont.Small;
            y += 36f;

            List<BusinessReportService.ContractEstimate> estimates =
                BusinessReportService.ActiveEstimates(state);

            if (estimates.Count == 0)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.6f);
                string emptyMessage = "No standing agreements. Build a trading record and settlements will propose them.";
                float emptyMessageHeight = Text.CalcHeight(emptyMessage, inRect.width);
                Widgets.Label(new Rect(6f, y, inRect.width, emptyMessageHeight), emptyMessage);
                GUI.color = Color.white;
                return y + emptyMessageHeight;
            }

            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            Widgets.Label(new Rect(6f, y, inRect.width, LineHeight),
                "Per delivery cycle. Everything below the revenue line is an estimate.");
            GUI.color = Color.white;
            y += LineHeight + 4f;

            foreach (BusinessReportService.ContractEstimate estimate in estimates)
            {
                y = DrawEstimate(new Rect(0f, y, inRect.width, 126f), estimate);
            }

            return y;
        }

        private float DrawEstimate(Rect rect, BusinessReportService.ContractEstimate estimate)
        {
            RecurringContract contract = estimate.contract;
            float y = rect.y;

            Widgets.Label(new Rect(6f, y, rect.width - 12f, LineHeight),
                $"{contract.settlementName} — {contract.quantityPerCycle}x {contract.ItemLabel()} " +
                $"every {contract.CadenceDays:F0} days" +
                (contract.status == ContractStatus.Suspended ? "   (suspended by war)" : ""));
            y += LineHeight;

            y = EstimateLine(rect, y, "Revenue, payable", estimate.revenue);
            y = EstimateLine(rect, y, "If you bought the goods instead", estimate.inputsIfBought);
            y = EstimateLine(rect, y, "Wage bill over the cycle", estimate.payroll);
            y = EstimateLine(rect, y, "Delivery premium earned, and hauled for", estimate.transport);

            Widgets.DrawLineHorizontal(20f, y + 2f, 400f);
            y += 8f;

            Widgets.Label(new Rect(20f, y, 260f, LineHeight), "Estimated margin");

            GUI.color = estimate.Margin >= 0 ? new Color(0.6f, 0.9f, 0.6f) : new Color(1f, 0.55f, 0.55f);
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(new Rect(280f, y, 140f, LineHeight), estimate.Margin.ToString("N0"));
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            // The sentence that turns four numbers into a decision (§45).
            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            Widgets.Label(new Rect(440f, y, rect.width - 450f, LineHeight),
                estimate.Margin >= 0
                    ? $"about {estimate.MarginPerDay:0} silver a day; making the goods rather than " +
                      $"buying them is worth {estimate.MakingSaves:N0} a cycle"
                    : "the wage bill alone outweighs this agreement");
            GUI.color = Color.white;

            return y + LineHeight + 12f;
        }

        private static float EstimateLine(Rect rect, float y, string label, int amount)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.85f);
            Widgets.Label(new Rect(20f, y, 400f, LineHeight), label);

            GUI.color = amount >= 0 ? new Color(0.6f, 0.9f, 0.6f) : new Color(1f, 0.75f, 0.75f);
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(new Rect(420f, y, 140f, LineHeight), amount.ToString("N0"));
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            return y + LineHeight;
        }
    }
}
