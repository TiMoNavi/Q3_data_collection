using UnityEngine;

namespace DataCapture.Synchronization
{
    [CreateAssetMenu(fileName = "SynchronizationHealthStateSO", menuName = "DataCapture/30 Time Synchronization/Synchronization Health State")]
    public class SynchronizationHealthStateSO : ScriptableObject, SObasic.IActiveState
    {
        public bool latestIsSendable;
        public bool requiredQueuesHealthy;
        public string latestDropReason;
        public long latestFrameId = -1;
        public long latestTimestampUnixMs;
        public int requiredQueueCount;
        public int healthyRequiredQueueCount;
        public int producedSnapshotCount;
        public int droppedSnapshotCount;

        public bool Active => latestIsSendable && requiredQueuesHealthy;

        public void SetLatest(bool isSendable, long frameId, long timestampUnixMs, string dropReason)
        {
            latestIsSendable = isSendable;
            latestFrameId = frameId;
            latestTimestampUnixMs = timestampUnixMs;
            latestDropReason = isSendable ? string.Empty : (dropReason ?? string.Empty);
            if (isSendable)
            {
                producedSnapshotCount++;
            }
            else
            {
                droppedSnapshotCount++;
            }
        }
    }
}
