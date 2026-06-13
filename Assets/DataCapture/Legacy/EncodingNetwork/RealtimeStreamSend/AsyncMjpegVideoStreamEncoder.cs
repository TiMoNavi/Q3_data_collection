using DataCapture.Synchronization;
using SObasic.CurrentQueueBridge;
using UnityEngine;
using UnityEngine.Rendering;

namespace DataCapture.Networking
{
    public class AsyncMjpegVideoStreamEncoder : MonoBehaviour
    {
        [SerializeField] private CurrentCameraImageSO currentCameraImage;
        [SerializeField] private CurrentCameraStreamStateSO currentStreamState;
        [SerializeField] private CurrentEncodedFrameSO currentEncodedFrame;
        [SerializeField] private EncodedFrameQueueSO encodedFrameQueue;
        [SerializeField] private EncoderConfigurationSO encoderConfiguration;
        [SerializeField] private EncodingPipelineConfigurationSO pipelineConfiguration;
        [SerializeField] private VideoPacketSender videoSender;
        [SerializeField] private RecordingSessionStateSO recordingState;
        [SerializeField] private CaptureTransmissionGateSO transmissionGate;
        [SerializeField] private MergedFrameSnapshotQueueSO mergedQueue;
        [SerializeField] private bool requireSendableMergedSnapshot = true;
        [SerializeField] private bool streamOnUpdate;
        [SerializeField, Range(1, 100)] private int jpegQuality = 70;

        [Header("Runtime Diagnostics")]
        [SerializeField] private ResolvedVideoEncodingParameters resolvedParameters;
        [SerializeField] private string lastStatus;
        [SerializeField] private long lastEncodedSourceFrameId = -1;
        [SerializeField] private int queuedFrameCount;
        [SerializeField] private int encodedFrameCount;
        [SerializeField] private int sentFrameCount;
        [SerializeField] private int sendSkippedCount;
        [SerializeField] private int sendFailedCount;
        [SerializeField] private int readbackErrorCount;
        [SerializeField] private int blockedAttemptCount;

        private bool readbackPending;
        private float nextFrameTime;
        private long nextEncodedFrameId;

        private void Update()
        {
            if (streamOnUpdate)
            {
                TryQueueFrame();
            }
        }

        [ContextMenu("Queue MJPEG Video Frame")]
        public bool TryQueueFrame()
        {
            if (pipelineConfiguration != null && !pipelineConfiguration.AllowsVideo)
            {
                return Block("Pipeline mode does not allow video encoding.");
            }

            if (pipelineConfiguration != null &&
                pipelineConfiguration.videoEncoderBackend != VideoEncoderBackend.DebugJpeg)
            {
                return Block("MJPEG fallback is only used when videoEncoderBackend is DebugJpeg.");
            }

            if (transmissionGate != null && !transmissionGate.Active)
            {
                return Block("Transmission gate blocked: " + transmissionGate.activeBlocker);
            }

            if (recordingState != null && !recordingState.IsRecording)
            {
                return Block("Recording state is " + recordingState.State + ".");
            }

            if (readbackPending)
            {
                return Block("MJPEG GPU readback is still pending.");
            }

            if (currentCameraImage == null || !currentCameraImage.isValid || currentCameraImage.currentTexture == null)
            {
                return Block("CurrentCameraImage is not ready.");
            }

            CameraImageFrameRecord sourceFrame = currentCameraImage.ToRecord();
            if (sourceFrame.frameId == lastEncodedSourceFrameId)
            {
                return Block("Camera frame was already encoded.");
            }

            if (requireSendableMergedSnapshot &&
                !SynchronizedFrameSelection.HasSendableSnapshotForFrame(mergedQueue, sourceFrame))
            {
                return Block("No sendable merged snapshot for camera frame " + sourceFrame.frameId + ".");
            }

            resolvedParameters = VideoEncodingParameterResolver.Resolve(
                currentStreamState,
                currentCameraImage,
                encoderConfiguration,
                pipelineConfiguration);
            if (!resolvedParameters.isValid)
            {
                return Block(resolvedParameters.invalidReason);
            }

            if (Time.unscaledTime < nextFrameTime)
            {
                return Block("MJPEG frame-rate throttle is active.");
            }

            int width = Mathf.Max(16, resolvedParameters.width);
            int height = Mathf.Max(16, resolvedParameters.height);
            RenderTexture scaled = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(currentCameraImage.currentTexture, scaled);

            readbackPending = true;
            nextFrameTime = Time.unscaledTime + 1f / Mathf.Max(1, resolvedParameters.frameRate);
            queuedFrameCount++;

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

            lastStatus = "Queued MJPEG frame " + sourceFrame.frameId + " at " + width + "x" + height + ".";
            return true;
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
                lastStatus = "MJPEG GPU readback failed for camera frame " + sourceFrame.frameId + ".";
                return;
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.LoadRawTextureData(request.GetData<byte>());
            texture.Apply(false, false);
            byte[] payload = texture.EncodeToJPG(Mathf.Clamp(jpegQuality, 1, 100));
            Destroy(texture);

            var record = new EncodedFrameRecord
            {
                encodedFrameId = nextEncodedFrameId++,
                sourceCameraFrameId = sourceFrame.frameId,
                timestampUnixMs = sourceFrame.timestampUnixMs,
                isKeyFrame = true,
                width = width,
                height = height,
                codec = "DEBUG_MJPEG",
                byteLength = payload.Length,
                debugFilePath = string.Empty
            };

            currentEncodedFrame?.SetFrame(record);
            encodedFrameQueue?.RecordData(record);
            encodedFrameCount++;
            lastEncodedSourceFrameId = sourceFrame.frameId;

            NetworkSendResult sendResult = videoSender != null
                ? videoSender.SendDetailed(record, payload)
                : NetworkSendResult.Failed("VideoPacketSender is not assigned.");
            if (sendResult.Sent)
            {
                sentFrameCount++;
            }
            else if (sendResult.outcome == NetworkSendOutcome.Skipped)
            {
                sendSkippedCount++;
            }
            else
            {
                sendFailedCount++;
            }

            lastStatus = "Encoded MJPEG frame " + sourceFrame.frameId + " bytes=" + payload.Length + " outcome=" + sendResult.outcome + ".";
        }

        private bool Block(string reason)
        {
            blockedAttemptCount++;
            lastStatus = reason;
            return false;
        }
    }
}
