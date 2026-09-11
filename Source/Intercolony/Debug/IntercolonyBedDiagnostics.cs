/*
 * TEMPORARY PLAYTEST DIAGNOSTICS - REVERT THIS WHOLE FILE AFTER THE REPRODUCTION.
 *
 * This file is deliberately not production code. It observes RimWorld's bed/job state to
 * identify the origin and lifecycle of an employee LayDown job. It must not become a fix,
 * compatibility workaround, or permanent Harmony surface. The single switch below is the
 * operator's off switch; the release pass must remove this file entirely.
 *
 * Registration note: HarmonyPatches still calls Harmony.PatchAll() for this assembly. Every
 * diagnostic patch class therefore has a guarded Prepare() that resolves its exact target before
 * Harmony sees it; an unavailable diagnostic is skipped and reported without failing PatchAll.
 * The startup check is attached to the existing post-PatchAll Verbose log call. Every patch below
 * is passive: no result, argument, reservation, claim, or control-flow decision is changed.
 */

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Intercolony
{
    /// <summary>
    /// Temporary, revertable instrumentation for the employee bad-bed LayDown reproduction.
    /// </summary>
    internal static class IntercolonyBedDiagnostics
    {
        // OPERATOR SWITCH - set false to silence all diagnostic work in this file.
        public const bool Enabled = true;

        private const string DiagnosticHarmonyId = "miannoni.intercolony";

        private const int LifecycleRingCapacity = 24;
        private const int RescueRingCapacity = 8;
        private const int LookupRingCapacity = 16;

        private static readonly ConditionalWeakTable<Job, CreationObservation> CreationByJob =
            new ConditionalWeakTable<Job, CreationObservation>();

        private static readonly ConditionalWeakTable<Pawn, PawnHistory> HistoryByPawn =
            new ConditionalWeakTable<Pawn, PawnHistory>();

        private static readonly ConditionalWeakTable<JobQueue, QueueOwner> OwnerByQueue =
            new ConditionalWeakTable<JobQueue, QueueOwner>();

        [ThreadStatic]
        private static EvaluationObservation activeEvaluation;

        [ThreadStatic]
        private static RescueLookupContext activeRescueLookup;

        [ThreadStatic]
        private static TuckRuntimeContext activeTuck;

        [ThreadStatic]
        private static EmployeeJobCreationContext activeJobCreation;

        [ThreadStatic]
        private static TrackerContext activeTracker;

        private static long nextSequence;
        private static int internalFailureReported;
        private static int startupPatchCheckReported;

        internal sealed class EvaluationScope
        {
            public readonly EvaluationObservation Previous;

            public EvaluationScope(EvaluationObservation previous)
            {
                Previous = previous;
            }
        }

        internal sealed class EvaluationObservation
        {
            public Pawn Pawn;
            public Job Job;
            public bool CurrentAtEvaluation;
            public int QueueIndexAtEvaluation;
            public bool RequestedWhileLyingDown;
        }

        private sealed class CreationObservation
        {
            public string CapturedBy;
            public int Tick;
            public string Stack;
            public Building_Bed Bed;
            public BedSnapshot BedAtCreation;
            public bool ExactAtCreation;
            public string EmployeeEvidence;
        }

        internal sealed class EmployeeJobCreationScope
        {
            public readonly EmployeeJobCreationContext Previous;

            public EmployeeJobCreationScope(EmployeeJobCreationContext previous)
            {
                Previous = previous;
            }
        }

        internal sealed class EmployeeJobCreationContext
        {
            public readonly EmployeeJobCreationContext Previous;
            public readonly Pawn Pawn;

            public EmployeeJobCreationContext(
                EmployeeJobCreationContext previous, Pawn pawn)
            {
                Previous = previous;
                Pawn = pawn;
            }
        }

        internal sealed class TrackerScope
        {
            public readonly TrackerContext Previous;

            public TrackerScope(TrackerContext previous)
            {
                Previous = previous;
            }
        }

        internal sealed class TrackerContext
        {
            public readonly TrackerContext Previous;
            public readonly Pawn Pawn;

            public TrackerContext(TrackerContext previous, Pawn pawn)
            {
                Previous = previous;
                Pawn = pawn;
            }
        }

        private sealed class QueueOwner
        {
            public Pawn Pawn;
        }

        private sealed class PawnHistory
        {
            public readonly LifecycleObservation[] Lifecycle =
                new LifecycleObservation[LifecycleRingCapacity];

            public readonly RescueLookupObservation[] Lookups =
                new RescueLookupObservation[LookupRingCapacity];

            public readonly RescueSelectionObservation[] Selections =
                new RescueSelectionObservation[RescueRingCapacity];

            public readonly TuckObservation[] Tucks =
                new TuckObservation[RescueRingCapacity];

            public int LifecycleNext;
            public int LifecycleCount;
            public int LifecycleDropped;
            public int LookupNext;
            public int LookupCount;
            public int LookupDropped;
            public int SelectionNext;
            public int SelectionCount;
            public int SelectionDropped;
            public int TuckNext;
            public int TuckCount;
            public int TuckDropped;

            public Building_Bed LastFailureBed;
            public int LastFailureRepeatCount;
            public int IncidentCount;
        }

        private sealed class LifecycleObservation
        {
            public long Sequence;
            public int Tick;
            public string Kind;
            public string Job;
            public int JobLoadID;
            public string Detail;
            public string EventStack;
            public string CreationStack;
            public string CreationSource;
            public int CreationTick;
            public BedSnapshot CreationBed;
            public bool CreationExactAtCreation;
        }

        private sealed class RescueLookupObservation
        {
            public long Sequence;
            public int Tick;
            public string Kind;
            public Pawn Rescuer;
            public Pawn Patient;
            public Building_Bed Bed;
            public BedSnapshot BedState;
            public bool? ValidForRescue;
            public string Stack;
        }

        internal sealed class RescueSelectionObservation
        {
            public long Sequence;
            public int Tick;
            public Pawn Rescuer;
            public Pawn Patient;
            public Job RescueJob;
            public string RescueJobText;
            public bool? HasJob;
            public bool Forced;

            public Building_Bed DecisionBed;
            public BedSnapshot DecisionBedState;
            public bool? DecisionBedValid;
            public Building_Bed JobBuildBed;
            public BedSnapshot JobBuildBedState;
            public bool? JobBuildBedValid;
            public string Stack;
        }

        internal sealed class TuckObservation
        {
            public long Sequence;
            public int Tick;
            public Pawn Taker;
            public Pawn Takee;
            public Building_Bed FinalBed;
            public BedSnapshot FinalBedState;
            public bool RescuedArgument;
            public bool? CanUseBedAtTuck;
            public string TakerJob;
            public bool TakerWasRescueJob;
            public RescueSelectionObservation MatchedSelection;
            public int LayDownJobLoadID = -1;
            public string LayDownJobText;
            public string Stack;
        }

        internal sealed class RescueLookupContext
        {
            public readonly RescueLookupContext Previous;
            public readonly Pawn Rescuer;
            public readonly Pawn Patient;
            public readonly string Kind;

            public RescueLookupContext(
                RescueLookupContext previous, Pawn rescuer, Pawn patient, string kind)
            {
                Previous = previous;
                Rescuer = rescuer;
                Patient = patient;
                Kind = kind;
            }
        }

        internal sealed class RescueLookupScope
        {
            public readonly RescueLookupContext Previous;

            public RescueLookupScope(RescueLookupContext previous)
            {
                Previous = previous;
            }
        }

        internal sealed class TuckRuntimeContext
        {
            public readonly TuckRuntimeContext Previous;
            public readonly Pawn Takee;
            public readonly Building_Bed Bed;
            public readonly TuckObservation Observation;

            public TuckRuntimeContext(
                TuckRuntimeContext previous, Pawn takee, Building_Bed bed,
                TuckObservation observation)
            {
                Previous = previous;
                Takee = takee;
                Bed = bed;
                Observation = observation;
            }
        }

        internal sealed class TuckScope
        {
            public readonly TuckRuntimeContext Previous;

            public TuckScope(TuckRuntimeContext previous)
            {
                Previous = previous;
            }
        }

        internal sealed class BedSnapshot
        {
            public bool HasBed;
            public string ThingID;
            public string Position;
            public string DefName;
            public bool? Medical;
            public int? SleepingSlotsCount;
            public bool? AnyUnoccupiedSleepingSlot;
            public string Error;
        }

        private sealed class SlotCheck
        {
            public bool OwnsBed;
            public int? AssignedSlot;
            public bool UnownedSlotOccupiedByPawn;
            public bool UnownedEmptySlot;
            public bool AnyUnoccupiedSleepingSlot;

            public bool AllThreeVanillaChecksFailed =>
                !OwnsBed && !UnownedSlotOccupiedByPawn && !UnownedEmptySlot;
        }

        internal static EvaluationScope BeginLayDownEvaluation(
            Job job, Pawn pawn, bool requestedWhileLyingDown)
        {
            try
            {
                if (!Enabled || pawn == null || !EmploymentService.IsEmployee(pawn) ||
                    !IsLayDown(job) || (!requestedWhileLyingDown && !pawn.Downed))
                {
                    return null;
                }

                if (pawn.jobs == null)
                {
                    return null;
                }

                RememberQueueOwner(pawn.jobs.jobQueue, pawn);
                EvaluationScope scope = new EvaluationScope(activeEvaluation);
                activeEvaluation = new EvaluationObservation
                {
                    Pawn = pawn,
                    Job = job,
                    CurrentAtEvaluation = pawn.jobs.curJob == job,
                    QueueIndexAtEvaluation = FindQueueIndex(pawn.jobs.jobQueue, job),
                    RequestedWhileLyingDown = requestedWhileLyingDown
                };
                return scope;
            }
            catch (Exception ex)
            {
                ReportInternalFailure("starting LayDown evaluation observation", ex);
                return null;
            }
        }

        internal static void EndLayDownEvaluation(EvaluationScope scope)
        {
            try
            {
                if (scope != null)
                {
                    activeEvaluation = scope.Previous;
                }
            }
            catch (Exception ex)
            {
                ReportInternalFailure("ending LayDown evaluation observation", ex);
            }
        }

        internal static EmployeeJobCreationScope BeginEmployeeJobCreation(Pawn pawn)
        {
            try
            {
                if (!Enabled || pawn == null || !EmploymentService.IsEmployee(pawn))
                {
                    return null;
                }

                EmployeeJobCreationScope scope = new EmployeeJobCreationScope(activeJobCreation);
                activeJobCreation = new EmployeeJobCreationContext(activeJobCreation, pawn);
                return scope;
            }
            catch (Exception ex)
            {
                ReportInternalFailure("starting employee job-creation context", ex);
                return null;
            }
        }

        internal static void EndEmployeeJobCreation(EmployeeJobCreationScope scope)
        {
            try
            {
                if (scope != null)
                {
                    activeJobCreation = scope.Previous;
                }
            }
            catch (Exception ex)
            {
                ReportInternalFailure("ending employee job-creation context", ex);
            }
        }

        internal static TrackerScope BeginTrackerContext(Pawn pawn)
        {
            try
            {
                if (!Enabled || pawn == null || !EmploymentService.IsEmployee(pawn))
                {
                    return null;
                }

                TrackerScope scope = new TrackerScope(activeTracker);
                activeTracker = new TrackerContext(activeTracker, pawn);
                return scope;
            }
            catch (Exception ex)
            {
                ReportInternalFailure("starting employee tracker context", ex);
                return null;
            }
        }

        internal static void EndTrackerContext(TrackerScope scope)
        {
            try
            {
                if (scope != null)
                {
                    activeTracker = scope.Previous;
                }
            }
            catch (Exception ex)
            {
                ReportInternalFailure("ending employee tracker context", ex);
            }
        }

        internal static void ObserveSleepingSlotLookup(Pawn pawn, Building_Bed bed)
        {
            try
            {
                // This is the only path that emits an incident. It runs before vanilla's own
                // Log.Error and duplicates RestUtility's three checks without invoking it.
                if (!Enabled || pawn == null || !EmploymentService.IsEmployee(pawn) || bed == null)
                {
                    return;
                }

                EvaluationObservation evaluation = activeEvaluation;
                if (evaluation == null || evaluation.Pawn != pawn || !IsLayDown(evaluation.Job) ||
                    evaluation.Job.targetA.Thing != bed)
                {
                    return;
                }

                SlotCheck check = ReadVanillaSlotChecks(pawn, bed);
                if (!check.AllThreeVanillaChecksFailed)
                {
                    ResetFailureSuppression(pawn, bed);
                    return;
                }

                EmitIncident(evaluation, bed, check);
            }
            catch (Exception ex)
            {
                ReportInternalFailure("observing the bad sleeping-slot lookup", ex);
            }
        }

        internal static void CaptureJobCreation(Job job, string capturedBy)
        {
            try
            {
                // JobMaker/Job constructors carry no pawn. Capture only with an employee-aware
                // caller context, or when an employee is physically occupying the target bed;
                // this keeps the costly stack capture out of the colony-wide LayDown path.
                // Jobs created outside either signal receive an employee-scoped lifecycle
                // fallback at enqueue/start.
                if (!Enabled || !IsLayDown(job))
                {
                    return;
                }

                Building_Bed bed = job.targetA.Thing as Building_Bed;
                Pawn employee = FindActiveCreationEmployee();
                bool employeeContext = employee != null;
                if (employee == null)
                {
                    employee = FindEmployeeOccupant(bed);
                }
                if (employee == null)
                {
                    return;
                }

                CreationObservation observation = new CreationObservation
                {
                    CapturedBy = capturedBy,
                    Tick = CurrentTick(),
                    Stack = CaptureStack(),
                    Bed = bed,
                    BedAtCreation = CaptureBedSnapshot(bed),
                    ExactAtCreation = employeeContext,
                    EmployeeEvidence = employeeContext
                        ? "employee-aware caller context"
                        : "employee physically occupying target bed"
                };

                CreationByJob.Remove(job);
                CreationByJob.Add(job, observation);
            }
            catch (Exception ex)
            {
                ReportInternalFailure("capturing LayDown creation", ex);
            }
        }

        internal static void ForgetJobCreation(Job job)
        {
            try
            {
                if (Enabled && job != null)
                {
                    CreationByJob.Remove(job);
                }
            }
            catch (Exception ex)
            {
                ReportInternalFailure("forgetting pooled job creation", ex);
            }
        }

        internal static void ObserveTrackerCreated(Pawn_JobTracker tracker, Pawn pawn)
        {
            try
            {
                if (!Enabled || pawn == null || !EmploymentService.IsEmployee(pawn) || tracker == null)
                {
                    return;
                }

                // JobQueue has no back-reference to its pawn. Keep only this weak identity
                // association for employees; tracker contexts cover queue calls made before
                // this association exists during live insertion and queue restoration.
                RememberQueueOwner(tracker.jobQueue, pawn);
            }
            catch (Exception ex)
            {
                ReportInternalFailure("associating an employee job queue", ex);
            }
        }

        internal static void ObserveStart(
            Pawn_JobTracker tracker, Pawn pawn, Job job, bool fromQueue,
            JobTag? tag, bool continueSleeping)
        {
            try
            {
                if (!Enabled || pawn == null || !EmploymentService.IsEmployee(pawn) ||
                    tracker == null || !IsLayDown(job))
                {
                    return;
                }

                RememberQueueOwner(tracker.jobQueue, pawn);
                string detail = "fromQueue=" + fromQueue +
                    "; tag=" + FormatTag(tag) +
                    "; continueSleeping=" + continueSleeping +
                    "; activeTuck=" + FormatActiveTuck(pawn);
                RecordLifecycle(pawn, job, "START", detail);

                if (activeTuck != null && activeTuck.Takee == pawn &&
                    activeTuck.Observation != null)
                {
                    activeTuck.Observation.LayDownJobLoadID = job.loadID;
                    activeTuck.Observation.LayDownJobText = DescribeJob(job);
                }
            }
            catch (Exception ex)
            {
                ReportInternalFailure("capturing a LayDown start", ex);
            }
        }

        internal static void ObserveEnd(
            Pawn_JobTracker tracker, Pawn pawn, JobCondition condition)
        {
            try
            {
                if (!Enabled || pawn == null || !EmploymentService.IsEmployee(pawn) ||
                    tracker == null)
                {
                    return;
                }

                RememberQueueOwner(tracker.jobQueue, pawn);
                Job job = tracker.curJob;
                if (IsLayDown(job))
                {
                    RecordLifecycle(
                        pawn, job, "END_CALL",
                        "condition=" + condition + "; queueCountBefore=" +
                        (tracker.jobQueue == null ? "<null>" : tracker.jobQueue.Count.ToString()));
                }
            }
            catch (Exception ex)
            {
                ReportInternalFailure("capturing a LayDown end", ex);
            }
        }

        internal static void ObserveEnqueue(JobQueue queue, Job job, JobTag? tag, string kind)
        {
            try
            {
                if (!Enabled || !IsLayDown(job) || queue == null)
                {
                    return;
                }

                QueueOwner owner;
                Pawn pawn = null;
                // The tracker context is especially important for Common Sense, whose
                // StartJob/EndCurrentJob prefixes can enqueue before the ordinary body runs.
                if (OwnerByQueue.TryGetValue(queue, out owner) && owner != null &&
                    owner.Pawn != null && EmploymentService.IsEmployee(owner.Pawn))
                {
                    pawn = owner.Pawn;
                }
                else if (activeTracker != null && activeTracker.Pawn != null &&
                    EmploymentService.IsEmployee(activeTracker.Pawn))
                {
                    pawn = activeTracker.Pawn;
                }

                if (pawn == null)
                {
                    return;
                }

                RememberQueueOwner(queue, pawn);
                RecordLifecycle(pawn, job, kind, "tag=" + FormatTag(tag));
            }
            catch (Exception ex)
            {
                ReportInternalFailure("capturing a LayDown enqueue", ex);
            }
        }

        internal static RescueLookupScope BeginRescueLookup(
            Pawn rescuer, Pawn patient, string kind)
        {
            try
            {
                if (!Enabled || patient == null || !EmploymentService.IsEmployee(patient))
                {
                    return null;
                }

                RescueLookupScope scope = new RescueLookupScope(activeRescueLookup);
                activeRescueLookup = new RescueLookupContext(activeRescueLookup, rescuer, patient, kind);
                return scope;
            }
            catch (Exception ex)
            {
                ReportInternalFailure("starting rescue lookup observation", ex);
                return null;
            }
        }

        internal static void EndRescueLookup(RescueLookupScope scope)
        {
            try
            {
                if (scope != null)
                {
                    activeRescueLookup = scope.Previous;
                }
            }
            catch (Exception ex)
            {
                ReportInternalFailure("ending rescue lookup observation", ex);
            }
        }

        internal static void ObserveFindBedResult(
            Pawn rescuer, Pawn patient, Building_Bed bed)
        {
            try
            {
                if (!Enabled || patient == null || !EmploymentService.IsEmployee(patient))
                {
                    return;
                }

                RescueLookupContext context = activeRescueLookup;
                if (context == null || context.Rescuer != rescuer || context.Patient != patient)
                {
                    return;
                }

                PawnHistory history = GetHistory(patient);
                RescueLookupObservation observation = new RescueLookupObservation
                {
                    Sequence = NextSequence(),
                    Tick = CurrentTick(),
                    Kind = context.Kind,
                    Rescuer = rescuer,
                    Patient = patient,
                    Bed = bed,
                    BedState = CaptureBedSnapshot(bed),
                    ValidForRescue = TryIsValidRescueBed(bed, patient, rescuer),
                    Stack = CaptureStack()
                };
                AddLookup(history, observation);
            }
            catch (Exception ex)
            {
                ReportInternalFailure("capturing a rescue bed lookup", ex);
            }
        }

        internal static void ObserveRescueJob(
            Pawn rescuer, Thing target, bool forced, Job result)
        {
            try
            {
                Pawn patient = target as Pawn;
                if (!Enabled || patient == null || !EmploymentService.IsEmployee(patient))
                {
                    return;
                }

                PawnHistory history = GetHistory(patient);
                RescueLookupObservation decision = FindLatestLookup(
                    history, rescuer, patient, "RESCUE_DECISION");
                RescueLookupObservation jobBuild = FindLatestLookup(
                    history, rescuer, patient, "RESCUE_JOB_BUILD");
                Building_Bed jobBed = result == null ? null : result.targetB.Thing as Building_Bed;

                RescueSelectionObservation selection = new RescueSelectionObservation
                {
                    Sequence = NextSequence(),
                    Tick = CurrentTick(),
                    Rescuer = rescuer,
                    Patient = patient,
                    RescueJob = result,
                    RescueJobText = DescribeJob(result),
                    HasJob = result != null,
                    Forced = forced,
                    DecisionBed = decision == null ? null : decision.Bed,
                    DecisionBedState = decision == null ? null : decision.BedState,
                    DecisionBedValid = decision == null ? null : decision.ValidForRescue,
                    JobBuildBed = jobBuild == null ? jobBed : jobBuild.Bed,
                    JobBuildBedState = jobBuild == null
                        ? CaptureBedSnapshot(jobBed)
                        : jobBuild.BedState,
                    JobBuildBedValid = jobBuild == null
                        ? TryIsValidRescueBed(jobBed, patient, rescuer)
                        : jobBuild.ValidForRescue,
                    Stack = CaptureStack()
                };
                AddSelection(history, selection);
            }
            catch (Exception ex)
            {
                ReportInternalFailure("capturing a rescue job selection", ex);
            }
        }

        internal static TuckScope BeginTuck(
            Building_Bed bed, Pawn taker, Pawn takee, bool rescued)
        {
            try
            {
                if (!Enabled || takee == null || !EmploymentService.IsEmployee(takee))
                {
                    return null;
                }

                PawnHistory history = GetHistory(takee);
                RescueSelectionObservation selection = FindLatestSelection(history, taker);
                Job takerJob = taker == null ? null : taker.CurJob;
                TuckObservation observation = new TuckObservation
                {
                    Sequence = NextSequence(),
                    Tick = CurrentTick(),
                    Taker = taker,
                    Takee = takee,
                    FinalBed = bed,
                    FinalBedState = CaptureBedSnapshot(bed),
                    RescuedArgument = rescued,
                    CanUseBedAtTuck = TryCanUseBedNow(bed, takee),
                    TakerJob = DescribeJob(takerJob),
                    TakerWasRescueJob = takerJob != null && takerJob.def == JobDefOf.Rescue &&
                        takerJob.targetA.Thing == takee,
                    MatchedSelection = selection,
                    Stack = CaptureStack()
                };
                AddTuck(history, observation);
                activeTuck = new TuckRuntimeContext(activeTuck, takee, bed, observation);
                return new TuckScope(activeTuck.Previous);
            }
            catch (Exception ex)
            {
                ReportInternalFailure("starting tuck observation", ex);
                return null;
            }
        }

        internal static void EndTuck(TuckScope scope)
        {
            try
            {
                if (scope != null)
                {
                    activeTuck = scope.Previous;
                }
            }
            catch (Exception ex)
            {
                ReportInternalFailure("ending tuck observation", ex);
            }
        }

        private static Pawn FindActiveCreationEmployee()
        {
            EmployeeJobCreationContext factory = activeJobCreation;
            if (factory != null && factory.Pawn != null && EmploymentService.IsEmployee(factory.Pawn))
            {
                return factory.Pawn;
            }

            TuckRuntimeContext tuck = activeTuck;
            if (tuck != null && tuck.Takee != null && EmploymentService.IsEmployee(tuck.Takee))
            {
                return tuck.Takee;
            }

            RescueLookupContext rescue = activeRescueLookup;
            if (rescue != null && rescue.Patient != null && EmploymentService.IsEmployee(rescue.Patient))
            {
                return rescue.Patient;
            }

            return null;
        }

        private static Pawn FindEmployeeOccupant(Building_Bed bed)
        {
            try
            {
                if (bed == null)
                {
                    return null;
                }

                for (int i = 0; i < bed.SleepingSlotsCount; i++)
                {
                    Pawn occupant = bed.GetCurOccupant(i);
                    if (occupant != null && EmploymentService.IsEmployee(occupant))
                    {
                        return occupant;
                    }
                }
            }
            catch
            {
                // A best-effort identity inference must never affect job construction.
            }

            return null;
        }

        private static void RememberQueueOwner(JobQueue queue, Pawn pawn)
        {
            if (queue == null || pawn == null)
            {
                return;
            }

            QueueOwner owner;
            if (OwnerByQueue.TryGetValue(queue, out owner) && owner != null)
            {
                owner.Pawn = pawn;
                return;
            }

            OwnerByQueue.Add(queue, new QueueOwner { Pawn = pawn });
        }

        private static void RecordLifecycle(Pawn pawn, Job job, string kind, string detail)
        {
            if (!Enabled || pawn == null || !EmploymentService.IsEmployee(pawn) || !IsLayDown(job))
            {
                return;
            }

            PawnHistory history = GetHistory(pawn);
            CreationObservation creation;
            if (!CreationByJob.TryGetValue(job, out creation) || creation == null)
            {
                // Some employee jobs are built outside a pawn-aware factory context (for
                // example a finish action). The first employee queue/current-job event is the
                // nearest safe observation point: retain its stack and bed state, explicitly
                // marked as an association fallback rather than pretending it is constructor
                // time.
                creation = new CreationObservation
                {
                    CapturedBy = "employee " + kind + " association fallback",
                    Tick = CurrentTick(),
                    Stack = CaptureStack(),
                    Bed = job.targetA.Thing as Building_Bed,
                    BedAtCreation = CaptureBedSnapshot(job.targetA.Thing as Building_Bed),
                    ExactAtCreation = false,
                    EmployeeEvidence = "employee lifecycle association; constructor context unavailable"
                };
                CreationByJob.Remove(job);
                CreationByJob.Add(job, creation);
            }

            LifecycleObservation observation = new LifecycleObservation
            {
                Sequence = NextSequence(),
                Tick = CurrentTick(),
                Kind = kind,
                Job = DescribeJob(job),
                JobLoadID = job.loadID,
                Detail = detail,
                EventStack = CaptureStack(),
                CreationStack = creation == null ? null : creation.Stack,
                CreationSource = creation == null ? null : creation.CapturedBy,
                CreationTick = creation == null ? -1 : creation.Tick,
                CreationBed = creation == null ? null : creation.BedAtCreation,
                CreationExactAtCreation = creation != null && creation.ExactAtCreation
            };
            AddLifecycle(history, observation);
        }

        private static void AddLifecycle(PawnHistory history, LifecycleObservation observation)
        {
            if (history.LifecycleCount == LifecycleRingCapacity)
            {
                history.LifecycleDropped++;
            }
            else
            {
                history.LifecycleCount++;
            }

            history.Lifecycle[history.LifecycleNext] = observation;
            history.LifecycleNext = (history.LifecycleNext + 1) % LifecycleRingCapacity;
        }

        private static void AddLookup(PawnHistory history, RescueLookupObservation observation)
        {
            if (history.LookupCount == LookupRingCapacity)
            {
                history.LookupDropped++;
            }
            else
            {
                history.LookupCount++;
            }

            history.Lookups[history.LookupNext] = observation;
            history.LookupNext = (history.LookupNext + 1) % LookupRingCapacity;
        }

        private static void AddSelection(
            PawnHistory history, RescueSelectionObservation observation)
        {
            if (history.SelectionCount == RescueRingCapacity)
            {
                history.SelectionDropped++;
            }
            else
            {
                history.SelectionCount++;
            }

            history.Selections[history.SelectionNext] = observation;
            history.SelectionNext = (history.SelectionNext + 1) % RescueRingCapacity;
        }

        private static void AddTuck(PawnHistory history, TuckObservation observation)
        {
            if (history.TuckCount == RescueRingCapacity)
            {
                history.TuckDropped++;
            }
            else
            {
                history.TuckCount++;
            }

            history.Tucks[history.TuckNext] = observation;
            history.TuckNext = (history.TuckNext + 1) % RescueRingCapacity;
        }

        private static PawnHistory GetHistory(Pawn pawn)
        {
            PawnHistory history;
            if (HistoryByPawn.TryGetValue(pawn, out history) && history != null)
            {
                return history;
            }

            history = new PawnHistory();
            HistoryByPawn.Add(pawn, history);
            return history;
        }

        private static RescueLookupObservation FindLatestLookup(
            PawnHistory history, Pawn rescuer, Pawn patient, string kind)
        {
            if (history == null || history.LookupCount == 0)
            {
                return null;
            }

            for (int offset = 0; offset < history.LookupCount; offset++)
            {
                int index = RingIndex(history.LookupNext, history.LookupCount, LookupRingCapacity, offset);
                RescueLookupObservation observation = history.Lookups[index];
                if (observation != null && observation.Rescuer == rescuer &&
                    observation.Patient == patient && observation.Kind == kind)
                {
                    return observation;
                }
            }

            return null;
        }

        private static RescueSelectionObservation FindLatestSelection(
            PawnHistory history, Pawn taker)
        {
            if (history == null || history.SelectionCount == 0)
            {
                return null;
            }

            RescueSelectionObservation newestAny = null;
            for (int offset = 0; offset < history.SelectionCount; offset++)
            {
                int index = RingIndex(
                    history.SelectionNext, history.SelectionCount, RescueRingCapacity, offset);
                RescueSelectionObservation observation = history.Selections[index];
                if (observation == null)
                {
                    continue;
                }

                if (newestAny == null)
                {
                    newestAny = observation;
                }

                if (observation.Rescuer == taker)
                {
                    return observation;
                }
            }

            return newestAny;
        }

        private static int RingIndex(int next, int count, int capacity, int offsetFromNewest)
        {
            int index = next - 1 - offsetFromNewest;
            while (index < 0)
            {
                index += capacity;
            }
            return index % capacity;
        }

        private static void ResetFailureSuppression(Pawn pawn, Building_Bed bed)
        {
            try
            {
                PawnHistory history;
                if (pawn == null || !HistoryByPawn.TryGetValue(pawn, out history) || history == null)
                {
                    return;
                }

                if (history.LastFailureBed == bed)
                {
                    history.LastFailureBed = null;
                    history.LastFailureRepeatCount = 0;
                }
            }
            catch (Exception ex)
            {
                ReportInternalFailure("resetting repeated failure suppression", ex);
            }
        }

        private static void EmitIncident(
            EvaluationObservation evaluation, Building_Bed bed, SlotCheck check)
        {
            Pawn pawn = evaluation.Pawn;
            PawnHistory history = GetHistory(pawn);
            if (history.LastFailureBed == bed)
            {
                history.LastFailureRepeatCount++;
                return;
            }

            int previousSuppressed = history.LastFailureRepeatCount;
            history.LastFailureBed = bed;
            history.LastFailureRepeatCount = 0;
            history.IncidentCount++;

            string dump;
            try
            {
                dump = BuildIncidentDump(evaluation, bed, check, history, previousSuppressed);
            }
            catch (Exception ex)
            {
                ReportInternalFailure("building the bed incident dump", ex);
                dump = "TEMPORARY BED DIAGNOSTICS incident: employee=" + PawnName(pawn) +
                    "; bed=" + BedText(bed) + "; exact LayDown job=" + DescribeJob(evaluation.Job);
            }

            try
            {
                IntercolonyLog.Error(dump);
            }
            catch (Exception ex)
            {
                // Logging is diagnostic-only; even a logger failure must not escape into vanilla.
                ReportInternalFailure("writing the bed incident dump", ex);
            }
        }

        private static string BuildIncidentDump(
            EvaluationObservation evaluation, Building_Bed bed, SlotCheck check,
            PawnHistory history, int previousSuppressed)
        {
            Pawn pawn = evaluation.Pawn;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("TEMPORARY BED DIAGNOSTICS - BAD QUEUED LAYDOWN INCIDENT #" +
                history.IncidentCount + "; instrumentation only; no game state changed.");
            sb.AppendLine("employee=" + PawnName(pawn) + "; employeeThingID=" + ThingID(pawn) +
                "; tick=" + CurrentTick());
            sb.AppendLine("previousSamePairRepeatsSuppressed=" + previousSuppressed +
                "; future same employee/bed failures are counted and not reprinted.");

            sb.AppendLine("evaluatedJobLocationAtEntry=" +
                EvaluationLocation(evaluation.CurrentAtEvaluation, evaluation.QueueIndexAtEvaluation));
            bool currentAtFailure = pawn != null && pawn.jobs != null &&
                pawn.jobs.curJob == evaluation.Job;
            int queueIndexAtFailure = pawn == null || pawn.jobs == null
                ? -1 : FindQueueIndex(pawn.jobs.jobQueue, evaluation.Job);
            sb.AppendLine("evaluatedJobLocationAtFailure=" +
                EvaluationLocation(currentAtFailure, queueIndexAtFailure));
            sb.AppendLine("evaluatedJobAtFailure=" + DescribeJob(evaluation.Job));
            sb.AppendLine("evaluatedJobTargets=" + DescribeTargets(evaluation.Job));
            sb.AppendLine("requestedWhileLyingDown=" + evaluation.RequestedWhileLyingDown +
                "; pawnDowned=" + (pawn != null && pawn.Downed));
            sb.AppendLine("targetBedAtFailure=" + BedText(bed));

            sb.AppendLine("vanillaSlotChecksAtFailure: ownsBed=" + check.OwnsBed +
                "; assignedSlot=" + NullableInt(check.AssignedSlot) +
                "; unownedSlotOccupiedByEmployee=" + check.UnownedSlotOccupiedByPawn +
                "; unownedEmptySlot=" + check.UnownedEmptySlot +
                "; allThreeFailed=" + check.AllThreeVanillaChecksFailed +
                "; AnyUnoccupiedSleepingSlot=" + check.AnyUnoccupiedSleepingSlot);

            CreationObservation creation;
            if (evaluation.Job != null && CreationByJob.TryGetValue(evaluation.Job, out creation) &&
                creation != null)
            {
                sb.AppendLine("jobCreation: capturedBy=" + creation.CapturedBy +
                    "; tick=" + creation.Tick +
                    "; exactAtCreation=" + creation.ExactAtCreation +
                    "; employeeEvidence=" + creation.EmployeeEvidence +
                    "; bedAtCreation=" + SnapshotText(creation.BedAtCreation));
                sb.AppendLine("creationToFailureComparison: sameBed=" + SameBed(creation.Bed, bed) +
                    "; AnyUnoccupiedAtCreation=" + SnapshotAny(creation.BedAtCreation) +
                    "; AnyUnoccupiedAtFailure=" + check.AnyUnoccupiedSleepingSlot +
                    "; allThreeFailedAtFailure=" + check.AllThreeVanillaChecksFailed);
                AppendStack(sb, "jobCreationStack", creation.Stack);
            }
            else
            {
                sb.AppendLine("jobCreation: not captured (possibly deserialized, cloned, or already pooled).");
            }

            AppendSlots(sb, bed);
            AppendReservations(sb, bed);
            AppendPawnBeds(sb, pawn);
            AppendCurrentAndQueue(sb, pawn);
            AppendRescueFlow(sb, pawn, evaluation.Job, bed, check, history);
            AppendLifecycle(sb, history, evaluation.Job);
            AppendStack(sb, "failure/evaluationStack", CaptureStack());
            return sb.ToString();
        }

        private static void AppendSlots(StringBuilder sb, Building_Bed bed)
        {
            sb.AppendLine("bedSlotsAtFailure:");
            if (bed == null)
            {
                sb.AppendLine("  <null bed>");
                return;
            }

            int slotCount;
            try
            {
                slotCount = bed.SleepingSlotsCount;
            }
            catch (Exception ex)
            {
                sb.AppendLine("  slot enumeration failed: " + ExceptionText(ex));
                return;
            }

            for (int i = 0; i < slotCount; i++)
            {
                try
                {
                    IntVec3 position = bed.GetSleepingSlotPos(i);
                    Pawn occupant = bed.GetCurOccupant(i);
                    List<Pawn> owners = bed.OwnersForReading;
                    Pawn owner = owners != null && i < owners.Count ? owners[i] : null;
                    sb.AppendLine("  slot[" + i + "] position=" + position +
                        "; owner=" + PawnName(owner) +
                        "; GetCurOccupant=" + PawnName(occupant) +
                        "; physicallyLying=" + PhysicalPawnsAt(bed, position));
                }
                catch (Exception ex)
                {
                    sb.AppendLine("  slot[" + i + "] enumeration failed: " + ExceptionText(ex));
                }
            }
        }

        private static string PhysicalPawnsAt(Building_Bed bed, IntVec3 position)
        {
            if (bed == null || !bed.Spawned || bed.Map == null)
            {
                return "<not spawned>";
            }

            List<Thing> things = bed.Map.thingGrid.ThingsListAt(position);
            List<string> lying = new List<string>();
            List<string> pawns = new List<string>();
            for (int i = 0; i < things.Count; i++)
            {
                Pawn pawn = things[i] as Pawn;
                if (pawn == null)
                {
                    continue;
                }

                bool inBed = false;
                try
                {
                    inBed = pawn.GetPosture().InBed();
                }
                catch (Exception ex)
                {
                    pawns.Add(PawnName(pawn) + "(posture=" + ExceptionText(ex) + ")");
                    continue;
                }

                string value = PawnName(pawn) + "(inBed=" + inBed + ")";
                pawns.Add(value);
                if (inBed)
                {
                    lying.Add(PawnName(pawn));
                }
            }

            if (pawns.Count == 0)
            {
                return "none";
            }

            return "lying=" + Join(lying) + "; pawnsAtPosition=" + Join(pawns);
        }

        private static void AppendReservations(StringBuilder sb, Building_Bed bed)
        {
            sb.AppendLine("reservationsOnTargetBedAtFailure:");
            if (bed == null || !bed.Spawned || bed.Map == null || bed.Map.reservationManager == null)
            {
                sb.AppendLine("  <none visible; bed is not spawned or has no map reservation manager>");
                return;
            }

            List<ReservationManager.Reservation> reservations =
                bed.Map.reservationManager.ReservationsReadOnly;
            int matches = 0;
            for (int i = 0; i < reservations.Count; i++)
            {
                try
                {
                    ReservationManager.Reservation reservation = reservations[i];
                    if (reservation == null || reservation.Target.Thing != bed)
                    {
                        continue;
                    }

                    matches++;
                    sb.AppendLine("  reservation[" + i + "] claimant=" + PawnName(reservation.Claimant) +
                        "; target=" + DescribeTarget(reservation.Target) +
                        "; job=" + DescribeJob(reservation.Job) +
                        "; layer=" + (reservation.Layer == null ? "<null>" : reservation.Layer.defName) +
                        "; maxPawns=" + reservation.MaxPawns +
                        "; stackCount=" + reservation.StackCount);
                }
                catch (Exception ex)
                {
                    sb.AppendLine("  reservation[" + i + "] enumeration failed: " + ExceptionText(ex));
                }
            }

            if (matches == 0)
            {
                sb.AppendLine("  none");
            }
        }

        private static void AppendPawnBeds(StringBuilder sb, Pawn pawn)
        {
            sb.AppendLine("employeeBeds:");
            try
            {
                Building_Bed owned = pawn == null || pawn.ownership == null
                    ? null : pawn.ownership.OwnedBed;
                Building_Bed current = pawn == null ? null : pawn.CurrentBed();
                sb.AppendLine("  ownership.OwnedBed=" + BedText(owned));
                sb.AppendLine("  CurrentBed()=" + BedText(current));
            }
            catch (Exception ex)
            {
                sb.AppendLine("  bed ownership/current-bed read failed: " + ExceptionText(ex));
            }
        }

        private static void AppendCurrentAndQueue(StringBuilder sb, Pawn pawn)
        {
            sb.AppendLine("employeeJobStateAtFailure:");
            if (pawn == null || pawn.jobs == null)
            {
                sb.AppendLine("  jobs=<null>");
                return;
            }

            try
            {
                sb.AppendLine("  currentJob=" + DescribeJob(pawn.jobs.curJob));
                JobQueue queue = pawn.jobs.jobQueue;
                if (queue == null)
                {
                    sb.AppendLine("  queuedJobs=<null>");
                    return;
                }

                sb.AppendLine("  queuedJobs.count=" + queue.Count);
                for (int i = 0; i < queue.Count; i++)
                {
                    QueuedJob queued = queue[i];
                    sb.AppendLine("  queue[" + i + "] tag=" +
                        (queued == null ? "<null entry>" : FormatTag(queued.tag)) +
                        "; job=" + DescribeJob(queued == null ? null : queued.job));
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("  current/queue read failed: " + ExceptionText(ex));
            }
        }

        private static void AppendRescueFlow(
            StringBuilder sb, Pawn pawn, Job evaluatedJob, Building_Bed failureBed,
            SlotCheck failureCheck, PawnHistory history)
        {
            sb.AppendLine("rescueTuckEvidence:");
            bool exactTuck = false;
            bool taggedTuck = false;
            if (history != null)
            {
                for (int offset = 0; offset < history.TuckCount; offset++)
                {
                    int index = RingIndex(history.TuckNext, history.TuckCount, RescueRingCapacity, offset);
                    TuckObservation tuck = history.Tucks[index];
                    if (tuck == null)
                    {
                        continue;
                    }

                    if (tuck.LayDownJobLoadID == (evaluatedJob == null ? -1 : evaluatedJob.loadID))
                    {
                        exactTuck = true;
                    }
                }

                for (int offset = 0; offset < history.LifecycleCount; offset++)
                {
                    int index = RingIndex(
                        history.LifecycleNext, history.LifecycleCount, LifecycleRingCapacity, offset);
                    LifecycleObservation lifecycle = history.Lifecycle[index];
                    if (lifecycle == null || evaluatedJob == null ||
                        lifecycle.JobLoadID != evaluatedJob.loadID)
                    {
                        continue;
                    }

                    if (lifecycle.Detail != null && lifecycle.Detail.IndexOf(
                        "tag=TuckedIntoBed", StringComparison.Ordinal) >= 0)
                    {
                        taggedTuck = true;
                    }
                }
            }

            if (history == null || history.LookupCount == 0)
            {
                sb.AppendLine("  rescue lookups: none retained");
            }
            else
            {
                sb.AppendLine("  rescue lookup ring: retained=" + history.LookupCount +
                    "; droppedOldest=" + history.LookupDropped +
                    "; capacity=" + LookupRingCapacity);
                for (int offset = history.LookupCount - 1; offset >= 0; offset--)
                {
                    int index = RingIndex(
                        history.LookupNext, history.LookupCount, LookupRingCapacity, offset);
                    RescueLookupObservation lookup = history.Lookups[index];
                    if (lookup == null)
                    {
                        continue;
                    }

                    sb.AppendLine("  lookup kind=" + lookup.Kind + "; tick=" + lookup.Tick +
                        "; rescuer=" + PawnName(lookup.Rescuer) +
                        "; patient=" + PawnName(lookup.Patient) +
                        "; bed=" + SnapshotText(lookup.BedState) +
                        "; validForRescue=" + NullableBool(lookup.ValidForRescue));
                    AppendStack(sb, "  lookupStack", lookup.Stack);
                }
            }

            if (history == null || history.SelectionCount == 0)
            {
                sb.AppendLine("  rescue selections: none retained");
            }
            else
            {
                sb.AppendLine("  rescue selection ring: retained=" + history.SelectionCount +
                    "; droppedOldest=" + history.SelectionDropped +
                    "; capacity=" + RescueRingCapacity);
                for (int offset = history.SelectionCount - 1; offset >= 0; offset--)
                {
                    int index = RingIndex(
                        history.SelectionNext, history.SelectionCount, RescueRingCapacity, offset);
                    RescueSelectionObservation selection = history.Selections[index];
                    if (selection == null)
                    {
                        continue;
                    }

                    sb.AppendLine("  selection tick=" + selection.Tick +
                        "; rescuer=" + PawnName(selection.Rescuer) +
                        "; patient=" + PawnName(selection.Patient) +
                        "; forced=" + selection.Forced +
                        "; rescueJob=" + selection.RescueJobText);
                    sb.AppendLine("    decisionBed=" + SnapshotText(selection.DecisionBedState) +
                        "; validAtDecision=" + NullableBool(selection.DecisionBedValid) +
                        "; AnyUnoccupiedSleepingSlotAtDecision=" +
                        SnapshotAny(selection.DecisionBedState));
                    if (selection.DecisionBed != null && failureBed != null &&
                        SameBed(selection.DecisionBed, failureBed))
                    {
                        sb.AppendLine("    decisionToFailureComparison: sameBed=true" +
                            "; selectedValid=" + NullableBool(selection.DecisionBedValid) +
                            "; selectedAnyUnoccupied=" + SnapshotAny(selection.DecisionBedState) +
                            "; failureAnyUnoccupied=" + failureCheck.AnyUnoccupiedSleepingSlot +
                            "; failureUnownedEmpty=" + failureCheck.UnownedEmptySlot +
                            "; allThreeFailedAtFailure=" + failureCheck.AllThreeVanillaChecksFailed +
                            "; validAndFreeThenBadNow=" +
                            (selection.DecisionBedValid == true &&
                                selection.DecisionBedState != null &&
                                selection.DecisionBedState.AnyUnoccupiedSleepingSlot == true &&
                                failureCheck.AllThreeVanillaChecksFailed));
                    }
                    else
                    {
                        sb.AppendLine("    decisionToFailureComparison: sameBed=falseOrUnknown");
                    }
                    sb.AppendLine("    jobBuildBed=" + SnapshotText(selection.JobBuildBedState) +
                        "; validAtJobBuild=" + NullableBool(selection.JobBuildBedValid));
                    sb.AppendLine("    jobBuildToFailureComparison: sameBed=" +
                        SameBed(selection.JobBuildBed, failureBed) +
                        "; jobBuildValid=" + NullableBool(selection.JobBuildBedValid) +
                        "; jobBuildAnyUnoccupied=" + SnapshotAny(selection.JobBuildBedState) +
                        "; failureAnyUnoccupied=" + failureCheck.AnyUnoccupiedSleepingSlot);
                    AppendStack(sb, "    rescueJobSelectionStack", selection.Stack);
                }
            }

            if (history == null || history.TuckCount == 0)
            {
                sb.AppendLine("  tucks: none retained");
            }
            else
            {
                sb.AppendLine("  tuck ring: retained=" + history.TuckCount +
                    "; droppedOldest=" + history.TuckDropped +
                    "; capacity=" + RescueRingCapacity);
                for (int offset = history.TuckCount - 1; offset >= 0; offset--)
                {
                    int index = RingIndex(history.TuckNext, history.TuckCount, RescueRingCapacity, offset);
                    TuckObservation tuck = history.Tucks[index];
                    if (tuck == null)
                    {
                        continue;
                    }

                    bool exactSelection = tuck.MatchedSelection != null &&
                        tuck.MatchedSelection.Rescuer == tuck.Taker;
                    bool diverged = exactSelection &&
                        !SameBed(tuck.MatchedSelection.DecisionBed, tuck.FinalBed);
                    sb.AppendLine("  tuck tick=" + tuck.Tick +
                        "; taker=" + PawnName(tuck.Taker) +
                        "; takee=" + PawnName(tuck.Takee) +
                        "; finalTargetBed=" + SnapshotText(tuck.FinalBedState) +
                        "; rescuedArgument=" + tuck.RescuedArgument +
                        "; takerWasRescueJob=" + tuck.TakerWasRescueJob +
                        "; CanUseBedNowAtTuck=" + NullableBool(tuck.CanUseBedAtTuck) +
                        "; matchedDecisionBed=" + SnapshotText(
                            tuck.MatchedSelection == null ? null : tuck.MatchedSelection.DecisionBedState) +
                        "; matchedSelectionIsExactTaker=" + exactSelection +
                        "; decisionToFinalDivergence=" +
                        (!exactSelection ? "UNKNOWN" : diverged.ToString().ToUpperInvariant()) +
                        "; layDownStartedFromThisTuck=" +
                        (tuck.LayDownJobLoadID < 0 ? "not observed" : tuck.LayDownJobLoadID.ToString()));
                    sb.AppendLine("    takerJobAtTuck=" + tuck.TakerJob);
                    AppendStack(sb, "    tuckStack", tuck.Stack);
                }
            }

            string origin;
            if (exactTuck || taggedTuck)
            {
                origin = "RESCUE/TUCK evidence attached to this LayDown job";
            }
            else if (history != null &&
                (history.LookupCount > 0 || history.SelectionCount > 0 || history.TuckCount > 0))
            {
                origin = "RESCUE/TUCK evidence exists for this employee but is not attached to this job";
            }
            else if (history != null &&
                (history.LookupDropped > 0 || history.SelectionDropped > 0 || history.TuckDropped > 0))
            {
                origin = "rescue/tuck ring overflowed; origin is unknown from retained evidence";
            }
            else
            {
                origin = "no rescue/tuck evidence retained; consistent with unrelated rest flow";
            }
            sb.AppendLine("  originAssessment=" + origin);
        }

        private static void AppendLifecycle(
            StringBuilder sb, PawnHistory history, Job evaluatedJob)
        {
            sb.AppendLine("layDownLifecycleRing: capacity=" + LifecycleRingCapacity +
                "; retained=" + (history == null ? 0 : history.LifecycleCount) +
                "; droppedOldest=" + (history == null ? 0 : history.LifecycleDropped));
            if (history == null || history.LifecycleCount == 0)
            {
                sb.AppendLine("  none retained");
                return;
            }

            string previousCreationStack = null;
            for (int offset = history.LifecycleCount - 1; offset >= 0; offset--)
            {
                int index = RingIndex(
                    history.LifecycleNext, history.LifecycleCount, LifecycleRingCapacity, offset);
                LifecycleObservation lifecycle = history.Lifecycle[index];
                if (lifecycle == null)
                {
                    continue;
                }

                sb.AppendLine("  lifecycle tick=" + lifecycle.Tick +
                    "; kind=" + lifecycle.Kind +
                    "; jobLoadID=" + lifecycle.JobLoadID +
                    "; detail=" + lifecycle.Detail +
                    "; job=" + lifecycle.Job);
                if (lifecycle.CreationSource == null)
                {
                    sb.AppendLine("    creationAtEvent=<not captured>");
                }
                else
                {
                    sb.AppendLine("    creationAtEvent=source=" + lifecycle.CreationSource +
                        "; tick=" + lifecycle.CreationTick +
                        "; exactAtCreation=" + lifecycle.CreationExactAtCreation +
                        "; bed=" + SnapshotText(lifecycle.CreationBed));
                    if (lifecycle.CreationStack != previousCreationStack)
                    {
                        AppendStack(sb, "    creationStack", lifecycle.CreationStack);
                        previousCreationStack = lifecycle.CreationStack;
                    }
                    else
                    {
                        sb.AppendLine("    creationStack=<same as preceding lifecycle event>");
                    }
                }
                AppendStack(sb, "    lifecycleEventStack", lifecycle.EventStack);
            }

        }

        private static SlotCheck ReadVanillaSlotChecks(Pawn pawn, Building_Bed bed)
        {
            SlotCheck check = new SlotCheck();
            check.OwnsBed = bed.IsOwner(pawn, out check.AssignedSlot);
            int slotCount = bed.SleepingSlotsCount;
            List<Pawn> owners = bed.OwnersForReading;

            for (int i = 0; i < slotCount; i++)
            {
                Pawn occupant = bed.GetCurOccupant(i);
                if ((i >= owners.Count || owners[i] == null) && occupant == pawn)
                {
                    check.UnownedSlotOccupiedByPawn = true;
                    break;
                }
            }

            for (int i = 0; i < slotCount; i++)
            {
                Pawn occupant = bed.GetCurOccupant(i);
                if ((i >= owners.Count || owners[i] == null) && occupant == null)
                {
                    check.UnownedEmptySlot = true;
                    break;
                }
            }

            check.AnyUnoccupiedSleepingSlot = bed.AnyUnoccupiedSleepingSlot;
            return check;
        }

        private static BedSnapshot CaptureBedSnapshot(Building_Bed bed)
        {
            BedSnapshot snapshot = new BedSnapshot { HasBed = bed != null };
            if (bed == null)
            {
                return snapshot;
            }

            try
            {
                snapshot.ThingID = bed.ThingID;
                snapshot.Position = bed.Position.ToString();
                snapshot.DefName = bed.def == null ? "<null>" : bed.def.defName;
                snapshot.Medical = bed.Medical;
                snapshot.SleepingSlotsCount = bed.SleepingSlotsCount;
                snapshot.AnyUnoccupiedSleepingSlot = bed.AnyUnoccupiedSleepingSlot;
            }
            catch (Exception ex)
            {
                snapshot.Error = ExceptionText(ex);
            }
            return snapshot;
        }

        private static bool? TryIsValidRescueBed(
            Building_Bed bed, Pawn patient, Pawn rescuer)
        {
            try
            {
                if (bed == null || patient == null || rescuer == null)
                {
                    return false;
                }

                return RestUtility.IsValidBedFor(
                    bed, patient, rescuer, checkSocialProperness: false,
                    allowMedBedEvenIfSetToNoCare: false, ignoreOtherReservations: false,
                    guestStatus: patient.GuestStatus);
            }
            catch
            {
                return null;
            }
        }

        private static bool? TryCanUseBedNow(Building_Bed bed, Pawn takee)
        {
            try
            {
                if (bed == null || takee == null)
                {
                    return false;
                }

                return RestUtility.CanUseBedNow(
                    bed, takee, checkSocialProperness: false,
                    allowMedBedEvenIfSetToNoCare: false,
                    guestStatusOverride: takee.GuestStatus);
            }
            catch
            {
                return null;
            }
        }

        private static bool SameBed(Building_Bed first, Building_Bed second)
        {
            if (first == second)
            {
                return true;
            }
            if (first == null || second == null)
            {
                return false;
            }
            try
            {
                return first.ThingID == second.ThingID;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsLayDown(Job job)
        {
            return job != null && job.def != null && job.def == JobDefOf.LayDown;
        }

        private static int FindQueueIndex(JobQueue queue, Job job)
        {
            if (queue == null || job == null)
            {
                return -1;
            }

            for (int i = 0; i < queue.Count; i++)
            {
                QueuedJob queued = queue[i];
                if (queued != null && queued.job == job)
                {
                    return i;
                }
            }
            return -1;
        }

        private static string EvaluationLocation(bool current, int queueIndex)
        {
            if (current && queueIndex >= 0)
            {
                return "CURRENT_AND_QUEUE[" + queueIndex + "]";
            }
            if (current)
            {
                return "CURRENT";
            }
            if (queueIndex >= 0)
            {
                return "QUEUE[" + queueIndex + "]";
            }
            return "NEITHER_AT_ENTRY";
        }

        private static string DescribeJob(Job job)
        {
            try
            {
                if (job == null)
                {
                    return "<null>";
                }

                string defName = job.def == null ? "<null>" : job.def.defName;
                return "def=" + defName + "; loadID=" + job.loadID +
                    "; targets={A:" + DescribeTarget(job.targetA) +
                    ", B:" + DescribeTarget(job.targetB) +
                    ", C:" + DescribeTarget(job.targetC) + "}";
            }
            catch (Exception ex)
            {
                return "<job read failed: " + ExceptionText(ex) + ">";
            }
        }

        private static string DescribeTargets(Job job)
        {
            if (job == null)
            {
                return "A=<null>; B=<null>; C=<null>";
            }
            return "A=" + DescribeTarget(job.targetA) +
                "; B=" + DescribeTarget(job.targetB) +
                "; C=" + DescribeTarget(job.targetC);
        }

        private static string DescribeTarget(LocalTargetInfo target)
        {
            try
            {
                Thing thing = target.HasThing ? target.Thing : null;
                return "valid=" + target.IsValid + "; cell=" + target.Cell +
                    "; thingID=" + (thing == null ? "<none>" : thing.ThingID) +
                    "; thingPos=" + (thing == null ? "<none>" : thing.Position.ToString()) +
                    "; thingDef=" + (thing == null || thing.def == null ?
                        "<none>" : thing.def.defName);
            }
            catch (Exception ex)
            {
                return "<target read failed: " + ExceptionText(ex) + ">";
            }
        }

        private static string BedText(Building_Bed bed)
        {
            return SnapshotText(CaptureBedSnapshot(bed));
        }

        private static string SnapshotText(BedSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.HasBed)
            {
                return "<no bed target>";
            }

            return "ThingID=" + snapshot.ThingID +
                "; position=" + snapshot.Position +
                "; def=" + snapshot.DefName +
                "; Medical=" + NullableBool(snapshot.Medical) +
                "; SleepingSlotsCount=" + NullableInt(snapshot.SleepingSlotsCount) +
                "; AnyUnoccupiedSleepingSlot=" + NullableBool(snapshot.AnyUnoccupiedSleepingSlot) +
                (snapshot.Error == null ? "" : "; readError=" + snapshot.Error);
        }

        private static string SnapshotAny(BedSnapshot snapshot)
        {
            return snapshot == null || !snapshot.HasBed
                ? "<n/a>" : NullableBool(snapshot.AnyUnoccupiedSleepingSlot);
        }

        private static string PawnName(Pawn pawn)
        {
            try
            {
                return pawn == null ? "<null>" : pawn.LabelShort;
            }
            catch (Exception ex)
            {
                return "<pawn name failed: " + ExceptionText(ex) + ">";
            }
        }

        private static string ThingID(Thing thing)
        {
            try
            {
                return thing == null ? "<null>" : thing.ThingID;
            }
            catch (Exception ex)
            {
                return "<thing ID failed: " + ExceptionText(ex) + ">";
            }
        }

        private static string FormatTag(JobTag? tag)
        {
            return tag.HasValue ? tag.Value.ToString() : "<none>";
        }

        private static string FormatActiveTuck(Pawn pawn)
        {
            return activeTuck == null || activeTuck.Takee != pawn
                ? "none"
                : "takee=" + PawnName(activeTuck.Takee) + "; finalBed=" + BedText(activeTuck.Bed) +
                    "; rescued=" + activeTuck.Observation.RescuedArgument;
        }

        private static string NullableBool(bool? value)
        {
            return value.HasValue ? value.Value.ToString() : "<unknown>";
        }

        private static string NullableInt(int? value)
        {
            return value.HasValue ? value.Value.ToString() : "<unknown>";
        }

        private static string Join(List<string> values)
        {
            return values == null || values.Count == 0 ? "none" : string.Join(", ", values);
        }

        private static int CurrentTick()
        {
            try
            {
                return Find.TickManager == null ? -1 : Find.TickManager.TicksGame;
            }
            catch
            {
                return -1;
            }
        }

        private static long NextSequence()
        {
            try
            {
                return Interlocked.Increment(ref nextSequence);
            }
            catch
            {
                return 0;
            }
        }

        private static string CaptureStack()
        {
            try
            {
                return Environment.StackTrace;
            }
            catch (Exception ex)
            {
                return "<stack unavailable: " + ExceptionText(ex) + ">";
            }
        }

        private static void AppendStack(StringBuilder sb, string label, string stack)
        {
            sb.AppendLine(label + ":");
            sb.AppendLine(string.IsNullOrEmpty(stack) ? "  <not captured>" : stack);
        }

        private static string ExceptionText(Exception ex)
        {
            try
            {
                return ex == null ? "<null exception>" : ex.GetType().Name + ": " + ex.Message;
            }
            catch
            {
                return "<exception text unavailable>";
            }
        }

        internal static void ReportPatchFailure(string context, Exception ex)
        {
            ReportInternalFailure(context, ex);
        }

        private static void ReportInternalFailure(string context, Exception ex)
        {
            try
            {
                if (!DiagnosticsEnabled())
                {
                    return;
                }

                if (Interlocked.Exchange(ref internalFailureReported, 1) == 0)
                {
                    IntercolonyLog.Error("TEMPORARY BED DIAGNOSTICS failed while " + context +
                        "; subsequent instrumentation failures suppressed: " + ExceptionText(ex));
                }
            }
            catch
            {
                // Never allow a diagnostic failure to escape into the observed game path.
            }
        }

        private static bool DiagnosticsEnabled()
        {
            return Enabled;
        }

        // Harmony resolves an annotated target only after Prepare() returns true. Keep all
        // resolution here explicit and exact so a changed or ambiguous game API becomes a
        // skipped diagnostic, never an exception from the production PatchAll() call.
        private static MethodInfo ResolveMethodExact(
            Type declaringType, string methodName, Type[] argumentTypes, string targetName)
        {
            if (declaringType == null)
            {
                throw new ArgumentNullException(nameof(declaringType));
            }

            Type[] expectedArguments = argumentTypes ?? Type.EmptyTypes;
            MethodInfo match = null;
            int matchCount = 0;
            MethodInfo[] candidates = declaringType.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static);

            for (int i = 0; i < candidates.Length; i++)
            {
                MethodInfo candidate = candidates[i];
                if (candidate.Name != methodName ||
                    !ParameterTypesMatch(candidate.GetParameters(), expectedArguments))
                {
                    continue;
                }

                match = candidate;
                matchCount++;
            }

            if (matchCount != 1)
            {
                throw new MissingMethodException(
                    targetName + " resolved to " + matchCount + " methods.");
            }

            return match;
        }

        private static ConstructorInfo ResolveConstructorExact(
            Type declaringType, Type[] argumentTypes, string targetName)
        {
            if (declaringType == null)
            {
                throw new ArgumentNullException(nameof(declaringType));
            }

            Type[] expectedArguments = argumentTypes ?? Type.EmptyTypes;
            ConstructorInfo match = null;
            int matchCount = 0;
            ConstructorInfo[] candidates = declaringType.GetConstructors(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            for (int i = 0; i < candidates.Length; i++)
            {
                ConstructorInfo candidate = candidates[i];
                if (!ParameterTypesMatch(candidate.GetParameters(), expectedArguments))
                {
                    continue;
                }

                match = candidate;
                matchCount++;
            }

            if (matchCount != 1)
            {
                throw new MissingMethodException(
                    targetName + " resolved to " + matchCount + " constructors.");
            }

            return match;
        }

        private static bool ParameterTypesMatch(
            ParameterInfo[] parameters, Type[] expectedArguments)
        {
            if (parameters == null || expectedArguments == null ||
                parameters.Length != expectedArguments.Length)
            {
                return false;
            }

            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].ParameterType != expectedArguments[i])
                {
                    return false;
                }
            }

            return true;
        }

        internal static MethodBase ResolveJobGiverTarget()
        {
            return ResolveMethodExact(
                typeof(ThinkNode_JobGiver), nameof(ThinkNode_JobGiver.TryIssueJobPackage),
                new Type[] { typeof(Pawn), typeof(JobIssueParams) },
                "ThinkNode_JobGiver.TryIssueJobPackage(Pawn, JobIssueParams)");
        }

        internal static MethodBase ResolveHospitalitySleepTarget()
        {
            Type type = AccessTools.TypeByName("Hospitality.JobGiver_Sleep");
            return type == null
                ? null
                : ResolveMethodExact(
                    type, "TryIssueJobPackage",
                    new Type[] { typeof(Pawn), typeof(JobIssueParams) },
                    "Hospitality.JobGiver_Sleep.TryIssueJobPackage(Pawn, JobIssueParams)");
        }

        internal static MethodBase ResolveJobConstructorTarget(
            Type[] argumentTypes, string targetName)
        {
            return ResolveConstructorExact(typeof(Job), argumentTypes, targetName);
        }

        internal static List<MethodBase> ResolveJobConstructorTargets()
        {
            return new List<MethodBase>
            {
                ResolveJobConstructorTarget(
                    Type.EmptyTypes, "Job..ctor()"),
                ResolveJobConstructorTarget(
                    new Type[] { typeof(JobDef) }, "Job..ctor(JobDef)"),
                ResolveJobConstructorTarget(
                    new Type[] { typeof(JobDef), typeof(LocalTargetInfo) },
                    "Job..ctor(JobDef, LocalTargetInfo)"),
                ResolveJobConstructorTarget(
                    new Type[]
                    {
                        typeof(JobDef), typeof(LocalTargetInfo), typeof(LocalTargetInfo)
                    },
                    "Job..ctor(JobDef, LocalTargetInfo, LocalTargetInfo)"),
                ResolveJobConstructorTarget(
                    new Type[]
                    {
                        typeof(JobDef), typeof(LocalTargetInfo), typeof(LocalTargetInfo),
                        typeof(LocalTargetInfo)
                    },
                    "Job..ctor(JobDef, LocalTargetInfo, LocalTargetInfo, LocalTargetInfo)"),
                ResolveJobConstructorTarget(
                    new Type[] { typeof(JobDef), typeof(LocalTargetInfo), typeof(int), typeof(bool) },
                    "Job..ctor(JobDef, LocalTargetInfo, int, bool)"),
                ResolveJobConstructorTarget(
                    new Type[] { typeof(JobDef), typeof(int), typeof(bool) },
                    "Job..ctor(JobDef, int, bool)")
            };
        }

        internal static MethodBase ResolveJobMakerTarget(
            Type[] argumentTypes, string targetName)
        {
            return ResolveMethodExact(
                typeof(JobMaker), nameof(JobMaker.MakeJob), argumentTypes, targetName);
        }

        internal static List<MethodBase> ResolveJobMakerTargets()
        {
            return new List<MethodBase>
            {
                ResolveJobMakerTarget(
                    new Type[] { typeof(JobDef) },
                    "JobMaker.MakeJob(JobDef)"),
                ResolveJobMakerTarget(
                    new Type[] { typeof(JobDef), typeof(LocalTargetInfo) },
                    "JobMaker.MakeJob(JobDef, LocalTargetInfo)"),
                ResolveJobMakerTarget(
                    new Type[]
                    {
                        typeof(JobDef), typeof(LocalTargetInfo), typeof(LocalTargetInfo)
                    },
                    "JobMaker.MakeJob(JobDef, LocalTargetInfo, LocalTargetInfo)"),
                ResolveJobMakerTarget(
                    new Type[]
                    {
                        typeof(JobDef), typeof(LocalTargetInfo), typeof(LocalTargetInfo),
                        typeof(LocalTargetInfo)
                    },
                    "JobMaker.MakeJob(JobDef, LocalTargetInfo, LocalTargetInfo, LocalTargetInfo)"),
                ResolveJobMakerTarget(
                    new Type[] { typeof(JobDef), typeof(LocalTargetInfo), typeof(int), typeof(bool) },
                    "JobMaker.MakeJob(JobDef, LocalTargetInfo, int, bool)"),
                ResolveJobMakerTarget(
                    new Type[] { typeof(JobDef), typeof(int), typeof(bool) },
                    "JobMaker.MakeJob(JobDef, int, bool)")
            };
        }

        internal static MethodBase ResolveReturnToPoolTarget()
        {
            return ResolveMethodExact(
                typeof(JobMaker), nameof(JobMaker.ReturnToPool), new Type[] { typeof(Job) },
                "JobMaker.ReturnToPool(Job)");
        }

        internal static MethodBase ResolveJobEvaluationTarget()
        {
            return ResolveMethodExact(
                typeof(Job), nameof(Job.CanBeginNow), new Type[] { typeof(Pawn), typeof(bool) },
                "Job.CanBeginNow(Pawn, bool)");
        }

        internal static MethodBase ResolveSleepingSlotTarget()
        {
            return ResolveMethodExact(
                typeof(RestUtility), nameof(RestUtility.GetBedSleepingSlotPosFor),
                new Type[] { typeof(Pawn), typeof(Building_Bed) },
                "RestUtility.GetBedSleepingSlotPosFor(Pawn, Building_Bed)");
        }

        internal static MethodBase ResolveTrackerConstructorTarget()
        {
            return ResolveConstructorExact(
                typeof(Pawn_JobTracker), new Type[] { typeof(Pawn) },
                "Pawn_JobTracker..ctor(Pawn)");
        }

        internal static MethodBase ResolveTrackerExposeDataTarget()
        {
            return ResolveMethodExact(
                typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.ExposeData), Type.EmptyTypes,
                "Pawn_JobTracker.ExposeData()");
        }

        internal static MethodBase ResolveTrackerStartJobTarget()
        {
            return ResolveMethodExact(
                typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob),
                new Type[]
                {
                    typeof(Job), typeof(JobCondition), typeof(ThinkNode), typeof(bool), typeof(bool),
                    typeof(ThinkTreeDef), typeof(Nullable<JobTag>), typeof(bool), typeof(bool),
                    typeof(Nullable<bool>), typeof(bool), typeof(bool), typeof(bool)
                },
                "Pawn_JobTracker.StartJob(Job, JobCondition, ThinkNode, bool, bool, ThinkTreeDef, " +
                "JobTag?, bool, bool, bool?, bool, bool, bool)");
        }

        internal static MethodBase ResolveTrackerEndCurrentJobTarget()
        {
            return ResolveMethodExact(
                typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob),
                new Type[] { typeof(JobCondition), typeof(bool), typeof(bool) },
                "Pawn_JobTracker.EndCurrentJob(JobCondition, bool, bool)");
        }

        internal static MethodBase ResolveTrackerTryTakeOrderedJobTarget()
        {
            return ResolveMethodExact(
                typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob),
                new Type[] { typeof(Job), typeof(Nullable<JobTag>), typeof(bool) },
                "Pawn_JobTracker.TryTakeOrderedJob(Job, JobTag?, bool)");
        }

        internal static MethodBase ResolveEnqueueFirstTarget()
        {
            return ResolveMethodExact(
                typeof(JobQueue), nameof(JobQueue.EnqueueFirst),
                new Type[] { typeof(Job), typeof(Nullable<JobTag>) },
                "JobQueue.EnqueueFirst(Job, JobTag?)");
        }

        internal static MethodBase ResolveEnqueueLastTarget()
        {
            return ResolveMethodExact(
                typeof(JobQueue), nameof(JobQueue.EnqueueLast),
                new Type[] { typeof(Job), typeof(Nullable<JobTag>) },
                "JobQueue.EnqueueLast(Job, JobTag?)");
        }

        internal static MethodBase ResolveRescueDecisionTarget()
        {
            return ResolveMethodExact(
                typeof(WorkGiver_RescueDowned), nameof(WorkGiver_RescueDowned.HasJobOnThing),
                new Type[] { typeof(Pawn), typeof(Thing), typeof(bool) },
                "WorkGiver_RescueDowned.HasJobOnThing(Pawn, Thing, bool)");
        }

        internal static MethodBase ResolveRescueJobTarget()
        {
            return ResolveMethodExact(
                typeof(WorkGiver_RescueDowned), nameof(WorkGiver_RescueDowned.JobOnThing),
                new Type[] { typeof(Pawn), typeof(Thing), typeof(bool) },
                "WorkGiver_RescueDowned.JobOnThing(Pawn, Thing, bool)");
        }

        internal static MethodBase ResolveFindBedForTarget()
        {
            return ResolveMethodExact(
                typeof(RestUtility), nameof(RestUtility.FindBedFor),
                new Type[]
                {
                    typeof(Pawn), typeof(Pawn), typeof(bool), typeof(bool),
                    typeof(Nullable<GuestStatus>)
                },
                "RestUtility.FindBedFor(Pawn, Pawn, bool, bool, GuestStatus?)");
        }

        internal static MethodBase ResolveTuckTarget()
        {
            return ResolveMethodExact(
                typeof(RestUtility), nameof(RestUtility.TuckIntoBed),
                new Type[] { typeof(Building_Bed), typeof(Pawn), typeof(Pawn), typeof(bool) },
                "RestUtility.TuckIntoBed(Building_Bed, Pawn, Pawn, bool)");
        }

        internal static MethodBase ResolveStartupCheckTarget()
        {
            return ResolveMethodExact(
                typeof(IntercolonyLog), nameof(IntercolonyLog.Verbose), new Type[] { typeof(string) },
                "IntercolonyLog.Verbose(string)");
        }

        internal static bool PrepareTarget(string targetName, Func<MethodBase> resolver)
        {
            if (!DiagnosticsEnabled())
            {
                return false;
            }

            try
            {
                MethodBase target = resolver();
                if (target != null)
                {
                    return true;
                }

                ReportRegistrationFailure(targetName, "target resolved to null; patch skipped.");
            }
            catch (Exception ex)
            {
                ReportRegistrationFailure(targetName, ex);
            }

            return false;
        }

        internal static bool PrepareTargetSet(
            string targetName, Func<List<MethodBase>> resolver, int expectedCount)
        {
            if (!DiagnosticsEnabled())
            {
                return false;
            }

            try
            {
                List<MethodBase> targets = resolver();
                if (targets == null || targets.Count != expectedCount)
                {
                    ReportRegistrationFailure(
                        targetName,
                        "expected " + expectedCount + " distinct targets, but resolved " +
                        (targets == null ? "null" : targets.Count.ToString()) + "; patch skipped.");
                    return false;
                }

                for (int i = 0; i < targets.Count; i++)
                {
                    if (targets[i] == null)
                    {
                        ReportRegistrationFailure(
                            targetName, "target " + (i + 1) + " resolved to null; patch skipped.");
                        return false;
                    }

                    for (int j = i + 1; j < targets.Count; j++)
                    {
                        if (targets[i] == targets[j])
                        {
                            ReportRegistrationFailure(
                                targetName,
                                "target " + (i + 1) + " duplicated target " + (j + 1) +
                                "; patch skipped.");
                            return false;
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                ReportRegistrationFailure(targetName, ex);
                return false;
            }
        }

        internal static void ReportRegistrationFailure(string targetName, Exception ex)
        {
            ReportRegistrationFailure(targetName, ExceptionText(ex));
        }

        internal static void ReportRegistrationFailure(string targetName, string detail)
        {
            try
            {
                if (DiagnosticsEnabled())
                {
                    IntercolonyLog.Error(
                        "TEMPORARY BED DIAGNOSTICS PATCH REGISTRATION FAILED for " + targetName +
                        ": " + detail);
                }
            }
            catch
            {
                // Registration reporting must never become a second startup failure.
            }
        }

        private static bool HasExpectedPatch(MethodBase target, Type patchType)
        {
            Patches patchInfo = Harmony.GetPatchInfo(target);
            return patchInfo != null &&
                (ContainsExpectedPatch(patchInfo.Prefixes, patchType) ||
                 ContainsExpectedPatch(patchInfo.Postfixes, patchType) ||
                 ContainsExpectedPatch(patchInfo.Transpilers, patchType) ||
                 ContainsExpectedPatch(patchInfo.Finalizers, patchType));
        }

        private static bool ContainsExpectedPatch(IEnumerable<Patch> patches, Type patchType)
        {
            if (patches == null)
            {
                return false;
            }

            foreach (Patch patch in patches)
            {
                try
                {
                    if (patch != null && patch.owner == DiagnosticHarmonyId &&
                        patch.PatchMethod != null && patch.PatchMethod.DeclaringType == patchType)
                    {
                        return true;
                    }
                }
                catch
                {
                    // Ignore one unreadable patch record and inspect the remaining records.
                }
            }

            return false;
        }

        private static void VerifyTargetPatch(
            string targetName, Func<MethodBase> resolver, Type patchType)
        {
            try
            {
                MethodBase target = resolver();
                if (target == null)
                {
                    ReportRegistrationFailure(
                        targetName, "startup self-check resolved the target to null.");
                }
                else if (!HasExpectedPatch(target, patchType))
                {
                    ReportRegistrationFailure(
                        targetName,
                        "startup self-check resolved exactly one method, but no diagnostic patch " +
                        "from " + patchType.FullName + " is present after PatchAll.");
                }
            }
            catch (Exception ex)
            {
                ReportRegistrationFailure(targetName, ex);
            }
        }

        internal static void VerifyStartupPatches()
        {
            if (!DiagnosticsEnabled() || Interlocked.Exchange(ref startupPatchCheckReported, 1) != 0)
            {
                return;
            }

            try
            {
                VerifyTargetPatch(
                    "ThinkNode_JobGiver.TryIssueJobPackage(Pawn, JobIssueParams)",
                    ResolveJobGiverTarget, typeof(IntercolonyBedDiagnostics_JobGiverPatch));

                bool hospitalityPresent = false;
                try
                {
                    hospitalityPresent = AccessTools.TypeByName("Hospitality.JobGiver_Sleep") != null;
                }
                catch (Exception ex)
                {
                    ReportRegistrationFailure("Hospitality.JobGiver_Sleep type lookup", ex);
                }

                if (hospitalityPresent)
                {
                    VerifyTargetPatch(
                        "Hospitality.JobGiver_Sleep.TryIssueJobPackage(Pawn, JobIssueParams)",
                        ResolveHospitalitySleepTarget,
                        typeof(IntercolonyBedDiagnostics_HospitalitySleepPatch));
                }

                VerifyTargetPatch(
                    "Job..ctor()",
                    () => ResolveJobConstructorTarget(Type.EmptyTypes, "Job..ctor()"),
                    typeof(IntercolonyBedDiagnostics_JobConstructorPatch));
                VerifyTargetPatch(
                    "Job..ctor(JobDef)",
                    () => ResolveJobConstructorTarget(
                        new Type[] { typeof(JobDef) }, "Job..ctor(JobDef)"),
                    typeof(IntercolonyBedDiagnostics_JobConstructorPatch));
                VerifyTargetPatch(
                    "Job..ctor(JobDef, LocalTargetInfo)",
                    () => ResolveJobConstructorTarget(
                        new Type[] { typeof(JobDef), typeof(LocalTargetInfo) },
                        "Job..ctor(JobDef, LocalTargetInfo)"),
                    typeof(IntercolonyBedDiagnostics_JobConstructorPatch));
                VerifyTargetPatch(
                    "Job..ctor(JobDef, LocalTargetInfo, LocalTargetInfo)",
                    () => ResolveJobConstructorTarget(
                        new Type[]
                        {
                            typeof(JobDef), typeof(LocalTargetInfo), typeof(LocalTargetInfo)
                        },
                        "Job..ctor(JobDef, LocalTargetInfo, LocalTargetInfo)"),
                    typeof(IntercolonyBedDiagnostics_JobConstructorPatch));
                VerifyTargetPatch(
                    "Job..ctor(JobDef, LocalTargetInfo, LocalTargetInfo, LocalTargetInfo)",
                    () => ResolveJobConstructorTarget(
                        new Type[]
                        {
                            typeof(JobDef), typeof(LocalTargetInfo), typeof(LocalTargetInfo),
                            typeof(LocalTargetInfo)
                        },
                        "Job..ctor(JobDef, LocalTargetInfo, LocalTargetInfo, LocalTargetInfo)"),
                    typeof(IntercolonyBedDiagnostics_JobConstructorPatch));
                VerifyTargetPatch(
                    "Job..ctor(JobDef, LocalTargetInfo, int, bool)",
                    () => ResolveJobConstructorTarget(
                        new Type[] { typeof(JobDef), typeof(LocalTargetInfo), typeof(int), typeof(bool) },
                        "Job..ctor(JobDef, LocalTargetInfo, int, bool)"),
                    typeof(IntercolonyBedDiagnostics_JobConstructorPatch));
                VerifyTargetPatch(
                    "Job..ctor(JobDef, int, bool)",
                    () => ResolveJobConstructorTarget(
                        new Type[] { typeof(JobDef), typeof(int), typeof(bool) },
                        "Job..ctor(JobDef, int, bool)"),
                    typeof(IntercolonyBedDiagnostics_JobConstructorPatch));

                VerifyTargetPatch(
                    "JobMaker.MakeJob(JobDef)",
                    () => ResolveJobMakerTarget(
                        new Type[] { typeof(JobDef) }, "JobMaker.MakeJob(JobDef)"),
                    typeof(IntercolonyBedDiagnostics_JobMakerPatch));
                VerifyTargetPatch(
                    "JobMaker.MakeJob(JobDef, LocalTargetInfo)",
                    () => ResolveJobMakerTarget(
                        new Type[] { typeof(JobDef), typeof(LocalTargetInfo) },
                        "JobMaker.MakeJob(JobDef, LocalTargetInfo)"),
                    typeof(IntercolonyBedDiagnostics_JobMakerPatch));
                VerifyTargetPatch(
                    "JobMaker.MakeJob(JobDef, LocalTargetInfo, LocalTargetInfo)",
                    () => ResolveJobMakerTarget(
                        new Type[]
                        {
                            typeof(JobDef), typeof(LocalTargetInfo), typeof(LocalTargetInfo)
                        },
                        "JobMaker.MakeJob(JobDef, LocalTargetInfo, LocalTargetInfo)"),
                    typeof(IntercolonyBedDiagnostics_JobMakerPatch));
                VerifyTargetPatch(
                    "JobMaker.MakeJob(JobDef, LocalTargetInfo, LocalTargetInfo, LocalTargetInfo)",
                    () => ResolveJobMakerTarget(
                        new Type[]
                        {
                            typeof(JobDef), typeof(LocalTargetInfo), typeof(LocalTargetInfo),
                            typeof(LocalTargetInfo)
                        },
                        "JobMaker.MakeJob(JobDef, LocalTargetInfo, LocalTargetInfo, LocalTargetInfo)"),
                    typeof(IntercolonyBedDiagnostics_JobMakerPatch));
                VerifyTargetPatch(
                    "JobMaker.MakeJob(JobDef, LocalTargetInfo, int, bool)",
                    () => ResolveJobMakerTarget(
                        new Type[] { typeof(JobDef), typeof(LocalTargetInfo), typeof(int), typeof(bool) },
                        "JobMaker.MakeJob(JobDef, LocalTargetInfo, int, bool)"),
                    typeof(IntercolonyBedDiagnostics_JobMakerPatch));
                VerifyTargetPatch(
                    "JobMaker.MakeJob(JobDef, int, bool)",
                    () => ResolveJobMakerTarget(
                        new Type[] { typeof(JobDef), typeof(int), typeof(bool) },
                        "JobMaker.MakeJob(JobDef, int, bool)"),
                    typeof(IntercolonyBedDiagnostics_JobMakerPatch));

                VerifyTargetPatch(
                    "JobMaker.ReturnToPool(Job)",
                    ResolveReturnToPoolTarget, typeof(IntercolonyBedDiagnostics_JobPoolPatch));
                VerifyTargetPatch(
                    "Job.CanBeginNow(Pawn, bool)",
                    ResolveJobEvaluationTarget, typeof(IntercolonyBedDiagnostics_JobEvaluationPatch));
                VerifyTargetPatch(
                    "RestUtility.GetBedSleepingSlotPosFor(Pawn, Building_Bed)",
                    ResolveSleepingSlotTarget, typeof(IntercolonyBedDiagnostics_SleepingSlotPatch));
                VerifyTargetPatch(
                    "Pawn_JobTracker..ctor(Pawn)",
                    ResolveTrackerConstructorTarget,
                    typeof(IntercolonyBedDiagnostics_TrackerConstructorPatch));
                VerifyTargetPatch(
                    "Pawn_JobTracker.ExposeData()",
                    ResolveTrackerExposeDataTarget,
                    typeof(IntercolonyBedDiagnostics_TrackerExposeDataPatch));
                VerifyTargetPatch(
                    "Pawn_JobTracker.StartJob(Job, JobCondition, ThinkNode, bool, bool, " +
                    "ThinkTreeDef, JobTag?, bool, bool, bool?, bool, bool, bool)",
                    ResolveTrackerStartJobTarget,
                    typeof(IntercolonyBedDiagnostics_StartJobPatch));
                VerifyTargetPatch(
                    "Pawn_JobTracker.EndCurrentJob(JobCondition, bool, bool)",
                    ResolveTrackerEndCurrentJobTarget,
                    typeof(IntercolonyBedDiagnostics_EndCurrentJobPatch));
                VerifyTargetPatch(
                    "Pawn_JobTracker.TryTakeOrderedJob(Job, JobTag?, bool)",
                    ResolveTrackerTryTakeOrderedJobTarget,
                    typeof(IntercolonyBedDiagnostics_TryTakeOrderedJobPatch));
                VerifyTargetPatch(
                    "JobQueue.EnqueueFirst(Job, JobTag?)",
                    ResolveEnqueueFirstTarget, typeof(IntercolonyBedDiagnostics_EnqueueFirstPatch));
                VerifyTargetPatch(
                    "JobQueue.EnqueueLast(Job, JobTag?)",
                    ResolveEnqueueLastTarget, typeof(IntercolonyBedDiagnostics_EnqueueLastPatch));
                VerifyTargetPatch(
                    "WorkGiver_RescueDowned.HasJobOnThing(Pawn, Thing, bool)",
                    ResolveRescueDecisionTarget,
                    typeof(IntercolonyBedDiagnostics_RescueDecisionPatch));
                VerifyTargetPatch(
                    "WorkGiver_RescueDowned.JobOnThing(Pawn, Thing, bool)",
                    ResolveRescueJobTarget, typeof(IntercolonyBedDiagnostics_RescueJobPatch));
                VerifyTargetPatch(
                    "RestUtility.FindBedFor(Pawn, Pawn, bool, bool, GuestStatus?)",
                    ResolveFindBedForTarget, typeof(IntercolonyBedDiagnostics_FindBedForPatch));
                VerifyTargetPatch(
                    "RestUtility.TuckIntoBed(Building_Bed, Pawn, Pawn, bool)",
                    ResolveTuckTarget, typeof(IntercolonyBedDiagnostics_TuckPatch));
                VerifyTargetPatch(
                    "IntercolonyLog.Verbose(string)",
                    ResolveStartupCheckTarget,
                    typeof(IntercolonyBedDiagnostics_StartupCheckPatch));
            }
            catch (Exception ex)
            {
                ReportRegistrationFailure("startup diagnostic patch self-check", ex);
            }
        }
    }

    [HarmonyPatch(
        typeof(ThinkNode_JobGiver), nameof(ThinkNode_JobGiver.TryIssueJobPackage),
        new[] { typeof(Pawn), typeof(JobIssueParams) })]
    internal static class IntercolonyBedDiagnostics_JobGiverPatch
    {
        public static bool Prepare()
        {
            return IntercolonyBedDiagnostics.PrepareTarget(
                "ThinkNode_JobGiver.TryIssueJobPackage(Pawn, JobIssueParams)",
                IntercolonyBedDiagnostics.ResolveJobGiverTarget);
        }

        public static void Prefix(
            Pawn pawn, out IntercolonyBedDiagnostics.EmployeeJobCreationScope __state)
        {
            __state = null;
            try
            {
                __state = IntercolonyBedDiagnostics.BeginEmployeeJobCreation(pawn);
            }
            catch (Exception ex)
            {
                IntercolonyBedDiagnostics.ReportPatchFailure(
                    "ThinkNode_JobGiver.TryIssueJobPackage prefix", ex);
            }
        }

        public static void Postfix(IntercolonyBedDiagnostics.EmployeeJobCreationScope __state)
        {
            try
            {
                IntercolonyBedDiagnostics.EndEmployeeJobCreation(__state);
            }
            catch (Exception ex)
            {
                IntercolonyBedDiagnostics.ReportPatchFailure(
                    "ThinkNode_JobGiver.TryIssueJobPackage postfix", ex);
            }
        }
    }

    // Hospitality's JobGiver_Sleep overrides the base method and directly constructs LayDown
    // jobs. Resolve that optional third-party type at runtime so the diagnostic stays buildable
    // without a Hospitality reference while still keeping its constructor trace employee-only.
    // Prepare() below only skips this optional diagnostic patch when Hospitality is absent; it
    // is not a prefix decision and never changes a game method's control flow.
    [HarmonyPatch]
    internal static class IntercolonyBedDiagnostics_HospitalitySleepPatch
    {
        public static bool Prepare()
        {
            try
            {
                if (AccessTools.TypeByName("Hospitality.JobGiver_Sleep") == null)
                {
                    return false;
                }

                return IntercolonyBedDiagnostics.PrepareTarget(
                    "Hospitality.JobGiver_Sleep.TryIssueJobPackage(Pawn, JobIssueParams)",
                    IntercolonyBedDiagnostics.ResolveHospitalitySleepTarget);
            }
            catch (Exception ex)
            {
                IntercolonyBedDiagnostics.ReportRegistrationFailure(
                    "Hospitality.JobGiver_Sleep.TryIssueJobPackage(Pawn, JobIssueParams)", ex);
                return false;
            }
        }

        public static MethodBase TargetMethod()
        {
            try
            {
                return IntercolonyBedDiagnostics.ResolveHospitalitySleepTarget();
            }
            catch (Exception ex)
            {
                IntercolonyBedDiagnostics.ReportRegistrationFailure(
                    "Hospitality.JobGiver_Sleep.TryIssueJobPackage(Pawn, JobIssueParams)", ex);
                return null;
            }
        }

        public static void Prefix(
            Pawn pawn, out IntercolonyBedDiagnostics.EmployeeJobCreationScope __state)
        {
            __state = null;
            try
            {
                __state = IntercolonyBedDiagnostics.BeginEmployeeJobCreation(pawn);
            }
            catch (Exception ex)
            {
                IntercolonyBedDiagnostics.ReportPatchFailure(
                    "Hospitality JobGiver_Sleep prefix", ex);
            }
        }

        public static void Postfix(IntercolonyBedDiagnostics.EmployeeJobCreationScope __state)
        {
            try
            {
                IntercolonyBedDiagnostics.EndEmployeeJobCreation(__state);
            }
            catch (Exception ex)
            {
                IntercolonyBedDiagnostics.ReportPatchFailure(
                    "Hospitality JobGiver_Sleep postfix", ex);
            }
        }
    }

    [HarmonyPatch]
    internal static class IntercolonyBedDiagnostics_JobConstructorPatch
    {
        public static bool Prepare()
        {
            return IntercolonyBedDiagnostics.PrepareTargetSet(
                "Job constructors", IntercolonyBedDiagnostics.ResolveJobConstructorTargets, 7);
        }

        internal static IEnumerable<MethodBase> TargetMethods()
        {
            return IntercolonyBedDiagnostics.ResolveJobConstructorTargets();
        }

        public static void Postfix(Job __instance)
        {
            try
            {
                IntercolonyBedDiagnostics.CaptureJobCreation(__instance, "Job..ctor");
            }
            catch (Exception ex)
            {
                // The diagnostic must never affect a pooled or newly constructed vanilla job.
                IntercolonyBedDiagnostics.ReportPatchFailure("Job constructor postfix", ex);
            }
        }
    }

    [HarmonyPatch]
    internal static class IntercolonyBedDiagnostics_JobMakerPatch
    {
        public static bool Prepare()
        {
            return IntercolonyBedDiagnostics.PrepareTargetSet(
                "JobMaker.MakeJob overloads",
                IntercolonyBedDiagnostics.ResolveJobMakerTargets, 6);
        }

        internal static IEnumerable<MethodBase> TargetMethods()
        {
            // The parameterless pool factory returns before a JobDef exists. A later field
            // assignment has no passive setter seam; employee Start/Enqueue lifecycle capture
            // is the deliberate fallback for that uncommon shape.
            return IntercolonyBedDiagnostics.ResolveJobMakerTargets();
        }

        public static void Postfix(Job __result)
        {
            try
            {
                IntercolonyBedDiagnostics.CaptureJobCreation(__result, "JobMaker.MakeJob");
            }
            catch (Exception ex)
            {
                IntercolonyBedDiagnostics.ReportPatchFailure("JobMaker postfix", ex);
            }
        }
    }

    [HarmonyPatch(
        typeof(JobMaker), nameof(JobMaker.ReturnToPool), new[] { typeof(Job) })]
    internal static class IntercolonyBedDiagnostics_JobPoolPatch
    {
        public static bool Prepare()
        {
            return IntercolonyBedDiagnostics.PrepareTarget(
                "JobMaker.ReturnToPool(Job)", IntercolonyBedDiagnostics.ResolveReturnToPoolTarget);
        }

        public static void Prefix(Job job)
        {
            try
            {
                IntercolonyBedDiagnostics.ForgetJobCreation(job);
            }
            catch (Exception ex)
            {
                IntercolonyBedDiagnostics.ReportPatchFailure("JobMaker.ReturnToPool prefix", ex);
            }
        }
    }

    [HarmonyPatch(
        typeof(Job), nameof(Job.CanBeginNow), new[] { typeof(Pawn), typeof(bool) })]
    internal static class IntercolonyBedDiagnostics_JobEvaluationPatch
    {
        public static bool Prepare()
        {
            return IntercolonyBedDiagnostics.PrepareTarget(
                "Job.CanBeginNow(Pawn, bool)", IntercolonyBedDiagnostics.ResolveJobEvaluationTarget);
        }

        public static void Prefix(
            Job __instance, Pawn pawn, bool whileLyingDown,
            out IntercolonyBedDiagnostics.EvaluationScope __state)
        {
            __state = null;
            try
            {
                __state = IntercolonyBedDiagnostics.BeginLayDownEvaluation(
                    __instance, pawn, whileLyingDown);
            }
            catch (Exception ex)
            {
                IntercolonyBedDiagnostics.ReportPatchFailure("Job.CanBeginNow prefix", ex);
            }
        }

        public static void Postfix(IntercolonyBedDiagnostics.EvaluationScope __state)
        {
            try
            {
                IntercolonyBedDiagnostics.EndLayDownEvaluation(__state);
            }
            catch (Exception ex)
            {
                IntercolonyBedDiagnostics.ReportPatchFailure("Job.CanBeginNow postfix", ex);
            }
        }
    }

    [HarmonyPatch(
        typeof(RestUtility), nameof(RestUtility.GetBedSleepingSlotPosFor),
        new[] { typeof(Pawn), typeof(Building_Bed) })]
    internal static class IntercolonyBedDiagnostics_SleepingSlotPatch
    {
        public static bool Prepare()
        {
            return IntercolonyBedDiagnostics.PrepareTarget(
                "RestUtility.GetBedSleepingSlotPosFor(Pawn, Building_Bed)",
                IntercolonyBedDiagnostics.ResolveSleepingSlotTarget);
        }

        public static void Prefix(Pawn pawn, Building_Bed bed)
        {
            try
            {
                IntercolonyBedDiagnostics.ObserveSleepingSlotLookup(pawn, bed);
            }
            catch (Exception ex)
            {
                IntercolonyBedDiagnostics.ReportPatchFailure("GetBedSleepingSlotPosFor prefix", ex);
            }
        }
    }

    [HarmonyPatch(
        typeof(Pawn_JobTracker), MethodType.Constructor, new[] { typeof(Pawn) })]
    internal static class IntercolonyBedDiagnostics_TrackerConstructorPatch
    {
        public static bool Prepare()
        {
            return IntercolonyBedDiagnostics.PrepareTarget(
                "Pawn_JobTracker..ctor(Pawn)",
                IntercolonyBedDiagnostics.ResolveTrackerConstructorTarget);
        }

        public static void Postfix(Pawn_JobTracker __instance, Pawn newPawn)
        {
            try
            {
                IntercolonyBedDiagnostics.ObserveTrackerCreated(__instance, newPawn);
            }
            catch (Exception ex)
            {
                IntercolonyBedDiagnostics.ReportPatchFailure("Pawn_JobTracker constructor postfix", ex);
            }
        }
    }

    [HarmonyPatch(
        typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.ExposeData), new Type[] { })]
    internal static class IntercolonyBedDiagnostics_TrackerExposeDataPatch
    {
        public static bool Prepare()
        {
            return IntercolonyBedDiagnostics.PrepareTarget(
                "Pawn_JobTracker.ExposeData()",
                IntercolonyBedDiagnostics.ResolveTrackerExposeDataTarget);
        }

        public static void Postfix(Pawn_JobTracker __instance, Pawn ___pawn)
        {
            try
            {
                IntercolonyBedDiagnostics.ObserveTrackerCreated(__instance, ___pawn);
            }
            catch (Exception ex)
            {
                IntercolonyBedDiagnostics.ReportPatchFailure(
                    "Pawn_JobTracker.ExposeData postfix", ex);
            }
        }
    }

    [HarmonyPatch(
        typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob),
        new Type[]
        {
            typeof(Job), typeof(JobCondition), typeof(ThinkNode), typeof(bool), typeof(bool),
            typeof(ThinkTreeDef), typeof(Nullable<JobTag>), typeof(bool), typeof(bool),
            typeof(Nullable<bool>), typeof(bool), typeof(bool), typeof(bool)
        })]
    internal static class IntercolonyBedDiagnostics_StartJobPatch
    {
        public static bool Prepare()
        {
            return IntercolonyBedDiagnostics.PrepareTarget(
                "Pawn_JobTracker.StartJob(Job, JobCondition, ThinkNode, bool, bool, " +
                "ThinkTreeDef, JobTag?, bool, bool, bool?, bool, bool, bool)",
                IntercolonyBedDiagnostics.ResolveTrackerStartJobTarget);
        }

        [HarmonyPriority(Priority.First)]
        public static void Prefix(
            Pawn_JobTracker __instance, Pawn ___pawn, Job newJob, bool fromQueue,
            JobTag? tag, bool continueSleeping,
            out IntercolonyBedDiagnostics.TrackerScope __state)
        {
            __state = null;
            try
            {
                __state = IntercolonyBedDiagnostics.BeginTrackerContext(___pawn);
                IntercolonyBedDiagnostics.ObserveStart(
                    __instance, ___pawn, newJob, fromQueue, tag, continueSleeping);
            }
            catch (Exception ex)
            {
                IntercolonyBedDiagnostics.ReportPatchFailure("Pawn_JobTracker.StartJob prefix", ex);
            }
        }

        public static void Postfix(IntercolonyBedDiagnostics.TrackerScope __state)
        {
            try
            {
                IntercolonyBedDiagnostics.EndTrackerContext(__state);
            }
            catch (Exception ex)
            {
                IntercolonyBedDiagnostics.ReportPatchFailure(
                    "Pawn_JobTracker.StartJob postfix", ex);
            }
        }
    }

    [HarmonyPatch(
        typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob),
        new[] { typeof(JobCondition), typeof(bool), typeof(bool) })]
    internal static class IntercolonyBedDiagnostics_EndCurrentJobPatch
    {
        public static bool Prepare()
        {
            return IntercolonyBedDiagnostics.PrepareTarget(
                "Pawn_JobTracker.EndCurrentJob(JobCondition, bool, bool)",
                IntercolonyBedDiagnostics.ResolveTrackerEndCurrentJobTarget);
        }

        [HarmonyPriority(Priority.First)]
        public static void Prefix(
            Pawn_JobTracker __instance, Pawn ___pawn, JobCondition condition,
            out IntercolonyBedDiagnostics.TrackerScope __state)
        {
            __state = null;
            try
            {
                __state = IntercolonyBedDiagnostics.BeginTrackerContext(___pawn);
                IntercolonyBedDiagnostics.ObserveEnd(__instance, ___pawn, condition);
            }
            catch (Exception ex)
            {
                IntercolonyBedDiagnostics.ReportPatchFailure("Pawn_JobTracker.EndCurrentJob prefix", ex);
            }
        }

        public static void Postfix(IntercolonyBedDiagnostics.TrackerScope __state)
        {
            try
            {
                IntercolonyBedDiagnostics.EndTrackerContext(__state);
            }
            catch (Exception ex)
            {
                IntercolonyBedDiagnostics.ReportPatchFailure(
                    "Pawn_JobTracker.EndCurrentJob postfix", ex);
            }
        }
    }

    [HarmonyPatch(
        typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob),
        new[] { typeof(Job), typeof(Nullable<JobTag>), typeof(bool) })]
    internal static class IntercolonyBedDiagnostics_TryTakeOrderedJobPatch
    {
        public static bool Prepare()
        {
            return IntercolonyBedDiagnostics.PrepareTarget(
                "Pawn_JobTracker.TryTakeOrderedJob(Job, JobTag?, bool)",
                IntercolonyBedDiagnostics.ResolveTrackerTryTakeOrderedJobTarget);
        }

        public static void Prefix(
            Pawn ___pawn, out IntercolonyBedDiagnostics.TrackerScope __state)
        {
            __state = null;
            try
            {
                __state = IntercolonyBedDiagnostics.BeginTrackerContext(___pawn);
            }
            catch (Exception ex)
            {
                IntercolonyBedDiagnostics.ReportPatchFailure(
                    "Pawn_JobTracker.TryTakeOrderedJob prefix", ex);
            }
        }

        public static void Postfix(IntercolonyBedDiagnostics.TrackerScope __state)
        {
            try
            {
                IntercolonyBedDiagnostics.EndTrackerContext(__state);
            }
            catch (Exception ex)
            {
                IntercolonyBedDiagnostics.ReportPatchFailure(
                    "Pawn_JobTracker.TryTakeOrderedJob postfix", ex);
            }
        }
    }

    [HarmonyPatch(
        typeof(JobQueue), nameof(JobQueue.EnqueueFirst),
        new[] { typeof(Job), typeof(Nullable<JobTag>) })]
    internal static class IntercolonyBedDiagnostics_EnqueueFirstPatch
    {
        public static bool Prepare()
        {
            return IntercolonyBedDiagnostics.PrepareTarget(
                "JobQueue.EnqueueFirst(Job, JobTag?)",
                IntercolonyBedDiagnostics.ResolveEnqueueFirstTarget);
        }

        public static void Postfix(JobQueue __instance, Job j, JobTag? tag)
        {
            try
            {
                IntercolonyBedDiagnostics.ObserveEnqueue(__instance, j, tag, "ENQUEUE_FIRST");
            }
            catch (Exception ex)
            {
                IntercolonyBedDiagnostics.ReportPatchFailure("JobQueue.EnqueueFirst postfix", ex);
            }
        }
    }

    [HarmonyPatch(
        typeof(JobQueue), nameof(JobQueue.EnqueueLast),
        new[] { typeof(Job), typeof(Nullable<JobTag>) })]
    internal static class IntercolonyBedDiagnostics_EnqueueLastPatch
    {
        public static bool Prepare()
        {
            return IntercolonyBedDiagnostics.PrepareTarget(
                "JobQueue.EnqueueLast(Job, JobTag?)",
                IntercolonyBedDiagnostics.ResolveEnqueueLastTarget);
        }

        public static void Postfix(JobQueue __instance, Job j, JobTag? tag)
        {
            try
            {
                IntercolonyBedDiagnostics.ObserveEnqueue(__instance, j, tag, "ENQUEUE_LAST");
            }
            catch (Exception ex)
            {
                IntercolonyBedDiagnostics.ReportPatchFailure("JobQueue.EnqueueLast postfix", ex);
            }
        }
    }

    [HarmonyPatch(
        typeof(WorkGiver_RescueDowned), nameof(WorkGiver_RescueDowned.HasJobOnThing),
        new[] { typeof(Pawn), typeof(Thing), typeof(bool) })]
    internal static class IntercolonyBedDiagnostics_RescueDecisionPatch
    {
        public static bool Prepare()
        {
            return IntercolonyBedDiagnostics.PrepareTarget(
                "WorkGiver_RescueDowned.HasJobOnThing(Pawn, Thing, bool)",
                IntercolonyBedDiagnostics.ResolveRescueDecisionTarget);
        }

        [HarmonyPriority(Priority.First)]
        public static void Prefix(
            Pawn pawn, Thing t, out IntercolonyBedDiagnostics.RescueLookupScope __state)
        {
            __state = null;
            try
            {
                __state = IntercolonyBedDiagnostics.BeginRescueLookup(
                    pawn, t as Pawn, "RESCUE_DECISION");
            }
            catch (Exception ex)
            {
                IntercolonyBedDiagnostics.ReportPatchFailure("Rescue HasJobOnThing prefix", ex);
            }
        }

        public static void Postfix(IntercolonyBedDiagnostics.RescueLookupScope __state)
        {
            try
            {
                IntercolonyBedDiagnostics.EndRescueLookup(__state);
            }
            catch (Exception ex)
            {
                IntercolonyBedDiagnostics.ReportPatchFailure("Rescue HasJobOnThing postfix", ex);
            }
        }
    }

    [HarmonyPatch(
        typeof(WorkGiver_RescueDowned), nameof(WorkGiver_RescueDowned.JobOnThing),
        new[] { typeof(Pawn), typeof(Thing), typeof(bool) })]
    internal static class IntercolonyBedDiagnostics_RescueJobPatch
    {
        public static bool Prepare()
        {
            return IntercolonyBedDiagnostics.PrepareTarget(
                "WorkGiver_RescueDowned.JobOnThing(Pawn, Thing, bool)",
                IntercolonyBedDiagnostics.ResolveRescueJobTarget);
        }

        [HarmonyPriority(Priority.First)]
        public static void Prefix(
            Pawn pawn, Thing t, out IntercolonyBedDiagnostics.RescueLookupScope __state)
        {
            __state = null;
            try
            {
                __state = IntercolonyBedDiagnostics.BeginRescueLookup(
                    pawn, t as Pawn, "RESCUE_JOB_BUILD");
            }
            catch (Exception ex)
            {
                IntercolonyBedDiagnostics.ReportPatchFailure("Rescue JobOnThing prefix", ex);
            }
        }

        public static void Postfix(
            Pawn pawn, Thing t, bool forced, Job __result,
            IntercolonyBedDiagnostics.RescueLookupScope __state)
        {
            try
            {
                IntercolonyBedDiagnostics.ObserveRescueJob(pawn, t, forced, __result);
            }
            catch (Exception ex)
            {
                IntercolonyBedDiagnostics.ReportPatchFailure("Rescue JobOnThing postfix", ex);
            }
            finally
            {
                try
                {
                    IntercolonyBedDiagnostics.EndRescueLookup(__state);
                }
                catch (Exception ex)
                {
                    IntercolonyBedDiagnostics.ReportPatchFailure(
                        "Rescue JobOnThing lookup cleanup", ex);
                }
            }
        }
    }

    // Observe the shared concrete lookup instead of WorkGiver_TakeToBed.FindBed: Hospitality's
    // RescueDowned prefix calls this RestUtility overload directly and can bypass that helper.
    [HarmonyPatch(
        typeof(RestUtility), nameof(RestUtility.FindBedFor),
        new[] { typeof(Pawn), typeof(Pawn), typeof(bool), typeof(bool), typeof(Nullable<GuestStatus>) })]
    internal static class IntercolonyBedDiagnostics_FindBedForPatch
    {
        public static bool Prepare()
        {
            return IntercolonyBedDiagnostics.PrepareTarget(
                "RestUtility.FindBedFor(Pawn, Pawn, bool, bool, GuestStatus?)",
                IntercolonyBedDiagnostics.ResolveFindBedForTarget);
        }

        public static void Postfix(Pawn sleeper, Pawn traveler, Building_Bed __result)
        {
            try
            {
                IntercolonyBedDiagnostics.ObserveFindBedResult(traveler, sleeper, __result);
            }
            catch (Exception ex)
            {
                IntercolonyBedDiagnostics.ReportPatchFailure(
                    "RestUtility.FindBedFor postfix", ex);
            }
        }
    }

    [HarmonyPatch(
        typeof(RestUtility), nameof(RestUtility.TuckIntoBed),
        new[] { typeof(Building_Bed), typeof(Pawn), typeof(Pawn), typeof(bool) })]
    internal static class IntercolonyBedDiagnostics_TuckPatch
    {
        public static bool Prepare()
        {
            return IntercolonyBedDiagnostics.PrepareTarget(
                "RestUtility.TuckIntoBed(Building_Bed, Pawn, Pawn, bool)",
                IntercolonyBedDiagnostics.ResolveTuckTarget);
        }

        public static void Prefix(
            Building_Bed bed, Pawn taker, Pawn takee, bool rescued,
            out IntercolonyBedDiagnostics.TuckScope __state)
        {
            __state = null;
            try
            {
                __state = IntercolonyBedDiagnostics.BeginTuck(bed, taker, takee, rescued);
            }
            catch (Exception ex)
            {
                IntercolonyBedDiagnostics.ReportPatchFailure("RestUtility.TuckIntoBed prefix", ex);
            }
        }

        public static void Postfix(IntercolonyBedDiagnostics.TuckScope __state)
        {
            try
            {
                IntercolonyBedDiagnostics.EndTuck(__state);
            }
            catch (Exception ex)
            {
                IntercolonyBedDiagnostics.ReportPatchFailure("RestUtility.TuckIntoBed postfix", ex);
            }
        }
    }

    // HarmonyPatches logs this message immediately after PatchAll() returns. This postfix is
    // therefore the first point in this file that is guaranteed to run after the shared
    // registration pass has completed.
    [HarmonyPatch(
        typeof(IntercolonyLog), nameof(IntercolonyLog.Verbose), new[] { typeof(string) })]
    internal static class IntercolonyBedDiagnostics_StartupCheckPatch
    {
        public static bool Prepare()
        {
            return IntercolonyBedDiagnostics.PrepareTarget(
                "IntercolonyLog.Verbose(string)",
                IntercolonyBedDiagnostics.ResolveStartupCheckTarget);
        }

        public static void Postfix()
        {
            try
            {
                IntercolonyBedDiagnostics.VerifyStartupPatches();
            }
            catch (Exception ex)
            {
                IntercolonyBedDiagnostics.ReportRegistrationFailure(
                    "startup diagnostic patch self-check", ex);
            }
        }
    }
}
