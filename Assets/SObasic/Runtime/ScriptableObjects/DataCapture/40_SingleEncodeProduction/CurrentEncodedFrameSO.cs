using UnityEngine;
using SObasic.CurrentQueueBridge;

namespace DataCapture.Synchronization
{
    [CreateAssetMenu(fileName = "CurrentEncodedFrameSO", menuName = "DataCapture/50 Encoding Network/Current Encoded Frame")]
    public class CurrentEncodedFrameSO : ScriptableObject, ICurrentRecordSource
    {
        public bool isValid;
        public long encodedFrameId;
        public long sourceCameraFrameId;
        public long timestampUnixMs;
        public bool isKeyFrame;
        public int width;
        public int height;
        public string codec;
        public int byteLength;
        public string debugFilePath;

        public string SourceName => name;
        public System.Type RecordType => typeof(EncodedFrameRecord);
        public bool IsRecordValid => isValid;
        public long CurrentTimestampUnixMs => timestampUnixMs;
        public long RecordSequence => encodedFrameId;

        public void SetFrame(EncodedFrameRecord record)
        {
            encodedFrameId = record.encodedFrameId;
            sourceCameraFrameId = record.sourceCameraFrameId;
            timestampUnixMs = record.timestampUnixMs;
            isKeyFrame = record.isKeyFrame;
            width = record.width;
            height = record.height;
            codec = record.codec;
            byteLength = record.byteLength;
            debugFilePath = record.debugFilePath;
            isValid = timestampUnixMs > 0 && encodedFrameId >= 0;
        }

        public EncodedFrameRecord ToRecord()
        {
            return new EncodedFrameRecord
            {
                encodedFrameId = encodedFrameId,
                sourceCameraFrameId = sourceCameraFrameId,
                timestampUnixMs = timestampUnixMs,
                isKeyFrame = isKeyFrame,
                width = width,
                height = height,
                codec = codec,
                byteLength = byteLength,
                debugFilePath = debugFilePath
            };
        }

        public void Clear()
        {
            isValid = false;
            encodedFrameId = -1;
            sourceCameraFrameId = -1;
            timestampUnixMs = 0;
            isKeyFrame = false;
            width = 0;
            height = 0;
            codec = string.Empty;
            byteLength = 0;
            debugFilePath = string.Empty;
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
