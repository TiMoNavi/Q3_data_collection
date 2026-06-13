using UnityEngine;

namespace DataCapture.Synchronization
{
    public enum PassthroughCameraEye
    {
        Unknown,
        Left,
        Right
    }

    [System.Serializable]
    public struct CameraImageFrameRecord : ITimestampedData
    {
        public long frameId;
        public long timestampUnixMs;
        public Texture texture;
        public Vector2Int resolution;
        public long encodedFrameId;
        public string debugImagePath;

        public long Timestamp => timestampUnixMs;
    }

    [System.Serializable]
    public struct CameraFrameTimingRecord : ITimestampedData
    {
        public long frameId;
        public long timestampUnixMs;
        public string timestampUtc;
        public int unityFrame;
        public bool isUpdatedThisFrame;

        public long Timestamp => timestampUnixMs;
    }

    [System.Serializable]
    public struct CameraPoseRecord : ITimestampedData
    {
        public long frameId;
        public long timestampUnixMs;
        public Vector3 cameraPosition;
        public Quaternion cameraRotation;
        public Matrix4x4 cameraLocalToWorldMatrix;
        public Matrix4x4 cameraWorldToLocalMatrix;

        public long Timestamp => timestampUnixMs;
    }

    [System.Serializable]
    public struct CameraMetadataRecord : ITimestampedData
    {
        public long frameId;
        public long timestampUnixMs;
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

        public long Timestamp => timestampUnixMs;
    }

    [System.Serializable]
    public struct CameraStreamStateRecord : ITimestampedData
    {
        public long frameId;
        public long timestampUnixMs;
        public PassthroughCameraEye cameraEye;
        public Vector2Int requestedResolution;
        public Vector2Int currentResolution;
        public int requestedMaxFramerate;
        public float measuredFramerate;
        public bool isPlaying;
        public bool isUpdatedThisFrame;
        public bool isSupported;
        public string texturePropertyName;

        public long Timestamp => timestampUnixMs;
    }
}
