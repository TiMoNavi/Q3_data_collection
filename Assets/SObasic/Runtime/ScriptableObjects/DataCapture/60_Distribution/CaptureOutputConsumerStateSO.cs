using UnityEngine;

namespace DataCapture.Networking
{
    [CreateAssetMenu(fileName = "CaptureOutputConsumerStateSO", menuName = "DataCapture/50 Encoding Network/Capture Output Consumer State")]
    public class CaptureOutputConsumerStateSO : ScriptableObject
    {
        [Header("Consumer Cursor")]
        public string consumerName;
        public long lastConsumedOutputId = -1;
        public long lastConsumedTimestampUnixMs;
        public CaptureOutputKind acceptedOutputKind = CaptureOutputKind.FramePacket;
        public CaptureDeliveryKind acceptedDeliveryKind = CaptureDeliveryKind.Stream;

        [Header("Runtime Status")]
        public bool isActive = true;
        public bool hasFailure;
        public string lastStatus;
        public string lastFailureReason;
        public long lastStatusUnixMs;
        public int consumedCount;
        public int skippedCount;
        public int failedCount;

        public bool ShouldConsume(CaptureOutputRecord record)
        {
            return isActive &&
                !hasFailure &&
                record.status == CaptureOutputStatus.Ready &&
                record.outputId > lastConsumedOutputId &&
                record.outputKind == acceptedOutputKind &&
                record.deliveryKind == acceptedDeliveryKind;
        }

        public void MarkConsumed(CaptureOutputRecord record, string status)
        {
            lastConsumedOutputId = record.outputId;
            lastConsumedTimestampUnixMs = record.timestampUnixMs;
            consumedCount++;
            SetStatus(status, false);
        }

        public void MarkSkipped(string reason)
        {
            skippedCount++;
            SetStatus(reason, false);
        }

        public void MarkFailed(string reason)
        {
            failedCount++;
            hasFailure = true;
            lastFailureReason = string.IsNullOrWhiteSpace(reason)
                ? "Capture output consumer failed."
                : reason;
            SetStatus(lastFailureReason, true);
        }

        public void ClearFailure()
        {
            hasFailure = false;
            lastFailureReason = string.Empty;
            SetStatus("Failure cleared.", false);
        }

        public void ResetCursor()
        {
            lastConsumedOutputId = -1;
            lastConsumedTimestampUnixMs = 0;
            consumedCount = 0;
            skippedCount = 0;
            failedCount = 0;
            ClearFailure();
        }

        private void SetStatus(string status, bool failed)
        {
            lastStatus = status ?? string.Empty;
            lastStatusUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (failed)
            {
                Debug.LogWarning(lastStatus, this);
            }
        }
    }
}
