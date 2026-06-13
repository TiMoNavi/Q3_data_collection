using DataCapture.Synchronization;
using UnityEngine;

namespace DataCapture.Networking
{
    [System.Serializable]
    public struct ResolvedVideoEncodingParameters
    {
        public bool isValid;
        public string invalidReason;
        public string codec;
        public int width;
        public int height;
        public int frameRate;
        public int bitrateKbps;
        public int qualityPercent;
        public float keyFrameIntervalSeconds;
        public bool forceKeyFrameOnStart;
        public string resolutionSource;
        public string frameRateSource;
        public string bitrateSource;
    }

    public class VideoEncodingParameterResolver : MonoBehaviour
    {
        [SerializeField] private CurrentCameraStreamStateSO currentStreamState;
        [SerializeField] private CurrentCameraImageSO currentCameraImage;
        [SerializeField] private EncoderConfigurationSO encoderConfiguration;
        [SerializeField] private EncodingPipelineConfigurationSO pipelineConfiguration;
        [SerializeField] private bool resolveOnUpdate = true;

        [Header("Resolved")]
        [SerializeField] private ResolvedVideoEncodingParameters current;

        public ResolvedVideoEncodingParameters Current => current;

        private void Update()
        {
            if (resolveOnUpdate)
            {
                TryResolve(out current);
            }
        }

        [ContextMenu("Resolve Video Encoding Parameters")]
        public bool ResolveNow()
        {
            return TryResolve(out current);
        }

        public bool TryResolve(out ResolvedVideoEncodingParameters resolved)
        {
            resolved = Resolve(
                currentStreamState,
                currentCameraImage,
                encoderConfiguration,
                pipelineConfiguration);
            return resolved.isValid;
        }

        public static ResolvedVideoEncodingParameters Resolve(
            CurrentCameraStreamStateSO streamState,
            CurrentCameraImageSO image,
            EncoderConfigurationSO configuration,
            EncodingPipelineConfigurationSO pipeline)
        {
            int targetWidth = configuration != null ? configuration.targetWidth : 1280;
            int targetHeight = configuration != null ? configuration.targetHeight : 1280;
            int targetFrameRate = configuration != null ? configuration.targetFrameRate : 30;
            float keyFrameIntervalSeconds = configuration != null ? configuration.keyFrameIntervalSeconds : 1f;
            bool forceKeyFrameOnStart = configuration == null || configuration.forceKeyFrameOnStart;

            var resolved = new ResolvedVideoEncodingParameters
            {
                codec = ResolveCodec(configuration, pipeline),
                width = Mathf.Max(16, targetWidth),
                height = Mathf.Max(16, targetHeight),
                frameRate = Mathf.Clamp(targetFrameRate, 1, 120),
                bitrateKbps = 0,
                qualityPercent = Mathf.Clamp(configuration != null ? configuration.qualityPercent : 80, 1, 100),
                keyFrameIntervalSeconds = Mathf.Max(0.1f, keyFrameIntervalSeconds),
                forceKeyFrameOnStart = forceKeyFrameOnStart,
                resolutionSource = "EncoderConfiguration.target",
                frameRateSource = "EncoderConfiguration.target",
                bitrateSource = "EncoderConfiguration.targetBitrateKbps"
            };

            bool useCameraResolution = configuration == null ||
                configuration.resolutionSource == VideoEncodingResolutionSource.CameraStreamState;
            if (useCameraResolution && TryGetCameraResolution(streamState, image, out Vector2Int cameraResolution, out string resolutionSource))
            {
                resolved.width = cameraResolution.x;
                resolved.height = cameraResolution.y;
                resolved.resolutionSource = resolutionSource;
            }

            ApplyDownscale(configuration, ref resolved.width, ref resolved.height);
            resolved.width = MakeEncoderDimension(resolved.width);
            resolved.height = MakeEncoderDimension(resolved.height);

            bool useCameraFrameRate = configuration == null ||
                configuration.frameRateSource == VideoEncodingFrameRateSource.CameraStreamState;
            if (useCameraFrameRate && TryGetCameraFrameRate(streamState, out int cameraFrameRate, out string frameRateSource))
            {
                resolved.frameRate = cameraFrameRate;
                resolved.frameRateSource = frameRateSource;
            }

            resolved.bitrateKbps = ResolveBitrateKbps(configuration, resolved);
            resolved.bitrateSource = ResolveBitrateSource(configuration);

            resolved.isValid = resolved.width >= 16 &&
                resolved.height >= 16 &&
                resolved.frameRate > 0 &&
                resolved.bitrateKbps >= 128 &&
                !string.IsNullOrWhiteSpace(resolved.codec);
            resolved.invalidReason = resolved.isValid ? string.Empty : "Missing valid codec, resolution, frame rate, or bitrate.";
            return resolved;
        }

        private static string ResolveCodec(EncoderConfigurationSO configuration, EncodingPipelineConfigurationSO pipeline)
        {
            if (pipeline != null)
            {
                switch (pipeline.videoEncoderBackend)
                {
                    case VideoEncoderBackend.AndroidMediaCodecH264:
                        return "H264";
                    case VideoEncoderBackend.AndroidMediaCodecH265:
                        return "H265";
                    case VideoEncoderBackend.WebRtc:
                        return "WebRTC";
                    case VideoEncoderBackend.DebugJpeg:
                        return "DEBUG_JPEG";
                }
            }

            return configuration != null && !string.IsNullOrWhiteSpace(configuration.codec)
                ? configuration.codec
                : "H264";
        }

        private static bool TryGetCameraResolution(
            CurrentCameraStreamStateSO streamState,
            CurrentCameraImageSO image,
            out Vector2Int resolution,
            out string source)
        {
            if (streamState != null && streamState.isValid && IsValidResolution(streamState.currentResolution))
            {
                resolution = streamState.currentResolution;
                source = "CurrentCameraStreamState.currentResolution";
                return true;
            }

            if (image != null && image.isValid && IsValidResolution(image.resolution))
            {
                resolution = image.resolution;
                source = "CurrentCameraImage.resolution";
                return true;
            }

            resolution = default;
            source = string.Empty;
            return false;
        }

        private static bool TryGetCameraFrameRate(
            CurrentCameraStreamStateSO streamState,
            out int frameRate,
            out string source)
        {
            if (streamState != null && streamState.isValid && streamState.requestedMaxFramerate > 0)
            {
                frameRate = Mathf.Clamp(streamState.requestedMaxFramerate, 1, 120);
                source = "CurrentCameraStreamState.requestedMaxFramerate";
                return true;
            }

            if (streamState != null && streamState.isValid && streamState.measuredFramerate > 0.5f)
            {
                frameRate = Mathf.Clamp(Mathf.RoundToInt(streamState.measuredFramerate), 1, 120);
                source = "CurrentCameraStreamState.measuredFramerate";
                return true;
            }

            frameRate = 0;
            source = string.Empty;
            return false;
        }

        private static void ApplyDownscale(EncoderConfigurationSO configuration, ref int width, ref int height)
        {
            if (configuration == null || !configuration.allowDownscale)
            {
                return;
            }

            int maxWidth = Mathf.Max(16, configuration.maxOutputWidth);
            int maxHeight = Mathf.Max(16, configuration.maxOutputHeight);
            if (width <= maxWidth && height <= maxHeight)
            {
                return;
            }

            float scale = Mathf.Min(maxWidth / (float)width, maxHeight / (float)height);
            width = Mathf.Max(16, Mathf.RoundToInt(width * scale));
            height = Mathf.Max(16, Mathf.RoundToInt(height * scale));
        }

        private static int ResolveBitrateKbps(EncoderConfigurationSO configuration, ResolvedVideoEncodingParameters resolved)
        {
            if (configuration == null ||
                configuration.bitrateSource == VideoEncodingBitrateSource.ManualOverride)
            {
                return Mathf.Max(128, configuration != null ? configuration.targetBitrateKbps : 8000);
            }

            int minKbps = Mathf.Max(128, configuration.minAutoBitrateKbps);
            int maxKbps = Mathf.Max(minKbps, configuration.maxAutoBitrateKbps);
            float quality01 = Mathf.InverseLerp(1f, 100f, Mathf.Clamp(resolved.qualityPercent, 1, 100));
            float bitsPerPixelFrame = Mathf.Lerp(0.03f, 0.20f, quality01);

            if (IsHevc(resolved.codec))
            {
                bitsPerPixelFrame *= 0.70f;
            }

            float estimatedKbps = resolved.width * resolved.height * resolved.frameRate * bitsPerPixelFrame / 1000f;
            return Mathf.Clamp(Mathf.RoundToInt(estimatedKbps), minKbps, maxKbps);
        }

        private static string ResolveBitrateSource(EncoderConfigurationSO configuration)
        {
            if (configuration == null ||
                configuration.bitrateSource == VideoEncodingBitrateSource.ManualOverride)
            {
                return "EncoderConfiguration.targetBitrateKbps";
            }

            return "EncoderConfiguration.qualityPercent";
        }

        private static bool IsHevc(string codec)
        {
            return string.Equals(codec, "H265", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(codec, "HEVC", System.StringComparison.OrdinalIgnoreCase);
        }

        private static int MakeEncoderDimension(int value)
        {
            value = Mathf.Max(16, value);
            return value % 2 == 0 ? value : value - 1;
        }

        private static bool IsValidResolution(Vector2Int resolution)
        {
            return resolution.x >= 16 && resolution.y >= 16;
        }
    }
}
