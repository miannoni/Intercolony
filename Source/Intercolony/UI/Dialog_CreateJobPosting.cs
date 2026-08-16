using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

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
        private readonly Action<SkillDef, int, int, int, int, WageStructure, CombatClause, int> onConfirm;

        private SkillDef skill;
        private int minLevel = 8;
        private int positions = 1;
        private int termDays = 20;
        private int wageOffered = 30;
        private int lifespanDays = JobPostingService.DefaultLifespanDays;
        private WageStructure structure = WageStructure.Daily;
        private CombatClause clause = CombatClause.Civilian;

        private string wageBuffer;

        /// <summary>Cached band, recomputed only when an input that feeds it changes.</summary>
        private int rateLow;
        private int rateHigh;
        private int qualified;
        private bool rateValid;
        private int rateKey = int.MinValue;

        public Dialog_CreateJobPosting(
            IntercolonyWorldComponent state,
            Action<SkillDef, int, int, int, int, WageStructure, CombatClause, int> onConfirm)
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

        public override Vector2 InitialSize => new Vector2(660f, 720f);

        public override void DoWindowContents(Rect inRect)
        {
            float y = 0f;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, inRect.width, 32f), "Post a job");
            y += 36f;
            Text.Font = GameFont.Small;

            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            Widgets.Label(new Rect(0f, y, inRect.width, 40f),
                "You name the terms and the wage. Workers who can do the job, and who will work for " +
                "what you are offering, apply as the market brings them past.");
            GUI.color = Color.white;
            y += 44f;

            // --- Requirement ---
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
                Widgets.HorizontalSlider(new Rect(368f, y + 4f, inRect.width - 420f, 20f), ref level,
                    new FloatRange(0f, 20f), $"{minLevel}", 1f);
                if (Mathf.RoundToInt(level) != minLevel)
                {
                    minLevel = Mathf.RoundToInt(level);
                    rateKey = int.MinValue;
                }
            }

            y += 34f;

            // --- Positions ---
            Widgets.Label(new Rect(0f, y, 90f, 28f), "Positions:");
            float slots = positions;
            Widgets.HorizontalSlider(new Rect(92f, y + 4f, 240f, 20f), ref slots,
                new FloatRange(1f, 6f), $"{positions}", 1f);
            positions = Mathf.RoundToInt(slots);

            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            Widgets.Label(new Rect(344f, y, inRect.width - 348f, 40f),
                "Stays up until filled, or until it lapses.");
            GUI.color = Color.white;
            y += 34f;

            // --- Duration ---
            Widgets.Label(new Rect(0f, y, 90f, 28f), "Term:");
            float term = termDays;
            Widgets.HorizontalSlider(new Rect(92f, y + 4f, 240f, 20f), ref term,
                new FloatRange(2f, LaborCandidateService.MaxTermDays), $"{termDays} days", 1f);
            if (Mathf.RoundToInt(term) != termDays)
            {
                termDays = Mathf.RoundToInt(term);
                rateKey = int.MinValue;
            }

            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            Widgets.Label(new Rect(344f, y + 3f, inRect.width - 348f, 24f),
                "Longer terms cost less per day.");
            GUI.color = Color.white;
            y += 34f;

            // --- Lifespan ---
            Widgets.Label(new Rect(0f, y, 90f, 28f), "Advertise:");
            float life = lifespanDays;
            Widgets.HorizontalSlider(new Rect(92f, y + 4f, 240f, 20f), ref life,
                new FloatRange(JobPostingService.MinLifespanDays, JobPostingService.MaxLifespanDays),
                $"{lifespanDays} days", 1f);
            lifespanDays = Mathf.RoundToInt(life);

            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            Widgets.Label(new Rect(344f, y + 3f, inRect.width - 348f, 24f),
                "How long the notice stays up.");
            GUI.color = Color.white;
            y += 38f;

            // --- Clause (§42) ---
            Widgets.Label(new Rect(0f, y, 90f, 28f), "Clause:");
            float x = 92f;
            foreach (CombatClause option in CombatClauseUtility.All)
            {
                x = DrawChoice(x, y, option.LabelCap(), clause == option, () => clause = option);
            }

            y += 34f;

            // --- Wage structure (§37) ---
            Widgets.Label(new Rect(0f, y, 90f, 28f), "Paid:");
            x = 92f;
            foreach (WageStructure option in
                     new[] { WageStructure.Prepaid, WageStructure.Quadrum, WageStructure.Daily })
            {
                WageStructure captured = option;
                x = DrawChoice(x, y, option.Label().CapitalizeFirst(), structure == option,
                    () => structure = captured);
            }

            y += 40f;

            Widgets.DrawLineHorizontal(0f, y, inRect.width);
            y += 10f;

            // --- The wage, and what it is worth ---
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

            if (rateValid && Widgets.ButtonText(new Rect(inRect.width - 150f, y + 2f, 140f, 28f),
                    $"Match top ({rateHigh})"))
            {
                SetWage(rateHigh);
            }

            y += 36f;

            float slid = wageOffered;
            int sliderMax = Mathf.Max(rateValid ? rateHigh * 2 : 100, wageOffered);
            Widgets.HorizontalSlider(new Rect(0f, y + 4f, inRect.width, 20f), ref slid,
                new FloatRange(1f, sliderMax), null, 1f);
            if (Mathf.RoundToInt(slid) != wageOffered)
            {
                SetWage(Mathf.RoundToInt(slid));
            }

            y += 30f;

            y = DrawRateAdvice(inRect, y);

            // --- Commit ---
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

            y += 8f;

            GUI.color = new Color(1f, 1f, 1f, 0.7f);
            Widgets.Label(new Rect(0f, y, inRect.width, totalSummaryHeight), totalSummary);
            y += totalSummaryHeight + 2f;
            Widgets.Label(new Rect(0f, y, inRect.width, upFrontSummaryHeight), upFrontSummary);
            y += upFrontSummaryHeight + 2f;
            Widgets.Label(new Rect(0f, y, inRect.width, deathSummaryHeight), deathSummary);
            GUI.color = Color.white;
            y += deathSummaryHeight + 8f;

            if (Widgets.ButtonText(new Rect(0f, y, 170f, 36f), "Post"))
            {
                onConfirm?.Invoke(skill, minLevel, positions, termDays, wageOffered, structure,
                    clause, lifespanDays);
                Close();
            }

            if (Widgets.ButtonText(new Rect(inRect.width - 130f, y, 120f, 36f), "Cancel"))
            {
                Close();
            }
        }

        /// <summary>
        /// One choice in a row of them, drawn so the chosen one actually looks chosen.
        ///
        /// The highlight has to go **after** the button, not before. `Widgets.ButtonText` paints its
        /// own background, so a `DrawHighlightSelected` drawn first is simply covered up — which is
        /// why the clause and payment rows showed no selection at all while the hire dialog, which
        /// highlights a plain row rather than a button, looked right.
        /// </summary>
        private static float DrawChoice(float x, float y, string label, bool selected, Action choose)
        {
            Rect button = new Rect(x, y, 150f, 28f);

            if (Widgets.ButtonText(button, label) && !selected)
            {
                choose();
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }

            if (selected)
            {
                Widgets.DrawHighlightSelected(button);
            }

            return x + 154f;
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
