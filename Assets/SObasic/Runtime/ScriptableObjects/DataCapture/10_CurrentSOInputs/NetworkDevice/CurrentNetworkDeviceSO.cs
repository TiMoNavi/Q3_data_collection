using UnityEngine;
using SObasic.CurrentQueueBridge;

namespace DataCapture.Synchronization
{
    [CreateAssetMenu(fileName = "CurrentNetworkDeviceSO", menuName = "DataCapture/30 Pose Metadata Capture/Network Device/Current Network Device")]
    public class CurrentNetworkDeviceSO : ScriptableObject, ICurrentRecordSource
    {
        public bool isValid;
        public string sourceDeviceId;
        [TextArea(3, 12)] public string dataPayloadJson;
        public long deviceTimestamp;
        public long receiveTimestamp;
        public long clockOffsetMs;
        public long timestampUnixMs;

        public string SourceName => name;
        public System.Type RecordType => typeof(NetworkDeviceRecord);
        public bool IsRecordValid => isValid;
        public long CurrentTimestampUnixMs => timestampUnixMs;
        public long RecordSequence => timestampUnixMs;

        public void SetData(NetworkDeviceRecord record)
        {
            sourceDeviceId = record.sourceDeviceId;
            dataPayloadJson = record.dataPayloadJson;
            deviceTimestamp = record.deviceTimestamp;
            receiveTimestamp = record.receiveTimestamp;
            clockOffsetMs = record.clockOffsetMs;
            timestampUnixMs = record.timestampUnixMs;
            isValid = timestampUnixMs > 0;
        }

        public NetworkDeviceRecord ToRecord()
        {
            return new NetworkDeviceRecord
            {
                sourceDeviceId = sourceDeviceId,
                dataPayloadJson = dataPayloadJson,
                deviceTimestamp = deviceTimestamp,
                receiveTimestamp = receiveTimestamp,
                clockOffsetMs = clockOffsetMs,
                timestampUnixMs = timestampUnixMs
            };
        }

        public bool TryGetRecord(out ITimestampedData record)
        {
            if (!isValid)
            {
                record = null;
                return false;
            }

            record = ToRecord();
            return true;
        }
    }
}
