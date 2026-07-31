using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Intercolony
{
    public enum EmploymentStatus
    {
        /// <summary>Hired and paid; the worker is on the road.</summary>
        Travelling,

        /// <summary>On the map, in the player faction, working.</summary>
        Active,

        /// <summary>Term served, worker sent home.</summary>
        Completed,

        /// <summary>Ended early by the player.</summary>
        Dismissed,

        /// <summary>Ended by circumstance — worker died, settlement gone, no map to arrive at.</summary>
        Failed,

        /// <summary>
        /// The worker walked out over unpaid wages (§39 step 5). Distinct from
        /// <see cref="Failed"/> because it is the player's fault and leaves a debt behind, and
        /// Phase 19 must be able to tell the difference.
        /// </summary>
        Quit,

        /// <summary>
        /// Ended because the worker's own faction went to war with the colony (§88, §113).
        ///
        /// Unlike every other terminal status this one is a *transitional* state: the record is
        /// closed but the worker is still walking out under safe passage, so
        /// <see cref="EmploymentContract.pawn"/> stays live until they are off the map. See
        /// <see cref="HostilityPolicy"/>.
        /// </summary>
        Severed
    }

    /// <summary>
    /// Why an employee has downed tools. Needed because two different escalations end in the
    /// same visible state (§39's unpaid wages, §42's combat misuse) and only one of them can be
    /// fixed by handing over silver — a payroll payment must not put a worker back to work who
    /// stopped because you drafted them.
    /// </summary>
    public enum WorkRefusalReason
    {
        None,
        UnpaidWages,
        CombatMisuse
    }

    /// <summary>
    /// One fixed-term employment (DESIGN.md §32, §36.2, §109).
    ///
    /// The pawn is a **quest lodger** for the whole of <see cref="EmploymentStatus.Active"/>:
    /// <see cref="quest"/> carries a <c>QuestPart_ExtraFaction</c> naming <see cref="employerFaction"/>
    /// as the worker's home faction. That is what keeps them out of raid-point maths and stops
    /// <c>SetFaction</c> rewriting their <c>kindDef</c>. See docs/LABOR_TECHNICAL_NOTES.md.
    ///
    /// <see cref="pawn"/> and <see cref="quest"/> are cleared once employment ends, so a finished
    /// record never holds a reference the save cannot resolve.
    /// </summary>
    public class EmploymentContract : IExposable
    {
        public int id;

        public int settlementId;
        public string settlementName = "";
        public string factionName = "";

        /// <summary>Null before arrival and after departure.</summary>
        public Pawn pawn;

        /// <summary>Where the worker goes home to. Held separately because the pawn reference is cleared.</summary>
        public Faction employerFaction;

        /// <summary>
        /// Captured before the transfer as a belt-and-braces restore. Lodger status should make
        /// this unnecessary — the notes explain why it is kept anyway.
        /// </summary>
        public PawnKindDef originalKind;

        public Quest quest;

        /// <summary>Where the worker arrives and works. Also the map that paid.</summary>
        public Map destinationMap;

        /// <summary>Frozen at hire so a completed record still reads correctly after the pawn is gone.</summary>
        public string workerName = "";
        public string workerSkills = "";

        public int dailyWage;

        /// <summary>
        /// Length of the engagement, or **0 for open-ended** (§36.4): the worker stays until one
        /// side ends it under the rules.
        ///
        /// Zero rather than a separate flag because every existing term calculation already reads
        /// this field, and a flag would mean auditing all of them for the case where the two
        /// disagree. <see cref="IsOpenEnded"/> is the readable test.
        /// </summary>
        public int termDays;

        // --- Combat clause (§42, §43, §113) ------------------------------------------------

        /// <summary>What the worker agreed to be pointed at. Priced into <see cref="dailyWage"/>.</summary>
        public CombatClause combatClause = CombatClause.Civilian;

        /// <summary>
        /// Fights the worker was drafted into, whether or not the clause allowed it. §113 asks
        /// for "combat-use tracking where technically feasible", and a security contractor's
        /// count is a record of service rather than of misconduct.
        /// </summary>
        public int combatIncidents;

        /// <summary>Fights outside the clause's terms. Drives the escalation and doubles compensation.</summary>
        public int clauseBreaches;

        /// <summary>
        /// Highest <c>lastAttackTargetTick</c> already counted. Comparing against it is what makes
        /// the sampler idempotent: a reload, or two samples inside one firefight, cannot count the
        /// same shot twice.
        /// </summary>
        public int countedAttackTick = -99999;

        /// <summary>When the last incident was opened, so one skirmish is one incident.</summary>
        public int lastIncidentTick = -99999;

        /// <summary>Permanent injuries the worker already had on arrival. Compensation pays for the difference.</summary>
        public int permanentInjuriesOnArrival;

        /// <summary>Death or injury compensation actually handed over (§43).</summary>
        public int compensationPaid;

        /// <summary>Silver actually handed over so far — the whole term for prepaid, accumulating for periodic.</summary>
        public int paidSilver;

        // --- Payment structure (§37, §38, §39) ---

        public WageStructure wageStructure = WageStructure.Prepaid;

        /// <summary>When the next pay period falls due. -1 for prepaid, or once the term is over.</summary>
        public int nextPaymentTick = -1;

        /// <summary>Wages earned but not paid, because the colony did not have the silver (§39).</summary>
        public int arrearsSilver;

        /// <summary>Consecutive pay periods that could not be met in full. Drives the escalation.</summary>
        public int missedPayments;

        /// <summary>
        /// True once the worker has downed tools over arrears (§39 step 4). The priorities they
        /// had are saved so paying up restores the work plan rather than leaving the player to
        /// rebuild it.
        /// </summary>
        public bool refusingWork;

        /// <summary>Why they stopped. Only <see cref="WorkRefusalReason.UnpaidWages"/> can be paid off.</summary>
        public WorkRefusalReason refusalReason = WorkRefusalReason.None;

        public int hiredTick;
        public int arrivalTick;

        /// <summary>
        /// When the worker actually started, or -1 if they never arrived.
        ///
        /// Distinct from <see cref="arrivalTick"/>, which is the *expected* arrival set at hire and
        /// is a future tick while they travel. Tenure and §43's compensation both need to know when
        /// service began, and "endTick is still -1" used to stand in for "never arrived" — which
        /// stops being true the moment open-ended contracts exist, because those never set one.
        /// </summary>
        public int arrivedTick = NotArrived;

        /// <summary>
        /// Sentinel for <see cref="arrivedTick"/>, compared exactly rather than by sign.
        ///
        /// "Negative means never arrived" is the obvious test and it is wrong: a tick is only
        /// non-negative because the game has been running a while, and any code that constructs a
        /// contract with a backdated start — a self-test, a migration, a future scenario — lands on
        /// a negative tick that means the opposite of what the sign test concludes. Getting that
        /// wrong reads as tenure zero forever, which silently switches off severance, notice growth
        /// and renewal eligibility all at once, with nothing throwing.
        /// </summary>
        public const int NotArrived = -1;

        /// <summary>
        /// When a dismissal notice runs out (§36.4). -1 when none is being served.
        ///
        /// An open-ended worker is not sent home the instant the player clicks: they work out the
        /// notice, or it is paid off. That is what makes open-ended employment a commitment on the
        /// colony's side too, rather than strictly better than a fixed term.
        /// </summary>
        public int noticeEndTick = -1;

        // --- Renewal (§115) ----------------------------------------------------------------

        /// <summary>Set once the renewal question has been raised, so it is asked once per term.</summary>
        public bool renewalOffered;

        /// <summary>The worker did not ask to stay. §115: an ending must never be silent.</summary>
        public bool renewalDeclinedByWorker;

        /// <summary>The player said no. Voluntary non-renewal — they serve out the term and go.</summary>
        public bool renewalDeclinedByPlayer;

        /// <summary>What they want for another term. 0 when no offer is live.</summary>
        public int renewalWage;

        /// <summary>How many terms this worker has signed on for beyond the first.</summary>
        public int renewals;

        /// <summary>Set on arrival: the term runs from the first day of work, not from hiring.</summary>
        public int endTick = -1;

        public EmploymentStatus status = EmploymentStatus.Travelling;
        public string outcomeNote = "";

        /// <summary>
        /// Set once the term has run out while the worker was away from any map (in a caravan).
        /// Stops the "term ended but they are not here" letter repeating every hour.
        /// </summary>
        public bool termLapsedNotified;

        // --- Safe passage (§88, §113) ------------------------------------------------------

        /// <summary>
        /// True while a severed worker is walking out in no faction at all. That is what makes
        /// "they will not be hostile until they are off the map" true rather than a promise: a
        /// factionless pawn is nobody's enemy, so turrets hold their fire.
        /// </summary>
        public bool safePassage;

        /// <summary>
        /// When safe passage runs out. A worker who is still standing in the colony after this —
        /// walled in, downed, or simply blocked — reverts to their now-hostile faction, which is
        /// the stated consequence of not letting them leave.
        /// </summary>
        public int safePassageEndTick = -1;

        /// <summary>Work priorities saved when the worker downed tools, restored when arrears clear.</summary>
        private Dictionary<WorkTypeDef, int> heldPriorities = new Dictionary<WorkTypeDef, int>();

        public EmploymentContract()
        {
        }

        public bool IsOpen => status == EmploymentStatus.Travelling || status == EmploymentStatus.Active;

        public float DaysUntilArrival => (arrivalTick - GenTicks.TicksGame) / (float)GenDate.TicksPerDay;

        /// <summary>§36.4 — no agreed end date. Runs until one side terminates under the rules.</summary>
        public bool IsOpenEnded => termDays <= 0;

        /// <summary>Days actually served so far. 0 before arrival; the basis for §43's severance.</summary>
        public float TenureDays => arrivedTick == NotArrived
            ? 0f
            : Mathf.Max(0f, (GenTicks.TicksGame - arrivedTick) / (float)GenDate.TicksPerDay);

        /// <summary>Whether a dismissal notice is currently running down.</summary>
        public bool ServingNotice => noticeEndTick >= 0;

        public float DaysOfNoticeLeft => noticeEndTick < 0
            ? 0f
            : Mathf.Max(0f, (noticeEndTick - GenTicks.TicksGame) / (float)GenDate.TicksPerDay);

        /// <summary>
        /// Days left on the term. Meaningless for an open-ended contract, which reports
        /// <see cref="float.MaxValue"/> so that every "is this nearly over" test answers no rather
        /// than accidentally answering yes on a zero term.
        /// </summary>
        public float DaysRemaining => IsOpenEnded
            ? float.MaxValue
            : endTick < 0
                ? termDays
                : (endTick - GenTicks.TicksGame) / (float)GenDate.TicksPerDay;

        public float DaysUntilPayment => nextPaymentTick < 0
            ? -1f
            : (nextPaymentTick - GenTicks.TicksGame) / (float)GenDate.TicksPerDay;

        /// <summary>What the whole term costs under the chosen structure (§37).</summary>
        /// <summary>
        /// What the whole term costs (§37). An open-ended contract has no whole term, so this is
        /// its cost per pay period instead — the only figure that means anything for one.
        /// </summary>
        public int TotalCommitment => IsOpenEnded
            ? PeriodPayment
            : WageStructureUtility.TotalCost(wageStructure, dailyWage, termDays);

        /// <summary>Amount a single pay period costs. Zero for prepaid.</summary>
        public int PeriodPayment => WageStructureUtility.PeriodCost(wageStructure, dailyWage);

        /// <summary>
        /// Whether the worker is severed and still on their way out. Not <see cref="IsOpen"/>:
        /// the employment is over, nothing more is earned, and no payroll runs — but the pawn
        /// reference is still live and must be finished off.
        /// </summary>
        public bool IsLeavingUnderSafePassage => status == EmploymentStatus.Severed && pawn != null;

        /// <summary>Whether drafting this worker into a fight right now is within the terms (§42).</summary>
        public bool CombatUsePermittedNow =>
            combatClause.PermitsCombat(CombatClauseUtility.IsOnPlayerHomeMap(pawn));

        public string StatusLine()
        {
            switch (status)
            {
                case EmploymentStatus.Travelling:
                    return $"travelling — arrives in {Mathf.Max(0f, DaysUntilArrival):0.#}d";
                case EmploymentStatus.Active:
                    if (refusingWork)
                    {
                        return refusalReason == WorkRefusalReason.CombatMisuse
                            ? $"REFUSING WORK — drafted into combat {clauseBreaches}x against the clause"
                            : $"REFUSING WORK — {arrearsSilver} silver in arrears";
                    }

                    if (arrearsSilver > 0)
                    {
                        return $"owed {arrearsSilver} silver — {Mathf.Max(0f, DaysRemaining):0.#}d left";
                    }

                    if (termLapsedNotified)
                    {
                        return "term ended — away from the colony, will leave on return";
                    }

                    return wageStructure.IsPeriodic()
                        ? $"working — {Mathf.Max(0f, DaysRemaining):0.#}d left, " +
                          $"next pay in {Mathf.Max(0f, DaysUntilPayment):0.#}d"
                        : $"working — {Mathf.Max(0f, DaysRemaining):0.#}d left";
                case EmploymentStatus.Severed:
                    return pawn == null
                        ? outcomeNote.NullOrEmpty() ? "released — their faction went to war" : outcomeNote
                        : "released — leaving under safe passage";
                default:
                    return outcomeNote.NullOrEmpty() ? status.ToString().ToLower() : outcomeNote;
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", 0);
            Scribe_Values.Look(ref settlementId, "settlementId", -1);
            Scribe_Values.Look(ref settlementName, "settlementName", "");
            Scribe_Values.Look(ref factionName, "factionName", "");

            Scribe_References.Look(ref pawn, "pawn");
            Scribe_References.Look(ref employerFaction, "employerFaction");
            Scribe_References.Look(ref quest, "quest");
            Scribe_References.Look(ref destinationMap, "destinationMap");
            Scribe_Defs.Look(ref originalKind, "originalKind");

            Scribe_Values.Look(ref workerName, "workerName", "");
            Scribe_Values.Look(ref workerSkills, "workerSkills", "");

            Scribe_Values.Look(ref dailyWage, "dailyWage", 0);
            Scribe_Values.Look(ref termDays, "termDays", 0);
            Scribe_Values.Look(ref paidSilver, "paidSilver", 0);

            // Pre-Phase-20 saves have no clause node. Civilian is the right default for them:
            // it is what every existing contract was priced as, so an old save does not
            // retroactively acquire a discount it never paid for.
            Scribe_Values.Look(ref combatClause, "combatClause", CombatClause.Civilian);
            Scribe_Values.Look(ref combatIncidents, "combatIncidents", 0);
            Scribe_Values.Look(ref clauseBreaches, "clauseBreaches", 0);
            Scribe_Values.Look(ref countedAttackTick, "countedAttackTick", -99999);
            Scribe_Values.Look(ref lastIncidentTick, "lastIncidentTick", -99999);
            Scribe_Values.Look(ref permanentInjuriesOnArrival, "permanentInjuriesOnArrival", 0);
            Scribe_Values.Look(ref compensationPaid, "compensationPaid", 0);

            Scribe_Values.Look(ref wageStructure, "wageStructure", WageStructure.Prepaid);
            Scribe_Values.Look(ref nextPaymentTick, "nextPaymentTick", -1);
            Scribe_Values.Look(ref arrearsSilver, "arrearsSilver", 0);
            Scribe_Values.Look(ref missedPayments, "missedPayments", 0);
            Scribe_Values.Look(ref refusingWork, "refusingWork", false);
            Scribe_Values.Look(ref refusalReason, "refusalReason", WorkRefusalReason.None);
            Scribe_Collections.Look(ref heldPriorities, "heldPriorities", LookMode.Def, LookMode.Value);

            Scribe_Values.Look(ref hiredTick, "hiredTick", 0);
            Scribe_Values.Look(ref arrivalTick, "arrivalTick", 0);
            Scribe_Values.Look(ref arrivedTick, "arrivedTick", NotArrived);
            Scribe_Values.Look(ref noticeEndTick, "noticeEndTick", -1);
            Scribe_Values.Look(ref renewalOffered, "renewalOffered", false);
            Scribe_Values.Look(ref renewalDeclinedByWorker, "renewalDeclinedByWorker", false);
            Scribe_Values.Look(ref renewalDeclinedByPlayer, "renewalDeclinedByPlayer", false);
            Scribe_Values.Look(ref renewalWage, "renewalWage", 0);
            Scribe_Values.Look(ref renewals, "renewals", 0);
            Scribe_Values.Look(ref endTick, "endTick", -1);

            Scribe_Values.Look(ref status, "status", EmploymentStatus.Travelling);
            Scribe_Values.Look(ref outcomeNote, "outcomeNote", "");
            Scribe_Values.Look(ref termLapsedNotified, "termLapsedNotified", false);
            Scribe_Values.Look(ref safePassage, "safePassage", false);
            Scribe_Values.Look(ref safePassageEndTick, "safePassageEndTick", -1);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (heldPriorities == null)
                {
                    // A missing dictionary node loads as null, not empty.
                    heldPriorities = new Dictionary<WorkTypeDef, int>();
                }

                // A pre-Phase-20 save has refusingWork but no reason. Everything that could set
                // the flag before this phase was payroll, so that is what it was.
                if (refusingWork && refusalReason == WorkRefusalReason.None)
                {
                    refusalReason = WorkRefusalReason.UnpaidWages;
                }
            }
        }

        /// <summary>
        /// Stops the worker working and remembers what they were doing (§39 step 4).
        ///
        /// The player can set the priorities back — nothing here fights them for it — which is a
        /// deliberate limit rather than an oversight: §39 makes refusal a warning stage on the way
        /// to the worker leaving, not a wall.
        /// </summary>
        public void HoldWork(WorkRefusalReason reason)
        {
            if (refusingWork)
            {
                return;
            }

            // The refusal is a fact about the contract, not about the work tab, so it is recorded
            // before anything is known about the pawn's priorities. It used to be set only as a
            // side effect of successfully zeroing them, which meant a worker with no usable work
            // types got the "they have downed tools" letter while the contract still read as
            // working normally — and the next payroll run would have "resumed" a refusal that was
            // never recorded.
            refusingWork = true;
            refusalReason = reason;
            heldPriorities.Clear();

            if (pawn?.workSettings == null || !pawn.workSettings.EverWork)
            {
                return;
            }

            foreach (WorkTypeDef work in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                int priority = pawn.workSettings.GetPriority(work);
                if (priority > 0)
                {
                    heldPriorities[work] = priority;
                    pawn.workSettings.SetPriority(work, 0);
                }
            }
        }

        /// <summary>
        /// Puts the worker back on the jobs they had before they downed tools.
        ///
        /// <paramref name="becauseOf"/> must match why they stopped. Settling arrears cannot
        /// un-refuse a worker who downed tools because they were drafted into a firefight — there
        /// is nothing to pay, and §42's penalty would otherwise be cancelled by an unrelated
        /// payroll run. <see cref="WorkRefusalReason.None"/> resumes regardless, for the paths
        /// that clear the flag because the employment is ending anyway.
        /// </summary>
        public void ResumeWork(WorkRefusalReason becauseOf = WorkRefusalReason.None)
        {
            if (!refusingWork)
            {
                return;
            }

            if (becauseOf != WorkRefusalReason.None && becauseOf != refusalReason)
            {
                return;
            }

            refusingWork = false;
            refusalReason = WorkRefusalReason.None;

            if (pawn?.workSettings == null || !pawn.workSettings.EverWork)
            {
                heldPriorities.Clear();
                return;
            }

            foreach (KeyValuePair<WorkTypeDef, int> held in heldPriorities)
            {
                if (held.Key != null && !pawn.WorkTypeIsDisabled(held.Key))
                {
                    pawn.workSettings.SetPriority(held.Key, held.Value);
                }
            }

            heldPriorities.Clear();
        }

        /// <summary>
        /// An active contract whose pawn failed to resolve on load is unrecoverable: there is no
        /// employee left to manage. The world component drops these rather than tick them.
        ///
        /// A severed contract mid-safe-passage is *not* dropped when the pawn is missing: the
        /// record has already closed and the only thing left is a walk to the map edge, so losing
        /// the pawn simply means it finished. <see cref="EmploymentService.Advance"/> tidies it.
        /// </summary>
        public bool IsValidAfterLoad =>
            status != EmploymentStatus.Active || pawn != null;

        public override string ToString()
        {
            // Shows the commitment and the structure, not just paidSilver. It used to read
            // "(22/day x 20d = 0)" for a periodic hire, which looks like a zero-value contract
            // rather than one where nothing has been paid yet.
            string money = $"{dailyWage}/day × {termDays}d {wageStructure.Label()}, " +
                           $"{TotalCommitment} total, {paidSilver} paid";
            if (arrearsSilver > 0)
            {
                money += $", {arrearsSilver} owed";
            }

            if (compensationPaid > 0)
            {
                money += $", {compensationPaid} compensation";
            }

            string clause = combatClause.Label();
            if (clauseBreaches > 0)
            {
                clause += $", {clauseBreaches} breach(es)";
            }

            return $"Employment #{id} {workerName} from {settlementName} " +
                   $"({clause}; {money}) [{status}]";
        }
    }
}
