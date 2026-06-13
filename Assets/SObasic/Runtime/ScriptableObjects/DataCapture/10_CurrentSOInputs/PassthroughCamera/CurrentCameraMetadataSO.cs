using SObasic.CurrentQueueBridge;
using UnityEngine;

namespace DataCapture.Synchronization
{
    [CreateAssetMenu(fileName = "CurrentCameraMetadataSO", menuName = "DataCapture/10 Camera Capture/Current Camera Metadata")]
    public class CurrentCameraMetadataSO : ScriptableObject, ICurrentRecordSource
    {
        public long frameId;
        public long timestampUnixMs;
        public bool isValid;
        public Vector2Int currentResolution;
        public Vector2 focalLength;
        public Vector2 principalPoint;
        public Vector2Int sensorResolution;
        public Pose lensOffset;
        public Matrix4x4 projectionMatrix;
        public Matrix4x4 cameraLocalToWorldMatrix;
        public Matrix4x4 cameraWorldToLocalMatrix;
        public bool hasDistortionData;
        public Vector4 distortionCoefficients;
        public string metadataSource;

        public string SourceName => name;
        public System.Type RecordType => typeof(CameraMetadataRecord);
        public bool IsRecordValid => isValid;
        public long CurrentTimestampUnixMs => timestampUnixMs;
        public long RecordSequence => frameId;

        public void SetMetadata(CameraMetadataRecord record)
        {
            frameId = record.frameId;
            timestampUnixMs = record.timestampUnixMs;
            currentResolution = record.currentResolution;
            focalLength = record.focalLength;
            principalPoint = record.principalPoint;
            sensorResolution = record.sensorResolution;
            lensOffset = record.lensOffset;
            projectionMatrix = record.projectionMatrix;
            cameraLocalToWorldMatrix = record.cameraLocalToWorldMatrix;
            cameraWorldToLocalMatrix = record.cameraWorldToLocalMatrix;
            hasDistortionData = record.hasDistortionData;
            distortionCoefficients = record.distortionCoefficients;
            metadataSource = record.metadataSource;
            isValid = frameId > 0 && timestampUnixMs > 0 && currentResolution.x > 0 && currentResolution.y > 0;
        }

        public CameraMetadataRecord ToRecord()
        {
            return new CameraMetadataRecord
            {
                frameId = frameId,
                timestampUnixMs = timestampUnixMs,
                currentResolution = currentResolution,
                focalLength = focalLength,
                principalPoint = principalPoint,
                sensorResolution = sensorResolution,
                lensOffset = lensOffset,
                projectionMatrix = projectionMatrix,
                cameraLocalToWorldMatrix = cameraLocalToWorldMatrix,
                cameraWorldToLocalMatrix = cameraWorldToLocalMatrix,
                hasDistortionData = hasDistortionData,
                distortionCoefficients = distortionCoefficients,
                metadataSource = metadataSource
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
