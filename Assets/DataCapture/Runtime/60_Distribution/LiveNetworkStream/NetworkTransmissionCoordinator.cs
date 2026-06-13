using DataCapture.Synchronization;
using UnityEngine;

namespace DataCapture.Networking
{
    public class NetworkTransmissionCoordinator : MonoBehaviour
    {
        [SerializeField] private MergedFrameSnapshotQueueSO mergedQueue;
        [SerializeField] private MetadataPacketSender metadataSender;
        [SerializeField] private CaptureTransmissionGateSO transmissionGate;
        [SerializeField] private bool sendLatestMetadataOnUpdate;

        private long lastSentFrameId = -1;

        private void Update()
        {
            if (sendLatestMetadataOnUpdate)
            {
                SendLatestMetadata();
            }
        }

        [ContextMenu("Send Latest Metadata")]
        public bool SendLatestMetadata()
        {
            if (transmissionGate != null && !transmissionGate.Active)
            {
                return false;
            }

            if (mergedQueue == null || metadataSender == null)
            {
                return false;
            }

            MergedFrameSnapshotRecord[] records = mergedQueue.ExportSnapshot();
            if (records.Length == 0)
            {
                return false;
            }

            MergedFrameSnapshotRecord latest = records[records.Length - 1];
            if (!latest.isSendable)
            {
                return false;
            }

            if (latest.frameId == lastSentFrameId)
            {
                return false;
            }

            NetworkSendResult sendResult = metadataSender.SendDetailed(latest);
            if (!sendResult.Completed)
            {
                return false;
            }

            lastSentFrameId = latest.frameId;
            return true;
        }
    }
}
