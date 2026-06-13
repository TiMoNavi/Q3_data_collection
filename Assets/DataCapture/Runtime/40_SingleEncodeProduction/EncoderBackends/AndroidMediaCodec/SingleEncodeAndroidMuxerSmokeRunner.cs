using System;
using System.Collections;
using System.IO;
using UnityEngine;

#pragma warning disable 0414 // Several serialized smoke settings are consumed only in Android player builds.
namespace DataCapture.Networking
{
    public sealed class SingleEncodeAndroidMuxerSmokeRunner : MonoBehaviour
    {
        [Header("Run")]
        [SerializeField] private bool runOnStart;
        [SerializeField] private float startDelaySeconds = 1f;

        [Header("Synthetic Android Encoder")]
        [SerializeField] private string codec = "H264";
        [SerializeField] private int width = 320;
        [SerializeField] private int height = 320;
        [SerializeField] private int frameRate = 30;
        [SerializeField] private int frameCount = 60;
        [SerializeField] private int bitrateKbps = 2000;
        [SerializeField] private float keyFrameIntervalSeconds = 1f;

        [Header("Shared Input Timing")]
        [SerializeField] private bool useSharedVideoFrameInput;
        [SerializeField] private bool useSharedInputEncodingParameters;
        [SerializeField] private CurrentVideoFrameInputSO currentVideoFrameInput;
        [SerializeField] private float waitForSharedInputSeconds = 20f;

        [Header("Output Queue")]
        [SerializeField] private CurrentCaptureOutputSO currentOutput;
        [SerializeField] private CaptureOutputQueueSO outputQueue;
        [SerializeField] private string outputFolderName = "DataCapture/SingleEncodeAndroidMuxerSmoke";

        [Header("Result")]
        [SerializeField] private bool isRunning;
        [SerializeField] private bool lastRunSucceeded;
        [SerializeField] private string lastStatus;
        [SerializeField] private string encoderName;
        [SerializeField] private string mp4Path;
        [SerializeField] private string manifestPath;
        [SerializeField] private int publishedFramePacketCount;
        [SerializeField] private long accessUnitBytes;
        [SerializeField] private long mp4Bytes;
        [SerializeField] private long muxedSampleCount;
        [SerializeField] private long muxedBytes;
        [SerializeField] private int encodeCallCount;
        [SerializeField] private int zeroOutputFrameCount;
        [SerializeField] private string lastEncodeStatus;
        [SerializeField] private int observedInputFrameCount;
        [SerializeField] private long firstInputSourceCameraFrameId = -1;
        [SerializeField] private long lastInputSourceCameraFrameId = -1;
        [SerializeField] private string lastInputSourceKind;

        private const string JavaClassName = "com.q3datacapture.mediacodec.Q3SurfaceVideoEncoder";
        private long nextOutputId;
        private long firstInputTimestampUnixMs;

        private void Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (runOnStart)
            {
                StartCoroutine(RunAfterDelay());
            }
#endif
        }

        [ContextMenu("Run Android Single Encode Muxer Smoke")]
        public void RunFromContextMenu()
        {
            if (!isRunning)
            {
                StartCoroutine(RunSmokeTest());
            }
        }

        private IEnumerator RunAfterDelay()
        {
            if (startDelaySeconds > 0f)
            {
                yield return new WaitForSeconds(startDelaySeconds);
            }

            yield return RunSmokeTest();
        }

        private IEnumerator RunSmokeTest()
        {
            isRunning = true;
            lastRunSucceeded = false;
            lastStatus = "Starting Android single-encode muxer smoke.";
            encoderName = string.Empty;
            publishedFramePacketCount = 0;
            accessUnitBytes = 0;
            mp4Bytes = 0;
            muxedSampleCount = 0;
            muxedBytes = 0;
            encodeCallCount = 0;
            zeroOutputFrameCount = 0;
            lastEncodeStatus = string.Empty;
            observedInputFrameCount = 0;
            firstInputSourceCameraFrameId = -1;
            lastInputSourceCameraFrameId = -1;
            lastInputSourceKind = string.Empty;
            firstInputTimestampUnixMs = 0;

            int safeWidth = MakeEven(Mathf.Max(16, width));
            int safeHeight = MakeEven(Mathf.Max(16, height));
            int safeFrameRate = Mathf.Clamp(frameRate, 1, 120);
            int safeFrameCount = Mathf.Clamp(frameCount, 1, 600);
            int safeBitrateKbps = Mathf.Max(128, bitrateKbps);
            long sessionStartUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (useSharedVideoFrameInput && currentVideoFrameInput != null)
            {
                yield return WaitForSharedInput();
                if (currentVideoFrameInput.isValid && useSharedInputEncodingParameters)
                {
                    safeWidth = MakeEven(Mathf.Max(16, currentVideoFrameInput.outputResolution.x));
                    safeHeight = MakeEven(Mathf.Max(16, currentVideoFrameInput.outputResolution.y));
                    safeFrameRate = Mathf.Clamp(currentVideoFrameInput.frameRate > 0 ? currentVideoFrameInput.frameRate : safeFrameRate, 1, 120);
                    safeBitrateKbps = Mathf.Max(128, currentVideoFrameInput.bitrateKbps > 0 ? currentVideoFrameInput.bitrateKbps : safeBitrateKbps);
                    codec = string.IsNullOrWhiteSpace(currentVideoFrameInput.codec) ? codec : currentVideoFrameInput.codec;
                    lastStatus = "Using shared video frame input timing and encoding parameters.";
                }
                else if (currentVideoFrameInput.isValid)
                {
                    lastStatus = "Using shared video frame input timing with runner encoding parameters.";
                }
                else
                {
                    lastStatus = "Shared video frame input was not ready; falling back to synthetic timing.";
                    Debug.LogWarning(lastStatus, this);
                }
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidJavaObject encoder = null;
            bool started = false;
            try
            {
                string sessionFolder = CreateSessionFolder();
                mp4Path = Path.Combine(sessionFolder, "capture.mp4");
                manifestPath = Path.Combine(sessionFolder, "manifest.json");

                encoder = new AndroidJavaObject(JavaClassName);
                started = encoder.Call<bool>(
                    "startWithMp4",
                    codec,
                    safeWidth,
                    safeHeight,
                    safeFrameRate,
                    safeBitrateKbps,
                    Mathf.Max(0.1f, keyFrameIntervalSeconds),
                    mp4Path);

                encoderName = encoder.Call<string>("getCodecName") ?? string.Empty;
                lastStatus = encoder.Call<string>("getLastStatus") ?? string.Empty;
            }
            catch (Exception ex)
            {
                started = false;
                lastStatus = "Android muxer smoke start failed: " + ex.Message;
                Debug.LogException(ex, this);
            }

            if (started)
            {
                long frameDurationUs = 1000000L / safeFrameRate;
                long frameDurationMs = 1000L / safeFrameRate;
                for (int i = 0; i < safeFrameCount; i++)
                {
                    var inputFrame = ResolveInputFrame(i, frameDurationMs, sessionStartUnixMs);
                    try
                    {
                        byte[] payload = encoder.Call<byte[]>(
                            "encodePatternFrame",
                            ResolvePresentationTimeUs(inputFrame.timestampUnixMs, i * frameDurationUs),
                            i == 0);
                        encodeCallCount++;
                        if (payload != null && payload.Length > 0)
                        {
                            PublishAccessUnit(
                                payload,
                                inputFrame.sourceFrameId,
                                i == 0,
                                inputFrame.timestampUnixMs,
                                safeWidth,
                                safeHeight,
                                safeFrameRate);
                        }
                        else
                        {
                            zeroOutputFrameCount++;
                        }

                        if (payload == null || payload.Length == 0 || i == 0 || i == safeFrameCount - 1)
                        {
                            lastEncodeStatus = encoder.Call<string>("getLastStatus") ?? string.Empty;
                        }
                    }
                    catch (Exception ex)
                    {
                        lastStatus = "Android muxer smoke encode failed: " + ex.Message;
                        Debug.LogException(ex, this);
                        break;
                    }

                    yield return null;
                }

                StopEncoderAndCollectResult(encoder, safeWidth, safeHeight, safeFrameRate, safeFrameCount, sessionStartUnixMs);
            }

            if (encoder != null)
            {
                encoder.Dispose();
            }
#else
            lastStatus = "Android single-encode muxer smoke only runs on Android device builds.";
#endif

            isRunning = false;
            string result = "SingleEncodeAndroidMuxerSmokeRunner result: success=" + lastRunSucceeded +
                " codec=" + codec +
                " encoder=" + encoderName +
                " packets=" + publishedFramePacketCount +
                " accessUnitBytes=" + accessUnitBytes +
                " muxedSamples=" + muxedSampleCount +
                " muxedBytes=" + muxedBytes +
                " encodeCalls=" + encodeCallCount +
                " zeroOutputs=" + zeroOutputFrameCount +
                " mp4Bytes=" + mp4Bytes +
                " mp4Path=" + mp4Path +
                " status=" + lastStatus;
            if (lastRunSucceeded)
            {
                Debug.Log(result, this);
            }
            else
            {
                Debug.LogWarning(result, this);
            }

            yield break;
        }

        private string CreateSessionFolder()
        {
            string folder = Path.Combine(
                Application.persistentDataPath,
                outputFolderName,
                DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(folder);
            return folder;
        }

        private void PublishAccessUnit(
            byte[] payload,
            long sourceFrameId,
            bool isKeyFrame,
            long timestampUnixMs,
            int safeWidth,
            int safeHeight,
            int safeFrameRate)
        {
            if (outputQueue == null)
            {
                return;
            }

            var record = new CaptureOutputRecord
            {
                outputId = nextOutputId++,
                timestampUnixMs = timestampUnixMs,
                outputKind = CaptureOutputKind.FramePacket,
                deliveryKind = CaptureDeliveryKind.Stream,
                payloadKind = IsHevc(codec) ? CapturePayloadKind.H265AccessUnit : CapturePayloadKind.H264AccessUnit,
                payloadRef = CapturePayloadRef.FromBytes(payload),
                metadataMode = CaptureMetadataMode.None,
                status = CaptureOutputStatus.Ready,
                sourceCameraFrameId = sourceFrameId,
                sourceFrameStartId = sourceFrameId,
                sourceFrameEndId = sourceFrameId,
                codec = IsHevc(codec) ? "H265" : "H264",
                width = safeWidth,
                height = safeHeight,
                frameRate = safeFrameRate,
                isKeyFrame = isKeyFrame,
                byteLength = payload.Length
            };

            currentOutput?.SetRecord(record);
            outputQueue.RecordData(record);
            publishedFramePacketCount++;
            accessUnitBytes += payload.Length;
        }

        private void PublishMp4Artifact(
            int safeWidth,
            int safeHeight,
            int safeFrameRate,
            int safeFrameCount,
            long sessionStartUnixMs)
        {
            if (outputQueue == null || !File.Exists(mp4Path))
            {
                return;
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long length = new FileInfo(mp4Path).Length;
            var record = new CaptureOutputRecord
            {
                outputId = nextOutputId++,
                timestampUnixMs = now,
                outputKind = CaptureOutputKind.FileArtifact,
                deliveryKind = CaptureDeliveryKind.OneShot,
                payloadKind = CapturePayloadKind.Mp4File,
                payloadRef = CapturePayloadRef.FromLocalFile(mp4Path, length),
                metadataMode = CaptureMetadataMode.SidecarFile,
                manifestPath = manifestPath,
                status = CaptureOutputStatus.Ready,
                sourceCameraFrameId = safeFrameCount - 1,
                sourceFrameStartId = 0,
                sourceFrameEndId = safeFrameCount - 1,
                timestampStartUnixMs = sessionStartUnixMs,
                timestampEndUnixMs = now,
                codec = "MP4",
                width = safeWidth,
                height = safeHeight,
                frameRate = safeFrameRate,
                byteLength = length > int.MaxValue ? int.MaxValue : (int)length
            };

            currentOutput?.SetRecord(record);
            outputQueue.RecordData(record);
        }

        private void WriteManifest(int safeWidth, int safeHeight, int safeFrameRate, int safeFrameCount)
        {
            var manifest = new AndroidMuxerSmokeManifest
            {
                codec = codec,
                encoderName = encoderName,
                mp4Path = mp4Path,
                width = safeWidth,
                height = safeHeight,
                frameRate = safeFrameRate,
                frameCount = safeFrameCount,
                publishedFramePacketCount = publishedFramePacketCount,
                accessUnitBytes = accessUnitBytes,
                muxedSampleCount = muxedSampleCount,
                muxedBytes = muxedBytes,
                mp4Bytes = mp4Bytes,
                encodeCallCount = encodeCallCount,
                zeroOutputFrameCount = zeroOutputFrameCount,
                lastEncodeStatus = lastEncodeStatus,
                encoderInputMode = "SyntheticPatternSurface",
                inputTextureConsumedByEncoder = false,
                useSharedVideoFrameInput = useSharedVideoFrameInput,
                useSharedInputEncodingParameters = useSharedInputEncodingParameters,
                observedInputFrameCount = observedInputFrameCount,
                firstInputSourceCameraFrameId = firstInputSourceCameraFrameId,
                lastInputSourceCameraFrameId = lastInputSourceCameraFrameId,
                lastInputSourceKind = lastInputSourceKind,
                graphicsDeviceType = SystemInfo.graphicsDeviceType.ToString(),
                lastStatus = lastStatus
            };
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true));
        }

        private IEnumerator WaitForSharedInput()
        {
            float deadline = Time.realtimeSinceStartup + Mathf.Max(0f, waitForSharedInputSeconds);
            while (!currentVideoFrameInput.isValid && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }

        private InputFrameTiming ResolveInputFrame(int fallbackFrameIndex, long fallbackFrameDurationMs, long sessionStartUnixMs)
        {
            if (useSharedVideoFrameInput && currentVideoFrameInput != null && currentVideoFrameInput.isValid)
            {
                long sourceFrameId = currentVideoFrameInput.sourceCameraFrameId;
                long timestampUnixMs = currentVideoFrameInput.timestampUnixMs > 0
                    ? currentVideoFrameInput.timestampUnixMs
                    : sessionStartUnixMs + fallbackFrameIndex * fallbackFrameDurationMs;
                if (sourceFrameId != lastInputSourceCameraFrameId)
                {
                    observedInputFrameCount++;
                    if (firstInputSourceCameraFrameId < 0)
                    {
                        firstInputSourceCameraFrameId = sourceFrameId;
                    }
                }

                lastInputSourceCameraFrameId = sourceFrameId;
                lastInputSourceKind = currentVideoFrameInput.sourceKind.ToString();
                return new InputFrameTiming(sourceFrameId, timestampUnixMs);
            }

            return new InputFrameTiming(fallbackFrameIndex, sessionStartUnixMs + fallbackFrameIndex * fallbackFrameDurationMs);
        }

        private long ResolvePresentationTimeUs(long timestampUnixMs, long fallbackPresentationTimeUs)
        {
            if (!useSharedVideoFrameInput || timestampUnixMs <= 0)
            {
                return fallbackPresentationTimeUs;
            }

            if (firstInputTimestampUnixMs <= 0)
            {
                firstInputTimestampUnixMs = timestampUnixMs;
            }

            return Math.Max(0L, timestampUnixMs - firstInputTimestampUnixMs) * 1000L;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void StopEncoderAndCollectResult(
            AndroidJavaObject encoder,
            int safeWidth,
            int safeHeight,
            int safeFrameRate,
            int safeFrameCount,
            long sessionStartUnixMs)
        {
            if (encoder == null)
            {
                return;
            }

            try
            {
                encoder.Call("stop");
                muxedSampleCount = encoder.Call<long>("getMuxedSampleCount");
                muxedBytes = encoder.Call<long>("getMuxedBytes");
                lastStatus = encoder.Call<string>("getLastStatus") ?? string.Empty;
            }
            catch (Exception ex)
            {
                lastStatus = "Android muxer smoke stop failed: " + ex.Message;
                Debug.LogException(ex, this);
            }

            mp4Bytes = File.Exists(mp4Path) ? new FileInfo(mp4Path).Length : 0;
            WriteManifest(safeWidth, safeHeight, safeFrameRate, safeFrameCount);
            PublishMp4Artifact(safeWidth, safeHeight, safeFrameRate, safeFrameCount, sessionStartUnixMs);
            lastRunSucceeded = publishedFramePacketCount > 0 && mp4Bytes > 0 && muxedSampleCount > 0;
        }
#endif

        private static bool IsHevc(string value)
        {
            return string.Equals(value, "H265", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "HEVC", StringComparison.OrdinalIgnoreCase);
        }

        private static int MakeEven(int value)
        {
            return value % 2 == 0 ? value : value - 1;
        }

        [Serializable]
        private sealed class AndroidMuxerSmokeManifest
        {
            public string codec;
            public string encoderName;
            public string mp4Path;
            public int width;
            public int height;
            public int frameRate;
            public int frameCount;
            public int publishedFramePacketCount;
            public long accessUnitBytes;
            public long muxedSampleCount;
            public long muxedBytes;
            public long mp4Bytes;
            public int encodeCallCount;
            public int zeroOutputFrameCount;
            public string lastEncodeStatus;
            public string encoderInputMode;
            public bool inputTextureConsumedByEncoder;
            public bool useSharedVideoFrameInput;
            public bool useSharedInputEncodingParameters;
            public int observedInputFrameCount;
            public long firstInputSourceCameraFrameId;
            public long lastInputSourceCameraFrameId;
            public string lastInputSourceKind;
            public string graphicsDeviceType;
            public string lastStatus;
        }

        private readonly struct InputFrameTiming
        {
            public readonly long sourceFrameId;
            public readonly long timestampUnixMs;

            public InputFrameTiming(long sourceFrameId, long timestampUnixMs)
            {
                this.sourceFrameId = sourceFrameId;
                this.timestampUnixMs = timestampUnixMs;
            }
        }
    }
}
#pragma warning restore 0414
