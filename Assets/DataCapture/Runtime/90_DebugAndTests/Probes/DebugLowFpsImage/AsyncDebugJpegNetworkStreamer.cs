using DataCapture.Synchronization;
using SObasic.CurrentQueueBridge;
using UnityEngine;
using UnityEngine.Rendering;

namespace DataCapture.Networking
{
    public class AsyncDebugJpegNetworkStreamer : MonoBehaviour
    {
        [SerializeField] private CurrentVideoFrameInputSO currentVideoFrameInput;
        [SerializeField] private CurrentEncodedFrameSO currentEncodedFrame;
        [SerializeField] private EncodedFrameQueueSO encodedFrameQueue;
        [SerializeField] private EncodingPipelineConfigurationSO pipelineConfiguration;
        [SerializeField] private DebugImageStreamSettingsSO debugImageSettings;
        [SerializeField] private EncoderConfigurationSO configuration;
        [SerializeField] private EncodedOutputMetadataBinder outputBinder;
        [SerializeField] private VideoPacketSender videoSender;
        [SerializeField] private RecordingSessionStateSO recordingState;
        [SerializeField] private CaptureTransmissionGateSO transmissionGate;
        [SerializeField] private MergedFrameSnapshotQueueSO mergedQueue;
        [SerializeField] private bool requireSendableMergedSnapshot = true;
        [SerializeField] private bool publishCaptureOutput = true;
        [SerializeField] private bool streamOnUpdate;
        [SerializeField] private float maxFramesPerSecond = 2f;
        [SerializeField] private int maxDimension = 320;
        [SerializeField, Range(1, 100)] private int jpegQuality = 70;
        [SerializeField] private bool useSharedCompressionQuality = true;

        [Header("Runtime Diagnostics")]
        [SerializeField] private bool logDebugMessagesToUnity = true;
        [SerializeField] private float debugLogIntervalSeconds = 1f;
        [SerializeField] private string lastStreamerDebugMessage = string.Empty;
        [SerializeField] private long lastStreamerDebugUnixMs;
        [SerializeField] private int queuedFrameCount;
        [SerializeField] private int sentFrameCount;
        [SerializeField] private int readbackErrorCount;
        [SerializeField] private int blockedAttemptCount;

        private bool readbackPending;
        private float nextFrameTime;
        private long nextEncodedFrameId;
        private string lastLoggedDebugMessage = string.Empty;
        private float nextDebugLogTime;

        private void Update()
        {
            if (streamOnUpdate)
            {
                TryQueueFrame();
            }
        }

        [ContextMenu("Queue Debug JPEG Frame")]
        public bool TryQueueFrame()
        {
            if (pipelineConfiguration != null && !pipelineConfiguration.AllowsDebugImage)
            {
                return Block("Pipeline mode does not allow Debug JPEG streaming.", false);
            }

            if (transmissionGate != null && !transmissionGate.Active)
            {
                return Block("Transmission gate blocked: " + transmissionGate.activeBlocker, true);
            }

            if (readbackPending)
            {
                return Block("Debug JPEG GPU readback is still pending.", false);
            }

            if (currentVideoFrameInput == null)
            {
                return Block("CurrentVideoFrameInput SO is not assigned.", true);
            }

            if (!currentVideoFrameInput.isValid)
            {
                return Block("CurrentVideoFrameInput is not valid yet.", true);
            }

            if (videoSender == null)
            {
                return Block("VideoPacketSender is not assigned.", true);
            }

            if (recordingState != null && !recordingState.IsRecording)
            {
                return Block("Recording state is " + recordingState.State + ".", true);
            }

            float effectiveMaxFramesPerSecond = GetEffectiveMaxFramesPerSecond();
            if (Time.unscaledTime < nextFrameTime)
            {
                return Block("Debug JPEG frame-rate throttle is active.", false);
            }

            Texture source = currentVideoFrameInput.inputTexture;
            if (source == null)
            {
                return Block("CurrentVideoFrameInput texture is null.", true);
            }

            CameraImageFrameRecord sourceFrame = currentVideoFrameInput.ToSourceFrameRecord();
            if (GetEffectiveRequireSendableMergedSnapshot() && !HasSendableMergedSnapshot(sourceFrame))
            {
                return Block("No sendable merged snapshot for camera frame " + sourceFrame.frameId + ".", true);
            }

            int width;
            int height;
            GetScaledSize(source.width, source.height, GetEffectiveMaxDimension(), out width, out height);
            RenderTexture scaled = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, scaled);

            readbackPending = true;
            nextFrameTime = Time.unscaledTime + 1f / Mathf.Max(0.1f, effectiveMaxFramesPerSecond);

            AsyncGPUReadback.Request(scaled, 0, TextureFormat.RGBA32, request =>
            {
                try
                {
                    HandleReadback(request, scaled, sourceFrame, width, height);
                }
                finally
                {
                    RenderTexture.ReleaseTemporary(scaled);
                    readbackPending = false;
                }
            });

            queuedFrameCount++;
            SetDebugMessage("Queued Debug JPEG frame " + sourceFrame.frameId + " at " + width + "x" + height + ".");
            return true;
        }

        private bool HasSendableMergedSnapshot(CameraImageFrameRecord sourceFrame)
        {
            return SynchronizedFrameSelection.HasSendableSnapshotForFrame(mergedQueue, sourceFrame);
        }

        private void HandleReadback(
            AsyncGPUReadbackRequest request,
            RenderTexture scaled,
            CameraImageFrameRecord sourceFrame,
            int width,
            int height)
        {
            if (request.hasError)
            {
                readbackErrorCount++;
                SetDebugMessage("Debug JPEG GPU readback failed for camera frame " + sourceFrame.frameId + ".");
                LogDebugMessage(lastStreamerDebugMessage, true);
                return;
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.LoadRawTextureData(request.GetData<byte>());
            texture.Apply(false, false);
            byte[] payload = texture.EncodeToJPG(GetEffectiveJpegQuality());
            Destroy(texture);

            var record = new EncodedFrameRecord
            {
                encodedFrameId = nextEncodedFrameId++,
                sourceCameraFrameId = sourceFrame.frameId,
                timestampUnixMs = sourceFrame.timestampUnixMs,
                isKeyFrame = true,
                width = width,
                height = height,
                codec = "DEBUG_JPEG",
                byteLength = payload.Length,
                debugFilePath = string.Empty
            };

            currentEncodedFrame?.SetFrame(record);
            encodedFrameQueue?.RecordData(record);
            if (publishCaptureOutput && outputBinder != null)
            {
                outputBinder.PublishFramePacket(record, payload, CapturePayloadKind.DebugJpeg);
            }

            NetworkSendResult sendResult = videoSender.SendDetailed(record, payload);
            bool sent = sendResult.Sent;
            if (sent)
            {
                sentFrameCount++;
            }

            SetDebugMessage("Sent Debug JPEG frame " + record.sourceCameraFrameId + " bytes=" + payload.Length + " outcome=" + sendResult.outcome + ".");
            LogDebugMessage(lastStreamerDebugMessage, false);
        }

        private float GetEffectiveMaxFramesPerSecond()
        {
            return debugImageSettings != null
                ? debugImageSettings.maxFramesPerSecond
                : maxFramesPerSecond;
        }

        private int GetEffectiveMaxDimension()
        {
            int dimension = debugImageSettings != null
                ? debugImageSettings.maxDimension
                : maxDimension;
            return Mathf.Max(16, dimension);
        }

        private int GetEffectiveJpegQuality()
        {
            if (useSharedCompressionQuality &&
                currentVideoFrameInput != null &&
                currentVideoFrameInput.qualityPercent > 0)
            {
                return Mathf.Clamp(currentVideoFrameInput.qualityPercent, 1, 100);
            }

            int quality = debugImageSettings != null
                ? debugImageSettings.jpegQuality
                : jpegQuality;
            return Mathf.Clamp(quality, 1, 100);
        }

        private bool GetEffectiveRequireSendableMergedSnapshot()
        {
            return debugImageSettings != null
                ? debugImageSettings.requireSendableMergedSnapshot
                : requireSendableMergedSnapshot;
        }

        private static void GetScaledSize(int sourceWidth, int sourceHeight, int maxSize, out int width, out int height)
        {
            if (sourceWidth <= 0 || sourceHeight <= 0)
            {
                width = maxSize;
                height = maxSize;
                return;
            }

            float scale = Mathf.Min(1f, maxSize / (float)Mathf.Max(sourceWidth, sourceHeight));
            width = Mathf.Max(16, Mathf.RoundToInt(sourceWidth * scale));
            height = Mathf.Max(16, Mathf.RoundToInt(sourceHeight * scale));
        }

        private bool Block(string reason, bool logWhenRepeated)
        {
            blockedAttemptCount++;
            SetDebugMessage(reason);

            if (logWhenRepeated || reason != lastLoggedDebugMessage)
            {
                LogDebugMessage(reason, false);
            }

            return false;
        }

        private void SetDebugMessage(string message)
        {
            lastStreamerDebugMessage = message;
            lastStreamerDebugUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        private void LogDebugMessage(string message, bool warning)
        {
            if (!logDebugMessagesToUnity || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            if (message == lastLoggedDebugMessage && Time.unscaledTime < nextDebugLogTime)
            {
                return;
            }

            lastLoggedDebugMessage = message;
            nextDebugLogTime = Time.unscaledTime + Mathf.Max(0.1f, debugLogIntervalSeconds);

            if (warning)
            {
                Debug.LogWarning("AsyncDebugJpegNetworkStreamer: " + message, this);
            }
            else
            {
                Debug.Log("AsyncDebugJpegNetworkStreamer: " + message, this);
            }
        }
    }
}
