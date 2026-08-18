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
    /// §35.2's example screen has four lines — requirement, positions, duration, wage offered — and
    /// this is those four plus the two terms every employment already carries (§37's wage structure
    /// and §42's combat clause), because an applicant is accepting all of it at once.
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
        /// This is a dense form — four sliders, two option columns and three commitment
        /// summaries — and every slider needs a clear band above it for the end labels
        /// HorizontalSlider draws there. 90% left it about 20px short and put a scrollbar on
        /// a dialog that has no business scrolling; the extra 5% buys the room without
        /// removing anything the player needs to read.
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
        private const float SliderHeight = 20f;

        /// <summary>
        /// Widgets.HorizontalSlider draws its end labels above the track, not beside it
        /// (Widgets.cs:2116), so every slider needs this much clear space above its rect
        /// or the labels land in the row above.
        /// </summary>
        private const float SliderLabelBand = 19f;

        /// <summary>Height of a row that contains a slider: label band, track, and a little air.</summary>
        private const float SliderRowHeight = SliderLabelBand + SliderHeight + 6f;
        private const float SkillButtonWidth = 200f;
        private const float WageFieldWidth = 90f;
        private const float WageUnitWidth = 120f;
        private const float MatchTopButtonWidth = 140f;

        private const string IntroText =
            "You name the terms and the wage. Workers who can do the job, and who will work for " +
            "what you are offering, apply as the market brings them past.";

        private readonly IntercolonyWorldComponent state;
        private readonly Action<SkillDef, int, int, int, int, WageStructure, CombatClause> onConfirm;

        private SkillDef skill;
        private int minLevel = 8;
        private int positions = 1;
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
            Action<SkillDef, int, int, int, int, WageStructure, CombatClause> onConfirm)
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
                onConfirm?.Invoke(skill, minLevel, positions, termDays, wageOffered, structure,
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
            float y = 0f;

            Text.Font = GameFont.Medium;
            float titleHeight = Text.CalcHeight("Post a job", width);
            Rect titleRect = new Rect(0f, y, width, titleHeight);
            Widgets.Label(titleRect, "Post a job");
            TooltipHandler.TipRegion(titleRect, IntroText);
            y += titleHeight + SectionGap;
            Text.Font = GameFont.Small;

            float skillLabelWidth = Text.CalcSize("Skill:").x;
            float skillX = skillLabelWidth + RowGap;
            Widgets.Label(new Rect(0f, y, skillLabelWidth, ControlRowHeight), "Skill:");
            if (Widgets.ButtonText(new Rect(skillX, y, SkillButtonWidth, ControlRowHeight),
                    skill == null ? "Any work" : skill.skillLabel.CapitalizeFirst()))
            {
                OpenSkillMenu();
            }

            if (skill != null)
            {
                skillX += SkillButtonWidth + RowGap;
                float qualifierWidth = Text.CalcSize("at least").x;
                Widgets.Label(new Rect(skillX, y + SliderLabelBand, qualifierWidth, ControlRowHeight),
                    "at least");
                skillX += qualifierWidth + RowGap;
                float level = minLevel;
                Widgets.HorizontalSlider(
                    new Rect(skillX, y + SliderLabelBand, width - skillX, SliderHeight),
                    ref level,
                    new FloatRange(0f, 20f), $"{minLevel}", 1f);
                if (Mathf.RoundToInt(level) != minLevel)
                {
                    minLevel = Mathf.RoundToInt(level);
                    rateKey = int.MinValue;
                }
            }

            y += SliderRowHeight + SectionGap;

            float controlWidth = (width - OptionColumnGap) / 2f;
            Rect positionsRect = new Rect(0f, y, controlWidth, SliderRowHeight);
            float positionsLabelWidth = Text.CalcSize("Positions:").x;
            Widgets.Label(
                new Rect(positionsRect.x, y + SliderLabelBand, positionsLabelWidth, ControlRowHeight),
                "Positions:");
            float slots = positions;
            float positionsSliderX = positionsRect.x + positionsLabelWidth + RowGap;
            Widgets.HorizontalSlider(new Rect(positionsSliderX, y + SliderLabelBand,
                    positionsRect.xMax - positionsSliderX, SliderHeight), ref slots,
                new FloatRange(1f, 6f), $"{positions}", 1f);
            positions = Mathf.RoundToInt(slots);

            const string postingGuidance = "Stays up until filled, or until you take it down.";
            TooltipHandler.TipRegion(positionsRect, postingGuidance);

            Rect termRect = new Rect(controlWidth + OptionColumnGap, y, controlWidth,
                SliderRowHeight);
            float termLabelWidth = Text.CalcSize("Term:").x;
            Widgets.Label(new Rect(termRect.x, y + SliderLabelBand, termLabelWidth, ControlRowHeight),
                "Term:");
            float term = termDays;
            float termSliderX = termRect.x + termLabelWidth + RowGap;
            Widgets.HorizontalSlider(new Rect(termSliderX, y + SliderLabelBand,
                    termRect.xMax - termSliderX, SliderHeight), ref term,
                new FloatRange(2f, LaborCandidateService.MaxTermDays), $"{termDays} days", 1f);
            if (Mathf.RoundToInt(term) != termDays)
            {
                termDays = Mathf.RoundToInt(term);
                rateKey = int.MinValue;
            }

            const string termGuidance = "Longer terms cost less per day.";
            TooltipHandler.TipRegion(termRect, termGuidance);
            y += SliderRowHeight;

            y = DrawSectionDivider(width, y);

            RefreshRate();
            Text.Font = GameFont.Medium;
            float wageTitleHeight = Text.CalcHeight("Wage offered", width);
            float matchX = width - MatchTopButtonWidth;
            float unitX = matchX - RowGap - WageUnitWidth;
            float fieldX = unitX - RowGap - WageFieldWidth;
            Widgets.Label(new Rect(0f, y, fieldX - RowGap, wageTitleHeight), "Wage offered");
            Text.Font = GameFont.Small;

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

            y += Mathf.Max(wageTitleHeight, ControlRowHeight) + RowGap;
            float slid = wageOffered;
            int sliderMax = Mathf.Max(rateValid ? rateHigh * 2 : 100, wageOffered);
            Widgets.HorizontalSlider(new Rect(0f, y + SliderLabelBand, width, SliderHeight), ref slid,
                new FloatRange(1f, sliderMax), null, 1f);
            if (Mathf.RoundToInt(slid) != wageOffered)
            {
                SetWage(Mathf.RoundToInt(slid));
            }

            y = DrawRateAdvice(new Rect(0f, 0f, width, 0f), y + SliderRowHeight);

            y = DrawSectionDivider(width, y);

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
                    clause == option, () => clause = captured);
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

        private static float ContentWidth(float availableWidth)
        {
            return availableWidth - ContentLeft * 2f;
        }

        private static float DrawSectionDivider(float width, float y)
        {
            y += SectionGap / 2f;
            Widgets.DrawLineHorizontal(0f, y, width);
            return y + SectionGap / 2f;
        }

        private float OptionsHeight(float width)
        {
            Text.Font = GameFont.Medium;
            float titleHeight = Text.CalcHeight("Post a job", width);
            float wageTitleHeight = Text.CalcHeight("Wage offered", width);
            Text.Font = GameFont.Small;

            float height = titleHeight + SectionGap + SliderRowHeight + SectionGap;
            height += SliderRowHeight + SectionGap;
            height += Mathf.Max(wageTitleHeight, ControlRowHeight) + RowGap;
            height += SliderRowHeight + RateAdviceHeight(width) + SectionGap;

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
            int total = WageStructureUtility.TotalCost(structure, wageOffered, termDays) * positions;
            int upFront = WageStructureUtility.UpFrontCost(structure, wageOffered, termDays);
            int death = wageOffered * clause.DeathCompensationDays();
            totalSummary =
                $"If every position is filled and served out: {total} silver across {positions} " +
                $"worker{(positions == 1 ? "" : "s")}.";
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
            if (!rateValid)
            {
                string advice = skill == null
                    ? "Nobody is reachable for work at all right now. Check the Hire tab."
                    : $"Nobody reachable has {skill.skillLabel.CapitalizeFirst()} {minLevel}+. " +
                      "You can still post — the market changes — but expect a wait.";
                return Text.CalcHeight(advice, width);
            }

            string marketGuidance =
                $"{qualified} reachable worker{(qualified == 1 ? "" : "s")} can do this. " +
                $"For {termDays} days as a {clause.Label()} they ask " +
                $"{rateLow} to {rateHigh} silver a day.";
            string verdict = wageOffered < rateLow
                ? $"Your offer is below all of them. Expect no replies until the market changes — the cheapest wants {rateLow}."
                : wageOffered >= rateHigh
                    ? "Your offer clears everyone who qualifies. Expect the best of them."
                    : Mathf.InverseLerp(rateLow, rateHigh, wageOffered) < 0.34f
                        ? "Your offer sits low in that band — expect few replies, and not the strongest."
                        : Mathf.InverseLerp(rateLow, rateHigh, wageOffered) < 0.67f
                            ? "Your offer sits mid-band — expect a reasonable choice."
                            : "Your offer sits high in the band — expect most of them to be interested.";
            return Text.CalcHeight(marketGuidance, width) + RowGap +
                   Text.CalcHeight(verdict, width);
        }


        /// <summary>
        /// The going rate, and a plain sentence about where the offer sits in it.
        ///
        /// The sentence matters more than the numbers. "34 to 46" tells a player who already
        /// understands the market what to do; "your offer is below what anyone will take" tells a
        /// player who does not.
        /// </summary>
        private float DrawRateAdvice(Rect inRect, float y)
        {
            if (!rateValid)
            {
                string advice = skill == null
                    ? "Nobody is reachable for work at all right now. Check the Hire tab."
                    : $"Nobody reachable has {skill.skillLabel.CapitalizeFirst()} {minLevel}+. " +
                      "You can still post — the market changes — but expect a wait.";
                float adviceHeight = Text.CalcHeight(advice, inRect.width);
                GUI.color = new Color(1f, 0.8f, 0.5f);
                Widgets.Label(new Rect(0f, y, inRect.width, adviceHeight), advice);
                GUI.color = Color.white;
                return y + adviceHeight;
            }

            string marketGuidance =
                $"{qualified} reachable worker{(qualified == 1 ? "" : "s")} can do this. " +
                $"For {termDays} days as a {clause.Label()} they ask " +
                $"{rateLow} to {rateHigh} silver a day.";
            float marketGuidanceHeight = Text.CalcHeight(marketGuidance, inRect.width);
            GUI.color = new Color(1f, 1f, 1f, 0.75f);
            Widgets.Label(new Rect(0f, y, inRect.width, marketGuidanceHeight), marketGuidance);
            GUI.color = Color.white;
            y += marketGuidanceHeight + RowGap;

            string verdict;
            Color colour;

            if (wageOffered < rateLow)
            {
                verdict = $"Your offer is below all of them. Expect no replies until the market " +
                          $"changes — the cheapest wants {rateLow}.";
                colour = new Color(1f, 0.55f, 0.55f);
            }
            else if (wageOffered >= rateHigh)
            {
                verdict = "Your offer clears everyone who qualifies. Expect the best of them.";
                colour = new Color(0.6f, 0.9f, 0.6f);
            }
            else
            {
                float through = Mathf.InverseLerp(rateLow, rateHigh, wageOffered);
                verdict = through < 0.34f
                    ? "Your offer sits low in that band — expect few replies, and not the strongest."
                    : through < 0.67f
                        ? "Your offer sits mid-band — expect a reasonable choice."
                        : "Your offer sits high in the band — expect most of them to be interested.";
                colour = new Color(1f, 0.9f, 0.6f);
            }

            GUI.color = colour;
            float verdictHeight = Text.CalcHeight(verdict, inRect.width);
            Widgets.Label(new Rect(0f, y, inRect.width, verdictHeight), verdict);
            GUI.color = Color.white;
            return y + verdictHeight;
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
            // The structure belongs in the key now that it changes the band: without it,
            // switching to daily would keep showing the per-quadrum rate until something else
            // happened to invalidate the cache.
            int key = Gen.HashCombineInt(
                Gen.HashCombineInt(skill?.shortHash ?? 0, minLevel, termDays, (int)clause),
                (int)structure);
            if (key == rateKey)
            {
                return;
            }

            rateKey = key;
            rateValid = JobPostingService.GoingRate(
                state, skill, minLevel, termDays, clause, structure,
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
