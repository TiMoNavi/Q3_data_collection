using System;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace DataCapture.Networking
{
    public sealed class UnityDynamicTextureEncoderSmokeRunner : MonoBehaviour
    {
        private enum BridgeMode
        {
            TryUnityTextureBridge,
            PatternOnly
        }

        [Header("Run")]
        [SerializeField] private bool runOnStart;
        [SerializeField] private float startDelaySeconds = 1f;

        [Header("Unity Test Texture")]
        [SerializeField] private int width = 320;
        [SerializeField] private int height = 320;
        [SerializeField] private int frameRate = 30;
        [SerializeField] private int frameCount = 60;
        [SerializeField] private BridgeMode bridgeMode = BridgeMode.TryUnityTextureBridge;
        [SerializeField] private bool fallbackToPatternWhenBridgeUnavailable;

        [Header("Encoder")]
        [SerializeField] private string codec = "H264";
        [SerializeField] private int bitrateKbps = 2000;
        [SerializeField] private float keyFrameIntervalSeconds = 1f;

        [Header("Output Queue")]
        [SerializeField] private CurrentCaptureOutputSO currentOutput;
        [SerializeField] private CaptureOutputQueueSO outputQueue;
        [SerializeField] private string outputFolderName = "DataCapture/UnityDynamicTextureEncoderSmoke";

        [Header("Result")]
        [SerializeField] private bool isRunning;
        [SerializeField] private bool lastRunSucceeded;
        [SerializeField] private string lastStatus;
        [SerializeField] private string encoderName;
        [SerializeField] private string mp4Path;
        [SerializeField] private string manifestPath;
        [SerializeField] private string graphicsDeviceType;
        [SerializeField] private string nativeTexturePointerHex;
        [SerializeField] private string nativeBridgeStatusJson;
        [SerializeField] private bool nativeBridgeVulkanReady;
        [SerializeField] private int nativeBridgeProbeEventCount;
        [SerializeField] private int bridgeAttemptCount;
        [SerializeField] private int bridgeOutputFrameCount;
        [SerializeField] private int patternFallbackFrameCount;
        [SerializeField] private int publishedFramePacketCount;
        [SerializeField] private long accessUnitBytes;
        [SerializeField] private long mp4Bytes;
        [SerializeField] private long muxedSampleCount;
        [SerializeField] private long muxedBytes;
        [SerializeField] private string lastEncodeStatus;

        private const string JavaClassName = "com.q3datacapture.mediacodec.Q3SurfaceVideoEncoder";

        private RenderTexture dynamicRenderTexture;
        private Texture2D uploadTexture;
        private Color32[] pixels;
        private long nextOutputId;

        private void Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (runOnStart)
            {
                StartCoroutine(RunAfterDelay());
            }
#endif
        }

        private void OnDestroy()
        {
            ReleaseTextures();
        }

        [ContextMenu("Run Unity Dynamic Texture Encoder Smoke")]
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
            ResetResult();

            int safeWidth = MakeEven(Mathf.Max(16, width));
            int safeHeight = MakeEven(Mathf.Max(16, height));
            int safeFrameRate = Mathf.Clamp(frameRate, 1, 120);
            int safeFrameCount = Mathf.Clamp(frameCount, 1, 600);
            int safeBitrateKbps = Mathf.Max(128, bitrateKbps);
            float safeKeyFrameIntervalSeconds = Mathf.Max(0.1f, keyFrameIntervalSeconds);
            long sessionStartUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            EnsureTextures(safeWidth, safeHeight);
            graphicsDeviceType = SystemInfo.graphicsDeviceType.ToString();

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
                    safeKeyFrameIntervalSeconds,
                    mp4Path);

                encoderName = encoder.Call<string>("getCodecName") ?? string.Empty;
                lastStatus = encoder.Call<string>("getLastStatus") ?? string.Empty;
            }
            catch (Exception ex)
            {
                started = false;
                lastStatus = "Unity dynamic texture smoke start failed: " + ex.Message;
                Debug.LogException(ex, this);
            }

            if (started)
            {
                long frameDurationUs = 1000000L / safeFrameRate;
                long frameDurationMs = 1000L / safeFrameRate;
                for (int i = 0; i < safeFrameCount; i++)
                {
                    UpdateDynamicTexture(i, safeWidth, safeHeight);
                    long timestampUnixMs = sessionStartUnixMs + i * frameDurationMs;
                    long presentationTimeUs = i * frameDurationUs;
                    byte[] payload = null;

                    try
                    {
                        if (bridgeMode == BridgeMode.TryUnityTextureBridge)
                        {
                            IntPtr nativePtr = dynamicRenderTexture.GetNativeTexturePtr();
                            nativeTexturePointerHex = nativePtr == IntPtr.Zero
                                ? "0"
                                : "0x" + nativePtr.ToInt64().ToString("x");
                            SubmitNativeVulkanProbe(nativePtr, safeWidth, safeHeight);
                            bridgeAttemptCount++;
                            payload = encoder.Call<byte[]>(
                                "encodeUnityTextureFrame",
                                nativePtr.ToInt64(),
                                safeWidth,
                                safeHeight,
                                presentationTimeUs,
                                i == 0);
                            if (payload != null && payload.Length > 0)
                            {
                                bridgeOutputFrameCount++;
                            }
                        }

                        if ((payload == null || payload.Length == 0) && fallbackToPatternWhenBridgeUnavailable)
                        {
                            payload = encoder.Call<byte[]>(
                                "encodePatternFrame",
                                presentationTimeUs,
                                i == 0);
                            patternFallbackFrameCount++;
                        }
                        else if ((payload == null || payload.Length == 0) &&
                            bridgeMode == BridgeMode.TryUnityTextureBridge)
                        {
                            lastStatus = "Unity texture bridge produced no access unit and pattern fallback is disabled.";
                        }

                        if (payload != null && payload.Length > 0)
                        {
                            PublishAccessUnit(
                                payload,
                                i,
                                i == 0,
                                timestampUnixMs,
                                safeWidth,
                                safeHeight,
                                safeFrameRate);
                        }

                        lastEncodeStatus = encoder.Call<string>("getLastStatus") ?? string.Empty;
                    }
                    catch (Exception ex)
                    {
                        lastStatus = "Unity dynamic texture smoke encode failed: " + ex.Message;
                        Debug.LogException(ex, this);
                        break;
                    }

                    yield return null;
                    UpdateNativeVulkanBridgeStatus();
                }

                StopEncoderAndCollectResult(encoder, safeWidth, safeHeight, safeFrameRate, safeFrameCount, sessionStartUnixMs);
            }

            encoder?.Dispose();
#else
            lastStatus = "Unity dynamic texture encoder smoke only runs on Android device builds.";
#endif

            isRunning = false;
            string result = "UnityDynamicTextureEncoderSmokeRunner result: success=" + lastRunSucceeded +
                " graphics=" + graphicsDeviceType +
                " bridgeAttempts=" + bridgeAttemptCount +
                " bridgeOutputs=" + bridgeOutputFrameCount +
                " patternFallbacks=" + patternFallbackFrameCount +
                " packets=" + publishedFramePacketCount +
                " mp4Bytes=" + mp4Bytes +
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

        private void ResetResult()
        {
            isRunning = true;
            lastRunSucceeded = false;
            lastStatus = "Starting Unity dynamic texture encoder smoke.";
            encoderName = string.Empty;
            mp4Path = string.Empty;
            manifestPath = string.Empty;
            graphicsDeviceType = string.Empty;
            nativeTexturePointerHex = string.Empty;
            nativeBridgeStatusJson = string.Empty;
            nativeBridgeVulkanReady = false;
            nativeBridgeProbeEventCount = 0;
            bridgeAttemptCount = 0;
            bridgeOutputFrameCount = 0;
            patternFallbackFrameCount = 0;
            publishedFramePacketCount = 0;
            accessUnitBytes = 0;
            mp4Bytes = 0;
            muxedSampleCount = 0;
            muxedBytes = 0;
            lastEncodeStatus = string.Empty;
        }

        private void EnsureTextures(int safeWidth, int safeHeight)
        {
            if (dynamicRenderTexture != null &&
                dynamicRenderTexture.width == safeWidth &&
                dynamicRenderTexture.height == safeHeight)
            {
                return;
            }

            ReleaseTextures();
            dynamicRenderTexture = new RenderTexture(safeWidth, safeHeight, 0, RenderTextureFormat.ARGB32)
            {
                name = "Q3DC Unity Dynamic Encoder Smoke RT",
                useMipMap = false,
                autoGenerateMips = false
            };
            dynamicRenderTexture.Create();

            uploadTexture = new Texture2D(safeWidth, safeHeight, TextureFormat.RGBA32, false)
            {
                name = "Q3DC Unity Dynamic Encoder Smoke Upload"
            };
            pixels = new Color32[safeWidth * safeHeight];
        }

        private void ReleaseTextures()
        {
            if (dynamicRenderTexture != null)
            {
                dynamicRenderTexture.Release();
                DestroyUnityObject(dynamicRenderTexture);
                dynamicRenderTexture = null;
            }

            if (uploadTexture != null)
            {
                DestroyUnityObject(uploadTexture);
                uploadTexture = null;
            }

            pixels = null;
        }

        private void UpdateDynamicTexture(int frameIndex, int safeWidth, int safeHeight)
        {
            int stripeWidth = Mathf.Max(1, safeWidth / 8);
            for (int y = 0; y < safeHeight; y++)
            {
                for (int x = 0; x < safeWidth; x++)
                {
                    int stripe = (x / stripeWidth + frameIndex) & 7;
                    byte r = (byte)((stripe & 1) != 0 ? 255 : 32);
                    byte g = (byte)((stripe & 2) != 0 ? 255 : 48);
                    byte b = (byte)((stripe & 4) != 0 ? 255 : 64);
                    if (((x + frameIndex * 4) % safeWidth) < 6 || ((y + frameIndex * 3) % safeHeight) < 6)
                    {
                        r = 255;
                        g = 255;
                        b = 255;
                    }

                    pixels[y * safeWidth + x] = new Color32(r, g, b, 255);
                }
            }

            uploadTexture.SetPixels32(pixels);
            uploadTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            Graphics.Blit(uploadTexture, dynamicRenderTexture);
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
            var manifest = new UnityDynamicTextureSmokeManifest
            {
                codec = codec,
                encoderName = encoderName,
                mp4Path = mp4Path,
                width = safeWidth,
                height = safeHeight,
                frameRate = safeFrameRate,
                frameCount = safeFrameCount,
                graphicsDeviceType = graphicsDeviceType,
                nativeTexturePointerHex = nativeTexturePointerHex,
                nativeBridgeStatusJson = nativeBridgeStatusJson,
                nativeBridgeVulkanReady = nativeBridgeVulkanReady,
                nativeBridgeProbeEventCount = nativeBridgeProbeEventCount,
                bridgeMode = bridgeMode.ToString(),
                fallbackToPatternWhenBridgeUnavailable = fallbackToPatternWhenBridgeUnavailable,
                bridgeAttemptCount = bridgeAttemptCount,
                bridgeOutputFrameCount = bridgeOutputFrameCount,
                patternFallbackFrameCount = patternFallbackFrameCount,
                publishedFramePacketCount = publishedFramePacketCount,
                accessUnitBytes = accessUnitBytes,
                muxedSampleCount = muxedSampleCount,
                muxedBytes = muxedBytes,
                mp4Bytes = mp4Bytes,
                lastEncodeStatus = lastEncodeStatus,
                lastStatus = lastStatus
            };
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true));
        }

        private void SubmitNativeVulkanProbe(IntPtr nativePtr, int safeWidth, int safeHeight)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                NativeVulkanBridge.Q3DC_SetUnityTexture(nativePtr, safeWidth, safeHeight);
                IntPtr renderEventFunc = NativeVulkanBridge.Q3DC_GetRenderEventFunc();
                int eventId = NativeVulkanBridge.Q3DC_GetProbeEventId();
                if (renderEventFunc != IntPtr.Zero)
                {
                    GL.IssuePluginEvent(renderEventFunc, eventId);
                    nativeBridgeProbeEventCount++;
                }

                UpdateNativeVulkanBridgeStatus();
            }
            catch (Exception ex)
            {
                nativeBridgeStatusJson = "{\"error\":\"Native Vulkan bridge submit failed: " + EscapeJson(ex.Message) + "\"}";
            }
#endif
        }

        private void UpdateNativeVulkanBridgeStatus()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                nativeBridgeVulkanReady = NativeVulkanBridge.Q3DC_IsVulkanReady() != 0;
                byte[] buffer = new byte[2048];
                int length = NativeVulkanBridge.Q3DC_GetStatusJson(buffer, buffer.Length);
                nativeBridgeStatusJson = length > 0
                    ? Encoding.UTF8.GetString(buffer, 0, Mathf.Min(length, buffer.Length))
                    : string.Empty;
            }
            catch (Exception ex)
            {
                nativeBridgeStatusJson = "{\"error\":\"Native Vulkan bridge status failed: " + EscapeJson(ex.Message) + "\"}";
            }
#endif
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
                lastStatus = "Unity dynamic texture smoke stop failed: " + ex.Message;
                Debug.LogException(ex, this);
            }

            mp4Bytes = File.Exists(mp4Path) ? new FileInfo(mp4Path).Length : 0;
            WriteManifest(safeWidth, safeHeight, safeFrameRate, safeFrameCount);
            PublishMp4Artifact(safeWidth, safeHeight, safeFrameRate, safeFrameCount, sessionStartUnixMs);
            lastRunSucceeded = mp4Bytes > 0 &&
                muxedSampleCount > 0 &&
                (bridgeOutputFrameCount > 0 || patternFallbackFrameCount > 0);
            if (!lastRunSucceeded &&
                bridgeMode == BridgeMode.TryUnityTextureBridge &&
                bridgeOutputFrameCount == 0 &&
                patternFallbackFrameCount == 0)
            {
                lastStatus = "Unity texture bridge produced no access units. " + lastStatus;
            }
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

        private static void DestroyUnityObject(UnityEngine.Object unityObject)
        {
            if (unityObject == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(unityObject);
            }
            else
            {
                DestroyImmediate(unityObject);
            }
        }

        private static string EscapeJson(string value)
        {
            return string.IsNullOrEmpty(value)
                ? string.Empty
                : value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static class NativeVulkanBridge
        {
            private const string LibraryName = "q3dc_vulkan_bridge";

            [DllImport(LibraryName)]
            public static extern IntPtr Q3DC_GetRenderEventFunc();

            [DllImport(LibraryName)]
            public static extern int Q3DC_GetProbeEventId();

            [DllImport(LibraryName)]
            public static extern void Q3DC_SetUnityTexture(IntPtr texture, int width, int height);

            [DllImport(LibraryName)]
            public static extern int Q3DC_IsVulkanReady();

            [DllImport(LibraryName)]
            public static extern int Q3DC_GetStatusJson([Out] byte[] buffer, int bufferLength);
        }
#endif

        [Serializable]
        private sealed class UnityDynamicTextureSmokeManifest
        {
            public string codec;
            public string encoderName;
            public string mp4Path;
            public int width;
            public int height;
            public int frameRate;
            public int frameCount;
            public string graphicsDeviceType;
            public string nativeTexturePointerHex;
            public string nativeBridgeStatusJson;
            public bool nativeBridgeVulkanReady;
            public int nativeBridgeProbeEventCount;
            public string bridgeMode;
            public bool fallbackToPatternWhenBridgeUnavailable;
            public int bridgeAttemptCount;
            public int bridgeOutputFrameCount;
            public int patternFallbackFrameCount;
            public int publishedFramePacketCount;
            public long accessUnitBytes;
            public long muxedSampleCount;
            public long muxedBytes;
            public long mp4Bytes;
            public string lastEncodeStatus;
            public string lastStatus;
        }
    }
}
