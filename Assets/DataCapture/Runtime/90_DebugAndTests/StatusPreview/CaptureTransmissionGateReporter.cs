using DataCapture.Diagnostics;
using DataCapture.Synchronization;
using SObasic.CurrentQueueBridge;
using UnityEngine;

namespace DataCapture.Networking
{
    [DefaultExecutionOrder(1000)]
    public class CaptureTransmissionGateReporter : MonoBehaviour
    {
        [SerializeField] private OutputRouteGateSO outputRouteGate;
        [SerializeField] private RecordingSessionStateSO recordingState;
        [SerializeField] private TimestampMergerDebugStateSO mergerDebugState;
        [SerializeField] private QueueDebugStateSO[] requiredQueueStates;
        [SerializeField] private CaptureTransmissionGateSO transmissionGate;
        [SerializeField] private bool updateEveryFrame = true;

        private void Update()
        {
            if (updateEveryFrame)
            {
                UpdateGate();
            }
        }

        [ContextMenu("Update Transmission Gate")]
        public void UpdateGate()
        {
            if (transmissionGate == null)
            {
                return;
            }

            bool outputRouteReady = outputRouteGate != null && outputRouteGate.CanStartRecording;
            string outputRouteBlocker = outputRouteGate != null
                ? outputRouteGate.GetRecordingBlockReason()
                : "Output route gate SO is not assigned.";

            bool recordingActive = recordingState != null && recordingState.IsRecording;
            string recordingBlocker = recordingState == null
                ? "Recording session state SO is not assigned."
                : "Recording session is " + recordingState.State + ".";

            bool synthesisHealthy = IsSynthesisHealthy(out string synthesisBlocker);

            transmissionGate.SetState(
                outputRouteReady,
                outputRouteBlocker,
                recordingActive,
                recordingBlocker,
                synthesisHealthy,
                synthesisBlocker);
        }

        private bool IsSynthesisHealthy(out string blocker)
        {
            if (mergerDebugState == null)
            {
                blocker = "Timestamp merger debug state SO is not assigned.";
                return false;
            }

            if (!mergerDebugState.latestIsSendable)
            {
                blocker = string.IsNullOrWhiteSpace(mergerDebugState.latestDropReason)
                    ? "Latest merged frame is not sendable. Status=" + mergerDebugState.latestStatus + "."
                    : mergerDebugState.latestDropReason;
                return false;
            }

            if (requiredQueueStates != null)
            {
                foreach (QueueDebugStateSO queueState in requiredQueueStates)
                {
                    if (queueState == null)
                    {
                        continue;
                    }

                    if (!queueState.isHealthy)
                    {
                        blocker = queueState.queueName + ": " + queueState.statusMessage;
                        return false;
                    }
                }
            }

            blocker = string.Empty;
            return true;
        }
    }
}
