using System;
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
        private readonly LaborCandidate candidate;
        private readonly SettlementEconomicProfile profile;
        private readonly Map map;
        private readonly Action<int, WageStructure, CombatClause> onConfirm;
        private readonly int maxTermDays;

        private int termDays;
        private string termBuffer;
        private WageStructure structure = WageStructure.Quadrum;

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
            Action<int, WageStructure, CombatClause> onConfirm)
        {
            this.candidate = candidate;
            this.profile = profile;
            this.map = map;
            this.maxTermDays = Mathf.Max(candidate.minTermDays, maxTermDays);
            this.onConfirm = onConfirm;

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

        private int WageFor(CombatClause option) => LaborCandidateService.DailyWage(
            candidate.pawn, profile, candidate.distanceTiles, termDays, EmployerStanding, option);

        public override void DoWindowContents(Rect inRect)
        {
            float y = 0f;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, inRect.width, 32f), $"Hire {candidate.Name}");
            y += 36f;
            Text.Font = GameFont.Small;

            GUI.color = new Color(1f, 1f, 1f, 0.75f);
            Widgets.Label(new Rect(0f, y, inRect.width, 44f),
                $"{candidate.factionName}, from {candidate.settlementName}\n" +
                $"{candidate.SkillSummary(4)}");
            GUI.color = Color.white;
            y += 48f;

            int wage = DailyWage;

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

            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            Widgets.Label(new Rect(282f, y + 3f, inRect.width - 288f, 24f),
                $"{candidate.minTermDays} to {maxTermDays} — they will not work for less");
            GUI.color = Color.white;

            y += 32f;
            int slid = Mathf.RoundToInt(Widgets.HorizontalSlider(
                new Rect(0f, y + 4f, inRect.width, 20f), termDays, candidate.minTermDays, maxTermDays));
            if (slid != termDays)
            {
                SetTerm(slid);
            }

            y += 32f;

            // §36.4. Offered as a toggle beside the term rather than a fourth wage structure,
            // because it is not a way of paying — it is the absence of an end date.
            Rect openRect = new Rect(inRect.width - 220f, y - 34f, 210f, 28f);
            bool wasOpenEnded = openEnded;
            Widgets.CheckboxLabeled(openRect, "No end date", ref openEnded);
            if (openEnded != wasOpenEnded)
            {
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }

            Widgets.Label(new Rect(0f, y, inRect.width, 24f),
                openEnded
                    ? $"{wage} silver/day, open-ended \u2014 they stay until one of you ends it."
                    : $"{wage} silver/day for {termDays} days.");
            y += 26f;

            if (termDays > candidate.minTermDays)
            {
                int atMinimum = LaborCandidateService.DailyWage(
                    candidate.pawn, profile, candidate.distanceTiles, candidate.minTermDays,
                    EmployerStanding, clause);
                if (wage < atMinimum)
                {
                    GUI.color = new Color(0.6f, 0.9f, 0.6f);
                    Widgets.Label(new Rect(0f, y, inRect.width, 24f),
                        $"Longer term: {wage}/day instead of {atMinimum}/day at their minimum.");
                    GUI.color = Color.white;
                }
            }

            y += 28f;

            // --- Combat clause (§42) ---
            //
            // Above the wage structure on purpose. The clause changes the daily rate, so choosing
            // it first means every structure below is priced against a rate the player has already
            // settled — the same "compare, don't discover" standard §111 set.
            Widgets.Label(new Rect(0f, y, inRect.width, 24f), "What they can be asked to do:");
            y += 26f;

            foreach (CombatClause option in CombatClauseUtility.All)
            {
                y = DrawClauseOption(inRect, y, option);
            }

            y += 6f;

            // --- Wage structure (§37) ---
            Widgets.Label(new Rect(0f, y, inRect.width, 24f), "How they are paid:");
            y += 26f;

            y = DrawStructureOption(inRect, y, WageStructure.Prepaid, wage);
            y = DrawStructureOption(inRect, y, WageStructure.Quadrum, wage);
            y = DrawStructureOption(inRect, y, WageStructure.Daily, wage);

            // --- Commit ---
            if (openEnded && structure == WageStructure.Prepaid)
            {
                // Nothing to prepay when there is no agreed end. Silently corrected rather than
                // disabled, so the player is not left staring at a greyed-out row wondering why.
                structure = WageStructure.Quadrum;
            }

            int upFront = WageStructureUtility.UpFrontCost(structure, wage, termDays);
            int available = PurchaseOrderService.CountColonySilver(map);
            bool affordable = available >= upFront;

            float bottom = inRect.height - 40f;

            GUI.color = affordable ? new Color(1f, 1f, 1f, 0.7f) : new Color(1f, 0.6f, 0.6f);
            Widgets.Label(new Rect(0f, bottom - 26f, inRect.width, 24f),
                upFront > 0
                    ? $"Due now: {upFront} silver.  In storage: {available}."
                    : $"Nothing due now. In storage: {available}.");
            GUI.color = Color.white;

            if (Widgets.ButtonText(new Rect(0f, bottom, 170f, 36f), "Hire"))
            {
                onConfirm?.Invoke(openEnded ? 0 : termDays, structure, clause);
                Close();
            }

            if (Widgets.ButtonText(new Rect(inRect.width - 130f, bottom, 120f, 36f), "Cancel"))
            {
                Close();
            }
        }

        /// <summary>
        /// One radio row per combat clause, each showing its daily rate *and* what a death under it
        /// would cost. Both numbers together are the whole of §42's economics: the cheap worker is
        /// the expensive one to lose, and seeing that before hiring is what stops the meat-shield
        /// strategy being discovered as a good idea and abandoned only after the bill arrives.
        /// </summary>
        private float DrawClauseOption(Rect inRect, float y, CombatClause option)
        {
            const float RowHeight = 56f;
            Rect row = new Rect(0f, y, inRect.width, RowHeight);

            if (clause == option)
            {
                Widgets.DrawHighlightSelected(row);
            }
            else
            {
                Widgets.DrawHighlightIfMouseover(row);
            }

            Widgets.RadioButton(new Vector2(4f, y + 14f), clause == option);

            int optionWage = WageFor(option);
            int death = optionWage * option.DeathCompensationDays();

            Widgets.Label(new Rect(34f, y + 2f, inRect.width - 40f, 22f),
                $"{option.LabelCap()} — {optionWage} silver/day, {death} silver if they die");

            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            Widgets.Label(new Rect(34f, y + 24f, inRect.width - 40f, 32f), option.Explain());
            GUI.color = Color.white;

            if (Widgets.ButtonInvisible(row) && clause != option)
            {
                clause = option;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }

            return y + RowHeight;
        }

        /// <summary>
        /// One radio row per structure, each priced for the term currently chosen. Showing all
        /// three at once is the point: the player compares, rather than picking blind and
        /// discovering the cost later.
        /// </summary>
        private float DrawStructureOption(Rect inRect, float y, WageStructure option, int wage)
        {
            const float RowHeight = 52f;
            Rect row = new Rect(0f, y, inRect.width, RowHeight);

            if (structure == option)
            {
                Widgets.DrawHighlightSelected(row);
            }
            else
            {
                Widgets.DrawHighlightIfMouseover(row);
            }

            Widgets.RadioButton(new Vector2(4f, y + 12f), structure == option);

            Rect textRect = new Rect(34f, y + 2f, inRect.width - 40f, 22f);
            Widgets.Label(textRect, StructureTitle(option, wage));

            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            Widgets.Label(new Rect(34f, y + 24f, inRect.width - 40f, 26f),
                WageStructureUtility.Explain(option, wage, termDays));
            GUI.color = Color.white;

            if (Widgets.ButtonInvisible(row) && structure != option)
            {
                structure = option;
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            }

            return y + RowHeight;
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
