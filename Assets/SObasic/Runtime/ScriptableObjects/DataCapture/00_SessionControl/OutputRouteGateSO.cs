using UnityEngine;

namespace DataCapture.Networking
{
    [CreateAssetMenu(fileName = "OutputRouteGateSO", menuName = "DataCapture/50 Encoding Network/Output Route Gate")]
    public sealed class OutputRouteGateSO : ScriptableObject, SObasic.IActiveState
    {
        [Header("Route")]
        public DataCaptureSessionMode sessionMode = DataCaptureSessionMode.LocalOnly;
        public bool configurationAssigned;
        public StreamOutputTarget outputTarget = StreamOutputTarget.RemoteReceiver;
        public bool requiresNetworkHandshake;
        public bool handshakeStageEnabled;

        [Header("Handshake Result")]
        public bool networkHandshakeSatisfied;
        public string networkHandshakeBlocker = "Network handshake has not completed.";

        [Header("Recording Gate")]
        public bool canStartRecording;
        public string activeBlocker = "Output route has not been evaluated.";
        public string statusMessage = "Output route has not been evaluated.";
        public long lastUpdatedUnixMs;

        public bool Active => canStartRecording;
        public bool CanStartRecording => canStartRecording;

        public void SetUnavailable(string blocker)
        {
            configurationAssigned = false;
            requiresNetworkHandshake = false;
            handshakeStageEnabled = false;
            networkHandshakeSatisfied = false;
            canStartRecording = false;
            activeBlocker = Sanitize(blocker, "Network sender configuration SO is not assigned.");
            networkHandshakeBlocker = activeBlocker;
            statusMessage = activeBlocker;
            lastUpdatedUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        public void SetState(
            DataCaptureSessionMode sessionMode,
            StreamOutputTarget outputTarget,
            bool requiresNetworkHandshake,
            bool networkHandshakeSatisfied,
            string networkHandshakeBlocker)
        {
            configurationAssigned = true;
            this.sessionMode = sessionMode;
            this.outputTarget = outputTarget;
            this.requiresNetworkHandshake = requiresNetworkHandshake;
            handshakeStageEnabled = requiresNetworkHandshake;
            this.networkHandshakeSatisfied = networkHandshakeSatisfied;
            this.networkHandshakeBlocker = networkHandshakeSatisfied
                ? string.Empty
                : Sanitize(networkHandshakeBlocker, "Network handshake has not completed.");

            canStartRecording = !requiresNetworkHandshake || networkHandshakeSatisfied;
            activeBlocker = canStartRecording ? string.Empty : this.networkHandshakeBlocker;
            statusMessage = requiresNetworkHandshake
                ? (networkHandshakeSatisfied
                    ? "Network output route is ready."
                    : "Network output route is waiting for handshake.")
                : "Local output route skips network handshake.";
            lastUpdatedUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        public string GetRecordingBlockReason()
        {
            return canStartRecording
                ? string.Empty
                : Sanitize(activeBlocker, "Output route is not ready.");
        }

        private static string Sanitize(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
    }
}
