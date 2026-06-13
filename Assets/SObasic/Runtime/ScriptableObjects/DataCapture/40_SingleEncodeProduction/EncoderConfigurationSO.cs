using UnityEngine;

namespace DataCapture.Networking
{
    public enum VideoEncodingResolutionSource
    {
        CameraStreamState,
        ManualOverride
    }

    public enum VideoEncodingFrameRateSource
    {
        CameraStreamState,
        ManualOverride
    }

    public enum VideoEncodingBitrateSource
    {
        ManualOverride,
        QualityPercent
    }

    [CreateAssetMenu(fileName = "EncoderConfigurationSO", menuName = "DataCapture/50 Encoding Network/Encoder Configuration")]
    public class EncoderConfigurationSO : ScriptableObject
    {
        [Header("Codec")]
        public string codec = "H264";

        [Header("Target")]
        public int targetWidth = 1280;
        public int targetHeight = 1280;
        public int targetFrameRate = 30;
        public int targetBitrateKbps = 8000;
        [Range(1, 100)] public int qualityPercent = 80;
        public VideoEncodingBitrateSource bitrateSource = VideoEncodingBitrateSource.ManualOverride;
        public int minAutoBitrateKbps = 512;
        public int maxAutoBitrateKbps = 30000;
        public float keyFrameIntervalSeconds = 1f;
        public bool forceKeyFrameOnStart = true;

        [Header("Source Binding")]
        public VideoEncodingResolutionSource resolutionSource = VideoEncodingResolutionSource.CameraStreamState;
        public VideoEncodingFrameRateSource frameRateSource = VideoEncodingFrameRateSource.CameraStreamState;
        public bool allowDownscale = true;
        public int maxOutputWidth = 1280;
        public int maxOutputHeight = 1280;

        [Header("Diagnostics")]
        public bool debugSavePacketsToDisk;

        private void OnValidate()
        {
            targetWidth = Mathf.Max(16, targetWidth);
            targetHeight = Mathf.Max(16, targetHeight);
            targetFrameRate = Mathf.Clamp(targetFrameRate, 1, 120);
            targetBitrateKbps = Mathf.Max(128, targetBitrateKbps);
            qualityPercent = Mathf.Clamp(qualityPercent, 1, 100);
            minAutoBitrateKbps = Mathf.Max(128, minAutoBitrateKbps);
            maxAutoBitrateKbps = Mathf.Max(minAutoBitrateKbps, maxAutoBitrateKbps);
            keyFrameIntervalSeconds = Mathf.Max(0.1f, keyFrameIntervalSeconds);
            maxOutputWidth = Mathf.Max(16, maxOutputWidth);
            maxOutputHeight = Mathf.Max(16, maxOutputHeight);
        }
    }
}
