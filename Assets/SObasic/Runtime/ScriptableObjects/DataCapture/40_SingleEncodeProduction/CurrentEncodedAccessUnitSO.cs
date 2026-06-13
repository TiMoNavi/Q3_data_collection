using DataCapture.Synchronization;
using SObasic.CurrentQueueBridge;
using UnityEngine;

namespace DataCapture.Networking
{
    [CreateAssetMenu(fileName = "CurrentEncodedAccessUnitSO", menuName = "DataCapture/40 Single Encode Production/Current Encoded Access Unit")]
    public class CurrentEncodedAccessUnitSO : ScriptableObject, ICurrentRecordSource
    {
        [SerializeField] private bool isValid;
        [SerializeField] private EncodedAccessUnitRecord current;

        public EncodedAccessUnitRecord Current => current;
        public string SourceName => name;
        public System.Type RecordType => typeof(EncodedAccessUnitRecord);
        public bool IsRecordValid => isValid;
        public long CurrentTimestampUnixMs => current.Timestamp;
        public long RecordSequence => current.accessUnitId;

        public void SetRecord(EncodedAccessUnitRecord record)
        {
            current = record;
            isValid = record.accessUnitId >= 0 && record.Timestamp > 0;
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
