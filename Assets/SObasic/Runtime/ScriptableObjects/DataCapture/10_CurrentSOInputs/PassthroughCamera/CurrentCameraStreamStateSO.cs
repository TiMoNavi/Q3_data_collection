using SObasic.CurrentQueueBridge;
using UnityEngine;

namespace DataCapture.Synchronization
{
    [CreateAssetMenu(fileName = "CurrentCameraStreamStateSO", menuName = "DataCapture/10 Camera Capture/Current Camera Stream State")]
    public class CurrentCameraStreamStateSO : ScriptableObject, ICurrentRecordSource
    {
        public long frameId;
        public long timestampUnixMs;
        public bool isValid;
        public PassthroughCameraEye cameraEye;
        public Vector2Int requestedResolution;
        public Vector2Int currentResolution;
        public int requestedMaxFramerate;
        public float measuredFramerate;
        public bool isPlaying;
        public bool isUpdatedThisFrame;
        public bool isSupported;
        public string texturePropertyName;

        public string SourceName => name;
        public System.Type RecordType => typeof(CameraStreamStateRecord);
        public bool IsRecordValid => isValid;
        public long CurrentTimestampUnixMs => timestampUnixMs;
        public long RecordSequence => frameId;

        public void SetState(CameraStreamStateRecord record)
        {
            frameId = record.frameId;
            timestampUnixMs = record.timestampUnixMs;
            cameraEye = record.cameraEye;
            requestedResolution = record.requestedResolution;
            currentResolution = record.currentResolution;
            requestedMaxFramerate = record.requestedMaxFramerate;
            measuredFramerate = record.measuredFramerate;
            isPlaying = record.isPlaying;
            isUpdatedThisFrame = record.isUpdatedThisFrame;
            isSupported = record.isSupported;
            texturePropertyName = record.texturePropertyName;
            isValid = frameId > 0 && timestampUnixMs > 0;
        }

        public CameraStreamStateRecord ToRecord()
        {
            return new CameraStreamStateRecord
            {
                frameId = frameId,
                timestampUnixMs = timestampUnixMs,
                cameraEye = cameraEye,
                requestedResolution = requestedResolution,
                currentResolution = currentResolution,
                requestedMaxFramerate = requestedMaxFramerate,
                measuredFramerate = measuredFramerate,
                isPlaying = isPlaying,
                isUpdatedThisFrame = isUpdatedThisFrame,
                isSupported = isSupported,
                texturePropertyName = texturePropertyName
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
