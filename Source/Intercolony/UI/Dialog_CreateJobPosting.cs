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

        public override Vector2 InitialSize => new Vector2(660f, 760f);

        public override void DoWindowContents(Rect inRect)
        {
            int total = WageStructureUtility.TotalCost(structure, wageOffered, termDays) * positions;
            int upFront = WageStructureUtility.UpFrontCost(structure, wageOffered, termDays);
            int death = wageOffered * clause.DeathCompensationDays();
            string totalSummary =
                $"If every position is filled and served out: {total} silver across {positions} " +
                $"worker{(positions == 1 ? "" : "s")}.";
            string upFrontSummary = structure == WageStructure.Prepaid
                ? $"Due when you take on each applicant: {upFront} silver for the whole term, paid at once."
                : $"Due when you take on each applicant: {upFront} silver signing fee.";
            string deathSummary = $"Compensation if one of them dies: {death} silver each.";
            float totalSummaryHeight = Text.CalcHeight(totalSummary, inRect.width);
            float upFrontSummaryHeight = Text.CalcHeight(upFrontSummary, inRect.width);
            float deathSummaryHeight = Text.CalcHeight(deathSummary, inRect.width);
            float summaryHeight = 8f + totalSummaryHeight + 2f + upFrontSummaryHeight + 2f +
                                  deathSummaryHeight + 8f;
            float bottom = inRect.height - 40f;
            float optionsBottom = bottom - summaryHeight;
            Rect optionsRect = new Rect(0f, 0f, inRect.width, Mathf.Max(1f, optionsBottom));
            float optionsWidth = optionsRect.width - 16f;
            RefreshRate();
            Rect optionsView = new Rect(0f, 0f, optionsWidth, OptionsHeight(optionsWidth));

            Widgets.BeginScrollView(optionsRect, ref optionsScroll, optionsView);
            DrawOptions(optionsWidth);
            Widgets.EndScrollView();

            float y = optionsBottom + 8f;

            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            Widgets.Label(new Rect(0f, y, inRect.width, totalSummaryHeight), totalSummary);
            y += totalSummaryHeight + 2f;
            Widgets.Label(new Rect(0f, y, inRect.width, upFrontSummaryHeight), upFrontSummary);
            y += upFrontSummaryHeight + 2f;
            Widgets.Label(new Rect(0f, y, inRect.width, deathSummaryHeight), deathSummary);
            GUI.color = Color.white;
            y += deathSummaryHeight + 8f;

            if (Widgets.ButtonText(new Rect(0f, bottom, 170f, 36f), "Post"))
            {
                onConfirm?.Invoke(skill, minLevel, positions, termDays, wageOffered, structure,
                    clause);
                Close();
            }

            if (Widgets.ButtonText(new Rect(inRect.width - 130f, bottom, 120f, 36f), "Cancel"))
            {
                Close();
            }
        }

        private void DrawOptions(float width)
        {
            float y = 0f;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, width, 32f), "Post a job");
            y += 36f;
            Text.Font = GameFont.Small;

            string intro = "You name the terms and the wage. Workers who can do the job, and who will work for " +
                           "what you are offering, apply as the market brings them past.";
            float introHeight = Text.CalcHeight(intro, width);
            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            Widgets.Label(new Rect(0f, y, width, introHeight), intro);
            GUI.color = Color.white;
            y += introHeight + 4f;

            Widgets.Label(new Rect(0f, y, 90f, 28f), "Skill:");
            if (Widgets.ButtonText(new Rect(92f, y, 200f, 28f),
                    skill == null ? "Any work" : skill.skillLabel.CapitalizeFirst()))
            {
                OpenSkillMenu();
            }

            if (skill != null)
            {
                Widgets.Label(new Rect(302f, y, 60f, 28f), "at least");
                float level = minLevel;
                Widgets.HorizontalSlider(new Rect(368f, y + 4f, width - 420f, 20f), ref level,
                    new FloatRange(0f, 20f), $"{minLevel}", 1f);
                if (Mathf.RoundToInt(level) != minLevel)
                {
                    minLevel = Mathf.RoundToInt(level);
                    rateKey = int.MinValue;
                }
            }

            y += 34f;

            Widgets.Label(new Rect(0f, y, 90f, 28f), "Positions:");
            float slots = positions;
            Widgets.HorizontalSlider(new Rect(92f, y + 4f, 240f, 20f), ref slots,
                new FloatRange(1f, 6f), $"{positions}", 1f);
            positions = Mathf.RoundToInt(slots);

            const string postingGuidance = "Stays up until filled, or until you take it down.";
            float guidanceWidth = width - 348f;
            float postingGuidanceHeight = Text.CalcHeight(postingGuidance, guidanceWidth);
            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            Widgets.Label(new Rect(344f, y, guidanceWidth, postingGuidanceHeight), postingGuidance);
            GUI.color = Color.white;
            y += Mathf.Max(34f, postingGuidanceHeight + 4f);

            Widgets.Label(new Rect(0f, y, 90f, 28f), "Term:");
            float term = termDays;
            Widgets.HorizontalSlider(new Rect(92f, y + 4f, 240f, 20f), ref term,
                new FloatRange(2f, LaborCandidateService.MaxTermDays), $"{termDays} days", 1f);
            if (Mathf.RoundToInt(term) != termDays)
            {
                termDays = Mathf.RoundToInt(term);
                rateKey = int.MinValue;
            }

            const string termGuidance = "Longer terms cost less per day.";
            float termGuidanceHeight = Text.CalcHeight(termGuidance, guidanceWidth);
            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            Widgets.Label(new Rect(344f, y + 3f, guidanceWidth, termGuidanceHeight), termGuidance);
            GUI.color = Color.white;
            y += Mathf.Max(34f, termGuidanceHeight + 7f);

            Widgets.Label(new Rect(0f, y, width, 24f), "Clause:");
            y += 24f;
            foreach (CombatClause option in CombatClauseUtility.All)
            {
                y = LaborOptionRows.Draw(width, y, CombatClauseUtility.Summary(option, wageOffered),
                    option.Explain(), clause == option, () => clause = option);
            }

            y += 6f;
            Widgets.Label(new Rect(0f, y, width, 24f), "Paid:");
            y += 24f;
            foreach (WageStructure option in
                     new[] { WageStructure.Prepaid, WageStructure.Quadrum, WageStructure.Daily })
            {
                WageStructure captured = option;
                y = LaborOptionRows.Draw(width, y, StructureTitle(option),
                    WageStructureUtility.Explain(option, wageOffered, termDays), structure == option,
                    () => structure = captured);
            }

            y += 10f;
            Widgets.DrawLineHorizontal(0f, y, width);
            y += 10f;

            RefreshRate();
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, 240f, 32f), "Wage offered");
            Text.Font = GameFont.Small;

            int typed = wageOffered;
            Widgets.TextFieldNumeric(new Rect(240f, y + 2f, 90f, 28f), ref typed, ref wageBuffer, 1, 9999);
            if (typed != wageOffered)
            {
                wageOffered = typed;
            }

            Widgets.Label(new Rect(336f, y + 5f, 120f, 24f), "silver / day");
            if (rateValid && Widgets.ButtonText(new Rect(width - 150f, y + 2f, 140f, 28f),
                    $"Match top ({rateHigh})"))
            {
                SetWage(rateHigh);
            }

            y += 36f;
            float slid = wageOffered;
            int sliderMax = Mathf.Max(rateValid ? rateHigh * 2 : 100, wageOffered);
            Widgets.HorizontalSlider(new Rect(0f, y + 4f, width, 20f), ref slid,
                new FloatRange(1f, sliderMax), null, 1f);
            if (Mathf.RoundToInt(slid) != wageOffered)
            {
                SetWage(Mathf.RoundToInt(slid));
            }

            DrawRateAdvice(new Rect(0f, 0f, width, 0f), y + 30f);
        }

        private float OptionsHeight(float width)
        {
            string intro = "You name the terms and the wage. Workers who can do the job, and who will work for " +
                           "what you are offering, apply as the market brings them past.";
            const string postingGuidance = "Stays up until filled, or until you take it down.";
            const string termGuidance = "Longer terms cost less per day.";
            float guidanceWidth = width - 348f;
            float height = 36f + Text.CalcHeight(intro, width) + 4f + 34f;
            height += Mathf.Max(34f, Text.CalcHeight(postingGuidance, guidanceWidth) + 4f);
            height += Mathf.Max(34f, Text.CalcHeight(termGuidance, guidanceWidth) + 7f);
            height += 24f;

            foreach (CombatClause option in CombatClauseUtility.All)
            {
                height += LaborOptionRows.Height(CombatClauseUtility.Summary(option, wageOffered),
                    option.Explain(), width);
            }

            height += 30f;
            foreach (WageStructure option in
                     new[] { WageStructure.Prepaid, WageStructure.Quadrum, WageStructure.Daily })
            {
                height += LaborOptionRows.Height(StructureTitle(option),
                    WageStructureUtility.Explain(option, wageOffered, termDays), width);
            }

            return height + 20f + 36f + 30f + RateAdviceHeight(width);
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
                return Text.CalcHeight(advice, width) + 4f;
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
            return Text.CalcHeight(marketGuidance, width) + 4f + Text.CalcHeight(verdict, width) + 4f;
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
                return y + adviceHeight + 4f;
            }

            string marketGuidance =
                $"{qualified} reachable worker{(qualified == 1 ? "" : "s")} can do this. " +
                $"For {termDays} days as a {clause.Label()} they ask " +
                $"{rateLow} to {rateHigh} silver a day.";
            float marketGuidanceHeight = Text.CalcHeight(marketGuidance, inRect.width);
            GUI.color = new Color(1f, 1f, 1f, 0.75f);
            Widgets.Label(new Rect(0f, y, inRect.width, marketGuidanceHeight), marketGuidance);
            GUI.color = Color.white;
            y += marketGuidanceHeight + 4f;

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
            return y + verdictHeight + 4f;
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
