using UnityEngine;

namespace DataCapture.Networking
{
    public enum PCReceiverConnectionPhase
    {
        NotStarted,
        DiscoveryStarting,
        DiscoveryBroadcastSent,
        WaitingForResponse,
        MalformedResponse,
        IncompatibleResponse,
        SocketError,
        Paired,
        Disconnected
    }

    [CreateAssetMenu(fileName = "PCReceiverConnectionStatusSO", menuName = "DataCapture/50 Encoding Network/PC Receiver Connection Status")]
    public class PCReceiverConnectionStatusSO : ScriptableObject, SObasic.IActiveState
    {
        [Header("Discovery Request")]
        public bool discoveryRequested;
        public int discoveryRequestRevision;
        public long lastDiscoveryRequestUnixMs;
        public string lastDiscoveryRequestSource = string.Empty;

        [Header("Connection")]
        public PCReceiverConnectionPhase phase = PCReceiverConnectionPhase.NotStarted;
        public bool handshakeSucceeded;
        public bool pcConnected;
        public bool portsPaired;
        public string remoteHost = string.Empty;
        public int discoveryPort;
        public int metadataPort;
        public int videoPort;
        public int protocolVersion;

        [Header("Timing")]
        public long discoveryStartedUnixMs;
        public long lastDiscoverySentUnixMs;
        public long lastResponseUnixMs;
        public long lastPairedUnixMs;
        public long lastRoundTripMs = -1;

        [Header("Diagnostics")]
        public int discoveryAttemptCount;
        public int malformedResponseCount;
        public int incompatibleResponseCount;
        public int socketErrorCount;
        public string lastStatusMessage = "Not started.";
        public string lastErrorMessage = string.Empty;
        public string lastBlocker = "PC receiver has not been discovered.";

        [Header("Network Diagnostics")]
        public string localNetworkSummary = string.Empty;
        public string lastDiscoveryTargets = string.Empty;
        public string networkWarning = string.Empty;
        public bool vpnOrTunnelInterfaceDetected;
        public int discoveryTargetCount;
        public int discoveryCandidateInterfaceCount;
        public int ignoredVpnInterfaceCount;

        [Header("Runtime Diagnostics")]
        public bool logConnectionChangesToUnity = true;
        public string lastDebugMessage = string.Empty;
        public long lastChangedUnixMs;
        public int connectionChangeCount;
        public string[] recentDebugEvents = new string[16];

        public bool CanStartRecording => handshakeSucceeded;
        public bool Active => CanStartRecording;

        public void RequestDiscovery(string source)
        {
            discoveryRequested = true;
            discoveryRequestRevision++;
            lastDiscoveryRequestUnixMs = NowMs();
            lastDiscoveryRequestSource = string.IsNullOrWhiteSpace(source) ? "Unknown" : source;
            lastStatusMessage = "Discovery requested by " + lastDiscoveryRequestSource + ".";
            lastBlocker = "Waiting for PC receiver discovery response.";
            AppendDebugEvent(lastStatusMessage, false);
        }

        public void StopDiscoveryRequest(string source)
        {
            discoveryRequested = false;
            lastDiscoveryRequestUnixMs = NowMs();
            lastDiscoveryRequestSource = string.IsNullOrWhiteSpace(source) ? "Unknown" : source;
            if (!CanStartRecording)
            {
                MarkDisconnected("Discovery request stopped by " + lastDiscoveryRequestSource + ".");
            }
            else
            {
                AppendDebugEvent("Discovery request stopped by " + lastDiscoveryRequestSource + ".", false);
            }
        }

        public void ResetForDiscovery(int configuredDiscoveryPort, int configuredProtocolVersion)
        {
            phase = PCReceiverConnectionPhase.DiscoveryStarting;
            handshakeSucceeded = false;
            pcConnected = false;
            portsPaired = false;
            remoteHost = string.Empty;
            discoveryPort = configuredDiscoveryPort;
            protocolVersion = configuredProtocolVersion;
            metadataPort = 0;
            videoPort = 0;
            discoveryStartedUnixMs = NowMs();
            lastDiscoverySentUnixMs = 0;
            lastResponseUnixMs = 0;
            lastPairedUnixMs = 0;
            lastRoundTripMs = -1;
            discoveryAttemptCount = 0;
            malformedResponseCount = 0;
            incompatibleResponseCount = 0;
            socketErrorCount = 0;
            lastStatusMessage = "Discovery started.";
            lastErrorMessage = string.Empty;
            lastBlocker = "Waiting for PC receiver discovery response.";
            lastDiscoveryTargets = string.Empty;
            networkWarning = string.Empty;
            vpnOrTunnelInterfaceDetected = false;
            discoveryTargetCount = 0;
            discoveryCandidateInterfaceCount = 0;
            ignoredVpnInterfaceCount = 0;
            AppendDebugEvent("PC receiver discovery started on UDP port " + discoveryPort + ".", false);
        }

        public void MarkNetworkDiagnostics(
            string localSummary,
            string discoveryTargets,
            string warning,
            bool detectedVpnOrTunnel,
            int candidateInterfaceCount,
            int ignoredVpnCount,
            int targetCount)
        {
            string previousWarning = networkWarning;
            localNetworkSummary = localSummary ?? string.Empty;
            lastDiscoveryTargets = discoveryTargets ?? string.Empty;
            networkWarning = warning ?? string.Empty;
            vpnOrTunnelInterfaceDetected = detectedVpnOrTunnel;
            discoveryCandidateInterfaceCount = candidateInterfaceCount;
            ignoredVpnInterfaceCount = ignoredVpnCount;
            discoveryTargetCount = targetCount;

            if (!string.IsNullOrWhiteSpace(networkWarning) && networkWarning != previousWarning)
            {
                lastBlocker = "Network warning: " + networkWarning;
                AppendDebugEvent(lastBlocker, true);
            }
        }

        public void MarkBroadcastSent(string discoveryTargets = "")
        {
            phase = PCReceiverConnectionPhase.DiscoveryBroadcastSent;
            discoveryAttemptCount++;
            lastDiscoverySentUnixMs = NowMs();
            if (!string.IsNullOrWhiteSpace(discoveryTargets))
            {
                lastDiscoveryTargets = discoveryTargets;
            }

            lastStatusMessage = "Discovery probes sent.";
            lastBlocker = "Waiting for PC receiver discovery response.";
            if (discoveryAttemptCount == 1)
            {
                AppendDebugEvent(lastStatusMessage + " targets=" + lastDiscoveryTargets, false);
            }
        }

        public void MarkMalformedResponse(string message)
        {
            phase = PCReceiverConnectionPhase.MalformedResponse;
            handshakeSucceeded = false;
            pcConnected = false;
            portsPaired = false;
            remoteHost = string.Empty;
            metadataPort = 0;
            videoPort = 0;
            malformedResponseCount++;
            lastResponseUnixMs = NowMs();
            lastStatusMessage = "Ignored malformed discovery response.";
            lastErrorMessage = message;
            lastBlocker = "PC receiver response was not valid JSON for this protocol.";
            AppendDebugEvent(lastStatusMessage + " " + message, true);
        }

        public void MarkIncompatibleResponse(string message)
        {
            phase = PCReceiverConnectionPhase.IncompatibleResponse;
            handshakeSucceeded = false;
            pcConnected = false;
            portsPaired = false;
            remoteHost = string.Empty;
            metadataPort = 0;
            videoPort = 0;
            incompatibleResponseCount++;
            lastResponseUnixMs = NowMs();
            lastStatusMessage = "Ignored incompatible discovery response.";
            lastErrorMessage = message;
            lastBlocker = "PC receiver protocol or magic did not match.";
            AppendDebugEvent(lastStatusMessage + " " + message, true);
        }

        public void MarkSocketError(string message)
        {
            phase = PCReceiverConnectionPhase.SocketError;
            socketErrorCount++;
            handshakeSucceeded = false;
            pcConnected = false;
            portsPaired = false;
            remoteHost = string.Empty;
            metadataPort = 0;
            videoPort = 0;
            lastStatusMessage = "Discovery socket failed.";
            lastErrorMessage = message;
            lastBlocker = "Quest could not open or use the UDP discovery socket.";
            AppendDebugEvent(lastStatusMessage + " " + message, true);
        }

        public void MarkPaired(string host, int discoveredMetadataPort, int discoveredVideoPort)
        {
            phase = PCReceiverConnectionPhase.Paired;
            pcConnected = !string.IsNullOrWhiteSpace(host);
            portsPaired = discoveredMetadataPort > 0 && discoveredVideoPort > 0;
            handshakeSucceeded = pcConnected && portsPaired;
            remoteHost = host;
            metadataPort = discoveredMetadataPort;
            videoPort = discoveredVideoPort;
            lastResponseUnixMs = NowMs();
            lastPairedUnixMs = lastResponseUnixMs;
            lastRoundTripMs = lastDiscoverySentUnixMs > 0
                ? Mathf.Max(0, (int)(lastResponseUnixMs - lastDiscoverySentUnixMs))
                : -1;
            lastStatusMessage = CanStartRecording
                ? "PC receiver paired. Recording is enabled."
                : "PC receiver responded, but metadata/video ports are invalid.";
            lastErrorMessage = string.Empty;
            lastBlocker = CanStartRecording
                ? string.Empty
                : "PC receiver response did not include valid metadata/video ports.";
            AppendDebugEvent(
                lastStatusMessage +
                " host=" + remoteHost +
                " metadataPort=" + metadataPort +
                " videoPort=" + videoPort +
                " rttMs=" + lastRoundTripMs,
                !CanStartRecording);
        }

        public void MarkDisconnected(string message)
        {
            phase = PCReceiverConnectionPhase.Disconnected;
            handshakeSucceeded = false;
            pcConnected = false;
            portsPaired = false;
            remoteHost = string.Empty;
            metadataPort = 0;
            videoPort = 0;
            lastStatusMessage = "PC receiver disconnected.";
            lastErrorMessage = message;
            lastBlocker = "PC receiver is not currently paired.";
            AppendDebugEvent(lastStatusMessage + " " + message, true);
        }

        public string GetRecordingBlockReason()
        {
            return string.IsNullOrWhiteSpace(lastBlocker)
                ? "PC receiver is not ready for recording."
                : lastBlocker;
        }

        private static long NowMs()
        {
            return System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        [ContextMenu("Log Runtime Diagnostics")]
        public void LogRuntimeDiagnostics()
        {
            Debug.Log(
                "PCReceiverConnectionStatus diagnostics: phase=" + phase +
                " handshakeSucceeded=" + handshakeSucceeded +
                " canStartRecording=" + CanStartRecording +
                " host=" + remoteHost +
                " metadataPort=" + metadataPort +
                " videoPort=" + videoPort +
                " rttMs=" + lastRoundTripMs +
                " targets=" + lastDiscoveryTargets +
                " networkWarning=" + networkWarning +
                " blocker=" + lastBlocker +
                " lastDebug=" + lastDebugMessage,
                this);
        }

        private void AppendDebugEvent(string message, bool warning)
        {
            lastDebugMessage = message;
            lastChangedUnixMs = NowMs();
            connectionChangeCount++;

            if (recentDebugEvents == null || recentDebugEvents.Length == 0)
            {
                recentDebugEvents = new string[16];
            }

            for (int i = recentDebugEvents.Length - 1; i > 0; i--)
            {
                recentDebugEvents[i] = recentDebugEvents[i - 1];
            }

            recentDebugEvents[0] = lastChangedUnixMs + " " + message;

            if (!logConnectionChangesToUnity)
            {
                return;
            }

            if (warning)
            {
                Debug.LogWarning("PCReceiverConnectionStatus: " + message, this);
            }
            else
            {
                Debug.Log("PCReceiverConnectionStatus: " + message, this);
            }
        }
    }
}
