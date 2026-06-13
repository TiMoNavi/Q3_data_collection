using DataCapture.Synchronization;
using SObasic.CurrentQueueBridge;
using UnityEngine;

namespace DataCapture.Networking
{
    public sealed class SessionFinalizeController : MonoBehaviour
    {
        [Header("Inputs")]
        [SerializeField] private RecordingSessionStateSO recordingState;
        [SerializeField] private SessionModeSO sessionMode;
        [SerializeField] private NetworkSenderConfigurationSO networkConfiguration;
        [SerializeField] private SessionArtifactManifestSO sessionArtifactManifest;
        [SerializeField] private SingleEncodeOutputQueueSO singleEncodeOutputQueue;
        [SerializeField] private MetadataTimelineJournalSO metadataTimelineJournal;
        [SerializeField] private FrameIndexSO frameIndex;
        [SerializeField] private RealtimeAlignedStreamQueueSO realtimeAlignedStreamQueue;
        [SerializeField] private EncodingHealthStateSO encodingHealthState;
        [SerializeField] private Mp4ArtifactWriterStateSO mp4ArtifactWriterState;

        [Header("Output")]
        [SerializeField] private SessionFinalizeStateSO sessionFinalizeState;

        [Header("Runtime")]
        [SerializeField] private bool evaluateOnUpdate = true;
        [SerializeField] private bool requireRealtimeStreamForNetworkMode = true;
        [SerializeField] private bool requireMp4ForLocalOrHybridMode = true;

        [Header("Diagnostics")]
        [SerializeField] private RecordingSessionState lastObservedRecordingState = RecordingSessionState.NotStarted;
        [SerializeField] private int evaluationCount;
        [SerializeField] private string lastStatus;

        private void OnEnable()
        {
            if (recordingState != null)
            {
                lastObservedRecordingState = recordingState.State;
            }
        }

        private void Update()
        {
            if (evaluateOnUpdate)
            {
                EvaluateIfRecordingStopped();
            }
        }

        [ContextMenu("Evaluate Finalize State")]
        public bool EvaluateNow()
        {
            if (!Validate(out string blocker))
            {
                lastStatus = blocker;
                return false;
            }

            ApplyFinalizeState();
            evaluationCount++;
            lastStatus = sessionFinalizeState.Active
                ? "Session finalized as publishable."
                : "Session finalized as " + sessionFinalizeState.decision + ": " + sessionFinalizeState.activeBlocker;
            return sessionFinalizeState.Active;
        }

        public bool EvaluateIfRecordingStopped()
        {
            if (recordingState == null)
            {
                lastStatus = "RecordingSessionStateSO is not assigned.";
                return false;
            }

            RecordingSessionState currentState = recordingState.State;
            bool justStopped = lastObservedRecordingState != RecordingSessionState.NotStarted &&
                currentState == RecordingSessionState.NotStarted;
            lastObservedRecordingState = currentState;

            if (!justStopped)
            {
                return false;
            }

            return EvaluateNow();
        }

        private void ApplyFinalizeState()
        {
            ResolveOutputRequirements(out bool requiresRealtimeStream, out bool requiresMp4Artifact);
            DataCaptureSessionMode resolvedSessionMode = ResolveSessionMode();
            StreamOutputTarget resolvedOutputTarget = ResolveOutputTarget();
            SingleEncodeOutputRecord stage04Output = default;
            bool hasStage04Output = singleEncodeOutputQueue != null &&
                singleEncodeOutputQueue.TryGetLatest(out stage04Output);
            bool metadataComplete = hasStage04Output
                ? stage04Output.HasCompleteMetadataTimeline
                : metadataTimelineJournal != null && metadataTimelineJournal.Count > 0;
            bool indexComplete = hasStage04Output
                ? stage04Output.HasCompleteFrameIndex
                : frameIndex != null && frameIndex.Count > 0;
            bool liveStreamComplete = realtimeAlignedStreamQueue != null && realtimeAlignedStreamQueue.Count > 0;
            bool mp4Complete = hasStage04Output
                ? stage04Output.HasCompleteVideoArtifact
                : sessionArtifactManifest != null &&
                    sessionArtifactManifest.isComplete &&
                    !string.IsNullOrWhiteSpace(sessionArtifactManifest.mp4Path);
            bool encodingHealthy = encodingHealthState == null || !encodingHealthState.hasFailure;
            bool artifactWriterHealthy = mp4ArtifactWriterState == null || !mp4ArtifactWriterState.hasFailure;
            if (hasStage04Output && !stage04Output.IsReady)
            {
                encodingHealthy = false;
            }

            bool recordingEndedNormally = recordingState != null &&
                recordingState.IsNotStarted &&
                !recordingState.HasException;

            sessionFinalizeState.sessionMode = resolvedSessionMode;
            sessionFinalizeState.outputTarget = resolvedOutputTarget;
            sessionFinalizeState.realtimeStreamRequired = requiresRealtimeStream;
            sessionFinalizeState.mp4ArtifactRequired = requiresMp4Artifact;
            sessionFinalizeState.recordingEndedNormally = recordingEndedNormally;
            sessionFinalizeState.metadataTimelineComplete = metadataComplete;
            sessionFinalizeState.frameIndexComplete = indexComplete;
            sessionFinalizeState.realtimeStreamComplete = liveStreamComplete;
            sessionFinalizeState.mp4ArtifactComplete = mp4Complete;
            sessionFinalizeState.encodingHealthy = encodingHealthy && artifactWriterHealthy;
            sessionFinalizeState.modeBlocker = string.Empty;
            sessionFinalizeState.recordingBlocker = ResolveRecordingBlocker(recordingEndedNormally);
            sessionFinalizeState.metadataTimelineBlocker = metadataComplete
                ? string.Empty
                : "Metadata timeline is empty.";
            sessionFinalizeState.frameIndexBlocker = indexComplete
                ? string.Empty
                : "Frame index is empty.";
            sessionFinalizeState.realtimeStreamBlocker =
                requiresRealtimeStream && !liveStreamComplete
                    ? "Current output target requires realtime aligned stream records, but none were produced."
                    : string.Empty;
            sessionFinalizeState.mp4ArtifactBlocker =
                requiresMp4Artifact && !mp4Complete
                    ? ResolveMp4Blocker()
                    : string.Empty;
            sessionFinalizeState.encodingBlocker = hasStage04Output &&
                !stage04Output.IsReady &&
                !string.IsNullOrWhiteSpace(stage04Output.failureReason)
                    ? stage04Output.failureReason
                    : ResolveEncodingBlocker(encodingHealthy, artifactWriterHealthy);
            sessionFinalizeState.Evaluate();

            if (sessionFinalizeState.decision != SessionFinalizeDecision.Publish)
            {
                sessionFinalizeState.activeBlocker = ResolveBlocker(
                    metadataComplete,
                    indexComplete,
                    liveStreamComplete,
                    mp4Complete,
                    encodingHealthy,
                    artifactWriterHealthy,
                    recordingEndedNormally);
            }
        }

        private string ResolveBlocker(
            bool metadataComplete,
            bool indexComplete,
            bool liveStreamComplete,
            bool mp4Complete,
            bool encodingHealthy,
            bool artifactWriterHealthy,
            bool recordingEndedNormally)
        {
            if (!recordingEndedNormally)
            {
                return sessionFinalizeState.recordingBlocker;
            }

            if (!encodingHealthy)
            {
                return sessionFinalizeState.encodingBlocker;
            }

            if (!artifactWriterHealthy)
            {
                return sessionFinalizeState.encodingBlocker;
            }

            if (!metadataComplete)
            {
                return sessionFinalizeState.metadataTimelineBlocker;
            }

            if (!indexComplete)
            {
                return sessionFinalizeState.frameIndexBlocker;
            }

            if (sessionFinalizeState.realtimeStreamRequired && !liveStreamComplete)
            {
                return sessionFinalizeState.realtimeStreamBlocker;
            }

            if (sessionFinalizeState.mp4ArtifactRequired && !mp4Complete)
            {
                return sessionFinalizeState.mp4ArtifactBlocker;
            }

            return "Session artifact is incomplete or unhealthy.";
        }

        private void ResolveOutputRequirements(out bool requiresRealtimeStream, out bool requiresMp4Artifact)
        {
            StreamOutputTarget target = ResolveOutputTarget();
            switch (target)
            {
                case StreamOutputTarget.RemoteReceiver:
                case StreamOutputTarget.SelfReceiver:
                    requiresRealtimeStream = requireRealtimeStreamForNetworkMode;
                    requiresMp4Artifact = false;
                    return;
                case StreamOutputTarget.RemoteAndLocalFile:
                case StreamOutputTarget.SelfAndLocalFile:
                    requiresRealtimeStream = requireRealtimeStreamForNetworkMode;
                    requiresMp4Artifact = requireMp4ForLocalOrHybridMode;
                    return;
                case StreamOutputTarget.LocalFile:
                default:
                    requiresRealtimeStream = false;
                    requiresMp4Artifact = requireMp4ForLocalOrHybridMode;
                    return;
            }
        }

        private DataCaptureSessionMode ResolveSessionMode()
        {
            if (sessionMode != null)
            {
                return sessionMode.mode;
            }

            return networkConfiguration != null && networkConfiguration.UsesNetwork
                ? DataCaptureSessionMode.NetworkOrHybrid
                : DataCaptureSessionMode.LocalOnly;
        }

        private StreamOutputTarget ResolveOutputTarget()
        {
            if (networkConfiguration != null)
            {
                return networkConfiguration.outputTarget;
            }

            return sessionMode != null && sessionMode.UsesNetwork
                ? StreamOutputTarget.RemoteAndLocalFile
                : StreamOutputTarget.LocalFile;
        }

        private string ResolveRecordingBlocker(bool recordingEndedNormally)
        {
            if (recordingEndedNormally)
            {
                return string.Empty;
            }

            return recordingState != null && recordingState.HasException
                ? recordingState.LastExceptionReason
                : "Recording did not end normally.";
        }

        private string ResolveEncodingBlocker(bool encodingHealthy, bool artifactWriterHealthy)
        {
            if (!encodingHealthy)
            {
                return encodingHealthState != null && !string.IsNullOrWhiteSpace(encodingHealthState.lastFailureReason)
                    ? encodingHealthState.lastFailureReason
                    : "Encoding health reports failure.";
            }

            if (!artifactWriterHealthy)
            {
                return mp4ArtifactWriterState != null && !string.IsNullOrWhiteSpace(mp4ArtifactWriterState.lastFailureReason)
                    ? mp4ArtifactWriterState.lastFailureReason
                    : "MP4 artifact writer reports failure.";
            }

            return string.Empty;
        }

        private string ResolveMp4Blocker()
        {
            if (sessionArtifactManifest == null)
            {
                return "Current output target requires MP4, but SessionArtifactManifestSO is not assigned.";
            }

            if (!string.IsNullOrWhiteSpace(sessionArtifactManifest.failureReason))
            {
                return sessionArtifactManifest.failureReason;
            }

            if (string.IsNullOrWhiteSpace(sessionArtifactManifest.mp4Path))
            {
                return "Current output target requires MP4, but no MP4 path was published.";
            }

            if (!sessionArtifactManifest.isComplete)
            {
                return "Current output target requires MP4, but the session artifact manifest is incomplete.";
            }

            return "Current output target requires a complete MP4 session artifact.";
        }

        private bool Validate(out string blocker)
        {
            if (sessionFinalizeState == null)
            {
                blocker = "SessionFinalizeStateSO is not assigned.";
                return false;
            }

            blocker = string.Empty;
            return true;
        }
    }
}
