using UnityEngine;

namespace DataCapture.Synchronization
{
    [System.Serializable]
    public struct VirtualLayerFrameRecord : ITimestampedData
    {
        public long frameId;
        public long timestampUnixMs;
        public Texture texture;
        public Vector2Int resolution;
        public Vector3 cameraPosition;
        public Quaternion cameraRotation;
        public Matrix4x4 projectionMatrix;
        public Matrix4x4 cameraLocalToWorldMatrix;
        public string debugImagePath;

        public long Timestamp => timestampUnixMs;
    }
}
