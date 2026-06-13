using DataCapture.Synchronization;
using SObasic.CurrentQueueBridge;
using UnityEngine;

namespace DataCapture.Networking
{
    [CreateAssetMenu(fileName = "CurrentCaptureOutputSO", menuName = "DataCapture/50 Encoding Network/Current Capture Output")]
    public class CurrentCaptureOutputSO : ScriptableObject, ICurrentRecordSource
    {
        [SerializeField] private bool isValid;
        [SerializeField] private CaptureOutputRecord current;

        public CaptureOutputRecord Current => current;
        public string SourceName => name;
        public System.Type RecordType => typeof(CaptureOutputRecord);
        public bool IsRecordValid => isValid;
        public long CurrentTimestampUnixMs => current.timestampUnixMs;
        public long RecordSequence => current.outputId;

        public void SetRecord(CaptureOutputRecord record)
        {
            current = record;
            isValid = record.outputId >= 0 &&
                record.timestampUnixMs > 0 &&
                record.status == CaptureOutputStatus.Ready;
        }

        public void Clear()
        {
            current = default;
            isValid = false;
        }

        public bool TryGetRecord(out ITimestampedData record)
        {
            if (!isValid)
            {
                record = null;
                return false;
            }

            record = current;
            return true;
        }
    }
}
