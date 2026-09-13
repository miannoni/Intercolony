#if INTERCOLONY_DEV_BRIDGE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using RimWorld;
using Verse;

namespace Intercolony
{
    /// <summary>
    /// A loopback-only request/response socket that lets a development tool ask the running game
    /// questions and run its self-tests, without a person opening the debug menu.
    ///
    /// **Two gates, and both are needed.** The compile gate (INTERCOLONY_DEV_BRIDGE, set only by
    /// -p:EnableDevBridge=true) means a released build does not contain this file at all. The
    /// runtime gate below means even a bridge build stays silent unless asked. The first alone
    /// would rely on nobody ever packaging a development build; package.ps1 checks the artefact for
    /// exactly that reason.
    ///
    /// **Loopback only, and never configurable to anything else.** The listener binds
    /// IPAddress.Loopback, which is 127.0.0.1. There is deliberately no setting for the address:
    /// this executes self-tests inside the player's game, and the difference between that being
    /// reachable from the machine and reachable from the network is the difference between a
    /// development tool and a remote code execution hole. The port moves; the address does not.
    ///
    /// Command surface is narrow on purpose - named verbs only. No eval, no reflection, no
    /// "run the debug action called X". It grows by adding a verb with a contract, never by adding
    /// an interpreter.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class IntercolonyDevBridgeHost
    {
        private const string EnabledVariable = "INTERCOLONY_DEV_BRIDGE";
        private const string PortVariable = "INTERCOLONY_DEV_BRIDGE_PORT";
        private const int DefaultPort = 34117;

        /// <summary>
        /// A request larger than this is refused rather than buffered. Nothing the bridge accepts
        /// is remotely this big; the limit exists so a confused or malicious client cannot make the
        /// game allocate without bound.
        /// </summary>
        private const int MaxRequestBytes = 64 * 1024;

        /// <summary>How long to wait for a client to finish sending its one line.</summary>
        private const int SocketTimeoutMs = 10_000;

        /// <summary>
        /// How long a command may occupy the main thread before the socket thread gives up on it.
        ///
        /// Generous because the honest number is large: the full self-test suite runs seventeen
        /// suites synchronously on the main thread and takes minutes. A timeout shorter than the
        /// work would turn every successful suite run into a reported failure.
        /// </summary>
        private const int CommandTimeoutMs = 600_000;

        private static TcpListener listener;
        private static Thread acceptThread;
        private static volatile bool stopping;

        static IntercolonyDevBridgeHost()
        {
            // Runtime gate. Absent or anything other than "1" means this build behaves exactly like
            // a normal one.
            string enabled = Environment.GetEnvironmentVariable(EnabledVariable);
            if (enabled != "1")
            {
                return;
            }

            try
            {
                // The pump must be created here, on the main thread: creating a GameObject is
                // itself a Unity call. [StaticConstructorOnStartup] guarantees that.
                IntercolonyDevBridgePump.EnsureExists();
                Start(ResolvePort());
            }
            catch (Exception ex)
            {
                // A development tool must never be the reason the game will not start.
                IntercolonyLog.Error($"dev bridge failed to start: {ex}");
            }
        }

        private static int ResolvePort()
        {
            string configured = Environment.GetEnvironmentVariable(PortVariable);
            int port;
            if (!string.IsNullOrEmpty(configured) &&
                int.TryParse(configured, NumberStyles.Integer, CultureInfo.InvariantCulture, out port) &&
                port > 0 && port <= 65535)
            {
                return port;
            }

            if (!string.IsNullOrEmpty(configured))
            {
                IntercolonyLog.Warning(
                    $"dev bridge: {PortVariable}='{configured}' is not a valid port; using {DefaultPort}.");
            }

            return DefaultPort;
        }

        private static void Start(int port)
        {
            try
            {
                // IPAddress.Loopback, never IPAddress.Any. See the type comment.
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                IntercolonyLog.Error(
                    $"dev bridge: port {port} is already in use - another RimWorld with the bridge " +
                    $"enabled is probably still running. Set {PortVariable} to use a different port.");
                listener = null;
                return;
            }

            acceptThread = new Thread(AcceptLoop)
            {
                Name = "Intercolony dev bridge",
                IsBackground = true
            };
            acceptThread.Start();

            AppDomain.CurrentDomain.ProcessExit += (sender, args) => Stop();

            IntercolonyLog.Message($"dev bridge listening on 127.0.0.1:{port}.");
        }

        internal static void Stop()
        {
            stopping = true;
            try
            {
                listener?.Stop();
            }
            catch (Exception)
            {
                // Best effort: the process is going away regardless.
            }
        }

        /// <summary>
        /// One connection carries one request and one response, then closes. Simpler to reason
        /// about than a persistent session, and it means a client that dies mid-command cannot
        /// leave the bridge in a state anyone has to think about.
        /// </summary>
        private static void AcceptLoop()
        {
            while (!stopping)
            {
                TcpClient client = null;
                try
                {
                    client = listener.AcceptTcpClient();
                    HandleConnection(client);
                }
                catch (Exception)
                {
                    if (stopping)
                    {
                        return;
                    }

                    // One bad connection must never take the listener down with it - the next
                    // command has to work. Do not log here: even Verse.Log is outside the socket
                    // thread's deliberately tiny accept/read/queue/wait/write boundary.
                }
                finally
                {
                    try
                    {
                        client?.Close();
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        private static void HandleConnection(TcpClient client)
        {
            client.ReceiveTimeout = SocketTimeoutMs;
            client.SendTimeout = SocketTimeoutMs;

            NetworkStream stream = client.GetStream();
            string requestLine;
            if (!TryReadLine(stream, out requestLine))
            {
                WriteResponse(stream, IntercolonyDevBridgeProtocol.WriteResponse(
                    null, false, $"request exceeded {MaxRequestBytes} bytes", null));
                return;
            }

            string id = null;
            try
            {
                Dictionary<string, object> request =
                    IntercolonyDevBridgeProtocol.Parse(requestLine) as Dictionary<string, object>;
                if (request == null)
                {
                    throw new IntercolonyDevBridgeProtocol.JsonException(
                        "request must be a JSON object");
                }

                id = IntercolonyDevBridgeProtocol.GetString(request, "id");
                string command = IntercolonyDevBridgeProtocol.GetString(request, "command");
                Dictionary<string, object> args =
                    IntercolonyDevBridgeProtocol.GetObject(request, "args");

                if (string.IsNullOrEmpty(command))
                {
                    WriteResponse(stream, IntercolonyDevBridgeProtocol.WriteResponse(
                        id, false, "no command given", null));
                    return;
                }

                object result;
                Exception failure;
                // Everything below this line runs on the main thread. Nothing above it has touched
                // Verse at all.
                if (!IntercolonyDevBridgePump.Execute(
                        () => Dispatch(command, args), CommandTimeoutMs, out result, out failure))
                {
                    WriteResponse(stream, IntercolonyDevBridgeProtocol.WriteResponse(
                        id, false,
                        $"the game did not run the command within {CommandTimeoutMs / 1000}s - " +
                        "it may be loading, or busy with an earlier command",
                        null));
                    return;
                }

                if (failure != null)
                {
                    WriteResponse(stream, IntercolonyDevBridgeProtocol.WriteResponse(
                        id, false, $"{failure.GetType().Name}: {failure.Message}", null));
                    return;
                }

                WriteResponse(stream, IntercolonyDevBridgeProtocol.WriteResponse(
                    id, true, null, result));
            }
            catch (IntercolonyDevBridgeProtocol.JsonException ex)
            {
                // A malformed request is an ordinary answer, not an incident. The id may not have
                // been readable, in which case it comes back null and the client matches on the
                // fact that it got a response at all.
                WriteResponse(stream, IntercolonyDevBridgeProtocol.WriteResponse(
                    id, false, $"malformed request: {ex.Message}", null));
            }
            catch (Exception ex)
            {
                WriteResponse(stream, IntercolonyDevBridgeProtocol.WriteResponse(
                    id, false, $"{ex.GetType().Name}: {ex.Message}", null));
            }
        }

        /// <summary>
        /// Reads one LF-terminated line, refusing anything over the size cap. Returns false if the
        /// cap was hit, so the caller can answer with a structured error rather than dropping the
        /// connection.
        /// </summary>
        private static bool TryReadLine(NetworkStream stream, out string line)
        {
            using (MemoryStream buffer = new MemoryStream())
            {
                byte[] one = new byte[1];
                while (true)
                {
                    int read = stream.Read(one, 0, 1);
                    if (read <= 0)
                    {
                        break;
                    }

                    if (one[0] == (byte)'\n')
                    {
                        break;
                    }

                    if (buffer.Length >= MaxRequestBytes)
                    {
                        // Drain the rest of the client's line before answering.
                        //
                        // Replying and closing here instead looks correct and is not: the client is
                        // still writing, so closing makes the OS reset the connection, and the
                        // structured "too large" error is discarded with it. The client sees a
                        // connection reset - the one outcome the size cap exists to avoid. Draining
                        // costs nothing on loopback and lets the answer actually arrive.
                        DrainLine(stream);
                        line = null;
                        return false;
                    }

                    buffer.WriteByte(one[0]);
                }

                // Tolerate CRLF from a client that used a text writer without thinking about it.
                string text = Encoding.UTF8.GetString(buffer.ToArray());
                line = text.TrimEnd('\r');
                return true;
            }
        }

        /// <summary>
        /// Reads and discards up to the end of the current line, so an over-long request can be
        /// answered rather than reset.
        ///
        /// Bounded twice - by its own ceiling and by the socket's receive timeout - so a client that
        /// never sends a newline cannot hold the accept loop open indefinitely. Exceeding either is
        /// not an error worth reporting: the caller is already about to send a "too large" response,
        /// which is the true and useful answer either way.
        /// </summary>
        private static void DrainLine(NetworkStream stream)
        {
            const int MaxDrainBytes = 4 * 1024 * 1024;

            byte[] scratch = new byte[4096];
            int drained = 0;
            try
            {
                while (drained < MaxDrainBytes)
                {
                    int read = stream.Read(scratch, 0, scratch.Length);
                    if (read <= 0)
                    {
                        return;
                    }

                    drained += read;
                    for (int i = 0; i < read; i++)
                    {
                        if (scratch[i] == (byte)'\n')
                        {
                            return;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // The client gave up mid-send. Nothing to do; the response attempt that follows
                // will fail harmlessly and the connection closes either way.
            }
        }

        private static void WriteResponse(NetworkStream stream, string json)
        {
            byte[] payload = Encoding.UTF8.GetBytes(json + "\n");
            stream.Write(payload, 0, payload.Length);
            stream.Flush();
        }

        // ----------------------------------------------------------------- commands ----

        /// <summary>
        /// Runs on the main thread. An unknown command is a normal structured error, not an
        /// exception - a client probing for a verb this build does not have should get an answer.
        /// </summary>
        private static object Dispatch(string command, Dictionary<string, object> args)
        {
            switch (command)
            {
                case "status":
                    return Status();
                case "tests.list":
                    return TestsList();
                case "tests.run":
                    return TestsRun(IntercolonyDevBridgeProtocol.GetString(args, "name"));
                case "tests.run_all":
                    return TestsRunAll();
                case "state.summary":
                    return StateSummary();
                case "world_pawns.list":
                    return WorldPawnList();
                case "world_pawns.count":
                    return WorldPawnCount();
                case "postings.count":
                    return PostingCount();
                default:
                    throw new InvalidOperationException($"unknown command '{command}'");
            }
        }

        /// <summary>
        /// The suites this build knows about, straight from the runner's registry. Not a copy —
        /// a second list here would drift the first time a suite was added, which is the failure
        /// this whole arrangement exists to avoid.
        /// </summary>
        private static object TestsList()
        {
            List<object> tests = new List<object>();
            foreach (IntercolonyAllSelfTests.SelfTestDefinition definition in
                     IntercolonyAllSelfTests.List())
            {
                tests.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["id"] = definition.Id,
                    ["label"] = definition.Label,
                    ["requiresMap"] = definition.RequiresMap
                });
            }

            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["tests"] = tests
            };
        }

        /// <summary>
        /// Runs one suite by id.
        ///
        /// A missing precondition is reported as a precondition, never as a skip and never as a
        /// pass. The distinction is the point of the command: a caller has to be able to tell
        /// "this failed", "this was not exercised", and "this could not run here" apart, and the
        /// cheapest way to lose that is to let one of them wear another's clothes.
        /// </summary>
        private static object TestsRun(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new InvalidOperationException("tests.run needs args.name");
            }

            IntercolonyAllSelfTests.SuiteResult result =
                IntercolonyAllSelfTests.RunOne(
                    name, IntercolonyWorldComponent.Current, Find.CurrentMap);

            return DescribeResult(result);
        }

        private static Dictionary<string, object> DescribeResult(
            IntercolonyAllSelfTests.SuiteResult result)
        {
            // `crashed` covers two cases the runner deliberately conflates for its table: the suite
            // threw, and the suite returned output with no summary line to verify. Both mean "this
            // did not complete", both leave the real text in output, and neither may be reported as
            // a pass - so success is false for both and the output carries the detail.
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["id"] = result.id,
                ["label"] = result.name,
                ["passed"] = result.passed,
                ["failed"] = result.failed,
                ["skipped"] = result.skipped,
                ["success"] = result.Clean,
                ["durationMs"] = result.durationMs,
                ["output"] = result.output,
                ["preconditionError"] = result.preconditionError,
                ["exceptionText"] = result.exceptionText,
                ["unknownSuite"] = result.unknownSuite
            };
        }

        /// <summary>
        /// The whole suite, through the runner's own entry point so the report a client reads and
        /// the report the debug menu prints are the same text produced by the same code.
        ///
        /// The runner returns counts and its existing report from one pass through the registry.
        /// That matters beyond speed: two passes could observe different game state and return
        /// numbers that did not describe the text beside them.
        /// </summary>
        private static object TestsRunAll()
        {
            IntercolonyWorldComponent state = IntercolonyWorldComponent.Current;
            IntercolonyAllSelfTests.AllSuitesResult suite =
                IntercolonyAllSelfTests.RunAll(state, Find.CurrentMap);
            List<object> tests = new List<object>();
            foreach (IntercolonyAllSelfTests.SuiteResult result in suite.results)
            {
                tests.Add(DescribeResult(result));
            }

            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                // Two signals, never collapsed into one. success drives the caller's exit code and
                // means "nothing failed and nothing was blocked"; clean additionally means nothing
                // was skipped, which is the runner's own verdict and the project's rule that a
                // skipped assertion is not proof. A healthy run legitimately has skips, so folding
                // them into success would make every run look like a failure.
                ["success"] = suite.success,
                ["clean"] = suite.clean,
                ["passed"] = suite.passed,
                ["failed"] = suite.failed,
                ["skipped"] = suite.skipped,
                ["notRun"] = suite.notRun,
                ["durationMs"] = suite.durationMs,
                ["tests"] = tests,
                ["output"] = suite.output,
                ["preconditionError"] = state == null
                    ? "no world loaded - load a colony first"
                    : null
            };
        }

        /// <summary>The existing authoritative dump, not a second handwritten one.</summary>
        private static object StateSummary()
        {
            IntercolonyWorldComponent state = IntercolonyWorldComponent.Current;
            if (state == null)
            {
                throw new InvalidOperationException("no world loaded - load a colony first");
            }

            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["summary"] = state.DebugStateSummary()
            };
        }

        /// <summary>
        /// Lists the same world-pawn collection as WorldPawnCount, because a changed total proves a
        /// leak but does not identify the pawn that needs investigation.
        ///
        /// Each record is isolated so one damaged pawn cannot hide the rest of the snapshot. Null
        /// entries are counted separately because they explain why the record count can be lower
        /// than the source collection count.
        /// </summary>
        private static object WorldPawnList()
        {
            if (Find.World == null)
            {
                throw new InvalidOperationException("no world loaded - load a colony first");
            }

            // The client cross-checks this list against world_pawns.count, so both commands must
            // read the same accessor rather than reconstructing a list from another collection.
            RimWorld.Planet.WorldPawns worldPawns = Find.WorldPawns;
            List<Pawn> pawns = worldPawns?.AllPawnsAliveOrDead;

            // This is the distinction that matters for the leak: the world-pawn GC may collect an
            // ordinary pawn, but not one the mod pinned with PawnDiscardDecideMode.KeepForever and
            // never unpinned. Read the set once so every record and the total describe one snapshot.
            HashSet<Pawn> forcefullyKeptPawns = null;
            int keptForeverCount = 0;
            try
            {
                forcefullyKeptPawns = worldPawns?.ForcefullyKeptPawns;
                keptForeverCount = forcefullyKeptPawns?.Count ?? 0;
            }
            catch (Exception)
            {
                // A missing or unreadable pin set is diagnostic uncertainty, not a reason for the
                // list verb to fail. The record-level fallback below is false as well.
                forcefullyKeptPawns = null;
                keptForeverCount = 0;
            }

            List<object> records = new List<object>();
            int nulls = 0;
            if (pawns != null)
            {
                foreach (Pawn pawn in pawns)
                {
                    if (pawn == null)
                    {
                        // A null source entry is not a pawn record, but its count explains an
                        // otherwise confusing difference between count and the returned list.
                        nulls++;
                        continue;
                    }

                    bool keptForever = false;
                    try
                    {
                        keptForever = forcefullyKeptPawns?.Contains(pawn) ?? false;
                    }
                    catch (Exception)
                    {
                        // A throwing membership test makes the whole pin snapshot untrustworthy.
                        // Reset records already visited too, so false and zero remain consistent.
                        forcefullyKeptPawns = null;
                        keptForeverCount = 0;
                        foreach (Dictionary<string, object> record in records)
                        {
                            record["keptForever"] = false;
                        }
                    }

                    try
                    {
                        records.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            ["id"] = pawn.thingIDNumber,
                            ["label"] = pawn.Name?.ToStringFull
                                ?? pawn.LabelShortCap
                                ?? pawn.KindLabel
                                ?? "-",
                            ["kind"] = pawn.kindDef?.defName ?? "-",
                            ["race"] = pawn.def?.defName ?? "-",
                            ["faction"] = pawn.Faction?.Name ?? "-",
                            ["situation"] = worldPawns.GetSituation(pawn).ToString(),
                            ["dead"] = pawn.Dead,
                            ["spawned"] = pawn.Spawned,
                            ["humanlike"] = pawn.RaceProps?.Humanlike ?? false,
                            ["keptForever"] = keptForever
                        });
                    }
                    catch (Exception ex)
                    {
                        // A damaged pawn must remain visible as damaged; dropping it would make
                        // the very leak this command is meant to name disappear from the report.
                        int id = -1;
                        try
                        {
                            id = pawn.thingIDNumber;
                        }
                        catch (Exception)
                        {
                            // The fallback id is deliberately best effort for a malformed pawn.
                        }

                        records.Add(new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            ["id"] = id,
                            ["label"] = $"(unreadable: {ex.GetType().Name})",
                            ["kind"] = "-",
                            ["race"] = "-",
                            ["faction"] = "-",
                            ["situation"] = "-",
                            ["dead"] = false,
                            ["spawned"] = false,
                            ["humanlike"] = false,
                            ["keptForever"] = false
                        });
                    }
                }
            }

            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["count"] = pawns?.Count ?? 0,
                ["nulls"] = nulls,
                ["keptForeverCount"] = keptForeverCount,
                ["pawns"] = records
            };
        }

        /// <summary>
        /// World pawn totals.
        ///
        /// This exists so the job-posting pawn leak is observable from outside the suite that trips
        /// it. Applicants are pinned world pawns, and a posting that closes without discarding them
        /// leaks one pawn per applicant permanently. The runner's own leak check watches the
        /// commercial timeline, market pressure and entity ids — not world pawns — so nothing in
        /// the game would otherwise report this. Reading it either side of a run makes the delta a
        /// number rather than a suspicion.
        /// </summary>
        private static object WorldPawnCount()
        {
            if (Find.World == null)
            {
                throw new InvalidOperationException("no world loaded - load a colony first");
            }

            // Deliberately the same accessor the job-posting and animal suites assert on
            // (Find.WorldPawns?.AllPawnsAliveOrDead?.Count). Reading a different one would give a
            // number that could not be compared against the assertion it is meant to explain.
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["allPawnsAliveOrDead"] = Find.WorldPawns?.AllPawnsAliveOrDead?.Count ?? 0
            };
        }

        /// <summary>
        /// Total and open job postings, so an orchestrator can verify the precondition a fresh-world
        /// run claims — no open postings before the job-posting suite runs — instead of assuming it.
        /// </summary>
        private static object PostingCount()
        {
            IntercolonyWorldComponent state = IntercolonyWorldComponent.Current;
            if (state == null)
            {
                throw new InvalidOperationException("no world loaded - load a colony first");
            }

            int open = 0;
            foreach (JobPosting posting in state.Postings)
            {
                if (posting.IsOpen)
                {
                    open++;
                }
            }

            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["total"] = state.Postings.Count,
                ["open"] = open
            };
        }

        /// <summary>
        /// What the game is, and whether it is ready to be asked anything else.
        ///
        /// **Must answer at the main menu**, with no world and no map. An orchestrator polls this
        /// to decide when a freshly launched game is ready, so "not ready yet" has to be a
        /// successful response describing the state - not a connection failure, and not an
        /// exception. Every field below degrades rather than throws.
        /// </summary>
        private static object Status()
        {
            Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.Ordinal);
            result["bridgeVersion"] = IntercolonyDevBridgeProtocol.Version;
            result["processId"] = System.Diagnostics.Process.GetCurrentProcess().Id;

            string modVersion = null;
            try
            {
                modVersion = LoadedModManager.GetMod<IntercolonyMod>()?.Content?.ModMetaData?.ModVersion;
            }
            catch (Exception)
            {
                // Version is a nicety; never let it be the reason status fails.
            }

            result["intercolonyVersion"] = modVersion;

            bool worldLoaded = Find.World != null;
            result["worldLoaded"] = worldLoaded;

            Map map = worldLoaded ? Find.CurrentMap : null;
            result["mapLoaded"] = map != null;
            result["mapIsPlayerHome"] = map != null && map.IsPlayerHome;

            int? tick = null;
            try
            {
                if (Find.TickManager != null)
                {
                    tick = Find.TickManager.TicksGame;
                }
            }
            catch (Exception)
            {
            }

            result["tick"] = tick;

            IntercolonyWorldComponent state = worldLoaded ? IntercolonyWorldComponent.Current : null;
            result["worldComponentAvailable"] = state != null;
            result["saveSchema"] = state?.SaveVersion;
            result["currentSaveSchema"] = IntercolonyWorldComponent.CurrentSaveVersion;

            return result;
        }
    }
}
#endif
