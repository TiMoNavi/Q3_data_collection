using DataCapture.Synchronization;
using UnityEngine;

#pragma warning disable 0414 // Android-only test-pattern flag is intentionally serialized for device builds.
namespace DataCapture.Networking
{
    public class AndroidMediaCodecEncoderAdapter : MonoBehaviour, IVideoEncoderAdapter
    {
        [SerializeField] private CurrentCameraStreamStateSO currentStreamState;
        [SerializeField] private CurrentCameraImageSO currentCameraImage;
        [SerializeField] private EncoderConfigurationSO encoderConfiguration;
        [SerializeField] private EncodingPipelineConfigurationSO pipelineConfiguration;
        [SerializeField] private bool encodeGpuTestPatternUntilTextureBridgeReady = true;

        [Header("Runtime Diagnostics")]
        [SerializeField] private ResolvedVideoEncodingParameters resolvedParameters;
        [SerializeField] private bool encoderStarted;
        [SerializeField] private string encoderName = string.Empty;
        [SerializeField] private string lastStatus = "Not started.";
        [SerializeField] private int encodedFrameCount;
        [SerializeField] private int emptyOutputCount;
        [SerializeField] private int restartCount;

        private const string JavaClassName = "com.q3datacapture.mediacodec.Q3SurfaceVideoEncoder";

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject encoder;
#endif
        private long nextEncodedFrameId;
        private long firstSourceTimestampUnixMs;
        private string activeCodec = string.Empty;
        private int activeWidth;
        private int activeHeight;
        private int activeFrameRate;
        private int activeBitrateKbps;
        private float activeKeyFrameIntervalSeconds;
        private bool forceNextKeyFrame = true;

        public string CodecName => string.IsNullOrWhiteSpace(activeCodec) ? "H264" : activeCodec;

        public bool IsReady
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return encodeGpuTestPatternUntilTextureBridgeReady;
#else
                return false;
#endif
            }
        }

        private void OnDisable()
        {
            StopEncoder();
        }

        public bool TryEncode(
            Texture sourceTexture,
            CameraImageFrameRecord sourceFrame,
            out EncodedFrameRecord encodedFrame,
            out byte[] payload)
        {
            encodedFrame = default;
            payload = null;

            if (!IsReady)
            {
                lastStatus = "Android MediaCodec is only available on device; GPU test-pattern mode is disabled.";
                return false;
            }

            if (sourceTexture == null)
            {
                lastStatus = "Source texture is not ready.";
                return false;
            }

            resolvedParameters = VideoEncodingParameterResolver.Resolve(
                currentStreamState,
                currentCameraImage,
                encoderConfiguration,
                pipelineConfiguration);
            if (!resolvedParameters.isValid)
            {
                lastStatus = resolvedParameters.invalidReason;
                return false;
            }

            if (!EnsureEncoderStarted(resolvedParameters))
            {
                return false;
            }

            long presentationTimeUs = ResolvePresentationTimeUs(sourceFrame.timestampUnixMs);
            bool requestKeyFrame = forceNextKeyFrame || ShouldRequestPeriodicKeyFrame();

#if UNITY_ANDROID && !UNITY_EDITOR
            payload = encoder.Call<byte[]>(
                "encodePatternFrame",
                presentationTimeUs,
                requestKeyFrame);
            encoderName = encoder.Call<string>("getCodecName") ?? string.Empty;
            string javaStatus = encoder.Call<string>("getLastStatus");
            if (!string.IsNullOrWhiteSpace(javaStatus))
            {
                lastStatus = javaStatus;
            }
#else
            payload = null;
#endif

            if (payload == null || payload.Length == 0)
            {
                emptyOutputCount++;
                return false;
            }

            encodedFrame = new EncodedFrameRecord
            {
                encodedFrameId = nextEncodedFrameId++,
                sourceCameraFrameId = sourceFrame.frameId,
                timestampUnixMs = sourceFrame.timestampUnixMs,
                isKeyFrame = requestKeyFrame,
                width = resolvedParameters.width,
                height = resolvedParameters.height,
                codec = resolvedParameters.codec,
                byteLength = payload.Length,
                debugFilePath = string.Empty
            };

            forceNextKeyFrame = false;
            encodedFrameCount++;
            lastStatus = "Encoded " + resolvedParameters.codec + " GPU surface frame " + sourceFrame.frameId + " bytes=" + payload.Length + ".";
            return true;
        }

        private bool EnsureEncoderStarted(ResolvedVideoEncodingParameters parameters)
        {
            bool needsRestart = !encoderStarted ||
                activeCodec != parameters.codec ||
                activeWidth != parameters.width ||
                activeHeight != parameters.height ||
                activeFrameRate != parameters.frameRate ||
                activeBitrateKbps != parameters.bitrateKbps ||
                !Mathf.Approximately(activeKeyFrameIntervalSeconds, parameters.keyFrameIntervalSeconds);

            if (!needsRestart)
            {
                return true;
            }

            StopEncoder();

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                encoder = new AndroidJavaObject(JavaClassName);
                bool started = encoder.Call<bool>(
                    "start",
                    parameters.codec,
                    parameters.width,
                    parameters.height,
                    parameters.frameRate,
                    parameters.bitrateKbps,
                    parameters.keyFrameIntervalSeconds);
                if (!started)
                {
                    lastStatus = encoder.Call<string>("getLastStatus") ?? "MediaCodec start failed.";
                    StopEncoder();
                    return false;
                }

                encoderName = encoder.Call<string>("getCodecName") ?? string.Empty;
            }
            catch (System.Exception exception)
            {
                lastStatus = "MediaCodec start exception: " + exception.Message;
                StopEncoder();
                return false;
            }
#endif

            activeCodec = parameters.codec;
            activeWidth = parameters.width;
            activeHeight = parameters.height;
            activeFrameRate = parameters.frameRate;
            activeBitrateKbps = parameters.bitrateKbps;
            activeKeyFrameIntervalSeconds = parameters.keyFrameIntervalSeconds;
            encoderStarted = true;
            forceNextKeyFrame = parameters.forceKeyFrameOnStart;
            firstSourceTimestampUnixMs = 0;
            restartCount++;
            lastStatus = "Started MediaCodec GPU surface encoder " + parameters.codec + " " + parameters.width + "x" + parameters.height + "@" + parameters.frameRate + ".";
            return true;
        }

        private void StopEncoder()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (encoder != null)
            {
                try
                {
                    encoder.Call("stop");
                }
                catch (System.Exception)
                {
                }

                encoder.Dispose();
                encoder = null;
            }
#endif
            encoderStarted = false;
        }

        private long ResolvePresentationTimeUs(long timestampUnixMs)
        {
            if (timestampUnixMs <= 0)
            {
                return encodedFrameCount * 1000000L / Mathf.Max(1, activeFrameRate);
            }

            if (firstSourceTimestampUnixMs <= 0)
            {
                firstSourceTimestampUnixMs = timestampUnixMs;
            }

            return System.Math.Max(0L, timestampUnixMs - firstSourceTimestampUnixMs) * 1000L;
        }

        private bool ShouldRequestPeriodicKeyFrame()
        {
            int intervalFrames = Mathf.Max(
                1,
                Mathf.RoundToInt(Mathf.Max(0.1f, activeKeyFrameIntervalSeconds) * Mathf.Max(1, activeFrameRate)));
            return encodedFrameCount > 0 && encodedFrameCount % intervalFrames == 0;
        }
    }
}
#pragma warning restore 0414
