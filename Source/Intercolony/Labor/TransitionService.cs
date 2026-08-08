using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// A long-serving employee who wants to stay for good (DESIGN.md §44, §116).
    ///
    /// §116's acceptance criterion is a constraint rather than a feature: *"Conversion is
    /// rare/meaningful and cannot be exploited as cheap recruitment."* Three things enforce it, and
    /// the third does most of the work:
    ///
    /// * **Time.** Two quadrums of service before anyone asks.
    /// * **Conduct.** A spotless record — no late wages, no arrears, no clause breach. The same
    ///   record §115's renewal reads, because attachment is renewal's larger sibling.
    /// * **Price.** The release fee scales with the worker's own market rate, so the people worth
    ///   converting are precisely the expensive ones. A brilliant mason costs what a brilliant mason
    ///   is worth; a mediocre hauler is cheap and not worth the trouble. A flat fee would have made
    ///   this a shop.
    ///
    /// §44 lists five outcomes and all five exist: pay the fee, negotiate it down with Social, the
    /// faction agrees, the worker defects and diplomacy suffers, or decline.
    /// </summary>
    public static class TransitionService
    {
        /// <summary>
        /// Service before a worker grows attached. Two quadrums (§116's "rare and meaningful").
        ///
        /// Chosen over a full year deliberately: long enough that both sides have committed to each
        /// other and short enough that a player running the labour system well will actually see it,
        /// with the release fee — not the wait — doing the work of stopping it being cheap.
        /// </summary>
        public const int RequiredTenureDays = 2 * GenDate.DaysPerQuadrum;

        /// <summary>Days of the worker's own wage their faction wants to release them.</summary>
        public const int ReleaseFeeDays = 180;

        /// <summary>Best discount a negotiator can talk them down by, at Social 20.</summary>
        public const float MaxNegotiationDiscount = 0.35f;

        /// <summary>Days before a declined offer can be raised again.</summary>
        public const int ReofferAfterDays = 30;

        // --- Eligibility (§44 "after long positive employment") ----------------------------

        /// <summary>
        /// Whether this worker has grown attached, and if not, what is missing.
        ///
        /// The reason string is for the Labor tab rather than a letter — a player who is *trying*
        /// to reach this should be able to see how far off they are, because a rare outcome nobody
        /// can see approaching is indistinguishable from one that does not exist.
        /// </summary>
        public static bool IsEligible(IntercolonyWorldComponent state, EmploymentContract contract,
            out string reason)
        {
            reason = null;

            if (contract?.pawn == null || !contract.pawn.Spawned)
            {
                reason = "Not currently working here.";
                return false;
            }

            return MeetsTerms(state, contract, out reason);
        }

        /// <summary>
        /// The record half of eligibility — everything that is a fact about the employment rather
        /// than about the pawn standing on the map.
        ///
        /// Split out because it is the half worth testing, and testing it should not require joining
        /// a real pawn to the colony. Every gate §116's "rare and meaningful" depends on lives here.
        /// </summary>
        public static bool MeetsTerms(IntercolonyWorldComponent state, EmploymentContract contract,
            out string reason)
        {
            reason = null;

            if (contract == null || contract.status != EmploymentStatus.Active)
            {
                reason = "Not currently employed here.";
                return false;
            }

            if (contract.ServingNotice)
            {
                reason = "They are working out their notice.";
                return false;
            }

            if (contract.TenureDays < RequiredTenureDays)
            {
                reason = $"Served {contract.TenureDays:0} of {RequiredTenureDays} days.";
                return false;
            }

            if (contract.arrearsSilver > 0)
            {
                reason = $"They are owed {contract.arrearsSilver} silver.";
                return false;
            }

            if (contract.missedPayments > 0)
            {
                reason = "Their wages were not paid on time.";
                return false;
            }

            if (contract.clauseBreaches > 0)
            {
                reason = $"Drafted into combat {contract.clauseBreaches} time(s) against their clause.";
                return false;
            }

            if (EmployerReputationService.ScoreFor(state) < RenewalService.MinimumStandingToRenew)
            {
                reason = "Your standing as an employer is not one people settle down under.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Raises the question once a worker qualifies. Called from the employment beat.
        /// </summary>
        public static void Advance(IntercolonyWorldComponent state, EmploymentContract contract)
        {
            if (contract == null || contract.transitionResolved)
            {
                return;
            }

            // A declined offer comes back around, but not immediately — the worker is not going to
            // ask again the following morning.
            if (contract.transitionOfferedTick >= 0)
            {
                bool cooledOff = GenTicks.TicksGame - contract.transitionOfferedTick >=
                                 ReofferAfterDays * GenDate.TicksPerDay;
                if (!cooledOff)
                {
                    return;
                }
            }

            if (!IsEligible(state, contract, out _))
            {
                return;
            }

            contract.transitionOfferedTick = GenTicks.TicksGame;
            contract.transitionOffered = true;

            int fee = ReleaseFee(state, contract);

            IntercolonyLetters.Send(
                IntercolonyLetterImportance.Important,
                $"{contract.workerName} has grown attached",
                $"{contract.workerName} has worked here for {contract.TenureDays:0} days and would " +
                "like to remain permanently.\n\n" +
                $"{contract.factionName} will release them from their obligations for " +
                $"{fee} silver. A good negotiator can talk that figure down.\n\n" +
                "Answer in the Labor tab under Employees. They will keep working either way — this " +
                "is not a threat to leave.",
                LetterDefOf.PositiveEvent, contract.pawn);

            IntercolonyLog.Message($"Attachment offer raised: {contract} (fee {fee})");
        }

        // --- The fee (§44 "pay release fee", §116 "cannot be exploited") -------------------

        /// <summary>
        /// What the home faction wants, before negotiation.
        ///
        /// **Scaled to the worker rather than flat, and that is the anti-exploit mechanism.** Their
        /// daily wage already encodes skills, passions, distance and the colony's reputation
        /// (Phase 16 through 19 all feed it), so the fee tracks all of it for free — and the workers
        /// a player most wants to keep are exactly the ones they can least afford to buy.
        ///
        /// Goodwill shifts it: a faction that likes you parts with a citizen more easily than one
        /// that tolerates you.
        /// </summary>
        public static int ReleaseFee(IntercolonyWorldComponent state, EmploymentContract contract)
        {
            if (contract == null)
            {
                return 0;
            }

            float fee = contract.dailyWage * ReleaseFeeDays;
            fee *= GoodwillFactor(contract.employerFaction);

            return Mathf.Max(1, Mathf.RoundToInt(fee));
        }

        /// <summary>
        /// How much the source faction's regard moves the price. Guarded rather than trusting
        /// <c>PlayerGoodwill</c>: a faction with no relation entry makes that path throw, which
        /// Phase 20 found the hard way.
        /// </summary>
        private static float GoodwillFactor(Faction faction)
        {
            if (faction == null || faction.IsPlayer ||
                faction.RelationWith(Faction.OfPlayer, allowNull: true) == null)
            {
                return 1f;
            }

            return Mathf.Lerp(1.3f, 0.8f, Mathf.InverseLerp(-100f, 100f, faction.PlayerGoodwill));
        }

        /// <summary>
        /// The best negotiator the colony has, or null.
        ///
        /// Free colonists only — an employee cannot argue their own release, and a prisoner is not
        /// speaking for the colony.
        /// </summary>
        public static Pawn BestNegotiator(Map map)
        {
            if (map == null)
            {
                return null;
            }

            Pawn best = null;
            int bestLevel = -1;

            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if (pawn == null || pawn.Dead || pawn.skills == null || EmploymentService.IsEmployee(pawn))
                {
                    continue;
                }

                SkillRecord social = pawn.skills.GetSkill(SkillDefOf.Social);
                if (social == null || social.TotallyDisabled)
                {
                    continue;
                }

                if (social.Level > bestLevel)
                {
                    bestLevel = social.Level;
                    best = pawn;
                }
            }

            return best;
        }

        /// <summary>What the fee comes down to with this negotiator arguing (§44 "negotiate using Social").</summary>
        public static int NegotiatedFee(IntercolonyWorldComponent state, EmploymentContract contract,
            Pawn negotiator)
        {
            int fee = ReleaseFee(state, contract);
            if (negotiator?.skills == null)
            {
                return fee;
            }

            SkillRecord social = negotiator.skills.GetSkill(SkillDefOf.Social);
            if (social == null || social.TotallyDisabled)
            {
                return fee;
            }

            float discount = Mathf.Lerp(0f, MaxNegotiationDiscount, Mathf.Clamp01(social.Level / 20f));
            return Mathf.Max(1, Mathf.RoundToInt(fee * (1f - discount)));
        }

        // --- Outcomes (§44) ----------------------------------------------------------------

        /// <summary>
        /// Settles with the home faction and keeps the worker. The clean ending.
        /// </summary>
        public static bool TrySettle(IntercolonyWorldComponent state, EmploymentContract contract,
            Pawn negotiator, Map map, out string failReason)
        {
            failReason = null;

            if (contract == null || !contract.transitionOffered || contract.transitionResolved)
            {
                failReason = "There is no offer to settle.";
                return false;
            }

            map = map ?? contract.destinationMap ?? Find.AnyPlayerHomeMap;
            int fee = NegotiatedFee(state, contract, negotiator);

            int available = PurchaseOrderService.CountColonySilver(map);
            if (available < fee)
            {
                failReason = $"Not enough silver in storage: {available} of {fee} needed.";
                return false;
            }

            if (!PurchaseOrderService.TryTakeSilver(map, fee))
            {
                failReason = "Could not collect the silver.";
                return false;
            }

            string worker = contract.workerName;
            string faction = contract.factionName;

            LedgerService.Record(state, LedgerKind.ReleaseFee, -fee, contract.settlementName,
                $"{worker} released permanently");

            EmployerReputationService.NoteTransitionSettled(state, contract);
            Convert(contract, $"{worker} was released by {faction} for {fee} silver and stayed");

            IntercolonyLetters.Send(
                IntercolonyLetterImportance.Important,
                $"{worker} has joined the colony",
                $"{fee} silver has been paid to {faction}, and {worker} is released from their " +
                "obligations to them.\n\n" +
                "They are a colonist now — no wage, no term, no going home.\n\n" +
                (negotiator != null
                    ? $"{negotiator.LabelShortCap} negotiated the price down.\n\n"
                    : "Nobody was available to negotiate the price.\n\n") +
                // Named because it happens either way and the player will see the goodwill move.
                // A letter that reads as an unqualified success while relations quietly drop is the
                // kind of small dishonesty that makes a player distrust every other letter.
                $"{faction} think less of you for it regardless — they are a citizen short, bought " +
                "and paid for or not.",
                LetterDefOf.PositiveEvent);

            return true;
        }

        /// <summary>
        /// Keeps the worker without settling (§44 "pawn defects, causing diplomacy consequences").
        ///
        /// Deliberately available and deliberately expensive. §116 says conversion must not be cheap
        /// recruitment, and the cheapest possible route — simply not paying — is priced in the one
        /// currency a player cannot quietly farm: a faction's willingness to deal with them at all.
        /// The goodwill hit is large enough to make war a real possibility, and §88's policy then
        /// takes over the wreckage.
        /// </summary>
        public static void Defect(IntercolonyWorldComponent state, EmploymentContract contract)
        {
            if (contract == null || !contract.transitionOffered || contract.transitionResolved)
            {
                return;
            }

            string worker = contract.workerName;
            string faction = contract.factionName;
            Faction employer = contract.employerFaction;

            EmployerReputationService.NoteDefection(state, contract);
            Convert(contract, $"{worker} stayed without {faction} being paid off");

            bool nowHostile = HostilityPolicy.IsAtWar(employer);

            IntercolonyLetters.Send(
                IntercolonyLetterImportance.Always,
                $"{worker} has stayed",
                $"{worker} is a colonist now. {faction} was not paid, and considers them stolen.\n\n" +
                (nowHostile
                    ? $"{faction} is now hostile. Everything you had booked with them is void."
                    : $"{faction}'s opinion of you has fallen sharply. Another incident like this " +
                      "and they will be at war with you."),
                nowHostile ? LetterDefOf.ThreatBig : LetterDefOf.NegativeEvent);

            // The trade half of §88 acts on the same beat rather than an hour later, so a player who
            // just started a war finds out what it cost them immediately.
            if (nowHostile)
            {
                HostilityPolicy.Sweep(state);
            }
        }

        /// <summary>
        /// Turns the offer down. They keep working under the contract they have, and may ask again
        /// after a while — §44 lists declining as an outcome, not an ending.
        /// </summary>
        public static void Decline(EmploymentContract contract)
        {
            if (contract == null || !contract.transitionOffered)
            {
                return;
            }

            contract.transitionOffered = false;

            Messages.Message(
                $"{contract.workerName} will stay on as an employee.",
                MessageTypeDefOf.NeutralEvent, historical: false);
        }

        public static bool HasLiveOffer(EmploymentContract contract)
        {
            return contract != null &&
                   contract.status == EmploymentStatus.Active &&
                   contract.transitionOffered &&
                   !contract.transitionResolved;
        }

        // --- Making them a colonist --------------------------------------------------------

        /// <summary>
        /// Turns the employee into a permanent colonist, in place.
        ///
        /// **The one part of this phase that had to be got exactly right.** The worker is already in
        /// the player faction — that is how they work at all — so joining is not a faction change.
        /// It is the *removal of lodger status*, and lodger status is the quest.
        ///
        /// Ending the quest normally would send them home, because <c>QuestPart_Leave</c> has
        /// <c>leaveOnCleanup</c> set: that is exactly how every other departure in this mod works.
        /// So the pawn is taken out of that part's list first, and only then is the quest ended.
        /// <c>QuestPart_ExtraFaction.Cleanup</c> is safe to run — it only sets a relations-gain
        /// cooldown — and once the quest is no longer <c>Ongoing</c>,
        /// <c>QuestUtility.IsQuestLodger</c> goes false because it resolves through
        /// <c>HasExtraHomeFaction</c>. The pawn is then a colonist by every test the game applies:
        /// threat points count them, caravans take them, and nothing is left holding a claim.
        ///
        /// Deliberately **not** routed through <see cref="EmploymentService.End"/>, which restores
        /// the original <c>kindDef</c> and sends the worker home — both correct for a departure and
        /// both wrong here.
        /// </summary>
        private static void Convert(EmploymentContract contract, string note)
        {
            Pawn worker = contract.pawn;
            Quest quest = contract.quest;

            IntercolonyWorldComponent state = IntercolonyWorldComponent.Current;

            // Wages up to today are still owed — joining is not a way to skip a payday.
            PayrollService.SettleOnEnd(contract, EmploymentStatus.Completed, state?.LaborDebts, state);
            CompensationService.ClaimOnEnd(state, contract);

            contract.status = EmploymentStatus.Converted;
            contract.outcomeNote = note ?? "";
            contract.transitionResolved = true;
            contract.transitionOffered = false;
            contract.refusingWork = false;
            contract.refusalReason = WorkRefusalReason.None;

            try
            {
                if (quest != null && !quest.Historical)
                {
                    foreach (QuestPart part in quest.PartsListForReading)
                    {
                        // Take them off the departure list before it can act on them. Leaving the
                        // part in place with an empty list is safer than disabling it wholesale:
                        // MakePawnsLeave on nobody is a no-op.
                        if (part is QuestPart_Leave leave)
                        {
                            leave.pawns.Remove(worker);
                        }

                        if (part is QuestPart_ExtraFaction extra)
                        {
                            extra.affectedPawns.Remove(worker);
                        }
                    }

                    quest.End(QuestEndOutcome.Success, sendLetter: false, playSound: false);
                }
            }
            catch (System.Exception ex)
            {
                IntercolonyLog.Warning($"Employment #{contract.id} threw while converting: {ex}");
            }

            // Now genuinely one of the colony's own. SetFaction is not called — they are already in
            // the player faction — so the kind has to be set by hand, which is what SetFaction would
            // have done for anyone joining who was not a lodger.
            if (worker != null && !worker.Destroyed && Faction.OfPlayer?.def?.basicMemberKind != null)
            {
                worker.kindDef = Faction.OfPlayer.def.basicMemberKind;
            }

            contract.pawn = null;
            contract.quest = null;
            contract.destinationMap = null;

            IntercolonyLog.Message($"Converted to colonist: {contract} — {note}");
        }

        /// <summary>
        /// Whether this pawn is a former employee who settled here. Used by the self-test to prove
        /// the conversion actually took, rather than trusting that it did.
        /// </summary>
        public static bool IsSettledFormerEmployee(Pawn pawn)
        {
            return pawn != null &&
                   pawn.Faction == Faction.OfPlayer &&
                   !pawn.IsQuestLodger() &&
                   !EmploymentService.IsEmployee(pawn);
        }
    }
}
