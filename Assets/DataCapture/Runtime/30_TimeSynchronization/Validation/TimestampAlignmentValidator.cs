using UnityEngine;

namespace DataCapture.Synchronization
{
    public class TimestampAlignmentValidator : MonoBehaviour
    {
        [SerializeField] private MergedFrameSnapshotQueueSO mergedQueue;
        [SerializeField] private long warningDeltaMs = 50;

        [ContextMenu("Validate Latest Merged Snapshot")]
        public bool ValidateLatestMergedSnapshot()
        {
            if (mergedQueue == null || mergedQueue.Count == 0)
            {
                Debug.LogWarning("Merged queue is empty or not assigned.", this);
                return false;
            }

            MergedFrameSnapshotRecord[] records = mergedQueue.ExportSnapshot();
            MergedFrameSnapshotRecord latest = records[records.Length - 1];
            bool valid = IsDeltaValid(latest.cameraImageTimeDeltaMs) &&
                         IsDeltaValid(latest.cameraPoseTimeDeltaMs) &&
                         IsDeltaValid(latest.cameraMetadataTimeDeltaMs) &&
                         IsDeltaValid(latest.cameraStreamStateTimeDeltaMs) &&
                         IsDeltaValid(latest.controllerTimeDeltaMs);

            if (!valid)
            {
                Debug.LogWarning("Latest merged snapshot has a time delta above warning threshold.", this);
            }

            return valid;
        }

        private bool IsDeltaValid(long deltaMs)
        {
            return deltaMs < 0 || deltaMs <= warningDeltaMs;
        }
    }
}
