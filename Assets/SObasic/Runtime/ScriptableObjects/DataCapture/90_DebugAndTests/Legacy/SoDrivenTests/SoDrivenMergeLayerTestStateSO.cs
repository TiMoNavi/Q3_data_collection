using DataCapture.Synchronization;
using UnityEngine;

namespace DataCapture.Testing
{
    public enum SoDrivenMergeLayerTestPhase
    {
        Idle,
        WaitingInitialDelay,
        RequestingDiscovery,
        WaitingForPcHandshake,
        NormalizingRecordingState,
        RequestingRecordingStart,
        WaitingForRecordingQueues,
        WaitingForMergedFrame,
        RequestingRecordingStop,
        Completed,
        Failed
    }

    [CreateAssetMenu(fileName = "SoDrivenMergeLayerTestStateSO", menuName = "DataCapture/90 Diagnostics/SO Driven Merge Layer Test State")]
    public class SoDrivenMergeLayerTestStateSO : ScriptableObject
    {
        public bool isRunning;
        public bool isComplete;
        public bool hasFailure;
        public SoDrivenMergeLayerTestPhase phase;
        public string statusMessage = string.Empty;
        public string lastBlocker = string.Empty;
        public string stopReason = string.Empty;
        public long startedAtUnixMs;
        public long completedAtUnixMs;
        public int runRevision;
        public int startRequestRevision;
        public int stopRequestRevision;
        public int observedMergedCount;
        public int observedSendableCount;
        public long observedMergedFrameId;
        public long observedMergedTimestampUnixMs;
        public bool observedIsSendable;
        public MergedFrameStatus observedStatus;
        public string observedDropReason = string.Empty;

        [ContextMenu("Reset Test State")]
        public void ResetState()
        {
            isRunning = false;
            isComplete = false;
            hasFailure = false;
            phase = SoDrivenMergeLayerTestPhase.Idle;
            statusMessage = string.Empty;
            lastBlocker = string.Empty;
            stopReason = string.Empty;
            startedAtUnixMs = 0;
            completedAtUnixMs = 0;
            startRequestRevision = 0;
            stopRequestRevision = 0;
            observedMergedCount = 0;
            observedSendableCount = 0;
            observedMergedFrameId = 0;
            observedMergedTimestampUnixMs = 0;
            observedIsSendable = false;
            observedStatus = default;
            observedDropReason = string.Empty;
        }

        public void BeginRun(string message)
        {
            ResetState();
            isRunning = true;
            runRevision++;
            startedAtUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            SetPhase(SoDrivenMergeLayerTestPhase.WaitingInitialDelay, message);
        }

        public void SetPhase(SoDrivenMergeLayerTestPhase newPhase, string message)
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

        public void RecordObservation(
            int mergedCount,
            int sendableCount,
            long frameId,
            long timestampUnixMs,
            bool isSendable,
            MergedFrameStatus status,
            string dropReason)
        {
            observedMergedCount = mergedCount;
            observedSendableCount = sendableCount;
            observedMergedFrameId = frameId;
            observedMergedTimestampUnixMs = timestampUnixMs;
            observedIsSendable = isSendable;
            observedStatus = status;
            observedDropReason = dropReason ?? string.Empty;
        }

        public void Complete(string reason)
        {
            isRunning = false;
            isComplete = true;
            hasFailure = false;
            phase = SoDrivenMergeLayerTestPhase.Completed;
            stopReason = reason ?? string.Empty;
            statusMessage = stopReason;
            completedAtUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        public void Fail(string reason)
        {
            isRunning = false;
            isComplete = false;
            hasFailure = true;
            phase = SoDrivenMergeLayerTestPhase.Failed;
            stopReason = reason ?? string.Empty;
            statusMessage = stopReason;
            completedAtUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
}
