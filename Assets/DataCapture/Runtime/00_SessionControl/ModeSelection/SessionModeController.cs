using UnityEngine;

namespace DataCapture.Networking
{
    [ExecuteAlways]
    [DefaultExecutionOrder(-12000)]
    public sealed class SessionModeController : MonoBehaviour
    {
        [Header("SO State")]
        [SerializeField] private SessionModeSO sessionMode;
        [SerializeField] private NetworkSenderConfigurationSO networkConfiguration;

        [Header("Mode To Output Target")]
        [SerializeField] private bool applyModeToNetworkConfiguration = true;
        [SerializeField] private StreamOutputTarget localOnlyTarget = StreamOutputTarget.LocalFile;
        [SerializeField] private StreamOutputTarget networkOrHybridTarget = StreamOutputTarget.RemoteAndLocalFile;

        [Header("Runtime")]
        [SerializeField] private bool refreshEveryFrame = true;

        private void OnEnable()
        {
            RefreshMode();
        }

        private void Update()
        {
            if (refreshEveryFrame)
            {
                RefreshMode();
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            RefreshMode();
        }
#endif

        [ContextMenu("Refresh Session Mode")]
        public void RefreshMode()
        {
            if (sessionMode == null)
            {
                return;
            }

            if (networkConfiguration == null)
            {
                sessionMode.lastBlocker = "NetworkSenderConfigurationSO is not assigned.";
                return;
            }

            if (applyModeToNetworkConfiguration)
            {
                StreamOutputTarget target = sessionMode.UsesNetwork ? networkOrHybridTarget : localOnlyTarget;
                if (networkConfiguration.outputTarget != target)
                {
                    networkConfiguration.outputTarget = target;
                }
            }
            else
            {
                DataCaptureSessionMode inferredMode = networkConfiguration.UsesNetwork
                    ? DataCaptureSessionMode.NetworkOrHybrid
                    : DataCaptureSessionMode.LocalOnly;
                if (sessionMode.mode != inferredMode)
                {
                    sessionMode.SetMode(inferredMode);
                }
            }

            sessionMode.modeLabel = BuildModeLabel();
            sessionMode.lastBlocker = string.Empty;
            sessionMode.lastUpdatedUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        private string BuildModeLabel()
        {
            string target = networkConfiguration != null
                ? networkConfiguration.outputTarget.ToString()
                : "NoOutputTarget";
            return sessionMode.mode + " / " + target;
        }
    }
}
