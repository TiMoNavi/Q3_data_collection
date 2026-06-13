using DataCapture.Diagnostics;
using DataCapture.Synchronization;
using UnityEngine;

namespace DataCapture.Diagnostics
{
    public class FrameTimingDebugReporter : MonoBehaviour
    {
        [SerializeField] private MergedFrameSnapshotQueueSO mergedQueue;
        [SerializeField] private TimestampMergerDebugStateSO debugState;
        [SerializeField] private bool updateEveryFrame = true;

        private void Update()
        {
            if (updateEveryFrame)
            {
                UpdateDebugState();
            }
        }

        [ContextMenu("Update Debug State")]
        public void UpdateDebugState()
        {
            if (mergedQueue == null || debugState == null)
            {
                return;
            }

            MergedFrameSnapshotRecord[] records = mergedQueue.ExportSnapshot();
            if (records.Length == 0)
            {
                debugState.SetLatest(
                    0,
                    0,
                    0,
                    false,
                    false,
                    false,
                    false,
                    false,
                    false,
                    -1,
                    -1,
                    -1,
                    -1,
                    -1,
                    -1,
                    MergedFrameStatus.WaitingForCamera,
                    false,
                    MergedFrameStreamMask.CameraTiming,
                    MergedFrameStreamMask.CameraTiming,
                    MergedFrameStreamMask.None,
                    "Merged queue is empty.",
                    "Merged queue is empty.");
                return;
            }

            MergedFrameSnapshotRecord latest = records[records.Length - 1];
            debugState.SetLatest(
                latest.frameId,
                latest.timestampUnixMs,
                records.Length,
                latest.hasCameraImage,
                latest.hasCameraPose,
                latest.hasCameraMetadata,
                latest.hasCameraStreamState,
                latest.hasController,
                latest.hasVirtualLayer,
                latest.cameraImageTimeDeltaMs,
                latest.cameraPoseTimeDeltaMs,
                latest.cameraMetadataTimeDeltaMs,
                latest.cameraStreamStateTimeDeltaMs,
                latest.controllerTimeDeltaMs,
                latest.virtualLayerTimeDeltaMs,
                latest.status,
                latest.isSendable,
                latest.requiredStreamMask,
                latest.missingRequiredStreamMask,
                latest.matchedStreamMask,
                latest.dropReason,
                latest.status.ToString());
        }
    }
}
