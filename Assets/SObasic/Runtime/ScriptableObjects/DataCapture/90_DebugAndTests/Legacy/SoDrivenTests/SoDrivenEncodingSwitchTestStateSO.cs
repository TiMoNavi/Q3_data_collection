using DataCapture.Networking;
using UnityEngine;

namespace DataCapture.Testing
{
    public enum SoDrivenEncodingSwitchTestPhase
    {
        Idle,
        WaitingInitialDelay,
        RequestingDiscovery,
        WaitingForPcHandshake,
        NormalizingRecordingState,
        RequestingRecordingStart,
        WaitingForRecordingQueues,
        WaitingForMergedFrame,
        TestingDebugImageOutput,
        TestingMjpegVideoOutput,
        TestingDualOutput,
        RestoringPipeline,
        RequestingRecordingStop,
        Completed,
        Failed
    }

    [CreateAssetMenu(fileName = "SoDrivenEncodingSwitchTestStateSO", menuName = "DataCapture/90 Diagnostics/SO Driven Encoding Switch Test State")]
    public class SoDrivenEncodingSwitchTestStateSO : ScriptableObject
    {
        public bool isRunning;
        public bool isComplete;
        public bool hasFailure;
        public SoDrivenEncodingSwitchTestPhase phase;
        public string statusMessage = string.Empty;
        public string lastBlocker = string.Empty;
        public string stopReason = string.Empty;
        public long startedAtUnixMs;
        public long completedAtUnixMs;
        public int runRevision;
        public int startRequestRevision;
        public int stopRequestRevision;
        public EncodingPipelineMode originalPipelineMode;
        public VideoEncoderBackend originalVideoEncoderBackend;
        public bool originalAllowDebugImageDuringVideo;
        public int observedDebugImageFrames;
        public int observedMjpegVideoFrames;
        public bool observedDebugImageInDualMode;
        public bool observedMjpegVideoInDualMode;
        public string lastObservedCodec = string.Empty;
        public long lastObservedEncodedFrameId;
        public long lastObservedSourceCameraFrameId;
        public int lastObservedByteLength;

        [ContextMenu("Reset Test State")]
        public void ResetState()
        {
            isRunning = false;
            isComplete = false;
            hasFailure = false;
            phase = SoDrivenEncodingSwitchTestPhase.Idle;
            statusMessage = string.Empty;
            lastBlocker = string.Empty;
            stopReason = string.Empty;
            startedAtUnixMs = 0;
            completedAtUnixMs = 0;
            startRequestRevision = 0;
            stopRequestRevision = 0;
            observedDebugImageFrames = 0;
            observedMjpegVideoFrames = 0;
            observedDebugImageInDualMode = false;
            observedMjpegVideoInDualMode = false;
            lastObservedCodec = string.Empty;
            lastObservedEncodedFrameId = 0;
            lastObservedSourceCameraFrameId = 0;
            lastObservedByteLength = 0;
        }

        public void BeginRun(
            string message,
            EncodingPipelineMode pipelineMode,
            VideoEncoderBackend backend,
            bool allowDebugImageDuringVideo)
        {
            ResetState();
            isRunning = true;
            runRevision++;
            startedAtUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            originalPipelineMode = pipelineMode;
            originalVideoEncoderBackend = backend;
            originalAllowDebugImageDuringVideo = allowDebugImageDuringVideo;
            SetPhase(SoDrivenEncodingSwitchTestPhase.WaitingInitialDelay, message);
        }

        public void SetPhase(SoDrivenEncodingSwitchTestPhase newPhase, string message)
        {
            phase = newPhase;
            statusMessage = message ?? string.Empty;
        }

        public void SetBlocker(string blocker)
        {
            lastBlocker = blocker ?? string.Empty;
        }

        public void RecordStartRequest(int requestRevision)
        {
            startRequestRevision = requestRevision;
        }

        public void RecordStopRequest(int requestRevision)
        {
            stopRequestRevision = requestRevision;
        }

        public void RecordEncodedObservation(EncodedFrameRecordView record, bool dualMode)
        {
            lastObservedCodec = record.codec ?? string.Empty;
            lastObservedEncodedFrameId = record.encodedFrameId;
            lastObservedSourceCameraFrameId = record.sourceCameraFrameId;
            lastObservedByteLength = record.byteLength;

            if (record.codec == "DEBUG_JPEG")
            {
                observedDebugImageFrames++;
                if (dualMode)
                {
                    observedDebugImageInDualMode = true;
                }
            }
            else if (record.codec == "DEBUG_MJPEG")
            {
                observedMjpegVideoFrames++;
                if (dualMode)
                {
                    observedMjpegVideoInDualMode = true;
                }
            }
        }

        public void Complete(string reason)
        {
            isRunning = false;
            isComplete = true;
            hasFailure = false;
            phase = SoDrivenEncodingSwitchTestPhase.Completed;
            stopReason = reason ?? string.Empty;
            statusMessage = stopReason;
            completedAtUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        public void Fail(string reason)
        {
            isRunning = false;
            isComplete = false;
            hasFailure = true;
            phase = SoDrivenEncodingSwitchTestPhase.Failed;
            stopReason = reason ?? string.Empty;
            statusMessage = stopReason;
            completedAtUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }

    [System.Serializable]
    public struct EncodedFrameRecordView
    {
        public long encodedFrameId;
        public long sourceCameraFrameId;
        public string codec;
        public int byteLength;
    }
}
