using System;
using System.Collections;
using DataCapture.Diagnostics;
using DataCapture.Synchronization;
using SObasic.CurrentQueueBridge;
using UnityEngine;

namespace DataCapture.Testing
{
    [Serializable]
    public sealed class SynchronizationDebugLayer
    {
        // Layer resources:
        // - TimestampMergerDebugStateSO.
        // - MergedFrameSnapshotQueueSO.
        // - RecordingSessionStateSO is read only to stop on upstream recording exceptions.
        //
        // Normal:
        // - latestStatus == Complete.
        // - latestIsSendable == true.
        // - latestMissingRequiredStreamMask == None.
        // - Debug state or merged queue advances beyond baseline.
        //
        // Advancement:
        // - None. TimestampMerger must be driven by required queues.

        [SerializeField] private TimestampMergerDebugStateSO timestampMergerDebugState;
        [SerializeField] private MergedFrameSnapshotQueueSO mergedFrameSnapshotQueue;
        [SerializeField] private RecordingSessionStateSO recordingState;

        public SODebugSyncBaseline CaptureBaseline()
        {
            int mergedCount = timestampMergerDebugState != null ? timestampMergerDebugState.mergedCount : 0;
            long frameId = timestampMergerDebugState != null ? timestampMergerDebugState.latestCameraFrameId : 0;
            int queueCount = mergedFrameSnapshotQueue != null ? mergedFrameSnapshotQueue.Count : 0;
            long queueNewestTimestamp = mergedFrameSnapshotQueue != null ? mergedFrameSnapshotQueue.NewestTimestamp : 0;
            return new SODebugSyncBaseline(mergedCount, frameId, queueCount, queueNewestTimestamp);
        }

        public IEnumerator Run(
            MonoBehaviour owner,
            SODebugSyncBaseline baseline,
            float timeoutSeconds,
            float pollIntervalSeconds,
            Action<bool> complete)
        {
            float deadline = Time.unscaledTime + Mathf.Max(0.1f, timeoutSeconds);
            while (Time.unscaledTime < deadline)
            {
                if (IsHealthy(baseline, out string fields, out string blocker))
                {
                    SODebugLog.Pass(owner, "Synchronization", fields);
                    complete(true);
                    yield break;
                }

                if (recordingState != null && recordingState.HasException)
                {
                    SODebugLog.Fail(owner, "Synchronization", "RecordingSessionState", "HasException==false", "HasException=True", recordingState.LastExceptionReason, timeoutSeconds, BuildRecordingFields());
                    complete(false);
                    yield break;
                }

                yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, pollIntervalSeconds));
            }

            IsHealthy(baseline, out string finalFields, out string finalBlocker);
            SODebugLog.Fail(owner, "Synchronization", "TimestampMergerDebugState", "latestStatus==Complete && latestIsSendable==true", "not sendable", finalBlocker, timeoutSeconds, finalFields);
            complete(false);
        }

        private bool IsHealthy(SODebugSyncBaseline baseline, out string fields, out string blocker)
        {
            if (timestampMergerDebugState == null)
            {
                fields = "TimestampMergerDebugState=null";
                blocker = "TimestampMergerDebugState SO is not assigned.";
                return false;
            }

            bool statusComplete = timestampMergerDebugState.latestStatus == MergedFrameStatus.Complete;
            bool sendable = timestampMergerDebugState.latestIsSendable;
            bool noMissingRequired = timestampMergerDebugState.latestMissingRequiredStreamMask == MergedFrameStreamMask.None;
            bool debugAdvanced = timestampMergerDebugState.latestCameraFrameId > baseline.LatestCameraFrameId ||
                timestampMergerDebugState.mergedCount > baseline.MergedCount;
            bool queueAdvanced = mergedFrameSnapshotQueue != null &&
                (mergedFrameSnapshotQueue.Count > baseline.MergedQueueCount ||
                 mergedFrameSnapshotQueue.NewestTimestamp > baseline.MergedQueueNewestTimestamp);

            fields = SODebugLog.Fields(
                "latestStatus=" + timestampMergerDebugState.latestStatus,
                "latestIsSendable=" + SODebugLog.Bool(timestampMergerDebugState.latestIsSendable),
                "latestCameraFrameId=" + timestampMergerDebugState.latestCameraFrameId,
                "baselineCameraFrameId=" + baseline.LatestCameraFrameId,
                "mergedCount=" + timestampMergerDebugState.mergedCount,
                "baselineMergedCount=" + baseline.MergedCount,
                "latestRequiredStreamMask=" + timestampMergerDebugState.latestRequiredStreamMask,
                "latestMissingRequiredStreamMask=" + timestampMergerDebugState.latestMissingRequiredStreamMask,
                "latestMatchedStreamMask=" + timestampMergerDebugState.latestMatchedStreamMask,
                "latestDropReason=" + SODebugLog.Empty(timestampMergerDebugState.latestDropReason),
                "statusMessage=" + SODebugLog.Empty(timestampMergerDebugState.statusMessage),
                "mergedQueueCount=" + (mergedFrameSnapshotQueue != null ? mergedFrameSnapshotQueue.Count.ToString() : "null"),
                "baselineMergedQueueCount=" + baseline.MergedQueueCount,
                "mergedQueueNewest=" + (mergedFrameSnapshotQueue != null ? mergedFrameSnapshotQueue.NewestTimestamp.ToString() : "null"),
                "baselineMergedQueueNewest=" + baseline.MergedQueueNewestTimestamp);

            bool healthy = statusComplete && sendable && noMissingRequired && (debugAdvanced || queueAdvanced);
            if (healthy)
            {
                blocker = "TimestampMerger produced a sendable synchronized frame.";
                return true;
            }

            if (!statusComplete)
            {
                blocker = "TimestampMerger latestStatus is not Complete.";
            }
            else if (!sendable)
            {
                blocker = "TimestampMerger latestIsSendable is false.";
            }
            else if (!noMissingRequired)
            {
                blocker = "TimestampMerger is missing required streams: " + timestampMergerDebugState.latestMissingRequiredStreamMask + ".";
            }
            else
            {
                blocker = "TimestampMerger state did not advance beyond baseline.";
            }

            return false;
        }

        private string BuildRecordingFields()
        {
            return SODebugLog.Fields(
                "State=" + (recordingState != null ? recordingState.State.ToString() : "null"),
                "HasException=" + (recordingState != null ? SODebugLog.Bool(recordingState.HasException) : "null"),
                "LastExceptionReason=" + SODebugLog.Empty(recordingState != null ? recordingState.LastExceptionReason : null));
        }
    }
}
