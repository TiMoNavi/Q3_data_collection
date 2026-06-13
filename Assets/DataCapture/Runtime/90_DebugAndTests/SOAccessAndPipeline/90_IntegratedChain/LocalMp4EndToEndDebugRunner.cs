using System;
using System.Collections;
using System.IO;
using DataCapture.Networking;
using DataCapture.Synchronization;
using SObasic;
using SObasic.CurrentQueueBridge;
using UnityEngine;

namespace DataCapture.Testing
{
    public sealed class LocalMp4EndToEndDebugRunner : MonoBehaviour
    {
        [Header("Run Control")]
        [SerializeField] private bool runOnStart;
        [SerializeField] private bool resetRuntimeStateBeforeRun = true;
        [SerializeField] private bool forceLocalMp4ModeBeforeRun = true;
        [SerializeField] private float startDelaySeconds = 1.5f;
        [SerializeField] private float recordingDurationSeconds = 8f;
        [SerializeField] private float pollIntervalSeconds = 0.25f;

        [Header("Timeouts")]
        [SerializeField] private float recordingStartTimeoutSeconds = 6f;
        [SerializeField] private float currentInputsTimeoutSeconds = 8f;
        [SerializeField] private float queueTimeoutSeconds = 8f;
        [SerializeField] private float synchronizationTimeoutSeconds = 8f;
        [SerializeField] private float videoInputTimeoutSeconds = 8f;
        [SerializeField] private float mp4WriterStartTimeoutSeconds = 8f;
        [SerializeField] private float recordingStopTimeoutSeconds = 5f;
        [SerializeField] private float mp4FinalizeTimeoutSeconds = 20f;
        [SerializeField] private float productAssemblyTimeoutSeconds = 4f;

        [Header("Runtime State Reset")]
        [SerializeField] private SORuntimeStateResetter runtimeStateResetter;

        [Header("Mode / Route SOs")]
        [SerializeField] private SessionModeSO sessionMode;
        [SerializeField] private NetworkSenderConfigurationSO networkConfiguration;
        [SerializeField] private EncodingPipelineConfigurationSO encodingConfiguration;

        [Header("00 Session SOs")]
        [SerializeField] private RecordingToggleRequestSO recordingToggleRequest;
        [SerializeField] private RecordingSessionStateSO recordingState;

        [Header("01-03 Runtime Chain Layers")]
        [SerializeField] private CurrentSOInputsDebugLayer currentLayer = new CurrentSOInputsDebugLayer();
        [SerializeField] private QueueBuffersDebugLayer queueLayer = new QueueBuffersDebugLayer();
        [SerializeField] private SynchronizationDebugLayer synchronizationLayer = new SynchronizationDebugLayer();

        [Header("04 Local MP4 SOs")]
        [SerializeField] private CurrentVideoFrameInputSO currentVideoFrameInput;
        [SerializeField] private Mp4ArtifactWriterStateSO mp4ArtifactWriterState;
        [SerializeField] private SingleEncodeOutputQueueSO singleEncodeOutputQueue;
        [SerializeField] private MetadataTimelineJournalSO metadataTimelineJournal;
        [SerializeField] private FrameIndexSO frameIndex;

        [Header("05 Product Assembly")]
        [SerializeField] private SessionArtifactManifestBuilder manifestBuilder;
        [SerializeField] private SessionFinalizeController finalizeController;
        [SerializeField] private SessionArtifactManifestSO sessionArtifactManifest;
        [SerializeField] private SessionFinalizeStateSO sessionFinalizeState;

        private Coroutine activeRun;

        private void Start()
        {
            if (runOnStart)
            {
                activeRun = StartCoroutine(Run());
            }
        }

        private void OnDisable()
        {
            if (activeRun == null)
            {
                return;
            }

            StopCoroutine(activeRun);
            activeRun = null;
        }

        [ContextMenu("Run Local MP4 00-05 Debug")]
        public void RunFromContextMenu()
        {
            if (activeRun != null)
            {
                StopCoroutine(activeRun);
            }

            activeRun = StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            SODebugLog.Pass(this, "LocalMp4E2E.Start", BuildFields(
                "runOnStart=" + SODebugLog.Bool(runOnStart),
                "recordingDurationSeconds=" + recordingDurationSeconds.ToString("0.0")));

            if (startDelaySeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(startDelaySeconds);
            }

            if (resetRuntimeStateBeforeRun)
            {
                ResetRuntimeStateBeforeRun();
            }

            if (!ValidateRequiredReferences(out string missingReference))
            {
                Fail("LocalMp4E2E.Setup", "Scene references", "all required SOs assigned", "missing reference", missingReference, 0f);
                activeRun = null;
                yield break;
            }

            if (forceLocalMp4ModeBeforeRun && !ApplyLocalMp4Mode(out string modeError))
            {
                Fail("LocalMp4E2E.Setup", "Local MP4 mode", "LocalOnly + LocalFile + LocalMp4Save", "write failed", modeError, 0f);
                activeRun = null;
                yield break;
            }

            ResetObservedProductState();

            if (recordingState.Active)
            {
                RequestRecordingToggle("LocalMp4E2E.NormalizeStopBeforeRun");
                yield return WaitForRecordingInactive(recordingStopTimeoutSeconds);
                if (recordingState.Active)
                {
                    Fail("LocalMp4E2E.00", "RecordingSessionState", "Active==false before run", "Active=True", "Could not stop the previous recording window.", recordingStopTimeoutSeconds);
                    activeRun = null;
                    yield break;
                }
            }

            var currentBaselines = currentLayer.CaptureBaselines();
            var queueBaselines = queueLayer.CaptureBaselines();
            var syncBaseline = synchronizationLayer.CaptureBaseline();

            RequestRecordingToggle("LocalMp4E2E.StartRecordingHarness");
            yield return WaitUntil(() => recordingState.ShouldWriteQueues || recordingState.HasException, recordingStartTimeoutSeconds);
            if (!recordingState.ShouldWriteQueues || recordingState.HasException)
            {
                Fail(
                    "LocalMp4E2E.Harness",
                    "RecordingSessionState",
                    "ShouldWriteQueues==true && HasException==false",
                    "State=" + recordingState.State + ", HasException=" + SODebugLog.Bool(recordingState.HasException),
                    recordingState.HasException ? recordingState.LastExceptionReason : "Recording request was not consumed or recording warmup did not open queue writes.",
                    recordingStartTimeoutSeconds);
                activeRun = null;
                yield break;
            }

            SODebugLog.Pass(this, "LocalMp4E2E.Harness", BuildFields("stage=RecordingWindowOpened"));

            bool passed = false;
            yield return currentLayer.Run(this, currentBaselines, currentInputsTimeoutSeconds, pollIntervalSeconds, result => passed = result);
            if (!passed)
            {
                yield return StopRecordingIfNeeded();
                activeRun = null;
                yield break;
            }

            yield return queueLayer.Run(this, queueBaselines, queueTimeoutSeconds, pollIntervalSeconds, result => passed = result);
            if (!passed)
            {
                yield return StopRecordingIfNeeded();
                activeRun = null;
                yield break;
            }

            yield return synchronizationLayer.Run(this, syncBaseline, synchronizationTimeoutSeconds, pollIntervalSeconds, result => passed = result);
            if (!passed)
            {
                yield return StopRecordingIfNeeded();
                activeRun = null;
                yield break;
            }

            yield return WaitUntil(() => recordingState.IsRecording || recordingState.HasException, recordingStartTimeoutSeconds);
            if (!recordingState.IsRecording || recordingState.HasException)
            {
                Fail(
                    "LocalMp4E2E.03",
                    "RecordingSessionState",
                    "IsRecording==true after synchronized frame",
                    "State=" + recordingState.State + ", HasException=" + SODebugLog.Bool(recordingState.HasException),
                    recordingState.HasException ? recordingState.LastExceptionReason : "Synchronization passed, but the recording state never left warmup.",
                    recordingStartTimeoutSeconds);
                yield return StopRecordingIfNeeded();
                activeRun = null;
                yield break;
            }

            SODebugLog.Pass(this, "LocalMp4E2E.03", BuildFields("stage=RecordingStateIsRecording"));

            yield return WaitUntil(IsVideoInputReady, videoInputTimeoutSeconds);
            if (!IsVideoInputReady())
            {
                Fail(
                    "LocalMp4E2E.04.Input",
                    "CurrentVideoFrameInputSO",
                    "isValid==true && inputTexture!=null && frameRate>0 && timestampUnixMs is Stage 3 synchronized time",
                    BuildVideoInputActual(),
                    ResolveVideoInputBlocker(),
                    videoInputTimeoutSeconds);
                yield return StopRecordingIfNeeded();
                activeRun = null;
                yield break;
            }

            SODebugLog.Pass(this, "LocalMp4E2E.04.Input", BuildFields(BuildVideoInputActual()));

            yield return WaitUntil(() => mp4ArtifactWriterState.isRecording || mp4ArtifactWriterState.hasFailure, mp4WriterStartTimeoutSeconds);
            if (!mp4ArtifactWriterState.isRecording || mp4ArtifactWriterState.hasFailure)
            {
                Fail(
                    "LocalMp4E2E.04.MP4Writer",
                    "Mp4ArtifactWriterStateSO",
                    "isRecording==true && hasFailure==false",
                    BuildMp4Actual(),
                    ResolveMp4WriterBlocker("MP4 writer did not start."),
                    mp4WriterStartTimeoutSeconds);
                yield return StopRecordingIfNeeded();
                activeRun = null;
                yield break;
            }

            SODebugLog.Pass(this, "LocalMp4E2E.04.MP4Writer", BuildFields("stage=MP4WriterStarted", BuildMp4Actual()));

            yield return new WaitForSecondsRealtime(Mathf.Max(0f, recordingDurationSeconds));
            yield return StopRecordingIfNeeded();

            yield return WaitUntil(() => mp4ArtifactWriterState.finalized || mp4ArtifactWriterState.hasFailure, mp4FinalizeTimeoutSeconds);
            if (!IsMp4Finalized())
            {
                Fail(
                    "LocalMp4E2E.04.MP4Finalize",
                    "Mp4ArtifactWriterStateSO",
                    "finalized==true && byteLength>0 && sampleCount>0",
                    BuildMp4Actual(),
                    ResolveMp4WriterBlocker("MP4 writer did not finalize a usable artifact."),
                    mp4FinalizeTimeoutSeconds);
                activeRun = null;
                yield break;
            }

            SODebugLog.Pass(this, "LocalMp4E2E.04.MP4Finalize", BuildFields(BuildMp4Actual()));

            yield return WaitForProductAssembly();
            bool manifestOk = sessionArtifactManifest != null && sessionArtifactManifest.isComplete && !sessionArtifactManifest.hasFailure;
            bool finalizeOk = sessionFinalizeState != null && sessionFinalizeState.decision == SessionFinalizeDecision.Publish;

            if (!manifestOk || !finalizeOk)
            {
                Fail(
                    "LocalMp4E2E.05",
                    "SessionArtifactManifestSO + SessionFinalizeStateSO",
                    "manifest complete and finalize decision Publish",
                    BuildProductActual(),
                    ResolveProductBlocker(),
                    productAssemblyTimeoutSeconds);
                activeRun = null;
                yield break;
            }

            SODebugLog.Pass(this, "LocalMp4E2E.05", BuildFields(BuildProductActual()));
            if (!TryValidateTimeAlignment(out string timeAlignmentActual, out string timeAlignmentBlocker))
            {
                Fail(
                    "LocalMp4E2E.06.TimeAlignment",
                    "Local MP4 manifest + metadata sidecar",
                    "sampleCount == manifest.frameCount == metadata line count && metadata timestamps match manifest timebase",
                    timeAlignmentActual,
                    timeAlignmentBlocker,
                    0f);
                activeRun = null;
                yield break;
            }

            SODebugLog.Pass(this, "LocalMp4E2E.06.TimeAlignment", BuildFields(timeAlignmentActual));
            SODebugLog.Pass(this, "LocalMp4E2E.Pipeline", BuildFields(
                "result=new01-to-06 local MP4 flow passed",
                BuildMp4Actual(),
                BuildProductActual()));
            activeRun = null;
        }

        private bool ValidateRequiredReferences(out string blocker)
        {
            if (recordingToggleRequest == null)
            {
                blocker = "RecordingToggleRequestSO is not assigned.";
                return false;
            }

            if (recordingState == null)
            {
                blocker = "RecordingSessionStateSO is not assigned.";
                return false;
            }

            if (encodingConfiguration == null)
            {
                blocker = "EncodingPipelineConfigurationSO is not assigned.";
                return false;
            }

            if (currentVideoFrameInput == null)
            {
                blocker = "CurrentVideoFrameInputSO is not assigned.";
                return false;
            }

            if (mp4ArtifactWriterState == null)
            {
                blocker = "Mp4ArtifactWriterStateSO is not assigned.";
                return false;
            }

            if (sessionArtifactManifest == null)
            {
                blocker = "SessionArtifactManifestSO is not assigned.";
                return false;
            }

            if (sessionFinalizeState == null)
            {
                blocker = "SessionFinalizeStateSO is not assigned.";
                return false;
            }

            blocker = string.Empty;
            return true;
        }

        private bool ApplyLocalMp4Mode(out string error)
        {
            error = string.Empty;
            if (sessionMode != null)
            {
                sessionMode.SetMode(DataCaptureSessionMode.LocalOnly, "LocalMp4E2E");
            }

            if (networkConfiguration != null)
            {
                networkConfiguration.outputTarget = StreamOutputTarget.LocalFile;
                networkConfiguration.localSaveEnabled = true;
                networkConfiguration.sendVideo = false;
                networkConfiguration.sendMetadata = false;
            }

            if (!SObasic.SOValueAccessUtility.TryWrite(encodingConfiguration, "outputMode", CaptureVideoOutputMode.LocalMp4Save, out error))
            {
                return false;
            }

            if (!SObasic.SOValueAccessUtility.TryWrite(encodingConfiguration, "pipelineMode", EncodingPipelineMode.VideoOnly, out error))
            {
                return false;
            }

            if (!SObasic.SOValueAccessUtility.TryWrite(encodingConfiguration, "videoEncoderBackend", VideoEncoderBackend.AndroidMediaCodecH264, out error))
            {
                return false;
            }

            return SObasic.SOValueAccessUtility.TryWrite(encodingConfiguration, "allowDebugImageDuringVideo", false, out error);
        }

        private void ResetObservedProductState()
        {
            mp4ArtifactWriterState.isRecording = false;
            mp4ArtifactWriterState.finalized = false;
            mp4ArtifactWriterState.hasFailure = false;
            mp4ArtifactWriterState.sessionId = string.Empty;
            mp4ArtifactWriterState.outputPath = string.Empty;
            mp4ArtifactWriterState.metadataSidecarPath = string.Empty;
            mp4ArtifactWriterState.startedUnixMs = 0;
            mp4ArtifactWriterState.finalizedUnixMs = 0;
            mp4ArtifactWriterState.byteLength = 0;
            mp4ArtifactWriterState.sampleCount = 0;
            mp4ArtifactWriterState.lastStatus = string.Empty;
            mp4ArtifactWriterState.lastFailureReason = string.Empty;

            sessionArtifactManifest.sessionId = string.Empty;
            sessionArtifactManifest.isComplete = false;
            sessionArtifactManifest.hasFailure = false;
            sessionArtifactManifest.mp4Path = string.Empty;
            sessionArtifactManifest.metadataTimelinePath = string.Empty;
            sessionArtifactManifest.frameIndexPath = string.Empty;
            sessionArtifactManifest.manifestPath = string.Empty;
            sessionArtifactManifest.startedUnixMs = 0;
            sessionArtifactManifest.finalizedUnixMs = 0;
            sessionArtifactManifest.frameCount = 0;
            sessionArtifactManifest.accessUnitCount = 0;
            sessionArtifactManifest.byteLength = 0;
            sessionArtifactManifest.failureReason = string.Empty;

            sessionFinalizeState.decision = SessionFinalizeDecision.Pending;
            sessionFinalizeState.activeBlocker = "Local MP4 end-to-end debug run has not finalized.";
            sessionFinalizeState.lastUpdatedUnixMs = 0;
        }

        private void RequestRecordingToggle(string source)
        {
            recordingToggleRequest.Request(source);
            SODebugLog.Action(this, "LocalMp4E2E.00", "RecordingToggleRequestSO.Request", BuildFields(
                "source=" + source,
                "requestRevision=" + recordingToggleRequest.requestRevision));
        }

        private IEnumerator StopRecordingIfNeeded()
        {
            if (!recordingState.Active)
            {
                yield break;
            }

            RequestRecordingToggle("LocalMp4E2E.StopRecording");
            yield return WaitForRecordingInactive(recordingStopTimeoutSeconds);
        }

        private IEnumerator WaitForRecordingInactive(float timeoutSeconds)
        {
            yield return WaitUntil(() => !recordingState.Active, timeoutSeconds);
        }

        private IEnumerator WaitForProductAssembly()
        {
            float deadline = Time.unscaledTime + Mathf.Max(0.1f, productAssemblyTimeoutSeconds);
            while (Time.unscaledTime < deadline)
            {
                manifestBuilder?.BuildManifestIfReady();
                finalizeController?.EvaluateNow();

                if (sessionFinalizeState != null &&
                    sessionFinalizeState.decision != SessionFinalizeDecision.Pending)
                {
                    yield break;
                }

                yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, pollIntervalSeconds));
            }

            manifestBuilder?.BuildManifestIfReady();
            finalizeController?.EvaluateNow();
        }

        private IEnumerator WaitUntil(System.Func<bool> predicate, float timeoutSeconds)
        {
            float deadline = Time.unscaledTime + Mathf.Max(0.1f, timeoutSeconds);
            while (Time.unscaledTime < deadline)
            {
                if (predicate())
                {
                    yield break;
                }

                yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, pollIntervalSeconds));
            }
        }

        private bool IsVideoInputReady()
        {
            return currentVideoFrameInput != null &&
                currentVideoFrameInput.isValid &&
                currentVideoFrameInput.inputTexture != null &&
                currentVideoFrameInput.synchronizedSnapshotFrameId >= 0 &&
                currentVideoFrameInput.timestampUnixMs > 0 &&
                currentVideoFrameInput.sourceTimestampUnixMs > 0 &&
                currentVideoFrameInput.frameRate > 0 &&
                currentVideoFrameInput.outputResolution.x > 0 &&
                currentVideoFrameInput.outputResolution.y > 0;
        }

        private string ResolveVideoInputBlocker()
        {
            if (currentVideoFrameInput == null)
            {
                return "CurrentVideoFrameInputSO is not assigned.";
            }

            if (!currentVideoFrameInput.isValid)
            {
                return "VideoFrameInputResolver has not produced a valid local MP4 input.";
            }

            if (currentVideoFrameInput.inputTexture == null)
            {
                return "CurrentVideoFrameInputSO inputTexture is null.";
            }

            if (currentVideoFrameInput.synchronizedSnapshotFrameId < 0)
            {
                return "CurrentVideoFrameInputSO was populated from legacy current fallback instead of a Stage 3 MergedFrameSnapshotRecord.";
            }

            if (currentVideoFrameInput.timestampUnixMs <= 0)
            {
                return "CurrentVideoFrameInputSO timestampUnixMs is not the Stage 3 synchronized timestamp.";
            }

            if (currentVideoFrameInput.sourceTimestampUnixMs <= 0)
            {
                return "CurrentVideoFrameInputSO sourceTimestampUnixMs is missing.";
            }

            if (currentVideoFrameInput.frameRate <= 0 ||
                currentVideoFrameInput.outputResolution.x <= 0 ||
                currentVideoFrameInput.outputResolution.y <= 0)
            {
                return "CurrentVideoFrameInputSO encoding parameters are incomplete.";
            }

            return "VideoFrameInputResolver has not produced a valid local MP4 input.";
        }

        private bool IsMp4Finalized()
        {
            return mp4ArtifactWriterState != null &&
                mp4ArtifactWriterState.finalized &&
                !mp4ArtifactWriterState.hasFailure &&
                mp4ArtifactWriterState.byteLength > 0 &&
                mp4ArtifactWriterState.sampleCount > 0 &&
                !string.IsNullOrWhiteSpace(mp4ArtifactWriterState.outputPath);
        }

        private string ResolveMp4WriterBlocker(string fallback)
        {
            if (mp4ArtifactWriterState != null && !string.IsNullOrWhiteSpace(mp4ArtifactWriterState.lastFailureReason))
            {
                return mp4ArtifactWriterState.lastFailureReason;
            }

            if (mp4ArtifactWriterState != null && !string.IsNullOrWhiteSpace(mp4ArtifactWriterState.lastStatus))
            {
                return mp4ArtifactWriterState.lastStatus;
            }

            if (Application.isEditor || Application.platform != RuntimePlatform.Android)
            {
                return "Local MP4 bootstrap recorder is Android Player only; run on Quest Android Player.";
            }

            return fallback;
        }

        private string ResolveProductBlocker()
        {
            if (sessionFinalizeState != null && !string.IsNullOrWhiteSpace(sessionFinalizeState.activeBlocker))
            {
                return sessionFinalizeState.activeBlocker;
            }

            if (sessionArtifactManifest != null && !string.IsNullOrWhiteSpace(sessionArtifactManifest.failureReason))
            {
                return sessionArtifactManifest.failureReason;
            }

            SingleEncodeOutputRecord stage04Output = default;
            bool hasStage04Output = singleEncodeOutputQueue != null &&
                singleEncodeOutputQueue.TryGetLatest(out stage04Output);
            if (hasStage04Output && !stage04Output.HasCompleteMetadataTimeline)
            {
                return "Stage 04 output metadata timeline is empty.";
            }

            if (hasStage04Output && !stage04Output.HasCompleteFrameIndex)
            {
                return "Stage 04 output frame index is empty.";
            }

            if (!hasStage04Output && frameIndex != null && frameIndex.Count <= 0)
            {
                return "Frame index is empty; 40_FrameIndexWriter is not implemented/mounted.";
            }

            if (!hasStage04Output && metadataTimelineJournal != null && metadataTimelineJournal.Count <= 0)
            {
                return "Metadata timeline is empty.";
            }

            return "Stage 05 did not publish the session artifact.";
        }

        private string BuildVideoInputActual()
        {
            return "input.isValid=" + SODebugLog.Bool(currentVideoFrameInput != null && currentVideoFrameInput.isValid) +
                "; input.texture=" + SODebugLog.Empty(currentVideoFrameInput != null && currentVideoFrameInput.inputTexture != null ? currentVideoFrameInput.inputTexture.name : null) +
                "; input.sourceCameraFrameId=" + (currentVideoFrameInput != null ? currentVideoFrameInput.sourceCameraFrameId.ToString() : "null") +
                "; input.sourceTimestampUnixMs=" + (currentVideoFrameInput != null ? currentVideoFrameInput.sourceTimestampUnixMs.ToString() : "null") +
                "; input.synchronizedSnapshotFrameId=" + (currentVideoFrameInput != null ? currentVideoFrameInput.synchronizedSnapshotFrameId.ToString() : "null") +
                "; input.timestampUnixMs=" + (currentVideoFrameInput != null ? currentVideoFrameInput.timestampUnixMs.ToString() : "null") +
                "; input.outputResolution=" + (currentVideoFrameInput != null ? currentVideoFrameInput.outputResolution.x + "x" + currentVideoFrameInput.outputResolution.y : "null") +
                "; input.frameRate=" + (currentVideoFrameInput != null ? currentVideoFrameInput.frameRate.ToString() : "null") +
                "; input.codec=" + SODebugLog.Empty(currentVideoFrameInput != null ? currentVideoFrameInput.codec : null);
        }

        private string BuildMp4Actual()
        {
            return "mp4.isRecording=" + SODebugLog.Bool(mp4ArtifactWriterState != null && mp4ArtifactWriterState.isRecording) +
                "; mp4.finalized=" + SODebugLog.Bool(mp4ArtifactWriterState != null && mp4ArtifactWriterState.finalized) +
                "; mp4.hasFailure=" + SODebugLog.Bool(mp4ArtifactWriterState != null && mp4ArtifactWriterState.hasFailure) +
                "; mp4.outputPath=" + SODebugLog.Empty(mp4ArtifactWriterState != null ? mp4ArtifactWriterState.outputPath : null) +
                "; mp4.byteLength=" + (mp4ArtifactWriterState != null ? mp4ArtifactWriterState.byteLength.ToString() : "null") +
                "; mp4.sampleCount=" + (mp4ArtifactWriterState != null ? mp4ArtifactWriterState.sampleCount.ToString() : "null") +
                "; mp4.lastStatus=" + SODebugLog.Empty(mp4ArtifactWriterState != null ? mp4ArtifactWriterState.lastStatus : null) +
                "; mp4.lastFailureReason=" + SODebugLog.Empty(mp4ArtifactWriterState != null ? mp4ArtifactWriterState.lastFailureReason : null);
        }

        private string BuildProductActual()
        {
            return "manifest.isComplete=" + SODebugLog.Bool(sessionArtifactManifest != null && sessionArtifactManifest.isComplete) +
                "; manifest.hasFailure=" + SODebugLog.Bool(sessionArtifactManifest != null && sessionArtifactManifest.hasFailure) +
                "; manifest.mp4Path=" + SODebugLog.Empty(sessionArtifactManifest != null ? sessionArtifactManifest.mp4Path : null) +
                "; manifest.frameCount=" + (sessionArtifactManifest != null ? sessionArtifactManifest.frameCount.ToString() : "null") +
                "; manifest.byteLength=" + (sessionArtifactManifest != null ? sessionArtifactManifest.byteLength.ToString() : "null") +
                "; manifest.failureReason=" + SODebugLog.Empty(sessionArtifactManifest != null ? sessionArtifactManifest.failureReason : null) +
                "; finalize.decision=" + (sessionFinalizeState != null ? sessionFinalizeState.decision.ToString() : "null") +
                "; finalize.activeBlocker=" + SODebugLog.Empty(sessionFinalizeState != null ? sessionFinalizeState.activeBlocker : null) +
                "; finalize.metadataTimelineComplete=" + SODebugLog.Bool(sessionFinalizeState != null && sessionFinalizeState.metadataTimelineComplete) +
                "; finalize.frameIndexComplete=" + SODebugLog.Bool(sessionFinalizeState != null && sessionFinalizeState.frameIndexComplete) +
                "; finalize.mp4ArtifactComplete=" + SODebugLog.Bool(sessionFinalizeState != null && sessionFinalizeState.mp4ArtifactComplete) +
                "; stage04Output.count=" + (singleEncodeOutputQueue != null ? singleEncodeOutputQueue.Count.ToString() : "null") +
                "; metadataTimeline.count=" + (metadataTimelineJournal != null ? metadataTimelineJournal.Count.ToString() : "null") +
                "; frameIndex.count=" + (frameIndex != null ? frameIndex.Count.ToString() : "null");
        }

        private bool TryValidateTimeAlignment(out string actual, out string blocker)
        {
            actual = string.Empty;
            blocker = string.Empty;

            if (mp4ArtifactWriterState == null)
            {
                actual = "mp4State=null";
                blocker = "Mp4ArtifactWriterStateSO is not assigned.";
                return false;
            }

            string metadataPath = mp4ArtifactWriterState.metadataSidecarPath;
            if (string.IsNullOrWhiteSpace(metadataPath) && sessionArtifactManifest != null)
            {
                metadataPath = sessionArtifactManifest.metadataTimelinePath;
            }

            string artifactManifestPath = ResolveLocalMp4ArtifactManifestPath();
            if (string.IsNullOrWhiteSpace(metadataPath) || !File.Exists(metadataPath))
            {
                actual = BuildTimeAlignmentActual(0, 0, 0, 0, 0, metadataPath, artifactManifestPath, null);
                blocker = "Metadata sidecar JSONL is missing.";
                return false;
            }

            LocalMp4ArtifactManifestSummary artifactManifest = null;
            if (!TryReadLocalMp4ArtifactManifest(artifactManifestPath, out artifactManifest, out blocker))
            {
                actual = BuildTimeAlignmentActual(0, 0, 0, 0, 0, metadataPath, artifactManifestPath, null);
                return false;
            }

            if (!TryReadMetadataSidecar(metadataPath, out int metadataCount, out long firstTimestampUnixMs, out long lastTimestampUnixMs, out blocker))
            {
                actual = BuildTimeAlignmentActual(metadataCount, firstTimestampUnixMs, lastTimestampUnixMs, 0, 0, metadataPath, artifactManifestPath, artifactManifest);
                return false;
            }

            long expectedFrameCount = artifactManifest != null && artifactManifest.frameCount > 0
                ? artifactManifest.frameCount
                : mp4ArtifactWriterState.sampleCount;
            long sessionFrameCount = sessionArtifactManifest != null ? sessionArtifactManifest.frameCount : 0;
            long metadataSpanMs = metadataCount > 0 ? lastTimestampUnixMs - firstTimestampUnixMs : 0;
            long manifestSpanMs = artifactManifest != null ? artifactManifest.timestampEndUnixMs - artifactManifest.timestampStartUnixMs : 0;
            actual = BuildTimeAlignmentActual(metadataCount, firstTimestampUnixMs, lastTimestampUnixMs, metadataSpanMs, manifestSpanMs, metadataPath, artifactManifestPath, artifactManifest);

            if (metadataCount <= 0)
            {
                blocker = "Metadata sidecar has no frame records.";
                return false;
            }

            if (mp4ArtifactWriterState.sampleCount != expectedFrameCount)
            {
                blocker = "MP4 writer sampleCount does not match local artifact manifest frameCount.";
                return false;
            }

            if (sessionFrameCount > 0 && sessionFrameCount != expectedFrameCount)
            {
                blocker = "Session artifact manifest frameCount does not match local artifact manifest frameCount.";
                return false;
            }

            if (metadataCount != expectedFrameCount)
            {
                blocker = "Metadata sidecar line count does not match local artifact manifest frameCount.";
                return false;
            }

            if (artifactManifest == null)
            {
                blocker = "Local MP4 artifact manifest is missing.";
                return false;
            }

            if (artifactManifest.timestampStartUnixMs != firstTimestampUnixMs ||
                artifactManifest.timestampEndUnixMs != lastTimestampUnixMs)
            {
                blocker = "Metadata sidecar first/last timestamps do not match local artifact manifest timestampStart/timestampEnd.";
                return false;
            }

            if (!artifactManifest.preserveInputFrameTimestamps)
            {
                blocker = "Local MP4 artifact manifest does not preserve input frame timestamps.";
                return false;
            }

            if (artifactManifest.timebaseSource != "Stage3MergedFrameSnapshot.timestampUnixMs" ||
                artifactManifest.mp4PtsTimebase != "secondsSinceTimestampStartUnixMs")
            {
                blocker = "Local MP4 artifact manifest timebase fields are not the Stage 3 synchronized timestamp contract.";
                return false;
            }

            return true;
        }

        private string ResolveLocalMp4ArtifactManifestPath()
        {
            if (mp4ArtifactWriterState == null || string.IsNullOrWhiteSpace(mp4ArtifactWriterState.outputPath))
            {
                return string.Empty;
            }

            return Path.ChangeExtension(mp4ArtifactWriterState.outputPath, ".manifest.json");
        }

        private bool TryReadLocalMp4ArtifactManifest(
            string artifactManifestPath,
            out LocalMp4ArtifactManifestSummary manifest,
            out string blocker)
        {
            manifest = null;
            blocker = string.Empty;
            if (string.IsNullOrWhiteSpace(artifactManifestPath) || !File.Exists(artifactManifestPath))
            {
                blocker = "Local MP4 artifact manifest is missing.";
                return false;
            }

            try
            {
                manifest = JsonUtility.FromJson<LocalMp4ArtifactManifestSummary>(File.ReadAllText(artifactManifestPath));
            }
            catch (Exception ex)
            {
                blocker = "Failed to parse local MP4 artifact manifest: " + ex.Message;
                return false;
            }

            if (manifest == null)
            {
                blocker = "Local MP4 artifact manifest parsed as null.";
                return false;
            }

            return true;
        }

        private bool TryReadMetadataSidecar(
            string metadataPath,
            out int metadataCount,
            out long firstTimestampUnixMs,
            out long lastTimestampUnixMs,
            out string blocker)
        {
            metadataCount = 0;
            firstTimestampUnixMs = 0;
            lastTimestampUnixMs = 0;
            blocker = string.Empty;

            try
            {
                using (var reader = new StreamReader(metadataPath))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        var snapshot = JsonUtility.FromJson<MergedFrameSnapshotRecord>(line);
                        if (snapshot.timestampUnixMs <= 0)
                        {
                            blocker = "Metadata sidecar contains an invalid timestamp at line " + (metadataCount + 1) + ".";
                            return false;
                        }

                        if (metadataCount > 0 && snapshot.timestampUnixMs <= lastTimestampUnixMs)
                        {
                            blocker = "Metadata sidecar timestamps are not strictly increasing at line " + (metadataCount + 1) + ".";
                            return false;
                        }

                        if (metadataCount == 0)
                        {
                            firstTimestampUnixMs = snapshot.timestampUnixMs;
                        }

                        lastTimestampUnixMs = snapshot.timestampUnixMs;
                        metadataCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                blocker = "Failed to read metadata sidecar JSONL: " + ex.Message;
                return false;
            }

            return true;
        }

        private string BuildTimeAlignmentActual(
            int metadataCount,
            long firstTimestampUnixMs,
            long lastTimestampUnixMs,
            long metadataSpanMs,
            long manifestSpanMs,
            string metadataPath,
            string artifactManifestPath,
            LocalMp4ArtifactManifestSummary artifactManifest)
        {
            return "timebase.metadataCount=" + metadataCount +
                "; timebase.sampleCount=" + (mp4ArtifactWriterState != null ? mp4ArtifactWriterState.sampleCount.ToString() : "null") +
                "; timebase.sessionFrameCount=" + (sessionArtifactManifest != null ? sessionArtifactManifest.frameCount.ToString() : "null") +
                "; timebase.localManifestFrameCount=" + (artifactManifest != null ? artifactManifest.frameCount.ToString() : "null") +
                "; timebase.firstTimestampUnixMs=" + firstTimestampUnixMs +
                "; timebase.lastTimestampUnixMs=" + lastTimestampUnixMs +
                "; timebase.metadataSpanMs=" + metadataSpanMs +
                "; timebase.manifestSpanMs=" + manifestSpanMs +
                "; timebase.source=" + SODebugLog.Empty(artifactManifest != null ? artifactManifest.timebaseSource : null) +
                "; timebase.mp4Pts=" + SODebugLog.Empty(artifactManifest != null ? artifactManifest.mp4PtsTimebase : null) +
                "; timebase.preserveInputFrameTimestamps=" + SODebugLog.Bool(artifactManifest != null && artifactManifest.preserveInputFrameTimestamps) +
                "; timebase.metadataPath=" + SODebugLog.Empty(metadataPath) +
                "; timebase.localManifestPath=" + SODebugLog.Empty(artifactManifestPath);
        }

        private string BuildFields(params string[] extra)
        {
            return SODebugLog.Fields(
                "platform=" + Application.platform,
                "isEditor=" + SODebugLog.Bool(Application.isEditor),
                "sessionMode=" + (sessionMode != null ? sessionMode.mode.ToString() : "null"),
                "outputTarget=" + (networkConfiguration != null ? networkConfiguration.outputTarget.ToString() : "null"),
                "outputMode=" + encodingConfiguration.outputMode,
                "pipelineMode=" + encodingConfiguration.pipelineMode,
                "videoEncoderBackend=" + encodingConfiguration.videoEncoderBackend,
                "recordingState=" + recordingState.State,
                string.Join("; ", extra));
        }

        private void Fail(string layer, string target, string condition, string actual, string blocker, float timeoutSeconds)
        {
            SODebugLog.Fail(this, layer, target, condition, actual, blocker, timeoutSeconds, BuildFields(BuildVideoInputActual(), BuildMp4Actual(), BuildProductActual()));
        }

        private void ResetRuntimeStateBeforeRun()
        {
            if (runtimeStateResetter == null)
            {
                runtimeStateResetter = GetComponent<SORuntimeStateResetter>();
            }

            if (runtimeStateResetter == null)
            {
                SODebugLog.Fail(
                    this,
                    "LocalMp4E2E.Reset",
                    "SORuntimeStateResetter",
                    "assigned",
                    "null",
                    "No runtime state resetter is assigned; continuing without reset.",
                    0f);
                return;
            }

            runtimeStateResetter.ResetRuntimeState();
        }

        [Serializable]
        private sealed class LocalMp4ArtifactManifestSummary
        {
            public long timestampStartUnixMs;
            public long timestampEndUnixMs;
            public string timebaseSource;
            public string mp4PtsTimebase;
            public bool preserveInputFrameTimestamps;
            public int frameCount;
        }
    }
}
