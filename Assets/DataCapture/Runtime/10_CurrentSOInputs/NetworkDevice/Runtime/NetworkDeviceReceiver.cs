using UnityEngine;

namespace DataCapture.Synchronization
{
    public class NetworkDeviceReceiver : MonoBehaviour
    {
        [SerializeField] private string defaultDeviceId = "external-device";
        [SerializeField] private NetworkDeviceClockMapper clockMapper;
        [SerializeField] private NetworkDeviceCurrentWriter currentWriter;

        public bool ReceiveJson(string payloadJson)
        {
            return ReceiveJson(defaultDeviceId, payloadJson, 0);
        }

        public bool ReceiveJson(string sourceDeviceId, string payloadJson, long deviceTimestamp)
        {
            if (currentWriter == null)
            {
                return false;
            }

            NetworkDeviceRecord record = clockMapper != null
                ? clockMapper.CreateRecord(sourceDeviceId, payloadJson, deviceTimestamp)
                : CreateRecordWithoutMapper(sourceDeviceId, payloadJson, deviceTimestamp);

            return currentWriter.Write(record);
        }

        private NetworkDeviceRecord CreateRecordWithoutMapper(string sourceDeviceId, string payloadJson, long deviceTimestamp)
        {
            long receiveTimestamp = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            return new NetworkDeviceRecord
            {
                sourceDeviceId = string.IsNullOrEmpty(sourceDeviceId) ? defaultDeviceId : sourceDeviceId,
                dataPayloadJson = payloadJson,
                deviceTimestamp = deviceTimestamp,
                receiveTimestamp = receiveTimestamp,
                clockOffsetMs = deviceTimestamp > 0 ? receiveTimestamp - deviceTimestamp : 0,
                timestampUnixMs = deviceTimestamp > 0 ? receiveTimestamp : receiveTimestamp
            };
        }
    }
}
