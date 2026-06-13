using System;
using SObasic.CurrentQueueBridge;
using UnityEngine;

namespace DataCapture.Synchronization
{
    public class TimestampMerger : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private SyncConfiguration config;
        [SerializeField] private CompositeAlignmentConfigurationSO alignmentConfiguration;
        [SerializeField] private long defaultToleranceMs = 50;
        [SerializeField] private long defaultSourceOfflineTimeoutMs = 1000;
        [SerializeField] private bool mergeLatestCameraFrameEachUpdate;

        [Header("Warmup")]
        [SerializeField] private RecordingSessionStateSO recordingState;
        [SerializeField] private bool waitForRequiredStreamsBeforeOutput = true;
        [SerializeField] private bool suppressWarmupSnapshots = true;
        [SerializeField] private bool clearMergedQueueOnWarmupComplete = true;
        [SerializeField] private bool clearInputQueuesOnWarmupComplete = true;

        [Header("Camera Input Queues")]
        [SerializeField] private CameraFrameTimingQueueSO cameraTimingQueue;
        [SerializeField] private CameraImageQueueSO cameraImageQueue;
        [SerializeField] private CameraPoseQueueSO cameraPoseQueue;
        [SerializeField] private CameraMetadataQueueSO cameraMetadataQueue;
        [SerializeField] private CameraStreamStateQueueSO cameraStreamStateQueue;

        [Header("Pose Input Queues")]
        [SerializeField] private ControllerPoseQueueSO controllerQueue;

        [Header("Virtual Layer Input Queues")]
        [SerializeField] private VirtualLayerQueueSO virtualLayerQueue;

        [Header("Output")]
        [SerializeField] private MergedFrameSnapshotQueueSO mergedQueue;

        [Header("Diagnostics")]
        [SerializeField] private bool isWarmedUp;
        [SerializeField] private long warmupCompletedAtCameraFrameId = -1;
        [SerializeField] private MergedFrameStatus latestStatus = MergedFrameStatus.WaitingForCamera;
        [SerializeField] private string latestStatusMessage = string.Empty;

        private long lastMergedCameraFrameId = -1;
        private RecordingSessionState observedRecordingState = RecordingSessionState.NotStarted;

        private long Tolerance => config != null ? config.timeTolerance : defaultToleranceMs;
        private long SourceOfflineTimeoutMs => config != null ? config.sourceOfflineTimeoutMs : defaultSourceOfflineTimeoutMs;
        private bool HasAlignmentConfig => alignmentConfiguration != null && alignmentConfiguration.HasAnyStreams;

        private void Update()
        {
            if (!mergeLatestCameraFrameEachUpdate || cameraTimingQueue == null)
            {
                return;
            }

            CameraFrameTimingRecord[] snapshot = cameraTimingQueue.ExportSnapshot();
            if (snapshot.Length == 0)
            {
                return;
            }

            CameraFrameTimingRecord latestCameraTiming = snapshot[snapshot.Length - 1];
            if (latestCameraTiming.frameId == lastMergedCameraFrameId)
            {
                return;
            }

            MergeCameraFrame(latestCameraTiming);
        }

        public bool MergeCameraFrame(CameraFrameTimingRecord cameraTiming)
        {
            SyncRecordingStateTransition();

            if (recordingState != null && recordingState.IsNotStarted)
            {
                latestStatus = MergedFrameStatus.WaitingForCamera;
                latestStatusMessage = "Recording session has not started.";
                return false;
            }

            if (mergedQueue == null || cameraTiming.timestampUnixMs <= 0)
            {
                latestStatus = cameraTiming.timestampUnixMs <= 0
                    ? MergedFrameStatus.DroppedBadTimestamp
                    : MergedFrameStatus.WaitingForCamera;
                latestStatusMessage = mergedQueue == null
                    ? "Merged queue is not assigned."
                    : "Camera timing timestamp is invalid.";
                if (mergedQueue == null && recordingState != null && !recordingState.IsNotStarted)
                {
                    recordingState.Fail("TimestampMerger failed: merged queue is not assigned.");
                }

                return false;
            }

            long targetTime = cameraTiming.timestampUnixMs;
            long tolerance = Tolerance;
            long sourceOfflineTimeoutMs = SourceOfflineTimeoutMs;
            MergedFrameStreamMask requiredMask = BuildRequiredStreamMask();
            bool trackCameraImage = ShouldTrackQueue(cameraImageQueue);
            bool trackCameraPose = ShouldTrackQueue(cameraPoseQueue);
            bool trackCameraMetadata = ShouldTrackQueue(cameraMetadataQueue);
            bool trackCameraStreamState = ShouldTrackQueue(cameraStreamStateQueue);
            bool trackController = ShouldTrackQueue(controllerQueue);
            bool trackVirtualLayer = ShouldTrackQueue(virtualLayerQueue);

            MatchResult<CameraImageFrameRecord> imageMatch = trackCameraImage ? MatchStream(
                cameraImageQueue,
                targetTime,
                tolerance,
                sourceOfflineTimeoutMs,
                IsRequired(requiredMask, MergedFrameStreamMask.CameraImage)) : MatchResult<CameraImageFrameRecord>.NotTracked();
            MatchResult<CameraPoseRecord> poseMatch = trackCameraPose ? MatchStream(
                cameraPoseQueue,
                targetTime,
                tolerance,
                sourceOfflineTimeoutMs,
                IsRequired(requiredMask, MergedFrameStreamMask.CameraPose)) : MatchResult<CameraPoseRecord>.NotTracked();
            MatchResult<CameraMetadataRecord> metadataMatch = trackCameraMetadata ? MatchStream(
                cameraMetadataQueue,
                targetTime,
                tolerance,
                sourceOfflineTimeoutMs,
                IsRequired(requiredMask, MergedFrameStreamMask.CameraMetadata)) : MatchResult<CameraMetadataRecord>.NotTracked();
            MatchResult<CameraStreamStateRecord> streamStateMatch = trackCameraStreamState ? MatchStream(
                cameraStreamStateQueue,
                targetTime,
                tolerance,
                sourceOfflineTimeoutMs,
                IsRequired(requiredMask, MergedFrameStreamMask.CameraStreamState)) : MatchResult<CameraStreamStateRecord>.NotTracked();
            MatchResult<ControllerPoseRecord> controllerMatch = trackController ? MatchStream(
                controllerQueue,
                targetTime,
                tolerance,
                sourceOfflineTimeoutMs,
                IsRequired(requiredMask, MergedFrameStreamMask.Controller)) : MatchResult<ControllerPoseRecord>.NotTracked();
            MatchResult<VirtualLayerFrameRecord> virtualLayerMatch = trackVirtualLayer ? MatchStream(
                virtualLayerQueue,
                targetTime,
                tolerance,
                sourceOfflineTimeoutMs,
                IsRequired(requiredMask, MergedFrameStreamMask.VirtualLayer)) : MatchResult<VirtualLayerFrameRecord>.NotTracked();

            bool hasCameraImage = imageMatch.HasMatch;
            bool hasCameraPose = poseMatch.HasMatch;
            bool hasCameraMetadata = metadataMatch.HasMatch;
            bool hasCameraStreamState = streamStateMatch.HasMatch;
            bool hasController = controllerMatch.HasMatch;
            bool hasVirtualLayer = virtualLayerMatch.HasMatch;
            MergedFrameStreamMask matchedMask = MergedFrameStreamMask.CameraTiming;
            matchedMask = AddIfMatched(matchedMask, MergedFrameStreamMask.CameraImage, hasCameraImage);
            matchedMask = AddIfMatched(matchedMask, MergedFrameStreamMask.CameraPose, hasCameraPose);
            matchedMask = AddIfMatched(matchedMask, MergedFrameStreamMask.CameraMetadata, hasCameraMetadata);
            matchedMask = AddIfMatched(matchedMask, MergedFrameStreamMask.CameraStreamState, hasCameraStreamState);
            matchedMask = AddIfMatched(matchedMask, MergedFrameStreamMask.Controller, hasController);
            matchedMask = AddIfMatched(matchedMask, MergedFrameStreamMask.VirtualLayer, hasVirtualLayer);

            MergedFrameStreamMask missingRequiredMask = GetMissingRequiredMask(requiredMask, matchedMask);
            MergedFrameStatus status = ResolveStatus(
                missingRequiredMask,
                imageMatch,
                poseMatch,
                metadataMatch,
                streamStateMatch,
                controllerMatch,
                virtualLayerMatch);
            string dropReason = BuildDropReason(
                status,
                imageMatch,
                poseMatch,
                metadataMatch,
                streamStateMatch,
                controllerMatch,
                virtualLayerMatch);
            bool isSendable = status == MergedFrameStatus.Complete;

            var merged = new MergedFrameSnapshotRecord
            {
                frameId = cameraTiming.frameId,
                timestampUnixMs = targetTime,
                status = status,
                isSendable = isSendable,
                dropReason = dropReason,
                requiredStreamMask = requiredMask,
                missingRequiredStreamMask = missingRequiredMask,
                matchedStreamMask = matchedMask,
                cameraTiming = cameraTiming,
                cameraImage = imageMatch.Record,
                cameraPose = poseMatch.Record,
                cameraMetadata = metadataMatch.Record,
                cameraStreamState = streamStateMatch.Record,
                controller = controllerMatch.Record,
                virtualLayer = virtualLayerMatch.Record,
                hasCameraImage = hasCameraImage,
                hasCameraPose = hasCameraPose,
                hasCameraMetadata = hasCameraMetadata,
                hasCameraStreamState = hasCameraStreamState,
                hasController = hasController,
                hasVirtualLayer = hasVirtualLayer,
                cameraImageTimeDeltaMs = imageMatch.DeltaMs,
                cameraPoseTimeDeltaMs = poseMatch.DeltaMs,
                cameraMetadataTimeDeltaMs = metadataMatch.DeltaMs,
                cameraStreamStateTimeDeltaMs = streamStateMatch.DeltaMs,
                controllerTimeDeltaMs = controllerMatch.DeltaMs,
                virtualLayerTimeDeltaMs = virtualLayerMatch.DeltaMs,
                cameraImageMatchStatus = imageMatch.MatchStatus,
                cameraPoseMatchStatus = poseMatch.MatchStatus,
                cameraMetadataMatchStatus = metadataMatch.MatchStatus,
                cameraStreamStateMatchStatus = streamStateMatch.MatchStatus,
                controllerMatchStatus = controllerMatch.MatchStatus,
                virtualLayerMatchStatus = virtualLayerMatch.MatchStatus,
                cameraImageDropReason = imageMatch.DropReason,
                cameraPoseDropReason = poseMatch.DropReason,
                cameraMetadataDropReason = metadataMatch.DropReason,
                cameraStreamStateDropReason = streamStateMatch.DropReason,
                controllerDropReason = controllerMatch.DropReason,
                virtualLayerDropReason = virtualLayerMatch.DropReason
            };

            bool completedWarmupThisFrame = false;
            if (waitForRequiredStreamsBeforeOutput && !isWarmedUp)
            {
                if (status != MergedFrameStatus.Complete)
                {
                    merged.status = MergedFrameStatus.WaitingForRequiredSources;
                    merged.isSendable = false;
                    merged.dropReason = string.IsNullOrEmpty(dropReason)
                        ? "Waiting for all required streams to match the camera timestamp."
                        : dropReason;
                    latestStatus = merged.status;
                    latestStatusMessage = merged.dropReason;
                    lastMergedCameraFrameId = cameraTiming.frameId;

                    if (suppressWarmupSnapshots)
                    {
                        return false;
                    }

                    mergedQueue.RecordData(merged);
                    return true;
                }

                isWarmedUp = true;
                completedWarmupThisFrame = true;
                warmupCompletedAtCameraFrameId = cameraTiming.frameId;
                if (recordingState != null && recordingState.IsWarmingUp)
                {
                    recordingState.StartRecording();
                    observedRecordingState = recordingState.State;
                }

                if (clearMergedQueueOnWarmupComplete)
                {
                    mergedQueue.Clear();
                }
            }

            mergedQueue.RecordData(merged);
            if (completedWarmupThisFrame && clearInputQueuesOnWarmupComplete)
            {
                ClearInputQueues();
            }

            latestStatus = merged.status;
            latestStatusMessage = string.IsNullOrEmpty(merged.dropReason)
                ? merged.status.ToString()
                : merged.dropReason;
            lastMergedCameraFrameId = cameraTiming.frameId;
            return true;
        }

        [ContextMenu("Reset Warmup State")]
        public void ResetWarmupState()
        {
            isWarmedUp = false;
            warmupCompletedAtCameraFrameId = -1;
            latestStatus = MergedFrameStatus.WaitingForCamera;
            latestStatusMessage = string.Empty;
            lastMergedCameraFrameId = -1;
        }

        [ContextMenu("Merge Latest Camera Frame")]
        public void MergeLatestCameraFrame()
        {
            if (cameraTimingQueue == null)
            {
                return;
            }

            CameraFrameTimingRecord[] snapshot = cameraTimingQueue.ExportSnapshot();
            if (snapshot.Length == 0)
            {
                return;
            }

            MergeCameraFrame(snapshot[snapshot.Length - 1]);
        }

        private void SyncRecordingStateTransition()
        {
            if (recordingState == null || recordingState.State == observedRecordingState)
            {
                return;
            }

            observedRecordingState = recordingState.State;
            if (recordingState.IsWarmingUp)
            {
                ResetWarmupState();
                latestStatus = MergedFrameStatus.WaitingForRequiredSources;
                latestStatusMessage = "Recording session is warming up.";
            }
            else if (recordingState.IsRecording)
            {
                isWarmedUp = true;
                latestStatus = MergedFrameStatus.Complete;
                latestStatusMessage = "Recording session is active.";
            }
            else
            {
                ResetWarmupState();
            }
        }

        private MergedFrameStreamMask BuildRequiredStreamMask()
        {
            MergedFrameStreamMask mask = MergedFrameStreamMask.CameraTiming;
            mask = AddIfMatched(mask, MergedFrameStreamMask.CameraImage, IsQueueRequired(cameraImageQueue, config == null || config.requireCameraImage));
            mask = AddIfMatched(mask, MergedFrameStreamMask.CameraPose, IsQueueRequired(cameraPoseQueue, config == null || config.requireCameraPose));
            mask = AddIfMatched(mask, MergedFrameStreamMask.CameraMetadata, IsQueueRequired(cameraMetadataQueue, config == null || config.requireCameraMetadata));
            mask = AddIfMatched(mask, MergedFrameStreamMask.CameraStreamState, IsQueueRequired(cameraStreamStateQueue, config == null || config.requireCameraStreamState));
            mask = AddIfMatched(mask, MergedFrameStreamMask.Controller, IsQueueRequired(controllerQueue, config == null || config.requireController));
            mask = AddIfMatched(mask, MergedFrameStreamMask.VirtualLayer, IsQueueRequired(virtualLayerQueue, false));
            return mask;
        }

        private bool IsQueueRequired(ScriptableObject queueAsset, bool fallbackRequired)
        {
            if (HasAlignmentConfig)
            {
                return alignmentConfiguration.IsRequired(queueAsset, false);
            }

            return fallbackRequired;
        }

        private bool ShouldTrackQueue(ScriptableObject queueAsset)
        {
            return HasAlignmentConfig
                ? alignmentConfiguration.IsConfigured(queueAsset)
                : queueAsset != null;
        }

        private static bool IsRequired(MergedFrameStreamMask mask, MergedFrameStreamMask stream)
        {
            return (mask & stream) != 0;
        }

        private static MergedFrameStreamMask AddIfMatched(MergedFrameStreamMask mask, MergedFrameStreamMask stream, bool shouldAdd)
        {
            return shouldAdd ? mask | stream : mask;
        }

        private static MergedFrameStreamMask GetMissingRequiredMask(MergedFrameStreamMask requiredMask, MergedFrameStreamMask matchedMask)
        {
            return requiredMask & ~matchedMask;
        }

        private static MatchResult<T> MatchStream<T>(
            IDataSource<T> queue,
            long targetTime,
            long tolerance,
            long sourceOfflineTimeoutMs,
            bool isRequired)
            where T : ITimestampedData
        {
            if (queue == null)
            {
                return isRequired
                    ? MatchResult<T>.Missing(isRequired, "Queue is not assigned.")
                    : MatchResult<T>.NotTracked();
            }

            IQueueHealth health = queue as IQueueHealth;
            if (health == null || health.Count == 0)
            {
                return MatchResult<T>.Missing(isRequired, "Queue is empty.");
            }

            if (health.OldestTimestamp > 0 && targetTime < health.OldestTimestamp - tolerance)
            {
                return MatchResult<T>.Overflow(isRequired, "Target timestamp is older than the queue coverage window.");
            }

            if (health.NewestTimestamp > 0 && targetTime - health.NewestTimestamp > sourceOfflineTimeoutMs)
            {
                return MatchResult<T>.Offline(isRequired, "Queue newest timestamp is older than source timeout.");
            }

            T nearestWithinTolerance = queue.GetDataAt(targetTime, tolerance);
            if (nearestWithinTolerance.Timestamp > 0)
            {
                return MatchResult<T>.Matched(nearestWithinTolerance, targetTime, isRequired);
            }

            T nearestAny = queue.GetDataAt(targetTime, long.MaxValue);
            long delta = nearestAny.Timestamp > 0 ? Math.Abs(nearestAny.Timestamp - targetTime) : -1;
            return MatchResult<T>.OutsideTolerance(isRequired, delta);
        }

        private static MergedFrameStatus ResolveStatus(
            MergedFrameStreamMask missingRequiredMask,
            MatchResult<CameraImageFrameRecord> image,
            MatchResult<CameraPoseRecord> pose,
            MatchResult<CameraMetadataRecord> metadata,
            MatchResult<CameraStreamStateRecord> streamState,
            MatchResult<ControllerPoseRecord> controller,
            MatchResult<VirtualLayerFrameRecord> virtualLayer)
        {
            if (HasRequiredDrop(image, DropCategory.Overflow) ||
                HasRequiredDrop(pose, DropCategory.Overflow) ||
                HasRequiredDrop(metadata, DropCategory.Overflow) ||
                HasRequiredDrop(streamState, DropCategory.Overflow) ||
                HasRequiredDrop(controller, DropCategory.Overflow) ||
                HasRequiredDrop(virtualLayer, DropCategory.Overflow))
            {
                return MergedFrameStatus.DroppedQueueOverflow;
            }

            if (HasRequiredDrop(image, DropCategory.Offline) ||
                HasRequiredDrop(pose, DropCategory.Offline) ||
                HasRequiredDrop(metadata, DropCategory.Offline) ||
                HasRequiredDrop(streamState, DropCategory.Offline) ||
                HasRequiredDrop(controller, DropCategory.Offline) ||
                HasRequiredDrop(virtualLayer, DropCategory.Offline))
            {
                return MergedFrameStatus.DroppedSourceOffline;
            }

            if (missingRequiredMask != MergedFrameStreamMask.None)
            {
                return MergedFrameStatus.DroppedMissingRequired;
            }

            return MergedFrameStatus.Complete;
        }

        private static bool HasRequiredDrop<T>(MatchResult<T> result, DropCategory category)
            where T : ITimestampedData
        {
            return result.IsRequired && result.DropCategory == category;
        }

        private static string BuildDropReason(
            MergedFrameStatus status,
            MatchResult<CameraImageFrameRecord> image,
            MatchResult<CameraPoseRecord> pose,
            MatchResult<CameraMetadataRecord> metadata,
            MatchResult<CameraStreamStateRecord> streamState,
            MatchResult<ControllerPoseRecord> controller,
            MatchResult<VirtualLayerFrameRecord> virtualLayer)
        {
            if (status == MergedFrameStatus.Complete)
            {
                return string.Empty;
            }

            string reason = AppendReason(string.Empty, "CameraImage", image);
            reason = AppendReason(reason, "CameraPose", pose);
            reason = AppendReason(reason, "CameraMetadata", metadata);
            reason = AppendReason(reason, "CameraStreamState", streamState);
            reason = AppendReason(reason, "Controller", controller);
            reason = AppendReason(reason, "VirtualLayer", virtualLayer);
            return string.IsNullOrEmpty(reason) ? status.ToString() : reason;
        }

        private static string AppendReason<T>(string current, string label, MatchResult<T> result)
            where T : ITimestampedData
        {
            if (result.DropCategory == DropCategory.None ||
                result.DropCategory == DropCategory.NotTracked)
            {
                return current;
            }

            string entry = label + ": " + result.DropReason;
            return string.IsNullOrEmpty(current) ? entry : current + "; " + entry;
        }

        private void ClearInputQueues()
        {
            cameraTimingQueue?.Clear();
            cameraImageQueue?.Clear();
            cameraPoseQueue?.Clear();
            cameraMetadataQueue?.Clear();
            cameraStreamStateQueue?.Clear();
            controllerQueue?.Clear();
            virtualLayerQueue?.Clear();
        }

        private enum DropCategory
        {
            None,
            NotTracked,
            Missing,
            Offline,
            Overflow,
            OutsideTolerance
        }

        private readonly struct MatchResult<T> where T : ITimestampedData
        {
            public readonly T Record;
            public readonly bool HasMatch;
            public readonly bool IsRequired;
            public readonly long DeltaMs;
            public readonly FrameMatchStatus MatchStatus;
            public readonly DropCategory DropCategory;
            public readonly string DropReason;

            private MatchResult(T record, bool hasMatch, bool isRequired, long deltaMs, FrameMatchStatus matchStatus, DropCategory dropCategory, string dropReason)
            {
                Record = record;
                HasMatch = hasMatch;
                IsRequired = isRequired;
                DeltaMs = deltaMs;
                MatchStatus = matchStatus;
                DropCategory = dropCategory;
                DropReason = dropReason;
            }

            public static MatchResult<T> Matched(T record, long targetTime, bool isRequired)
            {
                long delta = Math.Abs(record.Timestamp - targetTime);
                FrameMatchStatus status = delta == 0
                    ? FrameMatchStatus.Exact
                    : FrameMatchStatus.WithinTolerance;
                return new MatchResult<T>(record, true, isRequired, delta, status, DropCategory.None, string.Empty);
            }

            public static MatchResult<T> NotTracked()
            {
                return new MatchResult<T>(default, false, false, -1, FrameMatchStatus.Missing, DropCategory.NotTracked, string.Empty);
            }

            public static MatchResult<T> Missing(bool isRequired, string reason)
            {
                return new MatchResult<T>(default, false, isRequired, -1, FrameMatchStatus.Missing, DropCategory.Missing, reason);
            }

            public static MatchResult<T> Offline(bool isRequired, string reason)
            {
                return new MatchResult<T>(default, false, isRequired, -1, FrameMatchStatus.Missing, DropCategory.Offline, reason);
            }

            public static MatchResult<T> Overflow(bool isRequired, string reason)
            {
                return new MatchResult<T>(default, false, isRequired, -1, FrameMatchStatus.Missing, DropCategory.Overflow, reason);
            }

            public static MatchResult<T> OutsideTolerance(bool isRequired, long deltaMs)
            {
                string reason = deltaMs >= 0
                    ? "Nearest record is outside tolerance. DeltaMs=" + deltaMs
                    : "No timestamped record found.";
                return new MatchResult<T>(default, false, isRequired, deltaMs, FrameMatchStatus.OutsideTolerance, DropCategory.OutsideTolerance, reason);
            }
        }
    }
}
