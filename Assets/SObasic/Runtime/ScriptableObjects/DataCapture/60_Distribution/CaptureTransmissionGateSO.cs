using UnityEngine;
using UnityEngine.Serialization;

namespace DataCapture.Networking
{
    [CreateAssetMenu(fileName = "CaptureTransmissionGateSO", menuName = "DataCapture/50 Encoding Network/Capture Transmission Gate")]
    public class CaptureTransmissionGateSO : ScriptableObject, SObasic.IActiveState
    {
        [Header("Gate 1 - Output Route")]
        [FormerlySerializedAs("pcReceiverReady")]
        public bool outputRouteReady;
        [FormerlySerializedAs("pcReceiverBlocker")]
        public string outputRouteBlocker = "Output route is not ready.";

        [Header("Gate 2 - Recording Session")]
        public bool recordingActive;
        public string recordingBlocker = "Recording has not started.";

        [Header("Gate 3 - Synchronization Health")]
        public bool synthesisHealthy;
        public string synthesisBlocker = "Merged synchronization has not produced a sendable frame.";

        [Header("Result")]
        public bool canEncodeAndSend;
        public string activeBlocker = "Transmission is blocked.";
        public long lastUpdatedUnixMs;

        [Header("Runtime Diagnostics")]
        public bool logStateChangesToUnity = true;
        public string lastDebugMessage;
        public long lastChangedUnixMs;
        public int gateChangeCount;
        public string[] recentDebugEvents = new string[16];

        public bool Active => canEncodeAndSend;

        public void SetState(
            bool outputRouteReady,
            string outputRouteBlocker,
            bool recordingActive,
            string recordingBlocker,
            bool synthesisHealthy,
            string synthesisBlocker)
        {
            bool previousOutputRouteReady = this.outputRouteReady;
            bool previousRecordingActive = this.recordingActive;
            bool previousSynthesisHealthy = this.synthesisHealthy;
            bool previousCanEncodeAndSend = canEncodeAndSend;
            string previousActiveBlocker = activeBlocker;

            this.outputRouteReady = outputRouteReady;
            this.outputRouteBlocker = outputRouteReady ? string.Empty : Sanitize(outputRouteBlocker, "Output route is not ready.");
            this.recordingActive = recordingActive;
            this.recordingBlocker = recordingActive ? string.Empty : Sanitize(recordingBlocker, "Recording has not started.");
            this.synthesisHealthy = synthesisHealthy;
            this.synthesisBlocker = synthesisHealthy ? string.Empty : Sanitize(synthesisBlocker, "Merged synchronization is not healthy.");

            canEncodeAndSend = outputRouteReady && recordingActive && synthesisHealthy;
            activeBlocker = ResolveBlocker();
            lastUpdatedUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (previousOutputRouteReady != this.outputRouteReady ||
                previousRecordingActive != this.recordingActive ||
                previousSynthesisHealthy != this.synthesisHealthy ||
                previousCanEncodeAndSend != canEncodeAndSend ||
                previousActiveBlocker != activeBlocker)
            {
                string message =
                    "TransmissionGate canEncodeAndSend=" + canEncodeAndSend +
                    " route=" + this.outputRouteReady +
                    " recording=" + this.recordingActive +
                    " synthesis=" + this.synthesisHealthy;

                if (!canEncodeAndSend)
                {
                    message += " blocker=" + activeBlocker;
                }

                AppendDebugEvent(message, previousCanEncodeAndSend && !canEncodeAndSend);
            }
        }

        private string ResolveBlocker()
        {
            if (canEncodeAndSend)
            {
                return string.Empty;
            }

            if (!outputRouteReady)
            {
                return outputRouteBlocker;
            }

            if (!recordingActive)
            {
                return recordingBlocker;
            }

            return synthesisBlocker;
        }

        private static string Sanitize(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        [ContextMenu("Log Runtime Diagnostics")]
        public void LogRuntimeDiagnostics()
        {
            Debug.Log(
                "TransmissionGate diagnostics: canEncodeAndSend=" + canEncodeAndSend +
                " route=" + outputRouteReady +
                " recording=" + recordingActive +
                " synthesis=" + synthesisHealthy +
                " blocker=" + activeBlocker +
                " lastDebug=" + lastDebugMessage,
                this);
        }

        private void AppendDebugEvent(string message, bool warning)
        {
            lastDebugMessage = message;
            lastChangedUnixMs = lastUpdatedUnixMs;
            gateChangeCount++;

            if (recentDebugEvents == null || recentDebugEvents.Length == 0)
            {
                recentDebugEvents = new string[16];
            }

            for (int i = recentDebugEvents.Length - 1; i > 0; i--)
            {
                recentDebugEvents[i] = recentDebugEvents[i - 1];
            }

            recentDebugEvents[0] = lastChangedUnixMs + " " + message;

            if (!logStateChangesToUnity)
            {
                return;
            }

            if (warning)
            {
                Debug.LogWarning(message, this);
            }
            else
            {
                Debug.Log(message, this);
            }
        }
    }
}
