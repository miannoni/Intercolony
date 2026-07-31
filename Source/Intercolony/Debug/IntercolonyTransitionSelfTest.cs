using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// End-to-end check of Phase 23 (DESIGN.md §116, §44).
    ///
    /// §116's acceptance criterion is a constraint, not a feature: *"Conversion is rare/meaningful
    /// and cannot be exploited as cheap recruitment."* So this test spends most of its effort trying
    /// to be the exploit — reaching a conversion cheaply, quickly, or with a bad record — and
    /// asserting that each route is closed.
    ///
    /// It does not convert a live pawn. Doing so would permanently join someone to the colony, which
    /// is not a thing a dev check should do to a save; the pawn-side mechanism is listed in
    /// docs/PENDING_PLAYTESTS.md instead, with what to watch for.
    /// </summary>
    public static class IntercolonyTransitionSelfTest
    {
        private class Results
        {
            public readonly StringBuilder sb = new StringBuilder();
            public int passed;
            public int failed;

            public void Check(bool condition, string label, string detail = null)
            {
                if (condition)
                {
                    passed++;
                    sb.AppendLine($"  PASS  {label}{(detail == null ? "" : $"  ({detail})")}");
                }
                else
                {
                    failed++;
                    sb.AppendLine($"  FAIL  {label}{(detail == null ? "" : $"  ({detail})")}");
                }
            }

            public void Info(string line)
            {
                sb.AppendLine($"        {line}");
            }
        }

        public static string Run(IntercolonyWorldComponent state, Map map)
        {
            Results r = new Results();
            r.sb.AppendLine("Employee-to-colonist transition self-test (§116, §44)");

            if (state == null || map == null)
            {
                r.sb.AppendLine("  No world or map. Open a colony first.");
                return Summarize(r);
            }

            EmployerReputation rep = state.EmployerStanding;
            float savedScore = rep?.Score ?? 0f;
            int savedTransitions = rep?.transitions ?? 0;
            int savedDefections = rep?.defections ?? 0;

            try
            {
                CheckEligibilityGates(r, state);
                CheckFeeCannotBeCheap(r, state, map);
                CheckNegotiation(r, state, map);
                CheckDefectionIsPricedNotFree(r, state);
            }
            catch (System.Exception ex)
            {
                r.sb.AppendLine($"  EXCEPTION: {ex}");
                r.failed++;
            }
            finally
            {
                if (rep != null)
                {
                    rep.Adjust(savedScore - rep.Score);
                    rep.transitions = savedTransitions;
                    rep.defections = savedDefections;
                }

                r.Info($"restored employer standing to {rep?.ScoreDisplay ?? 0}/100.");
            }

            return Summarize(r);
        }

        // --- "Rare" ------------------------------------------------------------------------

        /// <summary>
        /// Every gate, driven one at a time — because "no offer came" is only meaningful if the
        /// player can tell which of their own choices closed it.
        /// </summary>
        private static void CheckEligibilityGates(Results r, IntercolonyWorldComponent state)
        {
            r.Check(TransitionService.RequiredTenureDays == 2 * GenDate.DaysPerQuadrum,
                "attachment takes two quadrums of service (§116)",
                $"{TransitionService.RequiredTenureDays} days");

            // No pawn, so every case exercises the gates rather than the conversion itself.
            EmploymentContract tooNew = Synthetic(40, TransitionService.RequiredTenureDays - 5);
            r.Check(!TransitionService.MeetsTerms(state, tooNew, out string newWhy),
                "a worker short of the tenure bar is not attached",
                Trim(newWhy));

            EmploymentContract owed = Synthetic(40, TransitionService.RequiredTenureDays + 40);
            owed.arrearsSilver = 150;
            r.Check(!TransitionService.MeetsTerms(state, owed, out string owedWhy),
                "a worker still owed wages does not want to stay",
                Trim(owedWhy));

            EmploymentContract late = Synthetic(40, TransitionService.RequiredTenureDays + 40);
            late.missedPayments = 1;
            r.Check(!TransitionService.MeetsTerms(state, late, out string lateWhy),
                "a worker paid late does not want to stay",
                Trim(lateWhy));

            EmploymentContract drafted = Synthetic(40, TransitionService.RequiredTenureDays + 40);
            drafted.clauseBreaches = 1;
            r.Check(!TransitionService.MeetsTerms(state, drafted, out string draftedWhy),
                "a worker drafted against their clause does not want to stay (§42)",
                Trim(draftedWhy));

            EmploymentContract leaving = Synthetic(40, TransitionService.RequiredTenureDays + 40);
            leaving.noticeEndTick = GenTicks.TicksGame + GenDate.TicksPerDay;
            r.Check(!TransitionService.MeetsTerms(state, leaving, out _),
                "a worker under notice is not asked to settle down");

            r.Check(!newWhy.NullOrEmpty() && !owedWhy.NullOrEmpty() && !lateWhy.NullOrEmpty() &&
                    !draftedWhy.NullOrEmpty(),
                "every gate says what is missing rather than failing silently");

            // The tenure message has to be a progress reading, not a flat refusal, or a player
            // cannot tell they are getting closer.
            r.Check(newWhy.Contains("of"),
                "the tenure gate reports progress towards it", Trim(newWhy));
        }

        // --- "Cannot be exploited as cheap recruitment" ------------------------------------

        /// <summary>
        /// §116's constraint, expressed as arithmetic.
        ///
        /// The fee scales with the worker's own wage, which already encodes their skills, passions,
        /// distance and the colony's reputation. So the test that matters is not "is the fee large"
        /// but "is the fee large *for the workers worth having*" — a flat price would be cheap for
        /// exactly the people a player would want to exploit it on.
        /// </summary>
        private static void CheckFeeCannotBeCheap(Results r, IntercolonyWorldComponent state, Map map)
        {
            EmploymentContract cheap = Synthetic(12, 60);
            EmploymentContract dear = Synthetic(80, 60);

            int cheapFee = TransitionService.ReleaseFee(state, cheap);
            int dearFee = TransitionService.ReleaseFee(state, dear);

            r.Check(dearFee > cheapFee,
                "a better worker costs more to keep (§116)",
                $"{cheapFee} for a 12/day worker vs {dearFee} for an 80/day one");

            r.Check(dearFee >= dear.dailyWage * 100,
                "the fee is a serious commitment, not a formality",
                $"{dearFee} silver = {dearFee / dear.dailyWage} days of their wage");

            // The comparison §116 is really about: buying someone must cost far more than employing
            // them, or hiring becomes a down payment on recruitment.
            int aYearOfWages = dear.dailyWage * GenDate.DaysPerYear;
            r.Check(dearFee > aYearOfWages,
                "keeping a worker costs more than a year of employing them (§116)",
                $"{dearFee} to keep vs {aYearOfWages} for a year's wages");

            r.Check(TransitionService.ReleaseFee(state, null) == 0,
                "a missing contract prices at nothing rather than throwing");
        }

        private static void CheckNegotiation(Results r, IntercolonyWorldComponent state, Map map)
        {
            EmploymentContract contract = Synthetic(60, 60);
            int asking = TransitionService.ReleaseFee(state, contract);

            int withNobody = TransitionService.NegotiatedFee(state, contract, null);
            r.Check(withNobody == asking,
                "with no negotiator the price is the asking price (§44)",
                $"{withNobody}");

            Pawn best = TransitionService.BestNegotiator(map);
            if (best == null)
            {
                r.Info("negotiation skipped: no free colonist with Social available.");
                return;
            }

            int negotiated = TransitionService.NegotiatedFee(state, contract, best);
            int social = best.skills.GetSkill(SkillDefOf.Social).Level;

            r.Check(negotiated <= asking,
                "negotiating never raises the price (§44)",
                $"{best.LabelShortCap} (Social {social}): {asking} -> {negotiated}");

            if (social > 0)
            {
                r.Check(negotiated < asking,
                    "a negotiator with any Social at all saves something",
                    $"saved {asking - negotiated}");
            }

            // The ceiling matters as much as the effect: negotiation must not be able to make
            // conversion cheap, only cheaper.
            float floor = 1f - TransitionService.MaxNegotiationDiscount;
            r.Check(negotiated >= Mathf.RoundToInt(asking * floor) - 1,
                "negotiation is capped, so talking cannot make it cheap (§116)",
                $"floor is {floor:P0} of asking");

            r.Check(!TransitionService.IsSettledFormerEmployee(best) || !EmploymentService.IsEmployee(best),
                "an employee is not used to negotiate their own release");
        }

        /// <summary>
        /// §44's defection outcome must cost more than the fee, in a currency the player cannot farm.
        ///
        /// If refusing to pay were merely a reputation scratch it would be the obvious route every
        /// time, and §116's constraint would be decoration.
        /// </summary>
        private static void CheckDefectionIsPricedNotFree(Results r, IntercolonyWorldComponent state)
        {
            EmployerReputation rep = state.EmployerStanding;
            if (rep == null)
            {
                return;
            }

            EmploymentContract contract = Synthetic(50, 60);
            contract.transitionOffered = true;

            float before = rep.Score;
            EmployerReputationService.NoteDefection(state, contract);
            float afterDefect = rep.Score;

            rep.Adjust(before - rep.Score);
            EmployerReputationService.NoteTransitionSettled(state, contract);
            float afterSettle = rep.Score;
            rep.Adjust(before - rep.Score);

            r.Check(afterDefect < before,
                "defecting damages the colony's name as an employer (§44)",
                $"{before:0} -> {afterDefect:0}");

            r.Check(afterSettle > before,
                "settling properly improves it",
                $"{before:0} -> {afterSettle:0}");

            r.Check(before - afterDefect > afterSettle - before,
                "defection costs more than settling gains (§116)",
                $"-{before - afterDefect:0} against +{afterSettle - before:0}");

            r.Check(rep.defections == 1 && rep.transitions == 2,
                "both outcomes are counted, and only one as a defection",
                $"{rep.transitions} transitions, {rep.defections} defections");
        }

        // --- Helpers -----------------------------------------------------------------------

        /// <summary>
        /// A contract with no pawn, at a chosen tenure. Pawnless on purpose: every gate this test
        /// drives is meant to answer before the pawn matters, and a test that joined someone to the
        /// colony would be a dev check with a permanent side effect.
        /// </summary>
        private static EmploymentContract Synthetic(int dailyWage, int tenureDays)
        {
            return new EmploymentContract
            {
                id = -870,
                settlementName = "Testholme",
                factionName = "Test Confederacy",
                workerName = "Probe",
                workerSkills = "none",
                dailyWage = dailyWage,
                termDays = 0,
                combatClause = CombatClause.Civilian,
                wageStructure = WageStructure.Daily,
                hiredTick = GenTicks.TicksGame - tenureDays * GenDate.TicksPerDay,
                arrivalTick = GenTicks.TicksGame - tenureDays * GenDate.TicksPerDay,
                arrivedTick = GenTicks.TicksGame - tenureDays * GenDate.TicksPerDay,
                status = EmploymentStatus.Active
            };
        }

        private static string Trim(string text)
        {
            if (text.NullOrEmpty())
            {
                return "";
            }

            string flat = text.Replace("\n", " ");
            return flat.Length <= 70 ? flat : flat.Substring(0, 70) + "...";
        }

        private static string Summarize(Results r)
        {
            r.sb.AppendLine();
            r.sb.AppendLine($"  {r.passed} passed, {r.failed} failed.");
            return r.sb.ToString();
        }
    }
}
