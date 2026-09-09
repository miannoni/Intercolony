using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Intercolony
{
    /// <summary>
    /// The hiring commitment (DESIGN.md §37, §110, §111).
    ///
    /// Replaces the shared quantity dialog for hiring, because from Phase 18 the decision has two
    /// dimensions rather than one: how long, and how they are paid. §111's acceptance criterion is
    /// that "the trade-off between structures is visible at the moment of hiring rather than
    /// discovered afterwards", so all three structures are priced side by side against the term
    /// currently selected — not described in the abstract and left to the player to work out.
    ///
    /// The term slider stays on the commitment pop-up, which is where Matteo asked every
    /// commitment slider to live.
    /// </summary>
    public class Dialog_HireWorker : Window
    {
        private const float OptionsHeaderHeight = 26f;
        private const float OptionsSectionGap = 6f;
        private const float CostLabelWidth = 150f;
        private const float CostColumnGap = 8f;
        private const float CostRowGap = 4f;
        private const float CostTooltipWidth = 18f;

        private readonly LaborCandidate candidate;
        private readonly SettlementEconomicProfile profile;
        private readonly Map map;
        private readonly Action<int, WageStructure, CombatClause, EmploymentHireCostQuote> onConfirm;
        private readonly int maxTermDays;
        private readonly EmploymentEquipmentQuote equipmentQuote;
        private readonly bool emergencyDispatch;

        private int termDays;
        private string termBuffer;
        private WageStructure structure = WageStructure.Quadrum;
        private Vector2 optionsScroll;

        /// <summary>
        /// §42's clause. Civilian by default because it is the cheapest and the most restrictive:
        /// the player should have to choose to buy the right to draft someone, not discover they
        /// had it all along.
        /// </summary>
        private CombatClause clause = CombatClause.Civilian;

        /// <summary>
        /// §36.4 — no agreed end date. The term slider still sets the *pricing* term, because a
        /// worker signing on indefinitely prices like a long engagement rather than a day rate; what
        /// changes is that nothing expires.
        /// </summary>
        private bool openEnded;

        public Dialog_HireWorker(
            LaborCandidate candidate, SettlementEconomicProfile profile, Map map, int maxTermDays,
            bool emergencyDispatch,
            Action<int, WageStructure, CombatClause, EmploymentHireCostQuote> onConfirm)
        {
            this.candidate = candidate;
            this.profile = profile;
            this.map = map;
            this.maxTermDays = Mathf.Max(candidate.minTermDays, maxTermDays);
            this.emergencyDispatch = emergencyDispatch;
            this.onConfirm = onConfirm;
            equipmentQuote = EmploymentEquipmentService.Quote(candidate.pawn);

            // Open at the minimum term: the cheapest commitment, so spending more is a choice the
            // player makes rather than a default they have to notice and undo.
            termDays = candidate.minTermDays;
            termBuffer = termDays.ToString();

            // Periodic is the safer default. §37 calls quadrum payroll the "likely default for
            // longer employment", and it is the structure that cannot bankrupt a player at the
            // moment of hiring.
            structure = candidate.minTermDays >= GenDate.DaysPerQuadrum
                ? WageStructure.Quadrum
                : WageStructure.Daily;

            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize => new Vector2(620f, 760f);

        private float EmployerStanding =>
            EmployerReputationService.ScoreFor(IntercolonyWorldComponent.Current);

        private int DailyWage => WageFor(clause);

        private int WageFor(CombatClause option)
        {
            return WageFor(option, emergencyDispatch);
        }

        private int WageFor(CombatClause option, bool emergencyMode)
        {
            return LaborCandidateService.DailyWage(
                candidate.pawn, profile, candidate.distanceTiles,
                openEnded ? maxTermDays : termDays, EmployerStanding, option, emergencyMode);
        }

        public override void DoWindowContents(Rect inRect)
        {
            float y = 0f;

            Text.Font = GameFont.Medium;
            string title = emergencyDispatch
                ? $"Emergency hire — {candidate.Name}"
                : $"Hire {candidate.Name}";
            float titleHeight = Text.CalcHeight(title, inRect.width);
            Widgets.Label(new Rect(0f, y, inRect.width, titleHeight), title);
            y += titleHeight + 4f;
            Text.Font = GameFont.Small;

            string candidateSummary =
                $"{candidate.factionName}, from {candidate.settlementName}\n" +
                $"{candidate.SkillSummary(4)}";
            float candidateSummaryHeight = Text.CalcHeight(candidateSummary, inRect.width);
            GUI.color = new Color(1f, 1f, 1f, 0.75f);
            Widgets.Label(new Rect(0f, y, inRect.width, candidateSummaryHeight), candidateSummary);
            GUI.color = Color.white;
            y += candidateSummaryHeight + 4f;

            // --- Term ---
            Widgets.Label(new Rect(0f, y, 60f, 28f), "Days:");
            int typed = termDays;
            Widgets.TextFieldNumeric(new Rect(62f, y, 80f, 28f), ref typed, ref termBuffer,
                candidate.minTermDays, maxTermDays);
            if (typed != termDays)
            {
                SetTerm(typed);
            }

            if (Widgets.ButtonText(new Rect(150f, y, 60f, 28f), "Min"))
            {
                SetTerm(candidate.minTermDays);
            }

            if (Widgets.ButtonText(new Rect(214f, y, 60f, 28f), "Max"))
            {
                SetTerm(maxTermDays);
            }

            string termGuidance =
                $"{candidate.minTermDays} to {maxTermDays} — they will not work for less";
            float termGuidanceWidth = inRect.width - 288f;
            float termGuidanceHeight = Text.CalcHeight(termGuidance, termGuidanceWidth);
            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            Widgets.Label(new Rect(282f, y + 3f, termGuidanceWidth, termGuidanceHeight),
                termGuidance);
            GUI.color = Color.white;

            y += Mathf.Max(32f, termGuidanceHeight + 7f);
            int slid = Mathf.RoundToInt(Widgets.HorizontalSlider(
                new Rect(0f, y + 4f, inRect.width, 20f), termDays, candidate.minTermDays, maxTermDays));
            if (slid != termDays)
            {
                SetTerm(slid);
            }

            y += 32f;

            // §36.4. Offered as a toggle below the term rather than a fourth wage structure,
            // because it is not a way of paying — it is the absence of an end date.
            const float openEndedWidth = 210f;
            float openEndedHeight = Mathf.Max(
                24f, Text.CalcHeight("No end date", openEndedWidth - 24f));
            Rect openRect = new Rect(0f, y, openEndedWidth, openEndedHeight);
            bool wasOpenEnded = openEnded;
            Widgets.CheckboxLabeled(openRect, "No end date", ref openEnded);
            if (openEnded != wasOpenEnded)
            {
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }
            y += openEndedHeight + 2f;

            int wage = DailyWage;

            string wageSummary = openEnded
                ? $"{wage} silver/day, open-ended \u2014 they stay until one of you ends it."
                : $"{wage} silver/day for {termDays} days.";
            float wageSummaryHeight = Text.CalcHeight(wageSummary, inRect.width);
            Widgets.Label(new Rect(0f, y, inRect.width, wageSummaryHeight), wageSummary);
            y += wageSummaryHeight + 2f;

            if (termDays > candidate.minTermDays)
            {
                int atMinimum = LaborCandidateService.DailyWage(
                    candidate.pawn, profile, candidate.distanceTiles, candidate.minTermDays,
                    EmployerStanding, clause, emergencyDispatch);
                if (wage < atMinimum)
                {
                    string longerTerm =
                        $"Longer term: {wage}/day instead of {atMinimum}/day at their minimum.";
                    float longerTermHeight = Text.CalcHeight(longerTerm, inRect.width);
                    GUI.color = new Color(0.6f, 0.9f, 0.6f);
                    Widgets.Label(new Rect(0f, y, inRect.width, longerTermHeight), longerTerm);
                    GUI.color = Color.white;
                    y += longerTermHeight + 4f;
                }
            }

            // --- Commit ---
            if (openEnded && structure == WageStructure.Prepaid)
            {
                // Nothing to prepay when there is no agreed end. Silently corrected rather than
                // disabled, so the player is not left staring at a greyed-out row wondering why.
                structure = WageStructure.Quadrum;
            }

            int upFront = WageStructureUtility.UpFrontCost(structure, wage, termDays);
            EmploymentHireCostQuote hireCostQuote = EmploymentEquipmentService.QuoteHireCost(
                upFront, equipmentQuote);
            long totalDue = hireCostQuote.totalDue;
            int available = PurchaseOrderService.CountColonySilver(map);
            bool affordable = totalDue <= int.MaxValue && available >= totalDue;
            int ordinaryWage = emergencyDispatch ? WageFor(clause, false) : wage;
            List<TermRow> costRows = BuildCostRows(
                structure, hireCostQuote, available, candidate, emergencyDispatch,
                ordinaryWage, wage);
            float costHeight = CostRowsHeight(costRows, inRect.width);

            float bottom = inRect.height - 40f;
            float optionsBottom = bottom - costHeight - 6f;
            Rect optionsRect = new Rect(0f, y, inRect.width, Mathf.Max(1f, optionsBottom - y));
            float optionsWidth = optionsRect.width - 16f;
            float optionsHeight = OptionsHeight(optionsWidth, wage);
            Rect optionsView = new Rect(0f, 0f, optionsWidth, optionsHeight);

            Widgets.BeginScrollView(optionsRect, ref optionsScroll, optionsView);
            float optionY = 0f;

            // The clause changes the daily rate, so it stays above the payment structures whose
            // prices it controls. Only these choices scroll, keeping the commitment in view.
            Widgets.Label(new Rect(0f, optionY, optionsWidth, 24f),
                "What they can be asked to do:");
            optionY += OptionsHeaderHeight;

            foreach (CombatClause option in CombatClauseUtility.All)
            {
                optionY = DrawClauseOption(optionsWidth, optionY, option);
            }

            optionY += OptionsSectionGap;
            Widgets.Label(new Rect(0f, optionY, optionsWidth, 24f), "How they are paid:");
            optionY += OptionsHeaderHeight;

            optionY = DrawStructureOption(optionsWidth, optionY, WageStructure.Prepaid, wage);
            optionY = DrawStructureOption(optionsWidth, optionY, WageStructure.Quadrum, wage);
            DrawStructureOption(optionsWidth, optionY, WageStructure.Daily, wage);
            Widgets.EndScrollView();

            DrawCostRows(costRows, inRect.width, bottom - costHeight, affordable);

            bool guiEnabled = GUI.enabled;
            GUI.enabled = guiEnabled && affordable;
            if (Widgets.ButtonText(new Rect(0f, bottom, 170f, 36f), "Hire"))
            {
                onConfirm?.Invoke(openEnded ? 0 : termDays, structure, clause, hireCostQuote);
                Close();
            }
            GUI.enabled = guiEnabled;

            if (Widgets.ButtonText(new Rect(inRect.width - 130f, bottom, 120f, 36f), "Cancel"))
            {
                Close();
            }
        }

        private static List<TermRow> BuildCostRows(
            WageStructure structure, EmploymentHireCostQuote hireCostQuote, int available,
            LaborCandidate candidate, bool emergencyDispatch, int ordinaryWage, int wage)
        {
            List<TermRow> rows = new List<TermRow>
            {
                new TermRow(
                    structure.IsPeriodic() ? "Signing fee" : "Prepaid wages",
                    $"{hireCostQuote.upfrontWages:N0} silver"),
                new TermRow(
                    "Equipment bond",
                    EmploymentEquipmentService.BondLabel(hireCostQuote.equipment.bond),
                    EmploymentEquipmentService.BondTooltip)
            };

            if (emergencyDispatch)
            {
                int premium = Mathf.Max(0, wage - ordinaryWage);
                int arrivalDays = LaborCandidateService.ArrivalDaysFor(candidate, true);
                rows.Add(new TermRow(
                    "Emergency premium",
                    $"+{premium:N0} silver/day ({LaborCandidateService.EmergencyDispatchWageMultiplier:0.#}x wage)",
                    EmergencyPremiumTooltip(ordinaryWage, wage)));
                rows.Add(new TermRow(
                    "Arrival",
                    $"{ArrivalLabel(arrivalDays)} (ordinary: {ArrivalLabel(candidate.travelDays)})",
                    EmergencyArrivalTooltip(candidate, arrivalDays)));
            }

            rows.Add(new TermRow("Due at hire", $"{hireCostQuote.totalDue:N0} silver"));
            rows.Add(new TermRow("In storage", $"{available:N0} silver"));
            return rows;
        }

        private static string EmergencyPremiumTooltip(int ordinaryWage, int emergencyWage)
        {
            return $"Emergency dispatch applies a {LaborCandidateService.EmergencyDispatchWageMultiplier:0.#}x " +
                   $"urgency multiplier in the shared wage calculation ({ordinaryWage:N0} ordinary to " +
                   $"{emergencyWage:N0} silver/day). It pays for priority and mobilisation; it does " +
                   "not guarantee that a worker exists or can fulfil the request.";
        }

        private static string EmergencyArrivalTooltip(LaborCandidate candidate, int arrivalDays)
        {
            return $"This worker is in the nearest half of the current direct-hire market by ordinary " +
                   $"travel time ({candidate.travelDays} days). Emergency dispatch uses the existing " +
                   $"employment arrival time and compresses that trip to {ArrivalLabel(arrivalDays)} " +
                   "with a one-day minimum. Drop-pod arrival is not offered because F21 has no " +
                   "settlement logistics capability model to gate it on.";
        }

        private static string ArrivalLabel(int days)
        {
            return days <= 0
                ? "Same day"
                : $"Within {days} {(days == 1 ? "day" : "days")}";
        }

        private static float CostRowsHeight(List<TermRow> rows, float width)
        {
            float height = 0f;
            for (int i = 0; i < rows.Count; i++)
            {
                height += CostRowHeight(rows[i], width);
                if (i < rows.Count - 1)
                {
                    height += CostRowGap;
                }
            }

            return height;
        }

        private static float CostRowHeight(TermRow row, float width)
        {
            return Mathf.Max(
                Text.CalcHeight(row.label ?? "", CostLabelWidth),
                Text.CalcHeight(row.value ?? "", CostValueWidth(row, width)));
        }

        private static float CostValueWidth(TermRow row, float width)
        {
            float valueWidth = row.label.NullOrEmpty()
                ? width
                : width - CostLabelWidth - CostColumnGap;
            return Mathf.Max(1f, valueWidth - CostTooltipWidth);
        }

        private static void DrawCostRows(
            List<TermRow> rows, float width, float startY, bool affordable)
        {
            float y = startY;
            for (int i = 0; i < rows.Count; i++)
            {
                TermRow row = rows[i];
                float rowHeight = CostRowHeight(row, width);
                Rect rowRect = new Rect(0f, y, width, rowHeight);
                float valueX = 0f;

                if (!row.label.NullOrEmpty())
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.65f);
                    Widgets.Label(new Rect(0f, y, CostLabelWidth, rowHeight), row.label);
                    GUI.color = Color.white;
                    valueX = CostLabelWidth + CostColumnGap;
                }

                GUI.color = affordable
                    ? Color.white
                    : new Color(1f, 0.6f, 0.6f);
                Widgets.Label(new Rect(valueX, y, CostValueWidth(row, width), rowHeight),
                    row.value ?? "");
                GUI.color = Color.white;

                if (!row.tooltip.NullOrEmpty())
                {
                    TooltipHandler.TipRegion(rowRect, row.tooltip);
                    Widgets.DrawHighlightIfMouseover(rowRect);
                    GUI.color = new Color(0.6f, 0.85f, 1f, 0.65f);
                    Text.Anchor = TextAnchor.UpperCenter;
                    Widgets.Label(new Rect(width - CostTooltipWidth, y,
                        CostTooltipWidth, rowHeight), "?");
                    Text.Anchor = TextAnchor.UpperLeft;
                    GUI.color = Color.white;
                }

                y += rowHeight + CostRowGap;
            }
        }

        /// <summary>
        /// One radio row per combat clause, each showing its daily rate *and* what a death under it
        /// would cost. Both numbers together are the whole of §42's economics: the cheap worker is
        /// the expensive one to lose, and seeing that before hiring is what stops the meat-shield
        /// strategy being discovered as a good idea and abandoned only after the bill arrives.
        /// </summary>
        private float DrawClauseOption(float width, float y, CombatClause option)
        {
            int optionWage = WageFor(option);
            string title = option.Summary(optionWage);
            return LaborOptionRows.Draw(width, y, title, option.Explain(), clause == option,
                () => clause = option);
        }

        /// <summary>
        /// One radio row per structure, each priced for the term currently chosen. Showing all
        /// three at once is the point: the player compares, rather than picking blind and
        /// discovering the cost later.
        /// </summary>
        private float DrawStructureOption(float width, float y, WageStructure option, int wage)
        {
            string title = StructureTitle(option, wage);
            return LaborOptionRows.Draw(width, y, title,
                WageStructureUtility.Explain(option, wage, termDays), structure == option,
                () => structure = option);
        }

        private float OptionsHeight(float width, int wage)
        {
            float height = OptionsHeaderHeight;
            foreach (CombatClause option in CombatClauseUtility.All)
            {
                int optionWage = WageFor(option);
                height += LaborOptionRows.Height(
                    option.Summary(optionWage),
                    option.Explain(), width);
            }

            height += OptionsSectionGap + OptionsHeaderHeight;
            foreach (WageStructure option in
                     new[] { WageStructure.Prepaid, WageStructure.Quadrum, WageStructure.Daily })
            {
                height += LaborOptionRows.Height(
                    StructureTitle(option, wage),
                    WageStructureUtility.Explain(option, wage, termDays), width);
            }

            return height;
        }

        private string StructureTitle(WageStructure option, int wage)
        {
            int total = WageStructureUtility.TotalCost(option, wage, termDays);
            switch (option)
            {
                case WageStructure.Prepaid:
                    return $"Prepaid — {total} silver total";
                case WageStructure.Daily:
                    return $"Daily — {total} silver total";
                default:
                    return $"Per quadrum — {total} silver total";
            }
        }

        private void SetTerm(int value)
        {
            termDays = Mathf.Clamp(value, candidate.minTermDays, maxTermDays);
            termBuffer = termDays.ToString();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
        }
    }
}
