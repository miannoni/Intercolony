using RimWorld;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// Mood penalty for an employee who has not been paid (DESIGN.md §39 step 3).
    ///
    /// Situational rather than a memory: being owed wages is a *state*, not an event. A memory
    /// thought would need re-granting on a timer and would linger after the debt was settled,
    /// which is precisely the wrong behaviour — paying up should lift the mood immediately.
    ///
    /// Stage is chosen from how many pay periods have been missed rather than from the amount,
    /// because what stings is being repeatedly passed over, not the size of the number.
    /// </summary>
    public class ThoughtWorker_UnpaidWages : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (p == null || !p.IsFreeColonist)
            {
                return ThoughtState.Inactive;
            }

            EmploymentContract contract = FindContract(p);
            if (contract == null || contract.arrearsSilver <= 0)
            {
                return ThoughtState.Inactive;
            }

            if (contract.missedPayments >= PayrollService.MissesBeforeRefusingWork)
            {
                return ThoughtState.ActiveAtStage(1);
            }

            return ThoughtState.ActiveAtStage(0);
        }

        private static EmploymentContract FindContract(Pawn pawn)
        {
            IntercolonyWorldComponent state = IntercolonyWorldComponent.Current;
            if (state == null)
            {
                return null;
            }

            foreach (EmploymentContract contract in state.Employments)
            {
                if (contract.status == EmploymentStatus.Active && contract.pawn == pawn)
                {
                    return contract;
                }
            }

            return null;
        }
    }
}
