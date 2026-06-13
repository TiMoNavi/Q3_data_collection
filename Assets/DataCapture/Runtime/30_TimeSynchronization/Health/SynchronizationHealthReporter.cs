using DataCapture.Diagnostics;
using UnityEngine;

namespace DataCapture.Synchronization
{
    public class SynchronizationHealthReporter : MonoBehaviour
    {
        [SerializeField] private TimestampMergerDebugStateSO timestampMergerDebugState;
        [SerializeField] private SynchronizationHealthStateSO healthState;
        [SerializeField] private bool updateEveryFrame = true;

        private long lastReportedFrameId = -1;
        private long lastReportedTimestampUnixMs = -1;
        private MergedFrameStatus lastReportedStatus;
        private bool lastReportedSendable;
        private string lastReportedDropReason = string.Empty;

        private void Update()
        {
            if (updateEveryFrame)
            {
                UpdateHealthState();
            }
        }

        [ContextMenu("Update Health State")]
        public void UpdateHealthState()
        {
            if (healthState == null)
            {
                return;
            }

            if (timestampMergerDebugState == null)
            {
                healthState.requiredQueuesHealthy = false;
                healthState.requiredQueueCount = 0;
                healthState.healthyRequiredQueueCount = 0;
                ReportLatest(false, -1, 0, "TimestampMergerDebugState is not assigned.", MergedFrameStatus.WaitingForCamera);
                return;
            }

            MergedFrameStreamMask requiredMask = timestampMergerDebugState.latestRequiredStreamMask;
            MergedFrameStreamMask missingMask = timestampMergerDebugState.latestMissingRequiredStreamMask;
            int requiredCount = CountStreams(requiredMask);
            int missingCount = CountStreams(missingMask);
            bool requiredHealthy = requiredCount > 0 && missingMask == MergedFrameStreamMask.None;
            string dropReason = timestampMergerDebugState.latestDropReason;
            if (string.IsNullOrEmpty(dropReason) && !timestampMergerDebugState.latestIsSendable)
            {
                dropReason = timestampMergerDebugState.statusMessage;
            }

            healthState.requiredQueueCount = requiredCount;
            healthState.healthyRequiredQueueCount = Mathf.Max(0, requiredCount - missingCount);
            healthState.requiredQueuesHealthy = requiredHealthy;
            ReportLatest(
                timestampMergerDebugState.latestIsSendable,
                timestampMergerDebugState.latestCameraFrameId,
                timestampMergerDebugState.latestTimestampUnixMs,
                dropReason,
                timestampMergerDebugState.latestStatus);
        }

        private void ReportLatest(
            bool isSendable,
            long frameId,
            long timestampUnixMs,
            string dropReason,
            MergedFrameStatus status)
        {
            dropReason ??= string.Empty;
            if (frameId == lastReportedFrameId &&
                timestampUnixMs == lastReportedTimestampUnixMs &&
                status == lastReportedStatus &&
                isSendable == lastReportedSendable &&
                dropReason == lastReportedDropReason)
            {
                return;
            }

            healthState.SetLatest(isSendable, frameId, timestampUnixMs, dropReason);
            lastReportedFrameId = frameId;
            lastReportedTimestampUnixMs = timestampUnixMs;
            lastReportedStatus = status;
            lastReportedSendable = isSendable;
            lastReportedDropReason = dropReason;
        }

        private static int CountStreams(MergedFrameStreamMask mask)
        {
            int count = 0;
            if ((mask & MergedFrameStreamMask.CameraTiming) != 0) count++;
            if ((mask & MergedFrameStreamMask.CameraImage) != 0) count++;
            if ((mask & MergedFrameStreamMask.CameraPose) != 0) count++;
            if ((mask & MergedFrameStreamMask.CameraMetadata) != 0) count++;
            if ((mask & MergedFrameStreamMask.CameraStreamState) != 0) count++;
            if ((mask & MergedFrameStreamMask.Controller) != 0) count++;
            if ((mask & MergedFrameStreamMask.VirtualLayer) != 0) count++;
            return count;
        }
    }
}
