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

        private static void Stop()
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
                catch (Exception ex)
                {
                    if (stopping)
                    {
                        return;
                    }

                    // One bad connection must never take the listener down with it - the next
                    // command has to work. This is the whole reason the loop catches broadly.
                    IntercolonyLog.Warning($"dev bridge: connection failed: {ex.Message}");
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
                default:
                    throw new InvalidOperationException($"unknown command '{command}'");
            }
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
