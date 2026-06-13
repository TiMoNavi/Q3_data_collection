using UnityEngine;

namespace DataCapture.Synchronization
{
    /// <summary>
    /// ScriptableObject for synchronization configuration.
    /// Create instance: Assets/Create/DataCapture/SyncConfiguration
    /// </summary>
    [CreateAssetMenu(fileName = "SyncConfiguration", menuName = "DataCapture/00 Global/Sync Configuration")]
    public class SyncConfiguration : ScriptableObject
    {
        [Header("Buffer Settings")]
        [Tooltip("Ring buffer capacity for each data source")]
        public int bufferCapacity = 300;

        [Header("Synchronization Settings")]
        [Tooltip("Time tolerance for data matching (milliseconds)")]
        public long timeTolerance = 50;

        [Tooltip("A source is treated as offline if its newest timestamp is older than the camera target by this many milliseconds.")]
        public long sourceOfflineTimeoutMs = 1000;

        [Header("Data Source Sampling Rates (Hz)")]
        [Tooltip("Controller sampling rate (e.g., 120Hz)")]
        public float controllerSampleRate = 120f;

        [Tooltip("Camera frame rate (e.g., 60Hz)")]
        public float cameraFrameRate = 60f;

        [Tooltip("Network data expected rate (e.g., 30Hz, variable)")]
        public float networkDataRate = 30f;

        [Header("Output Settings")]
        [Tooltip("Output sampling rate for synchronized data (Hz)")]
        public float outputSampleRate = 30f;

        [Tooltip("Resample method: Nearest or Interpolate")]
        public ResampleMethod resampleMethod = ResampleMethod.Nearest;

        [Header("Data Source Enable/Disable")]
        [Tooltip("Enable controller data capture")]
        public bool captureControllers = true;

        [Tooltip("Enable passthrough camera capture")]
        public bool captureCameraFrames = true;

        [Header("Fallback Required Streams")]
        [Tooltip("Used only when no CompositeAlignmentConfigurationSO is assigned.")]
        public bool requireCameraImage = true;

        [Tooltip("Used only when no CompositeAlignmentConfigurationSO is assigned.")]
        public bool requireCameraPose = true;

        [Tooltip("Used only when no CompositeAlignmentConfigurationSO is assigned.")]
        public bool requireCameraMetadata = true;

        [Tooltip("Used only when no CompositeAlignmentConfigurationSO is assigned.")]
        public bool requireCameraStreamState = true;

        [Tooltip("Used only when no CompositeAlignmentConfigurationSO is assigned.")]
        public bool requireController = true;
    }

    public enum ResampleMethod
    {
        Nearest,
        Interpolate
    }
}
