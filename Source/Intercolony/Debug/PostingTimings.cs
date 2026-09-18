using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using LudeonTK;

namespace Intercolony
{
    /// <summary>
    /// Temporary, opt-in timing for the synchronous Emergency posting path. Delete this helper and
    /// its scope markers when the P0 baseline is complete.
    /// </summary>
    public static class PostingTimings
    {
        private const int PhaseCount = 5;
        private static Session currentSession;

        /// <summary>Default-off switch for the temporary Emergency posting measurement.</summary>
        public static bool Enabled;

        [DebugAction("Intercolony", "Toggle emergency posting timings",
            allowedGameStates = AllowedGameStates.Playing, displayPriority = 44)]
        private static void Toggle()
        {
            Enabled = !Enabled;
            IntercolonyLog.Message(Enabled
                ? "Emergency posting timings enabled."
                : "Emergency posting timings disabled.");
        }

        /// <summary>
        /// Starts one timing session only for an Emergency posting. Returning null is intentional:
        /// the disabled path creates neither a stopwatch nor any timing state.
        /// </summary>
        public static IDisposable BeginTryPost(bool emergencyDispatch)
        {
            if (!Enabled || !emergencyDispatch)
            {
                return null;
            }

            return new Session();
        }

        /// <summary>
        /// Starts an exclusive sub-phase. A nested phase pauses its parent, so the displayed phase
        /// values are readable parts of the TryPost total rather than inclusive double-counts.
        /// </summary>
        internal static IDisposable Phase(PostingTimingPhase phase)
        {
            Session session = currentSession;
            return session == null ? null : session.BeginPhase(phase);
        }

        private static int IndexOf(PostingTimingPhase phase)
        {
            switch (phase)
            {
                case PostingTimingPhase.LightweightCensusFiltering:
                    return 0;
                case PostingTimingPhase.CandidateApplicationSelection:
                    return 1;
                case PostingTimingPhase.PawnMaterialisation:
                    return 2;
                case PostingTimingPhase.EquipmentFulfilment:
                    return 3;
                case PostingTimingPhase.FinalApplicantPublication:
                    return 4;
                default:
                    return -1;
            }
        }

        private static string NameOf(PostingTimingPhase phase)
        {
            switch (phase)
            {
                case PostingTimingPhase.LightweightCensusFiltering:
                    return "lightweight census/filtering";
                case PostingTimingPhase.CandidateApplicationSelection:
                    return "candidate/application selection";
                case PostingTimingPhase.PawnMaterialisation:
                    return "Pawn materialisation";
                case PostingTimingPhase.EquipmentFulfilment:
                    return "equipment fulfilment";
                case PostingTimingPhase.FinalApplicantPublication:
                    return "final applicant publication / UI return";
                default:
                    return "unknown phase";
            }
        }

        private static void AppendMilliseconds(
            StringBuilder summary, string label, double milliseconds)
        {
            summary.Append("  ")
                .Append(label)
                .Append(": ")
                .Append(milliseconds.ToString("F3", CultureInfo.InvariantCulture))
                .Append(" ms");
        }

        private sealed class Session : IDisposable
        {
            private readonly Session previousSession;
            private readonly Stopwatch total = Stopwatch.StartNew();
            private readonly long[] phaseTicks = new long[PhaseCount];
            private readonly int[] phaseCalls = new int[PhaseCount];
            private PhaseScope activePhase;
            private bool disposed;

            internal Session()
            {
                previousSession = currentSession;
                currentSession = this;
            }

            internal IDisposable BeginPhase(PostingTimingPhase phase)
            {
                int index = IndexOf(phase);
                if (index < 0)
                {
                    return null;
                }

                long now = Stopwatch.GetTimestamp();
                activePhase?.Pause(now);
                PhaseScope scope = new PhaseScope(this, index, activePhase, now);
                activePhase = scope;
                return scope;
            }

            internal void AddTicks(int index, long ticks)
            {
                phaseTicks[index] += ticks;
                phaseCalls[index]++;
            }

            internal void SetActiveAfterDispose(
                PhaseScope disposedPhase, PhaseScope parent, long now)
            {
                if (!ReferenceEquals(activePhase, disposedPhase))
                {
                    return;
                }

                activePhase = parent;
                parent?.Resume(now);
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                while (activePhase != null)
                {
                    activePhase.Dispose();
                }

                total.Stop();
                if (ReferenceEquals(currentSession, this))
                {
                    currentSession = previousSession;
                }

                StringBuilder summary = new StringBuilder(320);
                summary.AppendLine("Emergency job posting timings");
                AppendMilliseconds(summary, "TryPost total", total.Elapsed.TotalMilliseconds);
                summary.AppendLine();

                AppendPhase(summary, PostingTimingPhase.LightweightCensusFiltering);
                AppendPhase(summary, PostingTimingPhase.CandidateApplicationSelection);
                AppendPhase(summary, PostingTimingPhase.PawnMaterialisation, includeCallStats: true);
                AppendPhase(summary, PostingTimingPhase.EquipmentFulfilment, includeCallStats: true);
                AppendPhase(summary, PostingTimingPhase.FinalApplicantPublication);

                IntercolonyLog.Message(summary.ToString());
            }

            private void AppendPhase(
                StringBuilder summary, PostingTimingPhase phase, bool includeCallStats = false)
            {
                int index = IndexOf(phase);
                double milliseconds = phaseTicks[index] * 1000d / Stopwatch.Frequency;
                AppendMilliseconds(summary, NameOf(phase), milliseconds);
                if (includeCallStats)
                {
                    int calls = phaseCalls[index];
                    summary.Append(" (calls=").Append(calls).Append(", mean=");
                    if (calls == 0)
                    {
                        summary.Append("n/a");
                    }
                    else
                    {
                        summary.Append(
                            (milliseconds / calls).ToString("F3", CultureInfo.InvariantCulture));
                        summary.Append(" ms/call");
                    }

                    summary.Append(")");
                }

                summary.AppendLine();
            }
        }

        private sealed class PhaseScope : IDisposable
        {
            private readonly Session session;
            private readonly int index;
            private readonly PhaseScope parent;
            private long segmentStarted;
            private long accumulatedTicks;
            private bool running = true;
            private bool disposed;

            internal PhaseScope(Session session, int index, PhaseScope parent, long now)
            {
                this.session = session;
                this.index = index;
                this.parent = parent;
                segmentStarted = now;
            }

            internal void Pause(long now)
            {
                if (!running)
                {
                    return;
                }

                accumulatedTicks += now - segmentStarted;
                running = false;
            }

            internal void Resume(long now)
            {
                if (disposed || running)
                {
                    return;
                }

                segmentStarted = now;
                running = true;
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                long now = Stopwatch.GetTimestamp();
                if (running)
                {
                    accumulatedTicks += now - segmentStarted;
                }

                session.AddTicks(index, accumulatedTicks);
                session.SetActiveAfterDispose(this, parent, now);
            }
        }
    }

    internal enum PostingTimingPhase
    {
        LightweightCensusFiltering,
        CandidateApplicationSelection,
        PawnMaterialisation,
        EquipmentFulfilment,
        FinalApplicantPublication
    }
}
