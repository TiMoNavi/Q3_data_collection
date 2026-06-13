using SObasic.CurrentQueueBridge;
using UnityEngine;

namespace DataCapture.Synchronization
{
    [CreateAssetMenu(fileName = "CurrentCameraImageSO", menuName = "DataCapture/10 Camera Capture/Current Camera Image")]
    public class CurrentCameraImageSO : ScriptableObject, ICurrentRecordSource
    {
        [Header("Current Texture Reference")]
        public Texture currentTexture;

        [Header("Identity")]
        public long frameId;
        public long timestampUnixMs;
        public string timestampUtc;
        public bool isValid;

        [Header("Image")]
        public Vector2Int resolution;
        public long encodedFrameId = -1;
        public string debugImagePath;

        public string SourceName => name;
        public System.Type RecordType => typeof(CameraImageFrameRecord);
        public bool IsRecordValid => isValid;
        public long CurrentTimestampUnixMs => timestampUnixMs;
        public long RecordSequence => frameId;

        public void SetFrame(Texture texture, CameraImageFrameRecord record, string utcTimestamp)
        {
            currentTexture = texture;
            frameId = record.frameId;
            timestampUnixMs = record.timestampUnixMs;
            timestampUtc = utcTimestamp;
            resolution = record.resolution;
            encodedFrameId = record.encodedFrameId;
            debugImagePath = record.debugImagePath;
            isValid = texture != null && timestampUnixMs > 0;
        }

        public CameraImageFrameRecord ToRecord()
        {
            return new CameraImageFrameRecord
            {
                frameId = frameId,
                timestampUnixMs = timestampUnixMs,
                texture = currentTexture,
                resolution = resolution,
                encodedFrameId = encodedFrameId,
                debugImagePath = debugImagePath
            };
        }

        public void Clear()
        {
            currentTexture = null;
            frameId = 0;
            timestampUnixMs = 0;
            timestampUtc = string.Empty;
            resolution = default;
            encodedFrameId = -1;
            debugImagePath = string.Empty;
            isValid = false;
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
