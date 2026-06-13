using UnityEngine;
using SObasic.CurrentQueueBridge;

namespace DataCapture.Synchronization
{
    [CreateAssetMenu(fileName = "CurrentVirtualLayerFrameSO", menuName = "DataCapture/20 Virtual Layer Capture/Current Virtual Layer Frame")]
    public class CurrentVirtualLayerFrameSO : ScriptableObject, ICurrentRecordSource
    {
        public RenderTexture currentTexture;
        public long frameId;
        public long timestampUnixMs;
        public bool isValid;
        public Vector2Int resolution;
        public Vector3 cameraPosition;
        public Quaternion cameraRotation;
        public Matrix4x4 projectionMatrix;
        public Matrix4x4 cameraLocalToWorldMatrix;
        public string debugImagePath;

        public string SourceName => name;
        public System.Type RecordType => typeof(VirtualLayerFrameRecord);
        public bool IsRecordValid => isValid;
        public long CurrentTimestampUnixMs => timestampUnixMs;
        public long RecordSequence => frameId;

        public void SetFrame(RenderTexture texture, VirtualLayerFrameRecord record)
        {
            currentTexture = texture;
            frameId = record.frameId;
            timestampUnixMs = record.timestampUnixMs;
            resolution = record.resolution;
            cameraPosition = record.cameraPosition;
            cameraRotation = record.cameraRotation;
            projectionMatrix = record.projectionMatrix;
            cameraLocalToWorldMatrix = record.cameraLocalToWorldMatrix;
            debugImagePath = record.debugImagePath;
            isValid = texture != null && timestampUnixMs > 0;
        }

        public VirtualLayerFrameRecord ToRecord()
        {
            return new VirtualLayerFrameRecord
            {
                frameId = frameId,
                timestampUnixMs = timestampUnixMs,
                texture = currentTexture,
                resolution = resolution,
                cameraPosition = cameraPosition,
                cameraRotation = cameraRotation,
                projectionMatrix = projectionMatrix,
                cameraLocalToWorldMatrix = cameraLocalToWorldMatrix,
                debugImagePath = debugImagePath
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
