using SObasic.CurrentQueueBridge;
using UnityEngine;

namespace DataCapture.Synchronization
{
    [CreateAssetMenu(fileName = "CurrentCameraFrameTimingSO", menuName = "DataCapture/10 Camera Capture/Current Camera Frame Timing")]
    public class CurrentCameraFrameTimingSO : ScriptableObject, ICurrentRecordSource
    {
        public long frameId;
        public long timestampUnixMs;
        public string timestampUtc;
        public int unityFrame;
        public bool isUpdatedThisFrame;
        public bool isValid;

        public string SourceName => name;
        public System.Type RecordType => typeof(CameraFrameTimingRecord);
        public bool IsRecordValid => isValid;
        public long CurrentTimestampUnixMs => timestampUnixMs;
        public long RecordSequence => frameId;

        public void SetTiming(CameraFrameTimingRecord record)
        {
            frameId = record.frameId;
            timestampUnixMs = record.timestampUnixMs;
            timestampUtc = record.timestampUtc;
            unityFrame = record.unityFrame;
            isUpdatedThisFrame = record.isUpdatedThisFrame;
            isValid = frameId > 0 && timestampUnixMs > 0;
        }

        public CameraFrameTimingRecord ToRecord()
        {
            return new CameraFrameTimingRecord
            {
                frameId = frameId,
                timestampUnixMs = timestampUnixMs,
                timestampUtc = timestampUtc,
                unityFrame = unityFrame,
                isUpdatedThisFrame = isUpdatedThisFrame
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
