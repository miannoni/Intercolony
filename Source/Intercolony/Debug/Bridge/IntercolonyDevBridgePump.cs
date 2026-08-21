#if INTERCOLONY_DEV_BRIDGE
using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

namespace Intercolony
{
    /// <summary>
    /// Moves bridge work off the socket thread and onto Unity's main thread.
    ///
    /// **This is the load-bearing piece of the whole bridge.** Verse is not thread-safe and does
    /// not pretend to be: Find.*, the def database, world state, maps and pawns all assume the main
    /// thread. Touching any of it from a socket thread does not reliably throw - it corrupts, or it
    /// works a hundred times and deadlocks on the hundred and first. So the listener thread is
    /// allowed to do exactly one thing with a decoded command: put it in this queue and wait.
    ///
    /// **Update() rather than a tick.** A tick callback stops when the game is paused, and being
    /// able to ask a paused game what state it is in is most of the point of the `status` command -
    /// "no world loaded" is an answer, not a failure to answer. Update() runs regardless.
    ///
    /// Deliberately not a Harmony patch. A dev-only MonoBehaviour that only exists in a build
    /// nobody ships is a far smaller compatibility liability than patching a hot vanilla method
    /// (DESIGN.md §63).
    /// </summary>
    public sealed class IntercolonyDevBridgePump : MonoBehaviour
    {
        /// <summary>
        /// One queued command and the handle its socket thread is blocked on.
        /// </summary>
        private sealed class PendingCommand
        {
            public Func<object> Work;
            public object Result;
            public Exception Failure;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        }

        /// <summary>
        /// How many commands to drain per frame. Bounded so a burst of requests cannot stall
        /// rendering: a self-test suite runs for minutes on the main thread and the game is
        /// visibly frozen while it does, which is acceptable for one deliberate command and would
        /// not be for a queue that never gives the frame back.
        /// </summary>
        private const int MaxCommandsPerFrame = 4;

        private static readonly ConcurrentQueue<PendingCommand> Queue =
            new ConcurrentQueue<PendingCommand>();

        private static IntercolonyDevBridgePump instance;

        /// <summary>
        /// Creates the pump if it does not exist. Must be called from the main thread; the
        /// GameObject constructor is itself a Unity API.
        /// </summary>
        public static void EnsureExists()
        {
            if (instance != null)
            {
                return;
            }

            GameObject host = new GameObject("IntercolonyDevBridge");
            UnityEngine.Object.DontDestroyOnLoad(host);
            instance = host.AddComponent<IntercolonyDevBridgePump>();
        }

        /// <summary>
        /// Called from a socket thread. Queues <paramref name="work"/> for the main thread and
        /// blocks until it completes, then rethrows whatever it threw so the caller sees the real
        /// failure rather than a timeout.
        ///
        /// Returns false on timeout, which is a genuine possibility rather than a formality: if the
        /// game is mid-load, or a previous self-test is still running, the queue does not drain. A
        /// timeout must therefore report "the game did not answer in time" and leave the listener
        /// healthy, not tear anything down.
        /// </summary>
        public static bool Execute(Func<object> work, int timeoutMs, out object result, out Exception failure)
        {
            PendingCommand pending = new PendingCommand { Work = work };
            Queue.Enqueue(pending);

            if (!pending.Done.Wait(timeoutMs))
            {
                result = null;
                failure = null;
                return false;
            }

            result = pending.Result;
            failure = pending.Failure;
            return true;
        }

        private void Update()
        {
            for (int i = 0; i < MaxCommandsPerFrame; i++)
            {
                PendingCommand pending;
                if (!Queue.TryDequeue(out pending))
                {
                    return;
                }

                try
                {
                    pending.Result = pending.Work();
                }
                catch (Exception ex)
                {
                    // Carried back to the socket thread rather than logged and swallowed: the
                    // client asked a question and an exception is the honest answer to it. It is
                    // also logged, because a bridge command that throws is worth seeing in
                    // Player.log even when the client handled it.
                    pending.Failure = ex;
                    IntercolonyLog.Error($"dev bridge command threw: {ex}");
                }
                finally
                {
                    // In a finally so a failure to signal cannot strand the socket thread for the
                    // whole command timeout.
                    pending.Done.Set();
                }
            }
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }

            // Release anything still waiting. Without this a socket thread blocked on a command
            // queued just before shutdown waits out its full timeout while the process tries to
            // exit.
            PendingCommand pending;
            while (Queue.TryDequeue(out pending))
            {
                pending.Failure = new InvalidOperationException("RimWorld is shutting down.");
                pending.Done.Set();
            }
        }
    }
}
#endif
