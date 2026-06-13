using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DataCapture.Networking
{
    [ExecuteAlways]
    [DefaultExecutionOrder(-10000)]
    public class LanDiscoveryClient : MonoBehaviour
    {
        [SerializeField] private NetworkSenderConfigurationSO configuration;
        [SerializeField] private PCDiscoveryRequestSO discoveryRequest;
        [SerializeField] private PCReceiverConnectionStatusSO pcReceiverStatus;
        [SerializeField] private bool consumeDiscoveryRequestAfterStart = true;
        [SerializeField] private bool discoverOnStart = true;
        [SerializeField] private bool allowEditorDiscovery = true;
        [SerializeField] private bool repeatUntilDiscovered = true;
        [SerializeField] private bool logDiscoveryEvents = true;
        [SerializeField] private float retryIntervalSeconds = 2f;
        [SerializeField] private int maxTargetsPerAttempt = 16;

        private UdpClient client;
        private IPEndPoint remoteEndPoint;
        private float nextDiscoveryTime;
        private bool discoveryActive;
        private bool lastRequestActive;
        private int lastRequestRevision = -1;
        private bool lastDiscoveryRequested;
        private int lastDiscoveryRequestRevision = -1;

#if UNITY_EDITOR
        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                EditorApplication.update -= EditorUpdate;
                EditorApplication.update += EditorUpdate;
            }
        }
#endif

        private void Start()
        {
            if (Application.isPlaying && discoverOnStart)
            {
                RequestDiscovery("LanDiscoveryClient Start");
            }
        }

        private void Update()
        {
            TickDiscovery();
        }

#if UNITY_EDITOR
        private void EditorUpdate()
        {
            if (!Application.isPlaying && this != null)
            {
                TickDiscovery();
            }
        }
#endif

        private void TickDiscovery()
        {
            if (!CanUseDiscovery())
            {
                return;
            }

            if (!Application.isPlaying && !allowEditorDiscovery)
            {
                return;
            }

            HandleDiscoveryRequestSO();
            PollResponse();

            if (discoveryActive &&
                repeatUntilDiscovered &&
                !configuration.receiverDiscovered &&
                GetTimeSeconds() >= nextDiscoveryTime)
            {
                SendDiscovery();
            }
        }

        [ContextMenu("Start Discovery")]
        public void StartDiscovery()
        {
            if (configuration == null)
            {
                return;
            }

            configuration.receiverDiscovered = false;
            configuration.remoteHost = string.Empty;
            configuration.metadataPort = 0;
            configuration.videoPort = 0;
            pcReceiverStatus?.ResetForDiscovery(configuration.discoveryPort, configuration.protocolVersion);
            configuration.lastDiscoveryStatus = "Discovery started.";
            discoveryActive = true;
            SendDiscovery();
        }

        [ContextMenu("Stop Discovery")]
        public void StopDiscovery()
        {
            discoveryActive = false;
            pcReceiverStatus?.StopDiscoveryRequest("LanDiscoveryClient");
            configuration.receiverDiscovered = false;
            configuration.remoteHost = string.Empty;
            configuration.metadataPort = 0;
            configuration.videoPort = 0;
            CloseClient();
        }

        private void HandleDiscoveryRequestSO()
        {
            if (discoveryRequest != null)
            {
                bool active = discoveryRequest.requested;
                bool revisionChanged = discoveryRequest.requestRevision != lastRequestRevision;
                bool stateChanged = active != lastRequestActive;
                lastRequestActive = active;
                lastRequestRevision = discoveryRequest.requestRevision;

                if (active && (revisionChanged || stateChanged || !discoveryActive))
                {
                    StartDiscovery();
                    if (consumeDiscoveryRequestAfterStart)
                    {
                        discoveryRequest.Clear();
                        lastRequestActive = false;
                    }
                }
                else if (!active && stateChanged && discoveryActive)
                {
                    StopDiscovery();
                }

                return;
            }

            if (pcReceiverStatus == null)
            {
                return;
            }

            bool requested = pcReceiverStatus.discoveryRequested;
            bool legacyRevisionChanged = pcReceiverStatus.discoveryRequestRevision != lastDiscoveryRequestRevision;
            bool requestStateChanged = requested != lastDiscoveryRequested;
            lastDiscoveryRequested = requested;
            lastDiscoveryRequestRevision = pcReceiverStatus.discoveryRequestRevision;

            if (requested && (legacyRevisionChanged || requestStateChanged || !discoveryActive))
            {
                StartDiscovery();
            }
            else if (!requested && requestStateChanged && discoveryActive)
            {
                StopDiscovery();
            }
        }

        public void RequestDiscovery(string source)
        {
            if (discoveryRequest != null)
            {
                discoveryRequest.Request(source);
                return;
            }

            pcReceiverStatus?.RequestDiscovery(source);
            StartDiscovery();
        }

        private void SendDiscovery()
        {
            if (configuration == null || !configuration.enableLanDiscovery)
            {
                return;
            }

            byte[] payload = Encoding.UTF8.GetBytes(configuration.discoveryMagic);
            LanDiscoveryNetworkPlanner.Plan plan = LanDiscoveryNetworkPlanner.Build(configuration, Application.isPlaying);
            ApplyNetworkDiagnostics(plan);

            if (!EnsureClient(plan.BindAddress))
            {
                return;
            }

            if (plan.Targets.Count == 0)
            {
                string message = "No LAN discovery targets were available.";
                configuration.lastDiscoveryStatus = message;
                pcReceiverStatus?.MarkSocketError(message);
                LogDiscovery(message, true);
                nextDiscoveryTime = GetTimeSeconds() + Mathf.Max(0.25f, retryIntervalSeconds);
                return;
            }

            int sentCount = 0;
            string lastError = string.Empty;
            int targetLimit = Mathf.Clamp(maxTargetsPerAttempt, 1, 64);
            int targetCount = Mathf.Min(plan.Targets.Count, targetLimit);
            for (int i = 0; i < targetCount; i++)
            {
                IPEndPoint target = plan.Targets[i].EndPoint;
                try
                {
                    client.Send(payload, payload.Length, target);
                    sentCount++;
                    LogDiscovery("Discovery probe sent to " + target + " via " + plan.Targets[i].Source + ".");
                }
                catch (SocketException ex)
                {
                    lastError = target + ": " + ex.Message;
                    LogDiscovery("Discovery probe failed for " + lastError, true);
                }
            }

            if (sentCount == 0)
            {
                string message = "Discovery socket failed for all targets. " + lastError;
                configuration.lastDiscoveryStatus = message;
                pcReceiverStatus?.MarkSocketError(message);
                LogDiscovery(message, true);
                nextDiscoveryTime = GetTimeSeconds() + Mathf.Max(0.25f, retryIntervalSeconds);
                return;
            }

            configuration.lastDiscoveryStatus = "Discovery probes sent to " + sentCount + " target(s).";
            configuration.lastDiscoveryUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            pcReceiverStatus?.MarkBroadcastSent(plan.TargetSummary);
            nextDiscoveryTime = GetTimeSeconds() + Mathf.Max(0.25f, retryIntervalSeconds);
        }

        private void PollResponse()
        {
            if (client == null)
            {
                return;
            }

            while (client.Available > 0)
            {
                byte[] payload = client.Receive(ref remoteEndPoint);
                string json = Encoding.UTF8.GetString(payload);
                DiscoveryResponse response;
                try
                {
                    response = JsonUtility.FromJson<DiscoveryResponse>(json);
                }
                catch (ArgumentException)
                {
                    configuration.lastDiscoveryStatus = "Ignored malformed discovery response.";
                    pcReceiverStatus?.MarkMalformedResponse("Discovery response JSON could not be parsed.");
                    LogDiscovery("Ignored malformed discovery response.", true);
                    continue;
                }

                if (response == null || response.magic != "Q3DC" || response.protocolVersion != configuration.protocolVersion)
                {
                    configuration.lastDiscoveryStatus = "Ignored incompatible discovery response.";
                    string magic = response != null ? response.magic : "<null>";
                    int protocolVersion = response != null ? response.protocolVersion : -1;
                    pcReceiverStatus?.MarkIncompatibleResponse(
                        $"Expected magic Q3DC/protocol {configuration.protocolVersion}, got magic {magic}/protocol {protocolVersion}.");
                    LogDiscovery("Ignored incompatible discovery response.", true);
                    continue;
                }

                configuration.remoteHost = remoteEndPoint.Address.ToString();
                configuration.metadataPort = response.metadataPort;
                configuration.videoPort = response.videoPort;
                configuration.receiverDiscovered = true;
                pcReceiverStatus?.MarkPaired(configuration.remoteHost, response.metadataPort, response.videoPort);
                configuration.lastDiscoveryStatus = "Receiver discovered at " + configuration.remoteHost;
                configuration.lastDiscoveryUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                LogDiscovery("Receiver discovered at " + configuration.remoteHost +
                    " metadataPort=" + response.metadataPort +
                    " videoPort=" + response.videoPort + ".");
            }
        }

        private void ApplyNetworkDiagnostics(LanDiscoveryNetworkPlanner.Plan plan)
        {
            if (configuration == null || plan == null)
            {
                return;
            }

            configuration.localNetworkSummary = plan.LocalNetworkSummary;
            configuration.lastDiscoveryTargets = plan.TargetSummary;
            configuration.networkWarning = plan.Warning;
            configuration.vpnOrTunnelInterfaceDetected = plan.VpnOrTunnelInterfaceDetected;
            configuration.discoveryTargetCount = plan.Targets.Count;
            configuration.discoveryCandidateInterfaceCount = plan.CandidateInterfaceCount;
            configuration.ignoredVpnInterfaceCount = plan.IgnoredInterfaceCount;

            pcReceiverStatus?.MarkNetworkDiagnostics(
                plan.LocalNetworkSummary,
                plan.TargetSummary,
                plan.Warning,
                plan.VpnOrTunnelInterfaceDetected,
                plan.CandidateInterfaceCount,
                plan.IgnoredInterfaceCount,
                plan.Targets.Count);
        }

        private bool EnsureClient(IPAddress bindAddress)
        {
            if (client != null)
            {
                return true;
            }

            try
            {
                client = new UdpClient(new IPEndPoint(bindAddress ?? IPAddress.Any, 0));
                client.EnableBroadcast = true;
                client.Client.Blocking = false;
                remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                return true;
            }
            catch (SocketException ex)
            {
                configuration.lastDiscoveryStatus = "Discovery socket failed: " + ex.Message;
                pcReceiverStatus?.MarkSocketError(ex.Message);
                LogDiscovery("Discovery socket failed: " + ex.Message, true);
                return false;
            }
        }

        private void LogDiscovery(string message, bool warning = false)
        {
            if (!logDiscoveryEvents)
            {
                return;
            }

            string formatted = "LanDiscoveryClient: " + message;
            if (warning)
            {
                Debug.LogWarning(formatted, this);
            }
            else
            {
                Debug.Log(formatted, this);
            }
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            EditorApplication.update -= EditorUpdate;
#endif
            pcReceiverStatus?.MarkDisconnected("LanDiscoveryClient was disabled.");
            CloseClient();
        }

        private bool CanUseDiscovery()
        {
            return configuration != null &&
                configuration.enableLanDiscovery;
        }

        private void CloseClient()
        {
            client?.Close();
            client = null;
            discoveryActive = false;
        }

        private static float GetTimeSeconds()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                return (float)EditorApplication.timeSinceStartup;
            }
#endif
            return Time.unscaledTime;
        }

        [Serializable]
        private class DiscoveryResponse
        {
            public string magic;
            public int protocolVersion;
            public string deviceRole;
            public int metadataPort;
            public int videoPort;
        }
    }
}
