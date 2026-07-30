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
        Failed
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
        public int paidSilver;

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

        public EmploymentContract()
        {
        }

        public bool IsOpen => status == EmploymentStatus.Travelling || status == EmploymentStatus.Active;

        public float DaysUntilArrival => (arrivalTick - GenTicks.TicksGame) / (float)GenDate.TicksPerDay;

        public float DaysRemaining => endTick < 0
            ? termDays
            : (endTick - GenTicks.TicksGame) / (float)GenDate.TicksPerDay;

        public string StatusLine()
        {
            switch (status)
            {
                case EmploymentStatus.Travelling:
                    return $"travelling — arrives in {Mathf.Max(0f, DaysUntilArrival):0.#}d";
                case EmploymentStatus.Active:
                    return termLapsedNotified
                        ? "term ended — away from the colony, will leave on return"
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

            Scribe_Values.Look(ref hiredTick, "hiredTick", 0);
            Scribe_Values.Look(ref arrivalTick, "arrivalTick", 0);
            Scribe_Values.Look(ref endTick, "endTick", -1);

            Scribe_Values.Look(ref status, "status", EmploymentStatus.Travelling);
            Scribe_Values.Look(ref outcomeNote, "outcomeNote", "");
            Scribe_Values.Look(ref termLapsedNotified, "termLapsedNotified", false);
        }

        /// <summary>
        /// An active contract whose pawn failed to resolve on load is unrecoverable: there is no
        /// employee left to manage. The world component drops these rather than tick them.
        /// </summary>
        public bool IsValidAfterLoad =>
            status != EmploymentStatus.Active || pawn != null;

        public override string ToString()
        {
            return $"Employment #{id} {workerName} from {settlementName} " +
                   $"({dailyWage}/day × {termDays}d = {paidSilver}) [{status}]";
        }
    }
}
