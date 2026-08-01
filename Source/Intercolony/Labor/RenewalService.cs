using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Whether a finishing engagement carries on (DESIGN.md §115, §36.3, §40).
    ///
    /// §115's acceptance criterion is a single sentence and it is the whole point: *"Neither
    /// employment nor supply agreements end by silently lapsing."* So every ending is announced,
    /// and every ending that could have been avoided says what would have avoided it.
    ///
    /// **The worker asks; the player answers.** That direction is deliberate. A renewal the player
    /// simply buys would make §40's reputation decorative — you could treat people badly and keep
    /// them by paying more. Making the *offer itself* conditional on conduct is what turns a season
    /// of paying on time into something with a payoff, and it means the reward for being a decent
    /// employer is continuity rather than a discount.
    ///
    /// The supply half lives in <see cref="ContractService"/> because it needs that file's cycle
    /// bookkeeping, but it answers the same question the same way — see
    /// <see cref="WouldRenew(IntercolonyWorldComponent, EmploymentContract, out string)"/> for the
    /// employment rule and its commercial twin for agreements.
    /// </summary>
    public static class RenewalService
    {
        /// <summary>
        /// How far ahead of the end a worker raises it. Long enough to answer without pausing the
        /// game in a panic, short enough that they are clearly near the end of the job.
        /// </summary>
        public const int OfferLeadDays = 5;

        /// <summary>
        /// Employer standing below which nobody offers to stay on.
        ///
        /// Set at the bottom of §40's "Decent" band rather than at neutral: a colony has to be
        /// actively poor to work for before people stop wanting to come back, not merely unproven.
        /// </summary>
        public const float MinimumStandingToRenew = 40f;

        /// <summary>
        /// What a returning worker charges, relative to their current wage.
        ///
        /// Slightly more, always. They know the job now, they know the colony wants them, and a
        /// renewal that cost the same would make long employment strictly cheaper than rehiring —
        /// which would quietly make the Hire tab and job postings pointless for anyone with one
        /// good worker.
        /// </summary>
        public const float RenewalWagePremium = 1.05f;

        // --- Employment (§36.3, §115) ------------------------------------------------------

        /// <summary>
        /// Raises the renewal question once, a few days before a term ends.
        ///
        /// Called from the employment beat. Open-ended contracts never reach this: there is nothing
        /// to renew, which is the point of them.
        /// </summary>
        public static void Advance(EmploymentContract contract)
        {
            if (contract == null || contract.status != EmploymentStatus.Active ||
                contract.IsOpenEnded || contract.renewalOffered || contract.ServingNotice)
            {
                return;
            }

            if (contract.DaysRemaining > OfferLeadDays)
            {
                return;
            }

            contract.renewalOffered = true;

            IntercolonyWorldComponent state = IntercolonyWorldComponent.Current;
            if (!WouldRenew(state, contract, out string refusal))
            {
                // §115: never silently. A term that is about to run out with no offer coming says
                // so, and says why, while the player can still do something about the next one.
                contract.renewalDeclinedByWorker = true;

                Find.LetterStack.ReceiveLetter(
                    "No renewal offered",
                    $"{contract.workerName}'s term ends in {Mathf.Max(0f, contract.DaysRemaining):0.#} days, " +
                    "and they have not asked to stay on.\n\n" + refusal,
                    LetterDefOf.NeutralEvent, contract.pawn);
                return;
            }

            contract.renewalWage = RenewalWage(contract);

            Find.LetterStack.ReceiveLetter(
                $"{contract.workerName} would like to stay",
                $"{contract.workerName}'s {contract.termDays}-day term ends in " +
                $"{Mathf.Max(0f, contract.DaysRemaining):0.#} days, and they have asked to stay on.\n\n" +
                $"They will sign another {contract.termDays} days at {contract.renewalWage} silver a day " +
                $"— they are on {contract.dailyWage} now.\n\n" +
                "Answer in the Labor tab under Employees. If you do nothing they go home when the " +
                "term ends.",
                LetterDefOf.PositiveEvent, contract.pawn);
        }

        /// <summary>
        /// Whether this worker wants to come back, and if not, what stopped them.
        ///
        /// Reads §40's record rather than a new one. Everything here is conduct the player chose:
        /// paying late, drafting someone who did not agree to fight, letting arrears run. A worker
        /// who was treated properly wants to stay; that is the whole mechanism.
        /// </summary>
        public static bool WouldRenew(IntercolonyWorldComponent state, EmploymentContract contract,
            out string reason)
        {
            reason = null;

            if (contract == null)
            {
                reason = "No contract.";
                return false;
            }

            if (contract.arrearsSilver > 0)
            {
                reason = $"They are still owed {contract.arrearsSilver} silver. Nobody signs on again " +
                         "with the last term unpaid.";
                return false;
            }

            if (contract.missedPayments > 0)
            {
                reason = "Their wages were not paid on time this term.";
                return false;
            }

            if (contract.clauseBreaches > 0)
            {
                reason = $"You drafted them into combat {contract.clauseBreaches} time(s) against the " +
                         $"terms of their {contract.combatClause.Label()} contract.";
                return false;
            }

            float standing = EmployerReputationService.ScoreFor(state);
            if (standing < MinimumStandingToRenew)
            {
                reason = $"Your standing as an employer ({state?.EmployerStanding?.TierLabel().ToLower()}) " +
                         "is not one people sign on again for.";
                return false;
            }

            // A worker who barely got here is not being asked to commit to another stretch — the
            // question only makes sense once they have actually done the job for a while.
            if (contract.TenureDays < 3f)
            {
                reason = "They were barely here long enough to judge.";
                return false;
            }

            return true;
        }

        public static int RenewalWage(EmploymentContract contract)
        {
            return Mathf.Max(1, Mathf.RoundToInt(contract.dailyWage * RenewalWagePremium));
        }

        /// <summary>
        /// Takes the worker up on it. The term restarts; the same pawn stays where they are.
        ///
        /// Nothing about the pawn changes — no departure, no second arrival, no faction round trip.
        /// That matters for §115's other acceptance criterion, *"employees can remain for long
        /// periods without faction-state drift"*: the safest way not to drift across a renewal is
        /// not to touch the faction at all.
        /// </summary>
        public static bool Accept(EmploymentContract contract, out string failReason)
        {
            failReason = null;

            if (contract == null || contract.status != EmploymentStatus.Active ||
                !contract.renewalOffered || contract.renewalDeclinedByWorker)
            {
                failReason = "There is no offer to accept.";
                return false;
            }

            int newWage = contract.renewalWage > 0 ? contract.renewalWage : RenewalWage(contract);

            contract.dailyWage = newWage;
            contract.endTick = GenTicks.TicksGame + contract.termDays * GenDate.TicksPerDay;
            contract.renewals++;

            // Re-armed for the next term rather than cleared, so a worker on their third renewal
            // is asked again rather than staying forever by accident.
            contract.renewalOffered = false;
            contract.renewalWage = 0;
            contract.termLapsedNotified = false;

            EmployerReputationService.NoteRenewal(IntercolonyWorldComponent.Current, contract);

            Messages.Message(
                $"{contract.workerName} has signed on for another {contract.termDays} days at " +
                $"{newWage} silver a day.",
                MessageTypeDefOf.PositiveEvent, historical: false);

            IntercolonyLog.Message($"Renewed: {contract}");
            return true;
        }

        /// <summary>
        /// Turns the offer down. §115 calls this voluntary non-renewal and it is not a dismissal —
        /// the worker serves out the term they agreed to and goes home on time.
        /// </summary>
        public static void Decline(EmploymentContract contract)
        {
            if (contract == null || !contract.renewalOffered)
            {
                return;
            }

            contract.renewalDeclinedByPlayer = true;
            contract.renewalWage = 0;

            Messages.Message(
                $"{contract.workerName} will go home when their term ends.",
                MessageTypeDefOf.NeutralEvent, historical: false);
        }

        /// <summary>Whether the Labor tab should be showing a renewal decision for this contract.</summary>
        public static bool HasLiveOffer(EmploymentContract contract)
        {
            return contract != null &&
                   contract.status == EmploymentStatus.Active &&
                   contract.renewalOffered &&
                   !contract.renewalDeclinedByWorker &&
                   !contract.renewalDeclinedByPlayer &&
                   !contract.ServingNotice;
        }

        // --- Termination of an open-ended contract (§36.4) ---------------------------------

        /// <summary>
        /// Days of notice owed, growing with service.
        ///
        /// §36.4 says an open-ended worker stays "until either side terminates under rules", and
        /// this is the colony's side of those rules. Without it, open-ended employment would be
        /// strictly better for the player than any fixed term — all of the flexibility and none of
        /// the commitment — which would make §36.2 and §36.3 dead options.
        /// </summary>
        public static int NoticeDays(EmploymentContract contract)
        {
            if (contract == null || !contract.IsOpenEnded)
            {
                return 0;
            }

            return Mathf.Clamp(Mathf.RoundToInt(contract.TenureDays / 6f), 3, 20);
        }

        /// <summary>Silver to end it today instead of working the notice out.</summary>
        public static int PayInLieu(EmploymentContract contract)
        {
            return NoticeDays(contract) * (contract?.dailyWage ?? 0);
        }

        /// <summary>
        /// Starts the notice running. The worker keeps working and keeps being paid until it ends.
        /// </summary>
        public static void GiveNotice(EmploymentContract contract)
        {
            if (contract == null || contract.status != EmploymentStatus.Active || contract.ServingNotice)
            {
                return;
            }

            int days = NoticeDays(contract);
            contract.noticeEndTick = GenTicks.TicksGame + days * GenDate.TicksPerDay;

            // A worker under notice is not going to be asked to stay on.
            contract.renewalOffered = true;
            contract.renewalDeclinedByPlayer = true;

            Find.LetterStack.ReceiveLetter(
                "Notice given",
                $"{contract.workerName} has been given {days} days' notice.\n\n" +
                "They keep working and keep drawing wages until it runs out, then go home. Nothing " +
                "is owed beyond the wages for those days.",
                LetterDefOf.NeutralEvent, contract.pawn);

            IntercolonyLog.Message($"Notice given ({days}d): {contract}");
        }

        /// <summary>
        /// Pays the notice out and ends the employment today.
        ///
        /// Costs the same as working it out, so this is a convenience rather than a saving — the
        /// choice is whether the colony wants the labour or the silver.
        /// </summary>
        public static bool TryPayInLieu(EmploymentContract contract, Map map, out string failReason)
        {
            failReason = null;

            if (contract == null || contract.status != EmploymentStatus.Active)
            {
                failReason = "Nothing to end.";
                return false;
            }

            int owed = PayInLieu(contract);
            map = map ?? contract.destinationMap ?? Find.AnyPlayerHomeMap;

            int available = PurchaseOrderService.CountColonySilver(map);
            if (available < owed)
            {
                failReason = $"Not enough silver in storage: {available} of {owed} needed.";
                return false;
            }

            if (owed > 0 && !PurchaseOrderService.TryTakeSilver(map, owed))
            {
                failReason = "Could not collect the silver.";
                return false;
            }

            contract.paidSilver += owed;

            LedgerService.Record(LedgerKind.WagePayment, -owed, contract.settlementName,
                $"{contract.workerName}, {NoticeDays(contract)}d notice paid in lieu");

            EmploymentService.End(contract, EmploymentStatus.Dismissed,
                $"{contract.workerName} was dismissed with {NoticeDays(contract)} days paid in lieu of notice");

            return true;
        }

        /// <summary>
        /// Ends it today and pays nothing. Legal, and remembered (§40).
        ///
        /// Deliberately available. §36.4's rules are meant to price a decision, not remove it — a
        /// colony in real trouble should be able to let someone go this afternoon and wear the
        /// consequence rather than be blocked by a dialog.
        /// </summary>
        public static void DismissWithoutNotice(EmploymentContract contract)
        {
            if (contract == null || contract.status != EmploymentStatus.Active)
            {
                return;
            }

            EmployerReputationService.NoteNoticeSkipped(IntercolonyWorldComponent.Current, contract);

            EmploymentService.End(contract, EmploymentStatus.Dismissed,
                $"{contract.workerName} was dismissed without the {NoticeDays(contract)} days' notice owed");
        }
    }
}
