using System.Collections;
using DataCapture.Diagnostics;
using DataCapture.Networking;
using DataCapture.Synchronization;
using SObasic.CurrentQueueBridge;
using UnityEngine;

namespace DataCapture.Testing
{
    [DisallowMultipleComponent]
    public class DataCaptureSoDrivenEncodingSwitchTest : MonoBehaviour
    {
        [SerializeField] private bool autoRunOnEnable;
        [SerializeField] private float startDelaySeconds = 1.5f;
        [SerializeField] private bool requestDiscovery = true;
        [SerializeField] private bool requirePcHandshake = true;
        [SerializeField] private float pcPairingTimeoutSeconds = 8f;
        [SerializeField] private bool normalizeRecordingStateOnStart = true;
        [SerializeField] private float recordingStateTimeoutSeconds = 8f;
        [SerializeField] private float mergeLayerTimeoutSeconds = 12f;
        [SerializeField] private float encodingTimeoutSeconds = 10f;
        [SerializeField] private bool restorePipelineOnComplete = true;
        [SerializeField] private SoDrivenEncodingSwitchTestRequestSO testRequest;
        [SerializeField] private bool consumeTestRequestAfterStart = true;
        [SerializeField] private PCDiscoveryRequestSO discoveryRequest;
        [SerializeField] private PCReceiverConnectionStatusSO pcReceiverStatus;
        [SerializeField] private RecordingToggleRequestSO recordingToggleRequest;
        [SerializeField] private RecordingSessionStateSO recordingState;
        [SerializeField] private TimestampMergerDebugStateSO mergerDebugState;
        [SerializeField] private MergedFrameSnapshotQueueSO mergedQueue;
        [SerializeField] private EncodingPipelineConfigurationSO pipelineConfiguration;
        [SerializeField] private CurrentEncodedFrameSO currentEncodedFrame;
        [SerializeField] private EncodedFrameQueueSO encodedFrameQueue;
        [SerializeField] private SoDrivenEncodingSwitchTestStateSO testState;

        private Coroutine activeFlow;
        private int lastHandledTestRequestRevision;
        private bool lastTestRequestedState;
        private EncodingPipelineMode originalPipelineMode;
        private VideoEncoderBackend originalBackend;
        private bool originalAllowDebugImageDuringVideo;
        private bool hasOriginalPipeline;

        private void OnEnable()
        {
            if (testRequest != null)
            {
                lastHandledTestRequestRevision = testRequest.requestRevision;
            }

            lastTestRequestedState = false;

            if (autoRunOnEnable && activeFlow == null)
            {
                activeFlow = StartCoroutine(RunTestFlow());
            }
        }

        private void Update()
        {
            HandleTestRequestSO();
        }

        [ContextMenu("Run SO Driven Encoding Switch Test")]
        public void RunFromContextMenu()
        {
            if (activeFlow == null)
            {
                activeFlow = StartCoroutine(RunTestFlow());
            }
        }

        private void HandleTestRequestSO()
        {
            if (testRequest == null)
            {
                return;
            }

            bool revisionChanged = testRequest.requestRevision != lastHandledTestRequestRevision;
            bool manualBoolRaised = testRequest.requested && !lastTestRequestedState;
            lastTestRequestedState = testRequest.requested;

            if (!revisionChanged && !manualBoolRaised)
            {
                return;
            }

            lastHandledTestRequestRevision = testRequest.requestRevision;

            if (activeFlow == null)
            {
                activeFlow = StartCoroutine(RunTestFlow());
            }

            if (consumeTestRequestAfterStart)
            {
                testRequest.Clear();
                lastTestRequestedState = false;
            }
        }

        private IEnumerator RunTestFlow()
        {
            if (!ValidateRequiredReferences())
            {
                yield break;
            }

            CaptureOriginalPipeline();
            testState?.BeginRun(
                "SO driven encoding switch test started.",
                originalPipelineMode,
                originalBackend,
                originalAllowDebugImageDuringVideo);

            if (startDelaySeconds > 0f)
            {
                SetPhase(SoDrivenEncodingSwitchTestPhase.WaitingInitialDelay, "Waiting before issuing SO requests.");
                yield return new WaitForSeconds(startDelaySeconds);
            }

            if (normalizeRecordingStateOnStart && recordingState != null && !recordingState.IsNotStarted)
            {
                SetPhase(SoDrivenEncodingSwitchTestPhase.NormalizingRecordingState, "Recording was already active; requesting stop through SO before starting test.");
                RequestStop("SO Driven Encoding Switch Test Normalize Stop");
                yield return WaitForRecordingNotStarted(recordingStateTimeoutSeconds);

                if (!recordingState.IsNotStarted)
                {
                    Fail("Timed out while normalizing recording state to NotStarted.");
                    yield break;
                }
            }

            if (requestDiscovery)
            {
                SetPhase(SoDrivenEncodingSwitchTestPhase.RequestingDiscovery, "Requesting LAN discovery through PCDiscoveryRequestSO.");
                discoveryRequest.Request("SO Driven Encoding Switch Test Discovery");
            }

            if (requirePcHandshake)
            {
                SetPhase(SoDrivenEncodingSwitchTestPhase.WaitingForPcHandshake, "Waiting for PCReceiverConnectionStatusSO.handshakeSucceeded.");
                float deadline = Time.unscaledTime + Mathf.Max(0f, pcPairingTimeoutSeconds);
                while (!pcReceiverStatus.handshakeSucceeded && Time.unscaledTime <= deadline)
                {
                    MarkBlocker(pcReceiverStatus.GetRecordingBlockReason());
                    yield return null;
                }

                if (!pcReceiverStatus.handshakeSucceeded)
                {
                    Fail("Timed out waiting for PC discovery handshake.");
                    yield break;
                }
            }

            SetPhase(SoDrivenEncodingSwitchTestPhase.RequestingRecordingStart, "Requesting recording start through RecordingToggleRequestSO.");
            recordingToggleRequest.Request("SO Driven Encoding Switch Test Start");
            testState?.RecordStartRequest(recordingToggleRequest.requestRevision);

            if (recordingState != null)
            {
                SetPhase(SoDrivenEncodingSwitchTestPhase.WaitingForRecordingQueues, "Waiting for RecordingSessionStateSO.ShouldWriteQueues.");
                yield return WaitForQueueWritingState(recordingStateTimeoutSeconds);

                if (!recordingState.ShouldWriteQueues)
                {
                    Fail(recordingState.HasException
                        ? recordingState.LastExceptionReason
                        : "Timed out waiting for recording warmup/queue writing state.");
                    yield break;
                }
            }

            SetPhase(SoDrivenEncodingSwitchTestPhase.WaitingForMergedFrame, "Waiting for a sendable merged frame before encoding checks.");
            yield return WaitForSendableMergedFrame(mergeLayerTimeoutSeconds);
            if (!HasSendableMergedFrame())
            {
                Fail("Timed out waiting for a sendable merged frame.");
                yield break;
            }

            yield return RunSingleCodecCheck(
                SoDrivenEncodingSwitchTestPhase.TestingDebugImageOutput,
                EncodingPipelineMode.DebugImageOnly,
                VideoEncoderBackend.DebugJpeg,
                "DEBUG_JPEG",
                false);
            if (testState != null && testState.hasFailure)
            {
                yield break;
            }

            yield return RunSingleCodecCheck(
                SoDrivenEncodingSwitchTestPhase.TestingMjpegVideoOutput,
                EncodingPipelineMode.VideoOnly,
                VideoEncoderBackend.DebugJpeg,
                "DEBUG_MJPEG",
                false);
            if (testState != null && testState.hasFailure)
            {
                yield break;
            }

            yield return RunDualOutputCheck();
            if (testState != null && testState.hasFailure)
            {
                yield break;
            }

            SetPhase(SoDrivenEncodingSwitchTestPhase.RequestingRecordingStop, "Encoding switch test passed; requesting recording stop through SO.");
            RequestStop("SO Driven Encoding Switch Test Stop After Success");
            yield return WaitForRecordingNotStarted(recordingStateTimeoutSeconds);
            RestorePipeline();
            Complete("Encoding switch test observed DEBUG_JPEG, DEBUG_MJPEG, and dual-output mode.");
        }

        private IEnumerator RunSingleCodecCheck(
            SoDrivenEncodingSwitchTestPhase phase,
            EncodingPipelineMode mode,
            VideoEncoderBackend backend,
            string expectedCodec,
            bool dualMode)
        {
            SetPhase(phase, "Switching pipeline to " + mode + " and waiting for " + expectedCodec + ".");
            long baselineSourceFrameId = GetLatestObservedSourceFrameId();
            SetPipeline(mode, backend, false);
            yield return WaitForCodec(expectedCodec, baselineSourceFrameId, dualMode);
        }

        private IEnumerator RunDualOutputCheck()
        {
            SetPhase(SoDrivenEncodingSwitchTestPhase.TestingDualOutput, "Switching pipeline to DebugImageAndVideo and waiting for DEBUG_JPEG + DEBUG_MJPEG.");
            long baselineSourceFrameId = GetLatestObservedSourceFrameId();
            SetPipeline(EncodingPipelineMode.DebugImageAndVideo, VideoEncoderBackend.DebugJpeg, false);

            bool sawDebugJpeg = false;
            bool sawMjpeg = false;
            float deadline = Time.unscaledTime + Mathf.Max(0.1f, encodingTimeoutSeconds);
            while (Time.unscaledTime <= deadline && (!sawDebugJpeg || !sawMjpeg))
            {
                if (!sawDebugJpeg && TryObserveCodec("DEBUG_JPEG", baselineSourceFrameId, out EncodedFrameRecordView debugRecord, out _))
                {
                    sawDebugJpeg = true;
                    testState?.RecordEncodedObservation(debugRecord, true);
                }

                if (!sawMjpeg && TryObserveCodec("DEBUG_MJPEG", baselineSourceFrameId, out EncodedFrameRecordView mjpegRecord, out _))
                {
                    sawMjpeg = true;
                    testState?.RecordEncodedObservation(mjpegRecord, true);
                }

                if (!sawDebugJpeg || !sawMjpeg)
                {
                    MarkBlocker("Waiting for dual output. DEBUG_JPEG=" + sawDebugJpeg + ", DEBUG_MJPEG=" + sawMjpeg + ".");
                    yield return null;
                }
            }

            if (!sawDebugJpeg || !sawMjpeg)
            {
                Fail("Timed out waiting for dual output. DEBUG_JPEG=" + sawDebugJpeg + ", DEBUG_MJPEG=" + sawMjpeg + ".");
            }
        }

        private IEnumerator WaitForCodec(string expectedCodec, long baselineSourceFrameId, bool dualMode)
        {
            float deadline = Time.unscaledTime + Mathf.Max(0.1f, encodingTimeoutSeconds);
            while (Time.unscaledTime <= deadline)
            {
                if (TryObserveCodec(expectedCodec, baselineSourceFrameId, out EncodedFrameRecordView record, out string blocker))
                {
                    testState?.RecordEncodedObservation(record, dualMode);
                    yield break;
                }

                MarkBlocker(blocker);
                yield return null;
            }

            Fail("Timed out waiting for encoded output codec " + expectedCodec + ".");
        }

        private bool TryObserveCodec(
            string expectedCodec,
            long baselineSourceFrameId,
            out EncodedFrameRecordView record,
            out string blocker)
        {
            blocker = "No encoded output observed yet for " + expectedCodec + ".";
            record = default;

            if (currentEncodedFrame != null && currentEncodedFrame.isValid)
            {
                EncodedFrameRecord current = currentEncodedFrame.ToRecord();
                if (IsMatchingCodec(current, expectedCodec, baselineSourceFrameId))
                {
                    record = ToView(current);
                    return true;
                }
            }

            if (encodedFrameQueue == null)
            {
                blocker = "EncodedFrameQueueSO is not assigned.";
                return false;
            }

            EncodedFrameRecord[] records = encodedFrameQueue.ExportSnapshot();
            for (int i = records.Length - 1; i >= 0; i--)
            {
                if (IsMatchingCodec(records[i], expectedCodec, baselineSourceFrameId))
                {
                    record = ToView(records[i]);
                    return true;
                }
            }

            blocker = "EncodedFrameQueueSO has no " + expectedCodec + " frame newer than source camera frame " + baselineSourceFrameId + ".";
            return false;
        }

        private static bool IsMatchingCodec(EncodedFrameRecord record, string expectedCodec, long baselineSourceFrameId)
        {
            return record.sourceCameraFrameId > baselineSourceFrameId &&
                string.Equals(record.codec, expectedCodec, System.StringComparison.OrdinalIgnoreCase) &&
                record.byteLength > 0;
        }

        private static EncodedFrameRecordView ToView(EncodedFrameRecord record)
        {
            return new EncodedFrameRecordView
            {
                encodedFrameId = record.encodedFrameId,
                sourceCameraFrameId = record.sourceCameraFrameId,
                codec = record.codec,
                byteLength = record.byteLength
            };
        }

        private long GetLatestObservedSourceFrameId()
        {
            long latest = currentEncodedFrame != null && currentEncodedFrame.isValid
                ? currentEncodedFrame.sourceCameraFrameId
                : 0;

            if (encodedFrameQueue == null)
            {
                return latest;
            }

            EncodedFrameRecord[] records = encodedFrameQueue.ExportSnapshot();
            for (int i = 0; i < records.Length; i++)
            {
                if (records[i].sourceCameraFrameId > latest)
                {
                    latest = records[i].sourceCameraFrameId;
                }
            }

            return latest;
        }

        private IEnumerator WaitForSendableMergedFrame(float timeoutSeconds)
        {
            float deadline = Time.unscaledTime + Mathf.Max(0f, timeoutSeconds);
            while (!HasSendableMergedFrame() && Time.unscaledTime <= deadline)
            {
                string blocker = mergerDebugState != null
                    ? mergerDebugState.latestDropReason
                    : "TimestampMergerDebugStateSO is not assigned.";
                if (string.IsNullOrWhiteSpace(blocker))
                {
                    blocker = "Waiting for sendable merged frame.";
                }

                MarkBlocker(blocker);
                yield return null;
            }
        }

        private bool HasSendableMergedFrame()
        {
            if (mergerDebugState != null && mergerDebugState.latestIsSendable)
            {
                return true;
            }

            if (mergedQueue == null)
            {
                return false;
            }

            MergedFrameSnapshotRecord[] snapshots = mergedQueue.ExportSnapshot();
            for (int i = snapshots.Length - 1; i >= 0; i--)
            {
                if (snapshots[i].isSendable)
                {
                    return true;
                }
            }

            return false;
        }

        private IEnumerator WaitForQueueWritingState(float timeoutSeconds)
        {
            float deadline = Time.unscaledTime + Mathf.Max(0f, timeoutSeconds);
            while (recordingState != null
                   && !recordingState.ShouldWriteQueues
                   && Time.unscaledTime <= deadline)
            {
                if (recordingState.IsNotStarted && recordingState.HasException)
                {
                    yield break;
                }

                yield return null;
            }
        }

        private IEnumerator WaitForRecordingNotStarted(float timeoutSeconds)
        {
            float deadline = Time.unscaledTime + Mathf.Max(0f, timeoutSeconds);
            while (recordingState != null
                   && !recordingState.IsNotStarted
                   && Time.unscaledTime <= deadline)
            {
                yield return null;
            }
        }

        private void SetPipeline(EncodingPipelineMode mode, VideoEncoderBackend backend, bool allowDebugImageDuringVideo)
        {
            pipelineConfiguration.pipelineMode = mode;
            pipelineConfiguration.videoEncoderBackend = backend;
            pipelineConfiguration.allowDebugImageDuringVideo = allowDebugImageDuringVideo;
        }

        private void CaptureOriginalPipeline()
        {
            originalPipelineMode = pipelineConfiguration.pipelineMode;
            originalBackend = pipelineConfiguration.videoEncoderBackend;
            originalAllowDebugImageDuringVideo = pipelineConfiguration.allowDebugImageDuringVideo;
            hasOriginalPipeline = true;
        }

        private void RestorePipeline()
        {
            if (!restorePipelineOnComplete || !hasOriginalPipeline || pipelineConfiguration == null)
            {
                return;
            }

            SetPhase(SoDrivenEncodingSwitchTestPhase.RestoringPipeline, "Restoring original encoding pipeline configuration.");
            SetPipeline(originalPipelineMode, originalBackend, originalAllowDebugImageDuringVideo);
        }

        private void RequestStop(string source)
        {
            if (recordingToggleRequest == null)
            {
                return;
            }

            if (recordingState != null && recordingState.IsNotStarted)
            {
                return;
            }

            recordingToggleRequest.Request(source);
            testState?.RecordStopRequest(recordingToggleRequest.requestRevision);
        }

        private bool ValidateRequiredReferences()
        {
            if (testRequest == null)
            {
                FailWithoutState("SO driven encoding switch test is missing request SO.");
                return false;
            }

            if (testState == null)
            {
                Debug.LogWarning("SO driven encoding switch test is missing state SO.", this);
            }

            if (pipelineConfiguration == null)
            {
                Fail("SO driven encoding switch test is missing EncodingPipelineConfigurationSO.");
                return false;
            }

            if (recordingToggleRequest == null)
            {
                Fail("SO driven encoding switch test is missing RecordingToggleRequestSO.");
                return false;
            }

            if (requestDiscovery && discoveryRequest == null)
            {
                Fail("SO driven encoding switch test is configured to request discovery, but PCDiscoveryRequestSO is missing.");
                return false;
            }

            if (requirePcHandshake && pcReceiverStatus == null)
            {
                Fail("SO driven encoding switch test requires PC handshake, but PCReceiverConnectionStatusSO is missing.");
                return false;
            }

            if (currentEncodedFrame == null && encodedFrameQueue == null)
            {
                Fail("SO driven encoding switch test needs CurrentEncodedFrameSO or EncodedFrameQueueSO.");
                return false;
            }

            return true;
        }

        private void SetPhase(SoDrivenEncodingSwitchTestPhase phase, string message)
        {
            testState?.SetPhase(phase, message);
        }

        private void MarkBlocker(string blocker)
        {
            testState?.SetBlocker(blocker);
        }

        private void Complete(string reason)
        {
            RestorePipeline();
            testState?.Complete(reason);
            Debug.Log(reason, this);
            activeFlow = null;
        }

        private void Fail(string reason)
        {
            RestorePipeline();
            testState?.Fail(reason);
            Debug.LogWarning(reason, this);
            RequestStop("SO Driven Encoding Switch Test Stop After Failure");
            activeFlow = null;
        }

        private void FailWithoutState(string reason)
        {
            Debug.LogWarning(reason, this);
            activeFlow = null;
        }
    }
}
