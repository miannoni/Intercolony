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
        Quit
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
        public int termDays;

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

        public int hiredTick;
        public int arrivalTick;

        /// <summary>Set on arrival: the term runs from the first day of work, not from hiring.</summary>
        public int endTick = -1;

        public EmploymentStatus status = EmploymentStatus.Travelling;
        public string outcomeNote = "";

        /// <summary>
        /// Set once the term has run out while the worker was away from any map (in a caravan).
        /// Stops the "term ended but they are not here" letter repeating every hour.
        /// </summary>
        public bool termLapsedNotified;

        /// <summary>Work priorities saved when the worker downed tools, restored when arrears clear.</summary>
        private Dictionary<WorkTypeDef, int> heldPriorities = new Dictionary<WorkTypeDef, int>();

        public EmploymentContract()
        {
        }

        public bool IsOpen => status == EmploymentStatus.Travelling || status == EmploymentStatus.Active;

        public float DaysUntilArrival => (arrivalTick - GenTicks.TicksGame) / (float)GenDate.TicksPerDay;

        public float DaysRemaining => endTick < 0
            ? termDays
            : (endTick - GenTicks.TicksGame) / (float)GenDate.TicksPerDay;

        public float DaysUntilPayment => nextPaymentTick < 0
            ? -1f
            : (nextPaymentTick - GenTicks.TicksGame) / (float)GenDate.TicksPerDay;

        /// <summary>What the whole term costs under the chosen structure (§37).</summary>
        public int TotalCommitment => WageStructureUtility.TotalCost(wageStructure, dailyWage, termDays);

        /// <summary>Amount a single pay period costs. Zero for prepaid.</summary>
        public int PeriodPayment => WageStructureUtility.PeriodCost(wageStructure, dailyWage);

        public string StatusLine()
        {
            switch (status)
            {
                case EmploymentStatus.Travelling:
                    return $"travelling — arrives in {Mathf.Max(0f, DaysUntilArrival):0.#}d";
                case EmploymentStatus.Active:
                    if (refusingWork)
                    {
                        return $"REFUSING WORK — {arrearsSilver} silver in arrears";
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

            Scribe_Values.Look(ref wageStructure, "wageStructure", WageStructure.Prepaid);
            Scribe_Values.Look(ref nextPaymentTick, "nextPaymentTick", -1);
            Scribe_Values.Look(ref arrearsSilver, "arrearsSilver", 0);
            Scribe_Values.Look(ref missedPayments, "missedPayments", 0);
            Scribe_Values.Look(ref refusingWork, "refusingWork", false);
            Scribe_Collections.Look(ref heldPriorities, "heldPriorities", LookMode.Def, LookMode.Value);

            Scribe_Values.Look(ref hiredTick, "hiredTick", 0);
            Scribe_Values.Look(ref arrivalTick, "arrivalTick", 0);
            Scribe_Values.Look(ref endTick, "endTick", -1);

            Scribe_Values.Look(ref status, "status", EmploymentStatus.Travelling);
            Scribe_Values.Look(ref outcomeNote, "outcomeNote", "");
            Scribe_Values.Look(ref termLapsedNotified, "termLapsedNotified", false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && heldPriorities == null)
            {
                // A missing dictionary node loads as null, not empty.
                heldPriorities = new Dictionary<WorkTypeDef, int>();
            }
        }

        /// <summary>
        /// Stops the worker working and remembers what they were doing (§39 step 4).
        ///
        /// The player can set the priorities back — nothing here fights them for it — which is a
        /// deliberate limit rather than an oversight: §39 makes refusal a warning stage on the way
        /// to the worker leaving, not a wall.
        /// </summary>
        public void HoldWork()
        {
            if (refusingWork || pawn?.workSettings == null || !pawn.workSettings.EverWork)
            {
                return;
            }

            heldPriorities.Clear();
            foreach (WorkTypeDef work in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                int priority = pawn.workSettings.GetPriority(work);
                if (priority > 0)
                {
                    heldPriorities[work] = priority;
                    pawn.workSettings.SetPriority(work, 0);
                }
            }

            refusingWork = true;
        }

        /// <summary>Puts the worker back on the jobs they had before they downed tools.</summary>
        public void ResumeWork()
        {
            if (!refusingWork)
            {
                return;
            }

            refusingWork = false;

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

            return $"Employment #{id} {workerName} from {settlementName} ({money}) [{status}]";
        }
    }
}
