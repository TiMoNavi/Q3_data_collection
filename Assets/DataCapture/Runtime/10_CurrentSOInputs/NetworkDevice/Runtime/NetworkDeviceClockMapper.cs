using UnityEngine;

namespace DataCapture.Synchronization
{
    public class NetworkDeviceClockMapper : MonoBehaviour
    {
        [SerializeField] private TimeStampVariable timestampVariable;
        [SerializeField] private long clockOffsetMs;
        [SerializeField] private bool updateOffsetFromSamples = true;
        [SerializeField, Range(0f, 1f)] private float offsetSmoothing = 0.1f;

        public long ClockOffsetMs => clockOffsetMs;

        public long MapDeviceTimestampToLocal(long deviceTimestamp)
        {
            return deviceTimestamp + clockOffsetMs;
        }

        public long GetReceiveTimestamp()
        {
            return SynchronizationClock.GetUnixMilliseconds(timestampVariable);
        }

        public long ObserveSample(long deviceTimestamp, long receiveTimestamp)
        {
            long observedOffset = receiveTimestamp - deviceTimestamp;
            if (!updateOffsetFromSamples)
            {
                return clockOffsetMs;
            }

            if (clockOffsetMs == 0)
            {
                clockOffsetMs = observedOffset;
            }
            else
            {
                clockOffsetMs = (long)Mathf.Lerp(clockOffsetMs, observedOffset, offsetSmoothing);
            }

            return clockOffsetMs;
        }

        public NetworkDeviceRecord CreateRecord(string sourceDeviceId, string payloadJson, long deviceTimestamp)
        {
            long receiveTimestamp = GetReceiveTimestamp();
            long offset = ObserveSample(deviceTimestamp, receiveTimestamp);

            return new NetworkDeviceRecord
            {
                sourceDeviceId = sourceDeviceId,
                dataPayloadJson = payloadJson,
                deviceTimestamp = deviceTimestamp,
                receiveTimestamp = receiveTimestamp,
                clockOffsetMs = offset,
                timestampUnixMs = deviceTimestamp > 0 ? deviceTimestamp + offset : receiveTimestamp
            };
        }
    }
}
