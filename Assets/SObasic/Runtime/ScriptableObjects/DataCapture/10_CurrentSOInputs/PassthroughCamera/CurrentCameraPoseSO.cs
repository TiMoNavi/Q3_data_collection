using SObasic.CurrentQueueBridge;
using UnityEngine;

namespace DataCapture.Synchronization
{
    [CreateAssetMenu(fileName = "CurrentCameraPoseSO", menuName = "DataCapture/10 Camera Capture/Current Camera Pose")]
    public class CurrentCameraPoseSO : ScriptableObject, ICurrentRecordSource
    {
        public long frameId;
        public long timestampUnixMs;
        public bool isValid;
        public Vector3 cameraPosition;
        public Quaternion cameraRotation;
        public Matrix4x4 cameraLocalToWorldMatrix;
        public Matrix4x4 cameraWorldToLocalMatrix;

        public string SourceName => name;
        public System.Type RecordType => typeof(CameraPoseRecord);
        public bool IsRecordValid => isValid;
        public long CurrentTimestampUnixMs => timestampUnixMs;
        public long RecordSequence => frameId;

        public void SetPose(CameraPoseRecord record)
        {
            frameId = record.frameId;
            timestampUnixMs = record.timestampUnixMs;
            cameraPosition = record.cameraPosition;
            cameraRotation = record.cameraRotation;
            cameraLocalToWorldMatrix = record.cameraLocalToWorldMatrix;
            cameraWorldToLocalMatrix = record.cameraWorldToLocalMatrix;
            isValid = frameId > 0 && timestampUnixMs > 0;
        }

        public CameraPoseRecord ToRecord()
        {
            return new CameraPoseRecord
            {
                frameId = frameId,
                timestampUnixMs = timestampUnixMs,
                cameraPosition = cameraPosition,
                cameraRotation = cameraRotation,
                cameraLocalToWorldMatrix = cameraLocalToWorldMatrix,
                cameraWorldToLocalMatrix = cameraWorldToLocalMatrix
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
