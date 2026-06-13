using System.Collections;
using DataCapture.Diagnostics;
using DataCapture.Networking;
using DataCapture.Synchronization;
using SObasic.CurrentQueueBridge;
using UnityEngine;

namespace DataCapture.Testing
{
    [DisallowMultipleComponent]
    public class DataCaptureSoDrivenAutoRecordingTest : MonoBehaviour
    {
        [SerializeField] private bool autoRunOnEnable;
        [SerializeField] private float startDelaySeconds = 1.5f;
        [SerializeField] private bool requestDiscovery = true;
        [SerializeField] private bool requirePcHandshake = true;
        [SerializeField] private float pcPairingTimeoutSeconds = 8f;
        [SerializeField] private bool normalizeRecordingStateOnStart = true;
        [SerializeField] private float recordingStateTimeoutSeconds = 8f;
        [SerializeField] private float mergeLayerTimeoutSeconds = 12f;
        [SerializeField] private bool requireSendableMergedFrame = true;
        [SerializeField] private int requiredSendableFrameCount = 1;
        [SerializeField] private float autoStopAfterSeconds = 20f;
        [SerializeField] private SoDrivenMergeLayerTestRequestSO testRequest;
        [SerializeField] private bool consumeTestRequestAfterStart = true;
        [SerializeField] private PCDiscoveryRequestSO discoveryRequest;
        [SerializeField] private PCReceiverConnectionStatusSO pcReceiverStatus;
        [SerializeField] private RecordingToggleRequestSO recordingToggleRequest;
        [SerializeField] private RecordingSessionStateSO recordingState;
        [SerializeField] private TimestampMergerDebugStateSO mergerDebugState;
        [SerializeField] private MergedFrameSnapshotQueueSO mergedQueue;
        [SerializeField] private SoDrivenMergeLayerTestStateSO testState;

        private Coroutine activeFlow;
        private int lastHandledTestRequestRevision;
        private bool lastTestRequestedState;

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

        [ContextMenu("Run SO Driven Test Flow")]
        public void RunFromContextMenu()
        {
            if (activeFlow == null)
            {
                activeFlow = StartCoroutine(RunTestFlow());
            }
        }

        [ContextMenu("Request Stop Through SO")]
        public void RequestStopThroughSO()
        {
            RequestStop("SO Driven Merge Layer Test Manual Stop");
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
            MarkBegin("SO driven merge-layer test started.");

            if (startDelaySeconds > 0f)
            {
                SetPhase(SoDrivenMergeLayerTestPhase.WaitingInitialDelay, "Waiting before issuing SO requests.");
                yield return new WaitForSeconds(startDelaySeconds);
            }

            if (recordingToggleRequest == null)
            {
                Fail("SO driven merge-layer test is missing RecordingToggleRequestSO.");
                yield break;
            }

            if (requestDiscovery && discoveryRequest == null)
            {
                Fail("SO driven merge-layer test is configured to request discovery, but PCDiscoveryRequestSO is missing.");
                yield break;
            }

            if (requirePcHandshake && pcReceiverStatus == null)
            {
                Fail("SO driven merge-layer test requires PC handshake, but PCReceiverConnectionStatusSO is missing.");
                yield break;
            }

            if (normalizeRecordingStateOnStart && recordingState != null && !recordingState.IsNotStarted)
            {
                SetPhase(SoDrivenMergeLayerTestPhase.NormalizingRecordingState, "Recording was already active; requesting stop through SO before starting test.");
                RequestStop("SO Driven Merge Layer Test Normalize Stop");
                yield return WaitForRecordingNotStarted(recordingStateTimeoutSeconds);

                if (!recordingState.IsNotStarted)
                {
                    Fail("Timed out while normalizing recording state to NotStarted.");
                    yield break;
                }
            }

            if (requestDiscovery)
            {
                SetPhase(SoDrivenMergeLayerTestPhase.RequestingDiscovery, "Requesting LAN discovery through PCDiscoveryRequestSO.");
                discoveryRequest.Request("SO Driven Merge Layer Test Discovery");
            }

            if (requirePcHandshake)
            {
                SetPhase(SoDrivenMergeLayerTestPhase.WaitingForPcHandshake, "Waiting for PCReceiverConnectionStatusSO.handshakeSucceeded.");
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

            int baselineMergedCount = mergerDebugState != null ? mergerDebugState.mergedCount : 0;
            long lastObservedFrameId = 0;
            int observedSendableCount = 0;

            SetPhase(SoDrivenMergeLayerTestPhase.RequestingRecordingStart, "Requesting recording start through RecordingToggleRequestSO.");
            recordingToggleRequest.Request("SO Driven Merge Layer Test Start");
            testState?.RecordStartRequest(recordingToggleRequest.requestRevision);

            if (recordingState != null)
            {
                SetPhase(SoDrivenMergeLayerTestPhase.WaitingForRecordingQueues, "Waiting for RecordingSessionStateSO.ShouldWriteQueues.");
                yield return WaitForQueueWritingState(recordingStateTimeoutSeconds);

                if (!recordingState.ShouldWriteQueues)
                {
                    Fail(recordingState.HasException
                        ? recordingState.LastExceptionReason
                        : "Timed out waiting for recording warmup/queue writing state.");
                    yield break;
                }
            }

            SetPhase(SoDrivenMergeLayerTestPhase.WaitingForMergedFrame, "Waiting for merge layer output.");

            float mergeDeadline = Time.unscaledTime + Mathf.Max(0f, mergeLayerTimeoutSeconds);
            float safetyDeadline = autoStopAfterSeconds > 0f
                ? Time.unscaledTime + autoStopAfterSeconds
                : float.PositiveInfinity;

            bool observedTarget = false;
            while (Time.unscaledTime <= mergeDeadline && Time.unscaledTime <= safetyDeadline)
            {
                if (TryObserveMergedOutput(
                        baselineMergedCount,
                        ref lastObservedFrameId,
                        ref observedSendableCount,
                        out string observationBlocker))
                {
                    observedTarget = true;
                    break;
                }

                MarkBlocker(observationBlocker);

                if (recordingState != null && recordingState.IsNotStarted)
                {
                    Fail(recordingState.HasException
                        ? recordingState.LastExceptionReason
                        : "Recording stopped before merge layer produced the expected output.");
                    yield break;
                }

                yield return null;
            }

            SetPhase(SoDrivenMergeLayerTestPhase.RequestingRecordingStop, "Merge-layer test reached stop condition; requesting recording stop through SO.");
            RequestStop(observedTarget
                ? "SO Driven Merge Layer Test Stop After Merge Output"
                : "SO Driven Merge Layer Test Stop After Timeout");

            if (recordingState != null)
            {
                yield return WaitForRecordingNotStarted(recordingStateTimeoutSeconds);
            }

            if (observedTarget)
            {
                Complete("Merge layer produced the expected output and recording stop was requested through SO.");
            }
            else
            {
                Fail("Timed out waiting for merge layer output.");
            }
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

        private bool TryObserveMergedOutput(
            int baselineMergedCount,
            ref long lastObservedFrameId,
            ref int observedSendableCount,
            out string blocker)
        {
            blocker = "No merge-layer output observed yet.";

            if (TryObserveMergerDebugState(baselineMergedCount, ref lastObservedFrameId, ref observedSendableCount, out blocker))
            {
                return true;
            }

            if (TryObserveMergedQueue(ref lastObservedFrameId, ref observedSendableCount, out blocker))
            {
                return true;
            }

            return false;
        }

        private bool TryObserveMergerDebugState(
            int baselineMergedCount,
            ref long lastObservedFrameId,
            ref int observedSendableCount,
            out string blocker)
        {
            blocker = "TimestampMergerDebugStateSO is not assigned.";
            if (mergerDebugState == null)
            {
                return false;
            }

            if (mergerDebugState.mergedCount <= baselineMergedCount || mergerDebugState.latestCameraFrameId <= 0)
            {
                blocker = string.IsNullOrWhiteSpace(mergerDebugState.statusMessage)
                    ? "TimestampMerger has not merged a new camera frame during this test run."
                    : mergerDebugState.statusMessage;
                return false;
            }

            bool newFrame = mergerDebugState.latestCameraFrameId != lastObservedFrameId;
            if (newFrame)
            {
                lastObservedFrameId = mergerDebugState.latestCameraFrameId;
                if (mergerDebugState.latestIsSendable)
                {
                    observedSendableCount++;
                }
            }

            testState?.RecordObservation(
                mergerDebugState.mergedCount,
                observedSendableCount,
                mergerDebugState.latestCameraFrameId,
                mergerDebugState.latestTimestampUnixMs,
                mergerDebugState.latestIsSendable,
                mergerDebugState.latestStatus,
                mergerDebugState.latestDropReason);

            if (!requireSendableMergedFrame)
            {
                return true;
            }

            if (mergerDebugState.latestIsSendable && observedSendableCount >= Mathf.Max(1, requiredSendableFrameCount))
            {
                return true;
            }

            blocker = string.IsNullOrWhiteSpace(mergerDebugState.latestDropReason)
                ? "Latest merged frame is not sendable yet."
                : mergerDebugState.latestDropReason;
            return false;
        }

        private bool TryObserveMergedQueue(
            ref long lastObservedFrameId,
            ref int observedSendableCount,
            out string blocker)
        {
            blocker = "MergedFrameSnapshotQueueSO is not assigned.";
            if (mergedQueue == null)
            {
                return false;
            }

            MergedFrameSnapshotRecord[] snapshots = mergedQueue.ExportSnapshot();
            if (snapshots == null || snapshots.Length == 0)
            {
                blocker = "MergedFrameSnapshotQueueSO is empty.";
                return false;
            }

            for (int i = snapshots.Length - 1; i >= 0; i--)
            {
                MergedFrameSnapshotRecord snapshot = snapshots[i];
                if (snapshot.frameId <= 0 || snapshot.frameId == lastObservedFrameId)
                {
                    continue;
                }

                lastObservedFrameId = snapshot.frameId;
                if (snapshot.isSendable)
                {
                    observedSendableCount++;
                }

                testState?.RecordObservation(
                    snapshots.Length,
                    observedSendableCount,
                    snapshot.frameId,
                    snapshot.timestampUnixMs,
                    snapshot.isSendable,
                    snapshot.status,
                    snapshot.dropReason);

                if (!requireSendableMergedFrame)
                {
                    return true;
                }

                if (snapshot.isSendable && observedSendableCount >= Mathf.Max(1, requiredSendableFrameCount))
                {
                    return true;
                }

                blocker = string.IsNullOrWhiteSpace(snapshot.dropReason)
                    ? "Latest queued merged snapshot is not sendable yet."
                    : snapshot.dropReason;
                return false;
            }

            blocker = "MergedFrameSnapshotQueueSO has no new frame for this test run.";
            return false;
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

        private void MarkBegin(string message)
        {
            testState?.BeginRun(message);
        }

        private void SetPhase(SoDrivenMergeLayerTestPhase phase, string message)
        {
            testState?.SetPhase(phase, message);
        }

        private void MarkBlocker(string blocker)
        {
            testState?.SetBlocker(blocker);
        }

        private void Complete(string reason)
        {
            testState?.Complete(reason);
            Debug.Log(reason, this);
            activeFlow = null;
        }

        private void Fail(string reason)
        {
            testState?.Fail(reason);
            Debug.LogWarning(reason, this);
            activeFlow = null;
        }
    }
}
