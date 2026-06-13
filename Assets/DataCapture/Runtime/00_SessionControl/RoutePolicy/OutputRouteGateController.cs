using UnityEngine;

namespace DataCapture.Networking
{
    [DefaultExecutionOrder(-11000)]
    public sealed class OutputRouteGateController : MonoBehaviour
    {
        [Header("SO Inputs")]
        [SerializeField] private SessionModeSO sessionMode;
        [SerializeField] private NetworkSenderConfigurationSO networkConfiguration;
        [SerializeField] private PCReceiverConnectionStatusSO pcReceiverStatus;
        [SerializeField] private PCDiscoveryRequestSO discoveryRequest;

        [Header("SO Output")]
        [SerializeField] private OutputRouteGateSO outputRouteGate;

        [Header("Handshake Stage")]
        [SerializeField] private Behaviour handshakeStage;
        [SerializeField] private bool clearPendingDiscoveryWhenHandshakeSkipped = true;
        [SerializeField] private bool updateEveryFrame = true;

        private void OnEnable()
        {
            RefreshGate();
        }

        private void Update()
        {
            if (updateEveryFrame)
            {
                RefreshGate();
            }
        }

        [ContextMenu("Refresh Output Route Gate")]
        public void RefreshGate()
        {
            if (outputRouteGate == null)
            {
                SetHandshakeStageEnabled(false);
                return;
            }

            if (networkConfiguration == null)
            {
                outputRouteGate.SetUnavailable("NetworkSenderConfigurationSO is not assigned.");
                SetHandshakeStageEnabled(false);
                return;
            }

            bool requiresHandshake = sessionMode != null
                ? sessionMode.UsesNetwork
                : networkConfiguration.UsesNetwork;
            bool handshakeSatisfied =
                !requiresHandshake ||
                (pcReceiverStatus != null && pcReceiverStatus.CanStartRecording);
            string handshakeBlocker = requiresHandshake
                ? ResolveHandshakeBlocker()
                : string.Empty;

            outputRouteGate.SetState(
                sessionMode != null ? sessionMode.mode : InferSessionMode(networkConfiguration),
                networkConfiguration.outputTarget,
                requiresHandshake,
                handshakeSatisfied,
                handshakeBlocker);

            SetHandshakeStageEnabled(requiresHandshake);

            if (!requiresHandshake && clearPendingDiscoveryWhenHandshakeSkipped)
            {
                discoveryRequest?.Clear();
            }
        }

        private string ResolveHandshakeBlocker()
        {
            if (pcReceiverStatus == null)
            {
                return "PCReceiverConnectionStatusSO is not assigned.";
            }

            string blocker = pcReceiverStatus.GetRecordingBlockReason();
            return string.IsNullOrWhiteSpace(blocker)
                ? "Network handshake has not completed."
                : blocker;
        }

        private void SetHandshakeStageEnabled(bool enabled)
        {
            if (handshakeStage != null && handshakeStage.enabled != enabled)
            {
                handshakeStage.enabled = enabled;
            }
        }

        private static DataCaptureSessionMode InferSessionMode(NetworkSenderConfigurationSO configuration)
        {
            return configuration != null && configuration.UsesNetwork
                ? DataCaptureSessionMode.NetworkOrHybrid
                : DataCaptureSessionMode.LocalOnly;
        }
    }
}
