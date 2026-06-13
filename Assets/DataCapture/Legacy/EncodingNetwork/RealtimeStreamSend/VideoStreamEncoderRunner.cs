using DataCapture.Synchronization;
using SObasic.CurrentQueueBridge;
using UnityEngine;

namespace DataCapture.Networking
{
    public class VideoStreamEncoderRunner : MonoBehaviour
    {
        [SerializeField] private EncodingPipelineConfigurationSO pipelineConfiguration;
        [SerializeField] private CurrentCameraImageSO currentCameraImage;
        [SerializeField] private CurrentCameraStreamStateSO currentStreamState;
        [SerializeField] private CurrentEncodedFrameSO currentEncodedFrame;
        [SerializeField] private EncodedFrameQueueSO encodedFrameQueue;
        [SerializeField] private EncoderConfigurationSO encoderConfiguration;
        [SerializeField] private MonoBehaviour encoderAdapterBehaviour;
        [SerializeField] private VideoPacketSender videoSender;
        [SerializeField] private RecordingSessionStateSO recordingState;
        [SerializeField] private CaptureTransmissionGateSO transmissionGate;
        [SerializeField] private MergedFrameSnapshotQueueSO mergedQueue;
        [SerializeField] private bool requireSendableMergedSnapshot = true;
        [SerializeField] private bool encodeOnUpdate;
        [SerializeField] private bool sendEncodedPayload = true;

        [Header("Runtime Diagnostics")]
        [SerializeField] private ResolvedVideoEncodingParameters resolvedParameters;
        [SerializeField] private string lastStatus;
        [SerializeField] private long lastEncodedSourceFrameId = -1;
        [SerializeField] private int encodedFrameCount;
        [SerializeField] private int blockedAttemptCount;

        private float nextFrameTime;

        private IVideoEncoderAdapter Encoder => encoderAdapterBehaviour as IVideoEncoderAdapter;

        private void Update()
        {
            if (encodeOnUpdate)
            {
                TryEncodeLatestFrame();
            }
        }

        [ContextMenu("Encode Latest Video Frame")]
        public bool TryEncodeLatestFrame()
        {
            if (pipelineConfiguration != null && !pipelineConfiguration.AllowsVideo)
            {
                return Block("Pipeline mode does not allow video encoding.");
            }

            if (transmissionGate != null && !transmissionGate.Active)
            {
                return Block("Transmission gate blocked: " + transmissionGate.activeBlocker);
            }

            if (recordingState != null && !recordingState.IsRecording)
            {
                return Block("Recording state is " + recordingState.State + ".");
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
                return Block("Video encoder frame-rate throttle is active.");
            }

            IVideoEncoderAdapter encoder = Encoder;
            if (encoder == null || !encoder.IsReady)
            {
                return Block("Video encoder adapter is not ready.");
            }

            if (!encoder.TryEncode(
                currentCameraImage.currentTexture,
                sourceFrame,
                out EncodedFrameRecord encodedFrame,
                out byte[] payload))
            {
                return Block("Video encoder adapter did not produce a frame.");
            }

            currentEncodedFrame?.SetFrame(encodedFrame);
            encodedFrameQueue?.RecordData(encodedFrame);

            if (sendEncodedPayload && videoSender != null)
            {
                videoSender.Send(encodedFrame, payload);
            }

            lastEncodedSourceFrameId = sourceFrame.frameId;
            encodedFrameCount++;
            nextFrameTime = Time.unscaledTime + 1f / Mathf.Max(1, resolvedParameters.frameRate);
            lastStatus = "Encoded video frame " + sourceFrame.frameId + " as " + encodedFrame.codec + ".";
            return true;
        }

        private bool Block(string reason)
        {
            blockedAttemptCount++;
            lastStatus = reason;
            return false;
        }
    }
}
