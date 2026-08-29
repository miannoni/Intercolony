using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Writing a job advertisement (DESIGN.md §35.2, §114).
    ///
    /// §35.2's example screen has three lines — requirement, duration, wage offered — and this is
    /// those three plus the two terms every employment already carries (§37's wage structure and
    /// §42's combat clause), because an applicant is accepting all of it at once. The posting itself
    /// names no number of positions: it stays open and the player hires as many applicants as they
    /// like.
    ///
    /// The going-rate band is the difference between this and a blind guess. It is **measured, not
    /// modelled**: it asks the same question the matcher will ask, of the same pool, so the numbers
    /// shown are the numbers that will decide who applies. A band rather than a figure because the
    /// requirement genuinely spans a range — a Construction 10 labourer next door and a
    /// Construction 18 master across the planet both qualify and do not cost the same.
    /// </summary>
    public class Dialog_CreateJobPosting : Window
    {
        private const float WindowWidth = 1000f;
        private const float WindowMargin = 18f;

        /// <summary>
        /// This is a dense form — a title, up to three labelled sliders with scale text, a wage
        /// field row, two option columns and three summary lines — and genuinely needs the room.
        /// Capped short of the full screen only so the window never touches the edges.
        /// </summary>
        private const float MaxScreenHeightFraction = 0.95f;
        private const float BottomButtonsHeight = 40f;
        private const float ContentInset = 8f;
        private const float ScrollbarGutter = 16f;

        /// <summary>
        /// The scrollbar gutter is reserved on both sides, not just the side the scrollbar
        /// appears on, so the visual margin does not change when the scroll view engages.
        /// </summary>
        private const float ContentLeft = ContentInset + ScrollbarGutter;
        private const float RowGap = 4f;
        private const float SectionGap = 8f;
        private const float OptionColumnGap = 12f;
        private const float ControlRowHeight = 28f;

        /// <summary>
        /// Widgets.HorizontalSlider draws its end labels above the track when asked to
        /// (Widgets.cs:2110-2134). Every slider in this dialog is called with
        /// label/leftAlignedLabel/rightAlignedLabel all null, which skips that block entirely —
        /// no text drawn, no shift to the track rect — so the min/current/max labels are drawn by
        /// this file, underneath the track, instead.
        /// </summary>
        private const float SliderTrackHeight = 20f;

        /// <summary>
        /// HorizontalSlider's rail and handle occupy only the first ~12px of the rect it is
        /// given (Widgets.cs — rail at +2 height 8, handle 12px from the rail centre), so the
        /// scale is placed against where the slider visually ends rather than the rect's full
        /// height, which would leave a gap.
        /// </summary>
        private const float SliderVisibleHeight = 12f;

        private const float SliderScaleGap = 2f;

        /// <summary>
        /// Row height is measured, not guessed — the scale text below the track is a full line
        /// under GameFont.Small (Text.LineHeight), tall enough to hold descenders such as the
        /// 'y' in "day". A hard-coded height clipped them.
        /// </summary>
        private static float SliderRowHeight =>
            SliderVisibleHeight + SliderScaleGap + SliderScaleTextHeight();

        /// <summary>
        /// The two-column grid every row in the upper form lines up on: labels in a fixed-width
        /// left column, every control starting at the same x in the column to its right.
        /// </summary>
        private const float LabelColumnWidth = 130f;
        private const float LabelColumnGap = 12f;
        private const float SliderWidth = 460f;

        private const float SkillButtonWidth = 200f;
        private const float WageFieldWidth = 90f;
        private const float WageUnitWidth = 120f;
        private const float MatchTopButtonWidth = 140f;

        private const string IntroText =
            "You name the terms and the wage. Workers who can do the job, and who will work for " +
            "what you are offering, apply as the market brings them past.";

        private readonly IntercolonyWorldComponent state;
        private readonly Action<SkillDef, int, int, int, WageStructure, CombatClause> onConfirm;

        private SkillDef skill;
        private int minLevel = 8;
        private int termDays = 20;
        private int wageOffered = 30;
        private WageStructure structure = WageStructure.Daily;
        private CombatClause clause = CombatClause.Civilian;

        private string wageBuffer;
        private Vector2 optionsScroll;

        /// <summary>Cached band, recomputed only when an input that feeds it changes.</summary>
        private int rateLow;
        private int rateHigh;
        private int qualified;
        private bool rateValid;
        private int rateKey = int.MinValue;

        public Dialog_CreateJobPosting(
            IntercolonyWorldComponent state,
            Action<SkillDef, int, int, int, WageStructure, CombatClause> onConfirm)
        {
            this.state = state;
            this.onConfirm = onConfirm;

            skill = DefDatabase<SkillDef>.AllDefsListForReading.Count > 0
                ? SkillDefOf.Construction
                : null;

            wageBuffer = wageOffered.ToString();

            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize
        {
            get
            {
                Text.Font = GameFont.Small;
                RefreshRate();
                float contentWidth = ContentWidth(WindowWidth - WindowMargin * 2f);
                BuildSummaries(out string totalSummary, out string upFrontSummary,
                    out string deathSummary);
                float fixedHeight = WindowMargin * 2f + BottomButtonsHeight +
                                    SummaryHeight(contentWidth, totalSummary, upFrontSummary,
                                        deathSummary);
                float contentHeight = OptionsHeight(contentWidth);
                float height = Mathf.Min(fixedHeight + contentHeight,
                    UI.screenHeight * MaxScreenHeightFraction);
                return new Vector2(WindowWidth,
                    Mathf.Max(fixedHeight + Text.LineHeight, height));
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            BuildSummaries(out string totalSummary, out string upFrontSummary,
                out string deathSummary);
            float contentWidth = ContentWidth(inRect.width);
            float totalSummaryHeight = Text.CalcHeight(totalSummary, contentWidth);
            float upFrontSummaryHeight = Text.CalcHeight(upFrontSummary, contentWidth);
            float deathSummaryHeight = Text.CalcHeight(deathSummary, contentWidth);
            float summaryHeight = SummaryHeight(contentWidth, totalSummary, upFrontSummary,
                deathSummary);
            float bottom = inRect.height - BottomButtonsHeight;
            float optionsBottom = bottom - summaryHeight;
            Rect optionsRect = new Rect(ContentLeft, 0f, contentWidth + ScrollbarGutter,
                Mathf.Max(1f, optionsBottom));
            RefreshRate();
            float optionsHeight = OptionsHeight(contentWidth);
            if (optionsHeight <= optionsRect.height)
            {
                optionsScroll = Vector2.zero;
                GUI.BeginGroup(new Rect(ContentLeft, 0f, contentWidth, optionsRect.height));
                DrawOptions(contentWidth);
                GUI.EndGroup();
            }
            else
            {
                Rect optionsView = new Rect(0f, 0f, contentWidth, optionsHeight);
                Widgets.BeginScrollView(optionsRect, ref optionsScroll, optionsView);
                DrawOptions(contentWidth);
                Widgets.EndScrollView();
            }

            float y = optionsBottom + SectionGap;

            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            Widgets.Label(new Rect(ContentLeft, y, contentWidth, totalSummaryHeight), totalSummary);
            y += totalSummaryHeight + RowGap;
            Widgets.Label(new Rect(ContentLeft, y, contentWidth, upFrontSummaryHeight),
                upFrontSummary);
            y += upFrontSummaryHeight + RowGap;
            Widgets.Label(new Rect(ContentLeft, y, contentWidth, deathSummaryHeight), deathSummary);
            GUI.color = Color.white;

            if (Widgets.ButtonText(new Rect(ContentLeft, bottom, 170f, 36f), "Post"))
            {
                onConfirm?.Invoke(skill, minLevel, termDays, wageOffered, structure,
                    clause);
                Close();
            }

            if (Widgets.ButtonText(new Rect(ContentLeft + contentWidth - 120f, bottom, 120f, 36f),
                    "Cancel"))
            {
                Close();
            }
        }

        private void DrawOptions(float width)
        {
            float controlsX = LabelColumnWidth + LabelColumnGap;
            float y = 0f;

            Text.Font = GameFont.Medium;
            float titleHeight = Text.CalcHeight("Post a job", width);
            Rect titleRect = new Rect(0f, y, width, titleHeight);
            Widgets.Label(titleRect, "Post a job");
            TooltipHandler.TipRegion(titleRect, IntroText);
            y += titleHeight + SectionGap;
            Text.Font = GameFont.Small;

            // Skill row.
            DrawRowLabel("Skill", y, ControlRowHeight);
            if (Widgets.ButtonText(new Rect(controlsX, y, SkillButtonWidth, ControlRowHeight),
                    skill == null ? "Any work" : skill.skillLabel.CapitalizeFirst()))
            {
                OpenSkillMenu();
            }
            y += ControlRowHeight + RowGap;

            // Minimum level row — only when a skill is set.
            if (skill != null)
            {
                DrawRowLabel("Minimum level", y, SliderTrackHeight);
                float level = minLevel;
                DrawLabeledSlider(controlsX, y, ref level, 0f, 20f, "0", $"{minLevel}", "20", 1f);
                if (Mathf.RoundToInt(level) != minLevel)
                {
                    minLevel = Mathf.RoundToInt(level);
                    rateKey = int.MinValue;
                }
                y += SliderRowHeight + RowGap;
            }

            // Term row.
            DrawRowLabel("Term", y, SliderTrackHeight);
            float term = termDays;
            DrawLabeledSlider(controlsX, y, ref term, 2f, LaborCandidateService.MaxTermDays, "2",
                $"{termDays} days", $"{LaborCandidateService.MaxTermDays}", 1f);
            if (Mathf.RoundToInt(term) != termDays)
            {
                termDays = Mathf.RoundToInt(term);
                rateKey = int.MinValue;
            }
            const string termGuidance = "Longer terms cost less per day.";
            TooltipHandler.TipRegion(new Rect(0f, y, width, SliderRowHeight), termGuidance);
            y += SliderRowHeight;

            y = DrawSectionDivider(width, y);

            RefreshRate();

            // Wage row: label, field, unit, Match top — all on the controls column x.
            float wageLabelHeight = Text.CalcHeight("Wage offered", LabelColumnWidth);
            float wageRowHeight = Mathf.Max(wageLabelHeight, ControlRowHeight);
            DrawRowLabel("Wage offered", y, wageRowHeight);

            float fieldX = controlsX;
            float unitX = fieldX + WageFieldWidth + RowGap;
            float matchX = controlsX + SliderWidth - MatchTopButtonWidth;

            int typed = wageOffered;
            Widgets.TextFieldNumeric(new Rect(fieldX, y, WageFieldWidth, ControlRowHeight),
                ref typed, ref wageBuffer, 1, 9999);
            if (typed != wageOffered)
            {
                wageOffered = typed;
            }

            Widgets.Label(new Rect(unitX, y, WageUnitWidth, ControlRowHeight), "silver / day");
            if (rateValid && Widgets.ButtonText(
                    new Rect(matchX, y, MatchTopButtonWidth, ControlRowHeight),
                    $"Match top ({rateHigh})"))
            {
                SetWage(rateHigh);
            }

            y += wageRowHeight + RowGap;

            // Wage slider row, directly beneath the field, same controls column x and width.
            float slid = wageOffered;
            int sliderMax = Mathf.Max(rateValid ? rateHigh * 2 : 100, wageOffered);
            DrawLabeledSlider(controlsX, y, ref slid, 1f, sliderMax, "1",
                $"{wageOffered} silver/day", $"{sliderMax}", 1f);
            if (Mathf.RoundToInt(slid) != wageOffered)
            {
                SetWage(Mathf.RoundToInt(slid));
            }
            y += SliderRowHeight;

            float rateAdviceHeight = RateAdviceHeight(width - controlsX);
            DrawRowLabel("Going rate", y, rateAdviceHeight);
            y = DrawRateAdvice(controlsX, width - controlsX, y);

            y = DrawSectionDivider(width, y);

            // Clause / Paid columns — unchanged.
            float columnWidth = (width - OptionColumnGap) / 2f;
            float clauseHeight = ClauseColumnHeight(columnWidth);
            float structureHeight = StructureColumnHeight(columnWidth);

            GUI.BeginGroup(new Rect(0f, y, columnWidth, clauseHeight));
            float clauseY = Text.CalcHeight("Clause:", columnWidth);
            Widgets.Label(new Rect(0f, 0f, columnWidth, clauseY), "Clause:");
            foreach (CombatClause option in CombatClauseUtility.All)
            {
                CombatClause captured = option;
                clauseY = LaborOptionRows.Draw(columnWidth, clauseY,
                    CombatClauseUtility.Summary(option, wageOffered), option.Explain(),
                    clause == option, () =>
                    {
                        clause = captured;
                        rateKey = int.MinValue;
                    });
            }
            GUI.EndGroup();

            GUI.BeginGroup(new Rect(columnWidth + OptionColumnGap, y, columnWidth, structureHeight));
            float structureY = Text.CalcHeight("Paid:", columnWidth);
            Widgets.Label(new Rect(0f, 0f, columnWidth, structureY), "Paid:");
            foreach (WageStructure option in
                     new[] { WageStructure.Prepaid, WageStructure.Quadrum, WageStructure.Daily })
            {
                WageStructure captured = option;
                structureY = LaborOptionRows.Draw(columnWidth, structureY, StructureTitle(option),
                    WageStructureUtility.Explain(option, wageOffered, termDays), structure == option,
                    () => structure = captured);
            }
            GUI.EndGroup();
        }

        /// <summary>
        /// Draws a row label in the fixed-width left column, vertically centred against the
        /// height of the control it labels rather than aligned to the top of the row.
        /// </summary>
        private static void DrawRowLabel(string label, float rowY, float controlHeight)
        {
            float labelHeight = Text.CalcHeight(label, LabelColumnWidth);
            float labelY = rowY + (controlHeight - labelHeight) / 2f;
            Widgets.Label(new Rect(0f, labelY, LabelColumnWidth, labelHeight), label);
        }

        /// <summary>
        /// A bare slider track (all three HorizontalSlider label parameters null, so it draws
        /// nothing above itself) with min / current / max drawn underneath, dimmed.
        /// </summary>
        private static void DrawLabeledSlider(float x, float y, ref float value, float min,
            float max, string minLabel, string currentLabel, string maxLabel, float roundTo)
        {
            Rect trackRect = new Rect(x, y, SliderWidth, SliderTrackHeight);
            value = Widgets.HorizontalSlider(trackRect, value, min, max, middleAlignment: false,
                label: null, leftAlignedLabel: null, rightAlignedLabel: null, roundTo: roundTo);

            Rect scaleRect = new Rect(x, y + SliderVisibleHeight + SliderScaleGap, SliderWidth,
                SliderScaleTextHeight());
            Color color = GUI.color;
            GameFont font = Text.Font;
            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            Text.Font = GameFont.Small;

            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(scaleRect, minLabel);
            Text.Anchor = TextAnchor.UpperLeft;

            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(scaleRect, currentLabel);
            Text.Anchor = TextAnchor.UpperLeft;

            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(scaleRect, maxLabel);
            Text.Anchor = TextAnchor.UpperLeft;

            Text.Font = font;
            GUI.color = color;
        }

        /// <summary>
        /// Text.LineHeight is indexed by the current GameFont, so this pins it to Small rather
        /// than trusting whatever font the caller left set — the same height this dialog uses
        /// for the scale labels themselves.
        /// </summary>
        private static float SliderScaleTextHeight()
        {
            GameFont font = Text.Font;
            Text.Font = GameFont.Small;
            float height = Text.LineHeight;
            Text.Font = font;
            return height;
        }

        private static float ContentWidth(float availableWidth)
        {
            return availableWidth - ContentLeft * 2f;
        }

        /// <summary>A separator should be felt, not read.</summary>
        private static readonly Color DividerColor = new Color(1f, 1f, 1f, 0.18f);

        private static float DrawSectionDivider(float width, float y)
        {
            y += SectionGap / 2f;
            Widgets.DrawLineHorizontal(0f, y, width, DividerColor);
            return y + SectionGap / 2f;
        }

        private float OptionsHeight(float width)
        {
            float controlsX = LabelColumnWidth + LabelColumnGap;

            Text.Font = GameFont.Medium;
            float titleHeight = Text.CalcHeight("Post a job", width);
            Text.Font = GameFont.Small;

            float height = titleHeight + SectionGap;
            height += ControlRowHeight + RowGap;
            if (skill != null)
            {
                height += SliderRowHeight + RowGap;
            }
            height += SliderRowHeight;
            height += SectionGap;

            float wageLabelHeight = Text.CalcHeight("Wage offered", LabelColumnWidth);
            float wageRowHeight = Mathf.Max(wageLabelHeight, ControlRowHeight);
            height += wageRowHeight + RowGap;
            height += SliderRowHeight;
            height += RateAdviceHeight(width - controlsX);
            height += SectionGap;

            float columnWidth = (width - OptionColumnGap) / 2f;
            height += Mathf.Max(ClauseColumnHeight(columnWidth),
                StructureColumnHeight(columnWidth));
            return height;
        }

        private float ClauseColumnHeight(float width)
        {
            float height = Text.CalcHeight("Clause:", width);
            foreach (CombatClause option in CombatClauseUtility.All)
            {
                height += LaborOptionRows.Height(CombatClauseUtility.Summary(option, wageOffered),
                    option.Explain(), width);
            }
            return height;
        }

        private float StructureColumnHeight(float width)
        {
            float height = Text.CalcHeight("Paid:", width);
            foreach (WageStructure option in
                     new[] { WageStructure.Prepaid, WageStructure.Quadrum, WageStructure.Daily })
            {
                height += LaborOptionRows.Height(StructureTitle(option),
                    WageStructureUtility.Explain(option, wageOffered, termDays), width);
            }
            return height;
        }

        private void BuildSummaries(out string totalSummary, out string upFrontSummary,
            out string deathSummary)
        {
            int total = WageStructureUtility.TotalCost(structure, wageOffered, termDays);
            int upFront = WageStructureUtility.UpFrontCost(structure, wageOffered, termDays);
            int death = wageOffered * clause.DeathCompensationDays();
            totalSummary =
                $"Each worker you take on: {total} silver over the full term.";
            upFrontSummary = structure == WageStructure.Prepaid
                ? $"Due when you take on each applicant: {upFront} silver for the whole term, paid at once."
                : $"Due when you take on each applicant: {upFront} silver signing fee.";
            deathSummary = $"Compensation if one of them dies: {death} silver each.";
        }

        private static float SummaryHeight(float width, string totalSummary,
            string upFrontSummary, string deathSummary)
        {
            return SectionGap + Text.CalcHeight(totalSummary, width) + RowGap +
                   Text.CalcHeight(upFrontSummary, width) + RowGap +
                   Text.CalcHeight(deathSummary, width) + SectionGap;
        }

        private string StructureTitle(WageStructure option)
        {
            int total = WageStructureUtility.TotalCost(option, wageOffered, termDays);
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

        private float RateAdviceHeight(float width)
        {
            string advice = GoingRateText();
            float valueHeight = Text.CalcHeight(advice, width);
            float labelHeight = Text.CalcHeight("Going rate", LabelColumnWidth);
            return Mathf.Max(valueHeight, labelHeight);
        }

        /// <summary>
        /// The going rate is a labelled value; the tooltip explains why it moves.
        /// Drawn starting at the controls column x so it aligns under the wage controls above it.
        /// </summary>
        private float DrawRateAdvice(float x, float width, float y)
        {
            string advice = GoingRateText();
            float valueHeight = Text.CalcHeight(advice, width);
            float rowHeight = Mathf.Max(valueHeight,
                Text.CalcHeight("Going rate", LabelColumnWidth));
            Rect valueRect = new Rect(x, y, width, valueHeight);
            Widgets.Label(valueRect, advice);
            TooltipHandler.TipRegion(valueRect, GoingRateTooltip());
            return y + rowHeight;
        }

        private string GoingRateText()
        {
            string workers = $"{qualified} reachable worker{(qualified == 1 ? "" : "s")}";
            if (!rateValid)
            {
                return $"{workers} can do this job; no daily ask is available.";
            }

            string ask = rateLow == rateHigh
                ? $"{rateLow} silver"
                : $"{rateLow}–{rateHigh} silver";
            return $"{workers} can do this job; they ask {ask} per day.";
        }

        private string GoingRateTooltip()
        {
            string standing = EmployerStandingLabel();
            return $"Employer standing: {standing}. Workers' daily asks include your standing " +
                   "as an employer, so this rate moves with your record.";
        }

        private string EmployerStandingLabel()
        {
            EmployerReputation reputation = EmployerReputationService.For(state);
            return reputation == null ? "unknown" : reputation.TierLabel();
        }

        /// <summary>
        /// Recomputes the band only when something feeding it changed.
        ///
        /// Not an optimisation for its own sake: the band walks the whole world pool, and GUI code
        /// runs at least twice a frame. Recomputing every frame would price forty workers a hundred
        /// times a second while the player drags the wage slider — which does not even affect it.
        /// </summary>
        private void RefreshRate()
        {
            int key = Gen.HashCombineInt(
                skill?.shortHash ?? 0, minLevel, termDays, (int)clause);
            if (key == rateKey)
            {
                return;
            }

            rateKey = key;
            rateValid = JobPostingService.GoingRate(
                state, skill, minLevel, termDays, clause,
                out rateLow, out rateHigh, out qualified);
        }

        private void SetWage(int value)
        {
            wageOffered = Mathf.Clamp(value, 1, 9999);
            wageBuffer = wageOffered.ToString();
        }

        private void OpenSkillMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Any work", () =>
                {
                    skill = null;
                    rateKey = int.MinValue;
                })
            };

            foreach (SkillDef def in DefDatabase<SkillDef>.AllDefsListForReading)
            {
                SkillDef captured = def;
                options.Add(new FloatMenuOption(def.skillLabel.CapitalizeFirst(), () =>
                {
                    skill = captured;
                    rateKey = int.MinValue;
                }));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }
    }
}
